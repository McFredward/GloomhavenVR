"""Prepare original-derived environment art only from the owner's PC snapshot.

Source releases contain the extraction/simplification recipe and authored shaders.
Every native bundle and derivative tier has its own durable completion receipt;
changing unrelated mod code never reruns those producers. Unity compilation is a
separate consumer and is not implied by these native geometry receipts.
"""
import hashlib
import importlib.util
import json
from pathlib import Path
import re

from storage import BuildError, _ordinary_owned, digest, record_file, value_hash, verify_files, write_json, build_progress

ROOT = "Assets/Bundle/EnvironmentMeshes/"
SOURCE_ROOT = "StreamingAssets/aa/StandaloneWindows64/"
PCG_ROOT = SOURCE_ROOT + "pcg_databases_assets_assets/pcg/"
PRODUCERS = ("tools/environment-mesh/export-native.py", "scripts/generate-environment-meshes.py")
ORIGINS = "quest-owned-sources.json"
ASSOCIATION = "owned-PC-original-static-environment-GHEM1"
KEY = re.compile(r"[0-9a-f]{24}")
HASH = re.compile(r"[0-9a-f]{64}")


def _helper(path, function):
    spec = importlib.util.spec_from_file_location("quest_owned_environment_" + function, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    if not callable(getattr(module, function, None)):
        raise BuildError("Current environment producer API changed; review " + path.name + ".")
    return module


def _source_rows(game_info):
    rows = [row for row in game_info["files"] if row["path"].startswith(PCG_ROOT) and row["path"].endswith(".bundle")]
    if not rows or len(rows) > 8192 or len({row["path"] for row in rows}) != len(rows):
        raise BuildError("Owned game has no unambiguous original environment database bundles.")
    for row in rows:
        if (".." in row["path"].split("/") or "\\" in row["path"] or ":" in row["path"]
                or not HASH.fullmatch(str(row.get("sha256", ""))) or type(row.get("size")) is not int):
            raise BuildError("Owned environment bundle inventory is malformed.")
    return sorted(rows, key=lambda row: row["path"])


def _producers(source, source_files):
    known = {row["path"]: row for row in source_files}
    result = []
    for name in PRODUCERS:
        row = known.get(name)
        if row is None or not verify_files(source, [row]):
            raise BuildError("Current environment producer is absent or changed: " + name)
        result.append(row)
    return result

def plan(game_info, source_files):
    known = {row["path"]: row for row in source_files}
    if any(name not in known for name in PRODUCERS):
        raise BuildError("Current mod environment producer sources are incomplete.")
    return {"association": ASSOCIATION, "sources": _source_rows(game_info), "producers": [known[name] for name in PRODUCERS]}


def _meta(path):
    guid = hashlib.md5(("GloomhavenVR.EnvironmentMeshes/" + path.name).encode()).hexdigest()
    text = "fileFormatVersion: 2\nguid: " + guid + "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n"
    path.with_name(path.name + ".meta").write_text(text)


def _copy(path, target, expected):
    """Accept exactly the cached producer's bytes at the compiler input writer."""
    _ordinary_owned(path); _ordinary_owned(target)
    if target.exists():
        if target.is_file() and record_file(target, expected["path"]) == expected: return
        raise BuildError("Owned environment compiler input changed: " + expected["path"])
    target.parent.mkdir(parents=True, exist_ok=True)
    before = path.stat(); count = 0; hashed = hashlib.sha256()
    temporary = target.with_name(target.name + ".environment-copy")
    try:
        with path.open("rb") as original, temporary.open("wb") as copied:
            for block in iter(lambda: original.read(1048576), b""):
                if copied.write(block) != len(block): raise BuildError("Environment compiler input write was incomplete.")
                hashed.update(block); count += len(block)
        stamp = lambda s: (s.st_dev, s.st_ino, s.st_size, s.st_mtime_ns, s.st_ctime_ns)
        if stamp(before) != stamp(path.stat()) or count != expected["size"] or hashed.hexdigest() != expected["sha256"]:
            raise BuildError("Owned environment producer bytes changed while staging: " + expected["path"])
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)


