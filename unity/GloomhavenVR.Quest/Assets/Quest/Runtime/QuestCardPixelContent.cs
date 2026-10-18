#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Install the small owned CPU-RGBA sidecar before card consumers start.</summary>
    internal static class QuestCardPixelContent
    {
        const string Member = "assets/quest-card-pixels.bin";
        const string Metadata = "assets/Quest/card-pixels.json";
        const int MaximumBytes = 512 * 1024 * 1024;

        [Serializable]
        sealed class Manifest
        {
            public int schema = 0;
            public int bytes = 0;
            public string sha256 = null;
        }

        internal static IEnumerator InstallRoutine(string apk, string streamingAssets, string root,
            Action<QuestGameContentProgress> progress, Action present)
        {
            object sync = new object(); QuestGameContentProgress latest = null, reported = null;
            // Only managed ZIP/file/hash data runs on the worker. Paths were
            // captured by Bootstrap on the main thread; presentation stays here.
            Task<bool> delivery = Task.Run(() => Install(apk, streamingAssets, root,
                value => { lock (sync) latest = value; }));
            void Report()
            {
                QuestGameContentProgress value; lock (sync) value = latest;
                if (value == null || value == reported) return;
                reported = value; progress?.Invoke(value);
            }
            while (!delivery.IsCompleted) { Report(); present(); yield return null; }
            Report(); present();
            if (delivery.IsFaulted)
                UnityEngine.Debug.LogWarning("[GloomhavenVR] [Quest cards] Owned mip pixels unavailable: "
                    + delivery.Exception.GetBaseException().Message + "; native artwork and filtering remain active.");
            else if (delivery.Result)
                UnityEngine.Debug.Log("[GloomhavenVR] [Quest cards] Owned mip pixels installed or retained.");
        }

        internal static bool Install(string apk, string streamingAssets, string root,
            Action<QuestGameContentProgress> progress = null)
        {
            if (!string.IsNullOrEmpty(apk))
            {
                using (var file = File.OpenRead(apk))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry metadata = archive.GetEntry(Metadata);
                    if (metadata == null) return false; // Older code-only players have no sidecar.
                    if (metadata.Length > 4096) throw new InvalidDataException("Card pixel metadata exceeds its byte bound.");
                    string text;
                    using (var reader = new StreamReader(metadata.Open())) text = reader.ReadToEnd();
                    Manifest manifest = Parse(text);
                    if (Reuse(root, manifest, text)) return true;
                    ZipArchiveEntry entry = archive.GetEntry(Member);
                    if (entry == null || entry.Length != manifest.bytes)
                        throw new InvalidDataException("Owned card pixels payload is missing or has the wrong length.");
                    using (var source = entry.Open()) Publish(root, source, manifest, text, progress);
                    return true;
                }
            }
            string path = Path.Combine(streamingAssets, "Quest/card-pixels.json");
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Card pixel metadata exceeds its byte bound.");
            string localText = File.ReadAllText(path);
            Manifest local = Parse(localText);
            if (Reuse(root, local, localText)) return true;
            using (var source = File.OpenRead(Path.Combine(streamingAssets, "quest-card-pixels.bin")))
                Publish(root, source, local, localText, progress);
            return true;
        }

        static Manifest Parse(string text)
        {
            Manifest result = JsonUtility.FromJson<Manifest>(text);
            if (result == null || result.schema != 1 || result.bytes < 9 || result.bytes > MaximumBytes
                || result.sha256 == null || result.sha256.Length != 64)
                throw new InvalidDataException("Owned card pixels metadata is invalid.");
            foreach (char value in result.sha256)
                if (!(value >= '0' && value <= '9' || value >= 'a' && value <= 'f'))
                    throw new InvalidDataException("Owned card pixels digest is invalid.");
            return result;
        }

        static bool Reuse(string root, Manifest manifest, string text)
        {
            string target = Path.Combine(root, "quest-card-pixels.bin");
            string receipt = target + ".receipt.json";
            // Private app storage + exact producer receipt. No bank walk or
            // repeated pixel/GPU/game hash work on warm launches.
            return File.Exists(target) && new FileInfo(target).Length == manifest.bytes
                && File.Exists(receipt) && new FileInfo(receipt).Length <= 4096
                && File.ReadAllText(receipt) == text;
        }

        static void Publish(string root, Stream source, Manifest manifest, string text,
            Action<QuestGameContentProgress> progress)
        {
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "quest-card-pixels.bin");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string digest;
                using (var output = File.Create(temporary))
                using (var hash = SHA256.Create())
                {
                    var buffer = new byte[1024 * 1024];
                    int remaining = manifest.bytes;
                    while (remaining > 0)
                    {
                        int count = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                        if (count == 0) throw new EndOfStreamException("Owned card pixels payload is truncated.");
                        output.Write(buffer, 0, count);
                        hash.TransformBlock(buffer, 0, count, null, 0);
                        remaining -= count;
                        progress?.Invoke(new QuestGameContentProgress("extracting-file", "quest-card-pixels.bin",
                            manifest.bytes - remaining, manifest.bytes, manifest.bytes - remaining, manifest.bytes, 1, 1));
                    }
                    if (source.ReadByte() != -1) throw new InvalidDataException("Owned card pixels payload has extra bytes.");
                    hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                }
                if (digest != manifest.sha256) throw new InvalidDataException("Owned card pixels payload digest does not match.");
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                File.WriteAllText(target + ".receipt.json", text);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
#endif
