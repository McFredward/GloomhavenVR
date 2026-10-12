using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Original UI/TMP programs with one additional, selective fragment depth test.
/// Shader derivatives retain the shipped native font/outline/sprite/stencil/clip semantics;
/// their exact upstream provenance is recorded beside the assets. Only this bar's cloned
/// material changes shader. Shared native fonts and every other widget stay untouched.</summary>
internal static class ActorBarDepthShaders
{
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
