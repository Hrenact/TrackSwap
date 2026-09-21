#include "virtual_tracker.h"

#include "pose_math.h"

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
    for (std::size_t axis = 0; axis < 3; ++axis)
    {
        result.position[axis] = pose.vecPosition[axis];
    }
    result.rotation[0] = pose.qRotation.x;
    result.rotation[1] = pose.qRotation.y;
    result.rotation[2] = pose.qRotation.z;
    result.rotation[3] = pose.qRotation.w;
    return result;
}

trackswap::control_protocol::TelemetryPose ToTelemetryPose(const vr::TrackedDevicePose_t& pose)
{
    return ToTelemetryPose(trackswap::pose_math::ConvertPose(pose));
}
} // namespace

namespace trackswap
{
VirtualTracker::VirtualTracker(std::uint8_t slot)
    : slot_(slot),
      serialNumber_("TRKSWAP-PROXY-" + (slot < 10 ? std::string("0") : std::string()) + std::to_string(slot)),
      registeredDeviceType_("trackswap/" + serialNumber_)
{
}

const char* VirtualTracker::SerialNumber() const
{
    return serialNumber_.c_str();
}

void VirtualTracker::ConfigureSource(const char* sourceDevicePath)
{
    sourceDevicePath_.fill('\0');
    if (sourceDevicePath != nullptr)
    {
        strncpy_s(sourceDevicePath_.data(), sourceDevicePath_.size(), sourceDevicePath, _TRUNCATE);
    }

    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    searchCountdown_ = 0;
}

void VirtualTracker::QueueSource(const char* sourceDevicePath)
{
    std::lock_guard<std::mutex> lock(pendingSourceMutex_);
    pendingEnabled_ = true;
    hasPendingEnabled_ = true;
    pendingSourceDevicePath_.fill('\0');
    strncpy_s(
        pendingSourceDevicePath_.data(),
        pendingSourceDevicePath_.size(),
        sourceDevicePath,
        _TRUNCATE);
    hasPendingSource_ = true;
}

void VirtualTracker::QueueOffset(const pose_math::RigidOffset& offset)
{
    std::lock_guard<std::mutex> lock(pendingSourceMutex_);
    pendingOffset_ = offset;
    hasPendingOffset_ = true;
}

bool VirtualTracker::QueueSnapshot(
    bool enabled,
    const char* sourceDevicePath,
    const char* targetDevicePath,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    std::lock_guard<std::mutex> lock(pendingSourceMutex_);
    if (revision < latestAcceptedSnapshotRevision_)
    {
        return false;
    }
    if (revision == latestAcceptedSnapshotRevision_ && revision != 0)
    {
        return true;
    }

    pendingEnabled_ = enabled;
    hasPendingEnabled_ = true;
    pendingSourceDevicePath_.fill('\0');
    pendingTargetDevicePath_.fill('\0');
    if (enabled)
    {
        strncpy_s(
            pendingSourceDevicePath_.data(),
            pendingSourceDevicePath_.size(),
            sourceDevicePath,
            _TRUNCATE);
        strncpy_s(
            pendingTargetDevicePath_.data(),
            pendingTargetDevicePath_.size(),
            targetDevicePath,
            _TRUNCATE);
    }
    pendingOffset_ = offset;
    hasPendingSource_ = true;
    hasPendingOffset_ = true;
    hasPendingSnapshotRevision_ = true;
    pendingSnapshotRevision_ = revision;
    latestAcceptedSnapshotRevision_ = revision;
    return true;
}

control_protocol::TelemetrySnapshot VirtualTracker::GetTelemetry() const
{
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    return telemetry_;
}

vr::EVRInitError VirtualTracker::Activate(std::uint32_t objectId)
{
    objectId_ = objectId;
    const vr::PropertyContainerHandle_t properties =
        vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId_);
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ModelNumber_String, "TrackSwap Virtual Tracker");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ManufacturerName_String, "Hrenact");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_RenderModelName_String,
        "{trackswap}trackswap_hidden_proxy");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_RegisteredDeviceType_String, registeredDeviceType_.c_str());
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ControllerType_String, "trackswap_tracker");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceOff_String,
        "{trackswap}/icons/trackswap_device_v3_off.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceSearching_String,
        "{trackswap}/icons/trackswap_device_v3_searching.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceSearchingAlert_String,
        "{trackswap}/icons/trackswap_device_v3_searching_alert.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceReady_String,
        "{trackswap}/icons/trackswap_device_v3_ready.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceReadyAlert_String,
        "{trackswap}/icons/trackswap_device_v3_ready_alert.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceNotReady_String,
        "{trackswap}/icons/trackswap_device_v3_not_ready.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceStandby_String,
        "{trackswap}/icons/trackswap_device_v3_standby.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceAlertLow_String,
        "{trackswap}/icons/trackswap_device_v3_alert_low.png");
    vr::VRProperties()->SetStringProperty(
        properties,
        vr::Prop_NamedIconPathDeviceStandbyAlert_String,
        "{trackswap}/icons/trackswap_device_v3_standby_alert.png");
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_WillDriftInYaw_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_DeviceIsWireless_Bool, false);
    vr::VRProperties()->SetBoolProperty(properties, vr::Prop_NeverTracked_Bool, false);
    lastPose_ = pose_math::MakeInvalidPose();
    return vr::VRInitError_None;
}

