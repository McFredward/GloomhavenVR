#if GHVR_QUEST_STARTUP
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Quest
{
    /// <summary>One startup recovery pass; original SaveData owns all subsequent gameplay IO.</summary>
    internal static class QuestGameSaveStorage
    {
        private const string Format = "gloomhaven-native-dat-snapshot-v1";
        private const string Journal = ".quest-save-transfer.json";
        private const string Marker = ".quest-save-snapshot.json";
        private const string BackupSuffix = ".ghvr-save-backup";
        private const long MaximumBytes = 128L * 1024L * 1024L;
        private static readonly string[] Roots = { "GloomSaves", "GloomSavesOpenBeta", "GloomSavesClosedBeta", "GloomSavesDev", "GloomSavesLocalDev", "GloomSavesInEditor" };
        private static bool initialized;

        [Serializable] private sealed class Transfer
        {
            public int schema;
            public string format;
            public string transferId;
            public string saveRoot;
            public string staging;
            public string backup;
            public SaveFile[] files;
        }
        [Serializable] private sealed class SaveFile
        {
            public string path;
            public long bytes;
            public string sha256;
        }

        internal static void Initialize()
        {
            if (!QuestStandalonePlatform.Enabled || initialized) return;
            Initialize(QuestGame.Compatibility.Paths.get_persistentDataPath());
        }

        internal static void Initialize(string persistentDataPath)
        {
            if (!QuestStandalonePlatform.Enabled || initialized) return;
            string persistent = Path.GetFullPath(persistentDataPath);
            RequireNoLinks(persistent);
            RecoverTransfer(persistent);
            int recovered = 0, unusable = 0;
            foreach (string name in Roots)
            {
                string root = Path.Combine(persistent, name);
                if (!Directory.Exists(root)) continue;
                RequireNoLinks(root);
                // Names/lengths only on ordinary launches. Never hash all saves,
                // deserialize player data here, or touch a healthy existing file.
                foreach (string path in Backups(root))
                {
                    string destination = path.Substring(0, path.Length - BackupSuffix.Length);
                    if (File.Exists(destination) && new FileInfo(destination).Length != 0) continue;
                    try
                    {
                        RequireNoLinks(path); RequireNoLinks(destination);
                        long bytes = new FileInfo(path).Length;
                        if (bytes <= 0 || bytes > MaximumBytes) { unusable++; continue; }
                        byte[] data = File.ReadAllBytes(path);
                        if (Array.TrueForAll(data, value => value == 0)) { unusable++; continue; }
                        RestoreFile(destination, data); recovered++;
                    }
                    catch (IOException) { unusable++; }
                    catch (UnauthorizedAccessException) { unusable++; }
                }
            }
            initialized = true;
            QuestGameSaveLifecycle.Install();
            Debug.Log("[Quest saves] Local native storage ready; recoveredFiles=" + recovered + "; unavailableBackups=" + unusable + "; cloudServices=false. Existing healthy saves retained.");
            if (QuestStandalonePlatform.DebugLogging) QuestGameSaveValidation.VerifyOriginalRootAndOwner();
        }

        private static IEnumerable<string> Backups(string root)
        {
            var directories = new Stack<string>(); directories.Push(root); int count = 0;
            while (directories.Count != 0)
            {
                string current = directories.Pop(); RequireNoLinks(current);
                foreach (string path in Directory.GetFiles(current, "*" + BackupSuffix, SearchOption.TopDirectoryOnly))
                {
                    if (++count > 20000) throw new IOException("Quest native backup discovery exceeded its startup limit.");
                    yield return path;
                }
                foreach (string child in Directory.GetDirectories(current))
                {
                    RequireNoLinks(child); directories.Push(child);
                }
            }
        }

        private static void RecoverTransfer(string persistent)
        {
            string journal = Path.Combine(persistent, Journal);
            if (!File.Exists(journal)) return;
            RequireNoLinks(journal);
            Transfer transfer = Read(journal);
            if (transfer.schema != 1 || transfer.format != Format || Array.IndexOf(Roots, transfer.saveRoot) < 0
                || !Regex.IsMatch(transfer.transferId ?? "", @"^\d{8}T\d{6}Z-[0-9a-f]{32}$")
                || !Regex.IsMatch(transfer.staging ?? "", @"^\.quest-save-import-[0-9a-f]{32}$")
                || transfer.backup != "QuestSaveBackups/" + transfer.transferId + "/" + transfer.saveRoot)
                throw new InvalidDataException("Quest save transfer journal has an unknown or escaping layout; native saves were not changed.");
            string root = Path.Combine(persistent, transfer.saveRoot);
            string staging = Path.Combine(persistent, transfer.staging);
            string backup = Path.Combine(persistent, transfer.backup.Replace('/', Path.DirectorySeparatorChar));
            RequireNoLinks(root); RequireNoLinks(staging); RequireNoLinks(backup);
            if (Directory.Exists(root))
            {
                // Either the new complete root is already live, or the original
                // root was never moved. Preserve it; never overlay global indexes.
                File.Delete(journal);
                Debug.LogWarning("[Quest saves] Interrupted snapshot transfer retained the existing native save root; staged/backup bytes remain available.");
                return;
            }
            if (Directory.Exists(backup))
            {
                Directory.Move(backup, root);
                File.Delete(journal);
                Debug.LogWarning("[Quest saves] Interrupted snapshot transfer rolled back the previous complete native save root.");
                return;
            }
            if (!Directory.Exists(staging))
                throw new IOException("Interrupted Quest save transfer has neither a native root, complete backup nor verified staged snapshot.");
            VerifyStaging(staging, transfer);
            Directory.Move(staging, root); File.Delete(journal);
            Debug.LogWarning("[Quest saves] Interrupted first snapshot transfer completed from its hash-verified staged native bytes.");
        }

        private static Transfer Read(string path)
        {
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Quest save transfer metadata is too large.");
            Transfer result = JsonUtility.FromJson<Transfer>(File.ReadAllText(path));
            if (result == null) throw new InvalidDataException("Quest save transfer metadata is invalid.");
            return result;
        }

        private static void VerifyStaging(string staging, Transfer journal)
        {
            string marker = Path.Combine(staging, Marker); RequireNoLinks(marker);
            Transfer transfer = Read(marker);
            if (transfer.schema != 1 || transfer.format != Format || transfer.transferId != journal.transferId
                || transfer.saveRoot != journal.saveRoot || transfer.files == null || transfer.files.Length > 20000)
                throw new InvalidDataException("Interrupted Quest save snapshot marker is inconsistent.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            foreach (SaveFile file in transfer.files)
            {
                if (file == null || string.IsNullOrEmpty(file.path) || file.path.IndexOf('\\') >= 0
                    || file.path.IndexOf(':') >= 0 || file.path.StartsWith("/", StringComparison.Ordinal)
                    || Array.Exists(file.path.Split('/'), part => part.Length == 0 || part == "." || part == "..")
                    || !names.Add(file.path) || file.bytes < 0 || file.bytes > MaximumBytes
                    || !Regex.IsMatch(file.sha256 ?? "", "^[0-9a-f]{64}$") || (total += file.bytes) > 1024L * 1024L * 1024L)
                    throw new InvalidDataException("Interrupted Quest save snapshot contains invalid file metadata.");
                string path = Path.Combine(staging, file.path.Replace('/', Path.DirectorySeparatorChar)); RequireNoLinks(path);
                if (!File.Exists(path) || new FileInfo(path).Length != file.bytes)
                    throw new InvalidDataException("Interrupted Quest save snapshot is incomplete.");
                using (var stream = File.OpenRead(path))
                using (var hasher = SHA256.Create())
                    if (BitConverter.ToString(hasher.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != file.sha256)
                        throw new InvalidDataException("Interrupted Quest save snapshot payload hash failed.");
            }
            if (!names.Contains("GlobalData.dat") || !names.Contains("GloomSaven.dat"))
                throw new InvalidDataException("Interrupted Quest save snapshot lacks the original global/root records.");
        }

        private static void RestoreFile(string path, byte[] bytes)
        {
            string temporary = path + ".ghvr-save-recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void RequireNoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Quest save recovery must not follow filesystem links.");
        }
    }
}
#endif
