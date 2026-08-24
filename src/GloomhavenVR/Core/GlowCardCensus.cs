using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// THE PALE FLOATING RECTANGLES ON THE GATE — collection half of the <c>[Perf] GLOW CARDS</c> line.
/// The reporting half is in <c>GlowCardCensus.Report.cs</c>.
///
/// <para>THE REPORT THIS WAS BUILT FOR (user, 2026-08-24, verbatim): <i>"Diese schwebenden
/// viereckigen Lichter an dem Tor erscheinen mir komisch, wird das richtig gerendert? Fehlt hier
/// irgendwas? Das ist dauerhaft so egal was ein oder ausgeblendet wird - ich kann mich nicht
/// erinnern, dass es flat sowas gab."</i> Three pale hard-edged rectangles beside and between the
/// leaves of a gate (<c>schwebende_lichter.jpg</c>), unchanged in size, position and brightness when
/// the masonry around them dissolves (<c>walls_gone.jpg</c>). At 7x magnification the middle one is a
/// PARALLELOGRAM in a plane aligned with neither the wall nor the door, with a soft bright hotspot
/// left of centre falling off to a flat pale surround and a hard straight edge all round — the
/// signature of a soft glow texture whose ALPHA is not reaching the blender, not of a light.</para>
///
/// <para>WHAT ModBuild 251 GOT WRONG, so it is not repeated. 251 shipped the hypothesis "these are
/// depth-fade cards and <c>depthTextureMode=None</c> makes them draw at full opacity" and its verdict
/// sentence asserted it. The user ran the A/B: <i>"Auch mit HeadDepthPrepass=true sind die
/// schwebenden Lichter an der Tür noch da"</i>, and <c>second_logs/Player.log</c> confirms the
/// experiment really ran (<c>depthTextureMode=Depth</c>). THE HYPOTHESIS IS DEAD. Worse, the census's
/// OWN numbers had already refuted it: the three cards carrying a depth-fade property were all
/// <c>SimpleParticleAlphaDFade</c> shield clouds on stone golems, <c>enabled+offscreen+not-submitted</c>
/// with AABB size 0, nowhere near the gate, while every card that WAS near the gate printed
/// <c>depth-fade props: NONE</c>. An instrument asserted a cause its own population could not carry.
/// <see cref="AppendVerdict"/> now derives every sentence from the SUBJECT-ELIGIBLE set alone and says
/// so when that set is empty.</para>
///
/// <para>WHAT THIS VERSION CHANGES, and why each change exists.
/// <list type="bullet">
/// <item>SELECTION IS NO LONGER A SHADER-NAME LIST. 251's cap of 16 was filled by 0-pixel offscreen
/// monster effects while a 583-pixel light shaft sat at index 4. Candidates are now marked by four
/// independent, per-renderer-free tests (shader name, transparent render queue, special/VR-only
/// LAYER, and PLATE-SHAPED bounds), pooled, and RANKED BY ON-SCREEN PIXEL SPAN. The plate test is the
/// one that does not care what the material is called: a card is a quad, and a quad's thinnest AABB
/// axis is a small fraction of its longest. Whatever those rectangles are, they are flat.</item>
/// <item>EVERY property and EVERY keyword the material actually has is dumped, walked off
/// <c>Shader.GetPropertyCount</c>/<c>GetPropertyName</c>/<c>GetPropertyType</c> rather than a
/// hard-coded list of nine names — because "depth-fade props: NONE" may only have meant that
/// <c>LightShaftShd</c> spells its fade something else.</item>
/// <item>A SCREEN RECT per card, projected through the head camera and printed both in normalised
/// top-left coordinates and in the pixels of a 3840x2160 screenshot. This is the identification key:
/// the user's photographs put the three rectangles at known screen positions, and a record whose rect
/// lands on one of them IS the subject. Nothing in 251 could be matched to a photo at all.</item>
/// <item>THE RENDER-PATH CONTRAST, printed once per line. The game draws this scene through
/// <c>ScenarioCamera</c>; the mod draws it through its own head camera with a different rendering
/// path and a different culling mask. "I don't remember this on the flat screen" is a claim about the
/// DIFFERENCE between those two cameras, and until now no line in this codebase printed both.</item>
/// <item>IT CAN SAMPLE WHEN IT MATTERS. 251 rode <see cref="PerfSceneProfile"/>'s self-rationing walk
/// and the A/B session got exactly one sample, on a 5-renderer frame. It now also runs a self-timed,
/// self-rationed walk of its own on windows the SCENE walk skipped, and it LATCHES the richest sample
/// it has seen so a window that caught the gate keeps answering after the player has looked away.</item>
/// </list></para>
///
/// <para>COST. On a window where the SCENE walk runs, this adds: one dictionary lookup per material
/// slot (keyed by the SHADER instance id the walk has already resolved, so <c>Shader.name</c> is
/// still marshalled once per distinct shader and never once per renderer), and one
/// <c>Renderer.bounds</c> read per SUBMITTED renderer. Everything else — names, ancestor walks,
/// property enumeration, pass tags, block read-back, projection — happens only for pooled candidates
/// and is capped. On a window the SCENE walk skipped, the standalone walk pays one
/// <c>FindObjectsOfType&lt;Renderer&gt;</c>; it TIMES ITSELF and rations itself to
/// <see cref="OwnWalkTargetMs"/> per window, and both numbers are printed. A per-frame scene sweep is
/// this repo's default suspect and has shipped as a defect three times; nothing here runs per frame.</para>
///
/// <para>MULTIPLAYER / REVERSIBILITY. Reads state, writes one log line. It never touches a
/// GameObject, a material, game state or wire traffic. Renderer/Material/Shader references are held
/// only between <see cref="Begin"/> and <see cref="Log"/> inside a single call of
/// <c>PerfMonitor.LogSceneProfile</c>, and both ends clear them.</para>
/// </summary>
internal static partial class GlowCardCensus
{
    private const string Scope = "Perf";

