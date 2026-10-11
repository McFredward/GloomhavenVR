using MapRuleLibrary.Adventure;

namespace GloomhavenVR.WorldUI;

/// <summary>The same persistent headquarters unlocks and first-map tutorial restrictions used
/// by MerchantMode, TempleMode and EnchantressMode. A resident and all of their furniture are
/// one presentation unit: an unavailable native destination must never leave a decorative stand
/// behind, even if another player's town presentation still has a transient visitor record.</summary>
internal static class TownServiceAvailability
{
    internal static bool NativeUnlocked(byte service)
    {
        var map = AdventureState.MapState;
        if (map == null || map.HeadquartersState == null) return false;
        bool tutorial = MapFTUEManager.IsPlaying;
        MapFTUEManager? ftue = tutorial ? Singleton<MapFTUEManager>.Instance : null;
        if (tutorial && ftue == null) return false;
        return FromNativeState(service, map.HeadquartersState.MerchantUnlocked,
            map.HeadquartersState.TempleUnlocked, map.HeadquartersState.EnhancerUnlocked,
            tutorial, !tutorial || ftue!.HasCompletedStep(EMapFTUEStep.CreatedSecondCharacter),
            !tutorial || ftue!.HasCompletedStep(EMapFTUEStep.BuyItem));
    }

    // Keep the three native modes' IsUnlocked decisions in one testable place. Both campaign and
    // Guildmaster use HeadquartersState; only campaign's first-map FTUE adds the temporary gate.
    internal static bool FromNativeState(byte service, bool merchant, bool temple, bool enchantress,
        bool tutorial, bool secondCharacterCreated, bool buyItemCompleted) => service switch
    {
        1 => merchant && (!tutorial || secondCharacterCreated),
        2 => temple && (!tutorial || buyItemCompleted),
        3 => enchantress && (!tutorial || buyItemCompleted),
        _ => false,
    };

    // A remote visitor never overrides this client's native save unlock. The resident author
    // can still publish all unlocked services when one of the three is absent; the aggregate
    // channel must not become inactive merely because a single entry is locked.
    internal static bool ShouldPublish(bool nativeUnlocked, bool immersive, bool remoteVisitor)
        => nativeUnlocked && (immersive || remoteVisitor);
}
