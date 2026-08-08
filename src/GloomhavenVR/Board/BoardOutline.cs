using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Board;

/// <summary>
/// A REAL outline of the control-board ASSET — the board's own silhouette, rounded corners,
/// bevels and fittings included — and not the rectangle the focus cue used to draw.
///
/// <para>USER RULING 2026-08-08 ("ich will, dass es wirkliche Outlines vom Asset des
/// Controlboards ist, nicht ein Rechteck"). The previous cue was four bars laid out from
/// <c>PlayTray.BoardHalfWidthLocal</c> and a hardcoded 0.5 aspect: a box that merely circumscribed
/// the board. The replacement follows the mesh.</para>
///
/// <para><b>TECHNIQUE — INVERTED HULL (scale-along-normals shell), and why not the alternatives.</b>
/// <list type="bullet">
/// <item><b>Renderer bounds</b> — explicitly rejected by the ruling: an AABB is a box with extra
///   steps, and a tilted board would make it a LOOSER box than the one we already had.</item>
/// <item><b>Stencil / screen-space edge detect</b> — needs a second camera pass or a command
///   buffer on the head camera. The mod does not own the game's render pipeline (built-in, forward,
///   one HMD camera under <c>VRCameraPolicy</c>), and a mod-injected full-screen pass in a stereo
///   VR frame is exactly the kind of thing that costs frames and breaks on the next driver. No.</item>
/// <item><b>A generated ring mesh from the footprint outline</b> — would need a 2-D silhouette
///   extraction of a 3-D asset (edge-loop walk on a boundary that is not manifold in the FBX) and
///   would still be a FLAT ring: it could not show the board's raised lip, the recesses or the
///   fittings, only its plan view.</item>
/// <item><b>Inverted hull (chosen)</b> — duplicate the board's OWN meshes, push every vertex out
///   along its (smoothed) normal, draw with <c>Cull Front</c> so only the far side of the shell
///   survives, and let the board's own depth occlude everything that overlaps it. What is left on
///   screen is exactly the silhouette of the real geometry, from any angle, for any board style,
///   with zero knowledge of what the board looks like. It is the classic outline technique for
///   precisely this reason.</item>
/// </list></para>
///
/// <para><b>WHY IT CANNOT Z-FIGHT.</b> The shell is displaced along the surface normal, so over the
/// board's face the only shell surface that survives front-culling is the far side — a full board
/// thickness plus two extrusions BEHIND the face, which the board's opaque depth kills outright.
/// The shell is never coplanar with the board anywhere, and it never writes depth
/// (<c>_ZWrite 0</c>), so it cannot disturb anything drawn after it either. The visible result is a
/// rim standing proud of the silhouette — see <see cref="FocusCue.OutlineRimExtrudeLocal"/>.</para>
///
/// <para><b>SCALE AND POSE.</b> Each shell is a child of the source renderer's own transform with an
/// IDENTITY local pose, so it inherits the board's live pose, tilt and scale for free and needs no
/// per-frame transform maths. Nothing here ever writes the tray's transform — the FIXIERT freeze
/// sentinel (<c>PlayTray.2.Watchdog</c>) watches the tray ROOT's world pose, and adding a child
/// does not touch it. The extrusion is expressed in board-ROOT-local metres and divided by each
/// renderer's own mesh→board scale, so every board style and every renderer inside it wears the
/// same rim, and the rim scales WITH a user-resized board (a rim whose weight relative to the board
/// stays constant is what "an outline of the asset" means; a constant-metric rim would swallow a
/// board the user shrank).</para>
///
/// <para><b>MATERIALS ARE NEVER SHARED WITH THE BOARD.</b> Each layer owns exactly one
/// <see cref="Material"/> INSTANCE built from the bundled <c>GloomhavenVR/Overlay</c> shader (the
/// only shader in reach that exposes <c>_Cull</c>/<c>_ZTest</c>/<c>_ZWrite</c> as properties —
/// <c>Sprites/Default</c> is <c>Cull Off</c> and would wash the board's face, and
/// <c>Shader.Find("Standard")</c> strips to it in the shipped game). The board's own materials are
/// read for nothing and written never.</para>
///
/// <para><b>MIXED REALITY.</b> In MR the outline draws a second, wider, dark KEYLINE layer under the
/// coloured rim, so the cue keeps contrast against an arbitrary real room (a white rim vanishes on a
/// white wall). Colours, opacity and both extrusions come from <see cref="FocusCue"/> — this class
/// owns geometry only.</para>
///
/// <para>Degrades honestly: no bundled board (procedural fallback / bundle not resident), no
/// readable mesh, or no Overlay shader ⇒ <see cref="Build"/> returns null and the caller keeps the
/// old <see cref="WorldFrame"/> rectangle. Logged once with the reason.</para>
/// </summary>
internal sealed class BoardOutline
{
    /// <summary>The instantiated board prefab under a board root. Both the LOCAL board
    /// (<c>PlayTray.EnsureBuilt</c>) and a PEER's board (<c>RemoteTrayVisual.Build</c>) name their
    /// clone exactly this, which is why one builder serves both sides.</summary>
    private const string VisualChildName = "TrayVisual";

