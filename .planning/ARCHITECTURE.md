# GloomhavenVR — Architecture & Interaction Concept

> Synthesized from `.planning/research/{TOOLCHAIN,VR-PRIOR-ART,CARDS,BOARD-INPUT,UI-ARCH}.md`.
> All class/method references verified against the decompiled sources in `decompiled/`.

## 0. Verified foundation

| Fact | Value |
|---|---|
| Engine | **Unity 2021.3.5f1**, Mono, .NET Framework profile → plugin TFM `net472` |
| Render pipeline | Built-in RP + PostProcessing v2 |
| Game assemblies | `GH.Runtime` (UI/presentation), `ScenarioRuleLibrary` (rules, own worker thread), `MapRuleLibrary`, `GH.Shared` |
| Input | Unity InputSystem 1.3.0 (active) + InControl; legacy `UnityEngine.Input` unused |
| UI | 100 % uGUI + TMP, all canvases Screen-Space-**Camera** (UICamera tag), custom `UIWindow`/`UIWindowManager` (45 window IDs) |
| Camera | Hand-rolled orbit rig in `CameraController.LateUpdate` (no Cinemachine at runtime), zoom = FOV |
| XR in shipped build | XRModule/SubsystemsModule/SpatialTracking present; **XR Management + provider absent** → mod ships them |
| Patch stability | Game frozen at v1.1.8307.0 (Jan 2024), no anticheat |
| Loader | **BepInEx 5.4.23.5** + preloader patcher; **HarmonyX** (bundled 0Harmony) |

**Golden seams discovered (why this mod is feasible):**

1. **One picking choke point:** `MF.FindInteractableAtMousePosition` (GH.Runtime/MF.cs:387) — all hex/actor/door/chest picking goes through a single screen-ray function. Patch it → the whole game follows the VR hand, incl. hover highlights.
2. **One tile-click delegate:** `TileBehaviour.s_Callback(CClientTile, List<CTile>, bool, bool, bool)` — all board clicks commit through it.
3. **Ready-made programmatic card API** (built for MP proxies): `CardsHandUI.SelectCard/UnselectCard`, `ProxySelectCardAction(cardInstanceID, ActionType)`, `ProxyShortRest`, `ProxyLongRest` — bypass all hover/raycast guards.
4. **Per-card Canvas:** every ability card is procedural uGUI with its **own re-parentable Canvas** (game re-parents them itself for previews) → real 3D cards with live original faces.
5. **Virtual mouse exists:** `InputManager.CreateVirtualMouse` (console support) — a VR pointer can drive all remaining 2D uGUI without touching the InputModule.
6. **Phase hooks without Harmony:** `UINavigation.StateMachine.EventStateChanged` (public event) + postfix on `Choreographer.SetChoreographerState` / `Choreographer.ProcessMessage` for engine→UI messages.

## 1. Module map

**The repo layout lives in [`docs/DEVELOPING.md`](../docs/DEVELOPING.md), and only there.**

This section used to carry its own copy of the tree. It was written when the mod was 182 files
and a module was a folder you could scan; by 2026-08 `WorldUI/` alone held 101 files in one
folder, and the copy here had drifted from the copy there — which is the failure mode a second
copy always has. The 2026-08 refactor moved 251 files into named subfolders and rewrote
`DEVELOPING.md` around them, including the two rules a newcomer needs (folder does not equal
namespace, on purpose; and the five path pins that fail loudly when a file moves). The 2026-09
refactor (ModBuild 481-482) moved more of the same way, so **any `src/` path written in a
`.planning/` record before 2026-09-08 is pre-refactor** — `.planning/INDEX.md` §5 tabulates the
translation.

What belongs HERE is the part `DEVELOPING.md` does not say: **why the modules are cut where they
are.** The cut follows the GAME's seams rather than ours —

- `Core/` owns everything that must happen before the engine draws a VR frame, and everything
  about the room the board stands in. It is the only module the others may depend on freely.
- `Rig/`, `Hands/` are the player's BODY: pose, locomotion, comfort, the primitives every
  interaction is built from. They know nothing about cards or hexes.
- `Cards/`, `Board/` are the two things the player MANIPULATES, and each is anchored on one
  golden seam from §0 — the card API for one, the picking choke point for the other.
