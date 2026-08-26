using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// ROUND 17 — THE CARD BODY IS TRULY PUNCHED OUT: geometry, not alpha.
///
/// USER RULING (2026-08-11, verbatim, binding): "Es kommt mir auch weiterhin so vor, dass die
/// Karte auf einem rechteckigen (jetzt bechen früher schwarzen) mesh einer rechteckigen Karte
/// liegt. Die Aufgabe ist doch eher das mesh der Karte auf das outline der Kartenoberfläche
/// 'auszustanzen'. Kümmer dich auch wieder darum dass die Karte wieder vollständig richtig
/// angezeigt wird - die Änderungen die dazu geführt haben waren offensichtlich nicht die Lösung
/// des Problems."
///
/// <para>WHY GEOMETRY. Sixteen rounds tried to make the rectangular slab LOOK punched — sprite
/// punches, rect crops, an alpha-cutout material clip. The cutout clip is hostage to the game
/// build's Standard shader variants (round 16's unresolved chief suspect: a build that stripped
/// the <c>_ALPHATEST_ON</c> variant silently selects one that never clips, and the slab paints
/// its full rectangular envelope — which matches the measured band to within 1/255). A mesh
/// whose BOUNDARY IS the card outline cannot be un-punched by any shader variant: there simply
/// is no fragment outside the contour to draw.</para>
///
/// <para>THE PIPELINE, all of it once per card kind per session, at the moment the footprint is
/// applied (<c>CardMesh.SetSilhouette</c> — cache load on warm launches, live capture on the
/// first), never per frame:
/// <list type="number">
/// <item>ISO-EXTRACTION: marching squares on the cached 224x343 silhouette footprint at the
///   0.5 alpha iso, with corner values interpolated so anti-aliased edge texels land the
///   contour sub-texel. The sample grid is virtually padded with one transparent ring so a
///   footprint touching the image border still yields CLOSED loops. Segments are emitted
///   oriented (inside on the LEFT), so chained loops are CCW around solid regions by
///   construction; the LARGEST closed loop by enclosed area is the card outline.</item>
/// <item>SIMPLIFICATION: closed-loop Douglas-Peucker. Tolerance starts at
///   <see cref="SimplifyToleranceTexels"/> texels (0.3 tx ≈ 0.09 mm on a 63.5 mm card at the
///   footprint's 224-texel width — invisible) and is escalated ×1.5 until the loop fits the
///   <see cref="MaxVertices"/> = 120 vertex budget. The escalation is logged.</item>
/// <item>MESH: front face = ear-clipped triangulation of the polygon (n ≤ 120 → a trivial
///   O(n²) once); back face = the same triangles mirrored (drawn through mirrored UVs exactly
///   like the rounded slab's back); rim = a wall extruded along the contour between z = 0 and
///   z = <c>CardMesh.Thickness</c> with hard outward normals. UVs are planar card-space
///   (u = x/width + 0.5) on every vertex, and the rim samples a hair INSIDE its contour point
///   (<c>CardMesh.RimUvInset</c> — the same trick the rounded slab's rim uses), so any texture
///   the shared materials ever carry maps identically to today.</item>
/// </list></para>
///
/// <para>THE BOX-METRICS INVARIANT (standing): nothing that MEASURES a card may measure the
/// shape. The built mesh's <c>bounds</c> are therefore pinned to the full card box
/// (width × height × thickness, the rounded slab's envelope) — a diagnostic or layout reading
/// mesh bounds sees the box, never the contour. Collider, fan layout, dock apron and the sweep's
/// face width (<c>VRCard.SweepFaceWidthWorld</c>) all derive from
/// <c>CardsConfig.CardWidth/CardHeight</c> and are
/// untouched by construction.</para>
///
/// <para>DEGRADES TO TODAY: every refusal (no loop, implausible area, triangulation failure)
/// returns null with a stated reason and the kind keeps the rounded-rect slab — the standing
/// rule, degrade to the rectangle, never to a wrong shape.</para>
/// </summary>
internal static class CardContour
{
    /// <summary>Vertex budget for the simplified outline polygon (the mission's ≤ 120).</summary>
    internal const int MaxVertices = 120;

    /// <summary>Starting Douglas-Peucker tolerance in footprint texels. 0.3 tx on the 224-wide
    /// footprint ≈ 0.09 mm at the real 63.5 mm card width — far below anything the eye resolves
    /// at fan distance; escalated ×1.5 until the vertex budget holds.</summary>
    internal const float SimplifyToleranceTexels = 0.3f;

