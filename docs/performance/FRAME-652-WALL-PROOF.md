# Build 652 wall policy: independent native runtime and CPU evidence

The maintainer's Build 651 headset logs did not establish a hardware improvement from
Build 650. The loaded three-room window increased from roughly 94.11 to 112.13 ms,
World 23.94 to 30.36 ms and Wall 7.82 to 12.12 ms with similar renderer populations.
Those different runs increased broadly across layers and are not a causal A/B.
Build 650's local CPU receipt must not be presented as a Frame FPS gain.

Build 652 changes visible geometry through an explicit quality setting. Regular
retains original view-dependent fades. Hide all instantly removes eligible walls and
attachments and pauses the old wall pipeline. Auto observes fully loaded, focused,
closed-options gameplay, defaults to 15 FPS and latches the same compromise after a
complete two-second low-FPS window. A new scenario or manual mode change resets it.
The native renderer flag does not disable gameplay objects, colliders or lights.

## Reproducible lanes and boundaries

Run `scripts/check-wall-performance-runtime.py --source-root <integrated-root>`
against Unity 2021.3.5f1. It compiles the complete actual Performance partial and arch
attachment classifier, plus complete actual LateUpdate, ClearAllBlocks, sender,
ComputeWireKeys/FNV, draw-write guard and water-protection methods. The scenery
SetHidden primitive is extracted in full. Inputs, copied production sources,
strict build logs, expected causal controls, source stability, Unity exit code and
raw results are retained privately under `.planning/debug/frame652-proof`.

Actual Unity renderers, meshes, hierarchy, bounds, native scene handles, materials,
MPBs, flags, cloning and Camera.Render pixels execute. The test's native Game scene
has a real valid negative handle; it is never replaced by an invented positive ID.
Only Time/Application reads are explicitly aliased to named deterministic test
clock/focus boundaries so synchronous tests can advance loaded/unloaded windows.

Original game controllers/configuration, staged collector inventory and commit,
broad floor/actor/mod classifiers, held registry, attachment restore helpers and
scenery classifier/collider ownership are explicit boundaries. The full collector
is source-bound, not executed by this fixture. Counts proving entry/helper invocation
cannot certify the collector's classification across every original/DLC room. Existing
full World and Environment lanes independently execute their complete production
owners and native effect/delivery primitives. Neither lane recreates the entire game
or certifies OpenXR picture, GPU time, multiplayer or headset FPS.

The complete actual key/FNV computation has one declared invocation counter at its
entry, solely for once-on-recovery assertions. Actual nonzero sender output and
stable keys also have to agree. No invocation occurs inside a warmed timed region;
there is no Harmony timing observer in the CPU region.

## Native correctness and causal controls

The wall scope proves immediate absence of actual wall/shelf/column pixels and
continued floor/actor/gate/shared-arch pixels. Native enabled, active, collider and
Light state remain intact. It checks wall, shelf, column, mounted and unit-dressing membership through the real
gather, shared corner/segment exceptions, native arch-family hierarchy,
water bounds, exact held identity changes, native ready reassertion, current MPB
preservation, foreign force-off ownership, real scenery release, lifecycle additions,
retirements, destroyed renderers, loading coalescence, collector finally/backoff,
Auto exclusions, single long hitches, duplicate-eye samples and latch recovery.

The settled hidden actual LateUpdate executes no old Tick, collector stage, MPB
setter, sender scan or draw-write diagnostic. Returning Regular resumes its entry
and native sender. New hidden-collected anchors get actual FNV keys once before
regular fading resumes; no wire census runs on ordinary frames, transitions into
Hide all, scenario teardown or absent/currently different native scenes.

The World lane adds exact-renderer native read observation, avoiding false conclusions
from unrelated live fixture renderers. Hidden ownership restores original material
slots once and makes zero later mesh/material/MPB reads even across transient native
mask clearing. Unhidden floors retain current live material validation. The native
clone retains original materials and receives forceRenderingOff=false. Two new
controls remove the hidden read guard or repeatedly restore a hidden source; both
must fail the exact native-read assertion. Repeated RestoreRenderer itself reads
native slots, so its earlier rejection is valid and explicitly recorded.

