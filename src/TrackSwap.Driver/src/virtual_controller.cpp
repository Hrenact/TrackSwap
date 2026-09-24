#include "virtual_controller.h"
#include "pose_hiding_hook.h"

#include <array>
#include <chrono>
#include <cmath>
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

vr::HmdMatrix34_t MakeLocalPoseOffset(
    float x,
    float y,
    float z,
    float rotationXDegrees)
{
    constexpr float Pi = 3.14159265358979323846F;
    const float rotationX = rotationXDegrees * Pi / 180.0F;
    const float cosine = std::cos(rotationX);
    const float sine = std::sin(rotationX);
    vr::HmdMatrix34_t result{};
    result.m[0][0] = 1.0F;
    result.m[0][3] = x;
    result.m[1][1] = cosine;
    result.m[1][2] = -sine;
    result.m[1][3] = y;
    result.m[2][1] = sine;
    result.m[2][2] = cosine;
    result.m[2][3] = z;
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
    const char* rotationSourceDevicePath,
    std::int32_t handSelectionPriority,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    if (revision < latestAcceptedRevision_) return false;
    if (revision == latestAcceptedRevision_ && revision != 0) return true;
    pendingEnabled_ = enabled;
    pendingLogicalSlot_ = enabled ? logicalSlot : 255;
    pendingSourceDevicePath_.fill('\0');
    pendingRotationSourceDevicePath_.fill('\0');
    if (enabled && sourceDevicePath != nullptr)
    {
        strncpy_s(pendingSourceDevicePath_.data(), pendingSourceDevicePath_.size(), sourceDevicePath, _TRUNCATE);
        if (rotationSourceDevicePath != nullptr)
        {
            strncpy_s(
                pendingRotationSourceDevicePath_.data(),
                pendingRotationSourceDevicePath_.size(),
                rotationSourceDevicePath,
                _TRUNCATE);
        }
    }
    pendingHandSelectionPriority_ = handSelectionPriority;
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
vr::TrackedDeviceIndex_t VirtualController::SourceDeviceId() const { return sourceId_; }
vr::TrackedDeviceIndex_t VirtualController::RotationSourceDeviceId() const { return rotationSourceId_; }

bool VirtualController::MatchesHapticComponent(vr::VRInputComponentHandle_t handle) const
{
    return hapticHandle_ != vr::k_ulInvalidInputComponentHandle && handle == hapticHandle_;
}

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
        left ? "oculus_quest2_controller_left" : "oculus_quest2_controller_right");
    // Keep the icon asset generation in the resource name. SteamVR caches named
    // device icons by resource path, so overwriting an existing PNG can leave a
    // redesigned icon visually stale even after the driver package is updated.
    const std::string iconPrefix = std::string("{trackswap}/icons/trackswap_controller_v2_") +
        (left ? "left_" : "right_");
    const auto setNamedIcon = [&](vr::ETrackedDeviceProperty property, const char* state)
    {
        const std::string path = iconPrefix + state + ".png";
        vr::VRProperties()->SetStringProperty(properties, property, path.c_str());
    };
    setNamedIcon(vr::Prop_NamedIconPathDeviceOff_String, "off");
    setNamedIcon(vr::Prop_NamedIconPathDeviceSearching_String, "searching");
    setNamedIcon(vr::Prop_NamedIconPathDeviceSearchingAlert_String, "searching_alert");
    setNamedIcon(vr::Prop_NamedIconPathDeviceReady_String, "ready");
    setNamedIcon(vr::Prop_NamedIconPathDeviceReadyAlert_String, "ready_alert");
    setNamedIcon(vr::Prop_NamedIconPathDeviceNotReady_String, "not_ready");
    setNamedIcon(vr::Prop_NamedIconPathDeviceStandby_String, "standby");
    setNamedIcon(vr::Prop_NamedIconPathDeviceAlertLow_String, "alert_low");
    setNamedIcon(vr::Prop_NamedIconPathDeviceStandbyAlert_String, "standby_alert");
    vr::VRProperties()->SetInt32Property(properties, vr::Prop_ControllerRoleHint_Int32,
        left ? vr::TrackedControllerRole_LeftHand : vr::TrackedControllerRole_RightHand);
    vr::VRProperties()->SetInt32Property(
        properties,
        vr::Prop_ControllerHandSelectionPriority_Int32,
        activeHandSelectionPriority_);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_DeviceIsWireless_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_NeverTracked_Bool, false);

    const char* primaryPath = left ? "/input/x" : "/input/a";
    const char* secondaryPath = left ? "/input/y" : "/input/b";
    const std::string primaryClickPath = std::string(primaryPath) + "/click";
    const std::string primaryTouchPath = std::string(primaryPath) + "/touch";
    const std::string secondaryClickPath = std::string(secondaryPath) + "/click";
    const std::string secondaryTouchPath = std::string(secondaryPath) + "/touch";
    vr::VRDriverInput()->CreateBooleanComponent(properties, primaryClickPath.c_str(), &primaryHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, primaryTouchPath.c_str(), &primaryTouchHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, secondaryClickPath.c_str(), &secondaryHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, secondaryTouchPath.c_str(), &secondaryTouchHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/joystick/x", &joystickXHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/joystick/y", &joystickYHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/joystick/click", &joystickClickHandle_);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/joystick/touch", &joystickTouchHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/trigger/value", &triggerValueHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedOneSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/trigger/touch", &triggerTouchHandle_);
    vr::VRDriverInput()->CreateScalarComponent(properties, "/input/grip/value", &gripValueHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedOneSided);
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/grip/touch", &gripTouchHandle_);
    if (left)
    {
        vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/system/click", &menuHandle_);
        vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/system/touch", &menuTouchHandle_);
    }
    vr::VRDriverInput()->CreateBooleanComponent(properties, "/input/thumbrest/touch", &thumbrestTouchHandle_);
    vr::VRDriverInput()->CreateHapticComponent(properties, "/output/haptic", &hapticHandle_);
    vr::VRDriverInput()->CreatePoseComponent(properties, "/pose/openxr_aim", &openXrAimPoseHandle_);
    vr::VRDriverInput()->CreatePoseComponent(properties, "/pose/openxr_grip", &openXrGripPoseHandle_);
    const float handedX = left ? 0.007F : -0.007F;
    const auto openXrAimPose = MakeLocalPoseOffset(handedX, -0.03894766F, 0.00949694F, -39.4F);
    const auto openXrGripPose = MakeLocalPoseOffset(handedX, -0.00182941F, 0.1019482F, 20.6F);
    vr::VRDriverInput()->UpdatePoseComponent(openXrAimPoseHandle_, &openXrAimPose, 0.0);
    vr::VRDriverInput()->UpdatePoseComponent(openXrGripPoseHandle_, &openXrGripPose, 0.0);
    const auto skeletonError = vr::VRDriverInput()->CreateSkeletonComponent(
        properties,
        left ? "/input/skeleton/left" : "/input/skeleton/right",
        left ? "/skeleton/hand/left" : "/skeleton/hand/right",
        "/pose/raw",
        vr::VRSkeletalTracking_Estimated,
        nullptr,
        0,
        &skeletonHandle_);
    if (skeletonError != vr::VRInputError_None)
    {
        skeletonHandle_ = vr::k_ulInvalidInputComponentHandle;
    }
    handAnimation_ = {};
    lastSkeletonUpdate_ = std::chrono::steady_clock::now();
    ApplySkeleton({});
    lastPose_ = pose_math::MakeInvalidPose();
    return vr::VRInitError_None;
}

