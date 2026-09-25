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
    internal const float TransitionSeconds = .70f;
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
    internal static bool MerchantCanAttend(float clock)
    {
        // Build 560: entering attention in the middle of a transfer froze a coin
        // between the fingertips and the counter. The author finishes the current
        // counted coin first; the resulting pose is the one observers receive.
        float cycle = clock - Mathf.Floor(clock / MerchantCycleSeconds) * MerchantCycleSeconds;
        int transfer = 0;
        while (transfer < 5 && cycle >= TransferEnd[transfer]) transfer++;
        float start = transfer == 0 ? 0f : TransferEnd[transfer - 1];
        float t = MerchantTime(cycle - start, TransferEnd[transfer] - start,
            (uint)Mathf.Floor(clock / MerchantCycleSeconds) * 6u + (uint)transfer);
        // The hand may abandon an ungripped reach or finish its released return;
        // neither case leaves a coin hovering when the work clock eases to rest.
        return t < .50f || t >= 3.05f;
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
        // Looking at a visitor is separate from offering an item hand to that visitor.
        // Keep the merchant's and priestess's hands on their OWN hips, behind the front
        // edge of the worktops. Build 563's "lowered" targets still put both wrists on
        // the table in the headset screenshots because they were authored forward of the
        // torso. Hands on hips need a rearward wrist, an outward elbow and a relaxed shoulder.
        work.Left = Vector3.Lerp(work.Left, service == 1 ? new Vector3(.32f, .97f, .04f)
            : service == 2 ? new Vector3(.30f, .87f, .015f) : new Vector3(.22f, 1.13f, .23f), attention);
        work.Right = Vector3.Lerp(work.Right, service == 1 ? new Vector3(-.32f, .97f, .04f)
            : service == 3 ? new Vector3(-.18f, 1.17f, .23f) : new Vector3(-.30f, .87f, .015f), attention);
        // Hand targets alone cannot lower an arm naturally. Author the matching elbow path as
        // part of the same blend so the upper arm leaves the shoulder downward instead of staying
        // abducted while the forearm reaches for a low hand target.
        if (service is 1 or 2)
        {
            float side = service == 1 ? .42f : .43f;
            work.LeftElbow = Vector3.Lerp(work.LeftElbow,
                new Vector3(side, service == 1 ? 1.05f : 1.06f, service == 1 ? .07f : .06f), attention);
            work.RightElbow = Vector3.Lerp(work.RightElbow,
                new Vector3(-side, service == 1 ? 1.05f : 1.06f, service == 1 ? .07f : .06f), attention);
        }
        work.RightRoll = Mathf.Lerp(work.RightRoll, service == 1 ? 65f : service == 3 ? 180f : 38f, attention);
        work.LeftRoll = Mathf.Lerp(work.LeftRoll, service == 1 ? -65f : service == 3 ? -65f : -38f, attention);
        work.RightCurl = Mathf.Lerp(work.RightCurl, service == 1 ? .22f : service == 3 ? .08f : .06f, attention);
        work.LeftCurl = Mathf.Lerp(work.LeftCurl, service == 1 ? .22f : service == 3 ? .26f : .06f, attention);
        work.Chest = Vector3.Lerp(work.Chest, Vector3.zero, attention);
        work.Body.Weight *= 1f - attention;
        work.Cast *= 1f - attention;
        work.CastSway *= 1f - attention;
        work.Curl = Mathf.Max(work.LeftCurl, work.RightCurl);
        return work;
    }

    /// <summary>Blend the attentive priestess from hands-on-hips into a deliberate closed-bowl
    /// pose after the native service reports that this visitor cannot donate again. The caller
    /// owns and replicates <paramref name="blend"/>; this method never reads local gameplay state.</summary>
    internal static void ApplyTempleAvailability(ref TownActivityVisual visual, bool donationAvailable, float blend)
    {
        if (donationAvailable) return;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend)) * visual.Attention;
        visual.Left = Vector3.Lerp(visual.Left, new Vector3(.085f, 1.205f, .18f), t);
        visual.Right = Vector3.Lerp(visual.Right, new Vector3(-.085f, 1.205f, .18f), t);
        visual.LeftElbow = Vector3.Lerp(visual.LeftElbow, new Vector3(.31f, 1.10f, .12f), t);
        visual.RightElbow = Vector3.Lerp(visual.RightElbow, new Vector3(-.31f, 1.10f, .12f), t);
        visual.LeftRoll = Mathf.Lerp(visual.LeftRoll, -82f, t);
        visual.RightRoll = Mathf.Lerp(visual.RightRoll, 82f, t);
        visual.LeftCurl = Mathf.Lerp(visual.LeftCurl, .18f, t);
        visual.RightCurl = Mathf.Lerp(visual.RightCurl, .18f, t);
        visual.Curl = Mathf.Max(visual.LeftCurl, visual.RightCurl);
    }
    internal static void ApplyMerchantOffering(ref TownActivityVisual visual, float blend)
    {
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend)) * visual.Attention;
        visual.Right = Vector3.Lerp(visual.Right, new Vector3(-.20f, 1.18f, .20f), t);
        visual.RightRoll = Mathf.Lerp(visual.RightRoll, 180f, t);
        visual.RightCurl = Mathf.Lerp(visual.RightCurl, 0f, t);
        visual.Curl = Mathf.Max(visual.LeftCurl, visual.RightCurl);
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
        ? new Vector3(.24f, .958f + index * .003f, .29f)
        : new Vector3(.24f, .958f, .38f - index * .026f);

    internal const float MerchantCycleSeconds = 28.6f;
    private static readonly float[] TransferEnd = { 4.8f, 8.9f, 14.6f, 19.1f, 24.3f, MerchantCycleSeconds };
    private static readonly float[] CoinPhase = { 0f, .65f, 1.12f, 1.70f, 2f, 2.75f, 3.10f, 3.6f };
    private static readonly float[] CoinDuration = { .70f, .43f, .65f, .35f, .80f, .38f, .70f };
    private static float MerchantTime(float segment, float length, uint phrase)
    {
        // Independently vary approach, grasp, inspection and release durations,
        // rather than globally speeding up the same mechanical movement. All
        // choices derive from the replicated work clock; no observer-local RNG.
        float sum = 0f;
        for (int i = 0; i < CoinDuration.Length; i++)
            sum += CoinDuration[i] * (.78f + .44f * Variation(phrase, (uint)(47 + i * 13)));
        float scale = length / sum, start = 0f;
        for (int i = 0; i < CoinDuration.Length; i++)
        {
            float duration = CoinDuration[i] * (.78f + .44f * Variation(phrase, (uint)(47 + i * 13))) * scale;
            if (segment <= start + duration)
                return Mathf.Lerp(CoinPhase[i], CoinPhase[i + 1], (segment - start) / duration);
            start += duration;
        }
        return 3.6f;
    }
    private static TownActivityVisual Merchant(float clock)
    {
        // The old 48-second block held the last pose for 19-29 seconds, and every
        // transfer held its endpoint after the generated motion finished. Both looked
        // like broken animation. Six transfers now fill the entire continuous cycle;
        // the varied timings still keep the individual reaches from being metronomic.
        float block = Mathf.Floor(clock / MerchantCycleSeconds);
        float cycle = clock - block * MerchantCycleSeconds;
        int transfer=0;
        while(transfer<5 && cycle>=TransferEnd[transfer]) transfer++;
        float start=transfer==0?0f:TransferEnd[transfer-1];
        int index=transfer<3?transfer:5-transfer;
        bool returning=transfer>=3;
        float segment=cycle-start, length=TransferEnd[transfer]-start;
        float t = MerchantTime(segment, length, (uint)block * 6u + (uint)transfer);
        Vector3 source = CoinSeat(index, returning), destination = CoinSeat(index, !returning);
        var visual = TownServiceMotionClips.Sample(returning ? 1 : 0, t / 3.6f);
        // Keep the generated shoulder/torso weight shift, but fit the hand to the
        // actual compact counter. Build 558 still stopped the pinched hand in midair
        // for t=1.70..2.00 and parked the support hand for almost a second. A single
        // curved transfer now carries the coin continuously between the two brief
        // tabletop contact windows. Its clock and variation remain shared by peers.
        float individuality = Variation((uint)block * 6u + (uint)transfer, 41u);
        Vector3 rest = new Vector3(.27f, 1.08f, .29f);
        Vector3 inspection = new Vector3(.27f + .018f * individuality,
            1.21f + .025f * individuality, .23f + .018f * individuality);
        float transferProgress = Soft(t, 1.05f, 2.84f);
        Vector3 curved = Vector3.Lerp(Vector3.Lerp(source, inspection, transferProgress),
            Vector3.Lerp(inspection, destination, transferProgress), transferProgress);
        Vector3 hand = Vector3.Lerp(rest, source, Soft(t, 0f, .85f));
        hand = Vector3.Lerp(hand, curved, Soft(t, 1.05f, 1.16f));
        hand = Vector3.Lerp(hand, rest, Soft(t, 3.03f, 3.6f));
        float grip = Ease(t, .86f, 1.04f) * (1f - Ease(t, 2.86f, 3.02f));
        float support = Soft(t, .45f, 1.35f) * (1f - Soft(t, 2.65f, 3.55f));
        visual.Left = hand;
        visual.Right = Vector3.Lerp(new Vector3(-.29f, 1.13f, .27f),
            new Vector3(-.23f + .012f * transferProgress, 1.10f + .012f * transferProgress,
                .31f - .025f * transferProgress), support);
        visual.RightRoll = 42f + 16f * support + 5f * transferProgress * support;
        visual.LeftCurl = grip * .55f;
        visual.RightCurl = .18f + .11f * support;
        // The imported Count and Return clips have different boundary body poses.
        // Feather their whole-body contribution to the same neutral stance at each
        // transfer edge while keeping the measured hand contact path authoritative.
        // Keep only a very short neutral seam between consecutive transfers. The former quarter
        // phase fade read as a crane stopping between every coin even though the hand path itself
        // was continuous.
        visual.Body.Weight *= Soft(t, 0f, .12f) * (1f - Soft(t, 3.45f, 3.6f));
        for (int coin = 0; coin < 3; coin++)
        {
            bool counted = returning ? coin <= index : coin < index;
            Vector3 seat = CoinSeat(coin, counted);
            if (coin == index) seat = t < 2.86f ? source : destination;
            // Ownership changes only while the pinch is stationary on its exact seat.
            // An interruption can freeze a half-closed grasp without moving the coin
            // through empty space; curling a finger does not attract objects to it.
            float held = coin == index && t >= 1.04f && t < 2.86f ? 1f : 0f;
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
        // Three separate experiments share the authored clock: a palm-held spark,
        // a two-hand sigil and a low tracing motion above the book. Their rests and
        // ends coincide, so even a late remote observer sees the same full gesture
        // without an elbow snap at the next phrase. The long reading interval stays.
        uint block = (uint)Mathf.Floor(clock / 48f);
        int variant = (int)(block % 3u);
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
        Vector3 leftSpell = variant == 0 ? new Vector3(.18f, 1.27f, .17f)
            : variant == 1 ? new Vector3(.12f, 1.25f, .23f)
            : new Vector3(.21f, 1.14f, .27f);
        Vector3 rightSpell = variant == 0 ? new Vector3(-.20f, 1.26f, .17f)
            : variant == 1 ? new Vector3(-.12f, 1.24f, .23f)
            : new Vector3(-.08f, 1.12f, .31f);
        float trace = shape * Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - .25f) / .50f));
        if (variant == 2) rightSpell += new Vector3(.025f * trace, 0f, .018f * trace);
        return new TownActivityVisual {
            Left = Vector3.Lerp(new Vector3(.22f, 1.11f, .23f),
                leftSpell, shape),
            Right = Vector3.Lerp(new Vector3(-.22f, 1.11f, .23f),
                rightSpell, lift),
            LeftElbow = new Vector3(.40f, .84f, .26f),
            RightElbow = new Vector3(-.40f, .84f, .26f), Body = body,
            LeftRoll = -65f + (variant == 1 ? -25f : 30f) * shape,
            RightRoll = 65f + 115f * turn,
            Cast = cast, LeftCurl = Mathf.Lerp(.26f, .10f, shape),
            CastSway = variant * .5f * lift,
            RightCurl = Mathf.Lerp(.26f, .08f, lift), EffectClock = clock
        };
    }
}
