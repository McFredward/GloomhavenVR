#if UNITY_EDITOR
using System;
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
    /// <summary>Compiles and pins the narrow Android decoder-to-capture output adapter.</summary>
    public static class QuestVideoValidation
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
        public const string SourcePath = "Assets/Quest/Resources/QuestCameraVideo.shader";
        public const string ReceiptPath = "QuestStartupEvidence/camera-video-output.android-validation.json";
        const string SourceSha256 = "c5a2ad58db4686846512f0855da08c870ca85913a1329a4f0c46f8f195cb8285";
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, passes = 1, compiledStages = 2;
            public string compilerPlatform, unityVersion, sourceSha256, compiledSha256, vertexSha256, fragmentSha256;
            public bool androidAssetsBuilt, allGlesPassStagesCompiled, allVulkanPassStagesCompiled, nativePlaybackPreserved = true;
            public bool hardwareVisualsVerified;
        }
        public static void Validate(bool androidAssetsBuilt)
        {
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(RequiredGraphicsApi))
                throw new InvalidOperationException("Camera movie output validation requires configured Android " + CompilerPlatform + ".");
            if (!File.Exists(SourcePath) || Hash(File.ReadAllBytes(SourcePath)) != SourceSha256)
                throw new InvalidOperationException("Camera movie output source differs from the verified program.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(SourcePath);
            if (shader == null || shader.name != "Hidden/GloomhavenVR/QuestCameraVideo" || shader.GetPropertyCount() != 5)
                throw new InvalidOperationException("Camera movie output shader identity or ABI differs.");
            foreach (string property in new[] { "_MainTex", "_UvTransform", "_Alpha", "_Plane", "_ZTest" })
                if (shader.FindPropertyIndex(property) < 0)
                    throw new InvalidOperationException("Camera movie output property is missing: " + property);
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null || data.SubshaderCount != 1 || data.GetSubshader(0).PassCount != 1)
                throw new InvalidOperationException("Camera movie output pass count differs.");
            var pass = data.GetSubshader(0).GetPass(0);
#if GHVR_QUEST_GAME
            var compiled = QuestVulkanShaderValidation.Compile(shader, 0, 0, new string[0], GraphicsTier.Tier2);
            QuestVulkanShaderValidation.RequireColorOutput(compiled);
            QuestVulkanShaderValidation.RequirePlain2D(compiled, "_MainTex");
            var receipt = new Receipt {
                compilerPlatform = CompilerPlatform, unityVersion = Application.unityVersion, sourceSha256 = SourceSha256,
                compiledSha256 = compiled.bankSha256, vertexSha256 = compiled.vertexSha256,
                fragmentSha256 = compiled.fragmentSha256, androidAssetsBuilt = androidAssetsBuilt,
                allGlesPassStagesCompiled = false, allVulkanPassStagesCompiled = true, hardwareVisualsVerified = false
            };
#else
            var compiled = pass.CompileVariant(ShaderType.Vertex, new string[0], ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
            if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                (compiled.Messages ?? new ShaderMessage[0]).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                throw new InvalidOperationException("Camera movie output GLES compilation failed.");
            string[] stages = QuestUiAssetValidation.VerifiedGlesSections(compiled.ShaderData);
            if (!stages[1].Contains("texture(") || !stages[1].Contains("_MainTex"))
                throw new InvalidOperationException("Camera movie output program does not sample the native decoder texture.");
            var receipt = new Receipt {
                compilerPlatform = CompilerPlatform, unityVersion = Application.unityVersion, sourceSha256 = SourceSha256,
                compiledSha256 = Hash(compiled.ShaderData), vertexSha256 = Hash(Encoding.UTF8.GetBytes(stages[0])),
                fragmentSha256 = Hash(Encoding.UTF8.GetBytes(stages[1])), androidAssetsBuilt = androidAssetsBuilt,
                allGlesPassStagesCompiled = !VulkanBackend, allVulkanPassStagesCompiled = VulkanBackend, hardwareVisualsVerified = false
            };
#endif
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            File.WriteAllText(ReceiptPath, JsonUtility.ToJson(receipt, true) + "\n");
            Debug.Log("[GloomhavenVR Quest] native camera movie output shader verified: passes=1, " + CompilerPlatform + " stages=2");
        }
        static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
        }
    }
}
#endif
