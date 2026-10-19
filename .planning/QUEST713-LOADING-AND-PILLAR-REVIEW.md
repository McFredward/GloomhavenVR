# B713 loading, pillar retirement and ping regression

## Actual current evidence

The capture `quest-capture-20261019T165519Z-f0b6a59c.zip` binds installed and
running B712 to input `d4e76da00ec2`, APK SHA256
`ad39f18ab0d639473bcda7951bd8779bdb0be6ca3aae93ba4091fcb6e2d35877`.
Its startup is not B711. Historical crash entries in its retained app history
are not new crashes from this run. No new screenshot accompanies this capture.
Source, logs, causal tests, snapshots and cleanup receipts are retained under
`/home/claw/quest3-local/build/evidence/quest713-loading-review-20261019/`.

The spinner arms at16:49:29.263/frame961; unified scene activation is16:49:33.016.
At16:49:47.229/frame1226 its own renderer is active with base alpha1 and an
actual current-head admission from frame1225. The menu scene activates16:49:59.211;
first menu sample is16:50:03.320; full spinner hide is16:50:03.364/frame2044.
This rules out describing the entire reported gap as an early state flag alone.
The source Sprite atlas/UV/shader match the working later scenario spinner.
Admission and material state are not final XR pixels.

## Loading findings and repair

The new B712 continuity/owner facts were dropped by an already-saturated general
presentation log lane. A dedicated finite reserve now retains these messages,
without increasing the256KiB global startup sink limit. The actual B712 replay
retains all five previously lost continuity facts after the repair.

The B712 menu receipt admitted a Graphic and OnWillRenderObject before proving
fragments. Controlled actual Unity rendering reproduced retirement with a
transparent native image, offscreen image, a later target clear and black actual
consumer material. These are real final-camera pixel checks, not state-only
assertions. A fifth case exposed the first attempted repair: unrelated visible
UI in the same capture could impersonate a transparent menu candidate.

The initial handoff now qualifies an actual original menu Image's Sprite geometry,
texture and material in a tiny private target, then requires two nonblank actual
capture-consumer draws with fresh, matching owner/head/target/material identities.
Unity2021 CanvasRenderer does not expose a mesh getter: Image.OnPopulateMesh is
called only to obtain a private eligibility mesh; its renderer and native flow
are untouched. This source eligibility is not labeled actual CanvasRenderer
submission. Foreign version UI alone cannot satisfy it. Authored material tint
is retained. Blank native decorative candidates are temporarily skipped so a
real visible native Image can qualify. Stack release invalidates pending proofs.
The small asynchronous probes run only during the initial handoff, outside head
render callbacks, with no full-eye readback, wait or native continuation delay.
There is no steady-state menu polling after successful handoff.

Five existing bounded Debug continuity points also sample the actual spinner
mesh/material through the same private-camera helper. These logs distinguish
source draw loss from a remaining composition problem; they explicitly report
`finalXrPixelsObserved=false`. No speculative shader, eye viewport, captured
camera clear, native Sprite atlas or global rendering change is included.

The fixture now uses original UIWindow.Show, native Bootstrap/Intro completion,
Single scene activation and unused-asset unload boundaries. Original menu
controller initialization remains controlled; it is not a full game launch.
It retains original imported Sprite metadata and compiles the complete production
probe file. Its real native-host final camera is not the Quest compositor.
The complete reported hardware black interval remains unverified until the next
headset capture: software regression closure is not a claim of final eye pixels.

## Pillar removal and package boundary

The history-before-removal checkpoint is shared dev `f15e66459`; the removal is
`28f5371df`. See [full PC/Frame/Quest history](retired-PILLAR-LOD-20261019.md).
Native pillars are no longer admitted to ScenarioTerrainBudget. Radial and
figure-derived distance selectors and their visible settings/profile writes are
removed. The old bound configuration key is hidden and inert for old files.
Figure LOD and ordinary wall detail remain. Exact100 pillar originals still
serve the independent environment chunk renderer; deleting those would remove
shared room geometry rather than only the retired feature.

All261 reduced pillar streams and261 corresponding metadata files are removed.
Their raw size is14,396,885bytes. The actual rebuilt Windows environment bundle
shrinks56,858,683 to52,361,903bytes, a4,496,780byte reduction. Other shipped banks
contain no environment mesh streams and remain unchanged. The Quest producer
also emits only exact100 for pillars and prunes obsolete50/0 staged files.
An update changes managed/native mod code and the small mod environment art;
original Campaign conversion, original shaders and movies must remain reusable.
The game-free Builder archive never contained the original-derived desktop bank;
its source recipe now excludes these derivatives from future owned Quest banks.

## Ping regression

B712 replaced the original two ParticleSystemRenderer draws with BakeMesh proxies.
The native prefab's cullingMode is Pause. Real automatic Unity playback reproduced
both source renderers becoming invisible and their clocks freezing, while native
controls advanced and emitted the later rising particles. The old manual Simulate
fixture hid this defect. The failed proxy replacement is removed; native particle
rendering, culling and simulation remain active. The earlier narrow vertex-stream
request is retained. This restores the native animation path; the separate Adreno
hex-field artifact is not claimed fixed without headset evidence.

## Validation scope

Focused production/native rendering, causal controls, actual environment-bank
loading, source gates, strict compile, update-source classification and extracted
release smoke results accompany the delivery. No full original import, shader
recovery, depth/movie generation or whole-game APK build is required locally.
Unchanged broader multiplayer/gameplay evidence is inherited, not reported as a
new complete local gate pass. Test process trees are reaped before shared cleanup.
