# Game-native environment assets — mechanisms, reachability, feasibility

**Investigation, 2026-08-12.** Question (user, verbatim): *"Ist es vielleicht nicht sogar möglich
Levels bzw Assets und Effekte aus dem Spiel direkt zu nutzen?"* — rebuild the mod's two ambient
3D environments (`Env_Cellar`, `Env_Swamp`, currently third-party low-poly, rejected for style)
from the game's own levels/assets/effects. Constraint: mod-side only, but runtime
instantiation/cloning of game assets is allowed. Sources: `decompiled/GH.Runtime` +
`decompiled/ScenarioRuleLibrary` (game), `src/GloomhavenVR` + `.planning` (mod). All line numbers
are from this repo's decompiled/mod sources.

---

## 1. How the game provisions scenario environment art

### 1.1 The pipeline in one paragraph

A scenario is: SceneController additively loads the **"Game" scene**
(`decompiled/GH.Runtime/SceneController.cs:203-213` — `ESceneType.Scenario => "Game"`), the
Choreographer additively loads the **"ProcGen" scene**
(`decompiled/GH.Runtime/Choreographer.cs:14722-14729` —
`SceneManager.LoadSceneAsync("ProcGen", LoadSceneMode.Additive)`), then for every room it loads a
**map template prefab via Addressables** and instantiates it under the ProcGen scene's `Maps`
root (`Choreographer.cs:14938-14949`):

```csharp
original = AssetBundleManager.Instance.LoadAssetFromBundle<GameObject>(
    "misc_mapsprocgen", "Map " + map.MapType, "mapsprocgen");   // Choreographer.cs:14943
GameObject newMapRootGameObject = UnityEngine.Object.Instantiate(original, m_MapSceneRoot.transform);
```

The map prefab is a *template*: hex tiles (`UnityGameEditorObject` type Tile/Coverage/EdgeTile),
walls, doors, a `ProceduralMapTile` + `ProceduralStyle` + `ApparanceEntity` per tile. The visual
dressing — floors, masonry, props on walls, torches, moss, water surfaces — is **synthesized at
runtime by the Apparance native engine** into `"Generated Content"` child containers
(`decompiled/GH.Runtime/ProceduralMapTile.cs:148-177,179-201`; layer 10, `ProceduralMapTile.cs:185-190`).
The look is fully parameterized by five style enums written into the Apparance parameter block
(`decompiled/GH.Runtime/ProceduralStyle.cs:138-166` → `WriteStyleParameters` writes
Biome/SubBiome/Theme/SubTheme/Tone + seeds).

### 1.2 The four load mechanisms, ranked by usefulness to the mod

**(a) Addressables by string path — the game's primary asset door.**
`AssetBundleManager` (a scene-persistent singleton, `decompiled/GH.Runtime/AssetBundleManager.cs:21,54`;
called from main-menu flows too, `SceneController.cs:1263`) wraps
`Addressables.LoadAssetAsync<T>(path)` (`AssetBundleManager.cs:136-145`) and
`Addressables.InstantiateAsync(path, parent, …)` (`AssetBundleManager.cs:147-157`). Paths are
plain strings of the form `Assets/_AssetBundles/<folder>/<file>.<ext>`
(`AssetBundleManager.cs:277-287` — `LoadAssetFromBundle`, synchronous via `WaitForCompletion`).
Known folders from code: `mapsprocgen`, `mapsleveleditor` (`Choreographer.cs:14943,14947`,
`LevelEditorController.cs:353,691`), `heroes`, `npcs`, per-DLC `Content/CharacterPrefabs`
(`AssetBundleManager.cs:295-303`). Whole label groups: `always_loaded_base`,
`always_loaded_dlc_1/2` (`AssetBundleManager.cs:84-93`). **Addressables works from any scene once
initialized (it is initialized at boot — the initial load screen drives
`LoadAllInitiallyRequiredBundles`, `AssetBundleManager.cs:66-114`), and the full key catalog is
enumerable at runtime** via `Addressables.ResourceLocators[i].Keys` — this is the diagnostic lever
(§5).

