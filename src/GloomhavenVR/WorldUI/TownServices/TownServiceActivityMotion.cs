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
    internal const float TransitionSeconds = .95f;
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
        // Build 574's headset view exposed the remaining error: the merchant's
        // target palms were level with the counter and the priestess's forward
        // elbow guide swung her arms out, creating the apparent shoulder kink. Keep
        // the merchant's free left hand on the convex belly above the counter;
        // the right hand remains ready to open for a card. Place the priestess's
        // hands beside her robe. The low target is intentionally
        // limited by the imported arm length. Check actual skinned triangles over
        // the whole transition.
        // The merchant's rounded coat is asymmetric under the satchel. Seat the
        // palms near its surface and carry each elbow outside the waist; simply
        // pushing the old hand targets inward made the actual skinned cuff cut
        // through his torso during approach. These targets were checked against
        // the imported skin across the complete attention transition.
        float prayerLeftX = work.Left.x, prayerRightX = work.Right.x;
        Vector3 leftRest = service == 1 ? new Vector3(.18f, 1.08f, .41f)
            : service == 2 ? new Vector3(.29f, .75f, .58f) : new Vector3(.22f, 1.13f, .23f);
        work.Left = Vector3.Lerp(work.Left, leftRest, attention);
        work.Right = Vector3.Lerp(work.Right, service == 1 ? new Vector3(-.23f, 1.10f, .44f)
            : service == 3 ? new Vector3(-.18f, 1.17f, .23f) : new Vector3(-.29f, .75f, .58f), attention);
        if (service == 2)
        {
            // The headset video on build 577 shows the palms descending through
            // the centreline while each elbow stays outside the body: halfway
            // through the approach the forearms briefly cross back toward the
            // chest before the hands reach their neutral side. The imported-rig
            // render reproduces this at attention .38 (elbow x=.30, palm x=.12).
            // Separate the hands early, then lower them along their own sides.
            // This leaves both the joined prayer and relaxed idle endpoints intact
            // and runs along the same reversible owner-authored attention clock.
            float separate = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, attention * 2f));
            work.Left.x = Mathf.Lerp(prayerLeftX, leftRest.x, separate);
            work.Right.x = Mathf.Lerp(prayerRightX, -leftRest.x, separate);
            // Keep the forearms extended in front until the hands have cleared
            // the chest; the last portion settles beside the robe.
            float settleDepth = attention * attention;
            // Ease down before moving back toward the shoulders. This keeps
            // reach close to its relaxed length throughout the transition,
            // rather than parking a sharply folded wrist above the bowl.
            float settleHeight = attention + .12f * Mathf.Sin(Mathf.PI * attention);
            work.Left.y = Mathf.Lerp(1.34f, .75f, settleHeight);
            work.Right.y = Mathf.Lerp(1.34f, .75f, settleHeight);
            work.Left.z = Mathf.Lerp(.29f, .58f, settleDepth);
            work.Right.z = Mathf.Lerp(.29f, .58f, settleDepth);
        }
        // Hand targets alone cannot lower an arm naturally. Author the matching elbow path as
        // part of the same blend so the upper arm leaves the shoulder downward instead of staying
        // abducted while the forearm reaches for a low hand target.
        if (service is 1 or 2)
        {
            // Her relaxed elbows descend below the shoulder beside the robe. A
            // forward guide forced a conspicuous diagonal from shoulder to elbow.
            float priestessElbowHeight = 1.08f;
            work.LeftElbow = Vector3.Lerp(work.LeftElbow,
                new Vector3(service == 1 ? .58f : .29f, service == 1 ? 1.02f : priestessElbowHeight,
                    service == 1 ? .30f : .66f), attention);
            work.RightElbow = Vector3.Lerp(work.RightElbow,
                new Vector3(service == 1 ? -.55f : -.29f, service == 1 ? 1.04f : priestessElbowHeight,
                    service == 1 ? .31f : .66f), attention);
            if (service == 2)
            {
                float settleDepth = attention * attention;
                work.LeftElbow.z = Mathf.Lerp(.34f, .66f, settleDepth);
                work.RightElbow.z = Mathf.Lerp(.34f, .66f, settleDepth);
            }
        }
        work.RightRoll = Mathf.Lerp(work.RightRoll, service == 1 ? 65f : service == 3 ? 180f : 0f, attention);
        work.LeftRoll = Mathf.Lerp(work.LeftRoll, service == 1 ? -65f : service == 3 ? -65f : 0f, attention);
        work.RightCurl = Mathf.Lerp(work.RightCurl, service == 1 ? .22f : service == 3 ? .08f : .06f, attention);
        work.LeftCurl = Mathf.Lerp(work.LeftCurl, service == 1 ? .22f : service == 3 ? .26f : .06f, attention);
        work.Chest = Vector3.Lerp(work.Chest, Vector3.zero, attention);
        work.Body.Weight *= 1f - attention;
        work.Cast *= 1f - attention;
        work.CastSway *= 1f - attention;
        work.Curl = Mathf.Max(work.LeftCurl, work.RightCurl);
        return work;
    }

    /// <summary>Blend the attentive priestess from relaxed hanging arms into a deliberate closed-bowl
    /// pose after the native service reports that this visitor cannot donate again. The caller
    /// owns and replicates <paramref name="blend"/>; this method never reads local gameplay state.</summary>
    internal static void ApplyTempleAvailability(ref TownActivityVisual visual, bool donationAvailable, float blend)
    {
        // `blend` is the replicated visual state and may still be non-zero while
        // availability is returning. Returning solely on the new boolean dropped
        // the complete cover pose in one frame as a visitor left the temple. Keep
        // applying the fading pose until its authored blend has reached zero.
        if (donationAvailable && blend <= 0f) return;
        float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend)) * visual.Attention;
        // The ordinary attentive arm now takes a longer route in front of the
        // torso. An unavailable visitor must still reach the bowl directly,
        // without inheriting that route as a hidden detour toward the hip.
        // Restore the original prayer-to-attention depth before applying the
        // continuously blended cover displacement. No additional state/clock
        // is required for the return trip.
        float coverWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(blend));
        visual.Left.z = Mathf.Lerp(visual.Left.z,
            Mathf.Lerp(.29f, .58f, visual.Attention), coverWeight);
        visual.LeftElbow.z = Mathf.Lerp(visual.LeftElbow.z,
            Mathf.Lerp(.34f, .66f, visual.Attention), coverWeight);
        // Build 572 hardware video (2026-09-27, 07:46:52) showed a detour through
        // the attended hip pose before the covering hand reached the bowl. Visual
        // has already mixed prayer into the attended targets by Attention; a second
        // Lerp toward the cover target weighted by Attention mixes that hip target
        // in twice. Add the difference between attended and covered targets instead.
        // At full cover this is exactly prayer -> cover throughout approach, and at
        // full attention it is the ordinary hip -> cover donation transition.
        // Build 573's marker and final-pose render missed 243/403 actual-skin
        // intersections across approach, donation, and departure. The right
        // resting forearm was buried in the robe; the left cover target sent its
        // sleeve across the torso. Put the palm over the near half of the bowl,
        // with an outward elbow, and leave the other arm alongside the robe. The
        // 403-frame imported-skin scan has zero intersections.
        visual.Left += (new Vector3(.035f, 1.12f, .20f) - new Vector3(.29f, .75f, .58f)) * t;
        visual.LeftElbow += (new Vector3(.44f, 1.10f, .33f) - new Vector3(.29f, 1.08f, .66f)) * t;
        // The temple solver starts with an inward-facing left palm. A quarter turn
        // places its palmar surface down across the opening. The right hand keeps
        // its already relaxed attention pose rather than twisting over the bowl.
        visual.LeftRoll += 82f * t;
        visual.LeftCurl += (.08f - .06f) * t;
        visual.Curl = Mathf.Max(visual.LeftCurl, visual.RightCurl);
    }

    /// <summary>One authored blessing performance after a committed donation revision.
    /// The age comes from the owner timestamp, so peers interpolate the same gesture.</summary>
    internal static void ApplyTempleBlessing(ref TownActivityVisual visual, float age)
    {
        if (age < 0f || age >= 2.45f) return;
        float lift = Soft(age, .04f, .52f) * (1f - Soft(age, 1.64f, 2.42f));
        float open = Soft(age, .48f, 1.12f);
        // Gather the light above the bowl, then send it forward with one hand while
        // the other returns toward her heart. Two equally outstretched arms read as a
        // rigid mannequin and put both sleeves in a T-shaped silhouette in the render.
        Vector3 left = new Vector3(Mathf.Lerp(.10f, .12f, open),
            Mathf.Lerp(1.20f, 1.28f, open), Mathf.Lerp(.26f, .36f, open));
        Vector3 right = new Vector3(Mathf.Lerp(-.10f, -.17f, open),
            Mathf.Lerp(1.20f, 1.36f, open), Mathf.Lerp(.20f, .13f, open));
        visual.Left = Vector3.Lerp(visual.Left, left, lift);
        visual.Right = Vector3.Lerp(visual.Right, right, lift);
        visual.LeftElbow = Vector3.Lerp(visual.LeftElbow, new Vector3(.29f, 1.10f, .45f), lift);
        visual.RightElbow = Vector3.Lerp(visual.RightElbow, new Vector3(-.29f, 1.12f, .40f), lift);
        visual.LeftRoll = Mathf.Lerp(visual.LeftRoll, 30f, lift);
        visual.RightRoll = Mathf.Lerp(visual.RightRoll, -88f, lift);
        visual.LeftCurl = Mathf.Lerp(visual.LeftCurl, .04f, lift);
        visual.RightCurl = Mathf.Lerp(visual.RightCurl, .04f, lift);
        visual.Chest += new Vector3(-3.5f * lift, 0f, 0f);
        visual.Curl = Mathf.Max(visual.LeftCurl, visual.RightCurl);
    }

    internal static void ApplyTempleBreath(ref TownActivityVisual visual, float sharedClock)
    {
        float weight = visual.Attention;
        if (weight <= 0f) return;
        float breath = Mathf.Sin(sharedClock * 1.46f);
        visual.Chest += new Vector3(.7f * breath * weight, 0f, .32f * breath * weight);
        Vector3 drift = new Vector3(0f, .003f * breath * weight, 0f);
        visual.Left += drift;
        visual.Right += drift;
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
        // targets left a visible 70 mm gap. Build 571 still put the joined hands at
        // z=.20, directly above the bowl, so both forearms projected straight across
        // the chest in the headset video. The later z=.36 prayer target instead
        // placed both forearms *inside* the chest during entry/exit. Keep the
        // palms joined in front of the sternum and the elbows outside the robe.
        visual.Left = new Vector3(.012f,height,.29f);
        visual.Right = new Vector3(-.012f,height,.29f);
        visual.LeftElbow = new Vector3(.38f,1.15f,.34f);
        visual.RightElbow = new Vector3(-.38f,1.15f,.34f);
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
