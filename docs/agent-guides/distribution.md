# Distribution and Release Rules

Read this file before changing the installer, uninstaller, Steam depot tooling, release packaging, registration cleanup, or release workflow.

## Installer and Uninstaller

- The user-facing product name is `TrackSwap VR`. Preserve compatibility identifiers as `TrackSwap`: executable and assembly names, namespaces, GitHub repository paths, installer `AppId`, install/data directories, named pipes, mutexes, manifest filename/app key, driver identifiers, configuration schema keys, and release archive filenames. Installer upgrades remove obsolete `TrackSwap` shortcuts while creating `TrackSwap VR` shortcuts.
- The primary Windows distribution is an unsigned Inno Setup installer until a maintainer adds code signing. Keep the complete ZIP as the portable/manual fallback.
- Install per user by default under `%LOCALAPPDATA%\Programs\TrackSwap`; do not require elevation for the normal path.
- Installation and uninstallation must refuse to mutate SteamVR integration while `vrserver`, `vrmonitor`, or `vrcompositor` is running.
- Installer upgrades must preserve user configuration, replace an existing TrackSwap driver registration safely, and update the registered TrackSwap application-manifest path without enabling UI auto-launch by default.
- Store TrackSwap-owned mutable data under the package root in `UserData`. Steam depot manifests and release packages must never include files inside this runtime-created directory, so Steam update, verification, and ordinary uninstall leave user configuration untouched. If the package root is not writable, fall back explicitly to `%LOCALAPPDATA%\TrackSwap` and expose that path and reason in the UI.
- When `UserData` is empty and recognized pre-v010 data exists under `%LOCALAPPDATA%\TrackSwap`, continue using the legacy directory until the user explicitly migrates it. Migration must copy to a same-volume staging directory, validate configuration before switching, preserve any displaced destination data, and restart UI/Runtime onto the new path. Offer legacy cleanup only after the new path is active; delete only recognized TrackSwap files and preserve unknown files.
- An ordinary Steam uninstall must remove the TrackSwap OpenVR driver registration, application-manifest and auto-launch records, and TrackSwap proxy `TrackingOverrides`, while leaving runtime-created `UserData` files in the remaining TrackSwap folder for reinstall. A separately labeled full-data removal action may also delete `UserData`, legacy `%LOCALAPPDATA%\TrackSwap`, TrackSwap-created SteamVR settings backups, shortcuts, uninstall registration, and the now-empty installation directory. Never delete shared SteamVR logs, Windows caches, unknown files, or unrelated OpenVR configuration.
- Steam distribution uses unpacked depot content, not the Inno installer. Keep a generated, preview-first SteamPipe build path with explicit AppID/DepotID inputs, mark `installscript.vdf` as the depot install script, exclude `UserData` and PDB files, and use one package-relative integration entry point for install and uninstall. Registration failure after partially installing integration must attempt rollback; uninstall cleanup must attempt both application and driver removal even if one fails.
- Keep installer registration and cleanup logic covered by an isolated fixture test. Shared JSON configuration must be changed atomically and unrelated applications, mappings, and settings must survive byte-semantics-equivalent rewrites.

## SteamVR Configuration Safety

- Do not write `steamvr.vrsettings` while SteamVR is running.
- Back up the settings file before applying, removing, or restoring mappings.
- Restore only `TrackingOverrides`; preserve unrelated SteamVR settings.
- Use exact OpenVR registered device paths, including the actual prefix returned by OpenVR. Never synthesize `/devices/lighthouse/` or another prefix from a serial number.
- Prevent self-maps, role self-reference, cycles, duplicate proxy slots, and conflicting target rules.
- Keep target input separate from the selected pose source.
- Treat concrete-device overrides as experimental because behavior can depend on the device driver and SteamVR version.
- Installation and removal of the SteamVR driver must be explicit commands or user actions. An ordinary build must never register a driver silently.

## Release Verification

- Before a release, repeat and document hardening for install, upgrade, disable, uninstall, SteamVR start/stop, standby, source power cycling, runtime/UI crashes, malformed configuration, device reconnection, multiple simultaneous physical sources, and configuration migration.
- Keep generated binaries, local SteamVR paths, test captures, machine-specific configuration, `UserData`, and PDB files out of release depots as required by the packaging rules.
- Keep the project MIT licensed under the name Hrenact. Record added third-party code or binary dependencies in `THIRD-PARTY-NOTICES.md` and preserve their required notices.
- Before every release, audit all third-party code, binaries, assets, tools that impose redistribution obligations, and referenced implementations against `THIRD-PARTY-NOTICES.md`. Verify exact package versions and pinned revisions rather than only project names. If any declaration is missing, stale, ambiguous, or lacks a required upstream notice, stop the release before tagging, packaging, or uploading and report every item that must be added or updated to the user. Never waive this gate or defer the correction until after release.
- Run `scripts/Test-ThirdPartyNotices.ps1` as a mandatory automated floor for that audit. Its success does not replace the manual review: the script can verify known and mechanically discoverable dependencies, but cannot prove that an undocumented copied snippet or newly referenced implementation does not exist.
- Self-contained .NET releases must copy `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` from the exact resolved `Microsoft.NETCore.App.Runtime.win-x64` package into the release Runtime directory as `DOTNET-LICENSE.txt` and `DOTNET-THIRD-PARTY-NOTICES.txt`. A missing or mismatched file is a release-blocking error.
- Ship `THIRD-PARTY-NOTICES.md` at the package root as a standalone legal document. Separately embed the repository file into the UI assembly at build time; the Settings legal-notice view must read only that immutable manifest resource and never the mutable package-root copy. Do not replace either form with a localized resource or generated translation.
- See `docs/steam-release-checklist.md` for the operational Steam release checklist.
