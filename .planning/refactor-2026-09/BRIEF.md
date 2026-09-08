# Refactor 2026-09 — the lane brief

> Third refactor programme. `CHARTER.md` (2026-07) and `PLAN-2026-08.md` still govern; this file
> states only what is different this time and what every lane must do identically.
> Written 2026-09-08 against `dev` = `60beaa1f` (ModBuild 480). Tree: **626 files / 550 924 lines**
> (was 485 / 391 105 at the 2026-08 plan, 182 / 85 600 at the charter).

## 0. The request, verbatim

> *"Ich möchte von dir nun als nächstes eine Umfassende Refactoring-Phase. Mache Code-Reviews und
> refactore den Code. Ziel hierbei ist es den Code auf einen Wartbaren Stand zu halten ("Clean Code
> Prinzipien"), Redundanzen zu beseitigen, toten Code zu entfernen und auch Fehler, Lücken und
> Risiken zu entdecken. Nach einer ersten Review-Phase möchte ich das du direkt die Dinge fixed und
> refactorst die du gefunden hast. … Versuche regression zu vermeiden."*

Four goals, in his order: maintainability, redundancy, dead code, **defects/gaps/risks**. Two
phases: review first (written down), then fix. And the constraint that outranks all four goals:
**no regression**. Every rule below serves that constraint.

## 1. What is different from the charter

1. **Defects are IN scope.** The charter's Tier 3 ("behavioural — not part of this refactor") is
   lifted for one case only: a defect that is *demonstrable from source* — you can state the
   input/state that produces the wrong output. Fix it minimally, put the demonstration in the
   commit message, and add a line to `HARDWARE-REGRESSION-2026-09.md` (your lane's section)
   naming what he should observe and the log token that proves it ran. No speculative
   "improvements", no tuning changes, no re-designs. If a fix needs a design decision, it is a
   finding, not a commit.
2. **Parallel construction is in scope.** `.planning/redundancy-audit.md` §0 defines the class:
   *two implementations of one concept the player experiences as one thing*. A text scan cannot
   see it; reading can. When both copies are in your file set, merge onto the better one and
   record the side-by-side in the commit; when they are not, it is a finding with both paths.
3. **Comments are not clutter here.** The tree is ~45 % comment by design: reason-comments that
   name the user report, the root cause, the rejected alternatives. Never strip them for "clean
   code". Change a comment only when it is factually WRONG — and then fix the sentence, do not
   delete it. Every claim in a comment is a hypothesis (`an-assertion-in-the-source-is-a-hypothesis`);
   a review that falsifies one against source is a first-class finding.
4. **Five lanes, disjoint file sets, one integrator.** You edit only your set. A change you need
   outside it goes into `NEEDED-OUTSIDE-<lane>.md` as an exact diff with its reason. A rename
   that would touch another lane's file is therefore forbidden — keep cross-set APIs stable.

## 2. Lanes and file sets

| lane | owns (everything under these paths, nothing else) | ≈ lines |
|---|---|---|
| **worldui-front** | `src/GloomhavenVR/WorldUI/{Modal,MapRoom,Surfaces,Composites,Tooltips,Buttons,FlatScreen}/`, `src/GloomhavenVR/WorldUI/*.cs` (the 14 root files) | 117 k |
| **worldui-frame** | `src/GloomhavenVR/WorldUI/{Conversion,Options,Patches,Sharpness,Materialise,Grab}/`, `src/GloomhavenVR/Board/`, `src/GloomhavenVR/Hands/` | 123 k |
| **net** | `src/GloomhavenVR/Net/`, `tests/GloomhavenVR.WireTests/` | 135 k |
| **core** | `src/GloomhavenVR/Core/`, `src/GloomhavenVR/Rig/`, `src/GloomhavenVR/Voice/`, `src/GloomhavenVR/Compat/`, `src/GloomhavenVR/Defaults/` | 124 k |
| **cards** | `src/GloomhavenVR/Cards/`, `src/GloomhavenVR/*.cs` (Plugin.cs etc.), `scripts/`, `.github/workflows/`, `docs/` | 96 k |

Shared artefacts, and who may touch them:

- `.planning/refactor-2026-09/REVIEW-<lane>.md`, `NEEDED-OUTSIDE-<lane>.md`, your own section of
  `HARDWARE-REGRESSION-2026-09.md`: **yours**.
- `.planning/refactor/*.allow`, `FRAME-ORDER.lock`, `INSTRUMENT-WRITES.baseline`,
  `STALE-DOC-REFS.md`: edit only the entries that name files in your set (retire an entry when
  its code changes; the gates are self-retiring and will tell you).
- `docs/PATCH-INVENTORY.md` is **generated**. If your change moves or renames a patch class,
  `patch-inventory.sh check` fails; run `bash scripts/patch-inventory.sh generate` and commit
  the result — the integrator regenerates again at the end.
- `src/GloomhavenVR/Net/NetProtocol.cs`: lane **net** owns it but **must not bump `ModBuild`**
  and must not change any `Write`/`TryRead` byte. The integrator bumps once.
- `Core/Loc/*`, `Defaults/*`: lane **core** owns them; everyone else files NEEDED-OUTSIDE.
- `decompiled/` is absent from worktrees; read the game source at
  `/home/claw/gloomhaven_vr/decompiled/` (absolute path, READ-ONLY).

## 3. The protocol — identical for every lane

### 3.0 Before touching anything

```bash
WT=<your worktree, absolute>
git -C "$WT" log --oneline -1          # MUST be 60beaa1f or a descendant (the BRIEF commit or later)
# if not:  git -C "$WT" merge --ff-only <the BRIEF commit sha named in your prompt>
cd "$WT" && bash scripts/worktree-setup.sh
cd "$WT" && bash scripts/refactor-guard.sh baseline     # YOUR OWN baseline, taken at YOUR HEAD
cat "$WT/.planning/refactor/.guard/baseline.rev"        # must equal git rev-parse HEAD
```

Agent worktrees are created from `origin/main`, which is hundreds of commits behind `dev`
(`git-worktree-merge-hazards` §4 — it has happened to every lane in every round). A file named
in this brief that "does not exist" is the stale base talking. **State your base SHA in your
report.** Take the baseline yourself: the linked one from the main checkout is not yours and may
not match your HEAD.

### 3.1 Phase 1 — review, written down, committed BEFORE any source change

Deliverable: `.planning/refactor-2026-09/REVIEW-<lane>.md`, one commit, no `src/` change in it.
Start with the instruments, then read:

```bash
python3 .planning/refactor/census-2026-08/hygiene2.py <your paths>     # methods by CODE lines
python3 .planning/refactor/census-2026-08/dupes2.py   <your paths> 12  # text duplication (blind to parallel construction)
python3 .planning/refactor/census-2026-08/loadbearing.py <your paths>  # instrument writes (upper bound)
```

Read `.planning/refactor/INVARIANTS-*.md` and `REVIEW-*.md` for your subsystem first — they
record what must not change and why, and what was already judged. Then the code.

Findings, each with: file:line, **class** (dead / duplication / parallel-construction /
structure-naming / defect / risk-gap / doc-drift), **tier** (0 dead · 1 motion · 2 dedup ·
3 defect), the **evidence** (for a defect: the failing input → wrong output; for dead code: the
§5 checklist below), the **proposed action**, and the **guard expectation**. Rank by risk
reduced, not by size. A finding you will NOT act on still goes in, with the reason — the
negative results are worth as much as the positive ones (`PLAN-2026-08.md` §0.2).

**Sub-agents: at most ONE alive at a time per lane** (his ruling, 2026-09-08, after a 5×4
fan-out hit the session limit and killed all 21 agents mid-read). Sequential read-only helpers
are fine; give each the §5 checklist and this file's path. **Commit `REVIEW-<lane>.md` early
and incrementally** — a partial review on disk survives a limit kill, an agent's context does not.

### 3.2 Phase 2 — fix, in tier order, one tier per commit

Order: Tier 0 → 1 → 2 → 3. One subsystem per commit, one tier per commit, build green at every
commit, and **the guard's `--summary` output pasted into every commit message**:

```bash
cd "$WT" && bash scripts/refactor-guard.sh check --summary
```

Expected per tier (`CHARTER.md` §3): Tier 0 — the member disappears, nothing else; Tier 1 —
empty, or `MOVED` only on `GloomhavenVR.csproj`, never on a type file (a `MOVED` type file is a
reordered field initialiser until proven otherwise); Tier 2 — `CHANGED` confined to the touched
types; Tier 3 — `CHANGED` confined to the type you meant, and the diff read line by line.
Anything in a type you did not intend to touch is collateral — revert.

Editing sub-agents: at most one at a time, in your worktree — **never two builds at once in one
worktree** (`obj/` collides). They do not push, do not stash, do not
`cd` elsewhere.

### 3.3 Before your last commit — the full suite

```bash
cd "$WT" && bash scripts/refactor-guard.sh check --summary     # runs 16 checkers + the compiled diff
cd "$WT" && EXPECT_WARNINGS=0 bash scripts/ci-build.sh          # 0 warnings, TreatWarningsAsErrors
cd "$WT" && python3 scripts/check-docs-i18n.py
cd "$WT" && bash scripts/wire-tests.sh                          # already inside check; run again if Net/ or tests/ moved
```

All green, or the report says exactly which line is red and why you left it.

### 3.4 The report (your final message)

1. Base SHA, worktree path, branch name, every commit (hash + one line), guard verdict per commit.
2. Findings acted on / findings deferred, with the reason for each deferral.
3. `NEEDED-OUTSIDE-<lane>.md` contents (exact diffs).
4. Hardware-observable changes (should be empty for Tier 0–2; Tier 3 lines with their tokens).
5. **Anything in this brief that was wrong.** Every round so far, this section has been the most
   valuable one.

## 4. Hard rules (any one of these broken = the commit is reverted at integration)

- **No wire-format change.** No `ModBuild` bump. No new record id (46 is reserved for the
  short-rest bit, not for you).
- **No config key removed or renamed** (the player's persisted value would revert silently).
  `check-surface.py` fails on it; do not route around it.
- **No log grep token removed or reworded.** Same gate. In particular these ModBuild 480 lines
  are OWED ON HARDWARE and untouchable: `Remote BURN look`, `DOCK MIRROR`, `GATE 3`,
  `NOT ASKED`, `REMOTE GLOW BLEND`, `SHORT REST PILE COVER`, `HELD BAR HIDE REFUSED`,
  `BURN ANIM STUCK`, `BURN ANIM FLAG LATCHED`, `MAP STORY SEND RATE`, `MAP PLACARD SCALE`.
- **No tuning value change** (anything in `Defaults/`, any `Bind(` default, any literal a
  hardware round settled). A number that looks arbitrary is the residue of a bug.
- **No frame-order change.** `FRAME-ORDER.lock` names the locked orderings; the guard cannot
  see a reorder inside a type.
- **No Harmony target change**, no patch on `ScenarioRuleLibrary`, Photon Bolt or
  `FFSNet.NetworkManager`; never write game state from presentation code.
- **Mirror pairs are design, not duplication.** A local ↔ remote pair (`check-mirrors.sh`,
  `check-remote-defaults.py`) is merged only if the two bodies are byte-identical after
  normalisation AND the merge keeps the remote side reading the OWNER's value, never the
  viewer's dial (`check-mirror-dials.py`). If they differ, write down why they differ instead.
- **1:1 is a standing ruling**: a peer's board mirrors the owner's CONTENT, ANIMATION, ORDER,
  POSITION, SIZE, STATE and TIMING; a shared window's size/pose is never client-local. A refactor
  that would let a mirror read a local value is a regression even if the guard is empty.
- **Instruments.** Before deleting, gating or moving any `Log*`/`Census*`/`Report*`/`Dump*`
  method, grep its body for a write and run `check-instrument-writes.py`: deleting a spent
  logger once nearly latched the wall fade off forever (`a-write-inside-a-logger`). Prefer to
  leave an instrument in place over retiring it; if you retire one, its token must not be one
  a doc or a backlog item still greps for (`grep -rn '<token>' .planning docs`).
- **Git:** never push, never `git stash` (`refs/stash` is shared across worktrees), never a
  destructive command, absolute paths everywhere, commit on your own branch only. Commit trailer:
  `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` and
  `Claude-Session: https://claude.ai/code/session_019XYDTbtxYgN6KuK3beHk3Q`.
- **Privacy:** the asset creator's real name never reaches a tracked file (handles only: ARMA,
  JJ-Pueppi). The key in `/home/claw/gloomhaven_vr/.env` is never printed, pasted or written.
- **Language:** code, comments, log strings, docs in English. User-facing strings EN+DE via
  `Core/Loc` (lane core; others file NEEDED-OUTSIDE).

## 5. "Unused" must be checked against (CHARTER §5, extended)

Before deleting anything, prove it is not:

1. a Harmony patch target or annotated patch method (`docs/PATCH-INVENTORY.md`);
2. a Unity message (`Awake/Start/Update/LateUpdate/OnDestroy/OnEnable/OnDisable/OnValidate/…`)
   or a serialized field;
3. reached by reflection or by name from the game (`AccessTools`, `nameof`, string literals,
   `Traverse`, `GetMethod(`);
4. a config key (§4) — an unread key is still a player's setting; mark it INERT in its
   description instead (precedent: `ScenarioWindowBoardClearanceMeters`, ModBuild 480);
5. a log grep token a doc or backlog item relies on;
6. referenced only from the debug menu (a feature);
7. a wire-test vector or a `tests/` shim pin (`Shims.cs` pins source files by path);
8. the ONLY writer of a field an instrument reads (the instrument then lies).

`git log -S'<name>'` is cheap and tells you whether the thing was deleted on purpose before.

## 6. What the census says at 60beaa1f (whole tree, for calibration)

- 8 184 methods; 88.2 % ≤ 50 code lines; 216 over 100; 42 over 200. The 40 largest are in
  `.planning/refactor-2026-09/census/hygiene2.txt`; seven of them are `Bind` monoliths and nine
  are diagnostics (`Log*`/`Report*`/`Census*`). The largest non-diagnostic non-Bind methods:
  `NetAvatarDriver.TickExtrasSend` 1 091, `PresenceState.TryRead` 987 / `Write` 805,
  `WallSegmentFade.Mounted.CollectWallMountedProps` 610, `RemoteAvatar.SetExtras` 400,
  `ArcSeats.TryClaimArcSeat` 389, `ModalFallback.4.Tick.Tick` 417.
- 167 text-duplication groups ≥ 12 lines, 154 cross-file. The same eight families as 2026-08 lead
  (`PropInfoSurface`↔`StatPanelSurface` 58, `RemoteControlBoard`↔`PileViewer` 35,
  `RemoteHandFan`↔`CardFan` 27, `DamageTooltipSurface`↔`DecisionDockSurface` 26,
  `WallSegmentFade.Body`↔`.Stacked` 26, `LoadoutConfirmPark`↔`StoryComposite` 25,
  `RemoteFanEnhancementRefresh`↔`HandFanEnhancementRefresh` 24). Two of those are mirror pairs.
- Module cycles: `WorldUI↔Core` (1 793 / 17 — the 17 are `CanvasConversion`, `MapRoomDriver`,
  `SelfUpdateDialog` reached from Core), `WorldUI↔Net` (168 / 417), `WorldUI↔Cards` (216 / 200).
  Full table in `census/layers.txt`.
- `STALE-DOC-REFS.md` still lists 21 demoted crefs, none cleared since 2026-08-27.
- The 2026-08 deferred list (`LOG-2026-08.md` Phase 4/5): `RemoteBoardFurniture`,
  `StoryComposite`, `CardsGameApi`, `WaterTerrainVR`, `CardFan`, `NetAvatarDriver`,
  `MapTableLegs`, the `PresenceState` serializer pair, the six `Bind` monoliths; and the 66
  load-bearing instrument writes in `INSTRUMENT-WRITES.baseline` (33 in `FadeDriver`).

None of this is an instruction to act; it is where the reading starts.
