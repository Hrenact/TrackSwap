#include "tracker_registry.h"

#include "controller_input_routing.h"

#include <openvr_driver.h>

#include <cmath>
#include <cstring>

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
    const char* rotationSourceDevicePath,
    bool hidePhysicalSource,
    std::int32_t handSelectionPriority,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    const std::size_t index = hand == ControllerHand::Left ? 0U : hand == ControllerHand::Right ? 1U : 2U;
    if (index >= controllers_.size()) return false;
    if (enabled) controllerRegistrationRequested_[index].store(true);
    {
        std::lock_guard<std::mutex> lock(hideSourceMutex_);
        const std::uint8_t previousLogicalSlot = controllerHideLogicalSlots_[index];
        if (previousLogicalSlot < controllerHideSourceRequested_.size() &&
            (!enabled || logicalSlot != previousLogicalSlot))
        {
            controllerHideSourceRequested_[previousLogicalSlot] = false;
            controllerHideSourcePaths_[previousLogicalSlot].fill('\0');
            controllerHideRotationSourcePaths_[previousLogicalSlot].fill('\0');
        }
        if (enabled && logicalSlot < controllerHideSourceRequested_.size())
        {
            controllerHideLogicalSlots_[index] = logicalSlot;
            controllerHideSourceRequested_[logicalSlot] = hidePhysicalSource;
            controllerHideSourcePaths_[logicalSlot].fill('\0');
            controllerHideRotationSourcePaths_[logicalSlot].fill('\0');
            if (hidePhysicalSource && sourceDevicePath != nullptr)
            {
                strncpy_s(
                    controllerHideSourcePaths_[logicalSlot].data(),
                    controllerHideSourcePaths_[logicalSlot].size(),
                    sourceDevicePath,
                    _TRUNCATE);
                if (rotationSourceDevicePath != nullptr)
                {
                    strncpy_s(
                        controllerHideRotationSourcePaths_[logicalSlot].data(),
                        controllerHideRotationSourcePaths_[logicalSlot].size(),
                        rotationSourceDevicePath,
                        _TRUNCATE);
                }
            }
        }
        else
        {
            controllerHideLogicalSlots_[index] = 255;
        }
    }
    return controllers_[index]->QueueSnapshot(
        enabled,
        logicalSlot,
        sourceDevicePath,
        rotationSourceDevicePath,
        handSelectionPriority,
        offset,
        revision);
}

