# State — where the project stands

**Updated 2026-09-21 for optional immersive town visits; dev 1.0.7 / ModBuild 539.** The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which planning records are current or historical).

---

## 1. Position

- **Town services / build 539:** VR options expose `WorldUI/ImmersiveTownServices`,
  enabled by default (maintainer clarification, 2026-09-21). Turning it off restores the three original service windows through
  the ordinary conversion path, including an already-open service, without changing native
  selection or invoking close/confirmation callbacks. Held samples are cancelled, original
  section parents and portraits restored. Enabled remote visitors remain visible regardless
  of the observer's local preference. Unity validation passes 617 assertions and 22 compiled
  negative controls. Hardware verification of live switching is pending.

- **Town services / build 538:** first immersive merchant, temple and enchantress variant.
  Three generated NPCs have body/finger rigs, authored greeting/idle animations, three mesh
  LODs and 4K textures. A separate `prebuilt/ghvr-town.bundle` keeps the existing asset bank
  unchanged. Native service sections become movable reading surfaces; gripping an original
  entry and placing its sample on the work tray selects through the original button.
  Native ownership, prices, restrictions, confirmations and continuations remain authoritative.
  Concurrent visitors share one NPC per service. Original visible widget output is transported
  to inert observer copies, including nested masks, card art and dynamic tooltip contents.
  The complete package requires **both** asset bundles; installing only the DLL is insufficient.
  Record and hardware checklist: [research/TOWN-SERVICES-FIRST-VARIANT.md](research/TOWN-SERVICES-FIRST-VARIANT.md).
  Source, Unity render and archive validation are recorded there; headset presentation remains
  unverified. This is a development handoff, not a release. Detailed facial animation and
  transaction-specific NPC hand choreography remain later polish.
  Original exports, generated sheets and paid mesh provenance remain separate in
  `.planning/debug/npc-references/`, `npc-modeling/` and `npc-meshes/` respectively.
  Seven FAL generation jobs were used, estimated USD 4.275; no additional paid generation
  was needed for runtime integration. Actual account billing was not independently audited.

- **Released 1.0.6 / ModBuild 537:** the maintainer confirmed the menu fix; the final
  hardware log audit found no release blocker. Main commit `59a5d884`, tag `v1.0.6`,
  release workflow `35525779327` succeeded. Published ZIP downloaded and verified.
  Automatic bookkeeping advanced dev to 1.0.7 at `ff59a14e`; no runtime build increment.

- **dev / 1.0.6 / ModBuild 537** removes VR-triggered native focus handoffs:
  opening settings must not shade otherwise usable menu entries. Native hidden callbacks
  are accepted in their actual order, and the row is silently cleared at closure rather
  than waiting one second. X -> immediate reopen and ordinary toggle closure share the
  same state; explicit reopen clears only the mod pane's pending same-frame close.
  Main-menu arbitration and independent scenario/map windows remain unchanged.
  The maintainer confirms build 536 fixed repeated opening; its new logs reproduce the
  stale selected row after X. Short-rest playback is unchanged and remains locally
  hardware-confirmed. Records: [OPTIONS-537.md](OPTIONS-537.md), [CLOSE-537.md](CLOSE-537.md).
  Options tests: 7,076 assertions / seven bindings / 19 negative controls; close lifecycle:
  1,796 assertions / six bindings / six negative controls. Full source/runtime guard and
  254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. Surfaces remain
  625 / 174 / 4,742; inventory 132 classes / 200 methods. Guard exit 1 is solely the
  expected old-baseline difference (101 changed, 78 added/removed, one order-only move).
  Direct compiled comparison with build 536 isolates the three intended menu types
  plus embedded build-number changes. Subsequently confirmed on the maintainer's headset
  and included in release 1.0.6, as recorded above.

- **dev / 1.0.6 / ModBuild 536** addresses repeated VR-options access. The initial
  Sep-20 logs are released build 534: its fourth opening within 60 seconds triggers
  CATCH-ALL FUSE, treating the registered mod menu as a cycling HUD banner. Registered
  mod menus are exempted from churn suppression; unknown HUD windows retain that guard.
  The mod-owned menu rows must stay visible, focused and pressable while their host is
  shown, and transient entry/injection failures must recover with bounded retry.
  Source/log records: [OPTIONS-536.md](OPTIONS-536.md), [BURN-536.md](BURN-536.md).
  **Short rest is hardware-confirmed resolved locally in the follow-up test.** The
  second_logs capture identifies build 535. At the previously failing 0.707-second
  renderer transition, grey/flow 1 and dissolve 0.646 survive unchanged while raw
  progress continues. One burn completes at 2.010 seconds before its pile flight.
  The maintainer reports no visible flash. No further burn change is made. Peer logs
  remain historical 500; new remote hardware confirmation is not available.
  Options implementation is integrated. Focused tests: 5,536 runtime assertions / four
  production bindings / 15 negative controls. Complete source/runtime guard and 254,565
  real-runtime wire assertions pass. Strict Release: zero warnings/errors. Bilingual docs,
  Actionlint, patch inventory and whitespace pass. Config/patch/log surfaces remain
  625 / 174 / 4,742; inventory remains 132 classes / 200 methods. Guard exit 1 is solely
  the expected compiled difference from baseline 080c505e9: 101 changed, 78 added/removed
  types and one order-only move. A separate comparison with the prior build-535 compiled
  output finds only the three intended menu types and build-number substitutions.
  Subsequent build-536 hardware confirms repeated opening works but exposes focus shading
  and delayed row clearing after X; build 537 addresses those. Short-rest confirmation stands.

