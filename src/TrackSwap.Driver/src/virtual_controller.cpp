#include "virtual_controller.h"

#include <cstdio>
#include <cstring>

namespace
{
trackswap::control_protocol::TelemetryPose ToTelemetryPose(const vr::DriverPose_t& pose)
{
    trackswap::control_protocol::TelemetryPose result{};
    result.connected = pose.deviceIsConnected ? 1U : 0U;
    result.valid = pose.poseIsValid ? 1U : 0U;
    result.trackingResult = static_cast<std::int32_t>(pose.result);
    for (std::size_t axis = 0; axis < 3; ++axis) result.position[axis] = pose.vecPosition[axis];
    result.rotation[0] = pose.qRotation.x;
    result.rotation[1] = pose.qRotation.y;
    result.rotation[2] = pose.qRotation.z;
    result.rotation[3] = pose.qRotation.w;
    return result;
}
}

namespace trackswap
{
VirtualController::VirtualController(ControllerHand hand)
    : hand_(hand),
      serialNumber_(hand == ControllerHand::Left ? "TRKSWAP-CONTROLLER-L" : "TRKSWAP-CONTROLLER-R"),
      registeredDeviceType_("trackswap/" + serialNumber_)
{
}

const char* VirtualController::SerialNumber() const { return serialNumber_.c_str(); }

bool VirtualController::QueueSnapshot(
    bool enabled,
    std::uint8_t logicalSlot,
    const char* sourceDevicePath,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    if (revision < latestAcceptedRevision_) return false;
    if (revision == latestAcceptedRevision_ && revision != 0) return true;
    pendingEnabled_ = enabled;
    pendingLogicalSlot_ = enabled ? logicalSlot : 255;
    pendingSourceDevicePath_.fill('\0');
    if (enabled && sourceDevicePath != nullptr)
    {
        strncpy_s(pendingSourceDevicePath_.data(), pendingSourceDevicePath_.size(), sourceDevicePath, _TRUNCATE);
    }
    pendingOffset_ = offset;
    pendingRevision_ = revision;
    latestAcceptedRevision_ = revision;
    hasPendingSnapshot_ = true;
    return true;
}

void VirtualController::QueueInput(const control_protocol::ControllerInputState& input)
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    pendingInput_ = input;
    hasPendingInput_ = true;
}

control_protocol::TelemetrySnapshot VirtualController::GetTelemetry() const
{
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    return telemetry_;
}

std::uint8_t VirtualController::LogicalSlot() const { return logicalSlot_.load(); }

vr::EVRInitError VirtualController::Activate(std::uint32_t objectId)
{
    objectId_ = objectId;
    const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId_);
    const bool left = hand_ == ControllerHand::Left;
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ModelNumber_String, left ? "TrackSwap Controller Left" : "TrackSwap Controller Right");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ManufacturerName_String, "Hrenact");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_RegisteredDeviceType_String, registeredDeviceType_.c_str());
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ControllerType_String, "trackswap_controller");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_InputProfilePath_String, "{trackswap}/input/trackswap_controller_profile.json");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_RenderModelName_String,
        left ? "{indexcontroller}valve_controller_knu_1_0_left" : "{indexcontroller}valve_controller_knu_1_0_right");
    vr::VRProperties()->SetInt32Property(properties, vr::Prop_ControllerRoleHint_Int32,
        left ? vr::TrackedControllerRole_LeftHand : vr::TrackedControllerRole_RightHand);
    vr::VRProperties()->SetInt32Property(properties, vr::Prop_ControllerHandSelectionPriority_Int32, 100);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_DeviceIsWireless_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_NeverTracked_Bool, false);

    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/a/click", &primaryHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/b/click", &secondaryHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/thumbstick/x", &joystickXHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/thumbstick/y", &joystickYHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/thumbstick/click", &joystickClickHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/trigger/value", &triggerValueHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedOneSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/trigger/click", &triggerClickHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/grip/value", &gripValueHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedOneSided);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/grip/force", &gripForceHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedOneSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/grip/touch", &gripTouchHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/system/click", &menuHandle_);
    lastPose_ = pose_math::MakeInvalidPose();
    return vr::VRInitError_None;
}

void VirtualController::Deactivate()
{
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    lastPose_ = pose_math::MakeInvalidPose();
}
void VirtualController::EnterStandby() {}
void* VirtualController::GetComponent(const char*) { return nullptr; }
void VirtualController::DebugRequest(const char*, char* response, std::uint32_t size)
{
    if (response != nullptr && size > 0) response[0] = '\0';
}
vr::DriverPose_t VirtualController::GetPose() { return lastPose_; }

