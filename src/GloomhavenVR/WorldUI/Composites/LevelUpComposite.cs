using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// The native level-up inventory is a sibling under Campaign Canvas, not a child of
/// UILevelUpWindow. Build719 floated that sibling (log5455) and map-room stickiness
/// kept it visible after native Hide. Carry the original inventory inside its actual
/// owner instead. A separate mount leaves the inventory's own slide animation intact;
/// neither native state, selection callbacks nor card authority are replaced.
/// </summary>
internal static class LevelUpComposite
{
    private static UILevelUpCardInventory? _inventory;
    private static UIWindow? _owner;
    private static RectTransform? _rect, _mount;
    private static Transform? _home;
    private static int _sibling;
    private static Vector2 _anchorMin, _anchorMax, _pivot, _size;
    private static Vector3 _position, _scale;
    private static Quaternion _rotation;
    private static readonly MovedSubtreeLayers Layers = new(128);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static float _previewRight;
    private static float _previewLeft;
    private static ComparisonSeat? _comparison;
    private static bool _faultLogged;

    /// <summary>The game deliberately keeps the existing deck visible during level-up
    /// (NewPartyDisplayUI._ignoreDisplayTypes includes CARDS). It must share the same
    /// preview envelope too, rather than covering the right fifth of the full card.
    /// Mounting this original view below the level root also makes fixed-fit apply one
    /// group transform, instead of fighting a second writer on the deck's own root.</summary>
    private sealed class ComparisonSeat
    {
        internal readonly RectTransform Rect, Mount;
        private readonly Transform? _parent;
        private readonly int _index;
        private readonly Vector2 _min, _max, _pivot, _size;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;

        internal ComparisonSeat(RectTransform rect, RectTransform parent)
        {
            Rect = rect; _parent = rect.parent; _index = rect.GetSiblingIndex();
            _min = rect.anchorMin; _max = rect.anchorMax; _pivot = rect.pivot;
            _size = rect.sizeDelta; _position = rect.anchoredPosition3D;
            _scale = rect.localScale; _rotation = rect.localRotation;
            Vector2 measured = rect.rect.size;
            Mount = (RectTransform)new GameObject("GloomhavenVR.LevelUpComparisonSeat",
                typeof(RectTransform)).transform;
            Mount.SetParent(parent, false);
            Mount.anchorMin = Mount.anchorMax = Mount.pivot = new Vector2(.5f, .5f);
            Mount.sizeDelta = measured;
            rect.SetParent(Mount, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = measured;
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        }

        internal bool Restore()
        {
            if (Rect != null)
            {
                Rect.SetParent(_parent, false);
                if (!ReferenceEquals(Rect.parent, _parent)) return false;
                Rect.anchorMin = _min; Rect.anchorMax = _max; Rect.pivot = _pivot;
                Rect.sizeDelta = _size; Rect.anchoredPosition3D = _position;
                Rect.localScale = _scale; Rect.localRotation = _rotation;
                Rect.SetSiblingIndex(_index);
            }
            if (Mount != null) Object.Destroy(Mount.gameObject);
            return true;
        }
    }

    // All three fixed-fit populations (fit, arrival signature and reveal veil) read
    // these exact original roots. IsShowing is NOT a lifetime: the game clears it
    // before the player chooses a card, while myWindow remains open.
    internal static UIWindow? FittedView => Singleton<UILevelUpWindow>.IsInitialized
        ? Singleton<UILevelUpWindow>.Instance?.myWindow : null;
    internal static UILevelUpCardInventory? InventoryView
    {
        get
        {
            var inventory = Singleton<UILevelUpWindow>.IsInitialized
                ? Singleton<UILevelUpWindow>.Instance?.inventory : null;
            return inventory != null ? inventory : Singleton<UIResetLevelUpWindow>.IsInitialized
                ? Singleton<UIResetLevelUpWindow>.Instance?.inventory : null;
        }
    }

    internal static void Tick()
    {
        try { TickCore(); }
        catch (System.Exception ex)
        {
            Reset();
            if (_faultLogged) return;
            _faultLogged = true;
            VRLog.Warn("WorldUI", "LEVEL-UP COMPOSITE: original inventory could not be seated ("
                + ex.GetType().Name + "); native window handling retains its controls.");
        }
    }

