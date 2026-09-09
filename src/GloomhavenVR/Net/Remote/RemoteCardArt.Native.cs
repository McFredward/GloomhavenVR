using ScenarioRuleLibrary;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

internal sealed partial class RemoteCardArt
{
    private CardAppearanceBindings? _nativeBindings;
    private readonly Dictionary<Graphic, Material> _nativeMaterials = new();
    private int _nativePlayer;
    private CPlayerActor? _nativeActor;
    private CAbilityCard? _nativeCard;
    internal void SetNativeAppearance(int playerId, CPlayerActor? actor, CAbilityCard? card)
    {
        _nativePlayer = playerId; _nativeActor = actor; _nativeCard = card;
        ApplyNativeAppearance();
    }
    internal void ApplyNativeAppearance()
    {
        if (_nativeBindings == null || _clone == null || !_clone.activeInHierarchy || _nativeActor == null || _nativeCard == null) return;
        if (RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, _nativeActor) == RevealGate.CardFaceSource.None) return;
        if (!CardAppearanceMirror.TryGet(_nativePlayer, _nativeActor, _nativeCard, out var from, out var to, out float progress)) return;
        // Validate the complete native role set before painting anything. A different prefab must
        // not receive a partial card that mixes owner output with this client's pooled defaults.
        foreach (var node in to!.Nodes)
            if (node.Role < 12 ? _nativeBindings.Graphics[node.Role] == null : !_nativeBindings.Groups.ContainsKey(node.Binding)) return;
        TakeFxLookHold();
        if (_burnRigState == BurnRig.Unbuilt) BuildBurnRig();
        foreach (var node in to.Nodes)
        {
            var old = node;
            foreach (var candidate in from!.Nodes) if (candidate.Role == node.Role && candidate.Binding == node.Binding && candidate.Mask == node.Mask) { old = candidate; break; }
            float k = old.Flags == node.Flags ? progress : 1f;
            float V(int i) => Mathf.LerpUnclamped(old.Values[i], node.Values[i], k);
            Color C(int i) => new(V(i), V(i + 1), V(i + 2), V(i + 3));
            if (node.Role >= 12)
            {
                CanvasGroup group = _nativeBindings.Groups[node.Binding];
                group.alpha = V(0); group.enabled = (node.Flags & 2) != 0; group.ignoreParentGroups = (node.Flags & 4) != 0;
                group.gameObject.SetActive((node.Flags & 1) != 0);
                continue;
            }
            Graphic graphic = _nativeBindings.Graphics[node.Role]!;
            graphic.color = C(0); graphic.canvasRenderer.SetColor(C(4));
            graphic.enabled = (node.Flags & 2) != 0;
            graphic.gameObject.SetActive((node.Flags & 1) != 0);
            if (graphic is TextMeshProUGUI text) text.enableVertexGradient = (node.Flags & 4) != 0;
            if (node.Mask == 0) continue;
            if (!_nativeMaterials.TryGetValue(graphic, out var material) || material == null)
            {
                material = new Material(graphic.material) { name = graphic.material.name + " (VR-native-card)" };
                _ownedMaterials.Add(material); _nativeMaterials[graphic] = material;
            }
            graphic.material = material;
            for (int f = 0; f < CardAppearanceBindings.FloatIds.Length; f++)
                if ((node.Mask & (1u << f)) != 0 && material.HasProperty(CardAppearanceBindings.FloatIds[f]))
                    material.SetFloat(CardAppearanceBindings.FloatIds[f], V(8 + f));
            if ((node.Mask & (1u << 15)) != 0 && material.HasProperty(CardAppearanceBindings.BurnTint)) material.SetColor(CardAppearanceBindings.BurnTint, C(23));
            if ((node.Mask & (1u << 16)) != 0 && material.HasProperty(CardAppearanceBindings.FlameTint)) material.SetColor(CardAppearanceBindings.FlameTint, C(27));
            if ((node.Mask & (1u << 17)) != 0 && material.HasProperty(CardAppearanceBindings.Noise)) material.SetTextureScale(CardAppearanceBindings.Noise, new Vector2(V(31), V(32)));
            if (node.Role == 11 && material.HasProperty(CardAppearanceBindings.Particle))
                material.SetTexture(CardAppearanceBindings.Particle, (node.Flags & 8) != 0 ? _nativeBindings.GhostTexture : _nativeBindings.BurnTexture);
        }
    }
}

/// <summary>Only presentation writes, after native layout and the ordinary mirror's state drivers.</summary>
internal sealed class RemoteCardAppearancePump : MonoBehaviour
{
    internal RemoteCardArt? Art;
    private void LateUpdate() => Art?.ApplyNativeAppearance();
}
