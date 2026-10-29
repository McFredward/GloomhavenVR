# Build742: producer confirmations, character quality and the world menu

The maintainer's local740 hardware report accepts the small level-up prompt,
but identifies a separate Perks prompt, whole-character-window aliasing and a
room which appears during the intro with the eyes at floor height. They also
request a freely movable/scalable main menu and ordinary world locomotion there.
This is class C: the integration changes shared capture and menu rig lifecycle.

## Evidence and limits

Immutable inputs and SHA256 receipts are in ignored
`.planning/debug/menu742/hardware-inputs/` and `round-start.json`.
Local logs identify740, Unity2021.3.5f1, D3D11 and RTX4090. Remote717 is stale.
No new screenshot/video accompanies this report; older supplied images are not
evidence for the new defects.

The room log precedes tracked menu recenter. It places the floor atY1, then
recenter puts the eyes at the authored vantageY1. The new readiness producer
waits for both tracked recenter and the original main-menu UIWindow becoming
visible. `MainMenuUIManager.Initialised` alone also describes EULA/sign-in, so
it is not sufficient. The exact manager/head/rig identity scopes the opening;
subpages retain it. This never declares a gameplay table or grants actor input.

Closed rooms use the actual central Floor/Ceiling meshes, excluding the cellar's
large outside art bounds. Seat the room around the ready eyes once; retain the
settled tracking floor for open-air surroundings. Map/scenario subject arithmetic
and same-rig room anchoring stay unchanged. Default's seven native render assets,
including original AMP programs, are unchanged.

## Window ownership and rendering

The menu retains the original desktop camera stack, original widgets and pointer
callbacks. Its world wrapper uses `SurfaceGrabBar`/`PanelGrabHandle`; one hand
moves, two resize. Ready-menu hand masks enable the actual near-grab offer, while
native Menu2D policy and scroll/reel arbitration remain in effect. Flight, turning
and world drag/zoom receive a presentation-only readiness exception. Window carries
use the unscaled tracking clock. A stale native table controller cannot clamp the
menu eyes. Movie/recovery round trips preserve absolute pose and size, including
after world zoom; ordinary motion never calls menu follow placement.

The original BlitCopy backdrop has no depth and shares queue1000 with the native
sky. A real cross-seam witness reproduces zero surviving red menu pixels when
the room overpaints it. Only the ready world menu uses the existing opaque,
Overlay shader at1002 after sky/reset, with depth testing/writing. A single
CullOff plane at+6cm then failed the real rear glass/UI witness: front1584 blue
UI pixels, rear0. Use two fixed outward-facing backings at+/-6cm with CullBack
and one shared material. Reverse only the rear mesh winding, preserving world
XY/UVs and per-eye texture swaps. The non-split image uses coincident opposite
faces. Both viewpoints now retain1584 blue pixels and the original asymmetric
RT marks. Nearer hands remain visible, farther geometry stays behind the original
menu image, and native RT alpha0 does not erase its RGB. Restore original shader/
queue for intro and other screen uses; retire the owned rear mesh on release or
quad loss. MR opaque coverage selects the appropriate exact owned face, retaining
foreign-material/shader refusal.

Generic confirmations retain their exact native producer. The Perks failure was
the cross-scene whitelist after that producer was already recorded. Both scene
cases now use the same reversible member capture at32 authored pixels in front
of the actual owner and its sorting tier+1. Original small boxes, callbacks,
permissions, native homes and persistent scene lifetimes remain authoritative.
Item/equipment/shop requests use their original UIItemConfirmationBox pool.
Exact deferred compiler callbacks recover their captured original producer; the
original short-rest tooltip action gets a scoped wrapper without changing its
action or permissions. No focus-based owner guess is added. Retire the complete
member lease before an external composite records its native home. Legacy map
seating and immersive town palms/masks cannot become competing transform authors;
refused home restoration retains its snapshot and refuses adoption. Existing
enhancement cancellation remains unchanged.

The character antialias implementation retains the fixed final head grid and existing
3D source presentation. Its source captures use a fixed2x absolute-head sampling
grid with conservative occupied-region storage. Crop origins change storage,
never sample spacing or phase. Native content still renders once per eye. The
independent resolve shader must not be another automatic pass on the display
renderer. Rejected postfilter and failed intermediate raster experiments remain
evidence, not passing coverage.

