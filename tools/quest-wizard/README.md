# Local Windows conversion wizard backend

The standard-library backend persists private local choices and seven verified
stage receipts under a separately marked owned workspace. It provisions pinned
portable Git and .NET 8/10 without changing system installations, resolves the
selected mod source to an immutable commit, and uses the existing builder's
owned-game, profile and DLC rules. Local uncommitted source inputs remain part of
the existing source inventory; ignored runtime dependency assemblies are copied
when available or derived from the selected source's existing XR package projects.
No Bash or private maintainer cache is required for that derivation.

Run `python tools/quest-wizard/wizard.py serve --state-root C:\GHQ
--ui-root tools/quest-wizard-ui --open-browser` with the Windows launcher. The
launcher supplies its existing isolated local CPython. Recovery continues to use
the separate pinned conversion interpreter; no extension wheel is loaded into
that CPython merely because it serves the UI.

The browser API is loopback only with a random request token. Mutations require
an exact Origin. GETs require the same token and either exact Origin or the
browser's same-origin Fetch Metadata, always with the exact bound Host. UI assets
are served from the declared ordinary directory; no arbitrary file endpoint,
commands, CORS or credentials are accepted. Local log responses are limited to 64 KiB for an explicitly named stage,
including its named Unity prerequisite or bounded asset-export logs when present. The token is in the browser fragment, never
HTTP request URLs or request logs. Closing the backend requests cancellation and
waits for owned child shutdown before exiting.

## JSON protocol

CLI commands `plan`, `status`, `run`, `cancel`, `discover`, and `serve` require
`--state-root`. Plan requires `--choices-file`; optionally `--session` amends an
explicitly selected prior session. Run/status/cancel require `--session`. The
server prints one `schema:1,event:server,url` object. Progress has stage IDs
`tools,source,unity,profile,inspect,build,install`; an unknown real percentage is
`null`, never an invented elapsed-time estimate.

HTTP uses GET `/api/discover`, `/api/status?session=ID`,
`/api/events?session=ID&after=N`, `/api/log?session=ID&stage=STAGE`; POST
`/api/plan {choices,session?}`, `/api/run {session}`,
`/api/cancel {session}`, `/api/browse {kind:game|unity}`,
`/api/action {session,action:unity-open|unity-check,nonce}`. The last route accepts
only an action currently declared by the running Unity stage. Closing a Hub or
installer window keeps the wait visible; reopening it does not complete the stage. Native browse is Windows
only. GET `/api/gallery` exposes twelve hash-pinned public publisher character/enemy
images bundled with the source release. An optional repair uses the exact official
announcement CDN pins anonymously in a private local cache. No account or platform
service is used, and an unavailable picture does not block conversion. GET
`/api/promo-artwork?id=OPAQUE` revalidates the exact bytes and PNG/JPEG header.
After a matching inspect/recovery witness, local game portraits can also become
available. GET `/api/artwork?session=ID&id=OPAQUE` checks the token/origin and image SHA again; status exposes only opaque IDs/URLs, never a filesystem path. Capture and cache cleanup capabilities remain false.
Discovery lists readable recent opaque session IDs because browser storage belongs
to the random port's origin. The browser automatically opens the last selected
session with its saved choices and progress, without starting any work. **Continue**
uses that same ID; **Set up a new build** explicitly opens a fresh selection without
removing previous sessions or verified caches. A missing/corrupt latest pointer
falls back to a readable recent session. Failure to load the selected session stays
visible and blocks accidental fresh planning until retry or an explicit new selection.

## Resume and prerequisites

Completed steps are reused only after their output size and SHA256 match the
saved receipt and their input/dependency key matches. Installation failures can
retry independently of an already verified APK. A changed profile does not
repeat portable-tool or source provisioning. Cancelled downloads retain partial
bytes; HTTP Range responses must match them. Tool extraction repairs members
against the pinned ZIP's CRC and publishes completion atomically. An OS kernel
workspace lock releases after process death; no persisted PID authorizes killing
another process. Windows children start suspended and join a kill-on-close Job
Object before resuming; cancellation waits for owned descendants to exit.

Unity Hub is downloaded automatically from a versioned official URL and verified
against the publisher SHA512/size pin before its installer window starts. Setup,
sign-in and license activation remain visible user actions where Unity/Windows
require them. The installed Hub's archived-version CLI is checked at runtime before
Editor/module automation; the existing Editor receives only its missing Android
modules. Unity documents this Hub CLI as deprecated with minimal support from
3.18; an absent command produces a specific actionable fallback, never a pretend
installation. A version check does **not** prove license activation. The Editor must also
complete a bounded, owned empty-project probe with the exact success marker. Only
then is `licenseVerified:true` recorded. The probe proves that the Editor can run;
it does not decide account/license eligibility or read/export license contents.
Old unverified prerequisite receipts are invalidated by prerequisite policy 2.
Sign-in remains an explicit repeatable user action; supported headless tools have
timeouts, and their authentication failures return to the visible wait. Unity
failure logs remain local.

