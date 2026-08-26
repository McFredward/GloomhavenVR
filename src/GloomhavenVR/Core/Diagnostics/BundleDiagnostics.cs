using System;
using System.IO;
using System.Text;

namespace GloomhavenVR.Core;

/// <summary>
/// Explains WHY <c>AssetBundle.LoadFromFile</c> returned null.
/// </summary>
/// <remarks>
/// Unity's own message ("Unable to read header from archive file") only lands in
/// Player.log, never in the mod's log, and it does not name the cause. The mod meanwhile
/// logs one warning per subsystem and then runs happily on procedural fallbacks — so a
/// dead bundle looks like "the hands render wrong", not "no bundled asset loaded at all".
/// That mis-read has cost two debugging rounds, hence this.
///
/// Everything needed sits in the first 30 bytes of a UnityFS archive:
///
///   0   "UnityFS\0"        magic
///   8   uint32 big-endian  wrapper format version
///   12  "5.x.x\0"          engine generation
///   18  NUL-terminated     the editor version that wrote the file
///
/// The one failure we actually hit: the game runs Unity 2021.3.5f1, whose runtime cannot
/// read the **format 8** wrapper a 2021.3.45f1 editor emits. Reading the header costs a
/// single 30-byte file read and only happens on the failure path.
/// </remarks>
internal static class BundleDiagnostics
{
    /// <summary>The Unity version of the game runtime — the only one that can read our bundles.</summary>
    private const string GameUnityVersion = "2021.3.5f1";
    private const int SupportedFormat = 7;

    /// <summary>
    /// A one-sentence cause for a failed load, ready to append to a log line. Never throws:
    /// this runs while something is already wrong, so an unreadable header must not add a
    /// second failure on top.
    /// </summary>
    internal static string Explain(string bundlePath)
    {
        try
        {
            // 64, not the 30 the fields need: the writer string at offset 18 is variable
            // length, and a truncated read would silently report the wrong editor.
            var header = new byte[64];
            int read;
            using (var fs = new FileStream(bundlePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                read = fs.Read(header, 0, header.Length);

            if (read < header.Length)
                return $"the file is only {new FileInfo(bundlePath).Length} bytes — truncated or still being copied.";

            if (Encoding.ASCII.GetString(header, 0, 7) != "UnityFS")
                return "the file is not a UnityFS archive at all (wrong file, or a corrupted copy).";

            // Big-endian, and BitConverter is little-endian on every platform we ship to.
            int format = (header[8] << 24) | (header[9] << 16) | (header[10] << 8) | header[11];
            string writer = ReadCString(header, 18);

            if (format != SupportedFormat)
                return $"it is a UnityFS FORMAT {format} archive written by Unity '{writer}', and this game " +
                       $"(Unity {GameUnityVersion}) can only read format {SupportedFormat}. The bundle was built " +
                       $"with the WRONG EDITOR — rebuild it with a {GameUnityVersion} editor. NOTE this affects " +
                       "EVERY bundled asset (hands, board, heads), not just the one that logged this.";

            if (!string.IsNullOrEmpty(writer) && writer != GameUnityVersion)
                return $"format {format} is fine, but it was written by Unity '{writer}' rather than " +
                       $"{GameUnityVersion} — suspect the shaders inside (pink materials) rather than the wrapper.";

            return $"the header is valid (format {format}, Unity '{writer}'), so the cause is inside the " +
                   "archive — a partial download, or an asset that fails to deserialize.";
        }
        catch (Exception e)
        {
            return $"the header could not even be read ({e.GetType().Name}: {e.Message}).";
        }
    }

    private static string ReadCString(byte[] buffer, int start)
    {
        int end = start;
        while (end < buffer.Length && buffer[end] != 0) end++;
        return Encoding.ASCII.GetString(buffer, start, end - start);
    }
}
