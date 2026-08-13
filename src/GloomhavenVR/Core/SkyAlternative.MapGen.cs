using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// GAME-GENERATED ROOM GEOMETRY for <see cref="SkyAlternative"/> — Apparance micro-generation
/// (<c>.planning/game-env-assets.md</c> approach B, de-risked by the retired GameEnvProbe whose
/// proven load/style/mute/focus sequence lives on here).
///
/// THE RULING (user, 2026-08-12, verbatim): "Ich WILL garnicht das die Umgebung im Menu rendert -
/// sondern nur im Szenario so wie es die Default originale Umgebung auch macht. Leg daher mit der
/// Umsetzung los von den zwei neuen Umgebungen. Lösche die alten assets und räum da wieder auf."
/// Scenario-only scope makes approach B viable BY CONSTRUCTION: inside a scenario the
/// ApparanceEngine is alive (it arrives with the ProcGen/Game scenes — the fact
/// <see cref="ApparanceDetailFocus"/> hard-verified), so no menu-time engine bootstrap is needed.
///
/// THE ModBuild-127 REPORT (user, verbatim): "Die neuen Umgebung werden nicht richtig gerendert
/// (siehe umgebung1.png und umgebung2.png), man sieht einen Boden ohne Texturen, aber nur
/// schwarze Wände (bzw gar keine)." FOUR ROOT CAUSES, all read from the decompiled game code and
/// the hardware log, all neutralized below — do NOT re-simplify any of these away:
///
///  A. MISSING RESOURCE PACKETS (128 log 2516/5532: floor renderers literally named
///     'Red Cube ... StoneRooms.Floor.Tile'; the SwampNight doors were
///     'Red Cube ... Marsh.Door.Thick'). Apparance resolves asset descriptors through
///     category-prefixed resource lists that load ON DEMAND through Addressables
///     (ApparanceResourceTable.LookupResourceList → ApparanceResourceListLoader.LoadAsync,
///     decompiled). The RUNNING scenario had never needed the Dungeon/Forest packets, and a
///     request that terminally misses gets a null-Object fallback AssetInfo
///     (ApparanceResources.GenerateFallbackAssetInfo) which is placed as the engine's DEBUG
///     'Red Cube' (ApparanceResources.GetPrefab → GetDebugMissingObject) — whose own
///     MaterialLoader never completes, so it renders NOTHING. Worse, the fallback is also
///     cached BY NAME in the engine-global ApparanceResources.Objects list, where
///     HandleAssetRequest's by-name loop finds it BEFORE any table lookup: one failed run
///     POISONS every later run of the same session.
///     ⇒ Fix: the WARMUP phase below pre-loads the style's packets to completion before the
///     first request can exist, and heals prior poison with the game's own
///     RefreshResourceList(clear_unused: true) (the exact call ApparanceEngine.RefreshResources
///     makes on scene changes).
///
///  B. FREEZE-BY-DISABLE DESTROYED THE CONTENT (decompiled ApparanceEngine.EntitiesGameTick →
///     ApparanceEntity.GameTick → CheckEntity: when an entity component is NOT
///     (activeInHierarchy && enabled && IsPopulated) and still holds a native handle it runs
///     DestroyEntity() → Clear() → ClearObject destroys EVERY child of every Generated-Content
///     tier container). The old freeze disabled the ApparanceEntity components, so ONE ENGINE
///     TICK after placement the whole generated room was destroyed — what the user saw was the
///     map template's authored skeleton: untextured gray hulls, no walls. The class doc's old
///     claim "a disabled one just stops updating" was WRONG (it described the Frozen flag, not
///     enabled). ⇒ Fix: NEUTRALIZE instead of disable — empty each entity's
///     m_GenerationTiers/m_GenerationRoot/m_Instances (publicized) FIRST, so the engine-tick
///     DestroyEntity disposes only the native entity and its Clear/DestroyObject find nothing
///     to kill; the content becomes plain GameObjects the engine has forgotten. Frozen=true
///     alone was REJECTED: the native engine re-tiers against the viewpoint autonomously, and
///     a room AROUND the player would re-synthesize on every locomotion, un-layered.
///
///  C. THE ROOM'S LIGHTS WERE NEVER ON (decompiled DynamicAmbience: Start → InitLighting →
///     SetLightLevel(0) clones every child Light template at intensity 0 and INACTIVE; only
///     ProceduralScenario.UpdateAmbience blends a tile's ambience in — and ONLY for tiles in
///     its own mapTiles list, which our clone never joins). So the candle/moon rig authored
///     into the generated content sat dark forever; the walls had no light to receive: black.
///     ⇒ Fix: drive the game's own rig to full (SetLightLevel(1f), publicized) at finalize,
///     restrict every room light to the MOD LAYER ONLY (they must relight OUR room, never
///     restyle the scenario board — game lights are never touched), scale each light's RANGE
///     by the room's lossy scale (Unity light range does not follow transform scale; at rig
///     scale ~27 an unscaled candle range covers centimeters), and spawn a modest two-point
///     fallback rig if the content brought no lights at all. RenderSettings.ambient* is never
///     written (it is the scenario's global state).
///
///  D. MOD SWEEPS ADOPTED THE ROOM AS A LIVE MAP TILE (WallSegmentFade's gate machinery adopted
///     OUR staged room's doors — "the embedding wall stack-adopts onto this column and fades";
///     SceneRegistry.MapTiles enrols every ProceduralTileObserver.OnEnable).
///     A placed room whose walls the wall fader dissolves is "gar keine Wände" by itself.
///     ⇒ Fix: the instance is put on a mod-owned layer already AT INSTANTIATE and re-applied on
///     every build poll (WallSegmentFade's IsModObject exclusion keys on the MOD layer;
///     the 128 log proved adoption can still graze the sub-second window between polls, is
///     transient, and ends at finalize), and at finalize every ProceduralMapTile/
///     ProceduralWall/ProceduralProp/ProceduralStyle/ProceduralDoorway/UnityGameEditorDoorProp/
///     TilesOcclusionVolume component is DESTROYED — their own OnDisable/OnDestroy deregister
///     them from the game's caches (ObjectCacheService, ProceduralWall.m_WallCache) and
///     SceneRegistry prunes destroyed entries, so no mod or game system ever treats the placed
///     room as scenery to manage again. MaterialLoader components are deliberately KEPT —
///     their pending Addressables loads still have to assign materials, and MaterialLoaderHeal
///     supervises them (census: registered=True).
///
/// THE ModBuild-128 REPORT (user, verbatim, three findings — the 128 pipeline itself is PROVEN:
/// census Cellar 709 renderers/680 drawing, SwampNight 266, both survived T+3 s):
///
///  1. "Man sieht nun etwas aber super winzig, nach einer Zeit dann ist es dann plötzlich um
///     einen drum rum aber spawened erst in eine schwarte umgebung mit der Umgebung als
///     miniaturversion da drin neben dem spielfeld." ROOT CAUSE: the staging pose was a FIXED
///     −50 world units below the play space, but world units are diorama units — the 128 log's
///     placement lines show rig scale 50.93 (Cellar) and 137.56 (Swamp), so −50 wu read as
///     50/S REAL meters: 36 cm at the swamp zoom. The user literally watched the miniature
///     build beside the board. ⇒ Fix (SPAWN POSE below): the staging DEPTH scales with the
///     rig scale AND is pushed beyond the head camera's far plane
///     (max(50·max(1,S), far·1.25 + 500) — beyond the far plane nothing is rasterized at
///     all), and ADDITIONALLY the staged build is put on a hidden layer no active camera
///     renders when such a layer exists — runtime-VERIFIED against Camera.allCameras every
///     poll, because this user's own log proves the head camera follows the anchor's
///     0xFFFFFFFF mask (line 405: "anchor 'Main Camera' mask 0xFFFFFFFF → head 0xFFFFFFFF"),
///     i.e. on this rig NO layer is safe and the depth fallback is the load-bearing fix.
///     REJECTED: carving the hidden bit out of the head camera's mask — VRRigDriver's
///     TickHeadCullingMask re-asserts the mask from the anchor EVERY FRAME (it owns it);
///     a write from here would fight the owner. DELIBERATELY NOT DONE: deactivating objects
///     (CheckEntity destroys inactive entities — the 127 lesson) or disabling renderers
///     (census/settle reads them; MaterialLoaders re-enable renderers they manage).
///     The placement POP stays for now (task ruling): the FX shell already shows instantly.
///
///  2. "Mir gefallen die Umgebungen nicht, sie sind Rechtecking, haben keine Beleuchtung, die
///     assets klippen ineinander und es fühlt sich nicht wie ein interesannter Ort an. Statt
///     selber assets zusammenzufwürfen kannst du nicht zwei echte interesannte fertige Räume
///     aus dem Spiel nutzen die zu dem Thema passen?" ⇒ Fix: the assembled-template look is
///     replaced by REAL AUTHORED MAPS from the same mapsprocgen catalog the campaign
///     scenarios load ("Map " + EMapType, decompiled Choreographer.cs:14943): per-style
///     PREFERENCE LISTS with 'Map A' as the terminal entry (see AUTHORED MAPS below —
///     revised after 129, when the composite ADDRESSES failed while the assets provably
///     existed, and again after 130, which ruled ONE SINGLE ROOM — finding a: the 129/130
///     multi-room composites loaded the whole level). Authored per-tile styles are PRESERVED — the fill rule
///     writes an axis only when the prefab left it Inherit/Default (in vanilla the scenario
///     style lives on the ProcGen Maps ROOT and tiles inherit through
///     ProceduralStyle.GetBiome's parent walk, decompiled — our clone has no parent style, so
///     unfilled axes would collapse to Default, not to the scenario's look). Exception, task
///     ruling: SwampNight always writes Tone=ForestMoonlight — the star dome needs night even
///     if the authored tone was daylight; every other authored axis wins.
///
///  3. "In der Sumpfumgebung sind rote boxen zu sehen (Asset fehlt oder wird nicht richtig
///     geladen?)" — the 128 misses were StoneRooms.Floor.Tile x9 and the whole
///     Marsh.Floor.Tile*/Marsh.Door.Thick/Thin family, WITH their packets warmed and loaded
///     ("packets warmed 2/3, failed: none"). ROOT CAUSE (decompiled, load-bearing): a miss
///     inside a LOADED packet is TERMINAL — ApparanceResourceList.FindExternalAsset, when the
///     list's Category prefixes the requested name but no entry matches, MINTS a null-Object
///     placeholder entry into the list and RETURNS it, so HandleAssetRequest never consults
///     another table and the placement becomes the debug 'Red Cube'. No amount of extra
///     packet warming can change which packet a category resolves to (LookupResourceList maps
///     the descriptor's first dot-segment through the FIRST table that carries it). The minted
///     placeholder also persists in the loaded list for the whole session (engine-global
///     RefreshResourceList purges only ApparanceResources.Objects, not lists).
///     ⇒ Fix, three layers: (i) warmup tokens are now derived from the map's OWN effective
///     styles (authored + fills), and every warmed list is purged of null-Object placeholder
///     entries; (ii) at settle, a HEAL pass replaces each remaining 'Red Cube' IN PLACE with
///     donor art found by suffix in the loaded packets — the exact frame the real asset would
///     have filled is recoverable from the cube instance because placement scales the asset's
///     bounds into the procedure frame (decompiled ApparanceEntity placement: scale =
///     localScale·frameSize/boundsSize, position = origin + R·(−boundsMin)·scale/localScale),
///     and a matching alias entry is injected into the category's own list so every LATER run
///     of the session resolves through the game's own path; (iii) a missing piece with no
///     donor anywhere is REMOVED — the user's finding is the red box, and a small gap in an
///     ambience room beats a debug cube. The census prints the NAMES of everything healed or
///     removed; goal state is 0 fallback renderers at placement, by construction.
///
/// THE ModBuild-130 REPORT (user, verbatim — the round this file's current shape answers):
/// "Die Umgebung spawned so klein wie das Spielfeld selber! Das soll auf keinen Fall so
/// sein. Der ganze Sinn ist es IN der Umgebung zu sein. Daher siehst du noch andere
/// Probleme: a) Das ganze Level inklusive aller Räume wird geladen statt nur ein Raum - ein
/// Raum reicht in dem man ist. b) Der Raum inklusive aller Effekte (Fackeln, Feldermäuse,
/// Belichtung, etc.) soll geladen werden damit es athmosphärisch ist!. c) Der Boden ist
/// nicht zu sehen, man kann hindurch sehen. Das muss nochmal deutlich überarbeitet werden.
/// Ich möchte einfach eine athmosphärische Spielumgebung in der man spielen kann - die zum
/// Styl des Spiels passt." FOUR revisions, each pinned to its evidence in his 130 log:
///
///  MAIN (the miniature): sizing is back to PERCEIVED meters and the room is seated around
///     the PLAYER — SkyAlternative's class doc (ANCHORING, fourth revision) owns that story;
///     this file's part is the NORMALIZATION MATH below. The 130 board-relative experiment
///     (2.75× board extent, floor at the board underside) is DELETED as a rejected
///     alternative: board extent is world-tiny at diorama zoom (his log line 551: rig scale
///     85.21, "placed map 5.8 m across" = a perceived ~7 cm tabletop miniature).
///  (a) ONE ROOM ONLY: the preference lists now name SINGLE-ROOM maps (AUTHORED MAPS,
///     130 revision below), and a map that still arrives with several room tiles is CULLED
///     to the tile containing the map's center before anything is measured or placed —
///     the whole-level load must never reach the player again.
///  (b) ATMOSPHERE STAYS ALIVE: the placed clone keeps its self-contained visual drivers
///     ticking — see COMPONENT PRESERVE-VS-DESTROY below.
///  (c) FLOOR INTEGRITY: floor pieces are NEVER removed — see FLOOR INTEGRITY below. (The
///     130 hole was OUR OWN removal fallback: line 552 "removed [StoneRooms.Floor.Tile]" —
///     the heal pass deleted floor tiles it found no donor for, because the donor match
///     demanded the exact variant suffix while the warmed Dungeon list only carried
///     'Floor.Tile#N' variants.) Also fixed here: the 130 main-room measurement read the
///     tile's authored BoxCollider (60.2) LARGER than the whole map's render bounds (35.0)
///     — colliders can exceed the art; measurement is now renderer-bounds per tile,
///     sanity-clamped to the map bounds.
///
/// AUTHORED MAPS (finding 2, REVISED after the ModBuild-129 run — THE GUESSING ENDED).
/// THE ModBuild-129 REPORT (user, verbatim): "Es ist immer noch kein real existierendes Level
/// sondern sieht aus wie ein wild zusammengefürter Raum der ein perfektes Rechteck ist. Statt
/// selber einen Raum mit assets zusammenzubauen nutze bitte einen zum Thema passenden echten
/// Raum aus einem Szenario/Level aus dem Spiel das athmosphärisch ist." ROOT CAUSE: the 129
/// build's composite loads FAILED on his install (his LogOutput.log 443 + 2787: "authored map
/// 'Map DDM'/'Map ABHM' failed to load from the Addressables catalog") and both styles fell
/// back to the single-room 'Map A' rectangle he had already rejected. THE EVIDENCE (his own
/// Player.log, same run, line 1490): the boot-time
/// 'LoadAlwaysLoadedAddressable always_loaded_standalone' dependency dump lists 113 'Map
/// *.prefab' assets INCLUDING 'Map DDM' and 'Map ABHM' — the ASSETS exist on his install and
/// are loaded at boot; what failed was the ADDRESS ROUTE: the full-path key
/// 'Assets/_AssetBundles/mapsprocgen/&lt;name&gt;.prefab' resolves for the singles A–N (the
/// same Player.log's always_loaded_base_high line spells those full paths out verbatim — and
/// 'Map A' loaded fine through it) but NOT for the composites on this catalog. Therefore:
///
///  - LOOKUP ORDER per candidate (LoadMapCandidate): (1) the game's ALWAYS-LOADED asset store
///    — AssetBundleManager._alwaysloadedHandles (publicized), searched by prefab name exactly
///    like the game's own TryGetAssetFromAlwaysLoadedHandles; synchronous, and PROVEN to hold
///    every base-game map on his install by the boot dump above. The handles are
///    session-lifetime (UnloadNotRequiredBundles clears _loadedHandles, never
///    _alwaysloadedHandles — decompiled AssetBundleManager.cs:225-247), so the reference
///    needs no mod-held handle. (2) the full-path Addressables key (the game's own
///    Choreographer.cs:14943 pattern) as the async fallback for installs whose store misses
///    the name but whose catalog carries the address.
///  - PREFERENCE LIST per style (<see cref="MapPreference"/>), walked in order; a candidate
///    that fails BOTH routes logs one warn and the next is tried; 'Map A' is the terminal
///    entry before the FX-shell-only degrade. Every listed name is VERIFIED present in his
///    boot dump (Player.log:1490): ABHM, GI, DDM, LML, A.
///  - MAP CATALOG census (belt and braces for other installs/DLC sets): the first activation
///    logs ONE line with every 'mapsprocgen' path key the Addressables catalog carries plus
///    every always-loaded 'Map *' prefab name — the next report convicts a wrong list from
///    facts, not guesses.
///  - CHOICES, 130 REVISION ("ein Raum reicht in dem man ist"): the lists now name
///    SINGLE-ROOM maps. His 130 MAP CATALOG census (his LogOutput.log line ~416) proves the
///    always-loaded store carries every 'Map DLC_SC&lt;scenario&gt;_RM&lt;room&gt;' prefab —
///    the DLC's PER-ROOM maps (RM = one revealed room of one scenario, the naming scheme
///    itself is the single-room guarantee) — plus the singles A–N. Cellar →
///    'Map DLC_SC02_RM01', 'Map DLC_SC04_RM01', 'Map DLC_SC06_RM01' (early DLC-campaign
///    rooms; the DLC arc opens INDOORS in town — cellars, house interiors, crypts — so the
///    early RM01 rooms are the enclosed-stone-room candidates; scenario-theme attribution is
///    INFERRED from the campaign arc, the assets' presence is VERIFIED in his census).
///    SwampNight → 'Map DLC_SC03_RM01' (scenario 3 is the DLC's ship/docks episode — water
///    at night, the closest authored fit), 'Map DLC_SC16_RM01', 'Map DLC_SC20_RM01'
///    (mid/late-arc rooms, flooded-quarter episodes — INFERRED). Theme risk is bounded
///    either way: authored axes are kept, unset axes are filled with the style vocabulary,
///    SwampNight forces the night tone, and the CULL (below) guarantees one room regardless.
///  - The 129 rejection of DLC_SC* members ("present only with the DLC") is OVERRULED by
///    the 130 evidence — his install carries them all — but its concern still holds for
///    base-game installs: those walk the list to the terminal 'Map A' (one warn per
///    candidate, once per session) and still get a room. 'Map A' stays terminal precisely
///    for them. STILL REJECTED: multi-tile composites (ABHM/DDM/GI/LML — the 130 finding a:
///    the whole level loaded), single-letter maps as PRIMARY (plain rectangles, the 129
///    finding), Solo_* (solo-content gated), numeric finales (D21/C82/M521…, boss arenas).
///  - CULL TO ONE ROOM (belt and braces, finding a): if the resolved map still arrives with
///    MORE than one ProceduralMapTile, FinalizeBuild destroys every tile subtree except the
///    one whose footprint contains the map's render-bounds center (nearest-center fallback)
///    BEFORE measuring or placing anything — the whole-level load can never reach the
///    player, whatever the preference list resolved to.
///
/// THE SEQUENCE (once per activation, all phases ticked from <see cref="SkyAlternative.Tick"/>):
///
///  1. LOAD the style's authored map: walk the preference list, per candidate the
///     always-loaded store first (synchronous), then a mod-held Addressables handle (own
///     handle, NOT AssetBundleManager's wrapper, whose WaitForCompletion would stall the
///     frame). Resolved prefabs are cached per style for the session.
///  2. WARMUP (root cause A): purge poisoned null-Object fallback resources (engine-global
///     via the game's own RefreshResourceList(clear_unused: true), per-list via a targeted
///     placeholder purge — finding 3), then kick the resource-list packets for the map's
///     EFFECTIVE style vocabulary (authored axes + our fills, read from the prefab) through
///     the game's own loader and WAIT until each is loaded (or failed, or the deadline
///     passes) BEFORE anything can request assets. A packet the game is already loading is
///     polled via LoadCheck; our own completions notify the engine exactly like the game's
///     HandleAsyncResourceListLoad does.
///  3. BUILD at a STAGING pose: instantiate under a mod root in the ProcGen scene (like the
///     game's own maps — parked at (head.x, −D, head.z) with D = max(50·max(1,S),
///     far·1.25 + 500): beyond the far plane AND ≥ 50 real meters down at any rig scale,
///     finding 1), put it on the BUILD layer immediately (root cause D; the hidden staging
///     layer when verified safe, the mod layer otherwise). Mute every
///     <c>StaticAmbience</c>/<c>DynamicAmbience</c> on the instance FIRST (they would
///     overwrite the scenario's own skybox/ambient/fog if any game system blended them in),
///     FILL the style axes the prefab left unset (finding 2 — authored axes are kept), apply
///     via <c>ForceValidate()</c> (the level editor's runtime apply path,
///     LevelEditorApparancePanel.cs:43), and populate the walls (the minimal
///     ProceduralScenario.SetupWalls: <c>IsPopulated = true</c> on each wall entity — corner
///     data stays default, which costs join quality only).
///  4. DETAIL FOCUS — BORROWED, ALWAYS (the documented decision): Apparance synthesis detail is
///     distance-scaled from the engine viewpoint, and the staging pose sits far below it —
///     beyond the proven detail range (ApparanceDetailFocus round 3: a tile re-synthesized with
///     the viewpoint ~29 wu away came out as coarse preview-grade scraps). So generation would
///     NOT complete usably at staging distance; the engine focus is borrowed for the build via
///     the probe's seam — <see cref="ApparanceDetailFocus.Uninstall"/> (its driver re-asserts
///     per frame, the two must never fight), override <c>EnableDetailFocus/DetailFocus</c> onto
///     a focus object at the staging map, and return both the instant the build settles or
///     fails. The scenario loses its gaze-driven focus only for the build window (≤ 30 s, once
///     per activation); a reveal during that window synthesizes against the scenario's own
///     authored viewpoint for those seconds — the pre-mod behaviour, transient and logged.
///  5. POLL (0.5 s cadence) until the Generated-Content renderer census is non-zero, no
///     <c>ApparanceEntity.IsBusy</c> remains and the census is stable across two consecutive
///     polls. Remaining 'Red Cube' fallbacks at that point are TERMINAL (finding 3) and go
///     through the HEAL pass (donor replacement / removal, alias injection), then the census
///     re-stabilizes fallback-free and the room finalizes. Timeout with NO content → one-shot
///     warn, staging cleaned up, focus returned, and the style degrades to the FX shell alone
///     (<see cref="GenPhase.Failed"/> — no retry loop; a style re-select or scenario re-entry
///     starts fresh).
///  6. FINALIZE + PLACE: sweep any last fallbacks (floors clone-patched, never deleted),
///     CULL to one room tile (130 finding a), force full tile visibility
///     (ProceduralMapTile.ApplyVisibility — the prefab's serialized visibility state is
///     asset data this mod cannot read, so All is forced rather than assumed), measure
///     (renderer bounds, clamped — the 130 measurement fix), run the FLOOR GATE (finding c),
///     light the room (root cause C, rig owners preserved disabled), neutralize the
///     Apparance machinery (root causes B + D), destroy the scenario-logic components while
///     PRESERVING the visual drivers (finding b — see COMPONENT PRESERVE-VS-DESTROY),
///     normalize the particles, strip all colliders (non-interactive by ruling), re-apply
///     the mod layer, then reparent the instance into the room branch with the normalization
///     below, re-base the flicker caches, drop the staging root, and log the ROOM CENSUS
///     diagnostic block (one line at placement, one T+3 s survival proof).
///
/// NORMALIZATION MATH (130 revision — LIFE-SIZE): the map is authored in world/diorama
/// units and normalized into FRAME units — the (post-cull) room tile, measured by its
/// RENDERER bounds at staging scale 1 and sanity-clamped to the whole map's bounds (the 130
/// measurement bug: the authored BoxCollider read 60.2 on a 35.0-unit map), becomes
/// <see cref="TargetMainRoomMeters"/> frame units across, capped so the WHOLE remaining map
/// never exceeds <see cref="MaxTotalRoomMeters"/> (n = min(11/mainExtent, 24/totalExtent)).
/// The FRAME is the room branch (SkyAlternative class doc ANCHORING, fourth revision):
/// seated at world scale = the live rig scale S, so the room's world size is
/// TargetMainRoomMeters × S wu = ~11 PERCEIVED meters — a life-size room at any zoom, by
/// construction. REJECTED (the 130 miniature): any board-derived world scale — the board's
/// extent is world-tiny at diorama zoom (his log: 5.8 wu at rig scale 85.21 = ~7 cm).
/// Placement: the room tile's center goes to the frame origin — the PLAYER's floor point
/// (SkyAlternative.PlaceBothBranches) — and the floor top goes to frame-local y = 0, which
/// the room branch pins at the REAL floor under the player's feet: floor height = the
/// average ProceduralMapTile height (figures stand at tile level), bounds-min fallback.
///
/// COMPONENT PRESERVE-VS-DESTROY (130 finding b — "inklusive aller Effekte ... damit es
/// athmosphärisch ist"). The rule: DESTROY exactly the scenario-logic and registry
/// machinery the 129 run PROVED safe to destroy; PRESERVE every self-contained visual
/// driver. The table, with the why per row:
///  DESTROYED — ProceduralMapTile/ProceduralWall/ProceduralProp/ProceduralDoorway/
///     ProceduralStyle/UnityGameEditorDoorProp/TilesOcclusionVolume (root cause D: their
///     OnDisable/OnDestroy deregister them from the game's caches; scenario logic, zero
///     visual behaviour of their own — 129-proven), ApparanceEntity neutralize+disable
///     (root cause B), StaticAmbience (NOT self-contained: Apply() writes
///     RenderSettings.skybox/ambientMode and the CAMERA's post-processing profile —
///     global state a preserved component must never be able to touch), all Colliders
///     (non-interactive by ruling).
///  PRESERVED — ParticleSystem (torch flames, dust, drips: self-contained Shuriken;
///     normalized to Hierarchy scaling + Local simulation space at finalize so they scale
///     with the room and ride re-seats), Animator/Animation (animated props, critters:
///     self-contained clips in local space), LightFlicker (the torch/candle flicker driver,
///     decompiled ThirdParty/LightFlicker.cs: pure Perlin writes to its OWN light/transform
///     — safe, BUT it caches initialPosition/initialScale in WORLD space at Start, which
///     runs at the STAGING pose; the TickGuard-style guard here is RebaseFlickerDrivers:
///     after every room placement/re-seat the component is swapped for a fresh copy with
///     the same public tuning, so its caches re-Start at the placed pose — without that,
///     adjustLocation flames would teleport back toward the staging depth), MaterialLoader
///     (pending Addressables material assigns, healer-supervised — unchanged),
///     DynamicAmbience (the room's light rig OWNER: SetLightLevel(1f) is driven at
///     finalize, then the component is left DISABLED, not destroyed — it has no Update and
///     ticks nothing itself; keeping it keeps the rig's template/instance bookkeeping
///     alive, disabling guarantees no scenario blend flow can ever drive our clone's
///     ambient/fog globals).
///
/// FLOOR INTEGRITY (130 finding c — "Der Boden ist nicht zu sehen"). Floor pieces may NEVER
/// be removed; four layers:
///  (i) WHERE FLOORS LIVE: when the heal pass meets any miss, a one-shot RESOURCE TOPOLOGY
///     census logs the loader's COMPLETE packet inventory (ApparanceResourceListLoader's
///     serialized _references names — decompiled: the full set of packets that exist),
///     which are loaded, the missing category's resolved mapping, and every loaded entry
///     whose name carries the missing piece's family token — the next hardware log answers
///     "which packet actually serves StoneRooms.Floor.*" from facts.
///  (ii) DONORS FROM ANY WARMED FLOOR SET: the donor match now strips the '#variant' from
///     the DONOR side too (the 130 bug: missing 'StoneRooms.Floor.Tile' found no donor
///     because the warmed Dungeon list only carries 'Floor.Tile#N' variants — exact-suffix
///     matching removed the floor), and the donor scope is every packet loaded in the
///     session (loader._loadedPackets), not just the ones this run warmed.
///  (iii) LAST RESORT — CLONE-PATCH, NEVER DELETE: a floor miss with no donor anywhere is
///     patched by cloning the nearest healthy floor piece instance into the hole. Removal
///     stays the fallback ONLY for non-floor pieces (a small prop gap beats a debug cube;
///     a floor gap is finding c).
///  (iv) FLOOR GATE: after healing, a grid of test points over the room's inner footprint
///     is checked against the floor-band renderer bounds (colliders are stripped — bounds
///     tests, no physics). An uncovered cell first tries re-enabling a reveal-disabled
///     authored hex floor renderer there (census: 'hex (5)'[no-loader] etc. — re-enabled
///     only when every material slot is present and no MaterialLoader manages it), else
///     clone-patches; one line logs the result either way.
///
/// LIFECYCLE: <see cref="CancelMapGen"/> runs on every deactivation (scenario end/leave, style
/// change, MR on, VR stop) — it returns the borrowed focus, destroys the staging root, and
/// resets the phase so the next activation regenerates. A PLACED room is a child of the frame
/// root and dies with it in <c>Deactivate</c>. Scenario unload is additionally covered by the
/// scope gate itself (no scenario ⇒ Deactivate before the ProcGen teardown can matter), and a
/// mid-scenario external kill of the instance (fake-null) is detected in the Built tick: warn
/// once, degrade to the FX shell. The cached prefab handles are released in
/// <see cref="ReleaseMapPrefab"/> (VR stop / hot reload).
///
/// COST: Idle/Failed/Built phases are one enum compare per frame (Built adds one fake-null
/// check and, once, the T+3 s census). All real work happens once per activation inside the
/// build window (the camera-mask verification allocates Camera.allCameras only on the 0.5 s
/// poll cadence, build window only). The room lights add one range write per light on the
/// rare scale-write events (<see cref="SyncRoomLightRanges"/>), zero steady-state.
/// MULTIPLAYER: local presentation only — the instance is mod-layer, never on the wire, and
/// the borrowed focus steers only LOCAL synthesis scheduling (peers run their own engines).
/// </summary>
internal static partial class SkyAlternative
{
    private const string ProcGenSceneName = "ProcGen";

