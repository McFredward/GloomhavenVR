# Standalone confirmation scale validation

Scope: bounded follow-up to the Build 753 and new Build 758 ESC reports. Build
754 already corrected the native-origin versus fitted visible-center mismatch;
the current hardware report demonstrates that this was not the only problem.
The new production change is restricted to the blackout ownership predicate in
`ModalFallback.11.PreConvertHide.cs`. No placement, clip policy, native interaction
permission or shared-window metric changes.

## Current Build 758 failure

The main hardware log identifies ModBuild 758. Its lines 7189 and 7197 show the
original `Confirmation Box_MainMenu_pc` being adopted by its actual native
`UI Scenario Esc Menu` producer and revealed at frame 9285. Lines 7198–7201 then
show the pre-convert blackout switching that same original Canvas off again in
the same frame. The late float-intent guard reports `tracked=True,
floated=False` even though the persistent producer-owned conversion exists.
The generic `Converted` list deliberately does not contain this member.

`HasPreConvertPresentation` now recognizes either a generic conversion or the
exact original claimed by `ConfirmationOnOwner.IsOwned`. The initial blackout,
standing-entry reassertion, hand-back and stand-down lifecycle use this one
predicate. Genuine unconverted windows still run their existing blackout body;
no choice, callback, native Show/Hide or network ACK is synthesized. The census
keeps `floated` as the actual generic-list result and separately prints
`ownedConfirmation`, making the two ownership paths distinguishable.

The separate exit-confirmation worker owns actual blackout/capture consumer
reproduction and its compiling historical control. This document's standalone
results below do not certify that new consumer fix before that proof completes.

## Executed production and boundaries

The fixture executes the complete actual `DialogSurface`, including delayed
first placement, pooled close/reopen and the source-bound actual
`CanvasConversion.PlaceHost` implementation. Its original native confirmation
layout, images, font/SDF shader, actual Unity perspective camera, final pixels and
uGUI `GraphicRaycaster` execute. The independent native Yes/No callback remains
unchanged unless the pre-existing explicit pointer action is invoked.

Converter allocation, native manager/session bootstrap, active `WorldScale` and
the converter's live camera binding remain declared surrounding inputs. No live
OpenXR rig, real gameplay choice, Steam multiplayer transport, `RayUguiDriver`
arbitration or HMD display executes here. The private standalone surface has no
recorded native producer; ESC producer ownership is covered by the separate
cropped-owner fixture owned by the other worker. Captured owned-window rendering
is not certified by this direct original-graphics test.

## Scale cases

The hardware scenario's base scale is 47.69. The native comfort minimum `.02`
is a multiplier of that base, not `.02` absolute game units per tracked meter.
Each of `.9538` (`47.69 × .02`), `2.3845` (`× .05`), `9.538` (`× .2`), `1`,
`10`, `47.69`, and `200` game units per tracked meter:

1. Closes the previous pooled confirmation and checks its exact original home.
2. Opens the same original while the camera is unavailable for two ticks, retaining
   one conversion and no invented pose.
3. Supplies a perspective camera and checks exactly one production first pose,
   original `.75 × rigScale` reading distance and `.001 × .7 × rigScale` world
   units per pixel.
4. Renders a final PNG and an independently imported original foreground at the
   same physical metric, comparing glyph presence. It checks both original option
   centers inside the viewport and both actual native graphic hits.
5. Checks stable apparent framing and that no extra native decision was created.

The independent camera inputs follow the real rig's existing policy:
`near = clamp(.05 × rigScale, .01, .5)`. A valid scenario with build scale 1,
base far 100 and no sky budget uses
`far = min(max(100, 100 × rigScale), near × 50000)`. These are not inferred from
the dialog bounds; the 200-scale camera therefore retains its legitimate
20,000-unit far plane and covers the 150-unit reading distance. Perspective
remains enabled throughout; an orthographic size is never used for this proof.

The existing late-camera and repeated-placement compiling defect controls remain
unchanged. They must still fail their original precise assertion before the new
scale cases.

## Result and retention

Focused production and both existing controls passed: **113 actual
native/Unity/pixel assertions and two compiling causal controls**. Evidence is
`debug/standalone-popup759/run-yiz24zl6`. All seven current and independent
original foregrounds contained exactly 84 bright glyph pixels; both native
option hits passed at every covered metric. Minimum-scenario and 200-scale
perspective PNGs were inspected. CSV rows retain exact scale, clip planes, host
metric, center distance and both projected option centers.

Four exploratory failures are retained, not presented as production defects:

- `run-6s0amsnu`: the first literal `.05` absolute-scale direct-render probe
  lost native glyphs.
- `run-8bhyvm67` and `run-rwufz4zy`: literal `.02` probes found both current
  and independently imported original SDF glyphs absent. Temporarily comparing
  two canvases introduced an avoidable graphic-depth ambiguity, so actual hits
  were subsequently moved before that comparison.
- `run-d_80h0g8`: before any reference toggling, actual native
  `RectangleContainsScreenPoint` rejected the Yes graphic's own projected center
  at `.02` absolute rig scale / `.000014` world units per pixel. Graphic depth,
  activation, raycast flag and camera were valid. This primitive is below the
  reported `.0012` host metric and is not evidence against the actual captured
  UI/input consumer. The fixture does not hide this missing hit behind a passing
  parity check: those unsupported absolute-unit diagnostic inputs are excluded
  from its production certification. The real `.02` comfort minimum for the
  hardware scenario remains included as `.9538` units per tracked meter.

These observations do not prove arbitrary tiny native geometry safe in all
possible game scenes. The actual normalized capture/input route needs its own
consumer proof; no blanket runtime workaround was added for this direct-port
floor. The first-frame blackout defect above has separate current-log evidence.

Every run reaped its editor/compiler processes and used canonical
`cleanup_completed_run`; the passing run removed 8,242,146 cache bytes without
skips. The private ADB endpoint is 5070, separate from the other worker's 5069.
No user's Frame or shared ADB endpoint is used. Parent owns final strict build,
source gates, affected-suite qualification and integration; no worker push.
