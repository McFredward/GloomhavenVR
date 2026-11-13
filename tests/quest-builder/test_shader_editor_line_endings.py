"""Exercise the actual public material checker with native Windows text bytes.

The pinned Mono compiler runs production method declarations with controlled
AssetDatabase responses. This is source/import contract evidence, not a new
Unity game import, compiled graphics-bank proof, or headset picture check.
"""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

from test_package_editor_contract import declaration

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestCampaignShaderValidation.cs"
DATA = Path(os.environ.get("GHVR_QUEST_TEST_UNITY_DATA", "/home/claw/unity-2021.3.5/Editor/Data"))
MONO = DATA / "MonoBleedingEdge/bin/mono"
MCS = DATA / "MonoBleedingEdge/lib/mono/4.5/mcs.exe"

SUPPORT = r'''
namespace GloomhavenVR.Quest.Editor {
 public static class QuestWizardProgress {
  public sealed class Counter {
   public Counter(string a,string b,long c,string d,string e) {}
   public void Report(long done,string detail) {}
   public void Complete(string detail) {}
  }
 }
}
'''

SHIM = r'''
namespace UnityEngine {
 public class Shader {}
 public class Material { public Shader shader; }
}
namespace UnityEditor {
 public static class AssetDatabase {
  public static string Mode,Path,Guid=new string('a',32);
  public static UnityEngine.Shader Shader=new UnityEngine.Shader();
  public static string GUIDToAssetPath(string guid) {return Mode=="different-path" ? "Assets/Other.mat" : Path;}
  public static string AssetPathToGUID(string path) {return Mode=="different-guid" ? new string('b',32) : Guid;}
  public static T LoadAssetAtPath<T>(string path) where T : class {
   if(Mode=="missing-material")return null;
   return new UnityEngine.Material {shader=Mode=="different-shader" ? new UnityEngine.Shader() : Shader} as T;
  }
  public static object[] LoadAllAssetsAtPath(string path) {return new object[]{LoadAssetAtPath<UnityEngine.Material>(path)};}
  public static bool TryGetGUIDAndLocalFileIdentifier(object asset,out string guid,out long id) {
   guid=Mode=="different-builtin-guid" ? new string('b',32) : new string('c',32);
   id=Mode=="different-builtin-id" ? 3 : 2;return Mode!="missing-builtin-identity";
  }
 }
}
'''

PROGRAM = r'''
class Program {
 static int Main(string[] args) {
  UnityEditor.AssetDatabase.Path=args[0];UnityEditor.AssetDatabase.Mode=args[1];
  var row=new GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.OriginalMaterial {
   guid=new string('a',32),assetPath=args[0],shaderGuid=new string('c',32),shaderFileId=2,
   originalShaderNull=args[2]=="null",originalEngineBuiltinShader=args[2]=="builtin",
   nativeFontImporterSubObject=args[2]=="font"
  };
  var input=new GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.Manifest {
   materials=new[]{row}
  };
  var shaders=new System.Collections.Generic.Dictionary<string,UnityEngine.Shader> {
   {new string('c',32),UnityEditor.AssetDatabase.Shader}
  };
  try {
   GloomhavenVR.Quest.Editor.QuestCampaignShaderValidation.VerifyImportedMaterials(input,shaders);
   System.Console.WriteLine("PASS public original material checker");return 0;
  } catch(System.Exception error) {System.Console.WriteLine(error.Message);return 2;}
 }
}
'''


