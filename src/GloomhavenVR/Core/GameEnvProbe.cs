using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// DE-RISKING DIAGNOSTIC for the game-styled ambient environments
/// (<c>.planning/game-env-assets.md</c> — approach B: instantiate a game map template via
/// Addressables and let the Apparance engine dress it). Three facts must come off the user's
/// rig before that lane can be built; this class produces all three in one log.
///
/// <para><b>1. PASSIVE — Addressables catalog dump</b> (automatic, once per session): the game
/// loads every env asset through Addressables string paths
/// (<c>AssetBundleManager.LoadAssetFromBundle</c> wraps <c>Addressables.LoadAssetAsync</c>,
/// decompiled <c>AssetBundleManager.cs:277-287</c>; initialized at boot by the initial load
/// screen, <c>AssetBundleManager.cs:66-114</c>), so the full key catalog is enumerable at
/// runtime via <c>Addressables.ResourceLocators[i].Keys</c>. The dump lists the
/// <c>Assets/_AssetBundles/&lt;folder&gt;/</c> histogram plus every key matching the env/FX
/// patterns we cannot see from decompiled code (mapsprocgen, "Map ", torch/flame/fog/…).</para>
///
/// <para><b>2. PASSIVE — engine availability matrix</b> (automatic, per scene transition): logs
/// <c>ApparanceEngine.Instance</c> null/alive, the ProcGen scene's loaded state and a
/// Generated-Content census (the same container name <c>ProceduralMapTile.ShowContent</c> walks,
/// decompiled <c>ProceduralMapTile.cs:148-177</c>) at the main menu and on scenario entry —
/// including the menu AFTER a scenario, which answers "does the engine persist?".</para>
///
/// <para><b>3. ACTIVE — the B-probe</b> (gated by <c>[Sky] EnvProbe</c>, default OFF, advanced
/// browser only; one-shot per session, MENU scene only): additively load the ProcGen scene if
/// absent (the Choreographer's own load path — <c>SceneManager.LoadSceneAsync("ProcGen",
/// LoadSceneMode.Additive)</c>, decompiled <c>Choreographer.cs:14722-14729</c>), load
/// <c>Assets/_AssetBundles/mapsprocgen/Map A.prefab</c> (the exact path
/// <c>Choreographer.LoadMaps</c> uses, <c>Choreographer.cs:14938-14949</c>), instantiate it
/// 500 m BELOW the floor so nothing shows even on success, write the style enums the way the
/// Choreographer does (<c>ProceduralStyle.Biome/SubBiome/Tone</c> field writes,
/// <c>Choreographer.cs:14812-14822</c>) and apply them the way the game's level editor does
/// (<c>ProceduralStyle.ForceValidate()</c>, decompiled <c>LevelEditorApparancePanel.cs:43</c>),
/// then poll for 30 s logging engine state / entity build state / Generated-Content renderer
/// census every 5 s. Ends with one <c>ENV PROBE VERDICT</c> line and destroys everything it
/// created (instance, focus object, Addressables handle, the ProcGen scene if WE loaded it).</para>
///
/// <para><b>Detail-focus handling.</b> Apparance synthesis detail falls off with distance from
/// the engine's viewpoint (the whole <see cref="ApparanceDetailFocus"/> saga) — a map 500 m
/// below the head would synthesize as coarse scraps and produce a FALSE "never built" verdict.
/// So the probe points the engine's own <c>EnableDetailFocus/DetailFocus</c> override at the
/// map instance for its 30 s. <see cref="ApparanceDetailFocus"/>'s driver re-asserts ITS focus
/// every frame while VR runs, so the two must never overlap: the probe calls its public
/// <see cref="ApparanceDetailFocus.Uninstall"/> before overriding and
/// <see cref="ApparanceDetailFocus.Install"/> after cleanup (both idempotent; the menu has no
/// scenario content, so 30 s without the gaze driver costs nothing).</para>
///
/// <para><b>Safety.</b> Every step is try/caught; a failure mid-probe cleans up what it created
/// and can never break the menu (per-frame work rides <see cref="TickGuard"/>). The probe
/// touches no global render state (any <c>StaticAmbience</c>/<c>DynamicAmbience</c> components
/// on the instance are disabled first — they would otherwise write skybox/ambient/fog when a
/// scenario system blends them in), never fights MixedReality/SkyBackdrop/SkyAlternative, and
/// is purely local — nothing on the wire, menu-scene only, aborts the moment the menu unloads.</para>
/// </summary>
internal static class GameEnvProbe
{
    private const string Name = "EnvProbe";
    private const string HostName = "GloomhavenVR.GameEnvProbe";

