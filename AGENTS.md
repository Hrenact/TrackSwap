# TrackSwap Repository Guide

This file records the current product baseline and the engineering constraints for future work in this repository. It applies to the entire repository unless a more specific `AGENTS.md` is added below a subdirectory.

## Product Baseline and Direction

TrackSwap has three deliberately separate workflows:

1. **Legacy static mapping**
   - The v001-compatible WPF workflow edits SteamVR `TrackingOverrides`.
   - It substitutes a pose source while leaving the target device's input handling intact.
   - Static `TrackingOverrides` do not provide a general position or rotation transform. Never describe them as supporting offsets.

2. **Runtime pose routing**
   - v002 introduced the independent Runtime, native OpenVR driver, live source switching, local rigid offsets, automatic calibration, telemetry, and a real-time 3D preview.
   - v003 added named route management and up to eight stable virtual proxies.
   - v004 hardened source reconnection and cross-route validation, migrated runtime targets away from ambiguous SteamVR roles, and refined device-model preview behavior.
   - v005 introduced the Dark Utility interface, device history and live selector refresh, application/runtime lifecycle controls, custom device/app icons, dark dialogs, and the Windows installer/uninstaller.
   - v006 added direct virtual-tracker output, virtual left/right controllers, fixed-address OSC input, live input monitors, route-mode-specific proxy rendering, and the `无` control-input option.
   - v007 added XInput, Oculus Touch-compatible controller input and synthesized skeletons, auto-applied route editing, centimetre-based direct pose manipulation, sixteen logical slots, and separate direct-tracker and replacement-proxy identities.
   - The current UI uses a configuration sidebar; each selected route owns its source, proxy slot, target, offset editor, status, and always-visible 3D preview.

3. **Direct virtual-tracker output**
   - v006 routes support a direct mode that publishes the transformed source pose as the stable TrackSwap virtual tracker without a `TrackingOverrides` target.
   - Newly created routes begin with no selected mode and show `请选择`; the user must explicitly choose a mode. Existing configurations without an explicit mode retain target-replacement behavior for compatibility.
   - Target replacement remains available when the user needs the target device to keep its original controller input.

4. **OSC virtual-controller output**
   - v006 routes may output one fixed virtual left controller and one fixed virtual right controller.
   - Physical devices provide pose while Runtime maps explicitly configured OSC addresses to basic controller inputs.
   - The virtual controllers use TrackSwap-owned identities and an Oculus Touch-compatible input profile; they must not impersonate Meta/Oculus serial numbers.

The default runtime data path is:

`physical source -> native driver transform -> TrackSwap virtual tracker`

The optional target-replacement path is:

`physical source -> native driver transform -> TrackSwap virtual proxy -> SteamVR target override`

The virtual-proxy-to-target relationship is a static bootstrap mapping. Changing a route's physical source or offset is a live Runtime operation and must not repeatedly rewrite `steamvr.vrsettings`.

## Branch and Release Policy

- `v001`, `v002`, `v003`, `v004`, `v005`, `v006`, `v007`, `v008`, and `v009` are published, immutable release tags. Never move or rewrite them.
- `main` currently remains the stable v001 line. Do not merge, retarget, or rewrite it without an explicit maintainer decision.
- Current post-v009 work remains on `v002-runtime` until the maintainer chooses the integration branch for the next release.
- The next release identifier is `v010`. Releases use one increasing sequence: `v001`, `v002`, `v003`, and so on. Do not introduce semantic-version labels or prerelease suffixes unless the maintainer changes this policy.
- Do not commit experimental driver/runtime work directly to `main`.
- Keep commits focused. Do not rewrite published history.
- The project is MIT licensed under the name Hrenact. Record added third-party code or binary dependencies in `THIRD-PARTY-NOTICES.md` and preserve their required notices.

## Installer and Uninstaller

- The primary Windows distribution is an unsigned Inno Setup installer until a maintainer adds code signing. Keep the complete ZIP as the portable/manual fallback.
- Install per user by default under `%LOCALAPPDATA%\Programs\TrackSwap`; do not require elevation for the normal path.
- Installation and uninstallation must refuse to mutate SteamVR integration while `vrserver`, `vrmonitor`, or `vrcompositor` is running.
- Installer upgrades must preserve user configuration, replace an existing TrackSwap driver registration safely, and update the registered TrackSwap application-manifest path without enabling UI auto-launch by default.
- A successful full uninstall must remove the TrackSwap OpenVR driver registration, TrackSwap application-manifest and auto-launch records, TrackSwap proxy `TrackingOverrides`, TrackSwap-created SteamVR settings backups, `%LOCALAPPDATA%\TrackSwap`, shortcuts, uninstall registration, and the installation directory. Never delete shared SteamVR logs, Windows caches, or unrelated OpenVR configuration.
- Keep installer registration and cleanup logic covered by an isolated fixture test. Shared JSON configuration must be changed atomically and unrelated applications, mappings, and settings must survive byte-semantics-equivalent rewrites.

