"""Portable evidence for the actual host driver and staged Unity Editor sources.

This describes build inputs, not successful preparation or device execution.
The caller includes ``value_hash(capture(...))`` in its derivative build identity
and compares a second capture before accepting the player build.
"""

from pathlib import Path
import hashlib
import json
import re

from storage import BuildError, record_file


_HASH = re.compile(r"[0-9a-f]{64}")
_COMMIT = re.compile(r"(?:[0-9a-f]{40}|[0-9a-f]{64})")
_UNITY = re.compile(r"20\d{2}\.\d+\.\d+[abfp]\d+")
_EDITOR = Path("Assets/Quest/Editor")
_TEMPLATE = Path("unity/GloomhavenVR.Quest")
_DRIVER = Path("tools/quest-builder")
_COMPUTE_RECEIPT = Path("QuestCampaignEvidence/compute-reference-types.json")
_COMPUTE_SCOPE = "native-class72-ComputeShaderImporter-PPtr-types"
_SHADER_MANIFEST = Path("Assets/QuestOriginalCampaign/campaign-shaders.json")
_SHADER_ORDER_RECEIPT = Path("QuestCampaignEvidence/fragment-stereo-input-order.json")
_SHADER_ORDER_SCOPE = "native-fragment-stereo-input-order"
_SHADER_ORDER_GENERATOR = Path("tools/quest-shaders/produce.py")
_SHADER_ORDER_FOLIAGE_GUIDS = frozenset((
    "1baec85ef43ddac49b802c75062235f0", "35a4838a171d5b546bff36f271f98005",
    "cf5270adcf02aa94f84c088a5d5671fb", "717b2309850595340bf803079af38b12",
))
_TOOL_FIELDS = (
    "unityVersion", "buildDriverSha256", "key", "editorSha256", "javaSha256",
    "apksignerSha256", "ndkPropertiesSha256", "buildToolsVersion",
)


def _real_path(path, label, *, directory=False):
    path = Path(path).absolute()
    # Inspect ancestors as well: a real file reached through a linked folder
    # must not silently become an input from another checkout or private tree.
    for part in (path, *path.parents):
        if part.is_symlink() or (hasattr(part, "is_junction") and part.is_junction()):
            raise BuildError("Build provenance rejects a linked " + label + ".")
    if directory and not path.is_dir():
        raise BuildError("Build provenance requires the " + label + " directory.")
    return path


def _files(root, suffix, *, recursive):
    found = []
    try:
        for path in sorted(root.iterdir()):
            _real_path(path, "source entry")
            if path.is_dir():
                if recursive:
                    found.extend(_files(path, suffix, recursive=True))
            elif path.suffix == suffix:
                if not path.is_file():
                    raise BuildError("Build provenance requires regular source files.")
                found.append(path)
    except OSError as exc:
        raise BuildError("Build provenance could not enumerate source files.") from exc
    return sorted(found)


def _identity(inputs):
    try:
        runtime = {"sourceCommit": inputs["mod"]["commit"],
                   "modBuild": inputs["mod"]["modBuild"], "inputKey": inputs["inputKey"]}
    except (KeyError, TypeError) as exc:
        raise BuildError("Build provenance requires the frozen runtime identity.") from exc
    if (not isinstance(runtime["sourceCommit"], str) or not _COMMIT.fullmatch(runtime["sourceCommit"])
            or type(runtime["modBuild"]) is not int or runtime["modBuild"] <= 0
            or not isinstance(runtime["inputKey"], str) or not _HASH.fullmatch(runtime["inputKey"])):
        raise BuildError("Build provenance has an invalid frozen runtime identity.")
    return runtime


def _toolchain(toolchain):
    if not isinstance(toolchain, dict) or not all(field in toolchain for field in _TOOL_FIELDS[:3]):
        raise BuildError("Build provenance requires the recorded host toolchain.")
    result = {field: toolchain[field] for field in _TOOL_FIELDS if field in toolchain}
    for field, value in result.items():
        pattern = (_UNITY if field == "unityVersion" else
                   re.compile(r"\d+(?:\.\d+){1,3}(?:[-.]rc\d+)?") if field == "buildToolsVersion" else _HASH)
        if not isinstance(value, str) or not pattern.fullmatch(value):
            raise BuildError("Build provenance has an invalid toolchain fingerprint: " + field)
    return result


