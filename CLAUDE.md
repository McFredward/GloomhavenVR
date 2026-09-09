# GloomhavenVR — read this first

A BepInEx 5 + HarmonyX mod that turns *Gloomhaven (Digital)* (Unity 2021.3.5f1, Mono, net472)
into a room-scale VR game. The board becomes a diorama on a table, the game's 2D UI is *converted*
into world-space panels on a wooden control board, and the player holds a real card fan.
Multiplayer is a first-class constraint, not an afterthought.

**Verified against `dev` at ModBuild 488, 2026-09-09.** If that is many builds behind, the newest
truth is the build-note block at the top of `src/GloomhavenVR/Net/NetProtocol.cs`, newest first —
that file is the project's real changelog. This one carries only what does not change per build.

## The user

Frederik (`frederik@lissek.info`, GitHub `McFredward`). **Always answer him in German.** Code,
comments, log strings, doc comments and agent prompts are English; user-facing strings are EN+DE
through `Core/Loc`. He tests on real hardware (Quest 3 over Virtual Desktop, PC-VR) every round
and reports numbered findings. A round is: read his report → dispatch workers on disjoint file
sets → review their diffs → merge → bump `ModBuild` → run the gates → push → write him a German
report.

**He is usually right about what he saw and often wrong about why.** Take the observation as data
and re-derive the cause. Several rounds were lost to accepting his diagnosis instead of his
symptom, and several more to explaining away a screenshot that showed the bug.

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
   The 38/40/42 holes may never be reused; 57 is next free.** No id
   has ever been retired or renumbered and none ever may be.
   **Card identity never goes on the wire** — reveals go only through `Net/RevealGate.cs`.
   `scripts/wire-tests.sh` (249,484 assertions at ModBuild 488) is the proof; a `Write`+`TryRead` change
   made in lockstep is invisible to a round trip, which is why the golden vectors exist.
4. **1:1 is a standing ruling.** A peer's board mirrors the owner's CONTENT, ANIMATION, ORDER,
   POSITION, SIZE, STATE and TIMING. A mirror must never read the viewer's dial. A shared
   window's size and pose may never depend on anything client-local. Every feature is
   multiplayer-compatible, syncs fully or not at all, and stays playable alongside flat/unmodded.
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
any change — read the printed verdict, not the status. Its baseline is per-worktree and
gitignored; take your own with `bash scripts/refactor-guard.sh baseline` before you start.

Current readings at 488: patch surface 107 classes / 165 methods · wire 249,484 assertions ·
config keys 625 · log tokens 4,713 · instrument-writes baseline 61 · bundle 74,943,763 bytes.
A number that has moved is not automatically wrong — but it must be explained in the commit.

**Read the NUMBER, not the word "green".** A deduplication in the 2026-09 round silently stopped
two wire vectors from being reached; the suite stayed green while the assertion count fell by two,
and only the count caught it. Print counts in commit messages.

**CI and the release workflow now run the same thirteen checkers**, plus one PR-only step: the
surface diff against the pull request's base, which needs a base commit and therefore cannot run
on a release. Until 2026-09-08 the RELEASE path ran eight fewer than CI — the path with no undo
was the weaker one. **The wire vectors cannot run on a hosted runner at all** (they need the
game's real `UnityEngine.CoreModule.dll` for `Mathf`'s banker's rounding), so run the full local
set before pushing to `main`.

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
  GitHub operations are additive and read-only; never destructive.
- **Delegate implementation.** The integrator lands the shared contract first, then splits the
  work by file so lanes cannot collide. Always give a lane `isolation: worktree`. **At most five
  lanes, and each lane may spawn at most ONE further worker** (his ruling, after a 5×4 fan-out hit
  the session limit and killed 21 agents mid-read).
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
- Commit trailer: `Co-Authored-By: Claude <model> <noreply@anthropic.com>` plus the session link.

## Privacy

The asset creator's real name never reaches a tracked file — handles only: **ARMA**, **JJ-Pueppi**.
The key in gitignored `/home/claw/gloomhaven_vr/.env` is never printed, echoed, pasted, or written
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