    /// <summary>The six FBX anchor empties. Everything the MOD parks on the board — cards, slot
    /// frames and highlights, the Confirm/Undo/rest keycaps — hangs under one of these, so a
    /// renderer with one of them as an ancestor is NOT part of the board asset and must not be
    /// hulled (a card in a slot would otherwise grow its own blinking outline).</summary>
    private static readonly string[] AnchorNames =
        { "Slot1", "Slot2", "ShortRestToken", "LongRestToken", "ConfirmButton", "UndoButton" };

    /// <summary>Name prefix of every mod-built GameObject — a second, cheap exclusion for anything
    /// the mod adds under the visual that is not below an anchor.</summary>
    private const string ModNamePrefix = "GloomhavenVR.";

    private const string RimName = "GloomhavenVR.BoardOutlineRim";
    private const string KeylineName = "GloomhavenVR.BoardOutlineKeyline";

    /// <summary>Draw queue of the dark MR keyline. Transparent range: the shell must run AFTER the
    /// opaque board has written depth, or the board could not occlude it.</summary>
    private const int KeylineQueue = 3000;

    /// <summary>Draw queue of the coloured rim — strictly after the keyline, so where the two
    /// overlap the COLOUR wins and the keyline survives only as the extra band beyond it. A fixed
    /// order, never a distance tie-break (the same rule <c>BoardVisual</c>'s sub-ladder states).</summary>
    private const int RimQueue = 3002;

    /// <summary>Vertex-weld tolerance for the smoothed normals, as a fraction of the mesh's own
    /// bounds. RELATIVE on purpose: mod meshes live at wildly different authoring scales (the
    /// armature rule — hands are authored ×100), so an absolute epsilon would weld everything on
    /// one asset and nothing on the next.</summary>
    private const float WeldFraction = 1e-4f;

    /// <summary>
    /// Baked shells, keyed by (source mesh instance id, extrusion in mesh-local microns). Kept for
    /// the process lifetime and NEVER destroyed: the local board and every peer's board clone the
    /// SAME prefab meshes, so the entries are shared by construction and freeing one would blank
    /// another player's outline. Three board styles × two layers is a handful of meshes.
    /// </summary>
    private static readonly Dictionary<(int Mesh, int Microns), Mesh> ShellCache = new(8);

    /// <summary>Smoothed (position-welded) normals per source mesh — the expensive half of the
    /// bake, reused by both layers and by every re-bake at a different width.</summary>
    private static readonly Dictionary<int, Vector3[]> SmoothCache = new(4);

    private readonly Transform _visual;
    private readonly Source[] _sources;
    private readonly Layer _rim;
    private readonly Layer _keyline;

    private BoardOutline(Transform visual, Source[] sources, Layer rim, Layer keyline)
    {
        _visual = visual;
        _sources = sources;
        _rim = rim;
        _keyline = keyline;
    }

