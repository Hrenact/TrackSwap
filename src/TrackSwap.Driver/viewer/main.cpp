#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cstring>
#include <cstdint>
#include <cstdio>
#include <mutex>
#include <string>
#include <thread>

#include <d3d11.h>
#include <d3dcompiler.h>
#include <dwmapi.h>
#include <dxgi1_2.h>
#include <shobjidl.h>
#include <wincodec.h>
#include <windows.h>
#include <windowsx.h>
#include <wrl/client.h>

#include "shared_display_protocol.h"

using Microsoft::WRL::ComPtr;

namespace
{
constexpr wchar_t WindowClassName[] = L"TrackSwapVrViewerWindow";
constexpr wchar_t WindowTitle[] = L"TrackSwap VR 视图";
constexpr int TrackSwapIconResource = 101;
constexpr int LogicalToolbarHeight = 42;
constexpr int LogicalStatusHeight = 26;

enum class EyeMode { Both, Left, Right };
enum class OutputSize { Follow, Hd, FullHd, QuadHd, Source };

struct Vertex { float x, y, u, v; };

class ViewerApp
{
public:
    int Run(HINSTANCE instance);

private:
    static LRESULT CALLBACK WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    static LRESULT CALLBACK RenderProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    LRESULT HandleMessage(HWND window, UINT message, WPARAM wParam, LPARAM lParam);
    bool OpenSharedState();
    bool EnsureDevice();
    bool CreateDeviceForLuid(std::uint64_t luid);
    bool CreateRenderResources();
    bool OpenPublishedTextures();
    void ResizeSwapChain();
    void PumpFrame();
    void StartDragPump();
    void StopDragPump();
    void RenderLatestFrame();
    void PaintChrome();
    void HandleClick(int x, int y);
    void ToggleFullscreen();
    void ToggleTopmost();
    void CycleOutputSize();
    void ApplyOutputSize();
    bool SaveScreenshot();
    void CloseSharedState();
    int Scale(int logicalPixels) const;
    std::array<RECT, 7> ButtonRectangles() const;
    void InvalidateToolbar();
    void InvalidateStatus();
    std::wstring StatusText() const;
    const wchar_t* OutputSizeText() const;

    HINSTANCE instance_ = nullptr;
    HWND window_ = nullptr;
    HWND renderWindow_ = nullptr;
    HANDLE mapping_ = nullptr;
    trackswap::SharedDisplayState* state_ = nullptr;
    ComPtr<IDXGIAdapter1> adapter_;
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGISwapChain1> swapChain_;
    ComPtr<ID3D11RenderTargetView> renderTarget_;
    ComPtr<ID3D11VertexShader> vertexShader_;
    ComPtr<ID3D11PixelShader> pixelShader_;
    ComPtr<ID3D11InputLayout> inputLayout_;
    ComPtr<ID3D11Buffer> vertexBuffer_;
    ComPtr<ID3D11SamplerState> sampler_;
    std::array<ComPtr<ID3D11Texture2D>, trackswap::SharedDisplaySlotCount> textures_;
    std::array<ComPtr<IDXGIKeyedMutex>, trackswap::SharedDisplaySlotCount> mutexes_;
    std::array<ComPtr<ID3D11ShaderResourceView>, trackswap::SharedDisplaySlotCount> views_;
    ComPtr<ID3D11Texture2D> previewTexture_;
    ComPtr<ID3D11ShaderResourceView> previewView_;
    std::int64_t openedGeneration_ = -1;
    std::int64_t lastFrameId_ = 0;
    std::uint64_t displayedFrames_ = 0;
    std::uint64_t skippedFrames_ = 0;
    std::int64_t lastPublishedCount_ = 0;
    std::chrono::steady_clock::time_point fpsStart_ = std::chrono::steady_clock::now();
    double viewerFps_ = 0.0;
    EyeMode eyeMode_ = EyeMode::Both;
    OutputSize outputSize_ = OutputSize::Follow;
    bool topmost_ = false;
    bool fullscreen_ = false;
    UINT dpi_ = 96;
    std::chrono::steady_clock::time_point lastStatusPaint_ = std::chrono::steady_clock::now();
    std::mutex renderMutex_;
    std::atomic_bool stopDragPump_{false};
    std::thread dragPumpThread_;
    bool nonClientDragOwnsPump_ = false;
    WINDOWPLACEMENT savedPlacement_{sizeof(WINDOWPLACEMENT)};
    DWORD savedStyle_ = 0;
};

int ViewerApp::Run(HINSTANCE instance)
{
    instance_ = instance;
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    WNDCLASSEXW renderClass{sizeof(WNDCLASSEXW)};
    renderClass.hInstance = instance;
    renderClass.lpfnWndProc = RenderProcedure;
    renderClass.lpszClassName = L"TrackSwapVrViewerRender";
    renderClass.hCursor = LoadCursor(nullptr, IDC_ARROW);
    RegisterClassExW(&renderClass);

    WNDCLASSEXW windowClass{sizeof(WNDCLASSEXW)};
    windowClass.hInstance = instance;
    windowClass.lpfnWndProc = WindowProcedure;
    windowClass.lpszClassName = WindowClassName;
    windowClass.hCursor = LoadCursor(nullptr, IDC_ARROW);
    windowClass.hIcon = LoadIconW(instance, MAKEINTRESOURCEW(TrackSwapIconResource));
    windowClass.hIconSm = windowClass.hIcon;
    windowClass.hbrBackground = nullptr;
    RegisterClassExW(&windowClass);

    const UINT initialDpi = GetDpiForSystem();
    window_ = CreateWindowExW(0, WindowClassName, WindowTitle,
        WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, CW_USEDEFAULT, CW_USEDEFAULT,
        MulDiv(1100, initialDpi, 96), MulDiv(690, initialDpi, 96),
        nullptr, nullptr, instance, this);
    if (window_ == nullptr)
    {
        CoUninitialize();
        return 1;
    }
    BOOL dark = TRUE;
    DwmSetWindowAttribute(window_, 20, &dark, sizeof(dark));
    ShowWindow(window_, SW_SHOW);
    UpdateWindow(window_);

    MSG message{};
    bool running = true;
    while (running)
    {
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE))
        {
            if (message.message == WM_QUIT)
            {
                running = false;
                break;
            }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        if (!running) break;
        {
            std::lock_guard<std::mutex> lock(renderMutex_);
            PumpFrame();
        }
        MsgWaitForMultipleObjectsEx(0, nullptr, 2, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }
    StopDragPump();
    CloseSharedState();
    CoUninitialize();
    return static_cast<int>(message.wParam);
}

LRESULT CALLBACK ViewerApp::WindowProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    auto* app = reinterpret_cast<ViewerApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE)
    {
        app = static_cast<ViewerApp*>(reinterpret_cast<CREATESTRUCTW*>(lParam)->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
    }
    return app == nullptr ? DefWindowProcW(window, message, wParam, lParam) :
        app->HandleMessage(window, message, wParam, lParam);
}