- **dev / 1.0.6 / ModBuild 535** retains the spent appearance of a Lost card while its
  original native burn continues across temporary face inactivity. Build-534 Debug shows one
  complete ramp, not a replay, but its spent shader floor disappears around 0.697 seconds.
  The ordinary sampler still equated inactive hierarchy with stopped playback, contradicting
  build 534. It now follows the actual tracked iterator. A production-method regression
  reproduces the previous floor loss; real detach/recovery cleanup is no longer stubbed out.
  Native timing, recovery, flights, remote concealment and normal logging are unchanged.
  Local draw and owner publication use the same corrected sampler. Peer logs remain build 500;
  the precise hardware deactivation writer and headset outcome remain unverified.
  Record: [BURN-535.md](BURN-535.md). Focused replay: 683 runtime assertions / seven source
  bindings / 36 negative controls. Complete source/runtime guard and 254,565 real-runtime
  wire assertions pass; flight timing 818, remote burn sequencing 108 and burn layout 228
  assertions pass. Guard exit 1 is solely the expected compiled difference from baseline
  080c505e9: 99 changed, 78 added/removed types and one order-only project move. Config,
  patch and log surfaces remain 625 / 174 / 4,742, with no removals. Patch inventory remains
  132 classes / 200 methods. Strict Release passes with zero warnings/errors; bilingual
  docs, Actionlint and whitespace pass.

- **Released 1.0.5 / ModBuild 534**, main 377d26ec, tag v1.0.5, GitHub Latest.
  Full development CI, reused PR validation and main release workflow succeeded. Downloaded
  ZIP CRC, contents and SHA256 match the published asset. Dev was automatically advanced to
  1.0.6 at 9081a992 and includes the release ancestry. Record: [RELEASE-105.md](RELEASE-105.md).
  The maintainer confirms the Guildmaster fixes; short-rest flashing remains and was explicitly
  deferred for release. New local Debug logs are build 534; peer logs remain historical 500.
  No gameplay exception/deadlock was found. Bounded decorative coin-material load failures,
  native backend DNS errors and shutdown-only exceptions remain documented in the release audit.
  Postrelease burn investigation resumes on dev without changing the published release.

- **dev / 1.0.5 / ModBuild 534** addresses all three build-533 hardware findings.
  Debug shows the first short-rest burn exits synchronously on an inactive original, followed
  by a no-ramp settle and a later animated LostMode reset. Verified original full cards now use
  native disabled playback, retaining the first complete animation across that hierarchy edge.
  Cold Guildmaster MR hid the existing GH_Map_Table as enclosing sky; native map furniture is
  excluded before that heuristic, independently of camera seating/map-driver readiness. The
  whole native table is recognized before optional campaign-slab loading. No duplicate is built.
  Guildmaster action caps remain vertical; WorldMap/City form a separate centred pair to their
  right, fitted to native support and knife clearance. Campaign controls remain unchanged.
  Local logs are 533; peer logs remain historical 500. No fresh screenshots were supplied.
  Source/log causes are established; current headset/peer results are not yet verified.
  Records: [BURN-534.md](BURN-534.md), [GUILD-534.md](GUILD-534.md), [MR-534.md](MR-534.md).
  Focused checks: burn replay 672 assertions / seven bindings / 32 negative controls;
  Guildmaster room 10,338 assertions / 25 bindings / nine negative controls;
  MR scenery/sky 79 assertions / 26 bindings / six negative controls.
  Complete source/runtime guard and 254,565 real-runtime wire assertions pass. Existing
  flight timing 818, remote burn sequencing 108 and burn layout 228 assertions also pass.
  The guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  99 changed, 78 added/removed types and one order-only project move. Config/patch/log
  surfaces stay 625 / 174 / 4,742 without removals. Patch inventory is 132 classes / 200
  methods: the existing BurnCardTimeline patch gains the tested original-only prefix.
  Bilingual docs, Actionlint and whitespace pass; strict Release zero warnings/errors.

