using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

internal sealed partial class RemoteCardArt
{
    private ItemAppearanceBindings? _itemNativeBindings;
    private ItemCardEffects? _itemNativeSource;
    private int _itemNativePlayer;
    private CPlayerActor? _itemNativeActor;
    private CItem? _itemNativeItem;
    private readonly List<Image> _itemNativeImages = new();
    internal void SetNativeItemAppearance(int playerId, CPlayerActor? actor, CItem? item)
    {
        _itemNativePlayer = playerId; _itemNativeActor = actor; _itemNativeItem = item;
        ApplyNativeItemAppearance();
    }
    internal void ApplyNativeItemAppearance()
    {
        if (_clone == null || _host == null || !_host.activeInHierarchy || _itemNativePlayer == 0
            || !ItemAppearanceMirror.TryGet(_itemNativePlayer, _itemNativeActor, _itemNativeItem,
                out ItemAppearanceState? previous, out ItemAppearanceState? current, out float progress)
            || previous == null || current == null) return;
        ItemCardEffects? effects = _clone.GetComponentInChildren<ItemCardEffects>(true);
        if (effects == null) return;
        if (!ReferenceEquals(_itemNativeSource, effects))
        {
            _itemNativeBindings?.Destroy(); _itemNativeSource = effects; _itemNativeBindings = new ItemAppearanceBindings(effects);
            RemoteItemAppearancePump pump = _clone.GetComponent<RemoteItemAppearancePump>() ?? _clone.AddComponent<RemoteItemAppearancePump>();
            pump.Art = this; pump.Source = effects;
        }
        _itemNativeImages.Clear();
        foreach (var graphic in _itemNativeBindings!.Graphics.Values) if (graphic is Image image) _itemNativeImages.Add(image);
        if (!TryMeasureFxFootprint(_itemNativeImages, out Vector4 bounds)) return;
        TakeFxLookHold();
        if (_itemNativeBindings.Apply(previous, current, progress, bounds, ApplyItemFlameQueue)) ItemAppearanceMirror.MarkPresented(_itemNativePlayer, current, progress);
        else ItemAppearanceMirror.RejectPresentation(_itemNativePlayer, current);
    }
    private void ApplyItemFlameQueue(Image flame, Material material)
    {
        MeasureFaceQueue(flame);
        if (_faceQueue >= 0) material.renderQueue = Mathf.Min(_faceQueue + 1, FlameQueueCeiling);
    }
    private void ReleaseNativeItemAppearance(ItemCardEffects? source)
    {
        if (!ReferenceEquals(_itemNativeSource, source)) return;
        _itemNativeBindings?.Destroy(); _itemNativeBindings = null; _itemNativeSource = null;
    }
}
internal sealed class RemoteItemAppearancePump : MonoBehaviour
{
    internal RemoteCardArt? Art;
    internal ItemCardEffects? Source;
    private void LateUpdate() => Art?.ApplyNativeItemAppearance();
    private void OnDestroy() { Art?.ReleaseItemAppearancePump(Source); Art = null; }
}
internal sealed partial class RemoteCardArt
{
    internal void ReleaseItemAppearancePump(ItemCardEffects? source) => ReleaseNativeItemAppearance(source);
}
