"""Exact native bytes and the audited old Windows loading transport contract."""
from __future__ import annotations
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import sprites
from test_sprites import asset, bank


def digest(value):
    return hashlib.sha256(value).hexdigest()


class PreservedNativeLoadingBytes(unittest.TestCase):
    def check_transport(self, newline):
        with tempfile.TemporaryDirectory(prefix="quest-native-loading-") as temporary:
            root = Path(temporary); game = root / "game"; project = root / "project"
            game.mkdir(); (game / "resources.assets").write_bytes(bank())
            folder = project / "Assets/Sprite"; folder.mkdir(parents=True)
            original = {}
            rows = []
            for index, name in enumerate(("LoadingBase", "LoadingOverlay"), 1):
                path = folder / (name + "_0.asset")
                text = asset(name, trimmed=False).replace("    x: 100\n", "    x: 0\n", 1).replace("    y: 200\n", "    y: 0\n", 1)
                path.write_text(text, encoding="utf-8", newline=newline)
                guid = str(index) * 32
                Path(str(path) + ".meta").write_text("guid: " + guid + "\n", encoding="utf-8")
                original[path] = (path.read_bytes(), path.stat().st_mtime_ns)
                rows.append({"assetPath": path.relative_to(project).as_posix(), "guid": guid,
                             "sha256": digest(path.read_bytes()), "originalCollection": "resources.assets",
                             "originalPathId": index})
            manifest = project / "Assets/QuestOriginalCampaign/native-sprites.json"
            manifest.parent.mkdir(parents=True); manifest.write_text(json.dumps({"assets": rows}), encoding="utf-8")
            receipt = sprites.restore_loading_sprite_geometry(project, game)
            for path, (payload, stamp) in original.items():
                self.assertEqual(path.read_bytes(), payload)
                self.assertEqual(path.stat().st_mtime_ns, stamp)
                row = next(row for row in receipt["assets"] if row["asset"] == path.relative_to(project).as_posix())
                self.assertEqual(row["restoredSha256"], digest(payload))
                self.assertTrue(row["preservedNativeDrawingGeometry"])
            self.assertEqual(receipt, sprites.restore_loading_sprite_geometry(project, game))

    def test_native_crlf_bytes_and_import_witness_are_preserved(self):
        self.check_transport("\r\n")

    def test_native_lf_bytes_and_import_witness_are_preserved(self):
        self.check_transport("\n")


