# Steam Link mixed-reality development

This page describes the game-side source. The companion project's
[compatibility page](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/COMPATIBILITY.md)
separates the stable Frame-only result from the newer paired preview. Historical
research/build notes describe their own checkpoints, not the current install flow.

## Player controls and dependencies

Open **VR Options → World → Scenery**, choose **Mixed Reality**, then enable
**Steam Link native passthrough** below the environment choices. In German the
path is **VR-Optionen → Umgebung → Schauplatz**, with **Steam Link: natives
Passthrough**. Connection detection exposes this choice only after identifying
Steam Frame over Steam Link. Its saved preference defaults on for an identified
connection; the Mixed Reality master defaults off and saved choices are retained.

Both switches apply immediately, including while VR Options remains open. The
native choice uses independent coverage, not colour replacement. Turning it off
returns to the chroma-key method; turning Mixed Reality off restores the selected
environment. There is no timeout. Standalone Frame passthrough uses its own native
backend and requires no streaming companion.

The streamed source requires the matching receiver. Detection of the headset,
SDK or a saved preference is not evidence that a receiver is installed or active.
With only the legacy route available, an enabled source and a missing receiver
can expose the packed image. Turn native streaming off if the picture is wrong.
Building/enabling the game mod changes no Frame files.

## Shared source and rendering ownership

The actual MultiPass head renders once. Two alpha-only commands use that eye's
existing opaque depth. Known owned transparent materials temporarily borrow
alpha-correct derivatives only during that head's render. Other cameras and the
game's material objects retain their original rendering. The producer supplies
RGB contribution **E** and independent coverage **A** from the same current eye:

```text
composite = E + (1 − A) × local_camera
```

Do not multiply E by A twice or derive A from a colour. Additive contribution at
A0 must survive; an RGBA render target alone is not proof of correct opacity.
There is no CPU image readback or second scene camera. Unsupported colour space,
renderer, stereo shape, unknown eye or lost ownership keeps ordinary output and
retires owned commands/materials. The currently admitted source is built-in
rendering, Gamma SDR UNorm and MultiPass.

Coverage production still does not certify every native VFX, refraction shader,
background-sampling material or late explicit draw. Refraction of the real room
would require its camera content; the game does not receive that camera video.
Focused source audits cover [depth](research/steamlink-eye-coverage-runtime-20261017.md),
[same-eye output](research/steamlink-scene-output-20261017.md) and
[transparent materials](research/steamlink-transparent-materials-runtime-20261017.md).

## Two output routes

| Route | Source output | Current status |
|---|---|---|
| Stable Frame-only atlas | E and A packed into one current per-eye video image; ordinary PC sender | Native passthrough and corrected stereo edges demonstrated in the headset; packing can reduce sharpness |
| Paired PC + Frame preview | Ordinary RGBA colour source after an explicit compatible-source grant; sender/receiver companions carry exact associated coverage | Windows/NVIDIA/HEVC software-checked preview; retained quality and combined menu picture **not yet hardware-confirmed** |

The two routes share source coverage but not admission. `SteamLinkCoverageBridge`
observes the native sender's source IPC with bounded asynchronous work.
`CanAcceptOrdinaryRgba` grants the source format; `CoverageOperational` is a
separate reported state. Neither is a visible headset result. The source must
not wait for operational coverage before sending ordinary RGBA if that would
prevent the new encoder generation from reaching its first valid frame.

`SteamLinkSceneRuntime` selects the route on its cold ownership boundary. A route
change retires the current output and begins a fresh complete stereo epoch; it
must not repack only the second eye of an in-flight pair. Permission is checked
again at the actual rendering boundary. Loss cannot authorize a stale mask or a
previous source epoch. Cancellation and retirement of the observer run off the
XR thread and cannot permit overlapping observer workers.

The paired transport requires authenticated compatible peers and exact source,
frame, eye and extent association. Its coverage metadata travels with the actual
HEVC access unit; a separately received latest mask, timestamp or compositor
submit counter is insufficient. The sender preserves the ordinary colour route;
this intention is not a measured resolution/latency improvement. For protocol
and adapter contracts see the companion project's
[developer guide](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/DEVELOPING.md)
and [bridge protocol](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/protocols/stream-bridge-v1.md).
Linux/Proton connection detection does not establish native Linux sender support.

## Stable atlas and edge-quality policy

