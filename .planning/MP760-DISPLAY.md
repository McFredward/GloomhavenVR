# MP760: native window layout and desktop aspect

Scope: class C because the native capture viewport is shared infrastructure.
Worker base `6a390f198`, branch `worker/mp760-display`. Integration and the final
complete checkpoint belong to the primary agent. No hardware acceptance is
claimed by the GPU fixture.

## Hardware evidence and cause

The paired Build759 inputs are pinned in the main checkout at
`.planning/debug/mp760/hardware759`; the remote player is the host. Its
`LogOutput.log`166/169 identifies a3440x1440 desktop and matching native capture.
The initial party-window hit frame is2580x1080 at1716, versus1920x1080 on the
client at3251. Both later use the fixed1988x1080 outer character frame.
`ziel_wird_rechts_abgeschnitten.png` shows the left creation quest and a narrow
strip of the right quest beyond the physical window edge.

Two source causes are independent of network delivery:

1. `FlatScreen` always presents a16:9 physical rectangle but allocated its native
   camera targets at the desktop aspect. Actual `CanvasScaler` IL reads
   `Canvas.renderingDisplaySize`; the real Unity replay confirms that the camera
   render target determines this layout metric. Thus an ultrawide desktop
   changed native anchor placement before conversion. The original framework
   already handles the target metric correctly: an additional scaler patch was
   investigated and removed when its negative control escaped.
2. Shared-window normalization deliberately covers public stories/events, not
   private party/creation windows. Their full-stretch native target entered
   conversion at the desktop-derived width. Resizing only the outer host later
   leaves nested opposite-edge anchors in that different coordinate frame.
   The original level9 creation ancestors all stretch; original quest
   RectTransforms9355/11201 are512px wide at opposite edges with366px insets.

## Runtime changes

`NativeUiViewport.Raster` supplies a canonical16:9 target for visible native
capture and discarded desktop rendering. Neither previous raster axis is
reduced:3440x1440 becomes3440x1935. Existing target lifetimes remain unchanged;
changing the desktop does not reallocate a standing VR screen. The mod does not
call `Screen.SetResolution`, change camera projection, replace native widgets,
patch `CanvasScaler`, or alter native callbacks.

`NativeWindowFrame` captures the serialized design metric for a true native
full-stretch UIWindow, preserving its authored insets. A native screen-space
root Canvas is also an explicit full-frame role. World-space roots, ordinary
fixed-size popups (including one coincidentally equal to the desktop canvas),
and targets without that native window/scaler relationship remain untouched.
The existing shared-window design law takes priority. The recorded frame is
reasserted during conversion maintenance; host pose, manual resizing and shared
placement authority are unchanged. Existing original-parent/anchor/size receipts
continue to own release.

`FlatScreen.OwnsNativeUiPixels` exposes the exact current native pixel domain to
the input worker: native root Canvas/camera identity, captured UI classification,
and the current owned target must agree. It also recognizes the existing
scrub-camera membership. It establishes no global mouse exception and adds no
registration or periodic census. Physical/virtual pointer dispatch is the
separate input lane.

## Focused evidence

`menu-depth-desktop/run-5ajs_cxx` passes271 assertions and three compiled causal
controls on Unity2021.3.5f1/OpenGL llvmpipe:

-131 assertions: original native CanvasScaler, all three match modes and all
  match positions,1920x1080/3440x1440/1280x720/restore inputs, actual native
  capture routing and final pixels from both eyes. Capture release retires the
  pixel-domain lease immediately; the next ordinary native scaler update
  resumes the current display metric. The historical desktop-aspect target
  fails the canonical-frame assertion.
-140 assertions: source-bound production conversion frame selection and target
  seating, full native serialized creation ancestry, original parchment/sprite
  art and native TMP font rendering, two full quest rectangles, direct original
  GraphicRaycaster hits, native frame drift/restore inputs and final head pixels.
  Missing initial design-frame admission and missing maintenance each fail the
  complete-quest-boundary assertion.
- Fixed-size coincidence, stretch insets and WorldSpace exclusions are explicit
  checks. Native game controllers and dynamic quest identities are input ports;
  no gameplay controller is copied into the presentation tree.

The test executes the production head/capture coordinators. Native page state,
the unchanged1988x1080 outer fixed-fit result, and XR eye observations are
supplied inputs. It is not a native campaign bootstrap, a Photon session, a real
OS fullscreen/window-manager test, Windows D3D11, or an HMD. Native dynamic quest
text is represented by exported prefab text; the fixture does not claim to
reproduce a particular player's quest content or the entire fixed-fit lifecycle.
The paired hardware, original anchors, source frame selection and independent
consumer pixels establish the causal layout correction; final headset behavior
still needs the maintainer's combined test.

Final strict production Release `Compile` completed with0 warnings/errors in
13.22s (`run-74nm1i4r`, DLL retained). This is not a complete local gate.
Dependent desktop/native-owner fixture results are recorded in the delivery
message. No wire, rule, localization or camera-normal changes are introduced.

## Failed experiments and cleanup

All unsuccessful runs remain unsuccessful evidence. New fixture import repairs
addressed recursive/dynamic-object-array JsonUtility limitations (flat primitive
JSON records now retain all141 original nodes), inactive-parent TMP generation,
and an initially disabled source camera. The added native-scaler patch was
removed after the actual original algorithm passed without it. A stale control
that also disabled the new inset contract was narrowed to the actual historical
missing conversion admission. Release timing now observes the original next
native update rather than claiming a scaler patch's immediate callback.

Every standalone run reaped its processes and used `cleanup_completed_run`.
Compiler/Unity project caches were removed; logs, original-input exports,
source/manifest pins, controls and final pixels remain. Only this worker's
artifacts and private ADB5074 are owned here. Main hardware inputs, shared game
references and other workers are untouched.
