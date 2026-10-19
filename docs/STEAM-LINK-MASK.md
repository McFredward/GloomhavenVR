# Steam Link mixed-reality development

The current streamed implementation uses only the **paired ordinary-RGBA route**.
Its [companion setup](https://github.com/McFredward/steam-link-native-passthrough#readme)
installs matching PC and Frame parts. The former Frame-only atlas, its quality
lease and runtime fallback were retired at the maintainer's request on 2026-10-19.
[Historical evidence](https://github.com/McFredward/steam-link-native-passthrough/blob/main/docs/history/ATLAS-RECEIVER.md)
does not describe a currently selectable method.

## Player controls and dependencies

Open **VR Options → World → Scenery**, choose **Mixed Reality**, then enable
**Steam Link native passthrough** below the environment choices. German:
**VR-Optionen → Umgebung → Schauplatz → Steam Link: natives Passthrough**.
The choice is exposed for an identified Steam Frame over Steam Link; its saved
preference defaults on, while the Mixed Reality master defaults off. Changes
apply immediately. Opening VR Options does not impose a feature timeout.

Detection of the headset/transport is not proof that matching companions are
installed. The game negotiates current source permission before emitting its
ordinary-RGBA contribution/coverage output. Missing, incompatible or disconnected
peers keep ordinary opaque presentation; they do not activate an atlas or a key-
colour fallback. An explicitly chosen Virtual Desktop chroma-key mode is separate.
Standalone Frame passthrough uses its own backend and needs no streamed companion.

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

The new route does not depend on a visible atlas header. Its design therefore
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
