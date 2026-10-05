import importlib.util
import io
import pathlib
import struct
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import protocol
import runtime
from wine_paths import relocate
from box64_guest import ORIGINAL, ADAPTED, apply
import box64_host
from tasks import canonical_task


class RuntimeContracts(unittest.TestCase):
    def test_box64_header_recipe_preserves_dependencies_without_shell_or_git(self):
        with tempfile.TemporaryDirectory(prefix="Quest & 100% ") as directory:
            source = pathlib.Path(directory)
            original = 'add_custom_command(\n  OUTPUT "${BOX64_ROOT}/src/git_head.h"\n' + box64_host.ORIGINAL + '\n  DEPENDS original_sources\n  VERBATIM)\n'
            (source / "CMakeLists.txt").write_text(original * 2)
            box64_host.apply(source, runtime.LOCK["box64"]["revision"])
            text = (source / "CMakeLists.txt").read_text()
            self.assertEqual(text.count('DEPENDS original_sources'), 2)
            self.assertEqual(text.count(box64_host.ADAPTED), 2)
            self.assertNotIn('COMMAND sh', text)
            self.assertNotIn('git rev-parse', text)
            self.assertIn(runtime.LOCK["box64"]["revision"][:7], (source / "quest_git_head.cmake").read_text())
            with self.assertRaisesRegex(RuntimeError, "audited source"):
                box64_host.apply(source, runtime.LOCK["box64"]["revision"])
            with self.assertRaisesRegex(RuntimeError, "full commit ID"):
                box64_host.apply(source, 'not-a-revision')

    def test_copied_boundary_header(self):
        self.assertEqual(protocol.HEADER.size, 20)
        expected = b"GHPR\x01\x00\x08\x00\x07\x00\x00\x00\x03\x00\x00\x00\x00\x00\x00\x00"
        self.assertEqual(protocol.HEADER.pack(protocol.MAGIC, 1, 8, 7, 3, 0), expected)

    def test_descriptor_not_nul_terminated_payload(self):
        self.assertEqual(protocol.string("Crypt.Door"), struct.pack("<I", 10) + b"Crypt.Door")
        with self.assertRaises(ValueError):
            protocol.string("Crypt\0Door")

    def test_only_documented_identity_fields_are_normalized(self):
        # Same original frame/parameter bytes, independent opaque set/resource IDs.
        def task(key, resource, tier=0):
            return struct.pack("<iii3fiiiii", 0x41414141, key, tier, 0, 0, 0,
                               0x4f4f4f4f, 1, 0, resource, 0) + struct.pack("<ii", 0x47474747, 0)
        first, second = task(13, 2), task(9, 5)
        self.assertEqual(canonical_task(first, {2: 1}), canonical_task(second, {5: 1}))
        self.assertNotEqual(canonical_task(first, {2: 1}), canonical_task(task(9, 5, tier=1), {5: 1}))
        with self.assertRaises(ValueError):
            canonical_task(first, {})
        with self.assertRaises(ValueError):
            canonical_task(first[:-1], {2: 1})

    def test_guest_rule_is_exact_and_android_only(self):
        self.assertIn("#ifdef ANDROID", ADAPTED)
        self.assertIn("header[18] != 0x3e", ADAPTED)
        with tempfile.TemporaryDirectory() as temporary:
            source = pathlib.Path(temporary)
            path = source / "src/os/os_linux.c"
            path.parent.mkdir(parents=True)
            path.write_text(ORIGINAL)
            apply(source)
            self.assertEqual(path.read_text(), ADAPTED)
            with self.assertRaises(RuntimeError):
                apply(source)

    def test_debian_ar_boundaries(self):
        def member(name, data):
            return (name + "/").encode().ljust(16) + b"0".ljust(12) + b"0".ljust(6) + b"0".ljust(6) + b"100644".ljust(8) + str(len(data)).encode().ljust(10) + b"`\n" + data + (b"\n" if len(data) % 2 else b"")
        with tempfile.TemporaryDirectory() as temporary:
            path = pathlib.Path(temporary) / "input.deb"
            path.write_bytes(b"!<arch>\n" + member("debian-binary", b"2.0\n") + member("data.tar.xz", b"xyz"))
            self.assertEqual(runtime.ar_member(path, "data.tar"), b"xyz")
            path.write_bytes(b"!<arch>\nshort")
            with self.assertRaises(RuntimeError):
                runtime.ar_member(path, "data.tar")

    def test_wine_socket_relocation_preserves_offsets_and_arguments(self):
        import hashlib
        original = b"/tmp/.wine-%u/server-%s\0"
        data = b"ELF-before\0" + original + b"ELF-after"
        expected = hashlib.sha256(data).hexdigest()
        changed = relocate(data, expected, original)
        self.assertEqual(len(changed), len(data))
        self.assertIn(b"./.wine-%u/server-%s\0", changed)
        self.assertEqual(changed[-9:], b"ELF-after")
        with self.assertRaises(RuntimeError):
            relocate(data + original, expected, original)
        with self.assertRaises(RuntimeError):
            relocate(changed, expected, original)

    def test_no_guest_execution_or_unpinned_stack(self):
        self.assertEqual(runtime.LOCK["box64"]["version"], "0.4.4")
        self.assertEqual(len(runtime.LOCK["box64"]["sha256"]), 64)
        bridge = (ROOT / "bridge.c").read_text()
        self.assertIn('char *arguments[] = {box, wine, worker_path, 0}', bridge)
        self.assertIn('"%s/libquest_box64.so", executable_dir', bridge)
        self.assertIn('return valid ? 0 : -1', bridge)
        self.assertNotIn('execv(worker_path', bridge)


if __name__ == "__main__":
    unittest.main()
