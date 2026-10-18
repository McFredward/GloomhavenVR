using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>CPU alpha decoded by the local builder from the player's original textures.</summary>
internal static class QuestCardAlpha
{
    internal const string FileName = "quest-card-alpha.bin";
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private const int MaxPixels = 32 * 1024 * 1024;
    private const int MaxTotalPixels = 128 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static string? _path;
    private static long _length, _modified;
    private static Dictionary<string, Entry>? _entries;
    private static bool _failureLogged;

    private sealed class Entry
    {
        internal int Width, Height, Compressed;
        internal long Offset;
    }

    /// <summary>
    /// The B684 native card is visible but its GPU-only alpha cannot safely be read
    /// through an XR framebuffer. The builder supplies the original mip-zero alpha,
    /// not a guessed outline. Exact name AND dimensions select it; crop, native trim,
    /// preserveAspect, union, contour extraction and physical rim remain CardFace's
    /// ordinary desktop calculation. These bytes never replace the displayed art.
    /// </summary>
    internal static Texture2D? Read(Texture source, int x, int y, int width, int height)
    {
        if (!QuestStandalonePlatform.Enabled || source == null || x < 0 || y < 0
            || width < 1 || height < 1 || x > source.width - width || y > source.height - height)
            return null;
        try
        {
            string path = Path.Combine(QuestStandalonePlatform.ResourceDirectory, FileName);
            if (_path != path)
            {
                _path = path; _entries = null; _failureLogged = false;
                Load(path);
            }
            if (_entries == null || !_entries.TryGetValue(source.name, out Entry entry)
                || entry.Width != source.width || entry.Height != source.height)
                return null;
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != _length || info.LastWriteTimeUtc.Ticks != _modified)
                throw new InvalidDataException("Original card alpha changed after its index was read.");

            byte[] compressed = new byte[entry.Compressed];
            using (var file = File.OpenRead(path))
            {
                file.Position = entry.Offset;
                ReadExactly(file, compressed);
            }
            var alpha = new byte[entry.Width * entry.Height];
            using (var encoded = new MemoryStream(compressed, false))
            using (var decoded = new DeflateStream(encoded, CompressionMode.Decompress))
            {
                ReadExactly(decoded, alpha);
                if (decoded.ReadByte() != -1) throw new InvalidDataException("Original card alpha has excess pixels.");
            }
            var pixels = new Color32[width * height];
            for (int row = 0; row < height; row++)
                for (int column = 0; column < width; column++)
                    pixels[row * width + column] = new Color32(255, 255, 255,
                        alpha[(y + row) * entry.Width + x + column]);
            var result = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            result.SetPixels32(pixels); result.Apply(false);
            return result;
        }
        catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException
            || error is ArgumentException || error is NotSupportedException)
        {
            _entries = null;
            if (!_failureLogged)
            {
                _failureLogged = true;
                VRLog.Warn("Cards", "Quest original card alpha unavailable: " + error.Message);
            }
            return null;
        }
    }

    private static void Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return;
        if (info.Length < 9 || info.Length > MaxFileBytes) throw new InvalidDataException("Original card alpha file bound.");
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Utf8, true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(5)) != "GHVA1") throw new InvalidDataException("Original card alpha format.");
        int count = reader.ReadInt32(), total = 0;
        if (count < 1 || count > 256) throw new InvalidDataException("Original card alpha entry bound.");
        var entries = new Dictionary<string, Entry>(count, StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            int nameLength = reader.ReadInt32();
            if (nameLength < 1 || nameLength > 1024) throw new InvalidDataException("Original card alpha name bound.");
            byte[] nameBytes = reader.ReadBytes(nameLength);
            if (nameBytes.Length != nameLength) throw new EndOfStreamException();
            string name = Utf8.GetString(nameBytes);
            var entry = new Entry { Width = reader.ReadInt32(), Height = reader.ReadInt32(), Compressed = reader.ReadInt32() };
            if (name.IndexOf('\0') >= 0 || entries.ContainsKey(name)
                || entry.Width < 1 || entry.Width > 8192 || entry.Height < 1 || entry.Height > 8192
                || (long)entry.Width * entry.Height > MaxPixels || entry.Compressed < 1
                || entry.Compressed > MaxFileBytes || entry.Compressed > file.Length - file.Position)
                throw new InvalidDataException("Original card alpha entry identity or extent.");
            total += entry.Width * entry.Height;
            if (total > MaxTotalPixels) throw new InvalidDataException("Original card alpha decoded bound.");
            entry.Offset = file.Position; file.Position += entry.Compressed;
            entries.Add(name, entry);
        }
        if (file.Position != file.Length) throw new InvalidDataException("Original card alpha trailing data.");
        _length = info.Length; _modified = info.LastWriteTimeUtc.Ticks; _entries = entries;
    }

    private static void ReadExactly(Stream source, byte[] target)
    {
        int position = 0;
        while (position < target.Length)
        {
            int read = source.Read(target, position, target.Length - position);
            if (read == 0) throw new EndOfStreamException("Original card alpha is truncated.");
            position += read;
        }
    }
}
