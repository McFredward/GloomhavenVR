# Quest wizard stage progress and offline presentation

## Completed export followed by cold staging-helper failure

The consecutive captures `quest-build-support-20261006T212710Z-ca831866.zip`
and `quest-build-support-20261006T213551Z-be492738.zip` identify the same shipped
Builder source `5dce06841`, ModBuild 627. The first still records an active
reference audit; the second proves that all 16 packages, the reference audit,
source guard, 204,339,293-byte final checkpoint and CAB index completed. The
reference audit alone lasted 933.023 seconds without a measured counter.
The failure was afterwards in `builder.owned_tmp_source_archive`: dynamically
executing `tmp_shaders.py` did not provide its sibling `recover` import on a
fresh Windows child process. No asset-export or Unity failure is established by
this capture.

The builder now uses the existing shared recovery-module loader for official
TMP sources and imports the staging helpers before starting original conversion.
A fresh isolated CLI regression intercepts only the network acquisition and
proves that the real helper imports reach the exact pinned archive request. A
missing helper fails before an export child can start. Existing completed raw
exports keep their absolute workspace, including the retained reference audit;
the exact shipped d4cc whole orchestration profile is also recognized. Unknown
profiles and changed raw bytes remain explicit failures.

The reference audit enumerates and opens assets once, reports actual file counts,
and emits large-file byte counters. Streaming YAML events replace full node
graphs for ordinary PPtr mappings; uncommon structures retain the previous
parser. Exact token spans, traversal order, GUID definitions and exclusions of
scalar text are retained. The fresh caller does not overwrite the auditor's
real total with a one-audit counter. The audit fixture has 2,040 files and equal
old/new output; its Linux median falls from 5.430 to 2.955 seconds. This is not a
full Windows timing prediction.

Project staging is a separately measured eighth conversion section, after the
seven raw-recovery sections. The 16-package schedule therefore has 23 tasks.
Fourteen actual staging operations have explicit boundaries; copying and final
output inventory contribute their measured file/byte counts to the whole build
bar. Progress migration preserves saved high-water. Estimates remain scoped to
observed throughput, never invented for unobserved future native work.

Staging copy/report writer proofs remove redundant fresh-byte reads; mutated
outputs are hashed after their last edit. An intermediate native reference audit
is deferred only when full staging owns the mandatory final audit. Direct
native-stage callers retain their original check, and a later duplicate GUID
still blocks a full-stage success report. Audio/Cubemap/platform-image receipts
reuse one actual hash per immutable container within an invocation; source
changes still fail. Retained source targets and pinned public package inventory
no longer repeat an already accepted digest. The complete seven-stage/21-operation
review is in `QUEST-BUILD-PERFORMANCE-20261006.md`, with measured versus unobserved
work and further cache/parallelism proposals identified explicitly.

A terminal build failure now has a prominent top-of-page alert with its stage,
bounded concrete cause, retry and diagnostic-export actions. Valid status is
rendered before fetching optional events, so a log request failure cannot retain
an obsolete running display. Stale Unity waits, animation and cancel controls
cannot conceal a confirmed failure. Active builds display the age of the last
actual progress report; silence explicitly means activity is unconfirmed rather
than falsely promising a running tool or advancing percentages with time.

## Actual conversion counts, scoped estimates and repeated native work

The supplied `quest-build-support-20261006T201048Z-1ddc1ec1.zip` identifies the
published Builder source `5dce06841e30d1befbe9761223e6e188a6b81adc`, ModBuild 627.
Its bounded logs show distinct packages progressing through package 5 of 16,
not a proved deadlock. Three truncated support-log lines are explicitly excluded
from replay. The old equally weighted build schedule gave all conversion just
1/21 of the bar and all packages just 1/7 of that span. Consequently the native
recipe work could remain at the same displayed 29.97 despite actual activity.

The producer now announces its real catalog package count before core export.
Conversion reserves 40 percent of the complete build operation, later Unity
import 18 percent; these are work spans, not elapsed-time estimates. Each package
is one conversion task alongside six other sections: this capture's schedule has
22 tasks. `progress.activeWork` names and counts the currently running operation,
rather than seven top-level Wizard stages. Actual exporter `ExportProgress`
collection counters are read while its synchronous request runs. Their upstream
denominator includes collections skipped by bundle-only export; successful
request return and committing the package remain independent boundaries.
Existing stored high-water is anchored once to the remaining measured work when
changing the distribution, so updating cannot reset or stall the bar's curve.

Labels include the active package and native index number, and incoming/retained
index activity is explicit. Native indexes share invocation-local parsed state
between journal preparation and merge; identical indexes are not rewritten.
Each actual recipe still qualifies its bytes while duplicate ancestor walks are
removed. The catalog uses current source-inventory hashes instead of rereading
all original bundles again. Exact known previous orchestration profiles,
including the actual shipped 5dce profile and its old log observer, remain eligible
for raw-export reuse. The exporter binary, C# capture and identity format are
unchanged. Cold/changed/corrupt/link evidence still invalidates reuse explicitly.

`row.timing` and `state.timing` report observed monotonic active time without
advancing work percentages or writing on status polls. Waiting, cancellation,
offline intervals and unobserved old runtime are excluded; old sessions identify
"since update" measurements. Counter throughput requires at least three
observations spanning ten seconds; package estimates require two completed fresh
packages with the same schedule. Retained packages do not train fresh duration.
ETA is explicitly a range for the current phase or remaining data packages;
later unobserved Unity/Android work has no fabricated whole-build estimate.
Estimate/timing details join the existing bounded progress log.

