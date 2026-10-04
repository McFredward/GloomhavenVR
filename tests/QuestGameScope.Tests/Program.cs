using System;
using System.Reflection;
using GloomhavenVR.Quest;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using GloomhavenVR.Core;

static class Program
{
    static int assertions;
    static QuestGameScope Reset(SystemLanguage language = SystemLanguage.English)
    {
        Resources.All.Clear(); Debug.Logs.Clear(); Debug.Errors.Clear();
        Resources.GlobalEnumerations = 0; Resources.TargetedQueries.Clear(); Resources.TargetedObjects = 0; SceneManager.Reset();
        QuestStandalonePlatform.Enabled = true; QuestStandalonePlatform.DebugLogging = false;
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
        Check(!options.Guildmaster.IsInteractable && Tooltip(options.Guildmaster.gameObject)!.CanBeShown, "original Guildmaster exclusion retained");
        Check(!workshop.IsInteractable && workshop.TooltipEnabled, "original Workshop identity excluded");
        Check(credits.IsInteractable && !credits.TooltipEnabled, "ordinary suboption ignores Workshop display label");
        Check(!voice.NativeButton.interactable && Tooltip(voice.NativeButton.gameObject) != null, "voice diagnostic exclusion retained");
        Check(create.ConfirmButton.interactable, "campaign DLC step remains under native ownership");
        workshop.Bind(new GLOOM.MainMenu.MenuSuboption("GUI_MULTIPLAYER")); workshop.IsInteractable = true;
        Tick(scope, .1f); Check(workshop.IsInteractable, "pooled Workshop exclusion cannot leak into multiplayer");
        NoErrors("existing scope");
    }
    static void GuildmasterAttachedTooltip()
    {
        var scope = Reset(); var options = new GLOOM.MainMenu.UIMainOptionsMenu();
        var button = options.Guildmaster; var tooltip = Tooltip(button.gameObject)!;
        button.Bind(new GLOOM.MainMenu.MenuSuboption("GUI_GUILDMASTER"));
        object? nativeCallback = button.CallbackIdentity;
        button.SetTooltip(true, "unreachable old scope text");
        Check(tooltip.ShownTooltipText == null && tooltip.tooltipText == "GUI_MAIN_MENU_GUILDMASTER_TOOLTIP",
            "original null menu tooltip pointer reproduces silent SetTooltip no-op");
        Check(tooltip.CanBeShown, "authored original Guildmaster description initially visible");
        Tick(scope);
        Check(ReferenceEquals(tooltip, Tooltip(button.gameObject)), "exact original Guildmaster tooltip reused");
        Check(button.gameObject.ComponentCount<UITextTooltipTarget>() == 1, "Guildmaster does not create competing tooltip component");
        Check(tooltip.ShownTooltipText == "Guildmaster is currently unavailable in the Quest standalone version.", "Guildmaster Quest explanation reaches actual attached target");
        Check(tooltip.tooltipText == "GUI_MAIN_MENU_GUILDMASTER_TOOLTIP", "authored localization key remains intact under runtime override");
        Check(!button.IsInteractable && tooltip.CanBeShown, "Guildmaster remains gray with original native hover available");
        Check(ReferenceEquals(button.CallbackIdentity, nativeCallback), "Guildmaster native content callback untouched");
        for (int i = 0; i < 128; i++) Tick(scope, .001f * i);
        Check(tooltip.SetTextCalls == 1, "stable Guildmaster explanation not refreshed every frame");
        Application.systemLanguage = SystemLanguage.German; Tick(scope, .2f);
        Check(tooltip.ShownTooltipText == "Guildmaster ist in der Quest-Standalone-Version derzeit nicht verfügbar.", "Guildmaster Quest explanation German");
        Check(tooltip.SetTextCalls == 2 && tooltip.CanBeShown, "Guildmaster language change refreshes existing native target once");
        button.IsInteractable = true; Tick(scope, .3f);
        Check(!button.IsInteractable && tooltip.CanBeShown, "native Guildmaster reenable retains exclusion and actual tooltip");
        NoErrors("Guildmaster attached tooltip");

        scope = Reset(); options = new GLOOM.MainMenu.UIMainOptionsMenu();
        UnityEngine.Object.Destroy(Tooltip(options.Guildmaster.gameObject)!);
        for (int i = 0; i < 64; i++) Tick(scope, .001f * i);
        Check(Debug.Errors.Count == 1 && Debug.Errors[0].Contains("Original Guildmaster attached tooltip binding is missing."),
            "changed Guildmaster scene binding emits one explicit original ABI failure");
        Check(Tooltip(options.Guildmaster.gameObject) == null, "changed Guildmaster scene does not create substitute tooltip");
    }
    static void DiscoveryBoundaries()
    {
        var scope = Reset();
        var asset = new UIPromotionDLCSlot(); asset.gameObject.scene = new Scene(false, false);
        var inactive = new UIPromotionDLCSlot(); inactive.gameObject.SetActive(false);
        var future = new FutureNativeOption(); future.NativeButton.interactable = false;
        object futureCallback = new object(); future.Callback = futureCallback;
        Tick(scope);
        Check(Resources.GlobalEnumerations == 0, "scope never enumerates broad imported asset population");
        Check(Resources.TargetedQueries.Count == 7 && !Resources.TargetedQueries.Contains(typeof(MonoBehaviour)), "scope queries only seven exact original owner types");
        Check(Resources.TargetedObjects <= 2, "scope transfers only native target candidates to managed code");
        Check(asset.NativeButton.interactable, "non-scene prefab purchase content remains untouched");
        Check(!inactive.NativeButton.interactable, "inactive original purchase widget discovered");
        Check(!future.NativeButton.interactable && ReferenceEquals(future.Callback, futureCallback), "future native option stays under original ownership");

        var late = new UIPromotionDLCSlot(); Tick(scope, .5f);
        Check(late.NativeButton.interactable, "discovery cadence remains one second");
        Tick(scope, 1.01f);
        Check(!late.NativeButton.interactable, "delayed native widget discovered on next ordinary scan");
        var roots = new System.Collections.Generic.List<MonoBehaviour>();
        var scratch = new System.Collections.Generic.List<MonoBehaviour>();
        var discovery = new QuestSceneObjects(scope); discovery.Collect(roots, scratch);
        Check(roots.Contains(inactive) && !roots.Contains(asset), "scene discovery includes inactive and excludes imported assets");
        int once = roots.FindAll(item => ReferenceEquals(item, scope)).Count;
        Check(once == 1, "anchor scene is not enumerated twice");
        Scene extra = SceneManager.AddScene(); var extraWidget = new FutureNativeOption(); extraWidget.gameObject.MoveToScene(extra);
        discovery.Collect(roots, scratch); Check(roots.Contains(extraWidget), "additive scene native widget discovered");
        SceneManager.Unload(extra); discovery.Collect(roots, scratch);
        Check(!roots.Contains(extraWidget), "unloaded scene no longer participates in discovery");
        scope.gameObject.MoveToScene(SceneManager.Persistent);
        var persistentWidget = new FutureNativeOption(); persistentWidget.gameObject.MoveToScene(SceneManager.Persistent);
        discovery.Collect(roots, scratch);
        Check(roots.Contains(scope) && roots.Contains(persistentWidget), "persistent native roots discovered without global assets scan");
        for (int i = 0; i < 8; i++) discovery.Collect(roots, scratch);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) discovery.Collect(roots, scratch);
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "steady loaded-scene discovery reuses root and component buffers");

        scope = Reset(); var desktop = new UIPromotionDLCSlot(); QuestStandalonePlatform.Enabled = false;
        Tick(scope); Check(desktop.NativeButton.interactable && Resources.GlobalEnumerations == 0 && Resources.TargetedQueries.Count == 0,
            "ordinary desktop scope leaves native UI and discovery untouched");
        QuestStandalonePlatform.Enabled = true; int scans = QuestGameScope.DiscoveryScans;
        Tick(scope, 1); Check(QuestGameScope.DiscoveryScans == scans, "normal logging does not sample discovery clocks");
        QuestStandalonePlatform.DebugLogging = true; Tick(scope, 2.01f);
        Check(QuestGameScope.DiscoveryScans == scans + 1 && QuestGameScope.SceneComponentCount > 0
            && QuestGameScope.DiscoveryWorstMs >= QuestGameScope.DiscoveryLastMs, "Debug discovery timing prices actual scoped work only");
        QuestStandalonePlatform.DebugLogging = false;
        NoErrors("scene discovery");
        TargetedBoundaries();
    }
    static void TargetedBoundaries()
    {
        var scope = Reset();
        var inactive = new UIPromotionDLCSlot(); inactive.gameObject.SetActive(false);
        var subclass = new FuturePromotion();
        var late = new GLOOM.MainMenu.UILoadGameSlot(); late.NativeLoad.TextLanguageKey = "Consoles/LEARN_MORE";
        var persistent = new UIDLCSelectorOption(); persistent.gameObject.MoveToScene(SceneManager.Persistent);
        persistent.PurchasePanel.SetActive(true);
        var additive = new UIPromotionDLCSlot(); Scene extra = SceneManager.AddScene(); additive.gameObject.MoveToScene(extra);
        for (int index = 0; index < 14338; index++) _ = new FutureNativeOption();
        Tick(scope);
        Check(!inactive.NativeButton.interactable && !late.NativeLoad.interactable && !persistent.NativeGamepadToggle.interactable
            && !additive.NativeButton.interactable, "targeted scope preserves inactive late additive persistent exclusions");
        Check(subclass.NativeButton.interactable, "targeted scope excludes future native subclasses from exact owner binding");
        Check(Resources.TargetedObjects == 5, "targeted query crosses managed boundary only for matching candidates");
        SceneManager.Unload(extra);
        var targeted = new QuestScopeObjects(); var actual = new System.Collections.Generic.List<MonoBehaviour>();
        targeted.Collect(actual);
        Check(!actual.Contains(additive), "targeted discovery drops unloaded scene owners");
        Check(actual.Count == 3 && targeted.LastNativeObjectCount == 5, "targeted result filters exact identities without broad scene census");
        var delayed = new UIPromotionDLCSlot(); delayed.gameObject.SetActive(false); Tick(scope, .5f);
        Check(delayed.NativeButton.interactable, "targeted discovery retains ordinary delayed-spawn cadence");
        Tick(scope, 1.01f); Check(!delayed.NativeButton.interactable, "targeted discovery finds new inactive owners without lifecycle hooks");
        UnityEngine.Object.Destroy(delayed);
        targeted.Collect(actual); Check(!actual.Contains(delayed), "targeted discovery drops destroyed owners");
        bool rejected = false;
        try { _ = new QuestScopeObjects(new[] { typeof(GameObject) }); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("MonoBehaviour ABI"); }
        Check(rejected, "targeted discovery validates exact owner component ABI once");
        NoErrors("targeted discovery");
    }
    static void DiscoveryAbiFailure()
    {
        var scope = Reset(); var native = new UIPromotionDLCSlot();
        var names = (string[])typeof(QuestScopeObjects).GetField("NativeNames", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        string original = names[0];
        try
        {
            names[0] = "Original.ScopeOwnerRenamedByAChangedGame";
            Tick(scope);
            Check(Debug.Errors.Count == 1 && Debug.Errors[0].Contains("scope-discovery")
                && Debug.Errors[0].Contains(names[0]), "changed original scope owner ABI fails visibly once");
            Check(Resources.TargetedQueries.Count == 0 && native.NativeButton.interactable, "invalid scope ABI never guesses substitute native owner bindings");
            for (int index = 1; index < 16; index++) Tick(scope, index * 1.01f);
            Check(Debug.Errors.Count == 1, "invalid scope ABI failure logging remains bounded across repeated scans");
            Check((bool)typeof(QuestGameScope).GetField("discoveryAttempted", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope)!,
                "original scope owner ABI validation remains one attempt per adapter");
        }
        finally { names[0] = original; }
    }
    public sealed class FuturePromotion : UIPromotionDLCSlot { }

    public sealed class FutureNativeOption : MonoBehaviour
    {
        public Button NativeButton = new Button();
        public object? Callback;
    }
    public static int Main()
    {
        try
        {
            AdsAndLocalization(); SavePooling(); ExistingTooltipOwnership(); SelectorPooling(); ExactExistingScope(); GuildmasterAttachedTooltip(); DiscoveryBoundaries(); DiscoveryAbiFailure();
            Console.WriteLine("PASS Quest native purchase scope: " + assertions + " behavioral assertions");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error.Message); return 1; }
    }
}
