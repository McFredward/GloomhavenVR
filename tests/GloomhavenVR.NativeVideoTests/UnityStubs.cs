using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Instances = new();
        public static T[] FindObjectsOfType<T>() => Instances.OfType<T>().ToArray();
        public bool Destroyed;
        public static void Destroy(Object o)
        {
            o.Destroyed = true;
            if (o is GameObject root) foreach (Component component in root.Components) component.Destroyed = true;
        }
        private static bool Null(Object? value) => ReferenceEquals(value, null) || value.Destroyed;
        public static bool operator ==(Object? a, Object? b) => Null(a) && Null(b) || ReferenceEquals(a, b);
        public static bool operator !=(Object? a, Object? b) => !(a == b);
        public override bool Equals(object? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
        public static void DontDestroyOnLoad(Object o) { ((GameObject)o).Persistent = true; }
    }
    public class MonoBehaviour : Component { }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public bool enabled = true;
        public bool isActiveAndEnabled => enabled && gameObject.activeSelf;
    }
    public class Transform : Component
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 localScale;
        private Transform? _parent;
        public Transform? parent => _parent;
        public Transform root => _parent?.root ?? this;
        public Transform(GameObject go) { gameObject = go; }
        public void SetParent(Transform parent, bool preserve) { _parent = parent; }
        public void SetPositionAndRotation(Vector3 p, Quaternion q) { position = p; rotation = q; }
    }
    public sealed class RectTransform : Transform
    {
        public RectTransform(GameObject go) : base(go) { }
        public Vector2 sizeDelta;
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public readonly Transform transform;
        public string name;
        public bool activeSelf = true;
        public bool Persistent;
        public Scene scene => new();
        public GameObject(string name, params Type[] types)
        {
            this.name = name;
            transform = Array.IndexOf(types, typeof(RectTransform)) >= 0 ? new RectTransform(this) : new Transform(this);
        }
        public T AddComponent<T>() where T : Component, new()
        {
            var result = new T { gameObject = this };
            Components.Add(result);
            Object.Instances.Add(result);
            return result;
        }
        public T? GetComponentInParent<T>(bool includeInactive) where T : Component =>
            Components.OfType<T>().FirstOrDefault() ?? transform.parent?.gameObject.GetComponentInParent<T>(includeInactive);
        public void SetActive(bool active) { activeSelf = active; }
    }
    public struct Scene { public bool isLoaded => true; }
    public sealed class Camera : Component { public float depth; }
    public sealed class Canvas : Component { public RenderMode renderMode; public Camera? worldCamera; }
    public sealed class CanvasGroup : Component { }
    public enum RenderMode { WorldSpace }
    public enum RuntimePlatform { WindowsPlayer, OSXPlayer, Switch, GameCoreXboxOne }
    public static class Application { public static RuntimePlatform platform; public static string dataPath = "/game"; }
    public sealed class Texture { public int width = 1920; public int height = 1080; }
    public readonly record struct Vector2(float x, float y);
    public readonly record struct Rect(float x, float y, float width, float height);
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 one => new() { x = 1, y = 1, z = 1 };
        public static Vector3 operator *(Vector3 a, float b) => new() { x = a.x * b, y = a.y * b, z = a.z * b };
    }
    public struct Quaternion { }
    public struct Color { public static Color white => new(); }
    public static class Time { public static float unscaledTime; }
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
}
namespace UnityEngine.UI
{
    public sealed class UIWindow : UnityEngine.Component { public bool HasGoneToStartingState; public bool IsOpen; }
    public sealed class GraphicRaycaster : UnityEngine.Component { }
    public sealed class RawImage : UnityEngine.Component
    {
        public UnityEngine.Texture texture = null!;
        public UnityEngine.Color color;
        public bool raycastTarget;
        public UnityEngine.Rect uvRect = new(0, 0, 1, 1);
    }
}
namespace UnityEngine.Video
{
    public enum VideoSource { VideoClip, Url }
    public enum VideoRenderMode { APIOnly, CameraNearPlane, CameraFarPlane }
    public enum VideoAudioOutputMode { Direct }
    public sealed class VideoClip { public string name = string.Empty; }
    public sealed class VideoPlayer : UnityEngine.Component
    {
        public VideoSource source;
        public VideoClip? clip;
        public UnityEngine.Camera targetCamera = null!;
        public float targetCameraAlpha = 1;
        public VideoRenderMode renderMode;
        public VideoAudioOutputMode audioOutputMode;
        public bool playOnAwake, isLooping;
        public ushort audioTrackCount;
        public event Action<VideoPlayer>? prepareCompleted, loopPointReached;
        public event Action<VideoPlayer, string>? errorReceived;
        public int StopCalls, PrepareCalls;
        public void Prepare() { PrepareCalls++; }
        public void Prepared() { isPrepared = true; prepareCompleted?.Invoke(this); }
        public Action CapturePrepared() { var queued = prepareCompleted; return () => { isPrepared = true; queued?.Invoke(this); }; }
        public void Ended() { isPlaying = false; loopPointReached?.Invoke(this); }
        public void Failed() => errorReceived?.Invoke(this, "injected failure");
        public void Stop() { StopCalls++; isPlaying = isPaused = false; }
        public void SetDirectAudioVolume(ushort track, float volume) { }

