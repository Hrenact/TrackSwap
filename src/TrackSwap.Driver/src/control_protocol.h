#pragma once

#include <cstddef>
#include <cstdint>

namespace trackswap::control_protocol
{
constexpr wchar_t PipePath[] = LR"(\\.\pipe\TrackSwap.Driver.v1)";
constexpr std::uint32_t Magic = 0x50575354;
constexpr std::uint16_t Version = 2;
constexpr std::uint16_t SetSourceMessageType = 1;
constexpr std::uint16_t SetOffsetMessageType = 2;
constexpr std::uint16_t ApplySnapshotMessageType = 3;
constexpr std::uint16_t GetTelemetryMessageType = 4;
constexpr std::uint16_t ResponseFlag = 0x8000;
constexpr std::size_t MaximumPayloadBytes = 4096;
constexpr std::size_t MaximumRoutes = 8;

#pragma pack(push, 1)
struct Header
{
    std::uint32_t magic;
    std::uint16_t version;
    std::uint16_t messageType;
    std::uint32_t payloadBytes;
    std::uint64_t requestId;
};

struct TelemetryPose
{
    std::uint8_t connected;
    std::uint8_t valid;
    std::int32_t trackingResult;
    double position[3];
    double rotation[4];
};

struct TelemetrySnapshot
{
    std::uint64_t sequence;
    std::uint64_t appliedRevision;
    TelemetryPose source;
    TelemetryPose output;
    TelemetryPose target;
};

struct TelemetryBatch
{
    std::uint8_t count;
    TelemetrySnapshot snapshots[MaximumRoutes];
};
#pragma pack(pop)

static_assert(sizeof(Header) == 20, "Driver control header layout changed.");
static_assert(sizeof(TelemetryPose) == 62, "Driver telemetry pose layout changed.");
static_assert(sizeof(TelemetrySnapshot) == 202, "Driver telemetry snapshot layout changed.");
static_assert(sizeof(TelemetryBatch) == 1617, "Driver telemetry batch layout changed.");
} // namespace trackswap::control_protocol
