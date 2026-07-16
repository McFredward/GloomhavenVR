# Camera & Layer Policy (fix/rig-camera-ownership + fix/menu-freeze-visuals + fix/menu-stack-input)

Outcome of hardware tests #3/#4 (Quest 3 + Virtual Desktop, 2026-07). Test #3 fixed
stereo hijacking, layer masks and rig lifetime. Test #4 proved the remaining
structural flaw: the rig head-tracked a GAME-owned camera, and the HMD froze with no
exceptions, a FOCUSED session and a silent log — game code can interfere with a
camera it owns in ways no health check observes. This document defines the policies
that close all of it and names the SINGLE owner of each.

## The owned-camera architecture (test #4 redesign)

**The rig owns its own head camera. Game cameras never render stereo. Period.**

```
GloomhavenVR.VRRig            (root: DontDestroyOnLoad+hidden; at orbit focus /
│                              menu vantage; yaw-aligned; scaled by WorldScale)
└── GloomhavenVR.HeadCamera   (OUR camera + TrackedPoseDriver — the ONE stereo
                               renderer; settings seeded from the anchor camera)
```

- The game camera (scenario camera / menu 'Main Camera') is only an **anchor
  reference**: vantage + yaw at build time, culling-mask source (re-read every
  frame), far plane. It is never reparented, retargeted or pose-driven — game
  camera writers, component toggles and VideoPlayer interactions can no longer
  break HMD pose application, whatever the exact trigger.
- Applies to BOTH rig kinds (menu and scenario) — one owned stereo camera
  everywhere, game cameras always desktop-only.
- Restoration on VR-off/hot reload: destroy our objects; nothing on the anchor to
  restore. (Two exceptions, both scenario-only and restored on teardown/unpatch:
  `m_IsCameraCodeControlDisabled` + the CameraController prefix-skips, kept solely
  so `FocusPoint` — the rig/panel/recenter anchor — stays parked.)

## §1 Stereo exclusion — owner: `Core.VRCameraPolicy`

While VR runs, **every game camera is forced to `StereoTargetEyeMask.None`**
(desktop-only) with `XRDevice.DisableAutoXRCameraTracking(cam, true)` — menu
cameras, the UICamera, RT cameras ('GUI 3D Camera'), late arrivals
('MainMenuVideo'). The only exemption is `VRCameraPolicy.AllowedHead`, which is
always the rig's own `GloomhavenVR.HeadCamera`, never a game camera. The old
`Reclaim` promotion path (game camera → rig head) is gone.

- **Pump:** `VRRigDriver` sweeps on every scene load, after every rig (re)build, and
  every 30 frames (catches cameras created mid-scene). Idempotent; no per-frame
  allocations (`Camera.GetAllCameras` into a reused buffer).
- **No stand-down:** even with no rig head (e.g. `[Rig] MenuRig=false`), game
  cameras stay stereo-None. Game cameras never render stereo, period.
- **Reversibility:** originals are recorded per camera and restored by
  `VRCameraPolicy.RestoreAll()` on VR-off / hot reload (`VRRigDriver.OnDestroy`).
- **Log lines:** `Stereo policy: '<cam>' forced to StereoTargetEyeMask.None (…)` per
  camera, plus a per-sweep summary; the camera inventory prints
  `Policy: mod layer=N (…), stereo forced None on N camera(s), head='…'`.

## §2 Layer policy — owner: `Core.VRLayers`

- **One dedicated mod layer**, resolved at runtime: first *unnamed* layer scanning
  31→8 (`LayerMask.LayerToName`), fallback = built-in UI layer 5 (logged either way).
- **All mod-owned visuals** go on it via `VRLayers.Apply(go)` (recursive): hands,
  fingers, lasers, reticles, flat screen + its reticle, starting indicator, physical
  buttons, wrist HUD, settings panel + gear, dev panels, play tray, card fan/shells,
  half-selection zones, rest tokens, comfort vignette. `Apply` is a no-op without VR
  (dev-sim keeps vanilla layers so desktop cameras render the sim).
- **Game-owned objects are never re-layered** (reversibility): converted uGUI panels,
  tooltips and live card faces keep their authored layers; the UI-layer culling bit
  for those remains owned by `WorldUI.CanvasConversion` (mask request counting +
  restore).
