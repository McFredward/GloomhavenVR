#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace GloomhavenVR.Quest
{
    /// <summary>Copies one manifested archive without involving Unity's frame-driven network stack.</summary>
    public static class QuestGameArchiveDelivery
    {
        // B613 stopped before the mod had initialized, while its bootstrap was waiting on
        // jar:file UnityWebRequest delivery or subsequent extraction. The capture does not
        // distinguish those stages. A managed worker now reads the known monolithic APK
        // directly: copying, verification and extraction each have independent progress.
        // No whole APK/archive buffer or Unity API belongs in this class.
        public static void Stage(QuestGameContentManifest manifest, string sourcePath, string destination, bool sourceIsApk,
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
                if (!sourceIsApk) { Copy(manifest, source, source.Length, maximumBytes, destination, progress); return; }
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
                    using (Stream input = selected.Open()) Copy(manifest, input, selected.Length, maximumBytes, destination, progress);
                }
            }
        }

        static void Copy(QuestGameContentManifest manifest, Stream input, long length, long maximumBytes, string destination,
            Action<QuestGameContentProgress> progress)
        {
            CheckLength(length, maximumBytes);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + ".quest-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var sha = SHA256.Create())
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    {
                        byte[] buffer = new byte[65536]; long copied = 0, reported = 0; int count;
                        QuestGameContent.Report(progress, "copying-archive", manifest.archive, 0, length);
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            // Check before writing; both the central-directory length and
                            // the actual inflated stream are bounded independently.
                            if (count > length - copied || count > maximumBytes - copied)
                                throw new InvalidDataException("Content archive exceeds its allowed size.");
                            sha.TransformBlock(buffer, 0, count, buffer, 0);
                            output.Write(buffer, 0, count); copied += count;
                            if (copied - reported >= 1048576)
                            { QuestGameContent.Report(progress, "copying-archive", manifest.archive, copied, length); reported = copied; }
                        }
                        if (copied != length) throw new InvalidDataException("Content archive is truncated.");
                        output.Flush(true);
                        if (copied != reported) QuestGameContent.Report(progress, "copying-archive", manifest.archive, copied, length);
                    }
                    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    if (BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() != manifest.archiveSha256)
                        throw new InvalidDataException("Delivered archive SHA-256 does not match the build manifest.");
                }
                ValidateDestination(destination);
                if (File.Exists(destination)) File.Replace(temporary, destination, null); else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static long MaximumArchiveBytes(QuestGameContentManifest manifest)
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

        static void CheckLength(long length, long maximumBytes)
        {
            if (length < 22 || length > maximumBytes) throw new InvalidDataException("Content archive size is outside its manifested bounds.");
        }

        static void ValidateDestination(string destination)
        {
            for (string parent = Path.GetDirectoryName(destination); parent != null; parent = Path.GetDirectoryName(parent))
                if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Archive delivery parent is a symbolic link.");
            if (File.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive delivery target is a symbolic link.");
            if (Directory.Exists(destination)) throw new InvalidDataException("Archive delivery destination is a directory.");
        }
    }
}
#endif
