using System.Collections.Generic;
using UnityEngine;

// These are the surrounding session/conversion inputs. Native GH.Runtime owners,
// UIWindow registry, uGUI lifecycle and all menu-depth code execute unchanged.
namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning = true; }
    internal static class VRLog
    {
        internal static readonly List<string> Messages = new();
        internal static bool WantsDebug => true;
        internal static void Warn(string scope, string message) => Messages.Add(message);
        internal static void Note(string scope, string message) => Messages.Add(message);
        internal static void Info(string scope, string message) => Messages.Add(message);
        internal static void Debug(string scope, string message) => Messages.Add(message);
    }
    internal static class PerfMonitor
    {
        private sealed class EmptyScope : System.IDisposable { public void Dispose() { } }
        internal static System.IDisposable Scope(string name) => new EmptyScope();
        internal static void Count(string name) { }
    }
}
namespace GloomhavenVR.Rig
{ internal static class VRRigDriver { internal static Camera? HeadCamera = null; } }
namespace GloomhavenVR.WorldUI
{
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive = true;
        internal sealed class Setting { internal bool Value = true; }
        internal static readonly Setting CharacterPreviewDepth = new();
    }
    internal sealed class ConvertedPanel
    {
        internal Transform Target = null!;
        internal bool IsAlive = true;
        internal RectTransform HostRect => throw MenuDepthPerfContext.Unexpected("ConvertedPanel.HostRect");
    }
    internal static class FlatScreen { internal static bool ManualScreenActive = false; }
    internal static class CanvasConversion { internal static readonly List<ConvertedPanel> ActivePanels = new(); }

    // The complete directory also compiles the Further Ability hover helper,
    // but this workload creates no ability-card owner and installs neither hover
    // patch. Its tooltip geometry belongs to the actual-kernel card suite. Keep
    // these inactive dependencies explicit: never invent a successful placement.
    internal static class MenuDepthPerfContext
    {
        internal static int UnexpectedCalls { get; private set; }
        internal static System.InvalidOperationException Unexpected(string member)
        {
            UnexpectedCalls++;
            return new System.InvalidOperationException(member + " is outside the native discovery/character performance workload.");
        }
    }
    internal static class ModalFallback
    {
        internal static ConvertedPanel? FindOwningWindow(Transform? hovered) =>
            throw MenuDepthPerfContext.Unexpected("ModalFallback.FindOwningWindow");
    }
    internal static class TooltipOnWindow
    {
        internal static void Settle(Component? widget, string kind, Component? homeOwner = null) =>
            throw MenuDepthPerfContext.Unexpected("TooltipOnWindow.Settle");
        internal static bool PlaceFullCardPreview(Component? row, Component? fullCard,
            float offsetXPixels, float offsetYPixels, string kind) =>
            throw MenuDepthPerfContext.Unexpected("TooltipOnWindow.PlaceFullCardPreview");
    }
}
namespace GloomhavenVR.Cards
{
    internal static class CardsConfig
    { internal sealed class Setting { internal bool Value = true; } internal static readonly Setting FaceMipBake = new(); }
    internal static class CardFace { internal static void Offer(Component source) { } }
    internal static class CardFxBounds { internal static void Reseat(Component source) { } }
}

public sealed class MenuDepthPerfRenderStamp : MonoBehaviour
{
    public int Draws, OwnedDraws;
    public RenderTexture? NativeTarget;
    public Texture? NativeBlue;
    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Draws++;
        bool owned = NativeTarget != null && !ReferenceEquals(GetComponent<Camera>().targetTexture, NativeTarget);
        if (owned) OwnedDraws++;
        Graphics.Blit(!owned && NativeBlue != null ? NativeBlue : source, destination);
    }
}
