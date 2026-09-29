#include "virtual_display_redirect.h"

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <iterator>
#include <thread>

#include <windows.h>

namespace trackswap
{
bool VirtualDisplayRedirect::Initialize()
{
    if (!InitializeSharedState())
    {
        return false;
    }
    Microsoft::WRL::ComPtr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))))
    {
        return false;
    }

    SIZE_T greatestDedicatedMemory = 0;
    for (UINT index = 0;; ++index)
    {
        Microsoft::WRL::ComPtr<IDXGIAdapter1> candidate;
        if (factory->EnumAdapters1(index, &candidate) == DXGI_ERROR_NOT_FOUND)
        {
            break;
        }
        DXGI_ADAPTER_DESC1 description{};
        if (FAILED(candidate->GetDesc1(&description)) ||
            (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0)
        {
            continue;
        }
        if (!adapter_ || description.DedicatedVideoMemory > greatestDedicatedMemory)
        {
            greatestDedicatedMemory = description.DedicatedVideoMemory;
            adapter_ = candidate;
            adapterLuid_ = static_cast<std::uint64_t>(static_cast<std::uint32_t>(description.AdapterLuid.LowPart)) |
                (static_cast<std::uint64_t>(static_cast<std::uint32_t>(description.AdapterLuid.HighPart)) << 32U);
        }
    }
    if (!adapter_)
    {
        return false;
    }

    D3D_FEATURE_LEVEL createdLevel{};
    const D3D_FEATURE_LEVEL requestedLevels[] =
    {
        D3D_FEATURE_LEVEL_11_1,
        D3D_FEATURE_LEVEL_11_0
    };
    if (FAILED(D3D11CreateDevice(
        adapter_.Get(),
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        requestedLevels,
        static_cast<UINT>(std::size(requestedLevels)),
        D3D11_SDK_VERSION,
        &device_,
        &createdLevel,
        &context_)))
    {
        Shutdown();
        return false;
    }

    const auto now = Clock::now();
    std::lock_guard<std::mutex> lock(timingMutex_);
    lastVsync_ = now;
    nextVsync_ = now;
    frameCounter_ = 0;
    return true;
}

void VirtualDisplayRedirect::Shutdown()
{
    ResetPublishedTextures();
    if (sharedState_ != nullptr)
    {
        sharedState_->magic = 0;
        UnmapViewOfFile(sharedState_);
        sharedState_ = nullptr;
    }
    if (sharedStateMapping_ != nullptr)
    {
        CloseHandle(sharedStateMapping_);
        sharedStateMapping_ = nullptr;
    }
    presentedTexture_.Reset();
    presentedHandle_ = 0;
    context_.Reset();
    device_.Reset();
    adapter_.Reset();
    adapterLuid_ = 0;
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
}

bool VirtualDisplayRedirect::IsValid() const
{
    return device_ && adapterLuid_ != 0 && sharedState_ != nullptr;
}

bool VirtualDisplayRedirect::InitializeSharedState()
{
    sharedStateMapping_ = CreateFileMappingW(
        INVALID_HANDLE_VALUE,
        nullptr,
        PAGE_READWRITE,
        0,
        static_cast<DWORD>(sizeof(SharedDisplayState)),
        SharedDisplayMappingName);
    if (sharedStateMapping_ == nullptr)
    {
        return false;
    }
    sharedState_ = static_cast<SharedDisplayState*>(MapViewOfFile(
        sharedStateMapping_, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedDisplayState)));
    if (sharedState_ == nullptr)
    {
        CloseHandle(sharedStateMapping_);
        sharedStateMapping_ = nullptr;
        return false;
    }
    std::memset(sharedState_, 0, sizeof(SharedDisplayState));
    sharedState_->magic = SharedDisplayMagic;
    sharedState_->version = SharedDisplayVersion;
    sharedState_->structureSize = sizeof(SharedDisplayState);
    sharedState_->slotCount = SharedDisplaySlotCount;
    return true;
}

void VirtualDisplayRedirect::ResetPublishedTextures()
{
    for (std::uint32_t slot = 0; slot < SharedDisplaySlotCount; ++slot)
    {
        publishedMutexes_[slot].Reset();
        publishedTextures_[slot].Reset();
        if (sharedState_ != nullptr)
        {
            sharedState_->textureHandles[slot] = 0;
            InterlockedExchange64(
                reinterpret_cast<volatile LONG64*>(&sharedState_->slotFrameIds[slot]), 0);
        }
    }
    if (sharedState_ != nullptr)
    {
        sharedState_->width = 0;
        sharedState_->height = 0;
        sharedState_->format = 0;
        InterlockedExchange64(
            reinterpret_cast<volatile LONG64*>(&sharedState_->publishedFrameId), 0);
    }
    nextPublishedSlot_ = 0;
}

