"""Fresh exporter guard order and exact legacy native-redirect compatibility."""
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-recovery"))
import bundle_recovery as bundles
from recover import RecoveryError


class LegacyRedirectTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.evidence = self.root / "Evidence"; (self.evidence / "QuestRecovery").mkdir(parents=True)
        self.path = self.evidence / "QuestRecovery/native-redirect-identities.jsonl"
        self.obj = {"collection": "resources.assets", "pathId": 17, "classId": 28, "className": "Texture2D", "fileId": 10300}
        self.row = {"guid": "0000000000000000f000000000000000", "path": "", "skippedCore": True,
                    "exportCollection": bundles.NONEXPORTABLE_REDIRECTS[0], "objects": [self.obj]}
        self.witness = {**self.obj, "guid": self.row["guid"], "exportCollection": self.row["exportCollection"], "type": 0}

    def tearDown(self): self.temp.cleanup()
    def write(self, rows=None): self.path.write_text("".join(json.dumps(row) + "\n" for row in rows or [self.witness]))

    def test_known_non_exportable_engine_redirect_keeps_native_evidence_without_core_asset(self):
        self.write(); before = self.path.read_bytes()
        self.assertEqual(bundles.exportable_identities([self.row], self.evidence), [])
        self.assertEqual(self.path.read_bytes(), before)
        # The existing native pointer consumer still gets its exact engine target.
        from native_targets import engine_redirects
        self.assertEqual(engine_redirects(self.path)[("resources.assets", 17)]["fileId"], 10300)

    def test_single_redirect_requires_its_exact_actual_exporter_type(self):
        self.row["exportCollection"] = self.witness["exportCollection"] = bundles.NONEXPORTABLE_REDIRECTS[1]
        self.write()
        self.assertEqual(bundles.exportable_identities([self.row], self.evidence), [])

    def test_type_name_alone_never_authorizes_an_unknown_original_object(self):
        with self.assertRaisesRegex(RecoveryError, "no exact native engine witness"):
            bundles.exportable_identities([self.row], self.evidence)

    def test_mismatched_pointer_source_id_class_or_guid_is_rejected(self):
        for name, value in (("pathId", 18), ("fileId", 10301), ("classId", 48), ("className", "Shader"),
                            ("guid", "0000000000000000e000000000000000"), ("collection", "another.assets"),
                            ("exportCollection", bundles.NONEXPORTABLE_REDIRECTS[1])):
            with self.subTest(name=name):
                bad = {**self.witness, name: value}; self.write([bad])
                with self.assertRaisesRegex(RecoveryError, "no exact native engine witness"):
                    bundles.exportable_identities([self.row], self.evidence)

    def test_non_engine_or_missing_reference_guid_is_not_an_exemption(self):
        for guid in ("b" * 32, "0000000deadbeef15deadf00d0000000"):
            self.row["guid"] = self.witness["guid"] = guid; self.write()
            with self.assertRaisesRegex(RecoveryError, "no exact native engine witness"):
                bundles.exportable_identities([self.row], self.evidence)

    def test_unknown_exportable_core_remains_a_blocking_bounded_error(self):
        self.row["exportCollection"] = "AssetRipper.Export.UnityProjects.Project.PrefabExportCollection"
        self.row["objects"] = [{**self.obj, "pathId": number} for number in range(8)]
        self.write()
        retained = bundles.exportable_identities([self.row], self.evidence)
        self.assertEqual(retained, [self.row])
        with self.assertRaises(RecoveryError) as caught:
            bundles.merge_export(self.root / "output", self.root / "incoming", retained, {})
        self.assertIn("Missing count=8", str(caught.exception))
        self.assertIn("resources.assets", str(caught.exception))
        self.assertEqual(str(caught.exception).count("Texture2D"), 3)
        self.assertFalse((self.root / "output").exists())

    def test_non_exportable_false_row_does_not_remove_a_normal_asset(self):
        self.write(); normal = {**self.row, "skippedCore": False, "path": "Assets/actual.mat"}
        self.assertEqual(bundles.exportable_identities([normal, self.row], self.evidence), [normal])

    def test_conflicting_native_witnesses_are_rejected(self):
        self.write([self.witness, {**self.witness, "fileId": 10400}])
        with self.assertRaisesRegex(RecoveryError, "conflicting redirects"):
            bundles.exportable_identities([self.row], self.evidence)

    def test_empty_original_object_list_is_not_accepted(self):
        self.row["objects"] = []; self.write()
        with self.assertRaisesRegex(RecoveryError, "no original object evidence"):
            bundles.exportable_identities([self.row], self.evidence)


