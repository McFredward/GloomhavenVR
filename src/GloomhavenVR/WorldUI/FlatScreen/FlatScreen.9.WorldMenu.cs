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
    private Shader? _worldMenuOriginalShader;
    private Shader? _worldMenuBackdropShader;
    private int _worldMenuOriginalQueue;
    private bool _worldMenuBackdropDepth;
    private GameObject? _worldMenuRear;
    private MeshRenderer? _worldMenuRearRenderer;
    private Mesh? _worldMenuRearMesh;

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
        if (_worldMenuHost == null || _quad == null)
            return;
        Transform picture = _quad.transform;
        // Parent the split backing to the same physical frame. Its six-centimetre design
        // gap and angular expansion scale with a deliberate window pinch, never player motion.
        float grow = 1f + BackplaneGapMeters / Mathf.Max(.1f, WorldUIConfig.ScreenDistance.Value);
        float gap = BackplaneGapMeters / _worldMenuUnit;
        if (_backQuad != null)
        {
            Transform back = _backQuad.transform;
            if (back.parent != picture) back.SetParent(picture, false);
            back.localPosition = new Vector3(0f, 0f, gap);
            back.localRotation = Quaternion.identity;
            back.localScale = new Vector3(grow, grow, 1f);
        }
        EnsureWorldMenuRear();
        if (_worldMenuRear != null && _worldMenuRearRenderer != null)
        {
            // Two fixed outward-facing surfaces keep the opaque image behind the UI
            // from either side. Reversed winding retains identical world XY/UVs:
            // rotating a second quad would reverse the picture's native mapping.
            _worldMenuRear.transform.localPosition = _backQuad != null
                ? new Vector3(0f, 0f, -gap) : Vector3.zero;
            _worldMenuRear.transform.localRotation = Quaternion.identity;
            _worldMenuRear.transform.localScale = _backQuad != null
                ? new Vector3(grow, grow, 1f) : Vector3.one;
            _worldMenuRearRenderer.sharedMaterial = _screenMaterial;
            _worldMenuRearRenderer.enabled = _worldMenuBackdropDepth;
        }
    }

    private void EnsureWorldMenuRear()
    {
        if (_worldMenuRear != null || _quad == null)
            return;
        MeshFilter? original = _quad.GetComponent<MeshFilter>();
        if (original == null || original.sharedMesh == null)
            return;
        _worldMenuRearMesh = Object.Instantiate(original.sharedMesh);
        _worldMenuRearMesh.name = "GloomhavenVR.MainMenu.RearBackdrop";
        _worldMenuRearMesh.hideFlags = HideFlags.HideAndDontSave;
        int[] triangles = _worldMenuRearMesh.triangles;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int swap = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = swap;
        }
        _worldMenuRearMesh.triangles = triangles;
        _worldMenuRear = new GameObject("GloomhavenVR.MainMenu.RearBackdrop",
            typeof(MeshFilter), typeof(MeshRenderer));
        _worldMenuRear.transform.SetParent(_quad.transform, false);
        _worldMenuRear.GetComponent<MeshFilter>().sharedMesh = _worldMenuRearMesh;
        _worldMenuRearRenderer = _worldMenuRear.GetComponent<MeshRenderer>();
        _worldMenuRearRenderer.enabled = false;
        VRLayers.Apply(_worldMenuRear);
    }

    private void ReleaseWorldMenu()
    {
        RestoreWorldMenuBackdrop();
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
        if (_worldMenuRear != null) Object.Destroy(_worldMenuRear);
        if (_worldMenuRearMesh != null) Object.Destroy(_worldMenuRearMesh);
        _worldMenuRear = null; _worldMenuRearRenderer = null; _worldMenuRearMesh = null;
        if (_quad != null) _quad.transform.SetParent(null, true);
        _worldMenuGrab?.Destroy();
        _worldMenuGrab = null;
        if (_worldMenuHost != null) Object.Destroy(_worldMenuHost);
        _worldMenuHost = null;
        _worldMenuPanel = null;
    }

    // A world window must occlude the room behind it and let nearer hands occlude it.
    // The boot screen's BlitCopy deliberately ignores depth. At Background1000 it can
    // also tie the native sky; the sky's1001 reset and ordinary room geometry then
    // overpaint its image. Reuse the already shipped unlit Overlay only for the ready
    // world window, after the sky/reset and before ordinary foreground geometry.
    private void TickWorldMenuBackdrop()
    {
        if (_worldMenuHost == null || !MenuWorldContext.IsReady)
        {
            RestoreWorldMenuBackdrop();
            return;
        }
        if (_screenMaterial == null || _worldMenuBackdropDepth)
            return;
        Shader? shader = Cards.PlayTray.OverlayShader();
        if (shader == null)
            return;
        _worldMenuOriginalShader = _screenMaterial.shader;
        _worldMenuOriginalQueue = _screenMaterial.renderQueue;
        _worldMenuBackdropShader = shader;
        _screenMaterial.shader = shader;
        _screenMaterial.SetFloat("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
        _screenMaterial.SetFloat("_ZWrite", 1f);
        _screenMaterial.SetFloat("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
        _screenMaterial.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        _screenMaterial.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        _screenMaterial.SetFloat("_VertexColor", 0f);
        _screenMaterial.SetFloat("_UseAlphaMask", 0f);
        _screenMaterial.SetColor("_Color", Color.white);
        _worldMenuBackdropDepth = true;
        SyncWorldMenuBack();
    }

    private void RestoreWorldMenuBackdrop()
    {
        if (_worldMenuBackdropDepth && _screenMaterial != null && _worldMenuOriginalShader != null)
        {
            _screenMaterial.shader = _worldMenuOriginalShader;
            _screenMaterial.renderQueue = _worldMenuOriginalQueue;
        }
        _worldMenuBackdropDepth = false;
        if (_worldMenuRearRenderer != null) _worldMenuRearRenderer.enabled = false;
        _worldMenuOriginalShader = _worldMenuBackdropShader = null;
    }

    private Renderer? WorldMenuBackdropForHead(Renderer? front)
    {
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (!_worldMenuBackdropDepth || _quad == null || head == null)
            return front;
        Vector3 eye = head.transform.position;
        if (head.stereoEnabled && head.stereoActiveEye != Camera.MonoOrStereoscopicEye.Mono)
        {
            Camera.StereoscopicEye side = head.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right
                ? Camera.StereoscopicEye.Right : Camera.StereoscopicEye.Left;
            eye = head.GetStereoViewMatrix(side).inverse.MultiplyPoint3x4(Vector3.zero);
        }
        return Vector3.Dot(eye - _quad.transform.position, _quad.transform.forward) > 0f
            ? _worldMenuRearRenderer : front;
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
