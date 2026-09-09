using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>Keep original bonus hover content on the converted owner bar.</summary>
internal static class NativeUseBarTooltipHost
{
    internal static void Restore(UIUseActiveBonus? slot)
    {
        if (!WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive || slot == null
            || slot.tooltip == null || UIManager.Instance == null) return;
        // UIUseActiveBonusTooltip.Show calls UIManager.HighlightElement, which reparents these
        // original roots onto the flat highlightHolder. That holder is not a converted VR canvas.
        // Use its paired native PRESENTATION restore, preserving active state, content and pose;
        // never replay the slot's Show/Toggle callbacks. Native Hide then finds nothing to restore.
        Restore(slot.tooltip.activeAbilityTooltip != null ? slot.tooltip.activeAbilityTooltip.gameObject : null);
        Restore(slot.tooltip.itemTooltip != null ? slot.tooltip.itemTooltip.gameObject : null);
    }

    private static void Restore(GameObject? root)
    {
        UIManager manager = UIManager.Instance;
        if (root == null || !manager.highlightedElements.ContainsKey(root)) return;
        manager.UnhighlightElement(root, unlockUI: false);
    }
}
