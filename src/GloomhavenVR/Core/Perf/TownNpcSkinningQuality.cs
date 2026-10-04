using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Optional renderer influence quality, independent of NPC geometry.
/// Build616 removes all prepared NPC mesh substitutions after the hardware report of
/// holes and broken surfaces. Town residents keep their exact shipped full-detail mesh
/// at every distance; this helper never opens the scenario derivative index or banks.</summary>
internal sealed class TownNpcSkinningQuality : IDisposable
{
    private readonly List<FigureSkinningBudget.Record> _skins = new();
    internal TownNpcSkinningQuality(Transform root)
    {
        if (root == null) return;
        foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            _skins.Add(new FigureSkinningBudget.Record { Renderer = skin });
    }
    internal void Tick()
    {
        foreach (FigureSkinningBudget.Record skin in _skins) skin.Apply(!VRSession.IsRunning);
    }
    public void Dispose()
    {
        foreach (FigureSkinningBudget.Record skin in _skins) skin.Apply(true);
        _skins.Clear();
    }
}