def _records(files, root, prefix, snapshot, snapshot_prefix, witnessed):
    result = []
    for path in files:
        relative = path.relative_to(root)
        public_path = (prefix / relative).as_posix()
        try:
            actual = record_file(path, public_path)
            witnessed.append((path, actual))
            baseline_path = _real_path(snapshot / relative, "snapshot source")
            baseline = None
            if baseline_path.exists():
                if not baseline_path.is_file():
                    raise BuildError("Build provenance snapshot source is not a regular file.")
                baseline = record_file(baseline_path, (snapshot_prefix / relative).as_posix())
            witnessed.append((baseline_path, baseline))
        except OSError as exc:
            raise BuildError("Build provenance could not read a source file.") from exc
        result.append({**actual, "snapshotPath": (snapshot_prefix / relative).as_posix(),
                       "snapshotSha256": baseline["sha256"] if baseline is not None else None,
                       "changedFromSnapshot": baseline is None or actual["sha256"] != baseline["sha256"]})
    return result


def _asset_record(project, name, witnessed):
    if (not isinstance(name, str) or not name.startswith("Assets/") or "\\" in name
            or any(part in ("", ".", "..") for part in name.split("/"))):
        raise BuildError("Build provenance compute evidence has an unsafe asset path.")
    path = _real_path(project / name, "compute evidence asset")
    if not path.is_file():
        raise BuildError("Build provenance compute evidence asset is missing.")
    row = record_file(path, name)
    witnessed.append((path, row))
    return row


