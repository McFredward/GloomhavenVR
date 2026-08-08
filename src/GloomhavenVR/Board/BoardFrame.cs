using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Board;

/// <summary>
/// ONE closed frame around the control board's OUTER CONTOUR — a picture frame around the asset,
/// with nothing whatsoever drawn inside it.
///
/// <para><b>USER RULING 2026-08-08, second round</b> ("Die Outlines sind völlig kaputt — ich habe mir
/// einen RAHMEN UM DAS ASSET vorgestellt, KEINE weiteren Outlines innerhalb des Assets"). The
/// ModBuild-82 INVERTED HULL is deleted, not demoted: it could not be repaired, because it failed in
/// two independent ways at once and only one of them was a bug.
/// <list type="number">
/// <item><b>It was the wrong shape by construction.</b> An inverted hull outlines EVERY surface of
///   every mesh it is built from — so it drew a rim around each card well, each recess, each dial
///   and each internal bevel of the board, exactly the "weitere Outlines innerhalb des Assets" the
///   ruling forbids. No width, colour or queue tweak removes interior edges from a hull: they ARE
///   the hull. A silhouette-only outline is a different object, not a tuned one.</item>
/// <item><b>Normal extrusion tears on this asset.</b> The board FBX is hard-edged and UV-split
///   almost everywhere (a bevelled, decorated prop), so its vertices are duplicated per face. Even
///   with the position-weld the old code did, a decimated 20 k-tri AI-generated mesh has enough
///   near-duplicate positions and near-degenerate slivers that the shell separates at the seams —
///   the white confetti in <c>outline.png</c>.</item>
/// </list></para>
///
/// <para><b>WHAT THIS BUILDS INSTEAD, and why the construction is exact rather than approximate.</b>
/// The board is a flat-ish prop that lives in its root's local XY plane with its thickness on Z (the
/// bundle contract, <c>unity/board-prep/prepare_playtray.py</c>: "board lying in local XY, thin axis
/// = Z, decorated TOP face toward -Z"). Its outer contour is therefore a 2-D question, and answered
/// as one:
/// <list type="number">
/// <item>project every vertex of every BOARD-ASSET mesh into board-ROOT-local space and drop the Z —
///   the plan-view point cloud of the whole asset;</item>
/// <item>take the CONVEX HULL of that cloud (<see cref="Hull"/>, monotone chain, with an
///   Akl–Toussaint octagon pre-filter so the sort sees hundreds of points instead of tens of
///   thousands);</item>
/// <item>offset that contour outward and stitch two closed rings into a band.</item>
/// </list>
/// Every interior vertex — every recess, well and fitting — is strictly INSIDE the hull and
/// contributes no geometry at all. That is not a filter that could miss something: it is the
/// definition of a hull. Interior detail cannot be drawn by this class.</para>
///
/// <para><b>IS THE REAL FOOTPRINT CONVEX? MEASURED, NOT ASSUMED.</b> Both shipped board styles were
/// parsed offline from their prepped meshes (<c>unity/board-prep/out/PlayTray_*.glb</c>) and their
/// plan-view silhouette compared against its own convex hull:
/// <list type="bullet">
/// <item>16vm268h ("bronze"), footprint 0.640 × 0.218 m: hull area 0.13822 m² vs. true filled
///   footprint 0.13249 m² — the hull overshoots by <b>4.3 %</b>; worst gap between the real boundary
///   and the hull <b>7.0 mm</b>, mean 3.8 mm. Hull: 42 vertices.</item>
/// <item>9capjqp6 ("steel"), footprint 0.640 × 0.369 m: hull 0.23367 m² vs. 0.22565 m² —
///   <b>3.6 %</b>; worst gap <b>10.4 mm</b>, mean 4.4 mm. Hull: 47 vertices.</item>
/// </list>
/// So the footprint is a rounded rectangle that is convex to within a centimetre, and the hull is
/// the exact answer along every straight edge and every corner round. The residual few millimetres
/// are shallow dips where the AI-generated corner rounding pinches slightly inward; there the frame
/// simply stands off the board by that much, which is what a frame is supposed to do. A concave
/// extraction (alpha shape / boundary-edge walk) would buy those millimetres back at the price of a
/// non-manifold edge walk on a mesh whose boundary is admittedly not manifold — the failure mode
/// that just cost a round.</para>
///
/// <para><b>ONE MESH, ONE RENDERER, ONE FRAME.</b> Every board renderer contributes to ONE point
/// cloud, which yields ONE hull, which yields ONE <see cref="Mesh"/> on ONE GameObject. The count is
/// structural, not a convention: <see cref="Build"/> has no loop that can emit a second frame, and
/// the built frame is ~190 triangles.</para>
///
/// <para><b>THE MR KEYLINE IS PART OF THE SAME MESH.</b> In mixed reality the frame needs a dark
/// border or a white rim vanishes on a white wall. That border is two extra bands in the SAME mesh
/// (submesh 0), hugging the coloured band (submesh 1) on its inner and outer edge. They share the
/// offset contours exactly — <see cref="Offset"/> is called once per radius and the result reused —
/// so the three bands are watertight neighbours that never OVERLAP. No overlap means the draw order
/// between them cannot matter and there is nothing to z-fight; the render queues below are only
/// about the rest of the transparent scene.</para>
///
/// <para><b>NO Z-FIGHTING WITH THE BOARD, BY SEPARATION IN BOTH X/Y AND Z.</b> The band starts
/// <see cref="FocusCue.OutlineGapLocal"/> OUTSIDE the silhouette, so it does not overlap the board in
/// plan view at all; and it sits at the board's frontmost measured Z minus <see cref="ProudLocal"/>,
/// so nothing of the board is ever in front of it. It writes no depth.</para>
///
/// <para><b>SCALE AND POSE, AND THE PINNED BOARD.</b> The frame is a child of the board ROOT with an
/// IDENTITY local pose, and its geometry is baked in board-ROOT-local metres. A board move, tilt or
/// user RESIZE is a write to the root's own transform (<c>PlayTray.3.Pose</c> writes
/// <c>_root.localScale</c>), which the frame inherits for free — no per-frame maths. Nothing here
/// ever writes the tray's transform: adding a child leaves the ROOT's world pose untouched, which is
/// what keeps a FIXIERT (pinned, world-frozen) board legal under the freeze sentinel in
/// <c>PlayTray.2.Watchdog</c>.</para>
///
/// <para><b>THE MESH REPORTS THE BOARD'S BOUNDS, NOT ITS OWN.</b> <c>PlayTray.MeasureBoardLocalExtents</c>
/// walks every mesh renderer under the tray and prefers <c>mesh.bounds</c>. An honest bound would
/// make the board's measured top edge — and with it the tooltip and enemy-reveal clearance — twitch
/// by the frame width every time the cue blinks on. The frame therefore reports the HULL's own
/// bounding box, which is by construction no larger than the board's. Costs a sliver of early
/// frustum culling at the screen edge.</para>
///
/// <para><b>MATERIALS ARE OWN INSTANCES.</b> Two <see cref="Material"/> instances from
/// <see cref="BoardVisual.Unlit"/> (<c>Sprites/Default</c>: unlit, Cull Off — right for a flat ring
/// that must survive being looked at from behind — alpha-blended, no depth write). The board's own
/// materials are read for nothing and written never. Note this drops the ModBuild-82 dependency on
/// the bundled <c>GloomhavenVR/Overlay</c> shader: a flat band needs no <c>_Cull Front</c>, so an old
/// bundle no longer costs the player the outline.</para>
///
/// <para>Degrades honestly: no bundled board (procedural fallback board — which is a genuine
/// RECTANGLE, so the caller's rectangle frame is its true contour, not a compromise), no readable
/// mesh, or a degenerate contour ⇒ <see cref="Build"/> returns null and the caller keeps
/// <see cref="WorldFrame"/>. Logged once with the reason.</para>
/// </summary>
internal sealed class BoardFrame
{
    /// <summary>The instantiated board prefab under a board root. Both the LOCAL board
    /// (<c>PlayTray.EnsureBuilt</c>) and a PEER's board (<c>RemoteTrayVisual.Build</c>) name their
    /// clone exactly this, which is why one builder serves both sides.</summary>
    private const string VisualChildName = "TrayVisual";

