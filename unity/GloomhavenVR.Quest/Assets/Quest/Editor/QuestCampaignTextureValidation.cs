#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Checks imported portable cubes retain every original face/mip.</summary>
    public static class QuestCampaignTextureValidation
    {
        public const string InputPath = "Assets/QuestOriginalCampaign/native-cubemaps.json";
        [Serializable] public sealed class Mip { public int face, mip, size; public string sha256; }
        [Serializable] public sealed class Cube
        {
            public string assetPath, guid, sha256;
            public long fileId;
            public int width, mipCount, faceCount, textureFormat;
            public bool isReadable, sourceMipChainPreserved;
            public Mip[] mips;
        }
        [Serializable] public sealed class Input
        {
            public int schema, nativeCubemapCount;
            public Cube[] assets;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema = 1, nativeCubemapCount, importedMipCount, gpuReadbackMipCount;
            public string unityVersion, graphicsDeviceType, sourceManifestSha256;
            public bool originalBc6GpuParityVerified, headsetGpuVerified;
        }
        public static Receipt Validate()
        {
            var input = JsonUtility.FromJson<Input>(File.ReadAllText(InputPath));
            if (input == null || input.schema != 1 || input.assets == null || input.nativeCubemapCount != input.assets.Length)
                throw new InvalidDataException("Original native Cubemap inventory is incomplete.");
            var receipt = new Receipt { unityVersion = Application.unityVersion,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(), sourceManifestSha256 = Hash(File.ReadAllBytes(InputPath)) };
            foreach (var row in input.assets)
            {
                var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(row.assetPath);
                string guid; long fileId;
                if (!row.sourceMipChainPreserved || cube == null || Hash(File.ReadAllBytes(row.assetPath)) != row.sha256 ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(cube, out guid, out fileId) || guid != row.guid || fileId != row.fileId ||
                    cube.width != row.width || cube.height != row.width || cube.mipmapCount != row.mipCount || row.faceCount != 6 ||
                    (int)cube.format != row.textureFormat || cube.isReadable != row.isReadable || row.mips == null || row.mips.Length != 6*row.mipCount)
                    throw new InvalidDataException("Portable original Cubemap identity failed import: " + row.assetPath);
                if (!SystemInfo.SupportsTextureFormat(cube.format))
                    throw new InvalidDataException("Current graphics device cannot sample the portable Cubemap format.");
                for (int mip = 0; mip < row.mipCount; mip++)
                {
                    var witnesses = row.mips.Where(value => value.mip == mip).OrderBy(value => value.face).ToArray();
                    if (witnesses.Length != 6 || witnesses.Where((value,index) => value.face != index).Any())
                        throw new InvalidDataException("Original Cubemap has an ambiguous face/mip witness.");
                    if (cube.isReadable)
                    {
                        foreach (var witness in witnesses)
                        {
                            var bytes = cube.GetPixelData<byte>(mip, (CubemapFace)witness.face).ToArray();
                            RequireMip(bytes, witness, row.assetPath); receipt.importedMipCount++;
                        }
                    }
                    else
                    {
                        // Native HDR assets keep their original nonreadable state. A GPU
                        // readback verifies imported half pixels without retaining a CPU copy.
                        if (cube.format != TextureFormat.RGBAHalf || !SystemInfo.supportsAsyncGPUReadback)
                            throw new InvalidDataException("Nonreadable native Cubemap requires supported exact half GPU readback.");
                        var request = AsyncGPUReadback.Request(cube, mip, TextureFormat.RGBAHalf);
                        request.WaitForCompletion();
                        if (request.hasError || request.layerCount != 6)
                            throw new InvalidDataException("Portable HDR Cubemap GPU readback failed.");
                        foreach (var witness in witnesses)
                        {
                            RequireMip(request.GetData<byte>(witness.face).ToArray(), witness, row.assetPath);
                            receipt.importedMipCount++; receipt.gpuReadbackMipCount++;
                        }
                    }
                }
                receipt.nativeCubemapCount++;
            }
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-cubemap-import.json", JsonUtility.ToJson(receipt,true));
            return receipt;
        }
        private static void RequireMip(byte[] data, Mip witness, string source)
        {
            if (data.Length != witness.size || Hash(data) != witness.sha256)
                throw new InvalidDataException("Portable original Cubemap pixels differ: " + source + " face=" + witness.face + " mip=" + witness.mip);
        }
        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        }
    }
}
#endif
