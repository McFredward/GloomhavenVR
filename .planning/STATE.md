# State — where the project stands

**Quest B625 delivered, 2026-10-06: final agent-host APK and Windows builder.**

The signed Campaign/Guildmaster/purchased-DLC hardware archive has passed actual
Player, native program, catalog, member CRC and nested APK/content readback.
Runtime is frozen at `bdb7c4aec`; APK SHA begins `3d32aeb6232f`, Windows archive
SHA begins `b4318631c22b`. Matching native debug symbols are retained. Headset
engine execution, menu visibility, generation time, saves and cross-platform
sessions still need hardware evidence; a successful build does not close them.
See [B625 handoff and hardware procedure](QUEST3-HARDWARE-625.md).

The separate game-free Windows source release launches with `Quest-Builder.cmd`,
uses owned Steam/GOG/Epic files, provisions pinned local tools/Python, preserves
verified stages and exports bounded/redacted build logs. Resource-aware native
jobs use measured 32/28-GiB compiler budgets and known Windows commit headroom;
actual paging speed is unverified. Fresh conversion disk demand is estimated
before downloads. Focused integrated checks pass: 426 builder, 50 Wizard, six
voice and nine UI/browser checks, with one optional browser skip. Actual exported
Git-free inventory/resume/reference checks pass; the first full Windows build,
fresh XR dependency compilation and guided Unity licensing remain user tests.
No further APK build on this host is authorized by the current instruction.

After accepted delivery, three completed clean Quest worker worktrees, three
verified historical source snapshots, B623/B624 outputs and the failed B625
native attempt are retired. Small audit receipts, branch refs and unique
ignored dependencies remain. The observed shared-filesystem free increase
during this cleanup is 54,144,888,832 bytes; the separately removed
owned temporary swap files total 34,359,738,368 bytes. Current B625
hardware files/symbols, warm imports, original game inputs, captures and other
agents' worktrees are retained. No tracked `dev` files are changed.

**Quest build host decision, 2026-10-06: B625 is the final agent-host APK build.**

Finish and deliver this Windows hardware package first. Future APK builds run on
the maintainer's stronger Windows PC through the builder/wizard. Deliver builder
sources/tools instead of repeating agent-host APK builds. Detect CPU/RAM and
choose bounded compiler concurrency, retain valid completed content/import caches,
resume interrupted stages and export useful build logs/support evidence for review.
The full Windows toolchain path must be verified separately from the already
working Windows installer; no Linux-only prerequisite may remain an implicit
requirement for novice users.

**Quest B625 corrections prepared,2026-10-06: full Campaign/Guildmaster scope.**

Verified B624 hardware startup installed5,897 remaining full-game files on the
headset; the earlier menu tree contained only470files. The PC installer now
expands both signed inventories, resumes changed-file batches and commits native
completion receipts. Full-target startup accepts those receipts without6,367
file-stat/hash scans and stops with localized PC-installer guidance if incomplete;
there is no hidden multi-minute archive adoption/extraction fallback.

Guildmaster is restored by explicit maintainer correction: original mode menu,
save validation/loading and multiplayer admission are retained. Its earlier
exclusion assumed a completely removed procedural engine; that replacement was
never completed. The port retains the original x64 DLL behind Wine/Box64. B624
failed the Wine guest libc entry before Engine.Start; the actual Android Box64
binary now includes the upstream glibc guest-start functions as well as Bionic.
Successful Quest engine execution, generation and mobile performance remain open.

Vulkan camera-video orientation uses the observed GPU projection sign; manual
mip generation respects automatic ownership. A Quest-only transparent consumer
preserves already-composed native menu RGB instead of multiplying alpha twice.
Exact black-menu causal closure remains unverified; two bounded handover snapshots
and asynchronous16x8probes will distinguish producer/visibility/consumer failure.
Native full-target compilation changes Debug to Release while Development player
logging remains available. The forthcoming signed B625 hardware package must be
built/read back before any new handoff replaces the accepted B624 package.

**Quest full Campaign B624 built and verified, 2026-10-06: isolated feature.**

The exact B623 hardware capture records a native Adreno/Vulkan crash during
replacement of the external XR eye images after live MSAA4 and eye scale1.5
requests, before original game bootstrap. Quest now activates the shared Steam
Frame standalone defaults and retains its initial Vulkan eye allocation. Native
startup MSAA is0 across all six actual Player quality levels; lower resolution
uses viewport-only scaling. Stored values survive, with accurate English/German
descriptions and effective labels. Desktop/GLES retain their original setters.

The signed full ARM64 IL2CPP Player contains all13 original scenes plus Quest
bootstrap and the complete Campaign/purchased-DLC native bank. APK size is
2,494,104,762 bytes, SHA `7b16dabefd36c103…`; adjacent bank size is10,912,754,848
bytes, SHA `0831f24bf61cd77b…`. Runtime is frozen at `59d787816`, input
`c85cbfcb18abf929…`. Release compilation has zero warnings/errors;3081 platform
assertions/17 defect controls,576 affected Frame assertions/5 controls and all378
builder tests pass. Actual delivery retains688 original Shaders/51,564 aliases
and13 ComputeShaders/36 Vulkan kernels, with zero exhaustive compiler queries.

Accepted whole-bank evidence is inherited only for6,365 byte-identical payloads.
The changed bundle retains all public roots and other native object bytes; its
six reserialized Shaders retain4,296 byte-identical actual executable programs.
Independent catalog comparison preserves lookup associations and dependency
edges. Actual APK CRC and native quality-setting readback pass. Native Build ID
`eb8f47caa41067ae` matches retained debug symbols and signed Player;1,150 actual
native-source/managed-backup files are hashed. Windows ZIP64/member CRCs, nested
APK/bank hashes and all50 current installer-source hashes pass. Windows archive
size is13,408,148,631 bytes, SHA `659a8d78960f3d3d…`. Headset startup, pictures,
Campaign runtime, saves and cross-platform sessions still require hardware tests.
See [B624 hardware procedure](QUEST3-HARDWARE-624.md).

After verified handoff,19 completed Quest worker worktrees,20 reproducible clean
historical source snapshots, obsolete B622 builds/project, old B620/B623 symbol
payloads, one rejected partial bank and old hardware packages are removed. Branch
refs, unique untracked inputs and small receipts are retained. The dirty worker
and eight dirty/unknown historical snapshots remain. Shared filesystem free space
increases68,756,516,864 bytes during this cleanup; the owned16-GiB build swap was
removed separately beforehand. Current imports/Library/Shader caches, canonical
game inputs, captures and other agents' checkouts remain. Only B624 payloads and
current handoff evidence remain in the main hardware directory. No tracked `dev`
files are changed; publish only `feature/quest3-standalone`.

**Quest full Campaign B623 built, 2026-10-06: isolated feature.**

The maintainer authorized implementation through a complete hardware APK, not
another menu diagnostic. Work remains on `feature/quest3-standalone`, independent
of concurrent `dev` changes. All13 original scenes and the entire original catalog
are staged. Actual Android compilation builds all three current-mod asset banks;
the original native engine/Opus payload and all1,465 core/six bundled audio clips
are prepared. Full source recovery stages688 original Shaders/9,187 materials.
The signed full IL2CPP Player and13.41-GB Windows hardware archive are built.
Actual native delivery passes all688 original Shaders/51,564 original aliases and
13 ComputeShaders/36 Vulkan kernels, with zero Player Shader compiler errors.
The APK is2,494,100,526 bytes, SHA `ef954d63eb7f…`; its adjacent10.91-GB bank has
6,367 verified entries/3,256 Android bundles. Runtime is frozen at `fa6c1f9a`,
with recorded host/Editor corrections from `796ea528d`. All378 affected builder
tests pass. Native Build ID `d23d2717d3cae4f7` matches retained debug symbols and
the signed Player;1,150 actual native-source/managed-backup files are hashed.
The independent whole-artifact audit passes8,968 native payloads, all6,531 original
public catalog roots,6,431 aliases,601 procedural definitions and actual movies.
It independently confirms both688-Shader/51,564-alias banks and all2,316 public
original Material roots; Windows ZIP64/CRC/nested-payload/50 installer-source
hashes pass. Windows SHA is `e855b11c43c57…`. Integrated headset execution,
pictures, timing, procedural runtime, saves and cross-platform sessions remain
hardware checks. B623 replaces a menu-only diagnostic with the full Campaign
candidate; see [B623 hardware procedure](QUEST3-HARDWARE-623.md).

Superseded B622 hardware packages and B615–B622 symbol payloads are removed after
actual identity/reference checks; small historical receipts remain. Current
imported project/Library, game inputs, saves, captures and matching B623 native
debug/source data are preserved. Cleanup in this resumed session reclaims
36,603,371,520 bytes, including the released owned16-GiB build swap. No other
agent's checkout or the shared `dev` branch is changed.
See [full port evidence](QUEST3-FULL-PORT-PROGRESS.md).

**Quest B622 hardware candidate ready, 2026-10-04: isolated feature.**

The exact B621 capture confirms nonuniform Intro decoder/capture/consumer pixels
but continued invisible headset imagery. The actual legacy screen shader expects
an XR texture array while the mod supplies Tex2D captures. Quest now selects a
centrally gated world shader with explicit 2D left/right inputs, GPU eye routing
and persistent-material transition resets. Real two-eye GPU and actual Android
mono/instancing/multiview banks cover the defect. Hardware scope discovery costs
81–94 ms alongside approximately one-second GC-free spikes. The candidate phases
seven exact native-owner queries while preserving inactive/persistent discovery
and known-owner enforcement. Local whole-cycle regression is retained honestly;
per-tick cost and Android improvement remain distinct evidence. Original hidden
UI alpha does suppress the actual blur draw, so no guessed visibility workaround
is applied. Audio, keyboard and confirmed hint/options behavior are preserved.
Signed ARM64 native build succeeds from clean freeze `4c10c034`, input `9d148c7036f2…`,
APK SHA `dd61d996dc75…`, size 2,587,919,101 bytes. Twenty-one affected Quest suites,
fourteen source checks, strict Release and direct unchanged wire/golden assertions
pass; the corrected loading test fixture and original failure are retained.
Independent final native audits verify seven preserved original types and 69
disjoint actual ARM64 functions, with 1,414 assertions plus two archive CRC checks.
Build ID `9a2274f00b636520` and exact packaged library bytes match. The finished
APK's resource index resolves both screen/video shaders. World-screen compiler
coverage is three banks/six stages; actual GLES resources retain mono and multiview
across three hardware tiers, with plain 2D capture samplers and actual vertex eye
routing. Instancing preflight success is not claimed as a third baked bank.

Windows ZIP SHA is `2552a84e172b…`, size 2,588,037,106 bytes. CRC/content/hash and
isolated embedded-B622 installer checks pass. The archive source `2f361f2d` records
its packaging-time state; later commits document final proof without altering the
delivered bytes. Three completed Quest workers, obsolete B621 project and retired
downloads/staging are removed after archival/symbol retention, measuring
44,187,906,048 additional free bytes. Latest-only handoff, captures, canonical
inputs, native history, branch refs and parallel dev work are preserved. Visible
B622 results remain unverified; publish only `feature/quest3-standalone`.
See [B622 hardware procedure](QUEST3-HARDWARE-622.md).

**Quest B621 hardware candidate ready, 2026-10-04: isolated feature.**

The exact B620 capture verifies installed APK/input and native movie decoding.
The maintainer confirms keyboard, Guildmaster explanation and options filter;
Intro/ambient imagery and the recurring hitch remain defects. The native hidden
promotion trailer is intentionally idle, separately proven against original and
final stripped CIL. B621 targets completed camera-capture consumption with bounded
pixel evidence and replaces active Quest scope's recurring global component sweep
with scene-root discovery. Recent frame spikes and actual discovery costs survive
in bounded Debug state. Headset outcomes remain unverified; preserve confirmed
behavior. See [B621 hardware procedure](QUEST3-HARDWARE-621.md).

The signed ARM64 IL2CPP candidate succeeds from clean native source `a06d33a1`,
input `bd857629028e…`, APK SHA `b983d610e5a8…`, size 2,587,918,076 bytes.
Nineteen final focused Quest suites, fourteen source checks, strict Release and
286,760 direct unchanged protocol/golden assertions pass. The loading fixture's
new read-only bridge/material stubs required one corrected rerun; its initial
failed receipt remains archived. No unrelated complete wrapper gate is claimed.
Actual source/CIL/generated C++ and compiled ARM64 boundaries pass 577 CIL plus
371 further assertions: 46 selected functions are present, with native build ID
`c2c3fb5d2392dbf3` and exact packaged library bytes. All 957 current mod C# files
are selected automatically. Editor/player lowering differences are recorded;
only actual per-method equalities are claimed. Both camera-video GLES stages and
the retained original shader/media/audio/sprite import checks pass.

The Windows archive passes CRC/content/hash verification and isolated installer
selection of embedded B621. ZIP SHA is `5a764db0d36a…`, size 2,588,030,548 bytes.
Near-plane completion and far-plane completed-color restoration pass real GPU
fixtures; additional far-plane copies have an unmeasured headset GPU cost. Native
UI blur recovery is a separate source-proven gap, with actual culling and pixel
evidence added rather than guessed visibility/shader changes. Visible video,
stereo depth and sustained hitch improvements remain hardware gates.

Three completed Quest worker roots and the obsolete B620 generated project were
removed after compact archival and matching B620 symbol retention, measuring
36,545,183,744 additional free bytes. Retiring superseded downloads and the verified
B621 packaging stage measured another 7,764,283,392 bytes. The handoff directory
contains only the latest APK, Windows archive and receipt. Current/native historical
evidence, canonical inputs, supplied captures, Git refs and parallel dev worktrees
remain. This candidate is published only on `feature/quest3-standalone`.


**Quest B620 scoped menu follow-up, 2026-10-04: isolated feature.**

The exact B619 package/input and supplied screenshot are verified. Audio is now
hardware-confirmed solved. Intro decodes and plays audio but the captured menu
camera stays grey; an Android-only output adapter composes original decoded
frames in the actual owned capture, retaining original camera modes and stereo
routing. Real Unity GPU tests cover depth, alpha, aspect, target replacement and
ownership; headset pixels remain unverified. The attached Guildmaster tooltip,
two desktop-only option rows and existing VR keyboard's Android TMP input seam
are corrected behind the central standalone gate. The generated player enables
incremental GC, with bounded Debug frame/GC/write-cost evidence to investigate
the reported recurring hitch. No sole hitch cause is established.
See [B620 hardware procedure](QUEST3-HARDWARE-620.md). Signed ARM64 IL2CPP
candidate succeeds from clean native source `4621f215`, input `4b5cb809a87c…`,
APK SHA `2d6c25001cb5…`. Eighteen final focused Quest suites, all fourteen source
checks, strict Release and 286,760 direct unchanged protocol/golden assertions
pass; no unrelated complete local gate is claimed. An integration test fixture
needed the new read-only capture seam; its corrected rerun passes. Real Unity
GPU tests and both Android video shader stages pass. Independent imported,
stripped, backup CIL and actual compiled native symbols establish the scoped
boundaries (136 CIL assertions + 96 further checks), with all 957 current mod
C# source files included automatically. Headset outcomes remain unverified.
The Windows ZIP passes complete CRC/content/hash and isolated installer checks;
embedded B620 identity wins in merged folders. APK and ZIP are about 2.59 GB.

Four obsolete Quest worker/project roots were removed after archive and matching
B619 symbol retention, measuring 36,563,001,344 additional free bytes. Retiring
superseded downloads and the verified B620 packaging stage measured another
7,764,107,264 bytes. Latest outputs contain only the B620 APK, Windows archive
and handoff. Canonical inputs, captures, Git refs and parallel dev worktrees stay.
A preview initially wrote its metadata into the B619 archive directory; B620
preview files were moved to their own archive. The retained B619 execution log
preserves its historical deletion evidence and measured result; a reconstruction
note records the overwritten historical summary rather than inventing counters.

**Quest B619 menu follow-up, 2026-10-04: isolated feature.**

The exact latest B618 capture and three screenshots are inspected. The maintainer
confirms menu loading, input and native MR; audio, Intro, spinner geometry, UI
materials and aliasing remain defects. B619 restores original audio channels and
lengths, native movie paths, spinner padding/pivot and Quest menu mip sampling.
Local DLC ownership becomes part of the build input; native ads remain with grey
PC-purchase/rebuild hints. The private test selects the confirmed Jaws of the Lion
and Solo Scenarios and checks their source content, using the authorized dummy
identity. See [619 evidence and hardware procedure](QUEST3-HARDWARE-619.md).
The signed ARM64 IL2CPP candidate succeeds from clean source `61c83b30`, input
`3a4df7911d12…`, APK SHA `86bc11630daa…`, size 2,587,817,335 bytes. Fifteen focused
Quest suites, fourteen source checks, strict Release and 286,760 direct unchanged
protocol/golden assertions pass. Actual Android audio/sprite imports, all original
post-effect and restored UI shader banks, final stripped CIL/AOT and packaged
media are verified. The complete Windows archive passes CRC, exact file/hash
checks and the isolated installer dry-run. The next headset outcome, full campaign
and authenticated Android crossplay remain separate gates. No unrelated full
local test gate was run for this Quest follow-up.

The maintainer additionally requires Steam, Epic and GOG PC input support, with
local installation metadata preferred for DLC discovery. GOG documents installed
`goggame-<DLC-product-ID>.info` mini-manifests, including offline installers. Our
provided Steam copy contains no verified Gloomhaven GOG/Epic metadata fixture;
ordinary bundled DLC assets cannot establish purchase. Separate future provider
discovery and offline profile adapters from DLC content validation. The current
B619 builder's automatic account/DLC path is Steam-specific; it does not establish
GOG/Epic support. The maintainer prioritized handing off B619 before that expansion.

Five obsolete Quest worker/project roots were removed after compact proof archival
and matching B618 native-symbol retention, measuring 35,268,067,328 additional free
bytes. Retiring duplicate B618 downloads and the verified B619 packaging stage
measured another 5,782,380,544 free bytes. Latest B619 project/native output, Git
refs, canonical source inputs, supplied captures and parallel dev worktrees remain.
The current download directory contains only B619 APK, Windows archive and handoff.

**Quest B618 longer Intro/menu follow-up, 2026-10-04: isolated feature.**

The longer supplied captures reach original Intro and MainMenu and expose native
video opening, camera-anchor rejection, missing Bloom passes and null platform
user removal. Existing content takes 182 seconds to re-read. Implementation uses
a content-keyed installation receipt without warm byte scans, one conditional
preparation canvas, original asset-load ownership and Quest-native camera-anchor
recovery. Lossless Intro container adaptation retains every authored A/V packet
and timestamp; its Android decoder outcome remains unverified. Merged Windows
archives now select embedded build stamps and confirm installed Android identity.
See [618 evidence and hardware procedure](QUEST3-HARDWARE-618.md). Hardware results,
shipping startup performance and full campaign/crossplay remain open.

The signed ARM64 IL2CPP candidate succeeds from clean native source `71ed954f`,
input `97e403505791…`, APK SHA `ee288e822a1b…`. Thirteen focused Quest suites,
affected reruns, all fourteen source checks, strict Release and 286,760 direct
unchanged protocol/golden assertions pass; no unrelated full local gate is claimed.
Independent final SDK/CIL/AOT/resource audits and all eighteen original GLES
shader programs (thirty-six stage sections) pass. The supplied Windows installer
log confirms B618 installation, then exposes a false rejection of Dexopt's
`[location is error]` diagnostic field. The installer now checks command exit and
exact package stamps for that structured query; 155 installer tests pass with
23 Windows-only skips. A small installer-only archive avoids another APK download.
The replacement full Windows archive includes that fix; the native APK is unchanged.
Twelve obsolete Quest worker/project roots are removed after evidence and matching
native-symbol retention, reclaiming approximately 96.8 GB during that cleanup.
Only the current APK, complete Windows archive and handoff receipt remain in the
download directory. Startup visuals and latency still require the next headset run.

**Quest B617 preparation follow-up, 2026-10-04: isolated feature.**

The exact B616 capture verifies APK/input and a running real VR rig after
27.234 seconds. Its current process has no native abort; the retained SIGABRT
is historical B615. At 703.564 seconds it is still doing live file verification,
before Addressables or original Bootstrap. Managed O0 SHA performs redundant
archive/file passes; movie writes total 2.868 seconds versus 179.583 seconds
for their individual hashes. The ordinary FlatScreen is active in the synthetic
startup scene, consistent with the reported empty rectangle; exact pixels have
no supplied screenshot. Optimized native hashing, one delivery operation and
observable overall/file/step progress are implemented alongside a narrow
synthetic-scene flat-screen gate. Full-byte integrity, accepted logo, original
presentation/rules/network semantics and concurrent `dev` work remain preserved.
See [617 evidence and procedure](QUEST3-HARDWARE-617.md). Eight relevant Quest
suites, all fourteen source checks, strict Release and 286,760 direct unchanged
protocol/golden assertions pass; no complete unrelated local gate is claimed.
The signed native ARM64 IL2CPP candidate succeeds from clean source `1d560e6d`,
input `3481d60c…`, APK SHA `2c9f9e09…`, retaining signing/package identity.
Independent actual SDK/stripped/native hash ABI and native presentation/order
audits and the exact Windows package/source/dry-run checks pass. Seven historical
Quest worktree/SDK directories and superseded B616 downloads/package staging are
removed after compact evidence archival, preserving refs, captures and native
inputs/symbols. Those historical cleanups reclaim approximately 8.0 GiB. Only
the B617 APK, Windows ZIP and receipt remain in the download directory. Headset
speedup, Intro/menu success and shipping startup performance remain unverified.

**Quest B616 native-abort follow-up, 2026-10-04: isolated feature.**