    /// <summary>Detailed records printed. The reported defect is three quads; fourteen is room for
    /// the whole family plus whatever else is big on the gate wall.</summary>
    private const int MaxCards = 14;

    /// <summary>How many of those get the FULL property/keyword/pass dump. The dump is the expensive
    /// and verbose part, and the ranking puts the subject at the top by construction.</summary>
    private const int FullDumpCards = 5;

    /// <summary>Ranked pool size. Candidates beyond this are dropped by SMALLEST span first and the
    /// count and largest dropped span are printed, so the cap never hides the subject silently.</summary>
    private const int MaxCandidates = 96;

    /// <summary>Compact one-line entries printed for pooled candidates that did not make MaxCards.</summary>
    private const int MaxTail = 22;

    /// <summary>Shader properties dumped per material before the dump says "+N more".</summary>
    private const int MaxProps = 44;

    /// <summary>
    /// Pixel span below which a card CANNOT be the photographed rectangle. The quads measure roughly
    /// 90x110 px of a 3840-px-wide screenshot, i.e. of order 100 px of a 3072x3264 eye; 24 px is a
    /// deliberately generous floor that still excludes the thousands of distant slots. Only cards at
    /// or above it, submitted, are allowed into the VERDICT.
    /// </summary>
    private const float SubjectMinPx = 24f;

    /// <summary>Thinnest AABB axis / longest AABB axis at or below which a renderer is PLATE-SHAPED —
    /// a card, a decal, a pane, a billboard. Shader-independent and material-independent, which is
    /// the whole point: it is the one test that survives not knowing what the subject is called.</summary>
    private const float PlateRatio = 0.22f;

    /// <summary>Amortised budget for the standalone walk (see <see cref="RunStandalone"/>).</summary>
    private const double OwnWalkTargetMs = 2d;

    /// <summary>Ceiling on the standalone walk's self-rationing, so one expensive sample cannot
    /// silence it for the rest of the session.</summary>
    private const int MaxOwnSkips = 8;

    // ==========================================================================================
    //  what marks a candidate
    // ==========================================================================================

