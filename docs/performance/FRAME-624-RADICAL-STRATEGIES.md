# Steam Frame Build 624: next render and presentation strategies

This review starts from `dev` commit `9cc7e589` in an isolated worker checkout.
It proposes a staged rendering redesign for the three-room scenario and future
four-player headroom. Only the callback-local material-read optimization described
below is implemented by this lane. The larger presentation compromises remain
proposals; no estimated gain is presented as measured headset performance.

## Evidence that changes the priority

The current main-checkout `steam_frame/LogOutput.log` and `Player.log` both identify
ModBuild 624, Unity 2021.3.5f1, D3D11/Turnip Adreno 750, SteamVR/OpenXR 2.17.10 and
MultiPass. The folder contains these logs and `openxr-diagnostics.log`, with no
supplied screenshots. Line references below use normalized `splitlines()` numbering.

The latest complete steady revision 3 summary at LogOutput line 7391 reports
103.46 ms mean, 104.39 ms median and 123.89 ms p95. All 193 frames exceed its current
41.67 ms application budget. The earlier revision 3 summaries span 61.04–103.61 ms
means, with strongly changing views. These values establish the practical failure;
they do not establish a controlled regression between builds or headset panel FPS.

At line 7395 the 103.46 ms frame comprises 44.39 ms in the logic bracket, 10.06 ms in render
callbacks and 49.01 ms unbracketed engine work/waits. The last span is unknown:
neither GPU saturation nor compositor wait can be assigned from it. The reported
XR GPU value 103.83 ms is explicitly at the application interval and unusable as
GPU busy time. Unity's draw/batch/pass/triangle counters return only zero samples
and are explicitly rejected at line 3000. Do not turn material slots into draw calls.
The final scene census has not completed; final renderer/shader populations are
unknown. The summary figure values are budget percentages, not actor populations;
native callbacks and detailed figure receipts separately establish live actors.

The same window ranks these measured scopes:

| Scope | Mean per frame the scope ran | Interpretation |
| --- | ---: | --- |
| `WallFade.Late` | 6.008 ms | Steady wall ownership/decision work remains material. |
| `EnvironmentBudget.PreCull` | 3.653 ms, 386 calls/193 frames | Every camera invocation repeats validity reads; two callbacks per application frame. |
| `ActorBars.Late` | 2.285 ms | Original bar anchors, facing and presentation remain CPU work. |
| `Cards.Driver` | 1.695 ms | Preserve input, card fronts and native animation. |
| `CanvasConversion` | 1.623 ms | Whole converted-panel maintenance, distinct from late work. |
| `CanvasConversion.Late` | 1.059 ms | Includes veil/reveal/order responsibilities. |
| `WorldUI.HiddenWindowVeil` | 0.937 ms | The discovery child is 0.676 ms; do not add it again. |
| `CanvasConversion.Fit` | 1.085 ms, 3886 calls | About 20 calls/frame across live panels, including hit-rect work. |

These nested scopes cannot be summed into a predicted saving. In particular,
removing every 3.653 ms of environment pre-cull would still leave an approximately
100 ms application frame in this window. Small exact-work fixes are useful but
cannot be the complete answer.

The most consequential application receipt is Player line 10717: 38 compatible static
surfaces, zero source renderers/zero chunks, 38 unreadable originals, one simpler
material and five identified ambient solvers at 0% effects. This receipt precedes
the final three-room reveal; no final three-room environment receipt is available.
It proves that the enabled floor/structural chunk controls delivered **no chunks
at that observed stage**. It does not prove that their source population remained 38
through the final room. Raising another instancing/batching flag without an actual
chunk/submission receipt is an inadequate strategy.

Initial eye-target diagnostics at LogOutput line 176–180 request 3408×3408 pixels per eye,
MSAA 1 and scale 1.00: 23.2 million pixel samples across two passes. These are initial
readbacks, not a constant-setting claim for every later measurement window.

## What has already been implemented

