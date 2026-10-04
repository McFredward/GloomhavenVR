#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Restores the original player's authored initialization order after SDK script remapping.</summary>
    public static class QuestOriginalScriptOrders
    {
        public const string InputPath = "Assets/QuestOriginalStartup/script-orders.json";
        public const string ReceiptPath = "QuestStartupEvidence/script-orders.json";

        [Serializable] public sealed class Entry
        {
            public string assemblyName, fullName, originalGuid, originalFileId, pluginPath;
            public int executionOrder;
            public string[] sourcePathIds;
            public bool referenced, package;
        }

        [Serializable] public sealed class Excluded
        {
            public string assemblyName, fullName, reason;
            public int executionOrder;
            public string[] sourcePathIds;
            public bool referenced;
            public int typeAttributes;
            public string baseAssemblyName, baseNamespace, baseName, pluginPath, originalAssemblySha256;
        }

        [Serializable] public sealed class Input
        {
            public int schema;
            public string sourceSha256;
            public Entry[] entries;
            public Excluded[] excluded;
        }

        [Serializable] public sealed class Restored
        {
            public string assemblyName, fullName, originalGuid, originalFileId, actualPath, actualGuid, actualFileId;
            public int executionOrder, previousOrder, verifiedOrder;
            public string[] sourcePathIds;
            public bool referenced, package;
        }

        [Serializable] public sealed class Receipt
        {
            public int schema = 1;
            public string sourceSha256, inputSha256;
            public Restored[] restored;
            public Excluded[] excluded;
            public bool allMappedOrdersVerified;
        }

        private sealed class Plan
        {
            public MonoScript script;
            public Restored receipt;
        }

        public static void RestoreAndVerify()
        {
            if (!File.Exists(InputPath)) throw new InvalidOperationException("Original script order manifest is missing.");
            byte[] bytes = File.ReadAllBytes(InputPath);
            var input = JsonUtility.FromJson<Input>(Encoding.UTF8.GetString(bytes));
            if (input == null || input.schema != 1 || input.entries == null || input.excluded == null ||
                !IsHex(input.sourceSha256, 64))
                throw new InvalidOperationException("Original script order manifest is invalid.");

            var identities = new HashSet<string>(StringComparer.Ordinal);
            var sourceIds = new HashSet<string>(StringComparer.Ordinal);
            var exclusions = new List<Excluded>();
            foreach (var excluded in input.excluded)
            {
                if (excluded == null || excluded.referenced ||
                    (excluded.reason != "assembly-outside-startup-closure" && excluded.reason != "no-original-top-level-type" &&
                     excluded.reason != "original-static-utility"))
                    throw new InvalidOperationException("Original script order exclusion has no supported source evidence.");
                ValidateIdentity(excluded.assemblyName, excluded.fullName, excluded.sourcePathIds, identities, sourceIds);
                if (excluded.reason == "original-static-utility") ValidateStaticUtility(excluded);
                exclusions.Add(excluded);
            }

            var available = new Dictionary<string, List<MonoScript>>(StringComparer.Ordinal);
            foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
            {
                Type type = script.GetClass();
                if (type == null) continue;
                string key = type.Assembly.GetName().Name + "|" + type.FullName;
                List<MonoScript> matches;
                if (!available.TryGetValue(key, out matches)) available[key] = matches = new List<MonoScript>();
                if (!matches.Contains(script)) matches.Add(script);
            }

            var plans = new List<Plan>();
            foreach (var entry in input.entries)
            {
                if (entry == null) throw new InvalidOperationException("Original script order entry is null.");
                ValidateIdentity(entry.assemblyName, entry.fullName, entry.sourcePathIds, identities, sourceIds);
                long originalFileId;
                if (!IsHex(entry.originalGuid, 32) ||
                    !long.TryParse(entry.originalFileId, NumberStyles.Integer, CultureInfo.InvariantCulture, out originalFileId) ||
                    originalFileId == 0 || !IsPluginPath(entry.pluginPath))
                    throw new InvalidOperationException("Original script order plugin identity is invalid: " + entry.fullName);

                List<MonoScript> candidates;
                available.TryGetValue(entry.assemblyName + "|" + entry.fullName, out candidates);
                // Disabled original SDK DLLs can still expose MonoScripts. Only the mapped package import is active.
                var matches = candidates == null ? new List<MonoScript>() : candidates.Where(script =>
                {
                    string path = AssetDatabase.GetAssetPath(script);
                    return entry.package ? path.StartsWith("Packages/", StringComparison.Ordinal) : path == entry.pluginPath;
                }).ToList();
                if (matches.Count == 0)
                {
                    if (entry.referenced || entry.executionOrder != 0)
                        throw new InvalidOperationException("Required original script order target is missing: " + entry.assemblyName + " " + entry.fullName);
                    exclusions.Add(new Excluded { assemblyName = entry.assemblyName, fullName = entry.fullName,
                        executionOrder = 0, sourcePathIds = entry.sourcePathIds, referenced = false,
                        reason = "unavailable-to-MonoImporter" });
                    continue;
                }
                if (matches.Count != 1)
                    throw new InvalidOperationException("Original script order target is ambiguous: " + entry.assemblyName + " " + entry.fullName);
                var target = matches[0];
                string guid;
                long fileId;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target, out guid, out fileId) || fileId == 0 ||
                    (!entry.package && (guid != entry.originalGuid || fileId != originalFileId)))
                    throw new InvalidOperationException("Original script order import identity differs: " + entry.assemblyName + " " + entry.fullName);
                plans.Add(new Plan { script = target, receipt = new Restored {
                    assemblyName = entry.assemblyName, fullName = entry.fullName, executionOrder = entry.executionOrder,
                    sourcePathIds = entry.sourcePathIds, referenced = entry.referenced, package = entry.package,
                    originalGuid = entry.originalGuid, originalFileId = entry.originalFileId,
                    actualPath = AssetDatabase.GetAssetPath(target), actualGuid = guid,
                    actualFileId = fileId.ToString(CultureInfo.InvariantCulture), previousOrder = MonoImporter.GetExecutionOrder(target) } });
            }

            // Validate the entire mapping before mutations. Restore previous values if Unity rejects any exact order.
            var applied = new List<Plan>();
            try
            {
                foreach (var plan in plans)
                {
                    // Original DLL importer metadata usually already retains the authored value.
                    // Avoid thousands of redundant native reimports while still verifying every explicit zero.
                    if (plan.receipt.previousOrder != plan.receipt.executionOrder)
                    {
                        applied.Add(plan);
                        MonoImporter.SetExecutionOrder(plan.script, plan.receipt.executionOrder);
                    }
                    plan.receipt.verifiedOrder = MonoImporter.GetExecutionOrder(plan.script);
                    if (plan.receipt.verifiedOrder != plan.receipt.executionOrder)
                        throw new InvalidOperationException("Unity changed the original script execution order: " + plan.receipt.fullName +
                            " expected=" + plan.receipt.executionOrder + " actual=" + plan.receipt.verifiedOrder);
                }
            }
            catch
            {
                foreach (var plan in applied) MonoImporter.SetExecutionOrder(plan.script, plan.receipt.previousOrder);
                throw;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(new Receipt {
                sourceSha256 = input.sourceSha256, inputSha256 = Hash(bytes),
                restored = plans.Select(p => p.receipt).OrderBy(r => r.assemblyName, StringComparer.Ordinal)
                    .ThenBy(r => r.fullName, StringComparer.Ordinal).ToArray(),
                excluded = exclusions.OrderBy(e => e.assemblyName, StringComparer.Ordinal)
                    .ThenBy(e => e.fullName, StringComparer.Ordinal).ToArray(), allMappedOrdersVerified = true }, true), new UTF8Encoding(false));
            Debug.Log("[Quest startup] Verified " + plans.Count + " original script execution orders (" +
                plans.Count(p => p.receipt.executionOrder != 0) + " nonzero); " + exclusions.Count + " evidenced exclusions.");
        }

        private static void ValidateIdentity(string assembly, string type, string[] ids,
            HashSet<string> identities, HashSet<string> sourceIds)
        {
            if (string.IsNullOrEmpty(assembly) || string.IsNullOrEmpty(type) || ids == null || ids.Length == 0 ||
                !identities.Add(assembly + "|" + type))
                throw new InvalidOperationException("Original script order type identity is missing or duplicated.");
            foreach (string id in ids)
            {
                long parsed;
                if (!long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) || parsed == 0 ||
                    !sourceIds.Add(id)) throw new InvalidOperationException("Original script order source path ID is invalid or duplicated.");
            }
        }

        private static bool IsPluginPath(string path)
        {
            return !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal) &&
                path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && path.IndexOf('\\') < 0 &&
                !path.Split('/').Any(part => part.Length == 0 || part == "." || part == "..");
        }

        private static void ValidateStaticUtility(Excluded excluded)
        {
            const int staticFlags = (int)(TypeAttributes.Abstract | TypeAttributes.Sealed);
            if ((excluded.typeAttributes & staticFlags) != staticFlags ||
                (excluded.typeAttributes & (int)TypeAttributes.Interface) != 0 || excluded.baseNamespace != "System" ||
                excluded.baseName != "Object" || (excluded.baseAssemblyName != "mscorlib" &&
                excluded.baseAssemblyName != "System.Runtime" && excluded.baseAssemblyName != "System.Private.CoreLib") ||
                !IsPluginPath(excluded.pluginPath) || !IsHex(excluded.originalAssemblySha256, 64))
                throw new InvalidOperationException("Original static utility exclusion lacks source type evidence: " + excluded.fullName);
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == excluded.assemblyName);
            if (assembly == null) assembly = Assembly.Load(excluded.assemblyName);
            Type type = assembly.GetType(excluded.fullName, false);
            // Static utility metadata can retain an authored order in its DLL importer,
            // but cannot own Unity lifecycle callbacks or an imported runtime MonoScript.
            if (type == null || !type.IsAbstract || !type.IsSealed || type.IsInterface || type.BaseType != typeof(object) ||
                typeof(MonoBehaviour).IsAssignableFrom(type) || typeof(ScriptableObject).IsAssignableFrom(type))
                throw new InvalidOperationException("Imported type does not confirm the original static utility exclusion: " + excluded.fullName);
        }

        private static bool IsHex(string value, int length)
        {
            return value != null && value.Length == length && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
