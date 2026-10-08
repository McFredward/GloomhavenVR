using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using GloomhavenVR.Quest.Editor;

class QuestKeywordStripControls
{
    const string Guid = "12345678123456781234567812345678";
    static int passed;
    static void Check(bool test) { if (!test) throw new Exception("Keyword strip control failed " + passed); ++passed; }
    static void Reject(Action action) { try { action(); } catch (InvalidDataException) { ++passed; return; } throw new Exception("Missing ownership rejection."); }
    static ShaderCompilerData Bank(ShaderCompilerPlatform platform, params string[] keys)
    { return new ShaderCompilerData { shaderCompilerPlatform = platform, shaderKeywordSet = new ShaderKeywordSet(keys) }; }
    static void Main(string[] args)
    {
        string path = Path.Combine(args[0], "manifest.json"); File.WriteAllText(path, "first");
        Environment.SetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST", path);
        var row = new QuestCampaignShaderStrip.OriginalShader { guid = Guid, assetPath = "Assets/Original.shader", originalName = "Original", sourceRestoration = "exact-original-dxbc", variants = new[] {
            new QuestCampaignShaderStrip.Bank { passType = "ForwardAdd", keywords = new[] { "DIRECTIONAL" } },
            new QuestCampaignShaderStrip.Bank { passType = "ForwardAdd", keywords = new[] { "POINT", "SHADOWS_CUBE" } },
            new QuestCampaignShaderStrip.Bank { passType = "ShadowCaster", keywords = new[] { "SHADOWS_CUBE" } }
        } };
        var manifest = new QuestCampaignShaderStrip.Manifest { schema = 1, scope = "campaign-compiler", graphicsApi = "Vulkan", requiredShaderCount = 1, shaders = new[] { row } };
        JsonUtility.Value = manifest;
        var audit = QuestCampaignShaderStrip.Audit(manifest); Check(audit.ContainsKey(Guid));
        var shader = new Shader { name = "Original", path = row.assetPath, guid = Guid };
        var snippet = new ShaderSnippetData { passType = PassType.ForwardAdd, passName = "FORWARD", shaderType = ShaderType.Vertex };
        var valid = Bank(ShaderCompilerPlatform.Vulkan, "POINT", "SHADOWS_CUBE", "INSTANCING_ON", "STEREO_MULTIVIEW_ON", "UNKNOWN_AUTHOR_FEATURE");
        var invalid = Bank(ShaderCompilerPlatform.Vulkan, "DIRECTIONAL", "SHADOWS_CUBE");
        var gles = Bank(ShaderCompilerPlatform.GLES3x, "DIRECTIONAL", "SHADOWS_CUBE");
        var data = new List<ShaderCompilerData> { invalid, valid, gles };
        var callback = new QuestCampaignShaderStrip(); callback.OnProcessShader(shader, snippet, data);
        Check(data.Count == 2); Check(data[0].shaderKeywordSet.GetShaderKeywords().Length == 5); Check(data[1].shaderCompilerPlatform == ShaderCompilerPlatform.GLES3x);
        Check(QuestCampaignShaderStrip.RemovedCount == 1);
        var unknownShader = new Shader { name = "Original", path = row.assetPath, guid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
        data = new List<ShaderCompilerData> { invalid }; callback.OnProcessShader(unknownShader, snippet, data); Check(data.Count == 1);
        foreach (var pass in new[] { PassType.Normal, PassType.ForwardBase, PassType.ShadowCaster, PassType.Deferred })
        { snippet.passType = pass; data = new List<ShaderCompilerData> { invalid }; callback.OnProcessShader(shader, snippet, data); Check(data.Count == 1); }
        snippet.passType = PassType.ForwardAdd;
        shader.name = "WrongImportedName"; Reject(() => callback.OnProcessShader(shader, snippet, new List<ShaderCompilerData> { invalid })); shader.name = row.originalName;
        row.sourceRestoration = "retained-source-contract"; Check(QuestCampaignShaderStrip.Audit(manifest).Count == 0); row.sourceRestoration = "exact-original-dxbc";
        manifest.requiredShaderCount = 2; Reject(() => QuestCampaignShaderStrip.Audit(manifest)); manifest.requiredShaderCount = 1;
        row.variants[0].keywords = new[] { "DIRECTIONAL", "SHADOWS_CUBE" }; Reject(() => QuestCampaignShaderStrip.Audit(manifest)); row.variants[0].keywords = new[] { "DIRECTIONAL" };
        File.WriteAllText(path, "changed ownership"); Reject(() => callback.OnProcessShader(shader, snippet, new List<ShaderCompilerData> { invalid }));
        Console.WriteLine("PASS " + passed + " bounded callback ownership/platform/native-graph/drift controls.");
    }
}

// Deterministic public API boundaries; actual native SDK compilation and bundle
// execution are proved separately by QuestCubeShadowStripWitness.
namespace UnityEngine
{
    public class Shader { public string name, path, guid; }
    public static class JsonUtility { public static object Value; public static T FromJson<T>(string text) { return (T)Value; } }
    public static class Debug { public static void Log(string message) { } }
}
namespace UnityEngine.Rendering
{
    public enum PassType { Normal, ForwardAdd, ForwardBase, ShadowCaster, Deferred }
    public struct ShaderKeyword { public string name; }
    public struct ShaderKeywordSet
    {
        string[] keys; public ShaderKeywordSet(string[] keys) { this.keys = keys; }
        public ShaderKeyword[] GetShaderKeywords() { return Array.ConvertAll(keys, key => new ShaderKeyword { name = key }); }
    }
}
namespace UnityEditor
{
    public static class AssetDatabase
    { public static string GetAssetPath(Shader shader) { current = shader; return shader.path; } static Shader current; public static string AssetPathToGUID(string path) { return current.guid; } }
}
namespace UnityEditor.Rendering
{
    public enum ShaderCompilerPlatform { Vulkan, GLES3x }
    public enum ShaderType { Vertex, Fragment }
    public struct ShaderSnippetData { public PassType passType; public string passName; public ShaderType shaderType; }
    public struct ShaderCompilerData { public ShaderCompilerPlatform shaderCompilerPlatform; public ShaderKeywordSet shaderKeywordSet; }
}
namespace UnityEditor.Build
{ public interface IPreprocessShaders { int callbackOrder { get; } void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data); } }
