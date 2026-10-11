# Frame wall transient-owner closure

Build617 hardware identifies five native waypoint particle births/deaths as the
sole scene-signature changes preceding 209.93/165.86 ms wall rescans. Other
ordinary changes include detached shield, heal and retaliation FX. Rebuilding the
wall table does not advance those native presentations; it repeats wall geometry
collection because their identities enter the unrelated scene signature.

## Original-owner exclusions

`WallSegmentFade.SelectionFacts.cs` now admits only exact published native visual
ownership alongside the existing hex-selection boundary:

- A particle must descend from a `WaypointHolder.m_Prefabs` member's actual
  `Prefab` transform. Merely sharing the holder ancestor or the names `Sparks`
  and `Rings` grants nothing.
- A detached action particle must descend from an original `ObjectPool` instance
  mapped to an exact published combat prefab. The read-only `spawnedObjects`
  and `pooledObjects` dictionaries retain ownership during native activation,
  parking and recycling. The prefab references come from the original
  `GlobalSettings` heal/condition/reveal/swap, active-bonus and retaliation/wound
  fields. An active mapping takes precedence over an old parked entry.
- Original water-name/shader protection and any actual wall-fade shader veto the
  new native presentation exclusion. Unknown projectile prefabs, summon meshes,
  environmental flames and other scenery remain conservative table inputs.

The classifier and every live `IsModObject` consumer share this predicate.
Particle ownership is re-evaluated on warm classification slices; permanent mod
clone ownership retains its previous immutable half. Native action roots and
owner answers live for one synchronous ancestry window and release every borrowed
reference when that window closes. There are no native callbacks, gameplay
writes, particle suppression, object-name exemption heuristics, scene searches or
scene-lifetime claims. The new path applies on every VR platform and changes no
wire format or remote presentation.

The relevant non-mesh collector lanes are Mounted and FreeStanding riders, which
reject `RendererFact.Mod`; native water remains under its own protected input
list. Direct hierarchy collectors use the same live predicate. A previously
carried particle acquiring a published owner is restored before Mounted sticky
carry. Mesh-only wall-adoption lanes cannot admit these particles, and protected
wall shaders keep their original signature term. Native figure/actor and light
rulings remain unchanged.

This closes the measured path-marker dependency and the enumerated original FX
families. It does not prove every detached effect is irrelevant to wall output.
The 30-skip safety ceiling, room-reveal, drift, board-movement and material gates
remain unchanged. No FPS gain or disappearance of all heavy rescans is claimed
without a new hardware run.

## Delivered draw diagnostic endpoints

The existing Debug-only actual head-camera pre-render sampler now reserves one
of its twelve samples for terminal restoration, checks completion before fade
bucket deduplication, and observes partial reversals from actual write direction.
Fade `.2494` and restored `0` sharing a bucket no longer drops the MPB-clear
endpoint or spends the remaining cap on another outward cycle.

Two renderer episodes per actual route prevent the first six HIGH walls from
consuming all LOW/toggle-family slots. The existing six-segment and three-route-
renderer-per-segment limits remain: a conservative upper bound is 18 renderer
episodes / 216 Debug samples, not six total renderer episodes. Combined routes
are distinct strings. Actual material, per-slot MPB, gate, enabled and render-mask reads
remain read-only. No recurring normal-level diagnostic is added. Imported HIGH/
LOW metadata routes in tests are GL surrogates, not the native HIGH fragment
program or a proof that headset wall popping is fixed.

## Focused verification

The production wall classifier/live ownership harness passes **19,504 assertions**.
Its 21 existing negative controls and seven added waypoint/prefab/parking/water/
wall/warm-reparent controls reach their intended failures. Fixture-only binding
corrections resumed only the affected controls; successful unrelated controls
were not repeated. Original native pool field names and collection shapes are
explicit reflection boundaries, not alternate production ownership code.

The actual Unity2021.3.5 GL environment/draw fixture passes **366 runtime
assertions**, including final restored MPB delivery, repeated partial reversals,
reserved endpoint capacity, three distinct imported shader routes and the global
scene cap. Four affected cap/terminal/route negative controls fail causally.
Compilation failures were rejected as evidence. An initial attempt to rename
`Shader.name` did not alter its compiled route and was replaced by genuinely
separate imported fixture shaders.

The unchanged ceiling summary helper passes seven assertions and four negative
controls, binding all 25 real atomic phases and the retained safety gate. The
source build passes with zero errors and zero warnings. These are worker-focused
receipts; the integrator still runs the complete final-tree gate and golden wire
vectors. Hardware pacing and native shader pixels remain open.
