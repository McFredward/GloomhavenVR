using System;
using System.Reflection;
using ClockStone;
using GloomhavenVR.Core;
using UnityEngine;

internal static class Program
{
    static int checks;
    static void Check(bool value, string why) { checks++; if (!value) throw new Exception(why); }
    static void Hook(Type patch, string method, AudioObject owner) => patch.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { owner });
    static AudioObject Fresh()
    {
        QuestStandalonePlatform.Enabled = VRSession.IsRunning = true;
        GloomhavenVR.Rig.VRRigDriver.HeadCamera = new(); return new();
    }
    static void Main()
    {
        foreach (bool scheduled in new[] { false, true })
        {
            AudioObject owner = Fresh(); AudioSource source = owner.primaryAudioSource!;
            AudioClip? clip = source.clip; Mixer? mixer = source.outputAudioMixerGroup;
            Type hook = scheduled ? typeof(AudioObject_QuestCombatScheduled) : typeof(AudioObject_QuestCombatDelayed);
            Hook(hook, "Prefix", owner); source.PlayDelayed(0);
            Check(source.AtStartDoppler == 0 && source.Starts == 1, "before-first-buffer: original native start saw artificial Doppler");
            Check(QuestCombatAudio.HasPrepared(source), "prepared-evidence: corrected source cannot be distinguished from an authored zero");
            Check(source.pitch == 1.125f && source.volume == .49f && source.spatialBlend == 1 && source.minDistance == 2 && source.maxDistance == 80
                && !source.loop && !source.muted && ReferenceEquals(source.clip, clip) && ReferenceEquals(source.outputAudioMixerGroup, mixer), "authored-playback: changed original samples, pitch, gain, spatial or mixer");
            Hook(typeof(AudioObject_QuestCombatStopped), "Postfix", owner);
            Check(!QuestCombatAudio.HasPrepared(source), "restored-evidence: retired source retains a false correction claim");
            Check(source.dopplerLevel == 1 && source.Starts == 1, "pool-stop: original Doppler was not restored or sound was replayed");
        }
        for (int gate = 0; gate < 11; gate++)
        {
            AudioObject owner = Fresh(); AudioSource source = owner.primaryAudioSource!;
            if (gate == 0) QuestStandalonePlatform.Enabled = false;
            if (gate == 1) VRSession.IsRunning = false;
            if (gate == 2) GloomhavenVR.Rig.VRRigDriver.HeadCamera = null;
            if (gate == 3) GloomhavenVR.Rig.VRRigDriver.HeadCamera!.transform.lossyScale.x = 1;
            if (gate == 4) source.loop = true;
            if (gate == 5) source.spatialBlend = 0;
            if (gate == 6) source.outputAudioMixerGroup!.name = "Music";
            if (gate == 7) source.outputAudioMixerGroup!.name = "SFX";
            if (gate == 8) source.outputAudioMixerGroup = null;
            if (gate == 9) source.clip = null;
            if (gate == 10) source.dopplerLevel = 0;
            float original = source.dopplerLevel;
            QuestCombatAudio.Prepare(owner);
            Check(source.dopplerLevel == original, "scope-gate: changed desktop, UI, unscaled, inactive, looping ambience or absent source " + gate);
        }
        {
            AudioObject owner = Fresh(); AudioSource source = owner.primaryAudioSource!;
            owner.secondaryAudioSource = new() { dopplerLevel = .25f };
            QuestCombatAudio.Prepare(owner);
            Check(source.dopplerLevel == 0 && owner.secondaryAudioSource.dopplerLevel == 0, "dual-source: next native pooled source retained velocity pitch");
            (owner.primaryAudioSource, owner.secondaryAudioSource) = (owner.secondaryAudioSource, owner.primaryAudioSource);
            Hook(typeof(AudioObject_QuestCombatReset), "Prefix", owner);
            Check(source.dopplerLevel == 1 && owner.primaryAudioSource.dopplerLevel == .25f, "pool-reset: original primary/secondary swap lost exact source state");
            QuestCombatAudio.Prepare(owner); QuestCombatAudio.Prepare(owner);
            QuestCombatAudio.Restore(owner);
            Check(source.dopplerLevel == 1 && owner.primaryAudioSource.dopplerLevel == .25f, "idempotence: repeated preparation replaced authored Doppler with zero");
            QuestCombatAudio.Prepare(owner); QuestStandalonePlatform.Enabled = false; QuestCombatAudio.Prepare(owner);
            Check(source.dopplerLevel == 1 && owner.primaryAudioSource.dopplerLevel == .25f, "desktop-return: prior Quest pool use contaminated desktop");
            QuestStandalonePlatform.Enabled = true; QuestCombatAudio.Prepare(owner); source.dopplerLevel = .5f; QuestCombatAudio.Restore(owner);
            Check(source.dopplerLevel == .5f, "native-writer: restore overwrote an independent native change");
        }
        {
            AudioObject owner = Fresh(); QuestCombatAudio.Prepare(owner);
            owner.secondaryAudioSource = new() { dopplerLevel = owner.primaryAudioSource!.dopplerLevel };
            Hook(typeof(AudioObject_QuestCombatSecondary), "Postfix", owner);
            QuestCombatAudio.Restore(owner);
            Check(owner.primaryAudioSource.dopplerLevel == 1 && owner.secondaryAudioSource.dopplerLevel == 1,
                "secondary-copy: original native crossfade creation leaked corrected Doppler into the pool");
        }
        Console.WriteLine("PASS native combat Doppler boundary: " + checks + " assertions; headset sound remains unverified");
    }
}
