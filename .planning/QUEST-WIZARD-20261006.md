# Quest wizard stage progress and offline presentation

## Windows receipt limit and automatic restart continuation

The later support capture `quest-build-support-20261006T175224Z-14c642bc.zip`
identifies the expected new release `964aa706a2573886860fe2677ee1ff227c791282`
and fails before launching its recovery child. The precise builder error is
`Recovery resume evidence is missing or oversized: core-recovery.json`.
Migration candidate selection already checked that this receipt existed; the
following generic reader capped it at 16 MiB. The capture does not include the
actual receipt size, so that number is not claimed as measured hardware evidence.
Only the large core-export inventory now receives a bounded 256 MiB reader.
Small ownership/input/binding metadata retains 16 MiB bounds. Error diagnostics
distinguish absent, non-regular, oversized and unreadable evidence and expose
observed byte counts and the relevant bound. Eligibility validation and the
child's independent core/batch/journal hash checks remain mandatory.

The capture also uses a fresh wizard session ID and all prerequisite stages have
one attempt. The former game-selection screen's ordinary Continue route planned
a new session unless the separate View previous build button was chosen. Browser
startup now opens the last readable owned session automatically, restoring its
choices and whole-stage progress without starting tools. Continue runs that same
ID. An explicit Set up a new build action opens fresh choices and preserves owner
state, caches and signing material. A failed status load cannot silently create a
new session. The restart preference is stored outside the release directory and
works across a changed loopback port or replacing the Builder source folder.
Missing/corrupt latest pointers fall back to a valid recent session; IDs must match
their owner directory and unusable metadata is not offered.

A default-source update is identified separately from the previous saved source.
It legitimately revalidates affected source/dependent stages, while compatible raw
exports remain eligible for reuse. Explicit advanced source selection is preserved.
Same-input stage progress remains durable; changed-input progress does not inherit
an unverified percentage from another recipe. The generic progress gate and
Unity observer are unchanged; no extra import or shader validation is introduced.

The support scan now follows the observed owner binding to the retained raw
folder. Before that binding exists, at most two recent folders are clearly labelled
as candidates. Five fixed receipt/identity/journal filenames have existence/size
observations only, without reading or exporting their proprietary inventories.
These diagnostics do not establish content integrity. Reader failures have a
specific bilingual explanation while the exact exception stays in bounded logs.

Focused large-inventory tests use a real 130,000-record receipt over 16 MiB, a
separate semantically valid large receipt through actual child hash verification
and journal replay, and rejection of unwitnessed/corrupt exported bytes. They do
not rerun either exporter. Restart selection tests establish readable pointer
fallback, fresh-store persistence, no startup state mutation and explicit older
session continuation. Actual Windows full-game success still requires a retest.

The final integrated focused gate passes 269 cases without skips: 136 wizard,
42 builder, 25 builder-startup, 18 release/support, 25 raw-export resume,
19 Node UI and four artwork tests. Eight direct Chrome DOM/loopback workflows
exercise restoration, unchanged choices, retained 28.6% stage total and five
completed prerequisites, no POST on startup, same-ID Continue across a changed
server port, status/result handling, explicit new selection and a visible/retryable
inaccessible saved session. The source-update explanation is limited to default
package source; advanced source pins remain authoritative.

Fresh Linux test Chrome profiles require `--password-store=basic` and
`--use-mock-keychain` so a host keyring cannot indefinitely prevent dispatching
loopback requests. Both test launchers use these flags only in temporary profiles;
the product browser is untouched. Bounded CDP command errors/connection-close
rejection retain failure evidence and allow owned test cleanup. No transport bridge
or production reload workaround was used.

The final extracted ZIP is independently compared with its committed helper/UI
bytes, exercised through isolated offline discovery and six exact image responses,
and reopened through two genuine local backend ports with the same failed owner
state. That witness creates no new session/build, keeps choices/progress/state
bytes intact and checks CLI discovery against the release commit. Compact audit
and private screenshots remain; clean finished worker checkouts and temporary
extractions are removed. This verifies delivered tooling, not a complete Windows
conversion or Quest rendering.

The maintainer's 2026-10-06 report requires a total percentage for every main
stage: finishing a substep must not finish the stage or reset its total. A later
clarification explicitly permits a second resetting substep bar alongside the
persistent stage total. The requested local-processing block and footer note
are removed; left-side character/enemy artwork is substantially larger.

## Progress meaning

`progress.stagePercent` describes observed scheduled work, not time remaining.
`progress.percent`, `done`, `total`, `phase` and `detail` retain raw substep data.
The versioned `progressPlan` is durable in the owner session, with explicit
operation boundaries and bounded Unity/Bee counter contributions. Progress
stays below 100 until the stage's independent output receipt is verified and
published. Checking/copying one file or completing a child command cannot finish
an unrelated parent operation. Same-input interruption retains attained progress;
changed inputs clear the affected plan. Existing sessions with only old raw
percentages do not inherit a false 100% total.

The Unity observer is enabled only by the wizard's private child environment.
It subscribes to public `UnityEditor.Progress` task events and samples existing
tasks, with rate limits and bounded retained IDs. It does not create/cancel tasks,
refresh assets, mutate gameplay or perform additional shader compilation.
Scene/Player/content-bank callbacks report actual host build activity. The host
also recognizes current log/Bee counters and import/Gradle/IL2CPP messages.
Unknown denominators retain current task text and the last measured stage total.
Foreign/future operation names in observed logs are ignored for the stage plan
and cannot fail the underlying command. Explicit internal operation APIs remain
strict so incorrect instrumentation is caught by focused checks.

