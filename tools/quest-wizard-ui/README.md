# Local Quest Wizard UI (developer notes)

`scripts/Build-Quest-Wizard.cmd` launches the loopback browser wizard on Windows.
The PowerShell entry point reuses the installer's pinned, script-local CPython and
virtual environment through `tools/quest-installer/bootstrap.ps1`. It passes a
native argument array to Python; paths containing spaces are not assembled into
an interpolated command string. The console remains the backend's lifetime owner.
`-NoBrowser` suppresses automatic browser opening; the backend prints its URL.

The UI has no external fonts, remote artwork, analytics, CDN or hosted service.
The Unity terms link is an explicit external link. German and English strings are
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
Unknown build percentages stay indeterminate. Stage counters and actual per-stage
percentages have distinct captions; there are no elapsed-time estimates.

Discovery selects a sole detected copy and its provider. Availability of a local
account does not establish a copied profile: the UI says it will be checked.
Purchased DLC are automatic unless the user explicitly chooses the manual
declaration. An explicit empty declaration means base game only.

## Artwork ownership

`assets/gloomhavenvr-logo.png` is an unchanged copy of the project's tracked
`docs/img/logo.png`. The shipped decorative cards are CSS and Unicode symbols.
No original game art or game files are included in this directory.

The optional stdlib `artwork.py` adapter consumes a locally recovered Campaign
project and an independently verified game fingerprint. It requires the original
full-recovery report, exact file size/hash, bounded PNG dimensions, canonical paths
and no symlinks. The backend keeps descriptor paths private and exposes opaque
IDs through its authenticated `/api/artwork` endpoint. `read_artwork` revalidates
every response. Images never become Git, release or cloud content.

This adapter does not decode untouched Unity game containers. Until a verified
recovery cache is available, the production interface shows its authored decorative
cards. The explicit design preview labels those cards as placeholders. Supporting
the adapter does not imply that a backend with `artwork:false` serves an image.

## Focused verification

```sh
node --test tools/quest-wizard-ui/tests/ui.test.mjs
python3 -B -m unittest discover -s tools/quest-wizard-ui/tests -p 'test_*.py'
node --test tools/quest-wizard-ui/tests/browser.test.mjs
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
