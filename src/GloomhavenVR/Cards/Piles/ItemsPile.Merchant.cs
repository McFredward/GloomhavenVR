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

    internal void TickInspection(IReadOnlyList<CItem> items)
    {
        if (_inspectionRelease == null || _root == null) return;
        VRHand? gate = VRHands.Primary == VRHands.Left ? VRHands.Right : VRHands.Left;
        _inspectionGateHand = gate;
        bool show = gate != null && gate.HasPose && gate.Grabber.Held == null
            && (CardsConfig.RevealAlways || gate.PalmGate.IsOpen || HighlightedIndex >= 0);
        if (gate != null)
        {
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
        for (int i = _inspectionRetiring.Count - 1; i >= 0; i--)
            if (_inspectionRetiring[i] == null) _inspectionRetiring.RemoveAt(i);
        if (!show && IsOpen)
        {
            IsOpen = false; ClearHandSweep();
            for (int i = _chips.Count - 1; i >= 0; i--)
                if (_chips[i].Holder == null) RetireInspectionAt(i);
        }
        IsOpen = show;
        // Polls are supplied by the map model owner. Diff by object identity, never item ID:
        // two copies are two cards and a sale must remove precisely the selected copy.
        for (int i = _chips.Count - 1; i >= 0; i--)
        {
            ItemChip chip = _chips[i];
            if (chip == null) { _chips.RemoveAt(i); continue; }
            if (chip.Holder == null && (!Contains(items, chip.Item) || !show)) RetireInspectionAt(i);
        }
        if (show)
        {
            foreach (CItem item in items)
            {
                bool exists = false;
                foreach (ItemChip chip in _chips) if (ReferenceEquals(chip.Item, item)) { exists = true; break; }
                // Finish a previous closing wave before generating a second face for that item.
                foreach (ItemChip chip in _inspectionRetiring)
                    if (chip != null && ReferenceEquals(chip.Item, item)) { exists = true; break; }
                if (!exists)
                {
                    ItemChip chip = ItemChip.Create(this, _root, item);
                    _chips.Add(chip); Relayout();
                    chip.BeginEmerge(Vector3.zero, 0f, _chips.Count % 2 == 0 ? -1f : 1f);
                }
            }
            UpdateHandSweep(); Relayout();
        }
        InspectionCurrent = show ? this : null;
        _inspectionPublished.Clear();
        _inspectionPublished.AddRange(_chips);
        _inspectionPublished.AddRange(_inspectionRetiring);
    }

    private static bool Contains(IReadOnlyList<CItem> items, CItem? item)
    {
        foreach (CItem candidate in items) if (ReferenceEquals(candidate, item)) return true;
        return false;
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
        _inspectionRetiring.Clear(); _inspectionPublished.Clear();
        if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
        _root = null; _title = null;
    }
}
