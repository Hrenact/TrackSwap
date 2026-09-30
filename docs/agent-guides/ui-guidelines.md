# TrackSwap UI Guidelines

Read this guide before changing the WPF UI, dialogs, previews, selectors, settings pages, or visual assets. For user-facing text, also read [`../localization.md`](../localization.md).

## Route Editing and Navigation

- Use the sidebar as the primary route navigation and management surface.
- Sidebar items communicate enabled, disabled, and pending lifecycle state. The selected-route header shows only the concise synchronization state (`配置已同步`, `待应用`, pending mapping/removal, or pending deletion); do not repeat it inside the route card.
- Route editing is auto-apply. Complete mode/source/target/controller selections submit immediately; numeric offset typing uses a short debounce. Keep incomplete numeric input local and show a concise header state without a modal interruption.
- When validation rejects a complete edit, show one Chinese shared `AppDialog` warning and wait for another user edit before retrying. Do not restore route-level `重新读取`, `应用更改`, or `应用配置` buttons. Gizmo release and offset reset submit immediately.
- Local-pose fields accept only ASCII digits, one decimal point, and an optional leading minus sign. Reject other typed or pasted characters before they reach the field.
- With no routes, hide route controls and show the centered empty state `暂无配置，请新建。`.
- Never implicitly select the first mode or device for a new route, nor the first option in a newly revealed mode-specific selector. Show `请选择` until the user decides. Changing mode clears fields owned by the new mode rather than guessing or carrying them over.
- Keep the legacy v001 settings workflow visibly separate from Runtime route management. New legacy static rules require concrete `/devices/...` targets; show `/user/...` roles only for existing legacy maintenance entries and never write them as new rules.

## Settings and Destructive Operations

- Organize Settings behind a compact category rail. Group environment/runtime health, SteamVR tools, device history, OSC, XInput, language, and files by responsibility.
- Keep a dedicated `文件管理` category. Show the active data root, every known TrackSwap-owned file, and external SteamVR/OpenVR files TrackSwap references or modifies. Label paths as inside or outside the installation directory and provide Explorer actions. Never describe a shared SteamVR file as wholly owned by TrackSwap.
- Keep `重置数据` and `预卸载准备` as separate dark-red cards at the bottom of `文件管理`. Reset removes TrackSwap mutable data and TrackSwap-created mappings while preserving registration. Pre-uninstall additionally unregisters driver/application integration. Neither launches an uninstaller, removes package files, or silently restores integration later.
- Both cleanup actions require SteamVR fully stopped, one Chinese `AppDialog` warning with a Windows system sound, Runtime shutdown, cleanup, and UI exit. While running, cover the whole client area with a non-dismissible black overlay; block interaction and closing; suspend lifecycle polling, Runtime restart, and SteamVR-follow auto-close; name the operation in the overlay. Resume normal lifecycle handling only after failure removes the overlay. Preserve unknown and unrelated files/state.
- Export backups only to a user-selected path. Include complete Runtime configuration and UI preferences, but exclude device history, logs, diagnostics, shared SteamVR configuration, and prior backups. Validate before import, warn that device paths may be sensitive, create an automatic rollback backup, confirm through one Chinese `AppDialog` with system sound, and restore the previous UI/Runtime state if any step fails. Import through Runtime; never replace `steamvr.vrsettings` from a backup.
- Keep runtime status and support diagnostics under `运行与诊断`. Exports may include versions, health, route-count summaries, validation results, and TrackSwap-filtered recent logs. Never export raw runtime configuration, device history, full SteamVR logs, physical paths, usernames, or serial numbers.
- Keep dangerous actions reversible and explain required SteamVR restarts or stopped-state writes before acting.

## Lifecycle and Status

- Keep `Runtime 启停行为` under Advanced Settings with exactly `跟随 TrackSwap` and `跟随 SteamVR`. An open UI remains a temporary Runtime owner for offline configuration. Runtime stays alive while stopped-SteamVR reconciliation is pending regardless of policy.
- Keep SteamVR-follow UI behavior as a separate default-off checkbox. Enabling registers the dashboard manifest, identifies the UI process, and enables auto-launch. Disabling removes registration and auto-launch so SteamVR cannot terminate a manually opened UI. Avoid duplicate UI processes and retain the driver/Runtime fallback for the first enabled session.
- In SteamVR-follow mode, close the UI only after SteamVR is fully stopped and Runtime reports reconciliation complete. Runtime owns retry and unresolved-state preservation.
- Keep a thin full-width bottom status bar for SteamVR, Runtime, OSC, XInput, and physical-source hiding. Keep the route limit beside Add. Use gray for inactive/unavailable, orange for enabled but waiting, green for healthy, and red for failed hiding; expose details in dark tooltips.
- Initialize observational OpenVR discovery/render-model clients as Utility applications. Reuse one process-wide Utility session for the UI lifetime and release it when SteamVR stops or the UI closes. Do not cycle Init/Shutdown or reconnect as a background application.
- Poll that session for `VREvent_Quit` at UI cadence and call `VR_ShutdownInternal` before SteamVR finishes stopping. Suppress reconnection until the stopped session has been observed.