After hard process death, a saved running session is marked interrupted only
when the kernel workspace lock has no active owner; a live external run remains
protected. No persisted PID is terminated during reconciliation.

The selected source must declare `BUILDER_RESUME_CONTRACT=1` to resume an
interrupted imported build; older source is retained and produces an explicit
action instead of destroying its Library. The current contract journals mutable
content, verifies completed source-export batches, and retries only the owned
unfinished work. An unchanged original game keeps the same Unity workspace when
the mod or local profile changes; each changed build still needs its own verified
receipts and newly generated assets. Library survives regeneration, but asset
copying is still required. Final Android color/API settings are serialized before
the first Editor start. No faster import time is claimed without a measured run.
Signing keys and device saves are never reset by this workflow.

The native compiler policy derives concurrency from measured CPU, physical RAM
and (on Windows) available commit headroom. It reserves 32 GiB for the first
compiler and 28 GiB for each additional one, plus Unity/OS headroom. A user job
setting is an upper bound. If physical RAM is insufficient but known Windows
commit headroom covers the full one-job budget, exactly one paging-assisted job
is admitted; unknown or insufficient headroom stops before Player-build output
changes. Accepted warm native results bypass this fresh-compilation admission.
Resource records expose the chosen budget and paging requirement, without
claiming that Windows paging speed has been measured. The private .NET apphost
routes only this Editor process to the original Bee backend with the thread
option before its target; the installed Unity tools are not edited.

New shader caches use `tool-cache/cs/<owned-game-hash>/o`; prior longer cache trees
are preserved. Original shader asset names and GUIDs remain unchanged. The shader
producer checks the actual Windows output roots before writing: a sufficiently
long custom build root still needs a shorter directory. Cache path shortening
does not assume Windows or Unity supports every extended-length path.

