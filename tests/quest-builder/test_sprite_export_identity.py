"""Exercise production promotion identity gates without another game import.

Compile the full validators against the pinned Editor SDK. Execute the exact
identity/geometry methods with controlled AssetDatabase responses under Mono;
this verifies exporter independence and rejection gates, not headset rendering.
"""
import hashlib
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from test_package_editor_contract import declaration

ROOT = Path(__file__).resolve().parents[2]
EDITOR = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = DATA / "MonoBleedingEdge/bin/mono"
MCS = DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"

SHIM = r'''
namespace UnityEngine {
 public struct Rect { public float x,y,width,height; }
 public struct Vector2 { public float x,y; }
 public struct Vector4 { public float x,y,z,w; }
 public class Texture2D {}
 public class Sprite {
  public Texture2D texture = new Texture2D();
  public Rect rect = new Rect { width=480,height=234 };
  public Rect textureRect = new Rect { width=480,height=234 };
  public Vector2 pivot;
 }
}
namespace UnityEditor {
 public static class AssetDatabase {
  public static string Guid,Mode;
  public static string AssetPathToGUID(string p) { return p.EndsWith(".png") ? new string('c',32) : Guid; }
  public static T LoadAssetAtPath<T>(string p) where T : class {
   if(Mode=="missing-sprite")return null;
   var s=new UnityEngine.Sprite();
   if(Mode=="missing-texture")s.texture=null;
   if(Mode=="empty-crop")s.textureRect=new UnityEngine.Rect();
   return s as T;
  }
  public static string GetAssetPath(object o) { return Mode=="missing-path" ? "" : "Assets/Texture/promo.png"; }
 }
}
namespace UnityEngine.Sprites {
 public static class DataUtility {
  public static UnityEngine.Vector4 GetOuterUV(UnityEngine.Sprite s) {
   float end=UnityEditor.AssetDatabase.Mode=="invalid-uv"?float.NaN:1;
   return new UnityEngine.Vector4 {z=end,w=1};
  }
  public static UnityEngine.Vector4 GetPadding(UnityEngine.Sprite s) { return new UnityEngine.Vector4(); }
 }
}
'''

