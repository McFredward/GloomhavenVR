# Full-loop hardware session — M4 / v0.1 validation script

> **Current action confirmation (build 530):** select the destination or target, then use
> the board's CONFIRM keycap. Historical second-click action-confirmation steps below
> are superseded; they are not supported VR behavior.

> **Historical hardware checklist.** Retained for source and diagnostic links; this is not
> the current release acceptance list. Use [STATE.md](../.planning/STATE.md) and the latest
> build notes for current behavior and outstanding headset checks.


> **Audited 2026-09-08 at ModBuild 483** (base `49ceab21`). Every `[Section] Key`, log
> marker, type, method and file path named below was grepped against the tree. Config
> keys: no doc here names a live key that has been removed, and every key described as
> DELETED really is gone. What an audit of names cannot establish is that each step's
> expected BEHAVIOUR is still current — where a step was found asserting something the
> code now forbids, it says so in place.

> The end-to-end pass that gates the v0.1 release: **fresh install → main menu →
> campaign map → scenario (hero placement) → two full rounds → guildmaster
> return**. Run it on Quest 3 over at least one runtime (ideally all three: Quest
> Link, Virtual Desktop/VDXR, Steam Link/SteamVR). Per-feature deep checklists
> live in `TESTING-P1..P4.md` and are referenced per station below; failures
> triage through `TESTING-P1.md` §4 first.
>
> **What this script covers**, of the twelve modules the plugin registers
> (`Plugin.RegisterModules`): Core, VREvents, Rig, Hands, Cards, Board, WorldUI,
> Compat — plus the map room (station 1b). **What it does NOT cover** — run these
> separately, they have no station here: the remaining four registered modules,
> multiplayer (**Net**), spatial voice (**Voice**), **SelfUpdate** and **DevModule**;
> and, though they are subsystems rather than modules, MixedReality, WallFade, the mod's
> environments and the ControlsLesson controls tutorial (`[Compat] WallFade` and
> `[Compat] ControlsLesson`). A green run of this script says nothing about any of them.
> (8 covered + 4 uncovered = the twelve; `DevModule` was missing from both lists until
> 2026-09-08, which is how a census of twelve listed eleven.)
>
> Keep `BepInEx/LogOutput.log` after every run — attach it to any issue.

## 0. Fresh install (release artifact)

1. Clean Gloomhaven install (verify game files / remove old `BepInEx/`).
2. Install BepInEx 5.4.23.5 per `INSTALL.md` §1; boot flat once; `LogOutput.log` exists.
3. Extract `dist/GloomhavenVR-<version>.zip` over the game folder (`INSTALL.md` §2).
4. Boot with the headset active.

- [ ] Log shows, in order: `OpenXR runtime assets ready (package 1.10.0)` →
      `Loaded 3 runtime dependencies` → `VR RUNNING on '<runtime>'`.
- [ ] `GH_Data/Plugins/x86_64/{UnityOpenXR,openxr_loader}.dll` and the
      `UnitySubsystems` manifest were created; `install-state.json` next to the
      preloader lists their hashes.
- [ ] A `dev.gloomhavenvr*.cfg` set appears under `BepInEx/config/` — the main
      `dev.gloomhavenvr.cfg` **plus one file per module** that binds its own
      (`…hands.cfg`, `…board.cfg`, `…cards.cfg`, `…worldui.cfg`, `…comfort.cfg`, …;
      the naming is built in `Core/ModuleConfig.cs`). Do not count them against a
      fixed number — a module gains a file the first time it binds one.
- [ ] (Vanilla guard) Set `[General] Enabled = false`, boot: zero mod log lines after
      the disabled notice, game fully normal. Re-enable.

## 1. Main menu (flat screen + menu rig) — P3c/P5

- [ ] Log: `Menu rig built at vantage of camera '<name>'`; the menu view is
      **head-tracked** (lean around — parallax, no frozen viewpoint).
- [ ] The floating 2D screen shows the full menu, readable, following slow head
      turns lazily (recenters past ~45°).
- [ ] Hands visible; dominant-hand laser + reticle on the screen; trigger clicks;
      click-and-drag works (map pan on the guildmaster screen later).
- [ ] Non-dominant A/X **tap** (press and release quickly): the game's own pause
      menu opens, floated in front of you — log `OPTIONS TAP: <menu> OPENED (X tap)`.
      Tap again → `… CLOSED (X tap)`. The mod's settings live in that window's
      Options page now; there is no separate mod settings panel to open.
      A **long hold** of the same button is the flat-screen rescue chord instead
      (`[WorldUI] ManualScreenChordSeconds`, see station 2).
- [ ] Recenter chord (B+Y both hands, 1 s) re-centers the menu view.
- [ ] Navigate: Guildmaster → party/roster screens all usable on the flat screen.

## 1b. Campaign / world map — the 3D MAP ROOM (default)

`[Rig] Vanilla2DMap` ships **false**, and off is the 3D map room: you stand IN the
campaign map. (It was `[Rig] Experimental3DMap`, off by default, up to ModBuild 229;
230 renamed AND inverted it. An existing config's choice is carried across once —
`[Rig] MapPresentationMigrated230`.) The flat 2D map is the opt-out at the end of
this station, not the expected picture.

