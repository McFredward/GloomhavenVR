#!/usr/bin/env python3
"""Build the small Unity-owned-session passthrough bridge for Android ARM64."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import urllib.request

HEADERS = {
    "openxr.h": "17e8e334a7ad0c6933b31143330d4771010e393df5d5d9d08566eb15bb171a15",
    "openxr_platform_defines.h": "a458ba5777415f2de518fdcdcc87fcf8651ba0468c9f69a101378672aad80946",
}
TAG = "release-1.1.63"


def fetch_headers(cache):
    folder = cache / "openxr"
    folder.mkdir(parents=True, exist_ok=True)
    for name, expected in HEADERS.items():
        path = folder / name
        if not path.exists():
            url = f"https://raw.githubusercontent.com/KhronosGroup/OpenXR-SDK/{TAG}/include/openxr/{name}"
            with urllib.request.urlopen(url, timeout=60) as response:
                contents = response.read()
            if hashlib.sha256(contents).hexdigest() != expected:
                raise RuntimeError(f"OpenXR header checksum mismatch: {name}")
            temporary = path.with_suffix(".partial")
            temporary.write_bytes(contents)
            os.replace(temporary, path)
        if hashlib.sha256(path.read_bytes()).hexdigest() != expected:
            raise RuntimeError(f"Cached OpenXR header checksum mismatch: {name}")
    return cache


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ndk", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--cache", type=Path, required=True)
    args = parser.parse_args()
    include = fetch_headers(args.cache.resolve())
    hosts = {"linux": "linux-x86_64", "darwin": "darwin-x86_64", "win32": "windows-x86_64"}
    host = hosts.get(sys.platform)
    if not host:
        raise RuntimeError(f"Unsupported NDK build host: {sys.platform}")
    compiler = args.ndk / "toolchains/llvm/prebuilt" / host / "bin/aarch64-linux-android29-clang++"
    if sys.platform == "win32":
        compiler = compiler.with_suffix(".cmd")
    if not compiler.is_file():
        raise RuntimeError(f"Android ARM64 compiler is missing: {compiler}")
    native = Path(__file__).resolve().parents[1] / "tools/quest-native"
    source = native / "passthrough.cpp"
    hash_source = native / "content_hash.cpp"
    sources = [source, hash_source, native / "content_hash.h"]
    output = args.output.resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_suffix(".partial.so")
    subprocess.run([str(compiler), "-std=c++17", "-O2", "-fPIC", "-shared", "-fvisibility=hidden",
                    "-static-libstdc++", "-Wl,--no-undefined", "-Wl,-soname,libghvr_quest_passthrough.so",
                    "-I", str(include), str(source), str(hash_source), "-o", str(temporary)], check=True)
    header = temporary.read_bytes()[:20]
    if header[:6] != b"\x7fELF\x02\x01" or header[18:20] != b"\xb7\x00":
        raise RuntimeError("Native output is not a little-endian ARM64 ELF library")
    os.replace(temporary, output)
    receipt = {"schema": 1, "abi": "arm64-v8a", "headerTag": TAG,
               "sourceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
               "sources": {item.name: hashlib.sha256(item.read_bytes()).hexdigest() for item in sources},
               "contentHashAbi": 1,
               "sha256": hashlib.sha256(output.read_bytes()).hexdigest()}
    output.with_suffix(".build.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print("Quest passthrough bridge built: arm64-v8a, pinned OpenXR headers.")


if __name__ == "__main__":
    try:
        main()
    except (OSError, RuntimeError, subprocess.CalledProcessError) as exc:
        print(f"Quest native build failed: {exc}", file=sys.stderr)
        sys.exit(1)
