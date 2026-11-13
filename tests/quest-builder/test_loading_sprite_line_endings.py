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
    public static class Debug { public static List<string> Logs=new List<string>();public static void Log(object value) {Logs.Add(value.ToString());} }
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
    [Flags] public enum ImportAssetOptions { ForceUpdate=1,ForceSynchronousImport=2 }
    public static class AssetDatabase {
        public sealed class Fault { public string assetPath,before,after; }
        public sealed class Setup { public Fault[] faults; }
        static Dictionary<string,QuestCampaignSpriteValidation.NativeSprite> rows;
        static Dictionary<string,Fault> faults;
        static Dictionary<object,string> paths=new Dictionary<object,string>();
        public static List<string> Imports=new List<string>();
        public static List<int> Options=new List<int>();
        public static void Initialize(string configuration){
            rows=new Dictionary<string,QuestCampaignSpriteValidation.NativeSprite>();
            foreach(var row in UnityEngine.JsonUtility.FromJson<QuestCampaignSpriteValidation.Input>(File.ReadAllText(QuestCampaignSpriteValidation.InputPath)).assets)rows.Add(row.assetPath,row);
            faults=new Dictionary<string,Fault>();
            foreach(var fault in UnityEngine.JsonUtility.FromJson<Setup>(File.ReadAllText(configuration)).faults)faults.Add(fault.assetPath,fault);
        }
        static string FaultAt(string path)=>faults.ContainsKey(path)?(Imports.Contains(path)?faults[path].after:faults[path].before):"correct";
        public static T LoadAssetAtPath<T>(string path) where T:class {
            if(rows==null||!rows.ContainsKey(path)||(FaultAt(path)=="missing"||FaultAt(path)=="importthrows"))return null;
            var row=rows[path];
            var sprite=new UnityEngine.Sprite { rect=new UnityEngine.Rect {x=row.rect.x,y=row.rect.y,width=row.rect.width,height=row.rect.height},
                pivot=new UnityEngine.Vector2 {x=row.pivot.x*row.rect.width,y=row.pivot.y*row.rect.height},border=row.border,pixelsPerUnit=row.pixelsPerUnit,
                vertices=(UnityEngine.Vector2[])row.vertices.Clone(),uv=(UnityEngine.Vector2[])row.uv.Clone() };
            if(!String.IsNullOrEmpty(row.textureGuid)){
                sprite.texture=new UnityEngine.Texture2D {width=row.textureWidth,height=row.textureHeight};paths[sprite.texture]="Textures/"+row.textureGuid;
            }
            string fault=FaultAt(path);
            if(fault=="rect")sprite.rect.width++;
            if(fault=="pivot")sprite.pivot.x++;
            if(fault=="border")sprite.border.x++;
            if(fault=="pixels")sprite.pixelsPerUnit++;
            if(fault=="texture"&&sprite.texture!=null)sprite.texture.width++;
            if(fault=="vertices"&&sprite.vertices.Length>0)sprite.vertices[0].x++;
            if(fault=="uv"&&sprite.uv.Length>0)sprite.uv[0].x++;
            paths[sprite]=path;return sprite as T;
        }
        public static bool TryGetGUIDAndLocalFileIdentifier(object value,out string guid,out long id){
            string path=paths[value];guid=FaultAt(path)=="guid"?new string('0',32):rows[path].guid;
            id=FaultAt(path)=="fileid"?0:rows[path].fileId;return FaultAt(path)!="identifier";
        }
        public static string AssetPathToGUID(string value)=>value.StartsWith("Textures/")?value.Substring(9):rows[value].guid;
        public static string GetAssetPath(object value)=>paths[value];
        public static void ImportAsset(string path,ImportAssetOptions options){bool fails=FaultAt(path)=="importthrows";Imports.Add(path);Options.Add((int)options);if(fails)throw new IOException("fixture import I/O failure");}
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
        if(args[0]=="--validate"){
            UnityEditor.AssetDatabase.Initialize(args[1]);
            object receipt=null;string error=null;
            try {receipt=QuestCampaignSpriteValidation.Validate();}catch(Exception failure){error=failure.Message;}
            Console.WriteLine(JsonSerializer.Serialize(new {receipt,error,imports=UnityEditor.AssetDatabase.Imports,options=UnityEditor.AssetDatabase.Options,logs=UnityEngine.Debug.Logs},new JsonSerializerOptions{IncludeFields=true}));
            return 0;
        }
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


class ComposedCampaignSpriteRepair(unittest.TestCase):
    """Run public Validate after real loading and case-path producers, not a private guard."""
    @classmethod
    def setUpClass(cls):
        OldWindowsLoadingTransport.setUpClass.__func__(cls)

    @classmethod
    def tearDownClass(cls):
        OldWindowsLoadingTransport.tearDownClass.__func__(cls)

    def fixture(self, folder, *, transport="lf-current", migrate=True):
        sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
        import case_paths
        project = folder / "project"; game = folder / "game"
        game.mkdir(); (game / "resources.assets").write_bytes(bank())
        (project / "Assets/Quest").mkdir(parents=True)
        campaign = project / "Assets/QuestOriginalCampaign"; campaign.mkdir()
        source_hash = digest((game / "resources.assets").read_bytes())
        native = []; packed = []
        for index, name in enumerate(("LoadingBase", "LoadingOverlay"), 1):
            original = asset(name, trimmed=False).replace("    x: 100\n    y: 200", "    x: 0\n    y: 0", 1)
            for suffix, rows in (("_0", native), ("", packed)):
                path = project / ("Assets/Sprite/" + name + suffix + ".asset")
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(original, encoding="utf-8", newline="\n" if transport == "crlf-current" else "\r\n")
                guid = str(index + (2 if suffix == "" else 0)) * 32
                Path(str(path) + ".meta").write_text("guid: " + guid + "\n", encoding="utf-8")
                rows.append({"assetPath": path.relative_to(project).as_posix(), "guid": guid,
                    "sha256": digest(path.read_bytes()), "fileId": 21300000,
                    "originalCollection": "resources.assets", "originalPathId": index,
                    "sourceContainerSha256": source_hash, "nativeDrawingStateRestored": True,
                    "rect": {"x": 0, "y": 0, "width": 128, "height": 128},
                    "pivot": {"x": .5, "y": .5}, "border": {"x": 0, "y": 0, "z": 0, "w": 0},
                    "pixelsPerUnit": 100, "textureGuid": "a" * 32, "textureWidth": 256, "textureHeight": 256,
                    "vertices": [{"x": 1, "y": 2}], "uv": [{"x": .25, "y": .5}]})
        for name, guid in (("Other", "5" * 32), ("other", "6" * 32)):
            path = project / ("Assets/Sprite/" + name + ".asset")
            path.write_text(asset("Other", trimmed=False), encoding="utf-8")
            Path(str(path) + ".meta").write_text("guid: " + guid + "\n", encoding="utf-8")
            if name == "other": native.append({**native[0], "assetPath": path.relative_to(project).as_posix(),
                "guid": guid, "sha256": digest(path.read_bytes()), "originalPathId": 3})
        def write(path, value):
            path.write_text(json.dumps(value), encoding="utf-8")
        manifest = campaign / "native-sprites.json"
        write(manifest, {"schema": 1, "nativeSpriteCount": len(native) + 2,
            "restoredNonPackedSpriteCount": len(native), "preservedPackedSpriteCount": 2, "assets": native})
        write(campaign / "packed-sprites.json", {"schema": 1, "sprites": [
            {**row, "collection": "resources.assets", "pathId": row["originalPathId"]} for row in packed]})
        write(campaign / "campaign-addressables.json", {"schema": 1, "entries": []})
        write(campaign / "script-bindings.json", {"schema": 1, "assetPaths": []})
        source = sprites.restore_loading_sprite_geometry(project, game)
        # Reproduce only the previously shipped Windows writer's LF/CRLF drift.
        # The old full native manifest remains exact; the later loading receipt
        # witnessed the new representation before case_paths rewrote its snapshot.
        for entry in source["assets"]:
            if entry["preservedNativeDrawingGeometry"] and not entry["preservedNativePackedGeometry"]:
                path = project / entry["asset"]
                payload = path.read_bytes()
                current = payload if transport == "same" else (payload.replace(b"\n", b"\r\n") if transport == "crlf-current" else payload.replace(b"\r\n", b"\n"))
                path.write_bytes(current); entry["restoredSha256"] = digest(current)
        write(project / sprites.RECEIPT, source)
        write(project / "QuestStartupEvidence/loading-sprite-import.json", {"schema": 1,
            "sourceReceiptSha256": digest((project / sprites.RECEIPT).read_bytes()),
            "allImportedAssetsVerified": True, "spinner": [{"assetPath": row["asset"], "guid": row["guid"],
                "sourceSha256": row["restoredSha256"], "metaSha256": row["metaSha256"],
                "originalDrawingGeometryVerified": True} for row in source["assets"]]})
        if migrate:
            before = digest(manifest.read_bytes()); report = case_paths.migrate(project)
            self.assertEqual(report["beforeManifestSha256"][case_paths.CAMPAIGN_ADDRESSABLES],
                digest(json.dumps({"schema": 1, "entries": []}).encode()))
            self.assertEqual(report["beforeManifestSha256"]["Assets/QuestOriginalCampaign/native-sprites.json"], before)
            self.assertNotEqual(before, digest(manifest.read_bytes()))
            self.assertTrue(report["moves"])
        return project

    def run_validate(self, project, faults=()):
        config = project / "spy-imports.json"; config.write_text(json.dumps({"faults": list(faults)}), encoding="utf-8")
        before = {path: path.read_bytes() for path in project.rglob("*") if path.is_file() and path != config
            and "QuestCampaignEvidence" not in path.parts}
        result = subprocess.run([self.dotnet, str(self.program), "--validate", str(config)], cwd=project,
            env=self.env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=20)
        self.assertEqual(result.returncode, 0, result.stdout)
        for path, payload in before.items(): self.assertEqual(path.read_bytes(), payload)
        return json.loads(result.stdout)

    def test_public_validate_accepts_real_composed_loading_and_case_producers_both_transports(self):
        for transport in ("lf-current", "crlf-current"):
            with self.subTest(transport=transport), tempfile.TemporaryDirectory(prefix="quest-composed-transport-") as temp:
                project = self.fixture(Path(temp), transport=transport)
                result = self.run_validate(project)
                self.assertIsNone(result["error"])
                self.assertEqual(result["receipt"]["lineEndingQualifiedLoadingSprites"], 2)
                self.assertEqual(result["receipt"]["importedNonPackedSpriteCount"], 3)
                self.assertEqual(result["imports"], [])

    def test_exact_native_source_needs_no_loading_or_case_receipt_repair(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp), transport="same")
            (project / "QuestStartupEvidence/loading-sprite-import.json").unlink()
            (project / "QuestStartupEvidence/case-path-migration.json").unlink()
            result = self.run_validate(project)
            self.assertIsNone(result["error"])
            self.assertEqual(result["receipt"]["lineEndingQualifiedLoadingSprites"], 0)
            self.assertEqual(result["receipt"]["targetedReimportedSprites"], 0)
            self.assertEqual(result["imports"], [])

    def test_exact_source_with_unproved_guid_metadata_is_never_reimported(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp), transport="same")
            path = "Assets/Sprite/LoadingBase_0.asset"
            (project / (path + ".meta")).write_text("guid: " + "b" * 32 + "\n")
            result = self.run_validate(project, [{"assetPath": path, "before": "fileid", "after": "correct"}])
            self.assertIn("source metadata identity differs", result["error"])
            self.assertEqual(result["imports"], [])

    def test_actual_producer_before_and_after_maps_do_not_require_unrelated_keyset_equality(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp))
            path = project / "QuestStartupEvidence/case-path-migration.json"; value = json.loads(path.read_text())
            value["beforeManifestSha256"]["Assets/QuestOriginalCampaign/native-platform-images.json"] = "a" * 64
            path.write_text(json.dumps(value))
            self.assertIsNone(self.run_validate(project)["error"])

    def test_valid_source_repairs_each_stale_import_once_and_preserves_files(self):
        for fault in ("missing", "identifier", "guid", "fileid", "rect", "pivot", "border", "pixels", "texture", "vertices", "uv"):
            with self.subTest(fault=fault), tempfile.TemporaryDirectory() as temp:
                project = self.fixture(Path(temp)); path = "Assets/Sprite/LoadingBase_0.asset"
                result = self.run_validate(project, [{"assetPath": path, "before": fault, "after": "correct"}])
                self.assertIsNone(result["error"])
                self.assertEqual(result["imports"], [path]); self.assertEqual(result["options"], [3])
                self.assertEqual(result["receipt"]["targetedReimportedSprites"], 1)
                self.assertTrue(any("targeted original Sprite reimport verified" in row for row in result["logs"]))

    def test_failed_targeted_reimport_is_bounded_and_preserves_source_files(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp)); path = "Assets/Sprite/LoadingBase_0.asset"
            result = self.run_validate(project, [{"assetPath": path, "before": "fileid", "after": "rect"}])
            self.assertIn("attempt=1/1", result["error"])
            self.assertEqual(result["imports"], [path]); self.assertIsNone(result["receipt"])
            self.assertFalse((project / "QuestCampaignEvidence/native-sprite-import.json").exists())

    def test_native_import_exception_reports_affected_path_and_single_attempt(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp)); path = "Assets/Sprite/LoadingBase_0.asset"
            result = self.run_validate(project, [{"assetPath": path, "before": "importthrows", "after": "correct"}])
            self.assertIn(path, result["error"]); self.assertIn("attempt=1/1", result["error"])
            self.assertIn("IOException: fixture import I/O failure", result["error"])
            self.assertEqual(result["imports"], [path]); self.assertIsNone(result["receipt"])

    def test_missing_case_chain_is_not_silently_accepted_or_reimported(self):
        with tempfile.TemporaryDirectory() as temp:
            project = self.fixture(Path(temp)); (project / "QuestStartupEvidence/case-path-migration.json").unlink()
            result = self.run_validate(project)
            self.assertIn("without an exact case-path migration", result["error"])
            self.assertEqual(result["imports"], [])

    def test_malformed_or_changed_case_proofs_never_qualify_a_loading_hash_mismatch(self):
        cases = ("before", "after", "missing-before", "missing-after", "wrong-target", "wrong-schema", "content", "references", "keys", "missing-flag", "duplicate-field", "duplicate-map", "duplicate-map-key", "nested-map", "truncated", "trailing", "malformed-number", "changed-manifest", "missing-import", "changed-meta", "changed-geometry", "mixed-newlines")
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as temp:
                project = self.fixture(Path(temp)); path = project / "QuestStartupEvidence/case-path-migration.json"
                data = json.loads(path.read_text()); manifest = "Assets/QuestOriginalCampaign/native-sprites.json"
                if case == "before": data["beforeManifestSha256"][manifest] = "0" * 64
                elif case == "after": data["manifestSha256"][manifest] = "0" * 64
                elif case == "missing-before": del data["beforeManifestSha256"][manifest]
                elif case == "missing-after": del data["manifestSha256"][manifest]
                elif case == "wrong-target": data["target"] = "startup"
                elif case == "wrong-schema": data["schema"] = 2
                elif case == "content": data["assetContentChanged"] = True
                elif case == "references": data["serializedReferencesChanged"] = True
                elif case == "keys": data["addressableKeysChanged"] = True
                elif case == "missing-flag": del data["addressableKeysChanged"]
                elif case == "nested-map": data["beforeManifestSha256"][manifest] = {"sha": data["beforeManifestSha256"][manifest]}
                text = json.dumps(data)
                if case == "duplicate-field": text = text.replace('"schema": 1', '"schema": 1, "schema": 1')
                elif case == "duplicate-map": text = text[:-1] + ', "manifestSha256": ' + json.dumps(data["manifestSha256"]) + '}'
                elif case == "duplicate-map-key": text = text.replace('"beforeManifestSha256": {', '"beforeManifestSha256": {"' + manifest + '": "' + data["beforeManifestSha256"][manifest] + '", ')
                elif case == "truncated": text = text[:-1]
                elif case == "trailing": text += " invalid"
                elif case == "malformed-number": text = text.replace('"schema": 1', '"schema": 01')
                path.write_text(text)
                sprite = project / "Assets/Sprite/LoadingBase_0.asset"
                if case == "changed-manifest": (project / manifest).write_text((project / manifest).read_text() + " ")
                elif case == "missing-import": (project / "QuestStartupEvidence/loading-sprite-import.json").unlink()
                elif case == "changed-meta": Path(str(sprite) + ".meta").write_text("guid: " + "b" * 32 + "\n")
                elif case == "changed-geometry": sprite.write_bytes(sprite.read_bytes().replace(b"width: 128", b"width: 129", 1))
                elif case == "mixed-newlines": sprite.write_bytes(sprite.read_bytes().replace(b"%YAML 1.1\n", b"%YAML 1.1\r\n"))
                result = self.run_validate(project)
                self.assertIsNotNone(result["error"], case); self.assertEqual(result["imports"], [], case)


def _case(name, expected):
    def test(self): self.check_case(name, expected)
    return test


for _name in ("lf-current", "crlf-current", "overlay-lf-current", "overlay-crlf-current"):
    setattr(OldWindowsLoadingTransport, "test_accepts_exact_" + _name.replace("-", "_"), _case(_name, True))
for _name in ("geometry-change", "unrelated-bytes", "mixed-newlines", "bare-carriage", "unrelated-sprite", "packed-loading", "wrong-guid", "wrong-path-id", "wrong-collection", "unrestored-native", "wrong-container", "packed-proof", "unpreserved-proof", "wrong-source-hash", "wrong-meta", "wrong-manifest", "duplicate-source", "duplicate-import", "unverified-geometry", "failed-import", "wrong-source-receipt", "missing-receipt", "changed-manifest", "changed-meta"):
    setattr(OldWindowsLoadingTransport, "test_rejects_" + _name.replace("-", "_"), _case(_name, False))


if __name__ == "__main__": unittest.main()
