using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

string root = Path.Combine(Path.GetTempPath(), "quest-mod-bundles-" + Guid.NewGuid().ToString("N"));
string originalWorking = Directory.GetCurrentDirectory();
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
string[] required = (string[])typeof(QuestModBundles).GetField("RequiredAssets", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
string[] requiredTown = (string[])typeof(QuestModBundles).GetField("RequiredTownAssets", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
const string Town = "Assets/Bundle/TownServices";
string[] voices = { Town + "/Audio/Greeting.WAV", Town + "/Audio/de/Greeting.wav", Town + "/Audio/voices.json" };
void WriteAsset(string path)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    File.WriteAllText(path, "authored-" + path); File.WriteAllText(path + ".meta", "original-guid-" + path);
}
GameObject Prefab(string path)
{
    var prefab = new GameObject { name = Path.GetFileNameWithoutExtension(path) };
    prefab.transform.meshFilters.Add(new MeshFilter { name = "AuthoredMesh" });
    string furniture = path.EndsWith("TownPriestess.prefab", StringComparison.Ordinal) ? "Shrine"
        : path.EndsWith("TownEnchantress.prefab", StringComparison.Ordinal) ? "Workbench" : "";
    if (furniture.Length != 0)
    {
        var child = new Transform { name = furniture };
        for (int i = 0; i < (furniture == "Shrine" ? 2 : 1); i++)
            child.children.Add(new Transform { name = "Runner" + i, meshFilters = { new MeshFilter { name = "ClothRunner_" + i } } });
        prefab.transform.children.Add(child);
    }
    return prefab;
}
void Setup(string name, bool fullGame = false)
{
    string project = Path.Combine(root, name, "project"); Directory.CreateDirectory(project); Directory.SetCurrentDirectory(project);
    AssetDatabase.imported.Clear(); AssetDatabase.dependencies.Clear(); BuildPipeline.bundleDependencies.Clear();
    foreach (string path in required.Concat(requiredTown).Concat(voices))
    {
        WriteAsset(path);
        if (path.StartsWith(Town, StringComparison.Ordinal))
        {
            if (path.EndsWith(".prefab", StringComparison.Ordinal)) AssetDatabase.imported[path] = Prefab(path);
            else if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) AssetDatabase.imported[path] = new AudioClip();
            else if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) AssetDatabase.imported[path] = new TextAsset();
        }
    }
    // Current mod additions must enter their proper bank without editing the recipe.
    string extra = Town + "/Prefabs/TownNewNpc.prefab"; WriteAsset(extra); AssetDatabase.imported[extra] = Prefab(extra);
    WriteAsset("Assets/Bundle/NewAuthoredHand.prefab");
    File.WriteAllText("Assets/Bundle/README.md", "not an asset"); File.WriteAllText("Assets/Bundle/License.txt", "not an asset");
    File.WriteAllText("Assets/Bundle/.private.json", "not an asset"); File.WriteAllText("Assets/Bundle/Helper.cginc", "main shader input");
    File.WriteAllText(Town + "/Audio/License.txt", "voice provenance, not an audio asset");
    File.WriteAllText(Town + "/Shaders/Shared.cginc", "town shader input");
    WriteAsset("Assets/Scripts/TownActor.cs"); WriteAsset("Assets/Scripts/Town.asmdef");
    Directory.CreateDirectory("Packages"); File.WriteAllText("Packages/manifest.json", "immutable package declaration");
    Directory.CreateDirectory("ProjectSettings"); File.WriteAllText("ProjectSettings/ProjectVersion.txt", "2021.3.5f1");
    Directory.CreateDirectory("Build/Bundles"); File.WriteAllText("Build/Bundles/gloomhavenvr.bundle", "immutable-desktop-bank");
    Application.dataPath = Path.Combine(project, "Assets"); Application.unityVersion = "2021.3.5f1";
    EditorUserBuildSettings.activeBuildTarget = BuildTarget.Android; EditorApplication.exitCode = -1;
    AssetDatabase.extraDependency = "Assets/Bundle/Helper.cginc";
    BuildPipeline.duringBuild = null; BuildPipeline.fail = false; BuildPipeline.manifestOverride = null;
    Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", Path.Combine(root, name, "android-output"));
    Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_FULL_GAME", fullGame ? "1" : null);
}
void Reject(string message)
{
    try { QuestModBundles.BuildAll(); Check(false, "Rejected case unexpectedly completed: " + message); }
    catch (InvalidOperationException) { Check(EditorApplication.exitCode == 1, "Failure did not return editor failure status: " + message); }
    string output = Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT");
    Check(!File.Exists(Path.Combine(output, "quest-mod-bundles.json")), "Failure published a successful bundle receipt: " + message);
}
JsonDocument Receipt() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT"), "quest-mod-bundles.json")));
string[] Strings(JsonElement value) => value.EnumerateArray().Select(item => item.GetString()).ToArray();
void Common(JsonElement receipt, GraphicsDeviceType api)
{
    Check(EditorApplication.exitCode == 0 && receipt.GetProperty("target").GetString() == "Android", "Recipe did not prove Android target.");
    Check(PlayerSettings.colorSpace == ColorSpace.Linear && PlayerSettings.stereoRenderingPath == StereoRenderingPath.SinglePass
        && !PlayerSettings.useDefaultGraphics && PlayerSettings.graphicsApis.SequenceEqual(new[] { api })
        && receipt.GetProperty("graphicsApi").GetString() == api.ToString(), "Authored compiler backend/color contract differs.");
    Check(BuildPipeline.lastTarget == BuildTarget.Android && !BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.DisableWriteTypeTree)
        && BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.ChunkBasedCompression)
        && BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.StrictMode)
        && BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.ForceRebuildAssetBundle), "Bundle recipe lost type trees, LZ4, clean rebuilding or strict failures.");
    var main = BuildPipeline.lastBuilds.Single(bank => bank.assetBundleName == "gloomhavenvr.bundle");
    Check(required.All(main.assetNames.Contains) && main.assetNames.Contains("Assets/Bundle/NewAuthoredHand.prefab"), "Main bank omitted required/new authored assets.");
    Check(!main.assetNames.Any(p => p.Contains("TownServices") || p.EndsWith(".meta") || p.EndsWith(".md") || p.EndsWith(".txt") || p.EndsWith(".cginc") || Path.GetFileName(p).StartsWith('.')), "Bookkeeping or town bank entered main bundle.");
    Check(File.ReadAllText("Build/Bundles/gloomhavenvr.bundle") == "immutable-desktop-bank", "Android recipe overwrote desktop output.");
    string[] sources = receipt.GetProperty("sourceFiles").EnumerateArray().Select(item => item.GetProperty("path").GetString()).ToArray();
    Check(sources.Contains("Assets/Bundle/Helper.cginc") && sources.Any(p => p.EndsWith(".meta")), "Shader input or GUID bytes lack hash provenance.");
    Check(Strings(receipt.GetProperty("builtinDependencies")).Contains("Resources/unity_builtin_extra"), "Built-in dependency was silently omitted.");
    foreach (var bank in receipt.GetProperty("banks").EnumerateArray())
    {
        string name = bank.GetProperty("bundleName").GetString(); var file = bank.GetProperty("bundle"); byte[] bytes = BuildPipeline.bundleBytes[name];
        Check(file.GetProperty("path").GetString() == name && file.GetProperty("size").GetInt64() == bytes.Length
            && file.GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Bank hash/size/path differs: " + name);
    }
}
try
{
    Setup("startup"); QuestModBundles.BuildAll();
    using (var document = Receipt())
    {
        JsonElement receipt = document.RootElement; Common(receipt, GraphicsDeviceType.OpenGLES3);
        Check(BuildPipeline.lastBuilds.Length == 1 && !receipt.GetProperty("townBanksIncluded").GetBoolean()
            && receipt.GetProperty("bundles").GetArrayLength() == 1, "Startup unexpectedly acquired campaign banks.");
        Check(!receipt.GetProperty("sourceFiles").EnumerateArray().Any(item => item.GetProperty("path").GetString().StartsWith(Town, StringComparison.Ordinal)), "Startup hashed unowned campaign input.");
    }
    Setup("campaign", true);
    BuildPipeline.bundleDependencies["ghvr-town-voices.bundle"] = new[] { "ghvr-town.bundle" };
    QuestModBundles.BuildAll();
    using (var document = Receipt())
    {
        JsonElement receipt = document.RootElement; Common(receipt, GraphicsDeviceType.Vulkan);
        Check(BuildPipeline.lastBuilds.Length == 3 && receipt.GetProperty("townBanksIncluded").GetBoolean()
            && receipt.GetProperty("bundles").GetArrayLength() == 3, "Campaign did not ship all three banks.");
        var art = BuildPipeline.lastBuilds.Single(bank => bank.assetBundleName == "ghvr-town.bundle");
        var voice = BuildPipeline.lastBuilds.Single(bank => bank.assetBundleName == "ghvr-town-voices.bundle");
        Check(requiredTown.All(art.assetNames.Contains) && art.assetNames.Contains(Town + "/Prefabs/TownNewNpc.prefab")
            && !art.assetNames.Any(p => p.Contains("/Audio/") || p.EndsWith(".meta") || p.EndsWith(".cginc")), "Town art selection lost required/current authored paths or included another bank.");
        Check(voice.assetNames.OrderBy(path => path, StringComparer.Ordinal).SequenceEqual(voices.OrderBy(path => path, StringComparer.Ordinal)), "Voice selection lost recursive/uppercase WAV or JSON, or packed bookkeeping.");
        Check(receipt.GetProperty("banks").EnumerateArray().Single(bank => bank.GetProperty("bundleName").GetString() == "ghvr-town-voices.bundle")
            .GetProperty("dependencies")[0].GetString() == "ghvr-town.bundle", "Valid bank dependency was not recorded.");
        string[] sources = receipt.GetProperty("sourceFiles").EnumerateArray().Select(item => item.GetProperty("path").GetString()).ToArray();
        Check(new[] { Town + "/Shaders/Shared.cginc", "Assets/Scripts/TownActor.cs", "Assets/Scripts/Town.asmdef", "Packages/manifest.json", "ProjectSettings/ProjectVersion.txt" }
            .All(sources.Contains), "Full campaign provenance omitted compiler/assembly/package/editor inputs.");
    }
    foreach (string mutation in new[] { "editor", "target", "missing-asset", "town-dependency", "source-change", "failed-build", "existing-output", "desktop-output" })
    {
        Setup(mutation);
        if (mutation == "editor") Application.unityVersion = "2021.3.99f1";
        if (mutation == "target") EditorUserBuildSettings.activeBuildTarget = BuildTarget.StandaloneWindows64;
        if (mutation == "missing-asset") File.Delete(required[0]);
        if (mutation == "town-dependency") AssetDatabase.extraDependency = Town + "/Prefabs/TownMerchant.prefab";
        if (mutation == "source-change") BuildPipeline.duringBuild = () => File.WriteAllText(required[0], "concurrently changed authored source");
        if (mutation == "failed-build") BuildPipeline.fail = true;
        if (mutation == "existing-output") { Directory.CreateDirectory(Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT")); File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT"), "foreign"), "keep"); }
        if (mutation == "desktop-output") Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", Path.GetFullPath("Build/Bundles"));
        Reject(mutation);
        Check(File.ReadAllText("Build/Bundles/gloomhavenvr.bundle") == "immutable-desktop-bank", "Rejected recipe touched desktop bank: " + mutation);
    }
    foreach (string mutation in new[] { "invalid-mode", "missing-town", "no-voices", "no-voice-metadata", "voice-import", "metadata-import", "prefab-import", "missing-mesh", "missing-furniture", "oversized-furniture", "runner-count", "script-source-change", "manifest-bank-missing", "unshipped-bank-dependency" })
    {
        Setup("campaign-" + mutation, true);
        var priestess = (GameObject)AssetDatabase.imported[Town + "/Prefabs/TownPriestess.prefab"];
        if (mutation == "invalid-mode") Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_FULL_GAME", "yes");
        if (mutation == "missing-town") File.Delete(requiredTown[0]);
        if (mutation == "no-voices") foreach (string voice in voices.Where(path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))) File.Delete(voice);
        if (mutation == "no-voice-metadata") File.Delete(voices[2]);
        if (mutation == "voice-import") AssetDatabase.imported.Remove(voices[0]);
        if (mutation == "metadata-import") AssetDatabase.imported[voices[2]] = new AudioClip();
        if (mutation == "prefab-import") AssetDatabase.imported.Remove(Town + "/Prefabs/TownMerchant.prefab");
        if (mutation == "missing-mesh") priestess.transform.meshFilters[0].sharedMesh = null;
        if (mutation == "missing-furniture") priestess.transform.children.Clear();
        if (mutation == "oversized-furniture") for (int i = 0; i < 129; i++) priestess.transform.Find("Shrine").children.Add(new Transform { name = "Extra" + i });
        if (mutation == "runner-count") priestess.transform.Find("Shrine").children.RemoveAt(0);
        if (mutation == "script-source-change") BuildPipeline.duringBuild = () => File.WriteAllText("Assets/Scripts/TownActor.cs", "concurrent script change");
        if (mutation == "manifest-bank-missing") BuildPipeline.manifestOverride = new[] { "gloomhavenvr.bundle", "ghvr-town.bundle" };
        if (mutation == "unshipped-bank-dependency") BuildPipeline.bundleDependencies["ghvr-town.bundle"] = new[] { "unshipped.bundle" };
        Reject(mutation);
        Check(File.ReadAllText("Build/Bundles/gloomhavenvr.bundle") == "immutable-desktop-bank", "Campaign rejection changed desktop bank: " + mutation);
    }
    Console.WriteLine("Quest authored mod bundle recipe: " + checks + " controlled assertions passed. Startup GLES and full-game Vulkan paths exercised; actual Android serialization/shaders and headset rendering remain separate proofs.");
}
finally
{
    Directory.SetCurrentDirectory(originalWorking);
    Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", null);
    Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_FULL_GAME", null);
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