    private const string ProcGenSceneName = "ProcGen";

    /// <summary>The exact Addressables path the game builds in <c>LoadAssetFromBundle</c>
    /// ("misc_mapsprocgen", "Map A", "mapsprocgen") — decompiled AssetBundleManager.cs:277-287.</summary>
    private const string MapAPrefabPath = "Assets/_AssetBundles/mapsprocgen/Map A.prefab";

    /// <summary>[Sky] EnvProbe — the active B-probe gate. Rides the rig module's config file
    /// (dev.gloomhavenvr.rig.cfg) beside [Sky] Style so both environment dials share one section;
    /// bound HERE (not in SkyAlternative.cs) with the file handle fetched from the
    /// <see cref="ModuleConfig"/> registry after an idempotent <c>Rig.RenderQuality.Bind()</c>,
    /// the owner of that file. Advanced browser only — deliberately no curated row.</summary>
    internal static ConfigEntry<bool>? Dial;

    private static GameObject? _hostGo;

    /// <summary>Install the diagnostic host (idempotent). Runs with or without VR — the catalog
    /// dump and the matrix are exactly as valuable from a flat session's log.</summary>
    internal static void Install()
    {
        if (_hostGo != null)
            return;
        BindConfig();
        _hostGo = new GameObject(HostName);
        UnityEngine.Object.DontDestroyOnLoad(_hostGo);
        _hostGo.hideFlags = HideFlags.HideAndDontSave;
        _hostGo.AddComponent<Driver>();
        VRLog.Info(Name, "installed — passive Addressables catalog dump + Apparance availability "
            + "matrix arm once per session; the active approach-B probe waits on [Sky] EnvProbe "
            + $"(currently {(Dial != null && Dial.Value ? "ON" : "off")}).");
    }

    /// <summary>Drop the host; the driver's OnDestroy aborts and cleans up any running probe.</summary>
    internal static void Uninstall()
    {
        if (_hostGo == null)
            return;
        try { UnityEngine.Object.Destroy(_hostGo); }
        catch { /* scene teardown already got it */ }
        _hostGo = null;
    }

    private static void BindConfig()
    {
        if (Dial != null)
            return;
        try
        {
            // The [Sky] section lives in the RIG module file, owned by Rig.RenderQuality.Bind
            // (which also ride-along-binds SkyAlternative). Bind() is idempotent; afterwards the
            // registry hands us the same ConfigFile instance without touching Rig/** code.
            Rig.RenderQuality.Bind();
            ConfigFile? rigFile = null;
            foreach (KeyValuePair<string, ConfigFile> kv in ModuleConfig.Snapshot())
            {
                if (string.Equals(kv.Key, "rig", StringComparison.Ordinal))
                {
                    rigFile = kv.Value;
                    break;
                }
            }
            if (rigFile == null)
            {
                VRLog.Warn(Name, "rig config file not found in the ModuleConfig registry — "
                    + "[Sky] EnvProbe not bound; the active probe stays unavailable this session.");
                return;
            }
            Dial = rigFile.Bind("Sky", "EnvProbe", Defaults.EnvProbe,
                "DIAGNOSTIC, one-shot per session — leave OFF in normal play. While ON and the "
                + "MAIN MENU is loaded, the mod runs the approach-B environment probe once: it "
                + "additively loads the game's ProcGen scene (if absent), instantiates the game's "
                + "own 'Map A' room template 500 m BELOW the floor (nothing becomes visible even "
                + "on success), writes a Dungeon/StoneRooms/Candlelight style into it and watches "
                + "the Apparance engine for 30 seconds, logging engine state, entity build state "
                + "and the Generated-Content renderer census every 5 seconds. Afterwards it "
                + "destroys everything it created and prints one 'ENV PROBE VERDICT' line. "
                + "How to use: switch ON, go to the main menu, wait 30 seconds, pull "
                + "LogOutput.log, switch OFF again. Purely local, nothing is synced to peers.");
        }
        catch (Exception e)
        {
            VRLog.Warn(Name, $"binding [Sky] EnvProbe failed ({e.GetType().Name}: {e.Message}) — "
                + "the active probe stays unavailable this session; passive diagnostics still run.");
        }
    }

    // ==============================================================================================
    //  Driver — all per-frame work, TickGuard-isolated
    // ==============================================================================================

