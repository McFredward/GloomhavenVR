# TESTING — Phase 3c (World-space UI), HMD checklist

> Prereqs: Phases 1–2 pass (`docs/TESTING-P1.md`, `docs/TESTING-P2.md`). Quest 3 (Link,
> Virtual Desktop or Steam Link) or any OpenXR HMD. Config lives in
> `BepInEx/config/dev.gloomhavenvr.worldui.cfg` (created on first run; renamed from `worldui.gloomhavenvr.cfg` in P5); every surface has
> its own toggle so a misbehaving one can be disabled without losing the rest.
> Grep the log for `[WorldUI]` lines.

## 0. Desktop smoke tests (no HMD)

- [ ] `[Dev] Enabled = true`, `[WorldUI] DevShowAllPanels = true` → six dummy panels
      appear arranged around a point in front of the desktop camera, each with a title
      and a poke button.
- [ ] `[Dev] SimulateHands = true` (or F8): drive the fake hand into a dummy panel's
      button — log shows `DevPanel '<slot>' poked.` (this exercises the same
      GraphicRaycaster + ExecuteEvents path as a real fingertip).
- [ ] `[WorldUI] DevForceConvert = true` + load a scenario on the desktop: initiative
      track / element board / combat log / objectives visibly leave the 2D HUD and float
      in the scene; disable again → they return to their exact 2D places (no drift,
      no scaling residue). Repeat with F6 (ScriptEngine hot reload) instead of the
      config toggle — same result.
- [ ] With the mod disabled (`[General] Enabled = false`): 2D game completely vanilla.

## 1. Physical button cluster

- [ ] In a scenario, a three-button cluster (Undo | Ready | Skip) sits at the table
      edge in front of you, tilted like a control panel.
- [ ] Labels mirror the 2D buttons **including state changes**: end turn ("End Turn"),
      confirm movement, open door, long rest, confirm targets … — walk one full turn
      and compare every label against the 2D button (set `DevForceConvert` off on a
      second monitor run if needed). The Ready cap turns amber for confirm-type states.
- [ ] Disabled buttons are dimmed and give **no** hover tick / click pulse haptics;
      enabled ones give both.
