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
- [ ] Test #14: bars keep a FIXED board-space size — step away / lean back and the
      bars do NOT grow; they scale only when the diorama itself is scaled (like the
      minis). Billboard rotation still tracks the HMD.
- [ ] `[WorldUI] BarFixedSize = false` restores the legacy distance growth (×2.5 max).
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
- [ ] **Trigger click (test #7 fix — ExecuteEvents delivery)**: point the DOMINANT
      hand's laser at a menu button (beam starts at the index KNUCKLE — curl-proof —
      and ENDS exactly at the reticle on the screen, never passing through) and pull
      the trigger. The button must actually CLICK. Log sequence per click
      ([WorldUI] ClickMode = execute, the default):
      `Under pointer (x,y): 'ButtonName' (canvas '…')` →
      `FlatScreen pointer: trigger PRESS at RT pixel (x,y), latch=True, mode=execute`
      → on release `DirectClick at (x,y) → clicked 'ButtonName'` +
      `FlatScreen pointer: trigger RELEASE … — CLICK (latched)`.
      If `Under pointer` names the WRONG element (or `nothing`), the pixel mapping is
      off — report that log line; if it names the right button but nothing happens,
      try `ClickMode = virtualmouse` and report both.
      The click latch freezes the pointer at the press pixel so hand tremor cannot
      turn the click into a no-op drag ([WorldUI] ClickLatch).
- [ ] **Hands visible on the black void**: both hands render in a light tone
      ([Hands] HandColor, unlit — lit shaders go black in the lightless void).
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

---

## 11. P6 (test-#8 follow-up) — UI-shell fixes

Config: `[Rig] Experimental3DMap` (placeholder, no effect), `[WorldUI] PanelsFollowView`,
`[WorldUI] ManualScreenChord` / `ManualScreenChordSeconds`, `[SettingsPanel] ChordHoldSeconds`
(chord now fires on RELEASE). Grep for `[Rig]`, `[WorldUI]`, `MODAL FALLBACK`,
`MANUAL SCREEN CHORD`, `Mode`.

### 11.1 Campaign/world map stays flat (Menu2D)

- [ ] Start guildmaster / campaign, land on the WORLD MAP: the HMD shows the floating
      2D screen with the **complete map + UI composite** (no giant 3D map below you,
      no black hole where the map should be). Log: `Menu rig built at vantage of
      camera '…'` — and NO `VR rig built at focus …` line before an actual scenario.
- [ ] The flat desktop window mirrors the same complete composite (map visible).
- [ ] Map camera appears in the capture log: `FlatScreen stack capture: '…' → RenderTexture`.
- [ ] Enter a combat scenario: `VR rig built at focus …` appears only now (diorama +
      panels + bars come up as in §1–§7). Mode log: `Menu2D -> …`.
- [ ] Leave the scenario back to the map: rig tears down (`rig kind change Scenario →
      Menu`), the flat screen returns with the map visible.
- [ ] `[Rig] Experimental3DMap = true` changes NOTHING (placeholder; documented in the
      config description).

### 11.2 Catch-all modal fallback (in-scenario dialogs) — P8: floating windows

