using GloomhavenVR.Cards;
using GloomhavenVR.Net;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    /// <summary>The offering is the player's actual loadout card, never an observer's copy.</summary>
    internal static bool TryOwnedTownCard(VRCard card, out CMapCharacter? character, out CAbilityCard? model)
    {
        character = null; model = null;
        MapRoomHand? live = s_live;
        if (card == null || !MapRoomDriver.Active || live == null
            || !live._cards.Contains(card)
            || !MapCardSources.TryGetValue(card, out MapCardSource source)) return false;
        character = source.Character; model = source.Model;
        // Offering a card to the enchantress is not a fan reorder. In particular, the
        // reorder gate's reflected ControllerPlayerId can be temporarily unknown while
        // a multiplayer room is connecting even though the game's own local ownership
        // and this player's published loadout are already valid. Requiring that gate
        // left the enchantress attentive but with no handoff target.
        return character != null && model != null
            && ReferenceEquals(character, live._character)
            && live._loadout.Contains(model)
            && (!FFSNetwork.IsOnline || character.IsUnderMyControl);
    }

    // MB497 supersedes the original map inspection-only ordering restriction. This address stays
    // local and survives scene reconstruction; only positional loadout seats travel to peers.
    internal static bool TryFanOrderKey(VRCard card, out string character, out int id)
    {
        character = string.Empty;
        id = int.MinValue;
        if (!MapCardSources.TryGetValue(card, out MapCardSource source)
            || source.Character == null || source.Model == null) return false;
        character = source.Character.CharacterName;
        id = source.Model.ID;
        return !string.IsNullOrEmpty(character);
    }

    internal static bool CanReorderLocalFan(VRCard card)
    {
        MapRoomHand? live = s_live;
        if (live == null || !MapCardSources.TryGetValue(card, out MapCardSource source)
            || source.Character == null || source.Model == null) return false;
        int local = NetPlayerActors.LocalPlayerId();
        return FanOrderMemory.CanReorder(
            !FFSNetwork.IsOnline || (local > 0 && ControllerPlayerId(source.Character) == local),
            live._loadout.Contains(source.Model), ReferenceEquals(source.Character, live._character));
    }
}
