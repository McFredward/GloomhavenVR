# Frame 634: decorative smoke budget coverage

## Supplied evidence

The immutable capture is
`.planning/debug/frame634-review-20261006T183809Z/inputs/steam_frame/` in the
integration checkout. `LogOutput.log:17,73` and `Player.log:40,155` (raw LF rows) identify
**Build633 / 902fa3a3b**, built 2026-10-06 18:15:18 UTC. The supplied image hashes
are recorded by the capture's manifest:

| Image | SHA-256 |
| --- | --- |
| 20261006202703_1.jpg | `13b7077b424d6da4160cc0efa052725189ded23b91436f53afae627dccdfe967` |
| 20261006202813_1.jpg | `463114d8a1f9b687d26d20bb9add2cf299eed87bbc6d187bd34e0103ecfeed71` |

Both images were inspected. The close view shows dense black puff-like silhouettes
above two torch flames. The overview shows similar silhouettes at additional
torches and around the large skull. They establish the visible defect, not the
responsible shader, blend equation or depth/viewport cause.

Every observed QUALITY-CONTROLS summary reports **environmentFX=0%**, including
Player raw LF rows 9581, 9798 and the later windows. The live eye request changes from
1.00 to 0.80 at `Player:9693`; the diagnostic explicitly retains the 1.00
allocation and requests viewport 0.80. The timing is consistent with the reported
visual change, but this smoke fix does not assign its native rendering cause or
claim to repair a viewport shader defect.

The user explicitly authorizes removal of decorative smoke in low graphics
profiles. This extends the existing configurable environment-effects budget's
coverage; it is not an unconditional visibility change. Profile/default selection
is integrated separately by the main agent. PC and Frame use the same production
implementation, with the existing client graphics configuration.

## Why the old zero budget missed these emitters

The native read-only bundle census supplies exact authoring evidence:

- `pcg_crypt.asset.bundle` contains 49 particle-system records. Both
  `PCG_Crypt_Exit` and `PCG_Crypt_Exit_Thin` contain
  `CR_St_WallTorch_Fire_Orange/p_fire_torch (8)`, with `center`, `fx_sparks (1)`,
  `fx_sparks_drop` and `distort` children. This accounts for ten numbered torch
  emitters in those two exported prefabs, not ten observed live scene emitters.
- Necropolis, cave and stone-room originals likewise author `p_fire_torch (1)`,
  `(8)` and `(9)` inside decorative wall/candelabra prefabs. The demonic tone
  also authors `p_fire_torch_Demon_01` with the same decorative child roles.
- The crypt floor original authors the looping world emitter `P_SewerFog`.
  The same bundle contains genuine `P_BearTrap_*` / `P_Breakable_*` gameplay
  effects, including smoke-cloud materials. They must remain outside the budget.

Private raw evidence is retained under
`.planning/debug/frame634-smoke/native-crypt-particles.json` and
`native-world-particles.json` in the worker evidence. The latter audits seven
additional bundles and 144 particle records with bundle hashes and original
path IDs. This is prefab evidence, not a reconstruction of every running emitter.
The supplied logs do not journal per-emitter ambient admission/masking, so exact
live masked-versus-excluded membership cannot be recovered retrospectively.

Three independent source paths explain missing coverage:

1. `AmbientIdentity` stripped `(Clone)` and `(Instance)` but required exact
   `p_fire_torch` / `p_fire_torch_blue`. Native numbered authoring such as
   `p_fire_torch (8)` therefore rejected the entire torch family and all children.
2. Ambient capture reused the static mesh `TileScope`. Its prop, doorway and
   Animator vetoes are correct for mesh substitutes, but do not establish that
   an exact native decorative fire emitter is a combat effect.
3. Discovery pruned entire ProceduralProp/Animator subtrees before reaching the
   ambient classifier. Repairing the classifier alone could not cover those
   branches. Native material-ready delivery had the same static mesh scope gate.

The old identity walk also returned at a familiar child without inspecting the
remaining ancestor chain. A same-named emitter under a pooled attack container
could therefore inherit decorative status. The new walk validates the complete
path before admitting it.

## Production change

