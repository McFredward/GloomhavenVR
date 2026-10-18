"""Closed pinned-tool outputs survive new sessions without archive replay."""
from copy import deepcopy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import provision
import state
import tool_reuse


class ToolReuseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.store = state.Store(self.root / "owned")
        self.first = self.store.create({"mode": "build"})
        self.key = state.value_hash({"fixture": "real closed tool provisioning"})
        self.lock = deepcopy(provision.LOCK)
        # Use the shipped Windows versions/executable shape with tiny pinned
        # archives. Real checksum, extractor, stage publisher and witnesses run;
        # external native version tools use a declared fixture output adapter.
        for name in tool_reuse.NAMES:
            spec = self.lock[name]
            archive = self.store.root / "tools/downloads" / (name + "-" + spec["version"] + ".zip")
            archive.parent.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as package:
                package.writestr(spec["executable"], (name + " fixture executable ").encode() * 5000)
                package.writestr("sdk/support.dll", (name + " original support payload ").encode() * 4000)
                package.writestr("sdk/manifest.json", b'{"fixture":"required support"}')
            spec["hash"] = hashlib.new(spec["algorithm"], archive.read_bytes()).hexdigest()
            spec["size"] = archive.stat().st_size
        self.selected = tool_reuse.recipe(lock=self.lock, host="windows-x64")
        class Versions:
            def run(inner, argv, log):
                name = log.name.removesuffix("-version.log")
                expected = "git version " + self.lock[name]["version"] if name == "git" else self.lock[name]["version"]
                log.parent.mkdir(parents=True, exist_ok=True)
                log.write_text(expected + "\n")
        with patch.object(provision, "host_key", return_value="windows-x64"), patch.object(provision, "spec_for", side_effect=lambda name: self.lock[name]):
            self.paths, self.details = provision.tools(self.store, self.first["session"], Versions())

    def publish(self):
        self.store.publish(self.first["session"], "tools", self.key, self.paths, self.details)
        self.assertTrue(tool_reuse.record(self.store, self.first["session"], self.key, self.selected))

    def pointer(self):
        return self.store.root / "tools/shared-receipts" / (state.value_hash(self.selected) + ".json")

    def test_warm_fresh_process_new_session_never_opens_tool_or_archive_payloads(self):
        self.publish()
        selection = self.root / "selection.json"
        state.atomic_json(selection, self.selected)
        script = """
from pathlib import Path
import json,sys
sys.path.insert(0,sys.argv[1])
import state,tool_reuse
selected=json.loads(Path(sys.argv[3]).read_text());store=state.Store(Path(sys.argv[2]))
tools=store.root/'tools';original=Path.open
def guarded(path,*args,**kwargs):
    if tools in path.parents and path.name!='wizard-tool.json' and 'shared-receipts' not in path.parts:
        raise AssertionError('Warm tool payload read: '+str(path))
    return original(path,*args,**kwargs)
Path.open=guarded
value=tool_reuse.lookup(store,selected)
assert value is not None
paths,details=value
new=store.create({'mode':'update-mod'})
key=state.value_hash({'different':'new session actual closed receipt'})
receipt=store.publish(new['session'],'tools',key,paths,details)
assert tool_reuse.record(store,new['session'],key,selected)
assert tool_reuse.lookup(store,selected) is not None
print(json.dumps({'outputs':len(paths),'key':key,'session':new['session'],'allSupportQualified':True}))
"""
        result = subprocess.run([sys.executable, "-I", "-B", "-c", script, str(ROOT / "tools/quest-wizard"),
                                 str(self.store.root), str(selection)], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        proof = json.loads(result.stdout.strip().splitlines()[-1])
        self.assertGreater(proof["outputs"], 10)
        self.assertNotEqual(proof["session"], self.first["session"])
        self.assertEqual(state.read_json(self.pointer())["session"], proof["session"])

    def test_no_pointer_is_published_before_actual_stage_publication(self):
        self.assertFalse(tool_reuse.record(self.store, self.first["session"], self.key, self.selected))
        self.assertFalse(self.pointer().exists())
        self.assertIsNone(tool_reuse.lookup(self.store, self.selected))

    def test_same_size_mtime_support_mutation_invalidates_whole_tool_reuse(self):
        self.publish()
        support = next(path for path in self.paths if path.name == "support.dll")
        before = support.stat()
        support.write_bytes(b"x" * before.st_size)
        os.utime(support, ns=(before.st_atime_ns, before.st_mtime_ns))
        self.assertIsNone(tool_reuse.lookup(state.Store(self.store.root), self.selected))
        self.assertEqual(support.stat().st_size, before.st_size)

    def test_missing_support_file_uses_normal_provision_instead_of_executable_marker(self):
        self.publish()
        support = next(path for path in self.paths if path.name == "support.dll")
        support.unlink()
        self.assertIsNone(tool_reuse.lookup(self.store, self.selected))
        self.assertTrue(Path(self.details["git"]).is_file())

    def test_changed_complete_output_roster_is_rejected_even_when_all_remaining_files_validate(self):
        self.publish()
        path = self.store.receipt(self.first["session"], "tools")
        value = state.read_json(path)
        value["outputs"] = [row for row in value["outputs"] if not row["path"].endswith("support.dll")]
        state.atomic_json(path, value)
        self.assertIsNotNone(self.store.valid(self.first["session"], "tools", self.key))
        self.assertIsNone(tool_reuse.lookup(self.store, self.selected))

    def test_pinned_recipe_and_host_or_tool_kind_changes_do_not_adopt_old_session(self):
        self.publish()
        changed = deepcopy(self.selected)
        changed["recipes"]["dotnet8"]["version"] = "different"
        self.assertIsNone(tool_reuse.lookup(self.store, changed))
        for host in ("linux-x64", "other"):
            self.assertIsNone(tool_reuse.lookup(self.store, tool_reuse.recipe(lock=self.lock, host=host)))
        self.assertIsNone(tool_reuse.lookup(self.store, tool_reuse.recipe("apk-signing", lock=self.lock, host="windows-x64")))

    def test_corrupt_or_oversized_pointer_is_a_cache_miss(self):
        self.publish()
        for data in (b"not JSON", b"{}", b"x" * (tool_reuse.MAX_POINTER_BYTES + 1)):
            self.pointer().write_bytes(data)
            self.assertIsNone(tool_reuse.lookup(self.store, self.selected))
        self.assertTrue(tool_reuse.record(self.store, self.first["session"], self.key, self.selected))
        self.assertIsNotNone(tool_reuse.lookup(self.store, self.selected))

    def test_wrong_session_key_or_recipe_pointer_never_reads_an_arbitrary_receipt(self):
        self.publish()
        original = state.read_json(self.pointer())
        for field, value in (("session", "../external"), ("stageKey", "0" * 64), ("recipeKey", "0" * 64)):
            altered = {**original, field: value}
            state.atomic_json(self.pointer(), altered)
            self.assertIsNone(tool_reuse.lookup(self.store, self.selected))

    def test_linked_shared_pointer_is_not_followed_or_overwritten(self):
        self.publish()
        external = self.root / "external.json"
        external.write_bytes(self.pointer().read_bytes())
        self.pointer().unlink()
        self.pointer().symlink_to(external)
        self.assertIsNone(tool_reuse.lookup(self.store, self.selected))
        self.assertFalse(tool_reuse.record(self.store, self.first["session"], self.key, self.selected))
        self.assertEqual(state.read_json(external)["session"], self.first["session"])

    def test_lookup_does_not_write_old_session_progress(self):
        self.publish()
        progress = self.store.session_dir(self.first["session"]) / "logs/progress.log"
        before = progress.read_bytes()
        with patch.object(self.store, "progress", side_effect=AssertionError("old session progress changed")):
            self.assertIsNotNone(tool_reuse.lookup(self.store, self.selected))
        self.assertEqual(progress.read_bytes(), before)


if __name__ == "__main__":
    unittest.main()
