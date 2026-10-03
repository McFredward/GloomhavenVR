# Build 615: bounded diagnostic census and pickup attribution

## Evidence and change

The loaded Build612 capture identified synchronous Debug census samples of
91.6–119.9ms. Rationing their frequency left each sampled frame exposed to the
whole cost. This change removes the component-wide `FindObjectsOfType` calls
from the diagnostic SCENE/SIM/GFX path. Loaded scene roots, including the
persistent mod-host scene, are visited incrementally. The SPLIT renderer/uGUI
summary now consumes the same completed inventory; when `SceneProfile=false` but
`SceneCensus=true`, a lighter sliced renderer/Graphic job still seeds the original
ZOOM sampler. Its captured visibility flags and numeric totals include explicit
span/age instead of claiming a one-instant snapshot. Scene/Debug cancellation
drops both pending jobs and complete old ZOOM references. Hierarchy children,
components, renderer/material records, behaviour types, animators, particles,
lights and texture-population reads share one pump.

The original definitions remain: inactive objects are excluded from population
counts; disabled components on active objects remain counted; forced-off
renderers are excluded from visible/submission candidates. An animator's original
`GetComponentInChildren<Renderer>(true)` question includes inactive children.
Its equivalent ancestor inventory avoids another subtree walk per animator.
No renderer, native behaviour, object activation, game state or wire state is
written by this instrument.

`[Perf] SceneProfileBudgetMilliseconds` and
`SceneProfileObjectsPerFrame` bound each slice by elapsed CPU time and work units.
The fallback values are 0.5ms and 64 units; accepted ranges are 0.1–4ms and
8–512 units. A work unit is a hierarchy/component/tally step, rather than an
entire subtree. The lower of the two budgets ends the current slice. This is a
soft time budget: a Unity call, one object's components, bounded ancestor walk
or final line formatting cannot be preempted. Actual maximum slice and atomic
unit times are printed; the pump is also priced in `Perf.SceneProfileSlice`.

A full census can span multiple summary windows on a large scene. Later windows
say that it is in progress instead of scheduling duplicate work. The reported
CPU sum excludes waiting between frames. The output includes slice count and
observation span, so it must not be interpreted as either one frame's cost or a
simultaneous population snapshot. The old cooldown still limits aggregate
instrument work. Existing FRAME/STEPS/SPLIT/SIM/SCENE/TEX/GFX tokens remain.

Loading, loaded-scene/procgen changes, additive scene membership changes,
`SceneProfile=false`, Debug being disabled, a graphics measurement boundary and
shutdown cancel unfinished samples and release their native object/material/
texture references. GFX/texture output is published only after a complete sample.
The pump cannot fault the rest of the performance monitor or native continuation.

Unity's list overloads are used for [scene roots](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/SceneManagement.Scene.GetRootGameObjects.html)
and [one object's components](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/Component.GetComponents.html).
Their individual native calls are measured atomic work, not claimed preemptible
operations. Lists and reflection results are reused after warmup.

## Hand stages

The existing hand/near-grip spans now contain separate named measurements:

- `Hands.NearGrip.Election`: unchanged registry election and hysteresis.
- `Hands.NearGrip.Distance`: native `ClosestPoint` or the original reach volume.
- `Hands.NearGrip.Eligibility`: original `CanGrab` and per-hand callbacks.
- `Hands.NearGrip.HoverCallbacks`: highlight hooks and subscribers.
- `Hands.NearGrip.Pickup` / `Release`: original pickup/release callbacks.
- `Hands.NearGrip.GhostConstruction`: original frozen-visual snapshot construction.

The monitor's original nesting excludes double counting from the mod total.
Every contact, callback, input edge and elected winner retains its original
ordering and frequency. Attribution off keeps the monitor's existing no-timer
branch. These stages let a later hardware spike distinguish callback creation
from distance checks; they do not establish that either caused the earlier
177ms hand spike. Unbracketed native work remains unresolved, not labelled GPU
workload by subtraction.

## Runtime proof and limits

`python3 scripts/check-perf-census-runtime.py` compiles the complete production
scene/texture census sources and original monitor/grab methods, records SHA256
provenance, and runs Unity 2021.3.5. The controlled scene has 1202 native renderers,
active-disabled/forced-off slots, an inactive animator child renderer, a native
particle system/light/LOD group, and a persistent scene renderer. Original Unity
population counts agree. Real native colliders and 320 distinct native textures/
materials exercise the reach and texture paths. External game loading context and
hand input are explicit seams; there is no headset image in this fixture.

The production fixture passes **3212 runtime assertions**. Eleven effective causal
controls reject an unbounded pump, unsliced textures, a missing persistent scene,
inactive population leakage, ignored Debug cancellation, a missing lightweight
roster or stale retained ZOOM roster, missing hover attribution
and duplicate eligibility callbacks. Two additional controls exercise an actual
summary fault: unfinished native inventories and completed ZOOM references must
be cancelled, and later SPLIT summaries must not requeue a stopped pump. Source order/mirrors/partial order, the
existing census lifecycle/visibility gate and strict Release are also checked.
The instrument-write source check initially identified a diagnostic fault latch
read directly in the host update; its pump is now explicitly diagnostic and the
bounded check is resumed without repeating unrelated suites.

Controlled desktop sample timing is not a headset saving. Build615 must be
measured on Frame before attributing smoother play or a percentage improvement.
Real scene mutations during the observation span can change individual samples;
scene/load boundaries discard partial data rather than publishing stale completeness.