    [Flags]
    private enum Mark
    {
        None = 0,
        /// <summary>Shader name contains one of <see cref="ShaderMarks"/>.</summary>
        Shader = 1,
        /// <summary>Material render queue is Transparent or later (>= 2900).</summary>
        Queue = 2,
        /// <summary>GameObject layer is one of the effect/render-target layers.</summary>
        Layer = 4,
        /// <summary>AABB is plate-shaped: thinnest axis &lt;= <see cref="PlateRatio"/> of longest.</summary>
        Plate = 8,
        /// <summary>Layer is rendered by the head camera and NOT by the game's ScenarioCamera —
        /// i.e. this renderer exists on the flat screen but is never drawn there.</summary>
        VrOnly = 16,
    }

    /// <summary>Shader-name fragments that mark a light/glow/decal card, matched case-insensitively.
    /// Kept from ModBuild 251 and widened; it is no longer the only selector, so a miss here is no
    /// longer fatal to the census.</summary>
    private static readonly string[] ShaderMarks =
    {
        "glow", "dfade", "depthfade", "softparticle", "lightshaft", "shaft",
        "flare", "halo", "beam", "decal", "particlemaster", "lightplane",
        "unlit", "additive", "emiss", "vfx/", "fx_", "billboard", "sprite"
    };

    /// <summary>Layers whose whole population is worth marking however small: effect layers and the
    /// three render-target layers this game declares (28 WaypointRenderTexture, 29 RenderTarget,
    /// 30 Outline). A renderer on a render-target layer that is nevertheless visible in the world is
    /// exactly the shape of "something is drawn that was never meant to be seen directly".</summary>
    private static readonly int[] MarkedLayers = { 1, 18, 28, 29, 30, 31 };

    /// <summary>The three colour properties the wall-fade path drives, in the SAME priority order it
    /// uses (WallSegmentFade.Mounted.cs) — so "which one is it driving" stays answerable.</summary>
    private static readonly string[] TintPropNames = { "_TintColor", "_Color", "_BaseColor" };

    private static readonly int[] TintProps = BuildIds(TintPropNames);

    /// <summary>Fixed-function state a material may expose as properties. When a material exposes
    /// NONE of them the blend is hardcoded in the shader pass and Unity offers no runtime query —
    /// that is a fact about the shader, and <see cref="AppendBlend"/> says it in words instead of
    /// printing "n/a" three times, which is what ModBuild 251 did for every single card.</summary>
    private static readonly string[] StatePropNames = { "_SrcBlend", "_DstBlend", "_ZWrite", "_ZTest", "_Cull", "_BlendOp" };

    private static readonly int[] StateProps = BuildIds(StatePropNames);

    /// <summary>Material tags worth printing: they are the only readable proxies for a blend state
    /// the engine will not hand back.</summary>
    private static readonly string[] TagNames = { "RenderType", "Queue", "IgnoreProjector", "ForceNoShadowCasting", "PreviewType", "LightMode" };

    private static int[] BuildIds(string[] names)
    {
        int[] ids = new int[names.Length];
        for (int i = 0; i < names.Length; i++)
            ids[i] = Shader.PropertyToID(names[i]);
        return ids;
    }

    // ==========================================================================================
    //  pooled candidates
    // ==========================================================================================

    private sealed class Candidate
    {
        public Renderer? R;
        public Material? Mat;
        public Shader? Sh;
        public bool Submitted;
        public float Span;
        public Mark Marks;
        public Bounds B;

        /// <summary>Ranking key. Span is the base — the instrument's whole failure in 251 was ranking
        /// by nothing at all — multiplied up for the two properties that make a candidate more likely
        /// to be the photographed subject, and multiplied DOWN for a card nobody can currently see.
        /// Printed next to every record so the order is checkable rather than trusted.</summary>
        public float Score => Span
                              * ((Marks & Mark.VrOnly) != 0 ? 8f : 1f)
                              * ((Marks & Mark.Plate) != 0 ? 3f : 1f)
                              * (Submitted ? 1f : 0.05f);
    }

    private static readonly List<Candidate> Pool = new(MaxCandidates);
    private static readonly Stack<Candidate> Spare = new(MaxCandidates);
    private static readonly Dictionary<int, bool> ShaderInterest = new(64);
    private static readonly HashSet<int> SeenRenderers = new(MaxCandidates * 2);
    private static readonly List<string> Scratch = new(8);
    private static MaterialPropertyBlock? _blockScratch;

