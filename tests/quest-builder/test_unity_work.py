"""Metadata-only pre-Editor inventory and exact warm invocation witnesses."""
import importlib.util
from contextlib import closing
import json
import os
from pathlib import Path
import shutil
import sqlite3
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("tested_unity_work", ROOT / "tools/quest-builder/unity_work.py")
work = importlib.util.module_from_spec(spec); spec.loader.exec_module(work)


class Progress:
    def __init__(self): self.events = []
    def event(self, *args, **kwargs): self.events.append((args, kwargs))


class UnityWorkTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.root = Path(self.temp.name)
        self.project = self.root / "project with spaces"; self.project.mkdir()
        self.progress = Progress(); self.launch = self.root / "logs/package-import-launch-abc.log"
        self.native = self.root / "logs/package-import-abc.log"
        self.argv = ["/opt/Unity/Editor/Unity", "-projectPath", str(self.project), "-buildTarget", "Android", "-logFile", str(self.native)]
        self.asset("Assets/A.asset"); self.asset("Assets/Figures/A.asset"); self.asset("Assets/Scripts/Example.cs")

    def tearDown(self): self.temp.cleanup()

    def asset(self, relative, content=b"payload"):
        path = self.project / relative; path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content); path.with_name(path.name + ".meta").write_text("fileFormatVersion: 2\nguid: " + "a" * 32 + "\n")
        return path

    def plan(self, argv=None): return work.publish_plan(argv or self.argv, self.launch, self.progress)

    def witness(self, *paths):
        plan = self.plan(); coverage = work.Coverage(plan)
        for path in paths: coverage.observe(path)
        coverage.close(); return plan

    def test_exact_unique_asset_files_excludes_meta_folders_hidden_and_external(self):
        self.asset("Assets/.hidden.asset"); self.asset("Assets/.hidden/Foo.asset")
        self.asset("Assets/Ignore~.asset")  # Unity ignores trailing '~', not middle.
        self.asset("Assets/temporary.asset~")
        self.asset("Assets/Folder/file.asset"); (self.project / "Assets/Folder.meta").write_text("guid: " + "a" * 32)
        self.asset("Library/NotAnAsset.asset"); self.asset("ProjectSettings/Test.asset")
        plan = self.plan()
        self.assertEqual(plan["total"], 5)
        with closing(sqlite3.connect(plan["database"])) as connection:
            keys = {row[0] for row in connection.execute("SELECT path FROM assets")}
        self.assertNotIn("Assets/Folder", keys); self.assertNotIn("Library/NotAnAsset.asset", keys)
        self.assertEqual(len(keys), plan["total"])
        self.assertLess(work.sidecar(self.native).stat().st_size, work.MAX_CONTROL_BYTES)

    def test_only_embedded_and_exact_locked_package_versions_are_scoped(self):
        embedded = self.asset("Packages/com.example.embed/package.json", b'{"name":"com.example.embed"}')
        self.asset("Packages/com.example.embed/Example.cs")
        self.asset("Library/PackageCache/com.example.cached@1.2.3/Example.asset")
        self.asset("Library/PackageCache/com.example.cached@9.9.9/Old.asset")
        (embedded.parent.parent / "packages-lock.json").write_text(json.dumps({"dependencies": {
            "com.example.cached": {"version": "1.2.3"}, "com.example.missing": {"version": "2.0.0"}}}))
        plan = self.plan()
        self.assertEqual(plan["total"], 6)
        self.assertEqual(plan["scopes"], ["Assets", "Packages/com.example.cached", "Packages/com.example.embed"])
        coverage = work.Coverage(plan)
        self.assertEqual(coverage.observe("Packages/com.example.cached/Example.asset"), (1, 6))
        self.assertEqual(coverage.observe("Library/PackageCache/com.example.cached@9.9.9/Old.asset"), (1, 6)); coverage.close()

    def test_census_never_reads_asset_or_meta_payloads(self):
        original = Path.open
        def guarded(path, *args, **kwargs):
            if path.is_relative_to(self.project / "Assets"): raise AssertionError("Asset payload opened")
            return original(path, *args, **kwargs)
        with mock.patch.object(Path, "open", guarded): plan = self.plan()
        self.assertEqual(plan["total"], 3)

    def test_completed_unique_paths_survive_failed_process_and_warm_restart(self):
        first = self.witness("Assets/A.asset", "Assets/A.asset", "Assets/Figures/A.asset")
        work.finish_plan(first, success=False, progress=self.progress)
        second = self.plan()
        self.assertNotEqual(first["invocationId"], second["invocationId"])
        self.assertEqual((second["done"], second["total"]), (2, 3))
        self.assertEqual(self.progress.events[-1][1]["status"], "start")

    def test_changed_asset_or_its_metadata_loses_only_its_coverage(self):
        self.witness("Assets/A.asset", "Assets/Figures/A.asset")
        (self.project / "Assets/A.asset").write_bytes(b"changed")
        self.assertEqual(self.plan()["done"], 1)
        (self.project / "Assets/Figures/A.asset.meta").write_text("changed GUID")
        self.assertEqual(self.plan()["done"], 0)

    def test_changed_global_script_context_rejects_all_previous_coverage(self):
        self.witness("Assets/A.asset", "Assets/Figures/A.asset")
        (self.project / "Assets/Scripts/Example.cs").write_text("changed script")
        self.assertEqual(self.plan()["done"], 0)

    def test_changed_project_settings_rejects_previous_coverage(self):
        self.asset("ProjectSettings/ProjectSettings.asset")
        self.witness("Assets/A.asset")
        (self.project / "ProjectSettings/ProjectSettings.asset").write_bytes(b"Linear")
        self.assertEqual(self.plan()["done"], 0)

    def test_changed_target_rejects_previous_coverage(self):
        self.witness("Assets/A.asset")
        changed = list(self.argv); changed[changed.index("Android")] = "StandaloneWindows64"
        self.assertEqual(self.plan(changed)["done"], 0)

    def test_valueless_graphics_flags_ignore_unrelated_next_argument_and_layout(self):
        base = list(self.argv)
        for flag in ("-force-d3d11", "-force-vulkan", "-force-glcore", "-forceGLES", "-forceGLES30"):
            with self.subTest(flag=flag):
                self.argv = base + [flag, "-executeMethod", "Build.One"]
                self.witness("Assets/A.asset")
                other_next_argument = base + [flag, "-quit", "-executeMethod", "Build.Two"]
                self.assertEqual(self.plan(other_next_argument)["done"], 1)
                other_layout = [base[0], flag] + base[1:] + ["-quit", "-executeMethod", "Build.Three"]
                self.assertEqual(self.plan(other_layout)["done"], 1)

    def test_actual_graphics_backend_change_rejects_retained_coverage(self):
        self.argv += ["-force-vulkan"]
        self.witness("Assets/A.asset")
        changed = list(self.argv); changed[changed.index("-force-vulkan")] = "-force-glcore"
        self.assertEqual(self.plan(changed)["done"], 0)

    def test_replaced_library_or_moved_project_rejects_previous_coverage(self):
        self.witness("Assets/A.asset")
        library = self.project / "Library"; saved = self.root / "saved-library"
        shutil.copytree(library, saved); shutil.rmtree(library); shutil.copytree(saved, library)
        self.assertEqual(self.plan()["done"], 0)
        self.witness("Assets/A.asset")
        moved = self.root / "moved"; shutil.copytree(self.project, moved)
        changed = list(self.argv); changed[changed.index(str(self.project))] = str(moved)
        self.assertEqual(self.plan(changed)["done"], 0)

    def test_current_source_scope_removes_deleted_assets_and_adds_new_paths(self):
        self.witness("Assets/A.asset")
        (self.project / "Assets/A.asset").unlink(); self.asset("Assets/New.asset")
        plan = self.plan(); self.assertEqual((plan["done"], plan["total"]), (0, 3))

    def test_finished_process_never_invents_unobserved_asset_completion(self):
        plan = self.witness("Assets/A.asset")
        work.finish_plan(plan, success=True, progress=self.progress)
        self.assertEqual(self.progress.events[-1][0][:4], ("unity-work-invocation", 1, 1, "invocations"))
        self.assertEqual(self.plan()["done"], 1)

    def test_sidecar_requires_fresh_actual_log_write_not_stale_file(self):
        self.native.parent.mkdir(parents=True); self.native.write_text("old import")
        plan = self.plan()
        self.assertIsNone(work.load_plan(self.native))
        self.native.write_text("live import")
        self.assertEqual(work.load_plan(self.native)["invocationId"], plan["invocationId"])
        self.assertIsNone(work.load_plan(self.native, started=plan["createdAt"] + 1))

    def test_stale_invocation_cannot_open_a_new_plan_database(self):
        first = self.plan(); self.plan()
        with self.assertRaises(ValueError): work.Coverage(first)

    def test_unavailable_or_corrupt_optional_telemetry_never_aborts(self):
        with mock.patch.object(work, "_walk", side_effect=PermissionError("metadata unavailable")):
            self.assertIsNone(self.plan())
        plan = self.plan(); Path(plan["database"]).write_bytes(b"invalid database")
        self.assertIsNone(self.plan())

    def test_missing_project_version_only_symlinks_and_unresolved_packages_do_not_launch_work(self):
        self.assertIsNone(work.publish_plan(["Unity", "-version"], self.launch, self.progress))
        self.assertIsNone(work.publish_plan(["Unity"], self.launch, self.progress))
        if hasattr(os, "symlink"):
            path = self.project / "Assets/External.asset"; path.symlink_to(self.root / "external")
            self.assertEqual(self.plan()["total"], 3)


if __name__ == "__main__": unittest.main()
