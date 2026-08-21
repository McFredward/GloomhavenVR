using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// THE FLICKER, ROUND 8 — the first instrument in this family that measures the SAMPLING of a
/// floated panel instead of its C# state, and the first that carries a REFERENCE COLUMN.
///
/// <para>WHY A NEW CLASS AND NOT MORE OF <see cref="RenderTargetProbe"/>. Rounds 1-7 all measured
/// state: <see cref="PanelFlickerProbe"/> (three silent hardware sessions — the floated panels are
/// steady in both eyes), <see cref="CameraOrderProbe"/> (one camera-order shape for a whole session,
/// ZERO cameras between the two eye passes — both eyes read the same finished RenderTexture) and
/// <see cref="RenderTargetProbe"/> (3665 ticks, 0 alternations on the image, its texture, its writer
/// camera and the game's display manager). Every state-based explanation is excluded by measurement.
/// What none of them looked at is how many SOURCE TEXELS each surface crams into one RENDERED PIXEL.
/// That is a geometry question, it is answerable from Update, and it is the one thing the two
/// reported surfaces have in common.</para>
///
/// <para><b>AND THE TWO SURFACES ARE NOT THE SAME KIND OF OBJECT — this is what killed the
/// "RenderTexture is special" framing.</b> The live character render is a <c>RawImage</c> sampling
/// the editor-authored RenderTexture 'Character 3D assembly wide render texture' (1400x1024,
/// ARGB32, msaa=1, no mip chain by construction). The story picture is NOT: it is a plain
/// <c>UnityEngine.UI.Image</c> whose Sprite comes straight off Addressables as a <c>.png</c>
/// (decompiled <c>StoryImageViewer</c>: <c>Addressables.LoadAssetAsync&lt;Sprite&gt;(path + ".png")</c>
/// → <c>imageHolder.sprite = sprite</c>; the same asset also reaches <c>UIEventPanel.eventImage</c>
/// and <c>UILoadoutQuestWindow.imagePaper</c>). A RenderTexture and an Addressables sprite share
/// exactly one property that the four quiet portraits beside them do not: they are LARGE, DETAILED
/// images shown SMALL. <see cref="RenderTargetProbe"/> only ever enumerated RawImages whose texture
/// is a RenderTexture, so it was structurally incapable of seeing half of what he reported.</para>
///
/// <para>THE ARITHMETIC THIS EXISTS TO PUT IN THE LOG. A floated full-screen window is
/// 1920x1080 uGUI px at <c>CanvasScaleMm</c> 1 mm/px scaled to <c>ModalTargetWidthMeters</c> = 0.80 m
/// (ModalFallback.1.Core) and parked at <c>WindowDistanceMeters</c> = 1.2 m, i.e. ~36.9° x ~21.3°.
/// Against a ~3072x3264 per-eye target over a ~110°x~96° FOV (~28 px/° H) that window lands on
/// roughly 1030 x 720 rendered pixels — so its 1920 authored pixels are MINIFIED by ~1.9x before a
/// single texture is sampled. On a mipless bilinear texture minification is the textbook recipe for
/// texture-space shimmer, MSAA cannot touch it (MSAA resolves geometry edges only) and supersampling
/// makes the footprint smaller still. That is the same defect, and the same argument, that
/// <see cref="PanelMipBake"/> and <see cref="CardFaceMipBake"/> were built for after the user's
/// "Aliasing ist extrem stark bei den Rändern ... bei den Karten hast du das bereits geschafft".</para>
///
/// <para><b>AND HIS OWN SCREENSHOT ALREADY SHOWS IT.</b> <c>.planning/debug/flackern.jpg</c> — three
/// floated windows, one head pose, one frame. The LEFT party list is crisp everywhere. The MIDDLE
/// mercenary chooser's portrait thumbnails carry a regular diagonal cross-hatch moiré, and the RIGHT
/// character sheet has lost pieces of its glyphs ("H LDE DIE 2 E", "esundheit",
/// "Fertigkei s ar en", every tens digit in the stat column). Same frame, same distance, same eye.
/// That is the reference contrast this probe was going to have to construct, delivered for free, and
/// it says the defect is minification of detail — not anything specific to a RenderTexture. The full
/// reading is in <see cref="PanelScaleSentence"/>, which is also where the number that explains the
/// glyph half lives. Six rounds of instruments never opened it.</para>
///
/// <para>BUT THE ARITHMETIC ALONE PROVES NOTHING, because it applies to every graphic on the panel
/// and only two of them are reported. So this probe ships a REFERENCE COLUMN, which is the whole
/// point: it measures the COMPLAINED-ABOUT surface and the QUIET surfaces beside it, on the same
/// canvas, at the same distance, in the same log line. The four character portraits are small
/// source textures blown UP (magnification — stable under any filter); the character render and the
/// story picture are large source textures shrunk DOWN. If that contrast is real the log will print
/// it as two clearly separated numbers, and if it is not real the hypothesis dies on the same line.
/// One instrument that agrees with every broken build is worth nothing; one that can lose is worth
/// a round.</para>
///
/// <para>WHAT IT MEASURES, per Graphic, per scan:
/// <list type="bullet">
/// <item>TEXELS SHOWN — <c>Texture.width * |uvRect.width|</c> for a RawImage, <c>Sprite.rect</c> for
/// an Image. The actual source footprint the quad samples, not the texture's nominal size.</item>
/// <item>RENDERED PIXELS — the RectTransform's four world corners pushed through the LEFT EYE's
/// own projection (<c>WorldToViewportPoint(..., MonoOrStereoscopicEye.Left)</c>, not the mono
/// culling frustum) and scaled by <c>XRSettings.eyeTextureWidth/Height * renderViewportScale</c>.
/// Edge LENGTHS in pixel space, so a panel angled away from the head reports its foreshortened
/// size, which is the size that decides the aliasing.</item>
/// <item>MINIFICATION — texels per rendered pixel, the max of the two axes. &gt; 1 means the
/// surface is being shrunk and every texel it drops is a texel that can shimmer as the head moves.
/// &lt;= 1 means it is being magnified, which is stable under any filter and is the expected
/// reading for the quiet portraits.</item>
/// <item>MIP STATE — <c>mipmapCount</c>, <c>filterMode</c>, <c>anisoLevel</c>, and whether the
/// texture is a RenderTexture (which no mip bake can ever fix, see below).</item>
/// </list></para>
///
/// <para>AND IT APPLIES THE PROJECT'S OWN PROVEN REMEDY, GATED ON THE NUMBER IT JUST MEASURED.
/// This is deliberately NOT a fifth hypothesis: <see cref="CardFaceMipBake"/> is shipped, tested and
/// was accepted by the user for this exact complaint on the card faces, the initiative track and the
/// tooltips. The one surface family it was never wired to is the floated windows — grep the Rescan
/// call sites before this class and you find PropInfoSurface, EnemyRevealSurface, StatPanelSurface,
/// TablePanelSurfaces and WorldTooltips, and no window. So a graphic here is handed to the shared
/// bake cache ONLY when all of the following hold, and the log names each one it took:</para>
/// <list type="number">
/// <item>[WorldUI] PanelMipBake is on (the existing kill switch; no new knob was invented).</item>
/// <item>Its measured minification is at or above <see cref="BakeMinification"/> — the gate that
/// stops this from becoming the blanket bake of every menu. A magnified icon is skipped.</item>
/// <item>Its source texture has no mip chain already.</item>
/// <item>It is a Sprite or a plain Texture2D. <b>A RenderTexture is NEVER touched</b>, because there
/// is nothing safe to swap it for and because the game re-renders it every frame.</item>
/// </list>
///
/// <para>WHICH MAKES THE NEXT HARDWARE REPORT A DECISIVE EXPERIMENT RATHER THAN A FIFTH GUESS. The
/// bake can reach the story picture and cannot reach the character render. If both stop flickering,
/// the diagnosis was wrong about the mechanism (nothing here touched the RT). If the story picture
/// goes quiet and the character render does not, minification-of-mipless-detail is confirmed AND the
/// remaining work is scoped to exactly one surface. If neither changes, the numbers on the REFERENCE
/// line say whether minification was ever plausible, and round 9 starts somewhere else with that
/// ruled out. There is no outcome from which nothing is learned.</para>
///
/// <para>RESTORE — mutate-and-restore house style, same as <see cref="PanelMipBake.Restore"/>: every
/// panel this probe baked into is remembered by its Target RectTransform, and the originals go back
/// when the panel leaves <see cref="CanvasConversion.ActivePanels"/>, when the probe stands down, and
/// on <see cref="Shutdown"/>. Sprites restore through <see cref="CardFaceMipBake.RestoreSprites"/>
/// (the cache knows every replacement it minted); raw textures through the baked→original map here.
/// The baked textures themselves stay in the session-lifetime shared cache, which is the
/// CardFaceMipBake contract — a card face may be sampling the very copy a window shares.</para>
///
/// <para>Driven from <see cref="RenderTargetProbe.Tick"/> rather than from the tick site directly:
/// the Update seam that arms these probes lives in <c>ModalFallback.4.Tick.cs</c>, which this lane
/// does not own, and RenderTargetProbe is already called from there on exactly the right condition.</para>
/// </summary>
internal static class PanelSamplingProbe
{
    private const string Scope = "WorldUI";

