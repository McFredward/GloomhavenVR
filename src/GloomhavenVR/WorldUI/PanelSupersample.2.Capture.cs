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
    /// Estimated VRAM for one capture target: colour + its mip chain + a 24/8 depth-stencil, plus the
    /// multisample colour and depth surfaces when MSAA is on. Deliberately an over-estimate rather
    /// than an under-estimate — a budget that lies low is not a budget.
    /// </summary>
    private static long VramBytesFor(int w, int h, int msaa)
    {
        long px = (long)w * h;
        long colour = px * 4;
        long mips = colour / 3;
        long depthStencil = px * 4;
        long multisample = msaa > 1 ? (colour + depthStencil) * msaa : 0;
        return colour + mips + depthStencil + multisample;
    }

    private static string Mb(long bytes) => (bytes / (1024f * 1024f)).ToString("F1");

    /// <summary>
    /// Allocate the capture target. The descriptor comes from <c>FlatScreenStereo.CreateColorRt</c>
    /// — the shipped factory of this codebase's other RT path — because it is the one place that
    /// forces <c>D24_UNorm_S8_UInt</c> for a depth request, and a uGUI window without a STENCIL
    /// buffer loses every <see cref="Mask"/> in it (scroll viewports, circular avatars, the whole
    /// masked-content family). Mips are explicit, never automatic: <c>autoGenerateMips</c> is off and
    /// <see cref="RenderTexture.GenerateMips"/> runs in the capture camera's own
    /// <see cref="Camera.onPostRender"/>, i.e. immediately after the frame it belongs to.
    /// </summary>
    private static RenderTexture? CreateRt(int w, int h, ref int msaa, string window)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            RenderTexture rt = FlatScreenStereo.CreateColorRt(w, h, 24, $"GloomhavenVR.PanelSS_{window}");
            rt.antiAliasing = Mathf.Max(1, msaa);
            rt.useMipMap = true;
            rt.autoGenerateMips = false;
            rt.filterMode = FilterMode.Trilinear;
            rt.anisoLevel = AnisoLevel;
            rt.wrapMode = TextureWrapMode.Clamp;
            if (rt.Create())
                return rt;
            Object.Destroy(rt);
            if (msaa <= 1)
                break;
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: the driver refused a {w}x{h} mipmapped render "
                              + $"target at MSAA {msaa}x for '{window}' — retrying without MSAA. THE "
                              + "CONSEQUENCE if the retry also fails: this window keeps today's "
                              + "direct rendering.");
            msaa = 1;
        }
        VRLog.Warn(Scope, $"PANEL SUPERSAMPLE stands down on '{window}': a {w}x{h} render target could "
                          + "not be created at any MSAA level. THE CONSEQUENCE: this window keeps "
                          + "today's direct rendering — the same shimmer as before this build. "
                          + "Nothing was changed on the window itself.");
        return null;
    }

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
        cam.cullingMask = 1 << CaptureLayer;
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
        image.texture = e.Rt;
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
    /// Keep the capture frustum and the display quad on the host rect. The camera is a CHILD of the
    /// host, so only its projection needs writing (world-unit ortho size and aspect, recomputed from
    /// the live rect and lossy scale — the diorama scale is ~198 world units per real metre in the
    /// map room, and every quantity here is in WORLD units, never metres). The display quad is a
    /// scene root, so its full pose is copied; the final copy happens in
    /// <see cref="OnPreCull"/>, after every LateUpdate pose writer has run.
    /// </summary>
    private static void SyncGeometry(Entry e)
    {
        ConvertedPanel panel = e.Panel;
        RectTransform host = panel.HostRect;
        Rect rect = host.rect;
        Vector3 lossy = host.lossyScale;
        float scale = Mathf.Max(Mathf.Abs(lossy.y), 1e-6f);

        float rectHeightWorld = rect.height * scale;
        float slab = Mathf.Max(rectHeightWorld * 0.5f, 1e-4f);
        float standoff = slab * 2f;

        e.Cam.orthographicSize = rectHeightWorld * 0.5f;
        e.Cam.aspect = Mathf.Max(rect.width / Mathf.Max(rect.height, 1e-4f), 1e-4f);
        e.Cam.nearClipPlane = standoff - slab;
        e.Cam.farClipPlane = standoff + slab;
        Vector2 centre = rect.center;
        e.CamGo.transform.localPosition = new Vector3(centre.x, centre.y, -standoff / scale);
        e.CamGo.transform.localRotation = Quaternion.identity;
        e.CamGo.transform.localScale = Vector3.one;

        e.DisplayRect.sizeDelta = rect.size;
        SyncDisplayPose(e);

        // Re-allocate when the content fit materially resized the window: an RT built for the
        // pre-fit rect would either waste texels or stretch across the new one, and the whole point
        // of this path is that one RT texel corresponds to one authored pixel times the factor.
        if (Mathf.Abs(rect.width - e.Authored.x) > e.Authored.x * RectChangeFraction
            || Mathf.Abs(rect.height - e.Authored.y) > e.Authored.y * RectChangeFraction)
        {
            Reallocate(e, rect);
        }
    }

    private static void SyncDisplayPose(Entry e)
    {
        RectTransform host = e.Panel.HostRect;
        if (host == null || e.DisplayGo == null)
            return;
        Rect rect = host.rect;
        Transform t = e.DisplayGo.transform;
        t.SetPositionAndRotation(host.TransformPoint(rect.center), host.rotation);
        t.localScale = host.lossyScale;
    }

    private static void Reallocate(Entry e, Rect rect)
    {
        int rtW = Mathf.Clamp(Mathf.RoundToInt(rect.width * e.Factor), 16, MaxRtDimension);
        int rtH = Mathf.Clamp(Mathf.RoundToInt(rect.height * e.Factor), 16, MaxRtDimension);
        if (rtW == e.RtW && rtH == e.RtH)
        {
            e.Authored = rect.size;
            return;
        }
        int msaa = e.Msaa;
        long vram = VramBytesFor(rtW, rtH, msaa);
        while (msaa > 1 && vram > MaxPanelVramBytes)
        {
            msaa /= 2;
            vram = VramBytesFor(rtW, rtH, msaa);
        }
        if (vram > MaxPanelVramBytes || _vramTotal - e.VramBytes + vram > MaxTotalVramBytes)
        {
            e.Authored = rect.size; // keep the existing target; it merely stretches a little
            return;
        }

        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, e.Window);
        if (rt == null)
            return;
        RenderTexture old = e.Rt;
        e.Cam.targetTexture = rt;
        e.DisplayImage.texture = rt;
        e.Rt = rt;
        _vramTotal += vram - e.VramBytes;
        e.VramBytes = vram;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Msaa = msaa;
        e.MipCount = rt.mipmapCount;
        e.Authored = rect.size;
        if (old != null)
        {
            old.Release();
            Object.Destroy(old);
        }
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE re-allocated '{e.Window}' to {rtW}x{rtH} (MSAA {msaa}x, "
                          + $"{Mb(vram)} MB) after the content fit resized the host rect to "
                          + $"{rect.width:F0}x{rect.height:F0} uGUI px.");
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

    /// <summary>The dedicated capture layer: the first unnamed layer scanning 31 → 8 that is NOT the
    /// mod layer. -1 when none is free, which stands the whole path down.</summary>
    private static int CaptureLayer
    {
        get
        {
            if (_captureLayer != -2)
                return _captureLayer;
            _captureLayer = -1;
            int mod = VRLayers.ModLayer;
            for (int i = 31; i >= 8; i--)
            {
                if (i == mod || !string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                    continue;
                _captureLayer = i;
                VRLog.Info(Scope, $"PANEL SUPERSAMPLE capture layer resolved: {i} (first unnamed layer "
                                  + $"scanning 31->8 that is not the mod layer {mod}; mask "
                                  + $"0x{1 << i:X8}). Only the per-panel capture cameras render it; "
                                  + "every other camera has the bit cleared for the duration of its "
                                  + "own render.");
                break;
            }
            return _captureLayer;
        }
    }

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
        int layer = CaptureLayer;
        if (layer < 0 || e.Panel.HostGo == null)
            return;
        Scratch.Clear();
        Scratch.Add(e.Panel.HostGo.transform);
        int moved = 0;
        int skipped = 0;
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
            if (!isRoot && t.GetComponent<Canvas>() != null)
            {
                nested++;
                if (t.gameObject.layer == layer)
                    nestedOnLayer++;
            }
            if (t.gameObject.layer != layer)
            {
                if (!IsRecorded(e, t))
                    e.Relayered.Add(new LayerRecord { Transform = t, OriginalLayer = t.gameObject.layer });
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
        e.NestedTotal = nested;
        e.NestedCaptured = Mathf.Min(nestedOnLayer, nested);
        if (!initial && moved > 0)
        {
            VRLog.Info(Scope, $"PANEL SUPERSAMPLE: {moved} pooled/late transform(s) of '{e.Window}' "
                              + $"joined capture layer {layer} (a repopulating window brings children "
                              + "on the game's UI layer; until they are swept they would be drawn "
                              + "straight into the eye as well as into the capture).");
        }
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
        int layer = _captureLayer;
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
            if (cam == null || Entries.Count == 0 || _captureLayer < 0)
                return;
            Entry? mine = EntryForCamera(cam);
            if (mine != null)
            {
                mine.LastCaptureStart = Time.realtimeSinceStartup;
                return; // our own capture camera: it is the ONE camera that may see the layer
            }
            if (_poseSyncFrame != Time.frameCount)
            {
                _poseSyncFrame = Time.frameCount;
                for (int i = 0; i < Entries.Count; i++)
                    SyncDisplayPose(Entries[i]);
            }
            int bit = 1 << _captureLayer;
            if ((cam.cullingMask & bit) == 0)
                return;
            MaskedCameras[cam] = cam.cullingMask;
            cam.cullingMask &= ~bit;
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
            // MIPS, explicitly, immediately after the capture that owns them. autoGenerateMips is
            // off on purpose: on an MSAA target it is unreliable, and a mip chain that silently did
            // not build would look exactly like this whole feature not working.
            e.Rt.GenerateMips();
            e.Captures++;
            if (e.LastCaptureStart > 0f)
                e.CaptureMs += (Time.realtimeSinceStartup - e.LastCaptureStart) * 1000.0;
            if (!e.MipWarned && e.Rt.mipmapCount <= 1)
            {
                e.MipWarned = true;
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE: '{e.Window}' has a render target with "
                                  + $"mipmapCount={e.Rt.mipmapCount} — NO mip chain. THE CONSEQUENCE: "
                                  + "the panel is drawn from a single full-resolution level, so the "
                                  + "eye minifies it unfiltered and the shimmer this path exists to "
                                  + "remove will still be there. Everything else (input, geometry, "
                                  + "MSAA) is unaffected.");
            }
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the post-render mip generation");
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
        RestoreLayers(e);
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
                          + $"now {Mb(_vramTotal)} MB). Input was never affected either way.");
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
        e.CamGo = null!;
        e.Cam = null!;
        e.DisplayGo = null!;
        e.Rt = null!;
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
