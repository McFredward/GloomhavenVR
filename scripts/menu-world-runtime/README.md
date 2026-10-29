# Actual ready-menu world-window runtime

Run `python3 scripts/menu-world-runtime/run.py`. `--production-only` is an explicitly
partial development run. The normal invocation compiles and executes the complete
containing scope and causal defect controls in Unity 2021.3.5f1/OpenGL. It normally
needs one editor startup; allow roughly one minute including control compilation.

This scope is separate because the environment fixtures can observe room/sky
ownership but cannot execute the new native menu admission, tracked recenter,
original FlatScreen coordinator and physical window manipulations together.

The whole production `MenuWorldContext`, `FlatScreen.9.WorldMenu`, original screen
placement/follow, `SurfaceGrabBar`, `PanelGrabHandle`, `ProximityGrabber`, `RayGrabDriver`,
`LaserPointerPolicy`, registry,
rod mesh/layout/tween, flight, world grab and snap turn execute. Exact extracted
original kernels retain the contiguous first-pose recenter/admission coordinator,
its real `TrackingSettled` getter, menu recenter, hand-mask policy, `HandsDriver.Update` and
`ApplyMode`, `VRHand.SetInteractorMask`, the native physics mask initializer and exact
`Physics.Raycast`, native screen pointer projection/yield and canvas
order/render-root registration. A synthetic captured-native-art texture is drawn by
the actual head camera and compared pixel for pixel against an independently seated
world-geometry reference through player movement. Player locomotion and hand carries
also run while Unity's native game clock is paused.

The native main-menu manager/window/camera visibility, tracking/input samples,
configuration storage, original captured artwork, callback sinks and unrelated
scenario/tilt claims are declared ports. No port replaces readiness, window
placement, near-grab election, carry, scale, locomotion or pointer projection.
The actual `RayUguiDriver.Tick`, pressed-canvas handling, bounded canvas-plane
intersection and cancellation execute as extracted kernels before the whole ray-grab
and proximity drivers, followed by the original `FlatScreen.TickPointer` and its
pointer-yield helpers. Native widget raycasts/callbacks and unrelated town/scroll
paths remain explicit ports. `BoardDriver.SyncRayMask` and the map's original
`ApplyMask`/release block execute against native selection-field ports, including
map-before-hands arrival and return. Production hand visual construction is outside
the extracted mask-update kernel.
The fixture does not certify native game bootstrap, original native menu artwork,
OpenXR devices, D3D11/headset appearance or hardware frame pacing.

Admission cases cover intro, initialized-but-unshown EULA/sign-in, tracked arrival,
subpages, native camera loss, tracking/rig replacement and VR stop. Window cases
cover fixed world geometry through actual flight/turn/world drag/zoom, palm grip and
two-hand resize, actual laser pickup after a physics sync, front/behind native UI
arbitration, native solid blockers, default-mask handback, fresh and post-resize
stationary-palm laser sweep, both paused-clock stick-reel directions and release, late split backgrounds, native pointer UV and reticle, hidden/null
capture-owner refusals and same-opening movie/recovery round trips after world zoom.
The actual reverse-backing helper also retains native vertices/UVs with opposite
triangle winding, stays fixed across player motion, joins a late split capture at
the opposite physical gap, shares the original material and executes the shipped
Overlay shader. Intro restoration hides it. Closing and reopening destroys then
rebuilds its owned mesh and surface, including the original `FlatScreen.Tick` branch
for externally destroyed capture geometry. These owners are not fixture ports.

Every attempt retains sources, compiled variant assemblies, input manifests,
results, head captures and failed proof. The private editor and fd-proven escaped
compiler processes are stopped before `cleanup_completed_run` removes regenerable
projects; its own short TMPDIR and ADB socket cannot borrow another suite's state.

The Build744 hardware report exposed a native-physics self-occluder. The menu has
no board/map selection-mask override. Once physics synchronized the generous palm
box, the old all-layer default picked it about 9.6 mm ahead of the visible rod;
`RayGrabDriver` then refused the same handle under its ordinary nearer-object rule.
The source-derived native-only default keeps dedicated mod geometry on its direct
registry/UI/solid-distance routes. The old-default mutation must fail the actual
menu rod pickup; the missing-hand map mutation must fail native-only restoration.
Explicit native map/scenario masks remain unfiltered and authoritative. A separate
motion/reel control catches game-clock freezing after an accepted trigger pull.
