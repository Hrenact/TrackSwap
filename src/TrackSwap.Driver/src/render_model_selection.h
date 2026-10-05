#pragma once

namespace trackswap::render_models
{
inline constexpr const char* BuiltInFallback = "{trackswap}trackswap_proxy_tracker";
inline constexpr const char* HiddenProxy = "{trackswap}trackswap_hidden_proxy";

// Resolves a preferred SteamVR render model once during device activation.
// When SteamVR does not provide that model, the packaged TrackSwap square is
// returned instead. vendorDriver is optional for models supplied by a driver
// rather than SteamVR's shared resources (for example, the HTC tracker model).
const char* SelectPreferredOrFallback(const char* preferredModel, const char* vendorDriver = nullptr);
}