- [ ] Poking Ready with the index finger commits exactly like clicking the 2D button:
      cap depresses ~8 mm, the turn/selection ends, multiplayer-safe path (watch the
      log for the game's own "READY BUTTON - ON CLICK" line).
- [ ] Undo after a move undoes the waypoint; Skip skips the movement/attack step.
- [ ] While a confirmation dialog / banner is up (UI locked): pokes are swallowed
      (log: `click swallowed — UI locked`).
- [ ] Buttons are hidden in the main menu / map (Menu2D) and while no scenario runs.

## 2. Scenario panels (initiative / elements / log / objectives)

- [ ] Initiative track floats above the far table edge, readable, avatars poke-able
      (poke a player avatar with two cards selected → initiative swap, verify the
      round order changes accordingly).
- [ ] Element infusion board floats beside it; elements update on infusion/waning.
- [ ] Combat log panel scrolls (poke-drag the list) and updates during fights.
- [ ] Objectives panel shows the scenario goals and progress.
- [ ] Each panel disappears from the 2D HUD while converted and returns after
      leaving VR / disabling its toggle.
- [ ] Panels re-anchor sensibly after grabbing/rotating the table (they follow the
      camera focus + rig yaw).

## 3. Dialogs, banner, stat panels

- [ ] Trigger a confirmation (e.g. long rest, forced ESC prompts): the dialog appears
      ~0.75 m in front of your face, yes/no are poke-able, the rest of the world UI is
      locked while it's open (ModalUI mode). Cancel and confirm both work.
- [ ] Round start / player turn / enemy turn banners appear as a brief toast in front
      of your head and fade with the game's own animation; during the toast no
      converted surface accepts pokes (soft lock), afterwards everything works again.
- [ ] Poke/click an enemy (Phase-3a pick or 2D fallback): its stat panel opens as a
      world panel floating beside the miniature, billboarded to you; closing it
      restores nothing visibly (it was converted only while open).
- [ ] The enemy current-turn card behaves the same during monster turns.

## 4. Actor bars

- [ ] Every miniature (players + enemies) has its HP/effect bar floating just above
      its head, facing you, world-anchored (bars do NOT smear across a head-locked
      plane — the P2 known-issue is gone).
- [ ] Bars grow gently with distance but stay readable across the table (clamp ×2.5).
- [ ] Damage numbers, heals, XP/gold pops and condition icons still animate (data flow
      untouched).
- [ ] Kill an enemy: its bar disappears cleanly (no orphaned host canvases — check
      the scene in UnityExplorer for stray `GloomhavenVR.Panel_ActorBar` objects).
- [ ] `[WorldUI] ActorBars = false` → vanilla screen-projected bars return on the flat
      mirror and the fake-worldspace panels re-attach undamaged.

## 5. Wrist HUD

- [ ] Rotate your non-dominant wrist toward your face (watch-check gesture): a small
      dark panel fades in on the back of the wrist with name, level, HP, XP, gold and
      conditions of your current/selected character.
- [ ] Values update within ~0.3 s after taking damage / gaining XP.
- [ ] Look away → it fades out; it never blocks pokes (no raycasts).
- [ ] `[Hands] PrimaryHand = Left` moves it to the right wrist.

## 6. Floating 2D screen

Boot phases (menu-blackscreen fix, `fix/menu-blackscreen`):

1. **Intro/splash** (scene 0 + `Intro`): FlatScreen is gated OFF — the intro renders
   vanilla on the desktop. The HMD shows the menu rig's **void** ([Rig] VoidColor, default black) plus a
   small "GloomhavenVR starting…" label. Log: `Starting indicator shown (pre-menu
   scene, FlatScreen gated).`
2. **Main menu** (`Gloomhaven_unified` and later menu scenes): the screen engages.
   Log sequence: `FlatScreen shown (…)`, `FlatScreen: UICamera '<name>' →
   RenderTexture.`, `FlatScreen quad placed: pos=… shader=… | head '<name>' …`,
   `Desktop mirror active — FlatScreen RT (WxH) blits to the backbuffer …`,
   `UICamera '<name>' excluded from XR rendering (stereoTargetEye … → None …)`.
3. **Scenario**: screen hides, UICamera restored (`FlatScreen hidden — UICamera
   restored to the backbuffer.` + `UICamera '<name>' restored to vanilla XR
   behavior`).

Checklist:

- [ ] During the intro: desktop shows the intro video/logos normally; HMD shows the
      void ([Rig] VoidColor, default black since test #6) + the "starting…" label.
      The LABEL is the sign of life now — no label AND no menu later means the rig
      camera is not rendering (grep the `Camera inventory` lines, or set VoidColor
      to a grey like 1F2126FF to tell "renders but empty" from "camera dead").
- [ ] In the main menu (no scenario): a large virtual screen floats in front of you
      showing the full 2D menu, head-tracked via the menu rig ([Rig] MenuRig).
- [ ] **The desktop monitor shows the same menu at the same time** (RT mirror blit)
      and stays fully mouse-operable in parallel — this is the guaranteed fallback;
      the desktop must never be black.
- [ ] **Trigger click (test #6 fix)**: point the DOMINANT hand's laser at a menu
      button (beam starts at the index fingertip; only the dominant hand has a beam)
      and pull the trigger. The button must actually CLICK, not just hover. Log
      sequence per click:
      `FlatScreen pointer: trigger PRESS at RT pixel (x,y), latch=True` →
      `VirtualMouse: left button PRESSED at (x,y)` → on release
      `VirtualMouse: left button RELEASED at (x,y)` +
      `FlatScreen pointer: trigger RELEASE at RT pixel (x,y) — CLICK (latched)`.
      The click latch freezes the pointer at the press pixel so hand tremor cannot
      turn the click into a no-op drag ([WorldUI] ClickLatch).
- [ ] **Drag still works**: press and deliberately sweep the ray (> ~2° for ~0.15 s):
      log `FlatScreen pointer: click latch OPENED → drag (…)`, then scroll lists /
      sliders follow the ray until release (`… — drag end`).
- [ ] **Poke click**: lean/step toward the screen (or lower [WorldUI] ScreenDistance)
      and touch a button with the index fingertip: click at the poked position with
      a haptic pulse (`FlatScreen poke: <side> fingertip PRESS at RT pixel (x,y) …`,
      release on withdraw). Works with either hand.
- [ ] **Handedness switch**: in the menu, pull the NON-dominant trigger — the laser
      (and click hand) moves to that hand with a haptic confirm
      (`Handedness switch: … dominant hand is now …`); pulling the other trigger
      switches back. The choice persists ([Hands] PrimaryHand) and the card fan /
      wrist HUD side follows in scenarios.
- [ ] Screen size/distance feel OK (defaults: [WorldUI] ScreenWidth = 2.2 m at
      ScreenDistance = 1.6 m; both live-tunable).
- [ ] The void around the screen is pure black ([Rig] VoidColor — set a dark grey
      like 1F2126FF only when debugging camera issues).
- [ ] **Text input caveat**: clicking a text field focuses it, but typing requires the
      physical keyboard — expected limitation, document anything worse.
- [ ] Guildmaster/merchant/level-up screens are usable end-to-end on the screen.
- [ ] Entering a scenario hides the screen automatically; opening an unconverted
      modal window (merchant in town via ESC etc.) brings it back; converted
      confirmation dialogs do NOT trigger it.
- [ ] On hide, the desktop mirror gets its 2D UI back (UICamera targetTexture
      restored).
- [ ] After a recenter (B+Y chord) or scene change the screen snaps back directly in
      front of you (`FlatScreen quad placed:` logged again).

### 6b. Config behavior matrix (`[WorldUI] FlatScreen` × `[Rig] MenuRig`)

The two switches are independent; all four combinations are defined:

| FlatScreen | MenuRig | HMD in menus | Desktop in menus |
|---|---|---|---|
| true (default) | true (default) | VoidColor void + head-tracked floating screen | 2D UI via RT mirror blit (mouse works) |
| true | false | floating screen anchored to the static menu camera (no head tracking; the game cameras render stereo but do not follow your head) | 2D UI via RT mirror blit (mouse works) |
| false | true | head-tracked menu camera view (VoidColor void if the scene has no 3D content); screen-space UI renders wherever vanilla XR puts it | vanilla (XR mirror; UI untouched) |
| false | false | **fully vanilla** under XR: no rig, no redirect, UICamera untouched | vanilla (XR mirror) |

`[WorldUI] FlatScreen = false` is the **vanilla-menu fallback**: the mod never
touches the UICamera (no RenderTexture redirect, no stereo exclusion, no desktop
blit). Use it to bisect menu rendering problems. `[WorldUI] Master = false`
disables the whole surface set including the FlatScreen and its UICamera handling.

## 7. Tooltips

- [ ] Hover a converted surface element (initiative avatar, element, objective) with
      the fingertip: the game tooltip appears as a small world panel above your
      fingertip, facing you.
- [ ] It disappears on poke-out; `[WorldUI] Tooltips = false` restores the screen-space
      tooltip untouched.

## 8. Gamepad-mode guard

- [ ] With a gamepad plugged in and used in the menu BEFORE starting VR: once VR is up,
      the log shows `InputModeGuard: switching the game from gamepad to mouse mode`,
      and loading a scenario loads the `Game` scene (log scene name), not
      `Game_gamepad`.
- [ ] Pressing gamepad buttons during VR does not flip the UI to gamepad mode
      (log: `blocked switch to gamepad mode`).
- [ ] VR off → gamepad mode works again normally.

## 9. Reversibility / stability

- [ ] F6 hot reload mid-scenario with everything converted: all panels return to the
      2D HUD, no exceptions in the log, reload brings them back.
- [ ] Leave scenario → main menu → new scenario: conversions rebuild cleanly
      (no duplicate hosts, no dead references).
- [ ] 30 min play session: frametime stable (no per-frame GC growth from WorldUI —
      profile with the overlay or UnityExplorer if suspicious).

## 10. Menu-blackscreen triage (new log lines)

For any "black desktop / black HMD in the menus" report, grep the BepInEx log for:

| Line | Meaning |
|---|---|
| `Camera inventory after scene '<name>' (N active):` + per-camera lines | Full disposition of every active camera (tag, depth, clear, cullingMask, stereoTargetEye, render target, `[VR head]` marker), logged 2 frames after each scene load while in Menu2D. **Attach these lines to every report.** |
| `FlatScreen quad placed: … shader='…' … head '…' mask=…` | Quad pose + shader + head camera state at each instant placement. `shader='NULL'` = no usable shader shipped (report immediately). |
| `Desktop mirror active — FlatScreen RT …` | The end-of-frame RT→backbuffer blit engaged. If the desktop is still black WITH this line present, the RT itself is black (UICamera not rendering into it — check the inventory for `target=GloomhavenVR.FlatScreenRT`). |
| `UICamera '…' excluded from XR rendering …` / `… restored to vanilla XR behavior` | Hypothesis-B guard (screen-space UI kept out of the HMD / desktop backbuffer kept). |
| `FlatScreen quad was destroyed externally — rebuilding.` | A scene swap killed the quad; it self-heals. Frequent repeats = report. |
| `Starting indicator shown (pre-menu scene, FlatScreen gated).` | Intro gate active — FlatScreen deliberately idle during scene 0/`Intro`. |
| `Menu rig built around camera '…' (… clear X → Y …)` | Menu rig camera + clear-color override. HMD **grey** = camera renders, content missing; HMD **black** = camera not reaching the HMD at all. |

**Player.log is still wanted**: the previous report did not include it. Attach
`%USERPROFILE%\AppData\LocalLow\FlamingFowlStudios\Gloomhaven\Player.log` (and
`Player-prev.log`) together with `BepInEx/LogOutput.log` into a dated subfolder of
`.planning/debug/` — Unity-native XR/rendering errors land only there.

## Known limitations (expected, not failures)

- The phase banner no longer blocks 2D input full-screen while converted (its
  fullscreen block image travels with the toast); VR-side input is soft-locked
  instead. Desktop-parallel play during a banner is mildly less protected.
- The intro/splash is desktop-only by design (FlatScreen gate); the HMD shows the
  void ([Rig] VoidColor) + "starting…" label until the main menu scene loads.
- Keyboard text entry on the flat screen requires the physical keyboard.
