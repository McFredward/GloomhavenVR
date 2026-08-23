# CI/CD — how GloomhavenVR builds and releases itself

Developer document. Everything here is verified against the scripts and the shipped
build, not assumed. Where something cannot be done, it says so instead of pretending.

---

## 0. The one fact everything else depends on

**A GitHub-hosted Ubuntu runner can produce the complete, installable release zip.**
Not a partial one. Verified end to end by running the real scripts with the game
install hidden, exactly as CI sees it:

| Piece | Where it comes from on the runner | Size |
|---|---|---|
| `GloomhavenVR.dll` | compiled against `libs/RefAsm` (committed metadata stubs) | 6.4 MB |
| `GloomhavenVR.Preload.dll` | compiled, no game references at all | 31 KB |
| `RuntimeDeps/Unity.XR.*.dll` | `scripts/build-runtimedeps.sh` — compiled from needle-mirror package source at pinned **tags** | 270 KB |
| `Natives/openxr_loader.dll`, `UnityOpenXR.dll` | `scripts/fetch-natives.sh` — SHA256-pinned download from the OpenXR package | 2.7 MB |
| `gloomhavenvr.bundle` | **already in the repository** at `prebuilt/gloomhavenvr.bundle` | 70.2 MB |
| `INSTALL.txt` | rendered from `packaging/INSTALL.txt.in` | 3.6 KB |

Result: `dist/GloomhavenVR-<version>.zip`, **73,894,279 bytes**, `package-release.sh`'s
own layout assertions passing.

Two things that are often assumed and are worth stating plainly:

* **The 70 MB asset bundle is not a problem, because it is committed.**
  `prebuilt/gloomhavenvr.bundle` is a tracked blob in `main`'s history (it has been
  byte-identical at 70,218,494 bytes since ModBuild 172). `package-release.sh` falls
  back to it whenever a freshly built bundle is absent, which on a runner is always.
  So **a release is ~74 MB, not ~300 KB**, and `scripts/check-bundle-format.sh` runs
  in CI in full — it is not skipped.
* **Nothing about the pipeline requires a self-hosted runner.** That option was on the
  table and is not needed.

The zip layout is exactly what `scripts/package-release.sh` and
`packaging/INSTALL.txt.in` define. The pipeline does not change it — the in-game
auto-updater can rely on it.

---

## 1. Shape

```
        work                                     release
   ┌──────────────┐                        ┌──────────────────┐
   │     dev      │  ── git push dev:main ▸│       main       │
   └──────────────┘                        └──────────────────┘
   ci.yml                                   release.yml
   push + pull_request                      push
   build, gates                             bump, build, package,
   no release, no tag, no bump              tag, GitHub Release
```

* **`dev`** is the working branch. Everything lands there. Its builds are never
  released — that is the decision, and `ci.yml` has no code that could release.
* **`main`** is the release branch. **A push to `main` publishes a release.** There is
  no button, no approval step and no dry run. Pushing `dev` into `main` *is* the
  release action.
* There is no `master` branch and there is not meant to be one.

`main` currently sits at the same commit as `dev` (`4324f4d`), so the first release
push is an ordinary fast-forward.

---

## 2. What YOU do by hand — the complete list

Four things, and only one of them is required before the first release.

### 2.1 Allow the release workflow to push back (REQUIRED, one time)

`release.yml` commits the version bump and a tag back to `main`. It declares
`permissions: contents: write`, and **`GITHUB_TOKEN` with that scope is sufficient —
no Personal Access Token is needed.** The workflow-level `permissions:` block raises
the token's scope above the repository default, so in principle this works out of the
box. Set the repository default to match anyway, so a 403 can never be the surprise
that eats a release:

> **Settings → Actions → General → Workflow permissions**
> select **“Read and write permissions”** → **Save**

Leave “Allow GitHub Actions to create and approve pull requests” **off**; nothing here
opens a pull request.

**Do not put a branch protection rule on `main` that requires a pull request or
restricts who can push.** That is the one configuration that would break the push-back,
and it is the only case where a PAT (or a bypass entry for the bot) would become
necessary. If you ever want that protection, add
`github-actions[bot]` to the rule's bypass list rather than switching to a PAT.

### 2.2 Make `dev` the default branch (OPTIONAL, one click)