        public event Action<VideoPlayer>? started;
        public void StartPlayback() => started?.Invoke(this);
        public bool isPlaying, isPaused, isPrepared;
        public long frame = -1;
        public UnityEngine.Texture texture = null!;
        public string url { get; set; } = string.Empty;
        public string PlayedUrl = string.Empty;
        public int PlayCalls;
        public void Play() { PlayCalls++; PlayedUrl = url; isPlaying = true; started?.Invoke(this); }
    }
}
namespace UnityEngine.EventSystems
{
    public sealed class PointerEventData { }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData data); }
}
public sealed class UIMapFTUEInitialStep : UnityEngine.Component
{
    private string introVideoPath = "CP_Intro/GH_CP_Intro";
    private ClickTrackerExtended clickTrackerExtended = null!;
    public void SetTracker(ClickTrackerExtended tracker, string path) { clickTrackerExtended = tracker; introVideoPath = path; }
    public string Diagnostic => introVideoPath + clickTrackerExtended.enabled;
    public int Escapes;
    public bool Escape() { Escapes++; return true; }
}
public sealed class ClickTrackerExtended : UnityEngine.Component { }
public sealed class VideoCamera : UnityEngine.Component
{
    public static VideoCamera? s_This;
    public UnityEngine.Camera m_Camera = null!;
    public UnityEngine.Video.VideoPlayer m_VideoPlayer = null!;
    public int Completions;
    public bool RewardInputLocked;
    public Action? Completed;
    public string OriginalUrl = string.Empty;
    public int NativeSetupCalls;
    // Controlled native-shaped caller: execute the actual Harmony patch, not the helper
    // in isolation. This setter is followed by native setup and exactly one initial Play.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public bool PlayFullscreenVideo(string name, Action onCompletedCallback, bool reenableDisabledObjects)
    {
        Completed = onCompletedCallback;
        m_VideoPlayer.url = OriginalUrl;
        NativeSetupCalls++;
        RewardInputLocked = reenableDisabledObjects;
        m_VideoPlayer.Play();
        return true;
    }
    public void Stop() { m_VideoPlayer.isPlaying = false; }
    private void EndReached(UnityEngine.Video.VideoPlayer player)
    {
        if (!ReferenceEquals(player, m_VideoPlayer)) throw new Exception("wrong native decoder");
        Stop();
        RewardInputLocked = false;
        Completions++;
        Completed?.Invoke();
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled { get; set; } = false; internal static string ResourceDirectory { get; set; } = string.Empty; }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLayers { internal static void Apply(UnityEngine.GameObject go) { } }
    internal static class VRLog { internal static readonly List<string> Notes = new();
        internal static void Note(string a, string b) { Notes.Add(b); }
        internal static readonly List<string> Warnings = new();
        internal static void Warn(string a, string b) { Warnings.Add(b); } }
}
namespace GloomhavenVR.Hands.Interact
{
    internal interface IPanelGrabOwner { UnityEngine.Transform? GrabRoot { get; } }
    internal static class UguiPokeSurfaces
    {
        internal static readonly HashSet<UnityEngine.Canvas> Canvases = new();
        internal static void Register(UnityEngine.Canvas c) => Canvases.Add(c);
        internal static void Unregister(UnityEngine.Canvas c) => Canvases.Remove(c);
    }
}
namespace GloomhavenVR.WorldUI
{
    internal sealed class StereoVideoSurface : IDisposable
    {
        internal static bool Available = true;
        internal static int Attaches, Disposes;
        internal static bool IsAvailable(out string reason) { reason = Available ? string.Empty : "injected unsupported renderer"; return Available; }
        internal static bool TryAttach(UnityEngine.UI.RawImage image, out StereoVideoSurface? surface, out string reason)
        {
            surface = null;
            if (!IsAvailable(out reason)) return false;
            Attaches++; surface = new StereoVideoSurface(); return true;
        }
        internal static float PerEyeAspect(int width, int height) => width / (2f * height);
        public void Dispose() { Disposes++; }
    }
    internal sealed class ConfigValue { internal float Value = 1; }
    internal static class WorldUIConfig { internal static bool ConversionActive = true; internal static ConfigValue CanvasScaleMm = new(); }
    // The automatic movie GPU fixture executes the real screen readiness owner.
    // This native-flow fixture supplies only that explicit acceptance result.
    internal static class FlatScreen
    {
        internal static bool StartupSurfaceReady = true;
        internal static int StartupPrepareCalls;
        internal static int QuestPrepareCalls;
        internal static void PrepareQuestMovie(UnityEngine.Video.VideoPlayer player) { QuestPrepareCalls++; }
        internal static bool TryPrepareStartupMovie(UnityEngine.Video.VideoPlayer player)
        { StartupPrepareCalls++; return StartupSurfaceReady; }
    }
    internal static class CanvasConversion
    {
        internal static UnityEngine.Camera? WorldCamera;
        internal static readonly HashSet<ConvertedPanel> Ordered = new();
        internal static void RegisterStandalonePanelOrder(ConvertedPanel panel) => Ordered.Add(panel);
        internal static void UnregisterStandalonePanelOrder(ConvertedPanel panel) => Ordered.Remove(panel);
        internal static void SetPanelRenderVisible(ConvertedPanel panel, bool visible)
        { panel.RenderHidden = !visible; panel.HostCanvas.enabled = visible; panel.HostRaycaster.enabled = visible; }
    }
    internal static class PanelLayout { internal static float WorldScale => 1; }
    // Scene geometry is a stable fixture input; real capture/domain behavior executes in Unity.
    internal static class SharedWindowSize { internal static float ReferenceWorldScale => 1; }
    internal static class PanelPlacement
    {
        internal static void Spawn(UnityEngine.Camera head, float scale, out UnityEngine.Vector3 p, out UnityEngine.Quaternion q)
        { p = head.transform.position; p.z += 1.3f * scale; q = head.transform.rotation; }
    }
    internal sealed class ConvertedPanel
    {
        internal bool IsAlive => HostGo != null;
        public UnityEngine.RectTransform Target = null!, HostRect = null!;
        public UnityEngine.GameObject HostGo = null!;
        public UnityEngine.Canvas HostCanvas = null!;
        public UnityEngine.UI.GraphicRaycaster HostRaycaster = null!;
        public UnityEngine.UI.RawImage? ContentGraphic = null;
        public int BaseSortingOrder;
        public bool RenderHidden, RevealPending, SharedSpawnPoseReady;
        public SharedWindowKind SharedDesignKind;
    }
    internal sealed class GrabbableModal : GloomhavenVR.Hands.Interact.IPanelGrabOwner
    {
        public UnityEngine.Transform? GrabRoot { get; } = new UnityEngine.GameObject("Frame").transform;
        internal static readonly List<GrabbableModal> LiveHolders = new();
        internal static int LiveCount => LiveHolders.Count;
        internal string LogName = "Menu";
        internal bool Destroyed;
        private ConvertedPanel? _panel;
        internal bool GrabVisible => _panel != null && !_panel.RenderHidden;
        internal float SharedReferenceWorldScale { get; private set; }
        internal void Build(ConvertedPanel p, float s, string n)
        {
            LogName = n;
            _panel = p;
            SharedReferenceWorldScale = SharedWindowSize.ReferenceWorldScale;
            GrabRoot!.SetPositionAndRotation(p.HostGo.transform.position, new UnityEngine.Quaternion());
            LiveHolders.Add(this);
        }
        internal void Destroy() { Destroyed = true; LiveHolders.Remove(this); }
        internal void Tick() { }
        internal void SetExtraScale(float s) { }
        internal void SyncSharedState(UnityEngine.UI.UIWindow? w) { }
        internal void SnapFrameTo(UnityEngine.Vector3 p, UnityEngine.Quaternion q) { GrabRoot?.SetPositionAndRotation(p, q); }
    }
    internal static partial class ModalFallback
    {
        internal const int ModalHostSortingOrder = 1000;
        internal sealed class WindowPanel { internal GrabbableModal? Grab; }
        internal static readonly List<WindowPanel> Converted = new();
        // Other presentation owners are absent in movie fixtures. The separate town
        // ownership suite exercises their real live/retired combinations.
        private static ConvertedPanel? _errorPanel = null;
        private static GrabbableModal? _errorGrab = null;
        private static int _chromeLive, _chromeOrphansSinceCensus;
        internal static int Orphans => _chromeOrphansSinceCensus;
        internal static int LastSweepCount => _chromeLive;
        internal static void SweepForTest() => SweepOrphanChrome();
    }
    internal static class TownServicePresentation
    {
        internal static bool OwnsGrab(GrabbableModal holder) => false;
    }
    internal static class SharedWindowSizeLaw
    {
        internal static float SharedGrabFactor(float factor) => factor;
        internal static float ExtraScale(UnityEngine.Vector2 size, float mm) => 1 / mm;
    }
    internal enum SharedWindowKind { None, ScenarioStory, MapStory, QuestConfirm, Encounter, Video, RewardShowcase }
    internal static class SharedWindows
    {
        internal static bool Online;
        internal static bool ParticipatesHere(SharedWindowKind kind) => Online && kind != SharedWindowKind.None;
    }
    internal static class SharedWindowSpawnBridge
    {
        internal delegate bool InitialPoseReader(SharedWindowKind kind, out UnityEngine.Vector3 position,
            out UnityEngine.Quaternion rotation, out float factor);
        internal static Func<bool>? InitialAuthor;
        internal static InitialPoseReader? TryInitialPose;
        internal static bool IsInitialAuthor => InitialAuthor?.Invoke() == true;
    }
}

