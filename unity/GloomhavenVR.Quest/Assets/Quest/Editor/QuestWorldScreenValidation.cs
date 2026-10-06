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
    /// <summary>Verifies the actual world-screen capture samplers for the configured Android backend.</summary>
    public static class QuestWorldScreenValidation
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
        public const string SourcePath = "Assets/Quest/Resources/QuestWorldScreen.shader";
        public const string GlassSourcePath = "Assets/Quest/Resources/QuestWorldScreenGlass.shader";
        public const string ReceiptPath = "QuestStartupEvidence/world-screen.android-validation.json";
        [Serializable] public sealed class Bank
        {
            public string keyword, compiledSha256, vertexSha256, fragmentSha256;
            public int compiledBytes;
            public bool plainCaptureSamplers, stereoEyeRouting;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, passes = 1, compiledStages = 6;
            public string compilerPlatform, unityVersion, sourceSha256, stereoRenderingPath;
            public bool androidAssetsBuilt, allGlesPassStagesCompiled, allVulkanPassStagesCompiled, hardwareVisualsVerified;
            public bool multiPassTextureRouting;
            public Bank[] banks;
            public string glassSourceSha256;
            public int glassCompiledStages;
            public Bank[] glassBanks;
        }

        public static void Validate(bool androidAssetsBuilt)
        {
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(RequiredGraphicsApi))
                throw new InvalidOperationException("World-screen validation requires configured Android " + CompilerPlatform + ".");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SourcePath);
            if (shader == null || shader.name != "Hidden/GloomhavenVR/QuestWorldScreen" || shader.GetPropertyCount() != 3)
                throw new InvalidOperationException("Quest world-screen shader identity or ABI differs.");
            foreach (string property in new[] { "_MainTex", "_RightTex" })
            {
                int index = shader.FindPropertyIndex(property);
                if (index < 0 || shader.GetPropertyType(index) != ShaderPropertyType.Texture ||
                    shader.GetPropertyTextureDimension(index) != TextureDimension.Tex2D)
                    throw new InvalidOperationException("Owned world-screen capture must be plain Tex2D: " + property);
            }
            int stereo = shader.FindPropertyIndex("_StereoCapture");
            if (stereo < 0 || shader.GetPropertyType(stereo) != ShaderPropertyType.Float)
                throw new InvalidOperationException("Quest world-screen stereo-routing ABI differs.");
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null || data.SubshaderCount != 1 || data.GetSubshader(0).PassCount != 1)
                throw new InvalidOperationException("Quest world-screen pass count differs.");
            var pass = data.GetSubshader(0).GetPass(0);
            var banks = new List<Bank>();
#if GHVR_QUEST_GAME
            // The existing mod selects its left/right ordinary capture on each
            // camera eye pass. Its Vulkan MultiPass shader therefore samples the
            // live _MainTex without a GPU eye varying. A future single-pass mod
            // must prove both reflected captures and fragment eye routing here.
            if (PlayerSettings.stereoRenderingPath != StereoRenderingPath.MultiPass)
                throw new InvalidOperationException("Quest Vulkan world-screen currently requires the mod's validated MultiPass routing; single-pass eye routing has not been validated.");
            var compiled = QuestVulkanShaderValidation.Compile(shader, 0, 0, new string[0], GraphicsTier.Tier2);
            QuestVulkanShaderValidation.RequireColorOutput(compiled);
            QuestVulkanShaderValidation.RequirePlain2D(compiled, "_MainTex");
            banks.Add(new Bank {
                keyword = "", compiledSha256 = compiled.bankSha256,
                compiledBytes = compiled.vertex.Length + compiled.fragment.Length,
                vertexSha256 = compiled.vertexSha256, fragmentSha256 = compiled.fragmentSha256,
                plainCaptureSamplers = true, stereoEyeRouting = false
            });
            var receipt = new Receipt {
                compilerPlatform = CompilerPlatform, unityVersion = Application.unityVersion,
                sourceSha256 = Hash(File.ReadAllBytes(SourcePath)), compiledStages = 2,
                androidAssetsBuilt = androidAssetsBuilt, allGlesPassStagesCompiled = false,
                allVulkanPassStagesCompiled = true, stereoRenderingPath = "MultiPass", multiPassTextureRouting = true,
                hardwareVisualsVerified = false, banks = banks.ToArray()
            };
