using System;
using System.Collections.Generic;
using System.Reflection;
using Assets.Script.Misc;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using Script.GUI.SMNavigation;
using Script.GUI.SMNavigation.States.CampaignMapStates;
using Script.GUI.SMNavigation.States.PopupStates;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

internal static class Program
{
    private static int _checks, _matrixCases;
    private static NativePrompt? _current;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new InvalidOperationException("ASSERT: " + message); }
    private static void Set(object value, string field, object contents) => Access(value.GetType(), field).SetValue(value, contents);
    private static FieldInfo Access(Type type, string field) => type.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static T Read<T>(object value, string field) => (T)Access(value.GetType(), field).GetValue(value)!;
    private static void Patch(Type type, string method, params object[] args) => type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
    private static void Reset()
    {
        if (_current != null) MapRetirementPrompt.Replaced(_current.Presenter);
        _current = null;
        MapRoomDriver.Active = true; WorldUIConfig.ConversionActive = true; FlatScreen.ManualScreenActive = false;
        CanvasConversion.WorldCamera = new(); CanvasConversion.ThrowBeforeConvert = false; GrabbableModal.ThrowAfterConvert = false;
        CanvasConversion.Converts = CanvasConversion.Releases = 0; CanvasConversion.Last = null;
        VRLog.Warnings = VRLog.Notes = 0;
        Singleton<UIReadyToggle>.Instance = new(); Singleton<UIMapMultiplayerController>.Instance = new();
        Singleton<UIPersonalQuestResultManager>.Instance = new(); Singleton<UIRetirementManager>.Instance = new();
        Singleton<UINavigation>.Instance = new(); Singleton<KeyActionHandlerController>.Instance = new();
        Singleton<ESCMenu>.Instance = new(); Singleton<UIGuildmasterHUD>.Instance = new();
        Time.frameCount++;
    }
    private static NativePrompt Create(bool console)
    {
        var prompt = new NativePrompt(console);
        _current = prompt;
        Singleton<UIMapMultiplayerController>.Instance.GuildmasterConfirmAction = prompt.Presenter;
        return prompt;
    }
    private static CallbackPromise Begin(NativePrompt prompt, bool owner = false, bool optional = true)
    {
        var native = Singleton<UIMapMultiplayerController>.Instance.ConfirmRetirement(new CMapCharacter { IsUnderMyControl = owner }, new NetworkPlayer(), optional);
        if (optional && !owner) prompt.CaptureShown();
        return (CallbackPromise)native;
    }
    private static void OriginalDesktopFlow()
    {
        Reset(); NativePrompt prompt = Create(false); CallbackPromise pending = Begin(prompt);
        Check(pending.IsPending && Singleton<UIPersonalQuestResultManager>.Instance.Confirmations == 0, "optional observer promise waits for original press");
        Check(!MapRetirementPrompt.Visible, "hidden HUD alone has no VR retirement surface");
        GUIAnimator show = Read<GUIAnimator>(prompt.Source, "_showAnimation");
        Sprite nativeIcon = Read<Image>(prompt.Source, "_icon").sprite!;
        ExtendedButton originalButton = Read<ExtendedButton>(prompt.Source, "_button");
        MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible, "native pending retirement becomes reachable in VR");
        Check(prompt.Source.transform.IsChildOf(CanvasConversion.Last!.Target) && !ReferenceEquals(CanvasConversion.Last.Target, prompt.Source.transform), "conversion wraps exact native widget without cloning");
        Check(ReferenceEquals(nativeIcon, Read<Image>(prompt.Source, "_icon").sprite), "native icon presentation remains original");
        Check(Read<UITextTooltipTarget>(prompt.Source, "_tooltip").Text == "Original native retirement tooltip", "native tooltip is preserved");
        Check(originalButton.onClick.Count == 1 && show.IsPlaying && show.Stops == 0, "conversion preserves native animation and sole click listener");
        Check(pending.IsPending && Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 0, "showing never confirms optional retirement");
        Check(MapRetirementPrompt.OwnsGrab(GrabbableModal.Last!), "original retirement holder survives orphan sweep");
        prompt.PressOriginal();
        Check(Singleton<UIPersonalQuestResultManager>.Instance.Confirmations == 1 && Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 1, "original click alone enters native retirement barrier");
        Check(pending.IsPending && Singleton<UIRetirementManager>.Instance.Commits == 0, "VR click cannot bypass other native players");
        UIReadyToggle toggle = Singleton<UIReadyToggle>.Instance;
        Check(toggle.Type == UIReadyToggle.EReadyUpType.Player && toggle.ValidateDeparture, "original retirement player quorum and departure policy are retained");
        for (int i = 0; i < 3; i++) toggle.OtherNativePlayerReady();
        Check(!pending.IsPending && Singleton<UIRetirementManager>.Instance.Commits == 1, "all native Flat and VR retirement votes complete once");
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible && ReferenceEquals(prompt.Source.transform.parent, prompt.Hud.transform), "native hide restores original HUD hierarchy");
        Check(show.Stops == 1 && originalButton.onClick.Count == 0, "native hide retains original animation cleanup");
    }
    private static void RoleMatrix()
    {
        foreach (bool hostVr in new[] { false, true })
        foreach (bool receiverVr in new[] { false, true })
        foreach (bool owner in new[] { false, true })
        foreach (bool optional in new[] { false, true })
        foreach (bool console in new[] { false, true })
        {
            Reset(); NativePrompt prompt = Create(console); CallbackPromise pending = Begin(prompt, owner, optional);
            // Native ConfirmRetirement intentionally has no Flat/VR host branch. This
            // matrix executes that method with both presentation modes and native roles.
            MapRoomDriver.Active = receiverVr;
            MapRetirementPrompt.Tick(receiverVr);
            bool requiresClick = optional && !owner;
            string role = $"hostVR={hostVr}, receiverVR={receiverVr}, owner={owner}, optional={optional}, console={console}";
            Check(MapRetirementPrompt.Visible == (requiresClick && receiverVr), "role matrix native surface " + role);
            Check(Singleton<UIReadyToggle>.Instance.OwnReadyPresses == (requiresClick ? 0 : 1), "role matrix retains native optional branch " + role);
            if (requiresClick)
            {
                if (receiverVr && console) prompt.PressVr(); else prompt.PressOriginal();
                Check(Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 1, "role matrix actual receiver press reaches native barrier " + role);
            }
            for (int i = 0; i < 3; i++) Singleton<UIReadyToggle>.Instance.OtherNativePlayerReady();
            Check(!pending.IsPending && Singleton<UIRetirementManager>.Instance.Commits == 1, "role matrix native complete " + role);
            _matrixCases++;
        }
    }
    private static void StandingLifecycle()
    {
        Reset(); NativePrompt prompt = Create(false); CallbackPromise pending = Begin(prompt);
        MapRetirementPrompt.Tick(true);
        MapRetirementPrompt.Tick(false);
        Check(ReferenceEquals(prompt.Source.transform.parent, prompt.Hud.transform), "room-off restores original retirement hierarchy");
        Check(pending.IsPending && Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 0, "room-off does not resolve native promise");
        MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible && CanvasConversion.Converts == 2, "reenabling recovers standing prompt without Show replay");
        FlatScreen.ManualScreenActive = true; MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible && pending.IsPending, "flat takeover restores pending original prompt");
        FlatScreen.ManualScreenActive = false; MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible, "map recovers after flat takeover");
        WorldUIConfig.ConversionActive = false; MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible && pending.IsPending, "conversion-off preserves native pending state");
        WorldUIConfig.ConversionActive = true; MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible, "conversion-on recovers captured prompt");
        Patch(typeof(RetirementPromptReplacedPatch), "Prefix", prompt.Presenter);
        prompt.Presenter.ShowQuestSelectedAction(new CQuestState(), () => { });
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible && pending.IsPending, "ordinary quest action cannot inherit retirement capture");
    }
    private static void ConsolePermissions()
    {
        Reset(); NativePrompt prompt = Create(true); int calls = 0;
        prompt.Show(() => calls++); MapRetirementPrompt.Tick(true);
        Check(Singleton<KeyActionHandlerController>.Instance.Handlers.Count == 1, "console conversion preserves native keyboard registration");
        GameObject hit = prompt.Hit;
        Check(hit.layer == prompt.Source.gameObject.layer && hit.GetComponent<Image>()!.raycastTarget, "console hit surface inherits rendered layer");
        foreach (CampaignMapStateTag tag in Enum.GetValues<CampaignMapStateTag>())
        {
            Singleton<UINavigation>.Instance.StateMachine.Change(new CampaignMapState { StateTag = tag });
            Time.frameCount++;
            bool accepted = MapRetirementPrompt.TryPressConsole(hit);
            Check(accepted == (tag != CampaignMapStateTag.PartyPanel), "console native navigation tags gate actual press " + tag);
        }
        Singleton<UINavigation>.Instance.StateMachine.Change(new PopupState()); Time.frameCount++;
        Check(!MapRetirementPrompt.TryPressConsole(hit), "native popup blocker rejects VR press");
        Singleton<UINavigation>.Instance.StateMachine.Change(new CampaignMapState());
        Singleton<ESCMenu>.Instance.IsOpen = true;
        Check(!MapRetirementPrompt.TryPressConsole(hit), "ESC rejects console press");
        Singleton<ESCMenu>.Instance.IsOpen = false;
        FlatScreen.ManualScreenActive = true;
        Check(!MapRetirementPrompt.TryPressConsole(hit), "flat takeover rejects queued console press");
        FlatScreen.ManualScreenActive = false;
        Check(!MapRetirementPrompt.TryPressConsole(new GameObject("foreign")), "foreign hit cannot resolve native callback");
        Time.frameCount++; int before = calls;
        prompt.PressVr(); prompt.PressVr();
        Check(calls == before + 1, "console same-frame duplicate dispatch is bounded");
        // A native callback replaced without an observed Show edge must invalidate an
        // already queued click; a stale retirement surface cannot press another action.
        Set(prompt.Source, "_onConfirmCallback", (Action)(() => calls += 100)); Time.frameCount++;
        Check(!MapRetirementPrompt.TryPressConsole(hit) && calls == before + 1, "stale native callback identity rejects queued click");
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible, "stale callback removes original retirement surface");
        prompt.Show(() => calls++); MapRetirementPrompt.Tick(true); hit = prompt.Hit;
        Patch(typeof(RetirementPromptReplacedPatch), "Prefix", prompt.Presenter);
        prompt.Presenter.HideCharacterRetiredAction();
        Time.frameCount++;
        Check(!MapRetirementPrompt.TryPressConsole(hit) && !hit.activeSelf, "native hide immediately retires console click surface");
        Check(Singleton<KeyActionHandlerController>.Instance.Handlers.Count == 0, "native hide retains console hotkey cleanup");
    }
    private static void ConversionFailures()
    {
        Reset(); NativePrompt prompt = Create(true); CallbackPromise pending = Begin(prompt);
        CanvasConversion.WorldCamera = null; MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.Visible && pending.IsPending && MapRetirementPrompt.ScreenFallbackWanted, "active map camera failure exposes original pending HUD");
        Check(CanvasConversion.Converts == 0 && VRLog.Warnings == 1, "camera failure is reported once before hierarchy mutation");
        CanvasConversion.WorldCamera = new(); MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.ScreenFallbackWanted && pending.IsPending, "failed native opening retains stable fallback until original hide");
        FlatScreen.ManualScreenActive = true; MapRetirementPrompt.Tick(true); prompt.PressVr();
        Check(Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 1 && pending.IsPending, "camera fallback still requires original native player press");
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.ScreenFallbackWanted, "original camera-fallback prompt closure ends desktop request");

        Reset(); prompt = Create(true); pending = Begin(prompt);
        CanvasConversion.WorldCamera = new(); GrabbableModal.ThrowAfterConvert = true;
        MapRetirementPrompt.Tick(true);
        Check(ReferenceEquals(prompt.Source.transform.parent, prompt.Hud.transform) && pending.IsPending, "partial conversion failure restores original pending widget");
        Check(VRLog.Warnings == 1 && CanvasConversion.Releases == 1, "failed conversion logs once and releases host");
        Check(MapRetirementPrompt.ScreenFallbackWanted, "actual conversion failure exposes original pending HUD");
        GameObject originalHit = prompt.Hit;
        FlatScreen.ManualScreenActive = true;
        for (int i = 0; i < 5; i++) MapRetirementPrompt.Tick(true);
        Check(VRLog.Warnings == 1 && CanvasConversion.Converts == 1, "failed standing prompt does not retry each frame");
        Check(ReferenceEquals(originalHit, prompt.Hit) && prompt.Hit.layer == prompt.Source.gameObject.layer, "desktop fallback preserves stable original console hit surface");
        prompt.PressVr();
        Check(Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 1 && pending.IsPending, "fallback console HUD preserves real press and native barrier");
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.ScreenFallbackWanted, "original prompt closure ends failure fallback");
        FlatScreen.ManualScreenActive = false;
        GrabbableModal.ThrowAfterConvert = false;
        prompt.Show(() => { }); MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible, "new native Show recovers after conversion failure");
        Reset(); prompt = Create(false); pending = Begin(prompt);
        GrabbableModal.ThrowAfterConvert = true; MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.ScreenFallbackWanted, "desktop original failure requests native screen");
        FlatScreen.ManualScreenActive = true; MapRetirementPrompt.Tick(true); prompt.PressOriginal();
        Check(Singleton<UIReadyToggle>.Instance.OwnReadyPresses == 1 && pending.IsPending, "desktop fallback preserves original ExtendedButton callback");
        MapRetirementPrompt.Tick(true);
        Check(!MapRetirementPrompt.ScreenFallbackWanted && FlatScreen.ManualScreenActive, "retirement closure releases only its request and leaves external desktop policy untouched");
        Reset(); prompt = Create(true); pending = Begin(prompt);
        GrabbableModal.ThrowAfterConvert = true; MapRetirementPrompt.Tick(true); MapRetirementPrompt.Tick(false);
        Check(!MapRetirementPrompt.ScreenFallbackWanted && pending.IsPending, "disabled map presentation releases fallback without resolving native prompt");
        MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.ScreenFallbackWanted, "reenabling standing failed prompt recovers native desktop request");
    }
    private static void EarlySeams()
    {
        MapRetirementPrompt.Install(); MapRetirementPrompt.Install();
        Check(VRSession.Harmony!.Installed.Count == 2, "retirement seams install once before map entry");
        var shown = (IEnumerable<MethodBase>)typeof(RetirementPromptShownPatch).GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        int count = 0; foreach (MethodBase method in shown) { count++; Check(method.Name == "ShowCharacterRetiredAction", "early capture binds actual native retirement Show"); }
        Check(count == 2, "both original native presenters are captured");
        var retired = (IEnumerable<MethodBase>)typeof(RetirementPromptReplacedPatch).GetMethod("TargetMethods", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        count = 0; foreach (MethodBase method in retired) { count++; Check(method != null, "retirement replacement native seam exists"); }
        Check(count == 8, "native hide and shared widget replacement invalidate capture");
        Reset(); NativePrompt prompt = Create(false); MapRoomDriver.Active = false; CallbackPromise pending = Begin(prompt);
        MapRetirementPrompt.Tick(true);
        MapRoomDriver.Active = true; MapRetirementPrompt.Tick(true);
        Check(MapRetirementPrompt.Visible && pending.IsPending, "early native Show survives later map activation");
    }
    private static void NativeRootAnimation()
    {
        Reset(); NativePrompt prompt = Create(false); CallbackPromise pending = Begin(prompt);
        var original = (RectTransform)prompt.Source.transform;
        original.anchorMin = original.anchorMax = new(1, 0);
        original.pivot = new(.2f, .8f); original.anchoredPosition3D = new(63, -42, 0);
        var nativeTween = new NativeScaleFrames(original);
        nativeTween.Frame(new(.1f, .1f, 1));
        MapRetirementPrompt.Tick(true);
        ConvertedPanel panel = CanvasConversion.Last!;
        // Execute the actual production conversion-frame maintenance at each native
        // animation frame. It must pin only the mod wrapper, not the native tween target.
        foreach (float frame in new[] { .14f, .3f, .7f, 1f })
        {
            nativeTween.Frame(new(frame, frame, 1));
            original.anchoredPosition3D = new(63 + frame * 10, -42, 0);
            panel.Target.localScale = new(2, 2, 2);
            CanvasConversion.ReassertForFixture(panel);
            Check(Math.Abs(original.localScale.x - frame) < .001f && Math.Abs(original.localScale.y - frame) < .001f,
                "native root tween survives actual conversion frame maintenance");
            Check(Math.Abs(original.anchoredPosition3D.x - (63 + frame * 10)) < .001f && original.anchorMin.x == 1 && original.pivot.x == .2f,
                "native animated rect retains original local geometry");
            Check(panel.Target.localScale.x == 1 && pending.IsPending, "wrapper alone is maintained while native choice waits");
        }
        MapRetirementPrompt.Reset();
        Check(ReferenceEquals(original.parent, prompt.Hud.transform) && original.GetSiblingIndex() == 0,
            "wrapper teardown restores native parent and sibling order");
        Check(original.localScale.x == 1 && original.anchoredPosition3D.x == 73 && original.anchorMin.x == 1 && original.pivot.x == .2f,
            "teardown preserves native animation finish instead of resetting opening frame");
        Check(!prompt.Hud.Destroyed && !prompt.Source.gameObject.Destroyed && panel.Target.gameObject.Destroyed,
            "only mod wrapper is destroyed after verified native detach");
        Check(prompt.Source.gameObject.layer == 0 && panel.HostGo.Destroyed,
            "wrapper teardown restores native layer and releases conversion host");

        Reset(); prompt = Create(true); Begin(prompt); MapRetirementPrompt.Tick(true);
        panel = CanvasConversion.Last!;
        prompt.Source.transform.RefuseNextDetach = true;
        Patch(typeof(RetirementPromptReplacedPatch), "Prefix", prompt.Presenter);
        prompt.Presenter.HideCharacterRetiredAction();
        Check(!panel.Target.gameObject.Destroyed && !prompt.Source.gameObject.Destroyed,
            "activation-time detach refusal retains original and wrapper alive");
        MapRoomDriver.Active = false; MapRetirementPrompt.Tick(false); MapRetirementPrompt.LateTick();
        Check(ReferenceEquals(prompt.Source.transform.parent, prompt.Hud.transform) && panel.Target.gameObject.Destroyed && !prompt.Source.gameObject.Destroyed,
            "normal inactive-room frame retries verified native hierarchy restore");
    }
    public static int Main()
    {
        try
        {
            EarlySeams(); NativeRootAnimation(); OriginalDesktopFlow(); RoleMatrix(); StandingLifecycle(); ConsolePermissions(); ConversionFailures();
            Reset(); System.Console.WriteLine($"map retirement: {_checks} checks passed, {_matrixCases} Flat/VR role/presenter cases"); return 0;
        }
        catch (Exception error) { System.Console.Error.WriteLine(error); return 1; }
    }
    private sealed class NativePrompt
    {
        public readonly GameObject Hud = new("Original HUD");
        public readonly UIGuildmasterConfirmActionPresenter Presenter;
        public readonly MonoBehaviour Source;
        private readonly bool _console;
        public NativePrompt(bool console)
        {
            _console = console;
            var original = new GameObject("Original native prompt"); original.transform.SetParent(Hud.transform, false); original.SetActive(false);
            var presenter = new GameObject("Original native presenter"); presenter.transform.SetParent(Hud.transform, false);
            if (console)
            {
                Source = original.AddComponent<UIGuildmasterConfirmActionPopup>();
                Presenter = presenter.AddComponent<UIGuildmasterConfirmActionPopupPresenter>();
                Set(Presenter, "_popup", Source); Set(Source, "_hotkey", new Script.GUI.SMNavigation.HotkeysBehaviour.Hotkey());
                Set(Source, "_longPressHandler", new LongPressHandler()); Set(Source, "_keyAction", KeyAction.Confirm);
                Set(Source, "_header", Child<TMPro.TMP_Text>(original, "Native header")); Set(Source, "_message", Child<TMPro.TMP_Text>(original, "Native message"));
            }
            else
            {
                Source = original.AddComponent<UIGuildmasterConfirmActionButton>(); Presenter = presenter.AddComponent<UIGuildmasterConfirmActionButtonPresenter>();
                Set(Presenter, "_button", Source); Set(Source, "_highlight", Child<Image>(original, "Native highlight"));
                Set(Source, "_button", Child<ExtendedButton>(original, "Native button")); Set(Source, "_tooltip", Child<UITextTooltipTarget>(original, "Native tooltip"));
                Set(Source, "_showAnimation", Child<GUIAnimator>(original, "Native show animation")); Set(Source, "_highlightAnimation", Child<GUIAnimator>(original, "Native highlight animation"));
            }
            Set(Source, "_icon", Child<Image>(original, "Native icon"));
        }
        private static T Child<T>(GameObject parent, string name) where T : Component, new()
        { var child = new GameObject(name); child.transform.SetParent(parent.transform, false); return child.AddComponent<T>(); }
        public void CaptureShown() => Patch(typeof(RetirementPromptShownPatch), "Postfix", Presenter, Read<Action>(Source, "_onConfirmCallback"));
        public void Show(Action callback)
        {
            Patch(typeof(RetirementPromptShownPatch), "Prefix", Presenter);
            Presenter.ShowCharacterRetiredAction(new CMapCharacter(), new NetworkPlayer(), callback); CaptureShown();
        }
        public GameObject Hit => Source.transform.Children.Find(x => x.gameObject.name == "MapRetirementClick")!.gameObject;
        public void PressVr() => Hit.GetComponent<RetirementPromptClick>()!.OnPointerClick(new PointerEventData());
        public void PressOriginal()
        {
            if (_console)
            {
                KeyActionHandler handler = Singleton<KeyActionHandlerController>.Instance.Handlers[0];
                if (!handler.Blocker.IsBlock) handler.Callback();
            }
            else Read<ExtendedButton>(Source, "_button").onClick.Invoke();
        }
    }
}
