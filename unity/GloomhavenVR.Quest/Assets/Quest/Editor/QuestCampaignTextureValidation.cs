#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
        public const string Texture2DInputPath = "Assets/QuestOriginalCampaign/native-texture2d.json";
        public const string TextureReferenceInputPath = "Assets/QuestOriginalCampaign/native-texture-references.json";
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
            public int schema = 1, nativeCubemapCount, importedMipCount, gpuReadbackMipCount, nativePlatformImageCount, nativeTexture2DCount, nativeTextureReferenceCount;
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
        [Serializable] public sealed class NativeSampler
        {
            public int filterMode,aniso,wrapU,wrapV,wrapW,colorSpace,streamingMipmapsPriority;
            public float mipBias;
            public bool streamingMipmaps;
        }
        [Serializable] public sealed class FloatingTexture
        {
            public string assetPath,guid,sha256,pixelSha256;
            public long fileId;
            public int width,height,mipCount,textureFormat,sourceFormat;
            public bool isReadable,sourceMipChainPreserved,originalHalfBytesPreserved;
            public NativeSampler native;
            public Mip[] mips;
        }
        [Serializable] public sealed class FloatingTextures
        {
            public int schema,nativeTexture2DCount,originalHalfTextureCount,originalBc6hTextureCount;
            public FloatingTexture[] assets;
        }
        [Serializable] public sealed class Texture2DReceipt
        {
            public int schema=1,nativeTexture2DCount,originalHalfTextureCount,originalBc6hTextureCount,gpuReadbackMipCount;
            public string unityVersion,graphicsDeviceType,sourceManifestSha256;
            public bool originalHalfGpuBytesVerified,originalBc6GpuParityVerified,headsetGpuVerified;
        }
        [Serializable] public sealed class TextureReference
        {
            public string guid,assetPath;
            public long fileId;
            public int type;
        }
        [Serializable] public sealed class TextureReferenceOwner
        {
            public string assetPath,sha256;
            public TextureReference[] references;
        }
        [Serializable] public sealed class TextureReferences
        {
            public int schema,nativeTextureTargetCount,ownerCount,referenceCount;
            public TextureReference[] targets;
            public TextureReferenceOwner[] owners;
        }
        [Serializable] public sealed class TextureReferenceReceipt
        {
            public int schema=1,nativeTextureTargetCount,ownerCount,referenceCount;
            public string unityVersion,sourceManifestSha256;
            public bool importedConsumingMaterialReferencesVerified;
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
            receipt.nativeTexture2DCount=ValidateTexture2D().nativeTexture2DCount;
            receipt.nativeTextureReferenceCount=ValidateTextureReferences().referenceCount;
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-cubemap-import.json", JsonUtility.ToJson(receipt,true));
            return receipt;
        }
        public static TextureReferenceReceipt ValidateTextureReferences()
        {
            var input=JsonUtility.FromJson<TextureReferences>(File.ReadAllText(TextureReferenceInputPath));
            if(input==null||input.schema!=1||input.targets==null||input.owners==null||
               input.nativeTextureTargetCount!=input.targets.Length||input.ownerCount!=input.owners.Length)
                throw new InvalidDataException("Native texture consuming-reference inventory is incomplete.");
            var targets=new Dictionary<string,string>();
            foreach(var target in input.targets)
            {
                var key=target.guid+":"+target.fileId;
                if(target.type!=2||targets.ContainsKey(key))throw new InvalidDataException("Native texture target type is ambiguous.");
                targets.Add(key,target.assetPath);
            }
            var receipt=new TextureReferenceReceipt {nativeTextureTargetCount=input.targets.Length,
                unityVersion=Application.unityVersion,sourceManifestSha256=Hash(File.ReadAllBytes(TextureReferenceInputPath))};
            foreach(var row in input.owners)
            {
                if(receipt.ownerCount>0&&receipt.ownerCount%16==0)EditorUtility.UnloadUnusedAssetsImmediate();
                if(Path.GetExtension(row.assetPath)!=".mat"||Hash(File.ReadAllBytes(row.assetPath))!=row.sha256||row.references==null)
                    throw new InvalidDataException("Native texture consuming owner changed: "+row.assetPath);
                var material=AssetDatabase.LoadAssetAtPath<Material>(row.assetPath);
                if(material==null)throw new InvalidDataException("Native texture consuming material failed import: "+row.assetPath);
                var expected=new Dictionary<string,int>();
                foreach(var reference in row.references)
                {
                    var key=reference.guid+":"+reference.fileId;
                    if(reference.type!=2||!targets.ContainsKey(key))throw new InvalidDataException("Native texture owner references an unwitnessed target.");
                    expected[key]=expected.ContainsKey(key)?expected[key]+1:1;
                }
                // Saved texture properties survive independently of shader import.
                // Read their actual imported object references, rather than merely
                // proving the target texture can be loaded on its own.
                var serialized=new SerializedObject(material);var iterator=serialized.GetIterator();
                while(iterator.Next(true))
                {
                    if(iterator.propertyType!=SerializedPropertyType.ObjectReference||!(iterator.objectReferenceValue is Texture))continue;
                    string guid;long fileId;
                    if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(iterator.objectReferenceValue,out guid,out fileId))continue;
                    var key=guid+":"+fileId;
                    if(!targets.ContainsKey(key))continue;
                    if(!expected.ContainsKey(key)||expected[key]==0||AssetDatabase.GetAssetPath(iterator.objectReferenceValue)!=targets[key])
                        throw new InvalidDataException("Native texture consuming material resolved an unexpected target: "+row.assetPath);
                    expected[key]--;receipt.referenceCount++;
                }
                if(expected.Values.Any(count=>count!=0))throw new InvalidDataException("Native texture consuming material has unresolved references: "+row.assetPath);
                receipt.ownerCount++;
            }
            if(receipt.referenceCount!=input.referenceCount)throw new InvalidDataException("Native texture consuming-reference count changed.");
            receipt.importedConsumingMaterialReferencesVerified=true;
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-texture-reference-import.json",JsonUtility.ToJson(receipt,true));
            return receipt;
        }
        public static Texture2DReceipt ValidateTexture2D()
        {
            var input=JsonUtility.FromJson<FloatingTextures>(File.ReadAllText(Texture2DInputPath));
            if(input==null||input.schema!=1||input.assets==null||input.nativeTexture2DCount!=input.assets.Length||
               input.originalHalfTextureCount+input.originalBc6hTextureCount!=input.nativeTexture2DCount)
                throw new InvalidDataException("Native floating Texture2D inventory is incomplete.");
            var receipt=new Texture2DReceipt {unityVersion=Application.unityVersion,
                graphicsDeviceType=SystemInfo.graphicsDeviceType.ToString(),sourceManifestSha256=Hash(File.ReadAllBytes(Texture2DInputPath))};
            foreach(var row in input.assets)
            {
                if(receipt.nativeTexture2DCount>0&&receipt.nativeTexture2DCount%4==0)EditorUtility.UnloadUnusedAssetsImmediate();
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(row.assetPath);string guid;long fileId;
                if(texture==null||Hash(File.ReadAllBytes(row.assetPath))!=row.sha256||!row.sourceMipChainPreserved||
                   !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture,out guid,out fileId)||guid!=row.guid||fileId!=row.fileId||
                   texture.width!=row.width||texture.height!=row.height||texture.mipmapCount!=row.mipCount||
                   (int)texture.format!=row.textureFormat||row.textureFormat!=17||texture.isReadable!=row.isReadable||
                   row.native==null||row.mips==null||row.mips.Length!=row.mipCount)
                    throw new InvalidDataException("Native floating Texture2D failed import: "+row.assetPath);
                var native=row.native;
                if((int)texture.filterMode!=native.filterMode||texture.anisoLevel!=native.aniso||
                   texture.mipMapBias!=native.mipBias||(int)texture.wrapModeU!=native.wrapU||
                   (int)texture.wrapModeV!=native.wrapV||(int)texture.wrapModeW!=native.wrapW||
                   texture.streamingMipmaps!=native.streamingMipmaps||texture.streamingMipmapsPriority!=native.streamingMipmapsPriority||
                   UnityEngine.Experimental.Rendering.GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat)!=(native.colorSpace==1))
                    throw new InvalidDataException("Native floating Texture2D sampler/color contract changed: "+row.assetPath);
                if(!SystemInfo.SupportsTextureFormat(texture.format)||!SystemInfo.supportsAsyncGPUReadback)
                    throw new InvalidDataException("Native floating Texture2D requires exact supported half GPU readback.");
                for(int mip=0;mip<row.mipCount;mip++)
                {
                    var witness=row.mips[mip];
                    if(witness.mip!=mip)throw new InvalidDataException("Native Texture2D mip order changed.");
                    var request=AsyncGPUReadback.Request(texture,mip,TextureFormat.RGBAHalf);request.WaitForCompletion();
                    if(request.hasError||request.layerCount!=1)throw new InvalidDataException("Native floating Texture2D readback failed.");
                    RequireMip(request.GetData<byte>(0).ToArray(),witness,row.assetPath);receipt.gpuReadbackMipCount++;
                }
                if(row.sourceFormat==17)
                {
                    if(!row.originalHalfBytesPreserved)throw new InvalidDataException("Native VAT half bytes were changed.");
                    receipt.originalHalfTextureCount++;
                }
                else if(row.sourceFormat==24)receipt.originalBc6hTextureCount++;
                else throw new InvalidDataException("Native floating Texture2D source format changed.");
                receipt.nativeTexture2DCount++;
            }
            if(receipt.originalHalfTextureCount!=input.originalHalfTextureCount||receipt.originalBc6hTextureCount!=input.originalBc6hTextureCount)
                throw new InvalidDataException("Native floating Texture2D source coverage changed.");
            receipt.originalHalfGpuBytesVerified=true;
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-texture2d-import.json",JsonUtility.ToJson(receipt,true));
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
