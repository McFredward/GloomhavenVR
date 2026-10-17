using System;
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
    private static StereoVideoSurface? _stereo;
    private static bool _shownStereo;
    private static VideoPlayer? _reportedStereoSource;
    private static string _reportedStereoUrl = string.Empty;
    private static uint _reportedStereoOpening, _remoteOpening;
    private static string _remoteKey = string.Empty;
    private static uint _nativePlaybackGeneration;
    private static readonly FieldInfo? IntroClick = typeof(UIMapFTUEInitialStep).GetField(
        "clickTrackerExtended", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? IntroPath = typeof(UIMapFTUEInitialStep).GetField(
        "introVideoPath", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? NativeEndReached = typeof(VideoCamera).GetMethod(
        "EndReached", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(VideoPlayer) }, null);

    internal static UIWindow? Window { get; private set; }
    internal static bool Visible => _root != null && _root.activeSelf;
    internal static uint NativePlaybackGeneration => _nativePlaybackGeneration;
    internal static uint OriginatingPlaybackGeneration => NativeVideoReview.Active ? NativeVideoReview.Generation : _nativePlaybackGeneration;
    internal static VideoPlayer? OriginatingPlayer => NativePlayer ?? NativeVideoReview.Source;
    internal static bool HasRemoteSource => _remoteSource != null;

    // The native movie camera is also the decoder's live presentation/continuation lease.
    // Its pixels already go to the discarded sink; disabling it would invalidate NativePlayer.
    internal static bool OwnsRenderCamera(Camera? camera) => camera != null
        && VideoCamera.s_This != null && VideoCamera.s_This.m_Camera == camera;

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

    internal static bool NativePlaybackBusy
    {
        get
        {
            VideoCamera? owner = VideoCamera.s_This;
            return owner != null && owner.m_Camera != null && owner.m_Camera.isActiveAndEnabled
                && owner.m_VideoPlayer != null && owner.m_VideoPlayer.isActiveAndEnabled
                && (!owner.m_VideoPlayer.isPrepared || owner.m_VideoPlayer.isPlaying || owner.m_VideoPlayer.isPaused);
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
            VideoPlayer? player = OriginatingPlayer;
            if (player == NativeVideoReview.Source && player != null) return NativeVideoReview.SelectedKey;
            if (player == null || string.IsNullOrEmpty(player.url))
                return string.Empty;
            return NativeVideoMovies.NativePlaybackKey(player);
        }
    }

    // Native callers select only CP_Intro/GH_CP_Intro or Heroes/<ECharacter>. A peer may name
    // those assets, never an arbitrary file, network URL or path traversal. Keep the resolver
    // separate from existence so malformed keys can be exercised without the installed game.
    internal static bool IsPlaybackKey(string key) => NativeVideoMovies.IsPlaybackKey(key);

    internal static bool TryResolvePlaybackKey(string key, out string url) =>
        NativeVideoMovies.TryResolvePlaybackKey(key, out url);

    internal static bool TryGetGrab(out GrabbableModal? grab)
    {
        grab = Visible ? _grab : null;
        return grab != null;
    }

    // ModBuild 505 registered a real GrabbableModal without telling the ordinary orphan
    // sweep who owned it. The five-second sweep destroyed it; Tick then rebuilt a frame
    // at the world origin and pulled the still-playing movie down into the table. Ownership
    // is the live window's exact handle, never a name exemption for leftover chrome.
    internal static bool OwnsGrab(GrabbableModal grab) => Visible && ReferenceEquals(_grab, grab);

    // This UIWindow is a disabled identity for shared chrome, never a native pooled popup.
    // Exempt only the live movie's real click surface from the native Start/IsOpen guard.
    // A stale handler from a destroyed window must not skip a replacement movie either.
    internal static bool OwnsClick(GameObject? target) => Visible && _image != null
        && ReferenceEquals(_image.gameObject, target);

    internal static void SetRemoteSource(VideoPlayer? player, string selectedKey = "")
    {
        if (_remoteSource != player)
        {
            _remotePoseReady = false;
            unchecked { _remoteOpening++; }
        }
        _remoteSource = player;
        _remoteKey = player != null ? selectedKey : string.Empty;
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
        NativeVideoReview.Tick();
        // The network elects one presentation owner. A peer that also has its own native
        // decoder displays the elected cosmetic mirror while its native callbacks run normally.
        VideoPlayer? source = _remoteSource ?? (_nativePresentationSuppressed ? null : OriginatingPlayer);
        if (!WorldUIConfig.ConversionActive || CanvasConversion.WorldCamera == null
            || source == null || !source.isActiveAndEnabled || !source.isPrepared
            || !(source.isPlaying || source.isPaused) || source.frame < 0 || source.texture == null
            || source.texture.width <= 0 || source.texture.height <= 0
            || (source == _remoteSource && !_remotePoseReady))
        {
            Close();
            return;
        }

        if (NativeVideoMovies.IsStereoUrl(source.url)
            && (source.texture.width != 3840 || source.texture.height != 1080))
        {
            ReportStereoIssue(source, "versioned SBS frame dimensions are not 3840x1080",
                "refusing the invalid picture; native playback continues.");
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
        float aspect = DisplayAspect(source);
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
        root.AddComponent<NativeVideoWindowLifetime>();
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
        _shownStereo = NativeVideoMovies.IsStereoUrl(source.url);
        host.sizeDelta = new Vector2(1280f, 1280f / DisplayAspect(source));
        var content = new GameObject("Movie", typeof(RectTransform));
        content.transform.SetParent(root.transform, false);
        var rect = (RectTransform)content.transform;
        rect.sizeDelta = host.sizeDelta;
        _image = content.AddComponent<RawImage>();
        _image.texture = source.texture;
        _image.color = Color.white;
        if (_shownStereo && !StereoVideoSurface.TryAttach(_image, out _stereo, out string reason))
        {
            // Keep the exact selected frame/audio/clock when the local renderer is unavailable.
            // The left half is the approved original picture, never a stretched packed image.
            _image.uvRect = new Rect(.5f / source.texture.width, .5f / source.texture.height,
                .5f - 1f / source.texture.width, 1f - 1f / source.texture.height);
            ReportStereoIssue(source, reason, "showing the selected file's left eye in 2D.");
        }
        // Intercept the foreground plane even when the native movie is unskippable; clicking
        // the picture must not click map icons through it. Native escape remains the skip path.
        _image.raycastTarget = true;
        content.AddComponent<NativeVideoClick>();
        _panel = new ConvertedPanel
        {
            Target = rect, HostGo = root, HostRect = host,
            HostCanvas = canvas, HostRaycaster = raycaster,
            BaseSortingOrder = ModalFallback.ModalHostSortingOrder,
            ContentGraphic = _image,
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
        // The native movie can span a scene unload. Its ordinary grab holder must survive
        // for exactly as long as the persistent movie canvas, and Close destroys both.
        Transform? grabFrame = ((IPanelGrabOwner)_grab).GrabRoot;
        if (grabFrame != null) UnityEngine.Object.DontDestroyOnLoad(grabFrame.root.gameObject);
        if (source == _remoteSource)
        {
            Transform? frame = ((IPanelGrabOwner)_grab).GrabRoot;
            if (frame != null) frame.localScale = Vector3.one * _remoteGrabFactor;
            _grab.SnapFrameTo(position, rotation);
        }
        CanvasConversion.RegisterStandalonePanelOrder(_panel);
        _shownSource = source;
        _shownUrl = source.url;
        string selectedKey = source == _remoteSource && _remoteKey.Length > 0 ? _remoteKey
            : source == NativeVideoReview.Source ? NativeVideoReview.SelectedKey : NativeVideoMovies.NativePlaybackKey(source);
        string routing = _shownStereo ? (_stereo != null ? "stereo" : "left-only") : "mono";
        // HW-VERIFY
        VRLog.Note("WorldUI", "NATIVE VIDEO WINDOW: opened from a prepared video texture; native playback unchanged. "
            + $"key={selectedKey}, packedDimensions={source.texture.width}x{source.texture.height}, routing={routing}. "
            + "Window owns its persistent grab holder; movie pixels are foreground content for the bar.");
    }

    private static void Close()
    {
        _stereo?.Dispose();
        _stereo = null;
        _shownStereo = false;
        if (_panel != null)
        {
            CanvasConversion.UnregisterStandalonePanelOrder(_panel);
            UguiPokeSurfaces.Unregister(_panel.HostCanvas);
        }
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

    private static float DisplayAspect(VideoPlayer source) => _shownStereo
        ? StereoVideoSurface.PerEyeAspect(source.texture.width, source.texture.height)
        : source.texture.width / (float)Mathf.Max(1, source.texture.height);

    private static void ReportStereoIssue(VideoPlayer source, string reason, string outcome)
    {
        uint opening = source == _remoteSource ? _remoteOpening : NativePlaybackGeneration;
        if (_reportedStereoSource == source && _reportedStereoUrl == source.url && _reportedStereoOpening == opening) return;
        _reportedStereoSource = source;
        _reportedStereoUrl = source.url;
        _reportedStereoOpening = opening;
        VRLog.Warn("WorldUI", $"NATIVE VIDEO STEREO FALLBACK: opening={opening}, "
            + $"key={NativeVideoMovies.KeyFromUrl(source.url)}, reason={reason}; {outcome}");
    }

    internal static void OnRootDestroyed(GameObject root)
    {
        if (ReferenceEquals(root, _root)) Close();
    }

    internal static void CloseReview(VideoPlayer? player)
    {
        if (player != null && _shownSource == player) Close();
    }

    internal static void Shutdown()
    {
        NativeVideoReview.Stop();
        Close();
        SetRemoteSource(null);
        _nativePresentationSuppressed = false;
        ObserveNative(null);
    }

    internal static bool TrySkipNativeMovie()
    {
        if (Visible && _shownSource != null && _shownSource == NativeVideoReview.Source)
        {
            NativeVideoReview.Stop();
            return true;
        }
        // The original intro's click calls Escape -> its own ClickTracker callback, which stops
        // video AND performs FadeInShow. VideoCamera.Stop alone omits that continuation. Resolve
        // the live owner at click time; never escape a different popup or a cosmetic peer movie.
        VideoPlayer? native = NativePlayer;
        if (!Visible || native == null || native != _shownSource || !native.isPlaying) return false;
        string key = NativeVideoMovies.CanonicalKey(PlaybackKey);
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
        if (owner != null)
        {
            VRLog.Note("WorldUI", "NATIVE VIDEO WINDOW: skip delivered to the native campaign intro click.");
            return owner.Escape();
        }

        // Hero unlock movies have no UIMapFTUEInitialStep/ClickTracker owner. The game's
        // completion method stops the exact decoder, restores any disabled objects and
        // invokes its currently installed continuation (including chained unlocks). Calling
        // Stop alone would leave the reward promise/input lock unresolved. This runs only
        // for a real local player showing an allowlisted hero asset, never a peer mirror.
        if (!key.StartsWith("Heroes/", StringComparison.Ordinal) || !IsPlaybackKey(key)
            || NativeEndReached == null || VideoCamera.s_This == null) return false;
        VRLog.Note("WorldUI", "NATIVE VIDEO WINDOW: skip delivered to native hero-video completion.");
        NativeEndReached.Invoke(VideoCamera.s_This, new object[] { native });
        return true;
    }
}

internal sealed class NativeVideoWindowLifetime : MonoBehaviour
{
    private void OnDestroy() => NativeVideoWindow.OnRootDestroyed(gameObject);
}

internal sealed class NativeVideoClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (NativeVideoWindow.OwnsClick(gameObject)) NativeVideoWindow.TrySkipNativeMovie();
    }
}
