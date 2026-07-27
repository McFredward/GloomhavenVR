using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// EXPERIMENTAL runtime static batching — the last untested lever of the 2026-07 judder
/// investigation, and the only one that attacks the measured wall directly.
///
/// <para>WHAT THE MEASUREMENT SAYS. Six hardware sessions eliminated every pixel-cost explanation:
/// an 11x cut in the pixel-sample budget moved nothing, shadows off at minimum quality moved
/// nothing, the depth prepass ~6 %, the culling mask marginal, per-pixel lights ~1 %, the whole
/// mod's CPU 0.47 ms of 21.5. What is left is one number:
/// <c>HeadCamera 21.59 ms (cull 0.08 + submit 21.50) x2 passes</c>. The frame is main-thread
/// DRAW-CALL SUBMISSION, and a scenario submits ~1481 renderers over ~1603 material slots, twice
/// under MultiPass — roughly 3200 draw calls.</para>
///
/// <para>WHY BATCHING IS THE RIGHT LEVER HERE, AND WHY THE OTHER TWO ARE NOT. Those 1481 renderers
/// share only ~103 DISTINCT MATERIALS — 14.4 renderers per material. Merging them is therefore
/// possible in principle, and there are exactly three mechanisms:</para>
/// <list type="bullet">
/// <item>SINGLE-PASS STEREO would halve the passes, not the draws — and it is impossible here: the
/// game's shaders ship with no stereo variants and a player cannot compile one
/// (<see cref="StereoModeConfig"/>).</item>
/// <item>GPU INSTANCING is already enabled on 1568 of the material slots and is buying nothing,
/// because instancing merges renderers that share a material AND A MESH. This dungeon's geometry is
/// generated procedurally per room, so nearly every renderer has a mesh of its own.</item>
/// <item>STATIC BATCHING does not care that the meshes differ — it concatenates them into one
/// vertex buffer, after which consecutive renderers sharing a material collapse into one draw. It
/// needs no shader variant and no mesh authoring. It is the one mechanism the shipped assets cannot
/// veto.</item>
/// </list>
///
/// <para>AND THE GAME'S OWN DEVELOPERS DID EXACTLY THIS. <c>PerformanceUtility.Combine()</c> in the
/// decompiled <c>GH.Runtime</c> calls <c>StaticBatchingUtility.Combine</c> on the scene root named
/// <c>"Maps"</c>, wired to Shift+F2 behind <c>Main.s_DevMode</c>. That is the same root the
/// <c>[Perf] SCENE</c> line shows holding 1440 of the scenario's 1481 submitted renderers. This
/// pass is that call, made switchable, measured, bounded and — the part the hotkey has no answer
/// for — UNDOABLE.</para>
///
/// <para>WHAT COMBINING ACTUALLY COSTS, stated before it is defended:</para>
/// <list type="number">
/// <item>MEMORY. The combined mesh is a full COPY of every vertex in it, in system and video
/// memory, while the originals stay alive so the pass can be undone. Bounded by
/// <see cref="StaticBatchConfig.MaxVertices"/> and reported in the log in MB, not implied.</item>
/// <item>A HITCH. The combine itself is a synchronous copy of millions of vertices. It is
/// scheduled <see cref="StaticBatchConfig.SettleSeconds"/> after a scene load — inside the loading
/// screen — and processes ONE ROOT PER FRAME so the cost is spread rather than stacked. Its real
/// duration is measured and logged every time.</item>
/// <item>COMBINED OBJECTS MUST NOT MOVE. Their vertices are baked into the batch root's space, so
/// a combined object's own transform stops deciding where it is drawn. This is the one way the
/// feature can go visibly wrong, and it gets three independent defences: movers are excluded at
/// scan time (Animator/Animation/Rigidbody anywhere up to the root), a watchdog samples combined
/// objects for movement and can hand everything back on its own, and the offender is named in the
/// log so it can be added to <see cref="StaticBatchConfig.ExcludeNames"/>.</item>
/// </list>
///
/// <para>THE ROLLBACK GUARANTEE. Every combined renderer is recorded in a ledger BEFORE it is
/// touched — its original mesh, its additional vertex streams, and the pose it was combined at —
/// and the undo restores each one and destroys the combined meshes.
/// <see cref="StaticBatchInterop"/> owns the two internal writes Unity performs and this mod has to
/// invert; if either cannot be resolved, <see cref="BatchMode.On"/> silently
/// degrades to <see cref="BatchMode.Probe"/> and NOTHING is mutated. There is no path on which this
/// combines something it cannot hand back, which is the standing rule every game-object mutation in
/// this mod lives under.</para>
///
/// <para>MULTIPLAYER. Purely local rendering. No game state, no wire byte, no card identity. Two
/// peers with different settings play the same game; one submits fewer draw calls.</para>
///
/// <para>NEVER IN THE PRE-MENU SCENES. Same gate as <see cref="PerfSceneProfile"/>, for the same
/// reason it was added there: a heavy walk that runs before the settings pane exists cannot be
/// switched off from inside the headset.</para>
/// </summary>
internal static class StaticBatcher
{
    // ==========================================================================================
    //  Bounds — every loop in this file is bounded by one of these or by a collection's own Count
    // ==========================================================================================

    /// <summary>Ceiling on the ancestor walk in <see cref="IsUnderMover"/>. Unreachable in practice.</summary>
    private const int MaxHierarchyDepth = 256;

    /// <summary>Combined objects the watchdog re-poses per check (round-robin over the ledger).</summary>
    private const int WatchSampleSize = 64;

    /// <summary>Seconds between watchdog checks.</summary>
    private const float WatchInterval = 1f;

    /// <summary>Bytes per vertex assumed when the real stride cannot be read (position+normal+tangent+uv).</summary>
    private const int FallbackVertexStride = 44;

    /// <summary>Delay used when a pass is requested by hand rather than by a scene load.</summary>
    private const float ManualDelay = 0.25f;

    /// <summary>
    /// Watchdog undos in one scene after which the pass gives up on it entirely.
    ///
    /// <para>WITHOUT THIS THERE IS AN OSCILLATION, and it is the worst failure this feature could
    /// have: a scenario holding one moving object would batch, be undone a second later, be
    /// re-batched by the next rescan, be undone again — a combine hitch every thirty seconds
    /// forever, with a log nobody could read. Giving up after a few rounds turns that into one
    /// clear verdict and a stable picture. It resets on a scene load and on a mode change, so the
    /// giving-up is per scenario rather than for the session.</para>
    /// </summary>
    private const int MaxWatchdogRevertsPerScene = 3;

    /// <summary>A combine slower than this is called out as a hitch worth knowing about.</summary>
    private const double SlowCombineMs = 500d;

    /// <summary>Ceiling on roots adopted by auto-detection when no configured name matched.</summary>
    private const int MaxAutoRoots = 4;

    /// <summary>
    /// Mesh objects a root needs before AUTO-detection will adopt it — deliberately far above
    /// <see cref="StaticBatchConfig.MinRenderers"/>.
    ///
    /// <para>A NAMED root was chosen by a person and needs only to be worth the memory. An
    /// auto-detected one was chosen by a heuristic, and the heuristic runs in EVERY scene where the
    /// configured name is absent — which includes the main menu, whose 3D backdrop would otherwise
    /// qualify on a couple of dozen objects and earn a combine hitch for nothing. The target this
    /// exists to find carries ~1440; a bar of 200 clears it by a factor of seven and clears menu
    /// scenery by a comfortable margin in the other direction.</para>
    /// </summary>
    private const int MinAutoRootMeshes = 200;

    /// <summary>Scene roots named on the PROBE line when nothing matched.</summary>
    private const int SurveyRootsShown = 8;

    // ==========================================================================================
    //  The ledger — one entry per combined renderer, written BEFORE the combine
    // ==========================================================================================

    private sealed class Entry
    {
        public MeshFilter Filter = null!;
        public Renderer Renderer = null!;

