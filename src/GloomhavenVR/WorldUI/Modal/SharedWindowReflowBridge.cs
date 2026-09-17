using System;

namespace GloomhavenVR.WorldUI;

/// <summary>Net registers shared-pose authority without making WorldUI depend on its transport.
/// An unregistered online bridge declines layout; native windows remain visible and interactive.</summary>
internal static class SharedWindowReflowBridge
{
    internal static Func<bool>? CanArrange { get; set; }
    internal static Func<SharedWindowKind, bool>? Begin { get; set; }
    internal static Func<SharedWindowKind, int>? Revision { get; set; }
    internal static Action<SharedWindowKind>? End { get; set; }
}
