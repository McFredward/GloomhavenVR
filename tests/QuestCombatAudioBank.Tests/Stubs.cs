namespace UnityEngine {
 public class AudioClip { public string name="Hit"; public int channels=1,frequency=48000,samples=24591; }
 public class AudioSource { private AudioClip? _clip; public AudioClip? clip {get=>_clip;set{_clip=value;timeSamples=0;}} public float volume=.5f,pitch=1.2f; public int timeSamples; public bool loop; }
 public class AssetBundle {
  public static AssetBundle? Available=new(); public static int Opens,Reads; public static AudioClip? Corrected=new(){channels=4};
  public static AssetBundle? LoadFromFile(string path){Opens++;return Available;}
  public object? LoadAsset(string path,System.Type type){Reads++;return Corrected;}
 }
}
namespace GloomhavenVR.Core {
 public static class QuestStandalonePlatform { public static bool Enabled=true; }
 public static class VRSession { public static bool IsRunning=true; }
 public static class VRLog { public static bool WantsDebug=true; public static int Notes,Debugs;public static void Note(string a,string b){Notes++;}public static void Debug(string a,string b){Debugs++;} }
 public static class RuntimeDepsLoader { public static string PluginDir=""; }
}
