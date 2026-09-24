using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Cards;
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

public sealed class DecisionHoverProbe : MonoBehaviour, IPointerEnterHandler
{
    public int Entries;
    public void OnPointerEnter(PointerEventData data) { Entries++; }
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
    private static readonly List<Texture2D> Textures = new();
    private static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Check(bool value, string message)
    { _assertions++; if (!value) throw new InvalidOperationException(message); }
    private static Dictionary<Component, TownServiceToken> Tokens =>
        (Dictionary<Component, TownServiceToken>)typeof(TownServicePresentation).GetField("Tokens", Static)!.GetValue(null)!;
    private static object Context() => typeof(TownServicePresentation).GetMethod("SelectionContext", Static)!.Invoke(null, null)!;
    private static void Refresh()
    {
        if (TownServicePresentation.Catalog != null) TownServicePresentation.Catalog.Tick();
        else TownServicePresentation.Ritual?.Tick();
    }
    private static T Child<T>(string name, Transform parent) where T : Component => Probe.Go(name, parent).AddComponent<T>();
    private static Button ButtonOn(GameObject go) => go.AddComponent<Button>();

    private sealed class Session
    {
        internal UIWindow Window = null!;
        internal Component Slot = null!;
        internal Button Button = null!;
        internal int Clicks;
        internal TownServiceToken Token => TownServicePresentation.Catalog != null
            ? new List<TownServiceToken>(TownServicePresentation.Catalog.Samples)[0] : new List<TownServiceToken>(TownServicePresentation.Ritual!.Samples)[0];
        internal VRHand Hand = new();
        internal Transform OriginalParent = null!;
        internal WindowPanel OriginalPanel = null!;
        internal RawImage Portrait = null!, HiddenPortrait = null!;
        internal Texture2D PortraitTexture = null!;
        internal readonly Dictionary<Transform, Transform> NativeParents = new();
        internal object Owner = null!;
        internal object? SelectedCard;
        internal int HiddenCallbacks;
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

