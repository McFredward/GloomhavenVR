# Build725 menu performance and character presentation

## Hardware report

On 2026-10-27 the maintainer accepted the story illustration depth in Build724.
Character previews remained too large and too far behind the window, and became
unrecognizably pixelated when approached. The desired scene sits around the pane,
slightly protrudes and has an appropriate model size. The priority failure was
strong pulsed joystick-forward movement on the maintainer's powerful gaming PC.

Both fresh local logs identify Build724, commit `c5f70993d`, built on
2026-10-27 09:34:53 UTC. The runtime is SteamVR/OpenXR MultiPass, D3D11 on an
RTX4090. Reported eye targets are 4040 by 4040. The remote logs still identify
Build717 and do not describe this test. No new screenshot was supplied.
Byte-pinned inputs are retained under ignored `debug/menu-depth725/hardware-build724/`.

## Measured performance evidence

The log contains 509 reported frame spikes, 449 of which name at least 40ms in
the `MenuDepth` late step. Those samples have median 52.95ms and maximum 85.57ms
inside that step. The earlier first catalogue-load window also records a
one-off 119.49ms; 85.57ms is not the maximum over every loading window. The last
performance window reports 4.720ms average and 85.57ms
worst for MenuDepth. Normal frame median is about 11.12ms; representative motion
windows have logic p95 near 65ms. Reported runtime GPU values are not usable GPU
busy-time measurements and are not treated as such.

The Build724 source repeats full window/panel behaviour discovery and two global
include-inactive manager/RawImage searches on a quarter-second timer. It rebuilds
typed bindings with repeated linear searches. That structural work continues in
ordinary gameplay even when no character preview is being examined. The direct
CPU measurements identify a concrete frame-pacing regression; supplied logs do
not independently measure every joystick input gate. Of the 449 hot spike rows
with GC samples, 447 report no collection. The repeating stall therefore generally
is not a garbage-collection pause.

Player.log contains 28 Debug locomotion samples (LogOutput.log contains none).
Fifteen have active requested/applied flight and focus, including frame8468 with
66.3ms scaled and unscaled delta time. They report no zero-scaled requests or
competing world-grab/reel/scroll. These sparse observations support frame pacing
as the measured failure without claiming a complete input trace. The retained
`locomotion-observations.json` and `performance-distribution.json` record the data.

The flight path itself still integrates the selected direction with unscaled
delta time. The repair must remove unnecessary frame work while preserving
ordinary movement, native animation, input authority and shared window geometry.

## Presentation and capture findings

The Build724 character portal uses a pane at 0.85 times the native holder distance while
retaining the authored off-centre model scene. Its eye textures simply copy the
native 1024-square render target. Fixed low sample density and deep model placement
can both reduce useful model pixels as the viewer approaches.

Supersampled windows can also draw their automatic mono camera before two explicit
eye captures. A display component can remain enabled while its parent display is
inactive, so checking only the component's enabled flag admits unnecessary hidden
capture work. The repair must give stereo and mono capture explicit ownership,
retain immediate fresh mono recovery and use bounded physical sample demand for
the character's own targets.

## Implementation and validation

Three workers own discovery/lifecycle performance, model fit/resolution, and
capture ownership/physical sampling in disjoint worktrees based on current dev.
The primary agent reviews and integrates their checkpoints into dev.

Discovery retains one cold census of existing inactive native pools and each new
converted/window scope. Typed bindings persist across ticks. Original Image,
RawImage texture, typed-owner initialization, parent/canvas changes, windows and
character-manager arrivals coalesce changed identities. Small native registries
and the few known GUI camera targets remain live; elapsed time never triggers
another global manager/RawImage or window-subtree scan. Inherited Harmony targets
are deduplicated by RuntimeMethodHandle. Destroyed UIWindow components retire even
if their Transform survives. Original cached Character3DDisplayManager.Display
executes unchanged, including its request/refcount authority.

An active-driver pending-work hook stays registered even with no depth sources.
It runs before capture admission, outside leases, with reentry and failure guards.
Stable queries inspect only known managers/sources and pending sets; they do not
repeat the full typed binding loop or enumerate UI descendants. Real native
arrivals after the last LateUpdate do not wait for the old quarter-second timer.

