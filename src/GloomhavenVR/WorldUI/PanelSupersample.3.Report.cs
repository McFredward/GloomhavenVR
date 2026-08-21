// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here.

using UnityEngine;
using UnityEngine.XR;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- the report ---------------------------------------------------------------------------

    /// <summary>
    /// One state line per supersampled panel, on a 10 s cadence, hit or no hit. Shaped so the NEXT
    /// hardware round is decidable from the log alone — including the outcome in which this approach
    /// does not help.
    /// </summary>
    private static void Report(Entry e)
    {
        Camera? head = VRRigDriver.HeadCamera;
        string sampling = SamplingSentence(e, head);
        float ms = e.Captures > 0 ? (float)(e.CaptureMs / e.Captures) : 0f;

        Sb.Length = 0;
        Sb.Append("PANEL SUPERSAMPLE '").Append(e.Window).Append("': authored ")
          .Append(e.Authored.x.ToString("F0")).Append('x').Append(e.Authored.y.ToString("F0"))
          .Append(" uGUI px -> RT ").Append(e.RtW).Append('x').Append(e.RtH)
          .Append(" (factor ").Append(e.Factor.ToString("F2")).Append("), MSAA ").Append(e.Msaa)
          .Append("x, mips ").Append(e.Rt != null ? e.Rt.mipmapCount : 0)
          .Append(e.Rt != null && e.Rt.mipmapCount > 1 ? " GENERATED after every capture" : " NONE")
          .Append(", ").Append(e.Rt != null ? e.Rt.filterMode.ToString() : "?")
          .Append(" aniso ").Append(e.Rt != null ? e.Rt.anisoLevel : 0)
          .Append(", ").Append(Mb(e.VramBytes)).Append(" MB VRAM (session total ")
          .Append(Mb(_vramTotal)).Append(" of ").Append(Mb(MaxTotalVramBytes)).Append(" MB across ")
          .Append(Entries.Count).Append(" panel(s), cap ").Append(MaxPanels).Append("); ")
          .Append(sampling)
          .Append(" CAPTURE: ").Append(e.Captures).Append(" so far, every ")
          .Append(CaptureIntervalFrames).Append(" frame(s), ").Append(ms.ToString("F2"))
          .Append(" ms CPU submit each. LAYERS: ").Append(e.Relayered.Count)
          .Append(" transform(s) on capture layer ").Append(_captureLayer).Append(", ")
          .Append(e.ForeignSkipped).Append(" foreign render subtree(s) LEFT ALONE, ")
          .Append(e.NestedCaptured).Append('/').Append(e.NestedTotal)
          .Append(" nested canvas(es) on the capture layer.");

        Sb.Append(" HOW TO READ THIS LINE. (1) RT texels per rendered pixel is the same quantity "
                  + "PANEL SAMPLING calls 'panel scale', multiplied by the factor — but it now means "
                  + "something completely different, because the surface being minified is a MIPPED, "
                  + "trilinear, anisotropically filtered texture instead of hundreds of "
                  + "point-sampled graphics. A value above 1 was the DEFECT before this build and is "
                  + "merely a mip level now. (2) If the user reports the windows quiet, the "
                  + "diagnosis was right: the flicker was undersampled RASTERIZATION (uGUI geometry "
                  + "edges, sliced-sprite borders and mipless SDF text), which no mip bake could ever "
                  + "reach. (3) If the text is quiet but the window looks SOFTER than before, the "
                  + "factor is the dial: [WorldUI] PanelSupersampleFactor above 1.0 buys RT texels "
                  + "(and VRAM) for sharpness. (4) IF NOTHING CHANGED AT ALL, this approach is dead "
                  + "and it dies decisively: the whole window is one filtered texture here, so a "
                  + "shimmer that survives cannot be a sampling problem on the panel surface at all "
                  + "— the next round must look at the compositor, the eye buffers or the display "
                  + "path, not at the panel. (5) If the window is BLANK or the character portrait is "
                  + "missing, read the LAYERS counts: 'foreign render subtree(s) LEFT ALONE' are real "
                  + "3D Renderers this pass must never move; they are not captured, and if a window "
                  + "shows its character through one of those rather than through a RawImage, that "
                  + "character will draw in the world in front of the quad instead of inside it. "
                  + "(6) 'nested canvas(es) on the capture layer' must read N/N: a nested canvas left "
                  + "behind would be drawn into the eye directly AND be missing from the capture, "
                  + "i.e. one sharp panel with one shimmering sub-panel on top of it. (7) If the "
                  + "TEXT reads slightly THINNER, or the window looks darker during its ~0.3 s show "
                  + "animation, that is the known and bounded premultiplied-alpha artifact of "
                  + "compositing a transparent render target with the standard UI blend (derived in "
                  + "full at PanelSupersample.BuildDisplay) — it affects PARTIAL coverage only, is "
                  + "exact at alpha 0 and 1, and is a one-line change if it matters.");
        VRLog.Info(Scope, Sb.ToString());
        Sb.Length = 0;

        WarnOnOverflow(e);
    }

    /// <summary>
    /// The comparable number: RT texels per RENDERED pixel through the LEFT eye's own projection,
    /// computed exactly the way <see cref="PanelSamplingProbe"/> computes its panel scale — the
    /// RectTransform's world corners pushed through
    /// <c>WorldToViewportPoint(..., MonoOrStereoscopicEye.Left)</c> and scaled by
    /// <c>XRSettings.eyeTextureWidth/Height * renderViewportScale</c>, measured as pixel-space EDGE
    /// LENGTHS so a window yawed away from the head reports its foreshortened size. Duplicated rather
    /// than shared because that probe is another lane's file; the two numbers are therefore directly
    /// comparable in the same hardware log, which is the point.
    /// </summary>
    private static string SamplingSentence(Entry e, Camera? head)
    {
        if (head == null)
            return "SAMPLING NOT MEASURED (no head camera);";
        if (!TryEyeTarget(out float eyeW, out float eyeH))
            return "SAMPLING NOT MEASURED (no readable per-eye render target);";
        if (!TryRenderedSize(e.Panel.HostRect, head, eyeW, eyeH, out float pxW, out float pxH))
            return "SAMPLING NOT MEASURED (the window is behind the eye or sub-pixel this scan);";
        float texelsPerPixel = Mathf.Max(e.RtW / Mathf.Max(pxW, 0.01f), e.RtH / Mathf.Max(pxH, 0.01f));
        float authoredPerPixel = Mathf.Max(e.Authored.x / Mathf.Max(pxW, 0.01f),
            e.Authored.y / Mathf.Max(pxH, 0.01f));
        return $"drawn into {pxW:F0}x{pxH:F0} rendered px through the LEFT eye of a "
               + $"{eyeW:F0}x{eyeH:F0} per-eye target ({XRSettings.stereoRenderingMode}, viewportScale "
               + $"{XRSettings.renderViewportScale:F2}) = {texelsPerPixel:F2} RT texels per rendered "
               + $"pixel, against {authoredPerPixel:F2} authored px per rendered px (which is what "
               + "PANEL SAMPLING reported for this window before this build, and is now filtered "
               + "rather than point-sampled);";
    }

    /// <summary>
    /// Does the window paint OUTSIDE its host rect? The capture frames the host rect exactly, so that
    /// the OFF and ON geometry are identical to the pixel — which means anything drawn proud of the
    /// frame is cropped. The content fit centres content inside the host by construction
    /// (<c>ConvertedPanel.FitContentPadding</c>), so this should never fire; if it does, the log says
    /// so once rather than leaving a mysteriously clipped window.
    /// </summary>
    private static void WarnOnOverflow(Entry e)
    {
        if (e.OverflowWarned || e.Panel.HostRect == null || e.Panel.Target == null)
            return;
        RectTransform host = e.Panel.HostRect;
        Rect hostRect = host.rect;
        Rect target = e.Panel.Target.rect;
        float overflowX = target.width - hostRect.width;
        float overflowY = target.height - hostRect.height;
        if (overflowX <= hostRect.width * 0.01f && overflowY <= hostRect.height * 0.01f)
            return;
        e.OverflowWarned = true;
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' draws content larger than its host frame "
                          + $"({target.width:F0}x{target.height:F0} vs {hostRect.width:F0}x"
                          + $"{hostRect.height:F0} uGUI px). THE CONSEQUENCE: the capture frames the "
                          + "host rect exactly (so that the dial's OFF and ON geometry are identical), "
                          + "and content outside that frame is CROPPED from the supersampled image — "
                          + "it was visible before this build and is not now. Switch [WorldUI] "
                          + "PanelSupersample off if that content matters.");
    }

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
        return pxW >= 2f && pxH >= 2f;
    }

    private static bool TryRenderedSize(RectTransform? rect, Camera head, float eyeW, float eyeH,
        out float pixelsW, out float pixelsH)
    {
        pixelsW = 0f;
        pixelsH = 0f;
        if (rect == null)
            return false;
        rect.GetWorldCorners(Corners); // 0 = bottom-left, 1 = top-left, 3 = bottom-right
        Camera.MonoOrStereoscopicEye eye = XRSettings.isDeviceActive
            ? Camera.MonoOrStereoscopicEye.Left
            : Camera.MonoOrStereoscopicEye.Mono;
        Vector3 bl = head.WorldToViewportPoint(Corners[0], eye);
        Vector3 tl = head.WorldToViewportPoint(Corners[1], eye);
        Vector3 br = head.WorldToViewportPoint(Corners[3], eye);
        if (bl.z <= 0f || tl.z <= 0f || br.z <= 0f)
            return false;
        pixelsW = PixelDistance(bl, br, eyeW, eyeH);
        pixelsH = PixelDistance(bl, tl, eyeW, eyeH);
        return pixelsW >= 1f && pixelsH >= 1f;
    }

    private static float PixelDistance(Vector3 a, Vector3 b, float eyeW, float eyeH)
    {
        float dx = (b.x - a.x) * eyeW;
        float dy = (b.y - a.y) * eyeH;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