The new exact B615 capture verifies the installed APK/input. The maintainer
accepts the branded bar. All eleven real-mod modules complete before a native
UnityMain SIGABRT: Debug performance instrumentation calls the Quest Harmony
facade's broad runtime type scan, whose `Type.FullName` read aborts IL2CPP GC
descriptor initialization. The exact unrelated type is not retained. Original
Bootstrap has not yet been observed; the old saved state predates real-mod
creation. Targeted lookup and immediate startup checkpoints are implemented;
[616 evidence and procedure](QUEST3-HARDWARE-616.md). Accepted loading artwork,
original rules/save/network semantics and concurrent `dev` work remain unchanged.
The signed native ARM64 IL2CPP candidate succeeds from clean source `36d3f2cb`,
input `70d3fc52…`, APK SHA `14d421d1…`, retaining signing/package identity.
Five relevant Quest suites, all 14 source checks, strict Release and 286,760
unchanged protocol assertions pass; no complete unrelated gate is claimed.
Independent actual SDK, stripped checkpoint, resource/native symbol, exact
presentation and all 1,929 native script-order audits pass. The Windows handoff
is verified. Authorized cleanup removes 28 historical Quest worktrees and 329
obsolete generated cache roots, preserving branch refs, compact evidence,
current native symbols/inputs, captures and other agents' work. Measured disk
space reclaimed is 305,336,070,144 bytes (284.37 GiB). Only the B616 APK,
Windows test ZIP and handoff receipt remain in the download directory.
Hardware remains unverified.

**Quest B615 hardware candidate, 2026-10-04: isolated feature.**

The exact B614 capture identifies the first original YML failure as a Unity path
read from a worker. B615 publishes managed paths on the main thread before
initializers, preserves the original screenshot coroutine with a mobile filename
bridge, and delivers nine owned movies with original native URL playback. The
original script ordering is restored and independently checked against source.
Input readiness, public hook lookup and camera message signatures are corrected.
Reusable world-space loading artwork uses the existing GloomhavenVR logo, a real
bar and measured percentage. Its actual import preserves the original 1024×179
aspect; the real rig owns the later loading view. Bounded diagnostics retain first
failures and camera/video evidence. Binocular loading alignment, Android rule/menu
execution and the captured multiplayer widget failure still need hardware.

The signed 1,615,998,254-byte native ARM64 IL2CPP startup APK succeeds from clean
runtime/tool source `911fc23f`, input `44c066b3…`, retaining package and signing
identity. Ten scoped Quest suites pass at the preceding runtime-identical source;
the three affected suites, all 14 source checks and strict Release pass after the
Editor-only logo fix. Direct protocol vectors pass 286,760 assertions. Actual SDK,
1,929 imported script orders (140 nonzero), deployed assembly and generated scene
checks pass. Exact APK retention and Windows handoff verification are recorded in
[B615 evidence and procedure](QUEST3-HARDWARE-615.md); no headset success or complete
unrelated local gate is claimed. Work remains solely on `feature/quest3-standalone`.

**Quest loading follow-up, 2026-10-03: isolated feature, ModBuild 614.**

The new B613 capture verifies the installed APK/input and a stop inside the opaque
mod-content delivery gate before plugin creation, without a retained managed
exception. Its scene has no camera until delivery completes; a 42-second
observation does not rule out slow Debug/O0 verification. The exact old stall
cause is unresolved. B614 serializes an early temporary stereo loading view,
streams APK archives on workers with preserved verification, removes the extra
main-thread bank hash and records phase/bytes/UTC/frame progress. The collector
adds bounded app-start history and fixed stat-only delivery metadata.
[B614 evidence and procedure](QUEST3-HARDWARE-614.md). The signed 1,153,837,389-byte
APK is verified from clean runtime/tool source `8a12aaff`, input `6b07372a…`;
package and signing identity are retained. Seven focused Quest suites, 14 source
checks, strict Release and 286,760 protocol assertions pass. Independent compiled,
actual SDK and exact APK camera/font/shader retention audits pass. The private
Windows handoff verifies source dependencies and installer selection. Hardware
outcomes remain unverified; no full unrelated local gate is claimed. Work stays
solely on `feature/quest3-standalone`.

**Quest real-mod startup candidate, 2026-10-03: isolated feature, ModBuild 613.**

The supplied B612 capture verifies the installed APK/input and original loading
error after Intro. Original content and native Addressables loads succeeded;
an absent InputSystem getter is recorded, but repeated errors exhausted the
old budget before the first Android rule failure could be retained. The native
pause timeout does not prove OOM. Actual original rule libraries parse the
same content in Unity2021.3.5; B612 native output retained all protected types.

The maintainer requested immediate reuse of the real VR mod. B613 adopts the
player-owned XR session, activates the real plugin after verified Android mod
art extraction and uses its existing rig/input/menu/keyboard. The synthetic
menu bridge is not created. Native Quest passthrough replaces the desktop key
color and handles session recreation. All authored bank/compiler inputs and
the actual Android GLES3 shaders/textures are verified privately.

The local builder now audits imported Unity-package APIs before IL2CPP, restores
the original UGUI batching gate in a generated private package and initializes
the original public lazy rule root. Threaded, bounded diagnostics reserve 64
distinct error stacks. Unknown first Android rule failure, actual headset
startup, campaign generation/saves, Android crossplay and sustained performance
remain gates. [B613 procedure and evidence](QUEST3-HARDWARE-613.md).
The signed 1,153,868,013-byte APK is built and verified from clean runtime/tool
source `6a62daf2`, input `99fd0fec…`; package/signing identity are retained. The
private Windows handoff, APK integrity/signing and installer selection are
verified. All seven focused Quest suites and the already-run affected rendering,
MR, figure/card and VR-options regressions pass, alongside all 14 source checks,
strict Release (zero warnings/errors) and 286,760 protocol assertions. Following
the maintainer's question about redundant unrelated tests, validation is scoped
to these boundaries; the stopped broad run is not claimed as a complete gate.
No B613 headset success is claimed. Work remains solely on
`feature/quest3-standalone`.

**Quest original-startup checkpoint, 2026-10-03: isolated feature, ModBuild 612.**

The maintainer accepted B611 animation, lighting, textures and native button
handling. Its supplied capture verifies the installed APK hash and contains 14
72–73 FPS samples from the small diagnostic; this is not campaign performance
evidence. B612 adds right-stick height and fixes the Windows collector's handling
of successful ADB receipts on stderr. Startup/current/previous logs and bounded
state are collected alongside the older probe logs when present.

The privately built B612 Android player uses the original Bootstrap/Intro/unified/MainMenu
scene closure, the real current mod build/static weave and an offline local
platform adapter. The full VR mod remains inactive pending its AOT/XR lifecycle
proof. Actual Unity has remapped 7,665 package script references while preserving
original callbacks, resolved the export's case-colliding paths without changing
original GUIDs, and built a native Android catalog with 405 eligible assets,
762 original-key aliases and 10 labels. Required original native Unity modules
are derived from the selected player and exact editor. The actual signed ARM64
IL2CPP build succeeds with diagnostic Debug/O0, a higher parser nesting limit
and LLVM LLD for the large native library. Its 1,109,713,887-byte APK has SHA256
`567722d511049f183e35236427d4128ba6ee056398df7c4543b982efc8bf3797`, from frozen
runtime/tool source `3e4f8edb`, input `430fd5ea2730a2353be04c93d90c49dd7c4ede27ebc1ef6bd94122d9cf4f4fea`.
The embedded manifest matches the immutable input; package and B611 signing
identity are retained. Windows installation now budgets this large transfer by
verified file size, without uninstalling or retrying. Final checks pass 14 source
suites, all 111 local suites and 286,760 wire/golden assertions, bundle/mesh and
surface checks; strict Release has zero warnings/errors. The older Build-601
guard comparison requires review rather than exit zero: the fresh reviewed609
comparison accepts only build constants, additional Quest texts and branch
metadata, with unchanged references/resources. The private Windows package's
actual default dry-run is verified; headset outcomes remain unverified.

The original Photon client reached the live desktop Master/default lobby without
EOS or additional authentication. Android connection, original room admission
and PC crossplay remain hardware gates. Original tokens/rules/protected network
types stay unchanged. Guildmaster/Workshop and voice opt-in are unavailable in
this startup target; campaign generation, original save round trips, shader
parity and full mod execution remain separate gates. See
[the B612 procedure and boundaries](QUEST3-HARDWARE-612.md) and
[the original-network proof](QUEST3-NETWORK-PREFLIGHT.md).
Quest work stays on `feature/quest3-standalone`; concurrent `dev` work is untouched.

**Quest hardware response, 2026-10-03: isolated feature, ModBuild 611.**

The first private headset photographs show native passthrough, both controllers
and a successful diagnostic storage read. The maintainer reports incorrect rays,
an orange figure and missing navigation. Source evidence identifies grip-based
pointing and a dormant orange material property activated by the Standard shader
conversion; original 2048x2048 albedo/normal textures are present. The photographs
lack a visible build banner and cannot independently establish installed identity.

The611 diagnostic separates tracked grip/aim, adds joystick navigation with
neutral resume guards, corrects approximate tint mapping and adds reversible
material/atlas, animation, enlarged model, stereo/colour and lifecycle/timing
checks. Actual build/input identity is displayed; bounded evidence is persisted
for the new read-only Windows ADB collector. Private installation and one-run
hardware procedure: [expanded611 checklist](QUEST3-HARDWARE-611.md).
This remains a diagnostic; campaign/startup, original shaders/saves and crossplay
are still required. New headset outcomes remain unverified. Quest work is kept
on `feature/quest3-standalone`; parallel Frame610 `dev` work is untouched.

**Quest wireless installer, 2026-10-03: `feature/quest3-standalone`.**

The maintainer clarified that Quest work stays isolated on this feature branch
while other agents continue on `dev`. The accidental installer-preparation commit
on `dev` was reversed without removing their concurrent Frame610 work. Quest
runtime remains ModBuild609; this tooling update does not create a new game APK.

The Windows double-click entry point is `scripts/install-quest-wireless.cmd`.
It provisions a private pinned CPython runtime and `.quest-venv` beside the
script, so Windows users need no Python installation or environment activation.
The current installer has no third-party Python requirements; future pinned
requirements are installed into this environment when their manifest changes.
It remembers the successful local APK source, WLAN endpoint, ADB executable and
Quest hardware identity. First use or connection recovery can discover Wi-Fi over
an authorized USB Quest; later runs reconnect wirelessly, verify the latest
completed builder receipt or private handoff, install with `-r` and launch.
Signature conflicts stop with app data retained. Missing Windows ADB is provisioned
from Google's pinned official Platform-Tools into `scripts/.quest-adb/`; existing
external tools remain usable. No APK downloads, store services, global ADB resets
or automatic uninstalls occur. An explicit manual APK
path is also supported and remains distinguishable from verified builder output.

All 44 focused installer tests pass, including wrong/offline/unauthorized devices,
ambiguous selection, stale addresses, tampered/concurrently changed artifacts,
receipt containment, exit-zero ADB failures, signature mismatch controls and
automatic ADB discovery/provisioning boundaries before headset mutation.
Dry-runs against the actual B609 handoff and builder output select the reviewed
`5818e9d22cd47...` APK without ADB activity or settings writes. A portable
PowerShell7.6.6 runtime verifies option/path forwarding and failure exit codes.
The bootstrap adds 22 PowerShell controls for actual venv creation/reuse,
path relocation, owned-folder repair, exclusive setup, pinned runtime validation
and a real local-wheel install/hash rejection. Explicit Legacy argument controls
reproduce the maintainer's Windows5.1 quote-loss SyntaxError and pass after the
single-quoted Python-literal fix. His next Windows run confirms actual runtime
download, venv creation and selection of the reviewed B609 APK. Managed ADB and
wireless transport remain Windows/Quest checks.
The ADB helper adds 20 download/cache controls, including corrupt binaries/DLLs,
partial downloads, archive escape, foreign content, links/junctions and concurrent
setup. The original pinned Google archive also passes staged extraction and
offline cache reuse without executing its Windows binaries on the Linux host.
The complete feature-branch gate evidence is retained privately in the main
checkout's `.planning/debug/quest3/wireless-validation/`. Actual Windows/Quest
wireless transport and installation remain hardware checks. See
[the Windows wireless procedure](QUEST3-WIRELESS-INSTALL.md).

**Quest branch checkpoint, 2026-10-03: `feature/quest3-standalone`, ModBuild 609.**

The maintainer explicitly authorized implementation on a new branch from current
`dev` (`5344a550`, Build607). This isolated branch includes the owned-game local
builder, asset recovery/audit, generated static Harmony integration, an Android
OpenXR template and native passthrough composition in Unity's existing XR session.
A signed ARM64 IL2CPP hardware diagnostic with the original animated BanditGuard
asset is available privately; it uses the explicitly authorized DUMMY profile.
The latest parallel Frame608 changes are retained in this branch. Quest diagnostic
evidence does not change Frame hardware acceptance or imply that its desktop
package is the Quest app.

The checkpoint tests head/controllers, native passthrough, local diagnostic storage,
embedded identity and disabled Guildmaster/Workshop tooltip presentation. It is
**not a playable campaign, complete mod startup, original save or multiplayer port**.
The real export reports placeholder shaders, 16 serialization-layout errors and
deferred bundles; standalone lifecycle/content-root and original IL2CPP/platform
startup remain required. Compilation/signing and source tests are separate from
unverified Quest images, tracking and performance. See
[Quest implementation evidence](QUEST3-IMPLEMENTATION.md) and
[private hardware test procedure](QUEST3-HARDWARE-609.md).

Final integrated validation on runtime/tool commit `cc041fab` passes **14/14 source
suites, 107/107 complete local suites and 286,760 wire/golden assertions**, strict
Release with zero warnings/errors, bundle/figure-bank checks and independent
suite/log-hash verification. Relative to reviewed Frame608, eight existing compiled
types change only in the build constant; QuestText is the only added type, with no
removed type/reference/resource. The private signed ARM64 APK is built and validated
with explicit SDK/NDK/JDK selection and retained runtime shaders. Hardware remains
unverified. Evidence is in `.planning/debug/quest3/validation/`; original game payload,
APK, accounts and signing keys are excluded from Git.

The following records the preserved `dev`/Steam Frame baseline:

**Updated 2026-10-03: dev 1.1.0 / ModBuild 608, Frame607 hitch analysis and targeted interaction optimizations.**

The new Frame607 hardware capture confirms real figure/FX/cloth application, including
34.87% fewer admitted body vertices at 0/0. It does not isolate the slider FPS benefit:
all settled markers are 0/0, other quality intervals have active VR Options, and many
stationary samples are untracked. Both supplied logs identify607/5344a5504 and describe
the same run. Loaded interaction frames still reach 200–500 ms, with source-backed
card capture/census, prop pickup and atomic wall-table work. Actual GPU busy remains
unavailable; conditional managed heap samples fall repeatedly and do not prove a leak.
See [Frame607 hitch analysis](../docs/performance/FRAME-607-HITCH-ANALYSIS.md).

Build608 removes false wall invalidation from native-named visual ghost children via
their exact FigureVisualMirror owner, retaining real native and wall-shader signatures.
The native HexHighlight root emitter is also identified by its exact same-object
HexSelect_Control; unrelated descendants keep their conservative world facts.
Synchronous card diagnostic work is Debug-gated, bounded across frames and genuinely
deduplicated; blackout keeps native correction/recovery while bypassing repeated
unprintable inventories and impossible bright/translucent candidates. The offline
reader now accepts negative head-height medians, recovering eight pose records without
relaxing measurement exclusions. No render feature, wire layout or asset bank changes.
First-use mip readback and native/prop work remain targets. The current prewarm feeds
already-adopted card widgets, not all party hands or complete mip caches; loading-time
coverage needs the actual native hand skins and must not activate gameplay controllers.
Hardware cadence for608 and a controlled figure-slider FPS benefit remain unverified.

Final integrated validation on runtime commit `06cdfa01` passes **14/14 source suites,
103/103 local suites and 286,760 wire/golden assertions**, strict Release with zero
warnings/errors, five bilingual document pairs and independent suite/log-hash coverage
verification. The complete local run took 689.2 s with eight jobs. Input source hashes
remain identical to the validated tree. Against the preserved reviewed607 assembly,
only the three intended behavior types (card half-tone diagnostics, face blackout and
wall fade) and eight build-constant-only types change; no type/reference is added or
removed. Guard exit1 reflects its historical compiled comparison, not a failed gate.
Real Unity card proofs pass 486 assertions and ten defect controls; wall classifier
proofs pass 19,479 assertions and 21 controls; original-native local/remote figure
proofs pass 184 assertions and 18 controls. Evidence and compiled/source/artifact hashes
are retained in `.planning/debug/frame608-final-validation/`, with the native selector
asset graph and compact worker proofs in `.planning/debug/frame608-wall-ownership-worker/`.
No608 hardware performance or full party-hand prewarm outcome is claimed.

Build607 separates player/enemy/figure-FX/cloth measurement windows at the early
Update seam, retaining OLD settings on completed samples and discarding one mixed
transition frame. Preparation and steady FRAME tags carry revisions; a completed
native figure late pass, closed VR Options and a two-second quiet guard precede
steady collection. Scalar readiness avoids a new per-frame renderer census.
The offline report excludes known mixed/preparing windows and flags unknown legacy
state; actual GPU busy remains unavailable. This does not retroactively establish
the figure-only FPS benefit or justify the Build606 mesh package cost.

The existing WindowMaterialise key is exposed under Graphics → Windows/panels.
Fresh standalone Frame defaults OFF, PC remains ON, and saved choices are preserved.
Live OFF restores active effects and pending native close continuation exactly once;
cards, native window motion and NPC effects keep their separate behavior. This is
a DLL-only update; the complete Build606 mesh banks remain required. The final
integrated gate passes on runtime commit `38eeb1e4`: **14/14 source suites,
102/102 local suites, 286,760 wire/golden assertions**, strict Release with zero
warnings/errors and five bilingual document pairs. The local run took 627.7 s
with eight jobs; independent manifest/log-hash verification passed. The preserved
reviewed606 compiled comparison has seven intended existing behavior/config types,
eight inlined-build-only types and one new measurement helper, with no removal or
reference change. Guard exit 1 reflects those reviewed compiled changes, not a
failed subordinate gate. Proof and source hashes are retained under
`.planning/debug/frame607-final-validation/`. Actual Unity figure readiness has
98 assertions and 25 runtime defect controls; the measurement adapter has 48
assertions and seven controls, and the MR/switch suite has 576 assertions and
seven controls. An independent review additionally removed mixed-frame work-counter
contributions and omitted unfinished native captures at the boundary. Build607
hardware now confirms slider application and remaining hitches; no isolated FPS gain is claimed. See
[Build607 measurements and test procedure](../docs/performance/FRAME-607-MEASUREMENTS.md).

The maintainer approved the Frame605 optimization follow-up and clarified spectator
semantics: unused native flat draws are always suppressed in VR; DesktopMirrorLeftEye
now selects the left eye versus black. Frame defaults start black, saved choices remain.
Native headset menus/captures keep reversible camera ownership. Same-query UI memoization
and a 64-slot signature ring reduce duplicate work without delaying native presentation.

Selection-only native hex visuals no longer trigger a wall-table rebuild. Masked scenery
retains structural facts but skips unused render preparation; held local/remote props rescue
only their actual roots. Pure wall geometry and labels are prepared once with same-frame-only
hierarchy reuse and a final room-change gate. Genuine geometry changes still need the atomic
commit. New reversible settings add small compatible floor draw chunks, simpler floor shading
and positively identified environment-ambience budgets. Native source mesh/material slots,
cloning, visibility and callbacks remain; render masks end with each camera and recover before
native Update if interrupted. A successful MaterialLoaderHeal direct finish also publishes the
preparation edge. Immediate native floor identity plus the original plate-geometry verdict
prevents shader ownership from overlapping wall-dissolve saved arrays.

Figure sliders now use offline native-body derivatives as well as authored LODs. All 1,191
verified derivatives from 409 sources ship in 49 game-format parts; demand preparation and
immutable process-lifetime caches keep first grabs free of generation/I/O. Exact UV, skin,
bindpose and solver exclusions, strong native signatures and live local/remote ghost mesh
refresh preserve animation and 100% restoration. Body/FX diagnostics read actual current
meshes. New quality keys start enabled/reduced on Frame, original on PC; existing values stay.
Install the complete package including the new index/parts, not just a DLL. See
[Frame606 implementation and limits](../docs/performance/FRAME-606-IMPLEMENTATION.md).
Final integrated validation passed on runtime commit `9f267b98`: **14/14 source gates,
101/101 local suites, 286,760 wire/golden assertions**, all three main banks and 49
indexed figure-part headers, strict Release with zero warnings/errors and five bilingual
document pairs. Complete local coverage took 631.5 s with eight jobs; independent report
and log-hash verification passed. Compared with the preserved reviewed605 snapshot,
21 intended existing behavior/config types, eight inlined-build-only types and six new
rendering/patch helpers changed; no artifact or embedded resource was removed. The managed
serialization reference is intentional. Guard summary exit 1 records these reviewed compiled
changes, not failed subordinate gates. Actual Unity proofs pass 185 environment assertions /
19 runtime defect controls plus material-repair binding control, 32 desktop assertions / 11
controls and 82 original-native figure mesh assertions / six controls. The real ZIP verifies
byte-identical index, all 49 parts, main banks and current DLLs, including Frame launcher
permissions/text. Retained source, compiled and archive hashes are in
`.planning/debug/frame606-final-validation/`; compact worker proofs are retained separately.
Hardware appearance and OpenXR spectator pixels remain separate from automated proof.
The supplied Build606 / `8d7b8d7c2` hardware run now confirms 36 applied native-body
derivatives: admitted original/current vertices 278,708/181,529 at both sliders zero
(-34.87%, including inactive LOD slots), exact restoration at 100/100, and cheaper
discarded desktop-camera work. The maintainer reports much smoother, nearly playable
scenario performance. Full loaded means remain 53.28 ms versus the previous 52.67 ms
with different views/settings; figure-only FPS benefit is not isolated. Floor chunks
remain zero for 17 unreadable originals in this scenario. See
[Frame606 hardware analysis](../docs/performance/FRAME-606-ANALYSIS.md).

Historical hardware basis: the Frame605 run confirms deferred decorative creation (287 native
instances / 1,234 renderers) and ambient figure-effect suppression. Loaded, tracked windows
average 52.67 ms, still far from smooth standalone play. Native caps changed, but the original
cached body aggregate did not; nine of 17 actors lacked authored coarse meshes. At that build,
DesktopMirrorLeftEye=false still retained discarded native drawing. That historical switch
behavior is corrected above. See [Frame605 analysis](../docs/performance/FRAME-605-ANALYSIS.md).

