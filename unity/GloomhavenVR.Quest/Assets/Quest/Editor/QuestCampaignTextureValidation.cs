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
        public const string PlatformInputPath = "Assets/QuestOriginalCampaign/native-platform-images.json";
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
            public int schema = 1, nativeCubemapCount, importedMipCount, gpuReadbackMipCount, nativePlatformImageCount;
            public string unityVersion, graphicsDeviceType, sourceManifestSha256;
            public bool originalBc6GpuParityVerified, headsetGpuVerified;
        }
        [Serializable] public sealed class PlatformImage
        {
            public string assetPath,guid,sha256;
            public int classId,width,height,textureFormat,mipCount,colorFormat,depthStencilFormat,dimension,antiAliasing;
            public long fileId;
            public bool compatibleFormatFallback;
        }
        [Serializable] public sealed class PlatformImages
        {
            public int schema,nativePlatformImageCount,unsupportedImageClassCount;
            public PlatformImage[] assets;
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
            receipt.nativePlatformImageCount=ValidatePlatformImages();
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-cubemap-import.json", JsonUtility.ToJson(receipt,true));
            return receipt;
        }
        public static int ValidatePlatformImages()
        {
            var input=JsonUtility.FromJson<PlatformImages>(File.ReadAllText(PlatformInputPath));
            if(input==null||input.schema!=1||input.unsupportedImageClassCount!=0||input.assets==null||input.nativePlatformImageCount!=input.assets.Length)
                throw new InvalidDataException("Native platform-sensitive image inventory is incomplete.");
            foreach(var row in input.assets)
            {
                var value=AssetDatabase.LoadMainAssetAtPath(row.assetPath);string guid;long fileId;
                if(value==null||Hash(File.ReadAllBytes(row.assetPath))!=row.sha256||
                   !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value,out guid,out fileId)||guid!=row.guid||fileId!=row.fileId)
                    throw new InvalidDataException("Native platform-sensitive image failed import: "+row.assetPath);
                if(row.classId==28)
                {
                    var texture=value as Texture2D;
                    if(texture==null||texture.width!=row.width||texture.height!=row.height||
                       (int)texture.format!=row.textureFormat||texture.mipmapCount!=row.mipCount||!SystemInfo.SupportsTextureFormat(texture.format))
                        throw new InvalidDataException("Native Alpha8 font texture format failed import: "+row.assetPath);
                }
                else if(row.classId==84)
                {
                    var target=value as RenderTexture;
                    if(target==null||target.width!=row.width||target.height!=row.height||
                       (int)target.dimension!=row.dimension||target.antiAliasing!=row.antiAliasing||
                       (int)target.graphicsFormat!=row.colorFormat||!row.compatibleFormatFallback)
                        throw new InvalidDataException("Native runtime RenderTexture format failed import: "+row.assetPath);
                    if(!target.Create()||!target.IsCreated())
                        throw new InvalidDataException("Native runtime RenderTexture could not create its graphics backing: "+row.assetPath);
                    target.Release();
                }
                else throw new InvalidDataException("Unknown native platform-sensitive image class.");
            }
            return input.assets.Length;
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
