# `.planning/` — what to read, and what is finished

> **Audited 2026-09-08** against `dev` = `49ceab21` (**ModBuild 483**). Every verdict below is a
> snapshot taken on that date and against that tree; two builds is enough to make a line stale, so
> re-check before acting on one. This file covers **`.planning/*.md` at the top level only** — the
> 51 files that were there before this one. Every one of them has exactly one row below.
> Subdirectories are summarised in §6.

There are 51 markdown files at the top of this directory and a successor cannot read them. Most are
**the record of one finished round** — valuable as archaeology, worthless as instruction. This index
says, per file, which is which.

---

## 0. READ THESE, IN THIS ORDER — then stop

The latest multiplayer follow-up is [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md), documenting
native capture/send/playback and independent section refresh optimization. It distinguishes
production-harness evidence from the still-owed full-party headset performance test. The dated
inventory below remains historical; `.planning/STATE.md` and the newest build notes carry the
current status.

| # | File | Why, in one line |
|---|---|---|
| 0 | **`CLAUDE.md`** (repo root) | **Written after this audit, so it is not classified below.** The rules that do not change per build: who the user is and that he is answered in German, where the truth lives per question, the ten hard rules, the three gate commands with their current readings, the working practice that keeps parallel lanes from destroying each other, and the ways this project has actually lost time. It exists because the agent memory lives OUTSIDE the repository and does not travel with it. |
| 1 | **`src/GloomhavenVR/Net/NetProtocol.cs`**, the comment block from `// Build 483:` downwards | **Not in `.planning/`, and that is the point.** Those build notes are newest-first and are the current registry of what happened per build. Recent truth lives there, not here. Read the newest 15–20 entries and you know the state of the mod. |
| 2 | **`.planning/STATE.md`** | Where the project stands, what is owed, what HE has to judge, and the shape of a round. **Rewritten 2026-09-08, after this audit was taken.** The 275 KB file this index was written against is now `STATE-ARCHIVE-through-2026-08.md` — it was current as of ModBuild 315 and its gate list, record census and backlog are all wrong; it is kept for its round-by-round narrative alone. Integrator-owned; do not edit it from a lane. |
| 3 | **`docs/DEVELOPING.md`** | Repo layout, the folder-is-not-namespace rule, the five path pins, and — §Gates — **the authoritative list of the ~20 verification gates**. Any gate list you find inside a `.planning/` round record is stale by construction. |
| 4 | **`.planning/ARCHITECTURE.md`** | Why the modules are cut where they are, plus the verified-foundation table (Unity 2021.3.5f1 / net472 / BepInEx 5.4.23.5, and the six golden seams the whole mod hangs off). |
| 5 | **`.planning/refactor-2026-09/BRIEF.md`** + its **`NEEDED-OUTSIDE-*.md`** | The refactor programme that just landed (ModBuild 481–482). `BRIEF.md` states the rules every lane follows. The six `NEEDED-OUTSIDE-*.md` beside it are the **newest cross-lane handoffs and the likeliest place an item is still genuinely open** — by the brief's own rule a merge whose halves straddle two lanes is written up as a finding, not committed. Start any "what is left to do" question there, not in this directory. |
| 6 | **`.planning/INDEX.md`** (this file) | Everything else, classified. Read §1 (LIVE) and §4 (PARKED). Skip §3 unless you are working on that exact subsystem. |

**One thing that will bite you before anything else:** the 2026-08 and 2026-09 refactors moved
hundreds of files into named subfolders and renamed the plugin entry point. Almost every
`src/GloomhavenVR/...` path written in a record dated before 2026-09-08 is **pre-refactor and will
not resolve**. See §5 for the translation — it is mechanical, and a dangling path does not make the
record wrong.

---

## 1. LIVE — guidance that still governs