Only cosmetic — it decides what a visitor and a fresh `git clone` land on, and where a
new pull request points by default. Nothing in the pipeline depends on it.

> **Settings → Branches → Default branch → ✎ → `dev` → Update**

`main` is left exactly where it is either way. There is no rename, nothing is deleted
and no history moves.

### 2.3 Release (whenever you want one)

```bash
git push origin dev:main
```

That single command fires `release.yml`, which bumps the patch version, builds,
packages and publishes. **It is a real release the moment you press enter.** There is
no undo beyond deleting the release and the tag afterwards.

Before you type it, three things are worth having done:

```bash
scripts/wire-tests.sh        # the golden vectors (146,839 assertions) — CI CANNOT run these (see §5)
scripts/build.sh Release     # sanity: 0 errors, 6 warnings
git log --oneline main..dev  # what is about to go out
```

**The very first release** is a special case worth knowing about: `main` has 2,140
commits of history and no tags, so there is no previous release to compare against.
`scripts/release-notes.sh` handles this deliberately — it does **not** print 2,140
commit subjects (that would exceed GitHub's 125,000-character release-body limit and
the API call would fail outright). It prints one honest sentence — *"First release …
2,140 commits of development from 2026-07-14 to <date> published for the first
time"* — plus a link to the full commit history. Every later release lists the real
commit subjects since the previous tag, capped at 100 with an explicit "… and N more".

One failure mode to be aware of: a workflow runs from the version of itself **at the
pushed commit**. If you ever push a commit to `main` that predates `.github/workflows/`,
*nothing happens at all* — no run, no error, no release. Push `dev` (which contains
the workflows) and this cannot occur.

### 2.4 Regenerate the reference assemblies after a game update (REQUIRED, then)

The runner compiles against stripped stubs of the game's assemblies. A Gloomhaven
patch changes the signatures those stubs describe.

```bash
dotnet tool install -g JetBrains.Refasmer.CliTool   # once per machine
scripts/make-refasm.sh                              # after every game update
git add libs/RefAsm && git commit -m "chore(refasm): regenerate against game build <x>"
```

Note the package name: `JetBrains.Refasmer` is the *library* and installs no command;
the CLI is `JetBrains.Refasmer.CliTool` and its command is `refasmer`.

The script is deterministic — if the game did not actually change, the regeneration
produces byte-identical files and `git status` stays clean. If you skip this after a
game update, CI goes red (or, worse, stays green) for a defect that only exists in the
stubs. Details in `libs/RefAsm/README.md`.

### 2.5 When the repository goes public

Nothing breaks and nothing needs changing. One capability *appears*: while the
repository is **private**, release assets require an authenticated download, so the
planned in-game auto-updater cannot fetch them. The moment the repository is public,
`https://github.com/<owner>/<repo>/releases/latest/download/GloomhavenVR-<v>.zip`
becomes an anonymous download and the updater works. The release workflow prints a
notice saying exactly this on every run.

---

## 3. `ci.yml` — the working branch

Triggers on **push and pull_request to `dev`**. Concurrency group per ref with
`cancel-in-progress: true`: a newer push supersedes an older run, because the old run
is answering a question nobody is asking any more.

| Step | What it proves |
|---|---|
| `check-refasm.py` | the committed stubs still contain zero method bodies |
| `build-runtimedeps.sh` | the three shipped Unity XR assemblies still compile |
| `ci-build.sh Release` | 0 errors and **exactly 6** warnings, all known |
| `check-mirrors.sh` | no mirrored constant was tuned in only one place |
| `check-frame-order.sh` | no locked per-frame order moved |
| `patch-inventory.sh check` | no Harmony patch class went unregistered |
| `check-wire-coverage.py` | wire fields are covered |
| `rebase-defaults.py check` | config defaults have not drifted |
| `check-bundle-format.sh` | the committed bundle is still UnityFS format 7 / 2021.3.5f1 |
| wire tests **compile** | no wire file was moved or renamed (the vectors do not run — §5) |

It also uploads `GloomhavenVR.dll` + `GloomhavenVR.Preload.dll` as a 14-day workflow
artifact on pushes. That is the *whole* useful payload for a hardware test — every
install since ModBuild 172 has been plugin-DLL-only because the bundle has not
changed — and it costs 6 MB per push instead of the 74 MB a full zip would cost
against a private repository's artifact storage.

