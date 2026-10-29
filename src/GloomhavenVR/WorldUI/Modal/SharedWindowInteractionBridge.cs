using System;

namespace GloomhavenVR.WorldUI;

/// <summary>The generic window/grab code has no dependency on the networking module.
/// An absent transport keeps native local interaction available.</summary>
internal static class SharedWindowInteractionBridge
{
    internal static Func<SharedWindowKind, bool>? CanBegin;
    internal static Func<SharedWindowKind, bool>? MayWrite;
    internal static Action<SharedWindowKind>? Began;
    internal static Action<SharedWindowKind>? Released;
    internal static Action<SharedWindowKind>? Canceled;
}
