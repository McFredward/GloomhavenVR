using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The ONE quad the flakes are painted on, and the pool that keeps building it from allocating.
/// Everything about its geometry is derived from the panel's own <c>HostRect</c>, which is the
/// point: there is no authored size anywhere in this effect, so there is no size to get wrong at
/// 9.57x rig scale.
/// </summary>
internal static partial class WindowMaterialise
{
    /// <summary>
    /// The quad's name, and it MATTERS that it starts with <c>GloomhavenVR.</c>. The liveness rule
    /// that tears down an empty float (<c>ModalFallback.9.Spawn.cs</c>, <c>MeasureDrawsSomething</c>
    /// / <c>DrawsAnythingLoose</c>) treats any enabled <c>Renderer</c> as proof the window still
    /// draws something — EXCEPT ones whose GameObject name starts with this prefix. Without the
    /// prefix, a mod-owned decoration quad would keep a genuinely dead window alive forever, which
    /// is the exact defect the user's ruling <i>"Es darf niemals leere Fenster geben"</i> was
    /// written against.
    /// </summary>
    internal const string QuadName = "GloomhavenVR.WindowMaterialise";

    /// <summary>How far in front of the panel plane the quad sits, in the host canvas's own local
    /// units. -Z is toward the head (LoadingIndicator.cs:715). One unit is sub-millimetre at the
    /// shipped <c>CanvasScaleMm</c>; the draw ORDER is settled by the render queue
    /// (<c>Transparent+550</c>), not by this, so it only has to be non-negative.</summary>
    private const float ZNudgeLocal = 1f;

    /// <summary>UV margin around the panel rect, on every side, on top of the plume's own reach.
    /// The flakes at the very edge are half a cell wide and would be clipped by a tight quad.</summary>
    private const float EdgeMarginUv = 0.04f;

    /// <summary>Reused meshes. Bounded by the number of windows that can animate at once; a mesh is
    /// a native object, so this is about not churning the graphics driver, not about the GC.</summary>
    private static readonly Stack<Mesh> MeshPool = new(8);

    private static readonly List<Vector3> QuadVerts = new(4);
    private static readonly List<Vector2> QuadUvs = new(4);
    private static readonly List<int> QuadTris = new(6);

    private static bool _quadGeometryLogged;

    /// <summary>
    /// Build the flake quad for <paramref name="panel"/>, or return false when there is nothing
    /// sane to build it from (a degenerate rect, a missing shader). Returning false is not an
    /// error: the element-by-element dissolve is pure C# and runs perfectly well with no flakes.
    /// </summary>
    internal static bool TryBuildQuad(ConvertedPanel panel, out GameObject? go, out MeshRenderer? mr,
                                      out Mesh? mesh, out float aspect)
    {
        go = null;
        mr = null;
        mesh = null;
        aspect = 1f;

        RectTransform host = panel.HostRect;
        if (host == null)
            return false;
        Rect r = host.rect;
        if (r.width <= 0.01f || r.height <= 0.01f)
        {
            VRLog.Warn(Scope, $"WINDOW MATERIALISE: no flake quad for '{Name(panel)}' — its host rect "
                              + $"is {r.width:F1}x{r.height:F1}, which has no inside. The window "
                              + "still dissolves element by element.");
            return false;
        }
        Material? mat = SharedMaterial();
        if (mat == null)
            return false;

        aspect = r.width / r.height;

        // THE QUAD IS BIGGER THAN THE WINDOW, by exactly as far as the plume can reach plus a
        // margin. The plume travel is _Drift q-space units (i.e. multiples of the panel HEIGHT), so
        // in UV that is (wind.x / aspect, wind.y) * Drift — the division is what stops a wide panel
        // from padding three times too far on the x axis.
        Vector2 wind = WindowMaterialiseField.Wind;
        float duvx = wind.x / aspect * WindowMaterialiseField.Drift;
        float duvy = wind.y * WindowMaterialiseField.Drift;
        float padR = Mathf.Max(0f, duvx) + EdgeMarginUv;
        float padL = Mathf.Max(0f, -duvx) + EdgeMarginUv;
        float padU = Mathf.Max(0f, duvy) + EdgeMarginUv;
        float padD = Mathf.Max(0f, -duvy) + EdgeMarginUv;

        float u0 = -padL, u1 = 1f + padR, v0 = -padD, v1 = 1f + padU;

        // Positions are taken from rect.xMin/yMin rather than from a centred half-extent, so a host
        // whose pivot is not (0.5, 0.5) still gets a quad exactly over its rect.
        QuadVerts.Clear();
        QuadUvs.Clear();
        QuadTris.Clear();
        AddVert(r, u0, v0);
        AddVert(r, u1, v0);
        AddVert(r, u0, v1);
        AddVert(r, u1, v1);
        QuadTris.Add(0); QuadTris.Add(2); QuadTris.Add(1);
        QuadTris.Add(2); QuadTris.Add(3); QuadTris.Add(1);

        mesh = MeshPool.Count > 0 ? MeshPool.Pop() : null;
        if (mesh == null)
        {
            mesh = new Mesh { name = QuadName };
            mesh.MarkDynamic();
        }
        mesh.Clear();
        mesh.SetVertices(QuadVerts);
        mesh.SetUVs(0, QuadUvs);
        mesh.SetTriangles(QuadTris, 0);
        mesh.RecalculateBounds();

        go = new GameObject(QuadName);
        go.layer = panel.HostGo.layer;
        Transform t = go.transform;
        t.SetParent(host, worldPositionStays: false);
        t.localPosition = new Vector3(0f, 0f, -ZNudgeLocal);
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        MeshFilter mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;
        mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;

        LogGeometryOnce(panel, go, r, u1 - u0, v1 - v0);
        return true;
    }