    /// <summary>The six FBX anchor empties. Everything the MOD parks on the board — cards, slot
    /// frames and highlights, the Confirm/Undo/rest keycaps — hangs under one of these. A card in a
    /// slot is not part of the board's contour and must not push the frame outward.</summary>
    private static readonly string[] AnchorNames =
        { "Slot1", "Slot2", "ShortRestToken", "LongRestToken", "ConfirmButton", "UndoButton" };

    /// <summary>Name prefix of every mod-built GameObject — a second, cheap exclusion for anything
    /// the mod adds under the visual that is not below an anchor.</summary>
    private const string ModNamePrefix = "GloomhavenVR.";

    private const string FrameName = "GloomhavenVR.BoardFrame";

    /// <summary>Draw queue of the dark MR keyline bands. Transparent range: the frame must run after
    /// the opaque scene has written depth so the world can occlude it.</summary>
    private const int KeylineQueue = 3000;

    /// <summary>Draw queue of the coloured rim band. Later than the keyline as a matter of stated
    /// order rather than necessity — the bands do not overlap (see the class doc), so this only
    /// ranks the frame against the rest of the transparent scene.</summary>
    private const int RimQueue = 3002;

    /// <summary>How far in FRONT of the board's frontmost vertex the frame plane sits, in
    /// board-local metres (+Z points AWAY under the module convention, so this is subtracted).
    /// Small: the frame is a frame, not a floating halo — and it is already separated from the board
    /// in plan view, so this only guarantees that a raised lip can never occlude it at a tilt.</summary>
    private const float ProudLocal = 0.002f;

