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
        schedule.Work(3, 12f, 0f, .8f, true, 10.1f);
        schedule.Work(3, 12.05f, 0f, .9f, true, 10.2f);
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
    }
}
