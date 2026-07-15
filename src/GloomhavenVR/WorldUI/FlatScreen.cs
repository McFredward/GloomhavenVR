using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Floating 2D screen (ROADMAP P3c #6): a world-space quad showing the UICamera's
/// output for everything that is not physicalized — main menu, guildmaster map,
/// merchant, level-up, and any unconverted window.
///
/// Rendering: while the screen is visible the "UICamera"-tagged camera is retargeted
/// onto a RenderTexture shown on the quad (UUVR screen-mirror pattern; UI-ARCH §2.1
/// consequence (a)). The camera reference is re-resolved after every scene load —
/// this coordinates with the game's own re-wiring, verified via ilspycmd
/// (GH.Runtime.dll, CanvasManager): <c>private void OnSceneLoaded(Scene scene,
/// LoadSceneMode mode)</c> re-binds <c>persistentUICanvas.worldCamera</c> /
/// <c>tooltipCanvas.worldCamera</c> to the first camera tagged "UICamera" (IL 70 B,
/// PATCH-TARGETS §1.7 ✅) — Screen-Space-Camera canvases follow their worldCamera
/// into our RenderTexture automatically. <c>targetTexture</c> is restored on hide,
/// shutdown and camera change.
///
/// Pointer: the primary hand ray (Phase-2 <see cref="RayInteractor"/> pick pose) is
/// intersected with the screen plane in code (no physics collider — keeps clear of
/// Phase-3a's selection ray masks), hit UV × RT resolution → the P2
/// <see cref="VirtualMouse"/> bridge (<c>WarpTo</c>; trigger press/release →
/// <c>Press</c>/<c>Release</c> so drags work). Everything downstream (InControl
/// module, ClickTracker, IsPointerOverUI, tooltips) works untouched.
///
/// Show policy: auto-appears in <see cref="VRMode.Menu2D"/> (config), hides in
/// scenario modes; in <see cref="VRMode.ModalUI"/> it appears when no converted
/// dialog owns the modal (fallback for unconverted windows).
/// </summary>
internal sealed class FlatScreen
{
    private const float ScreenAspect = 16f / 9f;

    private GameObject? _quad;
    private Renderer? _quadRenderer;
    private Transform? _reticle;
    private RenderTexture? _rt;
    private Camera? _uiCamera;
    private bool _visible;
    private bool _pressing;

    public void Tick()
    {
        bool want = WantVisible();
        if (want && !_visible)
            Show();
        else if (!want && _visible)
            Hide();

        if (!_visible)
            return;

        // Camera may die/change with scene loads.
        if (_uiCamera == null)
        {
            ResolveUiCamera();
            if (_uiCamera == null)
                return;
            _uiCamera.targetTexture = _rt;
        }

        FollowHead();
        TickPointer();
    }

    public void Shutdown() => Hide();

    // ---- policy ------------------------------------------------------------------------

    private bool WantVisible()
    {
        if (!WorldUIConfig.FlatScreen.Value || !WorldUIConfig.ConversionActive)
            return false;

        VRMode mode = VRModeStateMachine.CurrentMode;
        if (mode == VRMode.Menu2D)
            return WorldUIConfig.FlatScreenAutoShow.Value;

        // Fallback for unconverted modal windows (merchant, level-up, ESC menu...):
        // the confirmation-box surface owns plain dialogs; everything else 2D.
        if (mode == VRMode.ModalUI)
            return !WorldUIConfig.Dialogs.Value || !IsConfirmationBoxOpen();

        return false;
    }

    private static bool IsConfirmationBoxOpen() =>
        Singleton<UIConfirmationBoxManager>.IsInitialized
        && Singleton<UIConfirmationBoxManager>.Instance.IsOpen;

    // ---- lifecycle ----------------------------------------------------------------------

    private void Show()
    {
        ResolveUiCamera();

        if (_rt == null)
        {
            _rt = new RenderTexture(Mathf.Max(Screen.width, 1280), Mathf.Max(Screen.height, 720), 24)
            {
                name = "GloomhavenVR.FlatScreenRT",
                antiAliasing = 1,
            };
            _rt.Create();
        }

        if (_quad == null)
        {
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "GloomhavenVR.FlatScreen";
            Object.Destroy(_quad.GetComponent<Collider>());
            _quadRenderer = _quad.GetComponent<Renderer>();
            Shader? shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default");
            _quadRenderer.sharedMaterial = new Material(shader) { mainTexture = _rt };

            GameObject reticleGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticleGo.name = "Reticle";
            Object.Destroy(reticleGo.GetComponent<Collider>());
            reticleGo.transform.SetParent(_quad.transform, worldPositionStays: false);
            reticleGo.GetComponent<Renderer>().sharedMaterial =
                WorldUIAssets.CreateFlatMaterial(new Color(1f, 0.9f, 0.3f, 0.9f));
            _reticle = reticleGo.transform;
            _reticle.gameObject.SetActive(false);
        }

        _quad.SetActive(true);
        if (_uiCamera != null)
            _uiCamera.targetTexture = _rt;

        PlaceScreen(instant: true);
        _visible = true;
        VRLog.Info("WorldUI", "FlatScreen shown (UICamera → RenderTexture).");
    }

    private void Hide()
    {
        if (_pressing)
        {
            VirtualMouse.Release();
            _pressing = false;
        }
        if (_uiCamera != null && _uiCamera.targetTexture == _rt)
            _uiCamera.targetTexture = null;
        _uiCamera = null;

        if (_quad != null)
            _quad.SetActive(false);
        if (_rt != null)
        {
            _rt.Release();
            Object.Destroy(_rt);
            _rt = null;
            if (_quad != null)
            {
                Object.Destroy(_quad);
                _quad = null;
                _quadRenderer = null;
                _reticle = null;
            }
        }
        if (_visible)
            VRLog.Info("WorldUI", "FlatScreen hidden — UICamera restored to the backbuffer.");
        _visible = false;
    }

    private void ResolveUiCamera()
    {
        // Prefer the scenario UIManager's serialized camera; otherwise scan by tag
        // (only on show/scene change — Camera.allCameras allocates).
        UIManager manager = UIManager.Instance;
        if (manager != null && manager.UICamera != null)
        {
            _uiCamera = manager.UICamera;
            return;
        }
        Camera[] all = Camera.allCameras;
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i].CompareTag("UICamera"))
            {
                _uiCamera = all[i];
                return;
            }
        }
        _uiCamera = null;
    }

    // ---- placement -----------------------------------------------------------------------

    private void PlaceScreen(bool instant)
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f)
            fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 target = h.position + fwd * (1.6f * scale);
        // Quad primitive faces -Z (visible from -forward side): +Z away from viewer.
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        float width = WorldUIConfig.FlatScreenWidth.Value * scale;
        Vector3 size = new(width, width / ScreenAspect, 1f);

        Transform t = _quad.transform;
        if (instant)
        {
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            t.position = Vector3.Lerp(t.position, target, Time.deltaTime * 3f);
            t.rotation = Quaternion.Slerp(t.rotation, rot, Time.deltaTime * 3f);
        }
        t.localScale = size;
    }

    private void FollowHead()
    {
        if (_quad == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        // Lazy follow: re-center only when the screen drifts far out of view.
        Vector3 toScreen = _quad.transform.position - head.transform.position;
        float angle = Vector3.Angle(head.transform.forward, toScreen);
        if (angle > 45f)
            PlaceScreen(instant: false);
    }

    // ---- pointer ---------------------------------------------------------------------------

    private void TickPointer()
    {
        if (_quad == null || _rt == null)
            return;

        VRHand? hand = VRHands.Primary;
        IPickProvider? pick = VRHands.PrimaryPick;
        if (pick == null || hand == null || !pick.TryGetPick(out PickPose pose))
        {
            HideReticle();
            return;
        }

        // Ray ∩ screen plane (quad faces -Z; plane normal = -forward toward viewer).
        Transform t = _quad.transform;
        Vector3 normal = -t.forward;
        float denom = Vector3.Dot(pose.Direction, normal);
        if (Mathf.Abs(denom) < 1e-4f)
        {
            HideReticle();
            return;
        }
        float dist = Vector3.Dot(t.position - pose.Origin, normal) / denom;
        if (dist < 0f)
        {
            HideReticle();
            return;
        }

        Vector3 hit = pose.Origin + pose.Direction * dist;
        Vector3 local = t.InverseTransformPoint(hit); // quad local: x/y in [-0.5, 0.5]
        if (Mathf.Abs(local.x) > 0.5f || Mathf.Abs(local.y) > 0.5f)
        {
            HideReticle();
            return;
        }

        // UV → virtual mouse pixels.
        var pixel = new Vector2((local.x + 0.5f) * _rt.width, (local.y + 0.5f) * _rt.height);
        VirtualMouse.WarpTo(pixel);

        if (_reticle != null)
        {
            if (!_reticle.gameObject.activeSelf)
                _reticle.gameObject.SetActive(true);
            _reticle.localPosition = new Vector3(local.x, local.y, -0.005f);
            _reticle.localScale = new Vector3(0.008f, 0.008f * ScreenAspect, 0.008f);
        }

        // Trigger = left mouse button (press/release so drags work).
        if (hand.TriggerDown && !_pressing)
        {
            _pressing = true;
            VirtualMouse.Press();
        }
        else if (_pressing && hand.TriggerUp)
        {
            _pressing = false;
            VirtualMouse.Release();
        }
    }

    private void HideReticle()
    {
        if (_reticle != null && _reticle.gameObject.activeSelf)
            _reticle.gameObject.SetActive(false);
        if (_pressing)
        {
            _pressing = false;
            VirtualMouse.Release();
        }
    }
}
