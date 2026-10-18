using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ClockStone;
using GloomhavenVR.Core;
using UnityEngine;

static class Program
{
    static int checks;
    static void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
    static void Reset()
    {
        foreach (string name in new[] { "Reports", "PerClip", "Background" })
        {
            object value = typeof(QuestCombatAudioEvidence).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            value.GetType().GetMethod("Clear")!.Invoke(value, null);
        }
        typeof(QuestCombatAudioEvidence).GetField("_otherEffects", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, 0);
        QuestStandalonePlatform.Enabled = VRSession.IsRunning = VRLog.Debug = true;
        SceneController.Instance = new(); RoomVisibilityManager.s_Instance = new(); AudioSettings.Reads = 0;
        AudioController.Reads = 0; AudioController.Music = AudioController.Ambient = null;
    }
    static int Main()
    {
        Reset(); var owner = new AudioObject();
        for (int gate = 0; gate < 6; gate++)
        {
            Reset(); owner.Reads = 0;
            if (gate == 0) QuestStandalonePlatform.Enabled = false;
            if (gate == 1) VRSession.IsRunning = false;
            if (gate == 2) VRLog.Debug = false;
            if (gate == 3) SceneController.Instance!.IsLoading = true;
            if (gate == 4) SceneController.Instance!.ScenarioIsLoading = true;
            if (gate == 5) RoomVisibilityManager.s_Instance!.Maps!.activeInHierarchy = false;
            QuestCombatAudioEvidence.Observe(owner);
            Check(owner.Reads == 0 && AudioSettings.Reads == 0 && QuestCombatAudioEvidence.Snapshot.Length == 0, "disabled-cost: gate read native audio or retained facts");
        }
        Reset(); owner.Source!.clip.name = "SFX_UIOpen"; owner.Source.outputAudioMixerGroup!.name = "UI"; QuestCombatAudioEvidence.Observe(owner);
        Check(AudioSettings.Reads == 0 && QuestCombatAudioEvidence.Snapshot.Length == 0, "scope: UI playback consumed attack allowance");
        owner.Source.clip.name = "SFX_BruteMeleeAttack"; owner.Source.outputAudioMixerGroup!.name = "SFX";
        for (int n = 0; n < 9; n++) QuestCombatAudioEvidence.Observe(owner);
        Check(QuestCombatAudioEvidence.Snapshot.Length == 2 && AudioSettings.Reads == 2, "per-clip: repeated attack exhausted the snapshot");
        Check(owner.Source.pitch == 1.125f && owner.Source.volume == .75f && owner.Source.spatialBlend == 1 && owner.Source.dopplerLevel == 1, "read-only: evidence changed native playback");
        string facts = QuestCombatAudioEvidence.Snapshot[0];
        foreach (string expected in new[] { "sourcePitch=1.1250", "nativePitch=1.1250", "channels=4", "rate=48000", "samples=48000", "load=DecompressOnLoad/Loaded", "dsp=1024x4", "headScale=22.00" })
            Check(facts.Contains(expected), "facts: missing actual " + expected);
        Check(QuestCombatAudioEvidence.Snapshot[1].Contains("start=2"), "per-clip: second start identity missing");
        for (int id = 1; id < 100; id++) { owner.Source.clip.Id = id; QuestCombatAudioEvidence.Observe(owner); }
        Check(QuestCombatAudioEvidence.Snapshot.Length == 8 && AudioSettings.Reads == 8, "total-bound: more than eight observations retained");
        owner.Reads = 0; for (int n = 0; n < 100; n++) QuestCombatAudioEvidence.Observe(owner);
        Check(owner.Reads == 0, "spent-cost: saturated evidence kept reading audio");
        var copy = QuestCombatAudioEvidence.Snapshot; copy[0] = "external mutation";
        Check(QuestCombatAudioEvidence.Snapshot[0] != copy[0], "snapshot: support consumer mutated retained evidence");
        Reset(); owner = new AudioObject(); owner.Source!.clip.name = "SFX_UITutorialFlowOpen";
        for (int id = 0; id < 10; id++) { owner.Source.clip.Id = id; QuestCombatAudioEvidence.Observe(owner); }
        Check(QuestCombatAudioEvidence.Snapshot.Length == 0 && AudioSettings.Reads == 0,
            "tutorial-ui-reserve: tutorial UI consumed the actual spatial-effect allowance");
        owner.Source.clip.name = "Brute_Overwhelming_Assault";
        owner.Source.outputAudioMixerGroup!.name = "3DSoundsReverb";
        for (int id = 0; id < 20; id++) { owner.Source.clip.Id = id; QuestCombatAudioEvidence.Observe(owner); }
        Check(QuestCombatAudioEvidence.Snapshot.Length == 2, "attack-reserve: generic effects exhausted the named attack share");
        owner.Source.clip.name = "SFX_MeleeAttack";
        for (int id = 20; id < 26; id++) { owner.Source.clip.Id = id; QuestCombatAudioEvidence.Observe(owner); }
        Check(QuestCombatAudioEvidence.Snapshot.Length == 8, "attack-reserve: six attack starts were not retained after other effects");
        Reset(); owner.Throw = true; QuestCombatAudioEvidence.Observe(owner);
        Check(QuestCombatAudioEvidence.Snapshot.Single() == "observation-unavailable=InvalidOperationException", "native-continuation: observation failure escaped into playback");
        Reset(); VRLog.Debug = false;
        Check(QuestCombatAudioEvidence.ObserveBackground().Length == 0 && AudioController.Reads == 0, "background-cost: normal logging queried native audio");
        Reset(); RoomVisibilityManager.s_Instance!.Maps!.activeInHierarchy = false;
        Check(QuestCombatAudioEvidence.ObserveBackground().Length == 0 && AudioController.Reads == 0, "background-cost: menu queried scenario audio");
        Reset(); AudioController.Music = new(); AudioController.Ambient = new();
        AudioController.Ambient.Source!.loop = true;
        AudioController.Ambient.Source.clip.name = new string('漢', 1000);
        string[] background = Array.Empty<string>();
        for (int i = 0; i < 12; i++) { Time.frameCount = i; AudioSettings.dspTime = i; background = QuestCombatAudioEvidence.ObserveBackground(); }
        Check(background.Length == 4 && background[0].StartsWith("frame=8 ") && background[3].StartsWith("frame=11 "), "background-bound: retain only four recent observations");
        Check(AudioController.Reads == 24, "background-cost: exactly two original owner references per snapshot");
        Check(background[3].Contains("dsp=11.000") && background[3].Contains("position=1234") && background[3].Contains("loop=True"), "background-facts: original mixer timeline missing");
        Check(background.Sum(s => System.Text.Encoding.UTF8.GetByteCount(s)) < 4096, "background-byte-bound: clip names exceeded bounded snapshot");
        Check(AudioController.Music.Source!.pitch == 1.125f && AudioController.Ambient.Source.pitch == 1.125f, "read-only: background observation changed playback");
        Reset(); AudioController.Music = new() { Throw = true };
        Check(QuestCombatAudioEvidence.ObserveBackground().Single() == "background-observation-unavailable=InvalidOperationException", "background-continuation: optional sample escaped");
        Console.WriteLine("PASS combat audio evidence: " + checks + " assertions; playback output is not certified");
        return 0;
    }
}