The current legacy profile is `scene-contribution-atlas-v3`, with equal 2048²
logical colour/opacity grids inside a 3168² carrier. Twelve separated neutral-grey
header cells recognize the layout. They neither threshold coverage nor authenticate
a session, body or exact UV registration. The receiver samples colour and coverage
through the original native invocation's current image/chart, retaining admitted
native decode, reprojection, distortion and foveation mapping.

A larger opacity plane does not preserve the ordinary stream's colour allocation:
the scene occupies only 2048/3168 of the carrier's linear size before encoding.
The native camera remains local. Legacy v1 stays accepted; v2 is quarantined after
a stereo failure and is not justified by its earlier ideal GPU results. See the
[v3 record](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/research/robust-native-coverage-20261018.md) and
[v2 regression review](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/research/eye-divergence-20261018.md).

**World → Scenery → Steam Link edge quality** defaults on for eligible legacy
native streaming. A separate isolated public OpenVR helper temporarily disables
foveated encoding and pre-encode sharpening and requests a uniform video width
aligned upward from the current XR carrier extent (4040 becomes 4096). Higher
existing manual widths are preserved. It does not change the application's eye
allocation, render scale or viewport; Mixed Reality is not a resolution control.
Increasing encoder width cannot restore source samples lost in atlas packing.

All four typed settings are snapshotted before any write. Restoration compares
owned values and preserves observed manual edits. Explicitly absent supported
settings are removed again on release; an effective default is not proof that a
key was originally absent. Off, disconnection or game exit requests cooperative
restoration. A crash can prevent restoration, and same-value concurrent edits
cannot be distinguished through the public API. No active timeout is imposed.

The lease survives camera/loading/options changes while its legacy generation
remains requested. The paired ordinary-RGBA path is admitted only after that
legacy quality lease is released; it must not inherit atlas-specific encoder
changes. SteamVR setting readback is not evidence of negotiated/decoded video
resolution. See the [quality investigation](https://github.com/McFredward/steam-link-native-passthrough/blob/dev/docs/research/stream-quality-comparison-20261018.md).

## Local Steam menu and focus

For the legacy atlas, `VRSession.InputFocus` must explicitly be true at the cold
and render ownership seams. False or unknown focus temporarily restores ordinary
output without changing the desired option or dropping the quality lease. Focus
regain requires a fresh complete stereo pair. Actual PC logs establish focus
loss for a full dashboard; not every local quick menu/bar variant is confirmed
to produce that signal.

The paired ordinary-RGBA path is deliberately not gated by this legacy-header
workaround. Simultaneous native game/menu composition remains a headset test,
not a conclusion from this branch or from a source grant. The game's own VR
Options does not pause native MR.

## Host identity: Windows and Proton

Gloomhaven and the mod are Windows applications. A Linux gaming host runs the
same PE reader under Proton, not a separate native Linux helper. A bounded child
loads the pinned public OpenVR SDK while Unity retains its active OpenXR session.
The active SteamVR session and background-client initialization guard this lookup;
a Windows process-name census cannot establish that native Linux SteamVR runs.

The child inherits OpenVR overrides and Proton's prefix/runtime variables. Let
the SDK use Proton's Windows OpenVR paths and `C:\vrclient` bridge; do not parse
the Linux registry from the mod or initialize another OpenVR client inside Unity.
Valve documents the [background-client contract](https://github.com/ValveSoftware/openvr/blob/v1.26.7/headers/openvr.h)
and the [Proton bridge](https://github.com/ValveSoftware/Proton/blob/proton_11.0/proton).
This lookup does not prove live Linux SteamVR/Steam Link interoperability or
support for a native Linux paired sender.

Pinned resources are extracted under
`%LOCALAPPDATA%\GloomhavenVR\SteamLink\<SHA256>` in the Windows profile or Proton
prefix. Inherited `LOCALAPPDATA`, then the Windows known-folder API, establish
the base; missing/invalid folders have no guessed system/install fallback.
Resource, directory, file and pin failures are distinguished from API failure.
The shader bank remains reproducible with `scripts/steamlink-scene-render/build.py`.

## Retired diagnostics and evidence

The synthetic source/test-pattern setting was removed at the maintainer's request.
Historical screenshots, scripts and dated results are not a current player feature
or a replacement headset pass. An old `dev.gloomhavenvr.steamlink.cfg` is neither
loaded nor silently deleted. Current player instructions live in the README and
installation guides; runtime compatibility/paired setup live in the companion
project. Gameplay and multiplayer protocols are separate from this output route.
