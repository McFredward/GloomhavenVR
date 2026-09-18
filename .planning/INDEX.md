# Planning index

Updated 2026-09-18 after successful 1.0.5 publication and the dev 1.0.6 / build-535 short-rest follow-up. This directory holds internal
engineering evidence, historical decisions and current status; it is not the player manual.

## Current entry points

| File | Purpose |
|---|---|
| [../AGENTS.md](../AGENTS.md) | Agent workflow, integration ownership and current user rulings |
| [../CLAUDE.md](../CLAUDE.md) | Technical contracts retained across agent tools |
| [STATE.md](STATE.md) | Current build, validation and outstanding hardware checks |
| [RELEASE-READINESS.md](RELEASE-READINESS.md) | Historical 1.0.0 repository audit and publication prerequisites |
| [../docs/DEVELOPING.md](../docs/DEVELOPING.md) | Developer setup and required gates |
| [../docs/CI-CD.md](../docs/CI-CD.md) | Actual release pipeline and main-branch provenance |
| [../src/GloomhavenVR/Net/NetProtocol.cs](../src/GloomhavenVR/Net/NetProtocol.cs) | Newest-first build notes and additive wire record registry |
| [PROJECT.md](PROJECT.md), [ARCHITECTURE.md](ARCHITECTURE.md) | Product scope and architectural reasoning |

## Latest measured work

- [BURN-535.md](BURN-535.md): preserve spent appearance across inactive original playback; production cleanup regression coverage.

- [RELEASE-105.md](RELEASE-105.md): successful main-branch 1.0.5 publication, verified asset and postrelease dev bookkeeping.

- [BURN-534.md](BURN-534.md): preserve the first original burn through temporary full-card inactivity.
- [GUILD-534.md](GUILD-534.md): native full-table recognition and separate Guildmaster map-surface caps.
- [MR-534.md](MR-534.md): prevent cold-start map furniture from being classified as enclosing sky.

- [ROUND-533.md](ROUND-533.md): exclude recovered hand cards from stale action-controller slots.
- [BURN-533.md](BURN-533.md): unresolved short-rest flash and bounded Debug evidence for the next test.

- [MR-532.md](MR-532.md): restrict MR backing geometry to UI under the latest hardware ruling.
- [BURN-532.md](BURN-532.md): retain spent appearance through the original burn's terminal step.
- [FLIGHT-532.md](FLIGHT-532.md): visible flight handover and accurate per-card completion evidence.

- [CARD-RECOVERY-531.md](CARD-RECOVERY-531.md): clear retained native burn effects on legitimate recovery, before local and remote presentation.
- [GUILD-RAIL-531.md](GUILD-RAIL-531.md): retain required Guildmaster controls when optional table-fit measurement fails.

- [MR-SCENARIO-530.md](MR-SCENARIO-530.md): MR terrain eligibility, wall ownership and backing lifetime.
- [GUILD-ROOM-530.md](GUILD-ROOM-530.md): Guildmaster right-side controls and native furniture floor contact.
- [MAP-PICKING-530.md](MAP-PICKING-530.md): centre-based overlapping map icon selection for laser and finger.
- [GUILD-QUESTS-530.md](GUILD-QUESTS-530.md): persistent browsing list versus actual accepted quest story.

- [BURN-528.md](BURN-528.md): local native burn-material ownership and bounded continuity evidence.
- [CARD-POOL-528.md](CARD-POOL-528.md): native card initialization failure, original hierarchy and recycled-copy repair.
- [GUILD-QUESTS-528.md](GUILD-QUESTS-528.md): return of the original standing quest list after story curtains.
- [GUILD-TABLE-528.md](GUILD-TABLE-528.md): original campaign tabletop reuse, furniture fit and cold-start asset limits.

- [RELEASE-104.md](RELEASE-104.md): build-527 hardware review and authorized 1.0.4 publication.

- [WINDOW-ANCHOR-527.md](WINDOW-ANCHOR-527.md): incoming window anchor, visible overlap and centre-first quest placement.

