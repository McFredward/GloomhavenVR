using System;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition, string reason) { count++; if (!condition) throw new Exception(reason); }
    public static int Run()
    {
        count = 0;
        OfferFeedback();
        NativeFrame();
        FirstVisitCue();
        Approach();
        WalkAway();
        foreach (float scale in new[] { .05f, 1f, 2f, 198.12f })
        for (int scenario = 0; scenario < 22; scenario++) RunCase(scale, scenario);
        SwapOffering();
        return count;
    }

    private static void NativeFrame()
    {
        var root = new GameObject("CardHilight", typeof(RectTransform), typeof(UIEnhancementCardHighlighter));
        var nativeFrame = new GameObject("GUI_LevelUp_Frame", typeof(RectTransform), typeof(Image));
        nativeFrame.transform.SetParent(root.transform, false);
        var rect = (RectTransform)nativeFrame.transform;
        rect.sizeDelta = new Vector2(100f, 100f);
        rect.localScale = new Vector3(.02f, 1f, 1f); // captured squeezed flat animation
        rect.GetComponent<Image>().raycastTarget = true;
        var card = new GameObject("Native print", typeof(RectTransform), typeof(AbilityCardUI), typeof(Image));
        card.transform.SetParent(root.transform, false);
        var mask = card.AddComponent<TownServiceNativeEnhancementCardMask>();
        mask.Mask();
        Check(rect.sizeDelta == Vector2.zero && Mathf.Abs(rect.localScale.x - 1f) < .001f
            && !rect.GetComponent<Image>().raycastTarget,
            "world-space full-card frame occupies the card rather than a squeezed vertical strip and cannot steal native clicks");
        mask.Restore();
        Check(rect.sizeDelta == new Vector2(100f, 100f) && Mathf.Abs(rect.localScale.x - .02f) < .001f
            && rect.GetComponent<Image>().raycastTarget,
            "native frame transform and input return to their original flat state");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void FirstVisitCue()
    {
        var root = new GameObject("First visit fixture");
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow));
        native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false);
        var card = new GameObject("Owned map card", typeof(VRCard)).GetComponent<VRCard>();
        card.transform.SetParent(root.transform, false); card.Owner = shop.character; card.Model.ID = 412;
        CardsDriver.OffScenarioFanCards = new[] { card };
        var slot = new GameObject("Native slot", typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot))
            .GetComponent<UIEnhanceCardSlot>();
        slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
        slot.AbilityCard = new GameObject("Native card", typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
        slot.AbilityCard.AbilityCard = card.Model;
        slot.Selected = () => shop.selectedCard = slot.AbilityCard;
        shop.CardsDisplay.slotsPool.Add(slot);
        var hand = new VRHand(); VRHands.Left = hand; VRHands.Right = null;
        hand.Grabber.Held = card; card.IsHeld = true;
        bool input = false;
        TownServicePresentation.SessionAge = .08f;
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => true, () => input))
        {
            handoff.Tick(); card.transform.position = handoff.Seat.position; handoff.Tick();
            CanvasGroup gate = handoff.Zone.GetComponent<CanvasGroup>();
            Check(gate.alpha > .3f && gate.alpha < .5f && hand.HoverTicks == 0 && hand.ClickPulses == 0,
                "first opening shows neutral palm locator while native input remains blocked");
            card.IsHeld = false; hand.Grabber.Held = null;
            Check(!TownServiceEnhancementHandoff.TryOffer(card) && handoff.Card == null,
                "first opening preview cannot commit native card selection");
            card.IsHeld = true; hand.Grabber.Held = card;
            input = true; handoff.Tick();
            Check(gate.alpha == 1f && hand.HoverTicks == 1 && hand.ClickPulses == 1,
                "ready first visit turns the same locator into a haptic snap target");
            CardsDriver.OffScenarioFanCards = Array.Empty<VRCard>(); handoff.Tick();
            Check(gate.alpha == 1f,
                "held owned card remains offerable when the visible fan no longer lists its plucked card");
        }
        CardsDriver.OffScenarioFanCards = new[] { card };
        TownServicePresentation.SessionAge = 3f;
        using (var revisit = new TownServiceEnhancementHandoff(shop, station, () => true, () => true))
        { revisit.Tick(); Check(revisit.Zone.GetComponent<CanvasGroup>().alpha == 1f,
            "later visit is ready without inheriting a stale preview latch"); }
        VRHands.Left = null; card.IsHeld = false;
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void OfferFeedback()
    {
        var root = new GameObject("Offer cue", typeof(RectTransform), typeof(CanvasGroup));
        var border = new GameObject("Border", typeof(RectTransform), typeof(Image));
        border.transform.SetParent(root.transform, false);
        var cue = new TownServiceOfferFeedback(root.GetComponent<CanvasGroup>(), root.transform);
        var hand = new VRHand();
        cue.Tick(false, hand, 0f, true, .43f); cue.Paint(false);
        Check(hand.HoverTicks == 0 && hand.ClickPulses == 0 && root.GetComponent<CanvasGroup>().alpha == 0f,
            "blocked native offer has no visual or haptic preview");
        cue.Tick(true, hand, .7f, false, .43f); cue.Paint(true);
        Check(root.GetComponent<CanvasGroup>().alpha > .99f && hand.HoverTicks == 0,
            "valid empty handoff target is visible before card approach");
        cue.Tick(true, hand, .30f, false, .43f);
        cue.Tick(true, hand, .22f, false, .43f);
        Check(hand.HoverTicks == 1 && hand.ClickPulses == 0,
            "approach has one debounced hover pulse");
        cue.Tick(true, hand, .10f, true, .43f);
        cue.Tick(true, hand, .08f, true, .43f);
        Check(hand.HoverTicks == 1 && hand.ClickPulses == 1,
            "native accept volume has one debounced snap pulse");
        var secondHand = new VRHand();
        cue.Tick(true, secondHand, .09f, true, .43f);
        Check(secondHand.HoverTicks == 1 && secondHand.ClickPulses == 1,
            "a different controller receives its own approach and snap edge");
        cue.Tick(false, hand, .08f, true, .43f); cue.Paint(false);
        Check(root.GetComponent<CanvasGroup>().alpha == 0f && hand.ClickPulses == 1,
            "native disablement clears an apparently actionable target");
        var bowl = new TownServiceOfferFeedback();
        var purseHand = new VRHand();
        bowl.Tick(true, purseHand, .20f, false, .28f, snapPulse: false);
        bowl.Tick(true, purseHand, .05f, true, .28f, snapPulse: false);
        Check(purseHand.HoverTicks == 1 && purseHand.ClickPulses == 0,
            "priestess approach adds one hover pulse without duplicating the purse token's inside-bowl snap pulse");
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static void SwapOffering()
    {
        var root = new GameObject("Swap fixture");
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow));
        native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false);
        palm.localPosition = new Vector3(-.18f, 1.14f, .23f);
        var fan = new GameObject("Fan").transform; fan.SetParent(root.transform, false); CardsDriver.FanRoot = fan;
        VRCard Card(int id)
        {
            var card = new GameObject("Card " + id, typeof(VRCard)).GetComponent<VRCard>();
            card.transform.SetParent(fan, false); card.Owner = shop.character; card.Model.ID = id;
            new GameObject("Full", typeof(FullAbilityCard)).transform.SetParent(card.transform, false);
            return card;
        }
        UIEnhanceCardSlot Slot(VRCard card)
        {
            var slot = new GameObject("Slot " + card.Model.ID, typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot)).GetComponent<UIEnhanceCardSlot>();
            slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
            slot.AbilityCard = new GameObject("Original " + card.Model.ID, typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
            slot.AbilityCard.transform.SetParent(slot.transform, false); slot.AbilityCard.AbilityCard = card.Model;
            slot.AbilityCard.fullAbilityCard = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>();
            slot.Selected = () => shop.selectedCard = slot.AbilityCard;
            shop.CardsDisplay.slotsPool.Add(slot); return slot;
        }
        VRCard first = Card(301), replacement = Card(302), rejected = Card(303);
        Slot(first); Slot(replacement); UIEnhanceCardSlot rejectedSlot = Slot(rejected);
        rejectedSlot.Selected = () => { shop.selectedCard = rejectedSlot.AbilityCard; throw new Exception("replacement rejected"); };
        CardsDriver.OffScenarioFanCards = new[] { first, replacement, rejected };
        CardsDriver.Returned = CardsDriver.Rebuilds = 0; CardsDriver.LastReturned = null;
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => true, () => true))
        {
            handoff.Tick(); first.transform.position = handoff.Seat.position;
            Check(TownServiceEnhancementHandoff.TryOffer(first), "first valid owned card occupies enchantress palm");
            rejected.transform.position = handoff.Seat.position;
            Check(!TownServiceEnhancementHandoff.TryOffer(rejected) && ReferenceEquals(handoff.Card, first)
                && CardsDriver.Returned == 0 && ReferenceEquals(shop.selectedCard!.AbilityCard, first.Model),
                "rejected replacement preserves prior enchantress card atomically");
            replacement.transform.position = handoff.Seat.position;
            Check(TownServiceEnhancementHandoff.TryOffer(replacement), "second valid owned card atomically swaps into enchantress palm");
            Check(ReferenceEquals(handoff.Card, replacement) && ReferenceEquals(shop.selectedCard!.AbilityCard, replacement.Model),
                "replacement owns both physical and native enchantment selection");
            Check(CardsDriver.Returned == 1 && ReferenceEquals(CardsDriver.LastReturned, first) && first.IsFlying,
                "displaced enchantment card takes canonical fan return flight");
            Check(TownServiceEnhancementHandoff.IsParked(first) && TownServiceEnhancementHandoff.IsParked(replacement),
                "returning old card and parked replacement remain uniquely published");
            Check(!TownServiceEnhancementHandoff.TryOffer(replacement), "same parked card cannot replace itself");
            CardsDriver.Complete();
            Check(!TownServiceEnhancementHandoff.IsParked(first) && TownServiceEnhancementHandoff.IsParked(replacement),
                "displaced card retires only after its canonical return completes");
        }
        CardsDriver.Complete();
        UnityEngine.Object.DestroyImmediate(root);
    }
    private static void RunCase(float scale, int scenario)
    {
        var root = new GameObject("Fixture"); root.transform.localScale = Vector3.one * scale;
        var native = new GameObject("Native", typeof(UIWindow), typeof(UINewEnhancementWindow)); native.transform.SetParent(root.transform, false);
        var shop = native.GetComponent<UINewEnhancementWindow>();
        var station = new GameObject("Resident").transform; station.SetParent(root.transform, false);
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(station, false); palm.localPosition = new Vector3(-.18f, 1.14f, .23f);
        TownServicePopulation.Station = new TownServiceStation { Root = station };
        var fan = new GameObject("Fan").transform; fan.SetParent(root.transform, false); CardsDriver.FanRoot = fan;
        var card = new GameObject("ActualHandCard", typeof(VRCard)).GetComponent<VRCard>(); card.transform.SetParent(fan, false);
        card.transform.position = palm.position; card.Owner = shop.character;
        card.Model.ID = 123;
        var face = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>(); face.transform.SetParent(card.transform, false);
        new GameObject("Top").transform.SetParent(face.transform, false);
        var secondFaceTop = new GameObject("Top").transform; secondFaceTop.SetParent(face.transform, false);
        var slot = new GameObject("OriginalSlot", typeof(RectTransform), typeof(Button), typeof(UIEnhanceCardSlot)).GetComponent<UIEnhanceCardSlot>();
        slot.transform.SetParent(native.transform, false); slot.Selectable = slot.GetComponent<Button>();
        slot.AbilityCard = new GameObject("OriginalCard", typeof(AbilityCardUI)).GetComponent<AbilityCardUI>();
        slot.AbilityCard.transform.SetParent(slot.transform, false); slot.AbilityCard.AbilityCard = card.Model;
        if (scenario == 21) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard { ID = card.Model.ID };
        slot.AbilityCard.fullAbilityCard = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>();
        slot.AbilityCard.fullAbilityCard.transform.SetParent(slot.AbilityCard.transform, false);
        var originalTop = new GameObject("Top").transform; originalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var secondOriginalTop = new GameObject("Top").transform; secondOriginalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var nativePrint = new GameObject("Native print", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        nativePrint.transform.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var nativeArea = new GameObject("Native enhancement area", typeof(RectTransform),
            typeof(Image), typeof(UIEnhancementButtonHighlight)).GetComponent<Image>();
        nativeArea.transform.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        shop.CardsDisplay.slotsPool.Add(slot);
        bool alive = true, input = true; int selected = 0;
        int priorOfferLines = TownServiceVoice.Inspections;
        slot.Selected = () =>
        {
            selected++; shop.selectedCard = slot.AbilityCard; shop.cardHolder.Card = slot.AbilityCard;
            if (scenario == 5) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 6) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard();
            if (scenario == 7) throw new Exception("native callback failed");
        };
        CardsDriver.Returned = CardsDriver.Rebuilds = 0; CardsDriver.LastReturned = null;
        VRRigDriver.HeadCamera = null; VRHands.Left = VRHands.Right = null;
        VRHands.Primary = scenario == 20 ? new VRHand { WorldScale = 3f * scale } : null;
        MapRoomDriver.Active = true;
        CardsDriver.OffScenarioFanCards = new[] { card };
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => alive, () => input))
        {
            handoff.Tick();
            Check(handoff.Zone.GetComponent<CanvasGroup>().alpha == 1f && !card.IsHeld,
                scenario == 21 ? "same owned card ID survives a native enhancement-list model refresh"
                    : "empty ready palm advertises an owned offering without requiring a held card");
            if (scenario == 1) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 2) card.transform.position = palm.TransformPoint(new Vector3(0f, 0f, 1f));
            if (scenario == 3) slot.Selectable.interactable = false;
            if (scenario == 4) card.Owned = false;
            if (scenario == 8) shop._isConfirmationBoxOpened = true;
            if (scenario == 9) card.IsHeld = true;
            if (scenario == 1 || scenario == 3 || scenario == 4 || scenario == 8)
            {
                handoff.Tick();
                Check(handoff.Zone.GetComponent<CanvasGroup>().alpha == 0f,
                    "foreign disabled or pending native selection never advertises a palm drop");
            }
            bool offered = TownServiceEnhancementHandoff.TryOffer(card);
            string reason = scenario == 1 ? "foreign native character refuses offering"
                : scenario == 2 ? "distant release refuses offering"
                : scenario == 3 ? "disabled native slot refuses offering"
                : scenario == 5 ? "native callback owner race refuses offering" : "invalid offering is refused";
            Check(offered == (scenario == 0 || scenario >= 10), reason);
            if (offered)
            {
                if (scenario == 21) Check(!ReferenceEquals(card.Model, shop.selectedCard!.AbilityCard),
                    "same owned card ID survives a native enhancement-list model refresh");
                Check(selected == 1 && ReferenceEquals(handoff.Card, card), "one native selection parks the original card");
                var palmCanvas = new GameObject("Palm canvas", typeof(Canvas)).GetComponent<Canvas>();
                var wrongCanvas = new GameObject("Other canvas", typeof(Canvas)).GetComponent<Canvas>();
                var area = nativeArea.GetComponent<UIEnhancementButtonHighlight>();
                area.Ability = new object();
                TownServicePresentation.Ritual = new TownServiceRitual { Handoff = handoff };
                TownServicePresentation.Ritual.Surfaces.Add(new TownServiceSurface
                    { Id = 11, Panel = new ConvertedPanel { HostCanvas = palmCanvas } });
                Check(TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativeArea.gameObject, out VRCard? selectedCard)
                    && ReferenceEquals(selectedCard, card),
                    "laser over a live original ability-area button selects that native area on the offered card");
                Check(!TownServiceEnhancementHandoff.TryNativeArea(wrongCanvas, nativeArea.gameObject, out _)
                    && !TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativePrint.gameObject, out _),
                    "unrelated canvas and non-ability card print never steal the physical reclaim trigger");
                shop._isConfirmationBoxOpened = true;
                Check(!TownServiceEnhancementHandoff.TryNativeArea(palmCanvas, nativeArea.gameObject, out _),
                    "native confirmation closes the area-selection laser gate");
                shop._isConfirmationBoxOpened = false;
                TownServicePresentation.Ritual = null;
                UnityEngine.Object.DestroyImmediate(palmCanvas.gameObject);
                UnityEngine.Object.DestroyImmediate(wrongCanvas.gameObject);
                Check(!nativePrint.enabled && nativeArea.enabled
                    && TownServiceVoice.Inspections == priorOfferLines + 1,
                    "same-frame handoff hides only duplicate art and reacts to the accepted offer");
                Check(TownServiceEnhancementHandoff.IsParked(card), "parked card excluded from fan adoption");
                Check(handoff.Face == face.transform && handoff.CloneOf(originalTop) == face.transform.Find("Top"), "actual printed face retains native provenance");
                Check(handoff.CloneOf(secondOriginalTop) == secondFaceTop, "same-named printed nodes retain distinct native provenance");
                Check(card.FullCollider && card.Grabbable && TownServiceEnhancementHandoff.CanReclaim(card), "offering remains reclaimable");
                Check(!TownServiceEnhancementHandoff.TryOffer(card), "duplicate release cannot select twice");
                palm.localPosition += new Vector3(.1f, .04f, -.02f); handoff.Tick();
                Check(Mathf.Abs((card.transform.position.y - palm.position.y) / scale - (scenario == 20 ? .405f : .17f)) < .007f
                    && (new Vector2(card.transform.position.x - palm.position.x, card.transform.position.z - palm.position.z)).magnitude < .001f * scale,
                    "physical offering floats upright above actual palm at every scale");
                Check(Vector3.Dot(card.transform.up, Vector3.up) > .999f, "offered ability card is upright over the palm");
                if (scenario == 20)
                    Check(Mathf.Abs(card.transform.lossyScale.x / scale - 3f) < .001f,"offered mage card preserves tracked reading size across independent resident scale");
                if (scenario == 10)
                {
                    var hand = new VRHand();
                    input=false; shop._isConfirmationBoxOpened=true;
                    var confirm=new GameObject("NativeRuneConfirmation",typeof(UIWindow),typeof(UIEnhancementConfirmationBox));
                    confirm.transform.SetParent(root.transform,false);
                    Singleton<UIEnhancementConfirmationBox>.Instance=confirm.GetComponent<UIEnhancementConfirmationBox>();
                    GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.ModalUI;
                    TownServicePalmConfirmation.Owned=false;
                    Check(!hand.Grabber.ForceGrab(card,true),"unowned rune prompt retains ordinary modal grab block");
                    TownServicePalmConfirmation.Owned=true;
                    Check(hand.Grabber.ForceGrab(card,true),"actual routed grab reclaims mage card through owned native confirmation");
                    TownServicePalmConfirmation.Owned=false;
                    GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode=GloomhavenVR.Core.Events.VRMode.TableIdle;
                    Check(handoff.Card == null && shop.selectedCard == null, "manual reclaim clears native options");
                    Check(CardsDriver.Returned == 0, "manual reclaim preserves held card");
                    card.transform.SetParent(fan, true); card.IsHeld = false;
                    Check(TownServiceEnhancementHandoff.ReturnReclaimed(card) && CardsDriver.Returned == 1, "reclaimed release returns to hand rather than palm");
                    Check(!TownServiceEnhancementHandoff.ReturnReclaimed(card), "reclaimed release is one shot");
                }
                else if (scenario == 11)
                {
                    input = false; handoff.Tick();
                    Check(handoff.Card == card, "opening input fade retains offering");
                }
                else if (scenario >= 18)
                {
                    handoff.Dispose();
                    var returning = TownServiceEnhancementHandoff.Returning;
                    Check(returning.Count == 1 && returning[0].Card == card
                        && returning[0].CardId == 123 && returning[0].Face == face.transform,
                        "return presentation survives ritual disposal with actual face and fixed identity");
                    UnityEngine.Object.DestroyImmediate(slot.AbilityCard.gameObject);
                    Check(returning[0].Face == face.transform && returning[0].CardId == 123,
                        "native pool recycling cannot change return face provenance");
                    Check(TownServiceEnhancementHandoff.IsParked(card), "return flight stays excluded from static fan");
                    if (scenario == 18)
                    {
                        CardsDriver.Complete();
                        Check(TownServiceEnhancementHandoff.Returning.Count == 0 && !TownServiceEnhancementHandoff.IsParked(card),
                            "only actual flight completion retires return presentation");
                    }
                    else
                    {
                        MapRoomDriver.Active = false;
                        Check(TownServiceEnhancementHandoff.Returning.Count == 0, "map teardown clears return presentation");
                        MapRoomDriver.Active = true;
                    }
                }
                else if (scenario >= 12)
                {
                    if (scenario == 12)
                    {
                        var camera = new GameObject("Head", typeof(Camera)).GetComponent<Camera>(); camera.transform.SetParent(root.transform, false);
                        camera.transform.position = palm.TransformPoint(new Vector3(0f, 0f, 3f)); VRRigDriver.HeadCamera = camera;
                    }
                    if (scenario == 13) card.CurrentCharacter = false;
                    if (scenario == 14) card.InLoadout = false;
                    if (scenario == 15) alive = false;
                    if (scenario == 16) shop.selectedCard = null;
                    if (scenario == 17) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard();
                    handoff.Tick();
                    Check(handoff.Card == null && CardsDriver.Returned == 1 && CardsDriver.LastReturned == card,
                        scenario == 12 ? "walking away returns original card" : "stale owner or native selection returns original card");
                }
            }
            else
            {
                Check(handoff.Card == null && !TownServiceEnhancementHandoff.IsParked(card), "rejection never steals card ownership");
                if (scenario < 5 || scenario == 8 || scenario == 9) Check(selected == 0, reason);
            }
        }
        if (scenario == 0)
        {
            slot.AbilityCard.gameObject.SetActive(false);
            Check(nativePrint.enabled && nativeArea.enabled,
                "native pooled card restores its artwork when the game disables it");
        }
        Check(card != null, "disposing station never destroys actual map card");
        UnityEngine.Object.DestroyImmediate(root);
        TownServicePopulation.Station = null; VRRigDriver.HeadCamera = null;
    }

    private static void WalkAway()
    {
        var root=new GameObject("Empty visit",typeof(UIWindow),typeof(UINewEnhancementWindow));
        var palm=new GameObject("ActivityOfferingPalm").transform;palm.SetParent(root.transform,false);
        TownServicePopulation.Station=new TownServiceStation{Root=root.transform};
        var head=new GameObject("Head",typeof(Camera)).GetComponent<Camera>();
        VRRigDriver.HeadCamera=head; MapRoomDriver.Active=true;
        WorldUIConfig.ImmersiveTownServices.Value=true; CardsDriver.OffScenarioFanCards=null;
        using(var handoff=new TownServiceEnhancementHandoff(root.GetComponent<UINewEnhancementWindow>(),root.transform,()=>true,()=>true))
        {
            handoff.Tick();head.transform.position=Vector3.forward*3f;
            int closed=ModalFallback.Closed;
            TownServiceEnhancementHandoff.TickApproach();
            Check(ModalFallback.Closed==closed+1 && !root.GetComponent<UIWindow>().IsOpen,
                "walking away closes empty native service through its existing exit path");
            TownServiceEnhancementHandoff.TickApproach();
            Check(ModalFallback.Closed==closed+1,"closed service is not repeatedly exited");
        }
        UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(head.gameObject);
        TownServicePopulation.Station=null;VRRigDriver.HeadCamera=null;
    }

    private static void Approach()
    {
        var root = new GameObject("Approach");
        var palm = new GameObject("ActivityOfferingPalm").transform; palm.SetParent(root.transform, false);
        TownServicePopulation.Station = new TownServiceStation { Root = root.transform };
        var card = new GameObject("OwnedCard", typeof(VRCard)).GetComponent<VRCard>(); card.transform.SetParent(root.transform, false);
        var hand = new VRHand(); hand.Grabber.Held = card; VRHands.Left = hand;
        CardsDriver.OffScenarioFanCards = new[] { card };
        var head = new GameObject("Head", typeof(Camera)).GetComponent<Camera>(); head.transform.SetParent(root.transform, false);
        VRRigDriver.HeadCamera = head;
        void Outside()
        {
            head.transform.position = palm.position + Vector3.forward * 3f;
            card.transform.position = palm.position + Vector3.forward * 3f;
            TownServiceEnhancementHandoff.TickApproach();
        }
        void Offer() { card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach(); }
        Outside();
        MapRoomDriver.Visits = 0; GuildmasterDestinations.Mode = EGuildmasterMode.None;
        card.Owned = false; Offer();
        Check(MapRoomDriver.Visits == 0, "foreign held card never opens native service");
        card.Owned = true; StoryComposite.PointOfNoReturn = true; Outside(); Offer();
        Check(MapRoomDriver.Visits == 0, "story commitment prevents automatic visit");
        StoryComposite.PointOfNoReturn = false; MapRoomDriver.CanVisit = false; Outside(); Offer();
        Check(MapRoomDriver.Visits == 0, "native unavailable service never opens");
        MapRoomDriver.CanVisit = true; Outside();
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 0, "distant card never opens service");
        Offer();
        Check(MapRoomDriver.Visits == 1, "owned card approach opens through original native visit");
        Check(MapRoomDriver.LastSuppressed, "automatic enchantress entry suppresses the flat button sound");
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "repeated approach cannot toggle native service");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        System.Threading.Thread.Sleep(125);
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "explicit close remains closed while card stays near");
        VRHands.Left = null; Outside(); head.transform.position = palm.position;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 2, "head proximity opens original service without a held card");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 2, "explicit close remains closed while head stays near");
        head.transform.position = palm.position + Vector3.forward * 1.6f; TownServiceEnhancementHandoff.TickApproach();
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 2, "head hysteresis avoids boundary reopen");
        Outside(); head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "leaving and returning re-arms proximity greeting");
        GuildmasterDestinations.Mode = EGuildmasterMode.Merchant;
        Outside(); head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "proximity never takes over another open service");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "closing another service while near does not take over");
        Outside(); MapRoomDriver.CanVisit = false;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "native rail briefly unavailable cannot consume the visitor's approach");
        MapRoomDriver.CanVisit = true; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "pending native rail retries are rate-limited between frames");
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "pending approach opens when original native rail becomes ready");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "explicit close clears a previously deferred approach");
        Outside(); GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.ModalUI;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "modal confirmation prevents proximity opening");
        GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.TableIdle;
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 5,
            "transient modal or unloaded cards defer the original visit instead of consuming its proximity edge");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 5, "explicit close of a deferred modal approach remains closed");
        VRHands.Left = hand; card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 6, "deliberately offering a card overrides an earlier proximity close");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; Outside(); VRHands.Left = null;
        CardsDriver.OffScenarioFanCards = null; head.transform.position = palm.position;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 6, "head arrival before owned map cards finish building stays pending");
        CardsDriver.OffScenarioFanCards = new[] { card };
        System.Threading.Thread.Sleep(125); TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 7, "pending visit opens after the map cards load without stepping away");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; Outside();
        head.transform.position = palm.position + Vector3.up * 1.5f;
        TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 8, "visitor attention uses the floor-plane reach even when HMD is above the palm");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; WorldUIConfig.MapRoomHand!.Value = false;
        Outside(); head.transform.position = palm.position; Offer();
        Check(!TownServiceEnhancementHandoff.Enabled && MapRoomDriver.Visits == 8, "disabled map hand prevents automatic immersive opening");
        WorldUIConfig.MapRoomHand.Value = true;
        UnityEngine.Object.DestroyImmediate(root); VRHands.Left = null; VRRigDriver.HeadCamera = null; TownServicePopulation.Station = null;
    }
}
