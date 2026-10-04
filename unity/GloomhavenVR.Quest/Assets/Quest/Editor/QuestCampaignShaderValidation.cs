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
    /// <summary>
    /// Compile the exact recovered Campaign shader identities and validate every
    /// original material binding. This gate proves compilation, not pixels.
    /// Original-reference rendering is a separate native Windows/D3D11 gate.
    /// </summary>
    public static class QuestCampaignShaderValidation
    {
        public const string DefaultManifest = "Assets/QuestOriginalCampaign/campaign-shaders.json";

        [Serializable] public sealed class Variant
        {
            public int subshader, pass;
            public string stereo, vertexOriginalDxbcSha256, fragmentOriginalDxbcSha256, fragmentOutput = "color";
            public string[] keywords;
        }
        [Serializable] public sealed class OriginalShader
        {
            public string guid, assetPath, originalName, originalSerializedFile, sourceSha256;
            public long originalPathId;
            public Variant[] variants;
        }
        [Serializable] public sealed class OriginalMaterial
        {
            public string guid, assetPath, shaderGuid;
        }
        [Serializable] public sealed class RenderCase
        {
            public string id, shaderGuid, materialGuid, meshGuid;
        }
        [Serializable] public sealed class Manifest
        {
            public int schema, requiredMaterialCount;
            public string scope;
            public OriginalShader[] shaders;
            public OriginalMaterial[] materials;
            public RenderCase[] renderCases;
        }
        [Serializable] public sealed class CompiledProgram
        {
            public string guid, stereo, glesSha256, file;
            public int subshader, pass;
            public string[] keywords;
            public bool vertexCompiled, fragmentCompiled, vertexEyeRoutingObserved;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, materialCount;
            public string unityVersion, compilerPlatform = "GLES3x", sourceManifestSha256;
            public bool originalPixelParityVerified, headsetPictureVerified;
            public CompiledProgram[] programs;
        }

        public static void Validate()
        {
            Validate(Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? DefaultManifest,
                     Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_OUTPUT") ?? "QuestCampaignShaderEvidence");
        }

        public static void Validate(string manifestPath, string outputPath)
        {
            if (Application.unityVersion != "2021.3.5f1")
                throw new InvalidOperationException("Campaign shader evidence requires original Unity 2021.3.5f1.");
            var input = Read(manifestPath);
            Directory.CreateDirectory(outputPath);
            string receiptPath = Path.Combine(outputPath, "android-compiler.json");
            if (File.Exists(receiptPath)) File.Delete(receiptPath);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.OpenGLES3))
                throw new InvalidOperationException("Campaign compiler gate requires the Android/GLES3 project.");
            var shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
            var programs = new List<CompiledProgram>();
            foreach (var row in input.shaders)
            {
                string path = ExactAsset(row.guid, row.assetPath);
                if (Hash(File.ReadAllBytes(path)) != row.sourceSha256)
                    throw new InvalidOperationException("Campaign translated shader bytes differ: " + row.guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || shader.name != row.originalName || shader.name == "Hidden/InternalErrorShader")
                    throw new InvalidOperationException("Campaign shader import changes its original identity: " + row.guid);
                RejectShaderErrors(shader);
                shaders.Add(row.guid, shader);
                var data = ShaderUtil.GetShaderData(shader);
                foreach (var variant in row.variants)
                {
                    if (variant.subshader < 0 || variant.subshader >= data.SubshaderCount)
                        throw new InvalidOperationException("Campaign shader loses an original subshader.");
                    var subshader = data.GetSubshader(variant.subshader);
                    if (variant.pass < 0 || variant.pass >= subshader.PassCount)
                        throw new InvalidOperationException("Campaign shader loses an original pass.");
                    var pass = subshader.GetPass(variant.pass);
                    var vertex = pass.CompileVariant(ShaderType.Vertex, variant.keywords, ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                    // GLES3x returns the entire linked native bank through the
                    // Vertex query. Verify both emitted stages in those bytes.
                    if (!vertex.Success || vertex.ShaderData == null || vertex.ShaderData.Length == 0)
                        throw new InvalidOperationException("Actual Campaign Android shader bank failed: " + row.guid + " / " + string.Join(" ", variant.keywords));
                    string source = new UTF8Encoding(false, true).GetString(vertex.ShaderData);
                    VerifyBank(source, variant.fragmentOutput);
                    bool eyeRouting = Regex.IsMatch(source, @"\bgl_ViewID_OVR\b") || Regex.IsMatch(source, @"\bgl_InstanceID\b") && Regex.IsMatch(source, @"\bunity_StereoEyeIndex\b");
                    if (variant.stereo == "multiview" && !eyeRouting)
                        throw new InvalidOperationException("Campaign multiview bank omits actual vertex eye routing.");
                    string key = row.guid + "-" + variant.subshader + "-" + variant.pass + "-" + Hash(Encoding.UTF8.GetBytes(string.Join("\n", variant.keywords))).Substring(0, 16);
                    string file = key + ".glsl";
                    File.WriteAllBytes(Path.Combine(outputPath, file), vertex.ShaderData);
                    programs.Add(new CompiledProgram {
                        guid = row.guid, subshader = variant.subshader, pass = variant.pass, stereo = variant.stereo,
                        keywords = variant.keywords, vertexCompiled = true, fragmentCompiled = true,
                        vertexEyeRoutingObserved = eyeRouting, glesSha256 = Hash(vertex.ShaderData), file = file
                    });
                }
                RejectShaderErrors(shader);
            }
            foreach (var row in input.materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(ExactAsset(row.guid, row.assetPath));
                Shader shader;
                if (material == null || !shaders.TryGetValue(row.shaderGuid, out shader) || material.shader != shader)
                    throw new InvalidOperationException("Campaign material no longer uses its exact original shader: " + row.guid);
            }
            File.WriteAllText(receiptPath, JsonUtility.ToJson(new Receipt {
                unityVersion = Application.unityVersion, sourceManifestSha256 = Hash(File.ReadAllBytes(manifestPath)),
                materialCount = input.materials.Length, programs = programs.ToArray()
            }, true) + "\n");
            Debug.Log("PASS Campaign Android shader gate: materials=" + input.materials.Length + ", actual banks=" + programs.Count + ". Original pixels and headset remain separate gates.");
        }

        public static void BuildReferenceCandidateBundle()
        {
            string manifestPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? DefaultManifest;
            string outputPath = Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_OUTPUT") ?? "QuestCampaignShaderEvidence";
            var input = Read(manifestPath);
            Directory.CreateDirectory(outputPath);
            var assetPaths = new List<string>();
            var addresses = new List<string>();
            foreach (var row in input.materials)
            {
                assetPaths.Add(ExactAsset(row.guid, row.assetPath));
                addresses.Add(row.guid);
            }
            foreach (var guid in input.renderCases.Select(row => row.meshGuid).Distinct(StringComparer.Ordinal))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<Mesh>(path) == null)
                    throw new InvalidOperationException("Original rendering fixture mesh is missing: " + guid);
                assetPaths.Add(ExactAsset(guid, path));
                addresses.Add(guid);
            }
            var builds = new[] { new AssetBundleBuild {
                assetBundleName = "quest-campaign-shader-candidates",
                assetNames = assetPaths.ToArray(), addressableNames = addresses.ToArray()
            } };
            var result = BuildPipeline.BuildAssetBundles(outputPath, builds, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
            if (result == null) throw new InvalidOperationException("Actual Windows candidate shader bundle build failed.");
            foreach (var row in input.shaders)
                RejectShaderErrors(AssetDatabase.LoadAssetAtPath<Shader>(ExactAsset(row.guid, row.assetPath)));
            Debug.Log("Built native D3D11 shader-reference candidates; no rendering outcome is asserted.");
        }

        public static void VerifyBank(string source) { VerifyBank(source, "color"); }

        public static void VerifyBank(string source, string originalFragmentOutput)
        {
            // Validate real generated sections after removing comments. A token
            // inside a comment cannot establish a working native program.
            source = Regex.Replace(source, @"/\*.*?\*/", "", RegexOptions.Singleline);
            source = Regex.Replace(source, @"(?m)//[^\n]*", "");
            int vertex = source.IndexOf("#ifdef VERTEX", StringComparison.Ordinal);
            int fragment = source.IndexOf("#ifdef FRAGMENT", StringComparison.Ordinal);
            if (vertex < 0 || fragment <= vertex)
                throw new InvalidOperationException("Campaign bank lacks complete GLES stages.");
            string vertexSource = source.Substring(vertex, fragment - vertex);
            string fragmentSource = source.Substring(fragment);
            if (!Regex.IsMatch(vertexSource, @"#version 3[01]0 es") || !Regex.IsMatch(fragmentSource, @"#version 3[01]0 es"))
                throw new InvalidOperationException("Campaign bank is not native GLES3.");
            if (!Regex.IsMatch(vertexSource, @"\bvoid\s+main\s*\(\s*\)") || !Regex.IsMatch(fragmentSource, @"\bvoid\s+main\s*\(\s*\)"))
                throw new InvalidOperationException("Campaign bank loses a native stage entry.");
            if (!Regex.IsMatch(vertexSource, @"\bgl_Position\s*="))
                throw new InvalidOperationException("Campaign vertex bank does not emit geometry.");
            if (originalFragmentOutput == "depth")
            {
                if (!Regex.IsMatch(fragmentSource, @"\bgl_FragDepth\s*="))
                    throw new InvalidOperationException("Campaign fragment bank loses its original depth output.");
            }
            else if (originalFragmentOutput == "color")
            {
                var outputs = Regex.Matches(fragmentSource, @"layout\s*\(\s*location\s*=\s*0\s*\)\s*out\s+(?:(?:highp|mediump|lowp)\s+)?vec4\s+(\w+)\s*;");
                if (outputs.Count != 1 || !Regex.IsMatch(fragmentSource, @"\b" + Regex.Escape(outputs[0].Groups[1].Value) + @"(?:\.[xyzwrgba]+)?\s*="))
                    throw new InvalidOperationException("Campaign fragment bank does not emit a unique native color output.");
            }
            else if (originalFragmentOutput != "none")
                throw new InvalidOperationException("Campaign original fragment output contract is unknown.");
        }

        private static Manifest Read(string path)
        {
            var input = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (input == null || input.schema != 1 || input.scope != "campaign" || input.shaders == null || input.shaders.Length == 0 ||
                input.materials == null || input.materials.Length == 0 || input.materials.Length != input.requiredMaterialCount ||
                input.renderCases == null || input.renderCases.Length == 0)
                throw new InvalidOperationException("Campaign shader identity/coverage receipt is incomplete.");
            var shaderGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in input.shaders)
            {
                if (row == null || !Guid(row.guid) || !shaderGuids.Add(row.guid) || !HashValue(row.sourceSha256) ||
                    string.IsNullOrEmpty(row.originalSerializedFile) || string.IsNullOrEmpty(row.originalName) || row.variants == null || row.variants.Length == 0)
                    throw new InvalidOperationException("Campaign shader identity/provenance is incomplete or duplicated.");
                var banks = new HashSet<string>(StringComparer.Ordinal);
                foreach (var bank in row.variants)
                    if (bank == null || bank.subshader < 0 || bank.pass < 0 || bank.keywords == null ||
                        bank.keywords.Any(keyword => !Regex.IsMatch(keyword, @"^[A-Z0-9_]+$")) || bank.keywords.Distinct().Count() != bank.keywords.Length ||
                        !HashValue(bank.vertexOriginalDxbcSha256) || !HashValue(bank.fragmentOriginalDxbcSha256) ||
                        bank.stereo != "mono" && bank.stereo != "instancing" && bank.stereo != "multiview" ||
                        bank.stereo == "multiview" && !bank.keywords.Contains("STEREO_MULTIVIEW_ON") ||
                        !banks.Add(bank.subshader + ":" + bank.pass + ":" + string.Join(" ", bank.keywords.OrderBy(value => value, StringComparer.Ordinal))))
                        throw new InvalidOperationException("Campaign original shader bank provenance is invalid or duplicated.");
            }
            if (input.materials.Select(row => row.guid).Distinct().Count() != input.materials.Length ||
                input.materials.Any(row => !Guid(row.guid) || !shaderGuids.Contains(row.shaderGuid)))
                throw new InvalidOperationException("Campaign original material coverage is invalid.");
            if (!shaderGuids.SetEquals(input.renderCases.Select(row => row.shaderGuid)))
                throw new InvalidOperationException("Campaign shader reference rendering coverage is incomplete.");
            return input;
        }

        private static string ExactAsset(string guid, string path)
        {
            if (!Guid(guid) || string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.Split('/').Any(part => part == ".." || part == "." || part.Length == 0) || path.Contains("\\") ||
                AssetDatabase.GUIDToAssetPath(guid) != path || AssetDatabase.AssetPathToGUID(path) != guid)
                throw new InvalidOperationException("Campaign asset no longer retains its captured original GUID/path association.");
            return path;
        }
        private static void RejectShaderErrors(Shader shader)
        {
            if (shader == null) throw new InvalidOperationException("Campaign shader asset is missing.");
            foreach (var message in ShaderUtil.GetShaderMessages(shader))
                if (message.severity == ShaderCompilerMessageSeverity.Error)
                    throw new InvalidOperationException("Campaign shader compile error: " + shader.name + ": " + message.message);
        }
        private static bool Guid(string value) { return value != null && Regex.IsMatch(value, @"^[0-9a-f]{32}$"); }
        private static bool HashValue(string value) { return value != null && Regex.IsMatch(value, @"^[0-9a-f]{64}$"); }
        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
