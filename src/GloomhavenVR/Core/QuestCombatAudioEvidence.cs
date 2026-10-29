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
    private static int _otherEffects;
    private static readonly List<string> Background = new(4);
    internal static string[] Snapshot => Reports.ToArray();

    internal static string[] ObserveBackground()
    {
        // Called only by the existing five-second startup-state snapshot. The
        // scenario-only idle click cannot be diagnosed from attack starts alone.
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !VRLog.Wants(VRLogLevel.Debug))
            return Array.Empty<string>();
        SceneController? scene = SceneController.Instance;
        RoomVisibilityManager? room = RoomVisibilityManager.s_Instance;
        if (scene == null || scene.IsLoading || scene.ScenarioIsLoading
            || room == null || room.Maps == null || !room.Maps.activeInHierarchy) return Background.ToArray();
        try
        {
            // Original direct references, not an audio-source or scene sweep.
            string facts = "frame=" + Time.frameCount + " dsp=" + AudioSettings.dspTime.ToString("F3", CultureInfo.InvariantCulture)
                + " music={" + BackgroundSource(AudioController.GetCurrentMusic())
                + "} ambience={" + BackgroundSource(AudioController.GetCurrentAmbienceSound()) + "}";
            Background.Add(facts);
            if (Background.Count > 4) Background.RemoveAt(0);
        }
        catch (Exception error)
        {
            if (Background.Count == 4) Background.RemoveAt(0);
            Background.Add("background-observation-unavailable=" + error.GetType().Name);
        }
        return Background.ToArray();
    }

    private static string BackgroundSource(AudioObject? owner)
    {
        if (owner == null) return "none";
        AudioSource source = owner.primaryAudioSource;
        if (source == null || source.clip == null) return "no-clip";
        AudioClip clip = source.clip;
        string name = clip.name ?? string.Empty;
        return "clip=" + name.Substring(0, Math.Min(64, name.Length)) + " channels=" + clip.channels
            + " rate=" + clip.frequency + " samples=" + clip.samples + " position=" + source.timeSamples
            + " loop=" + source.loop + " load=" + clip.loadType + "/" + clip.loadState
            + " playing=" + source.isPlaying + " virtual=" + source.isVirtual
            + " pitch=" + source.pitch.ToString("F4", CultureInfo.InvariantCulture)
            + " gain=" + source.volume.ToString("F3", CultureInfo.InvariantCulture);
    }

    private static bool IsUiCue(string name) => name.StartsWith("SFX_UI", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("PlaySound_UI", StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("UI", StringComparison.OrdinalIgnoreCase);

    private static bool IsImpactCue(string name) => name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("hurt", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("impact", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("wound", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsCombatCue(string name) => name.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0
        || IsImpactCue(name)
        || name.IndexOf("melee", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("ranged", StringComparison.OrdinalIgnoreCase) >= 0;

    internal static void Observe(AudioObject owner)
    {
        // No scene sweeps, audio samples, allocation or native property reads
        // during ordinary play. Keep two actual starts per clip, eight total.
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !VRLog.Wants(VRLogLevel.Debug)
            || Reports.Count >= 8 || owner == null) return;
        SceneController? scene = SceneController.Instance;
        RoomVisibilityManager? room = RoomVisibilityManager.s_Instance;
        if (scene == null || scene.IsLoading || scene.ScenarioIsLoading) return;
        try
        {
            // PlayAudioItem assigns audioID only AFTER PlayAudioSubItem returns.
            // _OnPlay is inside that call. Its already-bound AudioItem is the
            // current cue; audioID can still name an earlier pooled use.
            string nativeId = owner.audioItem?.Name ?? owner.audioID ?? string.Empty;
            bool roomActive = room != null && room.Maps != null && room.Maps.activeInHierarchy;
            if (!roomActive && !IsImpactCue(nativeId)) return;
            AudioSource source = owner.primaryAudioSource;
            if (source == null) return;
            AudioClip clip = source.clip;
            if (clip == null) return;
            string name = clip.name ?? string.Empty;
            string category = owner.category == null ? "none" : owner.category.Name ?? string.Empty;
            if (IsUiCue(name) || IsUiCue(nativeId) || string.Equals(category, "UI", StringComparison.OrdinalIgnoreCase)) return;
            string mixer = source.outputAudioMixerGroup == null ? "none" : source.outputAudioMixerGroup.name;
            // Tutorial UI uses the generic SFX mixer. B691 spent both unnamed
            // allowances on TutorialFlowOpen and retained no actual hit cue.
            // Reserve that share for native spatial effects; named attacks keep
            // their existing allowance regardless of their output mixer.
            bool nativeEffects = mixer == "3DSounds" || mixer == "3DSoundsReverb";
            bool namedAttack = IsCombatCue(name) || IsCombatCue(nativeId);
            if (source.loop || (!nativeEffects && !namedAttack) || (!namedAttack && _otherEffects >= 2)) return;
            int id = clip.GetInstanceID();
            PerClip.TryGetValue(id, out int count);
            if (count >= 2) return;
            AudioSettings.GetDSPBufferSize(out int length, out int buffers);
            var head = Rig.VRRigDriver.HeadCamera;
            // Snapshot storage survives the ordinary startup-log cap. The
            // existing support collector already exports startup-state.json.
            string facts = "clip=" + name.Substring(0, Math.Min(96, name.Length))
                + " nativeId=" + nativeId.Substring(0, Math.Min(96, nativeId.Length))
                + " category=" + category.Substring(0, Math.Min(64, category.Length))
                + " start=" + (count + 1) + " frame=" + Time.frameCount
                + " frameMs=" + (Time.unscaledDeltaTime * 1000f).ToString("F1", CultureInfo.InvariantCulture)
                + " sourcePitch=" + source.pitch.ToString("F4", CultureInfo.InvariantCulture)
                + " nativePitch=" + owner.originalPitch.ToString("F4", CultureInfo.InvariantCulture)
                + " channels=" + clip.channels + " rate=" + clip.frequency + " samples=" + clip.samples
                + " load=" + clip.loadType + "/" + clip.loadState + " playing=" + source.isPlaying
                + " virtual=" + source.isVirtual + " spatial=" + source.spatialBlend.ToString("F2", CultureInfo.InvariantCulture)
                + " doppler=" + source.dopplerLevel.ToString("F2", CultureInfo.InvariantCulture)
                + " scaledDopplerPrepared=" + QuestCombatAudio.HasPrepared(source)
                + " gain=" + source.volume.ToString("F3", CultureInfo.InvariantCulture)
                + " velocityMode=" + source.velocityUpdateMode
                + " sourcePosition=" + source.transform.position
                + " outputRate=" + AudioSettings.outputSampleRate + " dsp=" + length + "x" + buffers
                + " mixer=" + mixer
                + " headScale=" + (head == null ? 0 : head.transform.lossyScale.x).ToString("F2", CultureInfo.InvariantCulture);
            int report = Reports.Count;
            Reports.Add(facts);
            if (namedAttack && (IsImpactCue(name) || IsImpactCue(nativeId)))
                QuestCombatAudioTap.Observe(source, clip, value =>
                {
                    // Append bounded facts to this same start record. Never add
                    // another report or let a diagnostic consume later hit slots.
                    if (report < Reports.Count && Reports[report].Length + value.Length < 6144)
                        Reports[report] += " " + value;
                });
            PerClip[id] = count + 1;
            if (!namedAttack) _otherEffects++;
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
