"""An incompatible update reuses the ordinary owned workspace and cache owners."""
import copy
import json
from pathlib import Path
import shutil
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import builder
import import_workspace
import storage


class UpdateRebuildTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(); self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.repo, self.game, self.output = (self.root / name for name in ("source", "owned-game", "work"))
        for path in (self.repo, self.game, self.output): path.mkdir()
        (self.repo / "current-mod.cs").write_text("current source")
        (self.game / "original.asset").write_bytes(b"owned original asset")
        mod_rows, game_rows = storage.inventory(self.repo), storage.inventory(self.game)
        self.inputs = {"target": "game", "inputKey": "c" * 64,
            "game": {"key": storage.value_hash({"files": game_rows}), "files": game_rows, "unityVersion": "2021.3.5f1"},
            "mod": {"key": storage.value_hash({"files": mod_rows}), "files": mod_rows, "modBuild": 671},
            "profile": {"displayName": "Current owner", "dlcOwnership": {"ownedMask": 1}}, "profileKey": "d" * 64}
        self.plan = {"mode": "update-mod", "updateStrategy": "cached-full-build", "updateKey": "e" * 64,
                     "inputs": copy.deepcopy(self.inputs), "rebuildReasons": ["Quest shader changed"]}
        self.args = SimpleNamespace(command="update-mod", target="game", game_root=self.game,
                                    probe_assets=None, startup_project=None)

    def test_retained_owned_snapshot_and_unity_library_used_for_current_mod(self):
        # Exercise real immutable snapshot reuse, with a genuine unchanged
        # imported project on the original game's stable workspace identity.
        game_snapshot = self.output / "inputs/game" / self.inputs["game"]["key"]
        storage.snapshot(self.game, self.inputs["game"]["files"], game_snapshot)
        original = game_snapshot / "original.asset"
        stamp = original.stat()
        project = self.output / "projects" / import_workspace.workspace_key(self.inputs, "game")
        library = project / "Library/imported-original.cache"; library.parent.mkdir(parents=True)
        library.write_bytes(b"retained Unity import")
        library_stamp = library.stat()
        apk = self.output / "builds/current/GloomhavenVR-Quest.apk"
        observed = []
        def snapshots(inputs, output, repo, data, *_):
            source = output / "inputs/mod" / inputs["mod"]["key"]
            storage.snapshot(repo, inputs["mod"]["files"], source)
            storage.snapshot(data, inputs["game"]["files"], game_snapshot)
            return source, game_snapshot
        def prepare(args, inputs, output, source, game):
            self.assertEqual(args.command, "build")
            self.assertEqual(output, self.output); self.assertEqual(game, game_snapshot)
            self.assertEqual((source / "current-mod.cs").read_text(), "current source")
            self.assertEqual(library.read_bytes(), b"retained Unity import")
            observed.append("prepare"); return project
        def build(args, inputs, output, source, game, selected_project):
            self.assertEqual(selected_project, project)
            self.assertEqual(inputs["mod"]["modBuild"], 671)
            apk.parent.mkdir(parents=True); apk.write_bytes(b"controlled qualified builder output")
            storage.write_json(output / "latest-build.json", {"apk": apk.relative_to(output).as_posix()})
            observed.append("build"); return apk
        with patch.object(builder, "game_data", return_value=self.game), \
             patch.object(builder, "inspect_inputs", return_value=self.inputs), \
             patch.object(builder, "snapshot_inputs", side_effect=snapshots), \
             patch.object(builder, "prepare", side_effect=prepare), patch.object(builder, "build", side_effect=build), \
             patch.object(builder.native_admission, "require_capacity"), \
             patch.object(shutil, "disk_usage", return_value=shutil._ntuple_diskusage(400 << 30, 0, 400 << 30)), \
             patch.object(builder, "output_lock", side_effect=AssertionError("Migration cannot reacquire the CLI lock")), \
             patch.object(builder, "main", side_effect=AssertionError("Migration cannot restart the CLI")):
            self.assertEqual(builder.rebuild_update(self.args, self.plan, self.repo, self.output), apk)
        self.assertEqual(observed, ["prepare", "build"])
        self.assertEqual(self.args.command, "update-mod")
        self.assertEqual((original.stat().st_ino, original.stat().st_mtime_ns), (stamp.st_ino, stamp.st_mtime_ns))
        self.assertEqual((library.stat().st_ino, library.stat().st_mtime_ns), (library_stamp.st_ino, library_stamp.st_mtime_ns))
        receipt = json.loads((self.output / "updates" / self.plan["updateKey"] / "rebuilt-player.json").read_text())
        self.assertEqual(receipt["modBuild"], 671); self.assertEqual(receipt["inputKey"], self.inputs["inputKey"])
        self.assertEqual(json.loads((self.output / "latest-build.json").read_text())["apk"], receipt["apk"])

    def test_changed_selection_stops_before_preparation_and_preserves_completed_files(self):
        retained = self.output / "retained.cache"; retained.write_bytes(b"completed original data")
        for field in ("mod", "profile", "profileKey", "unityVersion"):
            changed = copy.deepcopy(self.inputs)
            if field == "mod": changed["mod"]["modBuild"] += 1
            elif field == "unityVersion": changed["game"][field] = "different Unity"
            else: changed[field] = "different owner"
            with self.subTest(field=field), patch.object(builder, "game_data", return_value=self.game), \
                 patch.object(builder, "inspect_inputs", return_value=changed), \
                 patch.object(builder, "snapshot_inputs", side_effect=AssertionError("Unqualified migration cannot copy")), \
                 patch.object(builder, "prepare", side_effect=AssertionError("Unqualified migration cannot rebuild")):
                with self.assertRaises(storage.BuildError):
                    builder.rebuild_update(self.args, self.plan, self.repo, self.output)
                self.assertEqual(retained.read_bytes(), b"completed original data")

    def test_profile_only_or_unqualified_strategy_never_enters_full_builder(self):
        for change in ({"mode": "update-profile"}, {"updateStrategy": "compact-code"}):
            with self.subTest(change=change), patch.object(builder, "inspect_inputs", side_effect=AssertionError("No full inspection")):
                with self.assertRaisesRegex(storage.BuildError, "qualified mod update"):
                    builder.rebuild_update(self.args, {**self.plan, **change}, self.repo, self.output)


if __name__ == "__main__": unittest.main()
