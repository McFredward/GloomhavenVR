using System;
using System.Reflection;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GloomhavenVR.WorldUI.MapRoom { }
namespace GloomhavenVR.WorldUI
{
    // This is the explicit foreign-service boundary of the Temple fixture. The
    // quiet merchant/mage fixture binds its actual controller independently.
    internal static class TownServiceQuietController
    {
        internal static bool OriginalVisible(Transform source,UIWindow window,byte service)=>false;
    }
    internal static class TownServiceTempleController
    {
        internal static void AnimateProxy(UITempleWindow temple,int previousLevel)=>BoundTempleController.AnimateProxy(temple,previousLevel);
        internal static bool SelectOriginal(UITempleWindow temple,UITempleShopSlot slot)=>BoundTempleController.SelectOriginal(temple,slot);
    }
}
namespace MapRuleLibrary.YML.Locations { public static class TempleYML { public sealed class TempleBlessingDefinition { } } }
namespace I2.Loc { public static class LocalizationManager { public static string CurrentLanguage="English"; } }
public sealed class FakeGoldCounter:MonoBehaviour
{
    public int Sets,Value,Animations;public void SetCount(int value){Sets++;Value=value;}public void CountTo(int value){Animations++;Value=value;}
}
public sealed class FakeProgressBar:MonoBehaviour
{
    public int Sets,Animations;public void SetAmount(float current,float next){Sets++;}public void PlayProgressTo(float current,float next,Action? done=null){Animations++;done?.Invoke();}
}
public sealed class FakeTempleTooltip:MonoBehaviour
{
    public int Populates,Hides;
    public void Populate(bool shown,UITempleShopSlot slot){if(shown){Populates++;transform.SetParent(slot.transform,false);GetComponent<UIWindow>().Show();}else Hide();}
    public void Hide(){Hides++;GetComponent<UIWindow>().IsOpen=false;}
}
namespace MapRuleLibrary.Party { public sealed class CMapCharacter {public string CharacterID="owned",CharacterName="Owned";} }
public static class AdventureState {public static NativeMapState MapState=new();}
public sealed class NativeMapState {public bool IsCampaign=true;public NativeMapParty MapParty=new();}
public sealed class NativeMapParty {public System.Collections.Generic.List<MapRuleLibrary.Party.CMapCharacter> SelectedCharacters=new();}
public static class CharacterClassManager {public static int GetModelInstanceIDFromCharacterID(string id)=>id.GetHashCode();}
public static class PlayerRegistry {public static NativePlayer MyPlayer=new();}
public sealed class NativePlayer {public bool HasControlOver(int id)=>true;}
public static class AudioControllerUtils {public static void PlaySound(string sound){} }
public sealed class FakeLocalizedText:MonoBehaviour
{
    public int Sets;public void SetArguments(string value){Sets++;}
}
internal static class TempleQuietControllerProof
{
    internal static int Run()
    {
        int checks=0;
        void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
        // Each visit is a separate player's current selection; native destination and ownership
        // differ. The helpers are static in-game per client, so reset between the peer contexts.
        for(int peer=0;peer<2;peer++)
        {
            var parent=new GameObject("NativeTempleParent",typeof(RectTransform));
            RectTransform parentRect=(RectTransform)parent.transform;parentRect.sizeDelta=new Vector2(932f,611f);
            parentRect.pivot=new Vector2(.13f,.81f);parentRect.localScale=new Vector3(.83f,1.4f,.7f);
            parentRect.localRotation=Quaternion.Euler(12f,41f,-7f);
            var root=new GameObject("Exact original Temple",typeof(RectTransform),typeof(UIWindow),typeof(UITempleWindow));
            root.transform.SetParent(parent.transform,false);
            var temple=root.GetComponent<UITempleWindow>();var window=root.GetComponent<UIWindow>();
            var rows=new GameObject("NativePool",typeof(RectTransform));rows.transform.SetParent(root.transform,false);
            var original=new GameObject("Original Blessing",typeof(RectTransform),typeof(Button),typeof(UITempleShopSlot));
            original.transform.SetParent(rows.transform,false);var slot=original.GetComponent<UITempleShopSlot>();
            slot.button=original.GetComponent<Button>();temple.Shop.slots.Add(slot);
            GameObject Child(string name,Type type){var go=new GameObject(name,typeof(RectTransform),type);go.transform.SetParent(root.transform,false);return go;}
            var gold=Child("Original GoldCounter",typeof(FakeGoldCounter));
            var progress=Child("Original Progress",typeof(FakeProgressBar));
            var label=Child("Original DevotionLevel",typeof(FakeLocalizedText));
            temple.totalDonatedGold=gold.GetComponent<FakeGoldCounter>();
            temple.devotionProgress=progress.GetComponent<FakeProgressBar>();temple.devotionLevel=label.GetComponent<FakeLocalizedText>();
            RectTransform goldRect=(RectTransform)gold.transform;
            goldRect.anchorMin=new Vector2(.17f,.21f);goldRect.anchorMax=new Vector2(.71f,.83f);
            goldRect.pivot=new Vector2(.21f,.76f);goldRect.sizeDelta=new Vector2(233f,71f);
            goldRect.anchoredPosition3D=new Vector3(17f,-28f,3f);goldRect.localScale=new Vector3(.72f,1.19f,.91f);
            goldRect.localRotation=Quaternion.Euler(13f,-27f,31f);
            Vector2 min=goldRect.anchorMin,max=goldRect.anchorMax,pivot=goldRect.pivot,size=goldRect.sizeDelta;
            Vector3 position=goldRect.anchoredPosition3D,scale=goldRect.localScale;Quaternion rotation=goldRect.localRotation;
            int sibling=goldRect.GetSiblingIndex();
            // A nested native canvas is intentionally independently enabled. The island must
            // clock original coroutines without introducing any hidden flat renderer.
            var nested=new GameObject("Original Counter Canvas",typeof(RectTransform),typeof(Canvas));nested.transform.SetParent(gold.transform,false);
            Canvas nativeCanvas=nested.GetComponent<Canvas>();nativeCanvas.enabled=true;
            var first=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="first"}};
            var selected=new NewPartyCharacterUI{Data=new FakeCharacter{CharacterID="peer-"+peer}};
            NewPartyDisplayUI.PartyDisplay=new NewPartyDisplayUI{Slots=new[]{first,selected},SelectedUISlot=selected};
            MapRoomHand.Selected=selected.Data;
            GuildmasterDestinations.Mode=peer==0?EGuildmasterMode.Enchantress:EGuildmasterMode.WorldMap;
            TownServicePresentation.QuietWindow=window;
            root.SetActive(false);
            Check(BoundTempleController.Prepare(window),"quiet temple can prepare exact originals below an inactive native window");
            Check(window.Shows==0 && !window.IsOpen && !root.activeSelf,
                "quiet temple never opens or activates the flat window");
            Check(ReferenceEquals(temple.character,selected.Service)
                && ReferenceEquals(NewPartyDisplayUI.PartyDisplay.SelectedUISlot,selected)
                && ReferenceEquals(MapRoomHand.Selected,selected.Data),
                "quiet native context preserves each peer's exact selected character");
            Check(temple.Shop.Displays==1 && temple.Shop.Refreshes==1,
                "quiet native rows initialize once for the actual blessing pool");
            Check(BoundTempleController.OriginalVisible(original.transform,window)
                && !original.activeInHierarchy,"inactive window does not hide the valid original purse row");
            Check(gold.activeInHierarchy && progress.activeInHierarchy && !nativeCanvas.enabled,
                "only original book counter clocks are active with native flat rendering disabled");
            var clockGroup=gold.transform.parent.GetComponent<CanvasGroup>();
            Check(clockGroup!=null && clockGroup.alpha==0f && !clockGroup.blocksRaycasts,
                "native counter clock island remains invisible and has no laser occluder");
            for(int sample=0;sample<20;sample++)
            {
                typeof(BoundTempleController).GetField("_nextSample",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
                Check(BoundTempleController.Prepare(window),"unchanged model remains prepared");
            }
            Check(temple.Shop.Displays==1 && temple.Shop.Refreshes==1 && temple.totalDonatedGold.Sets==1,
                "standing at the priestess causes no repeated hidden native UI refresh");
            rows.SetActive(false);
            Check(!BoundTempleController.OriginalVisible(original.transform,window),"a hidden native row ancestor is never exposed by quiet admission");
            rows.SetActive(true);
            TownServicePresentation.QuietWindow=null;
            Check(!BoundTempleController.OriginalVisible(original.transform,window),"foreign or retired logical temple cannot publish original rows");
            TownServicePresentation.QuietWindow=window;
            var tip=Child("Original held modifier",typeof(FakeTempleTooltip));tip.AddComponent<UIWindow>();temple.Shop.tooltip=tip.GetComponent<FakeTempleTooltip>();
            BoundTempleController.HoverOriginal(temple,slot,true);
            Check(temple.Shop.tooltip.Populates==1 && tip.activeInHierarchy && tip.GetComponent<UIWindow>().IsOpen
                && tip.transform.parent.GetComponent<CanvasGroup>().alpha==0f,
                "quiet held purse uses the original populated tooltip below an invisible active clock island");
            BoundTempleController.HoverOriginal(temple,slot,false);
            Check(!tip.GetComponent<UIWindow>().IsOpen && ReferenceEquals(tip.transform.parent,slot.transform),
                "quiet original tooltip closes before returning to its inactive native parent");
            int selections=0;
            temple.Shop.OnBlessingSelected.AddListener(_=>selections++);
            Check(BoundTempleController.SelectOriginal(temple,slot) && selections==1,
                "deliberate quiet donation dispatches the exact original selection event");
            temple.service.Affordable=false;
            Check(!BoundTempleController.SelectOriginal(temple,slot) && selections==1,
                "unaffordable quiet donation never invokes original selection");
            temple.service.Affordable=true;temple.service.Permission=false;
            Check(!BoundTempleController.SelectOriginal(temple,slot) && selections==1,
                "observer without native ownership cannot invoke original selection");
            temple.service.Permission=true;
            MapRoomHand.Selected=first.Data;
            Check(!BoundTempleController.SelectOriginal(temple,slot),"selected character change invalidates the exact original quiet transaction");
            MapRoomHand.Selected=selected.Data;
            // Run the actual production arbitration -> native selection -> deferred confirmation
            // chain below an inactive native source. The fake native boundary is the same one
            // used by the existing callback race matrix, not a shortcut into service.Buy.
            var prompt=new GameObject("Original quiet confirmation",typeof(UIWindow),typeof(UIEnhancementConfirmationBox));
            var box=prompt.GetComponent<UIEnhancementConfirmationBox>();Singleton<UIEnhancementConfirmationBox>.Instance=box;
            int commits=0;
            temple.Shop.OnBlessingSelected.AddListener(_=>
            {
                if(temple._isConfirmationBoxOpened)return;
                temple._isConfirmationBoxOpened=true;
                box.Show(()=>{commits++;temple._isConfirmationBoxOpened=false;},()=>temple._isConfirmationBoxOpened=false);
            });
            var ritual=new BoundRitual(()=>true,()=>temple.character);
            GloomhavenVR.Net.TownServices.TownServiceMirror.TransactionActive=false;
            GloomhavenVR.Net.TownServices.TownServiceMirror.Settled=false;
            GloomhavenVR.Net.TownServices.TownServiceMirror.Denied=false;
            GloomhavenVR.Net.TownServices.TownServiceMirror.Unavailable=false;
            GloomhavenVR.Net.TownServices.TownServiceMirror.CanBegin=true;
            Check(ritual.Donate(temple,slot),"quiet owned donation parks while original window remains inactive");
            ritual.TickPendingTempleDonation();
            Check(commits==0 && !box.GetComponent<UIWindow>().IsOpen,"quiet donation waits for original multiplayer transaction grant");
            GloomhavenVR.Net.TownServices.TownServiceMirror.Settled=true;
            ritual.TickPendingTempleDonation();
            Check(box.Confirmations==1 && commits==0 && !window.IsOpen,
                "quiet inactive source selects the original confirmation without opening the old window");
            box.Complete();
            Check(commits==1 && !temple._isConfirmationBoxOpened,
                "quiet donation executes exactly one original payment callback at native hidden completion");
            box.Complete();
            Check(commits==1,"quiet native confirmation duplicate completion cannot spend again");
            Object.DestroyImmediate(prompt);
            int previousLevel;
            BoundQuietTempleProxyPresentation.Prefix(temple,out previousLevel);
            var nativeProxy=new BoundNativeTempleProxy(window,temple.service,(id,blessing)=>{throw new Exception("unexpected visible native proxy");});
            nativeProxy.ProxyBuyBlessing(temple.character.CharacterID,temple.service.Blessings[0]);
            BoundQuietTempleProxyPresentation.Postfix(temple,previousLevel);
            Check(temple.service.Buys==1 && temple.totalDonatedGold.Animations==1
                && temple.devotionProgress.Animations==1 && !window.IsVisible,
                "quiet native proxy commits once and animates original book after invisible window branch");
            BoundQuietTempleProxyPresentation.Prefix(temple,out previousLevel);
            TownServicePresentation.QuietWindow=null;
            BoundQuietTempleProxyPresentation.Postfix(temple,previousLevel);
            Check(temple.totalDonatedGold.Animations==1,"retired quiet context cannot animate an unrelated native proxy");
            TownServicePresentation.QuietWindow=window;
            typeof(BoundTempleController).GetField("_nextSample",BindingFlags.Static|BindingFlags.NonPublic)!.SetValue(null,0f);
            Check(BoundTempleController.Prepare(window) && temple.Shop.Refreshes==2
                && temple.totalDonatedGold.Sets==1 && temple.devotionProgress.Sets==1,
                "native donation model refresh preserves its already-running original counter animation");
            BoundTempleController.Reset();
            Check(ReferenceEquals(gold.transform.parent,root.transform) && goldRect.GetSiblingIndex()==sibling
                && goldRect.anchorMin==min && goldRect.anchorMax==max && goldRect.pivot==pivot && goldRect.sizeDelta==size
                && goldRect.anchoredPosition3D==position && goldRect.localScale==scale
                && Quaternion.Angle(goldRect.localRotation,rotation)<.001f && nativeCanvas.enabled,
                "quiet counter lease restores exact original parent sibling rect and nested canvas state");
            Check(!gold.activeInHierarchy && !progress.activeInHierarchy,
                "retired visit returns only the counters to their original inactive native window");
            TownServicePresentation.QuietWindow=null;MapRoomHand.Selected=null;NewPartyDisplayUI.PartyDisplay=null;
            Object.DestroyImmediate(parent);
        }
        return checks;
    }
}
