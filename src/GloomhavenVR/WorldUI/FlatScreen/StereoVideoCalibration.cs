using System;
using System.IO;
using GloomhavenVR.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Opt-in calibration before converting actual films. The maintainer accepted the trailer
/// agent's staged plan on 2026-10-17: establish eye routing first, then judge a conversion pilot
/// in the headset. This owns an independent, silent decoder and a fixed menu-only canvas.
/// Never redirect a native player or impersonate a native/shared movie: its completion, skip,
/// audio and network clocks remain untouched. The normal launch does not load the shader or
/// look for calibration media. No inference or conversion is performed by the runtime.
/// </summary>
internal static class StereoVideoCalibration
{
    private static bool _checkedArguments;
    private static bool _requested;
    private static bool _attempted;
    private static bool _stereo;
    private static GameObject? _root;
    private static VideoPlayer? _player;
    private static Canvas? _canvas;
    private static RawImage? _image;
    private static TextMeshProUGUI? _caption;
    private static StereoVideoSurface? _surface;

    internal static void Tick()
    {
        if (!_checkedArguments)
        {
            _checkedArguments = true;
            _requested = StereoVideoCalibrationPolicy.Requested(Environment.GetCommandLineArgs());
        }
        if (!_requested || (_attempted && _root == null)) return;
        try
        {
            // The main menu is identified by its actual native scene, not the persistent
            // Gloomhaven_unified scene. Fail closed if session or scene ownership is unknown.
            string scene = SceneController.Instance != null ? SceneController.Instance.GetCurrentSceneName : string.Empty;
            bool allowed = StereoVideoCalibrationPolicy.Allowed(VRSession.IsRunning,
                WorldUIConfig.ConversionActive, scene, FFSNetwork.IsOnline);
            if (!allowed || CanvasConversion.WorldCamera == null)
            {
                if (_root != null) Stop();
                return;
            }
            if (!_attempted) Start();
            if (_player == null || !_player.isPrepared || _player.texture == null || _player.frame < 0) return;
            if (!StereoVideoCalibrationPolicy.ValidDimensions(_player.texture.width, _player.texture.height, _stereo))
            {
                Refuse("decoded dimensions do not match the explicit calibration layout");
                return;
            }
            if (_image == null) BuildPicture(CanvasConversion.WorldCamera);
            if (_image != null) _image.texture = _player.texture;
            if (_caption != null) _caption.text = Loc.StereoVideoCalibrationClose;
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
            Refuse("calibration movie missing: " + path);
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
            + (_stereo ? "full-SBS-left-first" : "mono-left") + ". Click the prepared picture to close; leaving the offline menu also closes it.");
    }

    private static void Prepared(VideoPlayer player)
    {
        if (player == _player) player.Play();
    }

    private static void Failed(VideoPlayer player, string reason)
    {
        if (player == _player) Refuse("decoder error: " + reason);
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
        VRLog.Note("WorldUI", "STEREO VIDEO CALIBRATION: prepared picture is 1.2m wide at 1.6m; "
            + "eye labels and frame codes require headset review. This is not converted game footage.");
    }

    private static void Refuse(string reason)
    {
        _attempted = true;
        VRLog.Alert("WorldUI", "STEREO VIDEO CALIBRATION: " + reason);
        Stop();
    }

    internal static bool OwnsClick(GameObject target) => _image != null && ReferenceEquals(_image.gameObject, target);

    internal static void Stop()
    {
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
        _image = null;
        _caption = null;
    }
}

internal sealed class StereoVideoCalibrationClick : MonoBehaviour, IPointerClickHandler
{
    public void OnPointerClick(PointerEventData eventData)
    {
        if (StereoVideoCalibration.OwnsClick(gameObject)) StereoVideoCalibration.Stop();
    }
}
