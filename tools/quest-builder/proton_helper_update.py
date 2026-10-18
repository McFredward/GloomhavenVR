"""One exact native helper repair without rebuilding Wine, game data or Unity.

The old runtime/payload manifests remain historical build evidence. The current
signed native-program contract and update evidence describe the two real new
ELFs. Unknown native edits retain the ordinary full preparation requirement.
"""
from __future__ import annotations
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import zipfile


BACKEND = "proton-arm64ec-fex"
PREFIX = "tools/quest-procedural-runtime/"
SOURCE_NAMES = ("proton_runtime.py", "proton_layout.py", "proton_launcher.c", "proton.lock.json",
                "bridge.c", "worker.c", "protocol.h")
PREVIOUS_SOURCE_ROWS = (
    {'path': 'tools/quest-procedural-runtime/proton_runtime.py', 'size': 17964, 'sha256': 'de8ca6e814838507592e23b99ed96c496de1733b8825a32d066da125b0ad8d79'},
    {'path': 'tools/quest-procedural-runtime/proton_layout.py', 'size': 12064, 'sha256': '656e626e5097e62c702e29e3845a833fb0ad4f776b76ad3c07677a54ce8f924c'},
    {'path': 'tools/quest-procedural-runtime/proton_launcher.c', 'size': 14839, 'sha256': 'b4b2758cbc723b945c5a200d6ff911a5dcdf507acbe64f93df317d5cd4ea116a'},
    {'path': 'tools/quest-procedural-runtime/proton.lock.json', 'size': 2383, 'sha256': '0f833bf80c23ee3d394880a11b0e332f0a7e52f02912b73cc8c39fcd4cb73f94'},
    {'path': 'tools/quest-procedural-runtime/bridge.c', 'size': 25207, 'sha256': '91957a6740d335667a2cab972421b4fa5824d4bfa9f13defee6efe6bc4a94c86'},
    {'path': 'tools/quest-procedural-runtime/worker.c', 'size': 11374, 'sha256': 'e8f27b055a2e06f6a68678f00f49a49cf42d44c2e9ceeccfd65982c273ff3d48'},
    {'path': 'tools/quest-procedural-runtime/protocol.h', 'size': 840, 'sha256': 'ff353f38e7ff9d1aabd7c48ccba93248e4764111e8a4d29649cd4435fac323a5'},
)
LEGACY_SOURCE_ROWS = (
    {'path': 'tools/quest-procedural-runtime/proton_runtime.py', 'size': 17956, 'sha256': '66c4da1a384090d0959fd8930230d6f456445c723b0be485c2b24ee750cacb15'},
    {'path': 'tools/quest-procedural-runtime/proton_layout.py', 'size': 12064, 'sha256': '656e626e5097e62c702e29e3845a833fb0ad4f776b76ad3c07677a54ce8f924c'},
    {'path': 'tools/quest-procedural-runtime/proton_launcher.c', 'size': 12961, 'sha256': '266e85b860c39deabf19407b68a511c9cf9e45c33baddc0d04727cb216892133'},
    {'path': 'tools/quest-procedural-runtime/proton.lock.json', 'size': 2383, 'sha256': '0f833bf80c23ee3d394880a11b0e332f0a7e52f02912b73cc8c39fcd4cb73f94'},
    {'path': 'tools/quest-procedural-runtime/bridge.c', 'size': 21938, 'sha256': 'b0854b7b8216d6029cdcb5ae1029acb1bb0439111db4013a10bdffbf10393339'},
    {'path': 'tools/quest-procedural-runtime/worker.c', 'size': 11374, 'sha256': 'e8f27b055a2e06f6a68678f00f49a49cf42d44c2e9ceeccfd65982c273ff3d48'},
    {'path': 'tools/quest-procedural-runtime/protocol.h', 'size': 840, 'sha256': 'ff353f38e7ff9d1aabd7c48ccba93248e4764111e8a4d29649cd4435fac323a5'},
)
CURRENT_SOURCE_ROWS = (
    {'path': 'tools/quest-procedural-runtime/proton_runtime.py', 'size': 18721, 'sha256': 'c3d237cf22279b2effd36f62762801b49949185fe5fa16b019ff08d925ae7196'},
    {'path': 'tools/quest-procedural-runtime/proton_layout.py', 'size': 12064, 'sha256': '656e626e5097e62c702e29e3845a833fb0ad4f776b76ad3c07677a54ce8f924c'},
    {'path': 'tools/quest-procedural-runtime/proton_launcher.c', 'size': 14839, 'sha256': 'b4b2758cbc723b945c5a200d6ff911a5dcdf507acbe64f93df317d5cd4ea116a'},
    {'path': 'tools/quest-procedural-runtime/proton.lock.json', 'size': 2383, 'sha256': '0f833bf80c23ee3d394880a11b0e332f0a7e52f02912b73cc8c39fcd4cb73f94'},
    {'path': 'tools/quest-procedural-runtime/bridge.c', 'size': 28569, 'sha256': 'd33d2206e43dd295f3bd10896bb509fd7f6c9975d1b83a5f9562b1423781807a'},
    {'path': 'tools/quest-procedural-runtime/worker.c', 'size': 11558, 'sha256': 'df8ba156c413a742538c99aa85e157cc22e7a7a13cfbd043c3f7ec49bbcf9269'},
    {'path': 'tools/quest-procedural-runtime/protocol.h', 'size': 840, 'sha256': 'ff353f38e7ff9d1aabd7c48ccba93248e4764111e8a4d29649cd4435fac323a5'},
)
SCOPE = "proton-reentry-helper-update-v2"
NATIVE_NAMES = ("libQuestApparance.so", "libquest_proton.so")
CONTRACT_MEMBER = "assets/Quest/procedural-native.json"