## Component Responsibilities

Keep the components in this repository separate:

1. **TrackSwap UI**
   - Windows WPF control surface targeting .NET Framework 4.8.
   - Exists to edit, adjust, inspect, and test configuration. It may submit configuration and display status or downsampled telemetry, but it must not own an operational service or perform background work required for routing, input, reconciliation, or device output. The current UI no longer exposes automatic calibration.
   - Every configured feature must continue working from TrackSwap Runtime after the UI closes. If an exceptional feature truly cannot work without the UI, disclose that dependency prominently and precisely to the user before they enable or rely on it; never leave an implicit UI dependency.
   - Retains the legacy static settings workflow under Settings.
   - Must not participate in the per-frame tracking path.

2. **TrackSwap Runtime**
   - Independent configuration and control process.
   - Owns persistent runtime configuration, IPC, validation, source discovery, calibration profiles, and telemetry fan-out.
   - Owns all operational behavior derived from that configuration, including stopped-SteamVR `TrackingOverrides` reconciliation, pending deletion completion, orphaned proxy cleanup, and retry state. None of these operations may depend on an open UI process.
   - Sends versioned, atomic snapshots for every virtual slot.
   - Must recover cleanly if the UI closes or reconnects.
   - Supports two explicit lifecycle policies selected under Advanced Settings: follow the TrackSwap UI process, or follow the SteamVR session. SteamVR-follow mode must still accept a live UI process as a temporary owner so offline configuration remains available while SteamVR is stopped.
   - Owns OSC UDP reception, address mapping, normalization, controller-input snapshot delivery, and OSC haptic-feedback transmission. OSC networking must never run inside the driver frame loop.

3. **TrackSwap OpenVR Driver**
   - Minimal native SteamVR server driver exposing stable virtual GenericTrackers and the fixed `TRKSWAP-CONTROLLER-L` / `TRKSWAP-CONTROLLER-R` controller pair.
   - Reads raw poses from selected physical sources, applies transforms, and submits virtual poses.
   - Keeps the last valid configuration if Runtime or UI disconnects.
   - Keeps the per-frame path local, deterministic, allocation-light, and non-blocking. Never require a synchronous IPC round trip for every pose update.
   - May request the packaged Runtime once during driver initialization so SteamVR-follow mode works without the UI. Process launch and path discovery must remain outside the per-frame path, use only package-relative trusted executables, and tolerate a missing Runtime without failing driver initialization.

## Route and Proxy Model