The Environment lane executes its complete owner with explicit exact hidden-identity
delegates. It proves active chunk release and native clone/ready/settings restoration
cannot clear wall masks; hidden walls cannot enter private chunks, while floors
remain live. A generic ambient/particle overlap is an explicit ownership boundary,
not proof that the original collector assigns every particle to a wall. The actual
ambient recovery primitive must preserve independent wall ownership. Three controls
remove the wall-reader guard, ambient unmask guard or last native-ready notification.
The existing native-continuation resolver-fault control still removes the entire
actual MaterialReady catch; its new multiline formatting does not weaken that test.

## Measured CPU expectation

`scripts/check-wall-performance-world-cpu.py` reuses the existing actual-source World
CPU fixture and stores a private, explicitly transformed fixture transfer. The only
addition configures the optional wall membership API before Install, outside timing.
Baseline is exact dev651 `2f6819eee`; complete production PreCull and real Unity
material/mesh/MPB APIs execute through direct delegates, both eyes per frame.

There are 440 native sources, 64 inactive, 376 active, representative 402 active material
slots / 23 shared originals and 581 live scope nodes per eye. The half-hidden case owns
220 active source identities. These are synthetic mixed fixture populations, not a
measured wall fraction of the supplied scenario. The all-hidden case is an upper-bound
World-path stress case, not the actual policy hiding floors/actors/gates. Baseline651
has no wall-quality setting, so its corresponding sources deliberately stay visible.
That changed geometry is the authorized compromise, not a detail-preserving speedup.

Fifteen warmed, interleaved AB/BA rounds of 32 application frames produced:

| Workload | Baseline lane median | Candidate lane median | Paired median change | Paired bootstrap95% interval |
| --- | ---: | ---: | ---: | ---: |
| Same presentation, no predicate |3.6790 ms|3.6731 ms|−0.004%|−0.742% to+0.530%|
| Same presentation, false predicate |3.6336 ms|3.6398 ms|+0.091%|−0.010% to+1.086%|
|220 active identities hidden |3.6528 ms|1.8511 ms|−49.348%|−49.460% to−49.113%|
|All376 active identities hidden |3.6745 ms|0.0683 ms|−98.140%|−98.145% to−98.132%|

Both hidden workloads won 15/15 paired rounds. Their pooled per-frame P95 fell
4.833→1.933 ms and4.9942→0.0745 ms. The visible cases' confidence intervals cross zero;
no measured general FPS improvement or material-cost regression is inferred from
those small differences. The stronger expected saving comes from removing native
wall draws and the entire costly wall pipeline; this lane measures only World CPU.

Entry-overhead timing compiles the exact 651 LateUpdate as baseline, with the same
cheap modeled Tick and logging boundary. It separately prices added Regular/Auto
observation/settled Hide overhead over 15 alternating warmed rounds of 30000 entries.
It is not a timing of the real old collector, applier or whole wall Tick. The final exact-651-entry measurement added a paired median 0.00000143 ms for
Regular, 0.00032238 ms for unlatched Auto and 0.00031586 ms for settled Hide per
entry. These local measurements bound the additional entry branch, not full wall work. Such local constants cannot predict Frame hardware FPS.

Unity Mono's GetAllocatedBytesForCurrentThread fails the positive 65536-byte allocation
control (returns 0). No allocated-byte or zero-allocation claim is certified. Mono heap
deltas are retained only as noisy diagnostic evidence.

## Native command-buffer boundary

Real Camera.Render proves forceRenderingOff suppresses normal wall geometry. A
foreign CommandBuffer.DrawRenderer still draws red pixels with the flag true.
Our World/Environment substitute paths are tested and guarded; an arbitrary foreign
command draw is outside that ownership. The root's original-game source audit treats
native room/object occlusion maps and fog/outline targets separately. Shared native
floor/room offscreen commands are not indiscriminately disabled. The proof does not
claim that all foreign or native command draws have ceased.

## Ordered receipts and failed attempts