void VirtualTracker::Deactivate()
{
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    targetId_ = vr::k_unTrackedDeviceIndexInvalid;
    lastPose_ = pose_math::MakeInvalidPose();
    lastHealth_ = false;
}

void VirtualTracker::EnterStandby()
{
}

void* VirtualTracker::GetComponent(const char* componentNameAndVersion)
{
    static_cast<void>(componentNameAndVersion);
    return nullptr;
}

void VirtualTracker::DebugRequest(
    const char* request,
    char* responseBuffer,
    std::uint32_t responseBufferSize)
{
    static_cast<void>(request);
    if (responseBuffer != nullptr && responseBufferSize > 0)
    {
        responseBuffer[0] = '\0';
    }
}

vr::DriverPose_t VirtualTracker::GetPose()
{
    return lastPose_;
}

void VirtualTracker::Update()
{
    if (objectId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        return;
    }

    ApplyPendingSource();

    if (!activeEnabled_)
    {
        lastPose_ = pose_math::MakeInvalidPose();
        SetHealth(false);
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

    if (targetId_ != vr::k_unTrackedDeviceIndexInvalid && !rawPoses_[targetId_].bDeviceIsConnected)
    {
        targetId_ = vr::k_unTrackedDeviceIndexInvalid;
        targetSearchCountdown_ = 0;
    }

    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (searchCountdown_ == 0)
        {
            FindSource();
            searchCountdown_ = SearchIntervalFrames;
        }
        else
        {
            --searchCountdown_;
        }
    }


    if (targetId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        if (targetSearchCountdown_ == 0)
        {
            FindTarget();
            targetSearchCountdown_ = SearchIntervalFrames;
        }
        else
        {
            --targetSearchCountdown_;
        }
    }

    if (sourceId_ == vr::k_unTrackedDeviceIndexInvalid)
    {
        // Keep an enabled proxy registered while its physical source is absent.
        // Reporting the proxy itself as disconnected can make SteamVR discard the
        // active TrackingOverrides attachment, which does not reliably recover in
        // the same session when the source returns. The pose remains deliberately
        // invalid, so consumers cannot mistake the last sample for healthy tracking.
        lastPose_ = pose_math::MakeInvalidPose(true);
        SetHealth(false);
    }
    else
    {
        lastPose_ = pose_math::ApplyOffset(
            pose_math::ConvertPose(rawPoses_[sourceId_]),
            activeOffset_);
        SetHealth(lastPose_.deviceIsConnected && lastPose_.poseIsValid);
        if (!lastPose_.deviceIsConnected)
        {
            sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
        }
    }

    PublishTelemetry();
    vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, lastPose_, sizeof(lastPose_));
}

void VirtualTracker::ApplyPendingSource()
{
    std::array<char, MaximumDevicePathBytes> pending{};
    std::array<char, MaximumDevicePathBytes> pendingTarget{};
    pose_math::RigidOffset pendingOffset = pose_math::IdentityOffset();
    bool sourceChanged = false;
    bool offsetChanged = false;
    bool snapshotRevisionChanged = false;
    bool enabledChanged = false;
    bool pendingEnabled = false;
    std::uint64_t pendingRevision = 0;
    {
        std::lock_guard<std::mutex> lock(pendingSourceMutex_);
        if (!hasPendingSource_ && !hasPendingOffset_ && !hasPendingEnabled_)
        {
            return;
        }
        if (hasPendingSource_)
        {
            pending = pendingSourceDevicePath_;
            pendingTarget = pendingTargetDevicePath_;
            hasPendingSource_ = false;
            sourceChanged = true;
        }
        if (hasPendingOffset_)
        {
            pendingOffset = pendingOffset_;
            hasPendingOffset_ = false;
            offsetChanged = true;
        }
        if (hasPendingSnapshotRevision_)
        {
            pendingRevision = pendingSnapshotRevision_;
            hasPendingSnapshotRevision_ = false;
            snapshotRevisionChanged = true;
        }
        if (hasPendingEnabled_)
        {
            pendingEnabled = pendingEnabled_;
            hasPendingEnabled_ = false;
            enabledChanged = true;
        }
    }

    if (sourceChanged)
    {
        ConfigureSource(pending.data());
        targetDevicePath_ = pendingTarget;
        targetId_ = vr::k_unTrackedDeviceIndexInvalid;
        targetSearchCountdown_ = 0;
        lastPose_ = pose_math::MakeInvalidPose();
        SetHealth(false);
        vr::VRDriverLog()->Log("TrackSwap accepted a live source switch.");
    }
    if (offsetChanged)
    {
        activeOffset_ = pendingOffset;
        vr::VRDriverLog()->Log("TrackSwap applied a live pose offset.");
    }
    if (snapshotRevisionChanged)
    {
        appliedSnapshotRevision_ = pendingRevision;
    }
    if (enabledChanged)
    {
        activeEnabled_ = pendingEnabled;
        if (!activeEnabled_)
        {
            ConfigureSource(nullptr);
            targetDevicePath_.fill('\0');
            targetId_ = vr::k_unTrackedDeviceIndexInvalid;
            lastPose_ = pose_math::MakeInvalidPose();
            SetHealth(false);
        }
    }
}

