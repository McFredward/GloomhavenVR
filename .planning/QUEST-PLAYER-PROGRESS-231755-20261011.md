# Quest Player progress scopes (2026-10-11)

## Captured running build

`quest-build-support-20261010T231755Z-b442a2ac.zip` identifies Builder
`b1cd188611167112feee42d854e31d9ebc86a987`, Runtime665. The export is a
running snapshot, not a completed APK or a new failure report. Its preparation
receipt closes all27 owners and has no pending transaction. The native
Addressables bank and its repaired native Shader audit finish successfully;
the run advances into the Android Player build.

The retained Player overview shows the scene counter14/14 as pending99%, a
never-started duplicate configuration part, an unobserved IL2CPP part, a native
part at99%, and the last Shader pass6/6 at100% while its parent remains running.
The current Bee counter is27/10069. Immediately preceding observed graph work
reaches9056/9233; these are different graph generations, not a restarted whole
export. The raw log records `IL2CPP_CodeGen` inside the Player graph. The old
parser misses this annotation and assigns every Bee counter to the native part.
Shader pass maxima also contribute directly to that same part. This explains
both the early99% plateau and the apparent conversion-step omission.

The archive keeps bounded log heads/tails: the selected Unity log is479,925,368
source bytes, of which2,090,084 redacted bytes are exported. Its native graph JSON and structured
backend progress files are not included. Capture replay can prove the actual
raw annotations and the displayed bad state; it cannot reconstruct every graph
node, original compilation duration or the eventual result of the running run.

## Phase and preservation contracts

A local completed Shader pass is distinct from all Player Shader work. The
low-order scene callback counts entry into each scene, not completion of the
last scene's native serialization. Its final14/14 event alone must not close
that logical phase. The actual platform PlayerBuildProgram handoff provides a
later, source-proven phase boundary. Configuration belongs to the already
completed validation invocation, not to a second Player preparation task.

IL2CPP conversion and native compilation are tasks within the same Bee work
graph. The UI must represent that relationship and give local counters their
own scope. A preliminary copying/code-generation graph cannot supply a finished
native-compilation percentage. Whole-operation completion still belongs to the
existing successful native/caller and final publication receipts.

This repair changes progress observation and display. Original asset producers,
Editor consumers, preparation owners and Unity Library are preserved. No fresh
game import, forced Shader compilation, Library deletion or local APK export is
part of this round. Runtime665 and the published dev665 gate evidence are
inherited unchanged; the maintainer continues the Windows build.

## Verification

The final delivery receipt records focused parser, hierarchy and browser tests,
pinned Unity call-order evidence and actual delivered-source continuation
fixtures. Native graph/progress artifacts from retained local pinned Unity work
and explicit graph fixtures supplement the raw Windows capture; neither is
claimed as the absent Windows graph files. A running capture establishes no
whole Windows APK or headset acceptance.

Integrated verification covers285 distinct Wizard cases, including the actual
Supervisor log/graph-to-Store composition and pinned Player dispatch. The first
integrated run passes282 and exposes three historical expectations for rounded
percentages and unscoped log annotations. The affected87-case rerun passes86;
after correcting the last next-log annotation expectation, all five native-memory
cases pass. Production code is unchanged across these fixture-only follow-ups;
this is composed focused evidence, not a claimed new complete-gate run. All40
affected Node/UI cases pass, including four actual Chrome replays. The89 existing
preparation/recovery cases pass on unchanged original producers and consumers.
The release receipt supplements this with ten actual preceding release-source
continuation fixtures, each with17 changed-input negative controls, and an actual
extracted Linux launcher restart. Complete Windows export and hardware acceptance
remain pending.

## Implemented observer and display

The Player method now shows scene preparation, Shader preparation, a combined
code-conversion/native dependency phase, then packaging. IL2CPP is a visible
child of that dependency phase, rather than an independent earlier step that
the actual producer can skip. The duplicated configuration row is removed;
the original validation invocation already owns configuration. Observed open
parts remain running, while authoritative completed/reused children stay green
and at100 even if a later sibling fails.

Scene callbacks expose started scenes. Their finite logical phase reserves one
additional platform handoff, so14/14 entries account for14/15 tasks and cannot
finish native serialization. The pinned2021.3.5f1 Android postprocessor actually
dispatches through the default Bee driver; its PlayerBuildProgram start closes
the preceding scene and Shader scopes. Pinned assembly IL and production scene
callback fixtures test this boundary without rerunning an import or Player build.

The read-only native observer reads the existing Player target's dependency
closure, excluding unrelated nodes. It includes cache results counted by the
backend and reserves one task for its successful return. Copying, lump creation
and registration-only preliminary graphs cannot claim the later IL2CPP C++
compilation denominator. Actual CodeGen results close their own child; backend
exit4 requires another graph and does not complete compilation. Fresh profiler
epochs and structured-log initialization prevent yesterday's retained counters
from being replayed. Bounded optional telemetry failures fall back to the real
printed C++ queue and never reject a build or reset Library.

Shader coverage reuses the existing asset census, deduplicating declared names
from bounded2KiB source headers. That explicitly named coverage owns80% of the
Shader phase; remaining passes and serialization belong to its actual platform
handoff. It is not presented as a census of every runtime compiler variant.
Local READY counters reserve their real compiler-pass return, and FINISHED
closes only that pass. The active bar then describes the remaining whole Shader
phase instead of leaving the completed pass at100 while more work continues.
The first ongoing pass of a still-unobserved known Shader source contributes
its measured variants fraction within that one source's coverage share;
additional passes of the same source cannot inflate the unique-source census.
Completed native and Gradle scopes similarly leave the active bar for packaging
or final result evidence; their completed overview cards retain100. A running
Unity N/N counter without its scope-return proof reserves that real return,
instead of treating a local counter as a completed outer operation.

Old revision6 Player scopes migrate only their inaccurate progress ownership.
The retained whole-build high-water is anchored to the remaining measured work,
so newly completed tasks advance it immediately instead of restarting at zero
or waiting for the corrected local curve to catch up. Accurate ratios remain
unrounded in storage; displayed unfinished percentages cannot round up to100.
No original producer, Editor consumer or source-compatibility pin changes.
