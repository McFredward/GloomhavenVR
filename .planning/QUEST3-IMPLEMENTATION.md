Current B616 follow-up targets the verified B615 native abort in the Quest
Harmony type resolver, retaining the accepted branded loading view. Resolve
requested types without initializing unrelated runtime metadata; preserve
short-name compatibility using generated aliases from current owned inputs.
Persist startup phase/module observations at transitions so a synchronous
initialization/crash cannot leave a misleading old delivery snapshot.
See [616 evidence and procedure](QUEST3-HARDWARE-616.md). Native artifact and
hardware confirmation remain pending.

# Quest implementation and hardware checkpoints

Current B615 hardware candidate follows the exact B614 capture: cached main-thread
paths repair the captured YML worker boundary; owned movie delivery and exact URL
bindings restore missing/failed-import content. Original script execution orders,
native-input readiness, public AOT hook lookup and camera callback naming are
corrected. Reusable loading artwork presents the existing logo, an actual bar and
measured percentage; its importer preserves the original PNG aspect. Debug camera
and video records do not claim a fix for the reported binocular loading defect.
See [615 evidence and procedure](QUEST3-HARDWARE-615.md). The signed native APK
succeeds from clean runtime/tool source `911fc23f` / input `44c066b3…`; deployment,
actual SDK and generated scene/order evidence pass. Exact APK/Windows handoff
verification is documented there. Headset image, original menu, campaign/saves
and Android crossplay remain gates. Only scoped Quest/source/build/protocol checks
are claimed; concurrent `dev` work remains separate.

Current isolated follow-up: ModBuild614 adds observable cold loading after the
verified B613 logo-only capture. The old gate cannot distinguish transfer from
verification. An early serialized stereo view and managed APK delivery retain
content checks while keeping the Unity main thread available; phase/byte/state
and bounded app-start capture evidence are expanded. See
[614 hardware procedure](QUEST3-HARDWARE-614.md). The signed 1,153,837,389-byte APK,
scoped Quest/source/build/protocol checks, actual SDK and packaged camera/font
retention are verified. The Windows handoff preserves installer/capture source
bytes and APK selection. No headset success or playable campaign is established.

Previous isolated implementation: ModBuild613 has a successfully built and signed
ARM64 IL2CPP startup player after the B612 hardware capture and the maintainer's
request to activate the existing VR mod. Its Windows handoff and targeted Quest
gate are verified; headset behavior remains unverified. Scope includes seven
Quest suites, affected existing regressions, source/strict-build checks, protocol
vectors and actual Android artifact validation. The maintainer questioned
repeated unrelated `dev` checks; no complete 115-suite verdict is claimed here.
The candidate reuses its real rig/input/UI/keyboard, shares the player-owned XR
session and supplies an authored Android mod bank with verified local paths.
It also adds actual Unity-package API compatibility gates, restores the owned
UGUI layout batching contract and initializes the original lazy rule root.
Bounded threaded startup logging now reserves first distinct error stacks.
See [613 hardware procedure](QUEST3-HARDWARE-613.md). Actual headset startup,
campaign generation, original save round trips and Android crossplay remain gates.

Previous checkpoint: ModBuild612, a successfully built and signed
real original-scene startup/menu checkpoint after the maintainer accepted B611 animation, lighting,
textures and button handling. Right-stick height and the Windows capture repair
are integrated. See [612 hardware procedure](QUEST3-HARDWARE-612.md),
[startup recovery](QUEST3-STARTUP-RECOVERY.md),
[runtime boundary](QUEST3-STARTUP-RUNTIME.md) and
[original network audit](QUEST3-NETWORK-PREFLIGHT.md).
This remains a guarded startup diagnostic. Campaign generation, full mod
lifecycle, original save round trips and Android crossplay are independent gates.
The private 1.1 GB ARM64 IL2CPP APK uses diagnostic Debug/O0 and LLVM LLD;
the embedded input and B611 signing continuity are verified. The Windows
installer now budgets transfer time from APK size. All 14 source and 111 local
suites, 286,760 wire/golden assertions and bundle/mesh/surface checks pass;
strict Release has zero warnings/errors. The fresh compiled comparison is
accepted against reviewed609, separately from the expected nonzero historical
Build-601 guard comparison. Actual Windows package selection is independently
validated. The subsequent B612 capture confirms original content/catalog loads,
an InputSystem getter failure and a native load-error window after the intro;
it does not establish the first Android rule failure or campaign readiness.