- Support at most sixteen routes in the configuration list, using stable logical slots `00` through `15`.
- Direct virtual-tracker routes publish user-facing devices identified as `TRKSWAP-TRACKER-00` through `TRKSWAP-TRACKER-15`. Target-replacement routes use the separate internal proxy identities `TRKSWAP-PROXY-00` through `TRKSWAP-PROXY-15`. Do not use a direct tracker as a static `TrackingOverrides` source, and do not present a replacement proxy as the direct-output identity.
- Register direct `TRKSWAP-TRACKER-XX` outputs as `TrackedDeviceClass_GenericTracker`. Register internal `TRKSWAP-PROXY-XX` replacement carriers as `TrackedDeviceClass_TrackingReference`, with the empty proxy render model, so games do not expose them as extra wearable trackers. A proxy must retain a valid pose for `TrackingOverrides`; never hide it by moving or invalidating its submitted pose.
- Additionally support at most one virtual left controller and one virtual right controller. Reject duplicate hand assignments across routes.
- Register a proxy on demand when an enabled route first uses its slot. Do not expose unused slots merely because capacity exists.
- A route's user-facing name is independent from its route id and proxy slot. Names must be unique case-insensitively and remain renameable.
- New routes use the lowest available slot and a default name such as `新配置`, `新配置 (1)`, and so on.
- Disabled or missing routes must submit invalid/disconnected tracking rather than a stale healthy pose.
- Every route explicitly selects direct virtual-tracker output, target replacement, or virtual-controller output. Direct routes require a source but no target and must not create a `TrackingOverrides` entry.
- Virtual-controller routes require a source pose, an explicit left/right hand, and an explicit control-input source. They do not create a `TrackingOverrides` entry.
- Virtual-controller control input may explicitly be `无`, `OSC`, or `XInput`. `无` keeps the controller pose active while holding every button and analog component at its neutral value; each input service must only update hands that explicitly select it and must not neutralize or otherwise clobber a hand owned by another input source.
- New routes use the UI-only incomplete `Unspecified` mode until the user explicitly chooses a mode; never persist or submit that value to Runtime. Configurations written before the mode field existed must deserialize as target-replacement routes.
- Configuration revisions are atomic across all slots. An older snapshot must never replace a newer accepted revision.
- Reject duplicate slots, conflicting targets, cycles, and other ambiguous routing rules.
- Reject duplicate physical pose sources by default. Advanced Settings may explicitly allow source reuse for testing or for driving both virtual controller hands from one device; this exception must not relax duplicate hand, slot, target, cycle, or role-dependency validation.
- A route may explicitly split its pose across one position source and one rotation source. Enabling split mode must leave the rotation source unselected until the user chooses it, and the two sources must be distinct. Sample both sources from the same OpenVR pose batch, use position and linear velocity from the position source, use orientation and angular velocity from the rotation source, then apply the route's one local rigid offset to that hybrid base pose. If either source is disconnected or invalid, submit an invalid output without falling back to the other source. Apply duplicate-source, role-dependency, cycle, physical-source-hiding, telemetry, and preview behavior to both paths.
- Reject role self-reference such as a physical right-hand controller routed through a proxy back to `/user/hand/right`. SteamVR can feed the overridden target pose back as the source and freeze the route on a previous frame.
- Hide SteamVR's generic head, left-hand, and right-hand role targets from runtime target selectors by default. They are an explicitly opt-in advanced UI option only; enabling visibility must not bypass self-reference, cycle, conflict, or migration validation.
- A proxy's first target binding, target change, disable cleanup, or deletion cleanup may require editing `TrackingOverrides`; perform that edit only while SteamVR is fully stopped.
- Treat the saved Runtime route configuration as the desired static-mapping state. When a new route or target change is applied while SteamVR is running, persist it as pending work and automatically reconcile `TrackingOverrides` after SteamVR fully stops; never require a second click on Apply for the same change. Pending deletion has priority over pending mapping: finish or resolve every deletion before reconciling mappings, and remove orphaned TrackSwap proxy mappings that no longer correspond to an enabled route.
- Deleting a route while SteamVR runs must persist a pending-deletion state and keep the proxy output active only when that proxy actually has a static `TrackingOverrides` entry to clean up. A route with no existing static mapping is removed immediately regardless of mode or SteamVR state. After SteamVR stops, remove any existing static mapping first and only then remove the Runtime route. Preserve the pending state on failure and allow cancellation.
- Hot source and offset changes for an already bootstrapped proxy must work without restarting SteamVR.

## UI Behavior