- [MR-VISIBLE-526.md](MR-VISIBLE-526.md): temple coordinate-frame regression and general transparent/cropped MR contributors.
- [MAP-HEADER-525.md](MAP-HEADER-525.md): diagnosed shared header pose contamination and repeated-opening restoration.
- [MR-REOPEN-524.md](MR-REOPEN-524.md): unresolved second-open backing growth; bounded actual MR edge diagnostics.
- [MR-MAP-BOUNDS-523.md](MR-MAP-BOUNDS-523.md): merchant/map screenshot evidence, original painted geometry, unified local/remote/effect bounds.

- [MR-ANIMATION-522.md](MR-ANIMATION-522.md): immediate native visibility, shared window erosion field, safe close/reopen and owned-resource cleanup.

- [TUTORIAL-HANDS-521.md](TUTORIAL-HANDS-521.md): restored per-task hands, original animated transitions and missing-model recovery.

- [MR-CI-520.md](MR-CI-520.md): scoped local/remote initiative backgrounds, trusted CI result reuse, evidence and validation limits.

- [WINDOW-REFLOW-519.md](WINDOW-REFLOW-519.md): opening-time overlap correction, shared authority, grip precedence and validation evidence.

- [TUTORIAL-RETRY-518.md](TUTORIAL-RETRY-518.md): integrated controller visibility and original defeat-retry placement, with evidence and validation limits.
- [TUTORIAL-CONTROLLERS-518.md](TUTORIAL-CONTROLLERS-518.md): persistent models, per-hand highlights, VR render layers and model/hand lifetime recovery.
- [RETRY-START-518.md](RETRY-START-518.md): per-participant original arrival and board restoration across defeat retries and round reloads.

- [BURN-517.md](BURN-517.md): integrated one-episode burn review, all producer paths and validation limits.
- [BURN-LOCAL-517.md](BURN-LOCAL-517.md): native reset/replay protection, historical reconstruction, replacement waiters and consumed items.
- [BURN-DRIVER-517.md](BURN-DRIVER-517.md): original-card claims, recovery and duplicate local flight admission.
- [BURN-REMOTE-517.md](BURN-REMOTE-517.md): stable remote burn discovery and native output continuity across missing address samples.

- [HARDWARE-516.md](HARDWARE-516.md): integrated latest hardware findings, log/screenshot evidence, regression coverage and limits.
- [TRAY-516.md](TRAY-516.md): paged discard cues, retained selection and recycle/undo lifetime.
- [MR-BACKING-516.md](MR-BACKING-516.md): visible-content MR fitting and matching local/remote geometry animation.
- [RESTART-516.md](RESTART-516.md): pooled native card ownership before scene reload, guarded continuation and aborted-load recovery.

- [RELEASE-1.0.3.md](RELEASE-1.0.3.md): maintainer acceptance, main release provenance and verified public package with the updated Glove assets.

- [GLOVE-SURFACE-515.md](GLOVE-SURFACE-515.md): authored Glove relief reduction, native A/B renders and bundle object verification.

- [TUTORIAL-SCOPE-514.md](TUTORIAL-SCOPE-514.md): native first-tutorial identity, scoped extra lessons and safe message-hold cleanup.

- [RELEASE-1.0.2.md](RELEASE-1.0.2.md): successful build-513 retest, scoped log review, performance limits and verified main release publication.

- [BURN-SEQUENCING-513.md](BURN-SEQUENCING-513.md): native burn completion before slot/fan replacement, causal observer release and original item effects.

- [DEADLOCK-512.md](DEADLOCK-512.md): native map reward continuation, travel recovery, mandatory close admission, failed conversion fallback and shared reward participation.

- [REWARDS-511.md](REWARDS-511.md): failed build-510 reward retest, native input/hover, original heading capture and passive TMP material reads.

- [REWARDS-510.md](REWARDS-510.md): native chest reward continuation, shared placement and first-reveal handoff.
- [RELEASE-1.0.1.md](RELEASE-1.0.1.md): combat log startup default and verified main release provenance.
- [TUTORIAL-508.md](TUTORIAL-508.md): sibling text/frame geometry, owner-only corner bounds
  and user-authorized omission of the premature quest-preparation hint.
