using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using ClockStone;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Four finite Debug-only hit observations. Read the Android decoder, original
/// source output and stereo listener without installing an audio DSP callback.
/// Quest system output may be muted; native game/category volume must remain
/// unchanged or AudioController may suppress the original Play call entirely.
/// </summary>
internal static class QuestCombatAudioTap
{
    private const int MaximumStarts = 4;
    private const int OutputSamples = 1024;
    private const int ClipFrames = 64;
    private const float LifetimeSeconds = 6f;
    private static int _starts;
    private static Pump? _pump;

    internal static void Observe(AudioSource source, AudioClip clip, Action<string> append)
    {
        // Do not allocate an owner, inspect sample buffers or consume allowance
        // outside an actual Debug Quest hit admitted by the existing observer.
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !VRLog.Wants(VRLogLevel.Debug)
            || VRSession.InputFocus == false || source == null || clip == null) return;
        _pump?.Cancel(source, "source-retriggered");
        if (_starts >= MaximumStarts) return;
        _starts++;
        if (_pump == null)
        {
            var owner = new GameObject("GloomhavenVR.QuestCombatAudioEvidence");
            UnityEngine.Object.DontDestroyOnLoad(owner);
            _pump = owner.AddComponent<Pump>();
        }
        _pump.Pending.Add(new Observation(source, clip, append));
    }

    internal static void PlaybackStarting(AudioObject owner)
    {
        // Prefix of the original delayed/scheduled start. Cancel even when the
        // report/start quota is spent, including a same-clip pool reuse.
        if (_pump == null || !VRLog.Wants(VRLogLevel.Debug) || owner == null) return;
        AudioSource source = owner.primaryAudioSource;
        if (source != null) _pump.Cancel(source, "source-retriggered");
    }

    private sealed class Observation
    {
        internal readonly AudioSource Source;
        internal readonly AudioClip Clip;
        internal readonly Action<string> Append;
        internal readonly float Deadline = Time.realtimeSinceStartup + LifetimeSeconds;
        internal readonly int InitialPosition;
        internal readonly double QueuedAt = AudioSettings.dspTime;
        internal bool Started;
        internal double ReadAt;
        internal int LastPosition;
        internal int Reads;
        internal Observation(AudioSource source, AudioClip clip, Action<string> append)
        {
            Source = source; Clip = clip; Append = append;
            LastPosition = InitialPosition = source.timeSamples;
            ReadAt = QueuedAt + BufferSeconds();
        }
        internal void Report(string facts) { try { Append(facts); } catch (Exception) { /* Optional evidence never escapes into native playback or teardown. */ } }
    }

    private sealed class Pump : MonoBehaviour
    {
        internal readonly List<Observation> Pending = new(MaximumStarts);
        private void OnApplicationPause(bool paused) { if (paused) { FinishAll("application-paused"); Retire(); } }
        internal void Cancel(AudioSource source, string reason)
        { for (int i = Pending.Count - 1; i >= 0; i--) if (Pending[i].Source == source) Finish(i, reason); }
        private void OnDestroy() { FinishAll("observer-destroyed"); if (_pump == this) _pump = null; }
        private void Update()
        {
            if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || !VRLog.Wants(VRLogLevel.Debug)
                || VRSession.InputFocus == false)
            { FinishAll("session-or-debug-ended"); Retire(); return; }
            SceneController? scene = SceneController.Instance;
            if (scene == null || scene.IsLoading || scene.ScenarioIsLoading)
            { FinishAll("native-load-started"); Retire(); return; }
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                Observation item = Pending[i];
                try
                {
                    AudioSource source = item.Source;
                    if (source == null || item.Clip == null || source.clip != item.Clip || !source.gameObject.activeInHierarchy)
                    { Finish(i, "source-retired-or-reused"); continue; }
                    if (Time.realtimeSinceStartup >= item.Deadline)
                    { Finish(i, item.Started ? "deadline-after-start" : "deadline-before-sample-advance"); continue; }
                    if (item.Clip.loadState != AudioDataLoadState.Loaded) continue;
                    int position = source.timeSamples;
                    if (item.Started && position < item.LastPosition)
                    { Finish(i, "sample-cursor-rewound"); continue; }
                    if (!item.Started)
                    {
                        // Queued PlayDelayed/PlayScheduled can report isPlaying.
                        // Require real sample advance AND one buffer since the
                        // observed Play call. Read immediately at that edge so
                        // the1024-sample history can retain an initial transient.
                        if (!source.isPlaying || position == item.InitialPosition) continue;
                        item.Started = true;
                    }
                    item.LastPosition = position;
                    if (!source.isPlaying) { Finish(i, "playback-ended"); continue; }
                    if (AudioSettings.dspTime < item.ReadAt) continue;
                    item.Report("tap" + (item.Reads + 1) + "={" + Capture(source, item.Clip, item.QueuedAt) + "}");
                    item.Reads++;
                    if (item.Reads >= 2) Pending.RemoveAt(i);
                    else item.ReadAt = AudioSettings.dspTime + BufferSeconds();
                }
                catch (Exception error) { Finish(i, "unavailable-" + error.GetType().Name); }
            }
            if (Pending.Count == 0) Retire();
        }
        private void Finish(int index, string reason)
        {
            Observation item = Pending[index]; Pending.RemoveAt(index);
            item.Report("tap-end=" + reason + ",reads=" + item.Reads);
        }
        private void FinishAll(string reason) { while (Pending.Count != 0) Finish(Pending.Count - 1, reason); }
        private void Retire()
        {
            if (_pump == this) _pump = null;
            UnityEngine.Object.Destroy(gameObject);
        }
    }

    private static double BufferSeconds()
    {
        AudioSettings.GetDSPBufferSize(out int samples, out _);
        return Math.Max(.01, samples / (double)Math.Max(1, AudioSettings.outputSampleRate));
    }

    private static string Capture(AudioSource source, AudioClip clip, double queuedAt)
    {
        int position = source.timeSamples;
        string facts = "frame=" + Time.frameCount + ",dsp=" + AudioSettings.dspTime.ToString("F3", CultureInfo.InvariantCulture)
            + ",queuedElapsed=" + (AudioSettings.dspTime - queuedAt).ToString("F4", CultureInfo.InvariantCulture)
            + ",historySamples=" + OutputSamples + ",position=" + position + ",virtual=" + source.isVirtual
            + ",pitch=" + source.pitch.ToString("F4", CultureInfo.InvariantCulture)
            + ",spatial=" + source.spatialBlend.ToString("F3", CultureInfo.InvariantCulture)
            + ",gain=" + source.volume.ToString("F4", CultureInfo.InvariantCulture)
            + ",doppler=" + source.dopplerLevel.ToString("F3", CultureInfo.InvariantCulture);
        // Never request/decompress audio to obtain optional evidence. Unsupported
        // streaming/compressed clips retain their normal native playback route.
        if (clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.loadState == AudioDataLoadState.Loaded
            && clip.channels >= 1 && clip.channels <= 8 && clip.samples >= ClipFrames)
        {
            int offset = Math.Min(Math.Max(0, position - ClipFrames), clip.samples - ClipFrames);
            var samples = new float[ClipFrames * clip.channels]; // at most512 floats
            bool read = clip.GetData(samples, offset);
            facts += ",decodedOffset=" + offset + ",decodedFrames=" + ClipFrames + ",decodedRead=" + read;
            if (read)
                for (int channel = 0; channel < clip.channels; channel++)
                    facts += ",decoded" + channel + "=" + Stats(samples, channel, clip.channels);
        }
        else facts += ",decoded=unavailable-without-loading";
        AudioSpeakerMode mode = AudioSettings.GetConfiguration().speakerMode;
        facts += ",speaker=" + mode;
        int outputs = mode == AudioSpeakerMode.Stereo ? 2 : mode == AudioSpeakerMode.Mono ? 1 : 0;
        if (outputs != 0)
        {
            var samples = new float[OutputSamples];
            for (int channel = 0; channel < outputs; channel++)
            {
                if (channel < clip.channels)
                {
                    source.GetOutputData(samples, channel);
                    facts += ",source" + channel + "=" + Stats(samples, 0, 1);
                }
                if (mode == AudioSpeakerMode.Stereo)
                {
                    AudioListener.GetOutputData(samples, channel);
                    facts += ",listener" + channel + "=" + Stats(samples, 0, 1);
                }
            }
        }
        else facts += ",output=unqualified-speaker-layout";
        return facts;
    }

    private static string Number(double value) => value.ToString(Math.Abs(value) > 1000 ? "E4" : "F5", CultureInfo.InvariantCulture);

    [StructLayout(LayoutKind.Explicit)]
    private struct Bits { [FieldOffset(0)] internal float Value; [FieldOffset(0)] internal uint Raw; }
    private static string Stats(float[] samples, int first, int stride)
    {
        ulong hash = 14695981039346656037UL;
        double square = 0, sum = 0;
        float peak = 0;
        int nonfinite = 0, fullscale = 0, count = 0;
        for (int i = first; i < samples.Length; i += stride)
        {
            float value = samples[i];
            var bits = new Bits { Value = value };
            for (int b = 0; b < 4; b++) { hash ^= (byte)(bits.Raw >> (b * 8)); hash *= 1099511628211UL; }
            count++;
            if (float.IsNaN(value) || float.IsInfinity(value)) { nonfinite++; continue; }
            peak = Math.Max(peak, Math.Abs(value));
            if (Math.Abs(value) >= .999f) fullscale++;
            square += (double)value * value; sum += value;
        }
        return "(" + hash.ToString("x16", CultureInfo.InvariantCulture) + ",n=" + count
            + ",peak=" + Number(peak)
            + ",rms=" + Number(Math.Sqrt(square / Math.Max(1, count)))
            + ",mean=" + Number(sum / Math.Max(1, count))
            + ",nonfinite=" + nonfinite + ",fullscale=" + fullscale + ")";
    }
}
