using System;
using System.Collections.Generic;
using HarmonyLib;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

internal static partial class CanvasConversion
{
    // Build624 still measured 1.03 ms in CanvasConversion.LateTick after hierarchy inventories
    // landed. Each converted panel nevertheless enumerated EVERY native registered window and
    // tested every unknown window's ancestry. This lane distributes that registry once across
    // all current roots. It is deliberately not a per-frame snapshot: CompleteReveal and other
    // native callbacks between panels can register/reparent windows before the next panel runs.
    // The native HashSet mutation version and synchronous hierarchy events invalidate it at
    // that next read, including same-count remove/add. Unsupported runtimes retain the old path.
    private struct SharedVeilRoot
    {
        internal HiddenWindowVeilState State;
        internal Transform Root;
        internal int Next;
    }

    private static readonly List<SharedVeilRoot> SharedVeilRoots = new(16);
    private static readonly Dictionary<HiddenWindowVeilState, int> SharedVeilRootIndices = new(16);
    private static readonly Dictionary<Transform, int> SharedVeilRootHeads = new(16);
    private struct SharedVeilGroupRead
    {
        internal Transform? Parent;
        internal bool HasGroup;
        internal float Alpha;
        internal bool IgnoreParents;
    }

    private static readonly Dictionary<Transform, SharedVeilGroupRead> SharedVeilGroupReads = new(128);
    private static readonly AccessTools.FieldRef<HashSet<UIWindow>, int>? NativeWindowRegistryVersion =
        ResolveNativeWindowRegistryVersion();
    private static HashSet<UIWindow>? SharedVeilRegistry;
    private static int SharedVeilRegistryVersion;
    private static int SharedVeilHierarchyRevision;
    private static bool SharedVeilRootsDirty;
    private static bool SharedVeilReadPass;
    private static long SharedRegistryScans, SharedRegistryMembers, IndependentRegistryScans, IndependentRegistryMembers, SharedRegistryReusePanels;

    private static AccessTools.FieldRef<HashSet<UIWindow>, int>? ResolveNativeWindowRegistryVersion()
    {
        try
        {
            // Unity's Mono and the framework reference have used these three names. Never infer
            // membership from Count: closing one window and enabling another keeps Count equal.
            foreach (string name in new[] { "_version", "m_version", "version" })
            {
                System.Reflection.FieldInfo? field = AccessTools.Field(typeof(HashSet<UIWindow>), name);
                if (field == null || field.FieldType != typeof(int) || field.IsStatic) continue;
                AccessTools.FieldRef<HashSet<UIWindow>, int> read =
                    AccessTools.FieldRefAccess<HashSet<UIWindow>, int>(name);
                // Prove the field observes add/remove on a PRIVATE set, never the native registry.
                var probe = new HashSet<UIWindow>();
                int empty = read(probe);
                probe.Add(null!);
                int added = read(probe);
                probe.Remove(null!);
                if (added != empty && read(probe) != added) return read;
            }
        }
        catch (Exception)
        {
            // This is optional work removal, not a prerequisite for displaying native windows.
        }
        return null;
    }

    private readonly struct SharedVeilReadScope : IDisposable
    {
        public void Dispose() => EndSharedVeilWindowReads();
    }

    private static SharedVeilReadScope BeginSharedVeilWindowReads()
    {
        EndSharedVeilWindowReads();
        if (!PerfConfig.SharedUiWindowReadsOn || NativeWindowRegistryVersion == null) return default;
        SharedVeilReadPass = true;
        for (int i = 0; i < Active.Count; i++)
        {
            ConvertedPanel panel = Active[i];
            if (!panel.IsAlive || panel.Target == null) continue;
            if (!HiddenWindowVeils.TryGetValue(panel, out HiddenWindowVeilState? state))
            {
                state = new HiddenWindowVeilState();
                HiddenWindowVeils.Add(panel, state);
            }
            RegisterSharedVeilRoot(state, panel.Target);
        }
        return default;
    }

    private static void EndSharedVeilWindowReads()
    {
        // Flush once per complete LateTick, never once per native window/graphic.
        // These count executed membership visits, not hypothetical saved getters.
        PerfMonitor.Count("UI.WindowRegistrySharedScans", SharedRegistryScans);
        PerfMonitor.Count("UI.WindowRegistrySharedMembers", SharedRegistryMembers);
        PerfMonitor.Count("UI.WindowRegistryIndependentScans", IndependentRegistryScans);
        PerfMonitor.Count("UI.WindowRegistryIndependentMembers", IndependentRegistryMembers);
        PerfMonitor.Count("UI.WindowRegistryReusePanels", SharedRegistryReusePanels);
        SharedRegistryScans = SharedRegistryMembers = IndependentRegistryScans = IndependentRegistryMembers = SharedRegistryReusePanels = 0;
        SharedVeilReadPass = false;
        SharedVeilRegistry = null;
        SharedVeilRoots.Clear();
        SharedVeilRootIndices.Clear();
        SharedVeilRootHeads.Clear();
        SharedVeilGroupReads.Clear();
        SharedVeilRootsDirty = false;
    }

