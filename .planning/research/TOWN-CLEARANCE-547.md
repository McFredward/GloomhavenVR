# Town station clearance (build 547)

## Measured problem and implementation

The old six-slot 4.8 m circle with a 2.1 XZ room multiplier does not fit the new
open stock terrace and the actual native inventory upper bound. That bound is
161 stock entries plus 512 owned entries, requiring eight 64-card return tables
per visitor. Four simultaneous workspaces are measured, not only the usual host
and one visitor. Initial contacts included all four cellar walls, forest trunks,
rocks and overlaps between visitor counters.

Residents retain their requested radius 4.8 m semicircle (merchant 0 degrees,
priestess -60, enchantress 60). Additional workspaces use radius 5.8 m at 105,
255 and 180 degrees. Return table pairs begin at X +/-2.85 m, rather than 2.60 m:
the earlier rotated return intersected its own main terrace end upright.

The mod's decorative map room shell uses a fixed 3.5 horizontal multiplier while
NPC mode is active, independent of inventory changes. Native game geometry and
room height are untouched. Discrete furniture, rocks and plants receive inverse
child XZ scale, retaining their original proportions as their positions move.
The shipped forest TrunksNear/Far meshes contain 106 disconnected complete trees;
private mesh copies translate each component without changing its triangle edge
vectors, UVs or normals. Authored meshes are never changed. Meshes are updated only
while the enable/disable factor changes; no steady-state mesh rebuild occurs.

Absolute room re-seating and relative uniform scale changes are reconciled without
compounding expansion. Disable, room replacement and teardown restore original
scales and meshes, including a scale change immediately before Reset.

## Evidence

- Actual Unity 2021.3.5 fixture: 12,069 assertions and five effective negative
  controls. Tests bind production sources, original environment bundle and actual
  Unity transforms/meshes; only room provider and time are fixture boundaries.
- Original bundle SHA256:
  `fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`.
- Exported C# layout poses plus original room triangles: 42 padded station parts,
  eight return tables at each of four simultaneous workspaces, zero intersections
  in either custom room. The test also checks a return against its own station.
- Read-only native levels 4, 5, 9 and 10 (campaign/Guildmaster variants): zero
  contacts against their authored active furniture.
- Private evidence: `.planning/debug/town-service-clearance/run-36v5e__d/` and
  `.planning/debug/town547-faces/layout-preserved-worst.json` in the worker checkout.

Reproduce the runtime fixture with `python3 scripts/check-town-service-clearance.py`.
Feed its `poses.csv` into `scripts/check-town-scene-layout.py` with
`--room-expansion 3.5 --return-modules 8`; add `--native-geometry` and
`--native-data-root` for original game scene verification. Developer UnityPy and
numpy dependencies are required for the separate mesh audit.

## Honest visual limits

The continuous cellar shell and welded canopy/shadow presentation still follow the
larger room shell. Canopy components are individual alpha cards, not whole trees;
their natural appearance and the pre-baked forest shadow alignment need visual
review. This work proves physical clearance, original prop/trunk proportions and
safe lifecycle behavior; it does not claim the old environment art is visually
identical after opening a substantially larger clearing. A purpose-authored large
room variant can improve that composition later. Headset observation remains
necessary for NPC accessibility, lighting and the surrounding canopy.
