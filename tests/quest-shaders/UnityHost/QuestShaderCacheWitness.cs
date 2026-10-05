#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GloomhavenVR.Quest.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Bounded authored fixture. This does not claim original-game or headset pixel parity.</summary>
public static class QuestShaderCacheWitness
{
    const string ShaderPath = "Assets/QuestOriginalCampaign/CacheFixture.shader";
    const string IncludePath = "Assets/QuestOriginalCampaign/ShaderPrograms/CacheFixture.hlsl";
    const string MaterialPath = "Assets/QuestOriginalCampaign/CacheFixture.mat";
    const string Output = "CacheWitness";
    const string ManifestPath = QuestCampaignShaderValidation.DefaultManifest;
    static readonly List<string> controls = new List<string>();
    [Serializable] sealed class Result { public int schema=1, firstNativeQueries, unchangedNativeQueries, changedValidations; public bool passed, realUnityVulkanCompiler, originalGamePixelsVerified, headsetVerified; public string unityVersion; public string[] controls; }
    static int changedValidations;
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException("CACHE WITNESS: " + message); }
    static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, name + " was accepted"); controls.Add(name);
    }
    static void WriteManifest(QuestCampaignShaderValidation.Manifest input)
    {
        input.shaders[0].sourceSha256 = QuestCampaignShaderCache.FileHash(ShaderPath);
        input.programs[0].sourceSha256 = QuestCampaignShaderCache.FileHash(IncludePath);
        File.WriteAllText(ManifestPath, JsonUtility.ToJson(input, true) + "\n", new UTF8Encoding(false));
    }
    static void Native(QuestCampaignShaderValidation.Manifest input, string control)
    {
        WriteManifest(input);
        QuestCampaignShaderValidation.Validate(ManifestPath, Output);
        Assert(!QuestCampaignShaderValidation.LastValidationCacheReused && QuestCampaignShaderValidation.LastNativeCompileCount == 2, control + " did not really query both Vulkan aliases");
        ++changedValidations; controls.Add(control);
    }
    public static void Run()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] {GraphicsDeviceType.Vulkan});
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.stereoRenderingPath = StereoRenderingPath.MultiPass;
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        Assert(shader != null && shader.name == "QuestPrivate/Cache", "authored shader import");
        AssetDatabase.CreateAsset(new Material(shader), MaterialPath); AssetDatabase.SaveAssets();
        string shaderGuid = AssetDatabase.AssetPathToGUID(ShaderPath), materialGuid = AssetDatabase.AssetPathToGUID(MaterialPath);
        var input = new QuestCampaignShaderValidation.Manifest {
            schema=1, scope="campaign", graphicsApi="Vulkan", compilerPlatform="Vulkan", requiredShaderCount=1, requiredMaterialCount=1, requiredHostRenderTargetCount=1,
            shaders=new[] {new QuestCampaignShaderValidation.OriginalShader {guid=shaderGuid, assetPath=ShaderPath, originalName="QuestPrivate/Cache", originalSerializedFile="authored-cache-fixture", originalPathId=1,
                variants=new[] {new QuestCampaignShaderValidation.Variant {subshader=0,pass=0,hardwareTier=2,stereo="mono",passType="Normal",keywords=new string[0],vertexOriginalDxbcSha256=new string('1',64),fragmentOriginalDxbcSha256=new string('2',64)},
                    new QuestCampaignShaderValidation.Variant {subshader=0,pass=0,hardwareTier=2,stereo="mono",passType="Normal",keywords=new[]{"FIXTURE_VARIANT"},vertexOriginalDxbcSha256=new string('1',64),fragmentOriginalDxbcSha256=new string('3',64)}}}},
            materials=new[] {new QuestCampaignShaderValidation.OriginalMaterial {guid=materialGuid,assetPath=MaterialPath,shaderGuid=shaderGuid}},
            renderCases=new[] {new QuestCampaignShaderValidation.RenderCase {id="authored-cache",shaderGuid=shaderGuid,materialGuid=materialGuid,meshGuid=new string('4',32)}},
            programs=new[] {new QuestCampaignShaderValidation.SourceProgram {assetPath=IncludePath,originalDxbcSha256=new string('2',64),originalInterfaceSha256=new string('5',64)}}
        };
        WriteManifest(input);
        QuestCampaignShaderValidation.Validate(ManifestPath, Output);
        int first = QuestCampaignShaderValidation.LastNativeCompileCount;
        Assert(first == 2 && !QuestCampaignShaderValidation.LastValidationCacheReused, "first actual validation");
        QuestCampaignShaderValidation.Validate(ManifestPath, Output);
        Assert(QuestCampaignShaderValidation.LastNativeCompileCount == 0 && QuestCampaignShaderValidation.LastValidationCacheReused, "unchanged reuse must perform zero native queries");
        controls.Add("unchanged-completed-hit-zero-native-queries");
        var closure = QuestCampaignShaderCache.Capture(input, ManifestPath);
        string receiptPath=Path.Combine(Output,"android-compiler.json"), markerPath=Path.Combine(Output,QuestCampaignShaderCache.MarkerName);
        byte[] receiptBytes=File.ReadAllBytes(receiptPath), markerBytes=File.ReadAllBytes(markerPath);
        var receipt=JsonUtility.FromJson<QuestCampaignShaderValidation.Receipt>(Encoding.UTF8.GetString(receiptBytes));
        Action<string,Action<QuestCampaignShaderValidation.Receipt>> receiptControl = (name, mutate) => {
            var changed=JsonUtility.FromJson<QuestCampaignShaderValidation.Receipt>(Encoding.UTF8.GetString(receiptBytes)); mutate(changed);
            File.WriteAllText(receiptPath,JsonUtility.ToJson(changed));
            // Re-bind the marker's receipt hash to prove the complete alias/output audit independently of its first SHA guard.
            var marker=JsonUtility.FromJson<QuestCampaignShaderCache.Marker>(Encoding.UTF8.GetString(markerBytes)); marker.receiptSha256=QuestCampaignShaderCache.FileHash(receiptPath);
            File.WriteAllText(markerPath,JsonUtility.ToJson(marker));
            Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),name); controls.Add(name);
            File.WriteAllBytes(receiptPath,receiptBytes); File.WriteAllBytes(markerPath,markerBytes);
        };
        receiptControl("missing-alias",r=>r.programs=r.programs.Take(1).ToArray());
        receiptControl("duplicate-alias",r=>r.programs[1]=r.programs[0]);
        receiptControl("null-alias",r=>r.programs[1]=null);
        receiptControl("uncompiled-stage",r=>r.programs[0].fragmentCompiled=false);
        receiptControl("changed-keywords",r=>r.programs[0].keywords=new[]{"UNKNOWN"});
        receiptControl("changed-tier",r=>r.programs[0].hardwareTier=0);
        receiptControl("changed-material-count",r=>r.materialCount=0);
        receiptControl("changed-output-address",r=>r.programs[0].vertexFile="../outside.spv");
        receiptControl("false-headset-claim",r=>r.headsetPictureVerified=true);
        foreach(string file in new[]{receipt.programs[0].file,receipt.programs[0].vertexFile,receipt.programs[0].fragmentFile}) {
            string path=Path.Combine(Output,file); byte[] bytes=File.ReadAllBytes(path); File.WriteAllBytes(path,new byte[]{0});
            Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),"corrupt native output "+file); controls.Add("corrupt-"+Path.GetExtension(file)); File.WriteAllBytes(path,bytes);
        }
        File.WriteAllText(receiptPath,"{"); Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),"malformed receipt"); controls.Add("malformed-receipt"); File.WriteAllBytes(receiptPath,receiptBytes);
        File.WriteAllText(markerPath,"{"); Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),"malformed marker"); controls.Add("malformed-marker"); File.WriteAllBytes(markerPath,markerBytes);
        var partial=JsonUtility.FromJson<QuestCampaignShaderCache.Marker>(Encoding.UTF8.GetString(markerBytes)); partial.completed=false;
        File.WriteAllText(markerPath,JsonUtility.ToJson(partial)); Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),"partial marker"); controls.Add("partial-marker");
        File.Delete(markerPath); File.WriteAllBytes(markerPath+".tmp",markerBytes); Assert(!QuestCampaignShaderCache.TryReuse(input,closure,Output),"unpublished temp marker"); controls.Add("atomic-temp-not-complete"); File.Delete(markerPath+".tmp"); File.WriteAllBytes(markerPath,markerBytes);
        var different=JsonUtility.FromJson<QuestCampaignShaderCache.Closure>(JsonUtility.ToJson(closure)); different.settingsSha256=new string('0',64);
        Reject(()=>QuestCampaignShaderCache.Complete(input,closure,different,Output),"changed-during-validation");
        string helper="Assets/Quest/Editor/QuestCampaignShaderCache.cs"; byte[] helperBytes=File.ReadAllBytes(helper); File.AppendAllText(helper,"\n// private dependency drift witness\n");
        Assert(!QuestCampaignShaderCache.TryReuse(input,QuestCampaignShaderCache.Capture(input,ManifestPath),Output),"helper drift"); controls.Add("helper-drift"); File.WriteAllBytes(helper,helperBytes);
        // Ordinary mod CIL, local identity, and APK version fields are outside native shader compilation inputs.
        Directory.CreateDirectory("Assets/Quest/Resources"); File.WriteAllText("Assets/Quest/Resources/cache-profile.json","{\"name\":\"private fixture\"}");
        Directory.CreateDirectory("Assets/PrivateMod"); File.WriteAllText("Assets/PrivateMod/Unrelated.cs","// private unrelated mod source\n");
        string version=PlayerSettings.bundleVersion; PlayerSettings.bundleVersion="99.77.55"; AssetDatabase.SaveAssets();
        var ordinary=QuestCampaignShaderCache.Capture(input,ManifestPath);
        Assert(QuestCampaignShaderCache.ClosureHash(ordinary)==QuestCampaignShaderCache.ClosureHash(closure) && QuestCampaignShaderCache.TryReuse(input,ordinary,Output),"packaging/CIL/profile changed graphics closure"); controls.Add("mod-profile-packaging-do-not-invalidate");
        PlayerSettings.bundleVersion=version; AssetDatabase.SaveAssets();
        var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath); material.shader=Shader.Find("Unlit/Color"); Assert(material.shader!=shader,"negative material fixture");
        Reject(()=>QuestCampaignShaderValidation.Validate(ManifestPath,Output),"material-identity-still-checked-on-hit"); material.shader=shader; EditorUtility.SetDirty(material); AssetDatabase.SaveAssets();
        byte[] shaderBytes=File.ReadAllBytes(ShaderPath); File.AppendAllText(ShaderPath,"\n// actual changed shader source witness\n"); Native(input,"shader-drift-real-revalidation"); File.WriteAllBytes(ShaderPath,shaderBytes);
        byte[] includeBytes=File.ReadAllBytes(IncludePath); File.AppendAllText(IncludePath,"\n// actual changed include witness\n"); Native(input,"include-drift-real-revalidation"); File.WriteAllBytes(IncludePath,includeBytes);
        string metaPath=ShaderPath+".meta"; byte[] metaBytes=File.ReadAllBytes(metaPath); File.WriteAllText(metaPath,Encoding.UTF8.GetString(metaBytes).Replace("userData:","userData: private-cache-witness"));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport); Native(input,"meta-drift-real-revalidation"); File.WriteAllBytes(metaPath,metaBytes); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        PlayerSettings.colorSpace=ColorSpace.Gamma; AssetDatabase.SaveAssets(); Native(input,"graphics-settings-real-revalidation"); PlayerSettings.colorSpace=ColorSpace.Linear; AssetDatabase.SaveAssets();
        Native(input,"restore-baseline-real-revalidation");
        QuestCampaignShaderValidation.Validate(ManifestPath,Output); Assert(QuestCampaignShaderValidation.LastNativeCompileCount==0 && QuestCampaignShaderValidation.LastValidationCacheReused,"final unchanged actual hit");
        Directory.CreateDirectory("CacheWitnessProof");
        File.WriteAllText("CacheWitnessProof/result.json",JsonUtility.ToJson(new Result {passed=true,realUnityVulkanCompiler=true,unityVersion=Application.unityVersion,firstNativeQueries=first,unchangedNativeQueries=0,changedValidations=changedValidations,controls=controls.ToArray()},true)+"\n");
        Debug.Log("PASS real tiny Vulkan completed cache witness: first=2, unchanged=0, changed validations="+changedValidations+", controls="+controls.Count+". Authored fixture only; no original-game/headset pixel claim.");
    }

    public static void RepairOwnedOutputs()
    {
        var receipt=JsonUtility.FromJson<QuestCampaignShaderValidation.Receipt>(File.ReadAllText(Path.Combine(Output,"android-compiler.json")));
        string untouched=Path.Combine(Output,"private-unrelated-file.txt");
        File.WriteAllText(untouched,"must remain unchanged\n");
        foreach (string file in new[]{receipt.programs[0].file,receipt.programs[0].vertexFile,receipt.programs[0].fragmentFile})
            File.WriteAllBytes(Path.Combine(Output,file),new byte[]{0});
        QuestCampaignShaderValidation.Validate(ManifestPath,Output);
        Assert(QuestCampaignShaderValidation.LastNativeCompileCount==2 && !QuestCampaignShaderValidation.LastValidationCacheReused,"corrupted outputs did not force actual revalidation");
        Assert(File.ReadAllText(untouched)=="must remain unchanged\n","revalidation touched an unrelated output");
        QuestCampaignShaderValidation.Validate(ManifestPath,Output);
        Assert(QuestCampaignShaderValidation.LastNativeCompileCount==0 && QuestCampaignShaderValidation.LastValidationCacheReused,"repaired output receipt cannot be reused");
        File.WriteAllText("CacheWitnessProof/repair.json","{\"schema\":1,\"passed\":true,\"revalidationNativeQueries\":2,\"cacheHitNativeQueries\":0,\"threeCorruptOwnedOutputsRepaired\":true,\"unrelatedFileUntouched\":true,\"headsetVerified\":false}\n");
        Debug.Log("PASS real tiny Vulkan owned output repair: actual revalidation=2, subsequent hit=0; three owned hashed outputs repaired, unrelated file untouched.");
    }
}
#endif
