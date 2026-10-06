# Local Quest Wizard UI (developer notes)

`Quest-Builder.cmd` launches the loopback browser wizard on Windows through
`scripts/quest-builder-wizard.cmd`.
The PowerShell entry point reuses the installer's pinned, script-local CPython and
virtual environment through `tools/quest-installer/bootstrap.ps1`. It passes a
native argument array to Python; paths containing spaces are not assembled into
an interpolated command string. The console remains the backend's lifetime owner.
`-NoBrowser` suppresses automatic browser opening; the backend prints its URL.

The default private state/build root is `%USERPROFILE%\.ghvrq`, rather than the
downloaded checkout directory. This shortens generated Windows Unity paths and
preserves downloads, original exports and imported projects across moving or
replacing the launcher folder. `-StateRoot` overrides that location explicitly.
The pinned Python installation and virtual environment remain script-local.
The backend's ownership marker and kernel guard also protect this shared user
workspace against two launchers attempting to build simultaneously.

The UI has no external fonts, analytics or hosted frontend. Six pinned public
publisher images are included for immediate offline display; the backend may
repair missing images from their exact approved public CDN URLs. The Unity
terms and publisher attribution are explicit external links. German and English strings are
kept in `i18n.mjs`. Further technical settings and bounded logs are collapsed.

## Backend boundary

The backend is maintained separately in `tools/quest-wizard/`. Launch command:

```text
wizard.py serve --state-root PATH --ui-root tools/quest-wizard-ui --open-browser
```

The server emits a loopback random-port URL with a private fragment token. The UI
sends `X-Quest-Token`; the backend validates Host and browser Origin/fetch context.
No browser-supplied command, executable or artwork filesystem path is accepted.

The schema-1 API uses discovery, plan, run, cancel, status, events, fixed-kind
native browse and bounded per-stage logs. `POST /api/plan` receives `{choices}`
and optionally `session` only after explicitly reviewing an existing session's
choices. Backend receipts decide safe reuse. A cancelled or interrupted process
is never presented as an automatically resumable verified build. Polling stops
for inactive sessions; cancellation remains pending until backend confirmation.
Each stage has one persistent total percentage (`progress.stagePercent`). Its
scheduled operations advance that total using observed completion/counters;
substep changes never reset the total. Only verified stage completion publishes
100%. A separately labelled substep bar may use raw `progress.percent` and reset
between tasks. Unknown substep totals remain indeterminate while the stage's
last observed total and concrete current task stay visible. These percentages
describe scheduled work, not time remaining; elapsed time never invents progress.

Unity reporting combines observed editor task counters, content-bank boundaries,
Player build callbacks and known log/Bee counters. The editor does not expose a
reliable percentage for every internal operation. Tasks without a measured total
retain their latest activity text rather than claiming false numerical progress.

Discovery selects a sole detected copy and its provider. Availability of a local
account does not establish a copied profile: the UI says it will be checked.
Purchased DLC are automatic unless the user explicitly chooses the manual
declaration. An explicit empty declaration means base game only.

## Artwork ownership

`assets/gloomhavenvr-logo.png` is an unchanged copy of the project's tracked
`docs/img/logo.png`. `assets/promo/` contains six exact publicly published
character/enemy promotional images, with source attribution in that directory
and SHA/size/dimensions pins in `promo-artwork.json`. They are not extracted from
the owner's game or recovered game banks. The default `Gallery` validates these
bundled files synchronously and serves opaque IDs through authenticated
`/api/promo-artwork`. Selecting a game without owned artwork does not erase the
publisher slideshow. The image area, captions and manual slideshow controls
remain available during setup and build progress.

The optional stdlib `artwork.py` adapter consumes a locally recovered Campaign
project and an independently verified game fingerprint. It requires the original
full-recovery report, exact file size/hash, bounded PNG dimensions, canonical paths
and no symlinks. The backend keeps descriptor paths private and exposes opaque
IDs through its authenticated `/api/artwork` endpoint. `read_artwork` revalidates
every response. Images never become Git, release or cloud content.

This adapter does not decode untouched Unity game containers. Until a verified
recovery cache is available, the production interface shows the bundled publisher
slideshow. The explicit design preview is labelled as a preview. Supporting
the adapter does not imply that a backend with `artwork:false` serves an image.

## Selected mod source

The ordinary release builds the mod source shipped inside the Builder ZIP,
identified by its manifest commit, project version and `NetProtocol.ModBuild`.
It does not adopt the `GloomhavenVR.dll` installed in the PC game's BepInEx folder
or silently fetch the latest `dev` branch. Advanced CLI source selection remains
explicit. Discovery describes the launch release; saved-session status describes
that session's resolved source, even when the launch ZIP has subsequently changed.
The visible mod badge uses this metadata rather than guessing from game files.

"Check mod files" hashes the selected source/tools/art inputs for a consistent
snapshot and safe cached-stage reuse. It does not run the exhaustive shader gate.
Receipt/source changes invalidate the affected work; retaining the owner workspace
preserves completed setup and reusable original exports between source releases.

## Focused verification

```sh
node --test tools/quest-wizard-ui/tests/ui.test.mjs
python3 -B -m unittest discover -s tools/quest-wizard-ui/tests -p 'test_*.py'
node --test tools/quest-wizard-ui/tests/browser.test.mjs
# Optional real HTTP witness; the selected backend must be the matching checkout.
QUEST_WIZARD_BACKEND=/path/to/tools/quest-wizard/wizard.py node --test tools/quest-wizard-ui/tests/real-api.test.mjs
```

The browser check uses installed Chrome and Node's built-in CDP/WebSocket support;
it skips visibly when Chrome is absent and downloads nothing. It serves only the
static UI in explicit `?preview=1` mode, checks the real DOM, keyboard activation,
consent gate, cancellation, restored choices, EN/DE and narrow-screen overflow.
Set `QUEST_WIZARD_SCREENSHOTS` to a private output directory for screenshots.

For manual preview, serve this directory on loopback and open
`/?preview=1&lang=de` (or `en`). Demo mode never calls the real API and deliberately
blocks its mock build before any completed APK/install claim. Screenshots are
design/interaction evidence, not proof of Windows provisioning, a native Unity
build or Quest hardware behavior.

The separate real-API browser witness starts the actual loopback backend with an
isolated temporary state directory. It proves JavaScript MIME/CSP and native fetch
binding, token-authorized discovery/POST planning/status/log rendering, reopening
a durable session and wrong-token rejection. CDP intercepts any accidental `/run`
attempt before it reaches the server; the test requires that no such attempt occurs.
It executes no provisioning, child tools, game conversion or Unity. Missing backend
configuration is reported as a skipped test rather than successful native evidence.

For the optional real owned-art transport check, additionally set
`QUEST_WIZARD_OWNED_GAME`, `QUEST_WIZARD_OWNED_RECOVERY` and
`QUEST_WIZARD_OWNED_INPUT` to matching private original provenance. The test copies
only three actual PNGs plus the unchanged recovery report/input manifest into its
temporary owned cache and establishes a clearly labeled fixture inspect receipt.
This inheritance proves the HTTP/UI artwork route and exact response hashes; it
does not represent a fresh inspect, recovery or build. Browser screenshots remain
private. All temporary projected game files are removed when the test finishes.
