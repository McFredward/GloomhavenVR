using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace GloomhavenVR.Core {
 internal static class VRLog { internal static void Info(string scope,string value) { } internal static void Debug(string scope,string value) { } internal static void Warn(string scope,string value) { } }
 internal static class BundleShaders { internal static Shader? Resolve(string name,string scope,string found,string missing)=>Shader.Find(name); }
 internal static class VRLayers { internal const int ModLayer=31; }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver {internal static Camera? HeadCamera;} }
namespace GloomhavenVR.Hands {internal enum HandSide {Left,Right} }
namespace GloomhavenVR.Board.FigureGrab {
 using GloomhavenVR.Hands;
 internal sealed class FigureGrabbable {
  internal static FigureGrabbable? Left,Right;internal Renderer[]? Renderers;
  internal static FigureGrabbable? HeldBy(HandSide side)=>side==HandSide.Left?Left:Right;
  internal Renderer[]? HeldRenderers()=>Renderers;
 }
 internal sealed class GrabbableProp {
  internal static GrabbableProp? Left=null,Right=null;internal Renderer[]? Renderers=null;
  internal static GrabbableProp? HeldBy(HandSide side)=>side==HandSide.Left?Left:Right;
  internal Renderer[]? HeldRenderers()=>Renderers;
 }
}
namespace GloomhavenVR.WorldUI {
 internal static class FixtureNativeSettings {
  internal static void Ensure() {
   var field=typeof(TMPro.TMP_Settings).GetField("s_Instance",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
   if(field==null)throw new System.InvalidOperationException("Native TMP settings singleton binding drift");
   if(field.GetValue(null)==null)field.SetValue(null,ScriptableObject.CreateInstance<TMPro.TMP_Settings>());
  }
 }
 internal sealed class NativeHost { internal GameObject HostGo=null!; }
 internal static partial class ActorBars {
  private const float DepthScanIntervalSeconds=2f;
  private static readonly List<Graphic> GraphicScratch=new();
  internal sealed class Adopted {
   internal NativeHost Panel=new();
   internal readonly List<(Graphic g,Material orig,Material inst)> DepthMats=new();
   internal readonly HashSet<int> DepthMatIds=new();
   internal readonly Dictionary<int,int> DepthMatRecords=new();
   internal readonly HashSet<int> DepthTextIds=new();
   internal readonly List<(TMPro.TMP_Text text,System.Action<TMPro.TMP_TextInfo> callback)> DepthTextRefresh=new();
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
