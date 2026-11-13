using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>Retain the separate native flame binding after the startup log fills.
/// Red card paint is correct; this observer never changes that paint or the fire.</summary>
internal static class QuestCardFireEvidence
{
    private const int Capacity = 4, ObservationLimit = 128, RowLimit = 900;
    private static readonly Dictionary<int, float> Next = new();
    private static readonly Dictionary<int, string> Rows = new();
    private static readonly Queue<int> Order = new();
    private static int _observations;
    internal static string[] Snapshot => new List<string>(Rows.Values).ToArray();

    internal static void Observe(CardEffects? fx, string transition)
    {
        if (!QuestStandalonePlatform.DebugLogging || fx == null || _observations >= ObservationLimit) return;
        int key = fx.GetInstanceID();
        float now = Time.unscaledTime;
        if (transition == "sample" && Next.TryGetValue(key, out float next) && now < next) return;
        Image? flame = fx._uiFxOverlay;
        Material? source = flame != null ? flame.material : null;
        // Idle hand cards reach the existing sample pump too. Do not consume the
        // entire bounded lane before the player actually burns their first card.
        if (!fx.HasEffect(CardEffects.FXTask.BurnCard) && !fx.HasEffect(CardEffects.FXTask.LostMode))
        {
            // DiscardMode also animates this same Image, with a blue ghost tint.
            // Do not spend the burn lane on ordinary discarded cards. A direct
            // native settled burn can lack toggled effect flags, so retain its
            // positive animation and warm tint without writing either value.
            if (source == null || !(Read(source, "_FXAnim") > 0f) || !source.HasProperty("_TintColor")) return;
            Color tint = source.GetColor("_TintColor");
            if (!(tint.r > tint.g && tint.r > tint.b && tint.a > 0f)) return;
        }
        if (!Rows.ContainsKey(key))
        {
            if (Order.Count >= Capacity)
            {
                int retired = Order.Dequeue();
                Rows.Remove(retired);
                Next.Remove(retired);
            }
            Order.Enqueue(key);
        }
        Next[key] = now + .25f;
        _observations++;
        try
        {
            CanvasRenderer? renderer = flame != null ? flame.canvasRenderer : null;
            Material? drawn = renderer != null && renderer.materialCount > 0 ? renderer.GetMaterial() : null;
            Texture? expected = fx.overlayFrameBurn;
            string row = $"cardFire f={Time.frameCount} fx={key} event={transition} initialized={fx.initialized} "
                + $"burn={fx.HasEffect(CardEffects.FXTask.BurnCard)} lost={fx.HasEffect(CardEffects.FXTask.LostMode)} "
                + $"active={flame != null && flame.isActiveAndEnabled} cull={renderer?.cull} "
                + $"graphic={flame?.color} rendered={renderer?.GetColor()} canvas={flame?.canvas?.renderMode} "
                + $"expected={expected?.name} base[{Describe(source, expected)}] draw[{Describe(drawn, expected)}]";
            Rows[key] = row.Length > RowLimit ? row.Substring(0, RowLimit) : row;
        }
        catch { /* Passive evidence cannot interrupt a native burn callback. */ }
    }

    private static string Describe(Material? material, Texture? expected)
    {
        if (material == null) return "none";
        Texture? texture = material.HasProperty("_ParticleTexture") ? material.GetTexture("_ParticleTexture") : null;
        return $"id={material.GetInstanceID()} shader={material.shader?.name} supported={material.shader?.isSupported} "
            + $"queue={material.renderQueue} anim={Read(material, "_FXAnim"):F3} glow={Read(material, "_Glow"):F2} "
            + $"tint={(material.HasProperty("_TintColor") ? material.GetColor("_TintColor") : Color.clear)} "
            + $"particle={texture?.name} expected={expected != null && (texture == expected || QuestCardFireMask.Matches(texture, expected))}";
    }
    private static float Read(Material material, string property) => material.HasProperty(property)
        ? material.GetFloat(property) : float.NaN;
}
