using GloomhavenVR.Cards;
using GloomhavenVR.Net;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
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
