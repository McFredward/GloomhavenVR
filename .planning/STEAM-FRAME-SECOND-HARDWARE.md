# Steam Frame standalone: second hardware performance review

Evidence: `.planning/debug/steam_frame/LogOutput.log`, ModBuild 584, supplied
2026-09-29. The accompanying `Player.log` predates this run. No Valve
`perfrecording-*.csv` was supplied, so the runtime's XR GPU figure cannot be
interpreted as GPU busy time; it tracks the frame interval, and Unity's
`FrameTimingManager` reports no samples.

## Controlled observations and limits

- The player tried eye-resolution scales 0.70, 0.75, 0.80, 0.95 and 1.00 with
  MSAA disabled. Submitted eye targets were 2386, 2728 and 3408 square pixels
  at 0.70, 0.80 and 1.00 respectively. The 0.75 and 0.95 periods are too short
  for stable 20-30-second scenario windows.
- In eight settled scenario windows at 0.80, frame p50 ranged from 41.32 to
  45.81 ms. The first three settled scenario windows at 1.00 read 45.01, 44.59
  and 42.02 ms. Camera pose and visible scene content varied; this is evidence
  against a large resolution benefit in this run, not a controlled GPU A/B.
  The 0.70 scenario windows saw fewer renderers or loading transitions and are
  not comparable to the settled 0.80/1.00 windows.
- At 0.80, main-thread logic averaged about 22-26 ms and cull/submit about
  11-14 ms in the settled scenario windows. The unassigned blocked remainder
  was about 11-14 ms and may contain GPU work, compositor pacing or both.
  Resolution does not reduce the measured logic or submission costs. The
  maintainer reports visible quality loss below scale 1.00, so the next
  hardware candidate should keep 1.00 rather than pursuing a smaller target.
- `[WallFade] RescanIntervalSeconds` reads 4.00 s. The decision cadence remains
  0.050 s. The longer rebuild period reduced how often rescan work is scheduled,
  but committing cycles still produced 58-68 ms single-frame spikes in the
  scenario. Several later windows skipped the commit and cost about 4-6 ms at
  worst across all rescan stages. This dial changes frequency, not atomic
  commit duration.
- The recurring named mod work in settled scenario windows includes
  `Hands.VRHand` about 1.5-3.2 ms, `CanvasConversion.Late` about 1.2-2.7 ms,
  `CanvasConversion` about 1.0-1.8 ms and `WallFade.Late` about 1.1-1.7 ms
  per frame. The post-scenario map adds `TownServicePresentation` about
  6.5-7.0 ms, `TownServicePopulation` about 3.1 ms and
  `TownServicePresentation.Late` about 2.5 ms per frame. Scope nesting must be
  checked before adding any of these figures together.

## Build 585 changes and next hardware check

Build 585 removes redundant work from canvas conversion, wall-cache enumeration
and town-service presentation. It also splits the hand timing into pose and
individual interactor scopes without changing their execution order. The
source and runtime checks passed, but these changes have not been measured on
the headset.

Keep the eye scale at 1.00 for picture quality; do not use resolution as the
proposed fix for the measured CPU cost. Capture Valve's Record VR Performance
CSV alongside the mod Debug log over a repeatable route. Its `peakCpu(ms)`,
`peakGpu(ms)`, `reprojected`, `width` and `height` fields can separate GPU
pressure from CPU/submission pressure. Compare the hand subscopes and the same
scenario and town views against Build 584. A headset comparison is still needed;
local tests cannot establish perceived smoothness.
