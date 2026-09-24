#include "pose_hiding_hook.h"

#include <algorithm>
#include <cstring>

#include <MinHook.h>

namespace trackswap
{
PoseHidingHook* PoseHidingHook::instance_ = nullptr;

PoseHidingHook::PoseHidingHook()
{
    for (auto& hidden : hiddenIds_) hidden.store(false);
    instance_ = this;
}

PoseHidingHook::~PoseHidingHook()
{
    Shutdown();
    if (instance_ == this) instance_ = nullptr;
}

void PoseHidingHook::Initialize(vr::IVRDriverContext* driverContext)
{
    driverContext_ = driverContext;
}

bool PoseHidingHook::InstallForInterface(const char* interfaceVersion, HookSlot& slot, void* detour)
{
    vr::EVRInitError error = vr::VRInitError_None;
    void* interfaceObject = driverContext_->GetGenericInterface(interfaceVersion, &error);
    if (interfaceObject == nullptr || error != vr::VRInitError_None)
    {
        return true;
    }
    void** vtable = *reinterpret_cast<void***>(interfaceObject);
    slot.target = vtable[1];
    if (slot.target == hook006_.target && hook006_.installed)
    {
        slot.original = hook006_.original;
        slot.installed = false;
        return true;
    }
    MH_STATUS status = MH_CreateHook(slot.target, detour, reinterpret_cast<void**>(&slot.original));
    if (status != MH_OK)
    {
        SetFailure(MH_StatusToString(status));
        return false;
    }
    status = MH_EnableHook(slot.target);
    if (status != MH_OK)
    {
        MH_RemoveHook(slot.target);
        SetFailure(MH_StatusToString(status));
        return false;
    }
    slot.installed = true;
    return true;
}

bool PoseHidingHook::EnsureInstalled()
{
    if (hookInstalled_.load()) return true;
    if (state_.load() == control_protocol::PhysicalSourceHidingState::Failed) return false;
    if (driverContext_ == nullptr)
    {
        SetFailure("OpenVR driver context is unavailable.");
        return false;
    }
    MH_STATUS status = MH_Initialize();
    if (status != MH_OK && status != MH_ERROR_ALREADY_INITIALIZED)
    {
        SetFailure(MH_StatusToString(status));
        return false;
    }
    minHookInitialized_ = true;

    // _006 is required by the OpenVR snapshot TrackSwap builds against. _005
    // is hooked as well when present so older physical drivers are covered.
    if (!InstallForInterface("IVRServerDriverHost_006", hook006_, reinterpret_cast<void*>(&Detour006)))
    {
        Shutdown();
        SetFailure("Could not hook the OpenVR pose interface; another pose modifier may already own it.");
        return false;
    }
    if (!InstallForInterface("IVRServerDriverHost_005", hook005_, reinterpret_cast<void*>(&Detour005)))
    {
        Shutdown();
        SetFailure("Could not hook all available OpenVR pose interfaces; another pose modifier may already own one.");
        return false;
    }
    hookInstalled_.store(true);
    state_.store(control_protocol::PhysicalSourceHidingState::Waiting);
    {
        std::lock_guard<std::mutex> lock(errorMutex_);
        lastError_.fill('\0');
    }
    UpdateState();
    return true;
}

void PoseHidingHook::Shutdown()
{
    if (!minHookInitialized_ && !hook005_.installed && !hook006_.installed)
    {
        hookInstalled_.store(false);
        activeDeviceCount_.store(0);
        if (state_.load() != control_protocol::PhysicalSourceHidingState::Failed)
        {
            state_.store(control_protocol::PhysicalSourceHidingState::Disabled);
        }
        return;
    }
    for (HookSlot* slot : {&hook005_, &hook006_})
    {
        if (slot->installed && slot->target != nullptr)
        {
            MH_DisableHook(slot->target);
            MH_RemoveHook(slot->target);
        }
        *slot = HookSlot{};
    }
    if (minHookInitialized_)
    {
        MH_Uninitialize();
        minHookInitialized_ = false;
    }
    hookInstalled_.store(false);
    for (auto& hidden : hiddenIds_) hidden.store(false);
    activeDeviceCount_.store(0);
    if (state_.load() != control_protocol::PhysicalSourceHidingState::Failed)
    {
        state_.store(control_protocol::PhysicalSourceHidingState::Disabled);
    }
}

void PoseHidingHook::SetRequestedDeviceCount(std::uint8_t count)
{
    requestedDeviceCount_.store(count);
    if (count == 0)
    {
        for (auto& hidden : hiddenIds_) hidden.store(false);
        activeDeviceCount_.store(0);
    }
    UpdateState();
}

void PoseHidingHook::SetHiddenDeviceIds(
    const std::array<bool, vr::k_unMaxTrackedDeviceCount>& hiddenIds)
{
    std::uint8_t activeCount = 0;
    for (std::size_t index = 0; index < hiddenIds_.size(); ++index)
    {
        hiddenIds_[index].store(hiddenIds[index]);
        if (hiddenIds[index]) ++activeCount;
    }
    activeDeviceCount_.store(activeCount);
    UpdateState();
}

void PoseHidingHook::UpdateState()
{
    const std::uint8_t requested = requestedDeviceCount_.load();
    if (requested == 0)
    {
        state_.store(control_protocol::PhysicalSourceHidingState::Disabled);
    }
    else if (state_.load() == control_protocol::PhysicalSourceHidingState::Failed)
    {
        return;
    }
    else if (!hookInstalled_.load() || activeDeviceCount_.load() < requested)
    {
        state_.store(control_protocol::PhysicalSourceHidingState::Waiting);
    }
    else
    {
        state_.store(control_protocol::PhysicalSourceHidingState::Active);
    }
}

void PoseHidingHook::SetFailure(const char* message)
{
    for (auto& hidden : hiddenIds_) hidden.store(false);
    activeDeviceCount_.store(0);
    state_.store(control_protocol::PhysicalSourceHidingState::Failed);
    std::lock_guard<std::mutex> lock(errorMutex_);
    lastError_.fill('\0');
    if (message != nullptr)
    {
        strncpy_s(lastError_.data(), lastError_.size(), message, _TRUNCATE);
    }
}

bool PoseHidingHook::ShouldHide(std::uint32_t deviceIndex) const
{
    return deviceIndex < hiddenIds_.size() && hiddenIds_[deviceIndex].load();
}

void PoseHidingHook::Forward(
    HookSlot& slot,
    vr::IVRServerDriverHost* host,
    std::uint32_t deviceIndex,
    const vr::DriverPose_t* newPose,
    std::uint32_t poseSize)
{
    if (slot.original == nullptr || newPose == nullptr) return;
    if (poseSize == sizeof(vr::DriverPose_t) && instance_ != nullptr && instance_->ShouldHide(deviceIndex))
    {
        vr::DriverPose_t hiddenPose = *newPose;
        hiddenPose.vecWorldFromDriverTranslation[1] += HiddenHeightMetres;
        slot.original(host, deviceIndex, hiddenPose, poseSize);
        return;
    }
    slot.original(host, deviceIndex, *newPose, poseSize);
}

void PoseHidingHook::Detour005(
    vr::IVRServerDriverHost* host,
    std::uint32_t deviceIndex,
    const vr::DriverPose_t* newPose,
    std::uint32_t poseSize)
{
    if (instance_ != nullptr) Forward(instance_->hook005_, host, deviceIndex, newPose, poseSize);
}

void PoseHidingHook::Detour006(
    vr::IVRServerDriverHost* host,
    std::uint32_t deviceIndex,
    const vr::DriverPose_t* newPose,
    std::uint32_t poseSize)
{
    if (instance_ != nullptr) Forward(instance_->hook006_, host, deviceIndex, newPose, poseSize);
}

void PoseHidingHook::RestoreRawPose(
    vr::TrackedDeviceIndex_t deviceIndex,
    vr::TrackedDevicePose_t& pose)
{
    if (instance_ != nullptr && instance_->ShouldHide(deviceIndex))
    {
        pose.mDeviceToAbsoluteTracking.m[1][3] -= static_cast<float>(HiddenHeightMetres);
    }
}

control_protocol::PhysicalSourceHidingStatus PoseHidingHook::GetStatus() const
{
    control_protocol::PhysicalSourceHidingStatus status{};
    status.state = static_cast<std::uint8_t>(state_.load());
    status.requestedDeviceCount = requestedDeviceCount_.load();
    status.activeDeviceCount = activeDeviceCount_.load();
    status.hookInstalled = hookInstalled_.load() ? 1 : 0;
    std::lock_guard<std::mutex> lock(errorMutex_);
    std::memcpy(status.lastError, lastError_.data(), lastError_.size());
    return status;
}
} // namespace trackswap
