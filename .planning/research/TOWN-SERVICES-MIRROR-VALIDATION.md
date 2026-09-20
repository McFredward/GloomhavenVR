# Town-service mirror validation

## Reproduction

Run `python3 scripts/check-town-service-mirror.py --source-root /path/to/current/checkout`.
Use `--suite basic` for the original-root-Canvas checkpoint, `--no-negative-controls`
for diagnosis, and `--output-dir` to retain a run elsewhere. Unity defaults to
`/home/claw/unity-2021.3.5/Editor/Unity`; override with `--unity` or `UNITY_PATH`.
The runner requires dotnet, xvfb-run, and a working OpenGL software or hardware driver.
All generated projects, production copies, hashes, assemblies, PNGs, measurements and
logs live under gitignored `.planning/debug/town-service-mirror/run-*`.

The harness compiles complete production assets, binding, frame, codec, delta,
material, mirror, motion and neutralization sources against real Unity 2021.3.5 engine/UI/TMP.
Only the external logger, head-camera provider, presentation-layer assignment and original texture/sprite lookup
adapters are fixtures. Original UI controllers are fixture MonoBehaviours with real
Awake/OnEnable counters. No fake Canvas, renderer, TMP, transforms or codec is used.

A fresh isolated Unity project imports TMP's bundled essential resources, enters
Play Mode, and runs each independently compiled case through engine frames. It
renders owner and observer into 512x384 render textures. Comparison requires visible
UI, average summed RGBA byte error <= .02/pixel and maximum <=20; disabled-Canvas
cases instead require completely blank images. The small tolerance accounts for
camera/scale floating point differences; missing masked widgets cannot pass.
Each negative control must compile and fail its named assertion. Compilation errors
and unrelated exceptions never count as a detected mutation.

## Checkpoint evidence (2026-09-21)

**Final full run:** `run-lhg4is1r` passed 285 production assertions and all ten
independently compiled negative controls in Unity 2021.3.5f1 / llvmpipe LLVM20.1.2.
The mutations corrupt text, mesh enabled state, group alpha, raycast suppression,
color, RectMask padding, inactive-template creation, Canvas enabled state, final
sibling ordering and Mask visibility. Each failed its specific expected assertion.
No unrelated failure is accepted. Source hashes and immutable source copies are
retained with the run.

Final measured means for thirteen-node original modules:

| Workload | Mean CPU time per iteration |
| --- | ---: |
| Two unchanged modules: capture/read | .087ms |
| Four unchanged modules: capture/read | .134ms |
| Two modules × two owners: changed capture/codec/receive/apply | 1.43ms |
| Four modules × four owners: changed capture/codec/receive/apply | 5.31ms |
| Four modules × four owners: unchanged playback | .00133ms |
| Four inactive modules: capture | .126ms |
| Closed owner: capture | .000113ms |

The changed loops run 50 simultaneous text updates, unchanged/inactive sampling
runs 200 iterations and steady playback/closed loops run 1000. The four-by-four
changed loop observed 28 collections. See the workload and allocation limitations
below; these synchronous CPU measurements exclude engine layout/render work.

`run-e7jcex17` passed basic production and six independent negative controls under
Unity 2021.3.5f1 / llvmpipe LLVM20.1.2. Source SHA256 identities are recorded in the run.
Coverage includes TMP/legacy text, rich text, colors, filled sprites, raw textures,
CanvasGroup/inherited alpha, RectMask2D padding/softness, stock stencil Mask, root
pose/scale, raycast suppression and inert template/observer instantiation.

`run-7k1nodb3` passed 198 assertions extending this with sibling reorder, newly added
Shadow, disabled Canvas, cumulative packet loss, four independent owner images,
reopen/late old session, owner close, peer removal and timeout isolation. Four-owner
unchanged playback took 0.6248ms across 1000 ticks in that run. Its initially reported
zero allocation counter is **not valid evidence**: an explicit 8192-byte allocation
calibration in `run-3x8lasqv` showed Unity Mono returns zero from that API. Current
measurements identify it as unavailable and separately report heap deltas and GC
collection counts. Heap deltas are not total allocated bytes.

