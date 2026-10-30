using System;
using System.Collections.Generic;
using UnityEngine;

namespace BepInEx.Configuration
{
    internal sealed class ConfigEntry<T> { internal T Value; internal ConfigEntry(T value) { Value=value; } }
    internal sealed class ConfigFile { internal ConfigEntry<T> Bind<T>(string section,string key,T value,string help)=>new(value); }
}
namespace Script.Controller { internal sealed class UnusedControllerBoundary { } }
internal static class Singleton<T> where T:new()
{ internal static T Instance=new(); internal static bool IsInitialized=true; }
internal sealed class TileBehaviour:MonoBehaviour { }
internal sealed class ProceduralMapTile:MonoBehaviour { internal BoxCollider BoxCollider=>GetComponent<BoxCollider>(); }
internal sealed class ObjectCacheService
{ internal readonly HashSet<TileBehaviour> Tiles=new(); internal HashSet<TileBehaviour> GetTileBehaviors()=>Tiles; }
internal static class UnityGameEditorRuntime { internal static Vector3 s_TileSize=new(2,0,2); }

namespace GloomhavenVR.Core.Events
{
    internal static class VRModeStateMachine { internal static bool TableInFrontOfPlayer=false; }
    // The root's native-menu/recenter lifecycle is a declared admission port.
    // This fixture exercises its consumers, not native sign-in/movie callbacks.
    internal static class MenuWorldContext
    {
        internal static bool IsReady=false;
        internal static bool AllowsSurroundings=>VRModeStateMachine.TableInFrontOfPlayer||IsReady;
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver
    {
        internal static Transform? RigRoot; internal static Camera? HeadCamera; internal static int RigPoseVersion=0;
        internal static Quaternion YawOnly(Quaternion value)=>Quaternion.Euler(0,value.eulerAngles.y,0);
    }
    internal static class RenderQuality
    { internal static void Bind()=>Core.SkyAlternative.BindConfig(new BepInEx.Configuration.ConfigFile()); }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static bool Active=false; internal static MeshRenderer? ParchmentRenderer=null;
        // Declared native map-frame measurement port. Its consumers run below.
        internal static bool TryGetParchmentFrame(out Vector3 center,out float scale)
        {
            center=Vector3.zero;scale=1;
            if(!Active||ParchmentRenderer==null)return false;
            Bounds bounds=ParchmentRenderer.bounds;
            float widest=Mathf.Max(Mathf.Abs(bounds.size.x),Mathf.Abs(bounds.size.z));
            if(widest<.01f)return false;
            center=bounds.center;scale=Mathf.Clamp(widest/1.2f,1f,2000f);return true;
        }
    }
    internal static class GuildmasterRoomGeometry
    { internal static bool TryFloor(out float floor){floor=0;return false;} }
}
namespace GloomhavenVR.WorldUI
{
    // Native map-seat solving is exercised by the town workspace fixture. This
    // port supplies its independently measured plane to the real scenery owner.
    internal static class TownServicePlacement
    {
        internal static bool HasFloor=false;internal static float Floor;
        internal static bool TryCanonicalFloor(out float floor){floor=Floor;return HasFloor;}
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning=true; }
    internal static class FrameDefaults { internal const bool Active=false; internal const int SkyStyleCode=0; }
    internal static class Defaults { internal const SkyStyle SkyStyle=Core.SkyStyle.Default; }
    internal static class VRLog
    {
        internal static void Info(string area,string message) { }
        internal static void Note(string area,string message) { }
        internal static void Warn(string area,string message) { }
    }
    internal static class VRLayers
    {
        internal const int ModLayer=27,ModLayerMask=1<<ModLayer;
        internal static void Apply(GameObject go){foreach(Transform t in go.GetComponentsInChildren<Transform>(true))t.gameObject.layer=ModLayer;}
    }
    internal static class SkyBackdrop { internal static Renderer? FindSky()=>null; }
    internal static class Haunt { internal static void Tick(){} internal static void StandDown(string reason){} }
    internal static class HauntFigures
    {
        internal static void Tick(GameObject? room,SkyStyle style){}
        internal static void StandDown(string reason){} internal static void ReleaseAll(string reason){}
    }
    internal static class EnvSound
    {
        internal static int StandDowns=0;
        internal static void Tick(GameObject? room,SkyStyle style,float scale){}
        internal static void StandDown(string reason){StandDowns++;} internal static void ReleaseAll(string reason){}
    }
    internal static class ElementMood { internal static void StandDown(string reason){} }
    internal static class ViewConeProbe
    { internal static void Arm(string reason){} internal static void Disarm(){} internal static void TickWatchdog(float scale){} }
    internal static class TeardownReport { internal static void Note(string reason){} }
    internal static class TickGuard { internal static void Run(string name,Action action,string area)=>action(); }
    internal static class SceneRegistry
    { internal static class MapTiles {internal static void Collect(List<ProceduralMapTile> result)=>result.Clear();} }
}
