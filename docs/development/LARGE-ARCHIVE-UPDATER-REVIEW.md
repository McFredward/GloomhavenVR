# Large release archive: compatibility with released updaters

Audit date: 2026-10-09. Candidate: `dev` commit
`8e55643d6bb699a21761d0e04ad758e82c2eea50`, runtime Build651. This review
does not create or replace a release and does not modify runtime code.

## Result

The freshly packaged `GloomhavenVR-1.1.0.zip` is accepted and completely
extracted by the actual verifier/extractor source shipped in **every release
from v0.9.0 through v1.0.8**, as well as the current implementation. The
expanded asset bank, additional bundle parts and nested Frame setup files
introduce no archive-layout incompatibility with those releases.

| Candidate property | Measured value |
|---|---:|
| Download size | 530,088,154 bytes, approximately 505.53 MiB |
| Expanded file size | 586,528,277 bytes, approximately 559.36 MiB |
| ZIP entries, including directories | 112 |
| Actual files | 102 |
| Root files | `INSTALL.txt`, `INSTALL-DEUTSCH.txt` only |

ZIP SHA-256:
`6da0d4923632093e79f446b263b3d93070f8cfb3f8bf971363e7a89964e41ff0`.

The published updater uses streaming download and extraction, not a
whole-archive memory buffer. A larger ZIP increases bandwidth and free-disk
requirements. It does not hit a current size, path or entry-count boundary.

## Released-source equivalence

Each tag was resolved and all six files below were extracted with `git show`.
`source-matrix.json` records each exact commit and source SHA-256. The six
files are byte-identical across all ten released revisions:

| Release | Resolved commit |
|---|---|
| v0.9.0 | `6ed31fa182a0cef9a785a5d2389f18ebc2a10d86` |
| v1.0.0 | `7639a2ae176970f2ae04f114a237af11f9bb60e0` |
| v1.0.1 | `5874c4bc2879c86a42b1ca186d6858deb411aaa4` |
| v1.0.2 | `d1e8e5f74db6a499cf42402c9c490b0cbeffb463` |
| v1.0.3 | `15bf6b6659ac2da2c8409e938e50f0175ea010c6` |
| v1.0.4 | `fe80b45fcc5bb0464f76e19f3879945ca5be887d` |
| v1.0.5 | `f789494d6873b27a429ee5b1caa0dd301a01fc1e` |
| v1.0.6 | `36258d06e50f2391ca49935b6b538d94e612885a` |
| v1.0.7 | `071a81f20e89437c1455d9d792e478979bc25da4` |
| v1.0.8 | `4640fff2f0dedf5e4c8d3ded7d21684ef4b5ed60` |

| Exact released source | SHA-256 |
|---|---|
| `SelfUpdateZip.cs` | `4bc1ba72e48ddc81ac73b0387eec4777de03c7301a932e6de0ec405c7a2200a7` |
| `SelfUpdateRelease.cs` | `ec6789666c1f01781867f005ebf6ce626263a659a93bdb2d696b5b455d45d7f7` |
| `SelfUpdateInstaller.cs` | `0943fad133b1766c53999702851845161ec5ffa325a66e98733ff22447253836` |
| `SelfUpdateApplyScript.cs` | `374b5dce4f2d03da923835107be79683fe542ba11e8587e0ed9044ae57c52709` |
| `SelfUpdateCheck.cs` | `6a6abc117b3c59ddae72bcce2f2cb40b46f30f4cb267829400746a967c7affcc` |
| `SelfUpdatePaths.cs` | `e2dccf75f602b59f2e31d98042d01a8f38812e0dd0788794ccb6326f97e9c1e2` |

One actual released-source execution therefore covers all ten tags without
pretending to have run ten different implementations. A second execution
covers current source. Current `SelfUpdateZip` changes only explanatory
comments and makes a null/non-stream `ZipArchiveEntry.Open` result fail
explicitly. Current apply-script differences remove six exact obsolete
Frame support files from the game root after a successful installation.
The released parser, downloader, check and staging cleanup remain identical.

## Executed compatibility proof

The disposable fixtures compile **unmodified source extracted from the tag**
for `SelfUpdateZip`, `SelfUpdateRelease` and `SelfUpdateApplyScript`. They run
on .NET 8.0.29. `Nullable=annotations` preserves historical nullable syntax
without turning modern compiler flow analysis into a modification of the old
source. All fixture builds treat compiler warnings as errors.

The independent manifest is produced by streaming every file from the real
ZIP with Python `zipfile` and SHA-256. Each implementation then:

1. Verifies the actual 530,088,154-byte ZIP against its published-size input.
2. Checks every manifest entry and the expanded byte accounting.
3. Extracts the **entire** archive with its original `Extract` method.
4. Checks exact file count, each file's length and all 102 content hashes.
5. Checks both required assemblies are present and nonempty, matching the
   staging worker's required-assembly postcondition.
6. Generates the original apply script and checks its recursive BepInEx
   copy, backups, process-wait order, both root guides and Steam relaunch.

| Executed source | Assertions | Extracted files | Hash mismatches | Full extraction |
|---|---:|---:|---:|---:|
| Released, exact v1.0.8 | 449 | 102 | 0 | 1,799.70 ms |
| Current, exact audited dev | 449 | 102 | 0 | 1,809.79 ms |

