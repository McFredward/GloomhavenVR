"""Compile the actual Editor source/SDK and run its ZIP body on Unity's Mono.

This bounded developer fixture never starts Unity. The execution assembly only
shims Unity's native JSON icall; a separate SDK assembly compiles without it.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import struct
import subprocess
import sys
import shutil
import time
import zipfile


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    cli = argparse.ArgumentParser(description=__doc__)
    cli.add_argument("--unity", type=Path, required=True, help="Installed Unity Editor/Data root")
    cli.add_argument("--packages", type=Path, required=True, help="Actual same-version Library/ScriptAssemblies directory")
    cli.add_argument("--bundle", type=Path, required=True, help="Small existing real UnityFS LZ4 output bundle")
    cli.add_argument("--output", type=Path, required=True, help="New isolated private evidence directory")
    args = cli.parse_args()
    source = Path(__file__).resolve().parents[2] / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestStartupAddressablesBuild.cs"
    fixture = Path(__file__).with_name("Fixture.cs")
    if args.output.exists():
        cli.error("Use a new isolated proof directory; existing output is preserved.")
    args.output.mkdir(parents=True)
    output = args.output.resolve()
    bundle = args.bundle.resolve()
    if not bundle.is_file() or bundle.stat().st_size > 16 * 1024 * 1024:
        cli.error("The existing real LZ4 bundle must be an ordinary bounded file (at most16MiB).")
    header = bundle.read_bytes()[:512]
    if not header.startswith(b"UnityFS\0"):
        cli.error("The fixture input must be an actual UnityFS bundle.")
    offset = 12
    for _ in range(2):
        offset = header.index(b"\0", offset) + 1
    size, _, _, flags = struct.unpack_from(">QIII", header, offset)
    if size != bundle.stat().st_size or flags & 63 not in (2, 3):
        cli.error("The bounded native bundle must retain its actual LZ4/LZ4HC header and size.")
    before = digest(bundle)
    mono_root = args.unity / "MonoBleedingEdge"
    mono = mono_root / "bin/mono"
    compiler = mono_root / "lib/mono/4.5/mcs.exe"
    references = {p.name: p.resolve() for p in (args.unity / "Managed/UnityEngine").glob("*.dll")}
    for p in args.packages.glob("*.dll"):
        if p.name.startswith(("Unity.", "UnityEngine.UI")):
            references[p.name] = p.resolve()
    references["Newtonsoft.Json.dll"] = (args.unity / "Managed/Newtonsoft.Json.dll").resolve()
    references["netstandard.dll"] = (mono_root / "lib/mono/4.5/Facades/netstandard.dll").resolve()
    for name in ("System.IO.Compression.dll", "System.IO.Compression.FileSystem.dll"):
        references[name] = (mono_root / "lib/mono/4.5" / name).resolve()
    if not mono.is_file() or not compiler.is_file() or any(not p.is_file() for p in references.values()):
        cli.error("The installed same-version Mono/Editor/package SDK must already exist.")
    common = [str(mono), str(compiler), "-langversion:latest", "-define:GHVR_QUEST_STARTUP", *["-r:" + str(p) for _, p in sorted(references.items())]]
    with (output / "sdk-compile.log").open("w") as log:
        subprocess.run([*common, "-target:library", "-out:" + str(output / "ActualEditorSdk.dll"), str(source)], stdout=log, stderr=subprocess.STDOUT, check=True)
    with (output / "fixture-compile.log").open("w") as log:
        subprocess.run([*common, "-nowarn:0436", "-out:" + str(output / "Fixture.exe"), str(source), str(fixture)], stdout=log, stderr=subprocess.STDOUT, check=True)
    env = dict(os.environ)
    env["MONO_PATH"] = os.pathsep.join(sorted({str(path.parent) for path in references.values()}))
    helper = output / "Python helper &100%.py"
    original_helper = Path(__file__).resolve().parents[2] / "tools/quest-builder/native_content_pack.py"
    shutil.copyfile(original_helper, helper)
    env.update(GHVR_QUEST_CONTENT_PACK_PYTHON=sys.executable, GHVR_QUEST_CONTENT_PACK_HELPER=str(helper))
    result = subprocess.run([str(mono), str(output / "Fixture.exe"), str(bundle), str(output / "cases")], env=env, check=False, capture_output=True, text=True)
    (output / "fixture-run.log").write_text(result.stdout + result.stderr)
    if result.returncode:
        print(result.stdout + result.stderr, file=sys.stderr)
        raise SystemExit(result.returncode)
    if digest(bundle) != before:
        raise RuntimeError("Original native bundle changed during the isolated fixture.")
    receipt = json.loads((output / "cases/result.json").read_text())
    benchmarks = {}
    for name, method in (("optimal", zipfile.ZIP_DEFLATED), ("stored", zipfile.ZIP_STORED)):
        path = output / ("python-" + name + ".zip"); timings = []
        for round in range(4):
            started = time.monotonic()
            with zipfile.ZipFile(path, "w", compression=method, compresslevel=6 if method == zipfile.ZIP_DEFLATED else None) as archive:
                archive.write(bundle, "StreamingAssets/aa/original.bundle")
            if round:
                timings.append((time.monotonic() - started) * 1000)
        with zipfile.ZipFile(path) as archive:
            if hashlib.sha256(archive.read("StreamingAssets/aa/original.bundle")).hexdigest() != before:
                raise RuntimeError("ZIP policy changed actual native bytes.")
        benchmarks[name] = {"medianMilliseconds": sorted(timings)[1], "runsMilliseconds": timings,
                            "zipBytes": path.stat().st_size, "zipSha256": digest(path), "compressionMethod": method,
                            "nativeBytesSha256": before}
    receipt["pythonBenchmark"] = benchmarks
    receipt.update(editorSourceSha256=digest(source), fixtureSourceSha256=digest(fixture), nativeBundleSha256=before,
                   nativeInfoCompression=flags & 63, actualEditorSdkCompilationVerified=True,
                   sdkAssemblySha256=digest(output / "ActualEditorSdk.dll"), monoSha256=digest(mono),
                   sdkReferences=[{"name": name, "sha256": digest(path)} for name, path in sorted(references.items())])
    (output / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(result.stdout, end="")


if __name__ == "__main__":
    main()
