# Camera & Layer Policy (fix/rig-camera-ownership + fix/menu-freeze-visuals + fix/menu-stack-input)

> **Audited 2026-09-08 at ModBuild 483.** Every type, file path, method, config key,
> constant and log marker named below was checked against the tree and still exists; the
> constants quoted (mod-layer sweep every 30 frames, heartbeat every 10 s,
> `BackplaneGapMeters` 6 cm, `NoUiFallbackSeconds` 3 s, 63 mm IPD fallback, the 64-canvas /
> 8-per-frame repair budget) match their declarations. What an audit of this kind CANNOT
> establish is that the *behaviour* described is still what the code does at each of those
> names — for that, the hardware-test sections in `docs/TESTING-*.md` and the source's own
> comments are the authority. The newest section here is §7, written at ModBuild 426.

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
- Applies to ALL THREE rig kinds — `RigKind { None, Scenario, Menu, Map }`
  (`Rig/VRRigDriver.cs`, selection in `UpdateBody`) — one owned stereo camera
  everywhere, game cameras always desktop-only.
- **Map rig** (`Rig/VRRigDriver.MapRig.cs`): the 3D campaign map. Reached ONLY
  through `MapRoomDriver.Wanted`, a POSITIVE map-open signal (a live
  `MapChoreographer` with an active worldMap/cityMap), never through "not a
  scenario" — and ordered AFTER the scenario test, so a live board always wins.
  Seat and scale come from the parchment renderer's world bounds; the anchor
  camera is read only for a horizontal direction and a culling mask, both with
  pure fallbacks. Its head mask is the map camera's OWN mask (see §3).
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
- **No stand-down:** even with no rig head at all, game cameras stay stereo-None
  (`VRCameraPolicy.Sweep` forces None whenever the head is null). Game cameras
  never render stereo, period.
- **Reversibility:** originals are recorded per camera and restored by
  `VRCameraPolicy.RestoreAll()` on VR-off / hot reload (`VRRigDriver.OnDestroy`).
- **Log lines:** `Stereo policy: '<cam>' forced to StereoTargetEyeMask.None (…)` per
  camera, plus a per-sweep summary; the camera inventory prints
  `Policy: mod layer=N (…), stereo forced None on N camera(s), head='…'`.

## §2 Layer policy — owner: `Core.VRLayers`

- **One dedicated mod layer**, resolved at runtime: first *unnamed* layer scanning
  31→8 (`LayerMask.LayerToName`), fallback = built-in UI layer 5 (logged either way).
- **All mod-owned visuals** go on it via `VRLayers.Apply(go)` (recursive): hands,
  fingers, lasers, reticles, the flat screen quad + its background quad + the
  starting indicator, physical buttons, wrist HUD, dev panels, play tray, card
  fan/piles/tray caps, half-selection zones, the map room's table button rail and
  wrist, loading indicator, voice badge, the alternative sky/room, and every
  remote-player visual (avatar, name tag, control board, hand/item fans). `Apply`
  is a no-op without VR (dev-sim keeps vanilla layers so desktop cameras render
  the sim). The class doc on `Core/VRLayers.cs` is the policy of record; the call
  sites are the inventory — grep `VRLayers.Apply` rather than trusting this list
  to stay complete. (The in-VR settings panel and the comfort vignette used to be
  named here. Both TYPES are gone — `WorldUI.SettingsPanel` was replaced by the VR
  options tab injected into the GAME's own options window, which is game-owned uGUI
  and therefore deliberately NOT re-layered; no comfort vignette ships.)
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
- **SCENARIO rig mask:** source mask | mod layer (never 0 — a zero source mask falls
  back to Default | mod); clear = solid `[Rig] VoidColor` (a Skybox anchor keeps
  Skybox — that IS content). The source is the live ANCHOR mask by default. **Opt-in
  narrowing (2026-07 submission pass, `[Optimize] HeadMaskFromScenarioCamera`,
  default off):** the scenario anchor is 'Main Camera' with mask 0xFFFFFFFF while the
  camera the flat game really draws the dungeon with excludes thirteen layers, so
  following the anchor makes the head camera cull and submit a surplus the game never
  draws — twice per frame under MultiPass. With the key on, the source is the
  ScenarioCamera's own mask (when non-zero) ORed with a `MaskNarrowingFloor` that adds
  the mod layer and the UI layer back unconditionally. Narrowed or not, the composed
  mask is logged on change.
