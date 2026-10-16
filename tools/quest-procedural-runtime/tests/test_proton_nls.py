"""Actual ELF interposition witnesses for the separately executed Wine server.

The process fixture reproduces the pinned server's executable-relative NLS
lookup. It does not run Wine, translated game code, Unity or an Android device.
"""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class ActualServerNlsOrigin(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        compiler = shutil.which("cc")
        if not compiler or not sys.platform.startswith("linux"):
            raise unittest.SkipTest("ELF interposition witness requires Linux and a C compiler.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="ghpr-nls-")
        cls.root = Path(cls.temporary.name)
        cls.native = cls.root / "native"
        cls.native.mkdir()
        cls.wine = cls.root / "Retained Wine & data"
        (cls.wine / "lib").mkdir(parents=True)
        cls.nls = cls.wine / "share/wine/nls/l_intl.nls"
        cls.nls.parent.mkdir(parents=True)
        cls.nls.write_bytes(b"retained NLS witness, never shipped")
        cls.bridge = cls.native / "libQuestApparance.so"
        args = [compiler, "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror"]
        subprocess.run(args + ["-shared", "-fPIC", "-fvisibility=hidden", "-DGHPR_PROTON_BACKEND=1",
                              str(ROOT / "bridge.c"), "-ldl", "-pthread", "-o", str(cls.bridge)], check=True)
        source = cls.root / "nls-process.c"
        source.write_text(r'''
#define _GNU_SOURCE
#include <assert.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
int main(int argc, char **argv) {
    char buffer[PATH_MAX];
    char *origin = realpath(argc > 1 ? argv[1] : "/proc/self/exe", NULL);
    if (!origin) return 65;
    char *(*volatile resolve)(const char *, char *) = realpath;
    assert(resolve(argc > 1 ? argv[1] : "/proc/self/exe", buffer));
    assert(!strcmp(buffer, origin));
    printf("%s\n", origin);
    if (argc > 1) { free(origin); return 0; }
    char *last = strrchr(origin, '/'); assert(last); *last = 0;
    char nls[PATH_MAX];
    int length = snprintf(nls, sizeof nls, "%s/../share/wine/nls/l_intl.nls", origin);
    assert(length > 0 && (size_t)length < sizeof nls);
    free(origin);
    FILE *input = fopen(nls, "rb");
    if (!input) return 66;
    assert(fgetc(input) == 'r'); fclose(input); return 0;
}
''')
        cls.server = cls.native / "libquest_proton_server.so"
        cls.other = cls.native / "unrelated-process"
        subprocess.run(args + [str(source), "-o", str(cls.server)], check=True)
        shutil.copy2(cls.server, cls.other)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def execute(self, *, executable=None, preload=True, arguments=(), **changes):
        environment = dict(os.environ, GHPR_NATIVE_DIR=str(self.native), GHPR_WINE_ROOT=str(self.wine),
                           LD_PRELOAD=str(self.bridge) if preload else "")
        environment.update(changes)
        return subprocess.run([str(executable or self.server), *arguments], env=environment,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=10)

    def test_unchanged_server_lookup_opens_retained_nls_without_creating_data(self):
        before = {p.relative_to(self.root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                  for p in self.root.rglob("*") if p.is_file()}
        old = self.execute(preload=False)
        self.assertEqual(old.returncode, 66)
        self.assertEqual(old.stdout.strip(), str(self.server))
        result = self.execute()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), str(self.wine / "lib/wineserver"))
        self.assertIn("retained Wine payload", result.stderr)
        after = {p.relative_to(self.root).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                 for p in self.root.rglob("*") if p.is_file()}
        self.assertEqual(before, after)
        self.assertFalse((self.wine / "bin").exists())

    def test_other_executable_and_wrong_native_identity_keep_actual_origin(self):
        for executable, changes in ((self.other, {}), (self.server, {"GHPR_NATIVE_DIR": str(self.root)})):
            with self.subTest(executable=executable, changes=changes):
                result = self.execute(executable=executable, **changes)
                self.assertEqual(result.returncode, 66, result.stderr)
                self.assertEqual(result.stdout.strip(), str(executable))
                self.assertNotIn("retained Wine payload", result.stderr)

    def test_missing_relative_or_dot_segment_wine_roots_delegate_libc(self):
        for root in ("", "relative", str(self.root / "absent"), str(self.wine) + "/",
                     str(self.wine) + "/../Retained Wine & data", str(self.wine) + "/./"):
            with self.subTest(root=root):
                result = self.execute(GHPR_WINE_ROOT=root)
                self.assertEqual(result.returncode, 66, result.stderr)
                self.assertEqual(result.stdout.strip(), str(self.server))
                self.assertNotIn("retained Wine payload", result.stderr)

    def test_unrelated_existing_and_missing_paths_keep_libc_behavior(self):
        result = self.execute(arguments=(str(self.nls.parent / "../nls/l_intl.nls"),))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.strip(), str(self.nls))
        self.assertNotIn("retained Wine payload", result.stderr)
        result = self.execute(arguments=(str(self.root / "not-present"),))
        self.assertEqual(result.returncode, 65)
        self.assertEqual(result.stdout, "")

    def test_existing_game_exports_and_only_proton_interposer_are_public(self):
        symbols = subprocess.check_output(["nm", "-D", "--defined-only", str(self.bridge)], text=True).split()
        self.assertIn("realpath", symbols)
        for name in ("ApparanceInitialise", "ApparanceIsRunning", "ApparanceUpdate", "ApparanceSave",
                     "ApparanceShutdown", "ApparanceCreateEntity", "ApparanceDestroyEntity", "ApparanceEntityBuild",
                     "ApparancePopEntityTask", "ApparancePopEngineTask", "ApparanceUpdateAsset", "ApparanceGetNextAssetRequest",
                     "quest_apparance_configure", "quest_apparance_last_error"):
            self.assertIn(name, symbols)
        normal = self.root / "ordinary-backend.so"
        compiler = shutil.which("cc")
        subprocess.run([compiler, "-std=c11", "-O2", "-Wall", "-Wextra", "-Werror", "-fPIC", "-shared",
                        "-fvisibility=hidden", str(ROOT / "bridge.c"), "-pthread", "-o", str(normal)], check=True)
        normal_symbols = subprocess.check_output(["nm", "-D", "--defined-only", str(normal)], text=True).split()
        self.assertNotIn("realpath", normal_symbols)


if __name__ == "__main__":
    unittest.main()
