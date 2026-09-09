using ScenarioRuleLibrary;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net;

internal sealed partial class RemoteCardArt
{
    private CardAppearanceBindings? _nativeBindings;
    private readonly Dictionary<Graphic, Material> _nativeMaterials = new();
    private int _nativePlayer;
    private bool _nativeOutputApplied;
    private byte _nativeLastSourceList;
    private CardAppearanceNode[]? _nativeDefaults;
    private CardAppearanceNode[]? _nativeExtraDefaults;
    private CardAppearanceState? _localNativeFrame;
    private readonly List<Image> _nativeBoundsImages = new();
    private bool ExplicitFlightOwnsLook => Surface == FxSurface.Flight || Surface == FxSurface.CardFlight;
    private CPlayerActor? _nativeActor;
    private CAbilityCard? _nativeCard;
    internal void SetNativeAppearance(int playerId, CPlayerActor? actor, CAbilityCard? card)
    {
        _localNativeFrame = null;
        bool changed = _nativePlayer != playerId || !ReferenceEquals(_nativeActor, actor) || !ReferenceEquals(_nativeCard, card);
        _nativePlayer = playerId; _nativeActor = actor; _nativeCard = card;
        if (changed)
        {
            _nativeOutputApplied = false;
            if (!ExplicitFlightOwnsLook) ClearPendingNativeAppearance();
        }
        ApplyNativeAppearance();
    }
    private bool TryCaptureNativeDefaults()
    {
        if (_nativeDefaults != null || _nativeBindings == null) return true;
        if (_cloneFace == null) return false;
        // ImageAddressableLoader hides its content groups while art is in flight. A snapshot
        // taken directly after OnEnable can freeze those temporary zeros as the reset baseline;
        // the next authority change then hides a fully loaded card forever (MB490 regression).
        if (_cloneFace.headerImage == null || _cloneFace.headerImage.sprite == null
            || _cloneFace.topActionButton?.actionButton?.image?.sprite == null
            || !_cloneFace.isLongRestCard && _cloneFace.bottomActionButton?.actionButton?.image?.sprite == null)
            return false;
        foreach (var loader in _cloneFace.GetComponentsInChildren<ImageAddressableLoader>(true))
            if (loader != null && loader.ReferenceCount > 0) return false;
        _nativeDefaults = _nativeBindings.Capture();
        _nativeExtraDefaults = _nativeBindings.CaptureExtraGroups();
        return true;
    }
    private void ClearPendingNativeAppearance()
    {
        // The receiver's pooled widget may still be spent after a rest. Before the first owner
        // frame is available it is never an authority for a newly exposed card's decoration.
        if (_clone == null || _nativeBindings == null) return;
        // Native playback owns independent per-role materials, including the low-detail shader.
        // Building the legacy burn rig here would first paint invented burn constants and could
        // refuse the low-detail card before its inherited ghost/fire had ever been cleared.
        ClearAbilityCardFx();
        _nativeBindings.RefreshGroups();
        if (_nativeDefaults != null)
            foreach (var node in NativeNodes(_nativeDefaults, _nativeExtraDefaults))
            {
                if (node.Role >= 12)
                {
                    if (!_nativeBindings.Groups.TryGetValue(node.Binding, out CanvasGroup group) || group == null) continue;
                    group.alpha = node.Values[0]; group.enabled = (node.Flags & 2) != 0;
                    group.ignoreParentGroups = (node.Flags & 4) != 0;
                    group.gameObject.SetActive((node.Flags & 1) != 0);
                }
                else if (_nativeBindings.Graphics[node.Role] is Graphic graphic)
                {
                    graphic.color = new Color(node.Values[0], node.Values[1], node.Values[2], node.Values[3]);
                    graphic.canvasRenderer.SetColor(new Color(node.Values[4], node.Values[5], node.Values[6], node.Values[7]));
                    graphic.enabled = (node.Flags & 2) != 0;
                    graphic.gameObject.SetActive((node.Flags & 1) != 0);
                    if (graphic is TextMeshProUGUI text) text.enableVertexGradient = (node.Flags & 4) != 0;
                }
            }
        for (int role = 0; role < _nativeBindings.Graphics.Length; role++)
        {
            if (_nativeBindings.Graphics[role] is not Graphic graphic || role >= 7 && role != 11) continue;
            if (!_nativeMaterials.TryGetValue(graphic, out var material) || material == null)
            {
                material = new Material(graphic.material) { name = graphic.material.name + " (VR-native-card)" };
                _ownedMaterials.Add(material); _nativeMaterials[graphic] = material;
            }
            graphic.material = material;
            // RestoreCard clears all four terms; leaving _Burn behind is a low/high pooled seam.
            SetFloatIfPresent(material, GreyOutId, 0f); SetFloatIfPresent(material, FlowId, 0f);
            SetFloatIfPresent(material, DissolveId, 0f); SetFloatIfPresent(material, BurnId, 0f);
            SetFloatIfPresent(material, FxAnimId, 0f);
        }
    }
    /// <summary>Freeze original output for a local flight; the new flight owns root pose and visibility.</summary>
    internal void SetLocalNativeAppearance(FullAbilityCard source)
    {
        if (source == null || source.cardEffects == null) return;
        var bindings = new CardAppearanceBindings(source.cardEffects);
        _localNativeFrame = new CardAppearanceState { Nodes = bindings.Capture(detachedRoot: true),
            ExtraGroups = bindings.CaptureExtraGroups(detachedRoot: true) };
        ApplyNativeAppearance();
    }
    internal void ApplyNativeAppearance()
    {
        if (_nativeBindings == null || _clone == null || _host == null || !_host.activeInHierarchy) return;
        if (!TryCaptureNativeDefaults()) return;
        if (_localNativeFrame != null)
        {
            ApplyNativeFrame(_localNativeFrame, _localNativeFrame, 1f);
            return;
        }
        if (_nativeActor == null || _nativeCard == null) return;
        if (RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, _nativeActor) == RevealGate.CardFaceSource.None) return;
        if (!CardAppearanceMirror.TryGet(_nativePlayer, _nativeActor, _nativeCard, out var from, out var to, out float progress))
        {
            // A discarded/active card changes its address when it starts burning. The old
            // sample is correctly invalidated, but erasing its already drawn wash would flash
            // a fresh blue card until the first owner sample at the lost address arrives.
            if (_nativeOutputApplied && RetainSpentRecessDuringBurn()) return;
            if (_nativeOutputApplied && !ExplicitFlightOwnsLook) ClearPendingNativeAppearance();
            _nativeOutputApplied = false;
            return;
        }
        ApplyNativeFrame(from!, to!, progress);
    }
    private bool RetainSpentRecessDuringBurn()
    {
        if (Surface != FxSurface.Recess || _nativeCard == null || _nativeActor?.CharacterClass == null
            || _nativeLastSourceList != NetProtocol.HeldFaceListDiscard && _nativeLastSourceList != NetProtocol.HeldFaceListActive)
            return false;
        var cards = _nativeActor.CharacterClass;
        // Identity/actor changes already relinquish _nativeOutputApplied in SetNativeAppearance.
        // Actual recovery immediately releases the old paint even if a delayed lost stamp remains.
        return !cards.HandAbilityCards.Contains(_nativeCard) && !cards.RoundAbilityCards.Contains(_nativeCard)
            && !cards.ActivatedCards.Contains(_nativeCard)
            && (cards.LostAbilityCards.Contains(_nativeCard) || cards.PermanentlyLostAbilityCards.Contains(_nativeCard));
    }
    private float SpentBurnFloor(Image? image, int channel, float ramp, CardAppearanceState? spent)
    {
        if (spent == null || image == null || _nativeBindings == null) return ramp;
        foreach (var node in spent.Nodes)
            if (node.Role < 7 && ReferenceEquals(_nativeBindings.Graphics[node.Role], image)
                && (node.Mask & (1u << channel)) != 0)
                return Mathf.Max(ramp, node.Values[8 + channel]);
        return ramp;
    }
    private static IEnumerable<CardAppearanceNode> NativeNodes(CardAppearanceNode[] nodes, CardAppearanceNode[]? extra)
    {
        foreach (var node in nodes) yield return node;
        if (extra != null) foreach (var node in extra) yield return node;
    }
    private void ApplyNativeFrame(CardAppearanceState from, CardAppearanceState to, float progress)
    {
        if (_nativeBindings == null) return;
        // Validate the complete native role set before painting anything. A different prefab must
        // not receive a partial card that mixes owner output with this client's pooled defaults.
        _nativeBindings.RefreshGroups();
        foreach (var node in NativeNodes(to.Nodes, to.ExtraGroups))
        {
            if (node.Role < 12 ? _nativeBindings.Graphics[node.Role] == null : !_nativeBindings.Groups.ContainsKey(node.Binding)) return;
            if (node.Role < 7 && ((node.Flags & 16) != 0 ? _nativeBindings.LowMaterial : CardAppearanceBindings.AuthoredMaterial(node.Role)) == null) return;
        }
        _nativeBoundsImages.Clear();
        bool needsBounds = false;
        foreach (var node in NativeNodes(to.Nodes, to.ExtraGroups))
        {
            if (node.Role >= 7) continue;
            Material template = (node.Flags & 16) != 0 ? _nativeBindings.LowMaterial! : CardAppearanceBindings.AuthoredMaterial(node.Role)!;
            needsBounds |= template.HasProperty(PosAndBoundsId);
            if (_nativeBindings.Graphics[node.Role] is Image image) _nativeBoundsImages.Add(image);
        }
        Vector4 nativeFootprint = default;
        if (needsBounds && !TryMeasureFxFootprint(_nativeBoundsImages, out nativeFootprint)) return;
        TakeFxLookHold();
        foreach (var node in NativeNodes(to.Nodes, to.ExtraGroups))
        {
            var old = node;
            foreach (var candidate in NativeNodes(from.Nodes, from.ExtraGroups)) if (candidate.Role == node.Role && candidate.Binding == node.Binding && candidate.Mask == node.Mask) { old = candidate; break; }
            float k = old.Flags == node.Flags ? progress : 1f;
            float V(int i) => Mathf.LerpUnclamped(old.Values[i], node.Values[i], k);
            Color C(int i) => new(V(i), V(i + 1), V(i + 2), V(i + 3));
            if (node.Role >= 12)
            {
                CanvasGroup group = _nativeBindings.Groups[node.Binding];
                group.alpha = V(0); group.enabled = (node.Flags & 2) != 0; group.ignoreParentGroups = (node.Flags & 4) != 0;
                group.gameObject.SetActive((node.Flags & 1) != 0);
                continue;
            }
            Graphic graphic = _nativeBindings.Graphics[node.Role]!;
            graphic.color = C(0); graphic.canvasRenderer.SetColor(C(4));
            graphic.enabled = (node.Flags & 2) != 0;
            graphic.gameObject.SetActive((node.Flags & 1) != 0);
            if (graphic is TextMeshProUGUI text) text.enableVertexGradient = (node.Flags & 4) != 0;
            if (node.Mask == 0 && node.Role >= 7) continue;
            Material template = node.Role < 7
                ? ((node.Flags & 16) != 0 ? _nativeBindings.LowMaterial! : CardAppearanceBindings.AuthoredMaterial(node.Role)!)
                : graphic.material;
            if (!_nativeMaterials.TryGetValue(graphic, out var material) || material == null || material.shader != template.shader)
            {
                if (material != null) { _ownedMaterials.Remove(material); Object.Destroy(material); }
                material = new Material(template) { name = template.name + " (VR-native-card)" };
                _ownedMaterials.Add(material); _nativeMaterials[graphic] = material;
            }
            graphic.material = material;
            // The clone moves/scales with fan/recess/held animations. Updating only when the
            // material was minted leaves a stale shader footprint after the first drawn frame.
            if (node.Role < 7 && material.HasProperty(PosAndBoundsId)) material.SetVector(PosAndBoundsId, nativeFootprint);
            for (int f = 0; f < CardAppearanceBindings.FloatIds.Length; f++)
                if ((node.Mask & (1u << f)) != 0 && material.HasProperty(CardAppearanceBindings.FloatIds[f]))
                    material.SetFloat(CardAppearanceBindings.FloatIds[f], V(8 + f));
            if ((node.Mask & (1u << 15)) != 0 && material.HasProperty(CardAppearanceBindings.BurnTint)) material.SetColor(CardAppearanceBindings.BurnTint, C(23));
            if ((node.Mask & (1u << 16)) != 0 && material.HasProperty(CardAppearanceBindings.FlameTint)) material.SetColor(CardAppearanceBindings.FlameTint, C(27));
            if ((node.Mask & (1u << 17)) != 0 && material.HasProperty(CardAppearanceBindings.Noise)) material.SetTextureScale(CardAppearanceBindings.Noise, new Vector2(V(31), V(32)));
            if (node.Role == 11 && material.HasProperty(CardAppearanceBindings.Particle))
            {
                material.SetTexture(CardAppearanceBindings.Particle, (node.Flags & 8) != 0 ? _nativeBindings.GhostTexture : _nativeBindings.BurnTexture);
                // Low-detail native cards need the same world-space draw ordering even when
                // the legacy high-detail burn rig had no matching _PosAndBounds images.
                if (graphic is Image flameImage) MeasureFaceQueue(flameImage);
                if (_faceQueue >= 0 && material.renderQueue <= _faceQueue)
                    material.renderQueue = Mathf.Min(_faceQueue + 1, FlameQueueCeiling);
            }
        }
        _nativeLastSourceList = NetProtocol.HeldFaceList(to.FaceCode);
        _nativeOutputApplied = true;
    }
}

/// <summary>Only presentation writes, after native layout and the ordinary mirror's state drivers.</summary>
internal sealed class RemoteCardAppearancePump : MonoBehaviour
{
    internal RemoteCardArt? Art;
    private void LateUpdate() => Art?.ApplyNativeAppearance();
}
