using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.Hands.Interact;

/// <summary>
/// Synthesizes uGUI pointer events (enter/exit/down/up/click) at a screen position,
/// the way the game itself clicks buttons programmatically
/// (<c>BaseButtons.clickButton</c> fires <c>ExecuteEvents.pointerClickHandler</c>,
/// UI-ARCH §4.4 — verified in the real GH.Runtime.dll: <c>private void
/// clickButton(GameObject Objbutton)</c>, IL 36 B).
///
/// MODALITY: this class never bypasses the game's UI lock. Hits are produced
/// exclusively by <see cref="GraphicRaycaster.Raycast"/> on ENABLED raycasters —
/// <c>UIManager.ToggleLockUI</c> disables every raycaster to lock the UI
/// (verified: <c>public void ToggleLockUI(bool active) { graphicRaycaster.enabled =
/// !active; ... }</c>), so a locked UI yields no hits and therefore no events.
///
/// One instance per hand; PointerEventData and hit lists are reused (no per-frame
/// allocations from our side; GraphicRaycaster's internal sort may allocate — it only
/// runs while a fingertip is actually near a registered canvas).
/// </summary>
internal sealed class UguiPointer
{
    /// <summary>Custom pointer IDs, clear of mouse (-1..-3) and touch (0+) ranges.</summary>
    private const int LeftHandPointerId = -101;
    private const int RightHandPointerId = -102;

    // P6: the far-ray pointer (RayUguiDriver) coexists with the fingertip pointer on
    // the same hand — distinct IDs so uGUI never sees one pointer teleporting.
    private const int LeftHandRayPointerId = -111;
    private const int RightHandRayPointerId = -112;

    private readonly int _pointerId;
    private readonly List<RaycastResult> _hits = new(16);

    private PointerEventData? _pointerData;
    private GameObject? _hovered;
    private GameObject? _pressed;
    private GameObject? _pressedClickHandler;

    internal UguiPointer(HandSide side, bool farRay = false)
    {
        _pointerId = farRay
            ? (side == HandSide.Left ? LeftHandRayPointerId : RightHandRayPointerId)
            : (side == HandSide.Left ? LeftHandPointerId : RightHandPointerId);
    }

    /// <summary>Currently hovered uGUI object (topmost raycast hit), if any.</summary>
    internal GameObject? Hovered => _hovered;

    /// <summary>
    /// Raycast a specific canvas at a screen point through its own GraphicRaycaster.
    /// Returns false (and clears hover) when the raycaster is missing or DISABLED —
    /// i.e. when the game locked its UI.
    /// </summary>
    internal bool TryRaycast(Canvas canvas, Vector2 screenPos, out RaycastResult topHit)
    {
        topHit = default;

        GraphicRaycaster? raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
        if (raycaster == null || !raycaster.enabled || !raycaster.isActiveAndEnabled)
            return false;

        PointerEventData data = GetData();
        data.position = screenPos;

        _hits.Clear();
        raycaster.Raycast(data, _hits);
        if (_hits.Count == 0)
            return false;

        // GraphicRaycaster appends results sorted by depth (closest/topmost first).
        topHit = _hits[0];
        data.pointerCurrentRaycast = topHit;
        return true;
    }

    /// <summary>Update hover state to <paramref name="target"/> (null = nothing hovered).</summary>
    internal void SetHovered(GameObject? target)
    {
        if (ReferenceEquals(target, _hovered))
            return;

        PointerEventData data = GetData();
        if (_hovered != null)
            ExecuteEvents.Execute(_hovered, data, ExecuteEvents.pointerExitHandler);
        _hovered = target;
        if (_hovered != null)
            ExecuteEvents.Execute(_hovered, data, ExecuteEvents.pointerEnterHandler);
    }

    /// <summary>True if the pointer currently has a pressed target.</summary>
    internal bool IsPressed => _pressed != null;

    /// <summary>Pointer-down on the hovered object (mirrors StandaloneInputModule press handling).</summary>
    internal void Press(Vector2 screenPos)
    {
        if (_hovered == null || _pressed != null)
            return;

        PointerEventData data = GetData();
        data.position = screenPos;
        data.pressPosition = screenPos;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        data.eligibleForClick = true;
        data.button = PointerEventData.InputButton.Left;

        GameObject pressTarget = ExecuteEvents.ExecuteHierarchy(_hovered, data, ExecuteEvents.pointerDownHandler)
                                 ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered);
        _pressed = pressTarget != null ? pressTarget : _hovered;
        _pressedClickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered);
        data.pointerPress = _pressed;
    }

    /// <summary>Pointer-up (+ click when released over the same handler).</summary>
    internal void Release(Vector2 screenPos)
    {
        if (_pressed == null)
            return;

        PointerEventData data = GetData();
        data.position = screenPos;

        ExecuteEvents.Execute(_pressed, data, ExecuteEvents.pointerUpHandler);

        GameObject? hoveredClickHandler = _hovered != null
            ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(_hovered)
            : null;
        if (_pressedClickHandler != null && ReferenceEquals(hoveredClickHandler, _pressedClickHandler))
            ExecuteEvents.Execute(_pressedClickHandler, data, ExecuteEvents.pointerClickHandler);

        data.pointerPress = null;
        data.eligibleForClick = false;
        _pressed = null;
        _pressedClickHandler = null;
    }

    /// <summary>Abort any in-flight hover/press (interactor disabled, canvas gone, …).</summary>
    internal void Cancel()
    {
        if (_pressed != null)
        {
            PointerEventData data = GetData();
            ExecuteEvents.Execute(_pressed, data, ExecuteEvents.pointerUpHandler);
            _pressed = null;
            _pressedClickHandler = null;
        }
        SetHovered(null);
    }

    private PointerEventData GetData()
    {
        // Created lazily: EventSystem.current may not exist during early boot.
        if (_pointerData == null)
            _pointerData = new PointerEventData(EventSystem.current) { pointerId = _pointerId };
        return _pointerData;
    }
}
