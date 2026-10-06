"""Pinned Bionic ARM64EC Wine + official FEX behind the unchanged GHPR boundary."""
from __future__ import annotations
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess

HERE = Path(__file__).resolve().parent
LOCK = json.loads((HERE / "proton.lock.json").read_text())
BACKEND = "proton-arm64ec-fex"


def local(name):
    spec = importlib.util.spec_from_file_location("quest_proton_" + name, HERE / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _record(path, root, digest):
    return dict(path=path.relative_to(root).as_posix(), sha256=digest(path), size=path.stat().st_size)


def _native_record(path, root, digest):
    result = _record(path, root, digest)
    result["executable"] = path.name in ("libquest_proton.so", "libquest_proton_server.so")
    return result


def _native_header(layout):
    return ("/* Generated only from the SHA-bound source-owned mapping. */\n"
            "static const char *const ghpr_wine_native_map[][2] = {\n" +
            "".join("    {" + json.dumps(name) + ", " + json.dumps(target) + "},\n"
                    for name, target in sorted(layout.NATIVE_MAP.items())) + "};\n")


def qualify_cache(output: Path, receipt: dict, key: str, sources: dict, ndk_hash: str, *, digest):
    layout = local("proton_layout")
    if receipt.get("inputKey") != key or receipt.get("backend") != BACKEND or receipt.get("sources") != sources or \
            receipt.get("ndkSha256") != ndk_hash or receipt.get("wine") != LOCK["wine"] or receipt.get("fex") != LOCK["fex"]:
        raise RuntimeError("Cached Proton native receipt differs from its source/backend key.")
    records = receipt.get("artifacts")
    if not isinstance(records, list) or not 1 <= len(records) <= 8192:
        raise RuntimeError("Cached Proton artifact inventory is absent or unbounded.")
    declared = set()
    for item in records:
        if not isinstance(item, dict) or not isinstance(item.get("path"), str):
            raise RuntimeError("Cached Proton artifact inventory has an invalid path.")
        name = layout.member_path(item["path"])
        if name != item["path"] or not name.startswith(("native/", "payload/")) or name in declared:
            raise RuntimeError("Cached Proton artifact path is duplicate, noncanonical or outside its qualified tree.")
        declared.add(name)
        path = output / name
        current = output
        for part in Path(name).parts:
            current /= part
            if current.is_symlink():
                raise RuntimeError("Cached Proton artifact crosses a symlink.")
        if not path.is_file() or path.stat().st_size != item.get("size") or digest(path) != item.get("sha256"):
            raise RuntimeError("Cached Proton artifact differs from its qualified source receipt.")
    required = {"native/" + name for name in layout.NATIVE_MAP.values()}
    required |= {"native/libQuestApparance.so", "native/libquest_proton.so", "native/libquest_proton_server.so"}
    if {name for name in declared if name.startswith("native/")} != required or not {
        "payload/ApparanceWorker.exe", "payload/wine/lib/wine/aarch64-windows/ntdll.dll",
        "payload/wine/lib/wine/aarch64-windows/libarm64ecfex.dll"} <= declared:
        raise RuntimeError("Cached Proton receipt underdeclares its required runtime inventory.")
    actual = {path.relative_to(output).as_posix() for root in (output / "native", output / "payload")
              for path in root.rglob("*") if path.is_file() or path.is_symlink()}
    if actual != declared:
        raise RuntimeError("Cached Proton runtime contains unlisted or missing files.")


def build(output_cache: Path, ndk: Path, *, helpers: dict) -> tuple[Path, dict]:
    digest, fetch, tool, ndk_bin = (helpers[name] for name in ("digest", "fetch", "tool", "ndk_bin"))
    ndk = Path(ndk).resolve()
    source_names = ("proton_runtime.py", "proton_layout.py", "proton_launcher.c", "proton.lock.json",
                    "bridge.c", "worker.c", "protocol.h")
    inputs = {name: digest(HERE / name) for name in source_names}
    ndk_hash = digest(ndk / "source.properties")
    key = hashlib.sha256(json.dumps(dict(backend=BACKEND, sources=inputs, ndkSha256=ndk_hash),
                                    sort_keys=True, separators=(",", ":")).encode()).hexdigest()
    output = Path(output_cache).resolve() / "procedural-runtime-proton" / key
    output.mkdir(parents=True, exist_ok=True)
    receipt_path = output / "native-build.json"
    if receipt_path.exists():
        print("[Quest procedural] Checking cached Proton/FEX native runtime.", flush=True)
        receipt = json.loads(receipt_path.read_text())
        qualify_cache(output, receipt, key, inputs, ndk_hash, digest=digest)
        return output, receipt
    native, payload = output / "native", output / "payload"
    for directory in (native, payload):
        if directory.exists():
            shutil.rmtree(directory)
        directory.mkdir()
    downloads = Path(output_cache).resolve() / "procedural-runtime-proton/downloads"
    print("[Quest procedural] Downloading/checking pinned Android Proton and official FEX packages.", flush=True)
    wine = fetch(downloads, "proton-11.0-2-arm64ec.wcp", LOCK["wine"]["url"], LOCK["wine"]["sha256"])
    fex = fetch(downloads, "fex-emu-wine_2609.1-1~n_arm64.deb", LOCK["fex"]["url"], LOCK["fex"]["sha256"])
    if wine.stat().st_size != LOCK["wine"]["size"] or fex.stat().st_size != LOCK["fex"]["size"]:
        raise RuntimeError("Pinned Proton/FEX artifact size differs from its exact qualified release.")
    layout = local("proton_layout")
    print("[Quest procedural] Preparing native Android Wine libraries and ARM64EC data.", flush=True)
    staged = layout.stage_wine(wine, output)
    fex_record = layout.stage_fex(fex, output, helpers["ar_member"])
    if fex_record["sha256"] != LOCK["fex"]["dllSha256"]:
        raise RuntimeError("Official FEX package ARM64EC DLL differs from its qualified hash.")
    notices = payload / "licenses"
    notices.mkdir(exist_ok=True)
    for name, pinned in LOCK["licenses"].items():
        shutil.copy2(fetch(downloads, name, pinned["url"], pinned["sha256"]), notices / name)
    (notices / "runtime-source-provenance.json").write_text(json.dumps(LOCK, indent=2) + "\n")
    snapshot = output / "sources"
    snapshot.mkdir(exist_ok=True)
    for name in ("protocol.h", "bridge.c", "worker.c", "proton_launcher.c"):
        shutil.copy2(HERE / name, snapshot / name)
    (snapshot / "proton_native_map.h").write_text(_native_header(layout))
    root = ndk_bin(ndk)
    clang, linker = tool(root, "clang"), tool(root, "ld.lld")
    target = ["--target=aarch64-linux-android29", "--sysroot=" + str(root.parent / "sysroot")]
    print("[Quest procedural] Compiling the small native launcher, ABI bridge and unchanged x64 worker.", flush=True)
    with (output / "native-build.log").open("w") as log:
        def run(command):
            subprocess.run(command, check=True, stdout=log, stderr=subprocess.STDOUT)
        run([clang, *target, "-std=c11", "-O2", "-fPIC", "-shared", "-fvisibility=hidden",
             "-Wall", "-Wextra", "-Werror", "-DGHPR_PROTON_BACKEND=1",
             '-DGHPR_NATIVE_INPUT_KEY="' + key + '"', str(snapshot / "bridge.c"),
             "-Wl,--no-undefined", "-Wl,-soname,libQuestApparance.so", "-pthread",
             "-o", str(native / "libQuestApparance.so")])
        run([clang, *target, "-std=c11", "-O2", "-fPIE", "-pie", "-fvisibility=hidden",
             "-Wall", "-Wextra", "-Werror", str(snapshot / "proton_launcher.c"),
             "-Wl,--no-undefined", "-Wl,--export-dynamic", "-ldl", "-pthread",
             "-o", str(native / "libquest_proton.so")])
        definition = output / "kernel32.def"
        definition.write_text("LIBRARY kernel32.dll\nEXPORTS\n" + "\n".join(helpers["KERNEL_IMPORTS"]) + "\n")
        run([linker, "-m", "i386pep", "--shared", "--out-implib=" + str(output / "kernel32.lib"),
             str(definition), "-o", str(output / "kernel32-import-placeholder.dll")])
        run([clang, "--target=x86_64-pc-windows-msvc", "-ffreestanding", "-fno-builtin", "-fno-stack-protector",
             "-O2", "-Wall", "-Wextra", "-Werror", "-c", str(snapshot / "worker.c"), "-o", str(output / "worker.obj")])
        run([linker, "-flavor", "link", "/entry:Main", "/subsystem:console", "/nodefaultlib",
             "/out:" + str(payload / "ApparanceWorker.exe"), str(output / "worker.obj"), str(output / "kernel32.lib")])
        for name in ("libQuestApparance.so", "libquest_proton.so"):
            run([tool(root, "llvm-strip"), "--strip-debug", str(native / name)])
            helpers["_elf"](native / name, executable=name != "libQuestApparance.so")
        actual = set(subprocess.check_output([tool(root, "llvm-nm"), "-D", "--defined-only",
                                             str(native / "libQuestApparance.so")], text=True).split())
        if not set(helpers["EXPORTS"] + ("quest_apparance_configure", "quest_apparance_last_error")).issubset(actual):
            raise RuntimeError("Proton bridge does not retain all twelve original functions and configuration exports.")
        actual = set(subprocess.check_output([tool(root, "llvm-nm"), "-D", "--defined-only",
                                             str(native / "libquest_proton.so")], text=True).split())
        if not {"dladdr", "dlopen", "execv", "posix_spawn", "wine_main_preload_info", "wine_r_debug"}.issubset(actual):
            raise RuntimeError("APK native Wine launcher does not export its complete scoped layout adapter.")
    receipt = dict(schema=1, backend=BACKEND, inputKey=key, ndkSha256=ndk_hash, sources=inputs,
                   wine=LOCK["wine"], fex=LOCK["fex"], fexDll=fex_record, **staged,
                   artifacts=[_record(path, output, digest) for folder in (native, payload)
                              for path in sorted(folder.rglob("*")) if path.is_file()],
                   androidExecutionVerified=False)
    receipt_path.write_text(json.dumps(receipt, indent=2) + "\n")
    return output, receipt


def _link_or_copy(source: Path, target: Path):
    target.parent.mkdir(parents=True, exist_ok=True)
    try:
        os.link(source, target)
    except OSError:
        shutil.copy2(source, target)


def stage(output_cache: Path, ndk: Path, android_plugin_directory: Path,
          streaming_assets_directory: Path, owner_engine_dll: Path, *, helpers: dict) -> dict:
    digest = helpers["digest"]
    original = Path(owner_engine_dll).resolve()
    if original.read_bytes()[:2] != b"MZ":
        raise RuntimeError("Owner's procedural engine is not its original Windows binary.")
    output, native = build(output_cache, ndk, helpers=helpers)
    print("[Quest procedural] Auditing actual Android imports and native/ARM64EC DLL bindings.", flush=True)
    # Distinct owner/binary inputs never mutate the qualified public native tree.
    stage_key = hashlib.sha256((native["inputKey"] + digest(original)).encode()).hexdigest()
    assembled = output / "assembled" / stage_key
    if assembled.exists():
        shutil.rmtree(assembled)
    assembled.mkdir(parents=True)
    try:
        for item in native["artifacts"]:
            _link_or_copy(output / item["path"], assembled / item["path"])
        shutil.copy2(original, assembled / "payload/ApparanceEngine.dll")
        stage_manifest = dict(schema=1, backend=BACKEND, nativeDirectory="native", payloadDirectory="payload",
                              wineRoot="payload/wine", worker="payload/ApparanceWorker.exe",
                              engine="payload/ApparanceEngine.dll", launcher="native/libquest_proton.so",
                              server="native/libquest_proton_server.so", fexPath=native["fexDll"]["path"],
                              nativeLibraries=native["nativeLibraries"] + [
                                  dict(path="native/" + name, sha256=digest(assembled / "native" / name),
                                       sourceSha256=native["sources"][source], sourceMember="source-owned/" + source)
                                  for name, source in (("libQuestApparance.so", "bridge.c"),
                                                       ("libquest_proton.so", "proton_launcher.c"))],
                              provenance=dict(wine=LOCK["wine"], fex=LOCK["fex"], ndkSha256=native["ndkSha256"]),
                              prefix=dict(mode="source-owned-private", zDrive="/", architecture="win64"),
                              choices=dict(x64Emulator="libarm64ecfex.dll", synchronization="server",
                                           unixLibraryHelper=False, disabledDrivers=list(local("proton_layout").DISABLED_DRIVERS),
                                           noNtsync=True, noFsync=True, noEsync=True, fexUnixCompanion=False,
                                           bootstrapPeRoots=["wineboot.exe", "services.exe", "winedevice.exe"],
                                           optionalUnixExcluded=native["optionalUnixExcluded"]),
                              androidExecutionVerified=False)
        (assembled / "runtime.stage.json").write_text(json.dumps(stage_manifest, indent=2) + "\n")
        audit_tools = local("proton_audit")
        root = helpers["ndk_bin"](Path(ndk))
        audit = audit_tools.audit_stage(assembled, expected_provenance=stage_manifest["provenance"],
                                       system_library_directory=root.parent / "sysroot/usr/lib/aarch64-linux-android/29")
        audit_path = output / ("proton-stage-audit-" + stage_key + ".json")
        audit_json = json.dumps(audit, indent=2) + "\n"
        audit_path.write_text(audit_json)
        latest = output / "proton-stage-audit.json.partial"
        latest.write_text(audit_json)
        os.replace(latest, output / "proton-stage-audit.json")
        missing = audit.get("missing", [])
        summary = dict(schema=1, backend=BACKEND, status=audit.get("status", "failed"),
                       androidExecutionVerified=False, nativeLibraries=len(audit.get("nativeLibraries", [])),
                       nativeImportOccurrences=audit.get("importedNativeOccurrences", 0),
                       peContexts=len(audit.get("peClosure", [])), peImportOccurrences=audit.get("importedPeOccurrences", 0),
                       requiredUnixCompanions=audit.get("requiredUnixCompanions", []),
                       missing=[str(value)[:2048] for value in missing[:128]], missingCount=len(missing),
                       manifestSha256=digest(assembled / "runtime.stage.json"),
                       auditSha256=digest(output / "proton-stage-audit.json"), nativeInputKey=native["inputKey"],
                       originalEngineSha256=digest(original), choices=stage_manifest["choices"],
                       limits=dict(missingRecords=128, missingCharactersPerRecord=2048, scope="static-builder-preparation-only"))
        (output / "proton-stage-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        audit_tools.require_passed(audit)
        print("[Quest procedural] Static runtime audit passed; staging the qualified payload (Android execution pending).", flush=True)
        payload = Path(streaming_assets_directory) / "ProceduralRuntime"
        if payload.exists():
            shutil.rmtree(payload)
        shutil.copytree(assembled / "payload", payload)
        native_dir = Path(android_plugin_directory) / "arm64-v8a"
        native_dir.mkdir(parents=True, exist_ok=True)
        # Remove only this runtime's known alternate-backend files. Other engine,
        # Unity, EOS and audio plug-ins keep their independent ownership.
        for old in ("libquest_box64.so", "libquest_wineserver.so"):
            (native_dir / old).unlink(missing_ok=True)
        android_files = []
        for path in sorted((assembled / "native").glob("*.so")):
            target = native_dir / path.name
            shutil.copy2(path, target)
            android_files.append(_native_record(target, native_dir, digest))
        (payload / "proton-stage-audit.json").write_text(json.dumps(audit, indent=2) + "\n")
        (payload / "runtime.stage.json").write_text(json.dumps(stage_manifest, indent=2) + "\n")
        receipt = dict(schema=2, backend=BACKEND, native=native, wine=LOCK["wine"], fex=LOCK["fex"],
                       androidNativeFiles=android_files, protonAuditSha256=digest(payload / "proton-stage-audit.json"),
                       originalEngineSha256=digest(original), originalEngineSource="owner-provided-PC-game",
                       androidExecutionVerified=False,
                       files=[_record(path, payload, digest) for path in sorted(payload.rglob("*")) if path.is_file()])
        (payload / "runtime-manifest.json").write_text(json.dumps(receipt, indent=2) + "\n")
        return dict(backend=BACKEND, payload=str(payload), nativeDirectory=str(native_dir),
                    manifest=str(payload / "runtime-manifest.json"), manifestSha256=digest(payload / "runtime-manifest.json"),
                    androidNativeFiles=android_files, files=len(receipt["files"]))
    finally:
        # This transient source-owned audit tree retains no extra owner DLL/data
        # or 640 MB duplicate runtime after success or failure. Public verified
        # downloads/native cache and the finished target remain resumable.
        shutil.rmtree(assembled)
