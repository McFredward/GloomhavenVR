using UnityEngine;

namespace GloomhavenVR.WorldUI.MenuDepth;

// Other projection entry points compile against the real Unity modules but are not executed by
// this math suite. Their unrelated XR eye observation is an explicit boundary, not a fake normal.
internal readonly struct MenuDepthEye
{
    internal bool Right => false;
    internal Camera HeadCamera => null!;
    internal Vector3 WorldPosition => Vector3.zero;
}
