# State — where the project stands

**Updated 2026-09-16 for the general continuation audit, ModBuild 512 on dev.** The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which planning records are current or historical).

---

## 1. Position

- **dev / 1.0.2 / ModBuild 512 audits mandatory input and native continuation.**
  Map reward managers can be reached outside a scenario controller; failed blocking map
  conversions can request a usable desktop; travel parking failure retains original guarded
  input. Reused windows recheck mandatory close admission. Shared rewards use explicit participation replies. Failed
  conversion and attachment restore native UI, retaining ownership when cleanup needs retry.
  No gameplay lock bypass or timed automatic confirmation is introduced. See
  [DEADLOCK-512.md](DEADLOCK-512.md) for the scope, evidence and final gate results.
  Latest supplied logs remain local 510 / remote 500; hardware acceptance is open.
  No main merge or release publication is part of this audit.

- **dev / 1.0.2 / ModBuild 511 corrects the failed build-510 chest retest.**
  Tutorial/custom scenarios use UIRewardsManager outside Guildmaster mode; its gamepad
  confirmation adapter rejected the VR click. Continue now supplies only native input,
  preserving native reward groups, multiplayer ownership/actions and the completion callback.
  The button paints native hover/press/disabled states. Capture/chrome include the exact
  original heading's live glyph bounds, retaining layout, fonts and native masks.
  Separate build-509 user logs exposed allocating TMP material reads that repeatedly tore
  down render targets; capture and diagnostic reads now use existing shared materials.
  This removes that demonstrated failure path, not every possible source of FPS dips.
  Local evidence is build 510; retained remote logs are build 500. See
  [REWARDS-511.md](REWARDS-511.md) for the full evidence and validation record.
  Headset continuation, hover, framing and current multiplayer acceptance remain open.
  No main merge or release publication is part of this fix.
- **511 integration checks pass:** strict Release zero warnings/errors; all 17 checkers,
  production suites and 253,759 wire assertions. Reward: 299 assertions / 13 negatives;
  materials: 2,031 / four; ink: 237 / five runtime negatives plus one placement binding.
  Retained build-502 compiled comparison: 35 changed types, 25 additions, no removals,
  reviewed. Config/patch/log surfaces remain 625 / 161 / 4,726; bilingual docs and
  whitespace checks pass. These checks do not replace the hardware acceptance above.
- **Build 510's hardware retest failed despite green checks.** Its reward test incorrectly
  modeled ConfirmPressed as an unconditional input latch. Build 511 executes the native
  ProcessRewards iterator through completion, with a negative control for that exact defect.
  The shared-window identity, placement and first-reveal handoff from 510 remain in place;
  [REWARDS-510.md](REWARDS-510.md) is the historical implementation record.

- **1.0.1 / ModBuild 509 is published as the latest GitHub release.**
  main and v1.0.1 name PR #3 merge be74759e; Release run 35014316673 succeeded.
  The public ZIP matches its published SHA256 and contains the complete asset bundle;
  its DLL reports 1.0.1 / build 509 / be74759 / IsDevBuild=false. Combat log startup
  defaults to off while saved preferences and manual display remain available.
  Candidate CI and all local gates passed, including 253,674 wire assertions and
  36 release topology checks. See [RELEASE-1.0.1.md](RELEASE-1.0.1.md).
- **dev targets 1.0.2.** The workflow preserved main ancestry and advanced the
  version in bot commit 12f66604. Build 512 is the next hardware candidate;
  the published 1.0.1 release remains unchanged.

- **1.0.1 / ModBuild 508 corrects the build-507 tutorial presentation retest.**
  The user confirms the deadlock is resolved, and the log completes BuyItem/FTUE.
  HelpText and its separate native BG now reflow and align together instead of leaving
  the border and text apart. Corner placement excludes adopted hint geometry while
  retaining it for interaction/chrome. The exact quest-preparation hint is omitted
  under the user's explicit exception; the later battle-goal explanation remains
  native and all quest/tutorial continuations retain their original authority.
  See [TUTORIAL-508.md](TUTORIAL-508.md). Final headset confirmation remains required.
- **508 source/regression gates pass:** all 17 checkers, 253,674 wire assertions and
  production suites. Hints: 45 assertions/seven negative controls; ink/placement:
  218 assertions/four runtime negatives plus one binding negative; preparation prefix:
  13 assertions/three negatives. Compiled comparison with retained build 502: 30 changed
  types, 14 additions, no removals. Surfaces: 625 config keys / 161 patch signatures /
  4,724 log tokens; runtime patch inventory 117 classes / 184 methods. Strict Release
  passes with zero warnings/errors; bilingual docs and whitespace checks pass.

- **1.0.1 / ModBuild 507 repairs savegame tutorial input and hint layout.**
  The live movie surface now accepts laser/poke skip through native continuation.
  Original HelpText wraps at authored font size; pending standalone dissolve callbacks
  are retired before owner adoption. The merchant-to-map dispatcher now emits complete
  native toggle events: its former silent Select omitted the FTUE listener, leaving
  BuyItem active and blocking quest progression. Native tutorial/travel locks remain
  authoritative. See [SAVEGAME-507.md](SAVEGAME-507.md). Headset replay remains required.
- **507 source and regression gates pass:** all 17 checkers, 253,674 wire assertions
  and production suites; movie 66 assertions/six negative controls, hints 38/five,
  native off-bar dispatch 10/two. Surfaces remain 625/161/4,723. Retained build-502
  compiled comparison: 30 changed types, 13 additions, no removals. Strict Release
  has zero warnings/errors; bilingual documentation and whitespace checks pass.

