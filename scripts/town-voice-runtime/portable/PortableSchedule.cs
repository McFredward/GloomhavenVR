using System;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.WorldUI
{
    // The schedule uses this cue index without requiring a Unity scene or audio clip.
    internal static class TownServiceVoice
    {
        internal static ushort GreetingFirstCue(byte service) => service == 1 ? (ushort)1
            : service == 2 ? (ushort)6 : (ushort)11;
    }
}

internal static class PortableSchedule
{
    private static int _checks;
    private static readonly Func<ushort, float> Duration = _ => 1f;

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        // The face's 2.4 m attention can rise while the 1.4 m native shop visit
        // remains false. This is the actual sequence while approaching the stand.
        var approach = new TownServiceVoiceSchedule();
        approach.Visit(1, false, float.PositiveInfinity, 0f);
        approach.Work(1, 10f, 0f, 0f, false, true, 0f);
        Check(!approach.At(1).Pending, "no greeting before merchant looks at a visitor");
        approach.Work(1, 10.1f, 0f, .1f, true, true, .1f);
        Check(approach.At(1).PendingCue is >= 1 and <= 5,
            "look-at starts a greeting outside the native shop range");
        approach.Visit(1, false, float.PositiveInfinity, .11f);
        Check(approach.At(1).Pending, "the absent native visit does not cancel a gaze greeting");
        approach.Sample(1, Duration, .11f, false);
        Check(approach.At(1).Cue is >= 1 and <= 5 && approach.At(1).Generation == 1,
            "look-at greeting is one shared authored cue");
        approach.Visit(2, true, 0f, .11f);
        approach.Sample(2, Duration, .11f, false);
        Check(approach.At(2).Cue is >= 6 and <= 10 && approach.At(1).Cue is >= 1 and <= 5,
            "separate visitors at merchant and priestess hear independent shared cues");
        approach.Visit(1, true, 0f, .2f);
        approach.Work(1, 10.2f, 0f, .3f, true, true, .2f);
        approach.Sample(1, Duration, .2f, false);
        Check(approach.At(1).Generation == 1 && !approach.At(1).Pending,
            "opening the native shop later cannot restart or duplicate the greeting");
        approach.Sample(1, Duration, 1.2f, false);
        approach.Visit(1, false, float.PositiveInfinity, 2f);
        approach.Work(1, 11f, 0f, 0f, false, true, 2f);
        approach.Work(1, 11.1f, 0f, .1f, true, true, 2.1f);
        Check(!approach.At(1).Pending, "brief range oscillation respects the greeting cooldown");
        approach.Work(1, 12f, 0f, 0f, false, true, 46f);
        approach.Work(1, 12.1f, 0f, .1f, true, true, 46.1f);
        approach.Sample(1, Duration, 46.1f, false);
        Check(approach.At(1).Generation == 2 && approach.At(1).Cue is >= 1 and <= 5,
            "a later distinct look-at may greet again after cooldown");

        var handover = new TownServiceVoiceSchedule();
        handover.FollowerAttention(1, .4f, true);
        handover.Work(1, 70f, 0f, .4f, true, true, 70f);
        handover.Sample(1, Duration, 70f, false);
        Check(!handover.At(1).Pending && handover.At(1).Generation == 0,
            "a new author does not greet an already-watched visitor again after cooldown");
        handover.Work(1, 70.1f, 0f, 0f, false, true, 70.1f);
        handover.Work(1, 70.2f, 0f, .2f, true, true, 70.2f);
        handover.Sample(1, Duration, 70.2f, false);
        Check(handover.At(1).Cue is >= 1 and <= 5 && handover.At(1).Generation == 1,
            "the same author greets a later new gaze edge");

        var joinedDuringAbsence = new TownServiceVoiceSchedule();
        joinedDuringAbsence.FollowerAttention(1, .4f, false);
        joinedDuringAbsence.Work(1, 1f, 0f, .2f, true, true, 1f);
        Check(joinedDuringAbsence.At(1).PendingCue is >= 1 and <= 5,
            "an author may greet a genuinely new visitor after an unseen interval");

        var nativeOnly = new TownServiceVoiceSchedule();
        nativeOnly.Visit(1, true, 0f, 0f);
        nativeOnly.Work(1, 1f, 0f, 0f, false, true, 0f);
        Check(!nativeOnly.At(1).Pending,
            "shop visit without authored gaze cannot choose a merchant greeting");
        nativeOnly.Work(1, 1.1f, 0f, .2f, true, true, .1f);
        Check(nativeOnly.At(1).PendingCue is >= 1 and <= 5,
            "first visible attention sample still greets after native state arrives first");

        var interrupted = new TownServiceVoiceSchedule();
        interrupted.Work(1, 1f, 0f, .2f, true, true, 0f);
        interrupted.Work(1, 1.1f, 0f, 0f, false, true, .1f);
        Check(!interrupted.At(1).Pending,
            "a visitor who leaves before delayed speech starts retires that greeting");
        interrupted.Work(1, 2f, 0f, .2f, true, true, .2f);
        interrupted.Work(1, 2.1f, 0f, .2f, true, false, .3f);
        Check(!interrupted.At(1).Pending,
            "a hidden merchant cannot begin an old greeting later");

        var delayed = new TownServiceVoiceSchedule();
        delayed.Work(1, 1f, 0f, .2f, true, true, 0f);
        delayed.Sample(1, Duration, 0f, true);
        delayed.Work(1, 1.1f, 0f, .3f, true, true, 6f);
        delayed.Visit(1, false, float.PositiveInfinity, 6f);
        delayed.Sample(1, Duration, 6f, false);
        Check(delayed.At(1).Cue is >= 1 and <= 5 && delayed.At(1).Generation == 1,
            "a continuing look survives longer narration without a native shop visit");

        var trade = new TownServiceVoiceSchedule();
        trade.Work(1, 1f, 0f, .2f, true, true, 0f);
        trade.Request(1, 21, .01f);
        trade.Visit(1, true, 0f, .02f);
        trade.Work(1, 1.1f, 0f, .3f, true, true, .02f);
        trade.Sample(1, Duration, .02f, false);
        Check(trade.At(1).Cue is >= 21 and <= 25 && trade.At(1).Generation == 1,
            "transaction speech outranks an unplayed gaze greeting");

        var otherResident = new TownServiceVoiceSchedule();
        otherResident.Visit(2, true, 0f, 0f);
        Check(otherResident.At(2).PendingCue is >= 6 and <= 10,
            "priestess native greeting remains on its existing visit edge");

        var epochAware=new TownServiceVoiceSchedule();
        Check(epochAware.Observe(1,7,16,9,.2f,4f,11),
            "first authority epoch accepts its utterance");
        Check(!epochAware.Observe(1,7,16,8,.3f,4.1f,11),
            "same epoch rejects an older utterance");
        Check(epochAware.Observe(1,7,17,1,.1f,4.2f,12),
            "returning player with a new authority epoch can start low generations");

        Console.WriteLine($"PASS merchant gaze voice scheduling: {_checks} assertions");
    }
}