- `WorldUI/` is everything the player READS. It is the largest module because the game's entire
  interface is uGUI and every window of it has to become an object in the room.
- `Net/` mirrors all of the above onto a peer, and is deliberately a separate module rather than
  a concern spread through the others: the 1:1 rule ("a peer sees what the owner sees") is
  checkable only when the mirror is in one place.

## 2. Core: XR bootstrap (Phase V1)

DaXcess pattern (LCVR/RepoXR), adapted:

1. **Preloader** (`BepInEx/patchers/GloomhavenVR.Preload.dll`): copy `UnityOpenXR.dll` +
   `openxr_loader.dll` → `Gloomhaven_Data/Plugins/x86_64/`, write
   `Gloomhaven_Data/UnitySubsystems/UnityOpenXR/UnitySubsystemsManifest.json` (version-matched
   to OpenXR 1.10.0). No restart needed since it runs before engine init.
2. **Plugin `Awake`**: `Assembly.LoadFile` RuntimeDeps → pre-flight check
   (`SubsystemManager.GetAllSubsystemDescriptors` contains "OpenXR Display"/"OpenXR Input") →
   create `XRGeneralSettings`/`XRManagerSettings`/`OpenXRLoader` via `ScriptableObject.CreateInstance`,
   enable `OculusTouchControllerProfile` (+ Index/generic as fallback) **before** session start,
   `RenderMode.MultiPass` (SPI = later opt-in), `InitXRSDK()` + `Start()`.
3. **Runtime failover**: enumerate `HKLM\SOFTWARE\Khronos\OpenXR\1` + well-known runtime JSONs
   (Meta, SteamVR, VDXR), retry via `XR_RUNTIME_JSON`. Force `-force-d3d11` if needed (OpenXR
   desktop requires D3D11).
4. Config gate: `vr_enabled` in BepInEx cfg; game stays 100 % vanilla when off.

Quest 3 works over Link (Meta runtime), Virtual Desktop (VDXR) and Steam Link (SteamVR) — all
OpenXR. Ship generic Touch profile; TouchPlus is icing (known Unity bug).

## 3. Rig: camera takeover (Phase V1)

- Prefix-skip `CameraController.LateUpdate` in VR mode (it writes transform+FOV every frame).
- Own rig: `VROrigin` GameObject → head camera with InputSystem `TrackedPoseDriver`
  (`<XRHMD>/centerEye*`); `XRDevice.DisableAutoXRCameraTracking` on game cameras we don't own.
- **Diorama scale**: rig scaled so the board reads as a table (~10–20×, tune to hex prefab size
  `UnityGameEditorRuntime.s_TileSize`); board root for reference framing:
  `ClientScenarioManager.m_Board` / `RoomVisibilityManager.Maps`.
- **World grab** (Demeo): one grip = drag table; two grips = rotate + pinch-scale; optional
  snap turn. Fog of war is tile-based (VR-safe); wall-fade is a global shader toggle we control.
- UICamera + worldspace-UI cam: kept for retained 2D surfaces, excluded from stereo where needed.
- PPv2: start MultiPass (compatible); disable VolumetricFog/problem effects via config.

## 4. Hands: presence & interaction primitives (Phase V2)

- Hand meshes from an AssetBundle (own companion project; SteamVR-plugin hands are BSD-3 and
  redistributable — verify per-asset license before shipping).
- `TrackedPoseDriver` per hand (`<XRController>{Left|Right}Hand/pointer*`).
- **FingerCurler** (LCVR pattern): trigger → index curl, grip → middle/ring/pinky, capacitive
  thumb touch → thumb; point pose = grip held + trigger released; open palm = nothing held.
- Interaction primitives exposed to all feature modules:
  - **Poke**: fingertip sphere-collider + `ExecuteEvents.Execute(pointerClick/Enter/Exit)` —
    the game itself clicks buttons this way (`BaseButtons.clickButton`), so modality
    (`UIManager.ToggleLockUI` disabling `GraphicRaycaster`s) is respected by checking
    raycaster state first.
  - **Ray**: index-finger ray for far interaction (board, distant UI), visible bendy/straight laser.
  - **Grab**: proximity + snap-to-pose (Demeo does *not* use physical colliders for cards —
    proximity grab feels better and is deterministic).
  - **Palm gate**: dot(palmNormal, toHMD) threshold with hysteresis → card fan visibility.
