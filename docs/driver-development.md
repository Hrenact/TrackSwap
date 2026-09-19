# OpenVR driver development loop

## Prerequisites

- Windows x64
- Visual Studio 2022 with **Desktop development with C++**
- CMake 3.24 or newer (the build script also discovers Visual Studio's bundled CMake)
- Git and network access for the first pinned OpenVR fetch
- SteamVR only for explicit registration and hardware checks

## Build and package

From the repository root:

```powershell
.\scripts\Build-Driver.ps1 -Configuration Debug
```

Use `-Clean` to remove only the repository's driver build/package directories
before rebuilding. The package is written to:

```text
artifacts\driver\trackswap
```

An ordinary solution or driver build never registers the driver with SteamVR.

## Register and remove explicitly

Fully exit SteamVR, then run:

```powershell
.\scripts\Install-Driver.ps1
```

The script discovers the runtime through `%LOCALAPPDATA%\openvr\openvrpaths.vrpath`,
refuses duplicate registration, and invokes SteamVR's own `vrpathreg.exe`.

To recover/remove the development registration, fully exit SteamVR and run:

```powershell
.\scripts\Uninstall-Driver.ps1
```

The Stage 1 provider registers `TRKSWAP-PROXY-00`. It reads the exact physical
OpenVR path from `driver_trackswap/sourceDevicePath`, mirrors that device's raw
pose, and deliberately reports invalid/disconnected tracking when the source is
missing or invalid. It does not alter `TrackingOverrides`.

Hardware validation is intentionally separate from build validation. Before a
test, obtain the exact registered device path from TrackSwap v001 while SteamVR
is running. Fully exit SteamVR before setting the development value or changing
driver registration. The maintainer-specific source path must never be committed.

After SteamVR is fully stopped, write the selected path only into the ignored
development package (not into `steamvr.vrsettings`), then register it:

```powershell
.\scripts\Set-PackagedDriverSource.ps1 -SourceDevicePath "/devices/<exact-path>"
.\scripts\Install-Driver.ps1
```

If SteamVR enters safe mode or the device behaves unexpectedly, exit SteamVR
and run `scripts\Uninstall-Driver.ps1`. This removes only the external driver
registration; it does not delete the package or modify `TrackingOverrides`.

## Runtime development loop

Run the independent Runtime with an explicit development configuration path:

```powershell
dotnet run --project src\TrackSwap.Runtime -c Release -- --run artifacts\runtime\runtime-config.json
```

In another terminal, atomically apply the current single-route milestone or
query status:

```powershell
dotnet run --project src\TrackSwap.Runtime -c Release -- --apply-single-route "/devices/<exact-path>"
dotnet run --project src\TrackSwap.Runtime -c Release -- --runtime-status
```

The Runtime saves through a same-directory temporary file and atomic replace,
keeps a `.previous` snapshot, and retries delivery to `TrackSwap.Driver.v1`.
Stopping the Runtime does not invalidate the driver's last accepted snapshot.
