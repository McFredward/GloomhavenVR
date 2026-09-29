# Steam Frame standalone: Build 585 performance log

Evidence: `.planning/debug/steam_frame/LogOutput.log` and `Player.log`, both
ModBuild 585. The `openxr-diagnostics.log` is from the previous day and does
not add timing data for this run. No `perfrecording-*.csv` was supplied.

## What the log establishes

- Eye scale stayed at 1.00 throughout: the runtime requested 3408x3408 per
  eye, with MultiPass stereo and MSAA disabled. Unity quality was `Fastest`,
  shadows disabled and pixel lights zero. Graphics jobs were enabled.
- The settled main-menu window has a 13.88 ms frame median (1723 frames in
  30.0 s). Map windows after loading range from 45.13 to 68.04 ms median;
  a 30.1 s map window has 62.38 ms median, 123.40 ms p95 and 431 frames.
  This window includes changing quest/loadout/modal windows, so it is not a
  controlled stationary map benchmark.
- The final 30.0 s scenario window has 41.26 ms median, 93.96 ms p95 and 594
  frames. Main-thread logic averages 26.59 ms (16.85 ms median), cull/submit
  averages 11.35 ms (11.40 ms median), and the blocked remainder averages
  12.69 ms. The logic median alone exceeds the 13.89 ms budget for 72 Hz.
  Reducing GPU work cannot by itself reach that rate in this scene.
- In the 30.1 s map window, logic averages 49.96 ms (42.34 ms median),
  cull/submit 6.31 ms, and blocked time 13.69 ms. Named mod work is 41.11
  ms/frame on average. Major measured scopes are `TownServicePresentation`
  9.62 ms, `CanvasConversion` 9.14 ms and `CanvasConversion.Late` 5.56 ms
  per frame. Other scopes are nested; do not sum all printed scope values.
  The town population scope averages 2.51 ms and is part of the presentation
  path. The map changed windows during this interval, so this is a measured
  hotspot, not proof that any one unchanged NPC pose costs that much.
- The final scenario window records `Cards.Driver` 3.10 ms/frame,
  `WallFade.Late` 1.78 ms and `Hands.VRHand` 1.58 ms. The new hand subscopes
  show pose at 0.24 ms, UI click at 0.48 ms and near grip at 0.46 ms per
  frame on average, with intermittent long calls. Hand pose is not the main
  steady bottleneck in this run.
- The first map activation includes a 3425.57 ms frame, with 2778.48 ms
  measured in `TownServicePresentation`. Nearby records show resident/native
  artwork and many catalog card mirrors and mip bakes. Initial town/catalog
  construction is a likely contributor, but the exact child operation is not
  timed. Scenario entry likewise has one-off card and wall construction
  stalls; they must be separated from steady-state comparisons.
- Wall rescan remains at 4.00 s. Completed scenario rescan commits still
  produce roughly 56-67 ms single-frame work in the later windows, with
  `WallCache` around 24-26 ms in the worst commit. Increasing the interval
  reduces frequency, not the duration of a committing frame.
- The final scenario window allocates 92.9 MB in 30 s and reports one GC of
  each generation. The reported heap rises and drops after collections;
  this short run does not establish a persistent leak. `Player.log` has four
  DNS resolution errors from Hydra, but no managed or native crash here.

## Interpretation and next evidence

Build 585 is in the same approximate scenario frame-time range as the earlier
Build 584 windows, but those windows used different viewpoints and interactions;
this is not a controlled before/after speedup measurement. The XR GPU counter
tracks the frame interval in the slow windows, while Unity's FrameTimingManager
has no samples. Treat neither as GPU busy time. Runtime GPU/CPU timing would
help identify additional GPU pressure, reprojection and throttling. Valve's
documentation shows a separate `Record VR Performance` switch directly below
`Show Perf Overlay in VR`, but the maintainer's current Steam Frame UI shows
only the overlay switch. Do not require a `perfrecording-*.csv` for the next
hardware run or infer that Developer Mode unlocks the missing switch. Use the
available performance overlay instead: capture its `G` and `C` average/peak
readings, target rate and per-eye resolution during each test segment. The
reason for the UI difference remains unverified. If the recording control
becomes available later, Valve documents the CSV output under
`/home/steamos/.local/share/Steam/logs/`.

For the next comparison, keep eye scale 1.00 and capture at least 30 seconds
each of an idle map without modal windows, the same map with NPC interaction,
and a stationary scenario view. The next CPU investigations are the town
presentation and canvas conversion paths, then the atomic wall commit and
card-art arrival spikes. Preserve the existing 1:1 presentation and controls.
