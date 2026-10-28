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
    /// <summary>Validates imported original Bloom assets and their actual Android compiler output.</summary>
    public static class QuestPostEffectValidation
    {
#if GHVR_QUEST_GAME
        private const GraphicsDeviceType RequiredGraphicsApi = GraphicsDeviceType.Vulkan;
        private const string CompilerPlatform = "Vulkan";
        private const bool VulkanBackend = true;
#else
        private const GraphicsDeviceType RequiredGraphicsApi = GraphicsDeviceType.OpenGLES3;
        private const string CompilerPlatform = "GLES3x";
        private const bool VulkanBackend = false;
#endif
        public const string InputPath = "QuestStartupEvidence/legacy-post-effects.json";
        public const string ReceiptPath = "QuestStartupEvidence/legacy-post-effects.android-validation.json";

        private sealed class Expected
        {
            public readonly string name, guid, sha256, upgradeSha256;
            public readonly int passes;
            public Expected(string name, string guid, string sha256, string upgradeSha256, int passes)
            {
                this.name = name; this.guid = guid; this.sha256 = sha256; this.upgradeSha256 = upgradeSha256; this.passes = passes;
            }
        }

        private static readonly Expected[] Assets = {
            new Expected("Hidden/BlendForBloom", "30881e480b10c1b46a3d99ec13496f5e",
                "84f4f797c3f77fca2797806b388d50bdbac7492118212ded78370a6d9d636ae5",
                "d22461e93e8d3bd6801fe12a8ea8a12632d870fd54afc4d34dca839337838abf", 11),
            new Expected("Hidden/BrightPassFilter2", "93f40d5ea0c0a7945a5782e2dcd23833",
                "26e81ea437fbb5ffb45cb8fc4556bdb2ab6c1c50d5396af5c2c8bc1562b8e424",
                "8a19269566f8fd692a44eb607c44f114abbc0a555d11c06995de22e2112e8dc1", 2),
            new Expected("Hidden/BlurAndFlares", "29d4384c2ae952c4597a9d894d381163",
                "63283e8f5e60b6a7c2771b175c1c82fe62813648306ccf185c56379f75b196eb",
                "343d875aed4f5f221ce7d5e33df24ffc90459d9533448d7ba9edca80fa40d390", 5)
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
        [Serializable] public sealed class CompiledProgram
        {
            public int passIndex, compiledBytes, vertexSectionBytes, fragmentSectionBytes;
            public string sha256, vertexSectionSha256, fragmentSectionSha256;
            public bool success, vertexSectionVerified, fragmentSectionVerified;
            public CompilerMessage[] messages;
        }
        [Serializable] public sealed class ValidatedAsset
        {
            public string name, assetPath, guid, sourceSha256, metaSha256;
            public string importedSourceSha256;
            public bool unityObjectToClipPosUpgradeApplied;
            public int importedSubshaderCount, importedPassCount;
            public bool shaderHasError;
            public CompilerMessage[] messages;
            public CompiledProgram[] glesPrograms, vulkanPrograms;
        }
        [Serializable] public sealed class ValidationReceipt
        {
            public int schema = 1;
            public string unityVersion, sourceReceiptSha256, activeBuildTarget, compilerPlatform;
            public bool androidAssetsBuilt, allImportedAssetsVerified, allGlesPassStagesCompiled, allVulkanPassStagesCompiled;
            public int combinedProgramCount, compiledStageSections;
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
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(RequiredGraphicsApi))
                throw new InvalidOperationException("Post-effect validation requires the configured Android " + CompilerPlatform + " build target.");
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
                // Unity may rewrite the official old vertex helper at import. The
                // only accepted alternate is the complete, independently audited
                // automatic upgrade (2/1/4 expression replacements plus its header).
                // All pass code, states and properties remain fingerprint-protected.
                SafePath(path);
                string importedSha256;
                using (var stream = File.OpenRead(path))
                using (var digest = SHA256.Create()) importedSha256 = Hex(digest.ComputeHash(stream));
                if (importedSha256 != expected.sha256 && importedSha256 != expected.upgradeSha256)
                    throw new InvalidOperationException("Post-effect source differs from official source/Unity API upgrade: " + expected.name);
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
                var programs = new List<CompiledProgram>();
                if (androidAssetsBuilt)
                {
                    // GLES3x's Vertex request contains the complete program; an
                    // independent Fragment request succeeds with empty bytecode.
                    // Verify both genuine emitted sections, not just API success.
                    // https://docs.unity3d.com/2021.2/Documentation/ScriptReference/ShaderData.Pass.CompileVariant.html
                    for (int index = 0; index < expected.passes; ++index)
                    {
                        var pass = subshader.GetPass(index);
                        if (pass == null || !pass.HasShaderStage(ShaderType.Vertex) || !pass.HasShaderStage(ShaderType.Fragment))
                            throw new InvalidOperationException("Imported post-effect stage is missing: " + expected.name + " " + index);
#if GHVR_QUEST_GAME
                        var compiled = QuestVulkanShaderValidation.Compile(shader, 0, index, new string[0], GraphicsTier.Tier2);
                        QuestVulkanShaderValidation.RequireColorOutput(compiled);
                        programs.Add(new CompiledProgram {
                            passIndex = index, success = true, compiledBytes = compiled.vertex.Length + compiled.fragment.Length,
                            sha256 = compiled.bankSha256, messages = new CompilerMessage[0],
                            vertexSectionVerified = true, fragmentSectionVerified = true,
                            vertexSectionBytes = compiled.vertex.Length, fragmentSectionBytes = compiled.fragment.Length,
                            vertexSectionSha256 = compiled.vertexSha256, fragmentSectionSha256 = compiled.fragmentSha256
                        });
#else
                        var compiled = pass.CompileVariant(ShaderType.Vertex, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                        var messages = Messages(compiled.Messages);
                        if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                            messages.Any(message => message.severity == ShaderCompilerMessageSeverity.Error.ToString()))
                            throw new InvalidOperationException("Android post-effect compilation failed: " + expected.name +
                                " pass=" + index + " " + Describe(messages));
                        var sections = VerifiedGlesSections(compiled.ShaderData, expected.name, index);
                        byte[] vertex = Encoding.UTF8.GetBytes(sections[0]), fragment = Encoding.UTF8.GetBytes(sections[1]);
                        programs.Add(new CompiledProgram {
                            passIndex = index, success = true, compiledBytes = compiled.ShaderData.Length,
                            sha256 = Hash(compiled.ShaderData), messages = messages,
                            vertexSectionVerified = true, fragmentSectionVerified = true,
                            vertexSectionBytes = vertex.Length, fragmentSectionBytes = fragment.Length,
                            vertexSectionSha256 = Hash(vertex), fragmentSectionSha256 = Hash(fragment)
                        });
#endif
                    }
                }
                RejectErrors(shader, expected.name);
                results.Add(new ValidatedAsset {
                    name = expected.name, assetPath = path, guid = expected.guid, sourceSha256 = expected.sha256,
                    metaSha256 = row.metaSha256, importedSourceSha256 = importedSha256,
                    unityObjectToClipPosUpgradeApplied = importedSha256 == expected.upgradeSha256,
                    importedSubshaderCount = data.SubshaderCount,
                    importedPassCount = subshader.PassCount, shaderHasError = false,
                    messages = Messages(ShaderUtil.GetShaderMessages(shader)),
                    glesPrograms = VulkanBackend ? new CompiledProgram[0] : programs.ToArray(),
                    vulkanPrograms = VulkanBackend ? programs.ToArray() : new CompiledProgram[0]
                });
            }
            var receipt = new ValidationReceipt {
                unityVersion = Application.unityVersion, sourceReceiptSha256 = Hash(inputBytes),
                activeBuildTarget = BuildTarget.Android.ToString(), compilerPlatform = CompilerPlatform,
                androidAssetsBuilt = androidAssetsBuilt, allImportedAssetsVerified = true,
                allGlesPassStagesCompiled = androidAssetsBuilt && !VulkanBackend,
                allVulkanPassStagesCompiled = androidAssetsBuilt && VulkanBackend,
                combinedProgramCount = results.Sum(result => result.glesPrograms.Length + result.vulkanPrograms.Length),
                compiledStageSections = results.Sum(result => result.glesPrograms.Length + result.vulkanPrograms.Length) * 2,
                originalPixelParityVerified = false, shaders = results.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            string temporary = ReceiptPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true) + "\n");
                File.Move(temporary, ReceiptPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Debug.Log("[GloomhavenVR Quest] original Bloom shaders verified: passes=11/2/5, " + CompilerPlatform + " stages=" +
                (androidAssetsBuilt ? "18 programs / 36 verified stages" : "pending Android assets build"));
        }

        private static string[] VerifiedGlesSections(byte[] bytes, string name, int pass)
        {
            string code = new UTF8Encoding(false, true).GetString(bytes);
            var match = Regex.Match(code, @"\A#ifdef VERTEX\n(.*?)\n#endif\n#ifdef FRAGMENT\n(.*?)\n#endif\n*\z", RegexOptions.Singleline);
            if (!match.Success)
                throw new InvalidOperationException("Android post-effect combined GLES program is incomplete: " + name + " " + pass);
            string vertex = match.Groups[1].Value, fragment = match.Groups[2].Value;
            foreach (string section in new[] { vertex, fragment })
                if (!section.StartsWith("#version 300 es\n", StringComparison.Ordinal) ||
                    !Regex.IsMatch(section, @"\bvoid\s+main\s*\(\s*\)"))
                    throw new InvalidOperationException("Android post-effect GLSL300 stage is missing: " + name + " " + pass);
            if (!Regex.IsMatch(vertex, @"\bgl_Position\b") || !Regex.IsMatch(fragment, @"\bSV_Target0\b"))
                throw new InvalidOperationException("Android post-effect GLSL stage output is missing: " + name + " " + pass);
            return new[] { vertex, fragment };
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
