namespace GloomhavenVR.Core
{
    internal static class VRSession { internal static bool IsRunning; }
}

namespace GloomhavenVR.WorldUI
{
    // Native pause-menu discovery and its later normal behavior are boundaries. The fixture
    // executes the unchanged production Tick prefix through the missing-menu early return.
    internal sealed partial class OptionsToggle
    {
        private bool _open = true;
        private int _spentPressId = -1;
        internal ESCMenu? Menu;
        internal bool ReachedNormalMenu;
        internal bool DroppedMissingMenu => !_open && _spentPressId == NonDominantHold.PressId;
        private ESCMenu? ResolveMenu() => Menu;
    }
    internal sealed class ESCMenu { }
    internal static class EyeReachCensus { internal static void Tick() { } }
    internal static class NonDominantHold
    {
        internal static bool ShortTapThisFrame;
        internal static bool Consumed;
        internal static int PressId = 42;
    }
    internal static class Plugin
    {
        internal sealed class Flag { internal bool Value; }
        internal static readonly Flag DevMode = new();
    }
}
