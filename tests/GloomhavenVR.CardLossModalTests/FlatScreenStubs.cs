namespace GloomhavenVR.Core
{
    internal static class VRLog
    {
        internal static void Info(string category, string text) { }
        internal static void Note(string category, string text) { }
    }
}
namespace GloomhavenVR.Core.Events
{
    internal enum VRMode { TableIdle, ModalUI, Menu2D }
    internal static class VRModeStateMachine { internal static VRMode CurrentMode; }
}
namespace GloomhavenVR.MapRoom
{
    internal static class MapRoomDriver { internal static bool Active; }
}
namespace GloomhavenVR.Cards.Patches
{
    internal static class HandSuppression { internal static bool Active, BurnActive; }
}
namespace GloomhavenVR.WorldUI
{
    internal static class NativeVideoWindow { internal static bool Visible; }
    internal static class LoadingIndicator { internal static bool FlatScreenSuppressed; }
    internal static class ModalFallback { internal static bool ScreenWanted, WindowModalActive; }
    internal static class WorldUIConfig
    {
        internal static bool ConversionActive;
        internal sealed class Setting { internal bool Value; }
        internal static readonly Setting Dialogs = new();
    }
    internal sealed partial class FlatScreen
    {
        private bool _cardLossFallbackNoted = false;
        private bool _rescueShow = false;
        private bool _manualShow = false;
        internal bool ConfirmationOpen;
        internal static bool ManualScreenActive { get; private set; }
        private bool IsConfirmationBoxOpen() => ConfirmationOpen;
        internal bool Read(bool manual = false, bool rescue = false)
        {
            _manualShow = manual;
            _rescueShow = rescue;
            UpdateScreenTakeover();
            return WantVisible();
        }
    }
}
