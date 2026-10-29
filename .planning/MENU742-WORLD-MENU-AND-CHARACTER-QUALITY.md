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

The character antialias candidate retains the fixed final head grid and existing
3D source presentation. Its source captures use a fixed2x absolute-head sampling
grid with conservative occupied-region storage. Crop origins change storage,
never sample spacing or phase. Native content still renders once per eye. The
independent resolve shader must not be another automatic pass on the display
renderer. Rejected postfilter and failed intermediate raster experiments remain
evidence, not passing coverage. Final pixel, lifetime and memory results are
pending and must be recorded before delivery.

## Validation status

Not a delivery candidate yet. The complete integrated gate, strict final builds,
metadata compilation, final compiled/resource review and cleanup/retirement are
pending the remaining worker changes.

Current focused evidence includes custom environment107 assertions/8 controls;
Default sky plus real menu backdrop81/16; real menu coordinator/controls103/13;
and the narrow original confirmation checkpoint241/18 plus native725/23.
Three affected boot/motion consumers pass; their precise
results are in ignored `menu742/menu-consumers/results.json`. These are focused
passes, not a new complete gate. Source16 and inventory preflight17+2 skips have
passing preliminary runs; later production inputs still require final checks.

Unity GL camera/geometry/pixel execution uses explicitly declared native input,
bootstrap and decoder ports. It does not establish D3D11 headset appearance,
motion-to-photon behavior or hardware frame pacing. The maintainer's headset
review remains separate from automated evidence.

Source delivery is a normal push to `origin/dev`. The Windows installation
script builds its archive; no local ZIP is requested. Build741 remains reserved
for the coordinated Quest standalone lane, which receives the final dev SHA
after this integration is validated and pushed.
