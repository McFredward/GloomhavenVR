# Steam Link mixed-reality development

The default streamed route uses Valve’s OpenXR alpha composition. Valve
introduced it in [SteamOS 0.5.8 Beta with the PC SteamVR Beta](https://steamcommunity.com/app/4165890/announcements/detail/527161296156696117?l=english).
When a running SteamVR session advertises `XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND`
for stereo, the game can choose it through Unity OpenXR and clear the background
transparently after acceptance. The game queries and verifies this on the live
session. Standalone retains its launch-marker admission and the same OpenXR
blend controller.

## Legacy companion

Open **VR Options → Advanced → Environment & world** and turn on
**Legacy Steam Link companion** for an identified Steam Frame over Steam Link.
In German, use **VR-Optionen → Erweitert → Umgebung & Welt →
Älterer Steam-Link-Companion**. Select **Mixed Reality** under **World →
Scenery** to enable it. The companion choice defaults off and overrides native
OpenXR passthrough only on the matching connection. The former
`[MixedReality] SteamLinkNative` value remains stored for compatibility.
The opt-in now lives in `[MixedReality] SteamLinkCompanion`.

The paired ordinary-RGBA route requires the matching
[PC and Frame companions](https://github.com/McFredward/steam-link-native-passthrough#readme).
After identifying the headset and transport, the game negotiates current source
permission before emitting coverage. Missing, incompatible or disconnected peers
keep the ordinary opaque image visible. The former Frame-only atlas and its
quality lease remain [historical](https://github.com/McFredward/steam-link-native-passthrough/blob/main/docs/history/ATLAS-RECEIVER.md).
Virtual Desktop uses its existing key-colour mode. The rest of this document
describes the companion route.

## Source and rendering ownership

The actual MultiPass head renders once. Alpha commands use that eye's existing
opaque depth, and known owned transparent materials borrow alpha-correct variants
only during that head's render. Game material objects and other cameras keep
their original rendering. The same current eye provides RGB contribution E and
independent coverage A:

```text
composite = E + (1 − A) × local_camera
```

E must not be multiplied by A again. Positive RGB at zero coverage is valid
emission; a colour is never used as a mask. There is no second scene camera or
CPU image readback in the game source. The admitted path is built-in rendering,
Gamma SDR UNorm and MultiPass. Unknown eye/renderer/colour space or ownership
loss restores ordinary output and retires commands/materials.

See [ordinary-RGBA source contract](STEAM-LINK-COVERAGE-SOURCE.md) and the focused
[depth](research/steamlink-eye-coverage-runtime-20261017.md),
[same-eye output](research/steamlink-scene-output-20261017.md) and
[transparent materials](research/steamlink-transparent-materials-runtime-20261017.md)
research. Refraction/background-sampling effects are not all certified. The game
never receives the real camera image, so its shaders cannot refract that room.

## Compatible-source grant versus final image proof

`SteamLinkCoverageBridge` observes source capability off the Unity thread.
`CanAcceptOrdinaryRgba` grants the source format; `CoverageOperational` is a
separate reported state. Requiring the second before supplying the first real
ordinary-RGBA frame would create a preparation cycle. Neither flag establishes
a visually correct headset result.

`SteamLinkSceneRuntime` admits a complete current stereo epoch, checks permission
again at the rendering seam and retires stale ownership. Late replies and the
previous eye pair never authorize a new epoch. Observer cancellation/retirement
stays off the XR thread, with no overlapping workers during teardown.

The native sender binds the actual shared game texture, PID, generation,
submitted bounds and eye/layer to an accepted encoder input and HEVC access unit.
Authenticated coverage travels with that same AU. The Frame must bind it to the
current decoded/selected image and revocable lease; a latest mask, approximate
time or compositor counter cannot substitute for this chain. Normal colour,
resolution, MSAA and Steam Link settings remain under the player's control.

## Headsets, transport and menus

Connection discovery includes Windows and Proton-aware game paths. The initial
paired sender is Windows/NVIDIA/HEVC only; a Windows game under Proton does not
make its Linux SteamVR host compatible with a Windows driver DLL.

The paired route does not depend on a visible atlas header. Its design therefore
permits the game and local Steam menu to coexist over native camera video while
preserving native menu shaders. The final combined menu/game headset picture
and retained full-resolution quality remain explicit hardware checks. No
source-level or software test pass may be described as their visual confirmation.

## Evidence and maintenance

The old test-pattern setting is not a player feature. Dated screenshots,
research documents and build notes remain historical evidence. An old
`dev.gloomhavenvr.steamlink.cfg` is not loaded or silently deleted.

Player installation and runtime compatibility are maintained in the
[companion project](https://github.com/McFredward/steam-link-native-passthrough#readme).
Current support begins with GloomhavenVR 1.2.0, or a corresponding development
build. Game multiplayer protocols and standalone passthrough are independent.