These local extraction times are SSD/.NET fixture observations, not download
or headset/Wine timings. Each full extraction tree was deleted immediately
after its checks and receipt were written.

Controls retain rejection of unknown root files, a root desktop launcher,
zip-slip, backslashes, absolute paths, drive separators, incorrect byte size,
missing archives, non-ZIP input and a missing required assembly. Positive
controls include both root guides, a nested Frame launcher and an additional
bundle under BepInEx. Release parsing checks the actual size, version ordering,
the signed-32-bit boundary, acceptance at 4 GiB minus one byte and rejection
at exactly 4 GiB.

The unchanged existing `GloomhavenVR.SelfUpdateArchiveTests` suite was also run
against this ZIP twice, retaining **all 22 original assertions** each time:
once with current `SelfUpdateZip`, once using its supported `ZipSource`
override to link the exact historical verifier. That suite retains the
current apply-script generator in both runs; the disposable fixture above
separately executes the exact old generator. No test expectation was weakened.

Retained evidence in the main checkout is gitignored under
`.planning/debug/large-release-audit/compat/`:

- `source-matrix.json`: all tag commits and six source hashes each.
- `archive-manifest.json`: exact paths, lengths and independent content hashes.
- `released/run/receipt.json`, `current/run/receipt.json`: actual executions.
- `released/run/apply-update.cmd`, `current/run/apply-update.cmd`: generated scripts.
- `archive-regressions-{released,current}.log`: original regression suite receipts.
- `Program.cs`, both fixture projects and extracted original source: reproducible
  local audit code, retained as evidence rather than adding a permanent test gate.

## Size-related operational boundaries

### GitHub hosting and publication

GitHub's official [release documentation](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases)
requires each asset to be smaller than 2 GiB and specifies no limit on a
release's total asset size or bandwidth usage. The current ZIP uses about
24.68% of that individual asset limit. It does not need to be split into
separate downloads. Bundle parts remain ordinary files inside one compatible
ZIP.

The integration audit executed the production
`scripts/release-upload.py` `GitHub.upload` path with this entire actual ZIP,
through standard-library `HTTPConnection.send`, into a hashing/counting
socket sink. The resulting `Content-Length`, total bytes and SHA-256 exactly
matched the archive, the maximum send chunk was 8,192 bytes and measured RSS
growth was 384 KiB. This verifies that the publication path streams the asset
rather than duplicating the entire archive in memory. It performed **no real
network connection, TLS session or GitHub upload**. Receipt:
`.planning/debug/large-release-audit/publication.json`.

### Download and memory

The original downloader uses `DownloadHandlerFile`, sets the request's overall
timeout to zero and compares progress against `long` byte counts. It aborts
only after **45 seconds with no byte progress**; a slow but continuing download
has no total-duration cap. Extraction uses `Stream.CopyTo` with a 64 KiB buffer
on the staging background thread. Neither code path loads the whole ZIP into
a managed byte array. The release JSON's 4 MiB text cap applies to API metadata,
not the asset download.

The released release parser requires `0 < asset size < 4,294,967,296` bytes.
That is an existing hard **less-than-4-GiB** limit. The current ZIP is comfortably
below it. The verifier's entry bound is 4,096 and its safe-name length bound is
240 characters; every current entry passes. Extra parts stay compatible while
they remain inside `BepInEx/` and those limits.

The parser chooses the **first acceptable `.zip` asset** in GitHub's response.
Adding differently targeted ZIP assets to a future release requires care:
older installed code cannot use a future new filename-selection rule before
it downloads the update. Preserve an unambiguous compatible release ZIP.

### Free disk space and applying an old installation

The updater stores the ZIP and expanded staging tree on the game volume. The
detached applier then backs up both existing mod-owned folders and recursively
copies the staged BepInEx tree with `robocopy /E`. This copies new bundle parts
and the nested Frame setup directory without a fixed filename list. It does
not use `/MIR`; existing player configuration and unrelated files are retained.

No released or current updater performs a free-space preflight. More space is
needed than the download alone. For an ordinary smaller old installation,
additional peak space is approximately `ZIP + 2 × expanded new mod`, combining
the archive, staging, old backup and net live-install growth. This candidate
therefore needs approximately **1,703,144,708 bytes (1.59 GiB) extra free space**
on the game volume; **2 GB free** provides practical headroom. Large extra files
in the backed-up mod directories increase that requirement. A conservative
general estimate is `ZIP + expanded + max(existing owned install, expanded)`.

That estimate is byte arithmetic, not a Windows allocation measurement; file
system block overhead, antivirus behavior and unrelated simultaneous writes
are not modeled. Out-of-space staging fails before the running install is
replaced. Backup failure aborts before installation; a later copy failure
enters the existing backup restore branch. Because this uses overwrite-only
copying, it does not remove newly introduced files after a partial failed
update or obsolete files missing from a newer ZIP. Those are pre-existing
apply-script semantics, not evidence of a failure with this archive.

## Scope and limitations

This proves old/current **managed archive, parser and generated-script
compatibility with the actual new package**. It does not execute a live GitHub
download, Unity's actual HTTP stack, Windows `cmd.exe`/`robocopy`, Steam
Frame/Wine process handover, a complete game restart or headset rendering.
The integration agent's publication transport proof and official hosting-limit
review are included above, with their own execution limits. No release, runtime
code or asset changes were required, and no complete gameplay gate was rerun
for this documentation-only audit.
