using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class HoverFixture : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    internal UINewEnhancementWindow Window = null!;
    internal AbilityCardUI Preview = null!;
    public void OnPointerEnter(PointerEventData data) { Window.SowingCard = Preview; Window.cardHolder.Card = Preview; }
    public void OnPointerExit(PointerEventData data) { Window.SowingCard = Window.selectedCard; Window.cardHolder.Card = Window.selectedCard; }
}

public static class InteractionProgram
{
    private class LegacyGrab : IGrabbable, IGrabbableHandFilter
    {
        internal int Releases;
        internal bool Allowed = true;
        public bool CanGrab => true;
        public bool GrabWithGrip => false;
        public bool AllowsHand(VRHand hand) => Allowed;
        public void OnGrab(VRHand hand) { }
        public void OnRelease(VRHand hand, Vector3 velocity) { Releases++; }
    }
    private sealed class CancellableGrab : LegacyGrab, IGrabCancellation
    {
        internal int Cancels;
        public void OnGrabCancelled(VRHand hand) { Cancels++; }
    }
    private static int _assertions;
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException(message); }
    private static Dictionary<Component, TownServiceToken> Tokens =>
        (Dictionary<Component, TownServiceToken>)typeof(TownServicePresentation).GetField("Tokens", Static)!.GetValue(null)!;
    private static object Context() => typeof(TownServicePresentation).GetMethod("SelectionContext", Static)!.Invoke(null, null)!;
    private static void Refresh() => typeof(TownServicePresentation).GetMethod("RefreshTokens", Static)!.Invoke(null, null);
    private static T Child<T>(string name, Transform parent) where T : Component => Probe.Go(name, parent).AddComponent<T>();
    private static Button ButtonOn(GameObject go) => go.AddComponent<Button>();

    private sealed class Session
    {
        internal UIWindow Window = null!;
        internal Component Slot = null!;
        internal Button Button = null!;
        internal int Clicks;
        internal TownServiceToken Token => Tokens[Slot];
        internal VRHand Hand = new();
        internal Transform OriginalParent = null!;
        internal void Grab()
        {
            Token.Tick(1);
            Check(Token.CanGrab, "sample is initially grabbable");
            Hand.Grabber.Grab(Token);
            Check(Token.HeldRoot != null, "grab creates held sample");
            var mat = (GameObject)typeof(TownServicePresentation).GetField("_mat", Static)!.GetValue(null)!;
            Token.HeldRoot!.position = mat.transform.TransformPoint(new Vector3(0, .05f, 0));
        }
        internal void Release()
        {
            Hand.TriggerUp = true; Hand.TriggerPressed = false;
            Hand.Grabber.ReleaseTick();
        }
    }

    private static Session Open(byte service)
    {
        new GameObject("events", typeof(EventSystem));
        Probe.Roots.Add(EventSystem.current.gameObject);
        var s = new Session { OriginalParent = Probe.Go("native-parent").transform };
        var root = Probe.Go("native-window", s.OriginalParent);
        if (service == 1)
        {
            var win = root.AddComponent<UIShopItemWindow>();
            win.ItemInventory = Child<UIShopItemInventory>("catalog", root.transform);
            var slot = Child<UIShopItemSlot>("item", win.ItemInventory.transform);
            s.Button = ButtonOn(slot.gameObject); slot.Selectable = s.Button;
            win.ItemInventory.slotPool.Add(slot); s.Slot = slot; s.Window = win;
        }
        else if (service == 2)
        {
            var win = root.AddComponent<UITempleWindow>();
            win.Shop = Child<TempleShop>("catalog", root.transform);
            var slot = Child<UITempleShopSlot>("blessing", win.Shop.transform);
            s.Button = ButtonOn(slot.gameObject); slot.button = s.Button;
            win.Shop.slots.Add(slot); s.Slot = slot; s.Window = win;
        }
        else
        {
            var win = root.AddComponent<UINewEnhancementWindow>();
            win.enhancementShop = Child<EnhancementShop>("enhancements", root.transform);
            win.cardHolder = Child<CardHolder>("card-holder", root.transform);
            win.CardsDisplay = Child<CardsDisplay>("cards", root.transform);
            win.CardsDisplay.abilityCardsPanel = (RectTransform)Probe.Go("card-scroll", win.CardsDisplay.transform).transform;
            var slot = Child<UIEnhanceCardSlot>("ability", win.CardsDisplay.abilityCardsPanel);
            slot.AbilityCard = new AbilityCardUI();
            s.Button = ButtonOn(slot.gameObject); slot.Selectable = s.Button;
            win.CardsDisplay.slotsPool.Add(slot); win.selectedCard = new AbilityCardUI();
            win.SowingCard = win.selectedCard; win.cardHolder.Card = win.selectedCard;
            var hover = slot.gameObject.AddComponent<HoverFixture>(); hover.Window = win; hover.Preview = slot.AbilityCard;
            s.Slot = slot; s.Window = win;
        }
        s.Button.onClick.AddListener(() => s.Clicks++);
        GuildmasterDestinations.Window = s.Window;
        GuildmasterDestinations.Mode = service == 1 ? EGuildmasterMode.Merchant : service == 2 ? EGuildmasterMode.Temple : EGuildmasterMode.Enchantress;
        ModalFallback.TryConvertWindow(s.Window);
        TownServicePresentation.Tick();
        Refresh(); // The production census is intentionally rate limited across openings.
        return s;
    }

    private static void Clean()
    {
        CanvasConversion.DeferParent = false; CanvasConversion.KeepActive = false;
        TownServiceSurface.FailAt = 0; ModalFallback.FailConvert = false;
        TownServicePresentation.Reset();
        typeof(TownServicePresentation).GetField("_failedWindow", Static)!.SetValue(null, null);
        Tokens.Clear(); ModalFallback.Converted.Clear(); CanvasConversion.ActivePanels.Clear();
        foreach (var root in Probe.Roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
        Probe.Roots.Clear(); Probe.Events.Clear(); GuildmasterDestinations.Window = null;
    }

    private static void IdentityChanges()
    {
        for (byte service = 1; service <= 3; service++)
        {
            var s = Open(service); s.Grab();
            if (service == 1) ((UIShopItemWindow)s.Window).ItemInventory.character = new object();
            else if (service == 2) ((UITempleWindow)s.Window).character = new object();
            else ((UINewEnhancementWindow)s.Window).character = new object();
            s.Token.Tick(1);
            Check(s.Token.HeldRoot == null, "owner switch cancels held selection");
            Check(s.Hand.Grabber.Heal() && s.Hand.Grabber.Held == null, "cancelled token releases grabber hand ownership");
            s.Release(); Check(s.Clicks == 0, "owner switch never clicks native selection"); Clean();
        }
        foreach (byte service in new byte[] { 1, 3 })
        {
            var s = Open(service); s.Grab();
            if (service == 1) ((UIShopItemWindow)s.Window).ItemInventory.mode++;
            else ((UINewEnhancementWindow)s.Window).mode++;
            s.Token.Tick(1); Check(s.Token.HeldRoot == null, "mode switch cancels held selection"); Clean();
        }
        var enhancement = Open(3); enhancement.Grab();
        ((UINewEnhancementWindow)enhancement.Window).selectedCard = new AbilityCardUI();
        enhancement.Token.Tick(1); Check(enhancement.Token.HeldRoot == null, "committed card switch cancels held selection"); Clean();

        var recycled = Open(3); recycled.Grab();
        // Native pool reuses the same AbilityCardUI for an equivalent card name/other owner.
        ((UIEnhanceCardSlot)recycled.Slot).AbilityCard!.AbilityCard = new object();
        recycled.Token.Tick(1); Check(recycled.Token.HeldRoot == null, "pooled underlying card switch cancels held selection"); Clean();

        var immediate = Open(1); immediate.Grab();
        ((UIShopItemWindow)immediate.Window).ItemInventory.character = new object();
        immediate.Release(); Check(immediate.Clicks == 0, "release rechecks context before next tick"); Clean();

        var sourceChanged = Open(3); sourceChanged.Grab();
        ((UIEnhanceCardSlot)sourceChanged.Slot).AbilityCard!.AbilityCard = new object();
        sourceChanged.Release(); Check(sourceChanged.Clicks == 0, "release rechecks pooled identity before next tick"); Clean();

        var expired = Open(1); expired.Grab();
        var sessionField = typeof(TownServicePresentation).GetField("_session", Static)!;
        sessionField.SetValue(null, (uint)sessionField.GetValue(null)! + 1);
        expired.Token.Tick(1);
        Check(expired.Token.HeldRoot == null && !expired.Token.CanGrab, "captured session mismatch cancels held selection"); Clean();

        var closed = Open(1); closed.Grab(); TownServiceToken old = closed.Token;
        uint session = TownServicePresentation.Session;
        closed.Window.Hide(); closed.Window.IsOpen = true;
        ModalFallback.TryConvertWindow(closed.Window); TownServicePresentation.Tick(); Refresh();
        Check(TownServicePresentation.Session != session, "same-frame close reopen retires session");
        closed.Hand.TriggerUp = true; old.OnRelease(closed.Hand, Vector3.zero);
        Check(closed.Clicks == 0 && old.HeldRoot == null, "old session cannot select after reopening"); Clean();
    }

    private static void HoverAndRelease()
    {
        var s = Open(3); var win = (UINewEnhancementWindow)s.Window;
        object committed = Context();
        s.Token.OnGrabHighlight(s.Hand, true); s.Hand.Grabber.Highlighted = s.Token;
        Check(win.SowingCard != win.selectedCard, "native hover fixture previews another card");
        s.Grab(); s.Token.Tick(1);
        Check(s.Token.HeldRoot != null && ReferenceEquals(committed, Context()), "BeginGrab hover preserves committed selection context");
        // Tick moved the visual to the tracked hand; explicitly put it back over the mat.
        var mat = (GameObject)typeof(TownServicePresentation).GetField("_mat", Static)!.GetValue(null)!;
        s.Token.HeldRoot!.position = mat.transform.TransformPoint(new Vector3(0, .05f, 0));
        s.Release(); s.Token.OnRelease(s.Hand, Vector3.zero);
        Check(s.Clicks == 1, "real drop dispatches native button exactly once"); Clean();

        foreach (bool triggerUp in new[] { false, true })
        {
            s = Open(1); s.Grab(); s.Hand.TriggerUp = triggerUp;
            s.Hand.Grabber.CancelAll();
            Check(s.Clicks == 0, "synthetic cancel never clicks even on trigger-up frame"); Clean();
        }
        s = Open(1); s.Grab(); s.Hand.HasPose = false; s.Release();
        Check(s.Clicks == 0, "tracking loss never clicks native selection"); Clean();
        s = Open(1); s.Grab(); s.Token.HeldRoot!.position += new Vector3(3, 0, 0); s.Release();
        Check(s.Clicks == 0, "drop outside mat never clicks"); Clean();
        s = Open(1); s.Grab(); s.Button.interactable = false; s.Release();
        Check(s.Clicks == 0, "native disabled button never clicks"); Clean();
    }

    private static void Handoff()
    {
        var nativeParent = Probe.Go("home").transform;
        var window = Child<UIWindow>("window", nativeParent);
        ModalFallback.TryConvertWindow(window);
        var previous = ModalFallback.Converted[0].Panel;
        CanvasConversion.DeferParent = true;
        Check(!ModalFallback.ReleaseForTownService(window, previous), "deferred parent restoration refuses handoff");
        previous.Target.SetParent(Probe.Go("wrong-parent").transform, false);
        Check(!ModalFallback.ReleaseForTownService(window, previous), "wrong original parent refuses handoff");
        previous.Target.SetParent(nativeParent, false);
        CanvasConversion.KeepActive = true; CanvasConversion.ActivePanels.Add(previous);
        Check(!ModalFallback.ReleaseForTownService(window, previous), "active conversion owner refuses handoff");
        CanvasConversion.KeepActive = false; CanvasConversion.DeferParent = false;
        Check(ModalFallback.ReleaseForTownService(window, previous), "restored native home allows handoff");
        var position = new Vector3(2, 3, 4); var rotation = Quaternion.Euler(0, 35, 0);
        var restored = ModalFallback.RestoreTownServiceContext(window, position, rotation);
        var wp = ModalFallback.Converted[0];
        Check(restored != null && wp.Grab!.Position == position && wp.PoseRePlaceDone && wp.SpawnAnchor == default,
            "restored context retains pose without opening relocation"); Clean();

        window = Probe.Go("root-window").AddComponent<UIWindow>();
        ModalFallback.TryConvertWindow(window); previous = ModalFallback.Converted[0].Panel;
        previous.Target.SetParent(Probe.Go("unexpected-parent").transform, false);
        CanvasConversion.DeferParent = true;
        Check(!ModalFallback.ReleaseForTownService(window, previous), "null original parent refuses unrelated parent handoff");
        previous.Target.SetParent(null, false);
        Check(ModalFallback.ReleaseForTownService(window, previous), "restored scene-root target allows handoff"); Clean();
    }

    private static void CancellationCompatibility()
    {
        var hand = new VRHand { TriggerUp = true };
        var legacy = new LegacyGrab(); hand.Grabber.Grab(legacy); hand.Grabber.CancelAll();
        Check(legacy.Releases == 1 && hand.Grabber.Held == null, "legacy cancellation still invokes existing release once");
        legacy = new LegacyGrab(); hand.Grabber.Grab(legacy); legacy.Allowed = false;
        Check(hand.Grabber.Heal() && legacy.Releases == 1, "legacy hand healing still invokes existing release once");
        var cancellable = new CancellableGrab(); hand.Grabber.Grab(cancellable); cancellable.Allowed = false;
        Check(hand.Grabber.Heal() && cancellable.Cancels == 1 && cancellable.Releases == 0,
            "hand healing uses explicit cancellation even on trigger-up frame"); Clean();
    }

    private static void RollbackAndContinuation()
    {
        var s = Open(3);
        Probe.Events.Clear(); s.Window.Hide();
        var order = string.Join(",", Probe.Events);
        Check(order.IndexOf("release:native-window", StringComparison.Ordinal) >= 0
            && order.IndexOf("release:native-window", StringComparison.Ordinal) < order.IndexOf("release:card-scroll", StringComparison.Ordinal),
            "context retires before restoring descendant sections");
        Check(order.IndexOf("release:card-scroll", StringComparison.Ordinal) < order.IndexOf("release:card-holder", StringComparison.Ordinal)
            && order.IndexOf("release:card-holder", StringComparison.Ordinal) < order.IndexOf("release:enhancements", StringComparison.Ordinal),
            "section rollback restores native hierarchy in LIFO order");
        Check(!TownServicePresentation.Active && s.Window.transform.parent == s.OriginalParent
            && CanvasConversion.ActivePanels.Count == 0, "native hide immediately restores hierarchy and all owners");
        Check(Probe.Events.Contains("native-continuation") && !Probe.Events.Exists(x => x.StartsWith("sample:")),
            "native continuation completes without animation sampling"); Clean();

        // Failure after two sections: the actual production catch must unwind and re-enroll the
        // intact native context. The fixture throws before the third section takes ownership.
        TownServiceSurface.FailAt = 12; s = Open(3);
        Check(!TownServicePresentation.Active && CanvasConversion.ActivePanels.Count == 1,
            "partial section failure leaves exactly one native context owner");
        var win = (UINewEnhancementWindow)s.Window;
        Check(win.cardHolder.transform.parent == win.transform && win.enhancementShop.transform.parent == win.transform,
            "partial section failure restores previous sections");
        Check(ModalFallback.Converted.Count == 1 && ModalFallback.Converted[0].Panel.OriginalParent == s.OriginalParent,
            "fallback context remembers original native parent"); Clean();
    }

    public static int Run()
    {
        _assertions = 0;
        try { IdentityChanges(); HoverAndRelease(); CancellationCompatibility(); Handoff(); RollbackAndContinuation(); return _assertions; }
        finally { Clean(); }
    }
}
