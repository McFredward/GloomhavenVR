using System;
using System.IO;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Opt-in calibration before converting actual films. The maintainer accepted the trailer
/// agent's staged plan on 2026-10-17: establish eye routing first, then judge a conversion pilot
/// in the headset. The subsequent Build683 logs confirm the argv was recognized but the
/// separate movie was missing. The maintainer now requests an Advanced-menu button instead:
/// each deliberate press can start a fresh attempt after closing or repairing an installation.
/// This owns an independent, silent decoder and a fixed menu-only canvas.
/// Never redirect a native player or impersonate a native/shared movie: its completion, skip,
/// audio and network clocks remain untouched. The normal launch does not load the shader or
/// look for calibration media. No inference or conversion is performed by the runtime.
/// </summary>
internal static class StereoVideoCalibration
{
    private const float FadeSeconds = 0.15f;
    private static bool _requested;
    private static bool _attempted;
    private static bool _stereo;
    private enum State { Idle, Preparing, Playing, Closed, Unavailable, MissingMovie, Failed }
    private static State _state;
    private static GameObject? _root;
    private static VideoPlayer? _player;
    private static Canvas? _canvas;
    private static CanvasGroup? _group;
    private static bool _closing;
    private static float _phaseAt;
    private static float _fadeFrom;
    private static RawImage? _image;
    private static TextMeshProUGUI? _caption;
    private static StereoVideoSurface? _surface;

    internal static string Status => _state switch
    {
        State.Preparing => Loc.StereoVideoCalibrationPreparing,
        State.Playing => _stereo ? Loc.StereoVideoCalibrationPlaying : Loc.StereoVideoCalibrationMono,
        State.Closed => Loc.StereoVideoCalibrationClosed,
        State.Unavailable => Loc.StereoVideoCalibrationUnavailable,
        State.MissingMovie => Loc.StereoVideoCalibrationMissingMovie,
        State.Failed => Loc.StereoVideoCalibrationFailed,
        _ => Loc.StereoVideoCalibrationReady,
    };