def stage(source, game, authored, output, game_info, source_files):
    """Return qualified generated compiler inputs beside immutable authored art."""
    source, game, authored, output = [Path(path).resolve() for path in (source, game, authored, output)]
    if any(root == authored or root in authored.parents or authored in root.parents for root in (source, game)):
        raise BuildError("Owned environment generation must stay outside source checkout and PC game.")
    if any(root == output or root in output.parents for root in (source, game)):
        raise BuildError("Owned environment cache must stay outside source checkout and PC game.")
    producers = _producers(source, source_files)
    bundles = _source_rows(game_info)
    producer_key = value_hash({"format": 1, "association": ASSOCIATION, "producers": producers})
    native_producer_key = value_hash({"format": 1, "producer": producers[0]})
    derivative_producer_key = value_hash({"format": 1, "producer": producers[1]})
    key = value_hash({"producerKey": producer_key, "bundles": bundles})
    cache = _ordinary_owned(output / "cache/environment-meshes" / key)
    cache.mkdir(parents=True, exist_ok=True)
    owner = {"schema": 1, "owner": "Quest owned environment geometry", "key": key,
             "producerKey": producer_key, "bundles": bundles}
    marker = cache / "owner.json"
    if marker.exists() and json.loads(marker.read_text()) != owner:
        raise BuildError("Owned environment geometry cache identity differs; retained for inspection.")
    write_json(marker, owner)
    exporter = _helper(source / PRODUCERS[0], "extract_bundle")
    generator = _helper(source / PRODUCERS[1], "prepare_mesh")
    originals = {}; ambiguous = set(); counter = build_progress.Counter("prepare-items:environment-bundles", len(bundles), "items")
    for row in bundles:
        path = _ordinary_owned(game / row["path"])
        if not path.is_file() or path.stat().st_size != row["size"]:
            raise BuildError("Owned environment source differs from its snapshot: " + row["path"])
        native = _ordinary_owned(output / "cache/environment-native" / value_hash({"producer": native_producer_key, "source": row}))
        receipt_path = native / "complete.json"
        identity = {"producerKey": native_producer_key, "source": row}
        if receipt_path.is_file():
            prior = json.loads(receipt_path.read_text())
            if prior.get("identity") != identity or not verify_files(native, prior.get("files", [])):
                raise BuildError("Completed environment source output changed: " + row["path"])
            extracted = prior["extracted"]
        else:
            native.mkdir(parents=True, exist_ok=True)
            extracted = exporter.extract_bundle(path, native, game / SOURCE_ROOT, expected_sha256=row["sha256"])
            if extracted.get("format") != 1 or not isinstance(extracted.get("meshes"), list):
                raise BuildError("Current environment extractor changed its original geometry contract.")
            native_files = []
            for mesh in extracted["meshes"]:
                if not KEY.fullmatch(str(mesh.get("key", ""))) or mesh.get("file") != mesh["key"] + ".bytes":
                    raise BuildError("Environment extractor produced an unsafe geometry identity.")
                file = record_file(native / mesh["file"], mesh["file"])
                if file["sha256"] != mesh["sha256"]: raise BuildError("Environment extraction geometry witness differs.")
                native_files.append(file)
            write_json(receipt_path, {"identity": identity, "extracted": extracted, "files": native_files})
        ambiguous.update(extracted["ambiguousRejected"])
        for mesh in extracted["meshes"]:
            mesh = dict(mesh, nativePath=str(native / mesh["file"]))
            old = originals.get(mesh["key"])
            if old is not None:
                if old["sha256"] != mesh["sha256"]: ambiguous.add(mesh["key"])
                else: old["sources"].extend(mesh["sources"])
            else: originals[mesh["key"]] = mesh
        counter.add(1, Path(row["path"]).name)
    counter.finish()
    meshes = [originals[key] for key in sorted(originals) if key not in ambiguous]
    if not meshes:
        raise BuildError("Owned environment extraction produced no admissible originals; current mobile defaults require this bank.")
    prepared = cache / "prepared"
    prepared.mkdir(parents=True, exist_ok=True)
    entries = []; counter = build_progress.Counter("prepare-items:environment-meshes", len(meshes), "items")
    for mesh in meshes:
        derivative = _ordinary_owned(output / "cache/environment-geometry" / value_hash({"producer": derivative_producer_key, "meshSha256": mesh["sha256"], "key": mesh["key"]}))
        entry, _ = generator.prepare_mesh(mesh, mesh["nativePath"], derivative / "prepared", receipt_dir=derivative / "receipts", producer_key=derivative_producer_key)
        for variant in entry["variants"]:
            path = derivative / "prepared" / variant["file"]
            _copy(path, prepared / variant["file"], {"path": variant["file"], "sha256": variant["sha256"], "size": path.stat().st_size})
        entries.append(entry); counter.add(1, mesh["signature"]["name"])
    counter.finish()
    write_json(prepared / "index.json", {"format": 1, "entries": entries})
    index_sha = digest(prepared / "index.json")
    origins = {"schema": 1, "association": ASSOCIATION, "gameKey": game_info["key"], "indexSha256": index_sha,
               "sources": [{**row, "path": row["path"][len(SOURCE_ROOT):]} for row in bundles]}
    write_json(prepared / ORIGINS, origins)
    names = sorted({variant["file"] for entry in entries for variant in entry["variants"]} | {"index.json", ORIGINS})
    for name in names: _meta(prepared / name)
    files = [record_file(prepared / item, ROOT + item) for name in names for item in (name, name + ".meta")]
    # The original checkout may contain private desktop derivatives, but none is
    # an admissible standalone input. The builder filtered those before copying.
    generated_marker = authored / "quest-owned-environment.json"
    generated = {"schema": 1, "association": ASSOCIATION, "gameKey": game_info["key"], "producerKey": producer_key,
                 "producers": producers, "sources": bundles, "indexSha256": index_sha, "files": files}
    if generated_marker.exists() and json.loads(generated_marker.read_text()) != generated:
        raise BuildError("Generated environment compiler input owner differs.")
    for row in files: _copy(prepared / Path(row["path"]).name, authored / row["path"], row)
    write_json(generated_marker, generated)
    return generated


