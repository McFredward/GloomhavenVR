# CI/CD — build, verification and release

Current workflow reference, reviewed against the tracked scripts on 2026-09-17.
Start with [DEVELOPING.md](DEVELOPING.md) for local setup. The workflow files and scripts
are authoritative; historical build results live in [STATE.md](../.planning/STATE.md).

## 0. Building without the game

A hosted Ubuntu runner produces the complete release archive using these inputs:

| Component | Source |
|---|---|
| Plugin and preloader | Source in `src/`; the plugin compiles against committed metadata-only `libs/RefAsm` when no game install is available |
| Unity XR managed dependencies | `scripts/build-runtimedeps.sh`, from pinned package-source tags |
| OpenXR native libraries | `scripts/fetch-natives.sh`, with pinned SHA256 hashes |
| Asset bundle | Committed `prebuilt/gloomhavenvr.bundle` by default; local Unity output requires explicit opt-in |
| Installation instructions | `packaging/INSTALL.txt.in` and `packaging/INSTALL.de.txt.in` |
| License texts and notices | Root GPL `LICENSE`, `packaging/THIRD-PARTY.txt` and pinned XR dependency notices under `Licenses/` in the archive |

The archive layout is documented in [DEVELOPING.md](DEVELOPING.md#packaging-a-release).
A full release includes the asset bundle. A CI artifact contains only the plugin and
preloader DLLs: it can update an existing compatible install, but cannot establish one.
Check the bundle history before calling an update DLL-only. Build 483 changed the bundle;
older installs need the full package even when a later build changes only code.

The auto-updater validates the archive layout in `Core/SelfUpdate/SelfUpdateZip.cs`.
A new top-level archive file requires an explicit change to that accept-list as well as
both packaging paths. Validate the update path when changing the package.

## 1. Branches and SDK

| Event | Workflow | Result |
|---|---|---|
| Push to `dev` | `.github/workflows/ci.yml` | Build and gates; no artifact, release, tag or version bump |
| Pull request to `dev` or `main` | `ci.yml` | Reuse full dev CI for an identical merged tree, otherwise full checks; always compare surfaces against the PR base |
| Manual CI on `dev`, `upload_dev_build=true` | `ci.yml` | Full build and gates, then an optional temporary DLL download |
| Push to `main` | `.github/workflows/release.yml` | Require exact-tree full CI evidence, then build, package, tag and publish; advance the next version on `dev` |

**Every release comes from `main`.** Integrate work into `dev`, then merge a reviewed
`dev` → `main` pull request when a release is authorized. Use a merge commit, not a
squash or rebase merge. The resulting push to `main` starts publication automatically;
it is not a dry run or a branch-protection test. A direct fast-forward is also supported
where repository rules permit it, but is not required by the pipeline.

`release.yml` requires the candidate to belong to current `main` and either already
belong to `dev` history or be a two-parent merge whose tree exactly matches its second
parent, with that parent contained in current `dev`. This permits the ordinary PR merge
commit while rejecting extra merge-resolution edits and unrelated same-tree commits.
A concurrently advancing `dev` is allowed: the reviewed source may be its ancestor.

After publication, the pipeline merges the release commit into the current `dev`, verifies
that this preserves the entire current development tree, and advances its version only
if it still names the released version. An independently advanced version stays intact,
including when only the release ancestry needs recording. A rejected push is retried
against freshly fetched refs; no force-push is used. Nothing commits to `main`.

Full CI and release builds explicitly install `ripgrep`; hosted runner images are not
assumed to provide the `rg` command.

`global.json` prefers stable .NET SDK 8.0.4xx and permits a later SDK family when absent.
Both workflows install .NET 8 and verify that 8.0.4xx was selected. Local .NET 10-only
installations are supported by the major roll-forward policy introduced in build 496.
The game plugin still targets net472; the SDK version is not the game's runtime version.

## 2. Maintainer steps

### 2.1 Repository permissions and branch rules

The release job declares `contents: write` for its `GITHUB_TOKEN`. Repository or
organization policy must allow it to push `v*` tags, create releases and push the
bookkeeping commits to `dev`. The maintainer must be able to merge the release PR into
`main`; the workflow does not need a branch-protection bypass. Review the actual repository rules before release; this file does not attest that any
particular ruleset, visibility setting or account permission is enabled.

A rule that blocks the post-release push to `dev` leaves the release published but the
next version unadvanced. The workflow prints recovery commands. A rule that blocks the
tag prevents publication. The workflow needs no permission to push commits to `main`.

The repository's default branch does not change these triggers. Contributions belong
on `dev`; a push or merge to `main` is a release action.

### 2.2 Review the exact candidate

Before editing, create a local compiled-form baseline if one is not already available.
Workers must first detach the shared baseline symlinks as described in
[CLAUDE.md](../CLAUDE.md#the-gates--run-all-of-them-before-you-push); never overwrite the
integration baseline through a worktree link:

```bash
bash scripts/refactor-guard.sh baseline
```

After integration, run the complete local gates with the real game references available:

```bash
bash scripts/refactor-guard.sh check --summary
bash scripts/ci-build.sh Release
python3 scripts/check-docs-i18n.py
bash scripts/bump-version.sh
git log --oneline origin/main..dev
```

The guard includes the full wire suite. It exits 1 for a compiled-form difference,
which is normal after code changes: inspect the verdict and account for every change.
A build must have zero errors and warnings. Record assertion counts and hardware evidence
in the round notes. CI success does not establish headset appearance or four-player scaling.

When a tester configuration is available, also run
`python3 scripts/rebase-defaults.py check`. It requires the gitignored
`.planning/debug/default/` drop; a missing drop is not a successful comparison. Preserve
explicitly pinned user defaults and investigate unresolved entries rather than overwriting
saved tuning wholesale.

### 2.3 Release when authorized

Set the intended version on `dev` before release. `scripts/bump-version.sh` without
arguments only prints it; `--major`, `--minor` and `--patch` change it. Prepare the
bilingual player summary in `packaging/release-highlights/<version>.md`, commit and
push the candidate to `dev`, and review its CI result and local gates.

Open a pull request from `dev` to `main`, review its checks and merge it using
**Create a merge commit**. Do not squash or rebase the release PR. Its resulting tree
must match the reviewed `dev` source; resolve any content conflicts on `dev` first.

**This publishes the version already committed in the csproj.** It does not increment
that version first. Follow the Release run through publication and the `dev` bump;
verify the tag names the tested `main` commit and the expected archive is attached.
Do not create a release from a worker branch or directly from a `dev` artifact.

### 2.4 After a game update

Regenerate the metadata-only reference assemblies using `scripts/make-refasm.sh` against
the new game install, then run `scripts/check-refasm.py`, build and test locally.
See [libs/RefAsm/README.md](../libs/RefAsm/README.md) for the procedure. Commit only the
metadata stubs, never the game's executable assemblies or decompiled sources.

### 2.5 Public downloads and the updater

The in-game updater uses the public release endpoint without a repository credential.
A private repository cannot serve that path to an ordinary player. Making the project
public and testing an actual release download are separate maintainer actions; source
checks and a successful build do not prove that the public endpoint is reachable.

## 3. Verification coverage

Full CI runs the source checks, strict build, reference-assembly validation, bundle-format
check, bilingual-docs check and standalone native presentation harnesses. Every dev push
and manual CI run executes them. Internal PRs may reuse that evidence only for an identical
merged Git tree; fork PRs always execute full checks with read-only permissions and no secrets.
The PR surface diff still runs when evidence is reused. Locally, `refactor-guard.sh` compares
against the stored baseline even without a PR.

Release verifies existing full CI evidence before rebuilding the exact main commit with
release flags. It does not duplicate source checks or regression harnesses. The fresh build,
reference assembly check, bundle validation, package layout/text checks and version checks remain.

The shared source checks cover mirrored constants, frame order, patch inventory, wire
coverage, identity secrecy, remote dial ownership, enum array sizes, partial initializer
order, instrument writes, remote defaults, tuning IDs, network-action receivers,
hardware-verification logging and option reachability. The workflow files list the
commands explicitly; release admission verifies their successful full-CI completion.

The card-capture, native-playback, board-refresh, card-loss-modal, map-flow, map-button, flight-timing,
figure-hold, native-video, reward-showcase, modal-close, reward-pose, conversion-rollback, panel-material, panel-ink, shared-video-playback and introduction-hint harnesses execute
production source with controlled Unity API substitutes and deliberate failing variants. These run on
hosted CI as well as through the local wire-test driver. The local driver also checks
presentation-send reuse. Their coverage does not replace the full wire vectors or a
headset test.

`scripts/card-loss-modal-tests.sh` checks native UI-lock ownership and extracts the actual
`FlatScreen.WantVisible` method to verify screen, dialog and rescue priorities. Its negative
controls reject both missing suppression and accidental suppression of real modal screens.
The production visibility/takeover policy also verifies failed map modal recovery and its
release after native closure, while preserving movie/loading priority.

`scripts/map-flow-tests.sh` checks native loadout ownership, the actual curtain/travel policies
and input admission in the map interactor. It covers reusing the party root after the story,
the separate outer PartyPanel and inner controller-owned window, native multiplayer barriers
and map locks. Native travel methods execute alongside the actual VR prefix to verify that
a failed button adoption retains original guarded input and teardown retries the next map.
The suite includes negative controls for the former descendant-only ownership check, missing integration,
overbroad curtain release and inappropriate travel visibility.

`scripts/map-button-tests.sh` executes the production off-bar dispatch. It checks complete
native toggle notification, sibling deselection and repeated presses; negative controls
reject the former silent Select/Deselect paths that omitted tutorial listeners.

`scripts/burn-layout-tests.sh` and `scripts/remote-burn-sequencing-tests.sh` exercise
production card-layout barriers, sequential burns, native release ordering and observer
handoffs. `scripts/burn-completion-tests.sh` checks retained original final frames through
widget retirement, recovery and address changes. `scripts/item-burn-tests.sh` observes
native item iterators, including paused game time, cancellation and overlapping effects.
`scripts/item-appearance-tests.sh` executes original item capture, codec, native writes and
clip release against controlled Unity APIs, and verifies the actual transport registration.
Each runs in full CI and the local wire driver, with runtime negative controls. Release
requires that exact-tree CI evidence instead of executing them again.
These controlled tests cannot establish headset rendering quality.

`scripts/flight-timing-tests.sh` checks transfer of card presentation between a board seat
and a flight, including stale state and arrival ownership. `scripts/figure-hold-tests.sh`
checks that native action setup receives the board pose and delayed held samples cannot
reclaim an action-released figure. Both include production integration checks.

`scripts/native-video-tests.sh` checks decoded-frame and initial-pose readiness, native intro
skip routing, video-window cleanup and the actual modal orphan sweep against the live
movie owner. Negative controls cover lost ownership, missing persistent-lifetime enrollment
and missing content binding. The actual pointer guard and attached click handler cover
laser/poke skip, stale playback identities and native hero-movie completion.
`scripts/panel-material-tests.sh` exercises passive native TMP material inspection and
the actual capture blur handler. Negative controls restore allocating material getters,
overbroad neutralization and a missing GrabPass remedy.

`scripts/reward-showcase-tests.sh` executes the native reward iterator through group progression
and the completion callback, including tutorial/custom-scenario input and multiplayer authority.
The retained iterator fixture is compared with the read-only game reference when available.
Negative controls reject the former physical-gamepad adapter, scenario-only manager lookup
and bypasses of native progression.

`scripts/modal-close-tests.sh` executes the complete production close method and mandatory
window classifier. It covers pooled windows that change policy after conversion, native
rescue ownership, ordinary menus and city destinations. Negative controls restore unchecked
close, missing rescue and incorrect rescue lifetime behavior.

`scripts/reward-pose-tests.sh` executes production reward pose sampling, receiving and election
with the key/opening-scoped handshake. It covers explicit absence, concurrent keys, late
opening, stale replies, reordered empty snapshots and a departing position sender. Golden
wire vectors include record 74, full snapshots and bounded four-sender fragment reassembly.

`scripts/conversion-rollback-tests.sh` executes the production conversion transaction,
outer modal failure handler, native restoration and host-destruction guard. Injected failures
cover partial adoption/enrollment, native camera restore, throwing/silent reparent refusal,
scene-root safety detach and repeated mod-only cleanup. Negative controls reject lost ownership,
unsafe destruction, premature snapshot disposal and replaying stale native layout on retries.
An additional binding negative verifies allocation ownership precedes native reparenting.

`scripts/panel-ink-tests.sh` exercises the production ink walker:
full-frame movie content remains measurable while ordinary backgrounds, hidden graphics and
clipped pixels retain their exclusions. It also tests placement-only annotation exclusion
in the ink walk and drawn-content union, while hit/chrome bounds retain the hint controls.
Original reward-heading glyph overflow is included without broadening native masks or affecting
other text; transformed, empty and invalid glyph bounds have dedicated negative controls.
`scripts/hint-tests.sh` checks queued native message
ownership, pending standalone dissolve cancellation and native sibling text/frame reflow/restoration
before composite adoption. Their
negative controls reject premature empty frames, lost message ownership and missing cleanup.
`scripts/quest-hint-tests.sh` executes the exact-producer suppression prefix, checking
native continuation, unrelated/battle-goal hints, scene identity and flat-game behavior.
Negative controls remove the callback, broaden producer matching and remove the VR gate.
`scripts/video-playback-tests.sh` additionally exercises cosmetic decoder failures and native audio
restoration; wire vectors cover the additive movie record and stale playback identities.

### Exact-tree CI evidence

`scripts/ci-proof-reuse.py` uses GitHub's [workflow-run metadata](https://docs.github.com/en/rest/actions/workflow-runs)
and [attempt-specific job metadata](https://docs.github.com/en/rest/actions/workflow-jobs).
It downloads no artifacts, binaries or executable logs. Proof is restricted to this repository's
active `ci.yml`, triggered by a dev push or manual dev run, with a source commit still reachable
from current dev. Its entire tree must match the candidate, including documentation, build
scripts and workflow files. The workflow and verifier must also match current dev's policy.

For PRs, the checked-out synthetic merge must have the event's exact base and head as its two
parents. The workflow-run `head_sha` alone never establishes which PR merge was tested.
Only the dedicated `Full checks [TREE]` job and its successful final completion step establish
proof; `Build and gates` remains the required branch-protection check but cannot mint proof.
A reused PR success cannot be reused recursively as if it had executed tests.

The newest matching dev run and its latest attempt must succeed. A later failed, cancelled,
queued or running attempt supersedes an older green result. Evidence expires after 30 days;
lookup is bounded to the latest 500 dev workflow runs. Missing/inaccessible/stale metadata means
full PR validation. Release fails before building or publishing: run full CI on dev for that
exact tree, then rerun Release. If the workflow/verifier policy changed, integrate current dev
through the normal reviewed main merge first. Existing runs from before this completion marker
was introduced do not qualify; the first new dev push creates the initial proof.

A manually requested DLL upload does not change what full CI proves. Its optional failure does
not invalidate successful checks, and neither uploads nor artifact cleanup can establish proof.
Every release still comes from main; PR merge-resolution edits must first be integrated and
validated on dev under the existing release provenance rule. No paid account or artifact storage
is required for evidence reuse. Fixture and local-Git regression tests run with:

```bash
python3 -m unittest discover -s tests -p 'test_ci_proof_reuse.py'
```

## Temporary development downloads

Normal dev pushes run every check; an internal PR may reuse identical-tree evidence.
Both retain workflow logs without uploading binaries. Local installation through `scripts/install.ps1` is unchanged.
For a hardware-test download, request the existing CI workflow explicitly on `dev`:

```bash
gh workflow run ci.yml --ref dev -f upload_dev_build=true
```

In the Actions UI, select **CI → Run workflow → dev** and enable the upload input when that
button is available. GitHub shows the manual control from the default branch's workflow;
until the next authorized merge to `main`, use the CLI against the registered workflow on
`dev`. Running CI without the input only checks the code. No manual CI action publishes a
release or tag. Upload requests on other refs are rejected with an explicit instruction.

A separate maintenance job with `actions: write` runs on trusted dev pushes and manual
upload requests. Ordinary pushes prune to at most three matching dev artifacts younger than
two days. Manual upload runs are serialized and reserve a slot by pruning to two before the
following single upload. Cleanup jobs also serialize their API snapshots/deletions. New downloads expire after two days and include the commit,
run ID and attempt in their names. The build job retains read-only repository permissions;
PRs never run maintenance; build/test steps never receive its write token.

Cleanup and dev upload are optional delivery steps. Failure produces a workflow warning and
job summary; a failed cleanup prevents another upload. Build/test failures still fail CI and
prevent delivery. The release workflow does not use this optional policy: its main-branch
ZIP upload remains mandatory through `gh release create`, outside Actions artifact storage.

The cleanup script defaults to previewing its decisions:

```bash
python3 scripts/prune-dev-artifacts.py --repo McFredward/GloomhavenVR
# Apply the same count/age policy when an explicit cleanup is intended:
python3 scripts/prune-dev-artifacts.py --repo McFredward/GloomhavenVR --apply
```

Only exact `GloomhavenVR-dev-<40-hex-SHA>` names (with an optional run-ID/attempt suffix)
are eligible. Pagination and candidate metadata are validated before deleting any artifact.
Release assets, workflow runs/logs, unrelated artifacts and caches are outside its scope.
Regression tests run in CI and locally with
`python3 -m unittest discover -s tests -p 'test_prune_dev_artifacts.py'`.

GitHub can take 6–12 hours to refresh artifact usage after deletion. The
[Actions billing documentation](https://docs.github.com/en/billing/concepts/product-billing/github-actions)
distinguishes artifact storage, free workflow logs/summaries and the separate cache allowance.
[Release assets](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases#storage-and-bandwidth-quotas)
use separate limits; do not move release ZIPs into temporary Actions artifacts.

## 4. Release order and recovery

`release.yml` performs these steps in order:

1. Require successful full dev CI for the exact Git tree. Read `<Version>`, reject an existing
   tag, require a clean tree and verify main/dev provenance.
2. Build and verify with job-level `GhvrReleaseBuild=true`, including builds inside packaging.
3. Package and require the exact versioned archive, the asset bundle and an unchanged tracked tree.
4. Render release notes; recheck that the tag is absent and `main` still contains the candidate.
5. Push the tag at the candidate commit, then create the release with `--verify-tag` and the archive.
6. Fetch current `dev`, integrate the released merge without changing current dev content,
   and bump its patch version if it still equals the released version. Retry a rejected
   push up to three times; preserve any manually advanced version and concurrent work.

Release runs are serialized and not cancelled by a newer push. CI runs may supersede
older CI runs. The release version bump carries no `[skip ci]`: such a marker could
later suppress a real release when that commit is pushed to `main`.

Release notes use an optional version-specific highlights file and a collapsed, capped
commit list. See [the highlights guide](../packaging/release-highlights/README.md).

Test the branch topology without publishing:

```bash
bash scripts/release-sim.sh --old
bash scripts/release-sim.sh
bash scripts/release-sim.sh --race
```

The default suite executes the production provenance/bookkeeping helper against temporary
local repositories. It covers repeated PR merges, rejected content changes and unrelated
commits, concurrent dev changes, a rejected push and retry, and a manually advanced version.
`--old` reproduces the historical main-bookkeeping failure; `--race` runs the current suite,
including its race cases. It does not simulate the hosted build, GitHub API or updater.

| Failure | Recovery |
|---|---|
| Non-fast-forward when publishing `dev:main` | Fetch and integrate the missing `main` history into `dev`; re-run review and gates. Never force-push. |
| Branch or tag rule rejects a push | Review the actual repository rule and required actor permissions; do not use a real release push as a speculative test. |
| Existing version tag | Check whether the release already completed and whether the next-version bump landed. Do not overwrite or delete a published tag. The sole documented exception is the build-503 refresh of the existing `v1.0.0` ZIP: it retains the original tag and replaces only the release asset/body from the final `main` commit. |
| Candidate fails release provenance | Use a normal merge of reviewed `dev` into `main`; its tree must match the dev source parent. Integrate content/conflict resolutions into `dev` first. |
| Tag exists but release creation failed | Inspect the failed run; recover the release using the already-tested tag and matching archive. Do not retag a different commit. |
| Release reports missing CI proof | Run full CI on dev for the exact candidate tree, wait for success, then rerun Release. Reused PR checks and old workflow runs without the completion marker do not qualify. |
| Optional dev download missing | Read the CI summary and cleanup/upload step. Check artifact quota; deletion may take 6–12 hours to become visible. Checks remain strict. |
| Release published, dev bookkeeping failed | Merge the release commit into current `dev`, preserve concurrent work, and advance the version if still needed. `scripts/release-provenance.sh prepare REPO RELEASE_SHA DEV_SHA MAIN_SHA RELEASE_VERSION` prepares this locally; review and push `dev` normally. Do not rerun publication. |
| Hundreds of missing game members at compile time | Check game/reference-assembly versions; regenerate stubs as in §2.4. |
| Archive rejected by updater | Check the public endpoint, expected archive name and `SelfUpdateZip` layout rules. |
| No release run after a `main` push | Check workflow presence, repository Actions settings and a `[skip ci]` marker in the pushed commit. |

## 5. What automation cannot establish

### Full wire vectors require the real Unity assembly

`tests/GloomhavenVR.WireTests` executes the game's `UnityEngine.CoreModule.dll` because
quantization depends on `Mathf.RoundToInt` behavior. Metadata-only references cannot run,
and replacing that implementation with a test shim would change the behavior under test.
The real game DLL cannot be redistributed to hosted runners.

Full CI therefore **compiles** this project and explicitly announces that its byte-level
assertions were not executed. Release reuses that compile evidence. Run `scripts/wire-tests.sh`
locally before a release; it reports the current assertion count. The local umbrella guard
already calls it.

Pure source checks should live in `scripts/`, or have a standalone twin there, so CI
can execute them. `check-card-identity-mask.py` and `CardIdentityMaskVectors.cs` are such
a pair; keep their rules aligned.

### Other limits

- **Headset behavior:** stereo rendering, pointer interaction, local/remote animation parity,
  native materials, actual frame times and full-party scaling need hardware evidence.
- **Bundle content:** the format checker validates the UnityFS wrapper and editor version,
  not every mesh or shader. Rebuilding requires Unity 2021.3.5f1 and the appropriate asset
  generator before packing; see [DEVELOPING.md](DEVELOPING.md#asset-bundle).
- **Reference freshness:** CI proves the committed assemblies contain metadata, not that
  they match the user's current game install.
- **RuntimeDeps provenance:** hosted builds compile the provisional package-source set.
  An editor-harvested set would require an explicit distribution change; gitignored local
  DLLs do not reach a runner. `versions.json` records the packaged provenance.
- **Updates:** public availability, download, staged replacement, relaunch and recovery must
  be exercised on Windows with an actual release candidate.

## 6. Versions

`src/GloomhavenVR/GloomhavenVR.csproj` owns `MAJOR.MINOR.PATCH`. `dev` names the next intended
release; after publication the workflow advances its patch component. Dev builds append
the commit identifier and release builds omit the dev marker through `GhvrReleaseBuild`.
Read the source and tags for current values instead of maintaining a second version here.

`NetProtocol.ModBuild` is independent: it is the manual multiplayer compatibility counter.
Increment it for each build handed to another player and record the changes beside it.
Mismatched builds are blocked even when their semantic version matches.

## 7. Agent workflow

Follow [AGENTS.md](../AGENTS.md) for integration, authorization and file ownership, then
[CLAUDE.md](../CLAUDE.md) for technical contracts and the latest
[STATE.md](../.planning/STATE.md) for outstanding validation. Agent-specific tooling does
not replace Git permissions or the release checks above. Never put credentials in tracked
files or treat local shell hooks as a security boundary.

The retained [Claude Code settings](../.claude/settings.json) and shell guard are optional
legacy-client tooling. They do not run as Codex permission enforcement. Their regression
check is `python3 .claude/hooks/test-guard-destructive.py`.
