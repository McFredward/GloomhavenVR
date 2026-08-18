using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The campaign-map parchment, acquired and made VISIBLE TO A FORWARD CAMERA for the LIFETIME of
/// the 3D map room.
///
/// <para>WHY AN OVERRIDE IS NEEDED AT ALL — this is not an optimisation, it is the only reason
/// anything is on screen. The game's parchment material has NO forward pass (stated in the class
/// doc of <c>WorldUI/FlatScreenStereo.3.Map.cs</c>: "every OTHER object on those layers uses a
/// deferred material with no forward pass, so it stays invisible in our forward camera — only the
/// parchment (whose materials we override with MapUnlit for exactly our render) draws"), and the
/// mod's head camera is forward (<c>VRRigDriver.HeadCamera.cs</c>,
/// <c>_camera.renderingPath = RenderingPath.Forward</c>). Head camera + no override = a BLACK
/// map. The bundled <c>GloomhavenVR/MapUnlit</c> shader is the forward pass the parchment lacks.
/// </para>
///
/// <para>WHAT IS DIFFERENT FROM THE FLAT PATH. <c>FlatScreenStereo</c> applies the same override
/// for exactly one camera's render and restores it in the same frame
/// (<c>ApplyWorldMapOverride</c>/<c>RestoreWorldMapOverride</c>). Here it is HELD for as long as
/// the map room is up, because the renderer must be visible to the head camera on every frame and
/// in both eye passes. That is a strictly weaker demand than the flat path already makes: the
/// game tolerating a swap-and-restore every frame is evidence that nothing re-drives
/// <c>sharedMaterials</c> per frame. The two owners are mutually exclusive by construction —
/// <see cref="MapRoomDriver"/> raises <c>FlatScreenStereo.MapRoomOwnsParchment</c> before it
/// acquires anything, and the flat path stands its own map render down on that flag.</para>
///
/// <para>TEARDOWN IS THE NON-NEGOTIABLE PART (the rule that file states about itself: "never leave
/// an override material on a game renderer"). <see cref="Release"/> restores the exact material
/// array captured at apply time and destroys the override instances; it runs on mode exit, on a
/// world↔city switch, on renderer death and from the rig teardown, and it is idempotent.</para>
/// </summary>
internal sealed class MapParchment
{
    private const string Scope = "MapRoom";

    /// <summary>Bundle shader that supplies the forward pass the game's parchment lacks.</summary>
    private const string MapUnlitShaderName = "GloomhavenVR/MapUnlit";

    /// <summary>
    /// UV channel fed to MapUnlit. TexCoord0 — the parchment mesh carries a real one and each
    /// quadrant submesh's UV already runs 0..1 over its own 4096² albedo, so scale=(1,1) and
    /// offset=(0,0) are correct. Hardware-established on the flat path
    /// (<c>FlatScreenStereo.3.Map.BuildOverrideMaterials</c>); copied here as a value, not
    /// re-derived, because re-deriving it is exactly how the object-space-position UV guess came
    /// back the first time.
    /// </summary>
    private const float MapUnlitUvChannel = 0f;

    private MeshRenderer? _renderer;
    private GameObject? _mapGo;
    private bool _isCity;
    private Material[]? _overrides;
    private Material[]? _originals;
    private bool _applied;
    private bool _shaderMissingLogged;

    /// <summary>The live parchment renderer while one is acquired, else null.</summary>
    internal MeshRenderer? Renderer => _renderer != null ? _renderer : null;

    /// <summary>The active map GameObject the renderer was found under (world or city map).</summary>
    internal GameObject? MapGo => _mapGo != null ? _mapGo : null;

    /// <summary>True when the acquired map is the CITY map rather than the world map.</summary>
    internal bool IsCity => _isCity;

    /// <summary>True while the MapUnlit override is sitting on the game renderer.</summary>
    internal bool Applied => _applied;

    /// <summary>World AABB of the parchment, or a degenerate box while nothing is acquired.</summary>
    internal Bounds WorldBounds => _renderer != null ? _renderer.bounds : default;

