# Runtime Routing and Pose Rules

Read this file before changing route configuration, Runtime reconciliation, proxy identities, the native driver, virtual HMD behavior, pose transforms, physical-source hiding, or SteamVR mappings. Also read `docs/agent-guides/distribution.md` before modifying shared SteamVR files.

## Runtime Data Paths

Default direct output:

`physical source -> native driver transform -> TrackSwap virtual tracker`

Optional target replacement:

`physical source -> native driver transform -> TrackSwap virtual proxy -> SteamVR target override`

The virtual-proxy-to-target relationship is a static bootstrap mapping. Changing a route's physical source or offset is a live Runtime operation and must not repeatedly rewrite `steamvr.vrsettings`.

## Virtual HMD

- At most one route may publish the TrackSwap-owned `TRKSWAP-HMD`. It reuses the normal source, split-source, local-offset, hiding, telemetry, and preview pipeline.
- The HMD exposes fixed baseline `IVRDisplayComponent` parameters without a physical scanout target and pairs it with one `TrackedDeviceClass_DisplayRedirect` implementing `IVRVirtualDisplay`.
- The redirect must publish the compositor GPU's `Prop_GraphicsAdapterLuid_Uint64`, honor the shared-texture keyed mutex, and provide stable display-frequency timing. Omitting the adapter LUID can leave the redirect recognized but never presented to.
- Never fall back to a desktop debug-display window: moving it off-screen triggers SteamVR compositor error 202, while advertising a non-desktop display without an active redirect can make SteamVR take over the user's real monitors.
- Runtime drives HMD pose after the UI closes. The headless HMD publishes `/proximity` as continuously active while running, independently from route source validity.
- HMD registration is bootstrapped by `driver_trackswap.enableVirtualHmd`. Enabling, disabling, or deleting it requires SteamVR to be fully stopped and restarted. Runtime owns reconciliation and must never edit the setting while SteamVR is running.
- Use explicit `手动位姿` to break SteamVR's no-HMD discovery deadlock. Manual pose is available to every route mode, persists an absolute standing-space pose, defaults to the origin, and works after the UI closes. It has no physical source path, split source, or hiding request. Never persist a source-free device route as an implicit bootstrap pose. Preserve manual pose and local offset independently when switching source kinds.
- A direct-display recommendation may be a SteamVR notification quirk. Do not implement `IVRDriverDirectModeComponent` merely to suppress it.
- Keep the HMD on its stable TrackSwap universe, but never publish a hard-coded `Prop_DriverProvidedChaperoneJson_String`. SteamVR Room Setup owns standing/seated origin and play-area data.
- Ship the VR picture viewer as a separate optional process. Publish only the latest completed compositor frame through versioned GPU shared textures. A slow, minimized, disconnected, or crashed viewer drops frames instead of applying back-pressure. Closing the viewer or UI never disables the HMD. Keep viewing GPU-local and support both-eye/left/right presentation, fixed output sizes, windowed/borderless fullscreen, always-on-top, and health counters.

## Route and Proxy Model

