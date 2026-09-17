using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal sealed class Transform
    {
        internal Transform? parent;
        internal bool IsChildOf(Transform ancestor)
        {
            for (Transform? node = this; node != null; node = node.parent)
                if (ReferenceEquals(node, ancestor)) return true;
            return false;
        }
    }
    internal struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(a.x + (b.x-a.x)*t, a.y+(b.y-a.y)*t);
    }
    internal struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x=x; this.y=y; this.width=width; this.height=height; }
        public Rect(Vector2 position, Vector2 size) : this(position.x,position.y,size.x,size.y) { }
        public float xMin => x;
        public float xMax => x+width;
        public float yMin => y;
        public float yMax => y+height;
        public Vector2 position => new(x,y);
        public Vector2 size => new(width,height);
        public Vector2 center => new(x+width/2,y+height/2);
        public static Rect MinMaxRect(float x0,float y0,float x1,float y1) => new(x0,y0,x1-x0,y1-y0);
    }
    internal static class Mathf
    {
        public static float Max(float a,float b) => Math.Max(a,b);
        public static float Min(float a,float b) => Math.Min(a,b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Clamp01(float a) => Math.Clamp(a,0,1);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class ConvertedPanel { public bool Owed; }
    internal static class ModalFallback
    {
        internal static bool AppearStillOwed(ConvertedPanel p,out string why) { why=""; return p.Owed; }
    }
    internal partial class GrabbableModal
    {
        internal static readonly List<GrabbableModal> LiveHolders = new();
        internal ConvertedPanel _panel = new();
        internal UnityEngine.Rect _mrInkRect;
        internal int _mrInkSampleFrame = -1;
        internal bool _mrInkValid;
        internal bool _barHiddenForEmpty;
    }
}

namespace GloomhavenVR
{
    internal static class Defaults { internal const float GrabBarTweenMs = 150f; }
}
