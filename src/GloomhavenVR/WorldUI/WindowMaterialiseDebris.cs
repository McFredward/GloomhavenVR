using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// <b>THE DEBRIS: REAL SOLIDS IN THE ROOM, NOT A DECAL ON THE PANE.</b>
///
/// <para>User, 2026-08-26, on what shipped in ModBuild 292/293: <i>"Ich mag die Fenster ein- und
/// ausblend-Animation nicht. Ich will eher, dass es wirkliche Partikeleffekte in der 3D-Umgebung
/// auslöst, aktuell ist es eher ein 2D-Effekt."</i> He is describing the mechanism accurately. The
/// old effect painted its flakes on ONE quad parented to the window's host rect, in the window's own
/// plane, with <c>ZWrite Off</c>. Nothing it drew ever left that plane, could ever be nearer or
/// further than the window, or could ever be hidden by a table leg. It was a decal, and it read as
/// one.</para>
///
/// <para>This class builds, per animating window, a mesh of <b>tetrahedral shards</b> — closed
/// four-faced solids, each torn from the position of a real element of the window, each with its own
/// size, its own tumble axis and its own out-of-plane launch velocity. They are flown entirely in
/// the vertex stage as a function of one uniform, they write depth, and they are drawn on two
/// renderers that bracket the window in the panel draw ladder. The result is debris that is
/// occluded by the room, occludes the room, passes visibly in front of and behind the window, and
/// separates from it with real parallax when the player leans.</para>
///
/// <para><b>NO HEAD POSE ANYWHERE.</b> User, 2026-08-26: <i>"Der Effekt soll nicht an den
/// Kopfbewegungen gebunden sein"</i>. This is why the shards are SOLIDS with their own orientation
/// and not billboards. Unity's default <c>ParticleSystemRenderMode.Billboard</c> orients every quad
/// toward the rendering camera, so the debris would rotate as he turned his head — the exact thing
/// he refused — and under MultiPass that orientation is computed once per eye, which is this
/// project's known route to stereo rivalry. Nothing in this file, and nothing in the shader it
/// drives, reads a camera position, a view matrix, a screen position or a depth texture. That is
/// checked mechanically by the preview's identifier audit, not asserted.</para>
///
/// <para><b>WHY NOT A <c>ParticleSystem</c>.</b> The integrator's brief allowed one if it were
/// pooled, and named <c>ParticleSystemRenderMode.Mesh</c> as the preferred shape. This is that
/// shape, without the component, for four reasons that are all specific to this effect rather than
/// general dislike:</para>
/// <list type="number">
/// <item><b>Scaling.</b> <c>ParticleSystemScalingMode.Local</c> ignores hierarchy scale by design
///   and is the default; this project has measured 37 of 43 of the game's own systems taking it,
///   and the rig runs at ~9.57 world units per metre with the map-room table at 198. Every size in
///   this file is authored in APPARENT METRES and converted once, from the panel's own measured
///   <c>lossyScale</c> and the live rig scale, with every step of the conversion logged. There is
///   no mode to get wrong.</item>
/// <item><b>Emission.</b> The whole point of the redesign is that a shard leaves from where the
///   window actually broke up. That means seeding each particle at a point inside a specific,
///   currently-visible <c>CanvasRenderer</c>'s rect and giving it that point's erosion threshold. A
///   <c>ParticleSystem</c> can be told to do that only through one <c>Emit(EmitParams)</c> call per
///   particle, and it still has nowhere to carry the threshold.</item>
/// <item><b>Cost.</b> A <c>ParticleSystem</c> simulates every frame, per window, on the CPU. This
///   mesh is built once per effect and then costs <b>two <c>SetFloat</c>s and one
///   <c>SetPropertyBlock</c> per frame</b>, because every particle's whole trajectory is a closed
///   function of the one progress uniform. The budget is 11.11 ms and the mod already spends a real
///   fraction of it.</item>
/// <item><b>Interruption.</b> A pooled system has to be <c>Clear()</c>ed and fully reset on all
///   fourteen rows of the interruption matrix. A mesh on a mod-owned child dies with its carrier;
///   "the host was destroyed under us" is <c>OnDestroy</c> and needs no reset at all.</item>
/// </list>
///
/// <para><b>THE WINDING GATE.</b> Nine meshes have now shipped in this project wound against the
/// side they are seen from. A shard is a CLOSED solid, so the strong form of the gate is available
/// and is used: every built mesh is checked for positive signed volume AND for every face normal
/// pointing away from the shard's own centroid, and a failure is logged as a code fault. The UV half
/// of the usual gate is <b>not applicable and is not faked</b>: a shard carries no texture
/// coordinates and the shader samples no texture, so a UV area would be a number with no meaning.
/// The outward-normal test is the property that gate is really about, and it is the stronger one
/// here.</para>
/// </summary>
internal static partial class WindowMaterialise
{
    /// <summary>
    /// The debris object's name, and it MATTERS that it starts with <c>GloomhavenVR.</c>. The
    /// liveness rule that tears down an empty float (<c>ModalFallback.9.Spawn.cs</c>,
    /// <c>MeasureDrawsSomething</c> / <c>DrawsAnythingLoose</c>) treats any enabled <c>Renderer</c>
    /// as proof the window still draws something — EXCEPT ones whose GameObject name starts with
    /// this prefix. Without it, a mod-owned decoration would keep a genuinely dead window alive
    /// forever, which is the exact defect the user's ruling <i>"Es darf niemals leere Fenster
    /// geben"</i> was written against.
    /// </summary>
    internal const string DebrisName = "GloomhavenVR.WindowMaterialiseDebris";

