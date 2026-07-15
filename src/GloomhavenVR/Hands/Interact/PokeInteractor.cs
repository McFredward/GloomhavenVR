using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Fingertip (index tip) press interaction (FROZEN Phase-2 API). Two target kinds:
///
/// 1. Registered <see cref="IPokeable"/> colliders (<see cref="VRInteractables"/>):
///    nearest collider within hover range gets OnPokeEnter/Exit; pressing into the
///    ~8 mm fingertip contact radius fires OnPoke once (re-arms after retracting).
///
/// 2. Registered uGUI canvases (<see cref="UguiPokeSurfaces"/>): when the fingertip
///    crosses a world-space canvas plane, real pointer events are synthesized via
///    ExecuteEvents (<see cref="UguiPointer"/>) — hover from the front side, press on
///    plane contact, release on retraction. Game modality is respected because hits
///    come from the canvas's own (enabled) GraphicRaycaster only.
///
/// Plain class ticked by <see cref="VRHand"/> every frame after pose update.
/// No per-frame allocations: for-loops over registries, reused event data.
/// All distances are meters at scale 1 and multiplied by the hand's world scale.
/// </summary>
internal sealed class PokeInteractor
{
    // Distances in meters (scale 1).
    private const float FingertipRadius = 0.008f;
    private const float HoverRange = 0.035f;
    private const float ReleaseRange = 0.02f;
    private const float CanvasHoverRange = 0.06f;
    private const float CanvasReleaseDepth = 0.012f;
    /// <summary>How far the fingertip may sink THROUGH the canvas before the press is cancelled.</summary>
    private const float CanvasPressThrough = 0.05f;

    private readonly VRHand _hand;
    private readonly UguiPointer _pointer;

    private bool _enabled = true;
    private IPokeable? _hovered;
    private bool _armed = true;

    private Canvas? _activeCanvas;
    private bool _canvasPressed;

    internal PokeInteractor(VRHand hand)
    {
        _hand = hand;
        _pointer = new UguiPointer(hand.Side);
    }

    /// <summary>Currently hovered pokeable, if any.</summary>
    public IPokeable? Hovered => _hovered;

    /// <summary>Currently hovered uGUI object (registered canvases only), if any.</summary>
    public GameObject? HoveredUi => _pointer.Hovered;