- Haptics via OpenXR on hover/click/grab confirm.

## 5. Cards: the Demeo hand (Phase V3b) — flagship feature

**Show:** suppress the 2D hand (`CardsHandManager.Show` prefix-skip in VR); on palm-up gate,
fan out `CCharacterClass.HandAbilityCards` as 3D cards anchored to the left hand.

**Card visual:** 3D card backing (bundle mesh) + the card's **own live uGUI Canvas** re-parented
to WorldSpace onto the backing (original art, live text). Fallback: per-card RenderTexture.

**Card-selection phase** (SRL message `PlayerToSelectAbilityCardsOrLongRest`, max 2 cards):
- Grab card from fan (proximity grab) → inspect at natural size.
- Release over the **play surface** (floating tray with 2 slots near the table edge) →
  `CardsHandUI.SelectCard(card)`; pull a card back off → `UnselectCard`.
- **First slot = initiative card** (mirrors game rule "first selected = initiative");
  reordering the two cards on the tray calls `InitiativeTrackPlayerAvatar.SwapInitiative()`.
- **Rest**: physical short-rest/long-rest tokens on the tray → `ProxyShortRest` /
  long-rest flow (`CardHandMode.LoseCard` → `HandleLongRest`).
- **Confirm**: big physical **Ready button** on the table → `ReadyButton.OnClickInternal`
  (17-state enum — reuse its state to label/enable the physical button).

**During your turn** (half selection): the two played cards lie on the table in front of you;
**poke the top or bottom half** → `FullAbilityCard.OnAbilityClick(ActionType, …)` /
`ProxySelectCardAction(cardInstanceID, ActionType)`. Default move/attack presented as two
small cards, same interaction. `CardsActionControlller` phase machine (Select1st→Target→
Select2nd→Target) drives which cards glow as poke-able.

**Event pump:** postfix `Choreographer.ProcessMessage` → typed VR event bus (cards drawn,
selection required, turn start/end). **Known trap:** `OnCardSelected` spin-waits the main
thread up to 1 s for the SRL worker ack (CardsHandUI.cs:2016) — route selection calls through
a coroutine/async wrapper so the render loop never blocks (reprojection covers one slow frame,
but never call it from per-frame hand code).

## 6. Board: touch & ray targeting (Phase V3a)

- **Picking**: Harmony-patch `MF.FindInteractableAtMousePosition` — in VR compute the pick from
  (a) fingertip overlap when hand is near the board ("touch"), else (b) index-finger ray.
  Also patch `InputManager.CursorPosition` (projected pick point) and let `HoverRegisterer`
  run unchanged → all hover highlights (pooled `HexSelect_Control` shader hexes, world-space,
  VR-safe) work for free.
- **Commit**: trigger/pinch while touching/pointing → drive the existing click path so
  `CInteractable.ShowNormalInterface` → `TileBehaviour.s_Callback` fires exactly as with mouse.
  **Never call `ScenarioRuleClient` directly for board actions** (undo/MP consistency).
- **AoE placement**: hover positions the pattern (existing melee-facing logic follows hovered
  tile); **thumbstick left/right → `WorldspaceStarHexDisplay.RotateAOEClockwise(bool)`**
  (public, 60° steps); trigger = lock, second press = confirm (mirrors mouse UX).
- Doors/chests/loot/actors: same delegate path — nothing extra needed.
- Actor info: poke an enemy → its stat panel as world-space popup at the miniature (see §7).

## 7. WorldUI: physicalized interface (Phase V3c)

Strategy per surface (inventory from UI-ARCH.md):

| Surface | VR treatment |
|---|---|
| Ready/Undo/Skip buttons | **Physical 3D buttons** on table edge → `OnClickInternal` (state-labeled) |
| Initiative track | World-space board above the table (convert `InitiativeTrack` canvas); avatars poke-able (initiative swap) |
| Element infusion board | World-space panel near initiative track (`InfusionBoardUI`) |
| Character HUD (HP/XP/conditions) | Wrist panel on left hand + full panel when looking at own miniature |
| Actor health/effect bars | Already positioned via `WorldToScreenPoint` fake-worldspace → re-anchor as **true world-space** canvases above miniatures (`WorldspaceDisplayPanelBase.TrackCharacter` replaced; beware its `LateUpdate` fighting re-parents — disable the tracker in VR) |
| Monster stat panel, combat log, objectives, phase banner | World-space panels, curved layout around table |
| Confirmation dialogs (`UIConfirmationBoxManager`) | World-space modal in front of HMD, poke Yes/No |
| Tooltips | Reuse singleton `UITooltip` (already render-mode-aware) on hover/point |
| Main menu, merchant, level-up, guildmaster map | **Floating 2D screen** (UUVR screen-mirror pattern) + virtual-mouse pointer — full physicalization is post-v1 |

