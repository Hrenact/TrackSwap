#include <cstring>
#include <array>

#include <openvr_driver.h>

#include "tracker_registry.h"
#include "control_server.h"

namespace
{
class TrackSwapServerProvider final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* driverContext) override
    {
        VR_INIT_SERVER_DRIVER_CONTEXT(driverContext);
        if (!controlServer_.Start(&trackerRegistry_))
        {
            vr::VRDriverLog()->Log("TrackSwap failed to start its driver control endpoint.");
            return vr::VRInitError_Driver_Failed;
        }

        vr::VRDriverLog()->Log("TrackSwap multi-route driver initialized; proxies will be registered on demand.");
        return vr::VRInitError_None;
    }

    void Cleanup() override
    {
        controlServer_.Stop();
        VR_CLEANUP_SERVER_DRIVER_CONTEXT();
    }

    const char* const* GetInterfaceVersions() override
    {
        return vr::k_InterfaceVersions;
    }

    void RunFrame() override
    {
        trackerRegistry_.RunFrame();
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