| File | What it decides | Checked |
|---|---|---|
| `PROJECT.md` | The product definition: the Demeo-like vision and requirements **R1–R5**. The only statement in the repo of what this mod is *for*. **Corrected 2026-09-08** — its §Constraints said *"multiplayer compatibility is a non-goal for v1"*, which the standing ruling reverses; the old text is quoted in place so the change is legible. | 2026-09-08 |
| `ARCHITECTURE.md` | Why the modules are cut where they are, plus the verified engine/loader/seam facts. Deliberately delegates the file tree to `docs/DEVELOPING.md` and says so. **Corrected 2026-09-08** — its §9 carried the same *"v1 targets single-player"* claim as `PROJECT.md`. Its §10 risk table is a 2026-07 snapshot and reads as history, not as a live register. | 2026-09-08 |
| `doorway-fade-experiments.md` | **STANDING RULING (user, 2026-08-02, supersedes all previous doorway rulings): archway/doorway segments NEVER fade.** `WallSegmentFade` keeps recognition only, so an archway renderer can never merge into a fadeable segment. Also records three failed experiments so they are not re-run. | 2026-09-08 |
| `haunt-events-design.md` | **Unbuilt design, still owed.** Four replacement apparitions (E1–E4) for ones deleted at ModBuild 149 or rejected outright. The shipped roster is still the four *survivors* — `Loc.cs` `h_vr_o_haunt` names window face, stair shaft, eyeshines, watcher — so nothing in its §3 has been built. Its §2 (animation levers: `SampleAnimation` **no**, null controller + Playables **yes**, freeze the animator **free**) is the reusable part. | 2026-09-08 |
| `VOICE-SPATIAL.md` | Spatial voice **shipped** (`src/GloomhavenVR/Voice/`, six files) but the file's own caveat still stands: **never run on hardware and never run with a second client.** That verification debt is open. Also carries the standing ruling *never patch `ScenarioRuleLibrary`, Photon Bolt or `FFSNet.NetworkManager`*. Paths are pre-refactor (§5); banner added. | 2026-09-08 |
| `deadlock-class-audit.md` | **User ruling, 2026-09-03: *"Das Problem hat allerhöchste Priorität — es darf keine Deadlocks dieser Art geben."*** A ruling about a CLASS, not about the four instances that had been fixed one at a time. The audit body is a snapshot at ModBuild 371 and says so; the ruling is not. | 2026-09-08 |
| `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` | **The one lane handoff with an item still open.** Its §2 asks `Cards/Piles/PileViewer.PileStack.Create` to call `NativeButtonSkin.StyleWorldReadableLabel` on the three pile captions. **Verified 2026-09-08: it still does not** — `PileViewer.cs:1029,1040` call only `ApplyFont`, while the item-use caption whose doc comment says it deliberately matches them *did* get the keyline (`Cards/Tray/PlayTray.4.Slots.cs:662`). The pairing is half-applied. §3 (bundle rebake) and §4 (ModBuild bump) are long since consumed. | 2026-09-08 |
| `wave2-research.md` | Kept for **§3 — the asset list with per-asset licence clauses verified at source on 2026-08-15**, including the "searched and empty" and "rejected, with the clause that killed each" lists. That negative half is what stops a future round re-running the same search. Its draught and handprint recommendations were acted on (`unity/GloomhavenVR.Assets/Assets/Editor/BuildEnvironmentRooms.cs`, `AddCellarDraught`). | 2026-09-08 |
| `redundancy-audit.md` | The "one concept, built twice" survey, 2026-09-05 @ ModBuild 437, answering *"genau so etwas will ich im gesamten Mod so gut es geht vermeiden"*. **Partly consumed** — the 2026-09 refactor collapsed seven duplications and left three that cross a lane boundary. A live backlog with a stale head count; banner added. | 2026-09-08 |
| `settings-audit.md` | The settings user-friendliness pass and the user's four questions of 2026-08-22 (delete dials that can break the game; anything you would not trust a user with goes to Erweitert; re-categorise; a slider is not always the right control). **Those rulings are still policy.** The concrete menu tree it proposes is superseded; banner added. | 2026-09-08 |

---

## 2. STALE — describes a state the tree has left

Each of these carries a dated banner at its head naming what superseded it. They are kept, not
deleted: the *reasoning* is still the best account of why the shipped design looks the way it does.

