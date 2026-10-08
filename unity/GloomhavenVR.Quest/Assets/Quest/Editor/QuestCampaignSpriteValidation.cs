#if UNITY_EDITOR
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace GloomhavenVR.Quest.Editor
{
    /// <summary>Checks exact original drawing state of every nonpacked Sprite.</summary>
    public static class QuestCampaignSpriteValidation
    {
        public const string InputPath = "Assets/QuestOriginalCampaign/native-sprites.json";
        [Serializable] public sealed class Rectangle { public float x,y,width,height; }
        [Serializable] public sealed class NativeSprite
        {
            public string assetPath,guid,sha256,textureGuid;
            public long fileId;
            public int textureWidth,textureHeight;
            public bool nativeDrawingStateRestored;
            public Rectangle rect;
            public Vector2 pivot;
            public Vector4 border;
            public float pixelsPerUnit;
            public Vector2[] vertices,uv;
        }
        [Serializable] public sealed class Input
        {
            public int schema,nativeSpriteCount,restoredNonPackedSpriteCount,preservedPackedSpriteCount;
            public NativeSprite[] assets;
        }
        [Serializable] public sealed class Receipt
        {
            public int schema=1,nativeSpriteCount,importedNonPackedSpriteCount,preservedPackedSpriteCount;
            public string unityVersion,sourceManifestSha256;
            public bool headsetPictureVerified;
        }
        public static Receipt Validate()
        {
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(InputPath));
            if(input==null||input.schema!=1||input.assets==null||input.restoredNonPackedSpriteCount!=input.assets.Length||
               input.nativeSpriteCount!=input.restoredNonPackedSpriteCount+input.preservedPackedSpriteCount)
                throw new InvalidDataException("Original full Sprite source inventory is incomplete.");
            int importedCount=0;
            foreach(var row in input.assets)
            {
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(row.assetPath);
                string guid;long fileId;
                if(!row.nativeDrawingStateRestored||sprite==null||Hash(File.ReadAllBytes(row.assetPath))!=row.sha256||
                   !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out guid,out fileId)||guid!=row.guid||fileId!=row.fileId||row.rect==null)
                    throw new InvalidDataException("Original Sprite import identity differs: "+row.assetPath);
                Rect rect=sprite.rect;
                Require(rect.x,row.rect.x,row.assetPath);Require(rect.y,row.rect.y,row.assetPath);
                Require(rect.width,row.rect.width,row.assetPath);Require(rect.height,row.rect.height,row.assetPath);
                Require(sprite.pivot.x,row.pivot.x*rect.width,row.assetPath);
                Require(sprite.pivot.y,row.pivot.y*rect.height,row.assetPath);
                for(int index=0;index<4;index++)Require(sprite.border[index],row.border[index],row.assetPath);
                Require(sprite.pixelsPerUnit,row.pixelsPerUnit,row.assetPath);
                if(String.IsNullOrEmpty(row.textureGuid))
                {
                    if(sprite.texture!=null)throw new InvalidDataException("Originally empty Sprite acquired a texture.");
                }
                else if(sprite.texture==null||sprite.texture.width!=row.textureWidth||sprite.texture.height!=row.textureHeight||
                        AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture))!=row.textureGuid)
                    throw new InvalidDataException("Original Sprite texture binding differs: "+row.assetPath);
                RequireVectors(sprite.vertices,row.vertices,row.assetPath+" vertices");
                RequireVectors(sprite.uv,row.uv,row.assetPath+" UV");
                // textureRect is not queried: Unity rejects that accessor for
                // legitimate tight-packed geometry; original streams prove shape.
                if(++importedCount%64==0)EditorUtility.UnloadUnusedAssetsImmediate();
            }
            var receipt=new Receipt{unityVersion=Application.unityVersion,nativeSpriteCount=input.nativeSpriteCount,
                importedNonPackedSpriteCount=input.assets.Length,preservedPackedSpriteCount=input.preservedPackedSpriteCount,
                sourceManifestSha256=Hash(File.ReadAllBytes(InputPath))};
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-sprite-import.json",JsonUtility.ToJson(receipt,true));
            return receipt;
        }
        private static void RequireVectors(Vector2[] actual,Vector2[] original,string source)
        {
            if(actual==null||original==null||actual.Length!=original.Length)
                throw new InvalidDataException("Original Sprite drawing stream length differs: "+source);
            for(int index=0;index<actual.Length;index++)
            {
                Require(actual[index].x,original[index].x,source);
                Require(actual[index].y,original[index].y,source);
            }
        }
        private static void Require(float actual,float expected,string source)
        {
            if(Single.IsNaN(actual)||Single.IsInfinity(actual)||Math.Abs(actual-expected)>0.000001f)
                throw new InvalidDataException("Original Sprite drawing state differs: "+source);
        }
        private static string Hash(byte[] data)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        }
    }
}
#endif
