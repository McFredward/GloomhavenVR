using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine.Rendering;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Original UI/TMP programs with one additional, selective fragment depth test.
/// Shader derivatives retain the shipped native font/outline/sprite/stencil/clip semantics;
/// their exact upstream provenance is recorded beside the assets. Only this bar's cloned
/// material changes shader. Shared native fonts and every other widget stay untouched.</summary>
internal static class ActorBarDepthShaders
{
    private static readonly List<Material> SelectiveMaterials = new(128);
    private static bool _captureAvailable;

    // Only ActorBars' own derivatives participate. Before a live capture (or
    // following teardown/failure), ordinary scene depth is the safe fallback.
    internal static void Register(Material material)
    {
        SelectiveMaterials.Add(material);
        SetMode(material, _captureAvailable);
    }

    internal static void Unregister(Material material) => SelectiveMaterials.Remove(material);

    internal static void SetCaptureAvailable(bool available)
    {
        if (_captureAvailable == available) return;
        _captureAvailable = available;
        for (int i = SelectiveMaterials.Count - 1; i >= 0; i--)
        {
            Material material = SelectiveMaterials[i];
            if (material == null) SelectiveMaterials.RemoveAt(i);
            else SetMode(material, available);
        }
    }

    private static void SetMode(Material material, bool available)
    {
        int mode = (int)(available ? CompareFunction.Always : CompareFunction.LessEqual);
        material.SetInt("unity_GUIZTestMode", mode);
        if (material.HasProperty("_ZTestMode")) material.SetInt("_ZTestMode", mode);
    }

    internal static Shader? For(Shader original)
    {
        string? name = original.name switch
        {
            "UI/Default" => "GloomhavenVR/ActorBarUI",
            "TextMeshPro/Distance Field" => "GloomhavenVR/ActorBarSDF",
            "TextMeshPro/Mobile/Distance Field" => "GloomhavenVR/ActorBarMobileSDF",
            "TextMeshPro/Sprite" => "GloomhavenVR/ActorBarSprite",
            _ => null,
        };
        return name == null ? null : BundleShaders.Resolve(name, "WorldUI",
            "complete native bars can ignore walls while respecting solid VR foreground",
            "bars retain ordinary depth occlusion until the full asset bank is available");
    }
}