| File | Why it is stale | Banner |
|---|---|---|
| `worldmap-3d.md` | Head says **"PLAN ONLY. Nothing in `src/` was changed."** The plan shipped: `src/GloomhavenVR/WorldUI/MapRoom/` holds 22 files. Read it as the design rationale for a built feature, not as a proposal. Its standing-ruling quotes (no UI that re-anchors as the head turns; game-asset environments abandoned) are still correct. | 2026-09-08 |
| `map-mp-and-shared-windows.md` | Head says **"DESIGN ONLY. Nothing under `src/` was changed."** It shipped — `Net/Remote/{RemoteMapRoom,RemoteStorySync}.cs`, `WorldUI/Modal/SharedWindows.cs`. Its baseline is ModBuild 219 (HEAD is 483) and its §F.1 complaint that `STATE.md` says "ModBuild 148" is itself 264 builds out of date. | 2026-09-08 |
| `WINDOW-MATERIALISE.md` | Head says **"committed on the lane branch"** and negotiates ModBuild 293 vs 294. The lane landed: `src/GloomhavenVR/WorldUI/Materialise/` holds five files including a `WindowMaterialiseVisibility.cs` that post-dates the document. | 2026-09-08 |
| `game-env-assets.md` | A 2026-08-12 feasibility investigation whose head reads as an open question. **The programme it enabled was abandoned by user ruling on 2026-08-13** — read `game-env-postmortem.md` (§4) first, then this only for the Apparance/Addressables mechanics. | 2026-09-08 |

---

## 3. CLOSED — the record of a finished round

Archaeology. Each answered a specific report on a specific ModBuild and shipped. **Do not read one
unless you are working on that subsystem** — and when you do, read it for the *reasoning*, never for
the paths, the gate numbers or the build numbers.

| File | What it closed | When |
|---|---|---|
| `ROADMAP.md` | Phases P0–P5. Already carries its own HISTORICAL banner. | pre-ModBuild 92 |
| `round-149.md` | The 17-item hardware round (fire, sound, UI, elements) — the index of that round's lanes. | 2026-08-15, MB 148→149 |
| `fire-loop-measurement.md` | The fire's visible loop. Carries its own supersession note pointing at `fire-wind-sparks.md`. | MB 150 |
| `fire-wind-sparks.md` | Deleted the wind-on-flame streaks in favour of blown sparks; the round that established *delete a term's frequency along with its amplitude*. | MB 151 |
| `perf-zoomed-out.md` | Zoomed-out overview framerate: measurement plus strategy set, every claim marked PROVEN or INFERRED. | 2026-08-09, MB 102 |
| `OPTIONS-MENU-NEVER-BLOCKED.md` | The ModBuild 289 defect that broke *"es MUSS immer möglich sein das Optionsmenu zu öffnen"* by blocking the **show** rather than the **click**. The ruling itself lives in `WorldUI/Patches/SettingsClickExemption.cs`. | 2026-08-25, MB 289 |
| `BLUE-WINDOWS-SINGLEPLAYER.md` | Scenario shared-window spawn height, and no blue multiplayer windows in singleplayer. | 2026-08-25, MB 289 |
| `BOARD-SWITCH-POSE.md` | A board switch losing its size (world scale 6.865 → 2.129 across one switch). | 2026-08-25, MB 289 |
| `BOARD-BUTTON-OVERHAUL.md` | Round 6 of the control-board buttons — the first the user said he liked. 131 KB; the durable part is the measurement method, not the result. | 2026-08-25 |
| `BOARD-REBUILD-HANDOVER.md` | Rounds 1–5 of the control-board mesh and texture rebuild. **Its "GATES — every one, every time" block is materially stale** (8 gates listed, ~20 exist; 146 857 assertions, 78 classes / 130 methods, "EXACTLY 6 warnings" against named files). Banner added; `docs/DEVELOPING.md` §Gates is the authority. | 2026-08-25 |
| `WALL-CRYSTAL-SEGMENT.md` | The wall section with crystal in it that would not fade (`sollte_faden.jpg`). | 2026-08-25, MB 290 |
| `BOSS-FIGURE-REACH.md` | Boss dragon: health bar through the face, and a figure grabbable only below it. | 2026-08-25, MB 290 |
| `EMPTY-WINDOW-LIVENESS.md` | The DARK verdict now hides rather than tears down; `PartyPanel` vanishing for seconds after a map spawn. | 2026-08-25, MB 291 |
| `CLOTH-DURING-SCALE.md` | Capes going stiff, then polygon-mush, during a figure resize — the round where ModBuild 290's "impossible" was refuted by the very counter 290 had shipped to check it. | MB 291 |
| `CONFIG-SCALE-COLLAPSE.md` | Adopting the user's 54-value tuned cfg drop as shipped defaults, and the 1000×-too-fine step the rebase exposed. | 2026-08-26, MB 293 |
| `CELLAR-WINDOW-VIEW.md` | The wood seen through the cellar window, and its animal calls localised to the window at the wood's own intensity. | MB 296 |
| `CELLAR-WINDOW-AND-ENTRANCE.md` | Deeper trees at the cellar window; an entrance that is no longer a perfect rectangle. Geometry only, in `Assets/Editor/BuildEnvironmentRooms.cs`. | 2026-09-02 |
| `FOREST-CLOUDS.md` | Thin night cloud over the forest clearing, with "never dense" and "never fully covers the moon" proven as numeric bounds rather than asserted. | 2026-09-02 |
| `HARDWARE-2026-09-02.md` | The hardware round that **could not answer its own backlog**, because the evidence sat below the shipped log level. Worth reading as method even if you never touch those subsystems. | 2026-09-02, MB 334 |
| `menu-findability-2026-09-02.md` | Cheats behind a cfg gate (default off), sub-menu back-navigation from the left rail, and line-break quality in tab labels. | 2026-09-02, MB 340 |
| `MENU-NEVER-DISTURBS-PLAY.md` | *"kein Optionsmenu — auch nicht die VR Optionen — darf den Spielfluss stören."* The ModBuild 340 defect **plus** the audit of every "a modal is open" gate in the mod, with a verdict per site. | 2026-09-02, MB 340 |
| `variant-tiles.md` | The three settings that became picture-tile strips (environment / hands / masks). | 2026-09-02 |
| `LANE-MODAL-351-NEEDED-OUTSIDE.md` | Item 12, the cut-off instruction placard. **Its fix was right and its cause was wrong**; corrected by `LANE-PICKBANNER-356`. | MB 351 |
| `LANE-PICKBANNER-356-NEEDED-OUTSIDE.md` | The real cause of item 12: `PickBannerTextMaxBytes = 96` cut a 107-byte composed German line. **Applied** — the constant is 160 today (`NetProtocol.cs:20746`). | MB 356 |
| `LANE-PROPS-357-NEEDED-OUTSIDE.md` | Held-prop resize lane. Every item was explicitly OPTIONAL; no gate depended on any of them. | MB 357 |
| `LANE-PROPINFO-357-NEEDED-OUTSIDE.md` | Held-prop info card. Its item 3 — a doc comment prescribing a fix that had already shipped — **was addressed**: the concession note was re-worded at ModBuild 366 (`GrabbableProp.cs:1304`). | MB 357 |
| `LANE-QUITDUNGEON-357-NEEDED-OUTSIDE.md` | The 2026-09-03 "Quest verwerfen" deadlock; asked only for the ModBuild bump. **Landed** — `CanvasConversion.1.Core.cs:268,519`. | 2026-09-03, MB 357 |
| `LANE-MAPHOVER-363-NEEDED-OUTSIDE.md` | `[MapRoom] HoverAnimation` (default `false`) and its two Loc keys. **All three landed** — `Plugin.cs:704`, `Loc.ConfigNames.cs:296`, `Loc.ConfigDescriptions.German.cs:1289`. | 2026-09-03, MB 363 |