- Use the sidebar as the primary route navigation and management surface.
- Let sidebar route items communicate enabled, disabled, and pending lifecycle state. The selected-route header must instead show the concise configuration synchronization state (`配置已同步`, `待应用`, pending mapping/removal, or pending deletion); do not repeat that status inside the route card.
- Route editing is auto-apply: complete mode/source/target/controller selections submit immediately, while numeric offset typing uses a short debounce. Incomplete numeric input remains local and shows a concise header state without a modal interruption. A complete candidate rejected by configuration validation must show one Chinese shared `AppDialog` warning for that edit and wait for the next user modification before trying again. Do not restore route-level `重新读取`, `应用更改`, or `应用配置` buttons. Gizmo release and offset reset submit immediately.
- Local-pose text fields accept only ASCII digits, one decimal point, and an optional leading minus sign; reject every other typed or pasted character before it reaches the field.
- Keep Settings organized behind a compact internal category rail. Group environment/runtime status, SteamVR maintenance tools, and device-history management by responsibility instead of appending unrelated cards to one long page.
- Keep runtime status and support diagnostics together under `运行与诊断`. Diagnostic exports must be deliberately privacy-preserving: include versions, health, route-count summaries, configuration validation results, and only TrackSwap-filtered recent log excerpts; never include raw runtime configuration, device history, complete SteamVR logs, physical device paths, usernames, or device serial numbers.
- Keep OSC transport, receiver health, send health, and the fixed left/right control and haptic-address reference in a dedicated Settings category. Loopback is the default address because OSC has no authentication or encryption. Present separate receive and send ports.
- Show compact live OSC monitors beside mapping fields: boolean inputs use an on/off center bar, one-sided scalar inputs use a left-to-right fill bar, and each joystick shares one square two-axis crosshair for its X/Y addresses. Size the square to span from the top of the X field to the bottom of the Y field, give every bar the same width as that square, and refresh at approximately 30 Hz only while the OSC Settings page is visible. Keep this observational polling outside the driver frame path.
- Treat the built-in OSC parameter addresses as read-only reference text: users may select and copy them, but do not edit them in place. Do not expose a separate OSC enable switch: derive receiver activation from enabled virtual-controller routes that select OSC, and stop it when no enabled route uses OSC. Apply the address and receive port automatically; debounce free-form address edits instead of requiring an Apply button or restarting the listener on every keystroke.
- Keep `Runtime 启停行为` under Advanced Settings as a compact selector with exactly `跟随 TrackSwap` and `跟随 SteamVR`. SteamVR-follow mode must still let an open TrackSwap UI temporarily own Runtime so offline configuration remains available when both Runtime and SteamVR were initially closed. Runtime itself performs stopped-SteamVR reconciliation and must remain alive while such work is pending, regardless of the selected lifecycle policy.
- Keep TrackSwap's SteamVR-follow UI behavior as a separate, default-off Advanced Settings checkbox. While enabled, register the OpenVR dashboard application manifest, identify the UI process, and enable auto-launch; while disabled, disable auto-launch and remove that manifest registration so SteamVR cannot terminate a manually opened TrackSwap process when the session ends. Retain the driver/Runtime launch fallback for the first enabled session, avoid duplicate UI processes, and close the UI as soon as SteamVR is fully stopped and Runtime reports that pending deletion or mapping reconciliation has completed. A reconciliation failure may be displayed by the UI when it is open, but retry and unresolved-state preservation belong to Runtime and continue without the UI.
- With no routes, hide route controls and show the centered empty state `暂无配置，请新建。`.
- Keep a thin full-width bottom status bar for SteamVR, Runtime, OSC, XInput, and physical-source hiding health. Leave the route-count limit beside the sidebar Add action rather than moving it into the status bar. Use gray for inactive or unavailable services, orange for enabled services waiting for a connection or signal, green for healthy active services, and red for a failed hiding hook; expose concise details through dark-themed tooltips.
- Physical-source hiding is an opt-in advanced Runtime feature. The global switch reveals one per-route `隐藏物理位姿来源设备` option; disabling the global switch clears every route request, changing a route source clears that route request, and hiding TrackSwap-owned virtual devices is forbidden. Runtime owns the persisted requests and the native driver performs the OpenVR pose hook after the UI closes. The hook must fail open: on injection conflict or partial failure, leave every physical device at its original pose. TrackSwap route output must always subtract its own hiding displacement and continue using the source's pre-hidden pose. Warn separately before hiding an HMD.
- When one driver behavior can be owned by more than one snapshot family (for example ordinary tracker/proxy snapshots and virtual-controller snapshots requesting physical-source hiding), retain each family's request and slot ownership independently, then derive the effective state under one lock before `RunFrame` consumes it. An empty, disabled, or periodic synchronization snapshot from one family must never clear another family's still-valid request. Do not implement active-to-active transitions as sequential clear-then-set IPC operations that expose a transient inactive frame, and do not infer previous control-plane ownership solely from frame-applied device state. Preserve this invariant when adding route modes or reordering synchronization, with regression coverage for interleaved snapshot delivery where practical.
- Keep the established **Dark Utility** visual language: neutral charcoal/graphite surfaces, low-contrast layers, compact 4/8 px spacing, restrained sans-serif hierarchy, subtle 1 px separators, 2–6 px radii, compact rectangular controls, and one clear blue accent. Avoid blue-tinted backgrounds, gradients, glass effects, decorative shadows, giant rounded cards, pill buttons, oversized headings, and marketing-style empty space.
- Keep every standard `ComboBox` on the shared application template, with a fixed `30 px` arrow column. Do not restore the old `38 px` reservation or introduce page-specific arrow widths; preserve enough selection-text width for compact controls such as `默认接触` without covering or clipping the final glyph.
- Preserve the established natural height of compact controls. When one control becomes taller because its parent row is sized by a larger sibling, fix the local stretching or alignment (for example, center the `ComboBox` vertically) instead of assigning a new fixed height to every peer. A regional or global size override can make the outlier match only by enlarging controls that were already correct; use explicit shared heights only when the design intentionally changes the baseline for the whole control family.
- Treat `src/TrackSwap/Assets/TrackSwap.svg` as the application icon master. Preserve its single centered triangular tracker, flat charcoal/blue/mint palette, uniform geometry, and legibility at 16 px; regenerate derived PNG and ICO assets with `scripts/generate-icon.ps1` after changing the master.
- Treat `src/TrackSwap.Driver/assets/trackswap_controller.png` as the original 1024 px RGBA **right-hand** virtual-controller status-icon master. Preserve its exact TrackSwap triangular head, white diagonal control mark, offset handle, transparency, and proportions; do not trace or reinterpret it. Use the master unchanged for right-hand assets and mirror it only for left-hand assets. Recolor only the blue outline and green status indicator, using the same blue/green/orange/red/gray state palette as the tracker family. Regenerate all 32 px SteamVR tracker and controller status assets with `scripts/generate-driver-icons.ps1`; never point virtual controllers back at the tracker glyph or SteamVR's generic fallback icon.
- Request a dark native Windows title bar for every app-owned window and dialog, with caption, border, and text colors aligned to the neutral application palette. Treat unsupported DWM attributes as a harmless platform fallback.
- Keep the dark theme explicit across every detached or system-hosted surface. Tooltips, context menus, popups, ComboBox drop-downs, and dialogs must define compatible foreground, background, border, and selection states instead of inheriting Windows theme defaults.
- Use the shared `AppDialog` for application messages and confirmations. Do not add new calls to the system `System.Windows.MessageBox`; new dialogs must preserve the Dark Utility palette, compact geometry, application icon, semantic status color and Windows system sound, keyboard defaults, and dark native title bar.
- Remember that the implicit `TextBlock` style can make text light even inside a system-default white popup. After adding or changing any popup-like UI, inspect the actual rendered state and reject white-on-white, black-on-black, or otherwise low-contrast combinations.
- Visual UI verification must cover normal, hover, selected, disabled, validation, and popup/tooltip states where applicable. A successful XAML build is not sufficient evidence that themed UI is readable.
- Initialize observational OpenVR clients used for device discovery or render-model loading as utility applications. Reuse one process-wide Utility session for the TrackSwap UI lifetime and release it when SteamVR stops or the UI closes; repeated Init/Shutdown cycles make SteamVR reload application input bindings and can overwhelm its controller/settings web UI. Do not reconnect these clients as background applications.
- Poll the process-wide Utility session for `VREvent_Quit` at UI cadence and call `VR_ShutdownInternal` before SteamVR finishes shutting down. SteamVR forcibly terminates clients that remain attached, which would otherwise close a manually opened TrackSwap window even when UI follow mode is disabled. Suppress reconnection until the stopped SteamVR session has been observed.
- Keep the selected route's 3D preview visible; do not make it collapsible.
- Keep numeric local-pose controls and the 3D preview side by side: the left card is the precise editor and the right preview is the direct-manipulation editor. Do not restore a separate automatic-calibration card.
- Express `PoseOffset` translations, UI fields, and persisted Runtime configuration in centimetres. Convert centimetres to metres only at the OpenVR driver-control boundary; raw OpenVR poses, driver transform math, and telemetry remain in metres. Do not add migration or dual-unit interpretation for older offset values.
- Present the 3D manipulation control as `调整工具`, using one compact segmented selector ordered `隐藏`, `移动`, `旋转`; keep `隐藏` as the default. Hiding the tool must not clear or disable the configured offset. Dragging updates the numeric fields and local preview continuously, then persists the route through the normal apply path when the drag ends; holding Shift provides finer adjustment.
- Anchor the Gizmo exclusively to the configured virtual output transform and use the virtual device's local axes. Its pose, visibility, and framing must not depend on whether the proxy model is rendered, and it must never move onto the physical source or final target when the proxy model is hidden.
- Prefer the device render model registered with OpenVR, but always retain built-in HMD, controller, tracker, and generic fallbacks. Loading or rendering a model must remain UI-only and observational.
- In the preview, render the physical pose source and final target; do not render the virtual proxy as a third user-facing object. The proxy is a compatibility and routing layer.
- The preview is observational. Closing, freezing, or overloading it must not affect submitted tracking poses.
- Show physical source, stable virtual proxy, target, connection health, active offset, and pending/applied state distinctly.
- Device selectors use connection state only: represent every connected physical device with a green status dot and every remembered but disconnected physical device with an orange status dot, including the device currently referenced by a route. Do not render `当前`, `历史`, `在线`, or `离线` as textual device-state prefixes. Persist discovered physical-device metadata in a small local JSON record, exclude TrackSwap virtual proxies, deduplicate by exact OpenVR device path, and provide a Settings action to clear unreferenced records while preserving route selections.
- Never implicitly choose the first available mode or device for a new route, or the first choice in a newly revealed mode-specific selector. Show `请选择` until the user makes an explicit choice. When changing route modes, clear selections that belong to the newly selected mode instead of carrying over or guessing a device; apply the same rule to future modes and mode-specific fields.
- Refresh physical-device selectors automatically at a low frequency while the UI is open. Only rebuild selector contents when the device catalog actually changes, defer updates while a related drop-down is open, and retain the last successful catalog across transient OpenVR enumeration failures to avoid flicker or accidental selection changes.
- Keep dangerous operations reversible and explain required SteamVR restarts or stopped-state writes before the user acts.
- Preserve target input from the original device; TrackSwap replaces pose, not controller input.
- Keep the legacy v001 settings workflow clearly separated from runtime route management.
- In the legacy v001 editor, new static rules must use explicit concrete `/devices/...` targets. Show `/user/...` role targets only when an existing rule already references them, label them as legacy maintenance entries, and do not allow them to be written as new rules.

