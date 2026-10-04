# State — where the project stands

**Build617 Frame hardware review, 2026-10-04. Documentation only; no new binary.**

Both supplied sinks identify 617 / e1da52eaa and agree on all 22 frame summaries.
The run enters a scenario directly at 3408 per eye; no 3D-map visit or new
screenshots are established. All 12 fully loaded/worn windows average 51.37 ms
(4687 frames / 240.4 s), versus the earlier616 comparison's 49.35 ms. There is no
demonstrated overall improvement. Initial preparation, mixed windows and the
user-presence-loss tail remain outside ordinary-play comparison; later summon/
burn gameplay is included. Natural VR motion does not invalidate the practical
comparison, but prevents attributing the small difference to one optimization.

Preparation succeeds in 38.82 s: four class skins, 271/271 collected sprites,
29/29 unused backings, no failure. Three real figure pickups reuse parked original
ghosts. Nine fan receipts report no readiness wait, but first construction still
costs 118.78 ms and a later rebuild 122.33 ms. Native ConsumeElement/InfuseElement
serialized art arrays remain outside the prep collector and existing test shape;
new portraits/native stat conversion still cost up to 126.19 ms. No cache eviction,
preparation failure or memory leak is established.

The completed wall budgets contain 21 commits in 67 fully post-hide/worn judged
cycles: 20 scene-signature changes and one safety ceiling. Thirteen emitted heavy
rescans cost 133.23–209.93 ms. Exact native waypoint/action FX ownership is the
next dependency-closure target, with shared exclusions across signatures and
collectors rather than name heuristics or removing the ceiling. Draw delivery
has 72 correct intermediate HIGH/left-eye samples; endpoint/block-clear sampling
has a coverage gap and hardware popping remains open. Native Choreographer.Update
now has positive 207.26 ms evidence on a 382.03 ms frame. Most measured spikes
have no GC, and larger unassigned frames still lack a trustworthy GPU-busy split.

See [the combined result and next implementation order](../docs/performance/FRAME-618-ANALYSIS.md),
[pacing and actual applied budgets](../docs/performance/FRAME-618-PERF-AUDIT.md),
[resource/preparation closure](../docs/performance/FRAME-618-COLD-PREPARATION.md) and
[native wall evidence](../docs/performance/FRAME-618-WALL-EVIDENCE.md).
Immutable input hashes and reproducible scripts are retained in
`.planning/debug/frame618-review/`. Only engineering Markdown changes; the
validated617 source, resources, wire and defaults remain identical. Extraction,
sink equality, hashes, documentation links and i18n are checked without repeating
the already-passed unchanged runtime suites. Own audit worktrees are retired after
integration/push; hardware evidence and other active worktrees are preserved.

---

**Scenario interaction preparation and bounded hitch attribution, 2026-10-04: 1.1.0 / ModBuild 617.**

Build 616 was the pre-implementation hardware baseline; the later617 result is
recorded above. On every VR
platform, the existing scenario spinner now covers one original resource job per
frame after native loading, alternating card and figure/stat work: all party
class/card sprites, up to 64 ordinary backings, reusable original figure ghosts and already-authored stat
art. Local/remote consumers share their existing paths. Acquisition rebinds the
current evaluated native pose/masks and validates mesh/material/bone/LOD identity;
sleeping figures, live holds, native actions and immediate lazy fallback remain.
Timeouts cancel pending preparation without retiring live presentation or holding
native continuation. Unseen async portraits and complete native stat conversion
remain cold work; the whole 65–99 ms stat hitch is not declared eliminated.

The bounded Debug census batches borrowed work and deduplicates complete ancestor
walks. Per-frame native callback and GC evidence continues past the 120-frame
summary cap; five optional actual engine time recorders reset/resume each frame.
First-install hook calibration is excluded from native frame evidence. Wall
samples now inspect original material/indexed-MPB/gate state at actual head-camera
pre-render, and bounded ceiling reports rank existing phase costs. Normal logs
gain no recurring diagnostic stream. The safety ceiling remains: the approximately
141 ms forced publication and hardware wall popping are **still open**, without a
proven complete native dependency closure or headset pixel result. See [implementation
and next capture](../docs/performance/FRAME-617-IMPLEMENTATION.md).

Integrated coverage includes 120 local suites: 118 passed in the full attempt;
two fixture-only bounded resumes cover the remaining failures without repeating
unrelated green suites. The public-cabinet fixture now follows actual Unity frames;
its intermittent initial failure is retained, not claimed as an NPC runtime fix.
The inactive-tint control targets the earlier causal preparation assertion without
weakening its mutation. Final calibration proof passed 1,427 production and 13 real-
frame assertions plus three relevant controls; other census controls retain their
full-run evidence. Fourteen source gates, 307,473 byte-exact wire/golden assertions,
the scheduler registry, bundle/index, config/patch/log surfaces and five bilingual
document pairs are covered. Strict Release has zero warnings/errors. The compiled
comparison against immutable 616 has 14 intended behavior types and eight exact
build-constant consumers changed, four types added, no removals, and identical
references/resources. The wire wrapper's duplicate local scheduler was canceled;
its remaining golden stage ran directly against the same built binary. The original
failed/interrupted receipts and an independent complete coverage ledger remain in
`.planning/debug/frame617-implementation/`. No full-gate-success exit is fabricated.

---

**Build616 hardware recheck and next strategy, 2026-10-04. No new binary.**

Both supplied Frame logs identify 616 / d52b3c328 and enter the scenario directly.
Six loaded, pre-options/all-zero windows average 49.35ms versus 615's 49.60ms:
only 0.5% lower, with no demonstrated improvement in recurring hitches. Ordinary
ability-fan construction reaches 172.37ms, pickup 109.10ms with nested ghost
construction 55.89ms, and stat preview peaks 65.93–98.54ms. At the final same 8970-renderer
population, vegetation 10→5→0% masks 71 then 76 additional renderers. One finished
5% timing window is confounded by visible options and figure preparation; no
complete finished 10% or final 0% window permits a vegetation-only time estimate.
The maintainer explicitly accepts slider/preparation stalls; they are excluded as
optimization targets. Extreme tail spikes follow lost user presence and are not
classified as worn-headset gameplay hitches.

Original native wall materials and the visual-clock clamp are actually applied,
but the reported pop remains open. The passing GL fixture covered LOW only;
the capture also uses HIGH/toggle-native branches. Numeric intermediate fades
cannot certify native pixels. Refresh-safe diagnostics progress for 130s but
true configuration changes still cancel them before a complete SCENE/SIM/GFX
inventory. Prioritize original resource/ghost/preview preparation before ordinary
input, audited redundant steady publication, and bounded engine/GC attribution;
retain 1:1 presentation and immediate interaction. See [the current audit](../docs/performance/FRAME-617-ANALYSIS.md)
and [native wall proof boundaries](../docs/performance/FRAME-617-WALLS.md), with
[the exact cold-resource call paths](../docs/performance/FRAME-617-HITCH-TARGETS.md).
These FRAME-617 documents describe the next investigation, not a shipped617 fix.
This follow-up changes developer documentation only; source/bundles/wire remain
identical to validated616. Evidence is in `.planning/debug/frame617-review/`.

---

**Original NPC meshes and Frame hardware follow-up, 2026-10-04: 1.1.0 / ModBuild 616.**

The supplied Frame logs identify 615 / 64db88dc9, with a real CampaignMap visit
followed by the scenario. Seven fully loaded scenario windows average 49.60ms
versus 53.20ms in 612 (6.8% observed improvement). They skip 24/24 stable wall
publications and 18.5% of health-bar bone checks. Newly culled Animator counts
and structural chunk counts are both zero here. Pickup 105.12ms, nested ghost
construction 48.32ms and stat-panel previews 68–79ms identify cold interaction
bursts; most of the longest frames remain outside named mod scopes. No GPU busy
counter or memory-leak attribution is established. See [the hardware audit](../docs/performance/FRAME-616-PERF-AUDIT.md)
and [implementation boundaries](../docs/performance/FRAME-616-IMPLEMENTATION.md).

Remove the immersive resident body-detail slider and all twelve simplified NPC
meshes in parts 17–28: 53,762,395 bytes. Keep the three original resident bodies,
faces/materials/bones, per-renderer skinning and shared animation/audio clocks.
Scenario detail/distance controls retain 1594 derivative entries and 66 identical
parts. The retired NPC key remains inert/hidden for config compatibility. Fresh
or missing-key Frame standalone profiles default immersive residents off;
PC profiles still default on, and explicit saved choices remain untouched.
Existing Frame profiles with true must disable the Environment toggle once.
This is a full installation with a smaller indexed asset set, not DLL-only.

The wall pop is a defect: native wall gates on a floor-labelled material admitted
the cheap shader, which cannot render its native dissolve. Active channels now
veto cheap shading, and later native writes restore originals before drawing.
A separate wall-only timestep bound preserves rendered intermediate states after
stalls. Native gameplay, coverage/dwell and shared card/UI fade clocks remain.
Refresh-only display changes now retain incremental diagnostic jobs while closing
the old FRAME pacing window; actual scene/load/config/debug invalidation stays.
Existing bounded Debug progress gains traversal/capture-span counters, without a
per-frame normal-level stream. Attached camera handlers also stop invalid automatic
Unity message-signature errors, retaining their explicit event subscriptions.
Hardware must verify appearance and completed census.

Integrated coverage contains all 118 local suites, 14 source gates and 307473
wire/golden assertions. The full attempt passed 115 local suites immediately;
three bounded resumes provide the remaining evidence. The native mirror's
publication failure captured an original InvalidCastException; its cause remains
unestablished, and passing resumed production is not a runtime fix claim. Added
failure-only diagnostics retain the immediate completeness requirement. The
cabinet direction control passes at its intended failure marker on its bounded
resume; the roster control now targets the earlier equivalent graphics-boundary
assertion. Original failures are retained, without rerunning unrelated green
checks. Bundle/index and config/patch/log surfaces pass; strict Release has zero
warnings/errors and the five bilingual document pairs agree. The compiled
comparison against immutable Build615 has ten intended behavior types, eight
exact build-constant consumers, TownNpcSkinningQuality added and
TownNpcDistanceDetail removed. References and resources are identical. Receipts,
source hashes and the independent coverage ledger are under
`.planning/debug/frame616-review/`. Hardware appearance, completed inventories
and residual hitch causes still require the next capture.

---

**Frame CPU/render implementation checkpoint, 2026-10-04: 1.1.0 / ModBuild 615.**

All five ranked tracks in FRAME-612-ANALYSIS now have integrated source changes:
actor-only particle signature exemption, exact same-commit wall bounds, native
bar/state admission and optional event-free transform culling, stable original UI
inventories, incremental Debug inventories/hand attribution, and optional small
readable structural chunks. See [the implementation and settings](../docs/performance/FRAME-615-IMPLEMENTATION.md),
[wall scope](../docs/performance/FRAME-615-WALLS.md),
[actual actor audit](../docs/performance/FRAME-615-ACTORS.md) and
[diagnostic lifecycle](../docs/performance/FRAME-615-DIAGNOSTICS.md).

The original Drake already uses CullUpdateTransforms; the new setting preserves
that authored mode. Cave mesh readability and active effects can prevent chunk
admission. Effective counters must establish actual application in the next run;
requested settings and Editor timings do not establish headset improvement.
Original 2D-map/non-NPC windows, input and NPC/remote clocks retain live semantics.
All Build614 NPC parity changes remain integrated. No game assets, mesh bundles,
wire grammar or native gameplay commands change in this DLL-only round.

Final integrated coverage passes all 118 local suites, 14 source gates and
307473 wire/golden assertions. The full local run had two fixture-binding
failures; both affected suites passed with exact final-tree production hashes
after including the new grab-eligibility helpers. Diagnostic marker wording was
rechecked only in its affected census suite. Original failures are preserved;
unrelated successful suites were not repeated. The diagnostic source checker now
recognizes compound writes, with twenty actual field-read/escape controls.
Bundle and config/patch/log surfaces pass, strict Release has zero warnings/errors,
and all five bilingual document pairs agree. The independent compiled comparison
against immutable Build614 has 24 intended behavior types, eight exact inlined
build-constant consumers and eleven intended new types; no type is removed and
reference/resource sets are identical. Final receipts, bound source hashes and
the resumed-coverage ledger live under `.planning/debug/frame615-review/`.

---

**NPC multiplayer original presentation, 2026-10-03: 1.1.0 / ModBuild 614.**

Both supplied PCVR logs identify **612 / a606f9530**; the host uses SteamVR and
its peer VirtualDesktopXR. All twelve supplied screenshots were inspected. The
peer's Player.log reports over six seconds of pending numeric state and original
artwork dependencies that differ across lazy borrow order and native atlas wraps.
See [the complete NPC parity review](TOWN-MP-614.md) for each reported surface,
its source correction, the explicit guide exception and hardware limits.

The public cabinet's logical category/page state no longer waits for immutable
artwork to be ready. Actual peer input callbacks accept a category/page before
claiming public authority; rejected input cannot steal it. Item fans, lifted
stock, offered native cards, options and confirmations still use original model
widgets and descriptors, not transferred bitmaps. Stable original sprite and
template aliases resolve game assets across both clients. Numeric pose/hover/
scroll delivery is losslessly packed within the existing 864-byte, 15 Hz lane;
real held cards and purses attach to the already smoothed avatar hand. Cold
fronts borrow and repay a finite early share without reducing the existing
streams' 32-second completion guarantee. In the controlled four-peer Unity
snapshot transport fixture, eight fronts finish in **0.561 s**, compared with
2.244 s before the new early turn and 6.936 s with the original queue control.
These numbers measure the controlled fixture, not live headset appearance time.

Merchant and mage offered-hand readiness has a numeric lifetime independent of
private UI sessions. Actual card ownership alone locks gaze and speech to that
visitor; concurrent visitors at different residents remain valid. One resident
author chooses the exact spoken take or optional silence. The priestess looks
at the accepted donor, with finite shared blessing/particle/light/audio clocks.
The maintainer's new local-only exception covers **town card pre-drop guides**;
purse props, purse ghosts and release feedback remain public. Scenario guides
and concealment rules retain their established behavior.

The complete local gate ran once: 105 of its 112 suites passed immediately.
Seven bounded failures were resumed. Four passed on the first targeted resume;
two remaining fixture expectations and a merchant badge fixture boundary were
corrected and passed independently. The new native donation replay
adds 161 Unity assertions and five effective causal controls with imported
shipped WAVs, original codecs/receivers, actual audio sources, seeded particles,
light, donor head movement and mouth blend shapes. Complete wire/golden vectors
pass **307471 assertions**, including sustained maximum-frame stream fairness;
14 source suites, bundle/surface checks, strict Release with zero warnings/errors
and five bilingual document pairs pass. Original failed receipts are retained.
Passed unrelated suites were not repeated. The final manifest contains **114
local suites**: the original 112 plus native donation and native peer replay.
The latter passes **45,862 assertions plus seven causal controls** on the final
integrated tree, including actual root resizing without artwork resend. After
that bounded runtime correction, basic mirror, public cabinet and original item
front suites also pass again. The compiled comparison against the original
integration commit e01a601a contains 23 intended behavior types plus eight
build/stamp consumers; no type is added/removed and reference/resource sets stay
unchanged. Receipts live under `.planning/debug/npc614-review/`.

Existing Steam Frame findings and Build613's scoped skinning correction remain
in effect. No NPC mesh/bundle is regenerated by this multiplayer correction.
Real network latency, the final headset picture, controller vibration and shared
audio audibility require the next matching-Build614 hardware test.

---

**Steam Frame hardware analysis and scoped skinning, 2026-10-03: 1.1.0 / ModBuild 613.**

The new run identifies **612 / a606f9530** and enters the scenario directly;
Gloomhaven_unified initialization is not an active map test. Loaded play averages
53.20ms versus 61.21ms in Build610 (13.1% observed improvement, not isolated
per-change savings). Later camera samples show only the two eye passes. Player.log
contains Debug records filtered from the companion LogOutput sink: 36 admitted
scenario derivatives halve original body vertices, and the disabled cloth/FX
budgets are effective. The confirmed NPC-body slider affects only the three
active map residents, which this scenario capture does not exercise. See the
[Frame612 analysis](../docs/performance/FRAME-612-ANALYSIS.md) for measurement
boundaries, remaining CPU spikes and configuration experiments.

The figure budget fought the existing hand guard over global skin weights,
producing 12830 repairs. Build613 removes global ownership and caps only owned
actor/NPC renderers, including native Auto slots, while hands retain FourBones.
Off, shutdown and foreign renderer changes retain exact restoration rules. The
actual extracted hand guard is exercised in both update orders with a causal
control. Bounded Debug readbacks now expose requested versus applied NPC and
distance tiers, far5 counts, body vertices and effective interval/bone settings.
No mesh assets, bundle layout, wire records or saved defaults change in this fix.
Headset frame-time gains from the skinning correction remain unmeasured.

Remaining evidence points to 158ms average wall-table commits, zero eligible
idle-bar skips despite prepared bounds, native Animator work, UI maintenance and
synchronous Debug inventories. The wall exemption, native bar eligibility and
animation-cadence proposals are analysis, not implemented fixes. Existing optional
UI intervals and disabling SceneProfile are useful separate experiments.

Validation covers all **112 local suites, 14 source suites and 294369 wire/golden
assertions**, strict Release with zero warnings/errors, bundle/surface checks and
five bilingual document pairs. The original complete local receipt records
111 passes and one fixture compile failure: its isolated log boundary lacked the
Debug-tier Info method now used by the production readback. Only that affected
suite was resumed after the one-line fixture correction (98 assertions and 23
controls); successful suites were not repeated. Full distance tests include 709
assertions and five controls; the bounded skinning proof adds 140 assertions and
three controls. Original failed and resumed receipts remain separate. The final
compiled612 comparison changes four behavior types and eight inlined build
constants, with no added/removed types, resources or binary changes. Evidence
and frozen production hashes live in `.planning/debug/frame613-validation/`.

**Steam Frame CPU/render checkpoint, 2026-10-03: 1.1.0 / ModBuild 612.**

Windows installer follow-up: the PowerShell preflight still accepted only the
old 20/45/75 identities and numeric part names after the bank gained tier5 and
mixed-tier distance parts. This omitted installer update reproduced the reported
`Invalid figure mesh identity` on the committed index. Its corrected original
PowerShell block now accepts all 1606 entries/78 compatible parts, and the full
script reaches the deliberate absent-game guard after SDK validation without
installation writes. Asset-copy and archive membership paths already use the
complete indexed part set. Runtime, mesh data and ModBuild remain unchanged;
validation is limited to the affected packaging/preflight checks. All 20 targeted
tests pass, including actual PowerShell execution, malformed asset rejection and
the full installer's no-write guard. Source-bound before/after evidence lives in
`.planning/debug/frame612-installer-validation/`.

The latest Frame inputs identify **610 / 3edbcb284**. Loaded CampaignMap and
ProcGen windows average 56.91ms and 61.21ms respectively; actual GPU busy remains
unavailable. Four automatic camera passes still run: two eyes plus discarded
ScenarioCamera/UI Camera passes. See [Frame610 analysis](../docs/performance/FRAME-610-ANALYSIS.md)
for exclusions, scope limits and the next hardware checks. These inputs do not
measure Build612 or establish its headset FPS gain.

Optional unused-camera suspension now disables those discarded Camera components
completely. Original managed main-camera readers, native projection enumeration
and late scenario-mask discovery retain the actual original Camera identity.
Native movie decoding and visible flat menu/preview captures remain necessary;
failed bridge setup retains the previous draw suppression instead of blocking
input or continuation. Shutdown, capture handoff and disabling the setting restore
owned cameras. Static panel maintenance and proven stationary idle-loop bar bone
checks gain configurable intervals; input, gameplay callbacks, root/state changes
and non-loop actions remain immediate. The Debug card census seeds once and follows
original widget lifetimes instead of repeatedly scanning the resource heap.

Original native avatar loops are sampled during preparation to obtain a stable,
state-scoped world-space bar ceiling. Dragon wing beats no longer move the bar;
sleep/wake and non-loop actions retain dynamic anchoring. Build610's rule mount
double-applied the canvas scale, making its text effectively invisible. Original
inline rules are visible again; long rules show a rendered `...`, expand on hover
or pin on click, and animate private goals/elements down. Unchanged owner-authored
record96 and its interpolated clock retain remote content and intermediate frames.

Distance LOD and NPC body detail use new immutable mesh parts with original
materials, skeletons and facial expressions. Far Berserker/Drake/Sun demon meshes
reduce the former lowest triangle counts by 83%/56%/81%. A renderer-level bone
influence cap also reaches native NPCs that explicitly forced FourBones. These
are reversible quality compromises on PC as well as Frame. Fresh Frame keys select
camera suspension, distance LOD, NPC detail45, two bones, panel interval0.05s and
idle-bar interval0.1s; saved settings remain intact. **FULL INSTALL REQUIRED:**
415 additional derivatives in 29 parts are required; the original 1191 derivatives
and 49 parts remain unchanged. Asset size is not evidence of frame-time savings.

