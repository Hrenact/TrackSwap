# TrackSwap Verification Guide

Read this guide before declaring implementation, packaging, or hardware work complete.

## Acceptance Baseline

The following behavior must not regress:

- UI, Runtime/shared protocol, and native driver remain separate projects and build reproducibly for Windows x64.
- Runtime, driver, and UI restart independently without corrupting saved configuration or crashing SteamVR.
- Stable virtual outputs mirror connected sources, preserve health, and recover after source reconnection.
- Versioned IPC supports live source and offset updates. Multiple slots register in one session and receive independent snapshots.
- The route editor uses numeric offsets and the virtual-output gizmo; legacy values are interpreted as centimetres without migration.
- Downsampled telemetry drives a source/final-target preview outside the tracking-critical path. The proxy remains available to telemetry but is not rendered as a third user-facing device.
- Preview loading through `IVRRenderModels_006` supports static and component-based models and falls back by device class without affecting tracking.
- Direct outputs use the visible `trackswap_proxy_tracker` model; target-replacement carriers use `trackswap_hidden_proxy`. Hidden carriers retain valid tracking and mapping behavior.
- Virtual devices publish TrackSwap-owned 32 px state icons; never regress to SteamVR's generic tracker icon.
- Deletion while SteamVR is stopped removes only that route's static mapping. Deletion while running remains pending only when a mapping exists, preserves live tracking, and automatically finishes after SteamVR stops. Routes without mappings delete immediately.
- Hardware verification has confirmed tracker-to-hand pose replacement while the original controller retains input.

Before a release, document hardening for install, upgrade, disable, uninstall, SteamVR start/stop, standby, source power cycling, Runtime/UI crashes, malformed configuration, reconnection, multiple simultaneous sources, and configuration migration.

## Engineering Verification

- Target Windows x64 for native integration; UI targets .NET Framework 4.8.
- Prefer official OpenVR headers/docs and pin or record exact revisions for vendored/downloaded inputs.
- Validate identifiers, slot indices, message lengths, transforms, and values at every IPC boundary.
- Log state transitions and actionable errors, not high-frequency poses by default.
- Keep generated binaries, machine SteamVR paths, captures, and machine configuration out of Git.
- Add focused tests for transform math, migration/validation, IPC parsing, telemetry batching, slot routing, route conflicts, and interleaved snapshot ownership.
- Verify proportionally: build affected projects, run automated tests, and state which SteamVR/hardware checks remain for the maintainer.
- Compilation and automated tests are necessary but never sufficient evidence of tracking correctness or UI readability.

## Process Locks and Lifecycle

- If TrackSwap UI or Runtime locks Release outputs, the maintainer authorizes closing those TrackSwap-owned processes without another prompt. Prefer graceful shutdown; terminate only exact remaining TrackSwap processes and report what was closed.
- SteamVR lifecycle authorization is recorded in the repository root guide. Always verify exact processes, avoid unrelated applications, and announce lifecycle changes in progress updates.

## Maintainer Test Machine

SteamVR has previously been located at `D:\Program Files (x86)\Steam\steamapps\common\SteamVR` and settings at `D:\Program Files (x86)\Steam\config\steamvr.vrsettings`. These are test references only; product code must discover paths dynamically.

At a hardware boundary, give the maintainer exact setup, expected outcome, and recovery instructions. Never infer tracking correctness from a successful build alone.