def _compute_repair(project, drivers, witnessed):
    """Recognize only the exact source-proven class72 PPtr repair receipt.

    This is applied owner-byte evidence. Its false import/hardware flags remain
    explicit; capturing these bytes does not claim that Unity or Quest ran them.
    """
    path = _real_path(project / _COMPUTE_RECEIPT, "compute repair receipt")
    if not path.exists():
        witnessed.append((path, None))
        return None
    if not path.is_file() or path.stat().st_size > 1024 * 1024:
        raise BuildError("Build provenance compute repair receipt is invalid or too large.")
    try:
        evidence = record_file(path, _COMPUTE_RECEIPT.as_posix())
        witnessed.append((path, evidence))
        receipt = json.loads(path.read_text(encoding="utf-8"))
        if (not isinstance(receipt, dict) or receipt.get("schema") != 1 or receipt.get("scope") != _COMPUTE_SCOPE
                or receipt.get("applied") is not True or receipt.get("targetCount") != 13
                or receipt.get("unchangedComputeSourcesAndMetas") is not True
                or receipt.get("unchangedOtherOwnerBytes") is not True
                or receipt.get("unityImportVerified") is not False or receipt.get("hardwareVerified") is not False
                or not isinstance(receipt.get("targets"), list) or len(receipt["targets"]) != 13
                or not isinstance(receipt.get("owners"), list)
                or type(receipt.get("ownerCount")) is not int or receipt["ownerCount"] != len(receipt["owners"])
                or type(receipt.get("changedReferenceCount")) is not int or receipt["changedReferenceCount"] < 0
                or any(not isinstance(receipt.get(key), str) or not _HASH.fullmatch(receipt[key])
                       for key in ("generatorSha256", "originalIdentityManifestSha256"))):
            raise BuildError("Build provenance has unrecognized compute repair evidence.")
        generator_path = _real_path(drivers.parent / "quest-compute/references.py", "compute repair generator")
        if not generator_path.is_file():
            raise BuildError("Build provenance compute repair generator is missing.")
        generator = record_file(generator_path, "tools/quest-compute/references.py")
        witnessed.append((generator_path, generator))
        if generator["sha256"] != receipt["generatorSha256"]:
            raise BuildError("Build provenance compute repair generator differs from its receipt.")
        targets, guids, paths = [], set(), set()
        for target in receipt["targets"]:
            if (not isinstance(target, dict) or target.get("classId") != 72 or target.get("fileId") != 7200000
                    or target.get("type") != 3 or not isinstance(target.get("guid"), str)
                    or not re.fullmatch(r"[0-9a-f]{32}", target["guid"]) or target["guid"] in guids
                    or not isinstance(target.get("assetPath"), str) or not target["assetPath"].endswith(".compute")):
                raise BuildError("Build provenance compute target identity is invalid.")
            actual = _asset_record(project, target["assetPath"], witnessed)
            meta = _asset_record(project, target["assetPath"] + ".meta", witnessed)
            if (actual["path"] in paths or actual["sha256"] != target.get("sourceSha256")
                    or meta["sha256"] != target.get("metaSha256")):
                raise BuildError("Build provenance compute source/meta differs from its repair receipt.")
            guids.add(target["guid"]); paths.add(actual["path"])
            targets.append({**actual, "meta": meta, "guid": target["guid"], "fileId": 7200000, "classId": 72, "type": 3})
        owners, owner_paths = [], set()
        for owner in receipt["owners"]:
            if (not isinstance(owner, dict) or owner.get("unchangedOtherOwnerBytes") is not True
                    or not isinstance(owner.get("beforeSha256"), str) or not _HASH.fullmatch(owner["beforeSha256"])
                    or type(owner.get("changedReferenceCount")) is not int or owner["changedReferenceCount"] < 0
                    or not isinstance(owner.get("references"), list) or not owner["references"]):
                raise BuildError("Build provenance compute owner evidence is invalid.")
            for reference in owner["references"]:
                if (not isinstance(reference, dict) or reference.get("guid") not in guids
                        or reference.get("fileId") != 7200000 or reference.get("type") != 3
                        or type(reference.get("beforeType")) is not int or reference["beforeType"] not in (2, 3)
                        or type(reference.get("typeTokenOffset")) is not int or reference["typeTokenOffset"] < 0):
                    raise BuildError("Build provenance compute owner reference is invalid.")
            if owner["changedReferenceCount"] != sum(ref["beforeType"] == 2 for ref in owner["references"]):
                raise BuildError("Build provenance compute owner change count differs from its references.")
            actual = _asset_record(project, owner.get("assetPath"), witnessed)
            if actual["path"] in owner_paths or actual["path"] in paths or actual["sha256"] != owner.get("sha256"):
                raise BuildError("Build provenance compute owner differs from its applied repair receipt.")
            owner_paths.add(actual["path"])
            owners.append({**actual, "beforeSha256": owner["beforeSha256"],
                           "changedReferenceCount": owner["changedReferenceCount"], "unchangedOtherOwnerBytes": True})
        if receipt["changedReferenceCount"] != sum(owner["changedReferenceCount"] for owner in owners):
            raise BuildError("Build provenance compute total change count differs from its owners.")
        return {**evidence, "scope": _COMPUTE_SCOPE, "generator": generator,
                "originalIdentityManifestSha256": receipt["originalIdentityManifestSha256"],
                "targetCount": 13, "ownerCount": len(owners), "changedReferenceCount": receipt["changedReferenceCount"],
                "targets": sorted(targets, key=lambda row: row["path"]), "owners": sorted(owners, key=lambda row: row["path"]),
                "applied": True, "unchangedComputeSourcesAndMetas": True, "unchangedOtherOwnerBytes": True,
                "unityImportVerified": False, "hardwareVerified": False}
    except (OSError, ValueError, TypeError, KeyError) as exc:
        raise BuildError("Build provenance could not capture recognized compute repair evidence.") from exc