Character calibration uses the completed native `character3D.models` membership,
including companions and only active/enabled renderer variants. Authored skinned
local bounds and imported bind poses for rigid and skinned bone-mounted equipment are sampled
once per native model identity and retained in a bounded cache. Animated world
bounds and a viewer's initial pose do not establish the fit. Native model, holder,
camera and light transforms remain fixed. A window-relative affine mapping fits
the envelope within82% width/75% height, limits depth to14% of the smaller aperture,
and puts its front up to3.5% of that aperture in front of the glass. Source-camera
viewport/RawImage crop semantics and the original native LDR effect path remain.

Only sampled character sources request per-Graphic physical pixel demand.
Desktop UI corners are mapped through the original UI camera onto its actual
physical pane before head projection. Intermediate UI raster density limits the
demand. Invalid/all-behind geometry returns unknown demand; partial eye-plane
crossing uses bounded visible-raster demand rather than an unbounded extent.
Character eye targets use256/512/768/1024/1536/2048 tiers and a shared96MiB reserved
pair budget. Quantization, growth margin and delayed shrinking avoid repeated
allocation around a tier boundary. Two seconds without capture releases owned
targets through the known-source maintenance loop, without losing pooled bindings.

Successful supersampled stereo explicitly owns its two eye captures and disables
the redundant automatic mono draw. Inactive display ancestry suppresses capture.
Losing sources/eye positions or encountering a capture failure restores a freshly
rendered native mono image. UI camera-stack passes with no prepared depth source
skip the depth-only canvas flush. Native colour, dissolve/material clocks,
callbacks, animation, input authority and absolute shared window geometry remain
original. No new setting, wire layout, movie or story companion is introduced.

Before the complete local gate, integrated source checks pass16/16, real Debug and
Release builds and the private metadata-reference build pass0W0E, and docs-i18n
passes all five bilingual document pairs. The private DLL-only compiled review
finds13 intended existing WorldUI implementation files, two added top-level types
(sampled-source marker and lifecycle observer), nested manager/calibration helpers,
and nine existing files containing only inlined724-to725
build substitutions. Source surface counts are669 config keys,244 literal Harmony
entries and4835 log tokens, with zero removals. The registered patch inventory is
215 classes/314 method declarations; its dynamic target declaration is a different
measure from the actual native-method census.

The complete249-suite local invocation starts from frozen runtime source at
`c0676ba42`. Remaining bounded fixture ports may land before their suite starts;
later source repairs require affected reruns with unchanged passing evidence kept.
Final exact receipts and normal Debug archive identity follow completion. Local
GPU/CPU evidence does not certify a new headset picture or frame-pacing result.

The independent CPU comparison `performance-worker/run-jmobrqe3` passes833
production assertions, three pinned724 comparator assertions and all nine
compiling defect-restoring controls. Both timing populations contain13 panels,
39 real native windows,24,531 UI behaviours,2,808 unrelated RawImages plus three
actual model consumers, and1,131 typed decorative images. During the same
three-second steady interval,724 performs312 root censuses,12 global censuses and
creates24 global result arrays.725 performs none of these or any structural
behaviour/ancestor visits. Measured mean Tick time is1.8632ms versus0.4768ms,
p9917.8348ms versus0.7254ms, and maximum18.4303ms versus0.9911ms. These are local
Mono CPU measurements in a constructed native-uGUI hierarchy, not an imported
complete game scene, GPU busy time or headset locomotion evidence. A16KiB probe
shows the native allocation-byte counter is unsupported, so receipts record-1;
zero allocated bytes is not claimed. Source pins remain unchanged and all831
retained proof files have independently verified copies in the primary checkout.


## Historical direct rendering boundary and final repair

A stricter first-head raster witness exposed an older direct world-canvas defect.
Exact shipped724, already registered before the real automatic head draw, renders
600 red model pixels into its owned eye while the head still contains0 red and
15600 blue pixels from the original mono target. Both native texture restoration
and the owned camera invocation succeed. The same result occurs automatically;
it is not solely a manual Camera.Render limitation. Global/local canvas rebuilds,
cull overrides and delayed post-render restoration were retained and rejected as
ineffective hypotheses. No speculative per-eye global flush was shipped.