PROGRAM = r'''
class Program {
 static int Main(string[] args) {
  UnityEditor.AssetDatabase.Guid=args[1]; UnityEditor.AssetDatabase.Mode=args[2];
  try {
   var method=typeof(GloomhavenVR.Quest.Editor.QuestSpriteGeometryValidation).GetMethod(
    args[3]=="promotion"?"ReadPromotion":"Read",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
   object[] values=args[3]=="promotion"?new object[]{args[0]}:new object[]{args[0],args[4]};
   var result=(GloomhavenVR.Quest.Editor.QuestSpriteGeometryValidation.ImportedSprite)method.Invoke(null,values);
   System.Console.WriteLine(result.guid+" "+result.sourceSha256+" "+result.metaSha256); return 0;
  } catch(System.Exception e) {
   System.Console.WriteLine((e.InnerException??e).Message);return 2;
  }
 }
}
'''


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity compiler unavailable")
class SpriteExportIdentityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = tempfile.TemporaryDirectory(prefix="quest-sprite-guid-")
        cls.base = Path(cls.folder.name)
        source = (EDITOR / "QuestSpriteGeometryValidation.cs").read_text()
        methods = [declaration(source, marker) for marker in (
            "[Serializable] public sealed class ImportedSprite", "private static ImportedSprite ReadPromotion",
            "private static ImportedSprite Read(string", "private static string Hash(string")]
        # This expression-bodied method has no brace for declaration().
        finite = source[source.index("private static bool Finite"):source.index("private static void Close")]
        production = ("using System;using System.IO;using System.Security.Cryptography;using System.Text.RegularExpressions;"
                      "using UnityEditor;using UnityEngine;using UnityEngine.Sprites;"
                      "namespace GloomhavenVR.Quest.Editor {public static class QuestSpriteGeometryValidation {" +
                      "\n".join(methods) + finite + "}}")
        witness = cls.base / "Witness.cs"
        witness.write_text(production + SHIM + PROGRAM)
        cls.exe = cls.base / "Witness.exe"
        result = subprocess.run([str(MONO), str(MCS), "-out:" + str(cls.exe), str(witness)],
                                capture_output=True, text=True)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def invoke(self, metadata, *, actual="a" * 32, mode="ok", method="promotion", expected="a" * 32):
        asset = self.base / "DLC_Promo_JawsOfTheLion.asset"
        asset.write_bytes(b"original owned drawing state")
        meta = Path(str(asset) + ".meta")
        if metadata is None:
            meta.unlink(missing_ok=True)
        else:
            meta.write_bytes(metadata)
        before = asset.read_bytes(), meta.read_bytes() if meta.exists() else None
        result = subprocess.run([str(MONO), str(self.exe), str(asset), actual, mode, method, expected],
                                capture_output=True, text=True)
        self.assertEqual(before, (asset.read_bytes(), meta.read_bytes() if meta.exists() else None))
        return result.returncode, result.stdout

    def test_independent_export_guids_and_crlf_are_valid_without_mutation(self):
        for guid in ("a" * 32, "0123456789abcdef0123456789abcdef"):
            for newline in ("\n", "\r\n"):
                metadata = ("fileFormatVersion: 2" + newline + "guid: " + guid + newline + "NativeFormatImporter:" + newline).encode()
                with self.subTest(guid=guid, newline=repr(newline)):
                    code, output = self.invoke(metadata, actual=guid)
                    self.assertEqual(code, 0, output)
                    self.assertIn(guid, output)
                    self.assertIn(hashlib.sha256(metadata).hexdigest(), output)

    def test_export_metadata_must_have_one_bounded_nonzero_identity(self):
        for metadata in (None, b"", b"guid: 123\n", b"guid: " + b"0" * 32 + b"\n",
                         b"guid: " + b"A" * 32 + b"\n", b"guid: " + b"a" * 32 + b"\nguid: " + b"b" * 32,
                         b"guid: " + b"a" * 32 + b"\n" + b" " * 16384):
            with self.subTest(metadata=metadata[:60] if metadata else metadata):
                code, output = self.invoke(metadata)
                self.assertEqual(code, 2, output)
                self.assertIn("promotion sprite metadata", output)

    def test_matching_import_texture_uv_and_crop_gates_remain_required(self):
        metadata = b"guid: " + b"a" * 32 + b"\n"
        for mode, message in (("missing-sprite", "failed to import"), ("missing-texture", "failed to import"),
                              ("invalid-uv", "outer UVs"), ("empty-crop", "texture crop"),
                              ("missing-path", "texture crop")):
            with self.subTest(mode=mode):
                code, output = self.invoke(metadata, mode=mode)
                self.assertEqual(code, 2, output)
                self.assertIn(message, output)
        code, output = self.invoke(metadata, actual="b" * 32)
        self.assertEqual(code, 2, output)
        self.assertIn("exported=" + "a" * 32 + "; imported=" + "b" * 32, output)

    def test_loading_receipt_pinned_identity_still_rejects_different_import(self):
        code, output = self.invoke(b"guid: " + b"b" * 32, actual="b" * 32, method="loading", expected="a" * 32)
        self.assertEqual(code, 2, output)
        self.assertIn("GUID does not match", output)

    def test_full_production_validators_compile_against_editor_sdk(self):
        counter = self.base / "Progress.cs"
        counter.write_text("namespace GloomhavenVR.Quest.Editor {public static class QuestWizardProgress {"
                           "public class Counter {public Counter(string p,string o,long t,string u,string d){}"
                           "public void Report(long c,string d){}public void Complete(string d){}}}}")
        refs = [DATA / "Managed/UnityEngine" / name for name in
                ("UnityEngine.CoreModule.dll", "UnityEngine.JSONSerializeModule.dll", "UnityEditor.CoreModule.dll")]
        result = subprocess.run([str(MONO), str(MCS), "-define:UNITY_EDITOR", "-target:library",
                                 "-out:" + str(self.base / "ActualValidators.dll"),
                                 *["-r:" + str(path) for path in refs], str(counter),
                                 str(EDITOR / "QuestSpriteGeometryValidation.cs"),
                                 str(EDITOR / "QuestCampaignSpriteValidation.cs")], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
