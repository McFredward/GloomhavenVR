using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UI;
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
    private static readonly Dictionary<Transform, Transform> OfferedPhysicalFrames = new();
    private readonly struct OfferedPhysicalMount
    {
        internal readonly Transform Parent;
        internal readonly int Owner;
        internal OfferedPhysicalMount(Transform parent, int owner) { Parent = parent; Owner = owner; }
    }
    private static readonly Dictionary<RemoteModule, OfferedPhysicalMount> OfferedPhysicalMounts = new();
    private static readonly Dictionary<RemoteModule, float> OfferedPhysicalReturns = new();
    private static readonly HashSet<MotionSlot> ActiveOfferedFrames = new();
    private static readonly List<Transform> DeadOfferedFrames = new();
    private sealed class OfferedFrameMotion
    {
        internal TownServiceMotionEntry Target = null!;
        internal ulong Sequence;
        internal float SampleTime, Started, Duration;
        internal float[] From = Array.Empty<float>();
        internal NativeRingClock? Ring;
    }
    private readonly struct NativeRingInfo
    {
        internal readonly int Root;
        internal readonly float Rate;
        internal NativeRingInfo(int root, float rate) { Root = root; Rate = rate; }
    }
    private static readonly Dictionary<string, NativeRingInfo> NativeRingRates = new();
    internal static bool ReadNativeRingRate(Transform original, out Transform? aura, out float rate)
    {
        aura = null; rate = 0f;
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
        foreach (MonoBehaviour controller in original.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (controller == null || controller.GetType().Name != "UIEnchantressEffect") continue;
            Type type = controller.GetType();
            if (type.GetField("enchantressEffect", fields)?.GetValue(controller) is not GameObject effect
                || !(effect.transform == original || effect.transform.IsChildOf(original))
                || type.GetField("rotationTime", fields)?.GetValue(controller) is not float duration
                || type.GetField("rotationSpeed", fields)?.GetValue(controller) is not float speed
                || float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f
                || float.IsNaN(speed) || float.IsInfinity(speed)) return false;
            // Exact native serialized UIEnchantressEffect.Rotate: speed*360/time.
            // Capture before neutralization; observers never run its world-Z callback.
            rate = speed * 360f / duration;
            if (float.IsNaN(rate) || float.IsInfinity(rate)) return false;
            aura = effect.transform; return true;
        }
        return false;
    }
    private static void PrepareNativeRingRate(string key, byte service, Transform original,
        Func<Transform, bool>? exclude, Transform? aura, float? rate)
    {
        if (service != 3) return;
        if (!rate.HasValue && ReadNativeRingRate(original, out aura, out float liveRate)) rate = liveRate;
        if (!rate.HasValue || float.IsNaN(rate.Value) || float.IsInfinity(rate.Value) || aura == null || aura.GetComponent<Graphic>() != null) return;
        using var binding = new TownServiceBinding(original, exclude);
        int index = Array.IndexOf(binding.Nodes, aura);
        if (index >= 0) NativeRingRates[key] = new NativeRingInfo(index, rate.Value);
    }
    private sealed class NativeRingClock
    {
        private readonly Transform _root;
        private readonly Transform[] _ink;
        private readonly float[] _original;
        private readonly bool[] _initialized;
        private readonly float _rate;
        private readonly uint _rootBinding;
        private TownServiceFrame? _authored;
        private float[]? _rootPose;
        private Transform? _print;
        private double _started;
        internal NativeRingClock(Transform[] nodes, uint[] bindings, NativeRingInfo info)
        {
            _root = nodes[info.Root]; _rootBinding = bindings[info.Root];
            var ink = new List<Transform>();
            foreach (Transform node in nodes) if (node != null && (node == _root || node.IsChildOf(_root))
                && node.GetComponent<Graphic>() != null) ink.Add(node);
            _ink = ink.ToArray(); _original = new float[_ink.Length]; _initialized = new bool[_ink.Length]; _rate = info.Rate;
        }
        internal void Reset() { _print = null; Array.Clear(_initialized, 0, _initialized.Length); }
        internal void Draw(Transform holder, Transform print, double now, Vector3 authoredScale, TownServiceFrame authored)
        {
            Transform root = _root;
            if (root == null) return;
            if (!ReferenceEquals(_authored, authored))
            {
                _authored = authored; _rootPose = null;
                foreach (TownServiceNode node in authored.Nodes)
                    if (node.Binding == _rootBinding && node.Values.TryGetValue(TownServiceProperty.Transform, out TownServiceValue? pose)
                        && pose.Numbers.Length >= 10) { _rootPose = pose.Numbers; break; }
            }
            if (root != holder)
            {
                if (_rootPose == null || root.parent == null) return;
                // Generic interpolation may combine a principal-axis rotation
                // with a different scale. Derive the native diameter from its
                // current complete authored TRS, never that rendered intermediate.
                Quaternion rotation = Rotation(_rootPose); Vector3 scale = Scale(_rootPose);
                Vector3 right = root.parent.TransformVector(rotation * new Vector3(scale.x, 0f, 0f));
                Vector3 up = root.parent.TransformVector(rotation * new Vector3(0f, scale.y, 0f));
                authoredScale = new Vector3(right.magnitude, up.magnitude, scale.z);
            }
            if (!ReferenceEquals(_print, print))
            { _print = print; _started = now; Array.Clear(_initialized, 0, _initialized.Length); }
            // User exception, 2026-10-08: intrinsic ring phase may be local,
            // provided direction/speed remain native and card orientation stays
            // authored. Observers never enable the native world-Z callback.
            for (int i = 0; i < _ink.Length; i++)
                if (!_initialized[i] && TryAxes(_ink[i], out Vector3 right, out _))
                {
                    Vector3 local = print.InverseTransformDirection(right);
                    _original[i] = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
                    _initialized[i] = true;
                }
            if (root.parent == null || !FitBasis(root, print, authoredScale)) return;
            float phase = (float)((_rate * (now - _started)) % 360d);
            for (int i = 0; i < _ink.Length; i++)
                if (_initialized[i] && _ink[i] != null && _ink[i].gameObject.activeInHierarchy
                    && _ink[i].parent != null && TryAxes(_ink[i], out _, out _) && TryAxes(_ink[i].parent, out _, out _))
                {
                    Transform drawing = _ink[i];
                    Vector3 direction = print.rotation * Quaternion.AngleAxis(phase + _original[i], Vector3.forward) * Vector3.right;
                    Vector3 local = drawing.parent.InverseTransformVector(direction);
                    if (local.sqrMagnitude < .0000000001f) continue;
                    drawing.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg);
                }
        }
        private static bool TryAxes(Transform? node, out Vector3 right, out Vector3 up)
        {
            right = up = Vector3.zero;
            if (node == null) return false;
            right = node.TransformVector(Vector3.right); up = node.TransformVector(Vector3.up);
            return Finite(right) && Finite(up) && right.sqrMagnitude > .0000000001f
                && up.sqrMagnitude > .0000000001f && Vector3.Cross(right, up).sqrMagnitude > .000000000000000001f;
        }
        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private static bool FitBasis(Transform root, Transform print, Vector3 authoredScale)
        {
            Transform parent = root.parent;
            if (!TryAxes(parent, out _, out _) || !Finite(authoredScale)) return false;
            // Same principal-stretch fit as the source native mask, evaluated
            // against this observer's actual canvas matrix. Quaternion products
            // alone lose the plane under a rotated nonuniform ancestor.
            Vector3 localRight = parent.InverseTransformVector(print.right);
            Vector3 localUp = parent.InverseTransformVector(print.up);
            Vector3 normal = Vector3.Cross(localRight, localUp).normalized;
            if (normal.sqrMagnitude < .5f) return false;
            Quaternion plane = Quaternion.LookRotation(normal, Vector3.Cross(normal, localRight).normalized);
            Vector3 right = parent.TransformVector(plane * Vector3.right), up = parent.TransformVector(plane * Vector3.up);
            float gxx = right.sqrMagnitude, gxy = Vector3.Dot(right, up), gyy = up.sqrMagnitude;
            float angle = .5f * Mathf.Atan2(2f * gxy, gxx - gyy) * Mathf.Rad2Deg;
            Quaternion rotation = plane * Quaternion.Euler(0f, 0f, angle);
            float x = parent.TransformVector(rotation * Vector3.right).magnitude;
            float y = parent.TransformVector(rotation * Vector3.up).magnitude;
            float diameter = Mathf.Sqrt(Mathf.Abs(authoredScale.x * authoredScale.y));
            if (x < .00001f || y < .00001f || diameter < .00001f) return false;
            root.localRotation = rotation;
            Vector3 scale = root.localScale;
            root.localScale = new Vector3(diameter / x, diameter / y, scale.z);
            return true;
        }
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
        OfferedFrames.TryGetValue(nativeHolder, out Transform? previousPrint);
        if (physicalPrint == null)
        { OfferedFrames.Remove(nativeHolder); GloomhavenVR.WorldUI.TownServiceDepthOrder.UnbindOffered(nativeHolder); }
        else if (nativeHolder != null)
        { OfferedFrames[nativeHolder] = physicalPrint; GloomhavenVR.WorldUI.TownServiceDepthOrder.BindOffered(nativeHolder, physicalPrint); }
        if (previousPrint != null && previousPrint != physicalPrint)
        {
            bool retained = false;
            foreach (Transform current in OfferedFrames.Values) retained |= current == previousPrint;
            if (!retained)
            {
                DeadOfferedFrames.Clear();
                foreach (var pair in OfferedPhysicalFrames) if (pair.Value == previousPrint) DeadOfferedFrames.Add(pair.Key);
                foreach (Transform old in DeadOfferedFrames) OfferedPhysicalFrames.Remove(old);
            }
        }
    }

    // Build660 published the actual new procedural backing, but kept its pose
    // on a different interpolation clock from the offered FullAbilityCard. A
    // head-facing turn/hover therefore separated the physical back and print.
    // The real handoff supplies these two source objects; neither an address
    // search nor an observer's nearest card establishes their shared identity.
    internal static void RegisterOfferedPhysical(Transform? body, Transform? printedFront)
    {
        if (ReferenceEquals(body, null)) return;
        if (body == null || printedFront == null) OfferedPhysicalFrames.Remove(body!);
        else OfferedPhysicalFrames[body] = printedFront;
    }

    private static bool IsOfferedPhysical(RemoteModule module) =>
        module.Address.StartsWith("map.cardbody|", StringComparison.Ordinal)
        || module.Address.StartsWith("map.cardbody.", StringComparison.Ordinal);

    // Called before any ordinary header, child tween or return writer. The
    // temporary exact print mount belongs only to the preceding rendered offer.
    // Reparent preserves its world picture and existing independent clocks; no
    // visibility/alpha changes occur between these writes and final card paint.
    private static void RestoreOfferedPhysicalMounts()
    {
        foreach (var pair in OfferedPhysicalMounts)
        {
            RemoteModule module = pair.Key;
            if (!module.Alive) continue;
            Transform? parent = pair.Value.Parent;
            if (parent == null) parent = SharedFrameForRemote?.Invoke(pair.Value.Owner);
            if (parent != null) module.Motion.Reparent(parent);
        }
        OfferedPhysicalMounts.Clear();
    }

    private static void ClearOfferedFrames()
    {
        RestoreOfferedPhysicalMounts(); OfferedPhysicalFrames.Clear(); OfferedPhysicalReturns.Clear();
        OfferedFrames.Clear();
        GloomhavenVR.WorldUI.TownServiceDepthOrder.ClearOffered();
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
        foreach (Transform stale in DeadOfferedFrames)
        { OfferedFrames.Remove(stale); GloomhavenVR.WorldUI.TownServiceDepthOrder.UnbindOffered(stale); }
        DeadOfferedFrames.Clear();
        foreach (var stale in OfferedPhysicalFrames)
            if (stale.Key == null || stale.Value == null) DeadOfferedFrames.Add(stale.Key!);
        foreach (Transform stale in DeadOfferedFrames) OfferedPhysicalFrames.Remove(stale);
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
                bool nativeRingRoot = NativeRingRates.TryGetValue(TemplateKey(holder.Last.Service,
                    holder.Last.Template, holder.Last.TemplateAddress), out NativeRingInfo ring) && ring.Root == 0;
                if (nativeRingRoot)
                {
                    // Unity's 3D lossyScale approximates a sheared ancestry even
                    // when the source mask made the actual ink plane isotropic.
                    // This native root publishes its rendered XY axis lengths;
                    // the observer's final principal fit preserves their area.
                    Vector3 printScale = pair.Value.lossyScale;
                    entry.Numbers[7] = original.TransformVector(Vector3.right).magnitude / Mathf.Abs(printScale.x);
                    entry.Numbers[8] = original.TransformVector(Vector3.up).magnitude / Mathf.Abs(printScale.y);
                }
                Canvas? canvas = original.GetComponentInParent<Canvas>(true);
                if (holder.Last.HasCanvasFrame && canvas != null && canvas.transform != original)
                {
                    if (!ValidMotionScale(canvas.transform.lossyScale)) continue;
                    entry.HasCanvasFrame = true; entry.CanvasPose = ReadPose(canvas.transform, pair.Value);
                    if (!nativeRingRoot && ReferenceEquals(original.parent, canvas.transform))
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
            foreach (var physical in OfferedPhysicalFrames)
            {
                if (physical.Value != pair.Value || !physical.Key.gameObject.activeInHierarchy
                    || !FindOfferedOriginal(lane, physical.Key, out LocalModule? body, out uint bodyBinding)
                    || body == print || body!.Binding.Root != physical.Key
                    || !(body.Address.StartsWith("map.cardbody|", StringComparison.Ordinal)
                        || body.Address.StartsWith("map.cardbody.", StringComparison.Ordinal))
                    || !MotionSources.TryGetValue(body, out SourceMotion? motion)) continue;
                if (!TryReadOfferedPhysicalPose(physical.Key, pair.Value, out float[] pose))
                {
                    Report("offered physical geometry " + body.Id,
                        new InvalidOperationException("Original backing-to-print matrix is not a finite factorable TRS."));
                    continue;
                }
                var entry = MotionHeader(body.Last!, 0, 9);
                entry.Visible = true; entry.Binding = bodyBinding; entry.OfferedModule = print!.Id;
                entry.OfferedStructure = print.Last!.Structure; entry.OfferedBinding = printBinding;
                entry.Numbers = pose;
                UpdateMotionSlot(motion, entry);
                MotionSlot slot = motion.Slots[entry.Key]; ActiveOfferedFrames.Add(slot);
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
        foreach (RemoteModule old in DeadOfferedRemoteMotion)
        { if (old.Host != null) GloomhavenVR.WorldUI.TownServiceDepthOrder.UnbindOffered(old.Host.transform); OfferedRemoteMotion.Remove(old); }
        DeadOfferedRemoteMotion.Clear();
        foreach (RemoteModule old in OfferedPhysicalReturns.Keys) if (!old.Alive) DeadOfferedRemoteMotion.Add(old);
        foreach (RemoteModule old in DeadOfferedRemoteMotion) OfferedPhysicalReturns.Remove(old);
        foreach (var current in MotionRemoteFrames)
            if (current.Key.Alive && IsOfferedPhysical(current.Key))
                PhysicalOfferingSuperseded(current.Key, current.Value);
        OfferedApplyOrder.Clear();
        foreach (var candidate in MotionRemoteFrames)
        {
            if (!candidate.Key.Alive || !candidate.Key.Host.activeInHierarchy) continue;
            // Merchant racks and unrelated visitor props have no print affinity.
            // Sort only the actual offered native partitions, not every town clone.
            foreach (MotionSlot slot in candidate.Value.Slots)
                if (slot.Entry.Kind == 9 && slot.Entry.Visible)
                { OfferedApplyOrder.Add(candidate.Key); break; }
        }
        // A withdrawn offer ends its presentation clock even if the inactive
        // original bank/module is retained for another card or later reopening.
        DeadOfferedRemoteMotion.Clear();
        foreach (RemoteModule old in OfferedRemoteMotion.Keys)
            if (!OfferedApplyOrder.Contains(old)) DeadOfferedRemoteMotion.Add(old);
        foreach (RemoteModule old in DeadOfferedRemoteMotion)
        { if (old.Host != null) GloomhavenVR.WorldUI.TownServiceDepthOrder.UnbindOffered(old.Host.transform); OfferedRemoteMotion.Remove(old); }
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
                if (IsOfferedPhysical(module))
                {
                    if (PhysicalOfferingBlocked(module, composed, physical, slot)) continue;
                    // Existing109 describes the measured source-relative TRS.
                    // Mounting that exact factorable frame on the rendered print
                    // retains its complete affine ancestry, including stretched
                    // or reflected canvases. World quaternion/lossyScale division
                    // cannot retain that geometry under a sheared ancestor.
                    if (module.AddedCanvas == null || relation.HasCanvasFrame
                        || holderIndex != 0 || module.Host.transform.parent == null) continue;
                    if (!OfferedPhysicalMounts.ContainsKey(module))
                        OfferedPhysicalMounts.Add(module, new OfferedPhysicalMount(module.Host.transform.parent, composed.Owner));
                    module.Motion.Reparent(print);
                    Transform root = module.Host.transform;
                    root.localPosition = Position(relation.Numbers); root.localRotation = Rotation(relation.Numbers);
                    root.localScale = Scale(relation.Numbers); NormalizeDetachedRoot(module);
                    module.Motion.AdoptExternalRootPose(); slot.Dirty = false;
                    continue;
                }
                float blend = motion.Duration > 0f ? Mathf.Clamp01((now - motion.Started) / motion.Duration) : 1f;
                if (relation.HasCanvasFrame)
                {
                    if (module.AddedCanvas == null || module.Host.transform.parent == null
                        || !ValidMotionScale(module.Host.transform.parent.lossyScale)) continue;
                    ApplyOfferedPose(module.Host.transform, print, relation.CanvasPose);
                }
                if (relation.OfferedLocalScale && !ReferenceEquals(holder.parent, module.Host.transform)) continue;
                ApplyOfferedPose(holder, print, relation.Numbers, relation.OfferedLocalScale, motion.From,
                    motion.Ring != null ? 1f : blend);
                motion.Ring?.Draw(holder, print, Time.timeAsDouble, Vector3.Scale(print.lossyScale, Scale(relation.Numbers)),
                    EffectiveRemoteFrame(module)!);
                GloomhavenVR.WorldUI.TownServiceDepthOrder.BindOffered(module.Host.transform, print);
                slot.Dirty = false;
            }
        }
    }

    private static bool PhysicalOfferingBlocked(RemoteModule module, RemoteMotion body, RemoteModule print,
        MotionSlot affinity)
    {
        bool blocked = PhysicalOfferingSuperseded(module, body);
        if (MotionRemoteFrames.TryGetValue(print, out RemoteMotion? physical))
            blocked |= PhysicalOfferingSuperseded(module, physical);
        return blocked || OfferedPhysicalReturns.TryGetValue(module, out float returnedAt) && affinity.SampleTime <= returnedAt;
    }

    private static bool PhysicalOfferingSuperseded(RemoteModule module, RemoteMotion frame)
    {
        bool blocked = false;
        foreach (MotionSlot slot in frame.Slots)
        {
            if (slot.Entry.Kind == 8)
            {
                if (!OfferedPhysicalReturns.TryGetValue(module, out float old) || old < slot.SampleTime)
                    OfferedPhysicalReturns[module] = slot.SampleTime;
                blocked = true;
            }
            else if (slot.Entry.Kind == 1 && slot.Entry.Hand != 0) blocked = true;
        }
        return blocked;
    }

    private static bool TryReadOfferedPhysicalPose(Transform body, Transform print, out float[] pose)
    {
        pose = Array.Empty<float>();
        Matrix4x4 relative = print.worldToLocalMatrix * body.localToWorldMatrix;
        Vector3 position = relative.MultiplyPoint3x4(Vector3.zero);
        Vector3 right = relative.MultiplyVector(Vector3.right), up = relative.MultiplyVector(Vector3.up),
            forward = relative.MultiplyVector(Vector3.forward);
        Vector3 scale = new(right.magnitude, up.magnitude, forward.magnitude);
        if (!ValidMotionScale(scale) || !FiniteOfferedPhysical(position)) return false;
        Vector3 x = right / scale.x, y = up / scale.y, z = forward / scale.z;
        if (Mathf.Abs(Vector3.Dot(x, y)) > .00001f || Mathf.Abs(Vector3.Dot(x, z)) > .00001f
            || Mathf.Abs(Vector3.Dot(y, z)) > .00001f) return false;
        Vector3 normal = Vector3.Cross(x, y).normalized;
        if (Vector3.Dot(normal, z) < 0f) scale.z = -scale.z;
        Quaternion rotation = Quaternion.LookRotation(normal, y);
        // A quaternion plus three scales is honest only if it reconstructs the
        // actual source columns. Preserve negative handedness on the third axis.
        if ((rotation * Vector3.right - x).sqrMagnitude > .0000000001f
            || (rotation * Vector3.up - y).sqrMagnitude > .0000000001f
            || (rotation * Vector3.forward * Mathf.Sign(scale.z) - z).sqrMagnitude > .0000000001f) return false;
        pose = new[] { position.x, position.y, position.z, rotation.x, rotation.y, rotation.z, rotation.w,
            scale.x, scale.y, scale.z };
        return true;
    }

    private static bool FiniteOfferedPhysical(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

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
        if (motion.Ring == null && module.LastFrame != null
            && NativeRingRates.TryGetValue(TemplateKey(module.LastFrame.Service, module.Template, module.Address), out NativeRingInfo info) && info.Root < module.Binding.Nodes.Length)
            motion.Ring = new NativeRingClock(module.Binding.Nodes, module.Binding.Bindings, info);
        if (motion.Sequence == slot.ReceivedSequence) return motion;
        TownServiceMotionEntry? old = motion.Target;
        bool samePrint = old != null && old.Session == target.Session && old.Structure == target.Structure
            && old.Binding == target.Binding && old.OfferedModule == target.OfferedModule
            && old.OfferedStructure == target.OfferedStructure && old.OfferedBinding == target.OfferedBinding
            && old.HasCanvasFrame == target.HasCanvasFrame && old.OfferedLocalScale == target.OfferedLocalScale;
        if (!samePrint) motion.Ring?.Reset();
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
