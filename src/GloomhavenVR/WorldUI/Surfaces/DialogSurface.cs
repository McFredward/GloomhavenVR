using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Confirmation dialogs as world-space modals in front of the HMD, poke yes/no
/// (ARCHITECTURE §7). Uses the manager's own show/hide notifications — verified via
/// ilspycmd (GH.Runtime.dll, <c>UIConfirmationBoxManager : Singleton&lt;UIConfirmationBoxManager&gt;</c>):
/// <code>
///   public event Action BoxWindowShown;
///   public event Action BoxWindowHidden;
///   private ConfirmationBox CurrentBox { get; }   // pc vs gamepad box (publicized)
///   public void ShowGenericConfirmation(string title, string explanation, UnityAction
///       onActionConfirmed, UnityAction onActionCancelled = null, ...)
/// </code>
/// The dialog opens while the game holds its modal lock (ModalUI mode) — the host
/// canvas raycaster mirrors the lock via <see cref="CanvasConversion"/>, EXCEPT that
/// the dialog itself must stay clickable inside the modal. The game locks the UI
/// through the raycasters it serializes but leaves the confirmation box usable; our
/// mirror does the same by exempting this host (<see cref="ConvertedPanel"/> raycaster
/// re-enabled after the lock sweep).
/// </summary>
internal sealed class DialogSurface
{
    private UIConfirmationBoxManager? _attached;
    private ConvertedPanel? _panel;
    private bool _pendingShow;

    public string Name => "Dialog";

    public void Tick()
    {
        if (_panel != null && !_panel.IsAlive)
            _panel = null;

        // (Re-)attach to the live scenario manager instance.
        UIConfirmationBoxManager? manager =
            Singleton<UIConfirmationBoxManager>.IsInitialized ? Singleton<UIConfirmationBoxManager>.Instance : null;
        if (!ReferenceEquals(manager, _attached))
        {
            Detach();
            _attached = manager;
            if (_attached != null)
            {
                _attached.BoxWindowShown += OnShown;
                _attached.BoxWindowHidden += OnHidden;
            }
        }

        if (_pendingShow)
        {
            _pendingShow = false;
            ConvertNow();
        }

        // Keep the modal usable: the UI lock legitimately disables all host
        // raycasters, but the dialog is the one surface that must accept pokes
        // while modal (the 2D game leaves it clickable too).
        if (_panel != null && _panel.HostRaycaster != null && !_panel.HostRaycaster.enabled)
            _panel.HostRaycaster.enabled = true;
    }

    private void OnShown()
    {
        if (!WorldUIConfig.Dialogs.Value || !WorldUIConfig.ConversionActive
            || Choreographer.s_Choreographer == null)
            return;
        // Convert on the next tick: the box finishes its own layout/show first.
        _pendingShow = true;
    }

    private void ConvertNow()
    {
        if (_panel != null || _attached == null)
            return;

        ConfirmationBox box = _attached.CurrentBox;
        if (box == null)
            return;

        _panel = CanvasConversion.Convert(box.transform as RectTransform, Name);
        if (_panel == null)
            return;

        // In front of the HMD, at a comfortable modal distance.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null)
        {
            float scale = PanelLayout.WorldScale;
            Transform h = head.transform;
            Vector3 fwd = h.forward;
            Vector3 pos = h.position + fwd * (0.75f * scale);
            // Canvas front faces -forward: point +Z away from the viewer.
            Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
            CanvasConversion.PlaceHost(_panel, pos, rot, scale * 0.7f);
        }
        VRLog.Info("WorldUI", "Confirmation dialog moved to world space (poke yes/no).");
    }

    private void OnHidden()
    {
        _pendingShow = false;
        if (_panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }
    }

    private void Detach()
    {
        if (_attached != null)
        {
            _attached.BoxWindowShown -= OnShown;
            _attached.BoxWindowHidden -= OnHidden;
            _attached = null;
        }
        OnHidden();
    }

    public void Shutdown() => Detach();
}
