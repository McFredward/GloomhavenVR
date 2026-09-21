using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Bounded, allocation-free facial mathematics shared by the owner and its mirrors.</summary>
internal static class TownServiceFaceMotion
{
    internal static float Weight(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
    internal static Vector2 LookAngles(Vector3 direction)
    {
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(direction.y, Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z)) * Mathf.Rad2Deg;
        return new Vector2(pitch, yaw);
    }
    internal static TownFacePose Aim(Quaternion optical, float scale, Vector3 pivot, Vector3 left, Vector3 right, Vector3? target, in TownFacePose previous, float delta)
    {
        var result = previous;
        Vector2 desired = target.HasValue ? LookAngles((Quaternion.Inverse(optical) * (target.Value - (left + right) * .5f))) : Vector2.zero;
        float dt = Mathf.Clamp(delta, 0f, .1f);
        // Eyes lead; the slower head follows within comfortable anatomical limits. No target
        // means a smooth neutral return, not a stale stare into a departed player's position.
        result.HeadPitch = Mathf.MoveTowards(previous.HeadPitch, Mathf.Clamp(desired.x * .6f, -22f, 22f), 55f * dt);
        result.HeadYaw = Mathf.MoveTowards(previous.HeadYaw, Mathf.Clamp(desired.y * .68f, -50f, 50f), 80f * dt);
        result.HeadRoll = Mathf.MoveTowards(previous.HeadRoll, 0f, 20f * dt);
        Quaternion head = Quaternion.Euler(result.HeadPitch, result.HeadYaw, result.HeadRoll);
        Quaternion deltaRotation = optical * head * Quaternion.Inverse(optical);
        left = pivot + deltaRotation * (left - pivot);
        right = pivot + deltaRotation * (right - pivot);
        Vector3 midpoint = (left + right) * .5f;
        Vector3 commonDirection = target.HasValue ? target.Value - midpoint : optical * Vector3.forward * 3f;
        // Do not let an HMD pushed inside the face create divergent or cross-eyed extremes.
        float distance = Mathf.Max(.65f * scale, commonDirection.magnitude);
        Vector3 focus = midpoint + commonDirection.normalized * distance;
        Vector2 l = LookAngles(Quaternion.Inverse(head) * (Quaternion.Inverse(optical) * (focus - left)));
        Vector2 r = LookAngles(Quaternion.Inverse(head) * (Quaternion.Inverse(optical) * (focus - right)));
        result.LeftPitch = Mathf.MoveTowards(previous.LeftPitch, Mathf.Clamp(l.x, -15f, 15f), 190f * dt);
        result.RightPitch = Mathf.MoveTowards(previous.RightPitch, Mathf.Clamp(r.x, -15f, 15f), 190f * dt);
        result.LeftYaw = Mathf.MoveTowards(previous.LeftYaw, Mathf.Clamp(l.y, -25f, 25f), 220f * dt);
        result.RightYaw = Mathf.MoveTowards(previous.RightYaw, Mathf.Clamp(r.y, -25f, 25f), 220f * dt);
        return result;
    }
    internal static float Blink(float clock, byte service)
    {
        // The complete closure/reopening is evaluated on every render frame, never captured
        // only at presence cadence. Service-specific offsets prevent synchronized blinking.
        float time = Mathf.Max(0f, clock) + service * 1.713f;
        int cycle = Mathf.FloorToInt(time / 5.2f);
        float at = time - cycle * 5.2f;
        float start = 2.05f + .65f * Mathf.Sin(cycle * 2.39996f + service);
        float t = at - start;
        if (t < 0f || t > .22f) return 0f;
        return t < .075f ? Mathf.SmoothStep(0f, 1f, t / .075f) : 1f - Mathf.SmoothStep(0f, 1f, (t - .075f) / .145f);
    }
    internal static TownServiceFacePose Evaluate(in TownFacePose state, float clock, byte service, Vector3 mouth)
    {
        float blink = Blink(clock, service);
        return new TownServiceFacePose {
            HeadPitch = state.HeadPitch, HeadYaw = state.HeadYaw, HeadRoll = state.HeadRoll,
            LeftPitch = state.LeftPitch, LeftYaw = state.LeftYaw, RightPitch = state.RightPitch, RightYaw = state.RightYaw,
            BlinkLeft = blink, BlinkRight = blink,
            JawOpen = state.Cue == 0 ? 0f : Weight(mouth.x),
            MouthWide = state.Cue == 0 ? 0f : Weight(mouth.y),
            MouthRound = state.Cue == 0 ? 0f : Weight(mouth.z),
            Smile = .06f + .025f * Mathf.Sin(clock * .43f + service * 2f),
            BrowRaise = .025f + .018f * Mathf.Sin(clock * .61f + service * 1.7f) };
    }
    internal static TownFacePose Interpolate(in TownFacePose from, in TownFacePose to, float t)
    {
        var result = to;
        result.HeadPitch = Mathf.Lerp(from.HeadPitch, to.HeadPitch, t);
        result.HeadYaw = Mathf.Lerp(from.HeadYaw, to.HeadYaw, t);
        result.HeadRoll = Mathf.Lerp(from.HeadRoll, to.HeadRoll, t);
        result.LeftPitch = Mathf.Lerp(from.LeftPitch, to.LeftPitch, t);
        result.LeftYaw = Mathf.Lerp(from.LeftYaw, to.LeftYaw, t);
        result.RightPitch = Mathf.Lerp(from.RightPitch, to.RightPitch, t);
        result.RightYaw = Mathf.Lerp(from.RightYaw, to.RightYaw, t);
        return result;
    }
}
