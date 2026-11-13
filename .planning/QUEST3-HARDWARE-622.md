# Quest B622 — actual XR screen samplers and bounded discovery ticks

## Verified B621 hardware evidence

`quest-capture-20261004T201420Z-fc2afb8a.zip` is verified against installed
APK bytes/hash, input, installation receipt and the actual B621 startup banner.
Capture SHA-256 is
`5bcaf8f056adbbf1a65015d36a2fd19b20ecaec41cca4f0b1e7205716dff7abc`.
The reported invisible Intro, absent ambient menu imagery and periodic hitches
persist. The ZIP supplies no new screenshot; the older B619 screenshot is not
attributed to B621. Previously confirmed audio, keyboard, Guildmaster explanation
and Quest options exclusions remain the baseline.

Decoded Intro pixels are nonuniform, and completed native capture and actual
FlatScreen consumer samples have identical hashes. At decoded frame 22, consumer
RGB maximum is 237; at frame 52 it is 209. The real consumer still uses
`Hidden/BlitCopy`. Unity 2021.3.5f1 changes this shader's screen-space source to a
texture-array sampler under the actual Quest instanced stereo configuration,
although FlatScreen owns ordinary left/right Tex2D captures. Plain desktop
sampling tests did not exercise this draw configuration. The original ambient
`Ambient_04_Cave_02.mov` also decodes and reaches the consumer. Its first frames
contain an authored dark fade, verified against original media; early black pixels
are not evidence of a decoder failure. The separate native promotion trailer is
intentionally idle and must not be forced to autoplay.

The latest 24 retained large frame spikes have median duration 102.47 ms and
median spacing 1.009 seconds, without GC increments. Scope discovery measures
81–94 ms during this same menu state, with 14,338 inspected scene behaviours.
This identifies a substantial periodic contributor, without excluding all other
work. Async pixel callback latency is not a main-thread CPU measurement.

The actual hidden Close Area has CanvasGroup alpha zero. B621 UI right-centre
pixels are transparent. A real CanvasRenderer GPU control confirms that alpha
zero suppresses the original blur even with `cullTransparentMesh=false` and
`cull=false`. Those flags therefore do not establish a hidden opaque occluder.
Authentic shader recovery is evaluated separately; native visibility is retained.

B622 also restores that attached original blur from its five audited D3D programs,
retaining three successive Grab/Draw pairs, nine-tap axes, homogeneous distortion,
original uniforms/defaults and opaque render states. Genuine Android RGB and ASTC
normal imports use the proven Unity encoding branches. Twelve real Canvas cases
include three rendered defect controls; positive maximum pixel error is 0.00347.
Native gates compile four GLES banks and keep separate source/Android receipts.
Recovery does not establish that this blur caused the invisible Intro or preview.

## Scoped candidate

The current shared mod selects the Quest world-screen shader only through its
central standalone gate. Both source captures remain plain 2D samplers, while the
vertex/output stages handle instanced or multiview headset layers. The GPU selects
left/right inputs for the same native depth policy. Mono, map and suspended-video
transitions reset both bindings and stereo routing on the persistent material
before released captures can remain referenced. Desktop keeps its existing shader
and per-camera eye swap. No additional full-size targets or copies are introduced
by this screen-draw correction; inherited completed-color restoration cost remains
a hardware measurement.

Actual two-eye GPU controls cover the old array-sampler defect, wrong eye routing,
alpha and persistent-material transitions. Android compiler validation requires
three real banks (mono, instancing, multiview), six vertex/fragment stages, plain
capture samplers and vertex eye selection. This proves the source/compiled path,
not the next visible headset outcome. Debug pixel observations retain bounded
sampling and include the actual URL/right-eye binding; the second far-video sample
waits past the authored initial fade.

Scope discovery must retain inactive, late-created, pooled and persistent native
widgets, including live `DontSave` owners. Exact typed queries preserve those
semantics and avoid repeating managed classification over every scene behaviour.
Seven batched typed queries were slower in local Mono (12.15 ms full scan versus
6.00 ms original), so that burst is not shipped as a demonstrated optimization.
Absolute one-second deadlines stagger the seven known families. Initial binding
and catch-up after low FPS or clock jumps can query the bounded full set; ordinary
frames query one family. In a genuine 14,346-behaviour Unity scene, mean production
tick is 1.6276 ms and worst tick 1.6947 ms; whole-cycle mean remains 11.3935 ms,
versus 5.8093 ms for the original full scan. Initial batch is 11.4626 ms and stalled
catch-up 11.4414 ms. This reduces the local frame burst while retaining the larger
whole-cycle cost honestly. Ten defect controls and 316 real-engine assertions
cover discovery/cadence, all seven families, inactive late AddComponent, prefab
exclusion, DontSave/DDOL, unload and clock jumps. Existing tracked widgets retain
per-frame enforcement and native callback/availability behavior. Debug state
records query-family count so batch and ordinary-tick measurements are distinct;
its legacy component count now denotes matched owners. Android frame-time
improvement remains a hardware gate; local timings cannot establish it.
The engine's per-thread allocation counter fails a known 8 KiB control; allocation
values are explicitly unsupported. Rounded retained-heap deltas of zero do not
establish zero per-query allocation. Managed scope controls and 26 original SDK
ABI assertions pass; preservation is supplied by the existing generated
`GH.Runtime preserve="all"` linker policy.