    /// <summary>The extracted polygon must enclose at least this fraction of the footprint —
    /// mirrors <c>CardMesh.SetSilhouette</c>'s own plausibility window.</summary>
    private const float MinAreaFraction = 0.5f;

    private const float Iso = 0.5f;

    /// <summary>
    /// Extract the card outline polygon from a footprint alpha mask. Returns CCW points in the
    /// footprint's own normalized 0..1 space (u right, v up), or null with a
    /// <paramref name="refusal"/>. Pure function of the mask — deterministic, so the contour
    /// never needs its own cache file: it re-derives identically from the persisted footprint
    /// every launch.
    /// </summary>
    internal static Vector2[]? Extract(byte[] alpha, int w, int h, out string? refusal)
    {
        refusal = null;
        if (alpha == null || w < 8 || h < 8 || alpha.Length != w * h)
        {
            refusal = $"malformed footprint ({w}x{h})";
            return null;
        }

        // Sample function over a virtually padded grid: one transparent ring around the mask so
        // loops are closed even when opaque texels touch the border.
        float Sample(int x, int y) =>
            (x < 0 || y < 0 || x >= w || y >= h) ? 0f : alpha[y * w + x] / 255f;

        // ---- marching squares: oriented segments (inside on the LEFT → CCW loops) ----------
        // Cell (x, y) spans corners bl=(x,y) br=(x+1,y) tr=(x+1,y+1) tl=(x,y+1) in PIXEL-CENTER
        // coordinates; x runs -1..w-1 and y runs -1..h-1 to cover the padded ring.
        var segStart = new Dictionary<long, int>(w * 4);
        var starts = new List<Vector2>(w * 4);
        var ends = new List<Vector2>(w * 4);

        // Offset keeps both quantized coordinates non-negative (the padded ring reaches −1), so
        // the packed key is collision-free by construction.
        long Key(Vector2 p) => ((long)(Mathf.RoundToInt(p.x * 4096f) + 8192) << 26)
                               | (long)(Mathf.RoundToInt(p.y * 4096f) + 8192);

        void Emit(Vector2 a, Vector2 b)
        {
            segStart[Key(a)] = starts.Count;
            starts.Add(a);
            ends.Add(b);
        }

        for (int y = -1; y < h; y++)
        {
            for (int x = -1; x < w; x++)
            {
                float bl = Sample(x, y), br = Sample(x + 1, y);
                float tr = Sample(x + 1, y + 1), tl = Sample(x, y + 1);
                int c = (bl >= Iso ? 1 : 0) | (br >= Iso ? 2 : 0)
                        | (tr >= Iso ? 4 : 0) | (tl >= Iso ? 8 : 0);
                if (c == 0 || c == 15)
                    continue;

                // Interpolated crossing points on the four cell edges (t of the 0.5 iso).
                Vector2 B = new(x + T(bl, br), y);          // bottom edge
                Vector2 R = new(x + 1f, y + T(br, tr));     // right edge
                Vector2 Tp = new(x + T(tl, tr), y + 1f);    // top edge
                Vector2 L = new(x, y + T(bl, tl));          // left edge

                switch (c)
                {
                    case 1: Emit(B, L); break;
                    case 2: Emit(R, B); break;
                    case 3: Emit(R, L); break;
                    case 4: Emit(Tp, R); break;
                    case 5: // saddle — the cell centre decides connectivity
                        if ((bl + br + tr + tl) * 0.25f >= Iso) { Emit(B, R); Emit(Tp, L); }
                        else { Emit(B, L); Emit(Tp, R); }
                        break;
                    case 6: Emit(Tp, B); break;
                    case 7: Emit(Tp, L); break;
                    case 8: Emit(L, Tp); break;
                    case 9: Emit(B, Tp); break;
                    case 10:
                        if ((bl + br + tr + tl) * 0.25f >= Iso) { Emit(L, B); Emit(R, Tp); }
                        else { Emit(R, B); Emit(L, Tp); }
                        break;
                    case 11: Emit(R, Tp); break;
                    case 12: Emit(L, R); break;
                    case 13: Emit(B, R); break;
                    case 14: Emit(L, B); break;
                }
            }
        }
        if (starts.Count < 8)
        {
            refusal = $"only {starts.Count} iso segment(s) — no card-sized contour at alpha 0.5";
            return null;
        }

        // ---- chain segments into loops; keep the largest by enclosed area ------------------
        var used = new bool[starts.Count];
        List<Vector2>? best = null;
        float bestArea = 0f;
        for (int seed = 0; seed < starts.Count; seed++)
        {
            if (used[seed])
                continue;
            var loop = new List<Vector2>(256);
            int cur = seed;
            bool closed = false;
            while (!used[cur])
            {
                used[cur] = true;
                loop.Add(starts[cur]);
                if (!segStart.TryGetValue(Key(ends[cur]), out int next))
                    break; // open chain (numerical mismatch) — abandoned, never guessed shut
                if (next == seed)
                {
                    closed = true;
                    break;
                }
                cur = next;
            }
            if (!closed || loop.Count < 8)
                continue;
            float area = SignedArea(loop);
            if (area > bestArea) // CCW (solid-region) loops have positive area by construction
            {
                bestArea = area;
                best = loop;
            }
        }
        if (best == null)
        {
            refusal = "no closed CCW loop chained from the iso segments";
            return null;
        }
        float areaFrac = bestArea / (w * (float)h);
        if (areaFrac < MinAreaFraction)
        {
            refusal = $"largest loop encloses only {areaFrac:P1} of the footprint " +
                      $"(gate {MinAreaFraction:P0}) — not a card outline";
            return null;
        }

        // ---- closed-loop Douglas-Peucker down to the vertex budget -------------------------
        float tol = SimplifyToleranceTexels;
        List<Vector2> simple = SimplifyClosed(best, tol);
        int escalations = 0;
        while (simple.Count > MaxVertices && escalations < 12)
        {
            tol *= 1.5f;
            escalations++;
            simple = SimplifyClosed(best, tol);
        }
        if (simple.Count < 8 || simple.Count > MaxVertices)
        {
            refusal = $"simplification failed ({simple.Count} vertices at tolerance {tol:F2} tx)";
            return null;
        }
        if (SignedArea(simple) <= 0f)
        {
            refusal = "simplified loop lost its CCW orientation";
            return null;
        }
        if (escalations > 0)
        {
            VRLog.Info("Cards", $"CARD CONTOUR: simplification tolerance escalated {escalations}× to " +
                                $"{tol:F2} texels to fit the {MaxVertices}-vertex budget " +
                                $"({best.Count} raw → {simple.Count} vertices).");
        }

        // Normalize from pixel-center coordinates into the footprint's 0..1 space — the same
        // space the mesh's planar card UVs live in (texel centre (x, y) ↔ ((x+0.5)/w, (y+0.5)/h)).
        var outPts = new Vector2[simple.Count];
        for (int i = 0; i < simple.Count; i++)
            outPts[i] = new Vector2((simple[i].x + 0.5f) / w, (simple[i].y + 0.5f) / h);
        return outPts;
    }

