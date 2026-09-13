# Planning index

Updated 2026-09-13 for the 1.0.0 long-rest hotfix, build 499. This directory holds internal
engineering evidence, historical decisions and current status; it is not the player manual.

## Current entry points

| File | Purpose |
|---|---|
| [../AGENTS.md](../AGENTS.md) | Agent workflow, integration ownership and current user rulings |
| [../CLAUDE.md](../CLAUDE.md) | Technical contracts retained across agent tools |
| [STATE.md](STATE.md) | Current build, validation and outstanding hardware checks |
| [RELEASE-READINESS.md](RELEASE-READINESS.md) | Upcoming 1.0.0 repository audit and publication prerequisites |
| [../docs/DEVELOPING.md](../docs/DEVELOPING.md) | Developer setup and required gates |
| [../docs/CI-CD.md](../docs/CI-CD.md) | Actual release pipeline and main-branch provenance |
| [../src/GloomhavenVR/Net/NetProtocol.cs](../src/GloomhavenVR/Net/NetProtocol.cs) | Newest-first build notes and additive wire record registry |
| [PROJECT.md](PROJECT.md), [ARCHITECTURE.md](ARCHITECTURE.md) | Product scope and architectural reasoning |

## Latest measured work

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