    /// <summary>
    /// <b>Ladder offsets, and why there are two renderers rather than one.</b>
    ///
    /// <para>Converted panels write NO depth, ever — that is a standing ruling with its own essay at
    /// <c>CanvasConversion.8.Order.cs:20-46</c>, and two deleted attempts behind it. So a window can
    /// never occlude anything by depth; panel-versus-anything composition is decided purely by
    /// <c>sortingOrder</c>, which Unity resolves BEFORE the render queue. A single debris renderer
    /// would therefore be entirely in front of the window or entirely behind it, whatever its
    /// geometry said.</para>
    ///
    /// <para>Worse, the one repo statement about the panels' own <c>ZTest</c> contradicts itself:
    /// <c>OnTopUiGraphics.cs:18</c> says <c>unity_GUIZTestMode</c> resolves to LEqual on a
    /// world-space canvas, <c>ActorBars.cs:172</c> says it effectively resolves to Always. Both
    /// files agree that a per-material override beats the global, which means the global is not
    /// something a decoration may rely on. <b>So this design relies on it for nothing.</b></para>
    ///
    /// <para>Instead each shard is assigned, AT BUILD TIME and from its own launch velocity, to the
    /// FRONT half or the BEHIND half of the cloud, and the two halves are drawn by two renderers
    /// registered on the panel's own distance ladder at offset +1 and -1. The window is drawn
    /// between them. Because the assignment is a per-shard constant computed on the CPU, it is
    /// identical in both eyes and never flips with head motion — a view-dependent partition would
    /// have been both a stereo hazard and the head-binding the user refused. The panel ladder steps
    /// by 16 (<c>PanelOrderStep</c>), so +1/-1 stay inside this window's own slot and cannot
    /// reorder it against a neighbouring panel.</para>
    /// </summary>
    private const int DebrisFrontOrderOffset = 1;

    private const int DebrisBehindOrderOffset = -1;

    /// <summary>Vertices per shard: four triangular faces, each with its own three vertices so the
    /// faces are FLAT-shaded. Sharing the four corners would smooth the normals across the faces and
    /// the shard would read as a blob rather than as a chip of something broken — which at these
    /// angular sizes (a 22 mm shard at 0.9 m is ~1.4 deg, tens of headset pixels) is clearly
    /// visible.</summary>
    internal const int VertsPerShard = 12;

    /// <summary>The canonical unit tetrahedron, corners at unit radius. Winding is verified rather
    /// than trusted — see <see cref="GateShardWinding"/>.</summary>
    private static readonly Vector3[] TetraCorner =
    {
        new Vector3( 1f,  1f,  1f) * 0.5773503f,
        new Vector3( 1f, -1f, -1f) * 0.5773503f,
        new Vector3(-1f,  1f, -1f) * 0.5773503f,
        new Vector3(-1f, -1f,  1f) * 0.5773503f,
    };

    /// <summary>The four faces. With Unity's convention (a triangle a,b,c faces the direction of
    /// <c>cross(b-a, c-a)</c>) every one of these points away from the centroid, which
    /// <see cref="GateShardWinding"/> re-derives at runtime rather than taking on trust.</summary>
    private static readonly int[] TetraFace = { 0, 1, 2,  0, 2, 3,  0, 3, 1,  1, 3, 2 };

    // ---- pools ---------------------------------------------------------------------------------
    // A Mesh is a native object: an unpooled, undestroyed one leaks until the scene changes. The
    // vertex buffers are ordinary managed arrays and are pooled for the GC's sake, not the driver's.

    private static readonly Stack<Mesh> MeshPool = new(8);

