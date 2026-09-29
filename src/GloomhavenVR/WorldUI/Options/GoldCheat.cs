using MapRuleLibrary.Party;
using MapRuleLibrary.State;
using MapRuleLibrary.Adventure;
using GloomhavenVR.WorldUI.MapRoom;

namespace GloomhavenVR.WorldUI;

/// <summary>A map-only test shortcut through the game's own gold and save APIs.</summary>
internal static class GoldCheat
{
    internal static string UnavailableReason()
    {
        if (FFSNetwork.IsOnline) return "cheat_gold_online";
        if (!MapRoomDriver.Active || AdventureState.MapState?.MapParty == null)
            return "cheat_gold_no_map";
        if (MapRoomHand.OwnedMerchantCharacter() == null)
            return "cheat_gold_no_character";
        if (SaveData.Instance == null) return "cheat_gold_no_save";
        return "";
    }

    internal static bool TryGrant(out CMapCharacter? character, out bool partyGold, out int balance)
    {
        character = null;
        partyGold = false;
        balance = 0;
        if (UnavailableReason().Length != 0) return false;

        // Re-read the active selection on the click. In particular, never remember a character
        // across a character switch or grant gold to a former selection after returning to town.
        character = MapRoomHand.OwnedMerchantCharacter();
        var state = AdventureState.MapState;
        var save = SaveData.Instance;
        if (character == null || state?.MapParty == null || save == null || FFSNetwork.IsOnline)
            return false;

        // Guildmaster can use a shared party purse. Its rules deliberately reject per-character
        // ModifyGold, so the same visible shortcut credits the purse the shop actually reads.
        partyGold = state.GoldMode == EGoldMode.PartyGold;
        if (partyGold)
        {
            state.MapParty.ModifyPartyGold(100, useGoldModifier: false);
            balance = state.MapParty.PartyGold;
        }
        else
        {
            character.ModifyGold(100, useGoldModifier: false);
            balance = character.CharacterGold;
        }

        // The native setter publishes its ordinary gold-changed message; the native save path
        // persists the resulting campaign state. No synthetic UI or direct field write is needed.
        save.SaveCurrentAdventureData();
        return true;
    }
}
