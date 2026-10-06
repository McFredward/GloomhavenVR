using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    // This unique original overlay is active exactly while its owner requests the
    // merchant's palm, including a parked card whose ghost has zero CanvasGroup alpha.
    // No card identity, local viewer distance or copied UI callback decides the pose.
    internal const string MerchantOfferingAddress = "merchant.offering|";
    private const float OfferingFreshSeconds = 3f;
    private static readonly Dictionary<int, TownServiceFrame> MerchantOfferings = new();
    private static readonly Dictionary<Transform, Transform> OfferedFrames = new();
    private static readonly HashSet<MotionSlot> ActiveOfferedFrames = new();
    private static readonly List<Transform> DeadOfferedFrames = new();

    // The real native fitter supplies both originals. Addresses and a viewer's
    // head are insufficient to identify a print while old cards return to a fan.
    internal static void RegisterOfferedFrame(Transform nativeHolder, Transform? physicalPrint)
    {
        if (ReferenceEquals(nativeHolder, null)) return;
        if (physicalPrint == null) OfferedFrames.Remove(nativeHolder);
        else if (nativeHolder != null) OfferedFrames[nativeHolder] = physicalPrint;
    }

    private static bool FindOfferedOriginal(LocalLane lane, Transform original, out LocalModule? module, out uint binding)
    {
        foreach (LocalModule candidate in lane.Modules.Values)
        {
            if (candidate.Last == null || candidate.Baseline == null) continue;
            int index = Array.IndexOf(candidate.Binding.Nodes, original);
            if (index < 0) continue;
            module = candidate; binding = candidate.Binding.Bindings[index]; return true;
        }
        module = null; binding = 0; return false;
    }

    private static void CollectOfferedFrameMotion(LocalLane lane, float now)
    {
        // Other lanes and returning cards have no native enhancement frame.
        ActiveOfferedFrames.Clear(); DeadOfferedFrames.Clear();
        foreach (var stale in OfferedFrames)
            if (stale.Key == null || stale.Value == null) DeadOfferedFrames.Add(stale.Key!);
        foreach (Transform stale in DeadOfferedFrames) OfferedFrames.Remove(stale);
        if (lane.Active && lane.Service == 3) foreach (var pair in OfferedFrames)
        {
            if (pair.Key == null || pair.Value == null || !ValidMotionScale(pair.Value.lossyScale)
                || !ValidMotionScale(pair.Key.lossyScale)
                || !FindOfferedOriginal(lane, pair.Key, out LocalModule? holder, out uint holderBinding)
                || !FindOfferedOriginal(lane, pair.Value, out LocalModule? print, out uint printBinding)
                || holder == print || !MotionSources.TryGetValue(holder!, out SourceMotion? motion)) continue;
            var entry = MotionHeader(holder!.Last!, 0, 9);
            entry.Visible = true; entry.Binding = holderBinding; entry.OfferedModule = print!.Id;
            entry.OfferedStructure = print.Last!.Structure; entry.OfferedBinding = printBinding;
            // Capture the fitted SOURCE pose, including the native pivot/inset and
            // depth bias. An observer neither refits an approximation nor faces
            // the copied widget towards its own head.
            entry.Numbers = ReadPose(pair.Key, pair.Value);
            Canvas? canvas = pair.Key.GetComponentInParent<Canvas>(true);
            if (holder!.Last!.HasCanvasFrame && canvas != null && canvas.transform != pair.Key)
            {
                if (!ValidMotionScale(canvas.transform.lossyScale)) continue;
                entry.HasCanvasFrame = true; entry.CanvasPose = ReadPose(canvas.transform, pair.Value);
                if (ReferenceEquals(pair.Key, holder.Binding.Root) && ReferenceEquals(pair.Key.parent, canvas.transform))
                {
                    // A rotated child of a stretched canvas cannot recover its
                    // exact local scale by dividing two lossy world scales. The
                    // receiver has this exact copied parent: retain source TRS.
                    entry.OfferedLocalScale = true;
                    Vector3 scale = pair.Key.localScale;
                    entry.Numbers[7] = scale.x; entry.Numbers[8] = scale.y; entry.Numbers[9] = scale.z;
                }
            }
            UpdateMotionSlot(motion, entry);
            MotionSlot slot = motion.Slots[entry.Key];
            ActiveOfferedFrames.Add(slot);
            if ((slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat)
                && !MotionLive.Contains(slot)) MotionLive.Add(slot);
        }
        foreach (var pair in MotionSources)
        {
            SourceMotion motion = pair.Value;
            if (pair.Key.Last == null) continue;
            var key = new TownServiceMotionKey(9, 0, pair.Key.Id, 0, 0, 0);
            if (!motion.Slots.TryGetValue(key, out MotionSlot? slot) || ActiveOfferedFrames.Contains(slot)) continue;
            if (slot.Entry.Visible)
            {
                var withdrawn = MotionHeader(pair.Key.Last!, 0, 9);
                withdrawn.Binding = slot.Entry.Binding; withdrawn.Numbers = IdentityPose();
                UpdateMotionSlot(motion, withdrawn);
            }
            if ((slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat)
                && !MotionLive.Contains(slot)) MotionLive.Add(slot);
        }
    }

    private static void ApplyOfferedFrames()
    {
        // Run once AFTER all independently authored native child/root tweens.
        // A hover-only update cannot put the ring and the physical card in
        // separate planes, even when the numeric budget delivers them separately.
        foreach (var pair in MotionRemoteFrames)
        {
            RemoteModule module = pair.Key;
            if (!module.Alive || !module.Host.activeInHierarchy) continue;
            foreach (MotionSlot slot in pair.Value.Slots)
            {
                TownServiceMotionEntry relation = slot.Entry;
                if (relation.Kind != 9 || relation.Lane != 0 || !relation.Visible
                    || !Remote.TryGetValue(pair.Value.Owner, out Dictionary<ushort, RemoteModule>? modules)
                    || !modules.TryGetValue(relation.OfferedModule, out RemoteModule? physical)
                    || !physical.Alive || !physical.Host.activeInHierarchy || physical.LastFrame == null
                    || physical.Session != relation.Session || physical.LastFrame.Structure != relation.OfferedStructure
                    || physical.LastFrame.Service != relation.Service || physical.LastFrame.PublicClaim != relation.PublicClaim) continue;
                int holderIndex = Array.IndexOf(module.Binding.Bindings, relation.Binding);
                int printIndex = Array.IndexOf(physical.Binding.Bindings, relation.OfferedBinding);
                if (holderIndex < 0 || printIndex < 0) continue;
                Transform holder = module.Binding.Nodes[holderIndex], print = physical.Binding.Nodes[printIndex];
                if (holder == null || print == null || holder.parent == null
                    || !ValidMotionScale(print.lossyScale) || !ValidMotionScale(holder.parent.lossyScale)) continue;
                // The source's converted canvas belongs to this same print. Its
                // native masks cannot stay in an independently interpolated world
                // frame while only the child ink follows the offered card.
                if (relation.HasCanvasFrame)
                {
                    if (module.AddedCanvas == null || module.Host.transform.parent == null
                        || !ValidMotionScale(module.Host.transform.parent.lossyScale)) continue;
                    ApplyOfferedPose(module.Host.transform, print, relation.CanvasPose);
                }
                if (relation.OfferedLocalScale && !ReferenceEquals(holder.parent, module.Host.transform)) continue;
                ApplyOfferedPose(holder, print, relation.Numbers, relation.OfferedLocalScale);
                slot.Dirty = false;
            }
        }
    }

    private static void ApplyOfferedPose(Transform target, Transform print, float[] pose, bool originalLocalScale = false)
    {
        target.position = print.TransformPoint(Position(pose));
        target.rotation = print.rotation * Rotation(pose);
        target.localScale = originalLocalScale ? Scale(pose)
            : DivideMotionScale(Vector3.Scale(print.lossyScale, Scale(pose)), target.parent.lossyScale);
    }

    internal static bool RemoteMerchantOffering
    {
        get
        {
            if (GloomhavenVR.WorldUI.TownServiceSharedCue.HasReadyMerchantVisitor) return true;
            float now = Time.unscaledTime;
            foreach (var pair in MerchantOfferings)
            {
                TownServiceFrame intent = pair.Value;
                if (InteractionOwner(1) != pair.Key
                    || !Sessions.TryGetValue(pair.Key, out TownServiceSessionInfo? session)
                    || !session.Active || session.Service != 1 || session.Session != intent.Session
                    || Array.BinarySearch(session.Modules, intent.Module) < 0
                    || now - session.LastSeenTime > OfferingFreshSeconds) continue;
                // Both sample times come from this same owner. Refreshing unrelated
                // inventory modules must not keep an old palm request alive forever.
                float age = now - session.ReceivedTime + session.SampleTime - intent.SampleTime;
                bool visible = intent.Visible;
                if (TryMerchantOfferingMotion(pair.Key, intent, now, out bool movedVisible, out float movedAge))
                { visible = movedVisible; age = movedAge; }
                if (visible && age <= OfferingFreshSeconds) return true;
            }
            return false;
        }
    }

    private static bool TryMerchantOfferingMotion(int peer, TownServiceFrame intent, float now,
        out bool visible, out float age)
    {
        visible = false; age = 0f;
        // The immutable original address/membership identifies this exact owner's
        // request even while its template assets are still initializing. A numeric
        // heartbeat refreshes only that request; unrelated inventory traffic cannot
        // keep the offered palm alive. Explicit withdrawal wins immediately.
        var key = new TownServiceMotionKey(1, 0, intent.Module, 0, 0, 0);
        if (!MotionPeers.TryGetValue(peer, out PeerMotion? source)
            || !source.Slots.TryGetValue(key, out MotionSlot? slot)
            || slot.Entry.Session != intent.Session || slot.Entry.Service != intent.Service
            || slot.Entry.Structure != intent.Structure || slot.Entry.PublicClaim != 0
            || slot.SampleTime < intent.SampleTime || now - slot.ReceivedAt > OfferingFreshSeconds) return false;
        visible = slot.Entry.Visible; age = Mathf.Max(0f, now - slot.ReceivedAt);
        return true;
    }

    private static void ObserveMerchantOffering(int peer, TownServiceFrame frame)
    {
        if (peer <= 0 || frame.Service != 1 || frame.TemplateAddress != MerchantOfferingAddress
            || !Sessions.TryGetValue(peer, out TownServiceSessionInfo? session)
            || !session.Active || session.Service != 1 || session.Session != frame.Session
            || Array.BinarySearch(session.Modules, frame.Module) < 0) return;
        if (MerchantOfferings.TryGetValue(peer, out TownServiceFrame? old)
            && old.Session == frame.Session && old.Sequence >= frame.Sequence) return;
        MerchantOfferings[peer] = frame;
    }

    private static void ReconcileMerchantOffering(int peer)
    {
        if (peer <= 0) return;
        MerchantOfferings.Remove(peer);
        // A module is allowed to arrive before its manifest, including late join.
        // Reconstruct only this owner's current membership; no template load is needed.
        if (Pending.TryGetValue(peer, out Dictionary<ushort, TownServiceFrame>? frames))
            foreach (TownServiceFrame frame in frames.Values) ObserveMerchantOffering(peer, frame);
    }
}
