using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.Core { internal static class VRLog { internal static void Info(string scope,string value) { } internal static void Debug(string scope,string value) { } } }
namespace GloomhavenVR.WorldUI {
 internal sealed class NativeHost { internal GameObject HostGo=null!; }
 internal static partial class ActorBars {
  private const float DepthScanIntervalSeconds=2f;
  private static readonly List<Graphic> GraphicScratch=new();
  internal sealed class Adopted {
   internal NativeHost Panel=new();
   internal readonly List<(Graphic g,Material orig,Material inst)> DepthMats=new();
   internal readonly HashSet<int> DepthMatIds=new();
   internal readonly Dictionary<int,int> DepthMatRecords=new();
   internal readonly HashSet<int> RaycastOffIds=new();
   internal readonly List<(Graphic g,bool raycast)> RaycastOff=new();
   internal bool DepthLogged,ScanPhased;
   internal float NextDepthScan;
  }
  internal static Adopted Create(GameObject root)=>new() {Panel=new NativeHost {HostGo=root}};
  internal static void Scan(Adopted owner,bool occluded)=>ScanBarGraphics(owner,"Actual TMP depth fixture",occluded);
  internal static void Restore(Adopted owner)=>RestoreBarDepthTest(owner);
 }
}
