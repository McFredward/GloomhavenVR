#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using FFSNet;
using GloomhavenVR.WorldUI;

// Exact read-only GH.Runtime ConfirmSell body follows. Networking, pricing,
// appearance and payment services are declared boundary counters; the original
// admission, latch and delayed confirm/cancel delegates execute unchanged.
internal sealed class UINewEnhancementWindow
{
    private bool _isConfirmationBoxOpened;
    private readonly NativeSaleCharacter character = new();
    private readonly NativeSaleService shopService = new();
    private readonly NativeSaleDisplay cardsDisplay = new();
    private readonly NativeSaleCard CheckedCard = new();
    private readonly object audioItemSellEnhancement = new();
    private readonly NativeSaleController controllerArea = new();
    private bool GamepadMode => false;
    internal bool ConfirmationOpened => _isConfirmationBoxOpened;
    internal int NativeEntries;
    internal int Removals => shopService.Removals;
    internal int Refreshes;
    private void RefreshSelectionAfterEnhance(NativeSaleCard card) => Refreshes++;
    internal void RequestSale(EnhancementSlot option, bool originalOnly = false)
    {
        if (!originalOnly && !TownServiceEnhancementJoinGuard.Prefix()) return;
        NativeEntries++;
        ConfirmSell(option);
    }
	private void ConfirmSell(EnhancementSlot enhancement)
	{
		_isConfirmationBoxOpened = true;
		if ((FFSNetwork.IsHost && PlayerRegistry.JoiningPlayers.Count > 0) || (FFSNetwork.IsClient && PlayerRegistry.OtherClientsAreJoining))
		{
			return;
		}
		int num = character.CalculateSellPriceEnhancement(enhancement.button);
		string information = string.Format("<color=#{2}>{0} <sprite name=\"Gold_Icon_White\" color=#{2}>{1}</color>", LocalizationManager.GetTranslation("Consoles/GUI_COST") + ":", num, UIInfoTools.Instance.goldColor.ToHex());
		Singleton<UIEnhancementConfirmationBox>.Instance.ShowConfirmation(LocalizationManager.GetTranslation("GUI_SELL_ENHANCEMENT_CONFIRMATION_TITLE"), information, enhancement.enhancement, delegate
		{
			_isConfirmationBoxOpened = false;
			if (FFSNetwork.IsOnline)
			{
				IProtocolToken supplementaryDataToken = new EnhancementToken(enhancement.button, enhancement.enhancement);
				Synchronizer.SendGameAction(GameActionType.SellEnhancement, ActionPhaseType.MapHQ, validateOnServerBeforeExecuting: true, disableAutoReplication: false, 0, 0, 0, 0, supplementaryDataBoolean: false, default(Guid), supplementaryDataToken);
				if (FFSNetwork.IsClient)
				{
					return;
				}
			}
			AudioControllerUtils.PlaySound(audioItemSellEnhancement);
			shopService.RemoveEnhancement(enhancement.button);
			cardsDisplay.OnRemovedEnhancement(CheckedCard.AbilityCard);
			RefreshSelectionAfterEnhance(CheckedCard);
			if (!GamepadMode)
			{
				controllerArea.Focus();
			}
		}, "GUI_SELL", null, delegate
		{
			_isConfirmationBoxOpened = false;
		});
	}
}
internal sealed class EnhancementSlot { internal readonly EnhancementButtonBase button = new(); internal readonly int enhancement = 1; }
internal sealed class NativeSaleCharacter { internal readonly string CharacterName = "native-owner"; internal int CalculateSellPriceEnhancement(object button) => 12; }
internal sealed class NativeSaleService { internal int Removals; internal void RemoveEnhancement(object button) => Removals++; }
internal sealed class NativeSaleDisplay { internal void OnRemovedEnhancement(object card) { } }
internal sealed class NativeSaleCard { internal readonly object AbilityCard = new(); }
internal sealed class NativeSaleColor { internal string ToHex() => "ffffff"; }
internal sealed class NativeSaleController { internal void Focus() => FfsSaleBoundary.Focused++; }
internal static class FfsSaleBoundary { internal static int Focused; }
internal static class LocalizationManager { internal static string GetTranslation(string key) => key; }
internal sealed class UIEnhancementConfirmationBox
{
    internal int Shown;
    internal Action Confirm, Cancel;
    internal void ShowConfirmation(string title, string text, int enhancement,
        Action confirm, string confirmKey, string cancelKey, Action cancel)
    { Shown++; Confirm = confirm; Cancel = cancel; }
}
namespace FFSNet
{
    internal static class PlayerRegistry
    {
        internal static List<object> JoiningPlayers { get; } = new();
        internal static bool OtherClientsAreJoining;
        internal static readonly NativeSalePlayer MyPlayer = new();
    }
    internal enum GameActionType { SellEnhancement }
    internal enum ActionPhaseType { MapHQ }
    internal interface IProtocolToken { }
    internal sealed class EnhancementToken : IProtocolToken
    { internal EnhancementToken(object button, int enhancement) { } }
    internal static class Synchronizer
    {
        internal static int Requests;
        internal static void SendGameAction(GameActionType action, ActionPhaseType phase,
            bool validateOnServerBeforeExecuting, bool disableAutoReplication,
            int actorID, int min, int max, int extra, bool supplementaryDataBoolean,
            Guid actorGuid, IProtocolToken supplementaryDataToken) => Requests++;
    }
}

// Original slot permission, retained ahead of the guarded native sale handler.
internal sealed class UINewEnhancementShopSlot
{
    private readonly NativeSaleButton button = new();
    private readonly NativeSaleCharacter character = new();
    private readonly EnhancementSlot enhancement = new();
    internal bool Click(UINewEnhancementWindow shop)
    {
        DetermineInteractability();
        if (!button.interactable) return false;
        shop.RequestSale(enhancement);
        return true;
    }
	public void DetermineInteractability()
	{
		CCharacterClass cCharacterClass = CharacterClassManager.Classes.FirstOrDefault((CCharacterClass x) => x.FindCardWithID(enhancement.button.AbilityCardID) != null);
		int controllableID = ((SaveData.Instance.Global.GameMode == EGameMode.Campaign) ? character.CharacterName.GetHashCode() : cCharacterClass.ModelInstanceID);
		button.interactable = !FFSNetwork.IsOnline || (cCharacterClass != null && PlayerRegistry.MyPlayer.HasControlOver(controllableID) && ControllableRegistry.GetControllable(controllableID)?.NetworkEntity != null);
	}
}
internal sealed class NativeSaleButton { internal bool interactable; }
internal sealed class EnhancementButtonBase { internal int AbilityCardID => 42; }
internal sealed class CCharacterClass
{
    internal int ModelInstanceID => 12;
    internal object FindCardWithID(int id) => id == 42 ? this : null;
}
internal static class CharacterClassManager
{ internal static readonly List<CCharacterClass> Classes = new() { new CCharacterClass() }; }
internal sealed class NativeSalePlayer
{
    internal bool Owns;
    internal bool HasControlOver(int id) => Owns && id == 12;
}
internal sealed class NativeSaleControllable { internal object NetworkEntity => this; }
internal static class ControllableRegistry
{ internal static NativeSaleControllable GetControllable(int id) => new(); }
internal enum EGameMode { Campaign, Guildmaster }
internal sealed class SaveData
{
    internal static readonly SaveData Instance = new();
    internal readonly SaleGlobal Global = new();
}
internal sealed class SaleGlobal { internal EGameMode GameMode => EGameMode.Guildmaster; }