@unittest.skipUnless(MONO.is_file() and MCS.is_file(), "Pinned Unity Mono compiler unavailable")
class ShaderEditorLineEndingsTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.folder = tempfile.TemporaryDirectory(prefix="quest-material-lines-")
        cls.base = Path(cls.folder.name)
        text = SOURCE.read_text()
        methods = [declaration(text, marker) for marker in (
            "[Serializable] public sealed class OriginalMaterial", "public static void VerifyImportedMaterials",
            "private static string ExactAsset", "private static bool Guid(string")]
        cls.production = ("using System;using System.IO;using System.Linq;using System.Collections.Generic;"
                          "using System.Text.RegularExpressions;using UnityEngine;using UnityEditor;"
                          "namespace GloomhavenVR.Quest.Editor { public static class QuestCampaignShaderValidation {" +
                          "public sealed class Manifest {public OriginalMaterial[] materials;}" +
                          "\n".join(methods) + "}}")
        witness = cls.base / "Witness.cs"
        witness.write_text(cls.production + SUPPORT + SHIM + PROGRAM)
        cls.exe = cls.base / "Witness.exe"
        result = subprocess.run([str(MONO), str(MCS), "-out:" + str(cls.exe), str(witness)],
                                capture_output=True, text=True)
        if result.returncode:
            raise AssertionError(result.stdout + result.stderr)

    @classmethod
    def tearDownClass(cls):
        cls.folder.cleanup()

    def invoke(self, data, *, mode="ok", kind="null"):
        project = self.base / "project"
        asset = project / "Assets/Original.mat"
        asset.parent.mkdir(parents=True, exist_ok=True)
        asset.write_bytes(data)
        before = asset.read_bytes()
        result = subprocess.run([str(MONO), str(self.exe), "Assets/Original.mat", mode, kind],
                                cwd=project, capture_output=True, text=True)
        self.assertEqual(before, asset.read_bytes(), "The checker must not rewrite native material bytes")
        return result.returncode, result.stdout

    def test_lf_crlf_mixed_and_final_line_retain_exact_original_null_pointer(self):
        for data in (b"Material:\n  m_Shader: {fileID: 0}\n  m_Name: Original\n",
                     b"Material:\r\n  m_Shader: {fileID: 0}\r\n  m_Name: Original\r\n",
                     b"Material:\n  m_Shader: {fileID: 0}\r\n  m_Name: Original\n",
                     b"Material:\r\n  m_Shader: {fileID: 0}",
                     b"Material:\n  m_Shader: {fileID: 0}\r"):
            with self.subTest(data=data):
                code, output = self.invoke(data)
                self.assertEqual(code, 0, output)

    def test_nonzero_foreign_or_differently_shaped_pointers_remain_rejected(self):
        for line in ("  m_Shader: {fileID: 1}", "  m_Shader: {fileID: -1}",
                     "  m_Shader: {fileID: 0, guid: " + "b" * 32 + ", type: 3}",
                     "  m_Shader: {fileID: 0} junk", "  m_Shader: {fileID: 0} ",
                     "  m_Shader: {fileID: 00}", "  m_Shader: {fileID: 0}\rjunk",
                     "  m_Shader: {fileID: 0}\r\r", "   m_Shader: {fileID: 0}",
                     "  Other: {fileID: 0}"):
            for newline in ("\n", "\r\n"):
                with self.subTest(line=line, newline=repr(newline)):
                    code, output = self.invoke(("Material:" + newline + line + newline).encode())
                    self.assertEqual(code, 2, output)
                    self.assertIn("Original null shader PPtr has changed", output)

    def test_original_imported_material_and_guid_path_association_remain_required(self):
        for mode, message in (("missing-material", "material is missing"),
                              ("different-guid", "GUID/path association"),
                              ("different-path", "GUID/path association")):
            with self.subTest(mode=mode):
                code, output = self.invoke(b"  m_Shader: {fileID: 0}\r\n", mode=mode)
                self.assertEqual(code, 2, output)
                self.assertIn(message, output)

    def test_original_bound_shader_font_and_builtin_identity_branches_remain_required(self):
        for kind in ("bound", "font", "builtin"):
            code, output = self.invoke(b"retained original material bytes", kind=kind)
            self.assertEqual(code, 0, output)
        for kind, mode, message in (("bound", "different-shader", "exact original shader"),
                                    ("font", "different-shader", "exact original shader"),
                                    ("builtin", "different-builtin-guid", "engine shader PPtr"),
                                    ("builtin", "different-builtin-id", "engine shader PPtr"),
                                    ("builtin", "missing-builtin-identity", "engine shader PPtr")):
            with self.subTest(kind=kind, mode=mode):
                code, output = self.invoke(b"retained original material bytes", kind=kind, mode=mode)
                self.assertEqual(code, 2, output)
                self.assertIn(message, output)

    def test_actual_public_method_and_identity_dependency_compile_against_pinned_editor_sdk(self):
        source = self.base / "Sdk.cs"
        source.write_text(self.production + SUPPORT)
        references = [DATA / "Managed/UnityEngine" / name for name in
                      ("UnityEngine.CoreModule.dll", "UnityEditor.CoreModule.dll")]
        if not all(path.is_file() for path in references):
            self.skipTest("Pinned Unity Editor SDK unavailable")
        result = subprocess.run([str(MONO), str(MCS), "-target:library", "-out:" + str(self.base / "Sdk.dll"),
                                 *["-r:" + str(path) for path in references], str(source)],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)


if __name__ == "__main__":
    unittest.main()
