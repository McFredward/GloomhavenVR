using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace GloomhavenVR.Core;

/// <summary>
/// Verify and unpack the downloaded release zip.
///
/// <para>WHY THIS TALKS TO <c>System.IO.Compression</c> THROUGH REFLECTION. <c>ZipArchive</c> lives
/// in <c>System.IO.Compression.dll</c> and <c>ZipFile</c> in
/// <c>System.IO.Compression.FileSystem.dll</c>; neither is in the default reference set of an
/// SDK-style net472 project, so using them by name needs one <c>&lt;Reference&gt;</c> line in
/// <c>src/GloomhavenVR/GloomhavenVR.csproj</c> — a file this lane does not own. Both assemblies
/// SHIP WITH THE GAME (<c>Gloomhaven_Data/Managed/</c>), so they load at runtime without anything
/// being added to the package; only the compile-time reference is missing.</para>
///
/// <para>INTEGRATOR: adding
/// <c>&lt;Reference Include="System.IO.Compression" Private="false" /&gt;</c> and
/// <c>&lt;Reference Include="System.IO.Compression.FileSystem" Private="false" /&gt;</c> to the
/// plugin csproj lets this whole file collapse into about thirty lines of direct calls. The public
/// surface below (<see cref="Verify"/> / <see cref="Extract"/>) is what the rest of the feature
/// uses and would not change.</para>
///
/// <para>WHAT IS AND IS NOT CHECKED — see <see cref="Verify"/>. Short version: this establishes
/// that the file is a real zip laid out like a GloomhavenVR release and that it carries the two
/// assemblies the mod is made of. It does NOT establish authorship. There is no signature and no
/// published checksum in the GitHub releases API beyond the byte size, so the trust anchor is TLS
/// to github.com and nothing else.</para>
/// </summary>
internal static class SelfUpdateZip
{
    /// <summary>Entries below this path, plus the single file <see cref="AllowedRootFile"/>, are
    /// the entire legal contents of a release zip (see scripts/package-release.sh).</summary>
    internal const string AllowedPrefix = "BepInEx/";

    /// <summary>The one entry a release zip carries outside <see cref="AllowedPrefix"/>.</summary>
    internal const string AllowedRootFile = "INSTALL.txt";

    /// <summary>Entries that MUST be present, or the zip is not a GloomhavenVR release.</summary>
    internal static readonly string[] RequiredEntries =
    {
        "BepInEx/plugins/GloomhavenVR/GloomhavenVR.dll",
        "BepInEx/patchers/GloomhavenVR/GloomhavenVR.Preload.dll",
    };

    /// <summary>A release zip has well under a hundred entries; anything wilder is not one.</summary>
    private const int MaxEntries = 4096;

    /// <summary>What <see cref="Verify"/> established about a downloaded file.</summary>
    internal sealed class Verdict
    {
        internal bool Ok;
        /// <summary>Which term failed, in the words the log line prints. Empty when Ok.</summary>
        internal string FailedTerm = string.Empty;
        internal int EntryCount;
        internal long UncompressedBytes;
        /// <summary>Entry names, in archive order — reused by <see cref="Extract"/>.</summary>
        internal List<string> Entries = new();
    }