HARNESS = r'''
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using GloomhavenVR.Quest.Editor;

namespace UnityEngine {
    public struct Vector2 { public float x,y; }
    public struct Vector4 { public float x,y,z,w; public float this[int index] { get { return index==0?x:index==1?y:index==2?z:w; } } }
    public struct Rect { public float x,y,width,height; }
    public class Texture2D { public int width,height; }
    public class Sprite { public Rect rect; public Vector2 pivot; public Vector4 border; public float pixelsPerUnit; public Texture2D texture; public Vector2[] vertices,uv; }
    public static class Debug { public static void Log(object value) {} }
    public static class Application { public static string unityVersion="fixture"; }
    public static class JsonUtility {
        static object Decode(JsonElement element,Type type) {
            if(element.ValueKind==JsonValueKind.Null)return null;
            if(type==typeof(string))return element.GetString();
            if(type==typeof(bool))return element.GetBoolean();
            if(type==typeof(int))return element.GetInt32();
            if(type==typeof(long))return element.GetInt64();
            if(type==typeof(float))return element.GetSingle();
            if(type.IsArray) { var values=element.EnumerateArray();var result=Array.CreateInstance(type.GetElementType(),element.GetArrayLength());int index=0;foreach(var value in values)result.SetValue(Decode(value,type.GetElementType()),index++);return result; }
            var instance=Activator.CreateInstance(type,true);
            foreach(var field in type.GetFields(BindingFlags.Public|BindingFlags.Instance))if(element.TryGetProperty(field.Name,out var value))field.SetValue(instance,Decode(value,field.FieldType));
            return instance;
        }
        public static T FromJson<T>(string value) { using(var document=JsonDocument.Parse(value))return (T)Decode(document.RootElement,typeof(T)); }
        public static string ToJson(object value,bool pretty=false)=>JsonSerializer.Serialize(value,new JsonSerializerOptions{IncludeFields=true});
    }
}
namespace UnityEditor {
    public static class AssetDatabase {
        public static T LoadAssetAtPath<T>(string path) where T:class=>null;
        public static bool TryGetGUIDAndLocalFileIdentifier(object value,out string guid,out long id){guid=null;id=0;return false;}
        public static string AssetPathToGUID(string value)=>null;
        public static string GetAssetPath(object value)=>null;
    }
    public static class EditorUtility { public static void UnloadUnusedAssetsImmediate(){} }
}
namespace GloomhavenVR.Quest.Editor {
    public static class QuestWizardProgress { public sealed class Counter { public Counter(string a,string b,int c,string d,string e){} public void Report(int count,string path){}public void Complete(string detail){} } }
    public static class QuestSpriteGeometryValidation {
        public const string InputPath="QuestStartupEvidence/loading-sprite-geometry.json";
        public const string ReceiptPath="QuestStartupEvidence/loading-sprite-import.json";
        public sealed class ImportedSprite { public string assetPath,guid,sourceSha256,metaSha256;public bool originalDrawingGeometryVerified; }
        public sealed class ValidationReceipt { public int schema;public string sourceReceiptSha256;public bool allImportedAssetsVerified;public ImportedSprite[] spinner; }
    }
}
class Program {
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();}
    static Dictionary<string,object> Map(params object[] pairs){var result=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)result[(string)pairs[i]]=pairs[i+1];return result;}
    static void Write(string path,object value){Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,JsonSerializer.Serialize(value));}
    static int Main(string[] args){
        string test=args[0],name=test.StartsWith("overlay-")?"LoadingOverlay":"LoadingBase";
        string path="Assets/Sprite/"+name+"_0.asset",guid=new string('a',32),container=new string('b',64);
        string text="%YAML 1.1\n--- !u!213 &21300000\nSprite:\n  m_Name: "+name+"\n  m_Rect:\n    height: 128\n";
        bool currentCrlf=test.EndsWith("crlf-current");
        byte[] original=Encoding.UTF8.GetBytes(currentCrlf?text:text.Replace("\n","\r\n"));
        byte[] current=Encoding.UTF8.GetBytes(currentCrlf?text.Replace("\n","\r\n"):text);
        if(test=="geometry-change")current=Encoding.UTF8.GetBytes(text.Replace("height: 128","height: 129"));
        if(test=="unrelated-bytes")current=Encoding.UTF8.GetBytes(text+"  unrelated: 1\n");
        if(test=="mixed-newlines")current=Encoding.UTF8.GetBytes(text.Replace("%YAML 1.1\n","%YAML 1.1\r\n"));
        if(test=="bare-carriage")current=Encoding.UTF8.GetBytes(text+"\r");
        if(test=="unrelated-sprite")path="Assets/Sprite/CardBack.asset";
        if(test=="packed-loading")path="Assets/Sprite/LoadingBase.asset";
        var row=new QuestCampaignSpriteValidation.NativeSprite {assetPath=path,guid=guid,sha256=Hash(original),nativeDrawingStateRestored=true,originalCollection="resources.assets",originalPathId=47,sourceContainerSha256=container};
        if(test=="wrong-collection")row.originalCollection="sharedassets0.assets";
        if(test=="unrestored-native")row.nativeDrawingStateRestored=false;
        Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllBytes(path,current);
        byte[] meta=Encoding.UTF8.GetBytes("guid: "+guid+"\n");File.WriteAllBytes(path+".meta",meta);
        string manifest=QuestCampaignSpriteValidation.InputPath;Write(manifest,Map("schema",1,"assets",new[]{Map("assetPath",path,"sha256",row.sha256,"guid",guid)}));
        var entry=Map("name",name,"asset",path,"guid",guid,"sourcePathId",47,"preservedNativeDrawingGeometry",true,"preservedNativePackedGeometry",false,"restoredSha256",Hash(current),"metaSha256",Hash(meta),"geometryManifest",manifest,"geometryManifestSha256",Hash(File.ReadAllBytes(manifest)));
        var other=Map("asset","Assets/Sprite/"+(name=="LoadingBase"?"LoadingOverlay":"LoadingBase")+"_0.asset");
        object[] entries={entry,other,Map("asset","Assets/Sprite/LoadingBase.asset"),Map("asset","Assets/Sprite/LoadingOverlay.asset")};
        if(test=="wrong-guid")entry["guid"]=new string('c',32);
        if(test=="wrong-path-id")entry["sourcePathId"]=48;
        if(test=="packed-proof")entry["preservedNativePackedGeometry"]=true;
        if(test=="unpreserved-proof")entry["preservedNativeDrawingGeometry"]=false;
        if(test=="wrong-source-hash")entry["restoredSha256"]=new string('0',64);
        if(test=="wrong-meta")entry["metaSha256"]=new string('0',64);
        if(test=="wrong-manifest")entry["geometryManifestSha256"]=new string('0',64);
        if(test=="duplicate-source")entries[3]=entry;
        string sourcePath=QuestSpriteGeometryValidation.InputPath;
        Write(sourcePath,Map("schema",1,"sourceSha256",test=="wrong-container"?new string('d',64):container,"assets",entries));
        var imported=Map("assetPath",path,"guid",guid,"sourceSha256",Hash(current),"metaSha256",Hash(meta),"originalDrawingGeometryVerified",test!="unverified-geometry");
        Write(QuestSpriteGeometryValidation.ReceiptPath,Map("schema",1,"sourceReceiptSha256",test=="wrong-source-receipt"?new string('0',64):Hash(File.ReadAllBytes(sourcePath)),"allImportedAssetsVerified",test!="failed-import","spinner",test=="duplicate-import"?new[]{imported,imported}:new[]{imported}));
        if(test=="missing-receipt")File.Delete(QuestSpriteGeometryValidation.ReceiptPath);
        if(test=="changed-manifest")File.AppendAllText(manifest," ");
        if(test=="changed-meta")File.AppendAllText(path+".meta"," ");
        var method=typeof(QuestCampaignSpriteValidation).GetMethod("QualifyLoadingLineEndings",BindingFlags.Static|BindingFlags.NonPublic);
        Console.WriteLine(method.Invoke(null,new object[]{row,current}));
        return 0;
    }
}
'''


