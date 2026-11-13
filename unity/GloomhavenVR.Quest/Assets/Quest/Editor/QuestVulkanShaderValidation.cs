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
    /// <summary>Decode and reflect actual Unity 2021.3.5 Android Vulkan banks.</summary>
    public static class QuestVulkanShaderValidation
    {
        [Serializable] public sealed class Image
        {
            public string name, stage, dimension;
            public int set, binding, samplerSet = -1, samplerBinding = -1, depth;
            public bool arrayed, multisampled;
        }
        [Serializable] public sealed class Interface
        {
            public string name, stage;
            public int location = -1, builtin = -1, componentType, components, bitWidth;
            public bool flat;
        }
        [Serializable] public sealed class Result
        {
            public byte[] vertex, fragment, bank;
            public string vertexSha256, fragmentSha256, bankSha256;
            public Image[] images;
            public Interface[] inputs, outputs;
            public bool vertexEyeRoutingObserved, fragmentEyeRoutingObserved;
        }
        private sealed class Module
        {
            public string stage;
            public byte[] bytes;
            public List<Image> images = new List<Image>();
            public List<Interface> inputs = new List<Interface>(), outputs = new List<Interface>();
        }

        public static Result Compile(Shader shader, int subshader, int pass, string[] keywords, GraphicsTier tier)
        { return Compile(shader, subshader, pass, keywords, (int)tier); }

        public static Result Compile(Shader shader, int subshader, int pass, string[] keywords, int tier)
        {
            if (Application.unityVersion != "2021.3.5f1" || shader == null || shader.name == "Hidden/InternalErrorShader")
                throw new InvalidOperationException("Original-version Vulkan shader identity is absent.");
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android ||
                !PlayerSettings.GetGraphicsAPIs(BuildTarget.Android).Contains(GraphicsDeviceType.Vulkan))
                throw new InvalidOperationException("Actual Vulkan compiler gate requires Android/Vulkan.");
            var data = ShaderUtil.GetShaderData(shader);
            if (subshader < 0 || subshader >= data.SubshaderCount || pass < 0 || pass >= data.GetSubshader(subshader).PassCount)
                throw new InvalidOperationException("Actual Vulkan original pass is absent.");
            var compiled = data.GetSubshader(subshader).GetPass(pass).CompileVariant(ShaderType.Vertex, keywords,
                ShaderCompilerPlatform.Vulkan, BuildTarget.Android, (GraphicsTier)tier);
            if (!compiled.Success || compiled.ShaderData == null || compiled.ShaderData.Length == 0)
                throw new InvalidOperationException("Actual Vulkan shader bank failed: " + shader.name + " / " +
                    string.Join(" ", keywords) + " / " + string.Join("; ", compiled.Messages.Select(row => row.message + " at " + row.file + ":" + row.line)));
            var result = Decode(compiled.ShaderData);
            // Unity deliberately strips resource OpNames from its cooked SPIR-V.
            // Its actual compiler reflection supplies the packed descriptor slot;
            // join that slot to the independently decoded native OpTypeImage.
            foreach (var texture in compiled.TextureBindings ?? new ShaderData.TextureBindingInfo[0])
            {
                int binding = texture.Index & 65535, set = texture.Index >> 16 & 255;
                var images = result.images.Where(image => image.binding == binding && image.set == set).ToArray();
                if (images.Length == 0) throw new InvalidOperationException("Cooked Vulkan texture reflection has no native descriptor: " + texture.Name);
                foreach (var image in images)
                {
                    image.name = texture.Name;
                    image.samplerBinding = texture.SamplerIndex < 0 ? -1 : texture.SamplerIndex & 65535;
                    image.samplerSet = texture.SamplerIndex < 0 ? -1 : texture.SamplerIndex >> 16 & 255;
                    string dimension = texture.Dim == TextureDimension.Tex2D ? "2D" : texture.Dim == TextureDimension.Tex3D ? "3D" :
                        texture.Dim == TextureDimension.Cube ? "Cube" : texture.Dim == TextureDimension.Tex2DArray ? "2D" :
                        texture.Dim == TextureDimension.CubeArray ? "Cube" : null;
                    bool array = texture.Dim == TextureDimension.Tex2DArray || texture.Dim == TextureDimension.CubeArray;
                    if (dimension == null || image.dimension != dimension || image.arrayed != array || image.multisampled != texture.Multisampled)
                        throw new InvalidOperationException("Cooked Vulkan image type disagrees with its compiler property: " + texture.Name);
                }
            }
            if (result.images.Any(image => string.IsNullOrEmpty(image.name)))
                throw new InvalidOperationException("Cooked Vulkan image loses its original property binding.");
            RequirePosition(result);
            return result;
        }

        public static void RequirePosition(Result result)
        {
            if (!result.outputs.Any(value => value.stage == "vertex" && value.builtin == 0 && value.componentType == 3 && value.components == 4 && value.bitWidth == 32))
                throw new InvalidOperationException("Actual Vulkan vertex bank has no native float4 Position output.");
        }
        public static void RequireColorOutput(Result result)
        {
            if (!result.outputs.Any(value => value.stage == "fragment" && value.location == 0 && value.componentType == 3 && value.components == 4 && value.bitWidth == 32))
                throw new InvalidOperationException("Actual Vulkan bank omits its native float4 RGB/alpha color output.");
        }
        public static void RequirePlain2D(Result result, string propertyName)
        {
            var images = result.images.Where(image => image.name == propertyName).ToArray();
            if (images.Length == 0 || images.Any(image => image.dimension != "2D" || image.arrayed || image.multisampled || image.depth != 0))
                throw new InvalidOperationException("Actual Vulkan property is not an ordinary 2D sampled image: " + propertyName);
        }
        public static void RequireStereoRouting(Result result, string keyword)
        {
            if (keyword != "STEREO_INSTANCING_ON" && keyword != "STEREO_MULTIVIEW_ON")
                throw new InvalidOperationException("Vulkan eye-routing gate requires an explicit native stereo alias.");
            if (!result.vertexEyeRoutingObserved || !result.fragmentEyeRoutingObserved)
                throw new InvalidOperationException("Actual Vulkan stereo alias omits eye routing.");
        }

        public static Result Decode(byte[] bank)
        {
            if (bank == null || bank.Length < 52) throw new InvalidOperationException("Native Vulkan stage table is truncated.");
            var modules = new List<Module>();
            var ranges = new List<KeyValuePair<int, int>>();
            for (int stage = 0; stage < 6; stage++)
            {
                uint start = Word(bank, 4 + stage * 8), count = Word(bank, 8 + stage * 8);
                if (count == 0) { if (start != 0) throw new InvalidOperationException("Native Vulkan empty stage has an offset."); continue; }
                if (start < 52 || start > bank.Length || count > bank.Length - start)
                    throw new InvalidOperationException("Native Vulkan stage extent is invalid.");
                if (ranges.Any(range => start < range.Value && start + count > range.Key))
                    throw new InvalidOperationException("Native Vulkan stage payloads overlap.");
                ranges.Add(new KeyValuePair<int, int>((int)start, (int)(start + count)));
                byte[] compressed = new byte[count]; Buffer.BlockCopy(bank, (int)start, compressed, 0, (int)count);
                modules.Add(Reflect(QuestSmolvDecoder.Decode(compressed)));
            }
            if (modules.Count != 2 || modules.Count(module => module.stage == "vertex") != 1 || modules.Count(module => module.stage == "fragment") != 1)
                throw new InvalidOperationException("Actual Vulkan bank does not contain exactly its vertex and fragment modules.");
            var vertex = modules.Single(module => module.stage == "vertex");
            var fragment = modules.Single(module => module.stage == "fragment");
            var result = new Result { bank = bank, vertex = vertex.bytes, fragment = fragment.bytes,
                vertexSha256 = Hash(vertex.bytes), fragmentSha256 = Hash(fragment.bytes), bankSha256 = Hash(bank),
                images = modules.SelectMany(module => module.images).ToArray(), inputs = modules.SelectMany(module => module.inputs).ToArray(),
                outputs = modules.SelectMany(module => module.outputs).ToArray() };
            result.vertexEyeRoutingObserved = vertex.inputs.Any(value => value.builtin == 43 || value.builtin == 4440);
            result.fragmentEyeRoutingObserved = fragment.inputs.Any(value => value.builtin == 4440 || value.builtin == 9 || value.name == "vs_BLENDINDICES0" || value.name == "unity_StereoEyeIndex");
            return result;
        }

        private static Module Reflect(byte[] bytes)
        {
            if (bytes.Length < 20 || (bytes.Length & 3) != 0 || Word(bytes, 0) != 0x07230203 || Word(bytes, 16) != 0)
                throw new InvalidOperationException("Native Vulkan SPIR-V header is invalid.");
            var words = new uint[bytes.Length / 4]; Buffer.BlockCopy(bytes, 0, words, 0, bytes.Length);
            var names = new Dictionary<uint, string>(); var types = new Dictionary<uint, uint[]>();
            var decorations = new Dictionary<string, uint>(); var variables = new List<uint[]>();
            string stage = null; int entries = 0;
            for (int index = 5; index < words.Length; )
            {
                int length = (int)(words[index] >> 16), op = (int)(words[index] & 65535);
                if (length < 1 || length > words.Length - index) throw new InvalidOperationException("Native Vulkan SPIR-V instruction is truncated.");
                var row = new uint[length]; Array.Copy(words, index, row, 0, length);
                if (op == 15)
                {
                    if (length < 4 || ++entries != 1 || row[1] != 0 && row[1] != 4)
                        throw new InvalidOperationException("Native Vulkan graphics execution model is invalid.");
                    stage = row[1] == 0 ? "vertex" : "fragment";
                }
                else if (op == 5) names.Add(row[1], String(row, 2));
                else if (op >= 19 && op <= 33) types.Add(row[1], row);
                else if (op == 59) variables.Add(row);
                else if (op == 71 && length >= 3) decorations.Add(row[1] + ":" + row[2], length >= 4 ? row[3] : 0);
                else if (op == 72 && length >= 5) decorations.Add(row[1] + ":" + row[2] + ":" + row[3], row[4]);
                index += length;
            }
            if (stage == null) throw new InvalidOperationException("Native Vulkan entry point is absent.");
            var module = new Module { stage = stage, bytes = bytes };
            foreach (var row in variables)
            {
                uint[] pointer;
                if (row.Length < 4 || !types.TryGetValue(row[1], out pointer) || (pointer[0] & 65535) != 32 || pointer.Length != 4)
                    throw new InvalidOperationException("Native Vulkan variable pointer is invalid.");
                uint typeId = pointer[3]; uint[] type = types[typeId];
                string name; names.TryGetValue(row[2], out name);
                int op = (int)(type[0] & 65535);
                if (row[3] == 0 && (op == 25 || op == 27))
                {
                    if (op == 27) type = types[type[2]];
                    if (type.Length < 9 || !decorations.ContainsKey(row[2] + ":33") || !decorations.ContainsKey(row[2] + ":34"))
                        throw new InvalidOperationException("Native Vulkan image descriptor is invalid.");
                    string[] dimensions = { "1D", "2D", "3D", "Cube", "Rect", "Buffer", "SubpassData" };
                    if (type[3] >= dimensions.Length) throw new InvalidOperationException("Native Vulkan image dimension is unknown.");
                    module.images.Add(new Image { name = name, stage = stage, dimension = dimensions[type[3]], depth = (int)type[4],
                        arrayed = type[5] != 0, multisampled = type[6] != 0, binding = (int)decorations[row[2] + ":33"], set = (int)decorations[row[2] + ":34"] });
                }
                else if (row[3] == 1 || row[3] == 3)
                {
                    var list = row[3] == 1 ? module.inputs : module.outputs;
                    if (op == 30)
                    {
                        for (int member = 2; member < type.Length; member++)
                            AddInterface(list, stage, name, type[member], typeId + ":" + (member - 2), types, decorations);
                    }
                    else AddInterface(list, stage, name, typeId, row[2].ToString(), types, decorations);
                }
            }
            return module;
        }
        private static void AddInterface(List<Interface> list, string stage, string name, uint typeId, string key,
            Dictionary<uint, uint[]> types, Dictionary<string, uint> decorations)
        {
            uint location, builtin;
            bool located = decorations.TryGetValue(key + ":30", out location), built = decorations.TryGetValue(key + ":11", out builtin);
            if (!located && !built) return;
            uint[] type = types[typeId]; int op = (int)(type[0] & 65535), components = 1;
            if (op == 28 || op == 29) { type = types[type[2]]; op = (int)(type[0] & 65535); }
            if (op == 23) { components = (int)type[3]; type = types[type[2]]; op = (int)(type[0] & 65535); }
            if (op != 20 && op != 21 && op != 22) throw new InvalidOperationException("Native Vulkan interface has an unsupported scalar type.");
            list.Add(new Interface { name = name, stage = stage, location = located ? (int)location : -1, builtin = built ? (int)builtin : -1,
                componentType = op == 20 ? 4 : op == 22 ? 3 : type[3] == 0 ? 1 : 2, components = components, bitWidth = op == 20 ? 1 : (int)type[2], flat = decorations.ContainsKey(key + ":14") });
        }
        private static string String(uint[] words, int offset)
        {
            var bytes = new byte[(words.Length - offset) * 4]; Buffer.BlockCopy(words, offset * 4, bytes, 0, bytes.Length);
            int length = Array.IndexOf(bytes, (byte)0); if (length < 0) throw new InvalidOperationException("Native Vulkan string is unterminated.");
            return new UTF8Encoding(false, true).GetString(bytes, 0, length);
        }
        private static uint Word(byte[] data, int offset) { return BitConverter.ToUInt32(data, offset); }
        private static string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}
#endif