This is still an original startup/menu slice. Full campaign assets, actual DLC
scenario recovery and authenticated Android crossplay remain separate port work.
Jaws of the Lion and Solo Scenarios ownership remains selected for this private
test, with the clearly labelled dummy identity.

## Hardware run

1. Extract the complete B622 Windows archive and run `Install-Quest.cmd`. Confirm
   embedded and installed B622. Retain app data and existing wireless settings.
2. Check Intro picture with sound, then the ambient menu background in both eyes.
   Report orientation, stereo depth, grey output and foreground overlaps separately.
3. Leave the menu visible for at least 60 seconds, then repeat in mixed reality.
   Report the recurring small hitch and collect with `Collect-Quest-Logs.cmd`.
   Supply its ZIP and screenshots of remaining image defects.
4. Repeat a warm launch without clearing app data. Compare native Intro-to-menu
   timing and any genuinely necessary preparation with the first launch.
5. Briefly recheck keyboard input, Guildmaster hint, Quest options exclusions and
   sound. These were already hardware-confirmed and should retain their behavior.

## Native candidate validation

Signed ARM64 IL2CPP build succeeds from clean native freeze
`4c10c034ad97b39971269e0031799bd45d4ec9cd`, input
`9d148c7036f2c08ece6c4f0518873a51a91faf62b4c75e30aa2c739ede946a00`.
APK SHA-256 is
`dd61d996dc7502c838fdc9518f48a1343ac13db5a9701146bb519d36b72becb0`,
size 2,587,919,101 bytes. Android code is 622; incremental GC remains enabled.
All 957 current mod C# files are selected automatically. Actual native compiler
gates verify six world-screen stages, two camera-video stages, four original blur
banks, the retained original UI banks and 36 legacy post-effect stages. Compiler
banks are distinct from variants retained in the baked Android resource.

Twenty-one final affected Quest suites, fourteen source checks, strict Release
with zero errors/warnings and 286,760 direct unchanged protocol/golden assertions
pass. The loading-view test initially lacked Shader/Material type seams for the
new central helper; its corrected affected rerun passes. Initial failed receipts
remain archived. This test-only change does not alter native frozen application
code. No unrelated complete wrapper gate is claimed.

The independent native scope proof verifies seven preserved original owner ABIs,
28 actual ARM64 functions, source/CIL/generated C++ and exact compiled/package
library bytes. It records 531 assertions plus archive CRC, native Build ID
`9a2274f00b636520` and packaged library SHA
`8d9be364f18f03de45743afb14c7d4afc88c93914eb58fcdf710854e10bb3e93`.
Only actual method equalities are claimed; player lowering differences and the
native `Math.Floor` intrinsic are recorded explicitly.

The independent video/screen audit adds 883 recorded assertions plus archive CRC,
41 actual compiled functions and finished-APK ResourceManager-to-Shader parsing.
Its method set is disjoint from the 28 scope functions: 69 distinct functions and
1,414 combined boundary assertions are verified, with two separate archive CRC
checks. All 27 selected mod bodies match import-to-stripping; 14 player bodies
have recorded lowering differences. All 41 stripped bodies match the native
backup. Actual call sequences are compared individually; the pixel probe's
compiler-generated display-class constructor is recorded explicitly.

The actual APK contains `Hidden/GloomhavenVR/QuestWorldScreen`, two Tex2D capture
properties, stereo routing and GLES mono/multiview programs across three hardware
tiers. Parsed fragment sources use plain 2D samplers and the stereo vertex uses
`gl_ViewID_OVR`. Instancing succeeds in the compiler preflight but is not retained
as a third bank in this GLES resource. Do not conflate compiler coverage with baked
variants. The original camera shader is also resolved through the finished APK's
resource index, and parsed shader bytes match the generated player cache.

The verified Windows ZIP has SHA-256
`2552a84e172bffdbdee17eed9fc0cca66a710e9a71f1e64954f184accb38fd39`,
size 2,588,037,106 bytes. Complete CRC/content/hash checks and isolated installer
selection of embedded B622 pass. Its clean installer/document source is
`2f361f2d`; the native freeze remains `4c10c034`. Later commits record completion
only. The archive's procedure reflects its packaging-time pending resource/cleanup
state; final completed receipts are recorded here without changing the delivered
archive bytes.

After matching B621 symbol retention and compact source/diagnostic archival,
three completed Quest worker roots and the obsolete B621 generated project are
removed. Measured additional free bytes are 36,452,593,664 for those roots and
7,735,312,384 for retired downloads/staging, totaling 44,187,906,048 bytes.
The handoff directory contains only the current APK, Windows archive and receipt.
Git refs, canonical inputs, current and historical native evidence, supplied
captures and parallel dev worktrees remain untouched. This candidate is pushed
only on `feature/quest3-standalone`. No observed B622 headset result is claimed.
