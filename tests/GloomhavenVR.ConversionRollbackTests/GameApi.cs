#pragma warning disable CS0649 // The game supplies these serialized fields; this fixture selects only relevant inputs.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    internal class Object
    {
        internal bool Destroyed;
        internal int DestroyFaults;
        internal string name = "fixture";
        public static bool operator ==(Object? a, Object? b) => (ReferenceEquals(a, null) || a.Destroyed) ? ReferenceEquals(b, null) || b.Destroyed : ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
        internal static void Destroy(Object value) { if(value.DestroyFaults>0){value.DestroyFaults--;throw new InvalidOperationException("host destroy fault");}value.Destroyed = true; }
    }
    internal readonly struct Vector2 { internal readonly float x, y; internal Vector2(float x, float y) { this.x=x;this.y=y; } }
    internal readonly struct Vector3 { internal readonly float x,y,z; internal Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; } }
    internal readonly struct Quaternion { }
    internal class Component : Object
    {
        internal GameObject gameObject = null!;
        internal Transform transform => gameObject.transform;
        internal T? GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    internal class Transform : Component
    {
        internal Transform? parent;
        internal Vector3 localPosition, localScale;
        internal Quaternion localRotation;
        internal bool RefuseParent;
        internal Transform? RefuseSpecificParent;
        internal int ThrowParentCount;
        internal void SetParent(Transform? value, bool worldPositionStays)
        {
            if (ThrowParentCount > 0) { ThrowParentCount--; throw new InvalidOperationException("reparent fault"); }
            if (!RefuseParent && (RefuseSpecificParent == null || !ReferenceEquals(value,RefuseSpecificParent))) parent = value;
        }
        internal bool IsChildOf(Transform other)
        {
            for (Transform? at=this; at!=null; at=at.parent) if (ReferenceEquals(at,other)) return true;
            return false;
        }
        internal void SetSiblingIndex(int index) { }
    }
    internal sealed class RectTransform : Transform
    {
        internal Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
    }
    internal sealed class GameObject : Object
    {
        internal readonly RectTransform transform;
        internal int layer;
        private readonly Dictionary<Type,Component> _components=new();
        internal GameObject(string label="object") { name=label;transform=new RectTransform { gameObject=this,name=label }; }
        internal T Add<T>() where T : Component,new() { var item=new T { gameObject=this };_components.Add(typeof(T),item);return item; }
        internal T? GetComponent<T>() where T : Component => _components.TryGetValue(typeof(T),out var value) ? (T)value : null;
    }
    internal sealed class Canvas : Component { internal bool enabled=true,overrideSorting;internal int sortingOrder;internal Camera? worldCamera; }
    internal sealed class Camera : Component { }
    internal sealed class CanvasGroup : Component { internal float alpha=1;internal bool blocksRaycasts=true,interactable=true; }
    internal sealed class Renderer : Component { internal bool enabled=true; }
}
namespace UnityEngine.UI
{
    internal class Graphic : UnityEngine.Component { internal bool enabled=true; }
    internal sealed class GraphicRaycaster : UnityEngine.Component { }
    internal sealed class RectMask2D : UnityEngine.Component { internal bool enabled=true; }
    internal sealed class UIWindow : UnityEngine.Component { internal bool IsOpen=true;internal bool _disableCanvas;internal int ID; }
}
namespace GloomhavenVR.WorldUI
{
    using UnityEngine;
    using UnityEngine.UI;
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target=null!;
        internal Transform? OriginalParent;
        internal int OriginalSiblingIndex;
        internal Vector2 OriginalAnchorMin,OriginalAnchorMax,OriginalPivot,OriginalAnchoredPosition,OriginalSizeDelta;
        internal Vector3 OriginalLocalScale,OriginalLocalPosition;
        internal Quaternion OriginalLocalRotation;
        internal int TargetHomeScene;
        internal bool TargetWasPersistent;
        internal GameObject? HostGo;
        internal Canvas? HostCanvas;
        internal bool PerFrameGuards,GuardHostMoving,GuardHostHeld,OrderListed,AdoptedOrderRebaseDirty,KeepBackgroundHidden;
        internal object? OrderSwapPeer;
        internal int OrderSwapStreak,RebaseEligible,RebaseDistinctOriginals,RebaseLifted,RebaseClamped,AdoptedOverrideAtAdoption,AdoptedOverrideNow,RebaseMinOriginal,RebaseMaxOriginal;
        internal readonly List<object> OrderFollowers=new();
        internal readonly List<NestedCanvasRecord> AdoptedCanvases=new();
        internal readonly List<Canvas> PreCapturedCanvases=new();
        internal readonly List<Camera?> PreCapturedCameras=new();
        internal readonly List<LayerRecord> Relayered=new();
        internal readonly List<Graphic> HiddenBackgrounds=new();
        internal readonly List<RectMask2D> AddedScrollMasks=new(),EnabledScrollMasks=new();
        internal readonly List<FlattenRecord> Flattened=new();
        internal readonly List<Canvas> HiddenCanvases=new();
        internal readonly List<Renderer> HiddenRenderers=new();
    }
    internal struct NestedCanvasRecord { internal Canvas Canvas;internal bool OriginalOverrideSorting,ConcededOverrideSorting;internal int OriginalSortingOrder;internal Camera? OriginalWorldCamera;internal GraphicRaycaster? AddedRaycaster; }
    internal struct LayerRecord { internal Transform Transform;internal int OriginalLayer; }
    internal struct FlattenRecord { internal Transform Transform;internal float OriginalLocalZ;internal Quaternion OriginalLocalRotation; }
    internal sealed class GrabbableModal
    {
        internal int Failures,Calls;
        internal bool Destroyed;
        internal void Destroy() { Calls++;if(Failures>0){Failures--;throw new InvalidOperationException("chrome fault");}Destroyed=true; }
    }
    internal static class PanelSupersample { internal static void NoticeRelease(ConvertedPanel panel) { } }
    internal static class UguiPokeSurfaces { internal static void Unregister(Canvas canvas) { } }
    internal static class VRLog
    {
        internal static int Alerts;
        internal static void Alert(string scope,string text) { Alerts++; }
        internal static void Info(string scope,string text) { }
        internal static void Error(string scope,string text) { }
    }
    internal static partial class ModalFallback
    {
        internal sealed class WindowPanel { internal ConvertedPanel Panel=null!; }
        internal static readonly List<WindowPanel> Converted=new();
        internal static void NoteReleasedWhileOpen(UIWindow window) { }
    }
    internal static partial class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> Active=new();
        private static readonly List<Canvas> ReleaseCameraCanvases=new();
        private static readonly List<Camera?> ReleaseCameraWanted=new();
        private static readonly List<GameObject> DeferredHosts=new();
        private static readonly List<int> DeferredHostTries=new(),DeferredHostScenes=new();
        private static readonly List<bool> DeferredHostPersistent=new();
        internal static int CameraRestoreFaults,TargetSceneRestores,ReleaseAttempts;
        internal static IDisposable Transaction(ConvertedPanel panel)=>new ConversionTransaction(panel);
        internal static void CompleteTransaction(IDisposable transaction)=>((ConversionTransaction)transaction).Complete();
        internal static bool Pending(RectTransform target)=>HasFailedConversion(target);
        internal static int Deferred=>DeferredHosts.Count;
        internal static void Retry()=>ServiceFailedConversions();
        private static void SetPanelRenderVisible(ConvertedPanel panel,bool visible)
        {
            ReleaseAttempts++;
            foreach(var canvas in panel.HiddenCanvases) canvas.enabled=visible;
            foreach(var renderer in panel.HiddenRenderers) renderer.enabled=visible;
            panel.HiddenCanvases.Clear();panel.HiddenRenderers.Clear();
        }
        private static void RestoreTargetScene(ConvertedPanel panel,RectTransform target) { TargetSceneRestores++; }
        private static bool HoldReleasedWindowDark(UIWindow window,RectTransform target)=>false;
        private static bool RestoreAdoptedCameras(string scope)
        {
            if(CameraRestoreFaults>0){CameraRestoreFaults--;throw new InvalidOperationException("camera restore fault");}
            for(int i=0;i<ReleaseCameraCanvases.Count;i++) ReleaseCameraCanvases[i].worldCamera=ReleaseCameraWanted[i];
            return false;
        }
        private static void NoteReleaseOwnership(string name,bool cameraWrong,bool beltHid,bool gameClosed,bool isWindow) { }
        private static void ReleaseHiddenWindowVeil(ConvertedPanel panel) { }
        private static void RestoreCameraMask() { }
        private static bool HostHoldsGameContent(GameObject host,out string blocker)
        {
            blocker="native";
            foreach(var target in Program.Targets) if(target.IsChildOf(host.transform))return true;
            return false;
        }
    }
}