    /// <summary>
    /// Acquire (or re-acquire) the parchment renderer for the currently ACTIVE map and report
    /// whether its world bounds are USABLE — i.e. whether a seat can be solved from them.
    ///
    /// <para>IT TOUCHES NO MATERIALS, and that is what lets the mode predicate call it every frame
    /// BEFORE the room has engaged. The switch can be on and the map GameObject active while the
    /// parchment mesh is still being built; if the rig flavour changed on the map GameObject alone
    /// it would tear the menu rig down into that gap, and a torn-down rig is an UNTRACKED HEADSET.
    /// So "is there a map" and "can it be stood on" are one question, answered here.</para>
    ///
    /// <para>The renderer lookup is a subtree walk, so it runs only when the ACTIVE MAP CHANGES —
    /// a world↔city switch does not change scene, so nothing else in the mod would notice it.</para>
    /// </summary>
    internal bool Acquire(global::MapChoreographer? choreo)
    {
        GameObject? active = ResolveActiveMapGo(choreo, out bool isCity);
        if (active == null)
        {
            Release("no active MapChoreographer map");
            return false;
        }
        if (!ReferenceEquals(active, _mapGo) || _renderer == null)
        {
            Release(ReferenceEquals(active, _mapGo)
                ? "the parchment renderer went away"
                : $"active map switched to {(isCity ? "CITY" : "WORLD")} ('{active.name}')");
            _mapGo = active;
            _isCity = isCity;
            _renderer = FindParchmentRenderer(active);
            if (_renderer == null)
                return false;
        }
        Vector3 size = _renderer.bounds.size;
        return Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) >= MapRoomSeat.MinUsableExtent;
    }

    /// <summary>
    /// <see cref="Acquire"/>, and then hold the MapUnlit override on the renderer. Safe to call
    /// every frame: a no-op once applied to the same renderer.
    /// </summary>
    /// <returns>True when a usable renderer is acquired — with or without the override, so a
    /// missing bundle shader still yields usable bounds for the seat rather than failing the mode.</returns>
    internal bool Ensure(global::MapChoreographer? choreo)
    {
        if (!Acquire(choreo))
            return false;
        if (!_applied)
            Apply();
        return true;
    }

    /// <summary>
    /// Restore the game's own materials and drop ours. Idempotent; the ONLY exit for the override,
    /// so there is exactly one place that can leave a renderer dirty and it does not.
    /// </summary>
    internal void Release(string reason)
    {
        if (_applied)
        {
            _applied = false;
            if (_renderer != null && _originals != null)
                _renderer.sharedMaterials = _originals;
            VRLog.Info(Scope, $"MAP ROOM parchment override released ({reason}) — the game renderer " +
                              "holds its own materials again.");
        }
        if (_overrides != null)
        {
            for (int i = 0; i < _overrides.Length; i++)
            {
                if (_overrides[i] != null)
                    Object.Destroy(_overrides[i]);
            }
            _overrides = null;
        }
        _originals = null;
        _renderer = null;
        _mapGo = null;
    }

    // ---- acquisition ------------------------------------------------------------------------

    /// <summary>
    /// The ACTIVE campaign map GameObject: <c>MapChoreographer</c> toggles worldMap/cityMap with
    /// SetActive, so "active in hierarchy" is the whole test. This is the same POSITIVE signal
    /// <c>FlatScreenStereo.TickFastMapEngage</c> trusts to decide the map is open — deliberately
    /// the same one, so the mode predicate and the acquisition can never disagree.
    /// </summary>
    internal static GameObject? ResolveActiveMapGo(global::MapChoreographer? choreo, out bool isCity)
    {
        isCity = false;
        if (choreo == null)
            return null;
        GameObject? world = choreo.worldMap;
        GameObject? city = choreo.cityMap;
        if (city != null && city.activeInHierarchy)
        {
            isCity = true;
            return city;
        }
        if (world != null && world.activeInHierarchy)
            return world;
        return null;
    }

    /// <summary>
    /// The renderer carrying the quadrant materials (name has a numeric <c>0N</c> suffix, any
    /// prefix — <c>GH_CampaignMap_0N_MAT</c> for the world map, <c>GH_City*_0N</c> for the city
    /// map), falling back to the map GameObject's own MeshRenderer, which is what the decompiled
    /// <c>MapChoreographer</c> itself uses.
    /// </summary>
    private static MeshRenderer? FindParchmentRenderer(GameObject mapGo)
    {
        MeshRenderer[] rends = mapGo.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
        for (int i = 0; i < rends.Length; i++)
        {
            Material[] mats = rends[i].sharedMaterials;
            for (int j = 0; j < mats.Length; j++)
            {
                if (mats[j] != null && QuadrantIndexFromName(mats[j].name) >= 0)
                    return rends[i];
            }
        }
        return mapGo.GetComponent<MeshRenderer>();
    }

    /// <summary>Quadrant index (0..3) parsed from a material name's "0N" suffix (N=1..4), or -1.</summary>
    internal static int QuadrantIndexFromName(string n)
    {
        if (string.IsNullOrEmpty(n))
            return -1;
        for (int k = 0; k + 1 < n.Length; k++)
        {
            if (n[k] == '0' && n[k + 1] >= '1' && n[k + 1] <= '4')
                return n[k + 1] - '1';
        }
        return -1;
    }

    // ---- the override -----------------------------------------------------------------------

    private void Apply()
    {
        if (_renderer == null)
            return;
        Shader? sh = BundleShaders.Resolve(
            MapUnlitShaderName, Scope,
            "MAP ROOM: the campaign map is drawn forward and textured for the head camera.",
            "MAP ROOM: the campaign map has no forward pass and no replacement — it will render BLACK.");
        if (sh == null)
        {
            if (!_shaderMissingLogged)
            {
                _shaderMissingLogged = true;
                VRLog.Warn(Scope, "MAP ROOM: the bundled GloomhavenVR/MapUnlit shader is not loaded, so the " +
                                  "parchment keeps its deferred material and renders BLACK in the forward head " +
                                  "camera. The room still stands up (scale/seat come from the mesh bounds, not " +
                                  "from the material), so the log below still measures the geometry.");
            }
            return;
        }

        Material[] current = _renderer.sharedMaterials;
        var overrides = new Material[current.Length];
        int withTex = 0;
        var sb = new System.Text.StringBuilder();
        Texture?[] quads = GatherQuadrantTextures(current);
        for (int i = 0; i < current.Length; i++)
        {
            Material? o = current[i];
            Texture? tex = null;
            string prop = "NONE";
            int q = -1;
            if (o != null)
            {
                q = QuadrantIndexFromName(o.name);
                if (o.HasProperty("_Alb") && (tex = o.GetTexture("_Alb")) != null)
                    prop = "_Alb";
                else if (o.HasProperty("_MainTex") && (tex = o.GetTexture("_MainTex")) != null)
                    prop = "_MainTex";
                else if ((tex = o.mainTexture) != null)
                    prop = "mainTexture";
            }
            if (q >= 0 && q < 4 && quads[q] != null)
                tex = quads[q];

            var m = new Material(sh) { name = "GloomhavenVR.MapRoom.MapUnlit." + i };
            m.SetFloat("_UvChannel", MapUnlitUvChannel);
            m.SetVector("_UvScale", new Vector4(1f, 1f, 0f, 0f));
            m.SetVector("_UvOffset", new Vector4(0f, 0f, 0f, 0f));
            m.SetFloat("_Bright", 1f);
            if (tex != null)
            {
                m.SetTexture("_MainTex", tex);
                withTex++;
            }
            overrides[i] = m;
            sb.Append($"\n  submesh[{i}] '{(o != null ? o.name : "<null>")}' quadrant " +
                      $"{(q >= 0 ? (q + 1).ToString("00") : "??")}: albedo from {prop}" +
                      (tex != null ? $" = '{tex.name}' {tex.width}x{tex.height}" : " (NONE — this submesh will be untextured)"));
        }

        if (withTex == 0)
        {
            for (int i = 0; i < overrides.Length; i++)
            {
                if (overrides[i] != null)
                    Object.Destroy(overrides[i]);
            }
            VRLog.Warn(Scope, "MAP ROOM: no parchment submesh exposed an albedo texture (_Alb / _MainTex / " +
                              "mainTexture all null) — the override is NOT applied (an untextured MapUnlit is " +
                              "no better than the black deferred material and would be harder to diagnose). " +
                              "Retrying while the map is open." + sb);
            return;
        }

        _originals = current;
        _overrides = overrides;
        _renderer.sharedMaterials = overrides;
        _applied = true;
        VRLog.Info(Scope, $"MAP ROOM parchment override APPLIED and HELD on '{_renderer.name}' " +
                          $"({(_isCity ? "CITY" : "WORLD")} map, {current.Length} submesh(es), {withTex} textured, " +
                          $"UvChannel={MapUnlitUvChannel:F0} scale(1,1) offset(0,0)). The game's own materials are " +
                          "captured verbatim and restored on every exit path (mode off, map switch, renderer death, " +
                          "rig teardown)." + sb);
    }

    /// <summary>
    /// The four <c>GH_CampaignMap_0N</c> quadrant albedos, indexed by the number in the MATERIAL
    /// name ([0]=01 … [3]=04). A submesh whose own material has lost its albedo can then still be
    /// textured from its quadrant sibling.
    /// </summary>
    private static Texture?[] GatherQuadrantTextures(Material[] mats)
    {
        var quads = new Texture?[4];
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i];
            if (m == null)
                continue;
            int idx = QuadrantIndexFromName(m.name);
            if (idx < 0 || idx >= 4 || quads[idx] != null)
                continue;
            Texture? tex = (m.HasProperty("_Alb") ? m.GetTexture("_Alb") : null)
                           ?? (m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null)
                           ?? m.mainTexture;
            if (tex != null)
                quads[idx] = tex;
        }
        return quads;
    }
}
