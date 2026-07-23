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
/// <item>HELD FADED (fade=1) — R2, IDENTICAL MPB on BOTH variants, provably ZERO view
///   dependence: map = constant r=1 <b>a=1</b> texture, <c>_Cutoff=1.1</c>,
///   <c>_ToggleWallfade=1</c>. occ.a=1 forces the per-pixel depth compare TRUE under ANY
///   depth convention (fragDepth ≤ 1 always), killing R2 suspect (b): <c>m = 1</c> is a
///   CONSTANT, no map/depth term varies with the view. LOW: <c>clip = 1 - 1.1 &lt; 0</c> →
///   constant discard wherever objY ≥ 0.4; the hard object-Y band below stays
///   constant-solid ("bis auf die Grundmauern"). HIGH: <c>M=1</c> → <c>B=1</c> and
///   <c>A = max(1,S) + 42n·(1-max(1,S)) = 1</c> — the screen-radial vignette S (R2 suspect
///   (a)) and the animated noise n are each MULTIPLIED BY ZERO; <c>clip = 1 + T·(1·1-1)
///   - 1.1 = -0.1</c> constant → TOTAL discard of every fragment. IMPOSSIBILITY NOTE
///   (why HIGH cannot keep its foundation band statically): the band term (1-worldY)/3
///   and the vignette (0.02·dist+screenRadial)^8 are summed inside S BEFORE the single
///   cutoff compare — S saturates to 1 both for worldY ≤ 0.1 (band) and for peripheral
///   pixels ((0.02d+radial)^8 ≥ 0.3), producing the IDENTICAL clip value 1-_Cutoff, so no
///   _Cutoff/ToggleWallFade/map choice separates them; every vignette coefficient (0.02,
///   the screen-center offset, ×3.33) is an immediate literal in the DXBC, not a material
///   property, so nothing neutralizes it either. Hence: LOW held = static fade with
///   foundation band (the expected path on this rig); HIGH held = static TOTAL discard,
///   band sacrificed — the only view-independent option that exists. Every fade logs the
///   wall's shader name + variant so a hardware log pins down which math applied.</item>
/// <item>SOLID (fade=0): the MPB is REMOVED — with <see cref="Compat.WallFadeDisable"/> now
///   pinning the GLOBAL <c>ToggleWallFade</c> to 0 unconditionally (the game-camera
///   TilesOcclusionGenerator still publishes a head-viewpoint-invalid map; globally-open
///   fade would sample garbage), an untouched renderer is bit-for-bit today's solid wall.</item>
/// </list>
///
/// OCCLUSION DECISION (per segment, VR-stable — round 6, FLOOR coverage; R1: the fraction
/// now literally means "share of the FLOOR (hex tiles) currently in view that this wall
/// hides" — round 5's two mid-wall-band sample layers also counted MID-AIR points a wall
/// hid without hiding any actual floor, so the on/off thresholds never matched what the
/// player perceived as covered play area): the floor is sampled with a per-room XZ grid
/// (4×4 while the room budget allows, then 3×3/2×2/center) ON the room tile bounds' TOP
/// surface — floor height + 0.05 wu epsilon; the room renderers ARE the hex-tile meshes
/// the game rasterizes into its occlusion map, so their AABB top is the tile plane.
/// World-unit sanity: the rig is scaled UP by WorldScale ≈ 11–20 (1 wu ≈ 5–9 real cm, hex
/// tile ≈ 1.72 wu) while board geometry keeps original game units — the floor plane needs
/// no height guessing (the round-4 "+0.4 wu off an untrusted bounds top" bug class is
/// gone; the !ABOVE-WALL diag marker stays as a tripwire). Each frame the samples inside
/// the head camera's view frustum (viewport test, 0.20 margin, ≤48 kept) stand in for the
/// CURRENTLY VISIBLE floor region. A segment's raw metric is the largest PER-ROOM fraction
/// of visible floor points whose head→point segment its AABB blocks (per-room
/// normalization: a wall covering the room being looked INTO is not diluted by samples of
/// other visible rooms; denominator floored at 3 so a single stray sample cannot read
/// 1.0). "Blocks" = AABB
/// entry strictly closer than the sample by a wall-thickness epsilon (0.5 × min horizontal
/// AABB extent, clamped 0.10–0.90 wu) OR the sample lying inside the wall AABB — the
/// round-4 "hit before 88% of distance" rule silently discarded exactly the near-edge
/// samples a leaned-in head actually loses sight of. Head inside the AABB counts as 1.0;
/// ≥1 visible sample suffices to decide (round 4 demanded 3 — leaning in close shrinks the
/// visible set to 1–2 precisely when the fade must fire). The raw fraction is EMA-smoothed
/// (tau 0.15s) to kill threshold jitter, then smoothed ≥ 0.25 switches ON ("wall hides
/// &gt;25% of the floor currently in view of some room"); once faded a SCHMITT TRIGGER keeps it ON
/// down to 0.10 — looking around only moves samples in/out of the frustum and rarely
/// drops a still-covering wall below the low bar. Transitions additionally need dwell: 0.10s
/// to fade IN (snappy; the EMA already adds ~0.2s), 2.5s continuously below 0.10 to un-fade
/// — and that un-fade dwell
/// stretches to 7s unless the PERSPECTIVE recently (≤3s) actually changed: real head
/// TRANSLATION &gt;0.18m in tracking space (scale-independent), rig-root motion
/// (world-grab/snap-turn), recenter/rig rebuild (RigPoseVersion), or a room-bounds shift seen
/// at rescan. Net effect: rotation alone almost never un-fades a wall; moving the head or
/// re-gripping the world re-evaluates promptly. The fade value itself stays exponentially
/// damped (tau 0.12s ≈ 0.35s visible transition).
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
            "whole-wall fade: per-ProceduralWall FLOOR-coverage decision (per-room fraction of " +
            "the frustum-visible hex-tile-plane samples hidden, EMA + Schmitt " +
            "trigger + perspective-anchored dwell), " +
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
        /// <summary>R2 diag: which fade-shader variant(s) this segment's renderers carry.</summary>
        public bool VariantHigh;
        public bool VariantLow;
        /// <summary>Distinct fade-shader name(s) seen on the renderers ("+"-joined).</summary>
        public string ShaderNames = "?";
        /// <summary>EMA-smoothed view-coverage fraction the Schmitt trigger reads.</summary>
        public float Smooth;
        public bool SmoothInit;
        // Last-tick raw numbers, kept for the throttled diagnostic.
        public float LastRaw;
        public int LastBlocked;
        public int LastVisible;
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
        private const float EnterDwellSeconds = 0.10f; // short — the fraction EMA already smooths entry
        private const float HeadMoveReevalMeters = 0.18f; // REAL tracking-space meters (scale-independent)
        private const float ReevalArmSeconds = 3f;     // how long a perspective change keeps re-eval armed
        private const float FadeTauSeconds = 0.12f;    // exp. fade time constant (~0.35s to 95%)
        private const float FractionTauSeconds = 0.15f; // EMA over the raw fraction (jitter killer)
        private const float FloorSampleEpsilon = 0.05f; // wu above the room tile bounds' top (R1 floor plane)
        private const float BlockEpsMinWorld = 0.10f;  // wu — blocked-test epsilon clamp (lo)
        private const float BlockEpsMaxWorld = 0.90f;  // wu — blocked-test epsilon clamp (hi)
        private const float FrustumMargin = 0.20f;     // viewport slack (also covers per-eye vs mono skew)
        private const int MaxTotalSamples = 96;        // precomputed floor samples (all rooms)
        private const int MaxVisibleSamples = 48;      // per-frame frustum-visible sample cap
        private const int MinVisibleForDecision = 1;   // leaning in close legitimately leaves 1–2 visible
        private const int RoomDenomFloor = 3;          // per-room fraction denominator floor (noise guard)
        private const float RescanIntervalSeconds = 2f;
        private const float DiagIntervalSeconds = 2f;  // throttled hardware diagnostic cadence

        private static readonly int TilesOcclusionMapId = Shader.PropertyToID("_TilesOcclusionMap");
        private static readonly int ToggleWallFadeId = Shader.PropertyToID("ToggleWallFade");
        private static readonly int ToggleWallfadeMatId = Shader.PropertyToID("_ToggleWallfade");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");

        private readonly Dictionary<ProceduralWall, Segment> _segments = new();
        private readonly List<ProceduralWall> _deadWalls = new();
        private readonly List<Bounds> _roomBounds = new();
        private readonly List<Vector3> _allSamples = new();     // per-room floor-plane grid (R1)
        private readonly List<int> _sampleRoom = new();         // room index per sample (parallel)
        private readonly List<Vector3> _visibleSamples = new(); // frustum-visible subset, per frame
        private readonly List<int> _visibleRoom = new();        // room index per visible sample
        private int[] _visPerRoom = Array.Empty<int>();         // per-frame visible count per room
        private int[] _blkPerRoom = Array.Empty<int>();         // per-segment blocked count per room
        private readonly List<Material> _matScratch = new();
        private readonly System.Text.StringBuilder _diagSb = new();
        private float _sampleYMin, _sampleYMax;                 // overall sample-height range (diag)
        private float _nextDiagTime;
        private MaterialPropertyBlock? _mpb;

        private Texture2D? _noiseTex; // transition dissolve pattern (r in [0.06,1], a=0)
        private Texture2D? _fullTex;  // held-faded constant (r=1, a=0)

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
            int visibleCount = CollectVisibleSamples(head!);
            bool reevalArmed = now - _lastReevalTime <= ReevalArmSeconds;

            float fadeStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeTauSeconds);
            float fracStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FractionTauSeconds);
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

                // Floor-coverage metric (EMA-smoothed) with a Schmitt trigger (0.25 on /
                // 0.10 off) + dwell hysteresis. The un-fade dwell is long, and much longer
                // still unless the perspective (head position / world grip) recently
                // changed — rotation-only head motion keeps the current state sticky.
                float fraction = BlockedFraction(seg, headPos, visibleCount, out int blockedTotal);
                seg.LastRaw = fraction;
                seg.LastBlocked = blockedTotal;
                seg.LastVisible = visibleCount;
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

                // Critically-damped-style exponential fade toward the debounced state.
                float target = seg.State ? 1f : 0f;
                seg.Fade += (target - seg.Fade) * fadeStep;
                if (Mathf.Abs(target - seg.Fade) < 0.005f)
                    seg.Fade = target;

                Apply(seg);
            }

            if (now >= _nextDiagTime)
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
                    + $"bounds / {_allSamples.Count} floor samples (tile plane +"
                    + $"{FloorSampleEpsilon:0.00} wu, y {_sampleYMin:F2}..{_sampleYMax:F2}) — "
                    + $"per-room FLOOR-coverage fade "
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
        /// Cull the precomputed play-area samples against the head camera's view frustum
        /// (viewport test with margin, capped at <see cref="MaxVisibleSamples"/>). The
        /// surviving set represents the currently visible play-area region.
        /// </summary>
        private int CollectVisibleSamples(Camera head)
        {
            _visibleSamples.Clear();
            _visibleRoom.Clear();
            Array.Clear(_visPerRoom, 0, _visPerRoom.Length);
            for (int i = 0; i < _allSamples.Count; i++)
            {
                // Mono view/projection of the head camera; per-eye stereo frustums differ
                // only by half the IPD and a slightly wider horizontal FOV — FrustumMargin
                // (0.20 viewport-relative) generously covers that skew.
                Vector3 vp = head.WorldToViewportPoint(_allSamples[i]);
                if (vp.z > 0f
                    && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin
                    && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin)
                {
                    _visibleSamples.Add(_allSamples[i]);
                    int room = _sampleRoom[i];
                    _visibleRoom.Add(room);
                    if (room >= 0 && room < _visPerRoom.Length)
                        _visPerRoom[room]++;
                    if (_visibleSamples.Count >= MaxVisibleSamples)
                        break;
                }
            }
            return _visibleSamples.Count;
        }

        /// <summary>
        /// Largest per-room fraction of the frustum-visible FLOOR samples whose
        /// head→sample segment this wall's AABB blocks (R1: "the wall hides X% of the
        /// floor currently in view of some room").
        ///
        /// ROUND-4 KILL-FACTOR AUTOPSY (why the previous metric almost never fired on
        /// hardware) with a worked example at world scale 20 (1 real m = 20 wu, 1 wu = 5
        /// real cm, hex tile 1.72 wu; plausible wall band: y from −0.3 (skirt) to +2.4
        /// (top), room floor top y = 0, room 5 hexes ≈ 8.6 wu deep):
        /// <list type="bullet">
        /// <item>HEAD HIGH ABOVE: standing over the diorama the head sits ~0.6 real m =
        ///   12 wu above the floor. A ray to a sample at height s clears a wall of top w at
        ///   horizontal distance x0 unless x0/x ≥ (h−s at scale)… concretely: blocked only
        ///   when sampleDist ≤ wallDist · (h−s)/(h−w). h=12, w=2.4, s=0.4 → shadow reaches
        ///   just 21% past the wall. Only the FIRST grid row behind a wall can ever be
        ///   blocked from a god view — which is geometrically CORRECT (you do see most of
        ///   the room) but means those first-row samples are the entire signal.</item>
        /// <item>THE 88% RULE ATE THE SIGNAL: that first row sits ~1.5–1.9 wu behind the
        ///   wall face at a 10–20 wu sample distance, so the AABB entry lands at 90–99% of
        ///   the distance — and round 4 required &lt;88%. The only samples that COULD be
        ///   blocked were exactly the ones discarded → fraction ≈ 0 nearly always. Fix: a
        ///   wall-thickness epsilon in wu (entry &lt; dist − eps, eps = 0.5·thickness
        ///   clamped 0.10–0.90), plus "sample inside the wall AABB" counts.</item>
        /// <item>DILUTION: leaning to 8 real cm above the wall top (head y=4.0, 1.5 wu
        ///   outside the wall face) the shadow covers the first row of the near room at the
        ///   low layer (y=0.375: blocked out to 1.5·(4−0.375)/(4−2.4) ≈ 3.4 wu from the
        ///   head ≈ 1.9 wu past the wall). That is 3 of the room's ~18 visible samples —
        ///   but round 4 divided by ALL visible samples of ALL rooms (e.g. 24) → 0.13 &lt;
        ///   0.25 → never ON. Fix: normalize per room (denominator floored at
        ///   <see cref="RoomDenomFloor"/>) and take the max: 3/max(18,3)… with the epsilon
        ///   fix typically 5–6 blocked of 18 → 0.31 ≥ 0.25 → ON.</item>
        /// <item>MIN-VISIBLE: leaning in close often leaves 1–2 samples in the frustum;
        ///   round 4 forced fraction = 0 below 3 visible — OFF exactly when the wall filled
        ///   the view. Now ≥1 decides (EMA + Schmitt absorb the noise).</item>
        /// </list>
        /// Head inside the AABB = 1 (wall in the face).
        /// </summary>
        private float BlockedFraction(Segment seg, Vector3 headPos, int visibleCount, out int blockedTotal)
        {
            blockedTotal = 0;
            Bounds b = seg.Bounds;
            if (b.Contains(headPos))
            {
                blockedTotal = visibleCount;
                return 1f;
            }
            if (visibleCount < MinVisibleForDecision)
                return 0f;

            Array.Clear(_blkPerRoom, 0, _blkPerRoom.Length);
            float eps = seg.BlockEps;
            for (int i = 0; i < visibleCount; i++)
            {
                Vector3 sample = _visibleSamples[i];
                Vector3 to = sample - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                var ray = new Ray(headPos, to / dist);
                if (b.IntersectRay(ray, out float d)
                    && (d < dist - eps || b.Contains(sample)))
                {
                    blockedTotal++;
                    int room = _visibleRoom[i];
                    if (room >= 0 && room < _blkPerRoom.Length)
                        _blkPerRoom[room]++;
                }
            }
            if (blockedTotal == 0)
                return 0f;

            // Per-room normalization: the wall that covers the room being looked INTO must
            // not be diluted by visible samples of other rooms. Denominator floor keeps a
            // single stray visible sample from reading as full coverage.
            float best = 0f;
            int rooms = Mathf.Min(_roomBounds.Count, _visPerRoom.Length);
            for (int r = 0; r < rooms; r++)
            {
                if (_visPerRoom[r] <= 0 || _blkPerRoom[r] <= 0)
                    continue;
                float f = _blkPerRoom[r] / (float)Mathf.Max(_visPerRoom[r], RoomDenomFloor);
                if (f > best)
                    best = f;
            }
            return best;
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
                VRLog.Info(Name,
                    $"fade ON '{wall}' shader '{seg.ShaderNames}' [{variant}] — held state: " +
                    (seg.VariantHigh
                        ? "static TOTAL discard (HIGH fuses foundation band + screen vignette " +
                          "into one pre-cutoff scalar; band not separable — see header math)"
                        : "static discard above object-Y 0.4 (hard foundation band; constant " +
                          "map term, zero view-dependent inputs)"));
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
                   .Append("' raw").Append(seg.LastRaw.ToString("F2"))
                   .Append(" ema").Append(seg.Smooth.ToString("F2"))
                   .Append(" blk").Append(seg.LastBlocked).Append('/').Append(seg.LastVisible)
                   .Append(seg.State ? " ON " : " off ").Append(seg.Fade.ToString("F2"))
                   .Append(" wy[").Append(b.min.y.ToString("F2")).Append("..")
                   .Append(b.max.y.ToString("F2")).Append(']')
                   .Append(" e").Append(seg.BlockEps.ToString("F2"))
                   .Append(seg.VariantHigh ? (seg.VariantLow ? " vH+L" : " vHIGH") : " vLOW");
            if (_sampleYMin > b.max.y)
                _diagSb.Append(" !ABOVE-WALL");
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
                // Held fully faded (R2): constant r=1,a=1 map + _Cutoff=1.1. a=1 forces the
                // per-pixel depth compare TRUE under any Z convention → map term m = 1
                // CONSTANT; LOW clips everything above its hard object-Y 0.4 foundation
                // band, HIGH gets A=B=1 (vignette and noise multiplied by zero) → constant
                // total discard. Zero view-dependent inputs on either variant — full math
                // in the class header.
                _mpb.SetTexture(TilesOcclusionMapId, _fullTex!);
                _mpb.SetFloat(CutoffId, 1.1f);
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
            _roomBounds.Clear();
            foreach (MeshRenderer r in gen.m_RoomRenderers)
            {
                if (r != null)
                    _roomBounds.Add(r.bounds);
            }
            _builtRoomCount = gen.m_RoomRenderers.Count;

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
            if (_visPerRoom.Length < _roomBounds.Count)
            {
                _visPerRoom = new int[_roomBounds.Count + 4];
                _blkPerRoom = new int[_roomBounds.Count + 4];
            }
        }

        /// <summary>
        /// Precompute the FLOOR occlusion samples (R1): a per-room XZ grid placed ON the
        /// room tile bounds' TOP surface (floor height + <see cref="FloorSampleEpsilon"/>),
        /// as dense as the room budget allows under <see cref="MaxTotalSamples"/>
        /// (4×4 → 3×3 → 2×2 → center per room). The room renderers are the hex-tile meshes
        /// themselves, so bounds.max.y IS the tile plane — no band/height inference. Each
        /// sample remembers its room for per-room normalization.
        /// </summary>
        private void RebuildSamples()
        {
            _allSamples.Clear();
            _sampleRoom.Clear();
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
                if (_allSamples.Count >= MaxTotalSamples)
                    break;
                Bounds b = _roomBounds[r];
                float y = b.max.y + FloorSampleEpsilon;
                if (y < _sampleYMin) _sampleYMin = y;
                if (y > _sampleYMax) _sampleYMax = y;
                for (int ix = 0; ix < grid; ix++)
                {
                    float x = Mathf.Lerp(b.min.x, b.max.x, (ix + 0.5f) / grid);
                    for (int iz = 0; iz < grid; iz++)
                    {
                        float z = Mathf.Lerp(b.min.z, b.max.z, (iz + 0.5f) / grid);
                        _allSamples.Add(new Vector3(x, y, z));
                        _sampleRoom.Add(r);
                    }
                }
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
        /// behind every pixel" under the shader's reversed-Z compare, m = 1-noise. Full
        /// (held) texture: r=1 AND a=1 — alpha 1 forces the depth compare TRUE under any Z
        /// convention, making the map term m = 1 a CONSTANT (R2: the held-state discard
        /// carries zero per-pixel view dependence; see class header).
        /// </summary>
        private bool EnsureTextures()
        {
            if (_noiseTex != null && _fullTex != null)
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

            _fullTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "GloomhavenVR.WallFadeFull",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var full = new Color32[4];
            for (int i = 0; i < 4; i++)
                full[i] = new Color32(255, 0, 0, 255); // r=1 (unused once a=1), a=1 → m ≡ 1
            _fullTex.SetPixels32(full);
            _fullTex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
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
            _allSamples.Clear();
            _sampleRoom.Clear();
            _visibleSamples.Clear();
            _visibleRoom.Clear();
            if (_noiseTex != null)
            {
                try { Destroy(_noiseTex); } catch { /* already gone */ }
                _noiseTex = null;
            }
            if (_fullTex != null)
            {
                try { Destroy(_fullTex); } catch { /* already gone */ }
                _fullTex = null;
            }
        }
    }
}
