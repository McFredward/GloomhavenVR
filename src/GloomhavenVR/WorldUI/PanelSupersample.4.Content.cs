// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here. Part 4 answers exactly one question: WHAT RECTANGLE MUST THE CAPTURE FRAME so
// that switching the dial on can never take visible content away from the player.

using System.Collections.Generic;
using GloomhavenVR.Core;
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
}
