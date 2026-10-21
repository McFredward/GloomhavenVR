#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>Install the small owned CPU-alpha sidecar before card consumers start.</summary>
    internal static class QuestCardAlphaContent
    {
        const string Member = "assets/quest-card-alpha.bin";
        const string Metadata = "assets/Quest/card-alpha.json";
        const int MaximumBytes = 16 * 1024 * 1024;

        [Serializable]
        sealed class Manifest
        {
            public int schema = 0;
            public int bytes = 0;
            public string sha256 = null;
        }

        internal static bool Install(string apk, string streamingAssets, string root)
        {
            if (!string.IsNullOrEmpty(apk))
            {
                using (var file = File.OpenRead(apk))
                using (var archive = new ZipArchive(file, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry metadata = archive.GetEntry(Metadata);
                    if (metadata == null) return false; // Older code-only players have no sidecar.
                    if (metadata.Length > 4096) throw new InvalidDataException("Card alpha metadata exceeds its byte bound.");
                    string text;
                    using (var reader = new StreamReader(metadata.Open())) text = reader.ReadToEnd();
                    Manifest manifest = Parse(text);
                    if (Reuse(root, manifest, text)) return true;
                    ZipArchiveEntry entry = archive.GetEntry(Member);
                    if (entry == null || entry.Length != manifest.bytes)
                        throw new InvalidDataException("Owned card alpha payload is missing or has the wrong length.");
                    using (var source = entry.Open()) Publish(root, source, manifest, text);
                    return true;
                }
            }
            string path = Path.Combine(streamingAssets, "Quest/card-alpha.json");
            if (!File.Exists(path)) return false;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Card alpha metadata exceeds its byte bound.");
            string localText = File.ReadAllText(path);
            Manifest local = Parse(localText);
            if (Reuse(root, local, localText)) return true;
            using (var source = File.OpenRead(Path.Combine(streamingAssets, "quest-card-alpha.bin")))
                Publish(root, source, local, localText);
            return true;
        }

        static Manifest Parse(string text)
        {
            Manifest result = JsonUtility.FromJson<Manifest>(text);
            if (result == null || result.schema != 1 || result.bytes < 9 || result.bytes > MaximumBytes
                || result.sha256 == null || result.sha256.Length != 64)
                throw new InvalidDataException("Owned card alpha metadata is invalid.");
            foreach (char value in result.sha256)
                if (!(value >= '0' && value <= '9' || value >= 'a' && value <= 'f'))
                    throw new InvalidDataException("Owned card alpha digest is invalid.");
            return result;
        }

        static bool Reuse(string root, Manifest manifest, string text)
        {
            string target = Path.Combine(root, "quest-card-alpha.bin");
            string receipt = target + ".receipt.json";
            // Private app storage + exact producer receipt. No bank walk or
            // repeated alpha/GPU/game hash work on warm launches.
            return File.Exists(target) && new FileInfo(target).Length == manifest.bytes
                && File.Exists(receipt) && new FileInfo(receipt).Length <= 4096
                && File.ReadAllText(receipt) == text;
        }

        static void Publish(string root, Stream source, Manifest manifest, string text)
        {
            Directory.CreateDirectory(root);
            string target = Path.Combine(root, "quest-card-alpha.bin");
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = File.Create(temporary))
                {
                    var buffer = new byte[32768];
                    int remaining = manifest.bytes;
                    while (remaining > 0)
                    {
                        int count = source.Read(buffer, 0, Math.Min(buffer.Length, remaining));
                        if (count == 0) throw new EndOfStreamException("Owned card alpha payload is truncated.");
                        output.Write(buffer, 0, count);
                        remaining -= count;
                    }
                    if (source.ReadByte() != -1) throw new InvalidDataException("Owned card alpha payload has extra bytes.");
                }
                string digest;
                using (var hash = SHA256.Create())
                using (var input = File.OpenRead(temporary))
                    digest = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                if (digest != manifest.sha256) throw new InvalidDataException("Owned card alpha payload digest does not match.");
                if (File.Exists(target)) File.Replace(temporary, target, null); else File.Move(temporary, target);
                File.WriteAllText(target + ".receipt.json", text);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
#endif
