using System;
using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// Guildmaster's NewAdventureMap does not provide the campaign tabletop in the place expected by
/// MapTableLegs. Add only the original slab's mesh/material presentation, fitted to this map and
/// its existing bench/barrel. Never move those native props or run a cloned gameplay controller.
/// Measurements and catalog discovery run once per map opening, not once per rendered frame.
/// </summary>
internal sealed class GuildmasterMapTable
{
    private readonly GuildmasterTableAssets _assets = new();
    private MeshRenderer? _parchment;
    private GameObject? _root;
    private bool _finished;
    private const float RimMeters = 0.12f;
    private const float PropClearanceMeters = 0.02f;
    private const float ThicknessMeters = 0.148f;
    private const float MapGapMeters = 0.006f;

    internal void Tick()
    {
        var state = MapRuleLibrary.Adventure.AdventureState.MapState;
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (state == null || state.IsCampaign || parchment == null)
        {
            if (_parchment != null) Release();
            return;
        }
        if (_parchment != parchment)
        {
            Release();
            _parchment = parchment;
        }
        if (_finished) return;
        try
        {
            if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _)) return;
            float scale = Mathf.Max(seat.Scale, 0.0001f);
            // Guildmaster already supplies GH_Map_Table as one mesh including its legs. The
            // optional slab finder deliberately rejects that shape; this does not mean the map
            // has no table. Respect its native geometry even while MR/material loading briefly
            // disables the renderer, and never load or overlay a second campaign slab.
            if (GuildmasterRoomGeometry.FindTableSupport(parchment, scale) != null)
            { _finished = true; return; }
            if (!_assets.Poll())
            {
                if (_assets.Pending) return;
                _finished = true;
                VRLog.Warn("MapRoom", "GUILDMASTER TABLE unavailable: the original campaign slab mesh/material "
                    + "was not resident or exposed by the native asset catalog; no substitute or gameplay scene was loaded. "
                    + _assets.Diagnostic);
                return;
            }
            // An actual native slab already under this map wins, including future game versions.
            if (MapTableLegs.TryFindTable(parchment, parchment.bounds, scale, out _, out _))
            { _finished = true; return; }
            Build(parchment, scale);
            _finished = true;
        }
        catch (Exception ex)
        {
            Release();
            _parchment = parchment;
            _finished = true;
            VRLog.Warn("MapRoom", $"GUILDMASTER TABLE unavailable: {ex.GetType().Name}: {ex.Message}; map input remains independent.");
        }
    }

    private void Build(MeshRenderer parchment, float scale)
    {
        Bounds map = parchment.bounds;
        var obstacles = new List<GuildmasterTableFit.Rect>();
        MeshRenderer[] renderers = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer r = renderers[i];
            if (!r.gameObject.activeInHierarchy || !IsFurniture(r.transform)) continue;
            Bounds b = r.bounds;
            // Include low furniture as well: its footprint must clear the optional table legs.
            if (b.min.y > map.max.y || b.max.y < map.max.y - 1.5f * scale) continue;
            obstacles.Add(new GuildmasterTableFit.Rect(b.min.x, b.max.x, b.min.z, b.max.z));
        }
        // MaterialLoader can temporarily disable a native prop renderer while loading its skin;
        // its mesh bounds still reserve space. Stable ordering makes the same native scene produce
        // the same fit for every peer even if Unity enumerates its renderers in another order.
        obstacles.Sort((a, b) =>
        {
            int order = a.Left.CompareTo(b.Left);
            if (order == 0) order = a.Right.CompareTo(b.Right);
            if (order == 0) order = a.Near.CompareTo(b.Near);
            return order != 0 ? order : a.Far.CompareTo(b.Far);
        });
        var footprint = new GuildmasterTableFit.Rect(map.min.x, map.max.x, map.min.z, map.max.z);
        if (!GuildmasterTableFit.TryFit(footprint, RimMeters * scale, PropClearanceMeters * scale,
            obstacles, out GuildmasterTableFit.Rect fit))
        {
            VRLog.Warn("MapRoom", "GUILDMASTER TABLE fit refused: native furniture overlaps the map footprint; "
                + "no prop was moved and no intersecting tabletop was created.");
            return;
        }
        _root = new GameObject("GloomhavenVR.GuildmasterMapTable");
        _root.SetActive(false);
        var model = new GameObject("CampaignTabletop") { layer = _assets.Layer };
        model.transform.SetParent(_root.transform, false);
        model.transform.localRotation = _assets.Rotation;
        model.transform.localScale = _assets.Scale;
        model.AddComponent<MeshFilter>().sharedMesh = _assets.Mesh;
        MeshRenderer renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = _assets.Materials!;
        // A transformed bounds calculation works even for non-readable native meshes, and does
        // not depend on a renderer having completed its first visible frame.
        Bounds source = TransformBounds(_assets.Mesh!.bounds, model.transform.localToWorldMatrix);
        Vector3 size = source.size;
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            throw new InvalidOperationException("The native slab has degenerate bounds");
        Vector3 targetSize = new(fit.Right - fit.Left, ThicknessMeters * scale, fit.Far - fit.Near);
        Vector3 ratios = new(targetSize.x / size.x, targetSize.y / size.y, targetSize.z / size.z);
        _root.transform.localScale = ratios;
        Vector3 centre = new((fit.Left + fit.Right) * 0.5f,
            map.max.y - MapGapMeters * scale - targetSize.y * 0.5f, (fit.Near + fit.Far) * 0.5f);
        _root.transform.position = centre - Vector3.Scale(source.center, ratios);
        _root.SetActive(true);
        VRLog.Info("MapRoom", $"GUILDMASTER TABLE fitted: original '{_assets.Mesh.name}', "
            + $"{targetSize.x / scale:F3} x {targetSize.y / scale:F3} x {targetSize.z / scale:F3} m; "
            + $"{obstacles.Count} native furniture bounds, {PropClearanceMeters * 1000f:F0} mm clearance. "
            + "Native furniture transforms and campaign presentation are unchanged.");
    }

    private static bool IsFurniture(Transform transform)
    {
        for (Transform? t = transform; t != null; t = t.parent)
            if (t.name.IndexOf("bench", StringComparison.OrdinalIgnoreCase) >= 0
                || t.name.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private static Bounds TransformBounds(Bounds bounds, Matrix4x4 matrix)
    {
        var result = new Bounds(matrix.MultiplyPoint3x4(bounds.min), Vector3.zero);
        for (int i = 1; i < 8; i++)
            result.Encapsulate(matrix.MultiplyPoint3x4(new Vector3(
                (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                (i & 4) == 0 ? bounds.min.z : bounds.max.z)));
        return result;
    }

    internal void Release()
    {
        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
        }
        _root = null;
        _parchment = null;
        _finished = false;
        _assets.Release();
    }
}