    /// <summary>The exact Addressables folder the game builds in <c>LoadAssetFromBundle</c>
    /// ("misc_mapsprocgen", "Map ...", "mapsprocgen") — decompiled AssetBundleManager.cs:277-287.</summary>
    private const string MapPrefabFolder = "Assets/_AssetBundles/mapsprocgen/";

    /// <summary>Per-style map PREFERENCE LISTS, walked in order at runtime (class doc AUTHORED
    /// MAPS — the 130 revision: SINGLE-ROOM maps only; choices, evidence and rejections
    /// documented there). The last entry is the terminal 'Map A' (proven loadable AND
    /// generable by the 128/129 hardware runs, and the only guaranteed hit on a base-game
    /// install without the DLC room maps) before the FX-shell-only degrade; every name is
    /// verified present in the user's 130 MAP CATALOG census. Index = (int)SkyStyle.</summary>
    private static readonly string[][] MapPreference =
    {
        Array.Empty<string>(),   // Default — never generates
        // Cellar: single authored DLC-campaign rooms, early indoor arc (class doc CHOICES).
        new[] { "Map DLC_SC02_RM01", "Map DLC_SC04_RM01", "Map DLC_SC06_RM01", "Map A" },
        // SwampNight: ship/docks + flooded-arc rooms, night tone forced (class doc CHOICES).
        new[] { "Map DLC_SC03_RM01", "Map DLC_SC16_RM01", "Map DLC_SC20_RM01", "Map A" },
    };

    /// <summary>PERCEIVED meters the room reads across at every seat event (130 revision):
    /// the room tile's renderer extent is normalized to this many frame units and the room
    /// branch is seated at world scale = the rig scale, so frame units ARE perceived meters.
    /// 11 m = mid of the task-ruled 10-14 m walkable single-room band: generous enough to
    /// hold the table diorama and walk around it, small enough that the walls stay present
    /// as a ROOM (class doc NORMALIZATION MATH).</summary>
    private const float TargetMainRoomMeters = 11f;

    /// <summary>Cap on the whole REMAINING map's frame extent — post-cull this is nearly
    /// always moot (one tile), but a single oversized/odd-shaped tile still stays inside
    /// the FX shell / far-plane budget (class doc NORMALIZATION MATH).</summary>
    private const float MaxTotalRoomMeters = 24f;

    /// <summary>Per-phase timeout: the Addressables load, the packet warmup and the generation
    /// poll each get this long before the run degrades (warmup degrades to building anyway —
    /// the census then proves what was missing).</summary>
    private const float GenTimeoutSeconds = 30f;

    private const float GenPollIntervalSeconds = 0.5f;

    /// <summary>Delay for the one-shot post-placement survival census (root cause B: the
    /// pre-fix content was destroyed ONE engine tick after placement — 3 s is unambiguous).</summary>
    private const float BuiltCensusDelaySeconds = 3f;

    /// <summary>Maximum heal passes per run — the engine can re-tier mid-window and mint new
    /// cubes after a pass; more than this means something structural and the deadline path
    /// judges what is left.</summary>
    private const int MaxHealPasses = 3;

    private enum GenPhase { Idle, Loading, Warmup, Building, Built, Failed }

    private static GenPhase _genPhase = GenPhase.Idle;
    private static SkyStyle _genStyle = SkyStyle.Default; // the style the current gen run is for