The supplied Frame604 / 253e89378 evidence confirms 17 admitted actors, 8/8 native
LOD caps and 15 disabled cloth solvers. Only eight bodies have authored coarse
meshes: lack of obvious visual change does not prove an inert slider. All 3,189
admitted scenery meshes were masked, but original shared forest bays, separate
grass blades and wall attachments still escaped classification. Build 605 expands
positive native coverage, avoids construction of proved purely decorative leaf
instances at zero density, and finishes remaining masking before loading-screen
closure/material reveal. Source prefab bundles remain loaded; floor plates and
actual wall cores remain. A separate configurable figure ambient-effects budget
reduces identified demon idle effects without hiding bodies or combat cues. Home
ghosts follow native evaluated condition poses/phase without copied gameplay
callbacks, and figure glow/depth retain native cutout alpha instead of exposing
rectangular VFX surfaces. Saved settings are retained; fresh Frame FX density is
zero and PC defaults to original. The main bundle changes, so install the complete
new package. See [STEAM-FRAME-SCENERY-605.md](STEAM-FRAME-SCENERY-605.md).
Final integrated validation passed on `909c227c`: **14/14 source gates, 97/97 local
suites, 286,755 wire/golden assertions, all three bundle-format checks**, strict
Release with zero warnings/errors and five bilingual document pairs. Complete
local coverage took 614.955 s with eight jobs; manifest IDs and report/log hashes
were independently verified. Relative to reviewed604, twelve intended existing
behavior/config types, eight build-constant-only types and eight new rendering/
patch helper types were reviewed; no type or embedded resource was removed or
unexpectedly changed. Actual Unity scenery/effects/overlay proofs pass 179/37,
93/21 and 164/12 assertions/negative controls; overlay pixels use original Drake
sleep/fly curves locally and remotely. The final gate supersedes the cancelled
pre-followup run and covers independently completing floor/grass materials.
Evidence is retained at `.planning/debug/frame605-final-validation/`; headset/FPS
acceptance remains separate. Supplied post-load windows of 65.61–88.37 ms do not
predict Build605 performance.

The preceding Build604 integration:

The supplied Frame603 / 5c60f6604 logs confirm the saved zero figure/vegetation
and disabled cloth values. Geometry masking works for 3,277 admitted meshes,
but the figure diagnostics in Player.log explicitly report zero actors/LOD caps
and zero stopped cloth solvers. Native models live under the Game board outside
additive ProcGen; strict geometry-scene equality excluded every actual actor.
Build 604 uses native board/actor identity across scenes and honors detail/cloth
choices while locally/remotely held. Cloth OFF also bypasses rescale cooking and
free-hand probe work at their sources. Native complete tree/trunk and wall plant
coverage is expanded with reversible ownership of only tree-exclusive picking
colliders. Town NPC rigs and cards/UI retain their presentation. Loaded windows
remain 64.76–102.12 ms at 3408 per eye with changing views/settings, not a matched
A/B or evidence of improvement. No new headset result is claimed.
See [STEAM-FRAME-SCENERY-604.md](STEAM-FRAME-SCENERY-604.md).
Final integrated validation passed on `aa003c6d`: **14/14 source gates, 96/96 local
suites, 286,754 wire/golden assertions, all three bundle-format checks**, strict
Release with zero warnings/errors and five bilingual document pairs. Complete
local coverage took 605.4 s with eight jobs; report/log hashes were independently
verified. Actual Unity proofs include scenery 111 assertions/19 negative controls,
figures 53/10 and physical cloth 72/12. Cloth OFF showed no secondary deformation
through 50 moving frames and recovered its simulation on ON. Portable scenery
coverage is 75 assertions/15 negatives; native figure boundaries have 47 assertions.
Compared with the preserved reviewed603 compiled snapshot: six intended behavior/
config types plus eight types differing only in the inlined 603→604 build constant
(including NetProtocol), no added/removed types. Exact source, logs, hashes and
compiled evidence are retained at `.planning/debug/frame604-final-validation/`.
The earlier complete gate was superseded after review identified real named vines
in 17-renderer native tree units; the final gate covers their complete carrier and
mixed-wall boundaries. No config key, patch target or log marker was removed.
Bundles remain the Build602 banks; this checkpoint changes the DLL. Users coming
from Build600 need those updated banks as well. Headset appearance and FPS remain
unverified for 604.

The preceding Build603 integration:

NPC Build 602 was published to origin/dev before this phase. Requested obsolete-run
cleanup freed 399.61 GB net; hardware logs, original/reference assets, all existing
branch refs and final NPC proof were retained. See the gitignored audit at
`.planning/debug/storage-audit-20261002/README.md`.

Latest Frame evidence identifies Build 601, including CampaignMap and ProcGen.
Its zero-decoration budget actually masks 3,457 meshes, but the grass cap coupling
prevents independent comparisons and anonymous structural foliage leaves remain.
Build 603 separates grass/vegetation/dressing, extends original mesh/LOD provenance
and retained collider representation, and adds original native player/enemy mesh
detail, optional scenario-figure cloth and next-load procedural generation controls.
PC keeps original defaults; fresh Frame adopts sparse detail without overwriting
persisted choices. Native gameplay, structural cores, reveal focus, map UI, NPCs
and local/remote cards are retained. Actual headset appearance/FPS remain open.
See [STEAM-FRAME-SCENERY-603.md](STEAM-FRAME-SCENERY-603.md).

Final integrated validation passed: **14/14 source gates, 95/95 local suites,
286,754 wire/golden assertions, all three bundle-format checks**, strict Release
with zero warnings/errors and five bilingual document pairs. Full local coverage
took 590.7 s with eight jobs, and all report/log hashes were independently verified.
The fixture inventory now pins 95 local/71 hosted suites without weakening coverage.
Compared with the reviewed NPC602 compiled snapshot: 18 changed/six added/zero removed
types; seven unrelated network/plugin types differ only in the inlined 602→603 build
constant. The other changes are the ten intended config/localisation/UI/scenery
types plus NetProtocol and six generation/figure driver/patch types. The historical
98fba1a8d guard comparison reports 56 changed/13 added/zero removed, incorporating
the already reviewed NPC phase. No config key, patch target or log marker was removed.
Town bundles are unchanged from 602; users coming from 600 need that updated town bank
as well as the 603 DLL. New hardware results are not implied by automated checks.

The preceding immersive multiplayer checkpoint:
The paired PCVR logs and supplied images identify Build 600. The review fixes
rejected resident transitions, original font/effect dependency failures, first-use
cabinet hierarchy mutation, held-stock loss, cold-page layout handover, movement
pauses, donation timing, speech routing and native purse depth. Shared actor/stand
lighting preserves original material animation and capture constraints. See
[TOWN-MP-602.md](TOWN-MP-602.md) for the complete review and hardware limits.
The town bundle and DLL both change. Final integrated validation passed:
**14/14 source gates, 92/92 local suites, 286,751 wire/golden assertions, all
three bundle-format checks**, strict Release with zero warnings/errors, and
five bilingual document pairs. The full local suite took 588.7 s with eight
jobs. The compiled review against `98fba1a8d` has 46 changed/seven added/zero
removed types, all explained by this review and propagated defaults/build
constants. No config key, patch target or log marker was removed. No new
headset result is claimed. This validated NPC checkpoint was published at `1b033daa`
before the requested storage cleanup and subsequent Frame phase.

The preceding essential-decoration checkpoint:
The Build 600 hardware test confirms the previous grass scope was ineffective:
zero density masked only 27 of 6,560 active renderers; the initial partial
hierarchy scan masked none. Loaded windows remain about 120–125 ms/frame at
changing views and settings, not a matched A/B. The maintainer requests massive
decoration reduction down to essential scenery. Build 601 introduces a broader
generated-decoration control (fresh Frame0%, PC100%) while retaining the old
grass key as an additional cap. Settled loading, late placement and reveal must
all reach discovery. Gameplay geometry, local/remote UI/cards, pickups and doors
remain protected, and hidden decoration must not leave invisible laser blockers.
The new local Unity suite executes the complete production classifier and driver;
portable source/lifecycle checks remain in CI. Actual scene-wide mask counts,
headset appearance and FPS gain are hardware-open. See
[STEAM-FRAME-SCENERY-601.md](STEAM-FRAME-SCENERY-601.md) and
[STEAM-FRAME-THIRTEENTH-HARDWARE.md](STEAM-FRAME-THIRTEENTH-HARDWARE.md).

Build 601 final integrated validation passed: **14/14 source gates, 87/87 local
suites, 286,621 wire/golden assertions, all three bundle-format checks**, and a
strict Release build with zero warnings/errors. The complete local gate ran once
after runtime integration. Production-classifier coverage includes 38 portable
assertions/six negative controls and 50 actual Unity 2021.3.5 assertions/eight
negative controls, including 1,200 late-generated meshes. The compiled comparison
against `7198477bf` contains the intended 17 changed types and one added native
material-readiness notification patch; the extra network types change only through
the inlined ModBuild constant. No config key, patch target or log marker was
removed. Native material completion is observed without changing materials.

A separate read-only asset finding shows that the supplied Standalone Fastest
profile references high Apparance generation. No native generation setting is
changed in this checkpoint; the further reversible geometry lever and reveal
risks are recorded in [STEAM-FRAME-NATIVE-DETAIL-601.md](STEAM-FRAME-NATIVE-DETAIL-601.md).

The preceding Build 600 integration and validation:
The maintainer explicitly authorized visual compromises for large Frame scenarios,
then clarified that all Frame optimizations must be settings available on PC too;
Frame is a different defaults profile, not a forced runtime policy. Build 600
adds a reversible decorative floor-grass density control (PC100%, Frame25%),
switchable shared wall-read and light-work caches, and restores the desktop mirror
toggle on Frame instead of overriding a saved off choice. New-scene Debug census
sampling bypasses an older menu cooldown; broader selected callback timings and
honest residual-span labels support the next hardware comparison. Original game
obstacles, walls, floors, cards, NPCs and local/remote widgets remain outside the
grass budget. Actual qualifying counts, headset appearance and FPS improvement
remain hardware-open. See [STEAM-FRAME-SCENERY-600.md](STEAM-FRAME-SCENERY-600.md).
Final local validation passed: 14/14 source gates, 86/86 local suites, 286,620
wire/golden assertions, all three bundle-format checks and a Release build with
zero warnings/errors. The compiled-form review reports the intended 24 changed
types and six added types; propagated network changes are only the Build 600
constant. No config key, patch target or log marker was removed. Headset gains
and the loaded scene's actual eligible grass population remain unverified.

The new Build 599 run confirms severe tracked scenario cost at 3408 pixels/eye:
complete post-load windows average 82.85 and 117.64 ms/frame at different views.
The nine selected native callbacks cost about 1.13 ms in the final sample and
do not explain the CPU wall. Their Debug summaries exist in the matching
`Player.log`, while `LogOutput.log` filters them. Unity draw counters expose only
zeros, and the menu census cooldown suppresses the scenario's detailed census.
No actual GPU busy time or matched Build 598/599 improvement is established.
Wall-table commits still cause 254–292 ms gameplay hitches. That preceding
Build 599 evidence review changed documentation only and did not rerun unchanged
runtime tests. See
[STEAM-FRAME-TWELFTH-HARDWARE.md](STEAM-FRAME-TWELFTH-HARDWARE.md).

The latest Build 598 Frame log is a direct `ProcGen` scenario run, with no 3D-map
test. Its apparent ~36 ms interval had an untracked HMD and seven visible
renderers. At stable 3408 pixels/eye the later scenario windows remain around
128–131 ms/frame. A wall-fade-off trial did not materially improve the owner's
experience; the log's brief off interval supports lower named mod time but
cannot yield a clean exact A/B because VR Options was open. Build 599 adds
bounded Debug-only native callback and actual Unity render-counter probes
(explicit `n/a` if the player exposes none), plus a scene/tracking/eye-aware
log comparison script. No headset speedup is claimed. See
[STEAM-FRAME-LARGE-SCENE-PERF.md](STEAM-FRAME-LARGE-SCENE-PERF.md).

The release ZIP now exposes only `BepInEx/` and the English/German install
texts at game-root level. The Steam Frame desktop starter, setup scripts and
art are nested under the plugin. The in-game updater accepts and copies the
desktop starter; successful upgrades remove six obsolete root helpers. The repository
install guides moved to `docs/install/`. The archive and updater have focused
local coverage. See
[STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).
The maintainer now confirms the separate `GloomhavenVR` Library entry and its
artwork on Frame. SteamVR still opens a dashboard requiring Resume Game, and
the active game's panel is the original AppID with a gray cover. The shortcut
continues to launch AppID 780290 for Steamworks identity; native VR launch
classification needs publisher Steamworks metadata. The locally testable
theater/dashboard and app-key options, with their limitations, are in
[STEAM-FRAME-LAUNCH-PRESENTATION.md](STEAM-FRAME-LAUNCH-PRESENTATION.md).
The complete immersive NPC work through Build 583 and the Steam Frame changes
through dev Build 559 now share the sole `dev` integration branch. Future NPC and
Frame work belongs on `dev`. The NPC presentation remains a two-client hardware
candidate, not a headset-confirmed result: see [TOWN-MP-583.md](TOWN-MP-583.md).
Town cloth interaction was retired in Build 582 pending a reliable runtime
design; the authored cloth remains static. The merchant's visible hand-to-belly
gap remains open. The first Frame run reached a scenario, but the native exit
after increasing eye resolution is not root-caused; see
[STEAM-FRAME-FIRST-HARDWARE.md](STEAM-FRAME-FIRST-HARDWARE.md). The original Frame
controller model and render foveation also remain unimplemented; feasibility is
recorded in [STEAM-FRAME.md](STEAM-FRAME.md) and
[STEAM-FRAME-FOVEATION.md](STEAM-FRAME-FOVEATION.md).
The second Frame run found little frame-time improvement below eye scale 1.00;
the maintainer reports unacceptable image quality there. Build 585 removes
source-proven CPU work and adds hand-step attribution. See
[STEAM-FRAME-SECOND-HARDWARE.md](STEAM-FRAME-SECOND-HARDWARE.md).
The Build 585 logs confirm substantial main-thread cost on the map and in a
scenario even at the `Fastest` quality preset; its scenario logic median alone
exceeds the 72 Hz frame budget. The later screenshots show about 17 FPS for
both Steam Frame overlay counters on the map, and the late logs put about
40 ms/frame in main-thread logic. The recording switch is absent on this
headset, so the two snapshots do not isolate GPU busy time. See
[STEAM-FRAME-THIRD-HARDWARE.md](STEAM-FRAME-THIRD-HARDWARE.md) and
[STEAM-FRAME-FOURTH-HARDWARE.md](STEAM-FRAME-FOURTH-HARDWARE.md).
The Build 586 run at 1728x1728 per eye gives roughly the same late-map
59-62 ms/frame as Build 585 at 3408x3408, despite 74% fewer submitted pixels;
the measured map work remains predominantly main-thread logic. Keep 3408 fixed
for the Build 587 CPU optimization comparison. Build 587 reduces hidden merchant
page work and adds bounded Debug attribution of the remaining town, veil and
modal-conversion cost; its headset gain remains unmeasured. See
[STEAM-FRAME-FIFTH-HARDWARE.md](STEAM-FRAME-FIFTH-HARDWARE.md).
Build 587 was pushed separately so the maintainer can measure its CPU changes
at 3408 per eye. Build 588 adds a separate Frame launcher and opt-in gate;
the original Steam entry remains flat only after the Frame helper creates its
marker. Steam Game Mode argument forwarding and prelaunch VR-settings visibility
remain hardware checks. See [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).
The Build 592 run at 3408 per eye shows a 2.65 s merchant catalog build,
roughly 6 ms/frame of recurring merchant work on the map, and 90–103 ms
scenario wall rescans. Menu and scenario also have long stalls not attributable
to named mod scopes; the XR GPU counter is not usable as busy time. See
[STEAM-FRAME-SIXTH-HARDWARE.md](STEAM-FRAME-SIXTH-HARDWARE.md).
Build 593 uses separate fresh Frame mod defaults while retaining existing
per-player values and the native game's saved graphics quality. The Frame
setup suggests 3408 pixels per eye via Valve's game-root `vrpreferences.json`;
SteamVR's user override remains authoritative. The persistent merchant
catalog now avoids repeated quadratic row searches. The enchantress's hidden
native card list no longer reparents its 549-element pool, although the game's
controller and callbacks still initialize those widgets. The selected map hand
prepares the same card clones it later reveals during the loading phase;
activation and final fitting keep their original order. Additional Debug
scopes isolate the remaining priestess/enchantress entry and fan reveal costs.
The Build 593 headset run confirms 10/10 then 9/9 map card fronts prewarmed
before reveal, but map hand activation/mip work, merchant catalog refresh and
first NPC approaches still produce interactive hitches; scenario pacing remains
roughly 41–52 ms median. Its screenshot shows the native flat gamepad popup and
desktop UI in SteamVR's application panel. The intended one-second wall setting
is not effective in this trace (live rescan 4.00 s; visibility evaluation 0.250 s).
See [STEAM-FRAME-DEFAULTS.md](STEAM-FRAME-DEFAULTS.md) and
[STEAM-FRAME-SEVENTH-HARDWARE.md](STEAM-FRAME-SEVENTH-HARDWARE.md).
The Build 594 Debug trace confirms that changing "Verdeckung prüfen alle" to
about 1 s saved the value but never altered the 0.25 s effective cadence;
the option omitted its actual BepInEx range. Build 595 makes the displayed,
saved and effective range agree. The same trace retains 42 ms scenario medians,
recurring merchant CPU work and interactive NPC/fan/wall hitches. See
[STEAM-FRAME-EIGHTH-HARDWARE.md](STEAM-FRAME-EIGHTH-HARDWARE.md) for measured
priorities and explicitly optional Frame compromises.
Build 596 moves avoidable presentation work out of interactive frames on all
platforms: dormant windows, merchant cold pages, parked card mip cache and
inactive enhancement-slot wrappers. Remote public map fans now pin class art
before their first face; scenario secrecy is unchanged. Pure wall provenance
checks are reused within one atomic commit, and expensive forensic scene
censuses default off everywhere. A capped Debug-only phase probe helps isolate
remaining native scenario CPU. Fresh Frame graphics defaults already use
MSAA 0, a smaller texture-streaming floor and slower wall rescans; existing
settings are retained, and no further resolution/legibility reduction is
applied without a matched hardware result. See
[STEAM-FRAME-NINTH-HARDWARE.md](STEAM-FRAME-NINTH-HARDWARE.md).
The Build 596 Frame run found a 487.82 ms first priestess visit (139.62 ms
inside the book-inscription geometry), a failed first enchantress entry after
inactive native slot-pool warmup, and a temple blessing visual that outlived
the native donation callback. Steady map/town logic was still roughly
36–42 ms/frame; the merchant cabinet continued spending 4.35–4.86 ms/frame
on cards. Some 0.35 s window appearances rendered in only one to four frames.
Build 597 removes the unsafe native pool warmup, indexes original temple page
triangles, allows the blessing visuals to finish before the bowl-cover pose,
skips the fully hidden merchant cassette page, and bounds window animation
progress per rendered frame. Re-enabling immersive NPCs displays the loading
indicator through resident and public-stock preparation. These changes are
source-level changes whose overall frame-time effect was not confirmed by the
subsequent headset run. See
[STEAM-FRAME-TENTH-HARDWARE.md](STEAM-FRAME-TENTH-HARDWARE.md).
The Build 597 run with a large immediately revealed scenario reached 12 Hz and
showed 282–385 ms atomic wall-table commits, recurring water/material scans,
and 51–62 ms average map frames. Build 598 removes quadratic wall refresh
membership checks, makes unchanged water-tile discovery event-driven, spreads
the complete material watchdog pass over bounded frames, and avoids redundant
hidden icon/cabinet and unchanged NPC setters on the 3D map. Remaining wall
commit phases and native/runtime frame cost are still substantial; hardware
improvement is unverified. See
[STEAM-FRAME-ELEVENTH-HARDWARE.md](STEAM-FRAME-ELEVENTH-HARDWARE.md).
Build 589 addresses the Build 587 town multiplayer report: host self-grant
timeout, original atlas capture, public cabinet input, town handoff focus and
map character selection. Correct matching Build 587 peer logs then exposed a
repeated remote rack crash and independent handoff gates; Build 590 repairs
those source paths. The offered-card world pose and one original-template
structure mismatch remain headset-open. See [TOWN-MP-590.md](TOWN-MP-590.md)
and [TOWN-MP-589.md](TOWN-MP-589.md).
Build 591 retains the selected owned character through native NPC mode changes
and adds a default-hidden +100 gold test action. The initial conversion of the
enchantress's 549-element original inventory is cheaper, but the measured
110–148 ms Visit hitch is **not yet proven resolved**; new scopes separate
native opening and VR conversion for the next Debug hardware run. See
[TOWN-591.md](TOWN-591.md).

The file this replaces had gone 168 builds
stale while still saying "read this first"; it is kept as `STATE-ARCHIVE-through-2026-08.md` for
its round-by-round narrative and for nothing else.

**Read in this order.** `CLAUDE.md` (rules, gates, working practice — the part that does not
change per build) → this file (where things stand and what is owed) → the build-note block above
`ModBuild` in `src/GloomhavenVR/Net/NetProtocol.cs`, newest first (what happened, per build) →
`.planning/INDEX.md` (which planning records are current or historical).

---

## 1. Position

- **dev / 1.1.0 / ModBuild 600 (configurable Frame candidate):** reduce verified
  standalone floor-grass render submission by a stable user-selected density;
  remove repeated shared wall/material and unchanged light work with explicit
  bypass settings. Frame seeds defaults; PC can reproduce them, and Frame can
  disable them. The former forced/hidden Frame mirror has a live setting again.
  Debug census/probes cover the real loaded scenario with unchanged old-log
  parser support. This is source-proven implementation, not hardware acceptance.

