# Game-Asset Environments — Postmortem (ModBuilds 127–131, abandoned by user ruling 2026-08-13)

**The ruling that ends it** (user, verbatim): "lösche bitte das alles wieder was mit der nutzung
der Spileeigenen Räume und assets zu tun hat. Es funktioniert scheinbar nicht wie ich mir das
vorstelle. Gehe wieder dazu über mit custom assets etwas zu bauen. Aber nicht low-poly sondern
zum Stly des Spiels passendes."

This file preserves what five hardware rounds learned, so a future revival (or any other feature
touching Apparance/Addressables/map assets) starts from facts instead of re-deriving them.

## What was built (chronology)

- **127**: Scenario-only scope (`VRModeStateMachine.ScenarioBoardExists` gate). 'Map A' template
  instantiated offscreen, dressed by the live Apparance engine with style enums
  (Cellar = Dungeon/StoneRooms/Candlelight; SwampNight = Forest/Marsh/StillWaters/ForestMoonlight),
  frozen, normalized, placed into the ambient frame. Result: invisible — see 128 causes.
- **128**: Four rendering causes found and fixed (all confirmed by the next log's census):
  1. Disabling `ApparanceEntity` DESTROYS its generated content one engine tick later
     (`EntitiesGameTick` ignores `enabled`; `CheckEntity→DestroyEntity` wipes Generated-Content).
     Fix: empty `m_GenerationTiers`/`m_GenerationRoot`/`m_Instances` FIRST, then disable.
  2. Apparance resource packets load on demand; a miss is cached engine-globally by name as a
     'Red Cube' debug placeholder and poisons the session. Fix: packet warmup + poison purge.
  3. `DynamicAmbience` clones every light at intensity 0; only the real scenario's
     `UpdateAmbience` ever blends them in. Fix: `SetLightLevel(1f)` + mod-layer masks + range
     rescale (Unity light range ignores transform scale).
  4. The mod's own WallSegmentFade adopted the clone's doors and dissolved them. Fix: mod layer
     from birth + tile machinery destroyed at finalize.
- **129**: Multi-room composites attempted ('Map ABHM'/'Map DDM'); staging moved beyond the far
  plane (fixed −50 wu was 36 cm real at rig scale 137); red-cube heal pass (donor swap by piece
  suffix). Composites FAILED to load → Map A fallback (route problem, see 130).
- **130**: Dual-route loading — the composites EXIST (user's Player.log boot dump lists 113–115
  always-loaded 'Map *' prefabs) but the full-path Addressables key only resolves single-letter
  maps on his catalog; the game's own always-loaded asset store
  (`AssetBundleManager._alwaysloadedHandles`) is the reliable synchronous route. Board-relative
  sizing experiment (2.75× board extent, floor at board underside) → **7 cm miniature** (board
  extent is world-tiny at diorama zoom). MAP CATALOG census born.
- **131**: Life-size again (room world scale = live rig scale, 11 real m), single-room
  `DLC_SC*_RM*` maps, atmosphere preserved (particles/Animators/LightFlicker with per-seat
  rebase), floors never deleted (cross-packet donors, clone patches, 10×10 floor gate),
  **scale-settle re-seat** — which produced the fatal finding: the room re-seating around the
  player after a zoom reads as a PLAYER TELEPORT and visually displaces the board (5 events in
  the final log). Combined with the persistent "assembled, not authored" look, the user ended
  the approach.

## Durable technical knowledge (verified on hardware, cited in the deleted code)

- **Apparance**: entities tick regardless of `enabled`; disabled-but-alive entities self-destroy
  their content. Freezing = detach the generated children from the entity bookkeeping, then
  disable. `ApparanceResourceTable.LookupResourceList` maps the first dot-segment through the
  FIRST table carrying it; `ApparanceResourceList.FindExternalAsset` mints a null-Object
  placeholder on a terminal miss (engine-global, session-poisoning; `RefreshResourceList`
  purges `Objects` but not lists). Detail focus is position-based; borrowing it
  (`ApparanceDetailFocus.Uninstall()` → override → restore) works for a build window.
- **Addressables/catalog**: `StreamingAssets/aa` does not exist in this repo (only `Managed/`
  is mirrored); the user's own `Player.log` boot dumps (`LoadAlwaysLoadedAddressable`) are the
  ground truth for HIS install. Full-path keys `Assets/_AssetBundles/mapsprocgen/Map X.prefab`
  resolve only for single-letter maps; composites/DLC rooms live in the always-loaded handle
  store. 113–115 'Map *' prefabs exist: singles A–N, composites (ABHM, DDM, GI, LML, …),
  52× `DLC_SC*_RM*` single-room maps, 5× Solo maps.
- **Tile materials**: map-tile materials load at reveal time via per-renderer `MaterialLoader`
  + Addressables (no retry path in the game — that is why `Compat/MaterialLoaderHeal.cs`
  exists and stays). Tile visibility (`ProceduralMapTile.ApplyVisibility`) gates
  Generated-Content containers.
- **Ambience**: `StaticAmbience` writes global RenderSettings/post — never let it run on a
  clone. `DynamicAmbience` is safe once driven (`SetLightLevel(1f)`) and parked. `LightFlicker`
  caches world pose at `Start` — a clone staged elsewhere must rebase flicker drivers after
  placement or flames drift toward the staging pose.
- **Anchor models, judged by the user**:
  - Rig-child (125): room rides every locomotion write — rejected ("frei bewegen").
  - World-anchored + perceived-constant rescale via `NotifyRigScaled` pivot algebra (126–129):
    free movement works; but the board wanders relative to the room under zoom (it can sink
    below the room floor) — rejected for the room (finding 3, ModBuild 130 round). STILL
    CORRECT for sky/FX (dome must stay a distant sky).
  - Board-relative world-fixed (130): miniature — board extent is world-tiny at diorama zoom.
  - Life-size world-frozen + re-seat on zoom settle (131): re-seat = perceived player teleport
    + board displacement. **Any post-spawn re-seat of a room the player stands IN is a
    teleport. Do not re-seat occupied rooms. Ever.**
- **Merge/verification discipline** (unchanged, in memory too): logs are the ground truth; the
  user tested stale builds three times — always verify the log's `ModBuild` line first.

## Why it ultimately failed the user's bar

The engine-built rooms never read as *places*: style-dressed templates ("wild zusammengewürfelt,
perfektes Rechteck"), donor-healed gaps, and the anchoring dilemma — a life-size room around a
zoomable diorama either drifts (perceived-constant), miniaturizes (board-relative), or teleports
(re-seat). The authored look he wants comes from hand-built custom rooms in the game's painterly
style (the replacement approach), where the mod owns every vertex and no engine fights back.

## What survives the deletion

- Scenario-only gate, MR precedence, sky-sphere hiding, the FX shell (star dome with the
  astrophoto sky, moon, shooting stars, fireflies, ground fog, dust motes) and its
  perceived-constant anchor algebra.
- `Compat/MaterialLoaderHeal.cs` (predates this saga; heals the game's own reveal loads).
- The `ApparanceDetailFocus` compat feature (predates the saga).
- This document, and the build-note history in `NetProtocol.cs` (127–131).
