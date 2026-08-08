# Invariants — `src/GloomhavenVR/WorldUI/`

> Companion to `CHARTER.md`. **This file is evidence, not opinion.** Every entry
> below is code that looks arbitrary and is in fact the residue of a bug that
> took one or more Quest 3 hardware rounds to corner. Recovering the reason is
> the point; the refactor's job is to preserve it.
>
> **Symbols only — no line numbers.** Files are being edited concurrently.
>
> Reading order: entries are grouped by area. `Breaks if:` names the specific
> "simplification" that would put the bug back. When in doubt, the charter's
> default answer applies: **no**.

---

## 1. Flat screen, camera stack, stereo compositor

### Screen-space-camera canvases can only render through their own camera
- **Where:** `FlatScreen.CaptureStack`, `FlatScreen.IsUiCamera`, `FlatScreen.SetSplitRouting`, `FlatScreen.ClearUiRt`, `FlatScreenStereo` mirror creation
- **Rule:** the captured camera stack is **split**: UI-tagged/orthographic cameras retarget onto a mod-owned transparent **glass RT** drawn on its own quad; 3D cameras composite into the left/base RT on an opaque quad `BackplaneGapMeters` behind it. Both eyes show the *same* glass RT.
- **Why:** a `ScreenSpace-Camera` canvas renders **exclusively** through its assigned camera. A per-eye mirror camera is a different camera, so it can never reproduce the menu UI — test #18 showed the menu in the LEFT eye only, and the right eye had no UI at all once video-depth replaced the stereo suspension that had been masking it.
- **Established by:** `711a85d` feat(worldui): screen layer split — UI glass over stereo background
- **Breaks if:** "simplify" by mirroring all cameras uniformly, or by dropping the glass RT and letting the mirrors render the UI. The right eye goes blank.
- **Confidence:** high

### The no-UI split watchdog latch must be SCENE-SCOPED, not global
- **Where:** `FlatScreen.TickNoUiWatchdog` (`_splitNoUi` vs `_splitFailed`), re-arm in `FlatScreen.ReleaseStack` and on UI-camera capture
- **Rule:** two separate latches. `_splitNoUi` (evidence: "3 s of split routing with no UI camera captured") **re-arms** on stack release / scene change / the first UI-classified camera capture. `_splitFailed` (evidence: "RT or shader creation failed") persists until the next `Show`.
- **Why:** the UI-less loading scene between intro and menu runs a bare `Camera` with mask 0 for ~3 s. With one global latch it tripped `_splitFailed` permanently, so the main menu never re-engaged the split, `FlatScreenStereo` never ticked, and the *entire* stereo/video-depth chain was structurally unreachable in the menu — test #20 logged zero "Video depth layer ENGAGED" lines all session.
- **Established by:** `83e8ba3` fix(worldui): scene-scope the no-UI split fallback — the loading scene's latch kept the whole menu mono
- **Breaks if:** the two latches are merged back into one bool "because they mean the same thing". They do not: one is scene evidence, one is a hard failure.
- **Confidence:** high