## Visual Language and Shared Controls

- Preserve the **Dark Utility** language: neutral charcoal/graphite surfaces, low-contrast layers, compact 4/8 px spacing, restrained sans-serif hierarchy, subtle 1 px separators, 2–6 px radii, compact rectangular controls, and one clear blue accent.
- Avoid blue-tinted backgrounds, gradients, glass, decorative shadows, giant rounded cards, pill buttons, oversized headings, and marketing-style empty space.
- All standard `ComboBox` controls use the shared template and a fixed `30 px` arrow column. Do not restore `38 px` or add page-specific arrow widths.
- Preserve natural compact-control height. Fix local stretch/alignment problems locally instead of globally enlarging already-correct controls.
- Request a dark native title bar for every app-owned window/dialog. Unsupported DWM attributes are a harmless fallback.
- Explicitly theme tooltips, context menus, popups, drop-downs, and dialogs. Inspect rendered popup contrast because the implicit `TextBlock` style can otherwise cause white-on-white or black-on-black text.
- Use shared `AppDialog` for all application messages and confirmations. Do not add `System.Windows.MessageBox`. Preserve compact geometry, application icon, semantic status color, Windows system sound, keyboard defaults, and dark title bar.
- Visual verification covers normal, hover, selected, disabled, validation, popup, and tooltip states where applicable. A successful XAML build is not sufficient.

## Icons and Assets

- `src/TrackSwap/Assets/TrackSwap.svg` is the application icon master. Preserve the centered triangular tracker, flat charcoal/blue/mint palette, uniform geometry, and 16 px legibility. Regenerate PNG/ICO assets with `scripts/generate-icon.ps1`.
- `src/TrackSwap.Driver/assets/trackswap_controller.png` is the original 1024 px RGBA right-hand controller status-icon master. Preserve its exact head, diagonal mark, handle, transparency, and proportions. Use it unchanged for the right hand and mirror only for the left. Recolor only the outline/status colors using the tracker palette.
- Regenerate all 32 px SteamVR tracker/controller status assets with `scripts/generate-driver-icons.ps1`. Never use the tracker glyph or SteamVR generic fallback for virtual controllers.

## Preview and Pose Editing

- Keep the selected route's 3D preview always visible and beside the numeric local-pose editor. Do not restore automatic-calibration UI.
- Store and display `PoseOffset` translation in centimetres. Convert to metres only at the driver-control boundary; raw OpenVR math and telemetry stay in metres. Do not add migration or dual-unit interpretation.
- The `调整工具` selector is ordered `隐藏`, `移动`, `旋转`, defaulting to `隐藏`. Hiding it does not clear offsets. Dragging updates fields and local preview continuously; release persists through auto-apply. Shift enables fine adjustment.
- Anchor the gizmo only to the configured virtual output in its local axes. Its visibility, pose, and framing do not depend on proxy-model rendering and it never moves to the source or final target.
- Prefer OpenVR-registered render models but retain built-in HMD/controller/tracker/generic fallbacks. Model loading is UI-only and observational.
- Render the physical pose source and final target, not a third user-facing proxy. Preview failure, freezing, or overload must never affect submitted tracking.
- Show source, stable virtual output/proxy, target, health, active offset, and pending/applied state distinctly.

## Device Selectors and Physical-Source Hiding

- Device selectors use dots only: green for connected and orange for remembered/disconnected, including currently referenced devices. Do not prefix entries with `当前`, `历史`, `在线`, or `离线`.
- Persist discovered physical-device metadata in a small local JSON file, exclude TrackSwap virtual devices, deduplicate by exact OpenVR path, and let Settings clear unreferenced records while preserving route choices.
- Refresh the physical-device catalog automatically at low frequency. Rebuild controls only when the catalog changes, defer while related drop-downs are open, and retain the last successful catalog across transient enumeration failures.
- Physical-source hiding is an opt-in Advanced Runtime feature. Its global switch reveals the per-route `隐藏物理位姿来源设备` request. Disabling globally clears all requests; changing a source clears that route's request; TrackSwap-owned devices may never be hidden. Warn separately before hiding an HMD.
- Runtime owns requests and the driver hook remains active after UI exit. The hook fails open on conflict/partial failure, and route output uses the pre-hidden source pose while subtracting TrackSwap's hiding displacement.
- Preserve target input from the original target device: TrackSwap replaces pose, not controller input.

## OSC and XInput Settings Surfaces

- Keep OSC transport/health and fixed control/haptic address reference in a dedicated category. Loopback is the default because OSC has no authentication or encryption. Show separate receive and send ports.
- Built-in OSC addresses are selectable read-only text. Do not add an OSC enable switch; derive activation from enabled OSC controller routes. Auto-apply address and receive-port changes, debouncing free-form address edits.
- OSC monitors refresh around 30 Hz only while their page is visible. Booleans use an on/off center bar, one-sided scalars a left-to-right bar, and joystick X/Y one square crosshair. Give every bar the square's width and size the square from the X field top to the Y field bottom.
- Keep XInput in its own category immediately below OSC with left/right columns, live page-visible monitors, auto-apply, and one restore-default action.
