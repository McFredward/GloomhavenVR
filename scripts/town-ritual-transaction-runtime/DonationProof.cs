using System;
using Object=UnityEngine.Object;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using GloomhavenVR.WorldUI;

public sealed class FakeCharacter { public string CharacterID="owned"; }
public static class MapRoomHand { public static FakeCharacter? Selected; public static FakeCharacter? OwnedMerchantCharacter()=>Selected;public static bool TempleInspection;public static void SetTempleInspection(bool value)=>TempleInspection=value; }
public sealed class FakeTempleInventory { public CanvasGroup slotsCanvasGroup=null!; }
public sealed class FakeTempleOffering { public bool Available=true,VisitorPresent=true; }
public sealed class FakeTempleService
{
    public bool Affordable=true,Available=true,Permission=true;
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
        for(int scenario=0;scenario<9;scenario++)
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
            int voiceBefore=TownServiceVoice.Donations;
            if(scenario==1)temple.service.Affordable=false;
            if(scenario==2)MapRoomHand.Selected=new FakeCharacter{CharacterID="observer"};
            if(scenario==3)temple.service.Permission=false;
            if(scenario==4)ritual._templeOffering.Available=false;
            if(scenario==5)slot.IsAvailable=false;
            bool accepted=ritual.Donate(temple,slot);
            bool eligible=scenario==0||scenario>=6;
            if(scenario==8)Check(accepted,"native modal input lock does not invalidate its own donation");
            Check(accepted==eligible && commits==0,"purse drop preserves native ownership, availability and affordability before delayed payment");
            if(scenario==6)ritual._templeOffering.Available=ritual._templeOffering.VisitorPresent=false;
            if(accepted)box.Complete();
            if(scenario==6)
            {
                Check(commits==0&&cancels==1,"walking away before native completion cancels the donation");
                ritual._templeOffering.Available=ritual._templeOffering.VisitorPresent=true;
                Check(ritual.Donate(temple,slot),"cancelled offering can be presented again after returning");box.Complete();
                Check(commits==1,"retry after cancelled purse creates one native donation");
            }
            if(scenario==0||scenario==7||scenario==8)
            {
                Check(commits==1,"deliberate purse drop invokes exactly one native payment callback");
                if(scenario!=8)Check(!ritual.Donate(temple,slot)&&selections==1,"a delayed online stock refresh never permits a duplicate donation");
                if(scenario==7)
                {
                    temple.character=new FakeCharacter{CharacterID="other-owned"};MapRoomHand.Selected=temple.character;
                    Check(ritual.Donate(temple,slot),"another owned character retains its independent donation choice");box.Complete();
                    Check(commits==2,"character-specific donation latch does not consume another character's money");
                }
            }
            Check(TownServiceVoice.Donations-voiceBefore==commits,"resident speaks only after each guarded native donation callback");
            Object.DestroyImmediate(prompt);Object.DestroyImmediate(root);Object.DestroyImmediate(events);
        }
        MapRoomHand.Selected=null;
        return checks;
    }
}
