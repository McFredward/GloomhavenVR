using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>Replay the owner's actual purse tween against the same rendered rig holder
/// used by scenario cards. The complete lifetime and endpoint remain authored data.</summary>
internal static class TownServiceReturnMotion
{
    internal static void Apply(Transform root, Transform holder, Transform shared, float[] values, float age)
    {
        float t = Mathf.Clamp01(age / values[1]);
        float ease = t * t * (3f - 2f * t);
        root.position = holder.TransformPoint(Vector3.Lerp(Position(values, 2), Position(values, 12), ease));
        root.rotation = shared.rotation * Quaternion.Slerp(Rotation(values, 2), Rotation(values, 12), ease);
        Vector3 worldScale = Vector3.Scale(holder.lossyScale, Vector3.Lerp(Scale(values, 2), Scale(values, 12), ease));
        Vector3 parentScale = root.parent != null ? root.parent.lossyScale : Vector3.one;
        root.localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
    }
    private static Vector3 Position(float[] v, int n) => new(v[n], v[n + 1], v[n + 2]);
    private static Quaternion Rotation(float[] v, int n) => new(v[n + 3], v[n + 4], v[n + 5], v[n + 6]);
    private static Vector3 Scale(float[] v, int n) => new(v[n + 7], v[n + 8], v[n + 9]);
}
