using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Core;

/// <summary>
/// THE GEOMETRY THE SWELL NEEDS, AND THE CULLING BILL IT COMES WITH.
///
/// <para>WHY THIS FILE EXISTS. <c>GloomhavenVR/WaterVR</c> displaces its vertices vertically to
/// give the pool actual relief — the user's ruling after ModBuild 163 was <i>"Nicht nur 'calm'
/// sondern auch wirklich 3D wellen einbauen. Aktuell waren es nur weiße streifen auf einer flachen
/// Oberfläche"</i>, and no shading term can put relief on a sheet. But the film the game places is
/// <c>TERRAIN_Water_Plane</c>, a flat plane with a handful of vertices, and <b>a vertex program
/// cannot make a wave out of four corners.</b> So the driver hands the renderer a SUBDIVIDED copy
/// of the mesh it found, and takes it back on release.</para>
///
/// <para>WHY SUBDIVISION AND NOT A GENERATED GRID. A grid built to the mesh's bounds would be a
/// GUESS about the film's shape: the pool is laid out in hexes and the quad's own outline, UV
/// orientation and vertex colours are not ours to know. Midpoint refinement cannot guess wrong —
/// every new vertex lies on an existing edge, so the footprint, the UVs, the vertex colours and
/// the silhouette are bit-for-bit the surface the game authored, only sampled more finely. It
/// works on a hex, a quad, a fan or a strip without knowing which it was given.</para>
///
/// <para><b>THE CULLING TRAP, PAID EXACTLY.</b> Unity culls a renderer against its MESH's bounds,
/// so geometry a vertex program pushes outside them is culled anyway: a displaced surface vanishes
/// as you walk up to it and, under MultiPass, vanishes in ONE EYE FIRST because the two eye
/// frustums differ. This project has already lost a build to that, and the fix there had to be an
/// arc SWEEP because the displacement was a rotation, whose swept volume is not the rest bounds
/// plus a constant. <b>Here it is.</b> The displacement is a translation along world Y, bounded by
/// <see cref="WaterOwnSurface.MaxSwellAmplitude"/>, so the swept volume is exactly the rest bounds
/// Minkowski-summed with a segment of length twice that. Padding every LOCAL axis by that
/// amplitude contains that segment whatever the quad's orientation turns out to be, which is why
/// this is the true swept volume and not a guess-pad.</para>
///
/// <para>AND THE PAD IS THE CEILING, NOT THE CURRENT DIAL. The bounds are computed from
/// <see cref="WaterOwnSurface.MaxSwellAmplitude"/> and never from the amplitude actually in force,
/// so a player who raises <c>[Water] SwellHeight</c> mid-scene cannot outrun bounds that were baked
/// for a lower one — the failure that would arrive as "the water disappears when I lean in", i.e.
/// as the exact old bug, one dial-turn later.</para>
///
/// <para>THE COST IS STATED, NOT ASSUMED. This is a Quest 3 over Virtual Desktop at 90 Hz and the
/// perf log already shows the main thread blocked ~92% of the frame waiting on the GPU. A film
/// quad at <see cref="WaterOwnSurface.MaxSubdivisionLevel"/> becomes 128 triangles / 81 vertices
/// from 2 / 4, so the report's pool of
/// 17 films is 2176 triangles and 1377 vertices where it was 34 and 68. That is one small prop's
/// worth of geometry for the whole pool, it is static per mesh (built once, shared by every film
/// that uses the same source), and the census prints both counts.</para>
/// </summary>
internal static class WaterSwellMesh
{
    /// <summary>Built meshes, keyed by source mesh, level and the local-space pad — so the 17
    /// films of one pool share ONE mesh and one buffer upload. Cleared with everything else on
    /// restore.</summary>
    private static readonly Dictionary<long, Mesh> Cache = new(8);

    /// <summary>Counters for the census: a mesh swap that silently did nothing looks exactly like
    /// one that worked, which is the trap this whole module is built out of.</summary>
    internal static int MeshesBuilt { get; private set; }

    internal static int VerticesBuilt { get; private set; }

