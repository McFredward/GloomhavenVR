using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// What a peer's control board does while it stands between this viewer and the play field.
/// <see cref="Off"/> is the shipped behaviour, bit for bit: nothing is measured, nothing is
/// written, no material is touched.
/// </summary>
internal enum PeerBoardFadeMode
{
    /// <summary>Never yield. A peer's board renders exactly as it does today (default).</summary>
    Off = 0,

    /// <summary>Fade to <see cref="PeerBoardFadeTuning.Alpha"/> while the board occludes the
    /// play field, and back to solid when it stops.</summary>
    Transparent = 1,

    /// <summary>Disappear entirely while the board occludes the play field (the same decision,
    /// the same hysteresis, target alpha 0).</summary>
    Hidden = 2,
}

/// <summary>
/// Live-tunable decision thresholds for the peer-board see-through (canonical
/// <see cref="ModuleConfig.Create"/> pattern — <c>dev.gloomhavenvr.boardfade.cfg</c>), modelled
/// one for one on <c>WallFadeTuning</c>: the two Schmitt bars and the two un-fade dwells are the
/// values that needed hardware iteration for the WALLS, so they are config here from the start,
/// re-read through clamped accessors on EVERY evaluation tick. The remaining constants (the EMA
/// tau, the fade tau, the sample-grid geometry) stay code-owned — they were stable across every
/// wall round and there is no reason to believe boards are different.
/// </summary>
internal static class PeerBoardFadeTuning
{
    private static ConfigFile? _file;

    /// <summary>Off (shipped behaviour) / Transparent / Hidden — see <see cref="PeerBoardFadeMode"/>.</summary>
    internal static ConfigEntry<PeerBoardFadeMode>? FadeMode;
    /// <summary>Residual opacity of an occluding board in <see cref="PeerBoardFadeMode.Transparent"/>.</summary>
    internal static ConfigEntry<float>? OccludedAlpha;
    /// <summary>Smoothed view-coverage fraction at/above which a board yields (Schmitt high bar).</summary>
    internal static ConfigEntry<float>? OnFraction;
    /// <summary>Schmitt low bar: once yielded, it stays yielded while the fraction is at/above this.</summary>
    internal static ConfigEntry<float>? OffFraction;
    /// <summary>Seconds continuously below the low bar before coming back after a perspective change.</summary>
    internal static ConfigEntry<float>? ExitDwellMoved;
    /// <summary>Come-back dwell while the head has only ROTATED (no recent translation/recenter).</summary>
    internal static ConfigEntry<float>? ExitDwellStationary;

    internal static void Bind()
    {
        if (_file != null)
            return;
        ConfigFile config = _file = ModuleConfig.Create("boardfade");
        FadeMode = config.Bind("PeerBoardFade", "Mode", PeerBoardFadeMode.Off,
            "What a MITSPIELER's control board does while it stands between you and the play " +
            "field. Off = today's behaviour (nothing is measured or written). Transparent = it " +
            "fades to OccludedAlpha while it hides part of the board you are looking at. Hidden " +
            "= it disappears for as long as it does. PURELY LOCAL: the owner and every other " +
            "player still see their board exactly as before, and nothing goes on the wire. " +
            "Composes UNDER the [Net] RemoteBoards mode: this can only ever make a board that " +
            "mode already draws LESS visible, never more. Your OWN board is never affected.");
        OccludedAlpha = config.Bind("PeerBoardFade", "OccludedAlpha", 0.25f,
            "Residual opacity of an occluding peer board in Transparent mode: 0 = invisible " +
            "(same as Hidden), 1 = solid (same as Off). Live; clamped 0-0.95.");
        OnFraction = config.Bind("PeerBoardFade", "OnFraction", 0.12f,
            "A peer board yields when it hides at least this (EMA-smoothed) fraction of the " +
            "play-field sample points currently IN YOUR VIEW — 0.12 = the board covers an " +
            "eighth of the map you are looking at (Schmitt trigger high bar). Live; clamped " +
            "0.02-0.95.");
        OffFraction = config.Bind("PeerBoardFade", "OffFraction", 0.05f,
            "Once yielded, the board stays yielded while the smoothed coverage fraction stays " +
            "at or above this (Schmitt trigger low bar). Live; clamped 0.01-0.95 and never " +
            "above OnFraction.");
        ExitDwellMoved = config.Bind("PeerBoardFade", "ExitDwellMovedSeconds", 2.5f,
            "Seconds the coverage must stay below OffFraction before the board comes back when " +
            "the PERSPECTIVE recently changed (real head translation / rig recenter / the owner " +
            "moving their board). Live.");
        ExitDwellStationary = config.Bind("PeerBoardFade", "ExitDwellStationarySeconds", 7f,
            "Come-back dwell while the head has only ROTATED recently — rotation alone should " +
            "almost never bring a board back. Live; never below ExitDwellMovedSeconds.");
    }

    // Clamped live accessors — safe before Bind() (fall back to the shipped defaults, i.e. OFF).
    internal static PeerBoardFadeMode Mode => FadeMode == null ? PeerBoardFadeMode.Off : FadeMode.Value;
    internal static float Alpha => Mode == PeerBoardFadeMode.Hidden
        ? 0f
        : Clamped(OccludedAlpha, 0.25f, 0f, 0.95f);
    internal static float On => Clamped(OnFraction, 0.12f, 0.02f, 0.95f);
    internal static float Off => Mathf.Min(Clamped(OffFraction, 0.05f, 0.01f, 0.95f), On);
    internal static float DwellMoved => Clamped(ExitDwellMoved, 2.5f, 0.1f, 60f);
    internal static float DwellStationary =>
        Mathf.Max(Clamped(ExitDwellStationary, 7f, 0.1f, 120f), DwellMoved);

    private static float Clamped(ConfigEntry<float>? entry, float fallback, float min, float max) =>
        entry == null ? fallback : Mathf.Clamp(entry.Value, min, max);
}

