"""Exercise the authored Android helper protocol against actual JVM execution.

Android framework/Binder classes are explicit stand-ins. These tests establish
input/mutation boundaries, not Quest firmware support or real app readability.
"""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "tools/quest-installer/android/StorageAccess.java"
BASE = "/storage/emulated/0/Android/data/dev.gloomhavenvr.quest/files/"
MOD = BASE + "quest-mod-resources"
GAME = BASE + "quest-owned-game"


class AndroidStorageHelperTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        fallback = Path("/home/claw/unity-2021.3.5/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin")
        cls.javac = shutil.which("javac") or str(fallback / "javac")
        cls.java = shutil.which("java") or str(fallback / "java")
        if not Path(cls.javac).is_file() or not Path(cls.java).is_file():
            raise unittest.SkipTest("An actual JDK is required for the Android helper protocol tests.")
        cls.temporary = tempfile.TemporaryDirectory(prefix="quest-storage-helper-jvm-")
        cls.directory = Path(cls.temporary.name)
        classes = {
            "android/os/Process.java": '''package android.os;
public final class Process {
 public static int myUid() { return Integer.getInteger("fixture.uid", 2000); }
}''',
            "android/os/Build.java": '''package android.os;
public final class Build {
 public static final class VERSION {
  public static final int SDK_INT = Integer.getInteger("fixture.sdk", 34);
 }
}''',
            "android/os/IBinder.java": "package android.os; public interface IBinder { }",
            "android/os/ServiceManager.java": '''package android.os;
public final class ServiceManager {
 public static IBinder getService(String name) {
  if (!"mount".equals(name)) throw new AssertionError("Foreign service");
  return Boolean.getBoolean("fixture.noService") ? null : new IBinder() { };
 }
}''',
            "android/os/storage/IStorageManager.java": '''package android.os.storage;
public interface IStorageManager {
 void fixupAppDir(String path);
 public abstract class Stub {
  public static int calls;
  public static IStorageManager asInterface(android.os.IBinder binder) {
   if (binder == null) throw new AssertionError("Null binder");
   return new IStorageManager() {
    public void fixupAppDir(String path) {
     ++calls;
     if (Boolean.getBoolean("fixture.serviceFailure"))
      throw new SecurityException("Firmware storage repair refusal");
    }
   };
  }
 }
}''',
            "dev/gloomhavenvr/tools/StorageFixture.java": '''package dev.gloomhavenvr.tools;
public final class StorageFixture {
 public static void main(String[] args) throws Exception {
  try {
   if ("real-directory".equals(args[0])) {
    Class<?> type = Class.forName("dev.gloomhavenvr.tools.StorageAccess$RealDirectories");
    java.lang.reflect.Constructor<?> constructor = type.getDeclaredConstructor();
    constructor.setAccessible(true);
    Object facts = constructor.newInstance();
    java.lang.reflect.Method method = type.getMethod("requireOrdinaryDirectory", String.class);
    method.setAccessible(true);
    method.invoke(facts, args[1]);
   } else {
    StorageAccess.execute(System.in, System.out, new StorageAccess.DirectoryFacts() {
     public void requireOrdinaryDirectory(String path) throws Exception {
      if (path.endsWith("/missing") || path.endsWith("/symlink"))
       throw new java.io.IOException("Fixture directory boundary refused");
     }
    });
   }
  } catch (Throwable error) {
   while (error instanceof java.lang.reflect.InvocationTargetException && error.getCause() != null)
    error = error.getCause();
   System.err.println("FIXTURE_CALLS=" + android.os.storage.IStorageManager.Stub.calls);
   System.err.println(error.toString());
   System.exit(1);
  }
  System.err.println("FIXTURE_CALLS=" + android.os.storage.IStorageManager.Stub.calls);
 }
}''',
        }
        paths = []
        for name, content in classes.items():
            path = cls.directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8")
            paths.append(path)
        completed = subprocess.run([cls.javac, "-encoding", "UTF-8", "-source", "8", "-target", "8", "-g:none",
                                    "-d", str(cls.directory), str(SOURCE), *map(str, paths)],
                                   capture_output=True, text=True, timeout=45)
        if completed.returncode:
            cls.temporary.cleanup()
            raise AssertionError("Actual helper/JVM fixture compilation failed: " + completed.stderr)

    @classmethod
    def tearDownClass(cls):
        cls.temporary.cleanup()

    def invoke(self, paths=(), *, raw=None, properties=(), mode="protocol", extra=()):
        if raw is None:
            raw = ("GHVRQ_STORAGE_FIXUP_V1\n" + "\n".join(paths) + "\nEND\n").encode("utf-8")
        return subprocess.run([self.java, *["-D" + value for value in properties], "-cp", str(self.directory),
                               "dev.gloomhavenvr.tools.StorageFixture", mode, *extra],
                              input=raw, capture_output=True, timeout=15)

    def rejected(self, result, *, calls=0):
        self.assertNotEqual(0, result.returncode)
        self.assertIn(("FIXTURE_CALLS=" + str(calls)).encode(), result.stderr)
        self.assertNotIn(b"GHVRQ_STORAGE_COMPLETE", result.stdout)

    def test_two_bank_paths_are_acknowledged_in_exact_input_order(self):
        paths = [MOD + "/StreamingAssets", GAME + "/StreamingAssets/aa/Android"]
        result = self.invoke(paths)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("GHVRQ_STORAGE_BEGIN\t2\n"
                         + "GHVRQ_STORAGE_DONE\t1\t" + paths[0] + "\n"
                         + "GHVRQ_STORAGE_DONE\t2\t" + paths[1] + "\n"
                         + "GHVRQ_STORAGE_COMPLETE\t2\n", result.stdout.decode())
        self.assertIn(b"FIXTURE_CALLS=2", result.stderr)

    def test_unicode_and_spaces_remain_exact_without_shell_interpolation(self):
        path = GAME + "/StreamingAssets/Grüße 角色/$(literal)"
        result = self.invoke([path])
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn(path, result.stdout.decode("utf-8"))

    def test_foreign_savedata_traversal_and_alias_paths_never_mutate(self):
        for path in [BASE + "GloomSaves", MOD + "/settings", MOD + "/StreamingAssets/../settings",
                     MOD + "/StreamingAssets/./x", MOD + "/StreamingAssets//x",
                     MOD + "/StreamingAssets/x/", MOD + "/StreamingAssets\\x",
                     "/sdcard/Android/data/dev.gloomhavenvr.quest/files/quest-mod-resources",
                     "/storage/emulated/10/Android/data/dev.gloomhavenvr.quest/files/quest-mod-resources",
                     "/storage/emulated/0/Android/data/foreign/files/quest-mod-resources"]:
            with self.subTest(path=path):
                self.rejected(self.invoke([MOD, path]))

    def test_invalid_late_directory_is_found_before_first_platform_call(self):
        self.rejected(self.invoke([MOD, GAME + "/StreamingAssets/missing"]))
        self.rejected(self.invoke([MOD, GAME + "/StreamingAssets/symlink"]))

    def test_actual_filesystem_symlink_is_refused_without_android_calls(self):
        with tempfile.TemporaryDirectory(prefix="quest-helper-boundary-") as name:
            root = Path(name)
            ordinary = root / "ordinary"
            ordinary.mkdir()
            link = root / "link"
            try:
                link.symlink_to(ordinary, target_is_directory=True)
            except OSError as error:
                self.skipTest("This host cannot create the symlink fixture: " + str(error))
            self.assertEqual(0, self.invoke(mode="real-directory", extra=[str(ordinary)]).returncode)
            self.rejected(self.invoke(mode="real-directory", extra=[str(link)]))
            self.rejected(self.invoke(mode="real-directory", extra=[str(link / "child")]))

    def test_non_shell_and_other_user_shell_uid_refused(self):
        for uid in (0, 10191, 102000):
            with self.subTest(uid=uid):
                self.rejected(self.invoke([MOD], properties=["fixture.uid=" + str(uid)]))

    def test_unsupported_sdk_and_missing_service_refused(self):
        self.rejected(self.invoke([MOD], properties=["fixture.sdk=29"]))
        self.rejected(self.invoke([MOD], properties=["fixture.noService=true"]))

    def test_platform_security_failure_is_not_a_complete_repair(self):
        result = self.invoke([MOD], properties=["fixture.serviceFailure=true"])
        self.rejected(result, calls=1)
        self.assertIn(b"SecurityException: Firmware storage repair refusal", result.stderr)
        self.assertNotIn(b"GHVRQ_STORAGE_DONE", result.stdout)

    def test_duplicates_empty_input_wrong_magic_and_trailing_data_refused(self):
        self.rejected(self.invoke([MOD, MOD]))
        for raw in (b"GHVRQ_STORAGE_FIXUP_V1\nEND\n", b"wrong\nEND\n",
                    ("GHVRQ_STORAGE_FIXUP_V1\n" + MOD + "\nEND\ntrailing\n").encode(),
                    ("GHVRQ_STORAGE_FIXUP_V1\n" + MOD + "\nEND").encode()):
            with self.subTest(raw=raw[:40]):
                self.rejected(self.invoke(raw=raw))

    def test_invalid_utf8_nul_tab_cr_and_oversized_line_refused(self):
        for suffix in (b"\xff", b"\0", b"\t", b"\r", b"x" * 8193):
            with self.subTest(suffix=suffix[:10]):
                self.rejected(self.invoke(raw=b"GHVRQ_STORAGE_FIXUP_V1\n"
                                          + (MOD + "/StreamingAssets/").encode() + suffix + b"\nEND\n"))

    def test_total_bytes_and_directory_count_are_bounded_before_mutation(self):
        for paths in ([MOD + "/StreamingAssets/" + str(index) for index in range(32769)],
                      [MOD + "/StreamingAssets/" + "x" * 1024 + str(index) for index in range(4096)]):
            with self.subTest(count=len(paths)):
                self.rejected(self.invoke(paths))

    def test_actual_public_entrypoint_rejects_argv_with_bounded_error(self):
        result = subprocess.run([self.java, "-cp", str(self.directory),
                                 "dev.gloomhavenvr.tools.StorageAccess", "foreign-package"],
                                capture_output=True, timeout=15)
        self.assertEqual(2, result.returncode)
        self.assertEqual(b"", result.stdout)
        self.assertIn(b"GHVRQ_STORAGE_ERROR\tIllegalArgumentException:", result.stderr)
        self.assertLess(len(result.stderr), 900)

    def test_shipped_dex_and_source_match_the_reproducible_manifest(self):
        manifest = json.loads(SOURCE.with_suffix(".build.json").read_text(encoding="utf-8"))
        self.assertEqual(hashlib.sha256(SOURCE.read_bytes()).hexdigest(), manifest["sourceSha256"])
        dex = SOURCE.with_suffix(".dex").read_bytes()
        self.assertTrue(dex.startswith(b"dex\n"))
        self.assertEqual(len(dex), manifest["dexBytes"])
        self.assertEqual(hashlib.sha256(dex).hexdigest(), manifest["dexSha256"])
        self.assertEqual("dev.gloomhavenvr.tools.StorageAccess", manifest["mainClass"])
        self.assertLess(len(dex), 16384)


if __name__ == "__main__":
    unittest.main()