    private static Session Open(byte service, bool enabled = true)
    {
        WorldUIConfig.ImmersiveTownServices.Value = enabled;
        new GameObject("events", typeof(EventSystem));
        Probe.Roots.Add(EventSystem.current.gameObject);
        var s = new Session { OriginalParent = Probe.Go("native-parent").transform };
        var root = Probe.Go("native-window", s.OriginalParent);
        if (service == 1)
        {
            var win = root.AddComponent<UIShopItemWindow>();
            win.ItemInventory = Child<UIShopItemInventory>("catalog", root.transform);
            win.exitShopButton = Child<Button>("exit", root.transform);
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
        s.PortraitTexture = new Texture2D(2, 2) { name = service == 1 ? "GuildBackground_Merchant"
            : service == 2 ? "Guild_Background_Temple" : "Guild_Background_Enchantress" };
        Textures.Add(s.PortraitTexture);
        s.Portrait = Child<RawImage>("portrait", root.transform); s.Portrait.texture = s.PortraitTexture;
        s.HiddenPortrait = Child<RawImage>("already-hidden-portrait", root.transform);
        s.HiddenPortrait.texture = s.PortraitTexture; s.HiddenPortrait.enabled = false;
        foreach (Transform descendant in root.GetComponentsInChildren<Transform>(true))
            if (descendant != root.transform) s.NativeParents.Add(descendant, descendant.parent);
        s.Owner = service == 1 ? ((UIShopItemWindow)s.Window).ItemInventory.character
            : service == 2 ? ((UITempleWindow)s.Window).character : ((UINewEnhancementWindow)s.Window).character;
        s.SelectedCard = service == 3 ? ((UINewEnhancementWindow)s.Window).selectedCard : null;
        s.Window.onHidden.AddListener(() => s.HiddenCallbacks++);
        s.Button.onClick.AddListener(() => s.Clicks++);
        GuildmasterDestinations.Window = s.Window;
        GuildmasterDestinations.Mode = service == 1 ? EGuildmasterMode.Merchant : service == 2 ? EGuildmasterMode.Temple : EGuildmasterMode.Enchantress;
        ModalFallback.TryConvertWindow(s.Window);
        s.OriginalPanel = ModalFallback.Converted[0];
        TownServicePresentation.Tick();
        Refresh(); // The production census is intentionally rate limited across openings.
        return s;
    }

    private static void Clean()
    {
        CanvasConversion.DeferParent = false; CanvasConversion.KeepActive = false;
        TownServicePalmConfirmation.Clear(); TownServiceRitual.Fail = false; ModalFallback.FailConvert = false;
        TownServicePresentation.Reset();
        typeof(TownServicePresentation).GetField("_failedWindow", Static)!.SetValue(null, null);
        Tokens.Clear(); ModalFallback.Converted.Clear(); CanvasConversion.ActivePanels.Clear();
        foreach (var root in Probe.Roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
        Probe.Roots.Clear(); Probe.Events.Clear(); GuildmasterDestinations.Window = null;
        foreach (var texture in Textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
        Textures.Clear();
        WorldUIConfig.ImmersiveTownServices.Value = true;
        TownServiceEnhancementHandoff.Enabled = true;
    }

    private static void MapHandFallback()
    {
        var session = Open(3);
        Check(TownServicePresentation.Active, "enabled map hand uses physical enhancement");
        TownServiceEnhancementHandoff.Enabled = false;
        Check(!TownServicePresentation.Active, "map hand disable immediately fences physical enhancement");
        TownServicePresentation.Tick();
        Check(!TownServicePresentation.Active && CanvasConversion.ActivePanels.Count == 1,
            "map hand disable restores one usable native enhancement window");
        foreach (var pair in session.NativeParents) Check(pair.Key.parent == pair.Value,
            "map hand disable restores original enhancement widget parents");
        Clean(); TownServiceEnhancementHandoff.Enabled = false; session = Open(1);
        Check(!TownServicePresentation.Active && CanvasConversion.ActivePanels.Count == 1, "disabled map hand retains the complete usable native merchant window");
        Clean(); TownServiceEnhancementHandoff.Enabled = false; session = Open(3);
        Check(!TownServicePresentation.Active && CanvasConversion.ActivePanels.Count == 1,
            "manual enhancement visit without map hand keeps complete native window");
        Clean();
    }

    private static void IdentityChanges()
    {
        for (byte service = 2; service <= 3; service++)
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
        foreach (byte service in new byte[] { 3 })
        {
            var s = Open(service); s.Grab();
            if (service == 1) ((UIShopItemWindow)s.Window).ItemInventory.mode++;
            else ((UINewEnhancementWindow)s.Window).mode++;
            s.Token.Tick(1); Check(s.Token.HeldRoot == null, "mode switch cancels held selection"); Clean();
        }
        var merchantMode = Open(1);
        Check(TownServicePresentation.Catalog==null && TownServicePresentation.WorkspaceProps==null,
            "native merchant context never duplicates persistent public cabinet");
        ((UIShopItemWindow)merchantMode.Window).ItemInventory.mode++;
        TownServicePresentation.Tick();
        Check(TownServicePresentation.Active,"native merchant tab switch retains confirmation context");Clean();
        var enhancement = Open(3); enhancement.Grab();
        ((UINewEnhancementWindow)enhancement.Window).selectedCard = new AbilityCardUI();
        enhancement.Token.Tick(1); Check(enhancement.Token.HeldRoot == null, "committed card switch cancels held selection"); Clean();

        var recycled = Open(3); recycled.Grab();
        // Native pool reuses the same AbilityCardUI for an equivalent card name/other owner.
        ((UIEnhanceCardSlot)recycled.Slot).AbilityCard!.AbilityCard = new object();
        recycled.Token.Tick(1); Check(recycled.Token.HeldRoot == null, "pooled underlying card switch cancels held selection"); Clean();

        var immediate = Open(2); immediate.Grab();
        ((UITempleWindow)immediate.Window).character = new object();
        immediate.Release(); Check(immediate.Clicks == 0, "release rechecks context before next tick"); Clean();

        var sourceChanged = Open(3); sourceChanged.Grab();
        ((UIEnhanceCardSlot)sourceChanged.Slot).AbilityCard!.AbilityCard = new object();
        sourceChanged.Release(); Check(sourceChanged.Clicks == 0, "release rechecks pooled identity before next tick"); Clean();

        var expired = Open(2); expired.Grab();
        var sessionField = typeof(TownServicePresentation).GetField("_session", Static)!;
        sessionField.SetValue(null, (uint)sessionField.GetValue(null)! + 1);
        expired.Token.Tick(1);
        Check(expired.Token.HeldRoot == null && !expired.Token.CanGrab, "captured session mismatch cancels held selection"); Clean();

        var closed = Open(2); closed.Grab(); TownServiceToken old = closed.Token;
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
            s = Open(2); s.Grab(); s.Hand.TriggerUp = triggerUp;
            s.Hand.Grabber.CancelAll();
            Check(s.Clicks == 0, "synthetic cancel never clicks even on trigger-up frame"); Clean();
        }
        s = Open(2); s.Grab(); s.Hand.HasPose = false; s.Release();
        Check(s.Clicks == 0, "tracking loss never clicks native selection"); Clean();
        s = Open(2); s.Grab(); s.Token.HeldRoot!.position += new Vector3(3, 0, 0); s.Release();
        Check(s.Clicks == 0, "drop outside mat never clicks"); Clean();
        s = Open(2); s.Grab(); s.Button.interactable = false; s.Release();
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
        // Physical stations own a whole-window mask, not the old three floating sections.
        Probe.Events.Clear(); s.Window.Hide();
        Check(!TownServicePresentation.Active && s.Window.transform.parent == s.OriginalParent
            && CanvasConversion.ActivePanels.Count == 0, "native hide immediately restores hierarchy and all owners");
        foreach (var pair in s.NativeParents) Check(pair.Key.parent == pair.Value, "native hide restores physical ritual sources");
        Check(Probe.Events.Contains("native-continuation") && !Probe.Events.Exists(x => x.StartsWith("sample:")),
            "native continuation completes without animation sampling"); Clean();

        TownServiceRitual.Fail = true; s = Open(3);
        Check(!TownServicePresentation.Active && CanvasConversion.ActivePanels.Count == 1,
            "ritual construction failure leaves exactly one native context owner");
        foreach (var pair in s.NativeParents) Check(pair.Key.parent == pair.Value, "ritual failure restores native hierarchy");
        Check(ModalFallback.Converted.Count == 1 && ModalFallback.Converted[0].Panel.OriginalParent == s.OriginalParent,
            "fallback context remembers original native parent"); Clean();

        // Retained legacy section rollback is explicitly injected; no claim that 545
        // creates these sections in its normal physical presentation.
        s = Open(3);
        var win = (UINewEnhancementWindow)s.Window;
        var sections = (List<TownServiceSurface>)typeof(TownServicePresentation).GetField("Surfaces", Static)!.GetValue(null)!;
        sections.Add(new TownServiceSurface(10, (RectTransform)win.enhancementShop.transform, Vector3.zero, 1));
        sections.Add(new TownServiceSurface(11, (RectTransform)win.cardHolder.transform, Vector3.zero, 1));
        sections.Add(new TownServiceSurface(12, win.CardsDisplay.abilityCardsPanel, Vector3.zero, 1));
        var context = CanvasConversion.Convert(win.transform);
        typeof(TownServicePresentation).GetField("_context", Static)!.SetValue(null, context);
        Probe.Events.Clear(); s.Window.Hide();
        var order = string.Join(",", Probe.Events);
        Check(order.IndexOf("release:native-window", StringComparison.Ordinal) >= 0
            && order.IndexOf("release:native-window", StringComparison.Ordinal) < order.IndexOf("release:card-scroll", StringComparison.Ordinal),
            "context retires before restoring descendant sections");
        Check(order.IndexOf("release:card-scroll", StringComparison.Ordinal) < order.IndexOf("release:card-holder", StringComparison.Ordinal)
            && order.IndexOf("release:card-holder", StringComparison.Ordinal) < order.IndexOf("release:enhancements", StringComparison.Ordinal),
            "section rollback restores native hierarchy in LIFO order"); Clean();
    }

    private static void CheckClassic(Session s)
    {
        Check(s.Window.IsOpen && s.HiddenCallbacks == 0 && s.Clicks == 0,
            "toggle preserves native open controller without continuation callbacks");
        Check(!TownServicePresentation.OwnsWindow(s.Window), "rollback releases native window suppression claim");
        Check(!TownServicePresentation.Active && TownServicePresentation.Tray == null
            && TownServicePresentation.WorkMat == null && TownServicePresentation.Samples.Count == 0
            && TownServicePresentation.LocalSurfaces.Count == 0 && TownServicePresentation.StationRoot == null,
            "disabled presentation releases all local immersive ownership");
        Check(ModalFallback.Converted.Count == 1 && CanvasConversion.ActivePanels.Count == 1
            && ModalFallback.Converted[0].Panel.Target == s.Window.transform
            && ModalFallback.Converted[0].Panel.OriginalParent == s.OriginalParent,
            "disabled presentation has exactly one ordinary native conversion owner");
        var wp = ModalFallback.Converted[0];
        Check(!wp.ReflowCancelled && !wp.PoseRePlaceDone,
            "disabled window retains ordinary placement and fitting lifecycle");
        foreach (var pair in s.NativeParents)
            Check(pair.Key.parent == pair.Value, "toggle restores every original descendant parent");
        Check(s.Portrait.enabled && !s.HiddenPortrait.enabled,
            "toggle restores original portrait visibility without enabling hidden artwork");
        object owner = s.Window is UIShopItemWindow merchant ? merchant.ItemInventory.character
            : s.Window is UITempleWindow temple ? temple.character : ((UINewEnhancementWindow)s.Window).character;
        Check(ReferenceEquals(owner, s.Owner), "toggle retains native selected character");
        if (s.Window is UINewEnhancementWindow enhancement)
            Check(ReferenceEquals(s.SelectedCard, enhancement.selectedCard), "toggle retains native committed enhancement card");
    }

    private static void OptionalPresentation()
    {
        for (byte service = 2; service <= 3; service++)
        {
            var s = Open(service, false);
            Check(ModalFallback.Converted.Count == 1 && ReferenceEquals(s.OriginalPanel, ModalFallback.Converted[0])
                && !Probe.Events.Exists(x => x.StartsWith("section:") || x.StartsWith("release:")),
                "disabled opening never takes ownership of original window");
            CheckClassic(s);
            // Exercising many off ticks catches a path that silently reconverts the same window.
            for (int tick = 0; tick < 8; tick++) TownServicePresentation.Tick();
            Check(ReferenceEquals(s.OriginalPanel, ModalFallback.Converted[0]) && ModalFallback.Converted.Count == 1,
                "disabled idle ticks preserve the existing native conversion");
            uint previousSession = TownServicePresentation.Session;
            for (int cycle = 0; cycle < 5; cycle++)
            {
                WorldUIConfig.ImmersiveTownServices.Value = true;
                TownServicePresentation.Tick(); Refresh();
                Check(TownServicePresentation.Active && TownServicePresentation.Session != previousSession
                    && TownServicePresentation.OwnsWindow(s.Window)
                    && TownServicePresentation.Samples.Count == 1,
                    "reenabling creates a fresh usable immersive session");
                previousSession = TownServicePresentation.Session;
                Check(CanvasConversion.ActivePanels.Count == 0
                    && TownServicePresentation.LocalSurfaces.Count == 0,
                    "reenabling does not duplicate section or context owners");
                s.Grab(); TownServiceToken stale = s.Token;
                WorldUIConfig.ImmersiveTownServices.Value = false;
                // This release runs before the normal presentation update can tear down its
                // objects: the live config fence itself must revoke selection immediately.
                if (service == 2) Check(TownServicePresentation.OwnsWindow(s.Window),
                    "suppression ownership persists until rollback despite disabled option");
                s.Release();
                Check(s.Clicks == 0, "disabled option immediately fences a held release before next tick");
                TownServicePresentation.Tick();
                Check(stale.HeldRoot == null, "disabling cancels and removes the held sample");
                CheckClassic(s);
                WorldUIConfig.ImmersiveTownServices.Value = true;
                TownServicePresentation.Tick(); Refresh();
                s.Hand.TriggerUp = true; stale.OnRelease(s.Hand, Vector3.zero);
                Check(s.Clicks == 0, "retired sample cannot select after reenable");
                WorldUIConfig.ImmersiveTownServices.Value = false;
                TownServicePresentation.Tick(); CheckClassic(s);
            }
            // Disabling while still physically held must cancel ownership even without any
            // release input; an old reference cannot dispatch after another live enable.
            WorldUIConfig.ImmersiveTownServices.Value = true;
            TownServicePresentation.Tick(); Refresh(); s.Grab();
            TownServiceToken cancelled = s.Token;
            WorldUIConfig.ImmersiveTownServices.Value = false; TownServicePresentation.Tick();
            Check(cancelled.HeldRoot == null && s.Hand.Grabber.Heal() && s.Hand.Grabber.Held == null,
                "live disable cancels a still-held sample without release input");
            CheckClassic(s);
            WorldUIConfig.ImmersiveTownServices.Value = true; TownServicePresentation.Tick(); Refresh();
            s.Hand.TriggerUp = true; cancelled.OnRelease(s.Hand, Vector3.zero);
            Check(s.Clicks == 0, "cancelled toggle gesture cannot dispatch after another enable");
            // A new genuine gesture still performs the original selection after repeated toggles.
            WorldUIConfig.ImmersiveTownServices.Value = true;
            TownServicePresentation.Tick(); Refresh(); s.Grab(); s.Release();
            Check(s.Clicks == 1, "native selection remains usable after repeated presentation toggles");
            Clean();
        }
    }

    private static void ConfirmationFadeLifecycle()
    {
        var root = new GameObject("NativeConfirmationOwner", typeof(RectTransform));
        var prompt = new GameObject("NativeConfirmation", typeof(RectTransform), typeof(UIWindow));
        prompt.transform.SetParent(root.transform, false);
        var window = prompt.GetComponent<UIWindow>();
        object callback = new object(); MaskClock.Now = 0f;
        TownServiceConfirmationMask.Begin(window, () => callback);
        Check(TownServiceConfirmationMask.Owns(window), "only owned auto-confirm prompt is suppressed");
        window.Hide(); TownServiceConfirmationMask.Tick();
        Check(prompt.transform.parent != root.transform && TownServiceConfirmationMask.Owns(window),
            "native onHidden starts fade without exposing confirmation popup");
        window.IsVisible = false; TownServiceConfirmationMask.Tick();
        Check(prompt.transform.parent == root.transform && !TownServiceConfirmationMask.Owns(window),
            "finished native confirmation restores exact original hierarchy");
        window.IsOpen = window.IsVisible = true;
        TownServiceConfirmationMask.Begin(window, () => callback); callback = new object();
        TownServiceConfirmationMask.Tick();
        Check(prompt.transform.parent == root.transform, "reused prompt with unrelated callback is restored");
        TownServiceConfirmationMask.Begin(window, () => callback); MaskClock.Now = 6f;
        TownServiceConfirmationMask.Tick();
        Check(prompt.transform.parent == root.transform && !TownServiceConfirmationMask.Owns(window),
            "stalled native transition recovers original usable confirmation");
        TownServiceConfirmationMask.Clear(); UnityEngine.Object.DestroyImmediate(root);
    }

    private static void WindowMaskLifecycle()
    {
        var parent = (RectTransform)Probe.Go("mask-native-parent").transform;
        parent.sizeDelta = new Vector2(820, 530);
        parent.pivot = new Vector2(.23f, .77f);
        parent.position = new Vector3(14, -8, 2);
        parent.localRotation = Quaternion.Euler(0, 27, 0);
        Probe.Go("older-sibling", parent);
        var source = (RectTransform)Probe.Go("mask-native-window", parent).transform;
        source.anchorMin = new Vector2(.15f, .2f); source.anchorMax = new Vector2(.84f, .91f);
        source.pivot = new Vector2(.3f, .8f); source.sizeDelta = new Vector2(90, -45);
        source.anchoredPosition3D = new Vector3(17, -19, 3);
        source.localScale = new Vector3(.83f, .91f, 1);
        source.localRotation = Quaternion.Euler(0, 0, 4);
        Probe.Go("newer-sibling", parent);
        var native = source.gameObject.AddComponent<CanvasGroup>();
        native.alpha = .63f; native.interactable = false; native.blocksRaycasts = true;
        var independent=Probe.Go("native independent confirmation",source).AddComponent<CanvasGroup>();
        independent.ignoreParentGroups=true;
        int sibling = source.GetSiblingIndex();
        var corners = new Vector3[4]; source.GetWorldCorners(corners);
        Vector2 anchorMin = source.anchorMin, anchorMax = source.anchorMax, pivot = source.pivot, size = source.sizeDelta;
        Vector3 position = source.anchoredPosition3D, scale = source.localScale;
        Quaternion rotation = source.localRotation;
        for (int cycle = 0; cycle < 20; cycle++)
        {
            var mask = new TownServiceWindowMask(source);
            Check(source.parent != parent, "mask owns a separate wrapper without disabling source");
            Check(source.gameObject.activeInHierarchy && source.GetComponents<CanvasGroup>().Length == 1
                && source.GetComponent<CanvasGroup>() == native,
                "same-frame reopen never duplicates or replaces native CanvasGroup");
            Check(!independent.ignoreParentGroups,"nested native confirmation cannot escape the presentation mask");
            var suppression = source.parent.GetComponent<CanvasGroup>();
            Check(suppression != null && suppression.alpha == 0 && !suppression.blocksRaycasts,
                "mask suppresses rendering and raycasts on its own wrapper");
            var currentCorners = new Vector3[4]; source.GetWorldCorners(currentCorners);
            for (int i = 0; i < 4; i++) Check(Vector3.Distance(corners[i], currentCorners[i]) < .001f,
                "mask preserves native geometry with asymmetric parent pivot");
            // Native animations remain authoritative while the old illustration is masked.
            native.alpha = .41f + cycle * .01f; native.interactable = cycle % 2 == 0;
            native.blocksRaycasts = cycle % 3 == 0;
            float currentAlpha = native.alpha; bool currentInteractable = native.interactable, currentRaycasts = native.blocksRaycasts;
            Vector2 maskSize = ((RectTransform)source.parent).rect.size;
            mask.DetachFromPanel(Probe.Go("resident mask mount").transform);
            Check(((RectTransform)source.parent).rect.size == maskSize,
                "detached native selection pool retains its original layout dimensions");
            Check(!source.IsChildOf(parent),"hidden enchantment list does not occupy the character UI layout");
            mask.Dispose(); mask.Dispose();
            Check(independent.ignoreParentGroups,"native independent canvas policy is restored on mask disposal");
            Check(source.parent == parent && source.GetSiblingIndex() == sibling,
                "mask disposal restores original parent and sibling exactly");
            Check(source.anchorMin == anchorMin && source.anchorMax == anchorMax && source.pivot == pivot
                && source.sizeDelta == size && source.anchoredPosition3D == position && source.localScale == scale
                && Quaternion.Angle(source.localRotation, rotation) < .001f,
                "mask preserves all native local rect properties across repeated toggles");
            Check(native.alpha == currentAlpha && native.interactable == currentInteractable && native.blocksRaycasts == currentRaycasts,
                "mask disposal preserves current native animation and permissions");
        }
        // A native reparent beats disposal: the mask cannot reclaim a window from another owner.
        var moved = Probe.Go("native-new-home").transform;
        var relocatedMask = new TownServiceWindowMask(source);
        source.SetParent(moved, false); relocatedMask.Dispose();
        Check(source.parent == moved, "native reparent is never overwritten during mask disposal");
        Clean();
    }

    private static void MerchantContextLifecycle()
    {
        var session=Open(1,false);
        for(int cycle=0;cycle<20;cycle++)
        {
            WorldUIConfig.ImmersiveTownServices.Value=true;TownServicePresentation.Tick();
            Check(TownServicePresentation.Active&&TownServicePresentation.OwnsWindow(session.Window),
                "merchant native context remains owned behind persistent cabinet");
            Check(TownServicePresentation.Catalog==null&&TownServicePresentation.WorkspaceProps==null&&TownServicePresentation.Samples.Count==0,
                "merchant context cannot duplicate catalog or register hidden native row grabs");
            WorldUIConfig.ImmersiveTownServices.Value=false;
            Check(TownServicePresentation.OwnsWindow(session.Window),
                "merchant context suppression survives until explicit rollback");
            TownServicePresentation.Tick();CheckClassic(session);
        }
        Clean();
    }

    private static void ManualTrayPlacement()
    {
        var session = Open(2);
        Check(TownServicePresentation.Tray == null, "physical ritual has no legacy transaction drop tray");
        session.Grab();
        Vector3 waiting = TownServicePresentation.WorkMat!.parent.position;
        TownServicePresentation.Tick();
        Check(Vector3.Distance(waiting, TownServicePresentation.WorkMat.parent.position) < .001f,
            "presentation defers workspace relocation while original card is held");
        session.Token.OnGrabCancelled(session.Hand);
        Check(session.Clicks == 0, "waiting for relocation never confirms native selection");
        // The shipping merchant uses an invisible inspection frame. Exercise the retained
        // generic manual-tray lifetime branch with an explicitly supplied boundary fixture.
        var tray = new TownServiceTray(null, Vector3.zero, Quaternion.identity, 1f);
        tray.Root.SetParent(TownServicePresentation.WorkMat!.parent, false);
        typeof(TownServicePresentation).GetField("_tray", Static)!.SetValue(null, tray);
        Check(tray.Root.parent != null, "unmoved generic tray belongs to its workspace");
        Vector3 before = tray.Root.position;
        TownServicePresentation.Tick();
        Check(Vector3.Distance(tray.Root.position, before) > .001f, "unmoved tray follows owner workspace rearrangement");
        tray.IsGrabbed = true;
        Vector3 chosen = tray.Root.position + new Vector3(.3f, .2f, .4f);
        tray.Root.position = chosen;
        TownServicePresentation.Tick();
        Check(tray.Root.parent == null && Vector3.Distance(tray.Root.position, chosen) < .001f,
            "manual tray grab detaches before workspace movement");
        tray.IsGrabbed = false;
        TownServicePresentation.Tick();
        Check(tray.Root.parent == null && Vector3.Distance(tray.Root.position, chosen) < .001f,
            "released tray retains the player's chosen placement");
        Check(session.Clicks == 0, "workspace rearrangement never confirms an item");
        Clean();
    }

    private static void PhysicalMerchantSamples()
    {
        foreach (bool left in new[] { false, true })
        {
            Probe.Go("PhysicalEvents").AddComponent<UnityEngine.EventSystems.EventSystem>();
            var counter = Probe.Go("PhysicalCounter");
            var physical = Probe.Go("PhysicalCard", counter.transform).transform;
            physical.localPosition = new Vector3(.2f, .07f, .1f);
            physical.localRotation = Quaternion.Euler(65f, 0f, 0f);
            var original = (RectTransform)Probe.Go("OriginalItem", physical).transform;
            original.sizeDelta = new Vector2(180f, 145f);
            original.localScale = Vector3.one * .001f;
            var native = Probe.Go("NativeRow").AddComponent<Button>();
            int selections = 0; native.onClick.AddListener(() => selections++);
            object item = new object(), context = new object(); bool alive = true;
            var hand = new VRHand { Side = left ? HandSide.Left : HandSide.Right };
            using var token = new TownServiceToken(original, native, () => item, () => context,
                () => alive, counter.transform, physical);
            Vector3 home = physical.localPosition; Quaternion rotation = physical.localRotation;
            token.Tick(1f); native.interactable = false;
            Check(token.CanGrab, "unaffordable physical items remain inspectable");
            hand.Grabber.Grab(token);
            Check(token.HeldRoot == physical && original.IsChildOf(physical), "inspection lifts the original physical card without a duplicate");
            Check(token.HeldContent == null && selections == 0, "physical inspection has no duplicate mirror or selection");
            token.Tick(1f);
            Vector3 readingOffset=hand.Rig.GrabAnchor.InverseTransformPoint(physical.position);
            hand.Rig.GrabAnchor.position = new Vector3(2f, 2f, 2f); token.Tick(1f);
            Check(readingOffset.magnitude < .12f && Vector3.Distance(hand.Rig.GrabAnchor.InverseTransformPoint(physical.position),readingOffset) < .001f,
                "physical original follows either tracked hand");
            // Deliberately release over the historic purchase tray, with a valid enabled row.
            native.interactable = true; physical.position = counter.transform.TransformPoint(new Vector3(0f, .05f, 0f));
            hand.TriggerUp = true; token.OnRelease(hand, Vector3.zero);
            Check(selections == 0, "physical release over work tray never selects or purchases");
            Check(token.HeldRoot == null && token.IsMoving && !token.CanGrab, "physical release starts a bounded return animation");
            typeof(TownServiceToken).GetField("_returnStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(token, Time.unscaledTime - .11f);
            token.Tick(1f);
            Check(token.IsMoving && Vector3.Distance(physical.localPosition, home) > .001f,
                "physical card return has an observable intermediate pose");
            typeof(TownServiceToken).GetField("_returnStarted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(token, Time.unscaledTime - .5f);
            token.Tick(1f);
            Check(!token.IsMoving && token.CanGrab && Vector3.Distance(physical.localPosition, home) < .001f
                && Quaternion.Angle(physical.localRotation, rotation) < .01f,
                "physical return restores exact rack pose and immediate regrab");
            token.OnGrab(hand); token.Tick(1f); context = new object(); token.Tick(1f);
            Check(token.HeldRoot == null && !token.IsMoving && original != null && selections == 0,
                "owner change cancels physical inspection without destroying native card");
            token.OnGrab(hand); alive = false; token.Tick(1f);
            Check(token.HeldRoot == null && !token.CanGrab && original != null && selections == 0,
                "service closure cancels physical inspection without a transaction");
            Clean();
        }
    }

    private static void PhysicalPurse()
    {
        foreach (float scale in new[] { 1f, 2f })
        for (int scenario = 0; scenario < 6; scenario++)
        {
            var counter = Probe.Go("PurseCounter").transform;
            counter.localScale = Vector3.one * scale;
            var physical = Probe.Go("Purse", counter).transform;
            physical.localRotation = Quaternion.Euler(0f, 37f, 0f);
            var source = (RectTransform)Probe.Go("PurseReach", physical).transform;
            source.sizeDelta = new Vector2(.1f, .13f);
            var button = Probe.Go("NativeDonate").AddComponent<Button>();
            object identity = new object(); bool inspect = scenario != 4; int commits = 0;
            var hand = new VRHand { WorldScale = scale, TriggerUp = true };
            hand.Rig.GrabAnchor.localScale = Vector3.one * scale;
            hand.Rig.GrabAnchor.rotation = Quaternion.Euler(32f, 70f, 10f);
            using var token = new TownServiceToken(source, button, () => identity, () => identity, () => true,
                counter, physical, drop: () => { commits++; return true; }, eligible: () => true,
                inspect: () => inspect, reachDepth: .10f, uprightProp: true, handAllowed: candidate => scenario != 5);
            token.Tick(scale);
            var shape = (BoxCollider)typeof(TownServiceToken).GetField("_shape", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(token)!;
            Check(Mathf.Abs(shape.size.z - .1f * scale) < .0001f, "purse collider encloses its physical depth at each map scale");
            if (scenario == 4 || scenario == 5)
            {
                Check(!hand.Grabber.ForceGrab(token, true) && commits == 0,
                    "unowned or unavailable purse cannot be grabbed or donated");
                Clean(); continue;
            }
            Quaternion initial = physical.rotation;
            Check(hand.Grabber.ForceGrab(token, true), "available purse uses actual routed grab");
            token.Tick(scale);
            Check(Quaternion.Angle(initial, physical.rotation) < .05f, "purse pickup preserves upright physical orientation");
            Vector3 pinch = new Vector3(0f, CardsConfig.HeldOffPalm.Value, CardsConfig.HeldForward.Value);
            Vector3 below = physical.position - hand.Rig.GrabAnchor.TransformPoint(pinch);
            Check(Vector3.Distance(below, Vector3.down * (.055f * scale)) < .0001f,
                "purse hangs below pinch without applying map scale twice");
            Quaternion delta = Quaternion.Euler(10f, -20f, 15f);
            hand.Rig.GrabAnchor.rotation = delta * hand.Rig.GrabAnchor.rotation;
            token.Tick(scale);
            Check(Quaternion.Angle(delta * initial, physical.rotation) < .05f,
                "held purse follows hand rotation without an independent card pose");
            physical.position = counter.TransformPoint(new Vector3(scenario == 3 ? .6f : 0f, .05f, 0f));
            if (scenario == 1) hand.Grabber.CancelAll();
            if (scenario == 2) hand.HasPose = false;
            hand.Grabber.ReleaseTick(); token.OnRelease(hand, Vector3.zero);
            Check(commits == (scenario == 0 ? 1 : 0), "purse drop uses tracked deliberate bowl release once " + scenario);
            Clean();
        }
    }

    private static void PurseSettlement()
    {
        for(int scenario=0;scenario<3;scenario++)
        {
            var home=Probe.Go("Owned purse home").transform;
            var bowl=Probe.Go("Shared physical bowl").transform;bowl.position=new Vector3(2f,1f,0f);
            var physical=Probe.Go("Offering purse",home).transform;
            var source=(RectTransform)Probe.Go("Purse reach",physical).transform;source.sizeDelta=new Vector2(.1f,.13f);
            var button=Probe.Go("Native donation").AddComponent<Button>();
            object item=new object(),context=new object();int requests=0;
            using var token=new TownServiceToken(source,button,()=>item,()=>context,()=>true,bowl,physical,
                drop:()=>{requests++;return true;},eligible:()=>true,uprightProp:true);
            var hand=new VRHand{TriggerUp=true};token.Tick(1f);
            Check(hand.Grabber.ForceGrab(token,true),"purse settlement uses the actual routed physical grab");token.Tick(1f);
            physical.position=bowl.TransformPoint(new Vector3(0f,.05f,0f));hand.Grabber.ReleaseTick();
            Vector3 released=physical.position;home.position+=Vector3.right*7f;token.Tick(1f);
            Check(physical.parent==bowl&&Vector3.Distance(released,physical.position)<.0001f&&requests==1,
                "accepted purse waits at actual bowl instead of returning to moving hand before native payment");
            Check(!token.CanGrab&&token.PhysicalVisibility==1f,"pending native payment keeps one visible unreachable offering");
            if(scenario==0)
            {
                token.CompletePhysicalOffering(false);
                typeof(TownServiceToken).GetField("_returnStarted",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(token,Time.unscaledTime-1f);
                token.Tick(1f);
                Check(physical.parent==home&&token.PhysicalVisibility==1f&&token.CanGrab,"native rejection returns the purse without losing its pickup");
            }
            else
            {
                token.CompletePhysicalOffering(true);
                typeof(TownServiceToken).GetField("_settledAt",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(token,Time.unscaledTime-.14f);
                token.CompletePhysicalOffering(true);token.Tick(1f);
                Check(token.PhysicalVisibility>.1f&&token.PhysicalVisibility<.9f&&physical.position.y<released.y,
                    "confirmed purse sinks and fades once at bowl without restarting on duplicate completion");
                typeof(TownServiceToken).GetField("_settledAt",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(token,Time.unscaledTime-1f);token.Tick(1f);
                Check(token.PhysicalVisibility==0f&&!token.CanGrab&&!token.IsMoving&&requests==1,
                    "paid purse stays consumed and creates neither another callback nor invisible pickup");
                if(scenario==2)
                {
                    context=new object();token.Tick(1f);
                    Check(physical.parent==home&&token.PhysicalVisibility==1f,"new owned character restores its independent physical purse");
                }
            }
            Clean();
        }
    }

    private static void PhysicalCommitCases()
    {
        for(int scenario=0;scenario<9;scenario++)
        {
            var counter=Probe.Go("DropCounter");var physical=Probe.Go("DropCard",counter.transform).transform;
            var source=(RectTransform)Probe.Go("DisplayedRect",physical).transform;source.sizeDelta=new Vector2(.15f,.12f);
            var native=Probe.Go("HiddenNativeButton").AddComponent<Button>();native.gameObject.SetActive(false);
            object item=new object(),context=new object();bool eligible=true;int commits=0;
            var hand=new VRHand{TriggerUp=true};
            using var token=new TownServiceToken(source,native,()=>item,()=>context,()=>true,counter.transform,physical,
                drop:()=>{commits++;return true;},eligible:()=>eligible,zoneHalfWidth:scenario==8?.07f:.20f);
            token.Tick(1f);Check(token.CanGrab,"displayed physical prop remains grabbable with hidden native button");
            token.OnGrab(hand);
            if(scenario!=7)token.Tick(1f);
            physical.localPosition=new Vector3(0f,.05f,0f);
            if(scenario==8)physical.localPosition=new Vector3(.1f,.05f,0f);
            if(scenario==1)eligible=false;
            if(scenario==2)physical.localPosition=new Vector3(.5f,.05f,0f);
            if(scenario==3)item=new object();
            if(scenario==4)context=new object();
            if(scenario==5)hand.HasPose=false;
            if(scenario==6)token.OnGrabCancelled(hand);
            token.OnRelease(hand,Vector3.zero);token.OnRelease(hand,Vector3.zero);
            Check(commits==(scenario==0?1:0),"physical drop eligibility identity pose zone and cancellation fence "+scenario);
            Clean();
        }
    }


    private static void ParkedStockRegrab()
    {
        var counter=Probe.Go("Counter").transform;
        var physical=Probe.Go("OfferedItem",counter).transform;
        var source=(RectTransform)Probe.Go("Original",physical).transform;
        source.sizeDelta=new Vector2(.18f,.15f);
        var native=Probe.Go("NativeRow").AddComponent<Button>();
        var seat=Probe.Go("PalmSeat").transform;
        bool inspect=false; int reclaimed=0;
        using var token=new TownServiceToken(source,native,()=>source,()=>counter,()=>true,counter,physical,inspect:()=>inspect);
        token.ParkOffering(seat,()=>reclaimed++);
        token.Tick(1f);
        var hand=new VRHand();
        TownServiceMerchantHandoff.Reclaim=false;
        Check(!hand.Grabber.ForceGrab(token,true),"unowned modal card never bypasses inspection gate");
        TownServiceMerchantHandoff.Reclaim=true;
        Check(hand.Grabber.ForceGrab(token,true) && ReferenceEquals(hand.Grabber.Held,token),"actual routed grab reclaims parked stock through owned modal gate");
        Check(reclaimed==1 && token.HeldRoot==physical,"routed reclaim cancels once and adopts actual card");
        hand.Grabber.CancelAll(); TownServiceMerchantHandoff.Reclaim=false; Clean();
    }

    private static void PalmConfirmationLifecycle()
    {
        foreach(float scale in new[]{.05f,1f,198.12f})
        {
            var native=Probe.Go("NativeConfirmation"); var window=native.AddComponent<UIWindow>();
            var box=native.AddComponent<UIItemConfirmationBox>();
            var group=native.AddComponent<CanvasGroup>();
            box.titleText=Probe.Go("NativeTitle",native.transform).AddComponent<TMPro.TMP_Text>();
            box.informationText=Probe.Go("NativeInformation",native.transform).AddComponent<TMPro.TMP_Text>();
            box.confirmButton=Probe.Go("NativeConfirm",native.transform).AddComponent<Button>();
            box.cancelButton=Probe.Go("NativeCancel",native.transform).AddComponent<Button>();
            foreach(Component part in new Component[]{box.titleText,box.informationText,box.confirmButton,box.cancelButton})
                ((RectTransform)part.transform).sizeDelta=new Vector2(400f,80f);
            ((TMPro.TMP_Text)box.informationText).text="Native exact price and consequences";
            Probe.Go("DecisionEvents").AddComponent<EventSystem>();
            var hover=box.confirmButton.gameObject.AddComponent<DecisionHoverProbe>();
            int commits=0;box._onConfirmedCallback=()=>commits++;
            box.confirmButton.onClick.AddListener(()=>{box._onConfirmedCallback();window.Hide();});
            box.cancelButton.onClick.AddListener(box.OnCancel);
            var station=Probe.Go("Resident").transform;station.localScale=Vector3.one*scale;
            var seat=Probe.Go("Palm",station).transform;seat.localPosition=new Vector3(-.2f,1.35f,.2f);
            seat.localRotation=Quaternion.Euler(0f,12f,0f);
            int bars=GrabbableModal.Builds;
            TownServicePalmConfirmation.Begin(box,seat);TownServicePalmConfirmation.Tick();
            Check(TownServicePalmConfirmation.OwnsCurrent(window),"palm confirmation owns exact still-open callback");
            Check(GrabbableModal.Builds==bars,"palm confirmation never creates a window grab bar");
            var entry=new List<TownServicePalmConfirmation.Entry>(TownServicePalmConfirmation.Active)[0];
            Check(entry.Surfaces.Count==4 && commits==0,"four original decision controls remain pending without automatic confirmation");
            foreach(var surface in entry.Surfaces)
            {
                Vector3 relative=seat.InverseTransformPoint(surface.Panel.HostGo.transform.position);
                float height=surface.Panel.HostRect.rect.height*surface.Panel.HostGo.transform.lossyScale.y/scale;
                float bottom=station.InverseTransformPoint(surface.Panel.HostGo.transform.position).y-height*.5f;
                Check(relative.y<-.17f && relative.z<-.10f && relative.z>-.15f && bottom>=.9699f,
                    "confirmation controls remain directly below palm with actual tabletop clearance");
                Check(Vector3.Dot(surface.Panel.HostGo.transform.up,Vector3.up)>.999f,"native confirmation is upright independently of palm pitch");
                Check(surface.Panel.MrBackingSuppressed,"freestanding original controls have no mixed reality backing");
            }
            var confirm=entry.Surfaces[2].Panel;
            Check(confirm.Target==box.confirmButton.transform && box.confirmButton.IsInteractable(),"original interactive confirm button is moved intact");
            Check(confirm.HostRect.rect.height*confirm.HostGo.transform.lossyScale.y<=.0451f*scale,"native button fit obeys height limit at every map scale");
            group.alpha=.35f;TownServicePalmConfirmation.LateTick();
            Check(Mathf.Abs(confirm.HostGo.GetComponent<CanvasGroup>().alpha-.35f)<.001f,"native decision fade is retained under anchored controls");
            group.alpha=1f;TownServicePalmConfirmation.LateTick();
            ExecuteEvents.Execute(box.confirmButton.gameObject,new PointerEventData(EventSystem.current),ExecuteEvents.pointerEnterHandler);
            Check(hover.Entries==1,"native confirmation still receives pointer hover feedback");
            ExecuteEvents.Execute(box.confirmButton.gameObject,new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left},ExecuteEvents.pointerClickHandler);
            TownServicePalmConfirmation.Tick();
            Check(commits==1 && TownServicePalmConfirmation.Owns(window),"native confirm runs once while its hide transition remains owned");
            window.IsVisible=false;TownServicePalmConfirmation.Tick();
            Check(!TownServicePalmConfirmation.Owns(window)&&box.confirmButton.transform.parent==native.transform,"completed hide restores original hierarchy without losing continuation");
            window.IsOpen=window.IsVisible=true;box._onConfirmedCallback=()=>commits+=100;
            TownServicePalmConfirmation.Begin(box,seat);TownServicePalmConfirmation.Tick();
            box._onConfirmedCallback=()=>commits+=1000;
            TownServicePalmConfirmation.CancelOwned(window);
            Check(box.Cancels==0,"reused unrelated confirmation is never cancelled");
            TownServicePalmConfirmation.Tick();Clean();
        }
    }

    private static void NativeFolioAndTeardown()
    {
        var parent=Probe.Go("NativeFolio").transform;
        var list=Probe.Go("NativeScroll",parent);
        var viewport=(RectTransform)list.transform;viewport.sizeDelta=new Vector2(600f,800f);
        var content=(RectTransform)Probe.Go("NativeRows",viewport).transform;content.sizeDelta=new Vector2(600f,2000f);
        content.anchorMin=content.anchorMax=new Vector2(.5f,1f);content.pivot=new Vector2(.5f,1f);
        var scroll=list.AddComponent<ScrollRect>();scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.vertical=true;
        list.AddComponent<RectMask2D>();
        scroll.verticalNormalizedPosition=.45f;
        var anchor=Probe.Go("FolioAnchor").transform;
        var p=TownServiceRitualLayout.Folio(10);
        using(var surface=new TownServiceSurface(10,viewport,p.Position,p.Size.x,anchor,p.Rotation,p.Size.y))
        {
            Vector2 before=content.anchoredPosition;
            surface.Tick(Vector3.zero,Quaternion.identity,1f);
            Check(surface.Panel.Target.GetComponent<ScrollRect>()==scroll && scroll.content==content && scroll.viewport==viewport,
                "native enhancement folio preserves original scroll viewport and full row inventory");
            Check(Vector2.Distance(content.anchoredPosition,before)<.001f,"folio fitting never resets native scrolling");
            Probe.Go("FolioEvents").AddComponent<EventSystem>();
            ExecuteEvents.Execute(list,new PointerEventData(EventSystem.current){scrollDelta=new Vector2(0f,-1f)},ExecuteEvents.scrollHandler);
            Check(Vector2.Distance(content.anchoredPosition,before)>.001f,"original enhancement list consumes pointer scroll after anchoring");
        }
        Check(viewport.parent==parent,"folio disposal restores original native scroll hierarchy");
        var inscriptionParent=Probe.Go("InscriptionParent").transform;
        var source=Probe.Go("NativeInscription").AddComponent<TMPro.TMP_Text>();
        var inscription=new TownServiceRitual.Inscription("native",source,inscriptionParent,Vector3.zero,.2f,.1f);
        UnityEngine.Object.DestroyImmediate(inscriptionParent.gameObject);
        try { inscription.Dispose(); }
        catch(Exception ex){throw new Exception("destroyed inscription root can be disposed without blocking native teardown",ex);}
        Check(true,"destroyed inscription root can be disposed without blocking native teardown");Clean();
    }

    private static void EnhancementDecisionLayout()
    {
        var native=Probe.Go("NativeRuneDecision");native.AddComponent<UIWindow>();native.AddComponent<CanvasGroup>();
        var box=native.AddComponent<UIEnhancementConfirmationBox>();
        box.titleText=Probe.Go("Title",native.transform).AddComponent<TMPro.TMP_Text>();
        box.informationText=Probe.Go("Information",native.transform).AddComponent<TMPro.TMP_Text>();
        box.enhancementIcon=Probe.Go("Icon",native.transform).AddComponent<Image>();
        box.enhancementName=Probe.Go("Name",native.transform).AddComponent<TMPro.TMP_Text>();
        box.confirmButton=Probe.Go("Confirm",native.transform).AddComponent<Button>();
        box.cancelButton=Probe.Go("Cancel",native.transform).AddComponent<Button>();
        box._onConfirmCallback=()=>{};
        foreach(Component c in new Component[]{box.titleText,box.informationText,box.enhancementIcon,box.enhancementName,box.confirmButton,box.cancelButton})
            ((RectTransform)c.transform).sizeDelta=new Vector2(400f,80f);
        var station=Probe.Go("RuneResident").transform;
        var palm=Probe.Go("ActivityOfferingPalm",station).transform;palm.localPosition=new Vector3(-.18f,1.17f,.23f);
        var seat=Probe.Go("RunePalm",station).transform;seat.localPosition=palm.localPosition+Vector3.up*.17f;
        TownServicePalmConfirmation.Begin(box,seat);TownServicePalmConfirmation.Tick();
        var entry=new List<TownServicePalmConfirmation.Entry>(TownServicePalmConfirmation.Active)[0];
        Check(entry.Surfaces.Count==6,"enhancement decision retains all six original content groups");
        foreach(var surface in entry.Surfaces)
        {
            Vector3 relative=seat.InverseTransformPoint(surface.Panel.HostGo.transform.position);
            Check(Mathf.Abs(relative.x)<.24f && relative.z<-.10f && relative.z>-.15f && relative.y<-.17f,
                "all original enhancement confirmation content stays together below the palm");
        }
        TownServicePalmConfirmation.CancelOwned(box.GetComponent<UIWindow>());
        TownServicePalmConfirmation.CancelOwned(box.GetComponent<UIWindow>());
        Check(box.Cancels==1,"enhancement withdrawal cancels only the live decision once");
        Clean();
    }

    public static int Run()
    {
        _assertions = 0;
        try { NativeFolioAndTeardown(); EnhancementDecisionLayout(); PalmConfirmationLifecycle(); ParkedStockRegrab(); PhysicalCommitCases(); PhysicalMerchantSamples(); WindowMaskLifecycle(); MerchantContextLifecycle(); ConfirmationFadeLifecycle(); IdentityChanges(); HoverAndRelease(); CancellationCompatibility(); Handoff(); RollbackAndContinuation(); OptionalPresentation(); ManualTrayPlacement(); MapHandFallback(); PhysicalPurse(); PurseSettlement(); return _assertions; }
        finally { Clean(); }
    }
}