### The warning gate

`scripts/ci-build.sh` asserts 0 errors and exactly 6 warnings. `TreatWarningsAsErrors`
cannot be switched on while those six pre-existing nullable-analysis complaints exist,
so without the count a seventh warning would be invisible.

It checks the **count**, the **codes** (`CS8602`, `CS8604` only) and the **files**
(`ButtonCluster.cs`, `RemotePickBanner.cs`, `RemoteHandFan.cs`, `StatPanelSurface.cs`
only). It deliberately does **not** check line numbers: those move whenever the
surrounding code is edited, and a gate that cries wolf gets switched off. When someone
finally fixes one of the six, the gate fails and tells you to lower the number — that
is the intended ratchet.

---

## 4. `release.yml` — the release branch

Triggers on **push to `main`**. Concurrency group `release-main` with
**`cancel-in-progress: false`**: releases queue, they never cancel each other, because
a half-published release is worse than a slow one.

Order of operations, and why it is that order:

1. **Bump, commit, tag — before the build.** `scripts/bump-version.sh --patch` rewrites
   `<Version>` in `src/GloomhavenVR/GloomhavenVR.csproj`. Committing *before* building
   means the compiled DLL is stamped with a clean tree and with the exact commit the
   tag points at. Build first and the git stamp reads `…-dirty`, which is a bug report
   waiting to happen.
2. **Build with `GhvrReleaseBuild=true`**, set as a **job-level environment variable**.
   It has to be the environment and not `-p:` on one command, because
   `package-release.sh` runs its own `dotnet build` internally and a `-p:` would not
   reach it. MSBuild reads environment variables as properties, so it does.
   This is what makes `BuildInfo.IsDevBuild` false; every other build in the project is
   a dev build and shows the short commit hash in-game.
3. **The same gates as CI**, plus `fetch-natives.sh`.
4. **`package-release.sh`**, then an explicit assertion that
   `dist/GloomhavenVR-<version>.zip` exists under that exact name and that the asset
   bundle is inside it.
5. **Push the bump and the tag atomically**, then create the release. In that order, so
   that a rejected push leaves no tag, no release and no orphan.

### The three hazards

**Infinite loop.** Step 5 pushes to `main`, which is this workflow's own trigger. Two
independent guards:
1. *Primary:* a push made with `GITHUB_TOKEN` does not start a new workflow run. This
   is documented, deliberate GitHub behaviour and exists precisely for this case.
2. *Belt to that brace:* the bump commit's subject ends with `[skip ci]`, which GitHub
   honours for `push` events. If the token is ever swapped for a PAT — which would
   remove guard 1 — the workflow still would not spin.

**Tag / version disagreement.** The version is computed exactly once, into the csproj.
Everything downstream reads it back from there: `package-release.sh` names the zip with
the same `sed` expression `bump-version.sh` writes, the tag is `v$VERSION`, the release
title is `$VERSION`, and the DLL is compiled from the very commit being tagged. The
workflow then asserts the zip exists under the expected name before publishing
anything. There is no second place a version could come from, so there is nothing to
disagree with.

**Race with a second push.** The concurrency group serialises runs. In addition, just
before pushing, the workflow re-reads `origin/main` and refuses if it moved away from
the commit it built. That is deliberately a hard failure and not an auto-rebase: a
release must be the thing that was tested, not a merge nobody looked at. Nothing is
published in that case, and the queued run for the newer commit releases that state
instead.

---

## 5. What CI cannot check, and why

### The wire vectors do not run. This one matters.

`scripts/wire-tests.sh` drives 146,839 byte-exact assertions over the multiplayer wire
format. It **cannot run on a hosted runner**, and no amount of work in this lane
changes that:

* `tests/GloomhavenVR.WireTests` references `UnityEngine.CoreModule.dll` with
  `Private="true"` — it **loads and executes** it. That is on purpose: the vectors
  depend on `Mathf.RoundToInt`'s banker's rounding (`RoundToInt(0.5f) == 0`,
  `RoundToInt(1.5f) == 2`), which sits directly on the quantization path, and the
  project's own comment records that a hand-written shim was rejected for exactly that
  reason.
* A reference assembly cannot be executed. The CLR refuses it:
  `BadImageFormatException: Cannot load a reference assembly for execution`. Verified
  against `libs/RefAsm/UnityEngine.CoreModule.dll`.
