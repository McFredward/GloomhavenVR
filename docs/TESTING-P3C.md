# TESTING — Phase 3c (World-space UI), HMD checklist

> **Historical hardware checklist.** Retained for source and diagnostic links; this is not
> the current release acceptance list. Use [STATE.md](../.planning/STATE.md) and the latest
> build notes for current behavior and outstanding headset checks.


> **Audited 2026-09-08 at ModBuild 483** (base `49ceab21`). Every `[Section] Key`, log
> marker, type, method and file path named below was grepped against the tree. Config
> keys: no doc here names a live key that has been removed, and every key described as
> DELETED really is gone. What an audit of names cannot establish is that each step's
> expected BEHAVIOUR is still current — where a step was found asserting something the
> code now forbids, it says so in place.

> Prereqs: Phases 1–2 pass (`docs/TESTING-P1.md`, `docs/TESTING-P2.md`). Quest 3 (Link,
> Virtual Desktop or Steam Link) or any OpenXR HMD. Config lives in
> `BepInEx/config/dev.gloomhavenvr.worldui.cfg` (created on first run; renamed from
> `worldui.gloomhavenvr.cfg` in P5). NOTE: the per-surface kill switches this doc was
> written around are GONE — `Master`, `FlatScreen`, `ActorBars`, `Tooltips`,
> `ClickMode`, `ClickLatch`, `ManualScreenChord` and friends were all deleted in the
> 2026-08 rulings because each OFF state was a brick rather than a fallback. Some
> per-surface switches DO survive (`CombatLog`, `Dialogs`, `WristHud`, `MapRoomHand`,
> `DecisionDock`, `TrayNativeControls`, `ShowIntro`); the rest of the `[WorldUI]`
> section is tuning dials. Read the current set out of
> `src/GloomhavenVR/WorldUI/WorldUIConfig.cs` before believing any key named in this
> doc. Grep the log for `[WorldUI]` lines.

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

> **THE TABLE-EDGE CLUSTER IS GONE (retired 2026-08-25).** This section used to open
> "a three-button cluster (Undo | Ready | Skip) sits at the table edge in front of you,
> tilted like a control panel". There is no cluster and no `ButtonClusterMount`:
> `Cards/Tray/PlayTray.1.Core.cs:393` says so in as many words, and
> `PlayTray.6.Build.cs:474` calls the old owner "the retired cluster". CONFIRM, UNDO and
> SKIP are **keycaps in the control board's own recesses** now — SKIP is a generic cap in
> the third recess, seated by `SetConfirmUndoOffset` with its two siblings. The checks
> below still apply, to those caps; only the location has changed.
> (`PanelSlot.ButtonCluster` survives as a dummy-panel layout slot for
> `[WorldUI] DevShowAllPanels` and is not this.)

- [ ] In a scenario, CONFIRM, UNDO and SKIP are keycaps in the control board's recesses,
      reachable by poke and by laser.
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
- [ ] There is no `[WorldUI] ActorBars` switch any more — the bars are always on
      (user ruling 2026-08-13: its OFF released every adopted bar and left the flat
      game's fake-worldspace panels behind). Missing bars are a bug, not a setting.

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
   vanilla on the desktop. The HMD shows the menu rig's **void** (black, a code
   constant since 2026-08-22 — the old `[Rig] VoidColor` key is gone) plus a small
   "GloomhavenVR starting…" label. Log: `Starting indicator shown (pre-menu
   scene, FlatScreen gated).`
2. **Main menu** (`Gloomhaven_unified` and later menu scenes): the screen engages.
   Log sequence: `FlatScreen shown (backbuffer camera stack → RenderTexture; …)`, one
   `FlatScreen stack capture: '<cam>' → RenderTexture (depth …)` **per captured camera**,
   `FlatScreen quad placed: pos=… shader=… | head '<name>' …`,
   `Desktop mirror active — FlatScreen RT (WxH) blits to the backbuffer …`, and
   `[Core] Stereo policy: '<cam>' forced to StereoTargetEyeMask.None (…)` per camera plus
   a `Stereo policy sweep (…)` summary.
   (**None of the five lines this step used to name still exist.** It quoted a single
   `FlatScreen: UICamera '<name>' → RenderTexture.` and a `UICamera … excluded from XR
   rendering` — both predate the change from redirecting ONE camera to capturing the whole
   backbuffer STACK, and the move of stereo exclusion into `Core/VRCameraPolicy.cs`, which
   logs under `[Core]`. See `docs/CAMERA-POLICY.md` §1 and §6.)
