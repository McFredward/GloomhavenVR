# GloomhavenVR — read this first

A BepInEx 5 + HarmonyX mod that turns *Gloomhaven (Digital)* (Unity 2021.3.5f1, Mono, net472)
into a room-scale VR game. The board becomes a diorama on a table, the game's 2D UI is *converted*
into world-space panels on a wooden control board, and the player holds a real card fan.
Multiplayer is a first-class constraint, not an afterthought.

**Verified against `dev` at ModBuild 497, 2026-09-10.** If that is many builds behind, the newest
truth is the build-note block at the top of `src/GloomhavenVR/Net/NetProtocol.cs`, newest first —
that file is the project's real changelog. This one carries only what does not change per build.

## The user

The maintainer is **McFredward**. Answer the maintainer in German. Code, comments, log strings,
doc comments and developer documentation are English; player-facing strings are EN+DE through
`Core/Loc`. Hardware tests use Quest 3 over Virtual Desktop. Treat observations as evidence and
verify proposed causes against source, current build banners and supplied screenshots.

`AGENTS.md` adapts these technical contracts to the current agent workflow and takes precedence
over older Claude-specific instructions retained in historical planning records.

## Where the truth lives

| Question | Authority |
|---|---|
| What happened in build N, and why | the build note above `ModBuild` in `Net/NetProtocol.cs` |
| Which planning doc is still live | `.planning/INDEX.md`, then `.planning/STATE.md` |
| What is patched, by whom | `docs/PATCH-INVENTORY.md` (**generated** — `scripts/patch-inventory.sh generate`) |
| Why a patch exists and what it costs | `docs/PATCH-NOTES.md` |
| What must not change, per subsystem | `.planning/refactor/INVARIANTS-*.md` |
| Refactor rules and risk tiers | `.planning/refactor/CHARTER.md` |
| Frame-order contracts | `.planning/refactor/FRAME-ORDER.lock` |
| Every shipped default | `src/GloomhavenVR/Defaults/*.cs`, one annotated line each |
| Anything user-facing | `src/GloomhavenVR/Core/Loc/` |
| How to build, test, release | `docs/DEVELOPING.md`, `docs/CI-CD.md` |

**The code is the documentation.** This project writes long block comments that state the user's
report verbatim, the root cause, the evidence, and the alternatives that were tried and rejected.
Write that comment when you change something. Several rounds have been saved by a comment saying
"do NOT swap this back, here is why" — and several defects have been protected by a comment that
was simply wrong, so treat every claim in a comment as a hypothesis and check it.

## Hard rules — breaking one is a defect, not a style choice

1. **Mod-side only.** Never modify game data. `ressources/`, `libs/` and `decompiled/` are
   READ-ONLY. Never patch `ScenarioRuleLibrary`, Photon Bolt or `FFSNet.NetworkManager`. Never
   write game state from presentation code.
2. **`NetProtocol.ModBuild` +1 on every build handed to another player**, with a full build note.
   It is the multiplayer handshake key; mismatched peers get a blocking dialog.
3. **Wire:** magic `GVR1`, `Version` byte stays **3**, every change is an **additive TLV** record
   that old readers skip by length. **44 presence record IDs exist through 47; transport-envelope TLV 48 carries unchanged
   snapshots in message type 2. Native presentation streams use TLVs 49–53; presence adds flight provenance54 and native
   highlight55; native tooltip emitters use56 in the existing plume stream.
   Health57, native card appearance58, atomic rig actor59, character decisions60, flight source61,
   flight history62, native decision prompts63, lossless presentation compression64 and damage avoidance65 are additive.
   Second held actor66, held map provenance67 and native appearance provenance68 retain positional
   source addressing. The 38/40/42 holes may never be reused; 69 carries supplemental native card groups; 70 carries rig board pose and 71 fan insertion; 72 carries shared native video presentation; 73 carries shared native reward presentation; 74 carries key/opening-scoped reward pose participation; 75 carries owner burn progress and durable completion with or without a flight; 76 carries original item appearance; 77 carries shared map-window grip and automatic-motion masks; 78 carries original town-service presentation; 79 carries persistent town resident poses and animation clocks; 80 carries shared facial pose, expression clock and speech-adapter state, also in message type 21; 81 carries resident occupation clocks and attention transitions, paired atomically with unchanged80 in message type22; 82 carries native map-button hints; 83 is next free.** No id
   has ever been retired or renumbered and none ever may be.
   **Card identity never goes on the wire** — reveals go only through `Net/RevealGate.cs`.
   `scripts/wire-tests.sh` (final assertion count in STATE.md at ModBuild 497) is the proof; a `Write`+`TryRead` change
   made in lockstep is invisible to a round trip, which is why the golden vectors exist.
