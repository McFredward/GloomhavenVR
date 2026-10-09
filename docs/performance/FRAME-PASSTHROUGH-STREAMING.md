# Steam Frame mixed reality over PC streaming

Research date: 2026-10-09. Source base: dev `2f6819eee`, ModBuild651. No runtime
change, headset experiment, external installation or Steam configuration change
was performed. This is a feasibility decision, not a supported setup guide.
The [main investigation](FRAME-PASSTHROUGH.md) covers standalone integration.

## Practical conclusion

A transparent background in the PC game is not sufficient. A component **on the
Frame** must combine the streamed virtual scene with its local room camera.
The existing green-background mode only supplies a key colour; it does not obtain
passthrough or implement that receiver-side operation.

For stock Steam Link, there is currently no verified selective passthrough route
for this mod. This is not a claim that Valve can never support it: the current
evidence identifies the missing receiver capability and rules out one tempting
workaround. Keep the proven Virtual Desktop path unchanged and investigate native
standalone composition first.

## What current primary sources establish

Valve's [Frame development documentation](https://partner.steamgames.com/doc/steamhardware/steamframe)
distinguishes streamed PC games from applications running on the device. Its
[custom-engine guide](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom)
supports Linux ARM64 or Android development and recommends OpenXR. Neither page
provides an application-facing Steam Link chromakey or passthrough-mask contract.
Absence from these pages alone is **not** proof of unavailability.

