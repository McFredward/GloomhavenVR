using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Keep the game's own enhancement-area buttons and their animations in
/// front of the physical card, without drawing a second pooled ability-card print.
/// Native input and selection still belong to UIEnhancementButtonHighlight.</summary>
internal sealed class TownServiceNativeEnhancementCardMask : MonoBehaviour
{
    private readonly List<Graphic> _art = new();
    private readonly List<bool> _wasEnabled = new();
    private bool _masked;

    internal void Mask()
    {
        if (_masked)
        {
            // Native card refreshes may enable artwork again while the same physical
            // card remains offered; keep the printed copy hidden for that frame.
            foreach (Graphic graphic in _art) if (graphic != null) graphic.enabled = false;
            return;
        }
        var graphics = new List<Graphic>();
        GetComponentsInChildren(true, graphics);
        foreach (Graphic graphic in graphics)
        {
            if (graphic == null || graphic.GetComponentInParent<UIEnhancementButtonHighlight>() != null)
                continue;
            _art.Add(graphic);
            _wasEnabled.Add(graphic.enabled);
            graphic.enabled = false;
        }
        _masked = true;
    }

    internal void Restore()
    {
        if (!_masked) return;
        for (int i = 0; i < _art.Count; i++)
            if (_art[i] != null) _art[i].enabled = _wasEnabled[i];
        _art.Clear(); _wasEnabled.Clear(); _masked = false;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();
}
