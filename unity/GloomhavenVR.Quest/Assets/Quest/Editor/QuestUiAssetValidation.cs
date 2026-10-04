#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Checks restored original EULA/promotion shaders and their Android keyword banks.</summary>
    public static class QuestUiAssetValidation
    {
        public const string InputPath = "QuestStartupEvidence/original-ui-assets.json";
        public const string ReceiptPath = "QuestStartupEvidence/original-ui-assets.android-validation.json";
        private static readonly SourceShader[] Expected = {
            new SourceShader {
                name = "Splash Screen Shader", assetPath = "Assets/Shader/Splash Screen Shader.shader",
                guid = "36fec7f4d3bfafd409fd42ffef9eec70",
                sourceSha256 = "0050f621c03bbf14a26a89f6fdacaa6a3edc144a82d19dbb844d3c76625287d7",
                canonicalRecipeSha256 = "fd65d0a293f36a8d33578386f182d73b0aeba5ecd86a556826371db6ee1fff89",
                passCount = 1, keywords = new[] { "", "_ADDORMULT_ON" }
            },
            new SourceShader {
                name = "UI/Dissolve mask", assetPath = "Assets/Shader/UI_Dissolve mask.shader",
                guid = "ee924f72fbf9fcd4a9ea6e1dceb11982",
                sourceSha256 = "c2b96680f6cd9fd44070e998c89576beeb468ab88ac1f27a1d651f86f2c9abb4",
                canonicalRecipeSha256 = "0bbd001e5f0469c922464801d967e366451d698e98d787c3c97488f7f07008a7",
                passCount = 1, keywords = new[] { "" }
            }
        };

        [Serializable] public sealed class SourceShader
        {
            public string name, assetPath, guid, sourceSha256, metaSha256, canonicalRecipeSha256;
            public int passCount;
            public string[] keywords;
        }
        [Serializable] public sealed class SourceReceipt
        {
            public int schema;
            public string target, recipe, origin;
            public SourceShader[] shaders;
        }
        [Serializable] public sealed class CompiledBank
        {
            public string name, keyword, sha256, vertexSha256, fragmentSha256;
            public int bytes, vertexBytes, fragmentBytes;
        }
        [Serializable] public sealed class ValidationReceipt
        {
            public int schema = 1;
            public string unityVersion, sourceReceiptSha256;
            public bool androidAssetsBuilt, allGlesPassStagesCompiled, originalPixelParityVerified;
            public int importedPassCount;
            public SourceShader[] shaders;
            public CompiledBank[] glesPrograms;
        }

        public static void Validate(bool androidAssetsBuilt)
        {
            SafePath(ReceiptPath);
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            if (!Directory.Exists("Assets/Quest") ||
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.OpenGLES3))
                throw new InvalidOperationException("Original UI validation requires the generated Android/GLES3 Quest project.");
            SafePath(InputPath);
            if (!File.Exists(InputPath) || new FileInfo(InputPath).Length > 1024 * 1024)
                throw new InvalidOperationException("Original UI source receipt is missing or oversized.");
            byte[] bytes = File.ReadAllBytes(InputPath);
            var input = JsonUtility.FromJson<SourceReceipt>(Encoding.UTF8.GetString(bytes));
            if (input == null || input.schema != 1 || input.target != "startup" ||
                input.recipe != "original-ui-dxbc-port-v1" || input.origin != "original-compiled-DXBC-transcription" ||
                input.shaders == null || input.shaders.Length != Expected.Length)
                throw new InvalidOperationException("Original UI source receipt differs from the audited transcription.");
            var programs = new List<CompiledBank>();
            foreach (var expected in Expected)
            {
                var matching = input.shaders.Where(row => row != null && row.name == expected.name).ToArray();
                if (matching.Length != 1) throw new InvalidOperationException("Original UI source shader count differs: " + expected.name);
                var source = matching[0];
                string assetPath = expected.assetPath;
                if (source.assetPath != assetPath ||
                    source.guid != expected.guid || source.sourceSha256 != expected.sourceSha256 || source.passCount != 1 ||
                    source.canonicalRecipeSha256 != expected.canonicalRecipeSha256 || source.keywords == null ||
                    !source.keywords.SequenceEqual(expected.keywords))
                    throw new InvalidOperationException("Original UI source identity or keyword banks differ.");
                RequireHash(assetPath, expected.sourceSha256);
                RequireHash(assetPath + ".meta", source.metaSha256);
                if (AssetDatabase.AssetPathToGUID(assetPath) != expected.guid ||
                    AssetDatabase.GUIDToAssetPath(expected.guid) != assetPath)
                    throw new InvalidOperationException("Imported original UI shader GUID differs.");
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
                if (shader == null || shader.name != source.name)
                    throw new InvalidOperationException("Imported original UI shader is missing or renamed.");
                var data = ShaderUtil.GetShaderData(shader);
                if (data == null || data.SubshaderCount != 1 || data.GetSubshader(0).PassCount != 1)
                    throw new InvalidOperationException("Imported original UI shader pass bank differs.");
                var pass = data.GetSubshader(0).GetPass(0);
                if (!pass.HasShaderStage(ShaderType.Vertex) || !pass.HasShaderStage(ShaderType.Fragment))
                    throw new InvalidOperationException("Original EULA shader stage is missing.");
                // Compile both stages already at import/preflight, before any
                // costly IL2CPP/player build. The separate receipt flag records
                // whether Android assets have actually been built by the caller.
                {
                    foreach (string keyword in source.keywords)
                    {
                        string[] keywords = keyword.Length == 0 ? new string[0] : new[] { keyword };
                        var compiled = pass.CompileVariant(ShaderType.Vertex, keywords, ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                        if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                            (compiled.Messages ?? new ShaderMessage[0]).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                            throw new InvalidOperationException("Android original UI shader compilation failed: " + keyword);
                        string code = new UTF8Encoding(false, true).GetString(compiled.ShaderData);
                        var match = Regex.Match(code, @"\A#ifdef VERTEX\n(.*?)\n#endif\n#ifdef FRAGMENT\n(.*?)\n#endif\n*\z", RegexOptions.Singleline);
                        if (!match.Success) throw new InvalidOperationException("Android original UI shader has an incomplete GLES program.");
                        string vertex = match.Groups[1].Value, fragment = match.Groups[2].Value;
                        foreach (string section in new[] { vertex, fragment })
                            if (!section.StartsWith("#version 300 es\n", StringComparison.Ordinal) ||
                                !Regex.IsMatch(section, @"\bvoid\s+main\s*\(\s*\)"))
                                throw new InvalidOperationException("Android original UI GLSL300 stage is missing.");
                        if (!Regex.IsMatch(vertex, @"\bgl_Position\b") || !Regex.IsMatch(fragment, @"\bSV_Target0\b"))
                            throw new InvalidOperationException("Android original UI shader stage output is missing.");
                        programs.Add(new CompiledBank {
                            name = source.name, keyword = keyword, sha256 = Hash(compiled.ShaderData), bytes = compiled.ShaderData.Length,
                            vertexSha256 = Hash(Encoding.UTF8.GetBytes(vertex)), vertexBytes = Encoding.UTF8.GetByteCount(vertex),
                            fragmentSha256 = Hash(Encoding.UTF8.GetBytes(fragment)), fragmentBytes = Encoding.UTF8.GetByteCount(fragment)
                        });
                    }
                }
                if (ShaderUtil.ShaderHasError(shader) || (ShaderUtil.GetShaderMessages(shader) ?? new ShaderMessage[0])
                    .Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                    throw new InvalidOperationException("Imported original UI shader has compiler errors.");
            }
            var receipt = new ValidationReceipt {
                unityVersion = Application.unityVersion, sourceReceiptSha256 = Hash(bytes), shaders = input.shaders,
                androidAssetsBuilt = androidAssetsBuilt, allGlesPassStagesCompiled = true, importedPassCount = Expected.Length,
                originalPixelParityVerified = false, glesPrograms = programs.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            string temporary = ReceiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true) + "\n");
                File.Move(temporary, ReceiptPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Debug.Log("[GloomhavenVR Quest] original UI shaders verified: passes=2, keyword banks=" + programs.Count);
        }

        private static void SafePath(string relative)
        {
            var path = new FileInfo(Path.GetFullPath(relative));
            if (path.Exists && (path.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Linked original UI validation source: " + relative);
            for (var directory = path.Directory; directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Linked original UI validation directory: " + relative);
        }
        private static void RequireHash(string path, string expected)
        {
            SafePath(path);
            if (expected == null || expected.Length != 64 || !File.Exists(path) ||
                Hash(File.ReadAllBytes(path)) != expected)
                throw new InvalidOperationException("Original UI source/provenance SHA-256 differs: " + path);
        }
        private static string Hash(byte[] bytes)
        {
            using (var digest = SHA256.Create())
                return BitConverter.ToString(digest.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
