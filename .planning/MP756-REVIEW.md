# Multiplayer creation, visibility and progression review

Base: dev `79d249916` (Build755). Supplied paired hardware logs: Build753 on both peers, 2026-11-03. No new screenshots were supplied.

Classification: C. The union touches character creation/native actions, window lifecycle/placement, remote board presentation and new progression cues. Workers execute owned focused checks; integration executes the complete local gate once, strict Release (Debug where affected), wire goldens, source and applicable visual/localization checks. Bounded repairs retain unchanged input-bound proof.

Ownership: creation756 owns creation input guards and desync diagnostics; character756 owns character MenuDepth, quest readiness roster and perk presentation; board756 owns initiative enemy-info and remote active-card sequencing. Integrator owns rig/spawn, story recovery, rest/late-decision cues, localization and delivery stamp. All workers start from the base above in isolated sparse worktrees, sharing large read-only asset directories.

The maintainer explicitly authorizes recovery of a progression-critical story window after several seconds far outside the playfield. This supersedes the historical no-periodic-recall rule only for that window and condition. Recovery must have one shared author, yield to manual grabs, retain original content/input and never auto-continue.

Evidence and cleanup receipts live in ignored `.planning/debug/mp756/`. Old completed tooltip evidence is compressed per run, byte-verified and retained before its uncompressed original is removed. First whole-directory archival attempt ran out of temporary space; incomplete archive was removed after process exit, originals untouched.
