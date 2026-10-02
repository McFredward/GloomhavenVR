# Quest implementation and first hardware checkpoint

Implementation was explicitly authorized on 2026-10-03 after the planning-only
instruction. The maintainer requested a new branch from current `dev` and work
until a first state can be tested on hardware. The isolated integration branch
is `feature/quest3-standalone`, starting from `5344a550`, ModBuild 607.
The concurrent main `dev` checkout and original game references remain untouched.

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
hook; the final APK must be checked again before handoff.

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
