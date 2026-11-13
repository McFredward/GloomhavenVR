#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Real imported SDK remap controls; run only in a private fixture project.</summary>
public static class QuestOriginalScriptBindingsFixture
{
    private static int checks;
    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }
    [Serializable] private sealed class Report
    {
        public int checks, sourceSdkTypeCount, failureControls;
        public bool passed, androidPreferencesChanged;
    }

    public static void Run()
    {
        try
        {
            if (!File.Exists("quest-original-fixture.json")) throw new InvalidOperationException("Private fixture marker missing.");
            var input = JsonUtility.FromJson<QuestOriginalScriptBindings.Input>(File.ReadAllText("quest-original-fixture.json"));
            Check(input.bindings.Length == 28, "Expected the actual 28 original startup SDK types.");
            Directory.CreateDirectory("Assets/QuestOriginalStartup");
            Directory.CreateDirectory("Assets/OriginalFixture");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Original SDK fixture", typeof(RectTransform));
            var actualToOriginal = new Dictionary<string, QuestOriginalScriptBindings.Binding>();
            var assetPaths = new List<string>();
            Button button = null;
            foreach (var binding in input.bindings)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == binding.assemblyName)
                    .Select(a => a.GetType(binding.fullName, false)).SingleOrDefault(t => t != null);
                Check(type != null, "Actual package type unavailable: " + binding.fullName);
                MonoScript script;
                if (typeof(MonoBehaviour).IsAssignableFrom(type))
                {
                    var child = new GameObject(type.Name, typeof(RectTransform));
                    child.transform.SetParent(root.transform, false);
                    var component = (MonoBehaviour)child.AddComponent(type);
                    script = MonoScript.FromMonoBehaviour(component);
                    if (component is Button) button = (Button)component;
                }
                else if (typeof(ScriptableObject).IsAssignableFrom(type))
                {
                    var asset = ScriptableObject.CreateInstance(type);
                    var path = "Assets/OriginalFixture/" + type.Name + ".asset";
                    AssetDatabase.CreateAsset(asset, path);
                    assetPaths.Add(path);
                    script = MonoScript.FromScriptableObject(asset);
                }
                else throw new InvalidOperationException("Fixture type is not a Unity object: " + binding.fullName);
                string guid; long id;
                Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out id), "Imported SDK script identity missing.");
                actualToOriginal.Add(guid + ":" + id, binding);
            }
            Check(button != null, "Actual SDK Button missing.");
            UnityEventTools.AddBoolPersistentListener(button.onClick, root.SetActive, false);
            var prefabPath = "Assets/OriginalFixture/Original.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            assetPaths.Add(prefabPath);
            input.assetPaths = assetPaths.ToArray();
            var pattern = new Regex(@"m_Script:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32}),\s*type:\s*3\}");
            var originalBytes = new Dictionary<string, string>();
            foreach (var path in assetPaths)
            {
                var sdkText = File.ReadAllText(path);
                var oldText = pattern.Replace(sdkText, match => {
                    QuestOriginalScriptBindings.Binding binding;
                    if (!actualToOriginal.TryGetValue(match.Groups[2].Value + ":" + match.Groups[1].Value, out binding))
                        throw new InvalidOperationException("Unaccounted SDK script in fixture: " + match.Value);
                    return "m_Script: {fileID: " + binding.oldFileId + ", guid: " + binding.oldGuid + ", type: 3}";
                });
                originalBytes[path] = oldText;
                File.WriteAllText(path, oldText);
            }
            const string sidecar = "Assets/QuestOriginalStartup/script-bindings.json";
            File.WriteAllText(sidecar, JsonUtility.ToJson(input));
            QuestOriginalScriptBindings.RemapAndValidate();
            foreach (var path in assetPaths)
            {
                var after = File.ReadAllText(path);
                Check(pattern.Replace(after, "<script>") == pattern.Replace(originalBytes[path], "<script>"), "Callback/field serialization changed.");
                Check(!input.disabledPluginGuids.Any(guid => pattern.Matches(after).Cast<Match>().Any(m => m.Groups[2].Value == guid)), "Disabled plugin reference remains.");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Check(prefab != null, "Actual remapped prefab did not import.");
            var remappedButton = prefab.GetComponentInChildren<Button>();
            Check(remappedButton != null && remappedButton.onClick.GetPersistentEventCount() == 1, "Original native callback missing.");
            Check(remappedButton.onClick.GetPersistentMethodName(0) == "SetActive", "Original callback method changed.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var instanceButton = instance.GetComponentInChildren<Button>();
            Check(instanceButton.onClick.GetPersistentTarget(0) == instance, "Callback target no longer points at the native prefab root.");
            Check(instanceButton.onClick.GetPersistentListenerState(0) == UnityEngine.Events.UnityEventCallState.RuntimeOnly,
                "Original callback activation state changed.");
            // Permit execution on this disposable instance in an EditMode test;
            // the retained asset callback remains in its original RuntimeOnly state.
            instanceButton.onClick.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            instanceButton.onClick.Invoke();
            Check(!instance.activeSelf, "Retained original callback no longer executes.");
            UnityEngine.Object.DestroyImmediate(instance);
            // Every negative control restores the genuine serialized source and
            // must fail before any source-derived file changes.
            var failures = 0;
            Action<Action> reject = change => {
                foreach (var pair in originalBytes) File.WriteAllText(pair.Key, pair.Value);
                File.WriteAllText(sidecar, JsonUtility.ToJson(input));
                change();
                var expected = assetPaths.ToDictionary(path => path, File.ReadAllText);
                bool rejected = false;
                try { QuestOriginalScriptBindings.RemapAndValidate(); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected, "Invalid mapping unexpectedly passed.");
                foreach (var path in assetPaths) Check(expected[path] == File.ReadAllText(path), "Invalid transaction changed source asset.");
                failures++;
            };
            reject(() => {
                var text = File.ReadAllText(prefabPath);
                var first = pattern.Match(text);
                File.WriteAllText(prefabPath, text.Remove(first.Index, first.Length).Insert(first.Index,
                    "m_Script: {fileID: 922337203685477580, guid: " + first.Groups[2].Value + ", type: 3}"));
            });
            reject(() => {
                var invalid = JsonUtility.FromJson<QuestOriginalScriptBindings.Input>(JsonUtility.ToJson(input));
                invalid.bindings[0].fullName += "Missing";
                File.WriteAllText(sidecar, JsonUtility.ToJson(invalid));
            });
            reject(() => {
                var invalid = JsonUtility.FromJson<QuestOriginalScriptBindings.Input>(JsonUtility.ToJson(input));
                invalid.bindings = invalid.bindings.Concat(new[] { invalid.bindings[0] }).ToArray();
                File.WriteAllText(sidecar, JsonUtility.ToJson(invalid));
            });
            reject(() => {
                var invalid = JsonUtility.FromJson<QuestOriginalScriptBindings.Input>(JsonUtility.ToJson(input));
                invalid.assetPaths = invalid.assetPaths.Concat(new[] { "Assets/../../outside.prefab" }).ToArray();
                File.WriteAllText(sidecar, JsonUtility.ToJson(invalid));
            });
            File.WriteAllText("fixture-result.json", JsonUtility.ToJson(new Report { checks = checks,
                sourceSdkTypeCount = input.bindings.Length, failureControls = failures, passed = true,
                androidPreferencesChanged = false }, true));
            Debug.Log("[Original SDK fixture] Passed " + checks + " checks and " + failures + " fail-closed controls.");
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }
}
#endif