3. **Scenario**: screen hides, the captured cameras go back to the backbuffer
   (`FlatScreen hidden — captured cameras restored to the backbuffer.` +
   `[Core] Stereo policy released — N camera(s) restored to vanilla stereo behavior.`).

Checklist:

- [ ] During the intro: desktop shows the intro video/logos normally; HMD shows the
      black void + the "starting…" label. The LABEL is the whole sign of life: the
      void colour is a constant now and cannot be dialled to a debug grey, so
      "renders but empty" vs "camera dead" is decided by the label plus the
      `Camera inventory after scene '<name>'` lines — no label AND no menu later
      means the rig camera is not rendering; quote the inventory in the report.
- [ ] In the main menu (no scenario): a large virtual screen floats in front of you
      showing the full 2D menu, head-tracked via the menu rig (unconditional — the
      `[Rig] MenuRig` switch is gone; its OFF built no rig at all).
- [ ] **The desktop monitor shows the same menu at the same time** (RT mirror blit)
      and stays fully mouse-operable in parallel — this is the guaranteed fallback;
      the desktop must never be black.
- [ ] **Trigger click (test #7 fix — ExecuteEvents delivery)**: point the DOMINANT
      hand's laser at a menu button (beam starts at the index KNUCKLE — curl-proof —
      and ENDS exactly at the reticle on the screen, never passing through) and pull
      the trigger. The button must actually CLICK. Clicks are delivered through uGUI
      `ExecuteEvents`, unconditionally — the `[WorldUI] ClickMode` selector and its
      `virtualmouse` alternative are both gone (user ruling 2026-08-13). Log sequence
      per click: `Under pointer (x,y): 'ButtonName' (canvas '…')` →
      `FlatScreen pointer: trigger PRESS at RT pixel (x,y) …` → on release
      `DirectClick at (x,y) → clicked 'ButtonName'` +
      `FlatScreen pointer: trigger RELEASE … — CLICK (latched)`.
      If `Under pointer` names the WRONG element (or `nothing`), the pixel mapping is
      off — report that log line verbatim. The click latch (always on now; the
      `[WorldUI] ClickLatch` key is gone too) freezes the pointer at the press pixel
      so hand tremor cannot turn the click into a no-op drag.
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
      ScreenDistance = **1.7** m — `Defaults/Defaults.WorldUI.cs:273-274`, which is the
      default of record; both live-tunable).
- [ ] The void around the screen is pure black (a constant; there is no colour key).
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

### 6b. There is no menu-rendering bisection matrix any more

`[WorldUI] FlatScreen`, `[WorldUI] Master` and `[Rig] MenuRig` are all DELETED (user
rulings 2026-08-11 / 2026-08-13). Each of their OFF states was a brick, not a
fallback: no rig meant no head tracking and no anchor for the screen, and no
FlatScreen meant nothing in the menus could be clicked at all. The menu rig and the
flat screen are unconditional, so the only defined menu configuration is the shipped
one. Do not look for a config to bisect a menu-rendering problem with — take it to
the §10 triage table and attach the `Camera inventory` lines.

## 7. Tooltips

- [ ] Hover a converted surface element (initiative avatar, element, objective) with
      the fingertip: the game tooltip appears as a small world panel above your
      fingertip, facing you.
- [ ] It disappears on poke-out. There is no `[WorldUI] Tooltips` switch any more —
      tooltips are always on (user ruling 2026-08-13: OFF left the tooltip canvas at
      its 2D screen position, i.e. nowhere the player can read it).

## 8. Gamepad-mode guard

- [ ] With a gamepad plugged in and used in the menu BEFORE starting VR: once VR is up,
      the log shows `InputModeGuard: switching the game from gamepad to mouse mode`,
      and loading a scenario loads the `Game` scene (log scene name), not
      `Game_gamepad`. **This one has been observed to FAIL on a shipped build** —
      `Board/FigureGrab/FigureClothHands.cs:64-66` records a real ModBuild 286 VR log
      containing `Added scene: Game_gamepad`, because `SceneController` picks that scene
      whenever `InputManager.GamePadInUse`. Treat a `Game_gamepad` here as a finding to
      report, not as a mis-run step.
