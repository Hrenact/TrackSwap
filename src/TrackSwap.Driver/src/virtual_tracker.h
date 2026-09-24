#pragma once

#include <array>
#include <cstdint>
#include <mutex>
#include <string>

#include <openvr_driver.h>

#include "control_protocol.h"
#include "pose_math.h"

namespace trackswap
{
class VirtualTracker final : public vr::ITrackedDeviceServerDriver
{
public:
    VirtualTracker(std::uint8_t slot, bool proxyDevice);

    const char* SerialNumber() const;

    void ConfigureSource(const char* sourceDevicePath);
    void QueueSource(const char* sourceDevicePath);
    void QueueOffset(const pose_math::RigidOffset& offset);
    bool QueueSnapshot(
        bool enabled,
        const char* sourceDevicePath,
        const char* rotationSourceDevicePath,
        const char* targetDevicePath,
        const pose_math::RigidOffset& offset,
        std::uint64_t revision);
    control_protocol::TelemetrySnapshot GetTelemetry() const;
    vr::TrackedDeviceIndex_t SourceDeviceId() const;
    vr::TrackedDeviceIndex_t RotationSourceDeviceId() const;
    void Update();

    vr::EVRInitError Activate(std::uint32_t objectId) override;
    void Deactivate() override;
    void EnterStandby() override;
    void* GetComponent(const char* componentNameAndVersion) override;
    void DebugRequest(const char* request, char* responseBuffer, std::uint32_t responseBufferSize) override;
    vr::DriverPose_t GetPose() override;

private:
    bool DevicePathMatches(std::uint32_t deviceIndex, const char* expectedPath) const;
    void FindSource();
    void FindRotationSource();
    void FindTarget();
    void ApplyPendingSource();
    void PublishTelemetry();
    void SetHealth(bool healthy);
    void SetRenderModelVisible(bool visible, bool force = false);

    static constexpr std::size_t MaximumDevicePathBytes = 512;
    static constexpr std::uint32_t SearchIntervalFrames = 60;

    std::array<char, MaximumDevicePathBytes> sourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingSourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> rotationSourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingRotationSourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> targetDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingTargetDevicePath_{};
    pose_math::RigidOffset activeOffset_ = pose_math::IdentityOffset();
    pose_math::RigidOffset pendingOffset_ = pose_math::IdentityOffset();
    std::array<vr::TrackedDevicePose_t, vr::k_unMaxTrackedDeviceCount> rawPoses_{};
    vr::TrackedDeviceIndex_t objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::TrackedDeviceIndex_t sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::TrackedDeviceIndex_t rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::TrackedDeviceIndex_t targetId_ = vr::k_unTrackedDeviceIndexInvalid;
    std::uint32_t searchCountdown_ = 0;
    std::uint32_t rotationSearchCountdown_ = 0;
    std::uint32_t targetSearchCountdown_ = 0;
    bool lastHealth_ = false;
    bool renderModelVisible_ = true;
    bool activeEnabled_ = false;
    bool pendingEnabled_ = false;
    bool hasPendingEnabled_ = false;
    bool hasPendingSource_ = false;
    bool hasPendingOffset_ = false;
    bool hasPendingSnapshotRevision_ = false;
    std::uint64_t pendingSnapshotRevision_ = 0;
    std::uint64_t latestAcceptedSnapshotRevision_ = 0;
    std::uint64_t appliedSnapshotRevision_ = 0;
    std::mutex pendingSourceMutex_;
    mutable std::mutex telemetryMutex_;
    control_protocol::TelemetrySnapshot telemetry_{};
    vr::DriverPose_t lastPose_{};
    std::uint8_t slot_ = 0;
    bool proxyDevice_ = false;
    std::string serialNumber_;
    std::string registeredDeviceType_;
};
} // namespace trackswap