**(b) Resources.Load — small but instantly available anywhere.**
`Resources.Load("Settings/GlobalSettings")` (`decompiled/GH.Runtime/GlobalSettings.cs:381`) yields
the game's central prefab table: `m_ApparanceProps` (Trap, GoldPile, Chest, obstacles, Spawner,
TerrainWater/HotCoals/Rubble/Thorns, Portal, MonsterGrave — `GlobalSettings.cs:167-206,397-427`),
`m_SpecificProps` (RockSingle/Triple, BearTrap, SewerPipe, GraveSingle/Double, plinths —
`GlobalSettings.cs:209-240,515-536`), plus **plain FX prefab references**: hit effects, heal,
death-dissolve, condition FX (`GlobalSettings.cs:11-59,310-318`), and `VisualEffects`
(HexSelectControlParticles, CardSmoke — `GlobalSettings.cs:288-295,355`). Also
`Resources.Load<GameObject>("Hex")` (`UnityGameEditorRuntime.cs:40`) and
`Resources.Load<Material>("Decals/MeshDecalMaterial")` (`ProjectorModifier.cs:41`).
Caveat: the `m_ApparanceProps` entries are themselves Apparance generators
(`ProceduralProp` requires `ApparanceEntity`, `decompiled/GH.Runtime/ProceduralProp.cs:5-8`) —
without the engine they stay empty; the `m_SpecificProps` and the FX lists are ordinary prefabs.