    // Session-cached map prefabs, indexed by (int)SkyStyle (class doc step 1). A prefab either
    // comes from the game's always-loaded store (no handle of ours — the game's own
    // session-lifetime handle owns it) or from a mod-held Addressables handle (_mapHandlesHeld).
    private static readonly AsyncOperationHandle<GameObject>[] _mapHandles =
        new AsyncOperationHandle<GameObject>[3];
    private static readonly bool[] _mapHandlesHeld = new bool[3];
    private static readonly GameObject?[] _mapPrefabs = new GameObject?[3];
    private static readonly int[] _mapPrefIndex = new int[3]; // preference-list walk position
    private static string _mapName = "";  // the map the current run loads/loaded (for logs)
    private static bool _mapLoadWarned;   // one-shot: Addressables cannot deliver ANY map
    private static bool _catalogCensusDone; // one-shot MAP CATALOG census line (class doc)

    private static GameObject? _stagingRoot;  // in the ProcGen scene, far below the play space
    private static GameObject? _mapInstance;  // the map clone (staged, then frame child)
    private static GameObject? _genFocusGo;   // borrowed engine focus target at the staging map

    private static ApparanceEngine? _focusEngine; // engine we overrode (restore exactly this one)
    private static bool _origEnableFocus;
    private static GameObject? _origFocus;
    private static bool _borrowedGazeDriver;

    private static float _genDeadline;
    private static float _nextGenPoll;
    private static int _lastRendererCount = -1;
    private static bool _genFailWarned;   // one-shot per session: generation timed out/failed
    private static bool _genDiedWarned;   // one-shot per session: a placed room was killed externally

    // HIDDEN STAGING LAYER (finding 1): resolved once per session (first unnamed layer scanning
    // 31→8 that is NOT the mod layer), then runtime-VERIFIED against every active camera's
    // culling mask at build start and on every poll — this user's own log proves the head
    // camera can run mask 0xFFFFFFFF, in which case NO layer is safe and the scaled staging
    // depth is the (sufficient) fix. −2 = not resolved yet, −1 = no candidate exists.
    private static int _stagingLayer = -2;
    private static bool _stagingLayerSafe;      // verified this build window
    private static bool _stagingLayerWarned;    // one-shot per run: safety lost mid-build

    // WARMUP bookkeeping (root cause A). Paths are the loader's own asset paths; a pending
    // entry either completes through our callback or (when the game kicked the same load
    // first) through the LoadCheck poll. Failed paths are kept for the census. (The donor
    // search no longer tracks warmed paths itself — it walks the loader's own
    // _loadedPackets store, FLOOR INTEGRITY ii.)
    private static bool _warmupKickDone;
    private static int _warmupRequested;
    private static readonly List<string> _warmupPending = new();
    private static readonly List<string> _warmupFailed = new();
    private static string[] _warmupTokens = Array.Empty<string>();

    // HEAL bookkeeping (finding 3): what the heal pass did, for the census.
    private static int _healPasses;
    private static readonly List<string> _healedNames = new();  // "missing←donor"
    private static readonly List<string> _removedNames = new(); // no donor anywhere (non-floor only)
    private static readonly List<string> _floorPatchedNames = new(); // FLOOR INTEGRITY iii: clone-patched
    private static bool _topologyCensusDone; // one-shot per run: RESOURCE TOPOLOGY on first miss

    // ROOM LIGHTS (root cause C): every Light under the placed instance with its AUTHORED
    // range — Unity light range does not follow transform scale, so the range is re-derived
    // as (authored × instance lossyScale) on placement/re-seat writes (SyncRoomLightRanges).
    private static readonly List<Light> _roomLights = new();
    private static readonly List<float> _roomLightRanges = new();

    /// <summary>The placed map's whole extent in FRAME units (totalExtent × norm), recorded at
    /// finalize — <see cref="MinFarWorldUnits"/> multiplies it by the room branch's live scale
    /// for the far-plane budget (the room is world-fixed, so zoomed-in it can out-distance the
    /// rig-scaled sky dome). 0 while no room is placed.</summary>
    private static float _roomFrameExtent;

    // One-shot post-placement survival census (class doc COST).
    private static float _builtCensusTime;
    private static bool _builtCensusDone;