        /// <summary>The mesh the object drew before the combine. The whole point of the ledger.</summary>
        public Mesh Original = null!;

        /// <summary>MakeBatch nulls these two; captured so the undo can put them back.</summary>
        public Mesh? AdditionalStreams;
        public Mesh? EnlightenStream;

        /// <summary>The batch root — the space the combined vertices were baked into.</summary>
        public Transform Root = null!;

        /// <summary>Pose relative to <see cref="Root"/> at combine time (the watchdog's baseline).</summary>
        public Matrix4x4 RootLocal;
    }

    private static readonly List<Entry> Ledger = new(2048);

    /// <summary>The meshes THIS pass created, so the undo destroys exactly those and nothing else.</summary>
    private static readonly List<Mesh> CombinedMeshes = new(32);
    private static readonly HashSet<int> CombinedIds = new(32);

    // ==========================================================================================
    //  Scan scratch — allocated once, reused every pass
    // ==========================================================================================

    /// <summary>
    /// One root's scan result. <see cref="Filters"/> is the SINGLE authoritative list of what may be
    /// combined — there used to be a parallel list of the same objects' GameObjects, which is
    /// exactly the two-lists-that-can-disagree shape <see cref="CombineRoot"/> guards against, so it
    /// is gone and the array handed to Unity is derived from the ledger instead.
    /// </summary>
    private sealed class RootScan
    {
        public Transform Root = null!;
        public string Name = string.Empty;
        public readonly List<MeshFilter> Filters = new(1024);
        public int Vertices;
        public int MaterialSlots;

        public void Reset()
        {
            Filters.Clear();
            Vertices = 0;
            MaterialSlots = 0;
        }
    }

    private static readonly List<RootScan> Scans = new(4);
    private static readonly List<RootScan> PendingCombines = new(4);
    private static readonly List<GameObject> SceneRootScratch = new(64);

    /// <summary>The objects actually handed to Unity — built from the ledger, see CombineRoot.</summary>
    private static readonly List<GameObject> CombineScratch = new(1024);

    /// <summary>Scene roots and their mesh counts, filled only when no configured name matched.</summary>
    private static readonly List<KeyValuePair<GameObject, int>> RootSurvey = new(32);

    private static readonly List<Material> MaterialScratch = new(8);
    private static readonly List<Transform> ChainScratch = new(32);
    private static readonly Dictionary<int, bool> MoverCache = new(512);
    private static readonly Dictionary<int, bool> ShaderCache = new(64);
    private static readonly HashSet<int> DistinctMaterials = new(256);
    private static readonly Dictionary<int, HashSet<int>> MaterialsPerBatch = new(32);
    private static readonly List<string> ExcludeNameList = new(8);
    private static readonly List<string> ExcludeComponentList = new(8);

    // ==========================================================================================
    //  State
    // ==========================================================================================

    private static BatchDriver? _driver;
    private static bool _installed;

    private static BatchMode _lastMode = BatchMode.Off;
    private static float _scheduledAt;          // unscaledTime of the next pass; 0 = none scheduled
    private static string _scheduleReason = string.Empty;
    private static float _nextRescan;
    private static float _nextWatch;
    private static int _watchCursor;

    /// <summary>Watchdog undos in the current scene (see <see cref="MaxWatchdogRevertsPerScene"/>).</summary>
    private static int _watchdogReverts;

    /// <summary>Set when this scene has been given up on; blocks every further pass until reset.</summary>
    private static bool _suspended;

    // ---- what the settings panel reads --------------------------------------------------------

    /// <summary>True while at least one renderer is combined (i.e. there is something to undo).</summary>
    internal static bool Applied => Ledger.Count > 0;

    /// <summary>Renderers currently combined.</summary>
    internal static int BatchedRenderers => Ledger.Count;

    /// <summary>Combined meshes currently alive (one per Unity batch).</summary>
    internal static int CombinedMeshCount => CombinedMeshes.Count;

    /// <summary>Estimated system+video memory of the combined meshes, in MB (one copy each).</summary>
    internal static float MegabytesPerCopy { get; private set; }

    /// <summary>Material slots the combined renderers cost BEFORE the pass (one eye pass).</summary>
    internal static int DrawCallsBefore { get; private set; }

    /// <summary>Estimated floor AFTER the pass: distinct materials per batch, plus what cannot merge.</summary>
    internal static int DrawCallsAfter { get; private set; }

    /// <summary>Renderers the last scan found eligible.</summary>
    internal static int LastEligible { get; private set; }

    /// <summary>Renderers the last scan looked at.</summary>
    internal static int LastScanned { get; private set; }

    /// <summary>True once a scan has run this session (so the panel can tell "0" from "not yet").</summary>
    internal static bool HasProbed { get; private set; }

    /// <summary>Short neutral one-liner for the panel's status row (numbers, no prose).</summary>
    internal static string ShortStatus { get; private set; } = "-";

    /// <summary>
    /// True while this scene has been given up on after repeated watchdog undos. Surfaced in the
    /// panel because a pass that has quietly stopped trying looks exactly like one that is off,
    /// and the log is not something a player reads mid-scenario.
    /// </summary>
    internal static bool Suspended => _suspended;

    /// <summary>Why the pass is not combining, or empty when nothing is in the way.</summary>
    internal static string Blocker
    {
        get
        {
            if (!StaticBatchInterop.CanRevert)
                return StaticBatchInterop.Status;
            return string.Empty;
        }
    }

    // ==========================================================================================
    //  Install / shutdown
    // ==========================================================================================

    /// <summary>
    /// Attach the driver to the mod's own hardened Core host. Idempotent. Resolving the interop
    /// here (rather than at the first combine) means the log says whether the feature is even
    /// available at startup, and the settings panel can say so before anyone switches it on.
    /// </summary>
    internal static void Install(GameObject host)
    {
        if (_installed || host == null)
            return;
        _installed = true;

        StaticBatchConfig.Bind();
        StaticBatchInterop.Resolve();

        _driver = host.AddComponent<BatchDriver>();
        SceneManager.sceneLoaded += OnSceneLoaded;

        _lastMode = StaticBatchConfig.CurrentMode;
        if (_lastMode != BatchMode.Off)
            Schedule(StaticBatchConfig.Settle, "startup with the pass already switched on");

        VRLog.Info("Batch", $"Static batching installed, mode {_lastMode}. "
                            + "EXPERIMENTAL and OFF by default — it is the only lever in this "
                            + "investigation that mutates game objects. Mode = Probe measures "
                            + "without touching anything; Mode = On combines. Grep '[Batch]'.");
    }

    /// <summary>Hand everything back and detach (hot-reload / plugin shutdown).</summary>
    internal static void Uninstall()
    {
        if (!_installed)
            return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Revert("the mod is shutting down");
        if (_driver != null)
            UnityEngine.Object.Destroy(_driver);
        _driver = null;
        _installed = false;
        _scheduledAt = 0f;
        PendingCombines.Clear();
    }