def _shader_manifest(project, witnessed, parsed=None):
    """Bind actual compiler/pass metadata without exporting shader contents."""
    path = _real_path(project / _SHADER_MANIFEST, "campaign shader manifest")
    if not path.exists():
        witnessed.append((path, None))
        return None
    try:
        # The complete native manifest is currently about 73 MB. Bound the JSON
        # parse while retaining the authoritative full manifest byte hash.
        if not path.is_file() or path.stat().st_size > 256 * 1024 * 1024:
            raise BuildError("Build provenance campaign shader manifest is invalid or too large.")
        record = record_file(path, _SHADER_MANIFEST.as_posix())
        witnessed.append((path, record))
        with path.open(encoding="utf-8") as stream:
            manifest = json.load(stream)
        if (not isinstance(manifest, dict) or type(manifest.get("schema")) is not int or manifest["schema"] != 1
                or manifest.get("scope") != "campaign-compiler" or not isinstance(manifest.get("shaders"), list)
                or not manifest["shaders"] or not isinstance(manifest.get("materials"), list)
                or any(type(manifest.get(key)) is not int or manifest[key] < 0 for key in (
                    "requiredShaderCount", "requiredMaterialCount", "requiredOriginalNativeAliasCount", "requiredSyntheticAliasCount"))
                or manifest["requiredShaderCount"] != len(manifest["shaders"])
                or manifest["requiredMaterialCount"] != len(manifest["materials"])):
            raise BuildError("Build provenance has unrecognized campaign shader manifest metadata.")
        guids, paths, alias_count = set(), set(), 0
        for shader in manifest["shaders"]:
            if (not isinstance(shader, dict) or not isinstance(shader.get("assetPath"), str)
                    or not shader["assetPath"].startswith("Assets/") or not shader["assetPath"].endswith(".shader")
                    or "\\" in shader["assetPath"] or any(part in ("", ".", "..") for part in shader["assetPath"].split("/"))
                    or shader["assetPath"] in paths or not isinstance(shader.get("guid"), str)
                    or not re.fullmatch(r"[0-9a-f]{32}", shader["guid"]) or shader["guid"] in guids
                    or not isinstance(shader.get("sourceSha256"), str) or not _HASH.fullmatch(shader["sourceSha256"])
                    or not isinstance(shader.get("variants"), list) or not shader["variants"]):
                raise BuildError("Build provenance campaign shader record shape is invalid.")
            paths.add(shader["assetPath"]); guids.add(shader["guid"])
            for variant in shader["variants"]:
                if (not isinstance(variant, dict) or not isinstance(variant.get("passType"), str) or not variant["passType"]
                        or any(type(variant.get(key)) is not int or variant[key] < 0 for key in ("subshader", "pass", "hardwareTier"))
                        or not isinstance(variant.get("keywords"), list)
                        or any(not isinstance(keyword, str) for keyword in variant["keywords"])):
                    raise BuildError("Build provenance campaign shader variant shape is invalid.")
            alias_count += len(shader["variants"])
        if alias_count != manifest["requiredOriginalNativeAliasCount"] + manifest["requiredSyntheticAliasCount"]:
            raise BuildError("Build provenance campaign shader alias count differs from its manifest.")
        if parsed is not None:
            parsed["manifest"] = manifest
        return record
    except (OSError, ValueError, TypeError, KeyError) as exc:
        raise BuildError("Build provenance could not capture recognized campaign shader metadata.") from exc


def _shader_order_json(project, name, witnessed, maximum=16 * 1024 * 1024):
    if (not isinstance(name, str) or not name.startswith("QuestCampaignEvidence/")
            or "\\" in name or any(part in ("", ".", "..") for part in name.split("/"))):
        raise BuildError("Build provenance shader repair has an unsafe evidence path.")
    path = _real_path(project / name, "shader input repair evidence")
    if not path.is_file() or path.stat().st_size > maximum:
        raise BuildError("Build provenance shader repair evidence is missing or too large.")
    record = record_file(path, name)
    witnessed.append((path, record))
    return json.loads(path.read_text(encoding="utf-8")), record


def _shader_order_asset(project, name, witnessed, expected, meta_hash, guid=None):
    actual = _asset_record(project, name, witnessed)
    meta = _asset_record(project, name + ".meta", witnessed)
    if actual["sha256"] != expected or meta["sha256"] != meta_hash:
        raise BuildError("Build provenance shader repair source/meta differs from its receipt.")
    if guid is not None:
        metadata = (project / (name + ".meta")).read_text(encoding="utf-8")
        if not isinstance(guid, str) or not re.fullmatch(r"[0-9a-f]{32}", guid) or not re.search(
                r"(?m)^guid: " + re.escape(guid) + r"$", metadata):
            raise BuildError("Build provenance shader repair metadata GUID differs from its native identity.")
    return {**actual, "meta": meta}


