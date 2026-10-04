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
The signed ARM64 IL2CPP Debug/O0 build succeeds from clean source
`36d3f2cbf33d2f77368042622065682b11df16f5`, input
`70d3fc52d29f344dd79482ec08c90eff4119f164b3e18b05e51de8cf39170b68`.
APK size is 1,616,497,711 bytes, SHA256
`14d421d118150969649a80ab47a3a0c902b4ddef412136a4dc1fb1bec687f9e7`.
Package `dev.gloomhavenvr.quest` and signing certificate
`1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012`
retain installation identity. The profile remains explicitly DUMMY.

Five relevant Quest suites pass: builder 79 tests, weaver 290 assertions
(including 36 generated-facade lookup assertions), platform 3,057 assertions
plus 11 controls, startup persistence 39 assertions plus nine controls, and
startup delivery 164 assertions plus 12 controls. The first delivery-fixture
compile exceeded its 90-second limit under concurrent host load; its separate
serial rerun and compiled production execution pass. That mixed initial receipt
is retained as failed, and only its independently passing logging suite is
reused. All 14 source checks pass on the final source. Strict Release has zero
warnings/errors; direct unchanged wire/golden vectors pass 286,760 assertions.
No headset success or complete unrelated local gate is claimed.

The actual Player SDK and deployment audit verifies 48 plugins and eight SDK
assemblies, 535 referenced types and 1,441 members, with zero API issues.
All 47 original game plugins, including rule/network/EOS libraries, are
byte-identical to B615. The six immediate startup checkpoints and conditional
module-observation checkpoint survive UnityLinker. The final packaged native
library is byte-identical to its retained native output and matches the debug
symbols at BuildId `318c1b47530eb423`.

The generated alias table covers 171 assembly identities, 31,466 alias names
and 35,536 string candidates. Its 2,391,351 payload bytes survive the actual
resource index, UnityLinker and final signed APK exactly. All five lookup/reader
method bodies, generated native definitions and symbol identities are retained.
The requested name list is computed from current owned metadata; all 32 current
caller names are accounted for, including the required Decal/Updater aliases.

All 38 serialized native startup objects match accepted B615 after pointer-ID
normalization. Logo pixels remain exactly 1024×179 with one mip; Arial bytes,
progress geometry and one early stereo camera remain exact. Native script
orders retain all 1,929 restored identities, 140 nonzero values and 643 referenced
targets. All 62 duplicated aliases agree, and the full 2,581-MonoScript identity/
order/multiplicity inventory matches B615. Movie manifests and the mod archive
remain byte-identical; the rebuilt native Addressables filenames change only
within their generated catalog/bundle closure. The Windows package is checked
against these exact signed bytes before handoff. Its complete CRC check,
embedded APK digest, all sixteen installer/collector dependency comparisons
and isolated default installer dry run pass. All nine original movie files
match their source and finished delivery archive, totaling 463,716,609 bytes.

## Storage cleanup

The download directory retains only the current B616 APK, Windows test ZIP and
`handoff.json`. Validation receipts live separately under the main checkout's
`.planning/debug/quest3-validation/B616/`.

The maintainer's cache-cleanup request is completed against a reviewed exact
allowlist: 28 historical clean Quest worktrees, 264 generated fixture runs,
20 old Unity projects, 20 old builds, 13 clean reachable source snapshots and
12 old weave caches. All 357 roots are removed, with zero skips/errors.
Historical Git branch refs remain intact. Compact per-root archives are
verified by payload SHA-256 and ZIP CRC before deletion; inventories record
symlinks without following them. Active process and incoming-link guards are
checked before each removal. No environment file is read or archived.

Measured disk space reclaimed is 305,336,070,144 bytes (284.37 GiB), after
preserving 2,347,455,366 bytes of compact historical archives. The execution
receipt, source, inventories and archive hashes remain under
`.planning/debug/quest3-validation/cache-archive-B616/`. Current B616 native
artifacts/symbols and frozen inputs, exact B615 crash artifacts, canonical game
and startup references, all supplied captures, eight potentially unique dirty
source snapshots and other agents' work remain protected. This cleanup does
not establish a headset outcome.
