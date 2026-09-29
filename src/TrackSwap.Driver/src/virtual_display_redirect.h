#pragma once

#include <atomic>
#include <chrono>
#include <cstdint>
#include <mutex>
#include <string>

#include <d3d11.h>
#include <dxgi1_2.h>
#include <openvr_driver.h>
#include <wrl/client.h>

#include "shared_display_protocol.h"

namespace trackswap
{
class VirtualDisplayRedirect final :
    public vr::ITrackedDeviceServerDriver,
    public vr::IVRVirtualDisplay
{
public:
    static constexpr const char* SerialNumber = "TRKSWAP-DISPLAY";

    bool Initialize();
    void Shutdown();
    bool IsValid() const;

    vr::EVRInitError Activate(std::uint32_t objectId) override;
    void Deactivate() override;
    void EnterStandby() override;
    void* GetComponent(const char* version) override;
    void DebugRequest(const char* request, char* response, std::uint32_t responseSize) override;
    vr::DriverPose_t GetPose() override;

    void Present(const vr::PresentInfo_t* presentInfo, std::uint32_t presentInfoSize) override;
    void WaitForPresent() override;
    bool GetTimeSinceLastVsync(float* secondsSinceLastVsync, std::uint64_t* frameCounter) override;

private:
    using Clock = std::chrono::steady_clock;
    static constexpr double RefreshRateHz = 60.0;

    bool InitializeSharedState();
    void ResetPublishedTextures();
    bool EnsurePublishedTextures(const D3D11_TEXTURE2D_DESC& sourceDescription);
    bool HasActiveViewer() const;

    std::uint64_t adapterLuid_ = 0;
    std::uint32_t objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    Microsoft::WRL::ComPtr<IDXGIAdapter1> adapter_;
    Microsoft::WRL::ComPtr<ID3D11Device> device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> presentedTexture_;
    vr::SharedTextureHandle_t presentedHandle_ = 0;
    HANDLE sharedStateMapping_ = nullptr;
    SharedDisplayState* sharedState_ = nullptr;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> publishedTextures_[SharedDisplaySlotCount];
    Microsoft::WRL::ComPtr<IDXGIKeyedMutex> publishedMutexes_[SharedDisplaySlotCount];
    std::uint32_t nextPublishedSlot_ = 0;
    std::uint64_t publishedGeneration_ = 0;
    std::atomic<std::uint64_t> droppedCount_{0};
    mutable std::mutex timingMutex_;
    Clock::time_point lastVsync_{};
    Clock::time_point nextVsync_{};
    std::uint64_t frameCounter_ = 0;
    std::atomic<std::uint64_t> presentCount_{0};
    std::atomic<std::uint64_t> waitCount_{0};
};
} // namespace trackswap
