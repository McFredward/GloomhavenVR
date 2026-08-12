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
/// THE SEQUENCE (once per activation, all phases ticked from <see cref="SkyAlternative.Tick"/>):
///
///  1. LOAD <c>Assets/_AssetBundles/mapsprocgen/Map A.prefab</c> via a mod-held Addressables
///     handle (the exact path <c>Choreographer.LoadMaps</c> builds, decompiled
///     Choreographer.cs:14938-14949; own handle, NOT AssetBundleManager's wrapper, whose
///     WaitForCompletion would stall the frame). The loaded prefab is cached for the session.
///  2. BUILD at a STAGING pose: instantiate under a mod root in the ProcGen scene (like the
///     game's own maps — and parked at (head.x, −50, head.z): offscreen under the scenario map,
///     horizontally near the play space). Mute every <c>StaticAmbience</c>/<c>DynamicAmbience</c>
///     on the instance FIRST (they would overwrite the scenario's own skybox/ambient/fog if any
///     game system blended them in), write the style enums into every <c>ProceduralStyle</c>
///     (Choreographer's field-write pattern, Choreographer.cs:14812-14822), apply via
///     <c>ForceValidate()</c> (the level editor's runtime apply path,
///     LevelEditorApparancePanel.cs:43), and populate the walls (the minimal
///     ProceduralScenario.SetupWalls: <c>IsPopulated = true</c> on each wall entity — corner
///     data stays default, which costs join quality only).
///  3. DETAIL FOCUS — BORROWED, ALWAYS (the documented decision): Apparance synthesis detail is
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
///  4. POLL (0.5 s cadence) until the Generated-Content renderer census is non-zero, no
///     <c>ApparanceEntity.IsBusy</c> remains, and the census is stable across two consecutive
///     polls. Timeout 30 s → one-shot warn, staging cleaned up, focus returned, and the style
///     degrades to the FX shell alone (<see cref="GenPhase.Failed"/> — no retry loop; a style
///     re-select or scenario re-entry starts fresh).
///  5. FREEZE + PLACE: disable every Apparance/procedural update component (the engine must
///     never re-tier or regenerate the room once placed — an <c>ApparanceEntity</c> whose
///     GameObject goes INACTIVE has its native entity destroyed, so freezing disables the
///     COMPONENTS, never the GameObjects), strip all colliders (non-interactive by ruling),
///     apply the mod layer, then reparent the instance into the ambient frame with the
///     normalization below and drop the staging root.
///
/// NORMALIZATION MATH: the map is authored in world/diorama units; the ambient frame applies
/// the rig scale S, so a child at local scale n reads as (authored units × n) REAL meters —
/// independent of S by construction (frame world size = units·n·S, perceived = world/S). The
/// generated bounds are measured at staging (scale 1): n = TargetRoomMeters / max(size.x,
/// size.z) with TargetRoomMeters = 9 (room interior ≈ 8-10 real meters across; the bounds
/// include wall thickness, so the interior lands at the low end). Placement: the bounds'
/// horizontal center goes to the frame origin (= the player's floor point — the player spawns
/// INSIDE the room) and the floor top goes to frame-local y = 0 (= the real floor, tracking is
/// floor-origin): floor height = the average ProceduralMapTile height (figures stand at tile
/// level), bounds-min fallback.
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
/// check). All real work happens once per activation inside the ≤ 30 s build window.
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

    /// <summary>Per-phase timeout: Addressables load and the generation poll each get this
    /// long before the style degrades to the FX shell.</summary>
    private const float GenTimeoutSeconds = 30f;

    private const float GenPollIntervalSeconds = 0.5f;

    private enum GenPhase { Idle, Loading, Building, Built, Failed }

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
            BeginBuild();
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
        BeginBuild();
    }

    /// <summary>Class doc steps 2 + 3: instantiate at the staging pose, mute ambience, write +
    /// validate styles, populate walls, borrow the engine detail focus.</summary>
    private static void BeginBuild()
    {
        ApparanceEngine? engine = ApparanceEngine.Instance;
        if (engine == null)
        {
            // Gate said "scenario alive", so this is a load-order frame — retry next tick.
            return;
        }

        // Staging pose: under the scenario map, horizontally at the player (class doc step 2).
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

        // MUTE ambience FIRST — StaticAmbience.Apply writes RenderSettings.skybox/ambient and
        // DynamicAmbience drives the scenario camera's DynamicFog; both would overwrite the
        // scenario's OWN sky/fog/ambient if any game system ever blended our instance in.
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

        // BORROW the detail focus (class doc step 3 — the documented decision: staging sits
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

    /// <summary>Class doc step 4: poll until the census settles, then finalize; timeout →
    /// FX-shell fallback.</summary>
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

        int busy = 0, renderers = 0;
        foreach (ApparanceEntity e in _mapInstance.GetComponentsInChildren<ApparanceEntity>(true))
        {
            if (e == null)
                continue;
            try { if (e.IsBusy) busy++; }
            catch { /* engine tearing down mid-frame */ }
        }
        CensusGeneratedContent(_mapInstance.transform, ref renderers);

        // Settled = content exists, nothing building, and the census matched the previous poll
        // (one extra confirmation beat so a between-entities gap can't pass as "done").
        if (renderers > 0 && busy == 0 && renderers == _lastRendererCount)
        {
            FinalizeBuild(frame, renderers);
            return;
        }
        _lastRendererCount = renderers;

        if (now > _genDeadline)
            FailGeneration($"generation did not settle within {GenTimeoutSeconds:F0} s " +
                           $"(renderers={renderers}, busy entities={busy})");
    }

    /// <summary>Class doc step 5 + NORMALIZATION MATH: freeze, strip, layer, measure, place.</summary>
    private static void FinalizeBuild(Transform frame, int renderers)
    {
        GameObject inst = _mapInstance!;

        // FREEZE — components only, never GameObjects (an inactive ApparanceEntity destroys its
        // native entity and re-synthesizes on re-activation; a disabled one just stops updating).
        int frozen = 0;
        foreach (MonoBehaviour b in inst.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (b == null || !b.enabled)
                continue;
            if (b is ApparanceEntity || b is ProceduralStyle || b is ProceduralMapTile
                || b is ProceduralWall || b is ProceduralProp)
            {
                b.enabled = false;
                frozen++;
            }
        }

        // Non-interactive by ruling — a stray collider would eat laser/poke rays room-wide.
        Collider[] colliders = inst.GetComponentsInChildren<Collider>(true);
        foreach (Collider c in colliders)
            UnityEngine.Object.Destroy(c);

        VRLayers.Apply(inst); // mod layer, recursive — head camera only (Generated-Content
                              // containers are plain children; hideFlags don't hide them from this walk)

        // MEASURE at staging (scale 1, identity rotation): world bounds == authored map units.
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
        ProceduralMapTile[] tiles = inst.GetComponentsInChildren<ProceduralMapTile>(true);
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

        // PLACE: bounds center → frame origin (player inside the room), floor top → frame-local
        // y = 0 (the real floor). Frame-local meters read as real meters (class doc math).
        Vector3 centerOff = hasBounds ? bounds.center - rootPos : Vector3.zero;
        inst.transform.SetParent(frame, worldPositionStays: false);
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one * norm;
        inst.transform.localPosition =
            new Vector3(-centerOff.x, -(floorY - rootPos.y), -centerOff.z) * norm;

        ReturnBorrowedFocus();
        if (_stagingRoot != null)
        {
            UnityEngine.Object.Destroy(_stagingRoot); // focus GO went with ReturnBorrowedFocus/parenting
            _stagingRoot = null;
        }

        _genPhase = GenPhase.Built;
        VRLog.Info("Core", $"Sky alternative: {_genStyle} room BUILT from the game's own art — " +
                           $"{renderers} generated renderer(s), {frozen} procedural component(s) frozen" +
                           $"{(colliders.Length > 0 ? $", {colliders.Length} collider(s) stripped" : "")}. " +
                           $"Bounds {(hasBounds ? bounds.size.ToString("F1") : "<none>")} map units → " +
                           $"normalization {norm:F3} (target {TargetRoomMeters:F0} m across), floor at " +
                           $"map y {floorY - rootPos.y:F2} aligned to the real floor; placed in the " +
                           "ambient frame around the player. Engine detail focus returned.");
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
        bool hadWork = _genPhase == GenPhase.Loading || _genPhase == GenPhase.Building;
        CleanupStaging();
        _mapInstance = null; // if Built, the frame child is destroyed by the frame teardown
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

    /// <summary>Renderer count under every 'Generated Content' container — the success metric
    /// the reveal fix established ("maptile 'L' went Preview/0-renderers → All/229 renderers").
    /// The containers are HideAndDontSave (FindObjectsOfType misses them, a transform walk does
    /// not — SceneRegistry round-6 lesson); a full-depth name scan over one small map is cheap
    /// and runs on the 0.5 s poll cadence only.</summary>
    private static void CensusGeneratedContent(Transform root, ref int renderers)
    {
        var stack = new Stack<Transform>(64);
        stack.Push(root);
        while (stack.Count > 0)
        {
            Transform t = stack.Pop();
            if (string.Equals(t.name, "Generated Content", StringComparison.Ordinal))
            {
                renderers += t.GetComponentsInChildren<Renderer>(true).Length;
                continue; // no nested Generated Content inside a container
            }
            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
    }
}
