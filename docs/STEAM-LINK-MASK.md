# Steam Link mixed-reality development

## Real-image source, Build686

`VR Options → World → Scenery` now offers **Steam Link native
passthrough**, default off. This is the real-image producer, not the retired
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
changed by building or enabling the PC source. A fresh concrete approval is
required before preparing its temporary Frame component and native camera
setting. The existing standalone native MR backend is separate and unchanged.

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