4. **1:1 is a standing ruling.** A peer's board mirrors the owner's CONTENT, ANIMATION, ORDER,
   POSITION, SIZE, STATE and TIMING. A mirror must never read the viewer's dial. A shared
   window's size and pose may never depend on anything client-local. Every feature is
   multiplayer-compatible, syncs fully or not at all, and stays playable alongside flat/unmodded.
   **Card concealment is remote-only** (user clarification, 2026-09-09, build 490 review).
   Local controlled-character cards always retain their fronts, including short-rest burn flights.
   Remote selection cards and remote short-rest burn flights remain covered; action cards are open.
   The entire 3D map is public, locally and remotely (build 490 hardware clarification).
5. **Localisation:** every user-facing string EN+DE via `Core/Loc`. **Config:** every default on
   one annotated line in `Defaults/` with a `// => [Section] Key` comment, and a new key must end
   in a unit word `ConfigSteps` recognises or its stepper is unusable (user-reported twice; pinned
   by `tests/GloomhavenVR.WireTests/ConfigStepVectors.cs`).
6. **A config key is never removed or renamed** — the player's persisted value would silently
   revert. Mark it INERT in its description instead. **A log grep token is never reworded** — a
   hardware round greps for it. `scripts/check-surface.py` fails on both.
7. **Lights are never hidden or written by any visibility system; figures are never touched by
   any wall/visibility system.** Floor tiles never occlude and never fade.
8. **Everything that fades or moves does so WITH the animation.** Popping is unacceptable. One
   exception, granted by him: the decision area's collapse is instant.
9. **His approved look outranks geometric or technical correctness.**
10. **Window facing is yaw-only** — no window is ever pitched or rolled.

## The gates — run all of them before you push

```bash
bash scripts/refactor-guard.sh check --summary   # 17 checkers, then the compiled-form diff
bash scripts/ci-build.sh Release                 # 0 errors, 0 warnings (TreatWarningsAsErrors)
python3 scripts/check-docs-i18n.py               # EN/DE docs agree
```

`EXPECT_WARNINGS=0` is assigned INSIDE `ci-build.sh` and is not read from the environment, so the
`EXPECT_WARNINGS=0 …` prefix you will find in older commit messages and docs does nothing. It is
harmless, but it is not what makes the gate strict — the assignment in the script is.

The guard's **exit code is 1 whenever the compiled form differs at all**, which is normal after
any change — read the printed verdict, not the status. Baselines are gitignored. Worktree setup
links the integration baseline for read-only comparisons; before creating a fresh worker baseline,
remove only the worker symlinks for `.planning/refactor/.guard/baseline` and `baseline.rev`, then
run `bash scripts/refactor-guard.sh baseline`. Never write through a shared baseline symlink.

Current readings at 497: patch surface 109 classes / 167 methods · wire 253,579 assertions ·
config keys 625 · log tokens 4,716 · instrument-writes baseline 61 · bundle 74,943,763 bytes.
The production card capture, native playback and board refresh harnesses add 18,206 / 466 / 1,216
assertions. Twelve runtime negative controls cover hierarchy, snapshots, shader changes, output
writes, identity/recovery edges and allocation regressions.
A number that has moved is not automatically wrong — but it must be explained in the commit.

**Read the NUMBER, not the word "green".** A deduplication in the 2026-09 round silently stopped
two wire vectors from being reached; the suite stayed green while the assertion count fell by two,
and only the count caught it. Print counts in commit messages.

