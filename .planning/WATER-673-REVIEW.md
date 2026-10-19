# Build673: asynchronous water replacement throughout the installed game

Base: `dev` at `d6306e146c4d94c431ddadb6b4d1a8d59a177913`, Build672.
Final implementation and strict builds: `9a112b0fcac7e242ce8d0d0676038accbb5b5471`.
The maintainer reported native difficult-terrain water after scenario loading and
requested the replacement throughout the game. This repair includes terrain,
decorative ponds and DLC sluice films. It does not add an appearance setting.

## Evidence and cause

Both frozen local logs identify **Build671 / assembly1.1.0.0**, while the initial
source checkout was Build672. Their original inputs remain unchanged. The own
WaterVR shader resolves, and WallSegmentFade observes seven low terrain-water
surfaces; no genuine water-adoption report follows. This supports a discovery
failure, rather than proving a missing bundled shader.

The read-only native audit checks all380 renderer-bearing sources,57,915
renderers and7,527 MaterialLoader components. All45 water bindings serialize
empty renderer material arrays and publish their exact original materials later.
All36 terrain renderers already have the old matching `TERRAIN_Water` name.
Build615 (`1bc0a26c7`) replaced steady deep tile scans with a one-time placement
seed without publishing asynchronous material readiness to the water driver.
An empty first seed consequently never adopts the later native water material.
See [WATER-673-ASSET-AUDIT.md](WATER-673-ASSET-AUDIT.md) for all14 materials,
four native water shaders, exact GUID provenance and excluded non-water effects.

## Final behavior and ownership

The existing ScenarioEnvironmentBudget native-ready callback queues the exact
renderer for WaterTerrainVR before the unchanged generic world-material owner.
The bounded256-member queue drains before normal submission; an overflow drains
its exact members synchronously. Inactive preview rooms, late rooms and later
quality publications receive the same treatment. Ordinary placement discovery
remains bounded; the repair does not restore recurring deep scene scans.

All native procedural water-film slots receive private instances of the existing
WaterVR shader, retaining source tint and normal inputs. Mixed non-water slots
stay shared. Full slot/reference and owned-shader comparisons detect native
rewrites; restoration replaces only slots still carrying our exact instances.
Late shader availability re-applies retained originals without dropping pending
native-ready events. Basin/probe footprint tuning remains terrain-only. Exact
live ownership preserves the existing water-feature wall protection even with
the generic Overlay fallback, without treating every Overlay as water.

The replacement uses straight alpha and does not write depth. Native opaque film
queues<=2500 therefore become3000 on the private instance; already-transparent
terrain2900 and DLC3000 stay authored. Original materials and restoration queues
remain unchanged. Actual Camera.Render shows why: with opaque ground2450,
the old owned queue2000 erases the physically frontmost film to a pure red ground
pixel. The same film above ground2000 already worked. The new17-assertion pixel
case covers both ground queues and authored water2000/2900/3000; the compiled
old-queue control reproduces the defect. The GPU is llvmpipe, not a headset GPU.

## Validation scope

| Check | Evidence |
| --- | --- |
| Final water production |488 assertions; complete production WaterTerrainVR and shipped shader/include; all45 paths and14 exact material variants |
| Native completion boundary | Exact decompiled MaterialLoaderData method, real HarmonyX interception, actual ready postfix/publication and source-extracted current CoreModule delegate |
| Causal controls | Eight earlier compiled lifetime/ownership defects retained; final focused queue follow-up reruns production,17 pixel assertions and the new old-queue defect |
| Wall classifier | Existing full wall-read-facts suite:19,511 assertions/all29 controls, including newly lost exact-owned-water protection |
| Native figure boundary |731 assertions and the affected actor-water control; actual native SpittingDrake, animation/depth/alpha pixels |
| Source checks | Initial source16 pass; final affected patch-inventory, instrument-writes and hw-verify pass again; unaffected contracts reuse their recorded evidence |
| Strict builds | Final main-checkout Debug and Release:0 errors/0 warnings |
| Wire golden vectors | Earlier Build673 direct300,518 pass; serialization and governed water constants unchanged by the queue/fixture follow-up |
| Public surfaces and assets |666 config keys/236 Harmony entries/4,795 log tokens unchanged; bilingual docs and all bundled formats/banks pass |

The existing wall/figure fixtures needed explicit water-owner test boundaries
because they compile the actual updated classifier. The portable wall fixture
checks native water, real ownership input, retirement and generic-shader refusal;
its new compiled control must lose the exact protection input. The native figure
fixture has no water owner and retains that explicit false external boundary.
The separate complete water fixture proves the real ownership lifetime.

This is **focused/composed validation, not a new complete local runtime gate**.
The final water/CoreModule/native-ready/catalog input hashes match the retained
worker run exactly. Original game-controller execution, real Addressables
callbacks, actual loaded scenario rendering, headset appearance, FPS and paired
multiplayer outcomes remain outside these fixtures. Dev push invokes full hosted
CI; this report does not claim its future result.

## Compiled scope and retained evidence

An isolated clean Build672 snapshot remains separate from the old shared Build601
guard baseline. Final decompilation compares **1257 ->1257 types**, with no added
or removed type files. All twelve changed existing files are explained: three
implementation files (WaterTerrainVR, CoreModule callback and WallSegmentFade
ownership input), eight consumers of the inlined673 handshake constant, and the
private-baseline branch metadata. No gameplay, network payload, input/default,
camera/light, shader asset, mesh or MR-backing changes. Version3/next-free118.
The existing Build672 bundle is reused; the repair changes the plugin DLL.

Private receipts live under `.planning/debug/water673/`: frozen hardware inputs,
initial source/goldens, final builds, compiled snapshots/diff, source/artifact
bindings and hash-verified `water-runtime/` plus `integration/` archives. The
read-only asset receipts remain under `.planning/debug/water673-assets/`.
Failed earlier shader/include and premature queue expectations remain visible;
only actual passing final controls are counted above.

After processes finish and are reaped, the shared cleanup API removes generated
Unity/managed caches. Direct golden and wall-project caches are moved into their
explicit owned run before that API removes them; receipts retain original paths.
Final main build obj caches are removed while preserving current Debug/Release
plugin/preloader deliverables, logs, sources, captures, hardware and .keep pins.

Hardware follow-up should use Debug on673: load the reported scenario afresh,
reveal another room, reload/switch scenarios and check native quality changes.
Inspect the actual OWN SURFACE adoption/counts and pond/DLC films while moving
the head; confirm original tint/ripples, no head-bound mirror and no fountain
fading. Paired673 VR peers should retain the same water content; a flat player
keeps native materials. No such new hardware outcome is asserted here.
