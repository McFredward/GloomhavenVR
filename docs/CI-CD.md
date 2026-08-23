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
        ▲                                           │
        │        the bump commit and the tag        │
        └─────────── go back to dev / to refs/tags ─┘

   ci.yml                                   release.yml
   push to dev + PRs to dev and main        push to main
   build, gates                             build, package, tag,
   no release, no tag, no bump              GitHub Release, then bump dev
```

* **`dev`** is the working branch. Everything lands there. Its builds are never
  released — that is the decision, and `ci.yml` has no code that could release.
* **`main`** is the release branch. **A push to `main` publishes a release.** There is
  no button, no approval step and no dry run. Pushing `dev` into `main` *is* the
  release action.
* There is no `master` branch and there is not meant to be one.

### The invariant: `main` is only ever a fast-forward of `dev`

**Nothing writes a commit to `main` except `git push origin dev:main`.** Not the
release workflow, not a merge, not a hotfix. That is what keeps the release procedure
one command forever instead of turning into a merge-back ritual, and it is load-bearing
enough that `release.yml` refuses to release a commit that is not already contained in
`dev` (it says so, before it builds anything).

This was not always true, and it is worth knowing why the file says so much about it.
An earlier `release.yml` committed the version bump **onto `main`** and pushed
`HEAD:main`. `main` then carried a commit `dev` had never seen, so the *second*
`git push origin dev:main` was a non-fast-forward and the remote rejected it. The
workflow's own "refuse if `origin/main` moved" guard could not catch it — the thing
that had moved `main` was the previous run of that same workflow. The pipeline worked
exactly once and then wedged.

The fix was structural rather than defensive: the workflow now writes **no commit to
`main` at all**. It writes exactly two refs, `refs/tags/v<version>` and
`refs/heads/dev`. `scripts/release-sim.sh` reproduces both the old failure and the new
behaviour offline, in a scratch repository — `--old` wedges at release 2, the default
runs three consecutive releases with every `dev:main` push a fast-forward.

A pleasant side effect, and the reason §2.1a below can be short: **a branch ruleset on
`main` cannot break this pipeline, because a ruleset blocks commits and this pipeline
pushes none to `main`.**

---

## 2. What YOU do by hand — the complete list

### 2.1 Allow the release workflow to write (REQUIRED, one time)

`release.yml` pushes a tag, creates a release, and pushes the bookkeeping version bump
to `dev`. It declares `permissions: contents: write`, and **`GITHUB_TOKEN` with that
scope is sufficient — no Personal Access Token is needed.** The workflow-level
`permissions:` block raises the token's scope above the repository default, so in
principle this works out of the box. Set the repository default to match anyway, so a
403 can never be the surprise that eats a release:

> **Settings → Actions → General → Workflow permissions**
> select **“Read and write permissions”** → **Save**

Leave “Allow GitHub Actions to create and approve pull requests” **off**; nothing here
opens a pull request.

---

### 2.1a “Only I may push directly; everyone else opens a pull request”

**This section replaces an earlier instruction that said the opposite** (*“do not put a
branch protection rule on `main`”*, and *“add `github-actions[bot]` to the rule's
bypass list”*). Both were written for the old release design and the second of them was
never possible in the first place — see the note at the end. The old design needed
`main` writable by the bot; the current one does not, which is what makes this section
safe to write at all.

#### What is already true today, before you change anything

The repository is **public**. On GitHub, public means **read**, and only read:

> *“A repository owned by a personal account has two permission levels: the repository
> owner and collaborators.”* — GitHub Docs, *Permission levels for a personal account
> repository*

Nobody outside those two levels can push to `main`, to `dev`, or to any other branch.
Making a repository public grants visibility and the right to fork; it grants no write
access to anyone. A stranger's only route in is **fork → pull request**, and merging
that pull request is an action *you* take.

**So the goal — “only I can push directly, everyone else must open a PR” — is already
met today, provided you have no collaborators.** Check that in one place:

> **Settings → Collaborators** — the list should be empty (only you, the owner).

Do not oversell what the ruleset below adds. It buys exactly three things:

1. **Future-proofing.** The day you add a collaborator with write access, they can push
   straight to `main` — and a push to `main` *publishes a release*. The ruleset is what
   stops that from being possible before it can happen by surprise.
2. **Accident-proofing for you.** A ruleset that blocks force-pushes and deletions on
   `main` protects the release history from a mistyped command of your own, and that is
   the failure mode you are actually most exposed to as a solo owner.
3. **A visible contract.** Outside contributors can see that `main` is protected and
   that PRs are the way in.

It does **not** make the repository more private, and it does **not** close a hole that
is currently open to the public.

#### The clicks

> **Settings → Rules → Rulesets → New ruleset → New branch ruleset**

| Field | Set it to | Why |
|---|---|---|
| **Ruleset Name** | `protect-main` | anything; it is a label |
| **Enforcement status** | **Active** | `Disabled` creates it without effect |
| **Bypass list** → *+ Add bypass* | **Repository admin** | **this is the entry that keeps `git push origin dev:main` working for you.** You are the owner, so you hold the admin role |
| **Target branches** → *Add target* → *Include by pattern* | `main` | not `dev` — see below |
| ☑ **Restrict deletions** | on | `main` cannot be deleted |
| ☑ **Block force pushes** | on | release history cannot be rewritten |
| ☑ **Require a pull request before merging** | on | this is the rule you actually asked for |
| ↳ **Required approvals** | **0** | see the warning below |
| ☑ **Require status checks to pass** → *+ Add checks* → `Build and gates` | on, optional | the `ci.yml` job. It only becomes selectable after that job has reported on the repository at least once, which is why PRs to `main` were added to `ci.yml`'s triggers |

Everything else can stay off. In particular **do not create a *tag* ruleset** (the
`New tag ruleset` button next to the branch one). A tag ruleset matching `v*` is the one
piece of configuration that *would* break the release pipeline, because pushing
`refs/tags/v<version>` is the only write it makes to the release side.

**Required approvals must be 0 if you work alone.** GitHub does not let you approve your
own pull request. Set it to 1 and any PR you open yourself becomes unmergeable through
the UI — you would be relying on your bypass entry for every single change, which makes
the rule theatre. With 0, a PR is still *required*; it just does not require a second
person who does not exist. Raise it to 1 the day a second person does.

#### Should `dev` get a ruleset too? — **No. Recommended against.**

It is tempting for symmetry, and it is the one change that can break the pipeline.

* **What it would break.** After publishing, `release.yml` pushes one commit to `dev`
  (`chore(release): set <Version> to …`) so the next release starts from a fresh number
  without you editing anything. A pull-request rule on `dev` blocks that push.
* **And it cannot be bypassed the obvious way.** `GITHUB_TOKEN` acts as
  `github-actions[bot]`, which is a *system identity*, not a GitHub App — and the
  bypass picker offers repository/organization admins, the maintain and write roles,
  teams, GitHub Apps and Dependabot. `github-actions[bot]` is not among them. GitHub's
  own position on why is on the record: *“If we enabled GitHub Actions to push to a
  protected branch then any collaborator in your repo could push any code to any branch
  they wanted simply by creating a branch and coding the workflow to push to some other
  branch.”* Getting around it means a dedicated GitHub App token, a deploy key, or a PAT
  for a bot account — three moving parts and a secret to rotate, bought for a branch
  that the public cannot push to anyway.
* **What it would buy.** Nothing today. Non-collaborators cannot push to `dev` either.

So: **protect `main`, leave `dev` open.** If you later add a collaborator and want `dev`
gated as well, the honest options are (a) accept that the bump lands by hand — the
workflow tells you the exact two commands and the release itself is unaffected — or
(b) set up a deploy key or GitHub App for the bump. Do not reach for (b) before there is
a second person.

#### If the bypass entry does not work, nothing is lost

This is the part worth knowing before you click anything: **after the release rework,
the pipeline does not need to push to `main` at all.** So even a ruleset with no bypass
entry whatsoever cannot break a release. The only thing a wrong bypass list can block is
*your own* `git push origin dev:main`, and a blocked push changes nothing on the server
— you fix the bypass list and push again.

That also makes the verification trivial, and it should be your first act after
enabling the ruleset:

```bash
git push origin dev:main
```

If it is accepted, the bypass entry works. If it comes back with *“Changes must be made
through a pull request”*, the bypass entry is not taking effect — fix it before doing
anything else. There is no state to clean up either way.

#### Where this document is inferring rather than certain

* **Certain** (GitHub Docs): public grants read only; a personal repository has exactly
  two permission levels, owner and collaborator; the ruleset menu path is
  *Settings → Rules → Rulesets → New ruleset → New branch ruleset*; the bypass list
  admits repository admins, organization owners, enterprise owners, the maintain/write
  roles, teams, GitHub Apps and Dependabot; the rule labels quoted in the table are the
  documented ones; enforcement is Active or Disabled.
* **High confidence, not from a single documentation line:** that
  `github-actions[bot]` cannot be added to a ruleset bypass list. This is GitHub staff's
  stated position plus consistent community reports, not a sentence in the reference
  docs. **Verify it by looking:** open the bypass picker and search for “Actions”. If it
  *is* offered, the recommendation against protecting `dev` softens — but the
  recommendation for `main` does not change, because `main` needs no bypass for the bot
  either way.
* **Not verified against your account:** whether *your* bypass picker shows exactly the
  entries listed above. GitHub's ruleset UI differs between personal and
  organization-owned repositories, and this repository is personal. If “Repository
  admin” is not offered, look for a “Roles” or “Organization admin” grouping; if none of
  them appear at all, use the classic screen instead
  (**Settings → Branches → Add branch protection rule**, *Require a pull request before
  merging*, and leave *Do not allow bypassing the above settings* **unchecked** so that
  you, as admin, retain the direct push). The classic screen's exact admin-bypass
  semantics are the part this document is least sure of — which is another reason to
  prefer the ruleset, where the bypass list is explicit and visible.

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

That single command fires `release.yml`, which builds, packages, publishes and then
advances the version number on `dev` for next time. **It is a real release the moment
you press enter.** There is no undo beyond deleting the release and the tag afterwards.

Before you type it, four things are worth having done:

```bash
scripts/wire-tests.sh        # the golden vectors (146,839 assertions) — CI CANNOT run these (see §5)
scripts/build.sh Release     # sanity: 0 errors, 6 warnings
scripts/bump-version.sh      # THE number about to be released — it is not bumped for you first
git log --oneline main..dev  # what is about to go out
```

**The version that goes out is the one already in the csproj**, not that number plus
one. `scripts/bump-version.sh` with no argument prints it and changes nothing. The
workflow reads exactly that, tags it, and only *afterwards* bumps `dev` to the number
the *next* release will use. So the csproj on `dev` always names the next release, never
the last one — and if you want the next release to be a minor or a major instead of a
patch, run `scripts/bump-version.sh --minor` (or `--major`) on `dev` and push that
before you release.

Afterwards, `dev` is exactly `main` plus one bookkeeping commit. That is expected and is
what keeps the next `git push origin dev:main` a fast-forward. Nothing needs merging
back.

**The very first release** is a special case worth knowing about, in two ways.

It will be **`v0.1.0`** — the number sitting in the csproj right now — because a release
publishes the committed version rather than bumping past it. `dev` becomes `0.1.1`
immediately afterwards.

And it has no previous tag: `main` carries ~2,140 commits of history and nothing to
compare against. `scripts/release-notes.sh` handles that deliberately — it does **not**
print 2,140 commit subjects (that would exceed GitHub's 125,000-character release-body
limit and the API call would fail outright). It prints one honest sentence — *"First
release … 2,141 commits of development from 2026-07-14 to <date> published for the
first time"* — plus a link to the full commit history. Every later release lists the
real commit subjects since the previous tag, capped at 100 with an explicit "… and N
more", with the `chore(release):` bookkeeping commits filtered out.

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

### 2.5 The repository is public — what that changed

**It is public now**, so this is a record rather than a plan.

Nothing broke. One capability appeared: while the repository was **private**, release
assets required an authenticated download and the planned in-game auto-updater could
not fetch them. Now
`https://github.com/<owner>/<repo>/releases/latest/download/GloomhavenVR-<v>.zip`
is an anonymous download and the updater works. (`release.yml` used to print a notice
about this on every run; it has been removed, because it described a private
repository.)

