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
            public string originalCollection,sourceContainerSha256;
            public long originalPathId;
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
            public int lineEndingQualifiedLoadingSprites;
            public string unityVersion,sourceManifestSha256;
            public bool headsetPictureVerified;
        }
        public static Receipt Validate()
        {
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(InputPath));
            if(input==null||input.schema!=1||input.assets==null||input.restoredNonPackedSpriteCount!=input.assets.Length||
               input.nativeSpriteCount!=input.restoredNonPackedSpriteCount+input.preservedPackedSpriteCount)
                throw new InvalidDataException("Original full Sprite source inventory is incomplete.");
            int importedCount=0,lineEndingQualified=0;
            var progress=new QuestWizardProgress.Counter("unity-validation-campaign-sprites","unity-validation",
                input.assets.Length,"sprites","Original Campaign sprites");
            foreach(var row in input.assets)
            {
                progress.Report(importedCount,row.assetPath);
                var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(row.assetPath);
                string guid;long fileId;
                byte[] bytes=File.ReadAllBytes(row.assetPath);
                bool sourceMatches=Hash(bytes)==row.sha256;
                if(!sourceMatches&&QualifyLoadingLineEndings(row,bytes))
                {
                    sourceMatches=true;++lineEndingQualified;
                    Debug.Log("[Quest startup] original loading Sprite line-ending transport qualified; asset="+
                        row.assetPath+", bytes="+bytes.Length+"; completed conversion and Unity Library retained.");
                }
                if(!row.nativeDrawingStateRestored||sprite==null||!sourceMatches||
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
                progress.Report(importedCount,row.assetPath);
            }
            var receipt=new Receipt{unityVersion=Application.unityVersion,nativeSpriteCount=input.nativeSpriteCount,
                importedNonPackedSpriteCount=input.assets.Length,preservedPackedSpriteCount=input.preservedPackedSpriteCount,
                lineEndingQualifiedLoadingSprites=lineEndingQualified,
                sourceManifestSha256=Hash(File.ReadAllBytes(InputPath))};
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-sprite-import.json",JsonUtility.ToJson(receipt,true));
            progress.Complete("Original Campaign sprite import verified");
            return receipt;
        }

        [Serializable] sealed class LoadingAsset
        {
            public string name,asset,guid,metaSha256,restoredSha256,geometryManifest,geometryManifestSha256;
            public long sourcePathId;
            public bool preservedNativeDrawingGeometry,preservedNativePackedGeometry;
        }
        [Serializable] sealed class LoadingSource
        {
            public int schema;
            public string sourceSha256;
            public LoadingAsset[] assets;
        }

        /// <summary>
        /// Capture065547/661 proves the older Windows loading producer rewrote
        /// already native CRLF Sprite YAML as LF. Startup validation accepted
        /// its later receipt; Campaign validation still expected the complete
        /// earlier CRLF hash. Accept only the two actual nonpacked loading
        /// objects, a byte-exact inverse transport and the unchanged witnessed
        /// original/late-import identities. Drawing checks below still apply.
        /// No asset, conversion phase or retained Library is regenerated.
        /// </summary>
        static bool QualifyLoadingLineEndings(NativeSprite row,byte[] bytes)
        {
            string expectedName=row.assetPath=="Assets/Sprite/LoadingBase_0.asset"?"LoadingBase":
                row.assetPath=="Assets/Sprite/LoadingOverlay_0.asset"?"LoadingOverlay":null;
            if(expectedName==null||!row.nativeDrawingStateRestored||row.originalCollection!="resources.assets"||
               row.originalPathId<=0||bytes.Length==0||bytes.Length>1048576)
                return false;
            byte[] inverse=FlipLineEndings(bytes);
            if(inverse==null||Hash(inverse)!=row.sha256)return false;
            string sourcePath=QuestSpriteGeometryValidation.InputPath;
            string importedPath=QuestSpriteGeometryValidation.ReceiptPath;
            if(!File.Exists(sourcePath)||!File.Exists(importedPath)||
               new FileInfo(sourcePath).Length>65536||new FileInfo(importedPath).Length>65536)
                return false;
            byte[] sourceBytes=File.ReadAllBytes(sourcePath);
            var source=JsonUtility.FromJson<LoadingSource>(System.Text.Encoding.UTF8.GetString(sourceBytes));
            var imported=JsonUtility.FromJson<QuestSpriteGeometryValidation.ValidationReceipt>(File.ReadAllText(importedPath));
            if(source==null||source.schema!=1||source.assets==null||source.assets.Length!=4||
               source.sourceSha256!=row.sourceContainerSha256||imported==null||imported.schema!=1||
               !imported.allImportedAssetsVerified||imported.sourceReceiptSha256!=Hash(sourceBytes)||imported.spinner==null)
                return false;
            LoadingAsset entry=null;
            foreach(var candidate in source.assets)
                if(candidate!=null&&candidate.asset==row.assetPath)
                {if(entry!=null)return false;entry=candidate;}
            string actualHash=Hash(bytes),metaHash=HashFile(row.assetPath+".meta");
            if(entry==null||entry.name!=expectedName||entry.guid!=row.guid||entry.sourcePathId!=row.originalPathId||
               !entry.preservedNativeDrawingGeometry||entry.preservedNativePackedGeometry||
               entry.restoredSha256!=actualHash||entry.metaSha256!=metaHash||
               entry.geometryManifest!=InputPath||entry.geometryManifestSha256!=HashFile(InputPath))
                return false;
            int matches=0;
            foreach(var candidate in imported.spinner)
                if(candidate!=null&&candidate.assetPath==row.assetPath&&candidate.guid==row.guid&&
                   candidate.originalDrawingGeometryVerified&&candidate.sourceSha256==actualHash&&candidate.metaSha256==metaHash)
                    ++matches;
            return matches==1;
        }

        static byte[] FlipLineEndings(byte[] bytes)
        {
            int newlines=0,carriages=0;
            for(int index=0;index<bytes.Length;index++)
            {
                if(bytes[index]==13)
                {if(index+1==bytes.Length||bytes[index+1]!=10)return null;++carriages;}
                if(bytes[index]==10)
                {if(carriages>0&&(index==0||bytes[index-1]!=13))return null;++newlines;}
            }
            if(newlines==0||(carriages!=0&&carriages!=newlines))return null;
            byte[] result=new byte[bytes.Length+(carriages==0?newlines:-carriages)];
            int output=0;
            foreach(byte value in bytes)
            {
                if(carriages>0&&value==13)continue;
                if(carriages==0&&value==10)result[output++]=13;
                result[output++]=value;
            }
            return result;
        }
        static string HashFile(string path)
        {
            using(var stream=File.OpenRead(path))
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
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
