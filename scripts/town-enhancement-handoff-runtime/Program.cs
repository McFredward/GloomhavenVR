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
        Approach();
        foreach (float scale in new[] { .05f, 1f, 2f, 198.12f })
        for (int scenario = 0; scenario < 20; scenario++) RunCase(scale, scenario);
        return count;
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
        slot.AbilityCard.fullAbilityCard = new GameObject("Full", typeof(FullAbilityCard)).GetComponent<FullAbilityCard>();
        slot.AbilityCard.fullAbilityCard.transform.SetParent(slot.AbilityCard.transform, false);
        var originalTop = new GameObject("Top").transform; originalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        var secondOriginalTop = new GameObject("Top").transform; secondOriginalTop.SetParent(slot.AbilityCard.fullAbilityCard.transform, false);
        shop.CardsDisplay.slotsPool.Add(slot);
        bool alive = true, input = true; int selected = 0;
        slot.Selected = () =>
        {
            selected++; shop.selectedCard = slot.AbilityCard;
            if (scenario == 5) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 6) slot.AbilityCard.AbilityCard = new ScenarioRuleLibrary.CAbilityCard();
            if (scenario == 7) throw new Exception("native callback failed");
        };
        CardsDriver.Returned = CardsDriver.Rebuilds = 0; CardsDriver.LastReturned = null;
        VRRigDriver.HeadCamera = null; VRHands.Left = VRHands.Right = null;
        MapRoomDriver.Active = true;
        CardsDriver.OffScenarioFanCards = new[] { card };
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => alive, () => input))
        {
            handoff.Tick();
            Check(handoff.Zone.GetComponent<CanvasGroup>().alpha == 1f && !card.IsHeld,
                "empty ready palm advertises an owned offering without requiring a held card");
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
                Check(selected == 1 && ReferenceEquals(handoff.Card, card), "one native selection parks the original card");
                Check(TownServiceEnhancementHandoff.IsParked(card), "parked card excluded from fan adoption");
                Check(handoff.Face == face.transform && handoff.CloneOf(originalTop) == face.transform.Find("Top"), "actual printed face retains native provenance");
                Check(handoff.CloneOf(secondOriginalTop) == secondFaceTop, "same-named printed nodes retain distinct native provenance");
                Check(card.FullCollider && card.Grabbable && TownServiceEnhancementHandoff.CanReclaim(card), "offering remains reclaimable");
                Check(!TownServiceEnhancementHandoff.TryOffer(card), "duplicate release cannot select twice");
                palm.localPosition += new Vector3(.1f, .04f, -.02f); handoff.Tick();
                Check(Mathf.Abs((card.transform.position.y - palm.position.y) / scale - .17f) < .007f
                    && (new Vector2(card.transform.position.x - palm.position.x, card.transform.position.z - palm.position.z)).magnitude < .001f * scale,
                    "physical offering floats upright above actual palm at every scale");
                Check(Vector3.Dot(card.transform.up, Vector3.up) > .999f, "offered ability card is upright over the palm");
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
        Check(card != null, "disposing station never destroys actual map card");
        UnityEngine.Object.DestroyImmediate(root);
        TownServicePopulation.Station = null; VRRigDriver.HeadCamera = null;
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
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "repeated approach cannot toggle native service");
        GuildmasterDestinations.Mode = EGuildmasterMode.None;
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
        GuildmasterDestinations.Mode = EGuildmasterMode.None; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "closing another service while near does not take over");
        Outside(); GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.ModalUI;
        head.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 3, "modal confirmation prevents proximity opening");
        GloomhavenVR.Core.Events.VRModeStateMachine.CurrentMode = GloomhavenVR.Core.Events.VRMode.TableIdle;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 3, "modal closure does not silently reopen a service");
        VRHands.Left = hand; card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 4, "deliberately offering a card overrides an earlier proximity close");
        GuildmasterDestinations.Mode = EGuildmasterMode.None; WorldUIConfig.MapRoomHand!.Value = false;
        Outside(); head.transform.position = palm.position; Offer();
        Check(!TownServiceEnhancementHandoff.Enabled && MapRoomDriver.Visits == 4, "disabled map hand prevents automatic immersive opening");
        WorldUIConfig.MapRoomHand.Value = true;
        UnityEngine.Object.DestroyImmediate(root); VRHands.Left = null; VRRigDriver.HeadCamera = null; TownServicePopulation.Station = null;
    }
}
