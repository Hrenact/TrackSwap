#pragma once

#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>

#include <openvr_driver.h>

#include "control_protocol.h"
#include "pose_math.h"

namespace trackswap
{
class VirtualHmd final : public vr::ITrackedDeviceServerDriver, public vr::IVRDisplayComponent
{
public:
    static constexpr const char* SerialNumber = "TRKSWAP-HMD";

    bool QueueSnapshot(bool enabled, std::uint8_t logicalSlot, bool manualPose, const char* sourceDevicePath,
        const char* rotationSourceDevicePath, const pose_math::RigidOffset& offset, std::uint64_t revision);
    control_protocol::TelemetrySnapshot GetTelemetry() const;
    std::uint8_t LogicalSlot() const;
    vr::TrackedDeviceIndex_t SourceDeviceId() const;
    vr::TrackedDeviceIndex_t RotationSourceDeviceId() const;
    void Update();

    vr::EVRInitError Activate(std::uint32_t objectId) override;
    void Deactivate() override;
    void EnterStandby() override;
    void* GetComponent(const char* componentNameAndVersion) override;
    void DebugRequest(const char* request, char* responseBuffer, std::uint32_t responseBufferSize) override;
    vr::DriverPose_t GetPose() override;
    void GetWindowBounds(std::int32_t* x, std::int32_t* y, std::uint32_t* width, std::uint32_t* height) override;
    bool IsDisplayOnDesktop() override;
    bool IsDisplayRealDisplay() override;
    void GetRecommendedRenderTargetSize(std::uint32_t* width, std::uint32_t* height) override;
    void GetEyeOutputViewport(vr::EVREye eye, std::uint32_t* x, std::uint32_t* y,
        std::uint32_t* width, std::uint32_t* height) override;
    void GetProjectionRaw(vr::EVREye eye, float* left, float* right, float* top, float* bottom) override;
    vr::DistortionCoordinates_t ComputeDistortion(vr::EVREye eye, float u, float v) override;
    bool ComputeInverseDistortion(vr::HmdVector2_t* result, vr::EVREye eye,
        std::uint32_t channel, float u, float v) override;

private:
    bool DevicePathMatches(std::uint32_t index, const char* expected) const;
    void FindSource();
    void FindRotationSource();
    void ApplyPending();
    void PublishTelemetry();

    static constexpr std::size_t MaximumDevicePathBytes = 512;
    static constexpr std::uint32_t SearchIntervalFrames = 60;
    std::array<char, MaximumDevicePathBytes> sourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingSourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> rotationSourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingRotationSourceDevicePath_{};
    pose_math::RigidOffset activeOffset_ = pose_math::IdentityOffset();
    pose_math::RigidOffset pendingOffset_ = pose_math::IdentityOffset();
    std::array<vr::TrackedDevicePose_t, vr::k_unMaxTrackedDeviceCount> rawPoses_{};
    vr::TrackedDeviceIndex_t objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::VRInputComponentHandle_t proximityHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::TrackedDeviceIndex_t sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::TrackedDeviceIndex_t rotationSourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    std::uint32_t searchCountdown_ = 0;
    std::uint32_t rotationSearchCountdown_ = 0;
    bool activeEnabled_ = false;
    bool pendingEnabled_ = false;
    bool activeManualPose_ = false;
    bool pendingManualPose_ = false;
    bool hasPendingSnapshot_ = false;
    std::uint64_t pendingRevision_ = 0;
    std::uint64_t latestAcceptedRevision_ = 0;
    std::uint64_t appliedRevision_ = 0;
    std::atomic<std::uint8_t> logicalSlot_{255};
    std::uint8_t pendingLogicalSlot_ = 255;
    std::mutex pendingMutex_;
    mutable std::mutex telemetryMutex_;
    control_protocol::TelemetrySnapshot telemetry_{};
    vr::DriverPose_t lastPose_{};
};
} // namespace trackswap
