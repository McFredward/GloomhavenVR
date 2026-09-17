# Guildmaster tabletop — build 528

## Evidence and limitation

The maintainer's build-527 report describes the Guildmaster bench/barrel with a floating map and
requests the same tabletop used by the campaign, fitted without intersecting those props. No
current screenshot of this room was supplied. The remote logs identify build 500 and cannot
confirm the current Guildmaster scene. Current logs do not print a tabletop renderer census.

The existing campaign implementation does **not** manufacture a tabletop: `MapTableLegs` finds a
native slab already flush under the map, then creates only its underside and optional legs.
Its historical measured source is `GH_Map_TableTop_Lg`, 1.55 × 0.148 × 2.30 m. Native
`SceneController` loads separate `CampaignMap` and `NewAdventureMap` scenes. Therefore the old
campaign-only premise provides no replacement when the Guildmaster scene has no suitable slab.
This explains the missing fallback in source; it does not prove the exact current native object
inventory or shader state from the supplied logs.

## Change

`GuildmasterMapTable` runs only for a non-campaign map and owns a geometry-only presentation of
that original tabletop. Discovery checks resident native renderers/assets, then asynchronously
requests matching catalog assets and matching subassets from already loaded non-scene bundles.
No gameplay prefab is instantiated, no campaign scene is loaded, and native prop transforms and
materials are never written. Addressables handles are retained while used and released on exit;
native bundle ownership remains with the game.

The slab covers the measured parchment with a modest rim. The opening-time fit reduces that rim
against measured bench/barrel bounds with a 20 mm clearance; it refuses an impossible fit rather
than creating an intersection. Bounds of temporarily disabled prop renderers still reserve space
while their native materials load. Stable obstacle ordering gives identical native geometry the
same answer for each peer. The existing underside/leg builder then consumes the resulting slab.
Campaign rooms retain their original table and all existing environment/MR leg gates.

Discovery and fitting are opening-time work. A built/declined opening is a few state/reference
reads per tick, not repeated asset or scene scans. Switches and teardown release the owned slab.

## Validation

- `bash scripts/guildmaster-table-tests.sh`: 1,506 assertions executing production fit code,
  including scaled/translated maps, full parchment coverage, simultaneous bench/barrel clearance,
  and impossible geometry. Two deliberate regressions (lost clearance and collision bypass) fail.
- `bash scripts/ci-build.sh Release`: strict build, 0 warnings/errors (final run recorded by the
  integrator).

The game installation's scene/catalog files are not available in this checkout. A cold Guildmaster
start must still establish that the original campaign mesh/material is catalog-accessible or
resident. Missing originals produce `GUILDMASTER TABLE unavailable`, never a fabricated table;
intersecting furniture produces `GUILDMASTER TABLE fit refused`. A successful fit prints the mesh,
physical dimensions and prop count in `GUILDMASTER TABLE fitted`. The first headset check should
start directly in Guildmaster, inspect all table edges/legs against the barrel and bench, then
switch maps and return to a campaign to confirm both teardown and unchanged campaign furniture.
