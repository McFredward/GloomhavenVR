# Map icon overlap ownership — build 530

## Evidence and scope

The maintainer's build-529 report says partially overlapping scaled symbols are difficult or
impossible to select. The local log identifies `1.0.5 / ModBuild 529 / 579ee1f65`;
`schwer_zu_drücken.jpg` shows the close Guildmaster locations. The remote logs are build 500
and do not describe this run. The screenshot establishes the cluster, not which individual
physics hit won.

Source proves two independent starvation routes:

- `MapLocationInteractor.PickFrom` chose the smallest **3D box-entry distance** among drawn
  icon pads. Their thickness is 35% of their short edge, so scale and approach angle decide
  precedence in an overlap. That is unrelated to which symbol centre the player aims at.
  Its sixteen-hit nonalloc buffer could also omit candidates in a dense cluster.
- `PokeInteractor.TickPokeables` chose the nearest collider point. Inside overlapping boxes,
  both distances are zero and registration order wins, regardless of the intended icon.

Both campaign and Guildmaster use these paths. Native quest selection/continuation is not
changed.

## Implementation

Intersect the ray with the same plane on which `MapIconLayer` draws the symbols, after the
existing foreground bar/panel/fan and carry-release admission. Inspect every active registered
pad, require containment in its rotated footprint, and choose the nearest centre. Break exact
ties by a stable instance key. Existing uninflated pad footprints keep hover growth from moving
the boundary. Native authored collider fallback remains when no painted pad owns the point.

Finger targets use the same owner via an optional candidate filter. The filter only rejects
neighbouring map targets; ordinary distance arbitration against other objects, contact range,
grip gating, re-arm, hover transitions and native callbacks remain intact. Cache the owner once
per fingertip point/frame to avoid a quadratic registry traversal. Release/rescan invalidates
that cache. No gameplay model, icon artwork, network state or logging is changed.

## Validation and limits

- `scripts/map-icon-picking-tests.sh`: 333 production geometry/foreground assertions,
  12 source integration bindings, three disconnected-binding negatives and four compiled
  mutation controls (centre ownership, footprint, stable tie, plane direction).
- `scripts/map-flow-tests.sh`: 1,997 assertions, eight negative controls; native continuation
  fixture matches the read-only game source.
- Strict Release build: zero warnings/errors.

Hardware still needs both campaign and Guildmaster clusters at enlarged icon sizes, aiming at
both centres and exposed edges, fingertip pressing, and a foreground window/grab bar across
the map. Perfectly coincident centres have a deterministic winner; this change does not add a
new disambiguation menu for symbols the native map places at the exact same coordinates.