### The base captured camera must be force-cleared OPAQUE, and only the base
- **Where:** `FlatScreen.SelectBases`, `FlatScreen.TickStackClears`, `FlatScreen.OpaqueBlack` / `TransparentBlack`
- **Rule:** among same-target cameras only the **lowest-depth** camera gets a forced `SolidColor` clear; depth ties never pick the `UICamera`-tagged camera. The 3D background base clears **opaque**; the UI stack's base clears **transparent**.
- **Why:** `UI Camera` ships `clearFlags = Depth`. Redirected into a fresh RenderTexture that leaves the colour buffer (alpha ≈ 0) undefined, so the alpha-blended quad rendered nothing in the HMD while the alpha-ignoring desktop blit looked perfect (test #4). The glass RT is the opposite case — it *must* stay transparent or it hides the 3D background.
- **Established by:** `3e24f12` fix(worldui): flat screen visible in HMD — opaque RT clear + alpha-ignoring quad shader; `5edc0a8` fix(worldui): FlatScreen captures the FULL backbuffer camera stack into its RT
- **Breaks if:** the clear policy is unified ("all captured cameras clear the same way"), or the base selection is simplified to "camera[0]".
- **Confidence:** high

### The screen quad shader is `Hidden/BlitCopy`, deliberately
- **Where:** `FlatScreen.Show` (quad material creation)
- **Rule:** opaque `_MainTex` copy, `Blend Off`, ignores alpha. `Sprites/Default` is the fallback only.
- **Why:** `Hidden/BlitCopy` is an always-included internal shader *verified shipped* (the game itself `Shader.Find`s it, and `Graphics.Blit` depends on it). `Unlit/Texture` is stripped from the shipped build. There is no opaque keyword/MPB variant of `Sprites/Default`.
- **Established by:** `3e24f12`; original stripping diagnosis in `fdcf3db` fix(worldui): menu blackscreen
- **Breaks if:** someone "modernises" to `Unlit/Texture` or `Universal/…`. Black screen from launch.
- **Confidence:** high

### The backdrop quad's render queue is mode-dependent
- **Where:** `FlatScreen.TickBackdropDepth`
- **Rule:** in menu-like modes the opaque backdrop is pushed into the **Background** queue; scenario overlay behaviour is unchanged.
- **Why:** `Hidden/BlitCopy` is `ZTest Always / ZWrite Off`. In the Geometry queue it rendered *after* the nearer hands and, ignoring depth, painted over them — hands were invisible in front of the menu screen.
- **Established by:** `880b8ed` fix(render): hands over menu screen…
- **Breaks if:** the queue is made a constant.
- **Confidence:** high

### Quad + reticle are `DontDestroyOnLoad` and self-heal
- **Where:** `FlatScreen.Show`, `FlatScreen.Tick` (destroyed-quad detection)
- **Rule:** the quad is DDOL and `Tick` rebuilds it if an external destroy killed it.
- **Why:** the boot flow loads scene 0 → `Intro` → menu **all in Single mode**. A plain scene-object quad was destroyed by the first swap while `_visible` stayed true and the UICamera stayed redirected — black HMD *and* black desktop, forever.
- **Established by:** `fdcf3db`
- **Breaks if:** DDOL is dropped as "unnecessary since we re-place on scene load".
- **Confidence:** high

### Video depth is done by shifted RT copies — the `VideoPlayer` is never touched
- **Where:** `FlatScreenStereo` per-eye video shift path (`VideoOverscan`, `MaxVideoDisparityMeters`)
- **Rule:** while a camera-plane video is active, both eyes show **shifted copies of the left RT** (one full-RT blit per eye through an intermediate blit RT), with `~3 %` overscan. The player's `renderMode`/`targetTexture` are **never** re-routed.
- **Why:** player re-routing failed on hardware **twice**. Test #18: a mid-play `renderMode` switch never re-opens the player's internal render path → black in both eyes. Test #21: the APIOnly re-kick left the player prepared-but-never-playing (`isPlaying=False, frame -1`) → video reached no eye at all.
  The intermediate blit RT (rather than a quad-material UV offset) exists because `Sprites/Default` — the quad shader's fallback — **ignores `_MainTex_ST`**, which would silently drop the left eye's half of the disparity.
- **Established by:** `b7586cf` fix(worldui): video depth via shifted left-RT copies — never touch the VideoPlayer
- **Breaks if:** anyone re-introduces `VideoPlayer.renderMode = RenderTexture` "to save a blit", or replaces the intermediate blit with a material UV offset.
- **Confidence:** high

### `VideoOverscan` and `MaxVideoDisparityMeters` are derived, not taste
- **Where:** `FlatScreenStereo.VideoOverscan` (1.03), `FlatScreenStereo.MaxVideoDisparityMeters` (0.055)
- **Rule:** overscan margin covers the default shift (0.0083 UV, 1.75× headroom) *and* the worst clamped shift (0.0125 UV); disparity is clamped below the ~63 mm divergence limit.
- **Why:** without the margin the shift exposes **void at the RT edges**; without the clamp the eyes diverge past comfort. Both numbers were re-derived when `VideoDepth` went 0.8 → 2.2 m.
- **Established by:** `845ab06` fix(worldui): stronger menu-pan depth — video recedes 2.2 m, glass gap 6 cm
- **Breaks if:** rounded to "1.0 / no clamp".
- **Confidence:** high

### Pre-menu scenes force-suspend stereo (identical eyes)
- **Where:** `FlatScreen.IsPreMenuScene`, `FlatScreenStereo` intro guard
- **Rule:** while the screen shows a pre-menu scene, stereo suspends (both eyes = the left RT) **unless** the video-depth layer routed a player that frame.
- **Why:** the intro's render path is unverifiable — `IntroPlayer` only calls `_player.Play()`; the `VideoPlayer` binding and canvas mode are **serialized in `Intro.unity`**, invisible to code. Test #17 showed a one-eyed intro with no discovery line at all: frames reach the left RT through a camera-bound path the mirror cannot replay. The intro is flat 2D, so identical eyes lose nothing.
- **Established by:** `2f38101` fix(worldui): intro guard — force identical eyes on pre-menu scenes
- **Breaks if:** the guard is removed because "the video path works now". It works for the *menu* video, not the intro.
- **Confidence:** high

### Camera-plane `VideoPlayer` discovery needs BOTH recovery paths
- **Where:** `FlatScreenStereo` video re-check (`VideoRecheckIntervalFrames` 15) and global sweep (`VideoSweepIntervalFrames` 30)
- **Rule:** entries with no cached player re-run `GetComponent` every ~15 frames; a global `FindObjectsOfType<VideoPlayer>` sweep every ~30 frames matches near/far-plane players to captured cameras by `targetCamera` **or** host GameObject.
- **Why:** the intro's `VideoPlayer` binds to its camera via `VideoPlayer.targetCamera` **from a different GameObject**, so a one-shot `GetComponent` on the camera GO never found it. No suspension → the right-eye mirror rendered an empty scene (camera mask `0x0`).
- **Established by:** `ddf268f` fix(worldui): robust camera-plane video discovery — intro renders in both eyes
- **Breaks if:** collapsed to a single discovery strategy.
- **Confidence:** high

### Fullscreen SolidColor clears on NON-base captured cameras are demoted
- **Where:** `FlatScreen.TickStackClears` (`DemoteOverlaySolidClears`)
- **Rule:** a captured, non-base camera with a **fullscreen viewport** *and* a `SolidColor` clear is demoted to Depth-only while captured. A camera with a **sub-rect** `camera.rect` keeps its clear.
- **Why:** the campaign map's `Video Camera` was captured at depth 5.0 with its fullscreen clear intact. Rendering last into the RT, that clear wiped the whole composite (map + UI) black whenever the near-plane video blit did not land — the reported black map / black encounter backgrounds. A viewport-limited background, by contrast, **is** content.
- **Established by:** `54188a9` fix(worldui): RT stack — demote overlay SolidColor clears
- **Breaks if:** the `camera.rect` distinction is dropped as an edge case.
- **Confidence:** high

### Desktop scrub is gated on "flat screen hidden", not on VR
- **Where:** `FlatScreen.TickDesktopCameraScrub`, `FlatScreen.ReleaseDesktopScrub`
- **Rule:** every **non-head** backbuffer camera is retargeted onto a throwaway sink RT **only while the flat screen is hidden**; the rig `HeadCamera` is always excluded; fully reversible.
- **Why:** `VRCameraPolicy` forces every game camera to `StereoTargetEyeMask.None` (desktop-only). In `Menu2D`, `CaptureStack` redirects them into the flat-screen RT — but in a **scenario** the flat screen is hidden, `CaptureStack` never runs, and the game's flat 2D UI composites straight onto the monitor in parallel with the XR mirror. The game UI must still *render* (into the sink) because the canvas conversion consumes it.
- **Established by:** `530c1b0` fix(worldui): ITEM9 scrub game cameras off the desktop backbuffer in scenario state
- **Breaks if:** the sink is replaced by disabling the cameras. The converted panels go dark.
- **Confidence:** high

### The campaign map renders through a PRIVATE RT, forward, with a scoped material override
- **Where:** `FlatScreenStereo` map albedo camera / `EnsureMapRt` / `ReleaseMapRt` / `ApplyWorldMapOverride`
- **Rule:** a mod-owned **forward** camera draws the real parchment mesh into a **private** `_mapRt` (never the shared base RT), through per-submesh `GloomhavenVR/MapUnlit` material overrides applied in `onPreRender` and restored in `onPostRender`. The game's mesh and materials are never mutated.
- **Why:** the private RT is the decisive fix of a *seven-attempt* hunt. A magenta-clear test proved the forward render never survived on the shared base RT: the game's **deferred** `MapCamera` is force-pinned onto `_leftRt` by `CaptureStack` and out-competes the forward camera there (deferred resolve timing beats the depth nudge). The brown the user saw for weeks was deferred murk, invariant to every material/UV/texture change — it was **never our render**.
  Prior disproven paths, all removed: redirect the deferred camera (unlit murk — deferred lighting is never resolved into a redirected off-screen target), force Forward on the game camera (`Amp_Basic_N_MRAO` has no forward pass), sRGB/colorspace fixes (rig renders in Gamma — no-op), backbuffer `CameraTarget` grab (pure black under MULTIPASS XR), D24S8 stencil on the base RT, 2×2 texture blit (cannot follow live pan/zoom).
- **Established by:** `0094147` fix(map): render the map into a PRIVATE RT, not the shared base RT; `b48a41a` feat(map): render the REAL campaign-map mesh, textured, forward; `fb3ef2c` fix(worldui): render campaign map parchment unlit via mod forward camera
- **Breaks if:** the map is pointed back at the shared base RT "to save an RT", or the material override is made permanent instead of `onPreRender`/`onPostRender` scoped (multiplayer-safety + reversibility).
- **Confidence:** high

### `MapUnlit` samples the mesh's own `TexCoord0` on the GPU
- **Where:** `FlatScreenStereo.MapUnlitUvChannel` (0), `_UvScale=1 / _UvOffset=0`
- **Rule:** no per-quadrant UV remap; the shader reads `v.texcoord0`.
- **Why:** the long-standing "the mesh has no UVs, derive from vertex position" assumption was **wrong**. `isReadable=false` blocks CPU *data* access, not the channel's existence (`HasVertexAttribute` reads the layout descriptor and reported TexCoord0/1/2 = dim2). The object-space-position UV fought an oversized `mesh.bounds` and collapsed to a near-constant window — the flat brown. Each quadrant submesh's UV already runs 0..1 over its own 4096² texture.
- **Established by:** `53af144` fix(map): sample the mesh's REAL UV (TexCoord0)
- **Breaks if:** the `_UvChannel` knob is deleted (it is the no-rebuild escape hatch) or the positional-projection path is "restored as a fallback".
- **Confidence:** high

### Map matrices must be applied in `OnPreCull`, not `OnPreRender`
- **Where:** map matrix application hook in `FlatScreenStereo`
- **Rule:** captured game-map view+projection are applied in the mirror camera's `OnPreCull`.
- **Why:** applied only in `OnPreRender` the quads were **culled out** against the stale grazing matrices before the render ever ran → black RT. This is exactly the kind of ordering that reads as swappable and is not.
- **Established by:** `89fed52` fix(map): apply the captured map matrices in OnPreCull (before the mirror camera's culling)
- **Confidence:** high

### Map icon decals are drawn by us, on a cleared depth buffer, after the wind
- **Where:** map icon draw path (`DrawMapIcons`) + `CameraEvent.AfterForwardAlpha` command buffer
- **Rule:** location icons are drawn as forward quads through a command buffer at `AfterForwardAlpha` that **first clears the depth buffer**; the party token's renderers are re-drawn into the *same* buffer **after** the icons.
- **Why:** the icons are deferred *Decalicious* decals (`DecalCube`, 0 renderer materials) that project via the G-buffer — a forward camera cannot show them and there is no material to override. Render-queue 4000 alone was **not enough**: the cloud particles have `ZWrite ON` and, drifting above the icon plane, depth-rejected the icons wherever a cloud passed. And the party marker (`MapChoreographer.m_PartyToken`) is a world-space 3D mesh in the forward pass, so it must be re-drawn *after* our icons or the icons cover it.
- **Established by:** `287cfdb`, `e02be4f` fix(worldui): draw map icons on cleared depth so wind never occludes them; `4aebef0` (party marker on top)
- **Breaks if:** the depth clear is dropped, or the draw order icons→token is reversed.
- **Confidence:** high

### Wind/cloud particles are DIMMED, never disabled, and the scan is scene-wide
- **Where:** map particle handling (`MapWindOpacity`, default 0.3)
- **Rule:** scene-wide `FindObjectsOfType` scan matching `ParticleSystemRenderer` by **material OR GameObject name** (`Wind*`/`Cloud*`); scale each system's `startColor` alpha; restore originals on disengage; retry for a few seconds for late spawns.
- **Why:** two separate discoveries. (a) A **root-scoped** scan found *zero* systems — the Wind/Clouds GameObjects are **not children of the map root** (log: "suppressed 0"). (b) The user explicitly does **not** want the wind gone, only thinner; it is over-prominent because our forward capture lacks the flat map camera's fog-of-war `_WorldMask` that thins it, and campaign fog-of-war is disabled so there is no mask to borrow.
- **Established by:** `7e3389a` → `6e0824a` fix(worldui): dim map wind/cloud particles instead of disabling
- **Breaks if:** re-scoped to the map root, or "simplified" back to disabling the renderers.
- **Confidence:** high

### Map zoom is FOV-based and per-map, matching flat 1:1
- **Where:** map FOV state (`_mapFov`, `_activeMapIsCity`), `ZoomOutExtraHeight`
- **Rule:** default FOV 60 (world) / 80 (city) — the game's own `WorldMapConfig.DefaultFOV` / `CityMapConfig.DefaultFOV`; `_mapFov` resets to the new map's default on a world↔city switch; past the default FOV the camera also gains height.
- **Why:** flat's map zoom **is** camera FOV (`MapChoreographer.SetMapConfig` → `ResetZoomTo` → `m_Camera.fieldOfView`). A single distance-dolly at a fixed 55° FOV framed the **city** far too close, because flat frames the city at 80°.
- **Established by:** `b9d170c` fix(worldui): match flat's default map zoom (world FOV 60 / city FOV 80), FOV-based
- **Breaks if:** unified into one zoom model for both maps.
- **Confidence:** high

### Override materials are rebuilt on a world↔city switch
- **Where:** `BuildOverrideMaterials` + the renderer-identity guard
- **Rule:** the cache tracks **which renderer** the override materials were built for, not just the submesh count.
- **Why:** both maps have 4 submeshes, so a count-keyed cache kept the **world** textures/UVs on the **city** mesh.
- **Established by:** `82248f8` fix(map): rebuild override materials on world<->city switch
- **Breaks if:** the cache key is reduced to submesh count again.
- **Confidence:** high

### The game's real map camera is driven to our pose
- **Where:** map pose drive in the game camera's `OnPreCull`
- **Rule:** the **real** `CameraController.m_Camera` is moved to our computed map pose every frame in its `OnPreCull`.
- **Why:** on-map markers are UI elements projected through `m_Camera.WorldToScreenPoint`, and clicks are physics raycasts against `MapLocation` BoxColliders **through that same camera**. The mod prefix-skips `CameraController.LateUpdate` in VR, so `m_Camera` sat at a stale pose → markers projected off-screen and clicks missed. Driving the camera makes the game's own projection and raycasts line up **for free**, with no per-call patching. `VRRigDriver` uses `m_Camera` only as a build-time anchor and never follows its transform, so moving it is safe.
- **Established by:** `199df5a` feat(map): drive the game map camera to our pose → markers + clicks align
- **Breaks if:** replaced by patching the projection/raycast call sites individually.
- **Confidence:** high

### The map is MONO while the split keeps ROUTING
- **Where:** map engage path in `FlatScreenStereo` / `FlatScreen` split gate
- **Rule:** the campaign map does **not** suspend the stereo screen. Both eyes show the base RT (mono) while the split stays engaged; mirrors are held off.
- **Why:** suspension collapsed the SCREEN LAYER SPLIT and therefore dropped the game's **UI glass RT** — the markers, quest list and shields vanished with it.
- **Established by:** `76d678b` fix(worldui): keep UI glass over mono campaign map
- **Breaks if:** the map path reuses the generic "suspend stereo" helper.
- **Confidence:** high

### Map capture stays engaged behind overlays; only the *display* is gated
- **Where:** overlay gate (`UIEventPanel.IsOpen` + `StoryController.IsVisible`), `MapActive`
- **Rule:** while a scenario intro / story dialogue / encounter overlay is up, paint the map background black and gate `MapActive` — but **do not** disengage the capture.
- **Why:** disengaging causes re-detect thrash on every overlay open/close.
- **Established by:** `4aebef0`
- **Confidence:** medium

### Physical desktop mice are disabled while VR runs, and re-asserted per frame
- **Where:** `VirtualMouse.TickSuppressPhysicalMice` (`[WorldUI] SuppressPhysicalMouse`) — see the detailed entry in §9
- **Rule:** disable the physical mouse device(s), track exactly what was disabled, re-enable on VR exit, **re-assert every frame** against re-enumeration, and never touch our own virtual mouse.
- **Why:** their stale desktop position hovered/selected map and menu elements behind the player's back.
- **Established by:** `88b35b3` feat(worldui): laser-trigger map pan + suppress physical desktop mouse in VR
- **Breaks if:** the per-frame re-assert is turned into a one-shot.
- **Confidence:** high

### Map pan freezes the virtual mouse and consumes the click latch
- **Where:** `FlatScreen` map-pan gesture (`_mapPanGesture`), `FlatScreen.MapPanStartPixels` (22), `FlatScreenStereo.TryMapPixelToPlane`, `FlatScreenStereo.UpdateMapPan`
- **Rule:** a held trigger that travels past 22 RT pixels over the map converts into a pan. Entering the pan **consumes the click latch** (`_latched = false`), releases any VM button and ends any uGUI drag, so the release does **not** DirectClick. While panning the virtual mouse is **frozen** at the press pixel. `UpdateMapPan` writes **both** `m_TargetFocalPoint` and `m_FocalPoint`.
- **Why:** 22 px is "small so the map feels grabbed immediately, large enough to protect taps". Freezing the VM is what stops the pan from dragging the cursor across (and hovering) location markers. Both focal fields must be written because the game's `LateUpdate` lerp is prefix-skipped in VR, so nothing else syncs current to target. `TryMapPixelToPlane` reconstructs the ray manually from the **cached driven pose** rather than calling `ScreenPointToRay`, so it is immune to *when* the real camera transform is actually driven during the render loop.
- **Established by:** `88b35b3`
- **Confidence:** high

### Black-base detection is asynchronous, downsampled and consecutive-gated
- **Where:** `FlatScreenStereo.BlackProbeSize` (8), `BlackProbeIntervalFrames` (30), `BlackConsecutiveToEngage` (3), `BlackChannelThreshold` (6), `FastProbeFrames` (150), `AlbedoProbeRegion` (0.5)
- **Rule:** 8×8 async `AsyncGPUReadback` probe, several **consecutive** black reads required before engaging, and the region probe samples the **central 50 %** of the RT.
- **Why:** a synchronous probe stalls the GPU. A single black read is a frame blip, not evidence. And the *dark-but-not-black* map never trips a black watchdog at all — the centre-region probe (away from UI and corners) is the only one that measures the map area, which is why it exists in addition.
- **Established by:** `593d939`, `325c23b`, `85c2039` (mean + lit-fraction: "a lone bright texel can't mask a black map area")
- **Breaks if:** the consecutive gate is dropped or the probe is made synchronous / full-res / whole-frame.
- **Confidence:** high

---

## 2. Canvas conversion

### Nested game Canvases must stay ENABLED
- **Where:** `CanvasConversion.AdoptNestedCanvases`, `CanvasConversion.AdoptCanvas`, `NestedCanvasRecord`
- **Rule:** never disable a nested `Canvas` component. Instead: clear `overrideSorting`, ensure a `GraphicRaycaster` (add one if missing, destroy only ones we added), align `worldCamera` with the host, and register via `UguiPokeSurfaces.RegisterNested`.
- **Why:** a **disabled** `Canvas` component does not merge its children into the parent canvas — it stops rendering the **entire subtree**. Children only merge up when the component is **DESTROYED**. Round 19's neutralization therefore produced colliders with no pixels: the tray-docked initiative track was *invisible* while the laser still clamped on its host rect (test #20).
  The `overrideSorting` clear is the other half: the initiative track rides its own nested canvas at `sortingOrder 40`, which both drew it over everything *and* made the host `GraphicRaycaster` hollow (uGUI `Graphic`s register with their **nearest enabled parent canvas**, so every portrait belonged to the nested canvas and the host raycast hit an empty set — zero uGUI-click lines all session).
- **Established by:** `326d26e` fix(worldui): adopt nested canvases instead of disabling them (test #20); root cause in `6ccdf3e`
- **Breaks if:** "just disable the nested canvases, children merge up anyway". They do not.
- **Confidence:** high

### `overrideSorting` must be re-asserted, on a sweep AND per-frame for modals
- **Where:** `CanvasConversion.CanvasSweepIntervalFrames` (30), `ReassertAdoptedSorting` (modal hosts, every frame)
- **Rule:** the 30-frame sweep re-clears `overrideSorting` and catches pooled late-arriving canvases; modal hosts additionally re-clear **every frame**.
- **Why:** `AbilityCardUI` / `CardHighlight` flip `overrideSorting` back on live. A game writer flipping it even briefly pulls that subtree out of the host's dominant order between sweeps — a candidate flicker source that the up-to-30-frame gap could not cover.
- **Established by:** `326d26e`, `58de846`
- **Breaks if:** the per-frame modal re-assert is folded into the generic sweep.
- **Confidence:** high

### Dropdown overlays adopt KEEPING `overrideSorting`
- **Where:** `CanvasConversion.IsDropdownOverlay`, `DropdownListName` / `DropdownBlockerName`, `DropdownListSortingOrder` (4000), `DropdownBlockerSortingOrder` (3999), `NestedCanvasRecord.KeepOverrideSorting`
- **Rule:** the transient "Dropdown List" and its fullscreen "Blocker" are the **exception** to the clear-`overrideSorting` rule: they keep it, re-based to 4000/3999 — above host 1000 and the close-X at 1100, below the ray visuals at 5000. Modal hosts sweep adoption **every frame** so a fresh list is clickable immediately; dead records are pruned.
- **Why:** clearing `overrideSorting` on a list spawned at order 30000 dropped it to host order **at its hierarchy position** — behind later siblings — and `TMP_Dropdown.Show()` is a **no-op while the hidden list lives**, so a second click could never reopen it. Dropdowns were completely unusable.
- **Established by:** `33177db` fix(worldui): content-union depth mask, working dropdowns, thumbstick scroll
- **Breaks if:** the exception is removed for uniformity, or the sorting numbers are renumbered without preserving the 1000 < 1100 < 3999 < 4000 < 5000 band ordering.
- **Confidence:** high

### `Release` ALWAYS detaches the target before destroying the host
- **Where:** `CanvasConversion.Release`
- **Rule:** the converted target is detached from the host **unconditionally** (to the scene root if its original parent is gone) *before* `Object.Destroy(host)`.
- **Why:** the old code reparented only when the original parent was still alive; otherwise the window stayed a child of the host and the destroy **cascaded into it** — destroying the scenario `ESCMenu`, a Singleton the game never re-creates mid-scenario. No X could reopen the pause menu until a scenario reload. This was hunted across three commits including a Harmony `OnDestroy` stack probe.
- **Established by:** `ae8d3ae` fix(worldui): stop CanvasConversion.Release destroying the reparented modal
- **Breaks if:** the "if parent alive" guard is restored as an optimisation.
- **Confidence:** high

### `Release` forces a game-closed window back into the game's hidden state — but only `_disableCanvas` windows
- **Where:** `CanvasConversion.Release` forced-hidden branch
- **Rule:** if the game reports the window CLOSED at release time, force `CanvasGroup alpha = 0` + `blocksRaycasts = false`, and disable the `Canvas` **only for `_disableCanvas` windows**. Windows released while genuinely open stay untouched.
- **Why:** sticky floats re-enable the window's `Canvas` (`ReassertStickyVisible`); releasing in that forced-visible state restored the ESC menu's width-hugged content column as a grey left-edge overlay strip. The `_disableCanvas` scoping is essential: the game's own `Show` re-enables exactly those canvases via `OnTransitionStarted`, so disabling **any other** window's canvas would be permanent.
- **Established by:** `27d8323` fix(worldui): controller-X close releases sticky ESC floats; released closed windows forced hidden
- **Breaks if:** the `_disableCanvas` condition is dropped. Some windows never come back.
- **Confidence:** high

### `KeepBackgroundHidden` survives Release for the full-screen-menu family
- **Where:** `ConvertedPanel.KeepBackgroundHidden`, `CanvasConversion.HideFullScreenBackground`
- **Rule:** for the ESC/Options family the full-window blur/backing stays **disabled** on Release.
- **Why:** re-enabling the blur on Release left a translucent veil after an X-close, because the game could momentarily re-show the ESC menu flat.
- **Established by:** `92fe985` fix(worldui): four coupled modal-pipeline bugs
- **Confidence:** high

### Re-fit needs hysteresis: growth fast-paths, shrink is damped
- **Where:** `CanvasConversion.FitHostToContent`, `FitChangeFraction` (0.02), `FitRefitMinIntervalSeconds` (1.5), `FitStableSeconds` (0.5), `FitCheckIntervalFrames` (30), `ConvertedPanel.FitPendingSize` / `FitPendingSince` / `FitLastApplied`
- **Rule:** **growth** beyond the current host bounds applies immediately (only the 30-frame cadence throttles it). A pure **shrink/re-centre** applies only when the candidate has held steady for 0.5 s **and** the last applied fit is ≥ 1.5 s old. The first fit after `Convert` is never damped.
- **Why:** `Panel_CombatLog` re-fit **hundreds of times** — 569×138 ↔ 569×291 twice a second for minutes — because its content grows and shrinks continuously as log entries fade in and out. Every applied re-fit reads as the content jumping. The asymmetry is deliberate: content must **never** sit clipped behind the damping, so growth cannot be delayed.
- **Established by:** `d1d6d55` fix(worldui): damp host re-fit churn — stability + rate limit on shrink
- **Breaks if:** the growth/shrink asymmetry is "simplified" into one symmetric threshold.
- **Confidence:** high

### Degenerate authored rects bypass the 100 px placeholder clamp
- **Where:** `CanvasConversion.Convert` / `FitHostToContent`, `ConvertedPanel.FitFrameDegenerate`
- **Rule:** a target whose own rect is degenerate (< 1 px) does **not** clamp the measured content into the artificial 100 px placeholder frame; zero-draw-size graphics are skipped from the union.
- **Why:** the objectives container is authored at size **(0,0)**. The `max(size, 100)` fallback pinned it at 100 px, so the measured content was clamped into a 100 px frame and the whole panel rendered tiny.
- **Established by:** `679875c` fix(worldui): uniform tray panel density + tight initiative/objectives content fit
- **Breaks if:** the `FitFrameDegenerate` branch is removed as dead — it is the *reason* objectives render at all.
- **Confidence:** high

### `Flatten2D` is a per-frame LateUpdate sweep, and it is opt-in
- **Where:** `WorldSurface.Flatten2D`, `ConvertedPanel.FlattenEnabled`, `CanvasConversion.FlattenSubtree`, `CanvasConversion.LateTick`, `FlattenAngleEpsilon` (0.05), `FlattenZEpsilon` (0.01), `FlattenRecord`
- **Rule:** every frame while shown, zero every descendant's **local z** and **local rotation**. `x/y` are **never** touched. Originals are recorded (idempotently) and restored on Release.
- **Why:** the 3D tilt is **baked into the game prefab's serialized RectTransforms** (styled for the perspective `UICamera`) — one-shot flattening is not enough because it is *also* re-written live from two directions: `ObjectPool.Spawn` writes **world-identity** rotations (a tilted *local* rotation under the rotated host, on every new log line), and the `GUIAnimator` `MOVE_LOCAL` channel tweens `localPosition.z` per frame via LeanTween. `x/y` must survive so positional entry/banner animations keep playing flat. `LateTick` (LateUpdate) is required so the sweep runs **after** the game's Update-time tween writers.
- **Established by:** `6480af8` fix(worldui): flatten baked 3D tilt inside the converted combat log; extended in `0273d99`
- **Breaks if:** made one-shot, moved out of LateUpdate, or made to zero `x/y` too.
- **Confidence:** high

### Content fit and depth-mask emission use DIFFERENT alpha floors
- **Where:** `CanvasConversion.FitMinAlpha` (0.05), `CanvasConversion.MaskMinAlpha` (0.15)
- **Rule:** the fit keeps the historic 0.05 floor; **mask emission** uses 0.15.
- **Why:** a barely-visible wide graphic (faint container / gradient title banner, effective alpha just over 0.05) passed the shared floor and stamped a **wide** depth quad at the options plane. Every later-drawn transparent behind that plane — including the pause window below the plane-intersection line — failed ZTest across a visually empty region: a clean straight-edge hard cut with sky showing in the gaps.
- **Established by:** `f903bbe` fix(worldui): raise depth-mask alpha floor to 0.15
- **Breaks if:** the two floors are merged back into one constant. Either the cut returns (0.05) or real content stops masking (0.15 everywhere).
- **Confidence:** high

### Invisible clippers and raycast catchers must NOT stamp depth
- **Where:** `CanvasConversion.CollectVisibleMaskRects` exclusion rules
- **Rule:** exclude (a) `Mask` images with `showMaskGraphic = false`, (b) any `ScrollRect.viewport` image, (c) sprite-null near-white raycast catchers. `Mask` with `showMaskGraphic = true` is kept.
- **Why:** a stencil `Mask` with `showMaskGraphic=false` draws **stencil-only** (`ColorMask 0`) — it renders no pixels but stamped an invisible full-pane depth quad that hard-cut the pause menu behind the options window.
- **Established by:** `951691c` fix(worldui): invisible clippers no longer stamp depth
- **Confidence:** high

### The depth mask is PER-GRAPHIC, not one union quad
- **Where:** `CanvasConversion.CollectVisibleMaskRects` (cap 256, overflow merged into the last quad), `GrabbableModal` dynamic mesh + rect-set hash gate
- **Rule:** one host-local quad per visible graphic, viewport-clamped, rebuilt **only when the quantized rect set changes**; per tick only the transform syncs.
- **Why:** a single union quad stamped menu-plane depth across the gaps **between** settings rows, so the world showed through (drawn earlier) but other transparent menus did not (drawn later, depth-tested). Known accepted approximation: a graphic's transparent padding inside its own rect still masks.
- **Established by:** `20a1860` fix(worldui): clip scrolling menus at their viewport, per-graphic depth mask (evolving from the union in `33177db`)
- **Breaks if:** reverted to a union rect "for performance". The hash gate already makes rebuilds rare.
- **Confidence:** high

### World-space scroll views need an explicit clipper
- **Where:** `CanvasConversion.EnsureScrollClipping`, `ConvertedPanel.AddedScrollMasks` / `EnabledScrollMasks`
- **Rule:** guarantee a working clipper on **every** `ScrollRect` viewport in a converted subtree — re-enable a disabled `RectMask2D` or add one when neither an enabled `RectMask2D` nor a functioning stencil `Mask` exists. Fully reversible: added masks destroyed, re-enabled ones re-disabled.
- **Why:** in 2D a full-screen submenu's scroll list needs no viewport mask — the **screen edge** clips it. On a world-space host there is no screen edge, so rows scrolled past the viewport rendered above the window and read as "the menu grew taller".
- **Established by:** `20a1860`
- **Breaks if:** the two restore lists are merged (added vs. pre-existing-but-disabled need *different* undo).
- **Confidence:** high

### Every measured graphic is clamped to its enclosing clipper
- **Where:** `CanvasConversion.TryGetVisibleHostRect`, `CanvasConversion.ClipperMemo`
- **Rule:** the shared measure helper clamps each graphic to its enclosing clipper's rect — so scrolled-out content can grow neither the fit union nor the mask coverage.
- **Why:** makes a scroll-position change **size-neutral by construction**, rather than by a threshold that eventually loses.
- **Established by:** `20a1860`
- **Confidence:** high

### `useModLayer` exists because of a cross-CAMERA double-draw
- **Where:** `ConvertedPanel.ModLayerEnabled`, `CanvasConversion.ApplyModLayer`, `LayerRecord`, `UiLayer` (5)
- **Rule:** floated modals move the host **and its entire converted subtree** (nested canvases cull by their own GameObject layer) onto the dedicated mod layer 27; a periodic sweep re-asserts for pooled/late children; every touched transform's original layer is recorded and restored.
- **Why:** the mod's world-space modal hosts lived on layer 5 (UI), and the game's **mono `UI Camera`** (cullingMask `0x20`, stereo=None → backbuffer) renders layer 5 too. Every floated modal was drawn **twice** — once stereo by the HMD head camera, once mono by the UI Camera — which fights the stereo render each frame. `sortingOrder` cannot fix a cross-camera double-draw; the earlier `sortingOrder=1000` fix did not stop it.
- **Established by:** `2125e5d` fix(worldui): stop UI-Camera double-draw of floated modals
- **Breaks if:** the subtree sweep is reduced to "just the host GameObject". Nested canvases cull independently.
- **Confidence:** high

### Modal hosts convert at `sortingOrder` 1000 to break an order-0 depth tie
- **Where:** `CanvasConversion.Convert` sorting-order parameter; `ModalFallback` passes 1000
- **Rule:** floated modal hosts get a dominant `sortingOrder` (1000, well above the game's -1/0/1/40). Adopted nested canvases keep `overrideSorting` cleared so they inherit it.
- **Why:** every world-space host was created at Unity's default order 0. Unity depth-sorts equal-order world-space canvases by **camera distance**, which jitters as the head micro-moves; a full-screen modal is a ~32×17 m plane spanning ~13 m of depth that overlaps the other panels, so the tie resolved differently frame-to-frame → the modal alternately drew in front of / behind them → flicker. Small content-fit panels are spatially separated and never hit it.
- **Established by:** `e828d98` fix(worldui): stop floated-modal flicker via dominant host sortingOrder
- **Breaks if:** the number is treated as arbitrary and renumbered without also moving the X (1100) and dropdown (3999/4000) tiers.
- **Confidence:** high

### `StatPanelSurface` converts at `sortingOrder` 10 — a deliberate middle tier
- **Where:** `StatPanelSurface.StatPanelSortingOrder` (10)
- **Rule:** above every board-widget label (≤ 3), far below the floated-modal tier (1000) and the ray visuals (5000).
- **Why:** mod keycap labels keep tiny sortingOrders (face 1 / label 3, needed intra-button), but the stat panels' hosts sat at default order 0 — and Unity sorts transparents by `sortingOrder` **before** queue/distance, so button text always drew *last*, i.e. through the held-figure info panel.
- **Established by:** `4ad7965` fix(worldui): button text no longer draws through the held-figure info panel
- **Confidence:** high

### TMP label materials are per-label INSTANCES forced depth-honest
- **Where:** `NativeButtonSkin.ApplyFont`
- **Rule:** each label gets its own `fontMaterial` instance with queue `Transparent (3000)` and `ZTest LEqual`, set via **both** spellings — `_ZTestMode` (distance-field shader) and `unity_GUIZTestMode` (UI variants).
- **Why:** every mod world TMP shared the sampled **game HUD font material**, authored for the 2D screen HUD: `renderQueue 4003` + `ZTest Always` — it drew on top of everything. Instancing is mandatory so the shared game asset is never mutated. The dual spelling is the ActorBars-verified "per-material beats global" trick.
- **Established by:** `4ad7965`
- **Breaks if:** only one property name is set, or the shared material is edited in place.
- **Confidence:** high

### `EarlySettleUntil` re-runs layer/background/adoption every frame for 1.5 s
- **Where:** `CanvasConversion.EarlySettleSeconds` (1.5), `ConvertedPanel.EarlySettleUntil`, re-run from both `Tick` **and** `LateTick`
- **Rule:** during the settle window, the mod-layer move + nested-canvas adoption + background hide run every frame, in `LateUpdate` as well as `Update`.
- **Why:** the full-window backing/blur is instantiated/faded a few frames **after** `Convert`, and the game enables/instantiates it in its **own Update — after our Tick** — so it rendered untreated for 1-2 frames on the game's UI layer (mono UI Camera double-draw = flicker). `LateUpdate` runs after all `Update`s and immediately before render, closing the gap.
- **Established by:** `dd20a8e`, `83af0d1` (item 1)
- **Breaks if:** the LateUpdate half is dropped as redundant.
- **Confidence:** high

### Background-opaque detection reads the graphic's INTRINSIC alpha
- **Where:** `CanvasConversion.HideFullScreenBackground`, `BackgroundCoverFraction` (0.85), `BackgroundOpaqueAlpha` (0.3)
- **Rule:** test `color.a` of the graphic itself, **not** the inherited `CanvasGroup` alpha.
- **Why:** a backing fading in from inherited alpha 0 would pass an inherited-alpha test on its first frame and render untreated. Testing intrinsic alpha hides it on frame one.
- **Established by:** `dd20a8e`
- **Confidence:** high

### `HideFullScreenBackground` must never disable a stencil-Mask driver
- **Where:** `CanvasConversion.HideFullScreenBackground` guard
- **Rule:** never disable an `Image` that drives a stencil `Mask` — it is a clipper, not a backing.
- **Why:** disabling it removes the clipping for the whole subtree.
- **Established by:** `20a1860`
- **Confidence:** high

### Modal hosts are created RENDER-HIDDEN and revealed after a settle
- **Where:** `CanvasConversion.RevealDelaySeconds` (0.15), `ConvertedPanel.RevealPending` / `RevealNotBefore`
- **Rule:** the host canvas starts disabled and is revealed only after a treated+stable settle.
- **Why:** so no untreated backing frame ever draws — the decisive zero-flicker pop-in fix after several partial ones.
- **Established by:** `1cc7f81` (item 3a)
- **Confidence:** high

### `capHeightToCanvas` reads `CanvasScaler.referenceResolution.y`, not the live rect
- **Where:** `CanvasConversion.ResolveStableHeightCap`, `CanvasConversion.FirstCapHeights`
- **Rule:** clamp the captured height to the **CanvasScaler reference** height (≈1080), falling back to the first (cold) captured height per window. Only the full-screen-menu family sets the flag.
- **Why:** `Convert` captures `target.rect.size.y`, which reads ~1080 on a **cold** first open (layout not yet expanded) but the settled ~2040 on every **warm** reopen — the pause menu opened ~1.9× taller with an empty bottom on every subsequent open. Reading the *live* post-layout root-canvas rect does not work: it grows 1080 → 2040 too, so the warm size never exceeded its own cap.
- **Established by:** `9f4fdd4` (bug #7), corrected in `92fe985` (issue 2)
- **Breaks if:** the cap source is switched back to a live rect.
- **Confidence:** high

### The one-shot fit must wait for N consecutive stable measurements
- **Where:** `CanvasConversion.OneShotSettleChecks` (6), `ConvertedPanel.FitOneShotStableSize` / `FitOneShotStableCount`, `SettleOneShotFit`
- **Rule:** force a deterministic layout rebuild and require the measured visible-content size to hold steady for N consecutive checks before the single fit+lock; a deadline is the safety floor.
- **Why:** the one-shot fit committed on the **first measurable frame**, so a warm re-open measured more laid-out content than a cold first open and locked a taller rect — the pause menu was a different size every time.
- **Established by:** `8ad0250` (item 5)
- **Confidence:** high

### Width-hug one-shot fit is for the ESC menu ONLY; the Options family keeps full width
- **Where:** `ModalFallback` full-screen-menu classification → `CanvasConversion.Convert(fitContent:…)`
- **Rule:** the ESC menu gets the width-hugging one-shot fit; the Options family converts `fitContent:false`, height-capped only.
- **Why:** the width-hug collapsed the wide Options window 1552 → 416 px (content shifted −580), so the **right panel fell outside the host** and its sliders were unclickable.
- **Established by:** `92fe985` (issue 3)
- **Breaks if:** the two are unified under "full-screen menu".
- **Confidence:** high

### The fit clamp normalizes the target frame corners
- **Where:** `CanvasConversion.FitHostToContent` (`Vector2.Min/Max` on the frame)
- **Rule:** normalize the target's world-corner frame with `Min`/`Max` before clamping.
- **Why:** a mid-show-animation rotation or negative scale can **invert** the frame corners and corrupt the clamp.
- **Established by:** `62265d2`
- **Confidence:** high

### The fit union clamps against the TARGET's own frame, not the shrunk host
- **Where:** `CanvasConversion.FitHostToContent`
- **Rule:** the union is clamped to the target's own world-corner frame.
- **Why:** clamping against the already-shrunk host makes growth impossible — a one-shot fit under-covered later story pages permanently.
- **Established by:** `bcbd564`
- **Confidence:** high

### EVERY pokeable host is content-fit, not just floated modals
- **Where:** `CanvasConversion.Tick` driving `ConvertedPanel.FitEnabled` centrally
- **Rule:** initiative track, combat log, objectives, dialogs, stat panels and modal windows all fit.
- **Why:** converted panels registered laser/poke planes at their **full window rect**: `Panel_InitiativeTrack` at ~45×25 m (sizeDelta 1920×1080), story window 32×18 m, combat log 14×7 m. These invisible planes sat in front of the scene, so the laser latched them first — the dot appeared off the visible dialog and clicks on real targets were eaten. Rated CRITICAL in test #14.
- **Established by:** `bcbd564` fix(worldui): content-fit ALL pokeable hosts, periodic growth re-fit
- **Confidence:** high

### Converted GAME-owned panels keep the UI layer
- **Where:** `CanvasConversion` layer policy (documented at the class)
- **Rule:** game-owned converted panels are **never** re-layered (except the explicit `useModLayer` modal opt-in, which records and restores). Only mod-owned trees go through `VRLayers.Apply`.
- **Why:** reversibility. `CanvasConversion` is the owner of the head camera's UI culling bit precisely so the game's own objects can stay where they are.
- **Established by:** `9c97b4e`, `1a00709`
- **Confidence:** high

---

## 3. Modal pipeline

### Passive hover panels must NEVER assert `ModalUI`
- **Where:** `ModalFallback.FallbackIds` (exclusions), `PropInfoSurface`, HelpBox handling
- **Rule:** `TextInfoPanel`, `PropInfoPanel`/`TrapInfoPanel` and `HelpBox` are **passive** — converted as non-pokeable world cards with the host raycaster kept dark, never pointer-over-UI, never modal.
- **Why:** twice a **self-sustaining hard lock**. `TextInfoPanel` is hover-driven: `WorldspaceStarHexDisplay`'s hover path is its *only* Show/Hide caller. Listed as a modal, `ModalUI` stopped our board pick/hover injection, so the hover-**leave** path that hides the panel never ran again — panel open forever, `ModalUI` forever, dead card fan and dead board clicks (tests #17 and #18). `HelpBox` did the same in test #16: the game showed its passive hint strip after a hero placement and `ModalUI` was held from that moment to shutdown, so the fan could never open (`CardSelection → ModalUI` at the placement, released only at quit).
- **Established by:** `66d4b7e` fix(worldui): TextInfoPanel passive; `1898aca` fix(worldui): passive HelpBox hint strip no longer asserts ModalUI
- **Breaks if:** any hover-driven or passive window is added back to `FallbackIds` "for completeness". The audit of all 26 remaining IDs is documented inline — `DurabilityPanel` stays modal **deliberately** (unmapped; escape-chord recoverable, unlike a dropped invisible blocking dialog).
- **Confidence:** high

### Floating a menu and asserting `ModalUI` are two separate wants
- **Where:** `ModalFallback` (`want` vs `wantLock`), `NonBlockingMenus`
- **Rule:** `want` floats **every** open menu; a separate `wantLock` (blocking prompts only) drives `ModalUI`. Reachable menus (ESC/Options/Multiplayer/Compendium) float **without** locking, so the board/cards/fan stay interactive.
- **Why:** coupling them made the pause/Options menu **invisible** (`want=false → convertWanted=false →` released to its 2D home) — a regression caught the same round it was introduced.
- **Established by:** `a753515` (4a regression), `646fc5f` (item 3b)
- **Breaks if:** re-merged into one flag.
- **Confidence:** high

### Reachable menus are STICKY floats
- **Where:** `ModalFallback` sticky handling, `ReassertStickyVisible`, `ConvertedPanel`/`WindowPanel.UserClosing`
- **Rule:** a sticky float survives the game hiding a *sibling*; `ReassertStickyVisible` re-asserts the window's `CanvasGroup` **and** re-enables its cached `Canvas`. Only `UserClosing` drops a sticky float, and `ReassertStickyVisible` is guarded by `!UserClosing`.
- **Why:** the ESC menu's Unity `ToggleGroup` is **single-toggle** — opening Multiplayer turns the Options toggle off, whose deselect calls `UIOptionsWindow.Hide()` (a CanvasGroup tween), and the mod released the observed-hidden float. Parallel windows were impossible. Separately, opening a 2nd non-blocking window ran the 1st window's hide-fade, which disables its **own Canvas** (content vanished, frame stayed) — hence the Canvas re-enable as well as the CanvasGroup.
- **Established by:** `8ad0250` (item 6), `c81b31c` (issue #5)
- **Breaks if:** the `!UserClosing` guard is dropped — nothing would ever close.
- **Confidence:** high

### `CloseFloatedWindow` is the ONLY close path, and it force-Hides after `Escape()`
- **Where:** `ModalFallback.CloseFloatedWindow`, `CloseTopModal`, `CloseStickyFloatsExceptEscMenu`, `OptionsToggle.CloseAll`
- **Rule:** every close — corner X, controller-X, escape chord — routes through `CloseFloatedWindow`, which sets `UserClosing`, calls `UIWindow.Escape()`, and **force-Hides** if the window is still open afterwards. `CloseAll` closes sub-windows first, then remaining sticky family floats, then the ESC menu **last**.
- **Why:** (a) calling the game's `Hide()` directly closes the game window but never sets `UserClosing`, so the float stayed alive and `ReassertStickyVisible` force-re-enabled its canvas forever — controller-X never visually closed the pause menu. (b) A submenu whose `escapeKeyAction` is `Skip` returns `true` from `Escape()` **without hiding**, leaving the X dead. (c) `UIWindow.Escape()` is Harmony-suppressed for `ESCMenu`, so the fallback `Hide()` is the path that actually lands.
- **Established by:** `27d8323`, `8794527` (item 7a), `92fe985`
- **Breaks if:** any caller "shortcuts" to `window.Hide()`.
- **Confidence:** high

### `CloseFloatedWindow` deterministically resets the ESC toggle group
- **Where:** `ModalFallback.CloseFloatedWindow` (toggle-group reset for Options/OptionsSubmenu/ViceOptionsSubmenu/Compendium)
- **Rule:** reset `Singleton<ESCMenu>.toggleGroup` on **both** close branches.
- **Why:** on the "game had already hidden it" branch the game's hide-callback (`toggleGroup.SetAllTogglesOff`) never ran, leaving the tab toggle ON — re-clicking it did nothing and the ESC menu read as unresponsive.
- **Established by:** `92fe985` (issue 4)
- **Confidence:** high

### `focusOnMouseHover` must be disabled while a full-screen menu floats
- **Where:** `ModalFallback` (save/restore of `InControlInputModuleExtended.Instance.focusOnMouseHover`)
- **Rule:** disable it while floating; restore on close and on Detach.
- **Why:** the scenario runs gamepad UI navigation (`ControllerInputArea.Focus` → `SetSelectedGameObject`). The mod's live `Mouse.current` drives the InControl module, whose `ProcessMove` **de-selects on hover change every frame** when `focusOnMouseHover` is set. The head-relative screen pointer sweeps the world-fixed floated menu as the head moves (plus Virtual Desktop host-mouse injection and the `Mouse.current` flip-war), so selection churned null↔button every frame → the highlight and its LeanTween fade flickered. Poke/laser clicks are unaffected — they use `ExecuteEvents`, not the hover-select path.
- **Established by:** `f20fb85` (bug 2)
- **Breaks if:** removed as a no-op. It *was* a genuine no-op in one earlier build (`e828d98` notes the flag was already false there), which is why it is easy to mistake for dead code — but it is the correct lever when the flag is set.
- **Confidence:** high

### Spawn placement is EVENT-gated, never per frame
- **Where:** `ModalFallback.ComputeHmdPose`, `ClampSpawnPose`, `PlaceAtHmd`, `PlaceFrameAt`
- **Rule:** `ComputeHmdPose` runs **only** at spawn placement, presence-regain refloat and lost-menu recall. Never per frame. This is called the *placement-healing regression rule* in the commits.
- **Why:** per-frame placement fights the user. `GrabbableModal.Build` seeds the grab frame from the just-placed host pose and `PanelGrabHandle` captures the frame rotation at grab-start, so the user's later grab-rotation stays authoritative and is never billboarded back. The settings panel demonstrated the failure mode directly (see `SettingsPanel` FOLLOW below).
- **Established by:** `be1e163`, `19e64e2` (request B), `9b84f64` (request A)
- **Breaks if:** placement is moved into `Tick` "so it stays correct".
- **Confidence:** high

### Spawn facing uses the panel→head vector, not the gaze forward
- **Where:** `ModalFallback.ComputeHmdPose` (facing), matching `PanelPlacement.Facing`
- **Rule:** yaw from the flattened `pos - head.position` vector, upright, +Z away from the head.
- **Why:** yawing to the raw **gaze forward** makes a *centred* window face the player but leaves an **offset** window's normal several degrees off — confirmation dialogs open as staggered secondaries, so they visibly did not face the player. For centred windows the two directions coincide, which is why the bug only showed on staggered dialogs.
- **Established by:** `be1e163` fix(worldui): one-shot face pause menus/confirmations at the head on spawn
- **Confidence:** high

### The board-plane spawn floor clears the window's BOTTOM edge
- **Where:** `ModalFallback.ClampSpawnPose` (half-height threaded in), the pitch limit and the eye-level cap
- **Rule:** `floorY = boardY + BoardTopClearanceMeters*scale + half.y`; steep-gaze pitch limit 15°; eye-level cap headroom 0.10 m.
- **Why:** clamping the **pivot** let tall windows (results / actor info) hang their bottom down into the control board. The pitch limit was tightened 25° → 15° and the headroom 0.05 → 0.10 m specifically because windows open while the player is looking **down at the board**, so the raw gaze-following spawn landed them exactly where the board is.
- **Established by:** `19e64e2` (request B), tightened in `3033da7`
- **Breaks if:** reverted to a pivot-based floor or the two numbers are rounded back.
- **Confidence:** high

### Spawn overlap avoidance: raise, then swing, then best-effort
- **Where:** `ModalFallback.ComputeHmdPose` overlap segment
- **Rule:** after the pitch/board clamps, box-test the panel's projected bounds against the play tray (found read-only by root name `GloomhavenVR.PlayTray`, renderer-bounds union) and the other converted modals' host-rect corners. Resolution order: raise until the bottom clears (capped at eye level) → swing laterally in **10° steps within ±30°** of the gaze → else take the least-overlap pose. **Staggered secondaries are skipped** — they overlap their parent by design.
- **Why:** narrator/story dialogs spawned inside the control board or on top of other modals.
- **Established by:** `9b84f64` feat(worldui): spawn-time overlap avoidance for floated modals
- **Breaks if:** the staggered-secondary exemption is removed — sub-menus would be pushed away from their parent.
- **Confidence:** high

### Off-view sticky menus are recalled after 6 s
- **Where:** `ModalFallback` recall timer (0.2 viewport margin, > 4 m real scale, 6 s)
- **Rule:** per floated **sticky full-screen menu whose game window `IsOpen`**, track continuous time outside the head frustum or too far away; after 6 s re-place it. Timer resets on visibility, while gripped, and on recall.
- **Why:** an incident: the floated ESC pause menu was laser-carried off-view and stayed **open**, so the by-design modal input block stayed engaged with no visible cause, and every trigger aimed at the cards hit the lost menu's wide grab zone first ("could not pick up cards"). The results window joins the recall too, because losing it blocks the scenario-end flow.
- **Established by:** `a312cb4` fix(worldui): lost-menu incident; extended in `19e64e2`
- **Breaks if:** the "while gripped" reset is dropped — the panel would be yanked out of the user's hand.
- **Confidence:** high

### The results window gets NO mod X button, deliberately
- **Where:** `ModalCloseButton` attach gate, `ModalFallback.IsResultsPanel`
- **Rule:** `ResultsPanel` / `AdventureCompletionPanel` are grabbable like every other modal but are **hard-excluded** from the X button.
- **Why:** the only way out must stay the native continue/retry/exit buttons — an X close would strand the scenario-end flow. Separately, the click-through story/dialog window also gets no X (gate on the *reachable-menu* set, not all grabbable modals).
- **Established by:** `19e64e2` (request A), `a753515` (1a)
- **Confidence:** high

### The X button sits on its OWN nested canvas at 1100
- **Where:** `ModalCloseButton` (nested canvas, `overrideSorting`, `sortingOrder` 1100, own `GraphicRaycaster`, `UguiPokeSurfaces.RegisterNested`)
- **Rule:** the X is not coplanar with the adopted game window.
- **Why:** at the same `sortingOrder` 1000 the coplanar raycast tie went to the **game graphic** and the X never fired.
- **Established by:** `ca6b22e` (#8)
- **Confidence:** high

### The X glyph's `localPosition.z` only is normalized
- **Where:** `ModalCloseButton` CrossBar setup
- **Rule:** set `anchoredPosition = zero`, then normalize **only z** — do not set `localPosition = Vector3.zero`.
- **Why:** the plate pivots at (1,1) (top-right corner), so snapping both bars to the pivot origin put the X up-and-right instead of centred.
- **Established by:** `8ad0250` (item 4)
- **Confidence:** high

### A grabbable modal's holder must stay at IDENTITY scale
- **Where:** `GrabbableModal.Build` (holder identity, frame carries the user factor, host SIZE tracks worldScale)
- **Rule:** the holder is identity-scaled so `frame.position` is a true fixed world point; bar/collider dimensions carry `worldScale` explicitly.
- **Why:** a `worldScale`-scaled holder tied the frame's WORLD position to `worldScale`: `host.position = holder.scale(worldScale) × frame.localPosition`. `worldScale` settles/animates at scenario start, so the start Story dialog's position **and** size collapsed toward the origin in lock-step (logs: pos 1285→608, scale 0.056→0.027, same ratio per frame) — the dialog flew away and vanished, **undismissable: a deadlock**.
- **Established by:** `cfe4018` fix(worldui): grabbable modal must hold a fixed world pose (deadlock)
- **Breaks if:** the holder is scaled again "so children inherit the diorama scale".
- **Confidence:** high

### Floated menus capture `worldScale` ONCE at spawn
- **Where:** `GrabbableModal` `_spawnWorldScale`
- **Rule:** host SIZE and bar are derived from the spawn-time diorama scale, not the live `PanelLayout.WorldScale`.
- **Why:** otherwise a menu tracks world zoom and appears to rescale whenever the player pinches the diorama.
- **Established by:** `1cc7f81` (item 2)
- **Confidence:** high

### `HostLateSync` exists because Update order between two writers is undefined
- **Where:** `GrabbableModal.Tick` + the `HostLateSync` component on the holder
- **Rule:** the host is re-synced from the frame in **LateUpdate**.
- **Why:** `GrabbableModal.Tick` copies frame→host from `ModalFallback.Tick` (module `Update`) while `PanelGrabHandle.Update` moves the frame from its own MonoBehaviour `Update` — **undefined relative order**. When the handle ran after the module, the depth mask (a rigid frame child) rendered at the NEW pose while the host lagged one frame; drags move 5-30 cm/frame vs. the 2 mm behind-plane offset, so dragging toward the viewer put the mask **in front of** the menu → the menu failed its own `ZTest LEqual` under every mask quad → content blinked *only during movement*.
- **Established by:** `951691c` (task 2)
- **Breaks if:** the LateUpdate sync is folded back into the module tick.
- **Confidence:** high

### `renderOnTop` (ZTest Always) was tried and DELIBERATELY REVERTED
- **Where:** absent from `CanvasConversion` — the `renderOnTop` / `ApplyRenderOnTop` / `ApplyZTestAlways` / `GraphicOverlayRecord` machinery was removed
- **Rule:** floated menus ZTest **normally** (LEqual). Hands and the board occlude them.
- **Why:** the feature was added (`b84817d`) to solve menu-vs-sky occlusion without disabling the sky, then removed one round later because the user rejected menus drawing over everything. The depth-mask approach replaced it.
- **Established by:** added `b84817d`, reverted `8ad0250` (item 1a)
- **Breaks if:** someone "restores" ZTest-Always as an easy fix for a menu-occlusion report. It was tried; it lost.
- **Confidence:** high

### The depth-mask quad's render state is exact
- **Where:** `GrabbableModal` depth-mask material
- **Rule:** Overlay shader forced to `ZWrite 1` / `ZTest LEqual` / `Cull Off` / `Blend Zero One` at **renderQueue 2999**, on mod layer 27, +2 mm **behind** the content.
- **Why:** 2999 draws after all opaque (nearer hands/board still win) and before the menu UI (~3000, which draws on top and passes LEqual at its own plane), so HUD *behind* fails ZTest and is occluded — while nothing is hidden and no colour changes. Every number in that sentence is load-bearing.
- **Established by:** `c02ad80` fix(worldui): depth-mask floated menus so HUD stops bleeding through
- **Breaks if:** the queue is moved to 3000, the blend is changed, or the 2 mm offset is dropped.
- **Confidence:** high

### The grab bar is opaque and sorts at 1100
- **Where:** `GrabbableModal` / `PanelGrabHandle` bar material
- **Rule:** opaque `GloomhavenVR/Overlay` with `_ZWrite` on, `MeshRenderer.sortingOrder` 1100, `ZTest` **LEqual**.
- **Why:** 1100 puts it above the order-1000 menu canvas and below the 5000 ray dot; LEqual is kept so a nearer hand still occludes the solid handle.
- **Established by:** `83af0d1` (item 3)
- **Confidence:** high

### The modal escape chord runs on raw XR state, by design
- **Where:** `ModalFallback.CloseTopModal` driven by `NonDominantHold`
- **Rule:** holding non-dominant A/X closes the **top** modal through `UIWindow.Escape()` (honouring `escapeKeyAction`) with `Hide()` as fallback. It runs on **XR controller state**, entirely independent of the pointer stack.
- **Why:** with pointer currency stolen (test #17), the floating `Text Info Panel` could never be dismissed — its close path runs on the game's own mouse reads. `ModalUI` never released; the session was unrecoverable until quit. The chord is the guarantee that *no future input failure* can hard-lock a session.
- **Established by:** `834159d` feat(worldui): modal escape chord — floating modals always closable
- **Breaks if:** re-implemented on top of the pointer/UI stack it exists to bypass.
- **Confidence:** high

### The story window's poll sources are decompile-verified, and level-triggered
- **Where:** `ModalFallback` polls: `StoryController.IsVisible`, `LevelMessageUILayoutGroup.IsShown`, `UIManager.dialogPopup.IsOpen`
- **Rule:** polls run **level-triggered every frame** and are **not** gated on `ScenarioBoardExists`; the Open set survives the menu→scenario transition (pruned by `IsOpen`/death, never blanket-cleared).
- **Why:** the story window's `UIWindowID` is **scene-serialized** (no `Story` member exists in `UIWindowID`), so the ID set never matched. And window events were edge-triggered *and* scenario-gated, so anything opening during the loading transition was lost forever. While shown, `StoryController` calls `AddUpdateBlocker` + `LockProcessingAction` — an invisible one is a **hard lock**. Test #10: MODAL FALLBACK fired **zero** times all session.
- **Established by:** `b5c87a7` fix(worldui): ModalFallback detects the scenario story box + covers the start transition
- **Breaks if:** the polls are re-gated on the scenario signal or made edge-triggered.
- **Confidence:** high

### The full-screen composite is suppressed while a burn is active
- **Where:** `FlatScreen.WantVisible` (short-circuit on `Cards.Patches.HandSuppression.BurnActive`)
- **Rule:** the `ModalUI` catch-all that raises the flat screen is short-circuited to false while a burn runs.
- **Why:** the burn UI-locks the game (→ `ModalUI`) but plays on the *world* card; the burn confirm is a `DialogPopup`, not a `UIConfirmationBox`, so `IsConfirmationBoxOpen()` is false and the catch-all raised the full desktop-mirror quad over an **empty** UI-lock composite — a ~1 s flat flash. A genuinely floated modal still shows.
- **Established by:** `9306411` (item 5b)
- **Confidence:** high

### The stick-scroll fallback engages only where the generic path no-ops
- **Where:** `ModalFallback` results-window scroll driver
- **Rule:** while a laser/poke hovers **anywhere** on the floated results host **and** the hover is outside any `ScrollRect` subtree (so the generic `RayUguiDriver.TickStickScroll` would no-op), synthesize the same wheel-notch stream — identical deadzone 0.3 / 40 notches/s. The two paths are mutually exclusive by construction; never both.
- **Why:** on the results window the laser mostly lands on the frame/backdrop (the achievement rows are display-only), so the generic path did nothing there.
- **Established by:** `3e7fd85` (user #12)
- **Breaks if:** the mutual-exclusion condition is relaxed — double scrolling.
- **Confidence:** high

---

## 4. Surfaces

### `WorldSurface.ReleaseCurrentPanel` exists because a surface's TARGET can change
- **Where:** `WorldSurface.ReleaseCurrentPanel`, `DecisionDockSurface` target-change handling
- **Rule:** a surface may drop its conversion when its target changes while `WantConverted` stays true.
- **Why:** `WorldSurface` only converts while `Panel == null`. The DecisionDock's target **changes across prompts** (`YesNoDialog` → `DialogPopup` → `TakeDamagePanel`) while `WantConverted` stays true, so the first conversion stuck forever and the new prompt neither docked nor floated (the claim keeps the generic path down). Concretely: after short-rest "Ja" the Yes/No box stayed on the board and the follow-up lose-card/redraw choice never appeared.
- **Established by:** `70296c1` (item 2)
- **Breaks if:** the `Panel == null` guard is treated as sufficient again.
- **Confidence:** high

### `ApplyContentWidth` must force `Panel.Target`, the container ROOT
- **Where:** `ObjectivesSurface` width application → `Panel.Target`
- **Rule:** the width is written to the conversion's `Target` (the container root), not to an inner row.
- **Why:** the objectives container's authored rect is **(0,0)**, so the conversion's `max(size, 100)` fallback pinned it at 100 px; writing width anywhere else was measured against that placeholder and did nothing visible. Two WIP commits were spent finding this.
- **Established by:** `64bb628` wip: objectives width forces the container root (after `52eaf00`)
- **Confidence:** high

### `FitWidthToMount` is `virtual` so objectives can opt out
- **Where:** `TrayMountedPanelSurface.FitWidthToMount` (default true), overridden false by `ObjectivesSurface`
- **Rule:** objectives do **not** fit their width to the mount.
- **Why:** the user's requirement is that "Breite" changes shape only and "Größe" changes glyph size. With width fitted to the mount, widening the budget also rescaled the glyphs — the two dials fought each other.
- **Established by:** `908f0c8` fix(worldui): objectives "Breite" changes shape only — the glyph size stays with "Größe"
- **Breaks if:** the virtual is inlined to a constant true "since only one subclass overrides it". That one override is the whole feature.
- **Confidence:** high

### Tray panels share ONE pixel density; only the multiplier varies
- **Where:** `TrayMountedPanelSurface.DensityScale` (virtual, 1), `PlayTray.TrayPixelsPerMeter` (2400), `MinDensityScale` (0.5) / `MaxDensityScale` (1)
- **Rule:** panel world size follows its **content pixel size** at the shared density, clamped **DOWN only** (never up) to the dock budget, with a 0.5× readability floor. `ObjectivesSurface` overrides `DensityScale` to 0.6, `EnemyRevealSurface` to 0.6, `DecisionDockSurface`/`DamageTooltipSurface` to 0.8.
- **Why:** docked panels used to be scaled by **host-rect fit into the dock area**, so effective pixel densities differed ~17× between panels (1714 px initiative host vs. 100 px objectives host over similar dock sizes) — objectives text was enormous and the initiative track was tiny.
- **Established by:** `679875c`, per-panel override in `46f71ab`
- **Breaks if:** the clamp is allowed to scale **up**, or the readability floor is removed.
- **Confidence:** high

### Initiative content fit is scoped to `initiativeTrackHolder`, by RULE not by name
- **Where:** `InitiativeTrackSurface` fit content root
- **Rule:** the fit measures only the game's own serialized `initiativeTrackHolder`.
- **Why:** the fullscreen **siblings** (`enemyCardsHolder`, `enemyCardsBlocker`, `helpBox`, gamepad tips) inflated the measured union to ~1714×959 px. Scoping to the serialized holder excludes them structurally; a name-based exclusion would rot.
- **Established by:** `679875c`; same idiom for `ElementBoardSurface` (`elementsHolder`, `544826d`)
- **Confidence:** high

### `NormalizeDepth` DFS-walks each portrait's subtree
- **Where:** `InitiativeTrackSurface.NormalizeDepth`, `DepthEpsilonPixels` (0.5), `InitiativeDepthMaxSpreadPx` (default 10, 0..40)
- **Rule:** DFS every **active portrait's subtree**, record every authored non-zero local z (idempotently), and remap by the single factor mapping the full protrusion spread onto the live cap. Cap 0 → factor 0 → truly flat. Raw z restored on release.
- **Why:** remapping only `initiativeTrackHolder`'s **direct children** did nothing: the row's authored recession (and the acting actor's raised selection frame) lives on transforms **nested inside** each portrait, whose direct-child z-spread is ~0 — so `rawSpread` fell below the epsilon and `NormalizeDepth` early-returned every tick. The slider was inert and 0 was never flat.
  Also: the pop is **authored geometry**, not an animation — `Select()` toggles `selectionObject.SetActive` and a material `_FXAnim`, never a transform z/scale — so a direct-child remap could never flatten it. Zeroing z here removes only the geometric protrusion (draw order is hierarchy-based), so the selection glow survives: flattening never hides whose turn it is.
- **Established by:** `991344c` fix(initiative): flatten portrait depth over the whole subtree so 0 is truly flat; capped in `c4097cd`
- **Breaks if:** reduced to direct children, or the cap becomes a per-|z| band instead of a **total spread** cap (that left ~40 px front-to-back and was still too strong).
- **Confidence:** high

### The initiative laser uses a per-portrait depth-aware picker
- **Where:** `InitiativeTrackSurface` implementing `IDepthPortraitPicker`; `UguiPointer.TryRaycast` far-ray branch
- **Rule:** for the FAR ray on a host with a registered picker, intersect the true world aim ray with each active portrait's **real world rect** (at its actual position and depth) and clamp the visible beam to the hit point. Falls back to the flat `GraphicRaycaster` pick on a miss. Scoped to the laser **only** — poke and every other panel keep the flat pick.
- **Why:** the far-ray uGUI pick derives its screen point from the **flat host plane**, so a depth-displaced portrait projected to a different screen position under perspective and resolved to a **neighbour**. Depth normalization only reduced the error; it never made the pick geometrically correct.
- **Established by:** `e4e4237` fix(worldui): per-portrait depth-aware laser pick for the initiative track
- **Confidence:** high

### The initiative reorder animation must not be stomped
- **Where:** `InitiativeTrackSurface` reorder detection (`InitiativeTrack.isAnimating` OR `animationDelayed`) → freeze `Panel.FitEnabled`, skip `NormalizeDepth`
- **Rule:** hold off **both** interfering passes for the reorder's duration; restore the captured fit state and resume depth normalization on the settled order.
- **Why:** `AnimateInitiativeReorder` disables the holder's `HorizontalLayoutGroup` + `ContentSizeFitter` and `LeanTween.moveX`-slides each entry, driving each portrait's **world** `transform.position.x`. Our per-tick re-fit shifts `Target.anchoredPosition` and resizes the host when the layout-disabled row's bounds change — moving the tween's world reference frame out from under its recorded targets. The slide only hinted, flickered, then the coroutine's end layout re-enable snapped to the final order.
- **Established by:** `fd5c2e4` fix(worldui): let initiative reorder animation play out un-stomped
- **Breaks if:** only one of the two passes is held off.
- **Confidence:** high

### The enemy reveal is anchored in the RIG-LOCAL frame with a HEAD-frame scale
- **Where:** `EnemyRevealSurface.PlantPose` / `EnemyRevealSurface.Place` (rig tracking space), `EnemyRevealSurface.HeadFrameScale`
- **Rule:** the follow target and stored pose are expressed in the **rig's tracking space** and re-projected through the live rig each frame; the size/offset scalar is `PanelLayout.WorldScale` — the **live rig lossy scale**, the player's own real→world conversion — and **deliberately NOT** `VRRigDriver.BaseWorldScale`.
- **Why:** this took **eleven** user reports across many rounds, and the two halves failed for opposite reasons.
  *Frame:* `HeadCamera` is a child of `RigRoot`, and `WorldGrab` writes `rig.rotation/position/localScale` directly, so any pose derived from the head's **world** pose bakes the world-grab yaw/drag/zoom in — the reveal appeared to move with the board. A world-**fixed** absolute pose was equally wrong: world-grab rotates the rig about the grab pivot, so a world-fixed reveal swings out of view when the player repositions the board. Rig-local is the only frame that glues the panel to the physical head *through* a grab: a world-grab moves head and reveal together (no change in rig-local), and a tray-grab moves the tray, not the rig (also no change).
  *Scale:* `BaseWorldScale` was the **residual board coupling** behind user report #2 — it is derived from the board's hex tile size and is world-grab-invariant, so it pinned the reveal's distance and size to the board's diorama frame and world-grabbing the board dragged and rescaled the reveal with it. Switching to the live rig scale deliberately **reverses** test #23(b)'s earlier "zoom with the board" intent.
- **Established by:** `f0bb9a1`, `16fcad4`, `4ba52a0` fix(worldui): decouple enemy reveal from the board — anchor to the player head frame, not the diorama base scale
- **Breaks if:** re-derived from `head.transform` world pose, or the scale is switched back to `BaseWorldScale` (an easy mistake — the *other* tray surfaces legitimately use the board frame).
- **Confidence:** high

### The reveal's board clearance raises by ELEVATION along the gaze, one-way, on the TARGET only
- **Where:** `EnemyRevealSurface.ApplyBoardClearance`, `NominalHalfHeight` (0.20 used as a **floor**), `MaxLiftAboveEye` (0.10)
- **Rule:** the clearance is applied to the **target** pose only (the snap and the lazy-follow goal), never to the applied pose per frame. It raises by **elevation angle** φ along the gaze ray, not by world Y. The half-height used is `max(measured, NominalHalfHeight)`. The lateral test is a **sight-line** test, not a footprint/containment test, and is deliberately generous about the board's yaw.
- **Why:** applying it per frame would re-introduce board-bobbing. Raising by Y would shorten the view ray — "1.3 m along a 60° downward gaze is only 0.65 m of ground reach, so a vertical lift shortens the ray a lot and the fixed-size reveal would loom at arm's length". The `NominalHalfHeight` floor exists because at spawn the host rect is still growing with the staggered card animation, and under-estimating there lets the finished panel sink back into the board. The sight-line framing is the key insight: the panel is already **beyond** the board in world space — it is occluded by **perspective**, not by containment, so a containment test would never fire. `MaxLiftAboveEye` is the comfort cap: never trade "swallowed by the board" for "you have to look up".
- **Established by:** `c188abf` fix(worldui): the enemy reveal is no longer planted behind the control board
- **Breaks if:** re-expressed as a Y offset, applied per frame, or the nominal floor is dropped.
- **Confidence:** high

### The reveal follow has a `misfit` guard against a pointless ease cycle
- **Where:** `EnemyRevealSurface` follow (`FollowDeadzoneDeg` 22, `FollowDwellSeconds` 0.5, `FollowSettledDeg` 5, `FollowEaseRate` 3)
- **Rule:** the follow is suppressed when the panel is off-gaze *because the board-clearance floor put it there*.
- **Why:** the clearance floor deliberately holds the panel **above** the gaze while the player looks down at the control board. Without the guard the panel would sit permanently "off gaze", re-arm the follow every dwell, ease nowhere (it is already at the target) and settle again — a 2 Hz log/ease cycle forever.
- **Established by:** `c188abf`
- **Confidence:** high

### The reveal's real height bug was a tray-coupled `ScrollRect`
- **Where:** `EnemyRevealSurface` — disable the owning `ScrollRect` while floated (re-asserted each tick), pin `enemyCardsHolder.localPosition`, restore on release
- **Rule:** disable the ScrollRect, do not just lock the host's Y.
- **Why:** an attribution log **disproved** the tray-dock theory: the cards were provably under our Y-locked host yet their world Y still swung ±20. `enemyCardsHolder` is the **CONTENT** of the "Main Area" `ScrollRect` on the tray-docked `InitiativeTrack`, whose `LateUpdate` kept sliding the content vertically **inside** our fixed host. Only after this was found could the Y-lock be removed and full-axis lazy follow restored.
- **Established by:** `c8f8da6` fix(worldui): enemy reveal height — disable the tray-coupled ScrollRect (issue #3, ROOT); Y-lock removed in `e5e7027`
- **Breaks if:** someone re-adds a Y-lock instead of the ScrollRect disable.
- **Confidence:** high

### The orphaned board-coupled scrollbar is enumerated, not read from the ScrollRect
- **Where:** `EnemyRevealSurface` scrollbar hiding
- **Rule:** enumerate `Scrollbar` **GameObjects** directly under the owning `ScrollRect`'s subtree (fallback: the whole `InitiativeTrack`), skip descendants of `enemyCardsHolder`, disable the rest. Reversible.
- **Why:** `ScrollRect.verticalScrollbar` / `.horizontalScrollbar` are **NULL in the prefab** — the scrollbar GameObject is a manually-placed child, never wired to the component. `HideScrollbar(null)` returned 0 and the hidden>0-gated log never fired, which is exactly why the bug survived a round.
- **Established by:** `b9386ea` fix(worldui): kill board-coupled enemy-reveal scrollbar robustly
- **Breaks if:** "simplified" back to the component references.
- **Confidence:** high

### Reveal fit pinning uses growth **hysteresis** plus a hard timeout
- **Where:** `EnemyRevealSurface.FitPinSettleSeconds` (1), `FitPinGrowthFraction` (0.03), `FitPinHardTimeoutSeconds` (2.5)
- **Rule:** the settle clock is reset only by growth beyond **3 %**; a hard 2.5 s timeout pins regardless. `metersPerPx` is cached once the fit pins.
- **Why:** `PinWhenSettled` reset the settle clock on **any** >0.5 px growth, and the active card's highlight **pulses** slightly larger — so the fit never pinned and `TickFit` kept re-centring/rescaling: the panel twitched every ~2 s.
- **Established by:** `098ca86` (item 8), `9bc5916`
- **Confidence:** high

### `StatPanelSurface` is non-pokeable, side-anchored, and hysteresis-released
- **Where:** `StatPanelSurface` (`Convert(pokeable:false)`, host raycaster kept disabled, side offset, `ReleaseDelaySeconds` 0.3, `ChurnWindowSeconds` 2 / `ChurnWarnCount` 5)
- **Rule:** all three breakers together. The panel anchors to the **SIDE** of the miniature (screen-right of the head→actor axis + a small lift), never above it.
- **Why:** a 45 Hz **self-occlusion feedback loop**. Pointing the laser at a miniature made the game show `ActorStatPanel`; placing it straight ABOVE the miniature put it **inside the very ray inspecting it**. The registered host latched the beam (`Ray.HasFreshUiHit`) → `IsPointerOverUI` true → `Interactable()` null → the game hid the panel → we released → the ray hit the board again → re-show. "Converted ActorStatPanel" appeared every other frame; figures were unclickable.
- **Established by:** `c0c6364` fix(worldui): stat panel non-pokeable, side-anchored, release hysteresis — kills the self-occlusion loop
- **Breaks if:** any *one* of the three is removed as redundant. They are independent breakers by design.
- **Confidence:** high

### The second stat panel is a DUMB VISUAL SNAPSHOT, never a Singleton clone
- **Where:** `StatPanelSurface` snapshot path, `HealSingleton`, `CopySnapshotDelaySeconds` (0.35) / `CopySnapshotTimeoutSeconds` (2)
- **Rule:** the real panel briefly shows the second figure, its hierarchy is `Instantiate`d under an **INACTIVE holder** (so `Awake` never runs), and `ActorStatPanel` + `UIWindow` + every `Singleton`-derived component is `DestroyImmediate`d **while never-activated** (so `OnDestroy` never runs either). Only the bare imagery is converted. `HealSingleton` runs unconditionally every Tick.
- **Why:** cloning the live `ActorStatPanel` Singleton meant its base `OnDestroy` **nulled the game's `_instance`**, soft-locking the enemy turn via the `HideActorStatPanel` NRE inside `Choreographer.ProcessMessage(StartTurn)`. Rated game-breaking.
- **Established by:** `50537a1` (task 1)
- **Breaks if:** the inactive-holder instantiate or the never-activated destroy is "cleaned up".
- **Confidence:** high

### The held stat panel docks on the side OPPOSITE the holding hand
- **Where:** `StatPanelSurface.ShowHeldFigure` (`_heldSideSign` from `hand.Side`), `HeldSideOffset` (0.15) / `HeldUpOffset` (0.05)
- **Rule:** right hand → viewer-left panel, left hand → viewer-right.
- **Why:** otherwise the holding hand occludes the panel it just opened.
- **Established by:** `4b1bff6` (item 5)
- **Confidence:** high

### `ActorStatPanel.Show` needs the latch cleared before retargeting
- **Where:** `StatPanelSurface` ForceShow / ClearHeldFigure symmetry
- **Rule:** `ForceShow` clears the latch (`HideForActor`) **before** `Show`; `ClearHeldFigure` hides the panel.
- **Why:** `ActorStatPanel.Show` is gated by `CanShow()`, which **refuses while `m_ActorShown != null`** — once the first figure was shown the panel latched to it and never retargeted, and `ClearHeldFigure` never hid it so it stuck in the world.
- **Established by:** `251bdc5` (item 3a)
- **Confidence:** high

### The DecisionDock docks the WHOLE dialog box, not the isolated button row
- **Where:** `DecisionDockSurface` prompt entries (dialog `box` preferred; isolated Yes/No row only as fallback)
- **Rule:** dock the entire dialog `box` (question text + both buttons together).
- **Why:** docking only the common ancestor of the Yes/No buttons dropped the question text (a sibling under `box`) so the player could not read the confirmation — **and** the button-only row measured as empty content, so the content fit found "nothing visible", collapsed the docked poke/laser plane to a **degenerate rect**, and the `ExtendedButton` clicks never landed. A hard deadlock.
- **Established by:** `2f45b51` fix(worldui): dock the whole short-rest confirm dialog, not just its buttons
- **Breaks if:** re-narrowed to "just the actionable widgets" for tidiness.
- **Confidence:** high

### Rows are isolated STRUCTURALLY, never by name
- **Where:** `DecisionDockSurface` row isolation (deepest common ancestor of the serialized actionable widgets, strict descendant of the window root)
- **Rule:** structural isolation only.
- **Why:** an explicit design rule stated at the generalization commit — names rot across game updates and localizations.
- **Established by:** `c6dbaa8` feat(worldui): dock every in-scenario decision below the cards
- **Confidence:** high

### The claim has a 1.5 s grace and then hands off to the float
- **Where:** `DecisionDockSurface.ClaimGraceSeconds` (1.5), `FloatDistanceMeters` (1.1), `FloatScaleFactor` (0.7)
- **Rule:** if the row cannot be isolated/converted within the grace, the claim **breaks** and the generic float takes over.
- **Why:** the stated contract is "a worse experience, never a deadlock". A claimed window is skipped **completely** — no float, no screen, no `ModalUI` — so a stuck claim would be an invisible hard lock.
- **Established by:** `e2733c5`, `c6dbaa8`
- **Breaks if:** the grace is removed on the grounds that isolation "always works".
- **Confidence:** high

### The dock asserts NO `ModalUI` — that is the point
- **Where:** `ModalFallback` claim map (claimed → skipped entirely)
- **Rule:** a claimed decision window never asserts `ModalUI`.
- **Why:** the burn follow-up (LoseCard flow) runs **through the card fan**, and `ModalUI` kills the fan's palm gate. The original `TakeDamagePanel` float asserted `ModalUI` and thereby broke the very interaction it was prompting for.
- **Established by:** `e2733c5`
- **Confidence:** high

### The decision gap is driven by mod-owned PLACEMENT, not in-row compression
- **Where:** `DecisionDockSurface.Place` (gap from the board's lower edge to the widget block top), `DecisionRowGapPx` (default 40, range 0-120), `BarClearanceMeters` (0.008), `NoBarPromptRefUp` (0.075)
- **Rule:** `DecisionRowGapPx` sets the distance between the grab-bar bottom (where the prompt is drawn) and the TOP of the interactive widget block, measured from the **visible button graphics**. The block top always keeps ≥ 8 mm clearance under the bar; gap ≥ 0.
- **Why:** **four rounds** of in-row compression heuristics all failed, and the log says why each time. (1) "free Graphic above the widget band": the panel's `Selectable` rects are authored spanning their whole option columns, so the band top sat level with the prompt text and every graphic was either "inside a widget" or "not above the band". (2) Serialized prompt anchors: `TakeDamagePanel.takeDamageText` is the take-damage **BUTTON'S OWN LABEL** ("Text" under "Receive Damage"), so "text and widget share a branch" held **by construction**. (3) Leaf-rect compression moved only widget root rects — better, but still in-row. (4) Ground truth: the "Schadensphase: …" prompt line is **not a child** of the isolated row at all, so no in-row measurement can ever find the gap the user sees.
  The fix moves a quantity the mod owns outright.
- **Established by:** `a164c00` fix(worldui): drive decision-gap by mod-owned dock placement + ground-truth dump
- **Breaks if:** anyone re-implements "measure the gap inside the row". It has been tried four times.
- **Confidence:** high

### Decision-dock buttons use DELIBERATE poke semantics, alone
- **Where:** `Hands.Interact.DeliberatePokeSurfaces` registry; `DecisionDockSurface` tags its docked host canvas per dock and untags on **every** release path; `[WorldUI] DecisionPokeDeliberate` (default true, read live at press time)
- **Rule:** decision buttons: contact **arms** (pointerDown + light haptic), the click fires on the conscious **withdrawal** past ReleaseDepth, sweep-through past PressThrough or leaving sideways cancels silently. Every other converted surface keeps the v3 12 mm push-in depth-fire.
- **Why:** accidental poke-confirms on irreversible choices. The per-canvas registry exists so the two press models can coexist; the untag-on-every-release-path is what stops a stale canvas keeping the deliberate mode after undock.
- **Established by:** `36f1766` (USER #13b)
- **Confidence:** high

### A `LayoutGroup` on a moved widget's parent is disabled while docked
- **Where:** `DecisionDockSurface` (record + restore of disabled layout components)
- **Rule:** component-disable it, record it, restore on undock.
- **Why:** otherwise it re-asserts the authored position and undoes the shift the same frame.
- **Established by:** `36f1766`
- **Confidence:** high

### `TrayControlDockSurface` holds through a transient null
- **Where:** `TrayControlDockSurface.HoldSeconds` (0.5), `holdOnTransientNull` opt-in (ShortRest)
- **Rule:** a docked control with `holdOnTransientNull` survives a momentary `FindWidget()` null and only releases when the null **persists** or the target is genuinely dead/swapped.
- **Why:** the native "Kurze Rast" widget dock released the instant `FindWidget()` blipped null (a game-side hide/hand-refresh), then re-docked next frame — so the native widget and the mod-drawn button **flickered against each other every few frames**.
- **Established by:** `087db14` fix(worldui): hold the native short-rest dock, drop the mod-button flicker
- **Breaks if:** the hysteresis is removed because "the widget is always there".
- **Confidence:** high

### Native tray widgets convert with the content fit DISABLED
- **Where:** `TrayControlDockSurface` conversions
- **Rule:** no content fit on native tray widgets.
- **Why:** they convert at their own tight rect, so the fit's "nothing visible" shrink pass only churned the log and risked mis-sizing the click plane. The native rect backs the poke/laser plane directly.
- **Established by:** `087db14`
- **Confidence:** high

### A non-rendering docked host has its raycaster stood down
- **Where:** `TrayControlDockSurface.Place` (raycaster disabled when the host's `CanvasGroup` alpha is 0)
- **Rule:** a docked collider must never outlive its renderer.
- **Why:** the native `ReadyButton` stays docked and interactable while the game drives its `canvasGroup` alpha to ~0 — "invisible but pressable" CONFIRM.
- **Established by:** `d56e4c8` (item 7)
- **Confidence:** high

### `TrayNativeControls` defaults to **false**
- **Where:** `WorldUIConfig.TrayNativeControls`
- **Rule:** the docking path exists but is off by default.
- **Why:** `TrayControlDockSurface` docks the REAL widgets only while they are game-side active, and releases back to the mod button when they hide — so board buttons **flickered** between the flat widget and the 3D mod button. Once `NativeButtonSkin` made the mod buttons indistinguishable from native ones, the swap became pure downside.
- **Established by:** `a307274` fix(worldui): all board buttons permanently native-skinned (no flicker)
- **Breaks if:** the default is flipped back, or the (now-unused-by-default) dock path is deleted — it is a deliberate config escape hatch.
- **Confidence:** high

### The combat log's host rect is PINNED and its follow key was RENAMED
- **Where:** `CombatLogSurface.OnConverted` (disable the fit outright), config key `CombatLogFollowSeat`
- **Rule:** the log converts at its own full window rect and the dynamic fit is disabled permanently. The config key was renamed from `CombatLogFollow` to `CombatLogFollowSeat`, and PINNED is the default.
- **Why:** (a) the fit thrashed 569×138 ↔ 569×291 as entries faded; the test-#17 damping only slowed it to once per 1.5 s, and on a static panel every applied re-fit reads as the content **jumping**. Static beats hugging. (b) The rename is deliberate config invalidation: an old persisted `true` would otherwise stick and keep the disliked head-follow behaviour. **A config key rename is a migration, not a typo fix.**
- **Established by:** `f043977`, `b7ab727`
- **Breaks if:** the key is "corrected" back to `CombatLogFollow`, silently restoring every user's stale value.
- **Confidence:** high

### The combat log's orientation derives at EVENTS only
- **Where:** `CombatLogSurface` (derive on conversion, on grab release — snap upright, yaw toward the head at that moment — and on seat-yaw re-derive)
- **Rule:** between events the panel does not move at all, like the control board. Yaw carry is **ON** during a grab.
- **Why:** the per-tick yaw billboard read as the content shifting with head motion and the panel constantly re-facing; and the billboard **fought the grab carry**.
- **Established by:** `b7ab727`
- **Confidence:** high

### A combat-log SHOW always requests a fresh in-view respawn
- **Where:** `CombatLogSurface.PlaceInView` via `PanelPlacement.Spawn`; separate `CombatLogUserClosed` flag
- **Rule:** the settings toggle / X-close recovery forces the next `Place()` to spawn in front of the head **regardless of FOLLOW/PINNED and any stale offsets**, and persists that as the new layout.
- **Why:** the show/hide flags were consistent, but a re-show re-derived the pose from persisted grab offsets — after a move (or a stale saved pose) the panel came back **out of the current view**. The toggle read as a complete no-op. The user-closed flag is kept **separate** from the feature master so hiding/re-showing never disturbs the feature toggle or the persisted layout.
- **Established by:** `1230785`, `e528764`
- **Confidence:** high

### `DamagePreviewSurface` asserts `Focus(true)` every tick and turns off exactly once
- **Where:** `DamagePreviewSurface` (`FocusRequest = "VR_DAMAGE_PREVIEW"`), turn-off uses the controller/values **cached at turn-on**
- **Rule:** while `DecisionDockSurface.DockingTakeDamage` holds and the panel has a valid attacked actor, assert `Focus(true, FocusRequest)` then `PreviewSimpleDamage(...)` each tick, read from the panel's own `CalculateCurrentDamage/Health`. On undock, `ResetDamagePreview` + `Focus(false)` exactly once.
- **Why:** the overlay is only activated by `WorldspacePanelUIController.Focus(true)` — nothing asserted it in VR — and the hover handlers that repaint it are **deliberately swallowed** while the row is docked (anti-jitter, see `TakeDamagePanelSafety`). Caching the controller at turn-on is required because the actor is nulled before the undock is observed.
- **Established by:** `8de7beb` fix(worldui): show world-space HP-cost damage preview in VR
- **Breaks if:** it "reuses" the live controller at turn-off time.
- **Confidence:** high

### `DamageTooltipSurface` converts whichever HelpBox is showing
- **Where:** `DamageTooltipSurface` (strict descendant of the take-damage dock)
- **Rule:** convert **either** `InitiativeTrack.Instance.helpBox` **or** the global `Singleton<HelpBox>`, with `Flatten2D`, parked just above the docked widget row. Only ever converted during the take-damage dock.
- **Why:** `TakeDamagePanel.ShowDamageTooltip` pushes its hint into *one of two* HelpBox windows depending on lethality. Both are self-contained `UIWindow`+`Canvas` boxes rendered by the perspective UI camera → 3D-tilted and floating up by the initiative track, nowhere near the buttons being read. Scoping the conversion to the dock keeps the global box's other uses untouched.
- **Established by:** `1a06673`
- **Confidence:** high

### The prompt TEXT is seated FROM the row, never beside it
- **Where:** `DecisionDockSurface.RowTopUpMeters` / `RowSeatTopUp` / `GapBoardMeters`, `DamageTooltipSurface.Place`, mirrored by `Net/RemoteBoardFurniture` (`promptRefY`)
- **Rule:** the prompt line's **bottom edge** hangs one `[Cards] DecisionGap_<board>` above the row's **measured top edge**, which the row publishes from the same walk that publishes its bottom edge for the use bars. No second seat is ever computed from the shared constants; when no measured row exists yet, the fallback is the row's own `RowSeatTopUp` (the seat the block is about to take), not a formula of the text's own.
- **Why:** the text used to be parked a fixed `0.11 × trayScale` above the decision **mount** while the row anchors to the grab bar in a solve the mount cancels out of — so `[Cards] DecisionOffset_<board>.y` (shipped default −0.157) moved the text and nothing else, and the user reported the line "reagiert wie ein Element das nicht zu dem Bereich dazugehört" (ModBuild 89). Two independent computations of one seat is how they drifted.
- **Note:** `RowTopUpMeters` deliberately survives the focus hide (unlike `RowBottomUpMeters`): the text keeps placing while hidden so it returns at final geometry.
- **Breaks if:** anyone re-derives the text seat from the mount, or nulls the top edge on the focus hide.
- **Confidence:** high

### The decision OFFSET moves the whole area; the decision GAP spaces it. Never the other way round
- **Where:** `DecisionDockSurface.MountOffsetUp` (fed into `Place`'s target and into `RowSeatTopUp`), `PlayTray.DecisionMountBase` (y −0.447), `Defaults.DecisionOffset_*` (y 0), the one-shot `[Cards] DecisionOffsetYRebased` migration, mirrored by `Net/RemoteBoardFurniture` (`promptRefY += decisionOff.y`)
- **Rule:** `[Cards] DecisionOffset_<board>` displaces the decision area **as one rigid body** — buttons, prompt text and the use-slot bars, by the same millimetres, on all three axes. X/Z do it by moving the mount everything is placed relative to; Y needs `MountOffsetUp` re-added to the up-axis solve, because that solve is anchored to the grab bar and the mount's own Y cancels out of it. `[Cards] DecisionGap_<board>` remains the ONLY dial that changes a distance *inside* the area (text ↔ button top). Neither dial may ever acquire the other's job.
- **Why:** the ModBuild-89 coupling made the text follow the row, which turned the Y dial from "moves the text only" into "moves nothing at all" — the user's sentence ("wenn ich den ganzen Entscheidungsbereich nach unten verschiebe … soll der Text mit") was still not true of the build. Making the dial live at its shipped −0.157 would have dropped the area 157 mm, so the displacement moved into the mount's base and the default became 0: identical geometry, live dial.
- **Breaks if:** anyone "simplifies" `DecisionMountBase.y` back to −0.290 (the area drops 157 mm), restores −0.157 in `Defaults`, or scales the offset by `DecisionScale` (resizing the dock would then move it — the ruling the gap already answers).
- **Proof in a log:** `DECISION DOCK SEAT:` states the applied offset and the block top *below the prompt reference* (= gap − offset), the one number the offset cannot move by moving the mount.
- **Confidence:** high

### `PropInfoSurface` release hysteresis and churn warning
- **Where:** `PropInfoSurface.ReleaseDelaySeconds` (0.3), `ChurnWindowSeconds` (2), `ChurnWarnCount` (5)
- **Rule:** a re-show within the window cancels the pending release instead of re-converting; more than 5 conversions in 2 s logs a warning.
- **Why:** the same anti-loop shape as `StatPanelSurface`. The churn warning exists so a *future* loop of this class is attributable from a hardware log alone.
- **Established by:** `66d4b7e`, pattern from `c0c6364`
- **Confidence:** high

### `HoverInfoScale` is read on every placement tick and multiplies `localScale`
- **Where:** `PropInfoSurface` absolute world factor (entry default 0.6 reproduces the old hard-coded `PanelLayout.WorldScale * 0.6f`); `WorldTooltips` multiplies its established metres-per-pixel by `HoverInfoScale / default` (exactly 1× at the default)
- **Rule:** read on every placement tick, not captured at conversion time; it scales the host's **uniform localScale** — a zoom, not a re-layout.
- **Why:** so an ALREADY SHOWN panel resizes on the spot instead of only on the next hover; and because nothing re-wraps, no rect is rewritten and the mutation stays trivially reversible on release. The two-family arrangement (one dial, two consumers with different unit conventions) exists because the mouse-over panels the user meant are `UITextInfoPanel`/`UIPropInfoPanel` — **not** `WorldTooltips`, which was the obvious guess and is the wrong family.
- **Established by:** `9d0ef99` feat(worldui): tunable size for the mouse-over info panels
- **Breaks if:** the two multiplication conventions are "unified" — the factory value would stop reproducing today's size.
- **Confidence:** high

---

## 5. World tooltips, hex hints, actor bars, wrist HUD

### The world tooltip is flattened IN PLACE, not converted
- **Where:** `WorldTooltips` (per-frame local-z/rotation zeroing + an added `RectMask2D`, both reverted on Restore)
- **Rule:** apply the `FlattenSubtree` idea in place; do **not** route the shared persistent `tooltipCanvas` through `CanvasConversion.Convert`.
- **Why:** the shared canvas is flipped to WorldSpace directly by the game and laid out internally by it — reparenting is too invasive. But bypassing the conversion also bypassed the flatten pass, so the baked local-z/rotation rendered as literal 3D geometry (text protruding). Tooltip lines are pooled/rebuilt per hover and the game rewrites them, so the zeroing must be **per-frame**.
- **Established by:** `1d0e439` (A)
- **Confidence:** high

### World-space tooltips are SCENARIO-ONLY, and the scale is applied at FLIP time
- **Where:** `WorldTooltips` mode gate (Menu2D keeps the vanilla Screen-Space-Camera tooltip); scale applied at flip and re-asserted every frame; canvas parked out of view until anchored
- **Rule:** both halves.
- **Why:** flipping the canvas to world space in `Menu2D` too, while applying the world scale only once a poke anchor existed (which **never happens in the menu**), left the canvas rect at its pixel dimensions **in world metres** — ~2560 px = 2.5 km. The reported "giant mouseover".
- **Established by:** `6ea1d1f` fix(worldui): world-space tooltips are scenario-only; scale applied at flip time
- **Breaks if:** the mode gate is dropped, or the scale is applied lazily again.
- **Confidence:** high

### The tooltip anchor is derived from REAL renderer bounds, every tick
- **Where:** `WorldTooltips` park/anchor computation
- **Rule:** derive the board's real top edge from the tray's combined **MeshRenderer bounds** expressed in board-LOCAL space (tilt-tight: `mesh.bounds` through the per-renderer relative matrix, world-AABB fallback), take max local +Y across the visible frame/decorations, and place the pivot above it along the board UP axis, centred on local X 0, proud toward the viewer. Recompute **every tick** from the live board pose and `lossyScale`.
- **Why:** two successive failures. (a) A fixed 3 cm board-local margin carried the board scale but ignored the tooltip's **own rendered half-width** (worldScale × hundreds of px ≈ 0.1 m) — an order of magnitude past the margin — so the panel sat *inside* the board and intruded further the more the board was resized up. (b) Pushing out by a half-extent derived from the **authored plate constants** (`InitiativeMountWidth`/`BoardTopLocalY`) under-estimated the real edge, because the VISIBLE board (bundled frame + decorations) is larger than the plate.
- **Established by:** `78628bd`, then `6a1e20c` fix(worldtooltips): pin action hint fixed ABOVE board top edge from real renderer bounds
- **Breaks if:** the authored constants are used again, or the recompute becomes one-shot.
- **Confidence:** high

### Tooltip hover grace never touches the tooltip's own alpha
- **Where:** `WorldTooltips` grace (widen the game's own show/hide fade: `transition=Fade`, longer `transitionDuration`; latch the parked position for a grace window)
- **Rule:** **deliberately never** touch the tooltip's alpha or `CanvasGroup`.
- **Why:** the game **reads it back** to drive its own state machine and cleanup. Writing it corrupts the game's tooltip lifecycle.
- **Established by:** `debe792` (user #7b)
- **Breaks if:** the grace is "simplified" to holding alpha at 1.
- **Confidence:** high

### `HexHintFacing` runs in LateUpdate and overrides ONLY rotation, ONLY while visible
- **Where:** `HexHintFacing`
- **Rule:** after `PropInfoSurface`'s Update-time placement, override the converted host's **rotation** to a live upright billboard toward the head. Position and scale stay `PropInfoSurface`'s. On hide it does **nothing**. Finds hosts via `CanvasConversion.ActivePanels` by `ConvertedPanel.Target` identity — it never touches `PropInfoSurface`.
- **Why:** the `PanelSlot.PropInfo` slot rotation faces the **cached seat yaw** (`PanelLayout` world-anchoring), so after a snap-turn / world-grab / lean the hint no longer squarely faces the head. Per-frame facing is *correct here* — the hint must track the head continuously while shown — which is the exception to the event-gated placement rule elsewhere.
- **Established by:** `f0825f4` fix(worldui): face hover hex-hint panels to the live head
- **Breaks if:** merged into `PropInfoSurface` (loses the LateUpdate ordering) or made one-shot.
- **Confidence:** high

### `ActorBars` push a fixed valid zoom exactly once at adopt
- **Where:** `ActorBars.Adopt` → `controller.OnUpdatedZoom(1f)`
- **Rule:** call it once per adopted panel with a **non-zero** value.
- **Why:** the game's `HealthBar` never left its `zoom = -1` sentinel, so `AdjustHealthBarMarks` early-returned and every bar kept the prefab-default marks instead of pooling `maxHealth-1` division marks. Root cause: `WorldspacePanelUIController.OnUpdatedZoom` is fed from the flat RTS-camera zoom via a `UnityEvent` **wired only in the prefab/scene** — there is **no code caller**, so it never fires in VR. Non-zero is required (the controller guards on its 0f-default cached zoom); the zoom only selects mark *styling*, the segment **count** is `maxHealth-1` at any zoom ≥ 0.
- **Established by:** `ac79b2c` fix(worldui): per-character HP-bar segments on VR actor bars
- **Breaks if:** the value is changed to 0, or the call is moved to a per-frame path.
- **Confidence:** high

### `ActorBars.Adopt` guards the game's `OnUpdatedZoom`
- **Where:** `ActorBars.Adopt` try/guard
- **Rule:** guarded; segmentation defers to the next health update on failure.
- **Why:** it NREs on not-ready 100×100 bars — the attributed source of a **~1-per-frame NRE flood** which, before `TickGuard` existed, starved the whole input pipeline.
- **Established by:** `6db83fb`
- **Confidence:** high

### Bar anchors are cached in BOARD units from renderer bounds
- **Where:** `ActorBars` anchor height (mini's renderer-bounds top + 12 % clearance, cached at adopt); `BarFixedSize` (default true)
- **Rule:** the anchor is in board units, so it scales with the diorama by construction; the distance clamp stays in HMD-relative **real** metres. The bar keeps a **fixed board-space size** — no distance-based growth.
- **Why:** a fixed real-metre offset (`0.045 × worldScale`) shrank in board units when the diorama was pinch-zoomed larger, sinking the bars **into** the miniatures. Separately, the LateTick distance compensation scaled the host up to 2.5× with HMD distance — bars grew when stepping away.
- **Established by:** `e93e3d1`, `d775986`
- **Breaks if:** the two unit systems (board units for the anchor, real metres for the clamp) are unified.
- **Confidence:** high

### Bar depth-testing is per-Graphic INSTANCE materials, under a FRESH config key
- **Where:** `ActorBars` depth-test path; `[WorldUI] BarsOccluded` in its own `dev.gloomhavenvr.bars.cfg`
- **Rule:** every `Graphic` under an adopted bar host gets a **per-instance** material with `unity_GUIZTestMode = LEqual` (and `_ZTestMode` where exposed for TMP); a 2 s rescan catches pooled division marks; snapshot+restore on release/shutdown/live-off. Bars stay **enabled** and billboarding — walls simply occlude them.
- **Why:** the mechanism was already in-tree but gated on the never-installed `DepthShaderSwap.BarsDepthTest` (always false). The new key exists **because a new key sidesteps the BepInEx persisted-config trap** — an old key would come back with the user's stale value. Also a standing user mandate: nothing may be toggled on/off to stop wall bleed-through; everything stays enabled and depth-tests.
- **Established by:** `ed942c7`, mandate in `f97920e`
- **Breaks if:** the config key is renamed/merged, or the hide/show probe is reintroduced.
- **Confidence:** high

### `ActorBars` hides the bar of a held actor, on edges only
- **Where:** `ActorBars.LateTick` (toggle `HostGo` active only on held↔released edges, behind a `HeldFigures.Count` fast-path)
- **Rule:** edge-triggered, with a count fast-path so other actors' bars are untouched.
- **Why:** redundant with the docked info panel; and a per-frame `SetActive` sweep over every bar is both wasteful and prone to fighting the game.
- **Established by:** `4b1bff6` (item 6)
- **Confidence:** medium

### `WristHud.FlatBackOfHand` is a single PROPER rotation — no handedness branch, no baked pitch
- **Where:** `WristHud.FlatBackOfHand` = `Quaternion.LookRotation(Vector3.up, Vector3.forward)`, applied in `WristHud.Build` / `WristHud.ApplyPose`
- **Rule:** one rotation for both hands. canvas +Z → wrist +Y (readable front out the back of the hand, toward the HMD); canvas +Y → wrist +Z (12 o'clock toward the fingers); canvas +X → wrist −X. The plane spans wrist X and Z. It is a **proper** rotation (det +1), so the front face is never mirrored. Tilt is `FlatBackOfHand * Euler(Pitch, Yaw, Roll)` from live per-style config — **never** baked into the base rotation.
- **Why:** four orientation commits, each fixing the previous one's regression, converged here. The repo's card/tray canvases read from local **−Z** (`PanelPlacement.Facing` "+Z away from viewer"; `VRCard` "viewer on the −Z side") and the wrist code inherited that assumption — but this TMP canvas reads from local **+Z**. The failure sequence was: vertical panel → edge-on invisible panel (normal put on the finger axis) → upside-down → **mirror-reversed back face**.
  **The absence of a per-side branch and of a hard-coded 90° pitch is itself the invariant.** `3cc7ac8` (un-mirror the LEFT wrist) and `123f596` (fold a 90° pitch) are both *superseded*; reintroducing either re-opens its bug. The in-code FLIP FALLBACK recipe is explicit that the normal and the gate axis must move **together**.
- **Established by:** `c66372a` → `4ad81b1` → `123f596` → `3cc7ac8` → `a0d4211` fix(worldui): WristHud flat on back-of-hand, +Y normal + matching gate
- **Breaks if:** a handedness sign flip is added "to un-mirror the left hand", or a pitch is baked into `FlatBackOfHand` instead of going through the config.
- **Confidence:** high

### The wrist look-at gate tests `Root.up` (+Y) — the gate axis and the panel normal are one decision
- **Where:** `WristHud` visibility gate, `ShowDot` 0.35 / `HideDot` 0.2 (asymmetric Schmitt trigger)
- **Rule:** the gate axis must equal the panel normal (+Y). Thresholds are asymmetric and deliberately low.
- **Why:** a regression put the panel normal on wrist **+Z** (the finger axis) so it stood perpendicular to the back-of-hand plane, while the gate still tested +Z (fingers, away from the face) so alpha never rose — an invisible panel behind an invisible gate, two bugs masking each other. The thresholds were lowered because "VISIBILITY is the #1 requirement: the back of the hand only needs to be roughly toward the HMD".
- **Established by:** `a0d4211`
- **Breaks if:** the normal is changed without the gate, or the hysteresis is collapsed to one threshold.
- **Confidence:** high

### The wrist HUD re-maps the resolved actor onto the LIVE scenario actor by GUID
- **Where:** `WristHud.ResolveActor` + live re-map by `ActorGuid` against `ScenarioManager.Scenario.PlayerActors`
- **Rule:** re-map before reading stats; compare identity by **guid**, not reference.
- **Why:** the flow-facing actor resolution (choreographer message actor / hand actor / initiative-track actor) can return a **SNAPSHOT CLONE** whose `m_Gold`/`m_XP` are frozen at capture, while the rules mutate the live instances. Gold/XP read stale forever even though the 4 Hz poll was running.
- **Established by:** `385b39e` (bug B)
- **Confidence:** high

### The wrist HUD shows only the LOCAL player in multiplayer
- **Where:** `WristHud.ResolveActor` (`IsUnderMyControl` gate when online)
- **Rule:** online → local only; offline unchanged.
- **Why:** MP information-leak rule.
- **Established by:** `ed3a0cf`
- **Confidence:** high

---

## 6. Avatar mirror

### A held CARD billboards to the REFLECTED head; a held FIGURE carries the hand-local pose
- **Where:** `AvatarMirror` held-card path vs `AvatarMirror.TryMirrorThroughHand`
- **Rule:** the two held-item classes are handled **differently and deliberately**. A grip-held card is billboarded to the reflected head. A held figure (and the grip-held card slab, on the pose side) is expressed in the **REAL hand's rigid frame** (`o = R⁻¹·(itemPos − handPos)`, plus localRot) and re-emitted under the **mirrored hand holder's** frame (`holderPos + R'·o`). Free-floating fan cards keep the exact world reflection. The open-fan loop **skips grip-held cards** so they cannot be double-placed.
- **Why:** the mirror's `Reflect()` rebuilds orientation as a **proper** rotation (`LookRotation` of the reflected forward/up). A true planar mirror is **improper** (det −1), so that proper rotation equals the true reflection composed with a local X flip: `R' = M·R·diag(-1,1,1)`. The rendered mirror hand (un-mirrored mesh under `R'`) therefore has its **thumb axis pointing opposite** to the true reflection. Items placed at the exact world reflection landed at the **PINKY** instead of between thumb and index.
  Separately, carrying a head-derived rotation through the wrist frame was wrong for cards — a mirror shows card **backs**, and the card must present to the reflected viewer, not rotate with the wrist.
- **Established by:** `d5c9c44` fix(mirror): held figure/card sat at pinky side — carry hand-local pose through the mirrored hand frame; card billboarding in `c2114a6` / `c7bff30`
- **Breaks if:** the two paths are unified "since both are held items". They are geometrically different problems.
- **Confidence:** high

### Style scale lives on a `HandVisual` CHILD, not the holder
- **Where:** `HandVisuals.Build` (writes the per-style scale onto the child it builds under); `AvatarMirror.Tick` and `RemoteAvatar.SetTarget` write only the holder
- **Rule:** the holder carries rig/sender scale; the child carries the style scale; a live check re-applies config edits.
- **Why:** both mirror and remote paths **stomped** the style scale on the shared holder, so styled hands rendered ~1.6× too big. `RemoteHandFan` sizing off the holder is unaffected by the split.
- **Established by:** `19a50e5` (task 1)
- **Breaks if:** the extra transform level is flattened away.
- **Confidence:** high

### The mirrored held figure needs a container + `LatePin`
- **Where:** `AvatarMirror` figure-clone container + `LatePin` (re-zero the clone's `localPosition` after the Animator)
- **Rule:** the clone sits in a container that takes the reflected pose; the clone's own local position is re-zeroed **after** the Animator runs. The reflected **source** pose is the RENDERED one (parent position + animated rotation).
- **Why:** the clone root **is** the Animator's GameObject and the game's clips write **ABSOLUTE root position curves**, so the reflected pose written in `Update` was stomped by the clone's own Animator every frame (the clip's baked pose = "wrong spot"). This is the exact counterpart of `HeldFigures.PinAnimatedRoots`.
- **Established by:** `19a50e5` (task 3)
- **Confidence:** high

### `AvatarMirror` tracks the live hand style and resets `_appliedScale` on rebuild
- **Where:** `AvatarMirror` style tracking, `AvatarMirror.BuildHands` / `RemoteAvatar.BuildHands`
- **Rule:** re-read `[Hands] HandStyle` and rebuild the hand visuals on change; reset `_appliedScale` so the holder scale (overwritten by `ApplyStyleScale` during a rebuild) is re-applied.
- **Why:** the style was read **once at build**, so the mirror showed the wrong hands after a live style switch; and the scale-applied latch made the rebuild silently keep the old scale.
- **Established by:** `9461a69` (task 2)
- **Confidence:** high

### `Teardown` must not destroy `CardMesh`'s SHARED back material
- **Where:** `AvatarMirror` teardown
- **Rule:** never destroy the shared material.
- **Why:** it turned **all** card backs pink after closing the mirror.
- **Established by:** `19a50e5` (task 2, drive-by)
- **Breaks if:** a generic "destroy everything we created" teardown is written.
- **Confidence:** high

---

## 7. Settings panel

### FOLLOW placement is EVENT-gated — the per-tick version wrote the config every frame
- **Where:** `SettingsPanel` FOLLOW branch (gated on first placement after open + `RigPoseVersion` bump)
- **Rule:** between events the panel keeps its world pose; grab-move only.
- **Why:** the FOLLOW branch ran **every tick**, deriving position from the live diorama `WorldScale` (so world-grab zoom slid the panel nearer/farther — read as "rescaling with zoom") and re-clamping into the head's forward cone each frame. `ClampIntoView` parks the pose exactly **ON** the cone edge and `PersistLayout` writes it back, so any head motion re-healed next frame: the panel chased the head **and BepInEx saved the cfg every frame** (per-frame "healed back into the forward field of view" spam in the hardware log).
- **Established by:** `ed33865` (T1)
- **Breaks if:** the placement is moved back into the tick. Note the compounding failure: a per-frame *clamp* plus a *persist* is an infinite write loop.
- **Confidence:** high

### `Build()` must clear the three accumulation lists first
- **Where:** `SettingsPanel.Build` (clear `_refreshers`, `_debugRows`, `_debugRowVisible`), plus a null-guard in the visibility refresher
- **Rule:** both the clear and the guard.
- **Why:** the holder GameObject is a plain scene object (not DDOL), so a scene unload destroys it and all its rows while the `SettingsPanel` instance survives on the DDOL driver. The next `SetOpen()` saw the Unity-null `_holder`, called `Build()` again **without clearing**, and appended a second set of rows on top of stale destroyed ones. The 0.25 s visibility refresher then hit `go.activeSelf` on a destroyed row — an **NRE flood that aborted the rest of `RefreshAll`** — and the duplicated rows stacked into an over-long layout with dead buttons.
- **Established by:** `c1e60fc` fix(worldui): stop SettingsPanel debug-menu NRE flood on rebuild
- **Breaks if:** the clears are considered redundant with a fresh `Build`.
- **Confidence:** high

### The sidebar column is pinned rigid
- **Where:** `SettingsPanel` sidebar layout (`min == preferredWidth`, `flexibleWidth = 0`, `flexibleHeight = 0`; the content column is `flexibleWidth` only)
- **Rule:** all three.
- **Why:** otherwise a `HorizontalLayoutGroup` regime re-fits the sidebar to a taller/wider page and the buttons move between pages.
- **Established by:** `6910388` (item 1)
- **Confidence:** high

### The settings panel size is decoupled from diorama zoom, snapshotted at open
- **Where:** `SettingsPanel` `_sizeScale`
- **Rule:** a fixed real-world reference width (≈ control-board size), snapshotted at open; `WorldScale` still drives position/distance; `SettingsScale` (0.5×-2×) is the only user size control.
- **Why:** using `PanelLayout.WorldScale` for size made the panel rescale with table zoom.
- **Established by:** `91afaf3` (item 6), `6db83fb`
- **Confidence:** high

### Deprecated config entries stay BOUND
- **Where:** `[Comfort] SeatedMode`, `ComfortSettings.Vignette*`, `[Cards] DebugMenu`, legacy `[TransientButtons]`/`[SquareCaps]` handling
- **Rule:** an entry whose feature was removed stays **bound** (marked deprecated / no-op) as long as any other seam still reads it, and legacy sections are **migrated** on `Bind()` rather than dropped.
- **Why:** charter §5 — an unread config key is still a user's persisted setting, and removing it silently changes behaviour on their machine. Concretely: `SeatedMode` is still read by `ComfortGizmos` and `VRRigDriver` for diagnostics; `Vignette*` is still referenced by `SettingsPanel` after the vignette component was deleted; `[TransientButtons]` values migrate 1:1 into `[RoundButtons]` and nonzero `[SquareCaps]` values are copied into **every** category they used to affect, so the current look is preserved.
- **Established by:** `7bb5757`, `8454e88`, `e0432fe` (item 3), `6910388` (item 5)
- **Breaks if:** a "dead config cleanup" pass deletes them.
- **Confidence:** high

### The `0 = "Auto"` sentinel was REMOVED on purpose
- **Where:** `ButtonTuning` per-category geometry binds
- **Rule:** every bind defaults to the exact **authored numeric** value it replaced (board 0.073×0.073×0.036/4 mm; gear 0.062, Fixiert 0.068 × 0.030 × 0.030/4 mm; cluster 0.084×0.084×0.012/8 mm, cap 42 mm). Saved 0s resolve to the numeric defaults.
- **Why:** the sentinel made steppers show "Auto" instead of a real number, and made "0" ambiguous between "authored" and "zero-sized". The numeric defaults reproduce today's look bit-exactly.
- **Established by:** `e0432fe` (item 3)
- **Breaks if:** a sentinel is reintroduced for convenience.
- **Confidence:** high

### Button categories must not share binds
- **Where:** `[RoundButtons]` / `[BoardButtons]` / `[BoardDashboard]` / `[RestButtons]`
- **Rule:** nothing outside a category reads its binds. `BoardButton` press travel is **per-instance** (passed at Create); rest discs keep the authored 4 mm constant.
- **Why:** value leaks across button families — tuning one shape moved unrelated buttons.
- **Established by:** `e0432fe` (item 2), `dd3dfd4` (item 3)
- **Confidence:** high

### Live re-skin rides `ButtonTuning.Version`, not direct calls
- **Where:** `ButtonTuning.Version` / `ButtonTuning.Changed` consumed by `ButtonCluster.Tick`, `PlayTray.ApplyButtonTuningIfChanged`, `CardsDriver` rest rebuild, `NativeButtonSkin`
- **Rule:** every settings write bumps a version counter; consumers poll it.
- **Why:** BepInEx `SettingChanged` fires off the main thread; the version counter is the main-thread-safe seam (the P2 threading rule: the handler only sets a flag, `Update` consumes it).
- **Established by:** `aba34a7` (point 9), `6a94185`
- **Breaks if:** consumers are wired to `SettingChanged` directly.
- **Confidence:** high

---

## 8. Buttons

### Keycap presses are DEPTH-fired, with a re-arm AND a cooldown
- **Where:** `ButtonTuning.PressFireFraction` (~0.9), `PressRearmFraction` (~0.5), `ButtonTuning.PokePressCooldownSeconds` (0.4); `BoardButton.Update` / `BoardButton.Press`, `ButtonCluster.PhysicalButton.Fire`
- **Rule:** a re-fire needs **BOTH** the depth-fire re-arm (cap retracted past `PressRearmFraction`) **AND** the 0.4 s cooldown. Laser presses route through the same cooldown (debounced, not dwelled). No timer dwell anywhere.
- **Why:** fingertip **contact** firing caused accidental presses when brushing a docked panel. And with only one of the two gates, the retract/re-entry of a single poke plus `PokeInteractor` hover-flicker fired **twice**. The pile-stack fix was ported here after the same class of double-trigger appeared on keycaps.
- **Established by:** `aba34a7` (point 6), `dd3dfd4` (item 1)
- **Breaks if:** either gate is dropped as redundant, or a dwell timer is reintroduced.
- **Confidence:** high

### uGUI poke fire depth is clamped against `PressThrough`, and a pending press stretches it
- **Where:** `PokePressDepthMm` (default 12, 0 = legacy instant), `UguiPointer.Cancel`
- **Rule:** the fire depth is clamped to **0.8×** the per-canvas `PressThrough` drop guard so the click always lands before the canvas is dropped; a **pending** press stretches `PressThrough` by **1.5×** so a fast stab that overshoots between two frames still fires instead of silently cancelling. Retracting before the fire depth cancels via pointerUp-without-click and charges no cooldown.
- **Why:** without the clamp the canvas is dropped before the click; without the stretch, a fast poke is silently swallowed between frames. Note the earlier opposite failure: holding pointerDown on contact and firing on **Release** required retracting the fingertip 12 mm *in front of* the plane, but a natural poke sinks **through** — so the click landed late or never, reading as a mandatory long hold on every converted native button.
- **Established by:** `dab77fb`, prior instant-click round `7bcf8b3` (USER #3/#7b)
- **Breaks if:** the two multipliers are removed.
- **Confidence:** high

### The `ButtonCluster` is DEPTH-CORRECT: `ZTest LEqual`, proud seat, opaque `BoardLit`
- **Where:** `ButtonCluster` (no `ApplyDrawOnTop`/`RenderOnTop`), `ClusterProudOffset` (10 mm, scaled by mount world scale)
- **Rule:** base/cap use lit opaque `GloomhavenVR/BoardLit` in the Geometry queue with default `ZTest LEqual` and `ZWrite` on. The docked cluster is proud-seated along `mount.up` by a **constant** 10 mm — deliberately **not** the per-board `ClusterOffset`.
- **Why:** at `renderQueue 4000-4003` with `ZTest Always` the cluster was the last live mod depth-breaker: it drew over walls in front and bled card **text** through held cards. Once `ZTest Always` is gone the base plate must clear the raised board rim, hence the proud seat. The per-board `ClusterOffset` is already consumed by `PlayTray` for the mount, so reusing it **double-applies**.
- **Established by:** `9591504` fix(worldui): make ButtonCluster depth-correct (proud seat + LEqual, drop ZTest Always)
- **Breaks if:** the proud offset is "unified" with the per-board offset.
- **Confidence:** high

### Round keycaps are a cached 64-segment generated disc
- **Where:** `CardMesh.GetRoundCap` (cache keyed on diameter/thickness/segments), planar XY UVs
- **Rule:** 64 segments, cached, authored at real size in the button's local frame (front toward the viewer, −Z).
- **Why:** Unity's `PrimitiveType.Cylinder` is ~20-sided and visibly **facets** at cap size. Planar XY UVs are required so the shared carved-grain `BoardLit` `_MainTex` still maps across the round face. The cache prevents per-button/per-frame rebuilds.
- **Established by:** `e17366a` fix(board): smooth round keycaps
- **Confidence:** high

### The round cap's label anchors off the real cap TOP, at 2 mm
- **Where:** `DockedLabelProud` (2 mm), `_capTopLocalY`
- **Rule:** anchor off the cap **top** (`_capTopLocalY`) for both shapes; 2 mm proud.
- **Why:** two calibrations in opposite directions. Seating the label off the cap **centre** (`_capRestY + 14 mm`) put the Round label nearly flush with its top face (the Round cylinder's top is `centre + capD`, the shallow Square/native cap's is `centre + capD/2`); at diorama scale the gap fell under a millimetre and the co-planar TMP z-fought the opaque cap face **per-eye under stereo**. Then 10 mm over-corrected — a full cap depth above a ~9 mm puck — and the label visibly **hovered**. 2 mm reads as printed on the cap. Anti-flicker comes from render-queue + ZTest ordering (transparent 3000, ZWrite off, `ZTest LEqual` after the opaque cap); the offset only clears sub-mm per-eye depth noise.
- **Established by:** `dd3dfd4` (item 2), `9d0e9e3` (user #4)
- **Breaks if:** treated as a tunable and rounded.
- **Confidence:** high

### The docked cluster is parented RIGIDLY under the tray root
- **Where:** `ButtonCluster` dock (fixed local pose, recomputed only on config rebuild / tray switch / cluster-scale change)
- **Rule:** parent it, do not copy the mount's world pose per frame.
- **Why:** the pose-follow ran in a **different update pass** than the tray's grab-follow, so fast board drags rendered the cluster **one frame behind**. Parenting carries it in the same transform pass as Confirm/Undo: zero latency, no per-frame world-pose writes. Tray teardown destroys the parented cluster; `Tick` detects the dead root and rebuilds.
- **Established by:** `e0432fe` (item 4)
- **Breaks if:** converted back to a pose-follow for symmetry with the other docked surfaces (which are deliberately **not** parented — see next entry).
- **Confidence:** high

### Docked *panel* surfaces are POSE-FOLLOWED, never re-parented — the opposite rule
- **Where:** `TrayMountedPanelSurface` (copy position/rotation/scale from the mount each tick)
- **Rule:** hosts are never re-parented under the tray.
- **Why:** these hosts carry **live GAME UI**, and a tray/rig teardown must never cascade into destroying game-owned canvases. The `ButtonCluster` above is mod-owned, which is exactly why the opposite rule applies to it.
- **Established by:** `b66ceb0`
- **Breaks if:** the two docking mechanisms are "unified".
- **Confidence:** high

### `NativeButtonSkin` re-borders sampled 9-slice sprites for world scale
- **Where:** `NativeButtonSkin.ReborderForWorld` (re-mints each sampled sprite at a PPU giving a ~6 mm world border); label `sortingOrder` 3 vs face 1
- **Rule:** both.
- **Why:** the sampled `Button_Normal` sprite is a 9-slice whose border **in world metres** (`border_px / pixelsPerUnit`) dwarfs a few-cm tray face, so `SpriteRenderer.Sliced` collapsed the nine slices into **four overlapping corner tiles** — the reported "4 black fields in a grid with a gold frame, no text". And because the face `SpriteRenderer` and the TMP label are both transparent with ZWrite off, **sorting, not the label's nearer z, decides draw order** — the face drew over the text.
- **Established by:** `70296c1` (item 1)
- **Breaks if:** the re-border is dropped, or the label sorting order is reset to match the face.
- **Confidence:** high

### The skin sprite is chosen by a FREQUENCY VOTE
- **Where:** `NativeButtonSkin` sampling
- **Rule:** vote over every scene `Selectable` to pick the shared 9-sliced background sprite.
- **Why:** so the *generic* button art is reused rather than one special button's icon.
- **Established by:** `d579f9e`
- **Confidence:** high

### Button appear/disappear animations are purely visual; input is live from frame one
- **Where:** `ButtonDissolveFx.PlayMaterialize` / dust dissolve; `_everShown` / `_ticked` storm suppression; `[ButtonAnim]` (durations floored at 0.05 s)
- **Rule:** colliders and input stay live immediately; the initial build-then-settle frame is **silent** (no dust storm at scenario start); durations are floored so the ramps never divide by zero.
- **Why:** the storm guard exists because the first settling pass hides/shows many buttons at once. The visual/logical split is what lets the animation compose with depth-fire + debounce without affecting them.
- **Established by:** `aba34a7` (point 7), `d8b2da7`, `e17366a`
- **Confidence:** high

### CONFIRM/UNDO visibility is gated on whether the ACTION is possible
- **Where:** `PlayTray.TickStatus` (hide, not just disable, when `CanConfirm`/`ReadyToggle`/`IsConfirmed`/`CanUndo` say no)
- **Rule:** hide.
- **Why:** an unpressable-but-visible button is a lie about affordance; and a disabled-but-present collider ate laser hits.
- **Established by:** `2a97bae` (item 7)
- **Confidence:** medium

---

## 9. Input, pointer, chords

### Virtual-mouse currency keep-alive is LEVEL-triggered, per tick
- **Where:** `VirtualMouse.TickKeepAlive` (level, from `VirtualMouse.Tick`) vs `VirtualMouse.ReclaimCurrency` (edge, from `WarpTo`/`SetLeftButton`); `KeepAliveIntervalFrames` (30)
- **Rule:** the level-triggered `TickKeepAlive` runs every frame regardless of any warp. `ReclaimCurrency` is kept as the edge path but is **not** sufficient on its own. (The type is `VirtualMouse`, in `VirtualMouseBridge.cs`.)
- **Why:** the original reclaim ran only inside `WarpTo`/`SetLeftButton`, whose sole callers live in the FlatScreen composite. With **window-style modals** every poke/laser click goes through `ExecuteEvents` instead, so nothing drove the virtual mouse in steady state. When the HMD standby (doff) made the InputSystem re-enumerate devices and promote the physical `Mouse` to `Mouse.current`, **no code path could ever take it back** — the game read the frozen hardware cursor for the rest of the session: overUI flicker, dead button edges, un-dismissable panels, `ModalUI` never released. A doff/don hard-lock.
- **Established by:** `77c2164` fix(worldui): level-triggered virtual-mouse currency keep-alive + device re-add
- **Breaks if:** the keep-alive is moved back into the warp path "since that's when it matters".
- **Confidence:** high

### A removed virtual mouse is RE-ADDED as the SAME instance
- **Where:** `VirtualMouse.EnsureCreated`
- **Rule:** `InputSystem.AddDevice(device)` with the same instance; rebuild from scratch only if the re-add throws.
- **Why:** the game's `InputManager._virtualMouse` reference and the `InputUser` pairing must stay valid. The game's own `CreateVirtualMouse` only checks `_virtualMouse == null` and could **never** recover a removed-but-referenced device.
- **Established by:** `77c2164`
- **Confidence:** high

### While the HMD is WORN the virtual mouse wins unconditionally
- **Where:** `VirtualMouse.TickKeepAlive` gated on `VRPresenceWatch.UserPresent`; `ThiefActiveGraceSeconds` (0.25) and `KeepAliveIntervalFrames` apply **only** while not worn
- **Rule:** while worn, `Reassert()` then `return` — the frame-count gate and the active-thief grace are both **skipped entirely**. The worn/not-worn policy flip is logged once per transition (`_wornPolicyKnown` / `_lastWornPolicy`), never per frame.
- **Why:** test #18 logs show a currency **flip-war** between `Mouse` and `ConsoleVirtualMouse` every ~2 lines for whole sessions: Virtual Desktop keeps injecting host mouse events while the user plays in VR, so the physical mouse stayed permanently "fresh" and the round-17 active-thief grace deferred to it — every VD event stole `Mouse.current`, the 30-frame keep-alive took it back, forever.
- **Established by:** `6d4b575` fix(worldui): presence-gated pointer currency
- **Breaks if:** the worn/not-worn asymmetry is removed.
- **Confidence:** high

### Every warp and button write is ALSO queued as a state event
- **Where:** `VirtualMouse.WarpTo`, `VirtualMouse.SetLeftButton`, `VirtualMouse.Reassert`; `VirtualMouse._shadow`
- **Rule:** three writes in `WarpTo` (`WarpCursorPosition`, `InputState.Change`, `QueueStateEvent`) and two in `SetLeftButton`. The immediate write is built from the **live device** (`CopyState`); the queued write is built from `_shadow`. `Reassert` calls `MakeCurrent()` **plus** a same-state `QueueStateEvent` that looks like a no-op and is not: a bare `MakeCurrent` loses to any hardware event already queued this frame.
- **Why:** the game's uGUI click path polls **single-frame edges** on `Mouse.current` (`wasPressedThisFrame`), which requires `device.wasUpdatedThisFrame` **and** an unpressed previous-frame buffer. `InputState.Change` writes land mid-frame from a MonoBehaviour `Update`; buffers flip lazily on the first write per update step, so a poller running **before** us sees a stale step count in the press frame and an already-pressed previous frame in the next — **the edge is structurally invisible** when the EventSystem updates first, which it did on the test rig. Position needs no edge, which is why hover kept working and clicks were 100 % dead (test #6). Queued events are processed at the next frame's input update, before all Updates, and re-claim currency for free.
- **Established by:** `670bf4d` fix(worldui): deliver virtual-mouse edges via queued state events (dead clicks, test #6)
- **Breaks if:** the "redundant" immediate write or the queued event is removed.
- **Confidence:** high

### `ClickMode` defaults to `execute` (ExecuteEvents), not the virtual mouse
- **Where:** `FlatScreen.DirectClick` (`down + up + click` on one frozen pixel), `[WorldUI] ClickMode`
- **Rule:** a tap is delivered via `ExecuteEvents` on the raycast target — the game's own `BaseButtons.clickButton` mechanism. The virtual mouse keeps hover and deliberate drags.
- **Why:** immune to input-module edge-visibility quirks (see above). This is the *reason* clicks default to ExecuteEvents.
- **Established by:** `2f43a79`
- **Confidence:** high

### Flat-menu DRAG needs its own `ExecuteEvents` session
- **Where:** `FlatScreen.BeginScreenDrag` / `UpdateScreenDrag` / `EndScreenDrag`, `ScreenDragPointerId` (−120)
- **Rule:** on click-latch open: `pointerDown` + `initializePotentialDrag` at the press pixel, then `beginDrag`/`dragHandler` every tick, `endDrag` + `pointerUp` on release. `PointerEventData` must carry position, pressPosition, `pointerPressRaycast`, `pointerCurrentRaycast`, `button = Left` and `dragging`. Mutually exclusive with the click latch (a drag clears `_latched`); torn down on release/hide/hide-reticle/handedness switch.
- **Why:** main-menu sliders could only be **clicked**, never dragged, so extremes like 0 were unreachable — a drag routed through the virtual-mouse device whose button edges do not survive the input module on hardware. `EventSystem.RaycastAll` at the RT pixel is the real screen pixel, so `pressEventCamera` resolves the slider's local point.
- **Established by:** `ddb4294` fix(worldui): draggable flat-menu sliders via ExecuteEvents drag
- **Confidence:** high

### The click LATCH freezes the pixel and re-warps it every tick
- **Where:** `FlatScreen.TickPointer` latch, `[WorldUI] ClickLatch`, `DragUnlockDegrees` / `DragUnlockSeconds`; poke equivalents `PokeContactMeters` (0.01) / `PokeReleaseMeters` (0.03) / `PokeThroughMeters` (0.08) / `PokeDragUnlockMeters` (0.015)
- **Rule:** the warp position freezes at the press pixel until release, and the **same** pixel is re-written every tick while pressed. Deliberate ray movement past the unlock threshold opens the latch into a real drag. Edge tremor off the quad **clamps** instead of cancelling.
- **Why:** sub-degree tremor at 1.6 m onto a 2560×1440 RT moves the pointer dozens of px; the release raycast then misses the pressed `IPointerClickHandler`, and any `IDragHandler` ancestor starts a click-cancelling drag past `EventSystem.pixelDragThreshold`. Re-writing the *same* pixel changes nothing for uGUI (zero drag delta) but keeps the per-tick **currency reclaim** and the queued-event stream alive during a held press — a Virtual Desktop mouse jitter mid-press could otherwise hold `Mouse.current` until release.
- **Established by:** `02b0536`, `9f60e6f`
- **Breaks if:** the re-warp is removed as a no-op. It is a no-op *for uGUI* and load-bearing *for currency*.
- **Confidence:** high

### The pointer releases on trigger STATE, not only the TriggerUp edge
- **Where:** `FlatScreen.TickPointer` release condition
- **Rule:** level, not edge.
- **Why:** a hands rebuild mid-press swallowed the edge and left uGUI in drag state — **hover dies globally**.
- **Established by:** `5edc0a8` (I3 hardening)
- **Confidence:** high

### A synthesized press held > 5 s without movement is force-released
- **Where:** `VirtualMouse.Tick` watchdog; `StuckPressSeconds` (5), `ActivityEpsilonSq` (1 px²)
- **Rule:** force-release and log. The activity clock only re-arms on movement above 1 px² — sub-pixel jitter must not keep the watchdog alive.
- **Why:** a press without its matching release keeps uGUI in drag state and **kills hover globally** (`PointerInputModuleExtended.ProcessDrag` suppresses enter/exit while dragging). Legitimate drags always move the pointer, so movement is the correct discriminator.
- **Established by:** `dfb326c`
- **Confidence:** high

### `VirtualMouse.SetLeftButton` logs at Info, deliberately
- **Where:** `VirtualMouse.SetLeftButton` log level
- **Rule:** Info, not Debug.
- **Why:** stated in-code — "BepInEx filters Debug from the disk log by default, **which made test #6 look like presses never fired**". The log level is part of the fix, not decoration.
- **Established by:** `670bf4d`
- **Confidence:** high

### Suppressing physical mice must never disable the only mouse
- **Where:** `VirtualMouse.TickSuppressPhysicalMice` (`want` requires `_mouse != null && _mouse.added`), `_disabledPhysicalMice`, `VirtualMouse.RestorePhysicalMice`
- **Rule:** a hard interlock — never disable a physical mouse unless our virtual one is live and added. Track exactly what we disabled; re-enable only devices that are `added && !enabled`. If a device we disable held currency, `MakeCurrent()` ours immediately ("don't leave a disabled device as the read source"). Runs **after** the keep-alive in `Tick`, and re-runs every frame against re-enumeration.
- **Why:** without the interlock a mis-ordered init disables the only pointer and the session is unrecoverable. The suppression exists *in addition to* the keep-alive because "the keep-alive only fixes WHICH device is read, not that the physical one still emits position/hover when it briefly holds currency".
- **Established by:** `88b35b3`
- **Breaks if:** the interlock is dropped, the tracking list is replaced by "re-enable all mice", or the ordering vs the keep-alive is swapped.
- **Confidence:** high

### `VirtualMouse.ForceReassert` returns `null` when nothing was wrong
- **Where:** `VirtualMouse.ForceReassert`, consumed by `WorldUIModule.OnSessionResumed`
- **Rule:** bypasses the throttle and the grace (`_nextKeepAliveFrame = 0`); returns `null` for "nothing to report", and **two different strings** for "currency re-asserted" vs "device re-added".
- **Why:** the session-resume recovery report must stay quiet when the resume was clean, and must distinguish the two failure modes.
- **Established by:** `77c2164`, `f90659b`
- **Confidence:** high

### `NonDominantHold` FREEZES the press on pose loss and requires `_hadHand` both frames
- **Where:** `NonDominantHold` (freeze on no pose; release only when the hand is present **this frame and last**; `ButtonIsUp`; stranded-latch self-heal; `HeldSeconds` on **unscaled** time)
- **Rule:** all four.
- **Why:** each fixes a distinct manufactured-tap bug. (a) A pose hiccup while X was physically held dropped `down` to false with `_wasDown == true`, firing a **phantom release**; the next frame the pose returned with the button still down, producing a fresh press and a fresh phantom tap — one spurious toggle per hiccup. (b) The `_hadHand` requirement stops the frame the pose returns from manufacturing an edge. (c) The freeze, in turn, could **strand** the tracker: `_wasDown`/`Consumed` left untouched across the gap meant no later press read as a fresh edge — the menu could be opened only once. (d) While a modal is open the game pauses (`timeScale 0`), which froze scaled `HeldSeconds` and made **every** release classify as a TAP — the tap/hold discriminator was dead while paused.
- **Established by:** `9c410ef`, `0e0f38e`
- **Breaks if:** any one of the four is simplified away.
- **Confidence:** high

### Face buttons are read on the CONTROLLER INSTANCE, not on `HasPose`
- **Where:** `NonDominantHold` observation gate
- **Rule:** observe the button whenever the `VRHand` exists. Do **not** gate on `VRHand.HasPose` / `IsTracked`.
- **Why:** a Quest/Virtual Desktop controller **held still** — resting after reading the floated pause menu, or lowered — reports `isTracked = false`, so `HasPose` went false and the tracker **froze**, even though `VRHand` still reads its `PrimaryButton` from the valid device every frame (the tracked gate skips only the pose). No `ShortTap` edge ever fired: the reported OPEN → CLOSE → nothing. Two prior fixes touched the latch and the self-heal; the button was never **observed** in the first place. A truly gone device is `ClearInput`'d to a clean "up", so it reads as no press, never a phantom.
- **Established by:** `2d509f0` fix(worldui): pause-menu X-tap reopens reliably (observe button without positional pose)
- **Breaks if:** a tracking gate is reintroduced "to avoid phantom input".
- **Confidence:** high

### One physical press serves exactly one intent — the press-identity gate
- **Where:** `NonDominantHold` monotonic `PressId` per physical down-edge; `OptionsToggle` `_spentPressId`
- **Rule:** the press that coincided with an external open/close is marked **spent** and its release is ignored.
- **Why:** the earlier `_armed`/`ButtonIsUp` release latch **re-armed on the very release frame that fired the tap**, so it could not distinguish the press that CLOSED the menu (via the game's own gamepad-escape, logged only as "closed externally") from the fresh press that must REOPEN it.
- **Established by:** `f20fb85` (bug 1)
- **Confidence:** high

### Controller-X is the SOLE ESC-menu owner; the game's own paths are Harmony-suppressed
- **Where:** `Patches/EscMenuInputBlock` (`ShowUIWindowSuppressor` no-ops `ESCMenu.ShowUIWindow`; `EscMenuEscapeSuppressor` no-ops `UIWindow.Escape` **scoped to `UIWindowID.ESCMenu` only**), self-registered from `InputModeGuard.Tick`, active only while `VRSession.IsRunning`; `OptionsToggle` one-shot same-press-keyed belt reconcile
- **Rule:** suppress both game paths, scope the Escape suppressor to the ESC menu (**sub-windows escape normally**), reflection-guard the patches, and degrade to a no-op with one warning if a target cannot be resolved.
- **Why:** the game reacted to the **same physical X press** on the press-**down** edge (`ESCMenu.ShowUIWindow` on `UI_PAUSE`, and `UIWindow.Escape` Toggle on `UI_CANCEL` in the mod-forced mouse mode) while the mod acts on the **release** edge — one press flipped the state twice and landed reopened.
- **Established by:** `a7e692b` fix(worldui): make controller-X the sole ESC-menu owner + belt reconcile
- **Breaks if:** the Escape suppressor is broadened to all windows (sub-menu X buttons die) or the reconcile is made continuous rather than one-shot.
- **Confidence:** high

### The X toggle decision is computed from LIVE game windows each tap
- **Where:** `OptionsToggle` (`actuallyOpen = ESCMenu.IsOpen || Options open || MP submenu open || compendium open`); compendium read via a **side-effect-free scene scan** run only on a tap
- **Rule:** never a cached `_open` bool.
- **Why:** a stray external Show/Hide desyncs a cached bool. And `SpecialUIProvider.CompendiumUIObject` **instantiates the prefab on access**, so it must never be touched on a per-frame path.
- **Established by:** `e7b2fab` fix(worldui): make X a robust pause-menu toggle that closes all sub-menus
- **Breaks if:** the compendium probe is moved into `Tick`.
- **Confidence:** high

### `ESCMenu` is cached, and recovered by an inactive-inclusive scene scan
- **Where:** `OptionsToggle` cached reference + scene scan including inactive objects; re-activate before `Show`
- **Rule:** cache the live (deactivated-but-not-destroyed) reference; on a tap, scan the scene **including inactive objects**; re-acquire only when the cache is Unity-dead.
- **Why:** after `menu.Hide()` the ESC menu GameObject deactivates and `Singleton<ESCMenu>.IsInitialized` flips **false**, so `menu == null` and no X could reopen it until a scenario reload. The Singleton clears its ref only in `OnDestroy`, so an alive-but-lost menu is recoverable — and the scan finding *nothing* is the unambiguous signal that the game genuinely destroyed it.
- **Established by:** `ea4f75c`, `f73cc1a`
- **Confidence:** high

### `TakeDamagePanelSafety` is three independent nets, all VR-gated
- **Where:** `Patches/TakeDamagePanelSafety` — (1) liveness guard on every widget-reachable entry point (run only while `actorBeingAttacked != null`, swallow stale events with a change-deduped log), (2) hover stand-down while `DecisionDockSurface.DockingTakeDamage`, (3) `get_IsLethalDamage` returns false instead of throwing when the actor is null
- **Rule:** all three; gated on VR conversion being active so vanilla is byte-identical when the mod is off. The panel's own state is **never** touched.
- **Why:** choosing "burn two discarded cards" hard-locked the game with the generic error dialog and bailed to the main menu. The panel closed through the game's **own** take-damage path and `ResetAndHide` nulled `actorBeingAttacked`; **after** that, (a) the reliable-click queue delivered a second Receive-Damage press a frame late → `TakeDamage()` → NRE at `actorBeingAttacked.Inventory`, and (b) the VR laser jittering across the docked toggle row fired the game's `OnMouseEnter*/OnMouseExit*` dozens of times a second → `ShowDamageTooltip` → `get_IsLethalDamage` deref → NRE. Each NRE was caught by the panel's own try/catch, which popped the error dialog. The same hover thrash, while the panel was *live*, flip-flopped the LoseCard preview fan (hand↔discard) so the burn pick never settled — the visible deadlock.
- **Established by:** `4662eab` fix(worldui): guard TakeDamagePanel against stale/hover events (burn-two deadlock)
- **Breaks if:** reduced to "just null-check the actor". The hover stand-down and the lethality backstop cover different call paths.
- **Confidence:** high

### `TickGuard` isolates every driver sub-tick
- **Where:** `Core.TickGuard.Run`; `WorldUIModule` wraps every Update/LateUpdate tick with cached delegates
- **Rule:** each sub-tick is isolated; the first throw per name is logged at Error **with stack**, repeats throttled to ~1/10 s on `Time.unscaledTime`; never rethrows; **no per-frame allocation** (delegates cached once).
- **Why:** a per-frame `NullReferenceException` flood (12272×, exactly 1/frame) started the instant the ESC menu closed. The game logs exceptions with **no stack trace**, so the flood was anonymous. `WorldUIDriver.Update` ran every tick in ONE unguarded sequence with `OptionsToggle` 6th — a single upstream throw silently **starved** `OptionsToggle.Tick`, so the X tap produced no handling and nothing was logged. An unguarded `Update` starves input on any NRE.
- **Established by:** `f20fb85`, promoted to shared in `16f32ee`
- **Breaks if:** the guard is removed as overhead, or the delegates are allocated per frame.
- **Confidence:** high

### `PanelPlacement.ClampIntoView` is idempotent inside the cone — that is the contract
- **Where:** `PanelPlacement.Spawn`, `PanelPlacement.ClampIntoView` (yaw ±35°, pitch [−30, +20], distance [0.45, 1.4] m × diorama scale), `PanelPlacement.Facing`
- **Rule:** an in-view pose passes through **unchanged**, so the clamp can be applied liberally at placement **events**; only an out-of-view candidate is relocated, and the caller persists the healed pose so it never strands again.
- **Why:** movable panels sometimes spawned far outside the view — the combat log stranded off to the side after a Mixed-Reality toggle re-derived it from a stale persisted offset. Idempotence is what makes it safe to call at every placement event without disturbing deliberate in-view poses. **But see the `SettingsPanel` FOLLOW entry: applying it per frame plus persisting is an infinite write loop.**
- **Established by:** `c42d68e` feat(worldui): shared in-view spawn/re-place clamp; heal the combat log
- **Breaks if:** the clamp is made to always re-centre, or called per frame.
- **Confidence:** high

### The laser grabs ONLY the visible drag bar
- **Where:** `PanelGrabHandle.BarCollider` (trigger, 1.5× pad ≈ 3.6 cm strip, `BarWidthFraction` 0.55) vs the proximity-only palm zone (`ZoneWidthFraction` 0.62, 5 cm); `RayGrabDriver` ray-tests the **bar** collider
- **Rule:** the ray-test target and the proximity target are **different colliders**. Handles that set no bar (tray / combat log / settings) keep the old registered-collider behaviour.
- **Why:** the lost-menu incident: with the ray testing the wide palm zone, every trigger aimed at the cards/board hit the menu's grab zone first and dragged the lost menu instead.
- **Established by:** `a312cb4` (1)
- **Confidence:** high

### One-hand panel carry derives yaw from a swing-twist decomposition
- **Where:** `PanelGrab` one-hand carry yaw
- **Rule:** yaw comes from the swing-twist decomposition of the actual wrist rotation **delta about world up** — pure pitch/roll contributes zero.
- **Why:** deriving yaw from the **horizontal projection of hand forward** meant pitching the controller while raising the arm swung the projected heading wildly — the tray rotated on a purely vertical move.
- **Established by:** `ae1f6e7` (task #4)
- **Confidence:** high

### `PanelGrabHandle` drops stale grip slots
- **Where:** `PanelGrabHandle` slot self-heal; `ProximityGrabber` force-release; `GrabbableBehaviour.OnDisable` clears `Holder`
- **Rule:** drop grip slots whose hand no longer holds the bar.
- **Why:** a **both-slots-stale** latch made `CanGrab` false **forever** — the panel became permanently ungrabbable.
- **Established by:** `bce9a63` (bug A, structural self-heal)
- **Confidence:** high

### `InputModeGuard` keeps `GamePadInUse` false so the right SCENES load
- **Where:** `InputModeGuard` prefixes on `InputManager.SetGamepadInputDevice(bool)` and `AssignGamepadBindingsToPlayerActions(bool)`
- **Rule:** swallow switches **to** gamepad while active; mouse switches always pass. The second patch is belt-and-braces on the only `isUseGamepadInPc = true` writer.
- **Why:** the game loads a **different scene** (`Game_gamepad` vs `Game`) depending on this flag. This is not a cosmetic input preference.
- **Established by:** `f29e0f5`; documented in `docs/PATCH-INVENTORY.md` #16/#17
- **Confidence:** high

---

## 10. Cross-cutting

### Change-deduped logging is a diagnostic contract, not tidiness
- **Where:** throughout — `MODAL SPAWN CLAMP`, `MODAL RECALL`, `MODAL DIAG`, `Host rect fit`, `Flattened N transform(s)`, `MAP CAP probe`, `fan state:`, docked-host world rect, adoption lines
- **Rule:** log **once per change**, at **Info**, with the resolved values.
- **Why:** BepInEx's default disk config **drops `Debug` entirely** — a `Debug` diagnostic never reaches the hardware log and therefore diagnoses nothing (test #19 had zero "Ray-uGUI canvas" lines while laser clicks demonstrably ran). Conversely, a per-frame Info line drowns the log. These lines are the *only* instrument on this project; they are cited by name in commit messages and by the user in bug reports. Charter §5 lists log grep tokens as a protected surface.
- **Established by:** `bf65821`, `670bf4d`, `d1d6d55`, and many others
- **Breaks if:** a logging cleanup demotes these to `Debug` or removes the dedup.
- **Confidence:** high

### Every game-object mutation is recorded and reversed
- **Where:** `ConvertedPanel.Original*` fields, `LayerRecord`, `NestedCanvasRecord`, `FlattenRecord`, `HiddenBackgrounds`, `AddedScrollMasks` vs `EnabledScrollMasks`, material snapshot/restore in `ActorBars` / `NativeButtonSkin` / map override
- **Rule:** record before mutating; restore on release, scene change, VR-off and hot reload. Distinguish **things we added** (destroy) from **things we changed** (restore).
- **Why:** charter working rule; also the multiplayer-safety and hot-reload contracts. The added-vs-changed distinction is why several of these lists come in pairs — merging them makes the undo wrong in one direction.
- **Established by:** structural, from `6d6a9a7` onward
- **Confidence:** high

### Config keys are user data
- **Where:** `WorldUIConfig` and the sibling module configs
- **Rule:** renaming a key is a deliberate **migration**; removing a key silently changes a user's persisted behaviour; a new key is the way to sidestep a bad persisted value.
- **Why:** stated three times in the history for three different bugs — `CombatLogFollow` → `CombatLogFollowSeat` (kill a persisted `true`), `[WorldUI] BarsOccluded` in a **new file** ("a new key sidesteps the BepInEx persisted-config trap"), and the `[TransientButtons]`/`[SquareCaps]` one-time migration. Also charter §5.
- **Confidence:** high

### `Master` and `FlatScreen` are loud kill switches
- **Where:** `WorldUIConfig.Master`, `WorldUIConfig.FlatScreen` — state logged loudly at startup and on every change, with the exact cfg fix in the message
- **Rule:** keep the loud logging.
- **Why:** test #11's "menu no longer loads" was most likely an accidentally persisted panel-toggled kill switch, and there was no way to tell from the log.
- **Established by:** `6cb5b95`
- **Confidence:** high

---

## 11. Second pass — additional verified invariants

> Found by a full read of the current source after the commit archaeology. Same
> rules apply; these are grouped by file rather than by theme to keep them short.

### Stereo separation and convergence must scale TOGETHER
- **Where:** `FlatScreenStereo.Tick` (`_sepScene = IPD × DepthStrength × WorldScale × ParallaxScale`, `_convScene = max(0.25, ScreenDistance) × WorldScale × ParallaxScale`), `MinConvergenceMeters` (0.25), `ScreenParallaxScale` clamp 1..60 default 6
- **Rule:** `ScreenParallaxScale` multiplies **both** terms.
- **Why:** the at-infinity disparity depends only on the **sep/conv ratio**, which stays invariant — so comfort at infinity is untouched by construction while scene-*internal* depth is amplified by the factor. Scaling only the separation would push the eyes past the divergence limit. The 1..60 clamp and the default 6 are "the range validated for the menu scenes".
- **Established by:** `b68d4da` feat(worldui): window depth for the flat screen — parallax scale
- **Breaks if:** the two are given separate multipliers, or the clamp is widened.
- **Confidence:** high

### Per-eye framing is a LENS SHIFT, never toe-in
- **Where:** `FlatScreenStereo.SyncCamera` — `proj.m02 -= proj.m00 * (_sepScene / _convScene)`, guarded by `_sepScene > 0 && _convScene > 1e-4`
- **Rule:** off-axis projection shift. The mirror cameras are **not** rotated toward each other.
- **Why:** stated in-code — "toe-in causes vertical parallax", which is a known comfort failure.
- **Established by:** `85c26f3`
- **Confidence:** high

### The video shift blit offsets are the NEGATIVE of the image shift
- **Where:** `FlatScreenStereo.OnPreRenderCamera` — left eye samples at `+shift`, right at `−shift`; Y offset is `margin` only
- **Rule:** the sign convention, and the absence of any Y shift.
- **Why:** "a blit offset moves the SAMPLING window, i.e. the negative of the image shift". Swapping the two inverts depth — the video pops **out** instead of receding. Zero Y offset means no vertical parallax by construction.
- **Established by:** `b7586cf`
- **Confidence:** high

### The map albedo camera sits in a two-sided depth sandwich
- **Where:** `FlatScreenStereo.ReconcileAlbedoCamera` — `cam.depth = mapSource.depth + 0.1f`
- **Rule:** just **above** the game map camera, and still **below** the head camera.
- **Why:** above so Unity composites us last into the base RT (we overwrite its black deferred render); below so the screen quad samples *this* frame's result. Both halves are load-bearing; a single-sided "make it highest" breaks the second.
- **Established by:** `fb3ef2c`, `0094147`
- **Confidence:** high

### `MapUnlitShader` probes every loaded AssetBundle
- **Where:** `FlatScreenStereo.MapUnlitShader` — `Shader.Find` first, then a scan across loaded bundles for `Assets/Bundle/Table/MapUnlit.shader`; same idiom as `Cards.PlayTray.OverlayShader` and `WorldUIAssets.CreateFlatMaterial`
- **Rule:** never rely on `Shader.Find` alone for a bundled shader.
- **Why:** `Shader.Find` does **not** see shaders that live only inside an AssetBundle. `BoardLit` resolves by accident because a bundle *prefab material* references it; `MapUnlit` and `GloomhavenVR/Overlay` are referenced only from C#, so they were never loaded and `Shader.Find` returned null (log: "Overlay shader NOT found"). This cost a whole round on the board HUD before it cost another on the map.
- **Established by:** `cefee9c` fix(cards): actually LOAD the bundled Overlay shader
- **Breaks if:** any new bundled shader is fetched with a bare `Shader.Find`.
- **Confidence:** high

### The base-camera depth tie must not pick the `UICamera`
- **Where:** `FlatScreen.SelectBases`
- **Rule:** lowest depth wins, but on a **tie** a `UICamera`-tagged incumbent is displaced.
- **Why:** depth ties are real in this game (test #5 inventory: `Main Camera` and `UI Camera` both at depth 1.0). With routing off the `UICamera`-tagged camera composites **last** per game intent, so on a tie it must not become the background base — its forced clear would erase every other camera's output.
- **Established by:** `5edc0a8`
- **Confidence:** high

### `WantedQuadWidth` is the single source of truth for placement AND re-place
- **Where:** `FlatScreen.WantedQuadWidth`, consumed by `FlatScreen.PlaceScreen` and by `FlatScreen.FollowHead`'s re-place trigger
- **Rule:** one function, both callers.
- **Why:** stated in-code — a divergence between the placement value and the re-place trigger's expected value would re-place the screen **every frame**.
- **Established by:** `02b0536`
- **Breaks if:** the follow computes its own expected width.
- **Confidence:** high

### Poke press and poke hold use different rect tolerances
- **Where:** `FlatScreen.TickPoke` — press requires `|local| <= 0.5`, a held press uses `0.55`
- **Rule:** the 0.05 slop exists only on the hold side.
- **Why:** a tremor at the quad edge must not drop an active poke. This mirrors the laser path's "clamp instead of drop" rule while pressed.
- **Established by:** `02b0536`
- **Confidence:** high

### A handedness switch returns early, before the pointer runs
- **Where:** `FlatScreen.TickPointer` — handedness switch is evaluated first, and on a switch the method returns
- **Rule:** both the ordering and the early return.
- **Why:** the switch "may change which hand is primary below", and the return keeps the newly dominant hand's fresh `TriggerDown` edge from firing an accidental click on the frame of the switch.
- **Established by:** `02b0536`
- **Confidence:** high

### `EndScreenDrag` runs before `DirectClick`, and a drag is never a click
- **Where:** `FlatScreen.TickPointer` release path; `FlatScreen.EndScreenDrag`
- **Rule:** end the drag first; `EndScreenDrag` fires `pointerUp` then `endDrag` and deliberately fires **no** `pointerClick`.
- **Why:** a drag already cleared `_latched`, so `DirectClick` cannot double-fire — the two paths are mutually exclusive by construction (tap → `DirectClick`; drag → `EndScreenDrag`). Firing a click at the end of a drag would commit the slider twice.
- **Established by:** `ddb4294`
- **Confidence:** high

### `CanvasSweepNextFrame` is advanced INSIDE the schedule check, not before the early-out
- **Where:** `CanvasConversion.AdoptNestedCanvases`
- **Rule:** the schedule advance is guarded by `if (Time.frameCount >= panel.CanvasSweepNextFrame)`.
- **Why:** modal hosts run this **every** frame; advancing unconditionally would push the deadline forever forward and the other schedule-driven consumer — the `ApplyModLayer` re-sweep for pooled children — would never fire again. This looks like a redundant guard and is a live coupling between two sweeps.
- **Established by:** `33177db`
- **Confidence:** high

### The dropdown Blocker is found by NAME under the host, on purpose
- **Where:** `CanvasConversion` Blocker scan — direct children of `HostRect`, matched on `name == "Blocker"`
- **Rule:** name-gated, host-scoped, separate from the subtree scan.
- **Why:** the game parents the Blocker under the **root/host** canvas, i.e. outside the target subtree the `GetComponentsInChildren` loop covers. The name gate exists specifically so mod-owned sibling canvases — `ModalCloseButton`'s X at order 1100 — are never swept up by it.
- **Established by:** `33177db`
- **Breaks if:** generalised to "adopt every child canvas of the host".
- **Confidence:** high

### The fit CLAMPS to the target frame; the depth mask deliberately does NOT
- **Where:** `CanvasConversion.TryMeasureContent` (clamped) vs `CanvasConversion.CollectVisibleMaskRects` (unclamped) — same measurement helper, opposite decision
- **Rule:** opposite clamp policy on purpose.
- **Why:** an open dropdown list may extend past the target frame and the mask must back it wherever it draws; the fit must not grow the host to cover a transient overlay.
- **Established by:** `20a1860`, `33177db`
- **Confidence:** high

### Two different definitions of "visible alpha" coexist
- **Where:** `CanvasConversion.TryGetVisibleHostRect` (multiplies by `CanvasRenderer.GetInheritedAlpha`) vs `CanvasConversion.HideFullScreenBackground` (uses `Graphic.color.a` **alone**)
- **Rule:** measurement uses inherited alpha; background detection uses intrinsic alpha.
- **Why:** for measurement, a faded-out graphic genuinely contributes nothing. For background detection the opposite is true — an intrinsically opaque full-cover image **is** the backing even at inherited alpha 0, and multiplying by the fade made it read as transparent for the whole fade-in, which is exactly the untreated-backing flicker. Together with the two alpha *floors* (0.05 / 0.15) this is four numbers pinned to three separate hardware bugs.
- **Established by:** `dd20a8e`, `f903bbe`
- **Breaks if:** a single shared "is this visible" helper is extracted.
- **Confidence:** high

### `ClipperMemo` is cleared per measurement PASS; the scratch buffers assume single-threaded, non-re-entrant ticks
- **Where:** `CanvasConversion.ClipperMemo` (cleared at the top of both `TryMeasureContent` and `CollectVisibleMaskRects`); `CanvasScratch`, `ScrollScratch`, `TransformScratch`, `BgGraphicScratch`, `BgCornerScratch`, `RectScratch`, `GraphicScratch`, `CornerScratch`
- **Rule:** the memo is per-pass, keyed by a graphic's **immediate parent** (siblings share one ancestor walk). Every scratch list is static and `Clear()`ed on entry and exit; `CollectVisibleMaskRects` deliberately reuses the fit's buffers.
- **Why:** stale memo entries across passes give wrong clip rects after a layout change. The shared statics are justified in-code as "single-threaded, never re-entered" — **any coroutine or re-entrancy a refactor introduces silently corrupts both the fit and the mask.**
- **Established by:** `20a1860`
- **Confidence:** high

### `FirstCapHeights` is a process-lifetime dictionary that is never cleared
- **Where:** `CanvasConversion.FirstCapHeights`, written only via `if (!ContainsKey)`
- **Rule:** never cleared, never overwritten.
- **Why:** it stores the **cold** first-open height per window, which is the whole point of the fallback height cap. Clearing it on scene change would let a warm capture become the new "cold" reference and the pause menu would grow again.
- **Established by:** `92fe985`
- **Confidence:** high

### `Prompt.IsOpen` and `Prompt.IsActive` are deliberately different
- **Where:** `ModalFallback.DecisionDock` prompt entries
- **Rule:** `IsOpen` gates the **claim** (float + ModalUI stand-down) and stays true while the game alpha-hides the panel; `IsActive` additionally requires visible + topmost and gates what the surface **docks**.
- **Why:** during the LoseCard pick and the burn confirm, `TakeDamagePanel.ToggleVisibility` drives an inner `CanvasGroup`, not the window. Collapsing the two either breaks the card fan (the claim drops, `ModalUI` returns — the test #21 regression) or docks a hidden row over an open dialog.
- **Established by:** `c6dbaa8`
- **Breaks if:** one predicate is used for both.
- **Confidence:** high

### The `Prompts` array ORDER decides which prompt wins
- **Where:** `ModalFallback.DecisionDock.Prompts`
- **Rule:** `DialogPopup` is **first**.
- **Why:** it is the follow-up confirm layered **on top of** the take-damage panel, so when both windows are open it is the active prompt. Reordering the array silently docks the wrong one.
- **Established by:** `c6dbaa8`
- **Confidence:** high

### The claim is INSTANCE-based, and `TakeDamagePanel` stays in `FallbackIds`
- **Where:** `ModalFallback.DecisionDock.ClaimsWindow`, `ModalFallback.FallbackIds`
- **Rule:** claims are matched by window instance, not by ID; and `TakeDamagePanel` remains listed as a fallback ID even though it is normally claimed.
- **Why:** instance matching is what lets one mechanism cover both the ID-tracked `TakeDamagePanel` **and** the ID-less poll-tracked `dialogPopup`. Keeping the ID listed is what lets the generic float take over the moment the claim breaks — the no-deadlock guarantee.
- **Established by:** `c6dbaa8`
- **Confidence:** high

### `BlockingWindowModalActive` is a separate property from `WindowModalActive`
- **Where:** `ModalFallback.BlockingWindowModalActive`, consumed by `RayInteractor.UpdateModalPickBlock`
- **Rule:** the ray's physics-pick block keys on the **blocking** property.
- **Why:** keying on `WindowModalActive` (any floated window) suppressed every board/card/tray pick behind a floating pause menu — the reported "cards can't be grabbed while the menu is open".
- **Established by:** `8794527` (item 4)
- **Confidence:** high

### `SyncEscMenuTabHighlights` drives the highlight Image ONLY
- **Where:** `ModalFallback.SyncEscMenuTabHighlights` / `SyncTab`, `_forcedTabs`
- **Rule:** never write `toggle.isOn` or the toggle-group state. Re-assert every tick and call `tab.CancelHighlightAnimations()`. On hand-back, fade only if `!tab.IsSelected`.
- **Why:** `ToggleGroup.allowSwitchOff` permits **zero**-on, never multiple-on; driving `isOn = true` runs the setter → `NotifyToggleOn` → turns the real active toggle **off** → hides that window. That is the parallel-windows bug all over again. `CancelHighlightAnimations` is needed because the game's LeanTween unhighlight fade would otherwise win.
- **Established by:** `ca6b22e` (#9)
- **Confidence:** high

### `ModalFallback.Tick` step 5b needs BOTH latches
- **Where:** `WindowPanel.ScaleReDerived` **and** `WindowPanel.OneShotFitted`
- **Rule:** the scale re-derive requires both.
- **Why:** `ConvertedPanel.FitOneShotApplied` is written by the one-shot path **and** by the ordinary per-frame fit, so it is ambiguous on its own. `OneShotFitted` is the disambiguator that excludes non-one-shot modals. Using `FitOneShotApplied` alone re-scales panels that were never one-shot fitted.
- **Established by:** `fb8c771`
- **Confidence:** high

### `ModalFallback.OnWindow` tracks fallback windows OUTSIDE a scenario too
- **Where:** `ModalFallback.OnWindow`, `ModalFallback.Tick` prune
- **Rule:** tracking is unconditional; the **scenario gate is applied level-triggered in `Tick`**. The Open set is pruned by `IsOpen`/death, never blanket-cleared.
- **Why:** the scenario-start story/intro windows open during **loading**, before the mode machine's scenario signal settles. An edge-triggered event gated on `ScenarioBoardExists` lost them forever and the screen never rose (test #10: MODAL FALLBACK fired zero times all session).
- **Established by:** `b5c87a7`
- **Breaks if:** the scenario gate is moved back onto the tracking.
- **Confidence:** high

### `ObjectivesSurface` must re-assert `horizontalFit = Unconstrained` every tick
- **Where:** `ObjectivesSurface.ApplyContentWidth` (`_widthFitter`)
- **Rule:** re-assert every tick, not once.
- **Why:** a `ContentSizeFitter` **drives** `SizeDeltaX`. One game-side rebuild with it re-enabled silently stomps the width we wrote and the dial looks dead again. Vertical fitting is deliberately left alone.
- **Established by:** `64bb628`
- **Confidence:** high

### `ObjectivesSurface.RestoreContentWidth` is VALUE-GUARDED
- **Where:** `ObjectivesSurface.RestoreContentWidth` — writes back only while `|live − _widthForcedX| <= 0.5`; `_widthForcedX` seeded `float.NaN`
- **Rule:** restore only if the live value is still ours.
- **Why:** `CanvasConversion.Release` has **already** restored the pristine 2D `OriginalSizeDelta` by the time this runs. Writing the captured placeholder over it would leave the game's own HUD container **100 px wide for the rest of the session**. The `NaN` seed makes "nothing written yet" fail the guard automatically.
- **Established by:** `64bb628`
- **Breaks if:** the guard is removed as paranoia. It is the difference between reversible and destructive.
- **Confidence:** high

### `EnemyRevealSurface` must capture the scrollbars BEFORE `Convert`
- **Where:** `EnemyRevealSurface.HideBoardCoupledScrollbars`, called before `CanvasConversion.Convert`; undone on convert failure, on prune, on release and in `Shutdown`
- **Rule:** capture first, and undo on **every** exit path including a failed convert.
- **Why:** after `Convert` reparents the holder, walking its parent chain climbs **our host**, not the original window — the owning `ScrollRect` becomes unfindable. The same rule as `ObjectivesSurface` above, stated the other way round: the framework's `Release` runs first, so surface-level state must be pre-captured or conditionally restored.
- **Established by:** `043fe8d`, `b9386ea`
- **Confidence:** high

### Restore-vs-`base.Shutdown()` order is deliberately OPPOSITE in different surfaces
- **Where:** `InitiativeTrackSurface.Shutdown` and `ObjectivesSurface.Shutdown` restore **before** `base.Shutdown()`; `DecisionDockSurface.Shutdown` restores **after**
- **Rule:** both orders are correct, for different reasons.
- **Why:** the first two must restore while the game's rows/levers are still parented and alive. The dock must let `CanvasConversion.Release` return the row to its 2D home **before** the suppression is lifted, or the restore paints a window that is still ours. A refactor that "standardises" the order breaks one of them.
- **Established by:** `991344c`, `64bb628`, `c6dbaa8`
- **Confidence:** high

### Host raycaster polarity is per-surface and re-asserted every tick — in BOTH directions
- **Where:** force **ON**: `DecisionDockSurface`, `DialogSurface`. Force **OFF**: `StatPanelSurface` (both watches + the snapshot copy), `PropInfoSurface`, `EnemyRevealSurface`, and conditionally `TrayControlDockSurface.Place`
- **Rule:** each surface re-asserts its own polarity every tick.
- **Why:** `CanvasConversion`'s UI-lock mirror re-enables or disables **all** host raycasters wholesale, so a one-shot write is always eventually stomped. The two directions exist for opposite reasons: the dock/dialog are the surfaces that must accept input *while modal*; the passive surfaces must stay invisible to `IsPointerOverUI` or they re-open the self-occlusion and hard-lock loops.
- **Established by:** `c0c6364`, `66d4b7e`, `c6dbaa8`, `1c4fcd8`
- **Breaks if:** centralised into one policy.
- **Confidence:** high

### `FlatScreen.ManualScreenActive` is a universal stand-down
- **Where:** consumed by `DecisionDockSurface.WantConverted`, `DamageTooltipSurface`, `DamagePreviewSurface`, `TrayControlDockSurface.FeatureEnabled`, and `ModalFallback` (releases floated conversions)
- **Rule:** every surface that docks or floats game widgets stands down while the manual rescue screen is up.
- **Why:** the manual screen mirrors the game's **2D composite**. A widget re-parented onto a world-space host is missing from that composite, so the universal rescue must put the windows back into the 2D UI first — otherwise the rescue itself hides the thing you invoked it for.
- **Established by:** `03228e7`, `1c4fcd8`
- **Confidence:** high

### `ButtonCluster.ClickUiButton` needs an EXPLICIT lock gate
- **Where:** `ButtonCluster.ClickUiButton` — `Selectable.IsInteractable()` precheck + `CanvasConversion.IsLockedNow` gate + `ExecuteEvents.pointerClickHandler`
- **Rule:** the explicit lock check is mandatory, and the click must go through `ExecuteEvents` on the real button.
- **Why:** `ExecuteEvents` **bypasses GraphicRaycasters**, so the game's raycaster-based UI lock would not stop it. And calling `OnClickInternal` directly would skip `InteractabilityManager`, the warning mask, `interactable`, and the multiplayer synchroniser — **deliberately not done**.
- **Established by:** `f29e0f5`
- **Breaks if:** the lock gate is dropped "because the raycaster handles it".
- **Confidence:** high

### `StyleEngravedLabel` is deliberately NOT called from `ApplyFont`
- **Where:** `NativeButtonSkin.ApplyFont` vs `NativeButtonSkin.StyleEngravedLabel`
- **Rule:** two separate calls; only keycap labels get the engraved treatment.
- **Why:** captions, HUD lines and the quest block share `ApplyFont` and **must stay un-outlined**. Folding the two would outline every mod-drawn string in the game.
- **Established by:** `9d0e9e3`
- **Confidence:** high

### `PanelGrabHandle.MinScale` / `MaxScale` are `internal`, as one source of truth
- **Where:** `PanelGrabHandle.MinScale` (0.15) / `MaxScale` (2), reused by `GrabbableModal.Tick` and `SettingsPanel`
- **Rule:** the owners clamp their own per-frame factor to the **same** range.
- **Why:** stated in-code — clamping to a higher per-panel minimum would **silently re-cap what the two-hand pinch just shrank**, so the user's pinch would appear to do nothing at the bottom of its range. The `internal` visibility exists purely to make the sharing possible.
- **Established by:** `1cc7f81` (item 4)
- **Confidence:** high

### Laser carry translates only, then RETURNS
- **Where:** `PanelGrabHandle.Update` laser branch — position lerp, then `return` before any rotation write
- **Rule:** the early return is the mechanism.
- **Why:** the owner keeps authoring the window's rotation; `GrabbableModal.GrabCarriesYaw` would otherwise be a **second rotation writer** on the same transform and the panel would jitter. This is the same two-writer rule that `IPanelGrabOwner.GrabCarriesYaw` exists to express.
- **Established by:** `4a2268b`
- **Confidence:** high

### One-hand yaw uses swing-twist; two-hand yaw uses heading — deliberately different
- **Where:** `PanelGrabHandle.TwistYawDegrees` (one hand) vs `Mathf.DeltaAngle(_anchorHeading, HeadingDegrees(pB - pA))` (two hands)
- **Rule:** the two carry modes use different yaw sources.
- **Why:** one-hand yaw derived from the horizontal projection of hand-forward blew up when the arm was raised (as forward approaches vertical its horizontal projection shrinks, so wrist noise swings the heading by tens of degrees) — a purely vertical carry spun the board. A **two-hand span** has no such pathology, so the simpler heading method is kept there.
- **Established by:** `ae1f6e7` (task #4)
- **Breaks if:** unified "for consistency".
- **Confidence:** high

### `PanelGrabHandle` heals stale grip slots every frame
- **Where:** `PanelGrabHandle.Update` (`!ReferenceEquals(_handX.Grabber.Held, this)` for both slots), plus a separate `HasPose` drop that runs **first**; `ProximityGrabber` force-release; `GrabbableBehaviour.OnDisable` clears `Holder`
- **Rule:** both checks, in that order; the slot-A heal also fires `OnGrabFinished()` when it empties both slots.
- **Why:** the grabber sets `Held` **before** `OnGrab`, so during any legitimate hold the check is always true and the heal is free. A missed release latches a stale slot — and with **both** slots stale `CanGrab` is false **forever**, so the bar refuses every grab. The `OnGrabFinished` call matters because persistence must still happen on a healed release.
- **Established by:** `bce9a63`
- **Confidence:** high

### `GrabbableModal` keeps the bar cube's primitive collider as a LASER-ONLY target
- **Where:** `GrabbableModal.EnsureFrame` — the bar's `BoxCollider` is kept (not destroyed), handed to `PanelGrabHandle.SetBarCollider`, and is **not** registered with `VRInteractables`; `BarColliderPad` (1.5, ≈ 3.6 cm strip)
- **Rule:** two colliders with two different jobs; only the narrow one is ray-tested.
- **Why:** it is a trigger on the mod render layer so the physics ray ignores it, and leaving it unregistered keeps the palm grab on the generous frame zone. 1.5 is "just enough slack to point at the 2.4 cm visible bar comfortably, without re-growing the swallow-everything zone the incident showed".
- **Established by:** `a312cb4`
- **Confidence:** high

### `DepthMaskQuadPaddingPx` is 3, deliberately down from 12
- **Where:** `GrabbableModal.DepthMaskQuadPaddingPx` (3), `DepthMaskMaxQuads` (256)
- **Rule:** tight padding; overflow past the cap merges into the **last** quad.
- **Why:** the old single-union mask used 12 px around the whole union. Per-graphic quads must stay tight or **adjacent-row pads merge and the gap is masked again** — re-creating the exact bug the per-graphic mesh was built to fix. 3 px only bridges antialiased edges. The overflow merge keeps coverage and degrades only gap fidelity.
- **Established by:** `20a1860`
- **Confidence:** high

### `EscMenuInputBlock` sets `_registered = true` BEFORE the try
- **Where:** `EscMenuInputBlock.EnsureRegistered`; `EscMenuInputBlock.Degrade` (`_degraded` one-shot); both `TargetMethod` resolvers return `null` on failure
- **Rule:** the latch is set before the work, but **after** the `harmony == null` early return. A `null` `TargetMethod` makes Harmony patch nothing.
- **Why:** a throw must not retry-spam every frame — but a not-yet-ready Harmony must still retry next frame, hence the ordering of the two early exits. `null` `TargetMethod` + one warning is the standard graceful-degradation shape in this repo (`WallFadeDisable`, `InitialInputSkip`) for when a game update renames a method.
- **Established by:** `a7e692b`
- **Confidence:** high

### `EscMenuEscapeSuppressor` sets `__result = false` before returning `false`
- **Where:** `EscMenuInputBlock.EscMenuEscapeSuppressor.Prefix`
- **Rule:** write the result, then skip the original.
- **Why:** `false` means "not handled / did not toggle" so `UIWindowManager` moves on. Skipping the original **without** setting `__result` leaves uninitialised-semantics for the caller.
- **Established by:** `a7e692b`
- **Confidence:** high

### `TakeDamagePanelSafety.AllowHover` returns false WITHOUT logging
- **Where:** `TakeDamagePanelSafety.AllowHover` — checks `DecisionDockSurface.DockingTakeDamage` first, returns `false` without touching `_lastSwallow`; `TakeDamagePanelSafety.Allow` resets `_lastSwallow = null` on the allowed path
- **Rule:** docked hover events are silently dropped; only *stale* events are logged, change-deduped by method name.
- **Why:** while docked, hover thrash is **expected noise**, not an anomaly — logging it would flood. Resetting the dedup on the allowed path is what makes a *new* burst re-log.
- **Established by:** `4662eab`
- **Confidence:** high

### `AvatarMirror` clamps its fan to exactly the wire's clamp
- **Where:** `AvatarMirror.MaxMirrorFanCards` (12) = `RemoteHandFan`'s broadcast clamp; `MaxCardSlabs = MaxMirrorFanCards + 2`
- **Rule:** the same number, deliberately.
- **Why:** so the glass and a peer's view can never disagree about how many cards are in the hand. `+2` is exactly one grip-held card per hand; the item fan claims no slabs.
- **Established by:** `c7bff30`
- **Confidence:** high

### The ITEM fan is deliberately NOT mirrored
- **Where:** `AvatarMirror.UpdateMirroredCards` — no item-fan state is read at all
- **Rule:** a NON-action, twice-negotiated with the user: one round removed both fans, the follow-up asked only the **ability** fan back.
- **Why:** peers still see it via `RemoteItemFan`, so nothing is lost on the wire. Implemented as "no proxy built and no item-fan state read" rather than a runtime toggle, so there is nothing to accidentally re-enable.
- **Established by:** `c2114a6`, `c7bff30`
- **Confidence:** high

### `AvatarMirror.BuildHands` releases the ghosts BEFORE destroying the old hands
- **Where:** `AvatarMirror.BuildHands`, `AvatarMirror.Teardown` — `_leftGhost.Release()` / `_rightGhost.Release()` first
- **Rule:** release, then destroy.
- **Why:** a style switch under an open fan would otherwise leak **one material per renderer, every switch**.
- **Established by:** `314ba8f`, `9461a69`
- **Confidence:** high

### `SoftCueArt.FrameSprite(0)` must reproduce the initiative sprite BIT-FOR-BIT
- **Where:** `SoftCueArt.FrameSprite` — `const float c = (Size - 1) * 0.5f`, `d = r - (outside + inside)`
- **Rule:** the integer-edge convention is preserved so that radius 0 degenerates **exactly** to the original `min(x, Size-1-x, y, Size-1-y)` field.
- **Why:** stated in-code — the constant exists "so radius 0 reproduces the initiative sprite exactly rather than half-a-pixel off it". This is a deliberate *refactor-compatibility* constraint left by an earlier extraction: the initiative cue is already approved on hardware and must not shift.
- **Established by:** `4569ba9`
- **Breaks if:** the field is rewritten with a cleaner `Size * 0.5f` centre.
- **Confidence:** high

### `SoftFramePulse` runs on `Time.unscaledTime` so several cues breathe IN PHASE
- **Where:** `SoftFramePulse.Tick`, `PulseHz` = 1/1.5 (the initiative ring's period)
- **Rule:** one global clock, one period shared with the initiative ring.
- **Why:** several usable item cards must read as **one cue**, not competing flickers — a per-instance clock would desynchronise them. Unscaled so the breath continues while the game pauses simulation time. `OnEnable` calls `Tick()` immediately so a frame switched on mid-breath never flashes at a stale alpha.
- **Established by:** `4569ba9`
- **Confidence:** high

### `SoftFramePulse` self-drives because the item chip's tick MUST stay change-gated
- **Where:** `SoftFramePulse` (a self-ticking component) vs `ItemsPile.ItemChip`'s maintenance tick
- **Rule:** the pulse must not be folded into the chip's tick.
- **Why:** stated in-code — the chip's maintenance tick is change-gated down to a bool compare "and must stay that way". Pushing a per-frame alpha through it would make every item chip do per-frame work.
- **Established by:** `4569ba9`
- **Confidence:** medium

### `ButtonCluster.SetVisible` has an unexplained asymmetry — FLAG, do not "fix"
- **Where:** `ButtonCluster.SetVisible` — `if (_visible == visible) { if (!visible) return; }`, i.e. early-returns only on a repeat **hide** and falls through on a repeat **show**
- **Rule:** unknown. This is the one asymmetry in the button files with **no stated why**.
- **Why (inferred):** most likely a deliberate re-assertion of `SetActive(true)` after external teardown/reactivation; the `_root.activeSelf` check makes the fall-through cheap.
- **Recommendation:** leave it, add a comment only after the user confirms. It is exactly the shape of a fix for a "the cluster vanished after a board rebuild" report.
- **Confidence:** low (on the reason) / high (that it should not be normalised)

---

## Suspected vestigial

> **Nothing here is deleted or changed by this document.** These are candidates
> for the Phase-2 dead-code census, each with the reasoning that makes it a
> *candidate* — and, where applicable, the reason it might still be load-bearing.

### The 3 unreachable-code warnings — IDENTIFIED
- **Where:** exactly three `if (<const false>)` **statement** sites in `FlatScreenStereo`:
  1. `FlatScreenStereo.BuildOverrideMaterials` — `if (MapUvDebug)` → the `tex = UvDebugTexture(); prop = "UV-DEBUG";` body
  2. `FlatScreenStereo.ApplyWorldMapOverride` — `if (MapDiagClearOnly)` → the early `return;` ("leave the game's deferred material on → nothing draws → magenta clear only")
  3. `FlatScreenStereo.DrawMapIcons` — `if (MapIconsSolidTest)` → the `mpb.SetTexture(IconMainTex, whiteTexture); mpb.SetColor(IconColor, magenta);` branch
- **Observation:** the sibling constants `MapDiagClearColor` and `MapDiagPerSubmeshChannel` are the *same family with the same intent* but produce **no** warning, because they are consumed inside ternaries rather than `if` statements. That asymmetry is an accident of expression form, not of design.
- **Why they exist:** each is a compile-time kill switch for a bisection experiment from the map hunt (UV read-out texture; clear-only pipeline proof; solid-magenta icon geometry proof), documented in-code as "Set to false to restore…" / "Set to true to…". The map bug took ~30 commits and each of these flags is what disambiguated one hypothesis.
- **Recommendation:** **not Tier 0.** These are a deliberate, documented diagnostic surface, and the map is the least-settled area in the subsystem. If the warnings must go, convert *all five* to one uniform mechanism (a `[Conditional]`-style flag or a live config entry) so the ability to re-arm is preserved — do not delete the branches. Note separately that the `true`-valued `MapDiagTopDown` / `MapMatchGameFraming` / `MapDriveGameCamera` are **not** diagnostics: they select shipping behaviour and their dead `else` branches are the previous disproven implementations. Deleting such an `else` is defensible; **flipping the flag is Tier 3.**
- **Confidence:** high

### `FlatScreenStereo.ReconcileMapDeferred` and the whole `MapCaptureMode` strategy — UNREACHABLE
- **Where:** `FlatScreenStereo.ReconcileMapDeferred`, `FlatScreenStereo.MapCaptureMode`, `MapStripBeautify` / `MapStripVolumetricFog` / `MapStripSSAO` / `MapStripPostProcess` / `MapStripAllImageEffects`
- **Observation:** `FlatScreenStereo.EndStackSync` unconditionally calls `RestoreStrippedEffects()` then `ReconcileAlbedoCamera(mapSource)`. `ReconcileMapDeferred` has **no caller**. So mode 1 ("passive-deferred") is configured, documented, logged and **inert**, while its config description still advertises it as the default. Two further inconsistencies: `MapCaptureMode`'s property fallback is `?? 2` while the bound config default is `1`, and mode 2 ("texture blit") has no implementation at all.
- **Recommendation:** the *method* is a Tier 0 candidate. The **config entries are user data** (charter §5) and their descriptions are actively misleading — the correct fix is to mark them deprecated/no-op like `[Comfort] SeatedMode`, not to delete them. `RestoreStrippedEffects` is still called and must stay.
- **Confidence:** high

### `FlatScreenStereo.BoostAmbientForMapRender` / `EnsureMapLight` — UNREACHABLE
- **Where:** `FlatScreenStereo.BoostAmbientForMapRender`, `FlatScreenStereo.EnsureMapLight`, `MapAlbedoAmbient` (default 4), `MapAlbedoLight`
- **Observation:** `BoostAmbientForMapRender` has no caller, yet its counterpart `RestoreAmbientAfterMapRender` **is** called from `ReleaseAlbedo`. `EnsureMapLight` is reachable only from the dead booster. The ambient boost was needed when the map was rendered *lit*; `MapUnlit` made it moot.
- **Recommendation:** Tier 0 for the methods. The restore call is harmless but should go with them, as a pair, in one commit. Config entries: deprecate, do not delete.
- **Confidence:** high

### The mesh-UV-rebuild strategy — UNREACHABLE
- **Where:** `FlatScreenStereo.EnsureCorrectedMesh`, `ReleaseCorrectedMesh`, `BuildUv0`, `ReadRealUvAuto`, `ReadRealUv`, `BuildPositionalUv`, `ApplyOrientation`, `s_uvConfigRevision`, plus `MapUvSource` / `MapUvSwapUV` / `MapUvFlipU` / `MapUvFlipV` / `MapUvChannel` / `MapUvComponent`
- **Observation:** none are called. Superseded by `GloomhavenVR/MapUnlit` sampling `TexCoord0` on the GPU. Note the live code **hard-codes** `MapUnlitUvChannel = 0` and explicitly says so — "not the persisted `MapUvChannel` config, which may hold a stale value" — i.e. the config entry is not merely unread, it is deliberately *distrusted*.
- **Recommendation:** the seven methods are the single largest Tier 0 block in this subsystem. The six config entries must be deprecated-in-place, and their descriptions corrected, because a user who edits `MapUvFlipU` today gets silence.
- **Confidence:** high

### `FlatScreen._backdropMenuQueue`
- **Where:** `FlatScreen._backdropMenuQueue`
- **Observation:** assigned in `TickBackdropDepth`, never read. `_screenMaterialQueueDefault` (the captured original) *is* read and is the load-bearing one.
- **Recommendation:** Tier 0, trivially.
- **Confidence:** high

### `SettingsPanel.DebugElement.BoardButtons` — already deleted, do not restore
- **Where:** `SettingsPanel.DebugElement`, `SettingsPanel.BoardButtonRowsVisible`
- **Observation:** the `BoardButtons` enum member, its chooser label and its reset case were removed as genuinely unreachable — `CurrentElement()` can only return an element listed in `SubCatElements`. The consequence is that `BoardButtonRowsVisible` now gates on `DebugElement.Generic`, which **reads like a bug and is not**: issue 6 merged the two because they are the same physical Confirm/Undo caps.
- **Recommendation:** leave alone; this entry exists to stop a future reviewer "fixing" the Generic gate.
- **Confidence:** high

### `TrayControlDockSurface._controls` is `Array.Empty<DockedControl>()`
- **Where:** `TrayControlDockSurface._controls`; `ContinueDocked` / `ContinueVisible` / `UndoDocked` / `ShortRestDocked` all hardcoded `false`
- **Observation:** stronger than "off by default" — the dock set is literally empty, and the four `false` constants are what other modules read to decide whether to show their mod-drawn twin. The constructor comment is a per-control registry of *why each one was pulled* (short rest: flicker against the mod disc; Continue/Undo: docked flat on the opaque board, pixels never reached the headset while the game still reported `alpha ≈ 1`, so an alpha check could not distinguish "docked+shown" from "docked+invisible" — invisible-but-pressable).
- **Recommendation:** **do not delete.** Removing the `false` constants silently hides the mod buttons; removing the machinery removes the only implementation of the native-widget dock and a documented config escape hatch. Deleting it is a user decision, i.e. Tier 3.
- **Confidence:** high

### `EnemyRevealSurface` documentation drift (not code)
- **Where:** `EnemyRevealSurface` constants block and `_position` field comments
- **Observation:** the comments still describe "LAZY FOLLOW — HORIZONTAL ONLY", a `_worldYLocked` field that no longer exists, and two contradictory layers on `_position` ("ABSOLUTE WORLD space … there is no follow" immediately followed by "RIG-LOCAL … the follow below"). A diagnostic string still prints `lockedY`. **The code is rig-local with all-axis lazy follow.**
- **Recommendation:** comment-only correction, Tier 1 at most — but do it early, because the stale comments describe *superseded* invariants and a refactorer reading them will draw the wrong conclusion.
- **Confidence:** high

### The `renderOnTop` remnants
- **Where:** `MixedReality.KeepMenusUnclipped` — explicitly noted in `b84817d` as "now a no-op kept for its call sites"
- **Observation:** a documented deliberate no-op after the sky-disable lever was replaced.
- **Recommendation:** removable together with its call sites in one commit; do not remove one side only.
- **Confidence:** high

### `ScreenLeftMirrorFallback` and the pre-private-RT map machinery
- **Where:** `FlatScreenStereo` widened-mask clone / left-eye mirror fallback, `MapCaptureMode`, `MapStrip*`, `MapExposure`, `MapAlbedoAmbient` / `MapAlbedoLight`, `MapUvSource` / `MapUvSwapUV` / `MapUvFlipU` / `MapUvFlipV` / `MapUvChannel` / `MapUvComponent`, `MapSrgbFix` / `MapGammaCorrect`, `MapBackbufferGrab`
- **Observation:** `fb3ef2c` states it removed "all disproven map machinery" (−1072 lines in `FlatScreenStereo`), but several of these are **config entries**, and charter §5 says an unread config key is still a user's persisted setting. Whichever survive are reachable-by-config, not dead.
- **Recommendation:** audit which of these are still *bound*. A bound-but-unread entry is a **documentation** problem (mark deprecated, like `[Comfort] SeatedMode`), not a deletion target.
- **Confidence:** medium

### `ScreenParallaxScale` without the window recess
- **Where:** `[WorldUI] ScreenParallaxScale`
- **Observation:** its sibling `ScreenWindowRecess` and the frame quads were reverted (`82affec`, "test #17: artificial elements rejected") but `ScreenParallaxScale` was **deliberately kept** — the commit says so explicitly: "it amplifies REAL scene depth and remains the correct tool for 3D content like the guildmaster town".
- **Recommendation:** **not vestigial.** Listed here only because the surrounding code was deleted and it looks orphaned.
- **Confidence:** high

### `DevPanels`
- **Where:** `DevPanels` (`[WorldUI] DevShowAllPanels` under `[Dev] SimulateHands`)
- **Observation:** desktop-testing scaffolding, gated behind two config flags, with a `_spawned` guard added purely to stop a phantom teardown log.
- **Recommendation:** charter §5 protects "referenced only from the debug menu, which is a deliberate feature". Same argument applies. Keep.
- **Confidence:** medium

### `ModalStyle = "screen"`
- **Where:** `WorldUIConfig.ModalStyle`
- **Observation:** the pre-P8 full-screen fallback path, superseded by `"window"` as the default. Explicitly retained in `1c4fcd8` as the escape hatch, and a window that **fails** to convert still raises the full screen automatically.
- **Recommendation:** the automatic failure path keeps the screen machinery live regardless; the config value itself is user data. Keep.
- **Confidence:** high

### `OcclusionProbe`
- **Where:** `Compat/OcclusionProbe` (outside this subsystem, but `ActorBars` integration was removed from it)
- **Observation:** default flipped to **false**, superseded by `DepthShaderSwap`; retained as "opt-in legacy fallback". While disabled it discovers no targets and restores anything ever hidden.
- **Recommendation:** flagged for the Compat subsystem's census, not this one. Noted here because `ActorBars` carries the scar (the removed linecast/hysteresis hide).
- **Confidence:** medium

### `SelectionReady` / `InitiativeSelectionGlow` figure-glow remnants
- **Where:** `SelectionReadyHighlighter`
- **Observation:** `1375bf3` reverted the board-mini glow the user did not want (dropped `SetHilighted`, `_owned` tracking, `Choreographer.FindClientActorGameObject`) and repointed the same pending-detection to the initiative bar. Whether any of the old path survives is worth a check.
- **Recommendation:** verify by grep during the census.
- **Confidence:** low

### `TrayControlDockSurface` as a whole
- **Where:** `TrayControlDockSurface`, `[WorldUI] TrayNativeControls` (default **false** since `a307274`)
- **Observation:** the entire surface is off by default and, per `424d7db`, "no native controls dock any more, so the control set is empty".
- **Recommendation:** **do not delete.** It is a deliberate config-reachable alternative ("the docking path stays available behind the config for anyone who prefers the real widgets"), and it is the only implementation of the native-widget dock mechanism. If it is ever removed it must be a user decision, i.e. Tier 3.
- **Confidence:** high

---

## Standing warnings for the refactor

1. **Ordering that looks swappable is not.** `OnPreCull` vs `OnPreRender` (map matrices), `Update` vs `LateUpdate` (`Flatten2D`, `HostLateSync`, `HexHintFacing`, early-settle), icons-then-party-token, sub-windows-then-parent (`CloseAll`), and the flag-bit order of the network packet (out of scope here but the same class of hazard).
2. **Paired lists exist because undo is asymmetric.** `AddedScrollMasks` vs `EnabledScrollMasks`; added raycasters vs pre-existing ones; `_splitNoUi` vs `_splitFailed`; `want` vs `wantLock`; `CombatLog` master vs `CombatLogUserClosed`. Merging any pair breaks one direction of the undo.
3. **Three near-identical constants are usually three different bugs.** `FitMinAlpha` 0.05 vs `MaskMinAlpha` 0.15; the several `ReleaseDelaySeconds` 0.3; the several `DensityScale` values. Before deduplicating, prove the difference is nil — charter §4 Tier 2.
4. **Every "redundant" second write is probably the fix.** The immediate + queued input write; the `_ZTestMode` + `unity_GUIZTestMode` pair; the re-warp of the latched pixel; the sweep + per-frame `overrideSorting` re-assert.
5. **A number that looks round is not therefore arbitrary.** 0.15 alpha, 2 mm label proud, 10 mm cluster proud, 2999 queue, 1000/1100/3999/4000/5000 sorting tiers, 12 mm poke depth, 0.4 s cooldown, 6 s recall, 1.5 s grace, 0.5/1.5 s fit damping — each has a cited observation above.
6. **The sorting/queue ladder is ONE system spanning four files.** cap face 1 → dust particles 2 → keycap label 3 → panel canvases and `StatPanelSurface` 10 → modal host 1000 → grab bar and `ModalCloseButton` 1100 → dropdown Blocker 3999 → dropdown List 4000 → ray visuals 5000; and, on the queue axis, opaque `BoardLit` (Geometry) → depth mask 2999 → UI/labels 3000, never the game HUD font's 4003+Always. There is **no shared constant** — renumbering any tier in isolation breaks a pair somewhere else.
7. **Static scratch buffers assume a single-threaded, non-re-entrant tick.** `CanvasConversion` and `GrabbableModal` both reuse process-static lists across measurement passes, documented as safe only because ticks run sequentially on the main thread. Introducing a coroutine, an async step, or a second driver instance silently corrupts the fit *and* the depth mask with no error.
8. **Three sibling files use three different "am I active?" gates** — `InputModeGuard.Active` (`ForceMouseMode && ConversionActive`), `EscMenuInputBlock.ShouldSuppress` (`VRSession.IsRunning`), `TakeDamagePanelSafety.GuardsActive` (`ConversionActive`), plus `VirtualMouse.TickSuppressPhysicalMice` (`SuppressPhysicalMouse && VRSession.IsRunning`). These are almost certainly deliberate and are **not** interchangeable. Unifying them is a Tier 3 proposal, not a cleanup.
9. **`EscMenuInputBlock` self-registers from `InputModeGuard.Tick`** while every other patch class registers in `WorldUIModule.Init`. The in-code comment says this was an ownership workaround, so it is a legitimate consolidation candidate — but moving it changes *when* the patch lands and its gate would have to stay `IsRunning`, not `ConversionActive`.
10. **Config keys are user data, and several descriptions are now lies.** `MapCaptureMode`, `MapStrip*`, `MapUv*`, `MapAlbedoAmbient/Light` all describe behaviour that no longer executes. The correct action is deprecate-in-place with corrected text (the `[Comfort] SeatedMode` precedent), never deletion.
