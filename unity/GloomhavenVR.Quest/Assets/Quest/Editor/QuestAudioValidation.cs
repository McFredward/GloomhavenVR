#nullable disable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Verify imported native samples before catalogs or player builds.</summary>
    public static class QuestAudioValidation
    {
        [Serializable] sealed class Clip
        {
            public string assetPath, name;
            public int channels, frequency, samples, loadType;
        }
        [Serializable] sealed class Manifest { public int schema; public Clip[] clips; }
        [Serializable] sealed class Result
        {
            public int schema = 1, clipCount, quadClipCount;
            public string unityVersion, target;
            public bool importedOriginalSampleMetadataVerified;
        }

        public static void Validate()
        {
            const string path = "Assets/Resources/QuestOriginalAudio.json";
            if (!File.Exists(path)) throw new InvalidOperationException("Original audio boundary evidence is missing.");
            var evidence = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (evidence == null || evidence.schema != 1 || evidence.clips == null || evidence.clips.Length == 0
                || evidence.clips.Length > 65536)
                throw new InvalidOperationException("Original audio boundary evidence has an invalid shape.");
            var result = new Result { unityVersion = Application.unityVersion, target = EditorUserBuildSettings.activeBuildTarget.ToString() };
            foreach (var original in evidence.clips)
            {
                if (original == null || string.IsNullOrEmpty(original.assetPath)
                    || !original.assetPath.StartsWith("Assets/AudioClip/", StringComparison.Ordinal)
                    || !original.assetPath.EndsWith(".wav", StringComparison.Ordinal)
                    || original.assetPath.Contains("..") || original.assetPath.Contains("\\"))
                    throw new InvalidOperationException("Original audio boundary contains an invalid asset path.");
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(original.assetPath);
                if (clip == null || clip.channels != original.channels || clip.frequency != original.frequency
                    || clip.samples != original.samples || (int)clip.loadType != original.loadType)
                    throw new InvalidOperationException("Imported original AudioClip sample metadata changed: " + original.assetPath
                        + " expected=" + original.channels + "ch/" + original.frequency + "Hz/" + original.samples
                        + " samples/load=" + original.loadType + " actual=" + (clip == null ? "missing" :
                        clip.channels + "ch/" + clip.frequency + "Hz/" + clip.samples + " samples/load=" + (int)clip.loadType));
                result.clipCount++;
                if (clip.channels == 4) result.quadClipCount++;
            }
            result.importedOriginalSampleMetadataVerified = true;
            Directory.CreateDirectory("QuestStartupEvidence");
            File.WriteAllText("QuestStartupEvidence/audio-import-validation.json", JsonUtility.ToJson(result, true) + "\n");
            Debug.Log("[Quest build] Original audio import verified clips=" + result.clipCount + " quad=" + result.quadClipCount
                + "; original channels, frequency, complete sample frames and load types retained.");
        }
    }
}
