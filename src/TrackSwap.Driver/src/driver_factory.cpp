#include <cstring>
#include <array>

#include <openvr_driver.h>

#include "tracker_registry.h"
#include "control_server.h"
#include "runtime_launcher.h"
#include "virtual_display_redirect.h"
#include "virtual_hmd_prototype.h"

namespace
{
class TrackSwapServerProvider final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* driverContext) override
    {
        VR_INIT_SERVER_DRIVER_CONTEXT(driverContext);
        trackerRegistry_.InitializePoseHiding(driverContext);
        trackerRegistry_.AttachVirtualHmd(&virtualHmd_);

        vr::EVRSettingsError virtualHmdSettingError = vr::VRSettingsError_None;
        const bool enableVirtualHmd = vr::VRSettings()->GetBool(
            "driver_trackswap",
            "enableVirtualHmd",
            &virtualHmdSettingError);
        if (virtualHmdSettingError == vr::VRSettingsError_None && enableVirtualHmd)
        {
            if (virtualDisplayRedirect_.Initialize())
            {
                virtualDisplayRedirectRegistered_ = vr::VRServerDriverHost()->TrackedDeviceAdded(
                    trackswap::VirtualDisplayRedirect::SerialNumber,
                    vr::TrackedDeviceClass_DisplayRedirect,
                    &virtualDisplayRedirect_);
            }
            virtualHmdRegistered_ = vr::VRServerDriverHost()->TrackedDeviceAdded(
                trackswap::VirtualHmd::SerialNumber,
                vr::TrackedDeviceClass_HMD,
                &virtualHmd_);
            vr::VRDriverLog()->Log(virtualHmdRegistered_
                ? "TrackSwap registered the virtual HMD."
                : "TrackSwap failed to register the virtual HMD.");
            vr::VRDriverLog()->Log(virtualDisplayRedirectRegistered_
                ? "TrackSwap registered the virtual display redirect."
                : "TrackSwap failed to register the virtual display redirect.");
        }
        if (!controlServer_.Start(&trackerRegistry_))
        {
            vr::VRDriverLog()->Log("TrackSwap failed to start its driver control endpoint.");
            return vr::VRInitError_Driver_Failed;
        }

        if (trackswap::LaunchRuntimeForSteamVrSession())
        {
            vr::VRDriverLog()->Log("TrackSwap requested its Runtime for this SteamVR session.");
        }
        else
        {
            vr::VRDriverLog()->Log("TrackSwap Runtime executable was not found beside the installed package.");
        }

        vr::VRDriverLog()->Log("TrackSwap multi-route driver initialized; proxies will be registered on demand.");
        return vr::VRInitError_None;
    }

    void Cleanup() override
    {
        controlServer_.Stop();
        trackerRegistry_.ShutdownPoseHiding();
        virtualHmdRegistered_ = false;
        virtualDisplayRedirectRegistered_ = false;
        virtualDisplayRedirect_.Shutdown();
        VR_CLEANUP_SERVER_DRIVER_CONTEXT();
    }

    const char* const* GetInterfaceVersions() override
    {
        return vr::k_InterfaceVersions;
    }

    void RunFrame() override
    {
        trackerRegistry_.RunFrame();
        if (virtualHmdRegistered_)
        {
            virtualHmd_.Update();
        }
    }

    bool ShouldBlockStandbyMode() override
    {
        return false;
    }

    void EnterStandby() override
    {
    }

    void LeaveStandby() override
    {
    }

private:
    trackswap::TrackerRegistry trackerRegistry_;
    trackswap::ControlServer controlServer_;
    trackswap::VirtualHmd virtualHmd_;
    trackswap::VirtualDisplayRedirect virtualDisplayRedirect_;
    bool virtualHmdRegistered_ = false;
    bool virtualDisplayRedirectRegistered_ = false;
};

TrackSwapServerProvider g_serverProvider;
} // namespace

extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* interfaceName, int* returnCode)
{
    if (interfaceName != nullptr &&
        std::strcmp(interfaceName, vr::IServerTrackedDeviceProvider_Version) == 0)
    {
        return &g_serverProvider;
    }

    if (returnCode != nullptr)
    {
        *returnCode = vr::VRInitError_Init_InterfaceNotFound;
    }

    return nullptr;
}
