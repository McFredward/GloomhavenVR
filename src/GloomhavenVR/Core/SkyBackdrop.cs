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
/// HOW THE SKY REACHES THE VR VIEW (diagnosed, third attempt)
/// ----------------------------------------------------------
/// In a SCENARIO the rig head camera (<c>GloomhavenVR.HeadCamera</c>) is the ONE stereo
/// renderer that reaches the HMD (<see cref="VRCameraPolicy"/>), and its culling mask is
/// <c>ComposeHeadMask(anchor.cullingMask)</c> = the game camera's mask OR the mod layer
/// (<c>VRRigDriver.ComposeHeadMask</c>). So the SAME head camera draws BOTH the sky sphere
/// (layer 0) AND the mod's world-space elements (layer 27) into ONE colour+depth buffer.
/// The occlusion is therefore a DEPTH problem inside a single camera — NOT a compositing /
/// FlatScreen-RT problem (FlatScreen only owns the flat 2D menu in Menu2D; in a scenario it
/// is hidden and the sky is drawn straight by the head camera). If the sphere mesh is not
/// actually DRAWN, the head camera's clear (SolidColor <c>[Rig] VoidColor</c>, default BLACK,
/// or a null Skybox) is all that remains → a BLACK sky. That is the failure mode below.
///
/// THE PROBLEM
/// -----------
/// The player is INSIDE the sphere. <c>AMP_SkyShader</c> WRITES DEPTH and exposes no plain
/// <c>_ZWrite</c> (confirmed by the property dump in <see cref="Decide"/>), so raising the
/// render queue alone does NOT stop occlusion: the dome is SQUASHED (y-extent only ~71 world
/// units), so a foreground object dragged toward/past its surface — especially vertically —
/// ends up geometrically BEHIND the shell, the sphere's depth is nearer, and the object (or
/// the ZTest-LEqual laser) gets clipped.
///
/// WHY THE TWO PRIOR ATTEMPTS RENDERED BLACK (root cause)
/// -----------------------------------------------------
/// Both prior fixes were the SAME family: SUPPRESS the sphere's automatic draw and REDRAW it
/// from a <see cref="CommandBuffer"/>.
///   - v1 (<c>enabled = false</c> + CB <see cref="CommandBuffer.DrawRenderer"/> + depth-only
///     <c>ClearRenderTarget</c>): BLACK.
///   - v2 (<see cref="Renderer.forceRenderingOff"/> = true + CB DrawRenderer + a depth-writing
///     far-sphere DRAW): STILL BLACK on hardware (Quest 3 + Virtual Desktop/VDXR).
/// Diagnosis: <b>suppressing a renderer ALSO suppresses <c>CommandBuffer.DrawRenderer</c> for
/// it.</b> Both <c>enabled = false</c> and <c>forceRenderingOff = true</c> drop the renderer
/// from the camera's prepared/culled render data, and <c>DrawRenderer</c> has NO batch to
/// issue for a renderer with no render data — so the sphere was drawn NEITHER automatically
/// NOR by the command buffer. Its colour was never written; the head camera's black void clear
/// showed through. The whole "suppress + CB-redraw the SAME renderer" family cannot work here,
/// which is why v1 and v2 both went black for the same reason.
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
///  - <b>DepthResetRenderer</b> — if NO depth-write property exists (the shader hard-codes
///    <c>ZWrite On</c>): the robust STRUCTURAL fix that does NOT suppress the sphere at all,
///    so its OWN vanilla automatic draw keeps rendering the full ANIMATED AMP_SkyShader look
///    (COLOUR GUARANTEED VISIBLE — it is the exact path that already works on the flat game).
///    We only do two things:
///      (1) drop the sphere material to <see cref="RenderQueue.Background"/> (1000) so the sky
///          draws FIRST (colour + its near shell depth), BEFORE scene opaque geometry; then
///      (2) add a REAL depth-reset renderer — a head-FACING flat QUAD at ~the far plane, a
///          persistent GameObject on the MOD LAYER (so ONLY the head camera draws it — it can
///          never pollute a game camera's depth), whose material is the bundled
///          <c>GloomhavenVR/Overlay</c> shader forced to <c>ZTest Always, ZWrite On,
///          Blend Zero One, Cull Off</c> with its <c>renderQueue pinned to 1999</c>
///          (Geometry-1). Because Unity renders opaque materials in ASCENDING render-queue
///          order, this draw lands DETERMINISTICALLY AFTER the sky (1000) and BEFORE all scene
///          opaque geometry (2000+). <c>Blend Zero One</c> leaves the COLOUR buffer untouched
///          (result = dst — the sky colour survives), while <c>ZWrite On</c> + <c>ZTest Always</c>
///          OVERWRITES the depth buffer to ~far. The scene's opaque geometry + all mod visuals
///          then render against a depth buffer that no longer holds any near sky-shell depth →
///          the sky is a pure colour backdrop that can occlude nothing, yet is fully VISIBLE.
///          Holds ALWAYS (board and laser benefit too).
///
///      WHY A FLAT QUAD, NOT A HEAD-CENTRED SPHERE (fixes the "see-through inside a hex
///      highlight that moves with the head"): a sphere centred on the head writes depth
///      R·cos(theta) — a FIXED screen-space RADIAL gradient (farthest at screen centre, ~30%
///      nearer at the edge of a 90° FOV). Where a highlighted hex overhangs the VOID around the
///      floating diorama (no opaque board writes real depth there), that reset depth IS the scene
///      depth the game's hex-highlight glow reads back (its border-flame/crosshair is a
///      depth-FADING transparent effect). A world-fixed hex sliding across that screen-fixed
///      radial gradient as the head moves made the fade shimmer inside the white = "moves with the
///      head." A flat quad PERPENDICULAR to the view axis has CONSTANT view-space z → a UNIFORM
///      depth across the whole screen (no radial gradient), so the readback no longer depends on
///      where the hex sits on screen → the head-tracking reveal is gone. Occlusion and sky
///      visibility are unchanged (still ~far, still colour = dst).
///
///    WHY THIS SHOWS COLOUR WHERE v1/v2 WENT BLACK: the sphere is NEVER suppressed — no
///    <c>enabled=false</c>, no <c>forceRenderingOff</c>, no CommandBuffer.DrawRenderer of it.
///    Its automatic draw is the vanilla, known-working colour path; we only reorder it and
///    reset depth after it with an ORDINARY opaque DRAW (the bread-and-butter of a tiled mobile
///    renderpass — no mid-pass depth CLEAR, which greyed the colour to black in attempt #1).
///
///    ORDERING IS BY RENDER QUEUE, NOT CAMERA EVENTS: a CameraEvent can only inject BEFORE all
///    opaque (BeforeForwardOpaque) or AFTER all opaque — there is no "between the sky and the
///    rest" event, which is exactly why the CB family had to suppress+reorder. Pinning the sky
///    to 1000 and the reset to 1999 gives that "between" slot with NO suppression.
///
///    This mechanism uses ONE camera (the rig head camera) — no second/stereo camera and no
///    culling-mask/stereo-policy changes. The reset renderer inherits the head camera's per-eye
///    matrices like any other object it draws.
///
/// SEPARATION FROM MR: this runs only while the sky is meant to be VISIBLE (MR OFF). When
/// mixed-reality turns ON, <see cref="MixedReality"/> hides the sphere for the chroma key via
/// its own <c>HideSkyMeshes</c> path; this stands down first (restores the renderer/material
/// and destroys the reset renderer) so the two never fight, and re-applies when MR turns back
/// off. The reset renderer lives on the MOD LAYER, which MR's sweep explicitly skips, and it is
/// destroyed before MR's sweep runs anyway. Teardown on VR stop / scene change / hot reload
/// restores the sphere to vanilla.
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
    /// Distance of the depth-reset QUAD in front of the head, as a fraction of the head camera's
    /// far clip plane. Just under 1 so the plane is never clipped by the far plane (which would
    /// leave the view un-reset), while writing ~far depth so ALL foreground geometry passes ZTest.
    /// </summary>
    private const float DepthResetFarFraction = 0.98f;

    /// <summary>
    /// How much larger than the reset distance the head-facing reset quad is drawn, so a single
    /// flat plane fully covers even a very wide / asymmetric VR (per-eye) frustum with generous
    /// margin. A large factor is FREE here — the quad writes no colour and a UNIFORM depth, so
    /// oversizing costs nothing visually and only guarantees full frustum coverage.
    /// </summary>
    private const float DepthResetCoverFactor = 8f;

    /// <summary>
    /// Render queue for the depth-reset draw: Geometry-1 (1999). It MUST fall strictly AFTER
    /// the sky (pinned to Background = 1000) and strictly BEFORE all scene opaque geometry
    /// (Geometry = 2000), so Unity's ascending render-queue order slots it exactly "between the
    /// sky and the rest" without any renderer suppression or camera-event trickery.
    /// </summary>
    private static readonly int DepthResetQueue = (int)RenderQueue.Geometry - 1; // 1999

    private enum Mechanism { Undecided, ZWriteOff, DepthResetRenderer }

    // Acquired target + decision (persist until the sphere instance dies / VR stops).
    private static Renderer? _sky;
    private static Material? _mat;
    private static Mechanism _mech;

    // Restore bookkeeping (captured once in Decide, replayed on RemoveEffects).
    private static int _savedRenderQueue;
    private static bool _savedQueueValid;
    private static string? _zwriteProp;
    private static float _savedZWrite;

    // DepthResetRenderer route — a real, head-facing depth-reset quad drawn by the head
    // camera only (mod layer). Assets (mesh + material) are built lazily and reused.
    private static GameObject? _resetGo;   // hosts the reset MeshRenderer (mod layer)
    private static MeshRenderer? _resetRenderer;
    private static Material? _resetMat;    // Overlay shader forced ZWrite-On/ZTest-Always/Blend-Zero-One, queue 1999
    private static Mesh? _resetMesh;       // a unit quad (built-in primitive mesh) — head-facing far plane
    private static bool _resetWarned;      // one-shot warning when the Overlay material is unavailable

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

        VRLog.Info("Core", $"SkyBackdrop: found sky sphere '{_sky!.gameObject.name}' (layer {_sky.gameObject.layer}, " +
                           $"shader '{shader.name}', material.renderQueue {mat.renderQueue}); {count} shader " +
                           $"propert{(count == 1 ? "y" : "ies")}{(sb.Length > 0 ? $": {sb}" : " (none).")}");

        _savedRenderQueue = mat.renderQueue;
        _savedQueueValid = true;
        RemoveResetObject(); // rebuilt by ApplyEffects if the reset route is chosen

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
            _mech = Mechanism.DepthResetRenderer;
            VRLog.Info("Core", $"SkyBackdrop mechanism = DepthResetRenderer: no depth-write property among {count} " +
                               $"(shader hard-codes ZWrite On) → the sphere is NOT suppressed (its OWN automatic draw " +
                               $"keeps rendering the animated AMP_SkyShader COLOUR — the vanilla path that already works, " +
                               $"so the sky is VISIBLE). We drop its material to renderQueue Background({BackgroundQueue}) " +
                               $"so it draws FIRST, then a REAL depth-reset renderer (mod-layer, head-only) at " +
                               $"renderQueue {DepthResetQueue} draws a head-FACING far quad (uniform depth) through the bundled Overlay " +
                               $"shader (ZWrite-On/ZTest-Always/Blend-Zero-One): an ORDINARY opaque DRAW that overwrites " +
                               $"depth to ~far WITHOUT touching colour. Ascending render-queue order slots it AFTER the sky " +
                               $"(1000) and BEFORE scene opaque (2000), so the sky occludes nothing yet stays visible. " +
                               $"NO renderer suppression (which killed the CB draw → BLACK in the prior two attempts) and " +
                               $"NO mid-pass depth clear (which greyed colour to BLACK on the tiled Quest GPU). Reversible.");
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
        else if (_mech == Mechanism.DepthResetRenderer)
        {
            // (1) Pin the sky FIRST (Background) so it draws before the depth reset and before
            //     scene opaque. The sphere is NEVER suppressed — its automatic draw renders the
            //     colour, exactly the vanilla path (this is why the sky is VISIBLE where the two
            //     CB-suppress attempts went BLACK: suppressing the renderer also kills its CB
            //     DrawRenderer, so it was drawn by nothing).
            if (mat.renderQueue != BackgroundQueue)
                mat.renderQueue = BackgroundQueue;

            // (2) A real, head-FACING depth-reset quad at queue 1999: overwrites depth to a
            //     UNIFORM ~far AFTER the sky, BEFORE scene opaque — an ordinary draw, tiled-GPU safe.
            EnsureResetAssets();
            EnsureResetObject();
            UpdateResetObject(); // follows the (moving) head, scales with the live far clip plane
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
        RemoveResetObject();
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

    // ---- depth-reset renderer (DepthResetRenderer route) --------------------------------------

    /// <summary>
    /// Build (once, reused) the depth-reset assets: a unit quad mesh and an Overlay-shader
    /// material forced to write depth without touching colour, pinned to queue 1999. Both are
    /// cheap and idempotent. A flat quad (not a sphere) so the written depth is UNIFORM across the
    /// screen — a head-centred sphere writes a screen-space radial depth gradient that a
    /// depth-fading hex highlight over the void reads back as a head-tracking see-through artifact.
    /// </summary>
    private static void EnsureResetAssets()
    {
        if (_resetMesh == null)
        {
            // Grab the built-in unit quad mesh (1x1 in XY, normal ±Z). DestroyImmediate the
            // temporary GameObject in the SAME frame (we are in Update, before rendering) so its
            // MeshRenderer never draws a stray quad at the origin for a frame. The mesh itself is a
            // shared built-in asset and survives the GameObject's destruction.
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
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
                _resetMat.SetFloat("_Cull", (int)CullMode.Off);            // draw regardless of quad facing
                _resetMat.SetFloat("_SrcBlend", (int)BlendMode.Zero);      // colour result = 0*src + 1*dst
                _resetMat.SetFloat("_DstBlend", (int)BlendMode.One);       //   = dst UNCHANGED (sky colour kept)
                // Pin the draw AFTER the sky (Background 1000) and BEFORE scene opaque (2000):
                // Unity renders opaque materials in ascending render-queue order.
                _resetMat.renderQueue = DepthResetQueue;
            }
        }
    }

    /// <summary>
    /// Create (or re-create after an external destroy) the persistent depth-reset GameObject:
    /// a MeshRenderer on the MOD LAYER (so ONLY the head camera draws it — a game camera can
    /// never pick it up and have its own depth reset), no shadows, our reset material. Cheap and
    /// idempotent; the transform is refreshed every frame by <see cref="UpdateResetObject"/>.
    /// </summary>
    private static void EnsureResetObject()
    {
        if (_resetMesh == null || _resetMat == null)
        {
            if (!_resetWarned)
            {
                _resetWarned = true;
                VRLog.Warn("Core", "SkyBackdrop: Overlay depth-reset material unavailable (gloomhavenvr.bundle " +
                                   "missing the 'GloomhavenVR/Overlay' shader?) — the sky is still drawn (VISIBLE) but its " +
                                   "depth is NOT reset, so it may occlude foreground until the bundle is updated.");
            }
            return;
        }
        if (_resetGo != null)
            return;

        _resetGo = new GameObject("GloomhavenVR.SkyBackdrop.DepthReset");
        _resetGo.transform.localScale = Vector3.one;
        // Mod layer: the head camera always renders it in a scenario (ComposeHeadMask ORs the
        // mod layer in), while NO game camera does — the depth reset stays confined to the HMD
        // view and never touches the flat game / FlatScreen RT composites.
        _resetGo.layer = VRLayers.ModLayer;

        MeshFilter mf = _resetGo.AddComponent<MeshFilter>();
        mf.sharedMesh = _resetMesh;
        _resetRenderer = _resetGo.AddComponent<MeshRenderer>();
        _resetRenderer.sharedMaterial = _resetMat;
        _resetRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _resetRenderer.receiveShadows = false;
        _resetRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _resetRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        VRLog.Info("Core", $"SkyBackdrop: depth-reset renderer created (mod layer {VRLayers.ModLayer}, " +
                           $"renderQueue {DepthResetQueue}) — a head-FACING far quad that overwrites depth to a UNIFORM ~far " +
                           $"AFTER the Background sky draws and BEFORE scene opaque, via an ordinary Overlay-shader draw " +
                           $"(Blend Zero One leaves the sky colour untouched). Uniform depth (flat plane, not a head-centred " +
                           $"sphere) so a depth-fading hex highlight over the void reads no head-tracking see-through. " +
                           $"No renderer suppression, no depth clear.");
    }

    /// <summary>
    /// Refresh the reset quad's pose every frame: a head-FACING flat plane placed just under the
    /// live far clip plane (TickClipPlanes changes it under WorldGrab zoom), oversized to cover the
    /// whole frustum. Because the plane is PERPENDICULAR to the view axis its view-space z — and
    /// therefore its written depth — is UNIFORM across the screen: no head-centred radial gradient,
    /// so a depth-fading hex highlight over the void no longer reads a head-tracking see-through.
    /// Re-posed here in Update (before rendering) so it faces the CURRENT head orientation with no
    /// latency; the large cover factor absorbs the two per-eye (stereo) frustums and any asymmetry.
    /// </summary>
    private static void UpdateResetObject()
    {
        if (_resetGo == null)
            return;
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
            return;

        Transform ht = head.transform;
        float far = Mathf.Max(1f, head.farClipPlane);
        float dist = DepthResetFarFraction * far;   // just under the far plane → not clipped
        // Unit quad lies in local XY (1x1); scaling X/Y sizes it, its normal follows local Z.
        // Placing it at head + forward*dist with the head's rotation makes the quad plane
        // perpendicular to the view axis at a constant view-space z = dist ⇒ uniform depth.
        float size = DepthResetCoverFactor * dist;
        Transform t = _resetGo.transform;
        t.position = ht.position + ht.forward * dist;
        t.rotation = ht.rotation;
        t.localScale = new Vector3(size, size, 1f);
    }

    private static void RemoveResetObject()
    {
        if (_resetGo != null)
        {
            UnityEngine.Object.Destroy(_resetGo);
            _resetGo = null;
        }
        _resetRenderer = null;
    }
}