void VirtualController::Update()
{
    if (objectId_ == vr::k_unTrackedDeviceIndexInvalid) return;
    ApplyPending();
    ApplyInput();
    if (!activeEnabled_)
    {
        lastPose_ = pose_math::MakeInvalidPose();
        PublishTelemetry();
        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
        return;
    }
    vr::VRServerDriverHost()->GetRawTrackedDevicePoses(0.0F, rawPoses_.data(), static_cast<std::uint32_t>(rawPoses_.size()));
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid && !rawPoses_[sourceId_].bDeviceIsConnected)
    {
        sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
        searchCountdown_ = 0;
    }
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (searchCountdown_ == 0) { FindSource(); searchCountdown_ = SearchIntervalFrames; }
        else --searchCountdown_;
    }
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        lastPose_ = pose_math::MakeInvalidPose(true);
    }
    else
    {
        lastPose_ = pose_math::ApplyOffset(pose_math::ConvertPose(rawPoses_[sourceId_]), activeOffset_);
    }
    PublishTelemetry();
    vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
}

void VirtualController::ApplyPending()
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    if (!hasPendingSnapshot_) return;
    activeEnabled_ = pendingEnabled_;
    logicalSlot_.store(pendingLogicalSlot_);
    sourceDevicePath_ = pendingSourceDevicePath_;
    activeOffset_ = pendingOffset_;
    appliedRevision_ = pendingRevision_;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    searchCountdown_ = 0;
    hasPendingSnapshot_ = false;
    if (!activeEnabled_)
    {
        activeInput_ = {};
    }
}

void VirtualController::ApplyInput()
{
    {
        std::lock_guard<std::mutex> lock(pendingMutex_);
        if (hasPendingInput_)
        {
            activeInput_ = pendingInput_;
            hasPendingInput_ = false;
        }
    }
    const auto& input = activeEnabled_ ? activeInput_ : control_protocol::ControllerInputState{};
    vr::VRDriverInput()->UpdateScalarComponent(joystickXHandle_, input.joystickX, 0.0);
    vr::VRDriverInput()->UpdateScalarComponent(joystickYHandle_, input.joystickY, 0.0);
    vr::VRDriverInput()->UpdateScalarComponent(triggerValueHandle_, input.triggerValue, 0.0);
    vr::VRDriverInput()->UpdateScalarComponent(gripValueHandle_, input.gripValue, 0.0);
    vr::VRDriverInput()->UpdateScalarComponent(gripForceHandle_, input.gripValue, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(joystickClickHandle_, input.joystickClick != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(triggerClickHandle_, input.triggerClick != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(gripTouchHandle_, input.gripClick != 0 || input.gripValue > 0.0F, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(primaryHandle_, input.primaryButton != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(secondaryHandle_, input.secondaryButton != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(menuHandle_, input.menuButton != 0, 0.0);
}

bool VirtualController::DevicePathMatches(std::uint32_t index, const char* expected) const
{
    if (index == objectId_ || expected == nullptr || expected[0] == '\0') return false;
    const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(index);
    std::array<char, MaximumDevicePathBytes> registered{};
    vr::ETrackedPropertyError error = vr::TrackedProp_Success;
    vr::VRProperties()->GetStringProperty(properties, vr::Prop_RegisteredDeviceType_String, registered.data(), static_cast<std::uint32_t>(registered.size()), &error);
    if (error != vr::TrackedProp_Success || registered[0] == '\0') return false;
    if (std::strncmp(registered.data(), "/devices/", 9) == 0) return std::strcmp(expected, registered.data()) == 0;
    std::array<char, MaximumDevicePathBytes> full{};
    return sprintf_s(full.data(), full.size(), "/devices/%s", registered.data()) > 0 && std::strcmp(expected, full.data()) == 0;
}

void VirtualController::FindSource()
{
    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
    {
        if (rawPoses_[index].bDeviceIsConnected && DevicePathMatches(index, sourceDevicePath_.data()))
        {
            sourceId_ = index;
            return;
        }
    }
}

void VirtualController::PublishTelemetry()
{
    control_protocol::TelemetrySnapshot snapshot{};
    { std::lock_guard<std::mutex> lock(telemetryMutex_); snapshot.sequence = telemetry_.sequence + 1; }
    snapshot.appliedRevision = appliedRevision_;
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid)
    {
        snapshot.source = ToTelemetryPose(pose_math::ConvertPose(rawPoses_[sourceId_]));
    }
    snapshot.output = ToTelemetryPose(lastPose_);
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    telemetry_ = snapshot;
}
} // namespace trackswap