    /// <summary>Enable/disable (mode policy). Disabling cancels in-flight hover/press.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            if (!value)
                CancelAll();
        }
    }

    internal void Tick()
    {
        if (!_enabled || !_hand.HasPose)
        {
            return;
        }

        float scale = _hand.WorldScale;
        Vector3 tip = _hand.Rig.IndexTip.position;

        TickPokeables(tip, scale);
        TickCanvases(tip, scale);
    }

    internal void CancelAll()
    {
        if (_hovered != null)
        {
            SafeExit(_hovered);
            _hovered = null;
        }
        _armed = true;
        _pointer.Cancel();
        _activeCanvas = null;
        _canvasPressed = false;
    }

    // ---- collider pokeables -------------------------------------------------------------

    private void TickPokeables(Vector3 tip, float scale)
    {
        var entries = VRInteractables.Pokeables;
        IPokeable? nearest = null;
        float nearestDist = float.MaxValue;
        bool sawDeadCollider = false;

        for (int i = 0; i < entries.Count; i++)
        {
            Collider collider = entries[i].Collider;
            if (collider == null)
            {
                sawDeadCollider = true;
                continue;
            }
            if (!collider.enabled || !collider.gameObject.activeInHierarchy)
                continue;

            float dist = Vector3.Distance(tip, collider.ClosestPoint(tip));
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = entries[i].Target;
            }
        }

        if (sawDeadCollider)
            VRInteractables.Prune();

        // Hover transition.
        IPokeable? newHover = nearest != null && nearestDist <= HoverRange * scale ? nearest : null;
        if (!ReferenceEquals(newHover, _hovered))
        {
            if (_hovered != null)
                SafeExit(_hovered);
            _hovered = newHover;
            _armed = true;
            if (_hovered != null)
            {
                _hovered.OnPokeEnter(_hand);
                _hand.SendHaptic(HapticPreset.HoverTick);
            }
        }

        // Contact (press) with re-arm hysteresis.
        if (_hovered != null)
        {
            float contact = FingertipRadius * scale;
            if (_armed && nearestDist <= contact)
            {
                _armed = false;
                _hovered.OnPoke(_hand);
                _hand.SendHaptic(HapticPreset.ClickPulse);
            }
            else if (!_armed && nearestDist > ReleaseRange * scale)
            {
                _armed = true;
            }
        }
    }

    private void SafeExit(IPokeable target)
    {
        try
        {
            target.OnPokeExit(_hand);
        }
        catch (System.Exception ex)
        {
            Core.VRLog.Error("Interact", $"OnPokeExit threw: {ex}");
        }
    }

    // ---- uGUI canvases --------------------------------------------------------------------

    private void TickCanvases(Vector3 tip, float scale)
    {
        var surfaces = UguiPokeSurfaces.Surfaces;

        // Find the canvas whose plane the fingertip is closest to (front side only).
        Canvas? best = null;
        float bestAbs = float.MaxValue;
        float bestSigned = 0f;
        bool sawDead = false;

        for (int i = 0; i < surfaces.Count; i++)
        {
            Canvas canvas = surfaces[i];
            if (canvas == null)
            {
                sawDead = true;
                continue;
            }
            if (!canvas.isActiveAndEnabled)
                continue;

            Transform t = canvas.transform;
            // uGUI renders facing -forward: the viewer/front side is where
            // dot(tip - canvas, forward) < 0. Fingers physically sink through the
            // plane on a press, so tolerate some press-through before dropping it.
            float signed = Vector3.Dot(tip - t.position, t.forward);
            if (signed > CanvasPressThrough * scale)
                continue; // far behind the canvas — ignore

            // Inside the canvas rect?
            var rect = (RectTransform)t;
            Vector3 local = t.InverseTransformPoint(tip);
            if (!rect.rect.Contains(new Vector2(local.x, local.y)))
                continue;

            float abs = Mathf.Abs(signed);
            if (abs < bestAbs)
            {
                bestAbs = abs;
                bestSigned = signed;
                best = canvas;
            }
        }

        if (sawDead)
            UguiPokeSurfaces.Prune();

        // Canvas switch / loss cancels in-flight pointer state.
        if (!ReferenceEquals(best, _activeCanvas))
        {
            _pointer.Cancel();
            _canvasPressed = false;
            _activeCanvas = best;
        }

        if (_activeCanvas == null)
            return;

        if (bestAbs > CanvasHoverRange * scale && !_canvasPressed)
        {
            _pointer.SetHovered(null);
            return;
        }

        // Project the fingertip onto the canvas plane → screen point of the canvas camera.
        Transform ct = _activeCanvas.transform;
        Vector3 onPlane = tip - ct.forward * bestSigned;
        Camera? cam = _activeCanvas.worldCamera != null ? _activeCanvas.worldCamera : Camera.main;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, onPlane);

        bool hit = _pointer.TryRaycast(_activeCanvas, screenPos, out RaycastResult top);
        GameObject? previousHover = _pointer.Hovered;
        _pointer.SetHovered(hit ? top.gameObject : null);
        if (hit && previousHover == null && _pointer.Hovered != null)
            _hand.SendHaptic(HapticPreset.HoverTick);

        // Press when the fingertip reaches the plane, release when it retracts.
        if (!_canvasPressed && hit && bestSigned >= -FingertipRadius * scale)
        {
            _canvasPressed = true;
            _pointer.Press(screenPos);
            _hand.SendHaptic(HapticPreset.ClickPulse);
        }
        else if (_canvasPressed && bestSigned < -CanvasReleaseDepth * scale)
        {
            _canvasPressed = false;
            _pointer.Release(screenPos);
        }
    }
}
