using System;
using System.Collections.Generic;
using UnityEngine;
namespace GloomhavenVR.Core {
 internal static class VRLog { internal static void Warn(string s,string m) {} }
 internal static class HeadEar {
  internal static bool Ready=true; internal static HashSet<string> Claims=new();
  internal static bool Claim(string name) { if(Ready) Claims.Add(name); return Ready; }
  internal static void Release(string name) { Claims.Remove(name); }
 }
}
namespace GloomhavenVR.Net { internal static class NetProtocol {internal const float StaleTimeoutSeconds=3;} }
namespace GloomhavenVR.Net.TownServices {
 internal sealed class TownServiceSessionInfo { internal bool Active; internal byte Service; internal float ReceivedTime,SessionAge; }
 internal static class TownServiceMirror { internal static Dictionary<int,TownServiceSessionInfo> RemoteSessions=new(); }
}
namespace GloomhavenVR.WorldUI {
 internal static class TownServicePresentation {internal static bool Active; internal static byte Service; internal static float SessionAge;}
 internal static class TownServicePopulation {internal static Transform? Frame;internal static bool IsFaceAuthor;}
 internal struct TownActivityVisual { internal float Cast, Attention; }
 internal static class TownServiceAssets {
  internal static Dictionary<string,AudioClip> Clips=new(); internal static Dictionary<string,TextAsset> Curves=new();
  internal static AudioClip? Audio(string n)=>Clips.TryGetValue(n,out var c)?c:null;
  internal static TextAsset? Text(string n)=>Curves.TryGetValue(n,out var c)?c:null;
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
}
public sealed class GlobalData {public int MasterVolume=100,StoryVolume=100;}
public sealed class SaveData {public static SaveData? Instance=new(); public GlobalData? Global=new();}