- **dev / 1.0.5 / ModBuild 533** fixes the clarified Spellweaver action-slot regression:
  the native action controller retains the first played card; recovering it from Lost to Hand
  previously made it eligible for the round slot again. Actual Hand membership now rejects
  that stale supplement, while Round/ExtraTurn cards and later legitimate selection still work.
  Both reported card flights worked; the later disappearance was the resurrected stale slot.
  Local logs are 532; remote logs remain historical 500. No independent remote static-pair
  reconstruction was found; owner-published slots inherit the corrected collection.
  The short-rest flash **remains unresolved**. The maintainer confirmed Debug was forgotten
  for this capture and enabled for the next test. Bounded opt-in diagnostics record
  native playback, reset and renderer/material transitions without adding normal-log streams.
  Records: [ROUND-533.md](ROUND-533.md), [BURN-533.md](BURN-533.md).
  Focused collector coverage: 50 runtime assertions across three production methods and six
  negative controls. Burn replay/diagnostics: 644 assertions, six bindings and 30 negative
  controls. Complete source/runtime guard and 254,565 real-runtime wire assertions pass;
  local flight timing 818, remote burn sequencing 108 and burn layout 228 assertions pass.
  The only guard exit-1 result is the expected compiled difference from baseline 080c505e9:
  99 changed, 77 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,742 with no removals; the additional marker is Debug-only BURN NATIVE TRACE.
  Patch inventory remains 132 classes / 199 methods. Strict Release zero warnings/errors;
  bilingual docs, Actionlint and whitespace pass. Headset results remain unverified.

- **dev / 1.0.5 / ModBuild 532** addresses the build-531 hardware report. MR backings
  now belong exclusively to UI: scenery underlays, fills and rims are retired, including
  the former unseen/preview routes. Native terrain/water and UI readability remain intact.
  Short-rest spent shader floors survive the native iterator's terminal step, which otherwise
  restores raw paint without writing a final frame. Pile flights take exclusive ownership of
  mod fade visibility and retire an obsolete vanish callback; native burn materials are preserved.
  The layout barrier now retains actual per-card artwork observations for release diagnostics.
  Local logs are 531; remote logs remain historical 500. Reviving Ether flights were launched
  (LogOutput 1278 and 2037); the logs do not establish whether the fade-handover defect caused
  those particular invisible flights. MR screenshot inspected; exact water renderer unknown.
  Source-proven fixes need headset confirmation, especially the reported missing flight.
  Records: [MR-532.md](MR-532.md), [BURN-532.md](BURN-532.md), [FLIGHT-532.md](FLIGHT-532.md).
  Integrated validation: all source/runtime stages and 254,565 real-runtime wire assertions
  pass; burn replay 633 / six bindings / 24 negative controls, flight timing 818 / 13 negative
  controls, burn layout 228 / 16 negative controls, MR scenery 54 / 26 bindings / five negative
  controls. Initial guard stopped at eight historical config-description strings classified as
  protected tokens. Restored those descriptions behind explicit inactive prefixes, then reran
  the affected MR checks, docs, surface census, strict Release and the unchanged remaining guard
  stages; unaffected runtime suites were not repeated. All checks pass. Compiled comparison
  retains the expected exit-1 difference from baseline 080c505e9: 99 changed, 76 added/removed,
  one order-only project move. Strict Release zero warnings/errors; bilingual docs, Actionlint,
  patch inventory and whitespace pass. Config/patch/log surfaces are 625 / 174 / 4,741 with no
  removals; patch inventory remains 132 classes / 199 methods. Independent source review found
  no additional actionable defect; headset/peer pixels remain unverified.

- **dev / 1.0.5 / ModBuild 531** Build-530 hardware confirms
  native Spellweaver recovery returns FireOrbs, ManaBolt, RidetheWind and FlameStrike from
  Lost to Hand (Player.log 9950–10001), but their burnt presentation remains. Native widget
  pile caching and retained burn presentation now receive explicit recovery reconciliation
  before local draw and remote publication. Old smoke cannot acquire a new Hand address;
  reset retries are isolated and cannot cancel a new burn or affect a pooled replacement. Guildmaster table controls are not globally redundant:
  native merchant/trainer/enhancement entry points exist. Build 530 introduced a silent
  whole-rail omission when optional support geometry could not be fitted. Refined mesh
  measurement and a readable right-side fallback retain access without changing campaign
  placement, native action availability or gameplay callbacks. Failed scans keep their
  ordinary cadence; missing-HUD and failed-support diagnostics are bounded.
  The maintainer reports the other build-530 hardware issues appear resolved. Current local
  logs are 530; remote logs remain historical 500. The new fixes still need headset checks.
  Records: [CARD-RECOVERY-531.md](CARD-RECOVERY-531.md), [GUILD-RAIL-531.md](GUILD-RAIL-531.md).
  Focused recovery: 552 runtime assertions / six bindings / 22 negative controls; room
  geometry: 7,014 assertions / 21 bindings / seven negative controls. Complete integration
  guard and 254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. The only guard exit-1
  verdict is the expected compiled difference from historical baseline 080c505e9:
  99 changed, 76 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,740 with no removals; patch inventory remains 132 classes / 199 methods.

