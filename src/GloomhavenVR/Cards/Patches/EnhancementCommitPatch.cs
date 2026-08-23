using HarmonyLib;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// THE ONE EDGE the Enchantress raises — see <see cref="HandFanEnhancementRefresh"/> for the whole
/// derivation, including why an in-place sticker write beats a face re-print.
///
/// <para>WHY THIS METHOD AND NOT ANOTHER. The enhancement commit raises no event of any kind (no
/// <c>event</c>, no <c>Action</c>, no <c>UnityEvent</c>, and the game's real bus —
/// <c>MapChoreographerUIEvents</c>, 16 members — has no enhancement member), so a patch is the only
/// available edge. <c>MapPartyEnhancementShopService.AddEnhancement</c> is the single choke point
/// for all four ways a card's enhancement set can change:
/// <list type="bullet">
///   <item>a local BUY — <c>UINewEnhancementWindow.ConfirmBuy</c> → confirmation → this;</item>
///   <item>a local SELL — <c>RemoveEnhancement</c> (MapPartyEnhancementShopService.cs:131) is one
///   line that forwards to this same method with <c>EEnhancement.NoEnhancement</c>;</item>
///   <item>a peer's buy and a peer's sell — <c>UINewEnhancementWindow.ProxyBuyEnhancement</c> /
///   <c>ProxySellEnhancement</c> (UINewEnhancementWindow.cs:738 / :793) both call it too.</item>
/// </list>
/// and it is the only implementation of <c>IEnhancementShopService</c> in the game.</para>
///
/// <para>POSTFIX, NOT PREFIX, and that is load-bearing: the commit's own last visual act is
/// <c>SaveDataShared.ApplyEnhancementIcons</c> (MapPartyEnhancementShopService.cs:58), which
/// re-projects the character's enhancement list onto the shared ability slots the refresh reads
/// back. Running before it would read the pre-commit state.</para>
///
/// <para>THE POSTFIX READS PLAIN FIELDS ONLY. <c>EnhancementButtonBase.AbilityCardID</c> and
/// <c>.AbilityName</c> are public fields set in <c>Init</c>; the <c>Enhancement</c> PROPERTY on the
/// same type walks <c>CharacterClassManager</c> and can throw, so it is not touched here — the
/// refresh resolves it per sticker, guarded. Nothing in this patch can throw into the game's
/// commit.</para>
/// </summary>
[HarmonyPatch(typeof(MapPartyEnhancementShopService), nameof(MapPartyEnhancementShopService.AddEnhancement))]
internal static class MapPartyEnhancementShopService_AddEnhancement_FanRefresh
{
    private static void Postfix(EnhancementButtonBase button)
    {
        if (button == null)
            return;
        HandFanEnhancementRefresh.CardEnhanced(button.AbilityCardID, button.AbilityName);
    }
}
