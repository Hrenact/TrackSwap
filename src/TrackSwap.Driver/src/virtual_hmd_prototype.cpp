#include "virtual_hmd_prototype.h"
#include "pose_hiding_hook.h"

#include <cstdio>
#include <cstring>

namespace
{
constexpr std::uint32_t WindowWidth = 1920;
constexpr std::uint32_t WindowHeight = 1080;
constexpr std::int32_t WindowX = 0;
constexpr std::int32_t WindowY = 0;
constexpr std::uint32_t RenderWidthPerEye = 1920;
constexpr std::uint32_t RenderHeightPerEye = 1920;
constexpr float DisplayFrequencyHz = 60.0F;
constexpr float UserIpdMeters = 0.063F;
constexpr std::uint64_t UniverseId = 0x54535750ULL;

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
bool VirtualHmd::QueueSnapshot(bool enabled, std::uint8_t logicalSlot, bool manualPose, const char* sourceDevicePath,
    const char* rotationSourceDevicePath, const pose_math::RigidOffset& offset,
    const pose_smoothing::Configuration& smoothing, std::uint64_t revision)
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    if (revision < latestAcceptedRevision_) return false;
    if (revision == latestAcceptedRevision_ && revision != 0) return true;
    pendingEnabled_ = enabled;
    pendingManualPose_ = enabled && manualPose;
    pendingLogicalSlot_ = enabled ? logicalSlot : 255;
    pendingSourceDevicePath_.fill('\0');
    pendingRotationSourceDevicePath_.fill('\0');
    if (enabled && sourceDevicePath != nullptr)
    {
        strncpy_s(pendingSourceDevicePath_.data(), pendingSourceDevicePath_.size(), sourceDevicePath, _TRUNCATE);
        if (rotationSourceDevicePath != nullptr)
        {
            strncpy_s(pendingRotationSourceDevicePath_.data(), pendingRotationSourceDevicePath_.size(),
                rotationSourceDevicePath, _TRUNCATE);
        }
    }
    pendingOffset_ = offset;
    pendingSmoothing_ = smoothing;
    pendingRevision_ = revision;
    latestAcceptedRevision_ = revision;
    hasPendingSnapshot_ = true;
    return true;
}

control_protocol::TelemetrySnapshot VirtualHmd::GetTelemetry() const
{
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    return telemetry_;
}
std::uint8_t VirtualHmd::LogicalSlot() const { return logicalSlot_.load(); }
vr::TrackedDeviceIndex_t VirtualHmd::SourceDeviceId() const { return sourceId_; }
vr::TrackedDeviceIndex_t VirtualHmd::RotationSourceDeviceId() const { return rotationSourceId_; }

vr::EVRInitError VirtualHmd::Activate(std::uint32_t objectId)
{
    objectId_ = objectId;
    const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId_);
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ModelNumber_String, "TrackSwap VR Virtual HMD");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ManufacturerName_String, "Hrenact");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_RegisteredDeviceType_String, "trackswap/TRKSWAP-HMD");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_RenderModelName_String, "generic_hmd");
    vr::VRProperties()->SetUint64Property(properties, vr::Prop_CurrentUniverseId_Uint64, UniverseId);
    vr::VRProperties()->SetFloatProperty(properties, vr::Prop_UserIpdMeters_Float, UserIpdMeters);
    vr::VRProperties()->SetFloatProperty(properties, vr::Prop_DisplayFrequency_Float, DisplayFrequencyHz);
    vr::VRProperties()->SetFloatProperty(properties, vr::Prop_UserHeadToEyeDepthMeters_Float, 0.0F);
    vr::VRProperties()->SetFloatProperty(properties, vr::Prop_SecondsFromVsyncToPhotons_Float, 0.0F);
    // This HMD has no physical scanout. Keep SteamVR on its desktop/debug
    // compositor path so it never searches the user's real monitors for a
    // direct-mode headset panel.
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_IsOnDesktop_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_DisplayDebugMode_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_WillDriftInYaw_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_DeviceIsWireless_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_NeverTracked_Bool, false);
    const auto proximityError = vr::VRDriverInput()->CreateBooleanComponent(
        properties, "/proximity", &proximityHandle_);
    if (proximityError != vr::VRInputError_None)
    {
        proximityHandle_ = vr::k_ulInvalidInputComponentHandle;
        vr::VRDriverLog()->Log("TrackSwap VR virtual HMD could not create the proximity component.");
    }
    lastPose_ = pose_math::MakeInvalidPose(true);
    vr::VRDriverLog()->Log("TrackSwap VR virtual HMD activated.");
    return vr::VRInitError_None;
}