- **dev / 1.0.5 / ModBuild 530** addresses the five build-529 hardware findings. MR
  backings are excluded from native wall ownership, supplemental unseen-region backings
  are restricted to the intended geometry, and stale/inactive sources are retired.
  Guildmaster controls fit the right tabletop behind the knife; its environment floor
  follows native furniture bases. Overlapping map icons use nearest visible centres for
  laser and fingertip selection. The original Guildmaster quest list stays during browsing
  dialogs, but actual accepted quest story/loadout still hides it, including native peer
  travel without a previously observed local selection. Campaign placement is unchanged.
  Records: [MR-SCENARIO-530.md](MR-SCENARIO-530.md), [GUILD-ROOM-530.md](GUILD-ROOM-530.md),
  [MAP-PICKING-530.md](MAP-PICKING-530.md), [GUILD-QUESTS-530.md](GUILD-QUESTS-530.md).
  Hardware confirms the build-528/529 table is visible. Exact MR pixel attribution,
  final floor contact/knife clearance and the new interactions still need headset checks.
  Remote logs remain historical build 500. EN/DE tutorial execution hints now explicitly
  require board CONFIRM; the unsupported second-pick advice is removed from all five uses.
  Validation: complete guard/source/presentation suite and 254,565 real-runtime wire
  assertions pass; strict Release zero warnings/errors, bilingual docs, Actionlint and
  whitespace checks pass. New focused totals: MR ownership 39, room geometry 6,273,
  map picking 333 and standing quest list 69, with bindings and negative controls.
  Tutorial scope rechecked after the wording change (42 runtime / 17 binding assertions).
  Guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  97 changed, 76 added/removed types, one order-only project move. Config/patch/log
  surfaces are 625 / 174 / 4,739, with no removals; patch inventory remains 132 classes /
  199 registered methods. One additional Debug floor-placement token versus build 529.

- **dev / 1.0.5 / ModBuild 529** moves the per-burn continuity records to opt-in Debug,
  including an early observer guard to avoid material/progress reads and formatting at
  ordinary verbosity. Re-enabling Debug starts fresh; anomaly output remains bounded.
  Build-528 table/pool diagnostics already require Debug. This corrects the earlier promise
  that normal logs would contain detailed continuity evidence. See [BURN-528.md](BURN-528.md).
  The user's sparse-normal-log requirement is also recorded in `AGENTS.md`.
  Validation: complete source/presentation guard and 254,565 real-runtime wire assertions
  pass; strict Release zero warnings/errors; bilingual docs and whitespace checks pass.
  The expected compiled diff from historical baseline 080c505e9 is the only guard exit-1
  verdict. Config/patch/log surfaces remain 625 / 174 / 4,738, with no removals.

- **dev / 1.0.5 / ModBuild 528** addresses the owner's local short-rest flash, Guildmaster
  table/standing quest list, and a native Swift Bow UI initialization error in the latest
  build-527 logs. Original faces retain animated materials through temporary ownership;
  raw progress/material diagnostics separate a restart from a material swap. Native card
  hierarchy returns before teardown/recycling, with damaged-copy rejection before reuse.
  The original quest widget returns after temporary story/journey withdrawal. Guildmaster
  discovers and fits the campaign table's original assets without moving native furniture.
  Records: [BURN-528.md](BURN-528.md), [CARD-POOL-528.md](CARD-POOL-528.md),
  [GUILD-QUESTS-528.md](GUILD-QUESTS-528.md), [GUILD-TABLE-528.md](GUILD-TABLE-528.md).
  **Hardware remains unverified:** exact flash writer, cold Guildmaster asset availability,
  final furniture fit, repeated scene transitions and current multiplayer observers.
  Remote logs remain build 500, not current evidence. No release was requested this round.
  Integrated validation: full guard source/presentation checks and real-runtime wire vectors
  pass (254,565 wire assertions). Focused totals: burn 534, material ownership 543, native
  pool lifetime 169, standing quest log 26, table fit/material lifecycle 1,530, plus bindings
  and rejected mutations. Strict Release zero warnings/errors; bilingual docs and Actionlint
  pass. Guard exit 1 is the expected compiled difference from baseline 080c505e9.
  Surfaces: 625 config keys / 174 patch attributes / 4,738 log tokens; two new registered
  pool hooks and five new diagnostic tokens versus 527. No existing surface was removed.

- **1.0.4 release authorized after the build-527 hardware report.** The maintainer reports
  no visible issues. Current local logs show completed encounter room making with the new
  window fixed and no mod Error/Fatal entries. Old remote logs are not current evidence.
  Release preparation and audit: [RELEASE-104.md](RELEASE-104.md).
  **Published successfully:** main Release run 35276519563 rebuilt immutable tag v1.0.4
  from main a6d044c4, verified its existing full dev CI evidence, packaged and uploaded the
  archive, checked its SHA256 and published it as Latest. No full suite was repeated on main.
  Recovery fixes landed through PRs #8/#9 after full dev CI; their PR checks reused proof.
  Dev bookkeeping completed at 24c8fce9 and now names **1.0.5**, still ModBuild 527.
  The earlier upload failures and draft-discovery defect are resolved; the release record
  preserves their evidence and the final archive digest.