class ExporterGuardExecution(unittest.TestCase):
    def test_actual_should_export_method_capture_guard_and_original_negative_control(self):
        dotnet = shutil.which("dotnet")
        if dotnet is None: self.skipTest("A .NET SDK is required for the actual C# guard proof.")
        text = (ROOT / "tools/quest-recovery/QuestExportIdentity.cs").read_text()
        start = text.index("    public static bool ShouldExport(")
        end = text.index("\n    private static void CaptureRedirects", start)
        method = text[start:end]
        self.assertIn("if (!collection.Exportable) return false;", method)
        stub = r'''
using System; using System.Linq; using System.Collections.Generic;
using AssetRipper.Assets; using AssetRipper.Assets.Collections;
namespace AssetRipper.Assets.Collections {
public class AssetCollection { public string Name { get; set; } = "resources.assets"; }
public class SerializedAssetCollection : AssetCollection { }
}
namespace AssetRipper.Assets {
public interface IUnityObjectBase { string ClassName { get; } AssetCollection Collection { get; } }
public class Original : IUnityObjectBase { public string ClassName => "Texture2D"; public AssetCollection Collection { get; set; } = new SerializedAssetCollection(); }
}
namespace AssetRipper.Export.UnityProjects {
public interface IExportContainer { }
public class ProjectAssetContainer : IExportContainer { public IExportCollection CurrentCollection { get; set; } = null!; }
public interface IExportCollection { bool Exportable { get; } IEnumerable<IUnityObjectBase> Assets { get; } Pointer CreateExportPointer(IExportContainer c, IUnityObjectBase a, bool local); }
public record Pointer(Guid GUID);
public class Collection : IExportCollection { public bool Exportable { get; set; } = true; public IEnumerable<IUnityObjectBase> Assets { get; set; } = new[]{new Original()}; public Pointer CreateExportPointer(IExportContainer c, IUnityObjectBase a, bool local) => new(Guid.Parse("00000000000000000000000000000001")); }
internal static class QuestExportIdentity {
public static int Captured, Recorded;
private static void CaptureRedirects(IExportContainer c) { Captured++; }
private static void Record(IExportContainer c, string guid, string path) { Recorded++; }
METHOD
}
}
namespace AssetRipper.Export.UnityProjects.Project { public class ManagerExportCollection : AssetRipper.Export.UnityProjects.Collection { } }
public static class Program {
static void Check(bool exportable, string name, bool expected, int expectedRecords) {
var collection = new AssetRipper.Export.UnityProjects.Collection { Exportable=exportable, Assets=new[]{new Original{Collection=new SerializedAssetCollection{Name=name}}} };
var container = new AssetRipper.Export.UnityProjects.ProjectAssetContainer{CurrentCollection=collection};
AssetRipper.Export.UnityProjects.QuestExportIdentity.Captured=0; AssetRipper.Export.UnityProjects.QuestExportIdentity.Recorded=0;
if (AssetRipper.Export.UnityProjects.QuestExportIdentity.ShouldExport(container)!=expected || AssetRipper.Export.UnityProjects.QuestExportIdentity.Captured!=1 || AssetRipper.Export.UnityProjects.QuestExportIdentity.Recorded!=expectedRecords) throw new Exception("Incorrect capture/exportability guard");
}
public static void Main() {
Environment.SetEnvironmentVariable("QUEST_EXPORT_BUNDLE_ONLY", "1");
Check(false, "resources.assets", false, 0); Check(false, "CAB-original", false, 0);
Check(true, "resources.assets", false, 1); Check(true, "CAB-original", true, 0);
Environment.SetEnvironmentVariable("QUEST_EXPORT_BUNDLE_ONLY", null); Check(true,"resources.assets",true,0); Check(false,"resources.assets",false,0);
Console.WriteLine("actual-ShouldExport:6-cases-passed");
}
}
'''
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources></PropertyGroup></Project>')
            env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")
            source = root / "Program.cs"; source.write_text(stub.replace("METHOD", method))
            build = subprocess.run([dotnet, "build", str(root / "Probe.csproj"), "--nologo", "-v", "quiet"], capture_output=True, text=True, env=env, timeout=90)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            dll = root / "bin/Debug/net8.0/Probe.dll"
            good = subprocess.run([dotnet, str(dll)], capture_output=True, text=True, env=env, timeout=10)
            self.assertEqual(good.returncode, 0, good.stdout + good.stderr)
            self.assertIn("6-cases-passed", good.stdout)
            # Compile the exact same method with only the new guard removed:
            # this reproduces the old bug, rather than checking text presence.
            source.write_text(stub.replace("METHOD", method.replace("        if (!collection.Exportable) return false;\n", "")))
            build = subprocess.run([dotnet, "build", str(root / "Probe.csproj"), "--no-restore", "--nologo", "-v", "quiet"], capture_output=True, text=True, env=env, timeout=90)
            self.assertEqual(build.returncode, 0, build.stdout + build.stderr)
            bad = subprocess.run([dotnet, str(dll)], capture_output=True, text=True, env=env, timeout=10)
            self.assertNotEqual(bad.returncode, 0)
            self.assertIn("Incorrect capture/exportability guard", bad.stderr)


if __name__ == "__main__": unittest.main()
