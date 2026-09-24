using FFSNet;
using MapRuleLibrary.Party;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    private bool _merchantInspection;
    internal static CMapCharacter? OwnedMerchantCharacter()
    {
        CMapCharacter? character = MapCharacterSelection.Current(out _);
        return MapRoomDriver.Active && character != null
            && (!FFSNetwork.IsOnline || character.IsUnderMyControl) ? character : null;
    }
    internal static void SetMerchantInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null || hand._merchantInspection == active) return;
        hand._merchantInspection = active;
        if (active) hand.ReleaseFan("merchant owned-item inspection");
        else if (MapRoomDriver.Active && hand._engaged) hand.RebuildFan();
    }
}
