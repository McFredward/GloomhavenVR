#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace GloomhavenVR.Quest
{
    public static partial class QuestGameContent
    {
        /// <summary>One worker owns validation, authenticated copy and extraction.
        /// Full byte checks are retained on every launch; proofs are operation-local.
        /// Completed files survive interruption and are individually rechecked next time.</summary>
        public static QuestGameContentDeliveryResult Deliver(QuestGameContentManifest manifest, string root, string sourcePath,
            bool sourceIsApk, string archivePath, string expectedArchive = "quest-startup-content.zip",
            Action<QuestGameContentProgress> progress = null)
        {
            Validate(manifest, manifest == null ? null : manifest.inputKey, expectedArchive);
            var result = new QuestGameContentDeliveryResult();
            var tracker = new DeliveryProgress(progress, manifest.files.Length);
            var verified = new Dictionary<string, QuestContentHash.FileIdentity>(StringComparer.Ordinal);
            var needed = new HashSet<string>(StringComparer.Ordinal);
            long neededBytes = 0;
            for (int i = 0; i < manifest.files.Length; ++i)
            {
                QuestGameContentFile file = manifest.files[i];
                string target = Target(root, file.path);
                bool matches = false;
                QuestContentHash.FileIdentity before = default;
                if (File.Exists(target) && new FileInfo(target).Length == file.size)
                {
                    before = QuestContentHash.Identity(target);
                    matches = Hash(target, tracker.Stream(i + 1), "checking-files", file.path) == file.sha256;
                    if (!before.Equals(QuestContentHash.Identity(target)))
                        throw new InvalidDataException("Content changed during its verification: " + file.path);
                }
                if (matches)
                {
                    verified.Add(file.path, before);
                    ++result.VerifiedFiles; result.VerifiedBytes = checked(result.VerifiedBytes + file.size);
                }
                else { needed.Add(file.path); neededBytes = checked(neededBytes + file.size); }
            }
            if (needed.Count == 0)
            {
                result.ReusedContent = true;
                tracker.Plan(0); tracker.Complete();
                CheckVerified(manifest, root, verified);
                return result;
            }

            archivePath = Path.GetFullPath(archivePath);
            QuestGameArchiveDelivery.ValidateDestination(archivePath);
            foreach (QuestGameContentFile file in manifest.files)
                if (string.Equals(archivePath, Target(root, file.path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The staged archive aliases its content destination.");
            QuestGameArchiveDelivery.VerifiedArchive archive = null;
            if (File.Exists(archivePath))
            {
                long size = new FileInfo(archivePath).Length;
                QuestContentHash.FileIdentity before = QuestContentHash.Identity(archivePath);
                if (size >= 22 && size <= QuestGameArchiveDelivery.MaximumArchiveBytes(manifest)
                    && Hash(archivePath, tracker.Stream(0), "verifying-archive", expectedArchive) == manifest.archiveSha256)
                {
                    archive = new QuestGameArchiveDelivery.VerifiedArchive(manifest, archivePath, before);
                    result.ReusedArchive = true;
                }
            }
            if (archive == null)
            {
                long length = QuestGameArchiveDelivery.SourceLength(manifest, sourcePath, sourceIsApk);
                tracker.Plan(checked(length + neededBytes));
                archive = QuestGameArchiveDelivery.StageVerified(manifest, sourcePath, archivePath, sourceIsApk, tracker.Stream(0));
                result.CopiedArchive = true; result.CopiedBytes = archive.Length;
            }
            else tracker.Plan(neededBytes);

            ExtractDelivered(manifest, archive, root, needed, verified, tracker, result);
            tracker.Complete();
            CheckVerified(manifest, root, verified);
            archive.Check(manifest);
            return result;
        }

        static void ExtractDelivered(QuestGameContentManifest manifest, QuestGameArchiveDelivery.VerifiedArchive archive, string root,
            HashSet<string> needed, Dictionary<string, QuestContentHash.FileIdentity> verified, DeliveryProgress tracker,
            QuestGameContentDeliveryResult result)
        {
            archive.Check(manifest);
            Directory.CreateDirectory(root);
            var wanted = new Dictionary<string, QuestGameContentFile>(StringComparer.Ordinal);
            foreach (QuestGameContentFile file in manifest.files) wanted.Add(file.path, file);
            using (var stream = new FileStream(archive.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
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
                for (int i = 0; i < manifest.files.Length; ++i)
                {
                    QuestGameContentFile file = manifest.files[i];
                    if (!needed.Contains(file.path)) continue;
                    string target = Target(root, file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    string temp = target + ".quest-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        Action<QuestGameContentProgress> notify = tracker.Stream(i + 1);
                        using (var sha = QuestContentHash.Create())
                        using (Stream input = entries[file.path].Open())
                        using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            byte[] buffer = new byte[QuestContentHash.BufferSize]; long written = 0, reported = 0; int count;
                            Report(notify, "extracting-file", file.path, 0, file.size);
                            while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                            {
                                if (count > file.size - written) throw new InvalidDataException("Startup entry exceeds its manifested size.");
                                sha.Update(buffer, count); output.Write(buffer, 0, count); written += count;
                                if (written - reported >= 1048576) { Report(notify, "extracting-file", file.path, written, file.size); reported = written; }
                            }
                            if (written != file.size) throw new InvalidDataException("Startup entry is truncated.");
                            if (sha.Finish() != file.sha256) throw new InvalidDataException("Startup file SHA-256 mismatch: " + file.path);
                            output.Flush(true);
                            if (written != reported || written == 0) Report(notify, "extracting-file", file.path, written, file.size);
                        }
                        // The unique temporary file had one writer. Byte hashing happened
                        // during that write; the operation-local identity then guards the
                        // published file until completion. No later launch trusts it.
                        Target(root, file.path);
                        if (File.Exists(target)) File.Replace(temp, target, null); else File.Move(temp, target);
                        verified.Add(file.path, QuestContentHash.Identity(target));
                        ++result.ExtractedFiles; result.ExtractedBytes = checked(result.ExtractedBytes + file.size);
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
        }

        static void CheckVerified(QuestGameContentManifest manifest, string root, Dictionary<string, QuestContentHash.FileIdentity> verified)
        {
            foreach (QuestGameContentFile file in manifest.files)
                if (!verified[file.path].Equals(QuestContentHash.Identity(Target(root, file.path))))
                    throw new InvalidDataException("Verified content changed during delivery: " + file.path);
        }

        sealed class DeliveryProgress
        {
            readonly Action<QuestGameContentProgress> receiver;
            readonly int files;
            long processed, total = -1;
            internal DeliveryProgress(Action<QuestGameContentProgress> receiver, int files) { this.receiver = receiver; this.files = files; }
            internal Action<QuestGameContentProgress> Stream(int index)
            {
                long previous = 0;
                return update =>
                {
                    if (update.ProcessedBytes < previous) throw new InvalidDataException("Content stream progress regressed.");
                    processed = checked(processed + update.ProcessedBytes - previous); previous = update.ProcessedBytes;
                    if (total >= 0 && processed > total) throw new InvalidDataException("Content work exceeded its planned byte count.");
                    if (receiver != null) receiver(new QuestGameContentProgress(update.Phase, update.File, update.ProcessedBytes, update.TotalBytes,
                        processed, total, index, files));
                };
            }
            internal void Plan(long remaining) { total = checked(processed + remaining); }
            internal void Complete()
            {
                if (processed != total) throw new InvalidDataException("Content work did not complete its planned byte count.");
                if (receiver != null) receiver(new QuestGameContentProgress("content-ready", "", 0, 0, processed, total, 0, files));
            }
        }

    }

    /// <summary>Copies one manifested archive without involving Unity's frame-driven network stack.</summary>
    public static class QuestGameArchiveDelivery
    {
        internal static long SourceLength(QuestGameContentManifest manifest, string sourcePath, bool sourceIsApk)
        {
            using (var source = File.OpenRead(sourcePath))
            {
                if (!sourceIsApk) { CheckLength(source.Length, MaximumArchiveBytes(manifest)); return source.Length; }
                using (var apk = new ZipArchive(source, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry selected = null;
                    foreach (ZipArchiveEntry entry in apk.Entries)
                        if (entry.FullName == "assets/" + manifest.archive)
                        {
                            if (selected != null) throw new InvalidDataException("The APK contains duplicate content archives.");
                            selected = entry;
                        }
                    if (selected == null) throw new InvalidDataException("The APK is missing its manifested content archive.");
                    CheckLength(selected.Length, MaximumArchiveBytes(manifest)); return selected.Length;
                }
            }
        }
        // B613 stopped before the mod had initialized, while its bootstrap was waiting on
        // jar:file UnityWebRequest delivery or subsequent extraction. The capture does not
        // distinguish those stages. A managed worker now reads the known monolithic APK
        // directly: copying, verification and extraction each have independent progress.
        // No whole APK/archive buffer or Unity API belongs in this class.
        public static void Stage(QuestGameContentManifest manifest, string sourcePath, string destination, bool sourceIsApk,
            Action<QuestGameContentProgress> progress = null)
        { StageVerified(manifest, sourcePath, destination, sourceIsApk, progress); }

        internal static VerifiedArchive StageVerified(QuestGameContentManifest manifest, string sourcePath, string destination, bool sourceIsApk,
            Action<QuestGameContentProgress> progress = null)
        {
            QuestGameContent.Validate(manifest, manifest == null ? null : manifest.inputKey, manifest == null ? null : manifest.archive);
            sourcePath = Path.GetFullPath(sourcePath);
            destination = Path.GetFullPath(destination);
            if (string.Equals(sourcePath, destination, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive delivery cannot overwrite its source.");
            ValidateDestination(destination);
            long maximumBytes = MaximumArchiveBytes(manifest);
            using (var source = File.OpenRead(sourcePath))
            {
                if (!sourceIsApk) return Copy(manifest, source, source.Length, maximumBytes, destination, progress);
                using (var apk = new ZipArchive(source, ZipArchiveMode.Read))
                {
                    string wanted = "assets/" + manifest.archive;
                    ZipArchiveEntry selected = null;
                    foreach (ZipArchiveEntry entry in apk.Entries)
                        if (entry.FullName == wanted)
                        {
                            if (selected != null) throw new InvalidDataException("The APK contains duplicate content archives: " + wanted);
                            selected = entry;
                        }
                    if (selected == null) throw new InvalidDataException("The APK is missing its manifested content archive: " + wanted);
                    CheckLength(selected.Length, maximumBytes);
                    using (Stream input = selected.Open()) return Copy(manifest, input, selected.Length, maximumBytes, destination, progress);
                }
            }
        }

        internal static VerifiedArchive VerifyExternal(QuestGameContentManifest manifest, string sourcePath,
            Action<QuestGameContentProgress> progress)
        {
            QuestGameContent.Validate(manifest, manifest.inputKey, manifest.archive);
            if (!manifest.externalDelivery) throw new InvalidDataException("External bank is outside this delivery contract.");
            sourcePath = Path.GetFullPath(sourcePath);
            ValidateDestination(sourcePath);
            QuestContentHash.FileIdentity before = QuestContentHash.Identity(sourcePath);
            CheckLength(before.Size, MaximumArchiveBytes(manifest));
            if (QuestGameContent.Hash(sourcePath, progress, "verifying-archive", manifest.archive) != manifest.archiveSha256)
                throw new InvalidDataException("External Campaign bank differs from the selected APK.");
            // The token checks unchanged identity during extraction. Do not
            // copy a second multi-GB archive into this same private directory.
            return new VerifiedArchive(manifest, sourcePath, before);
        }

        static VerifiedArchive Copy(QuestGameContentManifest manifest, Stream input, long length, long maximumBytes, string destination,
            Action<QuestGameContentProgress> progress)
        {
            CheckLength(length, maximumBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".quest-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var sha = QuestContentHash.Create())
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[QuestContentHash.BufferSize]; long copied = 0, reported = 0; int count;
                        QuestGameContent.Report(progress, "copying-archive", manifest.archive, 0, length);
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            // Check before writing; both the central-directory length and
                            // the actual inflated stream are bounded independently.
                            if (count > length - copied || count > maximumBytes - copied)
                                throw new InvalidDataException("Content archive exceeds its allowed size.");
                            sha.Update(buffer, count);
                            output.Write(buffer, 0, count); copied += count;
                            if (copied - reported >= 1048576)
                            { QuestGameContent.Report(progress, "copying-archive", manifest.archive, copied, length); reported = copied; }
                        }
                        if (copied != length) throw new InvalidDataException("Content archive is truncated.");
                        output.Flush(true);
                        if (copied != reported) QuestGameContent.Report(progress, "copying-archive", manifest.archive, copied, length);
                    }
                    if (sha.Finish() != manifest.archiveSha256)
                        throw new InvalidDataException("Delivered archive SHA-256 does not match the build manifest.");
                }
                ValidateDestination(destination);
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
                return new VerifiedArchive(manifest, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static long MaximumArchiveBytes(QuestGameContentManifest manifest)
        {
            try
            {
                // Our builder writes a standard ZIP, including files which may already
                // be compressed. Allow ZIP64/UTF-8 headers and DEFLATE worst-case growth,
                // while rejecting an archive unrelated to its manifested payload sizes.
                long maximum = 1048576;
                checked
                {
                    foreach (QuestGameContentFile file in manifest.files)
                        maximum += file.size + file.size / 512 + 1024 + 2L * Encoding.UTF8.GetByteCount(file.path);
                }
                return maximum;
            }
            catch (OverflowException error) { throw new InvalidDataException("Content archive size bound overflowed.", error); }
        }

        internal static void CheckLength(long length, long maximumBytes)
        {
            if (length < 22 || length > maximumBytes) throw new InvalidDataException("Content archive size is outside its manifested bounds.");
        }

        internal static void ValidateDestination(string destination)
        {
            for (string parent = Path.GetDirectoryName(destination); parent != null; parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Archive delivery parent is a symbolic link.");
            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive delivery target is a symbolic link.");
            if (Directory.Exists(destination)) throw new InvalidDataException("Archive delivery destination is a directory.");
        }

        // A full hash or authenticated copy issued this token inside the current
        // worker operation. It is not a persistent trust receipt.
        internal sealed class VerifiedArchive
        {
            internal readonly string Path;
            internal readonly long Length;
            readonly string input, hash;
            readonly QuestContentHash.FileIdentity identity;
            internal VerifiedArchive(QuestGameContentManifest manifest, string path)
                : this(manifest, path, QuestContentHash.Identity(path)) { }
            internal VerifiedArchive(QuestGameContentManifest manifest, string path, QuestContentHash.FileIdentity before)
            {
                Path = System.IO.Path.GetFullPath(path); input = manifest.inputKey; hash = manifest.archiveSha256;
                identity = before; Length = identity.Size;
                Check(manifest);
            }
            internal void Check(QuestGameContentManifest manifest)
            {
                ValidateDestination(Path);
                if (manifest.inputKey != input || manifest.archiveSha256 != hash || !identity.Equals(QuestContentHash.Identity(Path)))
                    throw new InvalidDataException("Verified content archive changed during delivery.");
            }
        }
    }
}
#endif
