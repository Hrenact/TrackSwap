#include "control_server.h"

#include "control_protocol.h"
#include "tracker_registry.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>

namespace
{
constexpr DWORD BufferBytes = 4096;
constexpr auto IoTimeout = std::chrono::seconds(2);
constexpr auto IoPollInterval = std::chrono::milliseconds(20);

bool CompleteOverlapped(
    HANDLE pipe,
    OVERLAPPED& operation,
    DWORD* transferred,
    const std::atomic<bool>& stopping,
    const std::chrono::steady_clock::time_point* deadline)
{
    while (!stopping.load())
    {
        DWORD waitMilliseconds = static_cast<DWORD>(IoPollInterval.count());
        if (deadline != nullptr)
        {
            const auto now = std::chrono::steady_clock::now();
            if (now >= *deadline)
            {
                break;
            }
            const auto remaining = std::chrono::duration_cast<std::chrono::milliseconds>(*deadline - now);
            waitMilliseconds = static_cast<DWORD>(std::max<std::int64_t>(1, std::min<std::int64_t>(
                IoPollInterval.count(), remaining.count())));
        }

        const DWORD waitResult = WaitForSingleObject(operation.hEvent, waitMilliseconds);
        if (waitResult == WAIT_OBJECT_0)
        {
            DWORD resultBytes = 0;
            if (!GetOverlappedResult(pipe, &operation, &resultBytes, FALSE))
            {
                return false;
            }
            if (transferred != nullptr)
            {
                *transferred = resultBytes;
            }
            return true;
        }
        if (waitResult != WAIT_TIMEOUT)
        {
            break;
        }
    }

    CancelIoEx(pipe, &operation);
    WaitForSingleObject(operation.hEvent, INFINITE);
    DWORD ignored = 0;
    GetOverlappedResult(pipe, &operation, &ignored, FALSE);
    return false;
}

bool ConnectClient(HANDLE pipe, const std::atomic<bool>& stopping)
{
    OVERLAPPED operation{};
    operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (operation.hEvent == nullptr)
    {
        return false;
    }

    bool connected = ConnectNamedPipe(pipe, &operation) != FALSE;
    if (!connected)
    {
        const DWORD error = GetLastError();
        if (error == ERROR_PIPE_CONNECTED)
        {
            connected = true;
        }
        else if (error == ERROR_IO_PENDING)
        {
            connected = CompleteOverlapped(pipe, operation, nullptr, stopping, nullptr);
        }
    }

    CloseHandle(operation.hEvent);
    return connected;
}

bool ReadExactly(HANDLE pipe, void* destination, DWORD bytes, const std::atomic<bool>& stopping)
{
    auto* output = static_cast<unsigned char*>(destination);
    DWORD total = 0;
    const auto deadline = std::chrono::steady_clock::now() + IoTimeout;
    while (total < bytes && !stopping.load())
    {
        OVERLAPPED operation{};
        operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (operation.hEvent == nullptr)
        {
            return false;
        }
        DWORD read = 0;
        bool completed = ReadFile(pipe, output + total, bytes - total, nullptr, &operation) != FALSE;
        if (completed)
        {
            completed = GetOverlappedResult(pipe, &operation, &read, TRUE) != FALSE;
        }
        else if (GetLastError() == ERROR_IO_PENDING)
        {
            completed = CompleteOverlapped(pipe, operation, &read, stopping, &deadline);
        }
        CloseHandle(operation.hEvent);
        if (!completed || read == 0)
        {
            return false;
        }
        total += read;
    }

    return total == bytes;
}

bool WriteExactly(HANDLE pipe, const void* source, DWORD bytes, const std::atomic<bool>& stopping)
{
    const auto* input = static_cast<const unsigned char*>(source);
    DWORD total = 0;
    const auto deadline = std::chrono::steady_clock::now() + IoTimeout;
    while (total < bytes && !stopping.load())
    {
        OVERLAPPED operation{};
        operation.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        if (operation.hEvent == nullptr)
        {
            return false;
        }
        DWORD written = 0;
        bool completed = WriteFile(pipe, input + total, bytes - total, nullptr, &operation) != FALSE;
        if (completed)
        {
            completed = GetOverlappedResult(pipe, &operation, &written, TRUE) != FALSE;
        }
        else if (GetLastError() == ERROR_IO_PENDING)
        {
            completed = CompleteOverlapped(pipe, operation, &written, stopping, &deadline);
        }
        CloseHandle(operation.hEvent);
        if (!completed || written == 0)
        {
            return false;
        }
        total += written;
    }
    return total == bytes;
}

bool IsValidSourcePath(const char* path, std::size_t length)
{
    constexpr char Prefix[] = "/devices/";
    if (length < sizeof(Prefix) || std::memcmp(path, Prefix, sizeof(Prefix) - 1) != 0)
    {
        return false;
    }

    for (std::size_t index = 0; index < length; ++index)
    {
        const unsigned char value = static_cast<unsigned char>(path[index]);
        if (value == 0 || value < 0x20 || value == 0x7F)
        {
            return false;
        }
    }
    return true;
}

bool IsValidTargetPath(const char* path, std::size_t length)
{
    constexpr char Head[] = "/user/head";
    constexpr char Left[] = "/user/hand/left";
    constexpr char Right[] = "/user/hand/right";
    return IsValidSourcePath(path, length) ||
        (length == sizeof(Head) - 1 && std::memcmp(path, Head, length) == 0) ||
        (length == sizeof(Left) - 1 && std::memcmp(path, Left, length) == 0) ||
        (length == sizeof(Right) - 1 && std::memcmp(path, Right, length) == 0);
}
} // namespace