namespace GloomhavenVR.WorldUI.MapRoom
{
    internal static class MapRetirementPrompt
    {
        internal static GloomhavenVR.WorldUI.GrabbableModal? OwnedGrab;
        internal static bool OwnsGrab(GloomhavenVR.WorldUI.GrabbableModal holder) =>
            ReferenceEquals(holder, OwnedGrab);
    }
}

public static class AudioController { public static float GetGlobalVolume() => .8f; }
public sealed class BackgroundView
{
    public UnityEngine.Video.VideoPlayer Player = null!;
    public string OriginalUrl = string.Empty;
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Update() { Player.url = OriginalUrl; Player.Play(); }
}
public sealed class IntroPlayer
{
    private readonly UnityEngine.Video.VideoPlayer _player;
    public int TimerStarts;
    public IntroPlayer(UnityEngine.Video.VideoPlayer player) { _player = player; }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void ShowVideo() { TimerStarts++; _player.Play(); }
    public void Show() => ShowVideo();
}
public sealed class PromotionVideoHandler { public void PlayVideo() { } }
namespace GloomhavenVR.Core.Events
{
    internal readonly struct SceneLoadedEvent { }
    internal static class VREvents
    {
        internal static event Action<SceneLoadedEvent>? SceneLoaded;
        internal static void LoadScene() => SceneLoaded?.Invoke(default);
        internal static int SubscriberCount => SceneLoaded?.GetInvocationList().Length ?? 0;
    }
    internal static class VRModeStateMachine { internal static bool ScenarioBoardExists = true; }
}

