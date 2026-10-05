using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GLOO.Introduction;
using ScenarioRuleLibrary.CustomLevels;

// Declared native boundaries for a narrow text adapter. The original FTUE row is
// supplied from the read-only game asset, and ToMessage retains the native construction
// body. Actual Harmony patches run against these methods. This is not a full savegame
// progression, headset layout or network test; those belong to the native flow lane.
public enum EMapFTUEStep { None, VisitMerchant = 9, BuyItem = 10, InteractWithMap = 11 }
public interface IMapFTUEStep { EMapFTUEStep Step { get; } }
public sealed class MapFTUEManager
{
    public static bool IsPlaying = true;
    public IMapFTUEStep? currentStep;
    public EMapFTUEStep CurrentStep => !IsPlaying || currentStep == null ? EMapFTUEStep.None : currentStep.Step;
}
public sealed class UIMapFTUEStep : IMapFTUEStep
{
    public MapFTUEStepConfigUI? config;
    public EMapFTUEStep Step => config!.Phase;
}
public sealed class UIShopItemWindow { public UIMapFTUEStep? ftueStep; }
namespace Assets.Script.Misc
{ public static class Singleton<T> where T : class { public static T? Instance; } }
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapRoomDriver { internal static bool Active = true; } }
namespace GloomhavenVR.WorldUI
{
    internal sealed class Setting<T> { internal T Value; internal Setting(T value) { Value = value; } }
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive = true;
        internal static readonly Setting<bool> ImmersiveTownServices = new(false);
    }
}
namespace MapRuleLibrary.Adventure
{
    public sealed class MapStatePort { public bool IsCampaign = true; }
    public static class AdventureState { public static MapStatePort? MapState = new(); }
}
namespace GloomhavenVR.Core
{ internal static partial class Loc { internal static string CurrentLanguage = "English"; } }
namespace GLOO.Introduction
{
    public sealed class MapFTUEStepConfigUI
    {
        public EMapFTUEStep Phase;
        public List<IntroductionStepUI> Steps = new();
        public List<IntroductionStepUI> GetSteps() => Steps.FindAll(it => it.IsVisible);
    }
    public sealed class IntroductionStepUI
    {
        public string LocalizationTextKey = "";
        public string LocalizationTextKeyController = "";
        public CLevelMessage.ELevelMessageLayoutType LayoutType;
        public bool ShowScreenBG;
        public bool IsVisible = true;
        public string Tag = "";
        [MethodImpl(MethodImplOptions.NoInlining)]
        public CLevelMessage ToMessage()
        {
            return new CLevelMessage(null, LayoutType,
                LayoutType == CLevelMessage.ELevelMessageLayoutType.HelpText ? LocalizationTextKey : null,
                0f, null, new CLevelTrigger { IsTriggeredByDismiss = true }, null,
                new List<CLevelMessagePage> { new(LocalizationTextKey, LocalizationTextKeyController) },
                shouldPause: false, ShowScreenBG, null,
                LayoutType == CLevelMessage.ELevelMessageLayoutType.HelpText ? LocalizationTextKeyController : null);
        }
    }
}
namespace ScenarioRuleLibrary.CustomLevels
{
    public sealed class CLevelTrigger { public bool IsTriggeredByDismiss; }
    public sealed class CLevelMessagePage
    {
        public string PageTextKey, PageTextKeyController;
        public CLevelMessagePage(string key, string controller) { PageTextKey = key; PageTextKeyController = controller; }
    }
    public sealed class CLevelMessage
    {
        public enum ELevelMessageLayoutType { HelpText = 1, FixedLowerRight = 5 }
        public readonly ELevelMessageLayoutType LayoutType;
        public readonly string? TitleKey, TitleKeyController;
        public readonly List<CLevelMessagePage> Pages;
        public readonly CLevelTrigger DismissTrigger;
        public readonly bool ShouldPause, ShowScreenBG;
        public CLevelMessage(object? name, ELevelMessageLayoutType type, string? title,
            float delay, object? image, CLevelTrigger dismiss, object? trigger,
            List<CLevelMessagePage> pages, bool shouldPause, bool showScreenBG,
            object? context, string? titleController)
        {
            LayoutType = type; TitleKey = title; TitleKeyController = titleController;
            Pages = pages; DismissTrigger = dismiss; ShouldPause = shouldPause; ShowScreenBG = showScreenBG;
        }
    }
}
public sealed class GameObjectPort { public bool activeSelf = true; }
public sealed class TextPort { public readonly GameObjectPort gameObject = new(); public string text = ""; }
public static class NativeLocalization
{
    public static bool Controller;
    public static string Translate(string? key) => "native:" + GloomhavenVR.Core.Loc.CurrentLanguage + ":" + key;
}
public sealed class LevelMessageUILayout
{
    public readonly TextPort title = new();
    public CLevelMessage? _message;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Init(CLevelMessage message) { _message = message; OnLanguageChanged(); }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnLanguageChanged() => title.text = NativeLocalization.Translate(
        NativeLocalization.Controller ? _message?.TitleKeyController : _message?.TitleKey);
}
public sealed class LevelMessagePageUI
{
    public CLevelMessagePage? page;
    public readonly TextPort information = new();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnLanguageChanged() => information.text = NativeLocalization.Translate(
        NativeLocalization.Controller ? page?.PageTextKeyController : page?.PageTextKey);
}
