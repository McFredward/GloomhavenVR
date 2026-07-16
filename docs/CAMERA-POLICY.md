# Camera & Layer Policy (fix/rig-camera-ownership + fix/menu-freeze-visuals)

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
  anchor game camera: mask = anchor mask | mod layer (never 0 — a zero source mask
  falls back to Default | mod), depth = anchor + 1, far plane from the anchor,
  clear = solid dark grey `HeadVoidColor` (a Skybox anchor keeps Skybox — that IS
  content), stereo = Both, implicit XR tracking off, pose via `TrackedPoseDriver`
  (center eye, UpdateAndBeforeRender).
- **Mask upkeep:** re-composed **every frame** from the live anchor mask (game code
  may toggle scene layers); once the anchor dies, our last mask is re-asserted.
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

## Expected log shape on a healthy run

```
[Core] Mod layer resolved: 27 (…)
[Rig] Menu rig built at vantage of camera 'Main Camera' (1:1 scale, owned head camera 'GloomhavenVR.HeadCamera': …) — trigger: initial.
[Core] Stereo policy: 'Main Camera' forced to StereoTargetEyeMask.None (menu rig built; head 'GloomhavenVR.HeadCamera' keeps the HMD).
[Core] Stereo policy sweep (menu rig built): forced None on 3 camera(s), …
[Hands] Hands built under 'GloomhavenVR.VRRig'.
[WorldUI] FlatScreen: UICamera 'UI Camera' → RenderTexture (clear Depth → SolidColor opaque black while redirected).
[WorldUI] FlatScreen quad placed: pos=…, … | head 'GloomhavenVR.HeadCamera' pos=…, fwd=…, mask=…, clear=SolidColor, stereo=Both.
[Core] Heartbeat #1: frames+600 | rigDriver=ok head=ok … | display=running input=running devices=5 hmd=tracked L=tracked R=tracked
```