`[WorldUI] ModalStyle` (default `window`, P8/test #12): the fallback no longer
summons the whole flat screen — only THAT window floats in front of the HMD.

- [ ] Play a scenario with tutorials enabled (or any scenario intro text): the moment
      a 2D window opens that VR does not convert, the WINDOW ITSELF appears as a
      world-space panel ~1.2 m in front of the HMD (NOT the full screen). Log:
      `MODAL FALLBACK: window '…' (ID …) opened …`, `MODAL WINDOW: '…' floated in
      front of the HMD …` and a mode change `… -> ModalUI`.
- [ ] Scenario-start STORY BOX ('UI Story Box' on 'Story Canvas'): floats as a panel;
      clicking it (laser trigger or fingertip poke) advances the story pages exactly
      like a 2D click (the box's full-area skip button receives the click). After the
      last page the panel disappears and the window is restored to its 2D home.
- [ ] Tutorial/level messages (`UILevelMessageBoxFixed`, Introduction Canvas) and
      scenario CHOICE dialogs (`dialogPopup`) float the same way — poke AND laser
      both click their buttons.
- [ ] Dismiss the floated window: it is restored to its exact 2D home (check the
      desktop mirror afterwards — the 2D UI must be pixel-identical), mode returns to
      the previous flow mode. Log: `MODAL WINDOW: '…' released — restored to its 2D
      home.`
- [ ] The events that ARE converted do NOT trigger the fallback: plain confirmation
      boxes (world-space dialog), actor stat panels, phase banners, combat log.
- [ ] ESC (desktop keyboard) mid-scenario: the ESC menu floats as a window too.
- [ ] End-of-scenario rewards/results panels float (IDs RewardsPanel / ResultsPanel
      in the fallback log) — no deadlock at scenario end.
- [ ] `[WorldUI] ModalStyle = screen` restores the pre-P8 behavior (full flat screen
      for every fallback window).
- [ ] AUTOMATIC per-window fallback: if a window fails to convert (log
      `MODAL WINDOW: conversion of '…' FAILED (…) — falling back to the full flat
      screen`), the full screen rises for exactly that window.
- [ ] Quit to menu / scene change with a floated window open: no leaked
      `GloomhavenVR.Panel_Modal_*` hosts (UnityExplorer), no errors; F6 hot reload
      restores every floated window to 2D.

### 11.3 Manual screen chord (self-rescue)

- [ ] In a scenario, hold the NON-dominant lower face button (A or X) for ~2 s:
      double-check haptic pulse, the flat screen toggles ON in any mode. Log:
      `MANUAL SCREEN CHORD: flat screen toggled ON …`. Hold again ~2 s → OFF.
- [ ] P8: the chord works REGARDLESS of `[WorldUI] ModalStyle` — while the manual
      screen is up, any floating modal windows are RELEASED back into the 2D
      composite (they must be visible ON the screen), and re-float when the chord
      toggles the screen off with the window still open.
- [ ] A SHORT hold (~0.6–1.5 s, release before 2 s) still toggles the **settings
      panel** on release — the two chords never fire together (a long hold consumes
      the press; no settings panel after a screen toggle).
- [ ] The manual latch resets when the scenario ends (screen policy returns to
      Menu2D auto-show).

### 11.4 World-fixed panels

- [ ] In a scenario, note where the initiative track / element board / combat log /
      objectives stand at the table. SNAP TURN several times: the panels stay put in
      the world (you turn past them) — they do NOT swing around to stay in front.
- [ ] World-grab move/rotate/scale the diorama: panels move/scale coherently with the
      table (fixed relative to the world, like the minis).
- [ ] Recenter (B+Y chord or settings panel): panels re-anchor once to face your new
      seat, then stay fixed again.
- [ ] `[WorldUI] PanelsFollowView = true` restores the old per-frame follow.

### 11.5 Zoom-stable actor bars

- [ ] In a scenario, pinch-zoom the diorama considerably LARGER (world bigger): HP
      bars stay just above the miniatures' heads — never inside the minis.
- [ ] Zoom far OUT: bars stay just above the minis at their fixed board-space size
      (legacy `[WorldUI] BarFixedSize = false`: gentle distance growth, ×2.5 max).
- [ ] Boss/large monsters: bar clears the taller model (bounds-derived anchor).
- [ ] Summons spawned mid-fight get correctly anchored bars (bounds cached at adopt).

### Converted vs fallback window sets (P6 reference)

| Set | Windows |
|---|---|
| **Converted / passive — never trigger the fallback** | ConfirmationBox (world dialog, while `[WorldUI] Dialogs` on), ActorStatPanel, EnemyCurrentTurnStatPanel (stat surfaces), CombatLog, CardHolder (Cards module), QuestTracker, MapObjectiveManager, TrapInfoPanel, DoorInfoPanel, MapNodeInfoPanel (hover popups), phase banner (custom-ID toast) |
| **Fallback → ModalUI + floating window (P8, `ModalStyle=window`) or flat screen (`screen` / conversion failure)** | EventsPanel, Message, HelpBox, TextInfoPanel, IntroductionScreen, RewardsPanel, ResultsPanel, TakeDamagePanel, DurabilityPanel, QuestPopup, UnlockQuestPopup, AdventureCompletionPanel, HeroLevelUpPanel, ESCMenu, Options(+Submenu/Vice), DifficultyPanel, CompendiumPanel, PartyPanel, EquipmentItemsPanel, Mutiplayer*/Character/MainMenu confirmation boxes, multiplayer panels; ConfirmationBox too when `[WorldUI] Dialogs = false` |
| **Live-polled (scene-serialized IDs), resolved to their UIWindow for the window style** | StoryController story box ('UI Story Box', `StoryController.window`), LevelMessageUILayoutGroup ×2 (tutorial/level messages incl. `UILevelMessageBoxFixed`, via `LevelMessagesUIHandler.s_Instance`), UIManager.dialogPopup (choice dialogs, `DialogPopup.Window`) |

## 12. Test #14 — panel planes, straight laser, placement

### 12.1 Content-fit laser/poke planes (ALL converted hosts)

- [ ] Start a scenario and check the log's `[Interact] Ray-uGUI canvas '…': world
      rect …` lines: `Panel_InitiativeTrack` must NOT report a ~45×25 m rect
      (sizeDelta 1920×1080) anymore — after the `Host rect fit 'GloomhavenVR.
      Panel_InitiativeTrack': 1920x1080 → …` line, the re-logged world rect must
      match the VISIBLE strip. Same for `Panel_CombatLog`, `Panel_Objectives` and
      every `Panel_Modal_*`.
- [ ] Sweep the laser across the table between panels: the dot lands on the board /
      scene behind, never on an invisible plane in mid-air; tray buttons and board
      hexes stay clickable with panels visible nearby.
- [ ] Multi-page story window: advance to a LONGER page — the host re-fits (a new
      `Host rect fit` log line at most ~0.5 s later) and the laser/poke plane covers
      the whole visible text (growth re-fit, every ~30 frames, 2 % threshold).
- [ ] No per-frame `Host rect fit` spam while a panel's content is static.

### 12.2 Dead-straight laser

- [ ] The beam NEVER changes direction when the dot crosses onto a panel, a card or
      a snapped hex — controller rotation alone re-aims it. Hits only shorten it.
- [ ] Hex snap ([Board] SnapToHexCenter): the game's own hex hover highlight shows
      the snapped hex; the visible dot stays exactly on the aim ray at the surface.
- [ ] The beam still starts at the pointing finger (knuckle projected onto the aim
      line) and does not swing when the trigger is pulled (finger curl).

### 12.3 Dot/beam on top of UI

- [ ] Move the dot across every corner of a floated dialog and the initiative track:
      the dot and the last beam segment are ALWAYS fully visible on the panel (never
      partially behind text/images). Render queue 4600 > canvas range; real scene
      geometry still occludes the beam normally.

### 12.4 Hero placement (second click)

- [ ] Round start: click your character (highlight) → point at a glowing start hex:
      log `[Placement] hover refresh → s_PlacementTile=(x,y), overUI=False, …` and
      the hex shows the hover star + character ghost preview.
- [ ] Click the hovered hex: log `[Placement] TileHandler click: tile=(x,y),
      armed=(x,y), actorSelected=True → will PLACE.` and the character moves.
- [ ] Repeat onto another glowing hex (re-position) — same flow.
- [ ] While the beam is ON a world panel (e.g. initiative track), overUI must log
      True and board hover must pause (vanilla semantics); off the panel it resumes.
