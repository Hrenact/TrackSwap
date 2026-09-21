#pragma once

#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>

#include <openvr_driver.h>

#include "control_protocol.h"
#include "pose_math.h"

namespace trackswap
{
enum class ControllerHand : std::uint8_t
{
    Left = 1,
    Right = 2
};

class VirtualController final : public vr::ITrackedDeviceServerDriver
{
public:
    explicit VirtualController(ControllerHand hand);

    const char* SerialNumber() const;
    bool QueueSnapshot(
        bool enabled,
        std::uint8_t logicalSlot,
        const char* sourceDevicePath,
        const pose_math::RigidOffset& offset,
        std::uint64_t revision);
    void QueueInput(const control_protocol::ControllerInputState& input);
    control_protocol::TelemetrySnapshot GetTelemetry() const;
    std::uint8_t LogicalSlot() const;
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
    void ApplyPending();
    void ApplyInput();
    void PublishTelemetry();

    static constexpr std::size_t MaximumDevicePathBytes = 512;
    static constexpr std::uint32_t SearchIntervalFrames = 60;

    ControllerHand hand_;
    std::string serialNumber_;
    std::string registeredDeviceType_;
    std::array<char, MaximumDevicePathBytes> sourceDevicePath_{};
    std::array<char, MaximumDevicePathBytes> pendingSourceDevicePath_{};
    pose_math::RigidOffset activeOffset_ = pose_math::IdentityOffset();
    pose_math::RigidOffset pendingOffset_ = pose_math::IdentityOffset();
    std::array<vr::TrackedDevicePose_t, vr::k_unMaxTrackedDeviceCount> rawPoses_{};
    vr::TrackedDeviceIndex_t objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::TrackedDeviceIndex_t sourceId_ = vr::k_unTrackedDeviceIndexInvalid;
    std::uint32_t searchCountdown_ = 0;
    bool activeEnabled_ = false;
    bool pendingEnabled_ = false;
    bool hasPendingSnapshot_ = false;
    std::uint64_t pendingRevision_ = 0;
    std::uint64_t latestAcceptedRevision_ = 0;
    std::uint64_t appliedRevision_ = 0;
    std::atomic<std::uint8_t> logicalSlot_{255};
    std::uint8_t pendingLogicalSlot_ = 255;
    std::mutex pendingMutex_;
    control_protocol::ControllerInputState pendingInput_{};
    control_protocol::ControllerInputState activeInput_{};
    bool hasPendingInput_ = false;
    vr::VRInputComponentHandle_t primaryHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t secondaryHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t joystickXHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t joystickYHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t joystickClickHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t triggerValueHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t triggerClickHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t gripValueHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t gripForceHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t gripTouchHandle_ = vr::k_ulInvalidInputComponentHandle;
    vr::VRInputComponentHandle_t menuHandle_ = vr::k_ulInvalidInputComponentHandle;
    mutable std::mutex telemetryMutex_;
    control_protocol::TelemetrySnapshot telemetry_{};
    vr::DriverPose_t lastPose_{};
};
} // namespace trackswap
