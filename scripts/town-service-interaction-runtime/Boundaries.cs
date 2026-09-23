// Unity transforms, colliders, EventSystem, Selectable and Button are real engine types.
// Game controllers, hand tracking and visual construction are explicit fixture boundaries.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;

public class UIWindow : MonoBehaviour
{
    public bool IsOpen = true, IsVisible = true;
    public UnityEvent onHidden = new UnityEvent();
    // Native onHidden fires while IsOpen still reports true.
    public void Hide() { onHidden.Invoke(); IsOpen = false; Probe.Events.Add("native-continuation"); }
}
public enum EGuildmasterMode { Merchant, Temple, Enchantress }
public class UIShopItemWindow : UIWindow { public UIShopItemInventory ItemInventory = null!; public Button exitShopButton = null!; }
public class UIShopItemInventory : MonoBehaviour
{ public object character = new object(); public int mode; public List<UIShopItemSlot> slotPool = new(); }
public class UIShopItemSlot : MonoBehaviour { public Selectable Selectable = null!; public object Item = new object(); }
public class UITempleWindow : UIWindow { public object character = new object(); public TempleShop Shop = null!; }
public class TempleShop : MonoBehaviour { public List<UITempleShopSlot> slots = new(); }
public class UITempleShopSlot : MonoBehaviour { public Selectable button = null!; public object Blessing = new object(); }
public class UINewEnhancementWindow : UIWindow
{
    public object character = new object(); public int mode;
    public AbilityCardUI? selectedCard, SowingCard;
    public EnhancementShop enhancementShop = null!;
    public CardHolder cardHolder = null!;
    public CardsDisplay CardsDisplay = null!;
}
public class AbilityCardUI { public object AbilityCard = new object(); }
public class EnhancementShop : MonoBehaviour { public List<UINewEnhancementShopSlot> slotsPool = new(); }
public class CardHolder : MonoBehaviour { public AbilityCardUI? Card; }
public class CardsDisplay : MonoBehaviour
{ public RectTransform abilityCardsPanel = null!; public List<UIEnhanceCardSlot> slotsPool = new(); }
public class UINewEnhancementShopSlot : MonoBehaviour { public Selectable button = null!; public object enhancement = new object(); }
public class UIEnhanceCardSlot : MonoBehaviour { public Selectable Selectable = null!; public AbilityCardUI? AbilityCard; }
public static class Probe
{
    public static List<string> Events = new();
    public static List<GameObject> Roots = new();
    public static GameObject Go(string name, Transform? parent = null)
    {
        var go = new GameObject(name, typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false); else Roots.Add(go);
        return go;
    }
}
namespace GloomhavenVR.Core { internal static class VRLog { public static void Note(string a, string b) { } } }
namespace GloomhavenVR.Core
{ internal static class Loc { internal static event Action? OnChanged { add { } remove { } } internal static string Mod(string key) => key; } }
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TMP_FontAsset : ScriptableObject { public Material material = null!; }
    public class TMP_Text : MonoBehaviour
    {
        public TMP_FontAsset? font; public Material? fontSharedMaterial;
        public RectTransform rectTransform => (RectTransform)transform;
        public TextAlignmentOptions alignment; public float fontSize, fontSizeMin, fontSizeMax;
        public bool enableAutoSizing; public Color color; public string text = "";
    }
    public class TextMeshPro : TMP_Text { }
}
namespace GloomhavenVR.Compat
{
    internal enum ControlAction { ProximityGrab }
    internal static class ControlsProgress { public static void Notify(ControlAction action) { } }
}
namespace GloomhavenVR.Board.FigureGrab
{ internal static class HeldPoseMirror { internal static float OffsetSign(bool left) => left ? -1 : 1; } }
namespace GloomhavenVR.Cards
{
    internal sealed class ConfigFloat { internal float Value; }
    internal static class CardsConfig
    { internal static ConfigFloat HeldOffPalm = new(), HeldForward = new(), HeldFaceBias = new(); }
    internal static class CardGripPose
    {
        internal static void ReadingPose(float bias, float side, Vector3 pinch, float a, float b,
            out Vector3 position, out Quaternion rotation) { position = Vector3.zero; rotation = Quaternion.identity; }
    }
}
namespace GloomhavenVR.Hands
{
    internal enum HandSide { Left, Right }
    internal enum Finger { Thumb, Index }
    internal enum HapticPreset { GrabPulse }
    internal struct FingerJoints { internal bool IsValid; internal Transform Tip; }
    internal sealed class RigFixture
    { internal Transform GrabAnchor = Probe.Go("hand").transform; internal FingerJoints GetFinger(Finger f) => default; }
    internal sealed class VRHand
    {
        internal bool HasPose = true, TriggerUp;
        internal bool TriggerPressed, GripPressed;
        internal Vector3 PalmVelocity;
        internal HandSide Side;
        internal RigFixture Rig = new();
        internal ProximityGrabber Grabber;
        internal VRHand() { Grabber = new ProximityGrabber(this); }
        internal void SendHaptic(HapticPreset h) { }
    }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static class VRInteractables
    {
        internal static void RegisterGrabbable(IGrabbable token, Collider shape) { }
        internal static void UnregisterGrabbable(IGrabbable token) { }
    }
    internal partial class ProximityGrabber
    {
        private VRHand _hand;
        private bool _releaseOnTriggerUp;
        private string _grabLabel = "fixture";
        internal IGrabbable? Held;
        internal IGrabbable? Highlighted;
        internal ProximityGrabber(VRHand hand) { _hand = hand; }
        internal void Grab(IGrabbable target) => BeginGrab(target, true, "trigger", "fixture");
        internal bool Heal() => HealDeadHeld();
        private void SetHighlighted(IGrabbable? next)
        {
            if (Highlighted is IGrabHighlight old) old.OnGrabHighlight(_hand, false);
            Highlighted = next;
        }
        private void LogGrab(string message) { }
        private static string DescribeGrabbable(IGrabbable held) => "sample";
    }
}
namespace GloomhavenVR.Net
{
    internal sealed class RemoteWidgetMirror
    {
        internal RemoteWidgetMirror(string name, Transform parent, float a, float b, Vector2 offset) { }
        internal bool Refresh(RectTransform source) => true;
        internal Transform? CloneOf(Transform source) => null;
        internal void TickLive() { }
        internal void Destroy() { }
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRoomDriver
    {
        internal static bool Active = true;
        internal static bool TryGetParchmentFrame(out Vector3 center, out float scale)
        { center = Vector3.zero; scale = 1; return true; }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static class TownServicePhysicalRay { internal static void Claim(GloomhavenVR.Hands.VRHand hand) { } }
    internal sealed class ConfigBool { internal bool Value = true; }
    internal static class WorldUIConfig
    { internal static ConfigBool ImmersiveTownServices = new(); }
    internal static class GuildmasterDestinations
    {
        internal static UIWindow? Window;
        internal static EGuildmasterMode Mode;
        internal static EGuildmasterMode CurrentDestinationMode() => Mode;
        internal static UIWindow? ModeWindow(EGuildmasterMode mode) => Window;
    }
    internal static class PanelLayout { internal static float WorldScale = 1; }
    internal static class TownServiceAssets { internal static GameObject? Prefab(string name) => Probe.Go(name); }
    internal static class WorldUIAssets
    { internal static Material CreateFlatMaterial(Color color) => new Material(Shader.Find("UI/Default")) { color = color }; }
    internal sealed class ConvertedPanel
    {
        internal Transform Target = null!;
        internal Transform? OriginalParent;
        internal GameObject HostGo = null!;
        internal bool IsAlive = true;
        internal int FitAppliedGeneration;
    }
    internal class GrabbableModal { }
    internal sealed class GrabFixture
    {
        internal Vector3 Position; internal Quaternion Rotation;
        internal void Destroy() { }
        internal void SnapFrameTo(Vector3 position, Quaternion rotation) { Position = position; Rotation = rotation; }
    }
    internal sealed class WindowPanel
    {
        internal UIWindow Window = null!; internal ConvertedPanel Panel = null!;
        internal GrabFixture? Grab = new(); internal bool UserClosing, PoseRePlaceDone, ReflowCancelled;
        internal Vector3 SpawnAnchor; internal int PoseRePlacedAtFit;
    }
    internal static class CanvasConversion
    {
        internal static List<ConvertedPanel> ActivePanels = new();
        internal static bool DeferParent, KeepActive;
        internal static ConvertedPanel Convert(Transform target)
        {
            var panel = new ConvertedPanel { Target = target, OriginalParent = target.parent, HostGo = Probe.Go("host:" + target.name) };
            target.SetParent(panel.HostGo.transform, false); ActivePanels.Add(panel); return panel;
        }
        internal static void Release(ConvertedPanel panel)
        {
            Probe.Events.Add("release:" + panel.Target.name);
            if (!DeferParent) panel.Target.SetParent(panel.OriginalParent, false);
            if (!KeepActive) { ActivePanels.Remove(panel); panel.IsAlive = false; }
        }
    }
    internal static class WindowMaterialise
    {
        internal static void DropPreRoll(ConvertedPanel panel, string reason) { }
        internal static void Cancel(ConvertedPanel panel, string reason) { Probe.Events.Add("cancel-animation"); }
    }
    internal static partial class ModalFallback
    {
        internal static List<WindowPanel> Converted = new();
        internal static bool FailConvert;
        internal static bool TryConvertWindow(UIWindow window)
        {
            if (FailConvert) return false;
            var panel = CanvasConversion.Convert(window.transform);
            Converted.Add(new WindowPanel { Window = window, Panel = panel }); return true;
        }
        private static void ReleasePreConvertHide(UIWindow window, string reason) { }
        private static void ReleaseScreenBind(UIWindow window, string reason) { }
    }
    internal sealed class TownServiceSurface : IDisposable
    {
        private ConvertedPanel _panel;
        internal static int FailAt;
        internal TownServiceSurface(ushort id, RectTransform source, Vector3 offset, float width, Transform? counterAnchor = null)
        {
            Probe.Events.Add("section:" + id);
            if (FailAt == id) throw new InvalidOperationException("section fixture failure");
            _panel = CanvasConversion.Convert(source);
        }
        internal bool OwnsGrab(GrabbableModal holder) => false;
        internal void SetVisibility(float value, bool allowInput = true) { }
        internal void Tick(Vector3 origin, Quaternion yaw, float scale) { }
        internal void LateTick() { }
        public void Dispose() => CanvasConversion.Release(_panel);
    }
    // Catalog composition has a separate real-Unity harness. Here it owns original rows
    // outside the masked context and REAL production tokens; no token fence is stubbed.
    internal sealed class TownServiceCatalog : IDisposable
    {
        private readonly List<TownServiceToken> _tokens = new();
        private readonly Dictionary<Transform, Transform> _parents = new();
        internal IReadOnlyCollection<TownServiceToken> Samples => _tokens;
        internal TownServiceCatalog(UIShopItemInventory inventory, Transform counter,
            Func<object?> context, Func<bool> session, Transform mat)
        {
            foreach (UIShopItemSlot slot in inventory.slotPool)
            {
                _parents.Add(slot.transform, slot.transform.parent);
                slot.transform.SetParent(counter, false);
                _tokens.Add(new TownServiceToken((RectTransform)slot.transform, slot.Selectable,
                    () => slot.Item, context, session, mat));
            }
        }
        internal void Tick(float scale = 1) { foreach (var token in _tokens) token.Tick(scale); }
        internal bool CanRelocate => !_tokens.Exists(t => t.IsMoving);
        internal void SetVisibility(float value, float relocation = 1f, bool allowInput = true) { }
        internal void LateTick() { }
        public void Dispose()
        {
            foreach (var token in _tokens) token.Dispose();
            foreach (var pair in _parents) pair.Key.SetParent(pair.Value, false);
            _tokens.Clear();
        }
    }
    // Ritual artwork/geometry have a separate fixture. This boundary feeds genuine
    // production tokens and the presentation-supplied lifetime/context delegates.
    internal sealed class TownServiceRitual : IDisposable
    {
        internal static bool Fail;
        private readonly List<TownServiceToken> _tokens = new();
        private readonly Dictionary<Transform, Transform> _parents = new();
        internal IReadOnlyCollection<TownServiceToken> Samples => _tokens;
        internal Transform Root { get; }
        internal bool CanRelocate => !_tokens.Exists(t => t.IsMoving);
        internal void SetVisibility(float value, bool input) { }
        internal TownServiceRitual(UIWindow window, byte service, Transform parent,
            Func<bool> session, Func<object?> context)
        {
            if (Fail) throw new InvalidOperationException("ritual fixture failure");
            Root = Probe.Go("ritual", parent).transform;
            Transform mat = TownServicePresentation.WorkMat!;
            if (service == 2)
            {
                foreach (var slot in window.GetComponent<UITempleWindow>().Shop.slots)
                    Add(slot.transform, slot.button, () => slot.Blessing, context, session, mat);
            }
            else foreach (var slot in window.GetComponent<UINewEnhancementWindow>().CardsDisplay.slotsPool)
                Add(slot.transform, slot.Selectable, () => slot.AbilityCard?.AbilityCard, context, session, mat);
        }
        private void Add(Transform source, Selectable button, Func<object?> identity,
            Func<object?> context, Func<bool> session, Transform mat)
        {
            _parents.Add(source, source.parent); source.SetParent(Root, false);
            _tokens.Add(new TownServiceToken((RectTransform)source, button, identity, context, session, mat));
        }
        internal void Tick(float scale=1) { foreach (var token in _tokens) token.Tick(scale); }
        public void Dispose()
        {
            foreach (var token in _tokens) token.Dispose();
            foreach (var pair in _parents) pair.Key.SetParent(pair.Value, false);
            _tokens.Clear(); UnityEngine.Object.Destroy(Root.gameObject);
        }
    }
    internal sealed class TownServiceStation : IDisposable
    {
        internal Transform Root = Probe.Go("station").transform;
        internal static TownServiceStation? Create(byte service, Vector3 center, float scale) => new();
        internal void Sample(string state, float time) { Probe.Events.Add("sample:" + state); }
        public void Dispose() { Probe.Events.Add("station-dispose"); }
    }
    internal static class TownServicePopulation
    {
        internal static Transform? Frame => null;
        internal static TownServiceStation? Acquire(byte service) => TownServiceStation.Create(service, Vector3.zero, 1f);
    }
    internal static class TownServiceSync
    {
        internal static void Reset() { }
        internal static void Tick(Transform frame, Transform? station) { }
    }
    // Workspace geometry/roster interpolation has its own production-bound Unity suite.
    // Here only the presentation owner's child lifetime and manual-tray ownership matter.
    internal sealed class TownServiceWorkspace : IDisposable
    {
        internal Transform Root { get; }
        internal Transform? FurnitureRoot => Root;
        internal sealed class Prop { }
        internal IReadOnlyList<Prop> Props => Array.Empty<Prop>();
        internal TownServiceWorkspace(Transform station, byte service = 1)
        { Root = Probe.Go("workspace", station).transform; }
        internal void Tick(bool mayRelocate = true) { if (mayRelocate) Root.localPosition += new Vector3(.01f, 0f, 0f); }
        internal float RelocationVisibility => 1f;
        internal bool InputAvailable => true;
        internal ulong RelocationRevision => 0;
        internal void SetVisibility(float value) { }
        public void Dispose() { UnityEngine.Object.Destroy(Root.gameObject); }
    }
    internal sealed class TownServiceTray : IDisposable
    {
        internal bool IsGrabbed;
        internal Transform Root = Probe.Go("tray").transform;
        internal TownServiceTray(TMPro.TMP_Text? text, Vector3 position, Quaternion rotation, float scale) { }
        internal void SetVisibility(float value) { }
        internal void Tick() { }
        internal void LateTick() { }
        public void Dispose() { UnityEngine.Object.Destroy(Root.gameObject); }
    }
}

namespace GloomhavenVR.WorldUI { internal static class MaskClock { internal static float Now; } }

// The handoff itself is exercised by the separate actual-card Unity suite.
namespace GloomhavenVR.WorldUI { internal static class TownServiceEnhancementHandoff { internal static void TickApproach() { } } }
