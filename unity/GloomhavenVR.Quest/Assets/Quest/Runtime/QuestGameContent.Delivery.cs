#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GloomhavenVR.Quest
{
    public static partial class QuestGameContent
    {
        const string InstallationReceiptName = ".quest-installation.receipt";
        const int InstallationReceiptSchema = 1;
        const int MaximumReceiptBytes = 8 * 1048576;
        const int MaximumReceiptFiles = 32768;
        const int MaximumReceiptPathBytes = 8192;
        static readonly UTF8Encoding ReceiptEncoding = new UTF8Encoding(false, true);

        /// <summary>Reuse an installed bank using a local receipt and cheap file
        /// metadata. No content-byte reads, archive access or progress callbacks.
        /// ConfigureNativeHash must precede workers on Android, as for Deliver.</summary>
        public static QuestGameContentDeliveryResult GetExisting(QuestGameContentManifest manifest, string root,
            string expectedArchive = "quest-startup-content.zip")
        {
            ValidateInstallation(manifest, root, expectedArchive);
            string key = InstallationContentKey(manifest);
            InstallationReceipt receipt = ReadInstallationReceipt(root);
            if (receipt == null || receipt.ContentKey != key) return null;
            foreach (QuestGameContentFile file in manifest.files)
                if (!ReceiptFileMatches(receipt, file, Target(root, file.path))) return null;
            return new QuestGameContentDeliveryResult
            {
                ReusedContent = true, ContentKey = key, InstallationState = "cached",
                InstallationReceiptReused = true, MetadataCheckedFiles = manifest.files.Length
            };
        }

        /// <summary>Install or repair owned content once, then retain a local
        /// installation receipt. Existing legacy files without a usable receipt
        /// receive one native byte check for adoption. Only changed/missing files
        /// require later work. This cache assumes private application ownership;
        /// it is neither an anti-piracy measure nor an adversarial file proof.</summary>
        public static QuestGameContentDeliveryResult Install(QuestGameContentManifest manifest, string root, string sourcePath,
            bool sourceIsApk, string archivePath, string expectedArchive = "quest-startup-content.zip",
            Action<QuestGameContentProgress> progress = null)
        {
            QuestGameContentDeliveryResult existing = GetExisting(manifest, root, expectedArchive);
            if (existing != null) return existing;
            string key = InstallationContentKey(manifest);
            InstallationReceipt receipt = ReadInstallationReceipt(root);
            var result = new QuestGameContentDeliveryResult { ContentKey = key };
            var verified = new Dictionary<string, QuestContentHash.FileIdentity>(StringComparer.Ordinal);
            var needed = new HashSet<string>(StringComparer.Ordinal);
            long neededBytes = 0;
            foreach (QuestGameContentFile file in manifest.files)
            {
                string target = Target(root, file.path);
                if (ReceiptFileMatches(receipt, file, target))
                {
                    verified.Add(file.path, QuestContentHash.Identity(target));
                    ++result.MetadataCheckedFiles;
                    continue;
                }
                // Old B616/B617 installs have no receipt. Adopt those files once
                // instead of copying the entire movie bank again. This check is
                // not a separate visible verification stage or a warm-launch scan.
                if (File.Exists(target) && new FileInfo(target).Length == file.size)
                {
                    QuestContentHash.FileIdentity before = QuestContentHash.Identity(target);
                    if (Hash(target) == file.sha256)
                    {
                        if (!before.Equals(QuestContentHash.Identity(target)))
                            throw new InvalidDataException("Content changed during installation adoption: " + file.path);
                        verified.Add(file.path, before);
                        ++result.VerifiedFiles; result.VerifiedBytes = checked(result.VerifiedBytes + file.size);
                        continue;
                    }
                }
                needed.Add(file.path); neededBytes = checked(neededBytes + file.size);
            }
            if (needed.Count == 0)
            {
                CheckVerified(manifest, root, verified);
                PublishInstallationReceipt(manifest, root, key, verified);
                result.ReusedContent = true; result.InstallationReceiptPublished = true;
                result.InstallationState = "adopted";
                return result;
            }

            archivePath = Path.GetFullPath(archivePath);
            QuestGameArchiveDelivery.ValidateDestination(archivePath);
            foreach (QuestGameContentFile file in manifest.files)
                if (string.Equals(archivePath, Target(root, file.path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The staged archive aliases its content destination.");
            if (string.Equals(archivePath, InstallationReceiptPath(root), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The staged archive aliases its installation receipt.");

            var tracker = new DeliveryProgress(progress, manifest.files.Length);
            QuestGameArchiveDelivery.VerifiedArchive archive = null;
            if (File.Exists(archivePath))
            {
                long size = new FileInfo(archivePath).Length;
                QuestContentHash.FileIdentity before = QuestContentHash.Identity(archivePath);
                if (size >= 22 && size <= QuestGameArchiveDelivery.MaximumArchiveBytes(manifest)
                    && Hash(archivePath) == manifest.archiveSha256)
                {
                    archive = new QuestGameArchiveDelivery.VerifiedArchive(manifest, archivePath, before);
                    result.ReusedArchive = true;
                }
            }
            if (archive == null)
            {
                long archiveBytes = QuestGameArchiveDelivery.SourceLength(manifest, sourcePath, sourceIsApk);
                tracker.Plan(checked(archiveBytes + neededBytes));
                archive = QuestGameArchiveDelivery.StageVerified(manifest, sourcePath, archivePath, sourceIsApk, tracker.Stream(0));
                result.CopiedArchive = true; result.CopiedBytes = archive.Length;
            }
            else tracker.Plan(neededBytes);
            ExtractDelivered(manifest, archive, root, needed, verified, tracker, result);
            // The completion callback may cancel or change a file. Only publish
            // installation state after the operation and its final metadata check.
            tracker.Complete();
            CheckVerified(manifest, root, verified); archive.Check(manifest);
            PublishInstallationReceipt(manifest, root, key, verified);
            result.InstallationReceiptPublished = true;
            result.InstallationState = receipt == null ? "installed" : "repaired";
            return result;
        }

        internal static string InstallationReceiptPath(string root) => Target(root, InstallationReceiptName);

        static void ValidateInstallation(QuestGameContentManifest manifest, string root, string expectedArchive)
        {
            Validate(manifest, manifest == null ? null : manifest.inputKey, expectedArchive);
            if (manifest.files.Length > MaximumReceiptFiles) throw new InvalidDataException("Installation manifest has too many files.");
            foreach (QuestGameContentFile file in manifest.files)
                if (string.Equals(file.path, InstallationReceiptName, StringComparison.OrdinalIgnoreCase)
                    || ReceiptEncoding.GetByteCount(file.path) > MaximumReceiptPathBytes)
                    throw new InvalidDataException("Installation manifest aliases its receipt or has an excessive path.");
            InstallationReceiptPath(root);
        }

        // The bank is independent of Git/input/build IDs and ZIP timestamps or
        // compression. Identical required bytes remain installed across mod updates.
        static string InstallationContentKey(QuestGameContentManifest manifest)
        {
            var files = new List<QuestGameContentFile>(manifest.files);
            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.path, right.path));
            using (var bytes = new MemoryStream())
            {
                using (var writer = new BinaryWriter(bytes, ReceiptEncoding, true))
                {
                    writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, manifest.archive);
                    writer.Write(files.Count);
                    foreach (QuestGameContentFile file in files)
                    { WriteReceiptString(writer, file.path); writer.Write(file.size); WriteReceiptString(writer, file.sha256); }
                }
                return SmallDigest(bytes.ToArray());
            }
        }

        sealed class InstallationFile
        {
            internal string Hash;
            internal long Size;
            internal QuestContentHash.FileIdentity Identity;
        }
        sealed class InstallationReceipt
        {
            internal string ContentKey;
            internal readonly Dictionary<string, InstallationFile> Files = new Dictionary<string, InstallationFile>(StringComparer.Ordinal);
        }

        static bool ReceiptFileMatches(InstallationReceipt receipt, QuestGameContentFile file, string target)
        {
            InstallationFile saved;
            if (receipt == null || !receipt.Files.TryGetValue(file.path, out saved) || saved.Size != file.size || saved.Hash != file.sha256
                || !File.Exists(target) || new FileInfo(target).Length != file.size) return false;
            return saved.Identity.Equals(QuestContentHash.Identity(target));
        }

        static InstallationReceipt ReadInstallationReceipt(string root)
        {
            string path = InstallationReceiptPath(root);
            if (!File.Exists(path)) return null;
            try
            {
                long size = new FileInfo(path).Length;
                if (size < 48 || size > MaximumReceiptBytes) return null;
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length != size) return null;
                int payload = bytes.Length - 32;
                using (var sha = SHA256.Create())
                {
                    byte[] checksum = sha.ComputeHash(bytes, 0, payload);
                    for (int i = 0; i < checksum.Length; ++i) if (checksum[i] != bytes[payload + i]) return null;
                }
                using (var stream = new MemoryStream(bytes, 0, payload, false))
                using (var reader = new BinaryReader(stream, ReceiptEncoding))
                {
                    if (reader.ReadInt32() != 0x49514847 || reader.ReadInt32() != InstallationReceiptSchema) return null;
                    var receipt = new InstallationReceipt { ContentKey = ReadReceiptString(reader, 64) };
                    if (!HashValid(receipt.ContentKey)) return null;
                    int count = reader.ReadInt32();
                    if (count <= 0 || count > MaximumReceiptFiles || count > payload / 80) return null;
                    for (int i = 0; i < count; ++i)
                    {
                        string relative = ReadReceiptString(reader, MaximumReceiptPathBytes);
                        var file = new InstallationFile { Hash = ReadReceiptString(reader, 64), Size = reader.ReadInt64() };
                        file.Identity = new QuestContentHash.FileIdentity
                        {
                            Device = reader.ReadUInt64(), Inode = reader.ReadUInt64(), Size = reader.ReadInt64(),
                            ModifiedSeconds = reader.ReadInt64(), ModifiedNanoseconds = reader.ReadInt64(),
                            ChangedSeconds = reader.ReadInt64(), ChangedNanoseconds = reader.ReadInt64()
                        };
                        if (!SafePath(relative) || !HashValid(file.Hash) || file.Size < 0 || file.Identity.Size != file.Size || receipt.Files.ContainsKey(relative)) return null;
                        receipt.Files.Add(relative, file);
                    }
                    return stream.Position == payload ? receipt : null;
                }
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (DecoderFallbackException) { return null; }
        }

        static void PublishInstallationReceipt(QuestGameContentManifest manifest, string root, string key,
            Dictionary<string, QuestContentHash.FileIdentity> verified)
        {
            Directory.CreateDirectory(root);
            string path = InstallationReceiptPath(root), temporary = path + ".quest-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes;
                using (var memory = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(memory, ReceiptEncoding, true))
                    {
                        writer.Write(0x49514847); writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, key); writer.Write(manifest.files.Length);
                        foreach (QuestGameContentFile file in manifest.files)
                        {
                            QuestContentHash.FileIdentity identity = verified[file.path];
                            WriteReceiptString(writer, file.path); WriteReceiptString(writer, file.sha256); writer.Write(file.size);
                            writer.Write(identity.Device); writer.Write(identity.Inode); writer.Write(identity.Size);
                            writer.Write(identity.ModifiedSeconds); writer.Write(identity.ModifiedNanoseconds);
                            writer.Write(identity.ChangedSeconds); writer.Write(identity.ChangedNanoseconds);
                        }
                    }
                    if (memory.Length > MaximumReceiptBytes - 32) throw new InvalidDataException("Installation receipt is excessive.");
                    byte[] payload = memory.ToArray();
                    using (var sha = SHA256.Create()) { byte[] checksum = sha.ComputeHash(payload); memory.Write(checksum, 0, checksum.Length); }
                    bytes = memory.ToArray();
                }
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
                CheckVerified(manifest, root, verified); InstallationReceiptPath(root);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        static string SmallDigest(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static void WriteReceiptString(BinaryWriter writer, string value)
        {
            byte[] bytes = ReceiptEncoding.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
        }
        static string ReadReceiptString(BinaryReader reader, int maximum)
        {
            int size = reader.ReadInt32();
            if (size < 0 || size > maximum || size > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException("Installation receipt string is invalid.");
            byte[] bytes = reader.ReadBytes(size);
            if (bytes.Length != size) throw new EndOfStreamException();
            return ReceiptEncoding.GetString(bytes);
        }
    }
}
#endif
