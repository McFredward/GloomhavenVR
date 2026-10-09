# Steam Frame mixed reality: implementation decision

Updated: 2026-10-09. No native Frame mixed-reality presentation has been
implemented or verified on the maintainer's headset. The bounded diagnostic
probe prepared in this round is read-only; it does not turn passthrough on.

## What the current mod does

`Core/MixedReality/MixedReality.cs` removes the sky/background geometry and
clears the VR camera to the selected key colour with **alpha fixed at one**.
Virtual Desktop can replace that colour with its local camera image. Steam
Link has not been shown to do that replacement. Green or black alone is not
an instruction to the headset compositor to display the room.

The desired Frame implementation keeps the existing table, scenario, cards,
figures and windows, but clears the empty background transparently. The
headset compositor supplies its own passthrough image behind that content.
It should not send the real-room camera image over multiplayer or ask the
game to render a second camera for it.

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

## Recommended implementation: standalone first

### 1. Measure capability without changing the renderer

Register one diagnostic `OpenXRFeature` alongside the existing controller
profiles, before `InitXRSDK`. It receives the real instance and system IDs
through `OnInstanceCreate` and `OnSystemChange`. Resolve
`xrEnumerateEnvironmentBlendModes` through Unity's existing
`xrGetInstanceProcAddr` pointer, and enumerate `PRIMARY_STEREO` with the
standard two-call count/data pattern. Bound the count, validate both return
codes and report query failure separately from a supported-mode list.

Do this once per instance/system, with lifetime reset on instance teardown.
Never create another XR instance/session, replace `xrEndFrame`, reset the
swapchain, copy camera images or do per-frame probing. The successful log
must say whether `Opaque`, `Additive` and/or `AlphaBlend` were actually returned.
The query is part of [core OpenXR 1.0][enumerate], so vendor passthrough
extensions are not a prerequisite.

Run the capability probe once on standalone and independently once on the
PC with Steam Frame connected through Steam Link. A successful enumeration
only establishes the runtime mode; it does not certify the final picture.

### 2. If the standalone runtime offers AlphaBlend, use Unity's existing API

Unity OpenXR **1.10 already exposes** protected
`OpenXRFeature.SetEnvironmentBlendMode` and `GetEnvironmentBlendMode`.
An MR-specific feature can wrap those for the current session. Its public
documentation says an unsupported selection falls back to runtime preference,
so read the selected mode back and fail back to ordinary VR if it was rejected.
An engine/package upgrade or a new Meta-specific passthrough layer is not
the first required step. [Unity 1.10 feature API][unity-feature]

The presentation integration then needs these concrete changes:

1. Keep the existing sky/environment hiding and UI-only MR backings.
2. For the verified native backend, clear the head camera to transparent
   black `(0, 0, 0, 0)` instead of an opaque key colour.
3. Select `AlphaBlend` through the feature, and validate the selected mode.
4. Check that the native projection layer permits source-alpha blending and
   that eye targets and every active post-processing pass preserve useful
   alpha. A transparent Unity clear does not prove that the submitted eye
   image still has transparent background pixels.
5. Keep normal game geometry opaque and test both eyes, text, card art,
   particles, water, fades and window materialisation. Save/restore the
   original camera and blend state on MR-off, scene transitions, XR-stop and
   reload; do not change another backend's key colour or settings.

The preferred backend arrangement is **native alpha when verified**, with
the existing **Virtual Desktop chromakey** retained as a separate backend.
An unsupported Frame mode should produce an actionable, bounded explanation,
not silently claim mixed reality while displaying green or black.

This requires no camera-frame access inside the game. The [OpenXR definition
of AlphaBlend][blend] assigns real-world composition to the runtime. Raw camera
copying, calibration and stereo reprojection become unnecessary if this path
works. Actual compositor behaviour remains a hardware acceptance check.

### 3. If the Windows bridge offers only Opaque

Record this as a blocker for the current game/runtime combination, even if a
native ARM64 sample supports AlphaBlend. First compare updated runtime/bridge
support with a minimal native local scene sample. A sample working on native
ARM64 does not fix a Windows game under Proton by itself.

OpenVR's `IVRTrackedCamera.HasCamera` is only an initial check for an alternate
camera-layer approach. Also require a successful actual frame read and camera
poses/calibration; the Frame developer report has `HasCamera=true` while
frame operations fail. Do not implement a heavy camera-copy/reprojection path
on that boolean alone, especially on the performance-limited standalone device.

If neither alpha composition nor readable camera frames reaches our game,
runtime/bridge support is needed. Keep normal VR available and avoid speculative
changes to the existing working rendering path.

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

The next useful hardware evidence is the one-shot blend-mode diagnostic in
the actual mod startup. If standalone reports AlphaBlend, the next bounded
implementation is the native transparent-background backend described above,
followed by an actual both-eye table-over-room test. If it reports only Opaque,
the Wine/runtime blocker is explicit instead of another ambiguous green-screen
test.

This round does not claim working headset passthrough, read-access to Frame
cameras, stock Steam Link chromakey or a measured MR performance gain. The
capability probe is an isolated diagnostic handover to the parallel Build652
integrator; the existing MR rendering behaviour remains unchanged.

[valve-frame]: https://partner.steamgames.com/doc/steamhardware/steamframe
[overlay-issue]: https://github.com/ValveSoftware/openvr/issues/1926
[rectus-compat]: https://github.com/Rectus/openxr-steamvr-passthrough/wiki/Compatibility
[enumerate]: https://registry.khronos.org/OpenXR/specs/1.1/man/html/xrEnumerateEnvironmentBlendModes.html
[blend]: https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrEnvironmentBlendMode.html
[unity-feature]: https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.10/api/UnityEngine.XR.OpenXR.Features.OpenXRFeature.html