The existing Frame work has removed unused native camera drawing, reduced optional
grass/vegetation/decoration and generation detail, reduced audited figure geometry
and skinning, suppressed audited secondary figure effects, disabled cloth, capped
event-free offscreen idle transform evaluation, prepared original card/figure
resources during genuine loading, and reduced exact redundant wall/light/UI work.
Original actions, collision, selection, loading continuation and shared native
widgets remain intact. Read the implementation reports 615–621 for their distinct
proof boundaries; a successful source/Unity fixture is not a new Frame measurement.

The current environment system has two materially different mechanisms:

- `ScenarioStructuralInstancing` enables an owned original material flag for
  repeated compatible mesh/material pairs. It retains native renderers and native
  MPBs. It makes no explicit grouped draw submission. Fresh Frame defaults leave
  this experiment off because the previously supplied native materials already
  had their flags enabled.
- `ScenarioEnvironmentBudget` creates private `Mesh.CombineMeshes` substitutes
  for positively proven floor cores and a small audited masonry identity list.
  It admits only readable geometry, excludes original LOD/lightmap/reflection/MPB
  conflicts, limits portions to 24 members/48,000 vertices, and revalidates before
  every camera cull. Source objects keep their materials, mesh identity, controllers
  and colliders. Render-only masks release after cameras and before native changes.

Optional simpler shading currently rejects all live native wall-fade channels,
including nominal floor materials carrying such channels. Build 620 restored the
original continuous bilinear noise/cutoff transition. The earlier square rank-map
substitute failed the maintainer's visual ruling and must stay removed.

## First implemented correction: validate shared material once per camera

`RetireChangedNativeMaterials` checked original shader/property/keyword inputs
again for every applied surface, even when they shared an original material.
The new reusable dictionary stores the material verdict only inside one synchronous
invocation. It clears at entry/exit and teardown. Each renderer's own MPB veto
remains live, and every later camera/eye sees native in-place material edits.
No frame cache, interval, setting, wire record or native continuation changes.

The source-bound real-Unity fixture has 48 surfaces sharing two originals. Its
complete production material method performs two validations per camera; the
causal redundant-read variant performs 48 and is rejected. Separate actual
`Camera.Render` invocations verify a native keyword/property edit, individual
MPB retirement, foreign material replacement and complete restoration. This
models the between-eye mutation seam without claiming an OpenXR eye-picture test.

The complete affected environment suite passes 733 production assertions across
44 production/negative variants; the new scope contributes 107 assertions to
626 existing assertions. The adjacent structural-instancing suite also passes
23 assertions across five production/causal variants. Overall local suite
coverage remains partial; final integration still requires the complete gate.
Strict Release has zero warnings/errors. The Unity Mono byte-allocation counter
does not calibrate after a known 8 KB allocation. The fixture instead verifies
reused dictionary storage, cleared material references and rejects repeated
dictionary creation; it makes no measured zero-byte allocation claim.

## Ranked next experiments

| Priority | Experiment | Potential | Critical unknown or risk |
| --- | --- | --- | --- |
| 1 | Fixed-view lower eye resolution A/B using the existing control | Large if the unexplained cost includes pixel work | CPU logic is already expensive; text readability must be retained. |
| 1 | Private exact readable environment mesh bank, followed by current small chunks | Converts the observed zero-chunk path into a real submission reduction | Exact original provenance/channel matching, memory, room lifecycle and actual coverage. |
| 2 | Dedicated cheap opaque wall/floor material preserving the original fade contract | Broader per-pixel reduction than the 38 observed admitted surfaces | New shader fidelity, both eyes, native MPBs and themed surfaces. |
| 2 | Explicit opaque repeated-mesh submission by small room/cell groups | Reduces repeated native renderer submission without mesh merging | Stereo integration, lights/probes, callbacks and culling costs. |
| 2 | Exact unchanged UI/material/geometry work removal | Reliable CPU saving with unchanged presentation | Avoid delaying pooled content, hidden-window protection or native animation. |
| 3 | User-approved abstract terrain and distant-room detail | Largest reduction in represented environment | New visible compromise and target/picking parity need explicit review. |
| 3 | User-approved tactical 2.5D board | Replaces most room geometry and environment shading with a few surfaces | Elevation, pickup, native animation and CPU driver costs require separate design and measurements. |
| 3 | User-approved visible idle-pose/effect quality reduction | Potential CPU/GPU relief after other budgets saturate | Native continuation and multiplayer animation parity prohibit blanket suspension. |

