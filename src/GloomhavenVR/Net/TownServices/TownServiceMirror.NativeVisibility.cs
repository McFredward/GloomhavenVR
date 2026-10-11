using System;
using System.Collections.Generic;
using System.Globalization;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Net.TownServices;

internal static partial class TownServiceMirror
{
    private sealed class NativeVisibilityTrace
    {
        internal uint Session;
        internal ulong Signature;
        internal float SampleAt;
        internal int Reports;
    }
    private static readonly Dictionary<long, NativeVisibilityTrace> NativeVisibilityTraces = new();
    private static float _nativeVisibilityWindow;
    private static int _nativeVisibilityReports;

    private static void TraceResetNativeVisibility()
    { NativeVisibilityTraces.Clear(); _nativeVisibilityWindow = 0f; _nativeVisibilityReports = 0; }

    private static void TraceRemoteNativeVisibility(float now)
    {
        if (!VRLog.WantsDebug) return;
        foreach (var peer in Remote)
        {
            if (peer.Key <= 0 || !Sessions.TryGetValue(peer.Key, out var session)
                || !session.Active || session.Service != 3 || !session.TransactionActive) continue;
            foreach (RemoteModule module in peer.Value.Values)
            {
                TownServiceFrame? frame = EffectiveRemoteFrame(module);
                if (module.Alive && frame != null)
                    TraceNativeVisibility(peer.Key, module.Binding, frame, module.Host, now);
            }
        }
    }

    private static void TraceNativeVisibility(int peer, TownServiceBinding binding,
        TownServiceFrame frame, GameObject host, float now)
    {
        // Native admission/active flags cannot explain an intermittent empty
        // overlay. Sample the actual owner's and observer's original ink state
        // after their respective property passes. Normal user logs are unchanged;
        // scans, formatting and bounded transition reports exist only at Debug.
        if (!VRLog.WantsDebug || frame.Service != 3 || frame.PublicCatalog || frame.VisitorStock
            || !(frame.TemplateAddress.StartsWith("enchant.holder", StringComparison.Ordinal)
                || frame.TemplateAddress.StartsWith("enchant.highlight", StringComparison.Ordinal)
                || frame.TemplateAddress.StartsWith("enhance.confirm.part.", StringComparison.Ordinal)
                || frame.TemplateAddress.StartsWith("face.", StringComparison.Ordinal))) return;
        long key = ((long)peer << 16) | frame.Module;
        if (!NativeVisibilityTraces.TryGetValue(key, out var trace) || trace.Session != frame.Session)
        {
            if (NativeVisibilityTraces.Count >= 128) NativeVisibilityTraces.Clear();
            trace = new NativeVisibilityTrace { Session = frame.Session };
            NativeVisibilityTraces[key] = trace;
        }
        if (now < trace.SampleAt || trace.Reports >= 20) return;
        if (now - _nativeVisibilityWindow >= 30f)
        { _nativeVisibilityWindow = now; _nativeVisibilityReports = 0; }
        if (_nativeVisibilityReports >= 128) return;
        trace.SampleAt = now + .1f;
        int graphics = 0, enabled = 0, active = 0, culled = 0, transparent = 0, inheritedTransparent = 0;
        int canvases = 0, enabledCanvases = 0, groups = 0, hiddenGroups = 0;
        int minOrder = int.MaxValue, maxOrder = int.MinValue, orders = 0;
        ulong orderSignature = 0;
        void ObserveOrder(int order, int layer)
        {
            orders++; minOrder = Mathf.Min(minOrder, order); maxOrder = Mathf.Max(maxOrder, order);
            orderSignature = unchecked((orderSignature * 131UL + (uint)order) * 131UL + (uint)layer);
        }
        Canvas? hostCanvas = host.GetComponent<Canvas>();
        if (hostCanvas != null) ObserveOrder(hostCanvas.sortingOrder, hostCanvas.sortingLayerID);
        foreach (Transform node in binding.Nodes)
        {
            if (node == null) continue;
            Graphic? graphic = node.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphics++; if (graphic.enabled) enabled++;
                if (graphic.canvas != null) ObserveOrder(graphic.canvas.sortingOrder, graphic.canvas.sortingLayerID);
                if (node.gameObject.activeInHierarchy) active++;
                if (graphic.canvasRenderer.cull) culled++;
                if (graphic.color.a <= 0f || graphic.canvasRenderer.GetColor().a <= 0f) transparent++;
                if (graphic.canvasRenderer.GetInheritedAlpha() <= 0f) inheritedTransparent++;
            }
            Canvas? canvas = node.GetComponent<Canvas>();
            if (canvas != null) { canvases++; if (canvas.isActiveAndEnabled) enabledCanvases++;
                ObserveOrder(canvas.sortingOrder, canvas.sortingLayerID); }
            Renderer? renderer = node.GetComponent<Renderer>();
            if (renderer != null) ObserveOrder(renderer.sortingOrder, renderer.sortingLayerID);
            CanvasGroup? group = node.GetComponent<CanvasGroup>();
            if (group != null && group.enabled) { groups++; if (group.alpha <= 0f) hiddenGroups++; }
        }
        bool output = binding.HasVisibleOutput();
        ulong signature = unchecked(orderSignature * 131UL + (frame.Visible ? 1UL : 0UL));
        foreach (int value in new[] { host.activeInHierarchy ? 1 : 0, output ? 1 : 0,
            (int)(Mathf.Clamp01(frame.ParentAlpha) * 255f), graphics, enabled, active,
            culled, transparent, inheritedTransparent, canvases, enabledCanvases, groups, hiddenGroups })
            signature = unchecked(signature * 131UL + (uint)value);
        if (trace.Reports != 0 && signature == trace.Signature) return;
        trace.Signature = signature; trace.Reports++; _nativeVisibilityReports++;
        bool member = peer == 0 ? Local.ContainsKey(frame.Module)
            : Sessions.TryGetValue(peer, out var session) && Array.BinarySearch(session.Modules, frame.Module) >= 0;
        VRLog.Debug("TownServices", "Native visibility " + (peer == 0 ? "owner" : "observer")
            + " peer=" + (peer == 0 ? LocalPeer : peer) + " session=" + frame.Session + " module=" + frame.Module
            + " address=" + frame.TemplateAddress + " sequence=" + frame.Sequence + " sample=" + frame.SampleTime.ToString("F3", CultureInfo.InvariantCulture)
            + " headerVisible=" + frame.Visible + " parentAlpha=" + frame.ParentAlpha.ToString("F3", CultureInfo.InvariantCulture)
            + " member=" + member + " hostActive=" + host.activeInHierarchy + " visibleInk=" + output
            + " graphic(active/enabled/culled/transparent/inheritedTransparent/total)=" + active + "/" + enabled + "/" + culled + "/" + transparent + "/" + inheritedTransparent + "/" + graphics
            + " canvas(enabled/total)=" + enabledCanvases + "/" + canvases + " group(hidden/total)=" + hiddenGroups + "/" + groups
            + " paintOrder(min/max/count)=" + (orders != 0 ? minOrder : 0) + "/" + (orders != 0 ? maxOrder : 0) + "/" + orders
            + " originalCanvasOrder=" + frame.CanvasSortingOrder + " hostCanvasOrder=" + (hostCanvas != null ? hostCanvas.sortingOrder : 0) + ".");
    }
}
