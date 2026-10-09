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
        public static int LastNativeCompileCount { get; private set; }
        public static bool LastValidationCacheReused { get; private set; }

        [Serializable] public sealed class Variant
        {
            public int subshader, pass, hardwareTier;
            public string stereo, passType, vertexOriginalDxbcSha256, fragmentOriginalDxbcSha256, fragmentOutput = "color";
            public string[] keywords;
            public bool requiresFragmentEyeRouting, viewInvariant;
        }
        [Serializable] public sealed class OriginalShader
        {
            public string guid, assetPath, originalName, originalSerializedFile, sourceSha256, sourceRestoration;
            public long originalPathId;
            public Variant[] variants;
            public RetainedContract retainedSourceContract;
        }
        [Serializable] public sealed class RetainedContract { public string sourceSha256; public SourceProvenance originalProvenance; }
        [Serializable] public sealed class SourceProvenance { public string receipt, receiptSha256; public RetainedShader shader; }
        [Serializable] public sealed class ImportUpgrade { public string kind, sha256; public int replacements; }
        [Serializable] public sealed class RetainedShader
        {
            public string guid, name, assetPath, sourceSha256, canonicalRecipeSha256;
            public long originalPathId;
            public ImportUpgrade importUpgrade;
        }
        [Serializable] public sealed class RetainedReceipt { public RetainedShader[] shaders; }
        [Serializable] public sealed class OriginalMaterial
        {
            public string guid, assetPath, shaderGuid;
            public bool originalEngineBuiltinShader, originalShaderNull, nativeFontImporterSubObject;
            public long shaderFileId;
        }
        [Serializable] public sealed class RenderCase
        {
            public string id, shaderGuid, materialGuid, meshGuid;
        }
        [Serializable] public sealed class Manifest
        {
            public int schema, requiredShaderCount, requiredMaterialCount, requiredHostRenderTargetCount;
            public string scope, graphicsApi, compilerPlatform;
            public OriginalShader[] shaders;
            public OriginalMaterial[] materials;
            public RenderCase[] renderCases;
            public SourceProgram[] programs;
        }
        [Serializable] public sealed class NativeSignature
        {
            public string semantic;
            public int semanticIndex, componentType, mask, readWriteMask;
        }
        [Serializable] public sealed class SourceProgram
        {
            public string assetPath, sourceSha256, originalDxbcSha256, originalInterfaceSha256;
            public NativeSignature[] originalOutputSignature;
        }
        [Serializable] public sealed class CompiledProgram
        {
            public string guid, stereo, glesSha256, file, bankSha256, vertexSha256, fragmentSha256, vertexFile, fragmentFile;
            public QuestVulkanShaderValidation.Image[] images;
            public int subshader, pass, hardwareTier;
            public string[] keywords;
            public bool vertexCompiled, fragmentCompiled, vertexEyeRoutingObserved, fragmentEyeRoutingObserved, multiviewLayoutObserved;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, materialCount;
            public string unityVersion, compilerPlatform = "GLES3x", graphicsApi, sourceManifestSha256;
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
            LastNativeCompileCount = 0;
            LastValidationCacheReused = false;
            if (Application.unityVersion != "2021.3.5f1")
                throw new InvalidOperationException("Campaign shader evidence requires original Unity 2021.3.5f1.");
            var input = Read(manifestPath);
            RequireGraphicsHost(input);
            VerifyProgramSources(input);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Directory.CreateDirectory(outputPath);
            string receiptPath = Path.Combine(outputPath, "android-compiler.json");
            bool vulkan = input.graphicsApi == "Vulkan";
            var backend = vulkan ? GraphicsDeviceType.Vulkan : GraphicsDeviceType.OpenGLES3;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(backend))
                throw new InvalidOperationException("Campaign compiler gate requires its declared Android graphics backend.");
            var before = vulkan ? QuestCampaignShaderCache.Capture(input, manifestPath) : null;
            bool reused = vulkan && QuestCampaignShaderCache.TryReuse(input, before, outputPath);
            if (!reused)
            {
                QuestCampaignShaderCache.Invalidate(outputPath);
                if (File.Exists(receiptPath)) File.Delete(receiptPath);
            }
            var shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
            var programs = new List<CompiledProgram>();
            var originalOutputs = (input.programs ?? new SourceProgram[0])
                .GroupBy(program => program.originalDxbcSha256, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().originalOutputSignature, StringComparer.Ordinal);
            foreach (var row in input.shaders)
            {
                string path = ExactAsset(row.guid, row.assetPath);
                if (!SourceMatches(row, Hash(File.ReadAllBytes(path))))
                    throw new InvalidOperationException("Campaign translated shader bytes differ: " + row.guid);
                // Complete pending imports before querying current banks.
                // A diagnostic force reimport is available for an existing
                // private library after replacing native instruction includes.
                if (Environment.GetEnvironmentVariable("GHVR_QUEST_FORCE_SHADER_IMPORT") == "1")
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
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
                    if (reused) continue;
                    NativeSignature[] nativeOutputs = null;
                    if (input.scope == "campaign-compiler" && (!originalOutputs.TryGetValue(variant.fragmentOriginalDxbcSha256, out nativeOutputs) || nativeOutputs == null))
                        throw new InvalidOperationException("Native fragment output signature is missing from instruction evidence.");
                    string key = row.guid + "-" + variant.subshader + "-" + variant.pass + "-t" + variant.hardwareTier + "-" + Hash(Encoding.UTF8.GetBytes(string.Join("\n", variant.keywords))).Substring(0, 16);
                    if (vulkan)
                    {
                        ++LastNativeCompileCount;
                        var actual = QuestVulkanShaderValidation.Compile(shader, variant.subshader, variant.pass, variant.keywords, variant.hardwareTier);
                        VerifyVulkanBank(actual, variant.fragmentOutput, nativeOutputs);
                        if (variant.stereo == "multiview" && (!actual.vertexEyeRoutingObserved && !variant.viewInvariant || variant.requiresFragmentEyeRouting && !actual.fragmentEyeRoutingObserved))
                            throw new InvalidOperationException("Campaign Vulkan multiview bank omits required native eye routing.");
                        string bankFile = actual.bankSha256 + ".vulkan", vertexFile = actual.vertexSha256 + ".vertex.spv", fragmentFile = actual.fragmentSha256 + ".fragment.spv";
                        WriteNativeBank(Path.Combine(outputPath, bankFile), actual.bank, actual.bankSha256);
                        WriteNativeBank(Path.Combine(outputPath, vertexFile), actual.vertex, actual.vertexSha256);
                        WriteNativeBank(Path.Combine(outputPath, fragmentFile), actual.fragment, actual.fragmentSha256);
                        programs.Add(new CompiledProgram {
                            guid = row.guid, subshader = variant.subshader, pass = variant.pass, hardwareTier = variant.hardwareTier, stereo = variant.stereo,
                            keywords = variant.keywords, vertexCompiled = true, fragmentCompiled = true,
                            vertexEyeRoutingObserved = actual.vertexEyeRoutingObserved, fragmentEyeRoutingObserved = actual.fragmentEyeRoutingObserved,
                            multiviewLayoutObserved = actual.inputs.Any(value => value.builtin == 4440),
                            bankSha256 = actual.bankSha256, vertexSha256 = actual.vertexSha256, fragmentSha256 = actual.fragmentSha256,
                            file = bankFile, vertexFile = vertexFile, fragmentFile = fragmentFile, images = actual.images
                        });
                        if (programs.Count % 100 == 0) Debug.Log("Campaign native Android bank coverage: " + programs.Count);
                        continue;
                    }
                    ++LastNativeCompileCount;
                    var vertex = pass.CompileVariant(ShaderType.Vertex, variant.keywords, ShaderCompilerPlatform.GLES3x, BuildTarget.Android, (GraphicsTier)variant.hardwareTier);
                    // GLES3x returns the entire linked native bank through the
                    // Vertex query. Verify both emitted stages in those bytes.
                    if (!vertex.Success || vertex.ShaderData == null || vertex.ShaderData.Length == 0)
                        throw new InvalidOperationException("Actual Campaign Android shader bank failed: " + row.guid + " / tier=" + variant.hardwareTier + " / " + string.Join(" ", variant.keywords) + " / " + string.Join("; ", vertex.Messages.Select(message => message.message + " at " + message.file + ":" + message.line)));
                    string source = new UTF8Encoding(false, true).GetString(vertex.ShaderData);
                    try { VerifyBank(source, variant.fragmentOutput, nativeOutputs); }
                    catch (InvalidOperationException error)
                    {
                        File.WriteAllBytes(Path.Combine(outputPath, "failed-native-bank.glsl"), vertex.ShaderData);
                        throw new InvalidOperationException(error.Message + " / " + row.guid + " / subshader=" + variant.subshader + " / pass=" + variant.pass + " / tier=" + variant.hardwareTier + " / " + string.Join(" ", variant.keywords), error);
                    }
                    bool eyeRouting = Regex.IsMatch(source, @"\bgl_ViewID_OVR\b") || Regex.IsMatch(source, @"\bgl_InstanceID\b") && Regex.IsMatch(source, @"\bunity_StereoEyeIndex\b");
                    string fragmentBank = source.Substring(source.IndexOf("#ifdef FRAGMENT", StringComparison.Ordinal));
                    bool fragmentEye = Regex.IsMatch(fragmentBank, @"\b(?:unity_StereoEyeIndex|vs_BLENDINDICES0)\b");
                    bool multiviewLayout = Regex.IsMatch(source, @"layout\s*\(\s*num_views\s*=\s*2\s*\)");
                    if (variant.stereo == "multiview" && (!multiviewLayout || !eyeRouting && !variant.viewInvariant || variant.requiresFragmentEyeRouting && !fragmentEye))
                        throw new InvalidOperationException("Campaign multiview bank omits required native eye routing.");
                    string file = key + ".glsl";
                    File.WriteAllBytes(Path.Combine(outputPath, file), vertex.ShaderData);
                    programs.Add(new CompiledProgram {
                        guid = row.guid, subshader = variant.subshader, pass = variant.pass, hardwareTier = variant.hardwareTier, stereo = variant.stereo,
                        keywords = variant.keywords, vertexCompiled = true, fragmentCompiled = true,
                        vertexEyeRoutingObserved = eyeRouting, fragmentEyeRoutingObserved = fragmentEye, multiviewLayoutObserved = multiviewLayout, glesSha256 = Hash(vertex.ShaderData), file = file
                    });
                    if (programs.Count % 100 == 0) Debug.Log("Campaign native Android bank coverage: " + programs.Count);
                }
                RejectShaderErrors(shader);
            }
            VerifyImportedMaterials(input, shaders);
            if (reused)
            {
                if (QuestCampaignShaderCache.ClosureHash(before) != QuestCampaignShaderCache.ClosureHash(QuestCampaignShaderCache.Capture(input, manifestPath)))
                    throw new InvalidOperationException("Native graphics inputs changed during completed evidence reuse.");
                LastValidationCacheReused = true;
                Debug.Log("PASS reused completed Campaign Android shader gate: shaders=" + input.shaders.Length + ", native aliases=" + input.shaders.Sum(row => row.variants.Length) + ", materials=" + input.materials.Length + ". Native outputs verified; original pixels and headset remain separate gates.");
                return;
            }
            File.WriteAllText(receiptPath, JsonUtility.ToJson(new Receipt {
                unityVersion = Application.unityVersion, compilerPlatform = vulkan ? "Vulkan" : "GLES3x", graphicsApi = vulkan ? "Vulkan" : "GLES3", sourceManifestSha256 = Hash(File.ReadAllBytes(manifestPath)),
                materialCount = input.materials.Length, programs = programs.ToArray()
            }, true) + "\n");
            if (vulkan) QuestCampaignShaderCache.Complete(input, before, QuestCampaignShaderCache.Capture(input, manifestPath), outputPath);
            Debug.Log("PASS Campaign Android shader gate: materials=" + input.materials.Length + ", actual banks=" + programs.Count + ". Original pixels and headset remain separate gates.");
        }

        public static void VerifyImportedMaterials(Manifest input, Dictionary<string, Shader> shaders)
        {
            var progress = new QuestWizardProgress.Counter("unity-shader-materials", null, input.materials.Length, "assets", "Validate original Shader and material identities");
            int done = 0;
            foreach (var row in input.materials)
            {
                progress.Report(done, row.assetPath);
                string path = ExactAsset(row.guid, row.assetPath);
                var material = row.nativeFontImporterSubObject ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().SingleOrDefault() : AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new InvalidOperationException("Campaign native material is missing: " + row.guid);
                if (row.originalShaderNull)
                {
                    if (!Regex.IsMatch(File.ReadAllText(path), @"(?m)^  m_Shader: \{fileID: 0\}$"))
                        throw new InvalidOperationException("Original null shader PPtr has changed: " + row.guid);
                    progress.Report(++done, row.assetPath);
                    continue;
                }
                if (row.originalEngineBuiltinShader)
                {
                    string guid; long fileId;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(material.shader, out guid, out fileId) || guid != row.shaderGuid || fileId != row.shaderFileId)
                        throw new InvalidOperationException("Original engine shader PPtr has changed: " + row.guid);
                    progress.Report(++done, row.assetPath);
                    continue;
                }
                Shader shader;
                if (!shaders.TryGetValue(row.shaderGuid, out shader) || material.shader != shader)
                    throw new InvalidOperationException("Campaign material no longer uses its exact original shader: " + row.guid);
                progress.Report(++done, row.assetPath);
            }
            progress.Complete("Original Shader and material identities validated");
        }

        public static void VerifyImportedIdentities(Manifest input)
        {
            RequireGraphicsHost(input);
            VerifyProgramSources(input);
            var shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
            var progress = new QuestWizardProgress.Counter("unity-shader-identities", null, input.shaders.Length, "shaders", "Validate imported original Shader identities");
            foreach (var row in input.shaders)
            {
                progress.Report(shaders.Count, row.assetPath);
                string path = ExactAsset(row.guid, row.assetPath);
                if (!SourceMatches(row, Hash(File.ReadAllBytes(path)))) throw new InvalidOperationException("Campaign translated shader bytes differ: " + row.guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || shader.name != row.originalName || shader.name == "Hidden/InternalErrorShader") throw new InvalidOperationException("Campaign shader import changes its original identity: " + row.guid);
                RejectShaderErrors(shader);
                foreach (var bank in row.variants.GroupBy(value => value.subshader + "/" + value.pass + "/" + value.passType).Select(group => group.First())) ImportedCollectionPassType(shader, bank);
                shaders.Add(row.guid, shader);
                progress.Report(shaders.Count, row.assetPath);
            }
            progress.Complete("Imported original Shader identities validated");
            VerifyImportedMaterials(input, shaders);
        }

        public static void PrepareVariantCollection()
        {
            PrepareVariantCollection(Environment.GetEnvironmentVariable("GHVR_QUEST_SHADER_MANIFEST") ?? DefaultManifest);
        }

        public static void PrepareVariantCollection(string manifestPath)
        {
            var input = Read(manifestPath);
            RequireGraphicsHost(input);
            VerifyProgramSources(input);
            const string path = "Assets/Resources/QuestCampaignShaderVariants.shadervariants";
            Directory.CreateDirectory("Assets/Resources");
            var collection = new ShaderVariantCollection();
            var retained = new HashSet<string>(StringComparer.Ordinal);
            int nativeAliases = 0;
            var progress = new QuestWizardProgress.Counter("unity-shader-retention", null, input.shaders.Sum(row => row.variants.Length), "variants", "Retain original native Shader aliases without compiling an extra matrix");
            foreach (var row in input.shaders)
            {
                progress.Report(nativeAliases, row.assetPath);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ExactAsset(row.guid, row.assetPath));
                if (shader == null || shader.name != row.originalName) throw new InvalidOperationException("Native shader source is missing before variant retention.");
                var passTypes = new Dictionary<string, PassType>(StringComparer.Ordinal);
                foreach (var bank in row.variants)
                {
                    string passKey = bank.subshader + "/" + bank.pass + "/" + bank.passType;
                    PassType type;
                    if (!passTypes.TryGetValue(passKey, out type))
                    {
                        type = ImportedCollectionPassType(shader, bank);
                        passTypes.Add(passKey, type);
                    }
                    var variant = new ShaderVariantCollection.ShaderVariant(shader, type, bank.keywords);
                    collection.Add(variant);
                    if (!collection.Contains(variant)) throw new InvalidOperationException("Original native shader collection entry was not retained.");
                    retained.Add(row.guid + "/" + type + "/" + string.Join(" ", bank.keywords.OrderBy(value => value, StringComparer.Ordinal)));
                    ++nativeAliases;
                    progress.Report(nativeAliases, row.assetPath);
                }
            }
            progress.Complete("Original native Shader aliases retained");
            // SVC deliberately has no pass ordinal or hardware-tier field.
            // Exact bank coverage still lives in the original native manifest;
            // verify its complete projection into this coarser retention API.
            if (collection.shaderCount != input.shaders.Length || collection.variantCount != retained.Count)
                throw new InvalidOperationException("Original native shader collection census changed.");
            var existing = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
            if (existing != null) { EditorUtility.CopySerialized(collection, existing); UnityEngine.Object.DestroyImmediate(collection); }
            else AssetDatabase.CreateAsset(collection, path);
            AssetDatabase.SaveAssets();
            // Keeping the collection in Resources retains its exact witnessed
            // banks without warming tens of thousands of programs at startup.
            // The game does not need to call WarmUp or load it synchronously.
            Debug.Log("Retained original Campaign shader banks in Resources: shaders=" + input.shaders.Length + ", native aliases=" + nativeAliases + ", unique collection entries=" + retained.Count + "; no startup shader warmup.");
        }

        public static PassType ImportedCollectionPassType(Shader shader, Variant bank)
        {
            PassType expected;
            if (string.IsNullOrEmpty(bank.passType) || !Enum.TryParse(bank.passType, false, out expected) || !Enum.IsDefined(typeof(PassType), expected))
                throw new InvalidOperationException("Original native LightMode has no proven shader collection PassType.");
            var data = ShaderUtil.GetShaderData(shader);
            if (bank.subshader < 0 || bank.subshader >= data.SubshaderCount)
                throw new InvalidOperationException("Native collection entry loses its original subshader.");
            var subshader = data.GetSubshader(bank.subshader);
            if (bank.pass < 0 || bank.pass >= subshader.PassCount)
                throw new InvalidOperationException("Native collection entry loses its original pass.");
            string mode = subshader.GetPass(bank.pass).FindTagValue(new ShaderTagId("LightMode")).name;
            PassType actual;
            switch ((mode ?? "").ToUpperInvariant())
            {
                case "": case "ALWAYS": actual = PassType.Normal; break;
                case "FORWARDBASE": actual = PassType.ForwardBase; break;
                case "FORWARDADD": actual = PassType.ForwardAdd; break;
                case "SHADOWCASTER": actual = PassType.ShadowCaster; break;
                case "DEFERRED": actual = PassType.Deferred; break;
                case "META": actual = PassType.Meta; break;
                case "MOTIONVECTORS": actual = PassType.MotionVectors; break;
                case "PREPASSBASE": actual = PassType.LightPrePassBase; break;
                case "PREPASSFINAL": actual = PassType.LightPrePassFinal; break;
                case "VERTEX": actual = PassType.Vertex; break;
                case "VERTEXLM": actual = PassType.VertexLM; break;
                case "VERTEXLMRGBM": actual = PassType.VertexLMRGBM; break;
                case "SRPDEFAULTUNLIT": actual = PassType.ScriptableRenderPipelineDefaultUnlit; break;
                default: throw new InvalidOperationException("Imported native LightMode is unsupported: " + mode);
            }
            if (actual != expected)
                throw new InvalidOperationException("Native shader collection PassType differs from imported pass: " + shader.name + " / " + bank.subshader + "/" + bank.pass + " / expected=" + expected + " / actual=" + actual);
            return actual;
        }

        private static void VerifyProgramSources(Manifest input)
        {
            if (input.scope != "campaign-compiler") return;
            if (input.programs == null || input.programs.Length == 0) throw new InvalidOperationException("Native instruction include manifest is missing.");
            var progress = new QuestWizardProgress.Counter("unity-shader-programs", null, input.programs.Length, "files", "Validate original translated Shader include files");
            int done = 0;
            foreach (var program in input.programs)
            {
                string path = program.assetPath.Replace('\\', '/');
                progress.Report(done, path);
                if (!path.StartsWith("Assets/QuestOriginalCampaign/ShaderPrograms/", StringComparison.Ordinal) || path.Contains("..") ||
                    !HashValue(program.originalDxbcSha256) || !HashValue(program.originalInterfaceSha256) ||
                    Hash(File.ReadAllBytes(path)) != program.sourceSha256)
                    throw new InvalidOperationException("Native shader instruction include changed: " + path);
                progress.Report(++done, path);
            }
            progress.Complete("Original translated Shader include files validated");
        }

        private static void RequireGraphicsHost(Manifest input)
        {
            // ShaderData exposes the host-supported imported pass list. Unity's
            // Null device silently removes native MRT/Deferred passes and
            // changes their ordinals before a compiler query can validate them.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ||
                SystemInfo.supportedRenderTargetCount < Math.Max(1, input.requiredHostRenderTargetCount))
                throw new InvalidOperationException("Native Campaign shader coverage requires an actual graphics host with original MRT support; omit -nographics (Linux: Xvfb/OpenGLCore).");
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
            var result = BuildPipeline.BuildAssetBundles(outputPath, builds, BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
            if (result == null) throw new InvalidOperationException("Actual Windows candidate shader bundle build failed.");
            foreach (var row in input.shaders)
                RejectShaderErrors(AssetDatabase.LoadAssetAtPath<Shader>(ExactAsset(row.guid, row.assetPath)));
            Debug.Log("Built native D3D11 shader-reference candidates; no rendering outcome is asserted.");
        }

        public static void VerifyBank(string source) { VerifyBank(source, "color"); }

        public static void VerifyBank(string source, string originalFragmentOutput)
        {
            VerifyBank(source, originalFragmentOutput, null);
        }

        public static void VerifyBank(string source, string originalFragmentOutput, NativeSignature[] nativeOutputs)
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
            VerifyStageLink(vertexSource, fragmentSource);
            if (!Regex.IsMatch(vertexSource, @"#version 3[01]0 es") || !Regex.IsMatch(fragmentSource, @"#version 3[01]0 es"))
                throw new InvalidOperationException("Campaign bank is not native GLES3.");
            if (!Regex.IsMatch(vertexSource, @"\bvoid\s+main\s*\(\s*\)") || !Regex.IsMatch(fragmentSource, @"\bvoid\s+main\s*\(\s*\)"))
                throw new InvalidOperationException("Campaign bank loses a native stage entry.");
            if (!Regex.IsMatch(vertexSource, @"\bgl_Position(?:\.[xyzwrgba]+)?\s*="))
                throw new InvalidOperationException("Campaign vertex bank does not emit geometry.");
            if (originalFragmentOutput == "depth")
            {
                if (!Regex.IsMatch(fragmentSource, @"\bgl_FragDepth\s*="))
                    throw new InvalidOperationException("Campaign fragment bank loses its original depth output.");
            }
            else if (originalFragmentOutput == "color")
            {
                var expected = nativeOutputs == null ? new[] { new NativeSignature {
                    semantic = "SV_Target", semanticIndex = 0, componentType = 3, mask = 15 } } :
                    nativeOutputs.Where(value => value.semantic.Equals("SV_Target", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (expected.Length == 0) throw new InvalidOperationException("Native color bank lacks its original target signature.");
                foreach (var target in expected)
                {
                    var outputs = Regex.Matches(fragmentSource, @"layout\s*\(\s*location\s*=\s*" + target.semanticIndex +
                        @"\s*\)\s*out\s+(?:(?:highp|mediump|lowp)\s+)?(float|int|uint|[iu]?vec[234])\s+(\w+)\s*;");
                    if (outputs.Count != 1) throw new InvalidOperationException("Campaign fragment bank loses a unique native render target.");
                    string type = outputs[0].Groups[1].Value, name = outputs[0].Groups[2].Value;
                    int componentType = type == "uint" || type.StartsWith("uvec", StringComparison.Ordinal) ? 1 :
                        type == "int" || type.StartsWith("ivec", StringComparison.Ordinal) ? 2 : 3;
                    int width = type.EndsWith("2", StringComparison.Ordinal) ? 2 : type.EndsWith("3", StringComparison.Ordinal) ? 3 : type.EndsWith("4", StringComparison.Ordinal) ? 4 : 1;
                    int written = target.mask & ~target.readWriteMask;
                    if (componentType != target.componentType || (written & ~((1 << width) - 1)) != 0 ||
                        !Regex.IsMatch(fragmentSource, @"\b" + Regex.Escape(name) + @"(?:\.[xyzwrgba]+)?\s*="))
                        throw new InvalidOperationException("Campaign fragment bank changes native target type/written components.");
                }
            }
            else if (originalFragmentOutput != "none")
                throw new InvalidOperationException("Campaign original fragment output contract is unknown.");
        }

        private static void WriteNativeBank(string path, byte[] bytes, string expected)
        {
            // Only a fresh, validated compiler payload can repair its own
            // content-addressed evidence leaf. Never overwrite arbitrary files
            // named by a stale receipt. Removing a damaged leaf also avoids
            // mutating another file through an accidental hardlink/symlink.
            if (!HashValue(expected) || bytes == null || Hash(bytes) != expected ||
                !Regex.IsMatch(Path.GetFileName(path), "^" + expected + @"\.(vulkan|vertex\.spv|fragment\.spv)$"))
                throw new InvalidOperationException("Native Vulkan evidence repair has an unsafe address or unverified payload.");
            if (File.Exists(path) && Hash(File.ReadAllBytes(path)) == expected) return;
            string temp = path + "." + System.Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        public static bool SourceMatches(OriginalShader row, string actual)
        {
            if (actual == row.sourceSha256) return true;
            string name, original, upgraded, recipe; long nativeId; int replacements;
            switch (row.guid)
            {
                case "30881e480b10c1b46a3d99ec13496f5e":
                    name="Hidden/BlendForBloom"; nativeId=135; replacements=2;
                    original="84f4f797c3f77fca2797806b388d50bdbac7492118212ded78370a6d9d636ae5";
                    upgraded="d22461e93e8d3bd6801fe12a8ea8a12632d870fd54afc4d34dca839337838abf";
                    recipe="263f263605bc9ce88c366f33e895b696b32faf4aa812979f41e508e23fe48653"; break;
                case "93f40d5ea0c0a7945a5782e2dcd23833":
                    name="Hidden/BrightPassFilter2"; nativeId=132; replacements=1;
                    original="26e81ea437fbb5ffb45cb8fc4556bdb2ab6c1c50d5396af5c2c8bc1562b8e424";
                    upgraded="8a19269566f8fd692a44eb607c44f114abbc0a555d11c06995de22e2112e8dc1";
                    recipe="feba6bb83a31a7dc0f7388eb0aa7c9af897f76f1c17b1b5750f2e1710ebe854a"; break;
                case "29d4384c2ae952c4597a9d894d381163":
                    name="Hidden/BlurAndFlares"; nativeId=137; replacements=4;
                    original="63283e8f5e60b6a7c2771b175c1c82fe62813648306ccf185c56379f75b196eb";
                    upgraded="343d875aed4f5f221ce7d5e33df24ffc90459d9533448d7ba9edca80fa40d390";
                    recipe="4c6e89d84f8d16d060162186390ead698e0e528c805dfd1e4de75359a457cb17"; break;
                default: return false;
            }
            var contract=row.retainedSourceContract; var proof=contract == null ? null : contract.originalProvenance;
            var shader=proof == null ? null : proof.shader; var upgrade=shader == null ? null : shader.importUpgrade;
            if (row.sourceRestoration != "retained-source-contract" || row.originalName != name || row.originalPathId != nativeId ||
                row.sourceSha256 != original || actual != upgraded || contract == null || proof == null || contract.sourceSha256 != original ||
                shader == null || shader.guid != row.guid || shader.name != name || shader.assetPath != row.assetPath ||
                shader.originalPathId != nativeId || shader.sourceSha256 != original || shader.canonicalRecipeSha256 != recipe ||
                upgrade == null || upgrade.kind != "UnityObjectToClipPos" || upgrade.replacements != replacements || upgrade.sha256 != upgraded ||
                proof.receipt != "QuestStartupEvidence/legacy-post-effects.json" || !File.Exists(proof.receipt) ||
                Hash(File.ReadAllBytes(proof.receipt)) != proof.receiptSha256) return false;
            var receipt=JsonUtility.FromJson<RetainedReceipt>(File.ReadAllText(proof.receipt));
            var witnessed=(receipt.shaders ?? new RetainedShader[0]).Where(value => value.guid == row.guid).ToArray();
            return witnessed.Length == 1 && JsonUtility.ToJson(witnessed[0]) == JsonUtility.ToJson(shader);
        }

        private static void VerifyVulkanBank(QuestVulkanShaderValidation.Result bank, string output, NativeSignature[] nativeOutputs)
        {
            foreach (var input in bank.inputs.Where(value => value.stage == "fragment" && value.location >= 0))
            {
                var candidates = bank.outputs.Where(value => value.stage == "vertex" && value.location == input.location).ToArray();
                if (candidates.Length != 1 || candidates[0].componentType != input.componentType || candidates[0].bitWidth != input.bitWidth || candidates[0].components < input.components)
                    throw new InvalidOperationException("Campaign Vulkan native stage interface is incompatible: " + input.location);
            }
            if (output == "depth")
            {
                if (!bank.outputs.Any(value => value.stage == "fragment" && value.builtin == 22 && value.componentType == 3 && value.components == 1))
                    throw new InvalidOperationException("Campaign Vulkan fragment bank loses its original depth output.");
            }
            else if (output == "color")
            {
                var expected = nativeOutputs == null ? new[] { new NativeSignature { semantic = "SV_Target", componentType = 3, mask = 15 } } :
                    nativeOutputs.Where(value => value.semantic.Equals("SV_Target", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (expected.Length == 0) throw new InvalidOperationException("Campaign Vulkan bank lacks original target signatures.");
                foreach (var target in expected)
                {
                    var values = bank.outputs.Where(value => value.stage == "fragment" && value.location == target.semanticIndex).ToArray();
                    if (values.Length != 1 || values[0].componentType != target.componentType || values[0].bitWidth != 32 ||
                        (target.mask & ~target.readWriteMask & ~((1 << values[0].components) - 1)) != 0)
                        throw new InvalidOperationException("Campaign Vulkan fragment bank changes native target type/written components.");
                }
            }
            else if (output != "none") throw new InvalidOperationException("Campaign Vulkan native fragment output contract is unknown.");
        }

        private static void VerifyStageLink(string vertex, string fragment)
        {
            // CompileVariant success means HLSLcc emitted source. It does not
            // prove that a GLES driver can link mismatched varying declarations.
            // Compare actual emitted types and array extents. GLSL ES3.10
            // permits interpolation qualifiers to differ between linked
            // stages; the fragment qualifier controls the interpolation.
            const string pattern = @"(?m)^\s*(?:layout\s*\([^\n]*\)\s*)?(?<qual>(?:(?:flat|smooth|centroid|noperspective|sample)\s+)*){0}\s+(?:(?:highp|mediump|lowp)\s+)?(?<type>\w+)\s+(?<name>\w+)\s*(?<array>\[[^\]\n]+\])?\s*;";
            var outputs = Regex.Matches(vertex, string.Format(pattern, "out")).Cast<Match>()
                .ToDictionary(match => match.Groups["name"].Value, match => match, StringComparer.Ordinal);
            foreach (Match input in Regex.Matches(fragment, string.Format(pattern, "in")))
            {
                Match output;
                string name = input.Groups["name"].Value;
                if (!outputs.TryGetValue(name, out output) || output.Groups["type"].Value != input.Groups["type"].Value ||
                    output.Groups["array"].Value != input.Groups["array"].Value)
                    throw new InvalidOperationException("Campaign emitted GLES stages have incompatible native varying: " + name);
            }
            foreach (Match sampler in Regex.Matches(fragment, @"\buniform\s+(?:(?:highp|mediump|lowp)\s+)?samplerCubeShadow\s+(\w+)\s*;"))
                if (Regex.IsMatch(fragment, @"\btextureLod\s*\(\s*" + Regex.Escape(sampler.Groups[1].Value) + @"\b"))
                    throw new InvalidOperationException("Campaign cube shadow emits an unavailable GLES explicit-LOD overload.");
        }

        private static Manifest Read(string path)
        {
            var input = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            if (input == null || input.schema != 1 || input.scope != "campaign" && input.scope != "campaign-compiler" || input.shaders == null || input.shaders.Length == 0 ||
                input.materials == null || input.scope == "campaign" && input.materials.Length == 0 || input.materials.Length != input.requiredMaterialCount ||
                input.scope == "campaign" && (input.renderCases == null || input.renderCases.Length == 0) ||
                input.scope == "campaign-compiler" && input.requiredShaderCount != input.shaders.Length)
                throw new InvalidOperationException("Campaign shader identity/coverage receipt is incomplete.");
            var shaderGuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in input.shaders)
            {
                if (row == null || !Guid(row.guid) || !shaderGuids.Add(row.guid) || !HashValue(row.sourceSha256) ||
                    string.IsNullOrEmpty(row.originalSerializedFile) || string.IsNullOrEmpty(row.originalName) || row.variants == null || row.variants.Length == 0)
                    throw new InvalidOperationException("Campaign shader identity/provenance is incomplete or duplicated.");
                var banks = new HashSet<string>(StringComparer.Ordinal);
                foreach (var bank in row.variants)
                    if (bank == null || bank.subshader < 0 || bank.pass < 0 || bank.hardwareTier < 0 || bank.hardwareTier > 2 || bank.keywords == null ||
                        bank.keywords.Any(keyword => !Regex.IsMatch(keyword, @"^[A-Za-z0-9_]+$")) || bank.keywords.Distinct().Count() != bank.keywords.Length ||
                        !HashValue(bank.vertexOriginalDxbcSha256) || !HashValue(bank.fragmentOriginalDxbcSha256) ||
                        bank.stereo != "mono" && bank.stereo != "instancing" && bank.stereo != "multiview" ||
                        bank.stereo == "multiview" && !bank.keywords.Contains("STEREO_MULTIVIEW_ON") ||
                        !banks.Add(bank.subshader + ":" + bank.pass + ":" + bank.hardwareTier + ":" + string.Join(" ", bank.keywords.OrderBy(value => value, StringComparer.Ordinal))))
                        throw new InvalidOperationException("Campaign original shader bank provenance is invalid or duplicated.");
            }
            if (input.materials.Select(row => row.guid).Distinct().Count() != input.materials.Length ||
                input.materials.Any(row => !Guid(row.guid) || !shaderGuids.Contains(row.shaderGuid) && !row.originalShaderNull && !row.originalEngineBuiltinShader))
                throw new InvalidOperationException("Campaign original material coverage is invalid.");
            if (input.scope == "campaign" && !shaderGuids.SetEquals(input.renderCases.Select(row => row.shaderGuid)))
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
