#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;

/// <summary>Run in an isolated Unity project with the original-order fixture DLL and UGUI package.</summary>
public static class QuestScriptOrderFixture
{
    private static int checks, failureControls;

    [Serializable] private sealed class Report
    {
        public int checks, failureControls, extremeOrderReadback;
        public bool passed;
    }

    // Registry packages cannot persist changed importer metadata. This exercises the
    // genuine setter/readback failure and rollback after an earlier mutable DLL target.
    public static void RunReadOnlyPackageControl()
    {
        try
        {
            if (!File.Exists("quest-script-orders-fixture.marker"))
                throw new InvalidOperationException("Private registry-package fixture required.");
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            var authored = scripts.Single(s => s.GetClass() != null && s.GetClass().FullName == "OriginalOrderFixture.Authored");
            var package = scripts.Single(s => s.GetClass() != null && s.GetClass().FullName == "UnityEngine.UI.Button");
            MonoImporter.SetExecutionOrder(authored, 888);
            int packageBefore = MonoImporter.GetExecutionOrder(package);
            Directory.CreateDirectory("Assets/QuestOriginalStartup");
            Write(new QuestOriginalScriptOrders.Input { schema = 1, sourceSha256 = new string('a', 64),
                entries = new[] { Entry(authored, -10100, "1", false), Entry(package, packageBefore - 51, "2", true) },
                excluded = new QuestOriginalScriptOrders.Excluded[0] });
            bool rejected = false;
            try { QuestOriginalScriptOrders.RestoreAndVerify(); }
            catch (InvalidOperationException error) { rejected = error.Message.Contains("Unity changed the original script execution order"); }
            Check(rejected, "Read-only package silently lost the required original execution order.");
            Check(MonoImporter.GetExecutionOrder(authored) == 888, "Earlier DLL mutation was not rolled back.");
            Check(MonoImporter.GetExecutionOrder(package) == packageBefore, "Read-only package order changed.");
            Check(!File.Exists(QuestOriginalScriptOrders.ReceiptPath), "Rejected setter emitted a success receipt.");
            File.WriteAllText("script-order-readonly-result.json", JsonUtility.ToJson(new Report {
                checks = checks, failureControls = 1, passed = true }, true));
            Debug.Log("[Original script order fixture] Read-only package rejection and DLL rollback verified.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void Run()
    {
        try
        {
            if (!File.Exists("quest-script-orders-fixture.marker")) throw new InvalidOperationException("Private fixture marker missing.");
            var scripts = MonoImporter.GetAllRuntimeMonoScripts();
            Func<string, MonoScript> script = name => scripts.Single(s => s.GetClass() != null && s.GetClass().FullName == name);
            var authored = script("OriginalOrderFixture.Authored");
            var zero = script("OriginalOrderFixture.ExplicitZero");
            var late = script("OriginalOrderFixture.Late");
            var package = script("UnityEngine.UI.Button");
            MonoImporter.SetExecutionOrder(zero, 120);
            var input = new QuestOriginalScriptOrders.Input {
                schema = 1, sourceSha256 = new string('a', 64),
                entries = new[] { Entry(authored, -10100, "1", false), Entry(zero, 0, "2", false),
                    Entry(late, 32001, "3", false), Entry(package, -51, "4", true) },
                excluded = new[] { new QuestOriginalScriptOrders.Excluded { assemblyName = "Outside", fullName = "Outside.Component",
                    executionOrder = 24000, sourcePathIds = new[] { "5" }, reason = "assembly-outside-startup-closure" } }
            };
            Directory.CreateDirectory("Assets/QuestOriginalStartup");
            Write(input);
            QuestOriginalScriptOrders.RestoreAndVerify();
            Check(MonoImporter.GetExecutionOrder(authored) == -10100, "Authored early order lost.");
            Check(MonoImporter.GetExecutionOrder(zero) == 0, "Explicit zero did not replace the prior nonzero order.");
            Check(MonoImporter.GetExecutionOrder(late) == 32001, "Original 32001 order was clamped.");
            Check(MonoImporter.GetExecutionOrder(package) == -51, "Mapped package order lost.");
            var receipt = JsonUtility.FromJson<QuestOriginalScriptOrders.Receipt>(File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath));
            Check(receipt.allMappedOrdersVerified && receipt.restored.Length == 4 && receipt.excluded.Length == 1, "Receipt coverage differs.");
            Check(receipt.sourceSha256 == input.sourceSha256 && receipt.inputSha256.Length == 64, "Input provenance missing.");
            Check(receipt.restored.Single(r => r.fullName.EndsWith("ExplicitZero")).previousOrder == 120, "Prior zero-target order evidence missing.");
            Check(receipt.restored.Single(r => r.fullName == "UnityEngine.UI.Button").actualPath.StartsWith("Packages/"), "SDK package target did not retain actual import identity.");

            Reject(input, i => i.entries[0].fullName += "Missing");
            Reject(input, i => { i.entries[0].fullName += "Missing"; i.entries[0].executionOrder = 0; i.entries[0].referenced = true; });
            Reject(input, i => i.entries = i.entries.Concat(new[] { i.entries[0] }).ToArray());
            Reject(input, i => i.entries[0].originalGuid = new string('b', 32));
            Reject(input, i => i.entries[0].originalFileId = "922337203685477580");
            Reject(input, i => i.entries[0].pluginPath = "Assets/../Original.dll");
            Reject(input, i => i.entries[1].sourcePathIds = i.entries[0].sourcePathIds);
            Reject(input, i => i.excluded[0].referenced = true);
            Reject(input, i => i.excluded[0].reason = "speculative-skip");
            Reject(input, i => i.sourceSha256 = "not-a-source-hash");

            var unavailable = Clone(input);
            unavailable.entries = unavailable.entries.Concat(new[] { new QuestOriginalScriptOrders.Entry {
                assemblyName = "Plain", fullName = "Plain.Unreferenced", executionOrder = 0,
                sourcePathIds = new[] { "6" }, originalGuid = new string('c', 32), originalFileId = "1",
                pluginPath = "Assets/Plugins/Plain.dll", referenced = false } }).ToArray();
            Write(unavailable);
            QuestOriginalScriptOrders.RestoreAndVerify();
            receipt = JsonUtility.FromJson<QuestOriginalScriptOrders.Receipt>(File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath));
            Check(receipt.excluded.Any(e => e.fullName == "Plain.Unreferenced" && e.reason == "unavailable-to-MonoImporter"),
                "Missing unreferenced default-order type was not evidenced.");
            File.WriteAllText("script-order-fixture-result.json", JsonUtility.ToJson(new Report { checks = checks,
                failureControls = failureControls, extremeOrderReadback = MonoImporter.GetExecutionOrder(late), passed = true }, true));
            Debug.Log("[Original script order fixture] Passed " + checks + " checks and " + failureControls + " fail-closed controls.");
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static QuestOriginalScriptOrders.Entry Entry(MonoScript script, int order, string sourceId, bool package)
    {
        string guid;
        long id;
        Check(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out guid, out id), "Fixture import identity missing.");
        return new QuestOriginalScriptOrders.Entry { assemblyName = script.GetClass().Assembly.GetName().Name,
            fullName = script.GetClass().FullName, executionOrder = order, sourcePathIds = new[] { sourceId },
            originalGuid = guid, originalFileId = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            pluginPath = package ? "Assets/Plugins/UnityEngine.UI.dll" : AssetDatabase.GetAssetPath(script), package = package };
    }

    private static void Reject(QuestOriginalScriptOrders.Input input, Action<QuestOriginalScriptOrders.Input> change)
    {
        var invalid = Clone(input);
        change(invalid);
        Write(invalid);
        string before = File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath);
        var scripts = MonoImporter.GetAllRuntimeMonoScripts();
        var orders = scripts.Select(MonoImporter.GetExecutionOrder).ToArray();
        bool rejected = false;
        try { QuestOriginalScriptOrders.RestoreAndVerify(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid original-order input passed.");
        Check(before == File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath), "Rejected input changed the evidence receipt.");
        Check(scripts.Select(MonoImporter.GetExecutionOrder).SequenceEqual(orders), "Rejected mapping changed execution order.");
        failureControls++;
    }

    private static QuestOriginalScriptOrders.Input Clone(QuestOriginalScriptOrders.Input input)
    {
        return JsonUtility.FromJson<QuestOriginalScriptOrders.Input>(JsonUtility.ToJson(input));
    }

    private static void Write(QuestOriginalScriptOrders.Input input) { File.WriteAllText(QuestOriginalScriptOrders.InputPath, JsonUtility.ToJson(input)); }
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
#endif
