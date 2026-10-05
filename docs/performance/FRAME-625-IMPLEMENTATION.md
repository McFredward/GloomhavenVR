# Isolated Steam Frame implementation based on Build625

This is an implementation candidate based on `dev`
`01503d4c2e28ee50b0b3662debb48a9ad19f88f0`, not a released Build625 binary or a
new headset measurement. The supplied October 5 hardware capture remains
Build624 (`b123cefc9` in both log sinks). The maintainer authorized all other
reversible three-dimensional measures, rejected a 2.5D board, and will compare
resolution personally. No eye-resolution default changes.

PC and Steam Frame use the same runtime and asset archive. Frame detection only
seeds fresh BepInEx entries; saved values survive. Every new compromise has an
independent live setting. Exact work sharing also has independent A/B switches.

## Hardware causes and limits

The [Build624 log audit](FRAME-624-LOG-AUDIT.md) retains input hashes, banners,
line references and loading boundaries. Across the post-spinner three-room
windows, 810 application frames take about 60.3 seconds: approximately 74.4 ms
per frame. The final steady window reaches 103.46 ms, approximately 9.7 application
frames per second. Neither number is compositor refresh rate.

The final frame contains 44.39 ms of bracketed logic, 10.06 ms of render callbacks
and 49.01 ms of unbracketed engine work/waits. No reliable GPU busy-time or native
draw-call counter is available. The XR GPU interval is not evidence of GPU
headroom. The diagnostic now says this explicitly, retaining existing grep tokens.
Resolution already had a poor quality/performance trade in the maintainer's test.

An earlier pre-reveal receipt shows 38 eligible surfaces, 38 unreadable original
meshes and zero prepared chunks. It establishes that the enabled chunk mechanism
was ineffective at that observed stage. There is no final three-room coverage
receipt. Grass, decorative vegetation, optional figure effects and cloth were
already strongly reduced; repeating those defaults cannot explain a new gain.
The 11.198-second reveal and 9.301-second native activation hitch are distinct
from the later steady-frame failure. The supplied run is offline and has no
screenshots; it does not measure multiplayer or establish a new shader picture.

## Settings in the common binary

All keys below are in `[Optimize]`. The first seven appear in the normal VR
Graphics scene-detail controls. The two exact-read controls also remain available
in Advanced. Existing graphics profiles set the new values only when explicitly
selected. Installation never overwrites saved settings.

| Key | Fresh PC | Fresh Frame | Actual change / reversal |
| --- | --- | --- | --- |
| `ScenarioEnvironmentMeshBank` | false | true | Verified private readable copies allow exact chunks from unreadable native geometry. Off restores native unreadable surfaces. |
| `ScenarioExplicitEnvironmentInstancing` | false | true | Small cancellable camera-bound repeated-mesh submissions. Off restores individual native draws. |
| `ScenarioCheapWallShading` | false | true | Original wall textures and continuous native dissolve with reduced optional surface/lighting work. Off restores native wall shaders. |
| `ScenarioTerrainDetailPercent` | 100 | 0 | Prepared coarser 3D wall bodies/pillars. 100 restores original geometry. |
| `ScenarioDistantTerrainDetailPercent` | 100 | 0 | Independent additional wall/pillar detail cap beyond the selected distance. |
| `ScenarioTerrainDistanceMeters` | 0.75 | 0.75 | Distance threshold in VR metres, range 0.1–10. |
| `VisibleIdleAnimationIntervalSeconds` | 0 | 0.2 | Holds audited visible event-free idle body poses between bounded samples. 0 restores every native pose; range 0–0.5 seconds. |
| `SharedEnvironmentMaterialReads` | true | true | Shares exact current material verdicts within one camera invocation. Off repeats original per-surface reads. |
| `SharedUiWindowReads` | true | true | Shares scoped local window reads and immediate duplicate nonvirtual mirror getters. Off retains independent original reads. |

The bank, exact chunks, instancing, cheap shading, geometry and visible-idle
choices have separate admission/fallbacks. Cheap shading does not claim floor
ownership. The existing `ScenarioStaticBatching` and `ScenarioStructuralBatching`
keys continue to control private chunk preparation; their names do not authorize
Unity internal static batching. Existing decoration, figure, cloth and offscreen
idle controls remain available alongside these settings.

## Render submission and native room cloning

The historical [batching removal](../../.planning/static-batching-removed.md)
records the actual disappeared-room defect: Unity internal batch state was written
onto Apparance's scene templates, then native clones received empty material
slots. The new route never calls `StaticBatchingUtility` or `SetStaticBatchInfo`,
never writes a native mesh, and never makes proxies children of native templates.
The actual publisher `ApparanceEntity.CreateInstance` calls `Object.Instantiate`;
its prefix synchronously restores camera masks before that call and never gates
native continuation.

Exact chunks have private readable input meshes. Repeated instances use owned
`CommandBuffer` submissions which can be removed synchronously when a later
native callback edits a source. Per-camera source masks restore after rendering,
on interrupted-render recovery, before native content creation, on disable and
teardown. A missing asset, unsupported original, MPB, changed transform/material,
foreign command buffer or preparation fault retains native rendering.

