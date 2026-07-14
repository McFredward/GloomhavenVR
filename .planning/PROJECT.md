# GloomhavenVR — Immersive VR Mod for Gloomhaven (Digital)

## Vision

Turn Gloomhaven (digital, Unity Mono, Flaming Fowl Studios) into a fully immersive
room-scale VR tabletop experience in the spirit of **Demeo** — not a flat-screen
camera port, but native-feeling motion controls:

- The scenario map becomes a **diorama on a virtual table** you stand at, grab,
  rotate, and zoom with your hands.
- Your **ability-card hand fans out physically** when you turn your palm toward
  you; you grab a card with the controller, inspect it, and **play it by placing
  it on a play surface**.
- Card halves (top/bottom action), buttons, and other controls are **pressed
  with your virtual finger** — reinjected as physical 3D elements.
- Hex targeting (movement, attacks, AoE) happens by **touching the board**.

Loaded via **BepInEx**, patched via **Harmony**, no game files modified.
Primary target: **Meta Quest 3 over PC (Link / SteamVR / OpenXR)**.

## Core Requirements

### R1 — Stereo VR rendering + head tracking
- Full 3D stereo rendering of the existing scene (not a flat screen in VR).
- Head-tracked camera replacing/overriding the game's Cinemachine camera rig.
- Comfortable table-scale presentation; world grab/zoom/rotate.

### R2 — Hand presence & motion controls
- Visible hand models driven by Quest 3 controllers (trigger/grip/thumb poses,
  capacitive touch where available).
- Finger poses react to controller input (point, grab, open palm).

### R3 — Physical card hand (Demeo-style)
- Palm-up gesture reveals the current ability-card hand as 3D cards.
- Cards are grabbable, inspectable up close, and playable by placing them onto
  a designated surface (card selection: 2 cards per round + initiative choice,
  rest actions included).
- Card faces rendered from the game's real card assets/UI.

### R4 — Physical UI interaction
- Card-area choices (top vs bottom action) selectable by finger-poke on the
  physical card / world-space UI.
- Key in-scenario controls (end turn, confirm, undo, element board, initiative
  tracker, character HUD) reachable as world-space/physical elements.
- Remaining 2D screens (menus, merchant, level-up) usable on a floating
  world-space screen with pointer interaction (acceptable fallback).

### R5 — Board touch selection
- Hex/tile selection (move target, attack target, AoE placement, doors, chests,
  loot) by physically touching/pointing at the board with the virtual hand.
- Hover feedback reuses the game's own highlight system.

## Constraints

- **Loader:** BepInEx (version per toolchain research), **patching:** Harmony.
- **No game-file modification** beyond what BepInEx requires; all assets shipped
  by the mod (AssetBundles).
- Game assemblies: `GH.Runtime.dll`, `GH.Shared.dll`, `ScenarioRuleLibrary.dll`,
  `MapRuleLibrary.dll` (no `Assembly-CSharp.dll`). Decompiled reference source
  in `decompiled/` (gitignored).
- Rules engine must stay untouched — we patch **UI/input/camera seams only**,
  never game logic (multiplayer desync + correctness).
- Single-player focus first; multiplayer compatibility is a non-goal for v1.

## Key References

- Design model: **Demeo** (Resolution Games).
- Prior art: TarkovVR/SPT-VR, LCVR & RepoXR (DaXcess), UUVR/NomaiVR/TwoForksVR
  (Raicuparta).
- Research reports: `.planning/research/*.md`.

## Working Agreements

- GSD workflow; phases tracked in `.planning/ROADMAP.md`.
- Parallel work on independent feature branches (`feat/<area>`), merged by the
  orchestrator; `main` stays buildable.
- Game DLLs & decompiled sources stay out of git (`.gitignore`).
