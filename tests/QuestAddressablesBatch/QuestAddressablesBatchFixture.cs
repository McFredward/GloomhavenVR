// Run only in a private Unity 2021.3.5f1 project with the pinned native packages.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public sealed class QuestBatchSaveWitness : AssetModificationProcessor
{
    public static int Saves;
    static string[] OnWillSaveAssets(string[] paths) { Saves++; return paths; }
}

public sealed class QuestBatchImportWitness : AssetPostprocessor
{
    public static int Calls, Assets;
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] prior)
    {
        int owned = imported.Count(path => path.Contains("/AssetGroups/"));
        if (owned > 0) { Calls++; Assets += owned; }
    }
}

public static class QuestAddressablesBatchFixture
{
    const int Count = 24;
    [Serializable] sealed class Case
    {
        public string name;
        public double milliseconds;
        public int groups, saves, importCallbacks, importedAssets;
        public string[] profileLinks;
    }
    [Serializable] sealed class Receipt
    {
        public int schema = 1, checks;
        public string unityVersion, addressablesVersion;
        public Case[] cases;
        public bool existingPartialCompleted, partialSchemaGuidRetained, failureReleasedEditing, noOriginalExceptionMasking, reopened;
    }
    [Serializable] sealed class Mapping
    {
        public int schema = 1, associatedEntryCount = 1, unresolvedEntryCount;
        public Association[] entries;
    }
    [Serializable] sealed class Association
    {
        public string assetPath, recoveredGuid, resourceTypeName = "UnityEngine.TextAsset, UnityEngine.CoreModule", sourceBundle, status = "associated";
        public string[] keys, labels;
    }
    static int checks;
    static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); checks++; }

    public static void Measure()
    {
        for (int index = 0; index < Count; index++)
        {
            string path = "Assets/Owned" + index + ".txt";
            File.WriteAllText(path, "private fixture " + index);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
        var baseline = MeasureCase("baseline", false);
        var batch = MeasureCase("batch", true);
        Require(baseline.groups == batch.groups && baseline.groups == Count, "Batch changed native group count.");
        Require(baseline.saves == batch.saves, "The fixture concealed package SaveAssets calls.");
        Require(baseline.profileLinks.SequenceEqual(batch.profileLinks), "Batch changed native local build/load profile links.");
        bool partial = VerifyPartial(out bool retained);
        bool released = VerifyFailure(out bool unmasked);
        var receipt = new Receipt { unityVersion = Application.unityVersion, addressablesVersion = "1.19.19",
            cases = new[] { baseline, batch }, existingPartialCompleted = partial, partialSchemaGuidRetained = retained,
            failureReleasedEditing = released, noOriginalExceptionMasking = unmasked, checks = checks };
        File.WriteAllText("batch-receipt.json", JsonUtility.ToJson(receipt, true));
        UnityEngine.Debug.Log("[Addressables batch witness] baselineMs=" + baseline.milliseconds + " batchMs=" + batch.milliseconds + " checks=" + checks);
    }

    static Case MeasureCase(string name, bool batch)
    {
        string folder = "Assets/" + name;
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var settings = AddressableAssetSettings.Create(folder, "Settings", true, true);
        QuestBatchSaveWitness.Saves = QuestBatchImportWitness.Calls = QuestBatchImportWitness.Assets = 0;
        var clock = Stopwatch.StartNew();
        if (batch) AssetDatabase.StartAssetEditing();
        try
        {
            for (int index = 0; index < Count; index++)
            {
                var group = QuestStartupAddressablesBuild.GetOrCreateOwnedGroup(settings, "Owned Campaign " + index, false);
                Configure(group);
                // Separate native ownership survives even when entry addresses
                // and labels resemble the original shared preload categories.
                string assetPath = "Assets/Owned" + index + ".txt";
                var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(assetPath), group, false, false);
                entry.address = entry.guid;
                settings.AddLabel("always_loaded_base", false);
                entry.SetLabel("always_loaded_base", true, false, false);
            }
            EditorUtility.SetDirty(settings);
        }
        finally { if (batch) AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();clock.Stop();
        var result = new Case { name = name, milliseconds = clock.Elapsed.TotalMilliseconds,
            groups = settings.groups.Count(group => group != null && group.Name.StartsWith("Owned Campaign ", StringComparison.Ordinal)),
            saves = QuestBatchSaveWitness.Saves, importCallbacks = QuestBatchImportWitness.Calls, importedAssets = QuestBatchImportWitness.Assets };
        result.profileLinks = Validate(settings);
        return result;
    }

    static void Configure(AddressableAssetGroup group)
    {
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        schema.IncludeAddressInCatalog = schema.IncludeGUIDInCatalog = schema.IncludeLabelsInCatalog = true;
    }

    static string[] Validate(AddressableAssetSettings settings)
    {
        var links = new HashSet<string>();
        for (int index = 0; index < Count; index++)
        {
            var group = settings.FindGroup("Owned Campaign " + index);
            Require(group != null, "Lost original lifetime group.");
            var bundle = group.GetSchema<BundledAssetGroupSchema>();
            var update = group.GetSchema<ContentUpdateGroupSchema>();
            Require(group.Schemas.Count == 2 && bundle != null && update != null, "Lost native schema types.");
            Require(bundle.Group == group && update.Group == group, "Native schema Group binding lost.");
            Require(bundle.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogether, "Bundle packing changed.");
            Require(bundle.IncludeAddressInCatalog && bundle.IncludeGUIDInCatalog && bundle.IncludeLabelsInCatalog, "Original key/label/GUID capabilities changed.");
            Require(!update.StaticContent, "Native update/lifetime mode changed.");
            string build = bundle.BuildPath.GetName(settings);
            string load = bundle.LoadPath.GetName(settings);
            Require(build == AddressableAssetSettings.kLocalBuildPath && load == AddressableAssetSettings.kLocalLoadPath, "Native profile links differ.");
            links.Add(build + "|" + load);
            Require(group.entries.Count == 1, "Native asset ownership changed.");
            var entry = group.entries.Single();
            Require(entry.parentGroup == group && entry.address == entry.guid && entry.labels.SetEquals(new[] { "always_loaded_base" }), "Native entry address/label binding changed.");
            Require(AssetDatabase.GetAssetPath(bundle).StartsWith(settings.GroupSchemaFolder, StringComparison.Ordinal) &&
                AssetDatabase.GetAssetPath(update).StartsWith(settings.GroupSchemaFolder, StringComparison.Ordinal), "Native schema files escaped their owner.");
        }
        return links.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    static bool VerifyPartial(out bool retained)
    {
        var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>("Assets/batch/Settings.asset");
        var group = settings.CreateGroup("Owned partial", false, false, false, null, typeof(BundledAssetGroupSchema));
        var old = group.GetSchema<BundledAssetGroupSchema>();
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(old));
        AssetDatabase.StartAssetEditing();
        try { Require(QuestStartupAddressablesBuild.GetOrCreateOwnedGroup(settings, "Owned partial", false) == group, "Existing partial group replaced."); Configure(group); }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        retained = group.GetSchema<BundledAssetGroupSchema>() == old && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(old)) == guid;
        Require(retained, "Existing native schema identity was replaced.");
        Require(group.GetSchema<ContentUpdateGroupSchema>()?.Group == group && group.Schemas.Count == 2, "Partial native schema was not completed.");
        return true;
    }

    static bool VerifyFailure(out bool unmasked)
    {
        Directory.CreateDirectory("Assets/QuestOriginalCampaign");
        File.WriteAllText("Assets/QuestOriginalCampaign/campaign-addressables.json", JsonUtility.ToJson(new Mapping
        {
            entries = new[] { new Association { assetPath = "Assets/MissingNativeAsset.txt", recoveredGuid = new string('a', 32), sourceBundle = "negative-original", keys = new[] { "negative" }, labels = new[] { "always_loaded_base" } } }
        }));
        Environment.SetEnvironmentVariable("GHVR_QUEST_TARGET", "game");
        unmasked = false;
        try { QuestStartupAddressablesBuild.Build(); }
        catch (InvalidDataException error) { unmasked = error.Message.Contains("identity failed import: Assets/MissingNativeAsset.txt"); }
        finally { Environment.SetEnvironmentVariable("GHVR_QUEST_TARGET", null); }
        Require(unmasked, "Original validation failure was lost or cleanup masked it.");
        File.WriteAllText("Assets/AfterFailure.txt", "must import immediately");
        AssetDatabase.ImportAsset("Assets/AfterFailure.txt", ImportAssetOptions.ForceSynchronousImport);
        bool released = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/AfterFailure.txt") != null;
        Require(released, "Failed original validation left asset editing suspended.");
        return released;
    }

    public static void Reopen()
    {
        var receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText("batch-receipt.json"));
        foreach (var row in receipt.cases)
        {
            var settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>("Assets/" + row.name + "/Settings.asset");
            Require(settings != null, "Settings did not survive Editor reopen.");
            Require(Validate(settings).SequenceEqual(row.profileLinks), "Saved native profile links did not survive reopen.");
        }
        var partialSettings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>("Assets/batch/Settings.asset");
        var partial = partialSettings.FindGroup("Owned partial");
        Require(partial != null && partial.GetSchema<BundledAssetGroupSchema>()?.Group == partial &&
            partial.GetSchema<ContentUpdateGroupSchema>()?.Group == partial, "Partial recovery schemas lost after reopen.");
        receipt.reopened = true;receipt.checks += checks;
        File.WriteAllText("batch-receipt.json", JsonUtility.ToJson(receipt, true));
        UnityEngine.Debug.Log("[Addressables batch witness] reopened checks=" + receipt.checks);
    }
}