/// <summary>
/// THE PLAY FIELD, AS A HANDFUL OF POINTS — the denominator every peer board is measured
/// against, built once per rescan and frustum-tested once per FRAME for all of them together.
///
/// <para>WHY A SAMPLE GRID AND NOT A SCREEN-SPACE FOOTPRINT TEST. The wall fade answers exactly
/// the same question ("does this thing hide the floor I am looking at?") by shooting the head→
/// floor-sample segments at the occluder and counting hits, and its whole hysteresis design — an
/// EMA over a FRACTION, a Schmitt trigger on that fraction, second-scale dwells — is built on a
/// scalar coverage number. A screen-rect overlap test answers a different, cruder question (it
/// cannot tell a board that is BEHIND the map from one in front of it, and it has no notion of
/// "how much"), and it would have needed its own hysteresis story. Reusing the wall's metric
/// means reusing the wall's proven thresholds.</para>
///
/// <para>SOURCE. <c>SceneRegistry.MapTiles</c> — the self-maintaining registry of every active
/// <c>ProceduralMapTile</c> that already replaced the mod's periodic
/// <c>FindObjectsOfType</c> sweeps, so a rescan here is a walk of ~10 entries and never a heap
/// scan. Each tile contributes a small grid over its own collider footprint, seated on the TOP
/// of that footprint (the tile plane the figures stand on), capped at
/// <see cref="MaxSamples"/> points over the whole map. Outside a scenario (menus, the void) the
/// registry is empty, the sample list is empty, and every board's coverage is 0 — nothing
/// fades, which is the correct answer where there is no play field.</para>
/// </summary>
internal static class PeerBoardPlayArea
{
    /// <summary>Total sample budget over the WHOLE map. The wall fade's own budget (96) for the
    /// same kind of grid; the cost that matters is one <c>WorldToViewportPoint</c> each, once per
    /// frame for every board together.</summary>
    private const int MaxSamples = 96;
    /// <summary>World units above the tile's own top surface, so a sample is never swallowed by
    /// the floor mesh it sits on (the wall fade's <c>FloorSampleEpsilon</c>).</summary>
    private const float FloorSampleEpsilon = 0.05f;
    /// <summary>Viewport slack on the frustum test — also covers the mono-vs-per-eye skew, which
    /// is the same 0.20 the wall fade uses for the same reason.</summary>
    private const float FrustumMargin = 0.20f;
    private const float RescanSeconds = 2f;
    /// <summary>Frustum-test cadence. The answer feeds a decision that is deliberately slow (an
    /// EMA, a Schmitt trigger and second-scale dwells), so sampling it at 20 Hz instead of 90 Hz
    /// cannot change WHICH boards fade — only when, within a twentieth of a dwell. It is the same
    /// argument (and the same conclusion) as <c>PerfConfig.WallFadeInterval</c>, taken as the
    /// default here because this runs once per PEER rather than once for the whole scene.</summary>
    internal const float EvalIntervalSeconds = 0.05f;

    private static readonly List<Vector3> Samples = new(MaxSamples);
    private static readonly bool[] Visible = new bool[MaxSamples];
    private static readonly List<ProceduralMapTile> TileScratch = new(32);
    private static int _visibleCount;
    private static int _frame = -1;
    private static float _nextRescan;
    private static float _nextVisibility;
    private static int _loggedSamples = -1;

    /// <summary>How many play-field samples exist at all (0 = no map: nothing may fade).</summary>
    internal static int Count => Samples.Count;
    /// <summary>How many of them are in the head frustum this frame (the fraction's denominator).</summary>
    internal static int VisibleCount => _visibleCount;
    internal static bool IsVisible(int i) => Visible[i];
    internal static Vector3 Sample(int i) => Samples[i];

    /// <summary>
    /// Rebuild the grid on its slow cadence and re-run the frustum test — at most ONCE per frame
    /// no matter how many peer boards ask, which is what keeps the shared half of this feature
    /// O(1) in the number of peers.
    /// </summary>
    internal static void EnsureFresh(Camera head, float now)
    {
        if (_frame == Time.frameCount || now < _nextVisibility)
            return;
        _frame = Time.frameCount;
        _nextVisibility = now + EvalIntervalSeconds;
        if (now >= _nextRescan)
        {
            _nextRescan = now + RescanSeconds;
            Rebuild();
        }
        UpdateVisibility(head);
    }

    private static void Rebuild()
    {
        Samples.Clear();
        TileScratch.Clear();
        SceneRegistry.MapTiles.Collect(TileScratch);
        int tiles = TileScratch.Count;
        if (tiles == 0)
        {
            LogCensusIfChanged(0);
            return;
        }

        // Split the budget over the tiles, as a square grid per tile (3×3 / 2×2 / centre only).
        // A hex map tile is a room piece several hexes across, so one point per tile would be a
        // caricature of the floor; 3×3 resolves "the board hides the left half of this room".
        int perTile = Mathf.Clamp(MaxSamples / tiles, 1, 9);
        int side = perTile >= 9 ? 3 : perTile >= 4 ? 2 : 1;
        for (int t = 0; t < tiles && Samples.Count < MaxSamples; t++)
        {
            ProceduralMapTile tile = TileScratch[t];
            if (tile == null)
                continue;
            BoxCollider? box = tile.BoxCollider;
            // The same bounds ladder ApparanceDetailFocus uses for the same objects: the tile's
            // own box when it has one, its position with a generous default footprint when it
            // does not (world units — a map tile is ~20 wu across).
            Bounds b = box != null
                ? box.bounds
                : new Bounds(tile.transform.position, new Vector3(20f, 4f, 20f));
            float y = b.max.y + FloorSampleEpsilon;
            for (int ix = 0; ix < side && Samples.Count < MaxSamples; ix++)
            {
                for (int iz = 0; iz < side && Samples.Count < MaxSamples; iz++)
                {
                    float fx = (ix + 0.5f) / side;
                    float fz = (iz + 0.5f) / side;
                    Samples.Add(new Vector3(Mathf.Lerp(b.min.x, b.max.x, fx), y,
                                            Mathf.Lerp(b.min.z, b.max.z, fz)));
                }
            }
        }
        LogCensusIfChanged(Samples.Count);
    }

    private static void UpdateVisibility(Camera head)
    {
        _visibleCount = 0;
        for (int i = 0; i < Samples.Count; i++)
        {
            // Mono view/projection of the head camera. Under MultiPass the two eye frusta differ
            // by half the IPD and a little horizontal skew; the 0.20 viewport margin covers that
            // generously, and — this is the point — it is ONE answer used by BOTH eyes, so the
            // decision it feeds cannot differ between them (see the stereo note on PeerBoardFade).
            Vector3 vp = head.WorldToViewportPoint(Samples[i]);
            bool vis = vp.z > 0f
                       && vp.x > -FrustumMargin && vp.x < 1f + FrustumMargin
                       && vp.y > -FrustumMargin && vp.y < 1f + FrustumMargin;
            Visible[i] = vis;
            if (vis)
                _visibleCount++;
        }
    }

    /// <summary>One line per real change of the grid census — the evidence that the metric has a
    /// denominator at all. A "0 sample(s)" line is the reading that explains a board which never
    /// fades: no map tiles were registered, so nothing could be judged.</summary>
    private static void LogCensusIfChanged(int count)
    {
        if (count == _loggedSamples)
            return;
        _loggedSamples = count;
        VRLog.Info("Net", $"Peer-board see-through: play-field grid rebuilt — {count} sample " +
                          $"point(s) over {TileScratch.Count} active map tile(s). This is the " +
                          "denominator every peer board's coverage fraction is measured against; " +
                          "0 means no map is loaded and no board can ever be judged occluding.");
    }
}