Multi-submesh overlapping instance groups stay native: actual pixel controls
showed an ordering mismatch. Instancing requires disjoint world bounds and an
actual compatible instancing shader variant. A verified native buffer pointer
identifies the owner's own camera/event/lifetime buffer; all other command
buffers keep exact native Renderer consumers available. Terrain and visible-idle
owners share that predicate without assuming every command buffer is theirs.

The independent terrain native-write/placement bridges remain active even if
optional environment preparation has failed. Static camera callbacks also
continue when a host is inactive; every private draw owner must release its
masks and retain originals at that boundary. New real-camera fixtures and
causal mutations cover these seams, with the current execution limits below.

Debug `Environment.ChunkSources/ChunkGroups`, `InstanceSources/InstanceGroups`
and `RenderCameras` count completed camera leases after late revocation, before
restoration. `NativeBufferFallback` records consumer fallback. These are camera
sums (including eyes), not unique scene objects, GPU draw calls or FPS.
Terrain triangle/cheap-surface counters currently record admitted pre-cull leases;
a later native writer may revoke those leases. They measure neither completed GPU
submissions nor final visible coverage. Prefer completed environment/idle lease
counters and actual application timings when assessing the combined hardware run.

## Coarser 3D walls and cheaper materials

Terrain admission requires original scenario/generated-content ownership, a
`ProceduralWall`, unambiguous bank provenance and fifteen audited wall-body or
pillar names. Twelve exist in the source bank. All floors, FloorHex meshes,
foundations/slabs, top caps, anonymous geometry, doors, figures, lights and
interactive obstacles retain originals. Revealed rooms keep their tactical
contents and original collision.

Prepared wall/pillar definitions total 9,116 original triangles, 3,486 at the
50 tier and 1,186 at the strongest tier. These are asset-definition totals,
not this scenario's submitted population or frame-time savings. Identical tiers
stay on the native renderer. Leaning or touching close restores full geometry.
Original vertex indexing allows a continuous 0.35-second 3D morph; a bounded step
prevents a hitch skipping every intermediate shape. There is no planar board,
room deactivation or MR scenery backing.

Cheap wall shading retains native LOW/HIGH map, enable, cutoff, foundation,
simplex/noise and dimming branches, including renderer/material-slot property
blocks. Native shader instructions supplied independent literal noise samples.
Original global uniforms remain global; no material default shadows them.
Normal/MRAO/detail/reflection and optional fine lighting are deliberately reduced.
Unknown shader routes, live vertex/emissive effects or foreign state stay native.
The [terrain report](FRAME-TERRAIN-PRESENTATION.md) separates this source and
surrogate graphics proof from original Windows shader/HMD appearance.

## Visible idle and exact local/multiplayer reads

Visible idle uses the positively audited event-free native scenario loop only.
Native state clocks still advance. Actions, transitions, movement, held figures,
events and unknown rigs immediately retain original evaluation. Private baked
body meshes hold the last evaluated pose between samples; source hierarchy,
material slots, mesh identity and enabled state remain original. Town residents
and their synchronized animation are outside this owner.

At most two actors warm a sample concurrently, and LateUpdate bakes at most two
actors or stops after a two-millisecond slice. A single atomic BakeMesh cannot be
preempted. Other due actors hold their existing pose. The interval is a minimum
hold duration, not guaranteed frequency under load. Larger intervals make idle
motion less smooth. See [visible idle](FRAME-VISIBLE-IDLE-WORK.md).

Scoped local window reads preserve pooled/reparented/activated content and every
native callback. The controlled local UI workload reduces registry member reads
from 2,000 to 100 and CanvasGroup reads from 4,800 to 900. The remote mirror reuses
only the immediate redundant nonvirtual getter before its own assignment. No
value crosses a prior write/callback, frame or peer. Virtual Graphic/TMP/Text
getters keep the original path. A controlled 60-Apply workload reduces tracked
source getters from 1,800 to 1,080, preserving writes, intermediate properties and
callbacks. These are exact subset work counts, not whole-UI/MP CPU or FPS gains.
See [multiplayer read proof](FRAME-MULTIPLAYER-READ-WORK.md).

## Shared package

`prebuilt/ghvr-environment.bundle` is an independent 56,747,136-byte Windows bank,
built with the game's exact Unity 2021.3.5f1. Its 3,172 declared assets contain
3,170 geometry streams, the immutable index and cheap wall shader. SHA-256:
`c04ea35f54a3ca7f9ca79e850325b7bcaeef55d22ef681b125e3c8a55e4d611b`.

The runtime validates exact original metadata, source-bundle provenance and
prepared stream hashes; ambiguous or unknown originals remain native. Streams
load on demand and immutable prepared resources stay process-local across scenes.
This adds memory/preparation cost; it does not promise every scenario is covered.
A missing, incompatible or incomplete package is terminal and reports bounded
ordinary-level context without holding native continuation.