    private static readonly List<Vector3> BufPos = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Vector3> BufNrm = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Vector4> BufTan = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Vector4> BufUv0 = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Vector4> BufUv1 = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Vector4> BufUv2 = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<Color> BufCol = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<int> BufIdxFront = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);
    private static readonly List<int> BufIdxBehind = new(VertsPerShard * WindowMaterialiseField.DebrisMaxCount);

    /// <summary>Cumulative emission weight per candidate element, rebuilt per effect.</summary>
    private static readonly List<float> EmitCumulative = new(256);
    private static readonly List<int> EmitIndex = new(256);

    /// <summary>The scale chain the geometry line was last logged for. CHANGE-GATED rather than
    /// once-per-process, and the map-room table is why: it runs at 198 world units per metre against
    /// a scenario's 9.57, so a once-per-process line would report the first window ever animated and
    /// then stay silent through the one case where the scaling could actually be wrong. -1 = never
    /// logged.</summary>
    private static float _loggedMetresPerCanvas = -1f;

    /// <summary>
    /// A deterministic 32-bit xorshift, seeded per effect from the panel's name.
    ///
    /// <para>Deterministic so that a given window breaks the same way every time it closes, which is
    /// what stops the effect reading as noise, and so that
    /// <c>unity/asset-preview/windowmaterialise_field.py</c> — which carries the identical generator
    /// and consumes the draws in the identical order — produces a reproducible cloud rather than a
    /// fresh sample per render. <b>It does NOT make a preview the same cloud as the game's</b>: the
    /// preview's element set is a synthetic stand-in, and the emission table is built from the
    /// elements, so the birth points differ. What the preview is evidence about is the MECHANISM and
    /// the arithmetic, not the particular window.</para>
    /// </summary>
    private static uint _rng = 0x9E3779B9u;

    private static void SeedRng(uint seed) => _rng = seed == 0u ? 0x9E3779B9u : seed;

    private static float Rand01()
    {
        _rng ^= _rng << 13;
        _rng ^= _rng >> 17;
        _rng ^= _rng << 5;
        return (_rng & 0xFFFFFFu) * (1f / 16777216f);
    }

    private static float RandSigned() => Rand01() * 2f - 1f;

    /// <summary>Everything one built debris cloud needs to be driven and torn down.</summary>
    internal sealed class DebrisCloud
    {
        internal GameObject Go = null!;
        internal MeshRenderer Front = null!;
        internal MeshRenderer Behind = null!;
        internal Mesh MeshFront = null!;
        internal Mesh MeshBehind = null!;
        internal int Shards;
        internal float ApparentMetresPerCanvasUnit;
    }

    /// <summary>
    /// <b>Build the shard cloud for <paramref name="panel"/>.</b> Returns false when there is
    /// nothing sane to build from — a degenerate rect, an unresolved shader, an intensity of zero,
    /// or a window with no visible element to tear a shard out of. <b>Returning false is not an
    /// error</b>: the element-by-element dissolve is pure C#, it is the half that actually removes
    /// the window, and it runs perfectly well with no debris at all.
    /// </summary>
    /// <param name="renderers">The already-collected element list — this is the emission source, and
    /// it is the reason the debris comes from where the window broke up rather than from a
    /// rectangle.</param>
    /// <param name="alpha">Each element's ORIGINAL alpha, parallel to <paramref name="renderers"/>.
    /// An element the game had already hidden sheds nothing.</param>
    internal static bool TryBuildDebris(ConvertedPanel panel, IList<CanvasRenderer> renderers,
                                        IList<float> alpha, out DebrisCloud? cloud)
    {
        cloud = null;

        RectTransform host = panel.HostRect;
        if (host == null || panel.HostGo == null)
            return false;

        Rect r = host.rect;
        if (r.width <= 0.01f || r.height <= 0.01f)
        {
            VRLog.Warn(Scope, $"WINDOW MATERIALISE: no debris for '{Name(panel)}' — its host rect is "
                              + $"{r.width:F1}x{r.height:F1}, which has no inside. The window still "
                              + "dissolves element by element.");
            return false;
        }

        float intensity = Intensity;
        if (intensity <= 0.001f)
            return false; // the dial's documented "clean directional wipe, no debris" setting

        Material? mat = SharedMaterial();
        if (mat == null)
            return false;

        // ---- THE SCALE CONVERSION, ONCE, FROM MEASUREMENT --------------------------------------
        // lossyScale is world units per canvas unit. PanelLayout.WorldScale is world units per real
        // metre (9.57 in a scenario, 198 on the map-room table, 1 outside a scenario). Their ratio
        // is the only number this effect needs, and getting it wrong is the 37-of-43 trap.
        Vector3 lossy = host.lossyScale;
        float worldPerCanvas = Mathf.Abs(lossy.x);
        float worldScale = Mathf.Max(PanelLayout.WorldScale, 0.01f);
        float metresPerCanvas = worldPerCanvas / worldScale;
        if (metresPerCanvas <= 1e-7f)
        {
            VRLog.Warn(Scope, $"WINDOW MATERIALISE: no debris for '{Name(panel)}' — its host scale "
                              + $"resolves to {metresPerCanvas:E3} apparent metres per canvas unit, "
                              + "which would make every shard degenerate. The element dissolve still "
                              + "runs.");
            return false;
        }
        float canvasPerMetre = 1f / metresPerCanvas;

        float panelWm = r.width * metresPerCanvas;
        float panelHm = r.height * metresPerCanvas;
        float areaM2 = Mathf.Max(panelWm * panelHm, 1e-5f);

        int shards = Mathf.Clamp(
            Mathf.RoundToInt(areaM2 * WindowMaterialiseField.DebrisPerSquareMetre),
            WindowMaterialiseField.DebrisMinCount, WindowMaterialiseField.DebrisMaxCount);

        // ---- WHERE THE SHARDS COME FROM ---------------------------------------------------------
        // A cumulative-area table over the elements that are actually drawing something. Sampling it
        // means a shard is torn out of a real piece of window in proportion to how much of the window
        // that piece is, so a big background plate sheds a lot and a two-pixel divider sheds almost
        // nothing. This is the difference the brief asked for: "seeded from the elements as they go
        // dark, so the debris comes from where the window actually broke up rather than from a
        // rectangle."
        if (!BuildEmissionTable(host, r, renderers, alpha))
        {
            VRLog.Info(Scope, $"WINDOW MATERIALISE: no debris for '{Name(panel)}' — none of its "
                              + $"{renderers.Count} element(s) is visible and non-degenerate, so "
                              + "there is nothing to tear a shard out of. The element dissolve still "
                              + "runs.");
            return false;
        }

        SeedRng((uint)(Name(panel).GetHashCode() ^ (shards * 2654435761u)));

        float aspect = r.width / r.height;
        Vector2 wind = WindowMaterialiseField.Wind;
        // The wind lives in ISOTROPIC q-space (x scaled by aspect) so a wide panel does not get a
        // wind that leans. Converting back to canvas units is a divide by aspect on x, and then the
        // vector is renormalised in CANVAS units so "drift metres" means the same distance whatever
        // the panel's shape.
        var windCanvas = new Vector2(wind.x / Mathf.Max(aspect, 1e-3f), wind.y);
        windCanvas = windCanvas.sqrMagnitude > 1e-9f ? windCanvas.normalized : Vector2.right;

        BufPos.Clear(); BufNrm.Clear(); BufTan.Clear();
        BufUv0.Clear(); BufUv1.Clear(); BufUv2.Clear(); BufCol.Clear();
        BufIdxFront.Clear(); BufIdxBehind.Clear();

        int frontCount = 0, behindCount = 0;
        float minSizeM = float.MaxValue, maxSizeM = 0f;

        for (int s = 0; s < shards; s++)
        {
            // -- birth point, in an element, in panel UV ------------------------------------------
            PickEmissionPoint(host, r, renderers, out Vector2 uv, out Vector3 birthLocal);
            float threshold = WindowMaterialiseField.Threshold(uv, aspect);

            // -- size, skewed small ---------------------------------------------------------------
            float sizeM = Mathf.Lerp(WindowMaterialiseField.DebrisMinMetres,
                                     WindowMaterialiseField.DebrisMaxMetres,
                                     Mathf.Pow(Rand01(), WindowMaterialiseField.DebrisSizePower));
            if (sizeM < minSizeM) minSizeM = sizeM;
            if (sizeM > maxSizeM) maxSizeM = sizeM;
            float sizeCanvas = sizeM * canvasPerMetre;

            // -- out of the plane. -Z is toward the head (LoadingIndicator.cs:715) -----------------
            bool behind = Rand01() < WindowMaterialiseField.DebrisBehindFraction;
            float liftCanvas = WindowMaterialiseField.DebrisLiftMetres * canvasPerMetre
                               * Mathf.Lerp(0.35f, 1f, Rand01())
                               * (behind ? 1f : -1f);

            // -- tumble ---------------------------------------------------------------------------
            var axis = new Vector3(RandSigned(), RandSigned(), RandSigned());
            axis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.up;
            float spinTurns = WindowMaterialiseField.DebrisSpinTurns * Mathf.Lerp(-1f, 1f, Rand01());

            float driftScale = Mathf.Lerp(0.6f, 1.4f, Rand01());
            float wanderAmp = WindowMaterialiseField.DebrisWanderMetres * canvasPerMetre * Rand01();
            float seedA = Rand01();
            float seedB = Rand01();
            float shade = Mathf.Lerp(0.72f, 1.15f, Rand01());

            // -- a shard is a squashed tetrahedron: a chip, not a die ------------------------------
            float squash = Mathf.Lerp(0.30f, 0.72f, Rand01());
            var lift = new Vector3(1f, 1f, squash);

            int baseVert = BufPos.Count;
            List<int> idx = behind ? BufIdxBehind : BufIdxFront;
            if (behind) behindCount++; else frontCount++;

            for (int f = 0; f < 4; f++)
            {
                int i0 = TetraFace[f * 3], i1 = TetraFace[f * 3 + 1], i2 = TetraFace[f * 3 + 2];
                Vector3 a = Vector3.Scale(TetraCorner[i0], lift);
                Vector3 b = Vector3.Scale(TetraCorner[i1], lift);
                Vector3 c = Vector3.Scale(TetraCorner[i2], lift);
                Vector3 n = Vector3.Cross(b - a, c - a);
                n = n.sqrMagnitude > 1e-9f ? n.normalized : Vector3.forward;

                AddShardVertex(birthLocal, n, a, sizeCanvas, uv, threshold, seedA,
                               axis, spinTurns, liftCanvas, driftScale, wanderAmp, seedB, shade);
                AddShardVertex(birthLocal, n, b, sizeCanvas, uv, threshold, seedA,
                               axis, spinTurns, liftCanvas, driftScale, wanderAmp, seedB, shade);
                AddShardVertex(birthLocal, n, c, sizeCanvas, uv, threshold, seedA,
                               axis, spinTurns, liftCanvas, driftScale, wanderAmp, seedB, shade);

                idx.Add(baseVert + f * 3);
                idx.Add(baseVert + f * 3 + 1);
                idx.Add(baseVert + f * 3 + 2);
            }
        }

        // ---- the carrier -------------------------------------------------------------------------
        var go = new GameObject(DebrisName) { layer = panel.HostGo.layer };
        Transform t = go.transform;
        t.SetParent(host, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        // THE LAYER, AND IT IS A CORRECTION TO THE OLD QUAD. That quad took panel.HostGo.layer, and
        // for a SUPERSAMPLED panel that is the panel's PRIVATE CAPTURE LAYER — its capture camera's
        // culling mask is exactly `1 << thatLayer` (PanelSupersample.2.Capture.cs:849), so the quad
        // was rendered INTO the panel's own render texture and composited flat onto the window. A
        // decal, drawn on the pane, in the most literal way available. VRLayers.Apply moves the
        // carrier to the mod layer, which every capture camera excludes and the head camera always
        // includes (MaskNarrowingFloor). It is a no-op outside a running VR session, so the
        // HostGo.layer seed above stays correct for the desktop dev path.
        VRLayers.Apply(go);

        var cl = new DebrisCloud
        {
            Go = go,
            Shards = shards,
            ApparentMetresPerCanvasUnit = metresPerCanvas,
        };
        // THE FURTHEST ANY SHARD CAN GET FROM ITS BIRTH POINT, in host-local units, from the same
        // constants the vertex stage integrates. Every term is at its own maximum, so this is an
        // upper bound and not an estimate — which is what a culling volume has to be.
        float reachCanvas = canvasPerMetre
                            * (WindowMaterialiseField.DebrisDriftMetres * 1.4f
                               + WindowMaterialiseField.DebrisLiftMetres
                               + WindowMaterialiseField.DebrisFallMetres
                               + WindowMaterialiseField.DebrisWanderMetres * 1.733f
                               + WindowMaterialiseField.DebrisMaxMetres);

        cl.MeshBehind = BuildHalf(go, mat, BufIdxBehind, "Behind", reachCanvas, out cl.Behind);
        cl.MeshFront = BuildHalf(go, mat, BufIdxFront, "Front", reachCanvas, out cl.Front);

        // The two halves bracket the window in the panel's own distance ladder, which is re-derived
        // every LateUpdate from measured eye distance. Registering rather than writing a sortingOrder
        // means the debris keeps its place when the ladder reshuffles, and the registration self-
        // prunes when these renderers are destroyed (CanvasConversion.8.Order.cs:630).
        CanvasConversion.RegisterOrderFollower(panel, cl.Behind, DebrisBehindOrderOffset);
        CanvasConversion.RegisterOrderFollower(panel, cl.Front, DebrisFrontOrderOffset);

        LogGeometryOnce(panel, r, lossy, worldScale, metresPerCanvas, panelWm, panelHm,
                        shards, frontCount, behindCount, minSizeM, maxSizeM);

        cloud = cl;
        return true;
    }

    /// <summary>One half of the cloud on its own renderer, because a panel writes no depth and
    /// <c>sortingOrder</c> is the only thing that can put geometry behind one.</summary>
    private static Mesh BuildHalf(GameObject parent, Material mat, List<int> indices, string half,
                                  float reachCanvas, out MeshRenderer mr)
    {
        var go = new GameObject($"{DebrisName}.{half}") { layer = parent.layer };
        Transform t = go.transform;
        t.SetParent(parent.transform, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        Mesh mesh = MeshPool.Count > 0 ? MeshPool.Pop() : null!;
        if (mesh == null)
        {
            mesh = new Mesh { name = DebrisName };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        // Both halves carry the WHOLE vertex buffer and differ only in their index list. Uploading
        // the shared buffer twice costs a little memory for the duration of one animation and buys a
        // single build pass and one shared trajectory evaluation; splitting the vertex arrays as
        // well would mean two of everything on the CPU side for no visual difference.
        mesh.SetVertices(BufPos);
        mesh.SetNormals(BufNrm);
        mesh.SetTangents(BufTan);
        mesh.SetUVs(0, BufUv0);
        mesh.SetUVs(1, BufUv1);
        mesh.SetUVs(2, BufUv2);
        mesh.SetColors(BufCol);
        mesh.SetTriangles(indices, 0, calculateBounds: false);

        // BOUNDS ARE AUTHORED, NOT CALCULATED, AND THAT IS THE ONE UNITY TRAP IN THIS FILE.
        // Culling cannot see vertex shaders: every shard's real position is computed in the vertex
        // stage, so RecalculateBounds would measure the UNDISPLACED birth positions — a box exactly
        // the size of the window — and the whole cloud would be culled away the moment the window
        // itself left the frustum edge, or worse, popped as it travelled. The box below is the birth
        // cloud grown by the furthest any shard can possibly reach, which the caller computes from
        // the same constants the vertex stage uses. Same failure class as the displaced-geometry arc
        // sweep in this project's memory.
        Bounds b = default;
        if (BufPos.Count > 0)
        {
            b = new Bounds(BufPos[0], Vector3.zero);
            for (int i = 1; i < BufPos.Count; i++)
                b.Encapsulate(BufPos[i]);
        }
        // Expand() grows the TOTAL size, i.e. half of it on each side, so the argument is twice the
        // reach. The reach itself already carries a shard's own half-extent.
        b.Expand(2f * reachCanvas);
        mesh.bounds = b;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;
        return mesh;
    }

    /// <summary>
    /// Pack one vertex. The stream layout is the whole per-shard state, and it is written from the
    /// CPU rather than derived in the shader from a seed on purpose: it is what lets the numpy mirror
    /// reproduce the identical cloud, so a preview render is evidence about the shipped effect and
    /// not merely a picture of the same idea.
    ///
    /// <para><c>POSITION</c> birth point (host-local) · <c>NORMAL</c> face normal (shard-local) ·
    /// <c>TANGENT</c> corner offset xyz (shard-local, unit) + size w (host-local) ·
    /// <c>TEXCOORD0</c> birth uv xy, threshold z, seedA w · <c>TEXCOORD1</c> tumble axis xyz, turns w
    /// · <c>TEXCOORD2</c> out-of-plane velocity x, drift scale y, seedB z, wander amplitude w ·
    /// <c>COLOR</c> shade jitter.</para>
    /// </summary>
    private static void AddShardVertex(Vector3 birth, Vector3 normal, Vector3 corner, float size,
                                       Vector2 uv, float threshold, float seedA,
                                       Vector3 axis, float spinTurns, float liftCanvas,
                                       float driftScale, float wanderAmp, float seedB, float shade)
    {
        BufPos.Add(birth);
        BufNrm.Add(normal);
        BufTan.Add(new Vector4(corner.x, corner.y, corner.z, size));
        BufUv0.Add(new Vector4(uv.x, uv.y, threshold, seedA));
        BufUv1.Add(new Vector4(axis.x, axis.y, axis.z, spinTurns));
        BufUv2.Add(new Vector4(liftCanvas, driftScale, seedB, wanderAmp));
        BufCol.Add(new Color(shade, shade, shade, 1f));
    }

    /// <summary>
    /// Cumulative-area table over the elements that are actually drawing. Returns false when none
    /// is, which is a real case (a window whose content has not laid out yet) and not an error.
    /// </summary>
    private static bool BuildEmissionTable(RectTransform host, Rect hostRect,
                                           IList<CanvasRenderer> renderers, IList<float> alpha)
    {
        EmitCumulative.Clear();
        EmitIndex.Clear();
        float total = 0f;
        int n = Mathf.Min(renderers.Count, alpha.Count);
        for (int i = 0; i < n; i++)
        {
            CanvasRenderer cr = renderers[i];
            if (cr == null || alpha[i] <= 0.02f)
                continue;
            if (cr.transform is not RectTransform rt)
                continue;
            if (!cr.gameObject.activeInHierarchy)
                continue;
            Rect er = rt.rect;
            if (er.width <= 0.5f || er.height <= 0.5f)
                continue;
            // Weighted by the element's area IN PANEL UV, clamped: a full-screen plate under a fitted
            // host really can measure several times the host's own area, and letting it do so would
            // make it shed almost every shard and leave the window's actual content emitting nothing.
            Vector2 c0 = UvOfPoint(host, hostRect, rt, 0f, 0f);
            Vector2 c1 = UvOfPoint(host, hostRect, rt, 1f, 1f);
            float w = Mathf.Abs(c1.x - c0.x), h = Mathf.Abs(c1.y - c0.y);
            float weight = Mathf.Clamp(w * h, 0.0004f, 1f);
            total += weight;
            EmitCumulative.Add(total);
            EmitIndex.Add(i);
        }
        return EmitIndex.Count > 0 && total > 0f;
    }

    /// <summary>Draw one birth point: an element chosen in proportion to its visible area, then a
    /// uniform point inside that element's own rect.</summary>
    private static void PickEmissionPoint(RectTransform host, Rect hostRect,
                                          IList<CanvasRenderer> renderers,
                                          out Vector2 uv, out Vector3 local)
    {
        float total = EmitCumulative[EmitCumulative.Count - 1];
        float pick = Rand01() * total;
        int lo = 0, hi = EmitCumulative.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (EmitCumulative[mid] < pick) lo = mid + 1; else hi = mid;
        }
        CanvasRenderer cr = renderers[EmitIndex[lo]];
        var rt = (RectTransform)cr.transform;
        uv = UvOfPoint(host, hostRect, rt, Rand01(), Rand01());
        local = new Vector3(hostRect.xMin + uv.x * hostRect.width,
                            hostRect.yMin + uv.y * hostRect.height,
                            0f);
    }

    /// <summary>One point of an element, in PANEL UV. Clamped, and that is load-bearing rather than
    /// defensive: elements really do sit outside the host rect (PanelInkBounds found a quest window
    /// drawing its reward row 373 px BELOW its own rect's bottom edge), and an unclamped UV would put
    /// a shard's threshold outside 0..1, where the field is no longer exact at its endpoints.
    /// </summary>
    private static Vector2 UvOfPoint(RectTransform host, Rect hostRect, RectTransform rt,
                                     float u, float v)
    {
        Rect er = rt.rect;
        Vector3 world = rt.TransformPoint(new Vector3(er.xMin + u * er.width,
                                                      er.yMin + v * er.height, 0f));
        Vector3 local = host.InverseTransformPoint(world);
        return new Vector2(
            Mathf.Clamp01((local.x - hostRect.xMin) / hostRect.width),
            Mathf.Clamp01((local.y - hostRect.yMin) / hostRect.height));
    }

    /// <summary>Give a mesh back to the pool, or destroy it. A Mesh is a native object.</summary>
    internal static void ReleaseMesh(Mesh? mesh)
    {
        if (mesh == null)
            return;
        if (MeshPool.Count < 8)
        {
            mesh.Clear();
            MeshPool.Push(mesh);
            return;
        }
        Object.Destroy(mesh);
    }

    /// <summary>
    /// <b>THE MESH GATE, RUN ONCE PER PROCESS ON A REAL BUILT SHARD.</b> Nine meshes have shipped in
    /// this project wound against the side they are seen from, so the shard's winding is DERIVED and
    /// checked rather than trusted to the literal table above.
    ///
    /// <para>Two properties, both computed from the canonical solid: the signed volume must be
    /// positive (the shard is a closed solid, so this is meaningful and is the strong form of the
    /// gate), and every face normal, taken as <c>cross(b-a, c-a)</c>, must point away from the
    /// shard's own centroid. The second is the property "wound against the side it is seen from"
    /// actually names.</para>
    ///
    /// <para>The UV half of the usual gate is <b>not applicable here and is not faked</b>: a shard
    /// carries no texture coordinates and the shader samples no texture, so a UV signed area would
    /// be a number with no referent. Reporting one would be worse than reporting its absence.</para>
    /// </summary>
    private static void GateShardWinding(out float signedVolume, out float worstOutward)
    {
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < 4; i++)
            centroid += TetraCorner[i];
        centroid *= 0.25f;

        signedVolume = 0f;
        worstOutward = float.MaxValue;
        for (int f = 0; f < 4; f++)
        {
            Vector3 a = TetraCorner[TetraFace[f * 3]];
            Vector3 b = TetraCorner[TetraFace[f * 3 + 1]];
            Vector3 c = TetraCorner[TetraFace[f * 3 + 2]];
            signedVolume += Vector3.Dot(a, Vector3.Cross(b, c));
            Vector3 n = Vector3.Cross(b - a, c - a).normalized;
            float outward = Vector3.Dot(n, (a + b + c) / 3f - centroid);
            if (outward < worstOutward)
                worstOutward = outward;
        }
        signedVolume /= 6f;
    }

    /// <summary>
    /// <b>THE WORLD SIZE, MEASURED AND PRINTED, NOT ASSUMED.</b> One line per process, and it is the
    /// line a hardware log is read against. It states the whole scale chain — canvas units, world
    /// units, apparent metres — because the 37-of-43 <c>ParticleSystemScalingMode</c> trap is
    /// precisely a chain whose middle link is invisible, and because the OLD quad's log line got this
    /// wrong: it multiplied the rect by <c>lossyScale</c> and called the product METRES, which is
    /// world units, i.e. 9.57x too large in a scenario and 198x on the map-room table.
    /// </summary>
    private static void LogGeometryOnce(ConvertedPanel panel, Rect r, Vector3 lossy, float worldScale,
                                        float metresPerCanvas, float panelWm, float panelHm,
                                        int shards, int front, int behind,
                                        float minSizeM, float maxSizeM)
    {
        // Within 1 % of the last logged chain is the same chain. Without the tolerance a grabbed
        // window's own _extraScale would re-log on every open.
        if (_loggedMetresPerCanvas > 0f
            && Mathf.Abs(metresPerCanvas - _loggedMetresPerCanvas) <= 0.01f * _loggedMetresPerCanvas)
            return;
        _loggedMetresPerCanvas = metresPerCanvas;

        GateShardWinding(out float vol, out float outward);

        // Angular size at a typical 0.9 m reading distance, which is what decides whether the debris
        // is in the spatial-aliasing regime this project has read as stereo rivalry before.
        float minDeg = 2f * Mathf.Atan2(minSizeM * 0.5f, 0.9f) * Mathf.Rad2Deg;
        float maxDeg = 2f * Mathf.Atan2(maxSizeM * 0.5f, 0.9f) * Mathf.Rad2Deg;

        VRLog.Info(Scope,
            $"WINDOW MATERIALISE debris geometry, MEASURED on '{Name(panel)}': host rect "
            + $"{r.width:F0}x{r.height:F0} canvas units, lossyScale ({lossy.x:F5}, {lossy.y:F5}, "
            + $"{lossy.z:F5}) world units per canvas unit, rig world scale {worldScale:F2} world "
            + $"units per real metre => {metresPerCanvas:E4} APPARENT METRES per canvas unit. The "
            + $"window is {panelWm:F3} x {panelHm:F3} m to the eye. {shards} shard(s) built "
            + $"({front} in front of the plane, {behind} behind it), {shards * VertsPerShard} verts "
            + $"and {shards * 4} tris total, sized {minSizeM * 1000f:F1}-{maxSizeM * 1000f:F1} mm "
            + $"(~{minDeg:F2}-{maxDeg:F2} deg at 0.9 m, i.e. well above the sub-pixel regime that "
            + "aliases per eye). MESH GATE: signed volume of the canonical shard "
            + $"{vol:F4} (must be > 0; a shard is a CLOSED solid so this is the strong form), worst "
            + $"face outwardness {outward:F4} (must be > 0). UV signed area is NOT APPLICABLE and is "
            + "not reported: a shard carries no texture coordinates and the shader samples no "
            + "texture. NO ParticleSystem is involved, so ParticleSystemScalingMode is not a "
            + "parameter of this effect; sizes are authored in apparent metres and converted by the "
            + "measured chain above.");

        if (vol <= 0f || outward <= 0f)
            VRLog.Error(Scope, "WINDOW MATERIALISE shard mesh FAILS its winding gate — signed volume "
                               + $"{vol:F4}, worst outwardness {outward:F4}. Every shard will be "
                               + "inside-out and will vanish under Cull Back. This is a code fault in "
                               + "the TetraFace table, not a config or asset problem.");
    }
}