## Pose Math Requirements

- Represent a configurable rigid offset as `T_output = T_source * T_offset`.
- Offset translation is expressed in the source device's local coordinate frame and stored in centimetres. Convert it to metres before applying it to OpenVR poses.
- Store quaternion components in `x / y / z / w` order and normalize validated input.
- Transform orientation and position together; do not implement translation as an unrelated world-space addition.
- When velocities are submitted, account for the offset lever arm. At minimum:
  `v_output = v_source + omega x (R_source * t_offset)`.
- Preserve valid/connected/tracking-result state deliberately. Never report a stale pose as healthy tracking.
- Calibration from simultaneously observed source and target poses uses:
  `T_offset = inverse(T_source) * T_target`.
- Capture calibration data before enabling an override that would hide the target's original pose.
- Reject unstable, insufficient, disconnected, or non-simultaneous calibration samples.

## OSC Controller Input

- Expose an Oculus Touch-compatible control surface while retaining the TrackSwap-owned identity: left face buttons are `/input/x` and `/input/y`, right face buttons are `/input/a` and `/input/b`, both hands use `/input/joystick`, `/input/trigger`, `/input/grip`, and the standard Touch skeletal paths. Trigger and grip expose `value` and `touch`, never a synthetic `click`. Expose `/input/system` only on the left controller; merge the left and right configured menu-button states into that component with logical OR so releasing either source cannot cancel the other source while it remains pressed. Do not reintroduce Knuckles-only trackpad, force-sensor, or per-finger scalar inputs.
- Expose `/pose/openxr_aim` and `/pose/openxr_grip` as real driver pose components for both virtual-controller hands. Keep their static local offsets aligned with the SteamVR Oculus Quest 2 controller render models, including mirrored handed X translation; declaring the paths in the input profile without creating and updating the corresponding driver components is incomplete.
- Keep the TrackSwap-owned controller profile complete enough for SteamVR's binding and controller-test UI: declare handed binding images and a `binding_image_point` for every exposed input/pose source. It may reference SteamVR's installed Oculus Touch artwork, legacy binding, and render models while retaining the `trackswap_controller` identity and only advertising implemented capabilities.
- Keep the controller type localized as `TrackSwap Controller` / `TrackSwap 控制器`. Declare `oculus_touch` through `compatibility_mode_controller_type` so applications select their native Touch bindings; do not simultaneously attach the Oculus remapping file, because SteamVR ignores compatibility mode in that combination and may instead choose a physical Pico binding as the remapping source. Do not ship app-specific defaults, and never replace the TrackSwap-owned controller identity or serials with Oculus identities.
- Virtual controllers default `Prop_ControllerHandSelectionPriority_Int32` to `0`. Expose the shared signed 32-bit value under Advanced Settings with a restore-default action; do not silently raise it to compete with physical-controller drivers. Disabled TrackSwap controllers submit disconnected poses so physical controllers can regain the roles.
- Accept numeric OSC values only for TrackSwap's built-in fixed addresses. Do not persist per-control addresses in `runtime-config.json`; legacy address fields are ignored when older configurations are loaded. Clamp joystick axes to `[-1, 1]`, trigger/grip values to `[0, 1]`, and interpret button values above `0.5` as pressed.
- Provide fixed per-hand OSC touch-assist inputs at `/trackswap/{left|right}/touch/thumb` and `/trackswap/{left|right}/touch/index`. Each numeric value above `0.5` temporarily inverts that finger's configured `默认接触` / `默认抬起` state. Match XInput touch synthesis: face/menu/joystick activity and nonzero trigger/grip values own their corresponding real touch state and take priority over assistance. Runtime must initialize and periodically refresh the complete explicit touch snapshot so defaults work before any OSC packet arrives and after the UI closes.
- Keep OSC input state independent for the left and right controller. A valid message refreshes only the hand whose mapping matched it.
- Treat repeated OSC values as valid receiver activity without forwarding redundant driver IPC. Apply every value in one UDP packet first, then send at most one final snapshot per changed hand; an unchanged keepalive packet must update receiver health but produce no immediate controller snapshot.
- Report a local OSC receive-port bind conflict explicitly through Runtime status and render the bottom OSC indicator red with a concise tooltip. The configured send port is the remote destination for haptic feedback, not a locally bound port, so never describe it as “occupied”; report genuine send failures separately and combine simultaneous receive/send failures in one tooltip.
- Retain each hand's last valid OSC input state indefinitely while its OSC route remains enabled, so a sender may intentionally hold a fixed pose or control state without retransmitting it. Reset immediately only when OSC is disabled, routing or relevant configuration changes, a controller route is disabled, or Runtime exits.
- Send virtual-controller haptic feedback for OSC-owned hands to `/trackswap/left/haptic` and `/trackswap/right/haptic`. Each message uses the OSC type tag `,fff` and carries duration in seconds, frequency in hertz, then normalized amplitude. Keep a three-second right-to-left haptic timeline above each hand's input mapping card: amplitude controls height, duration controls horizontal span, and frequency controls bounded/log-scaled stripe density so high frequencies do not alias into noise. A newer event for the same hand replaces the active envelope, so truncate the preceding preview segment at the new event instead of drawing overlapping events. Retain a short Runtime-owned event history so UI polling cannot lose brief pulses. Keep the approximately 30 Hz Runtime IPC polling separate from animation: scroll the cached timeline at the WPF composition/render cadence only while the OSC page is visible, and detach that rendering callback after leaving the page or closing the window. Put the read-only address below the preview. A per-hand `测试` action requests Runtime to play a short fixed recognition pattern that varies duration, frequency, and amplitude, then ends with a double tap; it must work before an OSC route exists. Runtime owns asynchronous scheduling, a repeated request for the same hand cancels and restarts that hand's previous pattern, and the UI must neither send UDP nor block for the full pattern.
- Keep the phone haptic diagnostic under `tools/` and outside release packaging. It must listen to the real OSC/UDP send port before relaying an event to a token-protected local WebSocket page, so a successful phone test proves the Runtime packet reached an independent receiver instead of bypassing the OSC path. It may approximate frequency and amplitude through browser vibration patterns, but must label that approximation and keep UDP receipt, browser receipt, and vibration-API acceptance as separate results. Do not make Runtime operation depend on this diagnostic bridge.
- Poll the driver's destructive haptic-event queue exactly once in Runtime, then fan each batch out to the XInput and OSC sinks. Never let multiple input services poll that queue independently, because one consumer would steal events from the other.
- OSC reception and driver IPC may run asynchronously, but the driver applies pending input components only from its normal frame loop. Never perform UDP or synchronous pipe work in `RunFrame`.

