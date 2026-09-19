# TrackSwap Repository Guide

This file records the current product baseline and the engineering constraints for future work in this repository. It applies to the entire repository unless a more specific `AGENTS.md` is added below a subdirectory.

## Product Baseline and Direction

TrackSwap has two deliberately separate workflows:

1. **Legacy static mapping**
   - The v001-compatible WPF workflow edits SteamVR `TrackingOverrides`.
   - It substitutes a pose source while leaving the target device's input handling intact.
   - Static `TrackingOverrides` do not provide a general position or rotation transform. Never describe them as supporting offsets.

2. **Runtime pose routing**
   - v002 introduced the independent Runtime, native OpenVR driver, live source switching, local rigid offsets, automatic calibration, telemetry, and a real-time 3D preview.
   - v003 added named route management and up to eight stable virtual proxies.
   - The current UI uses a configuration sidebar; each selected route owns its source, proxy slot, target, offset, calibration view, status, and always-visible 3D preview.

The normal runtime data path is:

`physical source -> native driver transform -> TrackSwap virtual proxy -> SteamVR target override`

The virtual-proxy-to-target relationship is a static bootstrap mapping. Changing a route's physical source or offset is a live Runtime operation and must not repeatedly rewrite `steamvr.vrsettings`.

## Branch and Release Policy

- `v001`, `v002`, and `v003` are published, immutable release tags. Never move or rewrite them.
- `main` currently remains the stable v001 line. Do not merge, retarget, or rewrite it without an explicit maintainer decision.
- Current post-v003 work remains on `v002-runtime` until the maintainer chooses the integration branch for the next release.
- The next release identifier is `v004`. Releases use one increasing sequence: `v001`, `v002`, `v003`, and so on. Do not introduce semantic-version labels or prerelease suffixes unless the maintainer changes this policy.
- Do not commit experimental driver/runtime work directly to `main`.
- Keep commits focused. Do not rewrite published history.
- The project is MIT licensed under the name Hrenact. Record added third-party code or binary dependencies in `THIRD-PARTY-NOTICES.md` and preserve their required notices.

## Component Responsibilities

Keep the components in this repository separate:

1. **TrackSwap UI**
   - Windows WPF control surface targeting .NET Framework 4.8.
   - Edits named routes and offsets, requests source switches, manages calibration profiles, displays status, and renders downsampled telemetry.
   - Retains the legacy static settings workflow under Settings.
   - Must not participate in the per-frame tracking path.

2. **TrackSwap Runtime**
   - Independent configuration and control process.
   - Owns persistent runtime configuration, IPC, validation, source discovery, calibration profiles, and telemetry fan-out.
   - Sends versioned, atomic snapshots for every virtual slot.
   - Must recover cleanly if the UI closes or reconnects.

3. **TrackSwap OpenVR Driver**
   - Minimal native SteamVR server driver exposing stable virtual GenericTrackers.
   - Reads raw poses from selected physical sources, applies transforms, and submits virtual poses.
   - Keeps the last valid configuration if Runtime or UI disconnects.
   - Keeps the per-frame path local, deterministic, allocation-light, and non-blocking. Never require a synchronous IPC round trip for every pose update.

## Route and Proxy Model

- Support at most eight virtual slots, identified by stable serials `TRKSWAP-PROXY-00` through `TRKSWAP-PROXY-07`.
- Register a proxy on demand when an enabled route first uses its slot. Do not expose unused slots merely because capacity exists.
- A route's user-facing name is independent from its route id and proxy slot. Names must be unique case-insensitively and remain renameable.
- New routes use the lowest available slot and a default name such as `新配置`, `新配置 (1)`, and so on.
- Disabled or missing routes must submit invalid/disconnected tracking rather than a stale healthy pose.
- Configuration revisions are atomic across all slots. An older snapshot must never replace a newer accepted revision.
- Reject duplicate slots, conflicting targets, cycles, and other ambiguous routing rules.
- Reject role self-reference such as a physical right-hand controller routed through a proxy back to `/user/hand/right`. SteamVR can feed the overridden target pose back as the source and freeze the route on a previous frame.
- A proxy's first target binding, target change, disable cleanup, or deletion cleanup may require editing `TrackingOverrides`; perform that edit only while SteamVR is fully stopped.
- Deleting a route while SteamVR runs must persist a pending-deletion state and keep the proxy output active. After SteamVR stops, remove the static mapping first and only then remove the Runtime route. Preserve the pending state on failure and allow cancellation.
- Hot source and offset changes for an already bootstrapped proxy must work without restarting SteamVR.

## UI Behavior