bool VirtualDisplayRedirect::HasActiveViewer() const
{
    if (sharedState_ == nullptr)
    {
        return false;
    }
    const auto heartbeat = static_cast<std::uint64_t>(InterlockedCompareExchange64(
        reinterpret_cast<volatile LONG64*>(&sharedState_->viewerHeartbeatMilliseconds), 0, 0));
    const auto now = GetTickCount64();
    return heartbeat != 0 && now >= heartbeat && now - heartbeat <= 2000;
}

bool VirtualDisplayRedirect::EnsurePublishedTextures(const D3D11_TEXTURE2D_DESC& sourceDescription)
{
    if (sharedState_ == nullptr)
    {
        return false;
    }
    if (publishedTextures_[0] &&
        sharedState_->width == sourceDescription.Width &&
        sharedState_->height == sourceDescription.Height &&
        sharedState_->format == static_cast<std::uint32_t>(sourceDescription.Format))
    {
        return true;
    }

    ResetPublishedTextures();
    D3D11_TEXTURE2D_DESC description = sourceDescription;
    description.MipLevels = 1;
    description.ArraySize = 1;
    description.SampleDesc.Count = 1;
    description.SampleDesc.Quality = 0;
    description.Usage = D3D11_USAGE_DEFAULT;
    description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    description.CPUAccessFlags = 0;
    description.MiscFlags = D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;

    for (std::uint32_t slot = 0; slot < SharedDisplaySlotCount; ++slot)
    {
        if (FAILED(device_->CreateTexture2D(&description, nullptr, &publishedTextures_[slot])) ||
            FAILED(publishedTextures_[slot].As(&publishedMutexes_[slot])))
        {
            ResetPublishedTextures();
            return false;
        }
        Microsoft::WRL::ComPtr<IDXGIResource> resource;
        HANDLE sharedHandle = nullptr;
        if (FAILED(publishedTextures_[slot].As(&resource)) ||
            FAILED(resource->GetSharedHandle(&sharedHandle)) || sharedHandle == nullptr)
        {
            ResetPublishedTextures();
            return false;
        }
        sharedState_->textureHandles[slot] =
            static_cast<std::uint64_t>(reinterpret_cast<std::uintptr_t>(sharedHandle));
    }
    sharedState_->adapterLuid = adapterLuid_;
    sharedState_->width = description.Width;
    sharedState_->height = description.Height;
    sharedState_->format = static_cast<std::uint32_t>(description.Format);
    ++publishedGeneration_;
    InterlockedExchange64(
        reinterpret_cast<volatile LONG64*>(&sharedState_->generation),
        static_cast<LONG64>(publishedGeneration_));
    return true;
}

vr::EVRInitError VirtualDisplayRedirect::Activate(std::uint32_t objectId)
{
    objectId_ = objectId;
    const auto properties = vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId_);
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ModelNumber_String, "TrackSwap Virtual Display");
    vr::VRProperties()->SetStringProperty(properties, vr::Prop_ManufacturerName_String, "Hrenact");
    vr::VRProperties()->SetFloatProperty(properties, vr::Prop_SecondsFromVsyncToPhotons_Float, 0.0F);
    vr::VRProperties()->SetUint64Property(properties, vr::Prop_GraphicsAdapterLuid_Uint64, adapterLuid_);
    return vr::VRInitError_None;
}

void VirtualDisplayRedirect::Deactivate()
{
    objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    presentedTexture_.Reset();
    presentedHandle_ = 0;
}

void VirtualDisplayRedirect::EnterStandby() {}

void* VirtualDisplayRedirect::GetComponent(const char* version)
{
    return version != nullptr && std::strcmp(version, vr::IVRVirtualDisplay_Version) == 0
        ? static_cast<vr::IVRVirtualDisplay*>(this)
        : nullptr;
}

void VirtualDisplayRedirect::DebugRequest(const char* request, char* response, std::uint32_t responseSize)
{
    if (response == nullptr || responseSize == 0)
    {
        return;
    }
    response[0] = '\0';
    if (request != nullptr && std::strcmp(request, "stats") == 0)
    {
        sprintf_s(response, responseSize, "present=%llu wait=%llu shared=%llu dropped=%llu",
            static_cast<unsigned long long>(presentCount_.load()),
            static_cast<unsigned long long>(waitCount_.load()),
            static_cast<unsigned long long>(sharedState_ == nullptr ? 0 : sharedState_->presentedFrames),
            static_cast<unsigned long long>(droppedCount_.load()));
    }
}

vr::DriverPose_t VirtualDisplayRedirect::GetPose()
{
    vr::DriverPose_t pose{};
    pose.qWorldFromDriverRotation.w = 1.0;
    pose.qDriverFromHeadRotation.w = 1.0;
    pose.qRotation.w = 1.0;
    pose.result = vr::TrackingResult_Running_OK;
    pose.poseIsValid = true;
    pose.deviceIsConnected = true;
    return pose;
}