### Existing resolution control as a discriminator

Use one fixed three-room save and view for scale 1.00, 0.70 and 0.55, with the same
graphics choices and complete post-settling windows. Those proposed scale ratios
reduce ideal eye pixel area to 49% and 30.25% respectively; they predict pixel counts,
not frame-time gains. Verify actual `EYE-TARGET DIAG` after each change. Keep
original card/UI artwork, geometry, font size and input hit areas. Compare the
logic, render and unbracketed spans as well as frame percentiles. A large resolution
response supports pixel/render-wait work; a weak response prioritizes CPU work.

This uses a current player quality choice. It requires no new privacy, gameplay or
multiplayer rule. Dynamic eye-resolution control is a later experiment: hysteresis
must avoid repeated target reallocation, and scene captures/window supersampling
must retain their own correct resources. First measure the existing fixed control.

### Make environment chunks work on unreadable original meshes

Add a bounded mod-owned bank of **exact original environment render geometry**
with source-bundle hash, object/path identity, submesh/index/channel layout and
bounds. Read the original game assets without modifying them. Generate private
readable derivatives during build/preparation, using the already established
original figure-bank tooling pattern, but independently validate environment
normals, tangents, UV/color channels and index winding. Match live sources by
strong provenance and structural metadata; a shared name alone is insufficient.
Never substitute a collider mesh or write the original `MeshFilter.sharedMesh`.

`Mesh.AcquireReadOnlyMeshData` is not a runtime escape from this restriction:
Unity 2021.3 explicitly rejects unreadable meshes. An editor-only API succeeding
does not prove a player solution. See the official
[2021.3 read-only mesh API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Mesh.AcquireReadOnlyMeshData.html).

Let the existing `Batch` retain two identities: its exact **native source mesh**
for lifetime/change validation, and its private readable **combine input**. Keep
the existing tile/cell/material/flags grouping and small portion bounds initially.
Missing/stale bank entries preserve originals and report bounded coverage reasons.
Deduplicate private inputs by source identity, cap memory, prepare only the
scenario's needed meshes while native loading is genuinely active, and release
unused derivatives at scene teardown. Genuine room reveals prepare only new
identities; never stall native gameplay continuation on cosmetic preparation.

This differs from the removed Unity static batching experiment. That experiment
wrote Unity internal batch state onto Apparance's in-scene clone sources; later
clones were born without material slots. The new route must never invoke
`StaticBatchingUtility`/`SetStaticBatchInfo`, mutate source slots or leave source
draw masks across native `Instantiate`. It uses separate owned chunk objects
and the current render-lease recovery. The historical removal ruling is recorded
in [static-batching-removed.md](../../.planning/static-batching-removed.md).

Required causal checks include unreadable originals with exact private inputs,
original clones before/between/nested camera renders, new room pooling, one source
changing mesh/material/MPB/flags, collider identity, interrupted render recovery,
foreign masks and stale derivative provenance. Compare native-versus-substitute
pixels and real camera submission receipts. A bank covering only 38 floors may
still be too small: report the actual eligible/copy-ready/source/chunk populations
before extending the classifier to more structural families.

### Cheap native-fade-compatible environment materials

The current simple shader still performs triplanar sampling for world-projected
materials, spherical-harmonic/direct lighting and fog; its native wall-channel
veto deliberately excludes most animated wall output. Propose a separate opt-in
material family that retains original albedo/tint, exact UV/world projection,
foundation/depth and the actual continuous native map/cutoff fade. Simplify normal,
MRAO/detail and lighting work; optionally use a constant calibrated surface light
for a clearly described flat-shading quality level. Start with audited opaque
masonry families, excluding water, foliage, actors, doors, interactive props and UI.

Do not reuse the cheap floor shader on walls merely by changing their identity.
It has no native wall-fade fragment branch. Carry the original delivered
renderer-specific MPB inputs and map enable bindings into the replacement shader;
unknown properties/routes fall back to native. Preserve both endpoints and the
intermediate bilinear dissolve, the never-fade floor rule, and original attached
torch protection. Per-surface camera leases and foreign material ownership remain
the authority. Never add play-area MR background geometry.

