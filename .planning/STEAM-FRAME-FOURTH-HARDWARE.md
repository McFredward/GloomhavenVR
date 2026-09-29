# Steam Frame standalone: Build 585 overlay and extended run

Evidence: main checkout `.planning/debug/steam_frame/Player.log` (complete
17:28 copy), `LogOutput.log` (earlier 15:58 copy), and the two 17:27 overlay
screenshots `20260929172747_1.jpg` and `20260929172752_1.jpg`. Both logs
identify ModBuild 585, binary commit `2dbde2cad`, SteamVR/OpenXR 2.17.10.
The screenshots show the map with town residents and the VR options window.
They are two instants, not a continuous GPU trace.

## What the overlay adds

- Both pictures show approximately 17 FPS on the `G` and `C` readings, a
  `[24]` target game-loop rate, red frame history, and peak readings over
  100 ms. The submitted target is 3408x3408 per eye. Valve documents `G` and
  `C` as GPU and CPU frame timing, with average values displayed as FPS until
  the frame rate is high enough to switch to milliseconds; `[xx]` is the
  current target game-loop rate. These readings confirm a persistent slow
  map interval, not just the isolated load spikes.
- The nearly identical `G` and `C` readings do not independently isolate
  GPU busy work: both can follow the runtime's app pacing. Unity's own XR GPU
  statistic still tracks the frame interval, and FrameTimingManager still
  returns no samples. GPU-specific optimization remains a hypothesis, while
  the measured main-thread work below is independently sufficient to miss
  both the native 72 Hz budget and the `[24]` app target.
- The perf logger's `XRDisplaySubsystem.TryGetDisplayRefreshRate` value moves
  through 72, 24, 18 and 12 Hz in this run. Given Valve's overlay semantics,
  a 24 or 12 Hz log budget must not be presented as the physical display's
  refresh rate; it is an effective runtime/app pacing state. In particular,
  a low `over-budget` percentage against 12 Hz does not imply acceptable VR
  responsiveness.

## Paired log windows

| Segment | Frame mean / p50 / p95 | Main-thread logic | Cull + submit | Named mod work |
|---|---:|---:|---:|---:|
| Late map, 10 s, 162 frames | 62.33 / 53.78 / 104.50 ms | 41.54 ms/frame | 8.73 ms/frame | 30.92 ms/frame |
| Following map, 10 s, 165 frames | 60.82 / 51.88 / 102.44 ms | 40.46 ms/frame | 8.52 ms/frame | 30.39 ms/frame |
| Settled scenario, 30 s, 660 frames | 45.60 / 41.45 / 74.39 ms | 19.99 ms/frame | 12.44 ms/frame | 11.19 ms/frame |
| Later scenario, 30 s, 619 frames | 48.48 / 42.58 / 80.87 ms | 21.49 ms/frame | 12.17 ms/frame | 11.81 ms/frame |

The late map windows coincide with the screenshots and include an open VR
options window, town presentation and native panels. They are not an idle-map
benchmark. The `SPLIT` remainder is about 12 ms/frame in these windows; it
may include compositor pacing or GPU waits and is not itself GPU busy time.

The largest recurring named map scopes in the final windows are
`TownServicePresentation` about 7.1-7.2 ms/frame,
`CanvasConversion.Late` 5.6-5.8 ms/frame, `CanvasConversion` 2.9-3.2
ms/frame, and `WorldUI.HiddenWindowVeil` about 2.7-2.8 ms/frame. The
`TownServicePopulation` and `Rig.MapRoom` entries are nested in parent
scopes; adding every printed scope would double count work. Three individual
map stalls were dominated by `TownServicePresentation`: 2114 ms on frame
9058, 869 ms on frame 9993 and 1102 ms on frame 12007. Two batches of 74
`CatalogPrice` mirrors accompany separate catalog constructions; their count
does not prove a per-frame rebuild of the same cards.

The scenario still has significant non-town costs: wall-cache commits occupy
roughly 51-67 ms on individual frames at the four-second rescan cadence, and
the later settled scenario has 1100+ visible renderers in the sampled view.
Changing wall rescan frequency alone cannot shorten a committing frame.

The reported managed heap rose during map interaction and later fell after
collection (about 741 MB to 675 MB); this run does not establish a persistent
memory leak. The player log has recurring Hydra DNS errors but no observed
native crash or managed fatal exception. The earlier `LogOutput.log` copy
stops before the screenshot period, so the later comparison uses `Player.log`.

## Next work

Build 586 removes four verified duplicate operations in the measured paths:
wall-cache material reads, hidden-veil component lookups, intermediate catalog
visibility toggles and unchanged panel-capture property writes. These edits
preserve frame cadence and presentation but their headset benefit is unmeasured.
A proposed indexed `CardMesh` body registry is kept out of Build 586: 74 new
cards imply at most 2,701 self-comparisons in the existing list, while native
UI and artwork construction remain substantial. The registry change would
raise lifecycle risk without a demonstrated payoff here.

Prioritize stable map CPU cost, then bound catalog-construction stalls and
scenario wall-commit spikes without changing visible timing, content or
multiplayer parity. Keep eye scale at 1.00 because the maintainer reported
unacceptable image quality below it. A continuous Valve runtime performance
capture would still improve GPU attribution, but the headset currently does
not expose the documented recording toggle; the overlay is sufficient to
establish the slow map interval and validate future before/after tests.

Overlay interpretation: Valve's Steam Frame Debugging documentation,
https://partner.steamgames.com/doc/steamhardware/steamframe/debugging.