void VirtualDisplayRedirect::Present(const vr::PresentInfo_t* presentInfo, std::uint32_t presentInfoSize)
{
    if (presentInfo == nullptr || presentInfoSize < sizeof(vr::PresentInfo_t) || !device_)
    {
        return;
    }

    presentCount_.fetch_add(1, std::memory_order_relaxed);
    if (!HasActiveViewer())
    {
        return;
    }

    if (presentedHandle_ != presentInfo->backbufferTextureHandle)
    {
        presentedTexture_.Reset();
        presentedHandle_ = presentInfo->backbufferTextureHandle;
        if (presentedHandle_ != 0)
        {
            ID3D11Texture2D* openedTexture = nullptr;
            device_->OpenSharedResource(
                reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(presentedHandle_)),
                IID_PPV_ARGS(&openedTexture));
            presentedTexture_.Attach(openedTexture);
        }
    }

    if (!presentedTexture_)
    {
        droppedCount_.fetch_add(1, std::memory_order_relaxed);
        return;
    }

    D3D11_TEXTURE2D_DESC sourceDescription{};
    presentedTexture_->GetDesc(&sourceDescription);
    if (!EnsurePublishedTextures(sourceDescription))
    {
        droppedCount_.fetch_add(1, std::memory_order_relaxed);
        return;
    }

    std::uint32_t selectedSlot = SharedDisplaySlotCount;
    for (std::uint32_t attempt = 0; attempt < SharedDisplaySlotCount; ++attempt)
    {
        const auto slot = (nextPublishedSlot_ + attempt) % SharedDisplaySlotCount;
        if (publishedMutexes_[slot]->AcquireSync(0, 0) == S_OK)
        {
            selectedSlot = slot;
            break;
        }
    }
    if (selectedSlot == SharedDisplaySlotCount)
    {
        droppedCount_.fetch_add(1, std::memory_order_relaxed);
        return;
    }

    Microsoft::WRL::ComPtr<IDXGIKeyedMutex> sourceMutex;
    const bool hasSourceMutex = SUCCEEDED(presentedTexture_.As(&sourceMutex));
    if (hasSourceMutex && sourceMutex->AcquireSync(0, 0) != S_OK)
    {
        publishedMutexes_[selectedSlot]->ReleaseSync(0);
        droppedCount_.fetch_add(1, std::memory_order_relaxed);
        return;
    }

    context_->CopySubresourceRegion(
        publishedTextures_[selectedSlot].Get(), 0, 0, 0, 0,
        presentedTexture_.Get(), 0, nullptr);
    context_->Flush();
    if (hasSourceMutex)
    {
        sourceMutex->ReleaseSync(0);
    }
    const auto frameId = presentInfo->nFrameId == 0
        ? presentCount_.load(std::memory_order_relaxed)
        : presentInfo->nFrameId;
    publishedMutexes_[selectedSlot]->ReleaseSync(1);
    InterlockedExchange64(
        reinterpret_cast<volatile LONG64*>(&sharedState_->slotFrameIds[selectedSlot]),
        static_cast<LONG64>(frameId));
    InterlockedExchange64(
        reinterpret_cast<volatile LONG64*>(&sharedState_->publishedFrameId),
        static_cast<LONG64>(frameId));
    InterlockedIncrement64(
        reinterpret_cast<volatile LONG64*>(&sharedState_->presentedFrames));
    sharedState_->droppedFrames = static_cast<std::int64_t>(droppedCount_.load(std::memory_order_relaxed));
    nextPublishedSlot_ = (selectedSlot + 1) % SharedDisplaySlotCount;
}

void VirtualDisplayRedirect::WaitForPresent()
{
    constexpr auto frameInterval = std::chrono::duration<double>(1.0 / RefreshRateHz);
    Clock::time_point target;
    {
        std::lock_guard<std::mutex> lock(timingMutex_);
        const auto now = Clock::now();
        if (nextVsync_ <= now)
        {
            const auto overdue = now - nextVsync_;
            const auto intervals = static_cast<std::uint64_t>(
                std::chrono::duration<double>(overdue).count() * RefreshRateHz) + 1U;
            nextVsync_ += std::chrono::duration_cast<Clock::duration>(frameInterval * intervals);
        }
        target = nextVsync_;
    }
    std::this_thread::sleep_until(target);
    {
        std::lock_guard<std::mutex> lock(timingMutex_);
        lastVsync_ = target;
        nextVsync_ = target + std::chrono::duration_cast<Clock::duration>(frameInterval);
        ++frameCounter_;
    }
    waitCount_.fetch_add(1, std::memory_order_relaxed);
}

bool VirtualDisplayRedirect::GetTimeSinceLastVsync(float* secondsSinceLastVsync, std::uint64_t* frameCounter)
{
    if (secondsSinceLastVsync == nullptr || frameCounter == nullptr)
    {
        return false;
    }
    std::lock_guard<std::mutex> lock(timingMutex_);
    *secondsSinceLastVsync = static_cast<float>(std::max(
        0.0,
        std::chrono::duration<double>(Clock::now() - lastVsync_).count()));
    *frameCounter = frameCounter_;
    return true;
}
} // namespace trackswap
