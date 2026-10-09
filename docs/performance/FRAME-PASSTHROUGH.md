# Steam Frame mixed reality: implementation decision

Updated: 2026-10-09. Build653 implements capability-gated native alpha
composition for Steam Frame standalone. The renderer and menu have automated
coverage; actual Proton/compositor output is still awaiting a headset test.

## What the current mod does

`Core/MixedReality/MixedReality.cs` selects native OpenXR alpha composition
for a Frame standalone installation detected by the existing setup marker.
That marker is cached when the XR instance starts; a Frame headset streamed
from a PC does not select this backend merely by its headset name.

The actual Wine/OpenXR system must enumerate `ALPHA_BLEND` for `PRIMARY_STEREO`
and begin a live session. Unity must then accept the requested mode before
camera transparency or sky suppression begins. Empty background pixels become
transparent black; table, scenario, figures, cards and windows keep rendering.
No room-camera image is copied, networked or rendered by a second game camera.

PC installations retain the original opaque selected key colour for compatible
streaming software such as Virtual Desktop. This implementation does not claim
native passthrough for stock Steam Link.

## Evidence and the two different paths

| Execution path | Implementable candidate | Evidence still needed |
|---|---|---|
| Standalone Windows game under Proton/FEX | OpenXR core `ALPHA_BLEND` in the game's existing Unity XR instance | Enumerated mode through the **actual Wine OpenXR bridge**, plus visible correct stereo composition |
| PC game streamed through stock Steam Link | A receiver-supported alpha/mask composition path | Evidence that the stock streaming receiver accepts and composites it; PC alpha support alone is insufficient |
| Streaming through a new custom receiver | Local Frame OpenXR scene application combines streamed game colour and mask with local passthrough | A substantial new streaming client, not a small mod flag or Steam shortcut |
| Existing Virtual Desktop chromakey | Keep the current key-colour backend | Existing receiver/headset compatibility, independent of stock Steam Link |

Valve's [Steam Frame developer overview][valve-frame] establishes SteamVR and
OpenXR execution models, but does not document a stock Steam Link chromakey
or game alpha-mask interface. This absence is not proof that no private or
future interface exists.