namespace GloomhavenVR.Net
{
    internal struct RigPose { public UnityEngine.Vector3 Position { get; set; } public UnityEngine.Quaternion Rotation { get; set; } }
    internal struct SharedWindowEntry { public uint ContentKey; public byte Flags, PoseStamp, SizeCode, Frame; public RigPose Pose; }
    internal static class RemoteVideoPlayback
    {
        internal static bool CanCloseReview;
        internal static int CloseCalls;
        internal static bool RequestCloseReview(out string selected)
        {
            CloseCalls++; selected = "Review/Heroes/Summoner.sbs-v1.mov";
            if (CanCloseReview) GloomhavenVR.WorldUI.NativeVideoWindow.SetRemoteSource(null);
            return CanCloseReview;
        }
    }
    internal static class NetProtocol { internal const byte ExtIdVideoWindow = 72, SharedPoseBit = 4, SharedFrameMax = 1, StorySizeMinCode = 1, StorySizeMaxCode = 255; }
    internal static class AvatarSerializer
    {
        internal static void WriteU32(byte[] buffer, ref int i, uint value) => throw new NotSupportedException();
        internal static uint ReadU32(byte[] buffer, ref int i) => throw new NotSupportedException();
        internal static void WritePoseShared(byte[] buffer, ref int i, in RigPose pose) => throw new NotSupportedException();
        internal static void ReadPoseShared(byte[] buffer, ref int i, out RigPose pose) => throw new NotSupportedException();
    }
}