---

## 4. PARKED — deliberately abandoned, and these exist so you do not re-open them

**Read the whole of §4 before proposing anything.** Each is a decision the user made, with the
evidence that produced it. Re-opening one without new information is the most expensive mistake
available in this repo.

| File | What was tried, and why it stopped | Ruled |
|---|---|---|
| `static-batching-removed.md` | Static batching ("Bündelung") caused invisible / wrongly-uncovered rooms across **eight rounds**. Parked at ModBuild 15, then **fully deleted** — code, call sites, config and the entire settings surface. **Recovery is documented: commit `8e6f8e8` is the last with the complete working implementation, and the file carries the per-file inventory to restore from it.** Its `src/` paths are therefore *deliberately* dangling. | user, 2026-08-02 |
| `wall-fade-stereo-rivalry.md` | A wall can be discarded in one eye and solid in the other at one distance/angle band. Understood down to the shader instructions and **not fixable on the game's masonry shader without losing the approved dissolve look.** Instrumentation reverted; ModBuild 79 renders exactly like 77. | user, 2026-08-08 |
| `startup-freeze.md` | The post-intro freeze was measured and partly addressed at ModBuild 108. The remaining lever is a bundle rebuild and **the user declined it on 2026-08-11. Do not re-propose it without new information.** | user, 2026-08-11 |
| `game-env-postmortem.md` | Rebuilding the ambient rooms from the game's own Apparance / map assets — five hardware rounds, ModBuilds 127–131. *"lösche bitte das alles wieder … Gehe wieder dazu über mit custom assets etwas zu bauen. Aber nicht low-poly sondern zum Stil des Spiels passendes."* The chronology of what failed, and why, is the point. | user, 2026-08-13 |
| `scene-interactables-PARKED.md` | Hand-reactive scene props. **Parked mid-survey — no code was written and no census instrument exists.** Its finding survives: the scene dressing (vines, banners) is the *worst* candidate, and its §7 says why no row in the table is settled. *"Ok belassen wir es vorerst bei den Figuren."* | user, MB 287 |
| `ATTACK-MODIFIER-DECK.md` | The attack-modifier deck as a physical deck you reach for. Feasibility pass only — **no code, nothing measured on hardware.** The rules model (`CMonsterAttackModifierDeck`, with `LastDrawnAttackModifierCards` as the read-only presentation hook) is complete and readable; the file names the one thing that blocks it. | user, 2026-08-30 |
| `hand-interaction-PARKED.md` | Hand interaction with scene VFX and hangings, ModBuilds 430–432; a follow-up lane removed the code. *"Das ganze System macht zu viele Probleme … entferne jegliche FX-Interaktion außer die erste mit dem Stoff die schon implementiert war."* Written to be **resumable**: every derived number, every defect paid for once, every hazard. | user, 2026-09-05, MB 432 |
| `held-prop-flash-experiments.md` | The held prop's flash ("Aufleuchten"). **Four rounds failed**; the user chose suppression over correctness — *"ich will erstmal, um es einfach zu halten, diese Animation gar nicht mehr in der Hand haben"*. 251 KB, the largest file here: read §6 (what shipped) and note that §12 is explicitly superseded by §13. | user, 2026-09-06 |

