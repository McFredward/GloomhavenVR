# State — where the project stands

**Updated 2026-09-14 for the map story/loadout hotfix, ModBuild 500 (version 1.0.0).** The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which planning records are current or historical).

---

## 1. Position

- **1.0.0 / ModBuild 500 fixes a map preparation softlock.** Current offline 499 logs show
  native battle goals opened under a party root still refused by frozen story-curtain
  membership. The native loadout's released hide request now admits its original root and
  descendants. VR map input and offline travel also honor the native map lock; online quest
  readiness retains its own visibility/state rule. No game state is forged to escape.
  See [MAP-500.md](MAP-500.md) and its evidence reports. Headset replay remains pending.
- **500 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and prior
  production suites; new map-flow harness 757 assertions with five negative controls.
  Strict Release has zero warnings/errors; bilingual docs pass. Reviewed compiled scope:
  11 changed types (including seven build-constant-only changes), two new helpers, no removal.
  Config/patch surfaces remain 625/152; log markers increase 4,717 to 4,718.
- **500 retains version 1.0.0 and is integrated on dev.** The existing main/tag `v1.0.0`
  still identifies build 498; no release assets or tags are changed by this hotfix.
- **CI storage policy (2026-09-13):** normal pushes/PRs no longer upload DLL artifacts.
  Manual CI on dev can request a tested download; serialized cleanup retains at most three
  builds for two days. Release ZIP publication on main remains mandatory and unchanged.
  Version 1.0.0 / ModBuild 499 runtime is unchanged. See [CI-STORAGE.md](CI-STORAGE.md).
- **1.0.0 / ModBuild 499 fixes an unintended fallback screen during remote long-rest burns.**
  Current 498 logs show the opaque desktop composite appearing while the remote board stays
  active; older 491 evidence has the same signature. Native foreign-hand UI locks were outside
  the local burn guard. The new guard checks every actual lock owner and preserves explicit
  screen requests. Original card/board presentation is unchanged. Headset confirmation remains
  pending. See [REST-499.md](REST-499.md) and its linked evidence reports.
- **499 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and existing
  production suites; new modal harness 1,058 assertions plus five negative controls. Strict
  Release has zero warnings/errors. Compiled scope is the fallback fix, its new helper and
  version constants. Config/patch surfaces are unchanged; one diagnostic was added.
- **499 remains version 1.0.0 at the maintainer's request.** The already published main/tag
  `v1.0.0` identifies build 498; this hotfix does not rewrite that tag or replace its assets.
  A later release publication must come from `main` and deliberately handle that existing tag.
- **1.0.0 / ModBuild 498 release authorized on 2026-09-10.** Prepared on `dev` for the
  main-triggered release pipeline. Gameplay and presentation carry build 497 unchanged; the
  full package includes the reviewed build 483 bundle and repository-readiness fixes. See
  [RELEASE-1.0.0.md](RELEASE-1.0.0.md) for candidate verification and publication status.
  The maintainer handles public visibility and the in-headset update test separately.
- **1.0.0 repository preparation (2026-09-10):** current guides and CI instructions reconciled,
  historical references clearly marked, generated logs/renders and shader disassembly kept local,
  installer/uninstaller edge cases fixed, stale local bundle overrides prevented, release notices packaged. Version and runtime
  remain 0.9.1 / ModBuild 497. See [RELEASE-READINESS.md](RELEASE-READINESS.md) for validation
  and separate publication follow-ups. No release, tag, main-branch push or visibility change
  is part of this preparation. The maintainer explicitly deferred the fire-asset license question.
