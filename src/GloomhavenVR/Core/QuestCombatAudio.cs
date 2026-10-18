using System.Runtime.CompilerServices;
using ClockStone;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Keep native combat cues at their authored pitch inside Quest's scaled diorama.</summary>
internal static class QuestCombatAudio
{
    private sealed class Original { internal float Doppler; }
    private static readonly ConditionalWeakTable<AudioSource, Original> Originals = new();

    internal static void Prepare(AudioObject owner)
    {
        // Restore an earlier pool use before deciding whether this use qualifies.
        // The original may swap primary/secondary sources during crossfades.
        if (owner == null) return;
        QuestCombatAudioTap.PlaybackStarting(owner);
        Restore(owner);
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning) return;
        // B692 proves startup quad Vorbis packets were exported under a mono
        // header. Use the separately owned original-channel bank for every
        // exact qualifying cue, including doors, before native playback starts.
        QuestCombatAudioBank.Prepare(owner.primaryAudioSource);
        QuestCombatAudioBank.Prepare(owner.secondaryAudioSource);
        Camera? head = Rig.VRRigDriver.HeadCamera;
        if (head == null || head.transform.lossyScale.x <= 1.0001f) return;
        PrepareSource(owner.primaryAudioSource);
        PrepareSource(owner.secondaryAudioSource);
    }

    private static void PrepareSource(AudioSource? source)
    {
        if (source == null || source.clip == null || source.loop || source.spatialBlend <= 0f
            || source.dopplerLevel <= 0f || source.outputAudioMixerGroup == null || Originals.TryGetValue(source, out _)) return;
        string mixer = source.outputAudioMixerGroup.name;
        if (mixer != "3DSounds" && mixer != "3DSoundsReverb") return;
        // B690 records the exact original quad PCM,48kHz,authored pitch1 but
        // doppler1 at head scale12.94 for both enemy and player damage cues.
        // AudioSource.pitch does NOT report FMOD's velocity-derived Doppler.
        // Pooled transform relocation, animated figures and scaled head motion
        // are presentation motion, not an authored change to a damage sound.
        // Match the existing EnvSound/VoiceSpatial scaled-world policy here;
        // do not re-encode/downmix clips, mute, move, delay or replay a sound.
        Originals.Add(source, new Original { Doppler = source.dopplerLevel });
        source.dopplerLevel = 0f;
    }

    internal static bool HasPrepared(AudioSource source) => source != null
        && source.dopplerLevel == 0f && Originals.TryGetValue(source, out _);

    internal static void InheritSecondary(AudioObject owner)
    {
        // Original _CreateSecondAudioSource copies the primary Doppler bit.
        // Keep its authored value even when the new component is created while
        // the primary is already corrected, so a later pool use is unchanged.
        AudioSource? primary = owner.primaryAudioSource, secondary = owner.secondaryAudioSource;
        QuestCombatAudioBank.Inherit(primary, secondary);
        if (primary == null || secondary == null || secondary.dopplerLevel != 0f
            || Originals.TryGetValue(secondary, out _) || !Originals.TryGetValue(primary, out Original? original)) return;
        Originals.Add(secondary, new Original { Doppler = original.Doppler });
    }

    internal static void Restore(AudioObject owner)
    {
        if (owner == null) return;
        RestoreSource(owner.primaryAudioSource);
        RestoreSource(owner.secondaryAudioSource);
    }

    private static void RestoreSource(AudioSource? source)
    {
        QuestCombatAudioBank.Restore(source);
        if (source == null || !Originals.TryGetValue(source, out Original? original)) return;
        // An independent native writer takes precedence over our old value.
        if (source.dopplerLevel == 0f) source.dopplerLevel = original.Doppler;
        Originals.Remove(source);
    }
}

// Bind before Unity's PlayDelayed/PlayScheduled, not after the existing _OnPlay
// evidence: the audio thread can consume its first buffer as soon as Play runs.
[HarmonyPatch(typeof(AudioObject), "_PlayDelayed")]
internal static class AudioObject_QuestCombatDelayed
{
    private static void Prefix(AudioObject __instance) => QuestCombatAudio.Prepare(__instance);
}

[HarmonyPatch(typeof(AudioObject), "_PlayScheduled")]
internal static class AudioObject_QuestCombatScheduled
{
    private static void Prefix(AudioObject __instance) => QuestCombatAudio.Prepare(__instance);
}

[HarmonyPatch(typeof(AudioObject), "_Stop")]
internal static class AudioObject_QuestCombatStopped
{
    private static void Postfix(AudioObject __instance) => QuestCombatAudio.Restore(__instance);
}

[HarmonyPatch(typeof(AudioObject), "_SetReferences0")]
internal static class AudioObject_QuestCombatReset
{
    private static void Prefix(AudioObject __instance) => QuestCombatAudio.Restore(__instance);
}

[HarmonyPatch(typeof(AudioObject), "_CreateSecondAudioSource")]
internal static class AudioObject_QuestCombatSecondary
{
    private static void Postfix(AudioObject __instance) => QuestCombatAudio.InheritSecondary(__instance);
}