    internal static void RequestStart()
    {
        // A second press during preparation/playback must not create another decoder or
        // move an existing picture. Only a completed/failed attempt can be deliberately retried.
        if (_requested) return;
        try
        {
            if (!Allowed() || CanvasConversion.WorldCamera == null)
            {
                _state = State.Unavailable;
                VRLog.Alert("WorldUI", "STEREO VIDEO CALIBRATION: menu request refused; requires VR in the offline main menu.");
                return;
            }
            _attempted = false;
            _requested = true;
            _state = State.Preparing;
        }
        catch (Exception ex)
        {
            Refuse("menu request stopped after " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool Allowed()
    {
        string scene = SceneController.Instance != null ? SceneController.Instance.GetCurrentSceneName : string.Empty;
        return StereoVideoCalibrationPolicy.Allowed(VRSession.IsRunning,
            WorldUIConfig.ConversionActive, scene, FFSNetwork.IsOnline);
    }

    internal static void Tick()
    {
        if (!_requested) return;
        // Unity scene teardown can destroy the root before the next world-UI tick. Its
        // managed surface still owns the camera callback/material and must be disposed.
        if (_attempted && (_root == null || _player == null))
        {
            Stop();
            return;
        }
        try
        {
            // The main menu is identified by its actual native scene, not the persistent
            // Gloomhaven_unified scene. Fail closed if session or scene ownership is unknown.
            if (!Allowed() || CanvasConversion.WorldCamera == null)
            {
                Stop();
                _state = State.Unavailable;
                return;
            }
            if (!_attempted) Start();
            // A prepared picture owns its close action even if the decoder later
            // loses its texture/frame. Never trap the menu behind a pending fade.
            if (_closing) { TickFade(); return; }
            if (_player == null || !_player.isPrepared || _player.texture == null || _player.frame < 0) return;
            if (!StereoVideoCalibrationPolicy.ValidDimensions(_player.texture.width, _player.texture.height, _stereo))
            {
                Refuse("decoded dimensions do not match the explicit calibration layout");
                return;
            }
            if (_image == null) BuildPicture(CanvasConversion.WorldCamera);
            if (_image != null) _image.texture = _player.texture;
            if (_caption != null) _caption.text = Loc.StereoVideoCalibrationClose;
            TickFade();
        }
        catch (Exception ex)
        {
            Refuse("calibration stopped after " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void Start()
    {
        _attempted = true;
        _stereo = StereoVideoSurface.IsAvailable(out string reason);
        string directory = Path.GetDirectoryName(typeof(StereoVideoCalibration).Assembly.Location)!;
        string path = StereoVideoCalibrationPolicy.MoviePath(directory, _stereo);
        if (!File.Exists(path))
        {
            Refuse("calibration movie missing: " + path, State.MissingMovie);
            return;
        }
        if (!_stereo) VRLog.Alert("WorldUI", "STEREO VIDEO CALIBRATION: using the mono companion; " + reason);
        _root = new GameObject("GloomhavenVR.StereoVideoCalibration");
        _root.SetActive(false);
        _player = _root.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.isLooping = true;
        _player.renderMode = VideoRenderMode.APIOnly;
        _player.audioOutputMode = VideoAudioOutputMode.None;
        _player.url = path;
        _player.errorReceived += Failed;
        _player.prepareCompleted += Prepared;
        _root.SetActive(true);
        _player.Prepare();
        VRLog.Note("WorldUI", "STEREO VIDEO CALIBRATION: preparing one silent decoder; layout="
            + (_stereo ? "full-SBS-left-first" : "mono-center") + ". Click the prepared picture to close; leaving the offline menu also closes it.");
    }

    private static void Prepared(VideoPlayer player)
    {
        if (_player != null && player != null && ReferenceEquals(player, _player)) player.Play();
    }

    private static void Failed(VideoPlayer player, string reason)
    {
        if (_player != null && player != null && ReferenceEquals(player, _player)) Refuse("decoder error: " + reason);
    }

    private static void BuildPicture(Camera head)
    {
        if (_root == null || _player == null || _player.texture == null) return;
        var host = new GameObject("Picture", typeof(RectTransform));
        host.transform.SetParent(_root.transform, false);
        _canvas = host.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.WorldSpace;
        _canvas.worldCamera = head;
        _canvas.sortingOrder = ModalFallback.ModalHostSortingOrder;
        _group = host.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _phaseAt = Time.unscaledTime;
        host.AddComponent<GraphicRaycaster>();
        var rect = (RectTransform)host.transform;
        float aspect = _stereo ? StereoVideoSurface.PerEyeAspect(_player.texture.width, _player.texture.height)
            : _player.texture.width / (float)_player.texture.height;
        rect.sizeDelta = new Vector2(1280f, 1280f / aspect);
        _image = host.AddComponent<RawImage>();
        _image.texture = _player.texture;
        _image.color = Color.white;
        _image.raycastTarget = true;
        if (_stereo && !StereoVideoSurface.TryAttach(_image, out _surface, out string reason))
        {
            Refuse("stereo surface unavailable after preflight: " + reason);
            return;
        }
        host.AddComponent<StereoVideoCalibrationClick>();
        var label = new GameObject("CloseHint", typeof(RectTransform));
        label.transform.SetParent(host.transform, false);
        var labelRect = (RectTransform)label.transform;
        labelRect.sizeDelta = new Vector2(1280f, 72f);
        labelRect.anchoredPosition = new Vector2(0f, -rect.sizeDelta.y * 0.5f - 40f);
        _caption = label.AddComponent<TextMeshProUGUI>();
        _caption.fontSize = 28;
        _caption.alignment = TextAlignmentOptions.Center;
        _caption.raycastTarget = false;
        _caption.text = Loc.StereoVideoCalibrationClose;
        // Fixed, yaw-only at the first prepared frame. No head following or periodic recall.
        Vector3 forward = head.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();
        float scale = PanelLayout.WorldScale;
        host.transform.SetPositionAndRotation(head.transform.position + forward * (1.6f * scale),
            Quaternion.LookRotation(forward, Vector3.up));
        host.transform.localScale = Vector3.one * (1.2f * scale / 1280f);
        VRLayers.Apply(host);
        UguiPokeSurfaces.Register(_canvas);
        _state = State.Playing;
        VRLog.Note("WorldUI", "STEREO VIDEO CALIBRATION: prepared picture is 1.2m wide at 1.6m; "
            + "eye labels and frame codes require headset review. This is not converted game footage.");
    }

    private static void Refuse(string reason, State state = State.Failed)
    {
        _attempted = true;
        VRLog.Alert("WorldUI", "STEREO VIDEO CALIBRATION: " + reason);
        Stop();
        _state = state;
    }

    internal static bool OwnsClick(GameObject target) => _image != null && ReferenceEquals(_image.gameObject, target);

    private static void TickFade()
    {
        if (_group == null) return;
        float progress = Mathf.Clamp01((Time.unscaledTime - _phaseAt) / FadeSeconds);
        _group.alpha = _closing ? _fadeFrom * (1f - progress) : progress;
        if (_closing && progress >= 1f) Stop();
    }

    internal static void RequestClose()
    {
        if (_group == null || _closing) return;
        _closing = true;
        _fadeFrom = _group.alpha;
        _phaseAt = Time.unscaledTime;
    }

    internal static void Stop()
    {
        if (_requested || _attempted) _state = State.Closed;
        _requested = false;
        _attempted = false;
        _surface?.Dispose();
        _surface = null;
        if (_canvas != null) UguiPokeSurfaces.Unregister(_canvas);
        if (_player != null)
        {
            _player.prepareCompleted -= Prepared;
            _player.errorReceived -= Failed;
            _player.Stop();
        }
        if (_root != null)
        {
            _root.SetActive(false);
            UnityEngine.Object.Destroy(_root);
            VRLog.Note("WorldUI", "STEREO VIDEO CALIBRATION: closed; decoder and eye surface released.");
        }
        _root = null;
        _player = null;
        _canvas = null;
        _group = null;
        _closing = false;
        _image = null;
        _caption = null;
    }
}

internal sealed class StereoVideoCalibrationClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (StereoVideoCalibration.OwnsClick(gameObject)) StereoVideoCalibration.RequestClose();
    }
}