    /// <summary>
    /// How far in front of the CARD PLANE the measured front face is still allowed to pull the frame
    /// (board-local metres). The bundle contract puts the decorated face at board-local z ≈ 0 — all
    /// six FBX anchors are authored at z = 0 and everything the mod parks on the board sits a few
    /// millimetres proud of it (<c>PlayTray.3.Pose</c>'s mounts are at z −0.004) — so a lip standing
    /// more than 2 cm proud is not a lip, it is a mis-imported or mis-oriented asset. Clamping keeps
    /// the frame IN THE BOARD'S PLANE instead of floating it a board-depth toward the player: the
    /// bronze board is 0.30 m deep, so an unclamped front-face read on a Z-flipped import would put
    /// the frame 30 cm in front of the board.
    /// </summary>
    private const float MaxFrontProudLocal = 0.02f;

    /// <summary>Hull edges shorter than this (board-local metres) are collapsed. A decimated mesh
    /// produces near-duplicate extreme points; the miter of a sub-millimetre edge is numerically
    /// worthless and would put a spike on the frame — the one artefact this round exists to kill.</summary>
    private const float MinEdgeLocal = 0.0008f;

    /// <summary>Floor on the miter's cosine when offsetting a contour vertex. A convex hull cannot
    /// have a reflex vertex, but it CAN have a very sharp one if the cloud has a spike; without a
    /// floor the miter length would run away. 0.25 caps the outward step at 4× the offset.</summary>
    private const float MinMiterCos = 0.25f;

    /// <summary>Board-local metres past the AUTHORED plate that a vertex may sit and still count
    /// toward the contour. Mirrors <c>PlayTray</c>'s own <c>BoardExtentSanityMargin</c> and exists
    /// for the same reason: one stray vertex must never drag the frame metres into the scene. Wide
    /// enough that the tallest shipped board (steel, half-height 0.184 m vs. the authored 0.16 m)
    /// clears it comfortably.</summary>
    private const float FootprintSanityMargin = 0.35f;

    /// <summary>Least vertices a usable contour may have.</summary>
    private const int MinContourVertices = 3;

    private readonly Vector2[] _contour;
    private readonly float _planeZ;
    private readonly Bounds _bounds;
    private readonly GameObject _go;
    private readonly MeshFilter _filter;
    private readonly MeshRenderer _renderer;
    private readonly Material _rimMaterial;
    private readonly Material _keylineMaterial;
    private readonly Mesh _mesh;

