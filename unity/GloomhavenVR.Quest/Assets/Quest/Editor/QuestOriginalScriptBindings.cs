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
            public string assemblyName, fullName, oldGuid, newGuid, newAssetPath;
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
            var bindingProgress = new QuestWizardProgress.Counter("unity-script-bindings", "unity-validation", input.bindings.Length, "bindings", "Map original package script identities");
            int bindingsDone = 0;
            foreach (var binding in input.bindings)
            {
                bindingProgress.Report(bindingsDone, binding.fullName);
                List<MonoScript> matches;
                if (!scripts.TryGetValue(binding.assemblyName + "|" + binding.fullName, out matches))
                    throw new InvalidOperationException("Package script is unavailable: " + binding.assemblyName + ":" + binding.fullName);
                matches = matches.Where(s => AssetDatabase.GetAssetPath(s).StartsWith("Packages/", StringComparison.Ordinal)).ToList();
                if (matches.Count != 1) throw new InvalidOperationException("Package script identity is ambiguous: " + binding.fullName);
                string guid; long fileId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(matches[0], out guid, out fileId) || fileId == 0 || guid == binding.oldGuid)
                    throw new InvalidOperationException("Package script has no independent imported identity: " + binding.fullName);
                var replacement = new Replacement { assemblyName = binding.assemblyName, fullName = binding.fullName,
                    oldGuid = binding.oldGuid, oldFileId = binding.oldFileId, newGuid = guid, newFileId = fileId,
                    newAssetPath = AssetDatabase.GetAssetPath(matches[0]) };
                var oldKey = binding.oldGuid + ":" + binding.oldFileId.ToString(CultureInfo.InvariantCulture);
                if (replacements.ContainsKey(oldKey)) throw new InvalidOperationException("Duplicate source script identity: " + oldKey);
                replacements.Add(oldKey, replacement);
                bindingProgress.Report(++bindingsDone, binding.fullName);
            }
            bindingProgress.Complete("Original package script identities mapped");
            var disabled = new HashSet<string>(input.disabledPluginGuids, StringComparer.Ordinal);
            var plans = new List<KeyValuePair<string, string>>();
            // Preserve the first original-to-package transition across reruns.
            // Publish its complete write set before asset mutation so a kill
            // during the loop leaves independently verifiable original hashes.
            var changed = ReadRetainedTransitions(input, replacements);
            var projectRoot = Path.GetFullPath(".") + Path.DirectorySeparatorChar;
            var assetProgress = new QuestWizardProgress.Counter("unity-script-assets", "unity-validation", input.assetPaths.Length, "files", "Validate original serialized script pointers");
            int assetsDone = 0;
            foreach (var path in input.assetPaths)
            {
                assetProgress.Report(assetsDone, path);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    !Path.GetFullPath(path).StartsWith(projectRoot, StringComparison.Ordinal) || !File.Exists(path))
                    throw new InvalidOperationException("Invalid original binding asset path: " + path);
                var before = File.ReadAllText(path);
                var normalizedBefore = Pointer.Replace(before, "m_Script: <identity>");
                string beforeSha256 = null, nonScriptSha256 = null;
                ChangedAsset retained;
                if (changed.TryGetValue(path, out retained))
                {
                    beforeSha256 = Hash(before);
                    nonScriptSha256 = Hash(normalizedBefore);
                    if ((beforeSha256 != retained.beforeSha256 && beforeSha256 != retained.afterSha256) ||
                        nonScriptSha256 != retained.nonScriptSha256)
                        throw new InvalidOperationException("Retained original script asset changed: " + path);
                }
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
                var normalizedAfter = Pointer.Replace(after, "m_Script: <identity>");
                if (normalizedBefore != normalizedAfter) throw new InvalidOperationException("Non-script serialization changed: " + path);
                assetProgress.Report(++assetsDone, path);
                if (count == 0) continue;
                plans.Add(new KeyValuePair<string, string>(path, after));
                var transition = new ChangedAsset { path = path, beforeSha256 = beforeSha256 ?? Hash(before), afterSha256 = Hash(after),
                    nonScriptSha256 = nonScriptSha256 ?? Hash(normalizedBefore), replacementCount = count };
                if (changed.TryGetValue(path, out retained))
                {
                    if (retained.beforeSha256 != transition.beforeSha256 || retained.afterSha256 != transition.afterSha256 ||
                        retained.nonScriptSha256 != transition.nonScriptSha256 || retained.replacementCount != count)
                        throw new InvalidOperationException("Retained original script transition differs: " + path);
                }
                else changed.Add(path, transition);
            }
            assetProgress.Complete("Original serialized script pointer inputs validated");
            // Validate the entire transaction before touching any original-derived file.
            PublishTransitions(new Receipt {
                replacements = replacements.Values.OrderBy(r => r.fullName).ToArray(),
                assets = changed.Values.OrderBy(a => a.path, StringComparer.Ordinal).ToArray(),
                callbacksAndOtherSerializedBytesPreserved = true });
            var writeProgress = new QuestWizardProgress.Counter("unity-script-write", "unity-validation", plans.Count, "files", "Apply validated original package script pointers");
            int written = 0;
            foreach (var plan in plans)
            { writeProgress.Report(written, plan.Key); File.WriteAllText(plan.Key, plan.Value, new UTF8Encoding(false)); writeProgress.Report(++written, plan.Key); }
            writeProgress.Complete("Validated original package script pointers applied");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Quest startup] Rebound " + plans.Sum(plan => changed[plan.Key].replacementCount) + " SDK script pointers; original callbacks retained.");
        }

        private static Dictionary<string, ChangedAsset> ReadRetainedTransitions(Input input, Dictionary<string, Replacement> replacements)
        {
            var result = new Dictionary<string, ChangedAsset>(StringComparer.Ordinal);
            const string path = "QuestStartupEvidence/script-remap.json";
            if (!File.Exists(path)) return result;
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
                throw new InvalidOperationException("Retained original script transition evidence is too large.");
            var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(path));
            if (receipt == null || receipt.schema != 1 || !receipt.callbacksAndOtherSerializedBytesPreserved ||
                receipt.replacements == null || receipt.assets == null || receipt.replacements.Length != replacements.Count)
                throw new InvalidOperationException("Retained original script transition evidence is invalid.");
            var mapped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in receipt.replacements)
            {
                Replacement expected;
                string key = row == null ? "" : row.oldGuid + ":" + row.oldFileId.ToString(CultureInfo.InvariantCulture);
                if (row == null || !mapped.Add(key) || !replacements.TryGetValue(key, out expected) ||
                    row.assemblyName != expected.assemblyName || row.fullName != expected.fullName ||
                    row.newGuid != expected.newGuid || row.newFileId != expected.newFileId ||
                    (!string.IsNullOrEmpty(row.newAssetPath) && row.newAssetPath != expected.newAssetPath))
                    throw new InvalidOperationException("Retained original package script mapping changed.");
            }
            var allowed = new HashSet<string>(input.assetPaths, StringComparer.Ordinal);
            foreach (var row in receipt.assets)
            {
                if (row == null || !allowed.Contains(row.path) || result.ContainsKey(row.path) || row.replacementCount <= 0 ||
                    !Regex.IsMatch(row.beforeSha256 ?? "", "^[0-9a-f]{64}$") || !Regex.IsMatch(row.afterSha256 ?? "", "^[0-9a-f]{64}$") ||
                    !Regex.IsMatch(row.nonScriptSha256 ?? "", "^[0-9a-f]{64}$"))
                    throw new InvalidOperationException("Retained original script asset evidence is invalid.");
                result.Add(row.path, row);
            }
            return result;
        }

        private static void PublishTransitions(Receipt receipt)
        {
            const string path = "QuestStartupEvidence/script-remap.json";
            Directory.CreateDirectory("QuestStartupEvidence");
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
