# Frame follow-up audit and defaults (2026-10-06)

The new Frame run starts VR successfully and completes an allocation-free live
resolution change to 0.80. With one revealed room, settled application frames
average 46.8–48.7 ms; with three, 98.0–102.8 ms. Terrain substitution maintenance
is the largest measured mod cost: approximately 10.5 ms per frame becomes 38 ms.
The supplied screenshots also show conspicuous dark smoke around the torches.
They do not establish its exact shader or blend failure.

## Immutable evidence

Inputs live in the main checkout at
`.planning/debug/frame634-review-20261006T183809Z/inputs/`; all 11 entries in
the adjacent `manifest.json` were independently verified against byte size and
SHA-256. `L`/`P` below denote steam_frame/LogOutput.log and Player.log. Rows are
one-based **raw LF** rows (`bytes.decode(...).split('\n')`); embedded CR does
not start another row. Player retains Debug detail absent from LogOutput.

| Current Frame input | Bytes | SHA-256 |
| --- | ---: | --- |
| LogOutput.log |10114866 | `3cc57a0e2d87bef8b4c9f20fe279a930e699282de2a98ab347402922cc55453b` |
| Player.log |10639659 | `f9a231032414b51063f6f3811539b2cf6fcd8486df7ca10108da5eb42bf6e181` |
| openxr-diagnostics.log |110459 | `0d9244cca6c3b1152ec748b8c703c0220d88541bc3b7322df6001283eb7da435` |
| 20261006202703_1.jpg |174269 | `13b7077b424d6da4160cc0efa052725189ded23b91436f53afae627dccdfe967` |
| 20261006202813_1.jpg |174090 | `463114d8a1f9b687d26d20bb9add2cf299eed87bbc6d187bd34e0103ecfeed71` |

Both screenshots were inspected. The first is a close view of two enemies and
a torch-lit door; the second shows the larger board, several characters and
multiple smoke clusters. The dark, jagged smoke shapes are visible in both;
the room geometry remains visible. A static screenshot cannot prove frame
smoothness, eye-to-eye parity or the cause of the smoke artifact.

| Capture | LogOutput build/commit rows | Player build/commit rows | Status |
| --- | --- | --- | --- |
| Frame |17/73: 633 /902fa3a3b |40/155: 633 /902fa3a3b | Current run, VR running |
| PC |17/71: 631 /674ea6bc4 |40/154: 631 /674ea6bc4 | Identical to the previous PC audit; stale for this comparison |
| Remote |17/71: 629 /e6ecf3d45 |40/156: 629 /e6ecf3d45 | Older capture, not a current 633 peer |

Do not interpret these inputs as a paired current multiplayer test. The prior
PC settings analysis remains [FRAME-631-PC-SETTINGS-AUDIT.md](FRAME-631-PC-SETTINGS-AUDIT.md).
The worker starts from dev 8b1c0a048, with Build 633 source, and introduces later
defaults; those changes are absent from the captured binary.

## Resolution: actual success and earlier drift

The session allocates 3408×3408 per eye at scale 1.00, D3D11/MultiPass/Forward,
MSAA 0 (L182). Saved 0.85 is initially applied: both native render parameters
report 0.85 at L184/186 and L356/358; API readback agrees at L189/361.
Later, the request remains 0.85 but **both** native viewports and the API readback
are 1.00 (L824/826/829 and 1086/1088/1091). This is a measured loss of an earlier
accepted viewport, rather than evidence that the original request never worked.
The capture does not identify which native/runtime writer reset it.

Build 633 `ApplyEyeScale` returns when `_lastLoggedEyeScale == wanted` without
checking actual viewport drift. That source path cannot repair an unchanged
saved request after an external reset. Reassertion should compare current
readbacks, retain quiet/bounded refusal behavior and avoid any allocation
fallback. The same gap matters to the new 0.80 startup default.

The explicit Fastest selection (L4029) requests 1.00 and logs its restoration
at L4044/4045. The later edit is effective: L5035 requests 1.00→0.80;
L5036 reports accepted API readback; L5067/5069 report native 0.80 viewports
for **both** eye passes while the targets remain 3408×3408. L5071 reports a
36% smaller pixel-sample budget; L5072 estimates 2726×2726 rendered pixels per
eye. The late three-room TEX readback still uses 2726×2726 (P12379).

There are no XRTextureManager create/setup lines after this live edit in
Player; its 12 resource lines are at startup P316–577. Subsequent measurement
windows continue through L8279. This run therefore supplies actual evidence
that the tested live change survives without the previous reallocation sequence.
The captured tail remains gameplay, not a shutdown/crash dump; it does not
certify every slider sequence or native driver stability. No FOV/camera crop
change is inferred from these pixel-work estimates.

## Fully loaded performance and remaining work

The native scenario spinner is released after 82.10 s (L1637/1638). Loading
windows and later cosmetic preparation are excluded: L1895/L3103/L4923 still
include interaction/card preparation. Room registry edges are 0→1 at L1153 and
1→3 at L6803. The later room-load fade ends at L6927 and figure revision 5
becomes steady at L6929.

