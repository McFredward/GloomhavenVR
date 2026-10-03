using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Mesh-only NPC quality: the original skinned renderer, material, bones and expressions
/// remain in place. No independently lit native LOD renderer is enabled or swapped.</summary>
internal sealed class TownNpcDistanceDetail : IDisposable
{
    private readonly List<ScenarioFigureMeshBank.Record> _meshes = new();
    private readonly List<FigureSkinningBudget.Record> _skins = new();
    private readonly FigureDistanceLodPolicy _distance = new();
    private readonly Transform _root;
    private int _cap = -2;
    private bool _distanceEnabled;
    private float _reportAt;
    private int _reports;
    internal TownNpcDistanceDetail(Transform root)
    {
        _root = root;
        if (root == null) return;
        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            _skins.Add(new FigureSkinningBudget.Record { Renderer = skin });
            Mesh mesh = skin.sharedMesh;
            // Separately created eyes, facial features and solver cloth stay original.
            if (mesh == null || mesh.vertexCount < 1000 || skin.GetComponent<Cloth>() != null) continue;
            _meshes.Add(new ScenarioFigureMeshBank.Record { Renderer = skin, Original = mesh });
        }
        Prepare();
    }
    private void Prepare()
    {
        int cap = PerfConfig.TownNpcMeshDetailPercent;
        bool distance = PerfConfig.FigureDistanceLodEnabled;
        if (_cap == cap && _distanceEnabled == distance) return;
        _cap = cap; _distanceEnabled = distance;
        foreach (ScenarioFigureMeshBank.Record mesh in _meshes)
        {
            ScenarioFigureMeshBank.Prepare(mesh.Original, cap);
            if (!distance) continue;
            ScenarioFigureMeshBank.Prepare(mesh.Original, Mathf.Min(cap, 45));
            ScenarioFigureMeshBank.Prepare(mesh.Original, -1);
        }
    }
    internal void Tick()
    {
        Prepare();
        Camera head = VRCameraPolicy.AllowedHead!;
        bool restore = !VRSession.IsRunning;
        int wanted = restore ? 100 : _cap;
        if (!restore && head != null && _meshes.Count > 0)
        {
            Bounds bounds = _meshes[0].Renderer.bounds;
            foreach (ScenarioFigureMeshBank.Record mesh in _meshes)
                if (mesh.Renderer != null) bounds.Encapsulate(mesh.Renderer.bounds);
            wanted = _distance.Select(wanted, bounds, head.transform.position, false);
        }
        foreach (ScenarioFigureMeshBank.Record mesh in _meshes) mesh.Apply(wanted);
        foreach (FigureSkinningBudget.Record skin in _skins) skin.Apply(restore);
        // The scenario-body census cannot prove this map-only setting. Read the exact
        // owned renderer slots after application, never re-enumerate the scene or bake
        // geometry. A capped five-second Debug census is also present in LogOutput.log.
        if (VRLog.WantsDebug && _root != null && _reports < 48 && Time.unscaledTime >= _reportAt)
        {
            _reportAt = Time.unscaledTime + 5f; _reports++;
            int original = 0, current = 0, derivatives = 0, far = 0;
            foreach (ScenarioFigureMeshBank.Record mesh in _meshes)
            {
                Mesh? live = mesh.Current;
                if (live == null) continue;
                original += mesh.Original.vertexCount; current += live.vertexCount;
                if (!mesh.UsesDerivative) continue;
                derivatives++;
                if (live.name.EndsWith("-5", StringComparison.Ordinal)) far++;
            }
            VRLog.Info("Perf", $"NPC body detail: root='{_root.name}' cap={_cap}% "
                + $"distanceLOD={_distanceEnabled} requestedDetail={wanted} boneLimit={PerfConfig.MaximumSkinningBones}; "
                + $"verified derivatives={derivatives}/{_meshes.Count} far5={far}; "
                + $"original/current body vertices {original}/{current} "
                + $"(cached renderer readback, not draw counts; bounded report {_reports}/48).");
        }
    }
    public void Dispose()
    {
        foreach (ScenarioFigureMeshBank.Record mesh in _meshes) mesh.Restore();
        foreach (FigureSkinningBudget.Record skin in _skins) skin.Apply(true);
        _meshes.Clear(); _skins.Clear();
    }
}