- **Menu2D (MENU rig) mask — test #10 rule:** the **MOD LAYER ONLY**, always. The
  HMD in Menu2D contains exactly: void + FlatScreen quad + hands + starting
  indicator. The anchor mask is **never** copied — test #10 copied a 3D-world anchor
  mask into the menu head camera and rendered a giant 1:1 world below the player
  while the flat screen floated inside it. Menu2D shows the world exclusively through
  the FlatScreen RT composite (§6). (The campaign map is no longer an example of that
  failure: since the map rig it is `RigKind.Map`, whose head mask IS the map camera's
  own mask **by design** — `MapRoomDriver.ResolveMapMask`, because there the player
  looks at the real map geometry, not at a photograph of it. That branch is
  unreachable outside a positively-detected open map, and `ResolveMapMask`'s own last
  fallback is the mod layer alone, so even a total failure to read a camera degrades
  to exactly the menu picture rather than to a broken menu.) Clear is always forced
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
  - the ANCHOR camera was destroyed, or (menu **or map**) disabled/deactivated →
    re-anchor to the next best camera,
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
- **Overlay SolidColor demotion (test #10, UNCONDITIONAL — the
  `[WorldUI] DemoteOverlaySolidClears` dial is gone, user ruling 2026-08-13,
  because its OFF let that clear wipe the composited campaign map to black, i.e. it
  could switch off the only route into a scenario from the options menu):** a
  NON-base captured camera with a FULLSCREEN viewport and a
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

### §6.1 Screen layer split — owner: `WorldUI.FlatScreen` (test #18)