    private static void TickCore()
    {
        var level = Singleton<UILevelUpWindow>.IsInitialized
            ? Singleton<UILevelUpWindow>.Instance : null;
        UILevelUpCardInventory? inventory = InventoryView;
        UIWindow? inventoryWindow = inventory != null ? inventory.GetComponent<UIWindow>() : null;
        UIWindow? owner = level != null && level.myWindow != null && level.myWindow.IsOpen
            ? level.myWindow : null;
        bool leveling = owner != null;
        if (owner == null && Singleton<UIResetLevelUpWindow>.IsInitialized)
        {
            var reset = Singleton<UIResetLevelUpWindow>.Instance;
            // The same native inventory also serves the explicit browse/reset toggle.
            // Its semantic owner then is the character panel, never the last leveled hero.
            if (reset != null && reset.displayToggle != null && reset.displayToggle.isOn
                && ReferenceEquals(reset.inventory, inventory))
                owner = NewPartyDisplayUI.PartyDisplay?.window;
        }
        RectTransform? mountParent = leveling ? owner?.transform as RectTransform
            : NewPartyDisplayUI.PartyDisplay?.AbilityCardsDisplay?.transform as RectTransform;
        ConvertedPanel? panel = FindContainingPanel(owner);
        if (!WorldUIConfig.ConversionActive || FlatScreen.ManualScreenActive
            || !Core.Events.VRModeStateMachine.TableInFrontOfPlayer
            || inventoryWindow == null || !inventoryWindow.IsOpen || owner == null
            || mountParent == null || panel == null || !panel.IsAlive)
        {
            Reset();
            return;
        }

        if (!ReferenceEquals(inventory, _inventory) || !ReferenceEquals(owner, _owner)
            || (_mount != null && !ReferenceEquals(_mount.parent, mountParent)))
        {
            Reset();
            // A rejected restore still owns an original subtree. Do not recapture
            // its temporary mount as a new native home on this rebind attempt.
            if (_rect != null || _mount != null || _comparison != null) return;
            // A previous standalone float must hand back its native home BEFORE the
            // home is recorded; otherwise its later release steals our parked child.
            if (!ModalFallback.ReleaseForComposite(inventoryWindow)) return;
            var rect = inventory!.transform as RectTransform;
            var parent = mountParent;
            if (rect == null || parent == null || rect.rect.width < 1f || rect.rect.height < 1f)
                return;
            _inventory = inventory;
            _owner = owner;
            _rect = rect;
            _home = rect.parent;
            _sibling = rect.GetSiblingIndex();
            _anchorMin = rect.anchorMin; _anchorMax = rect.anchorMax;
            _pivot = rect.pivot; _size = rect.sizeDelta;
            _position = rect.anchoredPosition3D;
            _rotation = rect.localRotation; _scale = rect.localScale;
            Vector2 measured = rect.rect.size;
            _mount = (RectTransform)new GameObject("GloomhavenVR.LevelUpInventorySeat",
                typeof(RectTransform)).transform;
            _mount.SetParent(parent, false);
            _mount.anchorMin = _mount.anchorMax = _mount.pivot = new Vector2(.5f, .5f);
            _mount.sizeDelta = measured;
            _mount.localScale = Vector3.one;
            rect.SetParent(_mount, false);
            CanvasConversion.TransferSeatedSubtree(rect, panel);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = measured;
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            // Native DisplayPanel settles x at zero. Retain the current horizontal
            // slide displacement; the mount supplies only its stable final seat.
            rect.anchoredPosition3D = new Vector3(_position.x, 0f, 0f);
            _previewRight = float.NegativeInfinity;
            _previewLeft = float.PositiveInfinity;
            Layers.Begin(parent.gameObject.layer);
            Layers.Walk(rect);
            VRLog.Info("WorldUI", "LEVEL-UP COMPOSITE: original card inventory seated inside '"
                + owner.name + "'; native card selection and confirmation remain intact.");
        }

        if (_mount == null || _rect == null) return;
        var ownerRect = mountParent;
        // The actual full-card rect is the preview envelope, not its animated glow.
        // Keep the largest envelope of this opening so hover/reveal never jiggles
        // the list or lets it cover the right edge of a newly revealed card.
        RectTransform? preview = leveling && level != null && level.cardHolder != null
            && level.cardHolder.cardHolder != null
            ? (level.cardHolder.cardHolder.Card != null
                ? level.cardHolder.cardHolder.Card.fullAbilityCard.transform as RectTransform
                : level.cardHolder.cardHolder.transform as RectTransform) : null;
        if (preview != null && preview.rect.width > 1f)
        {
            preview.GetWorldCorners(Corners);
            for (int i = 0; i < Corners.Length; i++)
            {
                _previewRight = Mathf.Max(_previewRight, ownerRect.InverseTransformPoint(Corners[i]).x);
                _previewLeft = Mathf.Min(_previewLeft, ownerRect.InverseTransformPoint(Corners[i]).x);
            }
        }
        // Browsing reset inventory sits beside the original character card list.
        if (!leveling && NewPartyDisplayUI.PartyDisplay?.AbilityCardsDisplay?.transform is RectTransform cards)
        {
            cards.GetWorldCorners(Corners);
            for (int i = 0; i < Corners.Length; i++)
                _previewRight = Mathf.Max(_previewRight, ownerRect.InverseTransformPoint(Corners[i]).x);
        }
        if (float.IsNegativeInfinity(_previewRight)) _previewRight = 0f;
        if (float.IsPositiveInfinity(_previewLeft)) _previewLeft = 0f;
        RectTransform? comparison = leveling
            ? NewPartyDisplayUI.PartyDisplay?.AbilityCardsDisplay?.transform as RectTransform : null;
        UIWindow? comparisonWindow = comparison != null ? comparison.GetComponent<UIWindow>() : null;
        bool compareVisible = comparison != null && comparison.gameObject.activeInHierarchy
            && (comparisonWindow == null || comparisonWindow.IsOpen || comparisonWindow.IsVisible);
        if (_comparison != null && (!compareVisible || !ReferenceEquals(comparison, _comparison.Rect)))
        {
            if (!_comparison.Restore()) return;
            _comparison = null;
        }
        if (compareVisible && comparison != null && _comparison == null
            && !ReferenceEquals(comparison, _rect) && !comparison.IsChildOf(ownerRect))
            _comparison = new ComparisonSeat(comparison, ownerRect);
        if (_comparison != null && _comparison.Mount != null)
        {
            Vector3 leftSeat = new(_previewLeft - ChromeParkTuning.ChromeGapPx
                - _comparison.Rect.rect.width * .5f, 0f, 0f);
            if ((_comparison.Mount.anchoredPosition3D - leftSeat).sqrMagnitude > .01f)
                _comparison.Mount.anchoredPosition3D = leftSeat;
        }
        Vector3 seat = new(_previewRight + ChromeParkTuning.ChromeGapPx + _rect.rect.width * .5f, 0f, 0f);
        if ((_mount.anchoredPosition3D - seat).sqrMagnitude > .01f)
            _mount.anchoredPosition3D = seat;
        int layer = owner.gameObject.layer;
        if (Layers.Written != layer || _rect.gameObject.layer != layer)
        {
            Layers.Begin(layer);
            Layers.Walk(_rect);
        }
    }

