using System;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>Distinguish a real destination exit from a map-surface-only switch.
/// Both use the same native map Toggle. Only the former must run the service's
/// Exit, restore its party selection and finish any pending onboarding promise.</summary>
internal static class TownWindowCloseScope
{
    [ThreadStatic] private static int _depth;
    internal static bool Active => _depth > 0;
    internal static Scope Enter(bool enabled = true)
    {
        if (enabled) _depth++;
        return new Scope(enabled);
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly bool _entered;
        internal Scope(bool entered) { _entered = entered; }
        public void Dispose() { if (_entered) _depth--; }
    }
}
