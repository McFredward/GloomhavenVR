"""Production reader/stager fixtures; original assemblies are never executed."""
import json
from pathlib import Path
import shutil
import struct
import tempfile
import unittest

import script_order as production
from storage import BuildError


def serialized(rows, *, tree=False, endian="<", version=22, unity="2021.3.5f1"):
    """Independent v22 fixture writer with explicit fields and table offsets."""
    def pack(form, *values):
        return struct.pack(endian + form, *values)
    def string(value):
        encoded = value.encode("utf-8")
        item = pack("i", len(encoded)) + encoded
        return item + bytes((-len(item)) % 4)
    objects = []
    for row in rows:
        payload = string(row.get("name", "Component")) + pack("i", row.get("order", 0)) + bytes(16)
        payload += string(row.get("name", "Component")) + string(row.get("namespace", "Game")) + string(row.get("assembly", "Game.dll"))
        objects.append(payload)
    metadata = unity.encode() + b"\0" + pack("i", 19) + bytes([int(tree)]) + pack("i", 1)
    metadata += pack("iBh", 115, 0, -1) + bytes(16)
    if tree:
        metadata += pack("ii", 1, 4) + bytes(32) + b"x\0y\0" + pack("ii", 1, 115)
    metadata += pack("i", len(rows))
    data = bytearray()
    info_positions = []
    for index, (row, payload) in enumerate(zip(rows, objects)):
        metadata += bytes((-(48 + len(metadata))) % 4)
        info_positions.append(48 + len(metadata))
        metadata += pack("qqIi", row.get("id", index + 1), len(data), len(payload), 0)
        data += payload
        data += bytes((-len(data)) % 8)
    # Script types, externals and referenced types are empty; trailing user info.
    metadata += pack("iii", 0, 0, 0) + b"\0"
    offset = (48 + len(metadata) + 15) // 16 * 16
    header = struct.pack(">IIIIB3sIqqq", 0, 0, version, 0, 0 if endian == "<" else 1, bytes(3),
                         len(metadata), offset + len(data), offset, 0)
    return header + metadata + bytes(offset - 48 - len(metadata)) + data, info_positions


class Case:
    def __init__(self, rows=None):
        self.temp = tempfile.TemporaryDirectory(prefix="quest-script-order-")
        self.root = Path(self.temp.name)
        self.project, self.game = self.root / "project", self.root / "game"
        # Runner supplies the selected source checkout separately from fixtures.
        self.source = Path(getattr(production, "SOURCE_ROOT", Path(production.__file__).resolve().parents[2]))
        self.rows = rows or [{"name": "Late", "order": 32001}, {"name": "Early", "order": -51},
                             {"name": "Default", "order": 0}]
        self.metadata = {"Game.dll": {"name": "Game", "types": [
            {"namespace": "Game", "name": name, "nested": False} for name in ("Late", "Early", "Default")]}}
        (self.game / "Managed").mkdir(parents=True)
        self.plugin = self.project / "Assets/Plugins/Game.dll"
        self.plugin.parent.mkdir(parents=True)
        (self.game / "Managed/Game.dll").write_bytes(b"owned-original-fixture")
        shutil.copyfile(self.game / "Managed/Game.dll", self.plugin)
        self.meta = Path(str(self.plugin) + ".meta")
        self.guid = "1234567890abcdef1234567890abcdef"
        self.meta.write_bytes(("fileFormatVersion: 2\nguid: " + self.guid + "\nPluginImporter:\n"
                              "  executionOrder: {}\n  userData: original-callback-contract\n"
                              "  platformData:\n  - second:\n      enabled: 1\n  - second:\n      enabled: 1\n").encode())
        self.bindings = self.project / "Assets/QuestOriginalStartup/script-bindings.json"
        self.bindings.parent.mkdir(parents=True)
        self.write_bindings([])
        self.bank = self.game / "globalgamemanagers.assets"
        self.write_bank()

    def write_bank(self):
        self.bank.write_bytes(serialized(self.rows)[0])

    def write_bindings(self, guids, bindings=None):
        self.bindings.write_text(json.dumps({"schema": 1, "disabledPluginGuids": guids, "bindings": bindings or []}))

    def reference(self, name="Early", guid=None, identifier=None):
        file_id = production._file_id_function(self.source)("Game", name) if identifier is None else identifier
        self.scene = self.project / "Assets/Scene.unity"
        self.scene.write_text("%YAML 1.1\n--- !u!114 &9\nMonoBehaviour:\n  m_Script: {fileID: " + str(file_id) +
                              ", guid: " + (guid or self.guid) + ", type: 3}\n  originalField: 42\n")

    def stage(self):
        return production.stage_script_orders(self.project, self.game, self.source, self.root / "cache", Path("unused"), metadata=self.metadata)

    def manifest(self):
        return json.loads((self.project / production.MANIFEST).read_text())

    def close(self):
        self.temp.cleanup()