Implementation was explicitly authorized on 2026-10-03 after the planning-only
instruction. The maintainer requested a new branch from current `dev` and work
until a first state can be tested on hardware. The isolated integration branch
is `feature/quest3-standalone`, starting from `5344a550`, ModBuild 607.
Original game references remain untouched. The parallel `dev` Frame608 changes
through `8146ed02` were merged into the Quest branch before the final checkpoint.
The combined checkpoint uses ModBuild609, retaining the Frame608 notes and behavior.
Worker worktrees and the primary Quest branch are used for isolation. The
maintainer's explicit 2026-10-03 clarification supersedes the generic integration
workflow: publish only `origin/feature/quest3-standalone`, keeping concurrent
Steam Frame `dev` work separate. Worker branches, original game payload and APKs
are not published.

The maintainer also explicitly authorized a visibly marked dummy identity for
builds on this host. The local diagnostic uses name `Quest Local Test (DUMMY)`,
Steam ID `0` and account ID `0`. Normal builds import the selected local Steam
account. Neither identity is an authentication credential. No Horizon app,
Steam/other-provider profile lookup or cloud service is used.

## Implemented boundaries

- The local Python builder selects and hashes original installation, mod/tool
  source, profile, static Steam logo and optional native diagnostic asset slice.
  Inputs are copied into isolated immutable snapshots; source and original game
  are never patched in place. Stage receipts validate outputs before reuse.
- Signing material is generated once in the private local output root. APKs are
  checked with actual Android tools for signing continuity, application ID and
  ARM64 IL2CPP player contents. Build evidence distinguishes `probe` from `game`.
- The Android template shares Unity's one OpenXR session with a native
  `XR_FB_passthrough` underlay. No second XR instance/session is created. Source
  structures and existing layer/view/depth chains are preserved. Camera alpha
  changes only when passthrough is actually active; VR remains available on failure.
- The diagnostic scene exercises tracked head and both controllers, explicit
  placement, passthrough toggle, embedded local identity, persistent diagnostic
  storage and English/German labels. Guildmaster and Steam Workshop entries are
  grey, with a controller-hover tooltip. These are diagnostic presentation checks,
  not proof of all original game ingress guards.
- A separate pure-native slice contains the original Bandit Guard mesh, skinning,
  avatar, rig, animator and animation clips. Original gameplay callbacks and physics
  are omitted only for the diagnostic. Original shader source is unavailable;
  Standard material conversion is explicitly a probe approximation. Full-game
  rendering parity remains a blocking requirement.

## Concrete evidence and limits

The game uses Unity 2021.3.5f1. Its exact Linux editor, matching Android module,
OpenJDK 8, NDK r21d and SDK/build-tools 30.0.2 are available locally. The existing
desktop editor/package version is retained; tool installation does not change
the mod's desktop runtime or shared reference assemblies.

The native passthrough bridge compiles to Android ARM64. Its host fixture executes
106 assertions including disabled/suspended/foreign/empty frames, missing extension
functions, partial creation cleanup and session recreation. These tests establish
composition and lifecycle logic, not the appearance of either eye on Quest.

A real signed diagnostic APK was compiled successfully using ARM64 IL2CPP,
Unity2021.3.5f1, GLES3, SDK29 minimum and SDK30 target. Both player and native library
were inspected with actual Android tools. Its signing certificate is recorded
in private build receipts; signing continuity is verified on subsequent builds.
The first manifest inspection caught an unconditional eye-tracking requirement
in the pinned Unity OpenXR1.13.0 MetaQuest build hook despite disabled EyeGaze.
The target now removes those unused feature/permission declarations after that
hook. The final APK was checked again successfully: neither eye-tracking permission
nor the mandatory eye-tracking feature is present. Validation additionally requires
the actual Unity/OpenXR/passthrough libraries and checks every native ELF header
for little-endian AArch64 shared-object code.

