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
///  A. MISSING RESOURCE PACKETS (log 7741: floor renderers literally named
///     'Red Cube (Unable to find prefab: StoneRooms.Floor.Tile)'; 4253-4256: the SwampNight
///     doors were 'Red Cube (Unable to find prefab: Marsh.Door.Thick)'). Apparance resolves
///     asset descriptors through category-prefixed resource lists that load ON DEMAND through
///     Addressables (ApparanceResourceTable.LookupResourceList →
///     ApparanceResourceListLoader.LoadAsync, decompiled). The RUNNING scenario had never
///     needed the Dungeon/Forest packets, and a request that terminally misses gets a
///     null-Object fallback AssetInfo (ApparanceResources.GenerateFallbackAssetInfo) which is
///     placed as the engine's DEBUG 'Red Cube' (ApparanceResources.GetPrefab →
///     GetDebugMissingObject) — whose own MaterialLoader never completes, so it renders
///     NOTHING. Worse, the fallback is also cached BY NAME in the engine-global
///     ApparanceResources.Objects list, where HandleAssetRequest's by-name loop finds it BEFORE
///     any table lookup: one failed run POISONS every later run of the same session.
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
///  D. MOD SWEEPS ADOPTED THE ROOM AS A LIVE MAP TILE (log 4253-4256: WallSegmentFade's gate
///     machinery adopted OUR staged room's doors — "the embedding wall stack-adopts onto this
///     column and fades"; SceneRegistry.MapTiles enrols every ProceduralTileObserver.OnEnable).
///     A placed room whose walls the wall fader dissolves is "gar keine Wände" by itself.
///     ⇒ Fix: the instance is put on the MOD LAYER already AT INSTANTIATE and re-applied on
///     every build poll (WallSegmentFade's IsModObject exclusion keys on that layer), and at
///     finalize every ProceduralMapTile/ProceduralWall/ProceduralProp/ProceduralStyle/
///     ProceduralDoorway/UnityGameEditorDoorProp/TilesOcclusionVolume component is DESTROYED —
///     their own OnDisable/OnDestroy deregister them from the game's caches (ObjectCacheService,
///     ProceduralWall.m_WallCache) and SceneRegistry prunes destroyed entries, so no mod or
///     game system ever treats the placed room as scenery to manage again. MaterialLoader
///     components are deliberately KEPT — their pending Addressables loads still have to
///     assign materials, and MaterialLoaderHeal supervises them (census: registered=True).
///
/// THE SEQUENCE (once per activation, all phases ticked from <see cref="SkyAlternative.Tick"/>):
///
///  1. LOAD <c>Assets/_AssetBundles/mapsprocgen/Map A.prefab</c> via a mod-held Addressables
///     handle (the exact path <c>Choreographer.LoadMaps</c> builds, decompiled
///     Choreographer.cs:14938-14949; own handle, NOT AssetBundleManager's wrapper, whose
///     WaitForCompletion would stall the frame). The loaded prefab is cached for the session.
///  2. WARMUP (root cause A): purge poisoned null-Object fallback resources, then kick the
///     style vocabulary's resource-list packets through the game's own loader and WAIT until
///     each is loaded (or failed, or the deadline passes) BEFORE anything can request assets.
///     A packet the game is already loading is polled via LoadCheck; our own completions
///     notify the engine exactly like the game's HandleAsyncResourceListLoad does.
///  3. BUILD at a STAGING pose: instantiate under a mod root in the ProcGen scene (like the
///     game's own maps — and parked at (head.x, −50, head.z): offscreen under the scenario map,
///     horizontally near the play space), put it on the MOD LAYER immediately (root cause D).
///     Mute every <c>StaticAmbience</c>/<c>DynamicAmbience</c> on the instance FIRST (they
///     would overwrite the scenario's own skybox/ambient/fog if any game system blended them
///     in), write the style enums into every <c>ProceduralStyle</c> (Choreographer's
///     field-write pattern, Choreographer.cs:14812-14822), apply via <c>ForceValidate()</c>
///     (the level editor's runtime apply path, LevelEditorApparancePanel.cs:43), and populate
///     the walls (the minimal ProceduralScenario.SetupWalls: <c>IsPopulated = true</c> on each
///     wall entity — corner data stays default, which costs join quality only).
///  4. DETAIL FOCUS — BORROWED, ALWAYS (the documented decision): Apparance synthesis detail is
///     distance-scaled from the engine viewpoint, and the staging pose sits 50 wu below it —
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
///     <c>ApparanceEntity.IsBusy</c> remains, the census is stable across two consecutive
///     polls AND it contains no 'Red Cube' fallback renderers (fallbacks get half the window
///     to resolve, then the room is placed anyway with the census naming the residue — a room
///     with a missing prop beats no room). Timeout with NO content → one-shot warn, staging
///     cleaned up, focus returned, and the style degrades to the FX shell alone
///     (<see cref="GenPhase.Failed"/> — no retry loop; a style re-select or scenario re-entry
///     starts fresh).
///  6. FINALIZE + PLACE: force full tile visibility (ProceduralMapTile.ApplyVisibility — the
///     Map A prefab's serialized visibility state is asset data this mod cannot read, so All
///     is forced rather than assumed), measure, light the room (root cause C), neutralize the
///     Apparance machinery (root causes B + D), strip all colliders (non-interactive by
///     ruling), re-apply the mod layer, then reparent the instance into the ambient frame with
///     the normalization below, drop the staging root, and log the ROOM CENSUS diagnostic
///     block (one line at placement, one T+3 s survival proof).
///
/// NORMALIZATION MATH: the map is authored in world/diorama units; the ambient frame applies
/// the rig scale S, so a child at local scale n reads as (authored units × n) REAL meters —
/// independent of S by construction (frame world size = units·n·S, perceived = world/S). The
/// generated bounds are measured at staging (scale 1): n = TargetRoomMeters / max(size.x,
/// size.z) with TargetRoomMeters = 9 (room interior ≈ 8-10 real meters across; the bounds
/// include wall thickness, so the interior lands at the low end). Placement: the bounds'
/// horizontal center goes to the frame origin — which since Finding 4 (2026-08-12) is the
/// SCENARIO PLAY FIELD'S center, so the diorama sits exactly mid-room (fallback: the player's
/// floor point until the board exists — the anchor logic lives in SkyAlternative.PlaceAtPlayer)
/// — and the floor top goes to frame-local y = 0 (= the real floor, tracking is floor-origin):
/// floor height = the average ProceduralMapTile height (figures stand at tile level),
/// bounds-min fallback.
///
/// LIFECYCLE: <see cref="CancelMapGen"/> runs on every deactivation (scenario end/leave, style
/// change, MR on, VR stop) — it returns the borrowed focus, destroys the staging root, and
/// resets the phase so the next activation regenerates. A PLACED room is a child of the frame
/// root and dies with it in <c>Deactivate</c>. Scenario unload is additionally covered by the
/// scope gate itself (no scenario ⇒ Deactivate before the ProcGen teardown can matter), and a
/// mid-scenario external kill of the instance (fake-null) is detected in the Built tick: warn
/// once, degrade to the FX shell. The cached prefab handle is released in
/// <see cref="ReleaseMapPrefab"/> (VR stop / hot reload).
///
/// COST: Idle/Failed/Built phases are one enum compare per frame (Built adds one fake-null
/// check and, once, the T+3 s census). All real work happens once per activation inside the
/// build window. The room lights add one range write per light on the rare scale-write events
/// (<see cref="SyncRoomLightRanges"/>), zero steady-state.
/// MULTIPLAYER: local presentation only — the instance is mod-layer, never on the wire, and
/// the borrowed focus steers only LOCAL synthesis scheduling (peers run their own engines).
/// </summary>
internal static partial class SkyAlternative
{
    private const string ProcGenSceneName = "ProcGen";

