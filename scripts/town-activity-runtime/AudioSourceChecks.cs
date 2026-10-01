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
            Check(source!.clip!.length<=.5f&&source.volume<=.065f,
                "small coin contact has a short bounded duration and quiet gain");
            Check(source!.spatialBlend==1f&&source.dopplerLevel==0f&&source.rolloffMode==AudioRolloffMode.Linear,"resident foley has continuous linear falloff and no moving-rig Doppler");
            Check(Mathf.Abs(source.minDistance-79.248f)<.01f&&Mathf.Abs(source.maxDistance-1386.84f)<.01f,
                "resident range follows the map's world units per perceived metre");
            Check(.8f*root.lossyScale.x<source.maxDistance,
                "a visitor eight tenths of a metre from the source is inside audible range");
            Check(Vector3.Distance(source.transform.position,root.TransformPoint(released.Left))<.00001f,"coin foley originates at actual resident contact");
            root.localScale=Vector3.one*100f;
            FaceClock.Now=.015f;audio.Tick(1,1,2.015f,.005f,true,in released);
            Check(Mathf.Abs(source.minDistance-40f)<.01f&&Mathf.Abs(source.maxDistance-700f)<.01f,
                "range tracks a later map scale change without recreating the voice");
            WorldUIConfig.ImmersiveTownSoundEffects.Value=false;
            FaceClock.Now=.017f;audio.Tick(1,1,2.017f,.002f,true,in released);
            Check(!source.isPlaying&&HeadEar.Claims.Count==0,
                "local resident effects preference immediately stops foley and releases listener");
            WorldUIConfig.ImmersiveTownSoundEffects.Value=true;
            float resumedClock=4f;
            FaceClock.Now=.018f;audio.Tick(1,2,resumedClock,.001f,true,in held);
            for(int n=0;n<11;n++){FaceClock.Now+=.24f;resumedClock+=.24f;audio.Tick(1,2,resumedClock,.24f,true,in held);}
            FaceClock.Now+=.01f;resumedClock+=.01f;audio.Tick(1,2,resumedClock,.01f,true,in released);
            source=root.GetComponentsInChildren<AudioSource>().SingleOrDefault(item=>item.isPlaying);
            Check(source!=null&&source.isPlaying&&HeadEar.Claims.Count==1,
                "resident effects preference resumes on the next shared physical contact");
            float full=source!.volume;SaveData.Instance.Global.MasterVolume=50;SaveData.Instance.Global.SFXVolume=20;
            FaceClock.Now+=.01f;audio.Tick(1,1,2.02f,.01f,true,in released);
            Check(Mathf.Abs(source.volume-full*.1f)<.00001f,"native master and effects settings both apply live");
            SaveData.Instance.Global.MasterVolume=0;
            FaceClock.Now+=.01f;audio.Tick(1,1,2.03f,.01f,true,in released);
            Check(source.volume==0f,"master mute immediately silences resident foley");
            Check(HeadEar.Claims.Count==1,"resident uses the shared head listener claim");
            FaceClock.Now+=.01f;audio.Tick(1,1,2.04f,.01f,false,in released);
            Check(!source.isPlaying&&HeadEar.Claims.Count==0,"hidden or disabled station stops sound and releases listener");
            FaceClock.Now+=.01f;audio.Tick(1,1,2.05f,.01f,true,in released);
            var voices=root.GetComponentsInChildren<AudioSource>();
            Check(voices.Length==2&&!voices.Any(item=>item.isPlaying),"reopening does not replay contact or leak voices");
            audio.Dispose();Check(HeadEar.Claims.Count==0,"resident disposal releases shared listener");
            Check(VRLog.Warnings==0,"normal native foley lifetime emits no warning");
            SaveData.Instance.Global.MasterVolume=100;SaveData.Instance.Global.SFXVolume=100;
            var blessingRoot=new GameObject("Shared blessing audio").transform;
            blessingRoot.localScale=Vector3.one*198.12f;
            using(var blessing=new TownServiceActivityAudio(blessingRoot,2))
            {
                FaceClock.Now=10f;TownServiceAssets.Spell=null;
                blessing.SampleBlessing(7,1,.1f,true);
                Check(blessingRoot.GetComponentsInChildren<AudioSource>().Length==0,
                    "unready blessing audio waits without consuming the shared event");
                TownServiceAssets.Spell=clip;
                FaceClock.Now=10.2f;blessing.SampleBlessing(7,1,.3f,true);
                var voice=blessingRoot.GetComponentInChildren<AudioSource>();
                Check(voice!=null && voice.clip==clip && Math.Abs(voice.time-.3f)<.02f,
                    "late ready blessing audio seeks the author age instead of restarting");
                voice!.time=.5f;blessing.SampleBlessing(7,1,.4f,true);
                Check(Math.Abs(voice.time-.5f)<.02f,
                    "repeated blessing event cannot restart its shared sound");
                blessing.SampleBlessing(7,2,2f,true);
                Check(!voice.isPlaying,
                    "expired blessing audio does not replay for late observers");
            }
            UnityEngine.Object.DestroyImmediate(blessingRoot.gameObject);
            var observerRoot=new GameObject("Authored contact observer").transform;
            using(var observer=new TownServiceActivityAudio(observerRoot,1))
            {
                var contactEvent=new GloomhavenVR.Net.TownActivitySoundState{Cue=1,Generation=7,StartedClock=20f};
                FaceClock.Now=20.2f;
                observer.Tick(1,9,100f,.01f,true,in released,false,20.2f,contactEvent);
                var voice=observerRoot.GetComponentInChildren<AudioSource>();
                Check(voice!=null && voice.clip==contact && Mathf.Abs(voice.time-.2f)<.02f,
                    "observer with no local contact edge seeks the authored foley age");
                voice!.time=.3f;
                observer.Tick(1,9,100.1f,.01f,true,in released,false,20.3f,contactEvent);
                Check(Mathf.Abs(voice.time-.3f)<.02f && observerRoot.GetComponentsInChildren<AudioSource>().Length==1,
                    "duplicate authored contact cannot restart its sound");
                contactEvent.Generation=8;contactEvent.StartedClock=19f;
                observer.Tick(1,9,100.2f,.01f,true,in released,false,20.4f,contactEvent);
                Check(observerRoot.GetComponentsInChildren<AudioSource>().Length==1,
                    "expired authored contact creates no late replay voice");
            }
            UnityEngine.Object.DestroyImmediate(observerRoot.gameObject);
            var lateRoot=new GameObject("Late authored spell clip").transform;
            using(var observer=new TownServiceActivityAudio(lateRoot,3))
            {
                var spellEvent=new GloomhavenVR.Net.TownActivitySoundState{Cue=2,Generation=1,StartedClock=30f};
                TownServiceAssets.Spell=null;FaceClock.Now=30f;
                observer.Tick(1,9,100f,.01f,true,in released,false,30f,spellEvent);
                Check(lateRoot.GetComponentsInChildren<AudioSource>().Length==0,
                    "unready authored foley waits without consuming the event");
                TownServiceAssets.Spell=clip;FaceClock.Now=30.25f;
                observer.Tick(1,9,100.1f,.01f,true,in released,false,30.25f,spellEvent);
                var voice=lateRoot.GetComponentInChildren<AudioSource>();
                Check(voice!=null && Mathf.Abs(voice.time-.25f)<.02f,
                    "late ready authored foley seeks the current age instead of replaying zero");
            }
            UnityEngine.Object.DestroyImmediate(lateRoot.gameObject);
        }
        finally
        {
            SaveData.Instance.Global.MasterVolume=100;SaveData.Instance.Global.SFXVolume=100;
            WorldUIConfig.ImmersiveTownSoundEffects.Value=true;
            AudioController.Items.Clear();TownServiceAssets.Coin=null;TownServiceAssets.Spell=null;
            UnityEngine.Object.DestroyImmediate(root.gameObject);UnityEngine.Object.DestroyImmediate(clip);
            UnityEngine.Object.DestroyImmediate(contact);
        }
        return count;
    }
}
