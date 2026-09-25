using System;
using System.Collections.Generic;
using GloomhavenVR.Hands;
using GloomhavenVR.Rig;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

internal sealed partial class ItemsPile
{
    // Inspection has a real ItemChip owner for the normal hold/transfer/return machinery,
    // but never publishes the scenario inventory or owns its use recess.
    private readonly Action<ItemChip, Vector3>? _inspectionRelease;
    private VRHand? _inspectionGateHand;
    private readonly List<ItemChip> _inspectionRetiring = new();
    private readonly List<ItemChip> _inspectionPublished = new();
    private readonly HashSet<CItem> _inspectionPresent = new();
    private readonly HashSet<CItem> _inspectionDesired = new();
    private readonly List<ItemChip> _inspectionNew = new();
    private uint _inspectionRevision = uint.MaxValue;
    private bool _inspectionCensusDirty = true;
    private int _inspectionLayoutPivot = int.MinValue;
    private (float Radius, float Step, float Split, float Falloff, float Scale) _inspectionLayout;
    internal static ItemsPile? InspectionCurrent { get; private set; }
    internal IReadOnlyList<ItemChip> InspectionChips => _inspectionPublished;
    internal static ItemsPile CreateInspection(Action<ItemChip, Vector3> release)
    {
        var pile = new ItemsPile(release);
        pile.EnsureRoot();
        pile._root!.name = "GloomhavenVR.MerchantOwnedItems";
        pile._root.SetParent(VRRigDriver.RigRoot, false);
        if (pile._title != null) pile._title.gameObject.SetActive(false);
        return pile;
    }

