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

The existing real VR head renders a temporary noninteractive progress canvas
while the larger content archive is verified. It adds no camera, controls or
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
needs verification. The next original scene run may expose further Android gaps.

## One hardware round

Use the complete private B615 Windows archive and `Install-Quest.cmd`. Installation
retains package/signing identity and existing app data. This is the same offline
dummy-identity startup diagnostic, without store/cloud services. Campaign and
authenticated Android EOS crossplay are not enabled.

The first launch must copy and verify almost 464 MB of additional original
movies. Allow several minutes while phase and MiB values advance; Debug/O0
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

Artifact and completed verification details are recorded after native packaging.
No complete unrelated local test gate is claimed.
