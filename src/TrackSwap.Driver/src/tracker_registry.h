#pragma once

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>

#include "control_protocol.h"
#include "pose_math.h"
#include "virtual_tracker.h"
#include "virtual_controller.h"

namespace trackswap
{
class TrackerRegistry final
{
public:
    TrackerRegistry();

    bool QueueSource(std::uint8_t slot, const char* sourceDevicePath);
    bool QueueOffset(std::uint8_t slot, const pose_math::RigidOffset& offset);
    bool QueueSnapshot(
        std::uint8_t slot,
        bool enabled,
        const char* sourceDevicePath,
        const char* targetDevicePath,
        const pose_math::RigidOffset& offset,
        std::uint64_t revision);
    bool QueueControllerSnapshot(
        ControllerHand hand,
        bool enabled,
        std::uint8_t logicalSlot,
        const char* sourceDevicePath,
        std::int32_t handSelectionPriority,
        const pose_math::RigidOffset& offset,
        std::uint64_t revision);
    bool QueueControllerInput(const control_protocol::ControllerInputState& input);
    control_protocol::TelemetryBatch GetTelemetry() const;
    void RunFrame();

private:
    std::array<std::unique_ptr<VirtualTracker>, control_protocol::MaximumRoutes> directTrackers_;
    std::array<std::unique_ptr<VirtualTracker>, control_protocol::MaximumRoutes> proxyTrackers_;
    std::array<std::atomic<bool>, control_protocol::MaximumRoutes> directRegistrationRequested_{};
    std::array<std::atomic<bool>, control_protocol::MaximumRoutes> proxyRegistrationRequested_{};
    std::array<bool, control_protocol::MaximumRoutes> directRegistered_{};
    std::array<bool, control_protocol::MaximumRoutes> proxyRegistered_{};
    std::array<std::atomic<bool>, control_protocol::MaximumRoutes> activeProxy_{};
    std::array<std::unique_ptr<VirtualController>, 2> controllers_;
    std::array<std::atomic<bool>, 2> controllerRegistrationRequested_{};
    std::array<bool, 2> controllerRegistered_{};
};
} // namespace trackswap
