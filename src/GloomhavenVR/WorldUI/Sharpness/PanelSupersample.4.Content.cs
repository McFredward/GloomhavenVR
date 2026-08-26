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
    ///
    /// <para><b>MODBUILD 205 — THE ONE CORRECTION TO THE ABOVE, AND IT IS TO THE GROWTH RULE.</b> 204's
    /// hysteresis worked: re-allocations fell from 58 in 62 s to 0-6 per session. What "grow immediately
    /// and unconditionally" did NOT know is that this window's rate ceiling has a cliff 28 px away —
    /// 2020 px of frame gives 2.00 texels per authored pixel and 2052 px gives 1.75, with nothing in
    /// between, because <see cref="RateQuantum"/> is 0.25. The 204 hardware log therefore carries 204's
    /// own warning (<c>GREW its capture frame to 2052x1464 ... stepped the achievable capture rate from
    /// 2.00 down to 1.75 ... BELOW the 2.00 band limit</c>), <c>ACHIEVED 1.75</c> on 15 of 70 readings
    /// and <c>NOT BAND-LIMITED</c> on 39 of 90 verdicts — and then the shrink hysteresis, doing exactly
    /// what it was told, held the window there, because the way back is one quantum and the dead band is
    /// one quantum. <b>Growth is still immediate and unconditional; what it is no longer allowed to do
    /// is buy overspill with the band limit.</b> A candidate frame is cut down to
    /// <see cref="BandLimitFrameBudgetPx"/> (which carries the whole argument, the rejected framings and
    /// the residual as numbers), the crop is given back to the edges proportionally to what each asked
    /// for and logged edge by edge with the graphic that set it, and a shrink that recovers the band
    /// limit or recovers cropped content is exempt from the dead band and the run
    /// (<see cref="ShrinkRecoversBandLimit"/>). RESOLUTION FOR THE WHOLE WINDOW OUTRANKS OVERSPILL:
    /// content outside the host rect is outside the window's own frame, while the band limit governs
    /// every pixel the user actually reads.</para>
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
        ApplyFrameHysteresis(e, hostRect, padX, padY, needLeft, needRight, needDown, needUp);

        // ---- THE GROWTH CLAMP (ModBuild 205) ----------------------------------------------------
        // RESOLUTION FOR THE WHOLE WINDOW OUTRANKS OVERSPILL. The held overspill is the ASK; what the
        // frame is actually built from is the ask cut down to whatever still keeps the achievable rate
        // at or above the band limit (BandLimitFrameBudgetPx, which carries the whole argument and the
        // residual numbers). The clamp is applied HERE rather than inside the hysteresis, and applied
        // on EVERY measurement rather than only on a change, for three reasons that all matter:
        //   (1) it is a pure function of the live host rect and the four holds, so a host rect that
        //       grows under a fixed overspill is caught by exactly the same code path as a growth of
        //       the overspill itself — there is no "committed frame change" it can slip past;
        //   (2) the holds stay a clean record of what the CONTENT asked for, so the 204 hysteresis
        //       arithmetic (grow / dead band / run / outlier) is untouched and keeps working on the
        //       measured need rather than on a truncated echo of itself — clamping the holds instead
        //       would make every subsequent measurement re-read as a fresh GROWTH, which would reset
        //       the shrink run forever and quietly disable the shrink side;
        //   (3) it is idempotent, so nothing can accumulate.
        Rect frame = ClampFrameToBandLimit(hostRect, e.HoldLeft, e.HoldRight, e.HoldDown, e.HoldUp,
                                           out float giveLeft, out float giveRight,
                                           out float giveDown, out float giveUp);
        e.CropLeft = e.HoldLeft - giveLeft;
        e.CropRight = e.HoldRight - giveRight;
        e.CropDown = e.HoldDown - giveDown;
        e.CropUp = e.HoldUp - giveUp;
        if (frame.width < 1f || frame.height < 1f)
            frame = hostRect;
        // CLAMPED is judged against the frame that is actually COMMITTED, not against the raw need:
        // the question the field answers is "is visible content outside the rectangle the camera
        // frames", and after this build those two rectangles are no longer the same thing.
        bool clamped = frame.xMin > union.xMin + 0.5f || frame.xMax < union.xMax - 0.5f
                       || frame.yMin > union.yMin + 0.5f || frame.yMax < union.yMax - 0.5f;
        // The trade, once per clamp STATE: what was asked, what was committed, what rate that saved,
        // and how many px were cropped on each edge by which graphic. A silent crop is unacceptable.
        NoteBandLimitClamp(e, hostRect, frame);

        // A COMMITTED CHANGE is a change of the RECTANGLE, whatever produced it — a hysteresis grow,
        // a hysteresis shrink, or the host rect moving under a fixed overspill. That is what costs a
        // reallocation and re-rolls the phase, so that is what the change record is keyed on.
        bool frameChanged = Mathf.Abs(frame.xMin - e.Frame.xMin) > 0.5f
                            || Mathf.Abs(frame.xMax - e.Frame.xMax) > 0.5f
                            || Mathf.Abs(frame.yMin - e.Frame.yMin) > 0.5f
                            || Mathf.Abs(frame.yMax - e.Frame.yMax) > 0.5f;
        // The host rect is committed BEFORE the change note, which reads it: after ModBuild 205 that
        // note's sub-band-limit warning names the host rect as the only remaining cause, and naming
        // the PREVIOUS measurement's host rect there would point at the wrong number.
        e.HostRectAtMeasure = hostRect;
        if (frameChanged)
            NoteFrameChange(e, frame);

        e.Frame = frame;
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
            float bandCrop = e.CropLeft + e.CropRight + e.CropDown + e.CropUp;
            string bandNote = bandCrop > 0.5f
                ? $" NOTE: {bandCrop:F0} px of that is the ModBuild 205 BAND-LIMIT CLAMP and not the "
                  + "expansion limit — the CLAMPED ITS CAPTURE FRAME line prices that crop edge by "
                  + "edge and names the graphic on each one. Raising MaxContentExpansion would not "
                  + "give it back; the levers there are the host's authored size and MaxRtDimension."
                : string.Empty;
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
                              + "[WorldUI] PanelSupersample off for this session." + bandNote);
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
    /// <item><b>ModBuild 205: A SHRINK THAT RECOVERS THE BAND LIMIT — OR RECOVERS CROPPED CONTENT —
    /// TAKES NEITHER THE DEAD BAND NOR THE RUN.</b> Rules 2 and 3 arbitrate between frames that are
    /// equally good; one that costs resolution or costs content is not. See
    /// <see cref="ShrinkRecoversBandLimit"/>, and <see cref="BandLimitFrameBudgetPx"/> for why 204's
    /// growth rule made that case reachable at all.</item>
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
    private static void ApplyFrameHysteresis(Entry e, Rect hostRect, float padX, float padY,
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

        // ---- THE BAND-LIMIT BYPASS (ModBuild 205) -----------------------------------------------
        // A frame that costs the band limit — or that is paying for its overspill in CROPPED content
        // — is not equally good as the smaller one, and the run exists only to arbitrate between two
        // EQUALLY GOOD frames. See ShrinkRecoversBandLimit for the whole rule and for why it cannot
        // re-open the ModBuild 203 flap.
        if (ShrinkRecoversBandLimit(e, hostRect, needLeft, needRight, needDown, needUp))
        {
            e.HoldLeft = needLeft;
            e.HoldRight = needRight;
            e.HoldDown = needDown;
            e.HoldUp = needUp;
            e.ShrinkRun = 0;
            e.FrameShrinks++;
            e.BandLimitShrinks++;
            if (e.FrameOutlierPending)
            {
                e.FrameOutlierReleases++;
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

    // ---- THE GROWTH CLAMP (ModBuild 205) --------------------------------------------------------

    /// <summary>
    /// <b>THE CAPTURE FRAME A HOST RECT AND FOUR ASKED OVERSPILLS ACTUALLY GET — cut down, on the
    /// <see cref="FrameQuantumPx"/> grid, to whatever still achieves
    /// <see cref="BandLimitedTexelsPerPixel"/>.</b> The rule, the measured failure it corrects and the
    /// residual numbers are all on <see cref="BandLimitFrameBudgetPx"/>.
    ///
    /// <para><b>WHY A BUDGET IN PIXELS IS EXACTLY EQUIVALENT TO THE RATE TEST</b>, i.e. why this does
    /// not need to call <see cref="FrameRateCeilingFor"/> per candidate: that function is
    /// <c>floor(min(4096/w, 4096/h) / 0.25) * 0.25</c>, and <c>floor(x/0.25)*0.25 &gt;= 2.00</c> iff
    /// <c>x &gt;= 2.00</c> iff <c>w &lt;= 4096/2.00</c> and <c>h &lt;= 4096/2.00</c>. So "frame within
    /// the budget on both axes" and "achieved rate at or above the band limit" are the SAME predicate,
    /// and the largest frame satisfying it is the largest grid multiple that fits the budget — which is
    /// what <see cref="SplitAllowance"/> hands out.</para>
    ///
    /// <para><b>THE ARITHMETIC IS <see cref="ResolveRate"/>'S, VERIFIED LINE BY LINE</b> (that method
    /// lives in PanelSupersample.2.Capture.cs, another lane's file). It computes
    /// <c>ceiling = Mathf.Min(MaxRtDimension / Mathf.Max(frame.width, 1f), MaxRtDimension /
    /// Mathf.Max(frame.height, 1f))</c>, then <c>rate = Mathf.Min(asked, ceiling)</c>, then
    /// <c>Mathf.Floor(rate / RateQuantum) * RateQuantum</c>, then <c>Mathf.Max(rate, RateQuantum)</c>.
    /// <see cref="FrameRateCeilingFor"/> is that expression with <c>asked</c> removed — identical
    /// constants, identical <c>Mathf.Max(.., 1f)</c> guards, identical floor-then-clamp order — so the
    /// two agree on every frame for which the DIMENSION is what binds, which is the only case this
    /// clamp claims to govern. <b>What it deliberately does not model:</b> the ask
    /// (<c>Factor / MinContentScale</c>, always &gt;= <see cref="BandLimitFactor"/>, so it can never be
    /// the thing that pushes the achieved rate below the band limit) and the VRAM step-down loop, which
    /// can still take the rate down when <see cref="MaxPanelVramBytes"/> binds — see
    /// <see cref="BandLimitFrameBudgetPx"/>, where that wall is priced at 69 px of frame height above
    /// today's operating point. A frame this clamp calls safe can therefore still lose the band limit
    /// to VRAM, and no frame arithmetic can prevent that; the report line prints the ACHIEVED rate, not
    /// this ceiling, and the two are compared on the resample line.</para>
    /// </summary>
    private static Rect ClampFrameToBandLimit(Rect hostRect, float left, float right,
                                              float down, float up,
                                              out float giveLeft, out float giveRight,
                                              out float giveDown, out float giveUp)
    {
        float allowX = AllowanceFor(hostRect.width);
        float allowY = AllowanceFor(hostRect.height);
        // ONE AXIS PAST THE BUDGET STANDS THE WHOLE CLAMP DOWN. The rate is the MINIMUM over both axes,
        // so if either axis alone is already past the budget the band limit is unreachable whatever is
        // cropped — and cropping content that buys nothing is pure loss. Cropping is justified by
        // saving the band limit and by nothing else.
        if (allowX < 0f || allowY < 0f)
        {
            allowX = -1f;
            allowY = -1f;
        }
        SplitAllowance(left, right, allowX, out giveLeft, out giveRight);
        SplitAllowance(down, up, allowY, out giveDown, out giveUp);
        return Rect.MinMaxRect(hostRect.xMin - giveLeft, hostRect.yMin - giveDown,
                               hostRect.xMax + giveRight, hostRect.yMax + giveUp);
    }

    /// <summary>How much overspill one axis may still spend and stay band-limited, on the
    /// <see cref="FrameQuantumPx"/> grid. <b>-1 means NO BOUND</b> — the host rect alone is already past
    /// <see cref="BandLimitFrameBudgetPx"/> on this axis, so nothing can be bought here and nothing is
    /// taken away. Zero is a real answer and is not the same thing: it means the band limit IS reachable
    /// and costs the whole of this axis' overspill.</summary>
    private static float AllowanceFor(float hostExtent)
    {
        float spare = BandLimitFrameBudgetPx - Mathf.Max(hostExtent, 0f);
        if (spare < -0.5f)
            return -1f;
        if (spare < FrameQuantumPx)
            return 0f;
        return Mathf.Floor(spare / FrameQuantumPx) * FrameQuantumPx;
    }

    /// <summary>
    /// <b>GIVE THE ALLOWANCE BACK TO THE TWO EDGES OF ONE AXIS, PROPORTIONALLY TO WHAT EACH ASKED
    /// FOR</b>, in whole <see cref="FrameQuantumPx"/> quanta. A negative allowance means unbounded and
    /// every ask is granted verbatim.
    /// <para>Whole quanta because the grid is the reason the frame does not re-roll the sub-texel phase
    /// on every measurement (<see cref="FrameQuantumPx"/>): handing an edge 22 px because that is its
    /// proportional share would give the phase back exactly what the grid took away. The leftover
    /// quantum a two-bucket largest-remainder split can produce goes to the larger remainder, then to
    /// the larger ask, then to the first edge — deterministic at every step, because a tie broken by
    /// float order would make the crop itself flap.</para>
    /// </summary>
    private static void SplitAllowance(float askA, float askB, float allowance,
                                       out float giveA, out float giveB)
    {
        int qa = Mathf.Max(0, Mathf.RoundToInt(askA / FrameQuantumPx));
        int qb = Mathf.Max(0, Mathf.RoundToInt(askB / FrameQuantumPx));
        int cap = allowance < 0f ? int.MaxValue : Mathf.Max(0, Mathf.RoundToInt(allowance / FrameQuantumPx));
        int total = qa + qb;
        if (total <= cap)
        {
            // The whole ask fits: hand back the asks THEMSELVES, not their quantised echo, so a window
            // that is nowhere near the budget — which is every window whose host rect is more than one
            // quantum inside 2048 px on both axes — is bit-for-bit what ModBuild 204 committed.
            giveA = askA;
            giveB = askB;
            return;
        }
        int ga = cap * qa / total;
        int gb = cap * qb / total;
        if (ga + gb < cap)
        {
            int ra = cap * qa - ga * total;
            int rb = cap * qb - gb * total;
            if (ra > rb || (ra == rb && qa >= qb))
                ga++;
            else
                gb++;
        }
        giveA = Mathf.Min(ga, qa) * FrameQuantumPx;
        giveB = Mathf.Min(gb, qb) * FrameQuantumPx;
    }

    /// <summary>
    /// <b>IS THIS SHRINK ONE THE HYSTERESIS IS NOT ALLOWED TO ARBITRATE?</b> True in exactly one case,
    /// stated two ways because the clamp can pay for a frame in two currencies: the frame committed
    /// right now is <b>below the band limit</b> and the candidate restores it, or the frame committed
    /// right now is <b>CROPPING content</b> that the candidate would not crop.
    ///
    /// <para><b>WHY IT CANNOT RE-OPEN THE ModBuild 203 FLAP.</b> That flap was 2020x1464 against
    /// 2020x1496 — 28 measurements each way in 62 s. Both legs sit at rate ceiling 2.00, and both are
    /// far inside the Y allowance (host height 1080 leaves <c>floor((2048-1080)/32)*32 = 960</c> px of
    /// Y overspill against the 384-416 px actually asked), so BOTH crops are zero. Neither clause can
    /// therefore be true on either leg: a band-limit-restoring shrink is a different event by
    /// construction, and the dead band still owns the flap exactly as ModBuild 204 shipped it.</para>
    ///
    /// <para><b>WHAT IT COSTS, and why the run's per-edge maximum is not used here.</b> This branch
    /// adopts a SINGLE measurement's need instead of a run maximum, so a need that dips for one
    /// measurement and comes back can cost one extra frame change. That is bounded and it is the right
    /// side to err on: growth is immediate and unconditional (rule 1), so the return trip is one
    /// measurement, whereas the state this branch escapes is a frame that is either reading unfiltered
    /// mip level 0 or hiding content — permanently, because the dead band is one quantum and the way
    /// back out of a one-quantum overshoot is one quantum. <see cref="Entry.BandLimitShrinks"/> counts
    /// every use so a hardware log can say whether it ever fires twice on the same window.</para>
    ///
    /// <para><b>ON REACHABILITY, honestly.</b> With the growth clamp in place the FIRST clause is
    /// normally unreachable: a frame below the band limit can no longer be committed unless the host
    /// rect ALONE is past the budget, and in that case the clamp has stood down and no shrink of the
    /// overspill can restore the limit either — so the clause is a guard, not a mechanism, and it is
    /// written the way the rule is written rather than the way today's call graph happens to be. The
    /// SECOND clause is the live one, and it is live precisely because of the clamp: with an X
    /// allowance of one quantum, an ask of L32/R32 is committed as L32/R0 with 32 px cropped, and a
    /// later honest need of L0/R32 is a ONE-QUANTUM shrink that the dead band would refuse forever
    /// while the cropped content stayed invisible.</para>
    /// </summary>
    private static bool ShrinkRecoversBandLimit(Entry e, Rect hostRect, float needLeft,
                                                float needRight, float needDown, float needUp)
    {
        Rect held = ClampFrameToBandLimit(hostRect, e.HoldLeft, e.HoldRight, e.HoldDown, e.HoldUp,
                                          out float hl, out float hr, out float hd, out float hu);
        Rect want = ClampFrameToBandLimit(hostRect, needLeft, needRight, needDown, needUp,
                                          out float nl, out float nr, out float nd, out float nu);
        float heldCrop = (e.HoldLeft - hl) + (e.HoldRight - hr) + (e.HoldDown - hd) + (e.HoldUp - hu);
        float wantCrop = (needLeft - nl) + (needRight - nr) + (needDown - nd) + (needUp - nu);
        if (wantCrop < heldCrop - 0.5f)
            return true;
        return FrameRateCeilingFor(held) < BandLimitedTexelsPerPixel - 1e-3f
               && FrameRateCeilingFor(want) >= BandLimitedTexelsPerPixel - 1e-3f;
    }

    /// <summary>
    /// <b>PRICE THE TRADE, ONCE PER CLAMP STATE.</b> A bounded coverage must be logged — this project
    /// has the standing rule — and a crop is the most expensive kind of bound there is, so the line
    /// carries everything a reader needs to decide whether the trade was right: the frame that was
    /// asked for, the frame that was committed, the rate that bought, and how many uGUI px were cropped
    /// on EACH edge together with the graphic that set that edge (<see cref="ExtremeRecord"/>, which
    /// ModBuild 204 already records for exactly this kind of question).
    /// <para>Keyed on the crop CHANGING, not on the measurement: a steady clamp says so once, and the
    /// release back to a whole frame is one more line rather than silence. The first clamp on a window
    /// is a Warn with the full argument; every later change is an Info, so a window whose crop moves
    /// cannot flood the log with warnings, and the sentences stop after
    /// <see cref="MaxBandClampLines"/> while the count does not.</para>
    /// </summary>
    private static void NoteBandLimitClamp(Entry e, Rect hostRect, Rect frame)
    {
        if (Mathf.Abs(e.CropLeft - e.LoggedCropLeft) < 0.5f
            && Mathf.Abs(e.CropRight - e.LoggedCropRight) < 0.5f
            && Mathf.Abs(e.CropDown - e.LoggedCropDown) < 0.5f
            && Mathf.Abs(e.CropUp - e.LoggedCropUp) < 0.5f)
            return;
        e.LoggedCropLeft = e.CropLeft;
        e.LoggedCropRight = e.CropRight;
        e.LoggedCropDown = e.CropDown;
        e.LoggedCropUp = e.CropUp;
        e.BandClamps++;

        // The sentences are bounded (MaxBandClampLines); the COUNT and the live crop are not — they are
        // on every CAPTURE FRAME line — so going quiet here can never read as "it stopped".
        if (e.BandClamps > MaxBandClampLines)
            return;

        float crop = e.CropLeft + e.CropRight + e.CropDown + e.CropUp;
        float askedW = hostRect.width + e.HoldLeft + e.HoldRight;
        float askedH = hostRect.height + e.HoldDown + e.HoldUp;
        Rect asked = new(0f, 0f, askedW, askedH);
        float askedCeiling = FrameRateCeilingFor(asked);
        float nowCeiling = FrameRateCeilingFor(frame);
        if (crop < 0.5f)
        {
            VRLog.Info(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' NO LONGER CLAMPS its capture frame — "
                              + $"the whole measured overspill (L{e.HoldLeft:F0} R{e.HoldRight:F0} "
                              + $"D{e.HoldDown:F0} U{e.HoldUp:F0} px) now fits inside the "
                              + $"{BandLimitFrameBudgetPx:F0} px band-limit budget, so the committed "
                              + $"frame is the full {frame.width:F0}x{frame.height:F0} uGUI px at "
                              + $"{nowCeiling:F2} texels per authored px. Clamp state changes on this "
                              + $"window so far: {e.BandClamps}.");
            return;
        }

        FrameSb.Length = 0;
        FrameSb.Append("PANEL SUPERSAMPLE: '").Append(e.Window)
               .Append("' CLAMPED ITS CAPTURE FRAME to keep the whole window band-limited. ASKED ")
               .Append(askedW.ToString("F0")).Append('x').Append(askedH.ToString("F0"))
               .Append(" uGUI px (host rect ").Append(hostRect.width.ToString("F0")).Append('x')
               .Append(hostRect.height.ToString("F0")).Append(" + measured overspill L")
               .Append(e.HoldLeft.ToString("F0")).Append(" R").Append(e.HoldRight.ToString("F0"))
               .Append(" D").Append(e.HoldDown.ToString("F0")).Append(" U")
               .Append(e.HoldUp.ToString("F0")).Append("), COMMITTED ")
               .Append(frame.width.ToString("F0")).Append('x').Append(frame.height.ToString("F0"))
               .Append(". THE RATE THAT SAVED: ").Append(askedCeiling.ToString("F2")).Append(" -> ")
               .Append(nowCeiling.ToString("F2"))
               .Append(" render-target texels per authored pixel, against a band limit of ")
               .Append(BandLimitedTexelsPerPixel.ToString("F2")).Append(" — ").Append(MaxRtDimension)
               .Append(" px per axis over the frame, quantised to ").Append(RateQuantum.ToString("F2"))
               .Append(", so there is nothing between 2.00 and 1.75 and ")
               .Append(BandLimitFrameBudgetPx.ToString("F0"))
               .Append(" px is the widest frame that still reaches the limit. WHAT IT COST, per edge:");
        for (int i = 0; i < 4; i++)
        {
            float px = i == EdgeLeft ? e.CropLeft
                     : i == EdgeRight ? e.CropRight
                     : i == EdgeDown ? e.CropDown
                     : e.CropUp;
            ExtremeRecord rec = ExtremeScratch[i];
            FrameSb.Append(' ').Append(EdgeNames[i]).Append(' ').Append(px.ToString("F0"))
                   .Append(px < 0.5f ? " px cropped" : " px CROPPED")
                   .Append(", edge set by '").Append(rec.Valid ? rec.Name : "(no reading)")
                   .Append("' at ").Append(RectText(rec.Rect)).Append(i < 3 ? ";" : ".");
        }
        // The lever named is the lever on the axis that actually bit — a Y crop is not answered by a
        // narrower host, and a line that says otherwise sends the next round at the wrong number.
        bool xBit = e.CropLeft + e.CropRight > 0.5f;
        float hostExtent = xBit ? hostRect.width : hostRect.height;
        float frameExtent = xBit ? frame.width : frame.height;
        FrameSb.Append(" WHY THIS IS THE RIGHT WAY ROUND: content outside the host rect is by "
                       + "definition outside the window's own frame, while the band limit governs "
                       + "every pixel the user actually reads — below it a 1-px glyph stroke exists or "
                       + "does not depending on the sub-texel phase, and on release that phase LOCKS "
                       + "at whatever the hand left behind (see BandLimitFactor). THE LEVERS, in "
                       + "order: (1) what draws outside the host rect — the edge names above; (2) the "
                       + "host's AUTHORED ")
               .Append(xBit ? "WIDTH" : "HEIGHT")
               .Append(", which is the real cause — this host is ")
               .Append(hostExtent.ToString("F0")).Append(" px on that axis and a host of ")
               .Append(Mathf.Max(0f, BandLimitFrameBudgetPx - (frameExtent - hostExtent)
                                     - FrameQuantumPx).ToString("F0"))
               .Append(" px would leave a full ").Append(FrameQuantumPx.ToString("F0"))
               .Append(" px quantum of growth headroom instead of ")
               .Append((BandLimitFrameBudgetPx - frameExtent).ToString("F0"))
               .Append(" px; (3) MaxRtDimension, which would have to be ")
               .Append(Mathf.CeilToInt(Mathf.Max(askedW, askedH) * BandLimitedTexelsPerPixel))
               .Append(" px for the ASKED frame to hold the band limit (the 160 MB per-panel VRAM cap "
                       + "does not bind until the frame reaches 3,145,728 uGUI px² at rate 2.00 — see "
                       + "BandLimitFrameBudgetPx, where both walls are priced). Clamp state changes on "
                       + "this window so far: ")
               .Append(e.BandClamps).Append('.');
        if (e.BandClamps == 1)
            VRLog.Warn(Scope, FrameSb.ToString());
        else
            VRLog.Info(Scope, FrameSb.ToString());
        FrameSb.Length = 0;
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
                              + "AND THE ModBuild 205 GROWTH CLAMP DID NOT PREVENT IT, which narrows "
                              + "the cause to ONE thing: an axis of the HOST RECT ALONE is at or past "
                              + $"the {BandLimitFrameBudgetPx:F0} px band-limit budget (this host is "
                              + $"{e.HostRectAtMeasure.width:F0}x{e.HostRectAtMeasure.height:F0}), so "
                              + "there is no overspill left to give back and the clamp correctly stood "
                              + "down rather than crop content for nothing. THE LEVERS, in order: the "
                              + "host's AUTHORED size (the window fit, not this class), then "
                              + "MaxRtDimension. Content that draws outside the host rect is NOT the "
                              + "cause in this branch. This is warned ONCE per window; the CAPTURE "
                              + "FRAME line carries the live ceiling every report.");
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
        // HERE rather than there, because there is per-component and this is per-scan.
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
        int meshBad = 0;

        NoteRendererState(e, t.canvasRenderer, t.gameObject.name);
        // AND THE SUB-MESHES THIS COMPONENT HANDS ITS OTHER MATERIALS TO — see Entry.SubMeshesSeen.
        // Done from here rather than from the outer walk on purpose: that walk skips anything
        // !activeInHierarchy, which is precisely one of the states this needs to be able to report.

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
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

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
               .Append((FrameShrinkDeadBandQuanta * FrameQuantumPx).ToString("F0")).Append(" px (")
               .Append(e.BandLimitShrinks)
               .Append(" shrink(s) BYPASSED it because the held frame was costing the band limit or "
                       + "costing cropped content); ")
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
                     + "level 0 again. After ModBuild 205 that can only happen when an axis of the "
                     + "HOST RECT ALONE is past the "
                     + BandLimitFrameBudgetPx.ToString("F0") + " px budget, where cropping buys "
                     + "nothing and the clamp stands down"
                   : " — at or above it, so growth has not cost this window its band limit")
               .Append(". BAND-LIMIT CLAMP (ModBuild 205, budget ")
               .Append(BandLimitFrameBudgetPx.ToString("F0")).Append(" px per axis): ")
               .Append(e.CropLeft + e.CropRight + e.CropDown + e.CropUp > 0.5f
                   ? "BITING — measured overspill CROPPED by L" + e.CropLeft.ToString("F0")
                     + " R" + e.CropRight.ToString("F0") + " D" + e.CropDown.ToString("F0")
                     + " U" + e.CropUp.ToString("F0") + " px to keep the whole window band-limited, "
                     + "which is the deliberate trade (resolution for every pixel the user reads "
                     + "outranks overspill outside the host rect) and is priced edge by edge, with the "
                     + "graphic on each edge, on this window's CLAMPED ITS CAPTURE FRAME line"
                   : "not biting — the whole measured overspill is framed")
               .Append(", ").Append(e.BandClamps).Append(" clamp state change(s) logged")
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