LRESULT CALLBACK ViewerApp::RenderProcedure(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_ERASEBKGND) return 1;
    return DefWindowProcW(window, message, wParam, lParam);
}

LRESULT ViewerApp::HandleMessage(HWND window, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_CREATE:
        dpi_ = GetDpiForWindow(window);
        renderWindow_ = CreateWindowExW(0, L"TrackSwapVrViewerRender", nullptr,
            WS_CHILD | WS_VISIBLE, 0, Scale(LogicalToolbarHeight), 1, 1,
            window, nullptr, instance_, nullptr);
        return 0;
    case WM_SIZE:
    {
        std::lock_guard<std::mutex> lock(renderMutex_);
        const int width = LOWORD(lParam);
        const int height = HIWORD(lParam);
        const int toolbarHeight = Scale(LogicalToolbarHeight);
        const int statusHeight = Scale(LogicalStatusHeight);
        MoveWindow(renderWindow_, 0, toolbarHeight, width,
            std::max(1, height - toolbarHeight - statusHeight), TRUE);
        ResizeSwapChain();
        InvalidateToolbar();
        InvalidateStatus();
        return 0;
    }
    case WM_DPICHANGED:
    {
        dpi_ = HIWORD(wParam);
        const auto* suggested = reinterpret_cast<RECT*>(lParam);
        SetWindowPos(window, nullptr, suggested->left, suggested->top,
            suggested->right - suggested->left, suggested->bottom - suggested->top,
            SWP_NOACTIVATE | SWP_NOZORDER);
        return 0;
    }
    case WM_ERASEBKGND:
        return 1;
    case WM_NCLBUTTONDOWN:
        if (wParam == HTCAPTION || (wParam >= HTLEFT && wParam <= HTBOTTOMRIGHT))
        {
            // Take over before DefWindowProc enters Windows' nested move/size
            // loop, then keep the drag pump alive until that call truly
            // returns. This removes the one-frame gap at each hand-off edge.
            nonClientDragOwnsPump_ = true;
            StartDragPump();
            const auto result = DefWindowProcW(window, message, wParam, lParam);
            StopDragPump();
            {
                std::lock_guard<std::mutex> lock(renderMutex_);
                PumpFrame();
            }
            nonClientDragOwnsPump_ = false;
            return result;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    case WM_ENTERSIZEMOVE:
        if (!nonClientDragOwnsPump_) StartDragPump();
        return 0;
    case WM_EXITSIZEMOVE:
        if (!nonClientDragOwnsPump_)
        {
            StopDragPump();
            std::lock_guard<std::mutex> lock(renderMutex_);
            PumpFrame();
        }
        return 0;
    case WM_PAINT:
        PaintChrome();
        return 0;
    case WM_LBUTTONUP:
        HandleClick(GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam));
        return 0;
    case WM_KEYDOWN:
        if (wParam == VK_F11) ToggleFullscreen();
        else if (wParam == '1') eyeMode_ = EyeMode::Both;
        else if (wParam == '2') eyeMode_ = EyeMode::Left;
        else if (wParam == '3') eyeMode_ = EyeMode::Right;
        else if (wParam == 'T') ToggleTopmost();
        else if (wParam == 'S' && (GetKeyState(VK_CONTROL) & 0x8000) != 0) SaveScreenshot();
        InvalidateToolbar();
        return 0;
    case WM_CLOSE:
        StopDragPump();
        if (state_ != nullptr)
        {
            InterlockedExchange64(reinterpret_cast<volatile LONG64*>(&state_->viewerProcessId), 0);
            InterlockedExchange64(reinterpret_cast<volatile LONG64*>(&state_->viewerHeartbeatMilliseconds), 0);
        }
        DestroyWindow(window);
        return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    default:
        return DefWindowProcW(window, message, wParam, lParam);
    }
}

