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
commands, CORS or credentials are accepted. Local logs are limited to the final
64 KiB of an explicitly named stage. The token is in the browser fragment, never
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
`/api/cancel {session}`, `/api/browse {kind:game|unity}`. Native browse is Windows
only. Artwork, capture and cache cleanup capabilities currently remain false.
Discovery lists recent opaque session IDs because browser storage belongs to the
random port's origin. Reopening a session does not start any work automatically.

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

Unity installation uses an existing Unity Hub's documented archived-version CLI
only after explicit acceptance of applicable terms. Hub installation and license
activation/sign-in remain visible user actions; a version check does **not** prove
license activation. `licenseVerified:false` is explicit. This backend does not
promise unattended Unity licensing. Unity failure logs remain local.

The central builder's interrupted imported workspace currently needs its pending
transactional resume integration: the backend retains such a workspace and
blocks rather than destroying its Library. This is an explicit interim limitation,
not complete whole-build resume. Stable asset workspaces across changed mod
revisions and shortening generated Windows paths are separate pending builder
improvements. Signing keys and device saves are never reset by this workflow.

Tool pins come from [official .NET release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json)
and [Git for Windows](https://github.com/git-for-windows/git/releases/tag/v2.51.0.windows.1).
Unity's [Hub CLI](https://docs.unity.com/en-us/hub/hub-cli-reference) and
[license workflow](https://docs.unity.com/en-us/hub/manage-license) remain its
authoritative installation and activation contracts.

Focused source fixtures: `python -B -m unittest discover -s tests/quest-wizard`.
They do not download tools, import Unity projects or assert hardware outcomes.
