"""Real Wizard signing packets cross the compact-update boundary without Unity.

Only native version output is adapted. Tiny cached, checksum-qualified archives
run the actual downloader, extractor, profile_tools writer and update reader.
No real Android SDK, Unity import, game input or headset result is asserted.
"""
from copy import deepcopy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import Mock, patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/quest-wizard"))
import provision
from state import Store, atomic_json

spec = importlib.util.spec_from_file_location("signing_handoff_driver", ROOT / "tools/quest-builder/update_driver.py")
driver = importlib.util.module_from_spec(spec)
spec.loader.exec_module(driver)


class SigningToolHandoffTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="quest signing handoff ")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.builder = SimpleNamespace(BuildError=RuntimeError,
            toolchain=Mock(side_effect=AssertionError("A signing-only update must not select Unity or an NDK")))

    def produce(self, host):
        store = Store(self.root / host)
        session = store.create({"mode": "update-mod"})
        source = provision.LOCK if host == "windows-x64" else provision.LOCK["linux"]
        specs = {name: deepcopy(source[name]) for name in ("apkJdk", "apkBuildTools")}
        suffix = ".exe" if host == "windows-x64" else ""
        for name, selected in specs.items():
            selected["archive"] = "zip"
            platform_suffix = "-linux-x64" if host == "linux-x64" else ""
            archive = store.root / "tools/downloads" / (name + "-" + selected["version"] + platform_suffix + ".zip")
            archive.parent.mkdir(parents=True, exist_ok=True)
            executable = Path(selected["executable"])
            names = ([executable, executable.with_name("keytool" + suffix)] if name == "apkJdk" else
                     [executable, executable.with_name("zipalign" + suffix), executable.parent / "lib/apksigner.jar"])
            with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_STORED) as package:
                for member in names:
                    package.writestr(member.as_posix(), (name + ":" + member.as_posix()).encode())
            selected["hash"] = hashlib.new(selected["algorithm"], archive.read_bytes()).hexdigest()
            selected["size"] = archive.stat().st_size

        def version(argv, log, **kwargs):
            self.assertEqual([str(value) for value in argv][1:], ["-version"])
            self.assertEqual(kwargs, {"timeout": 30})
            self.assertTrue(Path(argv[0]).is_file())
            Path(log).parent.mkdir(parents=True, exist_ok=True)
            Path(log).write_text('openjdk version "17.0.16"\n', encoding="utf-8")

        native = Mock(side_effect=version)
        with store.active(session), patch.object(provision, "host_key", return_value=host), \
                patch.object(provision, "spec_for", side_effect=lambda name: specs[name]), \
                patch.object(provision.urllib.request, "urlopen", side_effect=AssertionError("Cached tools need no network")):
            paths, details = provision.profile_tools(store, session["session"], SimpleNamespace(run=native), stage="unity")
        native.assert_called_once()
        self.assertEqual(paths[0].name, "apk-tools.json")
        packet = json.loads(paths[0].read_text(encoding="utf-8"))
        self.assertEqual(packet, {"schema": 1, **details["apkTools"], "host": host})
        return store, paths[0], packet, details["apkTools"]

    def read(self, path):
        return driver.signing_tools(self.builder, SimpleNamespace(apk_tools_json=str(path)), self.root / "build")

    def test_actual_windows_and_linux_packets_keep_the_six_tools_without_metadata(self):
        for host in ("windows-x64", "linux-x64"):
            with self.subTest(host=host):
                _, path, _, expected = self.produce(host)
                before = path.read_bytes(), path.stat().st_mtime_ns
                self.assertEqual(self.read(path), expected)
                self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), before)
        self.builder.toolchain.assert_not_called()

    def test_existing_cached_host_packet_is_reused_without_reprovision_or_rewrite(self):
        _, path, _, expected = self.produce("windows-x64")
        before = path.read_bytes(), path.stat().st_mtime_ns
        with patch.object(provision, "profile_tools", side_effect=AssertionError("Retain cached tool selection")):
            self.assertEqual(self.read(path), expected)
            self.assertEqual(self.read(path), expected)
        self.assertEqual((path.read_bytes(), path.stat().st_mtime_ns), before)
        self.builder.toolchain.assert_not_called()

    def test_legacy_packet_without_optional_host_remains_valid(self):
        _, path, packet, expected = self.produce("linux-x64")
        del packet["host"]
        atomic_json(path, packet)
        self.assertEqual(self.read(path), expected)

    def test_schema_host_and_unknown_fields_are_not_silently_ignored(self):
        _, path, packet, _ = self.produce("windows-x64")
        cases = [{**packet, "host": value} for value in (None, "", "macos-x64", 1, {}, ["windows-x64"])]
        cases += [{**packet, "schema": value} for value in (None, True, "1", 2)]
        cases += [{**packet, "unknown": "metadata"}, [], None]
        for value in cases:
            with self.subTest(value=value):
                atomic_json(path, value)
                with self.assertRaisesRegex(RuntimeError, "unsupported schema"):
                    self.read(path)
        self.builder.toolchain.assert_not_called()

    def test_missing_or_invalid_paths_fail_without_compiler_fallback(self):
        _, path, packet, _ = self.produce("linux-x64")
        for name in ("java", "keytool", "jdk", "aapt", "apksigner", "zipalign"):
            missing = {key: value for key, value in packet.items() if key != name}
            cases = [(missing, "unsupported schema")]
            cases += [({**packet, name: value}, "unsupported schema") for value in (None, [], 0, "", "bad\0path", "x" * 32768)]
            cases += [({**packet, name: str(self.root / "missing")}, "missing: " + name)]
            for value, message in cases:
                with self.subTest(name=name, value=value.get(name)):
                    atomic_json(path, value)
                    with self.assertRaisesRegex(RuntimeError, message):
                        self.read(path)
        self.builder.toolchain.assert_not_called()

    def test_declared_tool_still_has_to_exist_and_jdk_must_be_a_directory(self):
        _, path, packet, _ = self.produce("windows-x64")
        atomic_json(path, {**packet, "jdk": packet["java"]})
        with self.assertRaisesRegex(RuntimeError, "missing: jdk"):
            self.read(path)
        atomic_json(path, packet)
        Path(packet["zipalign"]).unlink()
        with self.assertRaisesRegex(RuntimeError, "missing: zipalign"):
            self.read(path)

    def test_packet_size_and_invalid_json_remain_bounded(self):
        _, path, _, _ = self.produce("linux-x64")
        path.write_bytes(b" " * 65537)
        with self.assertRaisesRegex(RuntimeError, "excessive"):
            self.read(path)
        for value in (b"{", b"\xff"):
            path.write_bytes(value)
            with self.assertRaisesRegex(RuntimeError, "unsupported schema"):
                self.read(path)


if __name__ == "__main__":
    unittest.main()
