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
        public readonly List<Renderer> Renderers=new();
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
        public Vector3 forward=>rotation.Forward;
        public Vector3 TransformDirection(Vector3 direction)=>rotation.Transform(direction);
        public Vector3 InverseTransformDirection(Vector3 direction)=>rotation.InverseTransform(direction);
        public void SetParent(Transform root,bool world){parent=root;}
        public void GetComponentsInChildren(bool inactive,List<Renderer> into){into.AddRange(gameObject.Renderers);}
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new(0,1,0);
        public static Vector3 one=>new(1,1,1);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public Vector3 normalized=>sqrMagnitude>.000001f?this*(1f/(float)Math.Sqrt(sqrMagnitude)):new(0,0,1);
        public static Vector3 operator -(Vector3 v)=>new(-v.x,-v.y,-v.z);
        public static Vector3 operator *(Vector3 v,float f)=>new(v.x*f,v.y*f,v.z*f);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object? value)=>value is Vector3 vector&&this==vector;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
    }
    public struct Vector4{public float x,y,z,w;public Vector4(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
    public class Shader
    {public string name="GloomhavenVR/TownNpc";private static readonly Dictionary<string,int> Ids=new();public static int PropertyToID(string name){if(!Ids.TryGetValue(name,out int id))Ids[name]=id=Ids.Count+1;return id;}}
    public class Material
    {
        public Shader shader=new();public readonly Dictionary<int,object> Values=new();
        public bool HasProperty(int id)=>shader.name.StartsWith("GloomhavenVR/Town",StringComparison.Ordinal);
        public void SetFloat(int id,float value)=>Values[id]=value;
        public void SetVector(int id,Vector4 value)=>Values[id]=value;
        public float GetFloat(int id)=>Values.TryGetValue(id,out object? value)&&value is float number?number:0;
        public Vector4 GetVector(int id)=>Values.TryGetValue(id,out object? value)&&value is Vector4 vector?vector:default;
    }
    public class MaterialPropertyBlock
    {
        public readonly Dictionary<int,object> Values=new();
        public void SetFloat(int id,float value)=>Values[id]=value;
        public void SetVector(int id,Vector4 value)=>Values[id]=value;
        public float GetFloat(int id)=>Values.TryGetValue(id,out object? value)&&value is float number?number:0;
        public Vector4 GetVector(int id)=>Values.TryGetValue(id,out object? value)&&value is Vector4 vector?vector:default;
        public void CopyFrom(MaterialPropertyBlock source){Values.Clear();foreach(var pair in source.Values)Values.Add(pair.Key,pair.Value);}
    }
    public class Renderer:Object
    {
        public int Writes;
        public readonly List<Material> Materials=new(){new Material()};
        public void GetSharedMaterials(List<Material> target){target.Clear();target.AddRange(Materials);}
        private readonly MaterialPropertyBlock _block=new();
        public void GetPropertyBlock(MaterialPropertyBlock target)=>target.CopyFrom(_block);
        public void SetPropertyBlock(MaterialPropertyBlock source){Writes++;_block.CopyFrom(source);}
    }
    public class MeshRenderer:Renderer{}
    public struct Quaternion
    {
        private System.Numerics.Quaternion _value;
        private bool _set;
        private System.Numerics.Quaternion Value=>_set?_value:System.Numerics.Quaternion.Identity;
        public Vector3 Forward=>Transform(new(0,0,1));
        public Vector3 Transform(Vector3 value){var v=System.Numerics.Vector3.Transform(new(value.x,value.y,value.z),Value);return new(v.X,v.Y,v.Z);}
        public Vector3 InverseTransform(Vector3 value){var v=System.Numerics.Vector3.Transform(new(value.x,value.y,value.z),System.Numerics.Quaternion.Inverse(Value));return new(v.X,v.Y,v.Z);}
        public static Quaternion LookRotation(Vector3 direction,Vector3 up)
        {
            direction=direction.normalized;
            float yaw=(float)Math.Atan2(direction.x,direction.z),pitch=-(float)Math.Asin(direction.y);
            return new(){_value=System.Numerics.Quaternion.CreateFromYawPitchRoll(yaw,pitch,0),_set=true};
        }
    }
    public struct Color{public float r,g,b;public Color(float r,float g,float b){this.r=r;this.g=g;this.b=b;}public static Color white=>new(1,1,1);public static Color black=>new(0,0,0);public Color linear=>this;}
    public enum ColorSpace{Gamma,Linear}
    public static class QualitySettings{public static ColorSpace activeColorSpace=>ColorSpace.Gamma;}
    public static class RenderSettings{public static Light? sun;public static UnityEngine.Rendering.SphericalHarmonicsL2 ambientProbe;}
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
        public bool isActiveAndEnabled=>this!=null&&enabled&&gameObject.activeInHierarchy;
        public static Light[] GetLights(LightType type,int layer)=>All.FindAll(light=>light!=null&&light.type==type&&(light.cullingMask&(1<<layer))!=0).ToArray();
        public LightBakingOutput bakingOutput;
    }
    public static class Mathf{public static float Abs(float f)=>Math.Abs(f);}
    public static class Time{public static float unscaledTime=>100;}
}
namespace UnityEngine.Rendering
{
    public struct SphericalHarmonicsL2
    {
        private float[]? _values;
        public float this[int colour,int coefficient]{get=>_values?[colour*9+coefficient]??0;set{_values??=new float[27];_values[colour*9+coefficient]=value;}}
    }
}
namespace GloomhavenVR.Core
{
    using UnityEngine;
    internal static class VRLayers{internal const int ModLayer=27;}
    internal static class SkyAlternative
    {
        internal static bool HasMoon=true;
        internal static Vector3 Direction=new(1,1,1);
        internal static Color Colour=new(.7f,.79f,.94f);
        internal static bool TryRoomMoonDirection(out Vector3 direction,out Color moon,out string source){direction=Direction;moon=Colour;source="fixture";return HasMoon;}
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

// This portable fixture measures registry ownership only. Actual shader binding and
// rendering run in check-town-lighting.py against real Unity/NPCs.
namespace GloomhavenVR.WorldUI { internal static class TownServiceLightList {internal static void Claim(UnityEngine.Light light){}internal static void Forget(UnityEngine.Light light){} } }
