# Quest B717 native ping and shield upload review

Date: 2026-10-21. Worker base: Quest `955fc1209574628c07777242bc09817d8598cf01`
(semantic1.2.0/B716). The new headset report still shows colored fragments on
pinged tiles and an incorrectly drawn shield during an attack. Evidence is the
308.36s video `dev.gloomhavenvr.quest-20261021-182718-0.mp4` and the B716 capture
retained under `quest717-review-20261021/capture`. The shield-shaped pale rectangle
is visible near the melee contact at approximately302s. Video cue and focused
frames remain in the ignored evidence; no movie pixels enter the release.

## Previous attempts and current hypothesis

B711/B716's narrowed POSITION/COLOR/UV request retained original native rendering
and clocks but did not cure the headset's ping fragments. B712 uploaded already
baked meshes and suppressed the native renderer with `forceRenderingOff`. That
lost native visibility and froze the original Pause-culling simulation, removing
the delayed rising animation. B713 correctly rolled that back. The exact causal
reproduction and the misleading manual-Simulate fixture are documented in
`QUEST713-PING-ANIMATION.md`; do not reintroduce that suppression.

The native particle upload/attribute-fetch path remains the leading hypothesis,
not a proven Adreno root cause. Both original ping materials are additive. The
original TargetShield path is more specific than a generic hit effect:
`Choreographer.TargetShield` spawns the exact
`GlobalSettings.m_ActiveBonusBuffTargetEffects.ShieldActiveBonusTargetEffect`.
The native GUID6520fa136e3c8d646b5df7d6cfcb9337 identifies `P_Shield_Frame`.
Its two shield mesh particles use engine shader fileID200, actually resolved by
Unity2021.3.5f1 as `Legacy Shaders/Particles/Additive`. Its fog uses the separate
`KriptoFX/RFX4/Particle` shader and is left alone. Although both shield mesh
renderers serialize GPU instancing enabled, actual `supportsMeshInstancing` and
material instancing are false. Disabling that flag would not change this path.
The zero-damage `WeaponCollision` fallback `P_Block_General` and its distortion
materials are different authored effects; they are not silently replaced.

## Corrected runtime experiment

Retain the original ParticleSystemRenderer enabled, on its original layer and
inside native visibility/culling. For the actual head-camera draw only, give it
a private copy of its exact additive material with a black `_MainTex`. That
submits zero additive RGB while preserving native visibility. Submit Unity's
live `BakeMesh` geometry through an ordinary owned mesh with explicit Float32
COLOR, original UV/indices, original visible material/texture, original property
block, sorting and depth contract. Return the native material and disable the
upload immediately after the camera, on pooling, or on reset. No native
`Simulate`, culling override, visibility suppression, custom particle/emission
clock, shader substitution or appearance redesign is used.

The original native ParticleSystem itself supplies a property block. A broad
`HasPropertyBlock` veto incorrectly refused every original draw in the first
local iteration. The final guard rejects only a live independent `_MainTex`
property, including a null override, and otherwise copies the original block.
Material identity, shader, mode and original stream contract are checked before
each lease. Independent material/texture/stream/visibility owners retain
control. No scene-wide renderer census is introduced.

The ping admission remains the native manager's exact newly assigned highlight,
including received pings. The shield postfix is installed only on Quest and
accepts only the exact native TargetShield prefab reference returned from the
original ObjectPool.Spawn call. Its two additive mesh draws use the same upload
helper; fog, lights, gameplay, source mesh, placement, pool/lifetime and native
curves remain unchanged. The finite reusable buffers are allocated per pooled
effect, not per frame. Only these transient effects pay the extra native black
submission plus the visible upload.

## Focused evidence and remaining boundary

