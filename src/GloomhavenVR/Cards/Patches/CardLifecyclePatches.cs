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

/// <summary>Return borrowed native faces before Unity starts destroying any scene hierarchy.
/// CardsHandUI.OnDestroy is too late: a FullAbilityCard moved beneath a scene-owned VR host
/// can already have lost its children when the native hand tries to recycle the pooled widget.
/// The load iterator runs only after EndScenarioSafely has finished its mandatory decisions.
/// IsLoading alone is deliberately NOT used: it also covers that earlier decision wait.
/// </summary>
[HarmonyPatch(typeof(SceneController), "LoadSceneCoroutine")]
internal static class SceneController_LoadScene_CardLifetime
{
    private static void Postfix(SceneController __instance, ref System.Collections.IEnumerator __result)
    {
        try
        {
            // Assign only after construction succeeds: failure retains the original iterator.
            __result = new NativeCardSceneLifetime(__result,
                () => __instance != null && !__instance.DataRestoring,
                () => GloomhavenVR.Core.TickGuard.Run("Cards.SceneRelease",
                    CardsDriver.ReleaseCardsBeforeSceneLoad, "Cards"));
        }
        catch (System.Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private static void ReportFailure(System.Exception ex)
    {
        try { GloomhavenVR.Core.VRLog.Error("Cards", $"CARD SCENE RELEASE interception failed; native loading continues: {ex}"); }
        catch { /* Diagnostics must not escape a network-driven scene transition. */ }
    }
}
