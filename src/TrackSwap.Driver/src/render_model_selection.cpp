#include "render_model_selection.h"

#include <openvr_driver.h>

#include <array>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <string>
#include <system_error>

#include <Windows.h>

namespace
{
std::filesystem::path FindSteamVrRoot()
{
    std::array<wchar_t, 32768> executablePath{};
    const DWORD length = GetModuleFileNameW(
        nullptr,
        executablePath.data(),
        static_cast<DWORD>(executablePath.size()));
    if (length == 0 || length >= executablePath.size())
    {
        return {};
    }

    // The driver is loaded inside <SteamVR>/bin/win64/vrserver.exe.
    return std::filesystem::path(executablePath.data()).parent_path().parent_path().parent_path();
}

bool FileExists(const std::filesystem::path& path)
{
    std::error_code error;
    return std::filesystem::is_regular_file(path, error) && !error;
}

bool IsAvailable(const char* modelName, const char* vendorDriver)
{
    if (modelName == nullptr || modelName[0] == '\0')
    {
        return false;
    }

    const std::filesystem::path steamVrRoot = FindSteamVrRoot();
    if (steamVrRoot.empty())
    {
        return false;
    }

    const char* resourceModelName = modelName;
    if (modelName[0] == '{')
    {
        const char* namespaceEnd = std::strchr(modelName, '}');
        if (namespaceEnd != nullptr && namespaceEnd[1] != '\0')
        {
            resourceModelName = namespaceEnd + 1;
        }
    }

    const auto hasModelIn = [&](const std::filesystem::path& renderModelRoot)
    {
        const std::filesystem::path modelDirectory = renderModelRoot / resourceModelName;
        return FileExists(modelDirectory / (std::string(resourceModelName) + ".json")) ||
            FileExists(modelDirectory / (std::string(resourceModelName) + ".obj"));
    };

    if (hasModelIn(steamVrRoot / "resources" / "rendermodels"))
    {
        return true;
    }

    return vendorDriver != nullptr && vendorDriver[0] != '\0' && hasModelIn(
        steamVrRoot / "drivers" / vendorDriver / "resources" / "rendermodels");
}
}

namespace trackswap::render_models
{
const char* SelectPreferredOrFallback(const char* preferredModel, const char* vendorDriver)
{
    if (IsAvailable(preferredModel, vendorDriver))
    {
        return preferredModel;
    }

    char message[256]{};
    std::snprintf(
        message,
        sizeof(message),
        "TrackSwap VR render model '%s' is unavailable; using the built-in fallback.",
        preferredModel == nullptr ? "" : preferredModel);
    vr::VRDriverLog()->Log(message);
    return BuiltInFallback;
}
}
