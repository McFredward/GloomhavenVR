using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Phase banner (round/turn/death announcements) as a brief HMD-anchored toast.
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>[RequireComponent(typeof(UIWindow))]
/// public class PhaseBannerHandler : Singleton&lt;PhaseBannerHandler&gt;</c> holding
/// <c>private UIWindow window;</c> (publicized) whose <c>public VisibilityEvent
/// onShown / onHidden</c> UnityEvents fire around the banner animation
/// (<c>UnityEngine.UI.UIWindow</c>, verified members <c>Show()/Hide()/IsOpen</c>).
///
/// The 2D banner blocks ALL input with a fullscreen <c>interactionBlock</c> Image
/// while it plays (UI-ARCH §9.7). After re-parenting, that image only covers the
/// toast — so while the banner is up a module-wide soft lock disables every other
/// converted surface's raycaster (and the physical buttons honor the same lock),
/// preserving the game's "no clicks during banner" guarantee.
/// </summary>
internal sealed class PhaseBannerSurface
{
    private PhaseBannerHandler? _attached;
    private ConvertedPanel? _panel;
    private bool _pendingShow;

    public string Name => "PhaseBanner";

    public void Tick()
    {
        if (_panel != null && !_panel.IsAlive)
        {
            _panel = null;
            CanvasConversion.SetSoftLock(this, false);
        }

        PhaseBannerHandler? handler =
            Singleton<PhaseBannerHandler>.IsInitialized ? Singleton<PhaseBannerHandler>.Instance : null;
        if (!ReferenceEquals(handler, _attached))
        {
            Detach();
            _attached = handler;
            if (_attached != null)
            {
                UIWindow window = _attached.GetComponent<UIWindow>();
                if (window != null)
                {
                    window.onShown.AddListener(OnShown);
                    window.onHidden.AddListener(OnHidden);
                }
            }
        }

        if (_pendingShow)
        {
            _pendingShow = false;
            ConvertNow();
        }

        // Soft-follow the head while the toast is up.
        if (_panel != null)
            PlaceToast(smooth: true);
    }

    private void OnShown()
    {
        if (!WorldUIConfig.PhaseBanner.Value || !WorldUIConfig.ConversionActive)
            return;
        _pendingShow = true;
    }

    private void ConvertNow()
    {
        if (_panel != null || _attached == null)
            return;
        _panel = CanvasConversion.Convert(_attached.transform as RectTransform, Name, pokeable: false);
        if (_panel == null)
            return;
        CanvasConversion.SetSoftLock(this, true);
        PlaceToast(smooth: false);
    }

    private void OnHidden()
    {
        _pendingShow = false;
        CanvasConversion.SetSoftLock(this, false);
        if (_panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }
    }

    private void PlaceToast(bool smooth)
    {
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null || _panel == null || _panel.HostGo == null)
            return;

        float scale = PanelLayout.WorldScale;
        Transform h = head.transform;
        Vector3 fwd = h.forward;
        Vector3 target = h.position + fwd * (1.3f * scale) + Vector3.up * (0.05f * scale);
        Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);

        // The banner rect is fullscreen-sized in pixels — scale it down so the toast
        // reads ~65 cm wide instead of a wall.
        if (smooth)
        {
            Transform t = _panel.HostGo.transform;
            t.position = Vector3.Lerp(t.position, target, Time.deltaTime * 5f);
            t.rotation = Quaternion.Slerp(t.rotation, rot, Time.deltaTime * 5f);
            float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
            t.localScale = Vector3.one * (metersPerPixel * scale * 0.35f);
        }
        else
        {
            CanvasConversion.PlaceHost(_panel, target, rot, scale * 0.35f);
        }
    }

    private void Detach()
    {
        if (_attached != null)
        {
            UIWindow window = _attached.GetComponent<UIWindow>();
            if (window != null)
            {
                window.onShown.RemoveListener(OnShown);
                window.onHidden.RemoveListener(OnHidden);
            }
            _attached = null;
        }
        OnHidden();
    }

    public void Shutdown() => Detach();
}
