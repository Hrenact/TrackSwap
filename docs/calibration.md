# TrackSwap automatic calibration

Automatic calibration solves the fixed local transform between a physical pose
source and an original physical target:

```text
T_offset = inverse(T_source) * T_target
```

Applying a capture does not switch the route's physical pose source to the
reference target. The virtual proxy continues to follow the selected source,
with the captured rigid offset applied. To follow the reference device itself,
select it as the runtime source and reset the offset to identity instead.

It does not align independent tracking universes. When, for example, a PICO
controller and a Lighthouse tracker do not already share one OpenVR tracking
space, align those spaces first with the appropriate external calibration tool.

## Safety precondition

The target's original pose must remain visible throughout capture. Disable the
`TRKSWAP-PROXY-00` `TrackingOverrides` bootstrap rule before starting SteamVR
for calibration. TrackSwap refuses to start capture while that static rule is
present. This avoids sampling an already-overridden target and deriving a
self-referential offset.

## Capture and validation

The Runtime initializes an OpenVR background client and captures 90 simultaneous
standing-space pose pairs, normally taking about 1.5 seconds. Both devices must
remain connected, awake, and pose-valid. They may move together, but their
relative mounting relationship must not change.

TrackSwap averages relative translations and sign-aligned quaternions, then
rejects a capture when relative stability exceeds either threshold:

- translation RMS greater than 0.015 metres;
- rotation RMS greater than 2 degrees.

At least 60 samples are required by the protocol; the UI requests 90. Capture
times out after 8 seconds if it cannot collect enough valid pairs.

## Profiles

Successful results are stored atomically in `calibration-profiles.json` beside
the Runtime configuration. Names are case-insensitively unique: saving the same
name replaces the prior profile while retaining a `.previous` recovery copy.
The UI can reapply or delete profiles. Deleting a profile does not change the
currently active driver offset; use “reset to identity” to clear that offset.

## Recommended hardware check

1. Exit SteamVR and remove the virtual proxy's static override.
2. Start SteamVR and confirm both original source and target are independently
   visible.
3. Select the intended physical source and physical calibration target.
4. Hold them in the desired rigid relationship and capture.
5. Confirm the virtual proxy overlaps the original target after the result is
   applied.
6. Exit SteamVR, restore the virtual-proxy-to-target bootstrap mapping, and
   restart SteamVR.

If capture fails, no active route or saved profile is changed. If the resulting
pose is wrong, reset the offset to identity and leave the static override
disabled until the source/target direction and tracking-space alignment are
verified.
