// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). The whole design argument, and every reason this class exists at all,
// lives in the class doc at the top of PanelSupersample.1.Core.cs; it is deliberately not
// restated here.

using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class PanelSupersample
{
    // ---- allocation ---------------------------------------------------------------------------

    /// <summary>
    /// Estimated VRAM for ONE panel: the capture target (colour + 24/8 depth-stencil + the
    /// multisample colour and depth surfaces when MSAA is on) PLUS the separate single-sample
    /// display target and its mip chain. Deliberately an over-estimate rather than an
    /// under-estimate — a budget that lies low is not a budget.
    /// <para>Worked example, and the one the caps are sized from: at 1920x1080 MSAA 4x the capture
    /// side is 7.9 (colour) + 7.9 (D24S8) + 63.3 (4 samples of both) = 79.1 MB and the display side
    /// is 7.9 + 2.6 (mips) = 10.5 MB, i.e. 89.7 MB for the pair. The ModBuild 192 log's 81.7 MB for
    /// the same window is the same arithmetic WITHOUT the split (mips were charged to the
    /// multisampled target, where they never existed).</para>
    /// </summary>
    private static long VramBytesFor(int w, int h, int msaa)
        => CaptureVramBytesFor(w, h, msaa) + MipVramBytesFor(w, h);

    private static long CaptureVramBytesFor(int w, int h, int msaa)
    {
        long px = (long)w * h;
        long colour = px * 4;
        long depthStencil = px * 4;
        long multisample = msaa > 1 ? (colour + depthStencil) * msaa : 0;
        return colour + depthStencil + multisample;
    }

    private static long MipVramBytesFor(int w, int h)
    {
        long colour = (long)w * h * 4;
        return colour + colour / 3;
    }

    private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

    /// <summary>
    /// Allocate the CAPTURE target — multisampled, with a stencil buffer, and deliberately WITHOUT
    /// mips. The descriptor comes from <c>FlatScreenStereo.CreateColorRt</c> — the shipped factory of
    /// this codebase's other RT path — because it is the one place that forces
    /// <c>D24_UNorm_S8_UInt</c> for a depth request, and a uGUI window without a STENCIL buffer loses
    /// every <see cref="Mask"/> in it (scroll viewports, circular avatars, the whole masked-content
    /// family).
    ///
    /// <para><b>WHY NO MIPS HERE — this is the ModBuild 192 bug.</b> That build asked this very
    /// method for <c>antiAliasing = 4</c> AND <c>useMipMap = true</c> on one target. A render texture
    /// cannot be both: <c>Create()</c> succeeded (so nothing failed loudly, and the stand-down below
    /// never fired) and the mip request was dropped on the floor — every hardware state line read
    /// <c>mips 1 NONE</c> and this class's own falsifier fired twice. <c>GenerateMips()</c> on a
    /// multisampled target is a no-op for the same reason. The mip chain now lives on a second,
    /// single-sample target (<see cref="CreateMipRt"/>) that the capture is RESOLVED into
    /// (<see cref="ResolveAndMip"/>) — which is the standard way to have both, and the same order an
    /// offline renderer uses: multisample, resolve, then band-limit.</para>
    /// </summary>
    private static RenderTexture? CreateRt(int w, int h, ref int msaa, string window)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            RenderTexture rt = FlatScreenStereo.CreateColorRt(w, h, 24, $"GloomhavenVR.PanelSS_{window}");
            rt.antiAliasing = Mathf.Max(1, msaa);
            rt.useMipMap = false;      // see the header: MSAA and mips are mutually exclusive
            rt.autoGenerateMips = false;
            rt.filterMode = FilterMode.Bilinear; // only ever read by the resolve blit
            rt.anisoLevel = 0;
            rt.wrapMode = TextureWrapMode.Clamp;
            if (rt.Create())
                return rt;
            Object.Destroy(rt);
            if (msaa <= 1)
                break;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the driver refused a {w}x{h} render target at MSAA "
                              + $"{msaa}x for '{window}' — retrying without MSAA. THE CONSEQUENCE if "
                              + "the retry also fails: this window keeps today's direct rendering.");
            msaa = 1;
        }
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE stands down on '{window}': a {w}x{h} render target could "
                          + "not be created at any MSAA level. THE CONSEQUENCE: this window keeps "
                          + "today's direct rendering — the same shimmer as before this build. "
                          + "Nothing was changed on the window itself.");
        return null;
    }

    /// <summary>
    /// Allocate the MIPPED DISPLAY target — single-sample, no depth, full mip chain, trilinear +
    /// anisotropic. Single-sample is what makes <c>useMipMap</c> stick at all (see
    /// <see cref="CreateRt"/>); no depth buffer because nothing is ever rendered into it, only
    /// blitted. <c>autoGenerateMips</c> is off on purpose so the chain is generated exactly once per
    /// capture, at a moment this class chooses, rather than at whatever moment Unity would pick.
    /// <para>Returns null (never throws) if the driver refuses it; the caller then displays the
    /// capture target directly, which is ModBuild 192's behaviour, and says so once.</para>
    /// </summary>
    private static RenderTexture? CreateMipRt(int w, int h, string window)
    {
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
        {
            name = $"GloomhavenVR.PanelSSMip_{window}",
            antiAliasing = 1,
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Trilinear,
            anisoLevel = AnisoLevel,
            wrapMode = TextureWrapMode.Clamp,
        };
        if (rt.Create() && rt.mipmapCount > 1)
            return rt;
        int got = rt.mipmapCount;
        rt.Release();
        Object.Destroy(rt);
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{window}' could not get a MIPPED display target "
                          + $"({w}x{h}, single-sample, no depth — the driver reported mipmapCount="
                          + $"{got}). THE CONSEQUENCE: the quad falls back to showing the "
                          + "multisampled capture target directly, i.e. exactly ModBuild 192's "
                          + "behaviour — sharp while still, and crawling while the window or the head "
                          + "moves, because the eye minifies an unfiltered texture. Input, geometry "
                          + "and MSAA are unaffected.");
        return null;
    }

    /// <summary>Give <paramref name="e"/> a mipped display target for its current capture size, or
    /// record the fallback. Never throws; the entry is usable either way.</summary>
    private static void AttachMipTarget(Entry e, int w, int h)
    {
        RenderTexture? mip = CreateMipRt(w, h, e.Window);
        if (mip == null)
        {
            e.MipRt = null!;
            e.MipFallback = true;
            e.MipCount = e.Rt != null ? e.Rt.mipmapCount : 1;
            // The pair's estimate already charged for a mip target we did not get; hand it back so
            // the session total stays a true statement about what is allocated.
            e.VramBytes = CaptureVramBytesFor(w, h, e.Msaa);
            return;
        }
        e.MipRt = mip;
        e.MipFallback = false;
        e.MipCount = mip.mipmapCount;
    }

    /// <summary>The texture the display quad shows: the mipped one when we have it, else the
    /// capture target (the ModBuild 192 fallback).</summary>
    private static RenderTexture DisplayTexture(Entry e) => e.MipRt != null ? e.MipRt : e.Rt;

    /// <summary>
    /// The capture camera, parented UNDER the host so its pose is exact by construction — whoever
    /// moves the window (the grab, the order ladder, a board dock, the pose re-place) moves camera
    /// and content together, and the captured image is invariant to all of it. That removes the whole
    /// class of one-frame pose-lag bugs a world-space follower camera would have. It also puts the
    /// camera inside the subtree the layer sweeps walk, which is safe: a transform carrying a
    /// <see cref="Camera"/> is skipped by <c>CanvasConversion.ApplyModLayer</c>'s own rule and by
    /// ours, and it is neither a <see cref="Canvas"/> nor a <see cref="Renderer"/>, so the render-hide
    /// sweep does not see it either.
    /// </summary>
    private static bool BuildCamera(Entry e, ConvertedPanel panel)
    {
        var go = new GameObject($"GloomhavenVR.PanelSSCam_{e.Window}");
        go.transform.SetParent(panel.HostRect, worldPositionStays: false);
        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent: the window keeps per-pixel alpha
        // THE ISOLATION GUARANTEE IN ONE LINE: this camera's mask is this panel's PRIVATE pool layer
        // and nothing else, so no other supersampled window can be inside what it captures however
        // close it stands or however deep the ortho slab is. ModBuild 193 wrote the ONE shared
        // capture layer here and that is the whole of the "quest window shows the merchant window"
        // defect — see PanelSupersample.5.Isolation.cs.
        cam.cullingMask = e.Layer >= 0 ? 1 << e.Layer : 0;
        cam.depth = -200f;                                // renders before every other camera in the frame
        cam.stereoTargetEye = StereoTargetEyeMask.None;   // mono, once per frame — never part of an eye pass
        cam.renderingPath = RenderingPath.Forward;        // uGUI only; a G-buffer here would be pure cost
        cam.allowHDR = false;
        cam.allowMSAA = true;
        cam.allowDynamicResolution = false;
        cam.useOcclusionCulling = false;
        cam.targetTexture = e.Rt;
        cam.enabled = true;
        e.CamGo = go;
        e.Cam = cam;
        return true;
    }

    /// <summary>
    /// The display: a mod-owned world-space canvas carrying one <see cref="RawImage"/> of the capture
    /// target, on the MOD layer (so the head camera draws it and the game's UI Camera never can), at
    /// the host rect's exact world pose and size. A SCENE ROOT, not a child of the host, for two
    /// reasons: a nested <see cref="Canvas"/> inside a converted subtree is adopted by
    /// <c>CanvasConversion.AdoptNestedCanvases</c> (which would merge it into the panel's hit-testing
    /// and re-write its sorting), and the capture camera must not be able to see it at all. It rides
    /// the far-to-near draw ladder as an offset-0 order follower, i.e. in its panel's own slot,
    /// exactly where the host canvas would have drawn.
    /// <para><c>raycastTarget</c> is false: this quad is a photograph, never a hit surface. The hit
    /// surface is, and remains, the real host canvas underneath it.</para>
    ///
    /// <para><b>THE ALPHA MATH, stated because it has one known, bounded artifact and a future round
    /// must not have to re-derive it.</b> The capture clears to transparent black and uGUI blends
    /// into it with <c>SrcAlpha, OneMinusSrcAlpha</c>, so a pixel of coverage <i>a</i> and colour
    /// <i>C</i> lands in the render target PREMULTIPLIED, as <c>(aC, a)</c>. The RawImage then
    /// composites it with the standard UI blend, which multiplies by alpha a SECOND time:
    /// <c>a·(aC) + (1-a)·dst = a²C + (1-a)·dst</c>. Fully opaque pixels (<i>a</i>=1) and fully
    /// transparent ones (<i>a</i>=0) are therefore EXACT — which is almost the whole of a floated
    /// window, because the modal family's full-window opaque backing is deliberately disabled
    /// (<c>ConvertedPanel.HideBackground</c>) and what remains is opaque content on nothing. Only
    /// PARTIAL coverage — SDF glyph edges, a CanvasGroup mid-fade — comes out darker than it should,
    /// which reads as very slightly thinner text and a slightly darker window during the ~0.3 s show
    /// animation. Correcting it needs <c>Blend One OneMinusSrcAlpha</c>, which no built-in UI shader
    /// offers and which this lane refuses to gamble a bundled/legacy <c>Shader.Find</c> on after nine
    /// failed rounds ("Shader.Find only sees loaded shaders"). It is also the exact composite
    /// <see cref="FlatScreen"/>'s split mode already ships and the user has already accepted.</para>
    /// </summary>
    private static bool BuildDisplay(Entry e, ConvertedPanel panel)
    {
        var go = new GameObject($"GloomhavenVR.PanelSS_{e.Window}");
        var rect = go.AddComponent<RectTransform>();
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        canvas.sortingOrder = panel.DrawSortingOrder;
        var image = go.AddComponent<RawImage>();
        image.texture = DisplayTexture(e);
        image.raycastTarget = false;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        VRLayers.Apply(go);
        e.DisplayGo = go;
        e.DisplayRect = rect;
        e.DisplayCanvas = canvas;
        e.DisplayImage = image;
        CanvasConversion.RegisterOrderFollower(panel, canvas, 0);
        return true;
    }

    // ---- per-frame sync -----------------------------------------------------------------------

    /// <summary>
    /// Keep the capture frustum, the display quad and the allocation on the panel's live CAPTURE
    /// FRAME (<see cref="Entry.Frame"/> — the host rect unioned with everything the window actually
    /// draws; see <see cref="MeasureFrame"/> for why it is not simply the host rect). The camera is
    /// a CHILD of the host, so only its projection needs writing (world-unit ortho size and aspect,
    /// recomputed from the live frame and lossy scale — the diorama scale is ~198 world units per
    /// real metre in the map room, and every quantity here is in WORLD units, never metres). The
    /// display quad is a scene root, so its full pose is copied; the final copy happens in
    /// <see cref="OnPreCull"/>, after every LateUpdate pose writer has run.
    ///
    /// <para>ORDERING, stated because ModBuild 192's residual defect was a movement one and the next
    /// round must not have to re-derive this. Within a frame: every Update runs (including
    /// <c>GrabbableModal.Tick</c>, which copies the grab frame onto the host), then every LateUpdate
    /// runs (including <c>GrabbableModal.LateSyncHost</c>, an ordinary MonoBehaviour LateUpdate with
    /// no defined order against this one, and this method via <c>CanvasConversion.LateTick</c>), and
    /// only THEN does the camera loop start. So a host pose written by ANY LateUpdate — before or
    /// after this method — is already final when the capture camera renders, because that camera is
    /// a CHILD of the host and reads the host's world matrix at render time, and it is already final
    /// when <see cref="OnPreCull"/> copies the quad's pose, because that runs inside the camera loop
    /// too. Capture content and quad pose therefore cannot disagree by a frame, whichever LateUpdate
    /// won. What this method WOULD be one frame late on is the PROJECTION: the capture camera's
    /// <c>orthographicSize</c> is a WORLD-unit quantity derived from the host's lossy scale, so a
    /// two-hand resize whose <c>LateSyncHost</c> lands after this method would leave the camera
    /// framing the previous frame's world size while rendering at the new one — the captured image
    /// would zoom by the scale ratio for that frame, inside a display quad that (reading the scale
    /// live in <see cref="OnPreCull"/>) is already correct. That is precisely a "manche Elemente
    /// nicht richtig dargestellt" artifact, and it is why <see cref="SyncProjection"/> is called
    /// AGAIN from the capture camera's own <see cref="Camera.onPreCull"/> — the last instant before
    /// it culls, after every LateUpdate in the frame, whoever won. The ALLOCATION can still trail a
    /// resize by a frame, which costs resolution and never geometry.</para>
    /// </summary>
    private static void SyncGeometry(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform host = panel.HostRect;

        // THE RESIZE / REPOSE TRIGGER (ModBuild 193). Read entirely from the host transform and the
        // host RectTransform, because that is where every writer lands: the content fit writes
        // HostRect.sizeDelta (CanvasConversion.3.Fit.cs, which computes its own `resized` flag right
        // there), GrabbableModal.SyncHostToFrame writes the host's position, rotation and localScale
        // every frame from the grab frame, and the release re-face
        // (GrabbableModal.IPanelGrabOwner.OnGrabFinished) writes the rotation once. Watching the
        // resulting VALUES catches all of them without coupling to any of those files — the same
        // argument CanvasConversion's own reveal gate makes for value-based stillness.
        bool dirty = NoticeGeometry(e, host);

        // Re-measure the capture frame on the cadence, and IMMEDIATELY on any geometry change: a
        // window that was re-fitted or re-scaled on release must not keep framing last size's
        // content. This is the "a resize must force a full re-capture" requirement — the capture
        // itself runs every frame, so forcing it means forcing everything the capture is derived
        // from: the frame, the projection, the allocation and the layer sweep.
        bool forced = dirty
                      && Time.frameCount - e.LastMeasureFrame >= ContentMeasureMinIntervalFrames;
        if (forced || Time.frameCount >= e.NextContentFrame)
        {
            e.NextContentFrame = Time.frameCount + ContentMeasureIntervalFrames;
            MeasureFrame(e);
        }

        SyncProjection(e);
        e.DisplayRect.sizeDelta = e.Frame.size;
        SyncDisplayPose(e);
        Rect frame = e.Frame;

        // Re-allocate when the frame materially resized: an RT built for the pre-fit frame would
        // either waste texels or stretch across the new one, and the whole point of this path is
        // that one RT texel corresponds to one authored pixel times the factor. A geometry change
        // forces the check regardless of the fraction, so a release-time re-fit can never leave a
        // stale target behind.
        if (dirty
            || Mathf.Abs(frame.width - e.Authored.x) > e.Authored.x * RectChangeFraction
            || Mathf.Abs(frame.height - e.Authored.y) > e.Authored.y * RectChangeFraction)
        {
            Reallocate(e, frame);
        }
    }

    /// <summary>
    /// Write the capture camera's projection from the entry's frame and the host's LIVE lossy scale.
    /// Called twice a frame on purpose — once from <see cref="SyncGeometry"/> (so a camera that
    /// somehow never gets a pre-cull callback is still correct) and once from the capture camera's
    /// own <see cref="Camera.onPreCull"/>, which is the last instant before it culls and therefore
    /// the only place a late LateUpdate scale write cannot beat. See <see cref="SyncGeometry"/>'s
    /// ORDERING paragraph for the artifact this second call removes. Both writes are idempotent.
    /// <para>Everything here is in WORLD units (the map room runs ~198 world units per real metre);
    /// <c>Frame</c> is in host-local uGUI pixels and the host's lossy scale is the bridge.</para>
    /// </summary>
    private static void SyncProjection(Entry e)
    {
        RectTransform? host = e.Panel.HostRect;
        if (host == null || e.Cam == null || e.CamGo == null)
            return;
        Rect frame = e.Frame;
        float scale = Mathf.Max(Mathf.Abs(host.lossyScale.y), 1e-6f);
        float frameHeightWorld = frame.height * scale;
        float slab = Mathf.Max(frameHeightWorld * 0.5f, 1e-4f);
        float standoff = slab * 2f;

        e.Cam.orthographicSize = frameHeightWorld * 0.5f;
        e.Cam.aspect = Mathf.Max(frame.width / Mathf.Max(frame.height, 1e-4f), 1e-4f);
        e.Cam.nearClipPlane = standoff - slab;
        e.Cam.farClipPlane = standoff + slab;
        Vector2 centre = frame.center;
        e.CamGo.transform.localPosition = new Vector3(centre.x, centre.y, -standoff / scale);
        e.CamGo.transform.localRotation = Quaternion.identity;
        e.CamGo.transform.localScale = Vector3.one;
    }

    /// <summary>
    /// Did the host's pose, scale or rect change since the last frame? Records the motion stamp that
    /// drives the per-frame layer sweep (<see cref="SweepAfterMotionFrames"/>) and returns true for
    /// the changes that invalidate the capture frame (rect or scale), NOT for a pure translation —
    /// moving a window changes nothing about what it draws or how big its render target must be.
    /// <para>The position epsilon is expressed in WORLD units and derived from the panel's own
    /// lossy scale (half an authored pixel), never in metres: at the map room's ~198 world units per
    /// metre a fixed metric epsilon would be either blind or permanently tripped.</para>
    ///
    /// <para><b>THIS INSTRUMENT IS NOT BLIND, AND ModBuild 194 MAKES THE LOG SAY SO ITSELF.</b> It
    /// was suspected of never firing, on the reading that the ModBuild 193 log's MOTION field was
    /// zero everywhere. It is not: 23 of that log's 226 state lines carry a non-zero count, the
    /// largest being 542 frames of a 900-frame report window, and several read
    /// <c>currently MOVING</c>. The reason a reader could believe otherwise is that a bare zero
    /// carries no evidence about WHY it is zero — a still window, a detector that never ran and a
    /// detector whose epsilon swallowed the drag all print the same character. So this method now
    /// also records how many comparisons it made (<see cref="Entry.MotionTicks"/>), the largest
    /// single-frame world-space step it saw (<see cref="Entry.MaxStepWorld"/>) and the epsilon that
    /// step was tested against (<see cref="Entry.MotionEpsWorld"/>), and the report prints all three
    /// in world units AND in authored uGUI pixels. A blind detector is then a large step with a zero
    /// count; a still window is a step below the epsilon with a large comparison count; a detector
    /// that never ran is a zero comparison count. The three are no longer confusable.</para>
    /// </summary>
    private static bool NoticeGeometry(Entry e, RectTransform host)
    {
        Transform t = host.transform;
        Vector3 pos = t.position;
        Quaternion rot = t.rotation;
        Vector3 scl = host.lossyScale;
        Vector2 size = host.rect.size;

        if (!e.HasPoseSnapshot)
        {
            e.HasPoseSnapshot = true;
            e.LastPos = pos;
            e.LastRot = rot;
            e.LastScale = scl;
            e.LastRectSize = size;
            return false;
        }

        float posEps = Mathf.Max(Mathf.Abs(scl.y) * 0.5f, 1e-6f);
        // The self-check, recorded BEFORE the snapshot is overwritten and regardless of the verdict.
        e.MotionTicks++;
        e.MotionEpsWorld = posEps;
        e.WorldPerAuthoredPx = Mathf.Abs(scl.y);
        float step = (pos - e.LastPos).magnitude;
        if (step > e.MaxStepWorld)
            e.MaxStepWorld = step;
        bool moved = (pos - e.LastPos).sqrMagnitude > posEps * posEps;
        bool turned = Quaternion.Angle(rot, e.LastRot) > 0.05f;
        bool rescaled = (scl - e.LastScale).magnitude > e.LastScale.magnitude * 1e-4f + 1e-9f;
        bool resized = Mathf.Abs(size.x - e.LastRectSize.x) > 0.5f
                       || Mathf.Abs(size.y - e.LastRectSize.y) > 0.5f;

        e.LastPos = pos;
        e.LastRot = rot;
        e.LastScale = scl;
        e.LastRectSize = size;

        if (moved || turned || rescaled || resized)
        {
            e.LastMotionFrame = Time.frameCount;
            e.MotionFrames++;
        }
        if (!rescaled && !resized)
            return false;
        e.GeometryDirtyEvents++;
        return true;
    }

    /// <summary>Is this window inside its motion window (moving, or settling from a move)? Drives
    /// the per-frame capture-layer sweep — see <see cref="SweepAfterMotionFrames"/>.</summary>
    private static bool IsMoving(Entry e) => Time.frameCount - e.LastMotionFrame <= SweepAfterMotionFrames;

    private static void SyncDisplayPose(Entry e)
    {
        RectTransform host = e.Panel.HostRect;
        if (host == null || e.DisplayGo == null)
            return;
        Transform t = e.DisplayGo.transform;
        t.SetPositionAndRotation(host.TransformPoint(e.Frame.center), host.rotation);
        t.localScale = host.lossyScale;
    }

    private static void Reallocate(Entry e, Rect frame)
    {
        int rtW = Mathf.Clamp(Mathf.RoundToInt(frame.width * e.Factor), 16, MaxRtDimension);
        int rtH = Mathf.Clamp(Mathf.RoundToInt(frame.height * e.Factor), 16, MaxRtDimension);
        if (rtW == e.RtW && rtH == e.RtH)
        {
            e.Authored = frame.size;
            return;
        }
        int msaa = e.Msaa;
        long budget = System.Math.Min(MaxPanelVramBytes,
            MaxTotalVramBytes - (_vramTotal - e.VramBytes));
        long vram = VramBytesFor(rtW, rtH, msaa);
        while (msaa > 1 && vram > budget)
        {
            msaa /= 2;
            vram = VramBytesFor(rtW, rtH, msaa);
        }
        if (vram > budget)
        {
            // Keep the existing target; the image merely resamples a little. Recording the new size
            // as `Authored` is what stops this from being re-tried (and re-logged) every frame.
            e.Authored = frame.size;
            return;
        }

        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, e.Window);
        if (rt == null)
        {
            e.Authored = frame.size; // as above: do not retry a refused allocation per frame
            return;
        }
        long previous = e.VramBytes;
        RenderTexture old = e.Rt;
        RenderTexture? oldMip = e.MipRt;
        e.Cam.targetTexture = rt;
        e.Rt = rt;
        e.Msaa = msaa;
        e.VramBytes = vram;                 // AttachMipTarget trims this if the mip target is refused
        AttachMipTarget(e, rtW, rtH);
        e.DisplayImage.texture = DisplayTexture(e);
        _vramTotal += e.VramBytes - previous;
        if (_vramTotal < 0)
            _vramTotal = 0;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Authored = frame.size;
        e.Reallocations++;
        if (old != null)
        {
            old.Release();
            Object.Destroy(old);
        }
        if (oldMip != null)
        {
            oldMip.Release();
            Object.Destroy(oldMip);
        }
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE re-allocated '{e.Window}' to {rtW}x{rtH} (MSAA {msaa}x, "
                          + $"mips {e.MipCount}, {Mb(e.VramBytes)} MB) after its capture frame changed "
                          + $"to {frame.width:F0}x{frame.height:F0} uGUI px (host rect "
                          + $"{e.HostRectAtMeasure.width:F0}x{e.HostRectAtMeasure.height:F0} + content "
                          + $"overspill {e.ExpandX:F0}x{e.ExpandY:F0}). A reallocation is REQUIRED for "
                          + "correctness, not just for sharpness: the target must match the frame the "
                          + "camera projects into it, or the window resamples through the wrong "
                          + "number of texels for as long as the mismatch lasts.");
    }

    /// <summary>
    /// The capture camera and the display follow the panel's own visibility exactly. A panel the
    /// render hide has switched off must not pay for a capture, and its quad must not keep showing
    /// the last frame it had.
    /// </summary>
    private static void SyncVisibility(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        bool visible = panel.HostCanvas != null && panel.HostCanvas.enabled
                       && !panel.RenderHidden && !panel.OwnerRenderHidden
                       && panel.HostGo != null && panel.HostGo.activeInHierarchy;
        bool due = Time.frameCount >= e.NextCaptureFrame;
        if (visible && due)
            e.NextCaptureFrame = Time.frameCount + CaptureIntervalFrames;
        if (e.Cam != null && e.Cam.enabled != (visible && due))
            e.Cam.enabled = visible && due;
        if (e.DisplayGo != null && e.DisplayGo.activeSelf != visible)
            e.DisplayGo.SetActive(visible);
    }

    // ---- the layer sweep ----------------------------------------------------------------------

    /// <summary>
    /// Move the panel's subtree onto the capture layer, WALKED not flattened, with the same rule
    /// <c>CanvasConversion.ApplyModLayer</c> uses: a subtree whose root carries a real
    /// <see cref="Renderer"/> or a <see cref="Camera"/> is skipped WHOLE — and so are its children,
    /// which is the entire point. uGUI draws through <c>CanvasRenderer</c>, which is not a
    /// <see cref="Renderer"/>, so a real Renderer inside a converted window is by definition NOT part
    /// of the UI: in the party/character windows it is the live 3D character rig, which the game
    /// renders with its own preview camera into a RenderTexture the window then shows as a
    /// <see cref="RawImage"/>. That camera culls BY LAYER, and moving the rig has already been
    /// shipped and reverted once ("relayering drew the character twice").
    /// <para>Our own capture camera IS inside this subtree (it is parented to the host so its pose is
    /// exact by construction) and is skipped by reference as well as by the Camera clause above. The
    /// display quad is a scene root and never appears in this walk at all — which is one of the two
    /// reasons it is not parented to the host; the other is that a nested <see cref="Canvas"/> inside
    /// a converted subtree gets adopted into the panel's hit-testing.</para>
    /// <para>Cost: the O(n) <see cref="IsRecorded"/> scan runs ONLY for a transform that is not yet on
    /// the capture layer, so a settled window costs one component walk and zero scans — the same
    /// shape, and the same reason, as <c>ApplyModLayer</c>'s own guard.</para>
    /// </summary>
    private static void ApplyCaptureLayer(Entry e, bool initial)
    {
        int layer = e.Layer;
        if (layer < 0 || e.Panel.HostGo == null)
            return;
        float sweepStart = Time.realtimeSinceStartup;
        Scratch.Clear();
        Scratch.Add(e.Panel.HostGo.transform);
        int moved = 0;
        int skipped = 0;
        int foreignLayer = 0;
        int nested = 0;
        int nestedOnLayer = 0;
        while (Scratch.Count > 0)
        {
            int last = Scratch.Count - 1;
            Transform t = Scratch[last];
            Scratch.RemoveAt(last);
            if (t == null)
                continue;
            if (ReferenceEquals(t, e.CamGo != null ? e.CamGo.transform : null))
                continue;
            bool isRoot = ReferenceEquals(t, e.Panel.HostGo.transform);
            if (!isRoot && (t.GetComponent<Renderer>() != null || t.GetComponent<Camera>() != null))
            {
                skipped++;
                continue; // and NOT its children either
            }
            // NEVER STEAL ANOTHER PANEL'S PRIVATE LAYER. The converted panels' subtrees are disjoint
            // today (each gets its own scene-root host GameObject), so this cannot fire; it is here
            // so the isolation guarantee does not rest on an invariant in a file this lane does not
            // own. If two entries ever did share a transform, both would rewrite its layer every
            // frame and the value would ALTERNATE — this project's "don't win a write war" failure,
            // and under MultiPass the two eyes would disagree about it. One mask test per transform
            // buys the guarantee outright. See IsForeignPoolLayer.
            // ONE PANEL'S FINGERPRINT ON A POOLED OBJECT IS NOT THE OTHER PANEL'S CLAIM
            // (ModBuild 195). The comment above assumed the converted subtrees are disjoint, so this
            // guard "cannot fire". IT FIRED — 18 of 30 report lines on 'Character Items Equipment
            // Content' in the ModBuild 194 log, and the `continue` skips the transform AND ITS WHOLE
            // SUBTREE, so the equipment tooltip's card art was never captured while its frame was:
            // a visible-but-empty box, which is the user's "so transparent, dass man sie kaum
            // erkennen kann". The cause is the game's SHARED CARD POOL: a card the shop tooltip used
            // arrives in the equipment window still carrying the shop panel's private layer. It is
            // OUR descendant now — the sweep only ever walks our own host — so the layer VALUE is a
            // stale fingerprint, not a competing owner.
            // WE TAKE IT, AND WE TAKE THE RECORD WITH IT. Claiming without transferring would have
            // been worse than the skip: our own restore would later write the SHOP's capture layer
            // onto it (that is what it would have found there), stranding the card on a layer no
            // camera renders. TryTakeForeignRecord moves the original game layer across, so exactly
            // one entry owns the object and the restore still hands back what the game gave.
            int observed = t.gameObject.layer;
            if (!isRoot && IsForeignPoolLayer(e, observed))
            {
                foreignLayer++;
                if (TryTakeForeignRecord(e, t, out int gameLayer))
                    observed = gameLayer;
                else
                    // No record to transfer: the other entry never wrote this object (it was pooled
                    // out and back before its sweep ran). We cannot know the game layer, so refuse
                    // rather than guess — a wrong restore is permanent, a skipped capture is one
                    // aliased element. Counted above so the report still names it.
                    continue;
            }
            if (!isRoot && t.GetComponent<Canvas>() != null)
            {
                nested++;
                if (t.gameObject.layer == layer)
                    nestedOnLayer++;
            }
            if (t.gameObject.layer != layer)
            {
                if (!IsRecorded(e, t))
                    e.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = observed });
                t.gameObject.layer = layer;
                moved++;
                if (!isRoot && t.GetComponent<Canvas>() != null)
                    nestedOnLayer++;
            }
            for (int i = t.childCount - 1; i >= 0; i--)
                Scratch.Add(t.GetChild(i));
        }
        Scratch.Clear();
        e.LayersMoved = e.Relayered.Count;
        e.ForeignSkipped = skipped;
        e.ForeignLayerSkipped = foreignLayer;
        e.NestedTotal = nested;
        e.NestedCaptured = Mathf.Min(nestedOnLayer, nested);
        e.Sweeps++;
        e.SweepMs += (Time.realtimeSinceStartup - sweepStart) * 1000.0;
        if (!initial && moved > 0)
        {
            e.LateJoiners += moved;
            VRLog.Info(Scope, $"PANEL SUPERSAMPLE: {moved} pooled/late transform(s) of '{e.Window}' "
                              + $"joined capture layer {layer} (a repopulating window brings children "
                              + "on the game's UI layer; until they are swept they would be MISSING "
                              + "from the capture and drawn straight into the eye instead — which is "
                              + "the ModBuild 192 'manche Elemente ... fehlen im Fenster' report). "
                              + $"This sweep ran because the window {(IsMoving(e) ? "is MOVING (per-frame "
                                  + "cadence)" : "reached its periodic cadence")}.");
        }
    }

    /// <summary>
    /// Move <paramref name="t"/>'s layer record from whichever OTHER entry owns it to us, and hand
    /// back the layer the GAME originally gave it. Returns false when no other entry has a record —
    /// see the call site for why that case refuses instead of guessing.
    /// </summary>
    private static bool TryTakeForeignRecord(Entry mine, Transform t, out int originalLayer)
    {
        originalLayer = 0;
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry other = Entries[i];
            if (ReferenceEquals(other, mine))
                continue;
            for (int r = 0; r < other.Relayered.Count; r++)
            {
                if (!ReferenceEquals(other.Relayered[r].Transform, t))
                    continue;
                originalLayer = other.Relayered[r].OriginalLayer;
                other.Relayered.RemoveAt(r);
                other.LayersMoved = other.Relayered.Count;
                return true;
            }
        }
        return false;
    }

    private static bool IsRecorded(Entry e, Transform t)
    {
        for (int i = 0; i < e.Relayered.Count; i++)
        {
            if (ReferenceEquals(e.Relayered[i].Transform, t))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Hand every recorded transform its original layer back — and ONLY while it is still on OUR
    /// layer. That guard is what makes this restore commute with
    /// <c>CanvasConversion.Release</c>'s: whichever runs first, the transform ends on the layer the
    /// game gave it, and neither restore can overwrite the other's work.
    /// </summary>
    private static void RestoreLayers(Entry e)
    {
        int layer = e.Layer;
        if (layer < 0)
        {
            e.Relayered.Clear();
            return;
        }
        for (int i = 0; i < e.Relayered.Count; i++)
        {
            LayerRecord record = e.Relayered[i];
            if (record.Transform != null && record.Transform.gameObject.layer == layer)
                record.Transform.gameObject.layer = record.OriginalLayer;
        }
        e.Relayered.Clear();
    }

    // ---- camera hooks -------------------------------------------------------------------------

    private static void InstallHooks()
    {
        if (_hooksInstalled)
            return;
        _hooksInstalled = true;
        Camera.onPreCull += OnPreCull;
        Camera.onPostRender += OnPostRender;
    }

    private static void UninstallHooks()
    {
        if (!_hooksInstalled)
            return;
        _hooksInstalled = false;
        Camera.onPreCull -= OnPreCull;
        Camera.onPostRender -= OnPostRender;
    }

    /// <summary>
    /// Clear the capture-layer bit from every camera that is not one of ours, for the duration of
    /// that camera's own render. WHY HERE and not on the head camera once per frame:
    /// <c>VRRigDriver.TickHeadCullingMask</c> re-writes the head mask every frame from the anchor
    /// camera's mask (0xFFFFFFFF in a scenario), so a mask edit from a tick would be reverted and
    /// re-applied forever — a write war, which this project has already paid for once. Editing
    /// inside the camera's own callback and handing the value back in
    /// <see cref="Camera.onPostRender"/> means no other writer ever observes a changed value, and
    /// MultiPass's two eye passes are treated identically because the pair fires once per pass.
    /// <para>The display quad's final pose is copied here too — the last moment in the frame, after
    /// every LateUpdate pose writer (grab, order ladder, board docks) has run.</para>
    /// </summary>
    private static void OnPreCull(Camera cam)
    {
        try
        {
            if (cam == null || Entries.Count == 0 || _poolMask == 0)
                return;
            Entry? mine = EntryForCamera(cam);
            if (mine != null)
            {
                mine.LastCaptureStart = Time.realtimeSinceStartup;
                // THE LAST INSTANT BEFORE THIS CAMERA CULLS. Re-deriving the projection here is what
                // makes a two-hand resize correct in the frame it happens: orthographicSize is a
                // WORLD-unit quantity read from the host's lossy scale, and GrabbableModal's own
                // LateUpdate host re-sync has no defined order against ours. See SyncGeometry's
                // ORDERING paragraph.
                SyncProjection(mine);
                return; // our own capture camera: it is the ONE camera that may see the layer
            }
            if (_poseSyncFrame != Time.frameCount)
            {
                _poseSyncFrame = Time.frameCount;
                for (int i = 0; i < Entries.Count; i++)
                    SyncDisplayPose(Entries[i]);
            }
            // THE UNION, not one bit: since ModBuild 194 every engaged panel holds its own pool
            // layer, so a camera that is not one of ours must lose ALL of them for the duration of
            // its render or a panel would be drawn into the eye as well as into its own capture.
            int bits = _poolMask;
            if ((cam.cullingMask & bits) == 0)
                return;
            MaskedCameras[cam] = cam.cullingMask;
            cam.cullingMask &= ~bits;
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the pre-cull mask guard");
        }
    }

    private static void OnPostRender(Camera cam)
    {
        try
        {
            if (cam == null)
                return;
            if (MaskedCameras.TryGetValue(cam, out int original))
            {
                cam.cullingMask = original;
                MaskedCameras.Remove(cam);
                return;
            }
            Entry? e = EntryForCamera(cam);
            if (e == null)
                return;
            ResolveAndMip(e);
            e.Captures++;
            if (e.LastCaptureStart > 0f)
                e.CaptureMs += (Time.realtimeSinceStartup - e.LastCaptureStart) * 1000.0;

            // THE FALSIFIER, kept from ModBuild 192 and now pointed at the texture the eye actually
            // samples. If this ever fires again, the mip half of this design is not running and the
            // window will crawl under motion no matter how sharp it looks while still.
            RenderTexture shown = DisplayTexture(e);
            if (!e.MipWarned && (shown == null || shown.mipmapCount <= 1))
            {
                e.MipWarned = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' is displayed from a render target "
                                  + $"with mipmapCount={(shown != null ? shown.mipmapCount : 0)} — NO "
                                  + "mip chain. THE CONSEQUENCE: the panel is drawn from a single "
                                  + "full-resolution level, so the eye minifies it unfiltered and the "
                                  + "shimmer this path exists to remove will still be there — "
                                  + "invisible while the head and the window are still, and crawling "
                                  + "as soon as either moves. Everything else (input, geometry, MSAA) "
                                  + "is unaffected.");
            }
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the post-render resolve and mip generation");
        }
    }

    /// <summary>
    /// RESOLVE, THEN BAND-LIMIT — run in the capture camera's own <see cref="Camera.onPostRender"/>,
    /// i.e. immediately after the frame it belongs to and (because that camera sits at
    /// <c>depth = -200</c>) before any other camera in the frame has culled. Both MultiPass eye
    /// passes therefore read one finished, identical, fully mipped texture, which is the invariant
    /// <c>CameraOrderProbe</c> measured and this path must not break.
    ///
    /// <para><see cref="Graphics.Blit(Texture, RenderTexture)"/> is what performs the MSAA resolve:
    /// binding a multisampled RenderTexture as a source texture resolves it, and the default blit
    /// material is a straight copy (<c>Blend Off</c>), so the capture's PREMULTIPLIED alpha survives
    /// the trip byte for byte — the composite derived in <see cref="BuildDisplay"/> is unchanged by
    /// this indirection. The project renders in Gamma colour space and both targets are
    /// <c>RenderTextureFormat.Default</c> with sRGB=False, so no colour conversion happens either.</para>
    ///
    /// <para><see cref="RenderTexture.active"/> is saved and restored around the blit because
    /// <c>Graphics.Blit</c> re-points it at its destination and we are inside the engine's own camera
    /// loop; leaving it moved would hand the next camera a target it did not ask for.</para>
    /// </summary>
    private static void ResolveAndMip(Entry e)
    {
        if (e.Rt == null || e.MipRt == null)
            return; // fallback: the quad shows the capture target directly (one Warn at allocation)
        RenderTexture? previous = RenderTexture.active;
        try
        {
            Graphics.Blit(e.Rt, e.MipRt);
            e.MipRt.GenerateMips();
            e.MipCount = e.MipRt.mipmapCount;
        }
        finally
        {
            RenderTexture.active = previous;
        }
    }

    private static Entry? EntryForCamera(Camera cam)
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            if (ReferenceEquals(Entries[i].Cam, cam))
                return Entries[i];
        }
        return null;
    }

    private static void RestoreMaskedCameras()
    {
        if (MaskedCameras.Count == 0)
            return;
        foreach (KeyValuePair<Camera, int> pair in MaskedCameras)
        {
            if (pair.Key != null)
                pair.Key.cullingMask = pair.Value;
        }
        MaskedCameras.Clear();
    }

    // ---- stand-down ---------------------------------------------------------------------------

    private static void StandDownAll(string why)
    {
        for (int i = Entries.Count - 1; i >= 0; i--)
            StandDown(Entries[i], i, why);
        RestoreMaskedCameras();
        UninstallHooks();
        _capLogged = false;
    }

    private static void StandDown(Entry e, int index, string why)
    {
        RestoreLayers(e);   // must run BEFORE the layer goes back in the pool: it reads e.Layer
        int layer = e.Layer;
        ReleaseLayer(layer);
        e.Layer = -1;
        DestroyEntryObjects(e);
        _vramTotal -= e.VramBytes;
        if (_vramTotal < 0)
            _vramTotal = 0;
        if (index >= 0 && index < Entries.Count && ReferenceEquals(Entries[index], e))
            Entries.RemoveAt(index);
        else
            Entries.Remove(e);
        _capLogged = false;
        // Budget just freed up: every window refused for cost reasons deserves one fresh look.
        // (Refusals are latched at all only so a permanent refusal cannot flood the log per frame.)
        Refused.Clear();
        if (Entries.Count == 0)
        {
            RestoreMaskedCameras();
            UninstallHooks();
        }
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE stood down on '{e.Window}' ({why}): the window's canvas "
                          + "is back on its original layer and is drawn straight into the eye again, "
                          + $"and {Mb(e.VramBytes)} MB of render target was released (session total "
                          + $"now {Mb(_vramTotal)} MB). Its private capture layer {layer} went back "
                          + $"into the pool ({FreeLayers.Count} of {PoolSize} free), so a window that "
                          + "was refused for want of one can now be re-considered. Input was never "
                          + "affected either way.");
    }

    private static void DestroyEntryObjects(Entry e)
    {
        if (e.Cam != null)
            e.Cam.targetTexture = null;
        if (e.CamGo != null)
            Object.Destroy(e.CamGo);
        if (e.DisplayGo != null)
            Object.Destroy(e.DisplayGo);
        if (e.Rt != null)
        {
            e.Rt.Release();
            Object.Destroy(e.Rt);
        }
        if (e.MipRt != null)
        {
            e.MipRt.Release();
            Object.Destroy(e.MipRt);
        }
        e.CamGo = null!;
        e.Cam = null!;
        e.DisplayGo = null!;
        e.Rt = null!;
        e.MipRt = null!;
    }

    private static void Fail(System.Exception ex, string where)
    {
        if (_errorLogged)
            return;
        _errorLogged = true;
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE failed in {where} ({ex.GetType().Name}: {ex.Message}) "
                          + "— the path stands down completely. THE CONSEQUENCE: every floated window "
                          + "goes back to being rasterized directly into the eye, i.e. exactly the "
                          + "behaviour with the dial off, including the reported shimmer. Nothing "
                          + "about input, placement or multiplayer changes.");
        try
        {
            StandDownAll("a failure in " + where);
        }
        catch
        {
            // Nothing left to do: never throw out of a tick.
        }
    }
}