`run-3x8lasqv` measured twelve bound original nodes per module (TMP, legacy Text,
Images, RawImage, Mask, RectMask2D, CanvasGroup, runtime Shadow); the later handle
MeshRenderer adds a thirteenth. The workload creates two or four distinct live
modules, mirrors each module to two or four owners, and runs 50 simultaneous text
changes. Timings include native TMP setters, production source probing/capture,
codec write/read, delta expansion and real binding application. Two modules × two
owners took 1.45ms/update; four modules × four owners took 3.97ms/update. Two/four
unchanged module captures took .096/.127ms/call. Four inactive module captures still
took .118ms/call. The four-by-four changed workload triggered 21 GC collections over
50 updates. These are synchronous CPU stress iterations within one Unity frame,
not a 90Hz cadence or a claim of per-frame VR cost. No network wait, Unity layout
rebuild, render or GPU work is included in those timed loops. Larger native catalogs
can cost more; allocations are material and the counter cannot quantify their total.

`run-dfjarlbb` passed the complete production suite (256 assertions): both Canvas
regressions below, nested module sibling order, retained handle MeshRenderer,
missing-baseline arrival after its delta, and exact intermediate/final motion
phase comparisons. Every surface's world pose is checked independently before
camera recentering; configured layer/head-camera assignment is checked before image
isolation. The layer adapter is a fixture, so this proves routing to that adapter,
not correctness of the game's VR layer bit itself.

Motion uses the production time-argument method at a representable half phase and
at the complete target. The owner fixture writes that same native animation phase;
world position/rotation/scale, group alpha and real rendered pixels must match.
Ordinary packet updates also continue actual Mirror.TickRemote calls over >.12s
before endpoint images, allowing production's <=100ms interpolation to finish.

With motion enabled and thirteen nodes/module, `run-dfjarlbb` measured four modules
× four owners at 5.57ms/simultaneous changed update, with 28 collections across 50
updates; unchanged four-module sampling was .127ms/call. Earlier measurements above
are retained with their exact workloads. Timing variation, native catalog size and
allocation pressure prevent interpreting these synthetic loops as headset budgets.

## Source-proven defects found by actual images

1. An extra observer wrapper Canvas changed RectMask2D's root pixel coordinate
   system: a cyan masked widget disappeared entirely. Original clip rect was
   (-142,-86,100,72); observer became approximately (-1.15,-1.13,.90,.84), cull=true.
   Removing the unnecessary Canvas or matching its pose/scale/rect restored the
   image (summed RGBA error reduced from 1,623,486 to 1,511). Production now avoids
   redundant root/child-module Canvases and aligns needed standalone wrappers.
2. The first correction was insufficient for a standalone subsection whose original
   outer Canvas is larger and whose own local animation scale is .8. Actual images
   in `run-63j4pgtu` differ by 367,752 summed RGBA byte values. This requires preserving
   the original outer Canvas coordinate frame, not substituting the section's frame.
   Explicit original Canvas pose/rect/settings now restore the image; the full
   production suite verifies this exact case.
3. Inserting an excluded row's observer host after applying parent siblings reversed
   two overlapping original widgets (87,284 summed RGBA difference). A final shared
   sibling-rank pass now preserves the original relative order after insertion.
   The full suite compares this overlap and independently disables that pass as
   a negative control.

## Bounds

The fixtures exercise stock Unity widgets and real rendered output, not every native
game prefab, VR stereo rendering, multiplayer transport, asynchronous art readiness,
or hardware tracking. Current production rejects custom Graphic subclasses before
activation; that protects callback inertness but does not demonstrate visual support
for those native subclasses. Hardware and native prefab parity remain separate checks.