    /// <summary>How often the panels are re-measured. Graphics are POOLED and REBUILT by the game
    /// (a window re-populating puts the original mipless sprite back), so this is also the re-assert
    /// cadence; 30 frames mirrors <see cref="PanelMipBake"/>'s ~1 s rescan without its cost.</summary>
    private const int ScanIntervalFrames = 30;

    /// <summary>How often the measurement is PRINTED, hit or no hit. A silent probe and a probe
    /// that never ran must never look the same again (RenderTargetProbe's standing rule).</summary>
    private const float SummaryIntervalSeconds = 10f;

    /// <summary>
    /// Texels-per-rendered-pixel at or above which a mipless surface is handed to the bake.
    /// 1.35 is chosen against the geometry, not by feel: a floated full-screen window is ~1.9x
    /// minified by construction (see the class doc), so 1.35 is comfortably below anything the
    /// complained-about surfaces can read while still excluding the near-1:1 and magnified chrome
    /// that made the 2026-08 COUNT-budget regression (menu chrome consumed every bake slot before
    /// the card art loaded — CardFaceMipBake.MaxBakedVramBytes tells that story in full). The gate
    /// is deliberately on the MEASURED number so this can never become a blanket bake.
    /// </summary>
    private const float BakeMinification = 1.35f;

    /// <summary>New bake ATTEMPTS per scan. A per-sprite bake needs a whole-atlas CPU readback the
    /// first time it meets an atlas (CardFaceMipBake.AtlasPixelsFor — cached, but the first one is a
    /// GPU stall), so a freshly opened window amortizes its bakes over a few frames instead of
    /// hitching once. Already-baked and already-refused graphics cost nothing and are not counted.</summary>
    private const int MaxBakeAttemptsPerScan = 4;

    /// <summary>Surfaces named individually in each report, worst minification first.</summary>
    private const int ReportedSurfaces = 6;

    /// <summary>A minification at or above this is called out as a suspect in the report text.</summary>
    private const float SuspectMinification = 1.35f;

    private static bool _armed;
    private static int _lastScanFrame = -1;
    private static float _nextSummary;
    private static bool _reportDue;

    /// <summary>Panel Target RectTransform instance id → the Target itself, for every panel this
    /// probe has baked into. Restore walks these and only these.</summary>
    private static readonly Dictionary<int, RectTransform> Baked = new(4);

    /// <summary>Baked raw-texture instance id → the game texture it replaced (RawImage path; there
    /// is no Sprite to hand back, so the mapping has to live here). First original wins, exactly as
    /// in <see cref="PanelMipBake"/>: restore must be deterministic.</summary>
    private static readonly Dictionary<int, Texture> RawOriginalByBaked = new(8);

    /// <summary>Source sprite/texture ids this probe has already ASKED the bake cache about. Only a
    /// first ask costs an attempt slot; every later one is a dictionary hit in the shared cache and
    /// is free, which is what lets a re-populated window get its swap back immediately (the game
    /// pools and rebuilds these graphics and puts the original mipless art back when it does).</summary>
    private static readonly HashSet<int> Attempted = new(64);

    /// <summary>Source ids the bake cache REFUSED (unbakeable sprite geometry, over the size cap,
    /// budget exhausted). Never asked again: the refusal is already on the record as a single
    /// MIP BAKE skip line, and re-asking every 30 frames would spend the attempt budget on graphics
    /// that can never take one.</summary>
    private static readonly HashSet<int> Refused = new(32);