Validation covers all **112 local suites, 14 source suites and 294,369 complete
wire/golden assertions**, strict Release with zero warnings/errors, bundle/surface
checks and five bilingual document pairs. The first complete local invocation
recorded 109 passes and three failures: two isolated fixture dependency boundaries
and an actor-suite wall deadline. Only those bounded checks were resumed; actual
actor production inputs are identical across its 27 completed cases. The final
review's late camera-mask fix has its own 94 native Camera assertions, causal
control and two affected source suites; the previous 18 camera controls remain
independent evidence. Original failed receipts are preserved, and no claim is
made that every final case ran in a single invocation. The native skin proof runs
1615 assertions plus 26 controls; distance meshes 310 plus three controls; original
inline rules 229 plus three rendered controls and stale-DLL rejection. The 24 mesh
renders and owner/peer rule renders are Editor evidence, not headset acceptance.

The independent compiled611 comparison contains 22 intended changed behavior
types, eight inlined build constants and reference-table order only; eight helpers
are added. No type is removed and reference/resource sets are unchanged. Final
source-bound evidence lives in `.planning/debug/frame612-validation/`. The
[passthrough investigation](../docs/performance/FRAME-PASSTHROUGH.md) distinguishes
hardware passthrough from application composition: vendor extensions are absent
in this Proton run, core alpha-blend support remains unqueried, and Steam Link
chromakey equivalence is not established. No Frame passthrough path is implemented.

**NPC multiplayer checkpoint, 2026-10-03: 1.1.0 / ModBuild 611.**

The supplied host and peer logs both identify Build609; the reviewed video and six
screenshots belong to that run. They show an empty public cabinet, overlapping
mage guides, delayed/grey moving item faces and missing observer donation visuals.
The logs independently contain missing `HeroHighlight_Darken`, ambiguous native
flow/spark textures and exact card topology differences at invisible `PokePad`
nodes. These are public 3D-map rendering defects, never card concealment.

Numeric pose, button/scroll, hover and shader animation no longer wait behind full
native artwork delivery. Additive message26/record97 is capped at one 864-byte
event per 1/15 second, with change detection and a one-second recovery heartbeat.
Visitor-held item/purse originals attach to the existing interpolated avatar hand
frames; immutable assets retain their original module/session/structure affinity.
Urgent cold fan baselines share compression without increasing the ordinary
fragment scheduler budget. Old wire records retain their exact byte layout.

A successful original temple callback records a durable commit revision/age for
shared resident animation and blessing VFX even after its visitor departs. Temple
native commits remain serialized briefly, but never occupy the NPC or hide another
visitor's purse. Merchant/mage occupations begin only on physical offering and use
the approved board avatar/name component above the NPC; removing the offered card
releases the occupation. Original mage cancellation clears native state and returns
only that exact offered card/session. Mage decision geometry clears the original
raised book through the complete owner-authored yaw sweep.

The maintainer's NPC test of 2026-10-03 explicitly permits **one shared enchantress
pre-drop hologram** instead of duplicated visitor guides. Its visual author is
selected among ready visitors, independently of native UI/gameplay ownership.
Every eligible visitor keeps its original release target and controller haptics;
the shared response uses the existing guide's ink/scale paint. No other NPC parity
exception is introduced. Review also found personal item partition descendants,
released mage return flights and independent temple inscription/purse previews
crossing the old shared-widget election; these originals must remain visible.

Integrated validation passes **111/111 complete local suites, 14/14 source suites
and 294,369 complete wire/golden assertions**, strict Release with zero warnings
or errors, original bundle checks, surface checks and five bilingual document
pairs. The complete local run took 716.1 s with eight jobs. A final existing wire
vector mistook the new urgent container for completion of the cold catalog; its
reviewed test-only correction now checks both exact original dependency bytes and
complete current card reconstruction, then all 20 unchanged background originals.
Two compiled causal controls reject omitted promotion and corrupted native data.
Per the maintainer's explicit 2026-10-03 instruction, the already successful local
and source suites were reused after this bounded test-only correction; independent
receipt/log-hash verification and 1,802 unchanged frozen inputs support that reuse.
The affected complete wire executable was rebuilt and rerun on `409913c3`.

Against the preserved reviewed Build610 assembly, 22 intended existing types
change, alongside five inlined build constants and one decompiler-label-only
file; nine helpers are added. No type, reference or resource is removed, and no
reference/resource changes. The historical guard comparison still differs because
its baseline predates these features; its bundle/surface/build stages passed.
The original native palm renderer executes 84 assertions and a 15-case yaw/scale
clearance matrix; the original motion capture/playback proof executes 593 assertions.
Queue and interpolation timings are controlled fixture measurements, not observed
LAN latency or headset performance. Hardware acceptance of Build611 remains pending.
Compact immutable inputs, reviewed frames, source-bound worker proofs and the
complete parity review are retained under `.planning/debug/npc611-review/`;
final receipts, unchanged-input proof and the independent compiled comparison
live in `.planning/debug/npc611-final-validation/`.

**Desktop dev checkpoint, 2026-10-03: 1.1.0 / ModBuild 610.**

The two supplied Frame captures both identify 609/24400128c; normal uses 3408×3408
per eye and low 2256×2256. Loaded, tracked 0/0 windows average 54.70/51.21 ms.
Three approximate moving-view comparisons favor low by 2.5–9.8 ms; ordinary VR
motion is retained as valid workload, while actions/view distributions prevent
an isolated causal saving. Later original figure application proves 34.92% fewer
admitted body vertices at 0/0, but figure-only frame timing has mixed signs.
Remaining loaded work includes 46–73 ms synchronous Debug card discovery,
32–36 ms first-use class-art scopes and 170 ms wall-table commits. Neither actual
GPU busy nor a memory leak is established. See
[Frame609 analysis](../docs/performance/FRAME-609-ANALYSIS.md); immutable inputs,
extraction and reviewed compiled609 baseline live in
`.planning/debug/frame609-run-analysis/`.

Build 610 replaces the sleeping/flying bar lifetime maximum and authored flight
floor with conservative original-skin envelopes on native bones. Preparation
uses private original fine meshes; evaluated poses raise immediately and lower
smoothly without a steady mesh bake or hierarchy/renderer inventory. Native
props/headless/unsupported skins retain their existing anchor fallback. Source
ownership excludes mod home mirrors and unrelated effect skeletons. Reduced
figure meshes do not change the anchor; scene and last-bar release clear caches.

The maintainer accepted a leftward foldout for long native scenario rules.
Short rules remain beside the board; long rules open with a localized button
into a scrollable native-text viewport. The private battle goal and complete
element-caption union reserve separate space. Additive board record 96 carries
owner-authored original rows, wording/styles, geometry, header appearance,
hover and scroll, including intermediate opening/closing frames. Both rules
and elements consume one owner-frame clock. Existing 52/53 bytes and privacy
rules stay unchanged; no game controller/callback runs in remote clones. MR
backings must use visible, clipped UI bounds rather than full scroll content.

Full-wire integration caught the old shared test-buffer capacity and a genuine
large-board transport limit: a complete U16 board could wait 43 seconds behind
other maximum streams and outlive its former 32-second assembly deadline. Only
boards whose validated encoded size exceeds the legacy 40KiB budget scale that
deadline, bounded below 52 seconds. Compressed small boards retain 32 seconds.
Datagram bytes, scheduler fairness, bandwidth and other stream deadlines stay
unchanged. This is a synthetic transport proof, not a reported hardware stall.

Hardware acceptance of the new poses, foldout and multiplayer animation remains
pending. The preserved Quest checkpoint below is independent historical work;
this Frame/board round does not implement or modify that port.

Final integrated validation on runtime commit `c517aa19` passes **14/14 source
suites, 109/109 complete local suites and 292,488 wire/golden assertions**, strict
Release with zero warnings/errors, bundle checks, five bilingual document pairs,
and independent complete suite/log-hash verification. The final local run took
789.3 s with eight jobs; all 1,782 frozen source/tool inputs remain unchanged.
The original-native skin proof has 533 assertions and 20 defect controls; the
native rules proof has 63 assertions, a competing-layout control and a separate
stale-DLL provenance rejection. These are Unity fixtures, not headset outcomes.
Against the preserved reviewed609 assembly, 24 intended existing behavior or
capacity types and five inlined-build-only types change, with ten added helpers
and no removed type/reference/resource or changed reference/resource. The final
compiled scope matches its independent preflight review. Guard exit1 reflects
the older historical compiled baseline, not failed subordinate gates. Evidence
is retained in `.planning/debug/frame610-final-validation/`; compact analysis and
worker proofs are in `.planning/debug/frame609-run-analysis/`.

**Quest branch checkpoint, 2026-10-03: `feature/quest3-standalone`, ModBuild 609.**

The maintainer explicitly authorized implementation on a new branch from current
`dev` (`5344a550`, Build607). This isolated branch includes the owned-game local
builder, asset recovery/audit, generated static Harmony integration, an Android
OpenXR template and native passthrough composition in Unity's existing XR session.
A signed ARM64 IL2CPP hardware diagnostic with the original animated BanditGuard
asset is available privately; it uses the explicitly authorized DUMMY profile.
The latest parallel Frame608 changes are retained in this branch. Quest diagnostic
evidence does not change Frame hardware acceptance or imply that its desktop
package is the Quest app.

The checkpoint tests head/controllers, native passthrough, local diagnostic storage,
embedded identity and disabled Guildmaster/Workshop tooltip presentation. It is
**not a playable campaign, complete mod startup, original save or multiplayer port**.
The real export reports placeholder shaders, 16 serialization-layout errors and
deferred bundles; standalone lifecycle/content-root and original IL2CPP/platform
startup remain required. Compilation/signing and source tests are separate from
unverified Quest images, tracking and performance. See
[Quest implementation evidence](QUEST3-IMPLEMENTATION.md) and
[private hardware test procedure](QUEST3-HARDWARE-609.md).

Final integrated validation on runtime/tool commit `cc041fab` passes **14/14 source
suites, 107/107 complete local suites and 286,760 wire/golden assertions**, strict
Release with zero warnings/errors, bundle/figure-bank checks and independent
suite/log-hash verification. Relative to reviewed Frame608, eight existing compiled
types change only in the build constant; QuestText is the only added type, with no
removed type/reference/resource. The private signed ARM64 APK is built and validated
with explicit SDK/NDK/JDK selection and retained runtime shaders. Hardware remains
unverified. Evidence is in `.planning/debug/quest3/validation/`; original game payload,
APK, accounts and signing keys are excluded from Git.

The following records the preserved `dev`/Steam Frame baseline:

**Updated 2026-10-03: dev 1.1.0 / ModBuild 608, Frame607 hitch analysis and targeted interaction optimizations.**

The new Frame607 hardware capture confirms real figure/FX/cloth application, including
34.87% fewer admitted body vertices at 0/0. It does not isolate the slider FPS benefit:
all settled markers are 0/0, other quality intervals have active VR Options, and many
stationary samples are untracked. Both supplied logs identify607/5344a5504 and describe
the same run. Loaded interaction frames still reach 200–500 ms, with source-backed
card capture/census, prop pickup and atomic wall-table work. Actual GPU busy remains
unavailable; conditional managed heap samples fall repeatedly and do not prove a leak.
See [Frame607 hitch analysis](../docs/performance/FRAME-607-HITCH-ANALYSIS.md).

Build608 removes false wall invalidation from native-named visual ghost children via
their exact FigureVisualMirror owner, retaining real native and wall-shader signatures.
The native HexHighlight root emitter is also identified by its exact same-object
HexSelect_Control; unrelated descendants keep their conservative world facts.
Synchronous card diagnostic work is Debug-gated, bounded across frames and genuinely
deduplicated; blackout keeps native correction/recovery while bypassing repeated
unprintable inventories and impossible bright/translucent candidates. The offline
reader now accepts negative head-height medians, recovering eight pose records without
relaxing measurement exclusions. No render feature, wire layout or asset bank changes.
First-use mip readback and native/prop work remain targets. The current prewarm feeds
already-adopted card widgets, not all party hands or complete mip caches; loading-time
coverage needs the actual native hand skins and must not activate gameplay controllers.
Hardware cadence for608 and a controlled figure-slider FPS benefit remain unverified.

Final integrated validation on runtime commit `06cdfa01` passes **14/14 source suites,
103/103 local suites and 286,760 wire/golden assertions**, strict Release with zero
warnings/errors, five bilingual document pairs and independent suite/log-hash coverage
verification. The complete local run took 689.2 s with eight jobs. Input source hashes
remain identical to the validated tree. Against the preserved reviewed607 assembly,
only the three intended behavior types (card half-tone diagnostics, face blackout and
wall fade) and eight build-constant-only types change; no type/reference is added or
removed. Guard exit1 reflects its historical compiled comparison, not a failed gate.
Real Unity card proofs pass 486 assertions and ten defect controls; wall classifier
proofs pass 19,479 assertions and 21 controls; original-native local/remote figure
proofs pass 184 assertions and 18 controls. Evidence and compiled/source/artifact hashes
are retained in `.planning/debug/frame608-final-validation/`, with the native selector
asset graph and compact worker proofs in `.planning/debug/frame608-wall-ownership-worker/`.
No608 hardware performance or full party-hand prewarm outcome is claimed.

Build607 separates player/enemy/figure-FX/cloth measurement windows at the early
Update seam, retaining OLD settings on completed samples and discarding one mixed
transition frame. Preparation and steady FRAME tags carry revisions; a completed
native figure late pass, closed VR Options and a two-second quiet guard precede
steady collection. Scalar readiness avoids a new per-frame renderer census.
The offline report excludes known mixed/preparing windows and flags unknown legacy
state; actual GPU busy remains unavailable. This does not retroactively establish
the figure-only FPS benefit or justify the Build606 mesh package cost.

The existing WindowMaterialise key is exposed under Graphics → Windows/panels.
Fresh standalone Frame defaults OFF, PC remains ON, and saved choices are preserved.
Live OFF restores active effects and pending native close continuation exactly once;
cards, native window motion and NPC effects keep their separate behavior. This is
a DLL-only update; the complete Build606 mesh banks remain required. The final
integrated gate passes on runtime commit `38eeb1e4`: **14/14 source suites,
102/102 local suites, 286,760 wire/golden assertions**, strict Release with zero
warnings/errors and five bilingual document pairs. The local run took 627.7 s
with eight jobs; independent manifest/log-hash verification passed. The preserved
reviewed606 compiled comparison has seven intended existing behavior/config types,
eight inlined-build-only types and one new measurement helper, with no removal or
reference change. Guard exit 1 reflects those reviewed compiled changes, not a
failed subordinate gate. Proof and source hashes are retained under
`.planning/debug/frame607-final-validation/`. Actual Unity figure readiness has
98 assertions and 25 runtime defect controls; the measurement adapter has 48
assertions and seven controls, and the MR/switch suite has 576 assertions and
seven controls. An independent review additionally removed mixed-frame work-counter
contributions and omitted unfinished native captures at the boundary. Build607
hardware now confirms slider application and remaining hitches; no isolated FPS gain is claimed. See
[Build607 measurements and test procedure](../docs/performance/FRAME-607-MEASUREMENTS.md).

The maintainer approved the Frame605 optimization follow-up and clarified spectator
semantics: unused native flat draws are always suppressed in VR; DesktopMirrorLeftEye
now selects the left eye versus black. Frame defaults start black, saved choices remain.
Native headset menus/captures keep reversible camera ownership. Same-query UI memoization
and a 64-slot signature ring reduce duplicate work without delaying native presentation.

Selection-only native hex visuals no longer trigger a wall-table rebuild. Masked scenery
retains structural facts but skips unused render preparation; held local/remote props rescue
only their actual roots. Pure wall geometry and labels are prepared once with same-frame-only
hierarchy reuse and a final room-change gate. Genuine geometry changes still need the atomic
commit. New reversible settings add small compatible floor draw chunks, simpler floor shading
and positively identified environment-ambience budgets. Native source mesh/material slots,
cloning, visibility and callbacks remain; render masks end with each camera and recover before
native Update if interrupted. A successful MaterialLoaderHeal direct finish also publishes the
preparation edge. Immediate native floor identity plus the original plate-geometry verdict
prevents shader ownership from overlapping wall-dissolve saved arrays.

Figure sliders now use offline native-body derivatives as well as authored LODs. All 1,191
verified derivatives from 409 sources ship in 49 game-format parts; demand preparation and
immutable process-lifetime caches keep first grabs free of generation/I/O. Exact UV, skin,
bindpose and solver exclusions, strong native signatures and live local/remote ghost mesh
refresh preserve animation and 100% restoration. Body/FX diagnostics read actual current
meshes. New quality keys start enabled/reduced on Frame, original on PC; existing values stay.
Install the complete package including the new index/parts, not just a DLL. See
[Frame606 implementation and limits](../docs/performance/FRAME-606-IMPLEMENTATION.md).
Final integrated validation passed on runtime commit `9f267b98`: **14/14 source gates,
101/101 local suites, 286,760 wire/golden assertions**, all three main banks and 49
indexed figure-part headers, strict Release with zero warnings/errors and five bilingual
document pairs. Complete local coverage took 631.5 s with eight jobs; independent report
and log-hash verification passed. Compared with the preserved reviewed605 snapshot,
21 intended existing behavior/config types, eight inlined-build-only types and six new
rendering/patch helpers changed; no artifact or embedded resource was removed. The managed
serialization reference is intentional. Guard summary exit 1 records these reviewed compiled
changes, not failed subordinate gates. Actual Unity proofs pass 185 environment assertions /
19 runtime defect controls plus material-repair binding control, 32 desktop assertions / 11
controls and 82 original-native figure mesh assertions / six controls. The real ZIP verifies
byte-identical index, all 49 parts, main banks and current DLLs, including Frame launcher
permissions/text. Retained source, compiled and archive hashes are in
`.planning/debug/frame606-final-validation/`; compact worker proofs are retained separately.
Hardware appearance and OpenXR spectator pixels remain separate from automated proof.
The supplied Build606 / `8d7b8d7c2` hardware run now confirms 36 applied native-body
derivatives: admitted original/current vertices 278,708/181,529 at both sliders zero
(-34.87%, including inactive LOD slots), exact restoration at 100/100, and cheaper
discarded desktop-camera work. The maintainer reports much smoother, nearly playable
scenario performance. Full loaded means remain 53.28 ms versus the previous 52.67 ms
with different views/settings; figure-only FPS benefit is not isolated. Floor chunks
remain zero for 17 unreadable originals in this scenario. See
[Frame606 hardware analysis](../docs/performance/FRAME-606-ANALYSIS.md).

Historical hardware basis: the Frame605 run confirms deferred decorative creation (287 native
instances / 1,234 renderers) and ambient figure-effect suppression. Loaded, tracked windows
average 52.67 ms, still far from smooth standalone play. Native caps changed, but the original
cached body aggregate did not; nine of 17 actors lacked authored coarse meshes. At that build,
DesktopMirrorLeftEye=false still retained discarded native drawing. That historical switch
behavior is corrected above. See [Frame605 analysis](../docs/performance/FRAME-605-ANALYSIS.md).

The supplied Frame604 / 253e89378 evidence confirms 17 admitted actors, 8/8 native
LOD caps and 15 disabled cloth solvers. Only eight bodies have authored coarse
meshes: lack of obvious visual change does not prove an inert slider. All 3,189
admitted scenery meshes were masked, but original shared forest bays, separate
grass blades and wall attachments still escaped classification. Build 605 expands
positive native coverage, avoids construction of proved purely decorative leaf
instances at zero density, and finishes remaining masking before loading-screen
closure/material reveal. Source prefab bundles remain loaded; floor plates and
actual wall cores remain. A separate configurable figure ambient-effects budget
reduces identified demon idle effects without hiding bodies or combat cues. Home
ghosts follow native evaluated condition poses/phase without copied gameplay
callbacks, and figure glow/depth retain native cutout alpha instead of exposing
rectangular VFX surfaces. Saved settings are retained; fresh Frame FX density is
zero and PC defaults to original. The main bundle changes, so install the complete
new package. See [STEAM-FRAME-SCENERY-605.md](STEAM-FRAME-SCENERY-605.md).
Final integrated validation passed on `909c227c`: **14/14 source gates, 97/97 local
suites, 286,755 wire/golden assertions, all three bundle-format checks**, strict
Release with zero warnings/errors and five bilingual document pairs. Complete
local coverage took 614.955 s with eight jobs; manifest IDs and report/log hashes
were independently verified. Relative to reviewed604, twelve intended existing
behavior/config types, eight build-constant-only types and eight new rendering/
patch helper types were reviewed; no type or embedded resource was removed or
unexpectedly changed. Actual Unity scenery/effects/overlay proofs pass 179/37,
93/21 and 164/12 assertions/negative controls; overlay pixels use original Drake
sleep/fly curves locally and remotely. The final gate supersedes the cancelled
pre-followup run and covers independently completing floor/grass materials.
Evidence is retained at `.planning/debug/frame605-final-validation/`; headset/FPS
acceptance remains separate. Supplied post-load windows of 65.61–88.37 ms do not
predict Build605 performance.

The preceding Build604 integration:

