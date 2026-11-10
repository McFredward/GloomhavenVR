using System.Runtime.CompilerServices;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>Independent content-recovery cadence for each original native window.
/// A permanently empty sibling must not consume another window's recovery polls.</summary>
internal sealed class WindowProbeCadence
{
    private sealed class Deadline
    {
        internal float Next;
        internal float Last;
    }

    private ConditionalWeakTable<UIWindow, Deadline> _windows = new();

    internal bool Due(UIWindow window, float now)
    {
        if (!_windows.TryGetValue(window, out Deadline? deadline))
        {
            deadline = new Deadline();
            _windows.Add(window, deadline);
        }
        if (now >= deadline.Last && now < deadline.Next)
            return false;
        deadline.Last = now;
        deadline.Next = now + 1f / 6f;
        return true;
    }

    internal void Forget(UIWindow window) => _windows.Remove(window);
    internal void Reset() => _windows = new ConditionalWeakTable<UIWindow, Deadline>();
}
