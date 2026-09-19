# Real-time pose preview

The preview polls downsampled route telemetry at approximately 30 Hz and renders
the two physical devices a user needs to compare:

- the selected physical pose source;
- the final physical target.

The transformed virtual output remains part of telemetry and routing diagnostics,
but is not rendered as a third user-facing object. The proxy is a compatibility
layer, not another device the user should have to interpret.

The UI reads `Prop_RenderModelName_String` for connected devices and loads the
registered static mesh and RGBA texture through `IVRRenderModels_006`. Component-
based controller models are assembled from their registered parts. Model data is
copied, cached, and released outside the telemetry path. If a driver does not
publish a usable model, SteamVR is stopped, or loading fails, the UI falls back to
a simplified HMD, controller, tracker, or generic marker. Only fallback geometry
uses device coloring and local coordinate axes; normal render models retain their
own textures.

The source and target are framed together around their combined center. The view
does not inherit device rotation: drag with the left mouse button to pan, drag with
the right mouse button to orbit, use the wheel to zoom, and choose `重置视图` to
restore the default camera.

## Isolation from tracking

The OpenVR server driver copies the latest poses into a small mutex-protected
telemetry snapshot after it has calculated the output pose. A separate named-
pipe worker serves that copy. Runtime forwards explicit `getTelemetry` requests;
the WPF UI limits itself to one outstanding request and polls at about 30 Hz.

No telemetry request is made from `VirtualTracker::Update`, and the driver never
waits for Runtime or the UI. Closing, freezing, or disconnecting the preview
therefore cannot stop pose submission. A failed telemetry request only changes
the preview status to unavailable. Render-model loading also runs outside the
driver and Runtime, so a missing or malformed model can only trigger the built-in
visual fallback.

## Hardware check

1. Start SteamVR, Runtime, and TrackSwap with the packaged driver installed.
2. Confirm the source and final target models appear and retain their registered
   textures. Confirm that no third virtual-proxy model is shown.
3. Drag and zoom across the entire preview viewport, then reset the view and
   confirm both models are framed around their combined center.
4. Apply a known local offset and verify the target's relative pose agrees with
   SteamVR while device motion does not reset the camera.
5. Power the source off and on. The preview may become unavailable while the pose
   is invalid, but tracking must recover automatically when the source returns.
6. Close TrackSwap and confirm SteamVR tracking continues normally.