    /// <summary>
    /// Open the zip and decide whether it may be installed. Everything checked here is checked
    /// BEFORE a single byte is written outside the staging folder.
    ///
    /// <list type="bullet">
    /// <item>the file is exactly <paramref name="expectedBytes"/> long — the size GitHub PUBLISHES
    /// for the asset, which is the only integrity figure the API offers;</item>
    /// <item>it opens as a zip at all, and its central directory is readable;</item>
    /// <item>every entry name is relative, has no <c>..</c> segment, no drive letter and no leading
    /// separator — a zip-slip entry means the whole archive is rejected, not skipped;</item>
    /// <item>every entry is under <c>BepInEx/</c> or is <c>INSTALL.txt</c>;</item>
    /// <item><see cref="RequiredEntries"/> are all present and non-empty.</item>
    /// </list>
    ///
    /// <para>NOT CHECKED, and deliberately: who built it. No signature, no hash from a second
    /// channel, no certificate pinning beyond what the OS TLS stack does for github.com. If the
    /// user wants more than that, the honest answer is a published SHA-256 in the release notes and
    /// a comparison here — this function is where that check would go.</para>
    /// </summary>
    internal static Verdict Verify(string zipPath, long expectedBytes)
    {
        var verdict = new Verdict();
        IDisposable? archive = null;
        try
        {
            var info = new FileInfo(zipPath);
            if (!info.Exists)
            {
                verdict.FailedTerm = "the downloaded file is not there";
                return verdict;
            }
            if (info.Length != expectedBytes)
            {
                verdict.FailedTerm = $"size mismatch — got {info.Length} bytes, the release "
                    + $"publishes {expectedBytes}";
                return verdict;
            }

            if (!TryOpen(zipPath, out archive, out string openError))
            {
                verdict.FailedTerm = $"not a readable zip — {openError}";
                return verdict;
            }

            foreach (object entry in EnumerateEntries(archive!))
            {
                string name = EntryName(entry);
                long length = EntryLength(entry);

                if (verdict.EntryCount++ > MaxEntries)
                {
                    verdict.FailedTerm = $"more than {MaxEntries} entries — not a release archive";
                    return verdict;
                }
                if (name.Length == 0)
                    continue; // directory marker with no name; harmless, nothing to extract
                if (!IsSafeEntryName(name))
                {
                    verdict.FailedTerm = $"entry '{name}' escapes the install folder";
                    return verdict;
                }
                if (!name.StartsWith(AllowedPrefix, StringComparison.Ordinal)
                    && !string.Equals(name, AllowedRootFile, StringComparison.Ordinal))
                {
                    verdict.FailedTerm = $"entry '{name}' is outside {AllowedPrefix} and is not "
                        + AllowedRootFile;
                    return verdict;
                }

                verdict.Entries.Add(name);
                verdict.UncompressedBytes += length;
            }

            foreach (string required in RequiredEntries)
            {
                if (!verdict.Entries.Contains(required))
                {
                    verdict.FailedTerm = $"'{required}' is missing from the archive";
                    return verdict;
                }
            }

            verdict.Ok = true;
            return verdict;
        }
        catch (Exception e)
        {
            verdict.Ok = false;
            verdict.FailedTerm = $"the archive could not be read — {e.Message}";
            return verdict;
        }
        finally
        {
            try { archive?.Dispose(); }
            catch (Exception) { /* closing a broken archive must not become the failure */ }
        }
    }