- Layers are render-only for the mod: pokes/grabs/rays run through the
  `VRInteractables`/`UguiPokeSurfaces` registries and `GraphicRaycaster`s, and board
  picking uses the game's own selection masks — moving mod visuals off Default cannot
  break interaction (it *removes* them from stray physics rays).

## §3 Owned head camera — owner: `Rig.VRRigDriver`

- **Creation:** `GloomhavenVR.HeadCamera` GO under the rig root, seeded from the
  anchor game camera: depth = anchor + 1, far plane from the anchor, stereo = Both,
  implicit XR tracking off, pose via `TrackedPoseDriver` (center eye,
  UpdateAndBeforeRender). Mask + clear depend on the RIG KIND (see below).
- **SCENARIO rig mask:** anchor mask | mod layer (never 0 — a zero source mask falls
  back to Default | mod); clear = solid `[Rig] VoidColor` (a Skybox anchor keeps
  Skybox — that IS content).
- **Menu2D (MENU rig) mask — test #10 rule:** the **MOD LAYER ONLY**, always. The
  HMD in Menu2D contains exactly: void + FlatScreen quad + hands + starting
  indicator. The anchor mask is **never** copied — on the campaign map the anchor
  ('MapCamera') culls the whole 3D world (0xF00FFE37); copying it rendered the giant
  map 1:1 below the player while the flat screen floated inside it. Menu2D shows the
  world exclusively through the FlatScreen RT composite (§6). Clear is always forced
  SolidColor `[Rig] VoidColor` (no Skybox exception). Everything that must be
  visible in Menu2D therefore MUST be on the mod layer (`VRLayers.Apply`).
- **Mask upkeep:** re-asserted **every frame** — scenario: re-composed from the live
  anchor mask (game code may toggle scene layers; once the anchor dies, our last
  mask is re-asserted); menu: pinned to the mod-layer mask (foreign writes, e.g.
  CanvasConversion's UI bit, are stripped — menu-visible mod UI lives on the mod
  layer, not the UI layer).
- **Rig root:** `DontDestroyOnLoad` + `HideAndDontSave` — scene swaps cannot destroy
  our camera mid-flight anymore; rebuilds are policy decisions, not accidents.
- **Lifetime health check (every `Update`):** tear down + rebuild when
  - the desired rig kind changed (scenario camera appeared/vanished, VR off),
  - OUR camera or rig root was destroyed externally (paranoia — nothing game-side
    should reach them),
  - the ANCHOR camera was destroyed, or (menu) disabled/deactivated → re-anchor to
    the next best camera,
  - a scene load revealed a **better menu camera** (priority: tag MainCamera →
    `Camera.main` → highest-depth enabled backbuffer camera that isn't the UICamera).
- Every teardown and rebuild is logged **with its trigger reason**.

## §4 Hands re-home — owner: `Hands.HandsDriver`

`HandsDriver.Update` polls `VRRigDriver.RigRoot` every frame: the old hands root dies
with the old rig root (child), and `Build` re-creates the hands under the new root the
frame it appears. Rig-state-driven, never scene-driven.

## §5 Heartbeat — owner: `Core.VRHeartbeat`

Test #4's freeze left a silent log — and our logs are change-driven, so silence
proved nothing. `VRHeartbeat` (on the mod-owned `GloomhavenVR.Core` root,
DontDestroyOnLoad + HideAndDontSave) logs one `[Core]` line every 10 s,
allocation-free between beats:

```
[Core] Heartbeat #N: frames+600 | rigDriver=ok head=ok pos(…) eul(…) moved=Y | display=running input=running devices=5 hmd=tracked L=tracked R=tracked
```

Reading a freeze: **no heartbeat lines** → our driver loop is dead (host GO
destroyed/disabled). `moved=N` while wearing the HMD → pose application dead.
`display=STOPPED` → compositor/runtime-side death. `devices=0` / `UNTRACKED` →
input-subsystem or tracking loss. All driver MonoBehaviours live on mod-owned
DontDestroyOnLoad + HideAndDontSave roots (`GloomhavenVR.Core`, `.RigDriver`,
`.HandsDriver`, `.WorldUIDriver`, `.Events`); only the `Plugin` itself (coroutine
host) rides the BepInEx manager GO — INSTALL.md recommends
`HideManagerGameObject = true`.

## §6 FlatScreen RT stack capture — owner: `WorldUI.FlatScreen`

Hardware test #5: redirecting ONLY the UICamera into the FlatScreen RT loses every
other backbuffer camera's content — 'Main Camera' (menu backdrop/slideshow,
clear=Depth) and the late-created 'MainMenuVideo' still rendered to the backbuffer,
which the desktop-mirror Blit overwrites at end of frame → black background on the
quad AND the desktop.

