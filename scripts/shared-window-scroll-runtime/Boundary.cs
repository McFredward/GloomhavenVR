using System;
using System.Collections.Generic;
using GloomhavenVR.WorldUI;
using UnityEngine;
using UnityEngine.UI;

// Explicit native session/window registry ports. Actual Unity ScrollRect,
// Scrollbar, graphics, masks, event camera, raycasters and input callbacks run.
public class UIWindow : MonoBehaviour { public bool IsOpen = true, IsVisible = true; }
public static class FFSNetwork { public static bool IsOnline = true; }
public static class Singleton<T> where T : class
{ public static T Instance = null!; public static bool IsInitialized => Instance != null; }
public class UICharacterStoryBox : MonoBehaviour { public List<string>? dialogs; public int currentDialogIndex; }
public sealed class StoryController { public UICharacterStoryBox? DialogBox; }
public sealed class MapStoryController { public UICharacterStoryBox? dialogBox; public UIWindow? window; }
public sealed class UIEventPanel { public EventData? eventData; public Screen? currentScreen; public class EventData { public string ID = "event"; } public class Screen { public string Name = "page"; } }
public class UIQuestPopup : MonoBehaviour { public Quest? quest; public class Quest { public string LocalisedNameKey = "quest"; } }
namespace FFSNet { internal static class PlayerRegistry { internal static int HostPlayerID = 1; } }
namespace GloomhavenVR.Core
{
    internal static class PerfConfig { internal static bool Quiet => true; }
    internal static class VRLog
    {
        internal static readonly List<string> Notes = new(), Warnings = new();
        internal static bool WantsDebug => false;
        internal static void Warn(string scope, string text) => Warnings.Add(text);
        internal static void Info(string scope, string text) { }
        internal static void Note(string scope, string text) => Notes.Add(text);
        internal static void Debug(string scope, string text) { }
    }
    internal static class BundleShaders
    { internal static Shader? Resolve(string name, string scope, string found, string missing) => Shader.Find(name); }
}
namespace GloomhavenVR.Cards
{
    internal static class CardFaceMipBake
    {
        internal static Sprite OriginalFor(Sprite sprite) => sprite;
        internal static Sprite? PresentationFor(Sprite? sprite) => sprite;
    }
}
namespace GloomhavenVR.WorldUI
{ internal static class PanelMipBake { internal static Texture OriginalFor(Texture texture) => texture; } }
namespace GloomhavenVR.Net.TownServices
{
    internal sealed class TownCardReturnOrigin { }
    internal sealed class TownOfferedHover { }
    internal sealed class TownRackState { }
    internal sealed class TownRackStamp { }
    internal sealed class TownCatalogBank { }
}
// Tracking and flight-focus notifications are explicit ports. The original
// RayUguiDriver stick response and its complete UguiPointer delivery execute.
namespace GloomhavenVR.Hands
{
    internal sealed class VRHand
    {
        internal Vector2 Thumbstick;
        internal Interact.RayUguiDriver RayUgui = null!;
        internal FixturePoke Poke = new();
        internal Interact.RayInteractor? Ray;
    }
    internal sealed class FixturePoke { internal GameObject? HoveredUi; }
    internal static class VRHands { internal static VRHand? Left, Right; internal static VRHand? Get(int side) => null; }
}
namespace GloomhavenVR.Hands.Interact
{
    // Non-Results initiative-depth selection and display-remapping patches are
    // boundary ports. All fixture points use the original in-camera projection;
    // actual nested census, GraphicRaycaster filters and winner selection run.
    internal sealed class RayInteractor { internal void SetWorldUiHit(Vector3 point, string source) { } }
    internal sealed class PokeOnlyTarget : MonoBehaviour { }
    internal interface IDepthPortraitPicker
    { bool TryPickPortrait(Vector3 origin, Vector3 direction, out GameObject portrait, out Vector3 hit); }
    internal static class DepthPortraitPicks
    { internal static bool TryGet(Canvas canvas, out IDepthPortraitPicker picker) { picker = null!; return false; } }
    internal static class UguiRaycastCoordinates
    {
        internal readonly struct Scope : IDisposable { public void Dispose() { } }
        internal static Scope Enter(GraphicRaycaster caster, UnityEngine.EventSystems.PointerEventData data, Canvas? host) => new();
    }
    internal static class UiScrollFocus
    {
        internal static int Hovers, Delivered;
        internal static bool CanScroll(ScrollRect scroll) => scroll.content != null && scroll.viewport != null
            && scroll.content.rect.height > scroll.viewport.rect.height;
        internal static void NoteScrollHover(GloomhavenVR.Hands.VRHand hand, UnityEngine.Object? target, string source) => Hovers++;
        internal static void NoteScrollDelivered(GloomhavenVR.Hands.VRHand hand, UnityEngine.Object? target, string source) => Delivered++;
    }
}
namespace GloomhavenVR.WorldUI.MapRoom
{ internal static class MapCityEventReadyUp { internal static bool Active; internal static uint ContentKey; } }
// Converted committed hit rects and unrelated raised-model geometry are explicit
// ports. Original Results surface admission and visible-UI filtering execute.
namespace GloomhavenVR.WorldUI
{
    internal static class VRKeyboard { internal static void NoticeClick(GameObject? target) { } }
    internal sealed class ConvertedPanel
    {
        internal Canvas HostCanvas = null!;
        internal RectTransform HostRect = null!;
        internal Transform Target = null!;
        internal readonly List<Transform> ExtraRenderRoots = new();
    }
    internal static partial class CanvasConversion
    {
        internal static readonly Dictionary<Canvas, Rect> HitRects = new();
        internal static bool TryGetHitRect(Canvas canvas, out Rect hit) => HitRects.TryGetValue(canvas, out hit);
    }
}
namespace GloomhavenVR.WorldUI.MenuDepth
{
    internal struct MenuDepthRayHit
    { internal float Distance; internal Vector3 WorldPoint, NativePoint; internal Transform NativeRoot; }
    internal static class MenuDepthSources
    {
        internal static bool TryRaycast(Canvas canvas, Vector3 origin, Vector3 direction, float budget, out MenuDepthRayHit hit)
        { hit = default; return false; }
        internal static bool TryNativeRaycast(Canvas host, Canvas native, Vector3 origin, Vector3 direction, float budget, out MenuDepthRayHit hit)
        { hit = default; return false; }
    }
}
namespace GloomhavenVR.WorldUI
{
    internal static partial class CanvasConversion
    { internal static bool BaseSortingOrderOf(GameObject go, out int order) { order = 0; return false; } }
    internal static partial class ModalFallback
    {
        internal static bool ShouldSwallowMenuTabClick(GameObject target) => false;
        internal sealed class FixturePanel
        {
            internal bool IsAlive = true;
            internal GameObject? HostGo;
            internal Transform HostTransform = null!;
            internal Transform? Target;
        }
        internal sealed class WindowPanel
        {
            internal bool IsResults = true;
            internal FixturePanel Panel = new();
            internal UIWindow? Window;
            internal ScrollRect? ResultsScroll;
            internal Scrollbar? ResultsScrollbar;
            internal bool ResultsScrollLogged, ResultsScrollSearchWarned;
            internal float NextResultsScrollSearch;
        }
        private static readonly List<WindowPanel> Converted = new();
        internal static void FixtureConfigure(WindowPanel panel) { Converted.Clear(); Converted.Add(panel); }
        internal static void FixtureTick() => TickResultsStickScroll();
    }
    internal enum SharedWindowKind : byte { None, ScenarioStory, MapStory, QuestConfirm, Encounter, Video, RewardShowcase, Results }
    internal static class SharedWindows
    {
        internal static UIWindow?[] Native = new UIWindow?[8];
        internal static bool MapActive = true;
        internal static Transform? Header;
        internal static UIWindow? WindowOf(SharedWindowKind kind) => Native[(byte)kind];
        internal static SharedWindowKind KindOf(UIWindow window)
        { for (byte i = 1; i <= 7; i++) if (Native[i] == window) return (SharedWindowKind)i; return 0; }
        internal static bool ParticipatesHere(SharedWindowKind kind) => FFSNetwork.IsOnline && kind != 0
            && ((kind != SharedWindowKind.MapStory && kind != SharedWindowKind.QuestConfirm && kind != SharedWindowKind.Encounter) || MapActive);
        internal static bool MapActiveFor(int id) => Program.Peers[id].Map;
    }
    internal static class ScenarioResultsNative
    {
        internal readonly struct Parts
        {
            internal readonly UIWindow Window;
            internal readonly Transform? Header;
            internal Parts(UIWindow window, Transform? header) { Window = window; Header = header; }
        }
        internal static bool TryRead(out Parts parts)
        {
            UIWindow? window = SharedWindows.WindowOf(SharedWindowKind.Results);
            parts = window == null ? default : new Parts(window, SharedWindows.Header);
            return window != null;
        }
    }
}
namespace GloomhavenVR.WorldUI.Surfaces
{ internal static class InitiativePortraitClickSound { internal static void NoticeClick(GameObject target) { } } }
namespace GloomhavenVR.Net
{
    // Only original source discovery/bootstrap and the transport state header are
    // ports here. Build/Prune/RootPose and binding application execute unchanged.
    internal sealed class ScenarioResultsState
    { internal Vector2 FrameSize; internal ScenarioResultsPart[] Parts = Array.Empty<ScenarioResultsPart>(); }
    internal sealed class ScenarioResultsPart
    {
        internal ushort Id, ParentId;
        internal uint ParentBinding, Structure;
        internal string Address = string.Empty;
        internal TownServices.TownServiceNode[] Nodes = Array.Empty<TownServices.TownServiceNode>();
    }
    internal sealed partial class FixtureResultsBuilder : IDisposable
    {
        private sealed class Copy
        { internal ScenarioResultsPart Part = null!; internal TownServices.TownServiceBinding Binding = null!; }
        private readonly Dictionary<ushort, Copy> _copies = new();
        private readonly HashSet<Transform> _cuts = new();
        private readonly TownServices.TownServiceAssets _assets = new();
        private readonly Dictionary<string, Transform> _originals = new();
        private GameObject? _quarantine, _picture;
        private void EnsureSources() { }
        private void ReadSources() { }
        private void ClearSources() { }
        private Transform? Original(string address) => _originals.TryGetValue(address, out var original) ? original : null;
        private void RetireCopies()
        { foreach (Copy copy in _copies.Values) copy.Binding.Dispose(); _copies.Clear(); }
        internal GameObject Mount = null!;
        internal Transform Construct(Transform root, Transform[] modules, ConvertedPanel panel)
        {
            _originals.Add("root", root);
            var parts = new List<ScenarioResultsPart>();
            using (var source = new TownServices.TownServiceBinding(root, child => Array.IndexOf(modules, child) >= 0))
            {
                parts.Add(new ScenarioResultsPart { Id = 1, Address = "root", Structure = source.Structure,
                    Nodes = (TownServices.TownServiceNode[])source.Read(_assets, true).Clone() });
                for (int i = 0; i < modules.Length; i++)
                {
                    string address = "module" + i; _originals.Add(address, modules[i]);
                    using var module = new TownServices.TownServiceBinding(modules[i]);
                    parts.Add(new ScenarioResultsPart { Id = (ushort)(i + 2), Address = address, ParentId = 1,
                        ParentBinding = source.Bindings[Array.IndexOf(source.Nodes, modules[i].parent)],
                        Structure = module.Structure, Nodes = (TownServices.TownServiceNode[])module.Read(_assets, true).Clone() });
                }
            }
            Build(new ScenarioResultsState { FrameSize = panel.HostRect.rect.size, Parts = parts.ToArray() }, panel);
            Mount = _picture!;
            return _copies[1].Binding.Root;
        }
        public void Dispose() => RetireCopies();
    }
    internal static class NetSession { internal static bool FlatNetMode; }
    internal static class SharedWindowSpawnAuthority { internal static int AuthorId = 1; }
    internal static class VersionGuard
    {
        internal static ushort PeerBuild(int id) => Program.Unmodded.Contains(id) ? (ushort)0 : NetProtocol.ModBuild;
        internal static void CollectContinuationPeers(List<int> peers, int local)
        { peers.Clear(); foreach (int id in Program.Peers.Keys) if (id != local && !Program.Unmodded.Contains(id)) peers.Add(id); }
    }
    internal static class RemoteStorySync
    {
        internal static uint HashDialog(List<string>? pages) => pages == null ? 0 : NetProtocol.HashMapKey(string.Join("/", pages));
    }
    internal interface INetTransport { int LocalPlayerId { get; } bool IsOnline { get; } }
}
