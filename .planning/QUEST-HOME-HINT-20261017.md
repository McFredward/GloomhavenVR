# Quest homepage update recommendation — 2026-10-17

The maintainer reports that the Mod Update tile stopped highlighting after the
latest releases. The exact source reproduces this: a completed APK and retained
Campaign bank through twelve compact updates give `available=true`, but
`inputsReady=false` and `missing=[campaign-content]`. The homepage correctly
requires both booleans; its styles and rendering predicate are unchanged.

The locator stopped after eight closed base owners. Replace that short lifetime
with bounded metadata traversal: at most 256 distinct owners and 4 MiB of total
JSON, existing per-file bounds, same-input and cycle checks. Reuse the selected
closed receipt rather than reading it twice. Only the selected APK must exist at
its recorded size; older APK bytes are unnecessary after cleanup if their closed
receipts still locate the present matching-size bank. Missing/cross-input/cyclic
metadata and missing/truncated banks must never claim ready.

This is a homepage hint, not signature, ownership or byte verification. The
normal update preflight is unchanged. The hint opens no APK/content bytes, hashes
no payloads and scans no build directories. It does not start/resume a build.
Current or newer installed ModBuilds remain unhighlighted, including host-only
Wizard fixes that keep runtime B683 unchanged.

Focused Python regressions cover 32 updates, retained banks after removal of
older APKs, one read per receipt, no payload access/hash/directory scan, invalid
selected APK, metadata budgets and bad ancestry. The actual Chrome regression
feeds the real Python recommendation from twelve closed updates into the shipped
UI, checks visible highlight/badge/prefill and ordinary missing/current/cyclic
cases, with a pre-fix failure witness. Unchanged runtime/art/native inventories
and prior checks are inherited, not presented as new headset/runtime tests.

Deliver the refreshed Windows/Linux Builder ZIP on feature/quest3-standalone.
No APK rebuild or installation is needed for this host-only repair. Extract the
new Builder and reopen it. The update highlight applies when its bundled runtime
is newer than the selected completed APK and local required inputs are present.

Evidence: `/home/claw/quest3-local/build/evidence/quest-home-hint-20261017/`.
Reap owned browser/test children before central completion cleanup. Preserve
compact evidence, current deliverables, captures and shared inputs; remove the
clean integrated worker and duplicate staged archive after handoff.
