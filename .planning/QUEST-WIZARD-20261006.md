# Quest wizard stage progress and offline presentation

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

## Evidence and limits

- Integrated Python checks: 127 wizard, 42 builder, 15 release/support and
  64 installer tests passed.
- All 14 Node UI tests passed, including four real Chrome/loopback workflows,
  with no browser skips. Witnesses cover raw 100-to-5 resets while stage totals
  continue, unknown Unity counters, wait/retry/failure logs, consent, keyboard,
  bilingual strings, slideshow races and mobile layout.
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
