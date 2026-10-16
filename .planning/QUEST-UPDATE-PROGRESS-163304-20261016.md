# Quest compact update: repeated imports, measured progress and ADB lifetime

## Observed report

Support163304 identifies Builder0adeb3fa7, runtimeB673 and cancelled Mod Update
session389a8faa42bf4e469971f337352765e2. The report was exported from the home
view (workspace scope), so the current run is in workspace.runs and its bounded
run logs rather than top-level session/stages fields. Historical failures in
other runs do not describe this attempt.

The current build runs2265.29seconds before cancellation. It does not launch a
new original asset recovery. Ownership qualification, current source capture,
managed weaving, standalone adaptation and actual Android SDK/API binding all
complete. The code-build Editor then repeatedly updates script import settings:
141API-Updater completions,107GH.Runtime imports and143asset refresh summaries
appear in the supplied native log. An ordinary refresh takes roughly15seconds.

The compact helper unconditionally called MonoImporter.SetExecutionOrder for
every retained original script row, including already matching values. Original
plugins contain many MonoScripts, so this repeatedly imports the same DLL and
restarts the API-Updater. The normal full-project helper already skips equal
values. The caller has not yet reached the actual BuildPipeline.BuildPlayer
call in this observed helper flow; the long delay is not measured IL2CPP cost.

The Wizard's native-log parser correctly sees activity, but Supervisor filters
operation against the ordinary build plan only. All six update operation names
are removed, so real input/source completions and current code work cannot
advance the selected update overview. Repeated native refresh logs update the
visible timestamp despite containing no finite task completion denominator.
This explains the simultaneous pending0/X overview, unknown percentage and
roughly15second repeated activity observations.

## Repair boundaries

Restore only genuinely differing execution orders, batch their import writes,
and verify the exact retained orders after the batch. Check the entire required
roster before modifying it. Keep original script identity/layout checks and the
matched IL2CPP/metadata publication gate; do not suppress the API-Updater globally
or mark incompatible scripts successful. The finite catalog, roster and order
counts are production observations, not percentages advanced by a timer.

Retain valid operations from the selected update plan. Give SDK preparation,
package binding, script preparation and actual native build separate measured
scope beneath update-code. Closing a subtask does not complete the parent native
pair. Ordinary refresh/heartbeat activity must not claim a new completed task.
Keep the current task visible through a legitimately long import or compiler
job, and preserve owned completed results across cancellation.

ADB clients can start a background daemon which outlives an ordinary CLI call.
Wizard-created servers need an owned lifetime independent of a successful
Python finally block on Windows console closure. Existing external ADB/SideQuest
servers remain borrowed and are not killed. Keep USB preference, saved wireless
addresses, app data and installation journals; no headset adbd restart is needed
for shutting down a host-side server. The implementation and measured lifecycle
fixtures are recorded in the round evidence.

## Validation scope

This repair ships a game-free Builder and keeps runtimeB673. Use focused
production-helper/Unity import, captured-log progress replay, actual-browser
and owned-process-lifetime fixtures. Do not build a local game APK or repeat the
unrelated multiplayer gate. The ignored evidence root is
/home/claw/quest3-local/build/evidence/B673-update-progress-163304-20261016/.
A passing helper/import or fake-daemon fixture is not a new Windows end-to-end
APK/installation result or a Quest picture/tracking result.

The real Unity2021.3.5f1 fixture executes the production preparation method on
128synthetic MonoScripts in one DLL and131retained rows. Three actual order
repairs produce one import (0.674seconds); unchanged repetition produces zero
imports (0.00335seconds), with identical metadata bytes/mtime and original roots.
The legacy setter negative control imports that DLL three times for three
already-matching values. Missing, conflicting and null roster controls fail
before metadata mutation. The fixture counts69progress events; six complete
phase sequences are the first three phases exercised twice, not all five phases
or a native compiler run. Its full Editor invocation takes7.61seconds and has no
C#compile warning/error. This does not predict the user's complete update time.

ADB validation covers120installer/lifetime/binary/wireless and87Wizard/device
cases. A real pinned SDK30.0.4 ADB foreground server starts, performs read-only
version/devices queries and is reaped in0.28seconds. Synthetic daemon fixtures
also exercise abrupt owner death and executable-version replacement. Windows
job API calls are checked with a controlled API fixture, not a Windows runtime.
The new support-log tests include only the newest two guard logs from home and
run views, redact credentials, and refuse linked log files.

An already-running global daemon left by an older Builder is indistinguishable
from an external server and is not killed automatically. Close that old daemon
once before testing the new owned lifetime. New private servers remain bound
to the current Wizard, including abrupt console closure; original wireless
configuration and headset-side adbd remain unchanged.

The existing raw capture replay now retains2of6completed update operations,
selects update-code, closes4of6compiler host subtasks and computes24.231631%
whole-step progress. These are reconstructed work shares from observed logs,
not a retroactive time estimate. Actual Chrome fixtures exercise counted
roster progress, completed rows and the immediate native handover display.
Seven compact regressions additionally separate late SDK tails from the next
native invocation; no local SDK completion can close that native build.

Primary integration passes142focused progress/observer/plan cases,27update
handoff cases,87Wizard/device cases,61compact APK cases and10support-log cases.
Worker evidence includes148focused progress Python cases and27current UI cases
with actual Chrome (20earlier UI cases are explicitly inherited). Counts overlap
and do not constitute a new unrelated complete suite. The primary integration
also updates the existing progress-record schema assertion for the two explicit
activity/progress timestamps. The worker corrected an existing shader assertion
that incorrectly treated a single100%local pass as completing the whole shader
phase; its old failure was reproduced unchanged at baseline.

Final archive/source identities and extracted-delivery checks are recorded in
the game-free handoff receipt after integration. Actual Windows console cleanup,
complete compact IL2CPP publication and the next Quest run remain hardware gates.
