# Quest threaded rule paths and owned startup movies — ModBuild 615

The maintainer supplied `quest-capture-20261004T060740Z-8a2eebfe.zip` and two
screenshots after seeing real hands/lasers, a gray Intro, a binocular double
loading symbol and the original loading-error dialog. This work remains solely
on `feature/quest3-standalone`; concurrent `dev` development is untouched.

## Verified evidence and cause boundary

All nine manifested capture files match their sizes and hashes. The installed
B614 APK hash is `6ac82c27da750b2b3cafb2b3a9cb6c9faa1a370911c141e25bd3829ad221fe52`,
with input `6b07372ace46d54e6582e195c72fb33c635272be22b4630083606670f7eacaf8`.
The real mod completes all eleven modules and its running XR rig. The first
rule-load failure is `Failed to initialise YML`, followed by
`get_persistentDataPath can only be called from the main thread`: the original
YML worker reaches `QuestGame.Compatibility.Paths.get_streamingAssetsPath` via
the DLC rule root. Subsequent missing rule records are cascading failures;
they do not establish corrupt YAML or save data.

The APK omitted all owned `StreamingAssets/Movies` files. The original ambient
directory is reported missing. Selected embedded VideoClips also had failed
Linux imports with zero source size. Unity documents Linux editor import
restrictions and target URL playback in its
[video compatibility manual](https://docs.unity3d.com/2021.3/Documentation/Manual/VideoSources-FileCompatibility.html).
This is a delivery/import defect; Android decoder and headset playback remain
unverified. The flat screenshots do not establish the reported binocular defect.
The actual retained GLES3 Sprite/UI/Blit shaders already contain multiview
variants. Missing single-pass-instanced keywords alone do not prove that these
OpenGLES shaders lack stereo support.

## Changes

The main-thread bootstrap publishes an immutable managed path snapshot before
any mod/original initializer. Original redirected getters never call Unity,
including from YML workers. Atomic publication rejects conflicting roots and
uninitialized access. Original absolute save paths remain unchanged. Only the
error screenshot argument uses Android's relative filename convention.

The builder delivers seven original ambient movies and the two selected menu
clips, totaling 463,716,609 bytes. Exact selected VideoPlayer bindings use
verified local URLs after scene load, preserving native playback, timing,
render/audio settings and callbacks. Failed Linux VideoClip imports are removed
only after proving their complete serialized reference closure. Other campaign
movies remain outside this startup slice. No video transcoding or replacement
content is introduced.

The existing real VR head renders reusable noninteractive world-space loading
artwork while the larger content archive is verified: the existing GloomhavenVR
logo, a gold bar and a percentage for measured phase/file bytes. The same helper
is serialized into the very early startup scene. Its explicit importer preserves
the existing PNG at 1024×179 rather than Unity default scaling to 1024×128. The
actual scene retains the independently checked original aspect, native font and
bar, with one early stereo camera and no new input components. It adds no camera, controls or
alternate rig, and is removed before the original Bootstrap loads. Startup
records have their own bounded reserve alongside original-error evidence.
Bounded Debug records report actual cameras, active native overlays, mod loading
surfaces and video preparation/first-frame/errors. No speculative stereo shader
or native presentation change is made.

Independent captured defects are corrected: input enforcement waits for native
singletons, woven self-hook lookup accepts its actual public visibility, and
camera event handlers avoid Unity's reserved parameterless message names.
The original binary assigns `UIMultiplayerSelectPlayerScreen` order -51 and its
player slots order 0, `PlatformLayer` -10100 and `KeyActionHandlerController` 3.
The actual B614 APK instead assigned each order 0. This export defect removes
the singleton-before-slots guarantee. The builder restores the original script
execution metadata rather than suppressing native callbacks. The exact null
operand in the captured `UIMultiplayerPlayerOption.Awake` stack is unavailable;
the ordering defect is independently proven, while its hardware repair still
needs verification. Source DLL metadata identifies unreferenced static utilities
that cannot own Unity lifecycle callbacks; their authored importer values are
retained, while their exclusion is checked against actual imported types. The
complete private original-project import verifies 1,929 orders, including all
140 nonzero values, before the final Android build. The next original scene run may expose further Android gaps.

## One hardware round

Use the complete private B615 Windows archive and `Install-Quest.cmd`. Installation
retains package/signing identity and existing app data. This is the same offline
dummy-identity startup diagnostic, without store/cloud services. Campaign and
authenticated Android EOS crossplay are not enabled.

The first launch must copy and verify almost 464 MB of additional original
movies. Allow several minutes while phase and percentage advance; Debug/O0
verification can be slow. A warm launch still verifies installed content.
Wait time alone does not establish a hang. If progress stops, collect logs while
the app remains open so state includes main-thread frames and worker progress.

Observe the Intro, native loading icon and main menu, then test existing lasers,
native buttons and VR Options/passthrough if reachable. Report whether the loading
symbol is doubled binocularly and whether closing either eye changes it. A flat
screenshot cannot settle that point. Do not start a campaign in this menu target.
Run `Collect-Quest-Logs.cmd` before closing the app, then supply its ZIP and
screenshots under the main ignored `.planning/debug/quest3_probleme/` directory.
After completed delivery, relaunch once and capture the warm run separately.

## Validation boundary

The focused Quest suites execute actual builder/weaver/startup/content/video
code, with declared Unity rendering and XR seams. They establish immutable worker
paths and unchanged original screenshot logic, exact owned movie binding and
native playback ownership, content-before-mod ordering and bounded evidence.
Actual SDK compilation, native Android build and exact APK audits are separate
checks. None establishes headset image correctness or campaign playability.

The maintainer additionally requested the existing GloomhavenVR logo and a real
phase progress bar/percentage, with reusable presentation for later in-game loads.
The handoff directory retains only the newest verified package and receipt;
superseded APKs/archives are removed, while historical audits and supplied
hardware captures remain outside it.

The recovery input is a separate verified clone of the immutable input used by
B614 (`53972e2f…`), preserving its original catalog path mappings. The current
mutable recovery project differs and is not substituted for the captured input.
The source snapshot intentionally has no Git checkout: its existing DLL Git
stamp reports unknown rather than the source revision. The verified embedded
input manifest and deployed assembly contracts are the canonical source
provenance; ModBuild 615 is retained. This stamp limitation has no demonstrated
gameplay or ABI effect.

## Completed native artifact and scoped checks

The signed ARM64 IL2CPP Debug/O0 APK is 1,615,998,254 bytes, SHA256
`e5b63478557e0141cb35d653f8d6fc1de76dd766ff3071dd6617d24242baf23b`.
Its immutable runtime/tool source is clean `911fc23f34a2050ac52cccd9db894239a8c90066`,
input `44c066b3fa76116058f55e7c3e873d98366471736c4631eba71bf4c8ebc19bcf`.
Package `dev.gloomhavenvr.quest` and certificate SHA256
`1412542b0b4cac2f1bc4941cbb4b01a5556eb8da2c34709086ab9375fd33c012`
retain installation identity and app data. The profile remains explicitly DUMMY.
The prior unhanded candidate was stopped after discovering the NPOT logo distortion;
its artifacts are excluded from the handoff.

Ten focused Quest suites pass at `158f2676`, followed by the three affected
builder/loading/order suites at final `911fc23f` after its Editor-only import fix.
All 14 source checks pass on the final source. Strict Release reports zero errors
and warnings; its separately audited DLL SHA256 is
`452387357a59e960547ab339103970df229125d4194c75f5197310c54f7ba842`.
Direct unchanged wire/golden vectors pass 286,760 assertions. No broad wrapper or
unrelated complete local gate is claimed. The private actual Editor fixtures pass
61 order checks with 16 rejection controls and 12 original-logo import checks.
Portable staging passes 13 tests, 12 defect controls and 57 recovery tests.

The actual final Editor restores 1,929 orders, including all 140 nonzero and 643
referenced targets; all 7,003 raw original IDs are accounted for by verification
or explicit source exclusions. The real generated scene retains the original logo
aspect, native font, progress geometry, WorldSpace canvas and one stereo camera.
All 48 deployed plugins and eight freshly compiled Player SDK contracts match.
The necessary independent final API audit reports zero issues. Forty-four plugin
files are byte-identical to the prior reviewed candidate; changed DLLs preserve
checked code apart from mod timestamps. All 62 compiled Player script types retain
the prior checked code. Exact-byte semantic proofs are explicitly reused;
standalone worker harnesses are not needlessly repeated.
No complete unrelated local test gate is claimed.

The exact signed APK passes 18 startup-presentation retention checks: actual native
logo dimensions 1024×179, original aspect, embedded Arial data, gold progress
geometry and percentage fields, and a single WorldSpace early camera owner. Its
native bank contains 2,581 MonoScripts; all 1,929 restored types, 140 nonzero and
643 referenced targets retain the verified orders. The 62 extra same-identity
records all agree. The packaged startup ZIP equals the finished project's ZIP;
all nine owned movies match original sizes and SHA256. Actual retained GLES3
UI/Default, Sprites/Default and Hidden/BlitCopy contain multiview variants and
`gl_ViewID_OVR`; this does not establish runtime shader selection or binocular
alignment. Hardware verification remains explicitly false.

The complete private Windows archive verifies ZIP CRC, embedded APK hash/receipt
and all 16 installer/collector dependencies against the final source bytes. Its
actual default dry-run selects B615 and its verified hash without contacting ADB.
Installation retains package/signing identity. The main handoff folder contains
only `GloomhavenVR-Quest-B615.apk`, `GloomhavenVR-Quest-B615-Windows-Test.zip` and
`handoff.json`. Cleanup removes 7,017,578,366 bytes of superseded downloads and
moves historical validation to the adjacent ignored validation archive. Supplied
`quest3_probleme` captures are untouched.
