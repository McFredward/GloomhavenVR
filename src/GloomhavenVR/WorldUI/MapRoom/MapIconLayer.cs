using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The location icons and the party token, re-drawn for the HEAD camera.
///
/// <para>WHY THEY ARE NOT SIMPLY THERE. Every map location's icon is a
/// <c>ThreeEyedGames.Decalicious</c> deferred decal — it draws in the deferred G-buffer pass and
/// contributes NOTHING to a forward camera, which is what the mod's head camera is. So a map room
/// with a working parchment and no icon layer is a blank sheet of paper. The flat path already
/// solved this by re-drawing each decal as a textured quad into a <c>CommandBuffer</c>
/// (<c>FlatScreenStereo.3.Map.DrawMapIcons</c>); the work here is to hang the same idea on the
/// head camera instead of a private capture camera.</para>
///
/// <para>TWO DELIBERATE DIFFERENCES FROM THE FLAT PATH, both because the target camera is now the
/// one the player looks through:</para>
/// <list type="number">
///   <item>NO DEPTH CLEAR. The flat path clears depth at <c>AfterForwardAlpha</c> so a cloud
///   particle can never reject an icon. On the head camera that same clear would throw away the
///   depth of everything the player is standing in — hands, panels, the parchment itself — for
///   whatever draws afterwards. It is also unnecessary: the wind particles do not write depth, and
///   the icons are lifted clear of the parchment, so ordinary <c>ZTest LEqual</c> already puts them
///   where they belong. Render QUEUE (4000, above the particles' ~3000) still decides the
///   particle overlap exactly as it does in flat.</item>
///   <item>THE QUAD FOOTPRINT COMES FROM THE DECAL TRANSFORM, not from the renderer's world AABB.
///   Decalicious draws a decal as a unit cube through <c>transform.localToWorldMatrix</c>, so the
///   AUTHORED footprint is <c>lossyScale.x</c> × <c>lossyScale.z</c>. Using the world AABB and then
///   re-applying the decal's 90° yaw transposes non-square icons — that is the "vereinzelt Icons
///   gequetscht" report, and its fix is copied here as a value rather than re-derived.</item>
/// </list>
///
/// <para>TEARDOWN: <see cref="Release"/> detaches the buffer from whatever camera holds it and
/// destroys the mesh and material. Nothing is ever left on a game object — the decals themselves
/// are only READ.</para>
/// </summary>
internal sealed class MapIconLayer
{
    private const string Scope = "MapRoom";

    /// <summary>Lift above the parchment's top face, world units. The parchment mesh is ~0.13
    /// thick and carries animated foliage on its surface; this clears both without being visible
    /// as a float at map scale.</summary>
    private const float IconLiftWorld = 0.10f;

    /// <summary>Frames between decal re-scans. Same cadence as the flat path's icon cache —
    /// locations are destroyed and respawned by <c>MapChoreographer.InitMap</c>, so a cached set
    /// must be short-lived, and ~0.2 s is far below the time any map change takes to be seen.</summary>
    private const int RescanIntervalFrames = 15;

    private static readonly int IconMainTex = Shader.PropertyToID("_MainTex");
    private static readonly int IconColor = Shader.PropertyToID("_Color");

    private static System.Type? _decalType;
    private static System.Reflection.PropertyInfo? _decalCurMatProp;
    private bool _decalTypeMissing;

    private CommandBuffer? _cmd;
    private Camera? _cam;
    private Mesh? _quad;
    private Material? _mat;

    private readonly List<Component> _decals = new(64);
    private readonly List<Renderer?> _decalRenderers = new(64);
    private readonly List<Renderer> _tokenRenderers = new(8);
    private readonly List<MaterialPropertyBlock> _mpbPool = new(64);
    private int _scanFrame = int.MinValue;
    private int _lastDrawn = -1;
    private bool _reported;

    /// <summary>Icons drawn on the most recent rebuild (diagnostics).</summary>
    internal int DrawnCount { get; private set; }

