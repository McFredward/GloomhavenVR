#if UNITY_EDITOR
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
            public int lineEndingQualifiedLoadingSprites,targetedReimportedSprites;
            public string unityVersion,sourceManifestSha256;
            public bool headsetPictureVerified;
        }
        public static Receipt Validate()
        {
            string manifestHash=HashFile(InputPath);
            var input=JsonUtility.FromJson<Input>(File.ReadAllText(InputPath));
            if(input==null||input.schema!=1||input.assets==null||input.restoredNonPackedSpriteCount!=input.assets.Length||
               input.nativeSpriteCount!=input.restoredNonPackedSpriteCount+input.preservedPackedSpriteCount)
                throw new InvalidDataException("Original full Sprite source inventory is incomplete.");
            int importedCount=0,lineEndingQualified=0,reimportedCount=0;
            var progress=new QuestWizardProgress.Counter("unity-validation-campaign-sprites","unity-validation",
                input.assets.Length,"sprites","Original Campaign sprites");
            foreach(var row in input.assets)
            {
                if(row==null||String.IsNullOrEmpty(row.assetPath)||!row.nativeDrawingStateRestored||row.rect==null)
                    throw new InvalidDataException("Original Sprite source inventory identity is incomplete.");
                progress.Report(importedCount,row.assetPath);
                byte[] bytes=File.ReadAllBytes(row.assetPath);
                string actualHash=Hash(bytes),transportReason;
                bool sourceMatches=actualHash==row.sha256;
                if(!sourceMatches)
                {
                    if(!CheckLoadingLineEndings(row,bytes,out transportReason,manifestHash))
                        throw new InvalidDataException("Original Sprite import identity differs: "+row.assetPath+
                            "; sourceSha256 expected="+row.sha256+" actual="+actualHash+"; loadingTransport="+transportReason);
                    ++lineEndingQualified;
                    Debug.Log("[Quest startup] original loading Sprite line-ending transport qualified; asset="+
                        row.assetPath+", bytes="+bytes.Length+"; completed conversion and Unity Library retained.");
                }
                if(!MatchesSourceGuid(row))
                    throw new InvalidDataException("Original Sprite source metadata identity differs: "+row.assetPath+
                        "; expected unique .meta GUID="+row.guid+"; source metadata must be restored before reimport.");
                // The original complete source bytes must already be proved before
                // retrying an imported object. Capture075120 had a collapsed identity
                // guard, obscuring source drift versus a stale Library object. A single
                // synchronous reimport of this exact asset can repair stale native
                // object state without regenerating assets or deleting the Library.
                // Never infer a replacement GUID or repair unproved source geometry.
                try { ValidateImportedSprite(row); }
                catch(InvalidDataException first)
                {
                    if(!MatchesSourceGuid(row))
                        throw new InvalidDataException(first.Message+"; targeted reimport refused: source .meta GUID is not exact.",first);
                    Debug.Log("[Quest startup] targeted original Sprite reimport; asset="+row.assetPath+
                        "; reason="+first.Message+"; attempt=1/1; source bytes and Library retained.");
                    progress.Report(importedCount,"Repairing imported Sprite: "+row.assetPath);
                    try { AssetDatabase.ImportAsset(row.assetPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport); }
                    catch(Exception importFailure)
                    {
                        throw new InvalidDataException("Original Sprite targeted reimport failed: "+row.assetPath+
                            "; attempt=1/1; "+importFailure.GetType().Name+": "+importFailure.Message,importFailure);
                    }
                    try { ValidateImportedSprite(row); }
                    catch(InvalidDataException second)
                    {
                        throw new InvalidDataException(second.Message+"; targeted reimport attempt=1/1 did not restore the witnessed original import.",second);
                    }
                    ++reimportedCount;
                    Debug.Log("[Quest startup] targeted original Sprite reimport verified; asset="+row.assetPath+"; attempt=1/1.");
                }
                // textureRect is not queried: Unity rejects that accessor for
                // legitimate tight-packed geometry; original streams prove shape.
                if(++importedCount%64==0)EditorUtility.UnloadUnusedAssetsImmediate();
                progress.Report(importedCount,row.assetPath);
            }
            var receipt=new Receipt{unityVersion=Application.unityVersion,nativeSpriteCount=input.nativeSpriteCount,
                importedNonPackedSpriteCount=input.assets.Length,preservedPackedSpriteCount=input.preservedPackedSpriteCount,
                lineEndingQualifiedLoadingSprites=lineEndingQualified,targetedReimportedSprites=reimportedCount,
                sourceManifestSha256=manifestHash};
            Directory.CreateDirectory("QuestCampaignEvidence");
            File.WriteAllText("QuestCampaignEvidence/native-sprite-import.json",JsonUtility.ToJson(receipt,true));
            progress.Complete("Original Campaign sprite import verified");
            return receipt;
        }

        static bool MatchesSourceGuid(NativeSprite row)
        {
            string path=row.assetPath+".meta";
            if(!File.Exists(path)||new FileInfo(path).Length>16384)return false;
            var matches=Regex.Matches(File.ReadAllText(path),@"^guid:[ \t]*([0-9a-f]{32})[ \t]*\r?$",RegexOptions.Multiline);
            return matches.Count==1&&matches[0].Groups[1].Value==row.guid;
        }

        static void ValidateImportedSprite(NativeSprite row)
        {
            var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(row.assetPath);
            if(sprite==null)throw new InvalidDataException("Original Sprite import identity differs: "+row.assetPath+"; imported Sprite is missing.");
            string guid;long fileId;
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite,out guid,out fileId))
                throw new InvalidDataException("Original Sprite import identity differs: "+row.assetPath+"; imported GUID/fileID could not be read.");
            if(guid!=row.guid||fileId!=row.fileId)
                throw new InvalidDataException("Original Sprite import identity differs: "+row.assetPath+
                    "; GUID expected="+row.guid+" actual="+guid+"; fileID expected="+row.fileId+" actual="+fileId);
            Rect rect=sprite.rect;
            Require(rect.x,row.rect.x,row.assetPath+" rect.x");Require(rect.y,row.rect.y,row.assetPath+" rect.y");
            Require(rect.width,row.rect.width,row.assetPath+" rect.width");Require(rect.height,row.rect.height,row.assetPath+" rect.height");
            Require(sprite.pivot.x,row.pivot.x*rect.width,row.assetPath+" pivot.x");
            Require(sprite.pivot.y,row.pivot.y*rect.height,row.assetPath+" pivot.y");
            for(int index=0;index<4;index++)Require(sprite.border[index],row.border[index],row.assetPath+" border["+index+"]");
            Require(sprite.pixelsPerUnit,row.pixelsPerUnit,row.assetPath+" pixelsPerUnit");
            if(String.IsNullOrEmpty(row.textureGuid))
            {
                if(sprite.texture!=null)throw new InvalidDataException("Originally empty Sprite acquired a texture: "+row.assetPath);
            }
            else if(sprite.texture==null||sprite.texture.width!=row.textureWidth||sprite.texture.height!=row.textureHeight||
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture))!=row.textureGuid)
                throw new InvalidDataException("Original Sprite texture binding differs: "+row.assetPath+
                    "; expected GUID="+row.textureGuid+" size="+row.textureWidth+"x"+row.textureHeight+
                    "; actual="+(sprite.texture==null?"null":AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite.texture))+
                    " "+sprite.texture.width+"x"+sprite.texture.height));
            RequireVectors(sprite.vertices,row.vertices,row.assetPath+" vertices");
            RequireVectors(sprite.uv,row.uv,row.assetPath+" UV");
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
            string reason;return CheckLoadingLineEndings(row,bytes,out reason);
        }
        static bool CheckLoadingLineEndings(NativeSprite row,byte[] bytes,out string reason,string manifestHash=null)
        {
            reason="not an exact witnessed nonpacked loading Sprite";
            string expectedName=row.assetPath=="Assets/Sprite/LoadingBase_0.asset"?"LoadingBase":
                row.assetPath=="Assets/Sprite/LoadingOverlay_0.asset"?"LoadingOverlay":null;
            if(expectedName==null||!row.nativeDrawingStateRestored||row.originalCollection!="resources.assets"||
               row.originalPathId<=0||bytes.Length==0||bytes.Length>1048576)
                return false;
            reason="source bytes are not the exact inverse LF/CRLF representation";
            byte[] inverse=FlipLineEndings(bytes);
            if(inverse==null||Hash(inverse)!=row.sha256)return false;
            reason="loading source/import receipt is absent or oversized";
            string sourcePath=QuestSpriteGeometryValidation.InputPath;
            string importedPath=QuestSpriteGeometryValidation.ReceiptPath;
            if(!File.Exists(sourcePath)||!File.Exists(importedPath)||
               new FileInfo(sourcePath).Length>65536||new FileInfo(importedPath).Length>65536)
                return false;
            byte[] sourceBytes=File.ReadAllBytes(sourcePath);
            var source=JsonUtility.FromJson<LoadingSource>(System.Text.Encoding.UTF8.GetString(sourceBytes));
            var imported=JsonUtility.FromJson<QuestSpriteGeometryValidation.ValidationReceipt>(File.ReadAllText(importedPath));
            reason="loading source/import receipt identity is incomplete or differs";
            if(source==null||source.schema!=1||source.assets==null||source.assets.Length!=4||
               source.sourceSha256!=row.sourceContainerSha256||imported==null||imported.schema!=1||
               !imported.allImportedAssetsVerified||imported.sourceReceiptSha256!=Hash(sourceBytes)||imported.spinner==null)
                return false;
            reason="loading source has an ambiguous asset entry";
            LoadingAsset entry=null;
            foreach(var candidate in source.assets)
                if(candidate!=null&&candidate.asset==row.assetPath)
                {if(entry!=null)return false;entry=candidate;}
            string actualHash=Hash(bytes),metaHash=HashFile(row.assetPath+".meta");
            reason="loading source entry identity/bytes/GUID metadata differs";
            if(entry==null||entry.name!=expectedName||entry.guid!=row.guid||entry.sourcePathId!=row.originalPathId||
               !entry.preservedNativeDrawingGeometry||entry.preservedNativePackedGeometry||
               entry.restoredSha256!=actualHash||entry.metaSha256!=metaHash||entry.geometryManifest!=InputPath)
                return false;
            if(manifestHash==null)manifestHash=HashFile(InputPath);
            reason="loading geometry manifest snapshot differs without an exact case-path migration: expected="+
                entry.geometryManifestSha256+" actual="+manifestHash;
            if(entry.geometryManifestSha256!=manifestHash&&!QualifyCasePathManifest(entry.geometryManifestSha256,manifestHash))return false;
            reason="loading import receipt does not uniquely verify current bytes, metadata and geometry";
            int matches=0;
            foreach(var candidate in imported.spinner)
                if(candidate!=null&&candidate.assetPath==row.assetPath&&candidate.guid==row.guid&&
                   candidate.originalDrawingGeometryVerified&&candidate.sourceSha256==actualHash&&candidate.metaSha256==metaHash)
                    ++matches;
            if(matches!=1)return false;
            reason="qualified";return true;
        }

        /// <summary>
        /// Capture075120 still failed the old guard because the later case-path
        /// producer changed the complete native manifest, while preserving these
        /// two loading rows and their complete source bytes. Its durable receipt
        /// records the exact before/after native-manifest hashes. Consume only
        /// that recorded transition; never accept an arbitrary newer manifest or
        /// rewrite completed preparation receipts. Unrelated manifest-map entries
        /// may legitimately differ: the actual retained receipt proves that case.
        /// </summary>
        static bool QualifyCasePathManifest(string before,string after)
        {
            const string path="QuestStartupEvidence/case-path-migration.json";
            if(!File.Exists(path)||new FileInfo(path).Length>2097152)return false;
            try
            {
                var root=new StrictJson(File.ReadAllText(path)).Read() as Dictionary<string,object>;
                object schema,target,content,references,keys,beforeMap,afterMap;
                if(root==null||!root.TryGetValue("schema",out schema)||!(schema is JsonNumber)||((JsonNumber)schema).Text!="1"||
                   !root.TryGetValue("target",out target)||(target as string)!="game"||
                   !root.TryGetValue("assetContentChanged",out content)||!(content is bool)||(bool)content||
                   !root.TryGetValue("serializedReferencesChanged",out references)||!(references is bool)||(bool)references||
                   !root.TryGetValue("addressableKeysChanged",out keys)||!(keys is bool)||(bool)keys||
                   !root.TryGetValue("beforeManifestSha256",out beforeMap)||!root.TryGetValue("manifestSha256",out afterMap))return false;
                string recordedBefore,recordedAfter;
                return ManifestHash(beforeMap,InputPath,out recordedBefore)&&ManifestHash(afterMap,InputPath,out recordedAfter)&&
                    recordedBefore==before&&recordedAfter==after;
            }
            catch(InvalidDataException) {return false;}
        }
        static bool ManifestHash(object value,string path,out string hash)
        {
            hash=null;var map=value as Dictionary<string,object>;
            if(map==null)return false;
            foreach(var pair in map)
            {
                string digest=pair.Value as string;
                if(!pair.Key.StartsWith("Assets/",StringComparison.Ordinal)||pair.Key.Contains("..")||pair.Key.Contains("\\")||
                    digest==null||!Regex.IsMatch(digest,@"\A[0-9a-f]{64}\z"))return false;
                if(pair.Key==path)hash=digest;
            }
            return hash!=null;
        }

        // JsonUtility does not deserialize dictionaries and silently resolves
        // duplicate JSON members. A bounded strict reader preserves the two flat
        // SHA maps and rejects duplicate, malformed, nested or truncated proofs.
        sealed class JsonNumber {public string Text;public JsonNumber(string text){Text=text;}}
        sealed class StrictJson
        {
            readonly string text;int index;
            public StrictJson(string value){text=value;}
            public object Read(){object value=Value(0);Space();if(index!=text.Length)Fail();return value;}
            void Space(){while(index<text.Length&&(text[index]==' '||text[index]=='\r'||text[index]=='\n'||text[index]=='\t'))++index;}
            bool Take(char value){Space();if(index<text.Length&&text[index]==value){++index;return true;}return false;}
            void Need(char value){if(!Take(value))Fail();}
            static void Fail(){throw new InvalidDataException("Malformed or duplicate case-path migration JSON proof.");}
            object Value(int depth)
            {
                Space();if(depth>32||index>=text.Length){Fail();return null;}
                char token=text[index];
                if(token=='"')return String();
                if(token=='{')
                {
                    ++index;var result=new Dictionary<string,object>(StringComparer.Ordinal);
                    if(Take('}'))return result;
                    do {Space();string key=String();Need(':');if(result.ContainsKey(key))Fail();result.Add(key,Value(depth+1));}while(Take(','));
                    Need('}');return result;
                }
                if(token=='[')
                {
                    ++index;var result=new List<object>();if(Take(']'))return result;
                    do {result.Add(Value(depth+1));}while(Take(','));Need(']');return result;
                }
                foreach(string literal in new[]{"true","false","null"})
                    if(index+literal.Length<=text.Length&&System.String.CompareOrdinal(text,index,literal,0,literal.Length)==0)
                    {index+=literal.Length;return literal=="null"?null:(object)(literal=="true");}
                int start=index;
                while(index<text.Length&&"-+0123456789.eE".IndexOf(text[index])>=0)++index;
                string number=text.Substring(start,index-start);
                if(!Regex.IsMatch(number,@"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z"))Fail();
                return new JsonNumber(number);
            }
            string String()
            {
                Space();if(index>=text.Length||text[index++]!='"'){Fail();return null;}
                var result=new StringBuilder();
                while(index<text.Length)
                {
                    char value=text[index++];if(value=='"')return result.ToString();if(value<32)Fail();
                    if(value!='\\'){result.Append(value);continue;}
                    if(index>=text.Length)Fail();char escape=text[index++];
                    switch(escape)
                    {
                        case '"':case '\\':case '/':result.Append(escape);break;
                        case 'b':result.Append('\b');break;case 'f':result.Append('\f');break;
                        case 'n':result.Append('\n');break;case 'r':result.Append('\r');break;case 't':result.Append('\t');break;
                        case 'u':
                            if(index+4>text.Length)Fail();int code=0;
                            for(int part=0;part<4;part++)
                            {char hex=text[index++];int digit=hex>='0'&&hex<='9'?hex-'0':hex>='a'&&hex<='f'?hex-'a'+10:hex>='A'&&hex<='F'?hex-'A'+10:-1;if(digit<0)Fail();code=code*16+digit;}
                            result.Append((char)code);break;
                        default:Fail();break;
                    }
                }
                Fail();return null;
            }
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
                throw new InvalidDataException("Original Sprite drawing state differs: "+source+"; expected="+expected+" actual="+actual);
        }
        private static string Hash(byte[] data)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();
        }
    }
}
#endif
