#pragma once

#include <array>
#include <atomic>
#include <cstdint>
#include <memory>

#include "control_protocol.h"
#include "pose_math.h"
#include "virtual_tracker.h"

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
    control_protocol::TelemetryBatch GetTelemetry() const;
    void RunFrame();

private:
    std::array<std::unique_ptr<VirtualTracker>, control_protocol::MaximumRoutes> trackers_;
    std::array<std::atomic<bool>, control_protocol::MaximumRoutes> registrationRequested_{};
    std::array<bool, control_protocol::MaximumRoutes> registered_{};
};
} // namespace trackswap
