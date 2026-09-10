# Repository preparation for 1.0.0

Reviewed on 2026-09-10, starting from `dev` commit `2164869707e0a14312d4a14aacd5b0a235123858`
(0.9.1, ModBuild 497). This prepares the repository; it does not publish 1.0.0.

## Completed scope

- Reconciled current build, installation, update and CI/release documentation with source.
  Added a [documentation index](../docs/README.md). Historical phase checklists retain their
  referenced paths with explicit historical banners. The old planning index is archived;
  the current [index](INDEX.md) points to current evidence and contracts.
- Preserved the concise bilingual player guides, tutorial recommendation, initiative annotation
  and blue shared-window marker. Corrected installation/restart and update-recovery claims.
- Updated agent workflow guidance to the actual Codex integration rules and private worker
  baselines. Optional Claude hooks remain functional tooling, not a claimed security boundary.
- Removed three generated editor logs, four voice-render frames and nine shader reports from
  tracking. Original local copies are preserved and ignored. New voice outputs go under
  `.planning/debug/renders/voice/`; rendered pixels and assertion coverage are unchanged.
- Retained tools needed for legacy shader restoration and current investigations. The old
  shader patcher is explicitly marked as a recovery tool. The two `classdata.tpk` inputs are
  retained UABEA/AssetsTools metadata (MIT provenance recorded in the patcher README), not
  extracted game program code. Exact Unity 2021.3.5f1 build instructions are consistent.
- Found a stale local bundle silently overriding the reviewed release assets: the main checkout
  still held an ignored 68,522,833-byte Unity output dated 2026-08-25, versus the committed
  74,943,763-byte bundle. Both installers/packagers now select `prebuilt/` by default; developer
  Unity output needs explicit opt-in. The old local file is preserved. This was a packaging
  selection defect, even though the older bundle passed the format/header check.
- Fixed XML escaping of local installation paths and optional uninstall verification failures.
  Both packaging paths now include the project's GPL text and pinned XR software notices.
  Prepared 1.0.0 player highlights without changing the current version.

Details: [hygiene inventory](RELEASE-READINESS-HYGIENE.md) and
[pipeline audit](RELEASE-READINESS-PIPELINE.md).

## Verification

Integrated checks passed against production baseline `21648697`. No gameplay, network,
rendering source or bundled artwork was changed by this audit; ModBuild remains 497.

- Full 17-checker guard: **253,579 wire assertions**, card capture **18,206**, native playback
  **466**, board refresh **1,216**, plus **12** deliberate runtime negative controls passed.
  Compiled comparison: **0 moved, 0 changed, 0 added/removed** types. Configuration keys
  **625**, patch surface **152**, and log tokens **4,716** remain unchanged.
- Strict Release and local release-package build: **0 warnings, 0 errors**. The package comparison
  exposed and fixed stale local bundle selection; final validation also compares the actual ZIP
  bundle with committed `prebuilt/` by SHA-256, rather than trusting layout/header checks alone.
- Current EN/DE guide structure and links pass the documentation checker.
- All 16 committed reference assemblies contain metadata and no executable method bodies.
- Bundle remains 74,943,763 bytes, UnityFS format 7, Unity 2021.3.5f1; its SHA-256 is recorded
  in the hygiene inventory. No asset rebuild was needed.
- Six isolated uninstall scenarios, the real installer XML-writing and license-staging blocks,
  and PowerShell parsing passed again against integrated scripts. The new package passes TXT
  encoding and layout checks. Production updater verification/extraction is checked with per-entry
  SHA-256 comparisons; see the pipeline report for the fixture scope and platform limitations.
- A final documentation review resolved 223 current-guide/local-reference targets, and corrected
  stale STATE claims about concealment, worker limits and available performance measurements.

### Optional tester-default comparison

`rebase-defaults.py check` was also run against the available older tester configuration;
it returns **2**, so this optional comparison is not counted as a green gate. Its three value
differences are cheats enabled, Debug logging and disabled vertical world dragging in that
local drop versus shipped false/Info/true. These are not changed by this audit. Three
non-literal card-grip initializers are refused by its parser; manual source comparison confirms
30 degrees, zero offset and 0.22 seconds match the drop. Three unmatched keys are stale
HandsDisturbScenery/HandsDisturbVfx/CombatLogUserClosed entries, with no current bind.
The requested build 497 defaults remain explicitly pinned. No automatic rebase was applied.

## History and publication boundaries

A redacted **Gitleaks 8.30.1** scan of Git history reachable from the pre-audit `origin/dev` and
`origin/main` completed: 2,571 commits scanned, about 167 MB. It reported two `generic-api-key`
findings in historical `Core/Loc.cs` controller-help comments. Both were inspected and are
false positives on the word `keycaps` and its explanatory comment, not credentials. No other
credential findings were reported. This is a bounded scanner result, not a guarantee about
all private information. The gitignored `.env` was not opened or included in the scan.

Removing files from the current tree does **not** remove their earlier versions from Git
history. Old shader disassemblies and raw editor logs remain reachable. Before exposing that
history publicly, choose and execute a separate history-publication approach; this audit does
not authorize a force-push, history rewrite or deletion of existing releases. Local investigation
material remains available for regression analysis.

**Maintainer decision, 2026-09-10:** leave the existing standard Unity Asset Store license
record and fire asset unchanged; the maintainer will handle that question later. No additional
permission was asserted and the deferred question is not counted as cleared by this audit.
Only software-license notices were added to the package; existing fire-license text was preserved.

The repository was verified **private**, with **main** as the default branch, during this audit.
Visibility was not changed. Source gates and local ZIP validation do not exercise public GitHub
availability or Windows process replacement. A real public updater test remains separate.

## Actual release remains a separate action

When the maintainer requests 1.0.0, set that version on `dev`, review the prepared highlights,
run the exact-candidate checks and fast-forward `main` to that reviewed commit. The existing
main-only release workflow then builds, tags and publishes it. See [CI/CD](../docs/CI-CD.md).
This preparation creates no 1.0.0 tag or release and does not push `main`.

Headset validation of build 497, four-player scaling and the earlier rare long-rest visibility
report remain as recorded in [STATE.md](STATE.md). Automated checks do not certify these outcomes.
