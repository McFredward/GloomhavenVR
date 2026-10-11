# Steam Frame standalone: first hardware log review

Evidence: `.planning/debug/steam_frame/Player.log`, `LogOutput.log`, and
`openxr-diagnostics.log`, supplied 2026-09-29. This is a normal-level run of
GloomhavenVR 1.0.8 / ModBuild 556 (`c9831f4f4`) on game version
1.1.8323.0, not current dev Build 559.
The headset picture and sustained application CPU/GPU frame times were not
recorded. Do not treat this run as a performance benchmark or a crash root-cause
proof.

## What happened

- SteamVR/OpenXR 2.17.10 initializes successfully. The game uses Unity
  2021.3.5f1, D3D11, and a Turnip Adreno 750 adapter. D3D11 reports a 4064 MB
  adapter budget; this is not a measurement of physical dedicated memory on a
  unified-memory headset. The runtime recommends 3408 x 3408 per eye at scale
  1.0, sample count 1. The mod's shipped MSAA level is 4 and its shipped
  `ForceTextureStreamingOff` default is true; the user's saved cfg values are
  unknown from this log.
- The process reaches the main menu, map, and scenario. The only mod exception
  is one isolated `SelectionReadyHighlighter` null reference during scenario
  initialization (`Player.log:3439`). It does not recur before the crash and is
  not evidence that it caused the native process exit.
- Near the end, the native XR texture manager requests repeated image creation,
  then six destroy requests and more creation (`Player.log:3867-3924`). The file
  ends abruptly after `Player.log:3927`, without a managed crash stack, OOM
  report, or D3D device-removed reason. The user confirmed that the mod's
  resolution-per-eye value was **increased**. That action and this swapchain
  churn are temporally correlated. Unity's
  `XRSettings.eyeTextureResolutionScale` reallocates eye textures on change, and
  the mod writes it for each new live setting. A Proton/OpenXR/driver allocation
  failure is plausible but still unproven; retrieve the native coredump and
  SteamVR logs before assigning a cause.
- The main-menu VR Options entry is reported missing. In Build 556, `VRMenuEntry`
  spent at most twelve main-menu searches per *active* scene. The game's
  `MainMenu` and `MainMenu_gamepad` scenes are added only at `Player.log:1607-1608`,
  around 11:01:50, long after process start. An additively loaded main menu can
  appear after that search budget expires without changing the active scene.
  The pause-menu row later exists (`Player.log:3734`). Late native scene loading
  is a source-proven failure mode; the exact search exhaustion was not logged at
  normal level in Build 556. Build 559 re-arms the bounded search when either
  native main-menu scene loads and logs successful entry creation once.
- Separately, the game's Hydra endpoint cannot be resolved three times
  (`Player.log:202`, `:1748`, `:3837`). The log does not connect that network
  failure to the XR exit; it matters if online services later fail. Windows
  video playback also reports `DXGI DeviceManager: Not implemented` for two
  media resources (`Player.log:256`, `:1669`), which calls for a dedicated video
  playback check under Proton rather than a claim that video is broken.

## Performance evidence and priorities

- No sustained application CPU/GPU frame times or reprojection ratio are in the
  supplied files. `WallSegmentFade` alone reports roughly 1.5-3.7 ms per frame
  during settled scenario windows, with larger transition spikes. Its final
  66.7 ms/frame three-tick window is 186.6 ms *unattributed* out of 200.1 ms;
  do not blame its measured phases for that final stall.
- The old eye-reach diagnostic ran three large scene sweeps after an options
  tap, measured at 89.7, 55.5 and 42.1 ms (187.3 ms total) and roughly 48 KB
  of output. In this short ordinary-level run, `LogOutput.log` is about 620 KB:
  `WallSegmentFade` contributes about 294 KB and `WorldUI` about 164 KB.
  The eye-reach sweep is now Debug-only on dev; player logs still retain bounded
  build/flow/failure context. The remaining WallFade diagnostics merit a separate
  log-budget review; this change does not claim to optimize their runtime.
- Reducing stereo shading load is the first controlled GPU experiment: a 0.75
  linear scale implies 44% fewer eye pixels than 1.0, but is a quality tradeoff
  and does not prove a GPU bottleneck. Until the live resolution control is
  verified safe, choose a SteamVR per-application resolution *before* launch.
  Compare 1.0 and 0.75 with identical scene/camera conditions and SteamVR's
  performance recording. Do not change the game's flat resolution as a proxy.
- Compare MSAA 4 versus 2 and texture streaming on versus off independently.
  The shipped 4096 MB streaming budget is consulted only while
  `ForceTextureStreamingOff` is false; it was tuned for a desktop card and is
  not an appropriate automatic Frame profile. Preserve sharp card/UI textures
  through per-surface priority if a lower memory budget is needed.
- Foveation is advertised by the runtime, not enabled by this application. The
  current Unity 2021 built-in/D3D11 path does not meet Valve's Unity foveation
  package requirements (Unity 2022.3+, URP, Vulkan). Do not present a menu
  switch until a real rendering path has been implemented and verified.
- The eye census reports `stereoRenderingMode=MultiPass`. Unity therefore
  traverses/draws for both eyes separately. Single-pass instancing is a
  longer-term CPU optimization candidate, especially under x86-to-Arm
  translation, but Unity 2021 built-in does not support it with deferred
  rendering, and the game's and mod's shaders would need a full stereo audit.
  It is not a safe config flip for this mod.

## Next evidence to collect on the headset

1. Enable the mod's Debug log and Steam Frame **Record VR Performance**, then
   reproduce a short, fixed route in the main menu, map, and scenario without
   changing resolution. Keep the generated `perfrecording-*.csv` from
   `/home/steamos/.local/share/Steam/logs`; it contains submitted fps, peak CPU
   and GPU frame times, reprojection, refresh rate, power, and actual eye size.
2. If changing resolution still exits the process, collect the contemporaneous
   SteamVR logs, `perfcriteria.txt` if present, and `coredumpctl` metadata and
   backtrace. A normal Unity/BepInEx log cannot identify a native crash reason.
3. Record the number of upward resolution steps. The normal log does not emit
   the mod's detailed eye-scale diagnostics because those are gated at Debug.

Primary references:

- Valve, Steam Frame debugging and performance recording:
  https://partner.steamgames.com/doc/steamhardware/steamframe/debugging
- Valve, Unity integration and foveation requirements:
  https://partner.steamgames.com/doc/steamhardware/steamframe/engines/unity
- Unity 2021.3, viewport scaling without eye-texture reallocation:
  https://docs.unity3d.com/2021.3/Documentation/ScriptReference/XR.XRSettings-renderViewportScale.html
- Unity 2021.3, single-pass instancing constraints:
  https://docs.unity3d.com/2021.3/Documentation/Manual/SinglePassInstancing.html
