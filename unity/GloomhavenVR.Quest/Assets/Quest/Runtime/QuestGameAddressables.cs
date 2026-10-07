#nullable disable
#if GHVR_QUEST_STARTUP
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace GloomhavenVR.Quest
{
    [Serializable] public sealed class QuestGameAddressableAlias { public string key, assetGuid; public string[] assetKeys; }
    [Serializable] public sealed class QuestGameAddressablesManifest
    {
        public int schema;
        public string inputKey, runtimeSettingsPath;
        public string[] requiredLabels;
        public QuestGameAddressableAlias[] aliases;
    }

    /// <summary>Initializes a genuinely built Android catalog through Addressables' public API.</summary>
    public sealed class QuestGameAddressables : IDisposable
    {
        public bool Ready { get; private set; }
        public Exception Failure { get; private set; }
        readonly List<AsyncOperationHandle> pinned = new List<AsyncOperationHandle>();
        Func<IResourceLocation, string> previousTransform, ownedTransform;
        IResourceLocator aliasLocator;

        public IEnumerator Install(QuestGameAddressablesManifest manifest, QuestGameContentManifest content, string root, string inputKey)
        {
            string settings = null;
            try
            {
                if (manifest == null || manifest.schema != 1 || manifest.inputKey != inputKey || manifest.requiredLabels == null || manifest.requiredLabels.Length == 0)
                    throw new InvalidDataException("Native startup Addressables manifest is missing or invalid.");
                settings = QuestGameContent.ResolveVerifiedPath(content, root, manifest.runtimeSettingsPath);
                var labels = new HashSet<string>(StringComparer.Ordinal);
                foreach (string label in manifest.requiredLabels)
                    if (string.IsNullOrEmpty(label) || !labels.Add(label)) throw new InvalidDataException("Native startup label is missing or duplicated.");
                previousTransform = Addressables.InternalIdTransformFunc;
                string originalStreaming = Application.streamingAssetsPath.TrimEnd('/');
                string extractedStreaming = Path.Combine(root, "StreamingAssets");
                ownedTransform = location =>
                {
                    string id = previousTransform != null ? previousTransform(location) : location.InternalId;
                    if (id.StartsWith(originalStreaming + "/", StringComparison.Ordinal))
                        return Path.Combine(extractedStreaming, id.Substring(originalStreaming.Length + 1));
                    return id;
                };
                Addressables.InternalIdTransformFunc = ownedTransform;
                PlayerPrefs.SetString(Addressables.kAddressablesRuntimeDataPath, settings);
            }
            catch (Exception e) { Failure = e; }
            if (Failure != null) yield break;
            AsyncOperationHandle<IResourceLocator> initialization = Addressables.InitializeAsync(false);
            pinned.Add(initialization);
            yield return initialization;
            if (initialization.Status != AsyncOperationStatus.Succeeded)
            {
                Failure = initialization.OperationException ?? new InvalidDataException("Native startup catalog initialization failed."); yield break;
            }
            try
            {
                if (manifest.aliases != null && manifest.aliases.Length != 0)
                {
                    var aliases = new AliasLocator();
                    foreach (QuestGameAddressableAlias alias in manifest.aliases)
                    {
                        if (alias == null || string.IsNullOrEmpty(alias.key)) throw new InvalidDataException("Invalid original Addressables alias.");
                        string[] keys = alias.assetKeys != null && alias.assetKeys.Length != 0 ? alias.assetKeys : new[] { alias.assetGuid };
                        var native = new List<IResourceLocation>();
                        var selected = new HashSet<string>(StringComparer.Ordinal);
                        foreach (string key in keys)
                        {
                            if (string.IsNullOrEmpty(key) || !selected.Add(key)) throw new InvalidDataException("Original alias has an empty or repeated native asset key.");
                            bool resolved = false;
                            foreach (IResourceLocator locator in Addressables.ResourceLocators)
                            {
                                if (!locator.Locate(key, typeof(UnityEngine.Object), out IList<IResourceLocation> locations)
                                    || locations == null || locations.Count == 0) continue;
                                foreach (IResourceLocation location in locations)
                                    if (!native.Contains(location)) native.Add(location);
                                resolved = true;
                                break;
                            }
                            if (!resolved) throw new InvalidDataException("Original key alias has no real native catalog asset: " + alias.key + " -> " + key);
                        }
                        aliases.Add(alias.key, native);
                    }
                    aliasLocator = aliases; Addressables.AddResourceLocator(aliases);
                    Debug.Log("[Quest startup] original Addressables aliases registered=" + manifest.aliases.Length);
                }
            }
            catch (Exception e) { Failure = e; }
            if (Failure != null) yield break;
            foreach (string label in manifest.requiredLabels)
            {
                AsyncOperationHandle<IList<IResourceLocation>> locations = Addressables.LoadResourceLocationsAsync(label, typeof(UnityEngine.Object));
                yield return locations;
                if (locations.Status != AsyncOperationStatus.Succeeded || locations.Result == null || locations.Result.Count == 0)
                {
                    Failure = locations.OperationException ?? new InvalidDataException("Native startup catalog has no actual assets for required label: " + label);
                    Addressables.Release(locations); yield break;
                }
                int count = locations.Result.Count;
                Addressables.Release(locations);
                // The original AssetBundleManager owns initial asset loading,
                // its progress and retained handles. Preloading the same labels
                // here delays the intro and creates a second lifetime owner.
                Debug.Log("[Quest startup] native Addressables label located=" + label + " assets=" + count);
            }
            Ready = true;
            Debug.Log("[Quest startup] native Addressables initialized; original AssetBundleManager owns required asset loading.");
        }
        public void Dispose()
        {
            foreach (AsyncOperationHandle handle in pinned) if (handle.IsValid()) Addressables.Release(handle);
            pinned.Clear();
            if (aliasLocator != null) Addressables.RemoveResourceLocator(aliasLocator);
            if (Addressables.InternalIdTransformFunc == ownedTransform) Addressables.InternalIdTransformFunc = previousTransform;
            Ready = false;
        }

        sealed class AliasLocator : IResourceLocator
        {
            readonly Dictionary<object, IList<IResourceLocation>> aliases = new Dictionary<object, IList<IResourceLocation>>();
            public string LocatorId { get { return "QuestOriginalKeys-v2"; } }
            public IEnumerable<object> Keys { get { return aliases.Keys; } }
            public void Add(string key, IList<IResourceLocation> native)
            {
                if (aliases.ContainsKey(key)) throw new InvalidDataException("Duplicate original key alias: " + key);
                aliases.Add(key, new List<IResourceLocation>(native));
            }
            public bool Locate(object key, Type type, out IList<IResourceLocation> locations)
            {
                locations = null;
                IList<IResourceLocation> all;
                if (!aliases.TryGetValue(key, out all)) return false;
                var matches = new List<IResourceLocation>();
                foreach (IResourceLocation location in all) if (type == null || type.IsAssignableFrom(location.ResourceType)) matches.Add(location);
                if (matches.Count == 0) return false;
                locations = matches; return true;
            }
        }
    }
}
#endif
