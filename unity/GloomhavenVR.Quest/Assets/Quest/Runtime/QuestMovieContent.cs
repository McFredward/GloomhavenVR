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
    /// <summary>Install owned stereo films and their tiny Android shaders before native playback.
    /// Warm launches consult a bounded private receipt and file sizes, never rehash banks.</summary>
    internal static class QuestMovieContent
    {
        const string Metadata = "assets/Quest/movies.json";
        const string Prefix = "assets/QuestMovies/";
        const string Bank = "ghvr-quest-movies.bundle";
        const int MaximumMetadata = 256 * 1024;
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
                UnityEngine.Debug.LogWarning("[GloomhavenVR] [Quest movies] Owned stereo movie delivery unavailable: "
                    + delivery.Exception.GetBaseException().Message + "; native original movie sources remain active.");
            else if (delivery.Result)
                UnityEngine.Debug.Log("[GloomhavenVR] [Quest movies] Owned stereo movie files installed or retained.");
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
                    if (entry.Length > MaximumMetadata) throw new InvalidDataException("Movie metadata exceeds its bound");
                    string text; using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();
                    return Publish(text, root, name => {
                        ZipArchiveEntry part = archive.GetEntry(Prefix + name);
                        if (part == null) throw new InvalidDataException("Owned movie missing: " + name);
                        return part.Open();
                    }, progress);
                }
            }
            string metadata = Path.Combine(streamingAssets, "Quest/figure-meshes.json");
            if (!File.Exists(metadata)) return false;
            if (new FileInfo(metadata).Length > MaximumMetadata) throw new InvalidDataException("Movie metadata exceeds its bound");
            return Publish(File.ReadAllText(metadata), root,
                name => File.OpenRead(Path.Combine(streamingAssets, "QuestMovies", name)), progress);
        }

        static bool Hash(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value) if (!(c >= '0' && c <= '9' || c >= 'a' && c <= 'f')) return false;
            return true;
        }

        static bool SafeMember(string path)
        {
            if (path == Bank) return true;
            string[] parts = path.Split('/');
            if (parts.Length != 2 || (parts[0] != "CP_Intro" && parts[0] != "Heroes"
                && parts[0] != "Ambient" && parts[0] != "Embedded") || !parts[1].EndsWith(".mov", StringComparison.Ordinal)) return false;
            foreach (char c in parts[1])
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9')
                    && c != '_' && c != '-' && c != '.') return false;
            return parts[1].Length <= 128 && !parts[1].Contains("..");
        }

        static bool Publish(string text, string root, Func<string, Stream> open, Action<QuestGameContentProgress> progress)
        {
            Manifest manifest = JsonUtility.FromJson<Manifest>(text);
            if (manifest == null || manifest.schema != 1 || manifest.target != "Android" || !Hash(manifest.producerKey)
                || manifest.files == null || manifest.files.Length != 23)
                throw new InvalidDataException("Owned movie manifest is invalid");
            long total = 0; var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Item item in manifest.files)
            {
                if (item.path == null || !SafeMember(item.path) || !names.Add(item.path)
                    || item.size <= 0 || item.size > 256L * 1024 * 1024 || !Hash(item.sha256))
                    throw new InvalidDataException("Owned movie member is invalid");
                total += item.size;
                if (total > 4L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Owned movie set exceed their bound");
            }
            if (!names.Contains(Bank)) throw new InvalidDataException("Owned movie shaders are absent");
            Directory.CreateDirectory(root);
            // Native playback begins only after the complete set is installed.
            // Each file has its own receipt: interruption never repeats finished media.
            Array.Sort(manifest.files, (a, b) => string.CompareOrdinal(a.path, b.path));
            long completed = 0; int count = 0;
            foreach (Item item in manifest.files)
            {
                string target = Path.Combine(root, item.path.Replace('/', Path.DirectorySeparatorChar)), receipt = target + ".quest-receipt";
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(receipt + ".tmp")) File.Delete(receipt + ".tmp");
                // A killed Android process cannot run finally. Retire only this
                // producer's exact UUID temporary names before a targeted retry.
                foreach (string stale in Directory.EnumerateFiles(Path.GetDirectoryName(target), Path.GetFileName(target) + ".*.tmp"))
                {
                    string suffix = Path.GetFileName(stale).Substring(Path.GetFileName(target).Length + 1);
                    Guid id;
                    if (suffix.Length == 36 && suffix.EndsWith(".tmp", StringComparison.Ordinal)
                        && Guid.TryParseExact(suffix.Substring(0, 32), "N", out id)) File.Delete(stale);
                }
                // File identity is independent of another film or orchestration revision.
                string proof = "quest-movie-file-v1:" + item.sha256 + ":" + item.size;
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
                            if (read == 0) throw new EndOfStreamException("Owned movie truncated: " + item.path);
                            output.Write(buffer, 0, read); hash.TransformBlock(buffer, 0, read, null, 0); copied += read;
                            progress?.Invoke(new QuestGameContentProgress("extracting-file", item.path, copied, item.size,
                                completed + copied, total, count + 1, manifest.files.Length));
                        }
                        if (input.ReadByte() != -1) throw new InvalidDataException("Owned movie has trailing data");
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        if (BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant() != item.sha256)
                            throw new InvalidDataException("Owned movie digest mismatch: " + item.path);
                    }
                    if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                    string proofTemporary = receipt + ".tmp";
                    try
                    {
                        File.WriteAllText(proofTemporary, proof);
                        if (File.Exists(receipt)) File.Replace(proofTemporary, receipt, null); else File.Move(proofTemporary, receipt);
                    }
                    finally { if (File.Exists(proofTemporary)) File.Delete(proofTemporary); }
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                completed += item.size; count++;
            }
            return true;
        }
    }
}
#endif