    /// <summary>Band radii the mesh is currently baked at, in board-local microns; keyline < 0 means
    /// "no keyline band" (outside MR). <see cref="int.MinValue"/> = never baked.</summary>
    private int _bakedGap = int.MinValue;
    private int _bakedRim = int.MinValue;
    private int _bakedKeyline = int.MinValue;
    private bool _keylineSubmesh;
    private bool _shown;

    private static bool _loggedBuilt;
    private static bool _loggedFallback;

    private BoardFrame(Vector2[] contour, float planeZ, Bounds bounds, GameObject go,
                       MeshFilter filter, MeshRenderer renderer, Material rim, Material keyline,
                       Mesh mesh)
    {
        _contour = contour;
        _planeZ = planeZ;
        _bounds = bounds;
        _go = go;
        _filter = filter;
        _renderer = renderer;
        _rimMaterial = rim;
        _keylineMaterial = keyline;
        _mesh = mesh;
    }

    // -------------------------------------------------------------------------------- building --

    /// <summary>
    /// Build the frame for the board asset under <paramref name="boardRoot"/> (the tray root
    /// locally, the remote board root for a peer). Null when there is no board ASSET to trace — the
    /// procedural fallback board, a peer still on the flat fallback, or a bundle whose meshes are not
    /// Read/Write enabled. The caller then keeps <see cref="WorldFrame"/>.
    /// </summary>
    internal static BoardFrame? Build(Transform? boardRoot, string label)
    {
        if (boardRoot == null)
            return null;
        Transform? visual = boardRoot.Find(VisualChildName);
        if (visual == null)
            return null; // procedural / flat fallback board — a rectangle IS its true contour

        var cloud = new List<Vector2>(4096);
        float frontZ = float.PositiveInfinity;
        int layer = boardRoot.gameObject.layer;
        int skippedUnreadable = 0;
        int meshes = 0;
        float xLimit = PlayTray.BoardHalfWidthLocal + FootprintSanityMargin;
        float yLimit = PlayTray.BoardTopLocalY + FootprintSanityMargin;
        Matrix4x4 worldToBoard = boardRoot.worldToLocalMatrix;

        foreach (MeshFilter mf in visual.GetComponentsInChildren<MeshFilter>(includeInactive: true))
        {
            if (mf == null || mf.GetComponent<MeshRenderer>() == null)
                continue;
            if (mf.gameObject.name.StartsWith(ModNamePrefix, System.StringComparison.Ordinal))
                continue;
            if (UnderAnchor(mf.transform, visual))
                continue;
            Mesh? mesh = mf.sharedMesh;
            if (mesh == null)
                continue;
            if (!mesh.isReadable)
            {
                skippedUnreadable++;
                continue;
            }

            Matrix4x4 toBoard = worldToBoard * mf.transform.localToWorldMatrix;
            Vector3[] verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 p = toBoard.MultiplyPoint3x4(verts[i]);
                // One absurd vertex must never define the contour (PlayTray's own extent rule).
                if (!(Mathf.Abs(p.x) <= xLimit) || !(Mathf.Abs(p.y) <= yLimit))
                    continue;
                cloud.Add(new Vector2(p.x, p.y));
                if (p.z < frontZ)
                    frontZ = p.z;
            }
            layer = mf.gameObject.layer; // ride the board's own (mod) layer, as its renderers do
            meshes++;
        }

        if (cloud.Count < MinContourVertices)
        {
            Fallback(label, skippedUnreadable > 0
                ? $"{skippedUnreadable} board mesh(es) are not Read/Write enabled, so their vertices "
                  + "cannot be read to trace a contour (re-import the FBX with isReadable)"
                : "the board asset exposes no mesh renderer of its own");
            return null;
        }

        Vector2[]? contour = Simplify(Hull(cloud));
        if (contour == null)
        {
            Fallback(label, $"the board's {cloud.Count} projected vertices collapse to a degenerate "
                            + "contour (fewer than three distinct hull points) — there is no "
                            + "footprint to frame");
            return null;
        }

