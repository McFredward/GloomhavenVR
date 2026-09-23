using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Closed-form work/attention timeline. Rendering rate and packet frequency cannot
/// change which activity phase a resident is performing. Reversals preserve the current pose.</summary>
internal struct TownActivityVisual
{
    internal Vector3 Left, Right;
    internal float Curl, Attention, Writing, Cast, CastSway;
    internal float LeftCurl, RightCurl, LeftRoll, RightRoll;
    internal Vector3 Chest, Coin0, Coin1, Coin2, CoinGrip;
    internal float EffectClock;
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
    // The previous imaginary writing loop has been removed. The merchant now counts
    // actual coins, with an explicit reach, pinch, inspection, deposit and release.
    internal static float Writing(float clock) => 0f;
    internal static Vector3 RestFocus(byte service) => service == 2
        ? new Vector3(0f, 1.16f, .34f) : new Vector3(0f, 1.01f, .28f);
    internal static TownActivityVisual Visual(byte service, in TownActivityPose state)
    {
        float attention = Blend(in state);
        var work = service == 1 ? Merchant(state.WorkClock) : service == 3 ? Enchantress(state.WorkClock) : Prayer(state.WorkClock);
        work.Attention = attention;
        // Interruption stops the shared work clock smoothly. A pinched coin stays in
        // the hand while greeting; it never slides through space back onto the table.
        if (service != 1) work.Left = Vector3.Lerp(work.Left, new Vector3(.21f, .959f, .337f), attention);
        work.Right = Vector3.Lerp(work.Right, service == 3 ? new Vector3(-.18f, 1.14f, .23f) : new Vector3(-.23f, .959f, .33f), attention);
        work.RightRoll = Mathf.Lerp(work.RightRoll, service == 3 ? 180f : 0f, attention);
        work.LeftRoll = Mathf.Lerp(work.LeftRoll, 0f, attention);
        work.RightCurl = Mathf.Lerp(work.RightCurl, service == 3 ? .08f : 0f, attention);
        work.LeftCurl = service == 1 ? work.LeftCurl : Mathf.Lerp(work.LeftCurl, 0f, attention);
        work.Chest = Vector3.Lerp(work.Chest, Vector3.zero, attention);
        work.Cast *= 1f - attention;
        work.Curl = Mathf.Max(work.LeftCurl, work.RightCurl);
        return work;
    }
    internal static TownActivityVisual Lerp(in TownActivityVisual from, in TownActivityVisual to, float t) => new TownActivityVisual {
        Left = Vector3.Lerp(from.Left, to.Left, t), Right = Vector3.Lerp(from.Right, to.Right, t),
        Curl = Mathf.Lerp(from.Curl, to.Curl, t), Attention = Mathf.Lerp(from.Attention, to.Attention, t),
        Writing = Mathf.Lerp(from.Writing, to.Writing, t), Cast = Mathf.Lerp(from.Cast, to.Cast, t),
        CastSway = Mathf.Lerp(from.CastSway, to.CastSway, t),
        LeftCurl = Mathf.Lerp(from.LeftCurl, to.LeftCurl, t), RightCurl = Mathf.Lerp(from.RightCurl, to.RightCurl, t),
        LeftRoll = Mathf.Lerp(from.LeftRoll, to.LeftRoll, t), RightRoll = Mathf.Lerp(from.RightRoll, to.RightRoll, t),
        Chest = Vector3.Lerp(from.Chest, to.Chest, t), Coin0 = Vector3.Lerp(from.Coin0, to.Coin0, t),
        Coin1 = Vector3.Lerp(from.Coin1, to.Coin1, t), Coin2 = Vector3.Lerp(from.Coin2, to.Coin2, t),
        CoinGrip = Vector3.Lerp(from.CoinGrip, to.CoinGrip, t), EffectClock = Mathf.Lerp(from.EffectClock, to.EffectClock, t) };
    internal static void Hands(byte service, in TownActivityPose state, out Vector3 left, out Vector3 right, out float curl)
    {
        var visual = Visual(service, in state);
        left = visual.Left; right = visual.Right; curl = visual.Curl;
    }

    private static float Ease(float time, float from, float to) => Mathf.SmoothStep(0f, 1f, (time - from) / (to - from));
    internal static Vector3 CoinSeat(int index, bool counted) => counted
        ? new Vector3(.08f, .970f + index * .003f, .34f)
        : new Vector3(.27f + index * .018f, .970f, .35f - index * .022f);