A [first-person developer report in Valve's OpenVR issue tracker][overlay-issue],
dated 2026-10-03, reports `ALPHA_BLEND` for native local scene applications on
Frame/SteamVR 2.18.2. The same report describes camera-frame failures and no
equivalent passthrough operation for a local overlay over Steam Link. This
is a reported measurement, **not a Valve support guarantee** and not a test
of our older 2.17.10 runtime, Windows bridge or Unity renderer.

The passthrough API-layer author's [compatibility list][rectus-compat] explicitly
lists Frame built-in and Arcturus camera access as unknown/untested and says
camera images are not forwarded when streaming. The layer is therefore not
a verified drop-in workaround for this headset. More streaming sources and
the private system-toggle distinction are in
[FRAME-PASSTHROUGH-STREAMING.md](FRAME-PASSTHROUGH-STREAMING.md).

## The maintainer's latest log

`.planning/debug/steam_frame/LogOutput.log` identifies **Build651** and
SteamVR/OpenXR **2.17.10**, reached through
`C:\openxr\wineopenxr64.json`, with Unity's OpenXR plugin **1.10.0**. The
successful `openxr-diagnostics.log` block is dated 2026-10-09 11:50:59.

The 50 offered extensions include neither `XR_FB_passthrough` nor
`XR_HTC_passthrough`. That rules out those vendor extensions in this run.
It does **not** rule out core alpha composition: environment blend modes
are enumerated separately and are absent from the supplied diagnostic report.

There is no second, current PC/Steam-Link blend-mode measurement in these
logs. A standalone result must never be labelled a streaming result.

## Implemented standalone path

### Compatibility and the menu

`OpenXrEnvironmentBlendFeature` receives the existing Unity instance, system
and session callbacks. Its bounded probe resolves
`xrEnumerateEnvironmentBlendModes` through Unity's existing
`xrGetInstanceProcAddr` pointer. It queries each actual instance/system once,
using the standard count/data calls and validated return codes/counts. It never
creates another XR instance or requires a vendor-specific passthrough extension.
The enumeration is part of [core OpenXR1.0][enumerate].

`FrameNativePassthrough` exposes checking, unsupported, failed-query,
failed-activation and available states. There is no fabricated minimum Proton
or SteamVR version: a version string cannot prove Wine bridge support and may
misclassify backports. Successful enumeration through the active bridge is the
compatibility check. Query failures remain distinct from a list containing only
Opaque/Additive.

On standalone, unavailable MR tiles/switches are grey and remain hoverable.
The bilingual hint explains that the selected Proton and SteamVR need updating,
followed by a game restart. A pending check has its own explanation. Controls
already open repaint when capability/session status changes; generic/Advanced
writes cannot bypass the gate. An existing enabled preference remains removable
and is preserved without exposing a green/black substitute. Native passthrough
has no chromakey-colour control. Other environment choices remain available.

### Blend ownership and asynchronous acceptance

Unity OpenXR1.10 already exposes protected
`OpenXRFeature.SetEnvironmentBlendMode` and `GetEnvironmentBlendMode`.
An unsupported request falls back to runtime preference, so the feature checks
actual readback. [Unity1.10 feature API][unity-feature]

The shipped native plugin **queues** the requested mode. Its getter reads the
actual submission mode, not that queue. The controller therefore requests
AlphaBlend once and leaves ordinary VR visible while waiting for acceptance.
A bounded activation deadline avoids indefinite unavailable presentation; its
maintenance grace prevents a single suspended frame from being treated as
an immediate incompatibility. Steady state performs no repeated enumeration
or native mode write.

The controller records its previous mode, cancels queued activation on off,
restores only its owned selection and respects external mode changes. Session
end/exiting restores while handles remain live; loss/destroy discards stale
ownership without dereferencing dead handles. New instances and sessions reset
capability/failure lifetime. Rapid off/on must handle queued restoration rather
than mistake an unconsumed AlphaBlend readback for externally owned composition.

### Camera, sky and alpha

Native MR clears the rig head to `(0,0,0,0)` only after mode acceptance. It reuses
existing sky suppression and UI-only MR backings. Unsupported, pending or failed
native activation leaves the normal selected environment. Turning off/VR stop
restores saved camera flags, colours, head HDR permission, sky and blend state.
No MR backing geometry is added inside the play area.

The head's HDR permission is temporarily disabled to avoid RGB-only HDR
intermediates. The head is created with Camera, TrackedPoseDriver and listener,
without an image-effect stack. Native post-processing/fog components remain
unconditionally suppressed by the existing VR compatibility path. MSAA, eye
resolution, other camera HDR permissions and gameplay materials are untouched.

The shipped `UnityOpenXR.dll` hash is
`2275da2750ebc9c815386604f73f0450b03fed6f44dafdeb15e978633e4866f5`.
The projection builder sets flags6 (`SOURCE_ALPHA`2 + `UNPREMULTIPLIED_ALPHA`4).
Frame-end submission retains source alpha for actual mode3 and removes it for
other modes, then writes that actual mode into `XrFrameEndInfo`. This allows the
official feature API path without a private compositor hook or `xrEndFrame`
replacement. Flag meaning: [OpenXR composition flags][flags].

The actual compositor, both eye swapchains, game shader edges, particles and
water still need hardware verification. A real Unity RGBA test is evidence for
camera output; it cannot establish what Proton/SteamVR shows in the headset.

## Streaming implementation boundary

For stock Steam Link, setting alpha on the PC must not be assumed to transmit
a transparency mask or enable local headset passthrough. The receiver owns
both the decoded eye picture and the local camera composition. A usable
solution needs a documented/successfully measured receiver capability or
an explicit integration with the receiver.

The fallback engineering route is a **custom Frame receiver**: render/capture
game colour and a separate visibility mask on the PC, encode/transport them
with the same frame identity, decode them on Frame, and submit the locally
composited scene with AlphaBlend. It also needs low-latency tracking/input,
prediction, controller bindings, audio, synchronised colour/mask frames and
acceptable decode/reprojection cost. This is an additional project; it is not
the recommended first step while standalone alpha remains unmeasured.

System passthrough controls can expose the room or blend the **whole** game
with it. That does not selectively remove only our background and cannot
replace game-aware MR. A library entry or an early VR launch fixes launch
presentation, not passthrough composition. Keep Steam's theatre feature for
other flat games, as explicitly requested by the maintainer.

## Acceptance and current delivery

Build653 supplies the native backend, availability UI and original PC fallback.
Focused native-boundary tests cover enumeration, lifecycle, asynchronous mode
acceptance/cancellation, refusal, failure and restoration. Real Unity graphics
tests execute the production MR tick and restoration, checking transparent
background/opaque geometry pixels and camera/HDR state. Actual uGUI/TMP tests
cover disabled MR choice hover and dynamic availability.

The latest supplied hardware log is still Build651/SteamVR2.17.10 and contains
no environment blend enumeration. It does not establish whether that specific
combination supports alpha. Test the new build on Frame, retain its one-shot
`OpenXR environment blend capabilities` report, then check:

1. Unsupported runtime: grey MR choice, readable update hint, ordinary VR intact.
2. Compatible runtime: enable MR; table/windows stay solid over the real room in
   both eyes, with no green screen or duplicate room-camera draw.
3. Toggle off/on, including quickly, open map/scenario and stop/restart VR;
   normal environments and windows remain usable.
4. PC streaming: selected chromakey and existing MR UI backings still work.

No stock Steam Link passthrough, room-camera access, headset result or measured
MR performance gain is claimed. Exact check receipts and boundaries are in
[FRAME-653-MR-REVIEW.md](../../.planning/FRAME-653-MR-REVIEW.md).

[valve-frame]: https://partner.steamgames.com/doc/steamhardware/steamframe
[overlay-issue]: https://github.com/ValveSoftware/openvr/issues/1926
[rectus-compat]: https://github.com/Rectus/openxr-steamvr-passthrough/wiki/Compatibility
[enumerate]: https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrEnumerateEnvironmentBlendModes.html
[blend]: https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrEnvironmentBlendMode.html
[unity-feature]: https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.10/api/UnityEngine.XR.OpenXR.Features.OpenXRFeature.html

[flags]: https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrCompositionLayerFlags.html
