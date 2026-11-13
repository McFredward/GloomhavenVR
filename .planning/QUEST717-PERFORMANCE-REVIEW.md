# Quest B716 second-room performance review — 2026-10-21

## Scope and established evidence

Base: `feature/quest3-standalone` `955fc1209`, main1.2.0 / B716. The primary
agent reviews the supplied eye recording; this lane reads the matching capture
manifest, startup log and durable startup-state snapshot. The installed APK,
receipt and observed current startup banner all identify B716. The previous
startup log belongs to B712 and is not substituted for current evidence.

The current snapshot ends in ProcGen after1,127.39seconds. Its retained scenario
pacing population is17,520frames over910.358seconds: approximately19.25sampled
frames/second,13,648frames at least40ms and193 at least100ms. This population
includes gameplay transitions and is not a compositor FPS/thermal measurement.
The latest24recorded slow frames have zero contemporaneous GC collections. GC
therefore does not explain those particular slow frames; earlier44collections
do not prove that GC caused the sustained room-opening regression either.

Current logcat starts after the gameplay process has exited and retains no app
process history. The startup text log contains installation descriptions of the
performance monitor, not its actual FRAME/STEPS/SPLIT windows. Consequently this
capture cannot attribute the measured slowdown to CPU submission, engine logic,
GPU shading, native draw calls, thermal state or a specific material. Hiding walls
improving the result is the maintainer's hardware evidence; it does not identify
which of those costs their presence creates. Demeo's appearance alone provides
no evidence of equivalent meshes, draw submission, shader passes or CPU work.

## Frame optimization coverage

Shared Frame defaults explicitly include Quest; saved choices remain authoritative.
The native floor probe reports Vulkan, pixelLights0 and an instancing-capable
Amp_Basic material. It is a private unshadowed copy, not a measurement of all
actual per-object lighting or GPU passes. Native Important lights can still
qualify for pixel rendering independently of the global cap.

The durable actual-consumer snapshot says:

- Figure distance detail is enabled. Six actors /23bodies have23derivatives;
  admitted vertex population is102,818→63,774. This demonstrates derivative
  consumers, not a measured GPU improvement or permission to change figure look.
- World material mode2 resolves its supported shader, with only28registered
  surfaces and zero last-pass variant/native slots. The old snapshot omitted the
  last camera and rejection reasons. Zero could describe an auxiliary camera,
  inactive/hidden sources, or absent coverage; it does not prove a global failure.
- Environment driver is active and healthy but has zero compatible surfaces,
  chunks and instance groups. A requested switch is not a successful draw batch.
- Terrain has12prepared exact/reduced surfaces after2,401visits. Its589floor and
  1,800identity refusals preserve existing conservative ownership. The last actual
  surviving draw reports five reduced surfaces,580→110triangles; the most recent
  paired invocation has zero. Current measured PreCull average/max is0.062/0.117ms
  over64debug samples. This lane is not a demonstrated dominant CPU cost.

The current material owner supports native Amp_Basic and exact original world
coordinators. There is no source-proven reason to admit arbitrary behaviours,
unreviewed coordinator subclasses, materials or generated names. No scope guard,
native shader equation, effect gate, default, scene visibility or mesh is relaxed
merely to increase these counters. Pillar LOD remains removed.

There is one established recurring CPU cost: QuestGameScope's five exact UI
families query every loaded scene root through GetComponentsInChildren(Type,true),
with staggered1second family deadlines. The snapshot records5,441queries,
last13.7795ms / worst46.9691ms. The primary agent owns any targeted optimization
of this separate path. Its measured cost alone does not explain a52ms mean frame.

## Source changes in this lane

Retain the exact already-composed FRAME, STEPS, COUNTS and SPLIT reports in a
24entry bounded Quest Debug history, stamped with actual frame/time/active-scene
context. The existing startup-state performance field includes it. Original
logging, measurement cadence, clock spans, missing-counter descriptions and
rendering decisions stay intact. This adds no timer, recorder, camera callback,
second scene census or per-frame formatting. Unexpected reports are bounded to
16,384characters with an explicit truncation marker; teardown clears the history.
Desktop and ordinary logging return before context reads or retained formatting.

Expand the existing world-material consumer snapshot with last camera/frame,
inactive/camera-excluded/performance-hidden populations, scope/shader/effect
refusals and cumulative discovery candidates/refusals. Observe up to16exact first
blocking component types during its existing ancestry walk. No additional walk
occurs; mutable ownership is still validated for every existing camera pass.
Settings/scene restoration clears discovery/type evidence. These are diagnostic
facts, not justification to force an unsupported renderer into the optimized path.

## Focused validation

All source/test invocations reap their children and call the common completed-run
cleanup API. Results and exact source hashes are retained externally under
`quest3-local/build/evidence/quest717-review-20261021/performance` before worktree
removal. Compilation failures during harness development are preserved and are
not counted as passing causal controls.

| Check | Scope |
|---|---|
| Strict Release build |0warnings /0errors |
| Retained performance history |16assertions; five compiling causal controls reject ordinary-player retention, omitted delivery, excess capacity, stale sessions and reversed observation order |
| Actual Unity world material / Quest public final-camera boundary |791assertions; two compiling controls reject loss of native coordinator scope and unreviewed subclass admission |
| Actual Unity incremental census and existing timing producer |1,489runtime +13realengine-frame assertions; one compiling control rejects omission of the exact retained SPLIT report |

These are focused host/source checks. They do not certify Android GPU work,
headset presentation, full-game performance, another complete local gate or
an actual successful second-room optimization.

## Next hardware measurement and decision

Keep the same Standalone settings, view scale and scenario. Let the first room
settle, then open the second room with walls still visible and wait at a matching
pose. Export while the app remains running, preferably within a few minutes of
the room-opening transition. The new retained windows survive later logcat
eviction; export timing still determines which bounded windows remain available.

Use their actual named steps, callback logic/render spans, material coverage,
camera count and available native/Unity GPU figures to choose the next change.
If render submission dominates, investigate repeated opaque scenery submission
and actual batch/instance admission while preserving native clone/material/fade
ownership. If supported expensive shaders remain native, identify the exact
rejected component/program/effect and prove a compatible route before changing
it. If the GPU timer is unavailable, say so rather than treating callback spans
or an interval-like XR number as GPU busy time. If gameplay/discovery dominates,
remove proven redundant work in that callback before changing visual quality.
Wall removal remains an explicit player policy, not an automatic new compromise.
