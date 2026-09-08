using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// Keep the native ability-card FX in the coordinate and draw space of its adopted face.
///
/// Hardware report 2026-09-08 item 5a: short-rest burning was audible but invisible.
/// The owner's LeapingCleave log proves a 1.99 s native ramp ending at _GreyOut 1.00
/// and a real VR-card flight. It does not prove that the shader drew the ramp.
/// CardEffects.Initialize records the original screen-hand origin in _PosAndBounds;
/// CardFace subsequently centres that very face on its own world canvas. Nonzero
/// screen coordinates are still wrong there. The remote face already remeasures this
/// footprint in RemoteCardArt.TryMeasureFxFootprint; only the local ability face lacked it.
///
/// There is a second conversion requirement: the native flame shader's authored
/// Geometry queue draws before the transparent card on a world canvas. The mirror
/// already places it one queue after the print (at most 3090, below the ghost hand).
/// Apply that identical ordering here while leaving the native timeline, tint, texture,
/// blend and hierarchy untouched. These are presentation corrections, not another burn.
///
/// Only initialized, adopted CardEffects materials are written. Initialize creates a
/// private Material for EVERY non-null imgComp image and fgFx (CardEffects.cs:316-340),
/// before any timeline runs. No authored/shared material is touched. We retain each
/// original footprint/queue and restore those fields when CardFace yields or returns
/// the face; every other property keeps the native timeline's latest value.
/// </summary>
internal sealed class AdoptedCardFxSpace
{
    private static readonly int BoundsId = Shader.PropertyToID("_PosAndBounds");
    private static readonly int GreyId = Shader.PropertyToID("_GreyOut");
    private const int FlameQueueCeiling = 3090;
    private readonly Dictionary<Material, Vector4> _bounds = new();
    private readonly Dictionary<Material, int> _queues = new();
    private Material? _measuredFlame;
    private int _faceQueue = -1;
    private bool _reported;

    internal void Maintain(FullAbilityCard? face, RectTransform? host)
    {
        CardEffects? fx = face != null ? face.cardEffects : null;
        if (face == null || host == null || fx == null || !fx.initialized
            || fx.imgComp == null || !CardArtGuard.IsAdopted(face))
            return;
        Canvas? canvas = host.GetComponent<Canvas>();
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            return;
        RectTransform? cardRect = face.transform as RectTransform;
        if (cardRect == null || cardRect.parent != host)
            return; // a dialog has taken ownership; CardFace must reclaim it first

        // The native header is the card-sized plate Initialize uses, not the effects
        // component's own child rect. The mirror finds this same largest FX plate.
        Image? header = fx._headerImage;
        if (header == null)
            return;
        Vector2 size = header.rectTransform.rect.size;
        if (size.x < 1f || size.y < 1f)
            return; // never feed a degenerate footprint to the card shader
        Vector3 origin = host.InverseTransformPoint(cardRect.position);
        var footprint = new Vector4(origin.x, origin.y, size.x, size.y);
        int changed = 0;
        Vector4 inherited = default;
        for (int i = 0; i < fx.imgComp.Length; i++)
        {
            Image img = fx.imgComp[i];
            Material? material = img != null ? img.material : null;
            if (material == null || !material.HasProperty(BoundsId) || !material.HasProperty(GreyId))
                continue;
            Vector4 current = material.GetVector(BoundsId);
            if (current == footprint)
                continue;
            if (!_bounds.ContainsKey(material))
                _bounds.Add(material, current);
            inherited = current;
            material.SetVector(BoundsId, footprint);
            img!.SetMaterialDirty(); // schedule the graphic's renderer binding update
            changed++;
        }

        Image? flame = fx._uiFxOverlay;
        Material? flameMaterial = flame != null ? flame.material : null;
        if (flameMaterial != null && !ReferenceEquals(flameMaterial, _measuredFlame))
        {
            _measuredFlame = flameMaterial;
            _faceQueue = -1;
            Graphic[] graphics = face.GetComponentsInChildren<Graphic>(includeInactive: true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || ReferenceEquals(graphic, flame) || graphic.material == null)
                    continue;
                _faceQueue = Mathf.Max(_faceQueue, graphic.material.renderQueue);
            }
        }
        if (flameMaterial != null && _faceQueue >= 0 && flameMaterial.renderQueue <= _faceQueue)
        {
            int wantedQueue = Mathf.Min(_faceQueue + 1, FlameQueueCeiling);
            if (flameMaterial.renderQueue != wantedQueue)
            {
                if (!_queues.ContainsKey(flameMaterial))
                    _queues.Add(flameMaterial, flameMaterial.renderQueue);
                flameMaterial.renderQueue = wantedQueue;
                flame!.SetMaterialDirty();
            }
        }
        if (!_reported && (changed > 0 || _queues.Count > 0))
        {
            _reported = true;
            VRLog.Note("Cards", $"LOCAL CARD FX SPACE: '{face.name}' — corrected {changed} native " +
                $"image footprint(s), inherited {inherited}, hosted {footprint}; flame queue " +
                $"{(flameMaterial != null ? flameMaterial.renderQueue : -1)}, print queue {_faceQueue}. " +
                "The original screen origin and opaque flame queue cannot describe this world canvas. " +
                "Native material state is live; only our footprint/queue are restored at release. " +
                "This measures the render inputs, not the final headset pixels.");
        }
    }

    internal void Restore()
    {
        foreach (KeyValuePair<Material, Vector4> entry in _bounds)
            if (entry.Key != null)
                entry.Key.SetVector(BoundsId, entry.Value);
        foreach (KeyValuePair<Material, int> entry in _queues)
            if (entry.Key != null)
                entry.Key.renderQueue = entry.Value;
        _bounds.Clear();
        _queues.Clear();
        _measuredFlame = null;
        _faceQueue = -1;
        _reported = false;
    }
}