    /// <summary>
    /// The subdivided, bounds-padded stand-in for <paramref name="source"/>, or null when this
    /// mesh cannot be refined — in which case the caller must leave the renderer's own mesh alone
    /// and the film is flat, which is a worse look and never a broken one.
    /// </summary>
    /// <param name="source">The renderer's authored <c>sharedMesh</c>. Never modified.</param>
    /// <param name="widthWU">The film's largest horizontal extent in world units, off its renderer
    /// bounds — what <see cref="WaterOwnSurface.SubdivisionLevel"/> measures the edge length against.</param>
    /// <param name="minScale">The smallest component of the renderer's lossy scale. The amplitude
    /// is a WORLD length and the bounds are LOCAL, so the pad is divided by this; a scale of 100
    /// (which this project's armatures really do use) would otherwise pad a hundredfold.</param>
    /// <param name="note">Always set: what was built, or why nothing was. Goes into the census.</param>
    internal static Mesh? Build(Mesh source, float widthWU, float minScale, out string note)
    {
        note = "";
        if (source == null)
        {
            note = "no source mesh";
            return null;
        }
        if (!source.isReadable)
        {
            // Reading vertices off a non-readable mesh logs an engine error and returns nothing.
            // Refusing here keeps the film flat rather than filling the log with red text nobody
            // can act on — the import setting belongs to the game, not to this mod.
            note = "'" + source.name + "' is not READABLE, so it cannot be subdivided — the film "
                   + "stays flat and keeps its own mesh";
            return null;
        }

        int level = WaterOwnSurface.SubdivisionLevel(widthWU, source.triangles.Length / 3);
        if (level <= 0)
        {
            note = "'" + source.name + "' is already fine enough at "
                   + (source.triangles.Length / 3) + " triangles over " + widthWU.ToString("0.##")
                   + " world units — kept as authored";
            return null;
        }

        float scale = float.IsNaN(minScale) || minScale <= 1e-4f ? 1f : minScale;
        float pad = WaterOwnSurface.MaxSwellAmplitude / scale;

        long key = ((long)source.GetInstanceID() << 24)
                   ^ ((long)level << 20)
                   ^ Mathf.RoundToInt(pad * 1000f);
        if (Cache.TryGetValue(key, out Mesh cached) && cached != null)
        {
            note = "'" + cached.name + "' (cached) " + cached.vertexCount + " verts / "
                   + (cached.triangles.Length / 3) + " tris, bounds padded "
                   + pad.ToString("0.###") + " local units on every axis";
            return cached;
        }

        Mesh built;
        try
        {
            built = Subdivide(source, level);
        }
        catch (Exception e)
        {
            note = "subdividing '" + source.name + "' threw " + e.GetType().Name
                   + " — the film keeps its own mesh and stays flat";
            return null;
        }

        // THE PAD. Every local axis, because the mesh is shared across renderers whose rotations
        // this module does not read, and a segment along world Y is contained in a box grown by
        // the same amount on all three local axes under any rotation.
        Bounds b = built.bounds;
        b.Expand(pad * 2f); // Expand() takes the TOTAL growth, i.e. half of it per side
        built.bounds = b;

        MeshesBuilt++;
        VerticesBuilt += built.vertexCount;
        Cache[key] = built;
        note = "'" + source.name + "' " + source.vertexCount + " verts / "
               + (source.triangles.Length / 3) + " tris -> '" + built.name + "' "
               + built.vertexCount + " verts / " + (built.triangles.Length / 3) + " tris (level "
               + level + ", target edge " + WaterOwnSurface.TargetEdgeWU.ToString("0.##")
               + " world units), bounds padded " + pad.ToString("0.###")
               + " local units on every axis for a peak displacement of "
               + WaterOwnSurface.MaxSwellAmplitude.ToString("0.###") + " world units";
        return built;
    }