GitHub and Buy Me a Coffee links use exact README destinations and locally pinned
brand symbols in the footer. The requested hero sentence and visible picture
source link are removed; provenance remains in developer asset documentation.
Nine further unchanged publisher pictures expand the offline gallery to 21.

### Qualification and limits

The final integrated tooling tree at `0b4a20341` passes 485 cases: 483 pass and
two existing optional recovery binding cases skip. It includes 177 Wizard cases,
149 recovery cases, 34 raw-resume cases and all 21 Node UI cases, with eight real
Chrome workflows and no UI skips. An initial startup check incorrectly tried to
serve the newly bundled icon license as an image. The test now qualifies its
local bytes while retaining the existing narrow static MIME endpoint; the final
complete focused gate passes. Browser screenshots cover desktop/mobile icons,
current-operation tasks, native index context and scoped timing presentation.

The exact received exporter log yields 6,352 observed collections out of the
14,940 upstream exportable upper bound. Offline replay of 2,669 captured package
progress events through the actual integrated work planner is monotone, includes
all five witnessed package boundaries and never claims whole-stage completion.
A separate actual captured timing replay excludes retained package 1 and trains
on fresh packages 2–4 (356.368, 400.749 and 448.403 seconds). Its last packet-5
estimate is about 77 minutes of remaining data-package work, with a wide 54–122
minute band; this is evidence of the method, not verified prediction accuracy.

The actual small 2,000-recipe Linux fixture compares shipped 5dce code to the new
implementation: hot native merge median 2.062 to 0.791 seconds, ancestor
qualifications 14,008 to 4,012, unchanged index writes one to zero. This does not
predict the complete Windows runtime or remaining exporter cost. Parallel full
exporter processes are not added: they reload the original core and would
multiply memory use before an actual memory/ownership scheduler is qualified.
No host APK/Player build, fresh full game export/import, exhaustive shader or
unrelated runtime/wire gate runs. Full Windows completion, ETA accuracy and the
headset outcome remain maintainer tests. Preserve the active build and owner
workspace; use the new source ZIP for a subsequent run or compatible continuation.
Compact evidence lives outside worker checkouts under
`quest3-local/build/evidence/B627-wizard-native-timing-final`,
`B627-recovery-speed-2ac40e85c` and `B627-wizard-timing`.

## Measured substeps, branding and minimum checkpoint work

The maintainer's later report asks that measured substeps continuously contribute
to the whole-stage bar, replaces the hero heading with the GloomhavenVR/Meta Quest
logos, removes its three badges and adds publisher slideshow pictures. The
version-two durable work schedule contains bounded recovery section, batch,
collection/file/byte, native-index and checkpoint spans. Large-file counters
advance only their current parent item. Six-decimal stored CSS/ARIA values retain
small measured changes; localized text uses up to two decimal places. Older saved
plans retain their high-water mark; independent output publication alone yields
100%. Unknown tool work never gains a fabricated time-based percentage.

Both logos ship locally; the Quest SVG preserves its original vector paths and
has exact provenance. Twelve exact public publisher images work offline before
game selection, including six additional monster reveals. The previous Archer
pin was an elite Guard and now uses the actual Archer reveal. Active source links
follow the pictured publisher announcement. Desktop and narrow layouts are
visually inspected. Artwork notes and hashes live beside the assets.

The maintainer then observes `Zwischenstand prüfen` two/three times and cancels
without a support export. Source inspection proves duplicate work: every normal
batch performed two full scans of the growing merged output, and cold journal
repair/checkpoint adoption could overlap more scans. This does not establish the
precise cause of the uncaptured Windows sequence or prove an infinite loop.

Normal batches now have no growing-output verification sweep. A new recovery
child qualifies the actual retained output once; current writer/copy hashes and
invocation-local file proofs qualify unchanged native recipes without repeated
byte reads. Proofs are not serialized and file-write stamps invalidate them.
Atomic checkpoint publication cleans its journal directly; interrupted cold
repair checks unchanged files once plus restored mutable indexes. A second merge
cannot overwrite an unfinished journal. Missing/changed evidence fails visibly
instead of silently restarting exporters. Retained batch exports skip original
staging/copy/export, completed reference audits remain reusable, and an unused
CoreExport is not reread when a matching merged checkpoint is used. Catalog-plan
hashes are reused for final indexing; tool-file hashes are read once when needed.

Saved-state adoption, interrupted-write repair, restored-index adoption and
retained-export qualification have separate bilingual labels. Active substeps
include their actual batch index/total; aggregate batch closure never displays
the next batch as already started. Existing reviewed exporter/orchestration
profiles remain eligible for raw workspace continuation; all other source/tool/
instrumentation witnesses must match. No original game-file or owner-workspace
cleanup is introduced.

Qualification uses real small multi-batch identity/merge/journal fixtures,
isolated HTTP startup serving all publisher/logo bytes, direct Chrome workflows
and an extracted source-release audit. No host Player/APK, new Unity import,
exhaustive shader validation or unrelated runtime/wire gate is required by this
tooling-only change. Full Windows throughput and the uncaptured sequence still
require the maintainer's build. The latest compact receipts are recorded under
`/home/claw/quest3-local/build/evidence/B627-wizard-minimal-*`.

Final focused gate at `4afd37dfef346924760020b27394227722053aa8` runs 435
cases: 143 wizard, 42 builder, 25 builder-startup, 18 release/support, 30
raw-resume, 15 builder-progress, 139 recovery, four artwork and 19 Node/UI.
433 pass and the two pre-existing optional recovery binding cases are skipped.
All eight direct Chrome/loopback workflows run, including twelve offline images,
both logos and `70 → 70.0001 → 70.01` bar updates with distinct batch labels.
The release notes added afterward do not change the qualified tooling sources.

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
