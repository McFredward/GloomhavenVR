"""Measured single-pass reference audits retain actual native pointer semantics."""
import collections
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools/quest-recovery"))
import recover

GUID = "0123456789abcdef0123456789abcdef"
OTHER = "fedcba9876543210fedcba9876543210"


class ReferenceAuditProgressTests(unittest.TestCase):
    def test_single_read_resolves_later_metadata_and_counts_unknown_native_suffix(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); assets = root / "Assets"; assets.mkdir()
            (assets / "first.unknown").write_text("--- !u!1 &1\nRoot:\n  reference: {fileID: 1, guid: " + GUID + ", type: 2}\n")
            (assets / "later.unknown.meta").write_text("fileFormatVersion: 2\nguid: " + GUID + "\nImporter:\n  missing: {fileID: 1, guid: " + OTHER + ", type: 2}\n")
            (assets / "other.unknown.meta").write_text("fileFormatVersion: 2\nguid: " + GUID + "\n")
            (assets / "binary.unknown").write_bytes(b"binary guid: " + OTHER.encode() + b"\x00" * 1024)
            opened = collections.Counter(); original = Path.open; output = io.StringIO()

            def observe(path, *args, **kwargs):
                opened[path.name] += 1
                return original(path, *args, **kwargs)

            with patch.object(Path, "open", observe), patch.dict(os.environ, {"GHVRQ_WIZARD_PROGRESS": "1"}), patch("sys.stdout", output):
                result = recover.audit_asset_references(root)
            self.assertEqual(result["referenceCount"], 2)
            self.assertEqual(result["missing"], {OTHER: ["Assets/later.unknown.meta"]})
            self.assertEqual(result["duplicateGuidCount"], 1)
            self.assertEqual(set(opened.values()), {1})
            events = [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in output.getvalue().splitlines()]
            self.assertEqual((events[0]["phase"], events[0]["done"], events[0]["total"]), ("recovery-asset-references", 0, 4))
            self.assertEqual((events[-1]["status"], events[-1]["done"], events[-1]["total"]), ("complete", 4, 4))

    def test_large_document_reports_bytes_only_complete_after_actual_audit(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); assets = root / "Assets"; assets.mkdir(); output = io.StringIO()
            (assets / "large.asset").write_text("--- !u!1 &1\nRoot:\n  payload: " + "a" * (4 * 1024 * 1024) + "\n  reference: {fileID: 1, guid: " + GUID + ", type: 2}\n")
            with patch.dict(os.environ, {"GHVRQ_WIZARD_PROGRESS": "1"}), patch("sys.stdout", output):
                result = recover.audit_asset_references(root)
            self.assertEqual(result["referenceCount"], 1)
            events = [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in output.getvalue().splitlines()]
            rows = [row for row in events if row["phase"] == "recovery-asset-reference-file"]
            self.assertEqual(rows[0]["done"], 0)
            self.assertEqual(rows[-1]["done"], rows[-1]["total"])
            self.assertEqual(rows[-1]["status"], "complete")
            self.assertEqual(events[-1]["phase"], "recovery-asset-references")

    def test_invalid_document_does_not_publish_completed_audit(self):
        import yaml
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); assets = root / "Assets"; assets.mkdir(); output = io.StringIO()
            (assets / "bad.asset").write_text("--- !u!1 &1\nRoot:\n  wrong: {field:, other:, invalid:}\n  reference: {fileID: 1, guid: " + GUID + ", type: 2}\n")
            with patch.dict(os.environ, {"GHVRQ_WIZARD_PROGRESS": "1"}), patch("sys.stdout", output):
                with self.assertRaises(yaml.YAMLError): recover.audit_asset_references(root)
            events = [json.loads(line.removeprefix("GHVRQ_PROGRESS ")) for line in output.getvalue().splitlines() if line.startswith("GHVRQ_PROGRESS ")]
            self.assertEqual(events[-1]["status"], "failed")
            self.assertNotIn("complete", [row["status"] for row in events])
            self.assertIn("Asset reference audit failed: Assets/bad.asset ; ScannerError", output.getvalue())


class StreamingPointerParserTests(unittest.TestCase):
    def test_large_numeric_arrays_do_not_construct_unrelated_yaml_nodes(self):
        import yaml
        text = "--- !u!1 &1\nRoot:\n  vectors:\n" + "  - {x: 1, y: 2, z: 3}\n" * 5000
        text += "  reference: {fileID: 1, guid: " + GUID + ", type: 2}\n"
        with patch.object(yaml, "compose", side_effect=AssertionError("Normal native maps need no composed node graph")):
            tokens = list(recover.serialized_pointer_tokens(text))
        self.assertEqual(tokens, [(GUID, text.index(GUID), text.index(GUID) + 32)])

    def test_stream_preserves_original_node_traversal_order_and_scalar_exclusions(self):
        import yaml
        text = ("Root:\n  quote: '{fileID: 1, guid: " + GUID + ", type: 2}'\n  literal: |\n    {fileID: 1, guid: " + GUID + ", type: 2}\n"
                "  references:\n  - {fileID: 1, guid: " + GUID + ", type: 2}\n  - nested: {fileID: 2, guid: " + OTHER + ", type: 2}\n"
                "  notPointer: {fileID: 1, guid: " + OTHER + ", type: 2, extra: 3}\n")
        expected = recover._composed_pointer_tokens(text, text, 0, yaml.CSafeLoader)
        self.assertEqual(list(recover.serialized_pointer_tokens(text)), expected)
        self.assertEqual([row[0] for row in expected], [OTHER, GUID])

    def test_duplicate_keys_and_non_scalar_values_match_original_nodes(self):
        import yaml
        texts = ["Root:\n  pointer: {fileID: [1], fileID: 2, guid: " + GUID + ", type: 2}\n",
                 "Root:\n  pointer: {fileID: 1, guid: " + GUID + ", type: [2]}\n"]
        for text in texts:
            with self.subTest(text=text):
                self.assertEqual(list(recover.serialized_pointer_tokens(text)), recover._composed_pointer_tokens(text, text, 0, yaml.CSafeLoader))

    def test_alias_references_use_original_parser_semantics(self):
        import yaml
        text = "Root:\n  original: &pointer {fileID: 1, guid: " + GUID + ", type: 2}\n  copy: *pointer\n"
        self.assertEqual(list(recover.serialized_pointer_tokens(text)), recover._composed_pointer_tokens(text, text, 0, yaml.CSafeLoader))

    def test_complex_mapping_keys_use_original_parser_semantics(self):
        import yaml
        text = "Root:\n  ? [left, right]\n  : {fileID: 1, guid: " + GUID + ", type: 2}\n"
        self.assertEqual(list(recover.serialized_pointer_tokens(text)), recover._composed_pointer_tokens(text, text, 0, yaml.CSafeLoader))

    def test_quoted_guid_in_an_actual_pointer_remains_an_explicit_error(self):
        text = "Root:\n  first: {fileID: 1, guid: " + GUID + ", type: 2}\n  quoted: {fileID: 1, guid: '" + OTHER + "', type: 2}\n"
        with self.assertRaisesRegex(recover.RecoveryError, "quoted native serialized"):
            list(recover.serialized_pointer_tokens(text))


if __name__ == "__main__": unittest.main()
