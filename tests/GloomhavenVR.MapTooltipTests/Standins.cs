using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal struct Vector2 { internal float x, y; internal Vector2(float a, float b) { x = a; y = b; } }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        internal float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x * b, a.y * b, a.z * b);
        internal static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new(Mathf.Lerp(a.x,b.x,t), Mathf.Lerp(a.y,b.y,t), Mathf.Lerp(a.z,b.z,t));
    }
    internal struct Vector4 { internal float x, y, z, w; internal Vector4(float a, float b, float c, float d) { x=a; y=b; z=c; w=d; } }
    internal struct Quaternion
    {
        internal float x, y, z, w;
        internal Quaternion(float a, float b, float c, float d) { x=a; y=b; z=c; w=d; }
        internal static float Dot(Quaternion a, Quaternion b) => a.x*b.x+a.y*b.y+a.z*b.z+a.w*b.w;
        internal static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            var q = System.Numerics.Quaternion.Slerp(new(a.x,a.y,a.z,a.w), new(b.x,b.y,b.z,b.w), Math.Clamp(t,0,1));
            return new(q.X,q.Y,q.Z,q.W);
        }
    }
    internal struct Color
    {
        internal float r, g, b, a;
        internal Color(float red,float green,float blue,float alpha) { r=red;g=green;b=blue;a=alpha; }
        internal static Color white => new(1,1,1,1);
        internal static Color Lerp(Color a, Color b, float t) => new(Mathf.Lerp(a.r,b.r,t),Mathf.Lerp(a.g,b.g,t),Mathf.Lerp(a.b,b.b,t),Mathf.Lerp(a.a,b.a,t));
    }
    internal static class Mathf
    {
        internal static float Lerp(float a,float b,float t) => a+(b-a)*Math.Clamp(t,0,1);
        internal static float Abs(float n) => Math.Abs(n);
    }
    internal static class Time { internal static float unscaledTime; }
    internal sealed class GameObject
    {
        internal bool activeSelf;
        internal readonly List<object> Components = new();
        internal void SetActive(bool value) => activeSelf=value;
        internal T? GetComponent<T>() where T : class
        { foreach (object c in Components) if (c is T match) return match; return null; }
        internal T AddComponent<T>() where T : new() { var c = new T(); Components.Add(c!); return c; }
    }
    internal class Transform
    {
        internal readonly GameObject gameObject = new();
        internal Transform? parent;
        internal T? GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    internal sealed class RectTransform : Transform
    {
        internal Vector2 anchorMin, anchorMax, pivot, sizeDelta;
        internal Vector3 anchoredPosition3D, localScale, position;
        internal Quaternion localRotation, rotation;
        internal void SetPositionAndRotation(Vector3 p, Quaternion q) { position=p;rotation=q; }
    }
    internal sealed class CanvasGroup
    {
        internal bool enabled=true, ignoreParentGroups;
        internal float alpha=1;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static bool TryGetParchmentFrame(out UnityEngine.Vector3 center,out float scale)
        { center=new(10,20,30);scale=4;return true; }
    }
    internal static partial class MapButtonTooltipPresentation
    {
        private static UnityEngine.RectTransform ResolveSource(bool detail) => new();
        private static void Report(Exception e) => throw new Exception("unexpected presentation failure",e);
        private sealed class Surface
        {
            internal bool Visible;
            internal void Show(bool visible) => Visible=visible;
            internal void Hide() => Visible=false;
            internal void Destroy() => Visible=false;
            internal void Paint(Picture p,UnityEngine.RectTransform source,string[]? lines,
                Picture? previous=null,float progress=1,UnityEngine.Vector3 center=default,float mapScale=1)
            { Visible=true; }
        }
    }
}
namespace UnityEngine.UI
{
    internal sealed class CanvasRenderer
    {
        internal UnityEngine.Color Color;
        internal void SetColor(UnityEngine.Color color) => Color=color;
    }
    internal class Graphic
    {
        internal bool enabled, raycastTarget=true;
        internal UnityEngine.Color color;
        internal readonly CanvasRenderer canvasRenderer = new();
    }
}
namespace TMPro
{
    internal enum FontWeight { Regular=400, Bold=700 }
    internal enum FontStyles { Normal=0, Bold=1, Italic=2 }
    internal enum TextAlignmentOptions { Left=257, Right=260 }
    internal enum TextOverflowModes { Overflow=0, Ellipsis=1 }
    internal sealed class TMP_Text : UnityEngine.UI.Graphic
    {
        internal string text="";
        internal float fontSize,fontSizeMin,fontSizeMax,characterSpacing,wordSpacing,lineSpacing,paragraphSpacing;
        internal FontWeight fontWeight;
        internal FontStyles fontStyle;
        internal TextAlignmentOptions alignment;
        internal TextOverflowModes overflowMode;
        internal bool enableAutoSizing,enableWordWrapping,richText;
        internal UnityEngine.Vector4 margin;
    }
}