This extension needs a new bundle and source-linked pixel controls plus Windows
shader/headset testing. Existing permission for simpler audited environment
shading does not prove permission for visibly different wall dissolution or
removal. Present the proposed flat shading for approval while preserving the
already approved dissolve; do not silently deploy a new wall look.

### Explicit submission instead of more material flags

After obtaining reliable repeated-mesh groups, test a separate mod-owned opaque
submission path. Reuse the native GPU mesh when exact copying is unnecessary;
an explicit instanced draw does not require a CPU combine mesh. Build matrices
and admitted source membership in bounded reusable groups, keyed by mesh/submesh,
material, layer, tile/cell and render flags. Start with non-fading floors/structural
cores without native MPBs or special command-buffer consumers. Keep the original
render-lease lifecycle and restore originals on any unsupported observation.

Unity 2021.3 `Graphics.DrawMeshInstanced` culls/sorts the whole group, does not
subsequently cull individual instances, and limits each call to 1023 instances.
Small spatial groups and explicit camera binding are therefore necessary; the
API is not proof that a whole-scenario group is faster. See the official
[2021.3 instanced draw API](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Graphics.DrawMeshInstanced.html).

A prototype must establish correct MultiPass camera/event placement without
double-queuing a draw, recover nested/interrupted cameras, retain original probes,
depth and collision, and bypass exact native `DrawRenderer` command-buffer paths.
Only after measured submission benefit should an instanced custom wall shader
carry per-instance fade values. Enabling an already-enabled native instancing flag
is neither this architecture nor its validation.

### CPU and canvas work before changing visible UI

The final log records about 20 live converted-panel maintenance calls per frame.
Source already caches hierarchy inventories and gates many unchanged transform
writes; avoid recommending those same fixes again. Remaining concrete review
targets are:

- `RefreshVeilWindowInventory` enumerates the entire native `UIWindow.GetWindows`
  registry separately for every panel each late frame. Reuse one current-frame
  registry observation or route newly registered windows to their current subtree
  inventory, retaining same-frame component-add/reparent/activation discovery.
  Existing veils still reread live alpha/open state and learn foreign renderer
  writes every frame; removing that protection revives documented one-frame flashes.
- Fit/hit/order readers can share **one synchronous-pass** material/geometry/clip
  ancestry read when their actual inputs and sample boundary agree. Pool movement,
  animation and layout writes invalidate immediately. Do not cache a previous
  frame's visible union or owner layout as if it were the current native picture.
- Bar scale already skips exact unchanged writes. Facing normally changes with
  tiny head motion; additional angular quantization or slower visible billboarding
  is a new presentation compromise, not an exact-work fix. Profile its real
  canvas cost before asking to reduce that visible cadence.
- Diagnostic snapshots/traces remain bounded Debug work. Optimize expensive
  construction behind log gates and shared current observations. Keep build,
  important lifecycle/failure context and significant bounded anomaly reports at
  normal level; reducing the maintainer's log level is not the optimization.

Actual `Canvas.BuildBatch` and `Animator.Update` markers are unavailable in this
player. Canvas/animator cost is a hypothesis needing a usable capture; do not
attribute the 49.01 ms residual to canvas rebuilding just because writes can dirty it.

### Radical room presentation budget

If exact chunks and cheaper shading remain insufficient, propose an explicit
tabletop mode: all revealed playable floors, actors, doors, interactable obstacles,
loot/chests, targeting/highlight information, health/status bars and original cards
remain represented. Replace **only positively classified decorative terrain**
with a small original-textured room/cell shell or remove it under an approved
cosmetic budget. A distant-room variant reduces decorative complexity while
preserving the complete accessible tactical board in every revealed room.

This needs a room-scoped owner with reversible renderer-only leases, original
controller/collider lifetime, transitions instead of popping, and current head
frustum/targeting/hand proximity overrides. Every native action restores any
required presentation before its dispatch. Unknown geometry remains native.
Do not deactivate a whole room, hide enemies in inactive rooms, suppress native
gameplay controllers or change room reveal/load state. Always retain floors,
and never write Light state through visibility ownership.

