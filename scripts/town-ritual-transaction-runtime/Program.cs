// Production Confirm/Click and the complete deferred guard run inside real Unity.
// Native Show/OnConfirm/Hide scheduling is an explicit controlled boundary: the source
// clears cancellation listeners on confirm, invokes its stored callback only at Hidden,
// and never assigns the unused _onCancelCallback field. Capture models only the Harmony
// prefix dispatch; real patch installation is a separate integration requirement.
using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
namespace GloomhavenVR.Core { internal static class VRLog { internal static bool WantsDebug => false; internal static void Debug(string scope,string message) {} internal static void Note(string scope,string message) {} } }
namespace GloomhavenVR.WorldUI { internal enum TownVoiceReaction : byte { PriestessDonate } internal static class TownServiceVoice { internal static int Donations; internal static void RequestReaction(byte service, TownVoiceReaction reaction) { if (service == 2 && reaction == TownVoiceReaction.PriestessDonate) Donations++; } } }
public class Singleton<T>{public static T Instance=default!;}
public class UIWindow:MonoBehaviour{public bool IsOpen;}
public class UITempleWindow:MonoBehaviour{public bool _isConfirmationBoxOpened;public FakeCharacter character=new();public FakeTempleInventory Shop=new();public FakeTempleService service=new();}
public class UINewEnhancementWindow:MonoBehaviour{public bool _isConfirmationBoxOpened;}
public static class TownServiceConfirmationMask{public static void Begin(UIWindow window,Func<Action?> callback){}}
public class UIEnhancementConfirmationBox:MonoBehaviour
{
    public Action? _onConfirmCallback, Hidden, Cancel;
    public Button confirmButton=null!,cancelButton=null!;
    public void Show(Action callback,Action? cancelled)
    {
        TownServiceRitualConfirmationGuard.Capture(this,ref callback,ref cancelled);
        _onConfirmCallback=callback;Cancel=cancelled;Hidden=()=>Cancel?.Invoke();GetComponent<UIWindow>().IsOpen=true;
    }
    public void Confirm(){Hidden=()=>_onConfirmCallback?.Invoke();Hide();}
    public void Hide(){GetComponent<UIWindow>().IsOpen=false;}
    public void Complete(){Hidden?.Invoke();}
}
public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition,string name){count++;if(!condition)throw new Exception(name);}
    public static int Run()
    {
        count=0;
        foreach(bool temple in new[]{false,true})for(int scenario=0;scenario<18;scenario++)
        {
            var root=new GameObject("RitualFixture");var events=new GameObject("Events",typeof(EventSystem));
            var source=new GameObject("NativeOffer",typeof(RectTransform),typeof(Button));source.transform.SetParent(root.transform,false);var button=source.GetComponent<Button>();
            var boxObject=new GameObject("NativeConfirmation",typeof(RectTransform),typeof(UIWindow),typeof(UIEnhancementConfirmationBox));boxObject.transform.SetParent(root.transform,false);
            var box=boxObject.GetComponent<UIEnhancementConfirmationBox>();Singleton<UIEnhancementConfirmationBox>.Instance=box;
            var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Button));confirm.transform.SetParent(boxObject.transform,false);box.confirmButton=confirm.GetComponent<Button>();
            var cancel=new GameObject("Cancel",typeof(RectTransform),typeof(Button));cancel.transform.SetParent(boxObject.transform,false);box.cancelButton=cancel.GetComponent<Button>();
            Component controller=temple?root.AddComponent<UITempleWindow>():root.AddComponent<UINewEnhancementWindow>();
            object owner=new(),item=new();bool eligible=true,alive=true;int selections=0,commits=0,cancels=0;
            Action previous=()=>{};box._onConfirmCallback=previous;
            void Own(bool value){if(controller is UITempleWindow t)t._isConfirmationBoxOpened=value;else ((UINewEnhancementWindow)controller)._isConfirmationBoxOpened=value;}
            box.confirmButton.onClick.AddListener(box.Confirm);
            box.cancelButton.onClick.AddListener(box.Hide);
            button.onClick.AddListener(()=>
            {
                selections++;if(scenario==8)return;
                box.Show(()=>{commits++;Own(false);},()=>{cancels++;Own(false);});Own(scenario!=7);
                if(scenario==1)eligible=false;
                if(scenario==2)owner=new object();
                if(scenario==3)item=new object();
                if(scenario==5)alive=false;
                if(scenario==6)button.interactable=false;
            });
            if(scenario==4)box.GetComponent<UIWindow>().IsOpen=true;
            if(scenario==9)eligible=false;
            var bound=new BoundRitual(()=>alive,()=>owner);
            bool result=bound.Confirm(button,()=>item,controller,()=>eligible);
            bool accepted=scenario==0||scenario>=10;
            Check(commits==0,"native transaction waits for hidden fade completion");
            if(scenario==10)owner=new object();
            if(scenario==11)item=new object();
            if(scenario==12)eligible=false;
            if(scenario==13)alive=false;
            if(scenario==14)UnityEngine.Object.DestroyImmediate(source);
            if(scenario==16)button.interactable=false;
            if(scenario==17)box.Cancel?.Invoke();
            box.Complete();
            string label=scenario switch{0=>"one native confirmation succeeds",1=>"post-selection affordability refused",2=>"post-selection owner change refused",3=>"post-selection selected item change refused",4=>"existing unrelated prompt untouched",5=>"closed session refused",6=>"new native disablement refused",7=>"unowned new callback not confirmed",8=>"native selection refusal not confirmed",9=>"initial affordability refusal",10=>"delayed owner change cancels original transaction",11=>"delayed item change cancels original transaction",12=>"delayed affordability change cancels original transaction",13=>"delayed session closure cancels original transaction",14=>"destroyed native offer cancels original transaction",15=>"successful delayed native transaction",16=>"delayed native disablement cancels original transaction",_=>"cancelled release never commits at hidden completion"};
            bool shouldCommit=scenario==0||scenario==15;
            Check(result==accepted&&commits==(shouldCommit?1:0),label);
            if(scenario>=10&&scenario!=15)Check(cancels==1,label);
            if(scenario is 0 or >=10){box.Complete();box.Cancel?.Invoke();Check(commits==(shouldCommit?1:0)&&cancels==(shouldCommit?0:1),"duplicate hidden completion is one shot");}
            if(scenario==4)Check(selections==0&&ReferenceEquals(box._onConfirmCallback,previous),label);
            if(scenario==9)Check(selections==0,label);
            if(scenario is 1 or 2 or 3 or 5 or 6)Check(!box.GetComponent<UIWindow>().IsOpen && cancels==1 && (controller is UITempleWindow t ? !t._isConfirmationBoxOpened : !((UINewEnhancementWindow)controller)._isConfirmationBoxOpened),"own stale prompt cancelled through native lifecycle");
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(events);
        }
        var a=new GameObject("Scoped",typeof(UIEnhancementConfirmationBox));var b=new GameObject("Unrelated",typeof(UIEnhancementConfirmationBox));
        Action cb=()=>{};Action? cancelCb=()=>{};Action original=cb;Action? originalCancel=cancelCb;
        using(var scope=TownServiceRitualConfirmationGuard.Begin(a.GetComponent<UIEnhancementConfirmationBox>(),()=>false))
        {
            TownServiceRitualConfirmationGuard.Capture(b.GetComponent<UIEnhancementConfirmationBox>(),ref cb,ref cancelCb);
            Check(ReferenceEquals(cb,original)&&ReferenceEquals(cancelCb,originalCancel),"unrelated box retains its native callbacks");
        }
        TownServiceRitualConfirmationGuard.Capture(a.GetComponent<UIEnhancementConfirmationBox>(),ref cb,ref cancelCb);
        Check(ReferenceEquals(cb,original)&&ReferenceEquals(cancelCb,originalCancel),"scope exits without intercepting future flat prompts");
        UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);
        return count + DonationProof.Run() + TempleExitProof.Run() + TempleApproachProof.Run();
    }
}