bool ViewerApp::OpenSharedState()
{
    if (state_ != nullptr && state_->magic == trackswap::SharedDisplayMagic) return true;
    CloseSharedState();
    mapping_ = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, trackswap::SharedDisplayMappingName);
    if (mapping_ == nullptr) return false;
    state_ = static_cast<trackswap::SharedDisplayState*>(MapViewOfFile(
        mapping_, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(trackswap::SharedDisplayState)));
    if (state_ == nullptr || state_->magic != trackswap::SharedDisplayMagic ||
        state_->version != trackswap::SharedDisplayVersion)
    {
        CloseSharedState();
        return false;
    }
    return true;
}

void ViewerApp::CloseSharedState()
{
    views_.fill(nullptr);
    mutexes_.fill(nullptr);
    textures_.fill(nullptr);
    previewView_.Reset();
    previewTexture_.Reset();
    openedGeneration_ = -1;
    if (state_ != nullptr) { UnmapViewOfFile(state_); state_ = nullptr; }
    if (mapping_ != nullptr) { CloseHandle(mapping_); mapping_ = nullptr; }
}

bool ViewerApp::EnsureDevice()
{
    if (device_ && state_ != nullptr && state_->adapterLuid == 0) return false;
    if (!device_ && !CreateDeviceForLuid(state_->adapterLuid)) return false;
    if (!swapChain_ && !CreateRenderResources()) return false;
    return OpenPublishedTextures();
}

bool ViewerApp::CreateDeviceForLuid(std::uint64_t packedLuid)
{
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(&factory)))) return false;
    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIAdapter1> candidate;
        if (factory->EnumAdapters1(index, &candidate) == DXGI_ERROR_NOT_FOUND) break;
        DXGI_ADAPTER_DESC1 description{};
        candidate->GetDesc1(&description);
        const auto luid = static_cast<std::uint64_t>(static_cast<std::uint32_t>(description.AdapterLuid.LowPart)) |
            (static_cast<std::uint64_t>(static_cast<std::uint32_t>(description.AdapterLuid.HighPart)) << 32U);
        if (luid == packedLuid) { adapter_ = candidate; break; }
    }
    if (!adapter_) return false;
    const D3D_FEATURE_LEVEL levels[]{D3D_FEATURE_LEVEL_11_1, D3D_FEATURE_LEVEL_11_0};
    D3D_FEATURE_LEVEL created{};
    const auto result = D3D11CreateDevice(adapter_.Get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, ARRAYSIZE(levels), D3D11_SDK_VERSION,
        &device_, &created, &context_);
    if (FAILED(result)) return false;
    ComPtr<IDXGIDevice1> dxgiDevice;
    if (SUCCEEDED(device_.As(&dxgiDevice))) dxgiDevice->SetMaximumFrameLatency(1);
    return true;
}

