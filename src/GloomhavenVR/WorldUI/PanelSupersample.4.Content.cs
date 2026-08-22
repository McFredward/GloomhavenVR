// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here. Part 4 answers exactly one question: WHAT RECTANGLE MUST THE CAPTURE FRAME so
// that switching the dial on can never take visible content away from the player.

using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- the capture frame ---------------------------------------------------------------------

    /// <summary>Corner scratch for the content walk. Deliberately NOT the report's
    /// <c>Corners</c> array: both run inside the same LateUpdate and sharing one buffer between two
    /// unrelated measurements is the kind of coupling that produces a wrong number once and then
    /// never again reproducibly.</summary>
    private static readonly Vector3[] ContentCorners = new Vector3[4];

    /// <summary>Work stack for <see cref="MeasureFrame"/>: a transform and the clip rectangle that
    /// is in force for it, in HOST-LOCAL uGUI pixels. Carrying the clip DOWN the walk is what makes
    /// the measurement O(subtree) instead of O(subtree x depth).</summary>
    private static readonly List<ClipFrame> ContentStack = new(256);

    private readonly struct ClipFrame
    {
        internal readonly Transform Transform;
        internal readonly Rect Clip;

        internal ClipFrame(Transform transform, Rect clip)
        {
            Transform = transform;
            Clip = clip;
        }
    }

    /// <summary>An unbounded rectangle to start the clip chain from (host-local uGUI px). Large
    /// enough that no authored uGUI rect can reach it, small enough that intersections stay exact in
    /// single precision.</summary>
    private static readonly Rect Unbounded = Rect.MinMaxRect(-1e6f, -1e6f, 1e6f, 1e6f);

    /// <summary>
    /// MEASURE THE CAPTURE FRAME — the union of the host rect and everything the window actually
    /// draws, in HOST-LOCAL uGUI pixels, clamped by <see cref="MaxContentExpansion"/>.
    ///
    /// <para><b>WHY THIS EXISTS.</b> ModBuild 192 framed the host rect exactly, on the argument that
    /// the dial's OFF and ON geometry should then be identical to the pixel. The hardware log
    /// falsified the premise that argument rested on: <i>"'New Party display' draws content larger
    /// than its host frame (1920x1080 vs 328x1080 uGUI px)"</i>. A world-space uGUI canvas does not
    /// clip at its own root rect, so in the OFF path that overspill IS drawn and IS visible; an
    /// orthographic camera framing the root rect cuts it off. Losing visible content is a worse
    /// defect than the shimmer this whole path removes, so the frame follows the content.</para>
    ///
    /// <para><b>WHAT IT COSTS.</b> The "OFF and ON are identical" promise becomes: identical
    /// whenever the content fits inside the host rect — which is the normal case, because the
    /// content fit centres content inside the host by construction
    /// (<c>CanvasConversion.FitContentPadding</c>) — and otherwise ON shows MORE than the host rect,
    /// never less. The quad grows with the frame and the image scale is unchanged (one authored
    /// pixel still maps to <c>Factor</c> render-target texels), so the growth is invisible except as
    /// transparent margin plus render-target cost. The state line reports the overspill in uGUI
    /// pixels every report, so a window that starts doing this is never a mystery.</para>
    ///
    /// <para><b>BIASED TOWARDS OVER-MEASURING, ON PURPOSE.</b> Over-measuring costs VRAM and adds
    /// transparent margin; under-measuring CROPS. So the drawing test is deliberately permissive
    /// (an enabled, un-culled Graphic with a non-zero own alpha counts, regardless of any
    /// CanvasGroup fade above it) and the only hard limit is
    /// <see cref="MaxContentExpansion"/> — which is reported as CLAMPED and warned about once,
    /// because it is the one remaining way this path can still cost the player content.</para>
    ///
    /// <para><b>MASKS ARE HONOURED, and that is not an optimisation.</b> A ScrollRect's content is
    /// routinely many times taller than its viewport; without clipping each graphic against its
    /// nearest enabled <see cref="RectMask2D"/> / <see cref="Mask"/> ancestor, one scroll list would
    /// expand the frame (and the render target) by an order of magnitude for content that is not
    /// drawn. The clip is carried down the walk and intersected, in host-local space, using
    /// axis-aligned bounds throughout — a rotated child inside a mask therefore measures as its
    /// bounding box, which errs towards over-measuring, which is the safe direction.</para>
    ///
    /// <para>Never throws. If anything is missing the frame falls back to the host rect, i.e.
    /// exactly ModBuild 192's behaviour.</para>
    /// </summary>
    private static void MeasureFrame(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform? host = panel.HostRect;
        if (host == null || panel.HostGo == null)
            return;

        float started = Time.realtimeSinceStartup;
        Rect hostRect = host.rect;
        Rect union = hostRect;

        ContentStack.Clear();
        ContentStack.Add(new ClipFrame(panel.HostGo.transform, Unbounded));
        while (ContentStack.Count > 0)
        {
            int last = ContentStack.Count - 1;
            ClipFrame node = ContentStack[last];
            ContentStack.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeSelf)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;

            bool isRoot = ReferenceEquals(t, panel.HostGo.transform);
            // FOREIGN RENDER SUBTREES ARE EXCLUDED FROM THE FRAME, and that does NOT lose them.
            // The same rule the layer sweep uses, for the same reason: a real Renderer or a Camera
            // in here belongs to somebody else (the live 3D character rig and its preview camera;
            // the shop window pools up to 44 of them as items arrive), it is NOT moved onto the
            // capture layer, and it is therefore NOT in the captured image at all. Framing it would
            // allocate render-target area for pixels that are guaranteed empty AND pull the frame's
            // centre off the content that IS captured. What happens to it instead is exactly what
            // happened before this path existed and before ModBuild 193 changed anything: it stays
            // on the game's own layer and the HEAD camera draws it directly, at its true world pose.
            // So it remains visible — unfiltered, and depth-sorted against the quad by its own world
            // z, which is ModBuild 192's behaviour unchanged. Nothing about the capture frame can
            // take it away; the frame only ever decides what the CAPTURE covers.
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            Rect clip = node.Clip;
            var rt = t as RectTransform;
            if (rt != null && !isRoot)
            {
                if (TryHostLocalBounds(host, rt, out Rect bounds))
                {
                    if (ClipsChildren(t))
                    {
                        if (!Intersect(clip, bounds, out clip))
                            continue; // fully clipped away: neither this nor anything under it draws
                    }
                    var graphic = t.GetComponent<Graphic>();
                    if (Draws(graphic) && Intersect(clip, bounds, out Rect visible))
                        union = Union(union, visible);
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                ContentStack.Add(new ClipFrame(t.GetChild(i), clip));
        }
        ContentStack.Clear();

        // THE CLAMP — the one place content can still be lost, so it keeps the host rect centred and
        // gives away as much of the overspill as the budget allows, edge by edge.
        float padX = hostRect.width * (MaxContentExpansion - 1f) * 0.5f;
        float padY = hostRect.height * (MaxContentExpansion - 1f) * 0.5f;
        float xMin = Mathf.Max(union.xMin, hostRect.xMin - padX);
        float xMax = Mathf.Min(union.xMax, hostRect.xMax + padX);
        float yMin = Mathf.Max(union.yMin, hostRect.yMin - padY);
        float yMax = Mathf.Min(union.yMax, hostRect.yMax + padY);
        bool clamped = xMin > union.xMin + 0.5f || xMax < union.xMax - 0.5f
                       || yMin > union.yMin + 0.5f || yMax < union.yMax - 0.5f;
        Rect frame = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        if (frame.width < 1f || frame.height < 1f)
            frame = hostRect;

        e.Frame = frame;
        e.HostRectAtMeasure = hostRect;
        e.ExpandX = Mathf.Max(0f, frame.width - hostRect.width);
        e.ExpandY = Mathf.Max(0f, frame.height - hostRect.height);
        e.ExpandClamped = clamped;
        e.ContentMeasures++;
        e.LastMeasureFrame = Time.frameCount;
        e.ContentMs += (Time.realtimeSinceStartup - started) * 1000.0;

        if (clamped && !e.ExpandClampWarned)
        {
            e.ExpandClampWarned = true;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' draws content that reaches "
                              + $"{union.width:F0}x{union.height:F0} uGUI px around a "
                              + $"{hostRect.width:F0}x{hostRect.height:F0} host rect — more than the "
                              + $"{MaxContentExpansion:F0}x expansion this path will frame. THE "
                              + $"CONSEQUENCE: the capture frames {frame.width:F0}x{frame.height:F0} "
                              + "and whatever lies outside THAT is cropped from the supersampled "
                              + "image; it was visible before this build and is not now. The clamp "
                              + "exists because the render target scales with the frame, so an "
                              + "unbounded frame is an unbounded allocation. If this window's content "
                              + "genuinely lives that far outside its own frame, raise "
                              + "MaxContentExpansion (and expect the VRAM cost to follow), or switch "
                              + "[WorldUI] PanelSupersample off for this session.");
        }
    }

    /// <summary>
    /// Does this transform CLIP its children? Both uGUI clipping mechanisms count:
    /// <see cref="RectMask2D"/> (a rectangle in the shader) and <see cref="Mask"/> (a stencil
    /// effect, which needs a <see cref="Graphic"/> to write the stencil and is inert without one).
    /// Only ENABLED ones — a disabled mask clips nothing, and the conversion enables/adds masks of
    /// its own (<c>CanvasConversion.EnsureScrollClipping</c>), so the live component state is the
    /// only trustworthy answer here.
    /// </summary>
    private static bool ClipsChildren(Transform t)
    {
        var rect2d = t.GetComponent<RectMask2D>();
        if (rect2d != null && rect2d.enabled && rect2d.gameObject.activeInHierarchy)
            return true;
        var mask = t.GetComponent<Mask>();
        if (mask != null && mask.enabled && mask.gameObject.activeInHierarchy)
        {
            var graphic = t.GetComponent<Graphic>();
            if (graphic != null && graphic.enabled)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Does this graphic put pixels on the screen? Permissive by design (see
    /// <see cref="MeasureFrame"/>): enabled, active, not culled by uGUI's own rect culling, and not
    /// authored fully transparent. A CanvasGroup fade is deliberately NOT consulted — a window
    /// measured mid-fade would otherwise report a frame that is too small and crop itself for a
    /// cadence once the fade completed.
    /// </summary>
    private static bool Draws(Graphic? g)
    {
        if (g == null || !g.enabled || !g.gameObject.activeInHierarchy)
            return false;
        if (g.color.a <= 0.004f)
            return false;
        CanvasRenderer cr = g.canvasRenderer;
        return cr != null && !cr.cull;
    }

    /// <summary>Axis-aligned bounds of <paramref name="rt"/> in <paramref name="host"/>'s local
    /// space, in uGUI pixels. World corners are used rather than the raw rect so a child under any
    /// chain of scales/rotations is measured where it actually lands.</summary>
    private static bool TryHostLocalBounds(RectTransform host, RectTransform rt, out Rect bounds)
    {
        bounds = default;
        Rect local = rt.rect;
        if (local.width <= 0f && local.height <= 0f)
            return false;
        rt.GetWorldCorners(ContentCorners);
        Vector3 first = host.InverseTransformPoint(ContentCorners[0]);
        float minX = first.x, maxX = first.x, minY = first.y, maxY = first.y;
        for (int i = 1; i < 4; i++)
        {
            Vector3 p = host.InverseTransformPoint(ContentCorners[i]);
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
        bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
        return true;
    }

    private static bool Intersect(Rect a, Rect b, out Rect result)
    {
        float xMin = Mathf.Max(a.xMin, b.xMin);
        float xMax = Mathf.Min(a.xMax, b.xMax);
        float yMin = Mathf.Max(a.yMin, b.yMin);
        float yMax = Mathf.Min(a.yMax, b.yMax);
        result = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    private static Rect Union(Rect a, Rect b) => Rect.MinMaxRect(
        Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
        Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    // ---- CONTENT INTEGRITY: what the window's TEXT is actually able to draw ----------------------

    /// <summary>Work stack for <see cref="MeasureContent"/>. Deliberately NOT
    /// <see cref="ContentStack"/> or <see cref="Scratch"/>: the frame measurement, the layer sweep and
    /// this scan all run inside the same LateUpdate and can call each other (the release repair runs
    /// all three), and sharing one buffer between re-entrant walks is the kind of coupling that
    /// produces a wrong number once and then never again reproducibly.</summary>
    private static readonly List<Transform> TextWalk = new(256);

    /// <summary>Scratch for the atlas census sentence.</summary>
    private static readonly StringBuilder AtlasSb = new(256);

    /// <summary>Cap on regenerations one scan may force, so a repair can never become the spike.</summary>
    private const int MaxRegeneratePerScan = 256;

    /// <summary>
    /// <b>THE INSTRUMENT FOR "DIE DARGESTELLTE ANZEIGE IST KAPUTT", AND THE REPAIR IN ONE WALK.</b>
    ///
    /// <para><b>WHAT THE PHOTOGRAPH ACTUALLY SHOWS</b> (.planning/debug/kaputte_anzeige.jpg, measured
    /// off the pixels rather than described): the mercenary window's six stat labels render as
    /// <i>"Ge n i"</i>, <i>"Go"</i>, <i>"F rt g te"</i>, <i>"Gebun e g st e"</i>, <i>"ers k :"</i>,
    /// <i>"erb s e :"</i> — individual GLYPHS absent from strings whose LAYOUT is intact. Three
    /// measurements pin that down and each one kills a candidate cause:
    /// <list type="number">
    /// <item>THE ADVANCES ARE FULL WIDTH. The trailing colon of <i>Verstärkungen:</i> (14 characters)
    /// and of <i>Verbesserungen:</i> (15) sit 12 px apart — one character's advance — so NOTHING was
    /// substituted, shortened or removed. A text engine that replaced a missing glyph would have
    /// shifted everything after it. <b>The characters are all still in the layout; their quads put no
    /// pixels down.</b></item>
    /// <item>THE GAPS ARE EMPTY, NOT DIM. Peak luminance inside the gap where <i>d h e</i> of
    /// "Gesundheit" belongs is 24, against a 20 background and a 147 ink. This is not a
    /// contrast/alpha artifact with a faint residue; the pixels were never written.</item>
    /// <item>IT IS NOT UNDERSAMPLING, WHICH IS THIS PATH'S OWN PRIOR DIAGNOSIS. The same window's
    /// SMALLER text — <i>"Schließe sechs Basisspiel-Nebenszenarien ab."</i> — renders every character,
    /// and the surviving glyphs are at full brightness and crisp. Minification below Nyquist dims and
    /// blurs uniformly; it does not delete some glyphs of one label and leave a smaller label
    /// perfect. The ink runs also do NOT line up into vertical stripes across the six rows, which is
    /// what a sampling-phase artifact would look like.</item>
    /// </list></para>
    ///
    /// <para><b>SO THIS SCAN SEPARATES EXACTLY THE THREE STATES THE ROUND WAS ASKED FOR</b>, per text
    /// component, with the comparison count on the same line:
    /// <list type="bullet">
    /// <item><see cref="Entry.GlyphsNotInAtlas"/> — the font asset does NOT have the character, its
    /// fallbacks included. Non-zero proves the atlas cannot serve the string, which is the dynamic
    /// font atlas hypothesis, and it also proves that re-taking the capture is useless.</item>
    /// <item><see cref="Entry.GlyphsNotVisible"/> — the text engine parsed the character and marked it
    /// NOT VISIBLE (overflow truncation, missing-glyph replacement, a maxVisibleCharacters clamp).
    /// The content is genuinely absent and the capture is faithful.</item>
    /// <item><see cref="Entry.GlyphsBlankQuad"/> — the character is visible and its generated quad has
    /// zero area: it holds its advance and draws nothing. <b>That is the exact shape of the
    /// photograph</b>, so a non-zero value here IS the finding and a permanent zero retires the whole
    /// family.</item>
    /// </list>
    /// A fourth state — the capture ran mid-repack, or ahead of the canvas rebuild — is counted at the
    /// capture instant instead (<see cref="Entry.CapturesDuringFontRebuild"/>,
    /// <see cref="Entry.CapturesBeforeCanvasUpdate"/>), because that is the only place the answer
    /// exists.</para>
    ///
    /// <para><b>AND IT REPAIRS AS IT GOES.</b> A component that fails any of the three tests (or every
    /// component, when <paramref name="repairAll"/> — a font atlas repack invalidates meshes that
    /// still pass every test) re-requests its characters into the atlas and re-generates its mesh on
    /// the spot. Forcing the regeneration is preferred over re-taking the capture blindly, because a
    /// stale mesh re-captured is still a stale mesh. The counts reported are the PRE-repair state, so
    /// the log says what was wrong and not merely that something ran.</para>
    ///
    /// <para>Never throws. Bounded by <see cref="MaxGlyphChecksPerScan"/> and
    /// <see cref="MaxRegeneratePerScan"/>, and a scan that hit either bound says so
    /// (<see cref="Entry.ContentScanTruncated"/>) rather than letting a truncated scan read clean.</para>
    /// </summary>
    private static void MeasureContent(Entry e, bool repairAll = false)
    {
        // FAIL SOFT, AND SAY SO. This scan calls into TextMeshPro and into uGUI's text generator on
        // objects the mod does not own. An exception here must cost this scan and nothing else — it
        // must NOT reach LateTick's catch, which stands the entire supersample path down and sends
        // every floated window back to the shimmering direct rendering. A scan that threw is counted
        // separately so it can never be read as a scan that came back clean.
        try
        {
            MeasureContentCore(e, repairAll);
        }
        catch (System.Exception ex)
        {
            e.ContentScanFailures++;
            TextWalk.Clear();
            AtlasSb.Length = 0;
            if (!e.ContentScanFailWarned)
            {
                e.ContentScanFailWarned = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the content-integrity scan on '{e.Window}' "
                                  + $"threw ({ex.GetType().Name}: {ex.Message}). THE CONSEQUENCE: this "
                                  + "window's CONTENT INTEGRITY field is stale from here on and its "
                                  + "release repair does not regenerate text — the capture, the mip "
                                  + "chain, the layer isolation and the still-window sharpness are "
                                  + "all unaffected, and the failure count on the state line keeps a "
                                  + "failed scan from reading as a clean one.");
            }
        }
    }

    private static void MeasureContentCore(Entry e, bool repairAll)
    {
        ConvertedPanel panel = e.Panel;
        if (panel.HostGo == null)
            return;
        float started = Time.realtimeSinceStartup;

        e.ContentScans++;
        e.TextComponents = 0;
        e.GlyphsChecked = 0;
        e.GlyphsNotInAtlas = 0;
        e.GlyphsNotVisible = 0;
        e.GlyphsBlankQuad = 0;
        e.WorstText = string.Empty;
        e.WorstTextBad = 0;
        e.WorstTextChecked = 0;
        e.TextCulled = 0;
        e.TextClean = 0;
        e.MeshQuadsChecked = 0;
        e.MeshMissingQuads = 0;
        e.MeshDegenerateQuads = 0;
        e.MeshDegenerateUv = 0;
        e.MeshUvOutOfRange = 0;
        e.MeshNonFinite = 0;
        e.MeshWorst = string.Empty;
        e.MeshWorstBad = 0;
        e.SubMeshesSeen = 0;
        e.SubMeshesInactive = 0;
        e.SubMeshesCulled = 0;
        e.SubMeshesWrongLayer = 0;
        e.SubMeshesNoTexture = 0;
        e.SubMeshWorst = string.Empty;
        e.ContentScanTruncated = false;
        e.RegeneratedComponents = 0;
        e.RegeneratedChars = 0;
        AtlasSb.Length = 0;
        int atlasesNamed = 0;
        bool anyRepaired = false;

        TextWalk.Clear();
        TextWalk.Add(panel.HostGo.transform);
        while (TextWalk.Count > 0)
        {
            int last = TextWalk.Count - 1;
            Transform t = TextWalk[last];
            TextWalk.RemoveAt(last);
            if (t == null || !t.gameObject.activeInHierarchy)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;
            bool isRoot = ReferenceEquals(t, panel.HostGo.transform);
            // The same foreign-subtree rule the frame measurement and the layer sweep use, for the
            // same reason: a real Renderer or a Camera in here belongs to somebody else.
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
                continue;

            // ONE GetComponent, not two: a transform carries at most one Graphic, and both text
            // families derive from it. On the party window this walk visits ~2700 transforms, so the
            // difference is a whole millisecond of a release frame.
            var graphic = t.GetComponent<Graphic>();
            if (graphic is TMP_Text tmp)
            {
                if (ScanTmpText(e, tmp, repairAll, ref atlasesNamed))
                    anyRepaired = true;
            }
            else if (graphic is Text legacy && ScanLegacyText(e, legacy, repairAll))
            {
                anyRepaired = true;
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                TextWalk.Add(t.GetChild(i));
        }
        TextWalk.Clear();

        if (atlasesNamed == 0)
            AtlasSb.Append("no font asset reached");
        e.AtlasNote = AtlasSb.ToString();
        AtlasSb.Length = 0;

        // ONE canvas update for the whole batch, and only if anything was actually re-generated: a
        // ForceUpdateCanvases per component would be N layout passes for one release.
        if (anyRepaired)
            Canvas.ForceUpdateCanvases();

        e.ContentScanMs += (Time.realtimeSinceStartup - started) * 1000.0;
    }

    /// <summary>Scan (and if needed repair) one TextMeshPro component. Returns true if it regenerated.
    /// <para>The ATLAS test reads the component's requested string, because that is what it ASKS for
    /// and it is answerable whether or not a mesh was ever generated; the VISIBILITY and QUAD tests
    /// read <c>textInfo</c>, because that is what it PRODUCED. The two together are what separates
    /// "the font cannot serve this string" from "the font can and the mesh still draws nothing".</para></summary>
    private static bool ScanTmpText(Entry e, TMP_Text t, bool repairAll, ref int atlasesNamed)
    {
        if (!t.isActiveAndEnabled)
            return false;
        string s = t.text;
        if (string.IsNullOrEmpty(s))
            return false;
        e.TextComponents++;

        int bad = 0;
        int checkedHere = 0;
        TMP_FontAsset? font = t.font;
        if (font != null)
        {
            NoteTmpAtlas(font, ref atlasesNamed);
            bool inTag = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                // Crude but sufficient rich-text skip: a '<...>' run is markup, not glyphs, and
                // counting it would inflate the comparison count with characters nothing draws.
                if (c == '<') { inTag = true; continue; }
                if (inTag) { if (c == '>') inTag = false; continue; }
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    continue;
                if (e.GlyphsChecked >= MaxGlyphChecksPerScan)
                {
                    e.ContentScanTruncated = true;
                    break;
                }
                e.GlyphsChecked++;
                checkedHere++;
                if (!font.HasCharacter(c, true, false))
                {
                    e.GlyphsNotInAtlas++;
                    bad++;
                }
            }
        }

        TMP_TextInfo info = t.textInfo;
        if (info != null && info.characterInfo != null)
        {
            int n = Mathf.Min(info.characterCount, info.characterInfo.Length);
            for (int i = 0; i < n; i++)
            {
                TMP_CharacterInfo ci = info.characterInfo[i];
                char c = ci.character;
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                    continue;
                if (!ci.isVisible)
                {
                    e.GlyphsNotVisible++;
                    bad++;
                    continue;
                }
                Vector3 bl = ci.vertex_BL.position;
                Vector3 tr = ci.vertex_TR.position;
                float area = Mathf.Abs((tr.x - bl.x) * (tr.y - bl.y));
                if (area <= DegenerateQuadArea)
                {
                    e.GlyphsBlankQuad++;
                    bad++;
                }
            }
        }

        // THE SUBMITTED MESH — the measurement ModBuild 196 did not make, and the reason 236 clean
        // readings did not settle anything. Everything above reads the LAYOUT; this reads what was
        // actually written into the vertex and UV arrays that get uploaded.
        int meshBad = ScanTmpMesh(e, t, info);

        NoteRendererState(e, t.canvasRenderer);
        // AND THE SUB-MESHES THIS COMPONENT HANDS ITS OTHER MATERIALS TO — see Entry.SubMeshesSeen.
        // Done from here rather than from the outer walk on purpose: that walk skips anything
        // !activeInHierarchy, which is precisely one of the states this needs to be able to report.
        ScanTmpSubMeshes(e, t);

        if (bad > e.WorstTextBad)
        {
            e.WorstTextBad = bad;
            e.WorstTextChecked = checkedHere;
            e.WorstText = Describe(t.gameObject.name, s);
        }
        if (meshBad > e.MeshWorstBad)
        {
            e.MeshWorstBad = meshBad;
            e.MeshWorst = Describe(t.gameObject.name, s);
        }
        if (bad == 0 && meshBad == 0)
            e.TextClean++;
        bad += meshBad;

        if ((!repairAll && bad == 0) || e.RegeneratedComponents >= MaxRegeneratePerScan)
        {
            if (e.RegeneratedComponents >= MaxRegeneratePerScan)
                e.ContentScanTruncated = true;
            return false;
        }

        // THE REPAIR, in the order that makes it a repair rather than a retry: put the characters
        // back in the atlas FIRST (a mesh regenerated against an atlas that still lacks them would
        // come out exactly as broken), then re-parse and re-generate the mesh.
        if (font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic)
            font.TryAddCharacters(s, out string _);
        t.ForceMeshUpdate(true, true);
        e.RegeneratedComponents++;
        e.RegeneratedChars += s.Length;
        return true;
    }

    /// <summary>
    /// <b>THE SUBMITTED MESH — THE ONE PLACE THE PHOTOGRAPH'S FAULT CAN LIVE AND ModBuild 196 COULD
    /// NOT LOOK.</b>
    ///
    /// <para><b>WHY THIS EXISTS AND WHY THE PREVIOUS INSTRUMENT WAS NOT WRONG, ONLY SHORT.</b>
    /// ModBuild 196 shipped a three-way scan and the full ModBuild 196 hardware log was then counted
    /// rather than sampled: <b>236 readings, of which 152 read <c>0 / 0 / 0</c> and 84 read exactly
    /// one character parsed-but-not-visible. Zero characters missing from the atlas, zero zero-area
    /// quads, zero atlas repacks, in a session with real drags and up to 140 release repairs on one
    /// window.</b> The photograph shows dozens of missing glyphs across six rows. A working
    /// instrument that never sees the defect is measuring the wrong quantity, and this is the
    /// quantity it was measuring: every counter in <see cref="ScanTmpText"/> reads
    /// <see cref="TMP_CharacterInfo"/>, which is the LAYOUT RECORD the text engine writes while it
    /// lays the string out. Its <c>isVisible</c> flag and its <c>vertex_BL/TR</c> positions are
    /// decided BEFORE anything is written into a mesh.</para>
    ///
    /// <para><b>WHAT IS DOWNSTREAM OF IT, AND MATCHES THE PHOTOGRAPH EXACTLY.</b> The glyph the eye
    /// sees is four vertices and four UVs in <c>textInfo.meshInfo[m]</c>, uploaded to the
    /// <see cref="CanvasRenderer"/>. Three things can go wrong there while every layout counter stays
    /// clean, and all three produce full advances with no ink — which is precisely what was measured
    /// off the image (the trailing colons of <i>Verstärkungen:</i> and <i>Verbesserungen:</i> exactly
    /// one advance apart, gaps at background luminance, no vertical alignment across rows):
    /// <list type="number">
    /// <item>THE QUAD WAS NEVER WRITTEN. The character's <c>vertexIndex</c> points past the end of
    /// the mesh that was actually generated — the layout ran further than the mesh did.</item>
    /// <item>THE ATLAS UV RECTANGLE COLLAPSED. Four identical UVs sample ONE atlas texel, so an SDF
    /// shader draws a flat distance value over the whole quad: full geometry, full advance, no ink.
    /// <b>Nothing in ModBuild 196 read a single UV.</b></item>
    /// <item>THE UVs POINT OUTSIDE THE ATLAS, i.e. they are stale against a texture that moved.</item>
    /// </list>
    /// A non-finite vertex is counted as a fourth: the GPU discards such a triangle silently.</para>
    ///
    /// <para><b>HOW TO READ IT.</b> <see cref="Entry.MeshQuadsChecked"/> is the denominator and is
    /// printed on the same line as every count, so "the mesh instrument never ran" can never again
    /// look like "the mesh is clean" — the mistake this whole round exists to stop repeating. If
    /// these counters stay at zero through a session in which the user sees the defect, then the
    /// fault is not in the text at ANY level, source or mesh, and the next round must stop looking at
    /// text: what remains is the capture path (measured by the CAPTURE PATH field) and frame pacing
    /// (measured by MOTION BUDGET and by the RELEASE line's dropped-frame count).</para>
    ///
    /// <para>Cost: four vector reads per visible character, inside a walk that already visits every
    /// character. Bounded by the same <see cref="MaxGlyphChecksPerScan"/> budget as the rest.</para>
    /// </summary>
    private static int ScanTmpMesh(Entry e, TMP_Text t, TMP_TextInfo? info)
    {
        if (info == null || info.characterInfo == null || info.meshInfo == null)
            return 0;
        int bad = 0;
        int n = Mathf.Min(info.characterCount, info.characterInfo.Length);
        for (int i = 0; i < n; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || char.IsWhiteSpace(ci.character) || char.IsControl(ci.character))
                continue;
            int m = ci.materialReferenceIndex;
            if (m < 0 || m >= info.meshInfo.Length)
            {
                e.MeshQuadsChecked++;
                e.MeshMissingQuads++;
                bad++;
                continue;
            }
            TMP_MeshInfo mi = info.meshInfo[m];
            Vector3[] verts = mi.vertices;
            Vector2[] uvs = mi.uvs0;
            int v = ci.vertexIndex;
            e.MeshQuadsChecked++;
            // "Written into the mesh" is decided by vertexCount, NOT by the array length: TMP keeps
            // its vertex arrays allocated at the high-water mark of every string this component has
            // ever held, so an array long enough to index proves nothing about this generation.
            if (verts == null || uvs == null || v < 0 || v + 3 >= mi.vertexCount
                || v + 3 >= verts.Length || v + 3 >= uvs.Length)
            {
                e.MeshMissingQuads++;
                bad++;
                continue;
            }

            Vector3 p0 = verts[v], p1 = verts[v + 1], p2 = verts[v + 2], p3 = verts[v + 3];
            Vector2 u0 = uvs[v], u1 = uvs[v + 1], u2 = uvs[v + 2], u3 = uvs[v + 3];
            if (!Finite(p0) || !Finite(p1) || !Finite(p2) || !Finite(p3)
                || !Finite(u0) || !Finite(u1) || !Finite(u2) || !Finite(u3))
            {
                e.MeshNonFinite++;
                bad++;
                continue;
            }

            float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
            float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
            float minY = Mathf.Min(Mathf.Min(p0.y, p1.y), Mathf.Min(p2.y, p3.y));
            float maxY = Mathf.Max(Mathf.Max(p0.y, p1.y), Mathf.Max(p2.y, p3.y));
            if ((maxX - minX) * (maxY - minY) <= DegenerateQuadArea)
            {
                e.MeshDegenerateQuads++;
                bad++;
                continue;
            }

            float uMinX = Mathf.Min(Mathf.Min(u0.x, u1.x), Mathf.Min(u2.x, u3.x));
            float uMaxX = Mathf.Max(Mathf.Max(u0.x, u1.x), Mathf.Max(u2.x, u3.x));
            float uMinY = Mathf.Min(Mathf.Min(u0.y, u1.y), Mathf.Min(u2.y, u3.y));
            float uMaxY = Mathf.Max(Mathf.Max(u0.y, u1.y), Mathf.Max(u2.y, u3.y));
            if ((uMaxX - uMinX) * (uMaxY - uMinY) <= DegenerateUvArea)
            {
                e.MeshDegenerateUv++;
                bad++;
                continue;
            }
            // Half a texel of slack on each side: TMP writes glyph UVs with a padding inset and a
            // legitimate edge glyph can sit fractionally outside [0,1] without sampling anything it
            // should not, since both targets clamp.
            const float slack = 0.001f;
            if (uMinX < -slack || uMinY < -slack || uMaxX > 1f + slack || uMaxY > 1f + slack)
            {
                e.MeshUvOutOfRange++;
                bad++;
            }
        }
        return bad;
    }

    /// <summary>
    /// <b>THE SUB-MESH SCAN — the object four builds of "0 defects out of 3,471 quads" never looked
    /// at.</b> The full argument is on <see cref="Entry.SubMeshesSeen"/>; the short version is that a
    /// <c>TextMeshProUGUI</c> draws only the glyphs served by its FIRST material and hands every
    /// other one — second atlas page, fallback font, inline sprite — to a <see cref="TMP_SubMeshUI"/>
    /// on a CHILD GameObject with its own CanvasRenderer, material, layer and active state.
    /// <c>textInfo.meshInfo[1..]</c> carries their vertex data, which <see cref="ScanTmpMesh"/> reads
    /// and finds clean, and <see cref="NoteRendererState"/> then asks the PARENT whether it drew.
    ///
    /// <para><b>THE ONE THING HERE THAT IS A FIX AND NOT A MEASUREMENT.</b> A sub-mesh born between
    /// two capture-layer sweeps sits on the game's UI layer, which this panel's capture camera does
    /// not cull in — so those glyphs are missing from the TEXTURE while every quad, UV and glyph
    /// record describing them is perfect. That is repaired on sight, unconditionally, and NOT gated
    /// on the defect count: a remedy that only runs when its own diagnostic already fired is a
    /// remedy that never runs, which is the ModBuild 196 mistake this lane has already paid for. The
    /// per-frame layer sweep still owns the general case; this closes the window between its
    /// cadences for the one family of objects that is born mid-string.</para>
    ///
    /// <para>Cost: TMP parents its sub-meshes as DIRECT children of the text GameObject, so this is
    /// one <c>childCount</c> loop and one <c>GetComponent</c> per child of a text component — no
    /// recursive search and no allocation. Windows whose text needs a single material report
    /// <c>0 sub-mesh(es)</c> and pay a single integer compare.</para>
    /// </summary>
    private static void ScanTmpSubMeshes(Entry e, TMP_Text t)
    {
        Transform parent = t.transform;
        int layer = e.Layer;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.GetChild(i);
            if (c == null)
                continue;
            var sub = c.GetComponent<TMP_SubMeshUI>();
            if (sub == null)
                continue;
            e.SubMeshesSeen++;

            int bad = 0;
            if (!c.gameObject.activeInHierarchy)
            {
                e.SubMeshesInactive++;
                bad++;
            }

            CanvasRenderer cr = sub.canvasRenderer;
            if (cr != null && (cr.cull || cr.GetAlpha() <= 0.004f || cr.GetInheritedAlpha() <= 0.004f))
            {
                e.SubMeshesCulled++;
                bad++;
            }

            Material mat = sub.materialForRendering;
            if (mat == null || (mat.HasProperty(MainTexId) && mat.GetTexture(MainTexId) == null))
            {
                e.SubMeshesNoTexture++;
                bad++;
            }

            // THE REPAIR. Only when this panel actually holds a private layer; a refused panel has
            // Layer < 0 and its subtree must stay exactly where the game put it.
            if (layer >= 0 && c.gameObject.layer != layer)
            {
                e.SubMeshesWrongLayer++;
                bad++;
                if (!IsRecorded(e, c))
                    e.Relayered.Add(new LayerRecord { Transform = c, OriginalLayer = c.gameObject.layer });
                c.gameObject.layer = layer;
            }

            if (bad > 0 && e.SubMeshWorst.Length == 0)
                e.SubMeshWorst = Describe(c.gameObject.name, t.text);
        }
    }

    /// <summary>Cached <c>_MainTex</c> id for <see cref="ScanTmpSubMeshes"/> — a string lookup per
    /// sub-mesh per scan would be the most expensive line in the walk.</summary>
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    private static bool Finite(Vector3 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
          || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

    private static bool Finite(Vector2 v) =>
        !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.x) || float.IsInfinity(v.y));

    /// <summary>Scan (and if needed repair) one legacy uGUI <see cref="Text"/>. The game's own UI is
    /// TextMeshPro throughout (the hierarchy census in the hardware log reads
    /// <c>[RectTransform, CanvasRenderer, TextMeshProUGUI, TextLocalizedListener]</c> on every label),
    /// so this path exists for the mod's own labels and for completeness; it tests glyph availability
    /// only, because legacy <c>TextGenerator</c> emits four vertices for whitespace as well and a
    /// zero-area quad there is normal rather than a defect.</summary>
    private static bool ScanLegacyText(Entry e, Text t, bool repairAll)
    {
        if (!t.isActiveAndEnabled)
            return false;
        Font? font = t.font;
        string s = t.text;
        if (font == null || string.IsNullOrEmpty(s))
            return false;
        e.TextComponents++;

        int bad = 0;
        int checkedHere = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c) || char.IsControl(c))
                continue;
            if (e.GlyphsChecked >= MaxGlyphChecksPerScan)
            {
                e.ContentScanTruncated = true;
                break;
            }
            e.GlyphsChecked++;
            checkedHere++;
            if (!font.GetCharacterInfo(c, out CharacterInfo _, t.fontSize, t.fontStyle))
            {
                e.GlyphsNotInAtlas++;
                bad++;
            }
        }

        NoteRendererState(e, t.canvasRenderer);

        if (bad > e.WorstTextBad)
        {
            e.WorstTextBad = bad;
            e.WorstTextChecked = checkedHere;
            e.WorstText = Describe(t.gameObject.name, s);
        }
        if (bad == 0)
            e.TextClean++;

        if ((!repairAll && bad == 0) || e.RegeneratedComponents >= MaxRegeneratePerScan)
            return false;
        if (font.dynamic)
            font.RequestCharactersInTexture(s, t.fontSize, t.fontStyle);
        t.SetAllDirty();
        e.RegeneratedComponents++;
        e.RegeneratedChars += s.Length;
        return true;
    }

    /// <summary>
    /// Fingerprint a TextMeshPro font asset's atlas and detect a REPACK.
    /// <para>TextMeshPro does NOT raise <see cref="Font.textureRebuilt"/> — that event belongs to the
    /// legacy dynamic <see cref="Font"/> — so the hook in <see cref="InstallHooks"/> alone would be
    /// blind to exactly the font family this game uses. Watching the atlas texture COUNT and the atlas
    /// texture's instance id is the cheapest observation that changes when TMP grows or replaces an
    /// atlas, and it is recorded into the same counters as the legacy event so one number answers
    /// "did an atlas move under us".</para>
    /// </summary>
    private static void NoteTmpAtlas(TMP_FontAsset font, ref int atlasesNamed)
    {
        int id = font.GetInstanceID();
        Texture atlas = font.atlasTexture;
        int textureId = atlas != null ? atlas.GetInstanceID() : 0;
        int count = font.atlasTextureCount;
        if (TmpAtlasSeen.TryGetValue(id, out (int Count, int TextureId) seen))
        {
            if (seen.Count != count || seen.TextureId != textureId)
            {
                _fontRebuilds++;
                _fontRebuildFrame = Time.frameCount;
                _fontRebuildName = font.name;
                ArmRebuildRepair();
            }
        }
        TmpAtlasSeen[id] = (count, textureId);

        if (atlasesNamed >= 3)
            return;
        if (atlasesNamed > 0)
            AtlasSb.Append(", ");
        atlasesNamed++;
        AtlasSb.Append('\'').Append(font.name).Append("' ").Append(font.atlasPopulationMode)
               .Append(' ').Append(font.atlasWidth).Append('x').Append(font.atlasHeight)
               .Append(" x").Append(count).Append(" texture(s)");
    }

    /// <summary>
    /// Count a text component that puts NO pixels into the capture for a reason that is not about
    /// glyphs: uGUI/TMP has culled its <see cref="CanvasRenderer"/>, or its effective alpha is zero.
    /// <para>Kept separate from every glyph counter on purpose — see <see cref="Entry.TextCulled"/>
    /// for why "missing from the capture" and "captured and painted over" must not be allowed to
    /// collapse into one number, and for the plain statement that NOTHING in this scan can see the
    /// second of those.</para>
    /// </summary>
    private static void NoteRendererState(Entry e, CanvasRenderer? cr)
    {
        if (cr == null)
            return;
        if (cr.cull || cr.GetAlpha() <= 0.004f || cr.GetInheritedAlpha() <= 0.004f)
            e.TextCulled++;
    }

    /// <summary>A component name plus the first few characters of its string, for the report. Bounded
    /// so one pathological label cannot make the state line unreadable.</summary>
    private static string Describe(string name, string text)
    {
        string trimmed = text.Length <= 28 ? text : text.Substring(0, 28) + "...";
        return name + " (\"" + trimmed.Replace('\n', ' ') + "\")";
    }
}
