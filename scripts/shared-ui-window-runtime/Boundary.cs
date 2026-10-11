using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core
{
    internal static class PerfConfig { internal static bool SharedUiWindowReadsOn = true; }
    internal static class VRLog
    {
        internal static bool WantsDebug = true;
        internal static void Note(string s, string m) { }
    }
    internal static class PerfMonitor
    {
        internal static bool StepsActive = true;
        internal static readonly Dictionary<string, long> Counts = new();
        internal static void Count(string name, long amount)
        {
            if (!StepsActive || amount == 0) return;
            Counts.TryGetValue(name, out long before); Counts[name] = before + amount;
        }
        internal static long Value(string name) => Counts.TryGetValue(name, out long value) ? value : 0;
        internal readonly struct Token : IDisposable { public void Dispose() { } }
        internal static Token Scope(string name) => default;
    }
}

namespace GloomhavenVR.WorldUI
{
    // Real Unity UI, CanvasGroup, CanvasRenderer and the publisher's complete original UIWindow
    // execute. These boundaries stand in only for other unrelated converted-panel services.
    internal sealed class ConvertedPanel
    {
        internal GameObject HostGo = null!;
        internal Transform Target = null!;
        internal bool IsAlive => Target != null;
        internal bool RenderHidden = false;
        internal int LastRevealFrame = -1;
        internal bool FlattenEnabled = false;
        internal float EarlySettleUntil = 0;
        internal bool ModLayerEnabled = false;
        internal bool HideBackground = false;
        internal bool RevealPending;
        internal bool RevealArmed;
        internal bool Dormant;
        internal bool Animating;
    }

    internal static class ModalFallback
    {
        internal static bool IsDormantPanel(ConvertedPanel panel) => panel.Dormant;
        internal static void TickFloatIntentGuard() { }
    }
    internal static class PanelSupersample
    {
        internal static bool OwnsPanelLayers(ConvertedPanel panel) => false;
        internal static void LateTick() { }
    }
    internal static class WindowMaterialise
    {
        internal static bool IsAnimating(ConvertedPanel panel) => panel.Animating;
    }
    internal static class Proof
    {
        internal static int RegistryScans, RegistryMembers, ComponentCaptures, GroupReads, NativeCullCallbacks;
        internal static void Reset() { RegistryScans = RegistryMembers = ComponentCaptures = GroupReads = NativeCullCallbacks = 0; GloomhavenVR.Core.PerfMonitor.Counts.Clear(); }
    }
    internal static partial class CanvasConversion
    {
        internal static readonly List<ConvertedPanel> Active = new();
        private const float FitMinAlpha = .05f;
        internal static Action<ConvertedPanel>? RevealCallback;
        internal static Action<ConvertedPanel>? SeatCallback;
        private static float PreSeatVeilAlpha(UnityEngine.CanvasRenderer cr, float alpha) => alpha;
        private static void TickSubViewSeatVeil(ConvertedPanel panel) => SeatCallback?.Invoke(panel);
        private static void FlattenSubtree(ConvertedPanel panel) { }
        private static void AdoptNestedCanvases(ConvertedPanel panel) { }
        private static void ApplyModLayer(ConvertedPanel panel, bool initial) { }
        private static void HideFullScreenBackground(ConvertedPanel panel, bool initial) { }
        private static void SetPanelRenderVisible(ConvertedPanel panel, bool visible) => panel.RenderHidden = !visible;
        private static void TickFlashVeilScan() { }
        private static void CompleteReveal(ConvertedPanel panel)
        {
            panel.RenderHidden = false;
            panel.RevealPending = panel.RevealArmed = false;
            RevealCallback?.Invoke(panel);
        }
    }
}
