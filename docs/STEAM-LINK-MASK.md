# Steam Link mask-carriage source experiment

The source experiment is part of **GloomhavenVR itself**, enabled through its VR
settings. There is no separate plugin, manual configuration step, CLI flag,
keyboard start key or externally installed fixture. The existing module-config
system stores the toggle in `dev.gloomhavenvr.steamlink.cfg`. It is disabled by default
and its persisted request is cleared at process startup. It does **not** enable
Steam Link passthrough, export real game alpha, modify the native PC Steam
sender, or install anything on the Frame.

The diagnostic temporarily replaces the current PC VR eye image with original
asymmetric RGB/explicit-coverage test canvases. The coverage plane is authored
independently of scene RGB and includes fractional opacity controls; this is not
color-key replacement. Both eyes carry a complete static generation with their
own eye marker. Their immutable application source ID is deliberately repeated,
not derived from a Unity frame counter, timestamp, submit number or native video
ID. A visible streamed pattern is only a first carriage control. It cannot prove
complete decoded-frame carriage, native composition, selected-resource lifetime,
reprojection/foveation mapping, real material alpha or working passthrough.

## Operation and escape

The normal VR settings toggle owns the request. Enabling it prepares the embedded
original PNGs/source manifests and waits while VR options are open. Closing the
options window starts at most **30 seconds** of diagnostic output on the existing
`VRRigDriver.HeadCamera`. Open the normal controller menu or reopen VR options to
stop immediately and restore ordinary output; disabling the request, timeout,
VR shutdown, camera replacement, component destruction and failures also stop it.
The request resets to off after stopping. A fresh explicit enable may start a new
run, with fresh owned resources and policy; no old deadline or callback is reused.

Normal menu/options opening stops synchronously before native showing, while the
render callback independently checks current options visibility and request/VR
state. Pending preparation is not cancelled by its own options OnShow/rebuild.
The controller-menu edge is necessary because the pattern would otherwise hide
the pause menu needed to reach VR options. No input is consumed, rebound or
blocked by this feature. A native menu that was already open when the experiment
started is not treated as a new opening on every frame.

Only the PC streaming source is eligible. Steam Frame standalone rejects this
request; its existing native passthrough remains independent. The source requires
the built-in render pipeline, active XR MultiPass, both eyes and the exact current
mod head camera. Missing/unsupported targets at menu closure stop the request
rather than indefinitely retrying or attaching to another camera. SPI/SRP/mono
callbacks retain the ordinary source blit.

### First hardware source-output check

Install the current `dev` build on the PC using the usual development installer;
verify its ModBuild banner. Start that PC game through the ordinary Steam Link
VR stream on Frame. No new Frame helper installation or restart is needed.

1. Keep Debug logging enabled. In VR options open **Advanced → Measurement &
   diagnostics → Steam Link mask test (EXP.)** and enable the test.
2. Close VR options. Expect the diagnostic color/grayscale pattern in both eyes.
   This build cannot show native streamed passthrough, even if the test succeeds.
3. Press the ordinary controller menu button. The game image must return
   immediately and normal menu interaction must remain usable.
4. Enable the test again and let its 30-second limit expire. Verify ordinary
   output returns and the switch is off. A fresh explicit enable must work again.

Retain the PC `LogOutput.log` and `Player.log`, with the run's ModBuild, under
`.planning/debug/steamlink_passthrough/`. A Frame screenshot can establish the
visible pattern, but not decoded-resource association or numerical fidelity.
Debug `PAYLOAD` lines record the selected/actual eye and first callback gates;
the stop summary should show positive left/right callback counts. Report absent
output or incomplete eye output rather than treating it as a successful carrier.

## Ownership and lifecycle

`CoreModule` binds/clears the diagnostic request before XR bootstrap, installs a
dormant driver on the existing hardened core host after runtime dependencies are
loaded, and shuts it down before host/VR teardown. The off path reads only the
request and owned-resource state, returning before menu/camera/clock sampling.
Standalone eligibility is cached outside callbacks because its launch marker
check involves filesystem access.