---

## 5. The one systemic staleness — file paths

Two refactor programmes moved the tree under every record written before them: **2026-08** moved 251
files into named subfolders, and **2026-09** (ModBuild 481–482) moved more and rewrote
`docs/DEVELOPING.md` around the result.

**The translation is mechanical — insert the new subfolder.** Verified 2026-09-08:

| written in a record | today |
|---|---|
| `Core/WallSegmentFade*.cs`, `Core/WallStandingProp.cs` | `Core/WallFade/…` |
| `Core/Loc*.cs` | `Core/Loc/Loc*.cs` |
| `Core/{SkyAlternative,ApparanceDetailFocus}.cs` | `Core/Environment/…` |
| `Core/BundleDiagnostics.cs` | `Core/Diagnostics/…` |
| `Core/PerfSceneProfile.cs` | `Core/Perf/…` |
| `WorldUI/{SharedWindows,ModalFallback.*}.cs` | `WorldUI/Modal/…` |
| `WorldUI/{PanelPlacement,CanvasConversion.*}.cs` | `WorldUI/Conversion/…` |
| `WorldUI/{GrabbableModal,PanelGrab}.cs` | `WorldUI/Grab/…` |
| `WorldUI/{OptionsToggle,ConfigCatalog}.cs` | `WorldUI/Options/…` |
| `WorldUI/WindowMaterialise*.cs` | `WorldUI/Materialise/…` |
| `Net/NetAvatarDriver.cs` | `Net/Avatar/…` |
| `Net/Remote*.cs` | `Net/Remote/…` |
| `Cards/BoardEngraving.cs` | `Cards/Caps/…` |
| `Cards/PlayTray.*.cs` | `Cards/Tray/…` |
| **`GloomhavenVR.cs`** | **`Plugin.cs`** — the entry point was renamed. |

**Genuinely gone, not moved** (verified 2026-09-08): `Core/StaticBatch*.cs` and `StaticBatcher.cs`
(deleted on purpose, §4), `Hands/SceneryActors.cs` (parked, §4), `WorldUI/Modal/ParkWatchdog.cs`,
`Board/FigureGrab/PropAnimWatch.cs` and `PropAnimBelt.PositionSweep.cs` (spent probes, removed).

The same debt exists inside the **source comments** and is tracked separately in
`.planning/refactor/STALE-DOC-REFS.md` — seven remaining `<see cref>` sites naming symbols that no
longer exist (29 of the original 36 were cleared by the 2026-09 refactor).

