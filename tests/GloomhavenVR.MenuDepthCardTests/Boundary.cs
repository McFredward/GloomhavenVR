using System.Collections.Generic;
using UnityEngine;

// Only surrounding feature/session state is supplied. Graphic generation,
// catalogue parsing, semantic discovery and native GH.Runtime fields are real.
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static void Warn(string scope, string message) => Messages.Add(message);
        internal static void Note(string scope, string message) => Messages.Add(message);
        internal static void Info(string scope, string message) => Messages.Add(message);
        internal static bool WantsDebug => true;
        internal static void Debug(string scope, string message) => Messages.Add(message);
    }
}
namespace GloomhavenVR.Cards
{
    // The entire actual mip producer executes. These unrelated card/config/perf
    // services are the fixture boundary; none changes sprite identity or pixels.
    internal static class CardsConfig
    {
        internal sealed class Setting { internal bool Value = true; }
        internal static readonly Setting FaceMipBake = new();
    }
    internal static class CardFace { internal static void Offer(Component source) { } }
    internal static class CardFxBounds { internal static void Reseat(Component source) { } }
}
namespace GloomhavenVR.Core
{
    internal static class PerfMonitor
    {
        private sealed class ScopeOwner : System.IDisposable { public void Dispose() { } }
        internal static System.IDisposable Scope(string name) => new ScopeOwner();
        internal static void Count(string name) { }
    }
}
namespace GloomhavenVR.Rig
{
    internal static class VRRigDriver { internal static Camera? HeadCamera = null; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig { internal static bool ConversionActive = true; internal sealed class Setting { internal bool Value = true; } internal static readonly Setting CharacterPreviewDepth = new(); }
    internal sealed class ConvertedPanel
    {
        internal RectTransform Target = null!;
        internal bool IsAlive = true;
        internal RectTransform HostRect = null!;
        internal GameObject HostGo = null!;
        internal Canvas HostCanvas = null!;
        internal bool OrderListed;
        internal int DrawSortingOrder = 100;
        internal readonly List<NestedCanvasRecord> AdoptedCanvases = new();
        internal bool AdoptedOrderRebaseDirty;
    }
    internal static partial class CanvasConversion
    {
        internal const int RaisedOverlayOrderOffset = 15;
        internal static readonly List<ConvertedPanel> ActivePanels = new();
        // The existing order-ladder service is an explicit boundary. Production
        // adoption and raised-overlay record/flag ownership execute unchanged.
        internal static void RegisterOrderFollower(ConvertedPanel owner, Canvas canvas, int offset)
            => canvas.sortingOrder = owner.DrawSortingOrder + offset;
    }
    internal static class ModalFallback
    {
        internal static ConvertedPanel? FindOwningWindow(Transform? source)
        {
            if (source == null) return null;
            foreach (ConvertedPanel panel in CanvasConversion.ActivePanels)
                if (panel.IsAlive && panel.Target != null && source.IsChildOf(panel.Target)) return panel;
            return null;
        }
    }
    internal static class CharacterTooltipPlacement
    {
        internal static void SettleNativeLocal(Component source) { }
        internal static void ReleaseNativeLocal(Component source) { }
        internal static bool TryLocalHint(Component source, out RectTransform? anchor) { anchor = null; return false; }
        internal static void ReleaseConvertedAnnotation(RectTransform source) { }
        internal static void TrackConvertedAnnotation(RectTransform source, RectTransform? anchor) { }
        internal static void PlaceLocal(RectTransform source, RectTransform host, RectTransform? anchor) { }
        internal static Vector3 RightCenter(RectTransform host, Quaternion rotation, RectTransform? anchor,
            float halfWidth, float halfHeight, float depth) => throw new System.NotSupportedException("No character annotation in card fixture");
    }
    internal static class FlatScreen { internal static bool ManualScreenActive; }
}