    private sealed class Driver : MonoBehaviour
    {
        // ---- passive: catalog dump ---------------------------------------------------------
        private bool _catalogDone;
        private int _catalogTries;
        private float _catalogNextTry = float.MaxValue;

        // ---- passive: availability matrix --------------------------------------------------
        private int _matrixCountdown = -1;       // frames until the scheduled matrix line fires
        private string _matrixLabel = "";
        private int _matrixLogged;
        private int _menusSeen;
        private int _scenariosSeen;
        private const int MatrixMaxLines = 8;    // hard cap — a long session must not spam

        // ---- active probe state machine ----------------------------------------------------
        private enum Phase { Idle, SceneLoading, PrefabLoading, Polling, Done }

        private Phase _phase = Phase.Idle;
        private bool _probeStarted;              // one-shot per session, success or not
        private float _deadline;                 // per-phase timeout (realtime)
        private float _nextPoll;
        private float _pollEnd;

        private bool _weLoadedProcGen;
        private AsyncOperation? _sceneOp;
        private AsyncOperationHandle<GameObject> _prefabHandle;
        private bool _prefabHandleHeld;

        private GameObject? _probeRoot;          // in the ProcGen scene, at (0,-500,0)
        private GameObject? _instance;           // the Map A clone
        private GameObject? _focusGo;            // engine detail-focus target at the map centre

        private ApparanceEngine? _focusEngine;   // engine we overrode (restore exactly this one)
        private bool _origEnableFocus;
        private GameObject? _origFocus;
        private bool _uninstalledGazeDriver;

        // verdict inputs
        private bool _engineSeen;
        private int _bestRenderers;

        private Action? _tick;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            // Hot reload / VR teardown mid-probe: put everything back before the host dies.
            try { CleanupProbe("driver destroyed"); }
            catch { /* teardown */ }
        }

        private void Update() => TickGuard.Run("Core.GameEnvProbe", _tick ??= Tick);

        private void Tick()
        {
            TickCatalogDump();
            TickMatrix();
            TickProbe();
        }

        // ==========================================================================================
        //  Scene bookkeeping
        // ==========================================================================================

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                string n = scene.name ?? "";
                bool menu = n.StartsWith("MainMenu", StringComparison.Ordinal);
                bool scenario = n.StartsWith("Game", StringComparison.Ordinal);
                if (!menu && !scenario)
                    return;

                if (menu)
                {
                    _menusSeen++;
                    // First menu = Addressables boot is done (the initial load screen drives it,
                    // AssetBundleManager.cs:66-114) — schedule the catalog dump shortly after.
                    if (!_catalogDone && _catalogNextTry == float.MaxValue)
                        _catalogNextTry = Time.realtimeSinceStartup + 5f;
                }
                else
                {
                    _scenariosSeen++;
                }

