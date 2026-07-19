using System;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// Makes the scenario sky backdrop sphere (<c>GH_SkySphere</c>, shader
/// <c>AMP_SkyShader</c>) a pure NON-OCCLUDING BACKDROP while keeping it visible, so
/// nothing in FRONT of it is ever clipped by it — floated menus, the control board when
/// dragged "outside" the shallow dome, and the laser reticle/beam that points past the
/// shell all stay visible.
///
/// THE PROBLEM
/// -----------
/// The player is INSIDE the sphere. <c>AMP_SkyShader</c> WRITES DEPTH and (per a prior
/// worker) exposes no plain <c>_ZWrite</c>, so raising the render queue alone does NOT stop
/// occlusion: the dome is SQUASHED (y-extent only ~71 world units), so a foreground object
/// dragged toward/past its surface — especially vertically — ends up geometrically BEHIND
/// the shell, the sphere's depth is nearer, and the object (or the ZTest-LEqual laser) gets
/// clipped.
///
/// THE FIX (self-selected at runtime, both fully reversible)
/// --------------------------------------------------------
/// On first sight of the sphere this dumps the shader's property names/count and the
/// material's render queue (so the hardware log names exactly what the shader exposes), then
/// picks the mechanism:
///
///  - <b>ZWriteOff</b> — if the shader exposes ANY depth-write property under any name
///    (<c>_ZWrite</c>, <c>__ZWrite</c>, <c>_DepthWrite</c>, …): set it to 0 and drop the
///    material to <see cref="RenderQueue.Background"/> (1000). The sphere renders first,
///    writes no depth, and everything after tests clean → it can never occlude. Cheapest,
///    zero visual risk (same material, same look).
///
///  - <b>DepthResetCB</b> — if NO depth-write property exists (the shader hard-codes
///    <c>ZWrite On</c>): the robust STRUCTURAL fix that is safe on TILED MOBILE GPUs. The
///    sphere is taken out of the head camera's AUTOMATIC opaque draw via
///    <see cref="Renderer.forceRenderingOff"/> = true and instead redrawn by a
///    <see cref="CommandBuffer"/> at <see cref="CameraEvent.BeforeForwardOpaque"/> that does
///    TWO ordinary DRAWS:
///      (1) <see cref="CommandBuffer.DrawRenderer(Renderer, Material, int, int)"/> of the sphere
///          with its OWN material — the full ANIMATED AMP_SkyShader look, writing colour + its
///          (near, shell) depth; then
///      (2) <see cref="CommandBuffer.DrawMesh(Mesh, Matrix4x4, Material)"/> of a HEAD-CENTRED
///          sphere at ~the far plane, rendered through the bundled <c>GloomhavenVR/Overlay</c>
///          shader forced to <c>ZTest Always, ZWrite On, Blend Zero One, Cull Off</c>. Because
///          the blend is <c>Zero One</c> it leaves the COLOUR buffer untouched (result = dst),
///          while <c>ZWrite On</c> + <c>ZTest Always</c> OVERWRITES the depth buffer to ~far in
///          every view direction. The scene's opaque geometry + all mod visuals then render
///          against a depth buffer that no longer holds the sphere's near shell depth → the
///          sphere is a pure colour backdrop that can occlude nothing, yet its AMP_SkyShader
///          sky is fully VISIBLE. Holds ALWAYS (board and laser benefit too).
///
///    WHY A DRAW AND NOT <c>ClearRenderTarget(depth-only)</c> (attempt #1's failure): a mid-pass
///    depth-ONLY <c>CommandBuffer.ClearRenderTarget</c> on a TILED mobile GPU (Quest 3 + Virtual
///    Desktop) forces a tile flush that GREYS/LOSES the colour we just drew → the sky went BLACK.
///    An ordinary DRAW that writes depth (this reset sphere) is the bread-and-butter of a
///    tile-based renderpass and needs no clear op, so the colour survives and the sky shows.
///
///    WHY forceRenderingOff and NOT <c>enabled = false</c>: disabling the renderer removes it
///    from Unity's culling/visible set, and <c>CommandBuffer.DrawRenderer</c> on a culled
///    renderer draws NOTHING — the sphere was then neither auto-drawn nor CB-drawn, so the sky
///    went COMPLETELY BLACK (issue #2). forceRenderingOff suppresses only the AUTOMATIC draw
///    while keeping the renderer culled/prepared, so DrawRenderer has valid render data and the
///    sky's own colour renders. It also guarantees ORDER: BeforeForwardOpaque runs before every
///    auto opaque draw (including the sphere's own queue-2000 draw), so drawing the sphere in the
///    CB then resetting depth beats any later re-write of the shell depth. Fully reversible.
///
///    This mechanism uses ONE camera (the rig head camera), needs no second/stereo camera and no
///    culling-mask/stereo-policy changes — the buffer inherits the head camera's per-eye matrices.
///
/// SEPARATION FROM MR: this runs only while the sky is meant to be VISIBLE (MR OFF). When
/// mixed-reality turns ON, <see cref="MixedReality"/> hides the sphere for the chroma key via
/// its own <c>HideSkyMeshes</c> path; this stands down first (restores the renderer/material)
/// so the two never fight, and re-applies when MR turns back off. Teardown on VR stop / scene
/// change / hot reload restores the sphere to vanilla.
/// </summary>
internal static class SkyBackdrop
{
    /// <summary>Case-insensitive fragments that mark a shader property as a depth-write toggle.</summary>
    private static readonly string[] DepthWriteHints =
    {
        "zwrite", "z_write", "depthwrite", "depth_write", "writedepth", "write_depth",
    };