    private static TownActivityVisual Merchant(float clock)
    {
        float cycle = clock % 21.6f;
        int transfer = (int)(cycle / 3.6f), index = transfer < 3 ? transfer : 5 - transfer;
        bool returning = transfer >= 3;
        float t = cycle - transfer * 3.6f;
        Vector3 source = CoinSeat(index, returning), destination = CoinSeat(index, !returning);
        Vector3 home = new Vector3(.22f, 1.12f, .39f);
        Vector3 inspect = new Vector3(.13f, 1.22f, .34f);
        Vector3 hand;
        if (t < .65f) hand = Vector3.Lerp(home, source, Ease(t, 0f, .65f));
        else if (t < 1.12f) hand = source;
        else if (t < 1.65f) hand = Vector3.Lerp(source, inspect, Ease(t, 1.12f, 1.65f));
        else if (t < 2.05f) hand = inspect;
        else if (t < 2.75f) hand = Vector3.Lerp(inspect, destination, Ease(t, 2.05f, 2.75f));
        else if (t < 3.10f) hand = destination;
        else hand = Vector3.Lerp(destination, home, Ease(t, 3.10f, 3.6f));
        float grip = Ease(t, .68f, .96f) * (1f - Ease(t, 2.84f, 3.08f));
        var visual = new TownActivityVisual { Left = hand, Right = new Vector3(-.20f, .959f, .37f),
            LeftCurl = grip * .55f, RightCurl = .025f,
            Chest = new Vector3(-2f * Ease(t, .2f, .8f) * (1f - Ease(t, 2.8f, 3.6f)), -4f * grip, 0f) };
        for (int coin = 0; coin < 3; coin++)
        {
            bool counted = returning ? coin <= index : coin < index;
            Vector3 seat = CoinSeat(coin, counted);
            if (coin == index) seat = t < 2.75f ? source : destination;
            // Ownership changes only while the pinch is stationary on its exact seat.
            // An interruption can freeze a half-closed grasp without moving the coin
            // through empty space; curling a finger does not attract objects to it.
            float held = coin == index && t >= .96f && t < 2.84f ? 1f : 0f;
            if (coin == 0) { visual.Coin0 = seat; visual.CoinGrip.x = held; }
            else if (coin == 1) { visual.Coin1 = seat; visual.CoinGrip.y = held; }
            else { visual.Coin2 = seat; visual.CoinGrip.z = held; }
        }
        return visual;
    }

    private static TownActivityVisual Prayer(float clock)
    {
        float breath = .003f * Mathf.Sin(clock * 1.15f);
        return new TownActivityVisual { Left = new Vector3(.025f, 1.21f + breath, .39f),
            Right = new Vector3(-.025f, 1.21f + breath, .39f), LeftCurl = .10f, RightCurl = .10f };
    }

    private readonly struct SpellKey
    {
        internal readonly float Time, LeftRoll, RightRoll, Strength;
        internal readonly Vector3 Left, Right, Chest;
        internal SpellKey(float time, Vector3 left, Vector3 right, float leftRoll, float rightRoll, Vector3 chest, float strength)
        { Time = time; Left = left; Right = right; LeftRoll = leftRoll; RightRoll = rightRoll; Chest = chest; Strength = strength; }
    }
    // Authored contact-space animation: study, prepare, open the palms, shape a large
    // spell with both arms/torso, settle, then inspect a smaller spell above one palm.
    // Unequal holds and anticipations replace the old periodic wrist bobbing.
    private static readonly SpellKey[] Spell = {
        new(0f, new(.15f,1.035f,.39f), new(-.14f,1.035f,.40f), 0f,0f,new(-2f,0f,0f),0f),
        new(2.4f,new(.15f,1.035f,.39f), new(-.14f,1.035f,.40f), 0f,0f,new(-2f,0f,0f),0f),
        new(3.3f,new(.18f,1.10f,.43f), new(-.19f,1.08f,.43f), 25f,30f,new(2f,-4f,2f),0f),
        new(4.4f,new(.12f,1.24f,.34f), new(-.12f,1.21f,.32f), 110f,180f,new(0f,3f,-2f),.30f),
        new(5.4f,new(.22f,1.32f,.29f), new(-.24f,1.31f,.28f), 150f,170f,new(-3f,7f,-3f),1f),
        new(6.9f,new(.27f,1.30f,.33f), new(-.20f,1.38f,.35f), 125f,195f,new(1f,-7f,3f),.92f),
        new(8.1f,new(.11f,1.29f,.32f), new(-.11f,1.28f,.31f), 110f,180f,new(2f,2f,0f),.45f),
        new(9.0f,new(.21f,1.12f,.40f), new(-.19f,1.12f,.40f), 35f,50f,new(0f,0f,0f),0f),
        new(10.6f,new(.15f,1.035f,.39f),new(-.14f,1.035f,.40f),0f,0f,new(-2f,0f,0f),0f),
        new(13.2f,new(.15f,1.035f,.39f),new(-.14f,1.035f,.40f),0f,0f,new(-2f,0f,0f),0f),
        new(14.2f,new(.13f,1.11f,.38f),new(-.20f,1.24f,.30f),30f,180f,new(0f,-5f,1f),.12f),
        new(15.4f,new(.10f,1.25f,.30f),new(-.20f,1.27f,.30f),95f,180f,new(-1f,-7f,2f),.48f),
        new(17.0f,new(.15f,1.15f,.36f),new(-.16f,1.30f,.33f),45f,195f,new(1f,-3f,1f),.34f),
        new(18.4f,new(.15f,1.035f,.39f),new(-.14f,1.035f,.40f),0f,0f,new(-2f,0f,0f),0f),
        new(20.4f,new(.15f,1.035f,.39f),new(-.14f,1.035f,.40f),0f,0f,new(-2f,0f,0f),0f)
    };
    private static TownActivityVisual Enchantress(float clock)
    {
        float cycle = clock % 20.4f;
        int next = 1;
        while (next < Spell.Length - 1 && Spell[next].Time < cycle) next++;
        SpellKey a = Spell[next - 1], b = Spell[next];
        float t = Ease(cycle, a.Time, b.Time);
        return new TownActivityVisual { Left = Vector3.Lerp(a.Left,b.Left,t), Right = Vector3.Lerp(a.Right,b.Right,t),
            LeftRoll = Mathf.Lerp(a.LeftRoll,b.LeftRoll,t), RightRoll = Mathf.Lerp(a.RightRoll,b.RightRoll,t),
            Chest = Vector3.Lerp(a.Chest,b.Chest,t), Cast = Mathf.Lerp(a.Strength,b.Strength,t),
            LeftCurl = .13f, RightCurl = .08f, EffectClock = clock };
    }
}
