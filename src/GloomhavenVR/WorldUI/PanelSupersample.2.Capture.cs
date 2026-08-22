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
            {
                ClearRt(rt);
                return rt;
            }
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
        {
            ClearRt(rt);
            return rt;
        }
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

    /// <summary>
    /// Clear a freshly created render target to transparent black.
    ///
    /// <para><b>WHY THIS IS NOT COSMETIC.</b> Unity does NOT clear a render target on
    /// <c>Create()</c>; its contents are whatever that VRAM last held. Every allocation on this path
    /// is immediately handed to the display quad — <see cref="Reallocate"/> points
    /// <c>DisplayImage.texture</c> at the new target in the same statement block that destroys the
    /// old one — so any frame in which the capture camera does not render (the panel is hidden for
    /// that frame, the camera is disabled, the resolve is skipped because one of the pair is null)
    /// shows uninitialised memory instead of the window. That is a rare window, but a re-allocation
    /// happens exactly at the moment this round is investigating: the content fit flips a released
    /// window's host rect and the target follows. Two <c>GL.Clear</c>s per allocation close it
    /// outright, and <see cref="Entry.CameraOffWhileVisible"/> measures how often the window they
    /// cover is actually open.</para>
    /// </summary>
    private static void ClearRt(RenderTexture rt)
    {
        RenderTexture? previous = RenderTexture.active;
        try
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
        }
        finally
        {
            RenderTexture.active = previous;
        }
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
            // THE DRAG BOUNDARY (ModBuild 197). A change arriving after the motion window has fully
            // lapsed is a NEW drag, so the per-drag accumulators start over here. Everything the
            // release edge prints — how long the drag was, how fast it was going when the hand let
            // go, how many of its frames were dropped — is accumulated between this reset and the
            // settle gate in ServiceRepairs.
            if (!IsMoving(e))
            {
                e.DragStartFrame = Time.frameCount;
                e.DragMotionFrames = 0;
                e.DragMaxStepWorld = 0f;
                e.DragDroppedFrames = 0;
                e.DragWorstFrameMs = 0f;
            }
            e.LastMotionFrame = Time.frameCount;
            e.MotionFrames++;
            e.DragMotionFrames++;
            // THE ABRUPT NUMBER: the step on the LAST frame that moved. A hand that stops before it
            // lets go leaves ~0 here; a hand still travelling leaves the drag's full speed, and the
            // release edge prints both this and the drag's largest step so the two are comparable.
            e.DragLastStepWorld = step;
            if (step > e.DragMaxStepWorld)
                e.DragMaxStepWorld = step;
            e.CurrentMotionRun++;
            if (e.CurrentMotionRun > e.LongestMotionRun)
                e.LongestMotionRun = e.CurrentMotionRun;
        }
        else
        {
            e.CurrentMotionRun = 0;
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
        // THE PATH COUNTER (ModBuild 197). A frame in which the quad IS shown and no capture is
        // taken is a frame in which the eye draws the previous capture — the honest answer to "is a
        // dragged window supersampled at all, or does something short-circuit". Expected 0 while
        // CaptureIntervalFrames is 1; printed with its denominator so a 0 is evidence.
        if (visible && !due)
            e.CameraOffWhileVisible++;
        if (visible && e.MipFallback)
            e.RawQuadFrames++;
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
        double sweepMs = (Time.realtimeSinceStartup - sweepStart) * 1000.0;
        e.SweepMs += sweepMs;
        // Split the cost by what the window was doing, because that is the ONLY way the log can
        // price the motion cadence against the settled one. ModBuild 195 could only report one
        // averaged figure, which is why "1.5 ms per sweep" had to be multiplied by hand against
        // "sweeps every frame while moving" to see the problem.
        if (IsMoving(e))
        {
            e.MotionSweeps++;
            e.MotionSweepMs += sweepMs;
        }
        else
        {
            e.StillSweeps++;
            e.StillSweepMs += sweepMs;
        }
        if (!initial && moved > 0)
        {
            e.LateJoiners += moved;
            VRLog.Info(Scope, $"PANEL SUPERSAMPLE: {moved} pooled/late transform(s) of '{e.Window}' "
                              + $"joined capture layer {layer} (a repopulating window brings children "
                              + "on the game's UI layer; until they are swept they would be MISSING "
                              + "from the capture and drawn straight into the eye instead — which is "
                              + "the ModBuild 192 'manche Elemente ... fehlen im Fenster' report). "
                              + $"This sweep ran because the window {(IsMoving(e) ? "is MOVING (the "
                                  + MovingSweepIntervalFrames + "-frame motion cadence)"
                                  : "reached its periodic cadence")}. WHICH CADENCE THIS LINE READS "
                              + "IS THE MEASUREMENT that set MovingSweepIntervalFrames: in the "
                              + "ModBuild 195 log, 22 of the 23 lines like this one read 'periodic' "
                              + "and exactly ONE read the motion cadence, which is why the motion "
                              + "cadence is no longer every frame.");
        }
    }

    // ---- the frame budget instrument ------------------------------------------------------------

    /// <summary>
    /// Sample this frame's unscaled duration into the MOTION or the STILL bucket for this window.
    /// <para>WHY THIS EXISTS, and it is the one measurement the previous three rounds did not have.
    /// The report's own ModBuild 194 clause says a drag that moves the window several RENDERED eye
    /// pixels per frame is a JUDDER problem that no filtering can reach. The ModBuild 195 log then
    /// measured exactly that: the largest single-frame host steps on real drags are 12.39, 18.67,
    /// 32.70, 46.96, 73.70, 93.77, 122.33 and 132.85 RENDERED eye pixels. At that rate one dropped
    /// frame is over a hundred pixels of positional error, so whether the frame was DROPPED is the
    /// whole question — and the session's own frame telemetry reads p50 17.33 ms against an 11.11 ms
    /// budget. This splits that number by what the window was doing, so the next log can say whether
    /// a dragged window's frames are worse than a still window's frames and by how much.</para>
    /// </summary>
    private static void SampleFrameBudget(Entry e, bool moving)
    {
        float ms = Time.unscaledDeltaTime * 1000f;
        if (ms <= 0f || ms > 1000f)
            return; // a load spike or a paused frame is not a frame-budget sample
        if (moving)
        {
            e.MotionFrameSamples++;
            e.MotionFrameMs += ms;
            if (ms > e.MotionFrameMsMax)
                e.MotionFrameMsMax = ms;
            if (ms > FrameBudgetMs)
                e.MotionFramesOverBudget++;
            // THE JUDDER COUNTER, per drag. Over-budget is not the same statement as DROPPED: a
            // frame at 11.2 ms against an 11.11 ms budget is v-sync jitter, while a frame past
            // DroppedFrameMs is one the headset had to fill by showing the previous image again —
            // and at the drag speeds this class measures, that is tens of rendered pixels of
            // positional error on a high-contrast edge, which is what the eye reads as flicker.
            if (ms > DroppedFrameMs)
                e.DragDroppedFrames++;
            if (ms > e.DragWorstFrameMs)
                e.DragWorstFrameMs = ms;
        }
        else
        {
            e.StillFrameSamples++;
            e.StillFrameMs += ms;
            if (ms > e.StillFrameMsMax)
                e.StillFrameMsMax = ms;
            if (ms > FrameBudgetMs)
                e.StillFramesOverBudget++;
        }
    }

    // ---- the repairs ----------------------------------------------------------------------------

    /// <summary>
    /// Run the two bounded repairs this class owes the "kaputte Anzeige" report, if either is due.
    ///
    /// <para><b>THE RELEASE REPAIR.</b> <see cref="ReleaseSettleFrames"/> frames after the last
    /// pose/scale/rect change — i.e. the instant the window comes to rest after a drag — force ALL
    /// THREE of the things a settled window's cadence would otherwise get to at its own pace: the
    /// content frame is re-measured, the capture layer is re-swept, and every text component in the
    /// subtree re-requests its glyphs and re-generates its mesh. A second pass follows
    /// <see cref="ReleaseSecondRepairFrames"/> frames later because the content fit can still flip
    /// the host rect after the release (the ModBuild 195 log's party window walks 328 -> 716 -> 1920
    /// uGUI px across one session). Two passes per release, never one per frame.</para>
    ///
    /// <para><b>THE FONT-REPACK REPAIR.</b> Armed by <see cref="OnFontTextureRebuilt"/> and by the
    /// TMP atlas check in <see cref="MeasureContent"/>. It regenerates text WITHOUT re-measuring or
    /// re-sweeping, because a repack changes what the glyphs look like and nothing about where the
    /// window is.</para>
    ///
    /// <para>Both END in a content-integrity scan, so the log records what the repair FOUND and not
    /// merely that it ran — a repair that never finds anything is a repair that should be deleted,
    /// and this is how the next round will be able to tell.</para>
    /// </summary>
    private static void ServiceRepairs(Entry e)
    {
        // A NEW motion event re-arms both stages. Keying the state machine on LastMotionFrame is what
        // makes "one release costs two repairs" true no matter how long the window then stands still.
        if (e.ReleaseRepairedMotionFrame != e.LastMotionFrame)
        {
            e.ReleaseRepairedMotionFrame = e.LastMotionFrame;
            e.ReleaseRepairStage = 0;
        }
        int sinceMotion = Time.frameCount - e.LastMotionFrame;
        if (e.ReleaseRepairStage == 0 && sinceMotion >= ReleaseSettleFrames)
        {
            e.ReleaseRepairStage = 1;
            e.ReleasesThisWindow++;
            if (sinceMotion > e.ReleaseGateFramesMax)
                e.ReleaseGateFramesMax = sinceMotion;
            ReleaseRepair(e);
            ReportRelease(e, sinceMotion);
            return;
        }
        if (e.ReleaseRepairStage == 1 && sinceMotion >= ReleaseSecondRepairFrames)
        {
            e.ReleaseRepairStage = 2;
            ReleaseRepair(e);
            return;
        }

        if (e.RebuildRepairFrame >= 0 && Time.frameCount >= e.RebuildRepairFrame)
        {
            e.RebuildRepairFrame = -1;
            e.RebuildRepairs++;
            // repairAll: a repack invalidates the UVs of meshes that still pass every integrity test,
            // so "nothing measured wrong" is not a reason to leave them alone here.
            MeasureContent(e, repairAll: true);
        }
    }

    /// <summary>
    /// <b>MODBUILD 197 MAKES THE ModBuild 196 RELEASE REPAIR ACTUALLY REGENERATE TEXT.</b> Its own
    /// documentation said it forces <i>"every text component in the subtree to re-request its glyphs
    /// and re-generate its mesh"</i>. It did not, and the hardware log says so in one number: across
    /// 236 state lines the largest regeneration this repair ever performed was <b>one character
    /// across one component</b>, and 151 of those lines read <c>0 character(s) across 0
    /// component(s)</c> — on windows carrying up to 297 text components and 4600 glyphs.
    ///
    /// <para>THE CAUSE was a gate, not a failure: <c>ScanTmpText</c> returns without regenerating
    /// whenever its own defect count is zero (<c>if ((!repairAll &amp;&amp; bad == 0) || ...) return
    /// false;</c>), and that count is zero in every reading this session produced. So the repair was
    /// conditioned on the very instrument ModBuild 196 shipped to find out whether the condition was
    /// the right one. The user reporting "no improvement" is therefore the expected outcome and NOT
    /// evidence about the text hypothesis: the remedy never ran.</para>
    ///
    /// <para>THE RELEASE REPAIR NOW PASSES <c>repairAll</c>. The report cadence's scan does not — a
    /// settled window must keep costing what it costs today — so the regeneration is bounded to two
    /// passes per release, capped by <c>MaxRegeneratePerScan</c>, and its cost is measured into
    /// <see cref="Entry.ReleaseRegenMs"/> and printed. THE CONSEQUENCE IF IT IS TOO EXPENSIVE: the
    /// release frame gets longer, which the RELEASE line reports per release; the lever is that cap.
    /// THE CONSEQUENCE FOR THE NEXT ROUND EITHER WAY: if the broken image survives a release on which
    /// every text component in the window was genuinely re-parsed and re-generated, the text-source
    /// family is dead outright rather than untested.</para>
    /// </summary>
    private static void ReleaseRepair(Entry e)
    {
        e.ReleaseRepairs++;
        MeasureFrame(e);
        SyncProjection(e);
        if (e.DisplayRect != null)
            e.DisplayRect.sizeDelta = e.Frame.size;
        SyncDisplayPose(e);
        Reallocate(e, e.Frame);
        e.NextSweepFrame = Time.frameCount + SweepIntervalFrames;
        ApplyCaptureLayer(e, initial: false);
        // Scan AND repair in one walk: the counts recorded are the PRE-repair state, so the report
        // says what was wrong at the release rather than only that a repair ran.
        float regenStart = Time.realtimeSinceStartup;
        MeasureContent(e, repairAll: true);
        e.ReleaseRegenComponents = e.RegeneratedComponents;
        e.ReleaseRegenMs = (Time.realtimeSinceStartup - regenStart) * 1000.0;
    }

    /// <summary>
    /// <b>ONE LINE PER RELEASE — the instrument this symptom did not have.</b>
    ///
    /// <para>THE USER'S NEW WORD IS <i>ABRUPT</i>: <i>"das Flackerproblem WÄHREND DER BEWEGUNG ist
    /// noch da — inklusive der möglichen kaputten Darstellung, wenn man nach der Bewegung ABRUPT
    /// loslässt"</i>. That is a claim about the release VELOCITY, and nothing in ModBuild 196 could
    /// confirm or deny it: the state line carried only a cumulative repair count on a 10-second
    /// cadence, so a gate that opened late, a gate that never opened and a gate that opened on time
    /// and found nothing all printed the same character.</para>
    ///
    /// <para>WHAT THIS LINE DECIDES, and each field carries the comparison it is read against:
    /// <list type="number">
    /// <item>WAS THE RELEASE ABRUPT? The drag's LAST single-frame step next to its LARGEST, both in
    /// rendered eye pixels. A hand that slowed before letting go leaves a last step far below the
    /// largest; a hand still travelling leaves them equal. If the broken image really does correlate
    /// with abruptness, these two numbers are where it shows.</item>
    /// <item>DID THE SETTLE GATE OPEN, AND WHEN? The frames from the last change to this repair,
    /// against <see cref="ReleaseSettleFrames"/>. A gate that opens exactly on the threshold is a
    /// gate that works; one that opens tens of frames late means somebody kept writing the pose after
    /// the hand let go, which is this project's "a stillness gate never opens for state someone else
    /// rewrites each frame". THE LONGEST MOTION RUN is printed with it, because a gate that never
    /// opens at all produces no line here and only that field would show why.</item>
    /// <item>HOW MANY BROKEN FRAMES THE EYE ALREADY SAW. The repair runs in the LateUpdate of the
    /// frame the gate opens, so the eye has ALREADY drawn every frame since the hand let go with
    /// whatever state the release left behind. That number is printed, because it bounds what a
    /// repair on this schedule can ever fix: a defect the user sees for 2 frames and a defect that
    /// persists are different reports, and this instrument cannot be read as the second one.</item>
    /// <item>WHAT THE SCAN FOUND AT THAT EXACT INSTANT, with its comparison count, its worst value
    /// and its threshold on the same line — including the MESH counters, which are new and which are
    /// the only ones that can see the photograph's fault (see <see cref="ScanTmpMesh"/>).</item>
    /// <item>WHETHER THE DRAG WAS DROPPING FRAMES. The count, the worst frame time and the
    /// <see cref="DroppedFrameMs"/> threshold, so the moving complaint can be read as judder or not
    /// from the same line as the release.</item>
    /// </list></para>
    /// </summary>
    private static void ReportRelease(Entry e, int gateFrames)
    {
        // The world -> rendered-eye-pixel bridge is measured by SamplingSentence, i.e. on the 10 s
        // report cadence. Before the first report of a freshly engaged panel it is 0, and dividing
        // by a floor would print a number thousands of times too large. A release that lands in that
        // gap says so instead, because an unlabelled wrong number is worse than a missing one.
        bool scaled = e.WorldPerAuthoredPx > 1e-9f && e.AuthoredPerRenderedPx > 1e-6f;
        float perAuthored = Mathf.Max(e.WorldPerAuthoredPx, 1e-9f);
        float perRendered = Mathf.Max(e.AuthoredPerRenderedPx, 1e-6f);
        float lastPx = scaled ? e.DragLastStepWorld / perAuthored / perRendered : -1f;
        float maxPx = scaled ? e.DragMaxStepWorld / perAuthored / perRendered : -1f;
        string pxNote = scaled
            ? string.Empty
            : " (the RENDERED eye px figures read -1 because the world-to-eye-pixel scale has not "
              + "been measured yet — it is derived once per 10 s report and this release landed "
              + "before this panel's first one; the world-unit figures are exact regardless)";
        int meshBad = e.MeshMissingQuads + e.MeshDegenerateQuads + e.MeshDegenerateUv
                      + e.MeshUvOutOfRange + e.MeshNonFinite;
        int glyphBad = e.GlyphsNotInAtlas + e.GlyphsNotVisible + e.GlyphsBlankQuad;
        VRLog.Info(Scope,
            $"PANEL SUPERSAMPLE RELEASE '{e.Window}': the drag ran {e.DragMotionFrames} frame(s) "
            + $"({Time.frameCount - e.DragStartFrame} frame(s) wall) and the settle gate opened "
            + $"{gateFrames} frame(s) after the last change, against a threshold of "
            + $"{ReleaseSettleFrames}. ABRUPTNESS (the user's own word, measured): the LAST "
            + $"single-frame host step before the hand let go was {e.DragLastStepWorld:F4} world "
            + $"units = {lastPx:F2} RENDERED eye px, against this drag's LARGEST step of "
            + $"{e.DragMaxStepWorld:F4} = {maxPx:F2} RENDERED eye px and a detector epsilon of "
            + $"{e.MotionEpsWorld:F4} world units{pxNote} — the two being equal means the window was "
            + "still travelling at full speed when it was released, and a last step far below the "
            + "largest means the hand slowed first. THE EYE HAS ALREADY DRAWN "
            + $"{gateFrames} frame(s) of the released image before this repair ran, and will draw "
            + $"{ReleaseSecondRepairFrames - gateFrames} more before the second pass, so a defect "
            + "this repair removes is one the player still sees for that long; a defect the player "
            + "reports as PERSISTING is one this repair did not remove at all. WHAT THE SCAN FOUND "
            + $"AT THIS INSTANT: {glyphBad} text-source defect(s) out of {e.GlyphsChecked} glyph "
            + $"lookup(s) across {e.TextComponents} component(s) ({e.GlyphsNotInAtlas} not in the "
            + $"atlas, {e.GlyphsNotVisible} not visible, {e.GlyphsBlankQuad} zero-area layout quad at "
            + $"threshold {DegenerateQuadArea:G3}); and {meshBad} SUBMITTED-MESH defect(s) out of "
            + $"{e.MeshQuadsChecked} quad(s) examined ({e.MeshMissingQuads} never written into the "
            + $"mesh, {e.MeshDegenerateQuads} zero-area in the mesh, {e.MeshDegenerateUv} with a "
            + $"collapsed atlas UV rect at threshold {DegenerateUvArea:G3}, {e.MeshUvOutOfRange} "
            + $"sampling outside the atlas, {e.MeshNonFinite} non-finite)"
            + (e.MeshWorstBad > 0 ? $", worst: {e.MeshWorst} with {e.MeshWorstBad}" : ", no worst")
            + $". THE REPAIR THEN RE-GENERATED {e.ReleaseRegenComponents} of {e.TextComponents} "
            + $"component(s) in {e.ReleaseRegenMs:F2} ms (cap {MaxRegeneratePerScan}) — ModBuild 196 "
            + "re-generated at most ONE component per release because it gated the regeneration on "
            + "the defect count, which is permanently zero; this pass is unconditional, so if the "
            + "broken image survives it the text-source family is falsified rather than untested. "
            + $"FRAME PACING DURING THIS DRAG: {e.DragDroppedFrames} of {e.DragMotionFrames} "
            + $"frame(s) exceeded {DroppedFrameMs:F2} ms (worst {e.DragWorstFrameMs:F2} ms, budget "
            + $"{FrameBudgetMs:F2} ms), i.e. that many frames on which the headset re-showed the "
            + $"previous image while the window was travelling up to {maxPx:F2} rendered px per "
            + "frame — that product, not the panel's filtering, is what a JUDDER reading of the "
            + "moving complaint rests on. LONGEST UNBROKEN MOTION RUN this report window: "
            + $"{e.LongestMotionRun} frame(s); if that ever equals the whole window, no release can "
            + "be detected at all and this line would simply be absent.");
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
        // THE TWO EVENTS THAT MAKE THE RELEASE REPORT DECIDABLE. Font.textureRebuilt fires when a
        // DYNAMIC font atlas is re-packed, which silently invalidates the UVs of every text mesh
        // already generated against it; Canvas.willRenderCanvases fires when uGUI runs its layout and
        // graphic rebuild queue for the frame. Recording the FRAME of each is what lets the capture
        // say whether it ran mid-repack or ahead of the rebuild — see Entry.CapturesDuringFontRebuild
        // and Entry.CapturesBeforeCanvasUpdate. Both are cheap: one int store per event.
        Font.textureRebuilt += OnFontTextureRebuilt;
        Canvas.willRenderCanvases += OnWillRenderCanvases;
    }

    private static void UninstallHooks()
    {
        if (!_hooksInstalled)
            return;
        _hooksInstalled = false;
        Camera.onPreCull -= OnPreCull;
        Camera.onPostRender -= OnPostRender;
        Font.textureRebuilt -= OnFontTextureRebuilt;
        Canvas.willRenderCanvases -= OnWillRenderCanvases;
    }

    /// <summary>
    /// A DYNAMIC FONT ATLAS WAS RE-PACKED. Every text mesh already generated against it now points at
    /// atlas regions that may hold different glyphs or nothing at all, and a canvas that is not
    /// re-generated keeps showing those stale UVs. uGUI's own <c>Text</c> subscribes to this event and
    /// re-generates itself, but only if it <c>IsActive()</c>, and TextMeshPro does not use this event
    /// at all — so this class arms its OWN repair for every engaged panel rather than trusting either.
    /// <para>Armed for the NEXT frame, not this one: an atlas being re-packed is usually still being
    /// filled by the requests that caused the repack, and regenerating inside that would just be
    /// first in the queue for the next repack.</para>
    /// </summary>
    private static void OnFontTextureRebuilt(Font font)
    {
        try
        {
            _fontRebuilds++;
            _fontRebuildFrame = Time.frameCount;
            _fontRebuildName = font != null ? font.name : "(null)";
            ArmRebuildRepair();
        }
        catch
        {
            // Never throw out of an engine callback.
        }
    }

    private static void OnWillRenderCanvases() => _canvasUpdateFrame = Time.frameCount;

    /// <summary>Ask every live entry to re-request its glyphs and re-generate its text on the next
    /// LateUpdate. Idempotent: an entry already armed keeps its earlier (never later) due frame.</summary>
    private static void ArmRebuildRepair()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entry e = Entries[i];
            if (e.RebuildRepairFrame < 0)
                e.RebuildRepairFrame = Time.frameCount + 1;
        }
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
                // THE CAPTURE-COINCIDENCE COUNTERS, recorded at the LAST instant before this camera
                // culls — i.e. the exact state the captured image is taken from. Every one of them
                // has a denominator (CaptureTicks) recorded on the same line, so "the instrument
                // never ran", "it ran and found nothing" and "it ran and found something" are three
                // different readings and can never print the same character.
                mine.CaptureTicks++;
                // Split by what the window is doing, so "was this window supersampled WHILE IT
                // MOVED" is a count and not an argument from the source.
                if (IsMoving(mine))
                    mine.MotionCaptures++;
                else
                    mine.StillCaptures++;
                if (_fontRebuildFrame == Time.frameCount)
                    mine.CapturesDuringFontRebuild++;
                if (_canvasUpdateFrame != Time.frameCount)
                    mine.CapturesBeforeCanvasUpdate++;
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
            // THE CAPTURE-SIDE FALSIFIER (ModBuild 197). "The capture, not the content, is what
            // breaks" needs a measurement, and this is the cheapest one that can find it: a resolve
            // whose source and destination disagree in size is a target that was re-allocated
            // between the capture and the resolve, and the blit then stretches one frame's image
            // across the other's texels. Expected 0 — Reallocate replaces BOTH targets together,
            // inside a LateUpdate, before the camera loop — and printed with its denominator so the
            // zero is evidence rather than an absent line.
            if (e.Rt.width != e.MipRt.width || e.Rt.height != e.MipRt.height)
                e.ResolveSizeMismatch++;
            Graphics.Blit(e.Rt, e.MipRt);
            e.MipRt.GenerateMips();
            e.MipCount = e.MipRt.mipmapCount;
            if (IsMoving(e))
                e.MotionResolves++;
            else
                e.StillResolves++;
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