    /// <summary>The exact Addressables path the game builds in <c>LoadAssetFromBundle</c>
    /// ("misc_mapsprocgen", "Map A", "mapsprocgen") — decompiled AssetBundleManager.cs:277-287.</summary>
    private const string MapAPrefabPath = "Assets/_AssetBundles/mapsprocgen/Map A.prefab";

    /// <summary>Target real-world size of the generated room across its longer horizontal
    /// bounds axis (class doc NORMALIZATION MATH).</summary>
    private const float TargetRoomMeters = 9f;

    /// <summary>Per-phase timeout: the Addressables load, the packet warmup and the generation
    /// poll each get this long before the run degrades (warmup degrades to building anyway —
    /// the census then proves what was missing).</summary>
    private const float GenTimeoutSeconds = 30f;

    private const float GenPollIntervalSeconds = 0.5f;

    /// <summary>Delay for the one-shot post-placement survival census (root cause B: the
    /// pre-fix content was destroyed ONE engine tick after placement — 3 s is unambiguous).</summary>
    private const float BuiltCensusDelaySeconds = 3f;

    private enum GenPhase { Idle, Loading, Warmup, Building, Built, Failed }

    private static GenPhase _genPhase = GenPhase.Idle;
    private static SkyStyle _genStyle = SkyStyle.Default; // the style the current gen run is for