    private static void AddVert(Rect r, float u, float v)
    {
        QuadVerts.Add(new Vector3(r.xMin + u * r.width, r.yMin + v * r.height, 0f));
        QuadUvs.Add(new Vector2(u, v));
    }

    /// <summary>Give the mesh back. Meshes are native objects: an unpooled, undestroyed one leaks
    /// until the scene changes.</summary>
    internal static void ReleaseMesh(Mesh? mesh)
    {
        if (mesh == null)
            return;
        if (MeshPool.Count < 8)
        {
            MeshPool.Push(mesh);
            return;
        }
        Object.Destroy(mesh);
    }

    /// <summary>
    /// <b>THE WORLD SIZE, MEASURED AND PRINTED, NOT ASSUMED.</b> This project has already measured
    /// 37 of 43 of the game's particle systems ignoring hierarchy scale, and a burst authored in
    /// local units at 9.57x rig scale is either invisible or absurd. This effect has no particle
    /// system precisely so that cannot happen — the quad is derived from the panel's own rect — but
    /// "derived, therefore correct" is an argument, and a number is evidence. One line per process.
    ///
    /// <para>The UV-space signed area is printed with it. A quad is a flat sheet, so the signed
    /// VOLUME every mesh in this project is gated on is identically zero and says nothing; the UV
    /// area is the half of that gate which still carries information (a negative or zero one would
    /// mean the UVs are mirrored or collapsed, and the flakes would run backwards or not at all).
    /// The winding half of the gate is answered by <c>Cull Off</c> in the shader.</para>
    /// </summary>
    private static void LogGeometryOnce(ConvertedPanel panel, GameObject go, Rect r,
                                        float uSpan, float vSpan)
    {
        if (_quadGeometryLogged)
            return;
        _quadGeometryLogged = true;

        // Shoelace over the two triangles, in UV space.
        float uvArea = 0f;
        for (int i = 0; i < QuadTris.Count; i += 3)
        {
            Vector2 a = QuadUvs[QuadTris[i]], b = QuadUvs[QuadTris[i + 1]], c = QuadUvs[QuadTris[i + 2]];
            uvArea += 0.5f * ((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
        }

        Vector3 s = go.transform.lossyScale;
        float wWorld = r.width * uSpan * Mathf.Abs(s.x);
        float hWorld = r.height * vSpan * Mathf.Abs(s.y);

        VRLog.Info(Scope, $"WINDOW MATERIALISE quad geometry, measured on '{Name(panel)}': host rect "
                          + $"{r.width:F0}x{r.height:F0} canvas units, quad {uSpan:F2}x{vSpan:F2} of "
                          + $"that (the extra is where the plume goes), lossyScale "
                          + $"({s.x:F5}, {s.y:F5}, {s.z:F5}) => {wWorld:F3} x {hWorld:F3} METRES in "
                          + $"the room. UV signed area {uvArea:F4} (must be > 0; a quad's signed "
                          + "VOLUME is 0 and carries no information, and the winding question is "
                          + "answered by Cull Off in the shader). No ParticleSystem is involved, so "
                          + "ParticleSystemScalingMode is not a parameter of this effect.");

        if (uvArea <= 0f)
            VRLog.Error(Scope, "WINDOW MATERIALISE quad has a NON-POSITIVE UV area — its UVs are "
                               + "mirrored or collapsed and the flake field will be wrong. This is a "
                               + "code fault in TryBuildQuad, not a config or asset problem.");
    }
}