    /// <summary>Party-token submesh draws on the most recent rebuild (diagnostics).</summary>
    internal int TokenDrawCount { get; private set; }

    /// <summary>
    /// Rebuild the icon command buffer for this frame and make sure it is attached to
    /// <paramref name="head"/>. Called every frame while the map room is up: the icons move with
    /// the map's own state, and the buffer is cheap to refill (a few dozen <c>DrawMesh</c> calls).
    /// </summary>
    internal void Tick(Camera? head, MeshRenderer? parchment, global::MapChoreographer? choreo)
    {
        if (head == null || parchment == null || choreo == null)
        {
            Release("no head camera / no parchment / no choreographer");
            return;
        }
        if (!EnsureResources())
            return;

        if (!ReferenceEquals(_cam, head))
        {
            Detach();
            head.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _cmd);
            _cam = head;
            VRLog.Info(Scope, $"MAP ROOM icon layer attached to '{head.name}' at AfterForwardAlpha " +
                              "(no depth clear — see the class doc; the flat path's clear would wipe the " +
                              "player's own scene depth).");
        }

        _cmd!.Clear();
        Rescan(choreo);

        float planeY = parchment.bounds.max.y + IconLiftWorld;
        int drawn = 0;
        for (int i = 0; i < _decals.Count; i++)
        {
            Component d = _decals[i];
            if (d == null || !d.gameObject.activeInHierarchy)
                continue;
            if (_decalCurMatProp?.GetValue(d) is not Material cm)
                continue;
            Texture? tex = cm.HasProperty(IconMainTex) ? cm.GetTexture(IconMainTex) : cm.mainTexture;
            if (tex == null || _decalRenderers[i] == null)
                continue;

            Transform dt = d.transform;
            Vector3 ds = dt.lossyScale;
            Vector3 dp = dt.position;
            var pos = new Vector3(dp.x, planeY, dp.z);
            var scale = new Vector3(Mathf.Max(Mathf.Abs(ds.x), 0.01f), 1f, Mathf.Max(Mathf.Abs(ds.z), 0.01f));
            var rot = Quaternion.Euler(0f, dt.eulerAngles.y, 0f);
            MaterialPropertyBlock mpb = RentMpb(drawn);
            mpb.SetTexture(IconMainTex, tex);
            mpb.SetColor(IconColor, Color.white);
            _cmd.DrawMesh(_quad, Matrix4x4.TRS(pos, rot, scale), _mat, 0, 0, mpb);
            drawn++;
        }

        int tokenDraws = 0;
        for (int i = 0; i < _tokenRenderers.Count; i++)
        {
            Renderer tr = _tokenRenderers[i];
            if (tr == null || !tr.enabled || !tr.gameObject.activeInHierarchy)
                continue;
            Material[] mats = tr.sharedMaterials;
            for (int sm = 0; sm < mats.Length; sm++)
            {
                if (mats[sm] == null)
                    continue;
                _cmd.DrawRenderer(tr, mats[sm], sm, -1); // -1 = the material's own valid passes
                tokenDraws++;
            }
        }

