# Build647 Frame regression: independent hardware audit

This read-only audit is bound to Build647 `7288d59ab` source and the immutable
latest Steam Frame archive. Both game sinks identify647 and the same run; their
FRAME summaries match exactly. The supplied gaming-PC log remains Build645 and
cannot establish a647 PC outcome. No new Frame screenshots were supplied.

The latest archive is
`.planning/debug/frame648/hardware647-inputs.tar.gz`,5,039,859 bytes, SHA256
`4cb608f2dce49bdd9757c1e733abdecd51bd8ecfa18362ec45e341397f2cf186`.
All three extracted inputs match the primary's immutable manifest. The previous
645 archive SHA256 remains
`d89a3daf9d601d67ee7f071194621e90595ba6dec58dc89f2eb08976ec74b397`.

## Loaded-view comparison

The645 comparison uses the existing seven closed-options FRAME summaries at
LogOutput lines5192,5411,5822,6176,6447,6686,6808:1,068 frames and100.4 printed
seconds. The647 comparison uses lines4620 and4833 after VR Options closed at4465:
126 frames and15.8 printed seconds. Both647 summaries report Game loaded=True,
loading=False, scenarioLoading=False. Six exact same native door GUIDs open in
both sessions, and both discovery inventories inspect5,058 scenery renderers.
Both use2728-by-2728 eyes at resolution scale0.8, viewport1 and MultiPass;
WorldMaterialMode2, terrain near/far0%, geometry cap64 and environment/figure
effects0% remain selected. Different head poses, scene clocks and window lengths
prevent this from being an identical-view controlled benchmark.

| Measured parent |645 weighted ms/frame |647 weighted ms/frame |Difference |
| --- | ---: | ---: | ---: |
| Whole frame |94.107 |123.976 |+29.869 |
| Named mod work |58.846 |85.127 |+26.281 |
| WorldMaterial.PreCull |23.944 |26.469 |+2.525 |
| ScenarioTerrain.PreCull |5.641 |21.248 |+15.607 |
| EnvironmentBudget.PreCull |1.352 |4.550 |+3.198 |
| WallFade.Late |7.819 |12.085 |+4.266 |
| SceneryBudget.Update |6.583 |7.182 |+0.599 |

The user-observed regression is supported: the selected loaded frames take31.74%
longer, approximately8.07 application frames/s instead of10.63. The new terrain
camera work is the largest measured parent increase. These are CPU callback
scopes including both eye calls, not independent GPU busy times. Nested wall
children must not be added to their parent. Reported XRgpu is at the frame
interval and explicitly unusable as GPU busy duration.

This table uses printed STEP averages multiplied by the frames that step ran,
then divides by all frames. The previous645 audit reconstructed costs from
rounded ms/s and seconds; its23.928 World/5.637 terrain/1.351 environment differ
slightly for that reason. FRAME means agree exactly. Printed duration and scope
counts also have boundary rounding; they are not exact timing traces.

## Actual647 coverage and avoidable work

The new environment bank load is reported at LogOutput123, with no mesh/index
failure reports. Player.log Debug preparation reports594 surfaces for5,058
distinct visits, refused scope153, floor2,150, identity2,161, bank0. This confirms
that broader room admission runs. The hardware log does not record the deployed
whole-bank SHA256 or byte count, so its exact packaged artifact hash is unverified.

The closed-window maximum is444 floor candidates/frame but84 completed floor
leases, with eight structural leases. The independent structural candidate budget
remains128 across the two eyes. Candidate admission and completed leases count
work; they do not establish actual frustum-visible draw calls. A native fallback
can be correct but expensive when hundreds of candidates repeat validation,
hierarchy, material and property-block reads before refusal.

Environment completed leases consistently report56 room-floor sources and20
groups across both eyes, corresponding to28 sources in10 groups per eye. Their
geometry is22,276 original versus20,972 submitted triangles across both eye
leases:5.85% fewer triangles in this actual grouped subset. Remaining environment
leases are14 sources in four groups across both eyes. Neither number establishes
the scenario's total GPU geometry. Strong offline structural reductions and the
18.1% whole floor-bank reduction do not imply equally strong runtime savings.

