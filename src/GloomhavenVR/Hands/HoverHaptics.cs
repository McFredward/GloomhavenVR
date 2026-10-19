using System.Collections.Generic;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Hands;

/// <summary>
/// One hover encounter per semantic surface, shared by this hand's pointer, fingertip and grab
/// drivers. Their edges occur at different distances: a global 50 ms cooldown never made their
/// separate pulses into one pulse per card. Refresh while touching/pointing; brief election or
/// raycast losses do not create another encounter. Click and grab feedback use their own presets.
/// </summary>
internal sealed class HoverHapticGate
{
    private const float ExitGraceSeconds = 0.15f;
    private const int MaxTargets = 64;
    private readonly List<Entry> _entries = new(8);

    private struct Entry
    {
        internal object Target;
        internal float SeenAt;
    }

    internal bool Enter(object? target, float now)
    {
        if (target == null)
            return false;
        int oldest = 0;
        for (int i = 0; i < _entries.Count; i++)
        {
            Entry entry = _entries[i];
            if (ReferenceEquals(entry.Target, target))
            {
                bool entered = now - entry.SeenAt > ExitGraceSeconds || now < entry.SeenAt;
                entry.SeenAt = now;
                _entries[i] = entry;
                return entered;
            }
            if (entry.SeenAt < _entries[oldest].SeenAt)
                oldest = i;
        }
        var added = new Entry { Target = target, SeenAt = now };
        if (_entries.Count < MaxTargets)
            _entries.Add(added);
        else
            _entries[oldest] = added;
        return true;
    }
}

/// <summary>A collider and the original uGUI region it represents share one feedback identity.</summary>
internal interface IHoverHapticTarget
{
    object? HoverHapticTarget { get; }
}

internal static class HoverHapticTargets
{
    internal static object? Pokeable(IPokeable target) =>
        target is IHoverHapticTarget semantic ? semantic.HoverHapticTarget : target;

    /// <summary>
    /// Native tooltip graphics are hover handlers, not actions. Resolve the original card half
    /// (including content pushers outside its button subtree), default-action plate, or ordinary
    /// Selectable. Icons, text and tooltip rebuilds inside one region never become new targets.
    /// </summary>
    internal static Object? Ui(GameObject? target)
    {
        if (target == null)
            return null;
        FullAbilityCard? card = target.GetComponentInParent<FullAbilityCard>();
        if (card != null)
        {
            FullAbilityCardAction? action = target.GetComponentInParent<FullAbilityCardAction>();
            FullCardEventPusher? pusher = target.GetComponentInParent<FullCardEventPusher>();
            bool isDefault = false;
            if (pusher != null)
            {
                FullCardEventPusher.EventType flags = pusher._eventType;
                isDefault = (flags & FullCardEventPusher.EventType.Default) != 0;
                if ((flags & FullCardEventPusher.EventType.Top) != 0)
                    action = card.topActionButton;
                else if ((flags & FullCardEventPusher.EventType.Bottom) != 0)
                    action = card.bottomActionButton;
            }
            if (action == null)
                return null;
            Button? standard = action.defaultActionButton;
            if (standard != null && (isDefault || target.transform.IsChildOf(standard.transform)))
                return standard;
            return action;
        }
        Selectable? selectable = target.GetComponentInParent<Selectable>();
        return selectable != null && selectable.IsActive() && selectable.IsInteractable()
            ? selectable : null;
    }
}
