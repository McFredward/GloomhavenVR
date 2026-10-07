using System;
using System.Reflection;
using Object=UnityEngine.Object;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;
using GloomhavenVR.Net.TownServices;

public sealed class FakeCharacter { public string CharacterID="owned"; }
public sealed partial class MapRoomHand
{
    public static FakeCharacter? Selected;
    public static FakeCharacter? OwnedMerchantCharacter()=>Selected;
    internal static readonly MapRoomHand s_live=new();
    internal bool _engaged=true;
    public static bool TempleInspection { get=>s_live._templeInspection; set=>s_live._templeInspection=value; }
    public static int AbilityPublishes,AbilityReleases;
    private void ReleaseFan(string reason){AbilityReleases++;CardsDriver.OffScenarioFanCards=null;}
    private void RebuildFan(){if(!TownInspection){AbilityPublishes++;CardsDriver.OffScenarioFanCards=new object();}}
}
public static class CardsDriver
{
    public static object? OffScenarioFanCards;
    public static bool OffScenarioFanIsOpen;
    public static void SuppressNextOffScenarioFanEdgeSound(bool open){}
}
public sealed class FakeTempleInventory
{
    public CanvasGroup slotsCanvasGroup=null!;public readonly System.Collections.Generic.List<UITempleShopSlot> slots=new();
    public readonly UnityEngine.Events.UnityEvent<object> OnBlessingSelected=new();public int Displays,Refreshes;public FakeTempleTooltip tooltip=null!;
    public void OnHovered(bool shown,UITempleShopSlot slot){tooltip.Populate(shown,slot);}
    public void Display(System.Collections.Generic.List<MapRuleLibrary.YML.Locations.TempleYML.TempleBlessingDefinition> blessings,FakeTempleService service)
    {Displays++;for(int i=0;i<slots.Count&&i<blessings.Count;i++)slots[i].Blessing=blessings[i];}
    public void Refresh(FakeCharacter character){Refreshes++;}
}
public sealed class FakeTempleOffering { public bool Available=true,VisitorPresent=true; }
public sealed class FakeTempleService
{
    public bool Affordable=true,Available=true,Permission=true;
    public readonly System.Collections.Generic.List<MapRuleLibrary.YML.Locations.TempleYML.TempleBlessingDefinition> Blessings=new(){new()};
    public int Gold,DevotionLevel=1,DevotionCurrentProgress,NextDevotionLevelAmount=100,Buys;
    public void Buy(string id,MapRuleLibrary.YML.Locations.TempleYML.TempleBlessingDefinition blessing){Buys++;Gold+=10;DevotionCurrentProgress+=10;}
    public int CalculateDevotionTotalProgress(int level)=>level*100;
    public System.Collections.Generic.List<MapRuleLibrary.YML.Locations.TempleYML.TempleBlessingDefinition> GetAvailableBlessings()=>Blessings;
    public int CalculateTotalGoldDonated()=>Gold;
    public bool IsAvailable(string id,object blessing)=>Available;
    public bool CanAfford(string id,object blessing)=>Affordable;
    public bool CanBuy(string id,object blessing)=>Permission;
}
public sealed class UITempleShopSlot:MonoBehaviour
{
    public Button button=null!;public object Blessing=new();public bool IsAvailable=true;
}
internal static class DonationProof
{
    internal static int Run()
    {
        int checks=0;void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
        for(int scenario=0;scenario<14;scenario++)
        {
            var root=new GameObject("Native temple",typeof(CanvasGroup),typeof(UITempleWindow));
            var events=new GameObject("Events",typeof(EventSystem));
            UITempleWindow temple=root.GetComponent<UITempleWindow>();temple.Shop.slotsCanvasGroup=root.GetComponent<CanvasGroup>();
            MapRoomHand.Selected=temple.character;
            var source=new GameObject("Blessing",typeof(RectTransform),typeof(Button),typeof(UITempleShopSlot));source.transform.SetParent(root.transform,false);
            var slot=source.GetComponent<UITempleShopSlot>();slot.button=source.GetComponent<Button>();
            // Native confirmation is its own canvas, not a child of the temple
            // inventory group that the game locks while the modal is open.
            var prompt=new GameObject("Original confirm",typeof(UIWindow),typeof(UIEnhancementConfirmationBox));
            var box=prompt.GetComponent<UIEnhancementConfirmationBox>();Singleton<UIEnhancementConfirmationBox>.Instance=box;
            var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Button));confirm.transform.SetParent(prompt.transform,false);box.confirmButton=confirm.GetComponent<Button>();
            int commits=0,selections=0,cancels=0;
            FakeTempleOffering? offering=null;
            slot.button.onClick.AddListener(()=>
            {
                if(temple._isConfirmationBoxOpened)return;
                selections++;temple._isConfirmationBoxOpened=true;
                box.Show(()=>{commits++;temple._isConfirmationBoxOpened=false;},()=>{cancels++;temple._isConfirmationBoxOpened=false;});
                if(scenario==8){temple.Shop.slotsCanvasGroup.interactable=false;slot.button.interactable=false;offering!.Available=false;}
            });
            var ritual=new BoundRitual(()=>true,()=>temple.character);
            offering=ritual._templeOffering;
            TownServiceMirror.TransactionActive=TownServiceMirror.Settled=false;
            TownServiceMirror.Denied=TownServiceMirror.Unavailable=false;
            TownServiceMirror.CanBegin=scenario!=9;
            int voiceBefore=TownServiceVoice.Donations;
            int revisionBefore=TownServiceMirror.Commits;
            if(scenario==1)temple.service.Affordable=false;
            if(scenario==2)MapRoomHand.Selected=new FakeCharacter{CharacterID="observer"};
            if(scenario==3)temple.service.Permission=false;
            if(scenario==4)ritual._templeOffering.Available=false;
            if(scenario==5)slot.IsAvailable=false;
            bool accepted=ritual.Donate(temple,slot);
            bool eligible=scenario==0||scenario>=6&&scenario!=9;
            Check(accepted==eligible && commits==0&&selections==0,"purse parks without running native selection before transaction arbitration");
            if(accepted)
            {
                ritual.TickPendingTempleDonation();
                Check(selections==0&&commits==0,"unsettled transaction claim never enters the native donation callback");
                if(scenario==10)
                {
                    object pending=typeof(BoundRitual).GetField("_pendingTempleDonation",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ritual)!;
                    pending.GetType().GetField("Started",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(pending,Time.unscaledTime-2.1f);
                    ritual.TickPendingTempleDonation();
                    Check(ritual.TempleGrantStalled&&!TownServiceMirror.TransactionActive&&selections==0&&commits==0,
                        "missing host grant returns the purse and exposes native Temple fallback before selection");
                    Object.DestroyImmediate(prompt);Object.DestroyImmediate(root);Object.DestroyImmediate(events);
                    continue;
                }
                if(scenario==11||scenario==12)
                {
                    TownServiceMirror.Denied=scenario==11;
                    TownServiceMirror.Unavailable=scenario==12;
                    ritual.TickPendingTempleDonation();
                    Check(!TownServiceMirror.TransactionActive&&selections==0&&commits==0
                        && ritual.TempleGrantStalled==(scenario==12),
                        scenario==11
                            ? "host Busy returns only this purse without reopening the original window"
                            : "unreachable host returns purse and restores the original VR Temple window");
                    Object.DestroyImmediate(prompt);Object.DestroyImmediate(root);Object.DestroyImmediate(events);
                    continue;
                }
                TownServiceMirror.Settled=true;
                ritual.TickPendingTempleDonation();
                if(scenario==13)TownServiceMirror.Settled=false;
            }
            if(scenario==8)Check(accepted,"native modal input lock does not invalidate its own donation");
            if(scenario==6)ritual._templeOffering.Available=ritual._templeOffering.VisitorPresent=false;
            if(accepted)box.Complete();
            if(scenario==13)Check(commits==0&&cancels==1,
                "revoked host grant cancels the original donation callback at hidden completion");
            if(scenario==6)
            {
                Check(commits==0&&cancels==1,"walking away before native completion cancels the donation");
                ritual._templeOffering.Available=ritual._templeOffering.VisitorPresent=true;
                Check(ritual.Donate(temple,slot),"cancelled offering can be presented again after returning");ritual.TickPendingTempleDonation();box.Complete();
                Check(commits==1,"retry after cancelled purse creates one native donation");
            }
            if(scenario==0||scenario==7||scenario==8)
            {
                Check(commits==1,"deliberate purse drop invokes exactly one native payment callback");
                if(scenario!=8)Check(!ritual.Donate(temple,slot)&&selections==1,"a delayed online stock refresh never permits a duplicate donation");
                if(scenario==7)
                {
                    temple.character=new FakeCharacter{CharacterID="other-owned"};MapRoomHand.Selected=temple.character;
                    Check(ritual.Donate(temple,slot),"another owned character retains its independent donation choice");ritual.TickPendingTempleDonation();box.Complete();
                    Check(commits==2,"character-specific donation latch does not consume another character's money");
                }
            }
            Check(TownServiceVoice.Donations-voiceBefore==commits,"resident speaks only after each guarded native donation callback");
            Check(TownServiceMirror.Commits-revisionBefore==commits,
                "shared blessing revision advances only after each native donation callback, never on availability changes");
            if (commits > 0) Check(!TownServiceMirror.TransactionActive,
                "successful donation retires its short native commit reservation before the shared blessing finishes");
            if(scenario==9)Check(!accepted&&selections==0,
                "another player's same-priestess claim blocks this purse before it can be parked");
            Object.DestroyImmediate(prompt);Object.DestroyImmediate(root);Object.DestroyImmediate(events);
        }
        MapRoomHand.Selected=null;
        return checks;
    }
}