**(c) The Apparance engine — the actual art generator.**
`ApparanceEngine` is a scene component (Apparance.Unity.dll; the mod references it publicized,
`src/GloomhavenVR/GloomhavenVR.csproj:37-42`). `ApparanceEngine.Instance` **is null in menus
before the first scenario** (mod knowledge, hard-verified:
`src/GloomhavenVR/Core/ApparanceDetailFocus.cs:176-181` — "No engine (menus before first
scenario)"); it arrives with the ProcGen/Game scenes. Its resource lists load lazily through
`ApparanceResourceListLoader` — Addressable `AssetReference`s living on the engine's own
GameObject (`decompiled/GH.Runtime/ApparanceResourceListLoader.cs:11-38,76-84`). Synthesis is
**viewpoint-driven and distance-scaled**: the engine feeds the native side one world position per
frame (`Engine.Update(0.1f, view_position)`), detail falls off with distance from that point, and
an `ApparanceEntity` whose GameObject goes inactive has its native entity destroyed and fully
re-synthesized on re-activation — the entire mechanism, including the mod's working
counter-measure (a steered focus object via the engine's own `EnableDetailFocus` override), is
documented and shipped in `src/GloomhavenVR/Core/ApparanceDetailFocus.cs:7-77` and its
`FocusDriver` (`:99-200`). Style changes on a live tile trigger a rebuild:
`ProceduralStyle.CheckChanges` → `Rebuild()` → `IProceduralContent.RebuildContent()`
(`ProceduralStyle.cs:189-247`); `ProceduralProp.Apply` shows the minimal invocation of an entity
(`ProceduralProp.cs:51-59`: set `PartialParameterOverride`, toggle `IsPopulated`,
`NotifyPropertyChanged()`).

**(d) Raw AssetBundle files on disk — exists, but legacy-shaped.**
`BundleLoadSettings.BundleLoadConfig.AssetsBundleLoadPath` points at
`StreamingAssets/AssetBundles/{MiscBundles,HeroBundles,NPCBundles}/<name>` and DLC packages
(`decompiled/GH.Runtime/BundleLoadSettings.cs:36-53`). The runtime code path actually loads
everything through Addressables (whose own bundles live under `StreamingAssets/aa`), so direct
`AssetBundle.LoadFromFile` against the install dir is possible but redundant — Addressables is
strictly easier and already handles dependencies.

### 1.3 Lighting, fog, atmosphere — how the game does ambience

Two components, both **inside the map prefabs**:

- **`StaticAmbience`** (one per scenario, `decompiled/GH.Runtime/StaticAmbience.cs`): sets
  `RenderSettings.skybox` + `RenderSettings.ambientMode` (`:50-51`) and pushes a Beautify
  post-processing profile onto the scenario camera (`:62-75`). Applied from
  `ProceduralScenario.UpdateAmbience` (`decompiled/GH.Runtime/ProceduralScenario.cs:580-585`).
- **`DynamicAmbience`** (one per map tile, `decompiled/GH.Runtime/DynamicAmbience.cs`): carries
  ambient intensity/color (`:110-111` → `RenderSettings.ambientIntensity/ambientSkyColor`), a set
  of **template `Light` children that are instantiated and intensity-blended** as the camera
  focus approaches the tile (`:150-172`), and a **`DynamicFogProfile`**. The fog is **NOT
  `RenderSettings.fog`** — it is the third-party *DynamicFogAndMist* **per-camera image effect**:
  `DynamicFog` is `[RequireComponent(typeof(Camera))]` with `OnRenderImage`
  (`decompiled/ThirdParty/DynamicFogAndMist/DynamicFog.cs:5-11`), and `DynamicAmbience.BeginBlendIn`
  cross-fades profiles on the scenario camera's component
  (`DynamicAmbience.cs:63-77` → `dynamicFog.SetTargetProfile(fogProfile, …)`). The only global
  render state the game touches for atmosphere is skybox + ambient light. Cross-tile blending is
  driven by camera-focus proximity in `ProceduralScenario.UpdateAmbience`
  (`ProceduralScenario.cs:610-698`).

Consequence for the mod: swamp-style fog in our environment should be **local** (particle fog, as
the current Env_Swamp already does, or a mod-owned `DynamicFog` clone on the head camera —
risky in stereo, see the parked wall-fade stereo-rivalry finding) — never `RenderSettings.fog`.
Point lights + `LightFlicker` (`decompiled/ThirdParty/LightFlicker.cs`) are how the game's torch
light behaves; flame visuals are Apparance-generated content / bundle particle prefabs (RFX4
particle framework is present: `decompiled/GH.Runtime/RFX4_ParticleLight.cs` etc.), not
code-named constants — enumerating them needs the runtime catalog dump (§5).

---

## 2. What is reachable OUTSIDE a scenario (menu included)

| Mechanism | Menu / any scene? | Evidence |
|---|---|---|
| `Addressables.LoadAssetAsync(path)` / `InstantiateAsync` | **YES** — initialized at boot, singleton wrapper used from menu flows | `AssetBundleManager.cs:66-114`, `SceneController.cs:1263` |
| `Resources.Load` (GlobalSettings prefab table, Hex, decal mat) | **YES** — Resources is scene-independent | `GlobalSettings.cs:381` |
| Map template prefab instantiation | **YES** (it's just an Addressables prefab: `Assets/_AssetBundles/mapsprocgen/Map <EMapType>.prefab`) — but renders only bare template tiles without the engine | `Choreographer.cs:14943`, §1.1 |
| Apparance synthesis (the actual room dressing) | **NO by default** — `ApparanceEngine.Instance` is null before the first scenario; the engine + its `ApparanceResourceListLoader` (serialized AssetReference list) live in a game scene | `ApparanceDetailFocus.cs:176-181`, `ApparanceResourceListLoader.cs:22-38` |
| `SceneManager.LoadSceneAsync("ProcGen", Additive)` | **UNVERIFIED-BUT-PLAUSIBLE from any scene** — plain build-settings scene load, no scenario-state precondition visible at the call site; what else rides in that scene (RoomVisibilityManager etc., `Choreographer.cs:14811`) and whether it NREs without a Choreographer must be probed on hardware | `Choreographer.cs:14722-14737` |

If Apparance content turns out to be scenario-locked in practice, the fallbacks are:

- **(a) clone-and-persist**: mid-scenario, clone a fully built tile's `"Generated Content"`
  container (meshes/materials are ordinary Unity objects at that point; the containers are
  `HideAndDontSave` — the mod already learned to see them, `src/GloomhavenVR/Core/SceneRegistry.cs`
  round-6 note), strip all game components, `DontDestroyOnLoad`, reuse forever. In-memory only —
  survives scene changes, not game restarts (re-capture per session, or serialize meshes to disk
  once).
- **(b) capture-and-rebuild**: the mod has offline precedent — UnityPy extraction of meshes +
  textures from `ressources/GH_Data/sharedassets*.assets` (map-capture work, memory
  `map-capture-bug.md`) — a one-time offline harvest of wall/floor/prop meshes into the mod's own
  bundle, i.e. game-authentic art shipped the mod's way.
- **(c) direct bundle load from install dir**: possible (`BundleLoadSettings.cs:36-53` names the
  folders) but strictly dominated by Addressables (§1.2d).

---

## 3. Concretely: our two ambiences in the game's own vocabulary

The style axes a runtime loader can request (all
`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary.YML/ScenarioPossibleRoom.cs`):

- **EBiome** (`:16-25`): `Crypt`, `Dungeon`, `Cave`, `Forest`, `City`.
- **ESubBiome** (`:27-67`): `Necropolis`, `Ruined`, `Sewers`, `CorpseWood`, **`Marsh`**,
  `StoneRooms`, `CityWalls`, `StoneWoodenRooms`, `Shack`, `Rot`, DLC variants (Ship, Tunnels,
  Void, Arena…).
- **ETheme** (`:69-180`): `Torture`, `Library`, `Treasure`, `Kitchen`, `AlchemyLab`, `Chapel`,
  `BurialChamber`, `Mausoleum`, `Shrine`, `AncientLibrary`, `Study`, `Armoury`,
  `DeepForestGlade`, `DruidsGrove`, `MagicGrotto`, `MushroomLand`, **`StillWaters`**,
  `WoodcuttersCottage`, `Volcanic`, `Ice`, `OldMine`…
- **ETone** (`:200-228`) — this is the lighting mood: `Dark`, `Natural`, `Spectral`, `Demonic`,
  `Toxic`, `Evil`, `Crystal`, `ForestDefault`, `ForestEerie`, **`ForestMoonlight`**,
  `ForestFairy`, **`Bioluminescence`**, `LavaLamp`, `Gaslight`, **`Candlelight`**.

**Dungeon cellar** → `Biome=Dungeon` (or `Crypt`), `SubBiome=StoneRooms` (or
`StoneWoodenRooms`/`Sewers`), `Theme=Kitchen`/`Library`/`Treasure`/`Study`,
`Tone=Candlelight` (or `Gaslight`/`Dark`). **Swamp night** → `Biome=Forest`, `SubBiome=Marsh`
(or `CorpseWood`/`Rot`), `Theme=StillWaters`/`MushroomLand`/`DruidsGrove`,
`Tone=ForestMoonlight` (or `Bioluminescence`/`ForestEerie`). These are exactly the values the
Choreographer prints per room ("Scenario Base Biome is set to …", `Choreographer.cs:14809-14822`)
and `ProceduralScenario.SetRandomRoomStyles` randomizes (`ProceduralScenario.cs:518-540`).

**Room geometry to request**: any `EMapType` name
(`decompiled/ScenarioRuleLibrary/ScenarioRuleLibrary/EMapType.cs` — `A`…`N` singles, composites
like `ABHM`, DLC rooms) as `Assets/_AssetBundles/mapsprocgen/Map <name>.prefab`. A single small
room (`Map A`-class) is the right size for an ambience shell.

**FX**: torch/candle *light* = per-tile `DynamicAmbience` light templates + `LightFlicker`;
flame/firefly/fog *visuals* = Apparance generated content and bundle particle prefabs (RFX4),
plus instantly-loadable code-referenced FX in `GlobalSettings` (§1.2b). Water: the game's terrain
water is an Apparance prop (`GlobalSettings.cs:193,416`). Fog: per-camera image effect, §1.3 —
do NOT set `RenderSettings.fog`; skybox/ambient are set globally by the game only inside
scenarios, so a menu-scene environment may set them but must restore on despawn.

---

## 4. Feasibility verdicts

| # | Approach | Verdict | Effort | Notes |
|---|---|---|---|---|
| A | Direct Addressables load of game prefabs by path | **Works anywhere, partial art** | S | Map templates render bare (hex tiles, no dressing) without Apparance; plain props/FX from `GlobalSettings` + catalog-discovered prefabs work fully. Necessary substrate for B; not sufficient alone. |
| B | Apparance micro-generation (instantiate `Map A`, set `ProceduralStyle`, let the engine dress it) | **The real prize — full Gloomhaven-native rooms, restyleable by enum** | M (in-scenario) / M–L (menu: engine bootstrap via additive ProcGen load or engine-GO persist, unverified) | The mod already drives the engine's viewpoint (`ApparanceDetailFocus`) and knows entity build state (`ApparanceEntity.IsBusy`). Two rooms ≈ two prefab instantiations + 5 enum writes each. Perf: one-time synthesis cost, then static; renderer count of one small room is far below the big-room census the mod already survives (`.planning/perf-zoomed-out.md` §1). |
| C | Scenario-time capture + `DontDestroyOnLoad` persist | **Reliable fallback, session-scoped** | M | Clone built `Generated Content`, strip components, reparent. No engine needed after capture; requires one scenario visit per session (or a one-time mesh serialization to make it permanent). |
| D | Restyle our own bundle with game-matched art (UnityPy offline harvest of meshes/textures → `unity/GloomhavenVR.Assets`) | **Always works, zero runtime risk, most hand-work** | M–L | Proven tooling (map-capture memory). Keeps the current `SkyAlternative` pipeline untouched — only the prefab content changes. |

**Implement first: B, staged behind A's diagnostic.** B is the only approach that delivers "levels
… direkt aus dem Spiel" with the game's own lighting moods (Tone), and every risky unknown in it
is answerable by one log-dump build (below). If the menu-scene engine bootstrap fails on hardware,
B still works wherever the engine exists (scenario + level editor), and C covers the menu with the
very objects B built — the two compose: B generates, C persists.

### The de-risking diagnostic (one build, one log)

A dev-console command / startup dump on the user's rig that logs:
1. **Addressables catalog census**: iterate `Addressables.ResourceLocators` → all string keys,
   grouped by `Assets/_AssetBundles/<folder>/`; flag `mapsprocgen/*`, anything matching
   `torch|flame|fire|fog|firefly|water|env|sky` — this enumerates every loadable env asset + FX
   prefab name we cannot see from decompiled code.
2. **Engine availability matrix**: `ApparanceEngine.Instance` null/alive in MainMenu, CampaignMap,
   Scenario, and back in MainMenu after a scenario (does the engine persist?).
3. **The B-probe**: from the MAIN MENU, `SceneManager.LoadSceneAsync("ProcGen", Additive)`; log the
   scene's root objects, whether `ApparanceEngine.Instance` comes alive, then
   `Addressables.LoadAssetAsync<GameObject>("Assets/_AssetBundles/mapsprocgen/Map A.prefab")`,
   instantiate under a mod root far from origin, write `Biome=Dungeon, SubBiome=StoneRooms,
   Tone=Candlelight` into its `ProceduralStyle`s, point the detail focus at it
   (`ApparanceDetailFocus` infrastructure), and after 10 s log the renderer census under
   `"Generated Content"` (the exact success metric the mod already used for the reveal fix:
   "maptile 'L' went Preview/0-renderers → All/229 renderers").
4. **Ambience inventory** (in-scenario): dump `StaticAmbience.skyboxMaterial.name`, each tile's
   `DynamicAmbience.fogProfile.name` + light-template descriptions — the palette for our two moods.

---

## 5. Free-movement note (environment anchoring vs. locomotion)

All three locomotion systems move the **rig root**, never the world: Flight translates it
(`src/GloomhavenVR/Rig/Flight.cs:213` — `rig.position += step`, speed in apparent meters scaled by
rig scale), SnapTurn rotates it about the head (`src/GloomhavenVR/Rig/SnapTurn.cs:161-162` —
`rig.RotateAround(pivot, Vector3.up, degrees)`), and WorldGrab writes its position/rotation/scale
directly (`src/GloomhavenVR/Rig/WorldGrab.cs:362,401,403`; its doc states the invariant: "All
motion is applied INVERSELY to the rig root — game objects are never moved", `WorldGrab.cs:20-21`).
The current environment is parented **under** that rig root with identity local pose
(`src/GloomhavenVR/Core/SkyAlternative.cs:366-431`, `ParentToAnchor`), which was the design goal
for a real-space room (stands still while leaning/walking, immune to diorama zoom —
`SkyAlternative.cs:72-98`) — but it means stick-flight, snap-turn and world-grab **carry the
environment along with the player**: only physical walking (head moving within tracking space)
traverses it. To let the player fly/walk through a game-scale environment, the instance must be
anchored in **world space** (spawn-time scale = current rig scale to keep authored meters
perceptually right), accepting that WorldGrab's pinch-zoom then rescales the player relative to it
— which is the correct semantics for "moving through a level" (and the existing rig-root anchor
remains right for the room-around-the-table use). A per-style anchor flag on `SkyAlternative`
(RigRoot vs. world) is the minimal change; the far-plane floor hook already exists
(`SkyAlternative.cs:215-216` → `VRRigDriver.TickClipPlanes`).

---

## Appendix: mod files that already touch these systems

- `src/GloomhavenVR/Core/SkyAlternative.cs` — env spawn/anchor/despawn pipeline (replace content here).
- `src/GloomhavenVR/Core/ApparanceDetailFocus.cs` — engine access, viewpoint steering, entity build state.
- `src/GloomhavenVR/Core/SceneRegistry.cs`, `MaterialLoaderHeal.cs` — Apparance container hideFlags/lifecycle knowledge, cheap component registries.
- `src/GloomhavenVR/Core/WallSegmentFade.*` / `UnseenTileOrder.cs` — tile/wall hierarchy consumers (walls under per-tile `Walls` nodes, `ProceduralScenario.cs:133-150`; `EN_Unseen_*` preview hexes).
- `src/GloomhavenVR/Core/BundleDiagnostics.cs` — bundle failure forensics (mod-bundle side).
- `unity/GloomhavenVR.Assets/Assets/Bundle/Environments/` — the current Env_Cellar/Env_Swamp prefabs + Env shaders (approach D's target).