- **Previous development version: 0.9.1, ModBuild 497.** Release 0.9.0 (494) was published from `main` by
  [Release run 34407935479](https://github.com/McFredward/GloomhavenVR/actions/runs/34407935479);
  the pipeline passed and advanced `dev` to 0.9.1. See [RELEASE-0.9.0.md](RELEASE-0.9.0.md).
- **496 fixes SDK selection for installation on .NET 10-only machines.** The 494 SDK pin
  was too restrictive; major roll-forward preserves the preferred CI SDK while accepting
  newer installed SDKs. Installer preflight and the legacy restore-tool launch are checked.
  See [SDK-INSTALL-496.md](SDK-INSTALL-496.md).
- **495 adds rendered control-board tiles and improves every variant caption.** Bilingual player
  documentation combines controls and play guidance, covers both main-controller layouts and
  distinguishes selection, actions and character inspection. This is DLL-only after 483.
  Actual headset caption readability and tile interaction still require hardware confirmation.
- **483 IS A FULL INSTALL.** The asset bundle changed for the first time since ModBuild 368:
  74,943,671 → 74,943,763 bytes. Builds 369–482 were all DLL-only drops. A DLL-only install of
  483 shows neither of its two content changes, and the `ENV SKY BRANCH` log line says so out
  loud if it happens.
- **Previous performance hardware evidence covers496, local logs only, one additional player.** Regular scenario
  windows average11.35ms/frame; after a room expansion12.69ms. The run supports the user's smooth
  experience; neither progressive collapse nor a leak is established. Managed heap samples rise.
  See [MP-497-PERF.md](MP-497-PERF.md). Older remote logs and regression JPGs are not from this run.
- **497 synchronizes board motion with head/hand packets and extends hand ordering.** Owned normal
  hands reorder in selection, action and map, retaining order into the scenario; remote concealed
  plucks preserve surviving card positions. Review also closes the previously missing remote
  insertion gap/marker. Arrival/recenter yaw faces the player. Requested defaults and bilingual
  guides are updated; saved settings remain. See [MP-ROUND-497.md](MP-ROUND-497.md).
- **484 is DLL-only relative to 483.** Upgrading from the tested 482 requires the full 483 bundle.
- **493 optimizes multiplayer native presentation without reducing fidelity or cadence.**
  Card capture reuses immutable output; native sends avoid decoding their own snapshots; native
  playback avoids redundant writes/material swaps; original board sections refresh independently.
  Four-state production harnesses cover isolation and immediate transition/recovery behavior.
  Hardware FPS and full-party headset output remain unmeasured. See
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).
- Gate readings at497: all17 checkers pass; wire **253,579** assertions (**+524**: board88,
  fan61, insertion/edge375). Production capture **18,206**, playback **466**, board refresh
  **1,216** and the **12 existing runtime negative controls** pass. Worker-only negative controls
  also rejected three deliberate board defects and three fan defects. Strict Release **0 errors /
  0 warnings**; bilingual docs and all16 metadata-only reference assemblies pass. Patch registration
  **109 classes /167 methods**, surface **152**, config keys **625**, log tokens **4,716**,
  instrument-writes baseline **61**, bundle **74,943,763 bytes**. Records70/71 are additive;
  existing grammars and4096-byte presence reassembly bound remain intact. The documented presence
  budget grows3837→3840 bytes; allocation4097 retains257 spare bytes. This budget is historical
  arithmetic plus a tested three-byte tail, not a new saturated whole-protocol fixture.
  Compiled comparison against `1a714ee2`: **25 changed types and four added helpers**, no removed
  types or resources; changes match the reviewed source, defaults, packet capacity and embedded
  build constants. Local controlled cards and the entire map remain open; concealment is remote-only
  in scenarios.

### Recent builds

