using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Closed-form work/attention timeline. Rendering rate and packet frequency cannot
/// change which activity phase a resident is performing. Reversals preserve the current pose.</summary>
internal static class TownServiceActivityMotion
{
    internal const float TransitionSeconds = .65f;
    internal static float Blend(in TownActivityPose state)
    {
        float t = Mathf.Clamp01(state.TransitionAge / TransitionSeconds);
        return state.FromBlend + ((state.Engaged ? 1f : 0f) - state.FromBlend) * t * t * (3f - 2f * t);
    }
    private static float Integral(in TownActivityPose state, float age)
    {
        float t = Mathf.Clamp01(age / TransitionSeconds), target = state.Engaged ? 1f : 0f;
        return state.FromBlend * Mathf.Min(age, TransitionSeconds)
            + (target - state.FromBlend) * TransitionSeconds * (t * t * t - .5f * t * t * t * t)
            + target * Mathf.Max(0f, age - TransitionSeconds);
    }
    internal static TownActivityPose Advance(TownActivityPose state, float seconds)
    {
        float dt = Mathf.Max(0f, seconds);
        state.WorkClock += Mathf.Max(0f, dt - Integral(in state, state.TransitionAge + dt) + Integral(in state, state.TransitionAge));
        state.TransitionAge = Mathf.Min(TransitionSeconds, state.TransitionAge + dt);
        return state;
    }
    internal static void Engage(ref TownActivityPose state, bool wanted)
    {
        if (wanted == state.Engaged) return;
        state.FromBlend = Blend(in state); state.TransitionAge = 0f; state.Engaged = wanted;
    }
    internal static float Wave(float clock, float period) => .5f - .5f * Mathf.Cos(clock * (2f * Mathf.PI / period));
    internal static float Pulse(float cycle, float from, float to)
    {
        if (cycle <= from || cycle >= to) return 0f;
        return Mathf.Sin((cycle - from) / (to - from) * Mathf.PI);
    }
    internal static float Writing(float clock)
    {
        float cycle = clock % 14f;
        return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((cycle - 5f) / 1.2f))
            * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((cycle - 11.5f) / 1.2f)));
    }
    internal static Vector3 RestFocus(byte service) => service == 2
        ? new Vector3(0f, 1.16f, .34f) : new Vector3(0f, 1.01f, .28f);
    internal static void Hands(byte service, in TownActivityPose state, out Vector3 left, out Vector3 right, out float curl)
    {
        float clock = state.WorkClock, cycle = clock % 14f;
        // Coordinates are metres in the stable station frame. The back edge of the actual
        // counter is within arm reach; no hand target is placed inside the catalog rack.
        if (service == 1)
        {
            float writing = Writing(clock);
            left = new Vector3(-.20f + .025f * Mathf.Sin(clock * 2.3f), 1.10f + .018f * Wave(clock, 1.4f), .36f);
            Vector3 restingPen = new Vector3(.22f, 1.10f, .36f);
            Vector3 ledger = new Vector3(.14f + .010f * Mathf.Sin(clock * 10f), 1.045f, .29f + .012f * Mathf.Sin(clock * 4f));
            right = Vector3.Lerp(restingPen, ledger, writing); curl = .55f;
        }
        else if (service == 2)
        {
            float breath = .004f * Mathf.Sin(clock * 1.15f);
            left = new Vector3(-.025f, 1.21f + breath, .39f);
            right = new Vector3(.025f, 1.21f + breath, .39f); curl = .10f;
        }
        else
        {
            float cast = Pulse(cycle, 7f, 12f);
            left = new Vector3(-.15f, 1.075f, .42f);
            right = new Vector3(.14f + .035f * cast * Mathf.Sin(clock * 1.8f),
                1.075f + .17f * cast, .42f - .045f * cast);
            curl = .22f + .18f * cast;
        }
        float attentive = Blend(in state);
        // Both hands settle visibly above the counter (or open from prayer); a partially
        // interrupted activity resumes from its stopped work clock rather than starting over.
        left = Vector3.Lerp(left, new Vector3(-.22f, 1.095f, .43f), attentive);
        right = Vector3.Lerp(right, new Vector3(.22f, 1.095f, .43f), attentive);
        curl = Mathf.Lerp(curl, .15f, attentive);
        // Anatomical left is station +X: the imported actor faces inward along station -Z.
        left.x = -left.x; right.x = -right.x;
    }
}
