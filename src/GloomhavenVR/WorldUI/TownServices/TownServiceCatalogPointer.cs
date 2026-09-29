using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GloomhavenVR.WorldUI;

/// <summary>Presentation input only. A catalog entry validates its native owner on every event.</summary>
internal sealed class TownServiceCatalogPointer : MonoBehaviour, IPointerClickHandler,
    IPointerEnterHandler, IPointerExitHandler, IScrollHandler
{
    internal Action? Click = null;
    internal Action<bool>? Hover = null;
    internal Action<int>? Scroll = null;
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left) Click?.Invoke();
    }
    public void OnPointerEnter(PointerEventData eventData) => Hover?.Invoke(true);
    public void OnPointerExit(PointerEventData eventData) => Hover?.Invoke(false);
    public void OnScroll(PointerEventData eventData)
    {
        if (Mathf.Abs(eventData.scrollDelta.y) > .01f) Scroll?.Invoke(eventData.scrollDelta.y > 0f ? -1 : 1);
    }
    private void OnDisable() => Hover?.Invoke(false);
}