Nothing else changed. In particular, public did **not** grant anyone write access —
§2.1a spells out what it did and did not do, and what a ruleset adds on top.

---

## 3. `ci.yml` — the working branch

Triggers on **push to `dev`**, and on **pull requests to `dev` and to `main`**.
Concurrency group per ref with `cancel-in-progress: true`: a newer push supersedes an
older run, because the old run is answering a question nobody is asking any more.

`main` is in the pull-request list on purpose. Once §2.1a's ruleset is in place, a pull
request is the only way anyone but you gets code into `main`, and a PR against a branch
this workflow does not list would get **no gates at all, silently**. It is also the job
you would select under *Require status checks to pass* — a check only becomes
selectable in that picker after it has reported on the repository at least once.

A pull-request run from a **fork** gets a read-only token and no secrets. Nothing here
needs either: every gate is a local read of the tree, and the artifact upload is guarded
to push events.

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
changed — and it costs 6 MB per push instead of the 74 MB a full zip would cost.

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

**The refs it writes are `refs/tags/v<version>` and `refs/heads/dev`. That is the whole
list.** It writes no commit to `main` — see §1 for why that matters and what it cost to
learn.

Order of operations, and why it is that order:

1. **Read the version and refuse early.** `scripts/bump-version.sh` with no argument
   *prints* `<Version>` from `src/GloomhavenVR/GloomhavenVR.csproj` and changes nothing.
   That number is the release. Before anything is built, the workflow refuses if:
   * `v<version>` already exists (that version has been released), or
   * the commit being released is **not contained in `origin/dev`** — releasing it would
     leave `main` ahead of `dev` and break the fast-forward invariant, or
   * the working tree is not clean.

   All three are cheap, so the cost of being told is thirty seconds and not a wasted
   fifteen-minute run.