    /// <summary>Per-layer renderer population, handed over by the walk that produced it so the
    /// path-contrast block can say how many renderers sit on the layers only VR draws.</summary>
    private static readonly int[] LayerPop = new int[32];
    private static readonly int[] LayerVis = new int[32];
    private static bool _layerPopKnown;

    // ---- per-renderer latch, set by OfferRenderer and consumed by Offer -----------------------
    private static int _curRendererId;
    private static Mark _curMarks;
    private static float _curSpan;
    private static Bounds _curBounds;
    private static bool _curSubmitted;

    // ---- window state -------------------------------------------------------------------------
    private static bool _armed;

    /// <summary>Did <see cref="Begin"/> run for the window <see cref="Log"/> is about to print?
    /// PerfMonitor calls AppendGfxLine — and therefore this line — even on a window where
    /// AppendSceneLine rationed itself and never walked, so "found nothing" and "never looked" are
    /// two different sentences and this flag is what tells them apart.</summary>
    private static bool _sampled;
    private static int _slotsOffered;
    private static int _renderersOffered;

    /// <summary>Renderer.bounds reads this window — the ONLY per-renderer cost this census adds to
    /// the SCENE walk it rides. Printed so the price is a number in the log and not a claim in a
    /// comment; this repo has shipped "the scan is type-indexed, near-free" as a defect twice.</summary>
    private static int _boundsReads;
    private static int _matched;
    private static int _dropped;
    private static float _droppedMaxSpan;
    private static float _poolMinScore;
    private static int _distinctGlowShaders;
    private static double _costMs;

    // ---- global facts, latched at Begin so cards and camera state are read TOGETHER -------------
    private static Camera? _head;
    private static DepthTextureMode _headDepth = DepthTextureMode.None;
    private static bool _headKnown;
    private static string _headName = "n/a";
    private static RenderingPath _headPath = RenderingPath.UsePlayerSettings;
    private static int _headMask;
    private static bool _softParticles;
    private static bool _depthDialOn;

    private static Camera? _scenarioCam;
    private static bool _scenarioKnown;
    private static RenderingPath _scenarioPath = RenderingPath.UsePlayerSettings;
    private static int _scenarioMask;
    private static int _scenarioBuffers;

    private static float _pixelsPerUnit;
    private static Vector3 _headPos;
    private static float _eyePxW, _eyePxH;
    private static bool _projectionKnown;

    // ---- standalone walk ------------------------------------------------------------------------
    private static int _ownSkip;
    private static double _lastOwnWalkMs;
    private static int _ownWalkRenderers;
    private static bool _ranOwnWalk;
    private static string _ownWalkNote = string.Empty;

    // ---- best-sample latch ------------------------------------------------------------------------
    private static string _bestSample = string.Empty;
    private static float _bestSampleScore;
    private static float _bestSampleAt = -1f;
    private static string _bestSampleScene = string.Empty;

    // ==========================================================================================
    //  collection — driven from PerfSceneProfile's SCENE walk, or from RunStandalone
    // ==========================================================================================

    /// <summary>Arm the census for one window and latch every GLOBAL fact the verdict needs, at the
    /// same instant the population is sampled.</summary>
    internal static void Begin(Camera? head)
    {
        Reset();
        _armed = true;
        _sampled = true;
        _head = head;
        _depthDialOn = PerfConfig.DepthPrepassOn;
        try { _softParticles = QualitySettings.softParticles; }
        catch (Exception) { _softParticles = false; }

        // Drop the best-sample latch when the scene changed: a 900px quad latched in the map room
        // would otherwise outrank every scenario sample for the rest of the session and the line
        // would keep printing a room the player left. Stale is acceptable only WITHIN one scene.
        try
        {
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (_bestSampleScene.Length > 0 && scene != _bestSampleScene)
            {
                _bestSample = string.Empty;
                _bestSampleScore = 0f;
                _bestSampleAt = -1f;
                _bestSampleScene = string.Empty;
            }
        }
        catch (Exception)
        {
            // an unreadable scene name simply leaves the latch alone
        }

        if (head != null)
        {
            try
            {
                _headDepth = head.depthTextureMode;
                _headName = head.name;
                _headPath = head.actualRenderingPath;
                _headMask = head.cullingMask;
                _headKnown = true;
            }
            catch (Exception) { _headKnown = false; }
            LatchProjection(head);
        }

        Camera? scenario = ResolveScenarioCamera();
        if (scenario != null)
        {
            try
            {
                _scenarioPath = scenario.actualRenderingPath;
                _scenarioMask = scenario.cullingMask;
                _scenarioBuffers = scenario.commandBufferCount;
                _scenarioKnown = true;
            }
            catch (Exception) { _scenarioKnown = false; }
        }
    }

