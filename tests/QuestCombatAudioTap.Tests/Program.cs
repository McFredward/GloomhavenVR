using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Core;
using UnityEngine;
class Program
{
    static int checks;
    static readonly BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static void Reset()
    {
        typeof(QuestCombatAudioTap).GetField("_starts", Static)!.SetValue(null, 0);
        typeof(QuestCombatAudioTap).GetField("_pump", Static)!.SetValue(null, null);
        GameObject.Components.Clear(); GameObject.Created = UnityEngine.Object.Destroyed = AudioListener.Reads = 0;
        QuestStandalonePlatform.Enabled = VRSession.IsRunning = VRLog.Debug = true; VRSession.InputFocus = true;
        SceneController.Instance = new(); Time.realtimeSinceStartup = 0; AudioSettings.dspTime = 0;
        AudioSettings.speakerMode = AudioSpeakerMode.Stereo;
    }
    static object? Pump => typeof(QuestCombatAudioTap).GetField("_pump", Static)!.GetValue(null);
    static void Tick(double time)
    {
        Time.realtimeSinceStartup = (float)time; AudioSettings.dspTime = time; Time.frameCount++;
        object? pump = Pump; pump?.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pump, null);
    }
    static AudioSource Source(AudioClip clip) => new() { clip = clip };
    static void Main()
    {
        for (int gate = 0; gate < 4; gate++)
        {
            Reset(); var clip = new AudioClip(); var source = Source(clip); int before = GameObject.Created;
            if (gate == 0) QuestStandalonePlatform.Enabled = false;
            if (gate == 1) VRSession.IsRunning = false;
            if (gate == 2) VRLog.Debug = false;
            if (gate == 3) VRSession.InputFocus = false;
            QuestCombatAudioTap.Observe(source, clip, _ => throw new Exception("unexpected callback"));
            Check(Pump == null && GameObject.Created == before && clip.Reads == 0 && source.Reads == 0, "normal-scope: sampling/owner outside Debug active Quest");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add);
            Tick(.001); Tick(.03); Check(rows.Count == 0 && clip.Reads == 0, "queued-play: sampled before original cursor advanced");
            source.timeSamples = 1024; Tick(.04); Check(rows.Count == 1 && clip.Reads == 1 && source.Reads == 2 && AudioListener.Reads == 2, "first-buffer: missing exact native tap calls");
            Tick(.05); Check(rows.Count == 1, "buffer-cadence: duplicate output read before next real buffer");
            source.timeSamples = 2048; Tick(.063);
            Check(rows.Count == 2 && clip.Reads == 2 && source.Reads == 4 && AudioListener.Reads == 4 && Pump == null, "finite-reads: did not stop after two taps");
            for (int i = 0; i < 10; i++) Tick(1 + i);
            Check(rows.Count == 2 && clip.Reads == 2 && source.Reads == 4, "retired-cost: sampled after observer retired");
            Check(source.pitch == 1.2f && source.volume == .4f && source.dopplerLevel == 0 && ReferenceEquals(source.clip, clip), "read-only: tap changed original playback");
            Check(rows[0].Contains("decodedOffset=960,decodedFrames=64,decodedRead=True") && rows[0].Contains("decoded3=") && rows[0].Contains("source1=") && rows[0].Contains("listener1="), "buffer-facts: missing interleaved/native-output evidence");
            Check(rows.Sum(x => x.Length) < 4096, "compact-facts: unbounded sample content in snapshot");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01); Tick(.02);
            Check(rows.Count == 0 && clip.Reads == 0, "dsp-buffer: sampled before one real buffer elapsed after queuing");
            Tick(.022);
            Check(rows.Count == 1, "early-transient: first real buffer did not capture immediately after sample advance");
        }
        for (int gate = 0; gate < 7; gate++)
        {
            Reset(); var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add);
            if (gate == 0) SceneController.Instance!.IsLoading = true;
            if (gate == 1) SceneController.Instance!.ScenarioIsLoading = true;
            if (gate == 2) VRSession.InputFocus = false;
            if (gate == 3) source.clip = new();
            if (gate == 4) source.gameObject.activeInHierarchy = false;
            if (gate == 5) clip.loadState = AudioDataLoadState.Loading;
            Tick(gate >= 5 ? 7 : .1);
            Check(rows.Count == 1 && clip.Reads == 0 && source.Reads == 0 && Pump == null, "bounded-lifetime: native loading/focus/reuse/deadline sampled or retained owner " + gate);
        }
        foreach (AudioClipLoadType load in new[] { AudioClipLoadType.CompressedInMemory, AudioClipLoadType.Streaming })
        {
            Reset(); var clip = new AudioClip { loadType = load }; var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01); Tick(.03);
            Check(clip.Reads == 0 && source.Reads == 2 && rows.Single().Contains("decoded=unavailable-without-loading"), "no-force-decode: optional evidence loaded compressed/streaming data");
        }
        Reset();
        {
            var clip = new AudioClip { channels = 1 }; var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01); Tick(.03);
            Check(clip.Reads == 1 && source.Reads == 1 && AudioListener.Reads == 2, "channel-bound: mono source was read as stereo");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>(); AudioSettings.speakerMode = AudioSpeakerMode.Quad;
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01); Tick(.03);
            Check(clip.Reads == 1 && source.Reads == 0 && AudioListener.Reads == 0 && rows.Single().Contains("output=unqualified-speaker-layout"), "speaker-bound: unsupported listener channels sampled");
        }
        Reset();
        {
            var clip = new AudioClip { Throw = true }; var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01); Tick(.03);
            Check(rows.Single().Contains("tap-end=unavailable-InvalidOperationException") && Pump == null, "optional-failure: native read failure escaped or retained owner");
        }
        Reset();
        {
            var rows = new List<string>();
            for (int i = 0; i < 30; i++) { var clip = new AudioClip(); QuestCombatAudioTap.Observe(Source(clip), clip, rows.Add); }
            Tick(7);
            Check(rows.Count == 4 && Pump == null, "start-bound: more than four original hits sampled");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add); source.timeSamples = 512; Tick(.01);
            source.timeSamples = 0; Tick(.03);
            Check(rows.Single().Contains("sample-cursor-rewound") && clip.Reads == 0 && Pump == null,
                "pool-rewind: old observation sampled a same-clip retrigger");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip); var rows = new List<string>();
            QuestCombatAudioTap.Observe(source, clip, rows.Add);
            QuestCombatAudioTap.PlaybackStarting(new ClockStone.AudioObject { primaryAudioSource = source });
            Tick(.1);
            Check(rows.Single().Contains("source-retriggered") && clip.Reads == 0 && Pump == null,
                "pool-retrigger: old observation survived exact native prefix");
        }
        Reset();
        {
            var clip = new AudioClip(); var source = Source(clip);
            QuestCombatAudioTap.Observe(source, clip, _ => throw new InvalidOperationException());
            Pump!.GetType().GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Pump, new object[] { true });
            Check(Pump == null && clip.Reads == 0, "optional-callback: teardown exception escaped into lifecycle");
        }
        string extreme = (string)typeof(QuestCombatAudioTap).GetMethod("Stats", Static)!.Invoke(null,
            new object[] { new[] { float.NaN, float.PositiveInfinity, float.MaxValue, -float.MaxValue }, 0, 1 })!;
        Check(extreme.Contains("nonfinite=2") && extreme.Contains("fullscale=2") && extreme.Contains("E+038") && extreme.Length < 256,
            "invalid-output-bound: nonfinite/fullscale native output lost bounded diagnostic facts");
        Console.WriteLine("PASS finite combat audio tap: " + checks + " assertions; modeled scheduling/lifetimes, no Android DSP-output claim");
    }
}