Plumbing: converted canvases get XRIT `TrackedDeviceGraphicRaycaster` (XRIT 2.x supports
2021.3) or our poke/`ExecuteEvents` path; remaining 2D driven via the game's own
**virtual mouse** (`InputManager.CreateVirtualMouse` warp + click = zero InputModule patches).
Force non-gamepad mode (`InputManager.GamePadInUse == false`) so `Game` (not `Game_gamepad`)
scene variants load. Mod strings via I2 `LocalizationManager.AddCSVSource`.

## 8. VR mode state machine

Driven by `UINavigation.StateMachine.EventStateChanged` + `Choreographer` postfixes:

```
Menu2D ── scenario loaded ──▶ TableIdle (spectate/world-grab, palms give hand)
TableIdle ── PlayerToSelectAbilityCards ──▶ CardSelection (fan + tray + ready btn)
TableIdle ── your turn ──▶ HalfSelection (played cards poke-able)
HalfSelection ── action needs targets ──▶ BoardTargeting (ray/touch + AoE stick)
BoardTargeting ── StepComplete/Pass ──▶ HalfSelection | TableIdle
any ── UIWindow modal ──▶ ModalUI (world-space dialog, rest locked)
```

Each mode enables/disables interaction primitives; world-grab and snap turn are available
in every scenario mode INCLUDING ModalUI (test #13 — floating dialogs must not freeze the
diorama; BoardTargeting still owns the stick, Menu2D has no table).

## 9. Multiplayer, legal, licensing

- Photon Bolt state replication; **mod issues exactly the commands the mouse UI issues** —
  UI/input/camera-only → no desync surface.
- > **Corrected 2026-09-08 (ModBuild 483).** This section used to end *"v1 targets single-player;
  > MP untested but not structurally broken."* That has been false for a long time and it now
  > contradicts a standing ruling: **every feature must be multiplayer-compatible, and the sync is
  > designed in from the start, not added afterwards.** Multiplayer is a first-class subsystem —
  > `src/GloomhavenVR/Net/` carries a versioned side-channel (`NetProtocol`, wire v3, magic
  > `GVR1`) with rig, extras and extension records, remote avatars, shared windows with a
  > one-size-everywhere law, a version-mismatch handshake keyed on `ModBuild`, and spatial voice.
  > Read the `NetProtocol.cs` header for the protocol and its three graceful-degradation nets
  > before touching any of it.
- No anticheat. Distribute **only own code + own bundles**; never game assets, decompiled
  code, or Addressables catalogs. Use game's own assets at runtime via Addressables keys.
- **Mod license: GPL-3.0** — we adapt patterns/code from LCVR/RepoXR/UUVR (all GPL-3.0);
  clean-room rewriting everything is not worth it. SteamVR hand assets (BSD-3) compatible.

## 10. Top risks (aggregated)

| Risk | Mitigation |
|---|---|
| PPv2 × Single-Pass-Instanced broken | MultiPass default; SPI as experimental opt-in with PPv2 patches (RepoXR precedent) |
| Card-selection main-thread spin-wait (1 s) | async/coroutine wrapper around Select/Unselect calls |
| Canvas conversion volume (45 windows) | phased: physicalize scenario-critical only, floating screen for the rest |
| Decompiler-mangled names (`ReadyButton` enum etc.) | verify patch targets against real DLL with ilspycmd/dnSpy before coding |
| Stereo artifacts (outlines/EPOOutline, fog, billboards) | per-effect config kill-switches in Compat module |
| `GraphicRaycaster`-based modality bypassed by direct ExecuteEvents | poke primitive checks raycaster enabled-state first |
| Harvested XR DLL version drift | single dummy 2021.3.5f1 build produces all RuntimeDeps + natives + manifest as one set |