        DrawnCount = drawn;
        TokenDrawCount = tokenDraws;
        if (drawn != _lastDrawn && !_reported)
        {
            _lastDrawn = drawn;
            if (drawn > 0)
            {
                _reported = true;
                VRLog.Info(Scope, $"MAP ROOM icons: {drawn} location icon(s) + {tokenDraws} party-token " +
                                  $"submesh draw(s) queued for the head camera at y={planeY:F2} " +
                                  $"({IconLiftWorld:F2} above the parchment top). Footprint = decal lossyScale.xz " +
                                  "at the decal's own yaw.");
            }
        }
    }

    /// <summary>Detach the buffer and destroy everything this layer owns. Idempotent.</summary>
    internal void Release(string reason)
    {
        bool had = _cam != null;
        Detach();
        if (_cmd != null)
        {
            _cmd.Release();
            _cmd = null;
        }
        if (_quad != null)
        {
            Object.Destroy(_quad);
            _quad = null;
        }
        if (_mat != null)
        {
            Object.Destroy(_mat);
            _mat = null;
        }
        _decals.Clear();
        _decalRenderers.Clear();
        _tokenRenderers.Clear();
        _mpbPool.Clear();
        _scanFrame = int.MinValue;
        _lastDrawn = -1;
        _reported = false;
        DrawnCount = 0;
        TokenDrawCount = 0;
        if (had)
            VRLog.Info(Scope, $"MAP ROOM icon layer released ({reason}) — command buffer detached, mesh and " +
                              "material destroyed; the game's decals were only ever read.");
    }

    private void Detach()
    {
        if (_cam != null && _cmd != null)
            _cam.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _cmd);
        _cam = null;
    }

    private bool EnsureResources()
    {
        if (_decalTypeMissing)
            return false;
        if (_decalType == null)
        {
            // The Decal type lives in an unreferenced assembly (ThreeEyedGames Decalicious) —
            // reached by name, exactly as the flat path does.
            _decalType = HarmonyLib.AccessTools.TypeByName("Decal");
            if (_decalType == null)
            {
                _decalTypeMissing = true;
                VRLog.Warn(Scope, "MAP ROOM icons: the Decalicious 'Decal' type is not present — location " +
                                  "icons cannot be re-drawn and the map will show the parchment only.");
                return false;
            }
            _decalCurMatProp = _decalType.GetProperty("CurrentMaterial");
        }
        _cmd ??= new CommandBuffer { name = "GloomhavenVR.MapRoom.Icons" };
        if (_quad == null)
        {
            _quad = new Mesh { name = "GloomhavenVR.MapRoom.IconQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateBounds();
        }
        if (_mat == null)
        {
            Shader? sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (sh == null)
            {
                _decalTypeMissing = true;
                VRLog.Warn(Scope, "MAP ROOM icons: no Unlit/Transparent or Sprites/Default shader — icons skipped.");
                return false;
            }
            _mat = new Material(sh) { name = "GloomhavenVR.MapRoom.IconMat" };
            // Above the wind/cloud particles (~3000), reproducing the flat layering where the map's
            // icons are canvas markers painted last and the wind drifts underneath them.
            _mat.renderQueue = 4000;
        }
        return true;
    }

    private void Rescan(global::MapChoreographer choreo)
    {
        bool due = _scanFrame == int.MinValue || Time.frameCount - _scanFrame >= RescanIntervalFrames;
        if (!due)
        {
            for (int i = 0; i < _decals.Count && !due; i++)
                due = _decals[i] == null; // a destroyed decal forces an early rescan
        }
        if (!due)
            return;
        _scanFrame = Time.frameCount;
        _decals.Clear();
        _decalRenderers.Clear();
        Collect(choreo.m_ScenariosParent);
        Collect(choreo.m_VillagesParent);
        if (_decals.Count == 0 && _decalType != null)
        {
            // Safety net for a save/version that parents its map icons elsewhere — the same
            // scene-wide sweep the flat path keeps for that case.
            UnityEngine.Object[] all = Object.FindObjectsOfType(_decalType);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c || !c.gameObject.activeInHierarchy)
                    continue;
                _decals.Add(c);
                _decalRenderers.Add(c.GetComponent<Renderer>());
            }
        }
        _tokenRenderers.Clear();
        PartyToken? token = choreo.m_PartyToken;
        if (token != null)
            token.GetComponentsInChildren(includeInactive: false, _tokenRenderers);
    }

    private void Collect(GameObject? rootGo)
    {
        if (rootGo == null || _decalType == null)
            return;
        Component[] found = rootGo.GetComponentsInChildren(_decalType, includeInactive: false);
        for (int i = 0; i < found.Length; i++)
        {
            _decals.Add(found[i]);
            _decalRenderers.Add(found[i] != null ? found[i].GetComponent<Renderer>() : null);
        }
    }

    private MaterialPropertyBlock RentMpb(int slot)
    {
        while (_mpbPool.Count <= slot)
            _mpbPool.Add(new MaterialPropertyBlock());
        return _mpbPool[slot];
    }
}