namespace trackswap
{
ControlServer::~ControlServer()
{
    Stop();
}

bool ControlServer::Start(TrackerRegistry* registry)
{
    if (registry == nullptr || worker_.joinable())
    {
        return false;
    }

    registry_ = registry;
    stopping_.store(false);
    worker_ = std::thread(&ControlServer::Run, this);
    return true;
}

void ControlServer::Stop()
{
    stopping_.store(true);
    if (worker_.joinable())
    {
        worker_.join();
    }
    registry_ = nullptr;
}

void ControlServer::Run()
{
    while (!stopping_.load())
    {
        HANDLE pipe = CreateNamedPipeW(
            control_protocol::PipePath,
            PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
            1,
            BufferBytes,
            BufferBytes,
            0,
            nullptr);
        if (pipe == INVALID_HANDLE_VALUE)
        {
            vr::VRDriverLog()->Log("TrackSwap failed to create its local driver control pipe.");
            return;
        }

        const bool connected = ConnectClient(pipe, stopping_);

        if (connected && !stopping_.load())
        {
            control_protocol::Header request{};
            std::array<char, control_protocol::MaximumPayloadBytes + 1> payload{};
            control_protocol::TelemetryBatch telemetry{};
            control_protocol::HapticFeedbackBatch hapticEvents{};
            bool telemetryResponse = false;
            bool hapticResponse = false;
            bool valid = ReadExactly(pipe, &request, sizeof(request), stopping_) &&
                request.magic == control_protocol::Magic &&
                request.version == control_protocol::Version &&
                request.payloadBytes > 0 &&
                request.payloadBytes <= control_protocol::MaximumPayloadBytes &&
                ReadExactly(pipe, payload.data(), request.payloadBytes, stopping_);

            if (valid && request.messageType == control_protocol::SetSourceMessageType)
            {
                valid = IsValidSourcePath(payload.data(), request.payloadBytes);
                if (valid)
                {
                    registry_->QueueSource(0, payload.data());
                }
            }
            else if (valid && request.messageType == control_protocol::SetOffsetMessageType)
            {
                constexpr std::size_t OffsetValueCount = 7;
                std::array<double, OffsetValueCount> values{};
                valid = request.payloadBytes == sizeof(values);
                if (valid)
                {
                    std::memcpy(values.data(), payload.data(), sizeof(values));
                    const pose_math::RigidOffset offset{
                        {values[0], values[1], values[2]},
                        {values[6], values[3], values[4], values[5]}};
                    valid = pose_math::IsValidOffset(offset);
                    if (valid)
                    {
                        registry_->QueueOffset(0, offset);
                    }
                }
            }
            else if (valid && request.messageType == control_protocol::ApplySnapshotMessageType)
            {
                constexpr std::size_t FixedBytes =
                    (2 * sizeof(std::uint8_t)) + sizeof(std::uint64_t) +
                    (2 * sizeof(std::uint16_t)) + (7 * sizeof(double));
                valid = request.payloadBytes >= FixedBytes;
                if (valid)
                {
                    const std::uint8_t slot = static_cast<std::uint8_t>(payload[0]);
                    const bool enabled = payload[1] != 0;
                    std::uint64_t revision = 0;
                    std::uint16_t sourcePathBytes = 0;
                    std::uint16_t targetPathBytes = 0;
                    constexpr std::size_t PrefixBytes = 2 * sizeof(std::uint8_t);
                    std::memcpy(&revision, payload.data() + PrefixBytes, sizeof(revision));
                    std::memcpy(
                        &sourcePathBytes,
                        payload.data() + PrefixBytes + sizeof(revision),
                        sizeof(sourcePathBytes));
                    std::memcpy(
                        &targetPathBytes,
                        payload.data() + PrefixBytes + sizeof(revision) + sizeof(sourcePathBytes),
                        sizeof(targetPathBytes));
                    valid = slot < control_protocol::MaximumRoutes &&
                        ((!enabled && sourcePathBytes == 0 && targetPathBytes == 0) ||
                         (enabled && sourcePathBytes > 0)) &&
                        request.payloadBytes == FixedBytes + sourcePathBytes + targetPathBytes;
                    if (valid)
                    {
                        const char* sourcePath = payload.data() + PrefixBytes + sizeof(revision) +
                            sizeof(sourcePathBytes) + sizeof(targetPathBytes);
                        const char* targetPath = sourcePath + sourcePathBytes;
                        valid = !enabled || (IsValidSourcePath(sourcePath, sourcePathBytes) &&
                            (targetPathBytes == 0 || IsValidTargetPath(targetPath, targetPathBytes)));
                        std::array<double, 7> values{};
                        std::memcpy(values.data(), targetPath + targetPathBytes, sizeof(values));
                        const pose_math::RigidOffset offset{
                            {values[0], values[1], values[2]},
                            {values[6], values[3], values[4], values[5]}};
                        valid = valid && pose_math::IsValidOffset(offset);
                        if (valid)
                        {
                            std::array<char, control_protocol::MaximumPayloadBytes + 1> terminatedSource{};
                            std::array<char, control_protocol::MaximumPayloadBytes + 1> terminatedTarget{};
                            std::memcpy(terminatedSource.data(), sourcePath, sourcePathBytes);
                            std::memcpy(terminatedTarget.data(), targetPath, targetPathBytes);
                            valid = registry_->QueueSnapshot(
                                slot,
                                enabled,
                                terminatedSource.data(),
                                terminatedTarget.data(),
                                offset,
                                revision);
                        }
                    }
                }
            }
            else if (valid && request.messageType == control_protocol::GetTelemetryMessageType)
            {
                valid = request.payloadBytes == 1;
                if (valid)
                {
                    telemetry = registry_->GetTelemetry();
                    telemetryResponse = true;
                }
            }
            else if (valid && request.messageType == control_protocol::ApplyControllerSnapshotMessageType)
            {
                constexpr std::size_t FixedBytes =
                    (3 * sizeof(std::uint8_t)) + sizeof(std::uint64_t) +
                    sizeof(std::int32_t) + sizeof(std::uint16_t) + (7 * sizeof(double));
                valid = request.payloadBytes >= FixedBytes;
                if (valid)
                {
                    const auto hand = static_cast<ControllerHand>(static_cast<std::uint8_t>(payload[0]));
                    const bool enabled = payload[1] != 0;
                    const std::uint8_t logicalSlot = static_cast<std::uint8_t>(payload[2]);
                    std::uint64_t revision = 0;
                    std::int32_t handSelectionPriority = 0;
                    std::uint16_t sourcePathBytes = 0;
                    std::memcpy(&revision, payload.data() + 3, sizeof(revision));
                    std::memcpy(
                        &handSelectionPriority,
                        payload.data() + 3 + sizeof(revision),
                        sizeof(handSelectionPriority));
                    std::memcpy(
                        &sourcePathBytes,
                        payload.data() + 3 + sizeof(revision) + sizeof(handSelectionPriority),
                        sizeof(sourcePathBytes));
                    valid = (hand == ControllerHand::Left || hand == ControllerHand::Right) &&
                        ((!enabled && sourcePathBytes == 0 && logicalSlot == 255) ||
                         (enabled && sourcePathBytes > 0 && logicalSlot < control_protocol::MaximumRoutes)) &&
                        request.payloadBytes == FixedBytes + sourcePathBytes;
                    const char* sourcePath = payload.data() + 3 + sizeof(revision) +
                        sizeof(handSelectionPriority) + sizeof(sourcePathBytes);
                    if (valid) valid = !enabled || IsValidSourcePath(sourcePath, sourcePathBytes);
                    std::array<double, 7> values{};
                    if (valid)
                    {
                        std::memcpy(values.data(), sourcePath + sourcePathBytes, sizeof(values));
                        const pose_math::RigidOffset offset{
                            {values[0], values[1], values[2]},
                            {values[6], values[3], values[4], values[5]}};
                        valid = pose_math::IsValidOffset(offset);
                        if (valid)
                        {
                            std::array<char, control_protocol::MaximumPayloadBytes + 1> terminatedSource{};
                            std::memcpy(terminatedSource.data(), sourcePath, sourcePathBytes);
                            valid = registry_->QueueControllerSnapshot(
                                hand,
                                enabled,
                                logicalSlot,
                                terminatedSource.data(),
                                handSelectionPriority,
                                offset,
                                revision);
                        }
                    }
                }
            }
            else if (valid && request.messageType == control_protocol::ApplyControllerInputMessageType)
            {
                valid = request.payloadBytes == sizeof(control_protocol::ControllerInputState);
                if (valid)
                {
                    control_protocol::ControllerInputState input{};
                    std::memcpy(&input, payload.data(), sizeof(input));
                    valid = (input.hand == static_cast<std::uint8_t>(ControllerHand::Left) ||
                             input.hand == static_cast<std::uint8_t>(ControllerHand::Right)) &&
                        std::isfinite(input.joystickX) && input.joystickX >= -1.0F && input.joystickX <= 1.0F &&
                        std::isfinite(input.joystickY) && input.joystickY >= -1.0F && input.joystickY <= 1.0F &&
                        std::isfinite(input.triggerValue) && input.triggerValue >= 0.0F && input.triggerValue <= 1.0F &&
                        std::isfinite(input.gripValue) && input.gripValue >= 0.0F && input.gripValue <= 1.0F &&
                        input.joystickClick <= 1 &&
                        input.primaryButton <= 1 && input.secondaryButton <= 1 && input.menuButton <= 1;
                    if (valid) valid = registry_->QueueControllerInput(input);
                }
            }
            else if (valid && request.messageType == control_protocol::GetHapticEventsMessageType)
            {
                valid = request.payloadBytes == 1;
                if (valid)
                {
                    hapticEvents = registry_->GetHapticEvents();
                    hapticResponse = true;
                }
            }
            else
            {
                valid = false;
            }

            constexpr char Accepted[] = "accepted";
            constexpr char Rejected[] = "rejected";
            const char* responseText = valid ? Accepted : Rejected;
            const void* responsePayload = telemetryResponse
                ? static_cast<const void*>(&telemetry)
                : hapticResponse
                    ? static_cast<const void*>(&hapticEvents)
                : static_cast<const void*>(responseText);
            const std::uint32_t responseBytes = telemetryResponse
                ? static_cast<std::uint32_t>(sizeof(telemetry))
                : hapticResponse
                    ? static_cast<std::uint32_t>(sizeof(hapticEvents))
                : static_cast<std::uint32_t>(std::strlen(responseText));
            control_protocol::Header response{
                control_protocol::Magic,
                control_protocol::Version,
                static_cast<std::uint16_t>(request.messageType | control_protocol::ResponseFlag),
                responseBytes,
                request.requestId};
            const bool responseWritten =
                WriteExactly(pipe, &response, sizeof(response), stopping_) &&
                WriteExactly(pipe, responsePayload, response.payloadBytes, stopping_);
            if (responseWritten)
            {
                std::uint8_t acknowledgement = 0;
                ReadExactly(pipe, &acknowledgement, sizeof(acknowledgement), stopping_);
            }
        }

        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
}
} // namespace trackswap