- **dev / 1.0.4 / ModBuild 527 preserves the newly opened map window's spawn pose.**
  Build-526 logs show three quest-popup animations moving only the newcomer. The solver now
  anchors incoming windows and admits only older movable overlaps. Visual occupancy uses
  original painted/cropped content instead of transparent host/hit rectangles, after opening
  effects settle. The legacy standing-quest-log preference now keeps a free gaze centre;
  hidden-log private quest selection retains its established corner placement.
  Evidence and focused validation: [WINDOW-ANCHOR-527.md](WINDOW-ANCHOR-527.md).
  Validation: reflow 1,322 assertions / 18 bindings / three negatives; quest seat 43 / three
  negatives; painted occupancy 453 / 29 negatives; shared reflow 74 / six bindings / four
  negatives. Strict Release zero warnings/errors. Frame-order, partial-order, bilingual docs,
  Actionlint, shell syntax and whitespace pass. Config/patch/log census unchanged:
  625 / 172 / 4,733. The user confirms the hardware behavior; a current matching-peer log was not supplied.

- **dev / 1.0.4 / ModBuild 526 addresses temple-first/repeated temple header drift and
  general invisible MR contributors.** The build-525 report confirms merchant improvement,
  but the same header moves down/left after temple entry and contaminates later merchant
  openings. The new source repair observes native header TRS before conversion and resolves
  original parent frames rather than replaying local coordinates across different parents.
  MR extent guards additionally distinguish native renderer transparency and the actual
  displayed capture footprint from unbounded authored geometry, in every direction.
  Original visible overflow and the window's existing animation remain part of the contract.
  Evidence, focused validation and hardware limits: [MR-VISIBLE-526.md](MR-VISIBLE-526.md).
  Validation: banner 314 assertions; ink/capture/watch 447; MR layout 258; animation 552,
  with integration bindings and mutation negatives. Strict Release zero warnings/errors.
  Source/frame/docs checks pass; config/patch/log surfaces unchanged: 625 / 172 / 4,733.
  The user confirms the MR reopen defect is fixed in the build-526 hardware test.


- **dev / 1.0.4 / ModBuild 525 repairs the native header that inflated reopened MR windows.**
  Build-524 diagnostics identify the same shared `UI Adventure Header/Icon` drifting upwards
  and shrinking on every merchant reopening, then carrying the defect into temple.
  The native world-preserving parent change retained the converted window's pose/scale;
  the old mod return path skipped geometry restoration after native ownership resumed.
  A tracked borrow now restores the original root-local layout/pose on both return paths,
  preserving native parent/sibling choices and child content. No MR geometry clamp or
  animation change. Source cause and reproduction are in [MAP-HEADER-525.md](MAP-HEADER-525.md);
  corrected headset appearance still needs confirmation.
  Validation: 180 runtime assertions, three bindings, four runtime negatives and one binding
  negative; strict Release zero warnings/errors. Frame-order, bilingual docs, Actionlint,
  shell syntax and whitespace pass. Config/patch/log surfaces unchanged: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 524 is a diagnostic build; the MR reopen defect remains open.**
  The user reports first merchant/map opening correct and subsequent openings too tall.
  Local logs identify 523; remote logs remain historical 500. Capture/hit bounds grow,
  but their extrema do not establish the actual MR contributor. Previous MR diagnostics
  were Debug-only. Bounded normal-level records now identify actual MR edge graphics,
  masks/alpha/material state and target/host instances across conversion lifetimes.
  No further rendering or native-flow change is claimed. See [MR-REOPEN-524.md](MR-REOPEN-524.md).
  Validation: strict Release zero warnings/errors; ink/diagnostics 398 assertions and
  24 negatives; MR layout/accessor 258 assertions, 48 bindings and 16 negatives; animation
  lifecycle 545 assertions, three bindings and three negatives. Frame-order, bilingual docs,
  shell syntax and whitespace pass. Config/patch/log surfaces: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 523 fits MR backgrounds to native painted geometry.**
  Source changes exclude empty text-layout height and reintroduced tooltip glyphs,
  but build-523 hardware evidence confirms that merchant/map reopen growth persists. Local steady/effect paths and inert remote surfaces share the original
  text/image/clip/visibility policy and a small margin. Native layout, hit/capture and grab
  geometry remain unchanged. Source fixes and build-522 screenshot/log evidence are in
  [MR-MAP-BOUNDS-523.md](MR-MAP-BOUNDS-523.md); headset confirmation remains open.

- **523 focused checks pass:** native ink/painted geometry 368 runtime assertions /
  nineteen runtime and one binding negative; MR layout/accessor 258 assertions /
  48 bindings / thirteen runtime and three binding negatives; actual animation lifecycle
  545 assertions / three bindings / three negatives. Strict Release has zero warnings/errors.
  Eleven frame-order locks, 617 hardware markers, shell syntax and bilingual docs pass.
  Config/patch/log surfaces stay 625 / 172 / 4,732. Unrelated local suites were not repeated.