Unity setup marks the editor operation complete only after actual Editor,
Android NDK, SDK/ADB and OpenJDK files are present. A login/window wait with
missing modules cannot imply completed installation. Fresh version/license
probes and retryable actions retain their existing ownership and nonce rules.

## Images and source selection

Six unmodified publicly published character/enemy images are included under
`tools/quest-wizard-ui/assets/promo/`, with source attribution and exact
hash/size/dimensions pins. Gallery construction validates local bytes immediately;
the browser uses the existing authenticated loopback endpoint. No network or
owned-game export is needed for the initial slideshow. The release inventory
requires these files and the new progress helper/Editor observer. Optional repair
retains the exact approved CDN sources and bounded diagnostics.

The default source is the Builder ZIP's immutable manifest commit. The project
version is 1.1.0 and runtime build is B627. The installed PC-game mod DLL is not
used as mod source; advanced source selection is explicit, with no silent fetch
of latest dev. Discovery describes the launch source and saved-session status
describes its resolved source, so replacing the launch ZIP does not mislabel an
older continued build. Small metadata reads use the protocol prefix because
its complete historical wire file exceeds 2 MB.

"Check mod files" establishes the selected source/tools/art inventory and
hashes for consistent working copies and safe cache identity. It does not run
the exhaustive shader gate. Retain `%USERPROFILE%\.ghvrq` and script-local Python
when updating the complete release to preserve completed setup and usable caches.

## Reported recovery pause and resumable update

The new Windows support export
`quest-build-support-20261006T171014Z-7c1c9fa2.zip` identifies source
`5bb804393db6b60652b663677b46799c0b3a3e65` / B627. Both core and batch-000
exporters finished post-export. The final measured event is collection merge
2761/2761, `Portrait_Jekserah.asset`, at 17:04:17.718 UTC; capture creation is
17:10:12.833 UTC. The current recovery log contains no failure. This establishes
355 seconds without activity reports, not a proven deadlock or broken portrait.
The next source operation is native evidence merging, followed by checkpoint
serialization and complete file verification.

Native merging now streams indexes, verifies each distinct retained/incoming
recipe once, retains unchanged copies and hashes new bytes during atomic copy.
Checkpoint serialization streams records instead of allocating an additional
complete formatted JSON string. Collection lookup uses an indexed GUID map.
Index/file/byte/record counters and phase durations expose these completion
operations; full journal rollback and checkpoint hash verification remain.
Only the complete committed-batch counter advances the recovery fraction of
the whole build stage. Finishing one collection or recipe never closes it.

The opt-in recovery scope captures actual Python stacks after 120 seconds
without progress, at most three times. It does not terminate conversion or
infer failure from elapsed time. The observer stops when the scope exits.
Atomic temporary files survive an OS force-kill in the same way as the earlier
JSON writer; they are never accepted as completed receipt outputs. No broad
unowned-file cleanup is introduced.

An audited raw-export migration keeps the earlier absolute recovery workspace
when the owned-game inventory, exporter instrumentation/configuration and
known prior post-export recipe identities agree. The original source, core,
batch, exporter and pending journal are independently checked by the recovery
process before replay. Derived Android outputs keep the complete new recipe
key and are rebuilt when needed. Unknown export contracts are not adopted.
Replacing the default release changes its source identity while retaining tool
setup; a resumed build uses the updated source rather than its old bugged copy.

## Evidence and limits

- Final integrated Python checks: 129 wizard, 42 builder, 15 release/support,
  64 installer, 16 raw-export resume and four quiet-diagnostic tests passed.
  The focused recovery suite ran 132 cases with two optional skips. Together
  with the 14 UI cases this is 416 tests, 414 passed and two skipped.
- All 14 Node UI tests passed, including four real Chrome/loopback workflows,
  with no browser skips. Witnesses cover raw 100-to-5 resets while stage totals
  continue, unknown Unity counters, wait/retry/failure logs, consent, keyboard,
  bilingual strings, slideshow races and mobile layout.
- Real interrupted-batch fixtures restore the actual merge journal, verify
  retained core and bundle files, and reach the new derived Android stage
  without repeating either exporter. Quiet subprocess fixtures capture an
  actual blocked call stack, remain successful, stop their observer on scope
  exit and tolerate an unavailable diagnostic thread.
- The actual installed Unity 2021.3.5f1 Editor compiled the observer and emitted
  real public task counters 3/10 and 4/10. The real log parser accepted both;
  this was an import/observer smoke, not a Player or APK build.
- Isolated default HTTP startup preserves stdlib module aliases, reports the
  source build and serves all six bundled PNG/JPEG responses with exact hashes.
- Before publication, the final source ZIP is extracted and checked through
  that same isolated startup and a genuine CLI discovery. Its adjacent audit
  records the immutable commit and archive hash. Temporary extracted copies
  and completed clean worker worktrees are removed; compact receipts remain.

Private browser screenshots and the compact Editor receipt/log are retained
under `.planning/debug/quest-wizard-refresh/`. The release and its audit are in
the main checkout's `.planning/debug/quest3/`. No game-runtime change or new
ModBuild is introduced. Actual Windows whole-game throughput and Quest rendering
remain hardware/build test outcomes, not conclusions from these UI checks.