    // Session-cached Addressables prefab (class doc step 1).
    private static AsyncOperationHandle<GameObject> _mapHandle;
    private static bool _mapHandleHeld;
    private static GameObject? _mapPrefab;
    private static bool _mapLoadWarned; // one-shot: Addressables cannot deliver Map A

    private static GameObject? _stagingRoot;  // in the ProcGen scene, at (head.x, -50, head.z)
    private static GameObject? _mapInstance;  // the Map A clone (staged, then frame child)
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

    // WARMUP bookkeeping (root cause A). Paths are the loader's own asset paths; a pending
    // entry either completes through our callback or (when the game kicked the same load
    // first) through the LoadCheck poll. Failed paths are kept for the census.
    private static bool _warmupKickDone;
    private static int _warmupRequested;
    private static readonly List<string> _warmupPending = new();
    private static readonly List<string> _warmupFailed = new();

    // ROOM LIGHTS (root cause C): every Light under the placed instance with its AUTHORED
    // range — Unity light range does not follow transform scale, so the range is re-derived
    // as (authored × instance lossyScale) on every scale write (SyncRoomLightRanges).
    private static readonly List<Light> _roomLights = new();
    private static readonly List<float> _roomLightRanges = new();

    // One-shot post-placement survival census (class doc COST).
    private static float _builtCensusTime;
    private static bool _builtCensusDone;

    /// <summary>
    /// Per-frame generation driver, called from <see cref="Tick"/> while a non-Default style is
    /// active in a scenario. <paramref name="frame"/> is the ambient frame root the finished
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
        if (_mapPrefab != null)
        {
            BeginWarmup(style);
            return;
        }
        if (_mapLoadWarned)
        {
            _genPhase = GenPhase.Failed; // Addressables already said no this session
            return;
        }
        try
        {
            _mapHandle = Addressables.LoadAssetAsync<GameObject>(MapAPrefabPath);
            _mapHandleHeld = true;
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
        if (!_mapHandle.IsDone)
        {
            if (Time.realtimeSinceStartup > _genDeadline)
                FailGeneration($"'{MapAPrefabPath}' Addressables load timed out ({GenTimeoutSeconds:F0} s)");
            return;
        }
        if (_mapHandle.Status != AsyncOperationStatus.Succeeded || _mapHandle.Result == null)
        {
            ReleaseMapPrefab();
            FailGeneration($"'{MapAPrefabPath}' Addressables load failed (status {_mapHandle.Status})");
            return;
        }
        _mapPrefab = _mapHandle.Result; // handle stays held for the session — the prefab is a
                                        // reference into Addressables' loaded bundle, not a copy
        _genStyle = style;
        BeginWarmup(style);
    }

