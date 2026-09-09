using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

/// <summary>Serialized native CardEffects roles survive cloning even before Initialize ran.</summary>
internal sealed class CardAppearanceBindings
{
    private static readonly string[] RoleFields = { "_headerImage", "_topButton", "_bottomAction", "_topDefAction", "_botDefAction",
        "_topDefActionIcon", "_botDefActionIcon", "_header", "_topDefActionTxt", "_bottomDefActionTxt", "_initiativeText", "_uiFxOverlay" };
    private static readonly FieldInfo?[] Fields = Array.ConvertAll(RoleFields, name => typeof(CardEffects).GetField(name,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
    internal static readonly string[] FloatNames = { "_GreyOut", "_Flow", "_Dissolve", "_Burn", "_Flow_Offset", "_Flow_Speed",
        "_Dissolve_VerticalGradient", "_FXAnim", "_FlowSpeed", "_OffsetStrength", "_ThinHighlights", "_ThinHighlight_Min",
        "_ThinHighlight_Max", "_Flow_NoiseTiling", "_Glow" };
    internal static readonly int[] FloatIds = Array.ConvertAll(FloatNames, Shader.PropertyToID);
    internal static readonly int BurnTint = Shader.PropertyToID("_Burn_ColourTint"), FlameTint = Shader.PropertyToID("_TintColor"),
        Noise = Shader.PropertyToID("_AnimNoise_Mask"), Particle = Shader.PropertyToID("_ParticleTexture");
    internal readonly Graphic?[] Graphics = new Graphic?[CardAppearanceNode.RoleCount];
    internal readonly Transform Root;
    internal readonly Texture? BurnTexture, GhostTexture;
    internal CardAppearanceBindings(CardEffects effects)
    {
        Root = effects.transform;
        for (int i = 0; i < Fields.Length; i++) Graphics[i] = Fields[i]?.GetValue(effects) as Graphic;
        BurnTexture = effects.overlayFrameBurn; GhostTexture = effects.overlayFrameGhost;
    }
    internal CardAppearanceNode[] Capture()
    {
        var nodes = new System.Collections.Generic.List<CardAppearanceNode>();
        for (byte role = 0; role < Graphics.Length; role++)
        {
            Graphic? graphic = Graphics[role];
            if (graphic == null) continue;
            var node = new CardAppearanceNode { Role = role, Flags = (byte)((graphic.gameObject.activeSelf ? 1 : 0)
                | (graphic.enabled ? 2 : 0) | (graphic is TextMeshProUGUI tmp && tmp.enableVertexGradient ? 4 : 0)) };
            Put(node.Values, 0, graphic.color);
            Color renderer = graphic.canvasRenderer.GetColor();
            // Private widget ancestors are presentation, while the outer VR card/board fade is
            // already mirrored by its own pose/visibility contract.
            for (Transform? t = graphic.transform; t != null; t = t.parent)
            {
                CanvasGroup? group = t.GetComponent<CanvasGroup>();
                if (group != null && group.enabled) renderer.a *= group.alpha;
                if (ReferenceEquals(t, Root) || group != null && group.ignoreParentGroups) break;
            }
            Put(node.Values, 4, renderer);
            Material material = graphic.material;
            uint allowed = CardAppearanceNode.AllowedMask(role);
            for (int f = 0; f < FloatIds.Length; f++) if ((allowed & (1u << f)) != 0 && material.HasProperty(FloatIds[f]))
            { node.Mask |= 1u << f; node.Values[8 + f] = material.GetFloat(FloatIds[f]); }
            if ((allowed & (1u << 15)) != 0 && material.HasProperty(BurnTint))
            { node.Mask |= 1u << 15; Put(node.Values, 23, material.GetColor(BurnTint)); }
            if ((allowed & (1u << 16)) != 0 && material.HasProperty(FlameTint))
            { node.Mask |= 1u << 16; Put(node.Values, 27, material.GetColor(FlameTint)); }
            if ((allowed & (1u << 17)) != 0 && material.HasProperty(Noise))
            { node.Mask |= 1u << 17; Vector2 scale = material.GetTextureScale(Noise); node.Values[31] = scale.x; node.Values[32] = scale.y; }
            // The two authored flame textures have a fixed role, never a transmitted asset name.
            if (role == 11 && material.HasProperty(Particle) && ReferenceEquals(material.GetTexture(Particle), GhostTexture))
                node.Flags |= 8;
            nodes.Add(node);
        }
        return nodes.ToArray();
    }
    internal static void Put(float[] values, int offset, Color color)
    { values[offset] = color.r; values[offset + 1] = color.g; values[offset + 2] = color.b; values[offset + 3] = color.a; }
}
