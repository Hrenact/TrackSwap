# TrackSwap Repository Guide

This file records the agreed product direction and engineering constraints for future work in this repository. It applies to the entire repository unless a more specific `AGENTS.md` is added below a subdirectory.

## Product Direction

TrackSwap v001 is a stable WPF utility that edits SteamVR `TrackingOverrides`. It routes one tracked device's pose to another device while leaving the target device's input handling intact.

The v002 goal is a runtime pose-routing system with:

- hot switching between physical pose sources without restarting SteamVR;
- user-defined position and rotation offsets;
- a future real-time 3D pose and offset preview;
- the existing v001 configuration workflow retained as a compatible, clearly separated feature.

Do not describe static `TrackingOverrides` as supporting offsets. They substitute a pose source but do not provide a general position or rotation transform.

## Branch and Release Policy

- `main` is the stable v001 line until v002 is ready to merge.
- Develop the new runtime architecture on `v002-runtime`.
- Do not commit experimental driver/runtime work directly to `main`.
- Releases use a single increasing sequence: `v001`, `v002`, `v003`, and so on. Do not introduce semantic-version labels or prerelease suffixes unless the maintainer changes this policy.
- Keep commits focused. Do not rewrite published history or move an existing release tag.
- The project is MIT licensed under the name Hrenact. Record any added third-party code or binary dependencies in `THIRD-PARTY-NOTICES.md` and preserve their required notices.

## Intended v002 Architecture

Keep the components in this repository but separate their responsibilities:

1. **TrackSwap UI**
   - Windows WPF control surface.
   - Edits routes and offsets, requests source switches, displays status, and eventually renders preview telemetry.
   - Must not participate in the per-frame tracking path.

2. **TrackSwap Runtime**
   - Independent control/configuration process or service.
   - Owns persistent runtime configuration, IPC, validation, source discovery, and telemetry fan-out.
   - Must recover cleanly if the UI closes or reconnects.

3. **TrackSwap OpenVR Driver**
   - Minimal native SteamVR server driver exposing one or more stable virtual tracked devices.
   - Reads raw poses from the selected physical source, applies the transform, and submits the virtual pose.
   - Keep the per-frame path local and deterministic. Never require a synchronous IPC round trip for every pose update.

The normal data path is:

`physical source -> native driver transform -> TrackSwap virtual device -> SteamVR target override`

Use a stable virtual-device-to-target bootstrap mapping. Hot switching should change the physical source followed by that virtual device through IPC, not repeatedly rewrite `steamvr.vrsettings`.

## Pose Math Requirements

- Represent a configurable rigid offset as `T_output = T_source * T_offset`.
- Define and document the offset coordinate convention before exposing editable numeric fields.
- Transform orientation and position together; do not implement translation as an unrelated world-space addition.
- When velocities are submitted, account for the offset lever arm. At minimum:
  `v_output = v_source + omega x (R_source * t_offset)`.
- Preserve valid/connected/tracking-result state deliberately. Never report a stale pose as healthy tracking.
- Calibration from simultaneously observed source and target poses uses:
  `T_offset = inverse(T_source) * T_target`.
- Capture calibration data before enabling an override that would hide the target's original pose.

## Milestones and Acceptance Gates

Work through these stages in order. A later stage may be designed early, but should not complicate the tracking path before its prerequisite is proven.

### Stage 0 - Architecture and Technical Proof

- Add separate project boundaries for UI, runtime/shared protocol, and native driver.
- Confirm the chosen OpenVR headers/runtime interface and x64 build toolchain.
- Define virtual device identity, configuration schema, IPC framing, and pose coordinate conventions.
- Produce a minimal build/install/uninstall development loop for the driver.

Exit gate: the solution builds reproducibly and the component contracts are documented.

### Stage 1 - Minimal Virtual Device

- Register one stable virtual GenericTracker with SteamVR.
- Read one selected physical device's raw pose.
- Submit a pass-through pose without offset.
- Handle source disconnect/reconnect without crashing SteamVR.

Exit gate: the virtual device reliably mirrors a real device for an extended test session.

### Stage 2 - Hot Source Switching

- Switch the virtual device between connected physical sources at runtime.
- Do not restart SteamVR or rewrite settings for each switch.
- Define behavior for missing, sleeping, duplicate, and reconnected sources.
- Avoid discontinuities where practical; make any unavoidable jump explicit in the UI.

