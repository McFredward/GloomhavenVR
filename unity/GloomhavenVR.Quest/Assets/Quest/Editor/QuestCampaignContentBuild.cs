#if GHVR_QUEST_GAME && UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Keep the complete owned bank outside Android's ZIP32 APK limit.</summary>
    public sealed class QuestCampaignContentBuild : IDisposable
    {
        internal const string Scope = "quest-campaign-player-content-exclusion";
        internal const string JournalPath = "QuestCampaignEvidence/excluded-payload/journal.json";
        internal const string ArchivePath = "Assets/StreamingAssets/quest-startup-content.zip";
        internal const string ArchiveTemporary = "QuestCampaignEvidence/excluded-payload/quest-startup-content.zip";
        internal const string NativePath = "Library/com.unity.addressables/aa/Android";
        internal const string NativeTemporary = "QuestCampaignEvidence/excluded-payload/native-addressables-Android";
        internal const string NativeLink = NativePath + "/AddressablesLink/link.xml";
        internal const string StableLink = "Assets/Quest/CampaignLink/link.xml";
        const string ManifestPath = "Assets/Quest/Resources/quest-startup-content.json";
        const string DeliveryPath = "Assets/StreamingAssets/Quest/content-delivery.json";
        internal const string InstallationPath = "Assets/StreamingAssets/Quest/installation-manifest.json";

        [Serializable] internal sealed class Journal
        {
            public int schema = 1;
            public string scope = Scope, inputKey, state;
            public MoveRecord[] moves;
            public LinkRecord nativeLink;
        }
        [Serializable] internal sealed class MoveRecord
        {
            public string kind, source, temporary, sha256;
            public long size;
        }
        [Serializable] internal sealed class LinkRecord
        {
            public string source = NativeLink, projectPath = StableLink, sha256, previousSha256;
        }
        [Serializable] sealed class Delivery
        {
            public int schema = 1;
            public string inputKey, package = "dev.gloomhavenvr.quest";
            public FileRecord[] files;
        }
        [Serializable] sealed class FileRecord
        {
            public string file, archive, sha256;
            public long size;
        }
        [Serializable] sealed class InstallationManifest
        {
            public int schema = 1;
            public string inputKey;
            public QuestGameContentManifest game, mod;
        }
        readonly string root;
        readonly Journal journal;
        bool disposed;

        public QuestCampaignContentBuild(string apk, string inputKey)
        {
            RequireKey(inputKey);
            root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var ownedPrevious = ReadJournal(root, null);
            if (ownedPrevious != null) RestoreExcludedContent(root, ownedPrevious.inputKey);
            string manifestFile = Confined(root, ManifestPath);
            byte[] originalManifest = File.ReadAllBytes(manifestFile);
            var manifest = JsonUtility.FromJson<QuestGameContentManifest>(System.Text.Encoding.UTF8.GetString(originalManifest));
            if (manifest == null || manifest.inputKey != inputKey || manifest.archive != "quest-startup-content.zip" || !IsHash(manifest.archiveSha256))
                throw new InvalidDataException("Campaign content bank does not match this APK.");
            string source = Confined(root, ArchivePath);
            if (!File.Exists(source)) throw new FileNotFoundException("Complete Campaign bank is unavailable.", source);
            string native = Confined(root, NativePath);
            if (Path.GetFullPath(Addressables.BuildPath) != native || !Directory.Exists(native))
                throw new InvalidDataException("Original Android Addressables BuildPath is unavailable or changed.");
            GuardTree(native);
            string linkSource = Confined(root, NativeLink), link = Confined(root, StableLink);
            if (!File.Exists(linkSource)) throw new FileNotFoundException("Native Campaign linker preservation is unavailable.", linkSource);
            string linkHash = Hash(linkSource);
            bool createdLink = !File.Exists(link);
            byte[] originalLink = createdLink ? null : File.ReadAllBytes(link);
            string previousLinkHash = createdLink ? null : Hash(link);
            if (!createdLink)
            {
                if (ownedPrevious == null || !OwnedLink(ownedPrevious.nativeLink, previousLinkHash))
                    throw new IOException("Campaign linker destination is not an exact previously owned file.");
            }
            else if (File.Exists(link + ".meta"))
                throw new IOException("Campaign linker destination has an unowned existing meta.");

            var moves = new System.Collections.Generic.List<MoveRecord> {
                new MoveRecord { kind = "file", source = ArchivePath, temporary = ArchiveTemporary,
                    sha256 = manifest.archiveSha256, size = new FileInfo(source).Length }
            };
            if (File.Exists(source + ".meta"))
                moves.Add(new MoveRecord { kind = "file", source = ArchivePath + ".meta", temporary = ArchiveTemporary + ".meta",
                    sha256 = Hash(source + ".meta"), size = new FileInfo(source + ".meta").Length });
            moves.Add(new MoveRecord { kind = "directory", source = NativePath, temporary = NativeTemporary, sha256 = linkHash });
            journal = new Journal { inputKey = inputKey, state = "planned", moves = moves.ToArray(),
                nativeLink = new LinkRecord { sha256 = linkHash, previousSha256 = previousLinkHash } };
            ValidateJournal(root, journal, inputKey);
            foreach (var move in journal.moves)
                if (Exists(Confined(root, move.temporary))) throw new IOException("Interrupted or unowned Campaign exclusion destination exists.");
            string delivery = Confined(root, DeliveryPath);
            byte[] originalDelivery = File.Exists(delivery) ? File.ReadAllBytes(delivery) : null;
            string installation = Confined(root, InstallationPath);
            byte[] originalInstallation = File.Exists(installation) ? File.ReadAllBytes(installation) : null;
            // Addressables1.19.19 GetStreamingAssetPaths ALWAYS adds BuildPath,
            // even with DoNotBuildWithPlayer. Keep its exact linker declarations
            // in Assets before excluding both generated content representations.
            WriteJournal(root, journal); // durable before all payload/link moves
            try
            {
                string destination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(apk)), "GloomhavenVR-Quest-content.zip");
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                CopyVerified(source, destination, manifest.archiveSha256);
                manifest.externalDelivery = true;
                WriteAtomic(manifestFile, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(manifest, true) + "\n"));
                // The PC installer needs the final native per-file inventory,
                // including current mod banks, before starting the headset app.
                // Publishing it inside the signed APK moves B624's six-minute
                // archive hash/extraction out of the game's startup path.
                var modManifest = JsonUtility.FromJson<QuestGameContentManifest>(File.ReadAllText(
                    Confined(root, "Assets/Quest/Resources/quest-mod-content.json")));
                if (modManifest == null || modManifest.schema != 1 || modManifest.inputKey != inputKey
                    || modManifest.archive != "quest-mod-content.zip" || !IsHash(modManifest.archiveSha256)
                    || modManifest.files == null || modManifest.files.Length == 0)
                    throw new InvalidDataException("PC installation requires this APK's complete current mod inventory.");
                WriteAtomic(installation, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new InstallationManifest {
                    inputKey = inputKey, game = manifest, mod = modManifest
                }, true) + "\n"));
                WriteAtomic(delivery, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(new Delivery {
                    inputKey = inputKey, files = new[] { new FileRecord {
                        file = Path.GetFileName(destination), archive = manifest.archive,
                        sha256 = manifest.archiveSha256, size = new FileInfo(destination).Length
                    } }
                }, true) + "\n"));
                if (createdLink || previousLinkHash != linkHash) WriteAtomic(link, File.ReadAllBytes(linkSource));
                AssetDatabase.ImportAsset(StableLink, ImportAssetOptions.ForceSynchronousImport);
                foreach (var move in journal.moves)
                {
                    string from = Confined(root, move.source), to = Confined(root, move.temporary);
                    if (Exists(to)) throw new IOException("Campaign exclusion destination changed during native build preparation.");
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    if (move.kind == "directory") Directory.Move(from, to); else File.Move(from, to);
                }
                journal.state = "excluded";
                WriteJournal(root, journal);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                if (Directory.Exists(native) || File.Exists(source))
                    throw new IOException("Native Campaign content became streamable again during Player preparation.");
            }
            catch (Exception original)
            {
                // A failed constructor never invokes Dispose. Keep the initiating
                // error, and retain the durable journal on any recovery conflict.
                try
                {
                    RestoreExcludedContent(root, inputKey);
                    WriteAtomic(manifestFile, originalManifest);
                    if (originalDelivery == null) { if (File.Exists(delivery)) File.Delete(delivery); }
                    else WriteAtomic(delivery, originalDelivery);
                    if (originalInstallation == null) { if (File.Exists(installation)) File.Delete(installation); }
                    else WriteAtomic(installation, originalInstallation);
                    if (createdLink && File.Exists(link) && Hash(link) == linkHash)
                    {
                        File.Delete(link);
                        if (File.Exists(link + ".meta")) File.Delete(link + ".meta");
                    }
                    else if (!createdLink && File.Exists(link) && Hash(link) == linkHash)
                        WriteAtomic(link, originalLink);
                }
                catch (Exception rollback)
                {
                    original.Data["QuestCampaignContentRollbackError"] = rollback.ToString();
                    Debug.LogError("Campaign exclusion rollback failed; preserved journal requires recovery: " + rollback);
                }
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            RestoreExcludedContent(root, journal.inputKey);
            disposed = true;
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Recover exact journaled payloads after a hard process death.</summary>
        public static void RestoreExcludedContent(string projectRoot, string inputKey)
        {
            string project = Path.GetFullPath(projectRoot);
            var previous = ReadJournal(project, inputKey);
            if (previous == null) return;
            if (previous.state == "restored")
            {
                // A completed older build owns no excluded payload. Content may
                // legitimately have been regenerated for a newer mod/profile.
                foreach (var move in previous.moves)
                {
                    if (Exists(Confined(project, move.temporary)))
                        throw new IOException("Restored Campaign journal has an unexpected excluded payload.");
                    string source = Confined(project, move.source);
                    if (Exists(source) && (move.kind == "directory" ? !Directory.Exists(source) : !File.Exists(source)))
                        throw new IOException("Restored Campaign source has an unexpected filesystem type.");
                    if (move.source == NativePath && Directory.Exists(source)) GuardTree(source);
                }
                string existingLink = Confined(project, StableLink);
                if (File.Exists(existingLink) && !OwnedLink(previous.nativeLink, Hash(existingLink)))
                    throw new IOException("Owned Campaign linker identity differs.");
                CleanupPending(project);
                return;
            }
            // Preflight every pair before any move. Both present is a conflict,
            // even when bytes look identical; never overwrite a regenerated source.
            foreach (var move in previous.moves)
            {
                string from = Confined(project, move.source), to = Confined(project, move.temporary);
                bool hasSource = Exists(from), hasTemporary = Exists(to);
                if (hasSource && hasTemporary) throw new IOException("Campaign content recovery cannot overwrite an existing source: " + move.source);
                if (!hasSource && !hasTemporary) throw new IOException("Journaled Campaign payload is missing: " + move.source);
                string existing = hasSource ? from : to;
                if (move.kind == "directory")
                {
                    if (!Directory.Exists(existing)) throw new IOException("Journaled native Addressables payload is not a directory.");
                    GuardTree(existing);
                    string nativeLink = Path.Combine(existing, "AddressablesLink/link.xml");
                    if (!File.Exists(nativeLink) || Hash(nativeLink) != move.sha256)
                        throw new InvalidDataException("Journaled native Addressables link identity differs.");
                }
                else if (!File.Exists(existing) || new FileInfo(existing).Length != move.size ||
                         (move.source.EndsWith(".meta", StringComparison.Ordinal) && Hash(existing) != move.sha256))
                    throw new InvalidDataException("Journaled Campaign file identity differs.");
            }
            string stable = Confined(project, StableLink);
            if (File.Exists(stable) && !OwnedLink(previous.nativeLink, Hash(stable)))
                throw new IOException("Owned Campaign linker file changed during Player build.");
            for (int index = previous.moves.Length - 1; index >= 0; --index)
            {
                var move = previous.moves[index];
                string from = Confined(project, move.source), to = Confined(project, move.temporary);
                if (!Exists(to)) continue;
                if (Exists(from)) throw new IOException("Campaign recovery source appeared after preflight.");
                Directory.CreateDirectory(Path.GetDirectoryName(from));
                if (move.kind == "directory") Directory.Move(to, from); else File.Move(to, from);
            }
            // A hard kill can leave a partial atomic metadata write. Its
            // trusted journal has now restored every source, so remove only
            // these exact owned pending siblings before rewriting the journal.
            CleanupPending(project);
            previous.state = "restored";
            WriteJournal(project, previous);
        }

        static Journal ReadJournal(string project, string inputKey)
        {
            if (inputKey != null) RequireKey(inputKey);
            string path = Confined(project, JournalPath);
            if (!File.Exists(path)) return null;
            var value = JsonUtility.FromJson<Journal>(File.ReadAllText(path));
            ValidateJournal(project, value, inputKey);
            return value;
        }

        static void ValidateJournal(string project, Journal value, string inputKey)
        {
            if (value == null || value.schema != 1 || value.scope != Scope || !IsHash(value.inputKey) || inputKey != null && value.inputKey != inputKey ||
                (value.state != "planned" && value.state != "excluded" && value.state != "restored") ||
                value.moves == null || (value.moves.Length != 2 && value.moves.Length != 3) || value.nativeLink == null ||
                value.nativeLink.source != NativeLink || value.nativeLink.projectPath != StableLink || !IsHash(value.nativeLink.sha256) ||
                !string.IsNullOrEmpty(value.nativeLink.previousSha256) && !IsHash(value.nativeLink.previousSha256))
                throw new InvalidDataException("Campaign exclusion journal does not match this project/input contract.");
            for (int index = 0; index < value.moves.Length; ++index)
            {
                var move = value.moves[index];
                string expectedSource = index == 0 ? ArchivePath : index == value.moves.Length - 1 ? NativePath : ArchivePath + ".meta";
                string expectedTemporary = index == 0 ? ArchiveTemporary : index == value.moves.Length - 1 ? NativeTemporary : ArchiveTemporary + ".meta";
                string kind = index == value.moves.Length - 1 ? "directory" : "file";
                if (move == null || move.source != expectedSource || move.temporary != expectedTemporary || move.kind != kind ||
                    !IsHash(move.sha256) || move.size < 0 || (kind == "directory" && (move.sha256 != value.nativeLink.sha256 || move.size != 0)))
                    throw new InvalidDataException("Campaign exclusion journal contains an unknown payload path/identity.");
                Confined(project, move.source); Confined(project, move.temporary);
            }
            Confined(project, value.nativeLink.source); Confined(project, value.nativeLink.projectPath);
        }

        static string Confined(string project, string relative)
        {
            if (string.IsNullOrEmpty(relative) || Path.IsPathRooted(relative) || relative.IndexOf('\\') >= 0 || relative.IndexOf(':') >= 0)
                throw new InvalidDataException("Campaign exclusion path is not project-relative.");
            string current = project;
            foreach (string component in relative.Split('/'))
            {
                if (component.Length == 0 || component == "." || component == "..") throw new InvalidDataException("Campaign exclusion path escapes its owned root.");
                current = Path.Combine(current, component);
                if (Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Campaign exclusion cannot follow a symlink/junction.");
            }
            return current;
        }

        static void GuardTree(string directory)
        {
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Native Addressables exclusion contains a symlink/junction.");
                if ((attributes & FileAttributes.Directory) != 0) GuardTree(entry);
            }
        }
        static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }
        static bool OwnedLink(LinkRecord link, string hash) { return hash == link.sha256 || hash == link.previousSha256; }
        static bool IsHash(string value) { return value != null && Regex.IsMatch(value, "\\A[0-9a-f]{64}\\z"); }
        static void RequireKey(string inputKey) { if (!IsHash(inputKey)) throw new InvalidDataException("Campaign exclusion requires the exact builder input key."); }
        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        static void CleanupPending(string project)
        {
            foreach (string relative in new[] { JournalPath, ManifestPath, DeliveryPath, StableLink })
            {
                string pending = Confined(project, relative + ".quest-content-pending");
                if (Directory.Exists(pending)) throw new IOException("Campaign metadata pending path is a directory.");
                if (File.Exists(pending)) File.Delete(pending);
            }
        }
        static void WriteJournal(string project, Journal value)
        {
            WriteAtomic(Confined(project, JournalPath), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(value, true) + "\n"));
        }
        static void WriteAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string pending = path + ".quest-content-pending";
            if (File.Exists(pending)) throw new IOException("Interrupted Campaign metadata write exists; inspect it before rebuilding.");
            try
            {
                using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(pending, path, null); else File.Move(pending, path);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        static void CopyVerified(string source, string destination, string expectedHash)
        {
            if (Path.GetFullPath(source) == Path.GetFullPath(destination)) throw new IOException("Campaign delivery cannot replace its original source.");
            string pending = destination + ".quest-content-pending";
            if (File.Exists(pending)) throw new IOException("Interrupted Campaign delivery copy exists.");
            try
            {
                using (var sha = SHA256.Create()) using (var input = File.OpenRead(source))
                using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[1024 * 1024]; int count;
                    var progress = new QuestWizardProgress.Counter("unity-content-delivery", "player", input.Length, "bytes", "Copy the complete Campaign bank for installation");
                    long copied = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, count); sha.TransformBlock(buffer, 0, count, buffer, 0);
                        copied += count;
                        progress.Report(copied, "Copy the complete Campaign bank for installation");
                    }
                    sha.TransformFinalBlock(buffer, 0, 0);
                    if (BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() != expectedHash)
                        throw new InvalidDataException("Complete Campaign bank differs from the actual native content manifest.");
                    output.Flush(true);
                    progress.Complete("Complete Campaign bank copied and validated");
                }
                if (File.Exists(destination)) File.Replace(pending, destination, null); else File.Move(pending, destination);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
    }
}
#endif