- [ ] Enter guildmaster / campaign: the HMD puts you at a table-sized parchment you
      can walk around and lean over — NOT a floating 2D screen. Log: the multi-line
      `MAP ROOM ENGAGED.` block (predicate / parchment / scale / seat / eye / mask)
      and, for anything wrong, the `MAP SCENE REPORT`.
- [ ] The main menu is untouched: `MAP ROOM ENGAGED` must NOT appear before a map
      is open (the gate is a live `MapChoreographer`, not "not a scenario").
- [ ] Location icons are **pressable**: poke one, or point the dominant laser at it
      and pull the trigger → log `MAP ROOM location CLICK on '<name>' (<source>)`.
      Hover feedback animates (`[MapRoom] HoverAnimation`).
- [ ] Press the **already-selected** location again → the travel / quest window
      floats with its confirm button (log lines prefixed `MAP TRAVEL CONFIRM:`);
      confirming actually travels / starts the quest.
- [ ] **Table buttons**: the guildmaster option bar (enhance, shop, trainer, map,
      temple, city, town records, mercenary log) stands as physical caps on the
      table rim, with the game's own icons and its highlight pulse. Press each →
      `MAP TABLE BUTTON '<mode>' (<source>)`; the matching game screen opens.
- [ ] **Map-room card hand** (`[WorldUI] MapRoomHand`, on by default): the selected
      character's scenario loadout fans on the non-dominant palm with the SAME
      gesture, animation and grab-to-read as in a scenario, and the wrist plate
      shows that character. Change the selection in the party display → fan and
      wrist follow live. Cards are inspect-only here (no play, no reordering).
- [ ] Windows that open on the map (story pages, quest info, shared map windows)
      float as world-space windows and are clickable by laser AND fingertip.
- [ ] City ↔ world map switch: no teleport/rebuild flicker; the room stays up.
- [ ] Sizes readable: the five `[MapRoom]` dials (`IconScale`,
      `GloomhavenIconScale`, `CityIconScale`, `PartyMarkerScale`, `PathWidthScale`)
      each visibly change what they name, live.
- [ ] Enter a scenario from the map: the room stands down and `VR rig built at
      focus …` appears (station 2). Leave the scenario → the room comes back.
- [ ] **Opt-out**: set `[Rig] Vanilla2DMap = true` and restart the map. Now the
      HMD shows void + flat screen + hands, the screen carries the **full map
      render** (terrain visible, not black) plus the map UI on top, and the room's
      extras (pressable icons, travel confirmation, map-room hand, the `[MapRoom]`
      dials) are all gone with it. Log: `Menu rig built at vantage of camera
      '<name>'`, and NO `MAP ROOM ENGAGED`.
- [ ] (Both presentations) Campaign intro video / encounter ("Begegnung")
      backgrounds: ambient art or video visible behind story boxes — not a black
      field. Log around it: `FlatScreen stack capture: 'Video Camera' →
      RenderTexture (… rect … mask …)`, `… fullscreen SolidColor clear DEMOTED to
      Depth …`, and later `FlatScreen stack member 'Video Camera' DISABLED …` when
      the video ends.
- [ ] Story boxes ('UI Story Box') page through and confirm via laser clicks.

## 2. Scenario start & hero placement — P1/P3a

Start any early scenario (e.g. Black Barrow (campaign) or a guildmaster job).

