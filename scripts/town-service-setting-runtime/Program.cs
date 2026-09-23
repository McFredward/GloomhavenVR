using System;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using GloomhavenVR.WorldUI.MapRoom;
using UnityEngine;

static class Program
{
    static int assertions;
    static void Check(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    static void Near(float actual, float expected, string message) => Check(Math.Abs(actual - expected) < .0001f, message + $": {actual} != {expected}");
    static void Main()
    {
        Vector3 a = new(-100, -2f, -100), b = new(100, 2f, -100), c = new(0, 0, 100);
        foreach (float x in new[] { -2f, 0f, 2f })
            foreach (float z in new[] { -1f, 0f, 1f })
            {
                Check(TownServicePlacement.TryHeight(new Vector3(x, 20, z), a, b, c, out float y), "inside triangle");
                Near(y, x * .02f, "actual relief");
                Check(TownServicePlacement.TryHeight(new Vector3(x, 0, z), c, b, a, out float reversed), "reversed triangle");
                Near(y, reversed, "winding invariant");
            }
        Check(!TownServicePlacement.TryHeight(new Vector3(100, 0, 0), a, b, c, out _), "outside triangle rejected");
        Check(!TownServicePlacement.TryHeight(Vector3.zero, a, a, a, out _), "degenerate rejected");
        MapRoomDriver.ParchmentRenderer = new MeshRenderer();
        MapRoomDriver.Floor = 20f;
        MapRoomDriver.Yaw = 0f;
        MapRoomDriver.Active = true;
        foreach (float scale in new[] { 1f, 2f, 4f })
        {
            var room = new Transform { position = new Vector3(0, 3, 0) };
            var floor = new Transform { position = room.position, mesh = new Mesh { vertices = new[] { a, b, c }, triangles = new[] { 0, 1, 2 } } };
            room.floor = floor;
            SkyAlternative.PlacedRoomRoot = room;
            for (byte service = 1; service <= 3; service++)
            {
                Check(TownServicePlacement.TryResolve(service, new Vector3(0, 21, 0), scale, out Vector3 p, out _), "valid placement");
                Near(p.y, 3f + p.x * .02f, "mesh floor, never tracking floor");
            }
            floor.mesh!.isReadable = false;
            Check(TownServicePlacement.TryResolve(1, Vector3.zero, scale, out Vector3 fallback, out _), "unreadable floor");
            Near(fallback.y, 3, "unreadable mesh exact plane fallback");
            SkyAlternative.PlacedRoomRoot = null;
            Check(TownServicePlacement.TryResolve(1, Vector3.zero, scale, out Vector3 mr, out _), "MR/default floor");
            Near(mr.y, 20, "MR canonical seat");
        }
        // Actual scene collision validation is in the real-mesh workspace audit. This
        // portable boundary checks unchanged pose semantics across reading-side changes.
        SkyAlternative.PlacedRoomRoot = null;
        for (int yaw = 0; yaw < 360; yaw += 5)
        {
            MapRoomDriver.Yaw = yaw;
            for(byte service=1;service<=3;service++)
            {
                Check(TownServicePlacement.TryResolve(service,Vector3.zero,1,out var p,out _),"all-yaw open placement");
                float radius=MathF.Sqrt(p.x*p.x+p.z*p.z);
                Near(radius,4.8f,"default station uses validated layout radius");
                Check(radius-(service==1?.82f:.362f)>1.4f,"all opened resident furniture clears native map diagonal");
            }
        }
        foreach(bool forest in new[]{false,true})
        {
            var room=new Transform{rotation=Quaternion.Euler(0,37,0)};
            if(forest)room.floor=new Transform();SkyAlternative.PlacedRoomRoot=room;
            for(byte service=1;service<=3;service++)
            {
                MapRoomDriver.Yaw=0;TownServicePlacement.TryResolve(service,Vector3.zero,1,out var original,out _);
                for(int yaw=5;yaw<360;yaw+=5)
                {
                    MapRoomDriver.Yaw=yaw;TownServicePlacement.TryResolve(service,Vector3.zero,1,out var current,out _);
                    Near(current.x,original.x,"room frame independent of reading x");
                    Near(current.z,original.z,"room frame independent of reading z");
                }
            }
        }
        SkyAlternative.PlacedRoomRoot=null;
        MapRoomDriver.Yaw = 0;
        MapRoomDriver.Active = false;
        Check(!TownServicePlacement.TryResolve(1, Vector3.zero, 1, out _, out _), "no off-map spawn");
        Console.WriteLine($"Town setting: {assertions} production geometry assertions passed");
        StationLifecycle.Run();
        GroundingLifecycle.Run();
    }
}

namespace GloomhavenVR.Core
{
    internal static class SkyAlternative { internal static Transform? PlacedRoomRoot; }
    internal static class VRLayers { internal const int ModLayer=27; }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomSeat { internal readonly struct Seat { internal readonly Vector3 FloorPosition; internal readonly float YawDegrees; internal Seat(float floor, float yaw) { FloorPosition = new Vector3(0, floor, 0); YawDegrees = yaw; } } }
    internal static class MapRoomDriver
    {
        internal static MeshRenderer? ParchmentRenderer;
        internal static bool Active;
        internal static float Floor, Yaw;
        internal static Vector3 Center=Vector3.zero;
        internal static float Scale=1;
        internal static bool FrameReady=true;
        internal static bool TryGetParchmentFrame(out Vector3 center,out float scale) { center=Center;scale=Scale;return FrameReady; }
        internal static bool TrySolveSeat(out MapRoomSeat.Seat seat, out string reason) { seat = new(Floor,Yaw); reason = ""; return Active; }
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public float this[int axis] { get=>axis==0?x:axis==1?y:z; set { if(axis==0)x=value;else if(axis==1)y=value;else z=value; } }
        public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public static Vector3 up => new(0,1,0);
        public static Vector3 one => new(1,1,1);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object? o)=>o is Vector3 v&&this==v;
        public override int GetHashCode()=>HashCode.Combine(x,y,z);
        public Vector3 normalized { get { float len=MathF.Sqrt(x*x+y*y+z*z);return this*(1/len); } }
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
    }
    public struct Quaternion
    {
        internal float yaw;
        public static Quaternion identity=>new();
        public static Quaternion operator *(Quaternion a,Quaternion b)=>new(){yaw=a.yaw+b.yaw};
        public static Quaternion Inverse(Quaternion q)=>new(){yaw=-q.yaw};
        public static Quaternion Euler(float x,float y,float z)=>new(){yaw=y*MathF.PI/180};
        public static Quaternion LookRotation(Vector3 forward,Vector3 up)=>new();
        public static Vector3 operator *(Quaternion q,Vector3 v)=>new(MathF.Cos(q.yaw)*v.x+MathF.Sin(q.yaw)*v.z,v.y,-MathF.Sin(q.yaw)*v.x+MathF.Cos(q.yaw)*v.z);
    }
    public class Transform
    {
        internal GameObject? Owner;
        internal LODGroup? Lod;
        public string name="";
        public Vector3 up=>TransformDirection(Vector3.up).normalized;
        public Matrix4x4 worldToLocalMatrix=>new(InverseTransformPoint);
        public Matrix4x4 localToWorldMatrix=>new(TransformPoint);
        public Vector3 TransformDirection(Vector3 value) { value=rotation*value;return parent!=null?parent.TransformDirection(value):value; }
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class
        {
            var result=new System.Collections.Generic.List<T>();
            if(typeof(T)==typeof(LODGroup) && Lod!=null)result.Add((T)(object)Lod);
            if(typeof(T)==typeof(MeshFilter) && mesh!=null)result.Add((T)(object)new MeshFilter{sharedMesh=mesh,transform=this});
            foreach(var child in children.Values)result.AddRange(child.GetComponentsInChildren<T>(inactive));
            return result.ToArray();
        }
        public GameObject gameObject=>Owner??=new GameObject(this);
        private Vector3 _localPosition, _localScale=Vector3.one;
        public int LocalWrites;
        public Vector3 localPosition {get=>_localPosition;set{_localPosition=value;LocalWrites++;}}
        public Vector3 localScale {get=>_localScale;set{_localScale=value;LocalWrites++;}}
        public Transform? parent;
        private readonly System.Collections.Generic.Dictionary<string,Transform> children=new();
        public void Add(string name,Transform child){children[name]=child;child.parent=this;child.name=name;}
        public Vector3 position {get=>parent!=null?parent.TransformPoint(localPosition):localPosition;set=>localPosition=parent!=null?parent.InverseTransformPoint(value):value;}
        public Vector3 lossyScale=>parent!=null?new Vector3(parent.lossyScale.x*localScale.x,parent.lossyScale.y*localScale.y,parent.lossyScale.z*localScale.z):localScale;
        public Quaternion rotation;
        public Vector3 eulerAngles=>new(0,rotation.yaw*180/MathF.PI,0);
        public int PoseWrites;
        public bool HasAnchor=true;
        public void SetPositionAndRotation(Vector3 p,Quaternion q) { position=p;rotation=q;PoseWrites++; }
        public Transform? floor;
        public Mesh? mesh;
        public Transform? Find(string name)=>children.TryGetValue(name,out var child)?child:name=="RoomGeo/Ground"?floor:name=="InteractionAnchor"&&HasAnchor?new Transform():null;
        public T? GetComponent<T>() where T:class=>new MeshFilter {sharedMesh=mesh!,transform=this} as T;
        public Vector3 InverseTransformPoint(Vector3 v){if(parent!=null)v=parent.InverseTransformPoint(v);v=Quaternion.Inverse(rotation)*(v-localPosition);return new Vector3(v.x/localScale.x,v.y/localScale.y,v.z/localScale.z);}
        public Vector3 TransformPoint(Vector3 v){v=new Vector3(v.x*localScale.x,v.y*localScale.y,v.z*localScale.z);v=rotation*v+localPosition;return parent!=null?parent.TransformPoint(v):v;}
    }
    public readonly struct Matrix4x4
    {
        private readonly Func<Vector3,Vector3> map;
        public Matrix4x4(Func<Vector3,Vector3> transform) { map=transform; }
        public Vector3 MultiplyPoint3x4(Vector3 value)=>map(value);
        public static Matrix4x4 operator *(Matrix4x4 a,Matrix4x4 b)=>new(v=>a.MultiplyPoint3x4(b.MultiplyPoint3x4(v)));
    }
    public struct Bounds { public Vector3 min,max; }
    public class MeshFilter { public Mesh sharedMesh=null!;public Transform transform=new();public string name=>transform.name; }
    public class Mesh { public bool isReadable=true; public Vector3[] vertices=Array.Empty<Vector3>(); public int[] triangles=Array.Empty<int>();
        public Bounds bounds { get {
            Vector3 low=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity), high=new(float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity);
            foreach(var v in vertices)for(int axis=0;axis<3;axis++){low[axis]=Math.Min(low[axis],v[axis]);high[axis]=Math.Max(high[axis],v[axis]);}
            return new Bounds{min=low,max=high};
        } }
    }
    public static class Mathf { public static float Abs(float v)=>Math.Abs(v); public static float Max(float a,float b)=>Math.Max(a,b); public static float Min(float a,float b)=>Math.Min(a,b); }
}

namespace UnityEngine { public class MeshRenderer { public Transform transform=new(); } }
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Quaternion YawOnly(Quaternion q)=>q; } }
