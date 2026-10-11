using UnityEngine;

namespace GloomhavenVR.WorldUI;

internal static partial class CanvasConversion
{
    /// <summary>Hand conversion ownership across the two explicit MapDialogSeat reparent edges.
    /// The pooled enhancement confirmation lives under the enchantress but also serves the
    /// temple. Merely moving its Transform left both converted panels holding its canvas:
    /// their camera, reveal, sorting, layer and eventual release writers still targeted the
    /// same native object. Preserve the first captured native state while moving those exact
    /// records to the physical host. A null destination restores them at the native home.
    /// No native input gate, controller, listener or transaction is changed.</summary>
    internal static void TransferSeatedSubtree(RectTransform root, ConvertedPanel? destination)
    {
        if (root == null) return;
        for (int p = 0; p < Active.Count; p++)
        {
            ConvertedPanel previous = Active[p];
            if (ReferenceEquals(previous, destination)) continue;
            for (int i = previous.AdoptedCanvases.Count - 1; i >= 0; i--)
            {
                NestedCanvasRecord record = previous.AdoptedCanvases[i];
                if (record.Canvas == null || !BelongsToSeatedSubtree(record.Canvas.transform, root)) continue;
                previous.AdoptedCanvases.RemoveAt(i);
                previous.AdoptedOrderRebaseDirty = true;
                Hands.Interact.UguiPokeSurfaces.UnregisterNested(previous.HostCanvas, record.Canvas);
                if (destination == null)
                {
                    record.Canvas.overrideSorting = record.OriginalOverrideSorting;
                    record.Canvas.sortingOrder = record.OriginalSortingOrder;
                    record.Canvas.worldCamera = record.OriginalWorldCamera;
                    if (record.AddedRaycaster != null) Object.Destroy(record.AddedRaycaster);
                }
                else
                {
                    // A destination sweep may have seen the moved canvas before this edge.
                    // Its record captured another conversion's writes, not native originals.
                    int existing = -1;
                    for (int d = 0; d < destination.AdoptedCanvases.Count; d++)
                        if (ReferenceEquals(destination.AdoptedCanvases[d].Canvas, record.Canvas)) { existing = d; break; }
                    if (existing >= 0) destination.AdoptedCanvases[existing] = record;
                    else destination.AdoptedCanvases.Add(record);
                    destination.AdoptedOrderRebaseDirty = true;
                    record.Canvas.worldCamera = destination.HostCanvas.worldCamera;
                    Hands.Interact.UguiPokeSurfaces.RegisterNested(destination.HostCanvas, record.Canvas);
                }
            }
            for (int i = previous.PreCapturedCanvases.Count - 1; i >= 0; i--)
            {
                Canvas canvas = previous.PreCapturedCanvases[i];
                if (canvas == null || !BelongsToSeatedSubtree(canvas.transform, root)) continue;
                Camera? camera = previous.PreCapturedCameras[i];
                previous.PreCapturedCanvases.RemoveAt(i); previous.PreCapturedCameras.RemoveAt(i);
                if (destination != null)
                {
                    int existing = destination.PreCapturedCanvases.IndexOf(canvas);
                    if (existing >= 0) destination.PreCapturedCameras[existing] = camera;
                    else { destination.PreCapturedCanvases.Add(canvas); destination.PreCapturedCameras.Add(camera); }
                }
            }
            for (int i = previous.Relayered.Count - 1; i >= 0; i--)
            {
                LayerRecord record = previous.Relayered[i];
                if (record.Transform == null || !BelongsToSeatedSubtree(record.Transform, root)) continue;
                previous.Relayered.RemoveAt(i);
                if (destination == null) record.Transform.gameObject.layer = record.OriginalLayer;
                else
                {
                    int existing = -1;
                    for (int d = 0; d < destination.Relayered.Count; d++)
                        if (ReferenceEquals(destination.Relayered[d].Transform, record.Transform)) { existing = d; break; }
                    if (existing >= 0) destination.Relayered[existing] = record;
                    else destination.Relayered.Add(record);
                }
            }
            for (int i = previous.Flattened.Count - 1; i >= 0; i--)
            {
                FlattenRecord record = previous.Flattened[i];
                if (record.Transform == null || !BelongsToSeatedSubtree(record.Transform, root)) continue;
                previous.Flattened.RemoveAt(i);
                if (destination == null)
                {
                    // The seat restores its root's full authored pose independently.
                    if (!ReferenceEquals(record.Transform, root))
                    {
                        Vector3 position = record.Transform.localPosition;
                        record.Transform.localPosition = new Vector3(position.x, position.y, record.OriginalLocalZ);
                        record.Transform.localRotation = record.OriginalLocalRotation;
                    }
                }
                else
                {
                    int existing = -1;
                    for (int d = 0; d < destination.Flattened.Count; d++)
                        if (ReferenceEquals(destination.Flattened[d].Transform, record.Transform)) { existing = d; break; }
                    if (existing >= 0) destination.Flattened[existing] = record;
                    else destination.Flattened.Add(record);
                }
            }
            for (int i = previous.OrderFollowers.Count - 1; i >= 0; i--)
            {
                OrderFollower record = previous.OrderFollowers[i];
                Transform? target = record.Canvas != null ? record.Canvas.transform : record.Renderer != null ? record.Renderer.transform : null;
                if (target == null || !BelongsToSeatedSubtree(target, root)) continue;
                previous.OrderFollowers.RemoveAt(i);
                if (destination != null)
                {
                    int existing = -1;
                    for (int d = 0; d < destination.OrderFollowers.Count; d++)
                        if (ReferenceEquals(destination.OrderFollowers[d].Canvas, record.Canvas)
                            && ReferenceEquals(destination.OrderFollowers[d].Renderer, record.Renderer)) { existing = d; break; }
                    if (existing >= 0) destination.OrderFollowers[existing] = record;
                    else destination.OrderFollowers.Add(record);
                }
            }
            for (int i = previous.HiddenBackgrounds.Count - 1; i >= 0; i--)
            {
                UnityEngine.UI.Graphic graphic = previous.HiddenBackgrounds[i];
                if (graphic == null || !BelongsToSeatedSubtree(graphic.transform, root)) continue;
                previous.HiddenBackgrounds.RemoveAt(i);
                if (destination != null)
                { if (!destination.HiddenBackgrounds.Contains(graphic)) destination.HiddenBackgrounds.Add(graphic); }
                else graphic.enabled = true;
            }
            for (int i = previous.HiddenCanvases.Count - 1; i >= 0; i--)
            {
                Canvas canvas = previous.HiddenCanvases[i];
                if (canvas == null || !BelongsToSeatedSubtree(canvas.transform, root)) continue;
                bool preStart = i < previous.HiddenCanvasWasPreStart.Count && previous.HiddenCanvasWasPreStart[i];
                previous.HiddenCanvases.RemoveAt(i);
                if (i < previous.HiddenCanvasWasPreStart.Count) previous.HiddenCanvasWasPreStart.RemoveAt(i);
                if (destination != null && destination.RenderHidden)
                {
                    if (!destination.HiddenCanvases.Contains(canvas))
                    { destination.HiddenCanvases.Add(canvas); destination.HiddenCanvasWasPreStart.Add(preStart); }
                }
                else if (destination == null || !RevealRestoreWithheld(destination, canvas)) canvas.enabled = true;
            }
            for (int i = previous.HiddenRenderers.Count - 1; i >= 0; i--)
            {
                Renderer renderer = previous.HiddenRenderers[i];
                if (renderer == null || !BelongsToSeatedSubtree(renderer.transform, root)) continue;
                previous.HiddenRenderers.RemoveAt(i);
                if (destination != null && destination.RenderHidden)
                { if (!destination.HiddenRenderers.Contains(renderer)) destination.HiddenRenderers.Add(renderer); }
                else renderer.enabled = true;
            }
            // An in-progress discovery walk must not flatten moved descendants on its
            // next budgeted tick. The new host's ordinary walk discovers its own tree.
            for (int i = previous.FlattenWalk.Count - 1; i >= 0; i--)
                if (previous.FlattenWalk[i] != null && BelongsToSeatedSubtree(previous.FlattenWalk[i], root))
                    previous.FlattenWalk.RemoveAt(i);
        }
        if (destination != null)
        {
            AdoptNestedCanvases(destination);
            RegisterSeatedInputOrder(root, destination);
        }
    }