- **dev / 1.0.4 / ModBuild 522 couples MR backgrounds to window materialisation.**
  Backgrounds use the same erosion field and element progress and disappear before
  the debris-only tail. Native hidden/empty/transparent content clears its backing
  immediately, locally and remotely, independently of cached geometry measurements.
  Close/reopen, MR toggles and native continuation remain independent of decoration.
  See [MR-ANIMATION-522.md](MR-ANIMATION-522.md); headset confirmation remains open.

- **522 focused checks pass:** MR layout/accessor 258 runtime assertions / 48 bindings /
  thirteen runtime and three binding negatives; native ink/live visibility 309 assertions /
  fourteen runtime and one binding negative; erosion mesh 35,833 assertions / ten negatives;
  actual animation lifecycle and native continuation 542 assertions / three bindings /
  three negatives. Strict Release has zero warnings/errors. All eleven frame-order locks,
  617 hardware markers, Actionlint, shell syntax and bilingual docs pass. Config and patch
  surfaces remain 625 / 172; log tokens increase to 4,732 with one new failure diagnostic.
  Full unrelated local suites were not repeated, as requested.

- **dev / 1.0.4 / ModBuild 521 restores the original tutorial hand/controller choices.**
  Card handling, fingertip interaction and prose show hands; key lessons show both
  controllers with the existing per-hand highlights. The original 0.22s animation remains.
  Recovery respects the active task and restores a hand immediately if its controller
  model is lost. Build 518's continuous-controller interpretation was explicitly corrected
  by the user. See [TUTORIAL-HANDS-521.md](TUTORIAL-HANDS-521.md). Only affected tests and
  the strict build run for this change, as requested; headset confirmation remains open.

- **521 focused checks pass:** controller presentation 3,452 runtime assertions / four
  bindings / twelve negatives; first-tutorial scope 42 / 17 / eight. Strict Release has
  zero warnings/errors. Config, patch and log surfaces are unchanged at 625 / 172 / 4,731.
  Full local guard and golden-wire suites were not repeated per the user's request.

- **dev / 1.0.4 / ModBuild 520 scopes initiative and element MR backgrounds to their
  original native rows, locally and on inert remote clones.** Transparent host extents
  and sibling UI cannot inflate those backgrounds. Normal window artwork and smooth
  sizing remain. Local evidence is build 519; remote files remain historical build 500.
  See [MR-CI-520.md](MR-CI-520.md) for evidence, validation and hardware limits.

- **CI now reuses trusted successful dev evidence for identical source trees.** Dev
  still runs full checks; unchanged internal PRs can reuse them, while forks and changed
  merges run full validation. Main releases require successful full-test evidence, then
  build/package the actual main commit without repeating the full suite. No proof
  artifacts are stored. No release or main update is part of this change.

- **520 integration passes:** 17 source checkers, all production harnesses and 254,565
  wire assertions; strict Release zero warnings/errors. MR: 250 runtime + 41 bindings,
  13 runtime / three binding negatives; native ink: 259 assertions / ten runtime and one
  binding negative. CI proof: 23 cases; release topology: 36 assertions; artifact cleanup:
  20 cases. Actionlint and bilingual docs pass. Config / patch / log surfaces are
  625 / 172 / 4,731; patch inventory 130 classes / 197 methods; bundle unchanged.
  Build-519 compiled comparison: 13 changed (seven build-only), one added, zero removed.
  Retained build-502 comparison: 86 changed / 54 added / zero removed.

- **dev / 1.0.4 / ModBuild 519 adds opening-time window room making.** Overlapping
  encounter/story windows can move together with a brief animation inside the view.
  One VR participant authors shared movement; stationary remote grips and manual moves
  interrupt it. Visible FINISHED story frames retain pose synchronization without reopening
  the native dialog. Additive record 77 carries explicit held/automatic masks; v3 remains.
  See [WINDOW-REFLOW-519.md](WINDOW-REFLOW-519.md) for evidence and final validation.
  Supplied logs remain local 515 / remote 500; hardware validation of this build is open.

- **519 integration passes:** 17 source checkers, all production suites, 254,565 wire
  assertions and strict Release with zero warnings/errors. Layout: 1,247 runtime + 15
  bindings / two negatives; authority: 74 + six / four. Surfaces: 625 config / 172 patch
  signatures / 4,730 log tokens. Patch inventory stays 130 classes / 197 methods; bundle
  unchanged. Build-518 compiled comparison: 14 changed / three added / zero removed,
  including five build-only changes and one buffer-size-only change. The retained build-502
  comparison is 84 changed / 53 added / zero removed. See the build record for evidence.

- **dev / 1.0.4 / ModBuild 518 addresses tutorial controllers and defeat Retry.** Both
  controllers stay visible throughout the custom first-tutorial lesson, with task-specific
  highlights on the applicable hands. Recursive VR-layer assignment protects their parts
  from scenery fading; missing models and rebuilt hands recover during the lesson.
  Retry restores each participant's original scenario head pose, scale and board pose.
  Round reloads preserve that baseline, and later peer movement or saved zoom cannot
  redefine it. See [TUTORIAL-RETRY-518.md](TUTORIAL-RETRY-518.md) for source evidence,
  focused coverage and final integration results. Hardware confirmation remains open;
  supplied logs still identify local 515 / remote 500. No bundle, wire or release changes.

