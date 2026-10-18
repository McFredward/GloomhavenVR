#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Install the bounded owned native-channel audio correction before original playback.
    /// Warm launches consult a bounded private receipt and file sizes, never rehash banks.</summary>
    internal static class QuestAudioClipContent
    {
        const string Metadata = "assets/Quest/audio-clips.json";
        const string Prefix = "assets/QuestAudioClips/";
        const string Index = "ghvr-quest-native-audio.tsv";
        const int MaximumMetadata = 16 * 1024;
        const string Bundle = "ghvr-quest-native-audio.bundle";
        [Serializable] sealed class Manifest { public int schema = 0; public string producerKey = null, target = null; public Item[] files = null; }
        [Serializable] sealed class Item { public string path = null, sha256 = null; public long size = 0; }

        internal static IEnumerator InstallRoutine(string apk, string streamingAssets, string root,
            Action<QuestGameContentProgress> progress, Action present)
        {
            object sync = new object(); QuestGameContentProgress latest = null, reported = null;
            // The worker touches managed file/hash/ZIP data only. Its consumer
            // may adjust main-thread presentation weights; dispatch that here.
            Task<bool> delivery = Task.Run(() => Install(apk, streamingAssets, root, value => { lock (sync) latest = value; }));
            void Report()
            {
                QuestGameContentProgress value; lock (sync) value = latest;
                if (value == null || value == reported) return;
                reported = value; progress?.Invoke(value);
            }
            while (!delivery.IsCompleted) { Report(); present(); yield return null; }
            Report();
            present();
            if (delivery.IsFaulted)
                UnityEngine.Debug.LogWarning("[GloomhavenVR] [Quest audio] Owned native-channel audio correction unavailable: "
                    + delivery.Exception.GetBaseException().Message + "; existing original audio remains active.");
            else if (delivery.Result)
                UnityEngine.Debug.Log("[GloomhavenVR] [Quest audio] Owned native-channel audio correction installed or retained.");
        }

        internal static bool Install(string apk, string streamingAssets, string root, Action<QuestGameContentProgress> progress)
        {
            if (!string.IsNullOrEmpty(apk))
            {
                using (var file = File.OpenRead(apk))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry(Metadata);
                    if (entry == null) return false;
                    if (entry.Length > MaximumMetadata) throw new InvalidDataException("Audio metadata exceeds its bound");
                    string text; using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                    return Publish(text, root, name => {
                        ZipArchiveEntry part = archive.GetEntry(Prefix + name);
                        if (part == null) throw new InvalidDataException("Owned audio part missing: " + name);
                        return part.Open();
                    }, progress);
                }
            }
            string metadata = Path.Combine(streamingAssets, "Quest/audio-clips.json");
            if (!File.Exists(metadata)) return false;
            if (new FileInfo(metadata).Length > MaximumMetadata) throw new InvalidDataException("Audio metadata exceeds its bound");
            return Publish(File.ReadAllText(metadata), root,
                name => File.OpenRead(Path.Combine(streamingAssets, "QuestAudioClips", name)), progress);
        }

        static bool Hash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }

        static bool Publish(string text, string root, Func<string, Stream> open, Action<QuestGameContentProgress> progress)
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(text);
            if (manifest == null || manifest.schema != 1 || manifest.target != "Android" || !Hash(manifest.producerKey)
                || manifest.files == null || manifest.files.Length != 2)
                throw new InvalidDataException("Owned audio manifest is invalid");
            long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Item item in manifest.files)
            {
                if (item.path == null || item.path != Path.GetFileName(item.path) || !names.Add(item.path)
                    || !(item.path == Index || item.path == Bundle) || item.size <= 0 || !Hash(item.sha256))
                    throw new InvalidDataException("Owned audio member is invalid");
                total += item.size;
                if (total > 64L * 1024 * 1024 || item.path == Index && item.size > 64 * 1024)
                    throw new InvalidDataException("Owned audio parts exceed their bound");
            }
            if (!names.Contains(Index)) throw new InvalidDataException("Owned audio index is absent");
            Directory.CreateDirectory(root);
            // The index is published last: no original consumer can observe an
            // index naming unfinished replacement parts after an interruption.
            Array.Sort(manifest.files, (a, b) => a.path == Index ? 1 : b.path == Index ? -1 : string.CompareOrdinal(a.path, b.path));
            long completed = 0; int count = 0;
            foreach (Item item in manifest.files)
            {
                string target = Path.Combine(root, item.path), receipt = target + ".quest-receipt";
                string proof = manifest.producerKey + ":" + item.sha256 + ":" + item.size;
                if (File.Exists(target) && new FileInfo(target).Length == item.size && File.Exists(receipt)
                    && new FileInfo(receipt).Length <= 256 && File.ReadAllText(receipt) == proof)
                { completed += item.size; count++; continue; }
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (Stream input = open(item.path))
                    using (var output = File.Create(temporary))
                    using (var hash = SHA256.Create())
                    {
                        var buffer = new byte[1024 * 1024]; long copied = 0;
                        while (copied < item.size)
                        {
                            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, item.size - copied));
                            if (read == 0) throw new EndOfStreamException("Owned audio part truncated: " + item.path);
                            output.Write(buffer, 0, read); hash.TransformBlock(buffer, 0, read, null, 0); copied += read;
                            progress?.Invoke(new QuestGameContentProgress("extracting-file", item.path, copied, item.size,
                                completed + copied, total, count + 1, manifest.files.Length));
                        }
                        if (input.ReadByte() != -1) throw new InvalidDataException("Owned audio part has trailing data");
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != item.sha256)
                            throw new InvalidDataException("Owned audio part digest mismatch: " + item.path);
                    }
                    if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                    File.WriteAllText(receipt, proof);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                completed += item.size; count++;
            }
            return true;
        }
    }
}
#endif
