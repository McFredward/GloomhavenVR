using GloomhavenVR.Core;
using UnityEngine;

if (args.Length == 2 && args[0] == "--assembly")
{
    using var pe = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(args[1]));
    var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
    var data = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress).GetContent();
    var embedded = new Dictionary<string, byte[]>();
    foreach (var handle in reader.ManifestResources)
    {
        var resource = reader.GetManifestResource(handle);
        if (!resource.Implementation.IsNil) continue;
        int offset = checked((int)resource.Offset);
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        embedded.Add(reader.GetString(resource.Name), data.AsSpan(offset + 4, length).ToArray());
    }
    byte[] bank = embedded[QuestLoadingAssets.BundleResource];
    var contract = QuestEnvironmentJson.Read(embedded[QuestLoadingAssets.ProvenanceResource]);
    string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bank)).ToLowerInvariant();
    if (QuestEnvironmentJson.Integer(contract, "bundleBytes") != bank.Length
        || QuestEnvironmentJson.String(contract, "bundleSha256") != hash
        || QuestEnvironmentJson.String(contract, "target") != "Android"
        || QuestEnvironmentJson.String(contract, "graphicsApi") != "Vulkan"
        || QuestEnvironmentJson.String(contract, "shaderName") != "GloomhavenVR/QuestLoadingSpinner")
        throw new Exception("Actual compiled plugin resource/provenance closure failed.");
    Console.WriteLine("PASS actual compiled plugin embedded loading resources: " + bank.Length + " bytes; " + hash);
    return;
}

string mode = args.Single();
int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
AssetBundle.Mode = mode;
Check(QuestLoadingAssets.ResolveShader() == null && AssetBundle.Loads == 0 && VRLog.Notes.Count == 0,
    "unconfigured startup and desktop never open the Android bank or latch a failure");
QuestStandalonePlatform.Enabled = true;
if (mode == "wrong-api") SystemInfo.graphicsDeviceType = UnityEngine.Rendering.GraphicsDeviceType.OpenGLCore;
if (mode == "shared-eye") UnityEngine.XR.XRSettings.stereoRenderingMode = UnityEngine.XR.XRSettings.StereoRenderingMode.SinglePassInstanced;
Shader? shader = QuestLoadingAssets.ResolveShader();
if (mode == "success")
{
    Check(shader != null && shader.name == "GloomhavenVR/QuestLoadingSpinner", "exact embedded shader resolves after platform adoption");
    Check(ReferenceEquals(shader, QuestLoadingAssets.ResolveShader()) && AssetBundle.Loads == 1, "successful bank is loaded once and shader cache is reused");
    Check(VRLog.Notes.Count == 1 && VRLog.Notes[0].Contains("ready from"), "one useful resource lifecycle note is retained");
    QuestStandalonePlatform.Enabled = false;
    Check(QuestLoadingAssets.ResolveShader() == null, "platform gate also protects previously loaded resources");
}
else
{
    Check(shader == null, "resource or native-bank failure retains the caller's native artwork fallback");
    int loads = AssetBundle.Loads, notes = VRLog.Notes.Count;
    Check(QuestLoadingAssets.ResolveShader() == null && AssetBundle.Loads == loads && VRLog.Notes.Count == notes && notes == 1,
        "permanent resource failure is bounded and never retried every frame");
    Check(VRLog.Notes[0].Contains("unavailable") && VRLog.Notes[0].Contains("fallback"), "failure context explicitly describes retained fallback");
    if (new[] { "missing-bank", "missing-provenance", "wrong-hash", "wrong-target", "wrong-api", "shared-eye" }.Contains(mode))
        Check(AssetBundle.Loads == 0, "invalid embedded provenance/routing fails before native bank loading");
    if (new[] { "wrong-bank", "wrong-path", "missing-shader", "wrong-shader", "unsupported" }.Contains(mode))
        Check(AssetBundle.Unloads == 1, "only the newly owned invalid bank is disposed");
}
Console.WriteLine($"Quest loading assets {mode}: {checks} assertions passed.");