    internal static ConvertedPanel? FindContainingPanel(UIWindow? owner)
    {
        if (owner == null) return null;
        // Walk outward from the exact native owner; an unrelated open window is
        // never a substitute, including another player's independently open panel.
        for (Transform? node = owner.transform; node != null; node = node.parent)
            for (int i = 0; i < CanvasConversion.ActivePanels.Count; i++)
            {
                var panel = CanvasConversion.ActivePanels[i];
                if (panel.IsAlive && ReferenceEquals(panel.Target, node)) return panel;
            }
        return null;
    }

    internal static void Reset()
    {
        if (_comparison != null)
        {
            if (!_comparison.Restore()) return;
            _comparison = null;
        }
        if (_rect != null)
        {
            // Return while the destination host is still alive (pre-release hook).
            // This is a presentation restore, never native Hide/ForceClose/confirm.
            _rect.SetParent(_home, false);
            // Unity can reject a reparent during activation callbacks. Never destroy
            // the mount while it still carries the original game inventory. Retry
            // the restore next tick instead of deleting a gameplay object with it.
            if (!ReferenceEquals(_rect.parent, _home)) return;
            CanvasConversion.TransferSeatedSubtree(_rect, null);
            Layers.Restore();
            _rect.anchorMin = _anchorMin; _rect.anchorMax = _anchorMax;
            _rect.pivot = _pivot; _rect.sizeDelta = _size;
            _rect.anchoredPosition3D = _position;
            _rect.localRotation = _rotation; _rect.localScale = _scale;
            _rect.SetSiblingIndex(_sibling);
        }
        if (_mount != null) Object.Destroy(_mount.gameObject);
        _mount = _rect = null; _owner = null; _inventory = null; _home = null;
    }
}
