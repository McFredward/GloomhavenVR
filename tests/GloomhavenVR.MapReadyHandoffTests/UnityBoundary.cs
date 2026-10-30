// Bounded uGUI/native API model for the production composite. This fixture proves
// hierarchy, ownership, geometry and restoration; it makes no Unity-render/headset claim.
using System;
using System.Collections.Generic;
using System.Linq;
using Numerics = System.Numerics;

namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static void Destroy(Object? value)
        {
            if (value == null) return;
            value.Destroyed = true;
            if (value is Component component) component.gameObject.Remove(component);
            if (value is GameObject game)
            {
                foreach (Component owned in game.Components) owned.Destroyed = true;
                foreach (Transform child in game.transform.Children.ToArray()) Destroy(child.gameObject);
                game.transform.SetParent(null, false);
                game.activeSelf = false;
            }
        }
        public static bool operator ==(Object? left, Object? right)
        {
            bool ln = ReferenceEquals(left, null) || left.Destroyed;
            bool rn = ReferenceEquals(right, null) || right.Destroyed;
            return ln || rn ? ln == rn : ReferenceEquals(left, right);
        }
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public int GetInstanceID() => GetHashCode();
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T? GetComponentInParent<T>() where T : Component
        {
            for (Transform? current = transform; current != null; current = current.parent)
            { T? value = current.GetComponent<T>(); if (value != null) return value; }
            return null;
        }
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) where T : Component
        { var list = new List<T>(); GetComponentsInChildren(includeInactive, list); return list.ToArray(); }
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> list) where T : Component
        {
            if (!includeInactive && !gameObject.activeInHierarchy) return;
            foreach (Component component in gameObject.Components) if (component is T item && !item.Destroyed) list.Add(item);
            foreach (Transform child in transform.Children) child.GetComponentsInChildren(includeInactive, list);
        }
    }
    public class Behaviour : Component { public bool enabled = true; public bool isActiveAndEnabled => enabled && gameObject.activeInHierarchy; }
    public class MonoBehaviour : Behaviour { }
    public class Renderer : Component { }
    public sealed class CanvasRenderer : Component { public bool cull; }
    public class CanvasGroup : Behaviour { public float alpha = 1; public bool interactable = true, blocksRaycasts = true, ignoreParentGroups; }
    public sealed class Canvas : Behaviour { }
    public sealed class GameObject : Object
    {
        public string name;
        public readonly Transform transform;
        internal readonly List<Component> Components = new();
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy => !Destroyed && activeSelf && (transform.parent?.gameObject.activeInHierarchy ?? true);
        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
            Components.Add(transform);
            foreach (Type type in components)
            { if (!typeof(Transform).IsAssignableFrom(type)) { var c = (Component)Activator.CreateInstance(type)!; c.gameObject = this; Components.Add(c); } }
        }
        public T AddComponent<T>() where T : Component, new()
        { var value = new T { gameObject = this }; Components.Add(value); return value; }
        public T? GetComponent<T>() where T : Component => Components.OfType<T>().FirstOrDefault(c => !c.Destroyed);
        internal void Remove(Component value) => Components.Remove(value);
        public void SetActive(bool value) => activeSelf = value;
    }
    public class Transform : Component
    {
        public Transform? parent;
        internal static Func<Transform,Transform?,bool>? RefuseParent;
        internal readonly List<Transform> Children = new();
        public int childCount => Children.Count;
        public Vector3 localScale = Vector3.one;
        public Quaternion localRotation = Quaternion.identity;
        private Vector3 _localPosition;
        public virtual Vector3 localPosition { get => _localPosition; set => _localPosition = value; }
        public Vector3 position { get => TransformPoint(Vector3.zero); set => localPosition = parent?.InverseTransformPoint(value) ?? value; }
        public Vector3 lossyScale => parent == null ? localScale : Vector3.Scale(parent.lossyScale, localScale);
        public Vector3 TransformPoint(Vector3 point)
        {
            Vector3 local = localRotation * Vector3.Scale(point, localScale) + localPosition;
            return parent == null ? local : parent.TransformPoint(local);
        }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 local = parent == null ? point : parent.InverseTransformPoint(point);
            local = Quaternion.Inverse(localRotation) * (local - localPosition);
            return new Vector3(local.x / localScale.x, local.y / localScale.y, local.z / localScale.z);
        }
        public Transform GetChild(int index) => Children[index];
        public int GetSiblingIndex() => parent?.Children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index)
        { if (parent == null) return; parent.Children.Remove(this); parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), this); }
        public void SetAsLastSibling() => SetSiblingIndex(parent?.Children.Count ?? 0);
        public void SetParent(Transform? value, bool worldPositionStays = true)
        {
            if (RefuseParent?.Invoke(this,value)==true) return;
            Vector3 world = position;
            parent?.Children.Remove(this); parent = value; parent?.Children.Add(this);
            if (worldPositionStays) position = world;
        }
        public bool IsChildOf(Transform ancestor)
        { for (Transform? current = this; current != null; current = current.parent) if (ReferenceEquals(current, ancestor)) return true; return false; }
    }
    public sealed class RectTransform : Transform
    {
        public enum Axis { Horizontal, Vertical }
        public Vector2 anchorMin = new(.5f,.5f), anchorMax = new(.5f,.5f), pivot = new(.5f,.5f), sizeDelta = new(100,100);
        private Vector3 _anchored;
        public Vector2 anchoredPosition { get => new(_anchored.x,_anchored.y); set { _anchored.x = value.x; _anchored.y = value.y; } }
        public Vector3 anchoredPosition3D { get => _anchored; set => _anchored = value; }
        public Rect rect
        {
            get
            {
                Rect p = (parent as RectTransform)?.rect ?? default;
                float w = sizeDelta.x + (anchorMax.x-anchorMin.x)*p.width, h = sizeDelta.y + (anchorMax.y-anchorMin.y)*p.height;
                return new Rect(-pivot.x*w,-pivot.y*h,w,h);
            }
        }
        public override Vector3 localPosition
        {
            get
            {
                Rect p = (parent as RectTransform)?.rect ?? default;
                return new Vector3(p.xMin + p.width*(anchorMin.x+(anchorMax.x-anchorMin.x)*pivot.x)+_anchored.x,
                    p.yMin + p.height*(anchorMin.y+(anchorMax.y-anchorMin.y)*pivot.y)+_anchored.y,_anchored.z);
            }
            set
            {
                Rect p = (parent as RectTransform)?.rect ?? default;
                _anchored = new Vector3(value.x-p.xMin-p.width*(anchorMin.x+(anchorMax.x-anchorMin.x)*pivot.x),
                    value.y-p.yMin-p.height*(anchorMin.y+(anchorMax.y-anchorMin.y)*pivot.y),value.z);
            }
        }
        public void SetSizeWithCurrentAnchors(Axis axis,float value)
        { Rect p = (parent as RectTransform)?.rect ?? default; if(axis == Axis.Horizontal) sizeDelta.x=value-(anchorMax.x-anchorMin.x)*p.width; else sizeDelta.y=value-(anchorMax.y-anchorMin.y)*p.height; }
        public void GetWorldCorners(Vector3[] output)
        {
            Rect r=rect; output[0]=TransformPoint(new(r.xMin,r.yMin,0)); output[1]=TransformPoint(new(r.xMin,r.yMax,0));
            output[2]=TransformPoint(new(r.xMax,r.yMax,0)); output[3]=TransformPoint(new(r.xMax,r.yMin,0));
        }
    }
    public struct Vector2
    {
        public static Vector2 up=>new(0,1);
        public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;}
        public static Vector2 zero => new(); public static Vector2 one => new(1,1);
        public float sqrMagnitude => x*x+y*y;
        public static Vector2 operator +(Vector2 a,Vector2 b)=>new(a.x+b.x,a.y+b.y);
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a,float b)=>new(a.x*b,a.y*b);
        public static Vector2 operator /(Vector2 a,float b)=>new(a.x/b,a.y/b);
        public static Vector2 Min(Vector2 a,Vector2 b)=>new(Math.Min(a.x,b.x),Math.Min(a.y,b.y));
        public static Vector2 Max(Vector2 a,Vector2 b)=>new(Math.Max(a.x,b.x),Math.Max(a.y,b.y));
        public static implicit operator Vector3(Vector2 value)=>new(value.x,value.y,0);
        public override string ToString()=>$"({x},{y})";
    }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 zero=>new();public static Vector3 one=>new(1,1,1);
        public float sqrMagnitude=>x*x+y*y+z*z;
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);
        public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);
        public static implicit operator Vector2(Vector3 value)=>new(value.x,value.y);
    }
    public struct Quaternion
    {
        internal Numerics.Quaternion Value;
        public static Quaternion identity => new(){Value=Numerics.Quaternion.Identity};
        public static Quaternion Euler(float x,float y,float z)=>new(){Value=Numerics.Quaternion.CreateFromYawPitchRoll(y*MathF.PI/180,x*MathF.PI/180,z*MathF.PI/180)};
        public static Quaternion Inverse(Quaternion value)=>new(){Value=Numerics.Quaternion.Inverse(value.Value)};
        public static Vector3 operator *(Quaternion q,Vector3 v)
        { Numerics.Vector3 r=Numerics.Vector3.Transform(new(v.x,v.y,v.z),q.Value);return new(r.X,r.Y,r.Z); }
    }
    public struct Rect
    {
        public float x,y,width,height;public Rect(float x,float y,float width,float height){this.x=x;this.y=y;this.width=width;this.height=height;}
        public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height;
        public Vector2 size=>new(width,height);public Vector2 center=>new(x+width/2,y+height/2);
        public static Rect MinMaxRect(float left,float bottom,float right,float top)=>new(left,bottom,right-left,top-bottom);
        public bool Overlaps(Rect other)=>xMin<other.xMax&&xMax>other.xMin&&yMin<other.yMax&&yMax>other.yMin;
    }
    public static class Mathf
    {
        public static float Abs(float value)=>Math.Abs(value);public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Min(float a,float b)=>Math.Min(a,b);public static float Clamp(float value,float a,float b)=>Math.Clamp(value,a,b);
        public static int Max(int a,int b)=>Math.Max(a,b); public static int Clamp(int a,int b,int lo)=>Math.Clamp(a,b,lo);
        public static bool Approximately(float a,float b)=>Math.Abs(a-b)<1e-4f;
    }
    public static class Time { public static int frameCount; public static float unscaledTime; }
}
namespace UnityEngine.UI
{
    public enum UIWindowID { None, PartyPanel, HeroLevelUpPanel }
    public sealed class UIWindow : UnityEngine.Behaviour
    { public UIWindowID ID; public bool IsOpen=true,IsVisible=true; public int NativeShowCalls,NativeHideCalls; public void Show(){IsOpen=true;++NativeShowCalls;}public void Hide(){IsOpen=false;IsVisible=false;++NativeHideCalls;} }
    public sealed class LayoutElement : UnityEngine.Behaviour { public bool ignoreLayout; }
    public class Graphic : UnityEngine.Behaviour { public UnityEngine.RectTransform rectTransform=>(UnityEngine.RectTransform)transform;public UnityEngine.Color color=new(1,1,1,1); }
    public sealed class Toggle : UnityEngine.Behaviour { public bool isOn; }
}
namespace UnityEngine
{ public struct Color { public float r,g,b,a;public Color(float r,float g,float b,float a){this.r=r;this.g=g;this.b=b;this.a=a;} } }