The captured stack is not composited onto ONE surface any more. Screen-Space-Camera
canvases render **exclusively through their assigned camera**, so no other camera —
in particular no §6.2 stereo mirror — can ever reproduce them: a right-eye RT
composed by mirrors alone lost the entire menu UI (test #18, a left-eye-only menu).
The stack is therefore split in two, and the split is **unconditional** —
`WorldUIConfig.ScreenLayerSplit` is a `const bool` since the 2026-08-22 settings
audit, not a bound key, because its off state is a one-eyed main menu.

- **UI glass layer:** UI-classified cameras (UICamera-tagged / orthographic — the
  canvases' `worldCamera` targets) are retargeted onto a mod-owned
  `GloomhavenVR.FlatScreenRT.UI`, same dimensions as the background RT, cleared to
  TRANSPARENT. Their canvases follow automatically. The screen quad shows that RT
  through an alpha-blended material (`Sprites/Default` → `UI/Default`): whatever the
  UI cameras did not draw stays see-through. Identical in both eyes, at the screen
  plane.
- **Background layer:** 3D perspective cameras keep compositing the left RT (§6
  unchanged) and are the only cameras §6.2 mirrors. They are shown on a second,
  opaque `GloomhavenVR.FlatScreen.Background` quad `BackplaneGapMeters` = **6 cm**
  real behind the glass (3 → 6 cm after test #19: the physical separation is the
  strongest depth cue of the whole split and costs nothing). The background quad is
  grown by `1 + gap/distance` so it subtends the same angle from the head — no edge
  inset.
- **The pointer plane stays the glass.** One authoritative quad: `TickPointer` /
  `TickPoke` and the RT pixel mapping are untouched, and both RTs share one
  resolution by construction.
- **Fallbacks, all to the proven single-RT path** (every camera composites the left
  RT, the screen quad goes opaque, stereo stays OFF because mirrors cannot carry the
  UI — degraded, but never one-eyed and never black):
  - `_splitFailed` — glass-RT or shader creation failed. Latched for the rest of this
    Show; cleared on the next `Show`.
  - `_splitNoUi` — no UI camera was captured for `NoUiFallbackSeconds` = 3 s while
    routing. **Scene-scoped** (test #20: the UI-less loading scene between intro and
    menu latched it for the whole Show, which kept the split — and with it stereo and
    the video depth layer — off for the entire main menu), so it re-arms on stack
    release (scene change) or the moment a UI-classified camera IS captured.
- **Pump/reversibility:** engaged/disengaged at the top of every capture sweep;
  teardown destroys the glass RT, the glass material and the background quad and puts
  the opaque material back on the screen quad. Engage, every fallback and every
  teardown reason are logged.

### §6.2 Stereo screen — owner: `WorldUI.FlatScreenStereo` (test #15 #7)

While the FlatScreen is visible in VR (`[WorldUI] StereoScreen`, default on;
`ScreenDepthStrength` scales it, 0 = mono), the screen renders **with stereo
depth** — a 3D-movie/window effect. The captured 3D cameras stay exactly as §6
describes and keep composing the (left) RT; per-eye rendering is added entirely
with mod-owned objects. It requires §6.1's split to be engaged (mirrors cannot
reproduce Screen-Space-Camera UI); without it the screen stays mono:

- **Right-eye RT** `GloomhavenVR.FlatScreenRT.Right` (same dimensions as the left
  RT — the pointer pixel mapping and desktop mirror keep using the LEFT RT and are
  untouched; the monitor stays monoscopic).
- **One mirror camera per captured 3D camera** under the hidden DontDestroyOnLoad root
  `GloomhavenVR.StereoScreenMirrors`, bare cameras whose state is FIELD-COPIED from
  the live source every tick: transform, projection matrix, culling mask **minus
  the mod layer** (they must never see the quad/hands — feedback), the EFFECTIVE
  clear flags (i.e. after §6's base-clear force and overlay demotion), rect, depth,
  clip planes, enabled state. Same `depth` ⇒ the right RT replays the stack in the
  same compositing order.
- **ONLY 3D CAMERAS ARRIVE HERE (test #18).** The pre-#18 zero-offset "MONO mirror"
  path for UI/orthographic cameras produced an EMPTY right-eye UI, because
  Screen-Space-Camera canvases render only through their own camera. §6.1's split
  now carries the UI on its glass RT — identical in both eyes at the screen plane —
  and syncs only the 3D background cameras into this class. While the split is not
  active, stereo stays off entirely (single mono RT — degraded, never one-eyed).
- **Eye geometry:** mirrors sit at `source + right × separation` with an off-axis
  projection (lens) shift that converges at the screen's own distance.
  `separation = IPD × ScreenDepthStrength × WorldScale × ScreenParallaxScale` and
  `convergence = ScreenDistance × WorldScale × ScreenParallaxScale` — the rig scale
  is the mod's one
  canonical real↔game relation (1 in the menu rig), so scene content at the
  screen-equivalent distance shows zero disparity and scene-infinity stays a few cm
  under the divergence limit. IPD is sampled from the XR head device (63 mm
  fallback), plausibility-clamped.
- **Per-eye quad texture (MultiPass):** a `Camera.onPreRender` hook swaps the
  material's texture per head-camera eye pass via `camera.stereoActiveEye`
  (Left → RT-L, Right → RT-R), with an automatic per-frame pass-parity fallback if
  a runtime reports Mono; the observed pattern is logged once per activation. The
  material is the one on §6.1's **background** quad (`FlatScreen` passes
  `_backRenderer` into `Tick`) — the glass quad in front is per-definition identical
  in both eyes and is never swapped.
- **Near-plane video — depth layer, suspension as the fallback:** VideoPlayers in
  CameraNearPlane/FarPlane mode blit only into their HOST camera's target, so a
  mirror can never reproduce them. While any captured camera hosts an enabled such
  player ('MainMenuVideo' ambient movies, the campaign 'Video Camera', the intro),
  the mirrors stop and both eyes instead show SHIFTED copies of the left RT —
  `[WorldUI] VideoDepthLayer` places the 2D video frame at a chosen depth behind the
  screen, disparity `p = IPD·V/(D+V)` split as a UV shift per eye. **Fallback
  suspension** (no left RT, shifted-RT creation failure, `VideoDepth` 0, or
  `VideoDepthLayer` off): both eye passes show the left RT unshifted. Either way the
  vanilla player keeps drawing into the left RT, so the result is never one-eyed and
  never black. Logged on every flip. A separate **intro guard** force-suspends with
  zero shift on pre-menu scenes (test #17: the intro's render path is not
  mirror-reproducible and not observable).
- **No policy fights:** mirrors are stereo-None with a targetTexture ⇒ invisible to
  §1's sweep by construction, and §6's capture sweep skips them
  (`targetTexture != null`). `XRDevice.DisableAutoXRCameraTracking` is set anyway.
- **Lifecycle/reversibility:** mirrors die with the captured stack (scene change →
  rebuild next sweep; late arrivals get a mirror the tick they are captured); hide,
  VR off, config off and hot reload run the full teardown (mirrors + right RT
  destroyed, hook unhooked, quad texture back on the left RT). With
  `StereoScreen=false` or strength 0 nothing is ever created — the §6 mono path is
  byte-identical.
- **Perf:** menu-scene rendering doubles while the screen is visible (menu-only
  surface — acceptable); the video suspension removes the extra cost exactly when a
  fullscreen video already dominates. No per-frame allocations.
- **Log lines:** `STEREO SCREEN ACTIVE — …`, `Stereo mirror created for '<cam>':
  3D/MONO …`, `Stereo screen eye passes observed: Left, Right — …`,
  `Stereo screen SUSPENDED/RESUMED — …`, `Stereo screen deactivated (<reason>)`.

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

## §7 Camera OWNERSHIP on game canvases — owner: `WorldUI.CanvasConversion` (ModBuild 426)

**The invariant.** *No canvas the mod does not own may reference a mod-owned camera, at any
time.* A `ScreenSpaceCamera` canvas is parked as ordinary geometry at its `planeDistance` in
front of whatever camera it names, so a GAME canvas naming `GloomhavenVR.HeadCamera` is
head-locked in the eye by construction. That is the left-edge rim the user reported from
ModBuild 418 on: *"Wenn man schnell hintereinander die Optionstaste drueckt erscheint am linken
Rand des auges so ein Rand der dem Kopf folgt statt dem Optionsmenu."*

**ModBuild 424's guard was necessary and not sufficient — do not re-derive this.** 424 added
`CanvasConversion.SafeOriginalWorldCamera`: a recorded "original" `Canvas.worldCamera` may never
be a value the mod wrote. It fired **23 times** in the 425 hardware log (a naive
`grep -c '] MODAL ADOPT CAMERA LEAK'` returns 27 — four of those lines only quote the marker),
and the eye census in the same log still read `EYE CENSUS CANVAS 'UI Map Esc Menu' ROOT …
mode=ScreenSpaceCamera … cam='GloomhavenVR.HeadCamera' … layer=5 … alpha 0.845`. A guard that
refuses a bad RECORDED value and leaves the bad LIVE value standing corrects nothing.

**Why it could not work, from the 425 log.** `Convert` re-parents the game window under the
mod's world-space host and only then adopts its canvases, and Unity reports a nested canvas's
`worldCamera`/`sortingOrder` from its ROOT — ours. Two independent readings in the log:
23 of 24 adoptions "leaked", and every one of them reports `sortingOrder=1000`, the host's SEED
order, not the canvas's own (the census reads the same ESC-menu canvas at `order=1200` once it
is a root again). The one adoption that did NOT leak is `UI Party Inventory Item Tooltip`, the
one canvas whose `overrideSorting` the game holds TRUE — i.e. the one that IS its own sorting
root and therefore reports its own values. `Release` then restored the camera in its
adopted-canvas loop, which runs BEFORE the re-parent, so the write was discarded while the
canvas was still nested.

**What is enforced now** (`WorldUI/Conversion/CanvasConversion.4b.CameraOwnership.cs`):

- **Capture before the re-parent** — `PreCaptureGameCameras`, called from `Convert` in the last
  frame in which the game's own values are readable. The recorded original is an observation,
  not `FindGameUiCamera`'s guess.
- **Restore after the re-parent, then read back** — `RestoreAdoptedCameras` runs at the end of
  `Release`, writes, re-reads, and writes the game's own UI camera a second time if the live
  value is still ours. Both values are on the log line.
- **A bounded repair pass** — `TickCameraOwnership` walks the canvases this mod has ever pointed
  at a mod camera (its own list, capped at 64, 8 entries per frame, no scene sweep) and corrects
  any that no live panel holds. World-space canvases are skipped: there `worldCamera` is the
  EVENT camera and the mod binds the head camera on purpose (`WorldTooltips`).
- **A belt that does not depend on any of that** — while the mod has handed a window back and the
  GAME reports it CLOSED, the window's own root canvas is held `enabled = false` through a
  one-for-one ledger and handed back the instant the game reopens it (`UIWindow.IsOpen`), the mod
  re-converts it, or the module shuts down. Never a timer, never a frame count. The pause menu
  must always be openable — that is what the lift conditions, and not a timeout, exist for.

**Log lines:** `] MODAL ADOPT CAMERA LEAK … LIVE VALUE AFTER THIS GUARD: '<cam>'`,
`] CANVAS CAMERA RESTORE (<where>): … live camera BEFORE '<cam>' … AFTER '<cam>' … VERDICT: …`,
`] CANVAS CAMERA REPAIR #n …`, `] MODAL DARK HOLD LIFTED (n of m taken this session) …`,
`] MODAL RELEASE OWNERSHIP for '<window>': …` — the last of these says WHICH HALF did the work,
so a log can state whether the belt is still load-bearing or can be retired.
