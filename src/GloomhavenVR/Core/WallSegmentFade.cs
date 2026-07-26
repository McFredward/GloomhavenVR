using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GloomhavenVR.Core;

/// <summary>
/// Live-tunable wall-see-through decision thresholds (canonical <see cref="ModuleConfig.Create"/>
/// pattern — <c>dev.gloomhavenvr.wallfade.cfg</c>). The fade DECISION constants that needed
/// hardware iteration every round (on/off view-coverage fractions and the two un-fade dwells)
/// are config entries now: the <see cref="WallSegmentFade"/> driver re-reads them through the
/// clamped accessors on EVERY evaluation tick, and the in-VR settings panel exposes them as
/// debug-menu steppers next to the Wall see-through toggle — so threshold tuning happens live
/// in the headset and persists (BepInEx saves on every entry write). The remaining constants
/// (EMA taus, sample-band geometry…) stay code-owned; they were stable across rounds.
/// </summary>
internal static class WallFadeTuning
{
    private static ConfigFile? _file;

    /// <summary>Smoothed view-coverage fraction at/above which a wall fades OUT (Schmitt high bar).</summary>
    internal static ConfigEntry<float>? OnFraction;
    /// <summary>Schmitt low bar: once faded, the wall stays faded while the fraction is at/above this.</summary>
    internal static ConfigEntry<float>? OffFraction;
    /// <summary>Seconds continuously below the low bar before un-fading after a recent perspective change.</summary>
    internal static ConfigEntry<float>? ExitDwellMoved;
    /// <summary>Un-fade dwell when the head only rotated (no recent translation/world-grab/recenter).</summary>
    internal static ConfigEntry<float>? ExitDwellStationary;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("wallfade");
        OnFraction = config.Bind("WallFade", "OnFraction", 0.25f,
            "Fade a wall when it hides at least this (EMA-smoothed) fraction of the frustum-visible " +
            "FLOOR (hex-tile plane) samples of some room — 0.25 = wall hides 25% of the floor you " +
            "are looking at (Schmitt trigger high bar). Live; clamped 0.05-0.95.");
        OffFraction = config.Bind("WallFade", "OffFraction", 0.10f,
            "Once faded, keep the wall faded while the smoothed floor-coverage fraction stays at or " +
            "above this (Schmitt trigger low bar). Live; clamped 0.01-0.95 and never above OnFraction.");
        ExitDwellMoved = config.Bind("WallFade", "ExitDwellMovedSeconds", 2.5f,
            "Seconds the fraction must stay below OffFraction before the wall un-fades when the " +
            "PERSPECTIVE recently changed (real head translation / world-grab / recenter). Live.");
        ExitDwellStationary = config.Bind("WallFade", "ExitDwellStationarySeconds", 7f,
            "Un-fade dwell while the head has only ROTATED recently — rotation alone should almost " +
            "never bring a wall back. Live; never below ExitDwellMovedSeconds.");
    }

    // Clamped live accessors — safe before Bind() (fall back to the shipped defaults).
    internal static float On => Clamped(OnFraction, 0.25f, 0.05f, 0.95f);
    internal static float Off => Mathf.Min(Clamped(OffFraction, 0.10f, 0.01f, 0.95f), On);
    internal static float DwellMoved => Clamped(ExitDwellMoved, 2.5f, 0.1f, 60f);
    internal static float DwellStationary =>
        Mathf.Max(Clamped(ExitDwellStationary, 7f, 0.1f, 120f), DwellMoved);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);
}

