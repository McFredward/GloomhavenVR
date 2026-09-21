# Town stations: floor, original dressing and practical lights

## Evidence

The supplied `LogOutput.log` banner is ModBuild 541 / assembly 1.0.7.0.
`höhe_händler.jpg` shows the actor soles and furniture supports above the custom
forest ground. Build 541 assigns `MapRoomSeat.Seat.FloorPosition.y`, the tracking
floor, to the station. The environment independently places its floor below the
map (`SkyAlternative.TryPlaceRoom`); its placed room transform is authoritative.
The log also records that the native map directional light excludes mod layer 27.
This explains why a normal lit shader alone cannot replace the former studio shader.

## Placement

`TownServicePlacement` preserves the canonical parchment reading frame and never
reads a head pose or player zoom. The three stations occupy separate sides of the
clearing. Only the elected author resolves position. A changed placed room or
canonical map frame triggers a new placement; steady frames do not write poses.
Peers retain the published author pose, even if their environment preference differs.

The source mesh under `RoomGeo/Ground` or `RoomGeo/Floor` supplies the actual
height at the stand, using triangle interpolation rather than bounds or a ray that
could hit scenery. Both shipped `Env_S_Ground` and `Env_C_Floor` were inspected and have
`m_IsReadable=true`. The exact environment plane is the fallback for unreadable mesh
data. Default/MR without a custom room retains the canonical map floor.
The actor and furniture are authored with their contact at station-local zero;
animated hands and inflated skinned bounds never determine placement.

## Environment clearance audit (build 542)

The author offsets, in parchment reading metres, are merchant `(-1.60, .55)`,
temple `(1.60, .55)` and enchantress `(0, 1.70)`. NPC local anchor `z=+.65`
is unchanged. This is a deterministic clearing layout, not a head-relative layout
or a runtime forest collision search. Only the elected author publishes these poses.

The previous outer offsets `(+/-2.15, 1.15)` / `(0, 2.75)` were unsafe. Actual
forest `Rocks1` vertices entered the enchantress's conservative body box at reading
yaw 90; cellar `WallN` entered that box at yaw 0. Forest shrubs/ferns also entered
the old station volumes. These were obstacle vertices inside a reserved body box,
not a claim that final animated skin triangles had already been collision-tested.

Evidence uses `prebuilt/gloomhavenvr.bundle`, SHA-256
`fe1a659c17b4151e929691aa070d402b8cd299a462315b1d6691d2622d491693`, with
`assets/bundle/environments/env_swamp.prefab` and `env_cellar.prefab`.
UnityPy decoded actual mesh vertices/triangles and applied the complete prefab
transform hierarchy. Projected triangle vertices/edges, including triangles
containing the origin, supplied radial minima in the standing height band through
2.10 real metres. This avoids the overly broad bounds of welded tree/rock meshes.
The temporary evidence files are `/tmp/gvr-town-environment-bounds.json`,
`/tmp/gvr-town-triangle-audit.py` and `/tmp/gvr-town-triangle-audit.log`.

`BuildEnvironmentRooms.cs` declares authored play diameters 9 / 6.5. Both room and
station placement measure the same parchment extent: `stationScale=extent/1.20`,
`roomScale=4.5*extent/authoredDiameter`. Thus one real station metre is 1.666667
forest units or 1.203704 cellar units. The build-541 log independently records a
237.74-unit parchment, forest scale 118.870 and seat scale 198.12 (reading yaw 90).
The relevant solid-prop/understory distances from room centre are:

| Environment geometry | Authored radial minimum | Real metres |
| --- | ---: | ---: |
| Cellar stool | 3.651 | 3.033 |
| Cellar north wall | 4.478 | 3.720 |
| Forest fern 0 | 5.127 | 3.076 |
| Forest stump 0 | 5.502 | 3.301 |
| Forest branches 0 | 5.555 | 3.333 |
| Forest near trunks | 5.691 | 3.415 |
| Forest rocks 1 | 6.055 | 3.633 |

The reserved station-local envelope is `|x| <= .90`, `-.50 <= z <= 1.15`;
all roots have radius at most 1.70. Since positive local Z faces outwards, its
maximum radius is `sqrt(.90^2 + (1.70+1.15)^2) = 2.988729` metres at every reading
yaw. The near counter edge stays at least 1.33189 metres from map centre, outside
the usual approximately 1.05-metre map/seat ring. The production-method fixture
checks all three offsets at 5-degree steps and rejects the former outer ring.
This protects authored placement, not arbitrary player movement or changed future
assets. Final actor clips must be sampled against this envelope when the final face
bundle is available; that verification is separate from the C# fixture. Ground-level
moss/grass carpets, atmospheric effects and flexible vegetation are not treated as
solid room walls; headset appearance and real ground contact still need inspection.
A second all-mesh audit (`/tmp/gvr-town-triangle-all.log` and
`/tmp/gvr-town-canopy-audit.log`) explicitly identifies limits: hanging forest
`Canopy` vertices enter the inner enchantress conservative body box at reading yaw 0,
at real heights 1.744–1.771 m. Canopy triangles within the reserved overall radius
can reach 1.369 m. Thus this is a solid wall/trunk/rock clearance proof, **not proof
of leaf/head clearance**. Exact final actor skin and foliage visual overlap need
separate validation or authored foliage clearance. Element-dependent growth/fire
and low carpets are also outside the static solid-prop guarantee. A cellar web
strand enters the radial region at height 1.976 m; final actor height determines
whether it is relevant.
The cellar's lowest ceiling timber is 2.489 real metres above its floor.

