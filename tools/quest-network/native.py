"""Build the pinned, interoperable original Photon Voice Opus native boundary.

No Photon SDK, app configuration or proprietary game file is downloaded. The
codec uses Xiph's BSD source; the four CTL wrappers are our platform ABI seam.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import urllib.request

OPUS_VERSION = "1.5.2"
OPUS_SHA256 = "65c1d2f78b9f2fb20082c38cbe47c951ad5839345876e46941612ee87f9a7ce1"
OPUS_URL = f"https://downloads.xiph.org/releases/opus/opus-{OPUS_VERSION}.tar.gz"
CTL_EXPORTS = tuple(f"quest_opus_{kind}_ctl_{operation}"
                    for kind in ("encoder", "decoder") for operation in ("get", "set"))
ORIGINAL_EXPORTS = ("opus_encoder_get_size", "opus_encoder_init", "opus_get_version_string",
                    "opus_encode", "opus_encode_float", "opus_decoder_get_size", "opus_decoder_init",
                    "opus_decode", "opus_decode_float", "opus_packet_get_bandwidth",
                    "opus_packet_get_nb_channels", "opus_strerror")


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def cmake_command() -> str:
    try:
        import cmake
        found = Path(cmake.CMAKE_BIN_DIR) / ("cmake.exe" if os.name == "nt" else "cmake")
    except ImportError as error:
        raise RuntimeError("The Quest builder venv requires cmake==3.31.6 for its voice codec.") from error
    if not found.is_file():
        raise RuntimeError("Installed CMake does not contain its executable.")
    version = subprocess.check_output([str(found), "--version"], text=True).splitlines()[0]
    if version != "cmake version 3.31.6":
        raise RuntimeError("The Quest voice recipe requires pinned CMake 3.31.6.")
    return str(found)


def android_compiler(ndk: Path, *, platform: str | None = None) -> tuple[list[str], Path]:
    """Use the real LLVM executable; Windows batch wrappers parse shell paths."""
    platform = platform or sys.platform
    host = {"linux": "linux-x86_64", "darwin": "darwin-x86_64", "win32": "windows-x86_64"}.get(platform)
    if host is None:
        raise RuntimeError("Unsupported Android voice build host: " + platform)
    bin_root = Path(ndk) / "toolchains/llvm/prebuilt" / host / "bin"
    suffix = ".exe" if platform == "win32" else ""
    compiler, nm = bin_root / ("clang" + suffix), bin_root / ("llvm-nm" + suffix)
    if not compiler.is_file() or not nm.is_file():
        raise RuntimeError("Selected NDK lacks its ARM64 compiler/symbol auditor.")
    return [str(compiler), "--target=aarch64-linux-android29",
            "--sysroot=" + str(bin_root.parent / "sysroot")], nm


def fetch_source(cache: Path) -> Path:
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / f"opus-{OPUS_VERSION}.tar.gz"
    if not archive.exists():
        with urllib.request.urlopen(OPUS_URL, timeout=60) as response:
            content = response.read(16 * 1024 * 1024 + 1)
        if hashlib.sha256(content).hexdigest() != OPUS_SHA256:
            raise RuntimeError("Opus source archive failed its pinned checksum.")
        temporary = archive.with_suffix(".partial")
        temporary.write_bytes(content)
        os.replace(temporary, archive)
    if digest(archive) != OPUS_SHA256:
        raise RuntimeError("Cached Opus source archive failed its pinned checksum.")
    source = cache / f"opus-{OPUS_VERSION}"
    # Always recreate verified source. A preexisting edited extracted source must
    # not silently acquire the original archive's trusted checksum in a receipt.
    if source.exists():
        shutil.rmtree(source)
    with tarfile.open(archive) as package:
        members = package.getmembers()
        for item in members:
            relative = Path(item.name)
            if not relative.parts or relative.is_absolute() or ".." in relative.parts or item.issym() or item.islnk() \
                    or relative.parts[0] != source.name or not (item.isfile() or item.isdir()):
                raise RuntimeError("Opus archive contains a nonlocal source member.")
        package.extractall(cache, members=members, filter="data")
    return source


def build_network_native(output_cache: Path, ndk: Path | None, *, host_test: bool = False) -> Path:
    """Return a verified libopus_egpv.so; host_test creates a separate Linux test build."""
    output_cache = Path(output_cache).resolve()
    bridge = Path(__file__).with_name("opus_bridge.c")
    ndk_version = "host-test" if host_test else digest(Path(ndk) / "source.properties") if ndk is not None else "absent"
    key = hashlib.sha256((OPUS_SHA256 + digest(bridge) + digest(Path(__file__)) + ndk_version + str(host_test)).encode()).hexdigest()
    output = output_cache / "voice" / key
    output.mkdir(parents=True, exist_ok=True)
    destination = output / "libopus_egpv.so"
    receipt_path = output / "native-voice.json"
    if destination.is_file() and receipt_path.is_file():
        receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
        if receipt.get("sha256") == digest(destination) and receipt.get("inputKey") == key:
            return destination
        raise RuntimeError("Cached native voice output differs from its build receipt.")
    # Each recipe/architecture has an exclusive verified extraction. Rebuilding
    # a host test must never delete source currently compiled by an ARM64 build.
    source = fetch_source(output / "verified-source")
    # Build upstream as static PIC, then export precisely the original codec API
    # and four fixed-argument CTL wrappers from our shared plugin.
    build = output / "cmake"
    command = [cmake_command(), "-S", str(source), "-B", str(build), "-G", "Unix Makefiles",
               "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_POSITION_INDEPENDENT_CODE=ON",
               "-DBUILD_SHARED_LIBS=OFF", "-DOPUS_BUILD_TESTING=OFF",
               "-DOPUS_BUILD_PROGRAMS=OFF", "-DOPUS_INSTALL_PKG_CONFIG_MODULE=OFF"]
    if host_test:
        if sys.platform != "linux":
            raise RuntimeError("The original SDK host codec test currently requires Linux.")
        compiler_path = shutil.which("cc")
        if not compiler_path:
            raise RuntimeError("Host C compiler unavailable.")
        compiler = [compiler_path]
        nm = shutil.which("nm")
        abi = "x86_64-host-test"
    else:
        if ndk is None:
            raise RuntimeError("Original Photon Voice needs the selected Unity Android NDK.")
        ndk = Path(ndk).resolve()
        compiler, nm = android_compiler(ndk)
        command += [f"-DCMAKE_TOOLCHAIN_FILE={ndk / 'build/cmake/android.toolchain.cmake'}",
                    "-DANDROID_ABI=arm64-v8a", "-DANDROID_PLATFORM=android-29"]
        abi = "arm64-v8a"
    if os.name == "nt":
        # Use the pinned venv wheel, even if a different Ninja exists on PATH.
        try:
            import ninja as ninja_package
            from importlib.metadata import version
            ninja = Path(ninja_package.BIN_DIR) / "ninja.exe"
            if version("ninja") != "1.11.1.4" or not ninja.is_file():
                raise RuntimeError("Windows voice builds require pinned Ninja 1.11.1.4.")
        except ImportError as error:
            raise RuntimeError("Windows voice builds require the Quest build requirements in its venv.") from error
        command[command.index("Unix Makefiles")] = "Ninja"
        command += [f"-DCMAKE_MAKE_PROGRAM={ninja}"]
    with (output / "native-build.log").open("w", encoding="utf-8") as log:
        subprocess.run(command, check=True, stdout=log, stderr=subprocess.STDOUT)
        subprocess.run([cmake_command(), "--build", str(build), "--parallel", "4"],
                       check=True, stdout=log, stderr=subprocess.STDOUT)
        archive = build / "libopus.a"
        if not archive.is_file():
            raise RuntimeError("Opus CMake did not produce its expected static archive.")
        subprocess.run(compiler + ["-O2", "-fPIC", "-shared", "-fvisibility=hidden",
                        "-I", str(source / "include"), str(bridge), "-Wl,--whole-archive",
                        str(archive), "-Wl,--no-whole-archive", "-Wl,--no-undefined",
                        "-Wl,-soname,libopus_egpv.so", "-lm", "-o", str(destination)],
                       check=True, stdout=log, stderr=subprocess.STDOUT)
    header = destination.read_bytes()[:20]
    if not host_test and (header[:6] != b"\x7fELF\x02\x01" or header[18:20] != b"\xb7\x00"):
        raise RuntimeError("Native voice output is not Android ARM64 ELF.")
    symbol_output = subprocess.check_output([str(nm), "-D", "--defined-only", str(destination)], text=True)
    symbols = {line.split()[-1] for line in symbol_output.splitlines() if line.split()}
    if not set(CTL_EXPORTS + ORIGINAL_EXPORTS) <= symbols:
        raise RuntimeError("Native voice output omits original required Opus/CTL exports.")
    receipt = {"schema": 1, "inputKey": key, "abi": abi, "opusVersion": OPUS_VERSION,
               "sourceArchiveSha256": OPUS_SHA256, "bridgeSha256": digest(bridge),
               "sha256": digest(destination), "fixedCtlExports": list(CTL_EXPORTS),
               "originalExports": list(ORIGINAL_EXPORTS), "androidRuntimeValidated": False}
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    shutil.copyfile(source / "COPYING", output / "OPUS-LICENSE.txt")
    return destination


def stage(output_cache: Path, ndk: Path, android_plugin_directory: Path,
          notices_directory: Path) -> dict[str, str]:
    """Build and copy the actual codec plus its required redistribution notice.

    The root builder owns full-target dispatch and Unity plugin importer metadata.
    This function neither contacts a game service nor installs Python packages.
    """
    native = build_network_native(output_cache, ndk)
    license_source = native.with_name("OPUS-LICENSE.txt")
    if not license_source.is_file():
        raise RuntimeError("Pinned Opus build omitted its required license notice.")
    plugins = Path(android_plugin_directory).resolve()
    notices = Path(notices_directory).resolve()
    plugins.mkdir(parents=True, exist_ok=True)
    notices.mkdir(parents=True, exist_ok=True)
    library = plugins / native.name
    license_file = notices / "OPUS-LICENSE.txt"
    shutil.copyfile(native, library)
    shutil.copyfile(license_source, license_file)
    return {"library": str(library), "librarySha256": digest(library),
            "license": str(license_file), "licenseSha256": digest(license_file),
            "receipt": str(native.with_name("native-voice.json"))}
