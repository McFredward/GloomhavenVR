"""Default-sky source closure, bounded player reads and strict retained identity."""
import copy
import io
import json
import os
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest.mock import patch
import warnings
import zipfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path(os.environ.get("GHVRQ_SKY_SOURCE_ROOT", ROOT))
sys.path.insert(0, str(SOURCE / "tools/quest-builder"))
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
# A dev-based isolated worker has the earlier shared storage module. The selected
# immutable Quest source supplies its actual ABI; integrated runs use ROOT.
if SOURCE != ROOT:
    sys.path.insert(0, str(SOURCE / "tools/quest-builder"))
import sky_resources as resources


class SkyResources(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)

    def tearDown(self):
        self.temporary.cleanup()

    def source(self):
        source = self.root / "source"
        for row in resources.plan(SOURCE)["sources"]:
            target = source / row["path"]; target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(SOURCE / row["path"], target)
        return source

    def test_closed_recipe_and_row_only_compiler_snapshot(self):
        expected = resources.plan(SOURCE)
        self.assertEqual(expected["recipeKey"], resources.value_hash(expected["recipe"]))
        self.assertEqual(resources.plan(self.root / "absent", expected["sources"]), expected)
        for missing in (resources.EDITOR, resources.TEMPLATE, "tools/quest-builder/full_shaders.py",
                        "tools/quest-shaders/produce.py", "prebuilt/quest-converters-win64-v1.zip"):
            with self.subTest(missing=missing), self.assertRaises(resources.BuildError):
                resources.plan(self.root / "absent", [r for r in expected["sources"] if r["path"] != missing])
        with self.assertRaises(resources.BuildError):
            resources.plan(self.root / "absent", expected["sources"] + [expected["sources"][0]])

    def test_transitive_inputs_change_identity_but_runtime_and_generated_caches_do_not(self):
        source = self.source(); initial = resources.plan(source)
        for name in ("tools/quest-shaders/integer_bits.py", "tools/quest-shaders/vendor/smol-v/source-pin.json",
                     "tools/quest-builder/storage.py", resources.EDITOR, resources.TEMPLATE):
            path = source / name; before = path.read_bytes(); path.write_bytes(before + b"changed source input")
            self.assertNotEqual(resources.plan(source)["recipeKey"], initial["recipeKey"], name)
            path.write_bytes(before)
        runtime = source / "src/GloomhavenVR/Core/Environment/NativeDefaultSky.cs"
        runtime.parent.mkdir(parents=True, exist_ok=True); runtime.write_text("runtime pose only")
        generated = source / "tools/quest-shaders/__pycache__/generated.py"
        generated.parent.mkdir(); generated.write_text("generated")
        self.assertEqual(resources.plan(source), initial)

    def test_native_pin_drift_is_actionable(self):
        source = self.source(); path = source / resources.NATIVE_PROVENANCE
        value = json.loads(path.read_text()); value["sources"]["sharedassets8.assets"] = "0" * 64
        path.write_text(json.dumps(value))
        with self.assertRaisesRegex(resources.BuildError, "audit the new originals"):
            resources.plan(source)

    def test_split_reader_only_inflates_selected_chunks(self):
        raw = io.BytesIO()
        with zipfile.ZipFile(raw, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for i in range(6): archive.writestr("assets/bin/Data/native.split" + str(i), bytes([i]) * resources.SPLIT_SIZE)
        with zipfile.ZipFile(io.BytesIO(raw.getvalue())) as archive:
            reader = resources.PlayerFile(archive, "assets/bin/Data/native")
            self.assertEqual(reader.read(16), b"\0" * 16)
            reader.seek(5 * resources.SPLIT_SIZE + 20)
            self.assertEqual(reader.read(32), b"\5" * 32)
            self.assertEqual(reader.used, {0, 5})
            reader.seek(-12, 2); self.assertEqual(reader.read(), b"\5" * 12)
            self.assertRaises(resources.BuildError, reader.seek, -1)

    def test_split_reader_rejects_sparse_or_ambiguous_sources(self):
        cases = [(("native.split0", 1048576), ("native.split2", 5)),
                 (("native.split0", 10), ("native.split1", 5)),
                 (("native", 48), ("native.split0", 48)),
                 (("native", 48), ("native", 48))]
        for entries in cases:
            raw = io.BytesIO()
            with self.subTest(entries=entries):
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    with zipfile.ZipFile(raw, "w") as z:
                        for name, size in entries: z.writestr(name, b"x" * size)
                with zipfile.ZipFile(io.BytesIO(raw.getvalue())) as z, self.assertRaises(resources.BuildError):
                    resources.PlayerFile(z, "native")

    def test_native_external_namespace_is_exact(self):
        self.assertEqual(resources._member("Resources/unity_builtin_extra"), "assets/bin/Data/Resources/unity_builtin_extra")
        for name in ("../outside", "/absolute", "a\\b", "c:path", "a//b", "a/./b", "folder/"):
            with self.subTest(name=name), self.assertRaises(resources.BuildError): resources._member(name)

    def test_player_header_fails_before_unitypy_reads_an_unbounded_table(self):
        class Reader(io.BytesIO): size = 48
        for raw in (b"\0" * 48, struct.pack(">IIIIB3xIqqq", 0, 0, 22, 0, 0, 20000000, 48, 48, 0)):
            with self.subTest(raw=raw[:12]), self.assertRaises(resources.BuildError):
                resources._player_serialized(Reader(raw))

    def test_pc_decoder_preserves_all_mips_and_native_row_order(self):
        # Three authored BC1 levels carry red/green/blue separately. Regenerating
        # mips from red mip zero would fail this independent encoded witness.
        blocks = [struct.pack("<HHI", color, 0, 0) for color in (0xf800, 0x07e0, 0x001f)]
        asset = {"tree": {"m_Width": 4, "m_Height": 4, "m_MipCount": 3, "m_TextureFormat": 10,
                    "m_StreamData": {"offset": 0, "size": 24, "path": "native"}}, "stream": b"".join(blocks)}
        result = resources._decode_bc1_mips(asset)
        pixels = result["tree"]["image data"]
        self.assertEqual(pixels, b"\xff\0\0\xff" * 16 + b"\0\xff\0\xff" * 4 + b"\0\0\xff\xff")
        self.assertEqual(result["tree"]["m_TextureFormat"], 4)
        self.assertEqual(asset["tree"]["m_TextureFormat"], 10)
        self.assertFalse(result["stream"])

    def test_metadata_closes_both_keys_without_code_or_timestamp_fields(self):
        expected = resources.plan(SOURCE)
        basis = {"kind": "retained-android-player", "originalSources": resources.PINS,
            "assets": [{"role": r, "name": resources.NAMES[r], "type": resources.TYPES[r], "owner": "assets/bin/Data/native",
                        "pathId": resources.IDS[r], "objectSha256": "1" * 64, "streamSha256": "2" * 64} for r in sorted(resources.IDS)]}
        value = {"schema": 1, "scope": resources.SCOPE, "recipe": expected["recipe"], "recipeKey": expected["recipeKey"],
                 "basis": basis, "producerKey": resources.value_hash({"recipeKey": expected["recipeKey"], "basis": basis})}
        self.assertEqual(resources._metadata(json.dumps(value).encode(), expected), value)
        for field in ("scope", "producerKey", "recipeKey"):
            wrong = copy.deepcopy(value); wrong[field] = "changed"
            with self.subTest(field=field), self.assertRaises(resources.BuildError):
                resources._metadata(json.dumps(wrong).encode(), expected)
        wrong = copy.deepcopy(value); wrong["basis"]["kind"] = "unknown-source"
        wrong["producerKey"] = resources.value_hash({"recipeKey": expected["recipeKey"], "basis": wrong["basis"]})
        with self.assertRaisesRegex(resources.BuildError, "unknown native acquisition basis"):
            resources._metadata(json.dumps(wrong).encode(), expected)

    def test_warm_cache_precedes_pc_editor_requirements(self):
        expected = resources.plan(SOURCE); folder = self.root / resources.CACHE_ROOT / expected["recipeKey"] / ("1" * 64)
        folder.mkdir(parents=True)
        answer = {"bundle": folder / resources.BANK, "cacheRelativePath": folder.relative_to(self.root).as_posix(), "reused": True}
        with patch.object(resources, "_validate", return_value=answer) as validate, patch.object(resources, "_produce_pc") as cold:
            self.assertEqual(resources.produce(None, SOURCE, self.root), answer)
            validate.assert_called_once(); cold.assert_not_called()

    def test_unknown_inputs_never_request_whole_game_recovery(self):
        with patch.object(resources, "_produce_pc") as cold, self.assertRaisesRegex(resources.BuildError, "no full-game rebuild is required"):
            resources.produce(None, SOURCE, self.root)
        cold.assert_not_called()

    def test_short_workspace_has_one_full_identity_and_keeps_unknown_collisions(self):
        expected = resources.plan(SOURCE); producer = "a" * 64
        work = resources._work_root(self.root, expected, producer)
        marker = work / "owner.json"; before = (marker.read_bytes(), marker.stat().st_mtime_ns)
        self.assertEqual(resources._work_root(self.root, expected, producer), work)
        self.assertEqual((marker.read_bytes(), marker.stat().st_mtime_ns), before)
        self.assertEqual(json.loads(marker.read_text()), {"schema": 1, "scope": resources.WORK_SCOPE,
            "recipeKey": expected["recipeKey"], "producerKey": producer})
        with self.assertRaises(resources.BuildError): resources._work_root(self.root, expected, "a" * 12 + "b" * 52)
        alien = self.root / "work/sky-cccccccccccc"; alien.mkdir()
        keep = alien / "unknown.keep"; keep.write_bytes(b"retain")
        with self.assertRaises(resources.BuildError): resources._work_root(self.root, expected, "c" * 64)
        self.assertEqual(keep.read_bytes(), b"retain")

    def test_short_windows_native_paths_fit_without_losing_sha_identities(self):
        self.assertLess(resources._windows_work_budget(r"C:\q", "f" * 64)["maximumPathUtf16Units"], 260)
        with self.assertRaisesRegex(resources.BuildError, "shorter output root"):
            resources._windows_work_budget("C:\\" + "long-root-" * 16, "f" * 64)

    def test_native_lightmode_import_preserves_pass_semantics_and_rejects_unknown(self):
        native = SimpleNamespace(tags=lambda row: row)
        original = native.tags
        producer = SimpleNamespace(recovery_module=lambda: native,
            shader_source=lambda form, *args: native.tags(form))
        resources._sky_import_tags(producer)
        for mode in ("ForwardBase", "ForwardAdd", "Deferred", "Vertex", "VertexLM", "ShadowCaster"):
            row = {"tags": [("LIGHTMODE", mode.upper()), ("RenderType", "Opaque")]}
            self.assertEqual(producer.shader_source(row, None, None, None),
                {"tags": [("LightMode", mode), ("RenderType", "Opaque")]})
            self.assertIs(native.tags, original)
            self.assertEqual(row["tags"][0][0], "LIGHTMODE")
        with self.assertRaises(resources.BuildError):
            producer.shader_source({"tags": [("LIGHTMODE", "Unknown")]}, None, None, None)
        self.assertIs(native.tags, original)


@unittest.skipUnless(os.environ.get("GHVRQ_SKY_BASE_APK"), "actual owned APK qualification is an explicit bounded scope")
class ActualSkyResources(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temporary = tempfile.TemporaryDirectory()
        cls.output = Path(cls.temporary.name)
        cls.result = resources.produce(None, SOURCE, cls.output, base_apk=Path(os.environ["GHVRQ_SKY_BASE_APK"]))
        cls.raw = cls.result["bundle"].read_bytes()

    @classmethod
    def tearDownClass(cls): cls.temporary.cleanup()

    def changed(self, mutate):
        import UnityPy
        env = UnityPy.load(self.raw); wrapper = next(iter(env.files.values())); file = env.assets[0]
        mutate(file)
        return wrapper.save("lz4")

    def test_actual_cold_android_data_has_all_native_dependencies_and_bounded_reads(self):
        value = json.loads(self.result["provenance"].read_text())
        self.assertFalse(self.result["reused"])
        self.assertEqual(self.result["basis"]["kind"], "retained-android-player")
        audit = resources.inspect_bank(self.raw, SOURCE)
        self.assertEqual(audit["platform"], 13)
        self.assertEqual(len(audit["shaders"]), 3)
        self.assertEqual([len(r["mips"]) for r in audit["textures"]], [13, 10])
        self.assertTrue(all(r["format"] == 34 for r in audit["textures"]))
        read_log = self.output / "logs" / ("default-sky-player-reads-" + self.result["recipeKey"][:12] + "-" + self.result["producerKey"][:12] + ".json")
        acquisition = json.loads(read_log.read_text())
        reads = acquisition["files"]
        self.assertLess(sum(r["size"] for f in reads for r in f["readMembers"]), 12 * 1048576)
        self.assertTrue(acquisition["scene"].endswith("level9")) # Explicitly the local B625 evidence, not a product locator.
        self.assertNotIn("files", value["acquisition"])

    def test_actual_cache_reuse_needs_no_editor_or_pc(self):
        result = resources.produce(None, SOURCE, self.output)
        self.assertTrue(result["reused"])
        self.assertEqual(result["bundle"].read_bytes(), self.raw)

    def test_actual_installed_resource_pair_reuses_without_any_player_or_pc_data(self):
        metadata = self.result["provenance"].read_bytes()
        values = [(resources.BUNDLE_RESOURCE, self.raw), (resources.PROVENANCE_RESOURCE, metadata)]
        header = struct.pack("<i", len(values))
        for name, raw in values:
            encoded = name.encode(); header += struct.pack("<ii", len(raw), len(encoded)) + encoded
        sidecar = struct.pack("<i", len(header)) + header + b"".join(raw for _, raw in values)
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive: archive.writestr(resources.RESOURCE_MEMBER, sidecar)
        with tempfile.TemporaryDirectory() as temp, zipfile.ZipFile(io.BytesIO(stream.getvalue())) as archive:
            result = resources._installed_pair(archive, SOURCE, resources.plan(SOURCE), Path(temp))
            self.assertTrue(result["reused"])
            self.assertEqual(result["bundle"].read_bytes(), self.raw)
            before = [(result[k].read_bytes(), result[k].stat().st_mtime_ns) for k in ("bundle", "provenance")]
            archive.fp.seek(0)
            second = resources._installed_pair(archive, SOURCE, resources.plan(SOURCE), Path(temp))
            self.assertEqual([(second[k].read_bytes(), second[k].stat().st_mtime_ns) for k in ("bundle", "provenance")], before)
            second["provenance"].write_bytes(b"damaged owned proof")
            repaired = resources._installed_pair(archive, SOURCE, resources.plan(SOURCE), Path(temp))
            self.assertTrue(repaired["repaired"])
            self.assertEqual(repaired["bundle"].read_bytes(), self.raw)
            self.assertEqual(repaired["provenance"].read_bytes(), metadata)

    def test_produce_repairs_only_exact_known_pair_from_actual_apk(self):
        for damage in ("corrupt-bank", "missing-bank", "corrupt-proof", "missing-proof"):
            with self.subTest(damage=damage), tempfile.TemporaryDirectory() as temp:
                output = Path(temp); folder = output / self.result["cacheRelativePath"]
                folder.mkdir(parents=True)
                for key in ("bundle", "provenance"): shutil.copyfile(self.result[key], folder / self.result[key].name)
                keep = folder / "unrecognized-child.keep"; keep.write_bytes(b"must survive scoped pair repair")
                path = folder / (resources.BANK if damage.endswith("bank") else resources.PROVENANCE)
                if damage.startswith("corrupt"): path.write_bytes(b"damaged owned pair")
                else: path.unlink()
                with self.assertRaises(resources.BuildError): resources._validate(folder, resources.plan(SOURCE), SOURCE, output)
                repaired = resources.produce(None, SOURCE, output, base_apk=Path(os.environ["GHVRQ_SKY_BASE_APK"]))
                self.assertTrue(repaired["repaired"])
                self.assertEqual(repaired["bundle"].read_bytes(), self.raw)
                self.assertEqual(keep.read_bytes(), b"must survive scoped pair repair")
                self.assertEqual(repaired["provenance"].read_bytes(), self.result["provenance"].read_bytes())
                self.assertNotIn("cacheRepair", json.loads(repaired["provenance"].read_text()))
                report = output / "logs" / ("default-sky-repair-" + repaired["recipeKey"][:12] + "-" + repaired["producerKey"][:12] + ".json")
                self.assertTrue(json.loads(report.read_text())["repairs"][-1]["before"])

    def test_unknown_source_and_file_position_are_not_cache_repairs(self):
        apk = Path(os.environ["GHVRQ_SKY_BASE_APK"])
        for kind in ("source", "directory", "link"):
            if kind == "link" and os.name == "nt": continue
            with self.subTest(kind=kind), tempfile.TemporaryDirectory() as temp:
                output = Path(temp); folder = output / self.result["cacheRelativePath"]; folder.mkdir(parents=True)
                sentinel = output / "original.keep"; sentinel.write_bytes(b"never overwritten")
                if kind == "directory": (folder / resources.BANK).mkdir()
                elif kind == "link": (folder / resources.BANK).symlink_to(sentinel)
                if kind == "source":
                    with patch.object(resources, "_android_assets", side_effect=resources.BuildError("unknown native source")), self.assertRaisesRegex(resources.BuildError, "unknown native source"):
                        resources.produce(None, SOURCE, output, base_apk=apk)
                else:
                    with self.assertRaises(resources.BuildError): resources.produce(None, SOURCE, output, base_apk=apk)
                self.assertEqual(sentinel.read_bytes(), b"never overwritten")
                self.assertFalse((folder / resources.PROVENANCE).exists())

    def test_causal_native_bank_controls_fail_closed(self):
        def tree(file, pid, change):
            obj = file.objects[pid]; value = obj.read_typetree(); change(value); obj.save_typetree(value)
        controls = {
            "desktop-target": lambda f: setattr(f, "_m_target_platform", 19),
            "wrong-unity": lambda f: setattr(f, "unity_version", "2021.3.4f1"),
            "lost-fallback": lambda f: f.objects.pop(1006),
            "external-fallback": lambda f: tree(f, 18, lambda t: t["m_Dependencies"][0].update(m_FileID=1)),
            "wrong-material-value": lambda f: tree(f, 4, lambda t: t["m_SavedProperties"]["m_Floats"].append(("wrong", 1))),
            "lost-mip": lambda f: tree(f, 8, lambda t: t.update(m_MipCount=12)),
            "windows-texture": lambda f: tree(f, 8, lambda t: t.update(m_TextureFormat=10)),
            "wrong-mesh": lambda f: tree(f, 12, lambda t: t["m_VertexData"].update(m_VertexCount=1678)),
            "wrong-shader-api": lambda f: tree(f, 18, lambda t: t.update(platforms=[4])),
            "wrong-runtime-name": lambda f: tree(f, 1, lambda t: t["m_Container"].__setitem__(0, ("wrong", t["m_Container"][0][1]))),
        }
        for name, mutation in controls.items():
            with self.subTest(control=name), self.assertRaises(resources.BuildError):
                resources.inspect_bank(self.changed(mutation), SOURCE)


@unittest.skipUnless(os.environ.get("GHVRQ_SKY_PC_GAME"), "exact original PC shader qualification is an explicit bounded scope")
class ActualVertexLit(unittest.TestCase):
    def test_three_native_lighting_programs_and_unknown_read_type_stride_hash_controls(self):
        with tempfile.TemporaryDirectory() as work:
            native = resources._load(SOURCE, "tools/quest-builder/full_shaders.py")
            assets = resources._pc_assets(Path(os.environ["GHVRQ_SKY_PC_GAME"]))
            original = native.restore_uniforms
            resources._install_sky_binder(native, assets)
            seen = set()
            for program in native.original_programs(assets["vertex-lit"]["object"].read()):
                interface = program["interface"]; key = resources.value_hash(interface)
                if key not in resources.VERTEX_LIT or key in seen: continue
                seen.add(key)
                translated = native.translate(program["dxbc"], Path(work))
                hlsl = Path(translated["hlslPath"]).read_text()
                args = (translated["inputSignature"], translated["originalBufferLayouts"])
                with self.assertRaises(native.ShaderRecoveryError): original(hlsl, interface, *args)
                bound, observed = native.restore_uniforms(hlsl, interface, *args)
                self.assertTrue(bound.startswith("int4 unity_VertexLightParams;\n"))
                self.assertIn("cb1_0_m0[0] = float4(0.0, 0.0, 0.0, 0.0);", bound)
                lighting = next(row for row in observed if row["originalBuffer"] == "UnityLighting")
                self.assertEqual(lighting["skyOriginalDxbcSha256"], program["originalDxbcSha256"])
                self.assertEqual(lighting["skyUnreachableInitializerScalars"], list(range(28)))
                self.assertIn("cb1_0_m0[uint(asint(r2.w)) + 7u].xyz", bound)
                for member, value in (("type", 1), ("columns", 3), ("byteOffset", 108), ("arraySize", 7)):
                    changed = copy.deepcopy(interface)
                    next(b for b in changed["buffers"] if b["name"] == "UnityLighting")["fields"][0][member] = value
                    with self.subTest(program=key, control=member), self.assertRaises(resources.BuildError):
                        resources._vertex_lit_uniforms(native, original, hlsl, changed, *args)
                for control in (hlsl.replace("+ 7u].xyz", "+ 0u].xyz", 1),
                                hlsl.replace("r2.w = 0.0f;", "r2.w = -1.0f;", 1), hlsl + "\n"):
                    with self.assertRaises(resources.BuildError):
                        resources._vertex_lit_uniforms(native, original, control, interface, *args)
            self.assertEqual(seen, set(resources.VERTEX_LIT))
            key = next(iter(resources.VERTEX_LIT)); old = resources.VERTEX_LIT[key]
            with patch.dict(resources.VERTEX_LIT, {key: ("0" * 64, *old[1:])}), self.assertRaises(resources.BuildError):
                resources._install_sky_binder(native, assets)


if __name__ == "__main__": unittest.main()
