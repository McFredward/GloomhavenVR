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
    /// <summary>Fort/keep superstructures: adopt plain meshes stacked on a tracked wall into that
    /// wall's fade (occlusion AABB + dissolve ride-along — see WallSegmentFade.Stacked.cs).</summary>
    internal static ConfigEntry<bool>? StackedShellFade;
    /// <summary>MP: also fade the walls a TEAMMATE's wall fade currently hides (wire record 17;
    /// receiver-side gate — own fades are always broadcast, see WallSegmentFade.Net.cs).</summary>
    internal static ConfigEntry<bool>? SyncPeerFades;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("wallfade");
        OnFraction = config.Bind("WallFade", "OnFraction", Defaults.OnFraction,
            "Fade a wall when it hides at least this (EMA-smoothed) fraction of the frustum-visible " +
            "FLOOR (hex-tile plane) samples of some room — 0.25 = wall hides 25% of the floor you " +
            "are looking at (Schmitt trigger high bar). Live; clamped 0.05-0.95.");
        OffFraction = config.Bind("WallFade", "OffFraction", Defaults.OffFraction,
            "Once faded, keep the wall faded while the smoothed floor-coverage fraction stays at or " +
            "above this (Schmitt trigger low bar). Live; clamped 0.01-0.95 and never above OnFraction.");
        ExitDwellMoved = config.Bind("WallFade", "ExitDwellMovedSeconds", Defaults.ExitDwellMovedSeconds,
            "Seconds the fraction must stay below OffFraction before the wall un-fades when the " +
            "PERSPECTIVE recently changed (real head translation / world-grab / recenter). Live.");
        ExitDwellStationary = config.Bind("WallFade", "ExitDwellStationarySeconds", Defaults.ExitDwellStationarySeconds,
            "Un-fade dwell while the head has only ROTATED recently — rotation alone should almost " +
            "never bring a wall back. Live; never below ExitDwellMovedSeconds.");
        StackedShellFade = config.Bind("WallFade", "StackedShellFade", Defaults.StackedShellFade,
            "Fade fort/keep superstructures with their wall: meshes WITHOUT a fade shader that sit " +
            "stacked directly on a tracked wall run (battlements, upper stories) join that wall's " +
            "occlusion box and dissolve/reappear with its fade — without this, a multi-story keep " +
            "stays fully solid because only its bottom course is real wall geometry. OFF = vanilla " +
            "look for such shells. Live (applies at the next 2s rescan).");
        SyncPeerFades = config.Bind("WallFade", "SyncPeerFades", Defaults.SyncPeerFades,
            "Multiplayer: walls that fade for a TEAMMATE also fade for you (and reappear when " +
            "they do for them) — same animation as your own wall fades. Receiver-side setting: " +
            "your own fades are always broadcast (bytes are cheap), each player's toggle decides " +
            "only what THEY see, so toggling mid-session needs no renegotiation. Live.");
    }

    // Clamped live accessors — safe before Bind() (fall back to the shipped defaults).
    internal static float On => Clamped(OnFraction, 0.25f, 0.05f, 0.95f);
    internal static float Off => Mathf.Min(Clamped(OffFraction, 0.10f, 0.01f, 0.95f), On);
    internal static float DwellMoved => Clamped(ExitDwellMoved, 2.5f, 0.1f, 60f);
    internal static float DwellStationary =>
        Mathf.Max(Clamped(ExitDwellStationary, 7f, 0.1f, 120f), DwellMoved);
    internal static bool StackedShells => StackedShellFade == null || StackedShellFade.Value;
    internal static bool SyncPeer => SyncPeerFades == null || SyncPeerFades.Value;

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
/// LOGICAL ROOM GROUPING (keep round 4 — user ruling: "Ich will weiterhin die normale
/// Raum-Logik", which retired round 3's distance-based cross-room MAX after one ModBuild):
/// the game's unit of reveal is the <c>CMap</c> (ScenarioRuleLibrary) reached via
/// <c>TilesOcclusionVolume.CentralTile.m_ClientTile.m_Tile.m_HexMap</c> — the exact object
/// whose <c>Revealed</c> flag the volume's own <c>IsVisible()</c> reads. One revealed room
/// may ship SEVERAL occlusion volumes ('Volume_1..6' of the keep are sub-volumes of ONE
/// CMap), and the registry used to treat every volume renderer as its own room — so the
/// wall in front of the player hid "another room's" samples that were in truth the SAME
/// room, and its own-room fraction read 0.00 forever. The registry now merges volume
/// renderers per (CMap, quantized floor height) into one LOGICAL room — union XZ footprint,
/// one grid, one fail-safe/anchor state — and the metric stays strict own-room accounting
/// against that merged grid. Single-volume rooms group to themselves (identical math);
/// terraced same-CMap volumes at different heights stay separate so each keeps its true
/// sample plane.
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
/// DOORWAY RULING (user, 2026-08-02 — final, supersedes every previous doorway ruling):
/// archway/doorway segments NEVER fade — no open/closed differentiation, no hard hide.
/// They are always solid. Fade renderers hugging a door prop are still RECOGNIZED per
/// door (spatial link to the UnityGameEditorDoorProp roots,
/// <see cref="FadeDriver.FindDoorwayRoot"/>) so an archway can never merge into a
/// fadeable wall segment — the per-door segment is held permanently SOLID and never
/// enters the fade decision. The three rounds of open-doorway fade/hide experiments
/// (and how to revive them) are parked in <c>.planning/doorway-fade-experiments.md</c>.
///
/// MULTIPLAYER: purely local rendering (MaterialPropertyBlocks + locally created textures);
/// nothing synced, peers unaffected.
/// Gated LIVE by [Compat] WallFade — OFF clears every block
/// immediately (exactly today's solid walls, zero per-frame cost beyond the enabled check).
/// </summary>
internal static partial class WallSegmentFade
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

    /// <summary>
    /// THE game's own marker for "this mesh fades when it hides the play area": its material
    /// runs one of the WallFade shader family (Amp_Basic_WallFade, Amp_Low/Amp_Basic_WallFade_Low,
    /// and any themed sibling — the flat game's fade is purely shader-driven, it keeps no object
    /// list). Single source of truth for "is this renderer fade-capable".
    /// </summary>
    internal static bool IsWallFadeShaderName(string shaderName) => shaderName.Contains("WallFade");

    /// <summary>
    /// Foliage family (Amp_Basic_Foliage, Amp_Basic_Foliage_Prop_Shader, …): the grasses, vines
    /// and bushes DRESSING a wall. They carry no WallFade path, so when the wall dissolves they
    /// used to stay behind as a view-blocking "Gestrüpp-Wand" (user report + gebüsch.png). They
    /// are collected as ATTACHMENTS of their wall's segment and hidden with it.
    /// </summary>
    internal static bool IsFoliageShaderName(string shaderName) => shaderName.Contains("Foliage");

    /// <summary>Per-wall-segment fade state.</summary>
    private sealed partial class Segment
    {
        /// <summary>Tracking anchor and dictionary key: the <see cref="ProceduralWall"/> for
        /// cache-listed walls; for shader-ADOPTED groups (advanced tilesets put fade-capable wall
        /// meshes on map tiles instead of ProceduralWall entities) the nearest
        /// <see cref="ProceduralTileObserver"/> ancestor, else the renderer's parent transform.</summary>
        public Component? Anchor;
        /// <summary>True = from ProceduralWall.m_WallCache (renderers re-collected from the wall's
        /// own subtree); false = adopted by shader match (renderers re-collected by the rescan sweep).</summary>
        public bool FromWallCache;
        public readonly List<MeshRenderer> Renderers = new();
        /// <summary>Refresh scratch: the renderer list BEFORE the current refresh, so a renderer
        /// that left a still-faded segment gets its property block cleared instead of keeping a
        /// stale fade forever.</summary>
        public readonly List<MeshRenderer> PrevRenderers = new();
        /// <summary>Foliage ATTACHMENTS (grass/vines/bushes dressing this wall — Foliage-family
        /// shaders, no fade path of their own): dissolved via an alpha-cutoff ramp while the wall
        /// fades and fully hidden in the held state, restored exactly when the wall returns.</summary>
        public readonly List<MeshRenderer> Foliage = new();
        public readonly List<MeshRenderer> PrevFoliage = new();
        /// <summary>0 = restored/untouched, 1 = dissolving (cutoff MPB set), 2 = hidden.</summary>
        public int FoliageState;
        /// <summary>ASSET-COMPLETE fade siblings (the Torbogen ruling, adopted groups only): a
        /// mixed doorway/arch asset carries the WallFade shader on its FRAME/PILLAR meshes but
        /// not on the wooden door wings / arch trim — fading only the shader-matched renderers
        /// left a floating door remnant (torbogen.png). These are the non-fade renderers under
        /// the same prefab-ish asset root (see <see cref="FadeDriver.FindAssetRoot"/>): hidden
        /// via renderer.enabled once the segment reaches the held state, restored exactly on
        /// unfade — the foliage restore discipline, no material mutation.</summary>
        public readonly List<MeshRenderer> Siblings = new();
        public readonly List<MeshRenderer> PrevSiblings = new();
        /// <summary>0 = restored/untouched, 2 = hidden (siblings have no dissolve ramp — they
        /// run arbitrary opaque shaders, so they pop with the END of the wall's dissolve).</summary>
        public int SiblingState;
        /// <summary>DOORWAY marker (user ruling 2026-08-02: doorways NEVER fade): non-null
        /// keys this segment to its door prop root ('ThinDoor : (guid)', the
        /// UnityGameEditorDoorProp object) — every fade renderer hugging that door lands in
        /// this ONE per-door segment, which the decision loop holds permanently SOLID. The
        /// keying exists purely so archway frames can never merge into a fadeable wall
        /// segment; parked experiments in .planning/doorway-fade-experiments.md.</summary>
        public Transform? DoorRoot;
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
        /// <summary>ROOM SEAM (user report 2026-08-09 — the wall piece that faded "falsch rum"):
        /// the OTHER decision-valid rooms whose bounds this wall also borders, i.e. every room
        /// besides <see cref="RoomIndex"/> within <see cref="FadeDriver.RoomBorderBandWU"/> of
        /// it. Empty for the overwhelming majority of walls; non-empty only for a wall standing
        /// in the seam BETWEEN two rooms, where "which room is mine" is a coin flip and getting
        /// it wrong inverts the fade. See <see cref="FadeDriver.BlockedFraction"/>.</summary>
        public readonly List<int> BorderRooms = new();
        /// <summary>Which room actually decided the last coverage reading (diag) — differs from
        /// <see cref="RoomIndex"/> only for seam walls.</summary>
        public int LastDecidingRoom = -1;
        /// <summary>R2 diag: which fade-shader variant(s) this segment's renderers carry.</summary>
        public bool VariantHigh;
        public bool VariantLow;
        /// <summary>Distinct fade-shader name(s) seen on the renderers ("+"-joined).</summary>
        public string ShaderNames = "?";
        /// <summary>How many renderers joined via the round-8 TOGGLE-NATIVE path (materials
        /// with _Cutoff + _WallFade_On/_ToggleWallfade — the masonry) — diag/fade-ON label.</summary>
        public int ToggleNative;
        /// <summary>Authored "Mask Clip Value" (<c>_Cutoff</c>) of the wall's fade material,
        /// clamped to (0,1) — the held state drives exactly this value like the flat game
        /// (which never writes _Cutoff at all). 0.5 fallback when unreadable.</summary>
        public float HeldCutoff = 0.5f;
        /// <summary>Whether <see cref="HeldCutoff"/> came from the material (diag).</summary>
        public bool CutoffAuthored;
        /// <summary>True when this segment's AABB engulfs its own room's floor samples AND it
        /// cannot be split further (single renderer): the coverage metric is meaningless for it,
        /// so it is held permanently SOLID (vanilla look). Re-derived every rescan.</summary>
        public bool Engulfing;
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

    private sealed partial class FadeDriver : MonoBehaviour
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
        /// <summary>Round-8 (BODY SHADER PROPERTIES dump, ModBuild 64): the keep masonry's
        /// <c>Amp_Basic_N_MRAO</c> exposes <c>_Cutoff</c> + <c>_WallFade_On</c> — the Amp
        /// family compiles the wall-fade subgraph into "plain" shaders behind this material
        /// toggle. Our MPB opens it per renderer alongside the standard fade set.</summary>
        private static readonly int WallFadeOnMatId = Shader.PropertyToID("_WallFade_On");
        /// <summary>Round-9 game-wide audit: the third toggle spelling. The game's own
        /// <c>ToggleWallFadeScript</c> writes this int per renderer at Start — an authored
        /// per-asset OPT-OUT (0 = never fade). Honored: a material whose only gate is this
        /// and reads 0 is authored always-solid and left alone (the doorway-ruling spirit);
        /// it is never pinned to 1 in the MPB.</summary>
        private static readonly int ToggleWallFadeLocalMatId =
            Shader.PropertyToID("_ToggleWallFadeLocal");
        /// <summary>The compile-time keyword behind <c>_WallFade_On</c> (ModBuild-65
        /// adjudication: wall materials ship authored 1 + keyword <c>_WALLFADE_ON_ON</c> —
        /// fade branch present, MPB driveable; floor materials ship 0 + no keyword — branch
        /// absent, MPB inert).</summary>
        private const string WallFadeOnKeyword = "_WALLFADE_ON_ON";

        private readonly Dictionary<Component, Segment> _segments = new();
        private readonly List<Component> _deadKeys = new();
        /// <summary>Renderers owned by wall-cache segments this rescan — the adoption sweep must
        /// never create a second segment for them.</summary>
        private readonly HashSet<MeshRenderer> _claimedRenderers = new();
        /// <summary>Per-shader "is wall-fade-capable" verdict cache (the rescan sweep tests every
        /// scene renderer; a scene has ~26 distinct materials over a handful of shaders).</summary>
        private readonly Dictionary<Shader, bool> _shaderVerdict = new();
        /// <summary>Same cache for the foliage-family verdict.</summary>
        private readonly Dictionary<Shader, bool> _shaderFoliageVerdict = new();
        /// <summary>Shared MPB for the foliage cutoff ramp (rewritten per renderer per frame
        /// while a segment is mid-dissolve; segments in a held state use none).</summary>
        private MaterialPropertyBlock? _foliageMpb;
        /// <summary>Cutoff the dissolve ramps TOWARD: safely above every texel's alpha, so a
        /// fully-faded cutout leaf discards completely. The ramp START approximates the common
        /// authored "Mask Clip Value" (~0.35) — close enough for a 0.35s transition.</summary>
        private const float FoliageCutoffStart = 0.35f;
        private const float FoliageCutoffEnd = 1.2f;
        /// <summary>Above this fade the foliage renderer is DISABLED outright — the cutoff ramp
        /// only removes cutout texels, and any opaque twig material would otherwise survive.</summary>
        private const float FoliageHideFade = 0.99f;
        // Rescan census (heartbeat diagnostics): how many fade-capable renderers exist, how many
        // the wall cache claimed, how many the shader sweep adopted — and the shader names seen on
        // cache walls that carry NO fade-capable renderer at all (the tripwire for a tileset whose
        // wall shaders are named outside the WallFade family).
        private int _censusFadeRenderers;
        private int _censusClaimed;
        private int _censusAdopted;
        private int _censusWallsWithoutFade;
        private readonly HashSet<string> _unfadeableWallShaders = new();
        private int _heartbeatSegCount = -1;
        /// <summary>Fade-capable renderer census as last heartbeat-logged — a change re-arms
        /// the heartbeat (round 5: the one stale pre-generation heartbeat hid the TRIPWIRE
        /// for five hardware rounds).</summary>
        private int _heartbeatFadeRenderers = -1;

        /// <summary>Adopted-group anchors whose combined AABB was too FAT to act as a wall slab
        /// (both horizontal extents large — e.g. a tile whose wall pieces ring the room; the AABB
        /// would contain the room's own floor samples and read as 100% coverage forever). Their
        /// renderers are tracked as per-renderer segments instead; membership persists across
        /// rescans so those segments keep their smoothing state. Cleared on scene load.</summary>
        private readonly HashSet<Component> _splitAnchors = new();
        private readonly List<KeyValuePair<Component, Segment>> _fatScratch = new();
        /// <summary>An adopted GROUP whose horizontal AABB is thicker than this (wu; a wall run's
        /// thin extent is ≤ ~2 wu, a hex tile ≈ 1.72 wu, a room ≥ ~8 wu) is split per renderer.</summary>
        private const float GroupSlabMaxHorizontal = 3.5f;

        // ---- asset-complete fade (Torbogen ruling) ----------------------------------------
        /// <summary>Rescan-scope dedupe: every renderer attached as an asset sibling this rescan
        /// — exactly ONE segment may own (and restore) a sibling, or two owners would fight over
        /// renderer.enabled every frame.</summary>
        private readonly HashSet<MeshRenderer> _siblingOwned = new();
        private readonly List<MeshRenderer> _subtreeScratch = new();
        /// <summary>An ancestor whose subtree holds more MeshRenderers than this is a CONTAINER
        /// (the 'L :' Apparance layer/section-root class), not a prefab-sized asset — the walk
        /// stops there and attaches nothing (fail-open: the ugly remnant stays visible, which
        /// beats hiding unrelated geometry). A doorway asset is ~2 frames + 4 pillars + 2 wings
        /// + trim + lock ≈ ≤ 12 renderers.</summary>
        private const int MaxAssetRootRenderers = 16;
        /// <summary>Ancestor-walk depth cap — prefab roots sit 1-3 levels above their meshes;
        /// anything deeper is scene structure.</summary>
        private const int MaxAssetRootDepth = 4;
        // ---- doorway recognition (user ruling 2026-08-02: doorways NEVER fade) ------------
        /// <summary>Door prop roots rebuilt each rescan: every live
        /// <c>UnityGameEditorDoorProp</c> transform (the 'ThinDoor : (guid)' objects the
        /// Choreographer opens via <c>ObjectCacheService.GetPropObject</c>). Fade renderers
        /// adjacent to one of these are grouped into a per-DOOR segment (see
        /// <see cref="FindDoorwayRoot"/>) that is held permanently SOLID — an archway must
        /// never merge into a fadeable wall segment. Entrance/exit doors lose their prop
        /// component at spawn (ApparanceLayer.Create destroys procDoor) — they are not
        /// listed and their frames keep the generic adopted behaviour (accepted).</summary>
        private readonly List<Transform> _doorRoots = new();
        /// <summary>Max XZ gap (wu) between a fade renderer's AABB and a door prop position for
        /// the renderer to count as that doorway's frame — mirrors the game's own wall-search
        /// radius around a door (ProceduralTile.FindMapTileByPosition: FindWallsNear(pos, 2.2f)).
        /// Frames/pillars hug the door; the next parallel wall run is ≥ a hex (~1.72 wu) of
        /// clear floor away, so 2.2 cannot swallow a neighbouring wall.</summary>
        private const float DoorwayLinkMaxXZ = 2.2f;

        /// <summary>Room-registry census as last logged (reveal re-anchor diagnostic).</summary>
        private int _lastRoomCensusCount = -1;
        private int _lastRoomCensusAnchored = -1;
        private readonly List<Bounds> _roomBounds = new();
        private readonly List<float> _roomFloorY = new();       // tile-anchored floor plane per room
        private readonly List<bool> _roomFloorAnchored = new(); // true = from a CentralTile anchor
        // LOGICAL ROOM GROUPING (round 4): per-renderer game-room identity (the CMap behind
        // the volume's CentralTile — the object whose .Revealed the game itself reveals),
        // its display label, and the merged-room tables the registry builds from them.
        private readonly Dictionary<MeshRenderer, object> _roomMapByRenderer = new();
        private readonly Dictionary<MeshRenderer, string> _roomMapLabelByRenderer = new();
        private readonly Dictionary<(object, int), int> _keyToRoomScratch = new();
        private readonly List<string> _roomLabels = new();      // per logical room (diag/census)
        private readonly List<int> _roomRendererCounts = new(); // volume renderers merged per room
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
        /// <summary>Rescan scratch for the two registry reads that replaced the rescan's
        /// <c>FindObjectsOfType&lt;TilesOcclusionVolume&gt;</c> and
        /// <c>FindObjectsOfType&lt;UnityGameEditorDoorProp&gt;</c> walks (PERF S1 — see
        /// <see cref="SceneRegistry"/>). Reused, so the swap allocates nothing per rescan
        /// where the sweeps allocated a fresh array each time.</summary>
        private readonly List<TilesOcclusionVolume> _volumeScratch = new();
        private readonly List<UnityGameEditorDoorProp> _doorPropScratch = new();

        // ==================================================================================
        // PERF S2 (2026-08-23) — THE 118 ms RESCAN.
        //
        // USER REPORT, verbatim: "Ich hab nun mal eine Map aufgemacht mit vielen Details und
        // hab dort zum Testen alle Räume aufgemacht. Ich merke deutliche Laggs wenn ich alle
        // Räume von oben anschaue. In VR ist dieses überblickende 'von oben schauen' sehr
        // wichtig, dass es möglich ist. Doch es waren schon sehr starke Laggs, dass ein
        // flüssiges Spielen nicht möglich ist."
        //
        // THE MEASUREMENT (ModBuild 226 hardware log, .planning/debug/Player.log). Every
        // [Perf] STEPS window reported
        //     WallFade.Rescan 115.6–118.2 ms avg, worst 141.18 ms, ~59 ms/s, frames 12–15
        // and every [Perf] SPLIT window in the same session reported
        //     STALLS: 13–16 logic frame(s) over 100 ms in this window.
        // Fifteen rescans, fifteen stalls, one per two seconds. At 90 Hz each one drops about
        // ten frames. The stall was ENTIRELY ours; the SPLIT line's stock explanation ("a
        // synchronous scene load, an asset-bundle decompress or a room regeneration, NOT
        // steady-state cost") is wrong for this session and misled nobody only because the
        // STEPS line named the step outright.
        //
        // WHERE THE 118 ms WENT. The rescan took ONE full-scene
        // FindObjectsOfType<Renderer>() (O(every loaded object), not O(matches)) and then
        // walked the resulting 8630-renderer array FOUR MORE TIMES — once for
        // AdoptShaderMatchedWalls, once for CollectWaterFeatures, once for
        // CollectStackCandidates, once for CollectWallMountedProps. Each of those walks
        // re-derived, per renderer, facts that are properties of the renderer and not of the
        // caller:
        //   * GetSharedMaterials + shader-name tests — THREE to FOUR times per renderer per
        //     rescan (wall-fade family, foliage family, water family);
        //   * r.name — an interop STRING ALLOCATION, three times per renderer per rescan
        //     (IsModObject in three of the four passes, plus the water name-token family);
        //   * m.shader.name — another interop string allocation per material, taken by the
        //     water test for EVERY scene renderer;
        //   * r.bounds — a native call per renderer per pass.
        // Twenty-six thousand interop string allocations and ~35 000 native material fetches
        // per rescan, for a scene whose fade-capable renderer count is 848.
        //
        // THE SHAPE OF THE FIX. Three levers, in order of what they bought:
        //  1. ONE CENSUS. The scene array is classified exactly ONCE per rescan cycle into the
        //     RendererFact table below, and all four passes read that table. Nothing about
        //     WHICH renderers a pass sees changes — the table stores the same predicates the
        //     passes used to compute inline, evaluated with the same code (IsModObject,
        //     RendererUsesWallFade, RendererUsesFoliage, the water test,
        //     IsMountableRendererType), from the same array, in the same order.
        //  2. A PER-FRAME BUDGET. The census is a PURE READ — it touches no segment, no
        //     renderer, no material — so it can be spread across frames with a resumable
        //     cursor without ever exposing a half-built segment table (the precedent in this
        //     codebase is the ≤400 transforms/frame sweep). Only the COMMIT stage mutates,
        //     and it runs whole, in one frame, exactly as the old rescan did.
        //  3. AN EVENT-DRIVEN SWEEP. FindObjectsOfType itself cannot be sliced, so it is taken
        //     only when the scene's STRUCTURAL SIGNATURE moved (room reveal, a new wall in the
        //     wall cache, a new map tile / door prop / occlusion volume) or when the snapshot
        //     aged past SnapshotMaxAgeSeconds. Between those, the same array is re-classified
        //     — which re-reads every renderer's live bounds and enabled flag, so nothing that
        //     MOVED is stale; the only thing a reused snapshot cannot see is a renderer that
        //     was CREATED since it was taken. See SnapshotMaxAgeSeconds for the bound on that
        //     and for why FastReclaimRegeneratedShell already covers the case that matters.
        // ==================================================================================

        /// <summary>
        /// One scene renderer's rescan-relevant classification, computed ONCE per cycle by
        /// <see cref="ClassifySlice"/> and read by all four collection passes.
        ///
        /// <para>WHY A STRUCT ARRAY. The table is walked three more times after it is built
        /// (stack candidates, mounted dressing, and the wall adoption index), and those walks
        /// must be pure managed float compares — the whole point is that the native calls
        /// happen once. A struct array keeps the walk cache-linear and allocation-free; the
        /// table is grown, never reallocated per cycle.</para>
        ///
        /// <para>WHAT IS AUTHORITATIVE AND WHAT IS A PREFILTER. <see cref="Bounds"/> is a
        /// SNAPSHOT taken during the census slices, i.e. up to a handful of frames before the
        /// commit. It is used ONLY to reject candidates cheaply (the ground band and the
        /// union-reach rects) — every bound that ends up inside a segment's decision AABB is
        /// re-read LIVE from the renderer in the commit stage, exactly as before. Scenery does
        /// not move between two frames; figures do, and figures are excluded on their own
        /// account by <see cref="IsFigureOrActorRenderer"/> long before geometry is consulted.
        /// </para>
        /// </summary>
        private struct RendererFact
        {
            public Renderer? R;
            /// <summary>Non-null iff <see cref="R"/> is a MeshRenderer (the type test the
            /// adoption and stack passes ran inline).</summary>
            public MeshRenderer? Mesh;
            public Bounds Bounds;
            /// <summary>The ANCHOR POINT the mounted/stacked prefilters test — exactly the
            /// quantity those passes compute inline: <c>transform.position</c> for a
            /// ParticleSystemRenderer (round-13: a particle system is anchored by its EMITTER,
            /// never by its live plume bounds, which drift every frame) and
            /// <c>(bounds.center.x, bounds.min.y, bounds.center.z)</c> for anything else, so
            /// <c>Anchor.y</c> IS the <c>anchorY</c> both passes use.</summary>
            public Vector3 Anchor;
            /// <summary>Fixed for a renderer's lifetime, so a census verdict on it can never
            /// go stale: the mod LAYER and the 'GloomhavenVR.' name prefix are both stamped at
            /// creation. (<c>enabled</c> is deliberately NOT cached — the game flips it at
            /// will, and a stale <c>enabled</c> used as a reject would NARROW a candidate set.
            /// Every pass that cares reads it live.)</summary>
            public bool Mod;
            public bool WallFadeShader;
            public bool FoliageShader;
            public bool WaterSurface;
            public bool Particles;
            public bool Mountable;
        }

        /// <summary>The scene snapshot this cycle is classifying (the array
        /// <c>FindObjectsOfType&lt;Renderer&gt;</c> returned). Reused across cycles when the
        /// structural signature has not moved — see the PERF S2 note above.</summary>
        private Renderer[] _snapshot = System.Array.Empty<Renderer>();

        /// <summary>Classification of <see cref="_snapshot"/>, index for index. Grown to fit,
        /// never shrunk — a rescan must not allocate.</summary>
        private RendererFact[] _facts = System.Array.Empty<RendererFact>();

        /// <summary>How many entries of <see cref="_facts"/> are live this cycle.</summary>
        private int _factCount;

        /// <summary>Indices into <see cref="_facts"/> of the MeshRenderers carrying a wall-fade
        /// shader — the adoption pass's whole input, in snapshot order (the order decides which
        /// renderer seeds a group's anchor, so it is preserved exactly).</summary>
        private readonly List<int> _factWallFade = new();

        /// <summary>Indices into <see cref="_facts"/> of the water surfaces — the water pass's
        /// whole input, in snapshot order.</summary>
        private readonly List<int> _factWater = new();

        /// <summary>Water-shader verdict per Shader, the same per-Shader memo
        /// <see cref="_shaderVerdict"/> and <see cref="_shaderFoliageVerdict"/> use. Before
        /// this existed the water test read <c>m.shader.name</c> — an interop string
        /// allocation — for every material of every scene renderer, every rescan. See
        /// <see cref="IsWaterShader"/>.</summary>
        private readonly Dictionary<Shader, bool> _shaderWaterVerdict = new();

        private enum RescanStage
        {
            /// <summary>No cycle in flight; the segment table is the last committed one.</summary>
            Idle,
            /// <summary>Walking <see cref="_snapshot"/> with <see cref="_classifyCursor"/>,
            /// filling <see cref="_facts"/>. Pure reads — nothing is mutated.</summary>
            Classify,
            /// <summary>Census complete; the next frame runs the (now cheap) mutation passes
            /// whole, in one frame.</summary>
            Commit,
        }

        private RescanStage _rescanStage = RescanStage.Idle;
        private int _classifyCursor;
        /// <summary>COLD = this cycle took a fresh sweep, so every fact must be derived from
        /// scratch (materials, names, types). WARM = the snapshot was reused, so only the
        /// values that can change without the renderer being recreated are refreshed: liveness,
        /// <c>enabled</c> and bounds. A renderer's shader family and name do not change over
        /// its lifetime; its transform does, every frame.</summary>
        private bool _classifyCold;

        /// <summary>
        /// Set whenever the wall system REASSIGNS a renderer's <c>sharedMaterials</c> — the
        /// dissolve swap (<c>WallSegmentFade.Dissolve.cs</c>) puts body meshes onto copies of
        /// the masonry fade shader and puts the authored array back on unfade. That is the one
        /// way a renderer's SHADER FAMILY can change without the renderer being recreated, so
        /// it is the one thing that can make a warm census's cached
        /// <c>WallFadeShader</c>/<c>FoliageShader</c>/<c>WaterSurface</c> verdicts wrong. The
        /// next cycle re-derives every fact from scratch — the budget absorbs it, and the
        /// alternative (a warm cycle carrying a stale shader verdict for up to
        /// <see cref="SnapshotMaxAgeSeconds"/>) would be a look change, which this round
        /// forbids. Static because the restore path is static; there is one driver.
        /// </summary>
        private static bool _censusMaterialsDirty;

        private bool _rescanUrgent;
        private float _snapshotTakenAt = float.NegativeInfinity;
        private int _snapshotSignature = -1;

        /// <summary>Millisecond budget the census may spend on ONE frame. 1.5 ms against an
        /// 11.11 ms budget leaves the frame intact; the cycle simply takes more frames. The
        /// old code spent 118 ms on one frame and dropped ten.</summary>
        private const float ClassifyBudgetMillis = 1.5f;

        /// <summary>Budget for a cycle triggered by a ROOM REVEAL rather than by the timer.
        /// A reveal invalidates the room registry outright and walls of an unanchored room are
        /// held fail-safe SOLID until it re-anchors, so a leisurely census there would show as
        /// "the walls stopped fading for a moment after the door opened". A reveal already
        /// coincides with the game's own room-generation hitch, so this is the one place where
        /// spending more is free.</summary>
        private const float ClassifyUrgentBudgetMillis = 6f;

        /// <summary>How many facts to classify between two clock reads. The Stopwatch read is
        /// itself ~20 ns, so checking it per renderer would be a measurable share of a walk
        /// whose per-item cost is what this whole change is about.</summary>
        private const int ClassifyChunk = 256;

        /// <summary>
        /// How stale a reused scene snapshot may get before a fresh
        /// <c>FindObjectsOfType&lt;Renderer&gt;</c> is taken regardless of the structural
        /// signature.
        ///
        /// <para>WHAT A REUSED SNAPSHOT CAN MISS, EXACTLY. Only a renderer CREATED since the
        /// sweep. Destroyed ones are caught by the per-fact null check; moved ones by the warm
        /// re-classification, which re-reads bounds every cycle. Every creation path this
        /// system cares about moves the structural signature and takes a fresh sweep on the
        /// spot: a room reveal (m_RoomRenderers), a new wall (ProceduralWall.m_WallCache), a
        /// new map tile / door prop / occlusion volume (the SceneRegistry counts), a scene load
        /// (OnSceneLoaded resets the cycle). The one path that does NOT is Apparance
        /// REGENERATING content under an existing tile — and that is precisely the case
        /// <see cref="FastReclaimRegeneratedShell"/> exists for, which sweeps on its own 0.25 s
        /// cadence while any stack-carrying wall is held faded. This value bounds everything
        /// else at six seconds, three rescans' worth, against the two seconds it used to be.
        /// </para>
        /// </summary>
        private const float SnapshotMaxAgeSeconds = 6f;

        /// <summary>
        /// Slack applied to every geometric test that reads a CENSUS bound rather than a live
        /// one, so a cached AABB can only ever REJECT what the live test would also reject.
        ///
        /// <para>WHY IT IS SAFE AT THIS SIZE. A fact's bounds are at most one census's worth of
        /// frames old — a handful of frames at 90 Hz, a fraction of a second at the 18 fps this
        /// scenario actually runs at. The renderers these prefilters gate are static scenery
        /// (masonry courses, sconces, banners, shell stories): they do not move at all. The
        /// things that DO move are figures and their accessories, and those are excluded by
        /// <see cref="IsFigureOrActorRenderer"/> on their own account, before any geometry is
        /// consulted. 1 wu is a whole ground-exclusion band — far more than anything static can
        /// travel in that window — and every candidate that survives a cached prefilter is
        /// re-tested against its LIVE bounds before it can be claimed.</para>
        /// </summary>
        private const float CensusBoundsSlackWU = 1.0f;

        /// <summary>Cycle accounting for the one attributable diagnostic line
        /// (<see cref="LogRescanBudget"/>) — reset every time that line is printed.</summary>
        private int _cycleCount;
        private int _cycleSweeps;
        private int _cycleClassifyFrames;
        private float _cycleWorstFrameMillis;
        private float _cycleWorstCommitMillis;
        private float _cycleWorstSweepMillis;
        private float _nextBudgetLogTime;
        private bool _budgetLoggedOnce;

        /// <summary>How often the budget line prints. Deliberately NOT per cycle (that would
        /// be one line every two seconds in a 9 MB log) and deliberately NOT change-triggered:
        /// a sweep that only logs when it finds something hides that it never ran, and this
        /// project has paid for that lesson. It prints on the FIRST completed cycle and then
        /// every five seconds, whatever it found — including nothing.</summary>
        private const float BudgetLogIntervalSeconds = 5f;

        /// <summary>Shared clock for the per-frame budget. One instance, started once — a
        /// Stopwatch that is allocated per slice would be its own cost.</summary>
        private static readonly System.Diagnostics.Stopwatch RescanClock =
            System.Diagnostics.Stopwatch.StartNew();

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
            _shaderVerdict.Clear(); // scene shaders died with their bundles — no dead keys
            _shaderWaterVerdict.Clear(); // …and so did the water shaders (PERF S2)
            AbandonRescanCycle();   // a census of the OLD scene may never commit into the new one
            _splitAnchors.Clear();
            _lastRoomCensusCount = -1; // fresh scene = fresh room registry (reveal diagnostics)
            _lastRoomCensusAnchored = -1;
            _lastLoggedMountedCount = -1;    // re-print the dressing census for the new scene
            _lastLoggedMountedRejected = -1;
            _lastLoggedStackedCount = -1;    // …and the stacked-shell census
            _lastLoggedStackedRejected = -1;
            _nextFastReclaim = 0f;           // fresh scene = fresh regen-churn sweep state
            _fastReclaimTotal = 0;
            _nextFastReclaimLog = 0f;
            _heartbeatFadeRenderers = -1;
            _lastLoggedFigureGuarded = -1;   // re-print the figure-guard proof line
            _cornerPieces.Clear();           // corner ownership dies with the scene
            _lastLoggedCornerCount = -1;
            _dumpedBodyShaders.Clear();      // re-dump body shader properties per scene
            _gateSliverLogged.Clear();       // re-log sliver-skipped gates per scene
            _ownershipChanges.Clear();       // fresh churn ledger per scene
            _masonryFadeShader = null;       // re-capture the dissolve-swap template
            _swapTotal = 0;
            _nativeTotal = 0;
            _nextSwapLog = 0f;
            _archRects.Clear();              // arch protection dies with the scene…
            _gateMemory.Clear();             // …and so does the reborn-gate state memory
            _waterRects.Clear();             // …and the fountain/pond protection rects
            _waterCensusSig = -1;            // …so the next scenario re-prints its census
            _lastLoggedReanchorCount = -1;   // re-print the re-anchor census
            _lastLoggedSeamCount = -1;       // …and the room-seam census (2026-08-09)
            _peerFades.Clear();              // peers re-state their fades for the new scene
            _loggedToggleMats.Clear();       // …and the toggle-native material lines
            _segmentListedIndex.Clear();     // scene renderers died with the scene — no dead keys
            _volumeScratch.Clear();
            _doorPropScratch.Clear();
        }

        /// <summary>
        /// LateUpdate on purpose: runs after every game Update, so the generator's renderer
        /// lists and the head pose are final for this frame. Fully guarded — a throw here must
        /// never starve the game loop (WorldUI lesson).
        /// </summary>
        private void LateUpdate()
        {
            // FRAME-ORDER WallSegmentFade.FadeDriver.LateUpdate LateUpdate-required [WallFade.Late]
            //   LateUpdate is the requirement, not a preference — see the doc comment above.
            //   Machine-checked so a later "all drivers tick in Update" tidy-up fails at commit
            //   time instead of producing a fade that samples last frame's head pose.
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
                {
                    ClearAllBlocks("inactive (toggle off / no scenario / no head)");
                    // PERF S2: a census in flight is measured against a scene we are no longer
                    // watching — resume would commit it blind. Drop it and its snapshot; the
                    // next active tick opens a fresh cycle.
                    AbandonRescanCycle();
                }
                _wasActive = false;
                return;
            }
            _wasActive = true;

            float now = Time.unscaledTime;
            bool sweptThisFrame = false;
            // PERF S2: the rescan is a three-stage pipeline now (sweep → budgeted census →
            // commit), not a single 118 ms call. A cycle is only STARTED when none is in
            // flight, so the reveal edge below cannot re-trigger every frame while the census
            // is still walking — _builtRoomCount is not updated until the commit runs.
            if (_rescanStage == RescanStage.Idle
                && (now >= _nextRescan || gen!.m_RoomRenderers.Count != _builtRoomCount))
            {
                _nextRescan = now + RescanIntervalSeconds;
                // A frame that had to take the FindObjectsOfType sweep has already spent more
                // than the budget allows, so it does no census work on top — the census starts
                // on the next frame. Nothing waits on it: the segment table in force is the
                // last committed one, exactly as it was between two old rescans.
                sweptThisFrame =
                    BeginRescanCycle(gen!, now, urgent: gen!.m_RoomRenderers.Count != _builtRoomCount);
            }
            if (_rescanStage != RescanStage.Idle && !sweptThisFrame)
                StepRescanCycle(gen!, now);
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
            // change the fade decision, which is exactly what the interval must NOT do.
            //
            // At interval 0 every frame evaluates, so this term is the frame delta again — measured
            // as a difference of two accumulated unscaledTime values rather than read from
            // unscaledDeltaTime, i.e. equal to within float noise, not bit-identical. The one real
            // difference is the 0.5 s clamp: after a long stall the old code fed the full stall
            // duration into the EMA (snapping the coverage almost to the raw sample), this one caps
            // the step. That is the safer direction for a hitch and cannot fire in steady state.
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
                // BOUNDLESS FAIL-SAFE (round 14 — user report: "Das Element über dem Rechteck
                // des Torbogens ist nun dauerhaft ausgeblendet und kommt auch nicht wieder,
                // obwohl es den Raum nicht verdeckt"). A segment without a decision AABB cannot
                // be judged, and the old `continue` skipped its ENTIRE tick — decision, fade
                // ramp AND Apply — so everything it had hidden stayed hidden with no path back:
                // a permanent latch, invisible in the diag (which skips boundless segments too)
                // and silent in the log (no state flip can happen if the state machine never
                // runs). That is the one thing the wall system may never do. A boundless
                // segment is now forced OFF and still runs the ramp + Apply below, so its
                // renderers, foliage, siblings, mounted props, stacked shell and body meshes
                // come back through the NORMAL animated un-fade. GATE COLUMNS are the class
                // that reaches this state (they legitimately own zero wall renderers, so every
                // pass that rebuilds an AABB from the renderer union can strip their bounds);
                // EnsureGateBounds re-anchors them from the arch seed at the next rescan, and
                // WatchLatch names anything that still disagrees.
                if (!seg.HasBounds)
                {
                    seg.State = false;
                    seg.PendingRaw = false;
                }
                // Undecidable-as-one-unit segments (see NeutralizeEngulfingSegments) are held
                // solid: state forced off, the fade below decays any residual block away.
                // DOORWAY segments (user ruling 2026-08-02): archways/doorways NEVER fade —
                // held permanently solid, no open/closed differentiation; recognition (the
                // per-door DoorRoot keying) exists only so their renderers cannot merge into
                // a fadeable wall segment. Any in-flight fade/MPB decays away right here.
                // ROOM-REVEAL FAIL-SAFE (fehlender_boden.png ruling): a wall whose room has no
                // VALID floor grid — unassociated, tile-UNANCHORED plane (median/bounds guess),
                // or a zero-sample grid (over the MaxTotalSamples budget) — must never fade:
                // its coverage would be measured against the wrong plane or the wrong room
                // (the mid-scenario reveal case), and a wrong fade deletes geometry. Solid is
                // the vanilla look, strictly safe; the wall joins the fade the moment its room
                // is anchored (next 2s rescan / reveal-triggered rescan).
                else if (seg.Engulfing || seg.DoorRoot != null
                    || !RoomDecisionValid(seg.RoomIndex))
                {
                    seg.State = false;
                    seg.PendingRaw = false;
                }
                // Room-coverage metric (EMA-smoothed) with the stepper-driven Schmitt
                // trigger + dwell hysteresis. The un-fade dwell is long, and much longer
                // still unless the perspective (head position / world grip) recently
                // changed — rotation-only head motion keeps the current state sticky.
                else if (evaluate)
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
                            LogStateFlip(seg); // R2 deliverable: shader variant applied
                        }
                    }
                }

                // Critically-damped-style exponential fade toward the debounced state — OR a
                // PEER's synced fade (MP sync, wire record 17): effective target =
                // max(local decision, any live peer set containing this wall's key). Composed
                // at the TARGET so the identical ramp, delivery and restore machinery runs
                // and a synced fade is visually indistinguishable from a local one;
                // dwell-free by design (the deciding peer already dwelled). Doorway segments
                // stay exempt here too — they never fade anywhere, on any machine.
                int peerFadeId = 0;
                bool remoteFade = seg.DoorRoot == null
                    && RemoteWantsFade(seg, now, out peerFadeId);
                LogRemoteFadeEdge(seg, remoteFade, peerFadeId);
                // GATE LIFT (round 12): the embedding wall fades with its gate column's
                // decision — same max-composition as the peer sync, native delivery.
                // Round-13 LINGER: the lift survives the gate segment's death (Apparance
                // prop churn destroys/rebirths the door prop every few seconds) so the
                // embedding wall does not flap with the prop lifecycle.
                bool gateLift = seg.DoorRoot == null
                    && ((seg.GateLift != null && seg.GateLift.State)
                        || now < seg.GateLiftUntil);
                if (seg.DoorRoot == null && seg.GateLift != null && seg.GateLift.State)
                    seg.GateLiftUntil = now + GateLiftLingerSeconds;
                LogGateLiftEdge(seg, gateLift);
                float target = (seg.State || remoteFade || gateLift) ? 1f : 0f;
                seg.Fade += (target - seg.Fade) * fadeStep;
                if (Mathf.Abs(target - seg.Fade) < 0.005f)
                    seg.Fade = target;
                // Round-14 watchdog: a fade the live coverage no longer supports must be
                // impossible to miss in the next hardware log (see WatchLatch).
                WatchLatch(seg, now, reevalArmed ? exitDwellMoved : exitDwellStationary,
                    remoteFade, gateLift);
                // A gate column's decision state must survive the door prop's death WITHOUT
                // outliving the evidence for it — snapshot it live, not once per rescan.
                if (seg.IsGateColumn)
                    WriteGateMemory(seg);

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

            // Shared corner pieces (round 7): min-fade of the adjacent walls, per frame.
            ApplyCornerPieces();
            // WHICH RENDERERS ARE ACTUALLY BEING FADED, BY NAME (user report 2026-08-19,
            // skelet.jpg: "Der Schädel ist immer noch nicht sichtbar"). Deliberately HERE — after
            // every applier has run, so the line reports what was written and not what was
            // intended (the ModBuild-164 lesson). Rate-limited and change-triggered; the
            // expensive half only runs on a frame where the written set moved. See
            // WallSegmentFade.FadeCensus.cs.
            LogFadeWriteCensus(now);
            // Regenerated shell pieces (Apparance churn) must be re-hidden faster than the
            // 2s rescan — see the fast-reclaim doc in WallSegmentFade.Stacked.cs.
            FastReclaimRegeneratedShell(now);

            // Re-log the heartbeat when the tracked set changes materially (walls stream in over
            // several rescans as Apparance generates, and adopted tilesets appear late) — the
            // first heartbeat of a scenario otherwise reports a half-built table forever.
            if (_heartbeatLogged && _heartbeatSegCount >= 0
                && Mathf.Abs(_segments.Count - _heartbeatSegCount) >= 5)
                _heartbeatLogged = false;
            // …and when the fade-capable renderer census changes (round 5): five hardware
            // rounds ran on a single STALE pre-generation heartbeat ("fade-capable 0") that
            // hid the unfadeable-wall TRIPWIRE — the line that names the shaders of cache
            // walls carrying NO fade-capable renderer (this keep tileset's masonry).
            if (_heartbeatLogged && _censusFadeRenderers != _heartbeatFadeRenderers)
                _heartbeatLogged = false;

            // [Optimize] QuietDiagnostics (PERF S1, 2026-08-09): the heartbeat block below is
            // the mod's second-largest burst and it was NOT gated. It re-arms on any ±5
            // segment change — i.e. constantly under Apparance's regen churn — and then runs
            // TWO more full-scene FindObjectsOfType walks (LogFloorColumnCensus's
            // MeshRenderer(includeInactive) sweep and LogMapTileCensus's ProceduralMapTile
            // one, ~10-15 ms EACH in the big room) plus a whole-table walk and a 40-line
            // string. It is pure DIAGNOSTIC output — nothing below writes a renderer, a
            // material or a segment — so suppressing it cannot change a single pixel; a
            // capture that wants only the [Perf] lines gets its frame time back. Leaving
            // _heartbeatLogged false means the census prints on the very next tick if the
            // switch is turned back off mid-session.
            if (!_heartbeatLogged && !PerfConfig.Quiet)
            {
                using var _censusScope = PerfMonitor.Scope("WallFade.Census");
                _heartbeatLogged = true;
                _heartbeatSegCount = _segments.Count;
                _heartbeatFadeRenderers = _censusFadeRenderers;
                LogFloorColumnCensus();
                LogMountedCensus();
                LogWallPathAudit();
                int highSegs = 0, lowSegs = 0, adoptedSegs = 0, engulfSegs = 0, foliage = 0;
                int siblings = 0, failSafeSegs = 0, doorways = 0, mounted = 0, stacked = 0;
                int bodyWalls = 0, bodyMeshes = 0, gates = 0;
                foreach (Segment s in _segments.Values)
                {
                    if (s.VariantHigh) highSegs++;
                    if (s.VariantLow) lowSegs++;
                    if (!s.FromWallCache) adoptedSegs++;
                    if (s.Engulfing) engulfSegs++;
                    foliage += s.Foliage.Count;
                    siblings += s.Siblings.Count;
                    mounted += s.Mounted.Count;
                    stacked += s.Stacked.Count;
                    if (s.Body.Count > 0)
                    {
                        bodyWalls++;
                        bodyMeshes += s.Body.Count;
                    }
                    if (!RoomDecisionValid(s.RoomIndex)) failSafeSegs++;
                    if (s.DoorRoot != null)
                        doorways++;
                    if (s.IsGateColumn)
                        gates++;
                }
                // ROUND-12 FAIL-SAFE FORENSICS (zero ADJACENT RE-ANCHOR lines at reach 4.0
                // — is the reach too short, or do these walls have no bounds at all?): name
                // each fail-safe wall with its nearest-anchored-room XZ gap (or NO-BOUNDS).
                var fsSb = new System.Text.StringBuilder();
                int fsListed = 0;
                foreach (Segment s in _segments.Values)
                {
                    if (RoomDecisionValid(s.RoomIndex) || s.DoorRoot != null)
                        continue;
                    if (fsListed++ >= 10) { fsSb.Append(", …"); break; }
                    if (fsSb.Length > 0)
                        fsSb.Append(", ");
                    string sn = s.Anchor != null ? s.Anchor.name : "<dead>";
                    if (!s.HasBounds)
                    {
                        fsSb.Append('\'').Append(sn).Append("' NO-BOUNDS");
                        continue;
                    }
                    float bestSq = float.PositiveInfinity;
                    for (int ri = 0; ri < _roomBounds.Count; ri++)
                    {
                        if (!RoomDecisionValid(ri))
                            continue;
                        Bounds room = _roomBounds[ri];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - s.Bounds.max.x,
                            s.Bounds.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - s.Bounds.max.z,
                            s.Bounds.min.z - room.max.z));
                        float sq = gx * gx + gz * gz;
                        if (sq < bestSq)
                            bestSq = sq;
                    }
                    fsSb.Append('\'').Append(sn).Append("' gap ")
                        .Append(float.IsInfinity(bestSq) ? "n/a" : Mathf.Sqrt(bestSq).ToString("F1"));
                }
                if (fsListed > 0)
                    VRLog.Info(Name, $"FAIL-SAFE GAPS: {fsSb} (re-anchor reach "
                        + $"{AdjacentReanchorMaxGapWU:0.0} wu — walls beyond it or without "
                        + "bounds stay solid; the round-12 datum for the next lever).");

                string unfadeable = _censusWallsWithoutFade > 0
                    ? $"; TRIPWIRE {_censusWallsWithoutFade} cache wall(s) carry NO fade-capable "
                      + $"renderer — their shaders: {string.Join(", ", _unfadeableWallShaders)} "
                      + $"— {bodyWalls} plain-mesh BODY column(s) ({bodyMeshes} mesh(es), "
                      + $"renderer.enabled fallback for materials WITHOUT native fade "
                      + $"controls; spanning courses hide, only fully-in-band courses stay "
                      + $"solid [round 8])"
                    : string.Empty;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': tracking "
                    + $"{_segments.Count} wall segments ({_segments.Count - adoptedSegs} from the "
                    + $"wall cache + {adoptedSegs} ADOPTED by shader, grouped by tile/parent; "
                    + $"fade-capable renderers {_censusFadeRenderers} = {_censusClaimed} claimed "
                    + $"+ {_censusAdopted} adopted; {_splitAnchors.Count} room-engulfing wall(s) "
                    + $"split per renderer, {engulfSegs} unsplittable held solid; {foliage} foliage "
                    + $"attachment(s) + {siblings} asset-sibling(s) + {mounted} wall-mounted "
                    + $"prop(s) (torches/candles — renderer.enabled only, Lights never touched) "
                    + $"ride their wall's fade; {stacked} STACKED SHELL piece(s) (fort/keep "
                    + $"superstructure meshes — extend their wall's occlusion AABB, dissolve "
                    + $"with it; [WallFade] StackedShellFade) ride their wall column; "
                    + $"{doorways} DOORWAY segment(s) held permanently solid (doorway fade "
                    + $"disabled — user ruling 2026-08-02); {gates} GATE column(s) (the wall "
                    + $"EMBEDDING a doorway — fades like any wall, only the arch rect stays "
                    + $"solid — user ruling 2026-08-07); "
                    + $"{failSafeSegs} wall(s) FAIL-SAFE solid (room unanchored/no floor grid)"
                    + $"{unfadeable}) "
                    + $"(shader variants: {lowSegs} LOW / "
                    + $"{highSegs} HIGH) against {_roomBounds.Count} LOGICAL room(s) "
                    + $"(grouped from {_builtRoomCount} volume renderer(s) by the game's CMap "
                    + $"room identity — round 4) / {_allSamples.Count} floor samples "
                    + $"({_roomsAnchored}/"
                    + $"{_roomBounds.Count} rooms tile-anchored, plane +"
                    + $"{FloorSampleEpsilon:0.00} wu, y {_sampleYMin:F2}..{_sampleYMax:F2}) — "
                    + $"per-wall ROOM-coverage fade (strict own-room accounting; "
                    + $"EMA tau {FractionTauSeconds:0.00}s; on ≥{onFraction:0.00}, off "
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
        /// Can the coverage metric be trusted for this room? Requires: an associated room, a
        /// TILE-ANCHORED floor plane (an unanchored plane is a median/bounds GUESS — the exact
        /// class of frame error the round-6 hardware log caught, and the mid-scenario-reveal
        /// hazard: a freshly revealed room without its volume anchor yet), and a non-empty
        /// sample grid (rooms past the MaxTotalSamples budget get none). Walls failing this are
        /// held SOLID by the decision loop — fade decisions on a guessed frame delete geometry.
        /// </summary>
        private bool RoomDecisionValid(int room) =>
            room >= 0
            && room < _roomFloorAnchored.Count && _roomFloorAnchored[room]
            && room < _roomSampleCount.Count && _roomSampleCount[room] > 0;

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
            seg.LastDecidingRoom = -1;
            int room = seg.RoomIndex;
            if (room < 0 || room >= _roomSampleCount.Count)
                return 0f;
            int total = _roomSampleCount[room];
            seg.LastRoomTotal = total;
            seg.LastDecidingRoom = room;
            if (total <= 0)
                return 0f;

            if (seg.Bounds.Contains(headPos))
            {
                // Wall in the face — treat as full coverage of its room.
                seg.LastBlocked = total;
                seg.LastRoomVisible = total;
                return 1f;
            }

            // STRICT OWN-ROOM accounting (round 4 — user ruling "normale Raum-Logik"; the
            // round-3 cross-room MAX is retired). The room is the LOGICAL room now: all
            // volume renderers of one game CMap merged into one grid, so a wall that
            // fronts a multi-volume room measures against that room's WHOLE floor.
            float fraction = RoomBlockedFraction(seg, headPos, room,
                out int blocked, out int visible, out _);
            seg.LastBlocked = blocked;
            seg.LastRoomVisible = visible;

            // ROOM SEAM (user report 2026-08-09, the wall that faded "falsch rum"): a wall
            // standing BETWEEN two rooms owns both of them — see the long note in
            // AssociateRooms for why picking one of them is a coin flip and why picking the
            // wrong one inverts the fade exactly as reported. Its coverage is therefore the
            // MAX over the rooms it borders: whichever room it is currently hiding from the
            // head is the room the user wants opened, and the wall must fade from EITHER
            // side. Segment.BorderRooms is empty for every wall that borders one room, so
            // this loop does not run at all for them — their behaviour is untouched.
            for (int i = 0; i < seg.BorderRooms.Count; i++)
            {
                int alt = seg.BorderRooms[i];
                if (alt < 0 || alt >= _roomSampleCount.Count || _roomSampleCount[alt] <= 0)
                    continue;
                float altFraction = RoomBlockedFraction(seg, headPos, alt,
                    out int altBlocked, out int altVisible, out int altTotal);
                if (altFraction <= fraction)
                    continue;
                fraction = altFraction;
                seg.LastBlocked = altBlocked;
                seg.LastRoomVisible = altVisible;
                seg.LastRoomTotal = altTotal;
                seg.LastDecidingRoom = alt;
            }
            return fraction;
        }

        /// <summary>The per-room half of the metric: fraction of ROOM's grid that is in
        /// view-direction AND whose head→point segment this wall's AABB clearly
        /// interrupts.</summary>
        private float RoomBlockedFraction(Segment seg, Vector3 headPos, int room,
            out int blockedOut, out int visibleOut, out int totalOut)
        {
            blockedOut = 0;
            visibleOut = 0;
            totalOut = room >= 0 && room < _roomSampleCount.Count ? _roomSampleCount[room] : 0;
            if (totalOut <= 0)
                return 0f;

            Bounds b = seg.Bounds;
            int start = _roomSampleStart[room];
            int end = Mathf.Min(start + totalOut, Mathf.Min(_allSamples.Count, _sampleVisible.Length));
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
            blockedOut = blocked;
            visibleOut = roomVisible;
            return blocked / (float)totalOut;
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
        private void LogStateFlip(Segment seg)
        {
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            if (!seg.FromWallCache)
                wall += "~"; // shader-adopted group (tile/parent-anchored), not a cache wall
            string variant = seg.VariantHigh ? (seg.VariantLow ? "HIGH+LOW" : "HIGH") : "LOW";
            if (seg.State)
            {
                // Which renderers this fade actually touches (name@AABB-top, first six): the
                // decisive line when a "hole" appears — if a floor piece is listed here, it is
                // either a ground renderer the strip missed or ground fused into a wall MESH.
                var rl = new System.Text.StringBuilder();
                int listed = 0;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                        continue;
                    if (listed++ >= 6) { rl.Append(", …"); break; }
                    if (rl.Length > 0) rl.Append(", ");
                    rl.Append(r.name).Append('@').Append(r.bounds.max.y.ToString("F1"));
                }
                string cutoff = $"map occ(r=1,a=0)→m=0, _Cutoff={seg.HeldCutoff:0.00} " +
                    (seg.CutoffAuthored ? "(authored)" : "(fallback)");
                VRLog.Info(Name,
                    $"fade ON '{wall}' shader '{seg.ShaderNames}' [{variant}] " +
                    $"({seg.Renderers.Count} renderer(s), {seg.ToggleNative} toggle-native: " +
                    $"{rl}; +{seg.Foliage.Count} foliage, " +
                    $"+{seg.Siblings.Count} asset-sibling(s), +{seg.Mounted.Count} mounted " +
                    $"prop(s) [{MountedNames(seg)}], +{seg.Stacked.Count} stacked shell " +
                    $"piece(s), +{seg.Body.Count} plain body mesh(es); " +
                    // ROUND 15: the blanket '[enabled-only]' marker was a lie once the
                    // attachments learned to dissolve — it is now the live channel breakdown,
                    // and 'enabled-only 0' is the proof that nothing in this fade pops. The
                    // DISSOLVE CENSUS line names any remainder and why.
                    $"delivery: {DissolveBreakdown(seg)}) — " +
                    $"held state: " +
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
            string name = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            if (name.Length > 24)
                name = name.Substring(0, 24);
            if (!seg.FromWallCache)
                name += "~"; // shader-adopted group
            Bounds b = seg.Bounds;
            _diagSb.Append(" | '").Append(name)
                   .Append("' r").Append(seg.RoomIndex);
            // ROOM SEAM (2026-08-09): a wall between two rooms is judged against both — print
            // which one actually decided, so the next hardware log reads the side directly
            // instead of leaving it to inference ("r0>1" = own room 0, room 1 won the max).
            if (seg.BorderRooms.Count > 0)
            {
                _diagSb.Append('>');
                if (seg.LastDecidingRoom >= 0)
                    _diagSb.Append(seg.LastDecidingRoom);
                else
                    _diagSb.Append('?');
            }
            _diagSb.Append(" raw").Append(seg.LastRaw.ToString("F2"))
                   .Append(" ema").Append(seg.Smooth.ToString("F2"))
                   .Append(" blk").Append(seg.LastBlocked).Append('/').Append(seg.LastRoomTotal)
                   .Append(" v").Append(seg.LastRoomVisible)
                   .Append(seg.State ? " ON " : " off ").Append(seg.Fade.ToString("F2"))
                   .Append(" wy[").Append(b.min.y.ToString("F2")).Append("..")
                   .Append(b.max.y.ToString("F2")).Append(']')
                   .Append(" e").Append(seg.BlockEps.ToString("F2"))
                   .Append(seg.VariantHigh ? (seg.VariantLow ? " vH+L" : " vHIGH")
                       : (seg.VariantLow ? " vLOW" : seg.Body.Count > 0 ? " vBODY" : " vLOW"));
            // Stacked shell pieces riding this wall (keep stories) — only printed when any
            // exist, so scenes without superstructures keep their diag lines unchanged.
            if (seg.Stacked.Count > 0)
                _diagSb.Append(" S").Append(seg.Stacked.Count);
            // Plain-mesh wall body (round 6): masonry without a fade shader, enabled-only.
            if (seg.Body.Count > 0)
                _diagSb.Append(" B").Append(seg.Body.Count);
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
            // Room grid empty (over the sample budget) or no room at all: the wall is held
            // solid by the reveal fail-safe — visible in the log as the reason it never fades.
            if (room < 0 || (room < _roomSampleCount.Count && _roomSampleCount[room] <= 0))
                _diagSb.Append(" !NOGRID");
            // A single mesh whose AABB is fat in BOTH horizontal axes (ring/corner piece) cannot
            // be split further — its coverage numbers may read permanently high. Flagged so the
            // hardware log distinguishes "genuinely occluding" from "AABB artifact"; !ENGULF
            // additionally marks the ones the containment test therefore holds solid.
            if (Mathf.Min(b.size.x, b.size.z) > GroupSlabMaxHorizontal)
                _diagSb.Append(" !FAT");
            if (seg.Engulfing)
                _diagSb.Append(" !ENGULF");
            // Doorway recognition (user ruling 2026-08-02): held permanently solid.
            if (seg.DoorRoot != null)
                _diagSb.Append(" DOORWAY");
            // Gate column (user ruling 2026-08-07): the doorway-EMBEDDING wall — fades.
            if (seg.IsGateColumn)
                _diagSb.Append(" GATE");
        }

        // ---- fade delivery ----------------------------------------------------------------

        /// <summary>
        /// Apply the segment's fade through the shader's own path (see class header).
        /// Reapplied every frame while faded because Apparance may regenerate wall renderers
        /// mid-fade; a null renderer triggers a prompt rescan.
        /// </summary>
        /// <summary>Return one foliage renderer to its vanilla state (visible, no MPB).</summary>
        private static void RestoreFoliageRenderer(MeshRenderer r)
        {
            if (r == null)
                return;
            if (!r.enabled)
                r.enabled = true;
            r.SetPropertyBlock(null);
        }

        /// <summary>Restore ALL of a segment's foliage — called whenever the segment leaves the
        /// table or goes solid, so no bush can stay hidden without an owner.</summary>
        private static void RestoreSegmentFoliage(Segment seg)
        {
            if (seg.FoliageState == 0)
                return;
            seg.FoliageState = 0;
            foreach (MeshRenderer f in seg.Foliage)
            {
                if (f != null)
                    RestoreFoliageRenderer(f);
            }
        }

        /// <summary>Put ONE asset sibling back exactly as authored: its dissolve channel undone
        /// (authored materials back, our copies destroyed), property block cleared, renderer
        /// visible again. Round 15 — before that, siblings were a pure enabled toggle.</summary>
        private static void RestoreSiblingProp(Segment seg, MeshRenderer? r)
        {
            if (r == null)
                return;
            if (seg.SiblingProps.TryGetValue(r, out MountedProp? p))
            {
                seg.SiblingProps.Remove(r);
                bool wroteBlock = p.NativeFade || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                RestorePropSwap(p, r);
                if (wroteBlock)
                    r.SetPropertyBlock(null);
            }
            if (!r.enabled)
                r.enabled = true;
        }

        /// <summary>Restore ALL of a segment's asset siblings (doors/trim of a mixed asset) —
        /// called on every path where the segment stops owning them (unfade, segment drop,
        /// group split, toggle-off, teardown), so no door can stay hidden without an owner.
        /// Round 15: also undoes their dissolve channel, and sweeps records whose renderer died
        /// so a material copy can never leak.</summary>
        private static void RestoreSegmentSiblings(Segment seg)
        {
            if (seg.SiblingState == 0)
                return;
            seg.SiblingState = 0;
            foreach (MeshRenderer s in seg.Siblings)
                RestoreSiblingProp(seg, s);
            // Anything still recorded is no longer in the sibling list (asset churn / a dead
            // renderer): our copies must die with it either way, and a survivor is fully
            // re-authorized here rather than left swapped with no owner.
            foreach (MountedProp p in seg.SiblingProps.Values)
            {
                Renderer r = p.Renderer;
                bool wroteBlock = p.NativeFade || p.ColorId >= 0 || p.CutoffId >= 0
                    || p.DissolveControlId >= 0;
                RestorePropSwap(p, r);
                if (r == null)
                    continue;
                if (wroteBlock)
                    r.SetPropertyBlock(null);
                if (!r.enabled)
                    r.enabled = true;
            }
            seg.SiblingProps.Clear();
        }

        /// <summary>
        /// Dissolve the segment's asset siblings alongside its fade (Torbogen ruling: the WHOLE
        /// doorway asset disappears, not just its shader-matched frame/pillars).
        ///
        /// ROUND 15 — siblings were the last enabled-only class: they stayed fully solid through
        /// the dissolve and switched off at the end, on the (round-3) assumption that "siblings
        /// run arbitrary opaque shaders where a cutoff MPB means nothing". That assumption is
        /// what the gate bug disproved for every attachment type — a channel-less material gets
        /// COPIES on the game's masonry fade shader, a toggle-native one gets the wall's own
        /// map/_Cutoff ramp (see WallSegmentFade.Dissolve.cs). So they ramp now, on both edges,
        /// with the guaranteed renderer-disable still at the end of the sweep.
        /// </summary>
        private void ApplySiblings(Segment seg)
        {
            if (seg.Siblings.Count == 0)
            {
                if (seg.SiblingState != 0)
                    RestoreSegmentSiblings(seg);
                return;
            }
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentSiblings(seg);
                return;
            }
            // No held-state early-out (round 5, regen churn): re-hide per frame — in the
            // steady state this is one enabled compare per sibling.
            foreach (MeshRenderer s in seg.Siblings)
            {
                if (s == null)
                    continue;
                if (!seg.SiblingProps.TryGetValue(s, out MountedProp? p))
                {
                    p = ClassifyProp(s);
                    seg.SiblingProps[s] = p;
                }
                EnsureDissolveChannel(p);
                DriveProp(p, seg.Fade);
                if (want == 2)
                {
                    if (s.enabled)
                        s.enabled = false;
                }
                else if (!s.enabled)
                {
                    s.enabled = true;
                }
            }
            seg.SiblingState = want;
        }

        /// <summary>
        /// Drive the segment's foliage attachments alongside its fade: mid-dissolve the cutout
        /// leaves ride an alpha-cutoff ramp (visually the same dissolve as the wall), and in the
        /// held state the renderer is disabled outright so opaque twig materials vanish too.
        /// All of it reverses exactly on unfade.
        /// </summary>
        private void ApplyFoliage(Segment seg)
        {
            if (seg.Foliage.Count == 0)
                return;
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentFoliage(seg);
                return;
            }
            // No held-state early-out (round 5, regen churn): a bush regenerated while the
            // wall is held faded must be re-hidden this frame, not never.
            if (want == 1)
            {
                _foliageMpb ??= new MaterialPropertyBlock();
                _foliageMpb.Clear();
                _foliageMpb.SetFloat(CutoffId,
                    Mathf.Lerp(FoliageCutoffStart, FoliageCutoffEnd, seg.Fade));
            }
            foreach (MeshRenderer f in seg.Foliage)
            {
                if (f == null)
                    continue;
                if (want == 2)
                {
                    if (f.enabled)
                        f.enabled = false;
                }
                else
                {
                    if (!f.enabled)
                        f.enabled = true;
                    f.SetPropertyBlock(_foliageMpb);
                }
            }
            seg.FoliageState = want;
        }

        private void Apply(Segment seg)
        {
            // DOORWAY segments (user ruling 2026-08-02) take this same path: the decision
            // loop pins their state to solid, so the fade decays to 0 and any residual
            // MPB/foliage/sibling state clears through the normal branches below.
            ApplyFoliage(seg);
            ApplySiblings(seg);
            ApplyMounted(seg);
            ApplyStacked(seg);
            ApplyBody(seg);
            // ROUND-15 DISSOLVE CENSUS: the appliers above have just established each piece's
            // dissolve channel, so this is the moment the breakdown is true. Logged once per
            // fade episode (whatever drove it — local decision, peer sync, gate lift), re-logged
            // only when the enabled-only count changes. See WallSegmentFade.Dissolve.cs.
            if (seg.Fade > 0f)
                LogDissolveCensus(seg);
            else
                seg.DissolveCensusLogged = false;
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
            // TOGGLE-NATIVE materials (round 8, Amp_Basic_N_MRAO masonry): open their gate
            // too — the same fade subgraph behind a differently-named material switch.
            // Unused entry on the classic WallFade shaders, exactly like _ToggleWallfade on
            // LOW. KNOWN RISK (logged per material by LogToggleNativeMaterialOnce): if
            // Amplify compiled the switch as a compile-time keyword, this float is inert and
            // the material becomes the shader-swap candidate — the toggle diag line plus the
            // next hardware round adjudicate.
            _mpb.SetFloat(WallFadeOnMatId, 1f);
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
        /// <summary>
        /// FIGURE RESTITUTION (round 7): sweep the shared hidden/ramped ledger against
        /// <see cref="IsFigureOrActorRenderer"/> and restore every violator NOW — a figure
        /// renderer adopted before the guard existed (or through any future gap) must come
        /// back the moment the guard classifies it, and the guarded collectors + sticky
        /// loops will not re-take it. Runs first in every rescan; logs once per incident.
        /// </summary>
        private readonly List<MountedProp> _figurePurgeScratch = new();

        private void PurgeFigureRenderers()
        {
            if (_mountedTouched.Count == 0)
                return;
            _figurePurgeScratch.Clear();
            foreach (MountedProp p in _mountedTouched.Values)
            {
                if (p.Renderer != null && IsFigureOrActorRenderer(p.Renderer))
                    _figurePurgeScratch.Add(p);
            }
            if (_figurePurgeScratch.Count == 0)
                return;
            var names = new System.Text.StringBuilder();
            foreach (MountedProp p in _figurePurgeScratch)
            {
                if (names.Length > 0)
                    names.Append(", ");
                names.Append('\'').Append(p.Renderer.name).Append('\'');
                RestoreProp(p);
            }
            VRLog.Warn(Name,
                $"FIGURE RESTITUTION: restored {_figurePurgeScratch.Count} previously-adopted "
                + $"FIGURE renderer(s) ({names}) — figures are NEVER touched by any wall "
                + "system (round-7 ruling, same severity as the Lights rule).");
            _figurePurgeScratch.Clear();
        }

        // ---- the rescan pipeline (PERF S2) -------------------------------------------------

        /// <summary>
        /// The scene's STRUCTURAL SIGNATURE: the counters that move when a renderer this system
        /// cares about is CREATED. Cheap by construction — five list/registry counts, no walk.
        ///
        /// <para>Every one of these is monotonic within a scene and every one of them is the
        /// game's own bookkeeping, not ours: <c>m_RoomRenderers</c> grows on a room reveal,
        /// <c>ProceduralWall.m_WallCache</c> grows as Apparance builds walls, and the three
        /// SceneRegistry registries grow as tiles / door props / occlusion volumes enrol in
        /// their own lifecycle methods (they count enrolled entries, destroyed ones included,
        /// so a churned scene still shows the growth). A change here means the last snapshot
        /// can no longer be complete, so the next cycle sweeps.</para>
        /// </summary>
        private static int SceneStructureSignature(TilesOcclusionGenerator gen)
        {
            int sig = 17;
            try
            {
                sig = sig * 31 + gen.m_RoomRenderers.Count;
                sig = sig * 31 + (ProceduralWall.m_WallCache?.Count ?? 0);
                sig = sig * 31 + SceneRegistry.MapTiles.Count;
                sig = sig * 31 + SceneRegistry.DoorProps.Count;
                sig = sig * 31 + SceneRegistry.Volumes.Count;
            }
            catch
            {
                // A game structure mid-build: treat it as "changed" so the cycle sweeps. Never
                // as "unchanged", which would let a reused snapshot outlive its scene.
                return _structureUnknown++;
            }
            return sig;
        }

        private static int _structureUnknown = int.MinValue / 2;

        /// <summary>
        /// Open a rescan cycle: take (or reuse) the scene snapshot, then hand over to
        /// <see cref="StepRescanCycle"/>. The SWEEP is the one part that cannot be sliced —
        /// <c>FindObjectsOfType</c> is atomic and O(every loaded object) — so it gets its own
        /// PerfMonitor scope and is taken as rarely as correctness allows (see
        /// <see cref="SnapshotMaxAgeSeconds"/>).
        /// </summary>
        /// <returns>True when this frame had to take the (atomic, unsliceable) scene sweep, so
        /// the caller can leave the census to the next frame instead of stacking it on top.
        /// </returns>
        private bool BeginRescanCycle(TilesOcclusionGenerator gen, float now, bool urgent)
        {
            _rescanUrgent = urgent;
            int sig = SceneStructureSignature(gen);
            bool mustSweep = _snapshot.Length == 0
                || sig != _snapshotSignature
                || now - _snapshotTakenAt >= SnapshotMaxAgeSeconds
                || float.IsNegativeInfinity(_snapshotTakenAt);
            if (mustSweep)
            {
                using (PerfMonitor.Scope("WallFade.Sweep"))
                {
                    float t0 = (float)RescanClock.Elapsed.TotalMilliseconds;
                    _snapshot = UnityEngine.Object.FindObjectsOfType<Renderer>();
                    float ms = (float)RescanClock.Elapsed.TotalMilliseconds - t0;
                    if (ms > _cycleWorstSweepMillis)
                        _cycleWorstSweepMillis = ms;
                }
                _snapshotSignature = sig;
                _snapshotTakenAt = now;
                _cycleSweeps++;
                _classifyCold = true;
            }
            else
            {
                // WARM: same array, re-read live. See RendererFact for what that can and
                // cannot miss — and _censusMaterialsDirty for the one thing that forces a
                // full re-derivation without a fresh sweep.
                _classifyCold = _censusMaterialsDirty;
            }
            _censusMaterialsDirty = false;
            if (_facts.Length < _snapshot.Length)
                _facts = new RendererFact[Mathf.NextPowerOfTwo(Mathf.Max(_snapshot.Length, 256))];
            _factCount = _snapshot.Length;
            _classifyCursor = 0;
            _factWallFade.Clear();
            _factWater.Clear();
            _rescanStage = RescanStage.Classify;
            return mustSweep;
        }

        /// <summary>
        /// Advance the cycle within this frame's budget. CLASSIFY is resumable and touches
        /// nothing; COMMIT is atomic and is where every mutation lives, so it is never split.
        /// </summary>
        private void StepRescanCycle(TilesOcclusionGenerator gen, float now)
        {
            float budget = _rescanUrgent ? ClassifyUrgentBudgetMillis : ClassifyBudgetMillis;
            float frameStart = (float)RescanClock.Elapsed.TotalMilliseconds;

            if (_rescanStage == RescanStage.Classify)
            {
                _cycleClassifyFrames++;
                using (PerfMonitor.Scope("WallFade.Classify"))
                {
                    while (_classifyCursor < _factCount)
                    {
                        int end = Mathf.Min(_classifyCursor + ClassifyChunk, _factCount);
                        ClassifySlice(_classifyCursor, end);
                        _classifyCursor = end;
                        if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget)
                            break;
                    }
                }
                if (_classifyCursor < _factCount)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
                _rescanStage = RescanStage.Commit;
                // Only run the commit on the SAME frame when the census barely cost anything —
                // otherwise the frame that finishes the census would also carry the commit and
                // we would be back to one fat frame, just a smaller one.
                if ((float)RescanClock.Elapsed.TotalMilliseconds - frameStart >= budget * 0.5f)
                {
                    NoteCycleFrame(frameStart);
                    return;
                }
            }

            // PERF S1 kept its own scope here and the name is load-bearing: 'WallFade.Rescan'
            // is what the [Perf] STEPS line ranks and what the integrator greps. It now covers
            // the COMMIT only — the sweep and the census report as 'WallFade.Sweep' and
            // 'WallFade.Classify', so the three costs are separable for the first time.
            using (PerfMonitor.Scope("WallFade.Rescan"))
            {
                float c0 = (float)RescanClock.Elapsed.TotalMilliseconds;
                Rescan(gen);
                float ms = (float)RescanClock.Elapsed.TotalMilliseconds - c0;
                if (ms > _cycleWorstCommitMillis)
                    _cycleWorstCommitMillis = ms;
            }
            _rescanStage = RescanStage.Idle;
            _rescanUrgent = false;
            _cycleCount++;
            NoteCycleFrame(frameStart);
            LogRescanBudget(now);
        }

        /// <summary>
        /// Drop the cycle in flight and every reference it holds. Called on scene load and
        /// teardown: a census taken over the OLD scene may never reach a commit, and the
        /// snapshot array must not keep a scene's worth of dead renderers alive. The next tick
        /// opens a fresh cycle with a fresh sweep (the signature check sees an empty snapshot).
        /// </summary>
        private void AbandonRescanCycle()
        {
            _rescanStage = RescanStage.Idle;
            _rescanUrgent = false;
            _classifyCursor = 0;
            _factCount = 0;
            _factWallFade.Clear();
            _factWater.Clear();
            _snapshot = System.Array.Empty<Renderer>();
            _facts = System.Array.Empty<RendererFact>();
            _snapshotSignature = -1;
            _snapshotTakenAt = float.NegativeInfinity;
        }

        private void NoteCycleFrame(float frameStart)
        {
            float ms = (float)RescanClock.Elapsed.TotalMilliseconds - frameStart;
            if (ms > _cycleWorstFrameMillis)
                _cycleWorstFrameMillis = ms;
        }

        /// <summary>
        /// Classify <c>[from, to)</c> of the snapshot into <see cref="_facts"/>. This is the
        /// ONLY place the four collection passes' per-renderer predicates are evaluated, and it
        /// evaluates each of them exactly once per renderer per cycle.
        ///
        /// <para>SAME VERDICTS, SAME CODE. Every flag below is filled by the very method the
        /// pass used to call inline — <see cref="IsModObject"/>,
        /// <see cref="RendererUsesWallFade"/>, <see cref="RendererUsesFoliage"/>,
        /// <see cref="IsWaterShader"/> + <see cref="IsWaterNameFamily"/>,
        /// <see cref="IsMountableRendererType"/> — over the same
        /// array in the same order. The two index lists are filled under the same conditions
        /// their pass's own loop used, so the passes see the same members in the same sequence
        /// (which matters: the first fade renderer of a group seeds that group's anchor).</para>
        ///
        /// <para>WARM SLICES re-read only what a live renderer can change without being
        /// recreated: liveness and the transform-derived bounds/anchor. Shader family, name and
        /// component type are fixed for a renderer's lifetime, so re-deriving them would be
        /// spending the exact interop cost this change exists to remove (the one exception,
        /// our own dissolve material swap, forces a cold cycle — see
        /// <c>_censusMaterialsDirty</c>). <c>enabled</c> is not cached at all: see
        /// <see cref="RendererFact.Mod"/> for why. The
        /// index lists are rebuilt on every slice, warm or cold, so a renderer that died is
        /// dropped from them immediately.</para>
        /// </summary>
        private void ClassifySlice(int from, int to)
        {
            for (int i = from; i < to; i++)
            {
                Renderer? r = _snapshot[i];
                ref RendererFact f = ref _facts[i];
                if (r == null)
                {
                    f.R = null;
                    f.Mesh = null;
                    continue;
                }
                bool cold = _classifyCold || !ReferenceEquals(f.R, r);
                if (cold)
                {
                    f.R = r;
                    f.Mesh = r as MeshRenderer;
                    f.Particles = r is ParticleSystemRenderer;
                    f.Mountable = IsMountableRendererType(r);
                    ClassifyMaterialsAndName(r, ref f);
                }
                f.Bounds = r.bounds;
                f.Anchor = f.Particles
                    ? r.transform.position
                    : new Vector3(f.Bounds.center.x, f.Bounds.min.y, f.Bounds.center.z);

                // AdoptShaderMatchedWalls' input: `any is MeshRenderer && RendererUsesWallFade`.
                // No enabled/mod filter — the old loop had none either.
                if (f.Mesh != null && f.WallFadeShader)
                    _factWallFade.Add(i);
                // CollectWaterFeatures' input. `enabled` is deliberately NOT part of the
                // membership test even though the pass's own guard has it: it is the one flag
                // the game flips at will, so a census verdict could go stale and drop a water
                // surface — and a dropped water surface means a fountain that fades, which the
                // 2026-08-09 ruling forbids ("lass den Brunnen niemals faden"). The pass reads
                // it LIVE instead; membership here is only the parts that cannot change.
                if (f.WaterSurface && !f.Mod)
                    _factWater.Add(i);
            }
        }

        /// <summary>
        /// The four name/shader verdicts a scene renderer carries, derived from ONE
        /// <c>GetSharedMaterials</c> and ONE <c>r.name</c> read.
        ///
        /// <para>WHY THIS EXISTS AS ITS OWN METHOD. The four collection passes asked four
        /// separate questions of the same renderer — <see cref="RendererUsesWallFade"/>,
        /// <see cref="RendererUsesFoliage"/>, the water test (<see cref="IsWaterShader"/> +
        /// <see cref="IsWaterNameFamily"/>) and
        /// <see cref="IsModObject"/> — and each of those opened its own
        /// <c>GetSharedMaterials</c> and/or its own <c>r.name</c>. Both are INTEROP calls and
        /// <c>r.name</c> allocates a managed string every time. Over 8630 renderers that was
        /// ~26 000 material fetches and ~26 000 string allocations per rescan for four
        /// questions with a single, shared answer. The verdict logic below is those four
        /// methods' bodies, unchanged, sharing one fetch: the per-Shader memos are the same
        /// three dictionaries they already used (so a shader seen by any of the three questions
        /// answers instantly for the others), the mod test is the same layer-or-prefix pair,
        /// and the water name family is the same token list, consulted — as before — only when
        /// the shader family already said no.</para>
        /// </summary>
        private void ClassifyMaterialsAndName(Renderer r, ref RendererFact f)
        {
            bool wallFade = false, foliage = false, water = false;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            for (int mi = 0; mi < _matScratch.Count; mi++)
            {
                Material mat = _matScratch[mi];
                if (mat == null)
                    continue;
                Shader sh = mat.shader;
                if (sh == null)
                    continue;
                if (!wallFade)
                {
                    if (!_shaderVerdict.TryGetValue(sh, out bool capable))
                    {
                        capable = IsWallFadeShaderName(sh.name);
                        _shaderVerdict[sh] = capable;
                    }
                    wallFade = capable;
                }
                if (!foliage)
                {
                    if (!_shaderFoliageVerdict.TryGetValue(sh, out bool leafy))
                    {
                        leafy = IsFoliageShaderName(sh.name);
                        _shaderFoliageVerdict[sh] = leafy;
                    }
                    foliage = leafy;
                }
                if (!water)
                    water = IsWaterShader(sh);
            }
            // The wall-fade and foliage families are MeshRenderer questions (that is the type
            // both RendererUsesWallFade and RendererUsesFoliage take); the water family is not
            // — the water test takes a plain Renderer and the water pass ran it over the whole
            // sweep, so it stays that way.
            f.WallFadeShader = wallFade && f.Mesh != null;
            f.FoliageShader = foliage && f.Mesh != null;

            string n = r.name;
            // IsModObject, verbatim: the mod layer OR the repo-convention name prefix (hardware
            // round 3 — the MR sky backing 'GloomhavenVR.MrBacking' leaked into the near-miss
            // census through the layer-only test).
            f.Mod = r.gameObject.layer == VRLayers.ModLayer
                || n.StartsWith("GloomhavenVR.", StringComparison.Ordinal);
            // The authored water name family, consulted — as before — only when the shader
            // family already said no. See WallSegmentFade.Water.cs.
            f.WaterSurface = water || IsWaterNameFamily(n);
        }

        /// <summary>
        /// THE ATTRIBUTABLE LINE. One [WallSegmentFade] BUDGET line every
        /// <see cref="BudgetLogIntervalSeconds"/>, printed whatever it found — including
        /// nothing. It names how many objects were walked, WHERE they came from (a fresh sweep
        /// or a reused snapshot), how the frame budget was spent, and the worst single frame
        /// any stage of the pipeline cost in the window. That last number is the one the
        /// integrator reads against [Perf] STEPS' 'WallFade.Rescan' worst field.
        /// </summary>
        private void LogRescanBudget(float now)
        {
            if (_budgetLoggedOnce && now < _nextBudgetLogTime)
                return;
            _budgetLoggedOnce = true;
            _nextBudgetLogTime = now + BudgetLogIntervalSeconds;
            VRLog.Info(Name,
                $"BUDGET: {_cycleCount} rescan cycle(s) completed since the last line — "
                + $"{_factCount} scene renderer(s) classified per cycle, {_cycleSweeps} of them "
                + $"from a FRESH FindObjectsOfType<Renderer> sweep (worst "
                + $"{_cycleWorstSweepMillis:F2}ms) and the rest from the reused snapshot "
                + $"(structural signature unchanged, age cap {SnapshotMaxAgeSeconds:0.0}s); "
                + $"census spread over {_cycleClassifyFrames} frame(s) at "
                + $"{ClassifyBudgetMillis:0.0}ms/frame ({ClassifyUrgentBudgetMillis:0.0}ms on a "
                + $"room-reveal edge); {_factWallFade.Count} fade-capable + {_factWater.Count} "
                + $"water renderer(s) indexed; WORST COMMIT {_cycleWorstCommitMillis:F2}ms, "
                + $"WORST SINGLE FRAME across all stages {_cycleWorstFrameMillis:F2}ms. "
                + "Before PERF S2 this work was ONE 118ms frame every 2s (ModBuild 226: "
                + "WallFade.Rescan 118.174ms avg, worst 141.18ms, 15 stalls per 30s window).");
            _cycleCount = 0;
            _cycleSweeps = 0;
            _cycleClassifyFrames = 0;
            _cycleWorstFrameMillis = 0f;
            _cycleWorstCommitMillis = 0f;
            _cycleWorstSweepMillis = 0f;
        }

        private void Rescan(TilesOcclusionGenerator gen)
        {
            // PERF S1: one memo scope for the whole (synchronous) rescan — see
            // FigureAncestryMemo for why that cannot change a single figure verdict.
            BeginFigureMemo();
            try { RescanCore(gen); }
            finally { EndFigureMemo(); }
        }

        private void RescanCore(TilesOcclusionGenerator gen)
        {
            // Figures first (round 7): nothing below may keep or re-take an actor renderer.
            PurgeFigureRenderers();
            // Tile-plane anchors (round 7): each TilesOcclusionVolume knows its room's
            // renderers AND its CentralTile, whose transform sits ON the tile plane. The
            // renderer bounds are only trusted for the XZ footprint — their Y is the
            // occlusion-proxy artifact the round-6 hardware log caught (tops ~9 wu above
            // the actual floor, see class header).
            _floorYByRenderer.Clear();
            _roomMapByRenderer.Clear();
            _roomMapLabelByRenderer.Clear();
            // PERF S1: was a full-scene FindObjectsOfType<TilesOcclusionVolume> (~10-15 ms in
            // the big room — the call is O(every loaded object), not O(volumes)). The registry
            // returns the IDENTICAL set: every volume enrols in its own Start (the method that
            // also announces it to the occlusion generator), the store was seeded from a real
            // sweep at install, and Collect applies the same activeInHierarchy + hideFlags
            // filters FindObjectsOfType does. See Core/SceneRegistry.cs.
            SceneRegistry.Volumes.Collect(_volumeScratch);
            foreach (TilesOcclusionVolume v in _volumeScratch)
            {
                if (v == null || v.CentralTile == null || v.Renderers == null)
                    continue;
                float tileY = v.CentralTile.transform.position.y;
                // ROUND-4 ROOM IDENTITY (user ruling: "normale Raum-Logik" — the game's own
                // room is the unit): CentralTile.m_ClientTile.m_Tile.m_HexMap is the CMap
                // this volume's own IsVisible() reads .Revealed from — the game's unit of
                // reveal. Every volume of one revealed room carries the same CMap.
                object? mapKey = null;
                string mapLabel = "?";
                try
                {
                    ScenarioRuleLibrary.CMap? map = v.CentralTile.m_ClientTile?.m_Tile?.m_HexMap;
                    if (map != null)
                    {
                        mapKey = map;
                        mapLabel = string.IsNullOrEmpty(map.RoomName)
                            ? map.MapInstanceName : map.RoomName;
                    }
                }
                catch { /* client-tile chain mid-build — renderers stay singleton rooms */ }
                foreach (MeshRenderer vr in v.Renderers)
                {
                    if (vr == null)
                        continue;
                    _floorYByRenderer[vr] = tileY;
                    if (mapKey != null)
                    {
                        _roomMapByRenderer[vr] = mapKey;
                        _roomMapLabelByRenderer[vr] = mapLabel;
                    }
                }
            }

            // LOGICAL ROOM GROUPING (round 4): the keep ships ONE game room as SIX occlusion
            // sub-volumes ('Volume_1..6', all under map tile 'E', same CMap); treating each
            // volume renderer as its own room split the room's floor grid six ways, walls
            // were assigned to one sixth each, and the front wall's own-room coverage read
            // 0.00 although it hid the (whole) room. Registry entries therefore merge per
            // (CMap, quantized anchor height): union XZ bounds, ONE grid, one anchor state.
            // Renderers without a CMap (no volume / chain unbuilt) stay singleton rooms —
            // exactly the old behaviour, and unanchored ones stay fail-safe solid.
            _roomBounds.Clear();
            _roomFloorY.Clear();
            _roomFloorAnchored.Clear();
            _roomLabels.Clear();
            _roomRendererCounts.Clear();
            _keyToRoomScratch.Clear();
            foreach (MeshRenderer r in gen.m_RoomRenderers)
            {
                if (r == null)
                    continue;
                bool anchored = _floorYByRenderer.TryGetValue(r, out float floorY);
                object? key = anchored && _roomMapByRenderer.TryGetValue(r, out object k)
                    ? k : null;
                if (key != null)
                {
                    // Same CMap on a DIFFERENT floor level (terraced rooms) must not share
                    // one sample plane — the anchor height is part of the key (0.5 wu bins).
                    (object, int) groupKey = (key, Mathf.RoundToInt(floorY * 2f));
                    if (_keyToRoomScratch.TryGetValue(groupKey, out int idx))
                    {
                        Bounds merged = _roomBounds[idx];
                        merged.Encapsulate(r.bounds);
                        _roomBounds[idx] = merged;
                        _roomRendererCounts[idx]++;
                        continue;
                    }
                    _keyToRoomScratch[groupKey] = _roomBounds.Count;
                }
                _roomBounds.Add(r.bounds);
                _roomFloorY.Add(anchored ? floorY : float.NaN);
                _roomFloorAnchored.Add(anchored);
                _roomLabels.Add(key != null && _roomMapLabelByRenderer.TryGetValue(r, out string lbl)
                    ? lbl : r.name);
                _roomRendererCounts.Add(1);
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

            // ROOM-REVEAL DIAGNOSTIC (mid-scenario door open → Choreographer
            // .RevealRoomCreateCharacterActors → TilesOcclusionGenerator.UpdateAwaitingVolumes
            // appends the new room's renderers; our Tick sees the count change and rescans
            // immediately): one unmissable line whenever the room registry or its anchor count
            // changes, plus a fresh heartbeat, so the next hardware log PROVES the registry
            // re-anchored on reveal instead of leaving it to inference.
            if (_roomBounds.Count != _lastRoomCensusCount
                || _roomsAnchored != _lastRoomCensusAnchored)
            {
                bool reveal = _lastRoomCensusCount >= 0 && _roomBounds.Count > _lastRoomCensusCount;
                // Grouping census: which logical rooms exist and how many volume renderers
                // each merged ('E'×6 = the keep's six sub-volumes as ONE room — round 4).
                var groups = new System.Text.StringBuilder();
                for (int i = 0; i < _roomLabels.Count && i < 8; i++)
                {
                    if (groups.Length > 0)
                        groups.Append(", ");
                    groups.Append('\'').Append(_roomLabels[i]).Append('\'');
                    if (i < _roomRendererCounts.Count && _roomRendererCounts[i] > 1)
                        groups.Append('×').Append(_roomRendererCounts[i]);
                }
                if (_roomLabels.Count > 8)
                    groups.Append(", …");
                VRLog.Info(Name,
                    $"room registry {(reveal ? "REVEAL re-anchor" : "refresh")}: "
                    + $"{Mathf.Max(_lastRoomCensusCount, 0)}→{_roomBounds.Count} LOGICAL room(s) "
                    + $"from {_builtRoomCount} volume renderer(s), grouped by the game's room "
                    + $"identity (CMap via CentralTile.m_ClientTile.m_Tile.m_HexMap — round 4): "
                    + $"[{groups}]; "
                    + $"{_roomsAnchored}/{_roomBounds.Count} tile-anchored — walls of unanchored "
                    + "rooms are held SOLID (fail-safe) until their volume anchors.");
                _lastRoomCensusCount = _roomBounds.Count;
                _lastRoomCensusAnchored = _roomsAnchored;
                _heartbeatLogged = false; // re-print the full table against the new room set
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

            // Drop segments whose anchor died (their renderers died with them). Attachments may
            // OUTLIVE the anchor (a split piece's asset siblings live in a different subtree),
            // so restore them first — a hidden door whose owner segment vanished would otherwise
            // stay invisible forever (the foliage-orphan lesson, applied to every attachment).
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                if (kv.Key == null)
                {
                    RestoreSegmentFoliage(kv.Value);
                    RestoreSegmentSiblings(kv.Value);
                    RestoreSegmentMounted(kv.Value);
                    RestoreSegmentStacked(kv.Value);
                    RestoreSegmentBody(kv.Value);
                    _deadKeys.Add(kv.Key!); // destroyed Unity object — reference still hashes
                }
            }
            foreach (Component dead in _deadKeys)
                _segments.Remove(dead);

            // STANDING PROPS (user report 2026-08-15, skelet.jpg): open a fresh verdict scope
            // for this rescan. Deliberately HERE — after the room registry has its anchored
            // floor planes (the rule measures a prop's foot against them) and before the first
            // renderer is collected into any segment, so no path can claim a standing prop even
            // once. See WallSegmentFade.Standing.cs.
            BeginStandingPropScope();

            // Adopt new walls / refresh renderer lists, shader-variant info and bounds.
            _claimedRenderers.Clear();
            _censusWallsWithoutFade = 0;
            _unfadeableWallShaders.Clear();
            List<ProceduralWall> cache = ProceduralWall.m_WallCache;
            for (int i = 0; i < cache.Count; i++)
            {
                ProceduralWall wall = cache[i];
                if (wall == null)
                    continue;
                if (_splitAnchors.Contains(wall))
                {
                    RefreshSplitWall(wall);
                    continue;
                }
                if (!_segments.TryGetValue(wall, out Segment? seg))
                {
                    seg = new Segment { Anchor = wall, FromWallCache = true };
                    _segments.Add(wall, seg);
                }
                RefreshSegment(seg);
                foreach (MeshRenderer r in seg.Renderers)
                    _claimedRenderers.Add(r);
            }

            // Doorway registry (recognition only — doorways never fade, user ruling
            // 2026-08-02): the live door props, refreshed before the adoption sweep so
            // FindDoorwayRoot can re-anchor frame/pillar renderers per door.
            _doorRoots.Clear();
            // PERF S1: registry read instead of the second full-scene FindObjectsOfType —
            // identical set, see the volume comment above and Core/SceneRegistry.cs.
            SceneRegistry.DoorProps.Collect(_doorPropScratch);
            foreach (UnityGameEditorDoorProp dp in _doorPropScratch)
            {
                if (dp != null)
                    _doorRoots.Add(dp.transform);
            }
            // GATE COLUMNS (user ruling 2026-08-07): the wall EMBEDDING each doorway fades
            // like any wall — only the arch rect stays solid. Seeded before the adoption/
            // stacked passes so they see the gate's bounds and face domain.
            SeedGateColumns(_doorPropScratch);

            // Second discovery source: ADOPT every other fade-capable renderer in the scene.
            // The user report behind this ("fortgeschritteneres Szenario mit ganz anderen
            // Mauern — dort werden sie nicht mehr ausgeblendet"): advanced tilesets ship wall
            // meshes as map-tile geometry, not as ProceduralWall entities, so the wall cache
            // never listed them — yet their materials run the same WallFade shader family,
            // because that is how the FLAT game fades them. The shader is the game's own
            // definition of "this is a fadeable wall", so it is our discovery key too.
            // PERF S2: the scene sweep AND the per-renderer classification both happened HERE
            // until 2026-08-23 — one FindObjectsOfType<Renderer> plus four full walks of the
            // resulting 8630-entry array, 118 ms on one frame every two seconds. The sweep now
            // runs in BeginRescanCycle (rarely) and the classification in ClassifySlice (spread
            // over frames); this pass and the three below read the finished RendererFact table.
            // Nothing about WHICH renderers they see changed — see the PERF S2 note by the
            // table's declaration for the argument, predicate by predicate.
            AdoptShaderMatchedWalls();

            // WATER FEATURES (user ruling 2026-08-09, brunnen.png — "lass den Brunnen niemals
            // faden"): rebuild the fountain/pond protection rects BEFORE the ground strip and
            // every adoption pass, so no pass can ever see a fountain's basin as fadeable and
            // leave its water plane hanging in mid-air. See WallSegmentFade.Water.cs.
            CollectWaterFeatures();

            RebuildSamples();
            AssociateRooms();
            StripGroundRenderers();
            NeutralizeEngulfingSegments();
            // Second ground pass ON PURPOSE: NeutralizeEngulfingSegments creates fresh
            // per-renderer segments AFTER the first strip, so a ground-level renderer inside a
            // just-split group would otherwise be fade-eligible for one full rescan interval —
            // exactly the "floor vanishes at the wall's foot" class. The pass is idempotent and
            // the table is ~tens of segments, so running it twice is noise.
            StripGroundRenderers();
            // Fort/keep superstructures (WallSegmentFade.Stacked.cs): AFTER ground strip +
            // engulf neutralization (needs the final base AABBs and room grids), BEFORE the
            // mounted pass (which must see the EXTENDED AABBs so torches hanging on the shell
            // attach to the same wall the shell rides).
            CollectStackedShellPieces();
            // PROP UNIT COHESION (user report 2026-08-19, skelet.jpg — "Der Kopf des Skeletts wird
            // immer noch ausgeblendet"): the statue's skull sat in one wall unit's renderer list
            // and its body in another's, and the two walls fade independently, so the statue was
            // decapitated. Regroup every multi-part prop and give each unit ONE owner. HERE on
            // purpose: after every pass that can put a renderer into a segment (so all the claims
            // are in), and BEFORE the sibling and mounted passes, so both of those see the
            // corrected lists — including the mounted pass's ownership table, whose NEAR-MISS
            // census is what reported this defect. See WallSegmentFade.PropUnit.cs.
            EnforcePropUnitCohesion();
            CollectAdoptedSiblings();
            // LAST on purpose: the mounted-dressing rule is geometric (airborne over the room
            // plane + hugging the wall slab), so it needs the FINAL segment table, their room
            // association and their ground-stripped (now shell-extended) AABBs.
            CollectWallMountedProps();
            // MP sync (record 17): refresh every segment's cross-machine wire key — needs the
            // final table and the room labels (part of the key derivation).
            ComputeWireKeys();
            // Gate-lift links (round 12): bind embedding walls to their gate columns.
            LinkGateLifts();
            // ROUND-14 BOUNDS GUARANTEE, deliberately LAST: no gate column may leave a rescan
            // without a decision AABB — a boundless segment is one the coverage decision cannot
            // reach, and an unreachable segment can hold its pieces hidden forever.
            EnsureGateBounds();
            // The standing-prop proof line, after every collection pass has run so its "claims
            // refused" count is the rescan's total (WallSegmentFade.Standing.cs).
            LogStandingPropCensus();
            // The prop-unit proof line, for the same reason and in the same place: emitted after
            // the pass has run, so every number in it is an outcome (WallSegmentFade.PropUnit.cs).
            LogPropUnitCensus();
        }

        /// <summary>A renderer whose AABB TOP reaches no higher than this above its room's floor
        /// plane is GROUND (or base course), never a wall — see <see cref="StripGroundRenderers"/>.
        /// 1 wu ≈ half a hex; the flat game's own foundation band keeps roughly this zone solid.</summary>
        private const float GroundExclusionHeightWU = 1.0f;

        /// <summary>
        /// THE JUNGLE GROUND FIX (second round — the split alone did not do it): this tileset's
        /// ProceduralWall entities carry ~24 fade-capable renderers EACH (734 under 31 walls in
        /// the hardware log), and among them are the room-edge GROUND hexes the wall grows from.
        /// Fading the wall MPB'd those too, and because the jungle meshes reach down the diorama
        /// skirt, the shader's foundation band sits below the map and the held-state discard ate
        /// the floor — near-camera-only (0.02·dist term), hence "hole in VR, floor on the flat
        /// screen". A renderer LYING AT the floor plane cannot possibly hide that floor from a
        /// head above, so it has no business being part of a fade: strip every renderer whose
        /// AABB top is within <see cref="GroundExclusionHeightWU"/> of its room's floor plane
        /// from the segment (clearing our block off it if one is applied), recompute the
        /// segment's AABB from what remains, and drop segments with nothing left.
        /// </summary>
        private void StripGroundRenderers()
        {
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                Segment seg = kv.Value;
                if (!seg.HasBounds || seg.RoomIndex < 0 || seg.RoomIndex >= _roomFloorY.Count)
                    continue;
                float ceiling = _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU;
                bool changed = false;
                for (int i = seg.Renderers.Count - 1; i >= 0; i--)
                {
                    MeshRenderer r = seg.Renderers[i];
                    // WATER FEATURE (user ruling 2026-08-09, brunnen.png): a fountain's basin
                    // stands just ABOVE the ground band — that is precisely why the ground
                    // strip missed it and it faded with 'Wall 1' while its water plane, which
                    // has no fade channel at all, stayed hanging in mid-air. Water-protected
                    // pieces leave the segment on the same path as the ground band, so they
                    // are held solid with the same machinery (block cleared, AABB rebuilt).
                    if (r == null || (r.bounds.max.y > ceiling && !IsWaterProtected(r.bounds)))
                        continue;
                    if (seg.HasBlock)
                        r.SetPropertyBlock(null); // it was mid-fade — return it to solid NOW
                    seg.Renderers.RemoveAt(i);
                    changed = true;
                }
                // Ground-level foliage (grass tufts ON the floor) stays visible always — only
                // wall-dressing foliage rides the fade. Restore anything already touched.
                for (int i = seg.Foliage.Count - 1; i >= 0; i--)
                {
                    MeshRenderer f = seg.Foliage[i];
                    if (f == null || (f.bounds.max.y > ceiling && !IsWaterProtected(f.bounds)))
                        continue;
                    if (seg.FoliageState != 0)
                        RestoreFoliageRenderer(f);
                    seg.Foliage.RemoveAt(i);
                }
                // BODY ground rule (round 8 — REVERT of the round-7 spanning-course rule,
                // which froze this keep solid: ALL 31 masonry courses span from the ground
                // band upward, so "spanning stays solid" classified the entire visible wall
                // as foundation and only the top assets flickered). Only courses ENTIRELY
                // inside the band stay solid; spanning courses hide with the wall —
                // visibility beats foundation on this enabled-only fallback, and foundation
                // preservation is the NATIVE path's job now (toggle-capable masonry fades
                // through its own shader incl. the world-Y gradient).
                for (int i = seg.Body.Count - 1; i >= 0; i--)
                {
                    Renderer br = seg.Body[i].Renderer;
                    if (br == null || (br.bounds.max.y > ceiling && !IsWaterProtected(br.bounds)))
                        continue;
                    if (seg.BodyState != 0)
                        RestoreProp(seg.Body[i]);
                    seg.Body.RemoveAt(i);
                    changed = true;
                }
                if (!changed)
                    continue;
                if (seg.Renderers.Count == 0 && seg.Body.Count == 0 && !seg.IsGateColumn)
                {
                    // (gate columns legitimately survive empty — round-13 lifecycle rule)
                    // Segment leaves the table — free ALL its attachments (bushes, doors, dressing).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreSegmentMounted(seg);
                    RestoreSegmentStacked(seg);
                    RestoreSegmentBody(seg);
                    _deadKeys.Add(kv.Key);
                    continue;
                }
                // ROUND 14: a GATE COLUMN's decision AABB is its ARCH SEED (plus the stacked /
                // pillar extensions), never a renderer union — it legitimately owns zero wall
                // renderers, so rebuilding its bounds here would leave it BOUNDLESS, i.e.
                // unevaluated, i.e. latched in whatever fade state it held (the "masonry above
                // the arch never comes back" report). Ground-stripping its adopted pillars is
                // still correct; its bounds are not this pass's to rebuild.
                if (seg.IsGateColumn)
                    continue;
                // Recompute the AABB from the surviving (actual wall) renderers + body.
                seg.HasBounds = false;
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r == null)
                        continue;
                    if (!seg.HasBounds)
                    {
                        seg.Bounds = r.bounds;
                        seg.HasBounds = true;
                    }
                    else
                    {
                        seg.Bounds.Encapsulate(r.bounds);
                    }
                }
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer == null)
                        continue;
                    if (!seg.HasBounds)
                    {
                        seg.Bounds = p.Renderer.bounds;
                        seg.HasBounds = true;
                    }
                    else
                    {
                        seg.Bounds.Encapsulate(p.Renderer.bounds);
                    }
                }
                if (seg.HasBounds)
                {
                    float thickness = Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z);
                    seg.BlockEps = Mathf.Clamp(0.5f * thickness, BlockEpsMinWorld, BlockEpsMaxWorld);
                }
            }
            foreach (Component dead in _deadKeys)
                _segments.Remove(dead);
        }

        /// <summary>
        /// THE JUNGLE-TILESET GROUND BUG (post-association pass, both discovery sources): a wall
        /// whose geometry RINGS its room has an AABB that CONTAINS the room's own floor samples,
        /// so BlockedFraction reads ~100% from every head position — permanent fade — and because
        /// those meshes reach down the diorama skirt, the shader's foundation band sits below the
        /// map and the held-state discard eats the GROUND at the wall's foot. Near-camera-only
        /// (the HIGH variant's 0.02·dist term), which is why the flat mirror still showed a floor
        /// while VR showed holes.
        ///
        /// The trigger is CONTAINMENT, deliberately not AABB fatness: an L-shaped stone corner
        /// run also has a fat AABB but contains only its corner quadrant (≈25% of the grid) —
        /// those keep today's whole-wall behaviour. A segment whose AABB XZ-contains ≥
        /// <see cref="EngulfSampleFraction"/> of its own room's samples cannot make a meaningful
        /// occlusion decision as ONE unit: multi-renderer segments are SPLIT per renderer (each
        /// piece is a proper slab deciding for itself; membership persists via _splitAnchors),
        /// and an unsplittable single-renderer segment is held SOLID (vanilla look — strictly
        /// better than permanently missing ground) and flagged !ENGULF in the diag.
        /// </summary>
        private const float EngulfSampleFraction = 0.4f;

        private void NeutralizeEngulfingSegments()
        {
            _fatScratch.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                Segment seg = kv.Value;
                if (!seg.HasBounds || seg.RoomIndex < 0)
                    continue;
                if (seg.DoorRoot != null)
                    continue; // doorway: held permanently solid anyway — and a split would
                              // strip the DoorRoot off the pieces, making the archway fadeable
                if (seg.IsGateColumn)
                    continue; // gate columns are lifecycle-protected (round 13) — an engulf
                              // split would destroy the segment the arch/lift depend on
                if (Mathf.Min(seg.Bounds.size.x, seg.Bounds.size.z) <= GroupSlabMaxHorizontal)
                    continue; // thin slab — cannot contain a room
                if (InsideOwnRoomFraction(seg) < EngulfSampleFraction)
                    continue; // fat but bordering (L-corner) — legitimate whole-wall behaviour
                if (seg.Renderers.Count <= 1)
                {
                    // Unsplittable (single mesh — including an already-split piece that is
                    // itself room-sized): undecidable as one unit — hold solid.
                    seg.Engulfing = true;
                    continue;
                }
                _fatScratch.Add(kv);
            }

            foreach (KeyValuePair<Component, Segment> engulfing in _fatScratch)
            {
                Segment group = engulfing.Value;
                _splitAnchors.Add(engulfing.Key);
                _segments.Remove(engulfing.Key);
                RestoreSegmentFoliage(group); // pieces re-adopt the bushes on the next rescan
                RestoreSegmentSiblings(group); // ditto for asset siblings (doors/trim)
                RestoreSegmentMounted(group);  // …and for the wall-mounted dressing (torches)
                RestoreSegmentStacked(group);  // …and for stacked shell pieces (keep stories)
                RestoreSegmentBody(group);     // …and for plain wall-body meshes (masonry)
                if (group.HasBlock)
                {
                    foreach (MeshRenderer r in group.Renderers)
                    {
                        if (r != null)
                            r.SetPropertyBlock(null);
                    }
                }
                foreach (MeshRenderer r in group.Renderers)
                {
                    if (r == null)
                        continue;
                    if (!_segments.TryGetValue(r, out Segment? sub))
                    {
                        sub = new Segment { Anchor = r, FromWallCache = group.FromWallCache };
                        _segments.Add(r, sub);
                    }
                    BeginRefresh(sub);
                    if (CollectWallFadeInfo(r, sub))
                    {
                        sub.Renderers.Add(r);
                        sub.Bounds = r.bounds;
                        sub.HasBounds = true;
                    }
                    FinishRefresh(sub);
                    if (group.FromWallCache)
                        _claimedRenderers.Add(r);
                }
            }

            // The freshly split pieces need a room before the next decision tick.
            if (_fatScratch.Count > 0)
                AssociateRooms();
        }

        /// <summary>Fraction of the segment's OWN room's floor samples that lie inside the
        /// segment AABB's XZ footprint (Y ignored — wall AABBs span the whole column).</summary>
        private float InsideOwnRoomFraction(Segment seg) =>
            InsideRoomFraction(seg.Bounds, seg.RoomIndex);

        /// <summary>Same containment test for an arbitrary AABB — the stacked-shell pass
        /// pre-checks a WOULD-BE extended wall AABB against the room grid before adopting a
        /// piece (a shell ringing the room must never join, or coverage reads 100% forever).</summary>
        private float InsideRoomFraction(Bounds b, int room)
        {
            if (room < 0 || room >= _roomSampleCount.Count)
                return 0f;
            int total = _roomSampleCount[room];
            if (total <= 0)
                return 0f;
            int start = _roomSampleStart[room];
            int end = Mathf.Min(start + total, _allSamples.Count);
            int inside = 0;
            for (int i = start; i < end; i++)
            {
                Vector3 s = _allSamples[i];
                if (s.x >= b.min.x && s.x <= b.max.x && s.z >= b.min.z && s.z <= b.max.z)
                    inside++;
            }
            return inside / (float)total;
        }

        /// <summary>
        /// Sweep all live MeshRenderers for wall-fade-capable materials that no wall-cache
        /// segment claimed, and group them into segments: by the nearest
        /// <see cref="ProceduralTileObserver"/> ancestor (the generation unit of tile-borne wall
        /// geometry — room-chunk granularity, same scale as a ProceduralWall run), else by the
        /// renderer's parent. Runs inside the 2s rescan; the shader verdict is cached per Shader
        /// so the steady-state cost is one dictionary probe per renderer.
        /// </summary>
        private void AdoptShaderMatchedWalls()
        {
            // Reset adopted segments for re-fill; keep their smoothing/fade state (keyed by
            // anchor, so a stable group keeps its EMA and dwell across rescans).
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                // Gate columns are seeded by SeedGateColumns (already refreshed this rescan)
                // and own no wall renderers — the shader sweep must not reset them.
                if (!kv.Value.FromWallCache && !kv.Value.IsGateColumn)
                    BeginRefresh(kv.Value);
            }

            _censusFadeRenderers = 0;
            _censusAdopted = 0;
            // PERF S2: `_factWallFade` holds exactly the indices the old loop's guard
            // (`any is MeshRenderer && RendererUsesWallFade(it)`) let through, in snapshot
            // order — which is load-bearing here, because the FIRST fade renderer of a group
            // is the one that seeds that group's anchor and bounds. Renderers destroyed since
            // the census are dropped by the null check below, exactly as the old `r == null`
            // arm did.
            for (int fi = 0; fi < _factWallFade.Count; fi++)
            {
                MeshRenderer? r = _facts[_factWallFade[fi]].Mesh;
                if (r == null)
                    continue;
                _censusFadeRenderers++;
                if (_claimedRenderers.Contains(r))
                    continue;
                // A fade renderer under a ProceduralWall belongs to that wall's segment; it can
                // only get here mid-stream (Apparance still generating) — the next rescan's
                // RefreshSegment picks it up, and adopting it now would double-track it.
                if (r.GetComponentInParent<ProceduralWall>() != null)
                    continue;
                Component? anchor = r.GetComponentInParent<ProceduralTileObserver>();
                if (anchor == null)
                    anchor = r.transform.parent != null ? r.transform.parent : r.transform;
                // DOORWAY override (user ruling 2026-08-02: doorways NEVER fade): a fade
                // renderer hugging a door prop is that DOORWAY's frame/pillar — anchor it on
                // the door root so every renderer of one archway lands in ONE per-door segment
                // the decision loop holds permanently SOLID. This outranks both the
                // tile/parent grouping (the round-1 'L :' layer container that mixed two
                // doorways into one look-at segment — which would fade as a wall) and the
                // split routing (a doorway is archway-sized, never a room-engulfing slab).
                Transform? doorRoot = FindDoorwayRoot(r);
                if (doorRoot != null)
                {
                    // ROUND-11/12 SPLIT: the permanently-solid DOORWAY segment holds ONLY
                    // the arch. A door-hugging fade renderer OUTSIDE the arch — the
                    // flanking PILLARS ("Säulen die nicht zum Rechteck gehören") and the
                    // torch fires on them — joins the GATE COLUMN's own renderer set
                    // instead and fades NATIVELY with the gate face (round 12: the
                    // round-11 "leave unclaimed" left them solid forever). Sliver-skipped
                    // doors keep the old full-radius grouping.
                    if (HasGateColumnFor(doorRoot) && !IsArchProtected(r.bounds, r.name))
                    {
                        UnityGameEditorDoorProp? gdp =
                            doorRoot.GetComponent<UnityGameEditorDoorProp>();
                        if (gdp != null && _segments.TryGetValue(gdp, out Segment? gseg)
                            && gseg.IsGateColumn && CollectWallFadeInfo(r, gseg))
                        {
                            gseg.Renderers.Add(r);
                            Bounds gb = gseg.Bounds;
                            gb.Encapsulate(r.bounds);
                            gseg.Bounds = gb;
                        }
                        continue;
                    }
                    anchor = doorRoot;
                }
                // A group that proved too fat to be a slab is tracked per renderer instead
                // (see _splitAnchors) — route straight to the per-renderer segment so its
                // smoothing state survives every rescan.
                else if (_splitAnchors.Contains(anchor))
                    anchor = r;
                if (!_segments.TryGetValue(anchor, out Segment? seg))
                {
                    seg = new Segment { Anchor = anchor, FromWallCache = false };
                    _segments.Add(anchor, seg);
                    BeginRefresh(seg);
                }
                seg.DoorRoot = doorRoot; // re-stamped every rescan (null for non-doorways)
                if (CollectWallFadeInfo(r, seg))
                {
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
                    _censusAdopted++;
                }
            }
            _censusClaimed = _censusFadeRenderers - _censusAdopted;

            // Finalize adopted segments: stale-block cleanup + thickness epsilon; drop the empty
            // ones (their renderers died or stopped matching). Groups whose AABB engulfs their
            // own room are handled by NeutralizeEngulfingSegments AFTER room association — the
            // containment test needs the room's samples, and plain AABB fatness is not enough
            // (an L-shaped corner run is fat too and must keep whole-wall behaviour).
            _deadKeys.Clear();
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                Segment seg = kv.Value;
                if (seg.IsGateColumn)
                {
                    // Gate columns may legitimately own zero renderers (kept alive), but
                    // the ones that adopted pillar/torch renderers this sweep (round 12)
                    // still need the leaver cleanup + epsilon derivation.
                    FinishRefresh(seg);
                    continue;
                }
                if (seg.FromWallCache)
                    continue;
                FinishRefresh(seg);
                if (seg.Renderers.Count == 0)
                {
                    // Adopted group dissolved (renderers died / stopped matching) — its hidden
                    // attachments must not outlive it (restore-everywhere discipline).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreSegmentMounted(seg);
                    RestoreSegmentStacked(seg);
                    _deadKeys.Add(kv.Key);
                }
            }
            foreach (Component dead in _deadKeys)
                _segments.Remove(dead);
        }

        /// <summary>
        /// Per-renderer tracking for a cache wall whose combined AABB proved too fat (see the
        /// split in Rescan): every fade-capable renderer under the wall gets its own segment,
        /// keyed by the renderer, and is claimed so the adoption sweep leaves it alone. Dead
        /// renderers fall out via the dead-key sweep (their key is the renderer itself).
        /// </summary>
        private readonly List<Segment> _splitPieceScratch = new();

        private void RefreshSplitWall(ProceduralWall wall)
        {
            _splitPieceScratch.Clear();
            MeshRenderer[] all = wall.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null || !RendererUsesWallFade(r))
                    continue;
                if (!_segments.TryGetValue(r, out Segment? sub))
                {
                    sub = new Segment { Anchor = r, FromWallCache = true };
                    _segments.Add(r, sub);
                }
                BeginRefresh(sub);
                if (CollectWallFadeInfo(r, sub))
                {
                    sub.Renderers.Add(r);
                    sub.Bounds = r.bounds;
                    sub.HasBounds = true;
                }
                _claimedRenderers.Add(r);
                _splitPieceScratch.Add(sub);
            }

            // The wall's foliage dressing rides the NEAREST piece's fade (XZ distance between
            // AABB centers): a bush hangs on the piece it grows from, and that is the piece
            // whose fade makes it a view-blocking leftover.
            foreach (MeshRenderer r in all)
            {
                // Same standing-prop exclusion as the unsplit path, and the same FIGURE arm
                // only: a floor-standing figure prop is never a wall's foliage dressing either,
                // while a bush standing on the ground still is (WallSegmentFade.Standing.cs).
                if (r == null || !RendererUsesFoliage(r) || IsStandingFigureOnlyProp(r))
                    continue;
                Segment? best = null;
                float bestSq = float.PositiveInfinity;
                Vector3 c = r.bounds.center;
                foreach (Segment piece in _splitPieceScratch)
                {
                    if (!piece.HasBounds)
                        continue;
                    float dx = piece.Bounds.center.x - c.x;
                    float dz = piece.Bounds.center.z - c.z;
                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        best = piece;
                    }
                }
                best?.Foliage.Add(r);
            }

            foreach (Segment sub in _splitPieceScratch)
                FinishRefresh(sub);
            _splitPieceScratch.Clear();
        }

        /// <summary>
        /// ASSET-ROOT RULE (Torbogen ruling, req: whole asset hides): for a fade-capable
        /// renderer of an ADOPTED group, the asset root is the NEAREST ancestor whose subtree
        /// also contains at least one non-fade MeshRenderer, provided that subtree stays under
        /// <see cref="MaxAssetRootRenderers"/> renderers and within
        /// <see cref="MaxAssetRootDepth"/> levels. Evidence (hardware log + torbogen.png): the
        /// adopted group 'L : (guid)' is an Apparance layer root whose fade renderers are
        /// CR_ST_Door_01_Frame_Thin + EN_CR_Pillar_Thin — the doorway's wooden wings and arch
        /// trim are NON-fade siblings under the same per-doorway subtree (the door prop is an
        /// Apparance ProceduralProp: frame, pillars, wings and trim are generated into one
        /// subtree; Choreographer.OpenDoor animates that same object). Walking up from the
        /// frame finds that per-doorway node BEFORE the renderer-count cap trips on the layer
        /// root. FAILURE MODES (accepted, fail-open): (a) frames parented flat under a big
        /// container → cap trips → nothing attached, today's remnant stays; (b) an asset root
        /// that also parents small unrelated dressing hides it with the doorway — bounded by
        /// the count cap and the per-renderer ground/actor/tile exclusions below.
        /// </summary>
        private Transform? FindAssetRoot(MeshRenderer fadeRenderer)
        {
            Transform? node = fadeRenderer.transform.parent;
            for (int depth = 0; node != null && depth < MaxAssetRootDepth; depth++)
            {
                node.GetComponentsInChildren(includeInactive: false, _subtreeScratch);
                if (_subtreeScratch.Count > MaxAssetRootRenderers)
                    return null; // container scale ('L :' layer/section root) — stop, attach nothing
                foreach (MeshRenderer c in _subtreeScratch)
                {
                    if (c != null && !RendererUsesWallFade(c))
                        return node; // nearest ancestor that mixes fade + non-fade = the asset
                }
                node = node.parent;
            }
            return null;
        }

        /// <summary>
        /// The door prop whose position the renderer's AABB hugs (XZ gap ≤
        /// <see cref="DoorwayLinkMaxXZ"/>), nearest wins — the linkage from a fade renderer to
        /// its OWNING door. Deliberately spatial, not hierarchical: the hardware log proved the
        /// frames are parented flat under a big 'L :' Apparance section container (the ancestor
        /// walk's documented fail-open), while the door prop is a SIBLING subtree — but the door
        /// object knows exactly where it stands, and archway frames exist only around doors.
        /// </summary>
        private Transform? FindDoorwayRoot(MeshRenderer r)
        {
            if (_doorRoots.Count == 0)
                return null;
            Bounds b = r.bounds;
            Transform? best = null;
            float bestSq = DoorwayLinkMaxXZ * DoorwayLinkMaxXZ;
            foreach (Transform door in _doorRoots)
            {
                if (door == null)
                    continue;
                Vector3 p = door.position;
                float gx = Mathf.Max(0f, Mathf.Max(b.min.x - p.x, p.x - b.max.x));
                float gz = Mathf.Max(0f, Mathf.Max(b.min.z - p.z, p.z - b.max.z));
                float sq = gx * gx + gz * gz;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = door;
                }
            }
            return best;
        }

        /// <summary>
        /// ASSET-COMPLETE FADE collection (adopted groups only — cache walls keep their
        /// deliberate "props/doors under the same entity are never touched" contract, their
        /// foliage path already covers the dressing): re-attach, per rescan, every non-fade
        /// renderer under each fade renderer's asset root (<see cref="FindAssetRoot"/>) so the
        /// WHOLE mixed asset hides with the segment. Exclusions, in order: renderers the game
        /// itself disabled (unless WE hid them), fade-capable renderers (tracked as segments),
        /// already-owned siblings (one owner per renderer), GROUND-ish renderers (AABB top
        /// within the ground band of the room's anchored floor plane — floor never fades, in
        /// any attachment type), and renderers under live game logic (ProceduralWall = cache
        /// territory, ActorBehaviour = characters, TileBehaviour = worldspace tile UI/logic).
        /// Runs after room association + ground strip because the ground exclusion needs the
        /// room plane; segments without a trusted plane attach nothing (they are fail-safe
        /// solid anyway). Leavers are restored exactly like foliage leavers.
        /// </summary>
        private void CollectAdoptedSiblings()
        {
            _siblingOwned.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (seg.FromWallCache)
                    continue;
                seg.PrevSiblings.Clear();
                seg.PrevSiblings.AddRange(seg.Siblings);
                seg.Siblings.Clear();
                if (seg.DoorRoot != null)
                {
                    // DOORWAY segment (user ruling 2026-08-02: never fades — held
                    // permanently solid): needs no sibling attachments. Restore anything a
                    // previous owner state still hides (ownership handover; nothing may
                    // stay hidden without an owner).
                    if (seg.SiblingState != 0)
                        RestoreSegmentSiblings(seg);
                    seg.PrevSiblings.Clear();
                    continue;
                }
                if (RoomDecisionValid(seg.RoomIndex))
                {
                    float ceiling = _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU;
                    foreach (MeshRenderer r in seg.Renderers)
                    {
                        if (r == null)
                            continue;
                        Transform? root = FindAssetRoot(r);
                        if (root == null)
                            continue;
                        root.GetComponentsInChildren(includeInactive: false, _subtreeScratch);
                        foreach (MeshRenderer c in _subtreeScratch)
                        {
                            if (c == null || _siblingOwned.Contains(c))
                                continue;
                            // A renderer the GAME disabled is not ours to manage — except
                            // one WE hid last rescan (still held faded): dropping it now
                            // would re-enable + re-hide it in a one-frame flash.
                            if (!c.enabled
                                && !(seg.SiblingState == 2 && seg.PrevSiblings.Contains(c)))
                                continue;
                            if (RendererUsesWallFade(c))
                                continue;
                            if (c.bounds.max.y <= ceiling)
                                continue; // floor-ish — never rides a fade, in any form
                            if (IsWaterProtected(c.bounds))
                                continue; // fountain/pond (user ruling 2026-08-09) — never fades
                            if (c.GetComponentInParent<ProceduralWall>() != null
                                || c.GetComponentInParent<ActorBehaviour>() != null
                                || c.GetComponentInParent<TileBehaviour>() != null)
                                continue;
                            _siblingOwned.Add(c);
                            seg.Siblings.Add(c);
                        }
                    }
                }
                if (seg.SiblingState != 0)
                {
                    // Restore leavers NOW — nothing else ever points at them again (round 15:
                    // including their dissolve channel, so no authored material stays swapped
                    // once nothing points at the renderer).
                    foreach (MeshRenderer prev in seg.PrevSiblings)
                    {
                        if (prev != null && !seg.Siblings.Contains(prev))
                            RestoreSiblingProp(seg, prev);
                    }
                    if (seg.Siblings.Count == 0)
                        seg.SiblingState = 0;
                }
                seg.PrevSiblings.Clear();
            }
        }

        /// <summary>
        /// GROUND FORENSICS (green-scenario "see-through floor", round 3): one line per room at
        /// heartbeat time naming every renderer whose AABB stands over the ROOM CENTER — the
        /// floor candidates (AABB top near the floor plane), what lies BELOW (the "inner walls"
        /// the user sees through the holes) and what hangs ABOVE. Each entry carries shader,
        /// material render queue, static-batch flag and our-MPB flag, so the next log says
        /// WHICH mesh the missing floor is, WHAT shader it runs and WHO touched it — instead of
        /// a fourth guessed fix.
        /// </summary>
        private void LogFloorColumnCensus()
        {
            if (_roomBounds.Count == 0)
                return;
            // includeInactive ON (fehlender_boden2.png round 2): the first census could not
            // tell "no floor renderer EXISTS" from "a floor renderer exists but something
            // disabled it" — the exact fork between "Apparance never synthesized the room"
            // (the confirmed reveal bug: ApparanceEntity.CheckEntity destroys hidden rooms'
            // native entities and re-synthesis runs against the parked Camera.main viewpoint)
            // and "our fade / the game disabled it". Disabled entries now carry WHO: the
            // renderer's own enabled flag and the first inactive ancestor by name.
            MeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>(includeInactive: true);
            var sb = new System.Text.StringBuilder();
            int rooms = Mathf.Min(_roomBounds.Count, 8);
            for (int r = 0; r < rooms; r++)
            {
                Vector3 center = _roomBounds[r].center;
                float floorY = r < _roomFloorY.Count ? _roomFloorY[r] : 0f;
                sb.Length = 0;
                sb.Append("FLOOR CENSUS room ").Append(r);
                if (r < _roomLabels.Count)
                    sb.Append(" '").Append(_roomLabels[r]).Append('\'');
                if (r < _roomRendererCounts.Count && _roomRendererCounts[r] > 1)
                    sb.Append('x').Append(_roomRendererCounts[r]);
                sb.Append(" center(").Append(center.x.ToString("F1")).Append(',')
                  .Append(center.z.ToString("F1")).Append(") floorY ").Append(floorY.ToString("F2"))
                  .Append(':');
                int listed = 0;
                foreach (MeshRenderer mr in all)
                {
                    if (mr == null)
                        continue;
                    Bounds b = mr.bounds;
                    if (center.x < b.min.x || center.x > b.max.x
                        || center.z < b.min.z || center.z > b.max.z)
                        continue;
                    if (listed++ >= 10) { sb.Append(" …"); break; }
                    string zone = b.max.y < floorY - 0.5f ? "BELOW"
                        : b.max.y <= floorY + 1.5f ? "FLOOR"
                        : "ABOVE";
                    Material? m = mr.sharedMaterial;
                    sb.Append(" [").Append(zone).Append("] '").Append(mr.name)
                      .Append("' y[").Append(b.min.y.ToString("F1")).Append("..")
                      .Append(b.max.y.ToString("F1")).Append("] sh='")
                      .Append(m != null && m.shader != null ? m.shader.name : "?")
                      .Append("' q").Append(m != null ? m.renderQueue : -1)
                      .Append(mr.isPartOfStaticBatch ? " BATCHED" : "")
                      .Append(mr.HasPropertyBlock() ? " OUR-MPB" : "");
                    // WHO turned it off: renderer.enabled = a component write (our fade only
                    // ever touches foliage/siblings this way); inactive hierarchy = a
                    // SetActive by name of the first inactive ancestor (ProceduralMapTile
                    // .ShowContent visibility toggles read as 'Generated Content'/'Preview').
                    if (!mr.enabled)
                        sb.Append(" OFF");
                    if (!mr.gameObject.activeInHierarchy)
                    {
                        Transform? t = mr.transform;
                        while (t != null && t.gameObject.activeSelf)
                            t = t.parent;
                        sb.Append(" INACTIVE:'")
                          .Append(t != null ? t.name : "?").Append('\'');
                    }
                    sb.Append(';');
                }
                if (listed == 0)
                    sb.Append(" (no renderer over the room center at all — nothing exists, "
                        + "not even disabled: the geometry was never generated)");
                VRLog.Info(Name, sb.ToString());
            }
            LogMapTileCensus(sb);
            LogApparanceViewpoint(sb);
        }

        /// <summary>
        /// One line per <see cref="ProceduralMapTile"/>: the game-side visibility state plus
        /// the Apparance generation state — visibility (Preview vs All), whether 'Generated
        /// Content' exists, how many of its children are active, whether the 'Preview' child
        /// (the scattered hex islands of an unrevealed room) is still showing, and the
        /// entity's IsPopulated/native-handle/IsBusy status plus the tile's distance from the
        /// synthesis viewpoint. Decides in one log whether a missing room floor is a
        /// VISIBILITY failure (Preview stuck on), a SYNTHESIS failure (visibility All,
        /// generation root empty — the parked-viewpoint reveal bug), or a DETAIL failure
        /// (built far from the viewpoint, coarse content only — the round-3 suspect).
        ///
        /// Round 3 added PER-CHILD forensics: the 'children 1/2 active, 229 renderer(s)'
        /// summary could not say WHICH child of 'Generated Content' holds the renderers —
        /// so a follow-up line per direct child names it, its activeSelf/activeInHierarchy
        /// state, its renderer counts (total / active-in-hierarchy / actually drawing), its
        /// combined bounds, and a sample of renderer names+y-bands+state. That splits
        /// "content exists but was left inactive" from "only preview-grade content was ever
        /// synthesized" without another blind hardware round.
        /// </summary>
        private void LogMapTileCensus(System.Text.StringBuilder sb)
        {
            // Where the engine is generating detail around RIGHT NOW — mirror of
            // ApparanceEngine.UpdateEngine's own source selection (focus override first,
            // Camera.main otherwise). Distances on the MAPTILE lines are measured to this.
            Vector3? viewpoint = null;
            try
            {
                ApparanceEngine? engineNow = ApparanceEngine.Instance;
                if (engineNow != null && engineNow.EnableDetailFocus && engineNow.DetailFocus != null)
                    viewpoint = engineNow.DetailFocus.transform.position;
                else if (Camera.main != null)
                    viewpoint = Camera.main.transform.position;
            }
            catch { /* engine mid-teardown — distances become 'n/a' */ }

            ProceduralMapTile[] tiles =
                UnityEngine.Object.FindObjectsOfType<ProceduralMapTile>(includeInactive: true);
            foreach (ProceduralMapTile tile in tiles)
            {
                if (tile == null)
                    continue;
                sb.Length = 0;
                sb.Append("MAPTILE '").Append(tile.name)
                  .Append("' pos(").Append(tile.transform.position.x.ToString("F1")).Append(',')
                  .Append(tile.transform.position.z.ToString("F1"))
                  .Append(") vis=").Append(tile.visibility)
                  .Append(tile.gameObject.activeInHierarchy ? "" : " INACTIVE");
                if (viewpoint.HasValue)
                    sb.Append(" focusDist=")
                      .Append(Vector3.Distance(viewpoint.Value, tile.transform.position).ToString("F1"));
                Transform? gen = FindChildByName(tile.transform, "Generated Content");
                if (gen == null)
                {
                    sb.Append(" genContent=NONE (never generated)");
                }
                else
                {
                    int children = gen.childCount, active = 0, renderers = 0;
                    bool previewActive = false;
                    for (int i = 0; i < children; i++)
                    {
                        Transform c = gen.GetChild(i);
                        if (c.gameObject.activeSelf)
                        {
                            active++;
                            if (c.name == "Preview")
                                previewActive = true;
                        }
                    }
                    _subtreeScratch.Clear();
                    gen.GetComponentsInChildren(includeInactive: true, _subtreeScratch);
                    renderers = _subtreeScratch.Count;
                    sb.Append(" genContent=").Append(gen.gameObject.activeSelf ? "on" : "OFF")
                      .Append(" children ").Append(active).Append('/').Append(children)
                      .Append(" active, ").Append(renderers).Append(" renderer(s)")
                      .Append(previewActive ? ", PREVIEW STILL ON" : "");
                }
                try
                {
                    ApparanceEntity? entity = tile.GetComponent<ApparanceEntity>();
                    if (entity != null)
                        sb.Append(" entity populated=").Append(entity.IsPopulated)
                          .Append(" handle=").Append(entity.m_EntityHandle != 0 ? "built" : "NONE")
                          .Append(" busy=").Append(entity.IsBusy)
                          .Append(" dynDetail=").Append(entity.DynamicDetail);
                }
                catch { sb.Append(" entity=?"); }
                VRLog.Info(Name, sb.ToString());

                if (gen != null)
                    LogGeneratedContentChildren(sb, tile.name, gen);
            }
        }

        /// <summary>Renderer sample cap per 'Generated Content' child line — enough names to
        /// recognize the asset family (Unseen preview hexes vs full CR floor/wall pieces)
        /// without flooding a heartbeat.</summary>
        private const int MaxChildRendererSamples = 8;

        /// <summary>
        /// The per-child forensics behind a MAPTILE line: for each DIRECT child of the
        /// tile's 'Generated Content' (the containers <c>ProceduralMapTile.ShowContent</c>
        /// toggles — 'Preview' vs the synthesized full-content groups), log activation,
        /// renderer census and bounds. Bounds are the union of renderer AABBs
        /// (world-space); inactive renderers still report usable transform-derived bounds,
        /// which is exactly what we need to see WHERE never-shown content would render.
        /// </summary>
        private void LogGeneratedContentChildren(
            System.Text.StringBuilder sb, string tileName, Transform gen)
        {
            for (int i = 0; i < gen.childCount; i++)
            {
                Transform child = gen.GetChild(i);
                _subtreeScratch.Clear();
                child.GetComponentsInChildren(includeInactive: true, _subtreeScratch);

                int total = _subtreeScratch.Count, activeInHier = 0, drawing = 0;
                Bounds union = default;
                bool haveBounds = false;
                foreach (MeshRenderer mr in _subtreeScratch)
                {
                    if (mr == null)
                        continue;
                    bool act = mr.gameObject.activeInHierarchy;
                    if (act)
                    {
                        activeInHier++;
                        if (mr.enabled)
                            drawing++;
                    }
                    Bounds b = mr.bounds;
                    if (!haveBounds) { union = b; haveBounds = true; }
                    else union.Encapsulate(b);
                }

                sb.Length = 0;
                sb.Append("MAPTILE '").Append(tileName)
                  .Append("' child[").Append(i).Append("] '").Append(child.name)
                  .Append("' self=").Append(child.gameObject.activeSelf ? "on" : "OFF")
                  .Append(" hier=").Append(child.gameObject.activeInHierarchy ? "on" : "OFF")
                  .Append(" renderers ").Append(total)
                  .Append(" (").Append(activeInHier).Append(" activeInHierarchy, ")
                  .Append(drawing).Append(" drawing)");
                if (haveBounds)
                    sb.Append(" bounds c(").Append(union.center.x.ToString("F1")).Append(',')
                      .Append(union.center.y.ToString("F1")).Append(',')
                      .Append(union.center.z.ToString("F1"))
                      .Append(") s(").Append(union.size.x.ToString("F1")).Append(',')
                      .Append(union.size.y.ToString("F1")).Append(',')
                      .Append(union.size.z.ToString("F1")).Append(')');

                int listed = 0;
                foreach (MeshRenderer mr in _subtreeScratch)
                {
                    if (mr == null)
                        continue;
                    if (listed >= MaxChildRendererSamples) { sb.Append(" …"); break; }
                    Bounds b = mr.bounds;
                    bool disabled = mr.gameObject.activeInHierarchy && !mr.enabled;
                    sb.Append(listed == 0 ? "; sample: '" : " '").Append(mr.name)
                      .Append("'[").Append(mr.gameObject.activeInHierarchy
                          ? (mr.enabled ? "on" : "disabled") : "off")
                      .Append(" y").Append(b.min.y.ToString("F1")).Append("..")
                      .Append(b.max.y.ToString("F1"));
                    // ROUND 4: a sampled DISABLED renderer names its game MaterialLoader
                    // state (ml=no-loader / never-started / loading a/b / null-result a/b /
                    // done-stuck / done) — same classifier the MaterialLoaderHeal watchdog
                    // acts on, so the next hardware log proves WHICH stranding mechanism
                    // left the revealed room's renderers active-but-disabled.
                    if (disabled)
                    {
                        try { sb.Append(" ml=").Append(MaterialLoaderHeal.DescribeForRenderer(mr)); }
                        catch { sb.Append(" ml=?"); }
                    }
                    sb.Append(']');
                    listed++;
                }
                VRLog.Info(Name, sb.ToString());
            }
        }

        /// <summary>
        /// The Apparance synthesis viewpoint line: which position the engine is generating
        /// detail around — the parked Camera.main (the bug) or the mod's head-tracking
        /// DetailFocus override (<see cref="ApparanceDetailFocus"/>, the fix). Proves from a
        /// hardware log that the override engaged, and where the parked camera actually sat.
        /// </summary>
        private static void LogApparanceViewpoint(System.Text.StringBuilder sb)
        {
            sb.Length = 0;
            sb.Append("APPARANCE VIEWPOINT: ");
            try
            {
                ApparanceEngine? engine = ApparanceEngine.Instance;
                if (engine == null)
                {
                    sb.Append("no engine instance");
                }
                else if (engine.EnableDetailFocus && engine.DetailFocus != null)
                {
                    Vector3 p = engine.DetailFocus.transform.position;
                    sb.Append("DetailFocus override '").Append(engine.DetailFocus.name)
                      .Append("' at (").Append(p.x.ToString("F1")).Append(',')
                      .Append(p.y.ToString("F1")).Append(',')
                      .Append(p.z.ToString("F1")).Append(')');
                }
                else
                {
                    Camera? main = Camera.main;
                    if (main != null)
                    {
                        Vector3 p = main.transform.position;
                        sb.Append("Camera.main '").Append(main.name)
                          .Append("' (PARKED under VR) at (").Append(p.x.ToString("F1"))
                          .Append(',').Append(p.y.ToString("F1")).Append(',')
                          .Append(p.z.ToString("F1")).Append(')');
                    }
                    else
                    {
                        sb.Append("no Camera.main — engine falls back to first active camera");
                    }
                }
            }
            catch (Exception e)
            {
                sb.Append("unreadable: ").Append(e.GetType().Name);
            }
            VRLog.Info(Name, sb.ToString());
        }

        /// <summary>Breadth-limited recursive child search by exact name (the game's own
        /// FindInChildren equivalent — map tiles nest 'Generated Content' a level down).</summary>
        private static Transform? FindChildByName(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (c.name == name)
                    return c;
                Transform? deep = FindChildByName(c, name);
                if (deep != null)
                    return deep;
            }
            return null;
        }

        /// <summary>Mod-owned visual (hands, cards, panels, MR backing plates…)? Never scenery:
        /// such a renderer must neither be adopted by ANY attachment sweep nor appear in their
        /// candidate/near-miss diagnostics. Two signals, because not every mod object lives on
        /// the mod layer: hardware round 3 caught the MR sky backing 'GloomhavenVR.MrBacking'
        /// (y[21.3..33.3]) in the stacked-shell NEAR-MISS census — every mod-created object
        /// carries the 'GloomhavenVR.' name prefix (repo convention), so that prefix is the
        /// second, layer-independent test.</summary>
        private static bool IsModObject(Renderer r) =>
            r.gameObject.layer == VRLayers.ModLayer
            || r.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal);

        /// <summary>
        /// FIGURES ARE NEVER TOUCHED — round-7 ruling, same severity as the Lights rule
        /// (mauern_problem_neu.png: the BRUTE's horned back-of-head was PERMANENTLY hidden —
        /// an accessory mesh adopted by a wall sweep inside a faded wall's ring-spanning
        /// AABB). Airtight, deliberately over-broad (fail-open = the renderer stays visible):
        /// <list type="bullet">
        /// <item>every <see cref="SkinnedMeshRenderer"/>, outright — characters are skinned,
        ///   scenery is not; the banner case loses skinned support, accepted;</item>
        /// <item>anything under an <c>ActorBehaviour</c> ancestor (the game's board actor);</item>
        /// <item>anything under a <c>CInteractableActor</c> ancestor (the game's interactable
        ///   figure root — the same component FigureGrab picks by);</item>
        /// <item>anything under an <c>Animator</c> ancestor — a horn/head accessory hangs off
        ///   a BONE, and whatever the actor component layout, the animation rig root is
        ///   always above it. Also excludes animated props (chests…), which no wall system
        ///   should ever hide anyway.</item>
        /// </list>
        /// Checked by EVERY adoption sweep (stack candidates + adoption + fast reclaim, wall
        /// body, mounted dressing, corner pieces) and enforced retroactively by
        /// <see cref="PurgeFigureRenderers"/> each rescan (restitution: a previously-adopted
        /// figure renderer is restored the moment this guard classifies it).
        /// </summary>
        private static bool IsFigureOrActorRenderer(Renderer r)
        {
            if (r is SkinnedMeshRenderer)
                return true;
            // FAST PATH (PERF S1) — only inside an open memo scope, and only for a renderer
            // whose whole ancestor chain is active. See FigureAncestryMemo.
            if (_figureMemoActive && r.gameObject.activeInHierarchy)
                return FigureAncestry(r.transform);
            return r.GetComponentInParent<ActorBehaviour>() != null
                || r.GetComponentInParent<CInteractableActor>() != null
                || r.GetComponentInParent<Animator>() != null;
        }

        /// <summary>
        /// PERF S1 (2026-08-09): memo for <see cref="IsFigureOrActorRenderer"/>'s ancestor
        /// search, keyed by transform.
        ///
        /// <para>WHY IT WAS WORTH IT. The guard runs THREE separate
        /// <c>GetComponentInParent</c> walks, and the stacked-shell candidate prefilter calls
        /// it for EVERY renderer in the scene — ~3000 of them per rescan, each walk visiting
        /// every level up to the scene root. That is tens of thousands of native component
        /// lookups per rescan, and it was the largest non-<c>FindObjectsOfType</c> item in the
        /// 50-97 ms rescan.</para>
        ///
        /// <para>WHY THE VERDICT IS UNCHANGED, RENDERER FOR RENDERER. "Is there an
        /// ActorBehaviour / CInteractableActor / Animator on this transform or any ancestor"
        /// is exactly what the three <c>GetComponentInParent</c> calls answer, and it is a
        /// property of the CHAIN, so a transform's answer is its own components OR its
        /// parent's answer — which is what <see cref="FigureAncestry"/> computes and caches.
        /// Two guards keep it exact rather than merely equivalent-in-practice:</para>
        /// <list type="bullet">
        /// <item>ACTIVE CHAINS ONLY. <c>GetComponentInParent&lt;T&gt;()</c> without
        ///   <c>includeInactive</c> considers only active GameObjects; for a renderer that is
        ///   <c>activeInHierarchy</c> every ancestor is active by definition, so on that path
        ///   the qualifier is vacuous and the memo cannot disagree. A renderer whose chain is
        ///   NOT fully active takes the original three calls verbatim.</item>
        /// <item>SCOPED, NEVER PERSISTENT. The memo is only consulted between
        ///   <see cref="BeginFigureMemo"/> and <see cref="EndFigureMemo"/>, which bracket ONE
        ///   synchronous pass (a rescan, a fast-reclaim sweep). No game code runs inside such
        ///   a pass, so nothing can re-parent an actor mid-pass — the case the round-7 "belt
        ///   over the prefilter" re-check exists for is a re-parent between FRAMES, and the
        ///   memo is empty at every frame boundary.</item>
        /// </list>
        /// </summary>
        private static readonly Dictionary<Transform, bool> FigureAncestryMemo = new(1024);
        private static bool _figureMemoActive;

        private static void BeginFigureMemo()
        {
            FigureAncestryMemo.Clear();
            _figureMemoActive = true;
        }

        private static void EndFigureMemo()
        {
            _figureMemoActive = false;
            FigureAncestryMemo.Clear(); // never hold transform references across frames
        }

        /// <summary>Does this transform or any ancestor carry one of the figure components?
        /// (Memoized upward — see <see cref="FigureAncestryMemo"/>.)</summary>
        private static bool FigureAncestry(Transform t)
        {
            if (FigureAncestryMemo.TryGetValue(t, out bool cached))
                return cached;
            bool here = t.GetComponent<ActorBehaviour>() != null
                || t.GetComponent<CInteractableActor>() != null
                || t.GetComponent<Animator>() != null;
            Transform? parent = t.parent;
            bool verdict = here || (parent != null && FigureAncestry(parent));
            FigureAncestryMemo[t] = verdict;
            return verdict;
        }

        /// <summary>Any shared material on a foliage-family shader? (Cached per Shader.)</summary>
        private bool RendererUsesFoliage(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    continue;
                Shader sh = m.shader;
                if (sh == null)
                    continue;
                if (!_shaderFoliageVerdict.TryGetValue(sh, out bool foliage))
                {
                    foliage = IsFoliageShaderName(sh.name);
                    _shaderFoliageVerdict[sh] = foliage;
                }
                if (foliage)
                    return true;
            }
            return false;
        }

        /// <summary>Any shared material on a wall-fade-capable shader? (Cached per Shader.)</summary>
        private bool RendererUsesWallFade(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null)
                    continue;
                Shader sh = m.shader;
                if (sh == null)
                    continue;
                if (!_shaderVerdict.TryGetValue(sh, out bool capable))
                {
                    capable = IsWallFadeShaderName(sh.name);
                    _shaderVerdict[sh] = capable;
                }
                if (capable)
                    return true;
            }
            return false;
        }

        /// <summary>ADJACENT RE-ANCHOR reach (round 6, wu; round 11 raised 2.0 → 4.0): a
        /// wall whose nearest room is unanchorable re-anchors to an anchored logical room
        /// only when it PHYSICALLY borders it — XZ gap at most this. Round-11 hardware: the
        /// two gate-flanking TOWERS stood permanently solid among 18 fail-safe walls — they
        /// PROTRUDE outward from the gate face on the rock base, beyond the old 2.0-wu
        /// reach of the room footprint. 4.0 covers tower/buttress protrusion while distant
        /// unrevealed rooms (other map tiles ≥ ~11 wu away) still can never reach. Every
        /// re-anchored wall is named in the census line below.</summary>
        private const float AdjacentReanchorMaxGapWU = 4.0f;

        /// <summary>Round-11 census: which walls the adjacent re-anchor rescued this rescan
        /// (name + XZ gap) — the log line that shows the towers joining the fade.</summary>
        private readonly List<string> _reanchorCensus = new();
        private int _lastLoggedReanchorCount = -1;

        /// <summary>ROOM SEAM band (wu) — user report 2026-08-09, the wall piece that faded
        /// "falsch rum". A wall whose AABB is within this of a SECOND decision-valid room's
        /// box borders that room too and is judged against it as well (max coverage wins; see
        /// the long note in <see cref="AssociateRooms"/>). Sized as ONE WALL THICKNESS, from
        /// the hardware log itself: this tileset's slabs measure 0.8–1.8 wu across (segment
        /// BlockEps 0.45–0.90 = half-thickness) and the two room boxes of the reported level
        /// sit 0.65 wu apart, so a partition standing in that seam is within ~1.25 wu of both
        /// while a wall well inside one room is not. Deliberately far below the 4.0 wu
        /// re-anchor reach: this must catch partitions, never distant rooms.</summary>
        private const float RoomBorderBandWU = 1.25f;

        /// <summary>Census of the seam walls (name + the rooms they are judged against) —
        /// change-triggered, so the next hardware log names them without spamming.</summary>
        private readonly List<string> _seamCensus = new();
        private int _lastLoggedSeamCount = -1;

        /// <summary>
        /// Is this material's wall-fade subgraph PRESENT AND DRIVEABLE (round-9 game-wide
        /// audit — all known gate spellings, each with its liveness rule)? Requires
        /// <c>_Cutoff</c> (the clip the fade sweeps), then:
        /// <list type="bullet">
        /// <item><c>_ToggleWallfade</c> — a runtime float uniform (DXBC-verified on the HIGH
        ///   variant + ParticleMaster): always driveable → native.</item>
        /// <item><c>_WallFade_On</c> — a compile-time switch (keyword
        ///   <c>_WALLFADE_ON_ON</c>; ModBuild-65 adjudication): native only when the
        ///   authored value is 1 or the keyword is enabled — otherwise the fade branch is
        ///   absent from the compiled variant and the MPB float is inert (the round-8
        ///   floors), so the renderer belongs to the enabled fallback instead.</item>
        /// <item><c>_ToggleWallFadeLocal</c> — the game's authored per-asset opt-out
        ///   (<c>ToggleWallFadeScript</c> writes it at Start): native only when it reads
        ///   nonzero. An opted-out asset is authored ALWAYS-SOLID and is honored (never
        ///   pinned to 1 — the doorway-ruling spirit).</item>
        /// </list>
        /// </summary>
        private static bool HasLiveWallFadeToggle(Material m)
        {
            if (!m.HasProperty(CutoffId))
                return false;
            if (m.HasProperty(ToggleWallfadeMatId))
                return true;
            if (m.HasProperty(WallFadeOnMatId))
                return m.GetFloat(WallFadeOnMatId) != 0f || m.IsKeywordEnabled(WallFadeOnKeyword);
            if (m.HasProperty(ToggleWallFadeLocalMatId))
                return Mathf.Abs(m.GetFloat(ToggleWallFadeLocalMatId)) > 0.5f;
            return false;
        }

        /// <summary>A wall-fade gate EXISTS on some material but is not live (authored off /
        /// keyword-absent variant) — the audit line's 'gated-off' class: authored to stay
        /// solid, deliberately untouched.</summary>
        private bool HasGatedOffWallFadeToggle(MeshRenderer r)
        {
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null || !m.HasProperty(CutoffId))
                    continue;
                bool hasGate = m.HasProperty(WallFadeOnMatId)
                    || m.HasProperty(ToggleWallfadeMatId)
                    || m.HasProperty(ToggleWallFadeLocalMatId);
                if (hasGate && !HasLiveWallFadeToggle(m))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// WALL-PATH AUDIT (round-9 game-wide generalization; user: "wende das mit ALLEN
        /// Mauer-Assets aus ALLEN Levels genauso an"): one heartbeat line that classifies
        /// EVERY cache wall's child renderers by the discovery/delivery path that owns them —
        /// the self-audit that lets every future hardware log prove a new tileset is covered
        /// without another blind round. Classes: native-name (WallFade shader family),
        /// toggle-native (live gate — round 8), attachment-claimed (body/stacked/mounted/
        /// corner via the ownership table), foliage, ground-band (solid by design),
        /// figure-guarded, gated-off (authored always-solid — honored), and UNCLAIMED.
        /// UNCLAIMED &gt; 0 is the alarm: an asset family fell through every path.
        /// </summary>
        private void LogWallPathAudit()
        {
            int walls = 0, nameN = 0, toggleN = 0, attach = 0, foliage = 0;
            int ground = 0, figures = 0, standing = 0, gatedOff = 0, unclaimed = 0;
            var unNames = new System.Text.StringBuilder();
            foreach (KeyValuePair<Component, Segment> kv in _segments)
            {
                Segment seg = kv.Value;
                if (!seg.FromWallCache || seg.Anchor == null)
                    continue;
                walls++;
                int segToggle = Mathf.Min(seg.ToggleNative, seg.Renderers.Count);
                toggleN += segToggle;
                nameN += seg.Renderers.Count - segToggle;
                float ceiling = RoomDecisionValid(seg.RoomIndex)
                    ? _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU
                    : float.NegativeInfinity;
                MeshRenderer[] all =
                    seg.Anchor.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
                foreach (MeshRenderer r in all)
                {
                    if (r == null || seg.Renderers.Contains(r))
                        continue; // counted above (native/toggle split)
                    if (seg.Foliage.Contains(r))
                    {
                        foliage++;
                    }
                    else if (_attachmentOwned.ContainsKey(r))
                    {
                        attach++; // body/stacked/mounted/corner — all fade-delivered
                    }
                    else if (IsModObject(r) || !r.enabled)
                    {
                        // not scenery / game-disabled — no path applies, not an alarm
                    }
                    else if (IsStandingFigureProp(r))
                    {
                        // Own bucket on purpose (2026-08-15): "figure-guarded" used to mean
                        // "an adoption sweep declined it" while the wall path could still be
                        // fading it. Anything counted HERE is refused by the wall path too, so
                        // the audit and the delivery can no longer disagree.
                        standing++;
                    }
                    else if (IsFigureOrActorRenderer(r))
                    {
                        figures++;
                    }
                    else if (r.bounds.max.y <= ceiling)
                    {
                        ground++;
                    }
                    else if (HasGatedOffWallFadeToggle(r))
                    {
                        gatedOff++;
                    }
                    else
                    {
                        unclaimed++;
                        if (unNames.Length < 160)
                        {
                            if (unNames.Length > 0)
                                unNames.Append(", ");
                            unNames.Append('\'').Append(r.name).Append('\'');
                        }
                    }
                }
            }
            if (walls == 0)
                return;
            VRLog.Info(Name,
                $"WALL-PATH AUDIT scene='{SceneManager.GetActiveScene().name}': {walls} cache "
                + $"wall(s) — renderers: {nameN} native-name + {toggleN} toggle-native (MPB "
                + $"fade), {attach} attachment-claimed (body/stacked/mounted/corner), "
                + $"{foliage} foliage, {ground} ground-band (solid by design), {standing} "
                + $"standing-prop (floor-standing figure/actor prop — never wall geometry on "
                + $"ANY path, WallSegmentFade.Standing.cs), {figures} figure-guarded, "
                + $"{gatedOff} gated-off (authored always-solid — honored), "
                + $"{unclaimed} UNCLAIMED"
                + (unclaimed > 0
                    ? $" [ALARM — fell through every path: {unNames}]"
                    : " — every wall renderer is owned by a path."));
        }

        /// <summary>Toggle-native materials already logged (round 8, cap 6): one line per
        /// material with its authored gate/cutoff values and shader keywords — the datum
        /// that adjudicates the keyword risk (see the Apply comment) from the next log.</summary>
        private readonly HashSet<string> _loggedToggleMats = new();

        private void LogToggleNativeMaterialOnce(Material m)
        {
            if (_loggedToggleMats.Count >= 6
                || !_loggedToggleMats.Add(m.shader.name + "/" + m.name))
                return;
            string wallFadeOn = m.HasProperty(WallFadeOnMatId)
                ? m.GetFloat(WallFadeOnMatId).ToString("0.##") : "n/a";
            string toggleWallfade = m.HasProperty(ToggleWallfadeMatId)
                ? m.GetFloat(ToggleWallfadeMatId).ToString("0.##") : "n/a";
            string cutoff = m.HasProperty(CutoffId)
                ? m.GetFloat(CutoffId).ToString("0.##") : "n/a";
            string local = m.HasProperty(ToggleWallFadeLocalMatId)
                ? m.GetFloat(ToggleWallFadeLocalMatId).ToString("0.##") : "n/a";
            string keywords;
            try { keywords = string.Join(",", m.shaderKeywords); }
            catch { keywords = "?"; }
            VRLog.Info(Name,
                $"TOGGLE-NATIVE MATERIAL '{m.name}' (shader '{m.shader.name}'): authored "
                + $"_WallFade_On={wallFadeOn}, _ToggleWallfade={toggleWallfade}, "
                + $"_ToggleWallFadeLocal={local}, _Cutoff={cutoff}, keywords=[{keywords}] — "
                + "native MPB fade path engaged (round 8; round-9 liveness rules: keyword-off "
                + "_WallFade_On variants and _ToggleWallFadeLocal opt-outs are NOT routed "
                + "here).");
        }

        /// <summary>
        /// Bind every wall segment to the ONE room whose AABB it borders: smallest XZ gap
        /// between wall AABB and room AABB (a wall bordering its room touches it → gap 0;
        /// Y is ignored — room-bounds Y is the untrusted proxy axis). Near-ties (a door
        /// wall between two rooms) go to the room whose center is nearer to the wall.
        ///
        /// ROUND-6 ADJACENT RE-ANCHOR: when the nearest room is NOT decision-valid
        /// (unanchored / no grid — the keep log: 23 perimeter walls stuck fail-safe on
        /// never-anchoring neighbor entries) but the wall PHYSICALLY borders an anchored
        /// logical room (gap ≤ <see cref="AdjacentReanchorMaxGapWU"/>), the wall is
        /// assigned to THAT room. This is still strict own-room accounting — the wall is
        /// simply bound to the room it actually encloses; an unanchorable sliver between
        /// the wall and the real room no longer steals the assignment. Walls bordering NO
        /// anchored room keep the fail-safe (solid).
        /// </summary>
        private void AssociateRooms()
        {
            _reanchorCensus.Clear();
            _seamCensus.Clear();
            foreach (Segment seg in _segments.Values)
            {
                seg.RoomIndex = -1;
                seg.BorderRooms.Clear();
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

                if (seg.RoomIndex >= 0 && !RoomDecisionValid(seg.RoomIndex))
                {
                    // Adjacent re-anchor (see the method doc): nearest DECISION-VALID room
                    // the wall actually borders, if any.
                    float maxGapSq = AdjacentReanchorMaxGapWU * AdjacentReanchorMaxGapWU;
                    float altGap = float.PositiveInfinity;
                    float altCenter = float.PositiveInfinity;
                    int alt = -1;
                    for (int r = 0; r < _roomBounds.Count; r++)
                    {
                        if (!RoomDecisionValid(r))
                            continue;
                        Bounds room = _roomBounds[r];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                        float gap = gx * gx + gz * gz;
                        if (gap > maxGapSq)
                            continue;
                        float cx = room.center.x - w.center.x;
                        float cz = room.center.z - w.center.z;
                        float center = cx * cx + cz * cz;
                        if (gap < altGap - 0.0001f
                            || (gap <= altGap + 0.0001f && center < altCenter))
                        {
                            altGap = gap;
                            altCenter = center;
                            alt = r;
                        }
                    }
                    if (alt >= 0)
                    {
                        seg.RoomIndex = alt;
                        if (_reanchorCensus.Count < 12)
                        {
                            string n = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                            _reanchorCensus.Add($"'{n}' gap {Mathf.Sqrt(altGap):F1}");
                        }
                    }
                }

                // ---- ROOM SEAM (user report 2026-08-09: one wall piece faded INVERTED) ------
                // The pick above is "nearest room box, ties by nearest room centre". For a wall
                // standing in the SEAM between two rooms that is a coin flip decided by tenths
                // of a wu: the hardware log's two rooms are map tiles 'E' (x 17.1..25.9,
                // z -4.1..4.1) and 'LL' (x 9.3..27.0, z 4.7..15.9) — their boxes are 0.65 wu
                // apart, while a masonry slab here is 0.8..1.8 wu THICK. Every partition
                // therefore overlaps or nearly overlaps BOTH boxes and the winner is decided by
                // which side the slab happens to lean.
                //
                // That coin flip is the whole bug, because the coverage metric is what carries
                // the side: a wall fades when it hides its OWN room's floor from the head, which
                // is "outside-in" by construction — and measuring a seam wall against the room
                // on the WRONG side inverts it exactly as reported ("von außen faded es nicht,
                // aber von innen"). Standing in room 2 you are outside room 1, the wall hides
                // room 1's floor, so it fades; standing outside room 2 it hides nothing of room
                // 1, so it stays. Every other wall in the level borders one room and behaves.
                //
                // The fix does not try to guess the coin flip right — it removes the flip. A
                // seam wall genuinely belongs to BOTH rooms it separates: from either side it is
                // the thing hiding the room you are looking into, and the standing invariant
                // ("Fading geht immer darum den Raum freizulegen von außen nach innen") holds
                // for both. So the wall records every decision-valid room it BORDERS and
                // BlockedFraction takes the max over them. This is NOT the retired round-3
                // cross-room MAX, which maxed over ALL rooms including ones the wall stood far
                // away from; the band is one wall thickness, so a wall that borders exactly one
                // room — the overwhelming majority — is bit-for-bit unchanged.
                //
                // MULTIPLAYER: nothing to send. Which wall a head occludes is a per-player fact
                // by definition, and the opt-in peer sync (wire record 17) already carries the
                // RESULT — this only changes how the LOCAL decision is computed, from the same
                // replicated room geometry on every machine, so the wire format, the key
                // derivation and the record are untouched.
                if (seg.RoomIndex >= 0)
                {
                    for (int r = 0; r < _roomBounds.Count; r++)
                    {
                        if (r == seg.RoomIndex || !RoomDecisionValid(r))
                            continue;
                        Bounds room = _roomBounds[r];
                        float gx = Mathf.Max(0f, Mathf.Max(room.min.x - w.max.x, w.min.x - room.max.x));
                        float gz = Mathf.Max(0f, Mathf.Max(room.min.z - w.max.z, w.min.z - room.max.z));
                        if (gx * gx + gz * gz > RoomBorderBandWU * RoomBorderBandWU)
                            continue;
                        seg.BorderRooms.Add(r);
                    }
                    if (seg.BorderRooms.Count > 0 && _seamCensus.Count < 10)
                    {
                        string n = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                        _seamCensus.Add($"'{n}' r{seg.RoomIndex}+{string.Join("+", seg.BorderRooms)}");
                    }
                }
            }
            if (_seamCensus.Count != _lastLoggedSeamCount)
            {
                _lastLoggedSeamCount = _seamCensus.Count;
                if (_seamCensus.Count > 0)
                    VRLog.Info(Name,
                        $"ROOM SEAM: {_seamCensus.Count} wall(s) stand between TWO rooms "
                        + $"(both room boxes within {RoomBorderBandWU:0.00} wu = one wall "
                        + "thickness) and are judged against EACH of them, max coverage wins — "
                        + "the 2026-08-09 inverted-wall report: a seam wall hides whichever room "
                        + "you are NOT in, so it must fade from either side. Non-seam walls keep "
                        + $"strict own-room accounting unchanged: {string.Join(", ", _seamCensus)}.");
            }
            if (_reanchorCensus.Count != _lastLoggedReanchorCount)
            {
                _lastLoggedReanchorCount = _reanchorCensus.Count;
                if (_reanchorCensus.Count > 0)
                    VRLog.Info(Name,
                        $"ADJACENT RE-ANCHOR: {_reanchorCensus.Count} wall(s) bound to the "
                        + $"anchored room they border (reach ≤{AdjacentReanchorMaxGapWU:0.0} wu "
                        + $"— round 11: gate towers protrude on the rock base): "
                        + $"{string.Join(", ", _reanchorCensus)}.");
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
            BeginRefresh(seg);
            if (seg.Anchor == null)
                return;
            MeshRenderer[] all = seg.Anchor.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (MeshRenderer r in all)
            {
                if (r == null)
                    continue;
                if (!CollectWallFadeInfo(r, seg))
                {
                    // Not fade-capable — but a foliage dressing of this wall rides its fade
                    // (the "Gestrüpp-Wand" report). Ground-level tufts are dropped later by
                    // StripGroundRenderers, exactly like ground geometry. A floor-standing
                    // FIGURE prop is excluded here too: the foliage list hides its renderers
                    // outright, so a mossy statue would vanish whole instead of losing a head.
                    // Deliberately the FIGURE arm only and not the 2026-08-19 widening — a bush
                    // is a multi-piece thing standing on the ground under the height cap, so the
                    // wider rule would hand the Gestrüpp-Wand report straight back. See
                    // WallSegmentFade.Standing.cs, IsStandingFigureOnlyProp.
                    if (RendererUsesFoliage(r) && !IsStandingFigureOnlyProp(r))
                        seg.Foliage.Add(r);
                    continue;
                }
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
            FinishRefresh(seg);

            // Census tripwire: a cache wall WITH renderers but ZERO fade-capable ones means a
            // tileset whose wall shaders live outside the WallFade family — the one case neither
            // discovery source can fade. The heartbeat prints the shader names so the next
            // hardware log identifies the family to add.
            if (seg.Renderers.Count == 0 && all.Length > 0)
            {
                _censusWallsWithoutFade++;
                if (_unfadeableWallShaders.Count < 8)
                {
                    foreach (MeshRenderer r in all)
                    {
                        if (r == null)
                            continue;
                        _matScratch.Clear();
                        r.GetSharedMaterials(_matScratch);
                        foreach (Material m in _matScratch)
                        {
                            if (m != null && m.shader != null && _unfadeableWallShaders.Count < 8)
                                _unfadeableWallShaders.Add(m.shader.name);
                        }
                    }
                }
                // ROUND 6: the tripwire case is no longer diagnose-only — these plain
                // meshes ARE the visible wall (the keep masonry); collect them as the
                // segment's BODY (bounds + enabled-only delivery, WallSegmentFade.Body.cs).
                CollectPlainWallBody(seg, all);
            }
            else if (seg.Body.Count > 0)
            {
                // The wall (re)gained real fade renderers — the game's own shader path
                // wins; release the body takeover cleanly.
                RestoreSegmentBody(seg);
                seg.Body.Clear();
            }
        }

        /// <summary>Reset a segment's collected state for re-fill, parking the previous renderer
        /// list so <see cref="FinishRefresh"/> can clear property blocks off leavers.</summary>
        private static void BeginRefresh(Segment seg)
        {
            seg.PrevRenderers.Clear();
            seg.PrevRenderers.AddRange(seg.Renderers);
            seg.Renderers.Clear();
            seg.PrevFoliage.Clear();
            seg.PrevFoliage.AddRange(seg.Foliage);
            seg.Foliage.Clear();
            seg.HasBounds = false;
            seg.VariantHigh = false;
            seg.VariantLow = false;
            seg.ToggleNative = 0;
            seg.ShaderNames = "?";
            seg.HeldCutoff = 0.5f;
            seg.CutoffAuthored = false;
            seg.Engulfing = false; // re-derived by NeutralizeEngulfingSegments after association
        }

        /// <summary>Post-refresh bookkeeping: clear our property block from renderers that LEFT a
        /// currently-faded segment (they would otherwise keep the fade forever — nothing else
        /// ever touches them again), then derive the blocked-test epsilon from the new bounds.</summary>
        private static void FinishRefresh(Segment seg)
        {
            if (seg.HasBlock)
            {
                foreach (MeshRenderer prev in seg.PrevRenderers)
                {
                    if (prev != null && !seg.Renderers.Contains(prev))
                        prev.SetPropertyBlock(null);
                }
            }
            seg.PrevRenderers.Clear();
            // Foliage that LEFT the segment is restored unconditionally — a hidden bush no
            // list points at any more would otherwise stay invisible forever.
            if (seg.FoliageState != 0)
            {
                foreach (MeshRenderer prev in seg.PrevFoliage)
                {
                    if (prev != null && !seg.Foliage.Contains(prev))
                        RestoreFoliageRenderer(prev);
                }
            }
            seg.PrevFoliage.Clear();
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
        /// Does any shared material use one of the wall-fade shaders — by NAME (the
        /// WallFade family) or by TOGGLE (round 8: materials exposing <c>_Cutoff</c> plus
        /// <c>_WallFade_On</c>/<c>_ToggleWallfade</c>, i.e. the same Amp fade subgraph
        /// behind a material switch; the keep's masonry Amp_Basic_N_MRAO is one)? Also
        /// records the shader name(s) and LOW/HIGH variant flags on the segment
        /// (rescan-time only). Deliberately NOT part of <see cref="RendererUsesWallFade"/>:
        /// the toggle test only runs for renderers that are already wall geometry by
        /// construction (children of cache walls) — N_MRAO dresses half the scenery, and a
        /// scene-wide toggle-based adoption would claim all of it as walls.
        /// </summary>
        private bool CollectWallFadeInfo(MeshRenderer r, Segment seg)
        {
            // STANDING PROPS ARE NEVER WALL GEOMETRY (user report 2026-08-15, skelet.jpg —
            // the skeleton statue's skull faded with 'Wall 1' while its body stayed). This is
            // the ONE choke point every wall-renderer collection path goes through — the cache
            // refresh, the split-wall refresh, the shader-adoption sweep and the gate-column
            // branch all add the renderer only when this returns true — so the guard lives here
            // rather than four times over. Refusing BEFORE the material walk also keeps the
            // prop out of ToggleNative counts, ShaderNames, the masonry template donor and the
            // authored-cutoff pick: the segment must not learn its wall math from a statue.
            // See WallSegmentFade.Standing.cs for the rule and why the plain figure guard is
            // not it.
            if (IsStandingFigureProp(r))
            {
                NoteStandingPropBlocked(r, seg);
                // Restitution: if this renderer was in THIS segment's list before the rule
                // existed (or before the prop moved into the ground band), it may be carrying
                // our fade block right now. FinishRefresh only clears leavers while the
                // segment HasBlock; clear it here unconditionally so a prop can never stay
                // half-dissolved because its owner happened to be solid this frame.
                if (seg.PrevRenderers.Contains(r))
                    r.SetPropertyBlock(null);
                return false;
            }
            bool any = false;
            _matScratch.Clear();
            r.GetSharedMaterials(_matScratch);
            foreach (Material m in _matScratch)
            {
                if (m == null || m.shader == null)
                    continue;
                string shaderName = m.shader.name;
                bool byName = shaderName.Contains("WallFade");
                bool byToggle = !byName && HasLiveWallFadeToggle(m);
                if (!byName && !byToggle)
                    continue;
                any = true;
                if (byToggle)
                {
                    seg.ToggleNative++;
                    LogToggleNativeMaterialOnce(m);
                    CaptureMasonryTemplate(m); // round-11 dissolve-swap template donor
                    shaderName += "(toggle-native)";
                }
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
                RestoreSegmentFoliage(seg);
                RestoreSegmentSiblings(seg);
                RestoreSegmentMounted(seg);
                RestoreSegmentStacked(seg);
                RestoreSegmentBody(seg);
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
            // Toggle-off / no-scenario stops the rescan entirely, so the orphan guard would never
            // run again: empty the hidden ledger right here. "WallFade off" must mean vanilla,
            // with nothing of ours left switched off anywhere.
            RestoreAllMountedProps();
            if (cleared > 0)
                VRLog.Info(Name, $"cleared property blocks on {cleared} renderers ({reason}).");
        }

        /// <summary>Full teardown: revert every renderer and destroy our textures.</summary>
        internal void Teardown()
        {
            try { ClearAllBlocks("teardown"); }
            catch { /* renderers already dying with the scene */ }
            // Nothing we ever hid may survive a teardown — including a prop whose owner segment
            // died on some path before it could restore it (the orphan ledger's last stop).
            try { RestoreAllMountedProps(); }
            catch { /* renderers already dying with the scene */ }
            _segments.Clear();
            _roomBounds.Clear();
            _roomFloorY.Clear();
            _roomFloorAnchored.Clear();
            _roomLabels.Clear();
            _roomRendererCounts.Clear();
            _keyToRoomScratch.Clear();
            _roomMapByRenderer.Clear();
            _roomMapLabelByRenderer.Clear();
            _roomSampleStart.Clear();
            _roomSampleCount.Clear();
            _allSamples.Clear();
            _floorYByRenderer.Clear();
            _cornerPieces.Clear();
            _peerFades.Clear();
            // Round 14: gate lifecycle state is per-scenario — a stale arch rect or a
            // remembered fade from the previous table must never seed the next one.
            _archRects.Clear();
            _gateMemory.Clear();
            _gateSliverLogged.Clear();
            // …and so is the water-feature protection (user ruling 2026-08-09).
            _waterRects.Clear();
            _waterCensusSig = -1;
            AbandonRescanCycle();
            _shaderWaterVerdict.Clear();
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
