using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Presents the native fullscreen movie as its own grabbable VR window. The campaign intro
/// uses VideoCamera rather than UIWindow, so converting windows cannot discover it; the map
/// deliberately suppresses the desktop composite. Read the decoder's original texture instead
/// of redirecting playback or copying its completion callback. Native sound, completion and
/// game progression stay owned by VideoCamera. A network mirror can supply a cosmetic decoder.
/// </summary>
internal static class NativeVideoWindow
{
    private static GameObject? _root;
    private static RawImage? _image;
    private static ConvertedPanel? _panel;
    private static GrabbableModal? _grab;
    private static VideoPlayer? _remoteSource;
    private static bool _remotePoseReady;
    private static Vector3 _remotePosition;
    private static Quaternion _remoteRotation;
    private static float _remoteGrabFactor = 1f;
    private static VideoPlayer? _shownSource;
    private static string _shownUrl = string.Empty;
    private static VideoPlayer? _observedNative;
    private static bool _nativePresentationSuppressed;
    private static uint _nativePlaybackGeneration;
    private static readonly FieldInfo? IntroClick = typeof(UIMapFTUEInitialStep).GetField(
        "clickTrackerExtended", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? IntroPath = typeof(UIMapFTUEInitialStep).GetField(
        "introVideoPath", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static UIWindow? Window { get; private set; }
    internal static bool Visible => _root != null && _root.activeSelf;
    internal static uint NativePlaybackGeneration => _nativePlaybackGeneration;

    /// <summary>Never returns the network mirror: only native playback may originate a movie.</summary>
    internal static VideoPlayer? NativePlayer
    {
        get
        {
            VideoCamera? owner = VideoCamera.s_This;
            ObserveNative(owner != null ? owner.m_VideoPlayer : null);
            if (owner == null || owner.m_Camera == null || !owner.m_Camera.isActiveAndEnabled)
                return null;
            VideoPlayer? player = owner.m_VideoPlayer;
            return player != null && player.isActiveAndEnabled && (player.isPlaying || player.isPaused)
                ? player : null;
        }
    }

    private static void ObserveNative(VideoPlayer? player)
    {
        if (_observedNative == player) return;
        if (_observedNative != null) _observedNative.started -= NativeStarted;
        _observedNative = player;
        if (player == null) return;
        player.started += NativeStarted;
        // Discovery may happen after Unity delivered started; this initial identity is still
        // distinct. Subsequent started events detect replay of the same clip between two ticks.
        unchecked { _nativePlaybackGeneration++; }
    }

    private static void NativeStarted(VideoPlayer player)
    {
        if (player == _observedNative) unchecked { _nativePlaybackGeneration++; }
    }

    internal static string PlaybackKey
    {
        get
        {
            VideoPlayer? player = NativePlayer;
            if (player == null || string.IsNullOrEmpty(player.url))
                return string.Empty;
            string url = player.url.Replace('\\', '/');
            int last = url.LastIndexOf('/');
            if (last <= 0) return string.Empty;
            int parent = url.LastIndexOf('/', last - 1);
            string key = url.Substring(parent + 1);
            return IsPlaybackKey(key) ? key : string.Empty;
        }
    }

    // Native callers select only CP_Intro/GH_CP_Intro or Heroes/<ECharacter>. A peer may name
    // those assets, never an arbitrary file, network URL or path traversal. Keep the resolver
    // separate from existence so malformed keys can be exercised without the installed game.
    internal static bool IsPlaybackKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > 128) return false;
        string extension = Application.platform == RuntimePlatform.Switch ? ".mp4" : ".mov";
        if (!key.EndsWith(extension, StringComparison.Ordinal)) return false;
        string stem = key.Substring(0, key.Length - extension.Length);
        if (stem == "CP_Intro/GH_CP_Intro") return true;
        if (!stem.StartsWith("Heroes/", StringComparison.Ordinal) || stem.Length <= 7) return false;
        for (int i = 7; i < stem.Length; i++)
        {
            char c = stem[i];
            if (!(c >= 'A' && c <= 'Z') && !(c >= 'a' && c <= 'z')
                && !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
        }
        return true;
    }

    internal static bool TryResolvePlaybackKey(string key, out string url)
    {
        url = string.Empty;
        if (!IsPlaybackKey(key)) return false;
        string root;
        if (Application.platform == RuntimePlatform.OSXPlayer)
            root = Path.Combine(Application.dataPath, "Resources", "Data", "StreamingAssets", "Movies");
        else
        {
            string folder = Application.platform == RuntimePlatform.Switch ? "Movies_MP4_30"
                : Application.platform == RuntimePlatform.GameCoreXboxOne ? "Movies_MOV_30" : "Movies";
            root = Path.Combine(Application.dataPath, "StreamingAssets", folder);
        }
        string candidate = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(candidate)) return false;
        url = candidate;
        return true;
    }