void VirtualHmd::Deactivate()
{
    poseSmoother_.Reset();
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    proximityHandle_ = vr::k_ulInvalidInputComponentHandle;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    lastPose_ = pose_math::MakeInvalidPose();
}
void VirtualHmd::EnterStandby() {}
void* VirtualHmd::GetComponent(const char* version)
{
    return version != nullptr && std::strcmp(version, vr::IVRDisplayComponent_Version) == 0
        ? static_cast<vr::IVRDisplayComponent*>(this) : nullptr;
}
void VirtualHmd::DebugRequest(const char*, char* response, std::uint32_t size)
{
    if (response != nullptr && size > 0) response[0] = '\0';
}
vr::DriverPose_t VirtualHmd::GetPose() { return lastPose_; }

void VirtualHmd::Update()
{
    if (objectId_ == vr::k_unTrackedDeviceIndexInvalid) return;
    if (proximityHandle_ != vr::k_ulInvalidInputComponentHandle)
    {
        // This headless HMD has no wear sensor. Keep it logically worn so
        // SteamVR does not enter the idle/sleep cadence used for an unworn HMD.
        vr::VRDriverInput()->UpdateBooleanComponent(proximityHandle_, true, 0.0);
    }
    ApplyPending();
    if (!activeEnabled_)
    {
        poseSmoother_.Reset();
        lastPose_ = pose_math::MakeInvalidPose(true);
        PublishTelemetry();
        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
        return;
    }
    if (activeManualPose_)
    {
        lastPose_ = pose_math::ApplyOffset(pose_math::MakeValidIdentityPose(true), activeOffset_);
        lastPose_ = poseSmoother_.Apply(lastPose_, activeSmoothing_);
        lastPose_.shouldApplyHeadModel = true;
        PublishTelemetry();
        vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
        return;
    }
    vr::VRServerDriverHost()->GetRawTrackedDevicePoses(0.0F, rawPoses_.data(), static_cast<std::uint32_t>(rawPoses_.size()));
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid && !rawPoses_[sourceId_].bDeviceIsConnected)
    {
        sourceId_ = vr::k_unTrackedDeviceIndexInvalid; searchCountdown_ = 0;
    }
    if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid && !rawPoses_[rotationSourceId_].bDeviceIsConnected)
    {
        rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid; rotationSearchCountdown_ = 0;
    }
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (searchCountdown_ == 0) { FindSource(); searchCountdown_ = SearchIntervalFrames; } else --searchCountdown_;
    }
    if (rotationSourceDevicePath_[0] != '\0' && rotationSourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (rotationSearchCountdown_ == 0) { FindRotationSource(); rotationSearchCountdown_ = SearchIntervalFrames; } else --rotationSearchCountdown_;
    }
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid) PoseHidingHook::RestoreRawPose(sourceId_, rawPoses_[sourceId_]);
    if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid && rotationSourceId_ != sourceId_)
        PoseHidingHook::RestoreRawPose(rotationSourceId_, rawPoses_[rotationSourceId_]);
    const bool split = rotationSourceDevicePath_[0] != '\0';
    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid || (split && rotationSourceId_ == vr::k_unTrackedDeviceIndexInvalid))
    {
        poseSmoother_.Reset();
        lastPose_ = pose_math::MakeInvalidPose(true);
    }
    else
    {
        vr::DriverPose_t base = pose_math::ConvertPose(rawPoses_[sourceId_]);
        if (split) base = pose_math::CombinePose(base, pose_math::ConvertPose(rawPoses_[rotationSourceId_]));
        lastPose_ = pose_math::ApplyOffset(base, activeOffset_);
        lastPose_ = poseSmoother_.Apply(lastPose_, activeSmoothing_);
        lastPose_.shouldApplyHeadModel = true;
    }
    PublishTelemetry();
    vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
}

void VirtualHmd::ApplyPending()
{
    std::lock_guard<std::mutex> lock(pendingMutex_);
    if (!hasPendingSnapshot_) return;
    activeEnabled_ = pendingEnabled_;
    activeManualPose_ = pendingManualPose_;
    logicalSlot_.store(pendingLogicalSlot_);
    sourceDevicePath_ = pendingSourceDevicePath_;
    rotationSourceDevicePath_ = pendingRotationSourceDevicePath_;
    activeOffset_ = pendingOffset_;
    activeSmoothing_ = pendingSmoothing_;
    poseSmoother_.Reset();
    appliedRevision_ = pendingRevision_;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    searchCountdown_ = 0;
    rotationSearchCountdown_ = 0;
    hasPendingSnapshot_ = false;
}