    /// <summary>The projection term, identical arithmetic to <c>PerfTextureCensus.Begin</c> (which is
    /// not editable from this lane). For world size <c>s</c> at distance <c>d</c> the pixel span is
    /// <c>s / d * (0.5 * eyeHeight * m11)</c>, and the bracket is constant for the window. Duplicated
    /// rather than shared because the other census applies a 120 px FLOOR and returns 0 below it —
    /// which is precisely why ModBuild 251 printed <c>~0px</c> for every candle and every torch and
    /// then ranked by nothing.</summary>
    private static void LatchProjection(Camera head)
    {
        _projectionKnown = false;
        try
        {
            float viewport = Mathf.Clamp(UnityEngine.XR.XRSettings.renderViewportScale, 0.01f, 1f);
            int w = UnityEngine.XR.XRSettings.eyeTextureWidth;
            int h = UnityEngine.XR.XRSettings.eyeTextureHeight;
            if (w < 2 || h < 2)
            {
                w = Screen.width;
                h = Screen.height;
                viewport = 1f;
            }
            _eyePxW = w * viewport;
            _eyePxH = h * viewport;
            if (!(_eyePxW >= 2f && _eyePxH >= 2f))
                return;

            float m11 = head.projectionMatrix.m11;
            if (!(m11 > 0.01f) || float.IsNaN(m11) || float.IsInfinity(m11))
                m11 = 1f / Mathf.Tan(Mathf.Clamp(head.fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);

            _pixelsPerUnit = 0.5f * _eyePxH * m11;
            _headPos = head.transform.position;
            _projectionKnown = _pixelsPerUnit > 1f;
        }
        catch (Exception)
        {
            _projectionKnown = false;
        }
    }

    /// <summary>
    /// Offer ONE RENDERER, before its material slots. Reads <c>Renderer.bounds</c> once and derives
    /// the two marks that need no material at all: the LAYER mark and the PLATE mark. The plate mark
    /// is the reason this entry point exists — a card is a quad whatever its shader is called, and
    /// ModBuild 251 had no test that could find the subject without already knowing its name.
    ///
    /// <para>Called for every renderer the walk sees. The bounds read is skipped for anything not
    /// submitted, so the added cost on a non-submitted renderer is one array index and a compare.</para>
    /// </summary>
    internal static void OfferRenderer(Renderer r, bool submitted, int layer)
    {
        if (!_armed || r == null)
            return;
        _renderersOffered++;
        _curRendererId = r.GetInstanceID();
        _curMarks = Mark.None;
        _curSpan = 0f;
        _curSubmitted = submitted;
        _curBounds = default;

        for (int i = 0; i < MarkedLayers.Length; i++)
        {
            if (MarkedLayers[i] == layer)
            {
                _curMarks |= Mark.Layer;
                break;
            }
        }
        // Rendered by us and NOT by the game's own scenario camera ⇒ this object is on screen in VR
        // and was never on screen on the flat screen. That is a whole class of report in one bit.
        if (_scenarioKnown && (_headMask & (1 << layer)) != 0 && (_scenarioMask & (1 << layer)) == 0)
            _curMarks |= Mark.VrOnly | Mark.Layer;

        if (!submitted || !_projectionKnown)
            return;

        try
        {
            Bounds b = r.bounds;
            _boundsReads++;
            _curBounds = b;
            Vector3 s = b.size;
            float longest = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            float thinnest = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (!(longest > 0f) || float.IsNaN(longest))
                return;
            float d = (b.center - _headPos).magnitude;
            if (!(d > 0.0001f) || float.IsNaN(d))
                return;
            float px = longest * _pixelsPerUnit / d;
            if (float.IsNaN(px) || float.IsInfinity(px))
                return;
            _curSpan = px;
            if (px >= SubjectMinPx && thinnest <= longest * PlateRatio)
                _curMarks |= Mark.Plate;
        }
        catch (Exception)
        {
            // A renderer without usable bounds still gets its layer marks.
        }

        if (_curMarks != Mark.None)
            Register(r, null, null, _curMarks);
    }

    /// <summary>
    /// Offer one material slot of the renderer most recently passed to <see cref="OfferRenderer"/>.
    /// The fast path — an ordinary shader on an ordinary queue — is one dictionary lookup and one
    /// integer compare: <paramref name="sh"/> is the reference <see cref="PerfSceneProfile"/>'s own
    /// shader tally has already resolved, so there is no second <c>Material.shader</c> marshal, and
    /// <c>Shader.name</c> is fetched once per distinct Shader object.
    /// </summary>
    internal static void Offer(Renderer r, Material mat, Shader sh, bool submitted, float texSpanPx)
    {
        if (!_armed || r == null || mat == null || sh == null)
            return;
        _slotsOffered++;

        int shaderId = sh.GetInstanceID();
        if (!ShaderInterest.TryGetValue(shaderId, out bool interesting))
        {
            interesting = IsGlowShader(sh);
            ShaderInterest[shaderId] = interesting;
            if (interesting)
                _distinctGlowShaders++;
        }

        Mark marks = _curRendererId == r.GetInstanceID() ? _curMarks : Mark.None;
        if (interesting)
            marks |= Mark.Shader;
        int queue;
        try { queue = mat.renderQueue; }
        catch (Exception) { queue = -1; }
        if (queue >= 2900)
            marks |= Mark.Queue;

        if (marks == Mark.None)
            return;
        _matched++;
        Register(r, mat, sh, marks);
    }

    /// <summary>Pool one renderer, ONCE, keeping the pool at <see cref="MaxCandidates"/> by evicting
    /// the LOWEST-scoring entry. A renderer already pooled by <see cref="OfferRenderer"/> has its
    /// material and marks filled in here rather than being pooled twice.</summary>
    private static void Register(Renderer r, Material? mat, Shader? sh, Mark marks)
    {
        int id = r.GetInstanceID();
        if (!SeenRenderers.Add(id))
        {
            // Already pooled from the geometry pass: upgrade it with the material and merged marks.
            for (int i = 0; i < Pool.Count; i++)
            {
                Candidate p = Pool[i];
                if (p.R == null || p.R.GetInstanceID() != id)
                    continue;
                p.Marks |= marks;
                if (p.Mat == null && mat != null)
                {
                    p.Mat = mat;
                    p.Sh = sh;
                }
                // The upgrade changed this entry's Score, so the cached pool minimum the fast-reject
                // below leans on is no longer valid. Recomputing 96 floats on the rare upgrade is
                // cheaper than letting a stale minimum silently reject a candidate that outranks one
                // already in the pool — which is the class of quiet sampling artefact this whole
                // rewrite exists to remove.
                if (Pool.Count >= MaxCandidates)
                    RecomputePoolMin();
                return;
            }
            return;
        }

        Candidate c = Spare.Count > 0 ? Spare.Pop() : new Candidate();
        c.R = r;
        c.Mat = mat;
        c.Sh = sh;
        c.Submitted = _curRendererId == id ? _curSubmitted : true;
        c.Span = _curRendererId == id ? _curSpan : 0f;
        c.Marks = marks;
        c.B = _curRendererId == id ? _curBounds : default;

        if (Pool.Count < MaxCandidates)
        {
            Pool.Add(c);
            if (Pool.Count == MaxCandidates)
                RecomputePoolMin();
            return;
        }
        if (c.Score <= _poolMinScore)
        {
            _dropped++;
            if (c.Span > _droppedMaxSpan)
                _droppedMaxSpan = c.Span;
            SeenRenderers.Remove(id);
            Recycle(c);
            return;
        }
        // Evict the current minimum.
        int worst = 0;
        float worstScore = float.MaxValue;
        for (int i = 0; i < Pool.Count; i++)
        {
            float s = Pool[i].Score;
            if (s >= worstScore)
                continue;
            worstScore = s;
            worst = i;
        }
        Candidate evicted = Pool[worst];
        _dropped++;
        if (evicted.Span > _droppedMaxSpan)
            _droppedMaxSpan = evicted.Span;
        if (evicted.R != null)
            SeenRenderers.Remove(evicted.R.GetInstanceID());
        Recycle(evicted);
        Pool[worst] = c;
        RecomputePoolMin();
    }

    private static void RecomputePoolMin()
    {
        float min = float.MaxValue;
        for (int i = 0; i < Pool.Count; i++)
        {
            float s = Pool[i].Score;
            if (s < min)
                min = s;
        }
        _poolMinScore = min;
    }

    private static void Recycle(Candidate c)
    {
        c.R = null;
        c.Mat = null;
        c.Sh = null;
        c.Marks = Mark.None;
        c.Span = 0f;
        Spare.Push(c);
    }

    private static bool IsGlowShader(Shader sh)
    {
        string name;
        try { name = sh.name; }
        catch (Exception) { return false; }
        if (string.IsNullOrEmpty(name))
            return false;
        for (int i = 0; i < ShaderMarks.Length; i++)
        {
            if (name.IndexOf(ShaderMarks[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>Hand over the per-layer population the walk already tallied, so the path-contrast
    /// block can say how many renderers sit on the layers only the head camera draws. Never
    /// recomputed here: a second pass over 3,000 renderers to count what the caller already counted
    /// is exactly the kind of duplicated sweep this repo keeps shipping as a defect.</summary>
    internal static void NoteLayerPopulation(int[] counts, int[] visible)
    {
        if (!_armed || counts == null || visible == null || counts.Length < 32 || visible.Length < 32)
            return;
        Array.Copy(counts, LayerPop, 32);
        Array.Copy(visible, LayerVis, 32);
        _layerPopKnown = true;
    }

    // ==========================================================================================
    //  the standalone walk — for windows the SCENE walk rationed away
    // ==========================================================================================

    /// <summary>
    /// Sample the census WITHOUT the SCENE walk. This exists because the ModBuild 251 A/B session
    /// produced exactly one GLOW CARDS sample with a population in it, and that sample landed on a
    /// five-renderer frame — the census was armed only when <see cref="PerfSceneProfile"/> chose to
    /// walk, and that walk rations itself down to 4 ms/window and skips up to eight windows in a row.
    /// An instrument that can only answer on windows somebody else chose is an instrument that
    /// answers the wrong question, which is what happened.
    ///
    /// <para>It is a <c>FindObjectsOfType&lt;Renderer&gt;</c> — the same sweep, and the same default
    /// suspect — so it is TIMED and RATIONED against <see cref="OwnWalkTargetMs"/> exactly the way
    /// the SCENE walk is, it only ever runs on a window that walk already declined, and its measured
    /// cost is printed on the line. It does no SIM half, no root tally, no shader tally and no
    /// texture census: it reads layer, enabled, isVisible, bounds and the shared material list, and
    /// nothing else.</para>
    /// </summary>
    internal static void RunStandalone(Camera? head)
    {
        if (_ownSkip > 0)
        {
            _ownSkip--;
            _ranOwnWalk = false;
            _ownWalkNote = "the standalone walk is rationed off this window (last one measured "
                           + _lastOwnWalkMs.ToString("F1") + "ms against a " + OwnWalkTargetMs.ToString("F0")
                           + "ms/window budget; " + _ownSkip + " more window(s) to skip)";
            return;
        }

        Stopwatch clock = Stopwatch.StartNew();
        Begin(head);
        _ranOwnWalk = true;

        Renderer[] all;
        try
        {
            all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        }
        catch (Exception e)
        {
            clock.Stop();
            _ownWalkNote = "the standalone walk threw " + e.GetType().Name + " and collected nothing";
            return;
        }

        _ownWalkRenderers = all.Length;
        int headMask = _headKnown ? _headMask : ~0;
        var mats = new List<Material>(8);
        Array.Clear(LayerPop, 0, 32);
        Array.Clear(LayerVis, 0, 32);

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
                continue;
            int layer = r.gameObject.layer;
            bool vis = r.isVisible;
            LayerPop[layer]++;
            if (vis)
                LayerVis[layer]++;
            bool subm = r.enabled && vis && (headMask & (1 << layer)) != 0;
            OfferRenderer(r, subm, layer);
            try
            {
                r.GetSharedMaterials(mats);
                for (int m = 0; m < mats.Count; m++)
                {
                    Material? mat = mats[m];
                    if (mat == null)
                        continue;
                    Shader? sh = mat.shader;
                    if (sh != null)
                        Offer(r, mat, sh, subm, 0f);
                }
            }
            catch (Exception)
            {
                // a renderer with no readable material array still counts through its geometry marks
            }
        }
        _layerPopKnown = true;

        clock.Stop();
        _lastOwnWalkMs = clock.Elapsed.TotalMilliseconds;
        _ownSkip = _lastOwnWalkMs <= OwnWalkTargetMs
            ? 0
            : Mathf.Min(MaxOwnSkips, Mathf.CeilToInt((float)(_lastOwnWalkMs / OwnWalkTargetMs)) - 1);
        _ownWalkNote = "STANDALONE WALK: the SCENE walk skipped this window, so this census walked "
                       + _ownWalkRenderers + " renderer(s) itself in "
                       + _lastOwnWalkMs.ToString("F1") + "ms (renderer sweep only — no SIM half, no "
                       + "root/shader tally, no texture census). It is rationed to "
                       + OwnWalkTargetMs.ToString("F0") + "ms/window, so the next "
                       + _ownSkip + " window(s) will skip it";
    }

    // ==========================================================================================
    //  ScenarioCamera — the game's own renderer of this scene
    // ==========================================================================================

    /// <summary>The game's own scenario camera, cached. <c>Camera.allCameras</c> ALLOCATES an array
    /// on every read, so it is probed at most once per window and only while unresolved — the same
    /// discipline VRRigDriver.HeadCamera.cs applies to the same lookup.</summary>
    private static Camera? ResolveScenarioCamera()
    {
        if (_scenarioCam != null)
            return _scenarioCam;
        try
        {
            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == "ScenarioCamera")
                {
                    _scenarioCam = all[i];
                    break;
                }
            }
        }
        catch (Exception)
        {
            _scenarioCam = null;
        }
        return _scenarioCam;
    }

    private static void Reset()
    {
        for (int i = 0; i < Pool.Count; i++)
            Recycle(Pool[i]);
        Pool.Clear();
        ShaderInterest.Clear();
        SeenRenderers.Clear();
        Scratch.Clear();
        _armed = false;
        _sampled = false;
        _slotsOffered = 0;
        _renderersOffered = 0;
        _boundsReads = 0;
        _matched = 0;
        _dropped = 0;
        _droppedMaxSpan = 0f;
        _poolMinScore = 0f;
        _distinctGlowShaders = 0;
        _layerPopKnown = false;
        _curRendererId = 0;
        _curMarks = Mark.None;
        _curSpan = 0f;
        _curSubmitted = false;
        _head = null;
        _headDepth = DepthTextureMode.None;
        _headKnown = false;
        _headName = "n/a";
        _headPath = RenderingPath.UsePlayerSettings;
        _headMask = 0;
        _scenarioKnown = false;
        _scenarioPath = RenderingPath.UsePlayerSettings;
        _scenarioMask = 0;
        _scenarioBuffers = 0;
        _projectionKnown = false;
        _pixelsPerUnit = 0f;
        _ranOwnWalk = false;
        _ownWalkRenderers = 0;
    }
}