| build | what it was | install |
|---|---|---|
| 480 | the review round he asked for BEFORE spending a hardware test. Five read-only review lanes, 19 defects, three new gates | DLL only |
| 481 | the 2026-09 refactor programme: five lanes over 626 files / 550k lines. Also found four gates that could not fail | DLL only |
| 482 | the four rulings he gave on 481's deferred list, one lane each | DLL only |
| 483 | his two hardware notes, both baked into the assets on his ruling "lieber sauber" | **full** |
| 484 | multiplayer pulse, flights, grabbing, rest controls/burns, original bonus widgets and bounded extras transport | DLL only after 483 |
| 485 | remote character-change animations for map-room hands and open discard/burnt browsers | DLL only after 483 |
| 486 | native animation transport and systematic board/card/window parity repairs | DLL only after 483 |
| 487 | laser ownership, phase-consistent card visibility, stable initiative, cap sizing and native tooltip/highlight/element output | DLL only after 483 |
| 488 | short visible window-facing turn after release, matching grab-bar timing and preserving the drawn centre | DLL only after 483 |
| 489 | native card output, atomic held fronts, character decisions, committed/pending health and correctly routed/sequenced flights | DLL only after 483 |
| 490 | pre-test face/overlay/flight audit; later hardware exposed native group-bound rendering failures | DLL only after 483 |
| 491 | repair dynamic native card artwork and independent laser paths behind grab bars | DLL only after 483 |
| 492 | spent rest-burn continuity, native element material binding, board transition diagnostics and hardware-log review | DLL only after 483 |
| 493 | multiplayer capture/send/playback and independent native-section refresh optimization, preserving complete animation | DLL only after 483 |
| 494 | release 0.9.0, reproducible SDK selection and hosted native presentation regression harnesses | **full release package** |
| 495 | rendered board variant tiles, brighter larger captions and concise illustrated EN/DE play guidance | DLL only after 483 |
| 496 | SDK 10 installation compatibility, early SDK diagnostics and maintenance-tool runtime fallback | DLL only after 483 |
| 497 | atomic board motion, stable hand sorting across phases/map, remote insertion cues and requested defaults | DLL only after 483 |
| 498 | release 1.0.0 with build 497 gameplay and reviewed installation/packaging | **full release package** |

---

## 2. Owed to him, and what he has to judge

### 2a. He must look at this and say whether it is right

**The cellar's surround is now BLACK when zoomed out.** He reported two star skies in the cellar
and ruled the dome away. The dome was never visible from inside the room (the stone shell has a
closed ceiling, a capped stair shaft and capped rat holes); it was visible from OUTSIDE the shell,
in the zoomed-out pose where the room reads as a model in front of you. That surround is now the
`[Rig] VoidColor` clear. **This is a consequence of his instruction, not a defect** — but he has
not seen it yet, and it is one line to put back.

### 2b. Latest multiplayer corrections

- Build497 addresses follow-board sample timing, initial heading and owned hand ordering. Its
  hardware checklist includes concealed plucks, map-to-scenario sorting, remote insertion cues and
  long-rest exclusion. The user still needs to verify headset appearance and full-party scaling.
  Evidence and source changes: [MP-ROUND-497.md](MP-ROUND-497.md).

- Build 493 removes redundant native presentation CPU/allocation work and adds regression harnesses
  for four independent boards/senders. Review also closes pooled initiative identity and local element
  readiness recovery dependencies. Per-frame source sampling, original widgets and all visual rules
  remain intact. Hardware performance scaling is still owed; detailed proof and limits are in
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).

- Build 492 publishes rest-offer appearance from canonical pile models and retains the actual
  spent base through native burn reset, with native completion tracked independently. Remote
  element effects use original materials even when the viewer's branch is inactive. Board
  disappearance remains open; diagnostic/performance evidence is in
  [MP-ROUND-492.md](MP-ROUND-492.md) and its lane reports.

- Build 491 fixes the native card hierarchy construction failure affecting local map fans and
  remote fronts. Map cards remain public. Independent map/world UI laser routes now respect
  foreground grab bars and the clicking hand. See [MP-REGRESSION-491.md](MP-REGRESSION-491.md).

- Build490 reviews every local/remote card surface for face visibility, native overlay output
  and semantic flight lifecycle. Fixes include viewer-independent selection privacy, held map
  provenance, stale pooled models/materials, actor-scoped burn claims and owner release mirroring.
  See [MP-CARD-REVIEW-490.md](MP-CARD-REVIEW-490.md).

- Build489 addresses the thirteen MB488 findings and additional review defects. Cards mirror actual
  owner output; native decisions follow their character; damage previews preserve committed HP;
  active exits choose their true pile and burn flights wait for native completion. The Trample
  attack refusal was valid Disarm, not a targeting defect. See [MP-ROUND-489.md](MP-ROUND-489.md).

- Build 488 replaces instant release-facing with a 150 ms default cubic ease-out, using the existing
  grab-bar duration. Target and pivot are captured at release; regrab and external placement
  cancel cleanly. Shared windows retain their existing no-reface ruling. See
  [WINDOW-TURN-488.md](WINDOW-TURN-488.md).