Tool pins come from [official .NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json)
and [Git for Windows](https://github.com/git-for-windows/git/releases/tag/v2.51.0.windows.1).
Unity's [Hub CLI](https://docs.unity.com/en-us/hub/hub-cli-reference) and
[license workflow](https://docs.unity.com/en-us/hub/manage-license) remain its
authoritative installation and activation contracts.

Focused source fixtures: `python -B -m unittest discover -s tests/quest-wizard`.
They do not download tools, import Unity projects or assert hardware outcomes.

## Complete Windows source release

The installer-only hardware handoff is not a builder distribution. Assemble the
full game-free builder from a committed integration checkout with:

```sh
python -I -B scripts/package-quest-builder.py --output /private/output/GloomhavenVR-Quest-Builder.zip
```

The assembler inventories tracked mod source, authored Unity project assets,
weaver, recovery, native/procedural source, Wizard/UI, launchers and tool pins.
The explicit public converter ZIP includes pinned open-source binaries,
corresponding upstream sources and license notices. Original-derived prebuilt
figure meshes, reference/game DLLs, decompiled game code, credentials, local
Unity licenses, caches, APKs and savegames are excluded. The release manifest
contains exact relative paths, sizes, hashes and the actual source commit/build;
assembly independently extracts and verifies the ZIP before publishing its audit.
The two declared Unity authoring source links are verified against their exact
in-repository mod targets and materialized as ordinary files; no links ship.
Future tracked mod files inside declared source roots enter the next release.
New root families or binary dependencies require an explicit inventory review.

Extract into a fresh folder and double-click `Quest-Builder.cmd`. No Git
checkout or installed Python is required. The launcher bootstraps pinned local
CPython/venv, then starts the existing browser Wizard. Portable Git is still
provisioned for public XR package sources; it is not needed to identify the
shipped mod. The separately owned build state defaults to `%USERPROFILE%\.ghvrq`.
Do not overlay different source release archives: unlisted source files are
rejected. Moving/replacing the release folder does not remove user build caches
or signing keys. A changed release manifest invalidates source and downstream
stage identities while portable tools remain reusable. Each release gets its
own resumable source-copy directory; old source workspaces remain untouched.
Generated compile-time XR assemblies are derived from the user's game files and
are inventoried separately from shipped source.

Before downloads the backend checks Windows x64, a short workspace root and a
4 GiB **setup-only** free-space floor. Before starting, the UI also presents a
bounded estimate of full-conversion disk demand: eight times the owned data size
plus a 20 GiB tools/import reserve, reduced conservatively by matching retained
snapshot/project/recovery candidates. The metadata-only scans have time/file
bounds. Unrelated game caches are not credited. Reuse is provisional until normal
content hash checks pass; native/package reserves remain even with large caches.
An amber warning displays estimated additional space versus available space,
and a provably insufficient fresh snapshot + setup stops before SDK downloads.
This planning estimate is not a full-game guarantee:
Unity imports and recovered content require substantially more free disk space,
and the check records `fullBuildSpaceVerified:false`. Unity Hub setup/sign-in and
activation of an eligible license remain guided manual steps. The backend never
reads or exports license contents and does not infer license validity from the
Editor version. Real Windows provisioning/conversion and headset outcomes still
require user testing; a package audit establishes only its delivered inputs.

Missing original-derived figure mesh banks keep the complete original actor
meshes intact through the mod's documented fallback. The Quest pipeline does not
yet generate Android derivative LOD banks; excluding desktop/game-derived banks
from public source delivery does not establish Steam Frame geometry-budget parity.

## Shareable build diagnostic package

The EN/DE **Save diagnostic package** button downloads a local support ZIP,
including after failure or while a build runs. Nothing is uploaded. CLI users can
run `python -I -B scripts/export-quest-build-support.py` for the latest session,
or select `--state-root`, `--session`, and a new `--output` destination. The
Wizard CLI also exposes `support --state-root ROOT --session ID [--output FILE]`.
Token/origin-protected POST `/api/support {session}` returns `application/zip`;
there is no arbitrary filesystem-path download endpoint.

The ZIP contains bounded stage/error context, mod/source/input/build identities,
host CPU/architecture, and the resource policy/events/metrics produced under
`build/evidence/`. These records supply memory, chosen jobs, stage timings and
outcomes when present. Only direct known stage/build logs, the three named resource records, and
explicit `core-export.log` / `BundleRecovery/batch-NNN/export.log` paths from at
most two current recovery workspaces are eligible. At most eight recent batch
logs per workspace are retained; no recursive project/cache/game traversal occurs.
When an owner-labelled raw-export binding points the current recipe at an earlier
export folder, that folder's logs are included. A failure before binding publication
can instead expose at most two recent folders, explicitly labelled as candidates.
Diagnostic metadata records only existence and byte sizes for five named receipt /
identity / journal files. Their contents and full proprietary asset inventories are
never read or exported by this diagnostic scan. These observations do not qualify
cached content or bypass the recovery process's hash checks.
Profile and DLC JSON, environment files, license files, signing credentials,
game assets and saves are excluded. Known profile/path values, authorization,
token/password/serial fields, signed URL queries, JWTs and key blocks are
redacted. Structured resource objects receive separate key redaction; oversized
structured records are omitted rather than exporting unparsed secret fields.
Each log retains bounded beginning/end context (2 MiB/file, 24 MiB total,
64 files); the manifest records truncation, omissions and exported hashes.
The export reads saved state directly and leaves running/interrupted workflow
status and receipts unchanged. An export is a diagnostic snapshot, not proof
that its APK was installed or that the headset picture is correct.

## Observed progress and failure visibility

Each stage carries its named current substep, real done/total/unit counters,
percentage and update time. `stagePercent` is a persistent total of observed
scheduled stage operations and remains below 100 until verified output publication.
`percent` retains the secondary substep counter; switching substeps can change its
denominator without resetting the stage total. Neither percentage estimates elapsed
time or time remaining. Downloads use bytes, snapshots/hash checks use actual file/byte
inventories, bounded recovery uses committed catalog batch counts, and Bee native
compilation uses its emitted action counts. AssetRipper API calls or other tools
without native counts remain visibly unknown; elapsed time never invents progress.
The child `GHVRQ_PROGRESS` protocol is enabled only for Wizard runs. Ordinary CLI
output stays quiet. State/event writes are rate limited and the final observed
counter is persisted; a failed or interrupted step never receives a success receipt.

The UI shows required actions and failures above the stage list. Failure context
names the affected builder substep and a concise next action; detailed original
error text, exception type/traceback, process exit, bounded invocation arguments
and log locations remain in `logs/progress.log` and the support ZIP. Credential
flags and environment contents are not logged. The log selector follows the
active/failed stage until the user chooses another one. Live viewing includes
known nested exporter logs instead of only the wrapper's generic exit message.
An old `last-failure.json` from another attempt cannot replace the current cause.

A changed conversion recipe gets a separate recovery key. Existing source/tool
caches and old checkpoints remain preserved, but an exporter correction can
require a new coherent export. This is not a promise that incompatible native
export checkpoints can be reused across recipe changes.

The raw-export eligibility reader permits the full `core-recovery.json` file
inventory up to 256 MiB; smaller control/input manifests remain capped at 16 MiB.
The read itself stays bounded if a file grows after its size check. Missing,
non-regular, oversized and unreadable evidence have distinct diagnostics with
observed bytes and the applicable limit. Reading this larger inventory does not
hash all game assets in the eligibility helper: the child still independently
verifies the retained core/batches and restores the actual pending merge journal.
