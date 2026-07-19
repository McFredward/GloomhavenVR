using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Procedural 3D card body (P7, hardware test #10: "cards look flat, I want real 3D
/// cards"). A rounded-rectangle slab with genuine thickness: front face (viewer side,
/// -Z), back face (+Z, decorative card-back pattern), and a rim wall — so a card read
/// edge-on or from behind looks like a physical card, not a textured quad.
///
/// Used by <see cref="VRCard"/> as the fallback when the bundle has no
/// <c>CardBacking.prefab</c> (asset contract: unity/.../Table/README.md). Geometry is
/// authored at real card size (meters, 1 unit = 1 m); layouts scale the transform.
/// Everything here runs once per prefab-less card build — no per-frame cost.
///
/// Convention (module-wide): +Z points AWAY from the viewer. The live face canvas
/// floats at z = -0.0012 in front of the front face (z = 0); the slab extends from
/// z = 0 to z = +thickness, i.e. entirely behind the face — matching the README
/// contract "front face area flush around z ≈ 0..+0.001".
///
/// Silhouette (hardware test #25): the game's ability cards are NOT plain rectangles —
/// the card art has an artistic, non-rectangular outline (transparent decorative
/// edges), and the rounded-rect slab's dark front/rim used to show as a rectangular
/// border AROUND that art. <see cref="SetSilhouette"/> switches the SHARED front/rim +
/// back materials to alpha-CLIP (stock Standard shader, Cutout mode) against a
/// card-space alpha footprint captured from the LIVE card art at runtime (see
/// <see cref="CardFace"/>), so the visible 3D silhouette (front border ring, thin rim,
/// decorative back) follows the card's ACTUAL outline instead of a rectangle. The mesh
/// GEOMETRY is unchanged (the full-envelope rounded slab, same pivot/size → grab
/// collider and fan layout unaffected); only the fragments inside the art outline
/// survive the clip. Every vertex — front, back AND rim — carries card-space planar UVs
/// so the single footprint texture maps onto every face; the rim samples a hair INSIDE
/// its outline point so the thin edge survives the clip along the solid silhouette. If
/// no footprint is ever supplied (or it fails the sanity guard) the materials stay
/// opaque — exactly the round-24 rounded-rect look, so this can only ever add the
/// ornate outline, never regress.
/// </summary>
internal static class CardMesh
{
    /// <summary>Card body thickness in meters (~1.5 mm — a stiff physical card).</summary>
    internal const float Thickness = 0.0015f;

    /// <summary>
    /// Corner radius in meters. Real Gloomhaven / poker cards round at ~3 mm on a
    /// 63.5 mm width (≈4.7% of width) — the gentle curve the card art outline follows.
    /// </summary>
    internal const float CornerRadius = 0.003f;

    // Arc subdivision per 90° corner. Test #24: the thin dark front now reads AS the
    // card's rounded border (see CardFace inset), so the corners must look smoothly
    // curved rather than faceted. 6 segments -> 4 arcs x 7 points = 28-point outline,
    // still a trivial one-time build.
    private const int CornerSegments = 6;

    // How far (fraction of card size) the rim's UV sample is pulled INWARD from its
    // outline point, so the thin edge samples fully-opaque interior of the silhouette
    // footprint (alpha ≈ 1) and survives the alpha clip along the solid outline instead
    // of straddling the ~0.5 alpha boundary. ~2 % ≈ 1.3 mm on a 63.5 mm card.
    private const float RimUvInset = 0.02f;

    private static Mesh? _sharedMesh;
    private static Vector2 _sharedMeshSize;
    private static Texture2D? _backTexture;

    // Shared front/rim + back materials (one pair for ALL cards). Cached so that a
    // silhouette supplied AFTER cards are built (SetSilhouette, first CardFace.Adopt)
    // mutates the very instances every backing renderer already references — so every
    // live card adopts the ornate outline at once. See VRCard.BuildProceduralBacking.
    private static Material? _edgeMaterial;
    private static Material? _backMaterial;
    private static bool _silhouetteApplied;

    /// <summary>
    /// Build (or reuse) the rounded slab mesh for the given card size. Submesh 0 =
    /// front + rim (dark edge material), submesh 1 = back (card-back material).
    /// The mesh is shared between all cards of the same size.
    /// </summary>
    internal static Mesh Get(float width, float height)
    {
        var size = new Vector2(width, height);
        if (_sharedMesh != null && _sharedMeshSize == size)
            return _sharedMesh;
        _sharedMesh = Build(width, height);
        _sharedMeshSize = size;
        return _sharedMesh;
    }

    private static Mesh Build(float width, float height)
    {
        float hw = width * 0.5f;
        float hh = height * 0.5f;
        float r = Mathf.Min(CornerRadius, Mathf.Min(hw, hh) * 0.45f);

        // Rounded-rect outline, counter-clockwise seen from the FRONT (-Z side).
        int loopCount = 4 * (CornerSegments + 1);
        var outline = new Vector2[loopCount];
        int k = 0;
        // Corner centers: TR, TL, BL, BR — sweeping 90° each keeps the loop CCW when
        // viewed from -Z (x right, y up, viewer looking along +Z).
        Vector2[] centers =
        {
            new(hw - r, hh - r), new(-hw + r, hh - r),
            new(-hw + r, -hh + r), new(hw - r, -hh + r),
        };
        float[] startAngles = { 0f, 90f, 180f, 270f };
        for (int c = 0; c < 4; c++)
        {
            for (int s = 0; s <= CornerSegments; s++)
            {
                float a = (startAngles[c] + 90f * s / CornerSegments) * Mathf.Deg2Rad;
                outline[k++] = centers[c] + new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
        }

        // Vertex layout: front loop + front center | back loop + back center |
        // rim (duplicated loop verts front+back for hard outward normals).
        int n = loopCount;
        var vertices = new Vector3[(n + 1) * 2 + n * 2];
        var normals = new Vector3[vertices.Length];
        var uv = new Vector2[vertices.Length];

        int frontBase = 0;              // n outline + 1 center
        int backBase = n + 1;           // n outline + 1 center
        int rimBase = (n + 1) * 2;      // n front + n back

        for (int i = 0; i < n; i++)
        {
            Vector2 p = outline[i];
            var uvP = new Vector2(p.x / width + 0.5f, p.y / height + 0.5f);

            vertices[frontBase + i] = new Vector3(p.x, p.y, 0f);
            normals[frontBase + i] = Vector3.back; // viewer side
            uv[frontBase + i] = uvP;

            vertices[backBase + i] = new Vector3(p.x, p.y, Thickness);
            normals[backBase + i] = Vector3.forward;
            // Mirror X so the back pattern is not a mirror image when seen from +Z.
            uv[backBase + i] = new Vector2(1f - uvP.x, uvP.y);

            // Rim duplicates (outward normal from the outline point).
            Vector3 outward = OutwardNormal(p, hw, hh, r);
            vertices[rimBase + i * 2] = new Vector3(p.x, p.y, 0f);
            vertices[rimBase + i * 2 + 1] = new Vector3(p.x, p.y, Thickness);
            normals[rimBase + i * 2] = outward;
            normals[rimBase + i * 2 + 1] = outward;
            // Card-space planar UV pulled slightly inward (so the silhouette footprint
            // is sampled just INSIDE the outline, alpha ≈ 1 on the solid edge) — the rim
            // then follows the ornate outline under the alpha clip and keeps its 1.5 mm
            // thickness along the visible silhouette. Same value front + back copy so
            // the whole edge wall clips consistently.
            Vector2 pin = new(p.x - outward.x * (width * RimUvInset),
                              p.y - outward.y * (height * RimUvInset));
            var uvRim = new Vector2(
                Mathf.Clamp01(pin.x / width + 0.5f),
                Mathf.Clamp01(pin.y / height + 0.5f));
            uv[rimBase + i * 2] = uvRim;
            uv[rimBase + i * 2 + 1] = uvRim;
        }
        vertices[frontBase + n] = new Vector3(0f, 0f, 0f);
        normals[frontBase + n] = Vector3.back;
        uv[frontBase + n] = new Vector2(0.5f, 0.5f);
        vertices[backBase + n] = new Vector3(0f, 0f, Thickness);
        normals[backBase + n] = Vector3.forward;
        uv[backBase + n] = new Vector2(0.5f, 0.5f);

        // Submesh 0: front fan (facing -Z) + rim quads. Submesh 1: back fan (+Z).
        var frontRim = new int[n * 3 + n * 6];
        var back = new int[n * 3];
        int t = 0;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // Front face: the outline is CCW in the XY plane; a viewer on the -Z
            // side (Unity left-handed: sees +X right, +Y up) needs CLOCKWISE
            // winding — center → next → i.
            frontRim[t++] = frontBase + n;
            frontRim[t++] = frontBase + next;
            frontRim[t++] = frontBase + i;
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int a = rimBase + i * 2;      // front, i
            int b = rimBase + i * 2 + 1;  // back, i
            int c = rimBase + next * 2;   // front, next
            int d = rimBase + next * 2 + 1; // back, next
            frontRim[t++] = a; frontRim[t++] = c; frontRim[t++] = b;
            frontRim[t++] = c; frontRim[t++] = d; frontRim[t++] = b;
        }
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            // Back face: viewed from +Z the x-axis appears mirrored, so the
            // CCW-in-XY order center → i → next reads clockwise there.
            back[i * 3] = backBase + n;
            back[i * 3 + 1] = backBase + i;
            back[i * 3 + 2] = backBase + next;
        }

        var mesh = new Mesh { name = "GloomhavenVR.CardBody" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.subMeshCount = 2;
        mesh.SetTriangles(frontRim, 0);
        mesh.SetTriangles(back, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>
    /// Item 4 (make the button side walls actually VISIBLE): build a real 3D keycap with a
    /// CHAMFERED front edge, authored at its REAL size in meters so the owning transform can
    /// stay unit-scaled (uniform scale keeps the 45° bevel normal a true 45° in world space,
    /// which is what lets it catch light). Three submeshes, coloured by the caller as a bright
    /// top / a BRIGHT parchment-lit bevel ring / a dark warm wall band, so a huge top→bevel→wall
    /// value gradient reads the cap as unmistakably RAISED even viewed near top-down against a
    /// dark board (the old flat-dark walls, ×0.45 of an already-dark top, vanished):
    ///   • submesh 0 — the TOP plateau (flat, faces the viewer at local −Z), inset by
    ///     <paramref name="bevel"/> from the outer edge;
    ///   • submesh 1 — the BEVEL RING: four ~45° chamfer quads bridging the inset plateau edge
    ///     (at z = −thickness) out to the full-size top edge (at z = −thickness + bevel). Angled
    ///     halfway between top and wall, so it is always partly visible AND shades distinctly
    ///     under BoardLit — the primary "this is 3D" cue;
    ///   • submesh 2 — the four vertical side WALLS + the hidden back.
    /// The cap spans local z = −<paramref name="thickness"/> (front/top, viewer side) to 0
    /// (back), matching the old cube placement, so the label offset and press travel are
    /// unchanged. Each face carries its own flat-shaded vertices/normal; triangle winding is
    /// derived from the outward normal so every face is front-facing regardless of corner order.
    /// </summary>
    internal static Mesh BuildBeveledKeycap(float width, float height, float thickness, float bevel)
    {
        float hw = width * 0.5f, hh = height * 0.5f;
        bevel = Mathf.Clamp(bevel, 0f, Mathf.Min(Mathf.Min(hw, hh) * 0.9f, thickness * 0.9f));
        float iw = hw - bevel, ih = hh - bevel;   // inset plateau half-extents
        float zTop = -thickness;                  // frontmost plane (the plateau)
        float zBev = -thickness + bevel;          // where the bevel meets the vertical wall
        float zBack = 0f;                          // hidden back

        var verts = new System.Collections.Generic.List<Vector3>(24);
        var norms = new System.Collections.Generic.List<Vector3>(24);
        var uvs = new System.Collections.Generic.List<Vector2>(24);
        var top = new System.Collections.Generic.List<int>(6);
        var ring = new System.Collections.Generic.List<int>(24);
        var walls = new System.Collections.Generic.List<int>(30);

        // Add a quad (a,b,c,d looping the rim) to submesh <sm> with flat normal <n>. Winding is
        // chosen from the outward normal so the face is always visible from its +n (OUTSIDE) side.
        //
        // FIX (Item 7 — "you can see the button's OWN UNDERSIDE / interior through it"): the whole
        // keycap was wound INSIDE-OUT and every face was back-face-CULLED by BoardLit (its cap
        // materials are `new Material(BoardLit)`, so they keep the shader's default _Cull = 2 =
        // Back — never the board's _Cull = 0). With every outward face culled, only the FAR interior
        // faces survived toward the viewer, so the button read as see-through onto its own back cap /
        // inner walls. Ground truth (the shipping card front face, CardMesh.Build): a triangle is
        // front-facing/visible from the side its RIGHT-HAND normal (Cross(v1−v0, v2−v0)) points
        // TOWARD. So to be visible from +n the EMITTED winding's RH normal must point +n — the
        // opposite of what this method did before (it forced the RH normal to −n in BOTH branches).
        void AddQuad(System.Collections.Generic.List<int> sm,
                     Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
        {
            int b0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            for (int i = 0; i < 4; i++) { norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f)); }
            Vector3 rh = Vector3.Cross(b - a, c - a); // RH normal of triangle (a,b,c)
            if (Vector3.Dot(rh, n) > 0f)
            {
                // (a,b,c)/(a,c,d) already wind so the RH normal points +n → visible from outside.
                sm.Add(b0); sm.Add(b0 + 1); sm.Add(b0 + 2);
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 3);
            }
            else
            {
                // Reverse so the RH normal flips to +n (outward).
                sm.Add(b0); sm.Add(b0 + 2); sm.Add(b0 + 1);
                sm.Add(b0); sm.Add(b0 + 3); sm.Add(b0 + 2);
            }
        }

        // Top plateau (faces the viewer, −Z).
        AddQuad(top, new(-iw, ih, zTop), new(iw, ih, zTop), new(iw, -ih, zTop), new(-iw, -ih, zTop),
                Vector3.back);

        // Bevel ring — four 45° chamfers (normal = outward + toward viewer). Corner folds are
        // shared edges (plateau corner → outer corner), so the ring is watertight.
        const float s = 0.70710678f;
        AddQuad(ring, new(-iw, ih, zTop), new(iw, ih, zTop), new(hw, hh, zBev), new(-hw, hh, zBev),
                new Vector3(0f, s, -s));   // +Y edge
        AddQuad(ring, new(iw, ih, zTop), new(iw, -ih, zTop), new(hw, -hh, zBev), new(hw, hh, zBev),
                new Vector3(s, 0f, -s));   // +X edge
        AddQuad(ring, new(iw, -ih, zTop), new(-iw, -ih, zTop), new(-hw, -hh, zBev), new(hw, -hh, zBev),
                new Vector3(0f, -s, -s));  // −Y edge
        AddQuad(ring, new(-iw, -ih, zTop), new(-iw, ih, zTop), new(-hw, hh, zBev), new(-hw, -hh, zBev),
                new Vector3(-s, 0f, -s));  // −X edge

        // Vertical side walls (outward normals) from the bevel base back to z = 0.
        AddQuad(walls, new(-hw, hh, zBev), new(hw, hh, zBev), new(hw, hh, zBack), new(-hw, hh, zBack),
                Vector3.up);
        AddQuad(walls, new(hw, hh, zBev), new(hw, -hh, zBev), new(hw, -hh, zBack), new(hw, hh, zBack),
                Vector3.right);
        AddQuad(walls, new(hw, -hh, zBev), new(-hw, -hh, zBev), new(-hw, -hh, zBack), new(hw, -hh, zBack),
                Vector3.down);
        AddQuad(walls, new(-hw, -hh, zBev), new(-hw, hh, zBev), new(-hw, hh, zBack), new(-hw, -hh, zBack),
                Vector3.left);
        // Hidden back (kept so the solid never shows a hole if seen edge-on).
        AddQuad(walls, new(-hw, hh, zBack), new(hw, hh, zBack), new(hw, -hh, zBack), new(-hw, -hh, zBack),
                Vector3.forward);

        var mesh = new Mesh { name = "GloomhavenVR.BeveledKeycap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 3;
        mesh.SetTriangles(top, 0);
        mesh.SetTriangles(ring, 1);
        mesh.SetTriangles(walls, 2);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Vector3 OutwardNormal(Vector2 p, float hw, float hh, float r)
    {
        // Direction from the nearest corner-arc center (also correct on the straight
        // edges, where the point lies on the inflated rect around the core rect).
        float cx = Mathf.Clamp(p.x, -hw + r, hw - r);
        float cy = Mathf.Clamp(p.y, -hh + r, hh - r);
        var v = new Vector2(p.x - cx, p.y - cy);
        if (v.sqrMagnitude < 1e-12f)
            return Vector3.right;
        v.Normalize();
        return new Vector3(v.x, v.y, 0f);
    }

    // ------------------------------------------------------------------ materials --

    /// <summary>Dark front/rim colour (hidden behind the live face; the thin rounded
    /// front reads as the card's border — see CardFace inset).</summary>
    private static readonly Color EdgeColor = new(0.10f, 0.09f, 0.08f);

    /// <summary>
    /// Dark neutral for the front (hidden behind the live face) and the rim edge.
    /// SHARED across every card (see <see cref="_edgeMaterial"/>) so a later
    /// <see cref="SetSilhouette"/> re-shapes them all at once.
    /// </summary>
    internal static Material CreateEdgeMaterial()
    {
        if (_edgeMaterial == null)
        {
            _edgeMaterial = NewMaterial();
            _edgeMaterial.color = EdgeColor;
        }
        return _edgeMaterial;
    }

    /// <summary>Opaque decorative card back: procedural lattice pattern texture. Shared
    /// across every card (see <see cref="CreateEdgeMaterial"/>).</summary>
    internal static Material CreateBackMaterial()
    {
        if (_backMaterial == null)
        {
            _backMaterial = NewMaterial();
            _backMaterial.color = Color.white;
            _backMaterial.mainTexture = GetBackTexture();
        }
        return _backMaterial;
    }

    /// <summary>True once <see cref="SetSilhouette"/> has re-shaped the card body to the
    /// real card-art outline (one-shot).</summary>
    internal static bool SilhouetteApplied => _silhouetteApplied;

    /// <summary>
    /// Re-shape the 3D card body to the real card silhouette (hardware test #25). One
    /// time per session: <paramref name="alpha"/> is a card-space opacity footprint of
    /// the LIVE card art (row-major, <c>alpha[y*w + x]</c>, x → right, y → up, normalized
    /// over the card face rect) captured by <see cref="CardFace"/>. Its alpha is baked
    /// into the shared front/rim and back materials, which flip to the stock Standard
    /// shader's Cutout (alpha-test) mode — so every card's slab is clipped to the art's
    /// outline: the dark front now reads as an ORNATE border ring, the rim follows the
    /// curve, the back carries the same shape.
    ///
    /// Robust by design: returns without applying (cards stay the opaque rounded-rect)
    /// if the footprint is malformed, degenerate (mostly empty or a solid rectangle —
    /// the latter would be pointless AND is the signature of a bad capture), hollow in
    /// the centre, or if the Standard shader (hence Cutout) is unavailable. It therefore
    /// can never make a card invisible. Returns whether the silhouette was applied.
    /// </summary>
    internal static bool SetSilhouette(byte[]? alpha, int w, int h)
    {
        if (_silhouetteApplied)
            return true;
        if (alpha == null || w <= 1 || h <= 1 || alpha.Length != w * h)
        {
            VRLog.Warn("Cards", $"CardMesh.SetSilhouette: malformed footprint " +
                                $"(alpha={(alpha == null ? "null" : alpha.Length.ToString())}, {w}x{h}) — kept rounded-rect.");
            return false;
        }

        // Need the Standard shader for a proper opaque alpha-CLIP (Cutout). Without it a
        // fallback shader would only alpha-blend (unlit, sorting hazards) — not worth the
        // risk, so keep the opaque rounded-rect instead.
        Material edge = CreateEdgeMaterial();
        Material back = CreateBackMaterial();
        if (edge.shader == null || edge.shader.name != "Standard")
        {
            VRLog.Warn("Cards", $"CardMesh.SetSilhouette: Standard shader unavailable " +
                                $"(edge shader='{edge.shader?.name ?? "null"}') — kept rounded-rect (no Cutout clip).");
            return false;
        }

        // --- sanity guard: reject empty / solid / hollow-centre footprints ----------
        long opaque = 0;
        for (int i = 0; i < alpha.Length; i++)
            if (alpha[i] >= 128) opaque++;
        float frac = (float)opaque / alpha.Length;
        bool centerOpaque = CenterOpaque(alpha, w, h);
        VRLog.Info("Cards", $"CardMesh.SetSilhouette: footprint {w}x{h}, opaque frac={frac:F3}, " +
                            $"centerOpaque={centerOpaque} (accept if 0.12<frac<0.985 & centre solid).");
        if (frac < 0.12f || frac > 0.985f)
        {
            // near-empty (bad/early capture, art not loaded) or near-solid (a plain
            // rectangle — clipping would be a visual no-op). The backing is already fit to
            // the visible art (VRCard.VisibleFaceFraction), so the card shows no black
            // border either way; this only decides whether the RIM traces an ornate outline.
            VRLog.Info("Cards", $"CardMesh.SetSilhouette: frac {frac:F3} out of range — kept rounded-rect " +
                                "(border already removed by the art-fitted backing).");
            return false;
        }
        // Centre must be solid card (a valid card is opaque at its middle).
        if (!centerOpaque)
        {
            VRLog.Info("Cards", "CardMesh.SetSilhouette: centre not solid — kept rounded-rect.");
            return false;
        }

        // --- bake the footprint alpha into both materials' textures -----------------
        Texture2D backPattern = GetBackTexture();
        var edgePixels = new Color32[alpha.Length];
        var backPixels = new Color32[alpha.Length];
        var edgeRgb = (Color32)EdgeColor;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte a = alpha[i];
                edgePixels[i] = new Color32(edgeRgb.r, edgeRgb.g, edgeRgb.b, a);
                // Card back is drawn through MIRRORED UVs (see Build): mirror the alpha
                // so the back outline lines up with the front. The lattice RGB is
                // left-right symmetric, so its own mirroring is invisible.
                Color rgb = backPattern.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
                byte am = alpha[y * w + (w - 1 - x)];
                backPixels[i] = new Color32(
                    (byte)(rgb.r * 255f), (byte)(rgb.g * 255f), (byte)(rgb.b * 255f), am);
            }
        }

        ConfigureCutout(edge, MakeCutoutTexture("GloomhavenVR.CardSilhouette.Edge", edgePixels, w, h));
        ConfigureCutout(back, MakeCutoutTexture("GloomhavenVR.CardSilhouette.Back", backPixels, w, h));
        _silhouetteApplied = true;
        VRLog.Info("Cards", "CardMesh.SetSilhouette: APPLIED — shared front/rim + back materials " +
                            "flipped to alpha-clip (Cutout); the 3D card body now traces the art outline.");
        return true;
    }

    /// <summary>Is the middle 20 % box of the footprint solidly opaque (a real card)?</summary>
    private static bool CenterOpaque(byte[] alpha, int w, int h)
    {
        int x0 = (int)(w * 0.4f), x1 = (int)(w * 0.6f);
        int y0 = (int)(h * 0.4f), y1 = (int)(h * 0.6f);
        int total = 0, opaque = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                total++;
                if (alpha[y * w + x] >= 128) opaque++;
            }
        return total > 0 && opaque >= total * 0.85f;
    }

    private static Texture2D MakeCutoutTexture(string name, Color32[] pixels, int w, int h)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        return tex;
    }

    /// <summary>Flip a shared Standard material into opaque alpha-test (Cutout) mode with
    /// the given footprint texture (RGB = look, A = card outline).</summary>
    private static void ConfigureCutout(Material m, Texture2D tex)
    {
        m.mainTexture = tex;
        m.color = Color.white;
        m.SetFloat("_Mode", 1f); // Cutout
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.EnableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_Cutoff", 0.5f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
    }

    private static Material NewMaterial()
    {
        Shader? shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Sprites/Default");
        var m = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        if (m.HasProperty("_Glossiness"))
            m.SetFloat("_Glossiness", 0.25f);
        return m;
    }

    /// <summary>
    /// 128x128 card-back pattern (built once, cached): deep burgundy field, gold
    /// diamond lattice, double border — reads as "card back" at fan distance without
    /// any bundled art. Replaced wholesale when CardBacking.prefab ships in the bundle.
    /// </summary>
    private static Texture2D GetBackTexture()
    {
        if (_backTexture != null)
            return _backTexture;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true)
        {
            name = "GloomhavenVR.CardBack",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        var field = new Color(0.28f, 0.08f, 0.10f);
        var fieldDark = new Color(0.22f, 0.06f, 0.08f);
        var gold = new Color(0.78f, 0.62f, 0.28f);
        var border = new Color(0.12f, 0.10f, 0.08f);

        var pixels = new Color[size * size];
        const int cell = 16; // lattice cell in pixels
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int edge = Mathf.Min(Mathf.Min(x, size - 1 - x), Mathf.Min(y, size - 1 - y));
                Color c;
                if (edge < 4)
                {
                    c = border; // outer dark border
                }
                else if (edge < 6)
                {
                    c = gold; // thin gold inner frame
                }
                else
                {
                    // Diamond lattice: distance to the nearest diagonal grid line.
                    int lx = x % cell;
                    int ly = y % cell;
                    int d1 = Mathf.Abs(lx - ly);
                    int d2 = Mathf.Abs(lx + ly - cell);
                    bool onLine = d1 <= 1 || d2 <= 1;
                    // Subtle two-tone checker inside the lattice cells.
                    bool alt = ((x / cell) + (y / cell)) % 2 == 0;
                    c = onLine ? gold * 0.85f : (alt ? field : fieldDark);
                    c.a = 1f;
                }
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        // Keep CPU-readable: SetSilhouette samples this pattern (GetPixelBilinear) to
        // composite the card-back with the captured outline alpha.
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: false);
        _backTexture = tex;
        return tex;
    }
}