    internal static bool TryGetGrab(out GrabbableModal? grab)
    {
        grab = Visible ? _grab : null;
        return grab != null;
    }

    internal static void SetRemoteSource(VideoPlayer? player)
    {
        if (_remoteSource != player) _remotePoseReady = false;
        _remoteSource = player;
    }

    internal static void SetNativePresentationSuppressed(bool suppressed) => _nativePresentationSuppressed = suppressed;

    internal static void SetRemotePose(Vector3 position, Quaternion rotation, float grabFactor)
    {
        _remotePosition = position;
        _remoteRotation = rotation;
        _remoteGrabFactor = SharedWindowSizeLaw.SharedGrabFactor(grabFactor);
        _remotePoseReady = true;
    }

    internal static void Tick()
    {
        // The network elects one presentation owner. A peer that also has its own native
        // decoder displays the elected cosmetic mirror while its native callbacks run normally.
        VideoPlayer? source = _remoteSource ?? (_nativePresentationSuppressed ? null : NativePlayer);
        if (!WorldUIConfig.ConversionActive || CanvasConversion.WorldCamera == null
            || source == null || !source.isActiveAndEnabled || !source.isPrepared
            || !(source.isPlaying || source.isPaused) || source.frame < 0 || source.texture == null
            || source.texture.width <= 0 || source.texture.height <= 0
            || (source == _remoteSource && !_remotePoseReady))
        {
            Close();
            return;
        }

        // Texture preparation is the reveal gate: there is never an empty grab bar while the
        // decoder prepares. A new native clip can reuse the same VideoPlayer component.
        if (_root == null || _shownSource != source || _shownUrl != source.url)
        {
            Close();
            Build(source);
        }
        if (_image == null || _grab == null || _panel == null) return;
        if (_image.texture != source.texture) _image.texture = source.texture;
        float aspect = source.texture.width / (float)Mathf.Max(1, source.texture.height);
        Vector2 size = new(1280f, 1280f / aspect);
        if (_panel.HostRect.sizeDelta != size)
        {
            _panel.HostRect.sizeDelta = size;
            _panel.Target.sizeDelta = size;
        }
        _grab.SetExtraScale(SharedWindowSizeLaw.ExtraScale(size, WorldUIConfig.CanvasScaleMm.Value));
        _grab.SyncSharedState(Window);
        _grab.Tick();
    }

