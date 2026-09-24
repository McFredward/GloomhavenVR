using System;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

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
    private string? _conversionFailure;

    // A dedicated conversion failure must not leave ConfirmationBox excluded by
    // the generic modal owner. This is polled there, including missed Show edges.
    internal static UIWindow? FallbackWindow { get; private set; }

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

        ConfirmationBox? box = _attached != null ? _attached.CurrentBox : null;
        if (box == null || !box.IsOpen)
        {
            OnHidden();
            return;
        }

        if (_conversionFailure != null)
        {
            RecoverConversion(box);
            return;
        }

        // MapChoreographer is NOT a Choreographer. The old scenario-only gate
        // excluded map card-choice confirmations while catch-all also refused them.
        // Read current native state as well as events: a dialog may predate attachment
        // or be opened while the player temporarily uses the desktop/map view.
        bool inRoom = VRModeStateMachine.TableInFrontOfPlayer && WorldUIConfig.ConversionActive;
        if (!inRoom || !WorldUIConfig.Dialogs.Value || FlatScreen.ManualScreenActive)
        {
            ReleasePanel();
            if (!inRoom) FallbackWindow = null;
            else if (!WorldUIConfig.Dialogs.Value) FallbackWindow = box.GetComponent<UIWindow>();
            return;
        }

        if (FallbackWindow != null)
            return; // generic conversion/screen recovery owns this native opening

        if (_pendingShow || _panel == null)
        {
            _pendingShow = false;
            try
            {
                ConvertNow();
                if (_panel == null)
                    UseFallback(box, "conversion returned no panel");
            }
            catch (Exception ex)
            {
                // Keep the original subtree intact. Release can itself require a
                // later retry; retaining _panel until success preserves that owner.
                _conversionFailure = ex.GetType().Name + ": " + ex.Message;
                RecoverConversion(box);
            }
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
            || !VRModeStateMachine.TableInFrontOfPlayer)
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

        // P5 (A.10): small close-range dialog — tighter hover halo / shallower press.
        _panel = CanvasConversion.Convert(box.transform as RectTransform, Name,
            pokeTuning: Hands.Interact.PokeSurfaceTuning.SmallDialog);
        if (_panel == null)
            return;

        // In front of the HMD, at a comfortable modal distance.
        Camera? head = CanvasConversion.WorldCamera;
        if (head != null)
        {
            float scale = PanelLayout.WorldScale;
            Transform h = head.transform;
            // YAW ONLY (user ruling 2026-09-04: "Das soll generell bei keinem Fenster der Fall sein.
            // Ausschließlich yaw-achse."). Canvas front faces −forward, so the yaw-only rotation
            // HeadFacing returns already points +Z away from the viewer — the same convention this
            // line always had, minus the pitch it used to inherit from the raw head forward.
            HeadFacing.Facing facing = HeadFacing.YawOnly(h);
            // POSITION KEPT ON THE RAW GAZE: a yes/no box is answered with a poke and belongs in the
            // middle of the view, which the raw gaze gives at any head pitch. The ruling constrains
            // the rotation; where the box lands is unchanged.
            Vector3 pos = h.position + h.forward * (0.75f * scale);
            CanvasConversion.PlaceHost(_panel, pos, facing.Rotation, scale * 0.7f);
            HeadFacing.LogPlaced(Name, facing, pos,
                "along the RAW gaze at 0.75 m × scale — unchanged; only the rotation is constrained "
                + "by the ruling");
        }
        VRLog.Info("WorldUI", "Confirmation dialog moved to world space (poke yes/no).");
    }

    private void OnHidden()
    {
        _pendingShow = false;
        FallbackWindow = null;
        ReleasePanel();
        _conversionFailure = null;
    }

    private void RecoverConversion(ConfirmationBox box)
    {
        ReleasePanel();
        UseFallback(box, _conversionFailure!);
        _conversionFailure = null;
    }

    private void UseFallback(ConfirmationBox box, string reason)
    {
        FallbackWindow = box.GetComponent<UIWindow>();
        VRLog.Alert("WorldUI", "CONFIRMATION FALLBACK: native dialog could not be floated ("
            + reason + "); generic modal conversion and desktop recovery retain its original buttons.");
    }

    private void ReleasePanel()
    {
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
