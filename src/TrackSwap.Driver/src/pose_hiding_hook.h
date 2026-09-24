#pragma once

#include <array>
#include <atomic>
#include <cstdint>
#include <mutex>

#include <openvr_driver.h>

#include "control_protocol.h"

namespace trackswap
{
class PoseHidingHook final
{
public:
    PoseHidingHook();
    ~PoseHidingHook();

    void Initialize(vr::IVRDriverContext* driverContext);
    void Shutdown();
    bool EnsureInstalled();
    void SetRequestedDeviceCount(std::uint8_t count);
    void SetHiddenDeviceIds(const std::array<bool, vr::k_unMaxTrackedDeviceCount>& hiddenIds);
    control_protocol::PhysicalSourceHidingStatus GetStatus() const;

    static void RestoreRawPose(vr::TrackedDeviceIndex_t deviceIndex, vr::TrackedDevicePose_t& pose);

private:
    using PoseUpdatedFunction = void (*)(
        vr::IVRServerDriverHost*,
        std::uint32_t,
        const vr::DriverPose_t&,
        std::uint32_t);

    struct HookSlot
    {
        void* target = nullptr;
        PoseUpdatedFunction original = nullptr;
        bool installed = false;
    };

    bool InstallForInterface(const char* interfaceVersion, HookSlot& slot, void* detour);
    bool ShouldHide(std::uint32_t deviceIndex) const;
    void SetFailure(const char* message);
    void UpdateState();
    static void Detour005(vr::IVRServerDriverHost*, std::uint32_t, const vr::DriverPose_t*, std::uint32_t);
    static void Detour006(vr::IVRServerDriverHost*, std::uint32_t, const vr::DriverPose_t*, std::uint32_t);
    static void Forward(HookSlot&, vr::IVRServerDriverHost*, std::uint32_t, const vr::DriverPose_t*, std::uint32_t);

    static constexpr double HiddenHeightMetres = 9001.0;
    static PoseHidingHook* instance_;

    vr::IVRDriverContext* driverContext_ = nullptr;
    HookSlot hook005_{};
    HookSlot hook006_{};
    std::array<std::atomic<bool>, vr::k_unMaxTrackedDeviceCount> hiddenIds_{};
    std::atomic<std::uint8_t> requestedDeviceCount_{0};
    std::atomic<std::uint8_t> activeDeviceCount_{0};
    std::atomic<control_protocol::PhysicalSourceHidingState> state_{
        control_protocol::PhysicalSourceHidingState::Disabled};
    std::atomic<bool> hookInstalled_{false};
    bool minHookInitialized_ = false;
    mutable std::mutex errorMutex_;
    std::array<char, 256> lastError_{};
};
} // namespace trackswap