Exit gate: repeated source changes work during one SteamVR session without a driver or UI restart.

### Stage 3 - Real-Time Offset

- Apply editable translation and rotation offsets in the driver.
- Support live updates with validation and an explicit reset-to-identity action.
- Verify handedness, axes, quaternion order, multiplication order, and velocity behavior.

Exit gate: known offsets produce repeatable measured results across source orientations.

### Stage 4 - Runtime and IPC

- Move configuration ownership and device-selection control into the independent runtime.
- Use versioned messages and atomic configuration snapshots.
- Driver keeps the last valid configuration if runtime/UI disconnects.
- Apply bounds checks, timeouts, and safe defaults to all data crossing IPC.

Exit gate: driver, runtime, and UI can restart independently without corrupting state or crashing SteamVR.

### Stage 5 - UI Integration

- Integrate runtime routes into the existing TrackSwap UI without obscuring the legacy settings feature.
- Show physical source, virtual proxy, target, connection health, active offset, and pending/applied state distinctly.
- Keep risky operations reversible and provide useful errors rather than silent failure.

Exit gate: a normal user can configure and hot-switch a route without editing files manually.

### Stage 6 - Calibration

- Capture source and target samples while both original poses are visible.
- Reject unstable or insufficient samples.
- Save, name, reapply, and reset calibration profiles.

Exit gate: automatic calibration reaches the same practical result as a manually verified offset.

### Stage 7 - 3D Preview

- Consume downsampled telemetry, normally 30-60 Hz.
- Render source, output, target, axes, and offset relationship.
- Preview must be observational: closing, freezing, or overloading it must not affect submitted tracking poses.

Exit gate: the preview accurately explains the active transform while remaining outside the critical path.

### Stage 8 - Hardening and v002 Release

- Test install, upgrade, disable, and uninstall flows.
- Test SteamVR start/stop, standby, source power cycling, runtime/UI crashes, malformed config, and device reconnection.
- Review driver and IPC boundaries for memory safety and validate all identifiers and transforms.
- Update user documentation, third-party notices, CI artifacts, and release metadata.

Exit gate: v002 can be installed and removed without manual residue, and failures degrade to invalid/disconnected tracking instead of crashing SteamVR.

## Existing v001 Safety Invariants

Preserve these behaviors unless a task explicitly replaces them with a safer equivalent:

- Do not write `steamvr.vrsettings` while SteamVR is running.
- Back up the settings file before applying, removing, or restoring mappings.
- Restore only `TrackingOverrides`; preserve unrelated SteamVR settings.
- Use exact OpenVR registered device paths, including any actual prefix returned by OpenVR. Never synthesize `/devices/lighthouse/` or another prefix from a serial number.
- Prevent self-maps, cycles, and conflicting rules.
- Keep target input separate from the selected pose source.
- Treat concrete-device overrides as experimental because behavior can depend on the device driver and SteamVR version.

## Development Practices

- Target Windows x64 for native driver/runtime integration. The existing UI targets .NET Framework 4.8.
- Prefer the official OpenVR headers and documentation. Pin or record the exact dependency revision when vendoring or downloading build inputs.
- Keep real-time code allocation-light, non-blocking, and independent from UI frame rate.
- Log state transitions and actionable errors, but do not log high-frequency poses by default.
- Do not silently install/register a SteamVR driver during an ordinary build. Installation and removal must be explicit commands or user actions.
- Keep generated binaries, local SteamVR paths, test captures, and machine-specific configuration out of Git.
- Add focused tests for transform math, configuration migration/validation, IPC parsing, and route conflict handling before relying on hardware-only tests.
- Verify changes proportionally: build affected projects, run automated tests, and state clearly which SteamVR/hardware checks still require the maintainer.

## Local Testing Notes

On the maintainer's current test machine, SteamVR has previously been found at `D:\Program Files (x86)\Steam\steamapps\common\SteamVR` and its settings at `D:\Program Files (x86)\Steam\config\steamvr.vrsettings`. These are test references only. Product code must discover paths dynamically and must not hard-code them.

When a hardware test is required, stop at a clear test boundary and give the maintainer exact setup, expected result, and recovery instructions. Never assume a successful build proves correct tracking behavior.
