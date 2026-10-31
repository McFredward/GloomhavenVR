using System;using System.IO;using System.Reflection;using System.Text;using GloomhavenVR.Core;using UnityEngine;
static class Program {
 static int count;static void Check(bool value,string why){if(!value)throw new Exception(why);count++;}
 static string Row(string name="Hit",int samples=24591,int channels=4)=>Convert.ToBase64String(Encoding.UTF8.GetBytes(name))+"\tassets/audio/"+new string('a',32)+".ogg\t"+samples+"\t48000\t"+channels;
 static void Reset(string row="") {
  foreach(string name in new[]{"Entries","Clips"})typeof(QuestCombatAudioBank).GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!.GetType().GetMethod("Clear")!.Invoke(typeof(QuestCombatAudioBank).GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null),null);
  foreach(string name in new[]{"_attempted","_reported"})typeof(QuestCombatAudioBank).GetField(name,BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,false);
  typeof(QuestCombatAudioBank).GetField("_bank",BindingFlags.NonPublic|BindingFlags.Static)!.SetValue(null,null);
  AssetBundle.Opens=AssetBundle.Reads=VRLog.Notes=VRLog.Debugs=0;AssetBundle.Available=new();AssetBundle.Corrected=new(){channels=4};QuestStandalonePlatform.Enabled=VRSession.IsRunning=true;
  File.WriteAllText(Path.Combine(RuntimeDepsLoader.PluginDir,"ghvr-quest-native-audio.tsv"),"GHVRQ_AUDIO_1\n"+(row==""?Row():row)+"\n");
 }
 static int Main(){string root=Path.Combine(Path.GetTempPath(),"ghvrq-audio-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);RuntimeDepsLoader.PluginDir=root;
 try {
  foreach(bool desktop in new[]{true,false}){Reset();var s=new AudioSource{clip=new()};if(desktop)QuestStandalonePlatform.Enabled=false;else VRSession.IsRunning=false;QuestCombatAudioBank.Prepare(s);Check(AssetBundle.Opens==0&&AssetBundle.Reads==0&&s.clip.channels==1,"disabled-scope");}
  foreach(int channels in new[]{2,4}){Reset();var s=new AudioSource{clip=new(){channels=channels}};QuestCombatAudioBank.Prepare(s);Check(AssetBundle.Opens==0,"correct-source-untouched");}
  foreach(int mismatch in new[]{0,1,2}){Reset();var clip=new AudioClip();if(mismatch==0)clip.name="ForeignHit";if(mismatch==1)clip.frequency=44100;if(mismatch==2)clip.samples++;var s=new AudioSource{clip=clip};QuestCombatAudioBank.Prepare(s);Check(AssetBundle.Opens==0&&s.clip==clip,"exact-native-identity");}
  Reset();var original=new AudioClip();var source=new AudioSource{clip=original,timeSamples=7};QuestCombatAudioBank.Prepare(source);Check(source.clip==AssetBundle.Corrected&&source.clip!.channels==4,"original-channel-repair");Check(source.volume==.5f&&source.pitch==1.2f&&source.timeSamples==7&&!source.loop,"playback-properties");
  QuestCombatAudioBank.Prepare(source);Check(AssetBundle.Opens==1&&AssetBundle.Reads==1,"retained-small-bank");Check(VRLog.Debugs==1,"bounded-report");
  QuestCombatAudioBank.Restore(source);Check(source.clip==original,"pool-restoration");QuestCombatAudioBank.Prepare(source);Check(AssetBundle.Opens==1&&AssetBundle.Reads==1&&VRLog.Debugs==1,"reuse-no-load-or-log");
  var second=new AudioSource{clip=source.clip};QuestCombatAudioBank.Inherit(source,second);QuestCombatAudioBank.Restore(second);Check(second.clip==original,"secondary-owner");
  var replacement=new AudioClip();source.clip=replacement;source.timeSamples=15;QuestCombatAudioBank.Prepare(source);Check(source.clip==AssetBundle.Corrected&&source.timeSamples==15,"native-retarget-without-stop");QuestCombatAudioBank.Restore(source);Check(source.clip==replacement&&source.timeSamples==15,"retarget-lease-owner");
  source.clip=replacement;QuestCombatAudioBank.Prepare(source);QuestCombatAudioBank.Prepare(source);QuestCombatAudioBank.Restore(source);Check(source.clip==replacement,"same-clip-retrigger-without-stop");
  var foreign=new AudioClip{name="Foreign"};source.clip=foreign;QuestCombatAudioBank.Restore(source);Check(source.clip==foreign,"foreign-writer");
  Reset();AssetBundle.Corrected=new(){channels=1};source=new(){clip=new()};var retained=source.clip;QuestCombatAudioBank.Prepare(source);Check(source.clip==retained&&VRLog.Notes==1,"bad-bank-original-retained");QuestCombatAudioBank.Prepare(new(){clip=new()});Check(VRLog.Notes==1,"bounded-failure");
  foreach(string malformed in new[]{Row(channels:1),Row(samples:-1),Row()+"\textra",Row()+"\n"+Row(),Row().Replace("assets/audio/","assets/../aa/")}){Reset(malformed);source=new(){clip=new()};retained=source.clip;QuestCombatAudioBank.Prepare(source);Check(source.clip==retained&&AssetBundle.Opens==0&&VRLog.Notes==1,"invalid-index-rejected");}
  Console.WriteLine("PASS original-channel audio substitution: "+count+" assertions; no headset quality claim");return 0;
 } finally{Directory.Delete(root,true);} }
}
