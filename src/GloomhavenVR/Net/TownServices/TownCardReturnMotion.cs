using GloomhavenVR.Hands;
using GloomhavenVR.Cards;
using UnityEngine;

namespace GloomhavenVR.Net.TownServices;

/// <summary>The existing original card tween, replayed every render frame. Native child offsets
/// remain relative to the card root: interpolating flattened face endpoints displaces them while
/// the original card rotates. No artwork or gameplay instruction is carried by this clock.</summary>
internal static class TownCardReturnMotion
{
    internal static float[] Capture(Transform source, Transform root, Transform shared, VRHand? hand,
        float age, float seconds, byte curve, float parameter, Matrix4x4 from, Quaternion fromRotation,
        Matrix4x4 to, Quaternion toRotation, Vector3 arc)
    {
        var values = new float[38]; values[0] = age; values[1] = seconds;
        values[2] = curve; values[3] = parameter;
        Write(values, 4, from, fromRotation, shared, hand);
        Write(values, 14, to, toRotation, shared, hand);
        Vector3 localArc = hand != null
            ? Quaternion.Inverse(hand.Rig.Root.rotation) * arc / Mathf.Max(.0001f, Mathf.Abs(hand.WorldScale))
            : shared.InverseTransformVector(arc);
        values[24] = localArc.x; values[25] = localArc.y; values[26] = localArc.z;
        Matrix4x4 child = root.worldToLocalMatrix * source.localToWorldMatrix;
        Put(values, 28, child.MultiplyPoint3x4(Vector3.zero),
            Quaternion.Inverse(root.rotation) * source.rotation,
            new Vector3(child.GetColumn(0).magnitude, child.GetColumn(1).magnitude, child.GetColumn(2).magnitude));
        return values;
    }

    private static void Write(float[] values, int at, Matrix4x4 world, Quaternion rotation,
        Transform shared, VRHand? hand)
    {
        if (hand == null)
        {
            Matrix4x4 local = shared.worldToLocalMatrix * world;
            Put(values, at, local.MultiplyPoint3x4(Vector3.zero), Quaternion.Inverse(shared.rotation) * rotation,
                new Vector3(local.GetColumn(0).magnitude, local.GetColumn(1).magnitude, local.GetColumn(2).magnitude));
            return;
        }
        float size = Mathf.Max(.0001f, Mathf.Abs(hand.WorldScale));
        Vector3 position = Quaternion.Inverse(hand.Rig.Root.rotation)
            * (world.MultiplyPoint3x4(Vector3.zero) - hand.Rig.Root.position) / size;
        Put(values, at, position, Quaternion.Inverse(shared.rotation) * rotation,
            new Vector3(world.GetColumn(0).magnitude, world.GetColumn(1).magnitude, world.GetColumn(2).magnitude) / size);
    }

    private static void Put(float[] values, int at, Vector3 p, Quaternion q, Vector3 s)
    {
        values[at] = p.x; values[at + 1] = p.y; values[at + 2] = p.z;
        values[at + 3] = q.x; values[at + 4] = q.y; values[at + 5] = q.z; values[at + 6] = q.w;
        values[at + 7] = s.x; values[at + 8] = s.y; values[at + 9] = s.z;
    }

    internal static void Apply(Transform source, Transform holder, Transform shared, byte hand,
        float[] values, float age)
    {
        float t = Mathf.Clamp01(age / values[1]);
        float ease = (byte)values[2] switch
        {
            1 => t >= 1f ? 1f : 1f - Mathf.Exp(-values[3] * age),
            2 => VRCard.SmootherStep(t),
            3 => t * t * ((values[3] + 1f) * t - values[3]),
            _ => t * t * (3f - 2f * t)
        };
        Vector3 position = Vector3.LerpUnclamped(Position(values, 4), Position(values, 14), ease);
        if (values[2] == 2f) position += VRCard.FlyArcOffset(ease, Position(values, 24), 1f);
        Quaternion rotation = Quaternion.SlerpUnclamped(Rotation(values, 4), Rotation(values, 14),
            values[2] == 3f ? t : ease);
        rotation = (hand > 2 ? shared.rotation : holder.rotation) * rotation;
        Vector3 scale = Vector3.Scale(holder.lossyScale,
            Vector3.LerpUnclamped(Scale(values, 4), Scale(values, 14), ease));
        Matrix4x4 world = Matrix4x4.TRS(holder.TransformPoint(position), rotation, scale)
            * Matrix4x4.TRS(Position(values, 28), Rotation(values, 28), Scale(values, 28));
        source.SetPositionAndRotation(world.MultiplyPoint3x4(Vector3.zero), rotation * Rotation(values, 28));
        Vector3 parent = source.parent != null ? source.parent.lossyScale : Vector3.one;
        source.localScale = new Vector3(world.GetColumn(0).magnitude / parent.x,
            world.GetColumn(1).magnitude / parent.y, world.GetColumn(2).magnitude / parent.z);
    }

    private static Vector3 Position(float[] v, int n) => new(v[n], v[n + 1], v[n + 2]);
    private static Quaternion Rotation(float[] v, int n) => new(v[n + 3], v[n + 4], v[n + 5], v[n + 6]);
    private static Vector3 Scale(float[] v, int n) => new(v[n + 7], v[n + 8], v[n + 9]);
}
