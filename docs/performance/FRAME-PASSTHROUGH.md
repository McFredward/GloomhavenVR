# Steam Frame passthrough investigation

Status: 2026-10-03. No native Frame mixed-reality path is implemented or proven.

Valve documents monochrome passthrough on the [Steam Frame hardware page](https://store.steampowered.com/hardware/steamframe).
Its [developer documentation](https://partner.steamgames.com/doc/steamhardware/steamframe)
does not establish an application camera-composition path for this Windows game
under Proton, or a Steam Link chromakey replacement equivalent to Virtual Desktop.
Hardware passthrough and application-controlled mixed reality are separate capabilities.

The supplied standalone log's latest block, 2026-10-03 13:04:54, identifies
SteamVR/OpenXR 2.17.10, `wineopenxr64.json`, Unity 2021.3.5 and D3D11. Its 50
offered extensions include neither `XR_FB_passthrough` nor `XR_HTC_passthrough`.
That excludes those vendor paths for this run, but does not establish whether
the core runtime supports transparent environment composition. The mod currently
uses an opaque Virtual Desktop chromakey background (`MixedReality.cs`,
`key.a = 1f`); green remains green without receiver-side chromakey support.

The next useful capability check is
[`xrEnumerateEnvironmentBlendModes`](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrEnumerateEnvironmentBlendModes.html)
for the actual system and `PRIMARY_STEREO`. This query is not in the current logs.
Run it independently for standalone Proton and PC OpenXR with Frame connected
through Steam Link; a result from one runtime does not certify the other.

- If `XR_ENVIRONMENT_BLEND_MODE_ALPHA_BLEND` is offered, prefer compositor
  composition. Eye render targets must retain alpha, projection layer flags must
  permit blending, and `XrFrameEndInfo.environmentBlendMode` must select the
  enumerated mode. The streaming receiver must also support that presentation.
  See [environment blend modes](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrEnvironmentBlendMode.html),
  [frame submission](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrFrameEndInfo.html)
  and [composition flags](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrCompositionLayerFlagBits.html).
- If only opaque composition is offered, inspect actual camera availability via
  `IVRTrackedCamera.HasCamera` before considering an independent camera layer.
  The [official OpenVR interface](https://raw.githubusercontent.com/ValveSoftware/openvr/master/headers/openvr.h)
  defines this query but does not guarantee access to Frame camera frames under
  Proton or forwarding to the streaming PC. Stereo reprojection and additional
  processing would also need evaluation on standalone hardware.
- If neither capability is available, runtime/Steam Link support is required.
  A mod setting or library shortcut cannot supply a missing compositor feature.

Do not disable Steam's theater presentation globally: the maintainer explicitly
wants to retain that feature for other flat games. Preserve the existing Virtual
Desktop path and keep unsupported Frame capabilities out of player setup claims.
