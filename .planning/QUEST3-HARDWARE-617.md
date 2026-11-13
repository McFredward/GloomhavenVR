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

B617 uses optimized incremental hashing, removing duplicate
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

Eight relevant Quest suites pass, including production delivery/loading paths,
corruption/interruption recovery and deliberate failures of the hash/proof guards.
All fourteen source checks, strict Release (zero warnings/errors), documentation
checks and 286,760 direct unchanged protocol/golden assertions pass. The complete
unrelated local gate was not run. Delivery passes with both managed and native
hash providers; native vectors and the real Android compiler are independent
checks rather than a headset benchmark.

The actual Android SDK and stripped CIL retain all six native calls, ABI/self-test,
worker guards and final file-identity checks. The ARM64 identity structure is
56 bytes with matching offsets. The native update wrapper passes the byte array
directly; the helper is independently optimized at `-O2`. Its actual packaged
dependencies are system libraries, with no additional C++ runtime requirement.
The original native script ordering remains exact for all 1,929 entries, including
140 nonzero entries and 62 consistent alias groups. The native loading scene
retains one camera, the original logo's 1024×179 pixels/aspect and original Arial
font, with the three requested detail rows. Binocular appearance, timing and menu
initialization still require the headset.

## Exact native candidate

The ARM64 IL2CPP Android startup build succeeds from clean frozen source
`1d560e6d57cfb3eef626ca6c1a04fa76bf44b0a1`, input
`3481d60c61f9487ea1eb76811b2a8eb04db1d742b866714baed769e1f6391a10`.
APK size is 1,616,590,492 bytes, SHA256
`2c9f9e0980d38c602e0a374cbe7f414aac424484c6fb5b6bf04902a9c07d3422`.
Package `dev.gloomhavenvr.quest` and signing certificate SHA256
`1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012`
are preserved for updates retaining app data.

Private handoff files live in the main checkout's `.planning/debug/quest3/`:
`GloomhavenVR-Quest-B617.apk`, `GloomhavenVR-Quest-B617-Windows-Test.zip` and
`handoff.json`. Validation and independent compact evidence are outside that
download directory in `.planning/debug/quest3-validation/B617/`. This remains
an explicitly dummy-identity startup diagnostic. No measured headset speedup,
working Intro/menu, campaign or authenticated crossplay is claimed.

The complete Windows ZIP passes CRC and embedded APK SHA checks, with all sixteen
installer/collector dependencies matching the frozen source. Its isolated
installer dry run selects this local diagnostic without an ADB operation or
settings write. Source/native/build audits remain distinct from this packaging
check and from future headset evidence.

Authorized cleanup archives compact repeatable evidence before removing three
historical Quest worker checkouts, four old SDK-proof directories, the superseded
B616 APK/Windows ZIP and its reproducible packaging staging folder. Branch refs,
all supplied captures, read-only game references and B615/B616/B617 native inputs
and symbols remain preserved. The two historical cleanup receipts measure about
8.0 GiB reclaimed. Exactly the three current handoff files remain in `quest3/`;
archive/validation files are kept outside it.

The shipping startup requirement remains open: multi-minute waits before the
menu are unacceptable. The current capture measures redundant verification
before the original game starts, not original game loading performance. After
this correction, measure delivery and original initialization separately on
cold and repeated launches. A complete performance build and content layout
that loads required game data when needed remain subsequent porting gates.

## Hardware procedure

Install the complete B617 Windows test package over the existing signed app.
Keep app data so the interrupted movie delivery can be resumed and reused.
Observe the preparation step count, overall progress, current file count/name,
whether the extra empty rectangle stays absent, and whether Intro/menu begin.
Collect logs after the run, including a stopped view if it stalls. Record wall
time for the first launch after updating and a second launch of the same APK.
Campaign, procedural generation, authenticated EOS and native voice remain gated.