bool VirtualTracker::DevicePathMatches(std::uint32_t deviceIndex, const char* expectedPath) const
{
    if (deviceIndex == objectId_ || expectedPath == nullptr || expectedPath[0] == '\0')
    {
        return false;
    }

    const vr::PropertyContainerHandle_t properties =
        vr::VRProperties()->TrackedDeviceToPropertyContainer(deviceIndex);
    std::array<char, MaximumDevicePathBytes> registeredType{};
    vr::ETrackedPropertyError error = vr::TrackedProp_Success;
    vr::VRProperties()->GetStringProperty(
        properties,
        vr::Prop_RegisteredDeviceType_String,
        registeredType.data(),
        static_cast<std::uint32_t>(registeredType.size()),
        &error);
    if (error != vr::TrackedProp_Success || registeredType[0] == '\0')
    {
        return false;
    }

    if (std::strncmp(registeredType.data(), "/devices/", 9) == 0)
    {
        return std::strcmp(expectedPath, registeredType.data()) == 0;
    }

    std::array<char, MaximumDevicePathBytes> fullPath{};
    const int written = sprintf_s(fullPath.data(), fullPath.size(), "/devices/%s", registeredType.data());
    return written > 0 && std::strcmp(expectedPath, fullPath.data()) == 0;
}

void VirtualTracker::FindSource()
{
    if (sourceDevicePath_[0] == '\0')
    {
        return;
    }

    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
    {
        if (rawPoses_[index].bDeviceIsConnected && DevicePathMatches(index, sourceDevicePath_.data()))
        {
            sourceId_ = index;
            vr::VRDriverLog()->Log("TrackSwap source device connected.");
            return;
        }
    }
}

void VirtualTracker::FindTarget()
{
    if (targetDevicePath_[0] == '\0')
    {
        return;
    }

    if (std::strcmp(targetDevicePath_.data(), "/user/head") == 0)
    {
        if (rawPoses_[vr::k_unTrackedDeviceIndex_Hmd].bDeviceIsConnected)
        {
            targetId_ = vr::k_unTrackedDeviceIndex_Hmd;
        }
        return;
    }

    vr::ETrackedControllerRole expectedRole = vr::TrackedControllerRole_Invalid;
    if (std::strcmp(targetDevicePath_.data(), "/user/hand/left") == 0)
    {
        expectedRole = vr::TrackedControllerRole_LeftHand;
    }
    else if (std::strcmp(targetDevicePath_.data(), "/user/hand/right") == 0)
    {
        expectedRole = vr::TrackedControllerRole_RightHand;
    }

    for (std::uint32_t index = 0; index < rawPoses_.size(); ++index)
    {
        if (index == objectId_ || !rawPoses_[index].bDeviceIsConnected)
        {
            continue;
        }
        if (expectedRole != vr::TrackedControllerRole_Invalid)
        {
            const vr::PropertyContainerHandle_t properties =
                vr::VRProperties()->TrackedDeviceToPropertyContainer(index);
            vr::ETrackedPropertyError error = vr::TrackedProp_Success;
            const auto role = static_cast<vr::ETrackedControllerRole>(
                vr::VRProperties()->GetInt32Property(properties, vr::Prop_ControllerRoleHint_Int32, &error));
            if (error == vr::TrackedProp_Success && role == expectedRole)
            {
                targetId_ = index;
                return;
            }
        }
        else if (DevicePathMatches(index, targetDevicePath_.data()))
        {
            targetId_ = index;
            return;
        }
    }
}

void VirtualTracker::PublishTelemetry()
{
    control_protocol::TelemetrySnapshot snapshot{};
    {
        std::lock_guard<std::mutex> lock(telemetryMutex_);
        snapshot.sequence = telemetry_.sequence + 1;
    }
    snapshot.appliedRevision = appliedSnapshotRevision_;
    if (sourceId_ != vr::k_unTrackedDeviceIndexInvalid)
    {
        snapshot.source = ToTelemetryPose(rawPoses_[sourceId_]);
    }
    snapshot.output = ToTelemetryPose(lastPose_);
    if (targetId_ != vr::k_unTrackedDeviceIndexInvalid)
    {
        snapshot.target = ToTelemetryPose(rawPoses_[targetId_]);
    }
    std::lock_guard<std::mutex> lock(telemetryMutex_);
    telemetry_ = snapshot;
}

void VirtualTracker::SetHealth(bool healthy)
{
    if (healthy == lastHealth_)
    {
        return;
    }

    lastHealth_ = healthy;
    vr::VRDriverLog()->Log(healthy
        ? "TrackSwap virtual tracker is receiving a valid source pose."
        : "TrackSwap source pose became invalid or disconnected.");
}
} // namespace trackswap
