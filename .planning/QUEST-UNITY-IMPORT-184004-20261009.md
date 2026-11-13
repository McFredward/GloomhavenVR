# Quest first Unity import — capture184004

This is a Builder observer/hierarchy repair on `feature/quest3-standalone`.
It adds no fresh local APK or full Unity import. Runtime sources follow the
published dev integration; this change does not alter original asset producers.

## Actual running evidence

`quest-build-support-20261009T184004Z-21194525.zip` identifies source
`8063c8fc84f86c3354699d53949450e89a3606f5`, Runtime657, and a running Windows
build. All27 preparation owners are recorded closed, without a pending producer.
Recovery qualification takes44.7s, retained preparation qualification26.2s and
managed compilation/weaving63.9s. Unity starts about181s after this build begins.

The child `package-import-ffb3cf92a42f.log` explicitly rebuilds Library because
the asset database does not yet exist. Earlier capture165405's memory admission
refused to launch this Editor, so this is the first genuine import of this
workspace, not evidence that a prior completed Library was discarded. At export,
Unity has been running60m10s and continues texture imports without a reported
failure. The17.8MB source log is truncated to a bounded head/tail in support;
its7,991 exported completion rows are not the complete live import counter.

## Correct owners and measured observation

- The initial full Editor/SDK import now opens `unity-import`, before
  `package-api`. The latter belongs under Unity Import and is named
  “Unity-API anbinden”, not preparation. SDK evidence must qualify the actual
  Android IL2CPP compilation before the import closes or API binding begins.
- The supervisor now tails the previously omitted `package-import` and
  `package-api` children. Native artifact/duration records count actual completed
  import operations, including legitimate reimports and warning-interleaved
  records. Bare starts and refreshes report activity without claiming completion.
  Counts, current action and elapsed observation are visible in the active view
  and expanded overview. Counts survive intervening activity/BUSY messages.
- Initial AssetDatabase import has no trustworthy native total before our
  Editor code compiles. No source-file census, historical time percentage or
  repeating0/1 task plan is substituted. Known Editor/Shader/Bee task counters
  still move the existing aggregate bar. BUSY events retain measured counts and
  report the actual active action instead of inventing completed work.
- API binding reports three actual assembly passes: validate/bind, write/hash,
  publish. Native content packaging reports four actual byte passes: original
  archive hash, native file hash, archive write/reuse, final archive hash.
  Their weighted fractions move the same parent/global bar; child completion
  never closes the whole operation. Parent closure remains source-owned.
- Unity buffers the content packer's stdout. Its one-line receipt remains
  unchanged; live byte counters use a bounded, explicitly owned sidecar tailed
  separately. Exact old/new fixtures produce identical ZIP and manifest bytes.
  Missing or unsafe optional observer paths do not break content production.
- Late closed child logs and archived memory attempts cannot reopen owners or
  replace current work. Native final Player startup resumes under validation,
  without reopening the already completed first import. Existing unfinished
  API-labelled states migrate their heading without clearing saved high-water
  progress or inventing completed import evidence.

## Retention and storage

Exact reviewed Builder AST and content-packer source aliases retain the same
preparation producer scope. Unknown producer, template, original game/profile
and orchestration edits still differ. The recovery recipe is unchanged. The
real current input still owns weaving, package binding and final delivery.
No Editor template or imported asset content changes for these observers.

The full-game workspace key already depends on original game, target, pinned
Editor and pre-import graphics settings rather than each mod release. Project
data lives in `<workspace>/build/projects/<workspace-key>`, with imported
artifacts, package/script/Shader caches and Bee native outputs in its `Library`.
The default Wizard workspace is `%USERPROFILE%/.ghvrq` on Windows and
`~/.ghvrq` on Linux; the user may choose another location. The current support
report redacts the selected absolute root, so it does not prove its spelling.

This Windows run uses the external Editor and Android tools at
`C:/Program Files/Unity/Hub/Editor/2021.3.5f1`. Shared caches can also reside
outside the workspace: pinned2021.3 UPM defaults to
`%LOCALAPPDATA%/Unity/cache` and Gradle to `%USERPROFILE%/.gradle`, subject to
configured overrides. These defaults are documented by
[Unity2021.3](https://docs.unity3d.com/2021.3/Documentation/Manual/upm-cache.html)
and [Gradle](https://docs.gradle.org/current/userguide/directory_layout.html);
they are not measured current disk consumers in this support report. This
repair neither moves live caches nor deletes global/shared directories.

The retained Library is reused by subsequent SDK invocations. The SDK Editor
invocation itself still runs; there is no newly claimed skip/cache for that
entire launch. Successful historical warm invocations of the same project take
approximately41–71s. The historical cold InitialRefresh takes95m54s, with a
later49m25s refresh caused by an old Android settings change; that invocation
eventually crashes, so it is not a successful APK total-time reference. A rough
60% time comparison for the user's current hour is explicitly an informal
estimate, never a measured denominator or a Wizard progress algorithm.

## Validation and acceptance

Root integration passes90 focused Builder cases (admission, content packing,
host scheduling,27-step retention, identity and package binding),24 import
observer/hierarchy cases,48 C# package-API assertions and five new Node/Chrome
cases. Worker receipts retain adjacent stage/retry/graphics/Shader/memory tests;
their counts overlap the integration cases and are not added as unique tests.
Actual child-process observations are visible before exit. The old observer and
planner fail the new regression controls. The live child fixture now emits the
same real operation boundary as the integrated Builder, rather than assuming an
unopened owner may change the active workflow.

Actual previous/new delivered inventories and Builder bytes are qualified at
the capture's full27-owner frontier, with synthetic local game/profile/journal
fixtures and changed-input controls. The extracted Linux launcher/API/session
test verifies the actual source archive. Detailed receipts live under the
private `B657-unity-progress-20261009` evidence directory. No new whole Windows
APK, headset outcome, complete wire gate or Shader matrix is claimed. Keep the
user's current import running; use the new archive at the next Wizard start.