/// <summary>
/// ISSUE #4 round 3 (wall see-through, VR redesign) — WHOLE-WALL fade with temporal hysteresis.
///
/// HARDWARE VERDICT on round 2 (per-pixel head-camera occlusion feed, removed with this class's
/// predecessor <c>WallFadeOcclusionFeed</c>): unusable in VR. The game's fade is a SCREEN-SPACE
/// per-pixel discard (wall fragment samples <c>_TilesOcclusionMap</c> at its own screen UV and
/// clips when it occludes the play area behind that pixel) — designed for a slow RTS camera.
/// Under fast head movement PARTS of walls pop in/out every frame. This driver replaces the
/// per-pixel screen-space decision with a per-WALL-SEGMENT decision computed on the CPU from the
/// head position, temporally smoothed, and delivered through the wall shaders' OWN fade path via
/// per-renderer <see cref="MaterialPropertyBlock"/>s.
///
/// WALL UNIT (research): scenario walls are <c>ProceduralWall</c> components (GH.Runtime,
/// Apparance procedural entities — one component per wall run with Left/RightCorner + Length;
/// all live instances in the publicized static <c>ProceduralWall.m_WallCache</c>). Their
/// generated child MeshRenderers carry the fade-capable shaders <c>Amp_Basic_WallFade</c>
/// (misc_high_shaders bundle) / <c>Amp_Low/Amp_Basic_WallFade_Low</c> (misc_shaders bundle).
/// The play area is the revealed room tiles: exactly <c>TilesOcclusionGenerator.s_Instance
/// .m_RoomRenderers</c> (fed per revealed room by <c>TilesOcclusionVolume.IsVisible()</c> ==
/// <c>m_HexMap.Revealed</c>) — the same renderer set the game itself rasterizes into the
/// occlusion map.
///
/// FADE MECHANISM (chosen: (a) the game's own map mechanism, forced per renderer — DXBC
/// disassembly of both wall fragment shaders, scratch <c>walldisasm[-low]</c>):
/// <list type="bullet">
/// <item>LOW variant (<c>Amp_Low/Amp_Basic_WallFade_Low</c>, misc_shaders — what the Quest
///   rig's hardware log reported): gate <c>ine cb0[4].x,0</c> (<c>ToggleWallFade</c>, int)
///   AND object-space Y &gt;= 0.4 (hard pre-clip FOUNDATION BAND — the base course never
///   fades); then <c>m = (occ.a &gt;= fragDepth) ? 1 : (1-occ.r)</c>,
///   <c>discard if m - _Cutoff &lt; 0</c> (<c>_Cutoff</c> = "Mask Clip Value", cb0[4].y).</item>
/// <item>HIGH variant (<c>Amp_Basic_WallFade</c>, misc_high_shaders), blob216 lines 165-229:
///   same map term <c>m</c>, <c>M = m·_ToggleWallfade</c> (material float, cb0[6].x);
///   <c>S = smoothstep(sat(3.33·((0.02·dist + screenRadial)^8 + (1-worldY)/3)))</c> — the
///   world-Y foundation ramp and the screen-edge vignette are SUMMED INSIDE one scalar;
///   <c>n</c> = time-drifting world-space simplex noise; <c>A = max(M,S) + 42n·(1-max(M,S))</c>;
///   <c>B = (M&gt;0) ? 1 : S</c>; <c>discard if 1 + ToggleWallFade·(A·B-1) - _Cutoff &lt; 0</c>
///   (<c>_Cutoff</c> = cb0[6].z).</item>
/// </list>
/// Unity property precedence is MPB &gt; material &gt; global, so a per-renderer
/// MaterialPropertyBlock can open the gate (<c>ToggleWallFade=1</c>), substitute the map
/// (<c>_TilesOcclusionMap</c> = a small CONSTANT texture) and set <c>_Cutoff</c> /
/// <c>_ToggleWallfade</c>. Concretely:
/// <list type="bullet">
/// <item>TRANSITION (0&lt;fade&lt;1): map = low-frequency VALUE-NOISE texture (r in [0.06,1],
///   a=0 — fails the reversed-Z depth compare, so <c>m = 1-noise</c>),
///   <c>_Cutoff = lerp(-0.05, 1, fade)</c> → progressive dissolve; the high variant
///   additionally dithers/vignettes with its own view terms. The noise is sampled at SCREEN
///   UV by the shader itself, so the pattern slides under head motion — confined to the
///   ~0.35s dissolve, cosmetic (under conventional-Z it would degrade to an end-of-sweep
///   pop; the rig is D3D11 reversed-Z).</item>
/// <item>HELD FADED (fade=1) — R3 (foundation-band fix; the R2 held state below deleted
///   the base course, the user's bug): drive EXACTLY the value the flat game's own
///   occlusion map delivers over a revealed room. The game never touches <c>_Cutoff</c>
///   at all — it only sets the global <c>ToggleWallFade=1</c> (ActivateWallFadeInGame
///   .Start, Main.Awake) and rasterizes the revealed-room footprints into the
///   screen-space RT <c>_TilesOcclusionMap</c> (TilesOcclusionGenerator
///   .UpdateCommandBuffers: rooms drawn on a (0,0,0,1)-cleared target, blurred, bound
///   globally); over a room interior the blurred map reads occ.r≈1, so the wall shader
///   computes <c>m = 1-occ.r ≈ 0</c> and its OWN foundation terms do the rest. Held MPB
///   on BOTH variants: map = constant r=1 <b>a=0</b> texture → <c>m = 0</c>
///   view-independently (a=0 fails the depth compare for every visible fragment under
///   either Z convention — reversed-Z and conventional fragDepth are both &gt; 0 except
///   the degenerate exact far/near-plane pixel), <c>_Cutoff</c> = the material's
///   AUTHORED "Mask Clip Value" (clamped 0.05–0.95; with m = 0 any 0&lt;c&lt;1 yields
///   the same held geometry — the authored value only shapes the HIGH dither density,
///   matching the flat game exactly), <c>_ToggleWallfade=1</c>. LOW (blob264 lines
///   46-49, 69-71): <c>clip = 0 - c &lt; 0</c> → constant discard wherever objY ≥ 0.4;
///   the base course below the shader's hard object-Y gate stays solid. HIGH (blob216
///   lines 216-229): <c>M = 0</c> → <c>B = S</c>, <c>A = S + 42n(1-S)</c>, so at the
///   foundation the world-Y ramp (1-worldY)/3 saturates S to 1 → <c>A·B = 1</c>,
///   <c>clip = 1-c &gt; 0</c> — solid base band, noise MULTIPLIED BY ZERO, worldY-only
///   (view-independent); up the wall S → 0 → <c>clip = -c &lt; 0</c> — constant
///   discard; between (worldY ≈ 0.4..1) the game's own noise-dithered band edge.
///   RESIDUAL VIEW COUPLING (HIGH only, game-native, accepted because the spec is
///   "exactly the flat game's faded wall"): S also sums the screen-radial vignette
///   (0.02·dist+screenRadial)^8, so peripheral pixels — and whole walls beyond
///   ~45 wu from the head, where min(0.02·dist,1)+radial ≥ 1 — keep the upper wall
///   partially visible exactly as the flat game does near screen edges / zoomed out.
///   The per-wall fade DECISION stays CPU-side and view-independent. IMPOSSIBILITY
///   NOTE (why the vignette cannot be stripped while keeping the band): band term and
///   vignette are summed inside S BEFORE the single cutoff compare, and every vignette
///   coefficient is an immediate DXBC literal — the only strictly view-independent
///   HIGH deliveries are m=1 constants (clip = 1-c everywhere: whole wall visible, or
///   with c&gt;1 the R2 TOTAL discard that erased the foundation). Every fade logs the
///   wall's shader variant + applied cutoff so a hardware log pins down which math
///   applied.</item>
/// <item>SOLID (fade=0): the MPB is REMOVED — with <see cref="Compat.WallFadeDisable"/> now
///   pinning the GLOBAL <c>ToggleWallFade</c> to 0 unconditionally (the game-camera
///   TilesOcclusionGenerator still publishes a head-viewpoint-invalid map; globally-open
///   fade would sample garbage), an untouched renderer is bit-for-bit today's solid wall.</item>
/// </list>
///
/// OCCLUSION DECISION (per segment, VR-stable — round 7, PER-WALL ROOM COVERAGE; user
/// spec: a wall's fraction literally means "share of ITS OWN room's floor (hex tiles)
/// hidden from the current viewpoint" — 0.25 = a quarter of that room's floor is behind
/// the wall).
///
/// ROUND-6 HARDWARE KILL FACTOR (log: EVERY diag line carried the !ABOVE-WALL tripwire):
/// the floor plane was taken from the room renderers' bounds.max.y — but m_RoomRenderers
/// are the game's top-down occlusion-map PROXY meshes; only their XZ footprint matches
/// the tiles, their AABB tops sat ~9 wu above the actual tile plane (log: sampY 9.05 vs
/// wall AABB tops ≤ 3.67, actual floor ≈ 0). Every head→sample ray therefore ran
/// entirely ABOVE every wall box — blocked counts were permanently 0/48 (the one logged
/// frame where the head dipped to y = −0.79 instantly read raw 0.69, proving the ray
/// math fine and the FRAME wrong). Round 7 anchors the floor plane per room on the
/// game's own tile data: each <c>TilesOcclusionVolume</c> maps its <c>Renderers</c> to
/// its <c>CentralTile</c> (a <c>TileBehaviour</c> whose transform sits ON the tile
/// plane — the game spawns its worldspace tile UI at exactly that position), so sample
/// height = CentralTile.position.y + 0.05 wu; rooms without a volume match fall back to
/// the median anchored height (then, with zero anchors, to the old bounds top — and the
/// !ABOVE-WALL tripwire stays to catch that in hardware logs).
///
/// METRIC: at rescan every wall is associated with ONE room (smallest XZ gap between the
/// wall AABB and the room AABB — walls border their room, gap ≈ 0; ties by nearer
/// center). Per frame:
///   fraction = (points of the wall's room floor grid that are IN VIEW-DIRECTION and
///               whose head→point segment the wall AABB clearly interrupts)
///              / (ALL floor-grid points of that room).
/// Frustum culling (viewport test, 0.20 margin) applies to the NUMERATOR ONLY — a floor
/// point outside the view cannot be "hidden by the wall" in the user's sense — while the
/// denominator stays the room's WHOLE grid so the number reads literally as "this wall
/// hides X% of the room's floor" and the VR-menu stepper values keep their plain meaning.
/// "Clearly interrupts" = AABB entry distance &lt; dist − max(0.5·thickness clamped
/// 0.10–0.90 wu, 0.05·dist), OR the sample lies inside the AABB (the round-4
/// 88%-of-distance rule discarded exactly the near-edge first-row samples that carry the
/// whole signal; a pure thickness epsilon was still too strict for long grazing rays —
/// the 5%-of-distance term keeps the margin proportionate). Head inside the wall AABB
/// counts as 1.0 (wall in the face).
///
/// TRIGGER (fully stepper-driven, WallFadeTuning live config): the raw fraction is
/// EMA-smoothed (tau 0.15s), then compared against the VR-menu steppers — ON at
/// ≥ OnFraction (default 0.25), and once faded a SCHMITT TRIGGER holds down to
/// OffFraction (default 0.10). HEAD-MOTION DECOUPLING: fade-IN needs only a short 0.2s
/// dwell (prompt); fade-OUT is deliberately DELAYED — ExitDwellMoved (2.5s) continuously
/// below the low bar when the PERSPECTIVE recently (≤3s) actually changed (real head
/// TRANSLATION &gt;0.18m tracking-space, rig-root motion from world-grab/snap-turn,
/// recenter/rig rebuild, room-bounds shift), stretching to ExitDwellStationary (7s) when
/// the head only rotated. No other head-motion-coupled term exists in the decision. The
/// fade value itself stays exponentially damped (tau 0.12s ≈ 0.35s visible transition).
///
/// MULTIPLAYER: purely local rendering (MaterialPropertyBlocks + locally created textures);
/// nothing synced, peers unaffected. Gated LIVE by [Compat] WallFade — OFF clears every block
/// immediately (exactly today's solid walls, zero per-frame cost beyond the enabled check).
/// </summary>
internal static class WallSegmentFade
{
    private const string Name = "WallSegmentFade";
    private const string DriverName = "GloomhavenVR.WallSegmentFade";