def _shader_input_repair(project, drivers, source, manifest_record, manifest, witnessed, target):
    """Bind only the witnessed 90 native Foliage fragment declaration moves.

    The receipt binds the current manifest. Its manifest marker deliberately
    carries no receipt hash, avoiding a circular hash dependency while making a
    missing applied receipt fail closed. Native compiler/driver witnesses remain
    separate from an unverified headset picture and original pixel parity.
    """
    path = _real_path(project / _SHADER_ORDER_RECEIPT, "shader input repair receipt")
    transaction = _real_path(project / "QuestCampaignEvidence/fragment-stereo-input-order.transaction",
                             "shader input repair transaction")
    if transaction.exists():
        raise BuildError("Build provenance shader input repair transaction is incomplete.")
    witnessed.append((transaction, None))
    marker = manifest.get("fragmentStereoInputOrderRepair") if manifest is not None else None
    if not path.exists():
        if marker is not None:
            raise BuildError("Build provenance applied shader input repair receipt is missing.")
        witnessed.append((path, None))
        return None
    expected_marker = {"path": _SHADER_ORDER_RECEIPT.as_posix(), "scope": _SHADER_ORDER_SCOPE, "programCount": 90}
    if target != "game" or manifest_record is None or marker != expected_marker:
        raise BuildError("Build provenance shader input repair lacks its recognized Campaign manifest marker.")
    try:
        receipt, evidence = _shader_order_json(project, _SHADER_ORDER_RECEIPT.as_posix(), witnessed, 1024 * 1024)
        if (not isinstance(receipt, dict) or type(receipt.get("schema")) is not int or receipt["schema"] != 1
                or receipt.get("scope") != _SHADER_ORDER_SCOPE or receipt.get("graphicsApi") != "Vulkan"
                or receipt.get("applied") is not True
                or type(receipt.get("programCount")) is not int or receipt["programCount"] != 90
                or type(receipt.get("changedProgramCount")) is not int or receipt["changedProgramCount"] != 90
                or receipt.get("headsetPictureVerified") is not False or receipt.get("originalPixelParityVerified") is not False
                or not isinstance(receipt.get("programs"), list) or len(receipt["programs"]) != 90):
            raise BuildError("Build provenance has unrecognized shader input repair evidence.")
        declared = receipt["manifest"]
        if (declared.get("path") != _SHADER_MANIFEST.as_posix()
                or not isinstance(declared.get("beforeSha256"), str) or not _HASH.fullmatch(declared["beforeSha256"])
                or declared.get("afterSha256") != manifest_record["sha256"]
                or declared["beforeSha256"] == declared["afterSha256"]):
            raise BuildError("Build provenance shader input repair differs from the current manifest.")
        generator_files = [_real_path(drivers.parent / "quest-shaders/produce.py", "shader input repair generator")]
        if not generator_files[0].is_file() or receipt["generator"].get("path") != _SHADER_ORDER_GENERATOR.as_posix():
            raise BuildError("Build provenance shader input repair generator is missing or unrecognized.")
        generator = _records(generator_files, drivers.parent / "quest-shaders", _SHADER_ORDER_GENERATOR.parent,
                             source / _SHADER_ORDER_GENERATOR.parent, _SHADER_ORDER_GENERATOR.parent, witnessed)[0]
        if generator["sha256"] != receipt["generator"].get("sha256"):
            raise BuildError("Build provenance shader input repair generator differs from its receipt.")
        helper_path = _real_path(drivers.parent / "quest-shaders/stereo_repair.py", "shader input repair helper")
        helper_name = Path("tools/quest-shaders/stereo_repair.py")
        if not helper_path.is_file() or receipt["repairHelper"].get("path") != helper_name.as_posix():
            raise BuildError("Build provenance shader input repair helper is missing or unrecognized.")
        helper = _records([helper_path], drivers.parent / "quest-shaders", helper_name.parent,
                          source / helper_name.parent, helper_name.parent, witnessed)[0]
        if helper["sha256"] != receipt["repairHelper"].get("sha256"):
            raise BuildError("Build provenance shader input repair helper differs from its receipt.")
        all_programs = manifest.get("programs")
        if not isinstance(all_programs, list):
            raise BuildError("Build provenance shader repair requires the actual original program manifest.")
        original = {row["assetPath"]: row for row in all_programs}
        if len(original) != len(all_programs):
            raise BuildError("Build provenance shader repair program manifest contains duplicate paths.")
        foliage_fragments = {variant["fragmentOriginalDxbcSha256"] for shader in manifest["shaders"]
                             if shader["guid"] in _SHADER_ORDER_FOLIAGE_GUIDS for variant in shader["variants"]}
        front_faces = {}
        for name, row in original.items():
            signature = row.get("originalInputSignature", [])
            faces = [item for item in signature if item.get("systemValue") == 9]
            if not faces:
                continue
            if (len(faces) != 1 or faces[0].get("semantic", "").upper() != "SV_ISFRONTFACE"
                    or row.get("originalDxbcSha256") not in foliage_fragments):
                raise BuildError("Build provenance shader repair is outside the native Foliage fragment closure.")
            front_faces[name] = row
        if len(front_faces) != 90:
            raise BuildError("Build provenance shader repair original FrontFace census differs.")
        programs, paths = [], set()
        for row in receipt["programs"]:
            name = row.get("assetPath"); native = front_faces.get(name)
            if (not isinstance(row, dict) or name in paths or native is None
                    or not isinstance(name, str) or not name.endswith(".hlsl")
                    or row.get("originalDeclarationsAndMathPreserved") is not True
                    or any(not isinstance(row.get(field), str) or not _HASH.fullmatch(row[field]) for field in (
                        "originalDxbcSha256", "originalInterfaceSha256", "beforeSha256", "afterSha256", "metaSha256", "restBytesSha256"))
                    or row["beforeSha256"] == row["afterSha256"]
                    or row["originalDxbcSha256"] != native.get("originalDxbcSha256")
                    or row["originalInterfaceSha256"] != native.get("originalInterfaceSha256")
                    or row["afterSha256"] != native.get("sourceSha256")):
                raise BuildError("Build provenance shader repair changed program identity is invalid.")
            actual = _shader_order_asset(project, name, witnessed, row["afterSha256"], row["metaSha256"])
            raw = (project / name).read_bytes()
            macro = b"    UNITY_VERTEX_OUTPUT_STEREO\n"
            rest = raw.replace(macro, b"")
            if raw.count(macro) != 1 or hashlib.sha256(rest).hexdigest() != row["restBytesSha256"]:
                raise BuildError("Build provenance shader repair changed original declarations or program math.")
            programs.append({**actual, "originalDxbcSha256": row["originalDxbcSha256"],
                "originalInterfaceSha256": row["originalInterfaceSha256"], "beforeSha256": row["beforeSha256"],
                "restBytesSha256": row["restBytesSha256"], "originalDeclarationsAndMathPreserved": True})
            paths.add(name)
        if paths != front_faces.keys():
            raise BuildError("Build provenance shader repair does not cover every native FrontFace program.")
        info = receipt["identities"]
        ledger_path = "QuestCampaignEvidence/fragment-stereo-input-order-identities.json"
        ledger, ledger_record = _shader_order_json(project, info.get("path"), witnessed)
        if (info.get("path") != ledger_path or info.get("sha256") != ledger_record["sha256"]
                or type(ledger.get("schema")) is not int or ledger["schema"] != 1
                or ledger.get("scope") != _SHADER_ORDER_SCOPE + "-identities"
                or any(type(info.get(key)) is not int or info[key] != manifest[field] for key, field in (
                    ("shaderCount", "requiredShaderCount"), ("materialCount", "requiredMaterialCount"),
                    ("originalAliasCount", "requiredOriginalNativeAliasCount")))
                or ledger.get("originalAliasCount") != manifest["requiredOriginalNativeAliasCount"]):
            raise BuildError("Build provenance shader repair unchanged identity ledger differs.")
        unchanged_counts = {}
        for kind, expected in (("shaders", {r["assetPath"]: r for r in manifest["shaders"]}),
                               ("materials", {r["assetPath"]: r for r in manifest["materials"]}),
                               ("programs", {name: r for name, r in original.items() if name not in paths})):
            values = ledger.get(kind)
            if not isinstance(values, list) or len(values) != len(expected):
                raise BuildError("Build provenance shader repair unchanged identity census differs.")
            seen = set()
            for row in values:
                name = row.get("assetPath"); native = expected.get(name)
                if (not isinstance(row, dict) or name in seen or native is None
                        or any(not isinstance(row.get(key), str) or not _HASH.fullmatch(row[key]) for key in ("sha256", "metaSha256"))
                        or kind != "programs" and row.get("guid") != native.get("guid")
                        or kind == "programs" and row["sha256"] != native.get("sourceSha256")):
                    raise BuildError("Build provenance shader repair unchanged native identity is invalid.")
                _shader_order_asset(project, name, witnessed, row["sha256"], row["metaSha256"],
                                    row.get("guid") if kind != "programs" else None)
                seen.add(name)
            unchanged_counts[kind] = len(values)
        compiler_input_info = receipt["nativeCompilerInput"]
        compiler_input, input_record = _shader_order_json(project, compiler_input_info.get("path"), witnessed)
        if (compiler_input_info.get("path") != "QuestCampaignEvidence/fragment-stereo-input-order-input.json"
                or compiler_input_info.get("sha256") != input_record["sha256"]
                or not isinstance(compiler_input.get("rewrites"), list) or len(compiler_input["rewrites"]) != 90
                or not isinstance(compiler_input.get("failures"), list) or len(compiler_input["failures"]) != 26
                or not isinstance(compiler_input.get("nativeControls"), list) or len(compiler_input["nativeControls"]) != 7):
            raise BuildError("Build provenance shader repair compiler input differs from its native witness.")
        tuple_fields = ("assetPath", "originalDxbcSha256", "originalInterfaceSha256", "beforeSha256", "afterSha256", "metaSha256")
        rewrites = {tuple(row[field] for field in tuple_fields) for row in compiler_input["rewrites"]}
        if rewrites != {tuple(row[field] for field in tuple_fields) for row in receipt["programs"]}:
            raise BuildError("Build provenance shader repair differs from the exact witnessed native includes.")
        witnesses = {"nativeCompilerInput": input_record}
        witness_data = {}
        for key, suffix, counts, flags in (
                ("nativeCompilerWitness", "witness", {"baselineRejectedCount": 26, "positiveCompilerCount": 33, "monoExactStageCount": 7},
                 {"actualNativeBundleBuilt": True}),
                ("nativeDriverWitness", "driver", {"actualNativePipelineAliasCount": 33, "actualDistinctNativePipelineCount": 33},
                 {"missingNativeEntryRejected": True})):
            info = receipt[key]
            if info.get("path") != "QuestCampaignEvidence/fragment-stereo-input-order-" + suffix + ".json":
                raise BuildError("Build provenance shader repair witness path is unrecognized.")
            data, record = _shader_order_json(project, info["path"], witnessed)
            if (info.get("sha256") != record["sha256"]
                    or type(data.get("schema")) is not int or data["schema"] != 1
                    or any(type(info.get(k)) is not int or info[k] != v or data.get(k) != v for k, v in counts.items())
                    or any(info.get(k) is not v or data.get(k) is not v for k, v in flags.items())
                    or data.get("headsetPictureVerified") is not False):
                raise BuildError("Build provenance shader repair native witness differs from its receipt.")
            witnesses[key] = {**record, **counts, **flags}
            witness_data[key] = data
        compiler = witness_data["nativeCompilerWitness"]
        driver = witness_data["nativeDriverWitness"]
        if (compiler.get("unityVersion") != "2021.3.5f1" or compiler.get("inputSha256") != input_record["sha256"]
                or compiler.get("originalDeclarationsAndMathPreserved") is not True
                or compiler.get("originalShaderAndMetaPreserved") is not True
                or compiler.get("originalPixelParityVerified") is not False
                or driver.get("graphicsApi") != "Vulkan"
                or driver.get("sourceWitnessSha256") != witnesses["nativeCompilerWitness"]["sha256"]
                or driver.get("originalWindowsPixelParityVerified") is not False):
            raise BuildError("Build provenance shader repair native witness cross-binding differs.")
        return {**evidence, "scope": _SHADER_ORDER_SCOPE, "graphicsApi": "Vulkan", "generator": generator,
            "repairHelper": helper, "applied": True,
            "manifest": {**manifest_record, "beforeSha256": declared["beforeSha256"]},
            "programCount": 90, "changedProgramCount": 90, "programs": sorted(programs, key=lambda row: row["path"]),
            "identities": {**ledger_record, "shaderCount": unchanged_counts["shaders"],
                "materialCount": unchanged_counts["materials"], "unchangedProgramCount": unchanged_counts["programs"],
                "originalAliasCount": manifest["requiredOriginalNativeAliasCount"]}, **witnesses,
            "headsetPictureVerified": False, "originalPixelParityVerified": False}
    except (OSError, ValueError, TypeError, KeyError, AttributeError) as exc:
        raise BuildError("Build provenance could not capture recognized shader input repair evidence.") from exc


