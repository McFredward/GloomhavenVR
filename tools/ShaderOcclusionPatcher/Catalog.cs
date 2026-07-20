using System.Text;
using System.Text.Json;

namespace ShaderOcclusionPatcher;

public sealed class CatalogCrcReport
{
    public string CatalogPath { get; set; } = "";
    public int EntryCount { get; set; }
    public int NonZeroCrcCount { get; set; }
    public int ZeroedThisRun { get; set; }
    public string Decision { get; set; } = "";
}

/// <summary>
/// Addressables CRC handling.
///
/// The catalog (StreamingAssets/aa/catalog.json) stores one serialized
/// AssetBundleRequestOptions JSON per bundle inside the base64 blob
/// m_ExtraDataString (UTF-16LE strings with a 4-byte length prefix). If the
/// "m_Crc" there is nonzero, Unity verifies a CRC32 of the uncompressed bundle
/// content on load and a patched bundle would FAIL to load.
///
/// Verified against the shipped catalog (2026-07): all 3255 entries carry
/// "m_Crc":0 — the game does not CRC-check its bundles, so patching bundles is
/// safe without touching the catalog. As a safety net (future game updates),
/// nonzero CRCs for bundles we patch are zeroed with a LENGTH-PRESERVING edit
/// ("m_Crc":12345 -> "m_Crc":0 + spaces) so the length-prefixed binary layout
/// of the blob stays valid. Zeroing (rather than recomputing) simply disables
/// the check for that bundle — documented, deliberate choice.
/// </summary>
public static class CatalogCrc
{
    private static string? FindCatalog(string gameData)
    {
        string p = Path.Combine(gameData, "StreamingAssets", "aa", "catalog.json");
        return File.Exists(p) ? p : null;
    }

    public static CatalogCrcReport? Inspect(string gameData)
    {
        string? path = FindCatalog(gameData);
        if (path is null) return null;

        var report = new CatalogCrcReport { CatalogPath = path };
        byte[] extra = GetExtraData(path, out _);
        foreach (var (_, crcDigits) in FindCrcFields(extra))
        {
            report.EntryCount++;
            if (crcDigits != "0") report.NonZeroCrcCount++;
        }
        report.Decision = report.NonZeroCrcCount == 0
            ? "no nonzero CRCs — catalog untouched"
            : "nonzero CRCs present — will be zeroed for patched bundles";
        return report;
    }

    public static void ZeroCrcs(string gameData, string backupDir, List<string> patchedBundleNames, Manifest manifest)
    {
        string? path = FindCatalog(gameData);
        if (path is null || patchedBundleNames.Count == 0 || manifest.CatalogCrc is null) return;

        byte[] extra = GetExtraData(path, out JsonDocument doc);
        doc.Dispose();
        bool changed = false;

        foreach (string bundleName in patchedBundleNames)
        {
            // locate this bundle's AssetBundleRequestOptions record via its
            // "m_BundleName":"<name>" (UTF-16LE), then zero the nearest
            // PRECEDING "m_Crc": value (field order: m_Hash, m_Crc, ..., m_BundleName).
            byte[] needle = Encoding.Unicode.GetBytes($"\"m_BundleName\":\"{bundleName}\"");
            int at = IndexOf(extra, needle, 0);
            if (at < 0)
            {
                Engine.Warn(manifest, $"catalog: no entry found for patched bundle '{bundleName}' — CRC not adjusted.");
                continue;
            }
            byte[] crcKey = Encoding.Unicode.GetBytes("\"m_Crc\":");
            int crcAt = LastIndexOfBefore(extra, crcKey, at);
            if (crcAt < 0)
            {
                Engine.Warn(manifest, $"catalog: entry for '{bundleName}' has no m_Crc field — CRC not adjusted.");
                continue;
            }
            int digitsStart = crcAt + crcKey.Length;
            int end = digitsStart;
            while (end + 1 < extra.Length && extra[end + 1] == 0 && extra[end] >= (byte)'0' && extra[end] <= (byte)'9')
                end += 2;
            string digits = Encoding.Unicode.GetString(extra, digitsStart, end - digitsStart);
            if (digits.Length == 0 || digits == "0") continue;

            // length-preserving: "0" followed by JSON-whitespace padding
            extra[digitsStart] = (byte)'0';
            extra[digitsStart + 1] = 0;
            for (int i = digitsStart + 2; i < end; i += 2)
            {
                extra[i] = (byte)' ';
                extra[i + 1] = 0;
            }
            changed = true;
            manifest.CatalogCrc.ZeroedThisRun++;
            Console.WriteLine($"  catalog: zeroed CRC {digits} for bundle '{bundleName}'");
        }

        if (!changed) return;

        // back up the pristine catalog once, then rewrite m_ExtraDataString
        string rel = Path.GetRelativePath(gameData, path);
        string backup = Path.Combine(backupDir, rel);
        if (!File.Exists(backup))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            File.Copy(path, backup);
            Console.WriteLine($"  backed up {rel}");
        }

        string json = File.ReadAllText(path);
        using var original = JsonDocument.Parse(json);
        string oldB64 = original.RootElement.GetProperty("m_ExtraDataString").GetString()!;
        string newB64 = Convert.ToBase64String(extra);
        // surgical string replace keeps every other byte of the catalog identical
        int idx = json.IndexOf(oldB64, StringComparison.Ordinal);
        if (idx < 0) throw new InvalidOperationException("catalog.json: could not relocate m_ExtraDataString for rewrite.");
        string tmp = path + ".sopatch-tmp";
        File.WriteAllText(tmp, json[..idx] + newB64 + json[(idx + oldB64.Length)..]);
        File.Move(tmp, path, overwrite: true);
        Console.WriteLine($"  catalog updated: {rel}");
    }

    private static byte[] GetExtraData(string path, out JsonDocument doc)
    {
        doc = JsonDocument.Parse(File.ReadAllText(path));
        string b64 = doc.RootElement.TryGetProperty("m_ExtraDataString", out var v)
            ? v.GetString() ?? "" : "";
        return b64.Length == 0 ? Array.Empty<byte>() : Convert.FromBase64String(b64);
    }

    private static IEnumerable<(int offset, string digits)> FindCrcFields(byte[] data)
    {
        byte[] key = Encoding.Unicode.GetBytes("\"m_Crc\":");
        int i = IndexOf(data, key, 0);
        while (i >= 0)
        {
            int start = i + key.Length;
            int end = start;
            while (end + 1 < data.Length && data[end + 1] == 0 && data[end] >= (byte)'0' && data[end] <= (byte)'9')
                end += 2;
            yield return (i, Encoding.Unicode.GetString(data, start, end - start));
            i = IndexOf(data, key, i + 2);
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (int i = from; i <= haystack.Length - needle.Length; i++)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }

    private static int LastIndexOfBefore(byte[] haystack, byte[] needle, int before)
    {
        for (int i = Math.Min(before, haystack.Length - needle.Length); i >= 0; i--)
        {
            int j = 0;
            while (j < needle.Length && haystack[i + j] == needle[j]) j++;
            if (j == needle.Length) return i;
        }
        return -1;
    }
}
