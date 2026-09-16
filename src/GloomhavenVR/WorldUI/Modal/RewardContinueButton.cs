using GloomhavenVR.Core;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Ordinary uGUI pointer/keyboard state machine with the native reward button skin.
/// Build 510 used Button's near-white default ColorTint and never sampled the native hover
/// sprite, so pointing at Continue had no clear visible response. State changes paint the
/// original source graphic, which also makes capture targets update with the same appearance.
/// </summary>
internal sealed class RewardContinueButton : Button
{
    private bool _reportedHover;

    public override void OnPointerEnter(PointerEventData eventData)
    {
        base.OnPointerEnter(eventData);
        if (_reportedHover) return;
        _reportedHover = true;
        VRLog.Note("WorldUI", $"REWARD SHOWCASE INPUT: pointer reached Continue; interactable={IsInteractable()}.");
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        base.DoStateTransition(state, instant);
        Paint(state);
    }

    internal void RefreshSkin() => Paint(currentSelectionState);

    private void Paint(SelectionState state)
    {
        if (targetGraphic is not Image image) return;
        NativeButtonSkin.FaceState face = state switch
        {
            SelectionState.Disabled => NativeButtonSkin.FaceState.Disabled,
            SelectionState.Pressed => NativeButtonSkin.FaceState.Pressed,
            SelectionState.Highlighted or SelectionState.Selected => NativeButtonSkin.FaceState.Accent,
            _ => NativeButtonSkin.FaceState.Idle,
        };
        var sprite = NativeButtonSkin.SpriteFor(face);
        if (sprite != null) image.sprite = sprite;
        image.color = NativeButtonSkin.ColorFor(face);
    }
}
