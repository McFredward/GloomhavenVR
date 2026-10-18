using System.Reflection;
using System.Security.Cryptography;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;

string previousWorking = Directory.GetCurrentDirectory();
string temporary = Path.Combine(Path.GetTempPath(), "quest-order-consumer-" + Guid.NewGuid().ToString("N"));
int checks = 0, rejected = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
T Clone<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));
void Write(string path, string text)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text);
}
QuestOriginalScriptOrders.SourceFile Record(string path) => new() {
    path = path, sha256 = Hash(File.ReadAllBytes(path)), size = new FileInfo(path).Length };
void Setup(string name)
{
    string directory = Path.Combine(temporary, name); Directory.CreateDirectory(directory); Directory.SetCurrentDirectory(directory);
    MonoImporter.scripts = new[] { new MonoScript { type = typeof(Authored), path = "Assets/Plugins/Fixture.dll",
        guid = new string('b', 32), fileId = 17, order = 77 } };
    MonoImporter.writes = 0;
    Write(QuestOriginalScriptOrders.ReceiptPath, "previous-receipt");
}
QuestOriginalScriptOrders.Input Input(string reason = "assembly-outside-campaign-closure") => new() {
    schema = 1, sourceSha256 = new string('a', 64),
    entries = new[] { new QuestOriginalScriptOrders.Entry {
        assemblyName = typeof(Authored).Assembly.GetName().Name, fullName = typeof(Authored).FullName,
        pluginPath = "Assets/Plugins/Fixture.dll", originalGuid = new string('b', 32), originalFileId = "17",
        sourcePathIds = new[] { "1" }, executionOrder = -10100, referenced = true } },
    excluded = new[] { new QuestOriginalScriptOrders.Excluded { assemblyName = "UnityEngine.TestRunner",
        fullName = "UnityEngine.TestTools.AssemblyInfo", sourcePathIds = new[] { "5627" }, reason = reason } } };