While the FlatScreen is visible (Menu2D auto-show / ModalUI fallback), **every game
camera that renders to the backbuffer is captured into `GloomhavenVR.FlatScreenRT`**:

- **Excluded:** the rig's own head camera (§3) and cameras already targeting another
  RT ('GUI 3D Camera' → character-assembly RT).
- **Ordering:** Unity renders cameras targeting the same RT in ascending `depth`
  order — the captured stack composes inside the RT exactly like it did on the
  backbuffer. Nothing is reordered.
- **Clear flags:** only the stack's lowest-depth camera gets an OPAQUE SolidColor
  clear forced (fresh RT color/alpha are undefined — test #4); all others keep their
  own clear flags (Depth etc.) so compositing matches the game's intent. Depth ties
  ('Main Camera' and 'UI Camera' both ship depth 1.0): the UICamera-tagged camera
  never wins the base pick — it composites last per game intent.
- **Overlay SolidColor demotion (test #10, `[WorldUI] DemoteOverlaySolidClears`,
  default on):** a NON-base captured camera with a FULLSCREEN viewport and a
  SolidColor clear (the campaign map's depth-5 'Video Camera' — decompiled
  GH.Runtime/VideoCamera.cs: enabled only around `PlayFullscreenVideo`, renderMode
  CameraNearPlane, its clear is merely the black backdrop behind fullscreen videos)
  gets its clear demoted to Depth while captured. Rendering last into the RT, the
  vanilla clear wipes the whole composite black whenever the near-plane video blit
  doesn't land in the redirected RT (test #10: black map + black encounter
  backgrounds). Demotion never hides content — the video frame still draws on top —
  and diverges from vanilla only in letterbox backdrops. Sub-rect SolidColor cameras
  keep their clear (`camera.rect` respected: a viewport-limited background IS
  content). Original flags restored on release; every demotion and every captured
  camera's enable/disable flip is logged.
- **Pump:** per-tick capture sweep (shared no-alloc scan buffer) — late-created
  cameras (MainMenuVideo) are captured the frame they appear; redirects and the base
  clear are re-asserted every tick against game-side rewrites.
- **Reversibility:** `targetTexture` and the base camera's clear flags are restored
  on FlatScreen hide, scene change (bus event → release, re-capture next tick), VR
  off and hot reload.
- **No fight with §1:** `VRCameraPolicy` owns exactly `stereoTargetEye`/XR tracking;
  the FlatScreen owns exactly `targetTexture`/clear of captured cameras. Stereo-None
  + RT target are complementary properties of the same camera.
- **Log lines:** `FlatScreen stack capture: '<cam>' → RenderTexture (depth …)` per
  camera, `FlatScreen stack base: '<cam>' … clears the RT`, and the camera inventory
  marks captured cameras with `[RT stack]` plus a stack summary line.

## Expected log shape on a healthy run

```
[Core] Mod layer resolved: 27 (…)
[Rig] Menu rig built at vantage of camera 'Main Camera' (1:1 scale, owned head camera 'GloomhavenVR.HeadCamera': …) — trigger: initial.
[Core] Stereo policy: 'Main Camera' forced to StereoTargetEyeMask.None (menu rig built; head 'GloomhavenVR.HeadCamera' keeps the HMD).
[Core] Stereo policy sweep (menu rig built): forced None on 3 camera(s), …
[Hands] Hands built under 'GloomhavenVR.VRRig'.
[WorldUI] FlatScreen stack capture: 'Main Camera' → RenderTexture (depth 1.0, clear Depth kept unless base).
[WorldUI] FlatScreen stack capture: 'UI Camera' → RenderTexture (depth 1.0, clear Depth kept unless base).
[WorldUI] FlatScreen stack base: 'Main Camera' (depth 1.0) clears the RT (clear Depth → SolidColor opaque black).
[WorldUI] FlatScreen quad placed: pos=…, … | head 'GloomhavenVR.HeadCamera' pos=…, fwd=…, mask=…, clear=SolidColor, stereo=Both.
[Core] Heartbeat #1: frames+600 | rigDriver=ok head=ok … | display=running input=running devices=5 hmd=tracked L=tracked R=tracked
```