All paths below are relative to the private proof debug root and are preserved for
archive before worker cleanup. Failed attempts remain evidence, never passing claims.

- `attempts/run-o4awoex0` and `run-ymau72qf`: fixture compiler access/name repairs.
- `attempts/run-awihbhak` and `run-fijo2gpd`: actual Unity required play mode and
  forbids duplicate CreateScene names; the fixture now uses valid native scenes.
- `attempts/run-jstbvq9u`: initial 658 wall assertions, clone=false and foreign command
  pixel=true; provisional entry timing.
- `wall-controls/run-cpaph7on`: 661 production assertions; nine controls pass, the
  collector-remask mutant is correctly rejected at an earlier lifecycle assertion.
- `wall-final/run-wmlsxpct`: 661 assertions / all ten initial controls, stable hashes.
- `world-attempts/run-23c_a22z`: added source required explicit native discovery.
- `world-attempts/run-jq77luk0`: global counters included other live sources; exact
  renderer observation corrected the fixture without changing production.
- `world-attempts/run-37qe47qs`: 772 World production assertions.
- `world-final/run-x4cw3n8g`: 772 production/all original 67 controls/new hidden-read
  control pass; new renotification control rejects correctly at earlier native read.
- `world-focused-final/run-k7ct0pwf`: 772 production and that corrected causal control
  pass. The manifest contains 67 inherited plus two new controls: composite coverage
  is 772 assertions and all 69 controls, not a fresh one-process full-control pass.
- `world-cpu/run-abm6ul4h`: private harness Git path binding failed compilation;
  corrected transfer preserves actual repository baseline paths.
- `world-cpu/run-l9k_u4vo` and `harness-w0ri1rxm`: final World CPU rounds, full sources,
  samples, transfer description and source-stability receipt.
- `cpu-repro-compile`: current shared CPU fixture initially failed strict CS0649
  because the optional read-observer renderer was never assigned in that lane.
  `cpu-repro-compile-final` passes zero warnings/errors with explicit null initialization;
  real production/timed algorithms are unchanged. Timings are inherited, not repeated.
- `world-repro-final/run-ggdj122s`: 772 production assertions and the corrected
  renotification control pass with that final shared-fixture initialization.
- `environment-attempts/run-u4gr7xwn`: fixture declaration typo, compile failure.
- `environment-attempts/run-4cz49fzs`: 11469 production assertions before late-owner case.
- `environment-final/run-l3rbuqt_`: added setup native-ready callback increased a
  local count; changed assertion to its exact pre-callback count plus one.
- `environment-complete/run-ekk0j6ys`: 11470 assertions plus the existing continuation guard
  and three new consumer controls. This is a focused subset, not all old controls.
- `wall-complete/run-yf06hple` and `wire-diagnosis/run-pcfm9cn3`, `run-r7zd7zrt`:
  actual production recovery defect. Valid loaded native scene handle −1298 was rejected
  by `_performanceScene < 0`, so no wire rebuild/output occurred. Root removed only
  the sign check in `ebf00f799`; exact native scene/generator identity still gates it.

Final wall receipt: `wall-final-complete/run-1h5ao5y2` passes 675 production
assertions and all 14 causal controls in one Unity process, plus exact-651 entry
timing. Source hashes remain unchanged and Unity exits zero. The sign-check mutant
is rejected using the real native negative handle; no handle substitution is applied.

The final Performance partial hash is
`3ecb84cc0b7d3bef1c990966f6913b0c4ba752698d272825f02cfe8b7127be04`;
Scenery source hash is
`d4d22ccdf3e3c91b49b0140969935a9671850a81ec812c6333e2ce5dc752f7eb`.
The exact baseline entry commit is `2f6819eee17ac602c6efda69d6e2db57651903cc`.
Both proof and source-stability receipts include all additional input hashes.

The earlier wire-recovery scope's unconditional mutant was rejected at an earlier
valid recovery assertion; the final control now asserts the precise extra
Regular-to-Hide work. All failed attempts and raw samples remain archived.
The root runs and records the full integrated repository gate separately; no focused lane above claims that gate.