bool ViewerApp::CreateRenderResources()
{
    ComPtr<IDXGIDevice> dxgiDevice;
    ComPtr<IDXGIAdapter> adapter;
    ComPtr<IDXGIFactory2> factory;
    if (FAILED(device_.As(&dxgiDevice)) || FAILED(dxgiDevice->GetAdapter(&adapter)) ||
        FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) return false;
    RECT rectangle{};
    GetClientRect(renderWindow_, &rectangle);
    DXGI_SWAP_CHAIN_DESC1 description{};
    description.Width = std::max(1L, rectangle.right);
    description.Height = std::max(1L, rectangle.bottom);
    description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    description.SampleDesc.Count = 1;
    description.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    description.BufferCount = 3;
    description.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    if (FAILED(factory->CreateSwapChainForHwnd(device_.Get(), renderWindow_, &description,
        nullptr, nullptr, &swapChain_))) return false;

    static constexpr char shader[] =
        "struct V{float2 p:POSITION;float2 u:TEXCOORD0;};"
        "struct P{float4 p:SV_POSITION;float2 u:TEXCOORD0;};"
        "P vs(V i){P o;o.p=float4(i.p,0,1);o.u=i.u;return o;}"
        "Texture2D t:register(t0);SamplerState s:register(s0);"
        "float4 ps(P i):SV_TARGET{return t.Sample(s,i.u);}";
    ComPtr<ID3DBlob> vsCode, psCode, errors;
    if (FAILED(D3DCompile(shader, sizeof(shader), nullptr, nullptr, nullptr, "vs", "vs_5_0", 0, 0, &vsCode, &errors)) ||
        FAILED(D3DCompile(shader, sizeof(shader), nullptr, nullptr, nullptr, "ps", "ps_5_0", 0, 0, &psCode, &errors)) ||
        FAILED(device_->CreateVertexShader(vsCode->GetBufferPointer(), vsCode->GetBufferSize(), nullptr, &vertexShader_)) ||
        FAILED(device_->CreatePixelShader(psCode->GetBufferPointer(), psCode->GetBufferSize(), nullptr, &pixelShader_))) return false;
    const D3D11_INPUT_ELEMENT_DESC layout[]{
        {"POSITION", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 0, D3D11_INPUT_PER_VERTEX_DATA, 0},
        {"TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT, 0, 8, D3D11_INPUT_PER_VERTEX_DATA, 0}};
    if (FAILED(device_->CreateInputLayout(layout, ARRAYSIZE(layout), vsCode->GetBufferPointer(),
        vsCode->GetBufferSize(), &inputLayout_))) return false;
    D3D11_BUFFER_DESC buffer{};
    buffer.ByteWidth = sizeof(Vertex) * 6;
    buffer.Usage = D3D11_USAGE_DYNAMIC;
    buffer.BindFlags = D3D11_BIND_VERTEX_BUFFER;
    buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    if (FAILED(device_->CreateBuffer(&buffer, nullptr, &vertexBuffer_))) return false;
    D3D11_SAMPLER_DESC sampler{};
    sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
    sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    if (FAILED(device_->CreateSamplerState(&sampler, &sampler_))) return false;
    ResizeSwapChain();
    return true;
}

bool ViewerApp::OpenPublishedTextures()
{
    const auto generation = InterlockedCompareExchange64(
        reinterpret_cast<volatile LONG64*>(&state_->generation), 0, 0);
    if (generation == openedGeneration_ && textures_[0]) return true;
    views_.fill(nullptr); mutexes_.fill(nullptr); textures_.fill(nullptr);
    previewView_.Reset(); previewTexture_.Reset();
    if (generation <= 0 || state_->width == 0 || state_->height == 0) return false;
    for (std::uint32_t slot = 0; slot < trackswap::SharedDisplaySlotCount; ++slot)
    {
        const auto handle = reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(state_->textureHandles[slot]));
        if (handle == nullptr || FAILED(device_->OpenSharedResource(handle, IID_PPV_ARGS(&textures_[slot]))) ||
            FAILED(textures_[slot].As(&mutexes_[slot])) ||
            FAILED(device_->CreateShaderResourceView(textures_[slot].Get(), nullptr, &views_[slot])))
        {
            views_.fill(nullptr); mutexes_.fill(nullptr); textures_.fill(nullptr);
            return false;
        }
    }
    D3D11_TEXTURE2D_DESC previewDescription{};
    textures_[0]->GetDesc(&previewDescription);
    previewDescription.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    previewDescription.CPUAccessFlags = 0;
    previewDescription.MiscFlags = 0;
    previewDescription.Usage = D3D11_USAGE_DEFAULT;
    if (FAILED(device_->CreateTexture2D(&previewDescription, nullptr, &previewTexture_)) ||
        FAILED(device_->CreateShaderResourceView(previewTexture_.Get(), nullptr, &previewView_)))
    {
        views_.fill(nullptr); mutexes_.fill(nullptr); textures_.fill(nullptr);
        previewView_.Reset(); previewTexture_.Reset();
        return false;
    }
    openedGeneration_ = generation;
    return true;
}

void ViewerApp::ResizeSwapChain()
{
    if (!swapChain_) return;
    context_->OMSetRenderTargets(0, nullptr, nullptr);
    renderTarget_.Reset();
    RECT rectangle{};
    GetClientRect(renderWindow_, &rectangle);
    if (rectangle.right <= 0 || rectangle.bottom <= 0) return;
    if (FAILED(swapChain_->ResizeBuffers(0, rectangle.right, rectangle.bottom,
        DXGI_FORMAT_UNKNOWN, 0))) return;
    ComPtr<ID3D11Texture2D> backBuffer;
    if (SUCCEEDED(swapChain_->GetBuffer(0, IID_PPV_ARGS(&backBuffer))))
        device_->CreateRenderTargetView(backBuffer.Get(), nullptr, &renderTarget_);
}

void ViewerApp::PumpFrame()
{
    if (OpenSharedState())
    {
        InterlockedExchange64(
            reinterpret_cast<volatile LONG64*>(&state_->viewerHeartbeatMilliseconds),
            static_cast<LONG64>(GetTickCount64()));
        InterlockedExchange64(
            reinterpret_cast<volatile LONG64*>(&state_->viewerProcessId),
            static_cast<LONG64>(GetCurrentProcessId()));
        if (EnsureDevice()) RenderLatestFrame();
    }

    const auto now = std::chrono::steady_clock::now();
    if (now - lastStatusPaint_ >= std::chrono::seconds(1))
    {
        lastStatusPaint_ = now;
        InvalidateStatus();
    }
}

void ViewerApp::StartDragPump()
{
    if (dragPumpThread_.joinable()) return;
    stopDragPump_.store(false, std::memory_order_release);
    dragPumpThread_ = std::thread([this]
    {
        while (!stopDragPump_.load(std::memory_order_acquire))
        {
            {
                std::lock_guard<std::mutex> lock(renderMutex_);
                PumpFrame();
            }
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
    });
}

void ViewerApp::StopDragPump()
{
    stopDragPump_.store(true, std::memory_order_release);
    if (dragPumpThread_.joinable()) dragPumpThread_.join();
}

void ViewerApp::RenderLatestFrame()
{
    if (!renderTarget_) return;
    std::uint32_t selected = trackswap::SharedDisplaySlotCount;
    std::int64_t selectedFrame = lastFrameId_;
    for (std::uint32_t slot = 0; slot < trackswap::SharedDisplaySlotCount; ++slot)
    {
        const auto frame = InterlockedCompareExchange64(
            reinterpret_cast<volatile LONG64*>(&state_->slotFrameIds[slot]), 0, 0);
        if (frame <= lastFrameId_ || mutexes_[slot]->AcquireSync(1, 0) != S_OK) continue;
        if (frame > selectedFrame)
        {
            if (selected != trackswap::SharedDisplaySlotCount) mutexes_[selected]->ReleaseSync(0);
            selected = slot;
            selectedFrame = frame;
        }
        else mutexes_[slot]->ReleaseSync(0);
    }
    if (selected == trackswap::SharedDisplaySlotCount) return;

    // Snapshot the shared slot before giving it back to SteamVR. The preview
    // swap chain must never sample a texture after the producer is allowed to
    // reuse that slot for a newer frame.
    context_->CopyResource(previewTexture_.Get(), textures_[selected].Get());
    context_->Flush();
    mutexes_[selected]->ReleaseSync(0);

    const auto publishedCount = InterlockedCompareExchange64(
        reinterpret_cast<volatile LONG64*>(&state_->presentedFrames), 0, 0);
    if (lastPublishedCount_ > 0 && publishedCount > lastPublishedCount_ + 1)
        skippedFrames_ += static_cast<std::uint64_t>(publishedCount - lastPublishedCount_ - 1);
    lastPublishedCount_ = publishedCount;
    float u0 = 0.0F, u1 = 1.0F;
    if (eyeMode_ == EyeMode::Left) u1 = 0.5F;
    else if (eyeMode_ == EyeMode::Right) u0 = 0.5F;
    const Vertex vertices[]{
        {-1,-1,u0,1},{-1,1,u0,0},{1,1,u1,0},
        {-1,-1,u0,1},{1,1,u1,0},{1,-1,u1,1}};
    D3D11_MAPPED_SUBRESOURCE mapped{};
    context_->Map(vertexBuffer_.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped);
    std::memcpy(mapped.pData, vertices, sizeof(vertices));
    context_->Unmap(vertexBuffer_.Get(), 0);

    RECT rectangle{};
    GetClientRect(renderWindow_, &rectangle);
    const float clientWidth = static_cast<float>(std::max(1L, rectangle.right));
    const float clientHeight = static_cast<float>(std::max(1L, rectangle.bottom));
    const float sourceWidth = static_cast<float>(state_->width) * (eyeMode_ == EyeMode::Both ? 1.0F : 0.5F);
    const float sourceHeight = static_cast<float>(state_->height);
    const float scale = std::min(clientWidth / sourceWidth, clientHeight / sourceHeight);
    D3D11_VIEWPORT viewport{};
    viewport.Width = sourceWidth * scale;
    viewport.Height = sourceHeight * scale;
    viewport.TopLeftX = (clientWidth - viewport.Width) * 0.5F;
    viewport.TopLeftY = (clientHeight - viewport.Height) * 0.5F;
    viewport.MinDepth = 0; viewport.MaxDepth = 1;
    const float black[]{0,0,0,1};
    context_->ClearRenderTargetView(renderTarget_.Get(), black);
    context_->OMSetRenderTargets(1, renderTarget_.GetAddressOf(), nullptr);
    context_->RSSetViewports(1, &viewport);
    const UINT stride = sizeof(Vertex), offset = 0;
    context_->IASetInputLayout(inputLayout_.Get());
    context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->IASetVertexBuffers(0, 1, vertexBuffer_.GetAddressOf(), &stride, &offset);
    context_->VSSetShader(vertexShader_.Get(), nullptr, 0);
    context_->PSSetShader(pixelShader_.Get(), nullptr, 0);
    context_->PSSetShaderResources(0, 1, previewView_.GetAddressOf());
    context_->PSSetSamplers(0, 1, sampler_.GetAddressOf());
    context_->Draw(6, 0);
    ID3D11ShaderResourceView* nullView = nullptr;
    context_->PSSetShaderResources(0, 1, &nullView);
    context_->Flush();
    // The virtual display and the desktop compositor have independent clocks.
    // Waiting for desktop v-sync here makes two nominally 60 Hz loops beat
    // against each other and periodically miss a published VR frame. Submit
    // immediately and let DWM select the latest queued preview frame instead.
    swapChain_->Present(0, 0);
    lastFrameId_ = selectedFrame;
    ++displayedFrames_;
    const auto now = std::chrono::steady_clock::now();
    const auto seconds = std::chrono::duration<double>(now - fpsStart_).count();
    if (seconds >= 1.0) { viewerFps_ = displayedFrames_ / seconds; displayedFrames_ = 0; fpsStart_ = now; }
}

void ViewerApp::PaintChrome()
{
    std::lock_guard<std::mutex> lock(renderMutex_);
    PAINTSTRUCT paint{};
    HDC dc = BeginPaint(window_, &paint);
    RECT client{}; GetClientRect(window_, &client);
    HBRUSH background = CreateSolidBrush(RGB(18,18,18));
    RECT toolbar{0, 0, client.right, Scale(LogicalToolbarHeight)};
    RECT statusArea{0, client.bottom - Scale(LogicalStatusHeight), client.right, client.bottom};
    FillRect(dc, &toolbar, background);
    FillRect(dc, &statusArea, background);
    DeleteObject(background);
    SetBkMode(dc, TRANSPARENT);
    SetTextColor(dc, RGB(225,225,225));
    HFONT font = CreateFontW(-Scale(15), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH, L"Segoe UI");
    auto oldFont = SelectObject(dc, font);
    const wchar_t* labels[]{L"双眼",L"左眼",L"右眼",OutputSizeText(),L"置顶",L"截图",L"全屏"};
    const auto boxes = ButtonRectangles();
    for (int index = 0; index < 7; ++index)
    {
        const bool selected = (index == 0 && eyeMode_ == EyeMode::Both) ||
            (index == 1 && eyeMode_ == EyeMode::Left) || (index == 2 && eyeMode_ == EyeMode::Right) ||
            (index == 4 && topmost_) || (index == 6 && fullscreen_);
        HBRUSH fill = CreateSolidBrush(selected ? RGB(35,74,120) : RGB(28,28,28));
        FillRect(dc, &boxes[index], fill); DeleteObject(fill);
        HPEN pen = CreatePen(PS_SOLID, 1, selected ? RGB(77,151,255) : RGB(55,55,55));
        auto oldPen = SelectObject(dc, pen); auto oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
        Rectangle(dc, boxes[index].left, boxes[index].top, boxes[index].right, boxes[index].bottom);
        SelectObject(dc, oldBrush); SelectObject(dc, oldPen); DeleteObject(pen);
        RECT textBox = boxes[index];
        DrawTextW(dc, labels[index], -1, &textBox, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
    std::wstring status = StatusText();
    RECT statusRect{Scale(12), client.bottom - Scale(LogicalStatusHeight),
        client.right - Scale(12), client.bottom};
    SetTextColor(dc, state_ != nullptr && lastFrameId_ > 0 ? RGB(80,210,145) : RGB(214,163,65));
    DrawTextW(dc, status.c_str(), -1, &statusRect, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    SelectObject(dc, oldFont); DeleteObject(font);
    EndPaint(window_, &paint);
}

std::wstring ViewerApp::StatusText() const
{
    if (state_ == nullptr) return L"等待 SteamVR 虚拟显示…";
    if (state_->width == 0 || openedGeneration_ < 0) return L"虚拟头显已连接，等待首帧…";
    wchar_t buffer[256]{};
    swprintf_s(buffer, L"● %.0f FPS   %ux%u   跳过 %llu 帧",
        viewerFps_, state_->width, state_->height,
        static_cast<unsigned long long>(skippedFrames_));
    return buffer;
}

const wchar_t* ViewerApp::OutputSizeText() const
{
    switch (outputSize_)
    {
    case OutputSize::Hd: return L"尺寸 1280×720";
    case OutputSize::FullHd: return L"尺寸 1920×1080";
    case OutputSize::QuadHd: return L"尺寸 2560×1440";
    case OutputSize::Source: return L"尺寸 原始";
    default: return L"尺寸 跟随窗口";
    }
}

void ViewerApp::HandleClick(int x, int y)
{
    const auto boxes = ButtonRectangles();
    POINT point{x, y};
    int selected = -1;
    for (int index = 0; index < static_cast<int>(boxes.size()); ++index)
        if (PtInRect(&boxes[index], point)) { selected = index; break; }
    if (selected == 0) eyeMode_ = EyeMode::Both;
    else if (selected == 1) eyeMode_ = EyeMode::Left;
    else if (selected == 2) eyeMode_ = EyeMode::Right;
    else if (selected == 3) CycleOutputSize();
    else if (selected == 4) ToggleTopmost();
    else if (selected == 5) SaveScreenshot();
    else if (selected == 6) ToggleFullscreen();
    InvalidateToolbar();
}

int ViewerApp::Scale(int logicalPixels) const
{
    return MulDiv(logicalPixels, static_cast<int>(dpi_), 96);
}

std::array<RECT, 7> ViewerApp::ButtonRectangles() const
{
    const RECT logical[]{
        {12,8,68,34},{72,8,128,34},{132,8,188,34},{202,8,346,34},
        {350,8,406,34},{410,8,466,34},{470,8,526,34}};
    std::array<RECT, 7> scaled{};
    for (std::size_t index = 0; index < scaled.size(); ++index)
        scaled[index] = {Scale(logical[index].left), Scale(logical[index].top),
            Scale(logical[index].right), Scale(logical[index].bottom)};
    return scaled;
}

void ViewerApp::InvalidateToolbar()
{
    RECT client{}; GetClientRect(window_, &client);
    RECT area{0, 0, client.right, Scale(LogicalToolbarHeight)};
    InvalidateRect(window_, &area, FALSE);
}

void ViewerApp::InvalidateStatus()
{
    RECT client{}; GetClientRect(window_, &client);
    RECT area{0, client.bottom - Scale(LogicalStatusHeight), client.right, client.bottom};
    InvalidateRect(window_, &area, FALSE);
}

void ViewerApp::ToggleTopmost()
{
    topmost_ = !topmost_;
    SetWindowPos(window_, topmost_ ? HWND_TOPMOST : HWND_NOTOPMOST, 0,0,0,0,
        SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
}

void ViewerApp::ToggleFullscreen()
{
    fullscreen_ = !fullscreen_;
    if (fullscreen_)
    {
        savedStyle_ = static_cast<DWORD>(GetWindowLongPtrW(window_, GWL_STYLE));
        GetWindowPlacement(window_, &savedPlacement_);
        MONITORINFO monitor{sizeof(MONITORINFO)};
        GetMonitorInfoW(MonitorFromWindow(window_, MONITOR_DEFAULTTONEAREST), &monitor);
        SetWindowLongPtrW(window_, GWL_STYLE, savedStyle_ & ~WS_OVERLAPPEDWINDOW);
        SetWindowPos(window_, HWND_TOP, monitor.rcMonitor.left, monitor.rcMonitor.top,
            monitor.rcMonitor.right - monitor.rcMonitor.left,
            monitor.rcMonitor.bottom - monitor.rcMonitor.top, SWP_FRAMECHANGED);
    }
    else
    {
        SetWindowLongPtrW(window_, GWL_STYLE, savedStyle_);
        SetWindowPlacement(window_, &savedPlacement_);
        SetWindowPos(window_, nullptr, 0,0,0,0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }
}

void ViewerApp::CycleOutputSize()
{
    outputSize_ = static_cast<OutputSize>((static_cast<int>(outputSize_) + 1) % 5);
    ApplyOutputSize();
}

void ViewerApp::ApplyOutputSize()
{
    if (outputSize_ == OutputSize::Follow || fullscreen_) return;
    int width = 1280, height = 720;
    if (outputSize_ == OutputSize::FullHd) { width = 1920; height = 1080; }
    else if (outputSize_ == OutputSize::QuadHd) { width = 2560; height = 1440; }
    else if (outputSize_ == OutputSize::Source && state_ != nullptr && state_->width > 0)
    { width = static_cast<int>(state_->width); height = static_cast<int>(state_->height); }
    RECT rectangle{0,0,width,height + Scale(LogicalToolbarHeight + LogicalStatusHeight)};
    AdjustWindowRectEx(&rectangle, static_cast<DWORD>(GetWindowLongPtrW(window_, GWL_STYLE)), FALSE, 0);
    SetWindowPos(window_, nullptr, 0,0, rectangle.right - rectangle.left,
        rectangle.bottom - rectangle.top, SWP_NOMOVE | SWP_NOZORDER);
}

bool ViewerApp::SaveScreenshot()
{
    std::lock_guard<std::mutex> lock(renderMutex_);
    if (!swapChain_) return false;
    ComPtr<ID3D11Texture2D> backBuffer;
    if (FAILED(swapChain_->GetBuffer(0, IID_PPV_ARGS(&backBuffer)))) return false;
    D3D11_TEXTURE2D_DESC description{}; backBuffer->GetDesc(&description);
    description.BindFlags = 0; description.MiscFlags = 0;
    description.Usage = D3D11_USAGE_STAGING; description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Texture2D> staging;
    if (FAILED(device_->CreateTexture2D(&description, nullptr, &staging))) return false;
    context_->CopyResource(staging.Get(), backBuffer.Get());
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(context_->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped))) return false;

    ComPtr<IFileSaveDialog> dialog;
    ComPtr<IShellItem> item;
    bool success = false;
    if (SUCCEEDED(CoCreateInstance(CLSID_FileSaveDialog, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&dialog))))
    {
        const COMDLG_FILTERSPEC filters[]{{L"PNG 图像", L"*.png"}};
        dialog->SetFileTypes(1, filters); dialog->SetDefaultExtension(L"png");
        dialog->SetFileName(L"TrackSwap-VR.png");
        if (SUCCEEDED(dialog->Show(window_)) && SUCCEEDED(dialog->GetResult(&item)))
        {
            PWSTR path = nullptr;
            if (SUCCEEDED(item->GetDisplayName(SIGDN_FILESYSPATH, &path)))
            {
                ComPtr<IWICImagingFactory> factory;
                ComPtr<IWICStream> stream;
                ComPtr<IWICBitmapEncoder> encoder;
                ComPtr<IWICBitmapFrameEncode> frame;
                ComPtr<IPropertyBag2> properties;
                if (SUCCEEDED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory))) &&
                    SUCCEEDED(factory->CreateStream(&stream)) && SUCCEEDED(stream->InitializeFromFilename(path, GENERIC_WRITE)) &&
                    SUCCEEDED(factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder)) &&
                    SUCCEEDED(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache)) &&
                    SUCCEEDED(encoder->CreateNewFrame(&frame, &properties)) && SUCCEEDED(frame->Initialize(properties.Get())) &&
                    SUCCEEDED(frame->SetSize(description.Width, description.Height)))
                {
                    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
                    if (SUCCEEDED(frame->SetPixelFormat(&format)) &&
                        SUCCEEDED(frame->WritePixels(description.Height, mapped.RowPitch,
                            mapped.RowPitch * description.Height, static_cast<BYTE*>(mapped.pData))) &&
                        SUCCEEDED(frame->Commit()) && SUCCEEDED(encoder->Commit())) success = true;
                }
                CoTaskMemFree(path);
            }
        }
    }
    context_->Unmap(staging.Get(), 0);
    return success;
}
} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int)
{
    HANDLE singleton = CreateMutexW(nullptr, TRUE, L"Local\\TrackSwap.VrViewer.Singleton");
    if (singleton == nullptr || GetLastError() == ERROR_ALREADY_EXISTS)
    {
        HWND existing = FindWindowW(WindowClassName, nullptr);
        if (existing != nullptr) { ShowWindow(existing, SW_RESTORE); SetForegroundWindow(existing); }
        if (singleton != nullptr) CloseHandle(singleton);
        return 0;
    }
    ViewerApp app;
    const int result = app.Run(instance);
    ReleaseMutex(singleton);
    CloseHandle(singleton);
    return result;
}