- Build487 closes the five MB486 hardware findings and the discovered legacy-element animation
  refusal. The card visibility matrix and source-vs-log evidence are recorded in
  [MP-ROUND-487.md](MP-ROUND-487.md) and its lane reports.
- Additive53 carries original element hierarchy output,54 binds covered short-rest provenance
  to the semantic flight sequence, and55 carries original mandatory-highlight presentation.
  Record56 adds actual original item-tooltip emitters to the existing native plume stream.
  No existing record grammar or game-state authority changes.

### Earlier multiplayer work

- Explicit short-rest state now uses record 46 independently of sacrifice-seat record 39.
- Remote active-bonus rows now use original serialized game slot and picker prefabs, including
  owner subwidget state in record 47. The giant custom caption and plate widgets are removed.
  Build 486 adds native intermediate values in record 49, original auxiliary slot state in 50,
  actual card particle frames in 51 and original element-board frames in 52. The broader review
  also repairs card/fan motion, native pointer transitions, owner initiative depth and shared
  windows. See [MP-PARITY-486.md](MP-PARITY-486.md).
- Map-room fan exchanges now use the owner's map character key. Equal-sized hands refresh
  immediately; discard/burnt browsers re-emerge on character retargets. See
  [MP-FAN-485.md](MP-FAN-485.md).
- Local and remote card pulse/flight/rest repairs are integrated. The supplied disconnect is a
  confirmed transport receive timeout; its underlying cause remains unresolved.

### 2c. Historical refactor follow-ups

From the 2026-09 refactor's reviews (`.planning/refactor-2026-09/REVIEW-*.md`). These record
previous findings and deferrals; they are not new user-approved exceptions to the current
contracts. Recheck each finding against source and later rulings before implementation:

- **Four records ride the send cadence, not the edge** — resolved in 482 for records 36/39/41/43.
  The remaining question is whether any OTHER record has the same shape.
- **The furniture's materials are never destroyed** (`REVIEW-net.md` N8). Needs an owned-materials
  design, not a minimal fix.
- **`RemoteContentSeconds`** resolved in 486: retained and marked INERT in both languages.
  Received/content edges drive the mirror immediately; a fixed recovery poll is not a content delay.
- **No negative cache in the figure resolver** (N9). Bounded; a retry window would be an invented
  tuning value.
- **The wall fade's `RescanCore` two remaining items**: a write-only field and a dead overload
  that carries the live one's evidence.
- Two holes found while removing the cellar dome, filed with arithmetic in
  `NEEDED-OUTSIDE-cellar-one-sky.md`: the stair alcove is placed from the UNSNAPPED hole while the
  wall is cut to the SNAPPED one, and `BuildShaft` has no floor.
- **A half-applied caption pairing, open since ModBuild 363.** `Cards/Piles/PileViewer.cs` applies
  `NativeButtonSkin.ApplyFont` to the three pile captions but never `StyleWorldReadableLabel`,
  while `Cards/Tray/PlayTray.4.Slots.cs` — the caption whose own doc says it is built to match
  those three exactly, *"the same muted parchment colour, the same native HUD font, and the SAME
  fit box and font ceiling"* — does call it. One line, and it reads as intentional, which is why
  it has survived: it changes how three captions LOOK, so it wants his eye, not a silent fix.
  Filed in `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` §2.

### 2d. Hardware evidence and remaining observations

The build 496 multiplayer logs now measure the native send/transport, section-refresh and
card-appearance instrumentation introduced in 492/493. See [MP-497-PERF.md](MP-497-PERF.md):
board, revision and native-send readings are present; some appearance/transport scopes are
below the printing threshold in individual windows. Compare frame and logic times as well,
without adding nested scopes or equating a missing line with zero work. The short singleplayer
492 run could not measure those multiplayer paths. Four-player scaling is still unmeasured.

`REMOTE BOARD VISIBILITY` records root transitions, but the one-off long-rest disappearance
remains unexplained. Rest-burn appearance and native-material corrections need headset
confirmation; source and timing checks alone cannot establish the picture.