QuestOriginalScriptOrders.SourceProof Stage(QuestOriginalScriptOrders.Input input, bool evidence = true)
{
    Write(QuestOriginalScriptOrders.InputPath, JsonUtility.ToJson(input));
    if (!evidence) return null;
    string bindings = "Assets/QuestOriginalCampaign/script-bindings.json";
    Write(bindings, "{\"schema\":1,\"disabledPluginGuids\":[]}");
    var proof = new QuestOriginalScriptOrders.SourceProof {
        schema = 1, scope = "original campaign closure; exact imported scripts verified after SDK remapping",
        sourceDistinctCount = input.entries.Length + input.excluded.Length,
        sourceRecordCount = input.entries.Length + input.excluded.Length,
        source = new QuestOriginalScriptOrders.SourceFile { path = "globalgamemanagers.assets", sha256 = input.sourceSha256, size = 100 },
        manifest = Record(QuestOriginalScriptOrders.InputPath), bindingManifest = Record(bindings), excluded = Clone(input.excluded) };
    Write(QuestOriginalScriptOrders.SourceProofPath, JsonUtility.ToJson(proof)); return proof;
}
void Reject(string name, Action<QuestOriginalScriptOrders.Input, QuestOriginalScriptOrders.SourceProof> change,
    bool evidence = true, bool restageInputHash = false)
{
    Setup(name); var input = Input(); var proof = Stage(input, evidence); change(input, proof);
    Write(QuestOriginalScriptOrders.InputPath, JsonUtility.ToJson(input));
    if (proof != null)
    {
        if (restageInputHash) proof.manifest = Record(QuestOriginalScriptOrders.InputPath);
        Write(QuestOriginalScriptOrders.SourceProofPath, JsonUtility.ToJson(proof));
    }
    bool failed = false;
    try { QuestOriginalScriptOrders.RestoreAndVerify(); }
    catch (InvalidOperationException) { failed = true; }
    Check(failed, "Defect passed: " + name);
    Check(MonoImporter.writes == 0 && MonoImporter.scripts[0].order == 77, "Rejected input changed importer orders: " + name);
    Check(File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath) == "previous-receipt", "Rejected input replaced receipt: " + name);
    rejected++;
}
try
{
    Setup("campaign"); var input = Input(); Stage(input); QuestOriginalScriptOrders.RestoreAndVerify();
    var receipt = JsonUtility.FromJson<QuestOriginalScriptOrders.Receipt>(File.ReadAllText(QuestOriginalScriptOrders.ReceiptPath));
    Check(receipt.allMappedOrdersVerified && receipt.restored.Length == 1 && receipt.excluded.Length == 1, "Campaign receipt coverage failed.");
    Check(receipt.excluded[0].reason == "assembly-outside-campaign-closure" && MonoImporter.scripts[0].order == -10100,
        "Campaign source-backed exclusion lost authored order or reason.");

    Setup("startup"); input = Input("assembly-outside-startup-closure"); input.excluded[0].assemblyName = "Outside";
    input.excluded[0].executionOrder = 24000; Stage(input, false); QuestOriginalScriptOrders.RestoreAndVerify();
    Check(MonoImporter.scripts[0].order == -10100, "Existing Startup contract changed.");
    Setup("top-level"); input = Input("no-original-top-level-type"); Stage(input, false); QuestOriginalScriptOrders.RestoreAndVerify();
    Check(MonoImporter.scripts[0].order == -10100, "Existing top-level-type exclusion changed.");

    Reject("missing-source", (i,p) => { }, evidence: false);
    Reject("startup-scope", (i,p) => p.scope = "original startup closure; exact imported scripts verified after SDK remapping");
    Reject("unknown-scope", (i,p) => p.scope = "campaign");
    Reject("source-schema", (i,p) => p.schema = 2);
    Reject("source-sha", (i,p) => p.source.sha256 = new string('c', 64));
    Reject("source-path", (i,p) => p.source.path = "resources.assets");
    Reject("source-size", (i,p) => p.source.size = 0);
    Reject("manifest-path", (i,p) => p.manifest.path = "Assets/Other/script-orders.json");
    Reject("manifest-sha", (i,p) => p.manifest.sha256 = new string('c', 64));
    Reject("manifest-size", (i,p) => p.manifest.size++);
    Reject("startup-binding", (i,p) => p.bindingManifest.path = "Assets/QuestOriginalStartup/script-bindings.json");
    Reject("binding-sha", (i,p) => p.bindingManifest.sha256 = new string('c', 64));
    Reject("binding-size", (i,p) => p.bindingManifest.size++);
    Reject("binding-missing", (i,p) => File.Delete(p.bindingManifest.path));
    Reject("source-count", (i,p) => p.sourceDistinctCount++);
    Reject("source-record-count", (i,p) => p.sourceRecordCount = 0);
    Reject("missing-source-row", (i,p) => p.excluded = Array.Empty<QuestOriginalScriptOrders.Excluded>());
    Reject("wrong-source-reason", (i,p) => p.excluded[0].reason = "assembly-outside-startup-closure");
    Reject("wrong-source-id", (i,p) => p.excluded[0].sourcePathIds = new[] { "9999" });
    Reject("duplicate-source-row", (i,p) => p.excluded = new[] { p.excluded[0], p.excluded[0] });
    Reject("referenced-input", (i,p) => i.excluded[0].referenced = true, restageInputHash: true);
    Reject("referenced-source", (i,p) => p.excluded[0].referenced = true);
    Reject("nonzero-input", (i,p) => i.excluded[0].executionOrder = 1, restageInputHash: true);
    Reject("nonzero-source", (i,p) => p.excluded[0].executionOrder = 1);
    Reject("unrelated-assembly", (i,p) => { i.excluded[0].assemblyName = "Required.Game"; p.excluded[0].assemblyName = "Required.Game"; }, restageInputHash: true);
    Reject("unknown-reason", (i,p) => i.excluded[0].reason = "optional-campaign-script", restageInputHash: true);
    Reject("referenced-target-missing", (i,p) => i.entries[0].fullName = "Missing.Component", restageInputHash: true);
    Reject("duplicate-source-id", (i,p) => { i.excluded[0].sourcePathIds = i.entries[0].sourcePathIds; p.excluded[0].sourcePathIds = i.entries[0].sourcePathIds; }, restageInputHash: true);
    Reject("plugin-guid", (i,p) => i.entries[0].originalGuid = new string('c', 32), restageInputHash: true);

    if (args.Length != 0)
    {
        // The actual completed source receipt can be inspected without launching
        // Unity, changing its project, or invoking original gameplay assemblies.
        string source = Path.GetFullPath(args[0]); Setup("actual-source-preflight");
        foreach (string path in new[] { QuestOriginalScriptOrders.InputPath, QuestOriginalScriptOrders.SourceProofPath,
            "Assets/QuestOriginalCampaign/script-bindings.json" })
            Write(path, File.ReadAllText(Path.Combine(source, path)));
        input = JsonUtility.FromJson<QuestOriginalScriptOrders.Input>(File.ReadAllText(QuestOriginalScriptOrders.InputPath));
        var validate = typeof(QuestOriginalScriptOrders).GetMethod("ValidateCampaignExclusions", BindingFlags.NonPublic | BindingFlags.Static);
        validate.Invoke(null, new object[] { input, File.ReadAllBytes(QuestOriginalScriptOrders.InputPath) });
        int count = input.excluded.Count(row => row.reason == "assembly-outside-campaign-closure");
        Check(count == 113 && input.excluded.Where(row => row.reason == "assembly-outside-campaign-closure")
            .All(row => row.assemblyName == "UnityEngine.TestRunner" && !row.referenced && row.executionOrder == 0),
            "Completed full-build witness differs from its diagnosed 113 exclusions.");
        Console.WriteLine("Actual full-build source preflight accepted " + count + " exact TestRunner exclusions; no native import claimed.");
    }
    Console.WriteLine("Script order consumer passed " + checks + " checks and " + rejected + " rejection controls; Unity APIs are fixture stubs.");
}
finally { Directory.SetCurrentDirectory(previousWorking); if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }

public sealed class Authored : MonoBehaviour { }
