using System;
using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.Core {
 internal static class VRLog {
  internal static readonly List<string> Warnings = new();
  internal static bool WantsDebug;
  internal static void Debug(string s,string m) { }
  internal static void Info(string s,string m) { }
  internal static void Warn(string s,string m) { Warnings.Add(m); }
 }
 internal static class HeadEar {
  internal static bool Ready=true; internal static HashSet<string> Claims=new();
  internal static bool Claim(string name) { if(Ready) Claims.Add(name); return Ready; }
  internal static void Release(string name) { Claims.Remove(name); }
 }
}
namespace GloomhavenVR.Net { internal static class NetPlayerActors { internal static int Local=1; internal static int LocalPlayerId()=>Local; } }
namespace GloomhavenVR.Net.TownServices {
 internal sealed class TownServiceSessionInfo { internal bool Active; internal byte Service; internal float ReceivedTime,LastSeenTime,SessionAge; }
 internal static class TownServiceMirror { internal static Dictionary<int,TownServiceSessionInfo> RemoteSessions=new(); internal static int Owner; internal static int TransactionOwner(byte service)=>service is 1 or 3 ? Owner : 0; }
}
namespace GloomhavenVR.WorldUI {
 internal static class StoryComposite { internal static bool PointOfNoReturn; }
 internal sealed class BoolSetting { internal bool Value=true; }
 internal static class WorldUIConfig { internal static readonly BoolSetting ImmersiveTownSpeech=new(); internal static readonly BoolSetting ImmersiveTownSoundEffects=new(); }
 internal static class TownServicePresentation {internal static bool Active; internal static byte Service; internal static float SessionAge;}
 internal static class TownServicePopulation {internal static Transform? Frame;internal static bool IsFaceAuthor;internal static uint PerformanceEpoch=1;}
 internal static class TownServiceAssets {
  internal static Dictionary<string,AudioClip> Clips=new(); internal static Dictionary<string,TextAsset> Curves=new();
  internal static AudioClip? Audio(string n)=>Clips.TryGetValue(n,out var c)?c:null;
  internal static TextAsset? Text(string n)=>Curves.TryGetValue(n,out var c)?c:null;
  internal static Shader? Shader(string n)=>n == "townnpc" ? UnityEngine.Shader.Find("GloomhavenVR/TownNpc") : null;
 }
}
namespace ClockStone {
 public sealed class AudioCategory {public string Name="";public AudioCategory? parentCategory;}
 public sealed class AudioObject { public AudioCategory? category; }
}
public sealed class AudioController {
 public static AudioController? Current=new();public bool DisableAudio;
 public static List<ClockStone.AudioObject> Playing=new();
 public static AudioController? DoesInstanceExist()=>Current;
 public static List<ClockStone.AudioObject> GetPlayingAudioObjects()=>Playing;
 public static bool IsValidAudioID(string id)=>false;
 internal static FixtureAudioItem GetAudioItem(string id)=>new();
}
public sealed class GlobalData {public int MasterVolume=100,StoryVolume=100,SFXVolume=100;}
public sealed class SaveData {public static SaveData? Instance=new(); public GlobalData? Global=new();}

// Explicit scene/endpoints: real Unity actors and imported WAVs are driven by the
// production code. Native save/payment callbacks, transport ownership/grants and
// final resident mesh authoring are outside this presentation-replay fixture.
internal static class ReplayClock { internal static float Now, Delta=1f/90f; }
internal sealed class FixtureAudioItem { internal FixtureAudioSubItem[] subItems=Array.Empty<FixtureAudioSubItem>(); }
internal sealed class FixtureAudioSubItem { internal AudioClip? Clip; }
namespace GloomhavenVR.Core {
 internal static class VRLayers { internal const int ModLayer=27; internal static void Apply(GameObject root)=>root.layer=ModLayer; }
}
namespace GloomhavenVR.Rig {
 internal static class VRRigDriver { internal static Camera? HeadCamera; }
}
namespace GloomhavenVR.Net {
 internal static class NetAvatarDriver {
  internal static readonly Dictionary<int,Vector3> Heads=new();
  internal static bool TryGetTownFaceHead(int player,out Vector3 point)=>Heads.TryGetValue(player,out point);
  internal static void CollectTownFacePeers(List<int> into) { foreach(int id in Heads.Keys)into.Add(id); }
 }
}
namespace GloomhavenVR.WorldUI {
 internal static class TownServiceRitualLayout { internal static readonly Vector3 Origin=new(0f,.978f,-.08f); }
 internal static class TownServiceTempleBowl {
  internal static readonly Vector3 Center=new(0f,.16f,.26f), PurseSeat=new(0f,.178f,.26f);
 }
 internal static class TownServiceDecor { internal static Transform? MoneyBagTemplate; }
}