`ScenarioEnvironmentAmbientEffects` owns positive native scenery-emitter
provenance separately from static mesh eligibility. It normalizes only Unity
Clone/Instance and trailing numeric duplicate suffixes, keeps an explicit native
decorative-family list, and vetoes unknown P_/p_ effects plus attack, condition,
projectile, invisibility and healing containers throughout the path. Audited
demonic torch and sewer-fog families join the existing moth, candle, torch and
firefly families. A generic looping system or a material named smoke is never
sufficient evidence.

The scope still requires Generated Content, a procedural map tile and a loaded
procedural scenario. Figure/SkinnedMeshRenderer, Canvas, mod-clone and preview
branches remain excluded. Exact decorative particle descendants below native
props or animated door scenery can qualify; their objects, colliders and
controllers are retained. Static meshes still pass the unchanged stricter
TileScope: no prop, doorway or animated mesh is newly admitted to a substitute.

Discovery descends prop/Animator branches only while the environment-effects
budget is below 100. At 100, its former pruning remains. Material-ready events
can adopt a decorative emitter independently of mesh eligibility, including a
late native particle renderer below a doorway. This adds bounded discovery work,
not an additional whole-scene per-frame scan.

The existing `[Optimize] ScenarioEnvironmentEffectsDensityPercent` remains the
only control. At **0**, all admitted decorative renderer roles are masked during
each camera's actual render lease, including additive, alpha and distortion
variants; there is no shader-name gate. The budget owns only particle renderers,
so native Light properties and water geometry are unchanged. At intermediate
values the existing deterministic path hash selects coverage; at **100** the
budget restores its owned state and leaves the original picture.

Pausing safe decorative emitters removes solver work without clearing particles
or invoking their stop actions. Collision, trigger and non-None stop-action
emitters retain native simulation even when their optional image is masked.
Late native collision/trigger changes release an owned pause. Every pause uses
`withChildren:false`; pooled combat descendants are never paused indirectly.
Initially stopped/paused systems remain stopped/paused. A native stop while
suppressed wins over restoration. Camera completion/recovery releases renderer
masks before native cloning; full quality, disabled VR and teardown restore only
state the budget actually claimed. No native GameObject, controller or Light is
disabled, and no native gameplay/scene completion flag or wire format changes.

This family-level budget also removes decorative flame/spark/distortion image
roles at zero; it is not a smoke-only shader rewrite. Their native lighting stays.
The next headset test must confirm the preferred low-profile appearance.

## Focused evidence and limits

The source-bound existing environment runtime runner executes complete production
environment code and the new classifier against actual Unity 2021.3.5f1
particle modules, renderer masks, camera callbacks, native-style cloning and
pixel fixtures. The final focused run is:
`.planning/debug/environment-budget-runtime/run-85vzjo85/`, with its source hashes,
fixture snapshot, compiled assemblies, per-case build logs, Unity log and results.

It passes **11,428 production assertions** and **11 selected causal controls**:
two existing ambience controls plus nine covering numbered identity, mesh scope,
discovery pruning, late material readiness, pooled-parent veto, native stop
restoration, and collision/trigger/stop callbacks. The native material-healer
readiness binding and its removed-edge check also pass. Other environment
mutation variants are not rerun; this is explicitly partial evidence.

The new fixture checks admission of audited numbered/blue/demonic torch and fog
families, shader-independent masks during actual Camera.Render, post-render mask
release, late doorway delivery, full-budget restoration, foreign pause/stop
preservation, callback simulation, native controller/light/water preservation and
teardown. Native game types and configuration remain explicit boundary surrogates.
The particle test uses a surrogate material to prove absence of a shader-name
gate; it does not execute the game's Windows particle shaders or reproduce the
black puff defect on the headset.

Original failed runs are retained: the first new fixture reused a foreign scene
name already created by an earlier test; unique per-fixture scene names correct
that test bookkeeping. The next run exposed the real remaining subtree-pruning
defect, leading to its production repair. A pooled-parent causal control was
narrowed to the historical early-return defect so it reaches the intended new
assertion rather than failing an earlier condition-child check. No assertion was
removed to make the production case pass.

Strict Release, diagnostic-state and source frame-order checks pass; surface
snapshot retains 668 config keys, 221 patches and 4,791 historical log tokens.
`git diff --check` passes. This worker does not repeat unrelated complete gates or
claim Frame FPS, smoke-free headset pixels, multiplayer performance, or an XR
shader/depth fix. The integrator reviews the combined change and runs affected
final checks before the next hardware handoff.
