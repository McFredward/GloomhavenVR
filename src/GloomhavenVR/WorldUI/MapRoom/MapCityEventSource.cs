using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using MapRuleLibrary.Adventure;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>The campaign-only encounter control is a separate native button class, outside
/// the ordinary guildmaster mode bar. Visibility and click eligibility remain distinct.</summary>
internal static class MapCityEventSource
{
    internal static T? Field<T>(object source, string name) where T : class =>
        source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(source) as T;

    internal static UICityEncounterButton? Resolve()
    {
        // Native UIGuildmasterHUD.Initialize excludes this control entirely in Guildmaster.
        if (!AdventureState.MapState.IsCampaign || !Singleton<UIGuildmasterHUD>.IsInitialized) return null;
        return Field<UICityEncounterButton>(Singleton<UIGuildmasterHUD>.Instance, "cityEncounterButton");
    }

    internal static bool Pressable(UICityEncounterButton city) =>
        !MapInputGate.IsBlocked && !StoryComposite.PointOfNoReturn && AdventureState.MapState.IsCampaign && city.Interactable
        && !OptionsBlocked()
        && (city.gameObject.activeSelf || InputManager.GamePadInUse)
        && AdventureState.MapState.MapParty.SelectedCharacters.Count() > 1
        && (!MapFTUEManager.IsPlaying || Singleton<MapFTUEManager>.Instance.HasCompletedStep(EMapFTUEStep.BuyItem));

    private static bool OptionsBlocked()
    {
        if (!Singleton<UIGuildmasterHUD>.IsInitialized) return true;
        // Read the native request set without removing a request or reactivating the bar. Unlike
        // window-mode caps, an encounter starts a shared gameplay flow and must retain this lock.
        var requests = Field<HashSet<Component>>(Singleton<UIGuildmasterHUD>.Instance, "disableOptionsRequests");
        return requests == null || requests.Count != 0;
    }
}