    // Reused scan buffers — the List overloads of GetComponentsInChildren allocate nothing.
    private static readonly List<Image> ImageScratch = new(128);
    private static readonly List<RawImage> RawScratch = new(16);
    private static readonly List<Surface> Surfaces = new(128);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly StringBuilder Sb = new(1024);

    /// <summary>One warning per session if the scan throws — a measurement surprise must never
    /// break the panel it is measuring (the PanelMipBake latch).</summary>
    private static bool _errorLogged;

    /// <summary>One line per session if the eye target cannot be read, naming the consequence.</summary>
    private static bool _noEyeTargetLogged;

    /// <summary>One line per session if there is no head camera to project through — a SEPARATE
    /// latch from <see cref="_noEyeTargetLogged"/> on purpose: two different causes sharing one
    /// latch is how a log line comes to name the wrong consequence.</summary>
    private static bool _noHeadLogged;

    /// <summary>Total bakes this probe has caused, for the report line.</summary>
    private static int _bakedGraphics;

    // =====================================================================================
    // THE HAND-OFF TO THE EYE PROBE (ModBuild 191).
    //
    // <see cref="EyeFrameProbe"/> reads back the PIXELS the eye actually receives over a 64x64
    // window of one surface. Which surface it should look at is exactly the question this class
    // already answers every scan: the WORST-minified graphic on a floated panel is the one the user
    // reports, and the BEST-sampled graphic on the same canvas is the reference column that does
    // not flicker. Publishing those two RectTransforms here rather than letting the eye probe pick
    // its own means the two log lines describe THE SAME two surfaces and can be read against each
    // other without a remembered value — which is the whole reason the REFERENCE column exists.
    //
    // These are plain statics rather than an event because the eye probe samples from inside the
    // render loop, where nothing may be recomputed; it needs a value that was settled in Update.
    // =====================================================================================

    /// <summary>The worst-minified measurable graphic across all floated panels on the last scan —
    /// the surface the flicker is reported on. Null when nothing was measurable.</summary>
    internal static RectTransform? SubjectRect { get; private set; }

    /// <summary>Name and measured minification of <see cref="SubjectRect"/>, for the eye probe's log.</summary>
    internal static string SubjectLabel { get; private set; } = "<none>";

    /// <summary>The best-sampled measurable graphic across all floated panels on the last scan —
    /// the quiet reference column. Null when nothing was measurable.</summary>
    internal static RectTransform? ReferenceRect { get; private set; }

    /// <summary>Name and measured minification of <see cref="ReferenceRect"/>.</summary>
    internal static string ReferenceLabel { get; private set; } = "<none>";

    /// <summary>Frame the two rects above were chosen on — the eye probe prints its age so a stale
    /// pick (a window closed between the scan and the capture) is visible rather than silent.</summary>
    internal static int SelectionFrame { get; private set; } = -1;

    // Scan-scoped candidates. Surfaces is cleared PER PANEL, so the pick has to be accumulated
    // across panels and committed once at the end of the scan.
    private static RectTransform? _candWorstRect;
    private static RectTransform? _candBestRect;
    private static string _candWorstLabel = "<none>";
    private static string _candBestLabel = "<none>";
    private static float _candWorst;
    private static float _candBest;

    private static void BeginSelection()
    {
        _candWorstRect = null;
        _candBestRect = null;
        _candWorstLabel = "<none>";
        _candBestLabel = "<none>";
        _candWorst = float.MinValue;
        _candBest = float.MaxValue;
    }

    /// <summary>Fold this panel's measured surfaces into the scan-wide worst/best pick.</summary>
    private static void AccumulateSelection(string window)
    {
        for (int i = 0; i < Surfaces.Count; i++)
        {
            Surface s = Surfaces[i];
            if (s.Rect == null)
                continue;
            if (s.Minification > _candWorst)
            {
                _candWorst = s.Minification;
                _candWorstRect = s.Rect;
                _candWorstLabel = $"'{s.Name}' {s.Kind} on '{window}', {s.Minification:F2}x "
                                  + $"minified, mips={s.Mips}, {s.Filter}, aniso {s.Aniso}"
                                  + (s.WasBaked ? ", MIP-BAKED" : string.Empty);
            }
            if (s.Minification < _candBest)
            {
                _candBest = s.Minification;
                _candBestRect = s.Rect;
                _candBestLabel = $"'{s.Name}' {s.Kind} on '{window}', {s.Minification:F2}x "
                                 + $"minified, mips={s.Mips}, {s.Filter}, aniso {s.Aniso}"
                                 + (s.WasBaked ? ", MIP-BAKED" : string.Empty);
            }
        }
    }

    /// <summary>Commit the scan's pick. An empty scan KEEPS the previous pick rather than blanking
    /// it: a window that momentarily measures nothing must not make the eye probe stand down and
    /// then re-arm, because the burst it is in the middle of would be torn in half.</summary>
    private static void CommitSelection()
    {
        if (_candWorstRect == null)
            return;
        SubjectRect = _candWorstRect;
        SubjectLabel = _candWorstLabel;
        ReferenceRect = _candBestRect;
        ReferenceLabel = _candBestLabel;
        SelectionFrame = Time.frameCount;
    }

    private static void ClearSelection()
    {
        SubjectRect = null;
        ReferenceRect = null;
        SubjectLabel = "<none>";
        ReferenceLabel = "<none>";
        SelectionFrame = -1;
    }

    /// <summary>One measured graphic. Value type in a reused list: the scan allocates nothing.</summary>
    private readonly struct Surface
    {
        internal readonly string Name;
        internal readonly string Kind;
        /// <summary>The graphic's own rect — published to <see cref="EyeFrameProbe"/> so the pixel
        /// probe samples the very surface this line names, not one it picked for itself.</summary>
        internal readonly RectTransform? Rect;
        internal readonly float TexelsW;
        internal readonly float TexelsH;
        internal readonly float PixelsW;
        internal readonly float PixelsH;
        internal readonly float Minification;
        internal readonly int Mips;
        internal readonly FilterMode Filter;
        internal readonly int Aniso;
        internal readonly bool IsRenderTexture;
        internal readonly bool WasBaked;

        internal Surface(string name, string kind, RectTransform? rect, float texelsW, float texelsH,
            float pixelsW, float pixelsH, float minification, int mips, FilterMode filter, int aniso,
            bool isRenderTexture, bool wasBaked)
        {
            Name = name;
            Kind = kind;
            Rect = rect;
            TexelsW = texelsW;
            TexelsH = texelsH;
            PixelsW = pixelsW;
            PixelsH = pixelsH;
            Minification = minification;
            Mips = mips;
            Filter = filter;
            Aniso = aniso;
            IsRenderTexture = isRenderTexture;
            WasBaked = wasBaked;
        }
    }

