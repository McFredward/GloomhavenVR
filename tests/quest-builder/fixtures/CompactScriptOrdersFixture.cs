using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using GloomhavenVR.Quest.Editor;

// This Editor fixture executes the production code-only preparation boundary.
// Its generated plugin contains no game assets or copied game code.
public sealed class CompactOrderImports : AssetPostprocessor
{
    public static int pluginImports;
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] oldPaths)
    { pluginImports += imported.Count(path => path == "Assets/Plugins/OrderFixture.dll"); }
}

public static class CompactScriptOrdersFixture
{
    [Serializable] sealed class Result
    {
        public bool success, exactRoots, changedOrdersRestored, secondRunUnchanged,
            invalidRosterUnchanged, conflictingRosterUnchanged, nullRosterUnchanged;
        public int scriptCount, rowCount, firstImports, secondImports, legacyUnchangedImports;
        public double firstSeconds, secondSeconds;
        public string error;
        public List<string> progress = new List<string>();
    }
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static QuestCodeUpdateBuild.ScriptType Row(int index, int order)
    {
        return new QuestCodeUpdateBuild.ScriptType { assembly = "OrderFixture.dll", @namespace = "OrderFixture",
            @class = "Script" + index.ToString("D3"), executionOrder = order, propertiesHash = new string('a', 32) };
    }
    static string Metadata() { return File.ReadAllText("Assets/Plugins/OrderFixture.dll.meta"); }

    public static void Run()
    {
        var result = new Result { scriptCount = 128 };
        Application.logMessageReceived += (message, trace, kind) => {
            if (message.StartsWith("GHVRQ_PROGRESS ", StringComparison.Ordinal)) result.progress.Add(message.Substring(15));
        };
        try
        {
            var rows = Enumerable.Range(0, result.scriptCount).Select(i => Row(i, i == 0 ? -30 : i == 32 ? 45 : i == 127 ? 900 : 0)).ToList();
            // Repeated native bank entries must preserve roots, while importing
            // shared plugin metadata once rather than once per serialized row.
            rows.Add(Row(0, -30)); rows.Add(Row(32, 45)); rows.Add(Row(127, 900));
            result.rowCount = rows.Count;
            // The previous unconditional call really does import even when its
            // value is unchanged; establish that behavior in this same Editor.
            var baseline = MonoImporter.GetAllRuntimeMonoScripts().Single(script => script.GetClass() != null
                && script.GetClass().FullName == "OrderFixture.Script001");
            CompactOrderImports.pluginImports = 0;
            for (int i = 0; i < 3; ++i) MonoImporter.SetExecutionOrder(baseline, MonoImporter.GetExecutionOrder(baseline));
            result.legacyUnchangedImports = CompactOrderImports.pluginImports;
            Require(result.legacyUnchangedImports == 3, "The fixture did not observe the reported unconditional-import behavior.");
            CompactOrderImports.pluginImports = 0;
            var watch = Stopwatch.StartNew();
            var roots = QuestCodeUpdateBuild.PrepareOriginalScripts(rows.ToArray());
            result.firstSeconds = watch.Elapsed.TotalSeconds;
            result.firstImports = CompactOrderImports.pluginImports;
            result.exactRoots = roots.Count == rows.Count && roots.All(root => root is MonoScript)
                && roots[0] == roots[128] && roots[32] == roots[129] && roots[127] == roots[130];
            Require(result.exactRoots, "Exact ordered roots and repeated original entries were not preserved.");
            result.changedOrdersRestored = rows.Select((row, i) => MonoImporter.GetExecutionOrder((MonoScript)roots[i]) == row.executionOrder).All(value => value);
            Require(result.changedOrdersRestored, "Original script orders were not restored exactly.");
            Require(result.firstImports <= 1, "Changed orders reimported the same plugin more than once: " + result.firstImports);

            string metadata = Metadata();
            DateTime metadataTime = File.GetLastWriteTimeUtc("Assets/Plugins/OrderFixture.dll.meta");
            CompactOrderImports.pluginImports = 0;
            watch.Restart();
            var second = QuestCodeUpdateBuild.PrepareOriginalScripts(rows.ToArray());
            result.secondSeconds = watch.Elapsed.TotalSeconds;
            result.secondImports = CompactOrderImports.pluginImports;
            result.secondRunUnchanged = result.secondImports == 0 && metadata == Metadata()
                && metadataTime == File.GetLastWriteTimeUtc("Assets/Plugins/OrderFixture.dll.meta") && second.SequenceEqual(roots);
            Require(result.secondRunUnchanged, "Unchanged original orders triggered plugin work or changed roots.");

            var invalid = rows.ToArray(); invalid[1] = Row(1, -31); invalid[invalid.Length - 1] = Row(999, 0);
            bool rejected = false;
            try { QuestCodeUpdateBuild.PrepareOriginalScripts(invalid); } catch (InvalidDataException error) { rejected = error.Message.Contains("Retained original serialized script is missing"); }
            result.invalidRosterUnchanged = rejected && metadata == Metadata() && CompactOrderImports.pluginImports == 0;
            Require(result.invalidRosterUnchanged, "A missing original script mutated valid imported orders before rejection.");

            var conflicting = rows.ToArray(); conflicting[conflicting.Length - 1] = Row(0, 800);
            rejected = false;
            try { QuestCodeUpdateBuild.PrepareOriginalScripts(conflicting); } catch (InvalidDataException error) { rejected = error.Message.Contains("conflicting execution orders"); }
            result.conflictingRosterUnchanged = rejected && metadata == Metadata() && CompactOrderImports.pluginImports == 0;
            Require(result.conflictingRosterUnchanged, "Conflicting original orders were not rejected before mutation.");

            var nullRows = rows.ToArray(); nullRows[1] = Row(1, -31); nullRows[nullRows.Length - 1] = null;
            rejected = false;
            try { QuestCodeUpdateBuild.PrepareOriginalScripts(nullRows); } catch (InvalidDataException error) { rejected = error.Message.Contains("invalid row"); }
            result.nullRosterUnchanged = rejected && metadata == Metadata() && CompactOrderImports.pluginImports == 0;
            Require(result.nullRosterUnchanged, "An invalid original row mutated valid imported orders before rejection.");
            result.success = true;
        }
        catch (Exception error) { result.error = error.ToString(); }
        string destination = Environment.GetEnvironmentVariable("GHVR_QUEST_ORDER_FIXTURE_RESULT");
        File.WriteAllText(destination, JsonUtility.ToJson(result, true));
        if (!result.success) throw new InvalidOperationException(result.error);
        UnityEngine.Debug.Log("[Compact script order fixture] exact 131-row roster retained; targeted repairs and unchanged retry verified.");
    }
}
