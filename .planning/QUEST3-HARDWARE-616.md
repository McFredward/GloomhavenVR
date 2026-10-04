# Quest native type-lookup abort — ModBuild 616

This candidate follows `quest-capture-20261004T084759Z-44ff3af2.zip`.
The maintainer accepts the existing logo/progress presentation, then reports
an application crash when loading finishes. Quest work stays solely on
`feature/quest3-standalone`; concurrent `dev` changes remain untouched.

## Verified evidence

The capture identifies installed B615 APK SHA256
`e5b63478557e0141cb35d653f8d6fc1de76dd766ff3071dd6617d24242baf23b`,
input `44c066b3fa76116058f55e7c3e873d98366471736c4631eba71bf4c8ebc19bcf`.
The current startup log and native Android crash history agree on this run.
Historical B609/B611 probe files and the previous B614 log describe older runs.
No new screenshots accompany this capture.

All eleven real-mod modules complete. The last module observation has not yet
confirmed a running rig. Android records a UnityMain SIGABRT at 08:47:42 UTC,
with IL2CPP library BuildId `9149e2d9252dcbe6`. Its stack reaches
`PerfMonitor.Sample`, `PerfFrameSplit.RollFrame`, `PerfNativeLoopProbe.Install`,
`HarmonyLib.AccessTools.TypeByName`, and a predicate reading `Type.FullName`.
The APK library is byte-identical to the retained 433,468,896-byte native
output, whose full digest is retained in the private audit receipt, and carries that
same BuildId. Retained symbols/disassembly identify the actual
abort helper at `Assert.cpp:13`; Android's adjacent `CpuInfo::Create` label is
misleading. The caller is `Class.cpp:1987`, the GC reference-field alignment
assertion `0 == (offset % sizeof(void*))`. The exact offending type/final field
offset is not retained in the supplied crash record.
This is an observed native abort; it is not the prior YML worker exception,
an original loading-error dialog, or evidence of a video decoder failure.
No original Bootstrap/Intro load has been observed in this run.

The old facade enumerates every runtime type to resolve a requested name.
Reading unrelated names can initialize unrelated IL2CPP metadata, even when
the target exists. A managed catch cannot recover from native abort.
The periodic state file still says `checking-mod-content`, although module
installation has subsequently completed. The log/crash history establishes
the later boundary; the older JSON snapshot does not establish a delivery hang.

## Changes and limits

Quest-only Harmony compatibility resolves full/global names directly in loaded
assemblies. Build-generated metadata aliases retain short-name fallback without
runtime enumeration of unrelated types. The alias input follows the current
managed inputs, avoiding a hand-maintained list as the mod develops.
The desktop Harmony implementation remains unchanged. Debug timing probes are
retained rather than disabling useful hardware measurements.

Startup snapshots persist the plugin, Addressables and original-scene boundaries
immediately. Changed real-module observations persist their measured lifecycle
state before relying on the five-second periodic writer. These are bounded
main-thread records and add no worker Unity API calls or per-frame disk writes.
The accepted logo, progress geometry and existing real VR controls are preserved.
Original rules, saves, video callbacks and network behavior are unchanged.

The source removes the exact observed broad-search path. Automated tests cannot
establish that the subsequent Android scene initialization will succeed. Campaign,
procedural runtime, native voice and authenticated EOS crossplay remain gated.
No store/Horizon/cloud services, Workshop or Guildmaster are enabled.

## Hardware procedure

Use the complete B616 Windows test archive and `Install-Quest.cmd`. Keep existing
wireless settings; updating the same signed package retains app data. Do not
uninstall or clear data to work around this crash. Existing verified movie/mod
files can be reused, but a warm launch still checks their hashes.

Observe whether loading reaches the real hands/controllers, Intro, native loading
view and main menu. If the menu becomes reachable, try native buttons, VR Options
and Quest passthrough. Report any binocular double loading symbol separately;
a flat screenshot cannot settle that rendering question. Do not start a campaign
in this startup slice.

Run `Collect-Quest-Logs.cmd` after a crash or while a stopped view remains open.
The collector already captures historical app-specific crash logcat even when
the process has exited; the supplied B615 record demonstrates that this works.
Supply its ZIP and any screenshots under `.planning/debug/quest3_probleme/`.

## Validation and native handoff

Relevant startup, lookup/weaver and builder checks are scoped to changed Quest
boundaries. Strict Release and source checks remain separate from actual SDK,
native Android and signed-artifact checks. Direct unchanged wire/golden vectors
verify protocol compatibility without rerunning unrelated gameplay suites.
The final artifact, input/source identity and focused-check receipts are recorded
here after the native build completes. No headset success or complete unrelated
local gate is claimed.