| Revealed rooms, effective viewport | Settled FRAME row | Mean /p95 ms; mod ms/frame | Terrain.PreCull /WallFade.Late /Environment.PreCull ms/frame |
| --- | --- | --- | --- |
| One, 0.80 | L6442: 27.1 s /579 frames |46.83 /74.54; 25.33 |10.464 /2.720 /1.327 (L6443) |
| Three, 0.80 | L7279: 10.1 s /98 frames |102.75 /137.03; 67.78 |36.639 /8.846 /3.678 (L7280) |
| Three, 0.80 | L7753: 30.0 s /296 frames |101.83 /132.20; 67.15 |38.032 /7.197 /3.829 (L7754) |
| Three, 0.80 | L8119: 20.1 s /205 frames |98.04 /129.79; 62.80 |37.742 /8.417 /3.750 (L8120) |

One-room adjacent steady windows L5651/5805 measure 47.45/48.72 ms. The later
three-room tail L8279 remains 98.27 ms. These are approximately 21 application
frames/s versus 10, far from 72 Hz. Reported adaptive display rates and the GPU
counter pinned near the frame interval are not independently measured GPU busy
time. Views differ and the earlier 1.00 windows include preparation, so this is
not an isolated FPS A/B for 0.80. It does establish persistent post-load pressure
with the smaller effective viewport.

The reductions already work: L7756 counts 26509880 original→9889596 submitted
terrain triangles across surviving paired-camera leases (**62.7% fewer**),
141010 cheap-surface leases and 84960 chunk sources→18880 groups. These totals
are not unique visible scene geometry or GPU draw calls. Explicit instance
sources/groups remain zero. P12222 reports 144 source renderers/32 prepared
chunks. L6766 reports 16 actors, 27 disabled cloth solvers, 112 masked optional FX
renderers, 88 paused particle solvers and admitted body vertices 202930→50378.
The main bottleneck remains despite those delivered reductions.

The strongest source opportunities are in terrain pre-cull: every active
prepared surface is validated/material-routed before any frustum broadphase;
`PrepareProxy` reads source/common-owner matrices, about 19 renderer-state
properties and renderer-wide/per-slot property blocks on every eye invocation.
The three-room counter reaches 478 cheap leases per application frame. Reusing
exact common values only within one synchronous invocation, rejecting safely
off-frustum originals before expensive proxy work, and optionally limiting how
many native sources receive substitutes can reduce this maintenance. Retained
originals must still render; no room may disappear. Keep current native clone,
material, probe, gameplay and property-block vetoes.

Scenery.Update also costs 4.852 ms in L7754 (5.946 ms at L7280); investigate
continuing finished-mask maintenance. Environment pre-cull is smaller, about
3.8 ms. WallFade.Late is an inclusive parent: do **not** add its nested Decide,
Pipeline or FastReclaim values to it. The logs measure these parent costs;
they do not isolate the cost of individual Unity API calls or establish the
net GPU effect of a new cap.

## Shader/particle inventory and source changes

P12377/12378 are an incremental census spanning 115.11 s, not an instantaneous
frame or a shader timing profile: 2366 active renderers, 845 enabled/visible/in-mask
candidates, 865 submitted material-slot candidates, 486 property-block renderers
(124 candidates), 119 distinct submitted material instances. Mesh candidates 769,
skinned 53, particle 21. Dominant shader slots are Amp_Basic_N_MRAO 730/1464;
VFX/ParticleMasterUnlitAdd 20/25 and KriptoFX distortion 4/4. There are 92 particle
systems, 21 playing, 20 emitting and 159 live particles. Counts do not prove the
smoke's cost or identify its material. Restrict any corrective smoke option to
verified visual parts; native combat completion may depend on IsAlive().

The requested default is implemented through two actual selectors:

- Fresh Frame config: `RenderQuality.Bind` selects `FrameDefaults.EyeResolutionScale`,
  now 0.80, when the Frame marker is active. Existing BepInEx entries retain saved
  values. Ordinary fresh PC `Defaults.EyeResolutionScale` remains 1.00.
- Explicit Standalone/Fastest action: `GraphicsProfiles.Apply(0)` requests 0.80.
  Simple/Good/Fantastic explicitly retain 1.00; changing the shared Frame constant
  alone would previously have altered all four actions. A later personal edit
  remains independent until another profile action is chosen.

The follow-up terrain source-limit default is 64 for fresh Frame/Standalone;
ordinary PC/default quality actions retain 0/unlimited. Overflow remains native
original scenery; this is a selectable CPU/GPU trade, not a hiding quota. Root
owns its binding/UI and the separate terrain worker owns runtime enforcement.
The external Quest worker was notified before shared edits; its clean handoff
uses shared FrameDefaults for the eventual Quest merge, with no separate eye
override reported. No Quest branch or archive was edited.

Checkpoint e4c350534 passes 256 existing profile-boundary assertions and
source-bound fresh Frame/PC selection checks. Cap checkpoint 7efaa651e combined
with root binding 7584a154a passes 265 assertions (four UI actions/nine retained
INERT controls also checked). Saved-file preservation uses the unchanged
BepInEx Bind contract; this fixture does not simulate real persisted config files
or native XR rendering. Hardware verification of the new defaults/cap and a
current paired multiplayer run remains pending. No full 146-suite claim is made.