Historical diagnostic watch list (some items date to 480); check the current build and logs
before asserting that a token has never printed:

`Remote BURN look` · `DOCK MIRROR` · `GATE 3` · `NOT ASKED` · `REMOTE GLOW BLEND` ·
`SHORT REST PILE COVER` · `HELD BAR HIDE REFUSED` · `BURN ANIM STUCK` · `BURN ANIM FLAG LATCHED` ·
`MAP STORY SEND RATE` · `MAP PLACARD SCALE` · `WALL COMMIT THREW` (absence is the good reading) ·
`PACKET REJECTED` (zero is the good reading) · `ENV SKY BRANCH` (cellar must read ABSENT) ·
`HELD-CARD EDGE PRE-EMPT` · `GLOVE NORMAL TAMED` (**gone** — the glove value is baked now, so
there is deliberately no line; the proof is the picture and the bundle size).

**Giant orange text identified:** MB482's census names the 19.87 m
`Furniture/UseBarsDrawer/UseBar0/Caption`. That replica was removed in 484. Confirm the original
widgets, their pickers, and their size in the next headset test.

---

## 3. Standing rulings that are easy to break by accident

The full set is in `CLAUDE.md`. These four have each been broken at least once *after* being
written down:

1. **Visibility, including the later MB490 user clarifications (2026-09-09):** concealment
   applies only to remote presentation in scenarios. Local controlled-character cards are
   always open, including short-rest flights. The entire 3D map is public, locally and remotely.
   In scenarios, remote action cards and action-phase damage sacrifices are open; remote
   ability-selection fans, held and placed cards are covered. Remote short-rest burn flights
   remain covered. This supersedes older pile/active-held exceptions and local concealment.
   Resolve actual model membership before delayed widget CardType; an unresolved positional
   address must never guess a card identity.
2. **Seeing a card's FACE and being allowed to NAME it in a prompt are two questions**, over one
   population. Merging them re-opens the ModBuild 477 identity leak.
   `scripts/check-card-identity-mask.py` fails the build if they become one predicate.
3. **Historical localization behavior is documented in `Core/Loc/Loc.cs`:** transported text
   uses the sender's language; locally resolved keys use the viewer's. An implementation comment
   alone does not establish a user-approved exception to visual parity; follow `AGENTS.md`.
4. **The options button opens and closes the pause menu and touches nothing else.**

---

## 4. Subsystems with a closing account — read it before you touch them

| subsystem | read first | why |
|---|---|---|
| wall fade | `.planning/perf/WALL-FADE-CLOSEOUT.md` | closed on hardware; every dial is settled and the instruments that lied are listed |
| multiplayer 1:1 | `.planning/multiplayer/DESIGN-1TO1-RESIDUE.md` §6 | historical closeout; later parity rulings and build reviews still apply |
| the 2026-09 refactor | `.planning/refactor-2026-09/BRIEF.md` + the five `REVIEW-*.md` | what was found, what was deferred, and what the tooling could not see |
| static batching | `.planning/static-batching-removed.md` | tried and completely removed by user ruling |
| per-eye fade rivalry | `.planning/wall-fade-stereo-rivalry.md` | parked; unfixable on the game's masonry shader without losing the dissolve |

---

## 5. The shape of a round

1. Read his German report. Take the **symptom** as data; re-derive the cause.
2. Land shared contracts, then delegate independent tasks on **disjoint file sets** in separate
   Git worktrees created from current `dev`. Initialize dependencies with `worktree-setup.sh`,
   respect the session concurrency limit and never overwrite a shared baseline symlink.
3. Review every diff. Restrict each merge patch to the lane's OWNED paths.
4. Apply the cross-lane `NEEDED-OUTSIDE-*.md` items yourself.
5. Bump `ModBuild` **once** for a changed runtime build handed to a player, with actionable
   build notes. Documentation or packaging-only preparation does not change compatibility.
6. Run the three gate commands from `CLAUDE.md`. Regenerate `docs/PATCH-INVENTORY.md` once, at
   the end, if any patch class moved.
7. Push to `origin/dev`.
8. Write him a German report: what was found, what was fixed, what he must judge, what is owed.
