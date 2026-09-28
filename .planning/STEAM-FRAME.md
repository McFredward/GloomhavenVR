# Steam Frame compatibility plan

Status: design and feasibility work, 2026-09-28. No Steam Frame hardware result has been claimed.

## Existing constraints

Gloomhaven Digital is a Windows x86-64 Unity 2021.3.5f1 Mono game. The mod
injects Unity OpenXR 1.10.0, uses D3D11 and MultiPass, and distributes Windows
native DLLs. The asset bundle must be built with the game's exact Unity editor.
These properties cannot be changed by updating the mod's separate asset project.

The controls tutorial already recognizes a `frame_controller` device name, but
renders the neutral controller. It currently recognizes Oculus Touch earlier in
the table, so SteamVR's compatibility mapping can obscure that recognition.
The existing eye-resolution control is uniform scaling, not foveated rendering.

## Execution modes

### PC streaming

Run the existing Windows game/mod on the PC through SteamVR's OpenXR runtime.
First record OpenXR runtime, HMD system name, actual controller interaction
profiles, eye/aim/grip poses, haptics, refresh rate, graphics API and frame
timings on the Frame. Verify every action in both hands, including menu/View,
D-pad, bumpers, trigger, grip, thumbsticks and the tutorial prompts. Do not
infer hardware identity solely from `InputDevice.name`: SteamVR can present
Frame controls as Oculus Touch.

Replace the generic tutorial controller with the runtime's actual render model.
Prefer `XR_EXT_render_model` plus `XR_EXT_interaction_render_model`; use
`IVRRenderModels` as the SteamVR-specific fallback. Adapt the loaded components
to the existing `ControllerKey` highlighting/anchor convention and preserve the
hand-to-controller transition. If model retrieval fails, keep the neutral
fallback and show a bounded diagnostic. Valve's Unity render-model sample
requires Unity 6, so it cannot be copied into this Unity 2021 mod unchanged.

### Standalone

Test the Windows build on the Frame through Proton/FEX first: Valve explicitly
lists that as the usual path for an existing Windows game. Check whether the
base game, BepInEx preloader, native OpenXR DLLs, DXVK/D3D11 presentation,
networking, asset bundle, audio and save paths all start and sustain VR at the
required frame rate. An Android/ARM64 APK would require the game's publisher to
provide that game build; a mod cannot turn the shipped Windows player into one.
Do not label standalone supported until the full campaign/tutorial and
multiplayer smoke tests pass on the actual headset. Record the per-eye CPU/GPU
timings and memory cost separately from PC streaming.

## Foveated rendering feasibility gate

Keep Valve's foveated *streaming* separate from game-side foveated *rendering*.
The former belongs to the streaming stack and must not be presented as a mod
rendering switch. For the latter, query actual OpenXR extension/graphics
support on both execution modes before exposing a player option.

Valve recommends `XR_FB_foveation`, `XR_FB_foveation_configuration`,
`XR_FB_foveation_vulkan`, `XR_FB_swapchain_update_state`, and
`XR_META_foveation_eye_tracked` for render foveation, with
`XR_EXT_eye_gaze_interaction` for gaze. Valve's ready-made Unity settings
require Unity 2022.3+, URP and Vulkan; this game has Unity 2021's built-in
pipeline and D3D11. A switch that only stores a Boolean would mislead players.

Prototype a version-matched native bridge in an isolated branch only if the
runtime exposes a compatible foveation path to this player. The bridge must
attach before swapchain creation, update the gaze-centred profile without
allocation on the render loop, and restore native rendering immediately on
disable or tracking loss. If the D3D11 swapchain cannot use the extension,
investigate a measured, visible-quality-preserving fallback or leave the
option unavailable; do not silently substitute uniform resolution scaling.

The requested setting is an explicit On/Off switch, localized in English and
German, displayed as available only where a real foveated render path is active.
On applies the tested profile; Off restores the same full-resolution frame as
the existing renderer. Log extension/capability decisions once and rate-limit
tracking-loss reports. Prove an actual spatial shading-rate difference, visual
quality at gaze movement and text edges, GPU-time benefit, and no interaction
latency regression. Test Steam Frame streamed and standalone independently.

## Primary sources

- Valve Steam Frame Unity guide: https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity
- Valve Steam Frame input: https://partner.steamgames.com/doc/steamhardware/steamframe/input
- Valve standalone compatibility: https://partner.steamgames.com/doc/steamhardware/steamframe/compatibility
- Valve custom engine/OpenXR features: https://partner.steamgames.com/doc/steamhardware/steamframe/engines/custom
- Valve Unity utilities (interaction profile and model sample): https://github.com/ValveSoftware/Unity/blob/main/com.valvesoftware.openxr.utils/Documentation~/index.md
