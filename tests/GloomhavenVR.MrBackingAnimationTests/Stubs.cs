using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal class Object
    {
        public bool Destroyed;
        public static void Destroy(Object? obj) { if (obj is not null) obj.Destroyed=true; }
        public static bool operator ==(Object? a,Object? b) => ReferenceEquals(a,b) || (ReferenceEquals(a,null) && b!.Destroyed) || (ReferenceEquals(b,null) && a!.Destroyed);
        public static bool operator !=(Object? a,Object? b) => !(a==b);
        public override bool Equals(object? obj) => ReferenceEquals(this,obj);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }
    internal class GameObject : Object
    {
        public bool activeInHierarchy=true, ThrowOnSet; public bool activeSelf=>activeInHierarchy;
        public void SetActive(bool active) { if(ThrowOnSet) throw new InvalidOperationException("destroyed plate"); activeInHierarchy=active; }
    }
    internal class Transform : Object
    {
        public GameObject gameObject=new(); public Renderer Renderer=new MeshRenderer();
        public T GetComponent<T>() where T:class => (Renderer as T)!;
    }
    internal sealed class RectTransform : Transform { public Rect rect=new(0,0,400,300); }
    internal class Renderer : Object { public Material? sharedMaterial; public void SetPropertyBlock(MaterialPropertyBlock b) { } }
    internal sealed class MeshRenderer : Renderer { }
    internal sealed class Material : Object { public Color color; }
    internal struct Color { }
    internal sealed class MaterialPropertyBlock { public void SetFloat(int id,float value) { } }
    internal sealed class CanvasRenderer { public float Alpha=1f; public void SetAlpha(float a) { Alpha=a; } }
    internal static class Time { public static float unscaledTime; }
    internal struct Vector2
    {
        public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; }
        public static Vector2 zero=>default;
        public Vector2 normalized { get { float n=MathF.Sqrt(x*x+y*y);return new(x/n,y/n); } }
        public static Vector2 operator -(Vector2 a,Vector2 b)=>new(a.x-b.x,a.y-b.y);
        public static Vector2 operator *(Vector2 a,float s)=>new(a.x*s,a.y*s);
        public static float Dot(Vector2 a,Vector2 b)=>a.x*b.x+a.y*b.y;
        public static Vector2 Lerp(Vector2 a,Vector2 b,float t)=>new(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t);
    }
    internal struct Rect
    {
        public float x,y,width,height; public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;}
        public Rect(Vector2 p,Vector2 s):this(p.x,p.y,s.x,s.y){}
        public float xMin=>x;public float xMax=>x+width;public float yMin=>y;public float yMax=>y+height;
        public Vector2 center=>new(x+width/2,y+height/2);public Vector2 size=>new(width,height);public Vector2 position=>new(x,y);
        public static Rect MinMaxRect(float x0,float y0,float x1,float y1)=>new(x0,y0,x1-x0,y1-y0);
    }
    internal static class Mathf
    {
        public static float Floor(float x)=>MathF.Floor(x);public static float Abs(float x)=>MathF.Abs(x);
        public static float Max(float a,float b)=>MathF.Max(a,b); public static float Min(float a,float b)=>MathF.Min(a,b);
        public static int Min(int a,int b)=>Math.Min(a,b);
        public static float Clamp01(float x)=>Math.Clamp(x,0,1);public static float Lerp(float a,float b,float t)=>a+(b-a)*Clamp01(t);
    }
}
namespace GloomhavenVR { internal static class Defaults { public const float GrabBarTweenMs=150f; } }
namespace GloomhavenVR.Core
{
    internal static class VRLog { public static bool Throw; public static int Warnings; public static void Warn(string scope,string message) { Warnings++;if(Throw)throw new InvalidOperationException("logger unavailable"); } }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    internal sealed class ConvertedPanel
    {
        public bool MrBackingSuppressed,RenderHidden,OwnerRenderHidden;
        public RectTransform? HostRect=new();public GameObject? HostGo=new();public Transform? FitContentRoot;
        public Transform Target=new();
    }
    internal static class MixedReality { public static bool BackingsWanted=true; }
    internal sealed class MrBackingVisibility { public Transform? Root;public readonly List<CanvasRenderer> Witnesses=new(); }
    internal static class PanelInkBounds
    {
        internal struct Ink { public bool Valid;public Rect Rect;public int Plates;public float PlateBottom; }
        public static bool Valid=true,Throw;public static Rect Bounds=new(20,30,200,120);public static Action? OnMeasure;
        public static bool TryMeasure(ConvertedPanel p,out Ink ink,Transform? contentRoot=null,List<CanvasRenderer>? visibleWitnesses=null)
        {
            if(Throw)throw new InvalidOperationException("measure");OnMeasure?.Invoke();ink=new(){Valid=Valid,Rect=Bounds};return Valid;
        }
    }
    internal static class GrabbableModal
    {
        public static bool Cached;public static Rect CachedRect=new(10,20,150,90);
        public static bool TryGetMrBackingRect(ConvertedPanel p,out Rect rect,out bool visible,out int frame){rect=CachedRect;visible=Cached;frame=0;return Cached;}
    }
    internal static class CanvasConversion
    {
        public static bool SeatVeilStanding;public static float SeatVeilClamp(CanvasRenderer r,float a)=>a;
        public static void RegisterOrderFollower(ConvertedPanel p,MeshRenderer r,int order) { }
    }
    // Renderer allocation/field geometry have separate production tests. These doubles allow
    // faults at the decoration boundary while exercising the actual animation state machine.
    internal sealed class MrBackingMaterialise
    {
        public static bool ThrowApply,ThrowRestore;public static int Disposals;
        public float LastProgress;
        public void Restore(Transform? plate) { if(ThrowRestore)throw new InvalidOperationException("restore"); }
        public void Apply(Transform plate,Rect bounds,Rect frame,float progress,Material mat)
        { if(ThrowApply)throw new InvalidOperationException("apply");LastProgress=progress; }
        public void Dispose() { Disposals++; }
    }
    internal static partial class MrBacking
    {
        private sealed class PanelEntry
        {
            internal ConvertedPanel Panel=null!;internal Transform? Plate;internal Material? FadeMat;
            internal readonly MrBackingAnimationState Animation=new();internal readonly MrBackingLayout Layout=new();
            internal readonly MrBackingVisibility Visibility=new();internal MrBackingMaterialise? Materialise;
            internal bool Faded;internal Rect Shown;
        }
        private static readonly List<PanelEntry> Panels=new();private static bool _applied;
        private static Material? _plateMat;private static Color _plateColor;
        private static void EnsurePlateMaterial() { _plateMat ??= new(); }
        private static void DestroyPlate(Transform? t,Material? m){Object.Destroy(t);Object.Destroy(m);}
        private static Material CreateFadeMaterial()=>new();private static Transform CreatePlate(RectTransform host) { var plate=new Transform();plate.Renderer.sharedMaterial=_plateMat;return plate; }
        private static void Fit(Transform plate,RectTransform host,Vector2 size,Vector2 center) { }
        private static Rect GlyphTrueRect(ConvertedPanel panel,RectTransform host,Rect rect,out bool glyph,Transform? contentRoot=null) {glyph=false;return rect;}
        internal static void ResetTest() { Panels.Clear();GrabbableModal.Cached=false;_applied=false;_plateMat=null;MixedReality.BackingsWanted=true;PanelInkBounds.Valid=true;PanelInkBounds.Throw=false;PanelInkBounds.OnMeasure=null;MrBackingMaterialise.ThrowApply=false;MrBackingMaterialise.ThrowRestore=false;MrBackingMaterialise.Disposals=0;GloomhavenVR.Core.VRLog.Throw=false;GloomhavenVR.Core.VRLog.Warnings=0;Time.unscaledTime=0; }
        internal static int EntryCount=>Panels.Count;
        internal static bool Ready=>_applied&&_plateMat!=null;
        internal static bool Active(ConvertedPanel p)=>FindAnimationEntry(p)?.Animation.Active??false;
        internal static bool Closed(ConvertedPanel p)=>FindAnimationEntry(p)?.Animation.Closed??false;
        internal static Transform? Plate(ConvertedPanel p)=>FindAnimationEntry(p)?.Plate;
        internal static float Progress(ConvertedPanel p)=>FindAnimationEntry(p)!.Animation.Progress;
        internal static Rect Bounds(ConvertedPanel p)=>FindAnimationEntry(p)!.Animation.Bounds;
        internal static Material? Fade(ConvertedPanel p)=>FindAnimationEntry(p)!.FadeMat;
        internal static MrBackingLayout Layout(ConvertedPanel p)=>FindAnimationEntry(p)!.Layout;
        internal static bool Visible(ConvertedPanel p)=>Plate(p)?.gameObject.activeInHierarchy??false;
    }
    internal sealed class VisibilityHold { public bool Released;public void Assert(){}public void Release(string reason){Released=true;} }
    internal static class WindowMaterialise
    {
        internal sealed class DebrisCloud { public Object? MeshFront,MeshBehind;public Renderer? Front,Behind; }
        public static float Intensity=1;public static void Unregister(WindowMaterialiseRunner r){}public static void ReleaseMesh(Object? m){}
    }
    internal sealed partial class WindowMaterialiseRunner
    {
        internal ConvertedPanel Panel=new();private bool _finished,_materialising;
        private Action? _onDone;internal bool Vanishing=>!_materialising;internal bool PointerBlind;
        private VisibilityHold? _hold=new();private WindowMaterialise.DebrisCloud? _debris;
        private MaterialPropertyBlock? _mpb;private const int FrontId=1,SizeScaleId=2;
        private List<CanvasRenderer>? _renderers=new(){new()};private List<float>? _origAlpha=new(){0.7f};
        private List<float>? _threshold=new(){0.1f,0.25f,0.5f,0.75f,0.9f};
        private GameObject gameObject=new();internal int Restores,Returns;
        internal WindowMaterialiseRunner(ConvertedPanel panel,bool appear,Action done) { Panel=panel;_materialising=appear;_onDone=done; }
        internal void Frame(float time)=>Apply(time);internal float Alpha=>_renderers![0].Alpha;
        private void Report(string reason){}private void ReturnBuffers(){Returns++;}
        private static void Destroy(Object o)=>Object.Destroy(o);
        private void RestoreAll(){Restores++;_renderers![0].SetAlpha(_origAlpha![0]);}
    }
}
