"""Exact packet/header qualification and retained sidecar identity; no game data."""
import base64
import hashlib
import json
from pathlib import Path
import sys
import struct
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import audio_banks as banks
import storage
from test_full_audio import bank, page


def ogg(fields, packets):
    header = b'\x01vorbis' + b'\0' * 4 + bytes([fields['m_Channels']]) + struct.pack('<I', fields['m_Frequency'])
    result = page([len(header)], header) + page([7], b'comment') + page([5], b'setup')
    for packet in packets: result += page([255] * (len(packet) // 255) + [len(packet) % 255], packet)
    return result


class AudioBankTests(unittest.TestCase):
    def test_plan_keeps_host_code_outside_owned_original_producer(self):
        rows = [{"path": name, "size": 44, "sha256": "a" * 64} for name in ("resources.assets", "resources.resource")]
        plan = banks.plan(ROOT, {"files": rows})
        self.assertEqual(plan, banks.plan(ROOT, {"files": rows[::-1]}))
        self.assertNotEqual(plan["producerKey"], banks.plan(ROOT, {"files": [{**row, "sha256": "b" * 64} for row in rows]})["producerKey"])
        self.assertFalse(any("builder.py" in row["path"] or "NetProtocol" in row["path"] for row in plan["recipe"]["sources"]))
        self.assertEqual(plan["recipe"]["unityModules"], banks.MODULES)
        with self.assertRaises(storage.BuildError): banks.plan(ROOT, {"files": rows[:1]})

    def test_corrected_container_keeps_original_vorbis_packets(self):
        clip = {"name": "OriginalHit", "channels": 4, "frequency": 48000, "duration": 400 / 48000}
        # The FSB's base bit says mono; its authored channel extension says4.
        raw, fields, packets = bank()
        corrected = ogg(fields, packets)
        self.assertEqual(banks.qualify_corrected(raw, corrected, clip), 400)
        for changed in (ogg({**fields, "m_Channels": 1}, packets),
                        ogg(fields, packets[:-1] + [b"changed original packet"])):
            with self.assertRaises(storage.BuildError): banks.qualify_corrected(raw, changed, clip)

    def test_durable_bank_stamps_avoid_rehashing_and_detect_changed_outputs(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); path = root / banks.BANK; path.write_bytes(b"owned immutable Android bank")
            row = storage.record_file(path, path.name); row["stamp"] = banks._stamp(path)
            storage.write_json(root / "completion.json", {"plan": {"source": "known"}, "files": [row], "manifest": {"schema": 1}})
            with patch.object(banks, "record_file", side_effect=AssertionError("A retained file stamp needs no rehash")):
                self.assertIsNotNone(banks._receipt(root, {"source": "known"}))
            path.write_bytes(b"changed same purpose bytes")
            self.assertIsNone(banks._receipt(root, {"source": "known"}))

    def test_sidecar_stage_retains_input_and_copies_only_owned_small_members(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); producer = root / "producer"; (producer / "banks").mkdir(parents=True)
            rows = []
            for name in (banks.BANK, banks.INDEX):
                path = producer / "banks" / name; path.write_bytes(name.encode()); rows.append(storage.record_file(path, name))
            manifest = {"schema": 1, "target": "Android", "producerKey": "a" * 64, "files": rows}
            banks.stage(root / "project", producer, manifest)
            values = banks.replacements(producer, manifest)
            self.assertEqual(json.loads(values[banks.METADATA]), manifest)
            self.assertEqual(set(values), {banks.METADATA, *(banks.PREFIX + row["path"] for row in rows)})
            for row in rows:
                target = root / "project/Assets/StreamingAssets/QuestAudioClips" / row["path"]
                self.assertEqual(target.read_bytes(), (producer / "banks" / row["path"]).read_bytes())
                self.assertNotEqual(target.stat().st_ino, (producer / "banks" / row["path"]).stat().st_ino)


if __name__ == "__main__": unittest.main()
