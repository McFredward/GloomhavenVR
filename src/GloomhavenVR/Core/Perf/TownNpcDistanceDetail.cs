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
    private int _cap = -2;
    private bool _distanceEnabled;
    internal TownNpcDistanceDetail(Transform root)
    {
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
    }
    public void Dispose()
    {
        foreach (ScenarioFigureMeshBank.Record mesh in _meshes) mesh.Restore();
        foreach (FigureSkinningBudget.Record skin in _skins) skin.Apply(true);
        _meshes.Clear(); _skins.Clear();
    }
}