    // ---- WARMUP: resource packets (root cause A) ------------------------------------------------

    /// <summary>The resource-list CATEGORY tokens a style's vocabulary can request (the
    /// category is a descriptor's first dot-segment: the hardware log's misses were
    /// 'StoneRooms.Floor.Tile' and 'Marsh.Door.Thick' — SubBiome names; Biome/Theme/Tone
    /// names are warmed too, and tokens without a table mapping are simply skipped).</summary>
    private static string[] WarmupTokens(SkyStyle style) => style == SkyStyle.Cellar
        ? new[] { "Dungeon", "StoneRooms", "Candlelight" }
        : new[] { "Forest", "Marsh", "StillWaters", "ForestMoonlight" };

    private static void BeginWarmup(SkyStyle style)
    {
        _genStyle = style;
        _warmupKickDone = false;
        _warmupRequested = 0;
        _warmupPending.Clear();
        _warmupFailed.Clear();
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
                    if (loader.LoadCheck(_warmupPending[i]) != null)
                        _warmupPending.RemoveAt(i);
                }
                catch { /* loader tearing down — the deadline path handles it */ }
            }
        }

        if (_warmupPending.Count == 0)
        {
            if (_warmupFailed.Count > 0)
                VRLog.Warn("Core", $"Sky alternative: {_warmupFailed.Count} resource packet(s) FAILED to load " +
                                   $"({string.Join(", ", _warmupFailed)}) — building anyway; missing art " +
                                   "resolves to the engine's fallback and the ROOM CENSUS will name it.");
            BeginBuild();
            return;
        }

        if (Time.realtimeSinceStartup > _genDeadline)
        {
            VRLog.Warn("Core", $"Sky alternative: packet warmup timed out with {_warmupPending.Count} " +
                               $"packet(s) still loading ({string.Join(", ", _warmupPending)}) — building " +
                               "anyway; the ROOM CENSUS will show any fallback residue.");
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
        // its assigned materials).
        try { res.RefreshResourceList(clear_unused: true); }
        catch (Exception e) { VRLog.Warn("Core", $"Sky alternative: resource-cache purge threw ({e.Message})."); }

        string[] tokens = WarmupTokens(_genStyle);
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
            if (loader.LoadCheck(path) != null)
            {
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

    // ---- BUILD ----------------------------------------------------------------------------------

    /// <summary>Class doc steps 3 + 4: instantiate at the staging pose, mod-layer it, mute
    /// ambience, write + validate styles, populate walls, borrow the engine detail focus.</summary>
    private static void BeginBuild()
    {
        ApparanceEngine? engine = ApparanceEngine.Instance;
        if (engine == null)
        {
            // Gate said "scenario alive", so this is a load-order frame — retry next tick.
            return;
        }

        // Staging pose: under the scenario map, horizontally at the player (class doc step 3).
        Camera? head = Rig.VRRigDriver.HeadCamera;
        Vector3 headPos = head != null ? head.transform.position : Vector3.zero;
        Vector3 staging = new(headPos.x, -50f, headPos.z);

        _stagingRoot = new GameObject("GloomhavenVR.SkyAlternative.MapGenStaging");
        Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
        if (pg.IsValid() && pg.isLoaded)
        {
            // Live in the ProcGen scene like the game's own maps do while building.
            try { SceneManager.MoveGameObjectToScene(_stagingRoot, pg); }
            catch { /* stays in the active scene — the staging pose still hides it */ }
        }
        _stagingRoot.transform.position = staging;

        _mapInstance = UnityEngine.Object.Instantiate(_mapPrefab!, _stagingRoot.transform);
        _mapInstance.name = "GloomhavenVR.SkyAlternative.Room." + _genStyle;
        _mapInstance.transform.localPosition = Vector3.zero;
        _mapInstance.transform.localRotation = Quaternion.identity;

        // MOD LAYER FROM BIRTH (root cause D): WallSegmentFade's IsModObject exclusion keys on
        // the mod layer, and the game cameras must never composite the staged build below the
        // map. Generated children arrive later on their authored layers — TickBuilding
        // re-applies this on every poll, FinalizeBuild once more.
        VRLayers.Apply(_mapInstance);

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

        // Style writes — the Choreographer's field-write pattern applied via ForceValidate (the
        // level editor's runtime apply path). Written on EVERY style under the instance so
        // nothing depends on parent inheritance (no scenario-level ProceduralStyle above us).
        // Vocabulary per .planning/game-env-assets.md §3: Cellar = Dungeon/StoneRooms/Candlelight;
        // SwampNight = Forest/Marsh/StillWaters/ForestMoonlight (ETone.Bioluminescence exists too,
        // but ForestMoonlight is the mood the star dome wants — a moonlit night, not a glow cave).
        ProceduralStyle[] styles = _mapInstance.GetComponentsInChildren<ProceduralStyle>(true);
        for (int i = 0; i < styles.Length; i++)
        {
            ProceduralStyle s = styles[i];
            if (s == null)
                continue;
            if (_genStyle == SkyStyle.Cellar)
            {
                s.Biome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome.Dungeon;
                s.SubBiome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome.StoneRooms;
                s.Tone = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone.Candlelight;
            }
            else
            {
                s.Biome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome.Forest;
                s.SubBiome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome.Marsh;
                s.Theme = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETheme.StillWaters;
                s.Tone = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone.ForestMoonlight;
            }
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
        // 50 wu below the viewpoint, beyond the proven detail range, so this is not optional).
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
                           $"'Map A' staged at {staging} ({styles.Length} ProceduralStyle(s) written+validated, " +
                           $"{walls} wall entity(ies) populated, {muted} ambience component(s) muted, engine " +
                           $"detail focus borrowed for ≤ {GenTimeoutSeconds:F0} s).");

        _genPhase = GenPhase.Building;
        float now = Time.realtimeSinceStartup;
        _genDeadline = now + GenTimeoutSeconds;
        _nextGenPoll = now;
        _lastRendererCount = -1;
    }

    /// <summary>Class doc step 5: poll until the census settles fallback-free, then finalize;
    /// timeout with content → place anyway (census names the residue); timeout without
    /// content → FX-shell fallback.</summary>
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

        // Children generated since the last poll are on their authored layers — re-apply the
        // mod layer each poll (root cause D; a sub-second window between polls remains and is
        // accepted: WallSegmentFade's sweeps run on their own 1 s cadence).
        VRLayers.Apply(_mapInstance);

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
        // (one extra confirmation beat so a between-entities gap can't pass as "done") AND no
        // 'Red Cube' fallback renderers remain — fallbacks get half the window to resolve
        // into real art before the room is placed with them (root cause A: a placed fallback
        // is permanent, but a room with one missing prop still beats the FX shell alone).
        bool fallbackGrace = now >= _genDeadline - GenTimeoutSeconds * 0.5f;
        if (renderers > 0 && busy == 0 && renderers == _lastRendererCount
            && (fallbacks == 0 || fallbackGrace))
        {
            FinalizeBuild(frame, renderers, fallbacks);
            return;
        }
        _lastRendererCount = renderers;

        if (now > _genDeadline)
        {
            if (renderers > 0)
                FinalizeBuild(frame, renderers, fallbacks); // late but real — place what exists
            else
                FailGeneration($"generation did not settle within {GenTimeoutSeconds:F0} s " +
                               $"(renderers={renderers}, busy entities={busy})");
        }
    }

    /// <summary>Class doc step 6 + NORMALIZATION MATH: force visibility, measure, light,
    /// neutralize, strip, layer, place, prove.</summary>
    private static void FinalizeBuild(Transform frame, int renderers, int fallbacks)
    {
        GameObject inst = _mapInstance!;

        // 1. FORCE FULL VISIBILITY (hypothesis B of the ModBuild-127 report): the Map A
        // prefab's serialized ProceduralMapTile.visibility is asset data this mod cannot read,
        // and the game's reveal flow (Choreographer) never runs for our clone — so the
        // fully-revealed state is FORCED, on our own staged instance only, via the game's own
        // ApplyVisibility (publicized; ShowContent activates the 'Full' content and drops the
        // gray 'Preview' hulls). The pre-force values go into the census.
        ProceduralMapTile[] tiles = inst.GetComponentsInChildren<ProceduralMapTile>(true);
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

        // 2. MEASURE at staging (scale 1, identity rotation): world bounds == authored map units.
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
        float extent = hasBounds ? Mathf.Max(bounds.size.x, bounds.size.z) : 0f;
        float norm = extent > 0.01f ? TargetRoomMeters / extent : 1f;
        norm = Mathf.Clamp(norm, 0.02f, 10f);

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
            // room can never be pitch black again: one warm/cool key near the ceiling center,
            // one dim fill from the opposite half. Authored in MAP UNITS here (the instance
            // is still at staging scale 1); SyncRoomLightRanges scales them with everything else.
            Color key = _genStyle == SkyStyle.Cellar
                ? new Color(1f, 0.83f, 0.58f)   // candlelight
                : new Color(0.62f, 0.72f, 1f);  // moonlight
            Vector3 center = hasBounds ? bounds.center : rootPos;
            float roomR = Mathf.Max(1f, extent * 0.5f);
            SpawnFallbackLight(inst.transform, "GloomhavenVR.SkyAlternative.RoomLight.Key",
                center + new Vector3(0f, roomR * 0.5f, 0f), key, 1.15f, extent * 0.9f);
            SpawnFallbackLight(inst.transform, "GloomhavenVR.SkyAlternative.RoomLight.Fill",
                center + new Vector3(roomR * 0.4f, roomR * 0.3f, roomR * 0.4f),
                Color.Lerp(key, Color.white, 0.5f), 0.45f, extent * 0.7f);
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
        // the placed room again. MaterialLoader components are KEPT: their pending
        // Addressables loads still assign materials and MaterialLoaderHeal supervises them.
        int destroyed = 0;
        destroyed += DestroyComponents<ProceduralMapTile>(inst);
        destroyed += DestroyComponents<ProceduralWall>(inst);
        destroyed += DestroyComponents<ProceduralProp>(inst);
        destroyed += DestroyComponents<ProceduralDoorway>(inst);
        destroyed += DestroyComponents<ProceduralStyle>(inst);
        destroyed += DestroyComponents<UnityGameEditorDoorProp>(inst);
        destroyed += DestroyComponents<TilesOcclusionVolume>(inst);
        destroyed += DestroyComponents<StaticAmbience>(inst);
        destroyed += DestroyComponents<DynamicAmbience>(inst); // light rig already at level 1

        // 6. Non-interactive by ruling — a stray collider would eat laser/poke rays room-wide.
        Collider[] colliders = inst.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            UnityEngine.Object.Destroy(c);

        // 7. Mod layer, recursive — head camera only. Generated-Content containers are plain
        // children; hideFlags don't hide them from this walk. Also covers the fallback lights.
        VRLayers.Apply(inst);

        // 8. PLACE: bounds center → frame origin (the play-field center since Finding 4 —
        // SkyAlternative.PlaceAtPlayer owns the anchor), floor top → frame-local y = 0 (the
        // real floor). Frame-local meters read as real meters (class doc math).
        Vector3 centerOff = hasBounds ? bounds.center - rootPos : Vector3.zero;
        inst.transform.SetParent(frame, worldPositionStays: false);
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one * norm;
        inst.transform.localPosition =
            new Vector3(-centerOff.x, -(floorY - rootPos.y), -centerOff.z) * norm;
        SyncRoomLightRanges();

        ReturnBorrowedFocus();
        if (_stagingRoot != null)
        {
            UnityEngine.Object.Destroy(_stagingRoot); // focus GO went with ReturnBorrowedFocus/parenting
            _stagingRoot = null;
        }

        _genPhase = GenPhase.Built;
        _builtCensusDone = false;
        _builtCensusTime = Time.realtimeSinceStartup + BuiltCensusDelaySeconds;
        VRLog.Info("Core", $"Sky alternative: {_genStyle} room BUILT from the game's own art — " +
                           $"{renderers} generated renderer(s), {neutralized} entity(ies) neutralized, " +
                           $"{destroyed} procedural component(s) destroyed" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} collider(s) stripped" : "")}. " +
                           $"Bounds {(hasBounds ? bounds.size.ToString("F1") : "<none>")} map units → " +
                           $"normalization {norm:F3} (target {TargetRoomMeters:F0} m across), floor at " +
                           $"map y {floorY - rootPos.y:F2} aligned to the real floor; placed in the " +
                           "ambient frame. Engine detail focus returned.");

        LogRoomCensus(inst, fallbacks, visBefore, ambienceRigs, activeLights);
    }

    /// <summary>THE DIAGNOSTIC BLOCK (grep: <c>ROOM CENSUS</c>) — one compact placement-time
    /// proof of the render state so the next hardware log convicts any residual cause without
    /// screenshots: renderer/material/loader states, room + scene lighting, ambient.</summary>
    private static void LogRoomCensus(
        GameObject inst, int fallbacks,
        Dictionary<ProceduralMapTile.Visibility, int> visBefore, int ambienceRigs, int activeLights)
    {
        try
        {
            int total = 0, drawing = 0, disabled = 0, nullSlot = 0;
            var samples = new List<string>(4);
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

            VRLog.Info("Core", $"Sky alternative ROOM CENSUS ({_genStyle}): renderers {total} " +
                               $"({drawing} drawing, {disabled} active-but-disabled, {nullSlot} with null " +
                               $"material slot(s), {fallbacks} fallback 'Red Cube'(s)); MaterialLoaders " +
                               $"{loaders} ({loaderEntries} entries, healer-supervised); tile visibility " +
                               $"pre-force [{string.Join(", ", visParts)}] → forced All; packets warmed " +
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

    /// <summary>Re-derive every room light's world range from its AUTHORED range × the
    /// instance's lossy scale (root cause C: Unity light range ignores transform scale).
    /// Called after placement and from every rig-scale write site in SkyAlternative
    /// (PlaceAtPlayer / NotifyRigScaled / HealScaleDrift) — a few float writes, prunes dead
    /// lights as it goes, no-op while no room is placed.</summary>
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
    /// the frame) and on mid-run style switches. Cached prefab handle stays (session-lifetime).</summary>
    private static void CancelMapGen(string reason)
    {
        bool hadWork = _genPhase == GenPhase.Loading || _genPhase == GenPhase.Warmup
            || _genPhase == GenPhase.Building;
        CleanupStaging();
        _mapInstance = null; // if Built, the frame child is destroyed by the frame teardown
        _roomLights.Clear();
        _roomLightRanges.Clear();
        _warmupPending.Clear();
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

    /// <summary>Release the session-cached Addressables handle (VR stop / hot reload via
    /// <see cref="RestoreAll"/>). The next activation reloads it.</summary>
    private static void ReleaseMapPrefab()
    {
        _mapPrefab = null;
        try
        {
            if (_mapHandleHeld && _mapHandle.IsValid())
                Addressables.Release(_mapHandle);
        }
        catch { /* released twice / never acquired */ }
        _mapHandleHeld = false;
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
    /// lesson); a full-depth name scan over one small map is cheap and runs on the 0.5 s poll
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