The workflow definitions and [CI/CD guide](docs/CI-CD.md) describe hosted validation. Full CI
on `dev` runs the source checks and production presentation harnesses. Per the user's
2026-09-17 instruction, a PR may reuse successful trusted validation of the identical Git tree;
otherwise it runs the full checks. Releases require that evidence before building and packaging
the actual `main` commit, without repeating the full suite. Missing or invalid evidence blocks
publication. Full golden wire vectors still require the game's real Unity runtime and must run
locally; hosted runners compile them against metadata references. A hosted green run does not
replace that local gate.

**Bundles are built ONLY with `/home/claw/unity-2021.3.5`**, never `unity-2021.3`. The wrong
editor produces a bundle that loads nothing and fails silently into the procedural fallback;
`scripts/check-bundle-format.sh` is the gate. The generated assets are committed, so a change to
`unity/.../Editor/BuildHands.cs` or `BuildEnvironments.cs` does nothing until you run that
generator (`-executeMethod GloomhavenVR.HandsBuilder.Build` / `GloomhavenVR.EnvironmentsBuilder.BuildAll`)
and then rebuild the bundle. Builds 369–482 were DLL-only drops; **483 changed the bundle**, so
it is a full install.

## Working practice

- **`main` is the release branch; work goes to `dev`.** Push every change to `origin/dev`
  unasked. Never push a worktree branch to the remote — only `main` and `dev` exist on GitHub.
  Do not rewrite published history or change repository visibility without explicit authorization.
- **Delegate independent implementation tasks** with explicit, disjoint file ownership. Create
  separate Git worktrees before dispatch; a tool call does not imply filesystem isolation. Respect
  the current session's concurrency limit and avoid nested workers when no slot is available.
- **Codex worker worktrees start at the current `dev` integration commit** (AGENTS.md, user's
  2026-09-08 ruling). Every lane states its base SHA and disjoint owned files; initialize its
  dependencies with `scripts/worktree-setup.sh`. Never base new work on the older release branch.
- **Never `git stash`** — `refs/stash` is shared across worktrees. **Never `cd`** in an
  integration round; use `git -C <worktree>` and absolute paths, or the next command silently runs
  somewhere else. Restrict merge patches to a lane's OWNED paths, never a broad `-- src/`.
- **Commit after every step.** Model-limit kills happen mid-round; work on disk survives what an
  agent context does not. After a kill, snapshot each worktree's uncommitted state as a WIP commit
  BEFORE resuming the lane.
- Hardware logs go in gitignored `.planning/debug/`. Triage them with
  `python3 scripts/log-triage.py`, and anchor every grep on `GloomhavenVR] `.
- Attribute commits truthfully. Do not add a Claude coauthor or session link to Codex work.

## Privacy

The asset creator's real name never reaches a tracked file — handles only: **ARMA**, **JJ-Pueppi**.
The key in gitignored `.env` is never printed, echoed, pasted, or written
into any tracked file.

## How this project has actually lost time

Every line here was paid for. They are the difference between a round that answers a question and
a round that does not.

- **A green suite is evidence about the classes it encodes and nothing else.** In 2026-09, sixteen
  gates were green through nineteen defects, and two of the worst were shipped by the build that
  closed the previous report. A gate is written the day a class is understood, so the newest class
  is always ungated — **read the last build's own diff as a suspect.**
- **Verify the instrument before believing the reading.** Instruments here have: measured the
  bookkeeping instead of the picture, asserted a cause they could not observe, printed a green
  verdict on a failed build, gone silent because a cap ate the burst, and been inverted against
  their own doc comment. A new instrument's first output is a hypothesis.
- **A gate needs a negative control.** A checker that cannot fail passes everything. Plant the
  defect, watch it fail, then confirm the same text in a comment does not trip it.
- **Measure the picture, not the state.** Clean state readings agreed with every wrong hypothesis
  for eight rounds; the defect was in sampling, which no state probe can see.
- **The blind spot is the lead.** When eight scans come back clean, the defect is in what no scan
  covers.
- **A claim in a comment is a hypothesis.** Six defects in one round were each protected by a
  comment asserting the opposite, with the falsifying reading already in the logs.
- **Ask the extent of a symptom before building an instrument for it.** Eight builds measured text
  while the defect also took images.
- **Open the screenshot first.** Three rounds tuned a term on a surface whose visible pixels were
  a different colour than assumed.
