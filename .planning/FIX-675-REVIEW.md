# Build675: hover feedback, tile admission and per-bar foreground depth

Integration base: `80a62893872cc8194a2e688928aa20d308dc66c7` on `dev`.
The development version remains1.1.1; this does not publish another release.

## Hardware evidence

The new October15 local `LogOutput.log` and `Player.log` both identify
ModBuild674. Copies and SHA256 receipts are retained under
`.planning/debug/fix675/hardware/`. The remote vanilla log remains the older
October11 input and is not a paired674 session. No new screenshot or video
accompanies these three reports.

The maintainer confirms the water repair. The empty NPC merchant has not
recurred in the latest test; this is qualified hardware feedback, rather than
proof that every possible long-session lifecycle has been exercised.

## Healthbar failure missed by Build672

Native `EYE-TARGET DIAG` lines178–181 and3790–3793 report two 3560x3560
SteamVR/OpenXR MultiPass targets, each with normalized viewport `(0,0,1,1)`.
The old `HealthbarForegroundDepth.Viewport` returned that normalized rectangle
directly to pixel-domain command-buffer viewport and shader mapping. Therefore
the capture, camera restoration and every bar's depth lookup could collapse
into a one-pixel rectangle. A foreground card covering that shared sample could
affect all actors, explaining the reported collective disappearance/flicker.

Lines7032–7041 establish that all ten adopted actor bars entered the optional
through-wall mode in this run. There is no corresponding foreground-capture
exception. The issue is the valid provider rectangle's unit conversion, rather
than evidence of a failed capture or a changed saved/default preference.

Build672's real GL/D3D tests exercised pixel rectangles and render textures,
but did not supply the native provider's normalized rectangle to this boundary.
Their passing results did not cover the behavior now observed on hardware.
The repair converts native XR parameters to actual target pixels before both
capture and camera restoration, retaining the existing shaders and original
visible geometry. New native raster tests distinguish spatially separated bars
across moving-card frames and exercise this production conversion.

## Card and action haptics

Source review finds independent fingertip, grabber and laser hover producers;
`ItemsPile` even documents intentional duplicate card feedback. That older
interpretation is superseded by the maintainer's current one-pulse-per-card
instruction. `RayUguiDriver` also uses the raw child target identity, so merely
crossing tooltip/icon children can pulse without entering a different action.
The repair shares physical card identity across fingertip, grabber, poke and
laser producers, and native action/default-button identity across UI children.
Every producer refreshes its existing encounter before early returns; 150ms of
absence debounces brief election/raycast losses. The same rule applies to card
and action targets, so two simultaneous different targets cannot keep resetting
a global action winner. Distinct new cards in one frame retain both receipts;
a legacy unscoped callback can stand in for the first matching-frame receipt.

Native standard-action plates retain their own identity, including original
`FullCardEventPusher.Default` flags outside a button's subtree. Geometric half
feedback never refreshes the normal half underneath a standard-action plate.
Delayed contact arming shares its hover encounter. Existing click/grab/confirm
presets and direct fingertip board feedback/the saved toggle remain unchanged;
passive laser tile hover is silent. English/German help describes that scope.

Independent review reproduced two intermediate defects before handoff: a global
same-frame guard consumed an unheard second card, and a proposed global action
winner emitted20 receipts for two continuously hovered targets in ten frames.
Both were corrected and receive native regression controls. Final native Unity
input proof passes32 assertions/all8 controls. Hardware clock/controller delivery
and actual visual/gameplay callbacks remain explicit boundaries.

## Prop tooltips

Prop `HoverRegisterer` picking uses a collider independently from the actual
hex ray. An elevated or overhanging obstacle/trap can therefore admit a tooltip
outside its own tile. Native `Resources/Hex` has a disabled rectangular box and
an active convex mesh collider, so changing gameplay collider geometry is not
the repair. The fix admits hover against the exact native hex-mask ray and
current prop/spawner membership while retaining original enter/exit callbacks.
It uses immutable native GUIDs across rules refresh; initial scenery without a
rules reference uses the game's unique `InstanceName` containing that GUID. It
never matches a generic shared prefab name. The active gameplay selection mask,
native collider shapes and direct fingertip reach stay unchanged. The extra
passive-only ray and its cost are documented beside the hover implementation.

Native Unity proof passes289 assertions/all5 controls using the recovered original
Hex mesh and BearTrap collider over three scales/three yaws. An oblique ray17mm
beyond the actual hex reproduces the old early prop hit. Neighbor/outer misses,
edge behavior, different active masks, rules refresh and spawners are covered.

## Validation and limits

The integrated Release build passes with0 warnings/errors; original direct wire
vectors pass300518 assertions using the real game Unity assembly. Docs/bank checks
pass. Baseline compiled1257 ->1273:16 additions, no removals and30 changed types,
all reviewed. Four additions are hover/tile helpers;12 belong to the concurrent
Steam Link diagnostic. Changes comprise17 repair/shared-localization types,
five Steam Link lifecycle/options/default types and eight inlined675 consumers.
Generated project/resource differences are only the four embedded original Steam
Link fixture inputs and their project declarations. Version3/next-free118 remain.

Surfaces666/236/4795 ->667/236/4797: one default-off Steam Link key and two PNG
diagnostic tokens added, no removals. The healthbar shader bundle and healthbar
defaults remain unchanged. Native GL:223/all12 controls and actual final-bank
D3D11:223/new control pass, plus five provider bindings/both disconnection controls.

Ten of11 existing focused integration suites passed on the combined tree:
card bindings18206, pick tray245/eight bindings, card-scene lifetime, burn
layout228, item burn225, map icon picking, catalog23974/all51 controls,
interaction7915/all73, options-close1801 and player help2308/all8. The older
enchantress fixture initially failed compilation because its hand boundary lacked
the new semantic API. An explicit silent pointer-haptic boundary fixes that
dependency while preserving all original assertions, callbacks, offer-feedback
counters and78 controls:1515 assertions/all78 pass. The separate32/all8 native
suite proves semantic feedback; the handoff fixture does not claim that coverage.

Fifteen source suites passed initially; the remaining instrument-write checker
incorrectly seeded `ProbeTexture`'s constructor by its type name. Exclude type
constructors from verb-based seeds while retaining caller-based promotion.35
parser/analyser assertions and11 old-seed controls retain real diagnostic escape
detection; the repaired checker passes56 load-bearing pairs against the unchanged
61-pair baseline. This is composed coverage of all16 source suites, not a second
complete run after a tooling-only repair.

Two local native suites are registered alongside the existing depth suite.
Together with the separately reviewed Steam Link registrations, inventory is221
local/100 hosted/16 source. This is focused/composed local validation, not a new
complete221-suite gate. Final actual Debug delivery and hosted CI receipts are
retained in ignored evidence after the final commit/push.

The concurrent Steam Link source experiment is separately reviewed at
`.planning/debug/steamlink-mask675/review/FINAL-APPROVAL.json`:38 committed source
blobs,607 policy controls,113 real binder checks and native-reference typing.
Its own limits are retained; this repair does not establish streamed passthrough
or make a Frame-side change. Trailer authors retain their separate changes.

Worker evidence is hash-archived after reaping processes; per-run caches and
verified raw duplicates are removed while retaining sources, results, captures,
failed attempts, original inputs/pins and current deliverables. Main proof and
source/compiled/surface receipts are under `.planning/debug/fix675/`.

Real hardware is required to confirm the final feel of haptics, headset bar
appearance and laser edge behavior. Native raster and production input tests
establish their tested conditions, not a headset outcome.