- [SAVEGAME-507.md](SAVEGAME-507.md): native video skip, readable owner hints and complete
  world-map toggle events for the merchant tutorial/quest continuation.
- [VIDEO-WINDOW-506.md](VIDEO-WINDOW-506.md): persistent movie grab ownership and full-frame ink.
- [SAVEGAME-505.md](SAVEGAME-505.md): shared native movie windows and per-message hint provenance.
- [UPDATE-504.md](UPDATE-504.md): public update check reaches the prompt; the prompt now builds
  its uGUI layout with valid RectTransforms, confirmed through the release-mode fake-version test.
- [GOLD-503.md](GOLD-503.md): current combined gold-pile value in held-prop cards.
- [MAP-502.md](MAP-502.md): native outer/inner party-window handover after quest introductions.
- [MAP-502-EVIDENCE.md](MAP-502-EVIDENCE.md): two single-player reproductions and the multiplayer fallback comparison.
- [MAP-502-IMPLEMENTATION.md](MAP-502-IMPLEMENTATION.md): narrow hierarchy ownership and production regression coverage.
- [MP-501.md](MP-501.md): card-flight ownership and immediate held-figure action handovers.
- [MP-501-EVIDENCE.md](MP-501-EVIDENCE.md): matching build 500 multiplayer log timelines.
- [MP-501-FLIGHT-REVIEW.md](MP-501-FLIGHT-REVIEW.md): independent flight lifecycle review.
- [MP-501-FIGURE-REVIEW.md](MP-501-FIGURE-REVIEW.md): independent figure lifecycle review.
- [MAP-500.md](MAP-500.md): native loadout handover, map input locks and hardware acceptance.
- [MAP-500-EVIDENCE.md](MAP-500-EVIDENCE.md): current offline 499 softlock timeline and cause.
- [MAP-500-TRAVEL.md](MAP-500-TRAVEL.md): locked VR selection and incorrect travel admission.
- [CI-STORAGE.md](CI-STORAGE.md): bounded manual dev downloads; main release uploads retained.
- [REST-499.md](REST-499.md): native card-loss fallback fix, current 498 and historical 491 evidence.
- [RELEASE-1.0.0.md](RELEASE-1.0.0.md): release candidate, checks and publication status.
- [MP-ROUND-497.md](MP-ROUND-497.md): board motion, hand order, remote insertion cues and defaults.
- [MP-497-PERF.md](MP-497-PERF.md): actual 496 hardware logs, one peer, measured frame times and limits.
- [SDK-INSTALL-496.md](SDK-INSTALL-496.md): SDK 10 compatibility and installer diagnostics.
- [RELEASE-0.9.0.md](RELEASE-0.9.0.md): completed release 494; historical evidence, not the next version.
- [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md): multiplayer optimization and production harnesses.

## Retained history

Older `MP-*`, `LANE-*`, dated rounds, phase plans and experiment documents record what was observed
at their stated builds. Their paths, counters and proposed fixes are not current instructions.
Do not delete a record solely because a round finished: source comments and standing user rulings
cite this reasoning. The [2026-09-08 inventory](INDEX-ARCHIVE-2026-09-08.md) classifies the older
material as live, stale, closed or deliberately abandoned **as of that date**. Re-check a claimed
open item against current source before acting on it.

`refactor/` contains executable gate contracts and baselines; `refactor-2026-09/` holds the completed
refactor programme and its handoffs. Other dated subdirectories retain investigation evidence.
Generated screenshots, renders and fresh tester logs belong in ignored `debug/`, including
`debug/remote/` for peer logs. They are not distributed as repository documentation.

The old STATE narrative is [STATE-ARCHIVE-through-2026-08.md](STATE-ARCHIVE-through-2026-08.md).
Historical evidence is deliberately retained; the current status always comes from STATE and the
latest build notes, rather than an old unfinished checkbox.