    /// <summary>
    /// A scene change invalidates the whole ledger: a SINGLE load has already destroyed everything
    /// in it, so the undo can only destroy the combined meshes; an ADDITIVE load leaves the batched
    /// objects alive, so the ledger MUST survive or they would be stuck combined with no way back.
    /// </summary>
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
        {
            Revert("a new scene was loaded (the previous scene's objects are gone)");
            // "Given up on" is per SCENARIO, not per session: the moving object that caused it
            // belongs to the map that just went away.
            _watchdogReverts = 0;
            _suspended = false;
        }
        PendingCombines.Clear();
        if (StaticBatchConfig.CurrentMode != BatchMode.Off)
            Schedule(StaticBatchConfig.Settle, $"scene '{scene.name}' loaded ({mode})");
    }

    /// <summary>Ask for a pass. The settings panel's "apply now" button and every internal trigger.</summary>
    internal static void RequestPass(string reason) => Schedule(ManualDelay, reason);

    /// <summary>Undo from outside (the settings panel's button).</summary>
    internal static void RequestRevert(string reason) => Revert(reason);

    private static void Schedule(float delay, string reason)
    {
        _scheduledAt = Time.unscaledTime + Mathf.Max(0f, delay);
        _scheduleReason = reason;
    }

    // ==========================================================================================
    //  Per-frame
    // ==========================================================================================

    /// <summary>
    /// One frame's worth of work. Everything below is a compare-and-return in the common case:
    /// nothing is scheduled, nothing is pending, and the two timers are not due.
    /// </summary>
    private static void Tick()
    {
        StaticBatchConfig.Bind();
        BatchMode mode = StaticBatchConfig.CurrentMode;

        if (mode != _lastMode)
        {
            OnModeChanged(_lastMode, mode);
            _lastMode = mode;
        }

        if (mode == BatchMode.Off)
            return;

        // The mod does not touch the flat game. Nothing here is worth anything without a headset,
        // and combining while VR is down would leave a mutation nobody asked for.
        if (!VRSession.IsRunning)
        {
            if (Applied)
                Revert("the VR session is no longer running");
            return;
        }

        if (PerfSceneProfile.IsPreMenuScene())
            return;

        // Given up on this scene (see MaxWatchdogRevertsPerScene). Nothing is combined and nothing
        // will be until the scenario changes or the mode is cycled — deliberately including the
        // rescan, which is exactly the half of the oscillation this has to break.
        if (_suspended)
        {
            _scheduledAt = 0f;
            return;
        }

        float now = Time.unscaledTime;

        // A queued root is combined one per frame, so a large map costs several small hitches
        // instead of one long freeze.
        if (PendingCombines.Count > 0)
        {
            RootScan scan = PendingCombines[0];
            PendingCombines.RemoveAt(0);
            CombineRoot(scan);
            // Guarded on the ledger too: a combine that threw calls Revert, which empties BOTH
            // lists — reporting an "applied" pass there would describe a batch that does not exist.
            if (PendingCombines.Count == 0 && Ledger.Count > 0)
                ReportApplied();
            return;
        }

        if (_scheduledAt > 0f && now >= _scheduledAt)
        {
            _scheduledAt = 0f;
            RunPass(mode, _scheduleReason);
            return;
        }

        if (Applied && StaticBatchConfig.WatchdogOn && now >= _nextWatch)
        {
            _nextWatch = now + WatchInterval;
            TickWatchdog();
            return;
        }

        float rescan = StaticBatchConfig.Rescan;
        if (Applied && rescan > 0f && now >= _nextRescan)
        {
            _nextRescan = now + rescan;
            TickRescan();
        }
    }

    private static void OnModeChanged(BatchMode from, BatchMode to)
    {
        VRLog.Info("Batch", $"Mode {from} → {to}.");
        PerfMonitor.MarkChange($"static batching {from} → {to} — A/B boundary");
        // Cycling the mode is the deliberate "try again" gesture, so it clears the give-up state —
        // that is what makes an ExcludeNames edit testable without reloading the scenario.
        _watchdogReverts = 0;
        _suspended = false;

        // Probe promises it mutates nothing, so switching DOWN to it has to hand back whatever On
        // took. Off does the same. Only a move away from Off schedules work.
        if (to != BatchMode.On && Applied)
            Revert($"the mode was set to {to}");

        if (to == BatchMode.Off)
        {
            _scheduledAt = 0f;
            PendingCombines.Clear();
            return;
        }
        Schedule(ManualDelay, $"the mode was set to {to}");
    }

    // ==========================================================================================
    //  The pass: scan → report → (optionally) queue the combines
    // ==========================================================================================

    private static void RunPass(BatchMode mode, string reason)
    {
        var sw = Stopwatch.StartNew();
        int scanned = Scan();
        sw.Stop();

        HasProbed = true;
        LastScanned = scanned;

        int eligible = 0, vertices = 0, slots = 0;
        for (int i = 0; i < Scans.Count; i++)
        {
            eligible += Scans[i].Filters.Count;
            vertices += Scans[i].Vertices;
            slots += Scans[i].MaterialSlots;
        }
        LastEligible = eligible;

        ReportProbe(mode, reason, scanned, eligible, vertices, slots, sw.Elapsed.TotalMilliseconds);
        UpdateShortStatus();

        if (mode != BatchMode.On)
            return;

        if (!StaticBatchInterop.CanRevert)
        {
            VRLog.Warn("Batch", "Mode is ON but the undo path is unavailable — NOT combining. "
                                + $"{StaticBatchInterop.Status}. The pass stays a measurement this "
                                + "session; nothing has been mutated.");
            return;
        }

        int min = StaticBatchConfig.MinRenderersPerRoot;
        for (int i = 0; i < Scans.Count; i++)
        {
            RootScan scan = Scans[i];
            if (scan.Filters.Count < min)
            {
                if (scan.Filters.Count > 0)
                    VRLog.Info("Batch", $"Root '{scan.Name}': {scan.Filters.Count} eligible "
                                        + $"renderer(s) is below MinRenderers ({min}) — skipped.");
                continue;
            }
            PendingCombines.Add(scan);
        }

        if (PendingCombines.Count == 0)
            VRLog.Info("Batch", "Nothing to combine — no root cleared MinRenderers. "
                                + "The [Batch] PROBE line above says why.");
    }

    /// <summary>
    /// Walk the configured roots and fill <see cref="Scans"/> with everything Unity would accept.
    /// Returns how many MeshFilters were LOOKED AT (the denominator of every rejection count).
    /// Never throws: an instrumentation-grade walk that takes the frame down with it is strictly
    /// worse than a missing measurement.
    /// </summary>
    private static int Scan()
    {
        Scans.Clear();
        // Cleared HERE, not in SurveyRoots: the survey only runs when no configured name matched,
        // so leaving it would let a successful pass report the previous failed pass's candidates.
        RootSurvey.Clear();
        MoverCache.Clear();
        ShaderCache.Clear();
        DistinctMaterials.Clear();
        ResetRejections();
        ParseExclusions();

        int excludeMask = ResolveExcludedLayerMask();
        int budget = StaticBatchConfig.VertexBudget;
        bool inactive = StaticBatchConfig.Inactive;
        int scanned = 0;
        int spent = 0;

        ResolveRoots();

        for (int r = 0; r < Scans.Count; r++)
        {
            RootScan scan = Scans[r];
            MeshFilter[] filters;
            try
            {
                filters = scan.Root.GetComponentsInChildren<MeshFilter>(inactive);
            }
            catch (Exception e)
            {
                VRLog.Warn("Batch", $"Root '{scan.Name}' could not be walked ({e.GetType().Name}) — skipped.");
                continue;
            }

            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter mf = filters[i];
                if (mf == null)
                    continue;
                scanned++;
                if (!IsEligible(mf, scan, excludeMask, out Mesh? mesh, out int slots))
                    continue;

                // The vertex budget is the memory guard, and it is spent in scan order across ALL
                // roots — not per root — so the ceiling in the config is the ceiling in reality.
                if (spent + mesh!.vertexCount > budget)
                {
                    _rejBudget++;
                    continue;
                }
                spent += mesh.vertexCount;

                // The distinct-material tally is taken HERE — after every rejection, so the ratio
                // on the PROBE line describes the renderers that would actually be combined rather
                // than the ones that were merely looked at.
                TallyMaterials(slots);

                scan.Filters.Add(mf);
                scan.Vertices += mesh.vertexCount;
                scan.MaterialSlots += slots;
            }
        }

        return scanned;
    }

    /// <summary>
    /// Resolve <see cref="StaticBatchConfig.Roots"/> against the root objects of every loaded
    /// scene. Reading the scenes' own root lists is a handful of objects — deliberately not
    /// <c>FindObjectsOfType&lt;GameObject&gt;</c>, which is what Unity's own
    /// <c>StaticBatchingUtility.Combine(root)</c> overload does and what makes it heavy.
    /// </summary>
    private static void ResolveRoots()
    {
        string spec = StaticBatchConfig.RootNames;
        // An EMPTY entry falls through to the survey and the auto-detect below rather than
        // returning here. "No configured name matches" is exactly what an empty list means, and
        // AutoDetectRoots is a separate, explicit switch — so an empty Roots with auto-detect ON
        // has to behave like a wrong Roots with auto-detect on, not like a silent off switch.
        string[] wanted = string.IsNullOrWhiteSpace(spec) ? Array.Empty<string>() : spec.Split(',');

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene;
            try
            {
                scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                SceneRootScratch.Clear();
                scene.GetRootGameObjects(SceneRootScratch);
            }
            catch (Exception)
            {
                continue;
            }

            for (int i = 0; i < SceneRootScratch.Count; i++)
            {
                GameObject go = SceneRootScratch[i];
                if (go == null)
                    continue;
                string name = go.name;
                for (int w = 0; w < wanted.Length; w++)
                {
                    string token = wanted[w].Trim();
                    if (token.Length == 0 || !string.Equals(token, name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    var scan = new RootScan { Root = go.transform, Name = name };
                    scan.Reset();
                    Scans.Add(scan);
                    break;
                }
            }
        }

        if (Scans.Count > 0)
            return;

        // Nothing matched. Two things have to happen here, and neither is optional for a feature
        // that is tested by handing a build to someone and reading the log afterwards.
        SurveyRoots();
        if (StaticBatchConfig.AutoRoots)
            AutoDetectRoots();

        // INFO, not a warning: outside a scenario there IS no dungeon root, and that is the normal
        // state in every menu — a warning per scene load would train the reader to skim past it.
        // A genuine typo in the entry produces this same line, which is why it names the spec.
        if (Scans.Count == 0)
            VRLog.Info("Batch", $"None of the configured roots ({spec}) exists in the loaded "
                                + "scene(s) and nothing was auto-detected — nothing to do. Normal "
                                + "outside a scenario. The PROBE line below lists the roots that ARE "
                                + "there with their mesh counts, which is where the right name for "
                                + "[Batching] Roots comes from.");
    }

    /// <summary>
    /// Every scene root with its mesh-object count, recorded for the PROBE line.
    ///
    /// <para>This exists because of a usability dead end, not for completeness: <c>Roots</c> is free
    /// TEXT, and free text is the one control shape the in-VR config browser can only display. A
    /// player whose scenario names its geometry root something other than "Maps" therefore cannot
    /// fix it from inside the headset — so the log has to hand them the answer, in the same line
    /// that tells them there was a problem.</para>
    /// </summary>
    private static void SurveyRoots()
    {
        // NOT cleared here — Scan() does it, and it has to, because this method only runs when no
        // configured name matched. Clearing in both places would read as belt-and-braces and hide
        // which of the two is load-bearing.
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            try
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                SceneRootScratch.Clear();
                scene.GetRootGameObjects(SceneRootScratch);
            }
            catch (Exception)
            {
                continue;
            }

            for (int i = 0; i < SceneRootScratch.Count; i++)
            {
                GameObject go = SceneRootScratch[i];
                if (go == null || IsModOwnedRoot(go.name))
                    continue;
                int meshes;
                try
                {
                    meshes = go.GetComponentsInChildren<MeshFilter>(includeInactive: true).Length;
                }
                catch (Exception)
                {
                    continue;
                }
                if (meshes > 0)
                    RootSurvey.Add(new KeyValuePair<GameObject, int>(go, meshes));
            }
        }
        RootSurvey.Sort(static (a, b) => b.Value.CompareTo(a.Value));
    }

    /// <summary>
    /// Adopt the busiest surveyed roots. Bounded three ways — the mod's own roots are skipped, a
    /// candidate has to clear <see cref="StaticBatchConfig.MinRenderers"/>, and at most
    /// <see cref="MaxAutoRoots"/> are taken — and every choice is named in the log, so an
    /// auto-detected pass is a reported decision rather than a silent one.
    /// </summary>
    private static void AutoDetectRoots()
    {
        int min = Mathf.Max(StaticBatchConfig.MinRenderersPerRoot, MinAutoRootMeshes);
        for (int i = 0; i < RootSurvey.Count && Scans.Count < MaxAutoRoots; i++)
        {
            if (RootSurvey[i].Value < min)
                break; // sorted descending — everything after this is smaller too
            GameObject go = RootSurvey[i].Key;
            var scan = new RootScan { Root = go.transform, Name = go.name };
            scan.Reset();
            Scans.Add(scan);
        }

        if (Scans.Count == 0)
            return;

        Line.Clear();
        for (int i = 0; i < Scans.Count; i++)
            Line.Append(i == 0 ? "" : ", ").Append('\'').Append(Scans[i].Name).Append('\'');
        VRLog.Info("Batch", $"AUTO-DETECTED {Scans.Count} root(s): {Line} (at least "
                            + $"{MinAutoRootMeshes} mesh objects each — the bar for an auto-detected "
                            + "root is deliberately far above MinRenderers so menu scenery cannot "
                            + $"qualify). None of the configured names ({StaticBatchConfig.RootNames}) exists here, and "
                            + "[Batching] AutoDetectRoots is on. To pin this down, copy the name(s) "
                            + "above into [Batching] Roots in dev.gloomhavenvr.batching.cfg — the "
                            + "PROBE line below lists every candidate that was considered.");
    }

    /// <summary>The mod's own scene roots, which auto-detection must never adopt.</summary>
    private static bool IsModOwnedRoot(string name) =>
        name.StartsWith("GloomhavenVR", StringComparison.Ordinal);

    // ---- eligibility: Unity's own rules, checked in cheapest-first order ----------------------

    private static int _rejNoMesh, _rejUnreadable, _rejNotBatchable, _rejEmptyMesh;
    private static int _rejNoRenderer, _rejDisabled, _rejAlreadyBatched, _rejShader;
    private static int _rejMover, _rejLayer, _rejName, _rejBudget, _rejStreams;

    private static void ResetRejections()
    {
        _rejNoMesh = _rejUnreadable = _rejNotBatchable = _rejEmptyMesh = 0;
        _rejNoRenderer = _rejDisabled = _rejAlreadyBatched = _rejShader = 0;
        _rejMover = _rejLayer = _rejName = _rejBudget = _rejStreams = 0;
    }

    /// <summary>
    /// Every test <c>InternalStaticBatchingUtility.CombineGameObjects</c> applies, plus the three
    /// this mod adds (layer, name, mover). Ordered cheapest first so the expensive component walk
    /// only runs for objects that would otherwise be accepted.
    /// </summary>
    private static bool IsEligible(MeshFilter mf, RootScan scan, int excludeMask,
        out Mesh? mesh, out int slots)
    {
        mesh = null;
        slots = 0;
        GameObject go = mf.gameObject;

        // --- the mod's own rules -------------------------------------------------------------
        if ((excludeMask & (1 << go.layer)) != 0)
        {
            _rejLayer++;
            Reject(go, "layer excluded");
            return false;
        }
        if (ExcludeNameList.Count > 0 && MatchesExcludedName(go.name))
        {
            _rejName++;
            Reject(go, "name excluded");
            return false;
        }

        // --- Unity's rules, mesh side ---------------------------------------------------------
        Mesh? shared = mf.sharedMesh;
        if (shared == null)
        {
            _rejNoMesh++;
            return false;
        }
        // THE ONE TEST THAT CAN KILL THE WHOLE FEATURE. A mesh whose Read/Write was disabled at
        // import has no CPU-side copy, so nothing can concatenate it — Unity silently skips it
        // rather than erroring. This game's IMPORTED meshes are like that (the world-map parchment
        // is documented as isReadable=false in FlatScreenStereo.3.Map), while the dungeon's are
        // GENERATED at runtime and are therefore readable. That is a prediction, and the PROBE
        // line's count for this rejection is what turns it into a measurement.
        if (!shared.isReadable)
        {
            _rejUnreadable++;
            Reject(go, "mesh is not readable (Read/Write disabled)");
            return false;
        }
        if (shared.vertexCount == 0)
        {
            _rejEmptyMesh++;
            return false;
        }
        if (!StaticBatchInterop.IsMeshBatchable(shared))
        {
            _rejNotBatchable++;
            Reject(go, "Unity's IsMeshBatchable said no");
            return false;
        }

        // --- Unity's rules, renderer side -----------------------------------------------------
        // MeshRenderer specifically: Unity accepts any Renderer sharing the GameObject with a
        // MeshFilter, but every other renderer type on one would be a construction this mod has
        // never seen and has no business being the first to combine.
        if (!go.TryGetComponent(out MeshRenderer mr) || mr == null)
        {
            _rejNoRenderer++;
            return false;
        }
        if (!mr.enabled)
        {
            _rejDisabled++;
            return false;
        }
        if (mr.isPartOfStaticBatch)
        {
            _rejAlreadyBatched++;
            return false;
        }
        // MakeBatch nulls these two streams, and Unity refuses any object whose stream length
        // disagrees with the mesh. Both are Enlighten/lightmapping artefacts this game does not
        // appear to use; excluded rather than reasoned about.
        if (mr.additionalVertexStreams != null
            && mr.additionalVertexStreams.vertexCount != shared.vertexCount)
        {
            _rejStreams++;
            return false;
        }

        try
        {
            mr.GetSharedMaterials(MaterialScratch);
        }
        catch (Exception)
        {
            _rejNoRenderer++;
            return false;
        }
        for (int m = 0; m < MaterialScratch.Count; m++)
        {
            Material? mat = MaterialScratch[m];
            if (mat == null || mat.shader == null)
                continue;
            if (ShaderRefusesBatching(mat.shader))
            {
                _rejShader++;
                Reject(go, $"shader '{mat.shader.name}' sets DisableBatching");
                return false;
            }
        }
        // Unity draws min(materials, submeshes) slices — the same arithmetic CombineGameObjects
        // uses, so the "before" figure below is the count that actually gets submitted.
        slots = Mathf.Min(MaterialScratch.Count, shared.subMeshCount);

        // --- the expensive one, last ----------------------------------------------------------
        if (IsUnderMover(go.transform, scan.Root))
        {
            _rejMover++;
            Reject(go, "it or an ancestor is animated / physics-driven");
            return false;
        }

        mesh = shared;
        return true;
    }

    /// <summary>
    /// Fold the accepted renderer's materials into the distinct-material set. Reads
    /// <see cref="MaterialScratch"/>, which <see cref="IsEligible"/> has just filled for this
    /// renderer and nothing touches in between — so this costs no second GetSharedMaterials.
    /// </summary>
    private static void TallyMaterials(int slots)
    {
        for (int m = 0; m < slots && m < MaterialScratch.Count; m++)
        {
            Material? mat = MaterialScratch[m];
            if (mat != null)
                DistinctMaterials.Add(mat.GetInstanceID());
        }
    }

    private static void Reject(GameObject go, string why)
    {
        if (StaticBatchConfig.Verbose)
            VRLog.Info("Batch", $"skip '{go.name}': {why}.");
    }

    /// <summary>Shader batching veto, memoized per shader instance (≈100 distinct, ≈1600 reads).</summary>
    private static bool ShaderRefusesBatching(Shader shader)
    {
        int id = shader.GetInstanceID();
        if (ShaderCache.TryGetValue(id, out bool known))
            return known;
        bool refuses = StaticBatchInterop.ShaderDisablesBatching(shader);
        ShaderCache[id] = refuses;
        return refuses;
    }

    /// <summary>
    /// True when this transform or any ancestor UP TO (and including) the batch root carries a
    /// component that drives a transform. Memoized over the chain, so a 1500-object subtree costs
    /// one walk per distinct ancestry rather than one per object.
    ///
    /// <para>THE WALK MUST ADVANCE. Written with the hang that <see cref="PerfSceneProfile"/>'s
    /// first version shipped firmly in mind: <c>node</c> is reassigned every turn, the loop is
    /// depth-bounded, and the bound is not a correctness device — Unity forbids transform cycles,
    /// so it can only ever cost a wrong answer instead of a frozen main thread.</para>
    /// </summary>
    private static bool IsUnderMover(Transform start, Transform root)
    {
        ChainScratch.Clear();
        Transform? node = start;
        bool mover = false;

        for (int depth = 0; depth < MaxHierarchyDepth && node != null; depth++)
        {
            int id = node.GetInstanceID();
            if (MoverCache.TryGetValue(id, out bool known))
            {
                mover = known;
                break;
            }
            ChainScratch.Add(node);
            if (IsMoverNode(node))
            {
                mover = true;
                break;
            }
            if (ReferenceEquals(node, root))
                break;
            node = node.parent;
        }

        for (int i = 0; i < ChainScratch.Count; i++)
            MoverCache[ChainScratch[i].GetInstanceID()] = mover;
        return mover;
    }

    /// <summary>
    /// The three components that mean "something writes this transform": an Animator, a legacy
    /// Animation, or a Rigidbody. They are hard-coded rather than configured because they are not a
    /// preference — a combined object whose transform is driven renders at the pose it was combined
    /// at, which is precisely the failure this whole feature has to avoid.
    /// <see cref="StaticBatchConfig.ExcludeComponents"/> adds to them by type name.
    /// </summary>
    private static bool IsMoverNode(Transform node)
    {
        GameObject go = node.gameObject;
        if (go.TryGetComponent<Animator>(out _)
            || go.TryGetComponent<Animation>(out _)
            || go.TryGetComponent<Rigidbody>(out _))
        {
            return true;
        }
        if (ExcludeComponentList.Count == 0)
            return false;

        Component[] components;
        try
        {
            components = go.GetComponents<Component>();
        }
        catch (Exception)
        {
            return false;
        }
        for (int i = 0; i < components.Length; i++)
        {
            Component c = components[i];
            if (c == null)
                continue;
            string type = c.GetType().Name;
            for (int e = 0; e < ExcludeComponentList.Count; e++)
            {
                if (string.Equals(type, ExcludeComponentList[e], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static bool MatchesExcludedName(string name)
    {
        for (int i = 0; i < ExcludeNameList.Count; i++)
        {
            if (name.IndexOf(ExcludeNameList[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static void ParseExclusions()
    {
        ExcludeNameList.Clear();
        Split(StaticBatchConfig.ExcludeNames?.Value, ExcludeNameList);
        ExcludeComponentList.Clear();
        Split(StaticBatchConfig.ExcludeComponents?.Value, ExcludeComponentList);
    }

    private static void Split(string? spec, List<string> into)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return;
        string[] parts = spec!.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i].Trim();
            if (token.Length > 0)
                into.Add(token);
        }
    }

    /// <summary>
    /// Layers never combined. The mod's OWN layer is ORed in unconditionally and cannot be removed:
    /// combining the hands, cards or control board would freeze them at the pose they were combined
    /// at, and a config typo does not get to break the mod's own embodiment.
    /// </summary>
    private static int ResolveExcludedLayerMask()
    {
        int mask = VRLayers.ModLayerMask;
        string spec = StaticBatchConfig.ExcludeLayers?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(spec))
            return mask;

        string[] parts = spec.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            string token = parts[i].Trim();
            if (token.Length == 0)
                continue;
            if (!int.TryParse(token, out int layer))
                layer = LayerMask.NameToLayer(token);
            if (layer < 0 || layer > 31)
            {
                VRLog.Warn("Batch", $"[Batching] ExcludeLayers: no layer '{token}' — ignored. The "
                                    + "[Perf] SCENE line lists every layer by name.");
                continue;
            }
            mask |= 1 << layer;
        }
        return mask;
    }

    // ==========================================================================================
    //  Combine — one root, one frame
    // ==========================================================================================

    private static void CombineRoot(RootScan scan)
    {
        if (scan.Root == null || scan.Filters.Count == 0)
            return;

        int ledgerStart = Ledger.Count;
        Transform root = scan.Root;

        // THE INVARIANT THIS LOOP EXISTS TO MAKE STRUCTURAL: the array handed to Unity is built
        // FROM the ledger entries, not from the scan's candidate list. Several frames can pass
        // between the scan and this combine (roots are processed one per frame), and in that gap an
        // object can be destroyed or lose its renderer. Passing the scan's list and recording the
        // ledger separately would allow the two to disagree — and an object Unity combines that the
        // ledger does not cover is precisely the thing this feature promises cannot exist. Building
        // one from the other means they cannot differ, whatever happened in between.
        CombineScratch.Clear();
        for (int i = 0; i < scan.Filters.Count; i++)
        {
            MeshFilter mf = scan.Filters[i];
            if (mf == null || mf.sharedMesh == null)
                continue;
            if (!mf.gameObject.TryGetComponent(out MeshRenderer mr) || mr == null)
                continue;
            Ledger.Add(new Entry
            {
                Filter = mf,
                Renderer = mr,
                Original = mf.sharedMesh,
                AdditionalStreams = mr.additionalVertexStreams,
                EnlightenStream = StaticBatchInterop.ReadEnlightenStream(mr),
                Root = root,
            });
            CombineScratch.Add(mf.gameObject);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            StaticBatchingUtility.Combine(CombineScratch.ToArray(), root.gameObject);
        }
        catch (Exception ex)
        {
            sw.Stop();
            VRLog.Error("Batch", $"StaticBatchingUtility.Combine threw on root '{scan.Name}': "
                                 + $"{ex.GetType().Name}: {ex.Message}. Handing back everything this "
                                 + "pass recorded.");
            Revert("the combine threw");
            return;
        }
        sw.Stop();

        // RECONCILE AGAINST REALITY. Unity applies its own eligibility tests inside Combine and
        // drops a whole batch that ended up with fewer than two members. An entry whose mesh did
        // not change was never combined, so it must not sit in the ledger claiming it was — the
        // undo would then "restore" a mesh that was never replaced and clear batch state that was
        // never set. This is also what makes the mod's own eligibility prediction safe to be
        // optimistic: reality gets the last word.
        int combined = 0, declined = 0;
        for (int i = Ledger.Count - 1; i >= ledgerStart; i--)
        {
            Entry e = Ledger[i];
            if (e.Filter == null || e.Renderer == null || e.Filter.sharedMesh == null
                || e.Filter.sharedMesh.GetInstanceID() == e.Original.GetInstanceID())
            {
                Ledger.RemoveAt(i);
                declined++;
                continue;
            }
            e.RootLocal = root.worldToLocalMatrix * e.Filter.transform.localToWorldMatrix;
            RegisterCombinedMesh(e.Filter.sharedMesh);
            combined++;
        }

        double ms = sw.Elapsed.TotalMilliseconds;
        string hitch = ms >= SlowCombineMs
            // Named rather than buried: a freeze this long in a headset is something the player
            // FELT, and a log that does not account for it invites them to blame the wrong thing.
            // The lever is the vertex budget, so the line says so instead of just reporting a number.
            ? $" THAT WAS A VISIBLE FREEZE ({ms:F0} ms) — one-off, not per frame, and it lands in "
              + "the loading screen at the default SettleSeconds. Lower [Batching] MaxVertices "
              + "(Speichergrenze in the panel) to shorten it at the price of batching less of the map."
            : " That millisecond figure is a ONE-OFF hitch, not a per-frame cost.";

        VRLog.Info("Batch", $"Root '{scan.Name}': combined {combined} renderer(s) "
                            + $"({declined} declined by Unity) into "
                            + $"{CombinedMeshes.Count} combined mesh(es) so far, "
                            + $"{scan.Vertices:N0} vertices offered, in {ms:F0} ms." + hitch);
    }

    private static void RegisterCombinedMesh(Mesh mesh)
    {
        int id = mesh.GetInstanceID();
        if (!CombinedIds.Add(id))
            return;
        CombinedMeshes.Add(mesh);
    }

    /// <summary>
    /// Close the pass: price it, optionally release the combined meshes' CPU copies, and log the
    /// one line the whole experiment is judged on.
    /// </summary>
    private static void ReportApplied()
    {
        MeasureBatchResult(out int before, out int after, out float megabytes);
        DrawCallsBefore = before;
        DrawCallsAfter = after;
        MegabytesPerCopy = megabytes;

        int freed = 0;
        if (StaticBatchConfig.FreeCpuCopy)
        {
            for (int i = 0; i < CombinedMeshes.Count; i++)
            {
                Mesh m = CombinedMeshes[i];
                if (m == null || !m.isReadable)
                    continue;
                try
                {
                    m.UploadMeshData(markNoLongerReadable: true);
                    freed++;
                }
                catch (Exception)
                {
                    // A mesh that refuses to release its CPU copy still renders; this is a memory
                    // optimisation on top of an optimisation, never a correctness step.
                }
            }
        }

        int passes = UnityEngine.XR.XRSettings.stereoRenderingMode
                     == UnityEngine.XR.XRSettings.StereoRenderingMode.MultiPass ? 2 : 1;

        VRLog.Info("Batch", $"APPLY — {Ledger.Count} renderer(s) combined into {CombinedMeshes.Count} "
                            + $"mesh(es). Submitted material slots on exactly those renderers: "
                            + $"{before} → ~{after} (x{passes} pass(es) = ~{before * passes} → "
                            + $"~{after * passes} draw calls/frame). This 'after' is a FLOOR computed "
                            + "the same way the [Perf] SCENE line computes its floor — distinct "
                            + "materials per combined mesh — not a measurement; the number that "
                            + "settles it is the HeadCamera submit figure on the next [Perf] SPLIT "
                            + $"line. Memory: ~{megabytes:F0} MB of duplicated vertex data"
                            + (freed > 0
                                ? $", CPU copy released on {freed} of them (so roughly half that in RAM)."
                                : " in system AND video memory (FreeCombinedCpuCopy is off).")
                            + " Everything here is undoable: Mode = Off, or the button in "
                            + "Einstellungen › Debug › Leistung & Effekte › Bündelung.");

        PerfMonitor.MarkChange($"static batching applied — {Ledger.Count} renderer(s), "
                               + $"~{before * passes} → ~{after * passes} draw calls (estimate)");
        UpdateShortStatus();

        // Start both timers HERE rather than leaving them at 0, which would make the first watchdog
        // check and the first rescan fire on the very next frame — a rescan one frame after the
        // pass can only ever find nothing, and the log line it would not write is still a walk.
        float now = Time.unscaledTime;
        _nextWatch = now + WatchInterval;
        _nextRescan = now + StaticBatchConfig.Rescan;
    }

    /// <summary>
    /// Price the pass. BEFORE = the material slots those renderers submitted individually. AFTER =
    /// per combined mesh, the number of DISTINCT materials in it — because Unity sorts a batch by
    /// material and consecutive same-material members collapse into one draw. Both are floors, and
    /// both are computed over exactly the same set of renderers, which is what makes the pair
    /// comparable at all.
    /// </summary>
    private static void MeasureBatchResult(out int before, out int after, out float megabytes)
    {
        before = 0;
        MaterialsPerBatch.Clear();

        for (int i = 0; i < Ledger.Count; i++)
        {
            Entry e = Ledger[i];
            if (e.Filter == null || e.Renderer == null || e.Filter.sharedMesh == null)
                continue;
            try
            {
                e.Renderer.GetSharedMaterials(MaterialScratch);
            }
            catch (Exception)
            {
                continue;
            }

            int slots = Mathf.Min(MaterialScratch.Count, e.Original == null ? MaterialScratch.Count
                                                                            : e.Original.subMeshCount);
            before += slots;

            int batchId = e.Filter.sharedMesh.GetInstanceID();
            if (!MaterialsPerBatch.TryGetValue(batchId, out HashSet<int> mats))
            {
                mats = new HashSet<int>(32);
                MaterialsPerBatch[batchId] = mats;
            }
            for (int m = 0; m < slots; m++)
            {
                Material? mat = MaterialScratch[m];
                if (mat == null)
                    continue;
                // A per-renderer property block stops that renderer from sharing a draw call with
                // anything, so it is counted as its OWN draw rather than folded into its material's.
                // (The mod's wall see-through sets these on wall segments; the game sets them on
                // decals and VFX.) Keyed on the renderer so two blocked renderers never collapse.
                mats.Add(e.Renderer.HasPropertyBlock()
                    ? unchecked(e.Renderer.GetInstanceID() * 31 + m)
                    : mat.GetInstanceID());
            }
        }

        after = 0;
        foreach (KeyValuePair<int, HashSet<int>> kv in MaterialsPerBatch)
            after += kv.Value.Count;

        long bytes = 0;
        for (int i = 0; i < CombinedMeshes.Count; i++)
        {
            Mesh m = CombinedMeshes[i];
            if (m == null)
                continue;
            bytes += (long)m.vertexCount * StrideOf(m);
        }
        megabytes = bytes / (1024f * 1024f);
    }

    /// <summary>Real bytes per vertex, summed over the mesh's vertex streams; falls back to a model.</summary>
    private static int StrideOf(Mesh mesh)
    {
        try
        {
            int stride = 0;
            int streams = mesh.vertexBufferCount;
            for (int s = 0; s < streams; s++)
                stride += mesh.GetVertexBufferStride(s);
            return stride > 0 ? stride : FallbackVertexStride;
        }
        catch (Exception)
        {
            return FallbackVertexStride;
        }
    }

    // ==========================================================================================
    //  Undo
    // ==========================================================================================

    /// <summary>
    /// Hand every combined renderer back its own mesh and its own batch state, then destroy the
    /// meshes this pass created. Safe to call at any time, from any state, more than once.
    ///
    /// <para>ORDER IS LOAD-BEARING: every original mesh is restored BEFORE any combined mesh is
    /// destroyed. Destroying first would leave renderers pointing at a dead mesh for the rest of
    /// the loop — a frame of missing geometry for no reason.</para>
    /// </summary>
    internal static void Revert(string reason)
    {
        if (Ledger.Count == 0 && CombinedMeshes.Count == 0)
        {
            PendingCombines.Clear();
            return;
        }

        int restored = 0, gone = 0, stuck = 0;
        for (int i = Ledger.Count - 1; i >= 0; i--)
        {
            Entry e = Ledger[i];
            if (e.Filter == null || e.Renderer == null)
            {
                // The object was destroyed under us (scene unload, a room the game tore down).
                // There is nothing to restore and nothing left to be wrong.
                gone++;
                continue;
            }
            if (e.Original != null)
                e.Filter.sharedMesh = e.Original;
            if (!StaticBatchInterop.ClearBatchState(e.Renderer))
                stuck++;
            if (e.Renderer is MeshRenderer mr)
            {
                if (e.AdditionalStreams != null)
                    mr.additionalVertexStreams = e.AdditionalStreams;
                StaticBatchInterop.WriteEnlightenStream(mr, e.EnlightenStream);
            }
            restored++;
        }
        Ledger.Clear();

        int destroyed = 0;
        for (int i = 0; i < CombinedMeshes.Count; i++)
        {
            Mesh m = CombinedMeshes[i];
            if (m == null)
                continue;
            UnityEngine.Object.Destroy(m);
            destroyed++;
        }
        CombinedMeshes.Clear();
        CombinedIds.Clear();
        PendingCombines.Clear();

        DrawCallsBefore = 0;
        DrawCallsAfter = 0;
        MegabytesPerCopy = 0f;
        _watchCursor = 0;
        UpdateShortStatus();

        VRLog.Info("Batch", $"REVERT ({reason}) — {restored} renderer(s) given their own mesh and "
                            + $"batch state back, {destroyed} combined mesh(es) destroyed"
                            + (gone > 0 ? $", {gone} object(s) had already been destroyed" : "")
                            + (stuck > 0
                                ? $". WARNING: {stuck} renderer(s) could not have their batch state "
                                  + "cleared and may draw wrong until the scene changes."
                                : ".")
                            + " The scene is back to exactly what the game shipped.");
        PerfMonitor.MarkChange($"static batching reverted ({reason}) — A/B boundary");
    }

    // ==========================================================================================
    //  Watchdog and rescan
    // ==========================================================================================

    /// <summary>
    /// Re-pose a rotating slice of the ledger against the batch root. Anything that moved is a
    /// combined object whose transform is being driven, which means it is now drawn in the wrong
    /// place — the single visible failure mode this feature has.
    /// </summary>
    private static void TickWatchdog()
    {
        if (Ledger.Count == 0)
            return;
        int sample = Mathf.Min(WatchSampleSize, Ledger.Count);
        for (int k = 0; k < sample; k++)
        {
            if (_watchCursor >= Ledger.Count)
                _watchCursor = 0;
            Entry e = Ledger[_watchCursor];
            _watchCursor++;

            if (e.Filter == null || e.Root == null)
                continue;
            Matrix4x4 now = e.Root.worldToLocalMatrix * e.Filter.transform.localToWorldMatrix;
            if (!Moved(now, e.RootLocal))
                continue;

            string name = e.Filter.gameObject.name;
            VRLog.Warn("Batch", $"WATCHDOG — combined object '{name}' MOVED relative to its batch "
                                + "root. A combined object's vertices are baked into that root's "
                                + "space, so it is now being drawn where it used to be. "
                                + (StaticBatchConfig.AutoRevert
                                    ? "Handing the whole pass back now. Put a piece of that name into "
                                      + "[Batching] ExcludeNames and the rest of the map still batches."
                                    : "WatchdogAutoRevert is OFF, so the batch is being kept for "
                                      + "inspection — the picture is wrong on purpose."));
            if (!StaticBatchConfig.AutoRevert)
                return;

            Revert($"the watchdog saw '{name}' move");
            _watchdogReverts++;
            if (_watchdogReverts >= MaxWatchdogRevertsPerScene)
            {
                _suspended = true;
                VRLog.Warn("Batch", $"GIVING UP on this scene — the watchdog has undone the pass "
                                    + $"{_watchdogReverts} time(s) here, which means something in "
                                    + "it moves and would be undone again after every re-check. "
                                    + "Nothing further will be combined until the scenario changes "
                                    + $"or the mode is cycled. The last offender was '{name}': put "
                                    + "a piece of that name into [Batching] ExcludeNames and the "
                                    + "rest of the map will batch normally.");
            }
            return;
        }
    }

    /// <summary>
    /// Element-wise matrix compare with a tolerance that scales with the magnitude, so a large
    /// translation in root space is not permanently "moved" by float noise while a millimetre
    /// nudge on a small offset still trips.
    /// </summary>
    private static bool Moved(in Matrix4x4 a, in Matrix4x4 b)
    {
        for (int i = 0; i < 16; i++)
        {
            float x = a[i];
            float y = b[i];
            if (Mathf.Abs(x - y) > 1e-4f * Mathf.Max(1f, Mathf.Abs(y)))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Rooms are revealed as the scenario is explored, so a single pass cannot cover a map that
    /// grows. This walks the CONFIGURED ROOTS only — a handful of objects, not the whole scene —
    /// and asks for another pass once enough new eligible renderers have appeared.
    /// </summary>
    private static void TickRescan()
    {
        int scanned = Scan();
        int eligible = 0;
        for (int i = 0; i < Scans.Count; i++)
            eligible += Scans[i].Filters.Count;

        LastScanned = scanned;
        LastEligible = eligible;

        if (eligible < StaticBatchConfig.Growth)
            return;

        VRLog.Info("Batch", $"RESCAN — {eligible} newly eligible renderer(s) appeared since the last "
                            + "pass (rooms revealed, props spawned). Combining them too; everything "
                            + "already combined is left exactly as it is.");
        int min = StaticBatchConfig.MinRenderersPerRoot;
        for (int i = 0; i < Scans.Count; i++)
        {
            if (Scans[i].Filters.Count >= min)
                PendingCombines.Add(Scans[i]);
        }
    }

    // ==========================================================================================
    //  Reporting
    // ==========================================================================================

    private static readonly StringBuilder Line = new(1024);

    /// <summary>
    /// The <c>[Batch] PROBE</c> line — the whole point of <see cref="BatchMode.Probe"/>. It answers
    /// "is this possible here, and what would it cost" WITHOUT a single mutation, and it names every
    /// rejection reason as a count so a disappointing number is diagnosable instead of just
    /// disappointing.
    /// </summary>
    private static void ReportProbe(BatchMode mode, string reason, int scanned, int eligible,
        int vertices, int slots, double scanMs)
    {
        Line.Clear();
        Line.Append("PROBE (").Append(reason).Append(") — mode ").Append(mode)
            .Append(" | roots: ");
        if (Scans.Count == 0)
        {
            Line.Append("none found");
        }
        else
        {
            for (int i = 0; i < Scans.Count; i++)
            {
                Line.Append(i == 0 ? "" : ", ").Append(Scans[i].Name).Append(' ')
                    .Append(Scans[i].Filters.Count).Append(" eligible");
            }
        }

        Line.Append(" | ").Append(eligible).Append(" of ").Append(scanned)
            .Append(" MeshFilter(s) can be combined, carrying ").Append(slots)
            .Append(" material slot(s) over ").Append(DistinctMaterials.Count)
            .Append(" distinct material(s)");
        if (DistinctMaterials.Count > 0)
        {
            Line.Append(" = ").Append((eligible / (float)DistinctMaterials.Count).ToString("F1"))
                .Append(" renderer(s) per material — that ratio IS the prize: it is the factor the "
                        + "draw-call count could fall by, and nothing else about this feature "
                        + "matters if it is near 1");
        }

        Line.Append(" | cost if applied: ").Append(vertices.ToString("N0")).Append(" vertices ≈ ")
            .Append((vertices * (long)FallbackVertexStride / (1024f * 1024f)).ToString("F0"))
            .Append(" MB duplicated (modelled at ").Append(FallbackVertexStride)
            .Append(" B/vertex; the APPLY line reports the real stride)");

        Line.Append(" | rejected: ");
        _reasonsWritten = 0;
        AppendReason("unreadable mesh", _rejUnreadable);
        AppendReason("animated/physics-driven", _rejMover);
        AppendReason("renderer disabled", _rejDisabled);
        AppendReason("no MeshRenderer", _rejNoRenderer);
        AppendReason("no mesh", _rejNoMesh);
        AppendReason("empty mesh", _rejEmptyMesh);
        AppendReason("already batched", _rejAlreadyBatched);
        AppendReason("shader forbids batching", _rejShader);
        AppendReason("Unity says not batchable", _rejNotBatchable);
        AppendReason("vertex-stream mismatch", _rejStreams);
        AppendReason("layer excluded", _rejLayer);
        AppendReason("name excluded", _rejName);
        AppendReason("over the vertex budget", _rejBudget);
        if (_reasonsWritten == 0)
            Line.Append("nothing");

        if (_rejUnreadable > 0)
        {
            Line.Append(" | NOTE: 'unreadable mesh' is the one rejection that cannot be configured "
                        + "away. A mesh imported with Read/Write disabled has no system-memory copy, "
                        + "so nothing can concatenate it. If that count is most of the scene, static "
                        + "batching is impossible on this content and no setting changes that.");
        }

        // The scene roots that EXIST, whenever the configured names did not all match. This is the
        // line a tester reads to learn what to put in [Batching] Roots — printed here rather than
        // left to the [Perf] SCENE line, because that one is off by default and this one is not.
        if (RootSurvey.Count > 0)
        {
            Line.Append(" | scene roots available (name: mesh objects):");
            int shown = Mathf.Min(SurveyRootsShown, RootSurvey.Count);
            for (int i = 0; i < shown; i++)
            {
                GameObject go = RootSurvey[i].Key;
                Line.Append(i == 0 ? " " : ", ").Append(go == null ? "<gone>" : go.name)
                    .Append(": ").Append(RootSurvey[i].Value);
            }
            if (RootSurvey.Count > shown)
                Line.Append(", +").Append(RootSurvey.Count - shown).Append(" more");
            Line.Append(" — put the right one into [Batching] Roots");
        }

        Line.Append(" | scan took ").Append(scanMs.ToString("F0")).Append(" ms");
        if (mode == BatchMode.Probe)
            Line.Append(" | nothing was mutated (mode is Probe).");

        VRLog.Info("Batch", Line.ToString());
    }

    /// <summary>How many rejection reasons the line already carries (drives the separator).</summary>
    private static int _reasonsWritten;

    private static void AppendReason(string label, int count)
    {
        if (count == 0)
            return;
        if (_reasonsWritten > 0)
            Line.Append(", ");
        Line.Append(count).Append(' ').Append(label);
        _reasonsWritten++;
    }

    private static void UpdateShortStatus()
    {
        if (Applied)
        {
            ShortStatus = $"{Ledger.Count} / {CombinedMeshes.Count} / {MegabytesPerCopy:F0} MB";
            return;
        }
        ShortStatus = HasProbed ? $"{LastEligible} / {LastScanned}" : "-";
    }

    // ==========================================================================================
    //  Settings-panel accessors (Debug ▸ Leistung & Effekte ▸ Bündelung)
    // ==========================================================================================

    /// <summary>Cycle-button readout for the mode row, in the game's language.</summary>
    internal static string ModeLabel()
    {
        StaticBatchConfig.Bind();
        return StaticBatchConfig.CurrentMode switch
        {
            BatchMode.Probe => Loc.Mod("batch_mode_probe"),
            BatchMode.On => Loc.Mod("batch_mode_on"),
            _ => Loc.Mod("off"),
        };
    }

    /// <summary>
    /// Advance Off → Probe → On → Off. The mode WRITE is all this does: the driver's next tick
    /// notices the change, closes the measurement window on it and schedules or undoes the pass —
    /// so the button has one job and there is one place that decides what a mode means.
    /// </summary>
    internal static void CycleMode()
    {
        StaticBatchConfig.Bind();
        if (StaticBatchConfig.Mode == null)
            return;
        StaticBatchConfig.Mode.Value = StaticBatchConfig.Mode.Value switch
        {
            BatchMode.Off => BatchMode.Probe,
            BatchMode.Probe => BatchMode.On,
            _ => BatchMode.Off,
        };
    }

    /// <summary>
    /// Draw-call readout: the material slots the combined renderers submitted before the pass
    /// against the estimated floor after it, per eye pass. "-" until a pass has actually run.
    /// </summary>
    internal static string DrawCallLabel() =>
        Applied && DrawCallsBefore > 0 ? $"{DrawCallsBefore} → ~{DrawCallsAfter}" : "-";

    // ==========================================================================================
    //  Host
    // ==========================================================================================

    /// <summary>
    /// The per-frame host. Its whole body is one <see cref="TickGuard.Run"/> call, so the pass
    /// appears by name in the <c>[Perf] STEPS</c> ranking (it is the one subsystem in this mod
    /// whose job is to change that ranking) and a throw inside it is isolated and attributed rather
    /// than becoming an anonymous per-frame NullReferenceException flood.
    /// </summary>
    private sealed class BatchDriver : MonoBehaviour
    {
        private static readonly Action TickStep = Tick;

        private void Update() => TickGuard.Run("Batch.Tick", TickStep);
    }
}
