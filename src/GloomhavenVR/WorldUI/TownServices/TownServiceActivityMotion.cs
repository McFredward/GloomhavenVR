using GloomhavenVR.Net;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

/// <summary>Closed-form work/attention timeline. Rendering rate and packet frequency cannot
/// change which activity phase a resident is performing. Reversals preserve the current pose.</summary>
internal struct TownActivityVisual
{
    internal Vector3 Left, Right, LeftElbow, RightElbow;
    internal TownMotionBody Body;
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
        work.Right = Vector3.Lerp(work.Right, service == 1 ? new Vector3(-.20f, 1.18f, .20f)
            : service == 3 ? new Vector3(-.18f, 1.14f, .23f) : new Vector3(-.20f, .959f, .33f), attention);
        work.RightRoll = Mathf.Lerp(work.RightRoll, service != 2 ? 180f : 0f, attention);
        work.LeftRoll = Mathf.Lerp(work.LeftRoll, 0f, attention);
        work.RightCurl = Mathf.Lerp(work.RightCurl, service == 3 ? .08f : 0f, attention);
        work.LeftCurl = service == 1 ? work.LeftCurl : Mathf.Lerp(work.LeftCurl, 0f, attention);
        work.Chest = Vector3.Lerp(work.Chest, Vector3.zero, attention);
        work.Body.Weight *= 1f - attention;
        work.Cast *= 1f - attention;
        work.Curl = Mathf.Max(work.LeftCurl, work.RightCurl);
        return work;
    }
    internal static TownActivityVisual Lerp(in TownActivityVisual from, in TownActivityVisual to, float t) => new TownActivityVisual {
        Left = Vector3.Lerp(from.Left, to.Left, t), Right = Vector3.Lerp(from.Right, to.Right, t),
        LeftElbow = Vector3.Lerp(from.LeftElbow, to.LeftElbow, t), RightElbow = Vector3.Lerp(from.RightElbow, to.RightElbow, t),
        Body = TownMotionBody.Lerp(in from.Body, in to.Body, t),
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
        ? new Vector3(.24f, .970f + index * .003f, .29f)
        : new Vector3(.24f, .970f, .38f - index * .026f);

    private static readonly float[] TransferEnd = { 4.8f, 8.9f, 14.6f, 19.1f, 24.3f, 28.6f };
    private static TownActivityVisual Merchant(float clock)
    {
        // Unequal observation/settling intervals keep an occupation from becoming a
        // metronome. This schedule is authored once and shared, never random per peer.
        float cycle = clock % 28.6f;
        int transfer=0;
        while(transfer<5 && cycle>=TransferEnd[transfer]) transfer++;
        float start=transfer==0?0f:TransferEnd[transfer-1];
        int index=transfer<3?transfer:5-transfer;
        bool returning=transfer>=3;
        float segment=cycle-start, length=TransferEnd[transfer]-start;
        float t=segment<3.25f?segment:3.25f+(segment-3.25f)*.35f/(length-3.25f);
        Vector3 source = CoinSeat(index, returning), destination = CoinSeat(index, !returning);
        var visual = TownServiceMotionClips.Sample(returning ? 1 : 0, t / 3.6f);
        // Generated wrists provide the full motion arc. Exact native coin seats are
        // corrected only through stationary grasp/release windows; never attract a coin.
        // Refit the recorded reach to the compact lectern. The entire path moves
        // with the contact seats; stationary IK cannot drag a hand through the coat.
        Vector3 hand = visual.Left + new Vector3(-.05f, 0f, -.11f);
        hand.x = Mathf.Max(.24f, hand.x);
        hand.z = Mathf.Min(.31f, hand.z);
        Vector3 sourceDelta = source - CoinSeat(0, returning);
        Vector3 targetDelta = destination - CoinSeat(0, !returning);
        hand += Vector3.Lerp(sourceDelta, targetDelta, Ease(t, 1.12f, 2.75f))
            * Ease(t, 0f, .65f) * (1f-Ease(t, 3.10f, 3.6f));
        float contact = Ease(t, .43f, .65f) * (1f-Ease(t, 1.12f, 1.35f));
        hand = Vector3.Lerp(hand, source, contact);
        contact = Ease(t, 2.22f, 2.75f) * (1f-Ease(t, 3.10f, 3.32f));
        hand = Vector3.Lerp(hand, destination, contact);
        float grip = Ease(t, .68f, .96f) * (1f - Ease(t, 2.84f, 3.08f));
        visual.Left = hand; visual.Right = new Vector3(-.32f, .959f, .35f);
        visual.LeftCurl = grip * .55f; visual.RightCurl = .025f;
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
        var visual=TownServiceMotionClips.Sample(3, (clock % 8f)/8f);
        // Keep loosely cupped prayer hands at chest height. Their actual skin clears
        // the opposite fingers; sampled breathing supplies the quiet weight change.
        visual.Left = new Vector3(.035f,1.43f,.25f);
        visual.Right = new Vector3(-.035f,1.43f,.25f);
        visual.LeftCurl=0f; visual.RightCurl=0f;
        return visual;
    }

    private readonly struct SpellKey
    {
        internal readonly float Time, LeftRoll, RightRoll, Strength;
        internal SpellKey(float time, float leftRoll, float rightRoll, float strength)
        { Time=time; LeftRoll=leftRoll; RightRoll=rightRoll; Strength=strength; }
    }
    // Palm orientation and effects follow the source performance. Its final
    // recovery is retimed continuously below; hands and body keep one clock.
    private static readonly SpellKey[] Spell = {
        new(0f,0f,0f,0f), new(.6f,0f,0f,0f), new(1.2f,35f,45f,0f), new(1.65f,80f,100f,0f),
        new(2.2f,110f,180f,.30f), new(2.7f,150f,170f,1f),
        new(3.45f,125f,195f,.92f), new(4.05f,110f,180f,.45f),
        new(4.9f,35f,50f,0f), new(5.3f,0f,0f,0f), new(6.1f,0f,0f,0f),
        new(7.1f,30f,180f,.12f), new(7.7f,95f,180f,.48f),
        new(8.5f,45f,165f,.34f), new(9.8f,0f,0f,0f), new(10.2f,0f,0f,0f)
    };
    private static float Retimed(float time, float sourceLength, float shownLength)
    {
        // Preserve source velocity at both endpoints while giving the middle of a
        // fast generated gesture enough time for the actual retargeted limb.
        float phase = time / shownLength, slope = shownLength / sourceLength;
        return sourceLength * (slope * phase + (3f - 3f * slope) * phase * phase
            + (2f * slope - 2f) * phase * phase * phase);
    }
    private static TownActivityVisual Enchantress(float clock)
    {
        float cycle = clock % 12.7f;
        if (cycle > 9f) cycle = 8.5f + Retimed(cycle - 9f, 1.7f, 3.7f);
        else if (cycle > 4.55f) cycle -= .5f;
        else if (cycle > 2.7f) cycle = 2.7f + Retimed(cycle - 2.7f, 1.35f, 1.85f);
        int next = 1;
        while (next < Spell.Length - 1 && Spell[next].Time < cycle) next++;
        SpellKey a = Spell[next - 1], b = Spell[next];
        float t = Ease(cycle, a.Time, b.Time);
        var generated=TownServiceMotionClips.Sample(2, cycle / 10.2f);
        float leftRest = 1f - Mathf.SmoothStep(0f, 1f, (generated.Left.y - 1.04f) / .14f);
        float rightRest = 1f - Mathf.SmoothStep(0f, 1f, (generated.Right.y - 1.04f) / .14f);
        generated.Left = Vector3.Lerp(generated.Left, new Vector3(Mathf.Max(.19f, generated.Left.x), Mathf.Max(1.02f, generated.Left.y), Mathf.Min(.32f, generated.Left.z)), leftRest);
        generated.Right = Vector3.Lerp(generated.Right, new Vector3(Mathf.Min(-.19f, generated.Right.x), Mathf.Max(1.02f, generated.Right.y), Mathf.Min(.32f, generated.Right.z)), rightRest);
        return new TownActivityVisual { Left = generated.Left, Right = generated.Right,
            LeftElbow=generated.LeftElbow, RightElbow=generated.RightElbow, Body=generated.Body,
            LeftRoll = Mathf.Lerp(a.LeftRoll,b.LeftRoll,t), RightRoll = Mathf.Lerp(a.RightRoll,b.RightRoll,t),
            Chest = Vector3.zero, Cast = Mathf.Lerp(a.Strength,b.Strength,t),
            LeftCurl = .13f, RightCurl = .08f, EffectClock = clock };
    }
}