`SteamLinkMaskLifecycle`, `ProbePolicy` and `SteamLinkMaskCallbackGate` are the
actual pure policy used by the runtime. A camera callback must match the current
policy, component and camera; a retired callback may stop its old policy but
cannot cancel a fresh request. A delayed retired driver OnDestroy disposes only
its own resources and never clears another driver's request. Current shutdown
clears its request while it still owns it, then retires the driver.

Admission closes **before** destruction of the component/textures. The callback
never writes config, logs, reads files, performs readback, waits on a socket or
compiles a shader. Three fixed slots retain the first fallback/left/right
observations, drained by Update; routine calls only increment saturated counters.
Actual/selected eye, XR/camera gates, color space, GL write state, target extents,
formats, dimensions, VR usage and sRGB flags are Debug-only observations. Ordinary
logs retain the significant start/stop/failure context without per-frame output.

## Immutable source and rendering scope

Four small original files are embedded in the existing mod DLL under
`GloomhavenVR.Assets.SteamLink.*`: `left.png`, `right.png`, and their respective
`*-source.json` manifests. There is no temporary extraction or user path. The
loader validates bounded resources, PNG chunks/CRCs/RGB8 extents before decoding,
Unity pixel orientation, full canvas/plane hashes, three complete source headers,
nonce/content/eye/layout/convention, stereo-pair identity and exact grayscale R8
coverage before marking the textures non-readable. It refuses inconsistent
sources rather than displaying guessed artwork.

The physical source canvas is 688×548 for each 320×192 logical source. Color and
coverage occupy separate known source rectangles. The source texture is RGB24,
non-mipmapped, point-filtered, clamped and created with `linear=false`. The public
`Graphics.Blit` path preserves inherited `GL.sRGBWrite` and `RenderTexture.active`
state. It changes no camera projection, pose, render target, quality, culling, XR
configuration, gameplay, UI state or network protocol.

Prior local Unity 2021.3.5 Play Mode evidence is inherited for unchanged normal
texture/blit paths: Gamma 20 controls and Linear 18 controls. Both eyes decoded
with the correct top-left source hashes; real mono OnRenderImage fallback matched
the ordinary no-component camera baseline and restored GL/RT state. Gamma GPU
blits preserved RGB bytes exactly. In Linear, sRGB RT plus `GL.sRGBWrite=true`
preserved them; the other tested paths had mean code error 24.2243 and maximum
73. These are local OpenGLCore/llvmpipe facts, not a claim about D3D, the actual
PC XR callback, codec fidelity, gaze/head mapping or the headset image.

Unity documents the built-in image-effect and stereo-eye contracts:
[stereoActiveEye](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Camera-stereoActiveEye.html),
[OnRenderImage](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/MonoBehaviour.OnRenderImage.html),
[Graphics.Blit](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Graphics.Blit.html).
Image-effect ordering and later native composition can still change the final
stream. A bounded Debug census records current effect types once per run.

## Focused verification

```bash
python3 -B scripts/check-steamlink-mask-runtime.py
# Optional local actual-Unity-reference type check in addition to portable controls:
python3 -B scripts/check-steamlink-mask-runtime.py --compile-runtime
# Limited repair of runtime typing only, reusing unchanged passing controls:
python3 -B scripts/check-steamlink-mask-runtime.py --compile-only
```

The runner uses the actual production policy/manifest/lifecycle/callback gate,
including pending-menu starts, request cancellation, deadline, VR/camera loss,
retired callbacks/drivers and fresh explicit repeat. Hosted execution needs no
private game assets. The optional runtime type-check uses real local Unity
references with compile-only adapters for other mod types; the complete mod
build separately verifies the actual integrated config/VR/options types. No test
claims a complete headset or native transport result. Each completed/failed run
reaps its processes and uses the common cache-cleanup API, retaining logs,
results and source/hash evidence. This focused scope is not a new full-suite gate.

Before native presentation work, recover both planes and their IDs from one
selected decoded resource and establish complete composition and mapping during
movement, gaze/foveation changes, repeats, reconnect and layout changes. Unknown
or conflicting association remains opaque. The Frame camera stays local; no
camera upload is needed. This experiment grants no Frame-side mutation scope.
