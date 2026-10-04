# Quest content delivery and observable preparation — ModBuild 617

This follows `quest-capture-20261004T095749Z-47c22f2f.zip`. The maintainer
reports repeated file-verification bars, more than five minutes of waiting,
no Intro and a grey rectangle behaving like the menu. The run was stopped
manually. Work remains solely on `feature/quest3-standalone`.

## Verified hardware boundary

All nine captured files match the collector manifest. Installed B616 is exactly
1,616,497,711 bytes, SHA256
`14d421d118150969649a80ab47a3a0c902b4ddef412136a4dc1fb1bec687f9e7`, input
`70d3fc52d29f344dd79482ec08c90eff4119f164b3e18b05e51de8cf39170b68`.
Older B609/B611 diagnostic files and the previous B615 startup log are historical.
The retained SIGABRT belongs to the previous B615 process, not this B616 run.

The real mod completes eleven modules and reaches its running rig 27.234 seconds
after the startup banner. Original content delivery then copies a 488,103,428-byte
archive, with inline hashing, in 181.932 seconds. Extraction first hashes the
same archive again for 189.076 seconds. Individual 30 MB movie copies take about
0.18 seconds, followed by approximately 11.8 seconds of hashing. The largest
220,149,969-byte movie copies in 1.367 seconds and hashes in 85.120 seconds.
After extraction, every manifested file is hashed again by the final ready check.

The last state is 703.564 seconds into this run, with 49,310 main-thread frames,
zero original errors and live worker progress only 0.060 seconds old. It is still
checking the largest movie, before Addressables or original Bootstrap. This
record supports ongoing slow verification rather than a deadlocked native menu.
The app subsequently pauses and tears down when the maintainer stops it.
The capture does not establish video decoder behavior, Intro playback or menu
correctness, because those stages were never reached.

The current process's logcat records `FlatScreen.Tick` and `FollowHead` during
this preparation scene. Source permits ordinary menu capture of the synthetic
zero-mask startup camera while `ShowIntro` is enabled. This establishes an active
synthetic flat presentation, consistent with the reported rectangle. No new
screenshot accompanies this capture, so exact visible pixels remain unverified.

## Implementation and validation boundary

The existing startup diagnostic disables native IL2CPP optimizations because the
recovered assemblies previously exhausted optimizer memory. This amplifies the
managed SHA work; it does not justify redundant full-file passes in a shipping
port. Ordinary mod Debug logging and native compiler optimization are separate.
[Unity's compiler modes](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Il2CppCompilerConfiguration.html)
describe this distinction. A release-performance build, optimized content layout
and measured cold/warm startup remain later acceptance gates.

The next candidate targets optimized incremental hashing, removing duplicate
passes within one delivery operation while retaining byte-integrity checks.
It keeps full-byte verification of reused files rather than trusting file size
alone. Verified partial installations remain recoverable after interruption.
Content progress spans the manifested workload instead of resetting its only
bar for each file. A separate five-step preparation count identifies verified
mod content, the observed real mod, verified original content, native Addressables
and the observed original Bootstrap scene. This count does not claim that the
main menu or the full game is ready.

The ordinary flat-screen surface is withheld only in `QuestOriginalStartup`.
Original Bootstrap, Intro and menu presentation retain their native path.
The accepted logo aspect, current rig, controllers and Quest input remain owned
by their existing components.

Source, focused production/mutation tests, optimized native hash vectors, actual
Android SDK compilation and exact signed-APK/Windows-package audits are separate
checks. The final identities and receipts will be recorded after those checks.
No measured headset speedup or completed menu initialization is claimed yet.

## Hardware procedure

Install the complete next Windows test package over the existing signed app.
Keep app data so the interrupted movie delivery can be resumed and reused.
Observe the preparation step count, overall progress, current file count/name,
whether the extra empty rectangle stays absent, and whether Intro/menu begin.
Collect logs after the run, including a stopped view if it stalls. Record wall
time for the first launch after updating and a second launch of the same APK.
Campaign, procedural generation, authenticated EOS and native voice remain gated.