- **518 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Tutorial controllers: 1,019 runtime +
  four bindings / nine negatives; retry: 433 + 17 / 15. Retained build-502 compiled diff:
  83 changed / 50 added / zero removed. Private build-517 comparison: 14 changed / three
  added / zero removed, including seven changes limited to the build constant. Reviewed
  surfaces: 625 config / 172 patch signatures / 4,729 log tokens; patch inventory 130
  classes / 197 methods. All 21 classified network-action patches, bilingual docs, shell
  syntax and whitespace pass. No hardware result is inferred from these checks.

- **Build 517 addresses repeated burn playback.** Native effect aliases,
  pile refresh and hover cleanup cannot restart or truncate an owned ability burn. Historical
  lost/consumed widget construction paints the original settled output; a replacement during
  playback waits for the original with cancellation-safe ownership. Actual recovery permits
  later burns. Item effects cannot overlap, and active-card resets requested during playback
  run after completion. Local/remote discovery uses original model identity; missing remote
  samples retain the same lost card's last owner-painted output. Actual completed flight claims
  remain distinct from historical baselines. Scene and pool boundaries retire native guards.
  Source and focused regression review complete; full integration results are recorded in
  [BURN-517.md](BURN-517.md). Supplied logs remain local 515 / remote 500, so this is not
  a headset verification. No bundle, wire-format or published-release changes.

- **517 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Ability replay: 496 runtime + three
  bindings / 13 negatives; items 225 / 11; local layout 219 / 16; remote sequencing 108 / 21;
  scene lifetime 36 + 23 / eight. Retained build-502 compiled diff: 77 changed / 47 added /
  zero removed. Private build-516 comparison: 16 changed / two added / zero removed,
  including seven changes limited to the propagated build constant. Reviewed surfaces:
  625 config / 171 patch signatures / 4,729 log tokens; patch inventory 128 classes / 195
  methods. Bilingual docs, shell syntax and whitespace pass. Hardware confirmation is open.

- **Build 516 hardware fixes remain included in dev.**
  Completed discard pages retain their native selected claims; final confirmation cannot
  light an unavailable second recess. Native recycling updates the locked prefix, and undo
  keeps earlier page return flights. MR backings fit visible native content with a small margin,
  reject empty/transient measurements and animate over a shared 150 ms locally and remotely.
  Borrowed native card hierarchies return before scene unload; native loading state blocks
  re-adoption, while aborted loads restore retained selected identities and original callbacks.
  Supplied local evidence is release 515; remote files remain historical 500. The exact first
  Unity destruction order is not logged. Hardware retest remains open; see
  [HARDWARE-516.md](HARDWARE-516.md) and its three lane reports.

- **516 integration checks pass:** all 17 guard checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Pick tray: 245 runtime + eight bindings /
  six negatives. MR: 233 + 25 / ten runtime + three binding negatives. Scene lifetime:
  35 + 20 / seven negatives. Native ink: 243 assertions. Retained build-502 compiled diff:
  76 changed / 45 added / zero removed; private build-515 comparison: 21 changed / three
  added / zero removed, with 13 changes limited to version/build constants. Reviewed surfaces:
  625 config / 164 patch signatures / 4,729 log tokens; patch inventory 120 classes / 187
  methods. Bilingual docs, shell syntax and whitespace pass. Published 1.0.3 is unchanged.

- **1.0.3 / ModBuild 515 is published from main.** PR #6 merged the accepted runtime/assets
  with bilingual release highlights as `fd76de86`. Release run 35151926513 passed; tag,
  public ZIP, bundle hash, release DLL and the unauthenticated latest endpoint were verified.
  The workflow retained main ancestry on dev and advanced its next version to **1.0.4**.
  See [RELEASE-1.0.3.md](RELEASE-1.0.3.md).

- **ModBuild 515 softens the Glove surface (full install).**
  Both glove materials reduce authored normal relief from 0.5 to 0.25. Native model renders
  and actual bundle checks cover both hands and confirm that Plate/Arcane, geometry and
  attachment anchors are preserved. The exact Unity 2021.3.5f1 bundle has 617 assets and
  74,942,975 bytes. All 17 checkers and production suites pass; 254,019 wire assertions;
  strict Release zero warnings/errors. Incremental compiled comparison has seven changed types,
  exclusively the propagated ModBuild constant; no added/removed types. Surface counts remain
  625 / 163 / 4,728, patch inventory 119 / 186. The maintainer accepted the current changes
  for release; no new per-case hardware capture accompanies that acceptance.
  See [GLOVE-SURFACE-515.md](GLOVE-SURFACE-515.md).