- Use the sidebar as the primary route navigation and management surface.
- With no routes, hide route controls and show the centered empty state `暂无配置，请新建。`.
- Keep the selected route's 3D preview visible; do not make it collapsible.
- The preview is observational. Closing, freezing, or overloading it must not affect submitted tracking poses.
- Show physical source, stable virtual proxy, target, connection health, active offset, and pending/applied state distinctly.
- Keep dangerous operations reversible and explain required SteamVR restarts or stopped-state writes before the user acts.
- Preserve target input from the original device; TrackSwap replaces pose, not controller input.
- Keep the legacy v001 settings workflow clearly separated from runtime route management.

## Pose Math Requirements

- Represent a configurable rigid offset as `T_output = T_source * T_offset`.
- Offset translation is expressed in the source device's local coordinate frame and measured in metres.
- Store quaternion components in `x / y / z / w` order and normalize validated input.
- Transform orientation and position together; do not implement translation as an unrelated world-space addition.
- When velocities are submitted, account for the offset lever arm. At minimum:
  `v_output = v_source + omega x (R_source * t_offset)`.
- Preserve valid/connected/tracking-result state deliberately. Never report a stale pose as healthy tracking.
- Calibration from simultaneously observed source and target poses uses:
  `T_offset = inverse(T_source) * T_target`.
- Capture calibration data before enabling an override that would hide the target's original pose.
- Reject unstable, insufficient, disconnected, or non-simultaneous calibration samples.

## SteamVR Configuration Safety

Preserve these behaviors unless a task explicitly replaces them with a safer equivalent:

- Do not write `steamvr.vrsettings` while SteamVR is running.
- Back up the settings file before applying, removing, or restoring mappings.
- Restore only `TrackingOverrides`; preserve unrelated SteamVR settings.
- Use exact OpenVR registered device paths, including the actual prefix returned by OpenVR. Never synthesize `/devices/lighthouse/` or another prefix from a serial number.
- Prevent self-maps, role self-reference, cycles, duplicate proxy slots, and conflicting target rules.
- Keep target input separate from the selected pose source.
- Treat concrete-device overrides as experimental because behavior can depend on the device driver and SteamVR version.
- Installation and removal of the SteamVR driver must be explicit commands or user actions. An ordinary build must never register a driver silently.

## Current Acceptance Baseline

The following capabilities have been implemented and must not regress:

- UI, Runtime/shared protocol, and native driver have separate project boundaries.
- The solution and native driver build reproducibly for Windows x64.
- Runtime, driver, and UI can restart independently without corrupting the saved configuration or crashing SteamVR.
- Stable virtual proxies mirror connected sources, preserve health state, and recover from source reconnection.
- Live source and offset updates work through versioned IPC.
- Automatic calibration profiles can be captured, named, saved, reapplied, and deleted.
- Downsampled per-route telemetry drives the source/output/target 3D preview outside the tracking-critical path.
- Multiple proxy slots can be registered in one SteamVR session and receive independent route snapshots.
- Deleting a route while SteamVR is stopped removes its static proxy mapping without disturbing other routes.
- Deleting a route while SteamVR is running marks it pending, preserves live tracking, and automatically removes its static mapping and Runtime route after SteamVR stops.
- Hardware testing has confirmed Tracker-to-hand pose replacement while the original hand controller keeps its input.

Before a v004 release, repeat and document hardening for install, upgrade, disable, uninstall, SteamVR start/stop, standby, source power cycling, runtime/UI crashes, malformed configuration, device reconnection, multiple simultaneous physical sources, and configuration migration.

## Development Practices

- Target Windows x64 for native driver/runtime integration. The UI targets .NET Framework 4.8.
- Prefer official OpenVR headers and documentation. Pin or record the exact dependency revision when vendoring or downloading build inputs.
- Validate all identifiers, slot indices, message lengths, transforms, and values crossing IPC boundaries.
- Log state transitions and actionable errors, but do not log high-frequency poses by default.
- Keep generated binaries, local SteamVR paths, test captures, and machine-specific configuration out of Git.
- Add focused tests for transform math, configuration migration and validation, IPC parsing, telemetry batching, slot routing, and route conflicts before relying on hardware-only tests.
- Verify changes proportionally: build affected projects, run automated tests, and state clearly which SteamVR or hardware checks still require the maintainer.
- Treat successful compilation and automated tests as necessary but insufficient for tracking correctness.

## Local Testing Notes

On the maintainer's current test machine, SteamVR has previously been found at `D:\Program Files (x86)\Steam\steamapps\common\SteamVR` and its settings at `D:\Program Files (x86)\Steam\config\steamvr.vrsettings`. These are test references only. Product code must discover paths dynamically and must not hard-code them.

When a hardware test is required, stop at a clear test boundary and give the maintainer exact setup, expected result, and recovery instructions. Never assume a successful build proves correct tracking behavior.