Final review found a separate camera-input defect which the unlit reference planes
could not observe. Decomposing the cropped clip matrix against the original head
projection folded a nonuniform crop shear into the native view. An actual Unity
oracle keeps the eye fixed and changes only storage: the old implementation changes
the view by46.68665, a transformed normal by44.4107 degrees and near/far by a factor
of0.326252. This also changes uGUI's projection-based pixel-size inputs. The repair
carries the cropped raster projection separately through eye copies and decomposes
against that projection, retaining the native view and its physical metric. The
original full-gate candidate was stopped before this runtime repair; its60 passes
and207 cancelled scopes are not a complete pass.

Conservative late-equipment, decoration and annotation bounds share the same
absolute grid. Uncertain bounds use the complete head extent. The UI scratch and
native source scratch use high-water capacity buckets;52 measured motion poses
require6 allocations each rather than the old11. Idle/failed-first/deferred-close
paths retire their complete owned resources after the active borrow ends.

With the fixture's RGBA32/D24 targets, at640x640 the measured owned peak is
24,576,000 bytes. An extreme4040x4040 close or coplanar view requires1,305,728,000
bytes (1245.24MiB), excluding Unity-internal and driver allocations. This is a
measured storage cost for that format, not evidence of hardware
frame pacing. Sampling remains2x per axis throughout movement; final eye pairs
retain their1x head grid. The repaired fixed-eye oracle now measures0 degrees
normal drift and near/far ratios0.9999999. Its6 assertions and3 omission controls
cover native model view, uGUI pixel density and the actual source-dispatch copy.
Actual coordinator cases retain326 antialias,1785 motion,246 pitch,92 paired
backing and32 filter-lifetime assertions on stable worker inputs.

An additional original-game TMP/SDF witness found a second, older metric error
in the native flat route. Its original glyph UV2 data remains in native Canvas
units while the head view contains the physical window's uniform scale. That
preserves geometry but changes SDF edge softness: the independent Continue-glyph
comparison measures0.889. A private minimal normalization of view rows, projection
column3 and near/far improves all36 visible glyph ROIs to0.999912--0.999988 over
both routes/eyes, four poses and storage retirement. A camera-transform experiment
is pixel-identical and unnecessary; no camera pose change is adopted. The new
title witness is isolated from an existing synthetic blue comparison label.
Its new0.999 glyph threshold distinguishes both known old-projection omissions;
all existing whole-window/motion thresholds remain unchanged.

Only`NativeUiScope.HeadCapture` needed the font-metric repair. Its one
production call is the original native-UI route in`FlatScreen.8.MenuDepth.cs`;
converted windows use their separate capture. The complete invocation retained its frozen baseline; the bounded repair and
affected consumers were then verified independently. The final aggregate records that follow-up and unchanged inherited closures;
it is not one successful final-tree invocation.

## Final validation

The final runtime is frozen at`9371bf027885d3f95231a2f5853fe9836f9bbb00`.
A subsequent fixture-only commit95f568e66 rebinds the existing right-eye omission
from its old inline expression to the named sampled eye; production is unchanged.
Strict Debug/Release pass0 warnings/errors in43.45/19.83 seconds; metadata passes
in36.58 seconds. All16 source suites pass in39.0 seconds;12 locked frame orders,
223 patch classes/338 methods and670 configuration keys remain verified.
Docs-i18n covers5 pairs. Surface attributes263 to265 and tokens4833 to4835 add
only the explicit item/persistent origin hooks and two bounded menu markers.

The complete invocation records264 PASS/3 FAIL over all267 local scopes in
3505.462 seconds. Level-up's old420-second editor limit cut off active work at
32/39 PASS; the original limit dates to14 cases. The bounded complete39-case
follow-up passes1056 production assertions and38 causal controls in525.682
seconds with its original30-second-per-case completion allowance. No assertion
or pixel criterion is relaxed. Flushed BEGIN/END timing receipts record progress.

The world-menu fixture used editor elapsed time while manually updating disabled
native components on selected player frames. Its unchanged movement criterion
could therefore see too little integrated native time. Count the actual distinct
frame deltas consumed by flight/world drag/pinch, with an independent wall bound.
The complete19-case follow-up passes120 assertions/18 controls in51.631 seconds;
a new scaled-clock control detects blocked paused motion. Flight code is unchanged.