## Original game asset provenance

All decoration loads through original Addressables resource-list assets at runtime.
No original asset is repackaged or changed:

| Station | Original resource list and entry | Original object |
| --- | --- | --- |
| Merchant light | `PCG_Gaslight.asset`, `Gaslight.Lighting.Torch.Wall#1` | `PCG_TO_Lantern_01` |
| Merchant coins | `PCG_Treasure.asset`, `Treasure.Clutter.FloorSmall#3` | `CR_TR_CoinsScatter_Small_02` |
| Merchant book | `PCG_Library.asset`, `Library.Clutter.Shelf.Individual#3` | `PCG_CR_ST_Shelf_Book_03` |
| Temple candles | `PCG_Tone_Candlelight.asset`, `Candlelight.Lighting.Torch.Wall#3` | `PCG_TO_Candle_02` |
| Temple offering cup | `PCG_Chapel.asset`, `Chapel.Clutter.Shelf.Individual#7` | `CR_ST_Shelf_Druidic_Cup_01` |
| Enchantress candle | `PCG_Tone_Candlelight.asset`, `Candlelight.Lighting.Torch.Wall#1` | `PCG_TO_Candle_01` |
| Enchantress vessels | `PCG_AlchemyLab.asset`, `AlchemyLab.Clutter.Shelf.Individual#3` | `CR_ST_Shelf_Alchemy_Jugs_01` |
| Enchantress book | `PCG_Library.asset`, `Library.Clutter.Shelf.Individual#1` | `PCG_CR_ST_Shelf_Book_01` |

Paths have prefix `Assets/PCG/`. Entries and referenced object names were checked
against the supplied original `GH_Data/StreamingAssets/aa` catalog and bundles.

`TownServiceDecor` reconstructs only Transform/MeshFilter/MeshRenderer objects.
It never instantiates original controllers, so neither `Awake` nor native material
loaders can execute on a copy. MaterialLoader asset references are read and loaded
through independently owned handles. Original meshes, textures, normals, UV transforms
and tints are retained; owned material copies use the stereo town shaders for lighting
and coordinated dissolve. No procedural coins, bottles or flame spheres are substituted.

Native candle flame/glow meshes reference built-in mesh 10210 (`Quad`), verified
as XY planar from `GH_Data/Resources/unity default resources`. They can therefore
billboard in the flame shader without altering physical furniture. The native flame
material contains an 8×8 flipbook. Its original shader is
`VFX/ParticleMasterUnlitAdd_Shd`; only compiled programs are available. The new
one-loop-per-second decorative playback uses those original frames and the shared
NPC clock. Its exact timing equivalence to the compiled native shader is not proven.

## Lighting and lifecycle

The bundled NPC shader receives actual scene ambient and lights. A single owned
mod-layer directional light follows the custom room's measured moon direction and
colour, not a fixed studio key. Default/MR adds no artificial directional fill.
Each stand has a bounded warm point light at the original lantern/candle location
(two for the temple's two candles, four in total),
using `ForceVertex` for the existing zero-pixel-light performance setting. Lights
start only once their visible source has loaded. Native lights and global ambient
settings are untouched. All materials, Addressables handles and owned lights are
released with the station. Geometry does not intercept lasers or hand targeting.

## Validation and integration

- `python3 scripts/check-town-service-setting.py`: 1,581 assertions executing the
  production geometry, 44 executing the complete production station lifecycle,
  plus seven compiled runtime negative controls. The prior outer-ring source fails
  the new baseline at runtime; the candidate offsets pass with the current integrated
  station implementation (including author handover and incoming scale changes).
- Strict Release build: zero warnings and zero errors.
- Root integration calls `Station.RefreshEnvironment(authorPose)` for all residents;
  observers need the call for asynchronous decoration and environment lighting too.
- `TownServiceAssets.Shader` loads `townflame.shader` explicitly from the town bundle;
  an unreferenced shader asset is not guaranteed to be available through Shader.Find.
- Prefab worker authors actual grounded soles, the functional merchant card rails,
  and the environment-lit / original-flame stereo shaders.

Hardware floor contact, scenery clearance, brightness, native decoration appearance
and repeated room switches remain unverified until the next headset test.
