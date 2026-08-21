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
        RenderTexture shownForBias = DisplayTexture(e);
        string sampling = SamplingSentence(e, head, shownForBias != null ? shownForBias.mipMapBias : 0f);
        float ms = e.Captures > 0 ? (float)(e.CaptureMs / e.Captures) : 0f;
        float sweepMs = e.Sweeps > 0 ? (float)(e.SweepMs / e.Sweeps) : 0f;
        float contentMs = e.ContentMeasures > 0 ? (float)(e.ContentMs / e.ContentMeasures) : 0f;
        // READ BACK FROM THE TEXTURE THE EYE ACTUALLY SAMPLES — never from what was requested. The
        // ModBuild 192 line reported "MSAA 4x" from a local variable while the mip state came from
        // the target, which is why the mip failure was visible in the log and the MSAA state was
        // not independently confirmed. Both come from the objects now.
        RenderTexture shown = DisplayTexture(e);
        int mips = shown != null ? shown.mipmapCount : 0;
        int captureAa = e.Rt != null ? e.Rt.antiAliasing : 0;

        Sb.Length = 0;
        Sb.Append("PANEL SUPERSAMPLE '").Append(e.Window).Append("': host rect ")
          .Append(e.HostRectAtMeasure.width.ToString("F0")).Append('x')
          .Append(e.HostRectAtMeasure.height.ToString("F0"))
          .Append(" uGUI px, CAPTURE FRAME ").Append(e.Frame.width.ToString("F0")).Append('x')
          .Append(e.Frame.height.ToString("F0"))
          .Append(e.ExpandX > 0.5f || e.ExpandY > 0.5f
              ? $" (GROWN by {e.ExpandX:F0}x{e.ExpandY:F0} px to cover content drawn outside the host "
                + $"frame{(e.ExpandClamped ? "; CLAMPED — content IS being cropped" : "")})"
              : " (= the host rect: no content draws outside it)")
          .Append(" -> RT ").Append(e.RtW).Append('x').Append(e.RtH)
          .Append(" (factor ").Append(e.Factor.ToString("F2")).Append("), capture MSAA ")
          .Append(captureAa)
          .Append("x -> mipped display target: mips ").Append(mips)
          .Append(mips > 1 ? " RESOLVED+GENERATED after every capture"
                           : e.MipFallback ? " NONE (mip target refused; showing the capture target)"
                                           : " NONE")
          .Append(", ").Append(shown != null ? shown.filterMode.ToString() : "?")
          .Append(" aniso ").Append(shown != null ? shown.anisoLevel : 0)
          .Append(", ").Append(Mb(e.VramBytes)).Append(" MB VRAM (session total ")
          .Append(Mb(_vramTotal)).Append(" of ").Append(Mb(MaxTotalVramBytes)).Append(" MB across ")
          .Append(Entries.Count).Append(" panel(s), cap ").Append(MaxPanels).Append("); ")
          .Append(sampling)
          .Append(" CAPTURE: ").Append(e.Captures).Append(" so far, every ")
          .Append(CaptureIntervalFrames).Append(" frame(s), ").Append(ms.ToString("F2"))
          .Append(" ms CPU submit each — which includes SUBMITTING the resolve blit and the mip "
                  + "generation but not their GPU time, so if frame time regresses in this build "
                  + "the first suspect is N panels x (one full-target blit + one mip chain) per "
                  + "frame, and the lever is MaxPanels or CaptureIntervalFrames, not this number. "
                  + "MOTION (this 10 s window): ").Append(e.MotionFrames)
          .Append(" frame(s) with a pose/scale/rect change out of ").Append(e.MotionTicks)
          .Append(" comparison(s), ").Append(e.GeometryDirtyEvents)
          .Append(" of them a RESIZE/RESCALE that forced a re-measure; ").Append(e.Reallocations)
          .Append(" re-allocation(s) since engage, currently ").Append(IsMoving(e) ? "MOVING" : "still")
          .Append(". INSTRUMENT SELF-CHECK: largest single-frame host step ")
          .Append(e.MaxStepWorld.ToString("F4")).Append(" world units = ")
          .Append((e.MaxStepWorld / Mathf.Max(e.WorldPerAuthoredPx, 1e-9f)).ToString("F2"))
          .Append(" authored px = ")
          .Append((e.MaxStepWorld / Mathf.Max(e.WorldPerAuthoredPx, 1e-9f)
                   / Mathf.Max(e.AuthoredPerRenderedPx, 1e-6f)).ToString("F2"))
          .Append(" RENDERED eye px, against an epsilon of ").Append(e.MotionEpsWorld.ToString("F4"))
          .Append(" world units = 0.50 authored px (the host measures ")
          .Append(e.WorldPerAuthoredPx.ToString("F5"))
          .Append(" world units per authored px). ISOLATION: private capture layer ").Append(e.Layer)
          .Append(" (pool ").Append(PoolSize).Append(", ").Append(FreeLayers.Count)
          .Append(" free), display quad sortingOrder ")
          .Append(e.DisplayCanvas != null ? e.DisplayCanvas.sortingOrder : -1).Append("; ")
          .Append(ForeignPanelsInFrustum(e))
          .Append(" other supersampled panel(s) currently inside THIS camera's frustum. LAYERS: ")
          .Append(e.Relayered.Count)
          .Append(" transform(s) on capture layer ").Append(e.Layer).Append(", ")
          .Append(e.LateJoiners).Append(" late joiner(s) swept since engage, ").Append(e.Sweeps)
          .Append(" sweep(s) at ").Append(sweepMs.ToString("F2")).Append(" ms each, ")
          .Append(e.ContentMeasures).Append(" content measure(s) at ")
          .Append(contentMs.ToString("F2")).Append(" ms each, ")
          .Append(e.ForeignSkipped).Append(" foreign render subtree(s) LEFT ALONE, ")
          .Append(e.ForeignLayerSkipped)
          .Append(" transform(s) left alone because ANOTHER panel owns their layer (expect 0), ")
          .Append(e.NestedCaptured).Append('/').Append(e.NestedTotal)
          .Append(" nested canvas(es) on the capture layer.");

        Sb.Append(" HOW TO READ THIS LINE — MODBUILD 194. (A) 'ISOLATION' IS THE NEW HEADLINE AND IT "
                  + "ANSWERS THE USER'S REPORT THAT ONE WINDOW WAS DRAWING ANOTHER INSIDE ITSELF "
                  + "(.planning/debug/window_merge.jpg: the floated quest card showing the merchant "
                  + "window's item rows in its own rect). ModBuild 193 resolved ONE capture layer for "
                  + "the whole mod and built every per-panel camera with that single bit, so each "
                  + "camera drew EVERY supersampled panel inside its frustum into its own render "
                  + "target — and that frustum is as deep as the window is tall, on windows standing "
                  + "side by side on an arc. Every panel now owns a PRIVATE layer out of a pool. READ "
                  + "IT LIKE THIS: 'private capture layer N' must be DIFFERENT on every panel's line "
                  + "in the same report window — if two lines ever show the same number, the pool is "
                  + "broken and the merge bug is back. 'K other supersampled panel(s) currently "
                  + "inside THIS camera's frustum' is NOT a defect in this build: those panels are on "
                  + "other layers and cannot be drawn here. It is the measurement of how often the "
                  + "193 bug WAS firing, so a non-zero K is the confirmation that this fix was "
                  + "load-bearing; a K that is permanently 0 on every panel means the shared layer "
                  + "cannot explain the photograph and the next round must look elsewhere (the test "
                  + "is the neighbour's HOST RECT as a world-space AABB against the frustum planes: "
                  + "it counts a box straddling a frustum corner as inside, and it cannot see a "
                  + "neighbour's content spilled outside its own rect, so read it as a magnitude and "
                  + "not as a proof — the proof is the private layer). 'display quad sortingOrder' "
                  + "is here because "
                  + "CanvasConversion rewrites every panel's draw order EVERY frame from its measured "
                  + "eye distance: two overlapping windows whose orders SWAP mid-drag would pop in "
                  + "front of each other, which is its own flicker and is not this class's to fix — "
                  + "if two panels ever print the same sortingOrder while overlapping, that is the "
                  + "finding. "
                  + "(B) 'INSTRUMENT SELF-CHECK' RETIRES A WRONG READING OF THE 193 LOG. The MOTION "
                  + "field was read as zero everywhere and the detector was declared blind. It is "
                  + "not: 23 of the 193 log's 226 state lines carry a non-zero motion count, up to "
                  + "542 frames of a 900-frame window, several reading 'currently MOVING'. THE "
                  + "CONSEQUENCE FOR THE NEXT ROUND IS THE OPPOSITE OF WHAT WAS ASSUMED: the "
                  + "per-frame layer sweep while moving, the forced re-measure and the reallocation "
                  + "check all DID run during real drags, and the user still reported the movement "
                  + "shimmer unchanged — so those three remedies are FALSIFIED as its cause, not "
                  + "untested. The self-check makes the zero unambiguous from now on: 'N frame(s) "
                  + "with a change out of M comparison(s)' plus the largest single-frame step and the "
                  + "epsilon it was tested against, in world units AND authored px. M = 0 means the "
                  + "detector never ran. A step far ABOVE the epsilon with N = 0 means it is blind. A "
                  + "step below the epsilon with a large M means the window really was still. Those "
                  + "three used to print the same character. THE 'RENDERED eye px' FIGURE IN THAT "
                  + "FIELD IS THE ONE THAT DECIDES WHAT KIND OF DEFECT A DRAG CAN BE, and it is new. "
                  + "If a dragged window moves several RENDERED pixels per frame, a single frame of "
                  + "pose lag is several pixels of positional error and the complaint is JUDDER, "
                  + "which no filtering can fix and which would have to be chased in the pose path "
                  + "(the host is written from the hand in Update/LateUpdate while the HMD pose is "
                  + "late-latched immediately before the render). If it moves well under one "
                  + "rendered pixel per frame, pose lag is invisible by construction and the "
                  + "complaint can only be sub-pixel SAMPLING, which is what (C) measures. Two "
                  + "ordering explanations are already DEAD and must not be re-opened: the capture "
                  + "camera sits at depth -200 and every other camera in the 193 log sits at -1, 0 "
                  + "or 1, so the resolve blit and GenerateMips in its onPostRender always complete "
                  + "before the head camera culls; and the capture camera is a CHILD of the host, so "
                  + "a pure translation cannot move the panel inside the captured image at all. "
                  + "(C) 'trilinear MIP LOD' IS THE LIVE HYPOTHESIS FOR THE REMAINING MOVING "
                  + "SHIMMER, now that mips are confirmed present (193 read 'mips 10' and 'mips 11' "
                  + "and the shimmer did not change, which kills the missing-mip-chain explanation "
                  + "outright). Trilinear does not REMOVE aliasing at a fractional LOD, it "
                  + "attenuates it: at 1.34 texels per rendered pixel the LOD is 0.42 and 58 % of "
                  + "every sample still comes from level 0, which at 1.34x minification is genuinely "
                  + "undersampled. A frozen alias pattern is invisible; the same pattern under a "
                  + "moving window crawls. Anisotropic filtering makes it worse for a panel facing "
                  + "the seat, because aniso selects a LOWER lod. HOW TO DECIDE IT NEXT ROUND: if the "
                  + "user still reports movement shimmer and this field reads a level-0 share above "
                  + "roughly 30 %, the levers in order of cost are mipMapBias (+0.5 to +1.0 on the "
                  + "display target — one line in AttachMipTarget, and it costs sharpness while "
                  + "still) and [WorldUI] PanelSupersampleFactor above 1.0 (which buys texels until "
                  + "the ratio drops below 1.0, at which point there is no minification left to "
                  + "alias and this hypothesis is dead by construction). If the field already reads "
                  + "0 % and the shimmer persists, the panel surface is fully band-limited and the "
                  + "cause is not on it. "
                  + "(0) 'mips' IS THE OLD HEADLINE. ModBuild 192 "
                  + "read 'mips 1 NONE' on every panel because a render target cannot be "
                  + "multisampled AND mipmapped, so the whole mip half of this design never ran and "
                  + "the eye was still minifying an unfiltered texture — invisible while everything "
                  + "was still, crawling the moment the window or the head moved, which is exactly "
                  + "what the user reported as 'flackert beim Verschieben'. The capture is now "
                  + "resolved into a second single-sample target and the chain is generated THERE. "
                  + "If this field reads anything other than a number well above 1, that fix is not "
                  + "working and the movement shimmer WILL still be reported. (0b) 'CAPTURE FRAME' "
                  + "is the second headline: if it reads GROWN, this window draws outside its own "
                  + "host rect and ModBuild 192 was CROPPING that content away — the growth is the "
                  + "fix, and its cost is that this window's ON geometry is larger than its OFF "
                  + "geometry by exactly that many uGUI pixels of transparent margin. If it reads "
                  + "CLAMPED, content is STILL being cropped and MaxContentExpansion is the dial. "
                  + "(0c) MOTION is how the next round decides the release case: 'frame(s) with a "
                  + "pose/scale/rect change' counts drags, 'forced a re-measure' counts the "
                  + "resize/rescale events that re-derived the frame and the projection, and "
                  + "'re-allocation(s)' counts how often the render target had to follow. If the "
                  + "user still reports wrong or missing elements after a release and this line "
                  + "shows re-measures and re-allocations happening, the frame is NOT the cause and "
                  + "the next suspect is the layer sweep — read 'late joiner(s)' and the "
                  + "nested-canvas ratio below. THE 193 EDITION OF THIS CLAUSE SAID that zero motion "
                  + "frames in a session with real drags would mean a blind instrument; that reading "
                  + "was applied to the 193 log and it was WRONG, because zero is what MOST report "
                  + "windows show even in a session full of dragging — only the panel actually being "
                  + "dragged counts frames, and 23 of 226 lines did. Use the INSTRUMENT SELF-CHECK "
                  + "field in (B) to decide this, never the bare zero. "
                  + "(1) RT texels per rendered pixel is the same quantity "
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
                  + "character will draw in the world in front of the quad instead of inside it. A "
                  + "HIGH count here (the 192 log shows 'UI Shop Item Window' reaching 44 as items "
                  + "pool in) means most of that window's art is NOT going through this path at all: "
                  + "it is neither captured nor band-limited nor supersampled, so it keeps shimmering "
                  + "however good the rest of the window looks, and it is also excluded from the "
                  + "CAPTURE FRAME above — deliberately, because framing pixels that are guaranteed "
                  + "empty would waste the target and pull the frame off the content that IS "
                  + "captured. Excluding it cannot LOSE it: the head camera still draws it directly "
                  + "at its true world pose, exactly as before this path existed. "
                  + "(6) 'nested canvas(es) on the capture layer' must read N/N: a nested canvas left "
                  + "behind would be drawn into the eye directly AND be missing from the capture, "
                  + "i.e. one sharp panel with one shimmering sub-panel on top of it. (7) If the "
                  + "TEXT reads slightly THINNER, or the window looks darker during its ~0.3 s show "
                  + "animation, that is the known and bounded premultiplied-alpha artifact of "
                  + "compositing a transparent render target with the standard UI blend (derived in "
                  + "full at PanelSupersample.BuildDisplay) — it affects PARTIAL coverage only, is "
                  + "exact at alpha 0 and 1, and is a one-line change if it matters. (8) 'late "
                  + "joiner(s)' is the ModBuild 193 answer to 'manche Elemente fehlen im Fenster': "
                  + "every transform counted there arrived on the GAME's UI layer after this panel "
                  + "was engaged, which for however many frames it took to sweep it meant MISSING "
                  + "from the capture and drawn straight into the eye at its own sorting order. The "
                  + "sweep now runs EVERY frame while the window is moving, because that is exactly "
                  + "when GrabbableModal.ThrottleDiagWhileMoving switches the panel's per-frame "
                  + "nested-canvas adoption off. A large late-joiner count with the user reporting "
                  + "the problem GONE confirms the mechanism; a large count with the problem still "
                  + "present means the sweep is not fast enough and the next step is to hook the "
                  + "arrivals rather than to poll for them.");
        VRLog.Info(Scope, Sb.ToString());
        Sb.Length = 0;

        // The MOTION counters are per report window on purpose: "this window was dragged in the
        // last ten seconds" is a decidable statement, "this window has been dragged 4000 times
        // since it opened" is not. Everything else stays cumulative.
        e.MotionFrames = 0;
        e.GeometryDirtyEvents = 0;
        e.MotionTicks = 0;
        e.MaxStepWorld = 0f;
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
    private static string SamplingSentence(Entry e, Camera? head, float shownBias)
    {
        if (head == null)
            return "SAMPLING NOT MEASURED (no head camera);";
        if (!TryEyeTarget(out float eyeW, out float eyeH))
            return "SAMPLING NOT MEASURED (no readable per-eye render target);";
        if (!TryRenderedSize(e.Panel.HostRect, head, eyeW, eyeH, out float pxW, out float pxH))
            return "SAMPLING NOT MEASURED (the window is behind the eye or sub-pixel this scan);";
        // Measured against the HOST RECT, not the capture frame, so the number stays directly
        // comparable with what PANEL SAMPLING reported for the same window before this path existed
        // — that comparability is the whole point of duplicating the probe's arithmetic here. RT
        // texels per AUTHORED pixel is the factor by construction (RtW = Frame.width * Factor and
        // the frame's pixels are the host's pixels), so the texel figure is the authored figure
        // scaled by the factor whether or not the frame grew past the host rect.
        float authoredPerPixel = Mathf.Max(e.HostRectAtMeasure.width / Mathf.Max(pxW, 0.01f),
            e.HostRectAtMeasure.height / Mathf.Max(pxH, 0.01f));
        e.AuthoredPerRenderedPx = authoredPerPixel;
        float texelsPerPixel = authoredPerPixel * e.Factor;
        // THE MIP LOD THIS MINIFICATION ACTUALLY SELECTS, and how much UNFILTERED level 0 survives
        // it. This is the ModBuild 194 addition and it exists to make one specific hypothesis about
        // the residual moving shimmer decidable from the log instead of arguable: trilinear does NOT
        // remove aliasing at a fractional LOD, it ATTENUATES it. At t texels per pixel the LOD is
        // log2(t); for 0 < LOD < 1 the hardware blends level 0 — which is minified by t and is
        // therefore genuinely undersampled — with level 1, weighting level 0 by (1 - LOD). At the
        // ModBuild 193 log's measured 1.34 texels per pixel that is LOD 0.42 and 58 % of every
        // sample coming from an aliased level. A frozen alias pattern is invisible; the same pattern
        // under a moving window crawls, which is the exact shape of "flackert beim Verschieben".
        // Anisotropic filtering makes this WORSE for a near-frontal panel, because it lowers the
        // selected LOD towards the minor axis' rate — aniso is bought for the map room's windows
        // yawed up to 85 degrees, and it costs band-limiting on the ones facing the seat.
        float lod = Mathf.Max(0f, Mathf.Log(Mathf.Max(texelsPerPixel, 1e-4f), 2f));
        float level0Weight = lod >= 1f ? 0f : 1f - lod;
        float bias = shownBias;
        return $"drawn into {pxW:F0}x{pxH:F0} rendered px through the LEFT eye of a "
               + $"{eyeW:F0}x{eyeH:F0} per-eye target ({XRSettings.stereoRenderingMode}, viewportScale "
               + $"{XRSettings.renderViewportScale:F2}) = {texelsPerPixel:F2} RT texels per rendered "
               + $"pixel, against {authoredPerPixel:F2} authored px per rendered px (which is what "
               + "PANEL SAMPLING reported for this window before this build, and is now filtered "
               + $"rather than point-sampled) -> trilinear MIP LOD {lod:F2} at mipMapBias "
               + $"{bias:F2}, so {level0Weight * 100f:F0} % of every texture sample still comes from "
               + $"UNFILTERED level 0 at {texelsPerPixel:F2}x minification;";
    }

    // THE ModBuild 192 OVERFLOW WARN IS GONE, and deliberately so. It compared the host rect with
    // `ConvertedPanel.Target.rect` — the game window's own authored rect, which for a full-screen
    // window is 1920x1080 whether or not anything is drawn out there — latched after one hit, and
    // then told the reader that the content was being cropped and their only option was to switch
    // the feature off. All three parts were wrong: the comparison was against the wrong rectangle,
    // the latch meant the reading could never be re-checked (in the 192 log it fired at engage time
    // against the PRE-fit 328x1080 host rect and the fit then grew the host to 1920x1080 four
    // seconds later, which the warn could no longer retract), and there is no longer anything to
    // switch off — MeasureFrame now grows the capture frame to cover the overspill instead of
    // cropping it. The live, measured, un-latched replacement is the "CAPTURE FRAME ... GROWN /
    // CLAMPED" field of the state line above, and the one remaining loss case (the expansion clamp)
    // warns from MeasureFrame itself, where the numbers actually are.

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
