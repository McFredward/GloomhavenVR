# Full-loop hardware session — M4 / v0.1 validation script

> The end-to-end pass that gates the v0.1 release: **fresh install → main menu →
> scenario (hero placement) → two full rounds → guildmaster return**, exercising
> every module in one sitting. Run it on Quest 3 over at least one runtime
> (ideally all three: Quest Link, Virtual Desktop/VDXR, Steam Link/SteamVR).
> Per-feature deep checklists live in `TESTING-P1..P4.md` and are referenced per
> station below; failures triage through `TESTING-P1.md` §4 first.
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
- [ ] All config files created under `BepInEx/config/` (`dev.gloomhavenvr*.cfg`, 5 files).
- [ ] (Vanilla guard) Set `[General] Enabled = false`, boot: zero mod log lines after
      the disabled notice, game fully normal. Re-enable.

## 1. Main menu (flat screen + menu rig) — P3c/P5

- [ ] Log: `Menu rig built around camera ...`; the menu view is **head-tracked**
      (lean around — parallax, no frozen viewpoint).
- [ ] The floating 2D screen shows the full menu, readable, following slow head
      turns lazily (recenters past ~45°).
- [ ] Hands visible; dominant-hand laser + reticle on the screen; trigger clicks;
      click-and-drag works (map pan on the guildmaster screen later).
- [ ] Settings chord: hold non-dominant A/X 0.6 s → panel opens in front of you;
      poke a toggle; close with X. (Gear button is scenario-only.)
- [ ] Recenter chord (B+Y both hands, 1 s) re-centers the menu view.
- [ ] Navigate: Guildmaster → party/roster screens all usable on the flat screen.

## 1b. Campaign / world map (Menu2D mask + RT composite) — test #10 fixes

- [ ] On the campaign map the HMD shows **ONLY void + flat screen + hands** — NO
      giant 3D map below/around you. Log: `Menu rig built at vantage of camera
      'MapCamera' (… mask MOD-ONLY 0x… (anchor 0x… NOT copied — test #10) …)`.
- [ ] The flat screen shows the **full map render** (terrain visible, not black)
      plus the map UI on top.
- [ ] Campaign intro video / encounter ("Begegnung") backgrounds: ambient art or
      video visible behind story boxes — not a black field. Log around it:
      `FlatScreen stack capture: 'Video Camera' → RenderTexture (… rect … mask …)`,
      `… fullscreen SolidColor clear DEMOTED to Depth …`, and later
      `FlatScreen stack member 'Video Camera' DISABLED …` when the video ends.
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
      left/right rotates it in 60° steps** with haptic ticks; snap turn does NOT
      fire while targeting (stick contention rule); confirm the attack.
- [ ] Element board / initiative track / combat log panels update live around the
      table; actor HP bars float above miniatures and track damage.
- [ ] Wrist HUD: look at the non-dominant wrist (watch gesture) → HP/XP/gold panel.
- [ ] Short rest: rest token on the tray → game confirmation appears as a
      **world-space modal**; while it is up the laser only shows when pointing at
      the dialog (ModalUI cone gating); poke Yes/No.
- [ ] In-VR settings panel via the SET gear: change snap-turn degrees and world
      scale live; toggle seated mode → immediate recenter; values persist in
      `dev.gloomhavenvr.comfort.cfg` after quitting.

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