- [ ] Pressing gamepad buttons during VR does not flip the UI to gamepad mode
      (log: `blocked switch to gamepad mode` — **`VRLog.Debug`, so BepInEx's default disk
      config drops it entirely**. Raise the log level before this step, or judge the step
      by the UI rather than by the log).
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
| `[Core] Stereo policy: '…' forced to StereoTargetEyeMask.None (…)` / `[Core] Stereo policy released — N camera(s) restored to vanilla stereo behavior.` | Game cameras kept out of the HMD, and the restore on VR-off. Owner is `Core/VRCameraPolicy` — note the `[Core]` prefix, not `[WorldUI]`. (This row used to quote a `UICamera '…' excluded from XR rendering` pair that no longer exists.) |
| `FlatScreen quad was destroyed externally — rebuilding.` | A scene swap killed the quad; it self-heals. Frequent repeats = report. |
| `Starting indicator shown (pre-menu scene, FlatScreen gated).` | Intro gate active — FlatScreen deliberately idle during scene 0/`Intro`. |
| `Menu rig built at vantage of camera '…' (…)` | Menu rig camera + clear-color override. HMD **grey** = camera renders, content missing; HMD **black** = camera not reaching the HMD at all. |

**Player.log is still wanted**: the previous report did not include it. Attach
`%USERPROFILE%\AppData\LocalLow\FlamingFowlStudios\Gloomhaven\Player.log` (and
`Player-prev.log`) together with `BepInEx/LogOutput.log` into a dated subfolder of
`.planning/debug/` — Unity-native XR/rendering errors land only there.

## Known limitations (expected, not failures)

- The phase banner no longer blocks 2D input full-screen while converted (its
  fullscreen block image travels with the toast); VR-side input is soft-locked
  instead. Desktop-parallel play during a banner is mildly less protected.
- The intro/splash is desktop-only by design (FlatScreen gate); the HMD shows the
  black void + "starting…" label until the main menu scene loads.
- Keyboard text entry on the flat screen requires the physical keyboard.

---

## 11. P6 (test-#8 follow-up) — UI-shell fixes

Config: `[Rig] Vanilla2DMap`, `[WorldUI] PanelsFollowView`,
`[WorldUI] ManualScreenChordSeconds` (default 2 s; the old `[WorldUI] ManualScreenChord`
on/off and the whole `[SettingsPanel]` section, `ChordHoldSeconds` included, are gone —
the chord is always available and fires on RELEASE). Grep for `[Rig]`, `[WorldUI]`,
`MODAL FALLBACK`, `MANUAL SCREEN CHORD`, `Mode`.

### 11.1 Campaign/world map: the 3D room ships, the flat map is the opt-out

`[Rig] Experimental3DMap` was RENAMED **and INVERTED** to `[Rig] Vanilla2DMap` at
ModBuild 230, and it ships **false** — so the 3D map room, not the flat screen, is
what a fresh install gets on the campaign map. An existing config's old choice is
carried across once (`[Rig] MapPresentationMigrated230`). The room's own checklist is
`TESTING-FULL-LOOP.md` §1b; what belongs HERE is the WorldUI side of both presentations.

- [ ] **Default (`Vanilla2DMap = false`)**: on the world map the HMD puts you AT the
      map (log: the `MAP ROOM ENGAGED.` block). The flat screen is NOT the map
      presentation here — map windows and story pages arrive as floating windows
      through the §11.2 fallback instead. No `VR rig built at focus …` before an
      actual scenario.
- [ ] **Opt-out (`Vanilla2DMap = true`)**: the HMD shows the floating 2D screen with
      the **complete map + UI composite** (no giant 3D map below you, no black hole
      where the map should be), and NO `MAP ROOM ENGAGED` line. Log: `Menu rig built
      at vantage of camera '…'`.
- [ ] (Opt-out) The flat desktop window mirrors the same complete composite (map
      visible), and the map camera appears in the capture log:
      `FlatScreen stack capture: '…' → RenderTexture`.
- [ ] Either way, enter a combat scenario: `VR rig built at focus …` appears only now
      (diorama + panels + bars come up as in §1–§7). Mode log: `Menu2D -> …`.
- [ ] Leave the scenario back to the map: rig tears down (`rig kind change Scenario →
      Menu`), and the presentation you configured comes back — room or flat screen.

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
- [ ] A SHORT **tap** of the same button (press and release quickly) opens/closes the
      game's own pause menu instead — log `OPTIONS TAP: <menu> OPENED (X tap)` /
      `… CLOSED (X tap)`. The mod's separate settings panel is gone; its settings live
      in that window's Options page. The two never fire together: a long hold consumes
      the press, so there is no pause menu after a screen toggle.
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
