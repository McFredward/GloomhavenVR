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
        for (int scenario = 0; scenario < 18; scenario++) RunCase(scale, scenario);
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
        using (var handoff = new TownServiceEnhancementHandoff(shop, station, () => alive, () => input))
        {
            handoff.Tick();
            if (scenario == 1) shop.character = new Owner { CharacterID = "foreign" };
            if (scenario == 2) card.transform.position = palm.TransformPoint(new Vector3(0f, 0f, 1f));
            if (scenario == 3) slot.Selectable.interactable = false;
            if (scenario == 4) card.Owned = false;
            if (scenario == 8) shop._isConfirmationBoxOpened = true;
            if (scenario == 9) card.IsHeld = true;
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
                Check(card.Grabbable && TownServiceEnhancementHandoff.CanReclaim(card), "offering remains reclaimable");
                Check(!TownServiceEnhancementHandoff.TryOffer(card), "duplicate release cannot select twice");
                palm.localPosition += new Vector3(.1f, .04f, -.02f); handoff.Tick();
                Check((card.transform.position - palm.position).magnitude < .06f * scale, "physical offering follows actual palm at every scale");
                if (scenario == 10)
                {
                    var hand = new VRHand(); card.Grab(hand);
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
        MapRoomDriver.Visits = 0; GuildmasterDestinations.Mode = EGuildmasterMode.None;
        card.Owned = false; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 0, "foreign held card never opens native service");
        card.Owned = true; StoryComposite.PointOfNoReturn = true; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 0, "story commitment prevents automatic visit");
        StoryComposite.PointOfNoReturn = false; MapRoomDriver.CanVisit = false; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 0, "native unavailable service never opens");
        MapRoomDriver.CanVisit = true; card.transform.position = palm.position + Vector3.forward * 5f;
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 0, "distant card never opens service");
        card.transform.position = palm.position; TownServiceEnhancementHandoff.TickApproach();
        Check(MapRoomDriver.Visits == 1, "owned card approach opens through original native visit");
        TownServiceEnhancementHandoff.TickApproach(); Check(MapRoomDriver.Visits == 1, "repeated approach cannot toggle native service");
        UnityEngine.Object.DestroyImmediate(root); VRHands.Left = null; TownServicePopulation.Station = null;
    }
}
