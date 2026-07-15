# Camera & Layer Policy (fix/rig-camera-ownership)

Outcome of hardware test #3 (Quest 3 + Virtual Desktop, 2026-07): after the intro the
HMD went permanently grey. Three ownership gaps compounded — this document defines the
policies that close them and names the SINGLE owner of each.

## Root causes (from BepInEx log + Player.log)

1. **Rig didn't survive the menu scene swap.** The menu rig was built around
   Gloomhaven_unified's 'Camera'. The MainMenu load *disabled* (not destroyed) that
   camera — the old `_camera == null` check never fired, so the rig froze around a
   dead camera and no rebuild was ever logged. (The odd "head y=-1.09" in the quad
   placement log was the stale TrackedPoseDriver of that disabled camera; the menu
   recenter math itself is correct: rig = anchor − yaw·headLocal ⇒ head lands on the
   anchor, with the rig root legitimately ~eye-height below it.)
2. **Foreign cameras hijacked the HMD.** MainMenu's 'Main Camera' arrived with
   stereo=Both + backbuffer and rendered into the headset next to/instead of the rig.
3. **Culling-mask inheritance was broken.** The rig copied the game camera's mask —
   'Camera' shipped mask 0x00000000 (renders NOTHING) and menu cameras cull 0x20
   (UI layer only), so layer-0 mod objects (hands, lasers, quad) were invisible; the
   old per-object "move to layer 5" hack fought the masks instead of owning a layer.
4. **Hands hung under the dead rig** — downstream of (1); see §4.

## §1 Stereo exclusion — owner: `Core.VRCameraPolicy`

While VR runs and a rig head camera exists, **only the rig head camera renders
stereo**. Every other active camera — including RenderTexture cameras such as
'GUI 3D Camera', where stereo=Both is wasted double rendering — is forced to
`StereoTargetEyeMask.None` (renders to the main/desktop display only) with
`XRDevice.DisableAutoXRCameraTracking(cam, true)`.

- **Pump:** `VRRigDriver` sweeps on every scene load, after every rig (re)build, and
  every 30 frames (catches cameras created mid-scene). Idempotent; no per-frame
  allocations (`Camera.GetAllCameras` into a reused buffer).
- **Stand-down:** with no rig head (e.g. `[Rig] MenuRig=false`), the sweep does
  nothing — a vanilla stereo camera beats a void HMD.
- **Reversibility:** originals are recorded per camera and restored by
  `VRCameraPolicy.RestoreAll()` on VR-off / hot reload (`VRRigDriver.OnDestroy`).
  `Reclaim(cam)` hands a camera back to the rig when it is promoted to head.
- The old FlatScreen-owned UICamera guard was **removed** — one owner, one policy.
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

## §3 Head camera mask & lifetime — owner: `Rig.VRRigDriver`

- **Mask:** head camera cullingMask = tracked game camera's mask OR
  `VRLayers.ModLayerMask`, **never 0** (a zero source mask falls back to
  Default | mod). Re-asserted every frame (game code / CanvasConversion may rewrite
  the mask); the original mask is restored on teardown. Applies to menu AND scenario
  rigs.
- **Stereo:** the head is set to `Both` on adoption (via `VRCameraPolicy.Reclaim`),
  restored on teardown.
- **Lifetime health check (every `Update`):** tear down + rebuild when
  - the desired rig kind changed (scenario camera appeared/vanished, VR off),
  - the head camera was **destroyed**,
  - the head camera was **disabled/deactivated** (the test-#3 killer),
  - the rig root was destroyed externally,
  - a scene load revealed a **better menu camera** (priority: tag MainCamera →
    `Camera.main` → highest-depth enabled backbuffer camera that isn't the UICamera).
- Every teardown and rebuild is logged **with its trigger reason**.

## §4 Hands re-home — owner: `Hands.HandsDriver`

`HandsDriver.Update` polls `VRRigDriver.RigRoot` every frame: the old hands root dies
with the old rig root (child), and `Build` re-creates the hands under the new root the
frame it appears. Rig-state-driven, never scene-driven — no event subscription needed;
the test-#3 hang was purely the upstream rig never rebuilding.

## Expected log shape on a healthy run

```
[Core] Mod layer resolved: 31 (…)
[Rig] Menu rig built around camera 'Main Camera' (… mask 0x00000020 → 0x80000021 …) — trigger: initial.
[Core] Stereo policy sweep (menu rig built): forced None on 2 camera(s), …
[Rig] Menu rig torn down (head camera 'Camera' disabled/deactivated) — menu camera restored.
[Rig] Menu rig built around camera 'Main Camera' (…) — trigger: head camera 'Camera' disabled/deactivated.
[Hands] Hands built under 'GloomhavenVR.VRRig'.
[WorldUI]   Policy: mod layer=31 (mask 0x80000000), stereo forced None on 2 camera(s), head='Main Camera'.
```