Architectural ornament admission is zero: the real scenery report at1843 retains
the previous238 masks,63 grass and175 scatter/details, out of5,058 inspected
sources. The new closed ornament catalog therefore adds no removal in this run.
This is evidence about runtime coverage, not a claim that all catalog candidates
should pass safety gates.

Two source-bound paths deserve measurement and repair:

- `ScenarioEnvironmentMeshBank.TryGetDetail` may fall back to an exact private
  clone. Terrain's `Surface.WantsSubstitute` treats a different mesh object as
  replacement even when its geometry is exact. A no-savings source can therefore
  pay proxy validation/draw setup while the native renderer already has the owned
  World material. An equivalent native path needs to retain current shaders,
  effects, fading and source identity; this audit does not implement that path.
- World renderer ownership/revocation callbacks call terrain's
  `FloorGroupOwns` and settled-floor role reader. Outside a floor read scope,
  these can independently repeat bank metadata reads. Sharing one synchronous
  read scope is compatible with fresh later-eye/native-write validation;
  a persistent mutable verdict would require a different proof.

## Extra camera and wall ownership defects

With VR Options open again, summaries5548/5705/5929 contain approximately four
World and Environment pre-cull calls/frame but two Terrain calls/frame. World
camera-excluded candidates reach880/frame, in addition to880 ordinary candidates;
its existing guard avoids full material admission but still traverses every
owner and reads current layers/revocation. World parent duration is26–29ms,
similar to26.47ms closed, so four callbacks do not mean twice the full material
cost.

Environment has no equivalent early layer exclusion in
`HandlePreCull` at1486: it always retires material changes and validates batches,
then counts their masks in `HandlePostRender`. This produces112 room-floor
source leases/40 groups on panel capture cameras that exclude world layers.
Environment pre-cull grows to approximately9.8–10.1ms/frame, versus4.55ms closed.
These extra leases are not extra rendered floor triangles. Such cameras must
retain native command-buffer consumers and nested-camera restitution while
avoiding irrelevant world validation.

There is also a source-proven false wall ownership path.647 logs a new split
wall run named `GloomhavenVR.Core`,14 then15 pieces; its voting geometry is
explicitly `GloomhavenVR.TerrainProxy` at4551/4732/5068 and later. No such run
occurs in645. Its shared run votes across room3 and later room0 and produces
seven visible fade edges. It is additional to39 native independently deciding
walls.

`WallSegmentFade.AdoptShaderMatchedWalls` at6806 walks `_factWallFade` without
rejecting the existing `RendererFact.Mod`/`IsModObject` classification. The index
at5430 is built from Mesh+WallFadeShader alone. The comments at5512 explicitly
describe this mod-blind consumer, and the signature exception at5536 retains
mod-owned wall-shader rows because that consumer can adopt them. Broader owned
WorldWallFade materials now expose private terrain proxies to this path.
Rejecting private presentation from native wall admission must preserve current
native wall decisions copied to proxies. Signature/index behavior must remain
consistent with any admission repair. The entire4.266ms wall parent increase
cannot be attributed to these15 pieces without a dedicated measurement.

## Setting-test boundaries

The primary loaded comparison ends before the near-detail slider sweep. At5547
near detail changes from0 to10, then travels through100 and back; at5800 distant
detail sweeps through100 and returns0; at5928 decoration begins changing and
returns to0 after the sweep. These are the explicit graphics-setting MARKs in
this capture; no individual floor/grouping/source-limit comparison is recorded.
Most intervals contain0–8 frames and do not produce complete FRAME summaries.
End-of-window quality snapshots are explicitly not latched A/B boundaries and
can display the next value immediately after a MARK. The141.95/138.26/140.25ms
open-options summaries additionally have different head poses. They cannot
prove individual slider performance effects or be pooled into the primary
closed-options regression comparison.

Earlier loaded647 summaries2185/2600/3424 average57.77/68.42/56.13ms while only
the first set of rooms is active and preparation continues. The door batch opens
later at3939–3944. Their lower cost does not establish all-open improvement.
Startup/loading hitches and the467.78ms transition window4030 are excluded.

Reproduction files are in the audit worktree's
`.planning/debug/frame648-audit/`: `extract.py`, `windows.json`, `metrics.json`,
`input-verification.json`, plus the unchanged extracted input sinks. The parser
asserts sink FRAME equality and matching door GUID order. No production source,
config defaults, assets or native game references were modified by this audit.
