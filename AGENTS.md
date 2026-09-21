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
   - The current UI uses a configuration sidebar; each selected route owns its source, proxy slot, target, offset, calibration view, status, and always-visible 3D preview.

3. **Direct virtual-tracker output**
   - v006 routes support a direct mode that publishes the transformed source pose as the stable TrackSwap virtual tracker without a `TrackingOverrides` target.
   - Direct output is the default for newly created routes. Existing configurations without an explicit mode retain target-replacement behavior for compatibility.
   - Target replacement remains available when the user needs the target device to keep its original controller input.

4. **OSC virtual-controller output**
   - v006 routes may output one fixed virtual left controller and one fixed virtual right controller.
   - Physical devices provide pose while Runtime maps explicitly configured OSC addresses to basic controller inputs.
   - The virtual controllers use TrackSwap-owned identities and an input profile compatible with Knuckles bindings; they must not impersonate Valve serial numbers.

The default runtime data path is:

`physical source -> native driver transform -> TrackSwap virtual proxy`

The optional target-replacement path is:

`physical source -> native driver transform -> TrackSwap virtual proxy -> SteamVR target override`

The virtual-proxy-to-target relationship is a static bootstrap mapping. Changing a route's physical source or offset is a live Runtime operation and must not repeatedly rewrite `steamvr.vrsettings`.

## Branch and Release Policy

- `v001`, `v002`, `v003`, `v004`, `v005`, and `v006` are published, immutable release tags. Never move or rewrite them.
- `main` currently remains the stable v001 line. Do not merge, retarget, or rewrite it without an explicit maintainer decision.
- Current post-v006 work remains on `v002-runtime` until the maintainer chooses the integration branch for the next release.
- The next release identifier is `v007`. Releases use one increasing sequence: `v001`, `v002`, `v003`, and so on. Do not introduce semantic-version labels or prerelease suffixes unless the maintainer changes this policy.
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
   - Edits named routes and offsets, requests source switches, manages calibration profiles, displays status, and renders downsampled telemetry.
   - Retains the legacy static settings workflow under Settings.
   - Must not participate in the per-frame tracking path.

2. **TrackSwap Runtime**
   - Independent configuration and control process.
   - Owns persistent runtime configuration, IPC, validation, source discovery, calibration profiles, and telemetry fan-out.
   - Sends versioned, atomic snapshots for every virtual slot.
   - Must recover cleanly if the UI closes or reconnects.
   - Supports two explicit lifecycle policies selected under Advanced Settings: follow the TrackSwap UI process, or follow the SteamVR session. SteamVR-follow mode must still accept a live UI process as a temporary owner so offline configuration remains available while SteamVR is stopped.
   - Owns OSC UDP reception, address mapping, normalization, silence timeouts, and controller-input snapshot delivery. OSC networking must never run inside the driver frame loop.

3. **TrackSwap OpenVR Driver**
   - Minimal native SteamVR server driver exposing stable virtual GenericTrackers and the fixed `TRKSWAP-CONTROLLER-L` / `TRKSWAP-CONTROLLER-R` controller pair.
   - Reads raw poses from selected physical sources, applies transforms, and submits virtual poses.
   - Keeps the last valid configuration if Runtime or UI disconnects.
   - Keeps the per-frame path local, deterministic, allocation-light, and non-blocking. Never require a synchronous IPC round trip for every pose update.
   - May request the packaged Runtime once during driver initialization so SteamVR-follow mode works without the UI. Process launch and path discovery must remain outside the per-frame path, use only package-relative trusted executables, and tolerate a missing Runtime without failing driver initialization.

## Route and Proxy Model

- Support at most eight virtual slots, identified by stable serials `TRKSWAP-PROXY-00` through `TRKSWAP-PROXY-07`.
- Additionally support at most one virtual left controller and one virtual right controller. Reject duplicate hand assignments across routes.
- Register a proxy on demand when an enabled route first uses its slot. Do not expose unused slots merely because capacity exists.
- A route's user-facing name is independent from its route id and proxy slot. Names must be unique case-insensitively and remain renameable.
- New routes use the lowest available slot and a default name such as `新配置`, `新配置 (1)`, and so on.
- Disabled or missing routes must submit invalid/disconnected tracking rather than a stale healthy pose.
- Every route explicitly selects direct virtual-tracker output, target replacement, or virtual-controller output. Direct routes require a source but no target and must not create a `TrackingOverrides` entry.
- Virtual-controller routes require a source pose, an explicit left/right hand, and an explicit control-input source. They do not create a `TrackingOverrides` entry.
- Virtual-controller control input may explicitly be `无` or `OSC`. `无` keeps the controller pose active while holding every button and analog component at its neutral value; OSC packets must not change that hand until the route explicitly selects `OSC` again.
- New routes default to direct virtual-tracker output. Configurations written before the mode field existed must deserialize as target-replacement routes.
- Configuration revisions are atomic across all slots. An older snapshot must never replace a newer accepted revision.
- Reject duplicate slots, conflicting targets, cycles, and other ambiguous routing rules.
- Reject role self-reference such as a physical right-hand controller routed through a proxy back to `/user/hand/right`. SteamVR can feed the overridden target pose back as the source and freeze the route on a previous frame.
- Hide SteamVR's generic head, left-hand, and right-hand role targets from runtime target selectors by default. They are an explicitly opt-in advanced UI option only; enabling visibility must not bypass self-reference, cycle, conflict, or migration validation.
- A proxy's first target binding, target change, disable cleanup, or deletion cleanup may require editing `TrackingOverrides`; perform that edit only while SteamVR is fully stopped.
- Treat the saved Runtime route configuration as the desired static-mapping state. When a new route or target change is applied while SteamVR is running, persist it as pending work and automatically reconcile `TrackingOverrides` after SteamVR fully stops; never require a second click on Apply for the same change. Pending deletion has priority over pending mapping: finish or resolve every deletion before reconciling mappings, and remove orphaned TrackSwap proxy mappings that no longer correspond to an enabled route.
- Deleting a route while SteamVR runs must persist a pending-deletion state and keep the proxy output active. After SteamVR stops, remove the static mapping first and only then remove the Runtime route. Preserve the pending state on failure and allow cancellation.
- Hot source and offset changes for an already bootstrapped proxy must work without restarting SteamVR.

