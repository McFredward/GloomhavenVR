using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static Action<GameObject>? BeforeDestroy;
        public static int DestroyRequests;
        private static readonly List<GameObject> Pending=new();
        public static void Destroy(GameObject obj){BeforeDestroy?.Invoke(obj);DestroyRequests++;Pending.Add(obj);}
        public static void FinishFrame(){foreach(var obj in Pending)ExternalDestroy(obj);Pending.Clear();}
        public static void ExternalDestroy(GameObject obj){obj.Destroyed=true;foreach(var light in obj.Lights)light.Destroyed=true;}
        private static bool Missing(Object? obj)=>ReferenceEquals(obj,null)||obj.Destroyed;
        public static bool operator ==(Object? a,Object? b)=>Missing(a)&&Missing(b)||ReferenceEquals(a,b);
        public static bool operator !=(Object? a,Object? b)=>!(a==b);
        public override bool Equals(object? obj)=>ReferenceEquals(this,obj);
        public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    public class GameObject:Object
    {
        public readonly string name;
        public int layer;
        public bool activeInHierarchy=true;
        public readonly Transform transform;
        public readonly List<Light> Lights=new();
        public readonly List<MonoBehaviour> Scripts=new();
        public GameObject(string name){this.name=name;transform=new Transform{gameObject=this};}
        public T AddComponent<T>() where T:Light,new(){var light=new T{gameObject=this};Lights.Add(light);Light.All.Add(light);return light;}
        public void GetComponents(List<MonoBehaviour> scripts){scripts.Clear();scripts.AddRange(Scripts);}
    }
    public class Transform
    {
        public GameObject gameObject=null!;
        public Transform? parent;
        public Vector3 localPosition,position,localScale=Vector3.one;
        public Vector3 lossyScale=>localScale;
        public Quaternion rotation;
        public void SetParent(Transform root,bool world){parent=root;}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0);
        public static Vector3 one=>new(1,1,1);
        public static Vector3 operator -(Vector3 v)=>new(-v.x,-v.y,-v.z);
        public static Vector3 operator *(Vector3 v,float f)=>new(v.x*f,v.y*f,v.z*f);
    }
    public struct Quaternion{public static Quaternion LookRotation(Vector3 direction,Vector3 up)=>new();}
    public struct Color{public float r,g,b;public Color(float r,float g,float b){this.r=r;this.g=g;this.b=b;}}
    public class MonoBehaviour:Object{}
    public enum LightType{Point,Directional}
    public enum LightRenderMode{Auto,ForceVertex}
    public enum LightShadows{None,Soft}
    public enum LightmapBakeType{Realtime,Baked}
    public struct LightBakingOutput{public LightmapBakeType lightmapBakeType;}
    public class Light:Object
    {
        public static readonly List<Light> All=new();
        public GameObject gameObject=null!;
        public Transform transform=>gameObject.transform;
        public string name=>gameObject.name;
        public LightType type;
        public LightRenderMode renderMode;
        public int cullingMask;
        public LightShadows shadows;
        public Color color;
        public float intensity,range;
        public bool enabled=true;
        public LightBakingOutput bakingOutput;
    }
    public static class Mathf{public static float Abs(float f)=>Math.Abs(f);}
    public static class Time{public static float unscaledTime=>100;}
}
namespace GloomhavenVR.Core
{
    using UnityEngine;
    internal static class VRLayers{internal const int ModLayer=27;}
    internal static class SkyAlternative
    {
        internal static bool HasMoon=true;
        internal static bool TryRoomMoonDirection(out Vector3 direction,out Color moon,out string source){direction=new(1,1,1);moon=new(.7f,.79f,.94f);source="fixture";return HasMoon;}
    }
}
namespace GloomhavenVR.Rig
{
    using UnityEngine;
    internal static partial class LightStabiliser
    {
        private const string RuleOwnVfx="vfx-own-script",RuleParticlePool="vfx-particle-pool-child",RuleAdditive="additive-writer-parent";
        private sealed class LightRecord
        {
            internal Light Light=null!;
            internal Transform Transform=null!;
            internal string Name="",ExcludeRule="";
            internal LightRenderMode OriginalMode;
            internal LightShadows AuthoredShadows,LastShadows;
            internal bool Baked,LastActive,LastEnabled;
            internal string[] OwnScripts=Array.Empty<string>();
            internal float AdoptedAt,LastRawIntensity,IntensityFloor,LastOutIntensity;
        }
        private static readonly List<LightRecord> Lights=new();
        private static readonly List<MonoBehaviour> ScriptBuf=new();
        internal static int Adopt(Light[] lights)=>AdoptLights(lights);
        internal static int Count=>Lights.Count;
        internal static void Reset()=>Lights.Clear();
    }
}
