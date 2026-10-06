using System;
using System.Collections.Generic;
using UnityEngine;
using GloomhavenVR.Core;

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
    private sealed class OfferedFrameMotion
    {
        internal TownServiceMotionEntry Target = null!;
        internal ulong Sequence;
        internal float SampleTime, Started, Duration;
        internal float[] From = Array.Empty<float>();
    }
    private static readonly Dictionary<RemoteModule, OfferedFrameMotion> OfferedRemoteMotion = new();
    private static readonly List<RemoteModule> DeadOfferedRemoteMotion = new();
    private static readonly List<RemoteModule> OfferedApplyOrder = new();
    private static float _offeredDiagnosticAt;


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
                || !FindOfferedOriginal(lane, pair.Value, out LocalModule? print, out uint printBinding)) continue;
            // The real native holder is partitioned: Aura branches and selectable
            // ability rows can each have their own detached observer canvas/root.
            // Build629 only tied the module containing CardHilight to the print;
            // fitting that ancestor cannot move a separately mounted descendant.
            // Retain each actual source descendant's relation, including native
            // partition pivots and its own enclosing converted canvas.
            foreach (LocalModule holder in lane.Modules.Values)
            {
                Transform original = holder.Binding.Root;
                if (holder == print || holder.Last == null || holder.Baseline == null || original == null
                    || !(original == pair.Key || original.IsChildOf(pair.Key))
                    || !ValidMotionScale(original.lossyScale)
                    || !MotionSources.TryGetValue(holder, out SourceMotion? motion)) continue;
                var entry = MotionHeader(holder.Last, 0, 9);
                entry.Visible = true; entry.Binding = holder.Binding.Bindings[0]; entry.OfferedModule = print!.Id;
                entry.OfferedStructure = print.Last!.Structure; entry.OfferedBinding = printBinding;
                entry.Numbers = ReadPose(original, pair.Value);
                Canvas? canvas = original.GetComponentInParent<Canvas>(true);
                if (holder.Last.HasCanvasFrame && canvas != null && canvas.transform != original)
                {
                    if (!ValidMotionScale(canvas.transform.lossyScale)) continue;
                    entry.HasCanvasFrame = true; entry.CanvasPose = ReadPose(canvas.transform, pair.Value);
                    if (ReferenceEquals(original.parent, canvas.transform))
                    {
                        // A rotated child of a stretched canvas requires its exact
                        // source local TRS, not division of two lossy world scales.
                        entry.OfferedLocalScale = true;
                        Vector3 scale = original.localScale;
                        entry.Numbers[7] = scale.x; entry.Numbers[8] = scale.y; entry.Numbers[9] = scale.z;
                    }
                }
                UpdateMotionSlot(motion, entry);
                MotionSlot slot = motion.Slots[entry.Key];
                ActiveOfferedFrames.Add(slot);
                if ((slot.Dirty || now - slot.SentAt >= TownServiceMotionCodec.Heartbeat)
                    && !MotionLive.Contains(slot)) MotionLive.Add(slot);
            }
        }
        if (lane.Active && lane.Service == 3 && OfferedFrames.Count != 0
            && VRLog.WantsDebug && now >= _offeredDiagnosticAt)
        {
            _offeredDiagnosticAt = now + 5f;
            VRLog.Info("TownMotion", "TOWN OFFERED FRAME registered=" + OfferedFrames.Count
                + " linkedOriginalModules=" + ActiveOfferedFrames.Count + " session=" + lane.Session + ".");
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

    private static void ApplyOfferedFrames(float now)
    {
        // Run once AFTER all independently authored native child/root tweens.
        // A hover-only update cannot put the ring and the physical card in
        // separate planes, even when the numeric budget delivers them separately.
        DeadOfferedRemoteMotion.Clear();
        foreach (RemoteModule old in OfferedRemoteMotion.Keys) if (!old.Alive) DeadOfferedRemoteMotion.Add(old);
        foreach (RemoteModule old in DeadOfferedRemoteMotion) OfferedRemoteMotion.Remove(old);
        OfferedApplyOrder.Clear();
        foreach (RemoteModule candidate in MotionRemoteFrames.Keys)
            if (candidate.Alive && candidate.Host.activeInHierarchy) OfferedApplyOrder.Add(candidate);
        OfferedApplyOrder.Sort(CompareOfferedParentage);
        foreach (RemoteModule module in OfferedApplyOrder)
        {
            RemoteMotion composed = MotionRemoteFrames[module];
            if (!module.Alive || !module.Host.activeInHierarchy) continue;
            foreach (MotionSlot slot in composed.Slots)
            {
                TownServiceMotionEntry relation = slot.Entry;
                if (relation.Kind != 9 || relation.Lane != 0 || !relation.Visible
                    || !Remote.TryGetValue(composed.Owner, out Dictionary<ushort, RemoteModule>? modules)
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
                OfferedFrameMotion motion = PrepareOfferedFrameMotion(module, slot, now);
                float blend = motion.Duration > 0f ? Mathf.Clamp01((now - motion.Started) / motion.Duration) : 1f;
                if (relation.HasCanvasFrame)
                {
                    if (module.AddedCanvas == null || module.Host.transform.parent == null
                        || !ValidMotionScale(module.Host.transform.parent.lossyScale)) continue;
                    ApplyOfferedPose(module.Host.transform, print, relation.CanvasPose);
                }
                if (relation.OfferedLocalScale && !ReferenceEquals(holder.parent, module.Host.transform)) continue;
                ApplyOfferedPose(holder, print, relation.Numbers, relation.OfferedLocalScale, motion.From, blend);
                slot.Dirty = false;
            }
        }
    }

    private static int CompareOfferedParentage(RemoteModule first, RemoteModule second)
    {
        // Detached canvases can still mount beneath another native partition.
        // Preserve parent-before-child writes even when packets/builds admitted
        // those modules in a different order; otherwise the ancestor rotates an
        // already fitted child's frame a second time.
        int a = 0, b = 0;
        for (Transform? node = first.Host.transform.parent; node != null; node = node.parent) a++;
        for (Transform? node = second.Host.transform.parent; node != null; node = node.parent) b++;
        int order = a.CompareTo(b);
        return order != 0 ? order : first.LastFrame!.Module.CompareTo(second.LastFrame!.Module);
    }

    private static OfferedFrameMotion PrepareOfferedFrameMotion(RemoteModule module, MotionSlot slot, float now)
    {
        TownServiceMotionEntry target = slot.Entry;
        if (!OfferedRemoteMotion.TryGetValue(module, out OfferedFrameMotion? motion))
            OfferedRemoteMotion.Add(module, motion = new OfferedFrameMotion());
        if (motion.Sequence == slot.ReceivedSequence) return motion;
        TownServiceMotionEntry? old = motion.Target;
        bool samePrint = old != null && old.Session == target.Session && old.Structure == target.Structure
            && old.Binding == target.Binding && old.OfferedModule == target.OfferedModule
            && old.OfferedStructure == target.OfferedStructure && old.OfferedBinding == target.OfferedBinding
            && old.HasCanvasFrame == target.HasCanvasFrame && old.OfferedLocalScale == target.OfferedLocalScale;
        float blend = motion.Duration > 0f ? Mathf.Clamp01((now - motion.Started) / motion.Duration) : 1f;
        if (samePrint && Quaternion.Angle(Rotation(old!.Numbers), Rotation(target.Numbers)) < .001f)
        {
            // Moving the enclosing source canvas is geometric registration, not
            // a new native spin sample. Preserve the ring's existing clock.
            motion.Target = target; motion.Sequence = slot.ReceivedSequence; return motion;
        }
        motion.From = samePrint ? BlendOfferedPose(motion.From, old!.Numbers, blend) : target.Numbers;
        // Each native partition owns its original rotation clock. An unrelated
        // hover/header cannot restart its ring tween, and an admitted descendant
        // relation must not snap over the normal per-render child interpolation.
        float interval = slot.SampleTime > motion.SampleTime ? slot.SampleTime - motion.SampleTime : TownServiceMotionCodec.SendInterval;
        Quaternion delta = samePrint ? Rotation(target.Numbers) * Quaternion.Inverse(Rotation(motion.From)) : Quaternion.identity;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        // Preserve only the native planar spin. Canvas registration and fitted
        // nonuniform scale must use one exact same target basis as the print;
        // tweening those mount matrices independently shears the selected rows.
        motion.Duration = samePrint && angle > .001f && Mathf.Abs(axis.z) > .999f
            ? Mathf.Clamp(interval * 1.1f, 1f / 90f, 1.5f) : 0f;
        motion.Target = target; motion.Sequence = slot.ReceivedSequence; motion.SampleTime = slot.SampleTime; motion.Started = now;
        return motion;
    }

    private static float[] BlendOfferedPose(float[] from, float[] to, float blend)
    {
        if (blend >= 1f) return to;
        Vector3 p = Vector3.LerpUnclamped(Position(from), Position(to), blend);
        Quaternion q = Quaternion.SlerpUnclamped(Rotation(from), Rotation(to), blend);
        Vector3 s = Vector3.LerpUnclamped(Scale(from), Scale(to), blend);
        return new[] { p.x, p.y, p.z, q.x, q.y, q.z, q.w, s.x, s.y, s.z };
    }

    private static void ApplyOfferedPose(Transform target, Transform print, float[] pose, bool originalLocalScale = false,
        float[]? from = null, float blend = 1f)
    {
        Vector3 position = Position(pose), scale = Scale(pose);
        Quaternion rotation = Rotation(pose);
        if (from != null && blend < 1f)
        {
            rotation = Quaternion.SlerpUnclamped(Rotation(from), rotation, blend);
        }
        target.position = print.TransformPoint(position);
        target.rotation = print.rotation * rotation;
        target.localScale = originalLocalScale ? scale
            : DivideMotionScale(Vector3.Scale(print.lossyScale, scale), target.parent.lossyScale);
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
