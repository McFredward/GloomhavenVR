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
    ///
    /// <para><b>ModBuild 201 ALSO MEASURES THE CONTENT'S OWN SCALE HERE</b>, in the same walk, because
    /// this is the only place that already knows both a child's authored rect and where that rect
    /// LANDS in host space. See <see cref="Entry.MinContentScale"/> for what that number is for and
    /// what the ModBuild 200 hardware log said about it. It also QUANTISES the frame it produces —
    /// see <see cref="FrameQuantumPx"/>, which is the flicker half of that round.</para>
    ///
    /// <para><b>MODBUILD 204 — THE 28/28 FLAP, AND WHY THE QUANTUM ALONE WAS NOT ENOUGH.</b>
    ///
    /// <para>THE USER'S REPORT, VERBATIM: the converted character window renders correctly when
    /// still; <i>"das flackern tritt auf sobald man das Fenster in die Hand nimmt und bewegt, und
    /// beim Loslassen wurde random ein Stand eingefroren der wieder manche elemente nicht sichtbar
    /// macht"</i>. ModBuild 192 fixed the STILL case and twelve builds did not fix the MOVING one.</para>
    ///
    /// <para><b>THE ModBuild 203 HARDWARE LOG SETTLES THE ALLOCATION HALF OF IT.</b> In 62 seconds
    /// the party window re-allocated its render target <b>58 times</b>, and the two frames it
    /// alternated between are:
    /// <list type="bullet">
    /// <item><b>28 x 2020x1464</b> uGUI px — host rect 1988x1080 + content overspill 32x384;</item>
    /// <item><b>28 x 2020x1496</b> uGUI px — host rect 1988x1080 + content overspill 32x<b>416</b>.</item>
    /// </list>
    /// A 28/28 flap between two frames that differ by <b>exactly one 32 px
    /// <see cref="FrameQuantumPx"/> in Y</b>. Rate: <b>2.51 re-allocations per second while the hand
    /// holds the window and 0.29/s while it does not</b> — 45 of the 58 fall inside a grab and 12 of
    /// them inside a single 2.8 s drag. Each one destroys and re-creates a ~150 MB, 12-mip render
    /// target, and each one re-rolls the sub-texel phase of every glyph in the window.</para>
    ///
    /// <para><b>WHY MOVING MAKES IT WORSE, READ FROM THE CODE AND NOT INFERRED.</b> The re-measure
    /// runs on <see cref="ContentMeasureIntervalFrames"/> (15) when nothing is changing, and
    /// <c>SyncGeometry</c> forces it on any geometry change subject only to
    /// <see cref="ContentMeasureMinIntervalFrames"/> (2). A union sitting ON a quantum boundary is
    /// therefore SAMPLED several times as often while the window is in the hand, and flips several
    /// times as often. The remedy is not a slower cadence, it is <b>not sampling at all</b>: see
    /// <see cref="TranslateHoldFrames"/>, and note that <see cref="NoticeGeometry"/> has stated the
    /// principle in its own comment since ModBuild 193 — it returns dirty for a rect or scale change
    /// and NOT for a pure translation, because <i>moving a window changes nothing about what it draws
    /// or how big its render target must be</i>. This build finally acts on that sentence.</para>
    ///
    /// <para><b>WHY IT IS EXACTLY THESE TWO SUB-VIEWS.</b> The character sheet and the perks view are
    /// the only sub-views whose content reaches OUTSIDE the host rect, so they are the only ones with
    /// a non-zero overspill and therefore the only ones with a quantum boundary to sit on. Every
    /// working sub-view reports <c>CAPTURE FRAME ... = the host rect: no content draws outside it</c>
    /// — zero overspill, no boundary, no re-allocation possible, ever. That is the 2-vs-2 differential
    /// four rounds have been hunting, and this is its mechanism.</para>
    ///
    /// <para><b>AND IT EXPLAINS THE FREEZE.</b> When the motion stops, the frame is latched at
    /// whichever of the two values the last measurement produced. If that is the SMALLER one while the
    /// content genuinely needs the larger, 32 px of content lies outside the capture frame and is
    /// simply not in the picture — permanently, until something re-measures. That is <i>"manche
    /// Elemente nicht sichtbar"</i>, and it is random because it depends on which side of the boundary
    /// the last sample fell. (It is NOT the only mechanism producing that sentence; the other, and the
    /// larger one, is the TMP sub-mesh cull latch — see <see cref="RepairSubMeshCull"/>.)</para>
    ///
    /// <para><b>WHAT ModBuild 201 GOT RIGHT AND WHAT IT LEFT OPEN.</b> The 32 px quantum was added to
    /// stop the sub-pixel PHASE re-rolling on every measurement and it succeeded at that: the frame no
    /// longer takes arbitrary fractional values. It has no HYSTERESIS, so a union sitting on a
    /// boundary still flips the whole frame by a full quantum, and one quantum is 32 px of phase.
    /// ModBuild 204 adds the hysteresis: <b>GROW immediately, SHRINK only past a one-quantum dead band,
    /// only after a run of <see cref="FrameShrinkRunMeasurements"/> consecutive measurements, and only
    /// while the window is not moving.</b></para>
    ///
    /// <para><b>REJECTED ALTERNATIVES.</b>
    /// <list type="number">
    /// <item><b>A COARSER QUANTUM (64 or 128 px).</b> It moves the boundary; it does not remove it. A
    /// union that lands on the new boundary flaps by 64 or 128 px instead of 32, i.e. the same defect
    /// with a larger amplitude and a larger allocation. The observed flap is not caused by the quantum
    /// being too fine, it is caused by there being no memory between measurements.</item>
    /// <item><b>A REALLOCATION RATE LIMITER.</b> It would hide the churn and keep the defect: the
    /// capture frame would still alternate, the camera would still re-frame, and the phase would still
    /// re-roll — only the VRAM traffic would drop. This project has already paid for a fuse that was
    /// hiding a loop ("the fuse was hiding a loop"), and the honest fix is upstream of the fuse.</item>
    /// <item><b>LATCH THE FRAME AT ENGAGE AND NEVER RE-MEASURE.</b> It fixes the flap outright and
    /// re-breaks ModBuild 192: the party window's host rect walks 328 -> 716 -> 1920 uGUI px across one
    /// session and its sub-views swap under a fixed host rect, so a latched frame crops real content.
    /// Growth must stay immediate and unconditional.</item>
    /// <item><b>SYMMETRIC HYSTERESIS (a band in both directions).</b> Refused: a delay before GROWING
    /// is a frame — possibly many — in which content the player can see is cut out of the picture. The
    /// asymmetry is the whole point, and it is why the shrink side carries every condition.</item>
    /// </list></para>
    /// </para>
    /// </summary>
    private static void MeasureFrame(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform? host = panel.HostRect;
        if (host == null || panel.HostGo == null)
            return;

        // ---- THE TRANSLATION GATE (ModBuild 204, part 1) ----------------------------------------
        // Everything this method measures is host-local, so a pure translation cannot change it. Do
        // not sample it, do not schedule it, do not pay for it. The gate is HERE rather than at the
        // call site on purpose: MeasureFrame has three callers (Engage, SyncGeometry's cadence and
        // ReleaseRepair), two of them in a file this lane does not own, and a decision that must hold
        // for all three belongs where the measurement is.
        if (SkipMeasureWhileTranslating(e, host))
        {
            e.FrameMeasuresSkipped++;
            return;
        }

        float started = Time.realtimeSinceStartup;
        Rect hostRect = host.rect;
        Rect union = hostRect;
        // THE ARGMAX/ARGMIN OF THE UNION (ModBuild 204, part 3). Four compares per drawing graphic on
        // a walk that already has its host-local bounds in hand — no second traversal, no allocation.
        ResetExtremes(hostRect);
        // THE CONTENT-SCALE CENSUS (ModBuild 201). Accumulated across the walk, committed at the end.
        float minScale = 1f;
        float minScaleArea = 0f;
        string minScaleName = string.Empty;
        int scaleSamples = 0;
        int subCritical = 0;
        float subCriticalArea = 0f;
        // A graphic must cover at least this much of the host rect before it is allowed to lower the
        // number — see MinScaledAreaFraction for why a bare minimum over every graphic is the wrong
        // statistic and would let one decorative pip quadruple every window's render target.
        float areaFloor = Mathf.Abs(hostRect.width * hostRect.height) * MinScaledAreaFraction;

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
                    {
                        union = Union(union, visible);
                        NoteExtremes(t.name, visible);
                        // THE SCALE OF THIS GRAPHIC'S OWN AUTHORED PIXELS, in host-local pixels. The
                        // two quantities are already in hand: `local` is the rect the artist authored
                        // and `bounds` is where it landed. Their ratio IS the accumulated scale chain
                        // between the two, without touching a single extra transform.
                        Rect local = rt.rect;
                        if (local.width > 1f && local.height > 1f)
                        {
                            float sx = bounds.width / local.width;
                            float sy = bounds.height / local.height;
                            // A ROTATED child measures as its bounding box, so its two ratios diverge
                            // and neither is a scale. Skip it rather than report a fiction; the axis-
                            // aligned siblings in the same subtree carry the same scale anyway.
                            if (sx > 1e-4f && sy > 1e-4f
                                && Mathf.Abs(sx - sy) <= 0.05f * Mathf.Max(sx, sy))
                            {
                                float s = Mathf.Min(sx, sy);
                                float area = Mathf.Abs(visible.width * visible.height);
                                scaleSamples++;
                                if (s < ScaledContentThreshold)
                                {
                                    subCritical++;
                                    if (area > subCriticalArea)
                                        subCriticalArea = area;
                                }
                                if (area >= areaFloor && s < minScale)
                                {
                                    minScale = s;
                                    minScaleArea = area;
                                    minScaleName = t.name;
                                }
                            }
                        }
                    }
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                ContentStack.Add(new ClipFrame(t.GetChild(i), clip));
        }
        ContentStack.Clear();

        // THE CLAMP — the one place content can still be lost, so it keeps the host rect centred and
        // gives away as much of the overspill as the budget allows, edge by edge.
        //
        // ModBuild 201: WHAT IS QUANTISED IS THE OVERSPILL, NOT THE ABSOLUTE EDGE — see
        // FrameQuantumPx for why the frame must sit on a grid at all, and read this paragraph for why
        // the grid is anchored on the HOST RECT'S OWN EDGES rather than on its centre. A host rect is
        // 390x880 or 1143x1080 uGUI px; its half-extents are not multiples of anything. Snapping
        // absolute edges to a grid would therefore inflate EVERY window (390x880 -> 448x896) and turn
        // this path's "the capture frame IS the host rect, so ON and OFF geometry are identical"
        // promise into a permanent GROWN reading on windows that overspill by nothing at all.
        // Quantising the overspill instead keeps a non-overspilling frame EXACTLY the host rect, and
        // still delivers the property the grid is for: between two measurements the host rect does
        // not move, so every frame edge differs from the last only by a whole number of grid cells,
        // and with the rate quantised to RateQuantum that is a whole number of texels.
        // The expansion limit is rounded OUTWARD too, never inward: this grid must not be able to
        // crop a single pixel more than ModBuild 200 already did. It costs at most one grid cell of
        // extra transparent margin on a window that was already at the limit.
        float padX = QuantiseUp(hostRect.width * (MaxContentExpansion - 1f) * 0.5f);
        float padY = QuantiseUp(hostRect.height * (MaxContentExpansion - 1f) * 0.5f);
        float needLeft = Mathf.Clamp(QuantiseUp(hostRect.xMin - union.xMin), 0f, padX);
        float needRight = Mathf.Clamp(QuantiseUp(union.xMax - hostRect.xMax), 0f, padX);
        float needDown = Mathf.Clamp(QuantiseUp(hostRect.yMin - union.yMin), 0f, padY);
        float needUp = Mathf.Clamp(QuantiseUp(union.yMax - hostRect.yMax), 0f, padY);
        RecordNeed(e, hostRect.width + needLeft + needRight, hostRect.height + needDown + needUp);

        // ---- THE HYSTERESIS (ModBuild 204, part 2) ----------------------------------------------
        // What is held is the per-edge OVERSPILL, never the absolute frame: the host rect can move
        // under it (the content fit walks this window 328 -> 716 -> 1920 uGUI px in one session) and
        // that must be followed exactly and immediately, while the only part of the frame the CONTENT
        // union decides — and therefore the only part that can flap — is the overspill.
        ApplyFrameHysteresis(e, padX, padY, needLeft, needRight, needDown, needUp);
        float xMin = hostRect.xMin - e.HoldLeft;
        float xMax = hostRect.xMax + e.HoldRight;
        float yMin = hostRect.yMin - e.HoldDown;
        float yMax = hostRect.yMax + e.HoldUp;
        // CLAMPED is judged against the frame that is actually COMMITTED, not against the raw need:
        // the question the field answers is "is visible content outside the rectangle the camera
        // frames", and after this build those two rectangles are no longer the same thing.
        bool clamped = xMin > union.xMin + 0.5f || xMax < union.xMax - 0.5f
                       || yMin > union.yMin + 0.5f || yMax < union.yMax - 0.5f;
        Rect frame = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        if (frame.width < 1f || frame.height < 1f)
            frame = hostRect;

        // A COMMITTED CHANGE is a change of the RECTANGLE, whatever produced it — a hysteresis grow,
        // a hysteresis shrink, or the host rect moving under a fixed overspill. That is what costs a
        // reallocation and re-rolls the phase, so that is what the change record is keyed on.
        bool frameChanged = Mathf.Abs(frame.xMin - e.Frame.xMin) > 0.5f
                            || Mathf.Abs(frame.xMax - e.Frame.xMax) > 0.5f
                            || Mathf.Abs(frame.yMin - e.Frame.yMin) > 0.5f
                            || Mathf.Abs(frame.yMax - e.Frame.yMax) > 0.5f;
        if (frameChanged)
            NoteFrameChange(e, frame);

        e.Frame = frame;
        e.HostRectAtMeasure = hostRect;
        e.ExpandX = Mathf.Max(0f, frame.width - hostRect.width);
        e.ExpandY = Mathf.Max(0f, frame.height - hostRect.height);
        e.ExpandClamped = clamped;
        e.ContentMeasures++;
        e.LastMeasureFrame = Time.frameCount;
        e.ScaleAtMeasure = host.lossyScale;
        e.ScaleAtMeasureValid = true;
        CommitExtremes(e);
        RecordContentScale(e, minScale, minScaleArea, minScaleName, scaleSamples,
                           subCritical, subCriticalArea);
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

    /// <summary>Snap an overspill (always &gt;= 0) UP to the capture-frame grid — see
    /// <see cref="FrameQuantumPx"/>. Every use rounds OUTWARD, so quantising the capture frame can
    /// only ever grow it and can never crop content that ModBuild 200 kept.</summary>
    private static float QuantiseUp(float v) => Mathf.Ceil(v / FrameQuantumPx) * FrameQuantumPx;

    // ---- THE TRANSLATION GATE (ModBuild 204, part 1) --------------------------------------------

    /// <summary>
    /// <b>MAY THIS MEASUREMENT BE SKIPPED BECAUSE THE WINDOW IS ONLY BEING CARRIED?</b>
    ///
    /// <para>THREE conditions, and all three must hold. (1) Nothing has explicitly FORCED a measure —
    /// a sub-view change sets <see cref="Entry.ForceMeasure"/> and is the one event that changes what
    /// the window draws without touching its transform. (2) The host moved within the last
    /// <see cref="TranslateHoldFrames"/> frames, which during a drag is every frame, because
    /// <c>GrabbableModal.SyncHostToFrame</c> rewrites the host pose from the grab frame every frame.
    /// (3) NEITHER the host rect NOR the host's lossy scale has changed since the last completed
    /// measurement — i.e. the change really is a pure translation (or rotation) and not the resize or
    /// the re-scale that <see cref="NoticeGeometry"/> already treats as a real invalidation.</para>
    ///
    /// <para><b>WHY THIS IS SOUND, and the one thing that would falsify it.</b>
    /// <see cref="MeasureFrame"/> expresses every bound in HOST-LOCAL uGUI pixels
    /// (<see cref="TryHostLocalBounds"/> inverse-transforms world corners through the host), so a
    /// rigid motion of the host cancels EXACTLY and the union is translation invariant up to float
    /// rounding — which is many orders below the 32 px <see cref="FrameQuantumPx"/> the frame is
    /// quantised to. Nothing downstream reads a camera or a viewport either: uGUI's own rect culling
    /// is judged against the mask rect, not against a view. <b>What a pure translation CAN still change
    /// is the CONTENT: an animation, a scroll or a pooled child arriving mid-drag genuinely moves the
    /// union.</b> That is the cost of this gate and it is bounded, not hidden — such a growth is
    /// deferred to the release, which re-measures unconditionally
    /// <see cref="ReleaseSettleFrames"/> frames after the hand lets go, and to the sub-view force
    /// above. The report prints the skip count next to the measure count so the size of that deferral
    /// is always on the record.</para>
    ///
    /// <para>The rect/scale test is made HERE, from the live host, rather than by reading
    /// <see cref="NoticeGeometry"/>'s verdict: that verdict is computed in another lane's file and is
    /// not passed down, and re-deriving it from two values this method already needs costs four
    /// compares.</para>
    /// </summary>
    private static bool SkipMeasureWhileTranslating(Entry e, RectTransform host)
    {
        if (e.ForceMeasure)
        {
            e.ForceMeasure = false;
            return false;
        }
        if (Time.frameCount - e.LastMotionFrame > TranslateHoldFrames)
            return false;
        if (!e.ScaleAtMeasureValid)
            return false;
        Rect r = host.rect;
        if (Mathf.Abs(r.width - e.HostRectAtMeasure.width) > 0.5f
            || Mathf.Abs(r.height - e.HostRectAtMeasure.height) > 0.5f)
            return false;
        Vector3 scale = host.lossyScale;
        // The same relative epsilon NoticeGeometry uses for its own rescale test, so the two cannot
        // disagree about what counts as a scale change.
        if ((scale - e.ScaleAtMeasure).magnitude > e.ScaleAtMeasure.magnitude * 1e-4f + 1e-9f)
            return false;
        return true;
    }

    // ---- THE HYSTERESIS (ModBuild 204, part 2) --------------------------------------------------

    /// <summary>
    /// <b>ASYMMETRIC HYSTERESIS ON THE PER-EDGE CAPTURE OVERSPILL — GROW NOW, SHRINK SLOWLY, AND ONLY
    /// IN THE SAFE DIRECTION.</b> The measured flap it exists to swallow, and every alternative that
    /// was rejected, are in <see cref="MeasureFrame"/>'s block comment. The rules, in order:
    /// <list type="number">
    /// <item><b>GROWTH IS IMMEDIATE AND UNCONDITIONAL</b>, per edge, whatever the window is doing.
    /// Content must never be cropped, not for one frame; a delay here would be the ModBuild 192
    /// defect back again.</item>
    /// <item><b>A NEED WITHIN <see cref="FrameShrinkDeadBandQuanta"/> QUANTA OF THE COMMITTED FRAME
    /// NEVER SHRINKS IT.</b> The ModBuild 203 flap is exactly one quantum, so the dead band kills it
    /// by construction rather than merely damping it.</item>
    /// <item><b>A LARGER SHRINK NEEDS A RUN</b> of <see cref="FrameShrinkRunMeasurements"/>
    /// consecutive measurements (or <see cref="FrameOutlierShrinkRunMeasurements"/> while an outlier
    /// growth is pending) AND the window at rest. What it then adopts is the run's per-edge MAXIMUM,
    /// never its last reading, so a shrink can never crop something that was needed inside the
    /// run.</item>
    /// <item><b>AN OUTLIER GROWTH IS ADOPTED IN FULL AND MARKED.</b> See
    /// <see cref="FrameOutlierGrowQuanta"/> for how it is released again, which is the answer to "a
    /// window must not ratchet upward forever".</item>
    /// </list>
    ///
    /// <para><b>WHAT THIS DOES TO THE TWO CEILINGS, stated because holding a frame LARGER than the
    /// current need is a real cost and this is where it is paid.</b>
    /// <list type="bullet">
    /// <item><b><see cref="MaxRtDimension"/> = 4096, and on this window the headroom is under 30 px
    /// of frame width.</b> The rate ceiling is <c>4096 / width</c> floored to
    /// <see cref="RateQuantum"/>: at the measured 2020 px frame that is 2.03 -> <b>2.00</b>, exactly
    /// the band limit, and at 2052 px — ONE quantum wider — it is 1.996 -> <b>1.75</b>. So a growth in
    /// X is not cosmetic and a held-large frame can silently cost this window its band limit. That is
    /// why <see cref="NoteFrameChange"/> computes the ceiling on both sides of every committed change
    /// and warns once, by name, when a growth pushes it under
    /// <see cref="BandLimitedTexelsPerPixel"/>; the live value is on every CAPTURE FRAME line as
    /// well. Growing can no longer step the achieved rate down without the log saying so.</item>
    /// <item><b><see cref="MaxPanelVramBytes"/> = 160 MB, and the hysteresis SPENDS LESS of it, not
    /// more.</b> The dead band's whole worst case is one quantum of extra margin per edge; on a
    /// 2020x1496 frame at rate 2.00 that is under 3 % of the target's texels, i.e. ~4 MB against this
    /// window's ~150 MB. Set against it, the ModBuild 203 log measured 58 destroy-and-recreate cycles
    /// of that ~150 MB target in 62 seconds. The hysteresis trades a few MB of steady residency for
    /// removing that churn, and if the trade ever goes the wrong way — a window held far above its
    /// need — the CAPTURE FRAME line says so directly as a large REFUSED-shrink count next to a large
    /// frame, and <see cref="FrameOutlierGrowQuanta"/> is the lever.</item>
    /// </list></para>
    /// </summary>
    private static void ApplyFrameHysteresis(Entry e, float padX, float padY,
                                             float needLeft, float needRight,
                                             float needDown, float needUp)
    {
        // The standing hold is clamped to the CURRENT expansion limits first: the host rect can shrink
        // under it, and a hold left over from a larger host would push the frame past the limit that
        // MaxContentExpansion exists to enforce.
        e.HoldLeft = Mathf.Min(e.HoldLeft, padX);
        e.HoldRight = Mathf.Min(e.HoldRight, padX);
        e.HoldDown = Mathf.Min(e.HoldDown, padY);
        e.HoldUp = Mathf.Min(e.HoldUp, padY);

        float step = Mathf.Max(Mathf.Max(needLeft - e.HoldLeft, needRight - e.HoldRight),
                               Mathf.Max(needDown - e.HoldDown, needUp - e.HoldUp));
        if (step > 0.5f)
        {
            bool outlier = step > FrameOutlierGrowQuanta * FrameQuantumPx + 0.5f;
            e.HoldLeft = Mathf.Max(e.HoldLeft, needLeft);
            e.HoldRight = Mathf.Max(e.HoldRight, needRight);
            e.HoldDown = Mathf.Max(e.HoldDown, needDown);
            e.HoldUp = Mathf.Max(e.HoldUp, needUp);
            e.FrameGrows++;
            e.ShrinkRun = 0;
            if (outlier)
            {
                e.FrameOutlierGrowths++;
                e.FrameOutlierPending = true;
            }
            else
            {
                // An ordinary growth REPLACES a pending outlier: the frame is now large because
                // ordinary content asked for it, so it must be given back on the ordinary run length.
                e.FrameOutlierPending = false;
            }
            return;
        }

        float dead = FrameShrinkDeadBandQuanta * FrameQuantumPx + 0.5f;
        bool candidate = e.HoldLeft - needLeft > dead || e.HoldRight - needRight > dead
                         || e.HoldDown - needDown > dead || e.HoldUp - needUp > dead;
        if (!candidate)
        {
            // Inside the dead band on every edge. This is the ModBuild 203 flap's smaller value and it
            // is not a shrink request at all — the run is broken rather than counted as refused, so
            // the REFUSED counter stays a measure of real pressure against the hysteresis.
            e.ShrinkRun = 0;
            return;
        }

        if (e.ShrinkRun == 0)
        {
            e.RunLeft = needLeft;
            e.RunRight = needRight;
            e.RunDown = needDown;
            e.RunUp = needUp;
        }
        else
        {
            e.RunLeft = Mathf.Max(e.RunLeft, needLeft);
            e.RunRight = Mathf.Max(e.RunRight, needRight);
            e.RunDown = Mathf.Max(e.RunDown, needDown);
            e.RunUp = Mathf.Max(e.RunUp, needUp);
        }
        e.ShrinkRun++;

        int required = e.FrameOutlierPending
            ? FrameOutlierShrinkRunMeasurements
            : FrameShrinkRunMeasurements;
        if (e.ShrinkRun < required || IsMoving(e))
        {
            e.FrameShrinksRefused++;
            return;
        }

        bool shrank = false;
        if (e.HoldLeft - e.RunLeft > dead) { e.HoldLeft = e.RunLeft; shrank = true; }
        if (e.HoldRight - e.RunRight > dead) { e.HoldRight = e.RunRight; shrank = true; }
        if (e.HoldDown - e.RunDown > dead) { e.HoldDown = e.RunDown; shrank = true; }
        if (e.HoldUp - e.RunUp > dead) { e.HoldUp = e.RunUp; shrank = true; }
        e.ShrinkRun = 0;
        if (!shrank)
        {
            // The run's MAXIMUM turned out to be inside the dead band even though some individual
            // reading was not. Refused, and counted as refused: that is exactly the flap signature.
            e.FrameShrinksRefused++;
            return;
        }
        e.FrameShrinks++;
        if (e.FrameOutlierPending)
        {
            e.FrameOutlierReleases++;
            e.FrameOutlierPending = false;
        }
    }

    /// <summary>Record one measured NEED into the per-window distribution. The last reading of a
    /// flapping quantity is indistinguishable from a steady one, which is why this is kept as
    /// lowest / mean / highest over a count and never as a single field.</summary>
    private static void RecordNeed(Entry e, float w, float h)
    {
        if (e.NeedReadings == 0)
        {
            e.NeedWLowest = w;
            e.NeedWHighest = w;
            e.NeedHLowest = h;
            e.NeedHHighest = h;
        }
        else
        {
            if (w < e.NeedWLowest) e.NeedWLowest = w;
            if (w > e.NeedWHighest) e.NeedWHighest = w;
            if (h < e.NeedHLowest) e.NeedHLowest = h;
            if (h > e.NeedHHighest) e.NeedHHighest = h;
        }
        e.NeedReadings++;
        e.NeedWSum += w;
        e.NeedHSum += h;
    }

    /// <summary>The per-axis capture RATE CEILING <see cref="MaxRtDimension"/> leaves for a frame,
    /// quantised down to <see cref="RateQuantum"/> exactly as <see cref="ResolveRate"/> does it.
    /// <para>It is computed here — a pure function of the frame and two constants — so that a GROWTH
    /// which silently steps the achievable rate down is named by the lane that caused it. The party
    /// window's 2020 px frame leaves 4096/2020 = 2.03 -> <b>2.00</b>, exactly the band limit; one
    /// quantum wider (2052 px) leaves 1.996 -> <b>1.75</b>, and the ModBuild 203 outlier at 2276 px
    /// leaves 1.80 -> 1.75, which is the only sub-band-limit rate in that whole session
    /// (<c>GOT 1.75</c>). So on THIS window the headroom above the band limit is under 30 px of frame
    /// width, and a growth in X is not a cosmetic cost.</para></summary>
    private static float FrameRateCeilingFor(Rect frame)
    {
        float raw = Mathf.Min(MaxRtDimension / Mathf.Max(frame.width, 1f),
                              MaxRtDimension / Mathf.Max(frame.height, 1f));
        return Mathf.Max(RateQuantum, Mathf.Floor(raw / RateQuantum) * RateQuantum);
    }

    // ---- THE EXTREMES (ModBuild 204, part 3) ----------------------------------------------------

    /// <summary>Live extremes of the measurement in progress. Static scratch, like every other buffer
    /// in this file, and committed onto the entry at the end of the walk.</summary>
    private static readonly ExtremeRecord[] ExtremeScratch = new ExtremeRecord[4];

    private static void ResetExtremes(Rect hostRect)
    {
        // Seeded from the HOST RECT, because the union is: an edge no graphic reaches past is set by
        // the host itself, and saying so is more useful than reporting an empty record.
        ExtremeScratch[EdgeLeft] = new ExtremeRecord
            { Value = hostRect.xMin, Name = "(host rect)", Rect = hostRect, Valid = true };
        ExtremeScratch[EdgeRight] = new ExtremeRecord
            { Value = hostRect.xMax, Name = "(host rect)", Rect = hostRect, Valid = true };
        ExtremeScratch[EdgeDown] = new ExtremeRecord
            { Value = hostRect.yMin, Name = "(host rect)", Rect = hostRect, Valid = true };
        ExtremeScratch[EdgeUp] = new ExtremeRecord
            { Value = hostRect.yMax, Name = "(host rect)", Rect = hostRect, Valid = true };
    }

    /// <summary>Four compares against the running extremes, for one drawing graphic whose host-local
    /// visible rect the walk already computed. This is the whole cost of part 3.</summary>
    private static void NoteExtremes(string name, Rect visible)
    {
        if (visible.xMin < ExtremeScratch[EdgeLeft].Value)
            ExtremeScratch[EdgeLeft] = new ExtremeRecord
                { Value = visible.xMin, Name = name, Rect = visible, Valid = true };
        if (visible.xMax > ExtremeScratch[EdgeRight].Value)
            ExtremeScratch[EdgeRight] = new ExtremeRecord
                { Value = visible.xMax, Name = name, Rect = visible, Valid = true };
        if (visible.yMin < ExtremeScratch[EdgeDown].Value)
            ExtremeScratch[EdgeDown] = new ExtremeRecord
                { Value = visible.yMin, Name = name, Rect = visible, Valid = true };
        if (visible.yMax > ExtremeScratch[EdgeUp].Value)
            ExtremeScratch[EdgeUp] = new ExtremeRecord
                { Value = visible.yMax, Name = name, Rect = visible, Valid = true };
    }

    private static void CommitExtremes(Entry e)
    {
        for (int i = 0; i < 4; i++)
            e.Extremes[i] = ExtremeScratch[i];
    }

    /// <summary>
    /// <b>NAME THE FLAPPER.</b> One sentence per committed capture-frame change: the previous and the
    /// new rectangle, and for EACH of the four edges the graphic that set it, with its host-local
    /// rect, then and now.
    /// <para>This is the field the last four rounds did not have. The existing <c>CONTENT EXTREMES</c>
    /// census (<c>CanvasConversion.3.Fit.cs</c>, another lane) names the LEFT and RIGHT edge only, and
    /// the ModBuild 203 flap is entirely in Y — so nothing in the log could say whether the union
    /// genuinely changed or whether the SAME graphic was being measured to two different heights. If
    /// the TOP/BOTTOM names are identical across a change and only the numbers move, it is the second;
    /// if the names change, the window's content genuinely changed and the hysteresis is doing exactly
    /// what it should by making that cost one reallocation instead of 2.51 per second.</para>
    /// <para>It also records what the change did to the achievable capture RATE — see
    /// <see cref="FrameRateCeilingFor"/> — and warns ONCE per window if a growth pushed that ceiling
    /// below the band limit, so growing can never silently step the picture down.</para>
    /// </summary>
    private static void NoteFrameChange(Entry e, Rect frame)
    {
        e.FrameChanges++;
        float wasCeiling = e.FrameRateCeiling > 0f ? e.FrameRateCeiling : FrameRateCeilingFor(e.Frame);
        float nowCeiling = FrameRateCeilingFor(frame);
        e.FrameRateCeiling = nowCeiling;

        FrameSb.Length = 0;
        FrameSb.Append(e.Frame.width.ToString("F0")).Append('x')
               .Append(e.Frame.height.ToString("F0")).Append(" -> ")
               .Append(frame.width.ToString("F0")).Append('x').Append(frame.height.ToString("F0"))
               .Append(" uGUI px on frame ").Append(Time.frameCount)
               .Append(IsMoving(e) ? " (window MOVING)" : " (window at rest)")
               .Append("; rate ceiling ").Append(wasCeiling.ToString("F2")).Append(" -> ")
               .Append(nowCeiling.ToString("F2")).Append(" texels per authored px (the ")
               .Append(MaxRtDimension).Append(" px per-axis ceiling over the frame, quantised to ")
               .Append(RateQuantum.ToString("F2")).Append("); EXTREMES then -> now:");
        for (int i = 0; i < 4; i++)
        {
            ExtremeRecord was = e.ExtremesPrev[i];
            ExtremeRecord now = ExtremeScratch[i];
            FrameSb.Append(' ').Append(EdgeNames[i]).Append(' ');
            if (!was.Valid)
                FrameSb.Append("(no previous reading)");
            else
                FrameSb.Append(was.Value.ToString("F0")).Append(" set by '").Append(was.Name)
                       .Append("' at ").Append(RectText(was.Rect));
            FrameSb.Append(" -> ").Append(now.Value.ToString("F0")).Append(" set by '")
                   .Append(now.Name).Append("' at ").Append(RectText(now.Rect))
                   .Append(was.Valid && string.Equals(was.Name, now.Name, System.StringComparison.Ordinal)
                       ? " [SAME GRAPHIC, different measurement]"
                       : " [DIFFERENT GRAPHIC]")
                   .Append(i < 3 ? ";" : ".");
        }
        e.FrameChangeNote = FrameSb.ToString();
        FrameSb.Length = 0;
        for (int i = 0; i < 4; i++)
            e.ExtremesPrev[i] = ExtremeScratch[i];

        if (nowCeiling < wasCeiling - 1e-3f && nowCeiling < BandLimitedTexelsPerPixel - 1e-3f
            && !e.RateCeilingWarned)
        {
            e.RateCeilingWarned = true;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' GREW its capture frame to "
                              + $"{frame.width:F0}x{frame.height:F0} uGUI px, and that growth stepped "
                              + $"the achievable capture rate from {wasCeiling:F2} down to "
                              + $"{nowCeiling:F2} render-target texels per authored pixel — BELOW the "
                              + $"{BandLimitedTexelsPerPixel:F2} band limit, because "
                              + $"{MaxRtDimension} / {frame.width:F0} px leaves less than 2.00 and the "
                              + $"rate is quantised to {RateQuantum:F2}. THE CONSEQUENCE: on this "
                              + "window the eye starts reading unfiltered mip level 0 again, i.e. "
                              + "exactly the ModBuild 198 defect, for as long as the frame stays this "
                              + "wide. THE HEADROOM IS SMALL BY CONSTRUCTION: at 2020 px this window "
                              + "sits at 2.03 and one 32 px quantum of extra width takes it to 1.996. "
                              + "THE LEVERS, in order: find what draws outside the host rect (the "
                              + "EXTREMES field on this window's CAPTURE FRAME line names it), then "
                              + "MaxContentExpansion, then MaxRtDimension. This is warned ONCE per "
                              + "window; the CAPTURE FRAME line carries the live ceiling every "
                              + "report.");
        }
    }

    private static string RectText(Rect r) =>
        $"({r.xMin:F0},{r.yMin:F0})-({r.xMax:F0},{r.yMax:F0})";

    /// <summary>Scratch for the frame-change sentence and for the two ModBuild 204 report lines.
    /// Deliberately not <c>Sb</c>: that one is owned by <c>Report</c>, which is another lane's file,
    /// and these lines are emitted from inside the same LateUpdate.</summary>
    private static readonly StringBuilder FrameSb = new(1024);

    /// <summary>
    /// Commit the content-scale census this measurement produced, and keep the whole distribution of
    /// it — because the summary field of the previous round was quoted as an operating point and was
    /// not one. See <see cref="Entry.MinContentScale"/>.
    /// </summary>
    private static void RecordContentScale(Entry e, float minScale, float area, string name,
                                           int samples, int subCritical, float subCriticalArea)
    {
        // A window whose walk found no measurable rect at all keeps the previous reading rather than
        // silently resetting to 1.0 — an unmeasured window and an unscaled one must not print alike.
        if (samples <= 0)
        {
            e.ContentScaleUnmeasured++;
            return;
        }
        minScale = Mathf.Clamp(minScale, MinContentScaleFloor, 1f);
        e.MinContentScale = minScale;
        e.MinContentScaleArea = area;
        e.MinContentScaleName = name;
        e.ContentScaleSamples = samples;
        e.ScaledGraphics = subCritical;
        e.ScaledGraphicsArea = subCriticalArea;

        e.ContentScaleReadings++;
        e.ContentScaleSum += minScale;
        if (e.ContentScaleReadings == 1)
        {
            e.MinContentScaleLowest = minScale;
            e.MinContentScaleHighest = minScale;
        }
        else
        {
            if (minScale < e.MinContentScaleLowest)
                e.MinContentScaleLowest = minScale;
            if (minScale > e.MinContentScaleHighest)
                e.MinContentScaleHighest = minScale;
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
    /// produces a wrong number once and then never again reproducibly.
    /// <para>From ModBuild 203 it carries the RESOLVED DRAW ORDER and the CLIP down the walk (see
    /// <see cref="OrderFrame"/>) so the over-paint census is O(subtree) and rides this one traversal
    /// instead of adding a second.</para></summary>
    private static readonly List<OrderFrame> TextWalk = new(256);

    /// <summary>
    /// A node of <see cref="MeasureContentCore"/>'s walk, plus everything that is decided by its
    /// ANCESTORS and would otherwise have to be re-derived by climbing back up per graphic:
    /// <list type="bullet">
    /// <item><see cref="Order"/> — the <c>sortingOrder</c> of the nearest enabled ancestor
    /// <see cref="Canvas"/> that has <c>overrideSorting</c> set, falling back to the host (root)
    /// canvas's own order. This is the OUTER half of uGUI's painter key.</item>
    /// <item><see cref="Canvas"/> — the nearest enabled ancestor canvas whether or not it overrides,
    /// so a plate can name the canvas it belongs to and print that canvas's sorting state.</item>
    /// <item><see cref="Clip"/> / <see cref="ClipEmpty"/> — the mask rectangle in force, in host-local
    /// uGUI px, exactly as <see cref="MeasureFrame"/> carries it. A scroll list's off-screen rows must
    /// not be counted as painted over: they are not drawn at all.</item>
    /// </list>
    /// <para><b>THE CLIP IS ADVISORY FOR THE CENSUS AND INERT FOR THE TEXT SCAN.</b> A fully clipped
    /// subtree still gets walked and its text still gets scanned and repaired, because those counters
    /// are eight rounds old and this round must not silently change what they measure — it only stops
    /// the census from recording a rect nothing draws.</para>
    /// </summary>
    private readonly struct OrderFrame
    {
        internal readonly Transform Transform;
        internal readonly int Order;
        internal readonly Canvas? Canvas;
        internal readonly Rect Clip;
        internal readonly bool ClipEmpty;

        internal OrderFrame(Transform transform, int order, Canvas? canvas, Rect clip, bool clipEmpty)
        {
            Transform = transform;
            Order = order;
            Canvas = canvas;
            Clip = clip;
            ClipEmpty = clipEmpty;
        }
    }

    /// <summary>
    /// One drawing graphic as the over-paint census sees it: WHERE it lands (host-local uGUI px,
    /// already clipped by its masks), WHEN it is painted (the resolved order key), and whether it is
    /// itself a full-frame plate. Everything the census answers is a comparison between two of these.
    /// </summary>
    private readonly struct DrawRecord
    {
        internal readonly Graphic Graphic;
        internal readonly Transform Transform;
        internal readonly Canvas? Canvas;
        internal readonly Rect Rect;
        internal readonly float Area;
        /// <summary>Fraction of the OPEN SUB-VIEW's rect this graphic covers (0..1).</summary>
        internal readonly float Cover;
        internal readonly int Order;
        internal readonly int Index;
        internal readonly bool Plate;

        internal DrawRecord(Graphic graphic, Transform transform, Canvas? canvas, Rect rect,
                            float area, float cover, int order, int index, bool plate)
        {
            Graphic = graphic;
            Transform = transform;
            Canvas = canvas;
            Rect = rect;
            Area = area;
            Cover = cover;
            Order = order;
            Index = index;
            Plate = plate;
        }
    }

    /// <summary>Every drawing graphic of the last census, in walk order. Bounded by
    /// <see cref="MaxOverPaintGraphics"/>; cleared the moment the census is committed, so no
    /// <see cref="Graphic"/> reference is held across frames.</summary>
    private static readonly List<DrawRecord> DrawRecords = new(512);

    /// <summary>Indices into <see cref="DrawRecords"/> of the records that are full-frame plates.</summary>
    private static readonly List<int> PlateRecords = new(8);

    /// <summary>Scratch for the per-plate sentences and for the foreign-subtree list.</summary>
    private static readonly StringBuilder OverPaintSb = new(1024);
    private static readonly StringBuilder ForeignSb = new(256);

    /// <summary>The three largest graphics one plate covers, as (area, name) — filled per plate.</summary>
    private static readonly List<(float Area, string Name)> CoveredTop = new(4);

    /// <summary>Property ids the plate census probes for, cached once: a string lookup per property
    /// per plate per scan would be the most expensive thing on this line. <c>_GrabTexture</c> and
    /// <c>_BackgroundTexture</c> are how a UI blur shader reads what is behind it;
    /// <c>_CameraOpaqueTexture</c> is the built-in-pipeline equivalent; a bound-but-null
    /// <c>_MainTex</c> is a plate drawing a flat colour over whatever it covers.</summary>
    private static readonly int GrabTexId = Shader.PropertyToID("_GrabTexture");
    private static readonly int BackgroundTexId = Shader.PropertyToID("_BackgroundTexture");
    private static readonly int CameraOpaqueTexId = Shader.PropertyToID("_CameraOpaqueTexture");

    /// <summary>Scratch for the atlas census sentence.</summary>
    private static readonly StringBuilder AtlasSb = new(256);

    /// <summary>Cap on regenerations one scan may force, so a repair can never become the spike.
    /// <para>From ModBuild 204 it is a cap on ONE PASS and no longer a cap on COVERAGE — see
    /// <see cref="WantsRegeneration"/> for the resume cursor, and for the reason the old behaviour
    /// was unsound.</para></summary>
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
            DrawRecords.Clear();
            PlateRecords.Clear();
            CoveredTop.Clear();
            OverPaintSb.Length = 0;
            ForeignSb.Length = 0;
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
        e.SubMeshesEmpty = 0;
        e.SubMeshesInUse = 0;
        e.SubMeshesInactive = 0;
        e.SubMeshesCulled = 0;
        e.SubMeshesWrongLayer = 0;
        e.SubMeshesNoTexture = 0;
        e.SubMeshWorst = string.Empty;
        e.ContentScanTruncated = false;
        e.RegeneratedComponents = 0;
        e.RegeneratedChars = 0;
        // ---- ModBuild 204: the three-way splits, the latch census and the regeneration pass ------
        e.SubMeshesCullFlag = 0;
        e.SubMeshesOwnAlpha = 0;
        e.SubMeshesInheritedAlpha = 0;
        e.SubMeshCullLatched = 0;
        e.SubMeshCullLatchedNote = string.Empty;
        e.SubMeshCullLatchedNamed = 0;
        e.TextCulledFlag = 0;
        e.TextCulledOwnAlpha = 0;
        e.TextCulledInheritedAlpha = 0;
        e.TextCulledNote = string.Empty;
        e.RegenCapBit = false;
        e.RegenDeferred = 0;
        e.RegenSkippedBeforeCursor = 0;
        // The pair cache is rebuilt by this walk (ScanTmpSubMeshes appends to it for free). Cleared
        // HERE rather than there, because there is per-component and this is per-scan.
        e.CullPairs.Clear();
        e.CullPairsTruncated = false;
        AtlasSb.Length = 0;
        int atlasesNamed = 0;
        bool anyRepaired = false;

        // ---- THE OVER-PAINT CENSUS RIDES THIS WALK (ModBuild 203) --------------------------------
        // Nothing below adds a traversal: the census reads the same nodes, the same one
        // GetComponent<Graphic> per node, and carries what it needs down the stack.
        ResetOverPaint(e);
        RectTransform? host = panel.HostRect;
        bool census = host != null;
        // THE CENSUS'S OWN BUDGET. Only the two phases that are NOT shared with the text scan can be
        // attributed honestly — resolving the open sub-view and committing the plate comparison. The
        // per-node share (one GetComponent<Canvas>, two mask GetComponents on interior nodes only,
        // and one host-local bounds per DRAWING graphic) rides inside the CONTENT INTEGRITY scan's own
        // ms figure, which is printed next to this one for exactly that reason. Both are bounded:
        // MaxOverPaintGraphics records, MaxPlatesReported sentences.
        float censusStarted = Time.realtimeSinceStartup;
        if (census)
            ResolveOpenSubView(e, panel, host!);
        e.OverPaintMs += (Time.realtimeSinceStartup - censusStarted) * 1000.0;
        float refArea = Mathf.Abs(e.OpenViewRect.width * e.OpenViewRect.height);
        int rootOrder = panel.HostCanvas != null ? panel.HostCanvas.sortingOrder : 0;
        int visitIndex = 0;

        TextWalk.Clear();
        TextWalk.Add(new OrderFrame(panel.HostGo.transform, rootOrder, panel.HostCanvas,
                                    Unbounded, clipEmpty: false));
        while (TextWalk.Count > 0)
        {
            int last = TextWalk.Count - 1;
            OrderFrame node = TextWalk[last];
            TextWalk.RemoveAt(last);
            Transform t = node.Transform;
            if (t == null || !t.gameObject.activeInHierarchy)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;
            bool isRoot = ReferenceEquals(t, panel.HostGo.transform);
            e.OverPaintVisited++;
            // THE DEPTH-FIRST HIERARCHY INDEX — the INNER half of uGUI's painter key. Children are
            // pushed in reverse and popped LIFO, so this counter increments in exact pre-order, which
            // is the order uGUI walks a canvas's graphics in when it builds its batches.
            int index = ++visitIndex;
            // The same foreign-subtree rule the frame measurement and the layer sweep use, for the
            // same reason: a real Renderer or a Camera in here belongs to somebody else. ModBuild 203
            // NAMES them on the way past instead of only counting them — see NoteForeignSubtree.
            if (!isRoot)
            {
                var renderer = t.GetComponent<Renderer>();
                var foreignCam = renderer == null ? t.GetComponent<Camera>() : null;
                if (renderer != null || foreignCam != null)
                {
                    if (census)
                        NoteForeignSubtree(e, host!, t, renderer, foreignCam);
                    continue;
                }
            }

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

            // ---- resolve this node's draw order and clip, for itself and for its children --------
            int order = node.Order;
            Canvas? canvas = node.Canvas;
            Rect clip = node.Clip;
            bool clipEmpty = node.ClipEmpty;
            if (census && !isRoot)
            {
                // A nested Canvas only starts a new sorting band when it is ENABLED and actually
                // OVERRIDES. That distinction is not decorative: 19 of this window's 20 adopted
                // nested canvases carry overrideSorting = false, so they are NOT sorting roots and
                // everything under them sorts by hierarchy inside the host batch — exactly as the
                // flat game drew it. Reading the flag rather than assuming it is what keeps this
                // census correct now that the sibling-canvas tie hypothesis is dead, and what would
                // make it show a real tie if the adoption's sorting state ever changed.
                var own = t.GetComponent<Canvas>();
                if (own != null && own.enabled)
                {
                    canvas = own;
                    if (own.overrideSorting)
                        order = own.sortingOrder;
                }

                var rt = t as RectTransform;
                if (rt != null)
                {
                    // A mask on a LEAF clips nothing, so the two mask GetComponents are only paid on
                    // interior nodes — which is where every mask in a uGUI hierarchy actually is.
                    if (!clipEmpty && t.childCount > 0 && ClipsChildren(t)
                        && TryHostLocalBounds(host!, rt, out Rect clipBounds))
                    {
                        if (!Intersect(clip, clipBounds, out clip))
                            clipEmpty = true;
                    }
                    if (!clipEmpty && Draws(graphic) && !IsModOwned(t.name))
                        RecordDrawn(e, host!, rt, graphic!, canvas, clip, order, index, refArea);
                }
            }

            for (int i = t.childCount - 1; i >= 0; i--)
                TextWalk.Add(new OrderFrame(t.GetChild(i), order, canvas, clip, clipEmpty));
        }
        TextWalk.Clear();
        if (census)
        {
            float commitStarted = Time.realtimeSinceStartup;
            CommitOverPaint(e);
            e.OverPaintMs += (Time.realtimeSinceStartup - commitStarted) * 1000.0;
        }

        // The pair cache is now this walk's, and it covers every text component the walk reached.
        e.CullPairCollections++;
        e.CullPairsFrame = Time.frameCount;
        e.CullPairsSource = "the content scan";
        // Park or wrap the regeneration cursor — see WantsRegeneration. Must run whether or not
        // anything was regenerated: a pass that found the whole subtree under the cap is exactly the
        // pass that completes a sweep.
        CloseRegenerationPass(e, repairAll);

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

        NoteRendererState(e, t.canvasRenderer, t.gameObject.name);
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

        if (!WantsRegeneration(e, repairAll, bad, e.TextComponents))
            return false;

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
        // THE PARENT'S OWN CULL FLAG, read ONCE. It is the other half of every sentence below: a
        // sub-mesh reading cull=TRUE under a parent reading cull=FALSE is the ModBuild 204 latch, and
        // nothing further has to be measured to say so (see RepairSubMeshCull).
        CanvasRenderer parentCr = t.canvasRenderer;
        bool parentCull = parentCr != null && parentCr.cull;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform c = parent.GetChild(i);
            if (c == null)
                continue;
            var sub = c.GetComponent<TMP_SubMeshUI>();
            if (sub == null)
                continue;
            e.SubMeshesSeen++;
            // THE PAIR CACHE rides this enumeration for free — see RepairSubMeshCull for why the
            // invariant is checked from a cache rather than from a walk. Recorded for EVERY sub-mesh
            // including the pooled empty ones: a pooled sub-mesh that the next string fills would
            // otherwise be filled with a latched cull flag and stay invisible.
            if (e.CullPairs.Count < MaxCullPairs)
                e.CullPairs.Add(new CullPair(t, sub));
            else
                e.CullPairsTruncated = true;

            // THE REPAIR, FIRST AND UNCONDITIONAL — before the emptiness test below can skip
            // anything. Only when this panel actually holds a private layer; a refused panel has
            // Layer < 0 and its subtree must stay exactly where the game put it. Deliberately NOT
            // behind the in-use test: an empty pooled sub-mesh that the next string fills would
            // otherwise be filled ON THE WRONG LAYER and stay there until the next sweep cadence,
            // which is the exact window this repair was added to close.
            int bad = 0;
            if (layer >= 0 && c.gameObject.layer != layer)
            {
                e.SubMeshesWrongLayer++;
                bad++;
                if (!IsRecorded(e, c))
                    e.Relayered.Add(new LayerRecord { Transform = c, OriginalLayer = c.gameObject.layer });
                c.gameObject.layer = layer;
            }

            // THE DENOMINATOR ModBuild 200 DID NOT HAVE, and without which its own numbers cannot be
            // read at all. TMP POOLS these objects: it creates one per material reference the string
            // has EVER needed and leaves the surplus in place with an emptied mesh. A pooled sub-mesh
            // is legitimately culled, legitimately transparent and legitimately without a texture —
            // its normal life looks exactly like the abuse. The ModBuild 200 log read
            // "19 TMP SUB-MESH(ES) ... of which 17 culled/transparent" on the character window and
            // 0 of 0 or 0 of 1 on every other window, and that difference is NOT evidence of a defect
            // until the empty ones are subtracted — the character window is simply the only window
            // with enough text to pool any. A sub-mesh whose MESH carries vertices is one the text
            // engine actually handed glyphs to; only those can be missing from the picture, and only
            // those are counted below.
            Mesh? mesh = sub.mesh;
            if (mesh == null || mesh.vertexCount == 0)
            {
                e.SubMeshesEmpty++;
                if (bad > 0 && e.SubMeshWorst.Length == 0)
                    e.SubMeshWorst = Describe(c.gameObject.name, t.text);
                continue;
            }
            e.SubMeshesInUse++;

            if (!c.gameObject.activeInHierarchy)
            {
                e.SubMeshesInactive++;
                bad++;
            }

            // ---- THREE DISJOINT BUCKETS (ModBuild 204), where there used to be one `||` ----------
            // "16 culled/transparent" is noise: the three conditions have completely different
            // causes, different fixes and different evidential weight. The CULL FLAG is the ModBuild
            // 204 latch; a zero OWN alpha is authored or animated; a zero INHERITED alpha is a
            // CanvasGroup fade somewhere above. Tested in that order and `continue`d, so every
            // sub-mesh lands in exactly one bucket and the three counts sum to the old one.
            CanvasRenderer cr = sub.canvasRenderer;
            if (cr != null && cr.cull)
            {
                e.SubMeshesCullFlag++;
                e.SubMeshesCulled++;
                bad++;
                if (!parentCull)
                {
                    // THE LATCH, and it needs no further measurement: TMP writes this flag ONLY from
                    // inside the parent's `if (m_canvasRenderer.cull != flag)` guard, so a culled
                    // child under an un-culled parent is a state TMP itself cannot reach or repair.
                    e.SubMeshCullLatched++;
                    if (e.SubMeshCullLatchedNamed < MaxCullNamed)
                    {
                        e.SubMeshCullLatchedNamed++;
                        e.SubMeshCullLatchedNote += (e.SubMeshCullLatchedNote.Length > 0 ? "; " : "")
                            + "'" + c.gameObject.name + "' cull=TRUE under '" + t.gameObject.name
                            + "' cull=FALSE";
                    }
                }
            }
            else if (cr != null && cr.GetAlpha() <= 0.004f)
            {
                e.SubMeshesOwnAlpha++;
                e.SubMeshesCulled++;
                bad++;
            }
            else if (cr != null && cr.GetInheritedAlpha() <= 0.004f)
            {
                e.SubMeshesInheritedAlpha++;
                e.SubMeshesCulled++;
                bad++;
            }

            Material mat = sub.materialForRendering;
            if (mat == null || (mat.HasProperty(MainTexId) && mat.GetTexture(MainTexId) == null))
            {
                e.SubMeshesNoTexture++;
                bad++;
            }

            if (bad > 0 && e.SubMeshWorst.Length == 0)
                e.SubMeshWorst = Describe(c.gameObject.name, t.text);
        }
    }

    /// <summary>Cached <c>_MainTex</c> id for <see cref="ScanTmpSubMeshes"/> — a string lookup per
    /// sub-mesh per scan would be the most expensive line in the walk.</summary>
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    // ---- THE TMP SUB-MESH CULL LATCH (ModBuild 204) ----------------------------------------------

    /// <summary>Work stack for <see cref="RefreshCullPairs"/>. A fourth stack rather than a shared
    /// one, for the reason every buffer in this file gets its own: these walks run inside the same
    /// LateUpdate and can be reached from each other's call chains.</summary>
    private static readonly List<Transform> CullWalk = new(256);

    /// <summary>
    /// <b>THE ROOT CAUSE, AND THE REPAIR. THIS IS MODBUILD 204's HEADLINE.</b>
    ///
    /// <para><b>THE USER'S REPORT, VERBATIM:</b> <i>"beim Loslassen wurde random ein Stand eingefroren
    /// der wieder manche elemente nicht sichtbar macht"</i> — and, before that, <i>"beim Loslassen kann
    /// es passieren, dass die dargestellte Anzeige kaputt ist ... bewege ich es nochmal und lasse los,
    /// sieht es wieder anders aus"</i>. Inside <c>New Party display</c> exactly TWO of six sub-views
    /// break: the CHARACTER SHEET and PERKS.</para>
    ///
    /// <para><b>THE EVIDENCE IS TWO QUOTATIONS OUT OF THE GAME'S OWN SHIPPED ASSEMBLIES</b>
    /// (<c>ressources/Managed/Unity.TextMeshPro.dll</c> and <c>UnityEngine.UI.dll</c>; both were
    /// decompiled and read, not inferred).
    /// <list type="number">
    /// <item><b><c>TMP_SubMeshUI.Cull(Rect, bool)</c> IS AN EMPTY METHOD.</b>
    /// <code>public override void Cull(Rect clipRect, bool validRect) { }</code>
    /// <c>TMP_SubMeshUI</c> derives from <see cref="MaskableGraphic"/>, so a <see cref="RectMask2D"/>
    /// registers it as a clippable and calls <c>Cull</c> on it every frame — <b>and it does nothing</b>.
    /// A RectMask2D is structurally incapable of culling OR UN-CULLING a TMP sub-mesh.</item>
    /// <item><b>THE ONLY WRITER OF A SUB-MESH'S CULL FLAG SITS INSIDE THE PARENT'S CHANGE-GUARD.</b>
    /// <c>TextMeshProUGUI.Cull</c>:
    /// <code>
    /// bool flag = !validRect || !clipRect.Overlaps(canvasSpaceClippingRect, allowInverse: true);
    /// if (m_canvasRenderer.cull != flag)              // THE GUARD
    /// {
    ///     m_canvasRenderer.cull = flag;
    ///     ...
    ///     for (int i = 1; i &lt; m_subTextObjects.Length &amp;&amp; m_subTextObjects[i] != null; i++)
    ///         m_subTextObjects[i].canvasRenderer.cull = flag;   // ONLY HERE
    /// }
    /// </code></item>
    /// <item><b><c>MaskableGraphic.UpdateCull(bool)</c> IS PRIVATE AND NON-VIRTUAL</b>, and
    /// <c>MaskableGraphic.UpdateClipParent()</c> calls <c>UpdateCull(cull: false)</c> whenever the
    /// resolved clipper changes: <c>if (m_ParentMask != null &amp;&amp; (rectMask2D != m_ParentMask ||
    /// !rectMask2D.IsActive())) { m_ParentMask.RemoveClippable(this); UpdateCull(cull: false); }</c>.
    /// <c>TextMeshProUGUI</c> cannot override it and does not.</item>
    /// </list></para>
    ///
    /// <para><b>THE LATCH.</b> The parent goes <c>cull: true -> false</c> through
    /// <c>UpdateClipParent</c>, <b>without the sub-mesh loop running</b>. The sub-meshes stay
    /// <c>true</c>. The next <c>TextMeshProUGUI.Cull</c> computes <c>flag = false</c>, tests
    /// <c>m_canvasRenderer.cull != flag</c> — which is now FALSE — and the guard blocks. <b>The
    /// sub-meshes are never repaired. They are permanently invisible, and nothing in TMP or in uGUI
    /// can ever fix them.</b></para>
    ///
    /// <para><b>AND THE MOD PULLS THAT TRIGGER ITSELF.</b> <c>UnmaskedUiGraphics</c> does
    /// <c>g.maskable = false; g.RecalculateClipping();</c> on every maskable graphic of a floated
    /// subtree, and <c>RecalculateClipping</c> IS <c>UpdateClipParent</c>. TMP reaches it too, from
    /// <c>TMP_SubMeshUI.OnEnable / OnDisable / OnTransformParentChanged</c> and from
    /// <c>MaskableGraphic.OnEnable / OnDisable</c> on the parent. <b>HONEST CAVEAT, because it cuts
    /// the other way:</b> <c>UnmaskedUiGraphics</c> sweeps <c>MaskableGraphic</c>, and a
    /// <c>TMP_SubMeshUI</c> IS one, so a sub-mesh that is in that sweep AND has a live
    /// <c>m_ParentMask</c> gets <c>UpdateCull(false)</c> of its own and is un-latched by accident.
    /// That is why the defect is intermittent and per-component rather than total, and it is exactly
    /// the <i>"random ein Stand"</i> the user reports.</para>
    ///
    /// <para><b>WHY EVERY PREVIOUS SCAN WAS CLEAN, AND ALL OF THEM WERE CORRECT.</b> The mesh IS
    /// submitted; the atlas IS right; the UVs ARE right; the layer IS right; the sampling IS
    /// band-limited. Twelve rounds measured all of that and every reading is still believed. A boolean
    /// on a CHILD GameObject that nobody ever split out is <c>true</c>. It is also why the missing
    /// pieces are a SCATTERED SUBSET: only a string that needs a second atlas page, a fallback font or
    /// an inline <c>&lt;sprite&gt;</c> HAS a sub-mesh at all. And it is why the character sheet reports
    /// 19 sub-meshes carrying vertices with 16 of them culled while every parent-level counter reads
    /// clean.</para>
    ///
    /// <para><b>THE REPAIR: COPY THE PARENT'S DECISION ONTO ITS CHILDREN.</b> That is precisely what
    /// TMP's own loop intends to do; this only supplies the run TMP's guard skipped. Four properties
    /// of it are load-bearing:
    /// <list type="number">
    /// <item><b>IT IS UNCONDITIONAL.</b> Not gated on a defect count, not gated on a scan finding
    /// something first, not gated on a config dial. This project has shipped FOUR remedies that never
    /// executed because they were conditioned on the very instrument that was supposed to decide
    /// whether they were needed — ModBuild 196's text regeneration and ModBuild 198's band-limit floor
    /// among them. This is not the fifth.</item>
    /// <item><b>IT CANNOT MAKE ANYTHING VISIBLE THAT TMP INTENDED TO HIDE</b>, for two independent
    /// reasons. First, it only ever copies PARENT -> CHILDREN, never the reverse, so a legitimately
    /// culled parent leaves its children culled; the two directions are counted separately
    /// (<see cref="Entry.CullRepairsUnhid"/> / <see cref="Entry.CullRepairsHid"/>) because they say
    /// completely different things about what went wrong. Second, and this is the belt to that
    /// braces: <c>TMP_SubMeshUI</c> does NOT override <c>SetClipRect</c>, so
    /// <see cref="MaskableGraphic"/>'s own implementation still runs on it and still calls
    /// <c>canvasRenderer.EnableRectClipping</c>. A RectMask2D's rectangle is therefore still enforced
    /// IN THE SHADER on an un-culled sub-mesh — <c>cull</c> was only ever the draw-call optimisation
    /// on top of it. Un-culling can at worst submit quads the shader then discards; it cannot put a
    /// glyph outside a viewport. (That asymmetry is exactly why TMP could get away with an empty
    /// <c>Cull</c> override in the first place, and exactly why the latch is invisible until a
    /// sub-mesh is stranded at <c>true</c>.)</item>
    /// <item><b>IT WALKS THE ACTUAL CHILD OBJECTS, NOT TMP'S <c>m_subTextObjects</c> ARRAY.</b> TMP's
    /// own loop reads <c>for (i = 1; i &lt; m_subTextObjects.Length &amp;&amp; m_subTextObjects[i] !=
    /// null; i++)</c> — it STOPS AT THE FIRST NULL, so a hole in that array strands every entry past
    /// it even on the code path where the guard does open. Enumerating the GameObject's children has
    /// no such hole, and it is also the enumeration <see cref="ScanTmpSubMeshes"/> already uses.</item>
    /// <item><b>THE PER-FRAME COST IS A COMPARE, NOT A WRITE.</b> The (parent, sub-mesh) pairs are
    /// CACHED (<see cref="Entry.CullPairs"/>, re-collected by every content scan and at the two
    /// release-edge instants), so the per-frame work is one boolean compare per pair — 19 of them on
    /// the character window — and only a mismatch writes. The cost is measured and printed against
    /// the 11.11 ms budget, because "unconditional and every frame" is a claim that has to be
    /// priced.</item>
    /// </list></para>
    ///
    /// <para><b>THE FLICKER HALF, SAME FILE, SAME MECHANISM.</b> <c>TextMeshProUGUI.Cull</c> defers
    /// whenever <c>m_isLayoutDirty</c>, stashing <c>m_ClipRect</c> / <c>m_ValidRect</c> and returning
    /// without updating anything; the stash is consumed later by <c>UpdateCulling()</c>, which compares
    /// that STORED clip rect against a FRESHLY COMPUTED <c>GetCanvasSpaceClippingRect()</c>. The two
    /// samples are taken at two different poses, so a rigid canvas move no longer cancels — and the
    /// size of that error is a number this mod already measures: the UPDATE -> LATEUPDATE POSE GAP,
    /// mean 2.28 and worst 31.66 authored px. Any text whose glyphs sit within ~32 authored px of a
    /// viewport edge therefore flips cull state while the window is dragged and is stable when it is
    /// still. <b>That is the flicker, in a number we already have</b>, and this repair covers it for
    /// the sub-mesh half by running every frame.</para>
    ///
    /// <para><b>REJECTED ALTERNATIVES.</b>
    /// <list type="number">
    /// <item><b>HARMONY-PATCH <c>TextMeshProUGUI.Cull</c> to run the loop unconditionally.</b> It is
    /// the narrowest possible fix and it was refused: it is a per-frame patch on a hot uGUI callback
    /// invoked by every RectMask2D for every text component in the whole game, mod-floated or not, and
    /// the mod's patch inventory is a frozen, audited surface. A cached compare over the panels this
    /// class already owns is smaller in blast radius and is measurable from the log.</item>
    /// <item><b>SET <c>maskable = false</c> ON THE SUB-MESHES.</b> That is the trigger, not the cure —
    /// it is literally the call <c>UnmaskedUiGraphics</c> makes, and whether it un-culls depends on
    /// whether <c>m_ParentMask</c> happens to be non-null at that instant. Relying on a side effect
    /// with a precondition nobody controls is what produced the intermittency in the first
    /// place.</item>
    /// <item><b>FORCE A MESH REGENERATION (<c>ForceMeshUpdate</c>) AND HOPE THE FLAG FOLLOWS.</b>
    /// ModBuild 197 already ships that on every release and the user reports the defect unchanged.
    /// Regeneration writes vertices; it does not touch <c>canvasRenderer.cull</c>, and the guard is
    /// still shut. That path is FALSIFIED, not untested.</item>
    /// <item><b>WRITE THE CHILD'S FLAG ONTO THE PARENT (the other direction).</b> Would make TMP's
    /// legitimate culling leak upward and hide whole labels. Never.</item>
    /// </list></para>
    /// </summary>
    private static void ServiceSubMeshCull(Entry e)
    {
        try
        {
            bool held = e.Panel != null && e.Panel.GuardHostHeld;

            // THE FOUR READINGS' EDGES, taken BEFORE this frame's pass so "at release" is the state
            // the hand left behind rather than the state this pass just repaired.
            if (held && !e.CullHeldLast)
            {
                // Rising edge: the reading standing before the grab is the BASELINE.
                e.CullGrabs++;
                e.CullBaseline = e.CullLive;
                e.CullDragMin = -1;
                e.CullDragMax = -1;
                e.CullDragSamples = 0;
                e.CullAtRelease = -1;
                e.CullSettled = -1;
                e.CullSettleFrame = -1;
            }
            else if (!held && e.CullHeldLast)
            {
                // Falling edge: force a fresh pair collection so the release reading cannot be a
                // stale cache, take the reading, and arm the settled one.
                RefreshCullPairs(e, "the release edge");
                RepairSubMeshCull(e);
                e.CullAtRelease = e.CullLive;
                e.CullSettleFrame = Time.frameCount + SubMeshCullSettleFrames;
                e.CullHeldLast = held;
                return;
            }
            e.CullHeldLast = held;

            if (e.CullSettleFrame >= 0 && Time.frameCount >= e.CullSettleFrame)
            {
                e.CullSettleFrame = -1;
                RefreshCullPairs(e, "the settled reading");
                RepairSubMeshCull(e);
                e.CullSettled = e.CullLive;
            }
            else
            {
                RepairSubMeshCull(e);
            }

            if (held)
            {
                e.CullDragSamples++;
                if (e.CullDragMin < 0 || e.CullLive < e.CullDragMin)
                    e.CullDragMin = e.CullLive;
                if (e.CullLive > e.CullDragMax)
                    e.CullDragMax = e.CullLive;
            }
        }
        catch (System.Exception)
        {
            // A throw here must cost this frame's invariant pass and nothing else — reaching
            // LateTick's catch would stand the whole supersample path down. The pair cache is
            // rebuilt by the next content scan regardless.
            CullWalk.Clear();
        }
    }

    /// <summary>
    /// THE INVARIANT ITSELF: for every cached (parent, sub-mesh) pair, if the child's cull flag
    /// disagrees with its parent's, write the PARENT's value onto the child. One compare per pair;
    /// only a mismatch writes. See <see cref="RepairSubMeshCull"/>'s call-site doc above for the
    /// decompiled evidence and for why the direction is one-way.
    /// </summary>
    private static void RepairSubMeshCull(Entry e)
    {
        List<CullPair> pairs = e.CullPairs;
        if (pairs.Count == 0)
        {
            e.CullLive = 0;
            return;
        }
        float started = Time.realtimeSinceStartup;
        // LATCHED PAIRS SEEN BY THIS PASS, counted BEFORE the write. This — not the number of pairs
        // left culled afterwards — is what the four readings snapshot: a pair that ends up culled
        // because its PARENT is culled is TMP working correctly and says nothing, while a child culled
        // under an UN-culled parent is a state TMP cannot reach and cannot repair. Measuring after the
        // repair would make every reading read 0 by construction, which is the mistake of measuring
        // the wrong stage that this project keeps paying for.
        int latched = 0;
        for (int i = 0; i < pairs.Count; i++)
        {
            CullPair p = pairs[i];
            if (p.Parent == null || p.Sub == null)
                continue;
            CanvasRenderer parentCr = p.Parent.canvasRenderer;
            CanvasRenderer subCr = p.Sub.canvasRenderer;
            if (parentCr == null || subCr == null)
                continue;
            e.CullChecks++;
            bool want = parentCr.cull;
            bool has = subCr.cull;
            if (has == want)
                continue;
            if (!want)
                latched++;
            subCr.cull = want;
            e.CullRepairs++;
            e.CullRepairsThisWindow++;
            if (want)
                e.CullRepairsHid++;
            else
                e.CullRepairsUnhid++;
            e.CullRepairNote = (want ? "HID '" : "UN-HID '") + p.Sub.gameObject.name
                               + "' under '" + p.Parent.gameObject.name + "' on frame "
                               + Time.frameCount.ToString();
        }
        e.CullLive = latched;
        e.CullCheckFrames++;
        e.CullCheckMs += (Time.realtimeSinceStartup - started) * 1000.0;
    }

    /// <summary>
    /// Re-collect the (parent, sub-mesh) pair cache by walking the panel subtree. Used at the two
    /// release-edge instants; the ordinary refresh rides <see cref="ScanTmpSubMeshes"/> for free.
    /// <para>It deliberately does NOT apply <see cref="ScanTmpText"/>'s filters — that method returns
    /// early for a component that is not <c>isActiveAndEnabled</c> or whose string is empty, and both
    /// of those states still have sub-mesh children whose cull flag can be latched. So this collection
    /// is a superset of the scan's, and the report says which of the two produced the cache.</para>
    /// </summary>
    private static void RefreshCullPairs(Entry e, string why)
    {
        ConvertedPanel panel = e.Panel;
        if (panel == null || panel.HostGo == null)
            return;
        float started = Time.realtimeSinceStartup;
        e.CullPairs.Clear();
        e.CullPairsTruncated = false;
        CullWalk.Clear();
        CullWalk.Add(panel.HostGo.transform);
        while (CullWalk.Count > 0)
        {
            int last = CullWalk.Count - 1;
            Transform t = CullWalk[last];
            CullWalk.RemoveAt(last);
            if (t == null)
                continue;
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
                CollectCullPairs(e, tmp);
            for (int i = t.childCount - 1; i >= 0; i--)
                CullWalk.Add(t.GetChild(i));
        }
        CullWalk.Clear();
        e.CullPairCollections++;
        e.CullPairsFrame = Time.frameCount;
        e.CullPairsSource = why;
        e.CullPairCollectMs += (Time.realtimeSinceStartup - started) * 1000.0;
    }

    /// <summary>Append every <see cref="TMPro.TMP_SubMeshUI"/> DIRECT CHILD of one text component to
    /// the pair cache. TMP parents its sub-meshes as direct children
    /// (<c>TMP_SubMeshUI.AddSubTextObject</c> does <c>SetParent(textComponent.transform)</c>), so this
    /// is one <c>childCount</c> loop and one <c>GetComponent</c> per child.</summary>
    private static void CollectCullPairs(Entry e, TMP_Text t)
    {
        Transform parent = t.transform;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            if (e.CullPairs.Count >= MaxCullPairs)
            {
                e.CullPairsTruncated = true;
                return;
            }
            Transform c = parent.GetChild(i);
            if (c == null)
                continue;
            var sub = c.GetComponent<TMP_SubMeshUI>();
            if (sub != null)
                e.CullPairs.Add(new CullPair(t, sub));
        }
    }

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

        NoteRendererState(e, t.canvasRenderer, t.gameObject.name);

        if (bad > e.WorstTextBad)
        {
            e.WorstTextBad = bad;
            e.WorstTextChecked = checkedHere;
            e.WorstText = Describe(t.gameObject.name, s);
        }
        if (bad == 0)
            e.TextClean++;

        if (!WantsRegeneration(e, repairAll, bad, e.TextComponents))
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
    private static void NoteRendererState(Entry e, CanvasRenderer? cr, string name)
    {
        if (cr == null)
            return;
        // THREE DISJOINT BUCKETS (ModBuild 204) — the identical `||` this site used to carry. See
        // ScanTmpSubMeshes for the argument; it applies word for word here, and the three counts sum
        // to the old TextCulled so nothing that was measured before this build becomes unreadable.
        string reason;
        if (cr.cull)
        {
            e.TextCulledFlag++;
            reason = "CULL FLAG";
        }
        else if (cr.GetAlpha() <= 0.004f)
        {
            e.TextCulledOwnAlpha++;
            reason = "own alpha 0";
        }
        else if (cr.GetInheritedAlpha() <= 0.004f)
        {
            e.TextCulledInheritedAlpha++;
            reason = "inherited alpha 0";
        }
        else
        {
            return;
        }
        e.TextCulled++;
        if (e.TextCulledNote.Length == 0)
            e.TextCulledNote = "'" + name + "' (" + reason + ")";
    }

    /// <summary>
    /// <b>MAY THIS COMPONENT BE RE-GENERATED BY THIS PASS? — THE ModBuild 204 RESUME CURSOR.</b>
    ///
    /// <para><b>WHAT WAS WRONG.</b> <c>ReleaseRepair</c> calls <c>MeasureContent(e, repairAll: true)</c>
    /// precisely so the regeneration is UNCONDITIONAL — that was ModBuild 197's correction of ModBuild
    /// 196's "a remedy gated on its own diagnostic" defect, and the release line still advertises it:
    /// <i>"this pass is unconditional, so if the broken image survives it the text-source family is
    /// falsified rather than untested"</i>. But the same <c>if</c> also short-circuited on
    /// <c>RegeneratedComponents &gt;= MaxRegeneratePerScan</c> (256), and this window carries up to
    /// <b>297</b> text components — the ModBuild 203 log reads 218 on the character sheet, the CARDS
    /// sub-view measured 270 and the ModBuild 202 log 239, so the window is one tab press from the cap.
    /// The walk is a DETERMINISTIC depth-first pre-order, so when the cap bit <b>the same components
    /// were skipped at every release, in every session, forever</b> — and any conclusion drawn from
    /// "the repair ran and the image is still broken" was unsound for exactly those components. That is
    /// the fourth instance of this project's signature failure: a remedy that looks unconditional and
    /// is not.</para>
    ///
    /// <para><b>WHY A CURSOR AND NOT AN UNBOUNDED PASS.</b> Lifting the cap was the other option and it
    /// was priced before it was rejected: the ModBuild 203 log measures ~16.5 ms for 218 components,
    /// i.e. ~76 µs each, so 297 components is ~22.5 ms — on a release frame that already runs 23-35 ms
    /// against an 11.11 ms budget, and a dropped frame during a release is exactly the judder this
    /// class has spent three rounds trying not to add. The cursor keeps the per-pass cost EXACTLY where
    /// it is today and buys full coverage instead: each <c>repairAll</c> pass starts where the last one
    /// stopped, and one release already runs TWO passes (<see cref="ReleaseSettleFrames"/> and
    /// <see cref="ReleaseSecondRepairFrames"/>), so 2 x 256 = 512 covers a 297-component window
    /// completely in a single release.</para>
    ///
    /// <para><b>THE RULES.</b> A component that measured BAD is always regenerated, cursor or not, up
    /// to the cap — the cursor may never delay a repair the scan actually asked for. A component that
    /// measured clean is regenerated only when <paramref name="repairAll"/> and its walk index is at or
    /// past the cursor. When the cap bites the pass records where to resume and marks itself TRUNCATED;
    /// when a pass reaches the end of the walk under the cap, the cursor wraps to 0 and one full sweep
    /// is complete. Every one of those numbers is printed (<see cref="ReportSubMeshCull"/>'s
    /// REGENERATION field), so a truncated pass can never again be reported in language implying
    /// completeness.</para>
    /// </summary>
    private static bool WantsRegeneration(Entry e, bool repairAll, int bad, int index)
    {
        if (!repairAll && bad == 0)
            return false;
        if (e.RegeneratedComponents >= MaxRegeneratePerScan)
        {
            e.ContentScanTruncated = true;
            e.RegenCapBit = true;
            e.RegenDeferred++;
            return false;
        }
        if (repairAll && bad == 0 && index < e.RegenCursor)
        {
            // Covered by an earlier pass of the SAME round-robin sweep. Counted, never silent.
            e.RegenSkippedBeforeCursor++;
            return false;
        }
        e.RegenLastIndex = index;
        return true;
    }

    /// <summary>Close out one <paramref name="repairAll"/> pass: either park the cursor where the cap
    /// stopped it, or wrap it and record that a full sweep of the subtree has completed. See
    /// <see cref="WantsRegeneration"/>.</summary>
    private static void CloseRegenerationPass(Entry e, bool repairAll)
    {
        if (!repairAll)
            return;
        e.RegenPassesThisSweep++;
        if (e.RegenCapBit)
        {
            e.RegenCursor = e.RegenLastIndex + 1;
            return;
        }
        e.RegenCursor = 0;
        e.RegenFullSweeps++;
        e.RegenPassesLastSweep = e.RegenPassesThisSweep;
        e.RegenPassesThisSweep = 0;
    }

    /// <summary>A component name plus the first few characters of its string, for the report. Bounded
    /// so one pathological label cannot make the state line unreadable.</summary>
    private static string Describe(string name, string text)
    {
        string trimmed = text.Length <= 28 ? text : text.Substring(0, 28) + "...";
        return name + " (\"" + trimmed.Replace('\n', ' ') + "\")";
    }

    // ---- THE SUB-VIEW SWEEP BURST (ModBuild 203) -------------------------------------------------

    /// <summary>
    /// <b>DID THE OPEN SUB-VIEW CHANGE SINCE THE LAST FRAME?</b> Called at the TOP of the per-frame
    /// service, before <c>SyncGeometry</c>, so that arming a burst can also force this same frame's
    /// capture-frame re-measure (by pulling <see cref="Entry.NextContentFrame"/> to now — the one
    /// existing seam that means "re-measure", used rather than a second MeasureFrame call).
    ///
    /// <para>Everything about WHY is on <see cref="Entry.SubViewChanges"/>. Never throws; a throw
    /// leaves the signature unchanged, which costs one missed burst and nothing else.</para>
    /// </summary>
    private static void NoticeSubViewChange(Entry e)
    {
        int sig;
        try
        {
            sig = ActiveSetSignature(e.Panel);
        }
        catch (System.Exception)
        {
            return;
        }
        if (!e.SubViewSigValid)
        {
            e.SubViewSigValid = true;
            e.SubViewSignature = sig;
            return;
        }
        if (sig == e.SubViewSignature)
            return;

        e.SubViewSignature = sig;
        e.SubViewChanges++;
        // FORCE THE CAPTURE-FRAME RE-MEASURE ON THIS FRAME, whether or not a burst is armed below. A
        // newly opened sub-view is exactly the case in which the window's drawn content changes
        // without its host rect moving, so nothing else in this class would notice: SyncGeometry's own
        // trigger reads the host transform and the host RectTransform, and a tab press moves neither.
        // ModBuild 204: AND IT FORCES IT PAST THE TRANSLATION GATE. A tab can be pressed while the
        // other hand carries the window, and that is precisely the case in which the gate would
        // otherwise skip the one measurement that genuinely has new information in it. See
        // SkipMeasureWhileTranslating — this flag is the only way through it.
        e.NextContentFrame = Time.frameCount;
        e.ForceMeasure = true;

        // THE COST FUSE — see SweepBurstCooldownFrames. A change inside the cooldown is the SAME
        // repopulation still settling; it extends a burst that is still running and is counted, but
        // it may not arm a second one. Without this a flapping signature would re-arm every frame and
        // this remedy would quietly become the per-frame sweep that was measured and removed twice.
        if (Time.frameCount - e.SubViewChangeFrame < SweepBurstCooldownFrames)
        {
            e.SubViewChangesCoalesced++;
            if (e.SweepBurstFramesLeft > 0)
                e.SweepBurstFramesLeft = Mathf.Max(e.SweepBurstFramesLeft, 1);
            return;
        }

        // AND RE-COLLECT THE SUB-MESH CULL PAIR CACHE (ModBuild 204). A new sub-view brings new text
        // components and therefore new TMP sub-meshes, and a sub-mesh born after the last content scan
        // is invisible to the per-frame invariant until the next one — which is up to ten seconds
        // away, and is exactly the window in which the user reports a freshly opened view coming up
        // broken. Placed AFTER the cooldown check on purpose, so the same cost fuse that bounds the
        // 1.71 ms layer sweep also bounds this ~1 ms walk.
        RefreshCullPairs(e, "a sub-view change");

        e.SweepBursts++;
        e.SubViewChangeFrame = Time.frameCount;
        e.SweepBurstFramesLeft = MinSweepBurstFrames;
        e.SweepBurstFramesRun = 0;
        e.SweepBurstMisses = 0;
        e.SweepBurstMoved = 0;
        e.SweepBurstLastHitFrame = -1;
    }

    /// <summary>
    /// A CHEAP, STABLE SIGNATURE OVER WHAT IS OPEN. Two independent parts, deliberately combined so
    /// that neither has to be right on its own:
    /// <list type="number">
    /// <item>the ACTIVE DIRECT CHILDREN of the conversion target, by instance id in sibling order —
    /// which works on any converted window, including ones that have no game sub-views at all;</item>
    /// <item>the game's own <c>NewPartyDisplayUI</c> answer: the <c>ActiveDisplay</c> enum plus which
    /// of the six sub-view roots are active. This catches a switch whose roots are NOT direct children
    /// of the target, which part (1) alone would miss.</item>
    /// </list>
    /// <para>Cost per frame: one <c>childCount</c> loop over a window's direct children (order ten)
    /// plus seven property reads on a singleton. That is why this runs every frame while the SWEEP it
    /// arms — at a measured 1.71 ms — does not.</para>
    /// <para>It is a LOCAL DERIVATION and does not read <c>CanvasConversion</c>'s <c>fx</c> state:
    /// that is another lane's file and exposes no accessor for it.</para>
    /// </summary>
    private static int ActiveSetSignature(ConvertedPanel panel)
    {
        int sig = 17;
        Transform? target = panel.Target;
        if (target != null)
        {
            int n = target.childCount;
            sig = sig * 31 + n;
            for (int i = 0; i < n; i++)
            {
                Transform c = target.GetChild(i);
                if (c != null && c.gameObject.activeSelf)
                    sig = sig * 31 + c.GetInstanceID();
            }
        }

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            return sig;
        }
        if (display == null || target == null)
            return sig;
        try
        {
            sig = sig * 31 + (int)display.ActiveDisplay;
            sig = MixSubView(sig, display.CharacterSelector, target);
            sig = MixSubView(sig, display.PerkManager, target);
            sig = MixSubView(sig, display.AbilityCardsDisplay, target);
            sig = MixSubView(sig, display.EnhancementCardsDisplay, target);
            sig = MixSubView(sig, display.ItemInventoryDisplay, target);
            sig = MixSubView(sig, display.BattleGoalWindow, target);
        }
        catch (System.Exception)
        {
            // A partial mix is still STABLE (it throws in the same place every frame), so it stays a
            // usable signature rather than a source of phantom changes.
        }
        return sig;
    }

    private static int MixSubView(int sig, Component? view, Transform target)
    {
        if (view == null)
            return sig * 31;
        Transform t = view.transform;
        bool open = t.gameObject.activeInHierarchy && IsUnder(t, target);
        return sig * 31 + (open ? t.GetInstanceID() : 0);
    }

    /// <summary>
    /// <b>THE BURST — sweep the capture layer on every frame while the newly opened view is still
    /// arriving, then get out of the way.</b>
    ///
    /// <para>Runs AFTER the visibility sync and BEFORE the ordinary cadence check, and resets that
    /// cadence as it goes so a burst frame is never followed by a redundant periodic sweep on the
    /// same frame. It backs off in two ways: it always runs <see cref="MinSweepBurstFrames"/> frames
    /// (the content does not exist yet on frame 0), then extends only while sweeps keep finding late
    /// joiners, and stops after <see cref="SweepBurstMissTolerance"/> consecutive empty sweeps or at
    /// <see cref="MaxSweepBurstFrames"/> whichever comes first.</para>
    ///
    /// <para>At the end it runs ONE content scan, so the CONTENT INTEGRITY, OVER-PAINT and attribution
    /// fields describe the view that was just opened rather than the one that was closed — the whole
    /// reason eight rounds of clean readings could not be assigned to anything.</para>
    ///
    /// <para>Never throws: the caller's LateTick catch would stand the entire supersample path down.</para>
    /// </summary>
    private static void ServiceSweepBurst(Entry e)
    {
        if (e.SweepBurstFramesLeft <= 0)
            return;
        // THE HARD WALL-CLOCK GATE, belt and braces beside the per-burst frame cap: a burst may only
        // ever sweep inside the MaxSweepBurstFrames frames that follow its arming, whatever a coalesced
        // change did to its counters. This is what makes the worst case arithmetic on
        // SweepBurstCooldownFrames a bound and not an intention.
        if (Time.frameCount - e.SubViewChangeFrame >= MaxSweepBurstFrames)
        {
            e.SweepBurstFramesLeft = 0;
            FinishSweepBurst(e);
            return;
        }
        float started = Time.realtimeSinceStartup;
        e.SweepBurstFramesLeft--;
        e.SweepBurstFramesRun++;
        int before = e.LateJoiners;
        ApplyCaptureLayer(e, initial: false);
        // The periodic cadence is re-armed from HERE, so the burst replaces it rather than doubling it.
        e.NextSweepFrame = Time.frameCount + SweepIntervalFrames;
        int moved = e.LateJoiners - before;
        e.SweepBurstMoved += moved;
        if (moved > 0)
        {
            e.SweepBurstLastHitFrame = Time.frameCount;
            e.SweepBurstMisses = 0;
            // STILL ARRIVING: extend, up to the hard cap. This is the only thing that can make a burst
            // longer than MinSweepBurstFrames, so a burst that reaches the cap is a window that was
            // still repopulating for a quarter of a second — which is a finding about the GAME's
            // cadence and not about ours, and the report says so.
            if (e.SweepBurstFramesRun + e.SweepBurstFramesLeft < MaxSweepBurstFrames)
                e.SweepBurstFramesLeft++;
        }
        else
        {
            e.SweepBurstMisses++;
            if (e.SweepBurstFramesRun >= MinSweepBurstFrames
                && e.SweepBurstMisses >= SweepBurstMissTolerance)
            {
                e.SweepBurstFramesLeft = 0;
            }
        }
        e.SweepBurstMs += (Time.realtimeSinceStartup - started) * 1000.0;
        if (e.SweepBurstFramesLeft <= 0)
            FinishSweepBurst(e);
    }

    /// <summary>Close a burst: commit its distribution, re-scan the content so every field on the
    /// state line describes the view that was just opened, and log the one line that prices it.</summary>
    private static void FinishSweepBurst(Entry e)
    {
        int hole = e.SweepBurstLastHitFrame >= 0
            ? e.SweepBurstLastHitFrame - e.SubViewChangeFrame
            : 0;
        e.SweepBurstHoleFramesLast = hole;
        if (hole > e.SweepBurstHoleFramesMax)
            e.SweepBurstHoleFramesMax = hole;
        e.SweepBurstHoleSum += hole;
        e.SweepBurstHoleReadings++;
        e.SweepBurstFramesLast = e.SweepBurstFramesRun;
        e.SweepBurstMovedLast = e.SweepBurstMoved;
        if (e.SweepBurstMoved == 0)
            e.SweepBurstsConverged++;

        // ONE content scan per burst, not per frame: this is what re-attributes every field on the
        // state line to the view that was just opened.
        MeasureContent(e);

        VRLog.Info(Scope, $"PANEL SUPERSAMPLE SUB-VIEW BURST '{e.Window}': TRIGGER = the set of "
            + "ACTIVE sub-view roots inside this window changed (a tab press; no drag, no resize, no "
            + $"host-rect change — which is why nothing before ModBuild 203 forced anything). NOW "
            + $"OPEN: {(e.OpenViewName.Length > 0 ? "'" + e.OpenViewName + "'" : "none")}, "
            + $"ActiveDisplay={(e.OpenViewActive.Length > 0 ? e.OpenViewActive : "unavailable")}, "
            + $"{e.OpenViewCount} root(s). THE BURST ran {e.SweepBurstFramesRun} frame(s) (floor "
            + $"{MinSweepBurstFrames}, cap {MaxSweepBurstFrames}, extended only while sweeps kept "
            + $"finding arrivals, ended after {e.SweepBurstMisses} consecutive empty sweep(s) against "
            + $"a tolerance of {SweepBurstMissTolerance}) and moved {e.SweepBurstMoved} transform(s) "
            + $"onto capture layer {e.Layer} in total ({e.SubViewChanges} change(s) noticed since "
            + $"engage, {e.SubViewChangesCoalesced} of them COALESCED into a running burst by the "
            + $"{SweepBurstCooldownFrames}-frame cost fuse rather than arming a second one), at "
            + $"{(e.SweepBursts > 0 ? e.SweepBurstMs / e.SweepBursts : 0.0):F2} ms per burst so far "
            + $"across {e.SweepBursts} burst(s) (a single sweep measured 1.71 ms on this window "
            + $"against an {FrameBudgetMs:F2} ms frame budget, so a burst is ~15 % of one frame for "
            + "the frames it runs and NOTHING for every other frame — it is armed by a content "
            + "change and disarms itself, which is what makes it different from the ModBuild 193 "
            + "per-frame-while-moving sweep that was measured, shipped and falsified). THE NUMBER "
            + "NOBODY HAS MEASURED BEFORE: "
            + $"{hole} frame(s) elapsed between the sub-view change and the LAST sweep that still "
            + "found a late joiner — that is the length of the window in which this view's content "
            + "was MISSING FROM THE CAPTURE and drawn straight into the eye at its own sorting order. "
            + $"WORST {e.SweepBurstHoleFramesMax}, MEAN "
            + $"{(e.SweepBurstHoleReadings > 0 ? (double)e.SweepBurstHoleSum / e.SweepBurstHoleReadings : 0.0):F1}, "
            + $"over {e.SweepBurstHoleReadings} burst(s), of which {e.SweepBurstsConverged} found "
            + "NOTHING AT ALL. HOW TO READ IT: 0 frames (or a burst that moved 0) means there was no "
            + "hole to close on this switch and the 'initial kaputt beim Öffnen' report is NOT a "
            + "late-joiner problem for this view — look at the OVER-PAINT CENSUS on the state line "
            + "instead. A SMALL number (1-3 frames) means the hole existed and this burst closed it, "
            + "and the user should see the difference on the very next tab press. A number that keeps "
            + "reaching the cap means the game is STILL repopulating after "
            + $"{MaxSweepBurstFrames} frames, i.e. the hole is the GAME's cadence and not ours, and "
            + "the fix would have to be an arrival HOOK rather than any poll — which is the step "
            + "ModBuild 193's own log line has been asking for since it was written. NOTE ON THE "
            + "NEIGHBOURING LINE: each sweep of this burst that moves something also prints a "
            + "'pooled/late transform(s) ... joined capture layer' line, and that line will say it "
            + "ran because the window 'reached its periodic cadence' — it is in another file and "
            + "cannot see this trigger. THIS line is the authority on why those sweeps ran.");
    }

    // ---- THE OVER-PAINT CENSUS (ModBuild 203) ----------------------------------------------------

    /// <summary>Clear the census counters for a fresh scan. The DISTRIBUTION fields
    /// (<see cref="Entry.OverPaintReadings"/> and friends) are deliberately NOT cleared: they are the
    /// whole point of the "never a bare extreme" rule and they accumulate across the session.</summary>
    private static void ResetOverPaint(Entry e)
    {
        DrawRecords.Clear();
        PlateRecords.Clear();
        CoveredTop.Clear();
        OverPaintSb.Length = 0;
        ForeignSb.Length = 0;
        e.OverPaintGraphics = 0;
        e.OverPaintVisited = 0;
        e.OverPaintTruncated = false;
        e.OverPaintPlates = 0;
        e.OverPaintOpaquePlates = 0;
        e.OverPaintCovered = 0;
        e.OverPaintWorstCovered = 0;
        e.OverPaintCoveredArea = 0f;
        e.OverPaintTied = 0;
        e.OverPaintNote = string.Empty;
        e.ForeignRenderers = 0;
        e.ForeignCoplanar = 0;
        e.ForeignNote = string.Empty;
    }

    /// <summary>
    /// <b>WHICH SUB-VIEW IS OPEN — the attribution eight rounds of clean measurements did not carry.</b>
    ///
    /// <para>Two of the party window's six sub-views render broken and four do not, and every field
    /// on this class's state line so far averaged over whichever happened to be open when the report
    /// cadence fired. A reading of "0 defects" is unreadable without knowing which view it was taken
    /// of, which is exactly how twenty-two rounds of correct measurements produced no decision.</para>
    ///
    /// <para><b>THIS IS A LOCAL DERIVATION, ON PURPOSE.</b> <c>CanvasConversion.3.Fit.cs</c> already
    /// computes an open-set signature (<c>fx.OpenSignature</c>) over the same six sub-views, but that
    /// state is private to another lane's file and NO read-only accessor for it exists — so nothing
    /// here reads it and nothing here edits that file. Instead this asks the game's own singleton the
    /// same question the fit asks it: <c>NewPartyDisplayUI.PartyDisplay</c> for the six sub-view roots
    /// and <c>ActiveDisplay</c> for the tab the game itself considers open. The two derivations can in
    /// principle disagree; if they ever do, the log prints the roots this one found and the count, so
    /// the disagreement is visible rather than silent.</para>
    ///
    /// <para>Windows that are not the party display have no such sub-views: the reference rect is then
    /// the HOST RECT and the source string says so, so "no sub-view" and "sub-view not measured" can
    /// never print alike. Never throws — every game-side access is guarded, and a throw falls back to
    /// the host rect.</para>
    /// </summary>
    private static void ResolveOpenSubView(Entry e, ConvertedPanel panel, RectTransform host)
    {
        e.OpenViewRect = host.rect;
        e.OpenViewName = string.Empty;
        e.OpenViewActive = string.Empty;
        e.OpenViewCount = 0;
        e.OpenViewSource = "the HOST RECT (this window has no NewPartyDisplayUI sub-views)";

        NewPartyDisplayUI? display;
        try
        {
            display = NewPartyDisplayUI.PartyDisplay;
        }
        catch (System.Exception)
        {
            e.OpenViewSource = "the HOST RECT (NewPartyDisplayUI.PartyDisplay threw)";
            return;
        }
        if (display == null || panel.Target == null)
            return;

        string active;
        try
        {
            active = display.ActiveDisplay.ToString();
        }
        catch (System.Exception)
        {
            active = "?";
        }
        e.OpenViewActive = active;

        float bestArea = 0f;
        int found = 0;
        try
        {
            found += ConsiderSubView(e, panel, host, display.CharacterSelector, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.PerkManager, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.AbilityCardsDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.EnhancementCardsDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.ItemInventoryDisplay, ref bestArea);
            found += ConsiderSubView(e, panel, host, display.BattleGoalWindow, ref bestArea);
        }
        catch (System.Exception)
        {
            // A partial sweep is still attributable — keep whatever was resolved and say the ask threw.
            e.OpenViewCount = found;
            e.OpenViewSource = $"NewPartyDisplayUI.ActiveDisplay={active}, but reading the sub-view "
                               + "roots THREW part way, so the rect may be the host rect";
            return;
        }

        e.OpenViewCount = found;
        e.OpenViewSource = found > 0
            ? $"NewPartyDisplayUI.ActiveDisplay={active}, {found} sub-view root(s) open inside this "
              + "window, the LARGEST of them measured as the reference rect"
            : $"NewPartyDisplayUI.ActiveDisplay={active} but NO sub-view root is active inside this "
              + "window, so the reference is the HOST RECT";
    }

    /// <summary>One candidate sub-view root: it counts when it is active, lives inside the window we
    /// converted, and measures. The LARGEST by host-local area becomes the reference rect — a plate
    /// is judged against the view it would hide, not against the whole host.</summary>
    private static int ConsiderSubView(Entry e, ConvertedPanel panel, RectTransform host,
                                       Component? view, ref float bestArea)
    {
        if (view == null)
            return 0;
        Transform t = view.transform;
        if (!t.gameObject.activeInHierarchy || !IsUnder(t, panel.Target))
            return 0;
        if (t is not RectTransform rt || !TryHostLocalBounds(host, rt, out Rect bounds))
            return 1; // open, but unmeasurable: still counted, so the count and the rect can disagree
        float area = Mathf.Abs(bounds.width * bounds.height);
        if (area > bestArea)
        {
            bestArea = area;
            e.OpenViewRect = bounds;
            e.OpenViewName = t.name;
        }
        return 1;
    }

    /// <summary>Is <paramref name="t"/> at or below <paramref name="root"/>? Bounded climb; no
    /// allocation, and it answers IDENTITY-or-descendant rather than "has a component of that type
    /// somewhere above", which is a slip this project has already shipped twice.</summary>
    private static bool IsUnder(Transform t, Transform? root)
    {
        if (root == null)
            return false;
        for (Transform? p = t; p != null; p = p.parent)
        {
            if (ReferenceEquals(p, root))
                return true;
        }
        return false;
    }

    /// <summary>The mod's own art inside a converted window (cue rings, grab handles) is not the
    /// game's layering and must not appear in a census about the game's layering.</summary>
    private static bool IsModOwned(string name) =>
        name.StartsWith("GloomhavenVR.", System.StringComparison.Ordinal);

    /// <summary>
    /// NAME a foreign render subtree instead of only counting it — <see cref="Entry.ForeignRenderers"/>
    /// for the argument. The HOST-LOCAL z is the load-bearing number: the display quad sits at
    /// host-local z = 0, so a foreign renderer at z ~ 0 is COPLANAR with it and its depth tie can
    /// resolve differently in the two MultiPass eyes.
    /// </summary>
    private static void NoteForeignSubtree(Entry e, RectTransform host, Transform t,
                                           Renderer? renderer, Camera? camera)
    {
        e.ForeignRenderers++;
        float z = host.InverseTransformPoint(t.position).z;
        if (Mathf.Abs(z) <= ForeignCoplanarEpsPx)
            e.ForeignCoplanar++;
        if (e.ForeignRenderers > MaxForeignNamed)
            return;
        bool enabled = renderer != null ? renderer.enabled : camera != null && camera.enabled;
        string type = renderer != null ? renderer.GetType().Name
                                       : camera != null ? camera.GetType().Name : "?";
        if (ForeignSb.Length > 0)
            ForeignSb.Append(", ");
        ForeignSb.Append('[').Append(HostPath(t, host.transform)).Append(", ").Append(type)
                 .Append(enabled ? ", ENABLED" : ", disabled")
                 .Append(", host-local z ").Append(z.ToString("F2")).Append(" px")
                 .Append(Mathf.Abs(z) <= ForeignCoplanarEpsPx
                     ? " — COPLANAR with the display quad" : string.Empty)
                 .Append(']');
    }

    /// <summary>Full hierarchy path from the panel host down to <paramref name="t"/>, bounded so one
    /// deep subtree cannot make the state line unreadable.</summary>
    private static string HostPath(Transform? t, Transform root)
    {
        if (t == null)
            return "(destroyed)";
        PathSb.Length = 0;
        int guard = 0;
        for (Transform? p = t; p != null && !ReferenceEquals(p, root) && guard < 24; p = p.parent, guard++)
        {
            if (PathSb.Length > 0)
                PathSb.Insert(0, '/');
            PathSb.Insert(0, p.name);
        }
        if (PathSb.Length == 0)
            PathSb.Append(t.name);
        return PathSb.ToString();
    }

    private static readonly StringBuilder PathSb = new(160);

    /// <summary>Record one drawing graphic for the census. The rect is the graphic's host-local
    /// axis-aligned bounds INTERSECTED with the mask clip in force, i.e. what it can actually put
    /// pixels on — the same rule <see cref="MeasureFrame"/> uses, for the same reason.</summary>
    private static void RecordDrawn(Entry e, RectTransform host, RectTransform rt, Graphic graphic,
                                    Canvas? canvas, Rect clip, int order, int index, float refArea)
    {
        if (DrawRecords.Count >= MaxOverPaintGraphics)
        {
            e.OverPaintTruncated = true;
            return;
        }
        if (!TryHostLocalBounds(host, rt, out Rect bounds))
            return;
        if (!Intersect(clip, bounds, out Rect visible))
            return;
        float area = Mathf.Abs(visible.width * visible.height);
        if (area <= 0f)
            return;
        // COVER IS MEASURED AGAINST THE OPEN SUB-VIEW, NOT THE HOST. A plate that fills a sub-view is
        // what hides a sub-view's content; the host rect of this window is 1920x1080 and a sub-view
        // backdrop at 1620x1080 would read as 84 % of it either way — but on a window where the host
        // is much larger than the open view, judging against the host would miss the plate entirely.
        float cover = 0f;
        if (refArea > 1f && Intersect(e.OpenViewRect, visible, out Rect onView))
            cover = Mathf.Abs(onView.width * onView.height) / refArea;
        bool plate = cover >= OverPaintPlateFraction;
        if (plate)
            PlateRecords.Add(DrawRecords.Count);
        DrawRecords.Add(new DrawRecord(graphic, rt, canvas, visible, area, cover, order, index, plate));
        e.OverPaintGraphics++;
    }

    /// <summary>
    /// <b>THE ANSWER FIELD: for every full-frame plate, HOW MANY DRAWING GRAPHICS IT IS PAINTED OVER.</b>
    ///
    /// <para>A graphic is painted over by a plate when it INTERSECTS the plate's rect, draws BEFORE it
    /// in resolved painter's order, and is not itself a plate (two stacked backdrops are a backdrop,
    /// not an occlusion). "Before" is the pair (resolved <c>sortingOrder</c>, depth-first hierarchy
    /// index) that <see cref="OrderFrame"/> carried down the walk.</para>
    ///
    /// <para><b>AND IT REPORTS THE TIE SEPARATELY.</b> Two canvases that both set
    /// <c>overrideSorting</c> to the SAME <c>sortingOrder</c> have NO defined order between them —
    /// Unity resolves them by canvas registration, not by hierarchy — so for those pairs the
    /// hierarchy index below is a plausible resolution and not a prediction. Every such pair is
    /// counted into <see cref="Entry.OverPaintTied"/> and named as an UNSTABLE TIE, which is how this
    /// instrument proves or refutes the parallel lane's draw-order defect without depending on it.</para>
    ///
    /// <para>Cost: <c>plates x records</c> rectangle compares, with at most
    /// <see cref="MaxOverPaintGraphics"/> records and typically fewer than a handful of plates. No
    /// traversal, no allocation beyond the report string.</para>
    /// </summary>
    private static void CommitOverPaint(Entry e)
    {
        e.OverPaintPlates = PlateRecords.Count;
        for (int p = 0; p < PlateRecords.Count; p++)
        {
            int pi = PlateRecords[p];
            DrawRecord plate = DrawRecords[pi];
            float ownAlpha = plate.Graphic != null ? plate.Graphic.color.a : 0f;
            CanvasRenderer? cr = plate.Graphic != null ? plate.Graphic.canvasRenderer : null;
            float crAlpha = cr != null ? cr.GetAlpha() : 1f;
            float inherited = cr != null ? cr.GetInheritedAlpha() : 1f;
            bool opaque = ownAlpha * crAlpha * inherited >= 0.99f;
            if (opaque)
                e.OverPaintOpaquePlates++;

            int covered = 0;
            int tied = 0;
            float coveredArea = 0f;
            CoveredTop.Clear();
            for (int i = 0; i < DrawRecords.Count; i++)
            {
                if (i == pi)
                    continue;
                DrawRecord r = DrawRecords[i];
                if (r.Plate)
                    continue;
                if (r.Order > plate.Order || (r.Order == plate.Order && r.Index > plate.Index))
                    continue; // drawn AFTER the plate: the plate cannot hide it
                if (!Intersect(plate.Rect, r.Rect, out Rect hit))
                    continue;
                float a = Mathf.Abs(hit.width * hit.height);
                covered++;
                coveredArea += a;
                if (r.Order == plate.Order && !ReferenceEquals(r.Canvas, plate.Canvas))
                    tied++;
                InsertCovered(a, r.Transform != null ? r.Transform.name : "?");
            }
            e.OverPaintCovered += covered;
            e.OverPaintCoveredArea += coveredArea;
            e.OverPaintTied += tied;
            if (covered > e.OverPaintWorstCovered)
                e.OverPaintWorstCovered = covered;

            if (p >= MaxPlatesReported)
                continue;
            if (OverPaintSb.Length > 0)
                OverPaintSb.Append(' ');
            OverPaintSb.Append('[').Append('#').Append(p + 1).Append(" '")
                .Append(plate.Transform != null ? plate.Transform.name : "?").Append("' at ")
                .Append(HostPath(plate.Transform, e.Panel.HostGo.transform)).Append(": rect ")
                .Append(plate.Rect.width.ToString("F0")).Append('x')
                .Append(plate.Rect.height.ToString("F0")).Append(" px = ")
                .Append((plate.Cover * 100f).ToString("F0"))
                .Append(" % of the open sub-view's area; COLOUR RGBA ")
                .Append(plate.Graphic != null ? plate.Graphic.color.r.ToString("F3") : "?").Append('/')
                .Append(plate.Graphic != null ? plate.Graphic.color.g.ToString("F3") : "?").Append('/')
                .Append(plate.Graphic != null ? plate.Graphic.color.b.ToString("F3") : "?").Append('/')
                .Append(ownAlpha.ToString("F3")).Append(" x crAlpha ").Append(crAlpha.ToString("F3"))
                .Append(" x inherited ").Append(inherited.ToString("F3"))
                .Append(opaque ? " = OPAQUE (it DELETES what it covers)"
                               : " = translucent (it TINTS what it covers)")
                .Append("; ").Append(PlateMaterialNote(plate.Graphic))
                .Append("; canvas '")
                .Append(plate.Canvas != null ? plate.Canvas.name : "none").Append("' sortingOrder ")
                .Append(plate.Canvas != null ? plate.Canvas.sortingOrder : 0)
                .Append(" overrideSorting ")
                .Append(plate.Canvas != null && plate.Canvas.overrideSorting ? "TRUE" : "false")
                .Append(", RESOLVED ORDER KEY (").Append(plate.Order).Append(", hierarchy index ")
                .Append(plate.Index).Append("); PAINTS OVER ").Append(covered).Append(" of ")
                .Append(e.OverPaintGraphics).Append(" drawing graphic(s)");
            if (covered == 0)
            {
                OverPaintSb.Append(" — NOTHING is drawn under it inside its own rect, i.e. it is a "
                                   + "LEGITIMATE BACKDROP and over-paint cannot be this view's fault");
            }
            else
            {
                OverPaintSb.Append(", ").Append(coveredArea.ToString("F0"))
                    .Append(" px² of intersecting area IN TOTAL (a SUM over the ").Append(covered)
                    .Append(", not a union — overlapping victims are counted once each), of which ")
                    .Append(tied)
                    .Append(" sit at the SAME resolved sortingOrder under a DIFFERENT canvas = an "
                            + "UNSTABLE TIE whose real GPU order this census cannot predict. THE ")
                    .Append(CoveredTop.Count).Append(" LARGEST (contributors, NOT the extent): ");
                for (int k = 0; k < CoveredTop.Count; k++)
                {
                    if (k > 0)
                        OverPaintSb.Append(", ");
                    OverPaintSb.Append('\'').Append(CoveredTop[k].Name).Append("' ")
                        .Append(CoveredTop[k].Area.ToString("F0")).Append(" px²");
                }
            }
            OverPaintSb.Append(']');
        }

        e.OverPaintNote = OverPaintSb.ToString();
        e.ForeignNote = ForeignSb.ToString();
        OverPaintSb.Length = 0;
        ForeignSb.Length = 0;
        DrawRecords.Clear();
        PlateRecords.Clear();
        CoveredTop.Clear();

        e.OverPaintScans++;
        e.OverPaintReadings++;
        e.OverPaintCoveredSum += e.OverPaintCovered;
        if (e.OverPaintReadings == 1)
        {
            e.OverPaintCoveredLowest = e.OverPaintCovered;
            e.OverPaintCoveredHighest = e.OverPaintCovered;
        }
        else
        {
            if (e.OverPaintCovered < e.OverPaintCoveredLowest)
                e.OverPaintCoveredLowest = e.OverPaintCovered;
            if (e.OverPaintCovered > e.OverPaintCoveredHighest)
                e.OverPaintCoveredHighest = e.OverPaintCovered;
        }
    }

    /// <summary>Keep the <see cref="MaxCoveredNamed"/> largest victims of one plate, by intersecting
    /// area. Insertion into a fixed tiny list — no sort, no allocation.</summary>
    private static void InsertCovered(float area, string name)
    {
        for (int i = 0; i < CoveredTop.Count; i++)
        {
            if (area > CoveredTop[i].Area)
            {
                CoveredTop.Insert(i, (area, name));
                if (CoveredTop.Count > MaxCoveredNamed)
                    CoveredTop.RemoveAt(CoveredTop.Count - 1);
                return;
            }
        }
        if (CoveredTop.Count < MaxCoveredNamed)
            CoveredTop.Add((area, name));
    }

    /// <summary>
    /// <b>WHAT THE PLATE'S MATERIAL IS — a specifically requested field, and nobody has ever read this
    /// shader name at runtime.</b>
    ///
    /// <para>The game ships <c>UIBlurDisabler</c> (<c>decompiled/GH.Runtime/UIBlurDisabler.cs:19</c>),
    /// whose entire remedy for these plates is <c>_image.material = null</c> — which PROVES the
    /// plate's appearance IS its material, not its colour and not its sprite. Its other branch is
    /// worth reading with this line in hand: <c>_color = new Color(17f, 17f, 17f, 85f)</c>, i.e. an
    /// UNCLAMPED colour whose alpha of 85 saturates to a fully opaque near-white plate, applied when
    /// <c>SimplifiedUI</c> and <c>DisableUIBlur</c> are both on. So the colour figures on this line
    /// are printed RAW and not clamped: a component above 1 is itself the finding.</para>
    ///
    /// <para><c>_image.material = null</c> makes <see cref="Graphic.material"/> return
    /// <c>defaultGraphicMaterial</c>, so "IS the default UI material" on this line is the same
    /// statement as "the blur disabler already ran (or was never needed) here". Anything else, with a
    /// <c>_GrabTexture</c>, <c>_BackgroundTexture</c> or <c>_CameraOpaqueTexture</c> property, is a
    /// shader that reads WHAT IS BEHIND IT — and a grab-pass source inside a render-to-texture capture
    /// is not the eye's framebuffer, which is a way for a plate to come out flat dark that no glyph,
    /// mesh or sampling instrument can see.</para>
    /// </summary>
    private static string PlateMaterialNote(Graphic? g)
    {
        if (g == null)
            return "material UNREADABLE (the graphic went away between the walk and the report)";
        Material mat;
        try
        {
            mat = g.material;
        }
        catch (System.Exception ex)
        {
            return $"material UNREADABLE ({ex.GetType().Name})";
        }
        if (mat == null)
            return "material NULL (nothing to draw with — uGUI falls back to the default UI material)";
        bool isDefault = ReferenceEquals(mat, Graphic.defaultGraphicMaterial);
        Shader? shader = mat.shader;
        string probes = isDefault
            ? string.Empty
            : ", probes ["
              + (mat.HasProperty(GrabTexId) ? "_GrabTexture YES" : "_GrabTexture no") + ", "
              + (mat.HasProperty(BackgroundTexId) ? "_BackgroundTexture YES" : "_BackgroundTexture no")
              + ", "
              + (mat.HasProperty(CameraOpaqueTexId) ? "_CameraOpaqueTexture YES"
                                                    : "_CameraOpaqueTexture no")
              + ", "
              + (!mat.HasProperty(MainTexId) ? "_MainTex ABSENT"
                  : mat.GetTexture(MainTexId) == null ? "_MainTex NULL" : "_MainTex bound")
              + "]";
        return (isDefault
                   ? "material IS the default UI material (i.e. no blur/grab shader here — the same "
                     + "state UIBlurDisabler produces with _image.material = null)"
                   : "material is NOT the default UI material")
               + ": shader '" + (shader != null ? shader.name : "?") + "', renderQueue "
               + mat.renderQueue + probes;
    }

    // ---- THE TWO ModBuild 204 REPORT LINES ------------------------------------------------------

    /// <summary>
    /// <b>ONE LINE PER PANEL PER REPORT CADENCE: THE SUB-MESH CULL LATCH, THE INVARIANT REPAIR, THE
    /// THREE-WAY SPLITS AND THE REGENERATION CURSOR.</b> Emitted from this file rather than as more
    /// fields on <c>Report</c>'s state line, because each of those needs its own comparison spelled
    /// out and that line is already at the limit of what a reader can hold.
    /// </summary>
    private static void ReportSubMeshCull(Entry e)
    {
        float perFrameMs = e.CullCheckFrames > 0 ? (float)(e.CullCheckMs / e.CullCheckFrames) : 0f;
        float collectMs = e.CullPairCollections > 0
            ? (float)(e.CullPairCollectMs / e.CullPairCollections)
            : 0f;

        FrameSb.Length = 0;
        FrameSb.Append("PANEL SUB-MESH CULL '").Append(e.Window).Append("'").Append(OpenViewTag(e))
               .Append(": THE INVARIANT — parent TextMeshProUGUI cull -> its TMP_SubMeshUI children — "
                       + "is checked EVERY FRAME and gated on nothing. ")
               .Append(e.CullPairs.Count).Append(" (parent, sub-mesh) pair(s) cached")
               .Append(e.CullPairsTruncated
                   ? " [TRUNCATED at " + MaxCullPairs + ": every count below is a LOWER BOUND]"
                   : string.Empty)
               .Append(", collected on frame ").Append(e.CullPairsFrame).Append(" by ")
               .Append(e.CullPairsSource).Append(" (").Append(e.CullPairCollections)
               .Append(" collection(s) at ").Append(collectMs.ToString("F2")).Append(" ms each); ")
               .Append(e.CullChecks).Append(" compare(s) over ").Append(e.CullCheckFrames)
               .Append(" frame(s) at ").Append(perFrameMs.ToString("F3"))
               .Append(" ms per frame against the ").Append(FrameBudgetMs.ToString("F2"))
               .Append(" ms budget. REPAIRED: ").Append(e.CullRepairs)
               .Append(" sub-mesh(es) since engage (").Append(e.CullRepairsUnhid)
               .Append(" UN-HIDDEN — the latch — and ").Append(e.CullRepairsHid)
               .Append(" hidden because their parent was legitimately culled), ")
               .Append(e.CullRepairsThisWindow).Append(" in this 10 s window")
               .Append(e.CullRepairNote.Length > 0 ? "; last: " + e.CullRepairNote : string.Empty)
               .Append(". LATCH CENSUS at the last content scan (taken AFTER that frame's invariant "
                       + "pass, so 0 here means the repair is HOLDING and not that no latch "
                       + "happened — read 'REPAIRED ... UN-HIDDEN' above for that): ")
               .Append(e.SubMeshCullLatched)
               .Append(" sub-mesh(es) read cull=TRUE while their PARENT read cull=FALSE, out of ")
               .Append(e.SubMeshesInUse).Append(" carrying vertices (").Append(e.SubMeshesSeen)
               .Append(" seen, ").Append(e.SubMeshesEmpty).Append(" pooled/empty)")
               .Append(e.SubMeshCullLatchedNote.Length > 0
                   ? " — " + e.SubMeshCullLatchedNote
                   : string.Empty)
               .Append(". WHY A SUB-MESH PUT NO PIXELS INTO THE CAPTURE, in three DISJOINT buckets "
                       + "(they used to be one '||'): ")
               .Append(e.SubMeshesCullFlag).Append(" CULL FLAG, ").Append(e.SubMeshesOwnAlpha)
               .Append(" own alpha 0, ").Append(e.SubMeshesInheritedAlpha)
               .Append(" inherited alpha 0; plus ").Append(e.SubMeshesInactive)
               .Append(" inactive, ").Append(e.SubMeshesWrongLayer)
               .Append(" on the wrong layer (repaired), ").Append(e.SubMeshesNoTexture)
               .Append(" with no bound texture. THE SAME SPLIT FOR WHOLE TEXT COMPONENTS: ")
               .Append(e.TextCulledFlag).Append(" CULL FLAG, ").Append(e.TextCulledOwnAlpha)
               .Append(" own alpha 0, ").Append(e.TextCulledInheritedAlpha)
               .Append(" inherited alpha 0, out of ").Append(e.TextComponents)
               .Append(" component(s)")
               .Append(e.TextCulledNote.Length > 0 ? " — first: " + e.TextCulledNote : string.Empty)
               .Append(". MOVING vs SETTLED (the four readings — LATCHED pairs seen at that instant, "
                       + "each taken BEFORE that frame's write; ").Append(e.CullGrabs)
               .Append(" grab(s) seen): baseline before the grab ")
               .Append(Reading(e.CullBaseline)).Append(", during the drag min ")
               .Append(Reading(e.CullDragMin)).Append(" max ").Append(Reading(e.CullDragMax))
               .Append(" over ").Append(e.CullDragSamples).Append(" sample(s), at the release edge ")
               .Append(Reading(e.CullAtRelease)).Append(", settled +")
               .Append(SubMeshCullSettleFrames).Append(" frames ").Append(Reading(e.CullSettled))
               .Append(". REGENERATION (release passes): ").Append(e.RegeneratedComponents)
               .Append(" of ").Append(e.TextComponents)
               .Append(" component(s) re-generated by the last pass, cap ")
               .Append(MaxRegeneratePerScan).Append(" per pass, cursor now at ").Append(e.RegenCursor)
               .Append(e.RegenCursor == 0
                   ? " (= the start: a full sweep of the subtree has COMPLETED)"
                   : " (the pass was TRUNCATED by the cap and the next one RESUMES there — it did NOT "
                     + "restart at the root)")
               .Append("; ").Append(e.RegenDeferred).Append(" component(s) deferred to the next pass, ")
               .Append(e.RegenSkippedBeforeCursor)
               .Append(" skipped as already covered by an earlier pass of this sweep; ")
               .Append(e.RegenFullSweeps).Append(" full sweep(s) completed, last one in ")
               .Append(e.RegenPassesLastSweep).Append(" pass(es); scan truncated: ")
               .Append(e.ContentScanTruncated ? "YES" : "no")
               .Append(". CONTENT-SCALE FRESHNESS: ").Append(e.ContentScaleUnmeasured)
               .Append(" measurement(s) found no measurable rect and therefore kept the PREVIOUS "
                       + "MinContentScale (deliberate — an unmeasured window and an unscaled one must "
                       + "not print alike), and the last capture-frame measurement ran on frame ")
               .Append(e.LastMeasureFrame).Append(" (now ").Append(Time.frameCount)
               .Append("), so a release that lands on an unmeasured frame is identifiable here.");

        FrameSb.Append(" HOW TO READ IT. (1) THE HEADLINE IS 'REPAIRED ... UN-HIDDEN'. Every "
                       + "un-hidden sub-mesh is a glyph run that WAS in the mesh, WAS in the atlas, "
                       + "WAS on the capture layer and was invisible anyway because a boolean on a "
                       + "child GameObject said so. TMP writes that boolean ONLY from inside "
                       + "TextMeshProUGUI.Cull's `if (m_canvasRenderer.cull != flag)` guard, and "
                       + "uGUI's MaskableGraphic.UpdateClipParent clears the PARENT's flag through a "
                       + "private non-virtual UpdateCull that does not run the loop — after which the "
                       + "guard is permanently shut. TMP_SubMeshUI.Cull is an EMPTY METHOD, so no "
                       + "RectMask2D can ever repair it either. Both quotations are in "
                       + "RepairSubMeshCull's doc, read out of the shipped assemblies. "
                       + "(2) THE 'REPAIRED' COUNT AND THE 'LATCH CENSUS' MEASURE DIFFERENT STAGES "
                       + "AND MUST NOT BE READ AS THE SAME NUMBER. The invariant pass runs EVERY "
                       + "FRAME and the content scan runs after it, so once the repair is working the "
                       + "LATCH CENSUS necessarily reads 0 — that zero means THE REPAIR IS HOLDING, "
                       + "not that the latch never happened. The number that proves the latch is real "
                       + "is 'REPAIRED ... UN-HIDDEN': it can only be non-zero if a sub-mesh was found "
                       + "culled under an un-culled parent, which is a state TMP itself cannot reach "
                       + "and cannot fix. So: UN-HIDDEN non-zero = the diagnosis is confirmed; "
                       + "UN-HIDDEN ZERO across a session in which the user still reports missing "
                       + "elements RETIRES this hypothesis outright — that outcome is stated here so "
                       + "it cannot be read as 'the instrument found nothing yet'. A non-zero LATCH "
                       + "CENSUS on top of a running repair is a THIRD thing: something is "
                       + "re-latching faster than once per frame, i.e. a writer this repair is losing "
                       + "a race with, and that is a write-war finding rather than a latch finding. "
                       + "(3) THE FOUR READINGS ARE WHAT MAKE THE NEXT SESSION CONCLUSIVE, and they "
                       + "are all taken BEFORE that frame's write, so they are the DEFECT RATE and "
                       + "not the residue. baseline 0 / drag max N / at release N / settled N means "
                       + "the latch is being re-created continuously and the repair is holding it off "
                       + "frame by frame — that is exactly the user's 'beim Loslassen wurde random "
                       + "ein Stand eingefroren' with the freeze now being repaired every frame. "
                       + "baseline 0 / drag max N / at release 0 / settled 0 means the latch is "
                       + "created only while the pose moves — which is the TextMeshProUGUI.Cull "
                       + "deferred-clip-rect path described in RepairSubMeshCull, and it predicts the "
                       + "flicker exactly. ALL FOUR ZERO with a non-zero UN-HIDDEN total means the "
                       + "latch happened outside a grab (a sub-view switch, a re-parent, an enable). "
                       + "ALL FOUR n/a means no grab was seen at all — the readings did not fail, "
                       + "there was nothing to read. "
                       + "(4) THE THREE-WAY SPLITS ARE NOT COSMETIC. 'CULL FLAG' is this defect; 'own "
                       + "alpha 0' is authored or animated and is not ours; 'inherited alpha 0' is a "
                       + "CanvasGroup fade above the component. Before this build all three were one "
                       + "'||' and '16 culled/transparent' carried no information at all. "
                       + "(5) THE REGENERATION CURSOR CLOSES A FOURTH 'UNCONDITIONAL' REMEDY THAT WAS "
                       + "NOT. The cap is 256 per pass and this window carries up to 297 components, "
                       + "and the walk is a deterministic pre-order — so the SAME tail was skipped at "
                       + "every release, forever, while the release line called the pass "
                       + "unconditional. One release runs two passes, so 2 x 256 now covers the whole "
                       + "subtree. If 'cursor now at' is non-zero on every report, the window needs "
                       + "more passes than a release provides and the lever is MaxRegeneratePerScan — "
                       + "priced at ~76 microseconds per component against a release frame already "
                       + "running 23-35 ms.");
        VRLog.Info(Scope, FrameSb.ToString());
        FrameSb.Length = 0;
        e.CullRepairsThisWindow = 0;
    }

    /// <summary>A four-readings field that never lets "not measured" look like "measured zero".</summary>
    private static string Reading(int v) => v < 0 ? "n/a" : v.ToString();

    /// <summary>
    /// <b>ONE LINE PER PANEL PER REPORT CADENCE: THE CAPTURE FRAME, ITS HYSTERESIS AND WHAT SET ITS
    /// EDGES.</b> The whole argument is on <see cref="MeasureFrame"/>; this prices it.
    /// </summary>
    private static void ReportCaptureFrame(Entry e)
    {
        int measures = e.ContentMeasures;
        float needWMean = e.NeedReadings > 0 ? (float)(e.NeedWSum / e.NeedReadings) : 0f;
        float needHMean = e.NeedReadings > 0 ? (float)(e.NeedHSum / e.NeedReadings) : 0f;
        float ceiling = e.FrameRateCeiling > 0f ? e.FrameRateCeiling : FrameRateCeilingFor(e.Frame);
        e.FrameRateCeiling = ceiling;

        FrameSb.Length = 0;
        FrameSb.Append("PANEL CAPTURE FRAME '").Append(e.Window).Append("'").Append(OpenViewTag(e))
               .Append(": ").Append(e.Frame.width.ToString("F0")).Append('x')
               .Append(e.Frame.height.ToString("F0")).Append(" uGUI px = host rect ")
               .Append(e.HostRectAtMeasure.width.ToString("F0")).Append('x')
               .Append(e.HostRectAtMeasure.height.ToString("F0")).Append(" + HELD overspill L")
               .Append(e.HoldLeft.ToString("F0")).Append(" R").Append(e.HoldRight.ToString("F0"))
               .Append(" D").Append(e.HoldDown.ToString("F0")).Append(" U")
               .Append(e.HoldUp.ToString("F0")).Append(" px (grid ")
               .Append(FrameQuantumPx.ToString("F0")).Append(" px). MEASUREMENTS: ").Append(measures)
               .Append(" taken, ").Append(e.FrameMeasuresSkipped)
               .Append(" SKIPPED because the window was only being TRANSLATED (the ModBuild 204 gate: "
                       + "every bound this measurement takes is HOST-LOCAL, so a rigid move cannot "
                       + "change it — see SkipMeasureWhileTranslating). HYSTERESIS: ")
               .Append(e.FrameGrows).Append(" grow(s), ").Append(e.FrameShrinks).Append(" shrink(s), ")
               .Append(e.FrameShrinksRefused)
               .Append(" shrink(s) REFUSED, current shrink run ").Append(e.ShrinkRun).Append(" of ")
               .Append(e.FrameOutlierPending
                   ? FrameOutlierShrinkRunMeasurements + " (an OUTLIER growth is pending, so the run "
                     + "is shortened)"
                   : FrameShrinkRunMeasurements.ToString())
               .Append(", dead band ").Append(FrameShrinkDeadBandQuanta).Append(" quantum(a) = ")
               .Append((FrameShrinkDeadBandQuanta * FrameQuantumPx).ToString("F0")).Append(" px; ")
               .Append(e.FrameOutlierGrowths).Append(" outlier growth(s) past ")
               .Append(FrameOutlierGrowQuanta).Append(" quanta in one step, ")
               .Append(e.FrameOutlierReleases).Append(" of them released again. ")
               .Append("MEASURED NEED (the whole distribution, because a flap is a distribution and "
                       + "its last reading is not): W lowest ")
               .Append(e.NeedWLowest.ToString("F0")).Append(" mean ").Append(needWMean.ToString("F1"))
               .Append(" highest ").Append(e.NeedWHighest.ToString("F0")).Append(" / H lowest ")
               .Append(e.NeedHLowest.ToString("F0")).Append(" mean ").Append(needHMean.ToString("F1"))
               .Append(" highest ").Append(e.NeedHHighest.ToString("F0")).Append(" over ")
               .Append(e.NeedReadings).Append(" reading(s). RE-ALLOCATIONS since engage: ")
               .Append(e.Reallocations).Append(" (the ModBuild 203 log measured 58 in 62 s on this "
                       + "window, 45 of them inside a grab, 2.51/s held against 0.29/s not held). "
                       + "RATE CEILING for this frame: ")
               .Append(ceiling.ToString("F2")).Append(" texels per authored px (")
               .Append(MaxRtDimension).Append(" px per axis, quantised to ")
               .Append(RateQuantum.ToString("F2")).Append(") against a band limit of ")
               .Append(BandLimitedTexelsPerPixel.ToString("F2"))
               .Append(ceiling < BandLimitedTexelsPerPixel - 1e-3f
                   ? " — BELOW THE BAND LIMIT: this frame is wide enough that the dimension ceiling, "
                     + "not the config, is what sets the rate, and the eye is reading unfiltered mip "
                     + "level 0 again"
                   : " — at or above it, so growth has not cost this window its band limit")
               .Append(". FRAME CHANGES: ").Append(e.FrameChanges).Append(" committed since engage; "
                       + "LAST CHANGE: ").Append(e.FrameChangeNote)
               .Append(" CONTENT OUTSIDE THE COMMITTED FRAME: ")
               .Append(e.ExpandClamped
                   ? "YES — the expansion clamp is biting and content IS being cropped"
                   : "none")
               .Append('.');

        FrameSb.Append(" HOW TO READ IT. GROWS + SHRINKS NEAR ZERO OVER A SESSION WITH REAL DRAGS IS "
                       + "THE FIX WORKING — the frame settled once and stayed there, and the "
                       + "re-allocation count should be within one or two of the grow count. A LARGE "
                       + "REFUSED-SHRINK COUNT WITH A LARGE FRAME IS THE HYSTERESIS HOLDING AN "
                       + "OUTLIER, and the GROWTH BOUND (FrameOutlierGrowQuanta) is what to look at "
                       + "next; check 'outlier growth(s)' against 'released again' on the same line. "
                       + "A LARGE SKIP COUNT NEXT TO A SMALL MEASURE COUNT IS EXPECTED AND IS THE "
                       + "POINT: it is the drag frames that used to re-sample a union that cannot "
                       + "have changed. THE 'MEASURED NEED' DISTRIBUTION IS THE FALSIFIER FOR THE "
                       + "WHOLE ROUND: if H lowest and H highest differ by exactly one 32 px quantum "
                       + "then the ModBuild 203 flap is still live in the MEASUREMENT and the "
                       + "hysteresis is doing its job downstream of it; if they are equal, the union "
                       + "itself has stopped moving and the flap was a sampling artefact of the "
                       + "cadence. THE 'LAST CHANGE' EXTREMES ARE THE ONE FIELD THAT NAMES THE "
                       + "FLAPPER: four edges, each with the graphic that set it, then and now. "
                       + "'[SAME GRAPHIC, different measurement]' on the TOP or BOTTOM edge means one "
                       + "object is being measured to two different heights — a layout or animation "
                       + "question, not a capture one. '[DIFFERENT GRAPHIC]' means the window's "
                       + "content genuinely changed and one reallocation is the correct price. NOTE "
                       + "WHAT THIS LINE CANNOT SEE: a growth that arrives DURING a drag is deferred "
                       + "to the release re-measure (ReleaseSettleFrames = "
                       + ReleaseSettleFrames + " frames after the hand stops), so content that "
                       + "appears outside the frame mid-drag is cropped for the rest of that drag. "
                       + "That is the stated cost of the translation gate; if a hardware session ever "
                       + "shows content missing ONLY WHILE DRAGGING and present again the instant the "
                       + "hand lets go, this is the first suspect and TranslateHoldFrames is the "
                       + "lever.");
        VRLog.Info(Scope, FrameSb.ToString());
        FrameSb.Length = 0;
    }
}
