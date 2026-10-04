#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Validates imported original Bloom assets and their actual Android compiler output.</summary>
    public static class QuestPostEffectValidation
    {
        public const string InputPath = "QuestStartupEvidence/legacy-post-effects.json";
        public const string ReceiptPath = "QuestStartupEvidence/legacy-post-effects.android-validation.json";

        private sealed class Expected
        {
            public readonly string name, guid, sha256;
            public readonly int passes;
            public Expected(string name, string guid, string sha256, int passes)
            {
                this.name = name; this.guid = guid; this.sha256 = sha256; this.passes = passes;
            }
        }

        private static readonly Expected[] Assets = {
            new Expected("Hidden/BlendForBloom", "30881e480b10c1b46a3d99ec13496f5e",
                "84f4f797c3f77fca2797806b388d50bdbac7492118212ded78370a6d9d636ae5", 11),
            new Expected("Hidden/BrightPassFilter2", "93f40d5ea0c0a7945a5782e2dcd23833",
                "26e81ea437fbb5ffb45cb8fc4556bdb2ab6c1c50d5396af5c2c8bc1562b8e424", 2),
            new Expected("Hidden/BlurAndFlares", "29d4384c2ae952c4597a9d894d381163",
                "63283e8f5e60b6a7c2771b175c1c82fe62813648306ccf185c56379f75b196eb", 5)
        };

        [Serializable] public sealed class SourceAsset
        {
            public string name, assetPath, guid, sourceSha256, metaSha256;
            public int passCount;
        }
        [Serializable] public sealed class SourceReceipt
        {
            public int schema;
            public string target, changeset, installerSha256;
            public SourceAsset[] shaders;
        }
        [Serializable] public sealed class CompilerMessage
        {
            public string message, details, file, severity, platform;
            public int line;
        }
        [Serializable] public sealed class CompiledStage
        {
            public int passIndex, compiledBytes;
            public string stage, sha256;
            public bool success;
            public CompilerMessage[] messages;
        }
        [Serializable] public sealed class ValidatedAsset
        {
            public string name, assetPath, guid, sourceSha256, metaSha256;
            public int importedSubshaderCount, importedPassCount;
            public bool shaderHasError;
            public CompilerMessage[] messages;
            public CompiledStage[] glesStages;
        }
        [Serializable] public sealed class ValidationReceipt
        {
            public int schema = 1;
            public string unityVersion, sourceReceiptSha256, activeBuildTarget, compilerPlatform;
            public bool androidAssetsBuilt, allImportedAssetsVerified, allGlesPassStagesCompiled;
            public bool originalPixelParityVerified;
            public ValidatedAsset[] shaders;
        }

        public static void Validate(bool androidAssetsBuilt)
        {
            // Never let a receipt from an earlier pass survive a failed revalidation.
            SafePath(ReceiptPath);
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            if (!Directory.Exists("Assets/Quest"))
                throw new InvalidOperationException("Post-effect validation requires the generated Quest project.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.OpenGLES3))
                throw new InvalidOperationException("Post-effect validation requires the configured Android/GLES3 build target.");
            SafePath(InputPath);
            if (!File.Exists(InputPath) || new FileInfo(InputPath).Length > 1024 * 1024)
                throw new InvalidOperationException("Legacy post-effect source receipt is missing or invalid.");
            byte[] inputBytes = File.ReadAllBytes(InputPath);
            var input = JsonUtility.FromJson<SourceReceipt>(Encoding.UTF8.GetString(inputBytes));
            if (input == null || input.schema != 1 || input.target != "startup" || input.changeset != "960ebf59018a" ||
                input.installerSha256 != "302ee984abf9c55134a0fa83ac1c43939f62720bf7dc3fc681a21f3cbb42ae96" ||
                input.shaders == null || input.shaders.Length != Assets.Length)
                throw new InvalidOperationException("Legacy post-effect source receipt differs from the audited official sources.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<ValidatedAsset>();
            foreach (var expected in Assets)
            {
                var rows = input.shaders.Where(row => row != null && row.name == expected.name).ToArray();
                string path = "Assets/Shader/" + expected.name.Replace('/', '_') + ".shader";
                if (rows.Length != 1 || !unique.Add(rows[0].assetPath) || rows[0].assetPath != path ||
                    rows[0].guid != expected.guid || rows[0].sourceSha256 != expected.sha256 ||
                    rows[0].passCount != expected.passes)
                    throw new InvalidOperationException("Legacy post-effect source identity differs: " + expected.name);
                var row = rows[0];
                RequireHash(path, expected.sha256);
                RequireHash(path + ".meta", row.metaSha256);
                if (AssetDatabase.AssetPathToGUID(path) != expected.guid || AssetDatabase.GUIDToAssetPath(expected.guid) != path)
                    throw new InvalidOperationException("Imported post-effect GUID differs: " + expected.name);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || shader.name != expected.name)
                    throw new InvalidOperationException("Imported post-effect Shader is missing or renamed: " + expected.name);
                // ShaderData reads the imported pass bank directly. Material.passCount
                // and shader.isSupported depend on the editor graphics device, which
                // is Null during the project's headless build. The pinned 2021.3.5f1
                // SDK has this public API but not GetSerializedSubshader.
                var data = ShaderUtil.GetShaderData(shader);
                if (data == null || data.SubshaderCount != 1)
                    throw new InvalidOperationException("Imported post-effect subshader count differs: " + expected.name);
                var subshader = data.GetSubshader(0);
                if (subshader == null || subshader.PassCount != expected.passes)
                    throw new InvalidOperationException("Imported post-effect pass count differs: " + expected.name);
                RejectErrors(shader, expected.name);
                var stages = new List<CompiledStage>();
                if (androidAssetsBuilt)
                {
                    // Ask the actual Unity Android shader compiler for every original
                    // pass, rather than inferring successful GLES output from an empty
                    // cached message list. Both shader stages are used by all 18 passes.
                    for (int index = 0; index < expected.passes; ++index)
                    {
                        var pass = subshader.GetPass(index);
                        if (pass == null)
                            throw new InvalidOperationException("Imported post-effect pass is missing: " + expected.name + " " + index);
                        foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
                        {
                            var compiled = pass.CompileVariant(stage, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                            var messages = Messages(compiled.Messages);
                            if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                                messages.Any(message => message.severity == ShaderCompilerMessageSeverity.Error.ToString()))
                                throw new InvalidOperationException("Android post-effect compilation failed: " + expected.name +
                                    " pass=" + index + " stage=" + stage + " " + Describe(messages));
                            stages.Add(new CompiledStage {
                                passIndex = index, stage = stage.ToString(), success = true,
                                compiledBytes = compiled.ShaderData.Length, sha256 = Hash(compiled.ShaderData), messages = messages
                            });
                        }
                    }
                }
                RejectErrors(shader, expected.name);
                results.Add(new ValidatedAsset {
                    name = expected.name, assetPath = path, guid = expected.guid, sourceSha256 = expected.sha256,
                    metaSha256 = row.metaSha256, importedSubshaderCount = data.SubshaderCount,
                    importedPassCount = subshader.PassCount, shaderHasError = false,
                    messages = Messages(ShaderUtil.GetShaderMessages(shader)), glesStages = stages.ToArray()
                });
            }
            var receipt = new ValidationReceipt {
                unityVersion = Application.unityVersion, sourceReceiptSha256 = Hash(inputBytes),
                activeBuildTarget = BuildTarget.Android.ToString(), compilerPlatform = ShaderCompilerPlatform.GLES3x.ToString(),
                androidAssetsBuilt = androidAssetsBuilt, allImportedAssetsVerified = true,
                allGlesPassStagesCompiled = androidAssetsBuilt, originalPixelParityVerified = false, shaders = results.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            string temporary = ReceiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true) + "\n");
                File.Move(temporary, ReceiptPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Debug.Log("[GloomhavenVR Quest] original Bloom shaders verified: passes=11/2/5, GLES stages=" +
                (androidAssetsBuilt ? "36 compiled" : "pending Android assets build"));
        }

        private static void RejectErrors(Shader shader, string name)
        {
            bool errors = ShaderUtil.ShaderHasError(shader);
            var messages = Messages(ShaderUtil.GetShaderMessages(shader));
            if (errors || messages.Any(message => message.severity == ShaderCompilerMessageSeverity.Error.ToString()))
                throw new InvalidOperationException("Imported post-effect shader compiler error: " + name + " " + Describe(messages));
        }
        private static CompilerMessage[] Messages(ShaderMessage[] messages)
        {
            return (messages ?? new ShaderMessage[0]).Select(message => new CompilerMessage {
                message = message.message, details = message.messageDetails, file = message.file, line = message.line,
                severity = message.severity.ToString(), platform = message.platform.ToString()
            }).ToArray();
        }
        private static string Describe(CompilerMessage[] messages)
        {
            return string.Join("; ", messages.Select(message => message.severity + ": " + message.message));
        }
        private static void SafePath(string relative)
        {
            var path = new FileInfo(Path.GetFullPath(relative));
            if (path.Exists && (path.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Symlinked post-effect evidence or source: " + relative);
            for (var directory = path.Directory; directory != null; directory = directory.Parent)
                if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Symlinked post-effect evidence or source directory: " + relative);
        }
        private static void RequireHash(string path, string expected)
        {
            SafePath(path);
            if (string.IsNullOrEmpty(expected) || expected.Length != 64 || !File.Exists(path))
                throw new InvalidOperationException("Post-effect source/provenance file is missing: " + path);
            using (var stream = File.OpenRead(path))
            using (var digest = SHA256.Create())
                if (Hex(digest.ComputeHash(stream)) != expected)
                    throw new InvalidOperationException("Post-effect source/provenance SHA-256 differs: " + path);
        }
        private static string Hash(byte[] bytes)
        {
            using (var digest = SHA256.Create()) return Hex(digest.ComputeHash(bytes));
        }
        private static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
