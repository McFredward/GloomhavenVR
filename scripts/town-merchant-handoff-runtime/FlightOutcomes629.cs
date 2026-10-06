using System;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Cards;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using MapRuleLibrary.Party;
using MapRuleLibrary.Adventure;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

public static partial class InteractionProgram
{
    private sealed class FlightSetup629 : IDisposable
    {
        internal readonly GameObject Root;
        internal readonly CMapCharacter Character = new();
        internal readonly CItem Owned = new(11), Stock = new(71);
        internal readonly Transform Palm;
        internal readonly UIItemConfirmationBox Box;
        internal FlightSetup629(float scale)
        {
            MapRoomDriver.Active = false; TownServiceMerchantHandoff.Reset(); TownServiceCardFlights.Tick();
            MapRoomDriver.Active = true; MapRoomDriver.CanVisit = true;
            FFSNet.FFSNetwork.IsOnline = false; StoryComposite.PointOfNoReturn = false;
            TownServiceEnhancementHandoff.WantsAbilityFan = false; TownServiceTempleOffering.WantsPurseFocus = false;
            GuildmasterDestinations.Mode = EGuildmasterMode.None; ShopService.Affordable = true;
            TownServiceMerchantTransaction.CommitFailures = 0; ItemsPile.ItemChip.NewArtReady = true;
            GloomhavenVR.Net.TownServices.TownServiceMirror.GrantSettled = true;
            Root = new GameObject("629 real native outcome boundary");
            Root.transform.localScale = Vector3.one * scale; Root.transform.rotation = Quaternion.Euler(0, 31, 0);
            VRRigDriver.RigRoot = Root.transform;
            var pool = new GameObject("Native pool", typeof(ObjectPool)); pool.transform.SetParent(Root.transform, false);
            ObjectPool.instance = pool.GetComponent<ObjectPool>();
            var head = new GameObject("Head", typeof(Camera)); head.transform.SetParent(Root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.6f, 1.3f); VRRigDriver.HeadCamera = head.GetComponent<Camera>();
            var wrist = new GameObject("Displayed wrist").transform; wrist.SetParent(Root.transform, false);
            wrist.localPosition = new Vector3(.4f, .8f, 1.1f); wrist.localRotation = Quaternion.Euler(13, 27, 9);
            VRHands.Left = new VRHand { WorldScale = scale }; VRHands.Right = new VRHand { WorldScale = scale };
            VRHands.Primary = VRHands.Right;
            VRHands.Left.Rig.PalmCenter = VRHands.Right.Rig.PalmCenter = wrist;
            VRHands.Left.Rig.GrabAnchor = VRHands.Right.Rig.GrabAnchor = wrist;
            VRHands.Left.PalmGate.IsOpen = true;
            var station = new GameObject("Station").transform; station.SetParent(Root.transform, false);
            Palm = new GameObject("ActivityOfferingPalm").transform; Palm.SetParent(station, false);
            Palm.localPosition = new Vector3(-.2f, 1.18f, .2f);
            TownServicePopulation.Station = new TownServiceStation { Root = station };
            Character.AllCharacterItems.Add(Owned); MapCharacterSelection.Selected = Character;
            var native = new GameObject("Native merchant", typeof(UIWindow), typeof(UIShopItemWindow));
            native.transform.SetParent(Root.transform, false); native.GetComponent<UIWindow>().IsOpen = true;
            var shop = native.GetComponent<UIShopItemWindow>(); shop.ItemInventory.character = Character;
            AdventureState.MapState.MapParty.Stock.Clear(); AdventureState.MapState.MapParty.Stock.Add(Stock);
            shop.ItemInventory.service = new ShopService(AdventureState.MapState.MapParty, _ => { });
            Singleton<UIGuildmasterHUD>.Instance = new UIGuildmasterHUD { shopWindow = shop };
            Box = new UIItemConfirmationBox(); Singleton<UIItemConfirmationBox>.Instance = Box;
            Box.confirmButton = new GameObject("Confirm", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            Box.cancelButton = new GameObject("Cancel", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            Box.cancelButton.onClick.AddListener(Box.OnCancel);
            TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.LateTick();
        }
        internal ItemsPile.ItemChip OfferOwned()
        {
            var chip = TownServiceMerchantHandoff.OwnedChips.First(c => ReferenceEquals(c.Item, Owned));
            chip.transform.position = Palm.position; chip.Release(Palm.position);
            TownServiceMerchantHandoff.Tick();
            Check(chip.TownOffering && Box.IsActive, "actual owner release opens the native sale request");
            return chip;
        }
        internal TownServiceToken OfferStock()
        {
            var physical = ObjectPool.SpawnCard(Stock.ID, ObjectPool.ECardType.Item, Root.transform);
            physical.transform.position = Palm.position; ((RectTransform)physical.transform).sizeDelta = new Vector2(140, 180);
            physical.transform.localScale = Vector3.one * .001f;
            var token = new TownServiceToken { PhysicalRoot = physical.transform };
            Check(TownServiceMerchantHandoff.Offer(Stock, false, Palm.position), "actual native stock can be offered for purchase");
            TownServiceCatalog.RetainOffer!(token); TownServiceMerchantHandoff.Tick();
            Check(Box.IsActive && token.Parks == 1, "stock original parks before its native buy prompt");
            return token;
        }
        public void Dispose()
        {
            MapRoomDriver.Active = false; TownServiceMerchantHandoff.Reset(); TownServiceCardFlights.Tick();
            UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(Box.Window.gameObject);
            UnityEngine.Object.DestroyImmediate(Box.confirmButton.gameObject); UnityEngine.Object.DestroyImmediate(Box.cancelButton.gameObject);
            MapRoomDriver.Active = true;
        }
    }

    public static int RunFlightOutcomes()
    {
        _count = 0;
        foreach (float scale in new[] { .05f, 1f, 198.12f })
        {
            using (var fixture = new FlightSetup629(scale))
            {
                var sold = fixture.OfferOwned(); fixture.Box.confirmButton.onClick.Invoke(); TownServiceMerchantHandoff.Tick();
                Check(sold.TownOffering && !sold.IsCollapsing,
                    "confirmed sale waits for authoritative native inventory before choosing its terminal destination");
                fixture.Character.AllCharacterItems.Remove(fixture.Owned); TownServiceMerchantHandoff.Tick();
                Check(sold.IsCollapsing && !sold.TownOffering && TownServiceMerchantHandoff.OwnedChips.Contains(sold),
                    "successful native sale retains the original merchant absorption instead of returning to the seller fan");
                Check(sold.TryTownReturnMotion(sold.transform, fixture.Root.transform, null, out uint revision, out float[] motion)
                    && revision != 0 && motion[2] == 3, "native sold original publishes its exact terminal collapse clock");
                Check((fixture.Root.transform.TransformPoint(new Vector3(motion[14], motion[15], motion[16])) - fixture.Palm.position).magnitude / scale < .0001f,
                    "successful native sale terminal endpoint is merchant palm, never seller wrist");
                var observer = new GameObject("Native sale observer").transform; observer.SetParent(fixture.Root.transform, false);
                for (int frame = 0; frame <= 20; frame++)
                {
                    float age = motion[1] * frame / 20f;
                    GloomhavenVR.Net.TownServices.TownCardReturnMotion.Apply(observer, fixture.Root.transform, fixture.Root.transform, 0, motion, age);
                    float t = frame / 20f, ease = t * t * ((motion[3] + 1f) * t - motion[3]);
                    Vector3 original = Vector3.LerpUnclamped(new Vector3(motion[4], motion[5], motion[6]), new Vector3(motion[14], motion[15], motion[16]), ease);
                    Check((observer.position - fixture.Root.transform.TransformPoint(original)).magnitude / scale < .0001f,
                        "remote terminal sale evaluates the authentic owner curve every render frame");
                }
                TownServicePopulation.Station!.Near = false; TownServiceMerchantHandoff.Tick();
                Check(!TownServiceMerchantHandoff.Active && TownServiceCardFlights.Returning.Contains(sold) && sold != null,
                    "walk-away retains only the terminal sold original independently of the retired private NPC session");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                var cancelled = fixture.OfferOwned(); fixture.Box.cancelButton.onClick.Invoke();
                Check(!cancelled.TownOffering && !cancelled.IsCollapsing && cancelled.TownReturnActive,
                    "native cancel immediately returns owned original to the canonical seller fan");
                Check(cancelled.TryTownReturnMotion(cancelled.transform, fixture.Root.transform, null, out _, out var motion) && motion[2] == 1,
                    "cancel owns the authentic fan glide, distinct from terminal merchant absorption");
                cancelled.AdvanceInspectionReturn(1f);
                Check((cancelled.transform.localPosition - cancelled.Home).sqrMagnitude < .000001f
                    && Quaternion.Angle(cancelled.transform.localRotation, cancelled.HomeRotation) < .1f,
                    "cancel return lands at its exact fan position and original face rotation");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                var token = fixture.OfferStock(); var prepared = TownServiceMerchantHandoff.PreparedPurchase;
                var front = prepared?.NativeItemCard;
                Check(prepared != null && !TownServiceMerchantHandoff.OwnedChips.Contains(prepared) && !fixture.Character.AllCharacterItems.Contains(fixture.Stock),
                    "prepared purchase original never represents native ownership or joins the fan before success");
                fixture.Box.confirmButton.onClick.Invoke(); TownServiceMerchantHandoff.Tick();
                Check(token.Returns == 0 && token.Restores == 0, "unresolved native purchase cannot manufacture an owned copy or cabinet return");
                CItem purchased = new(fixture.Stock.ID); fixture.Character.AllCharacterItems.Add(purchased);
                TownServiceMerchantHandoff.Tick();
                var actual = TownServiceMerchantHandoff.OwnedChips.Single(c => ReferenceEquals(c.Item, purchased));
                Check(ReferenceEquals(actual, prepared) && ReferenceEquals(actual.NativeItemCard, front)
                    && ReferenceEquals(front!.item, purchased) && front.StateRefreshes > 0,
                    "native purchase adopts the same prepared widget onto actual model without source or hierarchy churn");
                Check(actual.TownReturnActive && token.Restores == 1 && token.Returns == 0,
                    "confirmed purchase flies actual newly owned copy to fan without a second stock sample return flight");
                Check(actual.TryTownReturnMotion(actual.transform, fixture.Root.transform, null, out _, out var motion) && motion[2] == 1,
                    "newly owned purchase uses the same existing source clock as normal owned-item returns");
                TownServiceMerchantHandoff.Tick(); TownServiceMerchantHandoff.Tick();
                Check(actual != null && TownServiceMerchantHandoff.OwnedChips.Count(c => ReferenceEquals(c.Item, purchased)) == 1,
                    "post-purchase inventory Tick keeps exactly one prepared original without a retired emergence reference");
                actual.AdvanceInspectionReturn(1f);
                Check((actual.transform.localPosition - actual.Home).sqrMagnitude < .000001f,
                    "native purchased copy lands at the actual owned fan seat");
                Check(actual.GetComponent<BoxCollider>().enabled, "owned purchased original is normally grabbable after its authentic arrival");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                var cancelled = fixture.OfferStock(); var prepared = TownServiceMerchantHandoff.PreparedPurchase; fixture.Box.cancelButton.onClick.Invoke();
                Check(prepared == null && TownServiceMerchantHandoff.PreparedPurchase == null,
                    "purchase cancellation retires its unowned prepared ghost without changing inventory");
                Check(cancelled.Returns == 1 && cancelled.Restores == 0 && fixture.Character.AllCharacterItems.Count == 1,
                    "stock cancel returns the borrowed original to cabinet without adding an owned item");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                var swapped = fixture.OfferOwned(); var incoming = fixture.OfferStock();
                Check(!swapped.TownOffering && swapped.TownReturnActive && incoming.Parks == 1,
                    "owned-to-stock swap returns only displaced owned original and retains incoming borrowed sample");
            }
            foreach (bool switchCharacter in new[] { true, false })
            foreach (bool selling in new[] { true, false })
            using (var fixture = new FlightSetup629(scale))
            {
                ItemsPile.ItemChip? chip = selling ? fixture.OfferOwned() : null;
                TownServiceToken? token = selling ? null : fixture.OfferStock();
                fixture.Box.confirmButton.onClick.Invoke(); TownServiceMerchantHandoff.Tick();
                int cancels = fixture.Box.Cancels;
                if (switchCharacter) MapCharacterSelection.Selected = new CMapCharacter { CharacterName = "Tinkerer" };
                else TownServicePopulation.Station!.Near = false;
                TownServiceMerchantHandoff.Tick();
                Check(fixture.Box.Cancels == cancels && TownServiceCardFlights.HasRetained,
                    "character switch cannot reinterpret already-confirmed native ownership request as cancel");
                if (selling) fixture.Character.AllCharacterItems.Remove(fixture.Owned);
                else fixture.Character.AllCharacterItems.Add(new CItem(fixture.Stock.ID));
                TownServiceCardFlights.Tick();
                Check(selling ? chip != null && chip.IsCollapsing && TownServiceCardFlights.Returning.Contains(chip)
                    : token != null && token.Restores == 1 && token.Returns == 0 && TownServiceCardFlights.Returning.Count != 0,
                    "retired confirmed outcome uses original character inventory and retains its exact terminal native flight");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                var unresolved = fixture.OfferOwned(); fixture.Box.confirmButton.onClick.Invoke(); TownServiceMerchantHandoff.Tick();
                typeof(TownServiceMerchantHandoff).GetField("_tradeUntil", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Time.unscaledTime - 1f);
                TownServiceMerchantHandoff.Tick();
                Check(!unresolved.IsCollapsing && !unresolved.TownOffering && unresolved.TownReturnActive && fixture.Character.AllCharacterItems.Contains(fixture.Owned),
                    "confirmed timeout never guesses a sale or changes native inventory, and returns the original to its source");
            }
            using (var fixture = new FlightSetup629(scale))
            {
                ItemsPile.ItemChip.NewArtReady = false;
                var token = fixture.OfferStock(); fixture.Box.confirmButton.onClick.Invoke(); TownServiceMerchantHandoff.Tick();
                TownServicePopulation.Station!.Near = false; TownServiceMerchantHandoff.Tick();
                fixture.Character.AllCharacterItems.Add(new CItem(fixture.Stock.ID)); TownServiceCardFlights.Tick();
                var cold = TownServiceCardFlights.Returning.Single();
                Check(cold.InspectionArtPending && !cold.IsCollapsing && cold.transform.localScale == Vector3.zero,
                    "retired cold purchase waits for actual native front before its closed-wrist return starts");
                cold.SetArtReady(true); cold.TickInspectionArtArrival();
                Check(cold.IsCollapsing && cold.transform.localScale.x > 0f && token.Returns == 0,
                    "cold purchased original starts one closed-wrist return from its merchant pose when artwork arrives");
            }
        }
        return _count;
    }
}
