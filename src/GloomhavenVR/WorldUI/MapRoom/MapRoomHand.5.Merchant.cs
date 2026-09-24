using FFSNet;
using MapRuleLibrary.Party;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    private bool _merchantInspection, _templeInspection;
    private bool TownInspection => _merchantInspection || _templeInspection;
    internal static CMapCharacter? OwnedMerchantCharacter()
    {
        CMapCharacter? character = MapCharacterSelection.Current(out _);
        return MapRoomDriver.Active && character != null
            && (!FFSNetwork.IsOnline || character.IsUnderMyControl) ? character : null;
    }
    internal static void SetTempleInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null || hand._templeInspection == active) return;
        hand._templeInspection = active;
        if (active) hand.ReleaseFan("temple donation pouch");
        else if (!hand.TownInspection && MapRoomDriver.Active && hand._engaged) hand.RebuildFan();
    }
    internal static void SetMerchantInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null || hand._merchantInspection == active) return;
        hand._merchantInspection = active;
        if (active) hand.ReleaseFan("merchant owned-item inspection");
        else if (!hand.TownInspection && MapRoomDriver.Active && hand._engaged) hand.RebuildFan();
    }
}