    private static void RegisterSeatedInputOrder(RectTransform root, ConvertedPanel destination)
    {
        for (int i = 0; i < destination.AdoptedCanvases.Count; i++)
        {
            NestedCanvasRecord record = destination.AdoptedCanvases[i];
            if (!IsOrdinarySeatedCanvas(record, root)) continue;
            // Preserve distinct authored orders inside this one shared dialog. Dropdown
            // overlays and native sorting concessions keep their existing comparison.
            int offset = 1;
            for (int j = 0; j < destination.AdoptedCanvases.Count; j++)
            {
                NestedCanvasRecord other = destination.AdoptedCanvases[j];
                if (!IsOrdinarySeatedCanvas(other, root) || other.OriginalSortingOrder >= record.OriginalSortingOrder) continue;
                bool first = true;
                for (int k = 0; k < j; k++)
                    if (IsOrdinarySeatedCanvas(destination.AdoptedCanvases[k], root)
                        && destination.AdoptedCanvases[k].OriginalSortingOrder == other.OriginalSortingOrder)
                    { first = false; break; }
                if (first) offset++;
            }
            Hands.Interact.UguiPokeSurfaces.RegisterSeatedNested(destination.HostCanvas, record.Canvas, offset);
        }
    }

    private static bool IsOrdinarySeatedCanvas(NestedCanvasRecord record, RectTransform root) =>
        record.Canvas != null && BelongsToSeatedSubtree(record.Canvas.transform, root)
        && !record.KeepOverrideSorting && !record.ConcededOverrideSorting;

    /// <summary>Resolve only a transferred dialog's nested raycaster against the stable
    /// tier of its physical host. Canvas live ordering follows eye distance and may be
    /// below a modal host's input tier (1000); without this, native host paper wins the
    /// cross-raycaster comparison over the visible confirmation buttons. Drawing and
    /// native disablement remain untouched; the ordinary modal X stays above this tier.</summary>
    private static bool TrySeatedSubtreeBaseSortingOrder(GameObject raycasterGo, out int baseOrder)
    {
        baseOrder = 0;
        Canvas? nested = raycasterGo.GetComponent<Canvas>();
        if (!Hands.Interact.UguiPokeSurfaces.TrySeatedHostOf(nested, out Canvas? host, out int offset)) return false;
        for (int i = 0; i < Active.Count; i++)
        {
            ConvertedPanel panel = Active[i];
            if (!ReferenceEquals(panel.HostCanvas, host)) continue;
            for (int r = 0; r < panel.AdoptedCanvases.Count; r++)
            {
                NestedCanvasRecord record = panel.AdoptedCanvases[r];
                if (!ReferenceEquals(record.Canvas, nested)) continue;
                if (record.KeepOverrideSorting || record.ConcededOverrideSorting) return false;
                baseOrder = panel.BaseSortingOrder + offset; return true;
            }
        }
        return false;
    }

    private static bool BelongsToSeatedSubtree(Transform candidate, Transform root) =>
        ReferenceEquals(candidate, root) || candidate.IsChildOf(root);
}
