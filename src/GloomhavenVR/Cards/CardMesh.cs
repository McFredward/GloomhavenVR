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

    /// <summary>
    /// Render queue for a VR card that is CURRENTLY VISIBLE to the player (fanned in hand /
    /// held / in the browse view). Bug #2: the control board's action-button TMP label is
    /// force-drawn "on top" at queue 4003 with ZTest Always + ZWrite Off (ButtonCluster) so
    /// it clears the opaque board rim — which ALSO made it unconditionally overpaint any
    /// ability card in front of the board (button text bleeding through the card). A visible
    /// card pushes BOTH its opaque backing slab AND its world-space face-art graphics to THIS
    /// queue — above the button's 4003, and at/above the held-mini's 4100
    /// (<see cref="Board.FigureGrab.FigureGrabbable"/>) — via PER-INSTANCE materials, while
    /// KEEPING the shader's ZTest LEqual + ZWrite On. The card therefore wins DRAW ORDER over
    /// the ZWrite-off button widget (which never owns depth), yet still self-occludes and
    /// stays correctly hidden behind real walls / board geometry (LEqual). See
    /// <see cref="VRCard.SetRenderOnTop"/>; this mirrors the held-mini render-on-top fix.
    /// NOTE: applied per-instance on the card renderers, NEVER on the shared card materials
    /// (which <see cref="Net.RemoteHandFan"/> reuses for the opponent's hand backs).
    ///
    /// RETAINED BUT INACTIVE: the bump was reverted (it swallowed all card TEXT), so this constant
    /// has no live reader — its one code reference is inside the retained-but-uncalled
    /// <c>VRCard.ApplyRenderOnTop</c>. It stays because the 4200 &gt; 4100 &gt; 4003 ordering it
    /// records is still the design rationale for the widget queues in <c>PlayTray</c> and
    /// <c>ButtonCluster</c>. Do not "free up" the number.
    /// </summary>
    internal const int HeldCardRenderQueue = 4200;

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

        // Task #5a: planar UV from the cap's local XY, normalized 0..1 across the footprint —
        // the SAME convention CardMesh.Build uses for the card front (u = x/width + 0.5,
        // v = y/height + 0.5). Applied to EVERY vertex (top, bevel ring AND walls) so the grain
        // texture maps sensibly instead of the old single (0.5, 0.5) texel. Because the mapping
        // is continuous in XY it is watertight at the top→bevel fold (shared XY → shared UV → no
        // seam). The vertical walls share their edge's XY, so they sample a THIN grain strip along
        // that edge (a subtle stretched grain — acceptable per the task, texture set to Repeat).
        Vector2 Uv(Vector3 p) => new(p.x / width + 0.5f, p.y / height + 0.5f);

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
            norms.Add(n); norms.Add(n); norms.Add(n); norms.Add(n);
            uvs.Add(Uv(a)); uvs.Add(Uv(b)); uvs.Add(Uv(c)); uvs.Add(Uv(d));
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

    /// <summary>
    /// Segment count for the generated round board caps (user: "you can see the CORNERS in
    /// the 'round' buttons"). Unity's <see cref="PrimitiveType.Cylinder"/> has only ~20 radial
    /// sides, so a large round keycap reads as a faceted polygon; 64 sides reads perfectly
    /// smooth at the caps' on-board size while staying a trivial one-time build.
    /// </summary>
    internal const int RoundCapSegments = 64;

    private static readonly System.Collections.Generic.Dictionary<(int, int, int), Mesh> _roundCapCache = new();

    /// <summary>
    /// Cached, SHARED high-segment round-cap disc (see <see cref="BuildRoundCap"/>). Keyed on
    /// (diameter, thickness, segments) quantised to 0.1 mm so identical caps — every rest disc,
    /// and Confirm/Undo when their per-board shape is Round — reuse ONE mesh instead of
    /// rebuilding per button. Never rebuilt per frame (called once at each button's build).
    /// </summary>
    internal static Mesh GetRoundCap(float diameter, float thickness, int segments = RoundCapSegments)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        var key = (Mathf.RoundToInt(diameter * 10000f), Mathf.RoundToInt(thickness * 10000f), segments);
        if (_roundCapCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;
        Mesh built = BuildRoundCap(diameter, thickness, segments);
        _roundCapCache[key] = built;
        return built;
    }

    /// <summary>
    /// Smooth round keycap/disc mesh (user: the 'round' board buttons showed visible CORNERS
    /// because they were Unity's ~20-sided <see cref="PrimitiveType.Cylinder"/>). A genuine
    /// <paramref name="segments"/>-sided disc — a front cap fan (viewer side, −Z), a back cap
    /// fan (+Z) and a radial side wall — authored at REAL size (<paramref name="diameter"/> ×
    /// <paramref name="thickness"/>, centred on the local origin) so the owning holder stays
    /// identity-rotated / unit-scaled, reproducing the exact placement the flattened cylinder
    /// had (the front face protrudes <paramref name="thickness"/>/2 toward the viewer).
    ///
    /// Planar XY UVs (u = x/diameter + 0.5, v = y/diameter + 0.5) — the SAME convention as the
    /// card front (<see cref="Build"/>) and the beveled keycap (<see cref="BuildBeveledKeycap"/>)
    /// — so the shared carved-grain keycap <c>_MainTex</c> (grayscale grain × the state colour)
    /// maps across the round face exactly as it does on the square caps; the side wall samples
    /// its rim XY (a thin grain strip, like the keycap walls, texture set to Repeat). One
    /// submesh (the round caps carry a single keycap material — no bevel/wall split), so the
    /// caller's <c>sharedMaterial</c> and <c>SetCapColor</c> drive it unchanged.
    /// </summary>
    internal static Mesh BuildRoundCap(float diameter, float thickness, int segments)
    {
        segments = Mathf.Clamp(segments, 12, 128);
        int seg = segments;
        float r = diameter * 0.5f;
        float h = Mathf.Max(0.0005f, thickness * 0.5f);
        float zFront = -h;  // viewer side (−Z), matching the flattened-cylinder placement
        float zBack = h;

        var verts = new System.Collections.Generic.List<Vector3>(seg * 4 + 2);
        var norms = new System.Collections.Generic.List<Vector3>(seg * 4 + 2);
        var uvs = new System.Collections.Generic.List<Vector2>(seg * 4 + 2);
        var tris = new System.Collections.Generic.List<int>(seg * 12);

        Vector2 Uv(float x, float y) => new(x / diameter + 0.5f, y / diameter + 0.5f);

        var ring = new Vector2[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = 2f * Mathf.PI * i / seg;
            ring[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        // Front ring + centre (normal −Z, viewer side).
        int frontBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
            norms.Add(Vector3.back);
            uvs.Add(Uv(ring[i].x, ring[i].y));
        }
        int frontCenter = verts.Count;
        verts.Add(new Vector3(0f, 0f, zFront)); norms.Add(Vector3.back); uvs.Add(new Vector2(0.5f, 0.5f));

        // Back ring + centre (normal +Z).
        int backBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
            norms.Add(Vector3.forward);
            uvs.Add(Uv(ring[i].x, ring[i].y));
        }
        int backCenter = verts.Count;
        verts.Add(new Vector3(0f, 0f, zBack)); norms.Add(Vector3.forward); uvs.Add(new Vector2(0.5f, 0.5f));

        // Side wall: duplicated ring verts (front+back) with hard radial-outward normals.
        int wallBase = verts.Count;
        for (int i = 0; i < seg; i++)
        {
            Vector3 outward = new Vector3(ring[i].x, ring[i].y, 0f).normalized;
            verts.Add(new Vector3(ring[i].x, ring[i].y, zFront));
            verts.Add(new Vector3(ring[i].x, ring[i].y, zBack));
            norms.Add(outward); norms.Add(outward);
            uvs.Add(Uv(ring[i].x, ring[i].y)); uvs.Add(Uv(ring[i].x, ring[i].y));
        }

        // Front cap fan (visible from −Z): centre → next → i (clockwise from −Z, per Build's
        // front face — the winding that makes the RH normal point toward the viewer).
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            tris.Add(frontCenter); tris.Add(frontBase + next); tris.Add(frontBase + i);
        }
        // Back cap fan (visible from +Z): centre → i → next.
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            tris.Add(backCenter); tris.Add(backBase + i); tris.Add(backBase + next);
        }
        // Side wall quads (outward-facing — same winding as Build's rim: a,c,b / c,d,b).
        for (int i = 0; i < seg; i++)
        {
            int next = (i + 1) % seg;
            int a = wallBase + i * 2;        // front, i
            int b = wallBase + i * 2 + 1;    // back, i
            int c = wallBase + next * 2;     // front, next
            int d = wallBase + next * 2 + 1; // back, next
            tris.Add(a); tris.Add(c); tris.Add(b);
            tris.Add(c); tris.Add(d); tris.Add(b);
        }

        var mesh = new Mesh { name = "GloomhavenVR.RoundCap" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = 1;
        mesh.SetTriangles(tris, 0);
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
            // Trilinear + aniso, not Bilinear/aniso-1 (aliasing report 2026-08, "die Linien und
            // Rahmen auf allen Karten"): having a mip chain is only half the sampling fix. With
            // FilterMode.Bilinear the GPU picks ONE mip level and snaps between levels, so a card
            // drifting in the fan pops across the mip boundary — that pop reads as crawling edges
            // in stereo; Trilinear blends the two levels instead. anisoLevel 8 is the other half:
            // cards lie nearly flat on the table and fan out at steep angles, and at grazing
            // angles an isotropic sampler picks a mip for the SHORT axis, which over-blurs along
            // one direction and still aliases along the other. Same values the mip bake gives the
            // game's own card art (CardFaceMipBake.BakedAnisoLevel) — one consistent card look.
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
        };
        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: true, makeNoLongerReadable: true);
        VRLog.Info("Cards", $"CARD TEX: '{name}' {w}x{h} RGBA32 mips {tex.mipmapCount} " +
                            $"{tex.filterMode} aniso {tex.anisoLevel} — card cutout footprint.");
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
            // See MakeCutoutTexture for the full WHY. It matters most HERE: this pattern is
            // nothing BUT lines — a 1-px gold diamond lattice and a 2-px gold inner frame at
            // 128² — which is precisely the "Linien und Rahmen" content that shimmers under a
            // mip-snapping bilinear sampler on a card lying at a grazing angle on the table.
            filterMode = FilterMode.Trilinear,
            anisoLevel = 8,
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
        VRLog.Info("Cards", $"CARD TEX: '{tex.name}' {size}x{size} RGBA32 mips {tex.mipmapCount} " +
                            $"{tex.filterMode} aniso {tex.anisoLevel} — procedural card-back " +
                            "lattice/frame (built once, shared by every card back).");
        _backTexture = tex;
        return tex;
    }
}
