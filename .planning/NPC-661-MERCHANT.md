# Build 661 purchased merchant item review

Worker source starts at dev `12df1353f` (Build 660). The frozen hardware inputs
are `.planning/debug/npc661/inputs` in the main checkout. Host and remote each
identify ModBuild 660 and commit `12df1353f` built 2026-10-09 21:01:35 UTC. The
maintainer reports a gray purchased merchant front, while the enchantress is
not gray. Both supplied new videos show the enchantress; neither is treated as
recorded evidence of this merchant defect. Logs establish source lifecycle and
build identity, not the actual pixel contents of a card.

## Actual purchase ownership and the missed source state

`TownServiceMerchantHandoff` prepares the separate inventory ItemChip before
committing a purchase. The stock token's physical card and the prepared
purchased ItemChip are different original sources. An actual owned-item count
increase commits the outcome. The card-flight coordinator then invokes the
inventory fan's `BeginMerchantPurchase`; it adopts the authoritative CItem into
the same prepared native ItemCardUI and starts the original release glide.
Currency, inventory success, native callbacks and purchase author are unchanged.

`ItemsPile.ItemChip.PrepareMerchantPurchase` (6238) deliberately sets the chip
transform scale to **Vector3.zero** while its real original ItemCardUI/artwork is
being warmed. The FaceCanvas lies above the published face root, outside that
original subtree. `BeginMerchantPurchase` restores the physical width and
`PrepareInspectionReturn` restores/canonicalizes the actual canvas and root.
The original source is neither absent nor replaced by a generic colored front.

The prior proofs missed this actual state:

| Scope | Useful existing evidence | Missing boundary |
| --- | --- | --- |
| 629 | Native stock/inventory outcome and controller ordering | Correct outcomes do not establish observer geometry/content. |
| 646 | Original curve playback and approved destination hand | Rigid print roots and artificial hand registration missed later source/canvas provenance. |
| 655 | Actual active ItemChip sampler/update and transport jitter | Preparation cleared only release glide; the fixture did not execute the actual zero-scale purchase preparation. Its face was a generic fixture. |
| 658 | Cohort child geometry, large groups and native ability/action partitions | Exact native ability/action partitions are not the purchased ItemCardUI hierarchy or its external prepared FaceCanvas. |
| 660 | Actual terminal/ordinary source lifecycle, mutable child/rect geometry and source identity ACK | Its merchant preparation retained nonzero physical scale and did not bind the original purchased item front. Thus it could not expose this collapsed enclosing-canvas recipe. |

These scopes retain their stated coverage. Treating them as full purchased-item
render evidence was the false assumption. Existing offered enhancement overlays
and their confirmed stable depth/material behavior are untouched.

## Causal canvas failure and bounded repair

On the old source, live return cohorts suppressed their matching ordinary Kind1
roots. Record113 carried one physical curve and each original's child TRS,
visibility and alpha, but omitted the enclosing canvas recipe. Its observer
therefore retained the zero-scale canvas from actual purchase preparation. The
body moved while the printed front stayed collapsed. Restoring only the parent
scale validation order was insufficient: the new recipe still never arrived.

`5e181b44a` freezes the exact complete Kind1 native root beside every return part,
including copied canvas pose, rect, settings and flags. Sender staging includes
this root, the original child record and any layout dependency in one indivisible
subset. Existing Kind1 and113 grammar, 864-byte events and 15 Hz cadence remain.

The receiver accepts the adjacent root only for the exact per-index subset
sequence, physical structure, visibility and alpha. It waits for every original,
child and root before activating the complete cohort. The exact canvas/root and
return clocks become visible together. The root's provenance flag is in-memory
only; it does not change Kind1 wire interpretation. Root metadata is allowed to
compose with its exact live return even when a later artwork sample arrived.

An older subset arriving after a newer subset must be matched against its own
accepted sequence. A global assembly maximum wrongly rejected that root and
prevented the complete first source instant from activating. The reorder control
checks the exact frame at which the final missing packet arrives; allowing a
later snapshot to rescue the cohort would conceal this failure.

Snapshot acknowledgement retains the already existing exact SourceEntry
identity check for native clocks, and adds the same check for each root recipe.
Completion cannot clear Dirty or advance SentAt on a newer root captured during
multipart delivery. Frozen pose/settings arrays are copied; later source changes
do not mutate the retained instant.

Root-owned Motion hooks restore the real CanvasFrame before validating the
resulting parent scale. They stage roots and preserve the exact live clock/root
composer pairing. The merchant worktree uses those integration hooks privately
for proof; Motion.cs is not committed by this worker.

`40c626d79` additionally preflights every compression probe with the exact
21-byte header plus `EntryBytes` sum and the unchanged 128-entry bound. Kind1
companions made the 64-member initial trial exceed 8192 bytes; the codec correctly
threw before budget shrinking could run. Oversize trials now shrink normally.
No limits increase, exceptions are not swallowed, and invalid fitting entries
still reach the strict codec. Unchanged 17/40/64 vectors retain finite progress.

## Original material and visible-content proof boundary