    /// <summary>
    /// Unpack a VERIFIED zip into <paramref name="destinationRoot"/>. Call only after
    /// <see cref="Verify"/> returned Ok — the safety of every path here rests on that pass.
    /// <paramref name="progress"/> is bumped to the number of entries written, so a caller on the
    /// main thread can read it while this runs on a worker.
    /// </summary>
    internal static bool Extract(string zipPath, string destinationRoot, int[] progress,
        out string error)
    {
        error = string.Empty;
        IDisposable? archive = null;
        try
        {
            Directory.CreateDirectory(destinationRoot);
            if (!TryOpen(zipPath, out archive, out error))
                return false;

            string root = Path.GetFullPath(destinationRoot);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                root += Path.DirectorySeparatorChar;

            int written = 0;
            foreach (object entry in EnumerateEntries(archive!))
            {
                string name = EntryName(entry);
                if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal))
                    continue;
                if (!IsSafeEntryName(name))
                {
                    error = $"entry '{name}' escapes the install folder";
                    return false;
                }

                string target = Path.GetFullPath(Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar)));
                // Belt and braces: the name test above already forbids this, and the resolved path
                // is checked anyway, because a path that escapes here writes into the game install.
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    error = $"entry '{name}' resolves outside the staging folder";
                    return false;
                }

                string? dir = Path.GetDirectoryName(target);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                using (Stream source = EntryOpen(entry))
                using (var destination = new FileStream(target, FileMode.Create, FileAccess.Write,
                           FileShare.None, 1 << 16))
                {
                    source.CopyTo(destination, 1 << 16);
                }

                written++;
                if (progress.Length > 0)
                    progress[0] = written;
            }
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
        finally
        {
            try { archive?.Dispose(); }
            catch (Exception) { /* see Verify */ }
        }
    }

    /// <summary>
    /// Relative, forward-slashed, no <c>..</c> segment, no drive letter, no leading separator, no
    /// backslash (a zip entry name never contains one; a name that does is trying to be a path on
    /// exactly one platform).
    /// </summary>
    internal static bool IsSafeEntryName(string name)
    {
        if (name.Length == 0 || name.Length > 240)
            return false;
        if (name[0] == '/' || name[0] == '\\')
            return false;
        if (name.IndexOf('\\') >= 0 || name.IndexOf(':') >= 0)
            return false;
        foreach (char c in name)
        {
            if (c < ' ')
                return false;
        }
        string[] parts = name.Split('/');
        foreach (string part in parts)
        {
            if (string.Equals(part, "..", StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    // ---- the reflection layer ------------------------------------------------------------------
    // Resolved once, on first use. A missing member means the BCL is not what it has been since
    // .NET 4.5 and the caller simply gets "not a readable zip"; nothing here ever throws upward.

    private static bool _resolved;
    private static MethodInfo? _openRead;
    private static PropertyInfo? _entries;
    private static PropertyInfo? _fullName;
    private static PropertyInfo? _length;
    private static MethodInfo? _open;

    private static bool Resolve(out string error)
    {
        error = string.Empty;
        if (_resolved)
            return _openRead != null;
        _resolved = true;
        try
        {
            Type? zipFile = Type.GetType(
                "System.IO.Compression.ZipFile, System.IO.Compression.FileSystem", throwOnError: false);
            Type? entryType = Type.GetType(
                "System.IO.Compression.ZipArchiveEntry, System.IO.Compression", throwOnError: false);
            Type? archiveType = Type.GetType(
                "System.IO.Compression.ZipArchive, System.IO.Compression", throwOnError: false);
            if (zipFile == null || entryType == null || archiveType == null)
            {
                error = "System.IO.Compression is not loadable in this runtime";
                _openRead = null;
                return false;
            }

            _openRead = zipFile.GetMethod("OpenRead", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string) }, null);
            _entries = archiveType.GetProperty("Entries", BindingFlags.Public | BindingFlags.Instance);
            _fullName = entryType.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
            _length = entryType.GetProperty("Length", BindingFlags.Public | BindingFlags.Instance);
            _open = entryType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance,
                null, Type.EmptyTypes, null);

            if (_openRead == null || _entries == null || _fullName == null || _length == null
                || _open == null)
            {
                error = "System.IO.Compression does not have the members this expects";
                _openRead = null;
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            _openRead = null;
            return false;
        }
    }

    private static bool TryOpen(string path, out IDisposable? archive, out string error)
    {
        archive = null;
        if (!Resolve(out error))
            return false;
        try
        {
            archive = (IDisposable?)_openRead!.Invoke(null, new object[] { path });
            if (archive == null)
            {
                error = "the archive could not be opened";
                return false;
            }
            return true;
        }
        catch (TargetInvocationException e)
        {
            error = e.InnerException?.Message ?? e.Message;
            return false;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static IEnumerable<object> EnumerateEntries(IDisposable archive)
    {
        object? entries = _entries!.GetValue(archive, null);
        if (entries is not System.Collections.IEnumerable list)
            yield break;
        foreach (object? entry in list)
        {
            if (entry != null)
                yield return entry;
        }
    }

    private static string EntryName(object entry) =>
        _fullName!.GetValue(entry, null) as string ?? string.Empty;

    private static long EntryLength(object entry)
    {
        object? value = _length!.GetValue(entry, null);
        return value is long l ? l : 0L;
    }

    private static Stream EntryOpen(object entry) =>
        (Stream)_open!.Invoke(entry, Array.Empty<object>());
}
