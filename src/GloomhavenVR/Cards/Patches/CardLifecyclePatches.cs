using HarmonyLib;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards.Patches;

// ---------------------------------------------------------------------------
// Pool-safety patches. The VR layer re-parents each card's live FullAbilityCard
// canvas onto a world-space card (VRCard). The game POOLS AbilityCardUI objects
// (ObjectPool.SpawnCard/RecycleCard, verified ObjectPool.cs:415/520): if a card
// went back to the pool while its face is still parented under a VR object, the
// pool would hold a corrupted widget. These prefixes give the VR layer a chance
// to restore every adopted face BEFORE the game recycles.
// ---------------------------------------------------------------------------

/// <summary>
/// Scenario teardown: verified <c>private void OnDestroy()</c> (CardsHandUI.cs:399)
/// recycles every card of the hand via <c>ObjectPool.RecycleCard(...)</c>.
/// Unity magic method — never inlined (PATCH-TARGETS §5).
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), "OnDestroy")]
internal static class CardsHandUI_OnDestroy_Patch
{
    private static void Prefix(CardsHandUI __instance)
    {
        CardsSignals.RaiseHandDestroying(__instance);
    }
}

/// <summary>
/// Single-card removal: verified <c>public void DestroyCardUI(CAbilityCard
/// abilityCard)</c> (CardsHandUI.cs:1699) → RecycleCard.
/// </summary>
[HarmonyPatch(typeof(CardsHandUI), nameof(CardsHandUI.DestroyCardUI))]
internal static class CardsHandUI_DestroyCardUI_Patch
{
    private static void Prefix(CardsHandUI __instance, CAbilityCard abilityCard)
    {
        if (abilityCard == null)
            return;
        // Verified: private List<AbilityCardUI> cardsUI (publicized).
        var cards = __instance.cardsUI;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null && cards[i].AbilityCard == abilityCard)
            {
                CardsSignals.RaiseCardRecycling(cards[i]);
                return;
            }
        }
    }
}