- **ModBuild 514 limits additional VR lessons to the first native tutorial.**
  Admission uses the tutorial selector's first ID and filename, while later tutorials keep
  their native sequence and generic VR wording/input adaptations. Pending lesson/skip/hold
  state retires on scope loss; held messages cannot cross native controller ownership.
  Focused tests pass: 42 runtime + 17 binding assertions, seven runtime negative controls and
  one binding negative control. All 17 checkers and production suites pass; 254,019 wire
  assertions; strict Release zero warnings/errors. Compiled comparison: 71 changed / 42 added /
  zero removed against retained build 502; incremental build-513 comparison 19 changed / two
  added / zero removed, reviewed (tutorial scope plus propagated version/build constants).
  Surfaces 625 / 163 / 4,728; patch inventory 119 classes / 186 methods. The later release
  acceptance is recorded above; it does not enumerate individual tutorial transition tests.
  See [TUTORIAL-SCOPE-514.md](TUTORIAL-SCOPE-514.md).

- **Previous release: 1.0.2 / ModBuild 513.** PR #5 merged the hardware-tested
  dev source unchanged as `11107a29`. Release run 35146255179 passed; tag, public
  download, checksum and release DLL were verified. See [RELEASE-1.0.2.md](RELEASE-1.0.2.md).
  After that release, the workflow preserved main ancestry on dev and advanced to **1.0.3**.

- **ModBuild 513 sequences every native burn before card replacement.**
  Round slots, fans, active grids and character exchange retain their previous presentation
  until all native burns finish. Actual iterator completion distinguishes finished handles.
  Observers wait for the canonical owner's completion frame; durable original-card release
  addresses delayed delivery and slot reuse. Consumed items retain their native widget and
  share original item appearance through additive stream 17/18 (record 76). Incoming character
  views also wait for actual owner burn progress; offscreen completions do not invent flights.
  Native gameplay callbacks and mandatory decisions keep running. See
  [BURN-SEQUENCING-513.md](BURN-SEQUENCING-513.md). The maintainer reports a successful
  build-513 retest; current local logs also observe a build-513 peer. All six recorded burns
  complete with subsequent flights, and phase stalls resolve. Retained remote files remain
  historical build 500. Not every edge case is individually established by this capture.
  Coarse game-loop cadence declines during the session; no memory/GPU trace establishes
  its cause or a leak. The release audit records this limitation and warning triage.

- **513 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Focused suites: local layout 204, remote
  sequencing 70, native completion 33, item lifetime 115 and item appearance 645 assertions,
  with runtime negative controls. Retained build-502 compiled comparison: 65 changed / 40 added /
  zero removed; additional build-512 comparison: 39 changed / 11 added / zero removed, reviewed.
  Surfaces 625 / 162 / 4,728; patch inventory 118 classes / 185 methods. Bilingual docs,
  shell syntax and whitespace pass. These results do not establish headset appearance.

- **dev / 1.0.2 / ModBuild 512 audits mandatory input and native continuation.**
  Map reward managers can be reached outside a scenario controller; failed blocking map
  conversions can request a usable desktop; travel parking failure retains original guarded
  input. Reused windows recheck mandatory close admission. Shared rewards use explicit participation replies. Failed
  conversion and attachment restore native UI, retaining ownership when cleanup needs retry.
  No gameplay lock bypass or timed automatic confirmation is introduced. See
  [DEADLOCK-512.md](DEADLOCK-512.md) for the scope, evidence and final gate results.
  At implementation time, supplied logs were local 510 / remote 500. The later successful
  build-513 retest and release review are recorded above; this is the historical audit scope.

- **512 integration checks pass:** all 17 checkers and production suites; 253,893 wire
  assertions; strict Release zero warnings/errors. Focused suites: rewards 320, map flow
  1,997, modal desktop 1,066, mandatory close 76, reward pose 104 and rollback 87 assertions,
  with runtime negative controls. Retained build-502 comparison: 39 changed / 29 added /
  zero removed types, reviewed; additional build-511 compiled comparison confined to this
  audit. Surfaces 625 / 161 / 4,728; patch inventory 117 classes / 184 methods.
  Bilingual docs, shell syntax and whitespace pass. These checks alone do not establish hardware outcomes.

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
  Headset acceptance was open at implementation time; the later build-513 maintainer
  retest reports no observed issues. See the release audit for its actual evidence limits.
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

- **Previous release: 1.0.1 / ModBuild 509.**
  v1.0.1 names PR #3 merge be74759e; Release run 35014316673 succeeded.
  The public ZIP matches its published SHA256 and contains the complete asset bundle;
  its DLL reports 1.0.1 / build 509 / be74759 / IsDevBuild=false. Combat log startup
  defaults to off while saved preferences and manual display remain available.
  Candidate CI and all local gates passed, including 253,674 wire assertions and
  36 release topology checks. See [RELEASE-1.0.1.md](RELEASE-1.0.1.md).
- After 1.0.1, the workflow preserved main ancestry and advanced dev to 1.0.2 in
  bot commit 12f66604. The subsequent 1.0.2 publication is recorded above.

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
