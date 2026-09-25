using System;
using System.IO;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.WorldUI;
using UnityEngine;

public static class InteractionProgram
{
    private static int _checks;
    private static void Check(bool okay, string message) { _checks++; if (!okay) throw new Exception(message); }
    private static FieldInfo Field(string name) => typeof(TownServiceVoice)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;
    private static AudioSource? Source => (AudioSource?)Field("_source").GetValue(null);
    private static void Refresh()
    {
        Field("_frame").SetValue(null, -1);
        Field("_nextContext").SetValue(null, 0f);
        typeof(TownServiceVoice).GetMethod("Ensure", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    }

    public static int Run()
    {
        Func<ushort, float> duration = cue => cue >= 31 && cue <= 35 ? 2.69f : 2f;
        var schedule = new TownServiceVoiceSchedule();
        schedule.Visit(1, true, 0f, 0f); schedule.Sample(1, duration, 0f, false);
        Check(schedule.At(1).Cue >= 1 && schedule.At(1).Cue <= 5 && schedule.At(1).Generation == 1, "first visit starts one of five merchant greetings");
        schedule.Visit(2, true, 0f, 0f); schedule.Sample(2, duration, 0f, false);
        Check(schedule.At(2).Cue == 0, "only one resident speaks at a time");
        schedule.Sample(1, duration, 1f, false);
        Check(schedule.At(1).Generation == 1 && schedule.At(1).Started == 0f, "frame tick does not restart cue");
        schedule.Sample(1, duration, 1.5f, true);
        Check(schedule.At(1).Cue == 0, "native narration interrupts resident speech");
        schedule.Sample(2, duration, 2f, false); Check(schedule.At(2).Cue == 0, "global speech gap");
        schedule.Sample(2, duration, 3f, false); Check(schedule.At(2).Cue >= 6 && schedule.At(2).Cue <= 10, "waiting greeting follows gap");

        var variantEntry = new TownServiceVoiceSchedule.Entry();
        MethodInfo picker = typeof(TownServiceVoiceSchedule).GetMethod("Pick",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        ushort previousVariant = 0;
        for (int i = 0; i < 64; i++)
        {
            ushort selected = (ushort)picker.Invoke(null, new object[] { variantEntry, (ushort)16, i * .37f })!;
            Check(selected >= 16 && selected <= 20, "event selection stays in its five-line pool");
            if (previousVariant != 0) Check(selected != previousVariant,
                "event selection cannot repeat the immediately preceding performance");
            previousVariant = selected;
        }

        var prayer = new TownServiceVoiceSchedule();
        prayer.Work(2, 5.9f, 0f, 0f, true, 0f);
        prayer.Work(2, 6.01f, 0f, 0f, true, .11f); prayer.Sample(2, duration, .11f, false);
        Check(prayer.At(2).Cue >= 31 && prayer.At(2).Cue <= 35, "quiet prayer chooses one of five performances at a shared occupation phase");
        prayer.Work(2, 6.02f, 0f, 0f, true, .12f);
        Check(!prayer.At(2).Pending, "prayer cannot requeue each frame");
        prayer.Sample(2, duration, 3.1f, false);
        prayer.Request(2, 36, 4.2f); prayer.Request(2, 36, 4.25f);
        prayer.Sample(2, duration, 4.2f, false);
        Check(prayer.At(2).Cue >= 36 && prayer.At(2).Cue <= 40 && prayer.At(2).Generation == 2, "donation reaction chooses one of five performances and remains deduplicated");

        var cast = new TownServiceVoiceSchedule();
        cast.Work(3, 13f, .1f, 0f, true, 0f);
        cast.Work(3, 13.03f, .3f, 0f, true, .03f); cast.Sample(3, duration, .03f, false);
        ushort firstCast = cast.At(3).Cue;
        Check(firstCast >= 41 && firstCast <= 45, "spell phrase follows actual cast edge");
        cast.Sample(3, duration, 2.1f, false);
        cast.Work(3, 61f, .1f, 0f, true, 50f);
        cast.Work(3, 61.03f, .3f, 0f, true, 50.03f); cast.Sample(3, duration, 50.03f, false);
        Check(cast.At(3).Cue >= 41 && cast.At(3).Cue <= 45 && cast.At(3).Cue != firstCast, "second shared experiment varies the spoken phrase without an immediate repeat");
        cast.Work(3, 70f, .3f, 0f, true, 60f);
        cast.Work(3, 70.03f, .3f, 0f, true, 60.03f);
        Check(!cast.At(3).Pending, "discontinuous seek does not replay historical cast");

        var invitation = new TownServiceVoiceSchedule();
        invitation.Visit(3, true, 0f, 0f);
        invitation.Sample(3, duration, 0f, false);
        Check(invitation.At(3).Cue == 0, "enchantress does not greet before the shared hand gesture");
        invitation.Work(3, 1f, 0f, 0f, true, 0f);
        invitation.Work(3, 1.1f, 0f, .4f, true, .1f);
        invitation.Sample(3, duration, .1f, false);
        Check(invitation.At(3).Cue >= 51 && invitation.At(3).Cue <= 55 && invitation.At(3).Generation == 1,
            "hand extension selects one author-owned invitation");
        invitation.Work(3, 1.2f, 0f, .6f, true, .2f);
        Check(!invitation.At(3).Pending, "continuous hand pose does not repeat invitation");
        ushort invitationCue = invitation.At(3).Cue;
        Check(invitation.Observe(3, 7, invitationCue, 1, .6f, 1f), "observer adopts author invitation age");
        Check(Mathf.Abs(invitation.At(3).Started - .4f) < .001f,
            "shared invitation age survives handover");

        var inherited = new TownServiceVoiceSchedule();
        Check(inherited.Observe(1, 7, 16, 12, 1f, 10f), "observer adopts cue");
        inherited.Sample(1, duration, 10.2f, false);
        Check(inherited.At(1).Cue == 16 && inherited.At(1).Started == 9f,
            "authority handover inherits utterance age");
        Check(!inherited.Observe(1, 7, 16, 11, 1.5f, 11f), "old generation rejected");
        Check(!inherited.Observe(1, 7, 16, 12, .5f, 11f), "same generation rewind rejected");
        Check(inherited.Observe(1, 7, 0, 12, 0f, 11f), "end marker accepted");
        Check(!inherited.Observe(1, 7, 16, 12, 1.5f, 12f), "ended cue cannot reopen");
        Check(inherited.Observe(1, 8, 16, 12, 1.5f, 12f), "new author may continue cue");

        string assets = Environment.GetEnvironmentVariable("TOWN_SPEECH_ASSETS")!;
        string[] names = { "merchant-greet", "merchant-greet-2", "merchant-greet-3", "merchant-greet-4", "merchant-greet-5",
            "priestess-greet", "priestess-greet-2", "priestess-greet-3", "priestess-greet-4", "priestess-greet-5",
            "enchantress-greet", "enchantress-greet-2", "enchantress-greet-3", "enchantress-greet-4", "enchantress-greet-5",
            "merchant-offer", "merchant-offer-2", "merchant-offer-3", "merchant-offer-4", "merchant-offer-5",
            "merchant-buy", "merchant-buy-2", "merchant-buy-3", "merchant-buy-4", "merchant-buy-5",
            "merchant-sell", "merchant-sell-2", "merchant-sell-3", "merchant-sell-4", "merchant-sell-5",
            "priestess-prayer", "priestess-prayer-2", "priestess-prayer-3", "priestess-prayer-4", "priestess-prayer-5",
            "priestess-donate", "priestess-donate-2", "priestess-donate-3", "priestess-donate-4", "priestess-donate-5",
            "enchantress-cast-ember", "enchantress-cast-echo", "enchantress-cast-spark", "enchantress-cast-veil", "enchantress-cast-rune",
            "enchantress-enhance", "enchantress-enhance-2", "enchantress-enhance-3", "enchantress-enhance-4", "enchantress-enhance-5",
            "enchantress-invite", "enchantress-invite-2", "enchantress-invite-3", "enchantress-invite-4", "enchantress-invite-5" };
        for (ushort cue = 1; cue <= names.Length; cue++)
        {
            string json = File.ReadAllText(Path.Combine(assets, names[cue - 1] + ".json"));
            TownServiceVoiceCurve? curve = TownServiceVoiceCurve.Parse(json, cue);
            if (curve == null)
            {
                var broken = JsonUtility.FromJson<TownServiceVoiceCurve.Document>(json);
                throw new Exception("curve rejected cue=" + cue + " schema=" + broken.schema
                    + " embeddedCue=" + broken.cue + " service=" + broken.service
                    + " expectedService=" + TownServiceVoice.ServiceForCue(cue)
                    + " lang=" + broken.language + " packed=" + broken.packed.Length);
            }
            Check(true, "sound-derived cue identity validates: " + cue);
            Check(TownServiceVoiceCurve.Parse(json, (ushort)(cue == names.Length ? 1 : cue + 1)) == null,
                "wrong cue cannot adopt a different mouth performance");
            foreach (TownServiceVoiceCurve.Interval item in curve!.Intervals)
            {
                Vector3 shape = curve!.At((item.start + item.end) * .5f);
                Check(shape.x >= 0f && shape.x <= 1f && shape.y >= 0f && shape.y <= 1f
                    && shape.z >= 0f && shape.z <= 1f, "bounded mouth channels");
                if (item.value == "A" || item.value == "X")
                    Check(shape == Vector3.zero, "closed/silent interval closes mouth");
            }
            for (int i = 1; i < curve.Intervals.Length; i++)
            {
                var left = curve.Intervals[i - 1]; var right = curve.Intervals[i];
                if (Math.Abs(left.end - right.start) > .001f) continue;
                float epsilon = .00001f;
                Vector3 before = curve.At(left.end - epsilon);
                Vector3 after = curve.At(right.start + epsilon);
                Check(Vector3.Distance(before, after) < .015f,
                    "shared phoneme boundary does not step the mouth");
            }
            Check(curve!.At(-1f) == Vector3.zero && curve.At(curve.Duration) == Vector3.zero,
                "mouth closes outside spoken clip");
            TownServiceAssets.Curves[names[cue - 1]] = new TextAsset(json);
            TownServiceAssets.Clips[names[cue - 1]] = AudioClip.Create(names[cue - 1],
                (int)Math.Round(curve.Duration * 24000), 1, 24000, false);
        }

        var head = new GameObject("voice-head"); var frame = new GameObject("voice-frame");
        TownServicePopulation.Frame = frame.transform; frame.transform.localScale = Vector3.one * 198.12f;
        head.transform.position = new Vector3(1f, 2f, 3f);
        HeadEar.Claims.Add("ExistingEnvironment");
        TownServiceVoice.Tick(1, 0f, true, default);
        Check(TownServiceFaceSpeech.Sampler != null && TownServiceFaceSpeech.Observer != null,
            "station audio tick binds shared face adapter");
        int requests = 0;
        TownServicePopulation.IsFaceAuthor = false;
        TownServiceVoice.RelayRequest = (service, reaction) =>
        { if (service == 2 && reaction == TownVoiceReaction.PriestessDonate) requests++; };
        TownServiceVoice.RequestReaction(2, TownVoiceReaction.PriestessDonate);
        Check(requests == 1, "non-author forwards reaction without inventing a local voice cue");
        TownServicePopulation.IsFaceAuthor = true;
        Check(!TownServiceVoice.AcceptRelayedReaction(2, TownVoiceReaction.PriestessDonate,
            7, 4, 1, 4f), "stale presentation request cannot trigger speech");
        Check(TownServiceVoice.AcceptRelayedReaction(2, TownVoiceReaction.PriestessDonate,
            7, 4, 1, .1f), "face author accepts fresh visitor reaction");
        Check(!TownServiceVoice.AcceptRelayedReaction(2, TownVoiceReaction.PriestessDonate,
            7, 4, 1, .1f), "duplicate town snapshot cannot replay reaction");
        Check(!TownServiceVoice.AcceptRelayedReaction(2, TownVoiceReaction.PriestessDonate,
            7, 3, 2, .1f), "older visitor session cannot replay reaction");
        SaveData.Instance!.Global!.MasterVolume = 50; SaveData.Instance.Global.StoryVolume = 40;
        Refresh(); TownServiceFaceSpeech.Observer!(1, 7, 1, 1, .2f, head.transform);
        Check(Source != null && Source.clip == TownServiceAssets.Clips["merchant-greet"],
            "observer plays exact bundled cue");
        Check(Mathf.Abs(Source!.time - .2f) < .02f,
            "late joining observer seeks to elected author's shared cue age");
        Check(Mathf.Abs(Source!.volume - .084f) < .0001f, "master and story sliders scale greeting");
        WorldUIConfig.ImmersiveTownSpeech.Value = false;
        TownServiceFaceSpeech.Observer(1, 7, 1, 1, .25f, head.transform);
        Check(Source.volume == 0f, "local resident speech preference mutes playback without changing shared cue");
        WorldUIConfig.ImmersiveTownSpeech.Value = true;
        TownServiceFaceSpeech.Observer(1, 7, 1, 1, .3f, head.transform);
        Check(Mathf.Abs(Source.volume - .084f) < .0001f, "resident speech preference applies immediately");
        Check(Source.spatialBlend == 1f && Source.dopplerLevel == 0f
            && Source.rolloffMode == AudioRolloffMode.Linear
            && Mathf.Abs(Source.minDistance - 89.154f) < .01f
            && Mathf.Abs(Source.maxDistance - 1386.84f) < .01f,
            "voice has continuous linear falloff across the map and follows map scale");
        Check(HeadEar.Claims.Contains("TownResidents"), "voice shares existing head listener");
        Source.time = .5f; TownServiceFaceSpeech.Observer(1, 7, 1, 1, .4f, head.transform);
        Check(Mathf.Abs(Source.time - .5f) < .02f, "same cue packet never restarts audio");
        TownServiceFaceSpeech.Observer(1, 8, 1, 1, .45f, head.transform);
        Check(Mathf.Abs(Source.time - .5f) < .02f, "author handover does not restart audio");
        SaveData.Instance.Global.StoryVolume = 0; Refresh();
        TownServiceFaceSpeech.Observer(1, 8, 1, 1, .55f, head.transform);
        Check(Source.volume == 0f, "story mute silences resident speech");
        TownServiceFaceSpeech.Observer(2, 7, 31, 1, .1f, head.transform);
        SaveData.Instance.Global.StoryVolume = 100; Refresh();
        TownServiceFaceSpeech.Observer(2, 7, 31, 1, .2f, head.transform);
        Check(Mathf.Abs(Source.volume - .078f) < .0001f,
            "priestess source gain compensates measured integrated loudness");
        AudioController.Playing.Add(new ClockStone.AudioObject
            { category = new ClockStone.AudioCategory { Name = "VONarrationCampaign" } });
        Refresh(); TownServiceFaceSpeech.Observer(2, 7, 31, 1, .3f, head.transform);
        Check(Source.volume == 0f && AudioController.Playing.Count == 1,
            "native narration ducks only NPC-owned speech");
        AudioController.Playing.Clear(); Refresh();
        TownServiceFaceSpeech.Observer(2, 7, 31, 2, .15f, head.transform);
        Check(Source.isPlaying, "new authored generation can start after a previous cue");
        TownServiceFaceSpeech.Observer(2, 7, 31, 3,
            TownServiceAssets.Clips["priestess-prayer"].length + .1f, head.transform);
        Check(!Source.isPlaying && !HeadEar.Claims.Contains("TownResidents"),
            "late join skips expired shared cue and stops stale resident audio");
        TownServiceVoice.Reset();
        Check(!HeadEar.Claims.Contains("TownResidents") && HeadEar.Claims.Contains("ExistingEnvironment"),
            "teardown releases only own listener claim");
        AudioController.Playing.Clear();
        UnityEngine.Object.DestroyImmediate(head); UnityEngine.Object.DestroyImmediate(frame);
        foreach (AudioClip clip in TownServiceAssets.Clips.Values) UnityEngine.Object.DestroyImmediate(clip);
        foreach (TextAsset curve in TownServiceAssets.Curves.Values) UnityEngine.Object.DestroyImmediate(curve);
        return _checks;
    }
}
