using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>The same private numerical-mask lease for an original hosted item.</summary>
internal sealed class QuestHostedItemFireMask : MonoBehaviour
{
    // This is the native reference already used by RemoteCardArt's item rig,
    // not a graphic-name search or a guess among unrelated card Images.
    private static readonly FieldInfo? FlameField = typeof(ItemCardEffects).GetField("fgFx",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private readonly QuestCardFireMaskBindings _masks = new();
    private ItemCardEffects? _source;
    private Image? _flame;

    internal static void Attach(ItemCardUI card, ItemCardEffects source)
    {
        if (!QuestStandalonePlatform.Enabled) return;
        var owner = card.GetComponent<QuestHostedItemFireMask>() ?? card.gameObject.AddComponent<QuestHostedItemFireMask>();
        if (owner._source == source) return;
        owner._masks.ReleaseAll();
        owner._source = source;
        owner._flame = FlameField?.GetValue(source) as Image;
    }

    private void LateUpdate()
    {
        Canvas? root = _flame != null && _flame.canvas != null ? _flame.canvas.rootCanvas : null;
        if (!QuestStandalonePlatform.Enabled || _source == null || _flame == null
            || !_flame.isActiveAndEnabled || _source.cardBounds.x <= 0 || _source.cardBounds.y <= 0
            || root == null || root.renderMode != RenderMode.WorldSpace)
        { _masks.ReleaseAll(); return; }
        // The native timeline chooses its texture first. Only its exact burn
        // mask gets the numerical view; a spent/ghost handover keeps its own
        // texture, and disabling/pooling the widget restores/releases ours.
        if (_masks.Bind(_flame.material, _source.overlayFrameBurn)) _flame.SetMaterialDirty();
    }

    private void OnDisable() => _masks.ReleaseAll();
    private void OnDestroy() => _masks.ReleaseAll();
}
