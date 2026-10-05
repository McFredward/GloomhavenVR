using System;
using System.IO;
using System.Text.Json;
using Assets.Script.Misc;
using GLOO.Introduction;
using GloomhavenVR.Compat;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using HarmonyLib;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary.CustomLevels;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool condition, string message)
    { _assertions++; if (!condition) throw new Exception(message); }
    private static void Native(LevelMessageUILayout ui, CLevelMessage message, string why)
    { ui.Init(message); Check(ui.title.text.StartsWith("native:", StringComparison.Ordinal), why); }

    public static void Main(string[] args)
    {
        using var data = JsonDocument.Parse(File.ReadAllText(args[0]));
        var configData = data.RootElement.GetProperty("config");
        Check(configData.GetProperty("Phase").GetInt32() == (int)EMapFTUEStep.BuyItem,
            "Original game asset must still identify the BuyItem phase");
        var steps = configData.GetProperty("Steps");
        Check(steps.GetArrayLength() == 1, "Original BuyItem config must retain exactly its native exit instruction");
        var row = steps[0];
        var original = new IntroductionStepUI
        {
            LocalizationTextKey = row.GetProperty("LocalizationTextKey").GetString()!,
            LocalizationTextKeyController = row.GetProperty("LocalizationTextKeyController").GetString()!,
            LayoutType = (CLevelMessage.ELevelMessageLayoutType)row.GetProperty("LayoutType").GetInt32(),
            ShowScreenBG = row.GetProperty("ShowScreenBG").GetInt32() != 0,
            Tag = row.GetProperty("Tag").GetString()!,
        };
        Check(original.LocalizationTextKey == "FTUE_9.3" && original.LocalizationTextKeyController == "Consoles/FTUE_9.3_CONTROLLER",
            "Exact native merchant exit keys are pinned without rewriting global translations");
        var config = new MapFTUEStepConfigUI { Phase = EMapFTUEStep.BuyItem };
        config.Steps.Add(original);
        var shop = new UIShopItemWindow { ftueStep = new UIMapFTUEStep { config = config } };
        Singleton<UIShopItemWindow>.Instance = shop;
        var manager = new MapFTUEManager(); Singleton<MapFTUEManager>.Instance = manager;
        var harmony = new Harmony("ghvr.test.merchant-exit-text");
        harmony.CreateClassProcessor(typeof(MerchantTutorialExitMessagePatch)).Patch();
        harmony.CreateClassProcessor(typeof(MerchantTutorialExitTitlePatch)).Patch();
        harmony.CreateClassProcessor(typeof(MerchantTutorialExitPagePatch)).Patch();
        var message = original.ToMessage();
        var title = new LevelMessageUILayout(); title.Init(message);
        Check(title.title.text == "Close the merchant window with its X or press the Merchant button again.",
            "Original first-save merchant exit must explain both working VR close controls");
        Check(message.TitleKey == original.LocalizationTextKey && message.TitleKeyController == original.LocalizationTextKeyController
            && message.Pages[0].PageTextKey == original.LocalizationTextKey && message.Pages[0].PageTextKeyController == original.LocalizationTextKeyController,
            "Native localization keys and controller variants remain unchanged");
        Check(message.LayoutType == original.LayoutType && message.ShowScreenBG == original.ShowScreenBG
            && !message.ShouldPause && message.DismissTrigger.IsTriggeredByDismiss && config.Steps[0] == original,
            "Native presentation identity, dismissal and config membership remain unchanged");
        var page = new LevelMessagePageUI { page = message.Pages[0] }; page.OnLanguageChanged();
        Check(page.information.text == Loc.MerchantTutorialExit, "Native exit page follows the exact same VR instruction");
        Loc.CurrentLanguage = "German"; title.OnLanguageChanged(); page.OnLanguageChanged();
        Check(title.title.text == "Schließe das Händlerfenster über das X oder drücke erneut den Händlerknopf.",
            "German language repaint must restore the merchant exit instruction");
        Check(page.information.text == title.title.text, "German native page and title remain consistent");
        NativeLocalization.Controller = true; title.OnLanguageChanged(); page.OnLanguageChanged();
        Check(title.title.text == Loc.MerchantTutorialExit && page.information.text == Loc.MerchantTutorialExit,
            "Native controller repaint cannot restore obsolete WorldMap instructions");
        Loc.CurrentLanguage = "French"; title.OnLanguageChanged();
        Check(title.title.text.StartsWith("Close the merchant window", StringComparison.Ordinal), "Other languages retain the mod's English fallback");
        Loc.CurrentLanguage = "English"; NativeLocalization.Controller = false;
        var foreign = new IntroductionStepUI { LocalizationTextKey = original.LocalizationTextKey,
            LocalizationTextKeyController = original.LocalizationTextKeyController, LayoutType = original.LayoutType };
        Native(title, foreign.ToMessage(), "Matching text keys from another producer must not become merchant exit text");
        var foreignPage = new LevelMessagePageUI { page = new CLevelMessagePage(original.LocalizationTextKey, original.LocalizationTextKeyController) };
        foreignPage.OnLanguageChanged();
        Check(foreignPage.information.text.StartsWith("native:", StringComparison.Ordinal), "Matching page keys outside the exact original message remain native");
        original.LocalizationTextKey = "OTHER_HELP";
        Native(title, original.ToMessage(), "A future BuyItem help instruction with a different key remains native");
        original.LocalizationTextKey = "FTUE_9.3";
        original.LayoutType = CLevelMessage.ELevelMessageLayoutType.FixedLowerRight;
        Native(title, original.ToMessage(), "Page-based merchant explanations remain native");
        original.LayoutType = CLevelMessage.ELevelMessageLayoutType.HelpText;
        config.Phase = EMapFTUEStep.VisitMerchant;
        Native(title, original.ToMessage(), "A non-BuyItem serialized producer cannot inherit pending BuyItem text");
        config.Phase = EMapFTUEStep.BuyItem;
        MapRoomDriver.Active = false;
        Native(title, original.ToMessage(), "Flat and 2D map exit instructions remain native");
        MapRoomDriver.Active = true; WorldUIConfig.ImmersiveTownServices.Value = true;
        Native(title, original.ToMessage(), "Immersive resident onboarding does not teach a flat merchant exit");
        WorldUIConfig.ImmersiveTownServices.Value = false; AdventureState.MapState!.IsCampaign = false;
        Native(title, original.ToMessage(), "Guildmaster mode cannot inherit campaign onboarding text");
        AdventureState.MapState.IsCampaign = true; MapFTUEManager.IsPlaying = false;
        Native(title, original.ToMessage(), "Completed campaign onboarding cannot change ordinary merchant text");
        MapFTUEManager.IsPlaying = true; manager.CurrentStep = EMapFTUEStep.InteractWithMap;
        Native(title, original.ToMessage(), "Later map tutorial steps cannot reuse merchant exit text");
        manager.CurrentStep = EMapFTUEStep.BuyItem;
        title.Init(message); WorldUIConfig.ImmersiveTownServices.Value = true; title.OnLanguageChanged(); page.OnLanguageChanged();
        Check(title.title.text.StartsWith("native:", StringComparison.Ordinal) && page.information.text.StartsWith("native:", StringComparison.Ordinal),
            "Mode changes restore native painting even for a previously marked message");
        WorldUIConfig.ImmersiveTownServices.Value = false; title.OnLanguageChanged();
        Check(title.title.text == Loc.MerchantTutorialExit, "Returning to nonimmersive VR restores the same native message's correct text");
        Check(config.Steps.Count == 1 && config.Steps[0] == original && shop.ftueStep!.config == config
            && manager.CurrentStep == EMapFTUEStep.BuyItem && MapFTUEManager.IsPlaying,
            "Text adapter never advances tutorial state or replaces native configs");
        Console.WriteLine($"PASS merchant tutorial text: {_assertions} source-bound Harmony/native-row assertions");
    }
}
