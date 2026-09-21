#include "runtime_launcher.h"

#include <filesystem>
#include <string>
#include <vector>

#include <windows.h>

namespace
{
std::filesystem::path GetDriverModulePath()
{
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(
            GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&trackswap::LaunchRuntimeForSteamVrSession),
            &module))
    {
        return {};
    }

    std::vector<wchar_t> buffer(32768U, L'\0');
    const DWORD length = GetModuleFileNameW(module, buffer.data(), static_cast<DWORD>(buffer.size()));
    if (length == 0U || length >= buffer.size())
    {
        return {};
    }
    return std::filesystem::path(std::wstring(buffer.data(), length));
}

std::filesystem::path FindRuntimeExecutable()
{
    std::filesystem::path ancestor = GetDriverModulePath();
    for (int level = 0; level < 5 && !ancestor.empty(); ++level)
    {
        ancestor = ancestor.parent_path();
    }
    if (ancestor.empty())
    {
        return {};
    }

    const std::filesystem::path packaged = ancestor / "runtime" / "TrackSwap.Runtime.exe";
    std::error_code error;
    if (std::filesystem::is_regular_file(packaged, error))
    {
        return packaged;
    }

    const std::filesystem::path repository = ancestor.parent_path();
    const std::filesystem::path development = repository / "src" / "TrackSwap.Runtime" /
        "bin" / "Release" / "net8.0-windows" / "TrackSwap.Runtime.exe";
    error.clear();
    return std::filesystem::is_regular_file(development, error)
        ? development
        : std::filesystem::path{};
}
} // namespace

namespace trackswap
{
bool LaunchRuntimeForSteamVrSession()
{
    const std::filesystem::path executable = FindRuntimeExecutable();
    if (executable.empty())
    {
        return false;
    }

    std::wstring commandLine = L"\"" + executable.wstring() + L"\"";
    std::vector<wchar_t> mutableCommandLine(commandLine.begin(), commandLine.end());
    mutableCommandLine.push_back(L'\0');

    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const std::wstring workingDirectory = executable.parent_path().wstring();
    const BOOL created = CreateProcessW(
        executable.c_str(),
        mutableCommandLine.data(),
        nullptr,
        nullptr,
        FALSE,
        CREATE_NO_WINDOW,
        nullptr,
        workingDirectory.c_str(),
        &startup,
        &process);
    if (!created)
    {
        return false;
    }

    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return true;
}
} // namespace trackswap
