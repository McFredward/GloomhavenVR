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

## Validation status

The runtime is frozen at`ddd6719ff8285e33d32d858c33ee28865095f300`; the final
complete267-suite invocation and actual golden executable are running on that
integrated candidate. This remains pending delivery. Strict Debug/Release pass
0 warnings/errors in28.19/14.49 seconds; metadata compilation passes in25.93
seconds. All16 source suites pass in30.9 seconds. Docs-i18n covers5 pairs.

Compiled review against delivered740 finds28 existing runtime types,9 exact
ModBuild-only consumers,5 added types and0 removals. The late projection repair
changes exactly7 of those runtime types from the previously reviewed candidate.
All32 extracted resources are compared; only the75934-byte menu shader bank
changes. Its4 original serialized shader objects remain byte-identical; the new
one-pass resolve is the sole addition. Default's native bank remains unchanged.
Config670 is unchanged; Harmony attributes263 to265 and log tokens4833 to4835
add only the explicit item/persistent producer hooks and two bounded menu markers.
Generated inventory is223 patch classes/338 methods, each registered once.

Focused final environment evidence is custom107 assertions/8 controls, Default
plus menu depth98/16 and real menu coordinator/controls117/17 across18 builds.
Confirmation evidence includes1056 assertions,36 original plus2 destroyed-input
controls and240 zero-difference final-head samples, plus pure323/26 and legacy
native114/13. These are worker/focused proofs, not a complete root pass. Earlier
boot/motion consumer results live in ignored`menu742/menu-consumers/results.json`.

Failed preflight/control/timeout attempts remain preserved. New conservative
card bounds duplicated the old fit-depth mutation's short suffix; the fixture
now targets only its original draw assignment and retains the same native pixel
oracle. The capture fixture expects the correct2x source domain while retaining
its1x final-eye checks. The complete desktop fixture grew to75 cases; its old
420-second editor deadline stopped after39/70. A bounded900-second deadline and
per-case timing receipts preserve every render, pixel threshold and control.
Timed far-view retirement is compared with the actually observed near peak,
allowing retirement during a slow motion trace without weakening image checks.

Unity GL camera/geometry/pixel execution uses explicitly declared native input,
bootstrap and decoder ports. It does not establish D3D11 headset appearance,
motion-to-photon behavior or hardware frame pacing. The maintainer's headset
review remains separate from automated evidence.

Source delivery is a normal push to `origin/dev`. The Windows installation
script builds its archive; no local ZIP is requested. Build741 remains reserved
for the coordinated Quest standalone lane, which receives the final dev SHA
after this integration is validated and pushed.
