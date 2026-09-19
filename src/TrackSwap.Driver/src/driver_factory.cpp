#include <cstring>
#include <array>

#include <openvr_driver.h>

#include "virtual_tracker.h"
#include "control_server.h"

namespace
{
class TrackSwapServerProvider final : public vr::IServerTrackedDeviceProvider
{
public:
    vr::EVRInitError Init(vr::IVRDriverContext* driverContext) override
    {
        VR_INIT_SERVER_DRIVER_CONTEXT(driverContext);
        std::array<char, 512> sourceDevicePath{};
        vr::EVRSettingsError settingsError = vr::VRSettingsError_None;
        vr::VRSettings()->GetString(
            "driver_trackswap",
            "sourceDevicePath",
            sourceDevicePath.data(),
            static_cast<std::uint32_t>(sourceDevicePath.size()),
            &settingsError);
        if (settingsError != vr::VRSettingsError_None)
        {
            vr::VRDriverLog()->Log("TrackSwap could not read driver_trackswap/sourceDevicePath; the virtual tracker will remain disconnected.");
            sourceDevicePath[0] = '\0';
        }

        virtualTracker_.ConfigureSource(sourceDevicePath.data());
        if (!vr::VRServerDriverHost()->TrackedDeviceAdded(
                trackswap::VirtualTracker::SerialNumber,
                vr::TrackedDeviceClass_GenericTracker,
                &virtualTracker_))
        {
            vr::VRDriverLog()->Log("TrackSwap failed to register its virtual tracker.");
            return vr::VRInitError_Driver_Failed;
        }

        if (!controlServer_.Start(&virtualTracker_))
        {
            vr::VRDriverLog()->Log("TrackSwap failed to start its driver control endpoint.");
            return vr::VRInitError_Driver_Failed;
        }

        vr::VRDriverLog()->Log(sourceDevicePath[0] == '\0'
            ? "TrackSwap virtual tracker registered without a sourceDevicePath."
            : "TrackSwap virtual tracker registered with a configured sourceDevicePath.");
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
        virtualTracker_.Update();
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
    trackswap::VirtualTracker virtualTracker_;
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