2. **Build with `GhvrReleaseBuild=true`**, set as a **job-level environment variable**.
   It has to be the environment and not `-p:` on one command, because
   `package-release.sh` runs its own `dotnet build` internally and a `-p:` would not
   reach it. MSBuild reads environment variables as properties, so it does.
   This is what makes `BuildInfo.IsDevBuild` false; every other build in the project is
   a dev build and shows the short commit hash in-game.

   The build is stamped from a **clean tree at the exact commit being tagged**, which is
   simply the checkout as `actions/checkout` produced it. The old design had to
   bump-commit *before* building to achieve that; this design gets it for free and
   asserts it (step 1, and again after packaging) rather than assuming it. A release
   stamped `…-dirty` is a bug report waiting to happen.
3. **The same gates as CI**, plus `fetch-natives.sh`.
4. **`package-release.sh`**, then an explicit assertion that
   `dist/GloomhavenVR-<version>.zip` exists under that exact name, that the asset bundle
   is inside it, and that the build did not dirty a tracked file behind our back.
5. **Push the tag, then create the release.** In that order, so a rejected tag push
   leaves no release and no orphan; `gh release create --verify-tag` refuses to invent a
   tag that is not there.
6. **Bump `<Version>` on `dev`** — *after* the release is live, because it is
   bookkeeping and not part of the release. This is where the bump commit lives now.

