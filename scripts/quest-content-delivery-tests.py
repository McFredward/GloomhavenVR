#!/usr/bin/env python3
"""Run actual content delivery with native SHA-256 and corruption controls.

Host native code has the same exported ABI as Android. This proves worker/file
semantics and digest correctness, not Quest flash throughput or Unity rendering.
"""
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
try:
    import resource
except ImportError:
    resource = None


def native_vectors(library):
    api = ctypes.CDLL(str(library))
    api.ghvr_content_hash_create.restype = ctypes.c_void_p
    api.ghvr_content_hash_update.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_int]
    api.ghvr_content_hash_final.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    api.ghvr_content_hash_destroy.argtypes = [ctypes.c_void_p]
    assert api.ghvr_content_hash_abi() == 1
    vectors = [b"", b"abc", b"a" * 1000000]
    vectors.extend(bytes((i * 17 + 3) % 256 for i in range(n)) for n in (55, 56, 63, 64, 65, 127, 128, 129, 65537))
    checks = 0
    for data in vectors:
        for chunk in (1, 13, 64, 4093, 262144):
            context = api.ghvr_content_hash_create()
            assert context
            try:
                assert api.ghvr_content_hash_update(context, None, -1) == 0
                assert api.ghvr_content_hash_update(context, None, 1) == 0
                assert api.ghvr_content_hash_update(context, None, 0) == 1
                for start in range(0, len(data), chunk):
                    part = data[start:start + chunk]
                    assert api.ghvr_content_hash_update(context, part, len(part)) == 1
                digest = ctypes.create_string_buffer(32)
                assert api.ghvr_content_hash_final(context, digest) == 1
                assert digest.raw.hex() == hashlib.sha256(data).hexdigest()
                assert api.ghvr_content_hash_final(context, digest) == 0
                assert api.ghvr_content_hash_update(context, b"a", 1) == 0
                checks += 1
            finally:
                api.ghvr_content_hash_destroy(context)
    api.ghvr_content_hash_destroy(None)
    assert api.ghvr_content_hash_update(None, b"a", 1) == 0
    assert api.ghvr_content_hash_final(None, ctypes.create_string_buffer(32)) == 0
    # A bounded host-only throughput sample helps detect accidentally unoptimized
    # translation units. It is evidence, never a hardware performance promise.
    data = b"owned benchmark bytes" * 13107
    context = api.ghvr_content_hash_create()
    started = time.monotonic()
    for _ in range(256):
        assert api.ghvr_content_hash_update(context, data, len(data)) == 1
    elapsed = time.monotonic() - started
    digest = ctypes.create_string_buffer(32)
    assert api.ghvr_content_hash_final(context, digest) == 1
    assert digest.raw.hex() == hashlib.sha256(data * 256).hexdigest()
    api.ghvr_content_hash_destroy(context)
    return {"vectors": checks, "bytes": len(data) * 256, "hostSeconds": elapsed,
            "boundary": "host O2 scalar SHA-256; no Quest throughput claim"}