bool TrackerRegistry::QueueControllerInput(const control_protocol::ControllerInputState& input)
{
    const std::size_t index = input.hand == static_cast<std::uint8_t>(ControllerHand::Left) ? 0U :
        input.hand == static_cast<std::uint8_t>(ControllerHand::Right) ? 1U : 2U;
    if (index >= controllers_.size()) return false;
    std::lock_guard<std::mutex> lock(controllerInputMutex_);
    controllerInputs_[index] = input;
    controllers_[0]->QueueInput(controller_input_routing::BuildLeftInput(
        controllerInputs_[0],
        controllerInputs_[1]));
    if (index == 1U)
    {
        controllers_[1]->QueueInput(controller_input_routing::BuildRightInput(controllerInputs_[1]));
    }
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
    const char* rotationSourceDevicePath,
    const char* targetDevicePath,
    bool hidePhysicalSource,
    const pose_math::RigidOffset& offset,
    std::uint64_t revision)
{
    if (slot >= directTrackers_.size())
    {
        return false;
    }
    {
        std::lock_guard<std::mutex> lock(hideSourceMutex_);
        trackerHideSourceRequested_[slot] = enabled && hidePhysicalSource;
        trackerHideSourcePaths_[slot].fill('\0');
        trackerHideRotationSourcePaths_[slot].fill('\0');
        if (enabled && hidePhysicalSource && sourceDevicePath != nullptr)
        {
            strncpy_s(
                trackerHideSourcePaths_[slot].data(),
                trackerHideSourcePaths_[slot].size(),
                sourceDevicePath,
                _TRUNCATE);
            if (rotationSourceDevicePath != nullptr)
            {
                strncpy_s(
                    trackerHideRotationSourcePaths_[slot].data(),
                    trackerHideRotationSourcePaths_[slot].size(),
                    rotationSourceDevicePath,
                    _TRUNCATE);
            }
        }
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
        rotationSourceDevicePath,
        targetDevicePath,
        offset,
        revision);
    const bool inactiveQueued = inactive->QueueSnapshot(
        false,
        "",
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

control_protocol::HapticFeedbackBatch TrackerRegistry::GetHapticEvents()
{
    control_protocol::HapticFeedbackBatch batch{};
    std::size_t readIndex = hapticReadIndex_.load(std::memory_order_relaxed);
    const std::size_t writeIndex = hapticWriteIndex_.load(std::memory_order_acquire);
    while (readIndex != writeIndex && batch.count < control_protocol::MaximumHapticEvents)
    {
        batch.events[batch.count++] = hapticQueue_[readIndex];
        readIndex = (readIndex + 1) % HapticQueueCapacity;
    }
    hapticReadIndex_.store(readIndex, std::memory_order_release);
    return batch;
}

void TrackerRegistry::QueueHapticEvent(const vr::VREvent_t& event)
{
    std::uint8_t hand = 0;
    if (controllers_[0]->MatchesHapticComponent(event.data.hapticVibration.componentHandle))
    {
        hand = static_cast<std::uint8_t>(ControllerHand::Left);
    }
    else if (controllers_[1]->MatchesHapticComponent(event.data.hapticVibration.componentHandle))
    {
        hand = static_cast<std::uint8_t>(ControllerHand::Right);
    }
    if (hand == 0 ||
        !std::isfinite(event.data.hapticVibration.fDurationSeconds) ||
        !std::isfinite(event.data.hapticVibration.fFrequency) ||
        !std::isfinite(event.data.hapticVibration.fAmplitude))
    {
        return;
    }

    const std::size_t writeIndex = hapticWriteIndex_.load(std::memory_order_relaxed);
    const std::size_t nextIndex = (writeIndex + 1) % HapticQueueCapacity;
    if (nextIndex == hapticReadIndex_.load(std::memory_order_acquire))
    {
        return;
    }
    hapticQueue_[writeIndex] = {
        ++hapticSequence_,
        hand,
        event.data.hapticVibration.fDurationSeconds,
        event.data.hapticVibration.fFrequency,
        event.data.hapticVibration.fAmplitude};
    hapticWriteIndex_.store(nextIndex, std::memory_order_release);
}

void TrackerRegistry::InitializePoseHiding(vr::IVRDriverContext* driverContext)
{
    poseHidingHook_.Initialize(driverContext);
}

void TrackerRegistry::ShutdownPoseHiding()
{
    poseHidingHook_.Shutdown();
}

control_protocol::PhysicalSourceHidingStatus TrackerRegistry::GetPhysicalSourceHidingStatus() const
{
    return poseHidingHook_.GetStatus();
}

void TrackerRegistry::UpdatePoseHiding()
{
    std::array<bool, control_protocol::MaximumRoutes> requested{};
    std::array<std::array<char, MaximumDevicePathBytes>, control_protocol::MaximumRoutes> paths{};
    std::array<std::array<char, MaximumDevicePathBytes>, control_protocol::MaximumRoutes> rotationPaths{};
    {
        std::lock_guard<std::mutex> lock(hideSourceMutex_);
        for (std::size_t slot = 0; slot < requested.size(); ++slot)
        {
            if (controllerHideSourceRequested_[slot])
            {
                requested[slot] = true;
                paths[slot] = controllerHideSourcePaths_[slot];
                rotationPaths[slot] = controllerHideRotationSourcePaths_[slot];
            }
            else if (trackerHideSourceRequested_[slot])
            {
                requested[slot] = true;
                paths[slot] = trackerHideSourcePaths_[slot];
                rotationPaths[slot] = trackerHideRotationSourcePaths_[slot];
            }
        }
    }

    std::array<const char*, control_protocol::MaximumRoutes * 2> requestedPaths{};
    std::size_t requestedPathCount = 0;
    for (std::size_t slot = 0; slot < requested.size(); ++slot)
    {
        if (!requested[slot]) continue;
        requestedPaths[requestedPathCount++] = paths[slot].data();
        if (rotationPaths[slot][0] != '\0')
        {
            requestedPaths[requestedPathCount++] = rotationPaths[slot].data();
        }
    }
    std::uint8_t uniqueRequested = 0;
    for (std::size_t index = 0; index < requestedPathCount; ++index)
    {
        bool duplicate = false;
        for (std::size_t earlier = 0; earlier < index; ++earlier)
        {
            if (std::strcmp(requestedPaths[index], requestedPaths[earlier]) == 0)
            {
                duplicate = true;
                break;
            }
        }
        if (!duplicate) ++uniqueRequested;
    }

    poseHidingHook_.SetRequestedDeviceCount(uniqueRequested);
    if (uniqueRequested == 0)
    {
        poseHidingHook_.Shutdown();
        return;
    }
    if (!poseHidingHook_.EnsureInstalled()) return;

    std::array<bool, vr::k_unMaxTrackedDeviceCount> hiddenIds{};
    for (std::size_t slot = 0; slot < requested.size(); ++slot)
    {
        if (!requested[slot]) continue;
        VirtualTracker* tracker = activeProxy_[slot].load()
            ? proxyTrackers_[slot].get()
            : directTrackers_[slot].get();
        vr::TrackedDeviceIndex_t sourceId = tracker->SourceDeviceId();
        vr::TrackedDeviceIndex_t rotationSourceId = tracker->RotationSourceDeviceId();
        for (const auto& controller : controllers_)
        {
            if (controller->LogicalSlot() == slot)
            {
                sourceId = controller->SourceDeviceId();
                rotationSourceId = controller->RotationSourceDeviceId();
                break;
            }
        }
        if (sourceId < hiddenIds.size()) hiddenIds[sourceId] = true;
        if (rotationSourceId < hiddenIds.size()) hiddenIds[rotationSourceId] = true;
    }
    poseHidingHook_.SetHiddenDeviceIds(hiddenIds);
}

void TrackerRegistry::RunFrame()
{
    vr::VREvent_t event{};
    while (vr::VRServerDriverHost()->PollNextEvent(&event, sizeof(event)))
    {
        if (event.eventType == vr::VREvent_Input_HapticVibration)
        {
            QueueHapticEvent(event);
        }
    }
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
            // Replacement proxies are internal pose carriers for TrackingOverrides,
            // not user-facing body trackers. Keep direct outputs as GenericTracker,
            // but classify proxies as tracking references so applications do not
            // offer them as additional wearable trackers.
            proxyRegistered_[slot] = vr::VRServerDriverHost()->TrackedDeviceAdded(
                proxyTrackers_[slot]->SerialNumber(),
                vr::TrackedDeviceClass_TrackingReference,
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
    UpdatePoseHiding();
}
} // namespace trackswap
