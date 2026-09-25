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
        Func<ushort, float> duration = cue => cue == 7 ? 2.69f : 2f;
        var schedule = new TownServiceVoiceSchedule();
        schedule.Visit(1, true, 0f, 0f); schedule.Sample(1, duration, 0f, false);
        Check(schedule.At(1).Cue == 1 && schedule.At(1).Generation == 1, "first visit starts one greeting");
        schedule.Visit(2, true, 0f, 0f); schedule.Sample(2, duration, 0f, false);
        Check(schedule.At(2).Cue == 0, "only one resident speaks at a time");
        schedule.Sample(1, duration, 1f, false);
        Check(schedule.At(1).Generation == 1 && schedule.At(1).Started == 0f, "frame tick does not restart cue");
        schedule.Sample(1, duration, 1.5f, true);
        Check(schedule.At(1).Cue == 0, "native narration interrupts resident speech");
        schedule.Sample(2, duration, 2f, false); Check(schedule.At(2).Cue == 0, "global speech gap");
        schedule.Sample(2, duration, 3f, false); Check(schedule.At(2).Cue == 2, "waiting greeting follows gap");

        var prayer = new TownServiceVoiceSchedule();
        prayer.Work(2, 5.9f, 0f, 0f, true, 0f);
        prayer.Work(2, 6.01f, 0f, 0f, true, .11f); prayer.Sample(2, duration, .11f, false);
        Check(prayer.At(2).Cue == 7, "quiet prayer occurs at a shared occupation phase");
        prayer.Work(2, 6.02f, 0f, 0f, true, .12f);
        Check(!prayer.At(2).Pending, "prayer cannot requeue each frame");
        prayer.Sample(2, duration, 3.1f, false);
        prayer.Request(2, 8, 4.2f); prayer.Request(2, 8, 4.25f);
        prayer.Sample(2, duration, 4.2f, false);
        Check(prayer.At(2).Cue == 8 && prayer.At(2).Generation == 2, "donation reaction is distinct and deduplicated");

        var cast = new TownServiceVoiceSchedule();
        cast.Work(3, 13f, .1f, 0f, true, 0f);
        cast.Work(3, 13.03f, .3f, 0f, true, .03f); cast.Sample(3, duration, .03f, false);
        Check(cast.At(3).Cue == 9, "spell phrase follows actual cast edge");
        cast.Sample(3, duration, 2.1f, false);
        cast.Work(3, 61f, .1f, 0f, true, 50f);
        cast.Work(3, 61.03f, .3f, 0f, true, 50.03f); cast.Sample(3, duration, 50.03f, false);
        Check(cast.At(3).Cue == 10, "second shared experiment varies the spoken phrase");
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
        Check(invitation.At(3).Cue == 12 && invitation.At(3).Generation == 1,
            "hand extension selects one author-owned invitation");
        invitation.Work(3, 1.2f, 0f, .6f, true, .2f);
        Check(!invitation.At(3).Pending, "continuous hand pose does not repeat invitation");
        Check(invitation.Observe(3, 7, 12, 1, .6f, 1f), "observer adopts author invitation age");
        Check(Mathf.Abs(invitation.At(3).Started - .4f) < .001f,
            "shared invitation age survives handover");

        var inherited = new TownServiceVoiceSchedule();
        Check(inherited.Observe(1, 7, 4, 12, 1f, 10f), "observer adopts cue");
        inherited.Sample(1, duration, 10.2f, false);
        Check(inherited.At(1).Cue == 4 && inherited.At(1).Started == 9f,
            "authority handover inherits utterance age");
        Check(!inherited.Observe(1, 7, 4, 11, 1.5f, 11f), "old generation rejected");
        Check(!inherited.Observe(1, 7, 4, 12, .5f, 11f), "same generation rewind rejected");
        Check(inherited.Observe(1, 7, 0, 12, 0f, 11f), "end marker accepted");
        Check(!inherited.Observe(1, 7, 4, 12, 1.5f, 12f), "ended cue cannot reopen");
        Check(inherited.Observe(1, 8, 4, 12, 1.5f, 12f), "new author may continue cue");

        string assets = Environment.GetEnvironmentVariable("TOWN_SPEECH_ASSETS")!;
        string[] names = { "merchant-greet", "priestess-greet", "enchantress-greet", "merchant-offer",
            "merchant-buy", "merchant-sell", "priestess-prayer", "priestess-donate",
            "enchantress-cast-ember", "enchantress-cast-echo", "enchantress-enhance",
            "enchantress-invite" };
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
            Check(TownServiceVoiceCurve.Parse(json, (ushort)(cue == 11 ? 1 : cue + 1)) == null,
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
        Check(Mathf.Abs(Source!.volume - .104f) < .0001f, "master and story sliders scale greeting");
        Check(Source.spatialBlend == 1f && Source.dopplerLevel == 0f
            && Mathf.Abs(Source.minDistance - 128.778f) < .01f
            && Mathf.Abs(Source.maxDistance - 832.104f) < .01f,
            "voice is spatial and falloff follows map scale");
        Check(HeadEar.Claims.Contains("TownResidents"), "voice shares existing head listener");
        Source.time = .5f; TownServiceFaceSpeech.Observer(1, 7, 1, 1, .4f, head.transform);
        Check(Mathf.Abs(Source.time - .5f) < .02f, "same cue packet never restarts audio");
        TownServiceFaceSpeech.Observer(1, 8, 1, 1, .45f, head.transform);
        Check(Mathf.Abs(Source.time - .5f) < .02f, "author handover does not restart audio");
        SaveData.Instance.Global.StoryVolume = 0; Refresh();
        TownServiceFaceSpeech.Observer(1, 8, 1, 1, .55f, head.transform);
        Check(Source.volume == 0f, "story mute silences resident speech");
        TownServiceFaceSpeech.Observer(2, 7, 7, 1, .1f, head.transform);
        SaveData.Instance.Global.StoryVolume = 100; Refresh();
        TownServiceFaceSpeech.Observer(2, 7, 7, 1, .2f, head.transform);
        Check(Mathf.Abs(Source.volume - .08f) < .0001f, "priestess prayer is a quiet murmur");
        AudioController.Playing.Add(new ClockStone.AudioObject
            { category = new ClockStone.AudioCategory { Name = "VONarrationCampaign" } });
        Refresh(); TownServiceFaceSpeech.Observer(2, 7, 7, 1, .3f, head.transform);
        Check(Source.volume == 0f && AudioController.Playing.Count == 1,
            "native narration ducks only NPC-owned speech");
        AudioController.Playing.Clear(); Refresh();
        TownServiceFaceSpeech.Observer(2, 7, 7, 2, .15f, head.transform);
        Check(Source.isPlaying, "new authored generation can start after a previous cue");
        TownServiceFaceSpeech.Observer(2, 7, 7, 3,
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
