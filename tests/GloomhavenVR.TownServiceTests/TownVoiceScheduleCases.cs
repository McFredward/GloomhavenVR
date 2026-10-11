using System;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.WorldUI
{
    // The schedule is pure; its audio loader does not belong in this wire test.
    internal static class TownServiceVoice
    {
        internal static ushort GreetingFirstCue(byte service) => service switch
        {
            1 => 1, 2 => 6, 3 => 11, _ => 0
        };
    }
}

internal static class TownVoiceScheduleCases
{
    internal static void Run()
    {
        var schedule = new TownServiceVoiceSchedule();
        schedule.Visit(3, true, 0f, 10f);
        TownServiceVoiceSchedule.Entry entry = schedule.At(3);
        if (!entry.Pending || entry.PendingCue < 11 || entry.PendingCue > 15)
            throw new Exception("Enchantress native visit must queue one greeting even before Work seeds.");
        schedule.Work(3, 12f, 0f, .8f, true, true, 10.1f);
        schedule.Work(3, 12.05f, 0f, .9f, true, true, 10.2f);
        if (entry.PendingCue < 11 || entry.PendingCue > 15)
            throw new Exception("An already-open enchantress gesture must not replace the visit greeting.");
        schedule.Sample(3, _ => 2f, 10.3f, false);
        if (entry.Cue < 11 || entry.Cue > 15)
            throw new Exception("The scheduled enchantress greeting must begin.");
        schedule.Request(3, 51, 10.4f);
        if (entry.Pending)
            throw new Exception("Offering a card during the greeting must not queue an immediate duplicate invitation.");
        schedule.Sample(3, _ => 2f, 13f, false);
        schedule.Request(3, 51, 14f);
        if (!entry.Pending || entry.PendingCue < 51 || entry.PendingCue > 55)
            throw new Exception("A later accepted card offer may request a fresh invitation.");

        // The merchant's face acquires a visitor before the coin hand can stop
        // working. The native shop does not yet have a visit and body Attention
        // remains zero, but the visible gaze must already start a greeting.
        var merchant = new TownServiceVoiceSchedule();
        // A greeting may deliberately stay quiet. Pin the spoken branch here so
        // the test checks gaze timing, rather than the optional speech lottery.
        merchant.At(1).VariantState = 1;
        merchant.Visit(1, false, float.PositiveInfinity, 20f);
        merchant.Work(1, 3.1f, 0f, 0f, true, true, 20f);
        TownServiceVoiceSchedule.Entry greeting = merchant.At(1);
        if (!greeting.Pending || greeting.PendingCue < 1 || greeting.PendingCue > 5)
            throw new Exception("Merchant face gaze must cue speech before coin-body attention or native shop entry.");
        merchant.Sample(1, _ => 2f, 20.1f, false);
        if (greeting.Cue < 1 || greeting.Cue > 5)
            throw new Exception("The distant merchant gaze greeting must begin while the visitor is still in gaze range.");
        merchant.Work(1, 3.2f, 0f, .8f, true, true, 20.2f);
        merchant.Visit(1, true, .1f, 20.3f);
        if (greeting.Pending)
            throw new Exception("Later body attention and native shop entry must not queue another greeting.");

        var departure = new TownServiceVoiceSchedule();
        departure.At(1).VariantState = 1;
        departure.Work(1, 1f, 0f, 0f, true, true, 30f);
        departure.Work(1, 1.1f, 0f, 0f, false, true, 30.1f);
        if (departure.At(1).Pending)
            throw new Exception("A visitor who leaves before speech starts must cancel the gaze greeting.");

        var quiet = new TownServiceVoiceSchedule();
        quiet.At(1).VariantState = 2;
        quiet.Work(1, 3.1f, 0f, 0f, true, true, 20f);
        if (quiet.At(1).Pending || quiet.At(1).NextAllowed != 65f)
            throw new Exception("An intentionally quiet gaze greeting must consume the same repeat interval.");
        quiet.Work(1, 3.2f, 0f, 0f, false, true, 20.1f);
        quiet.Work(1, 3.3f, 0f, 0f, true, true, 20.2f);
        if (quiet.At(1).Pending)
            throw new Exception("A quiet greeting must not be rerolled when the visitor briefly reenters gaze range.");
    }
}
