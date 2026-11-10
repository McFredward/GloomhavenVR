#pragma warning disable CS0649
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
internal sealed class CombatLogHandler:MonoBehaviour { }
namespace UnityEngine.UI { internal sealed class UIWindow:MonoBehaviour { } }
namespace GloomhavenVR.Core
{
 internal static class VRLog {internal static void Info(string area,string message) { }}
}
namespace GloomhavenVR.Rig {internal static class VRRigDriver {internal static int RigPoseVersion=1;} }
namespace GloomhavenVR.WorldUI
{
 // The source's native coordinator supplies these fields. Physical Transforms,
 // Canvas hierarchy, real BepInEx clamping and final camera pixels are not ports.
 internal sealed class ConvertedPanel
 {internal GameObject HostGo=null!;internal RectTransform HostRect=null!;internal Vector2 SharedDesignFrame,NativeDesignFrame;}
 internal sealed class HandlePort {internal bool IsGrabbed;}
 internal sealed class AnchorPort {internal void ReauthorOrigin(){}internal void RecacheRigLocal(Transform frame){}}
 internal static class PanelLayout
 {
  internal static float WorldScale=1;internal static Vector3 Anchor;internal static Quaternion Yaw=Quaternion.identity;
  internal static bool TryGetAnchor(out Vector3 anchor,out Quaternion yaw){anchor=Anchor;yaw=Yaw;return true;}
 }
 internal static class PanelPlacement
 {
  internal static bool ClampIntoView(Camera camera,float scale,ref Vector3 point,out Quaternion facing){facing=Quaternion.identity;return false;}
  internal static void Spawn(Camera camera,float scale,out Vector3 point,out Quaternion facing){point=camera.transform.position+camera.transform.forward*(.7f*scale);facing=Quaternion.identity;}
 }
 internal static class SharedWindowSize {internal static bool IsArmed(ConvertedPanel panel)=>false;}
 internal static partial class CanvasConversion {internal static Camera? WorldCamera;}
 internal static partial class WorldUIConfig
 {
  internal static ConfigEntry<float> CombatLogRight=null!,CombatLogUp=null!,CombatLogForward=null!,CanvasScaleMm=null!,CombatLogScale=null!;
  private static ConfigFile _file=null!;
  internal static void Bind(string path)
  {
   _file=new ConfigFile(path,false);_file.SaveOnConfigSet=false;
   CombatLogRight=_file.Bind("WorldUI","CombatLogRight",0f);CombatLogUp=_file.Bind("WorldUI","CombatLogUp",0f);CombatLogForward=_file.Bind("WorldUI","CombatLogForward",0f);
   CanvasScaleMm=_file.Bind("WorldUI","CanvasScaleMm",1f);BindScale();
  }
 }
 internal static partial class CombatLogProjection
 {
  // Declared native scheduling/chrome ports. They do not write the frame's size,
  // native root rectangle, host pose or config, which are the affected boundary.
  internal static ConvertedPanel? Panel;internal static Transform? _frame;internal static HandlePort? _handle;
  internal static bool _respawnRequested,_placedFromConfig,_healLogged;internal static int _facedPoseVersion;
  internal static float _barGap=.015f;private static readonly AnchorPort _anchor=new();
  private static void EnsureFrame() { }
  private static void CancelReFace() { }
  private static void ReseatIntoLiveFrame() { }
  private static void TickAnchorHousekeeping(Camera head,float scale) { }
  private static void ApplyAnchorMode() { }
  private static void SyncBar(Rect rect,float metersPerPixel) { }
  private static void SyncCloseX(Rect rect) { }
  private static void TickEmptyNote(Rect rect,float metersPerPixel) { }
  private static void TickCapLaser() { }
  internal static void Configure(ConvertedPanel panel,Transform frame)
  {Panel=panel;_frame=frame;_handle=new HandlePort();_placedFromConfig=true;_respawnRequested=false;}
  internal static void Tick()=>Place();
  internal static void Release()=>PersistLayout();
  internal static void Respawn(){_respawnRequested=true;Place();}
  internal static void Grip(bool value){_handle!.IsGrabbed=value;}
 }
}
