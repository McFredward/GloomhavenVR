using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

internal static class Singleton<T> where T : class
{
    internal static T? Instance;
    internal static bool IsInitialized => Instance != null;
}
internal sealed class UIConfirmationBoxManager
{
    internal static UIConfirmationBoxManager? MainMenuInstance;
    private void ShowGenericConfirmation() { }
    private void ShowGenericWarningConfirmation() { }
    private void ShowGenericCancelConfirmation() { }
    private void ShowGenericSpendConfirmation() { }
    private void ShowCancelActiveAbility() { }
    internal ConfirmationBox CurrentBox = new();
    internal event Action? BoxWindowShown, BoxWindowHidden;
    internal void Show(bool notify = true)
    {
        CurrentBox.IsOpen = true;
        if (notify) BoxWindowShown?.Invoke();
    }
    internal void Hide(bool notify = true)
    {
        // Native UIWindow emits onHidden before assigning its hidden state.
        if (notify) BoxWindowHidden?.Invoke();
        CurrentBox.IsOpen = false;
    }
    internal void Confirm(Action callback) { if (!CurrentBox.IsOpen) return; Hide(); callback(); }
    internal void Cancel(Action callback) { if (!CurrentBox.IsOpen) return; Hide(); callback(); }
}
internal sealed class ConfirmationBox : Component
{
    internal bool IsOpen { get => Window.IsOpen; set { Window.IsOpen = value; Window.IsVisible = value; } }
    internal readonly UIWindow Window;
    internal ConfirmationBox() { Window = gameObject.AddComponent<UIWindow>(); Window.IsOpen = Window.IsVisible = false; }
}
internal sealed class UILevelUpWindow : Component { private void ConfirmSelectCard() { } }
internal sealed class UIResetLevelUpWindow : Component { private void ResetAbilities() { } }
internal sealed class UIPerksInventory : Component { private void OnSelectedPerk() { } }
internal sealed class UIPartyItemInventoryDisplay : Component { private void OnItemSelected() { } }
internal sealed class UIAdventurePartyAssemblyRoster : Component { private void OnSelectedCharacter() { } }
internal sealed class UIOptionsWindow : Component { private void AskLogOut() { } }
internal sealed class ESCMenu : Component { private void LoadMainMenu() { } private void ExitGame() { } }
internal sealed class UIMapEscMenu : Component { private void TurnOffTutorial() { } }
internal sealed class UIMultiplayerEscSubmenu : Component { private void ShowConfirmationBoxToEndSession() { } }
internal sealed class UIScenarioEscMenu : Component
{
    private void QuitDungeon() { } private void RestartScenario() { }
    private void QueueRestartRound() { } private void RestartTurn() { }
    private void SkipMission() { } private void SkipTutorial() { }
}
internal static class Choreographer { internal static object? s_Choreographer => null; }