    /// <summary>
    /// Arm/disarm with the floated-window layer and scan on <see cref="ScanIntervalFrames"/>.
    /// Called from <see cref="RenderTargetProbe.Tick"/> (see the class doc for why not from the
    /// tick site). Always safe to call; does nothing at all while no panel is floated.
    /// </summary>
    internal static void Tick(bool wanted)
    {
        // ModBuild 191: the PIXEL instrument rides the same arm condition and is driven from here
        // for the same reason this class is driven from RenderTargetProbe — the Update seam that
        // arms this family lives in ModalFallback.4.Tick.cs, which neither lane owns, and this
        // method is already called from it on exactly the right condition. It is called FIRST and
        // OUTSIDE the scan throttle below: the eye probe needs a per-frame arm/disarm edge (its
        // work happens on Camera.onPreRender, not here), whereas this class only re-measures every
        // ScanIntervalFrames. See EyeFrameProbe.
        EyeFrameProbe.Tick(wanted);

        if (!wanted)
        {
            if (_armed)
            {
                _armed = false;
                RestoreAll("stand-down (no floated panel left)");
                ClearSelection();
                _lastScanFrame = -1;
            }
            return;
        }

        if (!_armed)
        {
            _armed = true;
            _lastScanFrame = -1;
            _nextSummary = Time.unscaledTime + SummaryIntervalSeconds;
            VRLog.Info(Scope, "PANEL SAMPLING PROBE armed (ModBuild 189 — the first instrument in "
                              + "this family that measures GEOMETRY instead of C# state). For every "
                              + "graphic on every floated panel it computes TEXELS SHOWN / RENDERED "
                              + "PIXELS through the LEFT EYE's own projection, i.e. how many source "
                              + "texels are being crammed into one rendered pixel. Above 1.0 the "
                              + "surface is minified and a mipless bilinear texture will shimmer as "
                              + "the head moves — MSAA cannot touch that, it resolves geometry edges "
                              + "only. The report prints the complained-about surfaces AND the quiet "
                              + "ones beside them on the same canvas (the REFERENCE line), so the "
                              + "hypothesis can lose on its own numbers. NOTE for anyone reading the "
                              + "earlier rounds: the story picture is NOT a RenderTexture at all — it "
                              + "is an Addressables .png Sprite on a plain Image "
                              + "(decompiled StoryImageViewer), so RenderTargetProbe could never see "
                              + "it. That is why this probe enumerates every Graphic, not just the "
                              + "RenderTexture-backed ones.");
        }

        if (_lastScanFrame != -1 && Time.frameCount - _lastScanFrame < ScanIntervalFrames)
            return;
        _lastScanFrame = Time.frameCount;

        if (Time.unscaledTime >= _nextSummary)
        {
            _nextSummary = Time.unscaledTime + SummaryIntervalSeconds;
            _reportDue = true;
        }

        try
        {
            Scan();
        }
        catch (System.Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                VRLog.Warn(Scope, $"PANEL SAMPLING PROBE scan failed ({ex.GetType().Name}: "
                                  + $"{ex.Message}) — the consequence is that no minification numbers "
                                  + "and no mip bake reach the floated panels this session; every "
                                  + "graphic keeps exactly the texture the game gave it. Nothing was "
                                  + "written and nothing else changes.");
            }
        }
        _reportDue = false;
    }

    /// <summary>Full teardown: originals back, caches cleared. Called from the module's Shutdown so
    /// a ScriptEngine hot reload cannot leave a game window wearing our baked sprites.</summary>
    internal static void Shutdown()
    {
        EyeFrameProbe.Shutdown();
        _armed = false;
        _lastScanFrame = -1;
        RestoreAll("module shutdown");
        ClearSelection();
        Refused.Clear();
    }

    private static void Scan()
    {
        Camera? head = VRRigDriver.HeadCamera;
        IReadOnlyList<ConvertedPanel> panels = CanvasConversion.ActivePanels;

        // Panels that have gone home since the last scan get their originals back FIRST — the
        // Target survives the release (it is the game's own window, merely reparented), so the
        // restore still finds every graphic it swapped.
        RestoreDeparted(panels);

        if (head == null)
        {
            if (!_noHeadLogged)
            {
                _noHeadLogged = true;
                VRLog.Warn(Scope, "PANEL SAMPLING PROBE: no head camera (VRRigDriver.HeadCamera is "
                                  + "null). The consequence is that no rendered-pixel size can be "
                                  + "computed, so no minification is measured and no mip bake fires. "
                                  + "The probe stands down for this scan and retries on the next one.");
            }
            return;
        }
        if (!TryEyeTarget(out float eyePxW, out float eyePxH))
            return;

        bool bakeAllowed = WorldUIConfig.PanelMipBake != null && WorldUIConfig.PanelMipBake.Value;
        int attempts = 0;
        BeginSelection();

        for (int p = 0; p < panels.Count; p++)
        {
            ConvertedPanel panel = panels[p];
            if (panel == null || !panel.IsAlive || panel.HostGo == null)
                continue;

            Surfaces.Clear();
            MeasureAndTreat(panel, head, eyePxW, eyePxH, bakeAllowed, ref attempts);
            AccumulateSelection(panel.Target != null ? panel.Target.name : "<released>");
            if (_reportDue)
                Report(panel, head, eyePxW, eyePxH);
        }
        Surfaces.Clear();
        CommitSelection();
    }

    private static void MeasureAndTreat(ConvertedPanel panel, Camera head, float eyePxW, float eyePxH,
        bool bakeAllowed, ref int attempts)
    {
        // includeInactive: false. A hidden graphic has no rendered size, cannot alias and must not
        // spend a bake attempt — unlike PanelMipBake, whose surfaces toggle sub-widgets constantly,
        // this probe's gate IS the measured on-screen size, which a hidden graphic does not have.
        ImageScratch.Clear();
        panel.HostGo.GetComponentsInChildren(includeInactive: false, ImageScratch);
        for (int i = 0; i < ImageScratch.Count; i++)
        {
            Image img = ImageScratch[i];
            if (img == null || !Visible(img))
                continue;
            Sprite? sprite = img.sprite;
            if (sprite == null)
                continue;
            Texture2D? tex = sprite.texture;
            if (tex == null)
                continue;
            if (!TryRenderedSize(img.rectTransform, head, eyePxW, eyePxH, out float pxW, out float pxH))
                continue;

            float texelsW = sprite.rect.width;
            float texelsH = sprite.rect.height;
            float min = Minification(texelsW, texelsH, pxW, pxH);
            bool baked = CardFaceMipBake.IsBakedSprite(sprite);

            if (!baked && bakeAllowed && min >= BakeMinification && tex.mipmapCount <= 1
                && MayAsk(sprite.GetInstanceID(), ref attempts))
            {
                Sprite? replacement = CardFaceMipBake.ReplacementFor(sprite);
                if (replacement != null)
                {
                    img.sprite = replacement;
                    baked = true;
                    _bakedGraphics++;
                    Remember(panel);
                }
                else
                {
                    Refused.Add(sprite.GetInstanceID());
                }
            }

            Surfaces.Add(new Surface(img.name, $"Image[{img.type}]", img.rectTransform, texelsW,
                texelsH, pxW, pxH, min, tex.mipmapCount, tex.filterMode, tex.anisoLevel,
                isRenderTexture: false, baked));
        }
        ImageScratch.Clear();

        RawScratch.Clear();
        panel.HostGo.GetComponentsInChildren(includeInactive: false, RawScratch);
        for (int i = 0; i < RawScratch.Count; i++)
        {
            RawImage raw = RawScratch[i];
            if (raw == null || !Visible(raw))
                continue;
            Texture? tex = raw.texture;
            if (tex == null)
                continue; // provider unloaded, or destroyed — the Unity-null check matters
            if (!TryRenderedSize(raw.rectTransform, head, eyePxW, eyePxH, out float pxW, out float pxH))
                continue;

            Rect uv = raw.uvRect;
            float texelsW = tex.width * Mathf.Max(Mathf.Abs(uv.width), 0.0001f);
            float texelsH = tex.height * Mathf.Max(Mathf.Abs(uv.height), 0.0001f);
            float min = Minification(texelsW, texelsH, pxW, pxH);

            var rt = tex as RenderTexture;
            bool isRt = rt != null;
            bool baked = RawOriginalByBaked.ContainsKey(tex.GetInstanceID());
            int mips = isRt ? (rt!.useMipMap ? rt.mipmapCount : 1) : tex.mipmapCount;

            // A RenderTexture is NEVER swapped: it is re-rendered by the game every frame, so a
            // static copy would freeze the character, and there is nothing else safe to point the
            // RawImage at. It is measured and reported, and that is all.
            if (!isRt && !baked && bakeAllowed && min >= BakeMinification
                && tex is Texture2D flat && flat.mipmapCount <= 1
                && MayAsk(flat.GetInstanceID(), ref attempts))
            {
                Texture2D? bakedTex = CardFaceMipBake.BakedTextureFor(flat);
                if (bakedTex != null)
                {
                    int bakedId = bakedTex.GetInstanceID();
                    if (!RawOriginalByBaked.ContainsKey(bakedId))
                        RawOriginalByBaked[bakedId] = flat;
                    raw.texture = bakedTex; // identical dimensions → the game's uvRect stays valid
                    baked = true;
                    mips = bakedTex.mipmapCount;
                    _bakedGraphics++;
                    Remember(panel);
                }
                else
                {
                    Refused.Add(flat.GetInstanceID());
                }
            }

            Surfaces.Add(new Surface(raw.name, isRt ? "RawImage[RenderTexture]" : "RawImage[Texture2D]",
                raw.rectTransform, texelsW, texelsH, pxW, pxH, min, mips, tex.filterMode,
                tex.anisoLevel, isRt, baked));
        }
        RawScratch.Clear();
    }

    /// <summary>
    /// May this source id be handed to the bake cache on this scan? A FIRST ask costs one of the
    /// per-scan attempt slots, because the first sprite off a given atlas triggers that atlas's
    /// whole-CPU readback (a GPU stall, cached thereafter) and a freshly opened window must amortize
    /// those over frames rather than hitch once. Every LATER ask is free — the cache answers from a
    /// dictionary — and is deliberately still allowed, because the game pools and rebuilds these
    /// graphics and puts the original mipless art back when it does; without the re-ask the swap
    /// would be lost on the first repopulate and never return.
    /// </summary>
    private static bool MayAsk(int sourceId, ref int attempts)
    {
        if (Refused.Contains(sourceId))
            return false;
        if (Attempted.Contains(sourceId))
            return true; // cache hit — free
        if (attempts >= MaxBakeAttemptsPerScan)
            return false;
        attempts++;
        Attempted.Add(sourceId);
        return true;
    }

    /// <summary>
    /// Rendered size of a RectTransform in EYE-TARGET PIXELS, measured as the pixel-space LENGTHS of
    /// its bottom and left edges. Edge lengths rather than an axis-aligned bounding box because a
    /// floated window in the map-room arc is yawed away from the head by up to 85° — its
    /// foreshortened size is the size that decides the sampling, and a bounding box would report the
    /// unforeshortened one and understate the minification.
    ///
    /// <para>The projection is the LEFT EYE's (<c>MonoOrStereoscopicEye.Left</c>), not the camera's
    /// mono matrix: Unity's mono frustum on an XR camera is the CULLING frustum that encloses both
    /// eyes, so it is wider than either eye actually renders and would understate pixels-per-degree.
    /// The two eyes' projections differ only by their off-centre split, which does not change pixel
    /// density, so the left eye stands for both — and <see cref="CameraOrderProbe"/> already proved
    /// both eyes sample the identical texture.</para>
    ///
    /// <para>The rig scale (~198 game units per metre in the map room) needs no correction here and
    /// must not get one: panel and camera live in the same scaled world, so the projected result is
    /// already what the eye sees. That is exactly the trap the "Angular sizing needs the scale"
    /// round fell into from the other side — a bound in metres compared against a world-unit
    /// product. Nothing on this path is expressed in metres at all.</para>
    /// </summary>
    private static bool TryRenderedSize(RectTransform rect, Camera head, float eyePxW, float eyePxH,
        out float pixelsW, out float pixelsH)
    {
        pixelsW = 0f;
        pixelsH = 0f;
        if (rect == null)
            return false;
        rect.GetWorldCorners(Corners); // 0 = bottom-left, 1 = top-left, 2 = top-right, 3 = bottom-right

        Camera.MonoOrStereoscopicEye eye = XRSettings.isDeviceActive
            ? Camera.MonoOrStereoscopicEye.Left
            : Camera.MonoOrStereoscopicEye.Mono;
        Vector3 bl = head.WorldToViewportPoint(Corners[0], eye);
        Vector3 tl = head.WorldToViewportPoint(Corners[1], eye);
        Vector3 br = head.WorldToViewportPoint(Corners[3], eye);
        // z <= 0 means the corner is behind the eye: the viewport x/y are mirrored garbage there and
        // would produce a fictitious size. A panel partly behind the head is simply not measured
        // this scan; the arc puts it back in front within a frame or two.
        if (bl.z <= 0f || tl.z <= 0f || br.z <= 0f)
            return false;

        pixelsW = PixelDistance(bl, br, eyePxW, eyePxH);
        pixelsH = PixelDistance(bl, tl, eyePxW, eyePxH);
        return pixelsW >= 1f && pixelsH >= 1f;
    }

    private static float PixelDistance(Vector3 a, Vector3 b, float eyePxW, float eyePxH)
    {
        float dx = (b.x - a.x) * eyePxW;
        float dy = (b.y - a.y) * eyePxH;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Texels per rendered pixel — the max of the two axes, because shimmer on either axis
    /// is shimmer. 1.0 = one texel per pixel; above that the surface is dropping texels.</summary>
    private static float Minification(float texelsW, float texelsH, float pixelsW, float pixelsH)
        => Mathf.Max(texelsW / Mathf.Max(pixelsW, 0.01f), texelsH / Mathf.Max(pixelsH, 0.01f));

    /// <summary>The per-eye render target in pixels, including the live resolution and viewport
    /// scale levers — the same numbers <c>Rig/RenderQuality</c>'s EYE-TARGET DIAG prints, so the two
    /// log lines are directly comparable. Falls back to the desktop window outside XR (dev mode).</summary>
    private static bool TryEyeTarget(out float pxW, out float pxH)
    {
        float viewport = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int w = XRSettings.eyeTextureWidth;
        int h = XRSettings.eyeTextureHeight;
        if (w < 2 || h < 2)
        {
            w = Screen.width;
            h = Screen.height;
            viewport = 1f;
        }
        pxW = w * viewport;
        pxH = h * viewport;
        if (pxW >= 2f && pxH >= 2f)
            return true;
        if (!_noEyeTargetLogged)
        {
            _noEyeTargetLogged = true;
            VRLog.Warn(Scope, "PANEL SAMPLING PROBE: neither XRSettings.eyeTexture* nor Screen "
                              + "reports a usable render target size. The consequence is that no "
                              + "minification can be computed, so nothing is measured and no mip bake "
                              + "fires — every graphic keeps the texture the game gave it.");
        }
        return false;
    }

    private static bool Visible(Graphic g)
        => g.enabled && g.gameObject.activeInHierarchy && g.color.a > 0.01f
           && (g.canvasRenderer == null || g.canvasRenderer.GetAlpha() > 0.01f);

    private static void Remember(ConvertedPanel panel)
    {
        RectTransform target = panel.Target;
        if (target == null)
            return;
        Baked[target.GetInstanceID()] = target;
    }

    /// <summary>Restore every panel that is no longer floated. Runs before the measurement so a
    /// window that closed this frame is already back to its authored graphics.</summary>
    private static void RestoreDeparted(IReadOnlyList<ConvertedPanel> live)
    {
        if (Baked.Count == 0)
            return;
        List<int>? gone = null;
        foreach (KeyValuePair<int, RectTransform> entry in Baked)
        {
            bool stillLive = false;
            for (int i = 0; i < live.Count; i++)
            {
                ConvertedPanel panel = live[i];
                if (panel != null && panel.Target != null
                    && panel.Target.GetInstanceID() == entry.Key)
                {
                    stillLive = true;
                    break;
                }
            }
            if (stillLive)
                continue;
            (gone ??= new List<int>(2)).Add(entry.Key);
        }
        if (gone == null)
            return;
        for (int i = 0; i < gone.Count; i++)
        {
            if (Baked.TryGetValue(gone[i], out RectTransform target))
                RestoreOne(target);
            Baked.Remove(gone[i]);
        }
    }

    private static void RestoreAll(string why)
    {
        if (Baked.Count == 0)
            return;
        int n = Baked.Count;
        foreach (KeyValuePair<int, RectTransform> entry in Baked)
            RestoreOne(entry.Value);
        Baked.Clear();
        VRLog.Info(Scope, $"PANEL SAMPLING PROBE restored {n} panel(s) to their authored graphics "
                          + $"({why}). The baked copies stay in the shared CardFaceMipBake cache — "
                          + "that cache is session-lifetime by contract because a card face may be "
                          + "sampling the very copy a window shared.");
    }

    /// <summary>Hand one panel's graphics their originals back. Guarded: a restore surprise must
    /// never break the shutdown path it is on.</summary>
    private static void RestoreOne(RectTransform? target)
    {
        if (target == null)
            return;
        try
        {
            CardFaceMipBake.RestoreSprites(target);
            if (RawOriginalByBaked.Count == 0)
                return;
            RawScratch.Clear();
            target.GetComponentsInChildren(includeInactive: true, RawScratch);
            for (int i = 0; i < RawScratch.Count; i++)
            {
                RawImage raw = RawScratch[i];
                if (raw == null || raw.texture == null)
                    continue;
                if (RawOriginalByBaked.TryGetValue(raw.texture.GetInstanceID(), out Texture original))
                {
                    // An original the game has Destroyed since restores to null — the game's own
                    // "unloaded texture" state, which its provider re-fills on the next refresh.
                    raw.texture = original != null ? original : null;
                }
            }
            RawScratch.Clear();
        }
        catch (System.Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                VRLog.Warn(Scope, $"PANEL SAMPLING PROBE restore failed ({ex.GetType().Name}: "
                                  + $"{ex.Message}) — a floated panel may keep sampling mip-baked "
                                  + "copies of its own graphics after it returns to 2D. Visually "
                                  + "identical content at higher quality; nothing is lost, but it is "
                                  + "not the bit-for-bit restore this codebase promises.");
            }
        }
    }

    /// <summary>
    /// THE REPORT — one block per floated panel, printed on the summary cadence whether or not
    /// anything moved. Deliberately shaped so the hypothesis can be decided from the log alone:
    /// the worst-minified surfaces first, then the REFERENCE line naming the best-sampled ones on
    /// the SAME canvas at the SAME distance. If the character render and the story picture sit at
    /// the top and the portraits sit at the bottom, minification is the difference between the
    /// surface he reports and the surface he does not.
    /// </summary>
    private static void Report(ConvertedPanel panel, Camera head, float eyePxW, float eyePxH)
    {
        string window = panel.Target != null ? panel.Target.name : "<released>";
        string panelScale = PanelScaleSentence(panel, head, eyePxW, eyePxH);
        if (Surfaces.Count == 0)
        {
            VRLog.Info(Scope, $"PANEL SAMPLING '{window}':{panelScale} 0 measurable graphic(s) this "
                              + "scan (all hidden, transparent, zero-size or behind the eye). MEASURED "
                              + "AND EMPTY — this is not a probe that failed to run.");
            return;
        }

        // Insertion sort by minification, descending. The list is tens of entries and this runs
        // once per panel per 10 s; a comparison delegate would allocate on every report.
        for (int i = 1; i < Surfaces.Count; i++)
        {
            Surface key = Surfaces[i];
            int j = i - 1;
            while (j >= 0 && Surfaces[j].Minification < key.Minification)
            {
                Surfaces[j + 1] = Surfaces[j];
                j--;
            }
            Surfaces[j + 1] = key;
        }

        int suspects = 0;
        int mipless = 0;
        int renderTextures = 0;
        for (int i = 0; i < Surfaces.Count; i++)
        {
            if (Surfaces[i].Minification >= SuspectMinification)
                suspects++;
            if (Surfaces[i].Mips <= 1)
                mipless++;
            if (Surfaces[i].IsRenderTexture)
                renderTextures++;
        }

        Sb.Length = 0;
        Sb.Append("PANEL SAMPLING '").Append(window).Append("':").Append(panelScale).Append(' ')
          .Append(Surfaces.Count)
          .Append(" graphic(s) measured against a ").Append(eyePxW.ToString("F0")).Append('x')
          .Append(eyePxH.ToString("F0")).Append(" per-eye target (")
          .Append(XRSettings.stereoRenderingMode).Append(", viewportScale ")
          .Append(XRSettings.renderViewportScale.ToString("F2")).Append("); ")
          .Append(suspects).Append(" at or above ").Append(SuspectMinification.ToString("F2"))
          .Append("x minification, ").Append(mipless).Append(" mipless, ").Append(renderTextures)
          .Append(" RenderTexture-backed. WORST FIRST — ");
        int top = Mathf.Min(ReportedSurfaces, Surfaces.Count);
        for (int i = 0; i < top; i++)
            AppendSurface(Surfaces[i]);

        // THE REFERENCE COLUMN: the best-sampled surface on this same canvas. One number to compare
        // the suspect against, in the same line, so no future round has to trust a remembered value.
        Surface best = Surfaces[Surfaces.Count - 1];
        Sb.Append(" | REFERENCE (best-sampled graphic on this same canvas, same distance, same eye): ");
        AppendSurface(best);
        Sb.Append(" | READ IT LIKE THIS: minification is SOURCE TEXELS PER RENDERED PIXEL. At or "
                  + "below 1.00 the surface is magnified and is stable under any filter — that is the "
                  + "expected reading for the four character portraits he says do NOT flicker. Above "
                  + "1.00 with mips=1 the surface drops texels every frame the head moves, which is "
                  + "texture-space shimmer and reads as 'es flackert'. MSAA is irrelevant to it: MSAA "
                  + "resolves geometry edges, not texture sampling. If the surface he reports and the "
                  + "surfaces he does not are separated on this line, the hypothesis holds; if they "
                  + "read the same, it is dead and round 9 starts elsewhere.");
        if (renderTextures > 0)
        {
            Sb.Append(" NOTE: the RenderTexture-backed graphic(s) above are NOT bakeable — the game "
                      + "re-renders that texture every frame, so no mip copy can stand in for it. If "
                      + "the story picture goes quiet this build and the character render does not, "
                      + "that split is the confirmation, not a partial failure.");
        }
        Sb.Append(" Bakes caused by this probe so far: ").Append(_bakedGraphics).Append(". ")
          .Append(CardFaceMipBake.BudgetSummary).Append('.');
        VRLog.Info(Scope, Sb.ToString());
        Sb.Length = 0;
    }

    /// <summary>
    /// PANEL SCALE — authored uGUI pixels per rendered pixel for the whole host rect. The single
    /// number that decides the TEXT, and the one the user's own screenshot (.planning/debug,
    /// "flackern") points straight at.
    ///
    /// <para>WHAT THAT PHOTOGRAPH SHOWS, because it is better evidence than any of the seven rounds
    /// of theory. Three floated windows, one head pose, one frame. The LEFT party list renders
    /// perfectly: "Gloomhaven-Wohlstand 6", "Hilde Die 2Te", "Gold: 22" all crisp, portraits smooth.
    /// The MIDDLE mercenary chooser and the RIGHT character sheet are wrecked in two distinct ways —
    /// the middle panel's portrait thumbnails carry a regular diagonal cross-hatch (a moiré beat
    /// between the source texel grid and the screen grid, which is what minification of detailed art
    /// looks like when it is frozen in one frame and what "es flackert" looks like when the head
    /// moves), and the right panel's TEXT has lost pieces of its glyphs: "H LDE DIE 2 E",
    /// "esundheit", "Fertigkei s ar en", "Ve stär un gen", and every tens digit in the stat column
    /// gone. Thin high-contrast strokes vanishing in chunks is not compression and it is not a state
    /// bug: it is what happens when a 1-pixel-wide authored stroke is asked to land on less than one
    /// rendered pixel and the sample falls between strokes.</para>
    ///
    /// <para>So this number is reported per panel, next to the per-graphic minification, because the
    /// two failures have the same cause and different cures. Above 1.0 means the panel is drawn at
    /// LESS than its authored resolution — 1.86 (the value the geometry predicts for a full-screen
    /// floated window, see the class doc) means barely half, at which point a 1 px stroke is a
    /// coin-flip every frame. The four quiet portraits and the crisp left panel in that same
    /// photograph are the control: whatever this number reads for them is the number that does not
    /// flicker.</para>
    ///
    /// <para>WHAT MODBUILD 189 DID WITH THIS NUMBER, so the next reader does not re-derive it. Two
    /// things, and only one of them cost anything. (1) A BUG: <c>DeriveWindowScale</c> runs at
    /// convert time on the PRE-fit rect and was re-run after the content fit for the one-shot family
    /// ONLY, so every other window kept a full-screen window's shrink on a narrow fitted rect —
    /// 'New Party display' sat at <c>scale=0.083</c> (extraScale 0.417, i.e. 0.80 m / 1920 px) with
    /// a 328x1080 rect for 100 s of that very log, drawn 0.137 m wide where its own rule asks for
    /// 0.230 m. Fixed in <c>ModalFallback.TickWindowScaleRefit</c>; worth 1.68x of this number for
    /// free. (2) A TRADE: <c>[WorldUI] WindowLegibility</c>, default 1.25. Nothing else can buy
    /// rendered pixels — they are bought with SOLID ANGLE, so a nearer window at the same physical
    /// size is the identical purchase, not a cheaper one, and 1:1 for a 1920 px window costs ~57° of
    /// view, which is the width the user has already called too big. That sentence is the whole
    /// ceiling of this problem and it belongs next to the number that measures it.</para>
    ///
    /// <para>NOTE WHAT THIS BUILD CANNOT FIX. The mip bake reaches Sprites and Texture2D RawImages —
    /// the portraits, the frames, the ornamental bars. It does NOT reach text: uGUI Text and TMP
    /// draw from a font atlas through their own components, which this probe deliberately does not
    /// touch (an SDF atlas is not a candidate for a mip copy). If the glyph dropout survives this
    /// build while the thumbnail moiré goes away, that is the expected split and the text half wants
    /// a panel-scale change — more authored pixels per rendered pixel — not a texture change.</para>
    /// </summary>
    private static string PanelScaleSentence(ConvertedPanel panel, Camera head, float eyePxW, float eyePxH)
    {
        RectTransform? host = panel.HostRect;
        if (host == null)
            return " panel scale NOT MEASURED (no host rect);";
        if (!TryRenderedSize(host, head, eyePxW, eyePxH, out float pxW, out float pxH))
            return " panel scale NOT MEASURED (host rect behind the eye or sub-pixel);";
        Rect r = host.rect;
        if (r.width < 1f || r.height < 1f)
            return " panel scale NOT MEASURED (degenerate host rect);";
        float scaleW = r.width / pxW;
        float scaleH = r.height / pxH;
        float worst = Mathf.Max(scaleW, scaleH);
        string verdict = worst <= 1.0f
            ? "at or above authored resolution — thin strokes survive"
            : worst < 1.5f
                ? "below authored resolution — thin strokes are already lossy"
                : "WELL below authored resolution — a 1 px authored stroke lands on less than "
                  + "two-thirds of a rendered pixel and will drop in and out as the head moves";

        // ModBuild 189 — THE CAUSAL TERMS, on the same line as the symptom. Rounds 1-8 could each
        // say a panel was undersampled; none could say BY WHICH FACTOR OF WHAT, so every proposed
        // cure was arguable. Panel scale is a pure function of three things and nothing else:
        //   panel scale  =  (authored px) x (eye distance)  /  (world size x eye px per radian)
        // and the first three are all readable right here. Printing them turns the next hardware
        // log from "still bad" into "the window is X m across at Y m, which is Z° — the size dial
        // did/did not do what it claimed", which is the difference between a decidable round and a
        // tenth guess. The APPLIED SCALE term is the one that caught ModBuild 189's actual bug: a
        // 328 px window still carrying a 1920 px window's 0.417 shrink.
        float worldScale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
        Vector3 lossy = host.lossyScale;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        // Real metres: the diorama scale is on the rig, so world units / world scale is what a tape
        // measure held at the eye would read — and it is the only size statement that survives a zoom.
        float realW = r.width * lossy.x / worldScale;
        float realH = r.height * lossy.y / worldScale;
        float realDist = Vector3.Distance(host.position, head.transform.position) / worldScale;
        // extraScale x the player's own two-hand resize factor, i.e. everything between the authored
        // pixel and the metre that is NOT CanvasScaleMm and NOT the diorama scale.
        float applied = metersPerPixel > 1e-9f ? lossy.x / (metersPerPixel * worldScale) : 0f;
        float degW = 2f * Mathf.Atan2(realW * 0.5f, Mathf.Max(realDist, 1e-3f)) * Mathf.Rad2Deg;
        float degH = 2f * Mathf.Atan2(realH * 0.5f, Mathf.Max(realDist, 1e-3f)) * Mathf.Rad2Deg;

        return $" host {r.width:F0}x{r.height:F0} uGUI px drawn into {pxW:F0}x{pxH:F0} rendered px "
               + $"= panel scale {worst:F2} authored px per rendered px ({verdict})"
               + $" [CAUSE: {realW:F2}x{realH:F2} m at {realDist:F2} m = {degW:F0}°x{degH:F0}° of view, "
               + $"applied scale {applied:F3} (host shrink x player resize), {metersPerPixel * 1000f:F2} mm/px; "
               + $"reaching 1.00 from here needs {worst:F2}x more angle, i.e. {realW * worst:F2} m across "
               + $"at the same distance];";
    }

    private static void AppendSurface(Surface s)
    {
        Sb.Append('[').Append(s.Name).Append(' ').Append(s.Kind).Append(": ")
          .Append(s.TexelsW.ToString("F0")).Append('x').Append(s.TexelsH.ToString("F0"))
          .Append(" texels into ").Append(s.PixelsW.ToString("F0")).Append('x')
          .Append(s.PixelsH.ToString("F0")).Append(" px = ")
          .Append(s.Minification.ToString("F2")).Append("x, mips=").Append(s.Mips)
          .Append(' ').Append(s.Filter).Append(" aniso ").Append(s.Aniso)
          .Append(s.WasBaked ? ", MIP-BAKED" : string.Empty)
          .Append(s.IsRenderTexture ? ", RT (never bakeable)" : string.Empty).Append("] ");
    }
}