- Support at most sixteen routes with stable logical slots `00` through `15`.
- Direct routes publish `TRKSWAP-TRACKER-00` through `TRKSWAP-TRACKER-15` (`TRKSWAP-TRACKER-XX`) as `TrackedDeviceClass_GenericTracker`. Target replacement uses separate internal `TRKSWAP-PROXY-00` through `TRKSWAP-PROXY-15` (`TRKSWAP-PROXY-XX`) identities as `TrackedDeviceClass_TrackingReference` with the empty proxy render model.
- Never use a direct tracker as a `TrackingOverrides` source or expose a replacement proxy as the direct-output identity. A proxy must retain a valid pose for `TrackingOverrides`; never hide it by invalidating or moving its submitted pose.
- Register slots on demand. Disabled or missing routes submit invalid/disconnected tracking instead of stale healthy poses.
- Support at most one virtual controller per hand and at most one virtual HMD, even when routes are disabled. Reject duplicate hand assignments.
- Route names are independent from ids and slots, unique case-insensitively, and renameable. New routes use the lowest free slot and names such as `新配置`, `新配置 (1)`.
- Every route explicitly selects direct tracker, target replacement, virtual controller, or virtual HMD output as supported by the protocol. New routes remain UI-only `Unspecified` until the user chooses; never persist or submit `Unspecified`. Pre-mode configurations deserialize as target replacement.
- Direct routes need a source but no target and create no `TrackingOverrides`. Controller routes need a source pose, hand, and explicit `无`, `OSC`, or `XInput` input source and create no override. `无` preserves pose while keeping inputs neutral.
- Configuration revisions are atomic across all slots. Never allow an older snapshot to replace a newer accepted revision.
- Reject duplicate slots, conflicting targets, cycles, role self-reference, and ambiguous dependencies. For example, never route a physical right-hand controller through a proxy back to `/user/hand/right`, which can feed the overridden pose back as its own source. Duplicate physical sources are rejected by default; the advanced testing exception must not relax any other validation.
- Split pose uses distinct position and rotation sources sampled from the same OpenVR batch. Position and linear velocity come from the position source; orientation and angular velocity come from the rotation source; then apply one local rigid offset. If either source is invalid, submit invalid output without fallback. Apply validation, hiding, telemetry, and preview behavior to both sources.
- Per-route motion smoothing runs in the native driver's frame path after split-source composition and local-offset application, and before telemetry and pose submission. Position and rotation can be enabled independently with separate integer strengths from `0%` to `100%` that default to `0%`; normalize legacy fractional strengths to the nearest integer with halves away from zero. Linked adjustment is a persisted UI behavior and begins by assigning both channels their rounded average. Use frame-rate-independent interpolation, filter the matching linear or angular velocity with each channel, and reset filter history after invalid tracking, reconnection, source changes, disablement, or smoothing configuration changes. The feature behaves identically for trackers, controllers, and the virtual HMD and defaults to disabled.
- Hide generic head/left/right role targets from selectors by default. Advanced visibility is opt-in and never bypasses validation.
- A proxy's first target, target change, disable cleanup, or deletion cleanup may edit `TrackingOverrides` only while SteamVR is fully stopped.
- Saved Runtime routes are desired mapping state. Persist pending work while SteamVR runs and reconcile automatically after it stops. Pending deletion precedes mapping reconciliation; remove orphaned TrackSwap proxy mappings.
- Deleting a running route keeps a proxy active only when a real static mapping requires cleanup. Routes without mappings delete immediately. Remove the mapping before the Runtime route, preserve pending state on failure, and allow cancellation.
- Hot source and offset changes for an already bootstrapped proxy must not require restarting SteamVR.
- Physical-source hiding is opt-in and Runtime-owned. Never hide TrackSwap virtual devices. Changing a source clears that route's request. The native hook fails open and TrackSwap output uses the pre-hidden source pose while subtracting its own displacement.
- Retain hiding requests and slot ownership independently for every snapshot family, derive effective state under one lock, and never let an empty snapshot from one family clear another family's valid request. Avoid transient clear-then-set transitions.

## Pose Math

- Use `T_output = T_source * T_offset`.
- Offset translation is in the source device's local frame and persisted in centimetres. Convert to metres only at the driver-control boundary; raw OpenVR poses, driver math, and telemetry remain metres.
- Store quaternion components in `x / y / z / w` order and normalize validated input.
- Euler angles are a UI-only degree representation composed X then Y then Z (`q = qZ * qY * qX`). Runtime configuration, IPC, driver snapshots, and pose math remain quaternion-only; changing the UI representation must not create a configuration revision.
- Transform position and orientation together; do not add translation in world space.
- Account for the lever arm when submitting velocity: `v_output = v_source + omega x (R_source * t_offset)`.
- Preserve connected/valid/tracking-result state deliberately and never report stale pose as healthy.
- Calibration uses simultaneous observations: `T_offset = inverse(T_source) * T_target`. Capture before enabling an override and reject unstable, insufficient, disconnected, or non-simultaneous samples.