    /// <summary>One hulled renderer of the board asset.</summary>
    private readonly struct Source
    {
        internal readonly Transform Host;
        internal readonly Mesh Mesh;

        /// <summary>Uniform scale from this renderer's MESH-local units to board-ROOT-local metres.
        /// The extrusion is authored in board-local metres and divided by this, so one constant
        /// produces the same rim on every renderer of every board style.</summary>
        internal readonly float MeshToBoard;

        internal Source(Transform host, Mesh mesh, float meshToBoard)
        {
            Host = host;
            Mesh = mesh;
            MeshToBoard = meshToBoard;
        }
    }

    /// <summary>One shell pass (the coloured rim, or the MR keyline under it).</summary>
    private sealed class Layer
    {
        internal readonly List<MeshFilter> Filters = new(4);
        internal Material Material = null!;
        internal GameObject[] Objects = System.Array.Empty<GameObject>();

        /// <summary>Width the meshes are currently baked at, in mesh-local microns; -1 = never
        /// baked. A change (the MR toggle) re-points the filters at the cached mesh for the new
        /// width — no per-frame work, and each width is baked at most once per mesh.</summary>
        internal int Microns = -1;
        internal bool Shown;
    }

    private static bool _loggedBuilt;
    private static bool _loggedFallback;

    /// <summary>
    /// Build the outline for the board asset under <paramref name="boardRoot"/> (the tray root
    /// locally, the remote board root for a peer). Null when there is no board ASSET to outline —
    /// the procedural fallback board, a peer still on the flat fallback, a bundle whose meshes are
    /// not Read/Write enabled, or a bundle without the Overlay shader. The caller then keeps the
    /// old rectangle, which is worse but honest.
    /// </summary>
    internal static BoardOutline? Build(Transform? boardRoot, string label)
    {
        if (boardRoot == null)
            return null;
        Transform? visual = boardRoot.Find(VisualChildName);
        if (visual == null)
            return null; // procedural / flat fallback board — no asset exists to outline

        Shader? shader = PlayTray.OverlayShader();
        if (shader == null)
        {
            Fallback(label, "the bundled 'GloomhavenVR/Overlay' shader is not loaded (old bundle) — "
                            + "no other reachable shader exposes _Cull, and a two-sided shell would "
                            + "paint over the board's own face");
            return null;
        }

        var sources = new List<Source>(4);
        Matrix4x4 worldToBoard = boardRoot.worldToLocalMatrix;
        int skippedUnreadable = 0;
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
            float scale = (toBoard.MultiplyVector(Vector3.right).magnitude
                           + toBoard.MultiplyVector(Vector3.up).magnitude
                           + toBoard.MultiplyVector(Vector3.forward).magnitude) / 3f;
            if (!(scale > 1e-6f))
                continue;
            sources.Add(new Source(mf.transform, mesh, scale));
        }

        if (sources.Count == 0)
        {
            Fallback(label, skippedUnreadable > 0
                ? $"{skippedUnreadable} board mesh(es) are not Read/Write enabled, so their vertices "
                  + "cannot be read to build a shell (re-import the FBX with isReadable)"
                : "the board asset exposes no mesh renderer of its own");
            return null;
        }

        Source[] set = sources.ToArray();
        var outline = new BoardOutline(
            visual, set,
            NewLayer(set, shader, RimName, RimQueue),
            NewLayer(set, shader, KeylineName, KeylineQueue));

