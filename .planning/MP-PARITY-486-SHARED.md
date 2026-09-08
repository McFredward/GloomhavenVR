# 1:1 review: shared windows and map presentation — ModBuild 486

## Scope and approved exceptions

Read the scenario story and map story/quest/encounter synchronization paths, shared frame and
size laws, map-room presentation and gaze ownership, and the shared-window spawn anchor.
The existing record-19 seat-anchor and record-21 parchment-frame conversions retain their
established coordinate contracts. The shared-window initial anchor is spent when any user moves
it, consistent with the recorded narrowing in build 243: "ich meine nur die initiale
Spawnposition - es soll weiterhin von jedem Verschiebar sein".

Mixed-language key-based versus owner-text localization is explicitly approved in
`Core/Loc/Loc.cs`: "Das die Sprache gemixed ist finde ich OK, respektiert sogar noch mehr die
1:1 Regel, daher finde ich FAS sogar gut. lass es so." This is not a license to change other
content, layout or animation.

## Source-proven gaps and repairs

1. **Repeated shared-window drags could remain frozen until release.** TrackFrame kept the
   previous pose stamp during the entire drag and incremented it only after settling. ResolvePose
   elects ownership from each stamp's first arrival time. A player who had moved the window since
   that old stamp outranked every intermediate frame of the next drag. Both scenario and map
   paths now claim a new stamp on the first movement edge and retain a completion stamp.
2. **Remote movement and resizing stepped at packet arrival.** ResolvePose wrote each received
   target immediately. The shared `SharedWindowPoseTrack` now interpolates actual received
   positions, rotations and sizes, without extrapolation or duplicate restarts. Interpolation
   runs in the transmitted shared frame, before local zoom/seat projection. It retains the
   currently displayed pose when retargeted and resets for window/owner/frame identity changes.
3. **A stationary grip lost protection after 250 ms.** The existing comment claimed there was no
   grabbed flag, although `GrabbableModal.IsGrabbed` now exists. TrackFrame could settle a window
   still held by a hand, and ResolvePose could then accept a competing peer write. Both paths now
   protect the actual grip until release.
4. **Small visible movements were discarded.** The idle 5 mm / 0.5 degree thresholds also
   governed an actively held window and remote pose application. Held/finishing motion now samples
   every changed value; the receiver writes every changed interpolated value through the existing
   shared size law. Idle ownership detection retains its noise guard so receiver writes cannot
   become synthetic local hand moves.

## Validation

Nine deterministic shared-frame playback assertions cover actual intermediate positions/sizes,
repeated packets, idle, loss/hold, retarget continuity and identity reset. Replacing interpolation
with immediate endpoint application makes the negative-control run fail. Rotation uses Unity's
native Slerp and still needs runtime visual verification. Existing wire headers/records are
unchanged by these shared-window repairs.

No hardware test has yet established these paths' appearance or timing. The source review did
not find another actionable owner/viewer setting mismatch in the reviewed map/gaze conversions;
that is a scoped finding, not proof that every possible picture is identical.