    private static FadeDriver? _driver;

    /// <summary>Install the fade driver (idempotent). No-op when VR isn't running.</summary>
    public static void Install()
    {
        if (_driver != null || !VRSession.IsRunning)
            return;
        WallFadeTuning.Bind(); // decision thresholds are live config (debug-menu steppers)
        var go = new GameObject(DriverName);
        UnityEngine.Object.DontDestroyOnLoad(go);
        _driver = go.AddComponent<FadeDriver>();
        VRLog.Info(Name,
            $"installed (WallFade={(Plugin.WallFade != null && Plugin.WallFade.Value ? "on" : "off")}) — " +
            "whole-wall fade: per-ProceduralWall ROOM-coverage decision (fraction of the " +
            "wall's own room's tile-anchored floor grid hidden from the head, frustum-culled " +
            "numerator, EMA + stepper-driven Schmitt trigger + perspective-anchored dwell), " +
            "delivered via per-renderer MaterialPropertyBlocks through the wall shaders' own " +
            "map/cutoff fade path.");
    }

    /// <summary>Clear every property block and destroy the driver (hot-reload safe).</summary>
    public static void Uninstall()
    {
        if (_driver == null)
            return;
        try { _driver.Teardown(); }
        catch { /* scene already tearing down */ }
        try { UnityEngine.Object.Destroy(_driver.gameObject); }
        catch { /* scene teardown already got it */ }
        _driver = null;
    }

    private static bool Enabled => Plugin.WallFade != null && Plugin.WallFade.Value;

    /// <summary>Per-wall-segment fade state.</summary>
    private sealed class Segment
    {
        public ProceduralWall? Wall;
        public readonly List<MeshRenderer> Renderers = new();
        public Bounds Bounds;
        public bool HasBounds;

        /// <summary>Last raw (unsmoothed) occlusion verdict and when it first held.</summary>
        public bool PendingRaw;
        public float PendingSince;
        /// <summary>Debounced state the fade animates toward.</summary>
        public bool State;
        /// <summary>Damped fade value in [0,1]; 0 = solid (no MPB), 1 = held fully faded.</summary>
        public float Fade;
        /// <summary>Whether our MPB is currently applied to the renderers.</summary>
        public bool HasBlock;

        /// <summary>Blocked-test distance epsilon (world units, ~half the wall thickness).</summary>
        public float BlockEps = 0.3f;
        /// <summary>Index into the room tables of the ONE room this wall belongs to (XZ-nearest
        /// room AABB, recomputed at rescan); -1 while unassociated.</summary>
        public int RoomIndex = -1;
        /// <summary>R2 diag: which fade-shader variant(s) this segment's renderers carry.</summary>
        public bool VariantHigh;
        public bool VariantLow;
        /// <summary>Distinct fade-shader name(s) seen on the renderers ("+"-joined).</summary>
        public string ShaderNames = "?";
        /// <summary>Authored "Mask Clip Value" (<c>_Cutoff</c>) of the wall's fade material,
        /// clamped to (0,1) — the held state drives exactly this value like the flat game
        /// (which never writes _Cutoff at all). 0.5 fallback when unreadable.</summary>
        public float HeldCutoff = 0.5f;
        /// <summary>Whether <see cref="HeldCutoff"/> came from the material (diag).</summary>
        public bool CutoffAuthored;
        /// <summary>EMA-smoothed view-coverage fraction the Schmitt trigger reads.</summary>
        public float Smooth;
        public bool SmoothInit;
        // Last-tick raw numbers, kept for the throttled diagnostic.
        public float LastRaw;
        public int LastBlocked;
        /// <summary>In-view sample count of THIS wall's room last tick (numerator candidates).</summary>
        public int LastRoomVisible;
        /// <summary>Total floor-grid points of this wall's room (the fraction denominator).</summary>
        public int LastRoomTotal;
    }

    private sealed class FadeDriver : MonoBehaviour
    {
        // --- decision constants (see class header) ------------------------------------------
        // SCALE SEMANTICS (round-5 audit): every linear constant below is WORLD units (wu)
        // unless it says "real/tracking meters". The rig root is scaled UP by WorldScale
        // (hardware log: 20.3; range ~11–20), i.e. 1 real meter = 11–20 wu and 1 wu = 5–9
        // real cm; board geometry keeps original game units (hex tile ≈ 1.72 wu). Sample
        // height needs no inference since round 6: the samples sit ON the room tile
        // bounds' top surface — the tile plane itself (see RebuildSamples).
        // On/off fractions + the two exit dwells are LIVE CONFIG now (WallFadeTuning — the
        // settings panel's debug steppers drive them in-headset); read fresh every evaluation.
        private const float EnterDwellSeconds = 0.20f; // short fade-IN prompt dwell (~0.2s per spec)
        private const float HeadMoveReevalMeters = 0.18f; // REAL tracking-space meters (scale-independent)
        private const float ReevalArmSeconds = 3f;     // how long a perspective change keeps re-eval armed
        private const float FadeTauSeconds = 0.12f;    // exp. fade time constant (~0.35s to 95%)
        private const float FractionTauSeconds = 0.15f; // EMA over the raw fraction (jitter killer)
        private const float FloorSampleEpsilon = 0.05f; // wu above the tile-anchored floor plane
        private const float BlockEpsMinWorld = 0.10f;  // wu — thickness-epsilon clamp (lo)
        private const float BlockEpsMaxWorld = 0.90f;  // wu — thickness-epsilon clamp (hi)
        private const float BlockEpsDistFraction = 0.05f; // blocked eps = max(thicknessEps, 5% of dist)
        private const float FrustumMargin = 0.20f;     // viewport slack (also covers per-eye vs mono skew)
        private const int MaxTotalSamples = 96;        // precomputed floor samples (all rooms)
        private const float RescanIntervalSeconds = 2f;
        private const float DiagIntervalSeconds = 2f;  // throttled hardware diagnostic cadence

        private static readonly int TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap");
        private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade");
        private static readonly int ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        private readonly Dictionary<ProceduralWall, Segment> _segments = new();
        private readonly List<ProceduralWall> _deadWalls = new();
        private readonly List<Bounds> _roomBounds = new();
        private readonly List<float> _roomFloorY = new();       // tile-anchored floor plane per room
        private readonly List<bool> _roomFloorAnchored = new(); // true = from a CentralTile anchor
        private readonly List<int> _roomSampleStart = new();    // first sample index per room
        private readonly List<int> _roomSampleCount = new();    // grid size per room (denominator)
        private readonly List<Vector3> _allSamples = new();     // per-room floor-plane grid
        private readonly bool[] _sampleVisible = new bool[MaxTotalSamples]; // per-frame frustum flags
        private readonly Dictionary<MeshRenderer, float> _floorYByRenderer = new(); // volume anchors
        private readonly List<float> _floorYScratch = new();    // median fallback scratch
        private readonly List<Material> _matScratch = new();
        private readonly System.Text.StringBuilder _diagSb = new();
        private float _sampleYMin, _sampleYMax;                 // overall sample-height range (diag)
        private int _roomsAnchored;                             // rooms with a tile-anchored plane (diag)
        private float _nextDiagTime;

        // [Optimize] WallFadeEvalInterval state: when the visibility/coverage DECISION last ran and
        // what it last reported (the fade + material writes keep running every frame regardless).
        private float _nextEvalTime;
        private float _lastEvalTime;
        private int _lastVisibleCount;
        private MaterialPropertyBlock? _mpb;

        private Texture2D? _noiseTex;    // transition dissolve pattern (r in [0.06,1], a=0)
        private Texture2D? _occludedTex; // held-faded constant (r=1, a=0 → map term m = 0)