    /// <summary>Iso crossing parameter between two corner values (0.5 fallback keeps a degenerate
    /// pair from dividing by zero — both sides then straddle the iso equally).</summary>
    private static float T(float a, float b) =>
        Mathf.Abs(b - a) < 1e-6f ? 0.5f : Mathf.Clamp01((Iso - a) / (b - a));

    private static float SignedArea(List<Vector2> pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p = pts[i];
            Vector2 q = pts[(i + 1) % pts.Count];
            area += p.x * q.y - q.x * p.y;
        }
        return area * 0.5f;
    }

    // ------------------------------------------------------------- Douglas-Peucker (closed) --

    private static List<Vector2> SimplifyClosed(List<Vector2> loop, float tolerance)
    {
        // Split the ring at its two mutually farthest anchor points (index 0 and the point
        // farthest from it), run open-chain DP on both halves, and stitch.
        int far = 0;
        float farD = -1f;
        for (int i = 1; i < loop.Count; i++)
        {
            float d = (loop[i] - loop[0]).sqrMagnitude;
            if (d > farD)
            {
                farD = d;
                far = i;
            }
        }
        var keep = new bool[loop.Count];
        keep[0] = true;
        keep[far] = true;
        DouglasPeucker(loop, 0, far, tolerance, keep);
        DouglasPeuckerTail(loop, far, tolerance, keep);
        var result = new List<Vector2>(64);
        for (int i = 0; i < loop.Count; i++)
            if (keep[i])
                result.Add(loop[i]);
        return result;
    }

    private static void DouglasPeucker(List<Vector2> pts, int a, int b, float tol, bool[] keep)
    {
        if (b - a < 2)
            return;
        float maxD = 0f;
        int maxI = -1;
        for (int i = a + 1; i < b; i++)
        {
            float d = PerpDistance(pts[i], pts[a], pts[b]);
            if (d > maxD)
            {
                maxD = d;
                maxI = i;
            }
        }
        if (maxI < 0 || maxD <= tol)
            return;
        keep[maxI] = true;
        DouglasPeucker(pts, a, maxI, tol, keep);
        DouglasPeucker(pts, maxI, b, tol, keep);
    }

    /// <summary>DP over the wrapped half a → (end of array) → index 0, against the chord
    /// (pts[a], pts[0]) — the ring's second half, without materialising a rotated copy.</summary>
    private static void DouglasPeuckerTail(List<Vector2> pts, int a, float tol, bool[] keep)
    {
        int n = pts.Count;
        if (n - a < 2)
            return;
        float maxD = 0f;
        int maxI = -1;
        for (int i = a + 1; i < n; i++)
        {
            float d = PerpDistance(pts[i], pts[a], pts[0]);
            if (d > maxD)
            {
                maxD = d;
                maxI = i;
            }
        }
        if (maxI < 0 || maxD <= tol)
            return;
        keep[maxI] = true;
        DouglasPeucker(pts, a, maxI, tol, keep);
        DouglasPeuckerTail(pts, maxI, tol, keep);
    }

    private static float PerpDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.magnitude;
        if (len < 1e-6f)
            return (p - a).magnitude;
        return Mathf.Abs((p.x - a.x) * ab.y - (p.y - a.y) * ab.x) / len;
    }

    // ---------------------------------------------------------------------- mesh building --

    /// <summary>
    /// Build the punched-out card body from a CCW contour in footprint 0..1 space. Same
    /// conventions as the rounded slab (<c>CardMesh.Build</c>): submesh 0 = front + rim (edge
    /// material), submesh 1 = back (card-back material); front at z = 0 facing −Z (viewer),
    /// back at z = <paramref name="thickness"/>; planar card-space UVs, back UVs mirrored in X,
    /// rim UVs pulled <paramref name="rimUvInset"/> inside their contour point. Bounds pinned
    /// to the FULL card box (the box-metrics invariant). Null on triangulation failure.
    /// </summary>
    internal static Mesh? BuildBody(Vector2[] contour01, float width, float height,
                                    float thickness, float rimUvInset, out string? refusal)
    {
        refusal = null;
        int n = contour01.Length;
        if (n < 8)
        {
            refusal = $"contour has only {n} vertices";
            return null;
        }

        int[]? tris = EarClip(contour01);
        if (tris == null)
        {
            refusal = "ear-clipping failed (degenerate/self-intersecting polygon)";
            return null;
        }

        // Footprint 0..1 → card-local meters (the footprint spans the body's full envelope —
        // the same mapping the slab's planar UVs use: u = x/width + 0.5).
        var local = new Vector2[n];
        for (int i = 0; i < n; i++)
            local[i] = new Vector2((contour01[i].x - 0.5f) * width, (contour01[i].y - 0.5f) * height);

        var vertices = new Vector3[n * 2 + n * 2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];
        int frontBase = 0;
        int backBase = n;
        int rimBase = n * 2;

        for (int i = 0; i < n; i++)
        {
            Vector2 p = local[i];
            Vector2 uvP = contour01[i];

            vertices[frontBase + i] = new Vector3(p.x, p.y, 0f);
            normals[frontBase + i] = Vector3.back; // viewer side
            uv[frontBase + i] = uvP;

            vertices[backBase + i] = new Vector3(p.x, p.y, thickness);
            normals[backBase + i] = Vector3.forward;
            // Mirror X so the back pattern is not a mirror image when seen from +Z — the
            // rounded slab's exact convention.
            uv[backBase + i] = new Vector2(1f - uvP.x, uvP.y);

            // Rim duplicates with hard outward normals: average of the two adjacent edges'
            // outward perpendiculars (CCW polygon → outward = (dy, −dx) of the edge direction).
            Vector2 prev = local[(i - 1 + n) % n];
            Vector2 next = local[(i + 1) % n];
            Vector2 dIn = (p - prev).normalized;
            Vector2 dOut = (next - p).normalized;
            var outward2 = new Vector2(dIn.y + dOut.y, -(dIn.x + dOut.x));
            outward2 = outward2.sqrMagnitude < 1e-10f
                ? new Vector2(dIn.y, -dIn.x)
                : outward2.normalized;
            var outward = new Vector3(outward2.x, outward2.y, 0f);

            vertices[rimBase + i * 2] = new Vector3(p.x, p.y, 0f);
            vertices[rimBase + i * 2 + 1] = new Vector3(p.x, p.y, thickness);
            normals[rimBase + i * 2] = outward;
            normals[rimBase + i * 2 + 1] = outward;
            // Sample a hair INSIDE the contour point (same trick as the rounded slab's rim) so
            // any texture the shared material carries reads solid interior on the thin edge.
            Vector2 pin = new(p.x - outward2.x * (width * rimUvInset),
                              p.y - outward2.y * (height * rimUvInset));
            var uvRim = new Vector2(
                Mathf.Clamp01(pin.x / width + 0.5f),
                Mathf.Clamp01(pin.y / height + 0.5f));
            uv[rimBase + i * 2] = uvRim;
            uv[rimBase + i * 2 + 1] = uvRim;
        }

        // Submesh 0: front + rim. The polygon is CCW in XY; a viewer on the −Z side needs
        // CLOCKWISE winding, so the ear-clip triangles (CCW) are emitted reversed — the same
        // reasoning as the rounded slab's front fan.
        int triCount = tris.Length / 3;
        var frontRim = new int[tris.Length + n * 6];
        int t = 0;
        for (int k = 0; k < triCount; k++)
        {
            frontRim[t++] = frontBase + tris[k * 3];
            frontRim[t++] = frontBase + tris[k * 3 + 2];
            frontRim[t++] = frontBase + tris[k * 3 + 1];
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int a = rimBase + i * 2;        // front, i
            int b = rimBase + i * 2 + 1;    // back, i
            int c = rimBase + next * 2;     // front, next
            int d = rimBase + next * 2 + 1; // back, next
            frontRim[t++] = a; frontRim[t++] = c; frontRim[t++] = b;
            frontRim[t++] = c; frontRim[t++] = d; frontRim[t++] = b;
        }
        // Submesh 1: back — viewed from +Z the x-axis appears mirrored, so the CCW-in-XY
        // ear-clip order reads clockwise there and is emitted as-is.
        var back = new int[tris.Length];
        for (int k = 0; k < tris.Length; k++)
            back[k] = backBase + tris[k];

        var mesh = new Mesh { name = "GloomhavenVR.CardBodyPunched" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frontRim, 0);
        mesh.SetTriangles(back, 1);
        // BOX-METRICS INVARIANT: bounds pinned to the full card box, NOT the contour — any
        // consumer reading mesh bounds (diagnostics, furniture measuring) sees the same
        // envelope the rounded slab had. Assign AFTER SetTriangles (which recalculates).
        mesh.bounds = new Bounds(new Vector3(0f, 0f, thickness * 0.5f),
                                 new Vector3(width, height, thickness));
        return mesh;
    }

    // --------------------------------------------------------------------- ear clipping --

    /// <summary>Ear-clip triangulation of a CCW simple polygon. Null on failure. O(n²) at
    /// n ≤ 120, run once per kind per session at footprint apply — no per-frame cost.</summary>
    private static int[]? EarClip(Vector2[] pts)
    {
        int n = pts.Length;
        var idx = new List<int>(n);
        for (int i = 0; i < n; i++)
            idx.Add(i);
        var tris = new List<int>((n - 2) * 3);
        int guard = 0;
        int guardMax = n * n * 2;
        while (idx.Count > 3 && guard++ < guardMax)
        {
            bool clipped = false;
            for (int k = 0; k < idx.Count; k++)
            {
                int i0 = idx[(k - 1 + idx.Count) % idx.Count];
                int i1 = idx[k];
                int i2 = idx[(k + 1) % idx.Count];
                Vector2 a = pts[i0], b = pts[i1], c = pts[i2];
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (cross <= 1e-12f)
                    continue; // reflex or degenerate — not an ear on a CCW polygon
                bool contains = false;
                for (int m = 0; m < idx.Count; m++)
                {
                    int im = idx[m];
                    if (im == i0 || im == i1 || im == i2)
                        continue;
                    if (PointInTriangle(pts[im], a, b, c))
                    {
                        contains = true;
                        break;
                    }
                }
                if (contains)
                    continue;
                tris.Add(i0);
                tris.Add(i1);
                tris.Add(i2);
                idx.RemoveAt(k);
                clipped = true;
                break;
            }
            if (!clipped)
                return null; // no ear found — degenerate input, refuse rather than guess
        }
        if (idx.Count == 3)
        {
            tris.Add(idx[0]);
            tris.Add(idx[1]);
            tris.Add(idx[2]);
        }
        return tris.Count >= 3 ? tris.ToArray() : null;
    }

    private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
        float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
        float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
        bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
        bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
        return !(hasNeg && hasPos);
    }
}