namespace UnityEngine
{
    internal class Object { internal static void Destroy(Object? value) { if (value is Component c) c.gameObject.Remove(c); } }
    internal class Component : Object
    {
        internal GameObject? Owner;
        internal GameObject gameObject => Owner ?? GameObject.For(this);
        internal Transform transform => this is Transform t ? t : gameObject.transform;
        internal string name => gameObject.name;
        internal T? GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        internal T? GetComponentInParent<T>() where T : class
        { for (Transform? t = transform; t != null; t = t.parent) if (t.GetComponent<T>() is T value) return value; return null; }
    }
    internal sealed class GameObject : Object
    {
        internal readonly Transform transform;
        internal string name = "native";
        internal bool activeSelf = true;
        internal bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        internal int layer;
        internal int scene = 1;
        private readonly List<Component> _components = new();
        internal GameObject(string name = "native", Transform? transform = null)
        { this.name = name; this.transform = transform ?? new RectTransform(); this.transform.Owner = this; _components.Add(this.transform); }
        internal static GameObject For(Component component)
        { var go = new GameObject(transform: component as Transform); component.Owner = go; if (!go._components.Contains(component)) go._components.Add(component); return go; }
        internal T AddComponent<T>() where T : Component, new() { var c = new T { Owner = this }; _components.Add(c); return c; }
        internal T? GetComponent<T>() where T : class => _components.OfType<T>().FirstOrDefault();
        internal void Remove(Component c) => _components.Remove(c);
    }
    internal struct Vector2
    {
        internal float x, y;
        internal Vector2(float x, float y) { this.x = x; this.y = y; }
        internal static Vector2 zero => default;
        internal float sqrMagnitude => x*x+y*y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.x-b.x,a.y-b.y);
    }
    internal struct Vector3
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        internal static Vector3 one => new(1,1,1);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
    }
    internal sealed class Camera : Component { }
    internal class Transform : Component
    {
        private Transform? _parent;
        private readonly List<Transform> _children = new();
        internal Transform? parent => _parent;
        internal Vector3 position { get => localPosition; set => localPosition=value; }
        internal Quaternion rotation { get => localRotation; set => localRotation=value; }
        internal Vector3 lossyScale => localScale;
        internal Vector3 TransformPoint(Vector3 point) => point + localPosition;
        internal void SetPositionAndRotation(Vector3 point,Quaternion facing) { position=point;rotation=facing; }
        internal Vector3 forward => new(0,0,1);
        internal Vector3 localPosition;
        internal Vector3 localScale = Vector3.one;
        internal Quaternion localRotation = Quaternion.identity;
        internal bool RejectReparent;
        internal int childCount => _children.Count;
        internal Transform GetChild(int index) => _children[index];
        internal bool IsChildOf(Transform other) { for (Transform? t=this;t!=null;t=t.parent) if (ReferenceEquals(t,other)) return true;return false; }
        internal void SetParent(Transform? next, bool worldPositionStays) { if (RejectReparent) return; _parent?._children.Remove(this); _parent=next; next?._children.Add(this); }
        internal int GetSiblingIndex() => _parent?._children.IndexOf(this) ?? 0;
        internal void SetSiblingIndex(int index) { if (_parent==null) return; _parent._children.Remove(this); _parent._children.Insert(Math.Clamp(index,0,_parent._children.Count),this); }
        internal void SetAsLastSibling() => SetSiblingIndex(int.MaxValue);
    }
    internal sealed class RectTransform : Transform
    {
        internal Vector2 anchorMin, anchorMax, pivot;
        internal Vector2 anchoredPosition { get => new(localPosition.x, localPosition.y); set { localPosition.x=value.x;localPosition.y=value.y; } }
        internal Vector2 sizeDelta = new(1920,1080);
        internal Rect rect => new(sizeDelta);
    }
    internal readonly struct Rect { internal readonly Vector2 size; internal Rect(Vector2 size) { this.size=size; } }
    internal struct Quaternion { internal float w; internal static Quaternion identity => new(){w=1}; }
    internal sealed class Renderer : Component { }
}
namespace UnityEngine.UI
{
    internal sealed class UIWindow : Component { internal bool IsOpen=true, IsVisible=true; }
    internal sealed class GraphicRaycaster { internal bool enabled = true; }
    internal sealed class LayoutElement : Component { internal bool ignoreLayout; }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] internal sealed class HarmonyPatch : Attribute { }
    internal static class AccessTools
    {
        internal static MethodInfo DeclaredMethod(Type type, string name) => type.GetMethod(name, BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException(type.Name+"."+name);
    }
}
namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static int Warnings;
        internal static void Info(string scope, string value) { }
        internal static void Warn(string scope, string value) { ++Warnings; }
        internal static void Alert(string scope, string value) { ++Warnings; }
    }
}
namespace GloomhavenVR.Core.Events
{
    internal static class VRModeStateMachine { internal static bool TableInFrontOfPlayer = true; }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static class PokeSurfaceTuning { internal const int SmallDialog = 1; }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class Flag { internal bool Value = true; }
    internal static class WorldUIConfig
    {
        internal static readonly Flag Dialogs = new();
        internal static bool ConversionActive = true;
    }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
    internal static class PanelLayout { internal const float WorldScale = 1f; }
    internal sealed class ConvertedPanel
    {
        // Real ConvertedPanel liveness follows the original native rect, which
        // intentionally survives normal conversion release.
        internal bool IsAlive => Target != null;
        internal bool ModLayerEnabled;
        internal RectTransform Target = new();
        internal GameObject HostGo = new("host");
        internal RectTransform HostRect => (RectTransform)HostGo.transform;
        internal Transform? Home;
        internal readonly GraphicRaycaster HostRaycaster = new();
    }
    internal static class CanvasConversion
    {
        internal static int Converts, Releases, Transfers, CompositeReleases;
        internal static readonly List<ConvertedPanel> ActivePanels = new();
        internal static ConvertedPanel? LastDestination;
        internal static bool ReturnNull, ThrowConvert, ThrowPlace, ThrowRelease;
        internal static ConvertedPanel? Last;
        internal static Camera? WorldCamera = new();
        internal static ConvertedPanel? Convert(RectTransform? root, string name, int pokeTuning, bool? fitContent=null, bool flattenWindow=false, bool useModLayer=false)
        {
            ++Converts;
            if (ThrowConvert) throw new InvalidOperationException("convert");
            if (ReturnNull) return null;
            Last = new ConvertedPanel { Target = root!, Home = root!.parent, ModLayerEnabled=useModLayer };
            Last.HostGo.scene=root!.gameObject.scene;root.SetParent(Last.HostRect,false);
            ActivePanels.Add(Last); return Last;
        }
        internal static void Release(ConvertedPanel panel)
        {
            if (ThrowRelease) throw new InvalidOperationException("release");
            ++Releases; ActivePanels.Remove(panel);
            panel.Target.SetParent(panel.Home, false);
        }
        internal static void PlaceHost(ConvertedPanel panel, Vector3 pos, Quaternion rotation, float scale)
        { if (ThrowPlace) throw new InvalidOperationException("placement"); }
        internal static void TransferSeatedSubtree(RectTransform root, ConvertedPanel? destination)
        { ++Transfers; LastDestination = destination; }
        internal static void Reset()
        {
            ConfirmationOnOwner.Reset(); UIConfirmationBoxManager.MainMenuInstance = null;
            ActivePanels.Clear(); Converts = Releases = Transfers = CompositeReleases = 0; Last = LastDestination = null;
            ReturnNull = ThrowConvert = ThrowPlace = ThrowRelease = false;
            WorldCamera = new Camera();
        }
    }
    internal static class ChromeParkTuning { internal const float OffsetEpsilonPx = 0.1f; }
    internal static class ModalFallback
    {
        internal static bool ReleaseForComposite(UIWindow window)
        {
            ++CanvasConversion.CompositeReleases;
            foreach (var panel in CanvasConversion.ActivePanels.ToArray())
                if (ReferenceEquals(panel.Target,window.transform)) CanvasConversion.Release(panel);
            return true;
        }
    }
    internal static class HeadFacing
    {
        internal struct Facing { internal Quaternion Rotation => default; }
        internal static Facing YawOnly(Transform head) => default;
        internal static void LogPlaced(string name, Facing facing, Vector3 pos, string reason) { }
    }
}