## UI Behavior

- Use the sidebar as the primary route navigation and management surface.
- Keep Settings organized behind a compact internal category rail. Group environment/runtime status, SteamVR maintenance tools, and device-history management by responsibility instead of appending unrelated cards to one long page.
- Keep OSC transport, silence timeout, receiver health, and the fixed left/right control-address reference in a dedicated Settings category. Loopback is the default binding because OSC has no authentication or encryption.
- Show compact live OSC monitors beside mapping fields: boolean inputs use an on/off center bar, one-sided scalar inputs use a left-to-right fill bar, and each joystick shares one square two-axis crosshair for its X/Y addresses. Size the square to span from the top of the X field to the bottom of the Y field, give every bar the same width as that square, and refresh at approximately 30 Hz only while the OSC Settings page is visible. Keep this observational polling outside the driver frame path.
- Treat the built-in OSC parameter addresses as read-only reference text: users may select and copy them, but do not edit them in place. Apply the OSC enable state, endpoint, port, and reset timeout automatically; debounce free-form endpoint edits instead of requiring an Apply button or restarting the listener on every keystroke.
- Keep `Runtime 启停行为` under Advanced Settings as a compact selector with exactly `跟随 TrackSwap` and `跟随 SteamVR`. SteamVR-follow mode must still let an open TrackSwap UI temporarily own Runtime so configuration edits and stopped-SteamVR reconciliation work when both Runtime and SteamVR were initially closed.
- Keep TrackSwap's SteamVR-follow UI behavior as a separate, default-off Advanced Settings checkbox. Register the permanent OpenVR application manifest and auto-launch preference when SteamVR is available, retain the driver/Runtime launch fallback for the first session, avoid duplicate UI processes, and close the UI as soon as SteamVR is fully stopped and pending deletion or mapping reconciliation has completed. Keep the UI open on reconciliation failure instead of hiding an unresolved state.
- With no routes, hide route controls and show the centered empty state `暂无配置，请新建。`.
- Keep the established **Dark Utility** visual language: neutral charcoal/graphite surfaces, low-contrast layers, compact 4/8 px spacing, restrained sans-serif hierarchy, subtle 1 px separators, 2–6 px radii, compact rectangular controls, and one clear blue accent. Avoid blue-tinted backgrounds, gradients, glass effects, decorative shadows, giant rounded cards, pill buttons, oversized headings, and marketing-style empty space.
- Treat `src/TrackSwap/Assets/TrackSwap.svg` as the application icon master. Preserve its single centered triangular tracker, flat charcoal/blue/mint palette, uniform geometry, and legibility at 16 px; regenerate derived PNG and ICO assets with `scripts/generate-icon.ps1` after changing the master.
- Request a dark native Windows title bar for every app-owned window and dialog, with caption, border, and text colors aligned to the neutral application palette. Treat unsupported DWM attributes as a harmless platform fallback.
- Keep the dark theme explicit across every detached or system-hosted surface. Tooltips, context menus, popups, ComboBox drop-downs, and dialogs must define compatible foreground, background, border, and selection states instead of inheriting Windows theme defaults.
- Use the shared `AppDialog` for application messages and confirmations. Do not add new calls to the system `System.Windows.MessageBox`; new dialogs must preserve the Dark Utility palette, compact geometry, application icon, semantic status color and Windows system sound, keyboard defaults, and dark native title bar.
- Remember that the implicit `TextBlock` style can make text light even inside a system-default white popup. After adding or changing any popup-like UI, inspect the actual rendered state and reject white-on-white, black-on-black, or otherwise low-contrast combinations.
- Visual UI verification must cover normal, hover, selected, disabled, validation, and popup/tooltip states where applicable. A successful XAML build is not sufficient evidence that themed UI is readable.
- Initialize short-lived observational OpenVR clients used for device discovery or render-model loading as utility applications. Do not reconnect them as background applications: SteamVR will repeatedly reload application input bindings and can overwhelm its controller/settings web UI.
- Keep the selected route's 3D preview visible; do not make it collapsible.
- Prefer the device render model registered with OpenVR, but always retain built-in HMD, controller, tracker, and generic fallbacks. Loading or rendering a model must remain UI-only and observational.
- In the preview, render the physical pose source and final target; do not render the virtual proxy as a third user-facing object. The proxy is a compatibility and routing layer.
- The preview is observational. Closing, freezing, or overloading it must not affect submitted tracking poses.
- Show physical source, stable virtual proxy, target, connection health, active offset, and pending/applied state distinctly.
- Device selectors use connection state only: represent every connected physical device with a green status dot and every remembered but disconnected physical device with an orange status dot, including the device currently referenced by a route. Do not render `当前`, `历史`, `在线`, or `离线` as textual device-state prefixes. Persist discovered physical-device metadata in a small local JSON record, exclude TrackSwap virtual proxies, deduplicate by exact OpenVR device path, and provide a Settings action to clear unreferenced records while preserving route selections.
- Never implicitly choose the first available device for a new route or a newly revealed mode-specific selector. Show `请选择` until the user makes an explicit choice. When changing route modes, clear selections that belong to the newly selected mode instead of carrying over or guessing a device; apply the same rule to future modes and mode-specific fields.
- Refresh physical-device selectors automatically at a low frequency while the UI is open. Only rebuild selector contents when the device catalog actually changes, defer updates while a related drop-down is open, and retain the last successful catalog across transient OpenVR enumeration failures to avoid flicker or accidental selection changes.
- Keep dangerous operations reversible and explain required SteamVR restarts or stopped-state writes before the user acts.
- Preserve target input from the original device; TrackSwap replaces pose, not controller input.
- Keep the legacy v001 settings workflow clearly separated from runtime route management.
- In the legacy v001 editor, new static rules must use explicit concrete `/devices/...` targets. Show `/user/...` role targets only when an existing rule already references them, label them as legacy maintenance entries, and do not allow them to be written as new rules.

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