### The hazards

**Infinite loop — structurally impossible now.** The workflow triggers on `main` and
writes only to tags and to `dev`; `dev` runs `ci.yml`, which publishes nothing and
pushes nothing. There is no cycle to break.

The old `[skip ci]` marker on the bump commit is **deliberately gone**, and that is a
reversal worth recording. It was the belt to `GITHUB_TOKEN`'s braces when the bump went
onto `main`. Now the bump goes onto `dev`, where that commit will one day be the HEAD
commit of a `git push origin dev:main` — releasing twice with no work in between does
exactly that. GitHub honours `[skip ci]` on the head commit of a push, so the marker
would have **silently suppressed a real release**: no run, no error, no annotation. A
safety net that turns into a trap gets removed.

**Tag / version disagreement.** The version is read exactly once, from the csproj, and
never written during a release. Everything downstream reads the same line:
`package-release.sh` names the zip with the same `sed` expression, the tag is
`v$VERSION`, the release title is `$VERSION`, and the DLL is compiled from the very
commit the tag points at — which is `github.sha`, which is what `main` points at. The
workflow then asserts the zip exists under the expected name before publishing anything.
There is no second place a version could come from.

**Two releases fired close together.** The concurrency group serialises the runs; the
second does not cancel the first, because a half-published release is worse than a slow
one. The second run then reads the *same* `<Version>` — the first run's bump commit is
on `dev`, not on the commit the second run is building — sees that the tag already
exists, and **refuses before building anything**. The remedy is the ordinary release
command: `git push origin dev:main` again, which now carries the bump. Nothing is
half-published in between. `scripts/release-sim.sh --race` walks through exactly this.

**`main` rewritten mid-release.** `main` moving *forward* while a release builds is
harmless — the tag names the commit that was actually built and tested. `main` moving
*away* from it (a force-push backwards) is not, so the workflow re-checks containment
just before pushing the tag and refuses if the commit is no longer on `main`.

