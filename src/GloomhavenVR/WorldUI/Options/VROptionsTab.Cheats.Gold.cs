using System;
using GloomhavenVR.Core;
using MapRuleLibrary.Party;

namespace GloomhavenVR.WorldUI;

internal static partial class VROptionsTab
{
    private static string? _goldLastResult;

    private static int BuildGoldRow()
    {
        if (ContentRoot == null) return 0;
        BuildNote(ContentRoot, Loc.Mod("cheat_gold_hint"));
        RegisterCheatRow(BuildLinkRow(ContentRoot, GoldCaption(), OnGrantGold, asAction: true),
            GoldCaption);
        return 1;
    }

    private static string GoldCaption()
    {
        string reason = GoldCheat.UnavailableReason();
        return reason.Length != 0 ? Loc.Mod(reason) : _goldLastResult ?? Loc.Mod("cheat_gold");
    }

    private static void OnGrantGold()
    {
        try
        {
            if (!CheatsAvailable) return;
            string reason = GoldCheat.UnavailableReason();
            if (reason.Length != 0)
            {
                _goldLastResult = null;
                VRLog.Info("WorldUI", "CHEAT 'grant gold': REFUSED — " + reason);
            }
            else if (GoldCheat.TryGrant(out CMapCharacter? character, out bool partyGold,
                         out int balance))
            {
                string owner = partyGold ? Loc.Mod("cheat_gold_party")
                    : character?.DisplayCharacterName ?? character?.CharacterName ?? "?";
                _goldLastResult = string.Format(Loc.Mod("cheat_gold_done"), owner, balance);
                VRLog.Note("WorldUI", "CHEAT 'grant gold': +100 through native "
                    + (partyGold ? "CMapParty.ModifyPartyGold" : "CMapCharacter.ModifyGold")
                    + $" to {owner}; balance={balance}, native save requested.");
            }
            else
            {
                _goldLastResult = null;
                VRLog.Warn("WorldUI", "CHEAT 'grant gold': selection or save became unavailable "
                    + "before the press could be applied. No gold was granted.");
            }
            RefreshCheatRows();
        }
        catch (Exception ex)
        {
            _goldLastResult = null;
            VRLog.Error("WorldUI", "CHEAT 'grant gold' failed: " + ex);
            RefreshCheatRows();
        }
    }
}
