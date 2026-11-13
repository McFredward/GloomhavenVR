"""Execution/negative controls for the signed procedural native-program inventory."""
import copy
import hashlib
from pathlib import Path
import struct
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import native_plugins as gate
from storage import BuildError


class NativeProgramContract(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.header = bytearray(64)
        self.header[:7] = b"\x7fELF\x02\x01\x01"
        struct.pack_into("<HHI", self.header, 16, 3, 183, 1)
        self.contract = {"schema": 1, "backend": "proton-arm64ec-fex", "files": []}
        for name in sorted(gate.PROTON_REQUIRED | {"libQuestApparance.so", "libopus_egpv.so"}):
            path = gate.PREFIX + name
            output = self.root / path
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_bytes(self.header)
            self.contract["files"].append({"path": path, "size": 64, "sha256": hashlib.sha256(self.header).hexdigest()})

    def tearDown(self):
        self.temp.cleanup()

    def test_staged_inventory_is_actual_and_backend_bound(self):
        self.assertEqual(gate.verify_staged(self.root, self.contract, backend="proton-arm64ec-fex"), self.contract["files"])
        with self.assertRaisesRegex(BuildError, "selected backend"):
            gate.verify_staged(self.root, self.contract, backend="box64-wine9")

    def test_every_core_program_is_required(self):
        for row in self.contract["files"]:
            document = copy.deepcopy(self.contract)
            document["files"].remove(row)
            with self.subTest(path=row["path"]), self.assertRaises(BuildError): gate.validate(document)

    def test_unknown_duplicate_nonlocal_and_invalid_records_are_rejected(self):
        for change in ({"path": gate.PREFIX + "../libevil.so"}, {"path": "/libevil.so"},
                       {"path": gate.PREFIX + "lib spaces.so"}, {"size": True}, {"size": 63}, {"sha256": "unknown"}):
            document = copy.deepcopy(self.contract)
            document["files"][0].update(change)
            with self.subTest(change=change), self.assertRaises(BuildError): gate.validate(document)
        document = copy.deepcopy(self.contract)
        document["files"].append(document["files"][0])
        with self.assertRaisesRegex(BuildError, "repeats"): gate.validate(document)

    def test_old_backend_program_is_not_silently_carried_into_proton(self):
        document = copy.deepcopy(self.contract)
        document["files"].append({**document["files"][0], "path": gate.PREFIX + "libquest_box64.so"})
        with self.assertRaisesRegex(BuildError, "old Box64"): gate.validate(document)

    def test_wrong_actual_bytes_architecture_and_missing_native_are_rejected(self):
        row = self.contract["files"][0]; path = self.root / row["path"]
        path.write_bytes(b"changed")
        with self.assertRaisesRegex(BuildError, "changed"): gate.verify_staged(self.root, self.contract)
        bad = bytearray(self.header); struct.pack_into("<H", bad, 18, 62)
        path.write_bytes(bad); row["sha256"] = hashlib.sha256(bad).hexdigest()
        with self.assertRaisesRegex(BuildError, "Android ARM64"): gate.verify_staged(self.root, self.contract)
        path.unlink()
        with self.assertRaisesRegex(BuildError, "changed"): gate.verify_staged(self.root, self.contract)


if __name__ == "__main__": unittest.main()