class Orders(unittest.TestCase):
    def case(self, rows=None):
        case = Case(rows)
        self.addCleanup(case.close)
        return case

    def test_complete_exact_order_identity_and_zero(self):
        case = self.case(); case.reference()
        before_scene, original_dll = case.scene.read_bytes(), case.plugin.read_bytes()
        result = case.stage()
        entries = {v["fullName"]: v for v in case.manifest()["entries"]}
        self.assertEqual({k: v["executionOrder"] for k, v in entries.items()}, {"Game.Early": -51, "Game.Default": 0, "Game.Late": 32001})
        self.assertTrue(entries["Game.Early"]["referenced"])
        self.assertEqual(entries["Game.Early"]["sourcePathIds"], ["2"])
        self.assertEqual(entries["Game.Early"]["originalGuid"], case.guid)
        self.assertEqual(entries["Game.Early"]["originalFileId"], str(production._file_id_function(case.source)("Game", "Early")))
        self.assertEqual(before_scene, case.scene.read_bytes())
        self.assertEqual(original_dll, case.plugin.read_bytes())
        self.assertIn(b'"Game.Late": 32001', case.meta.read_bytes())
        self.assertIn(b'"Game.Default": 0', case.meta.read_bytes())
        self.assertEqual(result["sourceRecordCount"], 3)
        self.assertEqual(result["orderedRecordCount"], 2)
        self.assertEqual(result["manifest"]["sha256"], production.digest(case.project / production.MANIFEST))
        self.assertEqual(result["plugins"][0]["afterSha256"], production.digest(case.meta))

    def test_campaign_only_retains_exact_orders_pointers_and_editor_output_contract(self):
        case = self.case([{"name": "Early", "order": -51}, {"name": "Default", "order": 0},
                          {"assembly": "Cinemachine.dll", "name": "Brain", "order": 100}])
        case.reference()
        campaign = case.project / "Assets/QuestOriginalCampaign/script-bindings.json"
        campaign.parent.mkdir()
        case.bindings.replace(campaign)
        case.bindings = campaign
        before_scene, original_dll = case.scene.read_bytes(), case.plugin.read_bytes()
        result = case.stage()
        manifest = case.manifest()
        self.assertEqual([(e["fullName"], e["executionOrder"]) for e in manifest["entries"]],
                         [("Game.Default", 0), ("Game.Early", -51)])
        entry = next(e for e in manifest["entries"] if e["fullName"] == "Game.Early")
        self.assertEqual(entry["originalGuid"], case.guid)
        self.assertEqual(entry["originalFileId"], str(production._file_id_function(case.source)("Game", "Early")))
        self.assertTrue(entry["referenced"])
        self.assertEqual(manifest["excluded"][0]["reason"], "assembly-outside-campaign-closure")
        self.assertEqual(before_scene, case.scene.read_bytes())
        self.assertEqual(original_dll, case.plugin.read_bytes())
        self.assertIn(b'"Game.Default": 0', case.meta.read_bytes())
        self.assertFalse((case.project / "Assets/QuestOriginalStartup/script-bindings.json").exists())
        self.assertEqual(result["manifest"]["path"], production.MANIFEST)
        self.assertEqual(result["bindingManifest"]["path"], "Assets/QuestOriginalCampaign/script-bindings.json")
        self.assertEqual(result["bindingManifest"]["sha256"], production.digest(campaign))
        self.assertEqual(result["scope"], "original campaign closure; exact imported scripts verified after SDK remapping")

    def test_campaign_is_authoritative_over_stale_startup_sdk_exclusions(self):
        case = self.case()
        startup = case.bindings
        campaign = case.project / "Assets/QuestOriginalCampaign/script-bindings.json"
        campaign.parent.mkdir()
        case.bindings = campaign
        case.write_bindings([case.guid])
        case.meta.write_bytes(case.meta.read_bytes().replace(b"      enabled: 1", b"      enabled: 0"))
        result = case.stage()
        self.assertTrue(all(e["package"] for e in case.manifest()["entries"]))
        self.assertEqual(result["bindingManifest"]["sha256"], production.digest(campaign))
        self.assertTrue(startup.is_file())
        # A valid stale Startup declaration must never hide invalid Campaign
        # declarations or missing referenced SDK providers.
        before = case.meta.read_bytes()
        case.write_bindings([case.guid, case.guid])
        with self.assertRaisesRegex(BuildError, "invalid or duplicated"):
            case.stage()
        self.assertEqual(before, case.meta.read_bytes())
        outside = "a" * 32
        case.write_bindings([case.guid, outside], [{"oldGuid": outside}])
        with self.assertRaisesRegex(BuildError, "still referenced in the campaign closure"):
            case.stage()
        self.assertEqual(before, case.meta.read_bytes())

    def test_missing_or_invalid_campaign_manifest_never_falls_back_to_startup(self):
        case = self.case()
        folder = case.project / "Assets/QuestOriginalCampaign"
        folder.mkdir()
        before = case.meta.read_bytes()
        with self.assertRaisesRegex(BuildError, "declared SDK binding manifest"):
            case.stage()
        self.assertEqual(before, case.meta.read_bytes())
        (folder / "script-bindings.json").write_text("{broken")
        with self.assertRaisesRegex(BuildError, "declared SDK binding manifest"):
            case.stage()
        self.assertEqual(before, case.meta.read_bytes())

    def test_reader_tree_and_endian_variants(self):
        case = self.case()
        for tree in (False, True):
            for endian in ("<", ">"):
                with self.subTest(tree=tree, endian=endian):
                    case.bank.write_bytes(serialized(case.rows, tree=tree, endian=endian)[0])
                    got = production.read_script_orders(case.bank)
                    self.assertEqual([(v["name"], v["executionOrder"]) for v in got], [("Late", 32001), ("Early", -51), ("Default", 0)])

    def test_equal_duplicates_deduplicate_and_conflicts_fail(self):
        case = self.case([{"name": "Early", "order": -51}, {"name": "Early", "order": -51}]); case.stage()
        self.assertEqual(case.manifest()["entries"][0]["sourcePathIds"], ["1", "2"])
        case.rows[1]["order"] = 0; case.write_bank(); before = case.meta.read_bytes()
        with self.assertRaisesRegex(BuildError, "Conflicting"):case.stage()
        self.assertEqual(before, case.meta.read_bytes())

    def test_unknown_and_outside_closure_are_proven_unreferenced(self):
        case = self.case([{"name": "Early", "order": -51}, {"name": "OldEditorOnly", "order": 24000},
                          {"assembly": "Cinemachine.dll", "name": "Brain", "order": 100}])
        case.stage()
        omitted = case.manifest()["excluded"]
        self.assertEqual({e["reason"] for e in omitted}, {"assembly-outside-startup-closure", "no-original-top-level-type"})
        self.assertTrue(all(e["referenced"] is False and e["sourcePathIds"] for e in omitted))
        case.reference("OldEditorOnly")
        with self.assertRaisesRegex(BuildError, "Referenced original script"):case.stage()

    def test_nested_original_type_does_not_become_invented_top_level(self):
        case = self.case(); case.metadata["Game.dll"]["types"][0]["nested"] = True
        case.stage(); self.assertNotIn("Game.Late", [e["fullName"] for e in case.manifest()["entries"]])
        case.reference("Late")
        with self.assertRaisesRegex(BuildError, "Referenced original script"):case.stage()

    def test_static_utility_exclusion_preserves_original_order_and_proof(self):
        case = self.case(); item = case.metadata["Game.dll"]["types"][0]
        item.update(attributes=385, baseAssemblyName="mscorlib", baseNamespace="System", baseName="Object")
        case.stage(); manifest = case.manifest()
        self.assertNotIn("Game.Late", [v["fullName"] for v in manifest["entries"]])
        proof = next(v for v in manifest["excluded"] if v["fullName"] == "Game.Late")
        self.assertEqual(proof["reason"], "original-static-utility")
        self.assertEqual(proof["typeAttributes"], 385)
        self.assertEqual(proof["baseName"], "Object")
        self.assertEqual(proof["baseAssemblyName"], "mscorlib")
        self.assertEqual(proof["executionOrder"], 32001)
        self.assertEqual(proof["sourcePathIds"], ["1"])
        self.assertFalse(proof["referenced"])
        self.assertEqual(proof["pluginPath"], "Assets/Plugins/Game.dll")
        self.assertEqual(proof["originalAssemblySha256"], production.digest(case.plugin))
        self.assertIn(b'"Game.Late": 32001', case.meta.read_bytes())
        case.reference("Late"); before = case.meta.read_bytes()
        with self.assertRaisesRegex(BuildError, "static original utility"):case.stage()
        self.assertEqual(before, case.meta.read_bytes())

    def test_static_flags_do_not_exclude_components_or_unknown_bases(self):
        for flags, namespace, name, assembly in ((128, "UnityEngine", "MonoBehaviour", "UnityEngine.CoreModule"),
                                               (256, "UnityEngine", "MonoBehaviour", "UnityEngine.CoreModule"),
                                               (385, "UnityEngine", "MonoBehaviour", "UnityEngine.CoreModule"),
                                               (385, "System", "Object", "UnknownAssembly"),
                                               (417, "System", "Object", "mscorlib"),
                                               ("385", "System", "Object", "mscorlib")):
            case = self.case(); case.metadata["Game.dll"]["types"][0].update(attributes=flags,
                baseNamespace=namespace, baseName=name, baseAssemblyName=assembly)
            case.reference("Late"); case.stage()
            self.assertIn("Game.Late", [v["fullName"] for v in case.manifest()["entries"]])

    def test_unknown_pointer_and_duplicate_type_metadata_fail_before_mutation(self):
        case = self.case(); case.reference(identifier=987654321); before = case.meta.read_bytes()
        with self.assertRaisesRegex(BuildError, "absent from the original"):case.stage()
        self.assertEqual(before, case.meta.read_bytes())
        case.reference(); case.metadata["Game.dll"]["types"].append(case.metadata["Game.dll"]["types"][1].copy())
        with self.assertRaisesRegex(BuildError, "Ambiguous original type metadata"):case.stage()
        self.assertEqual(before, case.meta.read_bytes())

    def test_package_identity_declared_not_platform_disabled(self):
        case = self.case(); case.meta.write_bytes(case.meta.read_bytes().replace(b"      enabled: 1", b"      enabled: 0", 1))
        case.stage(); self.assertTrue(all(not v["package"] for v in case.manifest()["entries"]))
        case.meta.write_bytes(case.meta.read_bytes().replace(b"      enabled: 1", b"      enabled: 0"))
        with self.assertRaisesRegex(BuildError, "availability"):case.stage()
        case.write_bindings([case.guid]); case.stage(); self.assertTrue(all(v["package"] for v in case.manifest()["entries"]))
        case.meta.write_bytes(case.meta.read_bytes().replace(b"      enabled: 0", b"      enabled: 1", 1))
        with self.assertRaisesRegex(BuildError, "availability"):case.stage()

    def test_declared_sdk_guid_outside_closure_is_only_unused(self):
        case = self.case(); outside = "a" * 32; case.write_bindings([outside]); result = case.stage()
        self.assertEqual(result["unusedDisabledPluginGuids"], [outside])
        case.reference(guid=outside)
        with self.assertRaisesRegex(BuildError, "missing but still referenced"):case.stage()
        case.scene.unlink(); case.write_bindings([outside], [{"oldGuid": outside}])
        with self.assertRaisesRegex(BuildError, "missing but still referenced"):case.stage()

    def test_plugin_source_duplicate_and_meta_ambiguity_fail(self):
        for mutate in (lambda c:c.plugin.write_bytes(b"modified"),
                       lambda c:c.meta.write_bytes(c.meta.read_bytes().replace(b"  executionOrder: {}", b"  executionOrder: {}\n  executionOrder: {}")),
                       lambda c:c.meta.write_bytes(c.meta.read_bytes().replace(b"guid:", b"missing:")),
                       lambda c:c.write_bindings([c.guid, c.guid])):
            case = self.case(); mutate(case)
            with self.assertRaises(BuildError):case.stage()
        case = self.case(); duplicate = case.project / "Assets/Elsewhere/Game.dll"; duplicate.parent.mkdir()
        shutil.copyfile(case.plugin, duplicate); shutil.copyfile(case.meta, str(duplicate) + ".meta")
        with self.assertRaisesRegex(BuildError, "duplicate"):case.stage()

    def test_meta_preserves_every_other_byte_and_crlf(self):
        case = self.case()
        before = case.meta.read_bytes().replace(b"\n", b"\r\n"); case.meta.write_bytes(before); case.stage()
        after = case.meta.read_bytes()
        prefix, tail = before.split(b"  executionOrder: {}\r\n")
        self.assertTrue(after.startswith(prefix) and after.endswith(tail))
        self.assertNotIn(b"\n", after.replace(b"\r\n", b""))
        self.assertIn(b'    "Game.Early": -51\r\n', after)
        case.stage(); self.assertEqual(after, case.meta.read_bytes())

    def test_reader_rejects_corrupt_untrusted_format(self):
        case = self.case(); original, offsets = serialized(case.rows)
        for name, position, replacement in (
            ("version", 8, struct.pack(">I", 21)),
            ("file-size", 24, struct.pack(">q", len(original) + 1)),
            ("data-offset", 32, struct.pack(">q", len(original) + 1)),
            ("endianness", 16, b"\x02"),
            ("object-path-duplicate", offsets[1], struct.pack("<q", 1)),
            ("object-start", offsets[0] + 8, struct.pack("<q", -1)),
            ("object-size", offsets[0] + 16, struct.pack("<I", 0xFFFFFFFF)),
            ("type-index", offsets[0] + 20, struct.pack("<i", 7)),
            ("object-overlap", offsets[1] + 8, struct.pack("<q", 0))):
            with self.subTest(name=name):
                bad = bytearray(original); bad[position:position + len(replacement)] = replacement; case.bank.write_bytes(bad)
                with self.assertRaises(BuildError):production.read_script_orders(case.bank)
        for rows in ([{"assembly": "../Game.dll"}], [{"name": ""}], [{"namespace": "bad\0namespace"}]):
            case.bank.write_bytes(serialized(rows)[0])
            with self.assertRaises(BuildError):production.read_script_orders(case.bank)
        for version in ("2022.1.0f1", "2021.3.6f1"):
            case.bank.write_bytes(serialized(case.rows, unity=version)[0])
            with self.assertRaisesRegex(BuildError, "version"):production.read_script_orders(case.bank)
        case.bank.write_bytes(original[:-1])
        with self.assertRaises(BuildError):production.read_script_orders(case.bank)


if __name__ == "__main__":
    unittest.main()
