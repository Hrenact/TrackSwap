# TrackSwap v002 architecture contract

This document fixes the Stage 0 boundaries. It is normative for code added to
the v002 runtime line; changes to these contracts require a schema or protocol
version review.

## Components

### TrackSwap UI

The `src/TrackSwap` WPF application contains two deliberately separate tabs:
the v002 runtime route surface and the retained v001 static configuration
surface. The runtime tab sends bounded control requests to TrackSwap Runtime
and polls low-frequency status. UI lifetime and frame rate are never part of
pose submission.

### TrackSwap Runtime

`src/TrackSwap.Runtime` owns persistent v002 configuration, validation and IPC.
It exposes `applyConfiguration` and `getStatus` over the local control pipe and
retries delivery of the latest valid snapshot to the driver. The WPF UI may
start this process, but closing the UI does not stop it. Source discovery is
currently performed by the UI; moving it into Runtime remains part of the
hardening work before release.

### TrackSwap OpenVR driver

`src/TrackSwap.Driver` is a Windows x64 server driver. The Stage 0 DLL exports a
loadable `IServerTrackedDeviceProvider`. Stage 1 registers one stable
`TrackedDeviceClass_GenericTracker` and mirrors the configured physical source;
hardware verification remains an explicit maintainer test boundary.

The per-frame path is entirely inside the driver:

```text
physical OpenVR pose -> local rigid transform -> stable virtual tracker pose
```

Control/configuration crosses IPC as bounded snapshots. Pose submission never
waits for a synchronous IPC response.

## Identity and bootstrap mapping

Virtual devices use immutable serials `TRKSWAP-PROXY-00` through
`TRKSWAP-PROXY-07`. Stage 1 exposes slot 0 only; additional slots are reserved
by schema so adding them does not change identity rules. A route owns exactly
one slot, and two routes cannot own the same target.

The legacy SteamVR `TrackingOverrides` bootstrap maps a stable TrackSwap
virtual device to a target. Hot switching changes the physical source followed
by that virtual device; it does not rewrite `steamvr.vrsettings`.

All physical identifiers stored by v002 are exact registered device paths
returned by OpenVR. The runtime must not reconstruct a path from a serial.

## Pose coordinates and transform order

TrackSwap follows OpenVR raw tracking space: right-handed, metres, +Y up, +X
right and -Z forward. Quaternion fields are ordered `(x, y, z, w)`.

An offset is expressed in the source device's local frame and is applied by
right multiplication:

```text
T_output = T_source * T_offset
R_output = R_source * R_offset
p_output = p_source + R_source * t_offset
```

Translation is therefore not an unrelated world-space addition. Calibration
from simultaneous unmodified samples is:

```text
T_offset = inverse(T_source) * T_target
```

When velocities are submitted, the output linear velocity includes the offset
lever arm:

```text
v_output = v_source + omega_source x (R_source * t_offset)
```

Angular velocity is rotated only if the source and output tracking bases differ.
Stage 3 must add golden-vector tests for axes, handedness, quaternion order,
multiplication order and the lever-arm term before offsets reach hardware.

## Configuration snapshots

The current configuration schema is version 1. A snapshot contains a monotonic
non-negative revision and no more than eight routes. Each route contains:

- a stable `routeId`;
- an enabled flag;
- a unique virtual device slot;
- exact source and target OpenVR paths;
- a finite local rigid offset, bounded to 10 metres per translation axis.

The runtime validates an entire candidate snapshot before atomically replacing
the previous one. The driver retains the last valid snapshot if runtime or UI
disconnects. Invalid, missing or stale source poses must result in an explicitly
invalid/disconnected virtual pose, never a healthy stale pose.

## IPC framing

The UI/runtime control endpoint is the local named pipe `TrackSwap.Runtime.v1`.
The runtime/driver endpoint is `TrackSwap.Driver.v1`; its small binary envelope
is versioned and capped at 511 payload bytes so the native driver needs no JSON
parser. Driver control is handled on a worker thread and only queues an atomic
source update for `RunFrame`; pose submission never blocks on pipe I/O.

Driver protocol v1 message type 1 changes the exact UTF-8 `/devices/` source
path. Type 2 supplies seven little-endian IEEE-754 doubles in
`tx, ty, tz, qx, qy, qz, qw` order. The driver independently rejects non-finite
values, translations beyond 10 metres per axis, and zero-length quaternions.

Runtime control frames are
UTF-8 newline-delimited JSON with a hard limit of 65,536 bytes, including the
newline. Every envelope has `protocolVersion`, `messageType`, `requestId`, and a
JSON payload. Unknown versions/types, duplicate fields, invalid numeric values,
oversized frames and malformed JSON are rejected without changing active state.

Version 1 defines `applyConfiguration` and `getStatus`. The runtime validates
and atomically persists a whole candidate snapshot before publishing it to the
driver. Status includes the persisted configuration revision, the last revision
acknowledged by the driver, driver connection health, the active configuration,
and an actionable synchronization error when present. The driver snapshot
message carries one slot-0 source and offset with a monotonic revision; repeated
delivery of the same revision is idempotent, and older revisions are rejected.

## Calibration

Stage 6 calibration is a Runtime control-plane operation and remains outside the
driver pose loop. Runtime captures simultaneous raw OpenVR poses through a
short-lived background client, derives each relative transform as
`inverse(T_source) * T_target`, sign-aligns and averages quaternions, and rejects
insufficient or unstable samples. The UI refuses capture while the virtual
proxy has an active static override so the target's original pose is not hidden.
Named profiles are stored atomically beside the runtime configuration. See
[`calibration.md`](calibration.md) for thresholds and the hardware workflow.

## Pose preview telemetry

The driver publishes a bounded binary snapshot containing source, transformed
output, and physical target poses. Runtime forwards snapshots on demand and the
WPF preview polls at about 30 Hz. Telemetry reads a copied snapshot from the
driver's control thread; UI or Runtime latency never enters the pose submission
path. See [`pose-preview.md`](pose-preview.md).

## OpenVR dependency

The native build consumes Valve OpenVR's `IVRSystem_026` definitions, pinned to
commit `0924064316de3effbcd1acf1e309182a2deb1c05`. CMake fetches only build input
and does not install or redistribute SteamVR. Runtime bindings use the same
function-table layout. Update the revision deliberately and review interface
changes before accepting a newer SDK.