Both archive and installer paths include the bank for PC and Frame. Original
banks do not need rebuilding. The packaged checker verifies all streams against
the committed source/index. Before the interruption, an independent actual Linux
Unity2021 probe loaded all 3,170 committed streams, index and shader. That is
asset availability, not shader support/appearance on Frame.

## Final validation and handover boundary

The final complete local attempt recorded **141/141 scopes: 82 passes and
59 failures**. No tracked production/test/asset input changed during or after that
attempt (10,343 captured files). The complete source group passes **14/14**; the
pinned scheduler inventory passes after registering the four new local suites.

The final guard exits unsuccessfully at that failed local stage, before its
byte-vector execution or compiled snapshot. The wrapper's generic “wire format
changed” line is not evidence of a changed packet. The independently executed
current golden-vector binary passes **308,254 assertions**, six above the clean
Build625 receipt; no protocol source changes. Separate strict Debug and Release
builds pass with **zero errors/warnings**. Cached package identities match the main
checkout; offline/local-source, single-process MSBuild arguments avoid unavailable
feeds and compiler-server sockets without relaxing the compiler warning gate.

All bank/package hashes, four Unity2021.3.5f1 bundle headers, 1,594 figure
derivatives in 66 parts, EN/DE document checks and public surfaces pass. There
are **669 config keys (+9), 214 literal patch registrations and 4,790 log tokens**,
with no removals. The private compiled comparison against clean dev625 contains
**16 changed existing types and six added types**, plus explained build stamps
and decompiler-relative HintPaths; reference identities/resolved files are
unchanged and no entry is removed. Packaging layout/text/Frame setup checks pass.
The local archive is a packaging proof only: it still carries base ModBuild625
and must not be distributed or used for a paired hardware test.

The failure audit identifies socket/display, read-only cache, NuGet-feed/audit
or external-download rejection for 58 failed local scopes. None records a C#
compiler error. Quest-weaver initially emits only an unsuccessful build; its
separate single-processor continuation passes 33 executable assertions. A focused
offline continuation also repairs the merchant-tutorial-text audit dependency
(29 assertions and six controls); perf-census then compiles every variant but
cannot start Unity. The original **82-pass/59-failure** complete attempt remains
unchanged. Partial continuations do not make the complete gate successful.
Exact final/failed reports and failure classification remain in the candidate's
`.planning/debug/`.

Historical focused
Unity receipts belong to their captured source trees: environment 11,231
production assertions with its failure-bridge control; visible idle 423 assertions
and sixteen causal controls before the latest disable/counter changes; terrain
100 assertions and thirty controls before its disable fix; local UI 1,651
production assertions plus 1,650 fallback assertions and eleven controls;
multiplayer immediate reads 625 assertions and eight runtime controls plus one
scanner control. These receipts are not certification of the final combined tree.

After the interruption, the session can read the old worktrees but cannot write
them or the main `.git`. The exact committed history and every dirty root/worker
file were preserved in an isolated local checkout at
`/home/claw/gloomhaven_vr/.planning/debug/steam-frame-worktree`, branch
`work/steam-frame-radical-final-20261005`. It shares read-only source objects and
references; the main checkout and external NPC/Quest branches are untouched.
The failed attempt to register another main-repository worktree changed no ref.

Current permissions also reject X-display socket connections and new display
listeners. Focused Unity continuations fail before running their assertions.
Their original logs, the fixture compilation repair and final check receipts are
retained under the candidate's `.planning/debug/`. A restricted-environment
attempt must never be labelled a successful complete local gate.

No handover, merge, push or external-agent message is authorized until the
maintainer confirms the concrete result. The external NPC integrator must assign
the actual next common ModBuild, review any overlap with its newer dev, and run
the complete required gate on that final integrated tree before distribution.
Existing Build625 numbering is intentionally not reused for a hardware candidate.

## Next hardware comparison

Use the same save, fixed view and three revealed rooms. Wait for cosmetic
preparation to settle and collect complete Debug windows. First compare each new
option independently, then their combined Frame defaults, with resolution kept
at the current choice. The maintainer separately runs the resolution comparison.

Verify every room and original floor, reveal/pooling and doors, continuous wall
fade in both directions/eyes, targeting/collision, live setting reversal, near
hand geometry and idle-to-action/held transitions. Record actual submission and
triangle coverage; zero substitution is a supported fallback, not proof of gain.
Compare application mean/p95 time and logic/render/unbracketed spans.

Repeat as two- and four-player host and visitor, with shared fans/windows/actions
and native animations. Existing `Net.BoardMirrors`, `Mirror.NodesDriven`,
`Mirror.NodesSkipped`, `Net.Fans`, `Net.Presentation.NativeSend`, `Net.ApplyPending`
and `Net.Send` scopes distinguish local headroom from MP mirror work. No protocol,
NPC content, cadence, secrecy rule or remote animation is changed by this lane.