    private static void RegisterSharedVeilRoot(HiddenWindowVeilState state, Transform target)
    {
        if (SharedVeilRootIndices.TryGetValue(state, out int index))
        {
            SharedVeilRoot entry = SharedVeilRoots[index];
            if (ReferenceEquals(entry.Root, target)) return;
            entry.Root = target;
            SharedVeilRoots[index] = entry;
        }
        else
        {
            SharedVeilRootIndices.Add(state, SharedVeilRoots.Count);
            SharedVeilRoots.Add(new SharedVeilRoot { State = state, Root = target, Next = -1 });
        }
        SharedVeilRootsDirty = true;
    }

    private static void ForgetSharedVeilWindowInventory(HiddenWindowVeilState state)
    {
        if (!SharedVeilRootIndices.TryGetValue(state, out int index)) return;
        SharedVeilRoot entry = SharedVeilRoots[index];
        entry.Root = null!;
        SharedVeilRoots[index] = entry;
        SharedVeilRootsDirty = true;
    }

    private static bool TryRefreshSharedVeilWindowInventory(HiddenWindowVeilState state, Transform target)
    {
        if (!SharedVeilReadPass || NativeWindowRegistryVersion == null) return false;
        RegisterSharedVeilRoot(state, target);
        // GetWindows returns the LIVE mutable HashSet, maintained by native OnEnable/OnDisable
        // (decompiled/GH.Runtime/UnityEngine.UI/UIWindow.cs:398/404/642). Read it on every panel.
        HashSet<UIWindow>? registered = UIWindow.GetWindows();
        if (registered == null) return false;
        int version = NativeWindowRegistryVersion(registered);
        bool registryChanged = !ReferenceEquals(registered, SharedVeilRegistry)
            || version != SharedVeilRegistryVersion;
        if (!registryChanged && !SharedVeilRootsDirty
            && SharedVeilHierarchyRevision == UiHierarchyInventory.HierarchyRevision)
        {
            if (PerfMonitor.StepsActive && VRLog.WantsDebug) SharedRegistryReusePanels++;
            return true;
        }

        SharedVeilRootHeads.Clear();
        for (int i = 0; i < SharedVeilRoots.Count; i++)
        {
            SharedVeilRoot entry = SharedVeilRoots[i];
            if (entry.Root == null) continue;
            HiddenWindowVeilState owner = entry.State;
            if (owner.Inventory == null || !ReferenceEquals(owner.Inventory.Root, entry.Root))
            {
                owner.Inventory?.Dispose();
                owner.Inventory = new UiHierarchyInventory(entry.Root);
            }
            bool topologyChanged = owner.Inventory.Refresh();
            // Registry edits can add a component to an existing transform without a transform
            // event, then disable it again before this read. Retain those disabled windows too.
            if (!ReferenceEquals(owner.SharedRegistry, registered)
                || owner.SharedRegistryVersion != version || topologyChanged)
            {
                owner.Windows.Clear();
                entry.Root.GetComponentsInChildren(includeInactive: true, owner.Windows);
                owner.WindowSet.Clear();
                for (int w = 0; w < owner.Windows.Count; w++) owner.WindowSet.Add(owner.Windows[w]);
                owner.SharedRegistry = registered;
                owner.SharedRegistryVersion = version;
            }
            entry.Next = SharedVeilRootHeads.TryGetValue(entry.Root, out int head) ? head : -1;
            SharedVeilRoots[i] = entry;
            SharedVeilRootHeads[entry.Root] = i;
        }
        bool countRegistryWork = PerfMonitor.StepsActive && VRLog.WantsDebug;
        if (countRegistryWork) SharedRegistryScans++;
        foreach (UIWindow window in registered)
        {
            if (countRegistryWork) SharedRegistryMembers++;
            if (window == null) continue;
            for (Transform? node = window.transform; node != null; node = node.parent)
            {
                if (!SharedVeilRootHeads.TryGetValue(node, out int head)) continue;
                for (int i = head; i >= 0; i = SharedVeilRoots[i].Next)
                {
                    HiddenWindowVeilState owner = SharedVeilRoots[i].State;
                    if (owner.WindowSet.Add(window)) owner.Windows.Add(window);
                }
            }
        }
        SharedVeilRegistry = registered;
        SharedVeilRegistryVersion = version;
        SharedVeilHierarchyRevision = UiHierarchyInventory.HierarchyRevision;
        SharedVeilRootsDirty = false;
        return true;
    }

    // Only one Veil's pure graphic collection loop may reuse these group reads. No native
    // callback or show tween runs inside that loop, and the table is cleared on both sides.
    // The live off path retains the original per-graphic chain walk for direct A/B comparison.
    private static float SharedGroupChainAlphaBelow(Transform from, Transform window)
    {
        float alpha = 1f;
        for (Transform? node = from; node != null && !ReferenceEquals(node, window);)
        {
            if (!SharedVeilGroupReads.TryGetValue(node, out SharedVeilGroupRead read))
            {
                CanvasGroup? group = node.GetComponent<CanvasGroup>();
                read = new SharedVeilGroupRead
                {
                    Parent = node.parent,
                    HasGroup = group != null,
                    Alpha = group != null ? group.alpha : 1f,
                    IgnoreParents = group != null && group.ignoreParentGroups,
                };
                SharedVeilGroupReads.Add(node, read);
            }
            // Preserve the original leaf-to-root floating-point multiplication order too.
            if (read.HasGroup)
            {
                alpha *= read.Alpha;
                if (read.IgnoreParents) return alpha;
            }
            node = read.Parent;
        }
        return 0f;
    }
}