- [ ] Log: `Menu rig torn down` → `VR rig built at focus ...` → `Hands built`.
- [ ] **Scenario-start story/subtitle box (test #10 lock):** when the quest-intro
      story box ("Untertitel und Geschichte bestätigen" / narration pages) opens,
      the flat screen **rises automatically** and the box is clickable. Log:
      `MODAL FALLBACK poll: story box (StoryController.IsVisible) OPEN` →
      `MODAL FALLBACK ASSERTED …` and, after confirming, `… RELEASED`.
- [ ] **Universal rescue chord:** hold the NON-dominant lower face button (A or X)
      for 2 s at any point in a scenario → flat screen toggles on regardless of
      detection (log: `Manual screen chord ARMING …` then `MANUAL SCREEN CHORD:
      flat screen toggled ON`). Hold again to hide.
- [ ] The board reads as a table diorama (a hex ≈ 15 cm); first recenter placed you
      at the table edge (standing preset, or seated if configured).
- [ ] **Hero placement (CardSelection-mode ray, P5 matrix):** the dominant hand has
      a laser during placement; point at a spawn hex → game highlight follows; with
      `[Board] SnapToHexCenter = true` the reticle snaps to hex centers; trigger
      places the hero. Near-touch placement (finger to the hex) also works.
- [ ] World grab (test #10 free movement, `[Comfort] FreeMovement` default ON): one
      grip drags the table **in every direction — including straight up and down,
      past your head, below your feet; no clamps**; two grips rotate/pinch-scale
      with haptic detents across the widened 0.1×–12× range. Recenter chord (B+Y,
      1 s) returns you to the table edge from ANY position/scale. With
      `FreeMovement = false` the old clamps return (horizontal drag, head above
      table). `TESTING-P4.md` has the deep pass.

## 3. Round 1 — card selection (P3b) + movement (P3a)

Card selection:

- [ ] 2D card hand is invisible (no flat overlay), but the phase runs normally.
- [ ] Palm-up on the NON-dominant hand → cards fan out with live faces; palm-down
      hides them. **No laser shines from the fan hand** while the dominant hand
      keeps its ray (P5 matrix).
- [ ] Grab a card → inspect at natural size; release over the tray → it socketes
      into a slot and the game selects it (check: same card marked in 2D if you
      peek flat). Second card into slot 2.
- [ ] Initiative = slot 0: swap the two cards physically → initiative provably
      swaps (initiative track panel updates).
- [ ] Physical Ready button (table edge) shows the localized label and commits.

Turn (movement):

- [ ] Your turn → the two played cards lie before you; poke top/bottom half →
      action selected (`HalfSelection` per the overlay/log).
- [ ] Move targeting: touch or point-and-trigger a hex; game's second-click
      confirm behaves exactly as with a mouse; **Undo** (physical button) works.
- [ ] Enemy stat panel: poke an enemy miniature → its stat panel opens as a world
      panel near the miniature (P5 `MiniaturePoked`).

## 4. Round 2 — attack + AoE (P3a) + rest + modals (P3c)

- [ ] Select an AoE attack; pattern follows the hovered/touched hex; **thumbstick
      left/right rotates it in 60° steps** with haptic ticks — on the hand `[Comfort]
      TurnHand` does NOT use. **Snap turn keeps working throughout**: this step used to
      say it "does NOT fire while targeting (stick contention rule)", and ModBuild 138
      reversed exactly that (*"Die drehung soll nie blockiert sein!"*). A turn that
      refuses to fire here is the defect. Confirm the attack.
- [ ] Element board / initiative track / combat log panels update live around the
      table; actor HP bars float above miniatures and track damage.
- [ ] Wrist HUD: look at the non-dominant wrist (watch gesture) → HP/XP/gold panel.
- [ ] Short rest: rest token on the tray → game confirmation appears as a
      **world-space modal**; the laser stays visible wherever the hand points (the
      old ModalUI cone gate is retired — `[Hands] ModalRayConeDegrees` no longer
      exists) and clamps to the dialog when it crosses it; poke or trigger Yes/No.
- [ ] Settings via the non-dominant A/X **tap** — under 0.35 s, which opens the game's
      own options window where the mod's settings live as a VR row. (This step used to
      offer "the tray's SET gear" as the alternative; there is no gear cap and no
      separate mod settings panel — see `TESTING-P3B.md`.) Change
      `[Comfort] SnapTurnDegrees` and `TurnHand` live — the very next stick flick
      obeys them; values persist in `dev.gloomhavenvr.comfort.cfg` after quitting.
      (There is no seated-mode switch and no table-scale slider any more: the
      recenter seat is a single standing preset and the pinch-zoom owns the scale.)

## 5. Scenario end → guildmaster return

- [ ] Finish (or abandon) the scenario; results/rewards screens usable on the
      flat screen; log: `VR rig torn down` → `Menu rig built`.
- [ ] Level-up / merchant screens (if offered) usable via flat screen + pointer.
- [ ] Start a second scenario from there — rig/hands rebuild cleanly (no leaked
      panels, no double lasers, cards work again). This is the loop that catches
      teardown bugs.

## 6. Performance observation (MultiPass expectations)

The mod ships **MultiPass** stereo (each eye rendered separately) — the
compatibility-first choice; expect roughly 2× draw-call cost vs flat.

Record (per runtime):

| Metric | How | Expectation / what to report |
|---|---|---|
| Frame rate & reprojection share | runtime overlay (ODT/Link HUD, VD overlay, `SteamVR` frame timing) | stable 72/80/90 Hz on a mid PC in scenario view; note % reprojected during two-grip scale (worst case) |
| GPU frametime, scenario idle vs targeting vs card fan open | same overlay | fan open adds per-card canvases — report any step > 2 ms |
| PPv2 kill-switch delta | toggle `[Compat] DisablePostProcessing` (restart) | report frametime delta and any visual artifacts with PP ON (bloom/vignette under stereo) |
| Volumetric fog | `[Compat] DisableVolumetricFog = false` | known stereo hazard — screenshot artifacts if seen |
| Stereo anomalies | eyeball | outline/highlight effects (EPOOutline), billboards, water: one-eye rendering or swimming — screenshot + scene name |
| Load hitches | scenario load, first fan open | one-time hitches OK; recurring per-round hitches are bugs |
| Memory | Task Manager after 30 min | report growth > ~1 GB over the session (leaked RTs/panels) |

Report format: runtime + PC specs + the table above + `LogOutput.log`. SPI
(Single-Pass Instanced) is a post-v0.1 experiment — do not enable expectations
around it.

## Exit criteria (v0.1 gate)

Every checkbox above on at least one runtime, no flat-screen fallback needed for
any in-scenario action, and the performance table filled in. File deviations as
issues tagged `hw-pass` with the checkbox ID.
