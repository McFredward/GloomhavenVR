using System;
using GloomhavenVR.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Read-only original merchant detail placard. Native Show still owns its pooled card,
/// discount/binding text, modifiers and dismissal; the mirror does not inherit the intentionally
/// hidden inventory viewport's alpha or run any of those native controllers.</summary>
internal sealed class TownServiceCatalogPreview : IDisposable
{
    private readonly UIPartyItemInventoryTooltip _source;
    private readonly Func<bool> _visible;
    private readonly GameObject _mount;
    private readonly RemoteWidgetMirror _mirror;
    private readonly GameObject _hintMount;
    private readonly RemoteWidgetMirror _hintMirror;
    private UITextTooltipTarget? _hintTarget;
    private ItemCardUI? _lastCard, _hintCard;
    private int _hintItemId;
    private bool _ownsHint, _hintShown;
    private TownServiceWindowMask? _hintMask;
    private Transform? _maskedHintParent;
    private bool _shown, _disposed;
    private float _nextRefresh, _nextHintRefresh;
    internal Transform Source => _source.transform;
    internal Transform? HintSource => NativeTemplates.Tooltip != null ? NativeTemplates.Tooltip.transform : null;
    internal bool OwnsHintSource => !_disposed && _hintShown && _hintTarget != null
        && _hintCard != null && _hintCard.item != null && _hintCard.item.ID == _hintItemId
        && _hintTarget.TooltipShown && NativeTemplates.Tooltip != null
        && NativeTemplates.Tooltip.m_AnchorToTarget == _hintTarget.transform;
    internal Transform? HintContent => OwnsHintSource && HintSource != null ? _hintMirror.CloneOf(HintSource) : null;
    internal Transform? HintCloneOf(Transform original) => OwnsHintSource ? _hintMirror.CloneOf(original) : null;
    internal Transform? Content => !_disposed && _shown ? _mirror.CloneOf(Source) : null;
    internal Transform? CloneOf(Transform original) => !_disposed && _shown ? _mirror.CloneOf(original) : null;

    internal TownServiceCatalogPreview(UIPartyItemInventoryTooltip source, Transform parent, Func<bool> visible)
    {
        _source = source; _visible = visible;
        _mount = new GameObject("GloomhavenVR.Catalog.ItemDetails");
        _mount.transform.SetParent(parent, false);
        _mount.transform.localPosition = new Vector3(-.59f, .009f, .05f);
        _mount.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _mirror = new RemoteWidgetMirror("CatalogItemDetails", _mount.transform, .26f, .36f, Vector2.zero);
        _hintMount = new GameObject("GloomhavenVR.Catalog.ItemRules");
        _hintMount.transform.SetParent(parent, false);
        _hintMount.transform.localPosition = new Vector3(.60f, .009f, .28f);
        _hintMount.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        _hintMirror = new RemoteWidgetMirror("CatalogItemRules", _hintMount.transform, .26f, .22f, Vector2.zero);
    }

    internal void Tick()
    {
        if (_disposed) return;
        bool shown = _source != null && _source.IsShown && _source.gameObject.activeInHierarchy && _visible();
        if (!shown) { Clear(); return; }
        if (!_shown || _lastCard != _source!.m_ItemCardUI || Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + .10f;
            _lastCard = _source!.m_ItemCardUI;
            // Fit from the original authored root rather than a hidden ancestor. Refresh clones
            // only this tooltip subtree; inherited alpha-zero list wrappers never enter it.
            _shown = _mirror.Refresh(Source);
        }
        _mirror.TickLive();
        _mirror.SetShown(_shown);
        TickHints();
    }

    private void TickHints()
    {
        UITextTooltipTarget? target = _source.m_ItemCardUI != null ? _source.m_ItemCardUI.AllHintsCardTooltip : null;
        int itemId = _source.m_ItemCardUI != null && _source.m_ItemCardUI.item != null ? _source.m_ItemCardUI.item.ID : 0;
        if (target != _hintTarget || itemId != _hintItemId)
        {
            ClearHints();
            _hintTarget = target; _hintCard = _source.m_ItemCardUI; _hintItemId = itemId;
            if (target != null && !target.TooltipShown && EventSystem.current != null)
            {
                // Hovering a physical card is also its inspect gesture. Use the game's actual
                // rules-hint target; no fabricated descriptions or native trade callbacks.
                target.OnPointerEnter(new PointerEventData(EventSystem.current));
                _ownsHint = target.TooltipShown;
            }
        }
        UITooltip? tooltip = NativeTemplates.Tooltip;
        bool shown = _hintTarget != null && _hintTarget.TooltipShown && tooltip != null
            && tooltip.m_AnchorToTarget == _hintTarget.transform && tooltip.gameObject.activeInHierarchy;
        if (!shown) { _hintShown = false; _hintMirror.SetShown(false); ReleaseHintMask(); return; }
        if (!_hintShown || Time.unscaledTime >= _nextHintRefresh)
        {
            _nextHintRefresh = Time.unscaledTime + .10f;
            _hintShown = _hintMirror.Refresh(tooltip!.transform);
        }
        _hintMirror.TickLive(); _hintMirror.SetShown(_hintShown);
        if (_hintShown && tooltip != null && tooltip.transform is RectTransform rect)
        {
            if (_hintMask == null || rect.parent != _maskedHintParent)
            {
                ReleaseHintMask();
                _hintMask = new TownServiceWindowMask(rect);
                _maskedHintParent = rect.parent;
            }
        }
        else ReleaseHintMask();
    }

    private void ReleaseHintMask()
    {
        _hintMask?.Dispose(); _hintMask = null; _maskedHintParent = null;
    }

    private void ClearHints()
    {
        ReleaseHintMask();
        if (_ownsHint && _hintTarget != null && _hintCard != null && _hintCard.item != null
            && _hintCard.item.ID == _hintItemId)
        {
            UITooltip? tooltip = NativeTemplates.Tooltip;
            if (tooltip != null && tooltip.m_AnchorToTarget == _hintTarget.transform && EventSystem.current != null)
                _hintTarget.OnPointerExit(new PointerEventData(EventSystem.current));
            else
                // A later native hover owns the shared tooltip now. Retire only our target's
                // presentation flag; its HideTooltip callback would hide that unrelated hint.
                _hintTarget.TooltipShown = false;
        }
        _hintTarget = null; _hintCard = null; _hintItemId = 0; _ownsHint = false; _hintShown = false;
        _hintMirror.SetShown(false);
    }

    internal void Clear()
    {
        _shown = false;
        _mirror.SetShown(false);
        ClearHints();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Clear(); _disposed = true;
        _mirror.Destroy(); _hintMirror.Destroy();
        UnityEngine.Object.Destroy(_mount); UnityEngine.Object.Destroy(_hintMount);
        // Never destroy, hide, recycle or reparent the original tooltip or its pooled card.
    }
}
