using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Explicit model boundaries: no original native controllers or game callbacks run.
public class ProceduralBase : MonoBehaviour { }
public sealed class ProceduralScenario : MonoBehaviour { }
public sealed class ProceduralStyle : MonoBehaviour { public bool AnimateStyle; }
public sealed class ApparanceEntity : MonoBehaviour { }
public class ProceduralTileObserver : ProceduralBase { }
[RequireComponent(typeof(ApparanceEntity)),RequireComponent(typeof(ProceduralStyle))]
public sealed class ProceduralMapTile : ProceduralTileObserver { }
public sealed class ProceduralProp : ProceduralBase { }
public sealed class ProceduralDoorway : ProceduralBase { }
public sealed class MaterialLoader : MonoBehaviour { }
public sealed class RoomVisibilityTracker : MonoBehaviour { }
public sealed class TilesOcclusionVolume : MonoBehaviour { }
public sealed class UnityGameEditorDoorProp : MonoBehaviour { }
public sealed class CInteractable : MonoBehaviour { }
public sealed class ActorBehaviour : MonoBehaviour { }
public sealed class MapChoreographer : MonoBehaviour { public GameObject worldMap=null!,cityMap=null!; }
public sealed class UnknownNativeAnimation : MonoBehaviour { }

namespace GloomhavenVR.Core
{
    internal enum VRLogLevel { Info, Debug }
    internal static class VRSession
    {
        internal static bool IsRunning=true;
        internal static readonly Harmony Harmony=new("world.material."+typeof(VRSession).Assembly.GetName().Name);
    }
    internal static class PerfConfig { internal static int WorldMaterialQualityMode=2; }
    internal static class VRLog
    {
        internal static VRLogLevel Level=VRLogLevel.Debug;
        internal static readonly List<string> Messages=new();
        internal static void Note(string scope,string message)=>Messages.Add(message);
        internal static void Debug(string scope,string message)=>Messages.Add(message);
    }
    internal static class BundleShaders
    {
        internal static bool Missing,Throw=false;
        internal static Shader? Resolve(string name,string scope,string yes,string no)
        {
            if(Throw)throw new InvalidOperationException("fixture resolver failure");
            return Missing?null:Shader.Find("Fixture/WorldMaterialBridge");
        }
    }
    internal static class PerfMonitor
    {
        private sealed class Quiet:IDisposable { public void Dispose(){ } }
        internal static readonly Dictionary<string,int> Counts=new();
        internal static bool StepsActive=>true;
        internal static void RegisterDebug(string name){ }
        internal static void Count(string name,int value)=>Counts[name]=value;
        internal static IDisposable Scope(string name)=>new Quiet();
    }
    internal static class NativeWriteObserver
    {
        internal static int MaterialCopies,ArrayWrites;
        internal static void Copy(Material target,Material source){MaterialCopies++;target.CopyPropertiesFromMaterial(source);}
        internal static void Slots(Renderer renderer,Material[] slots){ArrayWrites++;renderer.sharedMaterials=slots;}
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    internal static class HeldProps { internal static bool Held=false; internal static bool OwnsRendererOf(Transform node)=>Held; }
    internal static class PropGrab { internal static bool Held=false; internal static bool OwnsRendererOf(Transform node)=>Held; }
}
