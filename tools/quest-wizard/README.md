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
only. Artwork becomes available only after an inspect receipt and server-owned recovery cache witness matching original PNGs. GET `/api/artwork?session=ID&id=OPAQUE` checks the token/origin and image SHA again; status exposes only opaque IDs/URLs, never a filesystem path. Capture and cache cleanup capabilities remain false.
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

Unity Hub is downloaded automatically from a versioned official URL and verified
against the publisher SHA512/size pin before its installer window starts. Setup,
sign-in and license activation remain visible user actions where Unity/Windows
require them. The installed Hub's archived-version CLI is checked at runtime before
Editor/module automation; the existing Editor receives only its missing Android
modules. Unity documents this Hub CLI as deprecated with minimal support from
3.18; an absent command produces a specific actionable fallback, never a pretend
installation. A version check does **not** prove license activation. `licenseVerified:false` is explicit. This backend does not
promise unattended Unity licensing. Unity failure logs remain local.

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