class OldWindowsLoadingTransport(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
        if not Path(dotnet).is_file(): raise unittest.SkipTest("The focused C# transport proof needs a local .NET SDK.")
        cls.temp = tempfile.TemporaryDirectory(prefix="quest-loading-transport-")
        cls.root = Path(cls.temp.name); cls.dotnet = dotnet
        (cls.root / "Program.cs").write_text(HARNESS, encoding="utf-8")
        source = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignSpriteValidation.cs"
        shutil.copy2(source, cls.root / "Production.cs")
        (cls.root / "proof.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType><DefineConstants>UNITY_EDITOR</DefineConstants><Nullable>disable</Nullable><TreatWarningsAsErrors>false</TreatWarningsAsErrors></PropertyGroup></Project>', encoding="utf-8")
        cls.env = {**os.environ, "DOTNET_ROOT": str(Path(dotnet).parent), "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
        built = subprocess.run([dotnet, "build", "proof.csproj", "--nologo", "-v:q", "--disable-build-servers", "-p:UseSharedCompilation=false"], cwd=cls.root, env=cls.env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        if built.returncode: raise AssertionError(built.stdout)
        cls.program = cls.root / "bin/Debug/net8.0/proof.dll"

    @classmethod
    def tearDownClass(cls):
        if hasattr(cls, "temp"): cls.temp.cleanup()

    def check_case(self, case, expected):
        run = self.root / ("run-" + case); run.mkdir(exist_ok=True)
        result = subprocess.run([self.dotnet, str(self.program), case], cwd=run, env=self.env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout)
        self.assertEqual(result.stdout.strip(), str(expected), result.stdout)


def _case(name, expected):
    def test(self): self.check_case(name, expected)
    return test


for _name in ("lf-current", "crlf-current", "overlay-lf-current", "overlay-crlf-current"):
    setattr(OldWindowsLoadingTransport, "test_accepts_exact_" + _name.replace("-", "_"), _case(_name, True))
for _name in ("geometry-change", "unrelated-bytes", "mixed-newlines", "bare-carriage", "unrelated-sprite", "packed-loading", "wrong-guid", "wrong-path-id", "wrong-collection", "unrestored-native", "wrong-container", "packed-proof", "unpreserved-proof", "wrong-source-hash", "wrong-meta", "wrong-manifest", "duplicate-source", "duplicate-import", "unverified-geometry", "failed-import", "wrong-source-receipt", "missing-receipt", "changed-manifest", "changed-meta"):
    setattr(OldWindowsLoadingTransport, "test_rejects_" + _name.replace("-", "_"), _case(_name, False))


if __name__ == "__main__": unittest.main()
