using FFSNet;
using GloomhavenVR.Cards;
using MapRuleLibrary.Party;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    private bool _merchantInspection, _templeInspection, _townInspectionFanWasOpen;
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
        bool wasInspecting = hand.TownInspection;
        hand._templeInspection = active;
        hand.SetTownInspectionFan(active, wasInspecting, "temple donation pouch");
    }
    internal static void SetMerchantInspection(bool active)
    {
        MapRoomHand? hand = s_live;
        if (hand == null || hand._merchantInspection == active) return;
        bool wasInspecting = hand.TownInspection;
        hand._merchantInspection = active;
        hand.SetTownInspectionFan(active, wasInspecting, "merchant owned-item inspection");
    }

    private void SetTownInspectionFan(bool active, bool wasInspecting, string reason)
    {
        if (active)
        {
            if (wasInspecting) return;
            _townInspectionFanWasOpen = CardsDriver.OffScenarioFanIsOpen;
            if (_townInspectionFanWasOpen) CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: false);
            ReleaseFan(reason);
            return;
        }
        if (TownInspection || !wasInspecting) return;
        if (_townInspectionFanWasOpen) CardsDriver.SuppressNextOffScenarioFanEdgeSound(open: true);
        _townInspectionFanWasOpen = false;
        if (MapRoomDriver.Active && _engaged) RebuildFan();
    }
}