def changed_rows(current, previous, backend):
    """Admit only exact reviewed snapshots; unknown native edits need preparation."""
    if backend != BACKEND:
        return set()
    paths = {row["path"] for row in CURRENT_SOURCE_ROWS}
    selected = lambda rows: sorted((row for row in rows if row.get("path") in paths), key=lambda row: row["path"])
    now, before = selected(current), selected(previous)
    if now != sorted(CURRENT_SOURCE_ROWS, key=lambda row: row["path"]):
        return set()
    for known in (PREVIOUS_SOURCE_ROWS, LEGACY_SOURCE_ROWS):
        if before == sorted(known, key=lambda row: row["path"]):
            old = {row["path"]: row for row in before}
            return {row["path"] for row in now if row != old.get(row["path"])}
    return set()


def _load(path, name):
    spec = importlib.util.spec_from_file_location("quest_helper_update_" + name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _read_native_contract(builder, apk):
    with zipfile.ZipFile(apk) as archive:
        if archive.namelist().count(CONTRACT_MEMBER) != 1:
            raise builder.BuildError("The source APK lacks one unambiguous procedural native contract.")
        info = archive.getinfo(CONTRACT_MEMBER)
        if info.file_size > 256 * 1024:
            raise builder.BuildError("The procedural native contract exceeds its bounded metadata scope.")
        document = json.loads(archive.read(info))
        rows = builder.native_plugins.validate(document, backend=BACKEND)
        selected = {}
        for name in NATIVE_NAMES:
            matches = [row for row in rows if row["path"] == builder.native_plugins.PREFIX + name]
            member = "lib/arm64-v8a/" + name
            if len(matches) != 1 or archive.namelist().count(member) != 1:
                raise builder.BuildError("The source APK lacks its declared native helper: " + name)
            row = matches[0]
            info = archive.getinfo(member)
            if not 64 <= info.file_size <= 4 * 1024 * 1024 or info.file_size != row["size"]:
                raise builder.BuildError("The source APK native helper size differs from its signed contract: " + name)
            if hashlib.sha256(archive.read(info)).hexdigest() != row["sha256"]:
                raise builder.BuildError("The source APK native helper differs from its signed contract: " + name)
            selected[name] = copy.deepcopy(row)
    return document, selected


def _compile(builder, source, output, tools):
    source = Path(source)
    rows, bodies = [], {}
    for name in SOURCE_NAMES:
        path = source / PREFIX / name
        if path.is_symlink() or not path.is_file() or path.stat().st_size > 1024 * 1024:
            raise builder.BuildError("The native helper source snapshot is absent or not a bounded ordinary file: " + name)
        body = path.read_bytes()
        bodies[name] = body
        rows.append({"path": PREFIX + name, "size": len(body), "sha256": hashlib.sha256(body).hexdigest()})
    selected = [row for row in rows if row["path"] in {r["path"] for r in CURRENT_SOURCE_ROWS}]
    if sorted(selected, key=lambda row: row["path"]) != sorted(CURRENT_SOURCE_ROWS, key=lambda row: row["path"]):
        raise builder.BuildError("The compact native helper repair differs from its exact reviewed source revision.")
    ndk = Path(tools["androidNdk"]).resolve()
    ndk_hash = builder.digest(ndk / "source.properties")
    native_key = builder.value_hash({"backend": BACKEND, "sources": {Path(r["path"]).name: r["sha256"] for r in rows},
                                     "ndkSha256": ndk_hash})
    key = builder.value_hash({"scope": SCOPE, "nativeInputKey": native_key})
    cache = Path(output) / "updates/native-helpers" / key
    proof = cache / "native-helper-build.json"

    def build_helpers():
        snapshot = cache / "sources"
        snapshot.mkdir(parents=True, exist_ok=True)
        for name, body in bodies.items():
            (snapshot / name).write_bytes(body)
        runtime = _load(source / PREFIX / "runtime.py", "runtime")
        proton = _load(snapshot / "proton_runtime.py", "proton")
        layout = proton.local("proton_layout")
        (snapshot / "proton_native_map.h").write_text(proton._native_header(layout), encoding="utf-8")
        compiler_root = runtime.ndk_bin(ndk)
        clang = runtime.tool(compiler_root, "clang")
        target = ["--target=aarch64-linux-android29", "--sysroot=" + str(compiler_root.parent / "sysroot")]
        common = [clang, *target, "-std=c11", "-O2", "-fvisibility=hidden", "-Wall", "-Wextra", "-Werror"]
        worker = cache / "ApparanceWorker.exe"
        linker = runtime.tool(compiler_root, "ld.lld")
        definition = snapshot / "kernel32.def"
        definition.write_text("LIBRARY kernel32.dll\nEXPORTS\n" + "\n".join(runtime.KERNEL_IMPORTS) + "\n", encoding="utf-8")
        worker_commands = [
            [linker, "-m", "i386pep", "--shared", "--out-implib=" + str(cache / "kernel32.lib"),
             str(definition), "-o", str(cache / "kernel32-import-placeholder.dll")],
            [clang, "--target=x86_64-pc-windows-msvc", "-ffreestanding", "-fno-builtin", "-fno-stack-protector",
             "-O2", "-Wall", "-Wextra", "-Werror", "-c", str(snapshot / "worker.c"), "-o", str(cache / "worker.obj")],
            [linker, "-flavor", "link", "/entry:Main", "/subsystem:console", "/nodefaultlib",
             "/out:" + str(worker), str(cache / "worker.obj"), str(cache / "kernel32.lib")],
        ]
        for index, arguments in enumerate(worker_commands):
            builder.build_progress.event("update-proton-helpers", index, 9, "tasks", Path(arguments[0]).name,
                                         operation="update-art")
            builder.command(arguments, cache / ("worker-" + str(index + 1) + ".log"))
        (snapshot / "quest_worker_payload.h").write_text(proton.worker_header(worker), encoding="utf-8")
        commands = [
            common + ["-fPIC", "-shared", "-DGHPR_PROTON_BACKEND=1", "-DGHPR_EMBEDDED_WORKER=1", '-DGHPR_NATIVE_INPUT_KEY="' + native_key + '"',
                      str(snapshot / "bridge.c"), "-Wl,--no-undefined", "-Wl,-soname,libQuestApparance.so", "-ldl", "-pthread",
                      "-o", str(cache / NATIVE_NAMES[0])],
            common + ["-fPIE", "-pie", str(snapshot / "proton_launcher.c"), "-Wl,--no-undefined", "-Wl,--export-dynamic",
                      "-ldl", "-pthread", "-o", str(cache / NATIVE_NAMES[1])],
        ]
        commands += [[runtime.tool(compiler_root, "llvm-strip"), "--strip-debug", str(cache / name)] for name in NATIVE_NAMES]
        exports = {}
        for index, arguments in enumerate(commands):
            builder.build_progress.event("update-proton-helpers", index + 3, 9, "tasks", Path(arguments[0]).name,
                                         operation="update-art")
            builder.command(arguments, cache / ("compile-" + str(index + 1) + ".log"))
        for index, name in enumerate(NATIVE_NAMES):
            path = cache / name
            runtime._elf(path, executable=name == "libquest_proton.so")
            arguments = [runtime.tool(compiler_root, "llvm-nm"), "-D", "--defined-only", str(path)]
            listing = builder.command(arguments, cache / (name + ".symbols.log"))
            exports[name] = sorted({line.split()[-1] for line in listing.splitlines() if line.split()})
            builder.build_progress.event("update-proton-helpers", index + 8, 9, "tasks", name,
                                         operation="update-art")
        required = set(runtime.EXPORTS) | {"quest_apparance_configure", "quest_apparance_last_error", "realpath"}
        if not required.issubset(exports[NATIVE_NAMES[0]]) or not {
                "dladdr", "dlopen", "execv", "posix_spawn", "wine_main_preload_info", "wine_r_debug"}.issubset(exports[NATIVE_NAMES[1]]):
            raise builder.BuildError("The repaired native helpers lack their required original ABI/layout exports.")
        evidence = {"schema": 1, "scope": SCOPE, "backend": BACKEND,
                    "nativeInputKey": native_key, "sourceFiles": rows, "ndkSha256": ndk_hash,
                    "nativeFiles": [builder.record_file(cache / name, builder.native_plugins.PREFIX + name) for name in NATIVE_NAMES],
                    "exports": exports, "embeddedWorker": builder.record_file(worker, "ApparanceWorker.exe"),
                    "androidDeviceExecutionVerified": False,
                    "wineBinaryChanged": False, "winePayloadChanged": False, "originalGameAssetsRebuilt": False}
        builder.write_json(proof, evidence)
        return [proof, worker, *(cache / name for name in NATIVE_NAMES)], {"nativeInputKey": native_key}

    builder.Stages(output).run("update-proton-helpers", key, build_helpers)
    evidence = json.loads(proof.read_text(encoding="utf-8"))
    if (evidence.get("scope") != SCOPE or evidence.get("nativeInputKey") != native_key
            or evidence.get("sourceFiles") != rows or evidence.get("ndkSha256") != ndk_hash):
        raise builder.BuildError("The retained native helper compiler receipt differs from the selected source/NDK.")
    return cache, evidence


def produce(builder, source, output, tools, base_apk, root, core, replacements):
    """Replace the two small ELFs; the bridge carries its source-built worker PE."""
    document, previous = _read_native_contract(builder, base_apk)
    cache, evidence = _compile(builder, source, output, tools)
    evidence = dict(evidence, previousNativeFiles=[previous[name] for name in NATIVE_NAMES])
    updated = copy.deepcopy(document)
    by_path = {row["path"]: row for row in evidence["nativeFiles"]}
    updated["files"] = [copy.deepcopy(by_path.get(row["path"], row)) for row in document["files"]]
    updated["helperUpdate"] = evidence
    builder.native_plugins.validate(updated, backend=BACKEND)
    result = dict(replacements)
    for name in NATIVE_NAMES:
        result["lib/arm64-v8a/" + name] = (cache / name).read_bytes()
    result[CONTRACT_MEMBER] = builder.canonical(updated)
    result.update(core.patch_resource_json(base_apk, {"quest-procedural-native": updated}, replacements=result))
    builder.write_json(Path(root) / "native-helper-update.json", evidence)
    return result, evidence