### Proving it without GitHub

`scripts/release-sim.sh` reproduces the branch topology offline in a scratch repository,
using the real `scripts/bump-version.sh`:

```bash
scripts/release-sim.sh --old      # the previous design: wedges at release 2
scripts/release-sim.sh            # the current design: 3 releases, all fast-forward
scripts/release-sim.sh --race     # two pushes in quick succession
```

The build and the GitHub API are not simulated — neither of them touches a ref, and the
defect this exists to catch was purely topological.

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
  publisher's binary and the repository is public.

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

* **The number on `dev` is the version the NEXT release will carry**, not the one that
  was last released. A release publishes what the csproj already says and then advances
  it, so the first release is `v0.1.0` and `dev` becomes `0.1.1` immediately after.
  (This is a change: the previous design bumped first and released `current + 1`.)
* **A release** advances the patch component automatically, on `dev`, after publishing:
  `0.1.0 → 0.1.1 → 0.1.2`. For a minor or a major, run
  `scripts/bump-version.sh --minor` / `--major` on `dev` and push it *before* releasing
  — that number is then the one that goes out. The workflow leaves the csproj alone if
  it finds a number other than the one it just released, so a hand-set version is never
  overwritten.
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
| `git push origin dev:main` rejected, `non-fast-forward` | `main` carries a commit `dev` does not have — somebody (or something) wrote to `main` directly | get that commit onto `dev` (`git cherry-pick` / `git merge`), then push again. Nothing but `dev:main` may ever write to `main` — §1 |
| `git push origin dev:main` rejected, *“Changes must be made through a pull request”* | the §2.1a ruleset is active and your bypass entry is not taking effect | add **Repository admin** to the ruleset's bypass list. Nothing was published and nothing needs cleaning up |
| Push to `main` did nothing at all | the pushed commit predates `.github/workflows/`, **or** its subject contains `[skip ci]` | push `dev` (which has the workflows) into `main`, not an old commit. No commit this pipeline creates carries `[skip ci]` any more — §4 |
| `remote: Permission to … denied to github-actions[bot]` (403) | repository workflow permissions are read-only, **or** a ruleset on `dev` is blocking the post-release bump. It is *not* `main`: the workflow pushes no commit there | §2.1 for the permissions toggle; §2.1a for why `dev` should not be protected. The release itself is unaffected either way — only the version bump is, and the workflow prints the two commands that fix it by hand |
| `Release published, version bump FAILED` | as above — the bump could not reach `dev` | the release is complete and correct. Run `scripts/bump-version.sh --patch` on `dev`, commit, push. Skip it and the *next* release fails with `Tag vX.Y.Z already exists` |
| `The commit being released is not contained in origin/dev` | something was pushed to `main` that is not on `dev` | nothing was built. Get it onto `dev` first — this refusal is what keeps `dev:main` a fast-forward forever |
| `Tag vX.Y.Z already exists` | that version has been released — usually two releases fired close together, so the second one is building a commit that predates the first one's bump | `git push origin dev:main` again; `dev` now carries the bump. If it really is a re-push of an old commit, set `<Version>` past it on `dev` |
| `main no longer contains <sha>` | `main` was force-pushed backwards mid-release | nothing was published. Sort `main` out, then release again |
| Build fails with hundreds of `CS0117` / `CS1061` | `libs/RefAsm` is stale after a game update | §2.4 |
| `error: N warning(s), expected exactly 6` | a new warning, or one of the six was fixed | fix it, or lower `EXPECT_WARNINGS` in `scripts/ci-build.sh` |
| `check-refasm.py` says a file has IL bodies | a real game DLL was committed into `libs/RefAsm` | `git rm` it and re-run `scripts/make-refasm.sh` — never commit game DLLs |
| `Dirty tree at checkout` | a tracked file differs from the commit being released | should be impossible on a runner; read the file list the step prints |
| Tag exists but no GitHub Release | `gh release create` failed after the tag push | re-run `gh release create v<x> dist/…zip …` locally, or delete the tag and release again |
| Release asset 404s for the auto-updater | was the private-repository symptom; the repository is public now, so look for a wrong URL or a missing asset instead | §2.5 |

Nothing in this pipeline is destructive to the repository. It creates tags and one
bookkeeping commit on `dev`; it deletes nothing, rewrites nothing, and writes no commit
to `main`. The worst failure leaves an unpublished build and a red run.