The new runner exports the actual 24 serialized renderer nodes and 8 material
descriptors of ItemCard from `misc_gui_assets_all.bundle`, prefab pathID
1064783067020900303. The original Leather_Armor sprite is read from its real
bundle, pathID -5575648043477861759. Its GUI_ItemCard_Effect material retains the
recovered native GUI/AbilityCard_Shd properties and real BlackCard, animation
noise and flow textures. Source/observer Sprite identity, texture/material
properties and fresh `_GreyOut=0` are checked every visible render frame.

The first native material fixture still rendered an opaque white whole-card
quad: the inherited reader omitted the custom material of the active
`UIFX_Overlay`. Matching Sprite identity and matching blank screenshots did
**not** prove visible armor. This was found by inspecting the output pictures.
The fixture now reads the actual ItemCardEffects imgComp/txtComp/fgFx component
references from the original prefab and executes the unchanged native
Initialize/RestoreCard methods. Native RestoreCard sets fgFx `_FXAnim=0`.

The game shaders' compiled fragments are not executed in this editor fixture.
Fresh item fragments use Unity UI/Default with the native item property table;
zero foreground FX has an explicit transparent fragment port. Other animated FX
fragments, game-created dynamic item model/action layout, particle execution and
full gameplay/controller initialization remain declared ports. Original TMP
layout/style uses the editor font atlas. These limits are recorded in exported
provenance; there is no claim of exact native GPU effects or complete hardware
item contents.

Actual brown/gray armor-region samples are now required on both source and
observer every frame; a uniformly white/gray surface is insufficient. Whole
untouched source/observer pictures retain their existing 10% changed-pixel
threshold. Every visible frame additionally checks the physical front/body
relative position/rotation/scale, including first receipt and terminal/ordinary
frames. Neither picture nor observer transform is normalized or manually fixed.
The real `BeginMerchantPurchase` is tested at both .14m and .16m, after .14m
preview/preparation. This exercises configured purchase width on the actual
source instead of inventing an external Canvas-scale change.

## Native end-transition issue exposed by visible artwork

The real ItemChip active Update checks `_releaseGlide>0`, subtracts capped udt,
then paints one last exponential pose even if subtraction crosses below zero.
Only the next ordinary Update assigns exact home position/rotation/scale.
The rolling native sampler captures age0, remaining glide duration, original
current pose and home target. It is published on the15 Hz numeric cadence.

The prior curve1 renderer forced ease1 when sampled remaining duration elapsed.
With visible original armor the strict picture proof fails at frame 32:
116 / 646 different pixels. The source still paints its final exponential pose,
while the observer has prematurely snapped. Removing that invented snap fixes
frame 32, but the next source ordinary settle at 33 is not sampled until 36. The
unclamped observer continues exponential motion and still differs94 / 643 pixels
at 33. Relative front/body geometry and original content remain coherent; this is
an independent whole-card phase boundary. The strict proof is still red until
that actual terminal boundary is transported coherently. Its threshold is not
weakened and neither this candidate nor a complete hardware fix is claimed.

## Evidence and current validation

- `npc661-merchant-start/run-nc90pa2v`: actual zero preparation first-front-scale0
  reproduction. Initial source extraction/dependency compile failures remain
  preserved separately.
- `npc661-merchant-canvas/run-iik27trm`: parent-guard reorder alone and its control
  both remain first-scale0; it is defensive, not the causal repair.
- `npc661-merchant-atomic/run-pi5ticjf`: first atomic canvas candidate 828 checks
  and old-canvas causal failure. This earlier render uses declared fixture ports.
- `npc661-merchant-multipart*`: failed transport-delay fixture setup preserved;
  delaying both first packets incorrectly changed the expected source instant.
  Corrected packet schedule delays first 14-part packet to 12, delivers its
  12-part partner at 6, and checks activation of that exact instant at 12.
- `npc661-merchant-preflight/run-cwtiwdz1`: canvas/budget/identity geometry 3063
  checks plus 3 causal controls pass, Debug 0 warnings/0 errors, source group 16/16.
  Its initial white-effect renderer limitation is superseded by the content
  checks above; it is not a visible-armor pass.
- `npc661-merchant-visible-armor/run-g2an_pu5`: actual visible armor, original
  fresh effects and old clamp fail at 32; the first effects-adapter nullable
  compilation failure is retained in `run-_q_hlpoh`.
- `npc661-merchant-native-exp/run-kpxz72ch`: unclamped native exponential passes
  crossing32 but fails strict ordinary-settle picture at 33.
- `npc661-merchant-native-width/run-4u98t5vn`: actual .16 purchase from .14 preview
  passes geometry/material/armor through 32; same terminal phase failure at 33.
- Unchanged portable `town-card-return-cohort658`: 261 assertions, including
  bounded 17/40/64-member progress, after expanded-payload preflight.

Runner command: `python3 scripts/npc661-merchant-runtime/run.py`. Suggested
inventory id: `npc661-merchant-runtime`. Every run retains exact source/adapter,
fixture and harness hashes, prefab/art provenance, packet/geometry traces and
source/observer screenshots. No new complete local gate is claimed by this
worker, and no new headset outcome is verified.
