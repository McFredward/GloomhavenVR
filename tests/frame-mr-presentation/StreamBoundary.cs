using UnityEngine;

// Rendering and XR admission are explicit boundaries. The real source owner,
// camera entry/exit, enable epoch and ownership predicates execute unchanged.
namespace GloomhavenVR.WorldUI
{
    internal static class VROptionsTab { internal static bool IsOpen; }
}
namespace GloomhavenVR.Core
{
    internal static class StereoModeConfig
    {
        internal enum Mode { MultiPass }
        internal static Mode Current => Mode.MultiPass;
    }
    internal static class FixtureXrSettings
    {
        // No HMD is instantiated in this real-GPU presentation fixture. Actual
        // camera eye/attachment bodies execute in steamlink-scene-startup.
        internal static bool StereoEnabled(Camera head) => true;
        internal static UnityEngine.XR.XRSettings.StereoRenderingMode stereoRenderingMode =>
            UnityEngine.XR.XRSettings.StereoRenderingMode.MultiPass;
    }
    internal static partial class VRLog
    {
        internal static void Warn(string module, string text) { }
    }
}
namespace GloomhavenVR.Core.SteamLink
{
    // Capability is a named input boundary; public OpenVR identity parsing and
    // helper ownership have their own isolated tests, not a simulated HMD here.
    internal static class SteamLinkConnection { internal static bool Available = true; }
    internal static class SteamLinkConnectionLifecycle
    {
        internal static bool Checking;
        internal static void Tick() { }
    }
    internal static class SteamLinkCoverageBridge
    {
        internal static bool CanAcceptOrdinaryRgba, Wanted;
        internal static int Ticks;
        internal static void Tick(bool wanted, double now) { Wanted = wanted; Ticks++; }
        internal static void Reset() { CanAcceptOrdinaryRgba = false; Wanted = false; }
    }
    internal static class ScenePayloadAssets
    {
        internal static bool TryGet(out Shader coverage, out string reason)
        {
            coverage = Shader.Find("Unlit/Color"); reason = string.Empty;
            return coverage != null;
        }
    }
    internal sealed class SceneHeadOutput : MonoBehaviour
    {
        internal static SceneHeadOutput? Last;
        internal Camera Head = null!;
        internal bool Ready;
        internal string Failure => "Fixture source preparation refused";
        private ulong _epoch;
        internal bool Prepare(Camera head, ulong epoch, Shader coverage)
        { Head = head; _epoch = epoch; Ready = true; Last = this; return true; }
        internal bool IsOwned => SteamLinkSceneRuntime.Owns(this, Head, _epoch);
        internal void TickCold() { }
        internal void Close() => Ready = false;
    }
}