    /// <summary>
    /// Per-frame generation driver, called from <see cref="Tick"/> while a non-Default style is
    /// active in a scenario. <paramref name="frame"/> is the WORLD-FIXED room-branch root the finished
    /// room is placed into.
    /// </summary>
    private static void TickMapGen(SkyStyle style, Transform frame)
    {
        // A style switch mid-generation restarts the run (Deactivate already destroyed the old
        // placed room; a staged half-build for the wrong style is cancelled here).
        if (_genStyle != style && _genPhase != GenPhase.Idle)
            CancelMapGen("style changed mid-generation");

        switch (_genPhase)
        {
            case GenPhase.Idle:
                BeginMapLoad(style);
                break;
            case GenPhase.Loading:
                TickMapLoad(style);
                break;
            case GenPhase.Warmup:
                TickWarmup();
                break;
            case GenPhase.Building:
                TickBuilding(frame);
                break;
            case GenPhase.Built:
                // Fake-null: the game (or a scene teardown race) destroyed our placed room —
                // degrade to the FX shell instead of leaving a half-broken style (class doc
                // LIFECYCLE). Deliberately no auto-rebuild: a system that kills the room once
                // would kill it again.
                if (_mapInstance == null)
                {
                    _mapInstance = null;
                    _genPhase = GenPhase.Failed;
                    if (!_genDiedWarned)
                    {
                        _genDiedWarned = true;
                        VRLog.Warn("Core", "Sky alternative: the placed game-generated room was destroyed " +
                                           "externally (scene teardown race?) — continuing with the FX shell " +
                                           "only. Re-select the style to regenerate.");
                    }
                }
                else if (!_builtCensusDone && Time.realtimeSinceStartup >= _builtCensusTime)
                {
                    // ROOT CAUSE B PROOF: the pre-fix build lost its generated content to
                    // ApparanceEntity.CheckEntity → DestroyEntity → Clear ONE engine tick
                    // after placement. This line makes survival (or a regression) a fact in
                    // the next hardware log instead of an inference from screenshots.
                    _builtCensusDone = true;
                    int total = 0, enabled = 0, fallbacks = 0;
                    foreach (Renderer r in _mapInstance.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null)
                            continue;
                        total++;
                        if (r.enabled && r.gameObject.activeInHierarchy)
                            enabled++;
                        if (IsFallbackRenderer(r))
                            fallbacks++;
                    }
                    VRLog.Info("Core", $"Sky alternative ROOM CENSUS T+{BuiltCensusDelaySeconds:F0}s: " +
                                       $"{total} renderer(s) still present ({enabled} enabled/active, " +
                                       $"{fallbacks} fallback(s)) — the placed room survived the engine " +
                                       "tick after placement.");
                }
                break;
            case GenPhase.Failed:
                break; // FX shell only, until re-activation
        }
    }

    private static void BeginMapLoad(SkyStyle style)
    {
        _genStyle = style;
        int i = (int)style;
        if (i <= 0 || i >= _mapPrefabs.Length)
        {
            _genPhase = GenPhase.Failed;
            return;
        }
        if (_mapPrefabs[i] != null)
        {
            _mapName = _mapPrefabs[i]!.name; // session cache — the prefab carries its map name
            BeginWarmup(style);
            return;
        }
        if (_mapLoadWarned)
        {
            _genPhase = GenPhase.Failed; // no route delivered ANY map this session
            return;
        }
        LogMapCatalogCensusOnce();
        TryNextCandidate(i);
    }

    /// <summary>Walk the style's preference list from the current position (class doc AUTHORED
    /// MAPS, 129 revision). Per candidate: the game's always-loaded store first (synchronous —
    /// the route his boot dump PROVES holds every base-game map), then the full-path
    /// Addressables key (async; TickMapLoad advances the walk when it fails). List
    /// exhausted → the run degrades to the FX shell.</summary>
    private static void TryNextCandidate(int styleIndex)
    {
        string[] prefs = MapPreference[styleIndex];
        if (_mapPrefIndex[styleIndex] < prefs.Length)
        {
            _mapName = prefs[_mapPrefIndex[styleIndex]];

            GameObject? preloaded = TryGetMapFromAlwaysLoaded(_mapName);
            if (preloaded != null)
            {
                _mapPrefabs[styleIndex] = preloaded; // the game's session-lifetime always-loaded
                                                     // handle owns it — no mod handle to hold
                VRLog.Info("Core", $"Sky alternative: authored map '{_mapName}' resolved from the game's " +
                                   $"always-loaded asset store for {_genStyle} (the boot-proven route; " +
                                   "no Addressables key needed).");
                BeginWarmup(_genStyle);
                return;
            }
            StartMapLoad(styleIndex, _mapName);
            return;
        }
        FailGeneration($"no {_genStyle} map could be resolved — the whole preference list " +
                       $"[{string.Join(", ", prefs)}] failed the always-loaded store AND the " +
                       "Addressables catalog (see the MAP CATALOG census line for what this " +
                       "install actually carries)");
        _mapLoadWarned = true;
    }

    /// <summary>Search the game's always-loaded Addressables handles for a prefab by name —
    /// the game's own (dead-code) TryGetAssetFromAlwaysLoadedHandles logic against the
    /// publicized <c>AssetBundleManager._alwaysloadedHandles</c>. His Player.log:1490 proves
    /// this store holds all 113 base-game 'Map *' prefabs at boot; the handles are never
    /// released mid-session (class doc AUTHORED MAPS).</summary>
    private static GameObject? TryGetMapFromAlwaysLoaded(string mapName)
    {
        try
        {
            AssetBundleManager? abm = AssetBundleManager.Instance;
            var handles = abm != null ? abm._alwaysloadedHandles : null;
            if (handles == null)
                return null;
            foreach (var kv in handles)
            {
                AsyncOperationHandle<IList<UnityEngine.Object>> h = kv.Value;
                if (!h.IsValid() || !h.IsDone || h.Status != AsyncOperationStatus.Succeeded
                    || h.Result == null)
                    continue;
                foreach (UnityEngine.Object o in h.Result)
                {
                    if (o is GameObject go && string.Equals(go.name, mapName, StringComparison.Ordinal))
                        return go;
                }
            }
        }
        catch { /* store mid-teardown — the Addressables route still runs */ }
        return null;
    }

    /// <summary>ONE line, first activation (class doc AUTHORED MAPS): what map keys/prefabs
    /// THIS install actually carries — the Addressables catalog's 'mapsprocgen' path keys and
    /// the always-loaded store's 'Map *' prefab names — so the next hardware log settles any
    /// preference-list debate from facts.</summary>
    private static void LogMapCatalogCensusOnce()
    {
        if (_catalogCensusDone)
            return;
        _catalogCensusDone = true;
        try
        {
            var pathKeys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var locator in Addressables.ResourceLocators)
            {
                if (locator?.Keys == null)
                    continue;
                foreach (object k in locator.Keys)
                {
                    if (k is string s && s.Contains("mapsprocgen"))
                        pathKeys.Add(s.Replace(MapPrefabFolder, "").Replace(".prefab", ""));
                }
            }
            var storeNames = new SortedSet<string>(StringComparer.Ordinal);
            AssetBundleManager? abm = AssetBundleManager.Instance;
            var handles = abm != null ? abm._alwaysloadedHandles : null;
            if (handles != null)
            {
                foreach (var kv in handles)
                {
                    AsyncOperationHandle<IList<UnityEngine.Object>> h = kv.Value;
                    if (!h.IsValid() || !h.IsDone || h.Status != AsyncOperationStatus.Succeeded
                        || h.Result == null)
                        continue;
                    foreach (UnityEngine.Object o in h.Result)
                    {
                        if (o is GameObject go && go.name.StartsWith("Map ", StringComparison.Ordinal))
                            storeNames.Add(go.name.Substring(4));
                    }
                }
            }
            VRLog.Info("Core", $"Sky alternative MAP CATALOG census: {pathKeys.Count} 'mapsprocgen' " +
                               $"Addressables path key(s) [{string.Join(", ", pathKeys)}]; " +
                               $"{storeNames.Count} always-loaded 'Map *' prefab(s) " +
                               $"[{string.Join(", ", storeNames)}].");
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative MAP CATALOG census failed ({e.GetType().Name}: " +
                               $"{e.Message}) — diagnostics only, the load walk continues.");
        }
    }

    private static void StartMapLoad(int styleIndex, string mapName)
    {
        try
        {
            _mapHandles[styleIndex] = Addressables.LoadAssetAsync<GameObject>(
                MapPrefabFolder + mapName + ".prefab");
            _mapHandlesHeld[styleIndex] = true;
            _genPhase = GenPhase.Loading;
            _genDeadline = Time.realtimeSinceStartup + GenTimeoutSeconds;
        }
        catch (Exception e)
        {
            FailGeneration($"Addressables.LoadAssetAsync threw ({e.GetType().Name}: {e.Message})");
        }
    }

    private static void TickMapLoad(SkyStyle style)
    {
        int i = (int)style;
        if (!_mapHandles[i].IsDone)
        {
            if (Time.realtimeSinceStartup > _genDeadline)
                FailGeneration($"'{_mapName}' Addressables load timed out ({GenTimeoutSeconds:F0} s)");
            return;
        }
        if (_mapHandles[i].Status != AsyncOperationStatus.Succeeded || _mapHandles[i].Result == null)
        {
            ReleaseMapHandle(i);
            // PREFERENCE-LIST WALK (class doc AUTHORED MAPS, 129 revision): this candidate
            // failed BOTH routes (store + key) — one warn, next entry. The walk position is
            // per-session: a name this install cannot deliver is never retried.
            string failedName = _mapName;
            _mapPrefIndex[i]++;
            string[] prefs = MapPreference[i];
            string next = _mapPrefIndex[i] < prefs.Length ? $"trying '{prefs[_mapPrefIndex[i]]}'" : "list exhausted";
            VRLog.Warn("Core", $"Sky alternative: authored map '{failedName}' failed to load — not in the " +
                               $"always-loaded store and no Addressables key delivered it for {style}; {next}.");
            TryNextCandidate(i);
            return;
        }
        _mapPrefabs[i] = _mapHandles[i].Result; // handle stays held for the session — the prefab
                                                // is a reference into Addressables' loaded
                                                // bundle, not a copy
        _genStyle = style;
        BeginWarmup(style);
    }

    // ---- WARMUP: resource packets (root cause A) ------------------------------------------------

    /// <summary>The mod's style vocabulary per style — used to FILL axes the authored map left
    /// Inherit/Default (finding 2: authored axes are kept; vocabulary per
    /// .planning/game-env-assets.md §3; ETone.Bioluminescence exists too, but ForestMoonlight
    /// is the mood the star dome wants — a moonlit night, not a glow cave).</summary>
    private static void StyleVocabulary(SkyStyle style,
        out ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome biome,
        out ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome subBiome,
        out ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETheme theme,
        out ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone tone)
    {
        if (style == SkyStyle.Cellar)
        {
            biome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome.Dungeon;
            subBiome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome.StoneRooms;
            theme = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETheme.Default;
            tone = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone.Candlelight;
        }
        else
        {
            biome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome.Forest;
            subBiome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome.Marsh;
            theme = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETheme.StillWaters;
            tone = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone.ForestMoonlight;
        }
    }

    /// <summary>An enum axis counts as AUTHORED when it names a concrete member — Inherit=0
    /// and Default=1 in every style enum (decompiled ScenarioPossibleRoom.cs), so ≥ 2 it is.</summary>
    private static bool IsAuthored(int enumValue) => enumValue >= 2;

    /// <summary>The resource-list CATEGORY tokens this run's map can request — derived from the
    /// map prefab's OWN effective styles (authored axes kept, unset axes as our vocabulary
    /// would fill them; finding 3: "derive the packets from the map's own requirements"). The
    /// category is a descriptor's first dot-segment and the tokens are enum member NAMES (the
    /// 128 misses were 'StoneRooms.Floor.Tile' and 'Marsh.Door.Thick' — SubBiome names);
    /// tokens without a table mapping are simply skipped. Vocabulary tokens come FIRST —
    /// that order is also the heal pass's donor preference.</summary>
    private static string[] EffectiveWarmupTokens(SkyStyle style, GameObject? prefab)
    {
        StyleVocabulary(style, out var vBiome, out var vSub, out var vTheme, out var vTone);
        var tokens = new List<string>(8);
        void Add(string name)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], name, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            tokens.Add(name);
        }
        // Ours first (donor preference order): the fill values a style-less map ends up with.
        if (IsAuthored((int)vSub)) Add(vSub.ToString());
        if (IsAuthored((int)vBiome)) Add(vBiome.ToString());
        if (IsAuthored((int)vTheme)) Add(vTheme.ToString());
        if (IsAuthored((int)vTone)) Add(vTone.ToString());
        // Then every authored axis the prefab brings itself (kept by the fill rule, so its
        // packets WILL be requested).
        if (prefab != null)
        {
            foreach (ProceduralStyle s in prefab.GetComponentsInChildren<ProceduralStyle>(true))
            {
                if (s == null)
                    continue;
                if (IsAuthored((int)s.SubBiome)) Add(s.SubBiome.ToString());
                if (IsAuthored((int)s.Biome)) Add(s.Biome.ToString());
                if (IsAuthored((int)s.Theme)) Add(s.Theme.ToString());
                if (IsAuthored((int)s.SubTheme)) Add(s.SubTheme.ToString());
                if (IsAuthored((int)s.Tone)) Add(s.Tone.ToString());
            }
        }
        return tokens.ToArray();
    }

    private static void BeginWarmup(SkyStyle style)
    {
        _genStyle = style;
        _warmupKickDone = false;
        _warmupRequested = 0;
        _warmupPending.Clear();
        _warmupFailed.Clear();
        _warmupTokens = EffectiveWarmupTokens(style, _mapPrefabs[(int)style]);
        _genPhase = GenPhase.Warmup;
        _genDeadline = Time.realtimeSinceStartup + GenTimeoutSeconds;
    }

    private static void TickWarmup()
    {
        ApparanceEngine? engine = ApparanceEngine.Instance;
        if (engine == null)
        {
            // Gate said "scenario alive", so this is a load-order frame — retry next tick
            // (the deadline keeps running; a scenario without an engine cannot build anyway).
            if (Time.realtimeSinceStartup > _genDeadline)
                FailGeneration("no ApparanceEngine appeared during the warmup window");
            return;
        }

        if (!_warmupKickDone)
        {
            _warmupKickDone = true;
            KickWarmup(engine);
            if (_warmupPending.Count == 0)
            {
                BeginBuild();
                return;
            }
        }

        // Poll pending packets: our own kicks complete through the callback, but a packet the
        // GAME was already loading (LoadAsync returned false) only becomes visible through
        // LoadCheck. Both paths converge here; a dictionary lookup per packet per frame.
        ApparanceResourceListLoader? loader = engine.GetComponent<ApparanceResourceListLoader>();
        if (loader != null)
        {
            for (int i = _warmupPending.Count - 1; i >= 0; i--)
            {
                try
                {
                    ApparanceResourceList? landed = loader.LoadCheck(_warmupPending[i]);
                    if (landed != null)
                    {
                        PurgeMintedPlaceholders(landed);
                        _warmupPending.RemoveAt(i);
                    }
                }
                catch { /* loader tearing down — the deadline path handles it */ }
            }
        }

        if (_warmupPending.Count == 0)
        {
            if (_warmupFailed.Count > 0)
                VRLog.Warn("Core", $"Sky alternative: {_warmupFailed.Count} resource packet(s) FAILED to load " +
                                   $"({string.Join(", ", _warmupFailed)}) — building anyway; missing art " +
                                   "resolves to the engine's fallback and the heal pass will name it.");
            BeginBuild();
            return;
        }

        if (Time.realtimeSinceStartup > _genDeadline)
        {
            VRLog.Warn("Core", $"Sky alternative: packet warmup timed out with {_warmupPending.Count} " +
                               $"packet(s) still loading ({string.Join(", ", _warmupPending)}) — building " +
                               "anyway; the heal pass and ROOM CENSUS will show any fallback residue.");
            BeginBuild();
        }
    }

    /// <summary>Heal prior poison and kick every style-relevant packet load. Read-from-source
    /// contract (class doc root cause A): a load completion — success OR null — is reported to
    /// the engine exactly like the game's own HandleAsyncResourceListLoad, so any dependent
    /// request the running scenario queues meanwhile resolves identically either way.</summary>
    private static void KickWarmup(ApparanceEngine engine)
    {
        ApparanceResourceListLoader? loader = engine.GetComponent<ApparanceResourceListLoader>();
        ApparanceResources? res = engine.Resources != null ? engine.Resources : engine.GetComponent<ApparanceResources>();
        if (loader == null || res == null)
        {
            VRLog.Warn("Core", "Sky alternative: engine has no resource-list loader/resources component — " +
                               "packet warmup skipped (generation may produce fallback 'Red Cube' art).");
            return;
        }

        // HEAL PRIOR POISON: a failed earlier run left null-Object fallback resources cached
        // BY NAME in the engine-global store (GenerateFallbackAssetInfo appends to Objects;
        // HandleAssetRequest's by-name loop returns them before any table lookup). The game's
        // own reset for exactly this store is RefreshResourceList(clear_unused: true) —
        // ApparanceEngine.RefreshResources calls it on every engine start/scene change, so
        // mid-scenario use only forces future requests to re-resolve (placed content keeps
        // its assigned materials). Per-LIST placeholder poison (finding 3: FindExternalAsset
        // mints into the loaded list, which this global purge does NOT reach) is purged in
        // PurgeMintedPlaceholders as each warmed list lands.
        try { res.RefreshResourceList(clear_unused: true); }
        catch (Exception e) { VRLog.Warn("Core", $"Sky alternative: resource-cache purge threw ({e.Message})."); }

        string[] tokens = _warmupTokens;
        int alreadyLoaded = 0;
        var kicked = new List<string>(4);
        try
        {
            foreach (ApparanceResourceTable table in res.Indirects)
            {
                if (table == null)
                    continue;
                var mappedCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ApparanceResourceTable.ResourceListMapping mapping in table.ResourceLists)
                {
                    if (string.IsNullOrEmpty(mapping.Category))
                        continue;
                    mappedCategories.Add(mapping.Category);
                    if (!TokenMatch(tokens, mapping.Category))
                        continue;
                    StartPacketLoad(loader, table.AssetPathPrefix + mapping.AssetPath,
                        kicked, ref alreadyLoaded);
                }
                if (table.autoFallback)
                {
                    // LookupResourceList resolves an unmapped category as prefix+category —
                    // mirror it so an auto-fallback table warms the same paths it would serve.
                    foreach (string t in tokens)
                    {
                        if (!mappedCategories.Contains(t))
                            StartPacketLoad(loader, table.AssetPathPrefix + t, kicked, ref alreadyLoaded);
                    }
                }
            }
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative: packet warmup enumeration threw ({e.GetType().Name}: " +
                               $"{e.Message}) — building with whatever is loaded.");
        }

        _warmupRequested = kicked.Count;
        VRLog.Info("Core", $"Sky alternative: warming {kicked.Count} resource packet(s) for {_genStyle} " +
                           $"map '{_mapName}', tokens [{string.Join(", ", tokens)}] " +
                           $"({(kicked.Count > 0 ? string.Join(", ", kicked) : "none needed")}; " +
                           $"{alreadyLoaded} already loaded) — generation starts when they land, so no " +
                           "asset request can fall back to the engine's 'Red Cube' placeholder.");
    }

    private static bool TokenMatch(string[] tokens, string category)
    {
        for (int i = 0; i < tokens.Length; i++)
        {
            if (string.Equals(tokens[i], category, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void StartPacketLoad(
        ApparanceResourceListLoader loader, string path, List<string> kicked, ref int alreadyLoaded)
    {
        try
        {
            ApparanceResourceList? loadedAlready = loader.LoadCheck(path);
            if (loadedAlready != null)
            {
                PurgeMintedPlaceholders(loadedAlready);
                alreadyLoaded++;
                return;
            }
            if (_warmupPending.Contains(path))
                return;
            _warmupPending.Add(path);
            kicked.Add(path);
            string captured = path;
            bool started = loader.LoadAsync(path, list =>
            {
                _warmupPending.Remove(captured);
                if (list == null && !_warmupFailed.Contains(captured))
                    _warmupFailed.Add(captured);
                if (list != null)
                    PurgeMintedPlaceholders(list);
                // Engine parity (game's HandleAsyncResourceListLoad): wake any dependent
                // request the running scenario queued for this packet meanwhile. A no-op when
                // nothing waits.
                try { ApparanceEngine.Instance?.NotifyAsyncResourceListCompleted(captured, list); }
                catch { /* engine tearing down */ }
            });
            // started == false: the GAME already has this packet in flight — keep it pending,
            // the LoadCheck poll in TickWarmup sees it land.
            _ = started;
        }
        catch (Exception e)
        {
            _warmupPending.Remove(path);
            if (!_warmupFailed.Contains(path))
                _warmupFailed.Add(path);
            VRLog.Warn("Core", $"Sky alternative: packet load '{path}' threw ({e.Message}).");
        }
    }

    /// <summary>Purge the null-Object placeholder entries a previous failed request MINTED into
    /// a loaded resource list (finding 3: FindExternalAsset appends them and they persist for
    /// the session; the game's own clear_unused semantics — RefreshResourceList removes exactly
    /// entries whose Object is null — applied to the list our warmup touches). Authored entries
    /// always carry an Object; our injected donor aliases do too, so both survive.</summary>
    private static void PurgeMintedPlaceholders(ApparanceResourceList list)
    {
        try
        {
            int n = list.Objects.RemoveAll(o => o != null && o.Object == null);
            if (n > 0)
                VRLog.Info("Core", $"Sky alternative: purged {n} minted null-Object placeholder(s) from " +
                                   $"resource list '{list.name}' (session poison from earlier terminal misses).");
        }
        catch { /* list tearing down */ }
    }

    // ---- BUILD ----------------------------------------------------------------------------------

    /// <summary>Resolve the hidden STAGING layer candidate once per session: the first unnamed
    /// layer scanning 31→8 that is not <see cref="VRLayers.ModLayer"/> (same convention as
    /// VRLayers — project layers are authored low). −1 when every layer is named/taken.</summary>
    private static int ResolveStagingLayer()
    {
        if (_stagingLayer != -2)
            return _stagingLayer;
        _stagingLayer = -1;
        for (int i = 31; i >= 8; i--)
        {
            if (i == VRLayers.ModLayer)
                continue;
            if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
            {
                _stagingLayer = i;
                break;
            }
        }
        return _stagingLayer;
    }

    /// <summary>TRUE only when NO active camera's culling mask contains the staging layer —
    /// verified against the live <c>Camera.allCameras</c> (head camera and every game/mirror
    /// camera included). On this user's rig the head camera follows the anchor's 0xFFFFFFFF
    /// mask (128 log line 405), so this is EXPECTED to return false there and the scaled
    /// staging depth carries the fix; the layer is defense-in-depth for rigs with the
    /// [Optimize] HeadMaskFromScenarioCamera narrowing on. Cameras that render only on manual
    /// Render() calls are not in allCameras — the depth covers those too.</summary>
    private static bool VerifyStagingLayerSafe(int layer, out string offender)
    {
        offender = "";
        if (layer < 0)
        {
            offender = "no unnamed layer free";
            return false;
        }
        int bit = 1 << layer;
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            Camera c = all[i];
            if (c != null && (c.cullingMask & bit) != 0)
            {
                offender = $"camera '{c.name}' mask 0x{c.cullingMask:X8}";
                return false;
            }
        }
        return true;
    }

    /// <summary>Put the STAGED build on the current build layer — the verified hidden staging
    /// layer while it is safe, the mod layer otherwise (the 128 discipline: WallSegmentFade's
    /// IsModObject exclusion keys on the mod layer). Re-applied on every poll because
    /// generated children arrive on their authored layers.</summary>
    private static void ApplyBuildLayer(GameObject go)
    {
        if (_stagingLayerSafe && _stagingLayer >= 0)
            SetLayerRecursive(go.transform, _stagingLayer);
        else
            VRLayers.Apply(go);
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    /// <summary>Class doc steps 3 + 4: instantiate at the staging pose, layer it, mute
    /// ambience, fill + validate styles, populate walls, borrow the engine detail focus.</summary>
    private static void BeginBuild()
    {
        ApparanceEngine? engine = ApparanceEngine.Instance;
        if (engine == null)
        {
            // Gate said "scenario alive", so this is a load-order frame — retry next tick.
            return;
        }
        GameObject? prefab = _mapPrefabs[(int)_genStyle];
        if (prefab == null)
        {
            FailGeneration("map prefab vanished between load and build");
            return;
        }

        // STAGING POSE (finding 1): horizontally at the player (the borrowed detail focus
        // lives there), vertically at a depth that is invisible AT ANY RIG SCALE — the 128
        // fixed −50 wu read as 50/S real meters (36 cm at the swamp zoom, the user watched
        // the miniature build). D = max(50·max(1,S), far·1.25 + 500): at least 50 REAL meters
        // down, and beyond the head camera's far plane, where nothing is rasterized at all.
        Camera? head = Rig.VRRigDriver.HeadCamera;
        Vector3 headPos = head != null ? head.transform.position : Vector3.zero;
        Transform? rig = Rig.VRRigDriver.RigRoot;
        float rigScale = rig != null ? rig.lossyScale.x : 1f;
        if (!(rigScale > 0f) || float.IsInfinity(rigScale))
            rigScale = 1f;
        float far = head != null ? head.farClipPlane : 0f;
        float depth = Mathf.Max(50f * Mathf.Max(1f, rigScale), far * 1.25f + 500f);
        Vector3 staging = new(headPos.x, headPos.y - depth, headPos.z);

        // HIDDEN STAGING LAYER (finding 1, defense-in-depth): verified now and on every poll.
        int stagingLayer = ResolveStagingLayer();
        _stagingLayerSafe = VerifyStagingLayerSafe(stagingLayer, out string offender);
        _stagingLayerWarned = false;

        _stagingRoot = new GameObject("GloomhavenVR.SkyAlternative.MapGenStaging");
        Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
        if (pg.IsValid() && pg.isLoaded)
        {
            // Live in the ProcGen scene like the game's own maps do while building.
            try { SceneManager.MoveGameObjectToScene(_stagingRoot, pg); }
            catch { /* stays in the active scene — the staging pose still hides it */ }
        }
        _stagingRoot.transform.position = staging;

        _mapInstance = UnityEngine.Object.Instantiate(prefab, _stagingRoot.transform);
        _mapInstance.name = "GloomhavenVR.SkyAlternative.Room." + _genStyle;
        _mapInstance.transform.localPosition = Vector3.zero;
        _mapInstance.transform.localRotation = Quaternion.identity;

        // LAYER FROM BIRTH (root cause D + finding 1): the game cameras must never composite
        // the staged build. Generated children arrive later on their authored layers —
        // TickBuilding re-applies this on every poll, FinalizeBuild moves everything to the
        // mod layer once.
        ApplyBuildLayer(_mapInstance);

        // MUTE ambience FIRST — StaticAmbience.Apply writes RenderSettings.skybox/ambient and
        // DynamicAmbience drives the scenario camera's DynamicFog; both would overwrite the
        // scenario's OWN sky/fog/ambient if any game system ever blended our instance in.
        // (The hardware log showed 0 muted here — the ambience components arrive WITH the
        // generated content; FinalizeBuild handles those, see root cause C.)
        int muted = 0;
        foreach (StaticAmbience amb in _mapInstance.GetComponentsInChildren<StaticAmbience>(true))
        {
            if (amb != null) { amb.enabled = false; muted++; }
        }
        foreach (DynamicAmbience amb in _mapInstance.GetComponentsInChildren<DynamicAmbience>(true))
        {
            if (amb != null) { amb.enabled = false; muted++; }
        }

        // STYLE FILL (finding 2) — authored axes are KEPT (the user asked for the game's real
        // rooms; overwriting an authored choice would throw away exactly the authorship he
        // wants), unset axes (Inherit=0/Default=1 — in vanilla they inherit the scenario style
        // through the ProcGen Maps root's ProceduralStyle, a parent our clone does not have)
        // are filled with our vocabulary, applied via ForceValidate (the level editor's
        // runtime apply path). Exception (task ruling): SwampNight always writes
        // Tone=ForestMoonlight — the star dome needs night.
        StyleVocabulary(_genStyle, out var vBiome, out var vSub, out var vTheme, out var vTone);
        bool forceTone = _genStyle == SkyStyle.SwampNight;
        int authoredKept = 0, filled = 0, tonesOverridden = 0;
        ProceduralStyle[] styles = _mapInstance.GetComponentsInChildren<ProceduralStyle>(true);
        for (int i = 0; i < styles.Length; i++)
        {
            ProceduralStyle s = styles[i];
            if (s == null)
                continue;
            if (IsAuthored((int)s.Biome)) authoredKept++;
            else if (IsAuthored((int)vBiome)) { s.Biome = vBiome; filled++; }
            if (IsAuthored((int)s.SubBiome)) authoredKept++;
            else if (IsAuthored((int)vSub)) { s.SubBiome = vSub; filled++; }
            if (IsAuthored((int)s.Theme)) authoredKept++;
            else if (IsAuthored((int)vTheme)) { s.Theme = vTheme; filled++; }
            if (forceTone)
            {
                if (IsAuthored((int)s.Tone) && s.Tone != vTone) tonesOverridden++;
                if (s.Tone != vTone) { s.Tone = vTone; filled++; }
                else authoredKept++;
            }
            else if (IsAuthored((int)s.Tone)) authoredKept++;
            else if (IsAuthored((int)vTone)) { s.Tone = vTone; filled++; }
        }
        for (int i = 0; i < styles.Length; i++)
        {
            try { styles[i]?.ForceValidate(); }
            catch { /* one broken style must not stop the rest */ }
        }

        // Walls (SetupWalls-minus-corners): the game populates wall entities after corner
        // analysis; without a scenario doing that for OUR instance, populate them directly —
        // default corner data costs join quality only.
        int walls = 0;
        foreach (ProceduralWall wall in _mapInstance.GetComponentsInChildren<ProceduralWall>(true))
        {
            if (wall == null)
                continue;
            ApparanceEntity we = wall.GetComponent<ApparanceEntity>();
            if (we != null)
            {
                we.IsPopulated = true;
                walls++;
            }
        }

        // BORROW the detail focus (class doc step 4 — the documented decision: staging sits
        // far below the viewpoint, beyond the proven detail range, so this is not optional).
        ApparanceDetailFocus.Uninstall();
        _borrowedGazeDriver = true;
        _genFocusGo = new GameObject("GloomhavenVR.SkyAlternative.MapGenFocus");
        _genFocusGo.transform.SetParent(_stagingRoot.transform, false);
        _focusEngine = engine;
        _origEnableFocus = engine.EnableDetailFocus;
        _origFocus = engine.DetailFocus;
        engine.EnableDetailFocus = true;
        engine.DetailFocus = _genFocusGo;

        VRLog.Info("Core", $"Sky alternative: generating the {_genStyle} room from the game's own art — " +
                           $"'{_mapName}' staged at {staging} (depth {depth:F0} wu = {depth / rigScale:F0} real m " +
                           $"below the player at rig scale {rigScale:F2}, beyond far plane {far:F0}; hidden " +
                           $"staging layer {(stagingLayer >= 0 ? stagingLayer.ToString() : "none")} " +
                           $"{(_stagingLayerSafe ? "VERIFIED safe — no active camera renders it" : $"NOT safe [{offender}] — mod layer + depth carry the hiding")}). " +
                           $"{styles.Length} ProceduralStyle(s): {authoredKept} authored axis value(s) kept, " +
                           $"{filled} filled with the {_genStyle} vocabulary" +
                           $"{(tonesOverridden > 0 ? $", {tonesOverridden} authored tone(s) overridden to ForestMoonlight for the star dome" : "")}; " +
                           $"{walls} wall entity(ies) populated, {muted} ambience component(s) muted, engine " +
                           $"detail focus borrowed for ≤ {GenTimeoutSeconds:F0} s.");

        _genPhase = GenPhase.Building;
        _healPasses = 0;
        _healedNames.Clear();
        _removedNames.Clear();
        _floorPatchedNames.Clear();
        _topologyCensusDone = false;
        float now = Time.realtimeSinceStartup;
        _genDeadline = now + GenTimeoutSeconds;
        _nextGenPoll = now;
        _lastRendererCount = -1;
    }

    /// <summary>Class doc step 5: poll until the census settles, heal any terminal 'Red Cube'
    /// fallbacks (finding 3), then finalize; timeout with content → heal + place anyway;
    /// timeout without content → FX-shell fallback.</summary>
    private static void TickBuilding(Transform frame)
    {
        if (_mapInstance == null)
        {
            FailGeneration("staged instance destroyed mid-build (scene teardown race)");
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (now < _nextGenPoll)
            return;
        _nextGenPoll = now + GenPollIntervalSeconds;

        // Staging-layer safety is re-verified every poll — a camera spawned mid-window (map
        // mirror, capture) that renders the layer demotes the build to the mod layer for the
        // rest of the window (the scaled depth keeps hiding it either way).
        if (_stagingLayerSafe && !VerifyStagingLayerSafe(_stagingLayer, out string offender))
        {
            _stagingLayerSafe = false;
            if (!_stagingLayerWarned)
            {
                _stagingLayerWarned = true;
                VRLog.Info("Core", $"Sky alternative: hidden staging layer {_stagingLayer} lost its safety " +
                                   $"mid-build ({offender}) — staged build demoted to the mod layer; the " +
                                   "scaled staging depth keeps it out of view.");
            }
        }

        // Children generated since the last poll are on their authored layers — re-apply the
        // build layer each poll (root cause D; a sub-second window between polls remains and
        // is accepted: WallSegmentFade's sweeps run on their own 1 s cadence).
        ApplyBuildLayer(_mapInstance);

        int busy = 0, renderers = 0, fallbacks = 0;
        foreach (ApparanceEntity e in _mapInstance.GetComponentsInChildren<ApparanceEntity>(true))
        {
            if (e == null)
                continue;
            try { if (e.IsBusy) busy++; }
            catch { /* engine tearing down mid-frame */ }
        }
        CensusGeneratedContent(_mapInstance.transform, ref renderers, ref fallbacks);

        // Settled = content exists, nothing building, the census matched the previous poll
        // (one extra confirmation beat so a between-entities gap can't pass as "done").
        // Remaining fallbacks at settle are TERMINAL (finding 3: the miss minted a null-Object
        // placeholder; the engine will never retry) — heal them NOW, then let the census
        // re-stabilize fallback-free before placing.
        bool settled = renderers > 0 && busy == 0 && renderers == _lastRendererCount;
        if (settled && fallbacks > 0 && _healPasses < MaxHealPasses)
        {
            HealFallbacks(_mapInstance);
            _lastRendererCount = -1; // the heal changed the census — demand fresh stability
            return;
        }
        if (settled && fallbacks == 0)
        {
            FinalizeBuild(frame, renderers);
            return;
        }
        _lastRendererCount = renderers;

        if (now > _genDeadline)
        {
            if (renderers > 0)
            {
                if (fallbacks > 0 && _healPasses < MaxHealPasses)
                    HealFallbacks(_mapInstance); // late but no red box is ever placed
                FinalizeBuild(frame, renderers);
            }
            else
                FailGeneration($"generation did not settle within {GenTimeoutSeconds:F0} s " +
                               $"(renderers={renderers}, busy entities={busy})");
        }
    }

    // ---- HEAL: terminal 'Red Cube' fallbacks (finding 3) ----------------------------------------

    /// <summary>Extract the missing asset name from a fallback instance's name — placement
    /// appends the resolver's error text to the instance name (decompiled ApparanceEntity:
    /// gameObject2.name += " (" + error_message + ")", error_message = "Unable to find
    /// prefab: " + name).</summary>
    private static string? ParseMissingName(string instanceName)
    {
        const string marker = "Unable to find prefab: ";
        int at = instanceName.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        int start = at + marker.Length;
        int end = instanceName.LastIndexOf(')');
        if (end <= start)
            end = instanceName.Length;
        return instanceName.Substring(start, end - start).Trim();
    }

    /// <summary>
    /// THE HEAL PASS (finding 3, "rote boxen"). Every remaining 'Red Cube' is terminal by
    /// construction (class doc), so each one is replaced IN PLACE with donor art found by
    /// suffix in the loaded resource lists — the cube instance's transform encodes the exact
    /// procedure frame the real asset would have filled (decompiled placement math, inverted
    /// in <see cref="ReplaceFallbackInstance"/>) — and a donor alias is injected into the
    /// category's own list so later runs of the session resolve through the game's own path.
    /// A missing piece with NO donor anywhere is REMOVED: the user's finding is the red box,
    /// and a small gap in a non-interactive ambience room beats a debug cube.
    /// REJECTED alternatives: warming more packets (cannot change which packet a category
    /// resolves to — LookupResourceList is deterministic per descriptor prefix) and rebuilding
    /// the entities after injection (a full re-synthesis mid-window for a handful of pieces).
    /// </summary>
    private static void HealFallbacks(GameObject inst)
    {
        _healPasses++;
        ApparanceEngine? engine = ApparanceEngine.Instance;
        AssetInfo? cubeInfo = null;
        try { cubeInfo = engine != null ? engine.GetDebugMissingObject() : null; }
        catch { /* engine tearing down */ }

        // Group the cube instances by missing name (9x StoneRooms.Floor.Tile in the 128 log).
        var byName = new Dictionary<string, List<GameObject>>(StringComparer.OrdinalIgnoreCase);
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !IsFallbackRenderer(r))
                continue;
            string? missing = ParseMissingName(r.name);
            if (missing == null || missing.Length == 0)
                missing = "unknown";
            if (!byName.TryGetValue(missing, out List<GameObject> list))
            {
                list = new List<GameObject>(4);
                byName[missing] = list;
            }
            if (!list.Contains(r.gameObject))
                list.Add(r.gameObject);
        }
        if (byName.Count == 0)
            return;
        LogResourceTopologyOnce(byName.Keys); // FLOOR INTEGRITY (i): where do these live?

        int healed = 0, removed = 0, floorPatched = 0;
        foreach (KeyValuePair<string, List<GameObject>> kv in byName)
        {
            GameObject? donor = FindDonor(kv.Key, out string donorName);
            bool replaced = false;
            bool isFloor = IsFloorPieceName(kv.Key);
            int patchedForName = 0;
            foreach (GameObject cube in kv.Value)
            {
                if (cube == null)
                    continue;
                if (donor != null && cubeInfo != null && cubeInfo.Object is GameObject cubeTemplate)
                {
                    try
                    {
                        ReplaceFallbackInstance(cube, cubeTemplate, cubeInfo, donor, kv.Key);
                        replaced = true;
                        healed++;
                    }
                    catch (Exception e)
                    {
                        VRLog.Warn("Core", $"Sky alternative heal: replacing '{kv.Key}' threw " +
                                           $"({e.GetType().Name}: {e.Message}) — " +
                                           (isFloor ? "clone-patching the floor instead." : "removing the cube instead."));
                        if (isFloor && CloneFloorPatch(cube, kv.Key)) patchedForName++;
                        else removed++;
                    }
                }
                else if (isFloor)
                {
                    // FLOOR INTEGRITY (iii): a floor piece is NEVER removed — the 130 hole
                    // ("Der Boden ist nicht zu sehen") was exactly this branch deleting
                    // 'StoneRooms.Floor.Tile'. No donor → clone a neighboring floor piece
                    // instance into the hole.
                    if (CloneFloorPatch(cube, kv.Key)) patchedForName++;
                    else removed++; // no floor art ANYWHERE in the clone — the gate reports it
                }
                else
                {
                    removed++; // non-floor: a small prop gap beats a debug cube (128 ruling)
                }
                try { UnityEngine.Object.Destroy(cube); }
                catch { /* already going down */ }
            }
            floorPatched += patchedForName;
            if (replaced)
            {
                if (_healedNames.Count < 8)
                    _healedNames.Add($"{kv.Key}←{donorName}");
                InjectDonorAlias(kv.Key, donor!);
            }
            else if (patchedForName > 0)
            {
                if (_floorPatchedNames.Count < 8 && !_floorPatchedNames.Contains(kv.Key))
                    _floorPatchedNames.Add(kv.Key);
            }
            else if (_removedNames.Count < 8)
            {
                _removedNames.Add(kv.Key);
            }
        }

        VRLog.Info("Core", $"Sky alternative HEAL pass {_healPasses} ({_genStyle}): {byName.Count} missing " +
                           $"asset name(s) → {healed} instance(s) replaced with donor art, {floorPatched} " +
                           $"floor piece(s) clone-patched, {removed} removed (no donor, non-floor). Healed: " +
                           $"[{(_healedNames.Count > 0 ? string.Join(", ", _healedNames) : "none")}]; floor-patched: " +
                           $"[{(_floorPatchedNames.Count > 0 ? string.Join(", ", _floorPatchedNames) : "none")}]; " +
                           $"removed: [{(_removedNames.Count > 0 ? string.Join(", ", _removedNames) : "none")}].");
    }

    /// <summary>A resource name that is a FLOOR piece — the family the 130 finding c bans
    /// from ever being removed ('StoneRooms.Floor.Tile', 'Marsh.Floor.Tile#2', …).</summary>
    private static bool IsFloorPieceName(string missing)
        => missing.IndexOf(".Floor", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// FLOOR INTEGRITY (iii) — LAST RESORT: patch a floor hole by cloning the nearest
    /// healthy floor piece INSTANCE over the fallback cube's spot (same-family pieces share
    /// the same procedure-frame semantics, so the neighbor's own scale/rotation are the
    /// right ones; only the XZ location comes from the hole). Never runs when a donor
    /// prefab exists — this is for "no floor art in any loaded packet".
    /// </summary>
    private static bool CloneFloorPatch(GameObject cube, string missing)
    {
        GameObject? inst = _mapInstance;
        if (inst == null || cube == null)
            return false;
        try
        {
            GameObject? src = null;
            float best = float.MaxValue;
            Vector3 at = cube.transform.position;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsFallbackRenderer(r) || r.gameObject == cube)
                    continue;
                if (r.name.IndexOf(".Floor", StringComparison.OrdinalIgnoreCase) < 0
                    && !r.name.StartsWith("GloomhavenVR.FloorPatch", StringComparison.Ordinal))
                    continue;
                Vector3 d = r.transform.position - at;
                float sq = d.x * d.x + d.z * d.z; // nearest in the floor PLANE
                if (sq < best)
                {
                    best = sq;
                    src = r.gameObject;
                }
            }
            if (src == null)
                return false;
            GameObject go = UnityEngine.Object.Instantiate(
                src, new Vector3(at.x, src.transform.position.y, at.z),
                src.transform.rotation, cube.transform.parent);
            go.name = "GloomhavenVR.FloorPatch." + missing; // mod prefix: IsModObject-excluded
            // lossy-scale match through the (possibly different) parent chain.
            Vector3 want = src.transform.lossyScale;
            Vector3 have = go.transform.lossyScale;
            go.transform.localScale = Vector3.Scale(go.transform.localScale, new Vector3(
                SafeDiv(want.x, have.x), SafeDiv(want.y, have.y), SafeDiv(want.z, have.z)));
            go.hideFlags = cube.hideFlags;
            ApplyBuildLayer(go);
            return true;
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative floor patch for '{missing}' threw " +
                               $"({e.GetType().Name}: {e.Message}) — the FLOOR GATE will report the gap.");
            return false;
        }
    }

    /// <summary>
    /// FLOOR INTEGRITY (i) — the RESOURCE TOPOLOGY census, once per run, on the first heal
    /// miss: WHERE the missing pieces' art actually lives. Logs the loader's complete
    /// serialized packet inventory (its _references names — the full set of packets that
    /// EXIST, decompiled ApparanceResourceListLoader.cs:22-23), which of them are loaded,
    /// each missing category's resolved table mapping, and every loaded entry carrying the
    /// missing family token — so the next hardware log answers 'which packet serves
    /// StoneRooms.Floor.*' from enumerated facts instead of warmup-token guesses.
    /// </summary>
    private static void LogResourceTopologyOnce(IEnumerable<string> missingNames)
    {
        if (_topologyCensusDone)
            return;
        _topologyCensusDone = true;
        try
        {
            ApparanceEngine? engine = ApparanceEngine.Instance;
            if (engine == null)
                return;
            ApparanceResourceListLoader? loader = engine.GetComponent<ApparanceResourceListLoader>();
            ApparanceResources? res = engine.Resources != null ? engine.Resources : engine.GetComponent<ApparanceResources>();
            if (loader == null || res == null)
                return;

            var refNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ApparanceResourceListLoader.NameReference nr in loader._references)
            {
                if (nr != null && !string.IsNullOrEmpty(nr.Name))
                    refNames.Add(nr.Name);
            }
            var loadedNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, ApparanceResourceList> kv in loader._loadedPackets)
                loadedNames.Add(kv.Key);

            // Per missing name: the category's resolved mapping (mirroring LookupResourceList)
            // and every loaded entry that carries the piece's family token (e.g. 'Floor').
            var perMiss = new List<string>(4);
            foreach (string missing in missingNames)
            {
                if (perMiss.Count >= 4)
                    break;
                int dot = missing.IndexOf('.');
                if (dot <= 0)
                    continue;
                string category = missing.Substring(0, dot);
                int dot2 = missing.IndexOf('.', dot + 1);
                string family = dot2 > dot ? missing.Substring(dot + 1, dot2 - dot - 1)
                                           : missing.Substring(dot + 1);
                string mapping = "<unmapped>";
                foreach (ApparanceResourceTable table in res.Indirects)
                {
                    if (table == null)
                        continue;
                    foreach (ApparanceResourceTable.ResourceListMapping m in table.ResourceLists)
                    {
                        if (string.Compare(m.Category, category, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            mapping = table.AssetPathPrefix + m.AssetPath;
                            break;
                        }
                    }
                    if (mapping == "<unmapped>" && table.autoFallback)
                        mapping = table.AssetPathPrefix + category + " [autoFallback]";
                    if (mapping != "<unmapped>")
                        break;
                }
                var carriers = new List<string>(6);
                foreach (KeyValuePair<string, ApparanceResourceList> kv in loader._loadedPackets)
                {
                    if (kv.Value == null || carriers.Count >= 6)
                        continue;
                    foreach (ApparanceObjectResource o in kv.Value.Objects)
                    {
                        if (o == null || string.IsNullOrEmpty(o.Name)
                            || o.Name.IndexOf(family, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        carriers.Add($"{kv.Key}:{o.Name}{(o.Object == null ? "[null]" : "")}");
                        if (carriers.Count >= 6)
                            break;
                    }
                }
                perMiss.Add($"'{missing}' cat '{category}' → {mapping}; '{family}' entries " +
                            $"[{(carriers.Count > 0 ? string.Join(", ", carriers) : "NONE in any loaded packet")}]");
            }

            VRLog.Info("Core", $"Sky alternative RESOURCE TOPOLOGY: {refNames.Count} packet(s) exist " +
                               $"[{string.Join(", ", refNames)}]; loaded now " +
                               $"[{string.Join(", ", loadedNames)}]. Miss resolution: " +
                               string.Join(" | ", perMiss));
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative RESOURCE TOPOLOGY census failed ({e.GetType().Name}: " +
                               $"{e.Message}) — diagnostics only, the heal continues.");
        }
    }

    /// <summary>
    /// Invert the engine's placement math to put the DONOR into the exact frame the cube fills
    /// (decompiled ApparanceEntity: scale = l ⊗ frameSize ⊘ boundsSize; worldPos = O +
    /// R·((−boundsMin) ⊗ scale ⊘ l); Instantiate at world pos/rot, then localScale = scale).
    /// From the cube instance (worldPos P_c, worldRot R, localScale s_c) and both prefab
    /// bounds: frameSize = s_c ⊗ size_c ⊘ l_c; O = P_c − R·((−min_c) ⊗ s_c ⊘ l_c);
    /// s_d = l_d ⊗ frameSize ⊘ size_d; P_d = O + R·((−min_d) ⊗ s_d ⊘ l_d). Zero-size axes
    /// keep scale 1, mirroring the engine's own guard.
    /// </summary>
    private static void ReplaceFallbackInstance(
        GameObject cube, GameObject cubeTemplate, AssetInfo cubeInfo, GameObject donor, string missing)
    {
        Vector3 lC = cubeTemplate.transform.localScale;
        Vector3 minC = cubeInfo.MinBounds;
        Vector3 sizeC = cubeInfo.MaxBounds - cubeInfo.MinBounds;

        // Donor bounds exactly the way the engine measures assets (AssetInfo.UpdateBounds:
        // collider bounds first, mesh bounds second, relative to the template's own position).
        Bounds db = ApparanceResources.AccumulateObjectBounds(donor);
        Vector3 minD = db.min - donor.transform.position;
        Vector3 sizeD = db.max - db.min;
        Vector3 lD = donor.transform.localScale;

        Vector3 sC = cube.transform.localScale;
        Quaternion rot = cube.transform.rotation;
        Vector3 frame = new(
            SafeDiv(sC.x * sizeC.x, lC.x), SafeDiv(sC.y * sizeC.y, lC.y), SafeDiv(sC.z * sizeC.z, lC.z));
        Vector3 sD = new(
            sizeD.x != 0f ? SafeDiv(lD.x * frame.x, sizeD.x) : 1f,
            sizeD.y != 0f ? SafeDiv(lD.y * frame.y, sizeD.y) : 1f,
            sizeD.z != 0f ? SafeDiv(lD.z * frame.z, sizeD.z) : 1f);
        Vector3 originWorld = cube.transform.position
            - rot * new Vector3(SafeDiv(-minC.x * sC.x, lC.x), SafeDiv(-minC.y * sC.y, lC.y), SafeDiv(-minC.z * sC.z, lC.z));
        Vector3 posD = originWorld
            + rot * new Vector3(SafeDiv(-minD.x * sD.x, lD.x), SafeDiv(-minD.y * sD.y, lD.y), SafeDiv(-minD.z * sD.z, lD.z));

        GameObject go = UnityEngine.Object.Instantiate(donor, posD, rot, cube.transform.parent);
        go.name = "GloomhavenVR.Heal." + missing; // mod prefix: IsModObject-excluded by name too
        go.transform.localScale = sD;
        go.hideFlags = cube.hideFlags; // blend into the Generated-Content container's flags
        ApplyBuildLayer(go);
    }

    private static float SafeDiv(float a, float b) => b != 0f ? a / b : a;

    /// <summary>
    /// Donor search for a missing asset name: an entry with the SAME piece suffix (everything
    /// after the category's first dot-segment, e.g. 'Floor.TileSplit' or 'Door.Thick' with
    /// the '#variant' stripped as second chance) in any of this run's warmed resource lists —
    /// preferred in warmup-token order, i.e. same-family art first — then the engine's
    /// preloaded Externals lists. Only GameObject entries qualify (materials fall back
    /// separately and are not healed here).
    /// </summary>
    private static GameObject? FindDonor(string missing, out string donorName)
    {
        donorName = "";
        int dot = missing.IndexOf('.');
        if (dot <= 0 || dot >= missing.Length - 1)
            return null;
        string suffix = missing.Substring(dot + 1);
        int hash = suffix.IndexOf('#');
        string baseSuffix = hash > 0 ? suffix.Substring(0, hash) : suffix;

        ApparanceEngine? engine = ApparanceEngine.Instance;
        if (engine == null)
            return null;
        ApparanceResourceListLoader? loader = engine.GetComponent<ApparanceResourceListLoader>();
        ApparanceResources? res = engine.Resources != null ? engine.Resources : engine.GetComponent<ApparanceResources>();

        GameObject? best = null;
        string bestName = "";
        int bestRank = int.MaxValue;

        void Consider(ApparanceObjectResource? o)
        {
            if (o == null || o.Object == null || !(o.Object is GameObject go) || string.IsNullOrEmpty(o.Name))
                return;
            if (string.Equals(o.Name, missing, StringComparison.OrdinalIgnoreCase))
                return; // the minted placeholder family — never a donor for itself
            int d = o.Name.IndexOf('.');
            if (d <= 0 || d >= o.Name.Length - 1)
                return;
            string prefix = o.Name.Substring(0, d);
            string tail = o.Name.Substring(d + 1);
            // 130 FLOOR FIX (FLOOR INTEGRITY ii): strip the '#variant' from the DONOR tail
            // too. The 130 removal of 'StoneRooms.Floor.Tile' happened because the warmed
            // Dungeon list only carries 'Floor.Tile#N' variants — under exact-only matching
            // a variant can serve a variant but never the base piece, so the base-name floor
            // had "no donor anywhere" while nine of its siblings healed fine.
            int dh = tail.IndexOf('#');
            string tailBase = dh > 0 ? tail.Substring(0, dh) : tail;
            int suffixRank;
            if (string.Equals(tail, suffix, StringComparison.OrdinalIgnoreCase))
                suffixRank = 0; // exact piece incl. variant
            else if (string.Equals(tailBase, suffix, StringComparison.OrdinalIgnoreCase))
                suffixRank = 1; // a donor VARIANT of the exact missing piece
            else if (string.Equals(tail, baseSuffix, StringComparison.OrdinalIgnoreCase))
                suffixRank = 2; // base piece, missing side's variant dropped
            else if (string.Equals(tailBase, baseSuffix, StringComparison.OrdinalIgnoreCase))
                suffixRank = 3; // both sides variant-stripped — family match
            else
                return;
            int prefRank = _warmupTokens.Length + 1;
            for (int t = 0; t < _warmupTokens.Length; t++)
            {
                if (string.Equals(_warmupTokens[t], prefix, StringComparison.OrdinalIgnoreCase))
                {
                    prefRank = t;
                    break;
                }
            }
            int rank = suffixRank * 100 + prefRank;
            if (rank < bestRank)
            {
                bestRank = rank;
                best = go;
                bestName = o.Name;
            }
        }

        try
        {
            if (loader != null)
            {
                // FLOOR INTEGRITY (ii): the donor scope is EVERY packet loaded in the
                // session (the loader's own _loadedPackets store, publicized — a superset of
                // this run's warmed paths), so a floor donor is found wherever a floor set
                // was ever warmed — by us or by the game.
                foreach (KeyValuePair<string, ApparanceResourceList> kv in loader._loadedPackets)
                {
                    ApparanceResourceList? list = kv.Value;
                    if (list == null)
                        continue;
                    for (int j = 0; j < list.Objects.Count; j++)
                        Consider(list.Objects[j]);
                }
            }
            if (res != null)
            {
                foreach (ApparanceResourceList external in res.Externals)
                {
                    if (external == null)
                        continue;
                    for (int j = 0; j < external.Objects.Count; j++)
                        Consider(external.Objects[j]);
                }
                for (int j = 0; j < res.Objects.Count; j++)
                    Consider(res.Objects[j]);
            }
        }
        catch { /* engine tearing down — whatever was found so far stands */ }

        donorName = bestName;
        return best;
    }

    /// <summary>
    /// Teach the SESSION the donor: inject an alias entry (missing name → donor prefab) into
    /// the resource list the category actually resolves to — mirrored from the game's own
    /// LookupResourceList (first table carrying the category, autoFallback path included), so
    /// every later run resolves through the game's own FindExternalAsset name match and the
    /// engine places the donor itself, frame-exact. The minted null placeholder for the same
    /// name is purged first (it would match BEFORE our alias).
    /// </summary>
    private static void InjectDonorAlias(string missing, GameObject donor)
    {
        try
        {
            ApparanceEngine? engine = ApparanceEngine.Instance;
            if (engine == null)
                return;
            ApparanceResourceListLoader? loader = engine.GetComponent<ApparanceResourceListLoader>();
            ApparanceResources? res = engine.Resources != null ? engine.Resources : engine.GetComponent<ApparanceResources>();
            if (loader == null || res == null)
                return;
            int dot = missing.IndexOf('.');
            if (dot <= 0)
                return;
            string category = missing.Substring(0, dot);

            ApparanceResourceList? target = null;
            foreach (ApparanceResourceTable table in res.Indirects)
            {
                if (table == null)
                    continue;
                string? path = null;
                bool mapped = false;
                foreach (ApparanceResourceTable.ResourceListMapping mapping in table.ResourceLists)
                {
                    if (string.Compare(mapping.Category, category, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        path = table.AssetPathPrefix + mapping.AssetPath;
                        mapped = true;
                        break;
                    }
                }
                if (!mapped && table.autoFallback)
                    path = table.AssetPathPrefix + category;
                if (path == null)
                    continue;
                target = loader.LoadCheck(path);
                if (target != null)
                    break;
            }
            if (target == null)
                return;

            target.Objects.RemoveAll(o => o != null && o.Object == null &&
                string.Equals(o.Name, missing, StringComparison.OrdinalIgnoreCase));
            var alias = new ApparanceObjectResource
            {
                Name = missing,
                Object = donor,
                Description = "GloomhavenVR donor alias",
            };
            alias.UpdateName(target.GetCategory());
            target.Objects.Add(alias);
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative heal: alias injection for '{missing}' threw " +
                               $"({e.GetType().Name}: {e.Message}) — this run is healed in place anyway.");
        }
    }

    // ---- FINALIZE -------------------------------------------------------------------------------

    /// <summary>Class doc step 6 + NORMALIZATION MATH: force visibility, measure, light,
    /// neutralize, strip, layer, place, prove.</summary>
    private static void FinalizeBuild(Transform frame, int renderers)
    {
        GameObject inst = _mapInstance!;

        // 0. NO RED BOX IS EVER PLACED (finding 3 goal state) — the heal pass normally
        // leaves zero fallbacks, but a deadline finalize after exhausted heal passes could
        // still carry one; sweep them here as last insurance. FLOOR pieces are clone-patched
        // first (FLOOR INTEGRITY iii — never deleted), everything else to the removed list.
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !IsFallbackRenderer(r))
                continue;
            string? miss = ParseMissingName(r.name);
            if (miss != null && IsFloorPieceName(miss) && CloneFloorPatch(r.gameObject, miss))
            {
                if (_floorPatchedNames.Count < 8 && !_floorPatchedNames.Contains(miss))
                    _floorPatchedNames.Add(miss);
            }
            else if (_removedNames.Count < 8 && miss != null && !_removedNames.Contains(miss))
            {
                _removedNames.Add(miss);
            }
            // Immediate, not deferred: the ROOM CENSUS below runs THIS frame and must prove
            // the 0-fallback goal state; the object is our own staged clone's child.
            try { UnityEngine.Object.DestroyImmediate(r.gameObject); }
            catch { /* already going down */ }
        }

        // 0.5 CULL TO ONE ROOM (130 finding a — "ein Raum reicht in dem man ist"): if the
        // resolved map still carries several authored room tiles, keep exactly the one whose
        // XZ footprint contains the map's render-bounds center (nearest-center fallback) and
        // destroy the other tiles' subtrees NOW — before anything measures, lights or
        // places. Their ApparanceEntities are neutralized first so the destroy disposes
        // plain GameObjects, not live engine containers (the root-cause-B lesson).
        ProceduralMapTile[] tiles = inst.GetComponentsInChildren<ProceduralMapTile>(true);
        int culledTiles = 0;
        if (tiles.Length > 1)
        {
            Bounds all = default;
            bool hasAll = false;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                if (!hasAll) { all = r.bounds; hasAll = true; }
                else all.Encapsulate(r.bounds);
            }
            ProceduralMapTile? keep = null;
            float bestSq = float.MaxValue;
            foreach (ProceduralMapTile tile in tiles)
            {
                if (tile == null)
                    continue;
                Bounds tb = TileRenderBounds(tile, out bool hasTb);
                if (!hasTb)
                    tb = new Bounds(tile.transform.position, Vector3.zero);
                bool contains = hasAll
                    && tb.min.x <= all.center.x && all.center.x <= tb.max.x
                    && tb.min.z <= all.center.z && all.center.z <= tb.max.z;
                Vector3 d = tb.center - (hasAll ? all.center : tile.transform.position);
                float sq = d.x * d.x + d.z * d.z;
                if (contains)
                    sq -= 1e6f; // containing tiles always beat merely-near ones
                if (keep == null || sq < bestSq)
                {
                    keep = tile;
                    bestSq = sq;
                }
            }
            foreach (ProceduralMapTile tile in tiles)
            {
                if (tile == null || tile == keep)
                    continue;
                foreach (ApparanceEntity e in tile.GetComponentsInChildren<ApparanceEntity>(true))
                {
                    if (e == null)
                        continue;
                    try
                    {
                        e.m_GenerationTiers?.Clear();
                        e.m_GenerationRoot = null;
                        e.m_Instances?.Clear();
                        e.enabled = false;
                    }
                    catch { /* publicized access — defensive */ }
                }
                try { UnityEngine.Object.DestroyImmediate(tile.gameObject); culledTiles++; }
                catch { /* already going down */ }
            }
            tiles = inst.GetComponentsInChildren<ProceduralMapTile>(true);
            VRLog.Info("Core", $"Sky alternative: CULLED '{_mapName}' to ONE room — {culledTiles} other " +
                               $"tile subtree(s) destroyed, kept '{(keep != null ? keep.name : "<none>")}' " +
                               "(130 finding a: the whole level must never reach the player).");
        }

        // 1. FORCE FULL VISIBILITY (hypothesis B of the ModBuild-127 report): the map
        // prefab's serialized ProceduralMapTile.visibility is asset data this mod cannot read,
        // and the game's reveal flow (Choreographer) never runs for our clone — so the
        // fully-revealed state is FORCED, on our own staged instance only, via the game's own
        // ApplyVisibility (publicized; ShowContent activates the 'Full' content and drops the
        // gray 'Preview' hulls). The pre-force values go into the census.
        var visBefore = new Dictionary<ProceduralMapTile.Visibility, int>();
        foreach (ProceduralMapTile tile in tiles)
        {
            if (tile == null)
                continue;
            visBefore.TryGetValue(tile.visibility, out int n);
            visBefore[tile.visibility] = n + 1;
            try
            {
                tile.visibility = ProceduralMapTile.Visibility.All;
                tile.ApplyVisibility();
            }
            catch { /* one broken tile must not stop the rest */ }
        }

        // 2. MEASURE at staging (scale 1, identity rotation): world bounds == authored map
        // units. 130 MEASUREMENT FIX (class doc): the room tile is measured by the RENDERER
        // bounds of its own subtree, NOT the authored BoxCollider — his 130 log read the
        // collider at 60.2 on a 35.0-unit map (colliders can far exceed the art), which
        // alone halved the placed size. Sanity-clamp to the whole map's bounds as the
        // belt-and-braces for any remaining pathological tile.
        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }
        Vector3 rootPos = inst.transform.position;
        float totalExtent = hasBounds ? Mathf.Max(bounds.size.x, bounds.size.z) : 0f;

        Bounds mainTile = default;
        bool hasMainTile = false;
        foreach (ProceduralMapTile tile in tiles)
        {
            if (tile == null)
                continue;
            Bounds tb = TileRenderBounds(tile, out bool hasTb);
            if (!hasTb)
                continue;
            float ext = Mathf.Max(tb.size.x, tb.size.z);
            if (!hasMainTile || ext > Mathf.Max(mainTile.size.x, mainTile.size.z))
            {
                mainTile = tb;
                hasMainTile = true;
            }
        }
        float mainExtent = hasMainTile ? Mathf.Max(mainTile.size.x, mainTile.size.z) : totalExtent;
        if (totalExtent > 0.01f && mainExtent > totalExtent)
            mainExtent = totalExtent; // the 130 sanity clamp — a room is never larger than its map

        float norm = 1f;
        if (mainExtent > 0.01f)
            norm = TargetMainRoomMeters / mainExtent;
        if (totalExtent > 0.01f)
            norm = Mathf.Min(norm, MaxTotalRoomMeters / totalExtent);
        norm = Mathf.Clamp(norm, 0.02f, 10f);

        // The frame-origin anchor: the room tile's center — the PLAYER stands mid-room
        // (the frame origin is their floor point, fourth revision).
        Vector3 anchorWorld = hasMainTile ? mainTile.center : (hasBounds ? bounds.center : rootPos);

        // Floor top = the tile plane figures stand on (average ProceduralMapTile height);
        // bounds-min fallback if the tile walk yields nothing.
        float floorY = hasBounds ? bounds.min.y : rootPos.y;
        if (tiles.Length > 0)
        {
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == null)
                    continue;
                sum += tiles[i].transform.position.y;
                n++;
            }
            if (n > 0)
                floorY = sum / n;
        }

        // 2.7 FLOOR GATE (FLOOR INTEGRITY iv — "Der Boden ist nicht zu sehen, man kann
        // hindurch sehen"): prove the floor is closed BEFORE the room is placed. Runs at
        // staging (map units, renderer-bounds tests — the colliders go in step 6 and were
        // never trustworthy for this, see the measurement fix).
        FloorGate(inst, hasMainTile ? mainTile : bounds, hasBounds || hasMainTile, floorY, mainExtent);

        // 3. LIGHT THE ROOM (root cause C). Drive the game's own ambience light rig to full —
        // DynamicAmbience.Start parked it at SetLightLevel(0) and only a ProceduralScenario's
        // blend flow (which our clone never joins) would ever raise it. SetLightLevel(1f) is
        // the game's own "fully blended in" light state: template intensity, instances active.
        // RenderSettings.ambient* is deliberately NOT written — that is the scenario's global
        // state (rejected alternative: calling ApplyBlendIn(1f), which would hijack it).
        int ambienceRigs = 0;
        foreach (DynamicAmbience amb in inst.GetComponentsInChildren<DynamicAmbience>(true))
        {
            if (amb == null)
                continue;
            try { amb.SetLightLevel(1f); ambienceRigs++; }
            catch { /* a rig without templates — the fallback rig below covers the room */ }
            // 130 finding b (COMPONENT PRESERVE-VS-DESTROY): the rig OWNER is PRESERVED but
            // DISABLED — it has no Update (decompiled: driven only by a scenario's blend
            // flow, which our clone never joins), so disabling costs nothing and guarantees
            // no future blend path can drive our clone's ambient/fog globals; its light
            // instances stay active at level 1 and their flicker drivers keep ticking.
            amb.enabled = false;
        }

        // Every room light is restricted to the MOD LAYER ONLY: it must relight OUR room
        // (whose geometry all lives on the mod layer), never restyle the scenario board or
        // the game's own tiles. These lights are part of the mod-owned clone — no game light
        // is ever written (hard rule 4 untouched). Authored ranges are recorded for
        // SyncRoomLightRanges: Unity light range does not follow transform scale, and the
        // placed room runs at (norm × rig scale) — unscaled, a candle would light centimeters.
        _roomLights.Clear();
        _roomLightRanges.Clear();
        int activeLights = 0;
        foreach (Light l in inst.GetComponentsInChildren<Light>(true))
        {
            if (l == null)
                continue;
            l.cullingMask = VRLayers.ModLayerMask;
            _roomLights.Add(l);
            _roomLightRanges.Add(l.range);
            if (l.enabled && l.gameObject.activeInHierarchy)
                activeLights++;
        }
        if (activeLights == 0)
        {
            // The generated content brought no usable lights — spawn a modest own rig so the
            // room can never be pitch black again: one warm/cool key near the ceiling center
            // of the MAIN room, one dim fill from the opposite half. Authored in MAP UNITS
            // here (the instance is still at staging scale 1); SyncRoomLightRanges scales
            // them with everything else.
            Color key = _genStyle == SkyStyle.Cellar
                ? new Color(1f, 0.83f, 0.58f)   // candlelight
                : new Color(0.62f, 0.72f, 1f);  // moonlight
            Vector3 center = anchorWorld;
            float roomR = Mathf.Max(1f, mainExtent * 0.5f);
            SpawnFallbackLight(inst.transform, "GloomhavenVR.SkyAlternative.RoomLight.Key",
                center + new Vector3(0f, roomR * 0.5f, 0f), key, 1.15f, Mathf.Max(mainExtent, 1f) * 0.9f);
            SpawnFallbackLight(inst.transform, "GloomhavenVR.SkyAlternative.RoomLight.Fill",
                center + new Vector3(roomR * 0.4f, roomR * 0.3f, roomR * 0.4f),
                Color.Lerp(key, Color.white, 0.5f), 0.45f, Mathf.Max(mainExtent, 1f) * 0.7f);
            activeLights = 2;
        }

        // 4. NEUTRALIZE THE APPARANCE MACHINERY (root cause B — this is the load-bearing
        // order; do NOT swap back to plain component disabling): CheckEntity destroys the
        // native entity of any non-enabled component on the NEXT ENGINE TICK, and its Clear /
        // DestroyObject callbacks would destroy every generated child by container reference
        // and by placement handle. Emptying m_GenerationTiers/m_GenerationRoot/m_Instances
        // FIRST (publicized) makes that destruction find nothing: the native entity is
        // disposed, the content survives as plain GameObjects the engine has forgotten.
        // (Frozen=true was REJECTED — it keeps the native entity alive, and the native side
        // re-tiers against the viewpoint autonomously; a room around the player would
        // re-synthesize on locomotion, un-layered.)
        int neutralized = 0;
        foreach (ApparanceEntity e in inst.GetComponentsInChildren<ApparanceEntity>(true))
        {
            if (e == null)
                continue;
            try
            {
                e.m_GenerationTiers?.Clear();
                e.m_GenerationRoot = null;
                e.m_Instances?.Clear();
            }
            catch { /* publicized access — defensive */ }
            if (e.enabled)
            {
                e.enabled = false; // next engine tick: DestroyEntity disposes the native side only
                neutralized++;
            }
        }

        // 5. DESTROY the procedural/tile machinery components (root cause D): their own
        // OnDisable/OnDestroy deregister them from the game's caches (ObjectCacheService via
        // ProceduralTileObserver.OnDisable, ProceduralWall.m_WallCache via its OnDestroy) and
        // SceneRegistry's ComponentRegistry prunes destroyed entries on Collect — so
        // WallSegmentFade, UnseenTileOrder and every other tile sweep provably never adopts
        // the placed room again. THE 130-b LINE (class doc COMPONENT PRESERVE-VS-DESTROY):
        // this list destroys scenario LOGIC only. PRESERVED and left ticking: MaterialLoader
        // (pending material assigns, healer-supervised), ParticleSystem (normalized below),
        // Animator/Animation (self-contained clips), LightFlicker (re-based after placement)
        // and the disabled DynamicAmbience rig owners (driven to level 1 above). StaticAmbience
        // is still DESTROYED — it is a pure global-state writer (RenderSettings.skybox/
        // ambientMode + the camera's post-processing profile), never a visual driver.
        int destroyed = 0;
        destroyed += DestroyComponents<ProceduralMapTile>(inst);
        destroyed += DestroyComponents<ProceduralWall>(inst);
        destroyed += DestroyComponents<ProceduralProp>(inst);
        destroyed += DestroyComponents<ProceduralDoorway>(inst);
        destroyed += DestroyComponents<ProceduralStyle>(inst);
        destroyed += DestroyComponents<UnityGameEditorDoorProp>(inst);
        destroyed += DestroyComponents<TilesOcclusionVolume>(inst);
        destroyed += DestroyComponents<StaticAmbience>(inst);

        // 5.5 PARTICLES STAY ALIVE AND IN SCALE (130 finding b: "Fackeln ... Effekte"): the
        // generated content's Shuriken systems (torch flames, dust, drips) are authored at
        // map scale, but the placed room runs at (norm × rig scale) — Hierarchy scaling
        // makes size/speed/shape follow the room's scale (the established pattern, same as
        // the FX shell), and Local simulation space makes in-flight particles ride the rare
        // re-seat moves instead of smearing behind the room. Defensive Play() for systems
        // authored with playOnAwake off.
        int roomParticles = 0;
        foreach (ParticleSystem ps in inst.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null)
                continue;
            roomParticles++;
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            if (main.simulationSpace == ParticleSystemSimulationSpace.World)
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
            if (!ps.isPlaying && ps.gameObject.activeInHierarchy)
                ps.Play(withChildren: false);
        }

        // 6. Non-interactive by ruling — a stray collider would eat laser/poke rays room-wide.
        Collider[] colliders = inst.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            UnityEngine.Object.Destroy(c);

        // 7. Mod layer, recursive — head camera only (the hidden staging layer, if it was
        // used, ends HERE; a placed room must render). Generated-Content containers are plain
        // children; hideFlags don't hide them from this walk. Also covers the fallback lights.
        VRLayers.Apply(inst);

        // 8. PLACE: room center → frame origin, floor top → frame-local y = 0. The frame is
        // the room branch (fourth revision — SkyAlternative.PlaceBothBranches owns its
        // pose/scale): its origin is the PLAYER's floor point, its scale the rig scale — so
        // the player stands mid-room on its floor and the room reads TargetMainRoomMeters
        // perceived meters. Between seat events it is world-frozen; zoom changes only the
        // perceived size of the whole arrangement (finding 3).
        Vector3 centerOff = anchorWorld - rootPos;
        inst.transform.SetParent(frame, worldPositionStays: false);
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one * norm;
        inst.transform.localPosition =
            new Vector3(-centerOff.x, -(floorY - rootPos.y), -centerOff.z) * norm;
        _roomFrameExtent = totalExtent * norm; // far-plane budget (MinFarWorldUnits)
        SyncRoomLightRanges();

        // 8.5 RE-BASE the world-space animation caches (COMPONENT PRESERVE-VS-DESTROY:
        // LightFlicker cached its Start pose at STAGING — without this, preserved flames
        // with adjustLocation would teleport back toward the staging depth).
        int flickerRebased = RebaseFlickerDrivers(inst);

        ReturnBorrowedFocus();
        if (_stagingRoot != null)
        {
            UnityEngine.Object.Destroy(_stagingRoot); // focus GO went with ReturnBorrowedFocus/parenting
            _stagingRoot = null;
        }

        _genPhase = GenPhase.Built;
        _builtCensusDone = false;
        _builtCensusTime = Time.realtimeSinceStartup + BuiltCensusDelaySeconds;
        VRLog.Info("Core", $"Sky alternative: {_genStyle} room BUILT from the game's own '{_mapName}' — " +
                           $"{renderers} generated renderer(s), {tiles.Length} room tile(s) kept" +
                           $"{(culledTiles > 0 ? $" after culling {culledTiles}" : "")}, " +
                           $"{neutralized} entity(ies) neutralized, {destroyed} logic component(s) destroyed" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} collider(s) stripped" : "")}; " +
                           $"alive: {roomParticles} particle system(s) kept ticking, {flickerRebased} flicker " +
                           $"driver(s) re-based, {ambienceRigs} ambience rig(s) at level 1 and preserved disabled. " +
                           $"Bounds {(hasBounds ? bounds.size.ToString("F1") : "<none>")} map units, room " +
                           $"{mainExtent:F1} → normalization {norm:F3} (room target {TargetMainRoomMeters:F0} " +
                           $"PERCEIVED m at the seat scale, whole map ≤ {MaxTotalRoomMeters:F0} m, frame extent " +
                           $"{totalExtent * norm:F1}), floor at map y {floorY - rootPos.y:F2} aligned to the real " +
                           "floor under the player; room center anchored to the room-branch origin = the " +
                           "player's floor point. Engine detail focus returned.");

        LogRoomCensus(inst, visBefore, ambienceRigs, activeLights);
    }

    /// <summary>THE DIAGNOSTIC BLOCK (grep: <c>ROOM CENSUS</c>) — one compact placement-time
    /// proof of the render state so the next hardware log convicts any residual cause without
    /// screenshots: renderer/material/loader states, fallback heal results BY NAME (finding 3),
    /// room + scene lighting, ambient.</summary>
    private static void LogRoomCensus(
        GameObject inst,
        Dictionary<ProceduralMapTile.Visibility, int> visBefore, int ambienceRigs, int activeLights)
    {
        try
        {
            int total = 0, drawing = 0, disabled = 0, nullSlot = 0, fallbacks = 0;
            var samples = new List<string>(4);
            var fallbackNames = new List<string>(8);
            Bounds placed = default;
            bool hasPlaced = false;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                total++;
                if (!hasPlaced) { placed = r.bounds; hasPlaced = true; }
                else placed.Encapsulate(r.bounds);
                bool active = r.gameObject.activeInHierarchy;
                if (active && r.enabled)
                    drawing++;
                if (IsFallbackRenderer(r))
                {
                    fallbacks++;
                    if (fallbackNames.Count < 8)
                        fallbackNames.Add(ParseMissingName(r.name) ?? r.name);
                }
                Material[] shared = r.sharedMaterials;
                bool hasNull = shared.Length == 0;
                foreach (Material m in shared)
                {
                    if (m == null) { hasNull = true; break; }
                }
                if (hasNull)
                    nullSlot++;
                if (active && !r.enabled)
                {
                    disabled++;
                    if (samples.Count < 4)
                        samples.Add($"'{r.name}'[{MaterialLoaderHeal.DescribeForRenderer(r)}]");
                }
            }

            int loaders = 0, loaderEntries = 0;
            foreach (MaterialLoader ml in inst.GetComponentsInChildren<MaterialLoader>(true))
            {
                if (ml == null)
                    continue;
                loaders++;
                loaderEntries += ml.LoadersData?.Count ?? 0;
            }

            var visParts = new List<string>(4);
            foreach (KeyValuePair<ProceduralMapTile.Visibility, int> kv in visBefore)
                visParts.Add($"{kv.Key} x{kv.Value}");

            // Scene lights: which lights could reach the placed room at all, and do any of the
            // GAME's lights cover the mod layer (expected: none — that is exactly why the room
            // carries its own rig).
            int sceneLights = 0, touching = 0, coveringModLayer = 0;
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                if (l == null || l.transform.IsChildOf(inst.transform))
                    continue;
                sceneLights++;
                bool touch = l.type == LightType.Directional
                    || (hasPlaced && placed.SqrDistance(l.transform.position) <= l.range * l.range);
                if (!touch)
                    continue;
                touching++;
                if ((l.cullingMask & VRLayers.ModLayerMask) != 0)
                    coveringModLayer++;
            }

            var lightParts = new List<string>(6);
            for (int i = 0; i < _roomLights.Count && lightParts.Count < 6; i++)
            {
                Light l = _roomLights[i];
                if (l == null)
                    continue;
                lightParts.Add($"'{l.name}' {l.type} on={l.enabled && l.gameObject.activeInHierarchy} " +
                               $"int={l.intensity:F2} range={l.range:F1} mask=0x{l.cullingMask:X8}");
            }

            VRLog.Info("Core", $"Sky alternative ROOM CENSUS ({_genStyle}, '{_mapName}'): renderers {total} " +
                               $"({drawing} drawing, {disabled} active-but-disabled, {nullSlot} with null " +
                               $"material slot(s), {fallbacks} fallback 'Red Cube'(s)" +
                               $"{(fallbackNames.Count > 0 ? ": " + string.Join(", ", fallbackNames) : "")}); " +
                               $"heal passes {_healPasses}, healed " +
                               $"[{(_healedNames.Count > 0 ? string.Join(", ", _healedNames) : "none")}], floor-patched " +
                               $"[{(_floorPatchedNames.Count > 0 ? string.Join(", ", _floorPatchedNames) : "none")}], removed " +
                               $"[{(_removedNames.Count > 0 ? string.Join(", ", _removedNames) : "none")}]; " +
                               $"MaterialLoaders {loaders} ({loaderEntries} entries, healer-supervised); tile " +
                               $"visibility pre-force [{string.Join(", ", visParts)}] → forced All; packets warmed " +
                               $"{_warmupRequested} (failed: {(_warmupFailed.Count > 0 ? string.Join(", ", _warmupFailed) : "none")}).");
            VRLog.Info("Core", $"Sky alternative ROOM CENSUS lights: room rig {_roomLights.Count} light(s) " +
                               $"({activeLights} active, {ambienceRigs} ambience rig(s) driven to level 1, " +
                               $"mod-layer-only masks) [{string.Join("; ", lightParts)}]; scene lights " +
                               $"{sceneLights}, {touching} reach the room bounds, {coveringModLayer} of " +
                               $"those cover the mod layer; ambient {RenderSettings.ambientMode} " +
                               $"intensity {RenderSettings.ambientIntensity:F2} sky {RenderSettings.ambientSkyColor}." +
                               $"{(samples.Count > 0 ? " Disabled samples: " + string.Join(" ", samples) : "")}");
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative ROOM CENSUS failed ({e.GetType().Name}: {e.Message}) — " +
                               "the room itself is unaffected (diagnostics only).");
        }
    }

    private static void SpawnFallbackLight(
        Transform parent, string name, Vector3 worldPos, Color color, float intensity, float range)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.position = worldPos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = Mathf.Max(1f, range);
        l.shadows = LightShadows.None; // ambience, not simulation — and shadow-free by budget
        l.cullingMask = VRLayers.ModLayerMask;
        _roomLights.Add(l);
        _roomLightRanges.Add(l.range);
    }

    private static int DestroyComponents<T>(GameObject root) where T : Component
    {
        int n = 0;
        foreach (T c in root.GetComponentsInChildren<T>(true))
        {
            if (c == null)
                continue;
            try { UnityEngine.Object.Destroy(c); n++; }
            catch { /* already going down with the scene */ }
        }
        return n;
    }

    /// <summary>Encapsulated RENDERER bounds of a tile's own subtree — the 130 measurement
    /// fix (the authored BoxCollider read 60.2 on a 35.0-unit map; art is the truth).</summary>
    private static Bounds TileRenderBounds(ProceduralMapTile tile, out bool has)
    {
        Bounds b = default;
        has = false;
        foreach (Renderer r in tile.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null)
                continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>
    /// THE FLOOR GATE (FLOOR INTEGRITY iv). A grid of test points over the room's inner
    /// footprint (12% inset — the wall band) is checked against the FLOOR-BAND renderers:
    /// enabled renderers whose bounds TOP sits within a band around the floor plane, plus
    /// anything named like floor art. An uncovered cell first tries to RE-ENABLE a
    /// reveal-disabled authored hex floor renderer there (the census's 'hex (N)'[no-loader]
    /// pieces — re-enabled only when every material slot is present and no MaterialLoader
    /// manages the renderer, so nothing can disable it again or load over it), else it
    /// CLONE-PATCHES the nearest healthy floor piece into the cell. One line logs the
    /// result; runs once per build at staging, renderer-bounds tests only (no physics — the
    /// colliders are stripped by ruling).
    /// </summary>
    private static void FloorGate(GameObject inst, Bounds room, bool hasRoom, float floorY, float mainExtent)
    {
        try
        {
            if (!hasRoom || mainExtent <= 0.01f)
            {
                VRLog.Warn("Core", "Sky alternative FLOOR GATE skipped — no measurable room footprint.");
                return;
            }
            float band = Mathf.Max(0.75f, 0.05f * mainExtent);

            var cover = new List<Bounds>(64);       // enabled floor-band renderer bounds
            var disabledHex = new List<Renderer>(16); // re-enable candidates (materials present)
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsFallbackRenderer(r))
                    continue;
                Bounds rb = r.bounds;
                bool inBand = rb.max.y >= floorY - band && rb.max.y <= floorY + band;
                bool floorish = inBand
                    || r.name.IndexOf(".Floor", StringComparison.OrdinalIgnoreCase) >= 0
                    || r.name.StartsWith("GloomhavenVR.FloorPatch", StringComparison.Ordinal);
                if (!floorish)
                    continue;
                bool drawing = r.enabled && r.gameObject.activeInHierarchy;
                if (drawing)
                {
                    cover.Add(rb);
                }
                else if (!r.enabled && r.gameObject.activeInHierarchy
                         && (r.name.StartsWith("hex", StringComparison.OrdinalIgnoreCase)
                             || r.name.StartsWith("halfhex", StringComparison.OrdinalIgnoreCase)))
                {
                    // The reveal-disabled authored floor hexes from his 130 census. Only
                    // usable when self-sufficient: all materials present, no loader that
                    // could disable it again mid-load.
                    Material[] shared = r.sharedMaterials;
                    bool ok = shared.Length > 0;
                    foreach (Material m in shared)
                    {
                        if (m == null) { ok = false; break; }
                    }
                    if (ok && MaterialLoaderHeal.DescribeForRenderer(r) == "no-loader")
                        disabledHex.Add(r);
                }
            }

            const int N = 10;
            float inset = 0.12f;
            float x0 = Mathf.Lerp(room.min.x, room.max.x, inset);
            float x1 = Mathf.Lerp(room.min.x, room.max.x, 1f - inset);
            float z0 = Mathf.Lerp(room.min.z, room.max.z, inset);
            float z1 = Mathf.Lerp(room.min.z, room.max.z, 1f - inset);
            int covered = 0, reEnabled = 0, patched = 0, holes = 0;
            var holeSamples = new List<string>(3);
            for (int ix = 0; ix < N; ix++)
            {
                for (int iz = 0; iz < N; iz++)
                {
                    float x = Mathf.Lerp(x0, x1, (ix + 0.5f) / N);
                    float z = Mathf.Lerp(z0, z1, (iz + 0.5f) / N);
                    bool hit = false;
                    for (int i = 0; i < cover.Count; i++)
                    {
                        Bounds b = cover[i];
                        if (b.min.x <= x && x <= b.max.x && b.min.z <= z && z <= b.max.z)
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (hit) { covered++; continue; }

                    // Uncovered: re-enable a disabled authored hex covering this point first.
                    Renderer? hex = null;
                    for (int i = 0; i < disabledHex.Count; i++)
                    {
                        Bounds b = disabledHex[i].bounds;
                        if (b.min.x <= x && x <= b.max.x && b.min.z <= z && z <= b.max.z)
                        {
                            hex = disabledHex[i];
                            break;
                        }
                    }
                    if (hex != null)
                    {
                        hex.enabled = true;
                        cover.Add(hex.bounds);
                        disabledHex.Remove(hex);
                        reEnabled++;
                        covered++;
                        continue;
                    }
                    // Else clone-patch the nearest healthy floor piece into the cell.
                    if (patched < 24 && PatchFloorCell(inst, new Vector3(x, floorY, z), cover))
                    {
                        patched++;
                        covered++;
                        continue;
                    }
                    holes++;
                    if (holeSamples.Count < 3)
                        holeSamples.Add($"({x:F1}, {z:F1})");
                }
            }
            VRLog.Info("Core", $"Sky alternative FLOOR GATE ({_genStyle}, '{_mapName}'): {covered}/{N * N} " +
                               $"cells covered — {reEnabled} authored hex floor(s) re-enabled, {patched} " +
                               $"cell(s) clone-patched, {holes} hole(s) left" +
                               $"{(holeSamples.Count > 0 ? " at " + string.Join(" ", holeSamples) : "")} " +
                               $"(floor band ±{band:F1} around map y {floorY:F2}, {cover.Count} floor renderer(s)).");
        }
        catch (Exception e)
        {
            VRLog.Warn("Core", $"Sky alternative FLOOR GATE failed ({e.GetType().Name}: {e.Message}) — " +
                               "the room is unaffected; the census still reports renderer state.");
        }
    }

    /// <summary>Clone the nearest floor-band piece over an uncovered gate cell (same parent
    /// as the source, so the authored local scale carries over verbatim). Appends the new
    /// cover bounds so later cells see it.</summary>
    private static bool PatchFloorCell(GameObject inst, Vector3 at, List<Bounds> cover)
    {
        try
        {
            Renderer? src = null;
            float best = float.MaxValue;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || IsFallbackRenderer(r) || !r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                if (r.name.IndexOf(".Floor", StringComparison.OrdinalIgnoreCase) < 0
                    && !r.name.StartsWith("hex", StringComparison.OrdinalIgnoreCase)
                    && !r.name.StartsWith("halfhex", StringComparison.OrdinalIgnoreCase)
                    && !r.name.StartsWith("GloomhavenVR.FloorPatch", StringComparison.Ordinal))
                    continue;
                Vector3 d = r.transform.position - at;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < best)
                {
                    best = sq;
                    src = r;
                }
            }
            if (src == null)
                return false;
            GameObject go = UnityEngine.Object.Instantiate(
                src.gameObject,
                new Vector3(at.x, src.transform.position.y, at.z),
                src.transform.rotation,
                src.transform.parent);
            go.name = "GloomhavenVR.FloorPatch.Gate";
            ApplyBuildLayer(go);
            Renderer? nr = go.GetComponentInChildren<Renderer>();
            if (nr != null)
                cover.Add(nr.bounds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// TickGuard-style guard for the PRESERVED LightFlicker drivers (class doc COMPONENT
    /// PRESERVE-VS-DESTROY): the component caches its light intensity, world position and
    /// scale at Start — which ran at the STAGING pose — and its Update writes the transform
    /// back from those caches when adjustLocation/adjustScale are authored on. Swap each one
    /// for a fresh copy carrying the same public tuning; the fresh Start (next frame, at the
    /// placed pose) re-bases every cache. Runs after placement and again after every re-seat
    /// (<see cref="NotifyRoomFrameMoved"/>). Reflection-free: LightFlicker's tuning fields
    /// are public (decompiled ThirdParty/LightFlicker.cs), only its caches are private —
    /// and a fresh Start rebuilds exactly those.
    /// </summary>
    private static int RebaseFlickerDrivers(GameObject inst)
    {
        int n = 0;
        foreach (LightFlicker f in inst.GetComponentsInChildren<LightFlicker>(true))
        {
            if (f == null)
                continue;
            try
            {
                GameObject go = f.gameObject;
                bool wasEnabled = f.enabled;
                float amount = f.amount;
                float speed = f.speed;
                bool adjustLocation = f.adjustLocation;
                float locationAdjustAmount = f.locationAdjustAmount;
                bool adjustScale = f.adjustScale;
                float scaleAdjustAmount = f.scaleAdjustAmount;
                Transform scaleObject = f.scaleObject;
                UnityEngine.Object.DestroyImmediate(f); // ours; immediate so no stale-cache Update runs
                LightFlicker fresh = go.AddComponent<LightFlicker>();
                fresh.amount = amount;
                fresh.speed = speed;
                fresh.adjustLocation = adjustLocation;
                fresh.locationAdjustAmount = locationAdjustAmount;
                fresh.adjustScale = adjustScale;
                fresh.scaleAdjustAmount = scaleAdjustAmount;
                fresh.scaleObject = scaleObject;
                fresh.enabled = wasEnabled;
                n++;
            }
            catch { /* one broken driver must not stop the rest */ }
        }
        return n;
    }

    /// <summary>The room branch just moved (a seat event in SkyAlternative.PlaceBothBranches)
    /// — re-base the placed room's world-space animation caches. No-op while nothing is
    /// placed.</summary>
    internal static void NotifyRoomFrameMoved()
    {
        if (_genPhase != GenPhase.Built || _mapInstance == null)
            return;
        RebaseFlickerDrivers(_mapInstance);
    }

    /// <summary>Re-derive every room light's world range from its AUTHORED range × the
    /// instance's lossy scale (root cause C: Unity light range ignores transform scale).
    /// Called after placement and from the branch placement site (PlaceBothBranches — the
    /// room branch is world-fixed between placements, so zoom never needs a range write) — a
    /// few float writes, prunes dead lights as it goes, no-op while no room is placed.</summary>
    private static void SyncRoomLightRanges()
    {
        GameObject? inst = _mapInstance;
        if (inst == null || _roomLights.Count == 0)
            return;
        float s = inst.transform.lossyScale.x;
        if (!(s > 0f) || float.IsInfinity(s))
            return;
        for (int i = _roomLights.Count - 1; i >= 0; i--)
        {
            Light l = _roomLights[i];
            if (l == null)
            {
                _roomLights.RemoveAt(i);
                _roomLightRanges.RemoveAt(i);
                continue;
            }
            l.range = _roomLightRanges[i] * s;
        }
    }

    /// <summary>Degrade this activation to the FX shell (one-shot warn per session).</summary>
    private static void FailGeneration(string reason)
    {
        CleanupStaging();
        _genPhase = GenPhase.Failed;
        if (reason.Contains("Addressables"))
            _mapLoadWarned = true; // the load path itself is broken — don't retry per activation
        if (!_genFailWarned)
        {
            _genFailWarned = true;
            VRLog.Warn("Core", $"Sky alternative: game-room generation FAILED — {reason}. The {_genStyle} " +
                               "style continues with its FX shell only (star dome / dust etc.); re-select " +
                               "the style or re-enter a scenario to try again.");
        }
        else
        {
            VRLog.Info("Core", $"Sky alternative: game-room generation failed again ({reason}) — FX shell only.");
        }
    }

    /// <summary>Cancel any in-flight generation and forget a placed room's bookkeeping. Called
    /// from <see cref="Deactivate"/> (the placed instance itself is a frame child and dies with
    /// the frame) and on mid-run style switches. Cached prefab handles stay (session-lifetime).</summary>
    private static void CancelMapGen(string reason)
    {
        bool hadWork = _genPhase == GenPhase.Loading || _genPhase == GenPhase.Warmup
            || _genPhase == GenPhase.Building;
        CleanupStaging();
        _mapInstance = null; // if Built, the frame child is destroyed by the frame teardown
        _roomFrameExtent = 0f;
        _roomLights.Clear();
        _roomLightRanges.Clear();
        _warmupPending.Clear();
        _healPasses = 0;
        _healedNames.Clear();
        _removedNames.Clear();
        _floorPatchedNames.Clear();
        _topologyCensusDone = false;
        _stagingLayerSafe = false;
        _genPhase = GenPhase.Idle;
        _genStyle = SkyStyle.Default;
        if (hadWork)
            VRLog.Info("Core", $"Sky alternative: game-room generation cancelled ({reason}).");
    }

    /// <summary>Destroy staging-time objects and return the borrowed focus. Idempotent.</summary>
    private static void CleanupStaging()
    {
        ReturnBorrowedFocus();
        if (_mapInstance != null && _genPhase != GenPhase.Built)
        {
            try { UnityEngine.Object.Destroy(_mapInstance); }
            catch { /* scene teardown already got it */ }
            _mapInstance = null;
        }
        if (_stagingRoot != null)
        {
            try { UnityEngine.Object.Destroy(_stagingRoot); }
            catch { /* scene teardown already got it */ }
            _stagingRoot = null;
        }
    }

    /// <summary>Give the engine its authored viewpoint back and reinstall the gaze driver —
    /// only what is still OURS is restored (a game system that re-pointed DetailFocus after us
    /// owns the field now; same rule as ApparanceDetailFocus.Restore). Idempotent.</summary>
    private static void ReturnBorrowedFocus()
    {
        try
        {
            if (_focusEngine != null && _genFocusGo != null && _focusEngine.DetailFocus == _genFocusGo)
            {
                _focusEngine.EnableDetailFocus = _origEnableFocus;
                _focusEngine.DetailFocus = _origFocus;
            }
        }
        catch { /* engine already torn down */ }
        _focusEngine = null;
        _origFocus = null;
        if (_genFocusGo != null)
        {
            try { UnityEngine.Object.Destroy(_genFocusGo); }
            catch { /* teardown */ }
            _genFocusGo = null;
        }
        if (_borrowedGazeDriver)
        {
            _borrowedGazeDriver = false;
            try { ApparanceDetailFocus.Install(); } // no-op when VR is not running
            catch { /* VR tearing down */ }
        }
    }

    private static void ReleaseMapHandle(int i)
    {
        _mapPrefabs[i] = null;
        try
        {
            if (_mapHandlesHeld[i] && _mapHandles[i].IsValid())
                Addressables.Release(_mapHandles[i]);
        }
        catch { /* released twice / never acquired */ }
        _mapHandlesHeld[i] = false;
    }

    /// <summary>Release the session-cached Addressables handles (VR stop / hot reload via
    /// <see cref="RestoreAll"/>). The next activation reloads them.</summary>
    private static void ReleaseMapPrefab()
    {
        for (int i = 0; i < _mapHandles.Length; i++)
            ReleaseMapHandle(i);
    }

    /// <summary>A renderer the engine placed as its debug missing-asset substitute — the
    /// 'Red Cube' prefab whose instance name carries the (Unable to find …) suffix that
    /// ApparanceEntity appends on a GetPrefab/GetMaterial miss (decompiled).</summary>
    private static bool IsFallbackRenderer(Renderer r)
        => r.name.StartsWith("Red Cube", StringComparison.Ordinal)
           || r.name.Contains("Unable to find"); // ApparanceEntity's parenthesized miss suffix

    /// <summary>Renderer count under every 'Generated Content' container — the success metric
    /// the reveal fix established ("maptile 'L' went Preview/0-renderers → All/229 renderers")
    /// — plus the fallback count (root cause A). The containers are HideAndDontSave
    /// (FindObjectsOfType misses them, a transform walk does not — SceneRegistry round-6
    /// lesson); a full-depth name scan over one map is cheap and runs on the 0.5 s poll
    /// cadence only.</summary>
    private static void CensusGeneratedContent(Transform root, ref int renderers, ref int fallbacks)
    {
        var stack = new Stack<Transform>(64);
        stack.Push(root);
        while (stack.Count > 0)
        {
            Transform t = stack.Pop();
            if (string.Equals(t.name, "Generated Content", StringComparison.Ordinal))
            {
                foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null)
                        continue;
                    renderers++;
                    if (IsFallbackRenderer(r))
                        fallbacks++;
                }
                continue; // no nested Generated Content inside a container
            }
            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
    }
}
