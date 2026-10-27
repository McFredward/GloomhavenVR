using System;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;
using UnityEngine.UI;

internal static class Program
{
    private static int _assertions;
    private static void Check(bool value,string why){++_assertions;if(!value)throw new InvalidOperationException("City preconvert: "+why);}
    private static UIWindow Window(string name)=>new GameObject(name).AddComponent<UIWindow>();
    private static void Main()
    {
        var ready=Window("Native pooled ready");var quest=Window("Quest preview");var other=Window("Private merchant");
        foreach(bool room in new[]{false,true})foreach(bool open in new[]{false,true})foreach(bool exact in new[]{false,true})
        {
            ModalFallback.Fresh();MapRoomDriver.Active=room;MapCityEventReadyUp.Window=ready;ModalFallback.Quest=quest;
            if(open)ready.Show();else ready.Hide();ModalFallback.Prepare(exact?ready:other);
            bool expected=room&&open&&exact;
            Check(ModalFallback.RowAdopted==expected,"only exact open native city proposal in map room may prepare");
            Check(expected?string.Join(",",ModalFallback.Steps)=="release-quest,release-loadout,adopt-row":ModalFallback.Steps.Count==0,"native ownership release precedes original row adoption");
        }
        // Simulate both Update orders: before the rig/map tick has run at all, or
        // after it already prepared the same native subject. The real conversion
        // entry must obtain complete content before its first content snapshot.
        foreach(bool mapTickFirst in new[]{false,true})
        {
            ModalFallback.Fresh();MapRoomDriver.Active=true;MapCityEventReadyUp.Window=ready;ModalFallback.Quest=quest;ready.Show();
            if(mapTickFirst)ModalFallback.Prepare(ready);
            int before=ModalFallback.Steps.Count;
            Check(ModalFallback.ConvertEntry(ready),"actual city conversion entry reaches first content snapshot");
            string tail=string.Join(",",ModalFallback.Steps.GetRange(before,ModalFallback.Steps.Count-before));
            Check(tail=="release-hidden-render,release-screen-bind,release-quest,release-loadout,adopt-row,first-content-snapshot","actual entry prepares complete city content before first conversion snapshot");
            Check(ModalFallback.RowAdopted&&!ModalFallback.QuestHolding&&!ModalFallback.LoadoutHolding,"first snapshot has no old native-content owner");
        }
        ModalFallback.Fresh();MapRoomDriver.Active=true;MapCityEventReadyUp.Window=ready;ModalFallback.Quest=quest;
        Check(ModalFallback.ConvertEntry(other)&&!ModalFallback.RowAdopted,"unrelated ordinary windows keep original conversion entry");
        Check(string.Join(",",ModalFallback.Steps)=="release-hidden-render,release-screen-bind,first-content-snapshot","unrelated conversion does not reconcile city or quest owners");
        Check(ready.NativeShowCalls==6&&ready.NativeHideCalls==4,"presentation does not show, hide or automatically confirm native ready-up");
        Console.WriteLine($"City preconvert: {_assertions} assertions; production preparation and conversion-entry prefix passed. No final capture/headset claim.");
    }
}