Removing large decorative assemblies, replacing structural wall appearance,
using flat terrain proxies, or visible distance-dependent pose degradation are
**new visible compromises**. The 2026-10-01 essential-scenery request and current
zero-decoration controls establish permission for existing audited dressing
reduction, not for every geometry deletion. Obtain a concrete approval for the
proposed visual examples before enabling the wider mode. Original window/card/NPC
contents and shared intermediate animation are not covered by that compromise.

### Tactical 2.5D fallback

If minimal 3D still misses the multiplayer budget, a more radical opt-in mode can
represent each revealed room with a small number of original-textured floor
surfaces and low-cost obstacle/door outlines. Lightweight actor pieces retain
identity, position, health, conditions and target highlighting. Hands, held cards
and readable original windows stay in VR. All revealed tactical information remains
public; a distant room never disappears merely because the local visitor looks away.

Define elevation, line-of-sight cues, figure pickup and action transitions before
implementation. A proposed action can restore the original figure presentation
while keeping the rest of the board inexpensive, but the exact intermediate
animation remains shared. Source controllers, native scenario state and colliders
remain authoritative; a renderer-only replacement must preserve targeting and
native action completion. Original 3D view remains a reversible mode choice.

This trades room depth and scenery appearance for a much smaller rendering
representation. It does not by itself remove the 34.72-ms measured logic span:
native controllers and mod presentation loops still run unless a separately
proven path removes their redundant work. Measure submission, actual GPU time
where available, logic and memory independently. A baked room image alone is not
evidence that the CPU bottleneck or multiplayer cost has been solved.

### Animation and effects after the existing zero budgets

Native `ActorBehaviour.DoTransform` includes locomotion/teleport/push-pull and
Animator state updates; native particle liveness can gate continuation. Blanket
`Animator.enabled=false`, `CullCompletely`, actor-Update skipping or pausing every
looping effect is unsafe. The current event-free offscreen idle budget already
retains the native state clock and restores synchronously for actions/holding.

A further option is a private lower-detail presentation rig or a frozen visible
idle mesh with native action/pose clocks still running. It needs exact pose and
bar-anchor ownership, native movement restoration before dispatch, original
held-figure behavior, transition effects, derivative memory bounds and action
fallback. That is a new visible idle-motion trade needing approval. Prefer
expanding exact audited secondary ambience/effect ownership first; distinguish
torch/candle ambience from attack/condition/reveal completion effects. Preserve
shared action FX/timing unless the user approves a specific cosmetic exception.

## Four-player headroom and acceptance

The supplied Frame scenario is not a four-player hardware result. `Net.Avatar`
at 0.296 ms in the cited single-run window does not price three remote original
boards, fans, effects and native captures. The separate NPC integrator owns current
town synchronization work; this lane changes no wire grammar, native controllers,
FFSNet or gameplay authority.

Set a concrete target before default selection: measure a stable application
cadence at an agreed resolution and view, including p95/p99 action hitches, then
reserve headroom for the actual added peers. At 72 Hz the nominal application budget
is 13.89 ms, and a 36 Hz half-rate budget is 27.78 ms. Those are planning budgets, not
observed stable Frame outcomes or promises of compositor quality.

Use the same difficult save with one, two and four players, all three rooms open,
and match peer builds. Record original native windows, card draws/discards/burns,
held figures, action effects, character switching and loading transitions. Keep
control-board/window/card geometry and owner-authored shared state identical;
gain headroom by exact immutable-content reuse, deduplicated current-frame reads,
event-driven resource lifetime and cheaper approved environmental presentation.
Do not reduce remote capture cadence, skip remote animations, hide remote boards
or change private-card rules under an assumed performance exception.

For each experiment preserve a bounded result ledger: source/build, actual eye
targets, settings, view/room/peer membership, applied eligible/substitute/restore
counts, complete steady windows and action spikes. Abandon a mechanism that adds
validation/memory cost without making actual substitutes or measured frame gains.
The user requested worktree isolation and a further question before handing
changes to the external main agent; none of these proposals authorizes that
handoff or integration into the concurrently edited main development checkout.
