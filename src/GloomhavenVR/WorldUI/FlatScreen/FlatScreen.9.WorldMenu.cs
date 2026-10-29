using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.WorldUI.Surfaces;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal sealed partial class FlatScreen
{
    private GameObject? _worldMenuHost;
    private SurfaceGrabBar? _worldMenuGrab;
    private ConvertedPanel? _worldMenuPanel;
    private float _worldMenuUnit;
    private uint _worldMenuOpening;
    private uint _savedMenuOpening;
    private bool _savedMenuPose;
    private Vector3 _savedMenuPosition;
    private Quaternion _savedMenuRotation;
    private float _savedMenuScale;
    private float _savedMenuUnit;

    /// <summary>
    /// Keep the original captured menu and its pointer mapping, but mount the screen on the
    /// existing world-window grab frame. No native widget is cloned or reimplemented. In
    /// particular moving the rig must never re-place this screen or overwrite a manual resize.
    /// </summary>
    private bool TickWorldMenu()
    {
        if (!MenuWorldContext.IsReady)
        {
            ReleaseWorldMenu();
            return false;
        }
        if (_quad == null)
            return true;
        if (_worldMenuHost != null && _worldMenuOpening != MenuWorldContext.Opening)
            ReleaseWorldMenu();
        if (_worldMenuHost == null)
        {
            // The boot screen can predate tracking and the real menu. Seat the world window
            // exactly once at arrival, then the common grab frame owns its pose and scale.
            PlaceScreen(instant: true);
            _worldMenuOpening = MenuWorldContext.Opening;
            float width = _quad.transform.TransformVector(Vector3.right).magnitude;
            _worldMenuUnit = width / 1920f;
            if (_savedMenuPose && _savedMenuOpening == _worldMenuOpening)
                _worldMenuUnit = _savedMenuUnit;
            _worldMenuHost = new GameObject("GloomhavenVR.MainMenuWindow", typeof(RectTransform));
            Object.DontDestroyOnLoad(_worldMenuHost);
            var rect = (RectTransform)_worldMenuHost.transform;
            rect.sizeDelta = new Vector2(1920f, 1920f / ScreenAspect);
            rect.SetPositionAndRotation(_quad.transform.position, _quad.transform.rotation);
            rect.localScale = Vector3.one * _worldMenuUnit;
            Transform picture = _quad.transform;
            picture.SetParent(rect, false);
            picture.localPosition = Vector3.zero;
            picture.localRotation = Quaternion.identity;
            picture.localScale = new Vector3(rect.rect.width, rect.rect.height, 1f);
            _worldMenuPanel = new ConvertedPanel
            {
                Target = rect, HostGo = _worldMenuHost, HostRect = rect,
            };
            _worldMenuGrab = new SurfaceGrabBar();
            _worldMenuGrab.Build(_worldMenuPanel,
                _worldMenuUnit / (WorldUIConfig.CanvasScaleMm.Value * .001f), 1f, "Main menu");
            if (_savedMenuPose && _savedMenuOpening == _worldMenuOpening)
            {
                Transform? frame = ((IPanelGrabOwner)_worldMenuGrab).GrabRoot;
                if (frame != null)
                {
                    frame.SetPositionAndRotation(_savedMenuPosition, _savedMenuRotation);
                    frame.localScale = Vector3.one * _savedMenuScale;
                }
            }
            VRLayers.Apply(_worldMenuHost);
            VRLog.Info("WorldUI", "MENU WORLD WINDOW: original menu capture mounted on the standard "
                + "movable/scalable window handle; locomotion leaves its world pose and size fixed.");
        }
        _worldMenuGrab?.Tick();
        SyncWorldMenuBack();
        return true;
    }

    private void SyncWorldMenuBack()
    {
        if (_worldMenuHost == null || _quad == null || _backQuad == null)
            return;
        Transform picture = _quad.transform, back = _backQuad.transform;
        // Parent the split backing to the same physical frame. Its six-centimetre design
        // gap and angular expansion scale with a deliberate window pinch, never player motion.
        if (back.parent != picture)
            back.SetParent(picture, false);
        float grow = 1f + BackplaneGapMeters / Mathf.Max(.1f, WorldUIConfig.ScreenDistance.Value);
        back.localPosition = new Vector3(0f, 0f, BackplaneGapMeters / _worldMenuUnit);
        back.localRotation = Quaternion.identity;
        back.localScale = new Vector3(grow, grow, 1f);
    }

    private void ReleaseWorldMenu()
    {
        if (_worldMenuHost == null && _worldMenuGrab == null)
            return;
        Transform? frame = _worldMenuGrab != null ? ((IPanelGrabOwner)_worldMenuGrab).GrabRoot : null;
        if (frame != null)
        {
            _savedMenuPose = true;
            _savedMenuOpening = _worldMenuOpening;
            _savedMenuPosition = frame.position;
            _savedMenuRotation = frame.rotation;
            _savedMenuScale = frame.localScale.x;
            _savedMenuUnit = _worldMenuHost != null
                ? _worldMenuHost.transform.lossyScale.x / frame.lossyScale.x : _worldMenuUnit;
        }
        // Hide/recovery retains ownership of its own quads. Detach before destroying only
        // our wrapper; a movie or options round-trip can restore this same opening's pose.
        if (_backQuad != null) _backQuad.transform.SetParent(null, true);
        if (_quad != null) _quad.transform.SetParent(null, true);
        _worldMenuGrab?.Destroy();
        _worldMenuGrab = null;
        if (_worldMenuHost != null) Object.Destroy(_worldMenuHost);
        _worldMenuHost = null;
        _worldMenuPanel = null;
    }

    // Confirmation ownership is proved from the producer's actual captured native camera.
    // These originals remain in their native screen canvas; the mod-owned world wrapper
    // is a presentation owner, so ancestor searches alone cannot discover that relation.
    internal static bool TryGetMenuOwner(Transform source, out ConvertedPanel? panel)
    {
        panel = null;
        FlatScreen? owner = _coverageOwner;
        if (owner == null || !owner._visible || !MenuWorldContext.IsReady
            || owner._worldMenuPanel == null || owner._worldMenuHost == null
            || !owner._worldMenuHost.activeInHierarchy)
            return false;
        Canvas? canvas = source.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.rootCanvas == null
            || canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceCamera)
            return false;
        Camera? camera = canvas.worldCamera != null ? canvas.worldCamera : canvas.rootCanvas.worldCamera;
        if (camera == null || !CapturedSet.TryGetValue(camera, out CapturedCamera capture)
            || !capture.IsUi || !((owner._uiRt != null && camera.targetTexture == owner._uiRt)
                || (owner._rt != null && camera.targetTexture == owner._rt)))
            return false;
        panel = owner._worldMenuPanel;
        return true;
    }

    internal static bool IsMenuOwner(ConvertedPanel panel) => _coverageOwner != null
        && _coverageOwner._visible && MenuWorldContext.IsReady
        && ReferenceEquals(_coverageOwner._worldMenuPanel, panel)
        && _coverageOwner._worldMenuHost != null && _coverageOwner._worldMenuHost.activeInHierarchy;
}
