# Real-time pose preview

The Stage 7 preview renders three driver-owned pose snapshots at approximately
30 Hz:

- physical source (orange);
- transformed virtual output (blue);
- physical device represented by the configured target role (green).

The UI reads `Prop_RenderModelName_String` for connected devices and loads the
registered static mesh and RGBA texture through `IVRRenderModels_006`. Source
and virtual output share the source device model. A role target uses the model
of the physical device currently assigned to that role. Model data is copied,
cached, and released outside the telemetry path. If a driver does not publish a
model, SteamVR is stopped, or loading fails, the UI falls back to a simplified
HMD, controller, tracker, or generic marker.

Each model includes local X/Y/Z axes colored red, green, and blue. The source,
output, and target retain orange, blue, and green tinting respectively. When
the target pose is valid, the view uses it as the origin; source and output are
the fallback anchors if the target is unavailable. The preview reports the
output-to-target translation distance when both are valid.

## Isolation from tracking

The OpenVR server driver copies the latest poses into a small mutex-protected
telemetry snapshot after it has calculated the output pose. A separate named-
pipe worker serves that copy. Runtime forwards explicit `getTelemetry` requests;
the WPF UI limits itself to one outstanding request and polls at about 30 Hz.

No telemetry request is made from `VirtualTracker::Update`, and the driver never
waits for Runtime or the UI. Closing, freezing, or disconnecting the preview
therefore cannot stop pose submission. A failed telemetry request only changes
the preview status to unavailable. Render-model loading also runs outside the
driver and Runtime, so a missing or malformed model can only trigger the
built-in visual fallback.

## Hardware check

1. Start SteamVR, Runtime, and TrackSwap with the Stage 7 driver installed.
2. Confirm all three markers appear and their local axes rotate with the devices.
3. Apply a known offset and verify the blue output separates from the orange
   source as expected.
4. When source and target are calibrated together, verify the blue and green
   markers overlap and the displayed distance is small.
5. Close TrackSwap and confirm SteamVR tracking continues normally.
