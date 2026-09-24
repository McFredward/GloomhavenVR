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
        if (service != 1) work.Left = Vector3.Lerp(work.Left, service == 2
            ? new Vector3(.012f, 1.23f, .20f) : new Vector3(.22f, 1.13f, .23f), attention);
        work.Right = Vector3.Lerp(work.Right, service == 1 ? new Vector3(-.20f, 1.18f, .20f)
            : service == 3 ? new Vector3(-.18f, 1.17f, .23f) : new Vector3(-.012f, 1.23f, .20f), attention);
        work.RightRoll = Mathf.Lerp(work.RightRoll, service != 2 ? 180f : 0f, attention);
        work.LeftRoll = Mathf.Lerp(work.LeftRoll, service == 3 ? 65f : 0f, attention);
        work.RightCurl = Mathf.Lerp(work.RightCurl, service == 3 ? .08f : 0f, attention);
        work.LeftCurl = service == 1 ? work.LeftCurl : Mathf.Lerp(work.LeftCurl, service == 3 ? .26f : 0f, attention);
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
        // A full set of coins returns to its original seats before the resident
        // pauses to inspect the ledger. The pause changes each shared clock block;
        // neither observer frame rate nor a local random generator selects it.
        float block = Mathf.Floor(clock / 48f), phase = clock - block * 48f;
        float delay = block == 0f ? 0f : Variation((uint)block, 17u) * 10f;
        float cycle = Mathf.Clamp(phase - delay, 0f, 28.6f);
        if (cycle >= 28.6f) cycle = 0f;
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
        visual.Left = hand; visual.Right = new Vector3(-.32f, 1.13f, .29f);
        visual.RightRoll = 65f;
        visual.LeftCurl = grip * .55f; visual.RightCurl = .26f;
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
        var visual=TownServiceMotionClips.Sample(3, (clock % 13f)/13f);
        uint block = (uint)Mathf.Floor(clock / 64f);
        float phase = clock - block * 64f, pause = 20f + Variation(block, 29u) * 20f;
        float rest = Soft(phase, pause, pause + 2.2f)
            * (1f - Soft(phase, pause + 7f, pause + 10f));
        float height = Mathf.Lerp(1.34f, 1.23f, rest);
        visual.Body.Weight = .55f;
        // Palm targets are actual skin surfaces, not wrist centres. The old +/-35 mm
        // targets left a visible 70 mm gap. Join the cupped hands at the sternum while
        // retaining a small skin allowance and the recorded breathing motion.
        visual.Left = new Vector3(.012f,height,.20f);
        visual.Right = new Vector3(-.012f,height,.20f);
        visual.LeftCurl=0f; visual.RightCurl=0f;
        return visual;
    }

    // Stateless variation is a pure function of the replicated occupation clock.
    // Every phrase starts and ends in the same reading pose, with zero velocity;
    // a late observer or ownership handover therefore sees the same performance.
    private static float Variation(uint block, uint salt)
    {
        uint value = unchecked(block * 747796405u + salt * 2891336453u + 277803737u);
        value = unchecked((value ^ (value >> 16)) * 2246822519u);
        return (value & 65535u) / 65535f;
    }
    private static float Soft(float time, float from, float to)
    {
        float t = Mathf.Clamp01((time - from) / (to - from));
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
    private static TownActivityVisual Enchantress(float clock)
    {
        // Hardware550 showed high elbows with hanging wrists, followed by the same
        // second flourish a few seconds later. Do not retime that unsuitable stage
        // performance again. One quiet, palm-supported experiment is surrounded by
        // long reading intervals, with distinct reach/observation/recovery timing.
        uint block = (uint)Mathf.Floor(clock / 48f);
        float phase = clock - block * 48f;
        float start = 12f + 10f * Variation(block, 3u);
        float length = 8f + 3f * Variation(block, 7u);
        float t = (phase - start) / length;
        float lift = Soft(t, 0f, .25f) * (1f - Soft(t, .68f, 1f));
        float shape = Soft(t, .18f, .4f) * (1f - Soft(t, .55f, .8f));
        float turn = Soft(t, 0f, .17f) * (1f - Soft(t, .78f, 1f));
        float strength = .55f + .45f * Variation(block, 11u);
        float cast = Soft(t, .28f, .42f) * (1f - Soft(t, .56f, .69f)) * strength;
        var quiet = TownServiceMotionClips.Sample(3, (clock % 13f) / 13f);
        var gesture = TownServiceMotionClips.Sample(2, Mathf.Clamp01(t));
        // The complete torso performance shares the hand envelope. Its restrained
        // contribution starts with the reach and subsides with the recovery; no
        // isolated arm cycle or high-frequency knee wobble is added afterwards.
        quiet.Body.Weight = .22f;
        gesture.Body.Weight = .42f;
        var body = TownMotionBody.Lerp(in quiet.Body, in gesture.Body, lift);
        return new TownActivityVisual {
            Left = Vector3.Lerp(new Vector3(.22f, 1.11f, .23f),
                new Vector3(.18f, 1.27f, .17f), shape),
            Right = Vector3.Lerp(new Vector3(-.22f, 1.11f, .23f),
                new Vector3(-.20f, 1.26f, .17f), lift),
            LeftElbow = new Vector3(.40f, .84f, .26f),
            RightElbow = new Vector3(-.40f, .84f, .26f), Body = body,
            LeftRoll = 65f - 30f * shape, RightRoll = 65f + 115f * turn,
            Cast = cast, LeftCurl = Mathf.Lerp(.26f, .10f, shape),
            RightCurl = Mathf.Lerp(.26f, .08f, lift), EffectClock = clock
        };
    }
}