    /// <summary>Name/shader fragments that identify the scenario sky sphere.</summary>
    private static readonly string[] SkyHints =
    {
        "gh_skysphere", "skysphere", "amp_skyshader", "amp_sky", "skyshader",
    };

    private const int ScanIntervalFrames = 60;

    /// <summary>
    /// Radius of the depth-reset sphere as a fraction of the head camera's far clip plane.
    /// Just under 1 so the sphere is never clipped by the far plane (which would leave those
    /// directions un-reset), while writing ~far depth so ALL foreground geometry passes ZTest.
    /// </summary>
    private const float DepthResetFarFraction = 0.98f;

    private enum Mechanism { Undecided, ZWriteOff, DepthResetCB }

    // Acquired target + decision (persist until the sphere instance dies / VR stops).
    private static Renderer? _sky;
    private static Material? _mat;
    private static Mechanism _mech;

    // Restore bookkeeping (captured once in Decide, replayed on RemoveEffects).
    private static int _savedRenderQueue;
    private static bool _savedQueueValid;
    private static string? _zwriteProp;
    private static float _savedZWrite;
    private static bool _renderingForcedOff;

    // DepthResetCB route — a command buffer bound to the current head camera.
    private static CommandBuffer? _cb;
    private static Camera? _cbCamera;

    // DepthResetCB route — the depth-reset draw assets (built lazily, reused).
    private static Material? _resetMat;   // Overlay shader forced ZWrite-On/ZTest-Always/Blend-Zero-One
    private static Mesh? _resetMesh;      // a unit sphere (built-in primitive mesh)
    private static bool _resetWarned;     // one-shot warning when the Overlay material is unavailable

    private static bool _applied;   // mechanism effects currently active
    private static int _scanNextFrame;

    private static readonly int BackgroundQueue = (int)RenderQueue.Background; // 1000

    /// <summary>
    /// Per-frame driver, called from <see cref="MixedReality.Tick"/>.
    /// <paramref name="mrHidingSky"/> is true while MR owns the sphere (chroma key) — we then
    /// stand down completely so MR can hide it. Otherwise the backdrop fix is applied/held.
    /// Self-gates on <see cref="VRSession.IsRunning"/>: tears everything down when VR stops.
    /// </summary>
    internal static void Tick(bool mrHidingSky)
    {
        if (!VRSession.IsRunning)
        {
            if (_mech != Mechanism.Undecided || _applied)
                FullReset();
            return;
        }

        if (mrHidingSky)
        {
            // MR hides the sphere for the chroma key — restore it to vanilla first so its
            // HideSkyMeshes path records/disables a clean renderer. Keep the decision cached.
            if (_applied)
                RemoveEffects();
            return;
        }

        EnsureAcquired();
        if (_sky == null || _mat == null)
            return;
        ApplyEffects();
    }

    /// <summary>Full teardown (VR stop / hot reload). Restores the sphere and forgets the decision.</summary>
    internal static void RestoreAll() => FullReset();

    // ---- acquisition --------------------------------------------------------------------------

