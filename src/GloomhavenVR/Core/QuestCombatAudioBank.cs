using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>Exact original quad Vorbis recovered independently of retained Campaign banks.</summary>
internal static class QuestCombatAudioBank
{
    private sealed class Entry { internal string Path = string.Empty; internal int Samples, Rate, Channels; }
    private sealed class Lease { internal AudioClip Original = null!, Applied = null!; }
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AudioClip> Clips = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<AudioSource, Lease> Leases = new();
    private static AssetBundle? _bank;
    private static bool _attempted;
    private static bool _reported;
    private static int _applied;
    internal static string Diagnostics => "nativeAudio entries=" + Entries.Count + " loaded=" + Clips.Count + " applied=" + _applied;

    internal static void Prepare(AudioSource? source)
    {
        // Native reuse can assign another mono clip without first stopping the
        // pooled component. Relinquish its old lease; equality-protected restore
        // leaves that newer native assignment intact.
        Restore(source);
        if (!QuestStandalonePlatform.Enabled || !VRSession.IsRunning || source == null || source.clip == null) return;
        AudioClip original = source.clip;
        // The demonstrated exporter defect is a mono header on authored quad
        // packets. Correct native quad/stereo clips and unrelated same-name
        // content remain untouched, even when a compatible sidecar is present.
        if (original.channels != 1) return;
        try
        {
            EnsureIndex();
            if (!Entries.TryGetValue(original.name, out Entry? entry) || original.frequency != entry.Rate || original.samples != entry.Samples) return;
            if (!Clips.TryGetValue(original.name, out AudioClip? corrected))
            {
                _bank ??= AssetBundle.LoadFromFile(Path.Combine(RuntimeDepsLoader.PluginDir, "ghvr-quest-native-audio.bundle"));
                if (_bank == null) throw new IOException("owned corrected audio bank unavailable");
                corrected = _bank.LoadAsset(entry.Path, typeof(AudioClip)) as AudioClip;
                if (corrected == null || corrected.channels != entry.Channels || corrected.frequency != entry.Rate || corrected.samples != entry.Samples)
                    throw new InvalidDataException("corrected audio differs from original channels/rate/timeline");
                // Retain the authored identity for native events and reports.
                corrected.name = original.name;
                Clips.Add(original.name, corrected);
                if (VRLog.WantsDebug) VRLog.Debug("Core", "Quest native audio restored original channels: " + original.name
                    + "; " + original.channels + "->" + corrected.channels + ", " + corrected.frequency + "Hz, " + corrected.samples + " frames.");
            }
            Leases.Add(source, new Lease { Original = original, Applied = corrected });
            int position = source.timeSamples;
            source.clip = corrected;
            // AudioController authors startTime/ClipStartTime BEFORE Play.
            // Assigning AudioSource.clip resets that cursor in the real engine.
            if (position > 0 && position < corrected.samples) source.timeSamples = position;
            _applied++;
        }
        catch (Exception error)
        {
            if (!_reported) { _reported = true; VRLog.Note("Core", "Quest native audio bank unavailable: " + error.Message + "; original playback retained."); }
        }
    }

    internal static void Restore(AudioSource? source)
    {
        if (source == null || !Leases.TryGetValue(source, out Lease? lease)) return;
        if (source.clip == lease.Applied)
        {
            int position = source.timeSamples;
            source.clip = lease.Original;
            if (position > 0 && position < lease.Original.samples) source.timeSamples = position;
        }
        Leases.Remove(source);
    }

    internal static void Inherit(AudioSource? primary, AudioSource? secondary)
    {
        if (primary == null || secondary == null || Leases.TryGetValue(secondary, out _) || !Leases.TryGetValue(primary, out Lease? lease)
            || secondary.clip != lease.Applied) return;
        Leases.Add(secondary, new Lease { Original = lease.Original, Applied = lease.Applied });
    }

    private static void EnsureIndex()
    {
        if (_attempted) return;
        _attempted = true;
        string path = Path.Combine(RuntimeDepsLoader.PluginDir, "ghvr-quest-native-audio.tsv");
        if (new FileInfo(path).Length > 64 * 1024) throw new InvalidDataException("unbounded corrected audio index");
        string[] rows = File.ReadAllLines(path);
        if (rows.Length < 2 || rows.Length > 129 || rows[0] != "GHVRQ_AUDIO_1") throw new InvalidDataException("invalid corrected audio index");
        var checkedEntries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        for (int i = 1; i < rows.Length; i++)
        {
            string[] fields = rows[i].Split('\t');
            if (fields.Length != 5) throw new InvalidDataException("invalid corrected audio row");
            string name = Encoding.UTF8.GetString(Convert.FromBase64String(fields[0]));
            string asset = fields[1];
            if (name.Length == 0 || name.Length > 128 || asset.Length != 49 || !asset.StartsWith("assets/audio/", StringComparison.Ordinal)
                || !asset.EndsWith(".ogg", StringComparison.Ordinal)) throw new InvalidDataException("invalid corrected audio identity");
            for (int c = 13; c < 45; c++) if (!(asset[c] >= '0' && asset[c] <= '9' || asset[c] >= 'a' && asset[c] <= 'f'))
                    throw new InvalidDataException("invalid corrected audio asset path");
            int samples = int.Parse(fields[2], CultureInfo.InvariantCulture), rate = int.Parse(fields[3], CultureInfo.InvariantCulture);
            int channels = int.Parse(fields[4], CultureInfo.InvariantCulture);
            if (rate < 8000 || rate > 192000 || samples <= 0 || samples > rate * 60 || channels != 4)
                throw new InvalidDataException("invalid corrected audio timeline");
            checkedEntries.Add(name, new Entry { Path = asset, Samples = samples, Rate = rate, Channels = channels });
        }
        foreach (var pair in checkedEntries) Entries.Add(pair.Key, pair.Value);
    }
}