- **dev / 1.1.0 / ModBuild 599 (large-scene attribution):** separate the
  native game loop, Unity render submissions and actual GPU time before
  changing procedurally revealed scene geometry. The Build 598 test contains
  no 3D-map run; resolution-changing windows are not valid A/B comparisons.
  Debug probes and the log report are bounded and read-only. The hardware
  bottleneck and a playable Frame rate remain open.
- **dev / 1.1.0 / ModBuild 598 (large-scene Frame candidate):** remove quadratic
  wall-refresh membership checks, use placement events for unchanged water tiles,
  bound material-watchdog work per frame, and reduce redundant hidden-icon,
  merchant-cabinet and NPC work on the 3D map. The headset gain and remaining
  atomic wall phases require a matched hardware test.
- **dev / 1.1.0 / ModBuild 596 (interactive CPU candidate):** global lossless
  scheduling and prewarm target the measured merchant, window, map-hand and
  enchantress costs. Original 2D/3D map windows keep their native paths;
  multiplayer map fronts and remote privacy gates remain intact. Frame-only
  quality compromises remain the previously chosen fresh defaults. The wall
  commit still contains necessary atomic scene work; scenario residual CPU is
  instrumented, not yet optimized away. Headset frame-time improvements and
  1:1 visual behavior need a matched local/remote test. See
  [STEAM-FRAME-NINTH-HARDWARE.md](STEAM-FRAME-NINTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 595 (Frame options correction):** the wall occlusion
  evaluation control is bounded to its already-enforced 0..0.25 s range in
  BepInEx and VR Options. Persisted values around 1 s now display their actual
  effective value instead of implying the runtime changed. The 4.00 s wall
  table rebuild is a different setting and remains unchanged. Build 594
  performance findings and next candidates are documented in
  [STEAM-FRAME-EIGHTH-HARDWARE.md](STEAM-FRAME-EIGHTH-HARDWARE.md). Headset
  smoothness remains open.

- **dev / 1.1.0 / ModBuild 594 (Frame hardware candidate):** restore the
  enchantress's original enhancement-point heading above its physical book
  after the discarded flat list is veiled. The post-load Character UI layer
  sweep now indexes captured transforms by identity rather than searching an
  ordered ledger for each of 2660–3050 newly pooled objects. The persistent
  merchant catalog refreshes warm/visible rows during its recurring census;
  cold rows are refreshed before their first exposed frame, including a page
  or category change. Unchanged physical cards avoid redundant hierarchy,
  camera and renderer reads. Frame VR forces the existing discarded-desktop
  camera sink even when a saved PC mirror preference is off, and suppresses
  the native flat-only gamepad connection popup. PC and flat launches keep
  their settings and native behavior. Debug logs now identify actual edits
  to the three wall-cadence controls and the effective live rates. Build 593
  traces show 4.00 s rescan / 0.250 s evaluation throughout the scenario,
  but no setting-edit value is logged there; the owner's intended 1 s cannot
  be attributed to a particular control from that run. Full headset speed,
  enchantress points, and SteamVR theater presentation remain to be checked.
  See [STEAM-FRAME-SEVENTH-HARDWARE.md](STEAM-FRAME-SEVENTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 593 (Frame hardware candidate):** fresh standalone
  mod defaults mirror the Build 592 test, preserving existing BepInEx and
  native game settings; the Frame installer suggests 3408 pixels per eye through
  `vrpreferences.json` without replacing an existing SteamVR choice. The
  merchant catalog's row reconciliation and item-count lookup are linear.
  The invisible native enhancement list avoids a full-subtree reparent on
  entry while its game controllers stay active. The selected map hand prepares
  original face clones during loading and retains a synchronous early-open
  fallback. Bounded Debug attribution separates residual NPC and fan work.
  SteamVR dashboard focus and its in-game gray original-AppID panel remain
  unresolved; no global theater setting was changed. See
  [STEAM-FRAME-SIXTH-HARDWARE.md](STEAM-FRAME-SIXTH-HARDWARE.md) and
  [STEAM-FRAME-LAUNCH-PRESENTATION.md](STEAM-FRAME-LAUNCH-PRESENTATION.md).

- **dev / 1.1.0 / ModBuild 592 (archive layout candidate):** root-level
  release clutter is removed without changing the plugin/patcher install paths.
  The Frame desktop starter is nested under the plugin so the published 1.0.8
  updater accepts the new ZIP. Setup and successful updates retire only the six
  owned legacy root files. The Frame
  installer still needs a headset check, while synthetic Steam setup, archive
  and self-update tests cover the paths and allowlist. No gameplay, wire format
  or asset-bundle content changed. A packaging-only follow-up normalizes Windows
  ZIP path separators before applying Unix launcher modes, after a Windows-built
  archive exposed flattened launcher filenames. See
  [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).

- **dev / 1.1.0 / ModBuild 591 (hardware candidate):** scoped native map-option
  transitions no longer clear the locally owned selected portrait and force a
  visible reselect. The default-hidden Cheats page can add 100 gold to the
  selected map character or shared Guildmaster purse through native rules and
  save. A measured quadratic initial layer-restore search was removed; residual
  enchantress entry time remains a headset question, now split into native
  opening and original-widget conversion. Validation: 14/14 source gates;
  79/80 passing in the full runtime run and the sole failed harness repaired
  and rerun successfully (all 80 suites have passing evidence); 286,609 wire
  assertions, strict Release 0/0, bundle/surface and EN/DE docs passed. The
  one-shot guard itself did not reach compiled-form comparison after that
  fixture failure. See [TOWN-591.md](TOWN-591.md).

- **dev / 1.1.0 / ModBuild 590 (corrected peer-log follow-up):** destroyed
  remote town-service modules now rebuild instead of aborting `ApplyPending`.
  Enchantress attention and usable card cue agree in the merchant overlap;
  visiting the priestess after an idle Merchant/Enchantress destination no
  longer requires a purse that is not yet available. A guarded template
  mismatch now reports its actual address and structural difference. The
  original Build 587 peer trace was supplied after Build 589 and is analyzed
  in [TOWN-MP-590.md](TOWN-MP-590.md). Integrated validation passed 14/14
  source checks, 80/80 runtime suites, 286609 wire assertions, strict Release
  build and EN/DE docs. Headset parity remains open.

- **dev / 1.1.0 / ModBuild 589 (town multiplayer repair candidate):** the
  merchant remains immersive across transaction failures; valid host grants
  no longer expire as unanswered. Original item art/price atlas capture is
  resolved by verified native descriptors, and public cabinet categories can
  be used without an assigned character or a free transaction lease. Remote
  merchant decision facing interpolates over its sample period. Map ownership,
  enchantress handoff and temple purse focus no longer depend on stale
  selection or unrelated gates. The integrated gate passed 14 source checks,
  80 runtime suites, 286609 wire assertions, the strict Release build and
  EN/DE docs validation. The headset result remains open. See
  [TOWN-MP-589.md](TOWN-MP-589.md).

- **dev / 1.1.0 / ModBuild 588 (Frame launcher candidate):** a Frame-only
  install helper creates a local `GloomhavenVR` Steam shortcut with the existing
  mod logo/icon and a marker requiring the exact `--gloomhavenvr` launch
  argument. Both boot stages refuse VR before touching XR or gameplay when the
  original Steam entry omits that argument. The shortcut forwards to original
  AppID 780290 to preserve its Proton prefix and saves; Windows PC installs
  have no marker and retain their established startup. The separate library
  entry does not alter original Steamworks VR metadata. Integrated validation
  passed 14 source checks, all 80 runtime suites, 286609 wire assertions,
  strict Release build, release-package layout and EN/DE documentation checks.
  The compiled-form difference against Build 587 is the opt-in gate plus
  expected build constants. Steam Game Mode argument forwarding and prelaunch
  app settings are hardware-open. See
  [STEAM-FRAME-SHORTCUT.md](STEAM-FRAME-SHORTCUT.md).

- **dev / 1.1.0 / ModBuild 587 (Frame CPU candidate):** hidden merchant pages
  defer their native price and body-material mirrors until the first exposed
  frame, before local render and multiplayer publication. Stable merchant
  observer election no longer repeats body-visibility checks. Hidden-window
  veil discovery remains complete every frame; one impossible hierarchy test
  is removed. Bounded Debug timing splits town visits, public catalog,
  observer state, veil discovery/reassert and slow modal-conversion stages.
  No UI timing, gameplay, wire or asset change. The integrated 14 source
  checkers, all 80 runtime suites, strict Release build and EN/DE docs check
  passed. A compiled-form comparison against Build 586 contains only the
  reviewed town, veil and modal changes plus the build/branch constants.
  Headset performance remains unmeasured. See
  [STEAM-FRAME-FIFTH-HARDWARE.md](STEAM-FRAME-FIFTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 586 (Frame CPU candidate):** reuse the wall-cache
  material snapshot, avoid repeated hidden-veil component lookups, apply town
  catalog visibility once per frame, and skip unchanged panel-capture property
  writes. These are source-proven reductions on measured hot paths, not a
  measured headset speedup. No wire or asset change. The Build 585 map
  screenshots show about 17 FPS with severe frame-time peaks, and the log
  attributes about 40 ms/frame to main-thread logic in matching late windows.
  See [STEAM-FRAME-FOURTH-HARDWARE.md](STEAM-FRAME-FOURTH-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 585 (Frame CPU candidate):** reduce redundant
  canvas-flatten, town-resident/catalog/token and wall-cache work without
  changing visual cadence or interaction order. Split the hand timing by pose
  and interactor to identify its next bottleneck. Build 584 hardware evidence
  shows similar scenario frame p50 at eye scale 0.80 and 1.00 despite lower
  picture quality at 0.80; Valve GPU time and Build 585 headset speedup remain
  unmeasured. See [STEAM-FRAME-SECOND-HARDWARE.md](STEAM-FRAME-SECOND-HARDWARE.md).

- **dev / 1.1.0 / ModBuild 584 (hardware candidate):** merge all NPC work through
  Build 583 with dev's Build 559 Frame changes. The player can test both on one
  build, and all future development integrates into `dev`. Both town bundles
  must accompany the DLL. The native Frame resolution exit and the NPC headset
  presentation remain open hardware checks. Integration validation passed
  14 source checks, 80 runtime suites, 286600 wire assertions, strict Release
  build and a complete package containing both town bundles. The compiled-form
  comparison against pre-NPC dev reports the expected new town types.

- **Integrated dev / 1.0.9 / ModBuild 559:** late additive main-menu loading
  re-arms bounded VR Options discovery; the eye-reach scene sweep is Debug-only
  after a 187.3 ms Frame measurement. Distinct live eye-resolution requests log
  before XR texture reallocation. Bilingual Frame installation is documented.
  See [STEAM-FRAME-FIRST-HARDWARE.md](STEAM-FRAME-FIRST-HARDWARE.md).

- **Integrated dev / 1.0.9 / ModBuild 558:** the tutorial distinguishes an
  explicitly identified Frame from SteamVR Touch emulation. The real controller
  model and render foveation are still future work.

- **NPC feature / 1.1.0 / ModBuild 583 (hardware candidate):** merchant
  original-card capture accepts observed shader-property counts and resolves
  the small texture identity collisions recorded on both peers. The shared
  cabinet remains public, while each visitor's held item and item fan may be
  mirrored independently of the stand's elected author. Pickup speech works
  through the merchant's gaze range. The enchantress's native handoff range now
  agrees with her gaze range; her offered hand follows visitor attention even
  before a cue is available. Only the three permanent resident stands remain;
  visiting a second NPC cannot spawn a fourth table. The current hardware
  evidence is build 582, so these build-583 outcomes still need a two-client
  headset comparison. See [TOWN-MP-583.md](TOWN-MP-583.md).

- **NPC feature / 1.1.0 / ModBuild 582 (hardware candidate):** cloth meshes remain
  static while their solver, hand interaction and publication/replay are removed;
  historic TLV90 decoding remains for compatibility. Merchant gaze greetings now
  use a voice channel independent of other residents. Every temple visitor can
  take and display their own purse; only valid native donation may place it in
  the bowl. The original immersive setting is a default-on Environment toggle
  that disappears with all NPC-specific audio options under the 2D map. Build
  580 logs do not verify these new headset outcomes.

- **NPC feature / 1.1.0 / ModBuild 581 (hardware candidate):** merchant speech
  follows the face's visitor target ahead of delayed coin-hand attention;
  enchantress approach switches idle resident destinations and resolves the
  narrow temple overlap; active transparent story/close targets once again
  receive laser input. The last two fixes address a scenario progression
  deadlock and a card handoff deadlock observed in Build 580. Independent
  per-resident host grants allow simultaneous visits to different NPCs while
  serializing offers at the same one. Automated checks
  verify the intended gates; headset behavior remains to be confirmed. See
  [TOWN-581.md](TOWN-581.md). The merchant hand/coat asset remains open.

- **NPC feature / 1.1.0 / ModBuild 580 (partial hardware candidate):** the elected
  face author greets a visitor when the merchant first turns to look at them,
  including consistent behavior after a multiplayer author handover. The
  priestess's build-579 staggered arm clocks have been removed in favor of a
  continuous prayer release and return. Native town cloth stays visibly
  deformed near a tracked hand and protects its sparse triangle interiors
  against hand passage; local/peer identity changes, withdrawal and station
  visibility reset its contact state. The merchant's attended hands still hover
  above the coat: the imported-skin pose candidate was rejected after a full
  motion scan found visible sleeve/coat intersections. Source and executable
  Unity evidence, together with remaining headset limits, are in
  [TOWN-580.md](TOWN-580.md).

- **NPC feature / 1.1.0 / ModBuild 579 (partial hardware candidate):** a
  visitor who enters the enchantress's area while another native town service
  is active no longer loses the only approach event. Local and remote cloth
  use each rendered hand's wrist and index-tip anchors, with contact across
  triangle interiors. The priestess releases prayer with separate arm phases;
  portable motion tests cover the intermediate visit. The merchant hands
  still visibly hover above the belly in build-578 hardware. Attempts to
  close that gap caused real skin intersections and were rejected; no merchant
  pose correction is claimed in this build. Successful generated Town suite
  runs now retain one marked result per suite. See [TOWN-579.md](TOWN-579.md).

- **NPC feature / 1.1.0 / ModBuild 578 (hardware candidate):** the offered
  enchantment card itself stops the laser while only original enhancement
  areas respond to selection. Its pulsing aura retains full side bounds.
  The priestess's prayer-to-neutral arm motion and the merchant's resting
  hands were retested against the imported skins and rendered at
  intermediate frames. Build-578 hardware subsequently disproved the claim
  that the merchant's hands visibly touch the belly. Dev ModBuild 557 contributes native character-creator
  fit and figure-proportional overhead bars with working wall visibility.
  Evidence and headset limits are in [TOWN-578.md](TOWN-578.md).

- **Integrated dev / 1.0.9 / ModBuild 557:** character creation joins the
  original character-screen fit; overhead bars honor the wall-visibility
  setting and, by default, scale proportionally with figures. The old zoom
  clamp remains a selectable setting. These source-backed corrections await
  headset confirmation; see [BARS-CREATOR-557.md](BARS-CREATOR-557.md).

- **NPC feature / 1.1.0 / ModBuild 577 (hardware candidate):** merchant offers
  keep a native confirmation throughout inspection, rebind lost controls and
  return cards when no native prompt can open. The offered enchantress card no
  longer blocks the laser from its own original enhancement areas, while all
  other physical occluders remain in force; its native ring stays round under
  rotated, nonuniform transforms. The priestess's entry blend avoids the prior
  elbow detour. The enchantress's cloth starts clear of its furniture root and
  holds smooth physical contact. Full-install art bundle and source/visual
  evidence are recorded in [TOWN-577.md](TOWN-577.md). Headset confirmation
  remains open.

- **NPC feature / 1.1.0 / ModBuild 576 (hardware candidate):** native saved
  headquarters unlocks and first-map tutorial gates determine whether each
  complete resident and stand exists. The immersive campaign's flat-only
  merchant onboarding steps resolve via native tutorial callbacks; other modes
  and setting-OFF retain their own flow. Merchant cassette/cards sit within the
  cabinet, and sold-out stamps clear when lifted. The enchantress shows a
  persistent neutral offer locator while native input initializes, fits the
  original aura and selectable areas to the physical card, and returns to the
  original usable VR window after a prolonged offer stall. Normal English
  replies and whispered spells now share one voice. Evidence and remaining
  headset checks are in [TOWN-576.md](TOWN-576.md).

- **NPC feature / 1.1.0 / ModBuild 575 (hardware candidate):** the merchant's
  Cancel click immediately returns the offered card; stock and owned cards can
  replace one another with release feedback while cabinet controls remain usable.
  Cabinet stock sits deeper, sold-out cards display a localized marker, and
  unsuccessful stock inspection has contextual shared speech. The enchantress's
  original aura spans the physical card, original enhancement areas accept a
  physical grip poke, and the card no longer responds to laser pickup. Incidental
  speech is less frequent. Full-quality town art and voice assets ship in two
  separately loaded bundles. The priestess shoulder and cloth motion receive a
  further rendered/solver review. Evidence and headset checks are in
  [TOWN-575.md](TOWN-575.md).

- **NPC feature / 1.1.0 / ModBuild 574 (hardware candidate):** immediate merchant
  buy/sell reuse preserves the native confirmation buttons. The actual Unity Cloth
  solver initializes before hand contact. Imported-skin tests now scan every animated
  priestess/merchant arm frame, correcting the crossing and thumb penetration missed
  by static markers. Unconnected cabinet and enchantress stand geometry is removed.
  Original enchantment-area buttons take laser selection over the offered card's
  own reclaim collider, and the native full-card effect covers the card instead of
  collapsing into a strip. Five contextual post-offer lines replace stale card
  invitations and follow the existing shared speech channel. Evidence and headset
  checks are in [TOWN-574.md](TOWN-574.md).

- **NPC feature / 1.1.0 / ModBuild 573 (hardware candidate):** merchant confirmations
  recover after a native window closes without updating its active wrapper; stock-to-owned
  replacement retains its card through transient tab changes. The red cabinet side appendage
  is gone and physical controls align with the imported sculpt. The priestess's shoulder and
  bowl path are corrected at the actual rig. The enchantress shows one card under the game's
  original selectable enhancement areas and speaks on native visits or accepted offers.
  Evidence and headset checks are in [TOWN-573.md](TOWN-573.md).

- **NPC feature / 1.1.0 / ModBuild 572 (hardware candidate):** the merchant's side cloth
  responds to finger contact and shares its movement with observers. Cabinet legs, braces,
  category controls and lantern mount align with the rebuilt shell. The priestess keeps her
  arms uncrossed while covering the bowl and blends into and out of that pose. The enchantress
  previews an opening handoff on first approach; valid merchant, enchantress and temple offers
  give stronger visual and bounded haptic feedback near their release volumes. Town art preloads
  asynchronously while the menu is open, avoiding the synchronous first-prefab bundle load;
  the remaining station construction cost awaits headset measurement. Source-grounded
  evidence and headset checks are in [TOWN-572.md](TOWN-572.md).

- **NPC feature / 1.1.0 / ModBuild 571 (hardware candidate):** a brief real cloth contact
  preserves the solver snapshot from approach so its first visible displacement is no longer
  re-zeroed. The priestess no longer speaks the unavailable response immediately after a
  committed donation; she performs a synchronized blessing and blends through the corrected
  prayer, attention and cover poses. The enchantress can finish opening after a brief native
  block, accepts either physical release, and exposes her original card-dependent options only
  while holding an offered card. The merchant cabinet is re-authored as a carved PBR shell with
  matching physical controls and aligned interaction anchors. Evidence and headset checks are in
  [TOWN-571.md](TOWN-571.md).

- **NPC feature / 1.1.0 / ModBuild 570 (hardware candidate):** merchant item widgets now leave
  the physical fan before their host is destroyed and restore every native face transform on
  creation, reclaim and maintenance. Real cloth contact presents the native solver immediately;
  only release retains a smooth recovery. An available priestess lets her arms hang beside the
  robe, while a known unavailable visit moves directly from prayer to the synchronized covered-bowl
  pose. Merchant and enchantress lines use the accepted high-quality speech renderer; casting uses
  five quiet invented incantations with five deterministic restrained effects. The complete native
  merchant lantern hook meets the cabinet ring. Evidence and the headset checklist are in
  [TOWN-570.md](TOWN-570.md).

- **NPC feature / 1.1.0 / ModBuild 569 (hardware candidate):** the merchant now claims and
  restores detached native child windows before generic modal conversion, keeping the item list
  behind the physical resident hidden without bypassing native transaction callbacks. Reclaimed
  item cards release stale renderer veils and independently restore fan parent, position, rotation
  and scale across repeated grab/release, close/reopen and replacement/cancel paths. Cloth contact
  follows the current simulated sheet rather than its former rest location. Production-bundle
  renders verify revised priestess shoulder/elbow anatomy and merchant thumb clearance. Merchant
  buy and sell prompts use their own synchronized voice families; the unavailable priestess has
  five synchronized explanations. At the native point of no return every resident smoothly returns
  to neutral, drops interaction and immediately silences active and queued speech for all peers.
  Evidence and the headset checklist are in [TOWN-569.md](TOWN-569.md).

- **NPC feature / 1.1.0 / ModBuild 568 (hardware candidate):** the permanent temple
  resident alone owns the blessing particle system, whose only live trigger is a later committed
  donation revision in the same owner/session. Close/reopen and hydration preserve their baseline,
  and smaller additive motes originate in the bowl. Priestess cover availability now blends out
  continuously rather than snapping when the temporary interaction record disappears; imported-rig
  tests measure every 90 Hz intermediate hand and elbow frame. Immersive service controllers are
  claimed before generic modal conversion, closed confirmations stay masked through render
  retirement, and a renderer reparented out of a hidden window is released immediately. This removes
  the filmed full native confirmation panel, its measured conversion spike and the stale veil that
  made returned item fronts grey. The merchant lantern is restored to the cabinet's exterior bracket
  with complete side-wall clearance. Evidence and the headset checklist are in
  [TOWN-568.md](TOWN-568.md).

- **NPC feature / 1.1.0 / ModBuild 567 (hardware candidate):** the recurring spatial
  open/close sound was the flat equipment-toggle clip reused as invented resident cloth foley on
  approach/departure attention thresholds; that path is removed while coin, spell and voice cues
  remain. Debug-only bounded traces identify any native town audio request. The purse appears only
  through the ordinary card-fan wrist gesture, including when donation is unavailable, while its
  interaction stays disabled. A seeded particle system replaces the polygon blessing. The actual
  production cloth class now measures capsule-to-sheet contact and preserves its visual zero, with
  measured contact response and complete recovery. Imported priestess shoulder origins, hip pose,
  palms-down bowl cover, cover exit and candle clearance were revalidated against the real bundle.
  Evidence and the headset checklist are in [TOWN-567.md](TOWN-567.md).

- **NPC feature / 1.1.0 / ModBuild 566 (hardware candidate):** automatic resident
  departure keeps the native mode transition but omits synthetic pointer audio. Temple entry
  preserves the exact selected party slot and no longer rebuilds an invisible primary workspace
  with two Cloth solvers; measured repeat entry is sub-millisecond in the Unity harness. The purse
  remains visible as status information independently of transaction eligibility. Actual imported
  skins now use accepted merchant/priestess hip silhouettes and a continuous bowl-cover return.
  Scale-correct gravity, bounded freedom, continuous supports and episode-relative rendering keep
  the real cloth responsive without table inversion, re-entry pops or solver reconstruction.
  Complete bounds separate all priestess candles from lanterns, book and bowl. Evidence and the
  headset checklist are in [TOWN-566.md](TOWN-566.md).

- **NPC feature / 1.1.0 / ModBuild 565 (hardware candidate):** repeated town-service
  visits resolve local input through the current wire generation, keeping the temple purse
  and original book visible after visiting another resident. Imported-model pose checks put
  merchant and priestess hands at their waists and turn the unavailable temple palms down
  through a continuous blend. The real 25x13 runner surface is reconstructed independently
  of FBX vertex order and simulated without inherited 19,800x scale, with per-column table
  support and real index-tip collision. Native flat window show/hide cues are suppressed from
  the first immersive call, priestess speech has measured loudness compensation, and the
  enchantress lamp uses its visible renderer footprint on the real workbench. Evidence and
  headset limits are in [TOWN-565.md](TOWN-565.md).

- **NPC feature / 1.1.0 / ModBuild 564 (hardware candidate):** each resident has
  one sticky, timeout-bounded multiplayer visitor lease. The elected visitor alone
  can interact and author the resident, while all peers receive the same station,
  pose, speech, offering and confirmation presentation. Temple eligibility and one
  blessing revision use additive inner town-service TLV91 without gameplay identity.
  The donation guide is blue and translucent, the real purse remains upright and
  snaps exactly into it, and an unavailable ritual hides the guide while the
  priestess covers the bowl. Merchant and priestess use reachable anatomical hip
  poses; merchant and enchantress card palms swap valid cards atomically. The
  enchantress practical is grounded to the workbench and the remaining immersive
  flat-service show sound is suppressed at its exact display edge. All fifteen
  priestess lines use the elderly `Wise_Woman` performance. Evidence and headset
  limits are in [TOWN-564.md](TOWN-564.md).

- **NPC feature / 1.1.0 / ModBuild 563 (hardware candidate):** native Unity Cloth
  replaces the rigid altar-runner spring and collides with the complete curved table,
  local/remote hands and heads without adding ray targets. Temple donations use the
  original confirmation continuation; every merchant-item return restores fan parent,
  art readiness, rotation and one palm size. Merchant stock uses the proven mip watcher.
  Automatic service fan-edge sounds are silent, the shared cabinet has physical foley,
  and local NPC speech/effects toggles are independent and default on. Voice regeneration
  replaces all fifteen priestess cues with one consistent close-miked voice and replaces the
  broken `merchant-sell-2` take. Evidence and headset limits are in
  [TOWN-563.md](TOWN-563.md).

- **NPC feature / 1.1.0 / ModBuild 562 (hardware candidate):** repeated temple
  visits rebuild the physical purse; item cards emerge only after their original
  front is ready and use canonical fan rotation. Merchant and priestess attention
  author elbows with lowered hands, coin work has no long neutral stop, cloth
  contact is stronger, and practical lights stay on their furniture. Automatic
  service entry is silent. Eleven resident cue families each contain five shared,
  non-repeating variants with linear spatial rolloff. Headset verification remains
  open; evidence and limits are in [TOWN-562.md](TOWN-562.md).

- **NPC feature / 1.1.0 / ModBuild 561 (hardware candidate):** the physical
  temple purse retains its original confirmation through native modal focus;
  page parchment, visitor hand poses and sleeve interiors address build-560
  screenshots. Returned original item cards settle upright. The merchant's
  offering pose comes from the elected resident author and speech age cannot
  rewind within a cue. The enchantress invites a visitor as her hand opens;
  new consistent merchant and priestess voices, bidirectional lip transitions
  and quiet, paced coin contact revise the audible presentation. These remain subject to headset
  verification; evidence and limits are in [TOWN-561.md](TOWN-561.md).

- **NPC feature / 1.1.0 / ModBuild 560 (hardware candidate):** quieter coin foley,
  English spatial resident lines and mouth curves, varied enchantress activity,
  merchant attention transition, visibly grounded cloth and cabinet details,
  guarded donation body target and ghost-purse bowl cue. One elected public
  merchant cabinet and buyer-owned native confirmation surfaces are mirrored
  to all players; remote visual controls remain inert. Presentation-only voice
  reactions are relayed from visitors to the elected resident author. Automated
  and hardware limits are recorded in [TOWN-560.md](TOWN-560.md).

- **NPC feature / 1.1.0 / ModBuild 559 (hardware candidate):** fixed-scale NPC
  foley, stable merchant/priestess proximity, native temple purse release
  diagnostics, curved blank book pages, continuous coin motion, reworked crank,
  contact-responsive shared altar cloth and inner sleeves. TLV79 adds an optional
  24-byte cloth tail without changing the older resident prefix; private visitor
  furniture carries equivalent runner controls in a bounded additive TLV90. Source and
  automated validation are described in [TOWN-559.md](TOWN-559.md); headset
  appearance and a completed donation remain unverified.

- **NPC feature / 1.1.0 / ModBuild 558 (hardware candidate):** the merchant stops
  coin work while attending to a player and uses coordinated, continuous hand/body
  movement; spatial foley has a practical interaction range. The oversized crank
  collider and the generic spent-item rotation no longer interfere with the
  cabinet or first inspection fan. Owned enhancement cards remain associated with
  native enhancement rows across refreshes, and the immersive enhancement screen
  is not considered active before its mask exists. The temple approach update now
  starts the purse path. The priestess's front/profile atlas no longer projects a
  photographed second ear or eye onto her lateral face; the fixed-detail rig and
  Windows town bundle were rebuilt. The rack mirror's ancestor-fade test now
  waits for its bounded publication cadence before asserting visibility, and
  offering mutations are checked before unrelated rack assertions.
  Automated checks cover source contracts and
  asset format, but the reported hardware interactions still require a headset test.

- **Released 1.0.8 / ModBuild 556:** `main` at tag `v1.0.8`; `dev` has advanced its
  project version to 1.0.9. The NPC branch incorporates that ancestry while keeping
  its own 1.1.0 feature version. Release validation and publication are complete;
  NPC hardware acceptance remains separate.

- **dev / 1.0.8 / ModBuild 556:** combat-log and control-board laser releases
  turn toward the owner using the same short animation as other local windows.
  The board's existing rig pose stream carries its intermediate turn to peers.
  No wire, asset or NPC change; headset motion still needs verification.
  See [REFACE-556.md](REFACE-556.md).

- **dev / 1.0.8 / ModBuild 555:** matching build 554 hardware logs show native story
  and reward completion succeeded, but the mod kept the disabled final page visible.
  Exact map/scenario story identity now ends retention on native close and prevents
  poll/conversion/visibility resurrection. Shared native opening histories also address
  queued story completion and post-quest reward synchronization. No NPC changes
  or release. Evidence and test sequence: [STORY-555.md](STORY-555.md).

- **dev / 1.0.8 / ModBuild 554:** hidden offline Cheats page gains a confirmed
  current-scenario victory action through the native result/reward flow, to test
  build 553's continuation fixes. Cheats remain disabled by default. No NPC content
  or release. Usage and limits: [CHEAT-554.md](CHEAT-554.md).

- **dev / 1.0.8 / ModBuild 553:** native level-up Continue, 3D-map confirmation
  ownership and missed-event/failure recovery, complete message queue callbacks,
  native popup cancellation and safe mandatory-window close admission. Source review
  covers reward/result chains and more than 30 window families. No matching third-party
  logs are available; the maintainer confirms the report concerns release 1.0.7,
  primarily Campaign. Current local/remote files belong to builds 551/500. No NPC code,
  asset bundle change or release is included. See [WINDOWS-553.md](WINDOWS-553.md).

- **NPC feature / 1.1.0 / ModBuild 552 (hardware candidate):** visible-surface laser
  targeting without resident proxy boxes; mage departure cleanup and independent
  Character UI; native per-card enhancement points beside the offered card; physical
  ability/item reclaim. Native priestess purse donations use the actual shared bowl,
  original authority and guarded confirmation. Original localized ink conforms to the
  book pages locally and remotely. Revised mirrored wrist motion, varied merchant
  contact timing and subtle original positional foley. Native black flame padding
  stays transparent. Full package required; environment bundle unchanged. See
  [research/TOWN-SERVICES-552.md](research/TOWN-SERVICES-552.md) for integration gates
  and hardware limits; automated success is not headset acceptance.

- **NPC feature / 1.1.0 / ModBuild 551 (hardware candidate):** callback-scoped
  palm regrabs and freestanding native confirmation; nearby held-card offer intent;
  complete original enchantment inventory on the stand and larger offered card;
  longer varied resident phrases without table bracing; actual articulated rolling
  shelves with a shared page indicator; original standing candles and opaque flame
  cores. Fix a destroyed-inscription shutdown exception. Full package required;
  source and hardware evidence are separated in
  [research/TOWN-SERVICES-551.md](research/TOWN-SERVICES-551.md). All 69 required
  suites are covered, including complete repeats of two repaired fixtures and final
  targeted changes; 14 source gates, 286,120 wire assertions and zero-warning strict
  Release pass. Actual asset review passes 951,242 assertions / nine controls;
  4,423 full-skin and 1,206 holder poses have no tested intersections. Matching town
  bundle: 96,164,115 bytes (`e56d6bd3…61da9`). Hardware results remain unverified.

- **NPC feature / 1.1.0 / ModBuild 550 (hardware candidate):** inert merchant laser/poke;
  shared ability/item hand-local tracking and owned-fan laser contact suppression;
  aimed vertical-stick cabinet paging with locomotion arbitration; upright animated
  actual-card palm handoffs retained through native confirmation, reclaimable and
  mirrored. Revised prayer, stance and coordinated body/arm retargeting address the
  549 hardware report. Fitted merchant worktop and actual native candles remove
  the reported torso/scroll intersections. Original environment bundle unchanged.
  See [research/TOWN-SERVICES-550.md](research/TOWN-SERVICES-550.md) for evidence,
  final validation and remaining hardware checks. All 69 local suites are covered
  with targeted complete repeats after two fixture repairs; 14 source gates, 286,103
  wire assertions, strict Release and final imported asset/motion checks pass.
  Full installation required: town bundle 96,187,501 bytes (`91dedb57…642492`).


- **NPC feature / 1.1.0 / ModBuild 549 (hardware candidate):** approved upright merchant
  cabinet with physical categories/crank and occluded animated page changes; original
  scenario item holding; complete owned-item fan near the merchant; palm-based buy/sell
  requests with explicit native confirmation. Public cabinet and private inspection
  lifetimes are independent, including late join and authority handoff. Stable owned
  practical lighting replaces renderer-dependent nearest-light changes. Revised arm
  skin/contact motion and front-semicircle placement preserve original room geometry.
  Final Windows town bundle: 96,167,323 bytes (`d5f413b9…675e5`); the environment
  bundle is unchanged. Fourteen source gates, all 69 local suites (four stale fixtures
  corrected with targeted negative-control repeats), 286,103 wire assertions and strict
  Release zero warnings/errors are covered. Combined assets pass 888,814 assertions /
  nine rendered controls; motion passes 268,749 / seventeen controls and 1,943 sampled
  full-skin poses without arm/torso or opposite-arm intersections. Exact package evidence
  and hardware limits: [research/TOWN-SERVICES-549.md](research/TOWN-SERVICES-549.md).


- **NPC feature / 1.1.0 / ModBuild 548 (hardware candidate):** restores original cellar
  and forest proportions, removes NPC distance LOD, replaces the oversized merchant
  table with a compact cabinet and physically turning racks, and revises portrait
  likeness, eye/lid geometry and prop-contact body motion. Four offline Kimodo-generated
  phrases use existing shared occupation clocks. Multiplayer rack clocks and membership
  use additive TLV85; live catalog IDs survive repeated page turns without exhaustion.
  A campaign city-cap null dereference found in build-547 logs is also fixed.
  The final Windows town bundle is 95,727,401 bytes (`afdd76b3…88c9`); the environment
  bundle remains byte-identical. Actual source-asset renders pass 2,044 assertions / nine
  visual negatives and final imported activity/contact tests pass 185,546 assertions /
  sixteen negatives. Final checks cover all 14 source and 66 runtime suites (one stale
  radius fixture was corrected and repeated), 286,090 wire assertions and a zero-warning
  strict Release. Bundle/surface checks pass; historical compiled-baseline differences
  remain. Exact package hashes and CRC evidence: `debug/town548-package-verification.json`.
  Headset quality and multiplayer frame timing require hardware confirmation.
  Evidence and checklist: [research/TOWN-SERVICES-548.md](research/TOWN-SERVICES-548.md).

- **dev / 1.0.7 / ModBuild 546**, based on the 1.0.6 runtime without NPC services:
  campaign city-event cap and native animation, original permanent map windows across
  2D/3D switches, and native map-button hints beside physical caps with synchronized
  multiplayer presentation. AoE uses short B/Y releases by default; both locomotion
  sticks stay available. A normal Comfort setting offers the legacy stick alternative,
  and English/German tutorial hints update to the selected binding even while open.
  Native targeting authority and shared encounter continuation remain in charge.
  Evidence, validation and hardware checklist: [MAP-546.md](MAP-546.md).
  The maintainer confirmed this map/AoE revision on hardware before releasing 1.0.7.
  At the time, dev remained the independent hotfix branch while NPC integration
  targeted 1.1.0; both lines merged into dev at Build 584.

- **Town hardware corrections / build 545 (hardware candidate):** build 544's eleven
  screenshots supersede earlier asset-quality assumptions. Physical complete merchant stock,
  native coin offerings, physical enhancement choices, original decoration and owner-authored
  multiplayer output are integrated. Final portrait-fitted faces, spherical eyes, closed
  costume joins and anatomical hands ship in the matching 100,501,555-byte Windows town bundle
  (`08ff8501…fe597e`). Actual asset validation passes 599 render assertions / six negatives; actual
  activity/contact validation passes 125088 / eleven negatives. All map environments share
  one scenery-checked station layout. Full local guard passes 14 source / 59 runtime suites and 260312 wire assertions; only
  the expected historical compiled-baseline differences remain. Strict Release has zero
  warnings/errors. The full matching ZIP is built and CRC/hash-verified. Dev CI status is
  checked at handoff; private evidence is `debug/town545-package-verification.json`.
  Close stereo appearance and hardware timing remain unverified; install the complete package
  on all VR peers. See [research/TOWN-SERVICES-545.md](research/TOWN-SERVICES-545.md).

- **Town hardware corrections / build 544 (new defects confirmed; superseded by 545 work):** the build-543 test exposed black eyes,
  merchant identity drift and open costume joins. Corrected Windows eye lighting, fitted
  heads, continuous neck/costume joins and shared work/attention animation are integrated.
  Full local guard passes 14 source / 51 runtime suites and 260310 wire assertions;
  strict Release has zero warnings/errors. Final assets pass 489 assertions / six visual
  negative controls, actual prefab faces 9862 assertions / three compiled negatives /
  nine anatomical corruption controls, and activities 92107 assertions / nine compiled
  negatives. The source-only CI subset passes 24031 assertions / five negatives.
  The full package contains the matching 98,325,951-byte Windows town bundle; the main
  bundle is unchanged. Install the full package on all VR peers. Headset appearance,
  close-range stereo and hardware frame timing remain unverified. See
  [research/TOWN-SERVICES-544.md](research/TOWN-SERVICES-544.md).

- **Town faces / build 543 (hardware defects confirmed; superseded by 544 work):** anatomical faces, separate eyes, shared
  head/eye tracking, frame-by-frame blink and subtle expression playback are integrated.
  Full local guard passes 50 runtime suites,
  258091 wire assertions and the real Unity face suite (2076 assertions / 16 negative
  controls); strict Release has zero warnings/errors. The voice audit finds original
  narration but no matching recordings for the three residents; no generated voices ship.
  Final asset rendering passes 469 assertions / six visual negative controls; production
  binding to the actual final prefabs passes 9862 assertions / three compiled negative
  controls plus nine deliberately corrupted jaw-weight cases. Root reviewed final neutral,
  blink, mouth and gaze-limit renders. The matching 99,211,283-byte Windows town bundle
  is required; install the full package. The main bundle is unchanged. Existing costume
  cut-edge imperfections and Windows eye-lighting failures were subsequently confirmed
  on hardware; the earlier automated checks did not establish a correct headset picture.
  See [research/TOWN-SERVICES-543.md](research/TOWN-SERVICES-543.md).

- **Town residents / build 542 hardware candidate:** all three NPCs remain on the map in immersive
  mode; direct NPC visits replace service map caps. Actual-floor placement, native decoration,
  practical lighting, held merchant inspection and additive shared resident authority are
  integrated. No purchase occurs on grip/release. Full source/runtime guard passes
  49 suites and 257025 wire assertions; expected old-baseline compiled differences remain.
  New neutral heads, original-game decoration and the matching 81.6 MB town bundle pass
  78 asset assertions and six visual negative controls. Fine facial mesh artifacts remain;
  facial animation topology and headset quality are not claimed complete. Install both
  bundles with the matching DLL. See [research/TOWN-SERVICES-542.md](research/TOWN-SERVICES-542.md).

- **Validation infrastructure (runtime remains build 541):** independent source/runtime
  suites now use bounded parallel execution with isolated logs and temporary outputs.
  CI distributes its runtime suites across four required shards while preserving exact-tree
  proof, main-only release publication and optional artifact limits. Measurement and coverage:
  [TEST-PARALLELISM.md](TEST-PARALLELISM.md).

- **Town services / build 541:** build-540 hardware logs reproduce merchant setup failure
  immediately after the Buy/Sell/All control conversions. The catalog dereferenced the
  gamepad-only Owned filter, absent from desktop merchant UI. Its handoff now follows the
  actual native control set. Repeated catalog tests cover both prefab variants and rollback.
  VR options expose an explicit immersive/original-window choice in a dedicated first town
  services section under Boards, backed by the existing default-on setting. Both asset
  bundles are unchanged from 540. This is a development correction, not a release; successful
  headset opening remains unverified. Evidence: [research/TOWN-SERVICES-541.md](research/TOWN-SERVICES-541.md).

- **Town services / build 540:** the first hardware test of 539 exposed invisible actors,
  black furniture, a native error dialog and the unsuitable floating merchant inventory.
  Corrected NPC LOD bounds and self-contained textured lighting ship in a new town bundle.
  Mirror-template preparation validates item provenance before touching a pooled card;
  the native item-ID-zero error path is avoided. The merchant now has six original item
  cards per page, original prices and buy/sell/filter/exit controls on its counter. Physical
  samples still select through native rows; purchases retain native confirmation.
  Reversible wrappers hide the obsolete list without disabling gameplay, and orphan-frame
  collection respects live service/error owners. Original item details/rule hints remain
  visible on the counter; additional visitors have separate full-size workspaces with
  owner-authored motion and materials. Temple/enchantress gain the asset and
  lifecycle corrections; their existing reading-surface interaction is not replaced by
  the merchant rack. Default-on settings and original-window rollback remain unchanged.
  Evidence, validation and hardware checklist: [research/TOWN-SERVICES-540.md](research/TOWN-SERVICES-540.md).
  Both bundles must be installed. This is an unreleased development build; corrected
  headset output remains unverified.

- **Town services / build 539:** VR options expose `WorldUI/ImmersiveTownServices`,
  enabled by default (maintainer clarification, 2026-09-21). Turning it off restores the three original service windows through
  the ordinary conversion path, including an already-open service, without changing native
  selection or invoking close/confirmation callbacks. Held samples are cancelled, original
  section parents and portraits restored. Enabled remote visitors remain visible regardless
  of the observer's local preference. Unity validation passes 617 assertions and 22 compiled
  negative controls. Hardware verification of live switching is pending.

- **Town services / build 538:** first immersive merchant, temple and enchantress variant.
  Three generated NPCs have body/finger rigs, authored greeting/idle animations, three mesh
  LODs and 4K textures. A separate `prebuilt/ghvr-town.bundle` keeps the existing asset bank
  unchanged. Native service sections become movable reading surfaces; gripping an original
  entry and placing its sample on the work tray selects through the original button.
  Native ownership, prices, restrictions, confirmations and continuations remain authoritative.
  Concurrent visitors share one NPC per service. Original visible widget output is transported
  to inert observer copies, including nested masks, card art and dynamic tooltip contents.
  The complete package requires **both** asset bundles; installing only the DLL is insufficient.
  Record and hardware checklist: [research/TOWN-SERVICES-FIRST-VARIANT.md](research/TOWN-SERVICES-FIRST-VARIANT.md).
  Source, Unity render and archive validation are recorded there; headset presentation remains
  unverified. This is a development handoff, not a release. Detailed facial animation and
  transaction-specific NPC hand choreography remain later polish.
  Original exports, generated sheets and paid mesh provenance remain separate in
  `.planning/debug/npc-references/`, `npc-modeling/` and `npc-meshes/` respectively.
  Seven FAL generation jobs were used, estimated USD 4.275; no additional paid generation
  was needed for runtime integration. Actual account billing was not independently audited.

- **Released 1.0.6 / ModBuild 537:** the maintainer confirmed the menu fix; the final
  hardware log audit found no release blocker. Main commit `59a5d884`, tag `v1.0.6`,
  release workflow `35525779327` succeeded. Published ZIP downloaded and verified.
  Automatic bookkeeping advanced dev to 1.0.7 at `ff59a14e`; no runtime build increment.

- **dev / 1.0.6 / ModBuild 537** removes VR-triggered native focus handoffs:
  opening settings must not shade otherwise usable menu entries. Native hidden callbacks
  are accepted in their actual order, and the row is silently cleared at closure rather
  than waiting one second. X -> immediate reopen and ordinary toggle closure share the
  same state; explicit reopen clears only the mod pane's pending same-frame close.
  Main-menu arbitration and independent scenario/map windows remain unchanged.
  The maintainer confirms build 536 fixed repeated opening; its new logs reproduce the
  stale selected row after X. Short-rest playback is unchanged and remains locally
  hardware-confirmed. Records: [OPTIONS-537.md](OPTIONS-537.md), [CLOSE-537.md](CLOSE-537.md).
  Options tests: 7,076 assertions / seven bindings / 19 negative controls; close lifecycle:
  1,796 assertions / six bindings / six negative controls. Full source/runtime guard and
  254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. Surfaces remain
  625 / 174 / 4,742; inventory 132 classes / 200 methods. Guard exit 1 is solely the
  expected old-baseline difference (101 changed, 78 added/removed, one order-only move).
  Direct compiled comparison with build 536 isolates the three intended menu types
  plus embedded build-number changes. Subsequently confirmed on the maintainer's headset
  and included in release 1.0.6, as recorded above.

- **dev / 1.0.6 / ModBuild 536** addresses repeated VR-options access. The initial
  Sep-20 logs are released build 534: its fourth opening within 60 seconds triggers
  CATCH-ALL FUSE, treating the registered mod menu as a cycling HUD banner. Registered
  mod menus are exempted from churn suppression; unknown HUD windows retain that guard.
  The mod-owned menu rows must stay visible, focused and pressable while their host is
  shown, and transient entry/injection failures must recover with bounded retry.
  Source/log records: [OPTIONS-536.md](OPTIONS-536.md), [BURN-536.md](BURN-536.md).
  **Short rest is hardware-confirmed resolved locally in the follow-up test.** The
  second_logs capture identifies build 535. At the previously failing 0.707-second
  renderer transition, grey/flow 1 and dissolve 0.646 survive unchanged while raw
  progress continues. One burn completes at 2.010 seconds before its pile flight.
  The maintainer reports no visible flash. No further burn change is made. Peer logs
  remain historical 500; new remote hardware confirmation is not available.
  Options implementation is integrated. Focused tests: 5,536 runtime assertions / four
  production bindings / 15 negative controls. Complete source/runtime guard and 254,565
  real-runtime wire assertions pass. Strict Release: zero warnings/errors. Bilingual docs,
  Actionlint, patch inventory and whitespace pass. Config/patch/log surfaces remain
  625 / 174 / 4,742; inventory remains 132 classes / 200 methods. Guard exit 1 is solely
  the expected compiled difference from baseline 080c505e9: 101 changed, 78 added/removed
  types and one order-only move. A separate comparison with the prior build-535 compiled
  output finds only the three intended menu types and build-number substitutions.
  Subsequent build-536 hardware confirms repeated opening works but exposes focus shading
  and delayed row clearing after X; build 537 addresses those. Short-rest confirmation stands.

- **dev / 1.0.6 / ModBuild 535** retains the spent appearance of a Lost card while its
  original native burn continues across temporary face inactivity. Build-534 Debug shows one
  complete ramp, not a replay, but its spent shader floor disappears around 0.697 seconds.
  The ordinary sampler still equated inactive hierarchy with stopped playback, contradicting
  build 534. It now follows the actual tracked iterator. A production-method regression
  reproduces the previous floor loss; real detach/recovery cleanup is no longer stubbed out.
  Native timing, recovery, flights, remote concealment and normal logging are unchanged.
  Local draw and owner publication use the same corrected sampler. Peer logs remain build 500;
  the precise hardware deactivation writer and headset outcome remain unverified.
  Record: [BURN-535.md](BURN-535.md). Focused replay: 683 runtime assertions / seven source
  bindings / 36 negative controls. Complete source/runtime guard and 254,565 real-runtime
  wire assertions pass; flight timing 818, remote burn sequencing 108 and burn layout 228
  assertions pass. Guard exit 1 is solely the expected compiled difference from baseline
  080c505e9: 99 changed, 78 added/removed types and one order-only project move. Config,
  patch and log surfaces remain 625 / 174 / 4,742, with no removals. Patch inventory remains
  132 classes / 200 methods. Strict Release passes with zero warnings/errors; bilingual
  docs, Actionlint and whitespace pass.

- **Released 1.0.5 / ModBuild 534**, main 377d26ec, tag v1.0.5, GitHub Latest.
  Full development CI, reused PR validation and main release workflow succeeded. Downloaded
  ZIP CRC, contents and SHA256 match the published asset. Dev was automatically advanced to
  1.0.6 at 9081a992 and includes the release ancestry. Record: [RELEASE-105.md](RELEASE-105.md).
  The maintainer confirms the Guildmaster fixes; short-rest flashing remains and was explicitly
  deferred for release. New local Debug logs are build 534; peer logs remain historical 500.
  No gameplay exception/deadlock was found. Bounded decorative coin-material load failures,
  native backend DNS errors and shutdown-only exceptions remain documented in the release audit.
  Postrelease burn investigation resumes on dev without changing the published release.

- **dev / 1.0.5 / ModBuild 534** addresses all three build-533 hardware findings.
  Debug shows the first short-rest burn exits synchronously on an inactive original, followed
  by a no-ramp settle and a later animated LostMode reset. Verified original full cards now use
  native disabled playback, retaining the first complete animation across that hierarchy edge.
  Cold Guildmaster MR hid the existing GH_Map_Table as enclosing sky; native map furniture is
  excluded before that heuristic, independently of camera seating/map-driver readiness. The
  whole native table is recognized before optional campaign-slab loading. No duplicate is built.
  Guildmaster action caps remain vertical; WorldMap/City form a separate centred pair to their
  right, fitted to native support and knife clearance. Campaign controls remain unchanged.
  Local logs are 533; peer logs remain historical 500. No fresh screenshots were supplied.
  Source/log causes are established; current headset/peer results are not yet verified.
  Records: [BURN-534.md](BURN-534.md), [GUILD-534.md](GUILD-534.md), [MR-534.md](MR-534.md).
  Focused checks: burn replay 672 assertions / seven bindings / 32 negative controls;
  Guildmaster room 10,338 assertions / 25 bindings / nine negative controls;
  MR scenery/sky 79 assertions / 26 bindings / six negative controls.
  Complete source/runtime guard and 254,565 real-runtime wire assertions pass. Existing
  flight timing 818, remote burn sequencing 108 and burn layout 228 assertions also pass.
  The guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  99 changed, 78 added/removed types and one order-only project move. Config/patch/log
  surfaces stay 625 / 174 / 4,742 without removals. Patch inventory is 132 classes / 200
  methods: the existing BurnCardTimeline patch gains the tested original-only prefix.
  Bilingual docs, Actionlint and whitespace pass; strict Release zero warnings/errors.

- **dev / 1.0.5 / ModBuild 533** fixes the clarified Spellweaver action-slot regression:
  the native action controller retains the first played card; recovering it from Lost to Hand
  previously made it eligible for the round slot again. Actual Hand membership now rejects
  that stale supplement, while Round/ExtraTurn cards and later legitimate selection still work.
  Both reported card flights worked; the later disappearance was the resurrected stale slot.
  Local logs are 532; remote logs remain historical 500. No independent remote static-pair
  reconstruction was found; owner-published slots inherit the corrected collection.
  The short-rest flash **remains unresolved**. The maintainer confirmed Debug was forgotten
  for this capture and enabled for the next test. Bounded opt-in diagnostics record
  native playback, reset and renderer/material transitions without adding normal-log streams.
  Records: [ROUND-533.md](ROUND-533.md), [BURN-533.md](BURN-533.md).
  Focused collector coverage: 50 runtime assertions across three production methods and six
  negative controls. Burn replay/diagnostics: 644 assertions, six bindings and 30 negative
  controls. Complete source/runtime guard and 254,565 real-runtime wire assertions pass;
  local flight timing 818, remote burn sequencing 108 and burn layout 228 assertions pass.
  The only guard exit-1 result is the expected compiled difference from baseline 080c505e9:
  99 changed, 77 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,742 with no removals; the additional marker is Debug-only BURN NATIVE TRACE.
  Patch inventory remains 132 classes / 199 methods. Strict Release zero warnings/errors;
  bilingual docs, Actionlint and whitespace pass. Headset results remain unverified.

- **dev / 1.0.5 / ModBuild 532** addresses the build-531 hardware report. MR backings
  now belong exclusively to UI: scenery underlays, fills and rims are retired, including
  the former unseen/preview routes. Native terrain/water and UI readability remain intact.
  Short-rest spent shader floors survive the native iterator's terminal step, which otherwise
  restores raw paint without writing a final frame. Pile flights take exclusive ownership of
  mod fade visibility and retire an obsolete vanish callback; native burn materials are preserved.
  The layout barrier now retains actual per-card artwork observations for release diagnostics.
  Local logs are 531; remote logs remain historical 500. Reviving Ether flights were launched
  (LogOutput 1278 and 2037); the logs do not establish whether the fade-handover defect caused
  those particular invisible flights. MR screenshot inspected; exact water renderer unknown.
  Source-proven fixes need headset confirmation, especially the reported missing flight.
  Records: [MR-532.md](MR-532.md), [BURN-532.md](BURN-532.md), [FLIGHT-532.md](FLIGHT-532.md).
  Integrated validation: all source/runtime stages and 254,565 real-runtime wire assertions
  pass; burn replay 633 / six bindings / 24 negative controls, flight timing 818 / 13 negative
  controls, burn layout 228 / 16 negative controls, MR scenery 54 / 26 bindings / five negative
  controls. Initial guard stopped at eight historical config-description strings classified as
  protected tokens. Restored those descriptions behind explicit inactive prefixes, then reran
  the affected MR checks, docs, surface census, strict Release and the unchanged remaining guard
  stages; unaffected runtime suites were not repeated. All checks pass. Compiled comparison
  retains the expected exit-1 difference from baseline 080c505e9: 99 changed, 76 added/removed,
  one order-only project move. Strict Release zero warnings/errors; bilingual docs, Actionlint,
  patch inventory and whitespace pass. Config/patch/log surfaces are 625 / 174 / 4,741 with no
  removals; patch inventory remains 132 classes / 199 methods. Independent source review found
  no additional actionable defect; headset/peer pixels remain unverified.

- **dev / 1.0.5 / ModBuild 531** Build-530 hardware confirms
  native Spellweaver recovery returns FireOrbs, ManaBolt, RidetheWind and FlameStrike from
  Lost to Hand (Player.log 9950–10001), but their burnt presentation remains. Native widget
  pile caching and retained burn presentation now receive explicit recovery reconciliation
  before local draw and remote publication. Old smoke cannot acquire a new Hand address;
  reset retries are isolated and cannot cancel a new burn or affect a pooled replacement. Guildmaster table controls are not globally redundant:
  native merchant/trainer/enhancement entry points exist. Build 530 introduced a silent
  whole-rail omission when optional support geometry could not be fitted. Refined mesh
  measurement and a readable right-side fallback retain access without changing campaign
  placement, native action availability or gameplay callbacks. Failed scans keep their
  ordinary cadence; missing-HUD and failed-support diagnostics are bounded.
  The maintainer reports the other build-530 hardware issues appear resolved. Current local
  logs are 530; remote logs remain historical 500. The new fixes still need headset checks.
  Records: [CARD-RECOVERY-531.md](CARD-RECOVERY-531.md), [GUILD-RAIL-531.md](GUILD-RAIL-531.md).
  Focused recovery: 552 runtime assertions / six bindings / 22 negative controls; room
  geometry: 7,014 assertions / 21 bindings / seven negative controls. Complete integration
  guard and 254,565 real-runtime wire assertions pass. Strict Release zero warnings/errors;
  bilingual docs, Actionlint, patch inventory and whitespace pass. The only guard exit-1
  verdict is the expected compiled difference from historical baseline 080c505e9:
  99 changed, 76 added/removed types, one order-only project move. Config/patch/log surfaces
  are 625 / 174 / 4,740 with no removals; patch inventory remains 132 classes / 199 methods.

- **dev / 1.0.5 / ModBuild 530** addresses the five build-529 hardware findings. MR
  backings are excluded from native wall ownership, supplemental unseen-region backings
  are restricted to the intended geometry, and stale/inactive sources are retired.
  Guildmaster controls fit the right tabletop behind the knife; its environment floor
  follows native furniture bases. Overlapping map icons use nearest visible centres for
  laser and fingertip selection. The original Guildmaster quest list stays during browsing
  dialogs, but actual accepted quest story/loadout still hides it, including native peer
  travel without a previously observed local selection. Campaign placement is unchanged.
  Records: [MR-SCENARIO-530.md](MR-SCENARIO-530.md), [GUILD-ROOM-530.md](GUILD-ROOM-530.md),
  [MAP-PICKING-530.md](MAP-PICKING-530.md), [GUILD-QUESTS-530.md](GUILD-QUESTS-530.md).
  Hardware confirms the build-528/529 table is visible. Exact MR pixel attribution,
  final floor contact/knife clearance and the new interactions still need headset checks.
  Remote logs remain historical build 500. EN/DE tutorial execution hints now explicitly
  require board CONFIRM; the unsupported second-pick advice is removed from all five uses.
  Validation: complete guard/source/presentation suite and 254,565 real-runtime wire
  assertions pass; strict Release zero warnings/errors, bilingual docs, Actionlint and
  whitespace checks pass. New focused totals: MR ownership 39, room geometry 6,273,
  map picking 333 and standing quest list 69, with bindings and negative controls.
  Tutorial scope rechecked after the wording change (42 runtime / 17 binding assertions).
  Guard exit 1 is solely the expected compiled difference from baseline 080c505e9:
  97 changed, 76 added/removed types, one order-only project move. Config/patch/log
  surfaces are 625 / 174 / 4,739, with no removals; patch inventory remains 132 classes /
  199 registered methods. One additional Debug floor-placement token versus build 529.

- **dev / 1.0.5 / ModBuild 529** moves the per-burn continuity records to opt-in Debug,
  including an early observer guard to avoid material/progress reads and formatting at
  ordinary verbosity. Re-enabling Debug starts fresh; anomaly output remains bounded.
  Build-528 table/pool diagnostics already require Debug. This corrects the earlier promise
  that normal logs would contain detailed continuity evidence. See [BURN-528.md](BURN-528.md).
  The user's sparse-normal-log requirement is also recorded in `AGENTS.md`.
  Validation: complete source/presentation guard and 254,565 real-runtime wire assertions
  pass; strict Release zero warnings/errors; bilingual docs and whitespace checks pass.
  The expected compiled diff from historical baseline 080c505e9 is the only guard exit-1
  verdict. Config/patch/log surfaces remain 625 / 174 / 4,738, with no removals.

- **dev / 1.0.5 / ModBuild 528** addresses the owner's local short-rest flash, Guildmaster
  table/standing quest list, and a native Swift Bow UI initialization error in the latest
  build-527 logs. Original faces retain animated materials through temporary ownership;
  raw progress/material diagnostics separate a restart from a material swap. Native card
  hierarchy returns before teardown/recycling, with damaged-copy rejection before reuse.
  The original quest widget returns after temporary story/journey withdrawal. Guildmaster
  discovers and fits the campaign table's original assets without moving native furniture.
  Records: [BURN-528.md](BURN-528.md), [CARD-POOL-528.md](CARD-POOL-528.md),
  [GUILD-QUESTS-528.md](GUILD-QUESTS-528.md), [GUILD-TABLE-528.md](GUILD-TABLE-528.md).
  **Hardware remains unverified:** exact flash writer, cold Guildmaster asset availability,
  final furniture fit, repeated scene transitions and current multiplayer observers.
  Remote logs remain build 500, not current evidence. No release was requested this round.
  Integrated validation: full guard source/presentation checks and real-runtime wire vectors
  pass (254,565 wire assertions). Focused totals: burn 534, material ownership 543, native
  pool lifetime 169, standing quest log 26, table fit/material lifecycle 1,530, plus bindings
  and rejected mutations. Strict Release zero warnings/errors; bilingual docs and Actionlint
  pass. Guard exit 1 is the expected compiled difference from baseline 080c505e9.
  Surfaces: 625 config keys / 174 patch attributes / 4,738 log tokens; two new registered
  pool hooks and five new diagnostic tokens versus 527. No existing surface was removed.

- **1.0.4 release authorized after the build-527 hardware report.** The maintainer reports
  no visible issues. Current local logs show completed encounter room making with the new
  window fixed and no mod Error/Fatal entries. Old remote logs are not current evidence.
  Release preparation and audit: [RELEASE-104.md](RELEASE-104.md).
  **Published successfully:** main Release run 35276519563 rebuilt immutable tag v1.0.4
  from main a6d044c4, verified its existing full dev CI evidence, packaged and uploaded the
  archive, checked its SHA256 and published it as Latest. No full suite was repeated on main.
  Recovery fixes landed through PRs #8/#9 after full dev CI; their PR checks reused proof.
  Dev bookkeeping completed at 24c8fce9 and now names **1.0.5**, still ModBuild 527.
  The earlier upload failures and draft-discovery defect are resolved; the release record
  preserves their evidence and the final archive digest.

- **dev / 1.0.4 / ModBuild 527 preserves the newly opened map window's spawn pose.**
  Build-526 logs show three quest-popup animations moving only the newcomer. The solver now
  anchors incoming windows and admits only older movable overlaps. Visual occupancy uses
  original painted/cropped content instead of transparent host/hit rectangles, after opening
  effects settle. The legacy standing-quest-log preference now keeps a free gaze centre;
  hidden-log private quest selection retains its established corner placement.
  Evidence and focused validation: [WINDOW-ANCHOR-527.md](WINDOW-ANCHOR-527.md).
  Validation: reflow 1,322 assertions / 18 bindings / three negatives; quest seat 43 / three
  negatives; painted occupancy 453 / 29 negatives; shared reflow 74 / six bindings / four
  negatives. Strict Release zero warnings/errors. Frame-order, partial-order, bilingual docs,
  Actionlint, shell syntax and whitespace pass. Config/patch/log census unchanged:
  625 / 172 / 4,733. The user confirms the hardware behavior; a current matching-peer log was not supplied.

- **dev / 1.0.4 / ModBuild 526 addresses temple-first/repeated temple header drift and
  general invisible MR contributors.** The build-525 report confirms merchant improvement,
  but the same header moves down/left after temple entry and contaminates later merchant
  openings. The new source repair observes native header TRS before conversion and resolves
  original parent frames rather than replaying local coordinates across different parents.
  MR extent guards additionally distinguish native renderer transparency and the actual
  displayed capture footprint from unbounded authored geometry, in every direction.
  Original visible overflow and the window's existing animation remain part of the contract.
  Evidence, focused validation and hardware limits: [MR-VISIBLE-526.md](MR-VISIBLE-526.md).
  Validation: banner 314 assertions; ink/capture/watch 447; MR layout 258; animation 552,
  with integration bindings and mutation negatives. Strict Release zero warnings/errors.
  Source/frame/docs checks pass; config/patch/log surfaces unchanged: 625 / 172 / 4,733.
  The user confirms the MR reopen defect is fixed in the build-526 hardware test.


- **dev / 1.0.4 / ModBuild 525 repairs the native header that inflated reopened MR windows.**
  Build-524 diagnostics identify the same shared `UI Adventure Header/Icon` drifting upwards
  and shrinking on every merchant reopening, then carrying the defect into temple.
  The native world-preserving parent change retained the converted window's pose/scale;
  the old mod return path skipped geometry restoration after native ownership resumed.
  A tracked borrow now restores the original root-local layout/pose on both return paths,
  preserving native parent/sibling choices and child content. No MR geometry clamp or
  animation change. Source cause and reproduction are in [MAP-HEADER-525.md](MAP-HEADER-525.md);
  corrected headset appearance still needs confirmation.
  Validation: 180 runtime assertions, three bindings, four runtime negatives and one binding
  negative; strict Release zero warnings/errors. Frame-order, bilingual docs, Actionlint,
  shell syntax and whitespace pass. Config/patch/log surfaces unchanged: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 524 is a diagnostic build; the MR reopen defect remains open.**
  The user reports first merchant/map opening correct and subsequent openings too tall.
  Local logs identify 523; remote logs remain historical 500. Capture/hit bounds grow,
  but their extrema do not establish the actual MR contributor. Previous MR diagnostics
  were Debug-only. Bounded normal-level records now identify actual MR edge graphics,
  masks/alpha/material state and target/host instances across conversion lifetimes.
  No further rendering or native-flow change is claimed. See [MR-REOPEN-524.md](MR-REOPEN-524.md).
  Validation: strict Release zero warnings/errors; ink/diagnostics 398 assertions and
  24 negatives; MR layout/accessor 258 assertions, 48 bindings and 16 negatives; animation
  lifecycle 545 assertions, three bindings and three negatives. Frame-order, bilingual docs,
  shell syntax and whitespace pass. Config/patch/log surfaces: 625 / 172 / 4,733.


- **dev / 1.0.4 / ModBuild 523 fits MR backgrounds to native painted geometry.**
  Source changes exclude empty text-layout height and reintroduced tooltip glyphs,
  but build-523 hardware evidence confirms that merchant/map reopen growth persists. Local steady/effect paths and inert remote surfaces share the original
  text/image/clip/visibility policy and a small margin. Native layout, hit/capture and grab
  geometry remain unchanged. Source fixes and build-522 screenshot/log evidence are in
  [MR-MAP-BOUNDS-523.md](MR-MAP-BOUNDS-523.md); headset confirmation remains open.

- **523 focused checks pass:** native ink/painted geometry 368 runtime assertions /
  nineteen runtime and one binding negative; MR layout/accessor 258 assertions /
  48 bindings / thirteen runtime and three binding negatives; actual animation lifecycle
  545 assertions / three bindings / three negatives. Strict Release has zero warnings/errors.
  Eleven frame-order locks, 617 hardware markers, shell syntax and bilingual docs pass.
  Config/patch/log surfaces stay 625 / 172 / 4,732. Unrelated local suites were not repeated.

- **dev / 1.0.4 / ModBuild 522 couples MR backgrounds to window materialisation.**
  Backgrounds use the same erosion field and element progress and disappear before
  the debris-only tail. Native hidden/empty/transparent content clears its backing
  immediately, locally and remotely, independently of cached geometry measurements.
  Close/reopen, MR toggles and native continuation remain independent of decoration.
  See [MR-ANIMATION-522.md](MR-ANIMATION-522.md); headset confirmation remains open.

- **522 focused checks pass:** MR layout/accessor 258 runtime assertions / 48 bindings /
  thirteen runtime and three binding negatives; native ink/live visibility 309 assertions /
  fourteen runtime and one binding negative; erosion mesh 35,833 assertions / ten negatives;
  actual animation lifecycle and native continuation 542 assertions / three bindings /
  three negatives. Strict Release has zero warnings/errors. All eleven frame-order locks,
  617 hardware markers, Actionlint, shell syntax and bilingual docs pass. Config and patch
  surfaces remain 625 / 172; log tokens increase to 4,732 with one new failure diagnostic.
  Full unrelated local suites were not repeated, as requested.

- **dev / 1.0.4 / ModBuild 521 restores the original tutorial hand/controller choices.**
  Card handling, fingertip interaction and prose show hands; key lessons show both
  controllers with the existing per-hand highlights. The original 0.22s animation remains.
  Recovery respects the active task and restores a hand immediately if its controller
  model is lost. Build 518's continuous-controller interpretation was explicitly corrected
  by the user. See [TUTORIAL-HANDS-521.md](TUTORIAL-HANDS-521.md). Only affected tests and
  the strict build run for this change, as requested; headset confirmation remains open.

- **521 focused checks pass:** controller presentation 3,452 runtime assertions / four
  bindings / twelve negatives; first-tutorial scope 42 / 17 / eight. Strict Release has
  zero warnings/errors. Config, patch and log surfaces are unchanged at 625 / 172 / 4,731.
  Full local guard and golden-wire suites were not repeated per the user's request.

- **dev / 1.0.4 / ModBuild 520 scopes initiative and element MR backgrounds to their
  original native rows, locally and on inert remote clones.** Transparent host extents
  and sibling UI cannot inflate those backgrounds. Normal window artwork and smooth
  sizing remain. Local evidence is build 519; remote files remain historical build 500.
  See [MR-CI-520.md](MR-CI-520.md) for evidence, validation and hardware limits.

- **CI now reuses trusted successful dev evidence for identical source trees.** Dev
  still runs full checks; unchanged internal PRs can reuse them, while forks and changed
  merges run full validation. Main releases require successful full-test evidence, then
  build/package the actual main commit without repeating the full suite. No proof
  artifacts are stored. No release or main update is part of this change.

- **520 integration passes:** 17 source checkers, all production harnesses and 254,565
  wire assertions; strict Release zero warnings/errors. MR: 250 runtime + 41 bindings,
  13 runtime / three binding negatives; native ink: 259 assertions / ten runtime and one
  binding negative. CI proof: 23 cases; release topology: 36 assertions; artifact cleanup:
  20 cases. Actionlint and bilingual docs pass. Config / patch / log surfaces are
  625 / 172 / 4,731; patch inventory 130 classes / 197 methods; bundle unchanged.
  Build-519 compiled comparison: 13 changed (seven build-only), one added, zero removed.
  Retained build-502 comparison: 86 changed / 54 added / zero removed.

- **dev / 1.0.4 / ModBuild 519 adds opening-time window room making.** Overlapping
  encounter/story windows can move together with a brief animation inside the view.
  One VR participant authors shared movement; stationary remote grips and manual moves
  interrupt it. Visible FINISHED story frames retain pose synchronization without reopening
  the native dialog. Additive record 77 carries explicit held/automatic masks; v3 remains.
  See [WINDOW-REFLOW-519.md](WINDOW-REFLOW-519.md) for evidence and final validation.
  Supplied logs remain local 515 / remote 500; hardware validation of this build is open.

- **519 integration passes:** 17 source checkers, all production suites, 254,565 wire
  assertions and strict Release with zero warnings/errors. Layout: 1,247 runtime + 15
  bindings / two negatives; authority: 74 + six / four. Surfaces: 625 config / 172 patch
  signatures / 4,730 log tokens. Patch inventory stays 130 classes / 197 methods; bundle
  unchanged. Build-518 compiled comparison: 14 changed / three added / zero removed,
  including five build-only changes and one buffer-size-only change. The retained build-502
  comparison is 84 changed / 53 added / zero removed. See the build record for evidence.

- **dev / 1.0.4 / ModBuild 518 addresses tutorial controllers and defeat Retry.** Both
  controllers stay visible throughout the custom first-tutorial lesson, with task-specific
  highlights on the applicable hands. Recursive VR-layer assignment protects their parts
  from scenery fading; missing models and rebuilt hands recover during the lesson.
  Retry restores each participant's original scenario head pose, scale and board pose.
  Round reloads preserve that baseline, and later peer movement or saved zoom cannot
  redefine it. See [TUTORIAL-RETRY-518.md](TUTORIAL-RETRY-518.md) for source evidence,
  focused coverage and final integration results. Hardware confirmation remains open;
  supplied logs still identify local 515 / remote 500. No bundle, wire or release changes.

- **518 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Tutorial controllers: 1,019 runtime +
  four bindings / nine negatives; retry: 433 + 17 / 15. Retained build-502 compiled diff:
  83 changed / 50 added / zero removed. Private build-517 comparison: 14 changed / three
  added / zero removed, including seven changes limited to the build constant. Reviewed
  surfaces: 625 config / 172 patch signatures / 4,729 log tokens; patch inventory 130
  classes / 197 methods. All 21 classified network-action patches, bilingual docs, shell
  syntax and whitespace pass. No hardware result is inferred from these checks.

- **Build 517 addresses repeated burn playback.** Native effect aliases,
  pile refresh and hover cleanup cannot restart or truncate an owned ability burn. Historical
  lost/consumed widget construction paints the original settled output; a replacement during
  playback waits for the original with cancellation-safe ownership. Actual recovery permits
  later burns. Item effects cannot overlap, and active-card resets requested during playback
  run after completion. Local/remote discovery uses original model identity; missing remote
  samples retain the same lost card's last owner-painted output. Actual completed flight claims
  remain distinct from historical baselines. Scene and pool boundaries retire native guards.
  Source and focused regression review complete; full integration results are recorded in
  [BURN-517.md](BURN-517.md). Supplied logs remain local 515 / remote 500, so this is not
  a headset verification. No bundle, wire-format or published-release changes.

- **517 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Ability replay: 496 runtime + three
  bindings / 13 negatives; items 225 / 11; local layout 219 / 16; remote sequencing 108 / 21;
  scene lifetime 36 + 23 / eight. Retained build-502 compiled diff: 77 changed / 47 added /
  zero removed. Private build-516 comparison: 16 changed / two added / zero removed,
  including seven changes limited to the propagated build constant. Reviewed surfaces:
  625 config / 171 patch signatures / 4,729 log tokens; patch inventory 128 classes / 195
  methods. Bilingual docs, shell syntax and whitespace pass. Hardware confirmation is open.

- **Build 516 hardware fixes remain included in dev.**
  Completed discard pages retain their native selected claims; final confirmation cannot
  light an unavailable second recess. Native recycling updates the locked prefix, and undo
  keeps earlier page return flights. MR backings fit visible native content with a small margin,
  reject empty/transient measurements and animate over a shared 150 ms locally and remotely.
  Borrowed native card hierarchies return before scene unload; native loading state blocks
  re-adoption, while aborted loads restore retained selected identities and original callbacks.
  Supplied local evidence is release 515; remote files remain historical 500. The exact first
  Unity destruction order is not logged. Hardware retest remains open; see
  [HARDWARE-516.md](HARDWARE-516.md) and its three lane reports.

- **516 integration checks pass:** all 17 guard checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Pick tray: 245 runtime + eight bindings /
  six negatives. MR: 233 + 25 / ten runtime + three binding negatives. Scene lifetime:
  35 + 20 / seven negatives. Native ink: 243 assertions. Retained build-502 compiled diff:
  76 changed / 45 added / zero removed; private build-515 comparison: 21 changed / three
  added / zero removed, with 13 changes limited to version/build constants. Reviewed surfaces:
  625 config / 164 patch signatures / 4,729 log tokens; patch inventory 120 classes / 187
  methods. Bilingual docs, shell syntax and whitespace pass. Published 1.0.3 is unchanged.

- **1.0.3 / ModBuild 515 is published from main.** PR #6 merged the accepted runtime/assets
  with bilingual release highlights as `fd76de86`. Release run 35151926513 passed; tag,
  public ZIP, bundle hash, release DLL and the unauthenticated latest endpoint were verified.
  The workflow retained main ancestry on dev and advanced its next version to **1.0.4**.
  See [RELEASE-1.0.3.md](RELEASE-1.0.3.md).

- **ModBuild 515 softens the Glove surface (full install).**
  Both glove materials reduce authored normal relief from 0.5 to 0.25. Native model renders
  and actual bundle checks cover both hands and confirm that Plate/Arcane, geometry and
  attachment anchors are preserved. The exact Unity 2021.3.5f1 bundle has 617 assets and
  74,942,975 bytes. All 17 checkers and production suites pass; 254,019 wire assertions;
  strict Release zero warnings/errors. Incremental compiled comparison has seven changed types,
  exclusively the propagated ModBuild constant; no added/removed types. Surface counts remain
  625 / 163 / 4,728, patch inventory 119 / 186. The maintainer accepted the current changes
  for release; no new per-case hardware capture accompanies that acceptance.
  See [GLOVE-SURFACE-515.md](GLOVE-SURFACE-515.md).

- **ModBuild 514 limits additional VR lessons to the first native tutorial.**
  Admission uses the tutorial selector's first ID and filename, while later tutorials keep
  their native sequence and generic VR wording/input adaptations. Pending lesson/skip/hold
  state retires on scope loss; held messages cannot cross native controller ownership.
  Focused tests pass: 42 runtime + 17 binding assertions, seven runtime negative controls and
  one binding negative control. All 17 checkers and production suites pass; 254,019 wire
  assertions; strict Release zero warnings/errors. Compiled comparison: 71 changed / 42 added /
  zero removed against retained build 502; incremental build-513 comparison 19 changed / two
  added / zero removed, reviewed (tutorial scope plus propagated version/build constants).
  Surfaces 625 / 163 / 4,728; patch inventory 119 classes / 186 methods. The later release
  acceptance is recorded above; it does not enumerate individual tutorial transition tests.
  See [TUTORIAL-SCOPE-514.md](TUTORIAL-SCOPE-514.md).

- **Previous release: 1.0.2 / ModBuild 513.** PR #5 merged the hardware-tested
  dev source unchanged as `11107a29`. Release run 35146255179 passed; tag, public
  download, checksum and release DLL were verified. See [RELEASE-1.0.2.md](RELEASE-1.0.2.md).
  After that release, the workflow preserved main ancestry on dev and advanced to **1.0.3**.

- **ModBuild 513 sequences every native burn before card replacement.**
  Round slots, fans, active grids and character exchange retain their previous presentation
  until all native burns finish. Actual iterator completion distinguishes finished handles.
  Observers wait for the canonical owner's completion frame; durable original-card release
  addresses delayed delivery and slot reuse. Consumed items retain their native widget and
  share original item appearance through additive stream 17/18 (record 76). Incoming character
  views also wait for actual owner burn progress; offscreen completions do not invent flights.
  Native gameplay callbacks and mandatory decisions keep running. See
  [BURN-SEQUENCING-513.md](BURN-SEQUENCING-513.md). The maintainer reports a successful
  build-513 retest; current local logs also observe a build-513 peer. All six recorded burns
  complete with subsequent flights, and phase stalls resolve. Retained remote files remain
  historical build 500. Not every edge case is individually established by this capture.
  Coarse game-loop cadence declines during the session; no memory/GPU trace establishes
  its cause or a leak. The release audit records this limitation and warning triage.

- **513 integration checks pass:** all 17 checkers and production suites; 254,019 wire
  assertions; strict Release zero warnings/errors. Focused suites: local layout 204, remote
  sequencing 70, native completion 33, item lifetime 115 and item appearance 645 assertions,
  with runtime negative controls. Retained build-502 compiled comparison: 65 changed / 40 added /
  zero removed; additional build-512 comparison: 39 changed / 11 added / zero removed, reviewed.
  Surfaces 625 / 162 / 4,728; patch inventory 118 classes / 185 methods. Bilingual docs,
  shell syntax and whitespace pass. These results do not establish headset appearance.

- **dev / 1.0.2 / ModBuild 512 audits mandatory input and native continuation.**
  Map reward managers can be reached outside a scenario controller; failed blocking map
  conversions can request a usable desktop; travel parking failure retains original guarded
  input. Reused windows recheck mandatory close admission. Shared rewards use explicit participation replies. Failed
  conversion and attachment restore native UI, retaining ownership when cleanup needs retry.
  No gameplay lock bypass or timed automatic confirmation is introduced. See
  [DEADLOCK-512.md](DEADLOCK-512.md) for the scope, evidence and final gate results.
  At implementation time, supplied logs were local 510 / remote 500. The later successful
  build-513 retest and release review are recorded above; this is the historical audit scope.

- **512 integration checks pass:** all 17 checkers and production suites; 253,893 wire
  assertions; strict Release zero warnings/errors. Focused suites: rewards 320, map flow
  1,997, modal desktop 1,066, mandatory close 76, reward pose 104 and rollback 87 assertions,
  with runtime negative controls. Retained build-502 comparison: 39 changed / 29 added /
  zero removed types, reviewed; additional build-511 compiled comparison confined to this
  audit. Surfaces 625 / 161 / 4,728; patch inventory 117 classes / 184 methods.
  Bilingual docs, shell syntax and whitespace pass. These checks alone do not establish hardware outcomes.

- **dev / 1.0.2 / ModBuild 511 corrects the failed build-510 chest retest.**
  Tutorial/custom scenarios use UIRewardsManager outside Guildmaster mode; its gamepad
  confirmation adapter rejected the VR click. Continue now supplies only native input,
  preserving native reward groups, multiplayer ownership/actions and the completion callback.
  The button paints native hover/press/disabled states. Capture/chrome include the exact
  original heading's live glyph bounds, retaining layout, fonts and native masks.
  Separate build-509 user logs exposed allocating TMP material reads that repeatedly tore
  down render targets; capture and diagnostic reads now use existing shared materials.
  This removes that demonstrated failure path, not every possible source of FPS dips.
  Local evidence is build 510; retained remote logs are build 500. See
  [REWARDS-511.md](REWARDS-511.md) for the full evidence and validation record.
  Headset acceptance was open at implementation time; the later build-513 maintainer
  retest reports no observed issues. See the release audit for its actual evidence limits.
- **511 integration checks pass:** strict Release zero warnings/errors; all 17 checkers,
  production suites and 253,759 wire assertions. Reward: 299 assertions / 13 negatives;
  materials: 2,031 / four; ink: 237 / five runtime negatives plus one placement binding.
  Retained build-502 compiled comparison: 35 changed types, 25 additions, no removals,
  reviewed. Config/patch/log surfaces remain 625 / 161 / 4,726; bilingual docs and
  whitespace checks pass. These checks do not replace the hardware acceptance above.
- **Build 510's hardware retest failed despite green checks.** Its reward test incorrectly
  modeled ConfirmPressed as an unconditional input latch. Build 511 executes the native
  ProcessRewards iterator through completion, with a negative control for that exact defect.
  The shared-window identity, placement and first-reveal handoff from 510 remain in place;
  [REWARDS-510.md](REWARDS-510.md) is the historical implementation record.

- **Previous release: 1.0.1 / ModBuild 509.**
  v1.0.1 names PR #3 merge be74759e; Release run 35014316673 succeeded.
  The public ZIP matches its published SHA256 and contains the complete asset bundle;
  its DLL reports 1.0.1 / build 509 / be74759 / IsDevBuild=false. Combat log startup
  defaults to off while saved preferences and manual display remain available.
  Candidate CI and all local gates passed, including 253,674 wire assertions and
  36 release topology checks. See [RELEASE-1.0.1.md](RELEASE-1.0.1.md).
- After 1.0.1, the workflow preserved main ancestry and advanced dev to 1.0.2 in
  bot commit 12f66604. The subsequent 1.0.2 publication is recorded above.

- **1.0.1 / ModBuild 508 corrects the build-507 tutorial presentation retest.**
  The user confirms the deadlock is resolved, and the log completes BuyItem/FTUE.
  HelpText and its separate native BG now reflow and align together instead of leaving
  the border and text apart. Corner placement excludes adopted hint geometry while
  retaining it for interaction/chrome. The exact quest-preparation hint is omitted
  under the user's explicit exception; the later battle-goal explanation remains
  native and all quest/tutorial continuations retain their original authority.
  See [TUTORIAL-508.md](TUTORIAL-508.md). Final headset confirmation remains required.
- **508 source/regression gates pass:** all 17 checkers, 253,674 wire assertions and
  production suites. Hints: 45 assertions/seven negative controls; ink/placement:
  218 assertions/four runtime negatives plus one binding negative; preparation prefix:
  13 assertions/three negatives. Compiled comparison with retained build 502: 30 changed
  types, 14 additions, no removals. Surfaces: 625 config keys / 161 patch signatures /
  4,724 log tokens; runtime patch inventory 117 classes / 184 methods. Strict Release
  passes with zero warnings/errors; bilingual docs and whitespace checks pass.

- **1.0.1 / ModBuild 507 repairs savegame tutorial input and hint layout.**
  The live movie surface now accepts laser/poke skip through native continuation.
  Original HelpText wraps at authored font size; pending standalone dissolve callbacks
  are retired before owner adoption. The merchant-to-map dispatcher now emits complete
  native toggle events: its former silent Select omitted the FTUE listener, leaving
  BuyItem active and blocking quest progression. Native tutorial/travel locks remain
  authoritative. See [SAVEGAME-507.md](SAVEGAME-507.md). Headset replay remains required.
- **507 source and regression gates pass:** all 17 checkers, 253,674 wire assertions
  and production suites; movie 66 assertions/six negative controls, hints 38/five,
  native off-bar dispatch 10/two. Surfaces remain 625/161/4,723. Retained build-502
  compiled comparison: 30 changed types, 13 additions, no removals. Strict Release
  has zero warnings/errors; bilingual documentation and whitespace checks pass.

- **1.0.1 / ModBuild 506 corrects the movie-window regression reported on 505.**
  The ordinary orphan sweep now recognizes the live video's exact grab holder,
  preventing repeated destruction/recreation at the world origin. Its full-frame
  image is explicitly content, so the backdrop exclusion cannot hide its handle.
  Chrome shares the canvas's persistent lifetime and module teardown; local and
  remote movie windows use the same ordinary grab/resize and modal ordering paths.
  See [VIDEO-WINDOW-506.md](VIDEO-WINDOW-506.md). Headset replay remains required.
- **506 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production regression suites. Movie ownership/sweep coverage now has 50 assertions
  and four negative controls; the ink walker adds 111 assertions/two negative controls.
  Strict Release: zero warnings/errors; bilingual docs and whitespace checks pass.
  Config/patch/log surfaces remain 625/161/4,723. Retained build-502 compiled comparison:
  28 changed types and 12 additions, no removals; the only newly changed types compared
  with the 505 review are ConvertedPanel and PanelInkBounds, alongside the intended
  movie/modal changes and build constants in types already in that review.

- **1.0.1 / ModBuild 505 fixes savegame introduction presentation.** Build 504 logs
  show native fullscreen video decoding to the desktop and introduction messages
  retaining old standalone conversions after adoption into a character window.
  Dedicated movie windows support shared playback/pose through additive TLV 72.
  Per-message native provenance and serialized owner references replace the global
  producer scan. Atomic conversion handover removes empty frames; hint fit excludes
  its fullscreen dimmer and cannot resize its owner. Native continue/fade behavior
  remains authoritative. See [SAVEGAME-505.md](SAVEGAME-505.md). Headset replay remains
  required; no main/tag/release change is part of this round.
- **505 local gates pass:** all 17 source checkers, 253,674 wire assertions and the
  production suites with their negative controls. New movie/hint suites cover 39
  native-video, 35 shared-playback and 20 hint assertions; updater coverage adds
  nine assertions. Strict Release has zero warnings/errors; bilingual docs pass.
  Compiled review against the retained build-502 baseline: 26 changed types, 12
  additions, no removals (including already-integrated gold/updater/version changes).
  Config keys remain 625; patch signatures increase 160→161 and log markers
  4,719→4,723, with no removals. Runtime patch inventory: 116 classes/183 methods.
- **1.0.0 / ModBuild 504 repairs the self-update prompt.** The 0.9.0 hardware log proves that
  the public latest-release request and version comparison succeeded, then a bare `Transform` in
  `SelfUpdateDialog.BuildProgressRow` threw before the dialog could be drawn. Every dialog layout
  node now has an explicit `RectTransform`; a production harness constructs the choice/progress
  path and mutates the Progress node back to the failing form as a negative control. The headset
  retest confirmed the visible prompt using `install.ps1 -FakeVersion 0.9.0`; that flag compiles
  a release-mode test build, so the regular updater path runs without changing the checkout. See
  [UPDATE-504.md](UPDATE-504.md).
- **1.0.0 / ModBuild 503 makes held gold-pile cards match the laser-hover amount.** Native hover
  totals every `MoneyToken` on the tile, while the held card had only applied `GoldConversion` to
  its one grabbed token. A combined pile can now show the same current total in both views without
  a game-state write. See [GOLD-503.md](GOLD-503.md). Headset confirmation remains pending.
- **503 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing production
  suites and negative controls pass; strict Release has zero warnings/errors and bilingual docs
  pass. Compiled review: eight changed types, seven build-constant-only and `GrabbableProp`; no
  additions/removals. Config/patch/log surfaces remain 625/160/4,719.
- **1.0.0 / ModBuild 502 fixes the missing native party-container handover.** Fresh 501
  single-player logs for quests 078 and 039 show battle-goal selection open beneath a still
  refused outer PartyPanel. Build 500 admitted its different inner owner but missed that
  ancestor; multiplayer success came from the older 90-tick fallback. The current native
  PartyPanel wrapper is now admitted directly while intro/readiness guards remain intact.
  See [MAP-502.md](MAP-502.md) and its evidence/review. Headset replay remains pending.
- **502 local gates pass:** all 17 source checkers, 253,579 wire assertions, existing suites
  and expanded map-flow tests (1,987 assertions/six negative controls). Strict Release has
  zero warnings/errors; bilingual docs pass. Compiled review: nine changed types, seven of
  them build constants only, with no additions/removals. Surfaces remain 625/160/4,719.
- **502 remains version 1.0.0 on dev.** The existing main/tag stays build 498. Build 501's
  card-flight/figure fixes are retained; its final hosted CI passed at `5aeb2cce`.
- **1.0.0 / ModBuild 501 fixes remote animation handovers.** Matching build 500 logs
  identify a flight starting while its remote source recess is still occupied; remote
  dock clearing additionally kept a stationary crumble beneath the flying copy. Source and
  destination ownership now survive delayed seating, overlapping flights and character changes.
  Held figures return to the board before native movement/facing/animation reads, with stale
  held samples rejected until release/switch. Recovery flights use native hand provenance.
  See [MP-501.md](MP-501.md), its independent reviews and hardware replay checklist.
- **501 local gates:** all 17 source checkers, 253,579 wire assertions and existing production
  suites; new flight tests cover 782 assertions/eight negative controls and figure tests cover
  1,440 assertions/six negative controls. Strict Release has zero warnings/errors; bilingual
  docs pass. Compiled review: 21 changed types (six build-constant-only), two new patch types,
  no removal. Config remains 625, patch signatures 152 to 160, log markers 4,718 to 4,719.
- **501 retains version 1.0.0 and is integrated on dev.** Existing main/tag `v1.0.0` remains build 498.
  New regression harnesses run in both CI and main release checks. Final headset timing remains
  unverified until the next hardware test.
- **ModBuild 500 attempted a map preparation softlock fix; 502 corrects its missing ancestor case.** Offline 499 logs show
  native battle goals opened under a party root still refused by frozen story-curtain
  membership. The native loadout's released hide request now admits its original root and
  descendants. VR map input and offline travel also honor the native map lock; online quest
  readiness retains its own visibility/state rule. No game state is forged to escape.
  See [MAP-500.md](MAP-500.md) and its evidence reports. Headset replay remains pending.
- **500 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and prior
  production suites; new map-flow harness 757 assertions with five negative controls.
  Strict Release has zero warnings/errors; bilingual docs pass. Reviewed compiled scope:
  11 changed types (including seven build-constant-only changes), two new helpers, no removal.
  Config/patch surfaces remain 625/152; log markers increase 4,717 to 4,718.
- **500 retains version 1.0.0 and is integrated on dev.** The existing main/tag `v1.0.0`
  still identifies build 498; no release assets or tags are changed by this hotfix.
- **CI storage policy (2026-09-13):** normal pushes/PRs no longer upload DLL artifacts.
  Manual CI on dev can request a tested download; serialized cleanup retains at most three
  builds for two days. Release ZIP publication on main remains mandatory and unchanged.
  Version 1.0.0 / ModBuild 499 runtime is unchanged. See [CI-STORAGE.md](CI-STORAGE.md).
- **1.0.0 / ModBuild 499 fixes an unintended fallback screen during remote long-rest burns.**
  Current 498 logs show the opaque desktop composite appearing while the remote board stays
  active; older 491 evidence has the same signature. Native foreign-hand UI locks were outside
  the local burn guard. The new guard checks every actual lock owner and preserves explicit
  screen requests. Original card/board presentation is unchanged. Headset confirmation remains
  pending. See [REST-499.md](REST-499.md) and its linked evidence reports.
- **499 local gates pass:** all 17 checkers, unchanged 253,579 wire assertions and existing
  production suites; new modal harness 1,058 assertions plus five negative controls. Strict
  Release has zero warnings/errors. Compiled scope is the fallback fix, its new helper and
  version constants. Config/patch surfaces are unchanged; one diagnostic was added.
- **499 remains version 1.0.0 at the maintainer's request.** The already published main/tag
  `v1.0.0` identifies build 498; this hotfix does not rewrite that tag or replace its assets.
  A later release publication must come from `main` and deliberately handle that existing tag.
- **1.0.0 / ModBuild 498 release authorized on 2026-09-10.** Prepared on `dev` for the
  main-triggered release pipeline. Gameplay and presentation carry build 497 unchanged; the
  full package includes the reviewed build 483 bundle and repository-readiness fixes. See
  [RELEASE-1.0.0.md](RELEASE-1.0.0.md) for candidate verification and publication status.
  The maintainer handles public visibility and the in-headset update test separately.
- **1.0.0 repository preparation (2026-09-10):** current guides and CI instructions reconciled,
  historical references clearly marked, generated logs/renders and shader disassembly kept local,
  installer/uninstaller edge cases fixed, stale local bundle overrides prevented, release notices packaged. Version and runtime
  remain 0.9.1 / ModBuild 497. See [RELEASE-READINESS.md](RELEASE-READINESS.md) for validation
  and separate publication follow-ups. No release, tag, main-branch push or visibility change
  is part of this preparation. The maintainer explicitly deferred the fire-asset license question.
- **Previous development version: 0.9.1, ModBuild 497.** Release 0.9.0 (494) was published from `main` by
  [Release run 34407935479](https://github.com/McFredward/GloomhavenVR/actions/runs/34407935479);
  the pipeline passed and advanced `dev` to 0.9.1. See [RELEASE-0.9.0.md](RELEASE-0.9.0.md).
- **496 fixes SDK selection for installation on .NET 10-only machines.** The 494 SDK pin
  was too restrictive; major roll-forward preserves the preferred CI SDK while accepting
  newer installed SDKs. Installer preflight and the legacy restore-tool launch are checked.
  See [SDK-INSTALL-496.md](SDK-INSTALL-496.md).
- **495 adds rendered control-board tiles and improves every variant caption.** Bilingual player
  documentation combines controls and play guidance, covers both main-controller layouts and
  distinguishes selection, actions and character inspection. This is DLL-only after 483.
  Actual headset caption readability and tile interaction still require hardware confirmation.
- **483 IS A FULL INSTALL.** The asset bundle changed for the first time since ModBuild 368:
  74,943,671 → 74,943,763 bytes. Builds 369–482 were all DLL-only drops. A DLL-only install of
  483 shows neither of its two content changes, and the `ENV SKY BRANCH` log line says so out
  loud if it happens.
- **Previous performance hardware evidence covers496, local logs only, one additional player.** Regular scenario
  windows average11.35ms/frame; after a room expansion12.69ms. The run supports the user's smooth
  experience; neither progressive collapse nor a leak is established. Managed heap samples rise.
  See [MP-497-PERF.md](MP-497-PERF.md). Older remote logs and regression JPGs are not from this run.
- **497 synchronizes board motion with head/hand packets and extends hand ordering.** Owned normal
  hands reorder in selection, action and map, retaining order into the scenario; remote concealed
  plucks preserve surviving card positions. Review also closes the previously missing remote
  insertion gap/marker. Arrival/recenter yaw faces the player. Requested defaults and bilingual
  guides are updated; saved settings remain. See [MP-ROUND-497.md](MP-ROUND-497.md).
- **484 is DLL-only relative to 483.** Upgrading from the tested 482 requires the full 483 bundle.
- **493 optimizes multiplayer native presentation without reducing fidelity or cadence.**
  Card capture reuses immutable output; native sends avoid decoding their own snapshots; native
  playback avoids redundant writes/material swaps; original board sections refresh independently.
  Four-state production harnesses cover isolation and immediate transition/recovery behavior.
  Hardware FPS and full-party headset output remain unmeasured. See
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).
- Gate readings at497: all17 checkers pass; wire **253,579** assertions (**+524**: board88,
  fan61, insertion/edge375). Production capture **18,206**, playback **466**, board refresh
  **1,216** and the **12 existing runtime negative controls** pass. Worker-only negative controls
  also rejected three deliberate board defects and three fan defects. Strict Release **0 errors /
  0 warnings**; bilingual docs and all16 metadata-only reference assemblies pass. Patch registration
  **109 classes /167 methods**, surface **152**, config keys **625**, log tokens **4,716**,
  instrument-writes baseline **61**, bundle **74,943,763 bytes**. Records70/71 are additive;
  existing grammars and4096-byte presence reassembly bound remain intact. The documented presence
  budget grows3837→3840 bytes; allocation4097 retains257 spare bytes. This budget is historical
  arithmetic plus a tested three-byte tail, not a new saturated whole-protocol fixture.
  Compiled comparison against `1a714ee2`: **25 changed types and four added helpers**, no removed
  types or resources; changes match the reviewed source, defaults, packet capacity and embedded
  build constants. Local controlled cards and the entire map remain open; concealment is remote-only
  in scenarios.

### Recent builds

| build | what it was | install |
|---|---|---|
| 480 | the review round he asked for BEFORE spending a hardware test. Five read-only review lanes, 19 defects, three new gates | DLL only |
| 481 | the 2026-09 refactor programme: five lanes over 626 files / 550k lines. Also found four gates that could not fail | DLL only |
| 482 | the four rulings he gave on 481's deferred list, one lane each | DLL only |
| 483 | his two hardware notes, both baked into the assets on his ruling "lieber sauber" | **full** |
| 484 | multiplayer pulse, flights, grabbing, rest controls/burns, original bonus widgets and bounded extras transport | DLL only after 483 |
| 485 | remote character-change animations for map-room hands and open discard/burnt browsers | DLL only after 483 |
| 486 | native animation transport and systematic board/card/window parity repairs | DLL only after 483 |
| 487 | laser ownership, phase-consistent card visibility, stable initiative, cap sizing and native tooltip/highlight/element output | DLL only after 483 |
| 488 | short visible window-facing turn after release, matching grab-bar timing and preserving the drawn centre | DLL only after 483 |
| 489 | native card output, atomic held fronts, character decisions, committed/pending health and correctly routed/sequenced flights | DLL only after 483 |
| 490 | pre-test face/overlay/flight audit; later hardware exposed native group-bound rendering failures | DLL only after 483 |
| 491 | repair dynamic native card artwork and independent laser paths behind grab bars | DLL only after 483 |
| 492 | spent rest-burn continuity, native element material binding, board transition diagnostics and hardware-log review | DLL only after 483 |
| 493 | multiplayer capture/send/playback and independent native-section refresh optimization, preserving complete animation | DLL only after 483 |
| 494 | release 0.9.0, reproducible SDK selection and hosted native presentation regression harnesses | **full release package** |
| 495 | rendered board variant tiles, brighter larger captions and concise illustrated EN/DE play guidance | DLL only after 483 |
| 496 | SDK 10 installation compatibility, early SDK diagnostics and maintenance-tool runtime fallback | DLL only after 483 |
| 497 | atomic board motion, stable hand sorting across phases/map, remote insertion cues and requested defaults | DLL only after 483 |
| 498 | release 1.0.0 with build 497 gameplay and reviewed installation/packaging | **full release package** |

---

## 2. Owed to him, and what he has to judge

### 2a. He must look at this and say whether it is right

**The cellar's surround is now BLACK when zoomed out.** He reported two star skies in the cellar
and ruled the dome away. The dome was never visible from inside the room (the stone shell has a
closed ceiling, a capped stair shaft and capped rat holes); it was visible from OUTSIDE the shell,
in the zoomed-out pose where the room reads as a model in front of you. That surround is now the
`[Rig] VoidColor` clear. **This is a consequence of his instruction, not a defect** — but he has
not seen it yet, and it is one line to put back.

### 2b. Latest multiplayer corrections

- Build497 addresses follow-board sample timing, initial heading and owned hand ordering. Its
  hardware checklist includes concealed plucks, map-to-scenario sorting, remote insertion cues and
  long-rest exclusion. The user still needs to verify headset appearance and full-party scaling.
  Evidence and source changes: [MP-ROUND-497.md](MP-ROUND-497.md).

- Build 493 removes redundant native presentation CPU/allocation work and adds regression harnesses
  for four independent boards/senders. Review also closes pooled initiative identity and local element
  readiness recovery dependencies. Per-frame source sampling, original widgets and all visual rules
  remain intact. Hardware performance scaling is still owed; detailed proof and limits are in
  [MP-PERFORMANCE-493.md](MP-PERFORMANCE-493.md).

- Build 492 publishes rest-offer appearance from canonical pile models and retains the actual
  spent base through native burn reset, with native completion tracked independently. Remote
  element effects use original materials even when the viewer's branch is inactive. Board
  disappearance remains open; diagnostic/performance evidence is in
  [MP-ROUND-492.md](MP-ROUND-492.md) and its lane reports.

- Build 491 fixes the native card hierarchy construction failure affecting local map fans and
  remote fronts. Map cards remain public. Independent map/world UI laser routes now respect
  foreground grab bars and the clicking hand. See [MP-REGRESSION-491.md](MP-REGRESSION-491.md).

- Build490 reviews every local/remote card surface for face visibility, native overlay output
  and semantic flight lifecycle. Fixes include viewer-independent selection privacy, held map
  provenance, stale pooled models/materials, actor-scoped burn claims and owner release mirroring.
  See [MP-CARD-REVIEW-490.md](MP-CARD-REVIEW-490.md).

- Build489 addresses the thirteen MB488 findings and additional review defects. Cards mirror actual
  owner output; native decisions follow their character; damage previews preserve committed HP;
  active exits choose their true pile and burn flights wait for native completion. The Trample
  attack refusal was valid Disarm, not a targeting defect. See [MP-ROUND-489.md](MP-ROUND-489.md).

- Build 488 replaces instant release-facing with a 150 ms default cubic ease-out, using the existing
  grab-bar duration. Target and pivot are captured at release; regrab and external placement
  cancel cleanly. Shared windows retain their existing no-reface ruling. See
  [WINDOW-TURN-488.md](WINDOW-TURN-488.md).

- Build487 closes the five MB486 hardware findings and the discovered legacy-element animation
  refusal. The card visibility matrix and source-vs-log evidence are recorded in
  [MP-ROUND-487.md](MP-ROUND-487.md) and its lane reports.
- Additive53 carries original element hierarchy output,54 binds covered short-rest provenance
  to the semantic flight sequence, and55 carries original mandatory-highlight presentation.
  Record56 adds actual original item-tooltip emitters to the existing native plume stream.
  No existing record grammar or game-state authority changes.

### Earlier multiplayer work

- Explicit short-rest state now uses record 46 independently of sacrifice-seat record 39.
- Remote active-bonus rows now use original serialized game slot and picker prefabs, including
  owner subwidget state in record 47. The giant custom caption and plate widgets are removed.
  Build 486 adds native intermediate values in record 49, original auxiliary slot state in 50,
  actual card particle frames in 51 and original element-board frames in 52. The broader review
  also repairs card/fan motion, native pointer transitions, owner initiative depth and shared
  windows. See [MP-PARITY-486.md](MP-PARITY-486.md).
- Map-room fan exchanges now use the owner's map character key. Equal-sized hands refresh
  immediately; discard/burnt browsers re-emerge on character retargets. See
  [MP-FAN-485.md](MP-FAN-485.md).
- Local and remote card pulse/flight/rest repairs are integrated. The supplied disconnect is a
  confirmed transport receive timeout; its underlying cause remains unresolved.

### 2c. Historical refactor follow-ups

From the 2026-09 refactor's reviews (`.planning/refactor-2026-09/REVIEW-*.md`). These record
previous findings and deferrals; they are not new user-approved exceptions to the current
contracts. Recheck each finding against source and later rulings before implementation:

- **Four records ride the send cadence, not the edge** — resolved in 482 for records 36/39/41/43.
  The remaining question is whether any OTHER record has the same shape.
- **The furniture's materials are never destroyed** (`REVIEW-net.md` N8). Needs an owned-materials
  design, not a minimal fix.
- **`RemoteContentSeconds`** resolved in 486: retained and marked INERT in both languages.
  Received/content edges drive the mirror immediately; a fixed recovery poll is not a content delay.
- **No negative cache in the figure resolver** (N9). Bounded; a retry window would be an invented
  tuning value.
- **The wall fade's `RescanCore` two remaining items**: a write-only field and a dead overload
  that carries the live one's evidence.
- Two holes found while removing the cellar dome, filed with arithmetic in
  `NEEDED-OUTSIDE-cellar-one-sky.md`: the stair alcove is placed from the UNSNAPPED hole while the
  wall is cut to the SNAPPED one, and `BuildShaft` has no floor.
- **A half-applied caption pairing, open since ModBuild 363.** `Cards/Piles/PileViewer.cs` applies
  `NativeButtonSkin.ApplyFont` to the three pile captions but never `StyleWorldReadableLabel`,
  while `Cards/Tray/PlayTray.4.Slots.cs` — the caption whose own doc says it is built to match
  those three exactly, *"the same muted parchment colour, the same native HUD font, and the SAME
  fit box and font ceiling"* — does call it. One line, and it reads as intentional, which is why
  it has survived: it changes how three captions LOOK, so it wants his eye, not a silent fix.
  Filed in `LANE-BOARDTEXT-357-NEEDED-OUTSIDE.md` §2.

### 2d. Hardware evidence and remaining observations

The build 496 multiplayer logs now measure the native send/transport, section-refresh and
card-appearance instrumentation introduced in 492/493. See [MP-497-PERF.md](MP-497-PERF.md):
board, revision and native-send readings are present; some appearance/transport scopes are
below the printing threshold in individual windows. Compare frame and logic times as well,
without adding nested scopes or equating a missing line with zero work. The short singleplayer
492 run could not measure those multiplayer paths. Four-player scaling is still unmeasured.

`REMOTE BOARD VISIBILITY` records root transitions, but the one-off long-rest disappearance
remains unexplained. Rest-burn appearance and native-material corrections need headset
confirmation; source and timing checks alone cannot establish the picture.

Historical diagnostic watch list (some items date to 480); check the current build and logs
before asserting that a token has never printed:

`Remote BURN look` · `DOCK MIRROR` · `GATE 3` · `NOT ASKED` · `REMOTE GLOW BLEND` ·
`SHORT REST PILE COVER` · `HELD BAR HIDE REFUSED` · `BURN ANIM STUCK` · `BURN ANIM FLAG LATCHED` ·
`MAP STORY SEND RATE` · `MAP PLACARD SCALE` · `WALL COMMIT THREW` (absence is the good reading) ·
`PACKET REJECTED` (zero is the good reading) · `ENV SKY BRANCH` (cellar must read ABSENT) ·
`HELD-CARD EDGE PRE-EMPT` · `GLOVE NORMAL TAMED` (**gone** — the glove value is baked now, so
there is deliberately no line; the proof is the picture and the bundle size).

**Giant orange text identified:** MB482's census names the 19.87 m
`Furniture/UseBarsDrawer/UseBar0/Caption`. That replica was removed in 484. Confirm the original
widgets, their pickers, and their size in the next headset test.

---

## 3. Standing rulings that are easy to break by accident

The full set is in `CLAUDE.md`. These four have each been broken at least once *after* being
written down:

1. **Visibility, including the later MB490 user clarifications (2026-09-09):** concealment
   applies only to remote presentation in scenarios. Local controlled-character cards are
   always open, including short-rest flights. The entire 3D map is public, locally and remotely.
   In scenarios, remote action cards and action-phase damage sacrifices are open; remote
   ability-selection fans, held and placed cards are covered. Remote short-rest burn flights
   remain covered. This supersedes older pile/active-held exceptions and local concealment.
   Resolve actual model membership before delayed widget CardType; an unresolved positional
   address must never guess a card identity.
2. **Seeing a card's FACE and being allowed to NAME it in a prompt are two questions**, over one
   population. Merging them re-opens the ModBuild 477 identity leak.
   `scripts/check-card-identity-mask.py` fails the build if they become one predicate.
3. **Historical localization behavior is documented in `Core/Loc/Loc.cs`:** transported text
   uses the sender's language; locally resolved keys use the viewer's. An implementation comment
   alone does not establish a user-approved exception to visual parity; follow `AGENTS.md`.
4. **The options button opens and closes the pause menu and touches nothing else.**

---

## 4. Subsystems with a closing account — read it before you touch them

| subsystem | read first | why |
|---|---|---|
| wall fade | `.planning/perf/WALL-FADE-CLOSEOUT.md` | closed on hardware; every dial is settled and the instruments that lied are listed |
| multiplayer 1:1 | `.planning/multiplayer/DESIGN-1TO1-RESIDUE.md` §6 | historical closeout; later parity rulings and build reviews still apply |
| the 2026-09 refactor | `.planning/refactor-2026-09/BRIEF.md` + the five `REVIEW-*.md` | what was found, what was deferred, and what the tooling could not see |
| static batching | `.planning/static-batching-removed.md` | tried and completely removed by user ruling |
| per-eye fade rivalry | `.planning/wall-fade-stereo-rivalry.md` | parked; unfixable on the game's masonry shader without losing the dissolve |

---

## 5. The shape of a round

1. Read his German report. Take the **symptom** as data; re-derive the cause.
2. Land shared contracts, then delegate independent tasks on **disjoint file sets** in separate
   Git worktrees created from current `dev`. Initialize dependencies with `worktree-setup.sh`,
   respect the session concurrency limit and never overwrite a shared baseline symlink.
3. Review every diff. Restrict each merge patch to the lane's OWNED paths.
4. Apply the cross-lane `NEEDED-OUTSIDE-*.md` items yourself.
5. Bump `ModBuild` **once** for a changed runtime build handed to a player, with actionable
   build notes. Documentation or packaging-only preparation does not change compatibility.
6. Run the three gate commands from `CLAUDE.md`. Regenerate `docs/PATCH-INVENTORY.md` once, at
   the end, if any patch class moved.
7. Push to `origin/dev`.
8. Write him a German report: what was found, what was fixed, what he must judge, what is owed.