## OSC Controller Input

- Expose the Knuckles-compatible basic controls with `/input/a` and `/input/b` on both hands, `/input/thumbstick` for the stick, `/input/trigger` for the trigger, and `/input/grip` value/force/touch for grip. Do not invent left-hand X/Y controls or use `/input/joystick` for the Knuckles-compatible profile.
- Keep the TrackSwap-owned controller profile complete enough for SteamVR's binding and controller-test UI: declare handed binding images and a `binding_image_point` for every exposed input/pose source. It may reference the official Index controller artwork and remapping resources while retaining the `trackswap_controller` identity and only advertising implemented capabilities.
- Accept numeric OSC values only for TrackSwap's built-in fixed addresses. Do not persist per-control addresses in `runtime-config.json`; legacy address fields are ignored when older configurations are loaded. Clamp joystick axes to `[-1, 1]`, trigger/grip values to `[0, 1]`, and interpret button values above `0.5` as pressed.
- Keep OSC input state independent for the left and right controller. A valid message refreshes only the hand whose mapping matched it.
- The selectable silence timeouts are `永不`, `1 秒`, `5 秒`, `30 秒`, and `1 分钟`; default to `5 秒`.
- When a finite timeout expires, submit a complete neutral snapshot for that hand. Always reset immediately when OSC is disabled, mappings change, a controller route is disabled, or Runtime exits, regardless of the selected timeout.
- OSC reception and driver IPC may run asynchronously, but the driver applies pending input components only from its normal frame loop. Never perform UDP or synchronous pipe work in `RunFrame`.

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
- Downsampled per-route telemetry drives the source-and-final-target 3D preview outside the tracking-critical path; the virtual output remains available to telemetry but is not rendered as a third user-facing device.
- The preview loads static device meshes and textures through `IVRRenderModels_006`, supports component-based controller models, and falls back to built-in device-class geometry without affecting tracking.
- Virtual proxies use the visible driver-owned `trackswap_proxy_tracker` render model in direct-output mode and switch to the empty `trackswap_hidden_proxy` render model in target-replacement mode. The empty model prevents SteamVR from drawing a misleading GenericTracker fallback while preserving tracking, routing, and telemetry; the hidden-model behavior has been hardware-verified.
- Virtual proxies publish driver-owned named 32 px status icons for every OpenVR device state. Do not allow SteamVR to fall back to the GenericTracker hexagon-and-`T` icon; keep these transparent status assets visually consistent with the TrackSwap tracker mark.
- Multiple proxy slots can be registered in one SteamVR session and receive independent route snapshots.
- Deleting a route while SteamVR is stopped removes its static proxy mapping without disturbing other routes.
- Deleting a route while SteamVR is running marks it pending, preserves live tracking, and automatically removes its static mapping and Runtime route after SteamVR stops.
- Hardware testing has confirmed Tracker-to-hand pose replacement while the original hand controller keeps its input.

Before the next release, repeat and document hardening for install, upgrade, disable, uninstall, SteamVR start/stop, standby, source power cycling, runtime/UI crashes, malformed configuration, device reconnection, multiple simultaneous physical sources, and configuration migration.

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
