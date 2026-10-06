"""Pinned build/staging of the isolated original Apparance runtime.

Only the owner's original Windows DLL is copied. Portable Wine is a CPU-side
Win32 ABI provider; Box64 translates this single engine worker, never the game.
All executable ARM64 files are packaged in Android nativeLibraryDir. The guest
DLL/Wine files are interpreted data in the ordinary owned-content archive.
"""
from __future__ import annotations
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path, PurePosixPath
import posixpath
import shutil
import struct
import subprocess
import sys
import tarfile
import urllib.request

HERE = Path(__file__).resolve().parent
LOCK = json.loads((HERE / "upstream.lock.json").read_text())
EXPORTS = ("ApparanceInitialise", "ApparanceIsRunning", "ApparanceUpdate", "ApparanceSave",
           "ApparanceShutdown", "ApparanceCreateEntity", "ApparanceDestroyEntity", "ApparanceEntityBuild",
           "ApparancePopEntityTask", "ApparancePopEngineTask", "ApparanceUpdateAsset", "ApparanceGetNextAssetRequest")
KERNEL_IMPORTS = ("LoadLibraryA", "GetProcAddress", "GetLastError", "GetStdHandle", "ReadFile", "WriteFile",
                  "ExitProcess", "GetProcessHeap", "HeapAlloc", "HeapFree", "CreateMutexA", "WaitForSingleObject",
                  "ReleaseMutex", "SetCurrentDirectoryA")


def _resource_tools():
    # Also works when this native module is loaded directly from an arbitrary cwd.
    import importlib.util
    location = Path(__file__).resolve().parents[1] / "quest-builder/host_resources.py"
    spec = importlib.util.spec_from_file_location("quest_native_host_resources", location)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fetch(cache: Path, name: str, url: str, expected: str) -> Path:
    cache.mkdir(parents=True, exist_ok=True)
    result = cache / name
    if not result.exists():
        partial = result.with_suffix(result.suffix + ".partial")
        with urllib.request.urlopen(url, timeout=60) as source, partial.open("wb") as target:
            shutil.copyfileobj(source, target, 1024 * 1024)
        if digest(partial) != expected:
            partial.unlink()
            raise RuntimeError(f"Procedural runtime download failed pinned checksum: {name}")
        os.replace(partial, result)
    if digest(result) != expected:
        raise RuntimeError(f"Procedural runtime cache failed pinned checksum: {name}")
    return result


