// Original callback installation and game costs are controlled boundaries. Production
// Confirm/Click run against real Unity buttons, EventSystem and pointer event dispatch.
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
public class Singleton<T>{public static T Instance=default!;}
public class UIWindow:MonoBehaviour{public bool IsOpen;}
public class UITempleWindow:MonoBehaviour{public bool _isConfirmationBoxOpened;}
public class UINewEnhancementWindow:MonoBehaviour{public bool _isConfirmationBoxOpened;}
public class UIEnhancementConfirmationBox:MonoBehaviour
{public Action? _onConfirmCallback;public Button confirmButton=null!,cancelButton=null!;public Action? Hidden;public void Hide(){GetComponent<UIWindow>().IsOpen=false;Hidden?.Invoke();}}
public static class InteractionProgram
{
    private static int count;
    private static void Check(bool condition,string name){count++;if(!condition)throw new Exception(name);}
    public static int Run()
    {
        count=0;
        foreach(bool temple in new[]{false,true})for(int scenario=0;scenario<10;scenario++)
        {
            var root=new GameObject("RitualFixture");var events=new GameObject("Events",typeof(EventSystem));
            var source=new GameObject("NativeOffer",typeof(RectTransform),typeof(Button));source.transform.SetParent(root.transform,false);var button=source.GetComponent<Button>();
            var boxObject=new GameObject("NativeConfirmation",typeof(RectTransform),typeof(UIWindow),typeof(UIEnhancementConfirmationBox));boxObject.transform.SetParent(root.transform,false);
            var box=boxObject.GetComponent<UIEnhancementConfirmationBox>();Singleton<UIEnhancementConfirmationBox>.Instance=box;
            var confirm=new GameObject("Confirm",typeof(RectTransform),typeof(Button));confirm.transform.SetParent(boxObject.transform,false);box.confirmButton=confirm.GetComponent<Button>();
            var cancel=new GameObject("Cancel",typeof(RectTransform),typeof(Button));cancel.transform.SetParent(boxObject.transform,false);box.cancelButton=cancel.GetComponent<Button>();
            Component controller=temple?root.AddComponent<UITempleWindow>():root.AddComponent<UINewEnhancementWindow>();
            object owner=new(),item=new();bool eligible=true,alive=true;int selections=0,commits=0;
            Action previous=()=>{};box._onConfirmCallback=previous;
            void Own(bool value){if(controller is UITempleWindow t)t._isConfirmationBoxOpened=value;else ((UINewEnhancementWindow)controller)._isConfirmationBoxOpened=value;}
            box.Hidden=()=>Own(false);
            box.confirmButton.onClick.AddListener(()=>{box._onConfirmCallback?.Invoke();box.Hide();});
            box.cancelButton.onClick.AddListener(()=>{box.Hide();Own(false);});
            button.onClick.AddListener(()=>
            {
                selections++;if(scenario==8)return;
                box._onConfirmCallback=()=>commits++;box.GetComponent<UIWindow>().IsOpen=true;Own(scenario!=7);
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
            string label=scenario switch{0=>"one native confirmation succeeds",1=>"post-selection affordability refused",2=>"post-selection owner change refused",3=>"post-selection selected item change refused",4=>"existing unrelated prompt untouched",5=>"closed session refused",6=>"new native disablement refused",7=>"unowned new callback not confirmed",8=>"native selection refusal not confirmed",_=>"initial affordability refusal"};
            Check(result==(scenario==0)&&commits==(scenario==0?1:0),label);
            if(scenario==4)Check(selections==0&&ReferenceEquals(box._onConfirmCallback,previous),label);
            if(scenario==9)Check(selections==0,label);
            if(scenario is 1 or 2 or 3 or 5 or 6)Check(!box.GetComponent<UIWindow>().IsOpen && (controller is UITempleWindow t ? !t._isConfirmationBoxOpened : !((UINewEnhancementWindow)controller)._isConfirmationBoxOpened),"own stale prompt cancelled through native lifecycle");
            UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(events);
        }
        return count;
    }
}

internal static class TownServiceConfirmationMask { internal static void Begin(UIWindow window, Func<object?> identity) { } }
