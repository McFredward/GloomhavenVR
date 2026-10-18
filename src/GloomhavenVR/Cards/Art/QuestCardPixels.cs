using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace GloomhavenVR.Cards;

/// <summary>Exact original UI mip-zero RGBA, decoded once by the local builder rather than an XR framebuffer.</summary>
internal static class QuestCardPixels
{
    internal const string FileName = "quest-card-pixels.bin";
    private const int MaxFileBytes = 512 * 1024 * 1024;
    private const int MaxPixels = 4096 * 4096;
    private const long MaxTotalPixels = 384L * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static string? _path;
    private static long _length, _modified;
    private static Dictionary<string, Entry>? _entries;
    private static bool _failureLogged, _usedLogged;

    private sealed class Entry
    {
        internal int Width, Height, Srgb, Compressed;
        internal long Offset;
        internal byte[] Digest = Array.Empty<byte>();
    }

    /// <summary>
    /// This is an optional filtering source, never a replacement for dynamic CPU
    /// portraits or an alpha/geometry guess. Exact normalized original name,
    /// dimensions and colour encoding select one collision-free builder entry.
    /// A changed GPU texture must retain native content instead of old file pixels.
    /// </summary>
    internal static Color32[]? Read(Texture2D source)
    {
        if (!QuestStandalonePlatform.Enabled || source == null || source.isReadable || source.updateCount != 0)
            return null;
        try
        {
            string path = Path.Combine(QuestStandalonePlatform.ResourceDirectory, FileName);
            EnsureLoaded(path);
            if (_entries == null || !_entries.TryGetValue(CanonicalName(source.name), out Entry entry)
                || entry.Width != source.width || entry.Height != source.height
                || (entry.Srgb != 0) != GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat))
                return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != _length || info.LastWriteTimeUtc.Ticks != _modified)
                throw new InvalidDataException("Original card pixels changed after their index was read.");
            byte[] compressed = new byte[entry.Compressed];
            using (var file = File.OpenRead(path))
            {
                file.Position = entry.Offset;
                ReadExactly(file, compressed);
            }
            byte[] rgba = new byte[checked(entry.Width * entry.Height * 4)];
            using (var encoded = new MemoryStream(compressed, false))
            using (var decoded = new DeflateStream(encoded, CompressionMode.Decompress))
            {
                ReadExactly(decoded, rgba);
                if (decoded.ReadByte() != -1) throw new InvalidDataException("Original card pixels have excess decoded bytes.");
            }
            using (var hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(rgba);
                for (int i = 0; i < digest.Length; i++)
                    if (digest[i] != entry.Digest[i])
                        throw new InvalidDataException("Original card pixel digest disagrees with its entry.");
            }
            var result = new Color32[entry.Width * entry.Height];
            for (int i = 0, at = 0; i < result.Length; i++, at += 4)
                result[i] = new Color32(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]);
            if (!_usedLogged)
            {
                _usedLogged = true;
                VRLog.Info("Cards", "Quest card mip filtering uses exact owned RGBA pixels; XR GPU readback remains disabled.");
            }
            return result;
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException
            || error is ArgumentException || error is NotSupportedException || error is OverflowException)
        {
            _entries = null;
            if (!_failureLogged)
            {
                _failureLogged = true;
                VRLog.Warn("Cards", "Quest original card pixels unavailable; native artwork retained: " + error.Message);
            }
            return null;
        }
    }

    private static string CanonicalName(string name)
    {
        // Match the original-to-TextureImporter filename alias before lookup.
        // The producer rejects collisions after applying the same exact rule.
        char[]? characters = null;
        for (int i = 0; i < name.Length; i++)
            if (name[i] < 32 || "<>:\"/\\|?*".IndexOf(name[i]) >= 0)
            {
                characters ??= name.ToCharArray();
                characters[i] = '_';
            }
        return characters == null ? name : new string(characters);
    }

    private static void EnsureLoaded(string path)
    {
        if (_path == path) return;
        _path = path; _entries = null; _failureLogged = false; _usedLogged = false;
        var info = new FileInfo(path);
        if (!info.Exists) return;
        if (info.Length < 9 || info.Length > MaxFileBytes) throw new InvalidDataException("Original card pixel file bound.");
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Utf8, true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(5)) != "GHVP1") throw new InvalidDataException("Original card pixel format.");
        int count = reader.ReadInt32();
        long total = 0;
        if (count < 1 || count > 4096) throw new InvalidDataException("Original card pixel entry bound.");
        var entries = new Dictionary<string, Entry>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int nameLength = reader.ReadInt32();
            if (nameLength < 1 || nameLength > 1024) throw new InvalidDataException("Original card pixel name bound.");
            byte[] nameBytes = reader.ReadBytes(nameLength);
            if (nameBytes.Length != nameLength) throw new EndOfStreamException();
            string name = Utf8.GetString(nameBytes);
            var entry = new Entry { Width = reader.ReadInt32(), Height = reader.ReadInt32(),
                Srgb = reader.ReadInt32(), Compressed = reader.ReadInt32(), Digest = reader.ReadBytes(32) };
            if (name.IndexOf('\0') >= 0 || CanonicalName(name) != name || entries.ContainsKey(name)
                || entry.Width < 1 || entry.Width > 4096 || entry.Height < 1 || entry.Height > 4096
                || (long)entry.Width * entry.Height > MaxPixels || (entry.Srgb != 0 && entry.Srgb != 1)
                || entry.Digest.Length != 32 || entry.Compressed < 1 || entry.Compressed > MaxFileBytes
                || entry.Compressed > file.Length - file.Position)
                throw new InvalidDataException("Original card pixel entry identity or extent.");
            total += (long)entry.Width * entry.Height;
            if (total > MaxTotalPixels) throw new InvalidDataException("Original card pixel decoded bound.");
            entry.Offset = file.Position; file.Position += entry.Compressed;
            entries.Add(name, entry);
        }
        if (file.Position != file.Length) throw new InvalidDataException("Original card pixel trailing data.");
        _length = info.Length; _modified = info.LastWriteTimeUtc.Ticks; _entries = entries;
    }

    private static void ReadExactly(Stream source, byte[] target)
    {
        int position = 0;
        while (position < target.Length)
        {
            int read = source.Read(target, position, target.Length - position);
            if (read == 0) throw new EndOfStreamException("Original card pixels are truncated.");
            position += read;
        }
    }
}