    internal void TickInspection(IReadOnlyList<CItem> items, uint revision)
    {
        if (_inspectionRelease == null || _root == null) return;
        VRHand? gate = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        _inspectionGateHand = gate;
        bool show = gate != null && gate.HasPose && gate.Grabber.Held == null
            && (CardsConfig.RevealAlways || gate.PalmGate.IsOpen || HighlightedIndex >= 0);
        if (gate != null)
        {
            gate.PalmGate.Enabled = true;
            gate.PalmGate.EnterDegrees = CardsConfig.RevealEnterDegrees.Value;
            gate.PalmGate.ExitDegrees = CardsConfig.RevealExitDegrees.Value;
            gate.PalmGate.IgnoreWhenHandBusy = CardsConfig.RevealIgnoreWhenGrabbing.Value;
            Vector3 target = gate.Rig.PalmCenter.position
                + gate.Rig.PalmCenter.up * (CardsConfig.FanPalmOffset.Value * gate.WorldScale);
            float smoothing = CardsConfig.FanFollowSmoothing.Value;
            if (!IsOpen || smoothing <= 0f) _root.position = target;
            else if ((_root.position - target).magnitude > CardsConfig.FanFollowDeadzone.Value * gate.WorldScale)
                _root.position = Vector3.Lerp(_root.position, target, 1f - Mathf.Exp(-smoothing * Time.unscaledDeltaTime));
            // Parent armatures can be centimetres; only the tracked hand's world scale is a metre.
            float parentScale = _root.parent != null ? _root.parent.lossyScale.x : 1f;
            _root.localScale = Vector3.one * (gate.WorldScale / Mathf.Max(.0001f, parentScale));
            PileFanShape.FaceHead(_root);
        }
        bool changed = false;
        if (_inspectionRevision != revision)
        {
            _inspectionRevision = revision;
            _inspectionDesired.Clear();
            foreach (CItem item in items) _inspectionDesired.Add(item);
            _inspectionCensusDirty = true;
        }
        for (int i = _inspectionRetiring.Count - 1; i >= 0; i--)
            if (_inspectionRetiring[i] == null)
            { _inspectionRetiring.RemoveAt(i); _inspectionCensusDirty = changed = true; }
        if (show != IsOpen)
        {
            _inspectionCensusDirty = true;
            if (!show) ClearHandSweep();
        }
        IsOpen = show;
        if (show) UpdateHandSweep();
        // Membership is resolved only on a model revision, reveal edge, returned held card or
        // completed closing animation. Native inventory reads and set construction are not a
        // per-frame operation. Two copies retain their separate reference identities.
        if (_inspectionCensusDirty)
        {
            _inspectionCensusDirty = false;
            for (int i = _chips.Count - 1; i >= 0; i--)
            {
                ItemChip chip = _chips[i];
                if (chip == null) { _chips.RemoveAt(i); changed = true; continue; }
                if (chip.Holder == null && !chip.TownOffering && (!show || chip.Item == null || !_inspectionDesired.Contains(chip.Item)))
                { RetireInspectionAt(i); changed = true; }
            }
            if (show)
            {
                _inspectionPresent.Clear();
                foreach (ItemChip chip in _chips)
                    if (chip != null && chip.Item != null) _inspectionPresent.Add(chip.Item);
                foreach (ItemChip chip in _inspectionRetiring)
                    if (chip != null && chip.Item != null) _inspectionPresent.Add(chip.Item);
                foreach (CItem item in items)
                {
                    // Finish the closing wave before generating a second face for that copy.
                    if (!_inspectionPresent.Add(item)) continue;
                    ItemChip chip = ItemChip.Create(this, _root, item);
                    _chips.Add(chip); _inspectionNew.Add(chip); changed = true;
                }
            }
        }
        if (show)
        {
            var layout = (CardsConfig.FanRadius.Value * CardsConfig.FanRadiusFactor(PileKind.Items).Value,
                CardsConfig.FanStepDegrees(PileKind.Items).Value, CardsConfig.FanSplitMultiplier.Value,
                CardsConfig.FanSplitFalloff.Value, CardsConfig.FanHoverSplitScale.Value);
            int pivot = SplitPivotIndex;
            if (changed || pivot != _inspectionLayoutPivot || !_inspectionLayout.Equals(layout))
            {
                _inspectionLayout = layout; _inspectionLayoutPivot = pivot;
                Relayout(); // one complete layout after the batch has its final membership
            }
            foreach (ItemChip chip in _inspectionNew)
                chip.BeginInspectionEmerge(Vector3.zero, chip.transform.localPosition.x >= 0f ? 1f : -1f);
            _inspectionNew.Clear();
        }
        InspectionCurrent = show ? this : null;
        if (changed)
        {
            _inspectionPublished.Clear();
            _inspectionPublished.AddRange(_chips);
            _inspectionPublished.AddRange(_inspectionRetiring);
        }
        if (show) CardsDriver.StandDownForItemFanContact(VRHands.Primary, _inspectionPublished);
    }

    internal void ResumeInspection(ItemChip chip)
    {
        _inspectionCensusDirty = true;
        if (chip.Holder != null) return;
        if (!IsOpen && _root != null) chip.BeginCollapse(_root.position);
        else { chip.ResumeInspectionGlide(); Relayout(); }
    }

    private void RetireInspectionAt(int index)
    {
        ItemChip chip = _chips[index]; _chips.RemoveAt(index);
        if (chip == null) return;
        if (!chip.IsCollapsing) chip.BeginCollapse(_root!.position);
        _inspectionRetiring.Add(chip);
    }
    internal void DestroyInspection()
    {
        if (ReferenceEquals(InspectionCurrent, this)) InspectionCurrent = null;
        IsOpen = false; ClearHandSweep();
        // Context changes withdraw inspection, never perform a transaction through release.
        foreach (ItemChip chip in _chips)
            if (chip != null && chip.Holder != null) chip.Holder.Grabber.CancelAll();
        ClearChips();
        foreach (ItemChip chip in _inspectionRetiring)
            if (chip != null) UnityEngine.Object.DestroyImmediate(chip.gameObject);
        _inspectionRetiring.Clear(); _inspectionPublished.Clear(); _inspectionNew.Clear();
        _inspectionDesired.Clear(); _inspectionPresent.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        _root = null; _title = null;
    }
}