The author of the [OpenXR SteamVR Passthrough API Layer](https://github.com/Rectus/openxr-steamvr-passthrough)
documents a PC-side compositor with chromakey/alpha overrides. Its
[compatibility table](https://github.com/Rectus/openxr-steamvr-passthrough/wiki/Compatibility),
updated 2026-10-06, explicitly lists both built-in Steam Frame and Arcturus Vision
cameras as unverified, with no working tested modes and camera feeds not forwarded
when streaming. Installing this layer on the PC therefore does not currently
establish a Frame solution.

A [Frame developer's report in Valve's OpenVR issue tracker](https://github.com/ValveSoftware/openvr/issues/1926)
provides a separate on-device result for SteamVR2.18.2 and Arcturus: native scene
apps offered `ALPHA_BLEND`, but an overlay above a Steam Link stream had no
corresponding passthrough path; camera frame calls failed even when `HasCamera`
was true. This is first-hand third-party evidence, **not a Valve confirmation**
or a measurement on the maintainer's firmware.

## Why the readily available layer does not fix it

The Rectus layer consumes camera pixels and composites them **before** the PC
runtime submits the game image. Its actual
[camera initialization](https://github.com/Rectus/openxr-steamvr-passthrough/blob/603c4c8e8d707ba2130c6a472ce5e539e3841a72/XR_APILAYER_NOVENDOR_steamvr_passthrough/camera_manager_openvr.cpp)
requires `HasCamera`, `AcquireVideoStreamingService` and working frame access.
A headset's system camera view is not evidence that those calls work on the PC.

The reviewed code is Windows-oriented: its
[layer manifest](https://github.com/Rectus/openxr-steamvr-passthrough/blob/603c4c8e8d707ba2130c6a472ce5e539e3841a72/XR_APILAYER_NOVENDOR_steamvr_passthrough/XR_APILAYER_NOVENDOR_steamvr_passthrough.json)
loads a DLL; its
[setup utility](https://github.com/Rectus/openxr-steamvr-passthrough/blob/603c4c8e8d707ba2130c6a472ce5e539e3841a72/passthrough-setup/passthrough-setup.cpp)
registers a Windows implicit layer; its
[implementation](https://github.com/Rectus/openxr-steamvr-passthrough/blob/603c4c8e8d707ba2130c6a472ce5e539e3841a72/XR_APILAYER_NOVENDOR_steamvr_passthrough/layer.cpp)
uses Windows APIs and checks for SteamVR/OpenXR. This is not a drop-in native
Linux ARM64 Steam Link receiver. Proton compatibility would be a separate test,
and would not itself solve unavailable camera frames.

## Concrete implementation choices

| Approach | Required implementation | Current decision |
| --- | --- | --- |
| Steam Link performs composition locally | A supported receiver chromakey operation, or transported scene alpha and a supported local passthrough blend mode | Preferred streaming architecture; no verified public integration contract found |
| PC-side Rectus layer | Frame camera frames, timing and calibration forwarded to the PC; the layer keys GloomhavenVR's green background | Blocked by current camera-forwarding evidence; do not prescribe installation as a fix |
| Native receiver integration | Decode the PC stereo stream locally, key green to transparent, submit the result with supported native environment blending | A separate streaming-client project, conditional on native composition and access to receiver frames; not a small mod patch |
| Native receiver API layer | Intercept the receiver's actual scene submissions, key their textures locally and select a supported blend mode | Only investigate after proving which submission API the receiver uses and that interception is possible; a game-side PC layer cannot intercept the headset receiver |
| Global translucent system passthrough | Blend the camera view over the entire VR image | Possible limited experiment, not selective diorama mixed reality; the virtual table also becomes translucent |

The native receiver choices are architectural proposals, not claims of existing
Frame support. A new receiver also needs pose prediction, reprojection, input,
audio, synchronization and sufficient decode/composition performance. Do not
replace Steam Link based solely on a transparent test cube.

OpenXR
[environment blend modes](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrEnvironmentBlendMode.html)
operate between the composed application image and the real world. Alpha needs
to survive the local render target and frame submission; ordinary transparency
between virtual layers or an overlay texture does not imply room visibility.
For streaming, verify the entire pipeline rather than assuming that alpha in
the PC render target survives encoding and reaches Frame composition.

## Bounded checks before implementing streaming support

1. Record the headset firmware, SteamVR versions on both ends, stream mode and
   active PC OpenXR runtime. On the **PC with Frame connected**, inspect whether
   SteamVR Room View has an actual camera image. A missing option is only a clue;
   the conclusive programmatic check needs acquisition and a valid frame, not
   `HasCamera` alone. Valve defines these calls in
   [IVRTrackedCamera](https://github.com/ValveSoftware/openvr/blob/master/headers/openvr.h).
2. Separately probe native Frame and the game's Proton session with
   [xrEnumerateEnvironmentBlendModes](https://registry.khronos.org/OpenXR/specs/1.0/man/html/xrEnumerateEnvironmentBlendModes.html)
   for `PRIMARY_STEREO`. An alpha result in a native scene app does not establish
   support in Proton or in the streamed PC runtime.
3. If native blending is proven, test a local transparent cube before modifying
   the game or receiver. That experiment must preserve texture alpha, use the
   [layer alpha flags](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrCompositionLayerFlagBits.html)
   correctly and select the enumerated mode in
   [XrFrameEndInfo](https://registry.khronos.org/OpenXR/specs/1.0/man/html/XrFrameEndInfo.html).
4. Ask Valve for a documented per-application Steam Link passthrough-mask/alpha
   route before committing to a receiver fork. No message has been sent as part
   of this investigation.

An optional existing experiment is
[Frame Passthrough Shortcuts](https://github.com/KominoVR/frame-passthrough-shortcuts):
its author describes locally switching between opaque and translucent system
passthrough, with optional RGB hardware. Its
[reviewed source](https://github.com/KominoVR/frame-passthrough-shortcuts/blob/032841dcde61059b25d33a487bf202a30fbf58f1/src/openvr_camera_source.cpp)
uses private `IVRCameraPassthroughInternal_001` configuration methods, tested by
the author on firmware20260918. It exposes neither camera frames nor a per-game
mask. This unsupported interface is a useful research lead, not a production
dependency or a safe guarantee for later firmware. Nothing was installed here.

## Separate launch and overlay concerns

A VR-marked library shortcut, avoiding an initial theater view, and displaying
the correct library artwork address startup and presentation. They do not create
room-camera composition. An OpenVR overlay's own alpha blends that overlay into
the virtual compositor image; it does not authorize punching holes through an
opaque Steam Link scene to reach passthrough. Valve's
[overlay texture API](https://github.com/ValveSoftware/openvr/wiki/IVROverlay::SetOverlayTexture)
is an overlay submission mechanism, not a camera-provider contract.

Do not disable theater presentation globally. The maintainer explicitly wants
to retain it for other flat games. Do not advertise native or streamed Frame
mixed reality in player installation instructions until a headset experiment
proves the selected route end to end.
