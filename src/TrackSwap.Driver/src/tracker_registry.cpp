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
}
} // namespace trackswap