## XInput Controller Input

- Poll XInput in Runtime, never in the driver frame loop. Use the lowest-index connected XInput controller and immediately neutralize only the XInput-owned hand or hands when it disconnects, the route changes source, or Runtime exits.
- Keep XInput sampling, observational status reads, and driver delivery independent. Update the in-memory state at polling cadence, let UI status reads take only the short state lock, and coalesce pending driver snapshots so slow synchronous driver IPC cannot block sampling or the 30 Hz Settings monitor.
- Default to the symmetric Xbox mapping: left/right sticks and clicks feed the corresponding virtual sticks; left/right shoulders feed the corresponding virtual trigger values; left/right analog triggers feed the corresponding virtual grip values; View and Menu are merged into the left controller's system button; `X/Y` feed left `X/Y`, while `A/B` feed right `A/B`. Internal thresholded states may support animation and remapping decisions, but the driver must not publish trigger or grip click components.
- Keep editable XInput mappings and their analog press threshold in the Runtime configuration. Present them in a dedicated Settings category immediately below OSC, with left-hand and right-hand columns and live monitors refreshed at approximately 30 Hz only while the page is visible. Apply changes automatically and provide one restore-default action.
- Apply the standard XInput stick deadzones and clamp normalized axes to `[-1, 1]`. A digital source mapped to a scalar control produces exactly `0` or `1`; an analog trigger mapped to a boolean control uses one user threshold, defaulting to `50%`, and changes state directly at that threshold without release hysteresis.
- Expose configurable thumb and index touch-assistance inputs independently for the left and right virtual controllers. Each helper has a selectable physical XInput source and a selectable `默认接触` / `默认抬起` idle state; holding the helper source temporarily inverts that state.
- Actual controller mappings take precedence over touch assistance globally. If a physical XInput source is assigned to any real virtual-controller input, ignore that source as a touch-assistance toggle on both hands and retain the helper's configured idle state.
- Expose `震动映射` directly below the analog threshold card with exactly `保留手别`, `保留手别（镜像）`, and `统一震动`. Preserve handedness maps left/right virtual haptics to the XInput low/high-frequency motors respectively; mirrored swaps them; unified sends the stronger active hand amplitude to both motors.
- Create one `/output/haptic` component per virtual controller. Drain SteamVR haptic events into a fixed allocation-free driver queue during `RunFrame`, transfer them to Runtime through versioned driver IPC, and call `XInputSetState` only from Runtime. Apply feedback only for hands currently owned by XInput, use the lowest-index connected controller, honor duration and amplitude, and immediately stop both motors on disconnect, route/input-source change, Runtime exit, or controller-index change.