#else
            foreach (string keyword in new[] { "", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON" })
            {
                var compiled = pass.CompileVariant(ShaderType.Vertex,
                    keyword.Length == 0 ? new string[0] : new[] { keyword },
                    ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                    (compiled.Messages ?? new ShaderMessage[0]).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                    throw new InvalidOperationException("Quest world-screen GLES compilation failed: " + keyword);
                string[] stages = VerifiedBank(compiled.ShaderData, keyword);
                banks.Add(new Bank {
                    keyword = keyword, compiledSha256 = Hash(compiled.ShaderData), compiledBytes = compiled.ShaderData.Length,
                    vertexSha256 = Hash(Encoding.UTF8.GetBytes(stages[0])), fragmentSha256 = Hash(Encoding.UTF8.GetBytes(stages[1])),
                    plainCaptureSamplers = true, stereoEyeRouting = keyword.Length != 0
                });
            }
            var receipt = new Receipt {
                compilerPlatform = CompilerPlatform, unityVersion = Application.unityVersion, sourceSha256 = Hash(File.ReadAllBytes(SourcePath)),
                androidAssetsBuilt = androidAssetsBuilt, allGlesPassStagesCompiled = !VulkanBackend, allVulkanPassStagesCompiled = VulkanBackend,
                stereoRenderingPath = PlayerSettings.stereoRenderingPath.ToString(),
                hardwareVisualsVerified = false, banks = banks.ToArray()
            };
#endif
            receipt.glassBanks = ValidateGlass();
            receipt.glassCompiledStages = receipt.glassBanks.Length * 2;
            receipt.glassSourceSha256 = Hash(File.ReadAllBytes(GlassSourcePath));
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(receipt, true) + "\n");
            Debug.Log("[GloomhavenVR Quest] world-screen " + CompilerPlatform + " programs verified: stages=" + receipt.compiledStages + ", stereo=" + receipt.stereoRenderingPath + "");
        }

        static Bank[] ValidateGlass()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GlassSourcePath);
            if (shader == null || shader.name != "Hidden/GloomhavenVR/QuestWorldScreenGlass" || shader.GetPropertyCount() != 1
                || shader.FindPropertyIndex("_MainTex") < 0 || shader.GetPropertyType(0) != ShaderPropertyType.Texture
                || shader.GetPropertyTextureDimension(0) != TextureDimension.Tex2D)
                throw new InvalidOperationException("Quest transparent UI capture shader ABI differs.");
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null || data.SubshaderCount != 1 || data.GetSubshader(0).PassCount != 1)
                throw new InvalidOperationException("Quest transparent UI capture shader pass count differs.");
            var banks = new List<Bank>();
#if GHVR_QUEST_GAME
            var compiled = QuestVulkanShaderValidation.Compile(shader, 0, 0, new string[0], GraphicsTier.Tier2);
            QuestVulkanShaderValidation.RequireColorOutput(compiled);
            QuestVulkanShaderValidation.RequirePlain2D(compiled, "_MainTex");
            banks.Add(new Bank {
                keyword = "", compiledSha256 = compiled.bankSha256,
                compiledBytes = compiled.vertex.Length + compiled.fragment.Length,
                vertexSha256 = compiled.vertexSha256, fragmentSha256 = compiled.fragmentSha256,
                plainCaptureSamplers = true, stereoEyeRouting = false
            });
#else
            var pass = data.GetSubshader(0).GetPass(0);
            foreach (string keyword in new[] { "", "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON" })
            {
                var compiled = pass.CompileVariant(ShaderType.Vertex,
                    keyword.Length == 0 ? new string[0] : new[] { keyword }, ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0
                    || (compiled.Messages ?? new ShaderMessage[0]).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                    throw new InvalidOperationException("Quest transparent UI capture GLES compilation failed: " + keyword);
                string[] stages = QuestUiAssetValidation.VerifiedGlesSections(compiled.ShaderData);
                if (!Regex.IsMatch(stages[1], @"\bsampler2D\s+_MainTex\b") || stages[1].Contains("sampler2DArray"))
                    throw new InvalidOperationException("Quest transparent UI capture must sample ordinary 2D pixels.");
                banks.Add(new Bank {
                    keyword = keyword, compiledSha256 = Hash(compiled.ShaderData), compiledBytes = compiled.ShaderData.Length,
                    vertexSha256 = Hash(Encoding.UTF8.GetBytes(stages[0])), fragmentSha256 = Hash(Encoding.UTF8.GetBytes(stages[1])),
                    plainCaptureSamplers = true, stereoEyeRouting = false
                });
            }
#endif
            return banks.ToArray();
        }

        internal static string[] VerifiedBank(byte[] bytes, string keyword)
        {
            string[] stages = QuestUiAssetValidation.VerifiedGlesSections(bytes);
            string vertex = stages[0], fragment = stages[1];
            string declarations = Regex.Replace(fragment, @"//[^\n]*|/\*.*?\*/", "", RegexOptions.Singleline);
            foreach (string property in keyword.Length == 0 ? new[] { "_MainTex" } : new[] { "_MainTex", "_RightTex" })
            {
                if (!Regex.IsMatch(declarations, @"\bsampler2D\s+" + property + @"\b") ||
                    Regex.IsMatch(declarations, @"\bsampler2DArray\s+" + property + @"\b"))
                    throw new InvalidOperationException("Quest world-screen compiled capture sampler is not plain 2D: " + property);
                if (!declarations.Contains("texture(") || !declarations.Contains(property))
                    throw new InvalidOperationException("Quest world-screen compiled capture sampling is missing: " + property);
            }
            if (keyword.Length != 0 && !fragment.Contains("_StereoCapture"))
                throw new InvalidOperationException("Quest world-screen compiled stereo-routing switch is missing.");
            if (keyword == "STEREO_INSTANCING_ON" && !vertex.Contains("gl_InstanceID"))
                throw new InvalidOperationException("Quest world-screen instanced vertex eye selection is missing.");
            if (keyword == "STEREO_MULTIVIEW_ON" && !vertex.Contains("gl_ViewID_OVR"))
                throw new InvalidOperationException("Quest world-screen multiview vertex eye selection is missing.");
            return stages;
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
    }
}
#endif
