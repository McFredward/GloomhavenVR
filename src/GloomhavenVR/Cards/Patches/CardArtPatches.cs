using HarmonyLib;

namespace GloomhavenVR.Cards.Patches;

/// <summary>
/// The one Harmony seam of <see cref="CardArtGuard"/> — see that class for the full root-cause
/// chain of the WHITE decision-phase card faces.
///
/// <c>FullAbilityCard.ShowCard()</c> is the game's re-entry into the addressable card-art
/// loader, and it runs from <c>OnEnable()</c>. On an ADOPTED face the mod re-activates the
/// GameObject every time the game's pick-mode <c>UpdateView</c> deactivates it
/// (<c>ToggleFullCard(active: false)</c>), so the loader is re-entered on every hand refresh;
/// each re-entry while the previous load is still running begins with
/// <c>ImageAddressableLoader.Unload</c> → <c>Image.sprite = null</c>, i.e. a white action half.
///
/// The prefix skips ONLY that case (adopted face + load actually in flight + inside the guard's
/// grace window) and the guard replays the skipped call once the loads are quiet, so no game
/// behaviour is lost — a card the mod has not adopted, and a card with no load running, both
/// run the original untouched.
/// </summary>
[HarmonyPatch(typeof(FullAbilityCard), nameof(FullAbilityCard.ShowCard))]
internal static class FullAbilityCard_ShowCard_ArtGuard
{
    private static bool Prefix(FullAbilityCard __instance) => !CardArtGuard.SuppressShowCard(__instance);
}
