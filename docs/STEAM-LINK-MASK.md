# Steam Link mixed-reality development

## Real-image source

`VR Options → World → Scenery` now offers **Steam Link native
passthrough** only after identifying a connected Steam Frame through Steam Link.
Its preference defaults on for that identified connection; the general Mixed
Reality master still defaults off, and saved preferences are retained.
This is the real-image producer, not the retired
test pattern. It requires the separately prepared matching Frame receiver;
turning it on without that receiver displays an encoded image. Choose the
Mixed Reality environment, then enable native streaming. Both switches apply
immediately; opening VR Options does not pause the real-image source. Disabling the master MR
switch restores the chosen environment; disabling only native streaming returns
to the existing chroma-key backend. There is no automatic timeout.

The actual MultiPass head renders once. Two alpha-only commands use that eye's
existing opaque depth, and known owned transparent materials temporarily borrow
alpha-correct derivatives only while that head renders. Other cameras and the
game's material objects retain their original rendering. The final image effect
packs scene contribution RGB and coverage from the same source into one image.
The unmodified Steam Link sender binary transports it; the Frame decoder retains native
reprojection/foveation and combines the contribution with camera passthrough.
Emission at coverage zero is preserved. No CPU pixel readback or second scene
camera is introduced.

The initial profile is Gamma SDR UNorm/MultiPass. Wrong formats, lost ownership,
unknown eyes and shutdown retain ordinary output and release owned
commands/materials. The current profile is still experimental: all native VFX,
refraction, late explicit command-buffer draws and intermediate UI-alpha semantics
are not certified by the owned shader tests. Actual XR callback order, codec
quality and headset motion still require a real-game test. No Frame files are
changed by building or enabling the PC source. Install the public matching
[Steam Link Native Passthrough receiver](https://github.com/McFredward/steam-link-native-passthrough)
separately using its documented supported SteamVR version. The existing
standalone native MR backend is separate and unchanged.

## Equal-resolution coverage and transport recognition

Build699 prepares `scene-contribution-atlas-v3` and requires its matching updated
Frame receiver. Independent coverage and scene RGB use the same2048-square
logical resolution from the same current eye source. No half-resolution alpha
box or C1 reconstruction filter remains in this path. This applies to all scene
geometry, without window tags or opacity thresholds. Genuine fractional edges,
glass and contribution at zero coverage remain distinct from packing softness.

The3168-square carrier keeps v2's four haloed coverage strips around central RGB.
V3 changes only its format recognition: twelve separated128-square neutral-grey
cells, in two unused corners, use four well-separated levels with inclusive
sixteen-code windows. These windows identify the format; they do not threshold
coverage or authenticate a frame, eye, body or exact UV registration. Both
planes still share the original native invocation's current image and R1 chart.
No previous mask, sticky decision, extra camera, descriptor or queue is added.

Build697's v2 real-stream test failed with different apparent scale/passthrough
between eyes. An owned lossy-codec reproduction shows that its small32-square,
two-code-window header can be accepted in one chart and rejected in another.
The exact shipping native fragment reproduces the observed output split; the
actual failed hardware guard values and encoder parameters remain unknown.
Build698 therefore restored v1 temporarily. V2 is retained unchanged for
regression controls and must not be re-enabled from its earlier ideal GPU proof.

The new receiver recognizes v3 first, then the frozen legacy v1; historical v2
tools remain quarantined. A v3 producer with an older receiver can display the
packed image. Building/enabling the mod does not update the headset patch.
At a fixed stream resolution the larger mask consumes more video area, reducing
RGB's allocation. The source profile itself does not change SteamVR resolution
or bitrate. The separate optional Build700 quality lease below requests uniform
encoding and an explicit width through public SteamVR settings.
Owned source/codec/native tests are separate from the still-required headset
picture and streaming-cost checks. See [the bounded v3 record](../.planning/STEAM-LINK-699-ROBUST-COVERAGE.md)
and the [failed v2 review](../.planning/STEAM-LINK-698-EYE-DIVERGENCE.md).

## Streamed edge quality (Build700)

The maintainer confirmed correct stereo in Build699, but still sees shimmering
outlines. Equal virtual RGB/coverage resolution does not guarantee equal video
quality. Retained native metadata demonstrates central high-detail colour and
peripheral low-detail coverage reconstruction; source raster phases and lossy
video also impose separate limits absent in direct standalone RGBA composition.
The current hardware stream's exact reconstruction weights are not measured.

**World → Scenery → Steam Link edge quality** defaults on for eligible native
Frame/Steam Link MR. It temporarily disables foveated encoding and pre-encode
sharpening. **Steam Link encoding width (px)** requests 3072 by default, adjustable
from 1024 to 4096 in steps of 64. A higher existing manual uniform width is kept.
This costs network/decoder capacity; reduce the width if streaming stutters.
Readback confirms a driver setting, not actual negotiated video dimensions.
No supported client maximum or standalone-identical picture is claimed.

One isolated public Background OpenVR client snapshots all four typed settings
before any write, enables the complete policy, and holds it only while requested
native MR remains eligible. It survives camera, loading and options-menu changes;
Off, disconnection or game exit requests cooperative restoration. There is no
active timeout. Restoration compares each still-owned value, preserving observed
manual edits. Startup is bounded to five seconds; a pending Off can wait for that
window. Unknown restoration blocks reacquisition in this game process. Killing
or crashing the helper itself can prevent restoration; concurrent same-value
manual edits cannot be identified by public IVRSettings. Build701 also preserves an originally missing value when OpenVR specifically
reports UnsetSettingHasNoDefault, removing that temporary key again on release.
An effective default cannot be distinguished from an explicit override, so
restoration of its value does not reconstruct that override's original absence.

The helper is embedded in the mod, hash-verified before execution and uses the
same Windows/Proton public SDK bridge as connection detection. There is no Steam
binary patch or Frame update for Build700. The accepted v3 shaders, atlas, mask,
receiver, both-eye admission and fractional opacity are unchanged. Standalone,
Virtual Desktop and other connections do not acquire this lease. See the
[scoped evidence and limits](../.planning/STEAM-LINK-700-EDGE-QUALITY.md).

Build700's first hardware review did not exercise this policy: all three leases
were refused at snapshot. Build701 admits explicitly absent settings without
inventing defaults. Successful application produces `Steam Link stream quality:
active`; a refusal now reports the exact bounded key/API code. The original
hardware failure did not record that code, and visible edge improvement is not
inferred from the automated checks.

New source epochs also wait for a complete explicitly observed stereo frame
before starting atlas output at a new frame boundary. Temporary non-stereo
camera attachments close normally and can resume; genuine rendering faults
remain refusals. This stores eye/frame metadata only, not previous-eye images.

## Host identity lookup: Windows and Proton

Gloomhaven and this mod are Windows applications. A Linux gaming host runs the
same PE reader under Proton, not a separate native Linux helper. Only a bounded
isolated child loads the pinned public OpenVR SDK; Unity keeps its active OpenXR
session. The caller first requires that active SteamVR session. The SDK's
`VRApplication_Background` initialization supplies the no-server guard; a Windows
process-name census cannot establish whether native Linux SteamVR is running.

The child inherits OpenVR overrides and Proton's prefix/runtime bridge variables.
Let the SDK use Proton's generated Windows OpenVR paths and `C:\vrclient` bridge,
including its ordered runtime history. Do not parse the Linux host registry from
the mod or initialize another OpenVR client inside Unity. Valve documents the
[background-client contract](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr.h)
and implements the [Proton path bridge](https://github.com/ValveSoftware/Proton/blob/proton_11.0/proton).
This does not establish that a given Linux SteamVR/Steam Link release works;
live host/transport interoperability still needs hardware evidence.

The two pinned resources are extracted under
`%LOCALAPPDATA%\GloomhavenVR\SteamLink\<SHA256>`, inside the Windows user's profile
or Proton prefix. Prefer inherited `LOCALAPPDATA`, then the Windows known-folder
API; normalize directory separators before local-path validation. Missing or
invalid folders have no guessed installation/system-directory fallback. Exact
resource, directory, file and cached-pin stages distinguish preparation failure
from API failure. Never infer receiver installation from SDK presence.

Focused evidence is in the linked research notes for
[depth coverage](research/steamlink-eye-coverage-runtime-20261017.md),
[same-eye output](research/steamlink-scene-output-20261017.md), and
[owned transparent materials](research/steamlink-transparent-materials-runtime-20261017.md).
The small embedded shader bank is reproducible with
`scripts/steamlink-scene-render/build.py`; its verifier requires the actual
AssetBundle container, complete compiled shader inventory and program banks.

## Retired synthetic source

The maintainer requested removal of the synthetic test pattern and its VR
setting on 2026-10-17. The shipped mod no longer binds
`SteamLink/PayloadTestEnabled`, creates the test driver, replaces either eye
with a synthetic canvas, or embeds the pattern textures/manifests. The ordinary
menu and VR-options lifecycle has no diagnostic-output stop hook.

An older `dev.gloomhavenvr.steamlink.cfg` is left untouched on disk and is no
longer opened by the mod. No user configuration file is deleted or migrated.
This explicit removal supersedes the usual rule to retain persisted config keys.

The completed synthetic streaming experiment and its original source history
remain useful development evidence. Historical scripts and controls were removed
with their production source; their prior results remain in Git history and
private captures. Their retirement is not a replacement passing test result.

The [real-game coverage audit](research/steamlink-game-coverage-audit-20261017.md)
records the native-material limits that remain relevant to the new producer.
The separate receiver fork retains historical offline experiments; the
synthetic-source removal installs nothing on the Frame and does not modify
the Steam sender.

Existing standalone Steam Frame passthrough, ordinary mixed-reality options,
menu focus/availability, gameplay and multiplayer protocols are unchanged.
