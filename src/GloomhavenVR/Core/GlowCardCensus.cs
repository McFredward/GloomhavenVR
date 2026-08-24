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
    private const int MaxCards = 16;

    /// <summary>How many of those get the FULL property/keyword/pass dump. The dump is the expensive
    /// and verbose part, and the ranking puts the subject at the top by construction.</summary>
    private const int FullDumpCards = 5;

    /// <summary>Ranked pool size. Candidates beyond this are dropped by SMALLEST span first and the
    /// count and largest dropped span are printed, so the cap never hides the subject silently.</summary>
    private const int MaxCandidates = 160;

    /// <summary>Compact one-line entries printed for pooled candidates that did not make MaxCards.</summary>
    private const int MaxTail = 44;

    /// <summary>Shader properties dumped per material before the dump says "+N more".</summary>
    private const int MaxProps = 44;

    /// <summary>
    /// Pixel span below which a card CANNOT be the photographed rectangle. The quads measure roughly
    /// 90x110 px of a 3840-px-wide screenshot, i.e. of order 100 px of a 3072x3264 eye; 24 px is a
    /// deliberately generous floor that still excludes the thousands of distant slots. Only cards at
    /// or above it, submitted, are allowed into the VERDICT.
    /// </summary>
    private const float SubjectMinPx = 24f;

    /// <summary>
    /// Upper edge of the subject band. Measured off the photographs rather than guessed: in
    /// <c>Wandproblem.jpg</c> (3840x2160) the two rectangles measure about 40x44 and 22x42 SCREEN
    /// pixels, and the eye texture is 3264 px tall against the screenshot's 2160, so their span in the
    /// units this census reports is of order 70-110 px. 250 is a generous ceiling around that and it
    /// is what keeps 9,000 px tree canopies and a 71,202 px ground plane from evicting them.
    /// </summary>
    private const float SubjectMaxPx = 250f;

    /// <summary>
    /// The size, in this census's own pixel units, the photographed rectangles actually are — and the
    /// point the subject band ranks TOWARDS rather than away from.
    ///
    /// <para>Within the band the score falls off with the ABSOLUTE LOG RATIO of a candidate's span to
    /// this number, so the ordering is scale-symmetric: 45 px and 180 px rank equally, 22 px and 360 px
    /// rank equally. That matters because the alternative orderings are both wrong. "Biggest first" is
    /// what ModBuild 253 did and it filled every record with ground planes. "Closest to 90 px, linear"
    /// would over-fit an estimate taken off a JPEG with a ruler; if that estimate is out by a factor
    /// of two the log form still keeps the subject near the top, and a linear one would not.</para>
    /// </summary>
    private const float SubjectTargetPx = 90f;

    /// <summary>Score floor for the subject band, above anything the out-of-band product can reach.</summary>
    private const float BandFloor = 1_000_000f;

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
        /// <summary>The shader declares no lighting pass at all: this surface does not take scene
        /// lighting. See <see cref="IsUnlitShader"/>.</summary>
        Unlit = 32,
        /// <summary>The material carries a non-black emission colour. See <see cref="IsEmissive"/>.</summary>
        Emissive = 64,
    }

    /// <summary>LightMode tag values that mean "the engine's lighting loop draws this pass". A shader
    /// with none of them takes no scene lighting. Read off the game's own shaders in the ModBuild 254
    /// hardware log, where every floor and masonry material printed
    /// <c>sub0{FORWARDBASE,FORWARDADD,DEFERRED}</c>.</summary>
    private static readonly string[] LitModes =
    {
        "ForwardBase", "ForwardAdd", "Deferred", "PrepassBase", "PrepassFinal",
        "Vertex", "VertexLM", "VertexLMRGBM"
    };

    private static readonly UnityEngine.Rendering.ShaderTagId LitModeTag = new("LightMode");

    /// <summary>Emission-ish colour properties, probed by name. Unity exposes no generic "is this
    /// emissive" query, and the game's Amplify kit does not use Unity's <c>_EmissionColor</c>
    /// exclusively.</summary>
    private static readonly int[] EmissionProps = BuildIds(new[]
    {
        "_EmissionColor", "_Emission", "_EmissiveColor", "_EmissiveColour",
        "_GlowColor", "_Glow", "_SelfIllum", "_SelfIllumColor"
    });

    /// <summary>Brightest channel an emission colour must exceed to count. Low enough to catch a dim
    /// authored glow, high enough that a lit shader's zeroed emission slot does not vote.</summary>
    private const float EmissionFloor = 0.05f;

    /// <summary>Pool slots reserved for the subject band, and for everything else. See <see cref="Insert"/>.</summary>
    private const int BandQuota = 96;

    private const int GeneralQuota = MaxCandidates - BandQuota;

    /// <summary>Per-shader facts, computed once per distinct Shader object and cached by instance id.</summary>
    private readonly struct ShaderFacts
    {
        public ShaderFacts(bool glow, bool unlit)
        {
            Glow = glow;
            Unlit = unlit;
        }

        public readonly bool Glow;
        public readonly bool Unlit;
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

        /// <summary>
        /// Ranking key, and the second thing ModBuild 253 got wrong. 253 ranked by raw span, so the
        /// pool filled with the biggest things on screen — a 71,202 px ground plane, a 17,983 px star
        /// dome, 9,000 px tree canopies — while the photographed rectangles are roughly SEVENTY pixels
        /// across. Bigger is not more likely to be the subject; being the SHAPE AND SIZE IN THE PHOTO
        /// is.
        ///
        /// <para>So there is a SUBJECT BAND: submitted, plate-shaped, and between
        /// <see cref="SubjectMinPx"/> and <see cref="SubjectMaxPx"/> pixels across. Everything in that
        /// band outranks everything outside it by construction (the +<see cref="BandFloor"/> term),
        /// and inside the band the ordering is by span. Outside it the old span product still applies,
        /// so the big context objects are still pooled and still printed — they just cannot evict the
        /// class the report is about. The score is printed on every record so this ordering can be
        /// checked rather than trusted.</para>
        /// </summary>
        public float Score => InBand
            ? BandFloor - Mathf.Abs(Mathf.Log(Mathf.Max(Span, 1f) / SubjectTargetPx)) * 1000f
            : Mathf.Min(BandFloor - 100_000f,
                        Span * ((Marks & Mark.VrOnly) != 0 ? 8f : 1f)
                             * ((Marks & Mark.Plate) != 0 ? 3f : 1f)
                             * (Submitted ? 1f : 0.05f));

        public int Queue = -1;

        /// <summary>The shader takes no scene lighting (<see cref="IsUnlitShader"/>).</summary>
        public bool Unlit;

        /// <summary>The material carries a live emission colour (<see cref="IsEmissive"/>).</summary>
        public bool Emissive;

        /// <summary>Does not depend on the room's lights for its brightness — either because nothing
        /// lights it or because it lights itself.</summary>
        public bool SelfLit => Unlit || Emissive;

        /// <summary>Alpha-test or later. Deliberately 2450 and not 2900 here: the question this term
        /// answers is "could this be drawn as a card rather than as ground", and cutout counts.</summary>
        public bool Transparent => Queue >= 2450;

        /// <summary>
        /// In the subject band. THIS IS THE TEST ModBuild 254 GOT WRONG and the correction is the whole
        /// point of this round: 254 required only SHAPE AND SIZE, and 160 of 160 eligible cards came
        /// back as ordinary lit floor geometry — <c>FR_Floor_Grass_Half_01</c>, <c>FR_Floor_Grass_BAY</c>,
        /// <c>FR_Floor_Scatter_Grass_Small_01</c> — because a floor hex IS a flat quad of about the
        /// right size. Ranking by shape repeated ModBuild 253's ranking-by-size failure one level down.
        ///
        /// <para>So membership now also requires the property the subject must have and a floor cannot:
        /// it must not owe its brightness to the room's lights. The photographs are the argument — the
        /// rectangles are pale and bright in a night scene where the masonry they sit on is nearly
        /// black and a candle two metres away renders warm and soft. A lit, opaque, queue-1900
        /// <c>_GROUND</c> material cannot do that. Either the shader has no lighting pass, or the
        /// material carries emission, or it draws in a transparent queue.</para>
        ///
        /// <para>And the shape term stays, but transparency can substitute for it: a particle card's
        /// bounds are the EMITTER volume, not the quad, so a genuine VFX card can fail the plate test
        /// through no fault of its own.</para>
        /// </summary>
        public bool InBand => Submitted
                              && Span >= SubjectMinPx
                              && Span <= SubjectMaxPx
                              && (SelfLit || Transparent)
                              && ((Marks & Mark.Plate) != 0 || Transparent);
    }

    private static readonly List<Candidate> Pool = new(MaxCandidates);
    private static readonly Stack<Candidate> Spare = new(MaxCandidates);
    private static readonly Dictionary<int, ShaderFacts> ShaderFactsCache = new(64);
    private static readonly List<string> Scratch = new(8);
    private static MaterialPropertyBlock? _blockScratch;

    /// <summary>Per-layer renderer population, handed over by the walk that produced it so the
    /// path-contrast block can say how many renderers sit on the layers only VR draws.</summary>
    private static readonly int[] LayerPop = new int[32];
    private static readonly int[] LayerVis = new int[32];
    private static bool _layerPopKnown;

    // ---- per-renderer latch, set by OfferRenderer and consumed by Offer -----------------------
    private static int _curRendererId;
    private static bool _curIsModLayer;
    private static Material? _curMat;
    private static Shader? _curSh;
    private static int _curQueue = -1;
    private static bool _curSlotUnlit;
    private static int _curSlotRank = -1;
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

    /// <summary>Renderers refused because they are on the mod's OWN layer. Printed, because an
    /// exclusion nobody can see is indistinguishable from a scan that never ran.</summary>
    private static int _modLayerSkipped;
    private static int _matched;
    private static int _dropped;
    private static float _droppedMaxSpan;
    private static float _bandMin;
    private static float _genMin;
    private static int _bandCount;
    private static int _genCount;

    /// <summary>Candidates that were the right SHAPE AND SIZE for the band. The difference between
    /// this and the number actually in the band is what the self-lit requirement removed, and it is
    /// printed: a filter nobody can see the effect of is a filter nobody can check.</summary>
    private static int _bandShaped;
    private static int _bandSeen;
    private static int _distinctUnlitShaders;
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
                BandRecurrence.Clear();
                _windowsWithBand = 0;
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

        // THE MOD'S OWN LAYER IS NOT A CANDIDATE. ModBuild 253's hardware sample proved this the
        // expensive way: all 14 detailed records and 41 of the 96 pooled candidates were the mod's
        // own environment room — a 1005 wu ground plane, a star dome, fog emitters, the hand meshes
        // and the pointer laser — because they are large, flat, transparent-queued AND on a layer the
        // ScenarioCamera does not render, which lights up four marks at once and scores x8 for being
        // "VR-only". They are VR-only by construction and they are not what the user photographed.
        // The subject is GAME geometry, so the pool holds game geometry.
        _curRendererId = r.GetInstanceID();
        _curMarks = Mark.None;
        _curSpan = 0f;
        _curSubmitted = submitted;
        _curBounds = default;
        _curMat = null;
        _curSh = null;
        _curQueue = -1;
        _curSlotUnlit = false;
        _curSlotRank = -1;

        _curIsModLayer = layer == VRLayers.ModLayer;
        if (_curIsModLayer)
        {
            _modLayerSkipped++;
            return;
        }

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
        // Same exclusion as OfferRenderer, latched there so this stays one boolean per slot.
        if (_curIsModLayer && _curRendererId == r.GetInstanceID())
            return;
        _slotsOffered++;

        int shaderId = sh.GetInstanceID();
        if (!ShaderFactsCache.TryGetValue(shaderId, out ShaderFacts facts))
        {
            facts = new ShaderFacts(IsGlowShader(sh), IsUnlitShader(sh));
            ShaderFactsCache[shaderId] = facts;
            if (facts.Glow)
                _distinctGlowShaders++;
            if (facts.Unlit)
                _distinctUnlitShaders++;
        }

        int queue;
        try { queue = mat.renderQueue; }
        catch (Exception) { queue = -1; }
        if (facts.Glow || queue >= 2900 || facts.Unlit)
            _matched++;
        if (facts.Glow)
            _curMarks |= Mark.Shader;
        if (queue >= 2900)
            _curMarks |= Mark.Queue;
        if (facts.Unlit)
            _curMarks |= Mark.Unlit;

        // Keep ONE representative slot per renderer, and make it the most diagnostic one rather than
        // slot 0: a two-slot prop whose second material is the glowing card must not be described by
        // its first, boring material.
        int rank = (facts.Unlit ? 4 : 0) + (queue >= 2450 ? 2 : 0) + (facts.Glow ? 1 : 0);
        if (_curMat == null || rank > _curSlotRank)
        {
            _curMat = mat;
            _curSh = sh;
            _curQueue = queue;
            _curSlotUnlit = facts.Unlit;
            _curSlotRank = rank;
        }
    }

    /// <summary>
    /// Close one renderer and pool it if anything marked it. Registration moved here in ModBuild 255
    /// so that a candidate is only ever scored ONCE, with its material already known: the band test
    /// below depends on the render queue and on whether the shader is lit, and neither is available
    /// while the geometry pass is running. The previous shape registered on geometry and patched the
    /// entry afterwards, which meant a card could be evicted under a score computed before the facts
    /// that decide its class had been read.
    /// </summary>
    internal static void EndRenderer(Renderer r)
    {
        if (!_armed || r == null || _curIsModLayer)
            return;
        if (_curRendererId != r.GetInstanceID() || _curMarks == Mark.None)
            return;

        Candidate c = Spare.Count > 0 ? Spare.Pop() : new Candidate();
        c.R = r;
        c.Mat = _curMat;
        c.Sh = _curSh;
        c.Queue = _curQueue;
        c.Unlit = _curSlotUnlit;
        c.Submitted = _curSubmitted;
        c.Span = _curSpan;
        c.Marks = _curMarks;
        c.B = _curBounds;
        c.Emissive = false;

        // The emission probe is the only per-candidate material read here, and it is gated on the
        // candidate already being the right shape and size — so it runs for a handful of renderers a
        // window, never for the population.
        bool rightShape = c.Submitted && c.Span >= SubjectMinPx && c.Span <= SubjectMaxPx
                          && ((c.Marks & Mark.Plate) != 0 || c.Queue >= 2450);
        if (rightShape)
        {
            _bandShaped++;
            if (!c.Unlit)
                c.Emissive = IsEmissive(c.Mat);
        }
        if (c.Emissive)
            c.Marks |= Mark.Emissive;

        Insert(c);
    }

    /// <summary>
    /// Pool one candidate under a TWO-QUOTA policy: the subject band and everything else compete
    /// separately and can only evict their own kind.
    ///
    /// <para>WHY TWO QUOTAS. ModBuild 254's single pool came back <b>160 of 160 in-band</b> — the band
    /// had eaten the entire census and there was no context left in it at all. That is bad twice over:
    /// it hides the wall and prop geometry another lane is reading this line for, and it means one
    /// over-broad band test can silently delete every other kind of record. A quota makes that
    /// impossible by construction: the band cannot exceed <see cref="BandQuota"/> entries and the rest
    /// of the scene always keeps <see cref="GeneralQuota"/>.</para>
    /// </summary>
    private static void Insert(Candidate c)
    {
        bool band = c.InBand;
        if (band)
            _bandSeen++;
        int count = band ? _bandCount : _genCount;
        int quota = band ? BandQuota : GeneralQuota;

        if (count < quota)
        {
            Pool.Add(c);
            if (band) _bandCount++; else _genCount++;
            if (count + 1 == quota)
                RecomputeMin(band);
            return;
        }

        float min = band ? _bandMin : _genMin;
        if (c.Score <= min)
        {
            DropIt(c);
            return;
        }

        int worst = -1;
        float worstScore = float.MaxValue;
        for (int i = 0; i < Pool.Count; i++)
        {
            Candidate p = Pool[i];
            if (p.InBand != band)
                continue;
            float s = p.Score;
            if (s >= worstScore)
                continue;
            worstScore = s;
            worst = i;
        }
        if (worst < 0)
        {
            DropIt(c);
            return;
        }
        DropIt(Pool[worst]);
        Pool[worst] = c;
        RecomputeMin(band);
    }

    private static void DropIt(Candidate c)
    {
        _dropped++;
        if (c.Span > _droppedMaxSpan)
            _droppedMaxSpan = c.Span;
        Recycle(c);
    }

    private static void RecomputeMin(bool band)
    {
        float min = float.MaxValue;
        for (int i = 0; i < Pool.Count; i++)
        {
            Candidate p = Pool[i];
            if (p.InBand != band)
                continue;
            float s = p.Score;
            if (s < min)
                min = s;
        }
        if (band)
            _bandMin = min;
        else
            _genMin = min;
    }

    private static void Recycle(Candidate c)
    {
        c.R = null;
        c.Mat = null;
        c.Sh = null;
        c.Marks = Mark.None;
        c.Span = 0f;
        c.Queue = -1;
        c.Unlit = false;
        c.Emissive = false;
        c.Submitted = false;
        Spare.Push(c);
    }

    /// <summary>
    /// TRUE when this shader declares NO lighting pass at all — no ForwardBase, no ForwardAdd, no
    /// Deferred, no PrepassBase, no Vertex/VertexLM. Such a material does not take scene lighting: it
    /// draws its own colours whatever the room is doing.
    ///
    /// <para>THIS IS THE TEST ModBuild 254 WAS MISSING, and it is what the report has been pointing at
    /// since the first photograph. The subject is a PALE, BRIGHT rectangle in a night scene in which
    /// every masonry surface around it is nearly black and a candle two metres away renders warm and
    /// soft. Whatever it is, it is not being lit — and the floor hexes that swamped 254's band all
    /// carry <c>sub0{FORWARDBASE,FORWARDADD,DEFERRED}</c>, which is the signature of a fully lit
    /// surface shader. Shape and size could not tell those two apart; this can.</para>
    ///
    /// <para>Cached per SHADER instance id, so it is evaluated about forty times a window (the number
    /// of distinct shaders in the room) and never once per renderer — the pass-tag walk marshals a
    /// managed string per pass and would be genuinely expensive at population scale. Conservative on
    /// failure: an unreadable tag set returns FALSE, because claiming "unlit" for something we could
    /// not read is exactly the ModBuild 251 mistake.</para>
    /// </summary>
    private static bool IsUnlitShader(Shader sh)
    {
        try
        {
            int subs = sh.subshaderCount;
            if (subs <= 0)
                return false;
            bool sawAnyPass = false;
            for (int s = 0; s < subs && s < 6; s++)
            {
                int pc = sh.GetPassCountInSubshader(s);
                for (int p = 0; p < pc && p < 12; p++)
                {
                    sawAnyPass = true;
                    string name = sh.FindPassTagValue(s, p, LitModeTag).name;
                    if (string.IsNullOrEmpty(name))
                        continue;
                    for (int i = 0; i < LitModes.Length; i++)
                    {
                        if (name.Equals(LitModes[i], StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }
            }
            return sawAnyPass;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// TRUE when the material carries an emission-ish colour bright enough to show. The second half of
    /// "does not take scene lighting": a LIT shader can still glow if its emission channel is driven,
    /// and excluding those would be the same over-tightening in the other direction. Probed by name
    /// against a short list because emission is not a property Unity exposes generically, and only for
    /// candidates already the right shape and size.
    /// </summary>
    private static bool IsEmissive(Material? mat)
    {
        if (mat == null)
            return false;
        for (int i = 0; i < EmissionProps.Length; i++)
        {
            try
            {
                if (!mat.HasProperty(EmissionProps[i]))
                    continue;
                Color c = mat.GetColor(EmissionProps[i]);
                if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > EmissionFloor)
                    return true;
            }
            catch (Exception)
            {
                // a property that is not a colour, or is unreadable, simply does not vote
            }
        }
        return false;
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
        // Same number, on a latch Reset() does NOT clear. The GATE DUMP runs AFTER Log() has reset
        // the window, and it needs this count to contrast a HIERARCHY walk against a
        // FindObjectsOfType SWEEP — the sweep skips DontSave objects and Apparance's generated
        // containers are HideAndDontSave, so the difference between the two counts is a measurement
        // of the population the census structurally cannot reach.
        _lastSweepRenderers = all.Length;
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
            EndRenderer(r);
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
        ShaderFactsCache.Clear();
        Scratch.Clear();
        _armed = false;
        _sampled = false;
        _slotsOffered = 0;
        _renderersOffered = 0;
        _boundsReads = 0;
        _modLayerSkipped = 0;
        _curIsModLayer = false;
        _matched = 0;
        _dropped = 0;
        _droppedMaxSpan = 0f;
        _bandMin = 0f;
        _genMin = 0f;
        _bandCount = 0;
        _genCount = 0;
        _bandShaped = 0;
        _bandSeen = 0;
        _distinctUnlitShaders = 0;
        _distinctGlowShaders = 0;
        _curMat = null;
        _curSh = null;
        _curQueue = -1;
        _curSlotUnlit = false;
        _curSlotRank = -1;
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
