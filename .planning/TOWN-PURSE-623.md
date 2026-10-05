# Build623 purse lifetime and native temple hint

The paired host and visitor logs both identify ModBuild622. The host also reports
`temple.tooltip|` with sender structure `46A7FF42/23` and observer structure
`027ADDDF/17`, at `LogOutput.log:9693`. The user reports an immediate correct wrist
preview followed by intermittent disappearance of the actual held purse.

## Source-proven cause and change

The actual `TownServiceSync.TickCore` changed the same original purse body's key
from `ritual.purse` to `ritual.purse.held` on every grab. Publication allocated a
new module and retired the ready preview module. The already loaded native
geometry therefore became dependent on another complete snapshot before the fast
hand/pose stream could display it. The existing motion proof manually registered
one stable module and consequently never executed that defective publication edge.

The purse now has one stable independent visitor address through wrist preview,
physical hold, bowl settlement and return. Preparation remains active while its
original wrist gate is closed. Pose, compensated hand holder, opacity and physical
body bounds retain the existing numeric/interpolated path. No second geometry or
image transport is introduced, and the pre-drop purse ghost stays visitor-local.

The native temple tooltip is `UITempleSlotTooltip`, not `UITooltip`. Its native
`Build` calls the modifier's counter update, which retains pooled counter children
even after fewer counters are shown. Freezing its startup tree under one static
address caused the logged differing topology. The new canonical key describes the
original retained counter-child count. Each observer makes an inactive copy of the
original hint and its actual native counter prefab, then freezes and neutralizes
it. Source widgets are never modified. No native Show/Build/UpdateCounters or
gameplay callback runs on the observer. All original captions, graphics, state and
animation properties continue to come from the owner. Cached keys and templates
avoid repeated construction.

## Focused evidence and limits

- Actual publisher/codec path: 214 assertions; reverting the stable address to the
  old pickup-dependent key fails the module/template identity assertion.
- Counter preparation: 158 assertions covering exact binding topology and order,
  inactive retained counters, immutable template reuse, a prefab serialized inside
  the source hint itself, and no Awake/OnEnable.
  Omitting original counter children fails the topology assertion.
- Existing real PCG purse geometry and compensated holder motion: 774 assertions; production and
  the hand-style-scale causal control pass. The native geometry is unchanged.
- Native ritual boundaries: 287 assertions and a secondary visitor held-purse
  lease control pass. Its fixture now includes the existing record103 header
  codec partial; the original missing-dependency failure is retained.

The tooltip fixture supplies original HUD/component references with the native
serialized field names; it does not run the game's HUD. The body publisher test
executes the genuine TickCore/Publish/RegisterModule path. These prove the source
defects and bounded presentation behavior, not a Build623 headset result or a
measured improvement in internet latency. Root integration owns the additional
secondary visitor hint admission and the final complete gate.
