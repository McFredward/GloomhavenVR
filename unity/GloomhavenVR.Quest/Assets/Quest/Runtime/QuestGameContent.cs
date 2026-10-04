#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace GloomhavenVR.Quest
{
    [Serializable] public sealed class QuestGameContentFile { public string path, sha256; public long size; }
    [Serializable] public sealed class QuestGameContentManifest
    {
        public int schema;
        public string inputKey, archive, archiveSha256;
        public QuestGameContentFile[] files;
    }

    /// <summary>A bounded worker-thread update. The receiver must marshal Unity work itself.</summary>
    public sealed class QuestGameContentProgress
    {
        public string Phase { get; }
        public string File { get; }
        public long ProcessedBytes { get; }
        public long TotalBytes { get; }
        public long OverallProcessedBytes { get; }
        public long OverallTotalBytes { get; }
        public int FileIndex { get; }
        public int FileCount { get; }

        public QuestGameContentProgress(string phase, string file, long processedBytes, long totalBytes,
            long overallProcessedBytes = 0, long overallTotalBytes = -1, int fileIndex = 0, int fileCount = 0)
        {
            Phase = phase; File = file; ProcessedBytes = processedBytes; TotalBytes = totalBytes;
            OverallProcessedBytes = overallProcessedBytes; OverallTotalBytes = overallTotalBytes;
            FileIndex = fileIndex; FileCount = fileCount;
        }
    }

    public sealed class QuestGameContentDeliveryResult
    {
        public bool ReusedContent { get; internal set; }
        public bool CopiedArchive { get; internal set; }
        public bool ReusedArchive { get; internal set; }
        public int VerifiedFiles { get; internal set; }
        public int ExtractedFiles { get; internal set; }
        public long VerifiedBytes { get; internal set; }
        public long CopiedBytes { get; internal set; }
        public long ExtractedBytes { get; internal set; }
        public string ContentKey { get; internal set; }
        public string InstallationState { get; internal set; }
        public bool InstallationReceiptReused { get; internal set; }
        public bool InstallationReceiptPublished { get; internal set; }
        public int MetadataCheckedFiles { get; internal set; }
    }

    /// <summary>File-backed original content delivery. No rule or save interpretation.</summary>
    public static partial class QuestGameContent
    {
        public static void ConfigureNativeHash() => QuestContentHash.Configure();

        public static void Validate(QuestGameContentManifest manifest, string inputKey, string expectedArchive = "quest-startup-content.zip")
        {
            if (expectedArchive != "quest-startup-content.zip" && expectedArchive != "quest-mod-content.zip")
                throw new InvalidDataException("Content archive scope is unsupported.");
            if (manifest == null || manifest.schema != 1 || manifest.inputKey != inputKey || string.IsNullOrEmpty(inputKey)
                || manifest.archive != expectedArchive || !HashValid(manifest.archiveSha256) || manifest.files == null || manifest.files.Length == 0)
                throw new InvalidDataException("Startup content provenance is missing or inconsistent.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestGameContentFile file in manifest.files)
                if (file == null || !SafePath(file.path) || !paths.Add(file.path) || !HashValid(file.sha256) || file.size < 0)
                    throw new InvalidDataException("Invalid or duplicate startup content manifest entry.");
        }

        public static bool IsReady(QuestGameContentManifest manifest, string root, Action<QuestGameContentProgress> progress = null)
        {
            foreach (QuestGameContentFile file in manifest.files)
            {
                string target = Target(root, file.path);
                if (!File.Exists(target) || new FileInfo(target).Length != file.size
                    || Hash(target, progress, "checking-files", file.path) != file.sha256) return false;
            }
            return true;
        }

        public static string ResolveVerifiedPath(QuestGameContentManifest manifest, string root, string relative)
        {
            foreach (QuestGameContentFile file in manifest.files)
                if (file.path == relative)
                {
                    string target = Target(root, relative);
                    if (!File.Exists(target) || new FileInfo(target).Length != file.size || Hash(target) != file.sha256)
                        throw new InvalidDataException("Required startup file failed verification: " + relative);
                    return target;
                }
            throw new InvalidDataException("Required startup file is not manifested: " + relative);
        }

        public static void Extract(QuestGameContentManifest manifest, string archive, string root, string expectedArchive = "quest-startup-content.zip",
            Action<QuestGameContentProgress> progress = null)
        {
            Validate(manifest, manifest == null ? null : manifest.inputKey, expectedArchive);
            if (Hash(archive, progress, "verifying-archive", expectedArchive) != manifest.archiveSha256)
                throw new InvalidDataException("Startup archive SHA-256 does not match the build manifest.");
            Directory.CreateDirectory(root);
            var wanted = new Dictionary<string, QuestGameContentFile>(StringComparer.Ordinal);
            foreach (QuestGameContentFile file in manifest.files) wanted.Add(file.path, file);
            using (var stream = File.OpenRead(archive))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    QuestGameContentFile file;
                    if (!SafePath(entry.FullName) || !wanted.TryGetValue(entry.FullName, out file) || entries.ContainsKey(entry.FullName) || entry.Length != file.size)
                        throw new InvalidDataException("Unexpected, duplicate or wrong-size startup ZIP entry: " + entry.FullName);
                    entries.Add(entry.FullName, entry);
                }
                if (entries.Count != wanted.Count) throw new InvalidDataException("Startup ZIP is missing manifested files.");
                foreach (QuestGameContentFile file in manifest.files)
                {
                    string target = Target(root, file.path);
                    if (File.Exists(target) && new FileInfo(target).Length == file.size
                        && Hash(target, progress, "checking-files", file.path) == file.sha256) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + ".quest-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (Stream input = entries[file.path].Open())
                        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                        {
                            byte[] buffer = new byte[65536]; long written = 0, reported = 0; int count;
                            Report(progress, "extracting-file", file.path, 0, file.size);
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                written += count;
                                if (written > file.size) throw new InvalidDataException("Startup entry exceeds its manifested size.");
                                output.Write(buffer, 0, count);
                                if (written - reported >= 1048576) { Report(progress, "extracting-file", file.path, written, file.size); reported = written; }
                            }
                            if (written != file.size) throw new InvalidDataException("Startup entry is truncated.");
                            output.Flush(true);
                            if (written != reported || written == 0) Report(progress, "extracting-file", file.path, written, file.size);
                        }
                        if (Hash(temp, progress, "verifying-file", file.path) != file.sha256)
                            throw new InvalidDataException("Startup file SHA-256 mismatch: " + file.path);
                        if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            if (!IsReady(manifest, root, progress)) throw new InvalidDataException("Extracted startup content failed its final verification.");
        }

        // Routing already verified content must retain the same containment and
        // symlink checks without repeating large media hashes on Unity's thread.
        internal static string ResolvePath(string root, string relative) => Target(root, relative);

        static string Target(string root, string relative)
        {
            if (!SafePath(relative)) throw new InvalidDataException("Unsafe content path.");
            root = Path.GetFullPath(root);
            string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidDataException("Content escapes its private root.");
            for (string parent = Path.GetDirectoryName(target); parent != null; parent = Path.GetDirectoryName(parent))
            {
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Content parent is a symbolic link.");
                if (parent == root) break;
            }
            if (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Content target is a symbolic link.");
            return target;
        }
        static bool SafePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 || path.IndexOf('\0') >= 0 || Path.IsPathRooted(path)) return false;
            foreach (string part in path.Split('/')) if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }
        static bool HashValid(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            foreach (char c in hash) if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;
            return true;
        }
        internal static void Report(Action<QuestGameContentProgress> progress, string phase, string file, long processed, long total)
        { if (progress != null) progress(new QuestGameContentProgress(phase, file, processed, total)); }

        public static string Hash(string path, Action<QuestGameContentProgress> progress = null, string phase = "hash", string relative = null)
        {
            using (var sha = QuestContentHash.Create())
            using (var file = File.OpenRead(path))
            {
                QuestContentHash.FileIdentity before = QuestContentHash.Identity(path);
                byte[] buffer = new byte[QuestContentHash.BufferSize]; long processed = 0, reported = 0; int count;
                long total = file.Length;
                Report(progress, phase, relative ?? Path.GetFileName(path), 0, total);
                while ((count = file.Read(buffer, 0, buffer.Length)) != 0)
                {
                    sha.Update(buffer, count);
                    processed += count;
                    if (processed - reported >= 1048576) { Report(progress, phase, relative ?? Path.GetFileName(path), processed, total); reported = processed; }
                }
                string digest = sha.Finish();
                if (processed != reported || processed == 0) Report(progress, phase, relative ?? Path.GetFileName(path), processed, total);
                if (processed != total || !before.Equals(QuestContentHash.Identity(path)))
                    throw new InvalidDataException("Content changed while it was being hashed: " + path);
                return digest;
            }
        }
    }
}
#endif
