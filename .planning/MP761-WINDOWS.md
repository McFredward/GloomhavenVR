# MP761 window and control review

Scope: maintainer's paired ModBuild759 multiplayer report, points1,5,6,10.
The source baseline is devf93ba368c (runtime760). This lane does not own card
flights, MR/material coverage, native figure audio or the wire implementation.
The final integrated multiplayer delivery is classC; the primary agent owns its
complete gate. Focused checks below are not a complete-gate claim.

## ESC multiplayer chooser

Matched hardware banners: both pinned LogOutput logs report ModBuild759,
version1.2.4-dev+f996308. Main inputs are preserved under
`.planning/validation/build761/hardware759` in the primary checkout.
Remote lines5755–5766 show the exact disagreement: closing the native multiplayer
submenu runs its native hide; the native chooser is listed among the windows the
Options key hid. The intervening sticky sweep explicitly leaves the chooser
alone because `MutiplayerPlayerPicker` was missing from the ESC-child family.
Thus the game cleaned up its assignment widgets while their old force-visible
VR float remained. This was not a native OnHidden failure.

The family now includes exactly `MutiplayerPlayerPicker`, the popup serialized
on `UIMultiplayerEscSubmenu.selectPlayerPopup`. Native OnHidden calls its Hide,
which hides the chooser and clears selection. The existing complete modal closer
then releases its already-native-hidden sticky float without replaying gameplay.
`MutiplayerHeroAssignPanel`, mandatory join assignment, stories and quests remain
outside the ESC family. The chooser is not added to global sticky player menus.

Focused proof: `scripts/check-esc-roster-close.py`, run-zp26b65z:
11 assertions execute the production family predicates, sticky sweep, complete
modal closer and FindPanel. The old missing-family mutation fails at the
already-native-hidden chooser release. The native game-controller/bootstrap and
other-flow eligibility are declared ports; this is not a native Photon session
or a headset picture. An initial fixture compilation failure (public enum XML
comment warning) is retained under run-jhxe8a2k and was fixed only in the fixture.
Both completed runs have canonical cleanup receipts.

## Forced-rest cap glow

The previous cue built four rectangular WorldFrame bars for every style. Hardware
logs enumerate those Top/Bottom/Left/Right renderers even for round rest buttons.
The shared cue now borrows each actual moving CapMesh (round or square and every
board construction) or its actual native SpriteRenderer. It adds quiet constant
warm light, following press motion and original visibility, without replacing a
material, mesh, collider, label or callback. A native sprite uses its original
alpha, including transparent texels whose RGB is nonzero. Original sprite sorting
is retained so the light cannot be painted behind an opaque native face. Depth
remains LEqual with no depth writes; normal MR additive-alpha rules remain owned
by the separate material lane. The same class/API still serves owner and observer.

Actual Unity proof extends the existing multiplayer-progression-runtime suite.
Production run-pgehbg9w passes222 assertions, including all three production cap
constructions, round/square and owner/observer builders, native sprite alpha and
fade, press travel, original input/mesh/material preservation, disappearance and
nearer opaque occlusion. Final shape/occluded/sprite images were inspected.
The legacy rectangle and missing sprite-alpha controls fail at their specific
final-pixel checks. The sprite-sorting and unnecessary-rest policy controls were
also rejected in run-4qld_gq3; their failure mechanism is unaffected by the later
white-RGB transparent sprite witness. Initial fixture compilation failures and
an initially weak alpha witness/control were retained, corrected and rerun only
for affected cases. These are real Unity OpenGL pixels with original generated
cap geometry, not a headset or live multiplayer acceptance.

## Initiative portrait edges

Original sharedassets6 player1632/Rect6107 and enemy1105/Rect5710 use a130px-tall
entry layout root. The native avatar button is on ActorContent; its raycastable
Header is52px tall, anchored11px above the content top, extending37px beyond the
entry. The old depth picker tested only the undersized entry root. Hardware main
line58608 already admits the Header through its committed outer hit rect
(y-94..106); therefore widening the shared laser surface was not the fix.

Only the initiative picker changes. It tests original active raycastable Graphics
whose native click-handler identity is the avatar button, at each actual graphic
plane, retaining culling, native interactability, Graphic.Raycast filters and
nearest-depth selection. Shared UguiPointer/RayUguiDriver code is untouched.

`initiative-row-runtime/run-2xn3tz58` passes96 edge assertions (native-exported
Header geometry at scales.02,.2,1,20; real Unity callbacks and pixels; native
CanvasGroup/RectMask2D, hidden/disabled/non-raycast/foreign-handler rejection,
backside and nearest-overlap cases),172 existing cold-conversion/row assertions,
and the old-root failing control. Header source hashes and serialized geometry
are exported from original assets read-only. Header raster colour/actor bootstrap
are explicit fixture ports, not a claim that the entire native initiative UI or
Windows headset picture was replayed. Earlier fixture-only compile/occluder
failures remain preserved. The actual original native LeanTween path continues to
execute in the inherited cold-conversion case.

## Results

Native ownership/presentation integration is being completed. Final evidence follows.