The first609 private APK and exact content/source hashes are recorded in the main
checkout's ignored `.planning/debug/quest3/handoff.json`. The builder's default
`.planning/quest3-local/` is also ignored, including its local signing/account data.
See [hardware steps and limits](QUEST3-HARDWARE-609.md). No headset was attached
to the build host, so passthrough/tracking/images have not been verified on Quest.

The reviewed609 file is 30,861,781 bytes, APK SHA-256
`5818e9d22cd47ed8fb79bde1c41dab815cb0a53faedc49da4075b9e5c1bddd6b`,
from runtime/tool commit `cc041fab` and input key
`58cd43841e856021966270f5c09ce9d46ef80f486472c60ff6addbd4e3c2991d`.
The player now retains explicit Resources materials for Standard and Unlit/Color;
the latter had only been requested dynamically. Actual shader compilation and
both serialized native shader payload names were verified in the final APK.
Specific native mesh/collider types needed by runtime CreatePrimitive are also
preserved in the diagnostic linker file. Earlier local APKs remain private
historical evidence and are not the reviewed handoff.

Original core recovery has produced 13 build scenes and original managed plugins.
All build-scene script identities resolve. The complete export remains unready:
compiled shader placeholders, deferred bundles, unsupported serialized layouts and
missing original references are reported explicitly. The builder rejects full-game
readiness failure rather than treating a zero exporter exit as a successful port.

The static weaver derives patch and helper closure from the selected compiled mod.
Its evidence, supported semantics and remaining IL2CPP/runtime risks are recorded in
[AOT evidence](QUEST3-AOT-EVIDENCE.md). Asset recovery details are recorded in
[recovery evidence](QUEST3-RECOVERY-EVIDENCE.md). Assembly integration is not a
successful original-game startup, save round trip or multiplayer connection.

## Final repository validation

The final `dev` runtime/tool tree at `cc041fab` passes **14/14 source suites,
107/107 complete local suites and 286,760 wire/golden assertions**, all three
main bundle formats and the 49-part figure bank. Strict Release has zero warnings
and errors; the five existing bilingual document pairs and seven Quest developer
documents' local links were checked. Suite coverage and recorded log hashes were
verified independently. The complete local run took 993.3 seconds with eight jobs.

Compared with the preserved reviewed Frame608 compiled snapshot, exactly eight
existing types differ only in the inlined 608 -> 609 build constant, plus the new
QuestText type. No existing type, assembly reference or embedded resource changes.
The guard's exit1 describes comparison with its much older `98fba1a8d` baseline;
all subordinate gates pass. Current validation, comparison and hashes are retained
privately under `.planning/debug/quest3/validation/`.

An earlier run failed `town-service-mirror` with a cast exception under unchanged
bound source. Coordinated focused/full production probes and the final complete
gate passed. Its original cause remains unproven; no speculative production fix
or relaxed assertion was introduced. This passing checkpoint does not establish
Quest images, original campaign startup, saves or multiplayer acceptance.

## Remaining complete-port gates

1. Resolve asset recovery defects and recover faithful Android shaders and complete
   required bundle dependencies. Preserve original scene/script/asset identities.
2. Adapt the original platform initialization boundary for embedded local identity
   and offline operation; initialize the existing mod without desktop BepInEx
   assumptions. Do not pretend an embedded Steam ID is a live Steam session.
3. Prove IL2CPP closure for original serialization, reflection and remaining runtime
   generators; preserve original save format and protected gameplay/network code.
4. Establish file-backed content extraction/root routing and rebuild original/mod
   bundles for Android with dependency manifests and immutable input provenance.
5. Prove original Photon crossplay and, if compulsory, legitimate Android EOS
   authentication using only the narrowly authorized services.
6. Implement every Guildmaster/Workshop ingress guard in the recovered game and
   carry the existing VR behavior into the native target without visual divergence.
7. Measure actual scenario timing, memory, reloads, MR and thermal behavior on Quest.

The first APK is deliberately a hardware diagnostic with an original animated
asset. It is not a playable campaign, save-format test or multiplayer port.
Generated APKs, recovered game content and account/signing data must remain in the
local output directory and must not be published as repository/release artifacts.
