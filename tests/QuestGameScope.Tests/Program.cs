using System;
using System.Reflection;
using GloomhavenVR.Quest;
using UnityEngine;
using UnityEngine.UI;

static class Program
{
    static int assertions;
    static QuestGameScope Reset(SystemLanguage language = SystemLanguage.English)
    {
        Resources.All.Clear(); Debug.Logs.Clear(); Debug.Errors.Clear();
        Application.systemLanguage = language; Time.unscaledTime = 0;
        return new QuestGameScope();
    }
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception("FAIL " + message); }
    static void Tick(QuestGameScope scope, float time = 0)
    {
        Time.unscaledTime = time;
        typeof(QuestGameScope).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scope, null);
    }
    static UITextTooltipTarget? Tooltip(GameObject host) => host.GetComponent<UITextTooltipTarget>();
    static void NoErrors(string context) => Check(Debug.Errors.Count == 0, context + " reflected original ABI succeeds");
    static void AdsAndLocalization()
    {
        foreach (bool inherited in new[] { false, true })
        {
            var scope = Reset();
            UIPromotionDLCSlot ad = inherited ? new UIBuyDLCSlot() : new UIPromotionDLCSlot();
            object image = ad.PromotionImage, title = ad.OriginalTitle;
            int calls = 0; Action callback = () => calls++; ad.NativeButton.onClick = callback;
            Tick(scope);
            Check(!ad.NativeButton.interactable, "inherited private promotion button disabled");
            var tooltip = Tooltip(ad.NativeButton.gameObject);
            Check(tooltip != null && tooltip.TooltipEnabled, "native purchase tooltip attached");
            Check(tooltip!.CanBeShown, "native hover remains available on disabled purchase button");
            Check(tooltip.ShownTooltipText == "Buy this DLC on your PC, then rebuild the Quest APK.", "purchase tooltip English text");
            Check(tooltip.RefreshRequested && tooltip.Subtext == null, "original SetText refresh ABI retained");
            Check(ReferenceEquals(image, ad.PromotionImage) && ReferenceEquals(title, ad.OriginalTitle), "native promo image and title retained");
            Check(ReferenceEquals(callback, ad.NativeButton.onClick), "native promo callback retained");
            ad.NativeButton.Press(); Check(calls == 0, "gray purchase button cannot invoke click");
            for (int i = 0; i < 100; i++) Tick(scope, .01f);
            Check(tooltip.SetTextCalls == 1, "stable purchase tooltip is not refreshed per frame");
            Check(ad.NativeButton.gameObject.ComponentCount<UITextTooltipTarget>() == 1, "purchase tooltip deduplicated");
            Application.systemLanguage = SystemLanguage.German; Tick(scope, .02f);
            Check(tooltip.ShownTooltipText == "Kaufe diesen DLC auf deinem PC und baue anschließend die Quest-APK neu.", "purchase tooltip German text");
            Check(tooltip.SetTextCalls == 2, "language change refreshes once");
            ad.NativeButton.interactable = true; Tick(scope, .03f);
            Check(!ad.NativeButton.interactable, "native promotion reenable is excluded again");
            NoErrors("promotion");
        }
    }
    static void SavePooling()
    {
        var scope = Reset(); var row = new GLOOM.MainMenu.UILoadGameSlot();
        int calls = 0; Action native = () => calls++; row.NativeLoad.onClick = native;
        row.NativeLoad.TextLanguageKey = "GUI_LOAD";
        row.name = "DLC kaufen"; row.NativeLoad.name = "Consoles/LEARN_MORE";
        Tick(scope);
        Check(row.NativeLoad.interactable && Tooltip(row.NativeLoad.gameObject) == null, "ordinary load ignores display name spoof");
        row.NativeLoad.TextLanguageKey = "Consoles/LEARN_MORE"; Tick(scope, 1.1f);
        var tooltip = Tooltip(row.NativeLoad.gameObject);
        Check(!row.NativeLoad.interactable && tooltip != null && tooltip.CanBeShown, "missing DLC save purchase disabled with native tooltip");
        Check(row.DeleteButton.interactable, "delete save availability unchanged");
        Check(ReferenceEquals(row.NativeLoad.onClick, native), "load callback preserved during purchase exclusion");
        row.NativeLoad.TextLanguageKey = "GUI_LOAD"; Tick(scope, 1.11f);
        Check(row.NativeLoad.interactable, "pooled load row restores captured native interaction immediately");
        Check(Tooltip(row.NativeLoad.gameObject) == null && tooltip!.Destroyed, "pooled load removes only added tooltip");
        row.NativeLoad.Press(); Check(calls == 1, "rebound ordinary load callback executes");
        row.NativeLoad.TextLanguageKey = "Consoles/LEARN_MORE"; Tick(scope, 2.2f);
        Check(!row.NativeLoad.interactable, "pooled missing DLC purchase can bind again");
        row.NativeLoad.TextLanguageKey = "GUI_LOAD"; row.NativeLoad.interactable = true; Tick(scope, 2.21f);
        Check(row.NativeLoad.interactable && Tooltip(row.NativeLoad.gameObject) == null, "native rebind interaction already restored remains untouched");
        NoErrors("save pool");
    }
    static void ExistingTooltipOwnership()
    {
        foreach (bool changedByNative in new[] { false, true })
        {
            var scope = Reset(); var row = new GLOOM.MainMenu.UILoadGameSlot();
            var tooltip = row.NativeLoad.gameObject.AddComponent<UITextTooltipTarget>();
            tooltip.TooltipEnabled = false; tooltip.SetText("Original missing DLC information");
            row.NativeLoad.TextLanguageKey = "Consoles/LEARN_MORE"; Tick(scope);
            Check(ReferenceEquals(Tooltip(row.NativeLoad.gameObject), tooltip), "original tooltip component reused");
            Check(tooltip.TooltipEnabled, "existing tooltip temporarily enabled");
            row.NativeLoad.TextLanguageKey = "GUI_LOAD";
            if (changedByNative) { tooltip.SetText("New native load information"); tooltip.TooltipEnabled = true; }
            Tick(scope, .1f);
            Check(!tooltip.Destroyed && ReferenceEquals(Tooltip(row.NativeLoad.gameObject), tooltip), "native tooltip never destroyed");
            Check(tooltip.ShownTooltipText == (changedByNative ? "New native load information" : "Original missing DLC information"), "rebound native tooltip text preserved");
            Check(tooltip.TooltipEnabled == changedByNative, "native tooltip enable state restored without overwriting new author");
            NoErrors("existing tooltip");
        }
        var disabledScope = Reset(); var disabled = new GLOOM.MainMenu.UILoadGameSlot();
        disabled.NativeLoad.interactable = false; disabled.NativeLoad.TextLanguageKey = "Consoles/LEARN_MORE";
        Tick(disabledScope); disabled.NativeLoad.TextLanguageKey = "GUI_LOAD"; Tick(disabledScope, .1f);
        Check(!disabled.NativeLoad.interactable, "already disabled native row stays disabled on release");
    }
    static void SelectorPooling()
    {
        var scope = Reset(); var row = new UIDLCSelectorOption(); object image = row.PromotionImage, description = row.OriginalDescription;
        row.NativeGamepadToggle.isOn = true; row.name = "Purchase DLC"; Tick(scope);
        Check(row.NativeGamepadToggle.interactable && Tooltip(row.PurchasePanel) == null, "owned DLC remains selectable");
        row.PurchasePanel.SetActive(true); Tick(scope, 1.1f);
        var tooltip = Tooltip(row.PurchasePanel);
        Check(!row.NativeGamepadToggle.interactable, "missing DLC gamepad purchase disabled");
        Check(tooltip != null && tooltip.CanBeShown, "native tooltip attaches to active purchase panel");
        Check(Tooltip(row.NativeGamepadToggle.gameObject) == null, "hidden toggle is not the tooltip hit surface");
        Check(row.NativeGamepadToggle.isOn, "native DLC selection value retained");
        Check(row.MouseToggle.interactable, "separate native mouse toggle availability untouched");
        Check(ReferenceEquals(image, row.PromotionImage) && ReferenceEquals(description, row.OriginalDescription), "DLC promo image and description retained");
        row.PurchasePanel.SetActive(false); Tick(scope, 1.11f);
        Check(row.NativeGamepadToggle.interactable, "pooled owned DLC toggle restored immediately");
        Check(Tooltip(row.PurchasePanel) == null && tooltip!.Destroyed, "pooled selector removes only added tooltip");
        NoErrors("selector pool");
    }
    static void ExactExistingScope()
    {
        var scope = Reset(); var options = new GLOOM.MainMenu.UIMainOptionsMenu();
        var workshop = new GLOOM.MainMenu.UIMainMenuSuboption(); workshop.Bind(new GLOOM.MainMenu.MenuSuboption("GUI_MODDING"));
        var credits = new GLOOM.MainMenu.UIMainMenuSuboption(); credits.Bind(new GLOOM.MainMenu.MenuSuboption("GUI_CREDITS"));
        credits.name = "Steam Workshop";
        var voice = new VoiceChat.VoceChatOptions();
        var create = new GLOOM.MainMenu.UICreateGameDLCStep();
        Tick(scope);
        Check(!options.Guildmaster.IsInteractable && options.Guildmaster.TooltipEnabled, "original Guildmaster exclusion retained");
        Check(!workshop.IsInteractable && workshop.TooltipEnabled, "original Workshop identity excluded");
        Check(credits.IsInteractable && !credits.TooltipEnabled, "ordinary suboption ignores Workshop display label");
        Check(!voice.NativeButton.interactable && Tooltip(voice.NativeButton.gameObject) != null, "voice diagnostic exclusion retained");
        Check(create.ConfirmButton.interactable, "campaign DLC step remains under native ownership");
        workshop.Bind(new GLOOM.MainMenu.MenuSuboption("GUI_MULTIPLAYER")); workshop.IsInteractable = true;
        Tick(scope, .1f); Check(workshop.IsInteractable, "pooled Workshop exclusion cannot leak into multiplayer");
        NoErrors("existing scope");
    }
    public static int Main()
    {
        try
        {
            AdsAndLocalization(); SavePooling(); ExistingTooltipOwnership(); SelectorPooling(); ExactExistingScope();
            Console.WriteLine("PASS Quest native purchase scope: " + assertions + " behavioral assertions");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error.Message); return 1; }
    }
}