* The NuGet `UnityEngine.Modules 2021.3.5` package does not help — its
  `UnityEngine.CoreModule.dll` is itself a stub, and calling `Mathf.RoundToInt` through
  it throws `NullReferenceException`. Verified, not assumed.
* Shipping the real `UnityEngine.CoreModule.dll` is out of the question: it is the
  publisher's binary and the repository is going public.

**What CI does instead:** it *compiles* the wire test project. That still catches the
failure its `<Compile Include>` list was written to catch — a wire file moved, renamed
or split. It does not catch a changed byte, and it does not pretend to. Both workflows
emit an explicit notice/warning annotation saying so, and `release.yml`'s is a
`::warning::` on purpose.

**So: run `scripts/wire-tests.sh` locally before pushing to `main`.** It is the one
gate a release genuinely cannot self-serve.

### Everything else that CI cannot see

* **Anything requiring a headset.** The mod's real failure modes — stereo rivalry,
  aliasing, a window drawn behind its host, a figure culled one eye first — are seen by
  eye, one photograph per round. No pipeline replaces that.
* **The asset bundle's contents.** `check-bundle-format.sh` reads the 30-byte UnityFS
  header and nothing else. It proves the runtime can open the archive; it proves
  nothing about the meshes and shaders inside. Rebuilding the bundle needs
  `/home/claw/unity-2021.3.5` — the game-exact editor — and is a local step.
* **Whether the reference assemblies are current.** CI proves they are stubs, never
  that they match the installed game. Only §2.4 does that.
* **The RuntimeDeps provenance.** The runner rebuilds the *provisional* set from
  package source. If the editor-harvested set (`unity/HARVESTING.md`) is ever adopted,
  a release built here would silently ship the provisional set instead — the harvested
  DLLs are gitignored and the runner has no way to obtain them. `versions.json` inside
  the zip records which set was used, so the fact is at least visible. Revisit this
  before the first harvested set is put into service.

---

## 6. Versions

`<Version>` in `src/GloomhavenVR/GloomhavenVR.csproj` is the single source of truth,
`MAJOR.MINOR.PATCH`, today `0.1.0`.

* **A release** bumps the patch component automatically. `0.1.0 → 0.1.1 → 0.1.2`.
  Bump the minor or major by hand in the csproj on `dev` before releasing (or run
  `scripts/bump-version.sh --minor` / `--major`); the release will then bump the patch
  of *that*.
* **A dev build** keeps the number and appends the short commit hash. The `BuildInfo`
  contract carries `Version`, `Commit`, `IsDevBuild` and a `Display` string, and the
  mod shows it in-game — so a screenshot from a test session identifies the exact
  build it came from.
* **`NetProtocol.ModBuild` is a different number and stays manual.** It is the
  multiplayer wire-compat counter; two peers with different ModBuilds cannot play
  together. Do not couple it to the release version.

---

## 7. When it breaks

| Symptom | Cause | Fix |
|---|---|---|
| Push to `main` did nothing at all | the pushed commit predates `.github/workflows/` | push `dev` into `main`, not an old commit |
| `remote: Permission to … denied to github-actions[bot]` (403) | repository workflow permissions are read-only, or a branch protection rule blocks the bot | §2.1 |
| Build fails with hundreds of `CS0117` / `CS1061` | `libs/RefAsm` is stale after a game update | §2.4 |
| `error: N warning(s), expected exactly 6` | a new warning, or one of the six was fixed | fix it, or lower `EXPECT_WARNINGS` in `scripts/ci-build.sh` |
| `check-refasm.py` says a file has IL bodies | a real game DLL was committed into `libs/RefAsm` | `git rm` it and re-run `scripts/make-refasm.sh` — never commit game DLLs |
| `main moved from … while this release was building` | someone pushed to `main` mid-release | nothing was published; the queued run releases the newer state |
| Tag exists but no GitHub Release | `gh release create` failed after the atomic push | re-run `gh release create v<x> dist/…zip …` locally, or delete the tag and release again |
| `Tag vX.Y.Z already exists` | that version was released before | set `<Version>` past it on `dev` and push again |
| Release asset 404s for the auto-updater | the repository is still private | §2.5 |

Nothing in this pipeline is destructive to the repository. The worst failure leaves an
unpublished build and a red run.