def main():
    if resource is not None:
        resource.setrlimit(resource.RLIMIT_CORE, (0, 0))
    root = Path(__file__).resolve().parents[1]
    runtime = root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime"
    names = ("QuestGameContent.cs", "QuestGameContent.Delivery.cs", "QuestGameArchiveDelivery.cs", "QuestContentHash.cs")
    sources = {name: (runtime / name).read_text() for name in names}
    native = root / "tools/quest-native"
    native_source = (native / "content_hash.cpp").read_text()
    output = root / ".planning/debug/quest-content-delivery"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    compiler = shutil.which("c++")
    if not compiler:
        raise SystemExit("A host C++17 compiler is required for the native hash ABI fixture.")
    mutations = (
        ("warm-cache-hash-bypass", "QuestGameArchiveDelivery.cs", 'matches = Hash(target, tracker.Stream(i + 1), "checking-files", file.path) == file.sha256;',
         'matches = Hash(target, tracker.Stream(i + 1), "checking-files", file.path) != null;', "exact-original-bytes"),
        ("archive-hash-bypass", "QuestGameArchiveDelivery.cs", "if (sha.Finish() != manifest.archiveSha256)", "if (manifest.archiveSha256 == null)", "accepted-archive-sha"),
        ("file-hash-bypass", "QuestGameArchiveDelivery.cs", "if (sha.Finish() != file.sha256)", "if (file.sha256 == null)", "accepted-file-sha"),
        ("content-proof-bypass", "QuestGameArchiveDelivery.cs", "if (!verified[file.path].Equals(QuestContentHash.Identity(Target(root, file.path))))", "if (verified.Count == 0)", "accepted-completion-operation-identity"),
        ("archive-proof-bypass", "QuestGameArchiveDelivery.cs", "if (manifest.inputKey != input || manifest.archiveSha256 != hash || !identity.Equals(QuestContentHash.Identity(Path)))", "if (input == null)", "accepted-archive-operation-identity"),
        ("hash-snapshot-bypass", "QuestGameContent.cs", "if (processed != total || !before.Equals(QuestContentHash.Identity(path)))", "if (processed != total)", "accepted-hash-final-callback-identity"),
        ("unconfigured-fallback", "QuestContentHash.cs", 'if (!native) throw new InvalidDataException("Quest native content verification was not configured before its worker.");', "/* forbidden fallback mutation */", "accepted-unconfigured-native"),
        ("native-abi-drift", "content_hash.cpp", "int ghvr_content_hash_abi() { return 1; }", "int ghvr_content_hash_abi() { return 2; }", "ABI is incompatible"),
        ("native-digest-corruption", "content_hash.cpp", "0x428a2f98,0x71374491", "0x428a2f99,0x71374491", "self-test failed"),
        ("native-ctime-ignored", "content_hash.cpp", "identity->changed_seconds = status.st_ctim.tv_sec; identity->changed_nanoseconds = status.st_ctim.tv_nsec;", "identity->changed_seconds = 0; identity->changed_nanoseconds = 0;", "accepted-hash-final-restored-mtime"),
        ("receipt-key-source-build", "QuestGameContent.Delivery.cs", "writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, manifest.archive);",
         "writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, manifest.archive); WriteReceiptString(writer, manifest.inputKey);", "receipt-pc-native-format-and-key"),
        ("receipt-key-zip-packaging", "QuestGameContent.Delivery.cs", "writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, manifest.archive);",
         "writer.Write(InstallationReceiptSchema); WriteReceiptString(writer, manifest.archive); WriteReceiptString(writer, manifest.archiveSha256);", "receipt-pc-native-format-and-key"),
        ("receipt-stat-bypass", "QuestGameContent.Delivery.cs", "return saved.Identity.Equals(QuestContentHash.Identity(target));", "return true;", "receipt-detect-changed-metadata"),
        ("receipt-warm-metadata-scan", "QuestGameContent.Delivery.cs", "if (checkMetadata)", "if (true)", "receipt-pc-native-format-and-key"),
        ("receipt-checksum-bypass", "QuestGameContent.Delivery.cs", "if (checksum[i] != bytes[payload + i]) return null;", "if (payload < 0) return null;", "receipt-invalid-checksum"),
        ("receipt-warm-byte-scan", "QuestGameContent.Delivery.cs", "if (existing != null) return existing;",
         "if (existing != null) { Hash(Target(root, manifest.files[0].path)); return existing; }", "receipt-warm-no-content-reads"),
        ("receipt-namespace-bypass", "QuestGameContent.Delivery.cs", "if (receipt == null || receipt.ContentKey != key) return null;",
         "if (receipt == null) return null;", "receipt-archive-namespace"),
    )
    cases = [("managed", sources, native_source, False, ""), ("native", sources, native_source, True, "")]
    for name, filename, before, after, expected in mutations:
        modified, cpp = dict(sources), native_source
        original = cpp if filename == "content_hash.cpp" else modified[filename]
        if original.count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        changed = original.replace(before, after)
        if filename == "content_hash.cpp":
            cpp = changed
        else:
            modified[filename] = changed
        cases.append((name, modified, cpp, True, expected))
    receipt = {"schema": 1, "sourceHashes": {name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()},
               "nativeHashes": {name: hashlib.sha256((native / name).read_bytes()).hexdigest() for name in ("content_hash.cpp", "content_hash.h")},
               "boundary": "real managed delivery and native host ABI; not Android storage or rendering proof", "cases": []}
    print("Quest content delivery evidence: " + str(run), flush=True)
    native_case = None
    for name, modified, cpp, use_native, expected in cases:
        case = run / name; case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestContentDelivery.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, text in modified.items():
            (case / filename).write_text(text)
        (case / "content_hash.cpp").write_text(cpp)
        shutil.copy2(native / "content_hash.h", case / "content_hash.h")
        library = case / "libghvr_quest_passthrough.so"
        subprocess.run([compiler, "-std=c++17", "-O2", "-Wall", "-Wextra", "-Werror", "-shared", "-fPIC",
                        str(case / "content_hash.cpp"), "-o", str(library)], check=True)
        env = dict(os.environ, LD_LIBRARY_PATH=str(case))
        command = [dotnet, "run", "--project", str(harness / "QuestContentDelivery.Tests.csproj"), "--configuration", "Release",
                   "--property:RuntimeSource=" + str(case), "--property:NativeHash=" + str(use_native).lower(), "--", str(case / "files")]
        result = subprocess.run(command, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (case / "console.log").write_text(result.stdout)
        passed = result.returncode == 0 and "PASS Quest content delivery:" in result.stdout if not expected else result.returncode != 0 and expected in result.stdout
        receipt["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed,
                                 "nativeSha256": hashlib.sha256(library.read_bytes()).hexdigest()})
        if name == "native" and passed:
            receipt["nativeVectors"] = native_vectors(library)
            native_case = case
        (run / "results.json").write_text(json.dumps(receipt, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        print("PASS rejected " + name + " at " + expected if expected else next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest content delivery:")), flush=True)
        # Real data is reproducible; retain compact source/log/receipt evidence.
        shutil.rmtree(case / "files")
    if native_case is None:
        raise RuntimeError("Native baseline was not exercised.")
    dll = native_case / "fixture/bin/Release/net8.0/QuestContentDelivery.Tests.dll"
    missing = subprocess.run([dotnet, str(dll), str(run / "missing-native-files"), "expect-native-unavailable"],
                             env=dict(os.environ, LD_LIBRARY_PATH=""), text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=30)
    (run / "missing-native.log").write_text(missing.stdout)
    passed = missing.returncode == 0 and "PASS native unavailable fails before workers" in missing.stdout
    receipt["cases"].append({"name": "missing-native-plugin", "exitCode": missing.returncode, "passed": passed})
    (run / "results.json").write_text(json.dumps(receipt, indent=2) + "\n")
    if not passed:
        raise SystemExit("FAIL missing native plugin; see " + str(run / "missing-native.log"))
    print("PASS content delivery and " + str(len(mutations) + 1) + " defect controls", flush=True)


if __name__ == "__main__":
    main()
