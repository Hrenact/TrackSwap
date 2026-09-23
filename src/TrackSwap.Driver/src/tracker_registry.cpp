#include "tracker_registry.h"

#include <openvr_driver.h>

namespace trackswap
{
TrackerRegistry::TrackerRegistry()
{
    for (std::uint8_t slot = 0; slot < directTrackers_.size(); ++slot)
    {
        directTrackers_[slot] = std::make_unique<VirtualTracker>(slot, false);
        proxyTrackers_[slot] = std::make_unique<VirtualTracker>(slot, true);
        directRegistrationRequested_[slot].store(false);
        proxyRegistrationRequested_[slot].store(false);
        directRegistered_[slot] = false;
        proxyRegistered_[slot] = false;
        activeProxy_[slot].store(false);
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
    std::int32_t handSelectionPriority,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    const std::size_t index = hand == ControllerHand::Left ? 0U : hand == ControllerHand::Right ? 1U : 2U;
    if (index >= controllers_.size()) return false;
    if (enabled) controllerRegistrationRequested_[index].store(true);
    return controllers_[index]->QueueSnapshot(
        enabled,
        logicalSlot,
        sourceDevicePath,
        handSelectionPriority,
        offset,
        revision);
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
    if (slot >= directTrackers_.size())
    {
        return false;
    }
    const bool proxy = activeProxy_[slot].load();
    (proxy ? proxyRegistrationRequested_[slot] : directRegistrationRequested_[slot]).store(true);
    (proxy ? proxyTrackers_[slot] : directTrackers_[slot])->QueueSource(sourceDevicePath);
    return true;
}

bool TrackerRegistry::QueueOffset(std::uint8_t slot, const pose_math::RigidOffset& offset)
{
    if (slot >= directTrackers_.size())
    {
        return false;
    }
    const bool proxy = activeProxy_[slot].load();
    (proxy ? proxyRegistrationRequested_[slot] : directRegistrationRequested_[slot]).store(true);
    (proxy ? proxyTrackers_[slot] : directTrackers_[slot])->QueueOffset(offset);
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
    if (slot >= directTrackers_.size())
    {
        return false;
    }
    const bool proxy = enabled && targetDevicePath != nullptr && targetDevicePath[0] != '\0';
    activeProxy_[slot].store(proxy);
    if (enabled)
    {
        (proxy ? proxyRegistrationRequested_[slot] : directRegistrationRequested_[slot]).store(true);
    }
    VirtualTracker* active = proxy ? proxyTrackers_[slot].get() : directTrackers_[slot].get();
    VirtualTracker* inactive = proxy ? directTrackers_[slot].get() : proxyTrackers_[slot].get();
    const bool activeQueued = active->QueueSnapshot(
        enabled,
        sourceDevicePath,
        targetDevicePath,
        offset,
        revision);
    const bool inactiveQueued = inactive->QueueSnapshot(
        false,
        "",
        "",
        offset,
        revision);
    return activeQueued && inactiveQueued;
}

control_protocol::TelemetryBatch TrackerRegistry::GetTelemetry() const
{
    control_protocol::TelemetryBatch batch{};
    batch.count = static_cast<std::uint8_t>(directTrackers_.size());
    for (std::size_t slot = 0; slot < directTrackers_.size(); ++slot)
    {
        batch.snapshots[slot] = activeProxy_[slot].load()
            ? proxyTrackers_[slot]->GetTelemetry()
            : directTrackers_[slot]->GetTelemetry();
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
    for (std::size_t slot = 0; slot < directTrackers_.size(); ++slot)
    {
        if (!directRegistered_[slot] && directRegistrationRequested_[slot].exchange(false))
        {
            directRegistered_[slot] = vr::VRServerDriverHost()->TrackedDeviceAdded(
                directTrackers_[slot]->SerialNumber(),
                vr::TrackedDeviceClass_GenericTracker,
                directTrackers_[slot].get());
            vr::VRDriverLog()->Log(directRegistered_[slot]
                ? "TrackSwap registered a requested direct virtual tracker."
                : "TrackSwap failed to register a requested direct virtual tracker.");
        }
        if (directRegistered_[slot])
        {
            directTrackers_[slot]->Update();
        }
        if (!proxyRegistered_[slot] && proxyRegistrationRequested_[slot].exchange(false))
        {
            proxyRegistered_[slot] = vr::VRServerDriverHost()->TrackedDeviceAdded(
                proxyTrackers_[slot]->SerialNumber(),
                vr::TrackedDeviceClass_GenericTracker,
                proxyTrackers_[slot].get());
            vr::VRDriverLog()->Log(proxyRegistered_[slot]
                ? "TrackSwap registered a requested replacement proxy."
                : "TrackSwap failed to register a requested replacement proxy.");
        }
        if (proxyRegistered_[slot])
        {
            proxyTrackers_[slot]->Update();
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