        // The frame's REPORTED bounds are the hull's, never the band's — see the class doc.
        var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < contour.Length; i++)
        {
            min = Vector2.Min(min, contour[i]);
            max = Vector2.Max(max, contour[i]);
        }
        // Sit on the board's own front face, but never further out than a real lip could be — see
        // MaxFrontProudLocal. Also never BEHIND the card plane, so a board whose frontmost vertex is
        // its back body cannot bury the frame inside the asset.
        float planeZ = Mathf.Clamp(frontZ, -MaxFrontProudLocal, 0f) - ProudLocal;
        var bounds = new Bounds(
            new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, planeZ),
            new Vector3(max.x - min.x, max.y - min.y, 0.001f));

        var go = new GameObject(FrameName);
        Transform t = go.transform;
        // IDENTITY local pose under the board ROOT: the frame inherits the board's live pose, tilt
        // and user scale with no per-frame maths, and dies with the board. The ROOT's own transform
        // is never written — that is what keeps a pinned board's freeze sentinel quiet.
        t.SetParent(boardRoot, worldPositionStays: false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        go.layer = layer;

        var mesh2 = new Mesh { name = "GloomhavenVR.BoardFrameRing" };
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh2;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        Material rim = BoardVisual.Unlit(Color.white);
        rim.renderQueue = RimQueue;
        Material keyline = BoardVisual.Unlit(Color.white);
        keyline.renderQueue = KeylineQueue;
        mr.sharedMaterial = rim;
        go.SetActive(false);

        var frame = new BoardFrame(contour, planeZ, bounds, go, filter, mr, rim, keyline, mesh2);

        if (!_loggedBuilt)
        {
            _loggedBuilt = true;
            VRLog.Info("Board", $"Focus frame: ONE closed contour frame built for '{label}' — "
                                + $"{contour.Length} hull vertices from {meshes} board mesh(es), "
                                + $"{cloud.Count} projected points, board-local front face z "
                                + $"{frontZ:0.####} → frame plane z {planeZ:0.####}, "
                                + $"footprint {(max.x - min.x):0.###} × {(max.y - min.y):0.###} m. "
                                + "Exactly 1 ring mesh on 1 renderer; NO geometry is emitted for any "
                                + "interior feature (every interior vertex is inside the hull by "
                                + "definition). This REPLACES the ModBuild-82 inverted hull, which "
                                + "outlined every recess and tore at the FBX's hard edges.");
        }
        return frame;
    }

    /// <summary>True when <paramref name="t"/> hangs below one of the six FBX anchors (i.e. it is
    /// mod cargo parked ON the board, not the board). Walks up to <paramref name="stop"/>, which is
    /// the prefab root, so the loop always terminates.</summary>
    private static bool UnderAnchor(Transform t, Transform stop)
    {
        for (Transform? p = t; p != null && p != stop; p = p.parent)
        {
            for (int i = 0; i < AnchorNames.Length; i++)
            {
                if (p.name == AnchorNames[i])
                    return true;
            }
        }
        return false;
    }

    // ---------------------------------------------------------------------------------- drawing --

    /// <summary>Show the frame in <paramref name="tint"/>, or hide it when null. In MR a dark
    /// keyline hugs the coloured band on both sides (see <see cref="FocusCue.OutlineKeylineTint"/>);
    /// outside MR only the coloured band is built, exactly the pre-MR look.</summary>
    internal void Apply(Color? tint)
    {
        if (_go == null)
            return;
        if (tint == null)
        {
            Show(false);
            return;
        }

        Color? keyline = FocusCue.OutlineKeylineTint();
        Bake(FocusCue.OutlineGapLocal, FocusCue.OutlineRimWidthLocal,
             keyline != null ? FocusCue.OutlineKeylineWidthLocal : 0f);
        if (keyline != null && _keylineMaterial != null)
            _keylineMaterial.color = keyline.Value;
        if (_rimMaterial != null)
            _rimMaterial.color = tint.Value;
        Show(true);
    }

    internal void Destroy()
    {
        if (_go != null)
            Object.Destroy(_go);
        if (_mesh != null)
            Object.Destroy(_mesh);
        // OWN instances — nothing else wears them.
        if (_rimMaterial != null)
            Object.Destroy(_rimMaterial);
        if (_keylineMaterial != null)
            Object.Destroy(_keylineMaterial);
    }

    private void Show(bool shown)
    {
        if (_shown == shown)
            return;
        _shown = shown;
        if (_go != null)
            _go.SetActive(shown);
    }

    // ----------------------------------------------------------------------------- ring geometry --

    /// <summary>
    /// Rebuild the ring for the given band radii (board-local metres): the coloured band spans
    /// <paramref name="gap"/> … <paramref name="gap"/> + <paramref name="rim"/> outside the contour,
    /// and — when <paramref name="keyline"/> &gt; 0 — a dark band of that width hugs it on each side.
    /// A no-op unless a width actually changed, which outside the MR toggle it never does.
    /// </summary>
    private void Bake(float gap, float rim, float keyline)
    {
        int gapU = Mathf.RoundToInt(gap * 1e6f);
        int rimU = Mathf.RoundToInt(rim * 1e6f);
        int keyU = keyline > 0f ? Mathf.RoundToInt(keyline * 1e6f) : -1;
        if (gapU == _bakedGap && rimU == _bakedRim && keyU == _bakedKeyline)
            return;
        _bakedGap = gapU;
        _bakedRim = rimU;
        _bakedKeyline = keyU;

        int n = _contour.Length;
        bool wantKeyline = keyU > 0;
        // Each contour radius is offset ONCE and the result shared by the bands that meet on it, so
        // neighbouring bands are watertight and can never overlap.
        Vector2[] cIn = Offset(_contour, gap);
        Vector2[] cOut = Offset(_contour, gap + rim);
        // The inner keyline may eat into the gap but never past the board's own contour: a keyline
        // wider than the gap would be drawn OVER the board's edge, which is interior art.
        Vector2[]? kIn = wantKeyline ? Offset(_contour, Mathf.Max(gap - keyline, 0f)) : null;
        Vector2[]? kOut = wantKeyline ? Offset(_contour, gap + rim + keyline) : null;

        int rings = wantKeyline ? 3 : 1;
        int vertexCount = rings * 2 * n;
        var verts = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var colors = new Color32[vertexCount];
        var white = new Color32(255, 255, 255, 255);
        // The unlit shader multiplies by the vertex COLOUR channel; a mesh without one feeds it an
        // undefined value (black on some drivers), which is how an "invisible" frame happens.
        var faceNormal = new Vector3(0f, 0f, -1f); // toward the viewer (+Z points away)

        int v = 0;
        int rimBase = Fill(verts, normals, colors, ref v, cIn, cOut, _planeZ, faceNormal, white);
        int keyInnerBase = 0, keyOuterBase = 0;
        if (wantKeyline)
        {
            keyInnerBase = Fill(verts, normals, colors, ref v, kIn!, cIn, _planeZ, faceNormal, white);
            keyOuterBase = Fill(verts, normals, colors, ref v, cOut, kOut!, _planeZ, faceNormal, white);
        }

        _mesh.Clear();
        _mesh.vertices = verts;
        _mesh.normals = normals;
        _mesh.colors32 = colors;
        if (wantKeyline)
        {
            _mesh.subMeshCount = 2;
            int[] key = new int[n * 6 * 2];
            Stitch(key, 0, keyInnerBase, n);
            Stitch(key, n * 6, keyOuterBase, n);
            _mesh.SetTriangles(key, 0, calculateBounds: false);
            _mesh.SetTriangles(Ring(rimBase, n), 1, calculateBounds: false);
        }
        else
        {
            _mesh.subMeshCount = 1;
            _mesh.SetTriangles(Ring(rimBase, n), 0, calculateBounds: false);
        }
        // The board's bounds, never the band's — see the class doc (measured board extents must not
        // twitch by the frame width every time the cue blinks on).
        _mesh.bounds = _bounds;

        if (_keylineSubmesh != wantKeyline)
        {
            _keylineSubmesh = wantKeyline;
            // Submesh 0 is the keyline, submesh 1 the rim, so the material array follows that order.
            _renderer.sharedMaterials = wantKeyline
                ? new[] { _keylineMaterial, _rimMaterial }
                : new[] { _rimMaterial };
        }
        if (_filter.sharedMesh != _mesh)
            _filter.sharedMesh = _mesh;
    }

    /// <summary>Append the 2 × <c>n</c> vertices of one band (inner ring then outer ring) and return
    /// the index the band starts at.</summary>
    private static int Fill(Vector3[] verts, Vector3[] normals, Color32[] colors, ref int v,
                            Vector2[] inner, Vector2[] outer, float z, Vector3 normal, Color32 color)
    {
        int start = v;
        int n = inner.Length;
        for (int i = 0; i < n; i++)
        {
            verts[v] = new Vector3(inner[i].x, inner[i].y, z);
            normals[v] = normal;
            colors[v] = color;
            v++;
        }
        for (int i = 0; i < n; i++)
        {
            verts[v] = new Vector3(outer[i].x, outer[i].y, z);
            normals[v] = normal;
            colors[v] = color;
            v++;
        }
        return start;
    }

    private static int[] Ring(int baseIndex, int n)
    {
        var tris = new int[n * 6];
        Stitch(tris, 0, baseIndex, n);
        return tris;
    }

    /// <summary>Stitch a closed band: quad i joins inner[i], outer[i], outer[i+1], inner[i+1].
    /// Wound so the face points at the viewer on the -Z side; the material is Cull Off, so this is
    /// correctness rather than necessity.</summary>
    private static void Stitch(int[] tris, int at, int baseIndex, int n)
    {
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            int a = baseIndex + i;          // inner i
            int b = baseIndex + j;          // inner i+1
            int c = baseIndex + n + i;      // outer i
            int d = baseIndex + n + j;      // outer i+1
            tris[at++] = a; tris[at++] = d; tris[at++] = c;
            tris[at++] = a; tris[at++] = b; tris[at++] = d;
        }
    }

    // --------------------------------------------------------------------------- contour maths --

    /// <summary>
    /// The convex hull of <paramref name="cloud"/> in counter-clockwise order (Andrew's monotone
    /// chain), preceded by an Akl–Toussaint octagon reject that typically discards well over 90 % of
    /// the points before the sort — a board mesh contributes ~22 000 of them and this runs on the
    /// frame a board is built on.
    /// </summary>
    private static List<Vector2> Hull(List<Vector2> cloud)
    {
        List<Vector2> pts = Octagon(cloud);
        pts.Sort(static (a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var hull = new List<Vector2>(pts.Count + 1);
        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;
            for (int k = 0; k < pts.Count; k++)
            {
                Vector2 p = pts[pass == 0 ? k : pts.Count - 1 - k];
                while (hull.Count - start >= 2
                       && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1); // the pass' last point opens the next pass
        }
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) =>
        (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

    /// <summary>Akl–Toussaint: keep only the points OUTSIDE the octagon spanned by the extremes of
    /// x, y, x+y and x−y. Everything discarded is strictly interior and provably cannot be on the
    /// hull.</summary>
    private static List<Vector2> Octagon(List<Vector2> cloud)
    {
        Vector2 xMin = cloud[0], xMax = cloud[0], yMin = cloud[0], yMax = cloud[0];
        Vector2 sMin = cloud[0], sMax = cloud[0], dMin = cloud[0], dMax = cloud[0];
        for (int i = 1; i < cloud.Count; i++)
        {
            Vector2 p = cloud[i];
            if (p.x < xMin.x) xMin = p;
            if (p.x > xMax.x) xMax = p;
            if (p.y < yMin.y) yMin = p;
            if (p.y > yMax.y) yMax = p;
            if (p.x + p.y < sMin.x + sMin.y) sMin = p;
            if (p.x + p.y > sMax.x + sMax.y) sMax = p;
            if (p.x - p.y < dMin.x - dMin.y) dMin = p;
            if (p.x - p.y > dMax.x - dMax.y) dMax = p;
        }

        // The eight extremes in CCW order — right, up-right, up, up-left, left, down-left, down,
        // down-right — i.e. the point each of those eight directions maximises. Repeated or
        // collinear entries (a cloud with no diagonal extreme of its own) collapse harmlessly: the
        // inside test below simply never rejects across a degenerate edge.
        Vector2[] oct = { xMax, sMax, yMax, dMin, xMin, sMin, yMin, dMax };

        var kept = new List<Vector2>(256);
        for (int i = 0; i < cloud.Count; i++)
        {
            if (!InsideConvex(oct, cloud[i]))
                kept.Add(cloud[i]);
        }
        for (int i = 0; i < oct.Length; i++)
            kept.Add(oct[i]); // the octagon's own corners ARE hull points — never drop them
        return kept;
    }

    /// <summary>True when <paramref name="p"/> is inside the CCW convex polygon
    /// <paramref name="poly"/>. Degenerate (repeated) polygon vertices simply never reject.</summary>
    private static bool InsideConvex(Vector2[] poly, Vector2 p)
    {
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i];
            Vector2 b = poly[(i + 1) % poly.Length];
            if (Cross(a, b, p) < 0f)
                return false;
        }
        return true;
    }

    /// <summary>Drop hull vertices closer than <see cref="MinEdgeLocal"/> to the one before them, so
    /// no miter is computed from a sub-millimetre edge. Null when fewer than three survive.</summary>
    private static Vector2[]? Simplify(List<Vector2> hull)
    {
        if (hull.Count < MinContourVertices)
            return null;
        float minSq = MinEdgeLocal * MinEdgeLocal;
        var kept = new List<Vector2>(hull.Count);
        for (int i = 0; i < hull.Count; i++)
        {
            if (kept.Count == 0 || (hull[i] - kept[kept.Count - 1]).sqrMagnitude >= minSq)
                kept.Add(hull[i]);
        }
        while (kept.Count > MinContourVertices
               && (kept[0] - kept[kept.Count - 1]).sqrMagnitude < minSq)
        {
            kept.RemoveAt(kept.Count - 1);
        }
        return kept.Count >= MinContourVertices ? kept.ToArray() : null;
    }

    /// <summary>
    /// Push a CCW convex contour outward by <paramref name="d"/> board-local metres along each
    /// vertex' miter. Exact for the straight runs; at a corner the miter cuts the true offset's
    /// circular arc, which on a hull of forty-odd vertices turning a few degrees at a time is a
    /// sub-tenth-of-a-millimetre difference. <see cref="MinMiterCos"/> caps a pathologically sharp
    /// vertex so a spike can never grow out of the frame.
    /// </summary>
    private static Vector2[] Offset(Vector2[] contour, float d)
    {
        int n = contour.Length;
        var edgeNormal = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 e = contour[(i + 1) % n] - contour[i];
            float len = e.magnitude;
            // A zero-length edge cannot happen after Simplify; fall back to the previous normal.
            edgeNormal[i] = len > 1e-9f ? new Vector2(e.y / len, -e.x / len)
                                        : (i > 0 ? edgeNormal[i - 1] : Vector2.up);
        }

        var outContour = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector2 prev = edgeNormal[(i - 1 + n) % n];
            Vector2 next = edgeNormal[i];
            Vector2 miter = prev + next;
            float len = miter.magnitude;
            if (len < 1e-9f)
            {
                outContour[i] = contour[i] + next * d;
                continue;
            }
            miter /= len;
            float cos = Mathf.Max(Vector2.Dot(miter, next), MinMiterCos);
            outContour[i] = contour[i] + miter * (d / cos);
        }
        return outContour;
    }

    private static void Fallback(string label, string why)
    {
        if (_loggedFallback)
            return;
        _loggedFallback = true;
        VRLog.Warn("Board", $"Focus frame for '{label}' falls back to the RECTANGLE frame: {why}. "
                            + "The cue still works; it just circumscribes the board instead of "
                            + "tracing its contour.");
    }
}