    private static void EnsureAcquired()
    {
        // Unity fake-null: the sphere was destroyed by a scene unload → forget it and re-scan.
        if (_sky == null || _mat == null)
        {
            if (_mech != Mechanism.Undecided || _applied || _sky != null || _mat != null)
                FullReset();

            if (Time.frameCount < _scanNextFrame)
                return;
            _scanNextFrame = Time.frameCount + ScanIntervalFrames;

            Renderer? found = FindSky();
            if (found == null)
                return;
            Material? mat = found.sharedMaterial;
            if (mat == null || mat.shader == null)
                return;

            _sky = found;
            _mat = mat;
            Decide();
        }
    }

    private static Renderer? FindSky()
    {
        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null)
                continue;
            if (MatchesHint(r.gameObject.name))
                return r;
            Material? m = r.sharedMaterial;
            if (m != null && m.shader != null && MatchesHint(m.shader.name))
                return r;
        }
        return null;
    }

    private static bool MatchesHint(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        for (int i = 0; i < SkyHints.Length; i++)
        {
            if (s!.IndexOf(SkyHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // ---- decision + investigation -------------------------------------------------------------

    private static void Decide()
    {
        Material mat = _mat!;
        Shader shader = mat.shader;

        // Investigate FIRST — dump the shader's property inventory + the material queue so the
        // hardware log confirms exactly what the shader exposes (and which mechanism ran).
        int count = 0;
        try { count = shader.GetPropertyCount(); }
        catch (Exception e) { VRLog.Warn("Core", $"SkyBackdrop: GetPropertyCount threw ({e.GetType().Name}) — treating shader as opaque."); }

        var sb = new StringBuilder(256);
        _zwriteProp = null;
        for (int i = 0; i < count; i++)
        {
            string pname;
            ShaderPropertyType ptype;
            try
            {
                pname = shader.GetPropertyName(i);
                ptype = shader.GetPropertyType(i);
            }
            catch (Exception) { continue; }

            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(pname).Append(':').Append(ptype);

            // First numeric property whose name reads like a depth-write toggle wins.
            if (_zwriteProp == null && IsNumeric(ptype) && HasDepthWriteHint(pname))
                _zwriteProp = pname;
        }

        VRLog.Info("Core", $"SkyBackdrop: found sky sphere '{_sky!.gameObject.name}' (shader '{shader.name}', " +
                           $"material.renderQueue {mat.renderQueue}); {count} shader propert{(count == 1 ? "y" : "ies")}" +
                           $"{(sb.Length > 0 ? $": {sb}" : " (none).")}");

        _savedRenderQueue = mat.renderQueue;
        _savedQueueValid = true;
        RemoveCB(); // rebuilt by ApplyEffects if the CB route is chosen

        if (_zwriteProp != null)
        {
            _mech = Mechanism.ZWriteOff;
            _savedZWrite = SafeGetFloat(mat, _zwriteProp);
            VRLog.Info("Core", $"SkyBackdrop mechanism = ZWriteOff: shader exposes depth-write property " +
                               $"'{_zwriteProp}' (was {_savedZWrite:0.##}) → forcing it to 0 and dropping the material to " +
                               $"renderQueue Background({BackgroundQueue}). The sky sphere writes no depth, so floated " +
                               $"menus / the moved board / the laser in front of it are never occluded. Reversible.");
        }
        else
        {
            _mech = Mechanism.DepthResetCB;
            VRLog.Info("Core", $"SkyBackdrop mechanism = DepthResetCB: no depth-write property among {count} " +
                               $"(shader hard-codes ZWrite On) → the sky sphere's AUTOMATIC draw is suppressed via " +
                               $"Renderer.forceRenderingOff and it is redrawn by a BeforeForwardOpaque command buffer " +
                               $"that (1) draws the sphere with its OWN animated AMP_SkyShader material (COLOUR VISIBLE) " +
                               $"then (2) DRAWS a head-centred far sphere through the bundled Overlay shader forced to " +
                               $"ZWrite-On/ZTest-Always/Blend-Zero-One — an ordinary DRAW that overwrites depth to ~far " +
                               $"WITHOUT touching colour (NO mid-pass depth CLEAR, which greyed the colour to BLACK on the " +
                               $"tiled Quest GPU in attempt #1). Scene geometry then tests against ~far depth, so the sky " +
                               $"occludes nothing — menus, board and laser always show, and the sky COLOUR is now visible. Reversible.");
        }
    }

    private static bool IsNumeric(ShaderPropertyType t) =>
        t == ShaderPropertyType.Float || t == ShaderPropertyType.Range || t == ShaderPropertyType.Int;

    private static bool HasDepthWriteHint(string name)
    {
        for (int i = 0; i < DepthWriteHints.Length; i++)
        {
            if (name.IndexOf(DepthWriteHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static float SafeGetFloat(Material m, string prop)
    {
        try { return m.GetFloat(prop); }
        catch (Exception) { return 1f; }
    }

    // ---- apply / remove (idempotent per-frame) ------------------------------------------------

    private static void ApplyEffects()
    {
        Material mat = _mat!;

        if (_mech == Mechanism.ZWriteOff)
        {
            if (mat.renderQueue != BackgroundQueue)
                mat.renderQueue = BackgroundQueue;
            if (_zwriteProp != null && mat.HasProperty(_zwriteProp) && mat.GetFloat(_zwriteProp) != 0f)
                mat.SetFloat(_zwriteProp, 0f);
        }
        else if (_mech == Mechanism.DepthResetCB)
        {
            // Take the sphere out of the head camera's AUTOMATIC opaque draw so only the command
            // buffer draws it (deterministic order: sphere first, depth reset second, both BEFORE
            // any scene opaque draw). Use forceRenderingOff, NOT enabled=false: disabling the
            // renderer removes it from Unity's culling/visible set, and CommandBuffer.DrawRenderer
            // on a culled renderer produces NO draw — that is why the earlier build showed a BLACK
            // sky (sphere neither auto-drawn nor CB-drawn). forceRenderingOff suppresses only the
            // automatic draw while keeping the renderer culled/prepared, so DrawRenderer has valid
            // render data and its COLOUR shows.
            if (!_sky!.forceRenderingOff)
            {
                _sky.forceRenderingOff = true;
                _renderingForcedOff = true;
            }
            EnsureResetAssets();
            EnsureCB();
            // Rebuild every frame: the depth-reset sphere follows the (moving) head and scales
            // with the live far clip plane (TickClipPlanes changes it under WorldGrab zoom).
            if (_cb != null)
                BuildCBCommands();
        }

        _applied = true;
    }

    private static void RemoveEffects()
    {
        Material? mat = _mat;
        if (mat != null)
        {
            if (_savedQueueValid)
                mat.renderQueue = _savedRenderQueue;
            if (_mech == Mechanism.ZWriteOff && _zwriteProp != null && mat.HasProperty(_zwriteProp))
                mat.SetFloat(_zwriteProp, _savedZWrite);
        }
        if (_renderingForcedOff && _sky != null)
            _sky.forceRenderingOff = false;
        _renderingForcedOff = false;
        RemoveCB();
        _applied = false;
    }

    private static void FullReset()
    {
        RemoveEffects();
        _sky = null;
        _mat = null;
        _mech = Mechanism.Undecided;
        _zwriteProp = null;
        _savedQueueValid = false;
        _scanNextFrame = 0;

        // Drop the depth-reset material (ours). The reset MESH is a shared built-in primitive
        // asset — never destroy it, just release the reference so a fresh one is fetched if the
        // static survived a hot reload.
        if (_resetMat != null)
        {
            UnityEngine.Object.Destroy(_resetMat);
            _resetMat = null;
        }
        _resetMesh = null;
        _resetWarned = false;
    }

    // ---- command buffer (DepthResetCB route) --------------------------------------------------

    /// <summary>
    /// Build (once, reused) the depth-reset assets: a unit sphere mesh and an Overlay-shader
    /// material forced to write depth without touching colour. Both are cheap and idempotent.
    /// </summary>
    private static void EnsureResetAssets()
    {
        if (_resetMesh == null)
        {
            // Grab the built-in unit sphere mesh. DestroyImmediate the temporary GameObject in
            // the SAME frame (we are in Update, before rendering) so its MeshRenderer never draws
            // a stray white sphere at the origin for a frame. The mesh itself is a shared built-in
            // asset and survives the GameObject's destruction.
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            MeshFilter? mf = tmp.GetComponent<MeshFilter>();
            _resetMesh = mf != null ? mf.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(tmp);
        }

        if (_resetMat == null)
        {
            Shader? overlay = PlayTray.OverlayShader();
            if (overlay != null)
            {
                _resetMat = new Material(overlay) { name = "GloomhavenVR.SkyBackdrop.DepthReset" };
                _resetMat.SetFloat("_ZTest", (int)CompareFunction.Always); // always write, whatever depth is there
                _resetMat.SetFloat("_ZWrite", 1f);                          // WRITE depth (reset it to ~far)
                _resetMat.SetFloat("_Cull", (int)CullMode.Off);            // seen from inside → draw both sides
                _resetMat.SetFloat("_SrcBlend", (int)BlendMode.Zero);      // colour result = 0*src + 1*dst
                _resetMat.SetFloat("_DstBlend", (int)BlendMode.One);       //   = dst UNCHANGED (sky colour kept)
            }
        }
    }

    private static void EnsureCB()
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
        {
            RemoveCB();
            return;
        }
        if (_cb != null && ReferenceEquals(_cbCamera, head))
            return; // already bound to the live head camera

        RemoveCB();
        _cb = new CommandBuffer { name = "GloomhavenVR.SkyBackdrop.DepthReset" };
        _cbCamera = head;
        BuildCBCommands();
        head.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, _cb);
        VRLog.Info("Core", $"SkyBackdrop: depth-reset command buffer attached to head camera '{head.name}' " +
                           $"(BeforeForwardOpaque) — sky COLOUR redrawn (forceRenderingOff sphere, visible) then depth " +
                           $"OVERWRITTEN to ~far by an Overlay-shader draw (no mid-pass depth clear) so it occludes nothing.");
    }

    private static void BuildCBCommands()
    {
        CommandBuffer cb = _cb!;
        cb.Clear();

        // (1) Draw the sky sphere with its OWN material(s) — full ANIMATED AMP_SkyShader look,
        //     colour + its (near, shell) depth.
        Renderer sky = _sky!;
        Material[] mats = sky.sharedMaterials;
        int subs = SubmeshCount(sky);
        for (int i = 0; i < subs; i++)
        {
            Material m = (i < mats.Length && mats[i] != null) ? mats[i] : _mat!;
            cb.DrawRenderer(sky, m, i, -1); // submesh i, all passes
        }

        // (2) DEPTH RESET via a DRAW (not a clear). A head-centred sphere at ~the far plane,
        //     drawn with the Overlay shader forced to ZWrite-On / ZTest-Always / Blend-Zero-One:
        //     it OVERWRITES the depth buffer to ~far in every view direction while leaving the
        //     colour untouched. The scene opaque + mod visuals that render afterwards all pass
        //     ZTest against ~far, so the sky's shell depth can occlude nothing. An ordinary draw
        //     is reliable on tiled mobile GPUs — unlike the depth-only ClearRenderTarget that
        //     greyed the colour to black on Quest (attempt #1).
        Camera? head = _cbCamera;
        if (_resetMat != null && _resetMesh != null && head != null)
        {
            float far = Mathf.Max(1f, head.farClipPlane);
            // Primitive sphere has diameter 1 (radius 0.5); scale s ⇒ radius 0.5*s. Want
            // radius = DepthResetFarFraction * far ⇒ s = 2 * fraction * far.
            float scale = 2f * DepthResetFarFraction * far;
            Matrix4x4 m = Matrix4x4.TRS(head.transform.position, Quaternion.identity, Vector3.one * scale);
            cb.DrawMesh(_resetMesh, m, _resetMat, 0, -1);
        }
        else if (!_resetWarned)
        {
            _resetWarned = true;
            VRLog.Warn("Core", "SkyBackdrop: Overlay depth-reset material unavailable (gloomhavenvr.bundle " +
                               "missing the 'GloomhavenVR/Overlay' shader?) — the sky is still drawn (VISIBLE) but its " +
                               "depth is NOT reset, so it may occlude foreground until the bundle is updated.");
        }
    }

    private static int SubmeshCount(Renderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
            return Mathf.Max(1, mf.sharedMesh.subMeshCount);
        if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            return Mathf.Max(1, smr.sharedMesh.subMeshCount);
        return 1;
    }

    private static void RemoveCB()
    {
        if (_cb != null)
        {
            if (_cbCamera != null) // Unity fake-null: a destroyed camera already dropped the buffer
                _cbCamera.RemoveCommandBuffer(CameraEvent.BeforeForwardOpaque, _cb);
            _cb.Dispose();
            _cb = null;
        }
        _cbCamera = null;
    }
}
