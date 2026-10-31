using System;
using System.Collections.Generic;
using UnityEngine.Video;

// Explicit Unity object/property seams only. The production lease and full
// consuming-owner predicate run unchanged; this fixture cannot prove GPU output.
namespace UnityEngine
{
    internal class Object
    {
        public bool Destroyed;
        public static bool operator ==(Object? left, Object? right) => ReferenceEquals(left, right)
            || (ReferenceEquals(left, null) || left.Destroyed) && (ReferenceEquals(right, null) || right.Destroyed);
        public static bool operator !=(Object? left, Object? right) => !(left == right);
        public override bool Equals(object? value) => ReferenceEquals(this, value);
        public override int GetHashCode() => base.GetHashCode();
    }
    internal sealed class Camera : Object
    { internal bool isActiveAndEnabled = true; internal string name = "owned camera"; internal RenderTexture? targetTexture; }
    internal sealed class RenderTexture : Object
    { internal string name = "owned capture"; internal bool Created = true; internal bool IsCreated() => Created; }
    internal sealed class Material : Object { }
    internal sealed class GameObject { internal bool activeInHierarchy = true; }
    internal sealed class Renderer : Object
    { internal bool enabled = true; internal readonly GameObject gameObject = new(); internal Material? sharedMaterial; }
    internal static class Time { internal static int frameCount; }
    internal static class Debug { internal static readonly List<string> Logs = new(); internal static void Log(string value) => Logs.Add(value); }
}
namespace UnityEngine.Video
{
    internal sealed class VideoPlayer : UnityEngine.Object
    {
        internal UnityEngine.Camera targetCamera = null!;
        internal string url = "Ambient/owned.sbs-v1.mov";
        internal float targetCameraAlpha = .73f;
        internal bool isActiveAndEnabled = true;
        internal event Action<VideoPlayer, string>? errorReceived;
        internal void Fail() => errorReceived?.Invoke(this, "explicit decoder error seam");
        internal int Subscribers => errorReceived?.GetInvocationList().Length ?? 0;
    }
}
namespace GloomhavenVR.Core
{
    internal static class QuestStandalonePlatform { internal static bool Enabled = true; }
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog { internal static bool WantsDebug = true; }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static UnityEngine.Camera? HeadCamera; } }
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static bool ConversionActive = true; }
    internal static class NativeVideoMovies
    {
        // Source admission/parsing is independently tested by native-video. This
        // supplies the currently selected exact native camera-plane source only.
        internal static VideoPlayer? CameraPlaneSource;
        internal static string NativePlaybackKey(VideoPlayer player) => player.url;
    }
    internal sealed partial class FlatScreen
    {
        internal static FlatScreen? _coverageOwner;
        internal bool _visible = true, _splitRouting = true;
        internal readonly UnityEngine.RenderTexture _rt = new(), _uiRt = new();
        internal UnityEngine.Material? _screenMaterial = new();
        internal UnityEngine.Renderer? _backRenderer = new();
        internal readonly Stereo _stereo = new();
        internal sealed class CapturedCamera { internal FlatScreen Owner = null!; internal bool IsUi; }
        internal static readonly Dictionary<UnityEngine.Camera, CapturedCamera> CapturedSet = new();
        private UnityEngine.RenderTexture TargetFor(CapturedCamera record) => record.IsUi ? _uiRt : _rt;
        // Exact desktop startup lifecycle is covered by stereo-movie-render.
        internal static bool OwnsStartupMovie(VideoPlayer player) => false;
        internal sealed class Stereo
        {
            internal VideoPlayer? Player;
            internal UnityEngine.Material? Material;
            internal bool Active = true, Created = true;
            internal bool OwnsStartupMovie(VideoPlayer player, UnityEngine.Material material) =>
                Active && Created && ReferenceEquals(player, Player) && ReferenceEquals(material, Material);
        }
    }
}
