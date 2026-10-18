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
        foreach (string name in new[] { "Reports", "PerClip" })
        {
            object value = typeof(QuestCombatAudioEvidence).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            value.GetType().GetMethod("Clear")!.Invoke(value, null);
        }
        QuestStandalonePlatform.Enabled = VRSession.IsRunning = VRLog.Debug = true;
        SceneController.Instance = new(); RoomVisibilityManager.s_Instance = new(); AudioSettings.Reads = 0;
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
        Reset(); owner.Throw = true; QuestCombatAudioEvidence.Observe(owner);
        Check(QuestCombatAudioEvidence.Snapshot.Single() == "observation-unavailable=InvalidOperationException", "native-continuation: observation failure escaped into playback");
        Console.WriteLine("PASS combat audio evidence: " + checks + " assertions; playback output is not certified");
        return 0;
    }
}