void VirtualController::Deactivate()
{
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
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
    if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid &&
        !rawPoses_[rotationSourceId_].bDeviceIsConnected)
    {
        rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
        rotationSearchCountdown_ = 0;
    }
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (searchCountdown_ == 0) { FindSource(); searchCountdown_ = SearchIntervalFrames; }
        else --searchCountdown_;
    }
    if (rotationSourceDevicePath_[0] != '\0' &&
        rotationSourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (rotationSearchCountdown_ == 0) { FindRotationSource(); rotationSearchCountdown_ = SearchIntervalFrames; }
        else --rotationSearchCountdown_;
    }
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid)
    {
        PoseHidingHook::RestoreRawPose(sourceId_, rawPoses_[sourceId_]);
    }
    if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid && rotationSourceId_ != sourceId_)
    {
        PoseHidingHook::RestoreRawPose(rotationSourceId_, rawPoses_[rotationSourceId_]);
    }
    const bool splitSource = rotationSourceDevicePath_[0] != '\0';
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid ||
        (splitSource && rotationSourceId_ == vr::k_unTrackedDeviceIndexInvalid))
    {
        lastPose_ = pose_math::MakeInvalidPose(true);
    }
    else
    {
        vr::DriverPose_t basePose = pose_math::ConvertPose(rawPoses_[sourceId_]);
        if (splitSource)
        {
            basePose = pose_math::CombinePose(
                basePose,
                pose_math::ConvertPose(rawPoses_[rotationSourceId_]));
        }
        lastPose_ = pose_math::ApplyOffset(basePose, activeOffset_);
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
    rotationSourceDevicePath_ = pendingRotationSourceDevicePath_;
    activeOffset_ = pendingOffset_;
    if (activeHandSelectionPriority_ != pendingHandSelectionPriority_)
    {
        activeHandSelectionPriority_ = pendingHandSelectionPriority_;
        if (objectId_ != vr::k_unTrackedDeviceIndexInvalid)
        {
            const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId_);
            vr::VRProperties()->SetInt32Property(
                properties,
                vr::Prop_ControllerHandSelectionPriority_Int32,
                activeHandSelectionPriority_);
        }
    }
    appliedRevision_ = pendingRevision_;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    searchCountdown_ = 0;
    rotationSearchCountdown_ = 0;
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
    vr::VRDriverInput()->UpdateBooleanComponent(joystickClickHandle_, input.joystickClick != 0, 0.0);
    const bool joystickActive = input.joystickClick != 0 ||
        input.joystickX * input.joystickX + input.joystickY * input.joystickY > 0.0025F;
    const bool explicitTouch = input.hasExplicitTouchState != 0;
    vr::VRDriverInput()->UpdateBooleanComponent(
        joystickTouchHandle_, explicitTouch ? input.joystickTouch != 0 : joystickActive, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(
        triggerTouchHandle_,
        explicitTouch ? input.triggerTouch != 0 : input.triggerValue > 0.0F,
        0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(
        gripTouchHandle_,
        explicitTouch ? input.gripTouch != 0 : input.gripValue > 0.0F,
        0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(primaryHandle_, input.primaryButton != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(
        primaryTouchHandle_, explicitTouch ? input.primaryTouch != 0 : input.primaryButton != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(secondaryHandle_, input.secondaryButton != 0, 0.0);
    vr::VRDriverInput()->UpdateBooleanComponent(
        secondaryTouchHandle_, explicitTouch ? input.secondaryTouch != 0 : input.secondaryButton != 0, 0.0);
    if (hand_ == ControllerHand::Left)
    {
        vr::VRDriverInput()->UpdateBooleanComponent(menuHandle_, input.menuButton != 0, 0.0);
        vr::VRDriverInput()->UpdateBooleanComponent(
            menuTouchHandle_, explicitTouch ? input.menuTouch != 0 : input.menuButton != 0, 0.0);
    }
    vr::VRDriverInput()->UpdateBooleanComponent(
        thumbrestTouchHandle_, explicitTouch && input.thumbRestTouch != 0, 0.0);
    ApplySkeleton(input);
}

void VirtualController::ApplySkeleton(const control_protocol::ControllerInputState& input)
{
    const auto now = std::chrono::steady_clock::now();
    const float elapsedSeconds = std::chrono::duration<float>(now - lastSkeletonUpdate_).count();
    lastSkeletonUpdate_ = now;
    finger_animation::Advance(handAnimation_, finger_animation::ComputeTargets(input), elapsedSeconds);

    if (skeletonHandle_ == vr::k_ulInvalidInputComponentHandle)
    {
        return;
    }

    std::array<vr::VRBoneTransform_t, eBone_Count> transforms{};
    const auto role = hand_ == ControllerHand::Left
        ? vr::TrackedControllerRole_LeftHand
        : vr::TrackedControllerRole_RightHand;
    handSimulation_.ComputeSkeletonTransforms(
        role,
        { handAnimation_.curls.thumb, handAnimation_.curls.index, handAnimation_.curls.middle, handAnimation_.curls.ring, handAnimation_.curls.pinky },
        { handAnimation_.splays.thumb, handAnimation_.splays.index, handAnimation_.splays.middle, handAnimation_.splays.ring, handAnimation_.splays.pinky },
        transforms.data());
    vr::VRDriverInput()->UpdateSkeletonComponent(
        skeletonHandle_,
        vr::VRSkeletalMotionRange_WithController,
        transforms.data(),
        static_cast<std::uint32_t>(transforms.size()));
    vr::VRDriverInput()->UpdateSkeletonComponent(
        skeletonHandle_,
        vr::VRSkeletalMotionRange_WithoutController,
        transforms.data(),
        static_cast<std::uint32_t>(transforms.size()));
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

void VirtualController::FindRotationSource()
{
    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
    {
        if (rawPoses_[index].bDeviceIsConnected &&
            DevicePathMatches(index, rotationSourceDevicePath_.data()))
        {
            rotationSourceId_ = index;
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
    if (rotationSourceDevicePath_[0] == '\0')
    {
        snapshot.rotationSource = snapshot.source;
    }
    else if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid)
    {
        snapshot.rotationSource = ToTelemetryPose(
            pose_math::ConvertPose(rawPoses_[rotationSourceId_]));
    }
    snapshot.output = ToTelemetryPose(lastPose_);
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    telemetry_ = snapshot;
}
} // namespace trackswap
