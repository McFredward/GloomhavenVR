#if GHVR_QUEST_GAME && UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Keep the complete owned bank outside Android's ZIP32 APK limit.</summary>
    public sealed class QuestCampaignContentBuild : IDisposable
    {
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
        readonly string source, temporary;
        public QuestCampaignContentBuild(string apk, string inputKey)
        {
            const string manifestPath = "Assets/Quest/Resources/quest-startup-content.json";
            var manifest = JsonUtility.FromJson<QuestGameContentManifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.inputKey != inputKey || manifest.archive != "quest-startup-content.zip")
                throw new InvalidDataException("Campaign content bank does not match this APK.");
            source = Path.Combine("Assets/StreamingAssets", manifest.archive);
            if (!File.Exists(source)) throw new FileNotFoundException("Complete Campaign bank is unavailable.", source);
            string destination = Path.Combine(Path.GetDirectoryName(apk), "GloomhavenVR-Quest-content.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
            manifest.externalDelivery = true;
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true) + "\n");
            Directory.CreateDirectory("Assets/StreamingAssets/Quest");
            File.WriteAllText("Assets/StreamingAssets/Quest/content-delivery.json", JsonUtility.ToJson(new Delivery {
                inputKey = inputKey, files = new[] { new FileRecord {
                    file = Path.GetFileName(destination), archive = manifest.archive,
                    sha256 = manifest.archiveSha256, size = new FileInfo(destination).Length
                } }
            }, true) + "\n");
            // Restore in Dispose even when the native player fails to build.
            // Only the generated project is changed; another build can repack it.
            temporary = Path.Combine("QuestCampaignEvidence", "excluded-payload", manifest.archive);
            Directory.CreateDirectory(Path.GetDirectoryName(temporary));
            if (File.Exists(temporary)) throw new IOException("Interrupted Campaign payload exclusion exists; inspect it before rebuilding.");
            File.Move(source, temporary);
            try { AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); }
            catch { File.Move(temporary, source); throw; }
        }
        public void Dispose()
        {
            if (!File.Exists(temporary)) return;
            if (File.Exists(source)) throw new IOException("Campaign payload destination changed during native build.");
            File.Move(temporary, source);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
#endif
