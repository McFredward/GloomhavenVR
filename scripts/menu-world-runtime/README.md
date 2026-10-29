# Actual ready-menu world-window runtime

Run `python3 scripts/menu-world-runtime/run.py`. `--production-only` is an explicitly
partial development run. The normal invocation compiles and executes the complete
containing scope and causal defect controls in Unity 2021.3.5f1/OpenGL. It normally
needs one editor startup; allow roughly one minute including control compilation.

This scope is separate because the environment fixtures can observe room/sky
ownership but cannot execute the new native menu admission, tracked recenter,
original FlatScreen coordinator and physical window manipulations together.

The whole production `MenuWorldContext`, `FlatScreen.9.WorldMenu`, original screen
placement/follow, `SurfaceGrabBar`, `PanelGrabHandle`, `ProximityGrabber`, registry,
rod mesh/layout/tween, flight, world grab and snap turn execute. Exact extracted
original kernels retain the contiguous first-pose recenter/admission coordinator,
its real `TrackingSettled` getter, menu recenter, hand-mask policy, `HandsDriver.Update` and
`ApplyMode`, `VRHand.SetInteractorMask`, native screen pointer projection and canvas
order/render-root registration. A synthetic captured-native-art texture is drawn by
the actual head camera and compared pixel for pixel against an independently seated
world-geometry reference through player movement. Player locomotion and hand carries
also run while Unity's native game clock is paused.

The native main-menu manager/window/camera visibility, tracking/input samples,
configuration storage, original captured artwork, callback sinks and unrelated
scenario/tilt claims are declared ports. No port replaces readiness, window
placement, near-grab election, carry, scale, locomotion or pointer projection.
Production hand visual construction is outside the extracted mask-update kernel.
The fixture does not certify native game bootstrap, original native menu artwork,
OpenXR devices, D3D11/headset appearance or hardware frame pacing.

Admission cases cover intro, initialized-but-unshown EULA/sign-in, tracked arrival,
subpages, native camera loss, tracking/rig replacement and VR stop. Window cases
cover fixed world geometry through actual flight/turn/world drag/zoom, palm grip and
two-hand resize, late split backgrounds, native pointer UV and reticle, hidden/null
capture-owner refusals and same-opening movie/recovery round trips after world zoom.

Every attempt retains sources, compiled variant assemblies, input manifests,
results, head captures and failed proof. The private editor and fd-proven escaped
compiler processes are stopped before `cleanup_completed_run` removes regenerable
projects; its own short TMPDIR and ADB socket cannot borrow another suite's state.