The supplied Frame603 / 5c60f6604 logs confirm the saved zero figure/vegetation
and disabled cloth values. Geometry masking works for 3,277 admitted meshes,
but the figure diagnostics in Player.log explicitly report zero actors/LOD caps
and zero stopped cloth solvers. Native models live under the Game board outside
additive ProcGen; strict geometry-scene equality excluded every actual actor.
Build 604 uses native board/actor identity across scenes and honors detail/cloth
choices while locally/remotely held. Cloth OFF also bypasses rescale cooking and
free-hand probe work at their sources. Native complete tree/trunk and wall plant
coverage is expanded with reversible ownership of only tree-exclusive picking
colliders. Town NPC rigs and cards/UI retain their presentation. Loaded windows
remain 64.76–102.12 ms at 3408 per eye with changing views/settings, not a matched
A/B or evidence of improvement. No new headset result is claimed.
See [STEAM-FRAME-SCENERY-604.md](STEAM-FRAME-SCENERY-604.md).
Final integrated validation passed on `aa003c6d`: **14/14 source gates, 96/96 local
suites, 286,754 wire/golden assertions, all three bundle-format checks**, strict
Release with zero warnings/errors and five bilingual document pairs. Complete
local coverage took 605.4 s with eight jobs; report/log hashes were independently
verified. Actual Unity proofs include scenery 111 assertions/19 negative controls,
figures 53/10 and physical cloth 72/12. Cloth OFF showed no secondary deformation
through 50 moving frames and recovered its simulation on ON. Portable scenery
coverage is 75 assertions/15 negatives; native figure boundaries have 47 assertions.
Compared with the preserved reviewed603 compiled snapshot: six intended behavior/
config types plus eight types differing only in the inlined 603→604 build constant
(including NetProtocol), no added/removed types. Exact source, logs, hashes and
compiled evidence are retained at `.planning/debug/frame604-final-validation/`.
The earlier complete gate was superseded after review identified real named vines
in 17-renderer native tree units; the final gate covers their complete carrier and
mixed-wall boundaries. No config key, patch target or log marker was removed.
Bundles remain the Build602 banks; this checkpoint changes the DLL. Users coming
from Build600 need those updated banks as well. Headset appearance and FPS remain
unverified for 604.

The preceding Build603 integration:

NPC Build 602 was published to origin/dev before this phase. Requested obsolete-run
cleanup freed 399.61 GB net; hardware logs, original/reference assets, all existing
branch refs and final NPC proof were retained. See the gitignored audit at
`.planning/debug/storage-audit-20261002/README.md`.

Latest Frame evidence identifies Build 601, including CampaignMap and ProcGen.
Its zero-decoration budget actually masks 3,457 meshes, but the grass cap coupling
prevents independent comparisons and anonymous structural foliage leaves remain.
Build 603 separates grass/vegetation/dressing, extends original mesh/LOD provenance
and retained collider representation, and adds original native player/enemy mesh
detail, optional scenario-figure cloth and next-load procedural generation controls.
PC keeps original defaults; fresh Frame adopts sparse detail without overwriting
persisted choices. Native gameplay, structural cores, reveal focus, map UI, NPCs
and local/remote cards are retained. Actual headset appearance/FPS remain open.
See [STEAM-FRAME-SCENERY-603.md](STEAM-FRAME-SCENERY-603.md).

Final integrated validation passed: **14/14 source gates, 95/95 local suites,
286,754 wire/golden assertions, all three bundle-format checks**, strict Release
with zero warnings/errors and five bilingual document pairs. Full local coverage
took 590.7 s with eight jobs, and all report/log hashes were independently verified.
The fixture inventory now pins 95 local/71 hosted suites without weakening coverage.
Compared with the reviewed NPC602 compiled snapshot: 18 changed/six added/zero removed
types; seven unrelated network/plugin types differ only in the inlined 602→603 build
constant. The other changes are the ten intended config/localisation/UI/scenery
types plus NetProtocol and six generation/figure driver/patch types. The historical
98fba1a8d guard comparison reports 56 changed/13 added/zero removed, incorporating
the already reviewed NPC phase. No config key, patch target or log marker was removed.
Town bundles are unchanged from 602; users coming from 600 need that updated town bank
as well as the 603 DLL. New hardware results are not implied by automated checks.

The preceding immersive multiplayer checkpoint:
The paired PCVR logs and supplied images identify Build 600. The review fixes
rejected resident transitions, original font/effect dependency failures, first-use
cabinet hierarchy mutation, held-stock loss, cold-page layout handover, movement
pauses, donation timing, speech routing and native purse depth. Shared actor/stand
lighting preserves original material animation and capture constraints. See
[TOWN-MP-602.md](TOWN-MP-602.md) for the complete review and hardware limits.
The town bundle and DLL both change. Final integrated validation passed:
**14/14 source gates, 92/92 local suites, 286,751 wire/golden assertions, all
three bundle-format checks**, strict Release with zero warnings/errors, and
five bilingual document pairs. The full local suite took 588.7 s with eight
jobs. The compiled review against `98fba1a8d` has 46 changed/seven added/zero
removed types, all explained by this review and propagated defaults/build
constants. No config key, patch target or log marker was removed. No new
headset result is claimed. This validated NPC checkpoint was published at `1b033daa`
before the requested storage cleanup and subsequent Frame phase.

The preceding essential-decoration checkpoint:
The Build 600 hardware test confirms the previous grass scope was ineffective:
zero density masked only 27 of 6,560 active renderers; the initial partial
hierarchy scan masked none. Loaded windows remain about 120–125 ms/frame at
changing views and settings, not a matched A/B. The maintainer requests massive
decoration reduction down to essential scenery. Build 601 introduces a broader
generated-decoration control (fresh Frame0%, PC100%) while retaining the old
grass key as an additional cap. Settled loading, late placement and reveal must
all reach discovery. Gameplay geometry, local/remote UI/cards, pickups and doors
remain protected, and hidden decoration must not leave invisible laser blockers.
The new local Unity suite executes the complete production classifier and driver;
portable source/lifecycle checks remain in CI. Actual scene-wide mask counts,
headset appearance and FPS gain are hardware-open. See
[STEAM-FRAME-SCENERY-601.md](STEAM-FRAME-SCENERY-601.md) and
[STEAM-FRAME-THIRTEENTH-HARDWARE.md](STEAM-FRAME-THIRTEENTH-HARDWARE.md).

Build 601 final integrated validation passed: **14/14 source gates, 87/87 local
suites, 286,621 wire/golden assertions, all three bundle-format checks**, and a
strict Release build with zero warnings/errors. The complete local gate ran once
after runtime integration. Production-classifier coverage includes 38 portable
assertions/six negative controls and 50 actual Unity 2021.3.5 assertions/eight
negative controls, including 1,200 late-generated meshes. The compiled comparison
against `7198477bf` contains the intended 17 changed types and one added native
material-readiness notification patch; the extra network types change only through
the inlined ModBuild constant. No config key, patch target or log marker was
removed. Native material completion is observed without changing materials.

A separate read-only asset finding shows that the supplied Standalone Fastest
profile references high Apparance generation. No native generation setting is
changed in this checkpoint; the further reversible geometry lever and reveal
risks are recorded in [STEAM-FRAME-NATIVE-DETAIL-601.md](STEAM-FRAME-NATIVE-DETAIL-601.md).

The preceding Build 600 integration and validation:
The maintainer explicitly authorized visual compromises for large Frame scenarios,
then clarified that all Frame optimizations must be settings available on PC too;
Frame is a different defaults profile, not a forced runtime policy. Build 600
adds a reversible decorative floor-grass density control (PC100%, Frame25%),
switchable shared wall-read and light-work caches, and restores the desktop mirror
toggle on Frame instead of overriding a saved off choice. New-scene Debug census
sampling bypasses an older menu cooldown; broader selected callback timings and
honest residual-span labels support the next hardware comparison. Original game
obstacles, walls, floors, cards, NPCs and local/remote widgets remain outside the
grass budget. Actual qualifying counts, headset appearance and FPS improvement
remain hardware-open. See [STEAM-FRAME-SCENERY-600.md](STEAM-FRAME-SCENERY-600.md).
Final local validation passed: 14/14 source gates, 86/86 local suites, 286,620
wire/golden assertions, all three bundle-format checks and a Release build with
zero warnings/errors. The compiled-form review reports the intended 24 changed
types and six added types; propagated network changes are only the Build 600
constant. No config key, patch target or log marker was removed. Headset gains
and the loaded scene's actual eligible grass population remain unverified.

The new Build 599 run confirms severe tracked scenario cost at 3408 pixels/eye:
complete post-load windows average 82.85 and 117.64 ms/frame at different views.
The nine selected native callbacks cost about 1.13 ms in the final sample and
do not explain the CPU wall. Their Debug summaries exist in the matching
`Player.log`, while `LogOutput.log` filters them. Unity draw counters expose only
zeros, and the menu census cooldown suppresses the scenario's detailed census.
No actual GPU busy time or matched Build 598/599 improvement is established.
Wall-table commits still cause 254–292 ms gameplay hitches. That preceding
Build 599 evidence review changed documentation only and did not rerun unchanged
runtime tests. See
[STEAM-FRAME-TWELFTH-HARDWARE.md](STEAM-FRAME-TWELFTH-HARDWARE.md).

The latest Build 598 Frame log is a direct `ProcGen` scenario run, with no 3D-map
test. Its apparent ~36 ms interval had an untracked HMD and seven visible
renderers. At stable 3408 pixels/eye the later scenario windows remain around
128–131 ms/frame. A wall-fade-off trial did not materially improve the owner's
experience; the log's brief off interval supports lower named mod time but
cannot yield a clean exact A/B because VR Options was open. Build 599 adds
bounded Debug-only native callback and actual Unity render-counter probes
(explicit `n/a` if the player exposes none), plus a scene/tracking/eye-aware
log comparison script. No headset speedup is claimed. See
[STEAM-FRAME-LARGE-SCENE-PERF.md](STEAM-FRAME-LARGE-SCENE-PERF.md).

The release ZIP now exposes only `BepInEx/` and the English/German install
texts at game-root level. The Steam Frame desktop starter, setup scripts and
art are nested under the plugin. The in-game updater accepts and copies the
desktop starter; successful upgrades remove six obsolete root helpers. The repository
install guides moved to `docs/install/`. The archive and updater have focused
local coverage. See
[STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).
The maintainer now confirms the separate `GloomhavenVR` Library entry and its
artwork on Frame. SteamVR still opens a dashboard requiring Resume Game, and
the active game's panel is the original AppID with a gray cover. The shortcut
continues to launch AppID 780290 for Steamworks identity; native VR launch
classification needs publisher Steamworks metadata. The locally testable
theater/dashboard and app-key options, with their limitations, are in
[STEAM-FRAME-LAUNCH-PRESENTATION.md](STEAM-FRAME-LAUNCH-PRESENTATION.md).
The complete immersive NPC work through Build 583 and the Steam Frame changes
through dev Build 559 now share the sole `dev` integration branch. Future NPC and
Frame work belongs on `dev`. The NPC presentation remains a two-client hardware
candidate, not a headset-confirmed result: see [TOWN-MP-583.md](TOWN-MP-583.md).
Town cloth interaction was retired in Build 582 pending a reliable runtime
design; the authored cloth remains static. The merchant's visible hand-to-belly
gap remains open. The first Frame run reached a scenario, but the native exit
after increasing eye resolution is not root-caused; see
[STEAM-FRAME-FIRST-HARDWARE.md](STEAM-FRAME-FIRST-HARDWARE.md). The original Frame
controller model and render foveation also remain unimplemented; feasibility is
recorded in [STEAM-FRAME.md](STEAM-FRAME.md) and
[STEAM-FRAME-FOVEATION.md](STEAM-FRAME-FOVEATION.md).
The second Frame run found little frame-time improvement below eye scale 1.00;
the maintainer reports unacceptable image quality there. Build 585 removes
source-proven CPU work and adds hand-step attribution. See
[STEAM-FRAME-SECOND-HARDWARE.md](STEAM-FRAME-SECOND-HARDWARE.md).
The Build 585 logs confirm substantial main-thread cost on the map and in a
scenario even at the `Fastest` quality preset; its scenario logic median alone
exceeds the 72 Hz frame budget. The later screenshots show about 17 FPS for
both Steam Frame overlay counters on the map, and the late logs put about
40 ms/frame in main-thread logic. The recording switch is absent on this
headset, so the two snapshots do not isolate GPU busy time. See
[STEAM-FRAME-THIRD-HARDWARE.md](STEAM-FRAME-THIRD-HARDWARE.md) and
[STEAM-FRAME-FOURTH-HARDWARE.md](STEAM-FRAME-FOURTH-HARDWARE.md).
The Build 586 run at 1728x1728 per eye gives roughly the same late-map
59-62 ms/frame as Build 585 at 3408x3408, despite 74% fewer submitted pixels;
the measured map work remains predominantly main-thread logic. Keep 3408 fixed
for the Build 587 CPU optimization comparison. Build 587 reduces hidden merchant
page work and adds bounded Debug attribution of the remaining town, veil and
modal-conversion cost; its headset gain remains unmeasured. See
[STEAM-FRAME-FIFTH-HARDWARE.md](STEAM-FRAME-FIFTH-HARDWARE.md).
Build 587 was pushed separately so the maintainer can measure its CPU changes
at 3408 per eye. Build 588 adds a separate Frame launcher and opt-in gate;
the original Steam entry remains flat only after the Frame helper creates its
marker. Steam Game Mode argument forwarding and prelaunch VR-settings visibility
remain hardware checks. See [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).
The Build 592 run at 3408 per eye shows a 2.65 s merchant catalog build,
roughly 6 ms/frame of recurring merchant work on the map, and 90–103 ms
scenario wall rescans. Menu and scenario also have long stalls not attributable
to named mod scopes; the XR GPU counter is not usable as busy time. See
[STEAM-FRAME-SIXTH-HARDWARE.md](STEAM-FRAME-SIXTH-HARDWARE.md).
Build 593 uses separate fresh Frame mod defaults while retaining existing
per-player values and the native game's saved graphics quality. The Frame
setup suggests 3408 pixels per eye via Valve's game-root `vrpreferences.json`;
SteamVR's user override remains authoritative. The persistent merchant
catalog now avoids repeated quadratic row searches. The enchantress's hidden
native card list no longer reparents its 549-element pool, although the game's
controller and callbacks still initialize those widgets. The selected map hand
prepares the same card clones it later reveals during the loading phase;
activation and final fitting keep their original order. Additional Debug
scopes isolate the remaining priestess/enchantress entry and fan reveal costs.
The Build 593 headset run confirms 10/10 then 9/9 map card fronts prewarmed
before reveal, but map hand activation/mip work, merchant catalog refresh and
first NPC approaches still produce interactive hitches; scenario pacing remains
roughly 41–52 ms median. Its screenshot shows the native flat gamepad popup and
desktop UI in SteamVR's application panel. The intended one-second wall setting
is not effective in this trace (live rescan 4.00 s; visibility evaluation 0.250 s).
See [STEAM-FRAME-DEFAULTS.md](STEAM-FRAME-DEFAULTS.md) and
[STEAM-FRAME-SEVENTH-HARDWARE.md](STEAM-FRAME-SEVENTH-HARDWARE.md).
The Build 594 Debug trace confirms that changing "Verdeckung prüfen alle" to
about 1 s saved the value but never altered the 0.25 s effective cadence;
the option omitted its actual BepInEx range. Build 595 makes the displayed,
saved and effective range agree. The same trace retains 42 ms scenario medians,
recurring merchant CPU work and interactive NPC/fan/wall hitches. See
[STEAM-FRAME-EIGHTH-HARDWARE.md](STEAM-FRAME-EIGHTH-HARDWARE.md) for measured
priorities and explicitly optional Frame compromises.
Build 596 moves avoidable presentation work out of interactive frames on all
platforms: dormant windows, merchant cold pages, parked card mip cache and
inactive enhancement-slot wrappers. Remote public map fans now pin class art
before their first face; scenario secrecy is unchanged. Pure wall provenance
checks are reused within one atomic commit, and expensive forensic scene
censuses default off everywhere. A capped Debug-only phase probe helps isolate
remaining native scenario CPU. Fresh Frame graphics defaults already use
MSAA 0, a smaller texture-streaming floor and slower wall rescans; existing
settings are retained, and no further resolution/legibility reduction is
applied without a matched hardware result. See
[STEAM-FRAME-NINTH-HARDWARE.md](STEAM-FRAME-NINTH-HARDWARE.md).
The Build 596 Frame run found a 487.82 ms first priestess visit (139.62 ms
inside the book-inscription geometry), a failed first enchantress entry after
inactive native slot-pool warmup, and a temple blessing visual that outlived
the native donation callback. Steady map/town logic was still roughly
36–42 ms/frame; the merchant cabinet continued spending 4.35–4.86 ms/frame
on cards. Some 0.35 s window appearances rendered in only one to four frames.
Build 597 removes the unsafe native pool warmup, indexes original temple page
triangles, allows the blessing visuals to finish before the bowl-cover pose,
skips the fully hidden merchant cassette page, and bounds window animation
progress per rendered frame. Re-enabling immersive NPCs displays the loading
indicator through resident and public-stock preparation. These changes are
source-level changes whose overall frame-time effect was not confirmed by the
subsequent headset run. See
[STEAM-FRAME-TENTH-HARDWARE.md](STEAM-FRAME-TENTH-HARDWARE.md).
The Build 597 run with a large immediately revealed scenario reached 12 Hz and
showed 282–385 ms atomic wall-table commits, recurring water/material scans,
and 51–62 ms average map frames. Build 598 removes quadratic wall refresh
membership checks, makes unchanged water-tile discovery event-driven, spreads
the complete material watchdog pass over bounded frames, and avoids redundant
hidden icon/cabinet and unchanged NPC setters on the 3D map. Remaining wall
commit phases and native/runtime frame cost are still substantial; hardware
improvement is unverified. See
[STEAM-FRAME-ELEVENTH-HARDWARE.md](STEAM-FRAME-ELEVENTH-HARDWARE.md).
Build 589 addresses the Build 587 town multiplayer report: host self-grant
timeout, original atlas capture, public cabinet input, town handoff focus and
map character selection. Correct matching Build 587 peer logs then exposed a
repeated remote rack crash and independent handoff gates; Build 590 repairs
those source paths. The offered-card world pose and one original-template
structure mismatch remain headset-open. See [TOWN-MP-590.md](TOWN-MP-590.md)
and [TOWN-MP-589.md](TOWN-MP-589.md).
Build 591 retains the selected owned character through native NPC mode changes
and adds a default-hidden +100 gold test action. The initial conversion of the
enchantress's 549-element original inventory is cheaper, but the measured
110–148 ms Visit hitch is **not yet proven resolved**; new scopes separate
native opening and VR conversion for the next Debug hardware run. See
[TOWN-591.md](TOWN-591.md).

The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which planning records are current or historical).

---

## 1. Position

- **dev / 1.1.0 / ModBuild 600 (configurable Frame candidate):** reduce verified
  standalone floor-grass render submission by a stable user-selected density;
  remove repeated shared wall/material and unchanged light work with explicit
  bypass settings. Frame seeds defaults; PC can reproduce them, and Frame can
  disable them. The former forced/hidden Frame mirror has a live setting again.
  Debug census/probes cover the real loaded scenario with unchanged old-log
  parser support. This is source-proven implementation, not hardware acceptance.

- **dev / 1.1.0 / ModBuild 599 (large-scene attribution):** separate the
  native game loop, Unity render submissions and actual GPU time before
  changing procedurally revealed scene geometry. The Build 598 test contains
  no 3D-map run; resolution-changing windows are not valid A/B comparisons.
  Debug probes and the log report are bounded and read-only. The hardware
  bottleneck and a playable Frame rate remain open.
- **dev / 1.1.0 / ModBuild 598 (large-scene Frame candidate):** remove quadratic
  wall-refresh membership checks, use placement events for unchanged water tiles,
  bound material-watchdog work per frame, and reduce redundant hidden-icon,
  merchant-cabinet and NPC work on the 3D map. The headset gain and remaining
  atomic wall phases require a matched hardware test.
