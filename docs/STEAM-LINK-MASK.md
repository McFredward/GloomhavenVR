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
The unmodified Steam Link sender transports it; the Frame decoder retains native
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

## Equal-resolution scene coverage

The current producer uses `scene-contribution-atlas-v2`: independent coverage and
scene RGB have the same2048-square logical resolution, sampled from the same
current eye source. The old half-resolution alpha box/reconstruction filter is
removed from this path. This applies to all scene geometry, without window tags
or thresholds that would erase genuine partial transparency. Coverage and format
cells remain neutral grey in the video transport; scene RGB retains its colors.

A matching updated receiver is required. It recognizes v2 and retains the frozen
v1 decoder for older producers. Building/enabling the mod does not update the
headset patch. The3168-square carrier layout uses four haloed coverage strips
around the central scene. At a fixed stream resolution, the larger mask consumes
more video area, reducing the area's allocation to RGB; no SteamVR resolution or
bitrate setting is changed automatically. Keep true MSAA/partial-opacity edges
and codec limitations distinct from additional packing blur. Headset appearance
and streaming cost still require hardware validation. See
[the scoped implementation record](../.planning/STEAM-LINK-697-FULL-COVERAGE.md).

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
