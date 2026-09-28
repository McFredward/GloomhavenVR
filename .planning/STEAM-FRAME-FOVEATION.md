# Steam Frame game-side foveated rendering feasibility

Status: source and specification review, 2026-09-28, based on `dev` at
`d9bc7b4a` / ModBuild 557. No Steam Frame runtime or headset measurement was
available. Decision for this build: **do not add a foveated-rendering On/Off
setting**. There is no demonstrated render path for that setting to control.

## Two distinct features

Steam Frame's **foveated streaming** directs higher quality *encoded/transported*
pixels toward the viewer's gaze after the PC game has rendered a frame. Valve
says it works across the VR library. Its control and benefit belong to SteamVR's
streaming stack, so the mod must neither claim nor expose it as a game rendering
feature. [Valve Steam Frame product page](https://store.steampowered.com/hardware/steamframe).

**Foveated rendering** varies work within the game's eye render targets. That
requires support through the render API, OpenXR swapchain and Unity rendering
path. SteamVR advertises the relevant `XR_FB_foveation` family and
`XR_META_foveation_eye_tracked`; extension advertisement alone does not show
that this game's swapchain can use them. `XR_EXT_eye_gaze_interaction` supplies
gaze information, not a shading-rate change.
[Valve custom-engine guide](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom).

## Current player and mod path

| Constraint | Repository evidence | Consequence |
| --- | --- | --- |
| Unity 2021.3.5f1 Windows x86-64 Mono game | `CLAUDE.md`; `OpenXRBootstrap.LogEnvironment()` logs `Application.unityVersion` | Rebuilding the separate asset bundle cannot upgrade the game's Unity player. |
| D3D11 desktop OpenXR | `Core/Startup/OpenXRBootstrap.cs` warns on other graphics APIs and documents observed native session failure; `-force-d3d11` is the recovery path | Switching to Vulkan or D3D12 is not a working mod setting. |
| Built-in rendering pipeline and MultiPass | `Core/StereoModeConfig.cs` records the shipped shader and flat-screen constraints; `OpenXRBootstrap.CreateSettings()` fixes MultiPass | A change to URP or single-pass cannot be applied to the existing player and assets as a local quality option. |
| Unity OpenXR 1.10.0 injected at runtime | `Core/Startup/RuntimeDepsLoader.cs`, `OpenXRBootstrap.CreateSettings()` | The mod creates interaction-profile features before initialization but does not own the native display/swapchain implementation. |
| Uniform eye-resolution control | `Rig/RenderQuality.cs` writes `XRSettings.eyeTextureResolutionScale` and, if needed, `XRSettings.renderViewportScale` | Existing resolution scaling changes the whole eye image; it is not foveated rendering. |

Valve's Unity foveation utility requires **Unity 2022.3+, URP and Vulkan** even
though its OpenXR package minimum is 1.9.1. Unity's current foveation manual
also requires Unity 2022.3 or newer, URP, and D3D12 or Vulkan for PC XR in its
standard path. It notes that some XR provider plug-ins offer a separate
Built-in Pipeline implementation in Unity 2022.3; that exception still does
not cover this Unity 2021 player. The OpenXR plugin version number alone
therefore does not make this game eligible.
[Valve Unity guide](https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity),
[Unity foveated-rendering manual](https://docs.unity3d.com/6000.2/Documentation/Manual/xr-foveated-rendering.html).

The OpenXR `XrSwapchainCreateInfoFoveationFB` structure must be chained to
`xrCreateSwapchain` for a foveation-capable swapchain. The Khronos-defined
foveation creation flags require **OpenGL plus `QCOM_texture_foveated`** or
**Vulkan plus `VK_EXT_fragment_density_map`** respectively; neither describes
the current D3D11 binding. `xrUpdateSwapchainFB` only updates the state of a
foveation-capable swapchain; it does not retrofit creation support onto Unity's
existing swapchains. A managed Boolean, or merely enabling OpenXR extension
names, would therefore be a placebo on the known player path.
[Khronos swapchain creation](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSwapchainCreateInfoFoveationFB.html),
[Khronos creation flags](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSwapchainCreateFoveationFlagBitsFB.html),
[Khronos swapchain update](https://registry.khronos.org/OpenXR/specs/1.1/man/html/XrSwapchainStateFoveationFB.html).

This is a **supported-path finding**, not proof that no custom native solution
could ever be built. Such a solution would need to replace or interpose on
Unity's graphics/OpenXR swapchain creation and render submission, establish a
compatible graphics binding, and prove the game's built-in shaders/UI remain
correct. It is outside a mod-only managed setting and cannot be validated by
extension enumeration alone. Proton/FEX translation of a Windows D3D11 game
to device Vulkan does not by itself change the OpenXR graphics binding seen by
the Windows Unity player. That standalone path remains a hardware question.

## Reopen criteria and hardware test

1. On Steam Frame PC streaming and standalone Proton/FEX separately, record the
   game build banner, Unity version, graphics API, runtime/system name, OpenXR
   enabled extension list, swapchain graphics binding, and per-eye GPU timing.
   Verify that the game actually starts in VR in each mode first.
2. Reopen an implementation only after a working path can create foveation-capable
   eye swapchains and alter *game-side* spatial shading/raster density. A future
   version-matched native bridge would have to attach before swapchain creation,
   handle restart and tracking loss, and restore the original full-quality state
   immediately on Off. A capability query alone is insufficient.
3. For any prototype, capture a spatial shading-density or fragment-density
   diagnostic with On and Off at the same eye resolution, then compare readable
   text/card edges during gaze motion and loss of eye tracking. Measure per-eye
   GPU time and interaction latency in matched scenes. Only after these checks
   should an EN/DE On/Off row be offered on capable runtime/graphics paths.

Until then, Steam Frame PC streaming may still benefit from SteamVR foveated
streaming automatically, while the mod's existing eye-resolution row remains an
honest, uniform rendering control.
