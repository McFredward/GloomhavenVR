using System;
using System.Linq;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

internal static class AudioSourceChecks
{
    private static int count;
    private static void Check(bool okay,string message){count++;if(!okay)throw new Exception(message);}
    internal static int Run()
    {
        count=0; var root=new GameObject("Resident audio test").transform;
        root.position=new Vector3(4,2,-7);root.rotation=Quaternion.Euler(0,33,0);root.localScale=Vector3.one*198.12f;
        AudioClip clip=AudioClip.Create("Native boundary coin",22050,1,22050,false);
        AudioClip contact=AudioClip.Create("coin-soft",10800,1,24000,false);
        try
        {
            TownServiceAssets.Coin=contact;
            AudioController.Items["PlaySound_ScenarioUIEquipmentToggle_Trinkets"]=new FixtureAudioItem{ subItems=new[]{new FixtureAudioSubItem{Clip=clip}}};
            using var audio=new TownServiceActivityAudio(root,1);
            var held=new TownActivityVisual { CoinGrip=Vector3.right,Left=new Vector3(.2f,1.1f,.3f) };
            var released=held;released.CoinGrip=Vector3.zero;
            FaceClock.Now=0;audio.Tick(1,1,2,.01f,true,in held);
            Check(root.GetComponentsInChildren<AudioSource>().Length==0,"late join creates no historical audio voice");
            FaceClock.Now=.01f;audio.Tick(1,1,2.01f,.01f,true,in released);
            var source=root.GetComponentInChildren<AudioSource>();
            Check(source!=null&&source.clip==contact&&source.clip!=clip,
                "coin contact uses the bundled short physical clink, never the long equipment UI toggle");
            Check(source!.clip!.length<=.45f&&source.volume<=.15f,
                "small coin contact has a short bounded duration and quiet gain");
            Check(source!.spatialBlend==1f&&source.dopplerLevel==0f,"resident foley is spatial and has no moving-rig Doppler");
            Check(Mathf.Abs(source.minDistance-148.59f)<.01f&&Mathf.Abs(source.maxDistance-891.54f)<.01f,
                "resident range follows the map's world units per perceived metre");
            Check(.8f*root.lossyScale.x<source.maxDistance,
                "a visitor eight tenths of a metre from the source is inside audible range");
            Check(Vector3.Distance(source.transform.position,root.TransformPoint(released.Left))<.00001f,"coin foley originates at actual resident contact");
            root.localScale=Vector3.one*100f;
            FaceClock.Now=.015f;audio.Tick(1,1,2.015f,.005f,true,in released);
            Check(Mathf.Abs(source.minDistance-75f)<.01f&&Mathf.Abs(source.maxDistance-450f)<.01f,
                "range tracks a later map scale change without recreating the voice");
            float full=source.volume;SaveData.Instance.Global.MasterVolume=50;SaveData.Instance.Global.SFXVolume=20;
            FaceClock.Now=.02f;audio.Tick(1,1,2.02f,.01f,true,in released);
            Check(Mathf.Abs(source.volume-full*.1f)<.00001f,"native master and effects settings both apply live");
            SaveData.Instance.Global.MasterVolume=0;
            FaceClock.Now=.03f;audio.Tick(1,1,2.03f,.01f,true,in released);
            Check(source.volume==0f,"master mute immediately silences resident foley");
            Check(HeadEar.Claims.Count==1,"resident uses the shared head listener claim");
            FaceClock.Now=.04f;audio.Tick(1,1,2.04f,.01f,false,in released);
            Check(!source.isPlaying&&HeadEar.Claims.Count==0,"hidden or disabled station stops sound and releases listener");
            FaceClock.Now=.05f;audio.Tick(1,1,2.05f,.01f,true,in released);
            Check(root.GetComponentsInChildren<AudioSource>().Length==1,"reopening does not replay contact or leak voices");
            audio.Dispose();Check(HeadEar.Claims.Count==0,"resident disposal releases shared listener");
            Check(VRLog.Warnings==0,"normal native foley lifetime emits no warning");
        }
        finally
        {
            SaveData.Instance.Global.MasterVolume=100;SaveData.Instance.Global.SFXVolume=100;
            AudioController.Items.Clear();TownServiceAssets.Coin=null;
            UnityEngine.Object.DestroyImmediate(root.gameObject);UnityEngine.Object.DestroyImmediate(clip);
            UnityEngine.Object.DestroyImmediate(contact);
        }
        return count;
    }
}
