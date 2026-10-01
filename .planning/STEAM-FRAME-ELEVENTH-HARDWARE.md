# Steam Frame: large scenario and 3D map (ModBuild 597)

The maintainer loaded a scenario that revealed a large area immediately and reported that it was nearly unplayable. Evidence is the new `.planning/debug/steam_frame/LogOutput.log` from 2026-10-01, with the ModBuild 597 banner and commit `468835f6b`. The adjacent `Player.log` predates this run and is not used for attribution. No new screenshot was supplied for this run.

## 3D map before scenario entry

At 3408×3408 per eye, MultiPass, native `Fastest` quality, shadows and MSAA off, four successive 30-second windows remained at the runtime's 24 Hz presentation rate. Frame-time means were 51.37, 59.19, 61.63 and 53.48 ms; p95 values were 84.57, 110.50, 102.97 and 98.03 ms. Named mod steps averaged 26.54–34.16 ms per frame. The repeatedly measured map costs were `TownPublicStock.Catalog` (3.11–4.04 ms per frame), `TownServicePopulation` (2.37–2.62 ms), `Rig.MapRoom` (3.59–4.81 ms), `CanvasConversion.Late` (4.25–6.69 ms), and `WorldUI.HiddenWindowVeil` (2.13–2.65 ms). Their parent/child step names overlap; these figures must not be added as independent totals. The map is CPU-heavy even when the number of visible renderers is modest.

The XR `gpu` field follows the presentation interval; the log explicitly marks it unsuitable as GPU busy time. The `Perf SPLIT` lines attribute about 34–42 ms per frame to Update→LateUpdate logic, about 5–7 ms to render submission, and about 11–13 ms to waiting. Reducing eye resolution alone cannot remove the measured logic work.

## Large scenario

At the large room reveal, the wall-fade census classified 6,474 scene renderers and indexed 1,594 fade-capable renderers. Its one-frame table commits took 385.13 ms at frame 6290 and 281.78 ms at frame 6400. The `WallCache` phase alone took 110.97 and 113.50 ms; other expensive atomic phases included mounted props, free-standing props and prop units. Subsequent wall decisions commonly took 5–18 ms on their own. Raising the four-second rescan interval would reduce commit frequency but would not shorten either hitch and would delay new geometry; it is not the main fix.

Interactive frames also report `Compat.WaterTerrainVR` up to 33.47 ms and `Compat.LoaderHeal` up to 22.29 ms. The runtime changed presentation rate from 36 to 24 Hz, then to 12 Hz while the player was in the scenario. Many other 100–250 ms frames attributed only a minority of their time to named mod steps; these cannot honestly be assigned to the mod, the game, or GPU work more precisely with this trace. Scene startup frames above 1 s are not used as evidence of steady gameplay performance.

## Validation target

Keep 3408×3408 and the same native quality setting for the next comparison. After the loading indicator is gone, compare the map's 30-second `FRAME`/`STEPS` windows and the scenario's `WallSegmentFade BUDGET`, `Compat.WaterTerrainVR`, `Compat.LoaderHeal`, presentation-rate transitions and spike lines against this run. Verify wall/foliage fade, water, late-loaded materials, merchant stock and map icons visually as well: source-level and automated checks cannot certify the headset picture. The remaining game/runtime-side time may still prevent 72 Hz after the measured mod costs fall.

## Build 598 source changes and remaining risk

- Wall refresh indexes previous renderer and foliage membership rather than searching growing lists for every child. The original ordered restore lists remain. This attacks the `WallCache` subphase, but the measured `Mounted`, `FreeStanding`, prop-unit and other commit phases still run atomically when a genuinely new area appears. A new 282–385 ms commit cannot be ruled out from source review alone.
- Water discovery uses the installed content-placement hook and seeds only newly active tiles. The old tile walk remains as a fallback if the hook fails. A new film triggers one basin-wide rescan so water geometry in earlier tiles stays covered.
- The material watchdog still visits every registered loader; it now checks at most 256 entries per frame in the slow pass. Its fast lane remains on the original cadence.
- The 3D map queues previously unseen hidden icon textures for mip preparation. The merchant's covered next page still warms native data but no longer receives per-frame card/collider/widget ticks. Cabinet material and collider writes are conditional on state changes, and an NPC animation clip is stopped only on a clip transition. New `MapRoom.*` and `TownStation.*` scopes will separate the residual cost in the next Debug log.

These are CPU-work reductions, not a new Frame quality preset. They apply on PC and Frame; map and scenario visuals, original 2D map windows and multiplayer publication are intended to retain their previous state. A matched Frame run plus visual inspection is required before claiming an FPS improvement or parity.

Local validation on the integrated tree: the complete 81-suite run passed 80 suites; `town-service-setting` initially failed to compile because its isolated stub did not yet define the new `PerfMonitor` scope. After adding that test-only stub, the focused suite passed all 1,886 production assertions and 16 negative controls. The separate golden-vector runner passed 286,618 assertions, all 14 source gates passed, the bundle format and configuration/log/patch-surface checks passed, and the Release build had zero errors and warnings. No runtime Frame comparison has yet been made for Build 598.
