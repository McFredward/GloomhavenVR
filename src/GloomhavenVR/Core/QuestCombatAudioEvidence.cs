using System;
using System.Collections.Generic;
using System.Globalization;
using ClockStone;
using HarmonyLib;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Bounded read-only native attack playback facts; never an audio correction.</summary>
internal static class QuestCombatAudioEvidence
{
    private static readonly List<string> Reports = new();
    private static readonly Dictionary<int, int> PerClip = new();
    internal static string[] Snapshot => Reports.ToArray();

    internal static void Observe(AudioObject owner)
    {
        // No scene sweeps, audio samples, allocation or native property reads
        // during ordinary play. Keep two actual starts per clip, eight total.
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !VRLog.Wants(VRLogLevel.Debug)
            || Reports.Count >= 8 || owner == null) return;
        SceneController? scene = SceneController.Instance;
        RoomVisibilityManager? room = RoomVisibilityManager.s_Instance;
        if (scene == null || scene.IsLoading || scene.ScenarioIsLoading
            || room == null || room.Maps == null || !room.Maps.activeInHierarchy) return;
        try
        {
            AudioSource source = owner.primaryAudioSource;
            if (source == null) return;
            AudioClip clip = source.clip;
            if (clip == null) return;
            string name = clip.name ?? string.Empty;
            string mixer = source.outputAudioMixerGroup == null ? "none" : source.outputAudioMixerGroup.name;
            bool nativeEffects = mixer == "SFX" || mixer == "3DSounds" || mixer == "3DSoundsReverb";
            bool namedAttack = name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("onhit", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("melee", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ranged", StringComparison.OrdinalIgnoreCase) >= 0;
            if (source.loop || (!nativeEffects && !namedAttack)) return;
            int id = clip.GetInstanceID();
            PerClip.TryGetValue(id, out int count);
            if (count >= 2) return;
            AudioSettings.GetDSPBufferSize(out int length, out int buffers);
            var head = Rig.VRRigDriver.HeadCamera;
            // Snapshot storage survives the ordinary startup-log cap. The
            // existing support collector already exports startup-state.json.
            string facts = "clip=" + name.Substring(0, Math.Min(96, name.Length))
                + " start=" + (count + 1) + " frame=" + Time.frameCount
                + " frameMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F1", CultureInfo.InvariantCulture)
                + " sourcePitch=" + source.pitch.ToString("F4", CultureInfo.InvariantCulture)
                + " nativePitch=" + owner.originalPitch.ToString("F4", CultureInfo.InvariantCulture)
                + " channels=" + clip.channels + " rate=" + clip.frequency + " samples=" + clip.samples
                + " load=" + clip.loadType + "/" + clip.loadState + " playing=" + source.isPlaying
                + " virtual=" + source.isVirtual + " spatial=" + source.spatialBlend.ToString("F2", CultureInfo.InvariantCulture)
                + " doppler=" + source.dopplerLevel.ToString("F2", CultureInfo.InvariantCulture)
                + " gain=" + source.volume.ToString("F3", CultureInfo.InvariantCulture)
                + " outputRate=" + AudioSettings.outputSampleRate + " dsp=" + length + "x" + buffers
                + " mixer=" + mixer
                + " headScale=" + (head == null ? 0 : head.transform.lossyScale.x).ToString("F2", CultureInfo.InvariantCulture);
            Reports.Add(facts);
            PerClip[id] = count + 1;
        }
        catch (Exception error)
        {
            // Optional evidence must never throw into the native playback path.
            Reports.Add("observation-unavailable=" + error.GetType().Name);
        }
    }
}

[HarmonyPatch(typeof(AudioObject), "_OnPlay")]
internal static class AudioObject_QuestCombatEvidence
{
    private static void Postfix(AudioObject __instance) => QuestCombatAudioEvidence.Observe(__instance);
}
