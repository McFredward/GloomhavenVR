using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>The actual priest's shared bowl is the destination for every owner's purse.
/// Personal native browsing workspaces may relocate; the physical bowl never follows them.</summary>
internal static class TownServiceTempleBowl
{
    internal static readonly Vector3 Center = new(0f, .16f, .26f);
    internal static Transform Create(Transform priest)
    {
        Transform frame = new GameObject("GloomhavenVR.Temple.SharedBowl").transform;
        frame.SetParent(priest, false);
        frame.localPosition = TownServiceRitualLayout.Origin;
        return frame;
    }
    internal static bool Contains(Transform frame, Vector3 world)
    {
        Vector3 point = frame.InverseTransformPoint(world) - Center;
        return point.x * point.x + point.z * point.z < .095f * .095f
            && point.y > -.045f && point.y < .15f;
    }
}
