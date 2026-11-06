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

## Results native ownership and presentation lease

ScenarioResultsNative reads UIResultsManager.myWindow and its original Header,
Results, Stats, Buttons, GUIAnimator output subjects and Player/NotSet ready root.
The native Singleton getters are plain existing-instance reads; nothing is
instantiated by polling. SharedWindows adds Results7 without renumbering1..6.
The primary agent owns the independent Results125 stream and shared pose contexts.

The original ready toggle remains game-owned. VR seats it inside the fitted
Results frame, using an independent sibling dock and exact native-home receipt.
It yields to existing map/loadout ownership and keeps native reveal/interactivity,
host exit authority and the game's client auto-ready. The common transfer seam
restores its real home before another composite records it. Native OnHidden
restores synchronously; owner deactivation only silences the independent dock,
then the ordinary tick restores home. Native owner destruction cannot destroy its
pooled child. A preliminary child-based OnDisable restoration hit Unity's
prohibition against reparenting while a parent deactivates; that failed control
is retained and the final dock avoids that unsupported operation.

ScenarioResultsVisualLease hides only original CanvasRenderer/mesh output while
an inert public picture is present. Original controllers, active flags, votes,
callbacks and interactability keep running. The exact client wait-for-host tip
yields too; a different native tip is restored immediately. Losing the common
picture restores the latest native draw state. Native closing fade tails remain
eligible via IsOpen or IsVisible.

`results-window-runtime/run-78g52_he` passes36 actual Unity assertions and four
causal controls (missing native-home handoff, stealing map ownership, failure to
restore native output, suppressing unrelated help). It executes the original
UINewAdventureResultsManager ready-decision method against explicit native
registry/network/acknowledgement ports; real Unity rects, GraphicRaycaster/button
callbacks, final pixels, capture-root membership and owner deactivate/destroy
lifecycle execute. It is not Photon, the entire native campaign-end bootstrap,
Net125 delivery or a headset claim. The read-only original level6 census confirms
112 standard Images,94 TMP texts and standard mask/shadow render components; no
custom Graphic adapter is required. Five unrelated transfer fixtures gained only
an inert no-Results boundary; ready-phase-owner68+4controls, map-ready-handoff52+
5controls and hint48+8controls pass with their actual existing transfer consumers.

The additional Net presentation sampler/replayer is now being implemented; its
qualification and full-frame payload measurements will be recorded separately.

## Results public picture (WorldUI + independent Net presentation)

`ScenarioResultsPresentation` samples only the exact existing native Results
window. It uses the approved generic TownServiceBinding/Assets/Material/Neutralize
render-output vocabulary; no NPC interaction/service header is reused. Static
panes and native battle-goal/achievement rows are bounded independent modules.
Their exact native relative paths, parent bindings and original prefab fields
permit a client to reconstruct cold rows before its own controller populates them.
Generated TMP children and exact mod-owned camera/display objects are excluded.
The separately docked original Ready root is represented in the Results root's
coordinate frame, retaining its real position/rotation/scale and authored rect.

A published sample clones the sampler's reused node array. Received pooled
values are immutable: interpolation allocates its own values/numbers, keeps
state/type/permission flags discrete and only blends original numeric drawing
output. It does not run GUIAnimator, Show, Hide, ReadyUp or completion callbacks.
Original native controllers continue independently under a reversible draw-only
lease. Observer modules are neutralized below an inactive quarantine, validated
as an entire candidate and revealed together; a missing template/asset keeps the
previous complete picture. The exact author frame temporarily owns observer fit
with a restoration receipt. Native closing tails remain eligible; native close
retires only the matching author/opening key, retaining a future complete packet
until its actual local native opening appears.

`results-presentation-runtime` imports the original level6 Results/Ready static
hierarchy plus the original externally referenced sharedassets6 battle-goal and
achievement prefabs, actual sprite/font data and native manager field identities.
It constructs four original goal and four original achievement rows. The actual
production sampler, binding/assets/material registry, codec (Unity Mono), clone
neutralizer, atomic playback and final Unity head pixels execute. Bootstrap,
original controller animation clocks, nonstandard native shader programs and
network transport remain explicit ports: the source/mirror comparison uses the
same imported render authoring and is not native Windows shader/HMD proof.

Qualified retained evidence:

- `run-jkknfu8y`: production17 assertions plus the future-opening control.
- `run-kufypt2v`: production16 and missing-original-prefab causal control.
- `run-0h5dzx14`: production15 plus reused-array, omitted-root-pose,
  future-opening-discard, absent interpolation and partial-candidate retirement
  controls. The neutralizer control reached its intended later controller-removal
  assertion; only the expected-failure label was corrected, then it passed alone
  in `run-7gegdic6`. The production assertions were not weakened.
- The independent original and complete observer images differ by zero channels
  above tolerance. The cold-row case also removes the observer's populated rows;
  all original579 nodes/23 modules are reconstructed from the exact native prefabs.
- The complete initial packet is11287 bytes (15 fragments at800 payload bytes),
  retained as `packet.bin`; the actual Mono write/read round-trip passes. Full
  packets at15Hz are unsuitable for the existing event budget, so the primary
  integration is adding independently based sparse updates. This measurement
  does not certify that pending transport work or actual Photon cadence.

Every run, including compile/export/bootstrap failures, has canonical completion
cleanup after process retirement. Original source, metadata, packets and captures
remain; regenerated Unity/compiler caches were removed. No release archive or
hardware success is claimed.