        private float _nextRescan;
        private int _builtRoomCount = -1;

        // Perspective-change tracking (arms aggressive re-evaluation for ReevalArmSeconds).
        private float _lastReevalTime = float.NegativeInfinity;
        private int _lastPoseVersion = -1;
        private Vector3 _headAnchor;      // head localPosition (tracking-space meters)
        private bool _headAnchorInit;
        private Vector3 _rigPos;          // rig-root snapshot (world-grab / snap-turn detection)
        private Quaternion _rigRot;
        private float _rigScale;
        private bool _rigSnapInit;
        private Vector3 _roomCenter;      // combined room-bounds center (board-move detection)
        private bool _roomCenterInit;
        private bool _roomBoundsMoved;
        private bool _wasActive;
        private bool _failureLogged;
        private bool _heartbeatLogged;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy() => Teardown();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Scenario scenes are additive; walls/volumes stream in — rescan promptly. Old
            // renderers die with their scene, so blocks need no explicit clearing here.
            _nextRescan = 0f;
            _builtRoomCount = -1;
            _heartbeatLogged = false;
            _nextDiagTime = 0f;
        }

        /// <summary>
        /// LateUpdate on purpose: runs after every game Update, so the generator's renderer
        /// lists and the head pose are final for this frame. Fully guarded — a throw here must
        /// never starve the game loop (WorldUI lesson).
        /// </summary>
        private void LateUpdate()
        {
            try
            {
                // Perf attribution (2026-07 perf pass): the per-segment visibility sweep walks
                // every wall's floor samples against the head pose EVERY FRAME, which makes it a
                // prime suspect for head-motion-correlated cost — so it gets its own measured
                // scope. The scope never alters the try/catch semantics around it.
                using (PerfMonitor.Scope("WallFade.Late"))
                    Tick();
            }
            catch (Exception e)
            {
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    VRLog.Warn(Name, $"driver tick threw (logged once): {e}");
                }
            }
        }

        private void Tick()
        {
            Camera? head = Rig.VRRigDriver.HeadCamera;
            TilesOcclusionGenerator gen = TilesOcclusionGenerator.s_Instance;
            bool active = Enabled && VRSession.IsRunning && head != null && gen != null;
            if (!active)
            {
                // Toggled off / no scenario: revert to exactly-solid immediately.
                if (_wasActive)
                    ClearAllBlocks("inactive (toggle off / no scenario / no head)");
                _wasActive = false;
                return;
            }
            _wasActive = true;

            float now = Time.unscaledTime;
            if (now >= _nextRescan || gen!.m_RoomRenderers.Count != _builtRoomCount)
            {
                _nextRescan = now + RescanIntervalSeconds;
                Rescan(gen!);
            }
            if (_segments.Count == 0 || _roomBounds.Count == 0)
                return;

            Transform headT = head!.transform;
            Vector3 headPos = headT.position;
            UpdatePerspectiveState(headT, now);

            // [Optimize] WallFadeEvalInterval (2026-07 perf pass). The expensive half of this tick
            // is the DECISION: UpdateSampleVisibility projects every room's floor samples through
            // the head camera and BlockedFraction re-measures every segment against them, every
            // frame — work that is by definition head-motion correlated. The cheap half is the
            // per-segment exponential FADE plus its material write, which must stay per-frame or
            // the fade would visibly step.
            //
            // So the interval gates the decision only; the fade keeps running at full rate toward
            // whatever the last decision was. That is safe by construction because the decision it
            // feeds is ALREADY deliberately slow — an EMA, a Schmitt trigger and second-scale dwell
            // hysteresis (see the thresholds below) — so sampling it at 20 Hz instead of 90 Hz
            // cannot change which walls fade, only when within a fraction of the dwell.
            //
            // DEFAULT 0 = every frame = today's behaviour; the [Perf] STEPS line's "WallFade.Late"
            // entry is what decides whether raising it is worth anything on real hardware.
            bool evaluate = true;
            float evalInterval = PerfConfig.WallFadeInterval;
            if (evalInterval > 0f)
            {
                if (now < _nextEvalTime)
                    evaluate = false;
                else
                    _nextEvalTime = now + evalInterval;
            }
            int visibleCount = evaluate ? UpdateSampleVisibility(head!) : _lastVisibleCount;
            _lastVisibleCount = visibleCount;
            bool reevalArmed = now - _lastReevalTime <= ReevalArmSeconds;

            float fadeStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeTauSeconds);
            // The coverage EMA advances by the time since the last EVALUATION, not since the last
            // frame — otherwise skipping evaluations would silently stretch its time constant and
            // change the fade decision, which is exactly what the interval must NOT do. With the
            // interval at 0 this is bit-identical to the old Time.unscaledDeltaTime term.
            float evalDt = evaluate
                ? (_lastEvalTime > 0f ? Mathf.Min(now - _lastEvalTime, 0.5f) : Time.unscaledDeltaTime)
                : 0f;
            if (evaluate)
                _lastEvalTime = now;
            float fracStep = 1f - Mathf.Exp(-evalDt / FractionTauSeconds);
            // Live thresholds (WallFadeTuning, clamped): tuning a stepper in the settings
            // panel re-shapes the Schmitt trigger / dwells on the very next evaluation.
            float onFraction = WallFadeTuning.On;
            float offFraction = WallFadeTuning.Off;
            float exitDwellMoved = WallFadeTuning.DwellMoved;
            float exitDwellStationary = WallFadeTuning.DwellStationary;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;

                // Room-coverage metric (EMA-smoothed) with the stepper-driven Schmitt
                // trigger + dwell hysteresis. The un-fade dwell is long, and much longer
                // still unless the perspective (head position / world grip) recently
                // changed — rotation-only head motion keeps the current state sticky.
                if (evaluate)
                {
                    float fraction = BlockedFraction(seg, headPos);
                    seg.LastRaw = fraction;
                    if (!seg.SmoothInit)
                    {
                        seg.SmoothInit = true;
                        seg.Smooth = fraction;
                    }
                    else
                    {
                        seg.Smooth += (fraction - seg.Smooth) * fracStep;
                    }
                    bool raw = seg.Smooth >= (seg.State ? offFraction : onFraction);
                    if (raw != seg.PendingRaw)
                    {
                        seg.PendingRaw = raw;
                        seg.PendingSince = now;
                    }
                    if (seg.PendingRaw != seg.State)
                    {
                        float dwell = seg.PendingRaw
                            ? EnterDwellSeconds
                            : (reevalArmed ? exitDwellMoved : exitDwellStationary);
                        if (now - seg.PendingSince >= dwell)
                        {
                            seg.State = seg.PendingRaw;
                            LogStateFlip(seg); // R2 deliverable: name the wall's shader variant
                        }
                    }
                }

                // Critically-damped-style exponential fade toward the debounced state.
                float target = seg.State ? 1f : 0f;
                seg.Fade += (target - seg.Fade) * fadeStep;
                if (Mathf.Abs(target - seg.Fade) < 0.005f)
                    seg.Fade = target;

                Apply(seg);
            }

            // [Optimize] QuietDiagnostics: the 2 Hz 'diag:' sweep is by far the mod's longest
            // log line (it names every tracked wall with eight numbers each) and it was the single
            // biggest contributor to the hardware log's size. It measured well under one line per
            // second, so it is NOT a frame-time problem and stays ON by default — but a clean
            // performance capture wants only the [Perf] lines, and this is the switch for that.
            if (now >= _nextDiagTime && !PerfConfig.Quiet)
            {
                _nextDiagTime = now + DiagIntervalSeconds;
                LogDiagnostic(headPos, visibleCount);
            }

            if (!_heartbeatLogged)
            {
                _heartbeatLogged = true;
                int highSegs = 0, lowSegs = 0;
                foreach (Segment s in _segments.Values)
                {
                    if (s.VariantHigh) highSegs++;
                    if (s.VariantLow) lowSegs++;
                }
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': tracking "
                    + $"{_segments.Count} wall segments (shader variants: {lowSegs} LOW / "
                    + $"{highSegs} HIGH) against {_roomBounds.Count} room-renderer "
                    + $"bounds / {_allSamples.Count} floor samples ({_roomsAnchored}/"
                    + $"{_roomBounds.Count} rooms tile-anchored, plane +"
                    + $"{FloorSampleEpsilon:0.00} wu, y {_sampleYMin:F2}..{_sampleYMax:F2}) — "
                    + $"per-wall ROOM-coverage fade "
                    + $"(EMA tau {FractionTauSeconds:0.00}s; on ≥{onFraction:0.00}, off "
                    + $"<{offFraction:0.00}; dwell {EnterDwellSeconds:0.00}s in, "
                    + $"{exitDwellMoved:0.0}s out moved / "
                    + $"{exitDwellStationary:0.0}s stationary — live config [WallFade]; "
                    + $"tau {FadeTauSeconds:0.00}s).");
            }
        }

        // ---- occlusion decision -----------------------------------------------------------

        /// <summary>
        /// Track PERSPECTIVE changes that should re-arm aggressive re-evaluation (each sets
        /// <see cref="_lastReevalTime"/> = now): recenter / rig rebuild (RigPoseVersion bumps
        /// there), rig-root motion beyond epsilon (world-grab drag/scale, snap-turn), a
        /// room-bounds shift flagged by <see cref="Rescan"/> (board moved/tilted), and — the
        /// primary anchor — real head TRANSLATION: the head camera's localPosition lives in
        /// tracking space (meters, independent of the diorama scale), so a &gt;0.18m move from
        /// the anchor re-arms and re-anchors while micro-sway and pure rotation never do.
        /// </summary>
        private void UpdatePerspectiveState(Transform headT, float now)
        {
            int pv = Rig.VRRigDriver.RigPoseVersion;
            if (pv != _lastPoseVersion)
            {
                _lastPoseVersion = pv;
                _lastReevalTime = now;
            }

            Transform? rig = Rig.VRRigDriver.RigRoot;
            if (rig != null)
            {
                Vector3 p = rig.position;
                Quaternion q = rig.rotation;
                float s = rig.lossyScale.x;
                if (!_rigSnapInit)
                {
                    _rigSnapInit = true;
                    _rigPos = p;
                    _rigRot = q;
                    _rigScale = s;
                }
                else if ((p - _rigPos).sqrMagnitude > 0.0004f * s * s // 2cm real, scale-aware
                         || Quaternion.Angle(q, _rigRot) > 0.5f
                         || Mathf.Abs(s - _rigScale) > 0.005f * Mathf.Max(_rigScale, 0.001f))
                {
                    _rigPos = p;
                    _rigRot = q;
                    _rigScale = s;
                    _lastReevalTime = now;
                }
            }

            if (_roomBoundsMoved)
            {
                _roomBoundsMoved = false;
                _lastReevalTime = now;
            }

            Vector3 headLocal = headT.localPosition;
            if (!_headAnchorInit)
            {
                _headAnchorInit = true;
                _headAnchor = headLocal;
            }
            else if ((headLocal - _headAnchor).sqrMagnitude
                     > HeadMoveReevalMeters * HeadMoveReevalMeters)
            {
                _headAnchor = headLocal;
                _lastReevalTime = now;
            }
        }

        /// <summary>
        /// Refresh the per-sample frustum flags (viewport test with margin) for ALL
        /// precomputed floor samples. Returns the overall visible count (diagnostic only —
        /// the metric reads the flags per room).
        /// </summary>
        private int UpdateSampleVisibility(Camera head)
        {
            int visible = 0;
            int n = Mathf.Min(_allSamples.Count, _sampleVisible.Length);
            for (int i = 0; i < n; i++)
            {
                // Mono view/projection of the head camera; per-eye stereo frustums differ
                // only by half the IPD and a slightly wider horizontal FOV — FrustumMargin
                // (0.20 viewport-relative) generously covers that skew.
                Vector3 vp = head.WorldToViewportPoint(_allSamples[i]);
                bool vis = vp.z > 0f
                    && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin
                    && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin;
                _sampleVisible[i] = vis;
                if (vis)
                    visible++;
            }
            return visible;
        }

        /// <summary>
        /// Fraction of the wall's OWN room's floor grid that the wall hides from the head:
        /// numerator = room grid points that are in view-direction (frustum flag) AND whose
        /// head→point segment the wall AABB clearly interrupts; denominator = the room's
        /// WHOLE grid (see class header for why the denominator is not frustum-culled).
        ///
        /// SELF-TEST (worked example, world scale 20 — 1 real m = 20 wu, 1 wu = 5 real cm;
        /// numbers chosen to match the round-6 hardware log: floor tile plane y = 0, wall
        /// tops ≈ 3.5, head standing 0.6 real m above the board):
        ///   Room: 5-hex ≈ 8.6 wu square footprint, x ∈ [0.3, 8.9], 4×4 grid → 16 points
        ///     (denominator), sample columns at x ≈ {1.38, 3.53, 5.68, 7.83}, y = 0.05.
        ///   Wall: run along the room's near edge, AABB x ∈ [−0.5, 0.3] (thickness 0.8 →
        ///     BlockEps = 0.4), y ∈ [−0.3, 3.5], z spanning the room. Player looks into
        ///     the room → all 16 points pass the frustum test.
        ///   (a) LEANING IN, head (−1.0, 4.5, roomMidZ) — 22.5 real cm above the floor,
        ///     5 cm outside the wall face. A ray to column x_s drops 4.45 wu; it reaches
        ///     the wall-top plane y = 3.5 at parameter t = 1.0/4.45 = 0.2247, i.e. at
        ///     x = −1 + 0.2247·(x_s+1): col 1 → x = −0.47, col 2 → x = 0.02 (both inside
        ///     the slab [−0.5, 0.3] → blocked), col 3 → x = 0.50, col 4 → x = 0.98 (past
        ///     the slab while still above the top → miss). Epsilon check col 2: dist =
        ///     √(4.53² + 4.45²) = 6.35, entry ≈ 0.2247·6.35 = 1.43 &lt; 6.35 −
        ///     max(0.4, 0.05·6.35 = 0.32) = 5.95 ✓. → 8/16 = 0.50 ≥ 0.25 → ON: the EMA
        ///     (tau 0.15s) crosses 0.25 after 0.15·ln(0.50/(0.50−0.25)) ≈ 0.10s, plus the
        ///     0.2s enter dwell → fades ~0.3s after the lean settles.
        ///   (b) STANDING TALL, head (−6, 12, roomMidZ) — 0.6 real m up, 0.3 m back: only
        ///     col 1 is shadowed (slab crossing y: 3.10→1.80 inside; col 2 stays ≥ 4.10
        ///     above the top) → 4/16 = 0.25, exactly the default bar — the marginal case.
        ///   Between (a) and (b) the shadow reach grows continuously as the head lowers,
        ///   so a pose hiding ~30% of the room (5/16 = 0.3125, e.g. col 1 + the first
        ///   oblique col-2 point) sits comfortably above the default 0.25: EMA crosses at
        ///   0.15·ln(0.3125/0.0625) ≈ 0.24s → ON ~0.45s after the pose settles. A wall
        ///   hiding ~30% of its room's floor therefore reliably triggers at default 0.25.
        /// </summary>
        private float BlockedFraction(Segment seg, Vector3 headPos)
        {
            seg.LastBlocked = 0;
            seg.LastRoomVisible = 0;
            seg.LastRoomTotal = 0;
            int room = seg.RoomIndex;
            if (room < 0 || room >= _roomSampleCount.Count)
                return 0f;
            int total = _roomSampleCount[room];
            seg.LastRoomTotal = total;
            if (total <= 0)
                return 0f;

            Bounds b = seg.Bounds;
            if (b.Contains(headPos))
            {
                // Wall in the face — treat as full coverage of its room.
                seg.LastBlocked = total;
                seg.LastRoomVisible = total;
                return 1f;
            }

            int start = _roomSampleStart[room];
            int end = Mathf.Min(start + total, Mathf.Min(_allSamples.Count, _sampleVisible.Length));
            float thicknessEps = seg.BlockEps;
            int blocked = 0, roomVisible = 0;
            for (int i = start; i < end; i++)
            {
                if (!_sampleVisible[i])
                    continue; // out of view-direction — cannot be "hidden by the wall"
                roomVisible++;
                Vector3 sample = _allSamples[i];
                Vector3 to = sample - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                // Generous "clearly before the point": wall entry must precede the sample
                // by max(half wall thickness, 5% of the ray length) — thickness alone is
                // too strict for long grazing rays, a pure percentage was the round-4 bug.
                float eps = Mathf.Max(thicknessEps, BlockEpsDistFraction * dist);
                var ray = new Ray(headPos, to / dist);
                if (b.IntersectRay(ray, out float d)
                    && (d < dist - eps || b.Contains(sample)))
                {
                    blocked++;
                }
            }
            seg.LastBlocked = blocked;
            seg.LastRoomVisible = roomVisible;
            return blocked / (float)total;
        }

        /// <summary>
        /// Throttled hardware diagnostic (1 line / <see cref="DiagIntervalSeconds"/>s while
        /// WallFade is active): overall sample/frustum stats plus the top-3 candidate walls
        /// by smoothed fraction — name, raw/EMA fraction, blocked/visible counts, state and
        /// fade, wall-AABB y-range, blocked epsilon, and an explicit "!ABOVE-WALL" marker
        /// when every sample sits above that wall's AABB top (the round-4 scale bug this
        /// line exists to catch). Next hardware log pinpoints any remaining miss from this.
        /// </summary>
        /// <summary>
        /// R2 deliverable: every debounced fade state flip logs the wall's fade-shader
        /// name(s) + variant and which held-state math therefore applies (rare event —
        /// unthrottled on purpose so hardware logs pin each fade to its variant).
        /// </summary>
        private static void LogStateFlip(Segment seg)
        {
            string wall = seg.Wall != null ? seg.Wall.name : "<dead>";
            string variant = seg.VariantHigh ? (seg.VariantLow ? "HIGH+LOW" : "HIGH") : "LOW";
            if (seg.State)
            {
                string cutoff = $"map occ(r=1,a=0)→m=0, _Cutoff={seg.HeldCutoff:0.00} " +
                    (seg.CutoffAuthored ? "(authored)" : "(fallback)");
                VRLog.Info(Name,
                    $"fade ON '{wall}' shader '{seg.ShaderNames}' [{variant}] — held state: " +
                    cutoff + " → " +
                    (seg.VariantHigh
                        ? "world-Y foundation gradient solid (S=1 ⇒ clip=1-c), upper wall " +
                          "discarded (clip=-c); game-native screen vignette/0.02·dist terms " +
                          "remain inside S — flat-game faded look"
                        : "discard above object-Y 0.4 only — base course below the hard " +
                          "shader gate stays solid (flat-game faded look, view-independent)"));
            }
            else
            {
                VRLog.Info(Name, $"fade OFF '{wall}' [{variant}] — MPB removed, solid.");
            }
        }

        private void LogDiagnostic(Vector3 headPos, int visibleCount)
        {
            if (_segments.Count == 0)
                return;
            Segment? s1 = null, s2 = null, s3 = null;
            foreach (Segment seg in _segments.Values)
            {
                if (!seg.HasBounds)
                    continue;
                if (s1 == null || seg.Smooth > s1.Smooth) { s3 = s2; s2 = s1; s1 = seg; }
                else if (s2 == null || seg.Smooth > s2.Smooth) { s3 = s2; s2 = seg; }
                else if (s3 == null || seg.Smooth > s3.Smooth) { s3 = seg; }
            }
            if (s1 == null)
                return;

            _diagSb.Length = 0;
            _diagSb.Append("diag: vis ").Append(visibleCount).Append('/').Append(_allSamples.Count)
                   .Append(" headY ").Append(headPos.y.ToString("F2"))
                   .Append(" sampY[").Append(_sampleYMin.ToString("F2")).Append("..")
                   .Append(_sampleYMax.ToString("F2")).Append(']');
            AppendSegDiag(s1);
            AppendSegDiag(s2);
            AppendSegDiag(s3);
            VRLog.Info(Name, _diagSb.ToString());
        }

        private void AppendSegDiag(Segment? seg)
        {
            if (seg == null)
                return;
            string name = seg.Wall != null ? seg.Wall.name : "<dead>";
            if (name.Length > 24)
                name = name.Substring(0, 24);
            Bounds b = seg.Bounds;
            _diagSb.Append(" | '").Append(name)
                   .Append("' r").Append(seg.RoomIndex)
                   .Append(" raw").Append(seg.LastRaw.ToString("F2"))
                   .Append(" ema").Append(seg.Smooth.ToString("F2"))
                   .Append(" blk").Append(seg.LastBlocked).Append('/').Append(seg.LastRoomTotal)
                   .Append(" v").Append(seg.LastRoomVisible)
                   .Append(seg.State ? " ON " : " off ").Append(seg.Fade.ToString("F2"))
                   .Append(" wy[").Append(b.min.y.ToString("F2")).Append("..")
                   .Append(b.max.y.ToString("F2")).Append(']')
                   .Append(" e").Append(seg.BlockEps.ToString("F2"))
                   .Append(seg.VariantHigh ? (seg.VariantLow ? " vH+L" : " vHIGH") : " vLOW");
            // Tripwire: this wall's own room's sample plane sits above the wall AABB top —
            // the exact frame-mismatch class the round-6 hardware log caught (sampY 9.05 vs
            // wall tops ≤3.67: bounds-derived plane, occlusion-proxy meshes).
            int room = seg.RoomIndex;
            float planeY = room >= 0 && room < _roomFloorY.Count
                ? _roomFloorY[room] + FloorSampleEpsilon
                : _sampleYMin;
            if (planeY > b.max.y)
                _diagSb.Append(" !ABOVE-WALL");
            if (room >= 0 && room < _roomFloorAnchored.Count && !_roomFloorAnchored[room])
                _diagSb.Append(" !UNANCHORED");
        }

        // ---- fade delivery ----------------------------------------------------------------

        /// <summary>
        /// Apply the segment's fade through the shader's own path (see class header).
        /// Reapplied every frame while faded because Apparance may regenerate wall renderers
        /// mid-fade; a null renderer triggers a prompt rescan.
        /// </summary>
        private void Apply(Segment seg)
        {
            if (seg.Fade <= 0f)
            {
                if (seg.HasBlock)
                {
                    seg.HasBlock = false;
                    foreach (MeshRenderer r in seg.Renderers)
                    {
                        if (r != null)
                            r.SetPropertyBlock(null);
                    }
                }
                return;
            }

            if (!EnsureTextures())
                return;
            _mpb ??= new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetInteger(ToggleWallFadeId, 1);
            // HIGH-variant map scale M = m·_ToggleWallfade (cb0[6].x) — pin to 1 so the held
            // math below holds regardless of the material's authored value; the LOW shader
            // has no such property (MPB entry simply unused there).
            _mpb.SetFloat(ToggleWallfadeMatId, 1f);
            if (seg.Fade >= 1f)
            {
                // Held fully faded (R3, foundation-band fix): constant r=1,a=0 map → map
                // term m = 1-r = 0 view-independently (a=0 fails the depth compare for
                // every visible fragment under either Z convention), _Cutoff = the
                // material's own authored Mask Clip Value — exactly the state the flat
                // game's occlusion map produces over a revealed room. LOW: clip = -c < 0
                // discards everything ABOVE the shader's hard objY-0.4 gate, base course
                // solid. HIGH: M=0 → the shader's own world-Y ramp keeps the foundation
                // gradient solid (S=1 → A·B=1, noise ×0) and discards the upper wall
                // (S=0 → clip = -c). Full math + residual game-native vignette terms in
                // the class header.
                _mpb.SetTexture(TilesOcclusionMapId, _occludedTex!);
                _mpb.SetFloat(CutoffId, seg.HeldCutoff);
            }
            else
            {
                // Dissolve: sweep the clip threshold across the noise texture's value range
                // (screen-space pattern — cosmetic, confined to the ~0.35s transition).
                _mpb.SetTexture(TilesOcclusionMapId, _noiseTex!);
                _mpb.SetFloat(CutoffId, Mathf.Lerp(-0.05f, 1f, seg.Fade));
            }

            seg.HasBlock = true;
            bool lostRenderer = false;
            foreach (MeshRenderer r in seg.Renderers)
            {
                if (r == null)
                {
                    lostRenderer = true;
                    continue;
                }
                r.SetPropertyBlock(_mpb);
            }
            if (lostRenderer)
                _nextRescan = 0f; // wall regenerated mid-fade — re-collect promptly
        }

        // ---- segment / play-area bookkeeping ------------------------------------------------

        /// <summary>
        /// Refresh the play-area bounds (from the generator's live room-renderer list) and the
        /// wall-segment table (from ProceduralWall.m_WallCache, publicized static). Renderers
        /// are matched by shader name ("WallFade") so props/doors under the same entity are
        /// never touched.
        /// </summary>
        private void Rescan(TilesOcclusionGenerator gen)
        {
            // Tile-plane anchors (round 7): each TilesOcclusionVolume knows its room's
            // renderers AND its CentralTile, whose transform sits ON the tile plane. The
            // renderer bounds are only trusted for the XZ footprint — their Y is the
            // occlusion-proxy artifact the round-6 hardware log caught (tops ~9 wu above
            // the actual floor, see class header).
            _floorYByRenderer.Clear();
            TilesOcclusionVolume[] volumes = UnityEngine.Object.FindObjectsOfType<TilesOcclusionVolume>();
            foreach (TilesOcclusionVolume v in volumes)
            {
                if (v == null || v.CentralTile == null || v.Renderers == null)
                    continue;
                float tileY = v.CentralTile.transform.position.y;
                foreach (MeshRenderer vr in v.Renderers)
                {
                    if (vr != null)
                        _floorYByRenderer[vr] = tileY;
                }
            }

            _roomBounds.Clear();
            _roomFloorY.Clear();
            _roomFloorAnchored.Clear();
            foreach (MeshRenderer r in gen.m_RoomRenderers)
            {
                if (r == null)
                    continue;
                _roomBounds.Add(r.bounds);
                bool anchored = _floorYByRenderer.TryGetValue(r, out float floorY);
                _roomFloorY.Add(anchored ? floorY : float.NaN);
                _roomFloorAnchored.Add(anchored);
            }
            _builtRoomCount = gen.m_RoomRenderers.Count;

            // Fallback for rooms without a volume match: median anchored height (rooms of
            // one scenario share the board plane), else the old bounds top — with the
            // !ABOVE-WALL/!UNANCHORED diag tripwires flagging that degraded mode.
            _floorYScratch.Clear();
            for (int i = 0; i < _roomFloorY.Count; i++)
            {
                if (_roomFloorAnchored[i])
                    _floorYScratch.Add(_roomFloorY[i]);
            }
            _roomsAnchored = _floorYScratch.Count;
            float fallbackY = float.NaN;
            if (_floorYScratch.Count > 0)
            {
                _floorYScratch.Sort();
                fallbackY = _floorYScratch[_floorYScratch.Count / 2];
            }
            for (int i = 0; i < _roomFloorY.Count; i++)
            {
                if (_roomFloorAnchored[i])
                    continue;
                _roomFloorY[i] = float.IsNaN(fallbackY) ? _roomBounds[i].max.y : fallbackY;
            }

            // Board moved/tilted or a room got revealed → the perspective onto the play area
            // changed; flag it so UpdatePerspectiveState re-arms aggressive re-evaluation.
            if (_roomBounds.Count > 0)
            {
                Vector3 combined = Vector3.zero;
                foreach (Bounds b in _roomBounds)
                    combined += b.center;
                combined /= _roomBounds.Count;
                if (_roomCenterInit && (combined - _roomCenter).sqrMagnitude > 0.0001f)
                    _roomBoundsMoved = true;
                _roomCenter = combined;
                _roomCenterInit = true;
            }

            // Drop segments whose wall died (their renderers died with them).
            _deadWalls.Clear();
            foreach (KeyValuePair<ProceduralWall, Segment> kv in _segments)
            {
                if (kv.Key == null)
                    _deadWalls.Add(kv.Key!); // destroyed Unity object — reference still hashes
            }
            foreach (ProceduralWall dead in _deadWalls)
                _segments.Remove(dead);

            // Adopt new walls / refresh renderer lists, shader-variant info and bounds.
            List<ProceduralWall> cache = ProceduralWall.m_WallCache;
            for (int i = 0; i < cache.Count; i++)
            {
                ProceduralWall wall = cache[i];
                if (wall == null)
                    continue;
                if (!_segments.TryGetValue(wall, out Segment? seg))
                {
                    seg = new Segment { Wall = wall };
                    _segments.Add(wall, seg);
                }
                RefreshSegment(seg);
            }

            RebuildSamples();
            AssociateRooms();
        }

        /// <summary>
        /// Bind every wall segment to the ONE room whose AABB it borders: smallest XZ gap
        /// between wall AABB and room AABB (a wall bordering its room touches it → gap 0;
        /// Y is ignored — room-bounds Y is the untrusted proxy axis). Near-ties (a door
        /// wall between two rooms) go to the room whose center is nearer to the wall.
        /// </summary>
        private void AssociateRooms()
        {
            foreach (Segment seg in _segments.Values)
            {
                seg.RoomIndex = -1;
                if (!seg.HasBounds)
                    continue;
                Bounds w = seg.Bounds;
                float bestGap = float.PositiveInfinity;
                float bestCenter = float.PositiveInfinity;
                for (int r = 0; r < _roomBounds.Count; r++)
                {
                    Bounds room = _roomBounds[r];
                    float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                    float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                    float gap = gx * gx + gz * gz;
                    float cx = room.center.x - w.center.x;
                    float cz = room.center.z - w.center.z;
                    float center = cx * cx + cz * cz;
                    if (gap < bestGap - 0.0001f
                        || (gap <= bestGap + 0.0001f && center < bestCenter))
                    {
                        bestGap = gap;
                        bestCenter = center;
                        seg.RoomIndex = r;
                    }
                }
            }
        }

        /// <summary>
        /// Precompute the FLOOR occlusion samples: a per-room XZ grid (footprint from the
        /// room renderer bounds — XZ is the trusted axis) placed ON the tile-anchored
        /// floor plane (<see cref="_roomFloorY"/> + <see cref="FloorSampleEpsilon"/>), as
        /// dense as the room budget allows under <see cref="MaxTotalSamples"/>
        /// (4×4 → 3×3 → 2×2 → center per room). Samples are room-contiguous; each room's
        /// [start,count) range doubles as the coverage-fraction denominator.
        /// </summary>
        private void RebuildSamples()
        {
            _allSamples.Clear();
            _roomSampleStart.Clear();
            _roomSampleCount.Clear();
            _sampleYMin = float.PositiveInfinity;
            _sampleYMax = float.NegativeInfinity;
            int rooms = _roomBounds.Count;
            if (rooms == 0)
            {
                _sampleYMin = _sampleYMax = 0f;
                return;
            }
            int grid = rooms * 16 <= MaxTotalSamples ? 4
                : rooms * 9 <= MaxTotalSamples ? 3
                : rooms * 4 <= MaxTotalSamples ? 2
                : 1;
            for (int r = 0; r < rooms; r++)
            {
                _roomSampleStart.Add(_allSamples.Count);
                if (_allSamples.Count + grid * grid > MaxTotalSamples)
                {
                    _roomSampleCount.Add(0); // over budget — room gets no grid this rescan
                    continue;
                }
                Bounds b = _roomBounds[r];
                float y = _roomFloorY[r] + FloorSampleEpsilon;
                if (y < _sampleYMin) _sampleYMin = y;
                if (y > _sampleYMax) _sampleYMax = y;
                for (int ix = 0; ix < grid; ix++)
                {
                    float x = Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / grid);
                    for (int iz = 0; iz < grid; iz++)
                    {
                        float z = Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / grid);
                        _allSamples.Add(new Vector3(x, y, z));
                    }
                }
                _roomSampleCount.Add(grid * grid);
            }
            if (float.IsInfinity(_sampleYMin))
                _sampleYMin = _sampleYMax = 0f;
        }

        /// <summary>
        /// Re-collect a segment's fade-capable renderers, combined AABB and shader-variant
        /// info (R2 diag: LOW = name contains "Low", e.g. Amp_Low/Amp_Basic_WallFade_Low
        /// from misc_shaders; anything else WallFade-capable is the HIGH misc_high_shaders
        /// variant).
        /// </summary>
        private void RefreshSegment(Segment seg)
        {
            seg.Renderers.Clear();
            seg.HasBounds = false;
            seg.VariantHigh = false;
            seg.VariantLow = false;
            seg.ShaderNames = "?";
            seg.HeldCutoff = 0.5f;
            seg.CutoffAuthored = false;
            if (seg.Wall == null)
                return;
            MeshRenderer[] all = seg.Wall.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null || !CollectWallFadeInfo(r, seg))
                    continue;
                seg.Renderers.Add(r);
                if (!seg.HasBounds)
                {
                    seg.Bounds = r.bounds;
                    seg.HasBounds = true;
                }
                else
                {
                    seg.Bounds.Encapsulate(r.bounds);
                }
                // Renderer may be brand new (Apparance rebuild) while the segment is mid-fade —
                // Apply() runs every frame for faded segments and will cover it.
            }
            if (seg.HasBounds)
            {
                // Blocked-test epsilon ≈ half the wall run's thickness (the smaller
                // horizontal AABB extent), clamped to sane world-unit bounds — an L-shaped
                // corner run has two large extents, hence the upper clamp.
                float thickness = Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z);
                seg.BlockEps = Mathf.Clamp(0.5f * thickness, BlockEpsMinWorld, BlockEpsMaxWorld);
            }
        }

        /// <summary>
        /// Does any shared material use one of the wall-fade shaders? Also records the
        /// shader name(s) and LOW/HIGH variant flags on the segment (rescan-time only —
        /// the string concat below runs once per distinct shader name per rescan).
        /// </summary>
        private bool CollectWallFadeInfo(MeshRenderer r, Segment seg)
        {
            bool any = false;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null)
                    continue;
                string shaderName = m.shader.name;
                if (!shaderName.Contains("WallFade"))
                    continue;
                any = true;
                // Held-state cutoff = the material's authored "Mask Clip Value" — the flat
                // game never writes _Cutoff, so this IS the value its fade runs with.
                // Clamped away from 0/1: with the held map's m = 0 any 0<c<1 produces the
                // identical geometry (c only shapes the HIGH variant's dither density),
                // while c = 0 would disable the LOW discard and c ≥ 1 would kill the HIGH
                // foundation band (clip = 1-c).
                if (!seg.CutoffAuthored && m.HasProperty(CutoffId))
                {
                    seg.HeldCutoff = Mathf.Clamp(m.GetFloat(CutoffId), 0.05f, 0.95f);
                    seg.CutoffAuthored = true;
                }
                if (shaderName.Contains("Low"))
                    seg.VariantLow = true;
                else
                    seg.VariantHigh = true;
                if (seg.ShaderNames == "?")
                    seg.ShaderNames = shaderName;
                else if (!seg.ShaderNames.Contains(shaderName))
                    seg.ShaderNames += "+" + shaderName;
            }
            return any;
        }

        // ---- textures / teardown ------------------------------------------------------------

        /// <summary>
        /// Create the two delivery textures. Noise: 64x64 value noise (Mathf.PerlinNoise),
        /// rank-flattened to a uniform histogram over [0.06,1] so the _Cutoff sweep dissolves at
        /// a constant area-rate; low frequency keeps the left/right-eye patterns correlated
        /// (screen-space sampling differs per eye only by disparity); alpha 0 = "play area
        /// behind every pixel" under the shader's reversed-Z compare, m = 1-noise. Occluded
        /// (held) texture: r=1 AND a=0 — the depth compare fails for every visible fragment
        /// under either Z convention, so the map term m = 1-r = 0 is a CONSTANT: exactly the
        /// value the flat game's occlusion map yields over a revealed room, which lets each
        /// shader variant's own foundation-band terms survive (see class header).
        /// </summary>
        private bool EnsureTextures()
        {
            if (_noiseTex != null && _occludedTex != null)
                return true;

            const int size = 64;
            _noiseTex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            int n = size * size;
            var values = new float[n];
            var order = new int[n];
            for (int i = 0; i < n; i++)
            {
                int x = i % size, y = i / size;
                // ~5 noise cells across the texture (which spans the SCREEN when sampled).
                values[i] = Mathf.PerlinNoise(x * (5f / size) + 11.31f, y * (5f / size) + 47.77f);
                order[i] = i;
            }
            Array.Sort(order, (a, b) => values[a].CompareTo(values[b]));
            var pixels = new Color32[n];
            for (int rank = 0; rank < n; rank++)
            {
                byte r = (byte)Mathf.RoundToInt(Mathf.Lerp(0.06f, 1f, (rank + 0.5f) / n) * 255f);
                pixels[order[rank]] = new Color32(r, 0, 0, 0);
            }
            _noiseTex.SetPixels32(pixels);
            _noiseTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _occludedTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeOccluded",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var occluded = new Color32[4];
            for (int i = 0; i < 4; i++)
                occluded[i] = new Color32(255, 0, 0, 0); // r=1, a=0 → m ≡ 1-r = 0 ("room behind")
            _occludedTex.SetPixels32(occluded);
            _occludedTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return true;
        }

        private void ClearAllBlocks(string reason)
        {
            int cleared = 0;
            foreach (Segment seg in _segments.Values)
            {
                seg.State = false;
                seg.PendingRaw = false;
                seg.Fade = 0f;
                seg.Smooth = 0f;
                seg.SmoothInit = false;
                if (!seg.HasBlock)
                    continue;
                seg.HasBlock = false;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null)
                    {
                        r.SetPropertyBlock(null);
                        cleared++;
                    }
                }
            }
            if (cleared > 0)
                VRLog.Info(Name, $"cleared property blocks on {cleared} renderers ({reason}).");
        }

        /// <summary>Full teardown: revert every renderer and destroy our textures.</summary>
        internal void Teardown()
        {
            try { ClearAllBlocks("teardown"); }
            catch { /* renderers already dying with the scene */ }
            _segments.Clear();
            _roomBounds.Clear();
            _roomFloorY.Clear();
            _roomFloorAnchored.Clear();
            _roomSampleStart.Clear();
            _roomSampleCount.Clear();
            _allSamples.Clear();
            _floorYByRenderer.Clear();
            if (_noiseTex != null)
            {
                try { Destroy(_noiseTex); } catch { /* already gone */ }
                _noiseTex = null;
            }
            if (_occludedTex != null)
            {
                try { Destroy(_occludedTex); } catch { /* already gone */ }
                _occludedTex = null;
            }
        }
    }
}