The engine snapshots the native UI texture identity before head pre-cull. An
optional direct-source interface now loans an allocated stable left-eye target
at Canvas.preWillRenderCanvases, without rendering the model. Each actual tracked
eye then renders its own view into that same surface. Dimensions remain locked
for that batch frame and may change at the next early boundary. The original
RawImage texture restores after each head draw. EndOfFrame and an early next-frame
cleanup cover missing head/post callbacks and teardown. Native UI materials,
stencil, RectMask2D, alpha, camera effects and input remain original; no shader
bundle, third target, ordinary blit or additional model render is introduced.
Failure recovery alone copies native mono into a primed stable surface (or clears
it when unavailable), preserves RenderTexture.active, cancels unbegun loans and
reports sparse restoration context. Candidate/prime/restore faults cannot escape
the external Canvas callback. With no direct candidates, XR eye sampling is skipped.

The automatic final witness has4096 blue/0 red native-mono pixels,22344 red/0 blue
owned-eye pixels, and5192 red/0 blue final-head pixels. The initially empty
registry receives late native Display with no added Driver.Tick. Actual camera
render count is one for that eye and the native texture is restored. Two consumers,
mask/alpha, opposite tracked positions, actual next-frame orphan cleanup, a visible
Unregister+Dispose fallback and explicit callback faults pass. The suspected early
release defect was not reproduced, so no speculative lifetime machinery was added.

## Final integrated verification

The complete local invocation records249 suites at frozen runtime c0676ba42:
248 pass and the newly established historical direct-head raster fails. After the
bounded repair, all15 affected suites are covered again. Thirteen pass directly;
the character/desktop harnesses initially reject obsolete mutation selectors
before Unity executes. Their narrow selector ports pass both complete focused
reruns. The final ledger therefore contains249 distinct passing local suites,
with unchanged coverage inherited for unaffected areas. This is not a claim of a
new single complete-gate pass on the later source tree.

Primary receipts live under ignored debug/menu-depth725/{local-gate,final-affected,
final-fixture-rerun}; integration/final-coverage.json lists every suite and its
qualifications. The final Character matrix has40 cases:11 positives/1095 assertions
and29 detected compiling controls. Performance has833 CPU +3 exact724 +18 manual
first-head +9 automatic assertions and10 controls; final Capture has323+22, Desktop
198+10, and graphic/native-mip/material coverage also passes. Private sources,
actual runner/uGUI assemblies, failed hypotheses and screenshots remain evidence.
Two old fixture issues were corrected without changing product math or thresholds:
LegacyAnimation binds omitted translation axes tozero, and a shared UI target's
partial viewport retained prior-window pixels outside its rect. Full target clear
removes that contamination. Input/output coordinate oracles remain independent.

All306431 unfiltered golden assertions pass separately after the full invocation;
all16 final source checks, five bilingual document pairs, strict real Debug/Release
builds and the final metadata-reference build pass0W0E. The final compiled review
finds13 intended existing WorldUI implementation files and four new top-level
interface/cleanup/observer files; nested manager/calibration/SkinBinding helpers
are counted inside their containing implementations. Nine other existing files
change only their inlined724-to725 build identity. There are no removed types.
Final source surface is669 settings,244 literal Harmony entries and4838 log tokens,
with zero removals; registered inventory remains215 classes/314 declarations.

Hosted CI40492153163 passes the initial runtime checkpoint, and40500071049 passes
final runtime434 at a5914801c. Later local fixture/retention ports receive their
own dev CI; these do not change the production C# source. Current normal Debug
ZIP target is dist/GloomhavenVR-1.2.2.zip. Clean delivery metadata, archive/DLL
hashes and byte comparisons against all22 movies/516 story companions are recorded
in ignored delivery receipts after packaging. The18483-byte renderer remains the
accepted beee5adbf2ebaba133e3e827269b45d267648ba681a465609c73cc51261b3c96 bundle.

The private ADB socket only isolates the test editor from an unresponsive shared
Android device. Shared/default ADB, read-only game assets and hardware inputs stay
untouched. Completed Editors/descendants/compiler processes are reaped before the
common cleanup API removes caches. Worker evidence is copied and byte-verified
before worktree removal. The maintainer's accepted story data remains unchanged;
new headset appearance, near-window sharpness and joystick frame pacing on the
reported RTX4090/D3D11 system still require the next hardware observation.