- **1.0.1 / ModBuild 506 corrects the movie-window regression reported on 505.**
  The ordinary orphan sweep now recognizes the live video's exact grab holder,
  preventing repeated destruction/recreation at the world origin. Its full-frame
  image is explicitly content, so the backdrop exclusion cannot hide its handle.
  Chrome shares the canvas's persistent lifetime and module teardown; local and
  remote movie windows use the same ordinary grab/resize and modal ordering paths.
  See [VIDEO-WINDOW-506.md](VIDEO-WINDOW-506.md). Headset replay remains required.
- **506 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production regression suites. Movie ownership/sweep coverage now has 50 assertions
  and four negative controls; the ink walker adds 111 assertions/two negative controls.
  Strict Release: zero warnings/errors; bilingual docs and whitespace checks pass.
  Config/patch/log surfaces remain 625/161/4,723. Retained build-502 compiled comparison:
  28 changed types and 12 additions, no removals; the only newly changed types compared
  with the 505 review are ConvertedPanel and PanelInkBounds, alongside the intended
  movie/modal changes and build constants in types already in that review.

- **1.0.1 / ModBuild 505 fixes savegame introduction presentation.** Build 504 logs
  show native fullscreen video decoding to the desktop and introduction messages
  retaining old standalone conversions after adoption into a character window.
  Dedicated movie windows support shared playback/pose through additive TLV 72.
  Per-message native provenance and serialized owner references replace the global
  producer scan. Atomic conversion handover removes empty frames; hint fit excludes
  its fullscreen dimmer and cannot resize its owner. Native continue/fade behavior
  remains authoritative. See [SAVEGAME-505.md](SAVEGAME-505.md). Headset replay remains
  required; no main/tag/release change is part of this round.
- **505 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production suites with their negative controls. New movie/hint suites cover 39
  native-video, 35 shared-playback and 20 hint assertions; updater coverage adds
  nine assertions. Strict Release has zero warnings/errors; bilingual docs pass.
  Compiled review against the retained build-502 baseline: 26 changed types, 12
  additions, no removals (including already-integrated gold/updater/version changes).
  Config keys remain 625; patch signatures increase 160→161 and log markers
  4,719→4,723, with no removals. Runtime patch inventory: 116 classes/183 methods.
- **1.0.0 / ModBuild 504 repairs the self-update prompt.** The 0.9.0 hardware log proves that
  the public latest-release request and version comparison succeeded, then a bare `Transform` in
  `SelfUpdateDialog.BuildProgressRow` threw before the dialog could be drawn. Every dialog layout
  node now has an explicit `RectTransform`; a production harness constructs the choice/progress
  path and mutates the Progress node back to the failing form as a negative control. The headset
  retest confirmed the visible prompt using `install.ps1 -FakeVersion 0.9.0`; that flag compiles
  a release-mode test build, so the regular updater path runs without changing the checkout. See
  [UPDATE-504.md](UPDATE-504.md).
- **1.0.0 / ModBuild 503 makes held gold-pile cards match the laser-hover amount.** Native hover
  totals every `MoneyToken` on the tile, while the held card had only applied `GoldConversion` to
  its one grabbed token. A combined pile can now show the same current total in both views without
  a game-state write. See [GOLD-503.md](GOLD-503.md). Headset confirmation remains pending.
- **503 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing production
  suites and negative controls pass; strict Release has zero warnings/errors and bilingual docs
  pass. Compiled review: eight changed types, seven build-constant-only and `GrabbableProp`; no
  additions/removals. Config/patch/log surfaces remain 625/160/4,719.
- **1.0.0 / ModBuild 502 fixes the missing native party-container handover.** Fresh 501
  single-player logs for quests 078 and 039 show battle-goal selection open beneath a still
  refused outer PartyPanel. Build 500 admitted its different inner owner but missed that
  ancestor; multiplayer success came from the older 90-tick fallback. The current native
  PartyPanel wrapper is now admitted directly while intro/readiness guards remain intact.
  See [MAP-502.md](MAP-502.md) and its evidence/review. Headset replay remains pending.
- **502 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing suites
  and expanded map-flow tests (1,987 assertions/six negative controls). Strict Release has
  zero warnings/errors; bilingual docs pass. Compiled review: nine changed types, seven of
  them build constants only, with no additions/removals. Surfaces remain 625/160/4,719.
- **502 remains version 1.0.0 on dev.** The existing main/tag stays build 498. Build 501's
  card-flight/figure fixes are retained; its final hosted CI passed at `5aeb2cce`.
- **1.0.0 / ModBuild 501 fixes remote animation handovers.** Matching build 500 logs
  identify a flight starting while its remote source recess is still occupied; remote
  dock clearing additionally kept a stationary crumble beneath the flying copy. Source and
  destination ownership now survive delayed seating, overlapping flights and character changes.
  Held figures return to the board before native movement/facing/animation reads, with stale
  held samples rejected until release/switch. Recovery flights use native hand provenance.
  See [MP-501.md](MP-501.md), its independent reviews and hardware replay checklist.
- **501 local gates:** all 17 source checkers, 253,579 wire assertions and existing production
  suites; new flight tests cover 782 assertions/eight negative controls and figure tests cover
  1,440 assertions/six negative controls. Strict Release has zero warnings/errors; bilingual
  docs pass. Compiled review: 21 changed types (six build-constant-only), two new patch types,
  no removal. Config remains 625, patch signatures 152 to 160, log markers 4,718 to 4,719.
- **501 retains version 1.0.0 and is integrated on dev.** Existing main/tag `v1.0.0` remains build 498.
  New regression harnesses run in both CI and main release checks. Final headset timing remains
  unverified until the next hardware test.
- **ModBuild 500 attempted a map preparation softlock fix; 502 corrects its missing ancestor case.** Offline 499 logs show
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
