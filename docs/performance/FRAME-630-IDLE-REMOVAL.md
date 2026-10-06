# Frame630: remove visible idle pose sampling

The maintainer rejected the sampled idle animation after the Build628 Steam Frame
run on 2026-10-06: visible motion looked choppy and provided no perceptible gain.
This explicit request withdraws the earlier visible-animation compromise and
supersedes CLAUDE.md's general requirement to retain old settings as inert entries.

The complete sampled presentation owner is removed: `ScenarioVisibleIdleSnapshot`,
`BakeMesh` scheduling and retained poses, private renderers, per-camera source masks,
native LOD table swaps, late camera ownership checks, clone-mask restoration,
disabled-cloth shape approximation, and sampling diagnostics. The independent
figure-detail budget keeps its actual native LOD and omitted-renderer ownership;
only the reader/scope used by idle substitutes is deleted. Original actor-bar
conservative envelopes, cycle preparation, native controllers and visible clocks
remain in place.

The following surfaces are deliberately retired, with no inert configuration binds:

- `[Optimize] VisibleIdleAnimationIntervalSeconds`
- `[Optimize] VisibleIdleDisabledClothApproximation`
- `ScenarioIdleAnimationClonePatch` on `FigureVisualMirror.CloneVisual`

Both defaults, graphics-preset writes, curated options, catalog entries and English/
German names and help are removed. A previously saved file may retain its old
unbound text; it cannot enable animation sampling. PC and Frame run the same
implementation. Their independent native offscreen-culling defaults remain.

`OffscreenIdleAnimation` remains independently adjustable. It admits verified,
event-free native scenario idle loops originally authored as `AlwaysAnimate` and
uses Unity's `CullUpdateTransforms`. Visible original skins stay native on every
frame; native state clocks continue when invisible. Actions, movement, local/remote
or prop holds, native active cloth, unknown scripts, events, transitions and foreign
culling ownership retain original evaluation. The synchronous real publisher Play
and locomotion prefixes, failure fallback, original-mode restoration and shutdown
remain. No camera callbacks, private rendering or manual animation stepping belong
to this retained owner.

The source census changes 670 to 668 config keys and 217 to 216 literal Harmony
registrations. All 4,790 protected log tokens remain. Removed measurement rows are
`Figure.VisibleIdleBakes`, `Figure.VisibleIdleSampling`, `Figure.VisibleIdleSources`,
`Figure.VisibleIdleLodRefused`, `Figure.VisibleIdlePhysicsRefused` and
`Figure.VisibleIdleClothApproximation`; `Figure.VisibleIdleBake` scopes and the
bounded `Visible idle retains native LOD for` diagnostic are removed with their
owner. Offscreen native rows and useful bounded failure messages remain.

Focused validation is recorded in the worker's `.planning/debug/frame630-idle-*`
receipts. The original audit's sampled-pose controls are retired with the deleted
mechanism. Its replacement drives the exact publisher Drake body/controller in
Unity2021, compares actual same-pose visible pixels to original rendering, checks
successive native clocks/bones and unchanged original LOD/mesh/material/mask state,
then verifies independent native offscreen culling, held/action/cloth/event/fault
fallback. Native figure detail, actor-bar envelopes, graphics profiles and UI
inventories have separate focused suites. The integrator regenerates the patch
inventory and runs the complete final gate. These source/native fixtures do not
establish new headset FPS, Frame stereo quality or multiplayer latency.
