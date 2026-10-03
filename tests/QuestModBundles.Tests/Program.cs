using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GloomhavenVR;
using UnityEditor;
using UnityEngine;

string root = Path.Combine(Path.GetTempPath(), "quest-mod-bundles-" + Guid.NewGuid().ToString("N"));
string originalWorking = Directory.GetCurrentDirectory();
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
string[] required = (string[])typeof(QuestModBundles).GetField("RequiredAssets", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
void Setup(string name)
{
    string project = Path.Combine(root, name, "project"); Directory.CreateDirectory(project); Directory.SetCurrentDirectory(project);
    foreach (string path in required)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, "authored-" + path); File.WriteAllText(path + ".meta", "original-guid-" + path);
    }
    Directory.CreateDirectory("Assets/Bundle/TownServices"); File.WriteAllText("Assets/Bundle/TownServices/Unshipped.prefab", "separate campaign bank");
    File.WriteAllText("Assets/Bundle/README.md", "not an asset"); File.WriteAllText("Assets/Bundle/License.txt", "not an asset");
    File.WriteAllText("Assets/Bundle/.private.json", "not an asset"); File.WriteAllText("Assets/Bundle/Helper.cginc", "shader input");
    Directory.CreateDirectory("Build/Bundles"); File.WriteAllText("Build/Bundles/gloomhavenvr.bundle", "immutable-desktop-bank");
    Application.dataPath = Path.Combine(project, "Assets"); Application.unityVersion = "2021.3.5f1";
    EditorUserBuildSettings.activeBuildTarget = BuildTarget.Android; EditorApplication.exitCode = -1;
    AssetDatabase.extraDependency = "Assets/Bundle/Helper.cginc"; BuildPipeline.duringBuild = null; BuildPipeline.fail = false;
    Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", Path.Combine(root, name, "android-output"));
}
void Reject(string message)
{
    try { QuestModBundles.BuildAll(); Check(false, "Rejected case unexpectedly completed: " + message); }
    catch (InvalidOperationException) { Check(EditorApplication.exitCode == 1, "Failure did not return editor failure status: " + message); }
    string output = Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT");
    Check(!File.Exists(Path.Combine(output, "quest-mod-bundles.json")), "Failure published a successful bundle receipt: " + message);
}
try
{
    Setup("success"); QuestModBundles.BuildAll();
    string output = Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT");
    using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "quest-mod-bundles.json")));
    JsonElement receipt = evidence.RootElement;
    Check(EditorApplication.exitCode == 0 && receipt.GetProperty("target").GetString() == "Android", "Recipe did not prove Android target.");
    Check(PlayerSettings.colorSpace == ColorSpace.Linear && PlayerSettings.stereoRenderingPath == StereoRenderingPath.SinglePass
        && !PlayerSettings.useDefaultGraphics && PlayerSettings.graphicsApis.SequenceEqual(new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 }), "Authored shader build does not match Quest player's colour/stereo/GLES contract.");
    Check(BuildPipeline.lastTarget == BuildTarget.Android && !BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.DisableWriteTypeTree)
        && BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.ChunkBasedCompression) && BuildPipeline.lastOptions.HasFlag(BuildAssetBundleOptions.StrictMode), "Bundle recipe lost type trees, LZ4 or strict shader/import failures.");
    Check(BuildPipeline.lastBuilds.Length == 1 && BuildPipeline.lastBuilds[0].assetBundleName == "gloomhavenvr.bundle"
        && required.All(BuildPipeline.lastBuilds[0].assetNames.Contains), "Main bank paths or required authored assets were lost.");
    Check(!BuildPipeline.lastBuilds[0].assetNames.Any(p => p.Contains("TownServices") || p.EndsWith(".meta") || p.EndsWith(".md") || p.EndsWith(".txt") || p.EndsWith(".cginc") || Path.GetFileName(p).StartsWith('.')), "Bookkeeping or distinct town bank entered main bundle.");
    Check(File.ReadAllText("Build/Bundles/gloomhavenvr.bundle") == "immutable-desktop-bank", "Android recipe overwrote desktop output.");
    Check(receipt.GetProperty("sourceFiles").EnumerateArray().Any(p => p.GetProperty("path").GetString() == "Assets/Bundle/Helper.cginc")
        && receipt.GetProperty("sourceFiles").EnumerateArray().Any(p => p.GetProperty("path").GetString().EndsWith(".meta")), "Dependency inputs and authored GUID bytes lack hash provenance.");
    Check(receipt.GetProperty("bundle").GetProperty("sha256").GetString() == Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3, 4 })).ToLowerInvariant()
        && receipt.GetProperty("bundle").GetProperty("size").GetInt64() == 4, "Output hash/size does not match built bank.");
    Check(receipt.GetProperty("builtinDependencies").EnumerateArray().Any(p => p.GetString() == "Resources/unity_builtin_extra"), "Built-in dependencies were silently omitted.");
    foreach (string mutation in new[] { "editor", "target", "missing-asset", "town-dependency", "source-change", "failed-build", "existing-output", "desktop-output" })
    {
        Setup(mutation);
        if (mutation == "editor") Application.unityVersion = "2021.3.99f1";
        if (mutation == "target") EditorUserBuildSettings.activeBuildTarget = BuildTarget.StandaloneWindows64;
        if (mutation == "missing-asset") File.Delete(required[0]);
        if (mutation == "town-dependency") AssetDatabase.extraDependency = "Assets/Bundle/TownServices/Unshipped.prefab";
        if (mutation == "source-change") BuildPipeline.duringBuild = () => File.WriteAllText(required[0], "concurrently changed authored source");
        if (mutation == "failed-build") BuildPipeline.fail = true;
        if (mutation == "existing-output") { Directory.CreateDirectory(Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT")); File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT"), "foreign"), "keep"); }
        if (mutation == "desktop-output") Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", Path.GetFullPath("Build/Bundles"));
        Reject(mutation);
        Check(File.ReadAllText("Build/Bundles/gloomhavenvr.bundle") == "immutable-desktop-bank", "Rejected recipe touched desktop bank: " + mutation);
    }
    Console.WriteLine("Quest authored mod bundle recipe: " + checks + " assertions passed. Actual Android shader/serialization and headset rendering remain separate proofs.");
}
finally
{
    Directory.SetCurrentDirectory(originalWorking); Environment.SetEnvironmentVariable("GHVR_QUEST_MOD_BUNDLE_OUTPUT", null);
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