                // Matrix line a moment later, so the scene's own Awake/Start chain (engine,
                // RoomVisibilityManager, map load kickoff) has actually run.
                if (_matrixLogged < MatrixMaxLines)
                {
                    _matrixCountdown = 90; // ~1 s at 90 Hz
                    _matrixLabel = menu
                        ? (_scenariosSeen > 0 ? $"menu #{_menusSeen} (after scenario)" : $"menu #{_menusSeen}")
                        : $"scenario #{_scenariosSeen}";
                }
            }
            catch (Exception e)
            {
                VRLog.Warn(Name, $"scene bookkeeping threw (ignored): {e.GetType().Name}: {e.Message}");
            }
        }

        private static bool IsMenuLoaded() =>
            IsSceneLoaded("MainMenu") || IsSceneLoaded("MainMenu_gamepad");

        private static bool IsScenarioLoaded() =>
            IsSceneLoaded("Game") || IsSceneLoaded("Game_gamepad");

        private static bool IsSceneLoaded(string name)
        {
            Scene s = SceneManager.GetSceneByName(name);
            return s.IsValid() && s.isLoaded;
        }

        // ==========================================================================================
        //  1. Addressables catalog dump (passive, once per session)
        // ==========================================================================================

        private void TickCatalogDump()
        {
            if (_catalogDone || Time.realtimeSinceStartup < _catalogNextTry)
                return;

            _catalogTries++;
            int totalKeys = 0;
            try
            {
                var folders = new Dictionary<string, int>(StringComparer.Ordinal);
                var matches = new List<string>(64);
                int matchCount = 0;
                int locators = 0;

                foreach (IResourceLocator locator in Addressables.ResourceLocators)
                {
                    locators++;
                    if (locator?.Keys == null)
                        continue;
                    foreach (object keyObj in locator.Keys)
                    {
                        if (keyObj is not string key || key.Length == 0)
                            continue;
                        totalKeys++;

                        const string prefix = "Assets/_AssetBundles/";
                        if (key.StartsWith(prefix, StringComparison.Ordinal))
                        {
                            int slash = key.IndexOf('/', prefix.Length);
                            string folder = slash > 0 ? key.Substring(prefix.Length, slash - prefix.Length) : "<root>";
                            folders.TryGetValue(folder, out int c);
                            folders[folder] = c + 1;
                        }

                        if (IsEnvKey(key))
                        {
                            matchCount++;
                            if (matches.Count < 40)
                                matches.Add(key);
                        }
                    }
                }

                if (totalKeys == 0)
                {
                    // Catalog not populated yet (boot still loading) — retry on a slow cadence.
                    if (_catalogTries >= 12)
                    {
                        _catalogDone = true;
                        VRLog.Warn(Name, $"CATALOG dump gave up after {_catalogTries} tries — "
                            + $"Addressables reported {locators} locator(s) and 0 string keys. "
                            + "Either Addressables never initialized or the catalog exposes no string keys.");
                    }
                    else
                    {
                        _catalogNextTry = Time.realtimeSinceStartup + 10f;
                    }
                    return;
                }

                _catalogDone = true;
                var sb = new StringBuilder(4096);
                sb.Append("CATALOG — Addressables key census (approach-B substrate; ")
                  .Append(locators).Append(" locator(s), ").Append(totalKeys).Append(" string keys).\n");

                // Folder histogram, largest first (bounded).
                var sorted = new List<KeyValuePair<string, int>>(folders);
                sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
                sb.Append("  Assets/_AssetBundles/ folders: ");
                int shown = 0;
                foreach (KeyValuePair<string, int> kv in sorted)
                {
                    if (shown++ == 20) { sb.Append("…+").Append(sorted.Count - 20).Append(" more"); break; }
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
                }
                sb.Append('\n');

                sb.Append("  env/FX pattern matches (mapsprocgen|Map |torch|flame|fire|fog|firefly|water|env|sky|candle|lantern|swamp|marsh): ")
                  .Append(matchCount).Append(" total, first ").Append(matches.Count).Append(":\n");
                for (int i = 0; i < matches.Count; i++)
                    sb.Append("    ").Append(matches[i]).Append('\n');

                VRLog.Info(Name, sb.ToString());
            }
            catch (Exception e)
            {
                _catalogDone = true; // never retry into the same throw every 10 s
                VRLog.Warn(Name, $"CATALOG dump threw ({e.GetType().Name}: {e.Message}) — skipped this session.");
            }
        }

        private static bool IsEnvKey(string key)
        {
            if (key.IndexOf("mapsprocgen", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (key.IndexOf("/Map ", StringComparison.Ordinal) >= 0)
                return true;
            // Report §5's FX pattern list — the loadable flame/fog/water prefab names are not
            // visible in decompiled code, only in the runtime catalog.
            string[] words = { "torch", "flame", "fire", "fog", "firefly", "water", "env", "sky",
                               "candle", "lantern", "swamp", "marsh" };
            for (int i = 0; i < words.Length; i++)
            {
                if (key.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        // ==========================================================================================
        //  2. Engine availability matrix (passive, per scene transition, capped)
        // ==========================================================================================

        private void TickMatrix()
        {
            if (_matrixCountdown < 0)
                return;
            if (_matrixCountdown-- > 0)
                return;

            _matrixLogged++;
            try
            {
                ApparanceEngine? engine = ApparanceEngine.Instance;
                bool procGen = IsSceneLoaded(ProcGenSceneName);

                int tiles = 0, containers = 0, renderers = 0;
                ProceduralMapTile[] all = FindObjectsOfType<ProceduralMapTile>();
                tiles = all.Length;
                int walked = 0;
                for (int i = 0; i < all.Length && walked < 8; i++)
                {
                    if (all[i] == null)
                        continue;
                    Transform? gen = FindChildByName(all[i].transform, "Generated Content", 3);
                    if (gen == null)
                        continue;
                    containers++;
                    walked++;
                    renderers += gen.GetComponentsInChildren<Renderer>(true).Length;
                }

                VRLog.Info(Name, $"MATRIX [{_matrixLabel}] ApparanceEngine.Instance="
                    + (engine != null ? "ALIVE" : "null")
                    + $"; ProcGen scene loaded={procGen}"
                    + $"; map tiles={tiles}, Generated-Content containers={containers} "
                    + $"(first {Math.Min(8, tiles)} tiles walked), renderers under them={renderers}.");
            }
            catch (Exception e)
            {
                VRLog.Warn(Name, $"MATRIX [{_matrixLabel}] census threw (ignored): {e.GetType().Name}: {e.Message}");
            }
        }

        /// <summary>Bounded-depth breadth-first child-by-name search — the Generated-Content
        /// containers are HideAndDontSave, so FindObjectsOfType misses them but a transform walk
        /// does not (SceneRegistry round-6 lesson).</summary>
        private static Transform? FindChildByName(Transform root, string name, int maxDepth)
        {
            if (root == null || maxDepth < 1)
                return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (string.Equals(c.name, name, StringComparison.Ordinal))
                    return c;
                Transform? deeper = FindChildByName(c, name, maxDepth - 1);
                if (deeper != null)
                    return deeper;
            }
            return null;
        }

        // ==========================================================================================
        //  3. The active B-probe
        // ==========================================================================================

        private void TickProbe()
        {
            try
            {
                switch (_phase)
                {
                    case Phase.Idle:
                        TickIdle();
                        break;
                    case Phase.SceneLoading:
                        TickSceneLoading();
                        break;
                    case Phase.PrefabLoading:
                        TickPrefabLoading();
                        break;
                    case Phase.Polling:
                        TickPolling();
                        break;
                    case Phase.Done:
                        break;
                }
            }
            catch (Exception e)
            {
                // A throw anywhere mid-probe ends it with a verdict + full cleanup — the menu
                // must be exactly as if the probe never ran.
                VRLog.Warn(Name, $"probe threw in phase {_phase} — aborting with cleanup. "
                    + $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
                FinishProbe("aborted: exception");
            }
        }

        private void TickIdle()
        {
            if (_probeStarted || Dial == null || !Dial.Value)
                return;
            if (!IsMenuLoaded() || IsScenarioLoaded())
                return;

            _probeStarted = true;
            VRLog.Note(Name, "ENV PROBE starting ([Sky] EnvProbe is ON, main menu loaded) — "
                + "approach-B micro-generation test, ~30 s, everything spawns 500 m below the floor "
                + "and is destroyed afterwards.");

            Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
            if (pg.IsValid() && pg.isLoaded)
            {
                _weLoadedProcGen = false;
                VRLog.Info(Name, "ProcGen scene already loaded — reusing it (will NOT unload it on cleanup).");
                BeginPrefabLoad();
                return;
            }

            // The Choreographer's own load path (Choreographer.cs:14727-14729) — a plain
            // build-settings additive scene load with no visible scenario-state precondition.
            // Whether it survives without a Choreographer is exactly what we are measuring.
            _sceneOp = SceneManager.LoadSceneAsync(ProcGenSceneName, LoadSceneMode.Additive);
            if (_sceneOp == null)
            {
                VRLog.Warn(Name, "SceneManager.LoadSceneAsync(\"ProcGen\", Additive) returned null — "
                    + "the scene is not in the build settings under that name from here.");
                FinishProbe("aborted: ProcGen scene load unavailable");
                return;
            }
            _weLoadedProcGen = true;
            _phase = Phase.SceneLoading;
            _deadline = Time.realtimeSinceStartup + 20f;
        }

        private void TickSceneLoading()
        {
            if (AbortIfMenuGone())
                return;
            if (_sceneOp != null && !_sceneOp.isDone)
            {
                if (Time.realtimeSinceStartup > _deadline)
                    FinishProbe("aborted: ProcGen scene load timed out (20 s)");
                return;
            }
            _sceneOp = null;

            Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
            if (!pg.IsValid() || !pg.isLoaded)
            {
                FinishProbe("aborted: ProcGen scene did not come up");
                return;
            }

            // Log what rides in that scene — the report could not see it statically.
            try
            {
                GameObject[] roots = pg.GetRootGameObjects();
                var sb = new StringBuilder(512);
                sb.Append("ProcGen scene loaded additively from the menu — ")
                  .Append(roots.Length).Append(" root object(s): ");
                for (int i = 0; i < roots.Length && i < 20; i++)
                    sb.Append('\'').Append(roots[i].name).Append("' ");
                if (roots.Length > 20)
                    sb.Append("…");
                sb.Append("| ApparanceEngine.Instance=")
                  .Append(ApparanceEngine.Instance != null ? "ALIVE" : "null");
                VRLog.Info(Name, sb.ToString());
            }
            catch (Exception e)
            {
                VRLog.Warn(Name, $"ProcGen root dump threw (ignored): {e.GetType().Name}: {e.Message}");
            }

            BeginPrefabLoad();
        }

        private void BeginPrefabLoad()
        {
            // Own Addressables handle (NOT AssetBundleManager.LoadAssetFromBundle): the game's
            // wrapper caches its handle in a session-lifetime dictionary and WaitForCompletion
            // would stall the menu frame; a mod-held handle polls async and can be Released.
            _prefabHandle = Addressables.LoadAssetAsync<GameObject>(MapAPrefabPath);
            _prefabHandleHeld = true;
            _phase = Phase.PrefabLoading;
            _deadline = Time.realtimeSinceStartup + 20f;
        }

        private void TickPrefabLoading()
        {
            if (AbortIfMenuGone())
                return;
            if (!_prefabHandle.IsDone)
            {
                if (Time.realtimeSinceStartup > _deadline)
                    FinishProbe("aborted: 'Map A' Addressables load timed out (20 s)");
                return;
            }
            if (_prefabHandle.Status != AsyncOperationStatus.Succeeded || _prefabHandle.Result == null)
            {
                VRLog.Warn(Name, $"Addressables could not load '{MapAPrefabPath}': "
                    + $"status={_prefabHandle.Status}, exception="
                    + (_prefabHandle.OperationException != null ? _prefabHandle.OperationException.Message : "<none>"));
                FinishProbe("aborted: Map A prefab load failed");
                return;
            }

            VRLog.Info(Name, $"'{MapAPrefabPath}' loaded — instantiating 500 m below the floor "
                + "and styling it Dungeon/StoneRooms/Candlelight.");
            BuildInstance(_prefabHandle.Result);
        }

        private void BuildInstance(GameObject prefab)
        {
            // Mutual exclusion with the gaze-driven focus driver FIRST (it re-asserts its own
            // DetailFocus every frame while VR runs). Idempotent, restored in cleanup.
            ApparanceDetailFocus.Uninstall();
            _uninstalledGazeDriver = true;

            _probeRoot = new GameObject(HostName + ".Root");
            Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
            if (pg.IsValid() && pg.isLoaded)
            {
                // Live in the ProcGen scene like the game's own maps do — and die with it.
                try { SceneManager.MoveGameObjectToScene(_probeRoot, pg); }
                catch { /* stays in the active scene — position still hides it */ }
            }
            _probeRoot.transform.position = new Vector3(0f, -500f, 0f);

            _instance = UnityEngine.Object.Instantiate(prefab, _probeRoot.transform);
            _instance.name = HostName + ".MapA";
            _instance.transform.localPosition = Vector3.zero;
            _instance.transform.localEulerAngles = Vector3.zero;

            // NO global render state from the probe: ambience components would set
            // skybox/ambient/fog if any scenario system ever blended them in (StaticAmbience
            // .Apply → RenderSettings, DynamicAmbience → per-camera DynamicFog).
            int muted = 0;
            foreach (StaticAmbience amb in _instance.GetComponentsInChildren<StaticAmbience>(true))
            {
                if (amb != null) { amb.enabled = false; muted++; }
            }
            foreach (DynamicAmbience amb in _instance.GetComponentsInChildren<DynamicAmbience>(true))
            {
                if (amb != null) { amb.enabled = false; muted++; }
            }

            // Style writes — the Choreographer's field-write pattern (Choreographer.cs:14812-14822)
            // applied via ForceValidate, the level editor's runtime apply path
            // (LevelEditorApparancePanel.cs:43: write fields, then ForceValidate → CheckChanges →
            // Rebuild). Written on EVERY style under the instance so nothing depends on parent
            // inheritance (our root has no scenario-level ProceduralStyle above it).
            ProceduralStyle[] styles = _instance.GetComponentsInChildren<ProceduralStyle>(true);
            for (int i = 0; i < styles.Length; i++)
            {
                ProceduralStyle s = styles[i];
                if (s == null)
                    continue;
                s.Biome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.EBiome.Dungeon;
                s.SubBiome = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ESubBiome.StoneRooms;
                s.Tone = ScenarioRuleLibrary.YML.ScenarioPossibleRoom.ETone.Candlelight;
            }
            for (int i = 0; i < styles.Length; i++)
            {
                try { styles[i]?.ForceValidate(); }
                catch { /* one broken style must not stop the rest */ }
            }

            // Walls are populated by the game AFTER corner analysis (ProceduralScenario
            // .SetupWalls sets each wall entity IsPopulated = true); without a scenario nobody
            // does that, so do the minimal version — corner data stays default, which only
            // costs join quality, not the build-or-not verdict.
            int walls = 0;
            foreach (ProceduralWall wall in _instance.GetComponentsInChildren<ProceduralWall>(true))
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

            // Engine detail focus at the map centre — synthesis detail is distance-scaled from
            // this point; without it the 500 m offset would fake a "never built" verdict.
            _focusGo = new GameObject(HostName + ".Focus");
            _focusGo.transform.SetParent(_probeRoot.transform, false);
            _focusGo.transform.localPosition = Vector3.zero;
            TryApplyDetailFocus();

            VRLog.Info(Name, $"Map A instantiated at (0,-500,0): {styles.Length} ProceduralStyle(s) "
                + $"written+validated, {walls} wall entity(ies) populated, {muted} ambience "
                + "component(s) muted. Polling for 30 s (log every 5 s).");

            _phase = Phase.Polling;
            float now = Time.realtimeSinceStartup;
            _nextPoll = now;          // first census immediately
            _pollEnd = now + 30f;
        }

        /// <summary>Point the engine's own detail-focus override at the probe (lazy — the
        /// engine may only appear a few frames after the ProcGen scene loads).</summary>
        private void TryApplyDetailFocus()
        {
            ApparanceEngine? engine = ApparanceEngine.Instance;
            if (engine == null || _focusGo == null || _focusEngine == engine)
                return;
            _focusEngine = engine;
            _origEnableFocus = engine.EnableDetailFocus;
            _origFocus = engine.DetailFocus;
            engine.EnableDetailFocus = true;
            engine.DetailFocus = _focusGo;
            VRLog.Info(Name, "engine detail focus pointed at the probe map "
                + $"(authored EnableDetailFocus={_origEnableFocus}) — restored on cleanup.");
        }

        private void TickPolling()
        {
            if (AbortIfMenuGone())
                return;
            if (Dial != null && !Dial.Value)
            {
                FinishProbe("aborted: [Sky] EnvProbe switched off mid-run");
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now >= _nextPoll)
            {
                _nextPoll = now + 5f;
                TryApplyDetailFocus();
                LogPollCensus(Mathf.RoundToInt(30f - (_pollEnd - now)));
            }

            if (now >= _pollEnd)
                FinishProbe("30 s poll complete");
        }

        private void LogPollCensus(int second)
        {
            ApparanceEngine? engine = ApparanceEngine.Instance;
            if (engine != null)
                _engineSeen = true;

            int entities = 0, busy = 0, populated = 0;
            int containers = 0, renderers = 0, enabledRenderers = 0;
            Bounds bounds = default;
            bool hasBounds = false;
            var shaders = new List<string>(6);

            if (_instance != null)
            {
                ApparanceEntity[] ents = _instance.GetComponentsInChildren<ApparanceEntity>(true);
                entities = ents.Length;
                for (int i = 0; i < ents.Length; i++)
                {
                    ApparanceEntity e = ents[i];
                    if (e == null)
                        continue;
                    try
                    {
                        if (e.IsBusy) busy++;
                        if (e.IsPopulated) populated++;
                    }
                    catch { /* engine tearing down mid-frame */ }
                }

                // Renderer census under every 'Generated Content' container — the success
                // metric the reveal fix already used ("maptile 'L' went Preview/0-renderers →
                // All/229 renderers").
                CensusGeneratedContent(_instance.transform, ref containers, ref renderers,
                    ref enabledRenderers, ref bounds, ref hasBounds, shaders);
            }

            if (renderers > _bestRenderers)
                _bestRenderers = renderers;

            var sb = new StringBuilder(384);
            sb.Append("PROBE t+").Append(second).Append("s: engine=")
              .Append(engine != null ? "ALIVE" : "null")
              .Append("; entities=").Append(entities)
              .Append(" (busy=").Append(busy).Append(", populated=").Append(populated).Append(')')
              .Append("; GeneratedContent: containers=").Append(containers)
              .Append(", renderers=").Append(renderers)
              .Append(" (enabled=").Append(enabledRenderers).Append(')');
            if (hasBounds)
                sb.Append(", bounds size=").Append(bounds.size.ToString("F1"));
            if (shaders.Count > 0)
            {
                sb.Append(", shaders: ");
                for (int i = 0; i < shaders.Count; i++)
                    sb.Append('\'').Append(shaders[i]).Append("' ");
            }
            VRLog.Info(Name, sb.ToString());
        }

        private static void CensusGeneratedContent(Transform root, ref int containers,
            ref int renderers, ref int enabledRenderers, ref Bounds bounds, ref bool hasBounds,
            List<string> shaders)
        {
            // The containers sit a couple of levels under each tile/wall (ProceduralMapTile
            // .ShowContent walks the same name); a full-depth name scan over one small map
            // instance is cheap and runs six times total.
            var stack = new Stack<Transform>(64);
            stack.Push(root);
            while (stack.Count > 0)
            {
                Transform t = stack.Pop();
                if (string.Equals(t.name, "Generated Content", StringComparison.Ordinal))
                {
                    containers++;
                    Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
                    renderers += rs.Length;
                    for (int i = 0; i < rs.Length; i++)
                    {
                        Renderer r = rs[i];
                        if (r == null)
                            continue;
                        if (r.enabled)
                            enabledRenderers++;
                        if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                        else bounds.Encapsulate(r.bounds);
                        if (shaders.Count < 6)
                        {
                            Material m = r.sharedMaterial; // shared — never instantiate materials
                            string? sn = m != null && m.shader != null ? m.shader.name : null;
                            if (sn != null && !shaders.Contains(sn))
                                shaders.Add(sn);
                        }
                    }
                    continue; // no nested Generated Content inside a container
                }
                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }
        }

        private bool AbortIfMenuGone()
        {
            if (IsMenuLoaded() && !IsScenarioLoaded())
                return false;
            FinishProbe("aborted: menu scene left mid-probe");
            return true;
        }

        // ==========================================================================================
        //  Verdict + cleanup
        // ==========================================================================================

        private void FinishProbe(string reason)
        {
            if (_phase == Phase.Done)
                return;

            // The verdict line the whole diagnostic exists for — printed on EVERY exit path.
            string engineState = _engineSeen ? "bootstrapped" : "dead";
            string mapState = _bestRenderers > 0 ? $"dressed with {_bestRenderers} renderers" : "never built";
            string verdict = _engineSeen && _bestRenderers > 0
                ? "viable"
                : "needs scenario-time capture (approach C)";
            VRLog.Note(Name, $"ENV PROBE VERDICT: engine {engineState} in menu; "
                + $"Map A {mapState}; approach B {verdict}. ({reason})");

            CleanupProbe(reason);
            _phase = Phase.Done;
        }

        /// <summary>Destroy everything probe-created, restore everything probe-overridden.
        /// Idempotent; every step individually guarded so one failure cannot strand the rest.</summary>
        private void CleanupProbe(string reason)
        {
            // 1. Engine focus back FIRST (while our focus object still exists), and only if the
            //    field still points at us — a game system that re-pointed it owns it now.
            try
            {
                if (_focusEngine != null && _focusGo != null && _focusEngine.DetailFocus == _focusGo)
                {
                    _focusEngine.EnableDetailFocus = _origEnableFocus;
                    _focusEngine.DetailFocus = _origFocus;
                }
            }
            catch { /* engine already torn down */ }
            _focusEngine = null;
            _origFocus = null;

            // 2. The map instance + root (+ focus child).
            try { if (_instance != null) Destroy(_instance); } catch { }
            _instance = null;
            try { if (_probeRoot != null) Destroy(_probeRoot); } catch { }
            _probeRoot = null;
            _focusGo = null;

            // 3. The Addressables handle.
            try
            {
                if (_prefabHandleHeld && _prefabHandle.IsValid())
                    Addressables.Release(_prefabHandle);
            }
            catch { /* released twice / never acquired */ }
            _prefabHandleHeld = false;

            // 4. The ProcGen scene — only if WE loaded it (a game-loaded one is not ours to drop).
            try
            {
                if (_weLoadedProcGen)
                {
                    Scene pg = SceneManager.GetSceneByName(ProcGenSceneName);
                    if (pg.IsValid() && pg.isLoaded)
                        SceneManager.UnloadSceneAsync(pg);
                }
            }
            catch { /* scene already unloading */ }
            _weLoadedProcGen = false;
            _sceneOp = null;

            // 5. Give the gaze-driven focus driver back (no-op when VR is not running).
            try
            {
                if (_uninstalledGazeDriver)
                    ApparanceDetailFocus.Install();
            }
            catch { }
            _uninstalledGazeDriver = false;

            VRLog.Info(Name, $"probe cleanup complete ({reason}) — instance, focus override, "
                + "Addressables handle and (if probe-loaded) the ProcGen scene are gone.");
        }
    }
}
