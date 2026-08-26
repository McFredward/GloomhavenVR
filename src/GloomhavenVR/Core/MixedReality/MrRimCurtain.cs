using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// The MR RIM CURTAIN (round 15): a mod-BUILT, opaque dark prism seated just inside each unseen
/// fog-of-war piece's VERTICAL side faces, so a ray through a translucent outer side face
/// terminates on dark instead of on the chroma key.
///
/// WHY A MOD-BUILT MESH AND NOT ANOTHER SAME-MESH COPY
/// ---------------------------------------------------
/// Round 13 closed the TOPS (user: "Die Lücken oben sind geschlossen und sieht gut aus top!") and
/// its own instrument reported the goal state — "UNBACKED PREVIEW RENDERERS — none", i.e. every
/// rim piece already carries a coplanar same-mesh dark underlay — yet the outer rim STILL reads
/// translucent-over-key ("die Ränder … also die Höhe … sind immer noch transparent",
/// mixed_reality_tiles5.png). So the existing backing is present on those faces and produces no
/// dark pixels there. The standing suspect (round 14) is the authored VERTEX-COLOR ALPHA on the
/// side/rim vertices: both candidate backing shaders multiply vertex color into their output
/// (Sprites/Default by design; the bundled Overlay's frag is <c>_MainTex × _Color × i.color</c>),
/// so a copy of the AUTHORED mesh inherits whatever alpha the artist put on those vertices and
/// can render nothing exactly where the piece is see-through. Round 14 tried to neutralize that
/// by stripping the color channel from a mesh copy and the hardware log answered definitively
/// (read from the log, ModBuild 72): <c>"MR: unseen backing mesh 'EN_CR_FloorTiles_Damaged_03' is
/// not CPU-readable — its vertex-color channel cannot be stripped"</c>. Mesh.colors / .vertices /
/// .triangles are therefore permanently unavailable for this geometry — no same-mesh copy can
/// ever be made vertex-color-independent.
///
/// A mesh the MOD builds has no color channel at all, so the attribute defaults to white and the
/// dark material renders its color everywhere, under any shader, with no assumption about the
/// game's authoring. That is the entire reason this file exists.
///
/// WHY A PRISM AND NOT A BOX (improvement on the brief)
/// ----------------------------------------------------
/// The obvious construction is an axis-aligned box inset inside the renderer's bounds. For a HEX
/// piece that is wrong at exactly the place that matters: a regular hexagon fills only ~75 % of
/// its own AABB, and each AABB corner sits √3·R/4 ≈ 0.43·R OUTSIDE the hex silhouette — at the
/// region boundary that is a dark spike poking past the authored edge, the round-5/6 failure the
/// user rejected on sight ("the rectangular base plate is VISIBLE at the region rim as an alien
/// dark slab"). So the cross-section is a HEXAGON inscribed in the mesh-local XZ bounds, with the
/// box only as the fallback for footprints that are not hex-like. The hexagon's orientation is
/// read from the bounds itself (a regular hexagon's AABB has aspect 2/√3 ≈ 1.1547, long axis
/// through the vertex pair), so nothing is assumed about the game's hex grid.
///
/// WHY BOUNDS AND NOT VERTICES: <see cref="Mesh.bounds"/> is served from the mesh's metadata and
/// works on a NON-readable mesh — it is the one geometric fact still available (round 13 already
/// reads it for the wafer). Working in the source's LOCAL space (mesh bounds are mesh-local, the
/// curtain is a child with an identity local transform) also makes the construction immune to the
/// board's world rotation/scale: a world-space AABB of a rotated hex would be a different, larger
/// rectangle every time the player turns the tray.
///
/// GEOMETRY CONTRACT (why this can never touch a top face)
/// -------------------------------------------------------
/// - TOP: the curtain's cap sits at mesh-top − <c>topClearanceWorld</c>, and the caller keeps that
///   clearance STRICTLY GREATER than the wafer drop. The round-13 wafer is a flat slab at
///   mesh-top − UnseenWaferDrop widened XZ ×UnseenSkirtScale; the curtain is BELOW it and NARROWER
///   than it, so from above the curtain is completely hidden behind a surface the user has already
///   approved. Nothing the curtain draws can appear on, or in front of, an authored top face.
/// - XZ: inset by <c>insetWorld</c> INSIDE the authored side faces, so the curtain can never
///   protrude past the outer silhouette (the standing "no alien dark ledge" ruling).
/// - BOTTOM: dropped a hair below the mesh bottom, and the prism is CLOSED (side walls + both
///   caps) so it is watertight from any direction.
///
/// Lifecycle: the curtain object is a CHILD of its source renderer (structural teardown against
/// Apparance's constant tile regeneration, exactly like the plate and the wafer). The generated
/// meshes are mod-owned assets that Unity does NOT collect with the GameObject, so they are shared
/// through <see cref="MeshCache"/> (keyed on the quantized shape) and destroyed together in
/// <see cref="ReleaseMeshes"/> when MR turns off.
/// </summary>
internal static class MrRimCurtain
{
    /// <summary>Quantization of the cache key, in local units — two pieces whose derived prism
    /// agrees to a tenth of a millimetre share one mesh. The board carries hundreds of identical
    /// hexes; without sharing every Apparance regen would strand a fresh Mesh asset per piece.</summary>
    private const float KeyQuantum = 0.0001f;