- **dev / 1.1.0 / ModBuild 596 (interactive CPU candidate):** global lossless
  scheduling and prewarm target the measured merchant, window, map-hand and
  enchantress costs. Original 2D/3D map windows keep their native paths;
  multiplayer map fronts and remote privacy gates remain intact. Frame-only
  quality compromises remain the previously chosen fresh defaults. The wall
  commit still contains necessary atomic scene work; scenario residual CPU is
  instrumented, not yet optimized away. Headset frame-time improvements and
  1:1 visual behavior need a matched local/remote test. See
  [STEAM-FRAME-NINTH-HARDWARE.md](STEAM-FRAME-NINTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 595 (Frame options correction):** the wall occlusion
  evaluation control is bounded to its already-enforced 0..0.25 s range in
  BepInEx and VR Options. Persisted values around 1 s now display their actual
  effective value instead of implying the runtime changed. The 4.00 s wall
  table rebuild is a different setting and remains unchanged. Build 594
  performance findings and next candidates are documented in
  [STEAM-FRAME-EIGHTH-HARDWARE.md](STEAM-FRAME-EIGHTH-HARDWARE.md). Headset
  smoothness remains open.

- **dev / 1.1.0 / ModBuild 594 (Frame hardware candidate):** restore the
  enchantress's original enhancement-point heading above its physical book
  after the discarded flat list is veiled. The post-load Character UI layer
  sweep now indexes captured transforms by identity rather than searching an
  ordered ledger for each of 2660–3050 newly pooled objects. The persistent
  merchant catalog refreshes warm/visible rows during its recurring census;
  cold rows are refreshed before their first exposed frame, including a page
  or category change. Unchanged physical cards avoid redundant hierarchy,
  camera and renderer reads. Frame VR forces the existing discarded-desktop
  camera sink even when a saved PC mirror preference is off, and suppresses
  the native flat-only gamepad connection popup. PC and flat launches keep
  their settings and native behavior. Debug logs now identify actual edits
  to the three wall-cadence controls and the effective live rates. Build 593
  traces show 4.00 s rescan / 0.250 s evaluation throughout the scenario,
  but no setting-edit value is logged there; the owner's intended 1 s cannot
  be attributed to a particular control from that run. Full headset speed,
  enchantress points, and SteamVR theater presentation remain to be checked.
  See [STEAM-FRAME-SEVENTH-HARDWARE.md](STEAM-FRAME-SEVENTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 593 (Frame hardware candidate):** fresh standalone
  mod defaults mirror the Build 592 test, preserving existing BepInEx and
  native game settings; the Frame installer suggests 3408 pixels per eye through
  `vrpreferences.json` without replacing an existing SteamVR choice. The
  merchant catalog's row reconciliation and item-count lookup are linear.
  The invisible native enhancement list avoids a full-subtree reparent on
  entry while its game controllers stay active. The selected map hand prepares
  original face clones during loading and retains a synchronous early-open
  fallback. Bounded Debug attribution separates residual NPC and fan work.
  SteamVR dashboard focus and its in-game gray original-AppID panel remain
  unresolved; no global theater setting was changed. See
  [STEAM-FRAME-SIXTH-HARDWARE.md](STEAM-FRAME-SIXTH-HARDWARE.md) and
  [STEAM-FRAME-LAUNCH-PRESENTATION.md](STEAM-FRAME-LAUNCH-PRESENTATION.md).

- **dev / 1.1.0 / ModBuild 592 (archive layout candidate):** root-level
  release clutter is removed without changing the plugin/patcher install paths.
  The Frame desktop starter is nested under the plugin so the published 1.0.8
  updater accepts the new ZIP. Setup and successful updates retire only the six
  owned legacy root files. The Frame
  installer still needs a headset check, while synthetic Steam setup, archive
  and self-update tests cover the paths and allowlist. No gameplay, wire format
  or asset-bundle content changed. A packaging-only follow-up normalizes Windows
  ZIP path separators before applying Unix launcher modes, after a Windows-built
  archive exposed flattened launcher filenames. See
  [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).

- **dev / 1.1.0 / ModBuild 591 (hardware candidate):** scoped native map-option
  transitions no longer clear the locally owned selected portrait and force a
  visible reselect. The default-hidden Cheats page can add 100 gold to the
  selected map character or shared Guildmaster purse through native rules and
  save. A measured quadratic initial layer-restore search was removed; residual
  enchantress entry time remains a headset question, now split into native
  opening and original-widget conversion. Validation: 14/14 source gates;
  79/80 passing in the full runtime run and the sole failed harness repaired
  and rerun successfully (all 80 suites have passing evidence); 286,609 wire
  assertions, strict Release 0/0, bundle/surface and EN/DE docs passed. The
  one-shot guard itself did not reach compiled-form comparison after that
  fixture failure. See [TOWN-591.md](TOWN-591.md).

- **dev / 1.1.0 / ModBuild 590 (corrected peer-log follow-up):** destroyed
  remote town-service modules now rebuild instead of aborting `ApplyPending`.
  Enchantress attention and usable card cue agree in the merchant overlap;
  visiting the priestess after an idle Merchant/Enchantress destination no
  longer requires a purse that is not yet available. A guarded template
  mismatch now reports its actual address and structural difference. The
  original Build 587 peer trace was supplied after Build 589 and is analyzed
  in [TOWN-MP-590.md](TOWN-MP-590.md). Integrated validation passed 14/14
  source checks, 80/80 runtime suites, 286609 wire assertions, strict Release
  build and EN/DE docs. Headset parity remains open.

- **dev / 1.1.0 / ModBuild 589 (town multiplayer repair candidate):** the
  merchant remains immersive across transaction failures; valid host grants
  no longer expire as unanswered. Original item art/price atlas capture is
  resolved by verified native descriptors, and public cabinet categories can
  be used without an assigned character or a free transaction lease. Remote
  merchant decision facing interpolates over its sample period. Map ownership,
  enchantress handoff and temple purse focus no longer depend on stale
  selection or unrelated gates. The integrated gate passed 14 source checks,
  80 runtime suites, 286609 wire assertions, the strict Release build and
  EN/DE docs validation. The headset result remains open. See
  [TOWN-MP-589.md](TOWN-MP-589.md).

- **dev / 1.1.0 / ModBuild 588 (Frame launcher candidate):** a Frame-only
  install helper creates a local `GloomhavenVR` Steam shortcut with the existing
  mod logo/icon and a marker requiring the exact `--gloomhavenvr` launch
  argument. Both boot stages refuse VR before touching XR or gameplay when the
  original Steam entry omits that argument. The shortcut forwards to original
  AppID 780290 to preserve its Proton prefix and saves; Windows PC installs
  have no marker and retain their established startup. The separate library
  entry does not alter original Steamworks VR metadata. Integrated validation
  passed 14 source checks, all 80 runtime suites, 286609 wire assertions,
  strict Release build, release-package layout and EN/DE documentation checks.
  The compiled-form difference against Build 587 is the opt-in gate plus
  expected build constants. Steam Game Mode argument forwarding and prelaunch
  app settings are hardware-open. See
  [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).

- **dev / 1.1.0 / ModBuild 587 (Frame CPU candidate):** hidden merchant pages
  defer their native price and body-material mirrors until the first exposed
  frame, before local render and multiplayer publication. Stable merchant
  observer election no longer repeats body-visibility checks. Hidden-window
  veil discovery remains complete every frame; one impossible hierarchy test
  is removed. Bounded Debug timing splits town visits, public catalog,
  observer state, veil discovery/reassert and slow modal-conversion stages.
  No UI timing, gameplay, wire or asset change. The integrated 14 source
  checkers, all 80 runtime suites, strict Release build and EN/DE docs check
  passed. A compiled-form comparison against Build 586 contains only the
  reviewed town, veil and modal changes plus the build/branch constants.
  Headset performance remains unmeasured. See
  [STEAM-FRAME-FIFTH-HARDWARE.md](STEAM-FRAME-FIFTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 586 (Frame CPU candidate):** reuse the wall-cache
  material snapshot, avoid repeated hidden-veil component lookups, apply town
  catalog visibility once per frame, and skip unchanged panel-capture property
  writes. These are source-proven reductions on measured hot paths, not a
  measured headset speedup. No wire or asset change. The Build 585 map
  screenshots show about 17 FPS with severe frame-time peaks, and the log
  attributes about 40 ms/frame to main-thread logic in matching late windows.
  See [STEAM-FRAME-FOURTH-HARDWARE.md](STEAM-FRAME-FOURTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 585 (Frame CPU candidate):** reduce redundant
  canvas-flatten, town-resident/catalog/token and wall-cache work without
  changing visual cadence or interaction order. Split the hand timing by pose
  and interactor to identify its next bottleneck. Build 584 hardware evidence
  shows similar scenario frame p50 at eye scale 0.80 and 1.00 despite lower
  picture quality at 0.80; Valve GPU time and Build 585 headset speedup remain
  unmeasured. See [STEAM-FRAME-SECOND-HARDWARE.md](STEAM-FRAME-SECOND-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 584 (hardware candidate):** merge all NPC work through
  Build 583 with dev's Build 559 Frame changes. The player can test both on one
  build, and all future development integrates into `dev`. Both town bundles
  must accompany the DLL. The native Frame resolution exit and the NPC headset
  presentation remain open hardware checks. Integration validation passed
  14 source checks, 80 runtime suites, 286600 wire assertions, strict Release
  build and a complete package containing both town bundles. The compiled-form
  comparison against pre-NPC dev reports the expected new town types.

- **Integrated dev / 1.0.9 / ModBuild 559:** late additive main-menu loading
  re-arms bounded VR Options discovery; the eye-reach scene sweep is Debug-only
  after a 187.3 ms Frame measurement. Distinct live eye-resolution requests log
  before XR texture reallocation. Bilingual Frame installation is documented.
  See [STEAM-FRAME-FIRST-HARDWARE.md](STEAM-FRAME-FIRST-HARDWARE.md).

- **Integrated dev / 1.0.9 / ModBuild 558:** the tutorial distinguishes an
  explicitly identified Frame from SteamVR Touch emulation. The real controller
  model and render foveation are still future work.

- **NPC feature / 1.1.0 / ModBuild 583 (hardware candidate):** merchant
  original-card capture accepts observed shader-property counts and resolves
  the small texture identity collisions recorded on both peers. The shared
  cabinet remains public, while each visitor's held item and item fan may be
  mirrored independently of the stand's elected author. Pickup speech works
  through the merchant's gaze range. The enchantress's native handoff range now
  agrees with her gaze range; her offered hand follows visitor attention even
  before a cue is available. Only the three permanent resident stands remain;
  visiting a second NPC cannot spawn a fourth table. The current hardware
  evidence is build 582, so these build-583 outcomes still need a two-client
  headset comparison. See [TOWN-MP-583.md](TOWN-MP-583.md).

- **NPC feature / 1.1.0 / ModBuild 582 (hardware candidate):** cloth meshes remain
  static while their solver, hand interaction and publication/replay are removed;
  historic TLV90 decoding remains for compatibility. Merchant gaze greetings now
  use a voice channel independent of other residents. Every temple visitor can
  take and display their own purse; only valid native donation may place it in
  the bowl. The original immersive setting is a default-on Environment toggle
  that disappears with all NPC-specific audio options under the 2D map. Build
  580 logs do not verify these new headset outcomes.

- **NPC feature / 1.1.0 / ModBuild 581 (hardware candidate):** merchant speech
  follows the face's visitor target ahead of delayed coin-hand attention;
  enchantress approach switches idle resident destinations and resolves the
  narrow temple overlap; active transparent story/close targets once again
  receive laser input. The last two fixes address a scenario progression
  deadlock and a card handoff deadlock observed in Build 580. Independent
  per-resident host grants allow simultaneous visits to different NPCs while
  serializing offers at the same one. Automated checks
  verify the intended gates; headset behavior remains to be confirmed. See
  [TOWN-581.md](TOWN-581.md). The merchant hand/coat asset remains open.

- **NPC feature / 1.1.0 / ModBuild 580 (partial hardware candidate):** the elected
  face author greets a visitor when the merchant first turns to look at them,
  including consistent behavior after a multiplayer author handover. The
  priestess's build-579 staggered arm clocks have been removed in favor of a
  continuous prayer release and return. Native town cloth stays visibly
  deformed near a tracked hand and protects its sparse triangle interiors
  against hand passage; local/peer identity changes, withdrawal and station
  visibility reset its contact state. The merchant's attended hands still hover
  above the coat: the imported-skin pose candidate was rejected after a full
  motion scan found visible sleeve/coat intersections. Source and executable
  Unity evidence, together with remaining headset limits, are in
  [TOWN-580.md](TOWN-580.md).

- **NPC feature / 1.1.0 / ModBuild 579 (partial hardware candidate):** a
  visitor who enters the enchantress's area while another native town service
  is active no longer loses the only approach event. Local and remote cloth
  use each rendered hand's wrist and index-tip anchors, with contact across
  triangle interiors. The priestess releases prayer with separate arm phases;
  portable motion tests cover the intermediate visit. The merchant hands
  still visibly hover above the belly in build-578 hardware. Attempts to
  close that gap caused real skin intersections and were rejected; no merchant
  pose correction is claimed in this build. Successful generated Town suite
  runs now retain one marked result per suite. See [TOWN-579.md](TOWN-579.md).

- **NPC feature / 1.1.0 / ModBuild 578 (hardware candidate):** the offered
  enchantment card itself stops the laser while only original enhancement
  areas respond to selection. Its pulsing aura retains full side bounds.
  The priestess's prayer-to-neutral arm motion and the merchant's resting
  hands were retested against the imported skins and rendered at
  intermediate frames. Build-578 hardware subsequently disproved the claim
  that the merchant's hands visibly touch the belly. Dev ModBuild 557 contributes native character-creator
  fit and figure-proportional overhead bars with working wall visibility.
  Evidence and headset limits are in [TOWN-578.md](TOWN-578.md).

- **Integrated dev / 1.0.9 / ModBuild 557:** character creation joins the
  original character-screen fit; overhead bars honor the wall-visibility
  setting and, by default, scale proportionally with figures. The old zoom
  clamp remains a selectable setting. These source-backed corrections await
  headset confirmation; see [BARS-CREATOR-557.md](BARS-CREATOR-557.md).

- **NPC feature / 1.1.0 / ModBuild 577 (hardware candidate):** merchant offers
  keep a native confirmation throughout inspection, rebind lost controls and
  return cards when no native prompt can open. The offered enchantress card no
  longer blocks the laser from its own original enhancement areas, while all
  other physical occluders remain in force; its native ring stays round under
  rotated, nonuniform transforms. The priestess's entry blend avoids the prior
  elbow detour. The enchantress's cloth starts clear of its furniture root and
  holds smooth physical contact. Full-install art bundle and source/visual
  evidence are recorded in [TOWN-577.md](TOWN-577.md). Headset confirmation
  remains open.

- **NPC feature / 1.1.0 / ModBuild 576 (hardware candidate):** native saved
  headquarters unlocks and first-map tutorial gates determine whether each
  complete resident and stand exists. The immersive campaign's flat-only
  merchant onboarding steps resolve via native tutorial callbacks; other modes
  and setting-OFF retain their own flow. Merchant cassette/cards sit within the
  cabinet, and sold-out stamps clear when lifted. The enchantress shows a
  persistent neutral offer locator while native input initializes, fits the
  original aura and selectable areas to the physical card, and returns to the
  original usable VR window after a prolonged offer stall. Normal English
  replies and whispered spells now share one voice. Evidence and remaining
  headset checks are in [TOWN-576.md](TOWN-576.md).

- **NPC feature / 1.1.0 / ModBuild 575 (hardware candidate):** the merchant's
  Cancel click immediately returns the offered card; stock and owned cards can
  replace one another with release feedback while cabinet controls remain usable.
  Cabinet stock sits deeper, sold-out cards display a localized marker, and
  unsuccessful stock inspection has contextual shared speech. The enchantress's
  original aura spans the physical card, original enhancement areas accept a
  physical grip poke, and the card no longer responds to laser pickup. Incidental
  speech is less frequent. Full-quality town art and voice assets ship in two
  separately loaded bundles. The priestess shoulder and cloth motion receive a
  further rendered/solver review. Evidence and headset checks are in
  [TOWN-575.md](TOWN-575.md).

- **NPC feature / 1.1.0 / ModBuild 574 (hardware candidate):** immediate merchant
  buy/sell reuse preserves the native confirmation buttons. The actual Unity Cloth
  solver initializes before hand contact. Imported-skin tests now scan every animated
  priestess/merchant arm frame, correcting the crossing and thumb penetration missed
  by static markers. Unconnected cabinet and enchantress stand geometry is removed.
  Original enchantment-area buttons take laser selection over the offered card's
  own reclaim collider, and the native full-card effect covers the card instead of
  collapsing into a strip. Five contextual post-offer lines replace stale card
  invitations and follow the existing shared speech channel. Evidence and headset
  checks are in [TOWN-574.md](TOWN-574.md).

- **NPC feature / 1.1.0 / ModBuild 573 (hardware candidate):** merchant confirmations
  recover after a native window closes without updating its active wrapper; stock-to-owned
  replacement retains its card through transient tab changes. The red cabinet side appendage
  is gone and physical controls align with the imported sculpt. The priestess's shoulder and
  bowl path are corrected at the actual rig. The enchantress shows one card under the game's
  original selectable enhancement areas and speaks on native visits or accepted offers.
  Evidence and headset checks are in [TOWN-573.md](TOWN-573.md).

- **NPC feature / 1.1.0 / ModBuild 572 (hardware candidate):** the merchant's side cloth
  responds to finger contact and shares its movement with observers. Cabinet legs, braces,
  category controls and lantern mount align with the rebuilt shell. The priestess keeps her
  arms uncrossed while covering the bowl and blends into and out of that pose. The enchantress
  previews an opening handoff on first approach; valid merchant, enchantress and temple offers
  give stronger visual and bounded haptic feedback near their release volumes. Town art preloads
  asynchronously while the menu is open, avoiding the synchronous first-prefab bundle load;
  the remaining station construction cost awaits headset measurement. Source-grounded
  evidence and headset checks are in [TOWN-572.md](TOWN-572.md).

- **NPC feature / 1.1.0 / ModBuild 571 (hardware candidate):** a brief real cloth contact
  preserves the solver snapshot from approach so its first visible displacement is no longer
  re-zeroed. The priestess no longer speaks the unavailable response immediately after a
  committed donation; she performs a synchronized blessing and blends through the corrected
  prayer, attention and cover poses. The enchantress can finish opening after a brief native
  block, accepts either physical release, and exposes her original card-dependent options only
  while holding an offered card. The merchant cabinet is re-authored as a carved PBR shell with
  matching physical controls and aligned interaction anchors. Evidence and headset checks are in
  [TOWN-571.md](TOWN-571.md).

- **NPC feature / 1.1.0 / ModBuild 570 (hardware candidate):** merchant item widgets now leave
  the physical fan before their host is destroyed and restore every native face transform on
  creation, reclaim and maintenance. Real cloth contact presents the native solver immediately;
  only release retains a smooth recovery. An available priestess lets her arms hang beside the
  robe, while a known unavailable visit moves directly from prayer to the synchronized covered-bowl
  pose. Merchant and enchantress lines use the accepted high-quality speech renderer; casting uses
  five quiet invented incantations with five deterministic restrained effects. The complete native
  merchant lantern hook meets the cabinet ring. Evidence and the headset checklist are in
  [TOWN-570.md](TOWN-570.md).

- **NPC feature / 1.1.0 / ModBuild 569 (hardware candidate):** the merchant now claims and
  restores detached native child windows before generic modal conversion, keeping the item list
  behind the physical resident hidden without bypassing native transaction callbacks. Reclaimed
  item cards release stale renderer veils and independently restore fan parent, position, rotation
  and scale across repeated grab/release, close/reopen and replacement/cancel paths. Cloth contact
  follows the current simulated sheet rather than its former rest location. Production-bundle
  renders verify revised priestess shoulder/elbow anatomy and merchant thumb clearance. Merchant
  buy and sell prompts use their own synchronized voice families; the unavailable priestess has
  five synchronized explanations. At the native point of no return every resident smoothly returns
  to neutral, drops interaction and immediately silences active and queued speech for all peers.
  Evidence and the headset checklist are in [TOWN-569.md](TOWN-569.md).

- **NPC feature / 1.1.0 / ModBuild 568 (hardware candidate):** the permanent temple
  resident alone owns the blessing particle system, whose only live trigger is a later committed
  donation revision in the same owner/session. Close/reopen and hydration preserve their baseline,
  and smaller additive motes originate in the bowl. Priestess cover availability now blends out
  continuously rather than snapping when the temporary interaction record disappears; imported-rig
  tests measure every 90 Hz intermediate hand and elbow frame. Immersive service controllers are
  claimed before generic modal conversion, closed confirmations stay masked through render
  retirement, and a renderer reparented out of a hidden window is released immediately. This removes
  the filmed full native confirmation panel, its measured conversion spike and the stale veil that
  made returned item fronts grey. The merchant lantern is restored to the cabinet's exterior bracket
  with complete side-wall clearance. Evidence and the headset checklist are in
  [TOWN-568.md](TOWN-568.md).

- **NPC feature / 1.1.0 / ModBuild 567 (hardware candidate):** the recurring spatial
  open/close sound was the flat equipment-toggle clip reused as invented resident cloth foley on
  approach/departure attention thresholds; that path is removed while coin, spell and voice cues
  remain. Debug-only bounded traces identify any native town audio request. The purse appears only
  through the ordinary card-fan wrist gesture, including when donation is unavailable, while its
  interaction stays disabled. A seeded particle system replaces the polygon blessing. The actual
  production cloth class now measures capsule-to-sheet contact and preserves its visual zero, with
  measured contact response and complete recovery. Imported priestess shoulder origins, hip pose,
  palms-down bowl cover, cover exit and candle clearance were revalidated against the real bundle.
  Evidence and the headset checklist are in [TOWN-567.md](TOWN-567.md).

- **NPC feature / 1.1.0 / ModBuild 566 (hardware candidate):** automatic resident
  departure keeps the native mode transition but omits synthetic pointer audio. Temple entry
  preserves the exact selected party slot and no longer rebuilds an invisible primary workspace
  with two Cloth solvers; measured repeat entry is sub-millisecond in the Unity harness. The purse
  remains visible as status information independently of transaction eligibility. Actual imported
  skins now use accepted merchant/priestess hip silhouettes and a continuous bowl-cover return.
  Scale-correct gravity, bounded freedom, continuous supports and episode-relative rendering keep
  the real cloth responsive without table inversion, re-entry pops or solver reconstruction.
  Complete bounds separate all priestess candles from lanterns, book and bowl. Evidence and the
  headset checklist are in [TOWN-566.md](TOWN-566.md).

- **NPC feature / 1.1.0 / ModBuild 565 (hardware candidate):** repeated town-service
  visits resolve local input through the current wire generation, keeping the temple purse
  and original book visible after visiting another resident. Imported-model pose checks put
  merchant and priestess hands at their waists and turn the unavailable temple palms down
  through a continuous blend. The real 25x13 runner surface is reconstructed independently
  of FBX vertex order and simulated without inherited 19,800x scale, with per-column table
  support and real index-tip collision. Native flat window show/hide cues are suppressed from
  the first immersive call, priestess speech has measured loudness compensation, and the
  enchantress lamp uses its visible renderer footprint on the real workbench. Evidence and
  headset limits are in [TOWN-565.md](TOWN-565.md).

- **NPC feature / 1.1.0 / ModBuild 564 (hardware candidate):** each resident has
  one sticky, timeout-bounded multiplayer visitor lease. The elected visitor alone
  can interact and author the resident, while all peers receive the same station,
  pose, speech, offering and confirmation presentation. Temple eligibility and one
  blessing revision use additive inner town-service TLV91 without gameplay identity.
  The donation guide is blue and translucent, the real purse remains upright and
  snaps exactly into it, and an unavailable ritual hides the guide while the
  priestess covers the bowl. Merchant and priestess use reachable anatomical hip
  poses; merchant and enchantress card palms swap valid cards atomically. The
  enchantress practical is grounded to the workbench and the remaining immersive
  flat-service show sound is suppressed at its exact display edge. All fifteen
  priestess lines use the elderly `Wise_Woman` performance. Evidence and headset
  limits are in [TOWN-564.md](TOWN-564.md).

- **NPC feature / 1.1.0 / ModBuild 563 (hardware candidate):** native Unity Cloth
  replaces the rigid altar-runner spring and collides with the complete curved table,
  local/remote hands and heads without adding ray targets. Temple donations use the
  original confirmation continuation; every merchant-item return restores fan parent,
  art readiness, rotation and one palm size. Merchant stock uses the proven mip watcher.
  Automatic service fan-edge sounds are silent, the shared cabinet has physical foley,
  and local NPC speech/effects toggles are independent and default on. Voice regeneration
  replaces all fifteen priestess cues with one consistent close-miked voice and replaces the
  broken `merchant-sell-2` take. Evidence and headset limits are in
  [TOWN-563.md](TOWN-563.md).

- **NPC feature / 1.1.0 / ModBuild 562 (hardware candidate):** repeated temple
  visits rebuild the physical purse; item cards emerge only after their original
  front is ready and use canonical fan rotation. Merchant and priestess attention
  author elbows with lowered hands, coin work has no long neutral stop, cloth
  contact is stronger, and practical lights stay on their furniture. Automatic
  service entry is silent. Eleven resident cue families each contain five shared,
  non-repeating variants with linear spatial rolloff. Headset verification remains
  open; evidence and limits are in [TOWN-562.md](TOWN-562.md).

- **NPC feature / 1.1.0 / ModBuild 561 (hardware candidate):** the physical
  temple purse retains its original confirmation through native modal focus;
  page parchment, visitor hand poses and sleeve interiors address build-560
  screenshots. Returned original item cards settle upright. The merchant's
  offering pose comes from the elected resident author and speech age cannot
  rewind within a cue. The enchantress invites a visitor as her hand opens;
  new consistent merchant and priestess voices, bidirectional lip transitions
  and quiet, paced coin contact revise the audible presentation. These remain subject to headset
  verification; evidence and limits are in [TOWN-561.md](TOWN-561.md).

- **NPC feature / 1.1.0 / ModBuild 560 (hardware candidate):** quieter coin foley,
  English spatial resident lines and mouth curves, varied enchantress activity,
  merchant attention transition, visibly grounded cloth and cabinet details,
  guarded donation body target and ghost-purse bowl cue. One elected public
  merchant cabinet and buyer-owned native confirmation surfaces are mirrored
  to all players; remote visual controls remain inert. Presentation-only voice
  reactions are relayed from visitors to the elected resident author. Automated
  and hardware limits are recorded in [TOWN-560.md](TOWN-560.md).

- **NPC feature / 1.1.0 / ModBuild 559 (hardware candidate):** fixed-scale NPC
  foley, stable merchant/priestess proximity, native temple purse release
  diagnostics, curved blank book pages, continuous coin motion, reworked crank,
  contact-responsive shared altar cloth and inner sleeves. TLV79 adds an optional
  24-byte cloth tail without changing the older resident prefix; private visitor
  furniture carries equivalent runner controls in a bounded additive TLV90. Source and
  automated validation are described in [TOWN-559.md](TOWN-559.md); headset
  appearance and a completed donation remain unverified.

- **NPC feature / 1.1.0 / ModBuild 558 (hardware candidate):** the merchant stops
  coin work while attending to a player and uses coordinated, continuous hand/body
  movement; spatial foley has a practical interaction range. The oversized crank
  collider and the generic spent-item rotation no longer interfere with the
  cabinet or first inspection fan. Owned enhancement cards remain associated with
  native enhancement rows across refreshes, and the immersive enhancement screen
  is not considered active before its mask exists. The temple approach update now
  starts the purse path. The priestess's front/profile atlas no longer projects a
  photographed second ear or eye onto her lateral face; the fixed-detail rig and
  Windows town bundle were rebuilt. The rack mirror's ancestor-fade test now
  waits for its bounded publication cadence before asserting visibility, and
  offering mutations are checked before unrelated rack assertions.
  Automated checks cover source contracts and
  asset format, but the reported hardware interactions still require a headset test.

- **Released 1.0.8 / ModBuild 556:** `main` at tag `v1.0.8`; `dev` has advanced its
  project version to 1.0.9. The NPC branch incorporates that ancestry while keeping
  its own 1.1.0 feature version. Release validation and publication are complete;
  NPC hardware acceptance remains separate.

- **dev / 1.0.8 / ModBuild 556:** combat-log and control-board laser releases
  turn toward the owner using the same short animation as other local windows.
  The board's existing rig pose stream carries its intermediate turn to peers.
  No wire, asset or NPC change; headset motion still needs verification.
  See [REFACE-556.md](REFACE-556.md).

- **dev / 1.0.8 / ModBuild 555:** matching build 554 hardware logs show native story
  and reward completion succeeded, but the mod kept the disabled final page visible.
  Exact map/scenario story identity now ends retention on native close and prevents
  poll/conversion/visibility resurrection. Shared native opening histories also address
  queued story completion and post-quest reward synchronization. No NPC changes
  or release. Evidence and test sequence: [STORY-555.md](STORY-555.md).

- **dev / 1.0.8 / ModBuild 554:** hidden offline Cheats page gains a confirmed
  current-scenario victory action through the native result/reward flow, to test
  build 553's continuation fixes. Cheats remain disabled by default. No NPC content
  or release. Usage and limits: [CHEAT-554.md](CHEAT-554.md).

- **dev / 1.0.8 / ModBuild 553:** native level-up Continue, 3D-map confirmation
  ownership and missed-event/failure recovery, complete message queue callbacks,
  native popup cancellation and safe mandatory-window close admission. Source review
  covers reward/result chains and more than 30 window families. No matching third-party
  logs are available; the maintainer confirms the report concerns release 1.0.7,
  primarily Campaign. Current local/remote files belong to builds 551/500. No NPC code,
  asset bundle change or release is included. See [WINDOWS-553.md](WINDOWS-553.md).

- **NPC feature / 1.1.0 / ModBuild 552 (hardware candidate):** visible-surface laser
  targeting without resident proxy boxes; mage departure cleanup and independent
  Character UI; native per-card enhancement points beside the offered card; physical
  ability/item reclaim. Native priestess purse donations use the actual shared bowl,
  original authority and guarded confirmation. Original localized ink conforms to the
  book pages locally and remotely. Revised mirrored wrist motion, varied merchant
  contact timing and subtle original positional foley. Native black flame padding
  stays transparent. Full package required; environment bundle unchanged. See
  [research/TOWN-SERVICES-552.md](research/TOWN-SERVICES-552.md) for integration gates
  and hardware limits; automated success is not headset acceptance.

- **NPC feature / 1.1.0 / ModBuild 551 (hardware candidate):** callback-scoped
  palm regrabs and freestanding native confirmation; nearby held-card offer intent;
  complete original enchantment inventory on the stand and larger offered card;
  longer varied resident phrases without table bracing; actual articulated rolling
  shelves with a shared page indicator; original standing candles and opaque flame
  cores. Fix a destroyed-inscription shutdown exception. Full package required;
  source and hardware evidence are separated in
  [research/TOWN-SERVICES-551.md](research/TOWN-SERVICES-551.md). All 69 required
  suites are covered, including complete repeats of two repaired fixtures and final
  targeted changes; 14 source gates, 286,120 wire assertions and zero-warning strict
  Release pass. Actual asset review passes 951,242 assertions / nine controls;
  4,423 full-skin and 1,206 holder poses have no tested intersections. Matching town
  bundle: 96,164,115 bytes (`e56d6bd3…61da9`). Hardware results remain unverified.

- **NPC feature / 1.1.0 / ModBuild 550 (hardware candidate):** inert merchant laser/poke;
  shared ability/item hand-local tracking and owned-fan laser contact suppression;
  aimed vertical-stick cabinet paging with locomotion arbitration; upright animated
  actual-card palm handoffs retained through native confirmation, reclaimable and
  mirrored. Revised prayer, stance and coordinated body/arm retargeting address the
  549 hardware report. Fitted merchant worktop and actual native candles remove
  the reported torso/scroll intersections. Original environment bundle unchanged.
  See [research/TOWN-SERVICES-550.md](research/TOWN-SERVICES-550.md) for evidence,
  final validation and remaining hardware checks. All 69 local suites are covered
  with targeted complete repeats after two fixture repairs; 14 source gates, 286,103
  wire assertions, strict Release and final imported asset/motion checks pass.
  Full installation required: town bundle 96,187,501 bytes (`91dedb57…642492`).


- **NPC feature / 1.1.0 / ModBuild 549 (hardware candidate):** approved upright merchant
  cabinet with physical categories/crank and occluded animated page changes; original
  scenario item holding; complete owned-item fan near the merchant; palm-based buy/sell
  requests with explicit native confirmation. Public cabinet and private inspection
  lifetimes are independent, including late join and authority handoff. Stable owned
  practical lighting replaces renderer-dependent nearest-light changes. Revised arm
  skin/contact motion and front-semicircle placement preserve original room geometry.
  Final Windows town bundle: 96,167,323 bytes (`d5f413b9…675e5`); the environment
  bundle is unchanged. Fourteen source gates, all 69 local suites (four stale fixtures
  corrected with targeted negative-control repeats), 286,103 wire assertions and strict
  Release zero warnings/errors are covered. Combined assets pass 888,814 assertions /
  nine rendered controls; motion passes 268,749 / seventeen controls and 1,943 sampled
  full-skin poses without arm/torso or opposite-arm intersections. Exact package evidence
  and hardware limits: [research/TOWN-SERVICES-549.md](research/TOWN-SERVICES-549.md).


- **NPC feature / 1.1.0 / ModBuild 548 (hardware candidate):** restores original cellar
  and forest proportions, removes NPC distance LOD, replaces the oversized merchant
  table with a compact cabinet and physically turning racks, and revises portrait
  likeness, eye/lid geometry and prop-contact body motion. Four offline Kimodo-generated
  phrases use existing shared occupation clocks. Multiplayer rack clocks and membership
  use additive TLV85; live catalog IDs survive repeated page turns without exhaustion.
  A campaign city-cap null dereference found in build-547 logs is also fixed.
  The final Windows town bundle is 95,727,401 bytes (`afdd76b3…88c9`); the environment
  bundle remains byte-identical. Actual source-asset renders pass 2,044 assertions / nine
  visual negatives and final imported activity/contact tests pass 185,546 assertions /
  sixteen negatives. Final checks cover all 14 source and 66 runtime suites (one stale
  radius fixture was corrected and repeated), 286,090 wire assertions and a zero-warning
  strict Release. Bundle/surface checks pass; historical compiled-baseline differences
  remain. Exact package hashes and CRC evidence: `debug/town548-package-verification.json`.
  Headset quality and multiplayer frame timing require hardware confirmation.
  Evidence and checklist: [research/TOWN-SERVICES-548.md](research/TOWN-SERVICES-548.md).

- **dev / 1.0.7 / ModBuild 546**, based on the 1.0.6 runtime without NPC services:
  campaign city-event cap and native animation, original permanent map windows across
  2D/3D switches, and native map-button hints beside physical caps with synchronized
  multiplayer presentation. AoE uses short B/Y releases by default; both locomotion
  sticks stay available. A normal Comfort setting offers the legacy stick alternative,
  and English/German tutorial hints update to the selected binding even while open.
  Native targeting authority and shared encounter continuation remain in charge.
  Evidence, validation and hardware checklist: [MAP-546.md](MAP-546.md).
  The maintainer confirmed this map/AoE revision on hardware before releasing 1.0.7.
  At the time, dev remained the independent hotfix branch while NPC integration
  targeted 1.1.0; both lines merged into dev at Build 584.

- **Town hardware corrections / build 545 (hardware candidate):** build 544's eleven
  screenshots supersede earlier asset-quality assumptions. Physical complete merchant stock,
  native coin offerings, physical enhancement choices, original decoration and owner-authored
  multiplayer output are integrated. Final portrait-fitted faces, spherical eyes, closed
  costume joins and anatomical hands ship in the matching 100,501,555-byte Windows town bundle
  (`08ff8501…fe597e`). Actual asset validation passes 599 render assertions / six negatives; actual
  activity/contact validation passes 125088 / eleven negatives. All map environments share
  one scenery-checked station layout. Full local guard passes 14 source / 59 runtime suites and 260312 wire assertions; only
  the expected historical compiled-baseline differences remain. Strict Release has zero
  warnings/errors. The full matching ZIP is built and CRC/hash-verified. Dev CI status is
  checked at handoff; private evidence is `debug/town545-package-verification.json`.
  Close stereo appearance and hardware timing remain unverified; install the complete package
  on all VR peers. See [research/TOWN-SERVICES-545.md](research/TOWN-SERVICES-545.md).

- **Town hardware corrections / build 544 (new defects confirmed; superseded by 545 work):** the build-543 test exposed black eyes,
  merchant identity drift and open costume joins. Corrected Windows eye lighting, fitted
  heads, continuous neck/costume joins and shared work/attention animation are integrated.
  Full local guard passes 14 source / 51 runtime suites and 260310 wire assertions;
  strict Release has zero warnings/errors. Final assets pass 489 assertions / six visual
  negative controls, actual prefab faces 9862 assertions / three compiled negatives /
  nine anatomical corruption controls, and activities 92107 assertions / nine compiled
  negatives. The source-only CI subset passes 24031 assertions / five negatives.
  The full package contains the matching 98,325,951-byte Windows town bundle; the main
  bundle is unchanged. Install the full package on all VR peers. Headset appearance,
  close-range stereo and hardware frame timing remain unverified. See
  [research/TOWN-SERVICES-544.md](research/TOWN-SERVICES-544.md).

- **Town faces / build 543 (hardware defects confirmed; superseded by 544 work):** anatomical faces, separate eyes, shared
  head/eye tracking, frame-by-frame blink and subtle expression playback are integrated.
  Full local guard passes 50 runtime suites,
  258091 wire assertions and the real Unity face suite (2076 assertions / 16 negative
  controls); strict Release has zero warnings/errors. The voice audit finds original
  narration but no matching recordings for the three residents; no generated voices ship.
  Final asset rendering passes 469 assertions / six visual negative controls; production
  binding to the actual final prefabs passes 9862 assertions / three compiled negative
  controls plus nine deliberately corrupted jaw-weight cases. Root reviewed final neutral,
  blink, mouth and gaze-limit renders. The matching 99,211,283-byte Windows town bundle
  is required; install the full package. The main bundle is unchanged. Existing costume
  cut-edge imperfections and Windows eye-lighting failures were subsequently confirmed
  on hardware; the earlier automated checks did not establish a correct headset picture.
  See [research/TOWN-SERVICES-543.md](research/TOWN-SERVICES-543.md).

- **Town residents / build 542 hardware candidate:** all three NPCs remain on the map in immersive
  mode; direct NPC visits replace service map caps. Actual-floor placement, native decoration,
  practical lighting, held merchant inspection and additive shared resident authority are
  integrated. No purchase occurs on grip/release. Full source/runtime guard passes
  49 suites and 257025 wire assertions; expected old-baseline compiled differences remain.
  New neutral heads, original-game decoration and the matching 81.6 MB town bundle pass
  78 asset assertions and six visual negative controls. Fine facial mesh artifacts remain;
  facial animation topology and headset quality are not claimed complete. Install both
  bundles with the matching DLL. See [research/TOWN-SERVICES-542.md](research/TOWN-SERVICES-542.md).

- **Validation infrastructure (runtime remains build 541):** independent source/runtime
  suites now use bounded parallel execution with isolated logs and temporary outputs.
  CI distributes its runtime suites across four required shards while preserving exact-tree
  proof, main-only release publication and optional artifact limits. Measurement and coverage:
  [TEST-PARALLELISM.md](TEST-PARALLELISM.md).

- **Town services / build 541:** build-540 hardware logs reproduce merchant setup failure
  immediately after the Buy/Sell/All control conversions. The catalog dereferenced the
  gamepad-only Owned filter, absent from desktop merchant UI. Its handoff now follows the
  actual native control set. Repeated catalog tests cover both prefab variants and rollback.
  VR options expose an explicit immersive/original-window choice in a dedicated first town
  services section under Boards, backed by the existing default-on setting. Both asset
  bundles are unchanged from 540. This is a development correction, not a release; successful
  headset opening remains unverified. Evidence: [research/TOWN-SERVICES-541.md](research/TOWN-SERVICES-541.md).

- **Town services / build 540:** the first hardware test of 539 exposed invisible actors,
  black furniture, a native error dialog and the unsuitable floating merchant inventory.
  Corrected NPC LOD bounds and self-contained textured lighting ship in a new town bundle.
  Mirror-template preparation validates item provenance before touching a pooled card;
  the native item-ID-zero error path is avoided. The merchant now has six original item
  cards per page, original prices and buy/sell/filter/exit controls on its counter. Physical
  samples still select through native rows; purchases retain native confirmation.
  Reversible wrappers hide the obsolete list without disabling gameplay, and orphan-frame
  collection respects live service/error owners. Original item details/rule hints remain
  visible on the counter; additional visitors have separate full-size workspaces with
  owner-authored motion and materials. Temple/enchantress gain the asset and
  lifecycle corrections; their existing reading-surface interaction is not replaced by
  the merchant rack. Default-on settings and original-window rollback remain unchanged.
  Evidence, validation and hardware checklist: [research/TOWN-SERVICES-540.md](research/TOWN-SERVICES-540.md).
  Both bundles must be installed. This is an unreleased development build; corrected
  headset output remains unverified.

- **Town services / build 539:** VR options expose `WorldUI/ImmersiveTownServices`,
  enabled by default (maintainer clarification, 2026-09-21). Turning it off restores the three original service windows through
  the ordinary conversion path, including an already-open service, without changing native
  selection or invoking close/confirmation callbacks. Held samples are cancelled, original
  section parents and portraits restored. Enabled remote visitors remain visible regardless
  of the observer's local preference. Unity validation passes 617 assertions and 22 compiled
  negative controls. Hardware verification of live switching is pending.

- **Town services / build 538:** first immersive merchant, temple and enchantress variant.
  Three generated NPCs have body/finger rigs, authored greeting/idle animations, three mesh
  LODs and 4K textures. A separate `prebuilt/ghvr-town.bundle` keeps the existing asset bank
  unchanged. Native service sections become movable reading surfaces; gripping an original
  entry and placing its sample on the work tray selects through the original button.
  Native ownership, prices, restrictions, confirmations and continuations remain authoritative.
  Concurrent visitors share one NPC per service. Original visible widget output is transported
  to inert observer copies, including nested masks, card art and dynamic tooltip contents.
  The complete package requires **both** asset bundles; installing only the DLL is insufficient.
  Record and hardware checklist: [research/TOWN-SERVICES-FIRST-VARIANT.md](research/TOWN-SERVICES-FIRST-VARIANT.md).
  Source, Unity render and archive validation are recorded there; headset presentation remains
  unverified. This is a development handoff, not a release. Detailed facial animation and
  transaction-specific NPC hand choreography remain later polish.
  Original exports, generated sheets and paid mesh provenance remain separate in
  `.planning/debug/npc-references/`, `npc-modeling/` and `npc-meshes/` respectively.
  Seven FAL generation jobs were used, estimated USD 4.275; no additional paid generation
  was needed for runtime integration. Actual account billing was not independently audited.

- **Released 1.0.6 / ModBuild 537:** the maintainer confirmed the menu fix; the final
  hardware log audit found no release blocker. Main commit `59a5d884`, tag `v1.0.6`,
  release workflow `35525779327` succeeded. Published ZIP downloaded and verified.
  Automatic bookkeeping advanced dev to 1.0.7 at `ff59a14e`; no runtime build increment.

- **dev / 1.0.6 / ModBuild 537** removes VR-triggered native focus handoffs:
  opening settings must not shade otherwise usable menu entries. Native hidden callbacks
  are accepted in their actual order, and the row is silently cleared at closure rather
  than waiting one second. X -> immediate reopen and ordinary toggle closure share the
  same state; explicit reopen clears only the mod pane's pending same-frame close.
  Main-menu arbitration and independent scenario/map windows remain unchanged.
  The maintainer confirms build 536 fixed repeated opening; its new logs reproduce the
  stale selected row after X. Short-rest playback is unchanged and remains locally
  hardware-confirmed. Records: [OPTIONS-537.md](OPTIONS-537.md), [CLOSE-537.md](CLOSE-537.md).
  Options tests: 7,076 assertions / seven bindings / 19 negative controls; close lifecycle:
  1,796 assertions / six bindings / six negative controls. Full source/runtime guard and
  254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. Surfaces remain
  625 / 174 / 4,742; inventory 132 classes / 200 methods. Guard exit 1 is solely the
  expected old-baseline difference (101 changed, 78 added/removed, one order-only move).
  Direct compiled comparison with build 536 isolates the three intended menu types
  plus embedded build-number changes. Subsequently confirmed on the maintainer's headset
  and included in release 1.0.6, as recorded above.

- **dev / 1.0.6 / ModBuild 536** addresses repeated VR-options access. The initial
  Sep-20 logs are released build 534: its fourth opening within 60 seconds triggers
  CATCH-ALL FUSE, treating the registered mod menu as a cycling HUD banner. Registered
  mod menus are exempted from churn suppression; unknown HUD windows retain that guard.
  The mod-owned menu rows must stay visible, focused and pressable while their host is
  shown, and transient entry/injection failures must recover with bounded retry.
  Source/log records: [OPTIONS-536.md](OPTIONS-536.md), [BURN-536.md](BURN-536.md).
  **Short rest is hardware-confirmed resolved locally in the follow-up test.** The
  second_logs capture identifies build 535. At the previously failing 0.707-second
  renderer transition, grey/flow 1 and dissolve 0.646 survive unchanged while raw
  progress continues. One burn completes at 2.010 seconds before its pile flight.
  The maintainer reports no visible flash. No further burn change is made. Peer logs
  remain historical 500; new remote hardware confirmation is not available.
  Options implementation is integrated. Focused tests: 5,536 runtime assertions / four
  production bindings / 15 negative controls. Complete source/runtime guard and 254,565
  real-runtime wire assertions pass. Strict Release: zero warnings/errors. Bilingual docs,
  Actionlint, patch inventory and whitespace pass. Config/patch/log surfaces remain
  625 / 174 / 4,742; inventory remains 132 classes / 200 methods. Guard exit 1 is solely
  the expected compiled difference from baseline 080c505e9: 101 changed, 78 added/removed
  types and one order-only move. A separate comparison with the prior build-535 compiled
  output finds only the three intended menu types and build-number substitutions.
  Subsequent build-536 hardware confirms repeated opening works but exposes focus shading
  and delayed row clearing after X; build 537 addresses those. Short-rest confirmation stands.

- **dev / 1.0.6 / ModBuild 535** retains the spent appearance of a Lost card while its
  original native burn continues across temporary face inactivity. Build-534 Debug shows one
  complete ramp, not a replay, but its spent shader floor disappears around 0.697 seconds.
  The ordinary sampler still equated inactive hierarchy with stopped playback, contradicting
  build 534. It now follows the actual tracked iterator. A production-method regression
  reproduces the previous floor loss; real detach/recovery cleanup is no longer stubbed out.
  Native timing, recovery, flights, remote concealment and normal logging are unchanged.
  Local draw and owner publication use the same corrected sampler. Peer logs remain build 500;
  the precise hardware deactivation writer and headset outcome remain unverified.
  Record: [BURN-535.md](BURN-535.md). Focused replay: 683 runtime assertions / seven source
  bindings / 36 negative controls. Complete source/runtime guard and 254,565 real-runtime
  wire assertions pass; flight timing 818, remote burn sequencing 108 and burn layout 228
  assertions pass. Guard exit 1 is solely the expected compiled difference from baseline
  080c505e9: 99 changed, 78 added/removed types and one order-only project move. Config,
  patch and log surfaces remain 625 / 174 / 4,742, with no removals. Patch inventory remains
  132 classes / 200 methods. Strict Release passes with zero warnings/errors; bilingual
  docs, Actionlint and whitespace pass.

- **Released 1.0.5 / ModBuild 534**, main 377d26ec, tag v1.0.5, GitHub Latest.
  Full development CI, reused PR validation and main release workflow succeeded. Downloaded
  ZIP CRC, contents and SHA256 match the published asset. Dev was automatically advanced to
  1.0.6 at 9081a992 and includes the release ancestry. Record: [RELEASE-105.md](RELEASE-105.md).
  The maintainer confirms the Guildmaster fixes; short-rest flashing remains and was explicitly
  deferred for release. New local Debug logs are build 534; peer logs remain historical 500.
  No gameplay exception/deadlock was found. Bounded decorative coin-material load failures,
  native backend DNS errors and shutdown-only exceptions remain documented in the release audit.
  Postrelease burn investigation resumes on dev without changing the published release.

- **dev / 1.0.5 / ModBuild 534** addresses all three build-533 hardware findings.
  Debug shows the first short-rest burn exits synchronously on an inactive original, followed
  by a no-ramp settle and a later animated LostMode reset. Verified original full cards now use
  native disabled playback, retaining the first complete animation across that hierarchy edge.
  Cold Guildmaster MR hid the existing GH_Map_Table as enclosing sky; native map furniture is
  excluded before that heuristic, independently of camera seating/map-driver readiness. The
  whole native table is recognized before optional campaign-slab loading. No duplicate is built.
  Guildmaster action caps remain vertical; WorldMap/City form a separate centred pair to their
  right, fitted to native support and knife clearance. Campaign controls remain unchanged.
  Local logs are 533; peer logs remain historical 500. No fresh screenshots were supplied.
  Source/log causes are established; current headset/peer results are not yet verified.
  Records: [BURN-534.md](BURN-534.md), [GUILD-534.md](GUILD-534.md), [MR-534.md](MR-534.md).
  Focused checks: burn replay 672 assertions / seven bindings / 32 negative controls;
  Guildmaster room 10,338 assertions / 25 bindings / nine negative controls;
  MR scenery/sky 79 assertions / 26 bindings / six negative controls.
  Complete source/runtime guard and 254,565 real-runtime wire assertions pass. Existing
  flight timing 818, remote burn sequencing 108 and burn layout 228 assertions also pass.
  The guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  99 changed, 78 added/removed types and one order-only project move. Config/patch/log
  surfaces stay 625 / 174 / 4,742 without removals. Patch inventory is 132 classes / 200
  methods: the existing BurnCardTimeline patch gains the tested original-only prefix.
  Bilingual docs, Actionlint and whitespace pass; strict Release zero warnings/errors.

- **dev / 1.0.5 / ModBuild 533** fixes the clarified Spellweaver action-slot regression:
  the native action controller retains the first played card; recovering it from Lost to Hand
  previously made it eligible for the round slot again. Actual Hand membership now rejects
  that stale supplement, while Round/ExtraTurn cards and later legitimate selection still work.
  Both reported card flights worked; the later disappearance was the resurrected stale slot.
  Local logs are 532; remote logs remain historical 500. No independent remote static-pair
  reconstruction was found; owner-published slots inherit the corrected collection.
  The short-rest flash **remains unresolved**. The maintainer confirmed Debug was forgotten
  for this capture and enabled for the next test. Bounded opt-in diagnostics record
  native playback, reset and renderer/material transitions without adding normal-log streams.
  Records: [ROUND-533.md](ROUND-533.md), [BURN-533.md](BURN-533.md).
  Focused collector coverage: 50 runtime assertions across three production methods and six
  negative controls. Burn replay/diagnostics: 644 assertions, six bindings and 30 negative
  controls. Complete source/runtime guard and 254,565 real-runtime wire assertions pass;
  local flight timing 818, remote burn sequencing 108 and burn layout 228 assertions pass.
  The only guard exit-1 result is the expected compiled difference from baseline 080c505e9:
  99 changed, 77 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,742 with no removals; the additional marker is Debug-only BURN NATIVE TRACE.
  Patch inventory remains 132 classes / 199 methods. Strict Release zero warnings/errors;
  bilingual docs, Actionlint and whitespace pass. Headset results remain unverified.

- **dev / 1.0.5 / ModBuild 532** addresses the build-531 hardware report. MR backings
  now belong exclusively to UI: scenery underlays, fills and rims are retired, including
  the former unseen/preview routes. Native terrain/water and UI readability remain intact.
  Short-rest spent shader floors survive the native iterator's terminal step, which otherwise
  restores raw paint without writing a final frame. Pile flights take exclusive ownership of
  mod fade visibility and retire an obsolete vanish callback; native burn materials are preserved.
  The layout barrier now retains actual per-card artwork observations for release diagnostics.
  Local logs are 531; remote logs remain historical 500. Reviving Ether flights were launched
  (LogOutput 1278 and 2037); the logs do not establish whether the fade-handover defect caused
  those particular invisible flights. MR screenshot inspected; exact water renderer unknown.
  Source-proven fixes need headset confirmation, especially the reported missing flight.
  Records: [MR-532.md](MR-532.md), [BURN-532.md](BURN-532.md), [FLIGHT-532.md](FLIGHT-532.md).
  Integrated validation: all source/runtime stages and 254,565 real-runtime wire assertions
  pass; burn replay 633 / six bindings / 24 negative controls, flight timing 818 / 13 negative
  controls, burn layout 228 / 16 negative controls, MR scenery 54 / 26 bindings / five negative
  controls. Initial guard stopped at eight historical config-description strings classified as
  protected tokens. Restored those descriptions behind explicit inactive prefixes, then reran
  the affected MR checks, docs, surface census, strict Release and the unchanged remaining guard
  stages; unaffected runtime suites were not repeated. All checks pass. Compiled comparison
  retains the expected exit-1 difference from baseline 080c505e9: 99 changed, 76 added/removed,
  one order-only project move. Strict Release zero warnings/errors; bilingual docs, Actionlint,
  patch inventory and whitespace pass. Config/patch/log surfaces are 625 / 174 / 4,741 with no
  removals; patch inventory remains 132 classes / 199 methods. Independent source review found
  no additional actionable defect; headset/peer pixels remain unverified.

- **dev / 1.0.5 / ModBuild 531** Build-530 hardware confirms
  native Spellweaver recovery returns FireOrbs, ManaBolt, RidetheWind and FlameStrike from
  Lost to Hand (Player.log 9950–10001), but their burnt presentation remains. Native widget
  pile caching and retained burn presentation now receive explicit recovery reconciliation
  before local draw and remote publication. Old smoke cannot acquire a new Hand address;
  reset retries are isolated and cannot cancel a new burn or affect a pooled replacement. Guildmaster table controls are not globally redundant:
  native merchant/trainer/enhancement entry points exist. Build 530 introduced a silent
  whole-rail omission when optional support geometry could not be fitted. Refined mesh
  measurement and a readable right-side fallback retain access without changing campaign
  placement, native action availability or gameplay callbacks. Failed scans keep their
  ordinary cadence; missing-HUD and failed-support diagnostics are bounded.
  The maintainer reports the other build-530 hardware issues appear resolved. Current local
  logs are 530; remote logs remain historical 500. The new fixes still need headset checks.
  Records: [CARD-RECOVERY-531.md](CARD-RECOVERY-531.md), [GUILD-RAIL-531.md](GUILD-RAIL-531.md).
  Focused recovery: 552 runtime assertions / six bindings / 22 negative controls; room
  geometry: 7,014 assertions / 21 bindings / seven negative controls. Complete integration
  guard and 254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. The only guard exit-1
  verdict is the expected compiled difference from historical baseline 080c505e9:
  99 changed, 76 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,740 with no removals; patch inventory remains 132 classes / 199 methods.

- **dev / 1.0.5 / ModBuild 530** addresses the five build-529 hardware findings. MR
  backings are excluded from native wall ownership, supplemental unseen-region backings
  are restricted to the intended geometry, and stale/inactive sources are retired.
  Guildmaster controls fit the right tabletop behind the knife; its environment floor
  follows native furniture bases. Overlapping map icons use nearest visible centres for
  laser and fingertip selection. The original Guildmaster quest list stays during browsing
  dialogs, but actual accepted quest story/loadout still hides it, including native peer
  travel without a previously observed local selection. Campaign placement is unchanged.
  Records: [MR-SCENARIO-530.md](MR-SCENARIO-530.md), [GUILD-ROOM-530.md](GUILD-ROOM-530.md),
  [MAP-PICKING-530.md](MAP-PICKING-530.md), [GUILD-QUESTS-530.md](GUILD-QUESTS-530.md).
  Hardware confirms the build-528/529 table is visible. Exact MR pixel attribution,
  final floor contact/knife clearance and the new interactions still need headset checks.
  Remote logs remain historical build 500. EN/DE tutorial execution hints now explicitly
  require board CONFIRM; the unsupported second-pick advice is removed from all five uses.
  Validation: complete guard/source/presentation suite and 254,565 real-runtime wire
  assertions pass; strict Release zero warnings/errors, bilingual docs, Actionlint and
  whitespace checks pass. New focused totals: MR ownership 39, room geometry 6,273,
  map picking 333 and standing quest list 69, with bindings and negative controls.
  Tutorial scope rechecked after the wording change (42 runtime / 17 binding assertions).
  Guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  97 changed, 76 added/removed types, one order-only project move. Config/patch/log
  surfaces are 625 / 174 / 4,739, with no removals; patch inventory remains 132 classes /
  199 registered methods. One additional Debug floor-placement token versus build 529.

- **dev / 1.0.5 / ModBuild 529** moves the per-burn continuity records to opt-in Debug,
  including an early observer guard to avoid material/progress reads and formatting at
  ordinary verbosity. Re-enabling Debug starts fresh; anomaly output remains bounded.
  Build-528 table/pool diagnostics already require Debug. This corrects the earlier promise
  that normal logs would contain detailed continuity evidence. See [BURN-528.md](BURN-528.md).
  The user's sparse-normal-log requirement is also recorded in `AGENTS.md`.
  Validation: complete source/presentation guard and 254,565 real-runtime wire assertions
  pass; strict Release zero warnings/errors; bilingual docs and whitespace checks pass.
  The expected compiled diff from historical baseline 080c505e9 is the only guard exit-1
  verdict. Config/patch/log surfaces remain 625 / 174 / 4,738, with no removals.

- **dev / 1.0.5 / ModBuild 528** addresses the owner's local short-rest flash, Guildmaster
  table/standing quest list, and a native Swift Bow UI initialization error in the latest
  build-527 logs. Original faces retain animated materials through temporary ownership;
  raw progress/material diagnostics separate a restart from a material swap. Native card
  hierarchy returns before teardown/recycling, with damaged-copy rejection before reuse.
  The original quest widget returns after temporary story/journey withdrawal. Guildmaster
  discovers and fits the campaign table's original assets without moving native furniture.
  Records: [BURN-528.md](BURN-528.md), [CARD-POOL-528.md](CARD-POOL-528.md),
  [GUILD-QUESTS-528.md](GUILD-QUESTS-528.md), [GUILD-TABLE-528.md](GUILD-TABLE-528.md).
  **Hardware remains unverified:** exact flash writer, cold Guildmaster asset availability,
  final furniture fit, repeated scene transitions and current multiplayer observers.
  Remote logs remain build 500, not current evidence. No release was requested this round.
  Integrated validation: full guard source/presentation checks and real-runtime wire vectors
  pass (254,565 wire assertions). Focused totals: burn 534, material ownership 543, native
  pool lifetime 169, standing quest log 26, table fit/material lifecycle 1,530, plus bindings
  and rejected mutations. Strict Release zero warnings/errors; bilingual docs and Actionlint
  pass. Guard exit 1 is the expected compiled difference from baseline 080c505e9.
  Surfaces: 625 config keys / 174 patch attributes / 4,738 log tokens; two new registered
  pool hooks and five new diagnostic tokens versus 527. No existing surface was removed.

- **1.0.4 release authorized after the build-527 hardware report.** The maintainer reports
  no visible issues. Current local logs show completed encounter room making with the new
  window fixed and no mod Error/Fatal entries. Old remote logs are not current evidence.
  Release preparation and audit: [RELEASE-104.md](RELEASE-104.md).
  **Published successfully:** main Release run 35276519563 rebuilt immutable tag v1.0.4
  from main a6d044c4, verified its existing full dev CI evidence, packaged and uploaded the
  archive, checked its SHA256 and published it as Latest. No full suite was repeated on main.
  Recovery fixes landed through PRs #8/#9 after full dev CI; their PR checks reused proof.
  Dev bookkeeping completed at 24c8fce9 and now names **1.0.5**, still ModBuild 527.
  The earlier upload failures and draft-discovery defect are resolved; the release record
  preserves their evidence and the final archive digest.

- **dev / 1.0.4 / ModBuild 527 preserves the newly opened map window's spawn pose.**
  Build-526 logs show three quest-popup animations moving only the newcomer. The solver now
  anchors incoming windows and admits only older movable overlaps. Visual occupancy uses
  original painted/cropped content instead of transparent host/hit rectangles, after opening
  effects settle. The legacy standing-quest-log preference now keeps a free gaze centre;
  hidden-log private quest selection retains its established corner placement.
  Evidence and focused validation: [WINDOW-ANCHOR-527.md](WINDOW-ANCHOR-527.md).
  Validation: reflow 1,322 assertions / 18 bindings / three negatives; quest seat 43 / three
  negatives; painted occupancy 453 / 29 negatives; shared reflow 74 / six bindings / four
  negatives. Strict Release zero warnings/errors. Frame-order, partial-order, bilingual docs,
  Actionlint, shell syntax and whitespace pass. Config/patch/log census unchanged:
  625 / 172 / 4,733. The user confirms the hardware behavior; a current matching-peer log was not supplied.

- **dev / 1.0.4 / ModBuild 526 addresses temple-first/repeated temple header drift and
  general invisible MR contributors.** The build-525 report confirms merchant improvement,
  but the same header moves down/left after temple entry and contaminates later merchant
  openings. The new source repair observes native header TRS before conversion and resolves
  original parent frames rather than replaying local coordinates across different parents.
  MR extent guards additionally distinguish native renderer transparency and the actual
  displayed capture footprint from unbounded authored geometry, in every direction.
  Original visible overflow and the window's existing animation remain part of the contract.
  Evidence, focused validation and hardware limits: [MR-VISIBLE-526.md](MR-VISIBLE-526.md).
  Validation: banner 314 assertions; ink/capture/watch 447; MR layout 258; animation 552,
  with integration bindings and mutation negatives. Strict Release zero warnings/errors.
  Source/frame/docs checks pass; config/patch/log surfaces unchanged: 625 / 172 / 4,733.
  The user confirms the MR reopen defect is fixed in the build-526 hardware test.


- **dev / 1.0.4 / ModBuild 525 repairs the native header that inflated reopened MR windows.**
  Build-524 diagnostics identify the same shared `UI Adventure Header/Icon` drifting upwards
  and shrinking on every merchant reopening, then carrying the defect into temple.
  The native world-preserving parent change retained the converted window's pose/scale;
  the old mod return path skipped geometry restoration after native ownership resumed.
  A tracked borrow now restores the original root-local layout/pose on both return paths,
  preserving native parent/sibling choices and child content. No MR geometry clamp or
  animation change. Source cause and reproduction are in [MAP-HEADER-525.md](MAP-HEADER-525.md);
  corrected headset appearance still needs confirmation.
  Validation: 180 runtime assertions, three bindings, four runtime negatives and one binding
  negative; strict Release zero warnings/errors. Frame-order, bilingual docs, Actionlint,
  shell syntax and whitespace pass. Config/patch/log surfaces unchanged: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 524 is a diagnostic build; the MR reopen defect remains open.**
  The user reports first merchant/map opening correct and subsequent openings too tall.
  Local logs identify 523; remote logs remain historical 500. Capture/hit bounds grow,
  but their extrema do not establish the actual MR contributor. Previous MR diagnostics
  were Debug-only. Bounded normal-level records now identify actual MR edge graphics,
  masks/alpha/material state and target/host instances across conversion lifetimes.
  No further rendering or native-flow change is claimed. See [MR-REOPEN-524.md](MR-REOPEN-524.md).
  Validation: strict Release zero warnings/errors; ink/diagnostics 398 assertions and
  24 negatives; MR layout/accessor 258 assertions, 48 bindings and 16 negatives; animation
  lifecycle 545 assertions, three bindings and three negatives. Frame-order, bilingual docs,
  shell syntax and whitespace pass. Config/patch/log surfaces: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 523 fits MR backgrounds to native painted geometry.**
  Source changes exclude empty text-layout height and reintroduced tooltip glyphs,
  but build-523 hardware evidence confirms that merchant/map reopen growth persists. Local steady/effect paths and inert remote surfaces share the original
  text/image/clip/visibility policy and a small margin. Native layout, hit/capture and grab
  geometry remain unchanged. Source fixes and build-522 screenshot/log evidence are in
  [MR-MAP-BOUNDS-523.md](MR-MAP-BOUNDS-523.md); headset confirmation remains open.

- **523 focused checks pass:** native ink/painted geometry 368 runtime assertions /
  nineteen runtime and one binding negative; MR layout/accessor 258 assertions /
  48 bindings / thirteen runtime and three binding negatives; actual animation lifecycle
  545 assertions / three bindings / three negatives. Strict Release has zero warnings/errors.
  Eleven frame-order locks, 617 hardware markers, shell syntax and bilingual docs pass.
  Config/patch/log surfaces stay 625 / 172 / 4,732. Unrelated local suites were not repeated.

- **dev / 1.0.4 / ModBuild 522 couples MR backgrounds to window materialisation.**
  Backgrounds use the same erosion field and element progress and disappear before
  the debris-only tail. Native hidden/empty/transparent content clears its backing
  immediately, locally and remotely, independently of cached geometry measurements.
  Close/reopen, MR toggles and native continuation remain independent of decoration.
  See [MR-ANIMATION-522.md](MR-ANIMATION-522.md); headset confirmation remains open.

- **522 focused checks pass:** MR layout/accessor 258 runtime assertions / 48 bindings /
  thirteen runtime and three binding negatives; native ink/live visibility 309 assertions /
  fourteen runtime and one binding negative; erosion mesh 35,833 assertions / ten negatives;
  actual animation lifecycle and native continuation 542 assertions / three bindings /
  three negatives. Strict Release has zero warnings/errors. All eleven frame-order locks,
  617 hardware markers, Actionlint, shell syntax and bilingual docs pass. Config and patch
  surfaces remain 625 / 172; log tokens increase to 4,732 with one new failure diagnostic.
  Full unrelated local suites were not repeated, as requested.

- **dev / 1.0.4 / ModBuild 521 restores the original tutorial hand/controller choices.**
  Card handling, fingertip interaction and prose show hands; key lessons show both
  controllers with the existing per-hand highlights. The original 0.22s animation remains.
  Recovery respects the active task and restores a hand immediately if its controller
  model is lost. Build 518's continuous-controller interpretation was explicitly corrected
  by the user. See [TUTORIAL-HANDS-521.md](TUTORIAL-HANDS-521.md). Only affected tests and
  the strict build run for this change, as requested; headset confirmation remains open.

- **521 focused checks pass:** controller presentation 3,452 runtime assertions / four
  bindings / twelve negatives; first-tutorial scope 42 / 17 / eight. Strict Release has
  zero warnings/errors. Config, patch and log surfaces are unchanged at 625 / 172 / 4,731.
  Full local guard and golden-wire suites were not repeated per the user's request.

- **dev / 1.0.4 / ModBuild 520 scopes initiative and element MR backgrounds to their
  original native rows, locally and on inert remote clones.** Transparent host extents
  and sibling UI cannot inflate those backgrounds. Normal window artwork and smooth
  sizing remain. Local evidence is build 519; remote files remain historical build 500.
  See [MR-CI-520.md](MR-CI-520.md) for evidence, validation and hardware limits.

- **CI now reuses trusted successful dev evidence for identical source trees.** Dev
  still runs full checks; unchanged internal PRs can reuse them, while forks and changed
  merges run full validation. Main releases require successful full-test evidence, then
  build/package the actual main commit without repeating the full suite. No proof
  artifacts are stored. No release or main update is part of this change.

- **520 integration passes:** 17 source checkers, all production harnesses and 254,565
  wire assertions; strict Release zero warnings/errors. MR: 250 runtime + 41 bindings,
  13 runtime / three binding negatives; native ink: 259 assertions / ten runtime and one
  binding negative. CI proof: 23 cases; release topology: 36 assertions; artifact cleanup:
  20 cases. Actionlint and bilingual docs pass. Config / patch / log surfaces are
  625 / 172 / 4,731; patch inventory 130 classes / 197 methods; bundle unchanged.
  Build-519 compiled comparison: 13 changed (seven build-only), one added, zero removed.
  Retained build-502 comparison: 86 changed / 54 added / zero removed.

- **dev / 1.0.4 / ModBuild 519 adds opening-time window room making.** Overlapping
  encounter/story windows can move together with a brief animation inside the view.
  One VR participant authors shared movement; stationary remote grips and manual moves
  interrupt it. Visible FINISHED story frames retain pose synchronization without reopening
  the native dialog. Additive record 77 carries explicit held/automatic masks; v3 remains.
  See [WINDOW-REFLOW-519.md](WINDOW-REFLOW-519.md) for evidence and final validation.
  Supplied logs remain local 515 / remote 500; hardware validation of this build is open.

- **519 integration passes:** 17 source checkers, all production suites, 254,565 wire
  assertions and strict Release with zero warnings/errors. Layout: 1,247 runtime + 15
  bindings / two negatives; authority: 74 + six / four. Surfaces: 625 config / 172 patch
  signatures / 4,730 log tokens. Patch inventory stays 130 classes / 197 methods; bundle
  unchanged. Build-518 compiled comparison: 14 changed / three added / zero removed,
  including five build-only changes and one buffer-size-only change. The retained build-502
  comparison is 84 changed / 53 added / zero removed. See the build record for evidence.

- **dev / 1.0.4 / ModBuild 518 addresses tutorial controllers and defeat Retry.** Both
  controllers stay visible throughout the custom first-tutorial lesson, with task-specific
  highlights on the applicable hands. Recursive VR-layer assignment protects their parts
  from scenery fading; missing models and rebuilt hands recover during the lesson.
  Retry restores each participant's original scenario head pose, scale and board pose.
  Round reloads preserve that baseline, and later peer movement or saved zoom cannot
  redefine it. See [TUTORIAL-RETRY-518.md](TUTORIAL-RETRY-518.md) for source evidence,
  focused coverage and final integration results. Hardware confirmation remains open;
  supplied logs still identify local 515 / remote 500. No bundle, wire or release changes.

- **518 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Tutorial controllers: 1,019 runtime +
  four bindings / nine negatives; retry: 433 + 17 / 15. Retained build-502 compiled diff:
  83 changed / 50 added / zero removed. Private build-517 comparison: 14 changed / three
  added / zero removed, including seven changes limited to the build constant. Reviewed
  surfaces: 625 config / 172 patch signatures / 4,729 log tokens; patch inventory 130
  classes / 197 methods. All 21 classified network-action patches, bilingual docs, shell
  syntax and whitespace pass. No hardware result is inferred from these checks.

- **Build 517 addresses repeated burn playback.** Native effect aliases,
  pile refresh and hover cleanup cannot restart or truncate an owned ability burn. Historical
  lost/consumed widget construction paints the original settled output; a replacement during
  playback waits for the original with cancellation-safe ownership. Actual recovery permits
  later burns. Item effects cannot overlap, and active-card resets requested during playback
  run after completion. Local/remote discovery uses original model identity; missing remote
  samples retain the same lost card's last owner-painted output. Actual completed flight claims
  remain distinct from historical baselines. Scene and pool boundaries retire native guards.
  Source and focused regression review complete; full integration results are recorded in
  [BURN-517.md](BURN-517.md). Supplied logs remain local 515 / remote 500, so this is not
  a headset verification. No bundle, wire-format or published-release changes.

- **517 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Ability replay: 496 runtime + three
  bindings / 13 negatives; items 225 / 11; local layout 219 / 16; remote sequencing 108 / 21;
  scene lifetime 36 + 23 / eight. Retained build-502 compiled diff: 77 changed / 47 added /
  zero removed. Private build-516 comparison: 16 changed / two added / zero removed,
  including seven changes limited to the propagated build constant. Reviewed surfaces:
  625 config / 171 patch signatures / 4,729 log tokens; patch inventory 128 classes / 195
  methods. Bilingual docs, shell syntax and whitespace pass. Hardware confirmation is open.

- **Build 516 hardware fixes remain included in dev.**
  Completed discard pages retain their native selected claims; final confirmation cannot
  light an unavailable second recess. Native recycling updates the locked prefix, and undo
  keeps earlier page return flights. MR backings fit visible native content with a small margin,
  reject empty/transient measurements and animate over a shared 150 ms locally and remotely.
  Borrowed native card hierarchies return before scene unload; native loading state blocks
  re-adoption, while aborted loads restore retained selected identities and original callbacks.
  Supplied local evidence is release 515; remote files remain historical 500. The exact first
  Unity destruction order is not logged. Hardware retest remains open; see
  [HARDWARE-516.md](HARDWARE-516.md) and its three lane reports.

- **516 integration checks pass:** all 17 guard checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Pick tray: 245 runtime + eight bindings /
  six negatives. MR: 233 + 25 / ten runtime + three binding negatives. Scene lifetime:
  35 + 20 / seven negatives. Native ink: 243 assertions. Retained build-502 compiled diff:
  76 changed / 45 added / zero removed; private build-515 comparison: 21 changed / three
  added / zero removed, with 13 changes limited to version/build constants. Reviewed surfaces:
  625 config / 164 patch signatures / 4,729 log tokens; patch inventory 120 classes / 187
  methods. Bilingual docs, shell syntax and whitespace pass. Published 1.0.3 is unchanged.

- **1.0.3 / ModBuild 515 is published from main.** PR #6 merged the accepted runtime/assets
  with bilingual release highlights as `fd76de86`. Release run 35151926513 passed; tag,
  public ZIP, bundle hash, release DLL and the unauthenticated latest endpoint were verified.
  The workflow retained main ancestry on dev and advanced its next version to **1.0.4**.
  See [RELEASE-1.0.3.md](RELEASE-1.0.3.md).

- **ModBuild 515 softens the Glove surface (full install).**
  Both glove materials reduce authored normal relief from 0.5 to 0.25. Native model renders
  and actual bundle checks cover both hands and confirm that Plate/Arcane, geometry and
  attachment anchors are preserved. The exact Unity 2021.3.5f1 bundle has 617 assets and
  74,942,975 bytes. All 17 checkers and production suites pass; 254,019 wire assertions;
  strict Release zero warnings/errors. Incremental compiled comparison has seven changed types,
  exclusively the propagated ModBuild constant; no added/removed types. Surface counts remain
  625 / 163 / 4,728, patch inventory 119 / 186. The maintainer accepted the current changes
  for release; no new per-case hardware capture accompanies that acceptance.
  See [GLOVE-SURFACE-515.md](GLOVE-SURFACE-515.md).

- **ModBuild 514 limits additional VR lessons to the first native tutorial.**
  Admission uses the tutorial selector's first ID and filename, while later tutorials keep
  their native sequence and generic VR wording/input adaptations. Pending lesson/skip/hold
  state retires on scope loss; held messages cannot cross native controller ownership.
  Focused tests pass: 42 runtime + 17 binding assertions, seven runtime negative controls and
  one binding negative control. All 17 checkers and production suites pass; 254,019 wire
  assertions; strict Release zero warnings/errors. Compiled comparison: 71 changed / 42 added /
  zero removed against retained build 502; incremental build-513 comparison 19 changed / two
  added / zero removed, reviewed (tutorial scope plus propagated version/build constants).
  Surfaces 625 / 163 / 4,728; patch inventory 119 classes / 186 methods. The later release
  acceptance is recorded above; it does not enumerate individual tutorial transition tests.
  See [TUTORIAL-SCOPE-514.md](TUTORIAL-SCOPE-514.md).

- **Previous release: 1.0.2 / ModBuild 513.** PR #5 merged the hardware-tested
  dev source unchanged as `11107a29`. Release run 35146255179 passed; tag, public
  download, checksum and release DLL were verified. See [RELEASE-1.0.2.md](RELEASE-1.0.2.md).
  After that release, the workflow preserved main ancestry on dev and advanced to **1.0.3**.

- **ModBuild 513 sequences every native burn before card replacement.**
  Round slots, fans, active grids and character exchange retain their previous presentation
  until all native burns finish. Actual iterator completion distinguishes finished handles.
  Observers wait for the canonical owner's completion frame; durable original-card release
  addresses delayed delivery and slot reuse. Consumed items retain their native widget and
  share original item appearance through additive stream 17/18 (record 76). Incoming character
  views also wait for actual owner burn progress; offscreen completions do not invent flights.
  Native gameplay callbacks and mandatory decisions keep running. See
  [BURN-SEQUENCING-513.md](BURN-SEQUENCING-513.md). The maintainer reports a successful
  build-513 retest; current local logs also observe a build-513 peer. All six recorded burns
  complete with subsequent flights, and phase stalls resolve. Retained remote files remain
  historical build 500. Not every edge case is individually established by this capture.
  Coarse game-loop cadence declines during the session; no memory/GPU trace establishes
  its cause or a leak. The release audit records this limitation and warning triage.

- **513 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Focused suites: local layout 204, remote
  sequencing 70, native completion 33, item lifetime 115 and item appearance 645 assertions,
  with runtime negative controls. Retained build-502 compiled comparison: 65 changed / 40 added /
  zero removed; additional build-512 comparison: 39 changed / 11 added / zero removed, reviewed.
  Surfaces 625 / 162 / 4,728; patch inventory 118 classes / 185 methods. Bilingual docs,
  shell syntax and whitespace pass. These results do not establish headset appearance.

- **dev / 1.0.2 / ModBuild 512 audits mandatory input and native continuation.**
  Map reward managers can be reached outside a scenario controller; failed blocking map
  conversions can request a usable desktop; travel parking failure retains original guarded
  input. Reused windows recheck mandatory close admission. Shared rewards use explicit participation replies. Failed
  conversion and attachment restore native UI, retaining ownership when cleanup needs retry.
  No gameplay lock bypass or timed automatic confirmation is introduced. See
  [DEADLOCK-512.md](DEADLOCK-512.md) for the scope, evidence and final gate results.
  At implementation time, supplied logs were local 510 / remote 500. The later successful
  build-513 retest and release review are recorded above; this is the historical audit scope.

- **512 integration checks pass:** all 17 checkers and production suites; 253,893 wire
  assertions; strict Release zero warnings/errors. Focused suites: rewards 320, map flow
  1,997, modal desktop 1,066, mandatory close 76, reward pose 104 and rollback 87 assertions,
  with runtime negative controls. Retained build-502 comparison: 39 changed / 29 added /
  zero removed types, reviewed; additional build-511 compiled comparison confined to this
  audit. Surfaces 625 / 161 / 4,728; patch inventory 117 classes / 184 methods.
  Bilingual docs, shell syntax and whitespace pass. These checks alone do not establish hardware outcomes.

- **dev / 1.0.2 / ModBuild 511 corrects the failed build-510 chest retest.**
  Tutorial/custom scenarios use UIRewardsManager outside Guildmaster mode; its gamepad
  confirmation adapter rejected the VR click. Continue now supplies only native input,
  preserving native reward groups, multiplayer ownership/actions and the completion callback.
  The button paints native hover/press/disabled states. Capture/chrome include the exact
  original heading's live glyph bounds, retaining layout, fonts and native masks.
  Separate build-509 user logs exposed allocating TMP material reads that repeatedly tore
  down render targets; capture and diagnostic reads now use existing shared materials.
  This removes that demonstrated failure path, not every possible source of FPS dips.
  Local evidence is build 510; retained remote logs are build 500. See
  [REWARDS-511.md](REWARDS-511.md) for the full evidence and validation record.
  Headset acceptance was open at implementation time; the later build-513 maintainer
  retest reports no observed issues. See the release audit for its actual evidence limits.
- **511 integration checks pass:** strict Release zero warnings/errors; all 17 checkers,
  production suites and 253,759 wire assertions. Reward: 299 assertions / 13 negatives;
  materials: 2,031 / four; ink: 237 / five runtime negatives plus one placement binding.
  Retained build-502 compiled comparison: 35 changed types, 25 additions, no removals,
  reviewed. Config/patch/log surfaces remain 625 / 161 / 4,726; bilingual docs and
  whitespace checks pass. These checks do not replace the hardware acceptance above.
- **Build 510's hardware retest failed despite green checks.** Its reward test incorrectly
  modeled ConfirmPressed as an unconditional input latch. Build 511 executes the native
  ProcessRewards iterator through completion, with a negative control for that exact defect.
  The shared-window identity, placement and first-reveal handoff from 510 remain in place;
  [REWARDS-510.md](REWARDS-510.md) is the historical implementation record.

- **Previous release: 1.0.1 / ModBuild 509.**
  v1.0.1 names PR #3 merge be74759e; Release run 35014316673 succeeded.
  The public ZIP matches its published SHA256 and contains the complete asset bundle;
  its DLL reports 1.0.1 / build 509 / be74759 / IsDevBuild=false. Combat log startup
  defaults to off while saved preferences and manual display remain available.
  Candidate CI and all local gates passed, including 253,674 wire assertions and
  36 release topology checks. See [RELEASE-1.0.1.md](RELEASE-1.0.1.md).
- After 1.0.1, the workflow preserved main ancestry and advanced dev to 1.0.2 in
  bot commit 12f66604. The subsequent 1.0.2 publication is recorded above.

- **1.0.1 / ModBuild 508 corrects the build-507 tutorial presentation retest.**
  The user confirms the deadlock is resolved, and the log completes BuyItem/FTUE.
  HelpText and its separate native BG now reflow and align together instead of leaving
  the border and text apart. Corner placement excludes adopted hint geometry while
  retaining it for interaction/chrome. The exact quest-preparation hint is omitted
  under the user's explicit exception; the later battle-goal explanation remains
  native and all quest/tutorial continuations retain their original authority.
  See [TUTORIAL-508.md](TUTORIAL-508.md). Final headset confirmation remains required.
- **508 source/regression gates pass:** all 17 checkers, 253,674 wire assertions and
  production suites. Hints: 45 assertions/seven negative controls; ink/placement:
  218 assertions/four runtime negatives plus one binding negative; preparation prefix:
  13 assertions/three negatives. Compiled comparison with retained build 502: 30 changed
  types, 14 additions, no removals. Surfaces: 625 config keys / 161 patch signatures /
  4,724 log tokens; runtime patch inventory 117 classes / 184 methods. Strict Release
  passes with zero warnings/errors; bilingual docs and whitespace checks pass.

- **1.0.1 / ModBuild 507 repairs savegame tutorial input and hint layout.**
  The live movie surface now accepts laser/poke skip through native continuation.
  Original HelpText wraps at authored font size; pending standalone dissolve callbacks
  are retired before owner adoption. The merchant-to-map dispatcher now emits complete
  native toggle events: its former silent Select omitted the FTUE listener, leaving
  BuyItem active and blocking quest progression. Native tutorial/travel locks remain
  authoritative. See [SAVEGAME-507.md](SAVEGAME-507.md). Headset replay remains required.
- **507 source and regression gates pass:** all 17 checkers, 253,674 wire assertions
  and production suites; movie 66 assertions/six negative controls, hints 38/five,
  native off-bar dispatch 10/two. Surfaces remain 625/161/4,723. Retained build-502
  compiled comparison: 30 changed types, 13 additions, no removals. Strict Release
  has zero warnings/errors; bilingual documentation and whitespace checks pass.

- **1.0.1 / ModBuild 506 corrects the movie-window regression reported on 505.**
  The ordinary orphan sweep now recognizes the live video's exact grab holder,
  preventing repeated destruction/recreation at the world origin. Its full-frame
  image is explicitly content, so the backdrop exclusion cannot hide its handle.
  Chrome shares the canvas's persistent lifetime and module teardown; local and
  remote movie windows use the same ordinary grab/resize and modal ordering paths.
  See [VIDEO-WINDOW-506.md](VIDEO-WINDOW-506.md). Headset replay remains required.
- **506 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production regression suites. Movie ownership/sweep coverage now has 50 assertions
  and four negative controls; the ink walker adds 111 assertions/two negative controls.
  Strict Release: zero warnings/errors; bilingual docs and whitespace checks pass.
  Config/patch/log surfaces remain 625/161/4,723. Retained build-502 compiled comparison:
  28 changed types and 12 additions, no removals; the only newly changed types compared
  with the 505 review are ConvertedPanel and PanelInkBounds, alongside the intended
  movie/modal changes and build constants in types already in that review.

- **1.0.1 / ModBuild 505 fixes savegame introduction presentation.** Build 504 logs
  show native fullscreen video decoding to the desktop and introduction messages
  retaining old standalone conversions after adoption into a character window.
  Dedicated movie windows support shared playback/pose through additive TLV 72.
  Per-message native provenance and serialized owner references replace the global
  producer scan. Atomic conversion handover removes empty frames; hint fit excludes
  its fullscreen dimmer and cannot resize its owner. Native continue/fade behavior
  remains authoritative. See [SAVEGAME-505.md](SAVEGAME-505.md). Headset replay remains
  required; no main/tag/release change is part of this round.
- **505 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production suites with their negative controls. New movie/hint suites cover 39
  native-video, 35 shared-playback and 20 hint assertions; updater coverage adds
  nine assertions. Strict Release has zero warnings/errors; bilingual docs pass.
  Compiled review against the retained build-502 baseline: 26 changed types, 12
  additions, no removals (including already-integrated gold/updater/version changes).
  Config keys remain 625; patch signatures increase 160→161 and log markers
  4,719→4,723, with no removals. Runtime patch inventory: 116 classes/183 methods.
- **1.0.0 / ModBuild 504 repairs the self-update prompt.** The 0.9.0 hardware log proves that
  the public latest-release request and version comparison succeeded, then a bare `Transform` in
  `SelfUpdateDialog.BuildProgressRow` threw before the dialog could be drawn. Every dialog layout
  node now has an explicit `RectTransform`; a production harness constructs the choice/progress
  path and mutates the Progress node back to the failing form as a negative control. The headset
  retest confirmed the visible prompt using `install.ps1 -FakeVersion 0.9.0`; that flag compiles
  a release-mode test build, so the regular updater path runs without changing the checkout. See
  [UPDATE-504.md](UPDATE-504.md).
- **1.0.0 / ModBuild 503 makes held gold-pile cards match the laser-hover amount.** Native hover
  totals every `MoneyToken` on the tile, while the held card had only applied `GoldConversion` to
  its one grabbed token. A combined pile can now show the same current total in both views without
  a game-state write. See [GOLD-503.md](GOLD-503.md). Headset confirmation remains pending.
- **503 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing production
  suites and negative controls pass; strict Release has zero warnings/errors and bilingual docs
  pass. Compiled review: eight changed types, seven build-constant-only and `GrabbableProp`; no
  additions/removals. Config/patch/log surfaces remain 625/160/4,719.
- **1.0.0 / ModBuild 502 fixes the missing native party-container handover.** Fresh 501
  single-player logs for quests 078 and 039 show battle-goal selection open beneath a still
  refused outer PartyPanel. Build 500 admitted its different inner owner but missed that
  ancestor; multiplayer success came from the older 90-tick fallback. The current native
  PartyPanel wrapper is now admitted directly while intro/readiness guards remain intact.
  See [MAP-502.md](MAP-502.md) and its evidence/review. Headset replay remains pending.
- **502 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing suites
  and expanded map-flow tests (1,987 assertions/six negative controls). Strict Release has
  zero warnings/errors; bilingual docs pass. Compiled review: nine changed types, seven of
  them build constants only, with no additions/removals. Surfaces remain 625/160/4,719.
- **502 remains version 1.0.0 on dev.** The existing main/tag stays build 498. Build 501's
  card-flight/figure fixes are retained; its final hosted CI passed at `5aeb2cce`.
- **1.0.0 / ModBuild 501 fixes remote animation handovers.** Matching build 500 logs
  identify a flight starting while its remote source recess is still occupied; remote
  dock clearing additionally kept a stationary crumble beneath the flying copy. Source and
  destination ownership now survive delayed seating, overlapping flights and character changes.
  Held figures return to the board before native movement/facing/animation reads, with stale
  held samples rejected until release/switch. Recovery flights use native hand provenance.
  See [MP-501.md](MP-501.md), its independent reviews and hardware replay checklist.
- **501 local gates:** all 17 source checkers, 253,579 wire assertions and existing production
  suites; new flight tests cover 782 assertions/eight negative controls and figure tests cover
  1,440 assertions/six negative controls. Strict Release has zero warnings/errors; bilingual
  docs pass. Compiled review: 21 changed types (six build-constant-only), two new patch types,
  no removal. Config remains 625, patch signatures 152 to 160, log markers 4,718 to 4,719.
- **501 retains version 1.0.0 and is integrated on dev.** Existing main/tag `v1.0.0` remains build 498.
  New regression harnesses run in both CI and main release checks. Final headset timing remains
  unverified until the next hardware test.
- **ModBuild 500 attempted a map preparation softlock fix; 502 corrects its missing ancestor case.** Offline 499 logs show
  native battle goals opened under a party root still refused by frozen story-curtain
  membership. The native loadout's released hide request now admits its original root and
  descendants. VR map input and offline travel also honor the native map lock; online quest
  readiness retains its own visibility/state rule. No game state is forged to escape.
  See [MAP-500.md](MAP-500.md) and its evidence reports. Headset replay remains pending.
- **500 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and prior
  production suites; new map-flow harness 757 assertions with five negative controls.
  Strict Release has zero warnings/errors; bilingual docs pass. Reviewed compiled scope:
  11 changed types (including seven build-constant-only changes), two new helpers, no removal.
  Config/patch surfaces remain 625/152; log markers increase 4,717 to 4,718.
- **500 retains version 1.0.0 and is integrated on dev.** The existing main/tag `v1.0.0`
  still identifies build 498; no release assets or tags are changed by this hotfix.
- **CI storage policy (2026-09-13):** normal pushes/PRs no longer upload DLL artifacts.
  Manual CI on dev can request a tested download; serialized cleanup retains at most three
  builds for two days. Release ZIP publication on main remains mandatory and unchanged.
  Version 1.0.0 / ModBuild 499 runtime is unchanged. See [CI-STORAGE.md](CI-STORAGE.md).
- **1.0.0 / ModBuild 499 fixes an unintended fallback screen during remote long-rest burns.**
  Current 498 logs show the opaque desktop composite appearing while the remote board stays
  active; older 491 evidence has the same signature. Native foreign-hand UI locks were outside
  the local burn guard. The new guard checks every actual lock owner and preserves explicit
  screen requests. Original card/board presentation is unchanged. Headset confirmation remains
  pending. See [REST-499.md](REST-499.md) and its linked evidence reports.
- **499 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and existing
  production suites; new modal harness 1,058 assertions plus five negative controls. Strict
  Release has zero warnings/errors. Compiled scope is the fallback fix, its new helper and
  version constants. Config/patch surfaces are unchanged; one diagnostic was added.
- **499 remains version 1.0.0 at the maintainer's request.** The already published main/tag
  `v1.0.0` identifies build 498; this hotfix does not rewrite that tag or replace its assets.
  A later release publication must come from `main` and deliberately handle that existing tag.
- **1.0.0 / ModBuild 498 release authorized on 2026-09-10.** Prepared on `dev` for the
  main-triggered release pipeline. Gameplay and presentation carry build 497 unchanged; the
  full package includes the reviewed build 483 bundle and repository-readiness fixes. See
  [RELEASE-1.0.0.md](RELEASE-1.0.0.md) for candidate verification and publication status.
  The maintainer handles public visibility and the in-headset update test separately.
- **1.0.0 repository preparation (2026-09-10):** current guides and CI instructions reconciled,
  historical references clearly marked, generated logs/renders and shader disassembly kept local,
  installer/uninstaller edge cases fixed, stale local bundle overrides prevented, release notices packaged. Version and runtime
  remain 0.9.1 / ModBuild 497. See [RELEASE-READINESS.md](RELEASE-READINESS.md) for validation
  and separate publication follow-ups. No release, tag, main-branch push or visibility change
  is part of this preparation. The maintainer explicitly deferred the fire-asset license question.
- **Previous development version: 0.9.1, ModBuild 497.** Release 0.9.0 (494) was published from `main` by
  [Release run 34407935479](https://github.com/McFredward/GloomhavenVR/actions/runs/34407935479);
  the pipeline passed and advanced `dev` to 0.9.1. See [RELEASE-0.9.0.md](RELEASE-0.9.0.md).
- **496 fixes SDK selection for installation on .NET 10-only machines.** The 494 SDK pin
  was too restrictive; major roll-forward preserves the preferred CI SDK while accepting
  newer installed SDKs. Installer preflight and the legacy restore-tool launch are checked.
  See [SDK-INSTALL-496.md](SDK-INSTALL-496.md).
- **495 adds rendered control-board tiles and improves every variant caption.** Bilingual player
  documentation combines controls and play guidance, covers both main-controller layouts and
  distinguishes selection, actions and character inspection. This is DLL-only after 483.
  Actual headset caption readability and tile interaction still require hardware confirmation.
- **483 IS A FULL INSTALL.** The asset bundle changed for the first time since ModBuild 368:
  74,943,671 → 74,943,763 bytes. Builds 369–482 were all DLL-only drops. A DLL-only install of
  483 shows neither of its two content changes, and the `ENV SKY BRANCH` log line says so out
  loud if it happens.
- **Previous performance hardware evidence covers496, local logs only, one additional player.** Regular scenario
  windows average11.35ms/frame; after a room expansion12.69ms. The run supports the user's smooth
  experience; neither progressive collapse nor a leak is established. Managed heap samples rise.
  See [MP-497-PERF.md](MP-497-PERF.md). Older remote logs and regression JPGs are not from this run.
- **497 synchronizes board motion with head/hand packets and extends hand ordering.** Owned normal
  hands reorder in selection, action and map, retaining order into the scenario; remote concealed
  plucks preserve surviving card positions. Review also closes the previously missing remote
  insertion gap/marker. Arrival/recenter yaw faces the player. Requested defaults and bilingual
  guides are updated; saved settings remain. See [MP-ROUND-497.md](MP-ROUND-497.md).
- **484 is DLL-only relative to 483.** Upgrading from the tested 482 requires the full 483 bundle.
- **493 optimizes multiplayer native presentation without reducing fidelity or cadence.**
  Card capture reuses immutable output; native sends avoid decoding their own snapshots; native
  playback avoids redundant writes/material swaps; original board sections refresh independently.
  Four-state production harnesses cover isolation and immediate transition/recovery behavior.
  Hardware FPS and full-party headset output remain unmeasured. See
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).
- Gate readings at497: all17 checkers pass; wire **253,579** assertions (**+524**: board88,
  fan61, insertion/edge375). Production capture **18,206**, playback **466**, board refresh
  **1,216** and the **12 existing runtime negative controls** pass. Worker-only negative controls
  also rejected three deliberate board defects and three fan defects. Strict Release **0 errors /
  0 warnings**; bilingual docs and all16 metadata-only reference assemblies pass. Patch registration
  **109 classes /167 methods**, surface **152**, config keys **625**, log tokens **4,716**,
  instrument-writes baseline **61**, bundle **74,943,763 bytes**. Records70/71 are additive;
  existing grammars and4096-byte presence reassembly bound remain intact. The documented presence
  budget grows3837→3840 bytes; allocation4097 retains257 spare bytes. This budget is historical
  arithmetic plus a tested three-byte tail, not a new saturated whole-protocol fixture.
  Compiled comparison against `1a714ee2`: **25 changed types and four added helpers**, no removed
  types or resources; changes match the reviewed source, defaults, packet capacity and embedded
  build constants. Local controlled cards and the entire map remain open; concealment is remote-only
  in scenarios.

### Recent builds

| build | what it was | install |
|---|---|---|
| 480 | the review round he asked for BEFORE spending a hardware test. Five read-only review lanes, 19 defects, three new gates | DLL only |
| 481 | the 2026-09 refactor programme: five lanes over 626 files / 550k lines. Also found four gates that could not fail | DLL only |
| 482 | the four rulings he gave on 481's deferred list, one lane each | DLL only |
| 483 | his two hardware notes, both baked into the assets on his ruling "lieber sauber" | **full** |
| 484 | multiplayer pulse, flights, grabbing, rest controls/burns, original bonus widgets and bounded extras transport | DLL only after 483 |
| 485 | remote character-change animations for map-room hands and open discard/burnt browsers | DLL only after 483 |
| 486 | native animation transport and systematic board/card/window parity repairs | DLL only after 483 |
| 487 | laser ownership, phase-consistent card visibility, stable initiative, cap sizing and native tooltip/highlight/element output | DLL only after 483 |
| 488 | short visible window-facing turn after release, matching grab-bar timing and preserving the drawn centre | DLL only after 483 |
| 489 | native card output, atomic held fronts, character decisions, committed/pending health and correctly routed/sequenced flights | DLL only after 483 |
| 490 | pre-test face/overlay/flight audit; later hardware exposed native group-bound rendering failures | DLL only after 483 |
| 491 | repair dynamic native card artwork and independent laser paths behind grab bars | DLL only after 483 |
| 492 | spent rest-burn continuity, native element material binding, board transition diagnostics and hardware-log review | DLL only after 483 |
| 493 | multiplayer capture/send/playback and independent native-section refresh optimization, preserving complete animation | DLL only after 483 |
| 494 | release 0.9.0, reproducible SDK selection and hosted native presentation regression harnesses | **full release package** |
| 495 | rendered board variant tiles, brighter larger captions and concise illustrated EN/DE play guidance | DLL only after 483 |
| 496 | SDK 10 installation compatibility, early SDK diagnostics and maintenance-tool runtime fallback | DLL only after 483 |
| 497 | atomic board motion, stable hand sorting across phases/map, remote insertion cues and requested defaults | DLL only after 483 |
| 498 | release 1.0.0 with build 497 gameplay and reviewed installation/packaging | **full release package** |

---

## 2. Owed to him, and what he has to judge

### 2a. He must look at this and say whether it is right

**The cellar's surround is now BLACK when zoomed out.** He reported two star skies in the cellar
and ruled the dome away. The dome was never visible from inside the room (the stone shell has a
closed ceiling, a capped stair shaft and capped rat holes); it was visible from OUTSIDE the shell,
in the zoomed-out pose where the room reads as a model in front of you. That surround is now the
`[Rig] VoidColor` clear. **This is a consequence of his instruction, not a defect** — but he has
not seen it yet, and it is one line to put back.

### 2b. Latest multiplayer corrections

- Build497 addresses follow-board sample timing, initial heading and owned hand ordering. Its
  hardware checklist includes concealed plucks, map-to-scenario sorting, remote insertion cues and
  long-rest exclusion. The user still needs to verify headset appearance and full-party scaling.
  Evidence and source changes: [MP-ROUND-497.md](MP-ROUND-497.md).

- Build 493 removes redundant native presentation CPU/allocation work and adds regression harnesses
  for four independent boards/senders. Review also closes pooled initiative identity and local element
  readiness recovery dependencies. Per-frame source sampling, original widgets and all visual rules
  remain intact. Hardware performance scaling is still owed; detailed proof and limits are in
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).

- Build 492 publishes rest-offer appearance from canonical pile models and retains the actual
  spent base through native burn reset, with native completion tracked independently. Remote
  element effects use original materials even when the viewer's branch is inactive. Board
  disappearance remains open; diagnostic/performance evidence is in
  [MP-ROUND-492.md](MP-ROUND-492.md) and its lane reports.

- Build 491 fixes the native card hierarchy construction failure affecting local map fans and
  remote fronts. Map cards remain public. Independent map/world UI laser routes now respect
  foreground grab bars and the clicking hand. See [MP-REGRESSION-491.md](MP-REGRESSION-491.md).

- Build490 reviews every local/remote card surface for face visibility, native overlay output
  and semantic flight lifecycle. Fixes include viewer-independent selection privacy, held map
  provenance, stale pooled models/materials, actor-scoped burn claims and owner release mirroring.
  See [MP-CARD-REVIEW-490.md](MP-CARD-REVIEW-490.md).

- Build489 addresses the thirteen MB488 findings and additional review defects. Cards mirror actual
  owner output; native decisions follow their character; damage previews preserve committed HP;
  active exits choose their true pile and burn flights wait for native completion. The Trample
  attack refusal was valid Disarm, not a targeting defect. See [MP-ROUND-489.md](MP-ROUND-489.md).

- Build 488 replaces instant release-facing with a 150 ms default cubic ease-out, using the existing
  grab-bar duration. Target and pivot are captured at release; regrab and external placement
  cancel cleanly. Shared windows retain their existing no-reface ruling. See
  [WINDOW-TURN-488.md](WINDOW-TURN-488.md).

- Build487 closes the five MB486 hardware findings and the discovered legacy-element animation
  refusal. The card visibility matrix and source-vs-log evidence are recorded in
  [MP-ROUND-487.md](MP-ROUND-487.md) and its lane reports.
- Additive53 carries original element hierarchy output,54 binds covered short-rest provenance
  to the semantic flight sequence, and55 carries original mandatory-highlight presentation.
  Record56 adds actual original item-tooltip emitters to the existing native plume stream.
  No existing record grammar or game-state authority changes.

### Earlier multiplayer work

- Explicit short-rest state now uses record 46 independently of sacrifice-seat record 39.
- Remote active-bonus rows now use original serialized game slot and picker prefabs, including
  owner subwidget state in record 47. The giant custom caption and plate widgets are removed.
  Build 486 adds native intermediate values in record 49, original auxiliary slot state in 50,
  actual card particle frames in 51 and original element-board frames in 52. The broader review
  also repairs card/fan motion, native pointer transitions, owner initiative depth and shared
  windows. See [MP-PARITY-486.md](MP-PARITY-486.md).
- Map-room fan exchanges now use the owner's map character key. Equal-sized hands refresh
  immediately; discard/burnt browsers re-emerge on character retargets. See
  [MP-FAN-485.md](MP-FAN-485.md).
- Local and remote card pulse/flight/rest repairs are integrated. The supplied disconnect is a
  confirmed transport receive timeout; its underlying cause remains unresolved.

### 2c. Historical refactor follow-ups

From the 2026-09 refactor's reviews (`.planning/refactor-2026-09/REVIEW-*.md`). These record
previous findings and deferrals; they are not new user-approved exceptions to the current
contracts. Recheck each finding against source and later rulings before implementation:

- **Four records ride the send cadence, not the edge** — resolved in 482 for records 36/39/41/43.
  The remaining question is whether any OTHER record has the same shape.
- **The furniture's materials are never destroyed** (`REVIEW-net.md` N8). Needs an owned-materials
  design, not a minimal fix.
- **`RemoteContentSeconds`** resolved in 486: retained and marked INERT in both languages.
  Received/content edges drive the mirror immediately; a fixed recovery poll is not a content delay.
- **No negative cache in the figure resolver** (N9). Bounded; a retry window would be an invented
  tuning value.
- **The wall fade's `RescanCore` two remaining items**: a write-only field and a dead overload
  that carries the live one's evidence.
- Two holes found while removing the cellar dome, filed with arithmetic in
  `NEEDED-OUTSIDE-cellar-one-sky.md`: the stair alcove is placed from the UNSNAPPED hole while the
  wall is cut to the SNAPPED one, and `BuildShaft` has no floor.
- **A half-applied caption pairing, open since ModBuild 363.** `Cards/Piles/PileViewer.cs` applies
  `NativeButtonSkin.ApplyFont` to the three pile captions but never `StyleWorldReadableLabel`,
  while `Cards/Tray/PlayTray.4.Slots.cs` — the caption whose own doc says it is built to match
  those three exactly, *"the same muted parchment colour, the same native HUD font, and the SAME
  fit box and font ceiling"* — does call it. One line, and it reads as intentional, which is why
  it has survived: it changes how three captions LOOK, so it wants his eye, not a silent fix.
  Filed in `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` §2.

### 2d. Hardware evidence and remaining observations

The build 496 multiplayer logs now measure the native send/transport, section-refresh and
card-appearance instrumentation introduced in 492/493. See [MP-497-PERF.md](MP-497-PERF.md):
board, revision and native-send readings are present; some appearance/transport scopes are
below the printing threshold in individual windows. Compare frame and logic times as well,
without adding nested scopes or equating a missing line with zero work. The short singleplayer
492 run could not measure those multiplayer paths. Four-player scaling is still unmeasured.

`REMOTE BOARD VISIBILITY` records root transitions, but the one-off long-rest disappearance
remains unexplained. Rest-burn appearance and native-material corrections need headset
confirmation; source and timing checks alone cannot establish the picture.

Historical diagnostic watch list (some items date to 480); check the current build and logs
before asserting that a token has never printed:

`Remote BURN look` · `DOCK MIRROR` · `GATE 3` · `NOT ASKED` · `REMOTE GLOW BLEND` ·
`SHORT REST PILE COVER` · `HELD BAR HIDE REFUSED` · `BURN ANIM STUCK` · `BURN ANIM FLAG LATCHED` ·
`MAP STORY SEND RATE` · `MAP PLACARD SCALE` · `WALL COMMIT THREW` (absence is the good reading) ·
`PACKET REJECTED` (zero is the good reading) · `ENV SKY BRANCH` (cellar must read ABSENT) ·
`HELD-CARD EDGE PRE-EMPT` · `GLOVE NORMAL TAMED` (**gone** — the glove value is baked now, so
there is deliberately no line; the proof is the picture and the bundle size).

**Giant orange text identified:** MB482's census names the 19.87 m
`Furniture/UseBarsDrawer/UseBar0/Caption`. That replica was removed in 484. Confirm the original
widgets, their pickers, and their size in the next headset test.

---

## 3. Standing rulings that are easy to break by accident

The full set is in `CLAUDE.md`. These four have each been broken at least once *after* being
written down:

1. **Visibility, including the later MB490 user clarifications (2026-09-09):** concealment
   applies only to remote presentation in scenarios. Local controlled-character cards are
   always open, including short-rest flights. The entire 3D map is public, locally and remotely.
   In scenarios, remote action cards and action-phase damage sacrifices are open; remote
   ability-selection fans, held and placed cards are covered. Remote short-rest burn flights
   remain covered. This supersedes older pile/active-held exceptions and local concealment.
   Resolve actual model membership before delayed widget CardType; an unresolved positional
   address must never guess a card identity.
2. **Seeing a card's FACE and being allowed to NAME it in a prompt are two questions**, over one
   population. Merging them re-opens the ModBuild 477 identity leak.
   `scripts/check-card-identity-mask.py` fails the build if they become one predicate.
3. **Historical localization behavior is documented in `Core/Loc/Loc.cs`:** transported text
   uses the sender's language; locally resolved keys use the viewer's. An implementation comment
   alone does not establish a user-approved exception to visual parity; follow `AGENTS.md`.
4. **The options button opens and closes the pause menu and touches nothing else.**

---

## 4. Subsystems with a closing account — read it before you touch them

| subsystem | read first | why |
|---|---|---|
| wall fade | `.planning/perf/WALL-FADE-CLOSEOUT.md` | closed on hardware; every dial is settled and the instruments that lied are listed |
| multiplayer 1:1 | `.planning/multiplayer/DESIGN-1TO1-RESIDUE.md` §6 | historical closeout; later parity rulings and build reviews still apply |
| the 2026-09 refactor | `.planning/refactor-2026-09/BRIEF.md` + the five `REVIEW-*.md` | what was found, what was deferred, and what the tooling could not see |
| static batching | `.planning/static-batching-removed.md` | tried and completely removed by user ruling |
| per-eye fade rivalry | `.planning/wall-fade-stereo-rivalry.md` | parked; unfixable on the game's masonry shader without losing the dissolve |

---

## 5. The shape of a round

1. Read his German report. Take the **symptom** as data; re-derive the cause.
2. Land shared contracts, then delegate independent tasks on **disjoint file sets** in separate
   Git worktrees created from current `dev`. Initialize dependencies with `worktree-setup.sh`,
   respect the session concurrency limit and never overwrite a shared baseline symlink.
3. Review every diff. Restrict each merge patch to the lane's OWNED paths.
4. Apply the cross-lane `NEEDED-OUTSIDE-*.md` items yourself.
5. Bump `ModBuild` **once** for a changed runtime build handed to a player, with actionable
   build notes. Documentation or packaging-only preparation does not change compatibility.
6. Run the three gate commands from `CLAUDE.md`. Regenerate `docs/PATCH-INVENTORY.md` once, at
   the end, if any patch class moved.
7. Push to `origin/dev`.
8. Write him a German report: what was found, what was fixed, what he must judge, what is owed.
