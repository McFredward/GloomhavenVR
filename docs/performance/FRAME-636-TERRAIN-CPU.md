# Frame636 terrain CPU follow-up

The supplied Frame636 capture (`de12a1e8f`) spends a frame-weighted 13.272ms
in terrain pre-cull and 1.667ms in terrain Update after all three rooms have
settled. These are callback costs, not GPU busy time. The changes here remove
repeated Unity accesses without adding a visual compromise or changing an
existing graphics option. The material-owner/shader repair is a separate lane.

## Current poses within one Update

The detail-selection loop captures the current head position/scale, both tracked
hand positions/scales, and the near/distant settings once. Each source still reads
its current native bounds and validates its current original mesh. The snapshot
expires when that synchronous Update returns; the next Update reads movement,
tracking loss, world scale and changed settings again. Hand/lean proximity and
the existing animated geometry transition remain unchanged.

## Current property blocks within each eye

Each proxy preparation checks the source's actual `Renderer.HasPropertyBlock()`.
An absent native block requires no renderer/slot block copies or effect-property
reads. Identical private default blocks are also left untouched. Native block
presence is never cached between cameras: a new native color, texture or effect
block is read during the next camera callback. The first empty callback after
native blocks disappear clears the private renderer and indexed overrides.
Empty indexed blocks skip effect reads; nonempty blocks preserve native contents
and slot precedence. Material changes clear previous private indexed overrides
before changing the slot array.

Proxy blocks set both `_GHVRTerrainNeverFade` and `_GHVRWorldNeverFade` together.
Floors use one and walls use zero. Actual floor admission remains excluded, so
this pins the shared helper's compatibility contract rather than introducing
private floor geometry. Native meshes, material arrays, colliders, controllers,
visibility and camera-budget fallback remain untouched.

## Validation and limits

The focused real-Unity fixture instruments the actual production transform/MPB
accesses while executing the original Unity operation. With 96 prepared sources,
one Update reads the head position and scale once and each tracked position once.
A settled empty-block camera checks 96 fresh presence guards and performs zero
block reads, effect reads or private block writes. Current tracking loss/scale,
late color/texture blocks, removal of renderer/slot blocks, live native effects,
floor channels and existing geometry/visibility/native-write controls execute.

Run `python3 scripts/check-terrain-budget-runtime.py` for the production case and
its causal variants. Focused source checks cover frame order, partial order and
instrument writes. The integration receipt records the final assertion/control
counts and source hashes. This is focused terrain validation with unchanged
integration evidence inherited; it is not a new complete project gate. These
access counts do not establish a millisecond saving, Frame FPS, multiplayer gain
or a correct headset picture.