        if (!_loggedBuilt)
        {
            _loggedBuilt = true;
            VRLog.Info("Board", $"Focus outline: REAL asset silhouette built for '{label}' — "
                                + $"{set.Length} inverted-hull shell(s) cloned from the control "
                                + "board's own meshes (Cull Front, ZTest LEqual, no ZWrite, own "
                                + "material instances). The cue now follows the board's rounded "
                                + "corners and fittings instead of a 0.64×0.32 rectangle.");
        }
        return outline;
    }

    /// <summary>Show the outline in <paramref name="tint"/>, or hide it when null. In MR a dark
    /// keyline layer is shown underneath (see <see cref="FocusCue.OutlineKeylineTint"/>); outside
    /// MR only the coloured rim draws, exactly the pre-MR look.</summary>
    internal void Apply(Color? tint)
    {
        if (_visual == null)
            return;
        if (tint == null)
        {
            Show(_rim, false);
            Show(_keyline, false);
            return;
        }

        Color? keyline = FocusCue.OutlineKeylineTint();
        // The keyline is baked/shown FIRST so that on the frame MR turns on both layers appear
        // together — a rim that arrives one frame before its backing reads as a flicker.
        if (keyline != null)
        {
            Bake(_keyline, FocusCue.OutlineKeylineExtrudeLocal);
            _keyline.Material.color = keyline.Value;
            Show(_keyline, true);
        }
        else
        {
            Show(_keyline, false);
        }

        Bake(_rim, FocusCue.OutlineRimExtrudeLocal);
        _rim.Material.color = tint.Value;
        Show(_rim, true);
    }

    internal void Destroy()
    {
        DestroyLayer(_rim);
        DestroyLayer(_keyline);
    }

    // ------------------------------------------------------------------------------- building --

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

    private static Layer NewLayer(Source[] sources, Shader shader, string name, int queue)
    {
        var layer = new Layer { Material = NewMaterial(shader, queue) };
        var objects = new GameObject[sources.Length];
        for (int i = 0; i < sources.Length; i++)
        {
            var go = new GameObject(name);
            Transform t = go.transform;
            // Identity local pose under the SOURCE renderer: the shell inherits the board's live
            // pose/tilt/scale with no per-frame maths, and dies with the board.
            t.SetParent(sources[i].Host, worldPositionStays: false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            go.layer = sources[i].Host.gameObject.layer; // ride the board's own (mod) layer
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = layer.Material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            go.SetActive(false);
            layer.Filters.Add(go.GetComponent<MeshFilter>());
            objects[i] = go;
        }
        layer.Objects = objects;
        return layer;
    }

    /// <summary>The one material of a layer: an OWN instance, front-face culled (that is what turns
    /// a fattened copy into an outline), depth-tested against the board but depth-WRITING nothing.</summary>
    private static Material NewMaterial(Shader shader, int queue)
    {
        var m = new Material(shader) { color = Color.white };
        if (m.HasProperty("_Cull"))
            m.SetFloat("_Cull", (float)CullMode.Front);
        if (m.HasProperty("_ZTest"))
            m.SetFloat("_ZTest", (float)CompareFunction.LessEqual);
        if (m.HasProperty("_ZWrite"))
            m.SetFloat("_ZWrite", 0f);
        if (m.HasProperty("_SrcBlend"))
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend"))
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.renderQueue = queue;
        return m;
    }

    private void Show(Layer layer, bool shown)
    {
        if (layer.Shown == shown)
            return;
        layer.Shown = shown;
        for (int i = 0; i < layer.Objects.Length; i++)
        {
            GameObject go = layer.Objects[i];
            if (go != null)
                go.SetActive(shown);
        }
    }

    private static void DestroyLayer(Layer layer)
    {
        for (int i = 0; i < layer.Objects.Length; i++)
        {
            if (layer.Objects[i] != null)
                Object.Destroy(layer.Objects[i]);
        }
        layer.Objects = System.Array.Empty<GameObject>();
        layer.Filters.Clear();
        if (layer.Material != null)
            Object.Destroy(layer.Material); // an OWN instance — nothing else wears it
    }

    /// <summary>Point a layer's filters at the shells for <paramref name="extrudeBoardLocal"/>
    /// board-local metres. A no-op unless the wanted width changed (i.e. unless the MR toggle
    /// moved), and every width is baked at most once per source mesh.</summary>
    private void Bake(Layer layer, float extrudeBoardLocal)
    {
        int microns = Mathf.RoundToInt(extrudeBoardLocal * 1e6f);
        if (layer.Microns == microns)
            return;
        layer.Microns = microns;
        for (int i = 0; i < _sources.Length; i++)
        {
            MeshFilter mf = layer.Filters[i];
            if (mf == null)
                continue;
            mf.sharedMesh = ShellFor(_sources[i].Mesh, extrudeBoardLocal / _sources[i].MeshToBoard);
        }
    }

    // ---------------------------------------------------------------------------- shell baking --

    /// <summary>The cached inverted-hull shell of <paramref name="src"/>, pushed out
    /// <paramref name="extrudeMeshLocal"/> MESH-local units along the smoothed normals.</summary>
    private static Mesh? ShellFor(Mesh src, float extrudeMeshLocal)
    {
        int microns = Mathf.RoundToInt(extrudeMeshLocal * 1e6f);
        if (microns <= 0)
            return null;
        var key = (src.GetInstanceID(), microns);
        if (ShellCache.TryGetValue(key, out Mesh cached) && cached != null)
            return cached;

        Vector3[]? normals = Smoothed(src);
        if (normals == null)
            return null;
        Vector3[] verts = src.vertices;
        float push = microns * 1e-6f;
        var shell = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            shell[i] = verts[i] + normals[i] * push;

        var mesh = new Mesh { name = $"GloomhavenVR.BoardOutlineShell[{src.name}:{microns}]" };
        if (verts.Length > 65000)
            mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = shell;
        // One submesh: the shell carries no texture and no lighting, so the source's material
        // split is irrelevant — only its silhouette matters.
        mesh.triangles = src.triangles;
        // The Overlay shader multiplies by the vertex COLOR channel. A mesh without one feeds it
        // an undefined value (black on some drivers), which is how an "invisible" outline happens.
        var white = new Color32[shell.Length];
        for (int i = 0; i < white.Length; i++)
            white[i] = new Color32(255, 255, 255, 255);
        mesh.colors32 = white;
        // Keep the SOURCE bounds. PlayTray.MeasureBoardLocalExtents walks every mesh renderer under
        // the tray and prefers mesh.bounds; an honestly grown bound would make the board's measured
        // top edge (and therefore the tooltip/enemy-reveal clearance) twitch by the rim width every
        // time the cue blinks on. Costs at most a sliver of early frustum culling at the screen edge.
        mesh.bounds = src.bounds;
        mesh.UploadMeshData(markNoLongerReadable: true);
        ShellCache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Per-vertex normals AVERAGED over every vertex at the same position. Required, not cosmetic:
    /// an FBX with hard edges (which a beveled board is, everywhere) splits its vertices so each
    /// side of an edge carries its own normal — extruding along those tears the shell open at every
    /// crease and the "outline" comes out as a set of disconnected flaps. Welding by position first
    /// is the standard fix. Cached per mesh; the weld tolerance is relative to the mesh's own bounds
    /// so it survives any authoring scale.
    /// </summary>
    private static Vector3[]? Smoothed(Mesh src)
    {
        int id = src.GetInstanceID();
        if (SmoothCache.TryGetValue(id, out Vector3[] hit))
            return hit;

        Vector3[] verts = src.vertices;
        Vector3[] normals = src.normals;
        if (verts.Length == 0 || normals.Length != verts.Length)
        {
            SmoothCache[id] = null!;
            return null;
        }

        Vector3 size = src.bounds.size;
        float span = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        float q = Mathf.Max(span * WeldFraction, 1e-9f);
        var acc = new Dictionary<Vector3Int, Vector3>(verts.Length);
        var cell = new Vector3Int[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 v = verts[i];
            var c = new Vector3Int(Mathf.RoundToInt(v.x / q), Mathf.RoundToInt(v.y / q),
                                   Mathf.RoundToInt(v.z / q));
            cell[i] = c;
            acc[c] = acc.TryGetValue(c, out Vector3 sum) ? sum + normals[i] : normals[i];
        }

        var outNormals = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 n = acc[cell[i]];
            // A vertex whose welded normals cancel (a zero-thickness fin) keeps its own normal:
            // a zero push would pin that vertex to the surface and pinch the shell.
            outNormals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : normals[i].normalized;
        }
        SmoothCache[id] = outNormals;
        return outNormals;
    }

    private static void Fallback(string label, string why)
    {
        if (_loggedFallback)
            return;
        _loggedFallback = true;
        VRLog.Warn("Board", $"Focus outline for '{label}' falls back to the RECTANGLE frame: {why}. "
                            + "The cue still works; it just circumscribes the board instead of "
                            + "tracing it.");
    }
}