/// <summary>
/// USER REQUEST 15 (2026-08): "…möchte ich auch dass man zusätzlich einstellen kann, dass die
/// Boards transparent werden oder verschwinden wenn sie Teile des Spielfeldes verdecken aus der
/// aktuellen View. Das soll rein lokal sein. Ich möchte hier mit einer Gleichen oder ähnlichen
/// Logik arbeiten wie es bei den Wänden bereits der Fall ist."
///
/// <para>ONE OF THESE RIDES ON EVERY PEER BOARD ROOT (attached by
/// <see cref="RemoteBoardFurniture"/>, which is the one per-board constructor that is handed the
/// board root). It measures — from THIS viewer's head, every frame — how much of the play field
/// the board it sits on is hiding, debounces that exactly the way the wall fade debounces a wall,
/// and drives the whole board's opacity. Purely local: no wire byte, no game state, no effect on
/// the owner or on any other viewer. The board's own pose, content and draw order are untouched.</para>
///
/// <para><b>WHAT WAS REUSED FROM <c>WallSegmentFade</c>, AND WHAT COULD NOT BE.</b> Reused
/// verbatim in shape and in numbers: the coverage metric (fraction of the head-visible floor
/// samples whose head→sample segment the occluder interrupts), the EMA over that fraction
/// (tau 0.15 s), the Schmitt trigger on the smoothed value, the short enter dwell (0.20 s) and
/// the two long exit dwells (2.5 s after a perspective change, 7 s while merely rotating), the
/// perspective-change arming (0.18 REAL tracking metres of head translation, 3 s arm window,
/// plus the rig pose version), and the critically-damped exponential ramp (tau 0.12 s) toward the
/// debounced state. Not reusable: the wall's OCCLUDER PROXY (a world-axis AABB) and its DELIVERY
/// (the game masonry shader's own <c>_Cutoff</c> discard). A control board is a thin slab at an
/// arbitrary attitude — its world AABB is mostly air, and a board seen edge-on would measure as a
/// wall-sized blocker — so the proxy here is the board's own BOARD-LOCAL box, which is an exact
/// oriented box test for a few more flops. And nothing on a peer board runs a fade shader, so the
/// delivery is the mod's own (below).</para>
///
/// <para><b>WHY THIS CANNOT REPRODUCE THE PARKED ONE-EYED WALL FADE</b>
/// (<c>.planning/wall-fade-stereo-rivalry.md</c>, user ruling: "das darf niemals passieren.
/// Entweder faded es auf beiden Augen oder gar nicht"). That defect is structural to the game's
/// masonry shader: its discard scalar contains a screen-radial term measured from EACH EYE'S OWN
/// screen centre, raised to the 8th power, so under MultiPass the two eyes evaluate different
/// discard conditions per fragment. Every decision in this class is a CPU scalar computed ONCE
/// per frame from the MONO head camera (position, and one shared frustum answer for the sample
/// grid), and it is delivered as a UNIFORM per-renderer alpha — one number for the whole surface,
/// identical in both stereo passes — or as a whole-renderer cull, which is equally per-eye
/// identical. There is no per-fragment, view-dependent term anywhere in the chain, and no
/// screen-space dissolve. The one thing that would reintroduce the defect — a screen-space
/// dither/dissolve pattern measured from the eye centre — is deliberately not used.</para>
///
/// <para><b>DELIVERY, AND THE HONEST LIMIT OF "TRANSPARENT".</b> A peer board is three families
/// of surface and each takes alpha differently:</para>
/// <list type="bullet">
/// <item>UI graphics (every mirrored widget, every TMP label on a canvas, the hosted card faces)
///   — ONE <see cref="CanvasGroup"/> on the board root. Its alpha multiplies down through nested
///   canvases, so it never touches the mirrors' OWN CanvasGroups (which
///   <c>RemoteWidgetMirror</c> drives from the source widget) and cannot fight them.</item>
/// <item>Renderers whose material can already blend (the Sprites/Default quads, the glows, the
///   3D TMP labels) — a <see cref="MaterialPropertyBlock"/> carrying the material's OWN live
///   colour with its alpha scaled. The material is never modified, so the per-frame colour
///   writers on this board (the cap press paint, the glow pulse) keep working and compose with
///   the fade instead of being frozen by it.</item>
/// <item>Renderers whose material CANNOT blend — and that is the important one: the real board
///   slab, the keycaps, the handle bars and the status plates all run the bundled
///   <c>GloomhavenVR/BoardLit</c>, an OPAQUE shader that exposes no <c>_Mode</c>, no
///   <c>_Surface</c>, no <c>_SrcBlend</c>/<c>_DstBlend</c> and no <c>_ZWrite</c> (measured, not
///   assumed — <c>HandGhost.MakeTransparent</c> documents the same finding from the hardware log
///   that made the ghost hand invisible-by-no-op). Writing alpha into such a material is a
///   guaranteed silent no-op. So a private CLONE of the material gets an unlit alpha-blended
///   shader (Sprites/Default, carrying <c>_MainTex</c> and tint across) for exactly as long as
///   the board is yielding, and the original shared material is put back on release. This is the
///   <c>HandGhost</c> recipe, and it is the only way "transparent" can mean transparent for the
///   slab rather than quietly meaning "hidden".</item>
/// </list>
/// <para>RESIDUE, STATED: while a board is swapped, the ordinary material writers on the swapped
/// surfaces (a style re-tint, a cap state colour) write to the material that is currently off the
/// renderer, so such a change appears when the board comes back rather than during the fade. Only
/// the swapped (opaque, unblendable) family is affected — the animated surfaces (glow pulse, cap
/// press) are in the MPB family, which composes live. If <c>Sprites/Default</c> cannot be found
/// at all, the unblendable family is CULLED past the half-way point of the ramp instead — it
/// pops, it is per-eye identical, and it is a fail-safe that has never been observed.</para>
///
/// <para>SECOND RESIDUE, AND IT IS A LOOK CHANGE, STATED BEFORE ANYONE REPORTS IT: the swapped
/// clone is UNLIT, so for the duration of the fade the slab and the keycaps lose their bevel
/// shading and read as flat albedo. The swap happens on the first frame of the ramp, i.e. while
/// the board is still ~100% opaque, so the flattening is visible for a moment before the fade
/// covers it. This is INFERRED, not measured — I have no headset here. It is also unavoidable on
/// this shader: BoardLit exists precisely because Gloomhaven's scenario/void scenes carry no
/// lights (a Standard "Fade" material would render the board black), so the alpha-capable target
/// has to be an unlit one. The alternative is not a prettier fade, it is no fade at all for the
/// slab — i.e. "Transparent" quietly meaning "Hidden", which is the outcome this class exists to
/// avoid. If the flash is judged worse than the flattening, the honest knobs are: use
/// <see cref="PeerBoardFadeMode.Hidden"/> (no partial state to look at), or delay the swap to a
/// lower alpha at the cost of the slab holding solid through the first part of the ramp.</para>
///
/// <para>PER-FRAME COST (arithmetic, not a hardware measurement — the numbers to check against
/// the next <c>[Perf]</c> capture). Shared, once per frame for ALL peers: up to 96
/// <c>WorldToViewportPoint</c> calls, and those are gated to 20 Hz. Per peer per DECISION tick
/// (also 20 Hz): one <c>InverseTransformPoint</c> plus, for each in-view sample, one
/// <c>MultiplyPoint3x4</c> and one <c>Bounds.IntersectRay</c> — ~96 × ~40 flops ≈ a few
/// microseconds. Per peer per FRAME: the exponential ramp (three flops) and, only while the board
/// is actually faded, one property-block write per renderer (~60 renderers on a full board). Per
/// peer every 0.5 s: one <c>GetComponentsInChildren</c> over the board plus eight corner
/// transforms per active renderer. With the mode Off — the default — every one of those is
/// skipped by the first statement of <see cref="LateUpdate"/>.</para>
///
/// <para><b>HOW IT COMPOSES WITH THE EXISTING DISPLAY MODES.</b> The rule is one sentence: the
/// [Net] <c>RemoteBoards</c> mode decides WHETHER a peer's board is drawn at all, and this
/// setting decides only HOW MUCH of an already-drawn board you see. It is strictly subtractive
/// and can never make a hidden board appear. The mechanism is structural rather than a rule
/// somebody has to remember: <c>RemoteControlBoard</c> deactivates the board ROOT when its mode
/// says hide, and a component on a deactivated root does not tick — so with "Aus" this class
/// never runs, and with "Aktionsphase" it runs exactly during the phases the board is shown and
/// restores itself the moment the gate shuts (<see cref="OnDisable"/>).</para>
///
/// <para><b>YOUR OWN BOARD IS EXEMPT, structurally: this component only ever exists on a
/// <see cref="RemoteControlBoard"/> root.</b> That is deliberate and it is what the user asked
/// for — the reported problem is "die Boards der Mitspieler verdecken die Sicht und ich muss sie
/// bitten, ihr Board zu verschieben", i.e. precisely the boards you cannot move. Your own board
/// is where you placed it, it is the surface you reach into and read continuously, and it is one
/// grab away from moving; fading it out from under your own hands would be a different feature
/// with a different failure mode.</para>
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY rendering, zero wire — a local viewing preference over
/// surfaces that are already local copies. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class PeerBoardFade : MonoBehaviour
{
    // ---- decision constants (the wall fade's, with its own justification) ---------------------
    /// <summary>Short fade-OUT prompt dwell — <c>WallSegmentFade.EnterDwellSeconds</c>.</summary>
    private const float EnterDwellSeconds = 0.20f;
    /// <summary>REAL tracking-space metres of head translation that count as "the perspective
    /// changed" (scale-independent — the rig root's own scale is divided out).</summary>
    private const float HeadMoveReevalMeters = 0.18f;
    /// <summary>How long a perspective change keeps the short exit dwell armed.</summary>
    private const float ReevalArmSeconds = 3f;
    /// <summary>Exponential fade time constant (~0.35 s to 95%) — the wall's.</summary>
    private const float FadeTauSeconds = 0.12f;
    /// <summary>EMA over the raw coverage fraction (the jitter killer) — the wall's.</summary>
    private const float FractionTauSeconds = 0.15f;
    /// <summary>How often the board's surface census and occluder box are rebuilt. A peer board
    /// grows and loses surfaces all session long (cards, chips, cloned widgets), so a one-shot
    /// scan would fade the board it was built with and nothing that came after — the same reason
    /// <c>BoardVisual.AdoptBoardOrder</c> runs on a cadence.</summary>
    private const float SurfaceScanSeconds = 0.5f;
    private const float DiagIntervalSeconds = 2f;
    /// <summary>Below this effective alpha the whole board is culled rather than drawn: at 1% the
    /// surface contributes nothing but overdraw, and a cull is the cheapest possible delivery.</summary>
    private const float CullAlpha = 0.01f;
    /// <summary>Segment-length fraction by which the board must be IN FRONT of a sample before it
    /// counts as blocking it — the analogue of the wall's <c>BlockEpsDistFraction</c>. Keeps a
    /// board lying essentially ON the sample from claiming it.</summary>
    private const float BlockEpsFraction = 0.02f;

    /// <summary>Colour properties an alpha write is attempted on, in this order. <c>_Color</c>
    /// covers Sprites/Default, Standard and BoardLit's clone; <c>_BaseColor</c> the URP-shaped
    /// materials; <c>_TintColor</c> the particle/additive glows; <c>_FaceColor</c> the 3D TMP SDF
    /// labels, whose fill alpha lives nowhere else.</summary>
    private static readonly int[] ColorIds =
    {
        Shader.PropertyToID("_Color"),
        Shader.PropertyToID("_BaseColor"),
        Shader.PropertyToID("_TintColor"),
        Shader.PropertyToID("_FaceColor"),
    };

    private static readonly int ModeId = Shader.PropertyToID("_Mode");
    private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
    private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

    /// <summary>Name marker on every material clone this class installs. Read by
    /// <c>BoardVisual.AdoptBoardOrder</c>, which must NOT adopt a surface that is only
    /// transparent because it is currently fading (see the note there).</summary>
    internal const string CloneMarker = " (PeerBoardFade)";

    /// <summary>Queue a swapped-in clone draws at: the standard transparent tier. It keeps
    /// sortingOrder 0 while every other transparent surface on the board rides the furniture
    /// cluster at order ≥ 100, so the slab draws UNDER its own content, which is the only
    /// stacking that can look right.</summary>
    private const int SwapRenderQueue = 3000;

    private sealed class Surface
    {
        public Renderer Renderer = null!;
        /// <summary>True when every material on this renderer can blend as authored (no swap
        /// needed) — the MPB family.</summary>
        public bool Blendable;
        /// <summary>The renderer's materials as we found them; non-null only while swapped.</summary>
        public Material[]? Original;
        /// <summary>Our private clones, parallel to <see cref="Original"/>; entries that needed
        /// no clone are the original material itself and are never destroyed.</summary>
        public Material[]? Installed;
        public bool Seen;
    }

    private readonly List<Surface> _surfaces = new(64);
    private readonly Dictionary<Renderer, Surface> _known = new(64);
    private readonly List<Renderer> _rendererScratch = new(64);
    private MaterialPropertyBlock? _mpb;
    private CanvasGroup? _group;

    private Bounds _localBox;
    private bool _hasBox;
    private float _nextSurfaceScan;

    // --- decision state (the wall's Segment fields, one board's worth) ---
    private float _smooth;
    private bool _smoothInit;
    private bool _pendingRaw;
    private float _pendingSince;
    private bool _state;
    private float _fade;
    private bool _engaged;
    private float _nextEvalTime;
    private float _lastEvalTime;

    // --- perspective tracking ---
    private Vector3 _lastHeadTrack;
    private bool _headInit;
    private float _lastReevalTime = -999f;
    private int _lastPoseVersion = -1;
    private Vector3 _lastBoardPos;
    private Quaternion _lastBoardRot = Quaternion.identity;
    private bool _boardPoseInit;

    // --- diagnostics ---
    private int _playerId = -1;
    private float _nextDiag;
    private int _lastBlocked;
    private int _lastVisible;
    private float _lastRaw;
    private bool _loggedState;
    /// <summary>Seeded TRUE (state = solid) so only a REAL flip ever writes a line: a board that
    /// comes and goes with the action-phase gate would otherwise announce "OFF" on every reveal.</summary>
    private bool _loggedStateInit = true;
    private int _loggedSurfaces = -1;

    /// <summary>
    /// Ensure the board rooted at <paramref name="boardRoot"/> carries the see-through driver.
    /// Idempotent. Binds the module config on the way through, so the settings rows exist as soon
    /// as the first peer board does.
    /// </summary>
    internal static PeerBoardFade? Attach(Transform? boardRoot)
    {
        if (boardRoot == null)
            return null;
        PeerBoardFadeTuning.Bind();
        PeerBoardFade? existing = boardRoot.gameObject.GetComponent<PeerBoardFade>();
        return existing != null ? existing : boardRoot.gameObject.AddComponent<PeerBoardFade>();
    }

    /// <summary>The owning peer, for the diagnostic line only. Handed in from the board's own
    /// refresh (the constructor does not know it yet).</summary>
    internal void Note(int playerId) => _playerId = playerId;

    private void LateUpdate()
    {
        PeerBoardFadeMode mode = PeerBoardFadeTuning.Mode;
        if (mode == PeerBoardFadeMode.Off)
        {
            // OFF IS BIT FOR BIT TODAY'S BEHAVIOUR: everything this class ever wrote is put back
            // on the first tick after the switch, and from then on the body below never runs.
            if (_engaged || _fade > 0f)
                ResetToSolid();
            return;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (_engaged || _fade > 0f)
                ResetToSolid();
            return;
        }

        float now = Time.unscaledTime;
        PeerBoardPlayArea.EnsureFresh(head, now);
        UpdatePerspectiveState(now);
        if (now >= _nextSurfaceScan)
        {
            _nextSurfaceScan = now + SurfaceScanSeconds;
            RefreshSurfaces();
        }

        // ---- the decision, in the wall's own order: raw fraction → EMA → Schmitt → dwell ------
        // THE DECISION IS GATED, THE RAMP IS NOT (the wall's split, and for its reason): the
        // expensive half is the per-sample geometry, which is head-motion correlated; the cheap
        // half is the exponential ramp plus its writes, which must stay per-frame or the fade
        // would visibly step. The EMA advances by the time since the last EVALUATION, never since
        // the last frame — otherwise the cadence would silently stretch its time constant and
        // change WHICH boards fade, which is exactly what it must not do.
        bool evaluate = now >= _nextEvalTime;
        if (evaluate)
        {
            _nextEvalTime = now + PeerBoardPlayArea.EvalIntervalSeconds;
            float fraction = BlockedFraction(head.transform.position);
            _lastRaw = fraction;
            float evalDt = _lastEvalTime > 0f
                ? Mathf.Min(now - _lastEvalTime, 0.5f)
                : Time.unscaledDeltaTime;
            _lastEvalTime = now;
            float fracStep = 1f - Mathf.Exp(-evalDt / FractionTauSeconds);
            if (!_smoothInit)
            {
                _smoothInit = true;
                _smooth = fraction;
            }
            else
            {
                _smooth += (fraction - _smooth) * fracStep;
            }
        }

        float onFraction = PeerBoardFadeTuning.On;
        float offFraction = PeerBoardFadeTuning.Off;
        bool reevalArmed = now - _lastReevalTime <= ReevalArmSeconds;
        float exitDwell = reevalArmed
            ? PeerBoardFadeTuning.DwellMoved
            : PeerBoardFadeTuning.DwellStationary;

        bool raw = _smooth >= (_state ? offFraction : onFraction);
        if (raw != _pendingRaw)
        {
            _pendingRaw = raw;
            _pendingSince = now;
        }
        if (_pendingRaw != _state)
        {
            float dwell = _pendingRaw ? EnterDwellSeconds : exitDwell;
            if (now - _pendingSince >= dwell)
                _state = _pendingRaw;
        }

        // Critically-damped-style exponential ramp toward the debounced state — the wall's, and
        // the reason a board never snaps even when the decision does.
        float fadeStep = 1f - Mathf.Exp(-Time.unscaledDeltaTime / FadeTauSeconds);
        float target = _state ? 1f : 0f;
        _fade += (target - _fade) * fadeStep;
        if (Mathf.Abs(target - _fade) < 0.005f)
            _fade = target;

        float alpha = Mathf.Lerp(1f, Mathf.Clamp01(PeerBoardFadeTuning.Alpha), _fade);
        Apply(alpha);
        LogStateIfChanged(alpha, onFraction, offFraction, exitDwell, reevalArmed);
        if (now >= _nextDiag && !PerfConfig.Quiet && (_engaged || _state))
        {
            _nextDiag = now + DiagIntervalSeconds;
            LogDiagnostic(alpha, onFraction, offFraction, reevalArmed);
        }
    }

    /// <summary>
    /// The board root going inactive is the [Net] RemoteBoards gate shutting (mode Off, or the
    /// secret selection phase under "Aktionsphase") — or the board being torn down for a rebuild.
    /// Both must leave the board exactly as it was found, and must forget the decision: a board
    /// that comes back is judged fresh against the view it comes back into, never against the one
    /// it left.
    /// </summary>
    private void OnDisable() => ResetToSolid();

    private void OnDestroy() => Release();

    private void ResetToSolid()
    {
        Release();
        _fade = 0f;
        _state = false;
        _pendingRaw = false;
        _smooth = 0f;
        _smoothInit = false;
        _lastEvalTime = 0f;
        _nextEvalTime = 0f;
        _loggedStateInit = true;
        _loggedState = false;
    }

    // ------------------------------------------------------------------ the coverage metric ----

    /// <summary>
    /// Fraction of the play-field samples IN VIEW whose head→sample segment this board's own
    /// oriented box interrupts.
    ///
    /// <para>WHY THE DENOMINATOR IS THE IN-VIEW SET AND NOT THE WHOLE MAP (the one deliberate
    /// departure from <c>WallSegmentFade</c>, which divides by its room's WHOLE grid). A wall
    /// belongs to a room and is judged against that room; a peer's board belongs to nothing and
    /// floats anywhere. Measured against the whole map, a board could hide every hex you are
    /// actually looking at and still score a few percent — the metric would be dominated by parts
    /// of the map behind you. "How much of what I am looking at is this board eating" is the
    /// question the user asked, and it is the only one whose answer is stable when you turn.</para>
    ///
    /// <para>AWKWARD CASES, and what this returns for them. EDGE-ON: the box is a few centimetres
    /// thick in board-local Z, so a board seen edge-on intersects almost no segments and scores
    /// ~0 — it does not fade, which is right, because edge-on it is not hiding anything. (A
    /// world-axis AABB, the wall's proxy, would have scored it like a wall — that is the reason
    /// for the local-space test.) BEHIND THE MAP: the hit must land strictly between the eye and
    /// the sample, so a board on the far side of the hexes it appears to overlap blocks nothing.
    /// BETWEEN TWO PLAYERS: irrelevant by construction — the only viewpoint that exists here is
    /// this client's head, and the answer is computed independently on every machine. IN YOUR
    /// FACE (the eye inside the box): full coverage, exactly like the wall.</para>
    ///
    /// <para>KNOWN OVER-COUNT, stated: the proxy is the board's furnished BOX, not its silhouette,
    /// so the notches between an off-edge dock and the slab (the initiative mirror hangs above the
    /// top edge, the piles off the right) are counted as solid. That makes the metric slightly
    /// generous — a board fades a little earlier than its literal pixels justify — which is the
    /// safe direction for a feature whose failure mode is "the board is still in my way", and it
    /// costs one box test instead of a per-renderer sweep.</para>
    /// </summary>
    private float BlockedFraction(Vector3 headPos)
    {
        _lastBlocked = 0;
        _lastVisible = PeerBoardPlayArea.VisibleCount;
        if (!_hasBox || _lastVisible <= 0)
            return 0f;

        Transform t = transform;
        Vector3 localEye = t.InverseTransformPoint(headPos);
        if (_localBox.Contains(localEye))
        {
            _lastBlocked = _lastVisible;
            return 1f;
        }

        Matrix4x4 toLocal = t.worldToLocalMatrix;
        int blocked = 0;
        int n = PeerBoardPlayArea.Count;
        for (int i = 0; i < n; i++)
        {
            if (!PeerBoardPlayArea.IsVisible(i))
                continue;
            Vector3 localSample = toLocal.MultiplyPoint3x4(PeerBoardPlayArea.Sample(i));
            Vector3 seg = localSample - localEye;
            float len = seg.magnitude;
            if (len <= 1e-4f)
                continue;
            var ray = new Ray(localEye, seg / len);
            if (!_localBox.IntersectRay(ray, out float hit))
                continue;
            // Strictly BETWEEN the eye and the sample: behind the eye is not an occluder, and a
            // hit at (or past) the sample is the board lying on the floor it "hides".
            if (hit <= 0f || hit >= len * (1f - BlockEpsFraction))
                continue;
            blocked++;
        }
        _lastBlocked = blocked;
        return (float)blocked / _lastVisible;
    }

    /// <summary>
    /// Arm the SHORT exit dwell whenever the viewpoint really changed: the rig pose version (a
    /// recenter / rig rebuild), a real head translation of <see cref="HeadMoveReevalMeters"/>
    /// TRACKING metres, or the owner moving the board itself — which is the board-specific third
    /// case the wall cannot have, and the one that matters most here, because the user's whole
    /// complaint is about a board somebody else is dragging around.
    /// </summary>
    private void UpdatePerspectiveState(float now)
    {
        int pv = Rig.VRRigDriver.RigPoseVersion;
        if (pv != _lastPoseVersion)
        {
            _lastPoseVersion = pv;
            _lastReevalTime = now;
        }

        Camera? head = Rig.VRRigDriver.HeadCamera;
        Transform? rig = Rig.VRRigDriver.RigRoot;
        if (head != null)
        {
            // The head's position expressed in the rig's own frame is TRACKING space: the diorama
            // scale (11–20 world units per real metre) divides out, so the 0.18 m threshold means
            // 18 real centimetres at every world scale — the same property the wall constant
            // claims for itself.
            Vector3 track = rig != null
                ? rig.InverseTransformPoint(head.transform.position)
                : head.transform.position;
            if (!_headInit)
            {
                _headInit = true;
                _lastHeadTrack = track;
            }
            else if ((track - _lastHeadTrack).sqrMagnitude >= HeadMoveReevalMeters * HeadMoveReevalMeters)
            {
                _lastHeadTrack = track;
                _lastReevalTime = now;
            }
        }

        Transform t = transform;
        if (!_boardPoseInit)
        {
            _boardPoseInit = true;
            _lastBoardPos = t.position;
            _lastBoardRot = t.rotation;
        }
        else if ((t.position - _lastBoardPos).sqrMagnitude > 0.0004f
                 || Quaternion.Angle(t.rotation, _lastBoardRot) > 2f)
        {
            _lastBoardPos = t.position;
            _lastBoardRot = t.rotation;
            _lastReevalTime = now;
        }
    }

    // ------------------------------------------------------------------- the surface census ----

    /// <summary>
    /// Re-take the board's surface census and its occluder box. Renderers that appeared since the
    /// last scan join (and are swapped immediately when the board is already yielding, so a card
    /// dealt mid-fade does not blink in solid); renderers that went away are restored and dropped.
    ///
    /// <para>The occluder box is built from the board-local extents of the ACTIVE renderers only —
    /// a hidden card occludes nothing — and it deliberately includes the docks, the initiative
    /// mirror and the owner tag, because those are exactly the parts that hang off the board and
    /// eat the view.</para>
    /// </summary>
    private void RefreshSurfaces()
    {
        for (int i = 0; i < _surfaces.Count; i++)
            _surfaces[i].Seen = false;

        Transform root = transform;
        Matrix4x4 toLocal = root.worldToLocalMatrix;
        _hasBox = false;
        _rendererScratch.Clear();
        GetComponentsInChildren(true, _rendererScratch);
        for (int i = 0; i < _rendererScratch.Count; i++)
        {
            Renderer r = _rendererScratch[i];
            if (r == null)
                continue;
            if (!_known.TryGetValue(r, out Surface s))
            {
                s = new Surface { Renderer = r, Blendable = CanBlendAll(r.sharedMaterials) };
                _known[r] = s;
                _surfaces.Add(s);
                if (_engaged && !s.Blendable)
                    Swap(s);
            }
            s.Seen = true;

            if (!r.enabled || !r.gameObject.activeInHierarchy)
                continue;
            Bounds lb = r.localBounds;
            Matrix4x4 m = toLocal * r.localToWorldMatrix;
            Vector3 c = lb.center;
            Vector3 e = lb.extents;
            for (int k = 0; k < 8; k++)
            {
                var corner = new Vector3(
                    c.x + ((k & 1) == 0 ? -e.x : e.x),
                    c.y + ((k & 2) == 0 ? -e.y : e.y),
                    c.z + ((k & 4) == 0 ? -e.z : e.z));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!_hasBox)
                {
                    _hasBox = true;
                    _localBox = new Bounds(p, Vector3.zero);
                }
                else
                {
                    _localBox.Encapsulate(p);
                }
            }
        }

        for (int i = _surfaces.Count - 1; i >= 0; i--)
        {
            Surface s = _surfaces[i];
            if (s.Seen)
                continue;
            Restore(s);
            if (s.Renderer != null)
                _known.Remove(s.Renderer);
            else
                PruneDeadKeys();
            _surfaces.RemoveAt(i);
        }
        LogCensusIfChanged();
    }

    /// <summary>A destroyed renderer is a Unity-null KEY that <c>Remove</c> can no longer find;
    /// sweep them out wholesale rather than leaving the dictionary to grow across rebuilds.</summary>
    private void PruneDeadKeys()
    {
        _known.Clear();
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (_surfaces[i].Renderer != null)
                _known[_surfaces[i].Renderer] = _surfaces[i];
        }
    }

    // ------------------------------------------------------------------------- the delivery ----

    private void Apply(float alpha)
    {
        if (alpha >= 0.999f)
        {
            Release();
            return;
        }
        Engage();

        bool cull = alpha <= CullAlpha;
        _mpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < _surfaces.Count; i++)
        {
            Surface s = _surfaces[i];
            Renderer r = s.Renderer;
            if (r == null)
                continue;
            // Nothing to blend WITH: an unblendable material that could not be swapped (no
            // alpha-capable shader in the process at all) is the fail-safe family — it is culled
            // past the half-way point of the ramp instead of pretending to fade.
            bool blendable = s.Blendable || s.Installed != null;
            bool off = cull || (!blendable && alpha < 0.5f);
            if (r.forceRenderingOff != off)
                r.forceRenderingOff = off;
            if (off || !blendable)
                continue;
            WriteAlpha(r, alpha);
        }

        if (_group != null)
            _group.alpha = alpha;
    }

    /// <summary>
    /// The renderer's own live colour with its alpha scaled, through a property block. Re-read
    /// from the material EVERY frame on purpose: that is what makes the fade compose with the
    /// board's own colour animations (cap press, glow pulse) instead of freezing them.
    /// </summary>
    private void WriteAlpha(Renderer r, float alpha)
    {
        Material? m = r.sharedMaterial;
        if (m == null || _mpb == null)
            return;
        _mpb.Clear();
        bool any = false;
        for (int i = 0; i < ColorIds.Length; i++)
        {
            if (!m.HasProperty(ColorIds[i]))
                continue;
            Color c = m.GetColor(ColorIds[i]);
            c.a *= alpha;
            _mpb.SetColor(ColorIds[i], c);
            any = true;
        }
        if (any)
            r.SetPropertyBlock(_mpb);
    }

    private void Engage()
    {
        if (_engaged)
            return;
        _engaged = true;
        if (_group == null)
        {
            // One group on the ROOT: its alpha multiplies down through every nested canvas, so the
            // mirrors' own CanvasGroups (driven from their source widgets) are never written to.
            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null)
                _group = gameObject.AddComponent<CanvasGroup>();
        }
        for (int i = 0; i < _surfaces.Count; i++)
        {
            Surface s = _surfaces[i];
            if (!s.Blendable && s.Installed == null)
                Swap(s);
        }
    }

    private void Release()
    {
        if (_engaged)
        {
            for (int i = 0; i < _surfaces.Count; i++)
                Restore(_surfaces[i]);
            _engaged = false;
        }
        else
        {
            // Not engaged, but a previous engage may have left flags on renderers that have since
            // been re-registered — cheap and idempotent.
            for (int i = 0; i < _surfaces.Count; i++)
            {
                Renderer r = _surfaces[i].Renderer;
                if (r != null && r.forceRenderingOff)
                    r.forceRenderingOff = false;
            }
        }
        if (_group != null)
            _group.alpha = 1f;
    }

    /// <summary>Install private, alpha-capable clones of the materials on an unblendable
    /// renderer. Never touches a shared asset: the clone is ours, the original array is
    /// remembered verbatim and put back in <see cref="Restore"/>.</summary>
    private void Swap(Surface s)
    {
        Renderer r = s.Renderer;
        if (r == null || s.Installed != null)
            return;
        Material[] originals = r.sharedMaterials;
        if (originals.Length == 0)
            return;
        var installed = new Material[originals.Length];
        bool anyClone = false;
        for (int i = 0; i < originals.Length; i++)
        {
            Material? m = originals[i];
            if (m == null || CanBlend(m))
            {
                installed[i] = m!;
                continue;
            }
            Material clone = MakeTransparentClone(m);
            installed[i] = clone;
            anyClone = ReferenceEquals(clone, m) ? anyClone : true;
        }
        if (!anyClone)
            return; // no alpha-capable shader available: the cull fail-safe takes this renderer
        s.Original = originals;
        s.Installed = installed;
        r.sharedMaterials = installed;
    }

    private void Restore(Surface s)
    {
        Renderer r = s.Renderer;
        if (r != null)
        {
            if (r.forceRenderingOff)
                r.forceRenderingOff = false;
            r.SetPropertyBlock(null);
            if (s.Original != null)
                r.sharedMaterials = s.Original;
        }
        if (s.Installed != null && s.Original != null)
        {
            for (int i = 0; i < s.Installed.Length; i++)
            {
                Material inst = s.Installed[i];
                if (inst != null && !ReferenceEquals(inst, s.Original[i]))
                    Object.Destroy(inst);
            }
        }
        s.Original = null;
        s.Installed = null;
    }

    /// <summary>
    /// A private clone of <paramref name="m"/> that can actually blend. The knob recipe first
    /// (a Standard/URP-shaped material only needs its surface mode flipped), and a SHADER SWAP
    /// when the shader has no blend state at all — which is the case that matters, because
    /// <c>GloomhavenVR/BoardLit</c> (the board slab, every keycap, the handle bars, the status
    /// plates) exposes none of the knobs and would silently ignore every alpha we ever wrote.
    /// Returns the input unchanged when no alpha-capable shader exists in the process.
    /// </summary>
    private static Material MakeTransparentClone(Material m)
    {
        if (!CanBlend(m))
        {
            Shader? target = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default")
                             ?? Shader.Find("Unlit/Transparent");
            if (target == null)
                return m;
            // Read the look BEFORE the swap — property ids resolve against the current shader.
            Texture? tex = m.HasProperty(MainTexId) ? m.GetTexture(MainTexId)
                : m.HasProperty(BaseMapId) ? m.GetTexture(BaseMapId) : null;
            Color tint = Color.white;
            for (int i = 0; i < ColorIds.Length; i++)
            {
                if (!m.HasProperty(ColorIds[i]))
                    continue;
                tint = m.GetColor(ColorIds[i]);
                break;
            }
            var swapped = new Material(target) { name = m.name + CloneMarker, color = tint };
            if (tex != null)
                swapped.mainTexture = tex;
            swapped.renderQueue = SwapRenderQueue;
            return swapped;
        }

        var clone = new Material(m) { name = m.name + CloneMarker };
        if (clone.HasProperty(ModeId))
            clone.SetFloat(ModeId, 2f); // Standard: 0 Opaque, 1 Cutout, 2 Fade, 3 Transparent
        if (clone.HasProperty(SurfaceId))
            clone.SetFloat(SurfaceId, 1f); // URP Lit/Unlit: 0 Opaque, 1 Transparent
        if (clone.HasProperty(SrcBlendId))
            clone.SetInt(SrcBlendId, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (clone.HasProperty(DstBlendId))
            clone.SetInt(DstBlendId, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (clone.HasProperty(ZWriteId))
            clone.SetInt(ZWriteId, 0);
        clone.DisableKeyword("_ALPHATEST_ON");
        clone.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        clone.EnableKeyword("_ALPHABLEND_ON");
        clone.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        if (clone.renderQueue < SwapRenderQueue)
            clone.renderQueue = SwapRenderQueue;
        return clone;
    }

    /// <summary>Can this material's shader blend at all? A shader that exposes neither the
    /// Standard/URP surface-mode switch nor the raw blend factors has its blending HARD-CODED
    /// (opaque, in every case this mod ships), so no property write will ever fade it —
    /// the measured root cause behind <c>HandGhost.MakeTransparent</c>. A material already in
    /// the transparent queue is taken at its word.</summary>
    private static bool CanBlend(Material m) =>
        m.renderQueue > 2500
        || m.HasProperty(ModeId) || m.HasProperty(SurfaceId)
        || (m.HasProperty(SrcBlendId) && m.HasProperty(DstBlendId));

    private static bool CanBlendAll(Material[] materials)
    {
        if (materials.Length == 0)
            return true;
        for (int i = 0; i < materials.Length; i++)
        {
            Material m = materials[i];
            if (m != null && !CanBlend(m))
                return false;
        }
        return true;
    }

    // ------------------------------------------------------------------------ diagnostics ------

    /// <summary>
    /// ONE line per real state flip, naming the peer, the test, the margin it crossed, the alpha
    /// it is being driven to and how long the dwell held it. This is the line a "the board still
    /// blocks my view" / "the board keeps flickering" report is answered against: if it never
    /// appears the board was never JUDGED occluding (read the coverage in the 2 s diag), and if it
    /// appears in pairs seconds apart the dwells are too short for that pose.
    /// </summary>
    private void LogStateIfChanged(float alpha, float onFraction, float offFraction,
                                   float exitDwell, bool reevalArmed)
    {
        if (_loggedStateInit && _state == _loggedState)
            return;
        _loggedStateInit = true;
        _loggedState = _state;
        VRLog.Info("Net", $"Peer board [{_playerId}] see-through {(_state ? "ON" : "OFF")} " +
            $"({PeerBoardFadeTuning.Mode}) — occlusion test: oriented board box vs the head→" +
            $"play-field segments, {_lastBlocked}/{_lastVisible} in-view sample(s) blocked, raw " +
            $"{_lastRaw:0.000}, smoothed {_smooth:0.000} vs the {(_state ? onFraction : offFraction):0.00} " +
            $"bar it just crossed (margin {Mathf.Abs(_smooth - (_state ? onFraction : offFraction)):0.000}); " +
            $"driving alpha {alpha:0.00} over ~{FadeTauSeconds * 3f:0.00}s. Hysteresis held it for " +
            $"{(_state ? EnterDwellSeconds : exitDwell):0.0}s of continuous agreement " +
            $"({(reevalArmed ? "perspective recently changed" : "head only rotating")}). " +
            "PURELY LOCAL — the owner's board is untouched and nothing went on the wire.");
    }

    /// <summary>Throttled state line while a board is yielding — the numbers that say WHY it is
    /// where it is, so a mis-judged board can be diagnosed without a screenshot. Off under
    /// <c>PerfConfig.Quiet</c>, exactly like the wall's own 2 Hz sweep.</summary>
    private void LogDiagnostic(float alpha, float onFraction, float offFraction, bool reevalArmed)
    {
        VRLog.Info("Net", $"Peer board [{_playerId}] see-through diag: state {(_state ? "ON" : "OFF")}, " +
            $"fade {_fade:0.00} → alpha {alpha:0.00}; coverage raw {_lastRaw:0.000} smoothed " +
            $"{_smooth:0.000} ({_lastBlocked}/{_lastVisible} of {PeerBoardPlayArea.Count} play-field " +
            $"samples in view); bars on {onFraction:0.00} / off {offFraction:0.00}; " +
            $"{(reevalArmed ? "short" : "long")} exit dwell armed; box (board-local) " +
            $"{_localBox.size.x:0.00}×{_localBox.size.y:0.00}×{_localBox.size.z:0.00} m; " +
            $"{_surfaces.Count} surface(s) driven.");
    }

    /// <summary>One line per real change of the surface census: how many renderers this board
    /// offers and how many of them needed a material swap because their shader cannot blend. A
    /// swap count of 0 on a board that is visibly still solid is the reading that says the swap
    /// path failed, not the decision.</summary>
    private void LogCensusIfChanged()
    {
        int swapped = 0;
        for (int i = 0; i < _surfaces.Count; i++)
        {
            if (!_surfaces[i].Blendable)
                swapped++;
        }
        int signature = _surfaces.Count * 397 + swapped;
        if (signature == _loggedSurfaces)
            return;
        _loggedSurfaces = signature;
        VRLog.Info("Net", $"Peer board [{_playerId}] see-through census: {_surfaces.Count} renderer(s), " +
            $"{swapped} of them on a shader that cannot blend (GloomhavenVR/BoardLit and friends) " +
            "and therefore delivered through a private material clone while faded; the rest take a " +
            "property-block alpha and every UI graphic rides one CanvasGroup on the board root.");
    }
}
