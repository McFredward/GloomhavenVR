# Native menu discovery workload

`python3 scripts/menu-depth-perf/run.py` compiles the complete production menu-depth
sources against actual Unity2021.3.5 and GH.Runtime, then executes real Playmode
frames. The surrounding session/configuration and converted-panel roster are
fixture inputs. Original UIWindow registration, native typed owners, Image and
RawImage lifecycle/texture setters, manager private fields and native cached-model
`Character3DDisplayManager.Display` execute. Native game-scene startup and Beautify
shader initialization are excluded explicitly; no gameplay scene is available.
No fixture discovery notification or binding-result substitute is supplied.

The heavy population contains13 converted roots,39 original UIWindow instances,
24,531 UI behaviours,2,808 unrelated RawImages and1,131 typed decorative images,
plus the real native model camera and3 of its actual RT consumers. Timing disables
unrelated canvas rasterization and automatic model-camera rendering; production
animation clocks and camera behavior are unchanged. Each actual frame times the
complete production Driver.Tick. A source-bound Harmony observer independently
counts real structural discovery calls. Cold work and functional lifecycle tests
are excluded from both steady timing populations; those populations are rebuilt
identically before measurement.

The comparison compiles the actual shipped Build724 commit `c5f70993d`, without
porting its scheduler. One retained local run (`run-jmobrqe3`) measured:

| Metric | Build724 | Repair |
| --- | ---: | ---: |
| Frames / elapsed |263 /3.0001s|270 /3.0000s|
| Driver mean CPU |1.8632ms|0.4768ms|
| p99 CPU |17.8348ms|0.7254ms|
| Maximum CPU |18.4303ms|0.9911ms|
| Root hierarchy censuses |312|0|
| Global discovery passes |12|0|
| Global result arrays created |24|0|
| Steady behaviour/ancestor walks |periodic original discovery|0 /0|

The repaired run passes833 assertions (23 fixed and3 per actual sampled frame),
the baseline3, and all9 compiling causal controls. Timing-frame assertion counts
vary with observed frame count. Restored periodic hierarchy/global work is caught;
so are omitted native graphic births, same-canvas ancestor reparenting, late
RawImage/camera targets, repeated null-target requester scans, missing first-draw
pump and inactive cold consumers. Component-only UIWindow destruction is checked
while retaining its live Transform and other inactive pools. Real image mesh
vertices establish shallow relief, rather than counting component creation alone.

The Mono thread-allocation API is calibrated with a16KiB managed allocation.
This editor returnszero even for that known allocation. Byte totals are therefore
reported as unavailable (`-1`), never as zero allocation. Actual global array
births remain observable through the executed, pinned discovery body.

## First-draw delivery boundary

The named `first-draw` and `automatic-first-draw` cases retain hard pixel checks.
They do not insert another Driver.Tick after late native Display/texture readiness.
An initially empty source registry must prepare and restore the actual native eye.
The enabled-head case runs the actual automatic engine render, with a native
OnRenderImage sentinel keeping mono output blue while owned eye output remains
red. It checks those textures independently before assessing the head raster.

Before the stable-left early-prime repair, registration and native eye generation
passed but final direct head delivery failed under the supplied Unity/GL profile: native mono4096blue/0red,
owned eye22344red/0blue, final head15600blue/0red (`run-5h5spi3h`). This is preserved
as a failure, not a production pass or a headset claim. The explicit actual
BeforeCapture-before-Camera.Render discriminator passes all4 late readiness /
HDR-LDR combinations (18 assertions; `run-st0f4vzf`). Private global/local rebuild,
scoped cull/rebuild and post-render restoration probes do not repair late direct
head delivery. Renderer.cull is false before and after source rendering. These
narrow source-copy probes are labelled separately and never modify shared code.

All original media, mesh/UV/material/reveal behavior is separately verified by the
unchanged full graphic/native-mip suite:582 assertions and14 causal controls in
`run-irefsssu`. CPU fixtures do not establish XR/headset appearance or comfort.
Every standalone run stops/reaps exact owned editors and descendants, tears down
its compiler processes, then invokes the central cleanup_completed_run API;
source copies, assemblies, logs, results and pixel witnesses remain retained.

The historical discriminator (`--known-ready-724-probe`, `run-saxig801`) compiles
all original Build724 menu sources and registers its source before the automatic
head frame. It retains1 known-ready source, produces600red/0blue owned-eye pixels,
keeps mono4096blue/0red, and draws head15600blue/0red. Exact expected failure is
recorded as a historical negative control, with source callback and restoration
checks reached first. The observed direct GL delivery defect therefore predates
the discovery repair. No D3D11/headset outcome is inferred from this engine proof.


## Stable direct batch repair verification

With capture checkpoint `f76629dc3` and authored calibration `ad5129cc7`, the full
14-case invocation (`run-ghxti3hc`) passes833 CPU assertions,3 exact724 comparator
assertions,18 manual first-draw assertions,9 actual automatic first-draw assertions
and all10 compiling causal controls. Manual head cameras now follow the actual
active-head contract before their same-update explicit render; the four cases
retain both LDR/HDR and late Display/texture readiness without another Tick.
Automatic mono remains4096blue/0red, owned eye22344red/0blue, and final automatic
head becomes5192red/0blue, with exact native target restoration. Omitting only the
early Canvas prime restores the hard old-blue head failure.

The repair primes a stable existing left RT identity before native canvas batch
construction, without rendering a model there. Each actual head eye refreshes
that same surface with its current projection. No new shader, material, image
copy or global Canvas rebuild is added. Read-only review verifies that borrowed
identity is established before texture notification, priming skips active capture
reentry, dimensions stay locked through a primed frame, maintenance skips active
loans, and end-of-frame/stale-frame cleanup restores orphaned loans.

The final run occurred alongside the complete integration gate: steady CPU mean
2.7540ms→0.9188ms and p9921.5491ms→1.4099ms, with zero recurring structural work.
The earlier unloaded comparison above remains a separate, correctly scoped
measurement. Neither is a hardware/VR comfort claim. All final26 input pins were
unchanged; exact Editor/descendant and compiler teardown preceded cache cleanup.