The fixture runs Unity2021.3.5f1 with Vulkan and exact original native prefabs,
materials, textures, shield convex mesh and recovered original shader programs.
Only unrelated game MonoBehaviours are removed from the small fixture copy;
all original native particle fields remain. The source export is read-only,
hashed before and after every run. Actual production helper source is compiled
into the fixture; platform/session/manager/camera routing are explicit external
boundaries. The original production shield spawn hook is compiled by the strict
mod build; this fixture directly exercises its admitted native effect rather
than pretending to run the full Choreographer/ObjectPool gameplay path.

Final evidence is retained under:
`/home/claw/quest3-local/build/evidence/quest717-review-20261021/fx/`.
The native run report records the precise assertion and input SHA counts.
The positive observation includes:

- Original ping229 visible pixels and shield879 visible pixels, maximum RGB
  difference0 between the original native path and explicit upload.
- Ordinary opaque depth occlusion, no duplicate visible contribution, original
  material/texture restoration, pooling including pooling during a head lease,
  foreign shader/material/texture/stream/visibility ownership and desktop no-op.
- Six actual automatic playback observations across five original systems:
  ping Pause systems, shield PauseAndCatchup systems and untouched fog. Native
  clocks, delayed emission, particle counts and original renderer visibility
  match the unpatched originals. No manual Simulate drives this observation.
- Compiling causal controls reject reintroduced native visibility suppression,
  the exact historical B712 implementation, missing material restoration,
  changed-shader admission, unrelated material admission and duplicate output.
- Strict Release compilation: zero warnings and errors. The primary integrator
  performs final actual-game API/AOT and source checks with the integrated tree.

The first live-shader control did not fail because pooled systems had no current
particles; the fixture now populates the actual native systems before changing
the shader. This is a corrected instrument, not proof that a weaker control was
adequate. Failed exploratory runs, corrected controls, logs and their cleanup
receipts are retained separately from the final passing evidence.

These host observations overcome the specific B712 clock/culling failure and
preserve original host pixels. They do not establish the final Adreno eye
picture or prove that this is the only shield defect. The next hardware test
must independently verify native ping rise, tile fragments and shield shape.
If fragments remain, actual per-device vertex/texture evidence is needed rather
than another change to particle timing. No full game import, original bank,
shader bank, audio/movie/depth recipe or APK rebuild ran in this lane; this is
a compact runtime-code update. Every fixture reaps its processes and uses
`cleanup_completed_run`; only finished regenerable caches are removed.

## Follow-up: optional Normal input and real rig scale

The final admission accepts exactly POSITION/COLOR/UV, with or without the
unconsumed Normal field. It does not rewrite either layout, admit additional
custom streams or override a live texture-property writer. A refused exact
owner now reports at Debug at most twice per kind, with at most three original
child descriptors; no recurring scene census or normal-level trace is added.
The original shader's unused Normal input cannot be a reason to silently skip
this correction on a native layout already narrowed to its consumed streams.

The expanded actual native fixture passes196 additional assertions in twelve
pixel cases: both three/four-stream layouts, both original effects and identity,
source ancestor scale13/yaw37, or tracked camera ancestor scale13/yaw37. Every
case has visible original pixels and maximum RGB difference0. Ping cases retain
228–229 visible pixels; shield cases828–891. The source-ancestor view aims at
actual native shield bounds because the original Local particle scaling ignores
ancestor scale for emitted geometry; aiming at a fictitious scaled centre made
the exploratory fixture miss the original entirely. Production needed no new
transform change. Arbitrary shear is outside this requested rig-scale boundary.

Automatic playback now observes ten native systems across both layouts at six
samples. Clocks, counts and native visibility remain equal. Two new compiling
controls reject removing compact-layout admission or removing the required
source-scale compensation, respectively. Existing full four-stream shader,
material, ownership and six earlier causal controls remain inherited from the
first reviewed checkpoint; they are not counted as newly rerun controls here.
Repeated refused exact owners exercise and verify the finite two-per-kind Debug
cap. Strict Release compilation again has zero warnings/errors. These remain
host/native ownership and pixel observations, not new Adreno acceptance.