def validated_records(authored, generated):
    """Bind the generated bank to its owner's actual staged compiler inputs."""
    if (not isinstance(generated, dict) or generated.get("schema") != 1 or generated.get("association") != ASSOCIATION
            or not HASH.fullmatch(str(generated.get("gameKey", ""))) or not HASH.fullmatch(str(generated.get("producerKey", "")))):
        raise BuildError("Owned environment compiler-input receipt is missing or unsupported.")
    authored = Path(authored)
    if json.loads((authored / "quest-owned-environment.json").read_text()) != generated:
        raise BuildError("Environment compiler-input owner differs from the current producer.")
    files = generated.get("files")
    if not isinstance(files, list) or not files or len({row["path"] for row in files}) != len(files):
        raise BuildError("Owned environment compiler-input file records are empty or duplicated.")
    for row in files:
        if not row["path"].startswith(ROOT) or Path(row["path"]).name != row["path"][len(ROOT):] or "\\" in row["path"] or ":" in row["path"]:
            raise BuildError("Environment compiler-input receipt escapes its generated root.")
    if not verify_files(authored, files): raise BuildError("Owned generated environment compiler input changed.")
    origins = json.loads((authored / ROOT / ORIGINS).read_text())
    sources = [{**row, "path": row["path"][len(SOURCE_ROOT):]} for row in generated["sources"]]
    if (origins.get("association") != ASSOCIATION or origins.get("gameKey") != generated["gameKey"] or origins.get("sources") != sources
            or origins.get("indexSha256") != generated["indexSha256"] or digest(authored / ROOT / "index.json") != generated["indexSha256"]):
        raise BuildError("Owned environment index is not bound to its original source witnesses.")
    return files
