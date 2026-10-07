#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Read-only bounded Debug evidence; the original audio controller owns playback.</summary>
    public sealed class QuestGameAudioEvidence
    {
        sealed class Sample { public double started; public int phase; }
        readonly Dictionary<string, Sample> scenes = new Dictionary<string, Sample>(StringComparer.Ordinal);
        static readonly double[] Delays = { 1, 4, 12 };
        public void Observe(string scene)
        {
            if (!GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging || string.IsNullOrEmpty(scene)
                || scene == "QuestOriginalStartup") return;
            Sample sample;
            if (!scenes.TryGetValue(scene, out sample))
            {
                if (scenes.Count >= 4) return;
                sample = new Sample { started = Time.realtimeSinceStartupAsDouble };
                scenes.Add(scene, sample);
            }
            if (sample.phase >= Delays.Length || Time.realtimeSinceStartupAsDouble - sample.started < Delays[sample.phase]) return;
            sample.phase++;
            try
            {
                var configuration = AudioSettings.GetConfiguration();
                int length, buffers;
                AudioSettings.GetDSPBufferSize(out length, out buffers);
                int listeners = 0;
                foreach (var listener in Resources.FindObjectsOfTypeAll<AudioListener>())
                    if (listener != null && listener.isActiveAndEnabled && listener.gameObject.scene.IsValid()
                        && listener.gameObject.scene.isLoaded) listeners++;
                Debug.Log("[Quest startup] audio device scene=" + scene + " phase=" + sample.phase
                    + " rate=" + AudioSettings.outputSampleRate + " configuredRate=" + configuration.sampleRate
                    + " dsp=" + length + "x" + buffers + " speakers=" + configuration.speakerMode
                    + " listeners=" + listeners + " listenerVolume=" + AudioListener.volume + " paused=" + AudioListener.pause);
                int active = 0, playing = 0, reported = 0;
                foreach (var source in Resources.FindObjectsOfTypeAll<AudioSource>())
                {
                    if (source == null || !source.isActiveAndEnabled || !source.gameObject.scene.IsValid()
                        || !source.gameObject.scene.isLoaded) continue;
                    active++;
                    if (source.isPlaying) playing++;
                    if (source.clip == null || reported >= 12) continue;
                    reported++;
                    var clip = source.clip;
                    Debug.Log("[Quest startup] audio source scene=" + scene + " phase=" + sample.phase
                        + " name=" + source.name + " clip=" + clip.name + " channels=" + clip.channels
                        + " rate=" + clip.frequency + " samples=" + clip.samples + " loadType=" + clip.loadType
                        + " loadState=" + clip.loadState + " playing=" + source.isPlaying + " virtual=" + source.isVirtual
                        + " timeSamples=" + source.timeSamples + " pitch=" + source.pitch + " volume=" + source.volume
                        + " muted=" + source.mute + " spatial=" + source.spatialBlend + " doppler=" + source.dopplerLevel
                        + " mixer=" + (source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "none"));
                }
                Debug.Log("[Quest startup] audio sources scene=" + scene + " phase=" + sample.phase
                    + " active=" + active + " playing=" + playing + " reported=" + reported);
            }
            catch (Exception error)
            {
                Debug.LogWarning("[Quest startup] audio evidence unavailable scene=" + scene + " phase=" + sample.phase
                    + " error=" + error.GetType().Name);
            }
        }
    }
}
#endif
