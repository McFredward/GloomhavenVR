# Documentation index

## Player guides

| Topic | English | Deutsch |
|---|---|---|
| Features and tutorial recommendation | [README](../README.md) | [README](../README.de.md) |
| Install, update and troubleshoot | [Install](../INSTALL.md) | [Installieren](../INSTALL.de.md) |
| Controls, rounds and multiplayer | [Playing](PLAYING.md) | [Spielen](PLAYING.de.md) |

The installation summary inside the release archive comes from
[`packaging/INSTALL.txt.in`](../packaging/INSTALL.txt.in) and
[`packaging/INSTALL.de.txt.in`](../packaging/INSTALL.de.txt.in). Change both languages
alongside the web guide, then run `python3 scripts/check-docs-i18n.py`.

## Current development references

| Document | Purpose |
|---|---|
| [AGENTS.md](../AGENTS.md), [CLAUDE.md](../CLAUDE.md) | Integration workflow and technical contracts |
| [STATE.md](../.planning/STATE.md), [planning index](../.planning/INDEX.md) | Current build, open issues, hardware evidence and planning status |
| [DEVELOPING.md](DEVELOPING.md) | Build setup, packaging, assets and local gates |
| [CI-CD.md](CI-CD.md) | Hosted checks, main-branch release procedure, recovery and verification limits |
| [PATCH-INVENTORY.md](PATCH-INVENTORY.md) | Generated patch targets and registrations; regenerate with `scripts/patch-inventory.sh generate` |
| [NET-ACTION-SURFACE.md](NET-ACTION-SURFACE.md) | Reviewed receiver-patch classifications; checked by `check-desync-surface.py` |
| [Asset guide](ASSET-GUIDE-MITWIRKENDE.md) | German brief for the contributing artist; mesh, path and rig contracts |
| [Image guide](img/README.md), [video shot list](VIDEO-SHOTLIST.md) | Media provenance, generation and optional additional recordings |

The current changelog is the newest-first note block beside `NetProtocol.ModBuild` in
[`NetProtocol.cs`](../src/GloomhavenVR/Net/NetProtocol.cs). Test counts belong in the
build's evidence, not in a second frozen counter here.

## Historical design and hardware references

These retain their original paths because code, diagnostics or refactor contracts refer
to them. Their phase-specific defaults and expected behavior are historical; compare with
current source, the player guide and latest build notes before using them as a test plan.
An old successful test does not certify the current build.

| Document | Retained value |
|---|---|
| [TESTING-P1](TESTING-P1.md) | Startup and OpenXR failure triage; §4 is linked by runtime errors |
| [TESTING-P2](TESTING-P2.md), [P3A](TESTING-P3A.md), [P3B](TESTING-P3B.md), [P3C](TESTING-P3C.md), [P4](TESTING-P4.md) | Phase-era interaction and hardware regression cases |
| [TESTING-FULL-LOOP](TESTING-FULL-LOOP.md) | Original M4/v0.1 end-to-end route; use current round notes for later multiplayer requirements |
| [INTERFACES-P2](INTERFACES-P2.md), [INTERFACES-P4](INTERFACES-P4.md) | Interface design history and compatibility rationale; source contracts remain authoritative |
| [CAMERA-POLICY](CAMERA-POLICY.md) | Camera ownership and layer-policy rationale from hardware investigations |
| [PATCH-NOTES](PATCH-NOTES.md) | Hand-maintained patch rationale; actual targets are in the generated inventory |

Do not delete a historical file solely because of its age. First check its source and
diagnostic references; preserve or migrate those references when removing a contract.