    /// <summary>Drop every built mesh. Called from the driver's own restore path — a mod-owned
    /// mesh must no more outlive its owner than a mod-owned material does.</summary>
    internal static void Clear()
    {
        foreach (KeyValuePair<long, Mesh> kv in Cache)
        {
            if (kv.Value == null)
                continue;
            try { UnityEngine.Object.Destroy(kv.Value); }
            catch { /* already gone with the scene */ }
        }
        Cache.Clear();
    }

    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// One-to-four midpoint refinement, <paramref name="level"/> times, welded.
    ///
    /// <para>WELDED VIA AN EDGE MAP rather than by rebuilding vertices per triangle: an unwelded
    /// refinement triples the vertex count for the same surface, and every duplicate is a buffer
    /// upload and a vertex-shader invocation on a headset that is already GPU-bound. Welding is
    /// also what keeps the vertex COUNT honest in the census.</para>
    ///
    /// <para>Positions, UVs, colours, normals and tangents are all carried through by linear
    /// interpolation — the surface is planar, so a midpoint's interpolated attribute IS its
    /// attribute. Nothing is invented and nothing is dropped.</para>
    /// </summary>
    private static Mesh Subdivide(Mesh source, int level)
    {
        var pos = new List<Vector3>(source.vertices);
        var uv = new List<Vector2>(source.uv);
        var col = new List<Color32>(source.colors32);
        var nrm = new List<Vector3>(source.normals);
        var tan = new List<Vector4>(source.tangents);

        bool hasUv = uv.Count == pos.Count;
        bool hasCol = col.Count == pos.Count;
        bool hasNrm = nrm.Count == pos.Count;
        bool hasTan = tan.Count == pos.Count;

        int subs = source.subMeshCount;
        var indices = new List<int[]>(subs);
        for (int s = 0; s < subs; s++)
            indices.Add(source.GetTriangles(s));

        for (int step = 0; step < level; step++)
        {
            var mid = new Dictionary<long, int>(pos.Count * 3);

            int Midpoint(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int found))
                    return found;
                int index = pos.Count;
                pos.Add((pos[a] + pos[b]) * 0.5f);
                if (hasUv) uv.Add((uv[a] + uv[b]) * 0.5f);
                if (hasCol) col.Add(Color32.Lerp(col[a], col[b], 0.5f));
                if (hasNrm) nrm.Add((nrm[a] + nrm[b]).normalized);
                if (hasTan)
                {
                    Vector4 t = (tan[a] + tan[b]) * 0.5f;
                    var xyz = new Vector3(t.x, t.y, t.z).normalized;
                    // The w is the bitangent's SIGN, so it is +/-1 and never an average of the
                    // two: a midpoint that inherited 0 would flip the bitangent to nothing.
                    tan.Add(new Vector4(xyz.x, xyz.y, xyz.z, tan[a].w));
                }
                mid[key] = index;
                return index;
            }

            for (int s = 0; s < subs; s++)
            {
                int[] tris = indices[s];
                var next = new int[tris.Length * 4];
                int w = 0;
                for (int i = 0; i + 2 < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                    next[w++] = a; next[w++] = ab; next[w++] = ca;
                    next[w++] = ab; next[w++] = b; next[w++] = bc;
                    next[w++] = ca; next[w++] = bc; next[w++] = c;
                    next[w++] = ab; next[w++] = bc; next[w++] = ca;
                }
                indices[s] = next;
            }
        }

        var m = new Mesh
        {
            name = source.name + "_GhvrSwell" + level,
            // A refined water quad passes 65k vertices only if the source was already enormous,
            // and WaterOwnSurface.MaxSubdivisionTriangles stops that long before — but the format is cheap insurance against
            // a silently truncated index buffer, which would arrive as holes in the pool.
            indexFormat = pos.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
        };
        m.SetVertices(pos);
        if (hasUv) m.SetUVs(0, uv);
        if (hasCol) m.SetColors(col);
        if (hasNrm) m.SetNormals(nrm);
        if (hasTan) m.SetTangents(tan);
        m.subMeshCount = subs;
        for (int s = 0; s < subs; s++)
            m.SetTriangles(indices[s], s, calculateBounds: false);
        m.RecalculateBounds();
        return m;
    }
}
