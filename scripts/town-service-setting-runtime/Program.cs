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
        Vector3 a = new(-40, -.8f, -40), b = new(40, .8f, -40), c = new(0, 0, 40);
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
        MapRoomDriver.Active = false;
        Check(!TownServicePlacement.TryResolve(1, Vector3.zero, 1, out _, out _), "no off-map spawn");
        Console.WriteLine($"Town setting: {assertions} production geometry assertions passed");
    }
}

namespace GloomhavenVR.Core
{
    internal static class SkyAlternative { internal static Transform? PlacedRoomRoot; }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomSeat { internal readonly struct Seat { internal readonly Vector3 FloorPosition; internal readonly float YawDegrees; internal Seat(float floor, float yaw) { FloorPosition = new Vector3(0, floor, 0); YawDegrees = yaw; } } }
    internal static class MapRoomDriver
    {
        internal static bool Active;
        internal static float Floor, Yaw;
        internal static bool TrySolveSeat(out MapRoomSeat.Seat seat, out string reason) { seat = new(Floor,Yaw); reason = ""; return Active; }
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public static Vector3 up => new(0,1,0);
        public Vector3 normalized { get { float len=MathF.Sqrt(x*x+y*y+z*z);return this*(1/len); } }
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float s)=>new(a.x*s,a.y*s,a.z*s);
    }
    public struct Quaternion
    {
        float yaw;
        public static Quaternion identity=>new();
        public static Quaternion Euler(float x,float y,float z)=>new(){yaw=y*MathF.PI/180};
        public static Quaternion LookRotation(Vector3 forward,Vector3 up)=>new();
        public static Vector3 operator *(Quaternion q,Vector3 v)=>new(MathF.Cos(q.yaw)*v.x+MathF.Sin(q.yaw)*v.z,v.y,-MathF.Sin(q.yaw)*v.x+MathF.Cos(q.yaw)*v.z);
    }
    public class Transform
    {
        public Vector3 position;
        public Transform? floor;
        public Mesh? mesh;
        public Transform? Find(string name)=>name=="RoomGeo/Ground"?floor:null;
        public T? GetComponent<T>() where T:class=>new MeshFilter {sharedMesh=mesh} as T;
        public Vector3 InverseTransformPoint(Vector3 v)=>v-position;
        public Vector3 TransformPoint(Vector3 v)=>v+position;
    }
    public class MeshFilter { public Mesh? sharedMesh; }
    public class Mesh { public bool isReadable=true; public Vector3[] vertices=Array.Empty<Vector3>(); public int[] triangles=Array.Empty<int>(); }
    public static class Mathf { public static float Abs(float v)=>Math.Abs(v); public static float Max(float a,float b)=>Math.Max(a,b); }
}
