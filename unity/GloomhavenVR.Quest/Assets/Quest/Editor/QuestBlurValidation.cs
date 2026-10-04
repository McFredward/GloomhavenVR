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
    /// <summary>Separate original blur provenance and actual Android program gate.</summary>
    public static class QuestBlurValidation
    {
        public const string InputPath = "QuestStartupEvidence/original-ui-blur.json";
        public const string ReceiptPath = "QuestStartupEvidence/original-ui-blur.android-validation.json";
        private const string AssetPath = "Assets/Shader/Custom_SimpleGrabPassBlur.shader";
        private const string Guid = "a0b681ed431356a47b5287e6441c1c11";
        private const string SourceHash = "408ffb291c0a071cd0eef920f87e0867c8007d0b01a8775d6e011811a8832bfc";

        [Serializable] public sealed class OriginalPrograms
        {
            public string grabVertex, horizontalFragment, verticalFragment, distortionVertex, distortionFragment;
        }
        [Serializable] public sealed class SourceReceipt
        {
            public int schema, drawPassCount, grabPassCount;
            public string target, recipe, origin, name, assetPath, guid, sourceSha256, metaSha256;
            public string canonicalRecipeSha256, canonicalFormSha256, originalDummySha256, normalEncodingPolicy;
            public OriginalPrograms originalDxbcSha256;
            public bool originalVertexColorAbsent, originalPassStatesRetained, propertiesAndDefaultsRetained;
            public bool materialAndSceneUnchanged, androidShaderCompiled, originalPixelParityVerified;
        }
        [Serializable] public sealed class Bank
        {
            public int pass, bytes, vertexBytes, fragmentBytes;
            public string sha256, vertexSha256, fragmentSha256, keyword;
        }
        [Serializable] public sealed class ValidationReceipt
        {
            public int schema = 1, importedDrawPassCount, importedGrabPassCount;
            public string unityVersion, sourceReceiptSha256;
            public bool androidAssetsBuilt, allGlesPassStagesCompiled, originalPixelParityVerified;
            public Bank[] glesPrograms;
        }

        public static void Validate(bool androidAssetsBuilt)
        {
            Safe(ReceiptPath);
            if (File.Exists(ReceiptPath)) File.Delete(ReceiptPath);
            if (!Directory.Exists("Assets/Quest") || EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.OpenGLES3))
                throw new InvalidOperationException("Original UI blur validation requires generated Android/GLES3.");
            Safe(InputPath);
            if (!File.Exists(InputPath) || new FileInfo(InputPath).Length > 64 * 1024)
                throw new InvalidOperationException("Original UI blur source receipt is missing or oversized.");
            byte[] bytes = File.ReadAllBytes(InputPath);
            var source = JsonUtility.FromJson<SourceReceipt>(Encoding.UTF8.GetString(bytes));
            if (source == null || source.schema != 1 || source.target != "startup" ||
                source.recipe != "original-ui-blur-dxbc-port-v1" || source.origin != "original-compiled-DXBC-transcription" ||
                source.name != "Custom/SimpleGrabPassBlur" || source.assetPath != AssetPath || source.guid != Guid ||
                source.sourceSha256 != SourceHash || source.drawPassCount != 3 || source.grabPassCount != 3 ||
                source.canonicalRecipeSha256 != "8c0b49510c15a60b71b6da8638bdca047d39fbc6317b8cbdd1d9d956413461ed" ||
                source.canonicalFormSha256 != "fa7b20ad60256357747bf0b160e9ef3d9a411391199f8b2d788a48f32fe04282" ||
                source.originalDummySha256 != "2becde7e00713952ce9151750bf0ef7970cfd8ff23fc4a970696f768cb440df7" ||
                source.normalEncodingPolicy != "Unity-2021.3.5-UnpackNormal-XY" ||
                !source.originalVertexColorAbsent || !source.originalPassStatesRetained || !source.propertiesAndDefaultsRetained ||
                !source.materialAndSceneUnchanged || source.androidShaderCompiled || source.originalPixelParityVerified)
                throw new InvalidOperationException("Original UI blur source receipt differs from audited native programs/states.");
            var programs = source.originalDxbcSha256;
            if (programs == null ||
                programs.grabVertex != "090e42733308dd506b0161f21410399f44c352fcb9a3bc19b3617314d310f8ef" ||
                programs.horizontalFragment != "faaf888f8d94c3464870932a40a683ef85256c7dcc45c62d92a15ac622296d4f" ||
                programs.verticalFragment != "922df212d277454ad870cba807774e3219a479d3b20bdad73265f3a69a598435" ||
                programs.distortionVertex != "cc824340e88532a35b4958ce8dd5fce9d7b976dbd3725ce3b93ef9396cb22ba7" ||
                programs.distortionFragment != "594977e3a883942b6b74e461b0953955919efe4c837903f4672a69334a35df5c")
                throw new InvalidOperationException("Original UI blur native bytecode identities differ.");
            RequireHash(AssetPath, SourceHash);
            RequireHash(AssetPath + ".meta", source.metaSha256);
            if (AssetDatabase.AssetPathToGUID(AssetPath) != Guid || AssetDatabase.GUIDToAssetPath(Guid) != AssetPath)
                throw new InvalidOperationException("Original UI blur imported GUID differs.");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(AssetPath);
            if (shader == null || shader.name != source.name)
                throw new InvalidOperationException("Original UI blur imported shader is missing.");
            int color = shader.FindPropertyIndex("_Color");
            if (color < 0 || shader.GetPropertyType(color) != ShaderPropertyType.Color ||
                shader.GetPropertyDefaultVectorValue(color) != Vector4.one)
                throw new InvalidOperationException("Original UI blur Color property/default differs.");
            var data = ShaderUtil.GetShaderData(shader);
            if (data == null || data.SubshaderCount != 1 || data.GetSubshader(0).PassCount != 6)
                throw new InvalidOperationException("Original UI blur three grab/draw pairs differ.");
            var compiledBanks = new List<Bank>();
            for (int index = 0; index < 6; index++)
            {
                var pass = data.GetSubshader(0).GetPass(index);
                bool vertex = pass.HasShaderStage(ShaderType.Vertex), fragment = pass.HasShaderStage(ShaderType.Fragment);
                if (vertex != (index % 2 == 1) || fragment != vertex)
                    throw new InvalidOperationException("Original UI blur grab/draw stage order differs.");
                if (!vertex) continue;
                foreach (string keyword in index == 5 ? new[] { "", "UNITY_ASTC_NORMALMAP_ENCODING" } : new[] { "" })
                {
                    var compiled = pass.CompileVariant(ShaderType.Vertex, keyword.Length == 0 ? new string[0] : new[] { keyword },
                        ShaderCompilerPlatform.GLES3x, BuildTarget.Android);
                    if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0 ||
                        (compiled.Messages ?? new ShaderMessage[0]).Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                        throw new InvalidOperationException("Original UI blur Android compilation failed: pass " + index);
                    string[] stages = QuestUiAssetValidation.VerifiedGlesSections(compiled.ShaderData);
                    byte[] v = Encoding.UTF8.GetBytes(stages[0]), f = Encoding.UTF8.GetBytes(stages[1]);
                    compiledBanks.Add(new Bank {
                        pass = index, keyword = keyword, bytes = compiled.ShaderData.Length, sha256 = Hash(compiled.ShaderData),
                        vertexBytes = v.Length, vertexSha256 = Hash(v), fragmentBytes = f.Length, fragmentSha256 = Hash(f)
                    });
                }
            }
            if (ShaderUtil.ShaderHasError(shader) || (ShaderUtil.GetShaderMessages(shader) ?? new ShaderMessage[0])
                .Any(message => message.severity == ShaderCompilerMessageSeverity.Error))
                throw new InvalidOperationException("Original UI blur imported shader has errors.");
            var receipt = new ValidationReceipt {
                unityVersion = Application.unityVersion, sourceReceiptSha256 = Hash(bytes), androidAssetsBuilt = androidAssetsBuilt,
                importedDrawPassCount = 3, importedGrabPassCount = 3, allGlesPassStagesCompiled = true,
                originalPixelParityVerified = false, glesPrograms = compiledBanks.ToArray()
            };
            Directory.CreateDirectory(Path.GetDirectoryName(ReceiptPath));
            string temporary = ReceiptPath + ".tmp-" + System.Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(receipt, true) + "\n");
                File.Move(temporary, ReceiptPath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            Debug.Log("[GloomhavenVR Quest] original UI blur verified: original grab/draw pairs=3, Android GLES banks=4");
        }

        private static void Safe(string relative)
        {
            var current = new FileInfo(Path.GetFullPath(relative)) as FileSystemInfo;
            while (current != null)
            {
                if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Linked original UI blur validation path.");
                current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent;
            }
        }
        private static void RequireHash(string path, string expected)
        {
            Safe(path);
            if (!File.Exists(path) || !string.Equals(Hash(File.ReadAllBytes(path)), expected, StringComparison.Ordinal))
                throw new InvalidOperationException("Original UI blur source hash differs: " + path);
        }
        private static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
#endif