def _voice_tools():
    spec = importlib.util.spec_from_file_location("quest_procedural_build_dependencies", HERE.parent / "quest-network/native.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _guest_patch(source):
    spec = importlib.util.spec_from_file_location("quest_procedural_readable_guest", HERE / "box64_guest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.apply(source)


def _host_patch(source, revision):
    spec = importlib.util.spec_from_file_location("quest_procedural_host_build", HERE / "box64_host.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.apply(source, revision)


def _glibc_guest_tools():
    spec = importlib.util.spec_from_file_location("quest_procedural_guest_entry", HERE / "box64_glibc_guest.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _abi_tools(name):
    spec = importlib.util.spec_from_file_location("quest_procedural_" + name, HERE / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _wine_paths(payload):
    spec = importlib.util.spec_from_file_location("quest_procedural_wine_paths", HERE / "wine_paths.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.apply(payload)


def ndk_bin(ndk: Path) -> Path:
    host = {"linux": "linux-x86_64", "win32": "windows-x86_64", "darwin": "darwin-x86_64"}.get(sys.platform)
    result = ndk / "toolchains/llvm/prebuilt" / str(host) / "bin"
    if not result.is_dir():
        raise RuntimeError("Selected Unity NDK lacks its host LLVM toolchain.")
    return result


def tool(root: Path, name: str) -> str:
    path = root / (name + (".exe" if os.name == "nt" else ""))
    if not path.is_file():
        raise RuntimeError(f"Selected Unity NDK lacks {name}.")
    return str(path)


def _elf(path: Path, *, executable=False):
    header = path.read_bytes()[:64]
    if header[:6] != b"\x7fELF\x02\x01" or header[18:20] != b"\xb7\x00":
        raise RuntimeError(f"Native procedural artifact is not ARM64 ELF: {path.name}")
    if executable and b"/system/bin/linker64" not in path.read_bytes():
        raise RuntimeError(f"Packaged procedural executable does not use Android linker64: {path.name}")


def build(output_cache: Path, ndk: Path) -> tuple[Path, dict]:
    ndk = Path(ndk).resolve()
    key = hashlib.sha256((digest(ndk / "source.properties") + "".join(digest(path) for path in
                         (HERE / "runtime.py", HERE / "upstream.lock.json", HERE / "protocol.h", HERE / "worker.c",
                          HERE / "bridge.c", HERE / "server_launcher.c", HERE / "box64_guest.py", HERE / "box64_host.py", HERE / "box64_glibc_guest.py",
                          HERE / "box64_bionic_abi.py", HERE / "bionic_abi.c", HERE / "abi_audit.py"))).encode()).hexdigest()
    output = Path(output_cache).resolve() / "procedural-runtime" / key
    output.mkdir(parents=True, exist_ok=True)
    receipt_path = output / "native-build.json"
    if receipt_path.exists():
        receipt = json.loads(receipt_path.read_text())
        if receipt.get("inputKey") != key or any(not (output / item["path"]).is_file() or
              digest(output / item["path"]) != item["sha256"] for item in receipt["artifacts"]):
            raise RuntimeError("Cached procedural native artifacts differ from their build receipt.")
        headers = receipt.get("abiWrapperHeaders", [])
        if not headers or any(not (output / "sources/abi-headers" / item["path"]).is_file() or
                              digest(output / "sources/abi-headers" / item["path"]) != item["sha256"] for item in headers):
            raise RuntimeError("Cached procedural ABI wrapper headers differ from their build receipt.")
        return output, receipt
    upstream = LOCK["box64"]
    archive = fetch(output / "downloads", "box64-source.tar.gz", upstream["url"], upstream["sha256"])
    source = output / ("box64-" + upstream["revision"])
    if source.exists():
        shutil.rmtree(source)
    with tarfile.open(archive) as package:
        selected = []
        for item in package.getmembers():
            path = PurePosixPath(item.name)
            if path.is_absolute() or ".." in path.parts or not path.parts or path.parts[0] != source.name \
                    or not (item.isfile() or item.isdir() or item.issym()):
                raise RuntimeError("Box64 source archive contains a nonlocal member.")
            # The archive's three x64lib aliases are unused test inputs. Omit
            # symlinks so a Windows builder requires no symlink privilege.
            if item.issym():
                target = posixpath.normpath(posixpath.join(str(path.parent), item.linkname))
                if not target.startswith(source.name + "/"):
                    raise RuntimeError("Box64 source archive contains a nonlocal symlink.")
            else:
                selected.append(item)
        package.extractall(output, members=selected, filter="data")
    _guest_patch(source)
    _host_patch(source, upstream["revision"])
    _glibc_guest_tools().apply(source)
    _abi_tools("box64_bionic_abi").apply(source)
    abi_headers = _abi_tools("abi_audit").freeze_headers(source, output / "sources/abi-headers")
    tools = _voice_tools()
    cmake = tools.cmake_command()
    command = [cmake, "-S", str(source), "-B", str(output / "box64-build"),
               f"-DCMAKE_TOOLCHAIN_FILE={ndk / 'build/cmake/android.toolchain.cmake'}",
               "-DANDROID_ABI=arm64-v8a", "-DANDROID_PLATFORM=android-29", "-DANDROID=ON", "-DARM64=ON",
               "-DCMAKE_BUILD_TYPE=Release", "-DNOGIT=ON", f"-DPython3_EXECUTABLE={sys.executable}",
               f"-DPYTHON_EXECUTABLE={sys.executable}",
               # Unity's old NDK GNU linker lacks --image-base; its own lld supports it.
               "-DCMAKE_EXE_LINKER_FLAGS=-fuse-ld=lld"]
    if os.name == "nt":
        import ninja
        from importlib.metadata import version
        if version("ninja") != "1.11.1.4":
            raise RuntimeError("Quest procedural builder requires pinned Ninja 1.11.1.4.")
        command += ["-G", "Ninja", f"-DCMAKE_MAKE_PROGRAM={Path(ninja.BIN_DIR) / 'ninja.exe'}"]
    root = ndk_bin(ndk)
    # Compile immutable copies. Normal concurrent mod edits never participate in
    # this native input key, and an edited helper cannot alter a running compile.
    snapshot = output / "sources"
    snapshot.mkdir(exist_ok=True)
    for name in ("protocol.h", "bridge.c", "worker.c", "server_launcher.c"):
        shutil.copy2(HERE / name, snapshot / name)
    clang, linker = tool(root, "clang"), tool(root, "ld.lld")
    resources = _resource_tools()
    policy = resources.phase_budget("box64", output)
    with resources.timed_phase("box64", log=output / "native-build.log"), (output / "native-build.log").open("w") as log:
        def run(arguments):
            subprocess.run(arguments, check=True, stdout=log, stderr=subprocess.STDOUT)
        run(command)
        run([cmake, "--build", str(output / "box64-build"), "--parallel", str(policy["jobs"])])
        box = output / "box64-build" / "box64"
        shutil.copy2(box, output / "libquest_box64.so")
        run([clang, "--target=aarch64-linux-android29", "--sysroot=" + str(root.parent / "sysroot"),
             "-std=c11", "-O2", "-fPIC", "-shared", "-fvisibility=hidden", "-Wall", "-Wextra", "-Werror",
             '-DGHPR_NATIVE_INPUT_KEY="' + key + '"',
             str(snapshot / "bridge.c"), "-Wl,--no-undefined", "-Wl,-soname,libQuestApparance.so", "-pthread",
             "-o", str(output / "libQuestApparance.so")])
        run([clang, "--target=aarch64-linux-android29", "--sysroot=" + str(root.parent / "sysroot"), "-std=c11", "-O2", "-fPIE", "-pie", "-Wall", "-Wextra", "-Werror",
             str(snapshot / "server_launcher.c"), "-o", str(output / "libquest_wineserver.so")])
        definition = output / "kernel32.def"
        definition.write_text("LIBRARY kernel32.dll\nEXPORTS\n" + "\n".join(KERNEL_IMPORTS) + "\n")
        run([linker, "-m", "i386pep", "--shared", "--out-implib=" + str(output / "kernel32.lib"),
             str(definition), "-o", str(output / "kernel32-import-placeholder.dll")])
        run([clang, "--target=x86_64-pc-windows-msvc", "-ffreestanding", "-fno-builtin", "-fno-stack-protector",
             "-O2", "-Wall", "-Wextra", "-Werror", "-c", str(snapshot / "worker.c"), "-o", str(output / "worker.obj")])
        run([linker, "-flavor", "link", "/entry:Main", "/subsystem:console", "/nodefaultlib",
             "/out:" + str(output / "ApparanceWorker.exe"), str(output / "worker.obj"), str(output / "kernel32.lib")])
        for name in ("libQuestApparance.so", "libquest_box64.so", "libquest_wineserver.so"):
            run([tool(root, "llvm-strip"), "--strip-debug", str(output / name)])
            _elf(output / name, executable=name != "libQuestApparance.so")
        symbols = subprocess.check_output([tool(root, "llvm-nm"), "-D", "--defined-only", str(output / "libQuestApparance.so")], text=True)
        actual = {line.split()[-1] for line in symbols.splitlines() if line.split()}
        if not set(EXPORTS + ("quest_apparance_configure", "quest_apparance_last_error")).issubset(actual):
            raise RuntimeError("Original procedural native exports are incomplete.")
        box_symbols = subprocess.check_output([tool(root, "llvm-nm"), "-D", "--defined-only", str(output / "libquest_box64.so")], text=True)
        box_exports = {line.split()[-1] for line in box_symbols.splitlines() if line.split()}
        guest_entry = _glibc_guest_tools()
        guest_entry.require_exports(box_exports)
        _abi_tools("box64_bionic_abi").require_exports(box_exports)
    names = ("libQuestApparance.so", "libquest_box64.so", "libquest_wineserver.so", "ApparanceWorker.exe")
    receipt = {"schema": 1, "inputKey": key, "box64": upstream, "ndkSha256": digest(ndk / "source.properties"),
               "artifacts": [dict(path=name, sha256=digest(output / name), size=(output / name).stat().st_size) for name in names],
               "box64GuestEntryExports": sorted(guest_entry.REQUIRED_EXPORTS),
               "box64DualGuestEntrySourceSha256": guest_entry.SOURCE_SHA256,
               "abiWrapperHeaders": abi_headers,
               "box64BionicAdapterExports": sorted(_abi_tools("box64_bionic_abi").REQUIRED_EXPORTS),
               "androidExecutionVerified": False}
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n")
    # Keep the verified final artifacts and provenance, not hundreds of MB of
    # compile objects/source caches. The pinned source archive remains available.
    shutil.rmtree(output / "box64-build")
    shutil.rmtree(source)
    return output, receipt


def ar_member(path: Path, wanted: str) -> bytes:
    with path.open("rb") as source:
        if source.read(8) != b"!<arch>\n":
            raise RuntimeError("Ubuntu dependency is not a Debian ar archive.")
        while header := source.read(60):
            if len(header) != 60 or header[58:60] != b"`\n":
                raise RuntimeError("Malformed Debian dependency member header.")
            name, length = header[:16].decode().strip().rstrip("/"), int(header[48:58])
            data = source.read(length)
            if len(data) != length:
                raise RuntimeError("Truncated Debian dependency member.")
            if length % 2:
                source.read(1)
            if name.startswith(wanted):
                return data
    raise RuntimeError(f"Debian dependency lacks {wanted}.")


def stage(output_cache: Path, ndk: Path, android_plugin_directory: Path,
          streaming_assets_directory: Path, owner_engine_dll: Path) -> dict:
    """Stage verified Android code and original interpreted data into the full target."""
    output, native = build(output_cache, ndk)
    native_dir = Path(android_plugin_directory) / "arm64-v8a"
    native_dir.mkdir(parents=True, exist_ok=True)
    payload = Path(streaming_assets_directory) / "ProceduralRuntime"
    if payload.exists():
        shutil.rmtree(payload)
    payload.mkdir(parents=True, exist_ok=True)
    notices = payload / "licenses"
    notices.mkdir(exist_ok=True)
    for item in native["artifacts"]:
        destination = payload / item["path"] if item["path"].endswith(".exe") else native_dir / item["path"]
        shutil.copy2(output / item["path"], destination)
    original = Path(owner_engine_dll)
    if original.read_bytes()[:2] != b"MZ":
        raise RuntimeError("Owner's procedural engine is not its original Windows binary.")
    shutil.copy2(original, payload / "ApparanceEngine.dll")
    downloads = Path(output_cache) / "procedural-runtime/downloads"
    wine = LOCK["wine"]
    archive = fetch(downloads, "wine-9.0-amd64.tar.xz", wine["url"], wine["sha256"])
    with tarfile.open(archive) as package:
        for item in package.getmembers():
            path = PurePosixPath(item.name)
            if path.is_absolute() or ".." in path.parts or not path.parts or path.parts[0] != "wine-9.0-amd64":
                raise RuntimeError("Portable Wine archive contains a nonlocal member.")
            relative = PurePosixPath(*path.parts[1:])
            text = str(relative)
            selected = text in ("bin/wine64", "bin/wineserver") or text.startswith("lib/wine/x86_64-") \
                       or text.startswith("share/wine/")
            if not selected or not item.isfile():
                continue
            destination = payload / "wine" / str(relative)
            destination.parent.mkdir(parents=True, exist_ok=True)
            with package.extractfile(item) as source, destination.open("wb") as target:
                shutil.copyfileobj(source, target)
            destination.chmod(0o644)
    wine_path_edits = _wine_paths(payload / "wine")
    with tarfile.open(output / "downloads/box64-source.tar.gz") as package:
        license_member = package.getmember("box64-" + LOCK["box64"]["revision"] + "/LICENSE")
        (notices / "Box64-MIT.txt").write_bytes(package.extractfile(license_member).read())
    for name, item in LOCK["licenses"].items():
        shutil.copy2(fetch(downloads, name, item["url"], item["sha256"]), notices / name)
    # Every non-system guest ELF dependency is copied from a pinned Ubuntu
    # package, including soname aliases materialized as ordinary data files.
    import zstandard
    guest = payload / "wine/lib"
    guest.mkdir(parents=True, exist_ok=True)
    guest_records = []
    for name, item in LOCK["guestPackages"].items():
        archive = fetch(downloads, name + ".deb", "https://archive.ubuntu.com/ubuntu/" + item["Filename"], item["SHA256"])
        compressed = ar_member(archive, "data.tar")
        if compressed[:4] == b"\x28\xb5\x2f\xfd":
            compressed = zstandard.ZstdDecompressor().decompress(compressed, max_output_size=256 * 1024 * 1024)
        with tarfile.open(fileobj=io.BytesIO(compressed)) as package:
            members = {posixpath.normpath(item.name): item for item in package.getmembers()}
            def contents(member):
                visited = set()
                while member.issym() or member.islnk():
                    if member.name in visited:
                        raise RuntimeError("Debian dependency contains a cyclic symlink.")
                    visited.add(member.name)
                    target = posixpath.normpath(posixpath.join(posixpath.dirname(member.name), member.linkname)) if member.issym() else posixpath.normpath(member.linkname)
                    if target not in members:
                        return None
                    member = members[target]
                if not member.isfile():
                    return None
                return package.extractfile(member).read()
            for relative, member in members.items():
                if "/x86_64-linux-gnu/" in relative and ".so." in PurePosixPath(relative).name:
                    data = contents(member)
                    if data is None or data[:6] != b"\x7fELF\x02\x01" or data[18:20] != b"\x3e\x00":
                        raise RuntimeError("Pinned guest runtime dependency is not x64 ELF.")
                    destination = guest / PurePosixPath(relative).name
                    destination.write_bytes(data)
                    guest_records.append(dict(path=destination.relative_to(payload).as_posix(), sha256=hashlib.sha256(data).hexdigest(), package=name))
                elif relative.endswith("/copyright"):
                    data = contents(member)
                    if data is not None:
                        (notices / (name + "-copyright.txt")).write_bytes(data)
    fonts = payload / "fontconfig"
    fonts.mkdir(exist_ok=True)
    (fonts / "fonts.conf").write_text('<?xml version="1.0"?><!DOCTYPE fontconfig SYSTEM "fonts.dtd"><fontconfig><cachedir prefix="xdg">fontconfig</cachedir></fontconfig>\n')
    # Validate actual interpreted Wine inputs against the tables used to compile
    # this exact Android executable, after all forced guest dependencies exist.
    abi = _abi_tools("abi_audit")
    ndk_tools = ndk_bin(Path(ndk))
    audit = abi.inspect(payload / "wine", output / "libquest_box64.so", output / "sources/abi-headers",
                        native["abiWrapperHeaders"], tool(ndk_tools, "clang"),
                        ndk_tools.parent / "sysroot/usr/lib/aarch64-linux-android/29")
    (output / "guest-abi-audit.json").write_text(json.dumps(audit, indent=2) + "\n")
    abi.require_passed(audit)
    (payload / "guest-abi-audit.json").write_text(json.dumps(audit, indent=2) + "\n")
    # The guest ELF files are interpreted bytes. Box64's Android-specific recipe
    # permits readable guest input without requiring writable executable files.
    receipt = {"schema": 1, "native": native, "wine": wine, "winePathEdits": wine_path_edits, "guestPackages": LOCK["guestPackages"],
               "guestAbiAuditSha256": digest(payload / "guest-abi-audit.json"),
               "guestLibraries": guest_records, "originalEngineSha256": digest(original),
               "originalEngineSource": "owner-provided-PC-game", "androidExecutionVerified": False,
               "files": [dict(path=path.relative_to(payload).as_posix(), sha256=digest(path), size=path.stat().st_size)
                         for path in sorted(payload.rglob("*")) if path.is_file() and path.name != "runtime-manifest.json"]}
    (payload / "runtime-manifest.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return {"payload": str(payload), "nativeDirectory": str(native_dir), "manifest": str(payload / "runtime-manifest.json"),
            "manifestSha256": digest(payload / "runtime-manifest.json"), "files": len(receipt["files"])}