The desktop suite's six legacy failures exposed old1x target-size assumptions,
a reference without the original CanvasGroup alpha/2x integration, and the real
mixed-color boundary defect described below. The final complete83-case desktop
scope passes in709.1 seconds. It retains the3.5-pixel independent anchor and95%
coverage/IoU criteria, adds hard RGB0 native/converted boundary checks and their
own omission controls, and includes the original native font/normal/copy/lifetime/
allocation/4k proofs. Its900-second editor bound accommodates the larger actual
pixel workload; per-case timings are retained. This does not measure HMD pacing.

The final affected union contains14 passing local scopes:12 head-window families,
the complete level-up suite and the complete world-menu suite. The first11-scope
head invocation records10 PASS/1 stale mutation-binding FAIL; the complete
capture follow-up passes419 assertions/29 controls in101.452 seconds. Rotation
integration separately passes156/4. All253 remaining full-invocation closures are
unchanged and retained. This is verified complete aggregate267-suite coverage,
not one successful complete invocation on the final source tree. The actual
retained golden executable independently passes306431 assertions; its262 explicit
compile inputs are identical except verified full-line Build742 comments.

Compiled review against delivered740 finds28 existing runtime types,9 exact
ModBuild-only consumers,5 added types and0 removals. The post-complete bounded
repairs change exactly4 known head-rendering types. All32 extracted resources
are compared; only the87819-byte menu shader bank changes (SHA256
9698e2dd0bac9d6966265861c44b5c8e2c008732877e898612ee52fa25ead3d1).
All4 previous serialized shaders remain byte-identical; only the added resolve
program changes. UnityFS7/2021.3.5f1 validation passes. Default's2219921-byte
original native bank remains unchanged.

Focused environment evidence is custom107 assertions/8 controls, Default plus
menu depth98/16 and real menu coordinator/controls120/18. Confirmation evidence
includes240 zero-difference final-head samples, plus pure323/26 and legacy
native114/13. The final source/input manifests, failed attempts, executed binaries,
shader audits, per-case timings and pixel comparisons remain in ignored
`menu742/validation-final.json` and its linked run directories.

## Color permission, storage and hardware boundaries

The new independent alpha/2x geometric reference gives exact silhouette IoU1 for
all four front/rear eye samples. It also exposed70 mixed-blue pixels per outer
eye: nearest foreground depth admitted color already blended with an unrelated
off-window UI neighbor. Hiding only that blue widget changed exactly those70
pixels, with unchanged geometry. Apply the existing foreground/glass/annotation
rule separately to each quarter-pixel before integrating color/depth. Both actual
coordinators now measure mixed RGB0; their own omitted-sample-clip controls restore
the defect. No extra render target, camera or native content render is introduced.
The final display clip remains an additional guard.

For RGBA32/D24, the actual4040-pixel front fixture with intentionally outboard
quest/tooltip bounds owns100663296 bytes each for UI and model scratch plus
261145600 bytes for its eye pair:441.05MiB. Extreme close/coplanar views peak at
1305728000 bytes (1245.24MiB);640-pixel motion peaks at24576000 bytes. These
exclude Unity/native effect temporaries, driver/program allocation and in-flight
retirement. Six high-water growth steps during52 poses replace the older eleven
small growth steps; complete ledgers additionally count timed shrink/regrowth and
the explicit post-motion retirement. UI and model allocation events are observed
independently; a model churn omission proves the guard. This is measured storage
for these formats, not hardware VRAM or performance acceptance.

The original game TMP DLL/font/atlas/material and hash-verified canonical SDF
source execute in the GL fixture. The native atlas's external8MiB stream span,
all Managed metadata and actual decoder modules are pinned; every run exports
its inputs from read-only references rather than a discovery cache. Model art is
independent synthetic geometry, with separately verified native view/normal/clip
inputs. XR/provider, actor/session/bootstrap and original Windows programs remain
explicit limits. No automated green result establishes D3D11/HMD image quality,
comfort or frame pacing; maintainer acceptance remains separate.

Owned test processes stop before per-attempt cache cleanup. Evidence, inputs,
current binaries and `.keep` pins remain preserved. All three clean owned worker
worktrees are archived, content/hash verified and retired; the clarity archive
verifier explicitly handles its3090 hardlinks. Final task-private process/socket
and cache audit is recorded with delivery. Historical unrelated worktrees and
read-only game references are preserved.

Delivery is a normal push to`origin/dev`; the Windows installation script builds
its archive. No local ZIP is requested. Quest741 remains the coordinated separate
lane; send its owner the final pushed dev SHA and new resolve-bank/API handoff.
