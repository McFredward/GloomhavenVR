# Build619 graphics settings audit

The maintainer requested four direct profiles and a distinction between ordinary
graphics choices, technical tuning and unconditional work removal on 2026-10-04.
This audit changes presentation controls, not gameplay or multiplayer authority.
Existing configuration keys are retained, including retired keys.

## Ordinary graphics controls

| Control | Decision and tradeoff |
|---|---|
| Four profile buttons | Standalone, Performance PC, Balanced PC, High-End PC; apply existing controls once and use the original native graphics callback. No saved profile index overrides later edits. |
| Eye resolution | Visible sharpness versus render cost; keep accessible. This multiplies the runtime's recommended resolution, rather than setting Steam's per-app resolution. |
| MSAA, anisotropic filtering | Familiar edge/texture quality choices; keep accessible. |
| Simple environment shading | Visible surface-lighting trade; keep accessible. It excludes actual masonry/water shaders, so disabling it alone is not evidence that the reported wall fade was caused by a substitute shader. |
| Grass, decoration, vegetation | Independent visible content trades, including audited foliage families and trees; keep accessible. |
| Player/enemy mesh detail, distance LOD | Visible geometry trades with original gameplay retained; keep accessible. Unknown/unproven models retain their original bodies. |
| Figure/environment effects | Visible particle/trail/additional-glow trades; keep accessible. Combat, condition and gameplay-dependent particle callbacks remain intact. |
| Figure cloth | Secondary simulation versus static cloth; keep accessible. |
| Reduced procedural detail | Visible native generation trade at the next scenario load; keep accessible and explain that timing. |
| Window supersampling, legibility, materialization | Window readability/animation choices; keep accessible. |
| Monitor mirror | Left-eye mirror versus a black desktop output; keep accessible. Private obsolete flat HUD rendering remains suppressed regardless. |

Environment selection and immersive NPC enablement remain in Environment;
healthbar occlusion/scale, wall visibility and their user-facing behavior remain
with their existing comfort/presentation controls. They are meaningful gameplay
readability choices, rather than housekeeping optimizations.

## Advanced controls

| Family | Reason |
|---|---|
| Full texture resolution, streaming disable/budget | VRAM/residency and texture quality trade; technical memory tuning. |
| Pixel-light limit, LOD bias, skinning bone limit | Engine-level quality/cost trade; retain with descriptions in Advanced. |
| Structural instancing/batching, static floor batching | Platform-dependent submission/memory/compatibility trade; retain in Advanced. |
| Offscreen idle evaluation | Changes transform evaluation for audited offscreen idle rigs; actions/held figures remain live. Retain in Advanced. |
| UI maintenance, healthbar pose and initiative-depth intervals | Temporal sampling/cost trade with immediate critical edges; Advanced. |
| Wall discovery/evaluation/sweep intervals, fan gaze-layout interval | Technical cadence controls; Advanced. Native fades and current hand/card motion continue each frame. |
| Card/panel mip baking and their memory limits | Aliasing versus GPU work/residency trade; Advanced. |
| Materialization duration, density and secondary parameters | Effect tuning; keep the simple on/off choice outside Advanced. |
| Fog/water/light calibration | Detailed presentation tuning; Advanced. |
| Performance instrumentation, recording and diagnostics | Diagnostics category and Advanced; avoid turning frequent traces into ordinary user logging. |

The category fallback remains for module-owned technical entries. Unsafe legacy
head masks/render routing and retired experiments stay unavailable in the UI.

## Unconditional optimizations and safety fixes

The following saved keys are now INERT and hidden. Switching them off offered
extra work or incorrect VR output without an intended visual/gameplay benefit:

| Retained key | Always-active behavior |
|---|---|
| Optimize/CacheTickDelegates | Reuse unchanged tick delegates. |
| Optimize/MapIconCache | Cache discovery; read actual map poses live. |
| Optimize/FigureScanCache | Reuse adopted figure ownership and avoid empty-hand scans. |
| Optimize/LeanLogStrings | Check log gates before constructing discarded messages. |
| Optimize/TooltipScanGate | Avoid empty tooltip work. |
| Optimize/SharedWallReadCache | Reuse unchanged reads within synchronous wall preparation. |
| Optimize/LightStabiliserWorkCache | Skip unused diagnostics and exact unchanged setters. |
| Optimize/AutomaticLodIdleSkip | Suspend only native callbacks whose own UnityLODGroup branch immediately returns; actual Unity LOD selection stays intact. |
| Optimize/SuspendUnusedCameras | Suspend unneeded native rendering while retaining projection, picking, required captures and visible native menus. |
| Compat/DisablePostProcessing | Suppress the existing audited stereo-incompatible flat postprocessing types. |
| Compat/DisableVolumetricFog | Suppress the existing stereo-incompatible fog component. |

Descriptions start with the same INERT retirement marker used by ConfigCatalog.
Legacy values remain readable on disk but cannot undo these fixes. Experimental
keys already retired before this build retain their existing status.

## Profile application

Standalone uses the current fresh-Frame constants, including native Fastest,
zero optional scenery/figure effects, minimum figure geometry, disabled cloth,
reduced generation, existing batching, lower maintenance cadence, disabled NPCs,
disabled window materialization and disabled desktop mirror. PC profiles use
native Simple/Good/Fantastic with progressively higher scene/mesh density and
MSAA 2/4/8. Texture clarity is retained; the maintainer's pixel-light limit remains
zero in all profiles. No profile silently changes these choices at startup.

The original GraphicSettings.SetQualityLevel input callback owns native quality,
its UI refresh and save. An unavailable/uninitialized native owner refuses the
action before any VR changes. Configuration autosave is temporarily suppressed
for the batch; prior flags are restored before each module is saved. Individual
save failures are reported without leaving other modules' autosave disabled.

The production profile action has a portable boundary harness; the actual
headset menu, native callback availability and visual tradeoffs still need
hardware verification. The integrated gate also checks curated/Advanced coverage
and preserved configuration surface.
