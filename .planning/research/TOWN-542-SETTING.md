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

- `python3 scripts/check-town-service-setting.py`: 69 assertions executing the
  production geometry, plus two compiled runtime negative controls (tracking-floor
  regression and ignored terrain relief).
- Strict Release build: zero warnings and zero errors.
- Root integration calls `Station.RefreshEnvironment(authorPose)` for all residents;
  observers need the call for asynchronous decoration and environment lighting too.
- `TownServiceAssets.Shader` loads `townflame.shader` explicitly from the town bundle;
  an unreferenced shader asset is not guaranteed to be available through Shader.Find.
- Prefab worker authors actual grounded soles, the functional merchant card rails,
  and the environment-lit / original-flame stereo shaders.

Hardware floor contact, scenery clearance, brightness, native decoration appearance
and repeated room switches remain unverified until the next headset test.
