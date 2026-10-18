#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only actual-import inventory; use only in a private project copied from a prepared build.</summary>
public static class QuestScriptOrderInventory
{
    [Serializable] private sealed class Row
    {
        public string assemblyName, fullName, pluginPath, baseType, status;
        public int executionOrder, typedImportCount, identityImportCount, typeAttributes;
        public bool referenced, typeExists, monoBehaviour, scriptableObject;
    }
    [Serializable] private sealed class Report
    {
        public int entries, scripts, nullClassScripts, missingReferenced, missingNonzero, missingUnityTypes;
        public Row[] missing;
    }
    private sealed class Import
    {
        public Type type;
        public string assemblyName, fullName, path, guid, fileId;
    }

    public static void Run()
    {
        try
        {
            if (!File.Exists("quest-script-orders-fixture.marker")) throw new InvalidOperationException("Private inventory marker missing.");
            var input = JsonUtility.FromJson<QuestOriginalScriptOrders.Input>(File.ReadAllText(QuestOriginalScriptOrders.InputPath));
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            var imports = scripts.Select(script => {
                Type type = script.GetClass();
                string guid; long id;
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out id);
                return new Import { type = type, assemblyName = type == null ? "" : type.Assembly.GetName().Name,
                    fullName = type == null ? "" : type.FullName, path = AssetDatabase.GetAssetPath(script), guid = guid,
                    fileId = id.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            }).ToArray();
            var rows = input.entries.Select(entry =>
            {
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == entry.assemblyName);
                if (assembly == null) assembly = Assembly.Load(entry.assemblyName);
                Type type = assembly.GetType(entry.fullName, false);
                int typed = imports.Count(s => s.type != null && s.assemblyName == entry.assemblyName &&
                    s.fullName == entry.fullName && (entry.package ? s.path.StartsWith("Packages/") : s.path == entry.pluginPath));
                int identity = imports.Count(s => s.path == entry.pluginPath && s.guid == entry.originalGuid && s.fileId == entry.originalFileId);
                return new Row { assemblyName = entry.assemblyName, fullName = entry.fullName, pluginPath = entry.pluginPath,
                    executionOrder = entry.executionOrder, referenced = entry.referenced, typedImportCount = typed,
                    identityImportCount = identity, typeExists = type != null, typeAttributes = type == null ? 0 : (int)type.Attributes,
                    baseType = type == null || type.BaseType == null ? "" : type.BaseType.FullName,
                    monoBehaviour = type != null && typeof(MonoBehaviour).IsAssignableFrom(type),
                    scriptableObject = type != null && typeof(ScriptableObject).IsAssignableFrom(type), status = typed == 1 ? "resolved" : "missing-or-ambiguous" };
            }).ToArray();
            var missing = rows.Where(r => r.typedImportCount != 1).ToArray();
            var report = new Report { entries = rows.Length, scripts = scripts.Length, nullClassScripts = imports.Count(s => s.type == null),
                missing = missing, missingReferenced = missing.Count(r => r.referenced), missingNonzero = missing.Count(r => r.executionOrder != 0),
                missingUnityTypes = missing.Count(r => r.monoBehaviour || r.scriptableObject) };
            File.WriteAllText("script-order-import-inventory.json", JsonUtility.ToJson(report, true));
            Debug.Log("[Original script order inventory] entries=" + report.entries + " missing=" + missing.Length +
                " referenced=" + report.missingReferenced + " nonzero=" + report.missingNonzero + " UnityTypes=" + report.missingUnityTypes);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
#endif
