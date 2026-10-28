using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using GloomhavenVR.Quest.Editor;
namespace UnityEngine {
 public static class Application {public static string unityVersion="2021.3.5f1";}
 public static class Debug {public static void Log(object value){Console.WriteLine(value);}}
 public static class JsonUtility {public static string ToJson(object value,bool pretty){return new JavaScriptSerializer().Serialize(value);}}
}
namespace UnityEngine.Profiling { public static class Profiler {
 public static long GetMonoUsedSizeLong(){return GC.GetTotalMemory(false);}
 public static long GetTotalAllocatedMemoryLong(){return 1024;}
 public static long GetTotalReservedMemoryLong(){return 2048;}
}}
namespace UnityEditor {public static class EditorUtility {
 public static int unloadCalls;public static object savedStartupScene=new object();
 public static void UnloadUnusedAssetsImmediate(){++unloadCalls;}
}}
public static class MemoryWitness {
 sealed class AbandonedContext {public byte[] graph=new byte[64*1024*1024];public static int finalized;~AbandonedContext(){++finalized;}}
 [MethodImpl(MethodImplOptions.NoInlining)] static WeakReference Abandon(){return new WeakReference(new AbandonedContext());}
 static int checks;
 static void Assert(bool condition,string description){++checks;if(!condition)throw new Exception(description);}
 public static int Main(string[] args){
  Directory.CreateDirectory(args[0]);Directory.SetCurrentDirectory(args[0]);
  var scene=UnityEditor.EditorUtility.savedStartupScene;var unreachable=Abandon();
  QuestBuild.ReleaseCampaignBuildMemory(new string('a',64));
  Assert(AbandonedContext.finalized==1,"Actual managed context finalizer completed");
  Assert(!unreachable.IsAlive,"Unreachable completed-build graph released");
  Assert(UnityEditor.EditorUtility.unloadCalls==1,"One safe native-unload dispatch");
  Assert(Object.ReferenceEquals(scene,UnityEditor.EditorUtility.savedStartupScene),"Live startup scene preserved");
  var values=new JavaScriptSerializer().Deserialize<QuestBuild.CampaignMemoryReceipt>(File.ReadAllText(QuestBuild.CampaignMemoryReceiptPath));
  Assert(values.schema==1&&values.samples.Length==2,"Bounded two-sample actual memory receipt");
  Assert(values.samples[0].managedBytes>values.samples[1].managedBytes,"Actual managed memory drops after completed graph release");
  Assert(values.samples[1].stage=="after-player-memory-release","Exact stage boundary recorded");
  Assert(values.unreachableManagedMemoryCollected&&values.unusedImportedAssetsUnloadRequested,"Executed release operations reported");
  Assert(!values.playerBuildCompleted&&!values.headsetPictureVerified,"No fabricated Player/headset success");
  Assert(values.samples[0].workingSetBytes>=0&&values.samples[0].processMemoryObserved,"Actual process memory read");
  Console.WriteLine("PASS actual Mono memory release and scoped dispatch controls: "+checks);
  return 0;
 }
}
