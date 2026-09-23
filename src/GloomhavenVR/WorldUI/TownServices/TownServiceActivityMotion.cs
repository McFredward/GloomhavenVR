using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Closed-form work/attention timeline. Rendering rate and packet frequency cannot
/// change which activity phase a resident is performing. Reversals preserve the current pose.</summary>
internal struct TownActivityVisual
{
    internal Vector3 Left, Right;
    internal float Curl, Attention, Writing, Cast, CastSway;
}
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
    internal static TownActivityVisual Visual(byte service, in TownActivityPose state)
    {
        Hands(service, in state, out Vector3 left, out Vector3 right, out float curl);
        float attention = Blend(in state);
        return new TownActivityVisual { Left = left, Right = right, Curl = curl, Attention = attention,
            Writing = service == 1 ? Writing(state.WorkClock) * (1f - attention) : 0f,
            Cast = Pulse(state.WorkClock % 14f, 7f, 12f) * (1f - attention),
            CastSway = .018f * Mathf.Sin(state.WorkClock * 2f) };
    }
    internal static TownActivityVisual Lerp(in TownActivityVisual from, in TownActivityVisual to, float t) => new TownActivityVisual {
        Left = Vector3.Lerp(from.Left, to.Left, t), Right = Vector3.Lerp(from.Right, to.Right, t),
        Curl = Mathf.Lerp(from.Curl, to.Curl, t), Attention = Mathf.Lerp(from.Attention, to.Attention, t),
        Writing = Mathf.Lerp(from.Writing, to.Writing, t), Cast = Mathf.Lerp(from.Cast, to.Cast, t),
        CastSway = Mathf.Lerp(from.CastSway, to.CastSway, t) };
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
            Vector3 ledger = new Vector3(.134f + .010f * Mathf.Sin(clock * 10f), 1.060f, .36f + .012f * Mathf.Sin(clock * 4f));
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
        // Both palms settle onto the counter (or open from prayer); a partially
        // interrupted activity resumes from its stopped work clock rather than starting over.
        float restWidth = service == 1 ? .23f : .21f;
        float restDepth = service == 1 ? .33f : .337f;
        left = Vector3.Lerp(left, new Vector3(-restWidth, .959f, restDepth), attentive);
        right = Vector3.Lerp(right, new Vector3(restWidth, .959f, restDepth), attentive);
        curl = Mathf.Lerp(curl, 0f, attentive);
        // Anatomical left is station +X: the imported actor faces inward along station -Z.
        left.x = -left.x; right.x = -right.x;
    }
}
