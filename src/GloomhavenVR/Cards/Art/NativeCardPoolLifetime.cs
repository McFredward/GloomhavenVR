using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>Keep native pooled cards self-contained across presentation and scene lifetimes.</summary>
internal static class NativeCardPoolLifetime
{
    /// <summary>
    /// Build 527's error names SwiftBow and SetUpInteractabilityRelationship's topActionButton
    /// access. Returning only adopted faces is insufficient: a short-rest dialog can own a
    /// face, and CardFace's captured parent can itself be that temporary dialog. Move native
    /// faces back into their pooled widget before Unity schedules scene-child destruction,
    /// including faces which the VR wrapper already yielded. No native decision is advanced.
    /// </summary>
    internal static void ReturnSceneHands()
    {
        CardsHandManager? manager = CardsHandManager.Instance;
        if (manager == null) return;
        foreach (CardsHandUI hand in manager.cardHandsUI)
        {
            if (hand == null) continue;
            foreach (AbilityCardUI widget in hand.cardsUI)
                ReturnToOwner(widget);
        }
    }

    internal static void ReturnToOwner(AbilityCardUI? widget)
    {
        if (widget == null || widget.fullAbilityCard == null) return;
        Transform face = widget.fullAbilityCard.transform;
        if (!face.IsChildOf(widget.transform))
        {
            face.SetParent(widget.transform, worldPositionStays: true);
            // RecycleCard normally performs these together. Reparenting early must not
            // bypass its parent-dependent cancellation and leave a pooled loss tween alive.
            widget.CancelLostAnimation();
        }
    }

    internal static bool IsIntact(AbilityCardUI? widget)
    {
        if (widget == null || widget.miniAbilityCard == null || widget.fullAbilityCard == null)
            return false;
        FullAbilityCard full = widget.fullAbilityCard;
        return full.transform.IsChildOf(widget.transform)
            && full.topActionButton != null && full.topActionButton.actionButton != null
            && full.topActionButton.transform.IsChildOf(full.transform)
            && full.bottomActionButton != null && full.bottomActionButton.actionButton != null
            && full.bottomActionButton.transform.IsChildOf(full.transform);
    }

    /// <summary>
    /// The game's pool trusts surviving roots, even when an earlier scene teardown destroyed
    /// a serialized child. Retire only damaged recycled UI copies before native selection;
    /// GetCardInstance then uses another intact copy or clones its untouched index-zero
    /// template. Card data, callbacks and Init remain entirely native. Never repair a broken
    /// card by skipping Init or its interaction setup, which would leave an unplayable hand.
    /// </summary>
    internal static void PruneDamagedCopies(ObjectPool.CardPool? pool)
    {
        if (pool == null || pool.CardType != ObjectPool.ECardType.Ability
            || pool.Instances.Count == 0) return;
        GameObject template = pool.Instances[0];
        if (template == null || !IsIntact(template.GetComponent<AbilityCardUI>())) return;
        for (int i = pool.Instances.Count - 1; i > 0; i--)
        {
            GameObject copy = pool.Instances[i];
            if (copy == null || IsIntact(copy.GetComponent<AbilityCardUI>())) continue;
            pool.Instances.RemoveAt(i);
            Object.Destroy(copy);
            VRLog.Warn("Cards", "CARD POOL REPAIR: retired an incomplete recycled ability-card widget; native spawning will use its intact original template.");
        }
    }
}
