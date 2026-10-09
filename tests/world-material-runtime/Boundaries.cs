using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

// Explicit model boundaries: no original native controllers or game callbacks run.
public class ProceduralBase : MonoBehaviour { }
public sealed class ProceduralScenario : MonoBehaviour { }
public sealed class ProceduralStyle : MonoBehaviour { public bool AnimateStyle; }
public sealed class ApparanceEntity : MonoBehaviour { }
public class ApparanceMap : MonoBehaviour { }
public sealed class ProceduralMapConfig : MonoBehaviour { }
public sealed class ProceduralPlacementNotifierHandler : MonoBehaviour { }
public sealed class LightShadowsModifierController : MonoBehaviour { }
public sealed class UnreviewedMapSubclass : ApparanceMap { }
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
        internal static Harmony Harmony=new("world.material."+typeof(VRSession).Assembly.GetName().Name);
    }
    internal static class PerfConfig { internal static int WorldMaterialQualityMode=2; internal static bool SharedEnvironmentMaterialReadsOn=true; }
    internal static class VRLog
    {
        internal static VRLogLevel Level=VRLogLevel.Debug;
        internal static readonly List<string> Messages=new();
        internal static void Note(string scope,string message)=>Messages.Add(message);
        internal static void Info(string scope,string message)=>Messages.Add(message);
        internal static void Debug(string scope,string message)=>Messages.Add(message);
    }
    internal static class BundleShaders
    {
        internal static bool Missing,Throw=false,AttachmentPixels;
        internal static Shader? Resolve(string name,string scope,string yes,string no)
        {
            if(Throw)throw new InvalidOperationException("fixture resolver failure");
            return Missing?null:Shader.Find(AttachmentPixels?"GloomhavenVR/WorldSimpleMaterial":"Fixture/WorldMaterialBridge");
        }
    }
    internal static class PerfMonitor
    {
        private sealed class Quiet:IDisposable { public void Dispose(){ } }
        internal static readonly Dictionary<string,int> Counts=new();
        internal static readonly Dictionary<string,int> ScopeCalls=new();
        internal static bool StepsActive=>true;
        internal static void RegisterDebug(string name){ }
        internal static void Count(string name,int value=1)=>Counts[name]=value;
        internal static IDisposable Scope(string name)
        { ScopeCalls.TryGetValue(name,out int count); ScopeCalls[name]=count+1; return new Quiet(); }
        internal static long BeginStep()=>0L;
        internal static void EndStep(string name,long begin){ }
    }
    internal static class NativeWriteObserver
    {
        internal static Renderer? TrackedRenderer;
        internal static int TrackedMaterials,TrackedMeshes,TrackedWideBlocks,TrackedSlotBlocks;
        internal static int MaterialCopies,ArrayWrites,MaterialReads,MapInventories,RendererBlockReads,SlotBlockReads,PropCopies,RegistryVisits,MeshReads;
        internal static void Copy(Material target,Material source){MaterialCopies++;target.CopyPropertiesFromMaterial(source);}
        internal static void Slots(Renderer renderer,Material[] slots){ArrayWrites++;renderer.sharedMaterials=slots;}
        internal static void Read(Renderer renderer,List<Material> slots){MaterialReads++;if(ReferenceEquals(renderer,TrackedRenderer))TrackedMaterials++;renderer.GetSharedMaterials(slots);}
        internal static MapChoreographer[] FindMaps(){MapInventories++;return UnityEngine.Object.FindObjectsOfType<MapChoreographer>(true);}
        internal static void RendererBlock(Renderer renderer,MaterialPropertyBlock block){RendererBlockReads++;if(ReferenceEquals(renderer,TrackedRenderer))TrackedWideBlocks++;renderer.GetPropertyBlock(block);}
        internal static void SlotBlock(Renderer renderer,MaterialPropertyBlock block,int slot){SlotBlockReads++;if(ReferenceEquals(renderer,TrackedRenderer))TrackedSlotBlocks++;renderer.GetPropertyBlock(block,slot);}
        internal static MeshFilter ReadMesh(MeshRenderer renderer){MeshReads++;if(ReferenceEquals(renderer,TrackedRenderer))TrackedMeshes++;return renderer.GetComponent<MeshFilter>();}
    }
}
namespace GloomhavenVR.Board.FigureGrab
{
    // Local/remote gameplay registries are explicit boundaries. Native Unity
    // visual identity/ancestry is real; PropGrab's two pure readers are extracted
    // unmodified from production and execute against this small registry model.
    internal static class HeldProps
    {
        internal static readonly List<GameObject> Visuals=new();
        internal static int Count=>Visuals.Count;
        internal static bool OwnsRendererOf(Transform? node)=>Owns(Visuals,node)||NetHeldProps.OwnsRendererOf(node);
        internal static bool Owns(List<GameObject> roots,Transform? node)
        {for(Transform? cur=node;cur!=null;cur=cur.parent)foreach(GameObject visual in roots)if(visual!=null&&ReferenceEquals(visual.transform,cur))return true;return false;}
        internal static bool TryGetSlot(int slot,out object prop,out GameObject visual,out int side,out float scale)
        {prop=null!;visual=Visuals[slot];side=0;scale=1;return visual!=null;}
    }
    internal static class NetHeldProps
    {
        internal static readonly List<GameObject> Visuals=new();
        internal static void CopyVisualRoots(List<GameObject> target){foreach(GameObject visual in Visuals)if(visual!=null)target.Add(visual);}
        internal static bool OwnsRendererOf(Transform? node)=>HeldProps.Owns(Visuals,node);
    }
    internal sealed class GrabbableProp {internal GameObject Visual=null!;}
    internal static partial class PropGrab
    {
        internal static readonly Dictionary<object,GrabbableProp> Registry=new();
        internal static object Register(GameObject visual){var key=new object();Registry.Add(key,new GrabbableProp{Visual=visual});return key;}
    }
}
