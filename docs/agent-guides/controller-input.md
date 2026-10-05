# TrackSwap VR Virtual-Controller Input

Read this guide before changing the fixed `TRKSWAP-CONTROLLER-L` / `TRKSWAP-CONTROLLER-R` devices, virtual-controller profiles, OSC, XInput, haptics, or synthesized skeletons.

## Controller Identity and OpenVR Surface

- Retain TrackSwap VR-owned serials and identity while exposing an Oculus Touch-compatible surface. Left face buttons are `/input/x` and `/input/y`; right face buttons are `/input/a` and `/input/b`; both hands expose `/input/joystick`, `/input/trigger`, `/input/grip`, and standard Touch skeletal paths.
- Trigger and grip expose `value` and `touch`, never synthetic `click`. Expose `/input/system` only on the left and merge the two configured menu states with logical OR. Do not expose Knuckles-only trackpads, force sensors, or per-finger scalar inputs.
- Create and update real `/pose/openxr_aim` and `/pose/openxr_grip` driver components for both hands. Align their local offsets with SteamVR's Quest 2 render models, including mirrored handed X translation.
- Use the handed SteamVR Quest 2 Touch render model when available. If that external model cannot be resolved, retain TrackSwap's packaged square as the visible-device fallback rather than leaving the controller model-less.
- The TrackSwap VR profile includes handed binding images and a `binding_image_point` for every exposed input/pose source. It may reference installed Oculus Touch art, legacy binding, and models while advertising only implemented capabilities.
- Localize the controller type as `TrackSwap VR Controller` / `TrackSwap VR 控制器`. Declare `oculus_touch` using `compatibility_mode_controller_type`; do not also attach the Oculus remapping file, ship app-specific defaults, or impersonate Oculus identities.
- Default `Prop_ControllerHandSelectionPriority_Int32` to `0`. Expose the shared signed 32-bit setting under Advanced Settings with restore-default. Disabled controllers submit disconnected poses so physical controllers can regain roles.

## OSC

- Accept numeric values only for TrackSwap VR's fixed built-in addresses. Do not persist per-control addresses in `runtime-config.json`; ignore legacy address fields while loading. Clamp joystick axes to `[-1, 1]`, trigger/grip to `[0, 1]`, and treat button values above `0.5` as pressed.
- Provide fixed touch-assist addresses `/trackswap/{left|right}/touch/thumb` and `/trackswap/{left|right}/touch/index`. Values above `0.5` temporarily invert the configured idle contact state. Real face/menu/joystick activity and nonzero trigger/grip values take priority.
- Runtime initializes and periodically refreshes a complete explicit touch snapshot so defaults work before packets arrive and after UI exit.
- Keep left/right state independent. A valid packet refreshes only matched hands. Apply all values in a packet, then send at most one snapshot per changed hand. Repeated unchanged values still refresh receiver health but cause no immediate driver IPC.
- Report receive-port bind conflicts explicitly and render OSC health red. The send port is a remote destination, never a local “occupied” port. Report send failures separately and combine simultaneous failures in one tooltip.
- Retain the last valid state indefinitely while its OSC route is enabled. Reset immediately only when OSC disables, relevant routing/configuration changes, the route disables, or Runtime exits.
- Send haptics to `/trackswap/left/haptic` and `/trackswap/right/haptic` with OSC tag `,fff`: duration seconds, frequency hertz, normalized amplitude.
- Keep a three-second right-to-left timeline above each hand card. Amplitude controls height, duration span, and frequency bounded/log-scaled stripe density. New same-hand events replace/truncate the prior envelope. Runtime retains short history so polling cannot miss pulses.
- Poll Runtime haptic history around 30 Hz, but animate cached timelines at WPF composition cadence only while visible and detach callbacks afterward. Put the read-only address below each preview.
- Per-hand `测试` asks Runtime to asynchronously play a fixed recognition pattern ending in a double tap, even without an OSC route. Repeating cancels/restarts that hand's pattern. The UI never sends UDP or blocks for the pattern.
- Keep the phone diagnostic under `tools/` and out of packages. It must receive real Runtime UDP before relaying to a token-protected local WebSocket page. Label browser vibration as approximate and report UDP receipt, browser receipt, and API acceptance separately.
- OSC networking and driver IPC may be asynchronous, but pending components are applied only in the normal driver frame loop. Never do UDP or synchronous pipe work in `RunFrame`.

## XInput

- Poll XInput in Runtime, never in the driver frame loop. Use the lowest connected index. Neutralize only XInput-owned hands immediately on disconnect, source change, Runtime exit, or controller-index change.
- Separate sampling, UI observation, and driver delivery. UI reads take only a short state lock; coalesce driver snapshots so slow IPC cannot block sampling or the 30 Hz monitor.
- Default mapping is symmetric: sticks/clicks to corresponding virtual sticks, shoulders to triggers, analog triggers to grips, View/Menu merged into left system, `X/Y` to left `X/Y`, and `A/B` to right `A/B`. Never publish trigger/grip click components.
- Persist editable mappings and analog threshold in Runtime configuration. Apply standard XInput deadzones and clamp axes to `[-1, 1]`. Digital-to-scalar is exactly `0` or `1`; analog-to-boolean uses one threshold (default `50%`) without release hysteresis.
- Provide independent left/right thumb and index touch helpers with a physical source and `默认接触` / `默认抬起` idle state; holding the source inverts it.
- Real mappings have global priority over touch assistance. If a physical source maps to any real input, ignore it as a helper toggle on both hands and retain the configured idle state.
- `震动映射` offers exactly `保留手别`, `保留手别（镜像）`, and `统一震动`. Normal maps left/right to low/high motors; mirrored swaps; unified sends the stronger hand amplitude to both.
- Create one `/output/haptic` component per hand. Drain events into a fixed allocation-free driver queue during `RunFrame`, transfer through versioned IPC, and call `XInputSetState` only in Runtime. Apply feedback only for XInput-owned hands and honor duration/amplitude.

## Shared Haptic Ownership

- Each input service updates only the hands that explicitly select it. An empty, disabled, or periodic snapshot from OSC, XInput, or `无` must never neutralize or overwrite a hand owned by another input source.
- Runtime polls the driver's destructive haptic queue exactly once and fans each batch to OSC and XInput. Multiple services must never poll it independently.
- Stop all affected motors/output immediately on disconnect, route/input-source change, Runtime exit, or controller-index change.

## Synthesized Skeletons

- Expose standard 31-bone left/right SteamVR skeletons. Generate `WithController` and `WithoutController` streams in the driver frame path without IPC or allocation.
- Follow Oculus Touch semantics: distinct thumb landing poses from face/system/menu/joystick/thumb-rest activity; index from trigger touch/value; middle/ring/pinky from grip touch/value. Synthesize contact from buttons and analog activity when capacitive channels do not exist.
- Vary splay continuously so extended fingers spread slightly and curled fingers converge; smooth changes over a short bounded interval.
- Drive trigger/grip animation from analog values and explicit/synthesized touch, never click fallbacks.
- The simulator derives from OpenVR's `handskeletonsimulation` sample pinned by `TRACKSWAP_OPENVR_REVISION`. Preserve Valve's license and exact revision in `THIRD-PARTY-NOTICES.md`.
