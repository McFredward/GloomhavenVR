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
/// DOORWAY RULING (user, torbogen3.png round 3 — HARD HIDE replaces the shader fade for
/// OPEN doorways entirely): archway frame/pillar renderers are adopted PER DOOR (spatial
/// link to the UnityGameEditorDoorProp roots, <see cref="FadeDriver.FindDoorwayRoot"/> —
/// the round-1 ancestor walk found +0 siblings because the frames parent flat under an
/// 'L :' section container). Door CLOSED → the whole archway is held SOLID exactly like
/// the door itself, coverage notwithstanding. Door OPEN (animator "Open" state, the
/// game's own check from Choreographer.OpenDoor; CObjectDoor.DoorIsOpen as rules-side
/// fallback) → the coverage decision hides the ENTIRE archway assembly as one unit via
/// renderer/light disables + particle stops — NOT the MaterialPropertyBlock fade, which
/// round 2's hardware run proved partial: the wooden wings, glow lights and the doorway
/// entity's non-WallFade stone stubs carry no fade-capable shader and survived every
/// MPB (torbogen3.png: wings + two glows + a floating wall chunk). The assembly is the
/// door prop's COMPLETE subtree (the game's own hide unit — ApparanceLayer.Create and
/// UnityGameEditorRuntime.MakeDoor both sweep GetComponentsInChildren&lt;Renderer&gt; on
/// it and toggle enabled, read from source) + the archway's fade renderers + a
/// conservative AABB sweep for container-mates (<see cref="FadeDriver.CollectDoorwayAssembly"/>).
/// The flat game merely swings the wings open (Choreographer.OpenDoor plays "Open";
/// nothing sinks or hides), so "open doorway must never block the view nor leave floating
/// remnants" is delivered here, not mirrored. Every touched component is tracked and
/// restored exactly on unfade/door-close/teardown; every diag/flip line carries
/// door=open/closed.
///
/// MULTIPLAYER: purely local rendering (MaterialPropertyBlocks + locally created textures);
/// nothing synced, peers unaffected (door state is READ-only: animator + rules flag).
/// Gated LIVE by [Compat] WallFade — OFF clears every block
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

    /// <summary>
    /// THE game's own marker for "this mesh fades when it hides the play area": its material
    /// runs one of the WallFade shader family (Amp_Basic_WallFade, Amp_Low/Amp_Basic_WallFade_Low,
    /// and any themed sibling — the flat game's fade is purely shader-driven, it keeps no object
    /// list). Single source of truth, shared with <see cref="StaticBatcher"/> so a fade-capable
    /// renderer is never folded into a combined mesh the fade's property blocks cannot reach.
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
    private sealed class Segment
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
        /// <summary>DOORWAY ruling (user, torbogen3.png round 3): non-null marks this segment as
        /// a DOORWAY — its fade renderers (frame/pillars) were adopted per-door, keyed by the
        /// door prop root ('ThinDoor : (guid)', the UnityGameEditorDoorProp object whose child
        /// is the ApparanceLayer wings prefab instance with the "Open" animator). The segment's
        /// fade is GATED on the door state instead of look-at coverage alone: door CLOSED →
        /// the whole archway is held solid exactly like the door itself (never fades); door
        /// OPEN → the coverage decision HARD-HIDES the complete assembly (the doorway hard-hide
        /// sets below) instead of running the shader fade — see the class-header ruling.</summary>
        public Transform? DoorRoot;
        /// <summary>Door state as of the last evaluation tick (animator "Open" state, with the
        /// rules-side CObjectDoor.DoorIsOpen as fallback). Only meaningful when
        /// <see cref="DoorRoot"/> is set.</summary>
        public bool DoorOpen;
        /// <summary>DOORWAY HARD-HIDE set (user ruling R3, torbogen3.png): EVERY renderer of
        /// the open-doorway assembly — the archway's own fade renderers, the door prop
        /// subtree's renderers of ANY type/shader (wooden wings, glow quads, lock, trim), and
        /// the conservatively AABB-swept container-mates (the doorway entity's non-WallFade
        /// stone stubs). Hidden via <c>enabled=false</c> when the fade holds, restored exactly
        /// (only ever contains renderers that were enabled when collected).</summary>
        public readonly List<Renderer> DoorwayRenderers = new();
        public readonly List<Renderer> PrevDoorwayRenderers = new();
        /// <summary>Light components of the assembly (door-subtree + swept candle/lantern
        /// glows) — <c>enabled=false</c> while hidden, restored exactly.</summary>
        public readonly List<Light> DoorwayLights = new();
        public readonly List<Light> PrevDoorwayLights = new();
        /// <summary>ParticleSystems of the assembly (flame VFX) — stopped+cleared while
        /// hidden (a merely disabled ParticleSystemRenderer would leave the Lights module
        /// glowing), replayed on restore. Only ever contains systems that were playing.</summary>
        public readonly List<ParticleSystem> DoorwayParticles = new();
        public readonly List<ParticleSystem> PrevDoorwayParticles = new();
        /// <summary>0 = restored/untouched, 2 = assembly hidden.</summary>
        public int DoorwayHideState;
        /// <summary>Collection census for the diag/flip lines (how the assembly was found).</summary>
        public int DoorwaySubtreeCount;
        public int DoorwaySweptCount;
        /// <summary>First few swept container-mate names ("what the AABB rule included" —
        /// the log deliverable proving the rule stayed conservative).</summary>
        public string DoorwaySweptNames = string.Empty;
        /// <summary>Signature of the last logged assembly census (change-triggered log).</summary>
        public int DoorwayLoggedSig = -1;
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
        // ---- doorway door-state gating (user ruling, torbogen2.png round 2) ---------------
        /// <summary>Door prop roots rebuilt each rescan: every live
        /// <c>UnityGameEditorDoorProp</c> transform (the 'ThinDoor : (guid)' objects the
        /// Choreographer opens via <c>ObjectCacheService.GetPropObject</c>). Fade renderers
        /// adjacent to one of these are grouped into a per-DOOR segment (see
        /// <see cref="FindDoorwayRoot"/>) so the whole archway shares one fade unit and one
        /// door-state gate. Entrance/exit doors lose their prop component at spawn
        /// (ApparanceLayer.Create destroys procDoor) — they never open, are not listed, and
        /// their frames keep the generic adopted behaviour (accepted fail-open).</summary>
        private readonly List<Transform> _doorRoots = new();
        /// <summary>Max XZ gap (wu) between a fade renderer's AABB and a door prop position for
        /// the renderer to count as that doorway's frame — mirrors the game's own wall-search
        /// radius around a door (ProceduralTile.FindMapTileByPosition: FindWallsNear(pos, 2.2f)).
        /// Frames/pillars hug the door; the next parallel wall run is ≥ a hex (~1.72 wu) of
        /// clear floor away, so 2.2 cannot swallow a neighbouring wall.</summary>
        private const float DoorwayLinkMaxXZ = 2.2f;
        /// <summary>Horizontal/upward slack (wu) around the archway segment's AABB inside which
        /// a container-mate renderer/light/particle counts as part of the doorway ASSEMBLY
        /// (the AABB sweep of <see cref="CollectDoorwayAssembly"/>). Deliberately below half a
        /// hex (~0.86 wu of clear floor separates the archway from the next wall run, and
        /// neighbouring wall meshes are excluded by shader/ancestry anyway) — the sweep must
        /// stay conservative: it exists for the doorway entity's own non-WallFade stone stubs,
        /// candles and banners generated INTO the archway footprint.</summary>
        private const float DoorwayAssemblyMarginWU = 1.0f;
        // ---- doorway hard-hide collection scratch (rescan-scope) --------------------------
        /// <summary>Cross-type dedupe for one rescan's doorway assemblies: every component any
        /// doorway adopted (renderer, light or particle system) — exactly ONE segment may own
        /// and restore each, the sibling-ownership rule extended to the whole assembly.</summary>
        private readonly HashSet<UnityEngine.Object> _doorwaySeen = new();
        private readonly List<Renderer> _rendererScratch = new();
        private readonly List<Light> _lightScratch = new();
        private readonly List<ParticleSystem> _psScratch = new();
        /// <summary>"Same container" evidence per doorway: the parents and ProceduralMapTile
        /// ancestors of the segment's own fade renderers (frames sit flat in the 'L :'
        /// container; the doorway entity generates its stone stubs into the same place).</summary>
        private readonly HashSet<Transform> _containerParents = new();
        private readonly HashSet<Transform> _containerTiles = new();
        /// <summary>Scene sweeps captured once per rescan (renderers by
        /// <see cref="AdoptShaderMatchedWalls"/>; lights/particles fetched lazily only when a
        /// doorway segment exists) so the assembly sweep never re-walks the scene per door.</summary>
        private MeshRenderer[]? _sceneRenderers;
        private Light[]? _sceneLights;
        private ParticleSystem[]? _sceneParticles;

        /// <summary>Room-registry census as last logged (reveal re-anchor diagnostic).</summary>
        private int _lastRoomCensusCount = -1;
        private int _lastRoomCensusAnchored = -1;
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
            _shaderVerdict.Clear(); // scene shaders died with their bundles — no dead keys
            _splitAnchors.Clear();
            _lastRoomCensusCount = -1; // fresh scene = fresh room registry (reveal diagnostics)
            _lastRoomCensusAnchored = -1;
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
                if (!seg.HasBounds)
                    continue;

                // Undecidable-as-one-unit segments (see NeutralizeEngulfingSegments) are held
                // solid: state forced off, the fade below decays any residual block away.
                // ROOM-REVEAL FAIL-SAFE (fehlender_boden.png ruling): a wall whose room has no
                // VALID floor grid — unassociated, tile-UNANCHORED plane (median/bounds guess),
                // or a zero-sample grid (over the MaxTotalSamples budget) — must never fade:
                // its coverage would be measured against the wrong plane or the wrong room
                // (the mid-scenario reveal case), and a wrong fade deletes geometry. Solid is
                // the vanilla look, strictly safe; the wall joins the fade the moment its room
                // is anchored (next 2s rescan / reveal-triggered rescan).
                if (seg.Engulfing || !RoomDecisionValid(seg.RoomIndex))
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
                    // DOORWAY ruling (user, torbogen2.png round 2): a doorway segment's fade
                    // follows the DOOR STATE, not look-at coverage alone. CLOSED → the whole
                    // archway (frame, pillars — and the wings/trim siblings that only hide at
                    // full fade anyway) is held SOLID exactly like the door itself; any
                    // in-flight fade decays right back (reversible). OPEN → the doorway must
                    // never block the view: the normal coverage decision applies and hides
                    // the segment as a unit WITH the door subtree siblings. The EMA above
                    // keeps integrating either way so the diag shows live numbers and an
                    // opening door starts from current coverage, not a stale one.
                    if (seg.DoorRoot != null)
                        seg.DoorOpen = DoorIsOpen(seg.DoorRoot);
                    if (seg.DoorRoot != null && !seg.DoorOpen)
                    {
                        if (seg.State)
                        {
                            seg.State = false;
                            LogStateFlip(seg); // door closed under a held fade — log the revert
                        }
                        seg.PendingRaw = false;
                    }
                    else
                    {
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
                                LogStateFlip(seg); // R2 deliverable: shader variant + door state
                            }
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

            // Re-log the heartbeat when the tracked set changes materially (walls stream in over
            // several rescans as Apparance generates, and adopted tilesets appear late) — the
            // first heartbeat of a scenario otherwise reports a half-built table forever.
            if (_heartbeatLogged && _heartbeatSegCount >= 0
                && Mathf.Abs(_segments.Count - _heartbeatSegCount) >= 5)
                _heartbeatLogged = false;

            if (!_heartbeatLogged)
            {
                _heartbeatLogged = true;
                _heartbeatSegCount = _segments.Count;
                LogFloorColumnCensus();
                int highSegs = 0, lowSegs = 0, adoptedSegs = 0, engulfSegs = 0, foliage = 0;
                int siblings = 0, failSafeSegs = 0, doorways = 0, doorsOpen = 0;
                int doorwayRenderers = 0, doorwayLights = 0, doorwayParticles = 0;
                foreach (Segment s in _segments.Values)
                {
                    if (s.VariantHigh) highSegs++;
                    if (s.VariantLow) lowSegs++;
                    if (!s.FromWallCache) adoptedSegs++;
                    if (s.Engulfing) engulfSegs++;
                    foliage += s.Foliage.Count;
                    siblings += s.Siblings.Count;
                    if (!RoomDecisionValid(s.RoomIndex)) failSafeSegs++;
                    if (s.DoorRoot != null)
                    {
                        doorways++;
                        if (s.DoorOpen) doorsOpen++;
                        doorwayRenderers += s.DoorwayRenderers.Count;
                        doorwayLights += s.DoorwayLights.Count;
                        doorwayParticles += s.DoorwayParticles.Count;
                    }
                }
                string unfadeable = _censusWallsWithoutFade > 0
                    ? $"; TRIPWIRE {_censusWallsWithoutFade} cache wall(s) carry NO fade-capable "
                      + $"renderer — their shaders: {string.Join(", ", _unfadeableWallShaders)}"
                    : string.Empty;
                VRLog.Info(Name,
                    $"heartbeat scene='{SceneManager.GetActiveScene().name}': tracking "
                    + $"{_segments.Count} wall segments ({_segments.Count - adoptedSegs} from the "
                    + $"wall cache + {adoptedSegs} ADOPTED by shader, grouped by tile/parent; "
                    + $"fade-capable renderers {_censusFadeRenderers} = {_censusClaimed} claimed "
                    + $"+ {_censusAdopted} adopted; {_splitAnchors.Count} room-engulfing wall(s) "
                    + $"split per renderer, {engulfSegs} unsplittable held solid; {foliage} foliage "
                    + $"attachment(s) + {siblings} asset-sibling(s) ride their wall's fade; "
                    + $"{doorways} DOORWAY segment(s) gated by door state ({doorsOpen} open — "
                    + $"closed archways held solid, open ones HARD-HIDE their whole assembly: "
                    + $"{doorwayRenderers} renderer(s)/{doorwayLights} light(s)/"
                    + $"{doorwayParticles} particle system(s) collected); "
                    + $"{failSafeSegs} wall(s) FAIL-SAFE solid (room unanchored/no floor grid)"
                    + $"{unfadeable}) "
                    + $"(shader variants: {lowSegs} LOW / "
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
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            if (!seg.FromWallCache)
                wall += "~"; // shader-adopted group (tile/parent-anchored), not a cache wall
            string variant = seg.VariantHigh ? (seg.VariantLow ? "HIGH+LOW" : "HIGH") : "LOW";
            // Doorway segments carry their gate state on every flip line — the deliverable
            // that lets a hardware log pin each archway fade to the door that ruled it.
            string door = seg.DoorRoot != null
                ? (seg.DoorOpen ? " door=open" : " door=closed")
                : string.Empty;
            if (seg.State)
            {
                if (seg.DoorRoot != null && seg.DoorOpen)
                {
                    // HARD-HIDE deliverable (R3): list the FULL hidden set — renderers by
                    // source plus lights and particle systems — so the next hardware run
                    // proves completeness (round 2's "6 renderer(s) … +0 asset-sibling(s)"
                    // line was the smoking gun for the surviving wings/glows/stone chunk).
                    var hl = new System.Text.StringBuilder();
                    int listedH = 0;
                    foreach (Renderer r in seg.DoorwayRenderers)
                    {
                        if (r == null)
                            continue;
                        if (listedH++ >= 8) { hl.Append(", …"); break; }
                        if (hl.Length > 0) hl.Append(", ");
                        hl.Append(r.name).Append('@').Append(r.bounds.max.y.ToString("F1"));
                    }
                    VRLog.Info(Name,
                        $"fade ON '{wall}' [{variant}] door=open — HARD HIDE whole archway "
                        + $"assembly (no shader fade): {seg.DoorwayRenderers.Count} renderer(s) "
                        + $"= {seg.Renderers.Count} archway fade + {seg.DoorwaySubtreeCount} "
                        + $"door-subtree + {seg.DoorwaySweptCount} swept"
                        + (seg.DoorwaySweptCount > 0 ? $" [{seg.DoorwaySweptNames}]" : "")
                        + $" + {seg.DoorwayLights.Count} light(s) + "
                        + $"{seg.DoorwayParticles.Count} particle system(s): {hl} — "
                        + "enabled=false / Stop+clear at fade end, restored exactly on "
                        + "unfade/door-close.");
                    return;
                }
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
                    $"fade ON '{wall}' shader '{seg.ShaderNames}' [{variant}]{door} " +
                    $"({seg.Renderers.Count} renderer(s): {rl}; +{seg.Foliage.Count} foliage, " +
                    $"+{seg.Siblings.Count} asset-sibling(s)) — held state: " +
                    cutoff + " → " +
                    (seg.VariantHigh
                        ? "world-Y foundation gradient solid (S=1 ⇒ clip=1-c), upper wall " +
                          "discarded (clip=-c); game-native screen vignette/0.02·dist terms " +
                          "remain inside S — flat-game faded look"
                        : "discard above object-Y 0.4 only — base course below the hard " +
                          "shader gate stays solid (flat-game faded look, view-independent)"));
            }
            else if (seg.DoorRoot != null && seg.DoorOpen)
            {
                VRLog.Info(Name, $"fade OFF '{wall}' [{variant}]{door} — archway assembly "
                    + $"restored ({seg.DoorwayRenderers.Count} renderer(s), "
                    + $"{seg.DoorwayLights.Count} light(s), {seg.DoorwayParticles.Count} "
                    + "particle system(s) back on), solid.");
            }
            else
            {
                VRLog.Info(Name, $"fade OFF '{wall}' [{variant}]{door} — MPB removed, solid.");
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
            // Doorway gate state (user ruling): closed = held solid regardless of coverage,
            // open = coverage decision hides the whole archway incl. door-subtree siblings.
            if (seg.DoorRoot != null)
                _diagSb.Append(seg.DoorOpen ? " door=open" : " door=closed");
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

        /// <summary>Restore ALL of a segment's asset siblings (doors/trim of a mixed asset) —
        /// called on every path where the segment stops owning them (unfade, segment drop,
        /// group split, toggle-off, teardown), so no door can stay hidden without an owner.
        /// enabled-toggle only; nothing else was ever touched on these renderers.</summary>
        private static void RestoreSegmentSiblings(Segment seg)
        {
            if (seg.SiblingState == 0)
                return;
            seg.SiblingState = 0;
            foreach (MeshRenderer s in seg.Siblings)
            {
                if (s != null && !s.enabled)
                    s.enabled = true;
            }
        }

        /// <summary>
        /// Hide the segment's asset siblings exactly while the segment holds fully faded
        /// (Torbogen ruling: the WHOLE doorway asset disappears, not just its shader-matched
        /// frame/pillars). No dissolve ramp — siblings run arbitrary opaque shaders where a
        /// cutoff MPB means nothing, so they switch off at the END of the wall's dissolve
        /// (same threshold as the foliage held state) and back on the moment the fade drops.
        /// </summary>
        private void ApplySiblings(Segment seg)
        {
            if (seg.Siblings.Count == 0)
                return;
            if (seg.Fade < FoliageHideFade)
            {
                RestoreSegmentSiblings(seg);
                return;
            }
            if (seg.SiblingState == 2)
                return; // already hidden — nothing per-frame to do
            foreach (MeshRenderer s in seg.Siblings)
            {
                if (s != null && s.enabled)
                    s.enabled = false;
            }
            seg.SiblingState = 2;
        }

        /// <summary>Restore the ENTIRE doorway assembly (renderers, lights, particles) —
        /// called on every path where the segment stops owning it (unfade, door close,
        /// segment drop, group split, toggle-off, teardown), so nothing can stay hidden
        /// without an owner. enabled-toggle + Play only; the collection lists only ever hold
        /// components that were enabled/playing when collected, so this restore is exact.</summary>
        private static void RestoreDoorwayAssembly(Segment seg)
        {
            if (seg.DoorwayHideState == 0)
                return;
            seg.DoorwayHideState = 0;
            foreach (Renderer r in seg.DoorwayRenderers)
            {
                if (r != null && !r.enabled)
                    r.enabled = true;
            }
            foreach (Light l in seg.DoorwayLights)
            {
                if (l != null && !l.enabled)
                    l.enabled = true;
            }
            foreach (ParticleSystem ps in seg.DoorwayParticles)
            {
                if (ps != null && !ps.isPlaying)
                    ps.Play(withChildren: false);
            }
        }

        /// <summary>
        /// HARD HIDE of an OPEN doorway (user ruling R3, torbogen3.png — replaces the shader
        /// fade for door=open entirely; anything without a fade-capable shader could never
        /// disappear through an MPB). Engages when the damped fade reaches the same threshold
        /// the sibling/foliage held state uses, drops the moment it falls below — visually a
        /// single all-at-once pop, which is exactly the ruling: the whole assembly or nothing,
        /// never a partial remnant. Loops every frame while held (no early-out on state) so a
        /// renderer Apparance regenerates mid-hide is caught next frame, not next rescan.
        /// Any stale MPB is cleared first: an open doorway never runs the shader path.
        /// </summary>
        private void ApplyDoorwayAssembly(Segment seg)
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
            if (seg.Fade < FoliageHideFade)
            {
                RestoreDoorwayAssembly(seg);
                return;
            }
            foreach (Renderer r in seg.DoorwayRenderers)
            {
                if (r != null && r.enabled)
                    r.enabled = false;
            }
            foreach (Light l in seg.DoorwayLights)
            {
                if (l != null && l.enabled)
                    l.enabled = false;
            }
            foreach (ParticleSystem ps in seg.DoorwayParticles)
            {
                // Stop+clear rather than a mere renderer disable: the Lights module of a
                // flame system emits real light that survives its renderer being off.
                if (ps != null && ps.isPlaying)
                    ps.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            seg.DoorwayHideState = 2;
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
            if (want == 2 && seg.FoliageState == 2)
                return; // already hidden — nothing per-frame to do
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
            // DOORWAY door=open (user ruling R3): the shader-fade path below never touches
            // an open doorway — its whole assembly hard-hides as one unit instead. Closed
            // doorways fall through to the normal path, where the decision loop already
            // forces them solid (never fades), and the assembly is restored on the way.
            if (seg.DoorRoot != null && seg.DoorOpen)
            {
                RestoreSegmentFoliage(seg); // doorway segments own no dressing attachments —
                RestoreSegmentSiblings(seg); // defensively free any handover leftovers
                ApplyDoorwayAssembly(seg);
                return;
            }
            RestoreDoorwayAssembly(seg);
            ApplyFoliage(seg);
            ApplySiblings(seg);
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
                VRLog.Info(Name,
                    $"room registry {(reveal ? "REVEAL re-anchor" : "refresh")}: "
                    + $"{Mathf.Max(_lastRoomCensusCount, 0)}→{_roomBounds.Count} room renderer(s), "
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
                    RestoreDoorwayAssembly(kv.Value);
                    _deadKeys.Add(kv.Key!); // destroyed Unity object — reference still hashes
                }
            }
            foreach (Component dead in _deadKeys)
                _segments.Remove(dead);

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

            // Doorway registry (door-state gating): the live door props, refreshed before the
            // adoption sweep so FindDoorwayRoot can re-anchor frame/pillar renderers per door.
            _doorRoots.Clear();
            UnityGameEditorDoorProp[] doorProps =
                UnityEngine.Object.FindObjectsOfType<UnityGameEditorDoorProp>();
            foreach (UnityGameEditorDoorProp dp in doorProps)
            {
                if (dp != null)
                    _doorRoots.Add(dp.transform);
            }

            // Second discovery source: ADOPT every other fade-capable renderer in the scene.
            // The user report behind this ("fortgeschritteneres Szenario mit ganz anderen
            // Mauern — dort werden sie nicht mehr ausgeblendet"): advanced tilesets ship wall
            // meshes as map-tile geometry, not as ProceduralWall entities, so the wall cache
            // never listed them — yet their materials run the same WallFade shader family,
            // because that is how the FLAT game fades them. The shader is the game's own
            // definition of "this is a fadeable wall", so it is our discovery key too.
            AdoptShaderMatchedWalls();

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
            CollectAdoptedSiblings();
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
                    if (r == null || r.bounds.max.y > ceiling)
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
                    if (f == null || f.bounds.max.y > ceiling)
                        continue;
                    if (seg.FoliageState != 0)
                        RestoreFoliageRenderer(f);
                    seg.Foliage.RemoveAt(i);
                }
                if (!changed)
                    continue;
                if (seg.Renderers.Count == 0)
                {
                    // Segment leaves the table — free ALL its attachments (bushes AND doors).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreDoorwayAssembly(seg);
                    _deadKeys.Add(kv.Key);
                    continue;
                }
                // Recompute the AABB from the surviving (actual wall) renderers.
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
                RestoreDoorwayAssembly(group); // and for a doorway's hard-hidden assembly
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
        private float InsideOwnRoomFraction(Segment seg)
        {
            int room = seg.RoomIndex;
            if (room < 0 || room >= _roomSampleCount.Count)
                return 0f;
            int total = _roomSampleCount[room];
            if (total <= 0)
                return 0f;
            int start = _roomSampleStart[room];
            int end = Mathf.Min(start + total, _allSamples.Count);
            Bounds b = seg.Bounds;
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
                if (!kv.Value.FromWallCache)
                    BeginRefresh(kv.Value);
            }

            _censusFadeRenderers = 0;
            _censusAdopted = 0;
            // Kept for the doorway-assembly AABB sweep this rescan (CollectDoorwayAssembly)
            // so it never pays a second FindObjectsOfType walk per door.
            MeshRenderer[] all = _sceneRenderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            foreach (MeshRenderer r in all)
            {
                if (r == null || !RendererUsesWallFade(r))
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
                // DOORWAY override (user ruling): a fade renderer hugging a door prop is that
                // DOORWAY's frame/pillar — anchor it on the door root so every renderer of one
                // archway lands in ONE per-door segment whose fade the door state gates. This
                // outranks both the tile/parent grouping (the round-1 'L :' layer container
                // that mixed two doorways into one look-at segment) and the split routing (a
                // doorway is archway-sized, never a room-engulfing slab).
                Transform? doorRoot = FindDoorwayRoot(r);
                if (doorRoot != null)
                    anchor = doorRoot;
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
                if (seg.FromWallCache)
                    continue;
                FinishRefresh(seg);
                if (seg.Renderers.Count == 0)
                {
                    // Adopted group dissolved (renderers died / stopped matching) — its hidden
                    // attachments must not outlive it (restore-everywhere discipline).
                    RestoreSegmentFoliage(seg);
                    RestoreSegmentSiblings(seg);
                    RestoreDoorwayAssembly(seg);
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
                if (r == null || !RendererUsesFoliage(r))
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
        /// Is the doorway's door OPEN? Primary: the game's own rendering-true check —
        /// <c>MF.GameObjectAnimatorControllerIsCurrentState(root, "Open")</c>, exactly what
        /// <c>Choreographer.OpenDoor</c> plays and <c>UnityGameEditorDoorProp.OnCursorEnter</c>
        /// reads (the animator lives on the ApparanceLayer wings prefab under the prop root;
        /// the state is entered the moment the opening animation starts and never left).
        /// Fallback: the rules-side <c>CObjectDoor.DoorIsOpen</c> via the prop root's
        /// <c>UnityGameEditorObject.PropObject</c> (covers a mid-load animator not yet bound).
        /// Read-only on both paths; any throw (PathFinder mid-teardown) reads CLOSED — the
        /// fail-safe that holds the archway solid, the vanilla look.
        /// </summary>
        private static bool DoorIsOpen(Transform root)
        {
            try
            {
                if (MF.GameObjectAnimatorControllerIsCurrentState(root.gameObject, "Open"))
                    return true;
                UnityGameEditorObject? obj = root.GetComponent<UnityGameEditorObject>();
                return obj != null && obj.PropObject is ScenarioRuleLibrary.CObjectDoor door
                    && door.DoorIsOpen;
            }
            catch
            {
                return false; // unknown = closed = held solid (fail-safe, vanilla look)
            }
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
            _doorwaySeen.Clear();
            EnsureDoorwaySceneSweeps();
            foreach (Segment seg in _segments.Values)
            {
                if (seg.FromWallCache)
                    continue;
                seg.PrevSiblings.Clear();
                seg.PrevSiblings.AddRange(seg.Siblings);
                seg.Siblings.Clear();
                if (seg.DoorRoot != null)
                {
                    // DOORWAY segment (user ruling R3, torbogen3.png): the open-door case is
                    // a HARD HIDE of the complete assembly, collected by
                    // CollectDoorwayAssembly — the generic asset-sibling path plays no part.
                    // Anything the old sibling path still hides is restored right here
                    // (ownership handover; nothing may stay hidden without an owner).
                    if (seg.SiblingState != 0)
                        RestoreSegmentSiblings(seg);
                    seg.PrevSiblings.Clear();
                    CollectDoorwayAssembly(seg);
                    continue;
                }
                // Segment stopped being a doorway (door prop died / re-anchor handover):
                // restore and drop its old assembly — same no-orphan discipline.
                if (seg.DoorwayHideState != 0)
                    RestoreDoorwayAssembly(seg);
                seg.DoorwayRenderers.Clear();
                seg.DoorwayLights.Clear();
                seg.DoorwayParticles.Clear();
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
                    // Restore leavers NOW — nothing else ever points at them again.
                    foreach (MeshRenderer prev in seg.PrevSiblings)
                    {
                        if (prev != null && !seg.Siblings.Contains(prev) && !prev.enabled)
                            prev.enabled = true;
                    }
                    if (seg.Siblings.Count == 0)
                        seg.SiblingState = 0;
                }
                seg.PrevSiblings.Clear();
            }
        }

        /// <summary>Fetch the per-rescan light/particle sweeps — only when a doorway segment
        /// exists at all (the arrays feed nothing else, and most rescans track zero doorways).
        /// Renderers come from <see cref="AdoptShaderMatchedWalls"/>'s existing walk.</summary>
        private void EnsureDoorwaySceneSweeps()
        {
            _sceneLights = null;
            _sceneParticles = null;
            foreach (Segment seg in _segments.Values)
            {
                if (seg.FromWallCache || seg.DoorRoot == null)
                    continue;
                _sceneLights = UnityEngine.Object.FindObjectsOfType<Light>();
                _sceneParticles = UnityEngine.Object.FindObjectsOfType<ParticleSystem>();
                return;
            }
        }

        /// <summary>
        /// DOORWAY ASSEMBLY collection (user ruling R3, torbogen3.png — the round-2 hardware
        /// run left the wooden wings, two glow lights and a floating stone chunk behind
        /// because only shader-matched frame/pillar renderers were ever collected). Rebuilt
        /// every rescan, three sources:
        /// <list type="number">
        /// <item>The archway's own fade renderers (<see cref="Segment.Renderers"/>) — under
        ///   door=open they hard-hide with the assembly instead of MPB-fading.</item>
        /// <item>The door prop root's COMPLETE subtree: every <see cref="Renderer"/> of ANY
        ///   type/shader (the wings prefab instance ApparanceLayer.Create parents there,
        ///   its glow quads, trim, lock — the game's own hide unit: ApparanceLayer.Create /
        ///   UnityGameEditorRuntime.MakeDoor sweep exactly this subtree with
        ///   GetComponentsInChildren&lt;Renderer&gt; and toggle enabled), plus its
        ///   <see cref="Light"/>s and <see cref="ParticleSystem"/>s. No renderer-count cap
        ///   (the round-2 "+0 asset-sibling(s)" failure) and no tile/wall-logic exclusions:
        ///   everything under the prop root IS the door. Only actors are excluded
        ///   defensively, and game-disabled renderers stay the game's (unrevealed wings).</item>
        /// <item>A CONSERVATIVE AABB sweep for container-mates — the doorway entity
        ///   (ProceduralDoorway, read from source) generates non-WallFade wall-stub/stone
        ///   meshes and dressing (candles, banners) flat into the same 'L :' container as
        ///   the frames, NOT under the prop root; that is the floating stone chunk. Rule:
        ///   enabled, non-fade-capable, AABB WHOLLY inside the archway box expanded by
        ///   <see cref="DoorwayAssemblyMarginWU"/> (XZ + top), above the ground band (floor
        ///   never rides), no ProceduralWall/ActorBehaviour/TileBehaviour ancestry, and
        ///   provably the SAME CONTAINER (under the door root, sharing a fade renderer's
        ///   parent, or sharing its ProceduralMapTile ancestor). Whole-AABB containment plus
        ///   ancestry is what keeps neighbouring wall segments and floor out; every swept
        ///   inclusion is logged by name.</item>
        /// </list>
        /// Lights/particles are swept by position under the same container rule (the glows:
        /// prefab data is bundle-side, so whether each is a Light, a flame ParticleSystem or
        /// an emissive mesh is handled uniformly rather than assumed). Ownership/restore
        /// discipline mirrors the sibling path exactly: one owner per component, leavers
        /// restored on the spot, everything restored when the segment stops qualifying.
        /// </summary>
        private void CollectDoorwayAssembly(Segment seg)
        {
            bool wasHidden = seg.DoorwayHideState == 2;
            seg.PrevDoorwayRenderers.Clear();
            seg.PrevDoorwayRenderers.AddRange(seg.DoorwayRenderers);
            seg.DoorwayRenderers.Clear();
            seg.PrevDoorwayLights.Clear();
            seg.PrevDoorwayLights.AddRange(seg.DoorwayLights);
            seg.DoorwayLights.Clear();
            seg.PrevDoorwayParticles.Clear();
            seg.PrevDoorwayParticles.AddRange(seg.DoorwayParticles);
            seg.DoorwayParticles.Clear();
            seg.DoorwaySubtreeCount = 0;
            seg.DoorwaySweptCount = 0;
            seg.DoorwaySweptNames = string.Empty;

            Transform? door = seg.DoorRoot;
            // Fail-safe symmetry with the fade decision: without a trusted room plane the
            // segment is held solid anyway (RoomDecisionValid gate in Tick), so an empty
            // assembly is correct — and the ground exclusion below needs the plane.
            if (door != null && seg.HasBounds && RoomDecisionValid(seg.RoomIndex))
            {
                float ceiling = _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU;

                // (1) the archway's own fade renderers ride the hard hide.
                foreach (MeshRenderer r in seg.Renderers)
                {
                    if (r != null && _doorwaySeen.Add(r))
                        seg.DoorwayRenderers.Add(r);
                }

                // (2) the door prop's complete subtree — any Renderer type, plus lights
                // and particle systems.
                door.GetComponentsInChildren(includeInactive: false, _rendererScratch);
                foreach (Renderer c in _rendererScratch)
                {
                    if (c == null || _doorwaySeen.Contains(c))
                        continue;
                    // Game-disabled (MakeDoor/ApparanceLayer hide unrevealed doors this
                    // way) is not ours to manage — except a renderer WE hid last rescan.
                    if (!c.enabled && !(wasHidden && seg.PrevDoorwayRenderers.Contains(c)))
                        continue;
                    if (c.GetComponentInParent<ActorBehaviour>() != null)
                        continue; // never an actor, even one parented under the prop
                    _doorwaySeen.Add(c);
                    seg.DoorwayRenderers.Add(c);
                    seg.DoorwaySubtreeCount++;
                    if (c is MeshRenderer mc)
                        _siblingOwned.Add(mc); // one owner per renderer, assembly included
                }
                door.GetComponentsInChildren(includeInactive: false, _lightScratch);
                foreach (Light l in _lightScratch)
                {
                    if (l == null || _doorwaySeen.Contains(l))
                        continue;
                    if (!l.enabled && !(wasHidden && seg.PrevDoorwayLights.Contains(l)))
                        continue;
                    _doorwaySeen.Add(l);
                    seg.DoorwayLights.Add(l);
                }
                door.GetComponentsInChildren(includeInactive: false, _psScratch);
                foreach (ParticleSystem ps in _psScratch)
                {
                    if (ps == null || _doorwaySeen.Contains(ps))
                        continue;
                    if (!ps.isPlaying && !(wasHidden && seg.PrevDoorwayParticles.Contains(ps)))
                        continue;
                    _doorwaySeen.Add(ps);
                    seg.DoorwayParticles.Add(ps);
                }

                // (3) conservative AABB sweep for container-mates (the stone stubs).
                BuildContainerSets(seg);
                Bounds box = seg.Bounds;
                box.Expand(new Vector3(2f * DoorwayAssemblyMarginWU, 0f, 2f * DoorwayAssemblyMarginWU));
                float topCap = seg.Bounds.max.y + DoorwayAssemblyMarginWU;
                if (_sceneRenderers != null)
                {
                    foreach (MeshRenderer c in _sceneRenderers)
                    {
                        if (c == null || _doorwaySeen.Contains(c) || _siblingOwned.Contains(c))
                            continue;
                        if (!c.enabled && !(wasHidden && seg.PrevDoorwayRenderers.Contains(c)))
                            continue;
                        if (RendererUsesWallFade(c))
                            continue; // other segments' territory — never swept
                        Bounds cb = c.bounds;
                        if (cb.max.y <= ceiling || cb.max.y > topCap)
                            continue; // floor never rides; taller = neighbouring column
                        if (cb.min.x < box.min.x || cb.max.x > box.max.x
                            || cb.min.z < box.min.z || cb.max.z > box.max.z)
                            continue; // must sit WHOLLY inside the expanded archway box
                        if (c.GetComponentInParent<ProceduralWall>() != null
                            || c.GetComponentInParent<ActorBehaviour>() != null
                            || c.GetComponentInParent<TileBehaviour>() != null)
                            continue; // cache-wall territory / live game logic
                        if (!SameArchwayContainer(c.transform, door))
                            continue;
                        _doorwaySeen.Add(c);
                        _siblingOwned.Add(c);
                        seg.DoorwayRenderers.Add(c);
                        seg.DoorwaySweptCount++;
                        if (seg.DoorwaySweptCount <= 6)
                        {
                            seg.DoorwaySweptNames +=
                                (seg.DoorwaySweptNames.Length > 0 ? ", " : "")
                                + c.name + "@" + cb.max.y.ToString("F1");
                        }
                    }
                }
                if (_sceneLights != null)
                {
                    foreach (Light l in _sceneLights)
                    {
                        if (l == null || _doorwaySeen.Contains(l))
                            continue;
                        if (!l.enabled && !(wasHidden && seg.PrevDoorwayLights.Contains(l)))
                            continue;
                        Vector3 p = l.transform.position;
                        if (p.x < box.min.x || p.x > box.max.x
                            || p.z < box.min.z || p.z > box.max.z
                            || p.y <= ceiling || p.y > topCap)
                            continue;
                        if (l.GetComponentInParent<ActorBehaviour>() != null)
                            continue;
                        if (!SameArchwayContainer(l.transform, door))
                            continue;
                        _doorwaySeen.Add(l);
                        seg.DoorwayLights.Add(l);
                    }
                }
                if (_sceneParticles != null)
                {
                    foreach (ParticleSystem ps in _sceneParticles)
                    {
                        if (ps == null || _doorwaySeen.Contains(ps))
                            continue;
                        if (!ps.isPlaying
                            && !(wasHidden && seg.PrevDoorwayParticles.Contains(ps)))
                            continue;
                        Vector3 p = ps.transform.position;
                        if (p.x < box.min.x || p.x > box.max.x
                            || p.z < box.min.z || p.z > box.max.z
                            || p.y <= ceiling || p.y > topCap)
                            continue;
                        if (ps.GetComponentInParent<ActorBehaviour>() != null)
                            continue;
                        if (!SameArchwayContainer(ps.transform, door))
                            continue;
                        _doorwaySeen.Add(ps);
                        seg.DoorwayParticles.Add(ps);
                    }
                }
            }

            // Leaver restore (no-orphan discipline): anything hidden that did not make the
            // new set is restored NOW — nothing else ever points at it again.
            if (wasHidden)
            {
                foreach (Renderer prev in seg.PrevDoorwayRenderers)
                {
                    if (prev != null && !prev.enabled && !seg.DoorwayRenderers.Contains(prev))
                        prev.enabled = true;
                }
                foreach (Light prev in seg.PrevDoorwayLights)
                {
                    if (prev != null && !prev.enabled && !seg.DoorwayLights.Contains(prev))
                        prev.enabled = true;
                }
                foreach (ParticleSystem prev in seg.PrevDoorwayParticles)
                {
                    if (prev != null && !seg.DoorwayParticles.Contains(prev))
                        prev.Play(withChildren: false);
                }
                if (seg.DoorwayRenderers.Count == 0 && seg.DoorwayLights.Count == 0
                    && seg.DoorwayParticles.Count == 0)
                    seg.DoorwayHideState = 0;
            }
            seg.PrevDoorwayRenderers.Clear();
            seg.PrevDoorwayLights.Clear();
            seg.PrevDoorwayParticles.Clear();

            // Census log, change-triggered (the deliverable proving the AABB rule stayed
            // conservative and the assembly is complete BEFORE the next hardware round).
            int sig = seg.DoorwayRenderers.Count
                + 1000 * seg.DoorwaySubtreeCount
                + 100000 * seg.DoorwaySweptCount
                + 10000000 * seg.DoorwayLights.Count
                + 100000000 * seg.DoorwayParticles.Count;
            if (sig != seg.DoorwayLoggedSig)
            {
                seg.DoorwayLoggedSig = sig;
                string doorName = door != null ? door.name : "<dead>";
                VRLog.Info(Name,
                    $"doorway assembly '{doorName}': {seg.DoorwayRenderers.Count} renderer(s) "
                    + $"= {seg.Renderers.Count} archway fade + {seg.DoorwaySubtreeCount} "
                    + $"door-subtree (any shader, incl. wings) + {seg.DoorwaySweptCount} "
                    + $"AABB-swept container-mate(s)"
                    + (seg.DoorwaySweptCount > 0 ? $" [{seg.DoorwaySweptNames}]" : "")
                    + $"; {seg.DoorwayLights.Count} light(s), {seg.DoorwayParticles.Count} "
                    + $"particle system(s) — door={(seg.DoorOpen ? "open" : "closed")}; "
                    + $"swept rule: enabled, non-fade shader, AABB wholly inside archway box "
                    + $"+{DoorwayAssemblyMarginWU:0.0} wu, above ground band, same "
                    + "container/tile, no wall/actor/tile-logic ancestry.");
            }
        }

        /// <summary>Per-doorway "same container" evidence: the immediate parents and
        /// ProceduralMapTile ancestors of the segment's own fade renderers (see
        /// <see cref="CollectDoorwayAssembly"/> source (3)).</summary>
        private void BuildContainerSets(Segment seg)
        {
            _containerParents.Clear();
            _containerTiles.Clear();
            foreach (MeshRenderer r in seg.Renderers)
            {
                if (r == null)
                    continue;
                Transform t = r.transform;
                if (t.parent != null)
                    _containerParents.Add(t.parent);
                ProceduralMapTile? tile = r.GetComponentInParent<ProceduralMapTile>();
                if (tile != null)
                    _containerTiles.Add(tile.transform);
            }
        }

        /// <summary>Is this transform provably part of the SAME archway container as the
        /// segment (under the door prop root, flat beside a fade renderer, or under the same
        /// ProceduralMapTile)? The cross-hierarchy guard of the AABB sweep: mod rig objects,
        /// held cards/figures, UI panels and sky geometry can all transiently intersect the
        /// archway box but never share this ancestry.</summary>
        private bool SameArchwayContainer(Transform t, Transform door)
        {
            if (t.IsChildOf(door))
                return true;
            if (t.parent != null && _containerParents.Contains(t.parent))
                return true;
            ProceduralMapTile? tile = t.GetComponentInParent<ProceduralMapTile>();
            return tile != null && _containerTiles.Contains(tile.transform);
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
                sb.Append("FLOOR CENSUS room ").Append(r)
                  .Append(" center(").Append(center.x.ToString("F1")).Append(',')
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
        /// entity's IsPopulated/native-handle status. Decides in one log whether a missing
        /// room floor is a VISIBILITY failure (Preview stuck on) or a SYNTHESIS failure
        /// (visibility All, generation root empty — the parked-viewpoint reveal bug).
        /// </summary>
        private void LogMapTileCensus(System.Text.StringBuilder sb)
        {
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
                          .Append(" handle=").Append(entity.m_EntityHandle != 0 ? "built" : "NONE");
                }
                catch { sb.Append(" entity=?"); }
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
                    // StripGroundRenderers, exactly like ground geometry.
                    if (RendererUsesFoliage(r))
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
                RestoreSegmentFoliage(seg);
                RestoreSegmentSiblings(seg);
                RestoreDoorwayAssembly(seg);
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
            _sceneRenderers = null; // release the per-rescan scene sweeps (doorway assembly)
            _sceneLights = null;
            _sceneParticles = null;
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