def capture(inputs, project, source, driver_dir, toolchain):
    """Capture sorted stable bytes without exposing paths, accounts or secrets.

    New actual source files without a frozen counterpart have a null baseline
    and ``changedFromSnapshot=True``. Unknown/missing input roots and symlinks
    fail closed; unrelated filesystem and toolchain fields are not serialized.
    """
    runtime, tools = _identity(inputs), _toolchain(toolchain)
    project = _real_path(project, "staged project", directory=True)
    source = _real_path(source, "frozen source", directory=True)
    drivers = _real_path(driver_dir, "live build driver", directory=True)
    editor = _real_path(project / _EDITOR, "staged Editor", directory=True)
    frozen_editor = _real_path(source / _TEMPLATE / _EDITOR, "snapshot Editor", directory=True)
    frozen_drivers = _real_path(source / _DRIVER, "snapshot build driver", directory=True)
    driver_files = _files(drivers, ".py", recursive=False)
    editor_files = _files(editor, ".cs", recursive=True)
    if drivers / "builder.py" not in driver_files or not editor_files:
        raise BuildError("Build provenance requires builder.py and staged Editor source files.")
    witnessed = []
    driver_records = _records(driver_files, drivers, _DRIVER, frozen_drivers, _DRIVER, witnessed)
    editor_records = _records(editor_files, editor, _EDITOR, frozen_editor, _TEMPLATE / _EDITOR, witnessed)
    if next(row for row in driver_records if row["path"] == "tools/quest-builder/builder.py")["sha256"] != tools["buildDriverSha256"]:
        raise BuildError("Build provenance driver differs from the recorded toolchain.")
    compute_repair = _compute_repair(project, drivers, witnessed)
    shader_state = {}
    shader_manifest = _shader_manifest(project, witnessed, shader_state)
    shader_repair = _shader_input_repair(project, drivers, source, shader_manifest,
                                        shader_state.get("manifest"), witnessed, inputs.get("target"))

    # A module imported earlier or a source changed during capture is not a
    # trustworthy launch record. Recheck membership and both sets of bytes.
    if driver_files != _files(drivers, ".py", recursive=False) or editor_files != _files(editor, ".cs", recursive=True):
        raise BuildError("Build provenance source selection changed during capture.")
    for path, expected in witnessed:
        _real_path(path, "source entry")
        if expected is None:
            if path.exists():
                raise BuildError("Build provenance snapshot source appeared during capture.")
            continue
        try:
            if not path.is_file() or record_file(path, expected["path"]) != expected:
                raise BuildError("Build provenance source bytes changed during capture.")
        except OSError as exc:
            raise BuildError("Build provenance source disappeared during capture.") from exc
    result = {"schema": 1, "runtime": runtime, "toolchain": tools,
              "buildDriverModules": driver_records, "stagedEditorSources": editor_records}
    if compute_repair is not None:
        result["campaignComputeReferenceRepair"] = compute_repair
    if shader_manifest is not None:
        result["campaignShaderManifest"] = shader_manifest
    if shader_repair is not None:
        result["campaignFragmentStereoInputOrderRepair"] = shader_repair
    return result
