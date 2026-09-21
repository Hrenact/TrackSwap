#include "tracker_registry.h"

#include <openvr_driver.h>

namespace trackswap
{
TrackerRegistry::TrackerRegistry()
{
    for (std::uint8_t slot = 0; slot < trackers_.size(); ++slot)
    {
        trackers_[slot] = std::make_unique<VirtualTracker>(slot);
        registrationRequested_[slot].store(false);
        registered_[slot] = false;
    }
    controllers_[0] = std::make_unique<VirtualController>(ControllerHand::Left);
    controllers_[1] = std::make_unique<VirtualController>(ControllerHand::Right);
    for (std::size_t index = 0; index < controllers_.size(); ++index)
    {
        controllerRegistrationRequested_[index].store(false);
        controllerRegistered_[index] = false;
    }
}

bool TrackerRegistry::QueueControllerSnapshot(
    ControllerHand hand,
    bool enabled,
    std::uint8_t logicalSlot,
    const char* sourceDevicePath,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    const std::size_t index = hand == ControllerHand::Left ? 0U : hand == ControllerHand::Right ? 1U : 2U;
    if (index >= controllers_.size()) return false;
    if (enabled) controllerRegistrationRequested_[index].store(true);
    return controllers_[index]->QueueSnapshot(enabled, logicalSlot, sourceDevicePath, offset, revision);
}

bool TrackerRegistry::QueueControllerInput(const control_protocol::ControllerInputState& input)
{
    const std::size_t index = input.hand == static_cast<std::uint8_t>(ControllerHand::Left) ? 0U :
        input.hand == static_cast<std::uint8_t>(ControllerHand::Right) ? 1U : 2U;
    if (index >= controllers_.size()) return false;
    controllers_[index]->QueueInput(input);
    return true;
}

bool TrackerRegistry::QueueSource(std::uint8_t slot, const char* sourceDevicePath)
{
    if (slot >= trackers_.size())
    {
        return false;
    }
    registrationRequested_[slot].store(true);
    trackers_[slot]->QueueSource(sourceDevicePath);
    return true;
}

bool TrackerRegistry::QueueOffset(std::uint8_t slot, const pose_math::RigidOffset& offset)
{
    if (slot >= trackers_.size())
    {
        return false;
    }
    registrationRequested_[slot].store(true);
    trackers_[slot]->QueueOffset(offset);
    return true;
}

bool TrackerRegistry::QueueSnapshot(
    std::uint8_t slot,
    bool enabled,
    const char* sourceDevicePath,
    const char* targetDevicePath,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    if (slot >= trackers_.size())
    {
        return false;
    }
    if (enabled)
    {
        registrationRequested_[slot].store(true);
    }
    return trackers_[slot]->QueueSnapshot(
        enabled,
        sourceDevicePath,
        targetDevicePath,
        offset,
        revision);
}

control_protocol::TelemetryBatch TrackerRegistry::GetTelemetry() const
{
    control_protocol::TelemetryBatch batch{};
    batch.count = static_cast<std::uint8_t>(trackers_.size());
    for (std::size_t slot = 0; slot < trackers_.size(); ++slot)
    {
        batch.snapshots[slot] = trackers_[slot]->GetTelemetry();
    }
    for (const auto& controller : controllers_)
    {
        const std::uint8_t slot = controller->LogicalSlot();
        if (slot < control_protocol::MaximumRoutes)
        {
            batch.snapshots[slot] = controller->GetTelemetry();
        }
    }
    return batch;
}

void TrackerRegistry::RunFrame()
{
    for (std::size_t slot = 0; slot < trackers_.size(); ++slot)
    {
        if (!registered_[slot] && registrationRequested_[slot].exchange(false))
        {
            registered_[slot] = vr::VRServerDriverHost()->TrackedDeviceAdded(
                trackers_[slot]->SerialNumber(),
                vr::TrackedDeviceClass_GenericTracker,
                trackers_[slot].get());
            vr::VRDriverLog()->Log(registered_[slot]
                ? "TrackSwap registered a requested virtual tracker."
                : "TrackSwap failed to register a requested virtual tracker.");
        }
        if (registered_[slot])
        {
            trackers_[slot]->Update();
        }
    }
    for (std::size_t index = 0; index < controllers_.size(); ++index)
    {
        if (!controllerRegistered_[index] && controllerRegistrationRequested_[index].exchange(false))
        {
            controllerRegistered_[index] = vr::VRServerDriverHost()->TrackedDeviceAdded(
                controllers_[index]->SerialNumber(),
                vr::TrackedDeviceClass_Controller,
                controllers_[index].get());
            vr::VRDriverLog()->Log(controllerRegistered_[index]
                ? "TrackSwap registered a requested virtual controller."
                : "TrackSwap failed to register a requested virtual controller.");
        }
        if (controllerRegistered_[index]) controllers_[index]->Update();
    }
}
} // namespace trackswap
