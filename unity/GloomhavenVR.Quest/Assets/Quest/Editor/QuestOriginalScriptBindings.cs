#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Rebinds owned exported SDK scripts to actual imported package scripts.</summary>
    public static class QuestOriginalScriptBindings
    {
        [Serializable] public sealed class Binding
        {
            public string assemblyName, fullName, oldGuid;
            public long oldFileId;
        }
        [Serializable] public sealed class Input
        {
            public int schema;
            public Binding[] bindings;
            public string[] assetPaths, disabledPluginGuids;
        }
        [Serializable] public sealed class Replacement
        {
            public string assemblyName, fullName, oldGuid, newGuid;
            public long oldFileId, newFileId;
        }
        [Serializable] public sealed class ChangedAsset
        {
            public string path, beforeSha256, afterSha256, nonScriptSha256;
            public int replacementCount;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1;
            public Replacement[] replacements;
            public ChangedAsset[] assets;
            public bool callbacksAndOtherSerializedBytesPreserved;
        }
        private static readonly Regex Pointer = new Regex(
            @"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}",
            RegexOptions.Compiled);

        public static void RemapAndValidate()
        {
#if GHVR_QUEST_GAME
            const string inputPath = "Assets/QuestOriginalCampaign/script-bindings.json";
#else
            const string inputPath = "Assets/QuestOriginalStartup/script-bindings.json";
#endif
            if (!File.Exists(inputPath)) throw new InvalidOperationException("Original startup script-binding evidence is missing.");
            var input = JsonUtility.FromJson<Input>(File.ReadAllText(inputPath));
            if (input == null || input.schema != 1 || input.bindings == null || input.assetPaths == null || input.disabledPluginGuids == null)
                throw new InvalidOperationException("Invalid original script-binding evidence.");
            var scripts = new Dictionary<string, List<MonoScript>>(StringComparer.Ordinal);
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                var type = script.GetClass();
                if (type == null) continue;
                var key = type.Assembly.GetName().Name + "|" + type.FullName;
                List<MonoScript> bucket;
                if (!scripts.TryGetValue(key, out bucket)) scripts.Add(key, bucket = new List<MonoScript>());
                if (!bucket.Contains(script)) bucket.Add(script);
            }
            var replacements = new Dictionary<string, Replacement>(StringComparer.Ordinal);
            foreach (var binding in input.bindings)
            {
                List<MonoScript> matches;
                if (!scripts.TryGetValue(binding.assemblyName + "|" + binding.fullName, out matches))
                    throw new InvalidOperationException("Package script is unavailable: " + binding.assemblyName + ":" + binding.fullName);
                matches = matches.Where(s => AssetDatabase.GetAssetPath(s).StartsWith("Packages/", StringComparison.Ordinal)).ToList();
                if (matches.Count != 1) throw new InvalidOperationException("Package script identity is ambiguous: " + binding.fullName);
                string guid; long fileId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(matches[0], out guid, out fileId) || fileId == 0 || guid == binding.oldGuid)
                    throw new InvalidOperationException("Package script has no independent imported identity: " + binding.fullName);
                var replacement = new Replacement { assemblyName = binding.assemblyName, fullName = binding.fullName,
                    oldGuid = binding.oldGuid, oldFileId = binding.oldFileId, newGuid = guid, newFileId = fileId };
                var oldKey = binding.oldGuid + ":" + binding.oldFileId.ToString(CultureInfo.InvariantCulture);
                if (replacements.ContainsKey(oldKey)) throw new InvalidOperationException("Duplicate source script identity: " + oldKey);
                replacements.Add(oldKey, replacement);
            }
            var disabled = new HashSet<string>(input.disabledPluginGuids, StringComparer.Ordinal);
            var plans = new List<KeyValuePair<string, string>>();
            var changed = new List<ChangedAsset>();
            var projectRoot = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            foreach (var path in input.assetPaths)
            {
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !Path.GetFullPath(path).StartsWith(projectRoot, StringComparison.Ordinal) || !File.Exists(path))
                    throw new InvalidOperationException("Invalid original binding asset path: " + path);
                var before = File.ReadAllText(path);
                var count = 0;
                var after = Pointer.Replace(before, match =>
                {
                    if (!disabled.Contains(match.Groups[2].Value)) return match.Value;
                    Replacement replacement;
                    var key = match.Groups[2].Value + ":" + match.Groups[1].Value;
                    if (!replacements.TryGetValue(key, out replacement))
                        throw new InvalidOperationException("Unmapped disabled package script: " + path + " " + key);
                    count++;
                    return "m_Script: {fileID: " + replacement.newFileId.ToString(CultureInfo.InvariantCulture) +
                        ", guid: " + replacement.newGuid + ", type: 3}";
                });
                if (Pointer.Matches(after).Cast<Match>().Any(m => disabled.Contains(m.Groups[2].Value)))
                    throw new InvalidOperationException("Disabled SDK script remained after remapping: " + path);
                // Normalize only script pointers: callbacks, fields, asset GUIDs and object IDs must remain byte-for-byte equal.
                var normalizedBefore = Pointer.Replace(before, "m_Script: <identity>");
                var normalizedAfter = Pointer.Replace(after, "m_Script: <identity>");
                if (normalizedBefore != normalizedAfter) throw new InvalidOperationException("Non-script serialization changed: " + path);
                if (count == 0) continue;
                plans.Add(new KeyValuePair<string, string>(path, after));
                changed.Add(new ChangedAsset { path = path, beforeSha256 = Hash(before), afterSha256 = Hash(after),
                    nonScriptSha256 = Hash(normalizedBefore), replacementCount = count });
            }
            // Validate the entire transaction before touching any original-derived file.
            foreach (var plan in plans) File.WriteAllText(plan.Key, plan.Value, new UTF8Encoding(false));
            Directory.CreateDirectory("QuestStartupEvidence");
            File.WriteAllText("QuestStartupEvidence/script-remap.json", JsonUtility.ToJson(new Receipt {
                replacements = replacements.Values.OrderBy(r => r.fullName).ToArray(), assets = changed.ToArray(),
                callbacksAndOtherSerializedBytesPreserved = true }, true));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Quest startup] Rebound " + changed.Sum(a => a.replacementCount) + " SDK script pointers; original callbacks retained.");
        }

        private static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
