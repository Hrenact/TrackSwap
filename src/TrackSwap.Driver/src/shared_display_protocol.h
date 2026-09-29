#pragma once

#include <cstdint>

namespace trackswap
{
constexpr wchar_t SharedDisplayMappingName[] = L"Local\\TrackSwap.VirtualDisplay.v1";
constexpr std::uint32_t SharedDisplayMagic = 0x56535454U; // TTSV
constexpr std::uint32_t SharedDisplayVersion = 1;
constexpr std::uint32_t SharedDisplaySlotCount = 3;

struct alignas(8) SharedDisplayState
{
    std::uint32_t magic;
    std::uint32_t version;
    std::uint32_t structureSize;
    std::uint32_t slotCount;
    std::uint64_t adapterLuid;
    volatile std::int64_t generation;
    volatile std::int64_t publishedFrameId;
    volatile std::int64_t presentedFrames;
    volatile std::int64_t droppedFrames;
    volatile std::int64_t viewerHeartbeatMilliseconds;
    volatile std::int64_t viewerProcessId;
    std::uint32_t width;
    std::uint32_t height;
    std::uint32_t format;
    std::uint32_t reserved;
    std::uint64_t textureHandles[SharedDisplaySlotCount];
    volatile std::int64_t slotFrameIds[SharedDisplaySlotCount];
};
} // namespace trackswap