## Synthesized Controller Skeletons

- Virtual controllers expose the standard 31-bone left/right SteamVR skeletal components. Generate both `WithController` and `WithoutController` streams in the driver frame path without IPC or allocation; do not expose Knuckles-only per-finger scalar inputs as public controller capabilities.
- Follow Oculus Touch hand semantics: infer distinct thumb landing poses from the handed face buttons, system/menu button, joystick, and thumb-rest activity; drive the index finger from trigger touch/value and the middle/ring/pinky group from grip touch/value. For OSC and XInput sources without capacitive channels, synthesize Touch-style contact from button presses and analog activity. Vary finger splay continuously so extended fingers are slightly spread and curled fingers converge, and smooth transitions over a short bounded response interval.
- Drive trigger and grip animation from their analog values and explicit or synthesized touch states. Do not restore private or public trigger/grip click fields as animation fallbacks.
- The skeletal simulator is built from the official OpenVR `handskeletonsimulation` sample pinned by `TRACKSWAP_OPENVR_REVISION`. Preserve Valve's license notice and keep the exact revision recorded in `THIRD-PARTY-NOTICES.md`.

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
- The current route editor uses numeric offset editing and the virtual-output adjustment tool instead of exposing calibration-profile controls. Legacy offset and calibration files receive no unit migration; their stored translation numbers are interpreted as centimetres.
- Downsampled per-route telemetry drives the source-and-final-target 3D preview outside the tracking-critical path; the virtual output remains available to telemetry but is not rendered as a third user-facing device.
- The preview loads static device meshes and textures through `IVRRenderModels_006`, supports component-based controller models, and falls back to built-in device-class geometry without affecting tracking.
- Virtual proxies use the visible driver-owned `trackswap_proxy_tracker` render model in direct-output mode and switch to the empty `trackswap_hidden_proxy` render model in target-replacement mode. The empty model prevents SteamVR from drawing a misleading GenericTracker fallback while preserving tracking, routing, and telemetry; the hidden-model behavior has been hardware-verified.
- Virtual proxies publish driver-owned named 32 px status icons for every OpenVR device state. Do not allow SteamVR to fall back to the GenericTracker hexagon-and-`T` icon; keep these transparent status assets visually consistent with the TrackSwap tracker mark.
- Multiple proxy slots can be registered in one SteamVR session and receive independent route snapshots.
- Deleting a route while SteamVR is stopped removes its static proxy mapping without disturbing other routes.
- Deleting a route with an existing static mapping while SteamVR is running marks it pending, preserves live tracking, and automatically removes its static mapping and Runtime route after SteamVR stops. Routes without a static mapping delete immediately.
- Hardware testing has confirmed Tracker-to-hand pose replacement while the original hand controller keeps its input.

Before the next release, repeat and document hardening for install, upgrade, disable, uninstall, SteamVR start/stop, standby, source power cycling, runtime/UI crashes, malformed configuration, device reconnection, multiple simultaneous physical sources, and configuration migration.

## Development Practices

- Target Windows x64 for native driver/runtime integration. The UI targets .NET Framework 4.8.
- When a running TrackSwap UI and/or TrackSwap Runtime process locks Release build outputs or otherwise prevents Release build verification, the user explicitly authorizes closing those TrackSwap-owned processes without asking again. Prefer a graceful UI close or Runtime shutdown first, terminate only the exact remaining TrackSwap process when necessary, and report which processes were closed. This authorization does not extend to SteamVR or unrelated processes.
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
