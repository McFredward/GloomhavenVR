using System;
using System.Collections.Generic;
using N = System.Numerics;

// The fixture models TRS reparenting, not Unity rendering. SetParent(true) deliberately
// carries the converted parent's world pose into the new screen parent as the game does.
namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        public static bool operator ==(Object? a, Object? b) =>
            (ReferenceEquals(a,null) || a.Destroyed) && (ReferenceEquals(b,null) || b.Destroyed)
            || ReferenceEquals(a,b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    internal struct Vector2
    {
        public float x,y;
        public Vector2(float x,float y) { this.x=x;this.y=y; }
    }
    internal struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 one => new(1,1,1);
        public static Vector3 zero => new(0,0,0);
        internal N.Vector3 Native => new(x,y,z);
        internal static Vector3 From(N.Vector3 v) => new(v.X,v.Y,v.Z);
    }
    internal struct Quaternion
    {
        public float x,y,z,w;
        public Quaternion(float x,float y,float z,float w) { this.x=x;this.y=y;this.z=z;this.w=w; }
        public static Quaternion identity => new(0,0,0,1);
        internal N.Quaternion Native => new(x,y,z,w);
        internal static Quaternion From(N.Quaternion q) => new(q.X,q.Y,q.Z,q.W);
        internal static Quaternion Yaw(float radians) => From(N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY,radians));
    }
    internal class Transform : Object
    {
        private readonly List<Transform> _children = new();
        public Transform? parent { get; private set; }
        public Vector3 localPosition;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        public int childCount => _children.Count;
        internal N.Matrix4x4 World => N.Matrix4x4.CreateScale(localScale.Native)
            * N.Matrix4x4.CreateFromQuaternion(localRotation.Native)
            * N.Matrix4x4.CreateTranslation(localPosition.Native) * (parent?.World ?? N.Matrix4x4.Identity);
        public void SetParent(Transform? value,bool worldPositionStays = true)
        {
            N.Matrix4x4 before = World;
            parent?._children.Remove(this);parent=value;parent?._children.Add(this);
            if (!worldPositionStays) return;
            N.Matrix4x4.Invert(parent?.World ?? N.Matrix4x4.Identity,out var inverse);
            if (!N.Matrix4x4.Decompose(before*inverse,out var scale,out var rotation,out var position))
                throw new Exception("fixture transform cannot be decomposed");
            localScale=Vector3.From(scale);localRotation=Quaternion.From(rotation);localPosition=Vector3.From(position);
        }
        public int GetSiblingIndex() => parent?._children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index)
        {
            if (parent==null) return;
            parent._children.Remove(this);parent._children.Insert(Math.Clamp(index,0,parent._children.Count),this);
        }
        public bool IsChildOf(Transform value)
        {
            for (Transform? node=this;node!=null;node=node.parent) if (ReferenceEquals(node,value)) return true;
            return false;
        }
    }
    internal sealed class RectTransform : Transform
    {
        public Vector2 anchorMin,anchorMax,pivot,sizeDelta;
        public Vector3 anchoredPosition3D { get => localPosition; set => localPosition=value; }
    }
    internal static class Mathf { public static int Clamp(int v,int min,int max)=>Math.Clamp(v,min,max); public static int Max(int a,int b)=>Math.Max(a,b); }
}