    private static void Build(VideoPlayer source)
    {
        var root = new GameObject("GloomhavenVR.NativeVideoWindow", typeof(RectTransform));
        _root = root;
        root.SetActive(false);
        UnityEngine.Object.DontDestroyOnLoad(root);
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = CanvasConversion.WorldCamera;
        var raycaster = root.AddComponent<GraphicRaycaster>();
        root.AddComponent<CanvasGroup>();
        // Identity for existing shared-window chrome only. Disabled before activation: native
        // Start/Show/Hide, UI registry membership and escape callbacks must never run on it.
        Window = root.AddComponent<UIWindow>();
        Window.enabled = false;
        var host = (RectTransform)root.transform;
        host.sizeDelta = new Vector2(1280f, 1280f * source.texture.height / source.texture.width);
        var content = new GameObject("Movie", typeof(RectTransform));
        content.transform.SetParent(root.transform, false);
        var rect = (RectTransform)content.transform;
        rect.sizeDelta = host.sizeDelta;
        _image = content.AddComponent<RawImage>();
        _image.texture = source.texture;
        _image.color = Color.white;
        // Intercept the foreground plane even when the native movie is unskippable; clicking
        // the picture must not click map icons through it. Native escape remains the skip path.
        _image.raycastTarget = true;
        content.AddComponent<NativeVideoClick>();
        _panel = new ConvertedPanel
        {
            Target = rect, HostGo = root, HostRect = host,
            HostCanvas = canvas, HostRaycaster = raycaster,
        };
        VRLayers.Apply(root);
        Camera head = CanvasConversion.WorldCamera!;
        PanelPlacement.Spawn(head, PanelLayout.WorldScale, out Vector3 position, out Quaternion rotation);
        position.y = head.transform.position.y;
        if (source == _remoteSource)
        {
            position = _remotePosition;
            rotation = _remoteRotation;
        }
        root.transform.SetPositionAndRotation(position, rotation);
        root.SetActive(true);
        UguiPokeSurfaces.Register(canvas);
        _grab = new GrabbableModal();
        _grab.Build(_panel, SharedWindowSizeLaw.ExtraScale(host.sizeDelta, WorldUIConfig.CanvasScaleMm.Value),
            "Native video");
        if (source == _remoteSource)
        {
            Transform? frame = ((IPanelGrabOwner)_grab).GrabRoot;
            if (frame != null) frame.localScale = Vector3.one * _remoteGrabFactor;
            _grab.SnapFrameTo(position, rotation);
        }
        _shownSource = source;
        _shownUrl = source.url;
        // HW-VERIFY
        VRLog.Note("WorldUI", "NATIVE VIDEO WINDOW: opened from a prepared video texture; native playback unchanged.");
    }

    private static void Close()
    {
        if (_panel != null) UguiPokeSurfaces.Unregister(_panel.HostCanvas);
        _grab?.Destroy();
        _grab = null;
        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
            // HW-VERIFY
            VRLog.Note("WorldUI", "NATIVE VIDEO WINDOW: closed; playback stopped or has no prepared frame.");
        }
        _root = null;
        Window = null;
        _panel = null;
        _image = null;
        _shownSource = null;
        _shownUrl = string.Empty;
    }

    internal static void Shutdown()
    {
        Close();
        SetRemoteSource(null);
        _nativePresentationSuppressed = false;
        ObserveNative(null);
    }

    internal static bool TrySkipNativeIntro()
    {
        // The original intro's click calls Escape -> its own ClickTracker callback, which stops
        // video AND performs FadeInShow. VideoCamera.Stop alone omits that continuation. Resolve
        // the live owner at click time; never escape a different popup or a cosmetic peer movie.
        VideoPlayer? native = NativePlayer;
        if (!Visible || native == null || native != _shownSource || !native.isPlaying) return false;
        string key = PlaybackKey;
        UIMapFTUEInitialStep? owner = null;
        foreach (UIMapFTUEInitialStep step in UnityEngine.Object.FindObjectsOfType<UIMapFTUEInitialStep>())
        {
            if (!step.isActiveAndEnabled || !step.gameObject.scene.isLoaded) continue;
            if (!(IntroClick?.GetValue(step) is ClickTrackerExtended tracker) || !tracker.isActiveAndEnabled) continue;
            string extension = Application.platform == RuntimePlatform.Switch ? ".mp4" : ".mov";
            if (!(IntroPath?.GetValue(step) is string path) || key != path + extension) continue;
            if (owner != null) return false; // ambiguous ownership cannot choose a continuation
            owner = step;
        }
        if (owner == null) return false;
        return owner.Escape();
    }
}

internal sealed class NativeVideoClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData) => NativeVideoWindow.TrySkipNativeIntro();
}