bool VirtualHmd::DevicePathMatches(std::uint32_t index, const char* expected) const
{
    if (index == objectId_ || expected == nullptr || expected[0] == '\0') return false;
    const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(index);
    std::array<char, MaximumDevicePathBytes> registered{};
    vr::ETrackedPropertyError error = vr::TrackedProp_Success;
    vr::VRProperties()->GetStringProperty(properties, vr::Prop_RegisteredDeviceType_String,
        registered.data(), static_cast<std::uint32_t>(registered.size()), &error);
    if (error != vr::TrackedProp_Success || registered[0] == '\0') return false;
    if (std::strncmp(registered.data(), "/devices/", 9) == 0) return std::strcmp(expected, registered.data()) == 0;
    std::array<char, MaximumDevicePathBytes> full{};
    return sprintf_s(full.data(), full.size(), "/devices/%s", registered.data()) > 0 &&
        std::strcmp(expected, full.data()) == 0;
}
void VirtualHmd::FindSource()
{
    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
        if (rawPoses_[index].bDeviceIsConnected && DevicePathMatches(index, sourceDevicePath_.data())) { sourceId_ = index; return; }
}
void VirtualHmd::FindRotationSource()
{
    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
        if (rawPoses_[index].bDeviceIsConnected && DevicePathMatches(index, rotationSourceDevicePath_.data())) { rotationSourceId_ = index; return; }
}
void VirtualHmd::PublishTelemetry()
{
    control_protocol::TelemetrySnapshot snapshot{};
    { std::lock_guard<std::mutex> lock(telemetryMutex_); snapshot.sequence = telemetry_.sequence + 1; }
    snapshot.appliedRevision = appliedRevision_;
    if (activeManualPose_) snapshot.source = ToTelemetryPose(pose_math::MakeValidIdentityPose(true));
    else if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid) snapshot.source = ToTelemetryPose(pose_math::ConvertPose(rawPoses_[sourceId_]));
    if (rotationSourceDevicePath_[0] == '\0') snapshot.rotationSource = snapshot.source;
    else if (rotationSourceId_ != vr::k_unTrackedDeviceIndexInvalid)
        snapshot.rotationSource = ToTelemetryPose(pose_math::ConvertPose(rawPoses_[rotationSourceId_]));
    snapshot.output = ToTelemetryPose(lastPose_);
    std::lock_guard<std::mutex> lock(telemetryMutex_); telemetry_ = snapshot;
}

void VirtualHmd::GetWindowBounds(std::int32_t* x, std::int32_t* y, std::uint32_t* width, std::uint32_t* height)
{ *x = WindowX; *y = WindowY; *width = WindowWidth; *height = WindowHeight; }
bool VirtualHmd::IsDisplayOnDesktop() { return false; }
bool VirtualHmd::IsDisplayRealDisplay() { return false; }
void VirtualHmd::GetRecommendedRenderTargetSize(std::uint32_t* width, std::uint32_t* height)
{ *width = RenderWidthPerEye; *height = RenderHeightPerEye; }
void VirtualHmd::GetEyeOutputViewport(vr::EVREye eye, std::uint32_t* x, std::uint32_t* y,
    std::uint32_t* width, std::uint32_t* height)
{ *x = eye == vr::Eye_Left ? 0U : WindowWidth / 2U; *y = 0; *width = WindowWidth / 2U; *height = WindowHeight; }
void VirtualHmd::GetProjectionRaw(vr::EVREye, float* left, float* right, float* top, float* bottom)
{ *left = -1.0F; *right = 1.0F; *top = -1.0F; *bottom = 1.0F; }
vr::DistortionCoordinates_t VirtualHmd::ComputeDistortion(vr::EVREye, float u, float v)
{
    vr::DistortionCoordinates_t result{};
    result.rfRed[0] = result.rfGreen[0] = result.rfBlue[0] = u;
    result.rfRed[1] = result.rfGreen[1] = result.rfBlue[1] = v;
    return result;
}
bool VirtualHmd::ComputeInverseDistortion(vr::HmdVector2_t*, vr::EVREye, std::uint32_t, float, float) { return false; }
} // namespace trackswap
