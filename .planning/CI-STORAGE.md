# CI artifact storage policy — 2026-09-13

The maintainer approved bounded, manually requested development downloads without a larger
GitHub account. Release uploads from main must remain mandatory. This changes CI only;
version 1.0.0 / ModBuild 499 and the game runtime remain unchanged.

## Evidence

The build-499 run 34779935370 passed its build and all checks but failed its final optional
DLL upload because GitHub reported an exhausted Actions artifact storage quota.
Repository enumeration found 392 development artifact records, 201 unexpired, accounting
for 1,638,555,338 unexpired bytes. Current uploads were approximately 9 MB each and retained
for 14 days after every successful dev push. Caches occupied 843,646,777 bytes in their
separate allowance; release ZIPs already use the Releases API directly.

The initial local cleanup preview selected all 392 matching records because even the newest
was older than two days. The local PAT lacks Actions write permission: DELETE returned 403
before any deletion. No broader PAT permissions were requested. The authorized cleanup is
instead performed by the next trusted dev CI run using its scoped GITHUB_TOKEN.

## Implementation

- Pushes/PRs retain the complete build and validation suite with no binary upload.
- A workflow_dispatch input, upload_dev_build (default false), requests a temporary DLL
  download from dev. Other refs explicitly reject an upload request.
- Trusted dev pushes run a separate maintenance job retaining at most three recent dev
  artifacts. Manual upload requests prune to two before uploading one. Downloads expire
  after two days. Cleanup jobs serialize; manual upload runs also serialize. Unique names
  include the commit, workflow run ID and attempt.
- Only the maintenance job has Actions write permission. Build/test steps remain read-only,
  and PRs never execute maintenance. Cleanup failure warns and prevents optional upload;
  upload failure warns in the job summary. Build and test failures remain hard failures.
- prune-dev-artifacts.py defaults to dry-run, validates the complete paginated listing before
  any mutation, and authorizes only exact legacy/new GloomhavenVR-dev artifact names. It
  addresses only Actions artifact endpoints. Runs, logs, release assets, caches and unrelated
  artifact names are outside the deletion scope. Non-404 API errors abort cleanup.
- release.yml is byte-identical to the prior commit: push/main, contents-write, strict checks,
  packaging and mandatory gh release create ZIP upload remain intact. No main push, release
  publication, tag replacement or repository-visibility change is part of this work.

## Verification

Cleanup boundary tests: 20 pass. Independent temporary negative controls fail when the name
filter or retained-count condition is removed. Actionlint 1.7.11 accepts both workflow files.
Independent review checks the push/PR/manual truth table, permission split, dependency and
failure handling, unique artifact names and concurrency groups. Local required gates pass:
all 17 checkers, 253,579 wire assertions, capture 18,206, playback 466, board refresh 1,216,
modal 1,058 and all 17 runtime negative controls. Strict Release has zero warnings/errors;
bilingual docs pass. Compiled comparison against 4f15f0af is identical: zero changed, added
or removed types. Config 625, patch surface 152 and log tokens 4,717 remain unchanged.
Hosted cleanup must additionally be checked against its actual run.

The existing CI workflow is already registered and present on main, so its dev definition can
be requested through GitHub CLI/API. The web Run workflow control follows the default branch
definition and becomes available with the next authorized main merge. See
[CI-CD.md](../docs/CI-CD.md#temporary-development-downloads) for the command and policy.

GitHub may take 6–12 hours to refresh artifact quota after deletion. This is why an optional
development upload must not turn otherwise successful checks red. The mandatory main release
upload is separate and does not consume Actions artifact storage. The existing v1.0.0 tag
still requires deliberate handling before publishing a replacement final release.
