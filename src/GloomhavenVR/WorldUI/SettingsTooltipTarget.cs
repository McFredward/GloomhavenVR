using System;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// One settings row's hover explanation — the marker component that turns a row into a tooltip
/// source.
///
/// <para>WHY IT IS A PLAIN uGUI HANDLER AND NOTHING MORE. The mod already has a complete hover
/// pipeline, and both of its input sources land in the same place: <c>UguiPointer.SetHovered</c>
/// dispatches real <see cref="ExecuteEvents.pointerEnterHandler"/> /
/// <see cref="ExecuteEvents.pointerExitHandler"/> up the full ancestor chain, exactly the way
/// Unity's own input module does, and BOTH the laser (<c>RayUguiDriver</c>) and the fingertip
/// (<c>PokeInteractor</c>) call it. So implementing the two stock interfaces is the whole
/// integration: no registration, no polling, no second tooltip system, and laser and touch work
/// identically by construction. A reference-counted tracker on the pointer side already
/// arbitrates two pointers on one row into exactly one enter and one exit.</para>
///
/// <para>WHY THE ROW AND NOT THE WIDGET. The handler sits on the ROW, which is the common ancestor
/// of that row's label and all of its buttons. Moving the beam from a stepper's "−" to its "+"
/// therefore fires nothing at all — the enter/exit walks stop at the shared ancestor — so the
/// explanation does not flicker while the player works the control it explains.</para>
///
/// <para>THE <see cref="OnDisable"/> IS NOT OPTIONAL. Settings rows are shown and hidden with
/// <c>SetActive</c> by the panel's category gate, and <c>ExecuteEvents</c> never delivers to an
/// inactive object — so a row that is hidden WHILE HOVERED (switch category with the beam resting
/// on a row) can never receive its exit, and the tooltip would be stranded on screen describing a
/// row that is no longer there. Releasing on disable closes that hole at the only place that can
/// see it.</para>
/// </summary>
internal sealed class SettingsTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>
    /// The explanation, resolved lazily. A delegate rather than a string so the text can name the
    /// CURRENT value of the setting it describes, and so a language change picks it up without
    /// anyone having to re-walk the rows.
    /// </summary>
    internal Func<string>? Text;

    /// <summary>Raised as (this, entered) — the panel owns the actual bubble.</summary>
    internal Action<SettingsTooltipTarget, bool>? Hover;

    public void OnPointerEnter(PointerEventData eventData) => Raise(true);

    public void OnPointerExit(PointerEventData eventData) => Raise(false);

    /// <summary>Category gate hid this row (or the panel closed) — release the bubble.</summary>
    private void OnDisable() => Raise(false);

    private void Raise(bool entered)
    {
        try
        {
            Hover?.Invoke(this, entered);
        }
        catch (Exception ex)
        {
            // A tooltip must never be able to break the hover dispatch for everything downstream
            // of it — the same chain carries the panel's clicks.
            VRLog.Error("WorldUI", $"Settings tooltip hover handler threw: {ex}");
        }
    }
}
