// Real Unity/uGUI transforms, canvases, buttons and pointer events; game stock, item renderer,
// hand tracking, widget mirroring and conversion bookkeeping are explicit test boundaries.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class UIShopItemInventory : MonoBehaviour
{
    public ScrollRect scroll = null!;
    public UIPartyItemInventoryTooltip itemTooltip = null!;
    public readonly List<UIShopItemSlot> slotPool = new();
    public Transform buyTab = null!, sellTab = null!, allFilter = null!, _ownedFilter = null!,
        headFilter = null!, bodyFilter = null!, handsFilter = null!, legsFilter = null!, smallItemsFilter = null!;
}
public class CatalogHoverProbe : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public int Enters, Exits;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data) { Enters++; }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data) { Exits++; }
}
public class UIShopItemSlot : MonoBehaviour
{ public ScenarioRuleLibrary.CItem Item = null!; public Selectable Selectable = null!; }
public class UIPartyItemInventoryTooltip : MonoBehaviour
{ public bool IsShown; public ItemCardUI m_ItemCardUI = null!; }
public class UITextTooltipTarget : MonoBehaviour
{
    public bool TooltipShown; public int Enters, Exits;
    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data)
    {
        // Native targets configured with anchorToExactMouseTargetInstead dereference this.
        if (data.pointerEnter != gameObject) throw new InvalidOperationException("native exact-target tooltip needs pointerEnter");
        Enters++; TooltipShown = true;
        GloomhavenVR.WorldUI.NativeTemplates.Tooltip!.m_AnchorToTarget = (RectTransform)transform;
        GloomhavenVR.WorldUI.NativeTemplates.Tooltip.gameObject.SetActive(true);
    }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data)
    { Exits++; TooltipShown = false; GloomhavenVR.WorldUI.NativeTemplates.Tooltip!.gameObject.SetActive(false); }
}
public class ItemCardUI : MonoBehaviour
{
    public UITextTooltipTarget AllHintsCardTooltip = null!;
    public ScenarioRuleLibrary.CItem item = null!;
    public void Show(bool highlightElement) { if (item == null || item.ID == 0) throw new Exception("invalid pooled item"); gameObject.SetActive(true); }
}
namespace UnityEngine.UI { public class UIWindow : MonoBehaviour { } public class UITooltip : MonoBehaviour { public RectTransform m_AnchorToTarget = null!; } }
namespace ScenarioRuleLibrary { public class CItem { public int ID; public CItem(int id) { ID = id; } } }
public static class ObjectPool
{
    public enum ECardType { Item }
    public static int Alive;
    public static GameObject SpawnCard(int id, ECardType type, Transform parent, bool resetLocalScale,
        bool resetToMiddle, bool resetLocalRotation, bool activate)
    {
        var go = new GameObject("OriginalItem", typeof(RectTransform), typeof(Image));
        go.SetActive(false); go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(180f, 145f);
        go.AddComponent<ItemCardUI>(); go.AddComponent<GraphicRaycaster>(); Alive++; return go;
    }
}
namespace TMPro
{
    public enum TextAlignmentOptions { Center }
    public class TMP_FontAsset : ScriptableObject { }
    public class TMP_Text : Graphic
    {
        public TMP_FontAsset? font; public Material? fontSharedMaterial;
        public TextAlignmentOptions alignment; public float fontSize; public string text = "";
    }
    public class TextMeshProUGUI : TMP_Text { }
}
namespace GloomhavenVR.Rig { internal static class VRRigDriver { internal static Camera? HeadCamera; } }
namespace GloomhavenVR.Core { internal static class VRLayers { internal static void Apply(GameObject go) { } } }
namespace GloomhavenVR.Cards
{
    internal static class ItemBurnPlayback { internal static void ObserveInitialState(ItemCardUI item) { } }
    internal static class CardFaceMipBake { internal static void Rescan(ItemCardUI item) { } }
}
namespace GloomhavenVR.Hands.Interact
{
    internal static class UguiPokeSurfaces
    {
        internal static HashSet<Canvas> Registered = new();
        internal static void Register(Canvas canvas) => Registered.Add(canvas);
        internal static void Unregister(Canvas canvas) => Registered.Remove(canvas);
    }
}
namespace GloomhavenVR.Net
{
    internal sealed class RemoteWidgetMirror
    {
        private readonly Dictionary<Transform, Transform> _clones = new(); private readonly Transform _mount;
        internal RemoteWidgetMirror(string name, Transform mount, float width, float height, Vector2 offset, Func<Transform, bool>? externallyShownBranch = null) { _mount = mount; }
        internal void SetOwnerFrame(Vector2 a, Vector2 b) { }
        internal bool Refresh(Transform source)
        {
            Clone(source, _mount); return true;
        }
        private void Clone(Transform source, Transform parent)
        {
            if (!_clones.TryGetValue(source, out Transform target))
            {
                var go = new GameObject("Inert:" + source.name, typeof(RectTransform));
                go.transform.SetParent(parent, false); _clones[source] = target = go.transform;
                CanvasGroup group = source.GetComponent<CanvasGroup>();
                if (group != null) go.AddComponent<CanvasGroup>().alpha = group.alpha;
            }
            foreach (Transform child in source) Clone(child, target);
        }
        internal Transform? CloneOf(Transform source) => _clones.TryGetValue(source, out var clone) ? clone : null;
        internal void SetShown(bool shown) { foreach (Transform clone in _clones.Values) if (clone != null && clone.parent == _mount) clone.gameObject.SetActive(shown); }
        internal void TickLive() { }
        internal void Destroy() { foreach (Transform clone in _clones.Values) if (clone != null) UnityEngine.Object.DestroyImmediate(clone.gameObject); _clones.Clear(); }
    }
    internal static class RemoteItemCardSource
    { internal static void ReturnBorrowed(int id, GameObject go) { if (!go.GetComponent<Image>().raycastTarget || !go.GetComponent<GraphicRaycaster>().enabled) throw new Exception("pooled native input flags restored before recycle"); ObjectPool.Alive--; UnityEngine.Object.DestroyImmediate(go); } }
}
namespace GloomhavenVR.WorldUI
{
    internal static class NativeTemplates { internal static UITooltip? Tooltip; }
    internal static class TownServiceNativeAssets { internal static void PrepareItem(ItemCardUI item) { } }
    internal sealed class TownServiceToken : IDisposable
    {
        internal bool Disposed; private readonly Func<bool> _alive;
        internal TownServiceToken(RectTransform source, Selectable button, Func<object?> identity, Func<object?> context, Func<bool> alive, Transform mat) { _alive = alive; }
        internal bool CanGrab => !Disposed && _alive();
        internal void Tick(float scale) { }
        public void Dispose() { Disposed = true; }
    }
    internal sealed class ConfigFloat { internal float Value = 1f; }
    internal static class WorldUIConfig { internal static ConfigFloat CanvasScaleMm = new(); }
    internal sealed class ConvertedPanel
    {
        internal Transform Target = null!, OriginalParent = null!;
        internal GameObject HostGo = null!; internal RectTransform HostRect => (RectTransform)HostGo.transform;
        internal bool IsAlive = true; internal Vector3 Position, Scale; internal Quaternion Rotation;
        internal int Sibling;
    }
    internal static class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> Active = new();
        internal static ConvertedPanel Convert(RectTransform source, string name, bool fitContent, bool useModLayer, bool transparentBackground)
        {
            var p = new ConvertedPanel { Target = source, OriginalParent = source.parent, Sibling = source.GetSiblingIndex(),
                Position = source.localPosition, Rotation = source.localRotation, Scale = source.localScale, HostGo = new GameObject(name, typeof(RectTransform)) };
            p.HostRect.sizeDelta = source.rect.size; source.SetParent(p.HostGo.transform, false); Active.Add(p); return p;
        }
        internal static void PlaceHost(ConvertedPanel p, Vector3 pos, Quaternion rotation, float worldScale)
        { p.HostGo.transform.SetPositionAndRotation(pos, rotation); p.HostGo.transform.localScale = Vector3.one * (.001f * worldScale); }
        internal static void Release(ConvertedPanel p)
        {
            p.Target.SetParent(p.OriginalParent, false); p.Target.SetSiblingIndex(p.Sibling);
            p.Target.localPosition = p.Position; p.Target.localRotation = p.Rotation; p.Target.localScale = p.Scale;
            p.IsAlive = false; Active.Remove(p); UnityEngine.Object.DestroyImmediate(p.HostGo);
        }
    }
    internal sealed class GrabbableModal
    {
        internal void Build(ConvertedPanel p, float scale, string name) { }
        internal void SetExtraScale(float scale) { }
        internal void SnapFrameTo(Vector3 p, Quaternion q) { }
        internal void Tick() { }
        internal void LateSyncHost() { }
        internal void Destroy() { }
    }
}
