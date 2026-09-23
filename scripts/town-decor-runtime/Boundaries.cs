using System;
using System.Collections.Generic;
using UnityEngine;
internal static class DecorClock { internal static float Now; }
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public sealed class State<T> { public T Result=default!;public bool Valid=true,Done=true;public AsyncOperationStatus Status;public Exception? Error; }
    public struct AsyncOperationHandle<T>
    {
        public State<T>? State;
        public bool IsDone=>State!.Done;
        public bool IsValid()=>State!=null&&State.Valid;
        public AsyncOperationStatus Status=>State!.Status;
        public T Result=>State!.Result;
        public Exception? OperationException=>State!.Error;
    }
}
namespace UnityEngine.AddressableAssets
{
    using UnityEngine.ResourceManagement.AsyncOperations;
    public sealed class AssetReferenceT<T> { public string RuntimeKey="";public bool RuntimeKeyIsValid()=>RuntimeKey.Length>0; }
    public static class Addressables
    {
        internal static readonly Dictionary<object,object> Assets=new();
        internal static readonly Dictionary<object,int> Failures=new(),Requests=new();
        internal static readonly HashSet<object> Pending=new();
        internal static int Held;
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(object key)
        {
            Requests.TryGetValue(key,out int count);Requests[key]=count+1;Held++;
            bool fail=Failures.TryGetValue(key,out int remaining)&&remaining>0;
            if(fail)Failures[key]=remaining-1;
            return new AsyncOperationHandle<T>{State=new State<T>{Done=!Pending.Contains(key),Status=fail?AsyncOperationStatus.Failed:AsyncOperationStatus.Succeeded,
                Result=fail?default!:(T)Assets[key],Error=fail?new Exception("fixture material failure"):null}};
        }
        public static void Release<T>(AsyncOperationHandle<T> handle){if(!handle.IsValid())throw new Exception("double release");handle.State!.Valid=false;Held--;}
    }
}
public sealed class ApparanceObjectResource { public string Name="";public UnityEngine.Object Object=null!; }
public sealed class ApparanceResourceList { public ApparanceObjectResource[] Objects=Array.Empty<ApparanceObjectResource>(); }
public sealed class MaterialLoader:MonoBehaviour { public MaterialLoaderData[] LoadersData=Array.Empty<MaterialLoaderData>(); }
public sealed class MaterialLoaderData { public Renderer Renderer=null!;public bool IsSaveExistedMaterials=false;public UnityEngine.AddressableAssets.AssetReferenceT<Material>[] MaterialReferences=Array.Empty<UnityEngine.AddressableAssets.AssetReferenceT<Material>>(); }
namespace GloomhavenVR.Core
{
    internal static class VRLayers {internal const int ModLayer=27;}
    internal static class VRLog { internal static readonly List<string> Warnings=new();internal static void Warn(string source,string text)=>Warnings.Add(text); }
}
namespace GloomhavenVR.Net {internal struct TownActivityVisual {internal float Cast {get;set;} internal float CastSway {get;set;} internal float EffectClock {get;set;}} }
namespace GloomhavenVR.Net.TownServices
{
    internal static class TownServiceMirror {internal static Registry Assets=new();}
    internal sealed class Registry {internal uint Generation {get;set;} internal readonly Dictionary<string,UnityEngine.Object> Items=new();internal void RegisterOriginal(string key,UnityEngine.Object value){if(Items.TryGetValue(key,out var old)&&old!=value)throw new Exception("unstable texture identity");Items[key]=value;}}
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServiceAssets { internal static Shader? Shader(string name)=>UnityEngine.Shader.Find(name=="townflame"?"GloomhavenVR/TownFlame":"GloomhavenVR/TownNpc"); }
    internal sealed class TownServiceLighting {
        internal static readonly HashSet<Light> Owned=new();
        internal static void ClaimPractical(Light light)=>Owned.Add(light);
        internal static void ForgetPractical(Light light)=>Owned.Remove(light);
        internal static float PracticalPower(byte service)=>2.6f;
        internal static Color PracticalColour(byte service)=>Color.white;
        internal readonly Dictionary<int,Vector3> Flames=new();internal void SetFlame(Vector3 world,int slot)=>Flames[slot]=world;}
    internal sealed class TownServiceActivityProps:IDisposable {internal TownServiceActivityProps(Transform root,byte service,Shader? shader){}internal void BindCoin(Transform coin,Vector3 offset){}internal void Sample(){}internal void Sample(in GloomhavenVR.Net.TownActivityVisual visual){}internal void Suspend(){}internal void SetVisibility(float value){}public void Dispose(){}}
}