---

## 6. Subdirectories

| Directory | What it is |
|---|---|
| `refactor-2026-09/` | **The newest programme, just landed, and the first place to look for open work.** `BRIEF.md` first, then the `REVIEW-*.md` lanes, then the six `NEEDED-OUTSIDE-*.md` — the current cross-lane handoffs, none of which was committed by the lane that wrote it. |
| `refactor/` | The 2026-07 charter and the 2026-08 programme. `CHARTER.md` and `PLAN-2026-08.md` **still govern**. `STALE-DOC-REFS.md` is the live cref debt (§5). `MENU-STRUCTURE.md` is the current menu-category rationale and supersedes the tree in `settings-audit.md`. |
| `review-2026-09-07/` | The five read-only review lanes of ModBuild 480 — 3 045 lines committed *before* a line of `src/` was touched, so the findings survived the build that acted on them. |
| `menu-audit/` | Five-part menu census feeding `refactor/MENU-STRUCTURE.md`. |
| `cellar-window/`, `cloth-cook-harness/`, `envsound-replica/`, `figures/`, `forest-clouds/`, `hand-reorder/`, `items/`, `multiplayer/`, `perf/`, `research/`, `voice/` | Per-subsystem working notes and offline harnesses. Enter one only when working on that subsystem. `research/` holds eleven pre-build reports, among them the five (`TOOLCHAIN`, `VR-PRIOR-ART`, `CARDS`, `BOARD-INPUT`, `UI-ARCH`) that `ARCHITECTURE.md` was synthesised from. |
| `debug/` | **Gitignored.** Hardware logs, screenshots, and the user's dropped `.cfg` files. A dropped cfg is always measured against the newest build and is taken verbatim. |

---

## 7. Counts

| verdict | files |
|---|---|
| LIVE | 10 |
| STALE (banner added 2026-09-08) | 4 |
| CLOSED | 28 |
| PARKED | 8 |
| reserved — `STATE.md`, integrator-owned | 1 |
| **total** | **51** |

Four further files are LIVE or CLOSED but also carry a dated banner because one section of them
misleads: `settings-audit.md`, `redundancy-audit.md`, `VOICE-SPATIAL.md` and
`BOARD-REBUILD-HANDOVER.md`. They are counted under their primary verdict.

---

## 8. What this audit changed, 2026-09-08

Nothing was deleted — this project keeps its records, and a closed round's reasoning is why nobody
re-tries a failed approach. Four commits, `.planning/*.md` only, no `src/` change and no `ModBuild`
bump:

1. **This file.**
2. **A dated banner on eight files whose first paragraph had gone false** — `worldmap-3d`,
   `map-mp-and-shared-windows`, `WINDOW-MATERIALISE`, `game-env-assets`, `BOARD-REBUILD-HANDOVER`,
   `settings-audit`, `redundancy-audit`, `VOICE-SPATIAL`.
3. **A consumption note on all seven `LANE-*-NEEDED-OUTSIDE.md`.** Each opened by saying its items
   were NOT applied — true when written, false for six of them now. Each was checked against the
   tree; the seventh (`LANE-BOARDTEXT-357` §2) is genuinely still open and now says so.
4. **Two sentences corrected in `PROJECT.md` and `ARCHITECTURE.md`** that ruled multiplayer out of
   scope, plus a path fix in `doorway-fade-experiments.md` and a pointer in `ROADMAP.md`.

**Not audited, by scope:** `STATE.md` (integrator-owned), `refactor/**`, `refactor-2026-09/**`,
`docs/**`, `README*`, `INSTALL*`, `src/**`, `scripts/**`.

**One finding for whoever owns the code:** `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` §2 has been open
since ModBuild 363. `Cards/Piles/PileViewer.cs:1029,1040` call `NativeButtonSkin.ApplyFont` on the
three pile captions but never `StyleWorldReadableLabel`, while `Cards/Tray/PlayTray.4.Slots.cs:662`
— the caption whose doc comment states it is built to match those three exactly, *"the same muted
parchment colour, the same native HUD font, and the SAME fit box and font ceiling"* — does call it.
The three pile captions hang over the same scenario ground and have the same legibility defect. It
is one line, and it is a finding here rather than a commit because this lane does not touch `src/`.