    /// <summary>Aspect window (long/short XZ half-extent) inside which a footprint is treated as a
    /// regular HEXAGON. A regular hexagon's AABB aspect is exactly 2/√3 ≈ 1.1547; the window is
    /// wide enough for the damaged/beveled variants ('..._Edge_Damage_03_PR') and narrow enough to
    /// send genuinely rectangular or elongated props to the box fallback.</summary>
    private const float HexAspectMin = 1.06f;
    private const float HexAspectMax = 1.30f;

    /// <summary>The shape the last <see cref="Build"/> produced — folded into the census line so
    /// the next hardware log states how the silhouette was derived, not just that it was.</summary>
    internal enum Shape
    {
        None,
        Hex,
        Box,
    }

    private readonly struct Key : System.IEquatable<Key>
    {
        private readonly int _shape;
        private readonly int _cx, _cz, _ex, _ez, _yTop, _yBot;

        internal Key(Shape shape, float cx, float cz, float ex, float ez, float yTop, float yBot)
        {
            _shape = (int)shape;
            _cx = Q(cx);
            _cz = Q(cz);
            _ex = Q(ex);
            _ez = Q(ez);
            _yTop = Q(yTop);
            _yBot = Q(yBot);
        }

        private static int Q(float v) => Mathf.RoundToInt(v / KeyQuantum);

        public bool Equals(Key o) =>
            _shape == o._shape && _cx == o._cx && _cz == o._cz && _ex == o._ex
            && _ez == o._ez && _yTop == o._yTop && _yBot == o._yBot;

        public override bool Equals(object? o) => o is Key k && Equals(k);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _shape;
                h = h * 31 + _cx;
                h = h * 31 + _cz;
                h = h * 31 + _ex;
                h = h * 31 + _ez;
                h = h * 31 + _yTop;
                h = h * 31 + _yBot;
                return h;
            }
        }
    }

    /// <summary>Shape key → the shared mod-built prism mesh. Mod-owned; destroyed in
    /// <see cref="ReleaseMeshes"/> (a Mesh is an asset, not a component — it outlives the
    /// GameObject that referenced it).</summary>
    private static readonly Dictionary<Key, Mesh> MeshCache = new(8);

    /// <summary>Scratch buffers reused by <see cref="BuildMesh"/> — this runs against Apparance
    /// regen churn (226 pieces re-backed per session in the ModBuild-57 run).</summary>
    private static readonly List<Vector3> VertScratch = new(16);
    private static readonly List<int> TriScratch = new(60);
    private static readonly List<Vector2> RingScratch = new(8);

    // Census of the last completed sweep-worth of builds (the caller resets and logs it).
    internal static int BuiltHex;
    internal static int BuiltBox;
    internal static int Skipped;
    internal static Shape LastShape;
    internal static Vector3 LastMeshSize;

    /// <summary>Clear the per-session census counters (called when the underlays are torn down).</summary>
    internal static void ResetCensus()
    {
        BuiltHex = 0;
        BuiltBox = 0;
        Skipped = 0;
        LastShape = Shape.None;
        LastMeshSize = Vector3.zero;
    }

    /// <summary>
    /// Build one piece's rim curtain as a CHILD of <paramref name="source"/> and return its
    /// renderer, or null when the piece is too small/thin to carry one (counted in
    /// <see cref="Skipped"/>).
    ///
    /// All three margins are WORLD units (the same currency as UnseenWaferDrop) and are converted
    /// into the source's local space through its lossy scale, so a scaled board (the VR tray
    /// scales the whole diorama) gets the same physical clearance it would at scale 1.
    /// </summary>
    internal static Renderer? Build(
        MeshRenderer source,
        Mesh sourceMesh,
        Material dark,
        float insetWorld,
        float topClearanceWorld,
        float bottomDropWorld)
    {
        if (source == null || sourceMesh == null || dark == null)
            return null;

        Bounds mb = sourceMesh.bounds; // mesh-local; served from metadata, no CPU read needed
        Vector3 ls = source.transform.lossyScale;
        float sx = Mathf.Abs(ls.x) > 1e-5f ? Mathf.Abs(ls.x) : 1f;
        float sy = Mathf.Abs(ls.y) > 1e-5f ? Mathf.Abs(ls.y) : 1f;
        float sz = Mathf.Abs(ls.z) > 1e-5f ? Mathf.Abs(ls.z) : 1f;

        float ex = mb.extents.x - insetWorld / sx;
        float ez = mb.extents.z - insetWorld / sz;
        float yTop = mb.max.y - topClearanceWorld / sy;
        float yBot = mb.min.y - bottomDropWorld / sy;
        if (ex <= 0f || ez <= 0f || yTop <= yBot)
        {
            Skipped++;
            return null; // a piece thinner than the clearances has no rim to curtain
        }

        float aspect = ex >= ez ? ex / Mathf.Max(ez, 1e-5f) : ez / Mathf.Max(ex, 1e-5f);
        Shape shape = aspect >= HexAspectMin && aspect <= HexAspectMax ? Shape.Hex : Shape.Box;

        var key = new Key(shape, mb.center.x, mb.center.z, ex, ez, yTop, yBot);
        if (!MeshCache.TryGetValue(key, out Mesh mesh) || mesh == null)
        {
            mesh = BuildMesh(shape, mb.center.x, mb.center.z, ex, ez, yTop, yBot);
            MeshCache[key] = mesh;
        }

        var go = new GameObject("GloomhavenVR.MrUnseenRim");
        go.transform.SetParent(source.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = source.gameObject.layer; // same convention as the plate/wafer children
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = dark;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        if (shape == Shape.Hex)
            BuiltHex++;
        else
            BuiltBox++;
        LastShape = shape;
        LastMeshSize = mb.size;
        return mr;
    }

    /// <summary>The closed prism: <paramref name="shape"/>'s cross-section extruded from
    /// <paramref name="yBot"/> to <paramref name="yTop"/>, side walls plus both caps. Unlit dark —
    /// no normals/UVs are needed, and the shared dark material is Cull Off (ModBuild-63 log:
    /// 'Sprites/Default'), so the windings below are belt-and-braces rather than load-bearing.</summary>
    private static Mesh BuildMesh(Shape shape, float cx, float cz, float ex, float ez,
                                  float yTop, float yBot)
    {
        RingScratch.Clear();
        if (shape == Shape.Hex && ez >= ex)
        {
            // POINTY-TOP: the vertex pair lies on the long (Z) axis; the flat sides face ±X.
            RingScratch.Add(new Vector2(ex, ez * 0.5f));
            RingScratch.Add(new Vector2(0f, ez));
            RingScratch.Add(new Vector2(-ex, ez * 0.5f));
            RingScratch.Add(new Vector2(-ex, -ez * 0.5f));
            RingScratch.Add(new Vector2(0f, -ez));
            RingScratch.Add(new Vector2(ex, -ez * 0.5f));
        }
        else if (shape == Shape.Hex)
        {
            // FLAT-TOP: the vertex pair lies on the long (X) axis.
            RingScratch.Add(new Vector2(ex, 0f));
            RingScratch.Add(new Vector2(ex * 0.5f, ez));
            RingScratch.Add(new Vector2(-ex * 0.5f, ez));
            RingScratch.Add(new Vector2(-ex, 0f));
            RingScratch.Add(new Vector2(-ex * 0.5f, -ez));
            RingScratch.Add(new Vector2(ex * 0.5f, -ez));
        }
        else
        {
            RingScratch.Add(new Vector2(ex, ez));
            RingScratch.Add(new Vector2(-ex, ez));
            RingScratch.Add(new Vector2(-ex, -ez));
            RingScratch.Add(new Vector2(ex, -ez));
        }

        int n = RingScratch.Count;
        VertScratch.Clear();
        TriScratch.Clear();
        for (int i = 0; i < n; i++) // 0..n-1 = top ring
            VertScratch.Add(new Vector3(cx + RingScratch[i].x, yTop, cz + RingScratch[i].y));
        for (int i = 0; i < n; i++) // n..2n-1 = bottom ring
            VertScratch.Add(new Vector3(cx + RingScratch[i].x, yBot, cz + RingScratch[i].y));

        for (int i = 0; i < n; i++) // side walls
        {
            int j = (i + 1) % n;
            TriScratch.Add(i);
            TriScratch.Add(n + i);
            TriScratch.Add(n + j);
            TriScratch.Add(i);
            TriScratch.Add(n + j);
            TriScratch.Add(j);
        }
        for (int i = 1; i < n - 1; i++) // top cap (fan)
        {
            TriScratch.Add(0);
            TriScratch.Add(i + 1);
            TriScratch.Add(i);
        }
        for (int i = 1; i < n - 1; i++) // bottom cap (fan, opposite winding)
        {
            TriScratch.Add(n);
            TriScratch.Add(n + i);
            TriScratch.Add(n + i + 1);
        }

        var mesh = new Mesh { name = "GloomhavenVR.MrRimCurtain" };
        mesh.SetVertices(VertScratch);
        mesh.SetTriangles(TriScratch, 0);
        mesh.RecalculateBounds();
        mesh.UploadMeshData(markNoLongerReadable: false);
        return mesh;
    }

    /// <summary>Destroy every generated prism mesh (MR off / VR stop / hot reload / a live
    /// inset-or-clearance retune). The curtain GameObjects die structurally with their sources;
    /// the Mesh ASSETS do not, so they are released here.</summary>
    internal static void ReleaseMeshes()
    {
        foreach (KeyValuePair<Key, Mesh> pair in MeshCache)
        {
            if (pair.Value != null)
                Object.Destroy(pair.Value);
        }
        MeshCache.Clear();
        ResetCensus();
    }
}
