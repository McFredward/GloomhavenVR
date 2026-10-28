"""Transactional repair of the three witnessed native postprocessing Layer headers.

Cold reconstruction uses full_shaders.stereo_wrapper. This narrowly recognized
adapter keeps an already imported project's other bytes and prior repair proofs.
Its compiler evidence establishes Android Vulkan programs, not headset pictures.
"""
from __future__ import annotations

import copy
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re

from storage import BuildError, _ordinary_owned, digest

SCOPE = "native-vulkan-layer-multiview-eye-macros"
MANIFEST = "Assets/QuestOriginalCampaign/campaign-shaders.json"
RECEIPT = "QuestCampaignEvidence/vertex-layer-eye-macros.json"
IDENTITIES = "QuestCampaignEvidence/vertex-layer-eye-macros-identities.json"
INPUT = "QuestCampaignEvidence/vertex-layer-eye-macros-input.json"
WITNESS = "QuestCampaignEvidence/vertex-layer-eye-macros-witness.json"
TRANSACTION = "QuestCampaignEvidence/vertex-layer-eye-macros.transaction"
MARKER = "vertexLayerEyeMacroRepair"
LEGACY_RECEIPT = "QuestStartupEvidence/legacy-post-effects.json"
LEGACY_IDENTITIES = {
    "Assets/Shader/Hidden_BrightPassFilter2.shader": ("93f40d5ea0c0a7945a5782e2dcd23833", "8a19269566f8fd692a44eb607c44f114abbc0a555d11c06995de22e2112e8dc1"),
    "Assets/Shader/Hidden_BlendForBloom.shader": ("30881e480b10c1b46a3d99ec13496f5e", "d22461e93e8d3bd6801fe12a8ea8a12632d870fd54afc4d34dca839337838abf"),
    "Assets/Shader/Hidden_BlurAndFlares.shader": ("29d4384c2ae952c4597a9d894d381163", "343d875aed4f5f221ce7d5e33df24ffc90459d9533448d7ba9edca80fa40d390"),
}
FINALPASS_GUID = "55cd61a63af3b914593da54c0365e166"
UBER_GUID = "b831b000b957e1b4d98692257f510eea"
PINS = frozenset((
    ("e05376780aeac23832d154e456d37dc27878a5654ec78c11c84e2bc1b8443bfc", "8534817b0ffa8ca14961b4926ae8cc7e4a6df3c6dd36deeaacc68eb0d8f16eef"),
    ("fcea0a84dc023896283668e97445eb90b619460c7ba46bc8c1a54a12e84b4a6e", "8534817b0ffa8ca14961b4926ae8cc7e4a6df3c6dd36deeaacc68eb0d8f16eef"),
    ("fcea0a84dc023896283668e97445eb90b619460c7ba46bc8c1a54a12e84b4a6e", "741146a3ced34e312d9020e8dccaa56fbb5f606419ea804f519697b68a5b16be"),
))
FIELDS = b"    UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX\n"
INITIALIZE = b"    UNITY_INITIALIZE_OUTPUT_STEREO_EYE_INDEX(stage_output);\n"
NEW_FIELDS = b"    #if defined(UNITY_STEREO_MULTIVIEW_ENABLED)\n    UNITY_VERTEX_OUTPUT_STEREO\n#else\n    UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX\n#endif\n"
NEW_INITIALIZE = b"    #if defined(UNITY_STEREO_MULTIVIEW_ENABLED)\n    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(stage_output);\n#else\n    UNITY_INITIALIZE_OUTPUT_STEREO_EYE_INDEX(stage_output);\n#endif\n"


def hash_bytes(raw): return hashlib.sha256(raw).hexdigest()
def encoded(value): return (json.dumps(value, sort_keys=True, indent=2) + "\n").encode()


def asset(root, name):
    if (not isinstance(name, str) or "\\" in name or ":" in name or "\x00" in name
            or PurePosixPath(name).is_absolute() or any(part in ("", ".", "..") for part in name.split("/"))):
        raise BuildError("Postprocessing repair requires confined portable paths.")
    return _ordinary_owned(root / name)


def read(root, name, bound=16 * 1024 * 1024):
    path = asset(root, name)
    if not path.is_file() or path.stat().st_size > bound:
        raise BuildError("Postprocessing repair evidence is missing or too large.")
    return json.loads(path.read_bytes())


def rewrite(raw):
    if raw.count(FIELDS) != 1 or raw.count(INITIALIZE) != 1 or NEW_FIELDS in raw or NEW_INITIALIZE in raw:
        raise BuildError("Postprocessing repair requires the exact unrepaired native Layer wrapper.")
    return raw.replace(FIELDS, NEW_FIELDS).replace(INITIALIZE, NEW_INITIALIZE)


def original_bytes(raw):
    if raw.count(NEW_FIELDS) != 1 or raw.count(NEW_INITIALIZE) != 1:
        raise BuildError("Postprocessing repaired source lacks its exact multiview guards.")
    return raw.replace(NEW_FIELDS, FIELDS).replace(NEW_INITIALIZE, INITIALIZE)


def target_rows(manifest):
    rows = [row for row in manifest["programs"] if (row.get("originalDxbcSha256"), row.get("originalInterfaceSha256")) in PINS]
    if (len(rows) != 3 or len({row["assetPath"] for row in rows}) != 3
            or {(row["originalDxbcSha256"], row["originalInterfaceSha256"]) for row in rows} != PINS):
        raise BuildError("Postprocessing native DXBC/interface census differs from the three witnessed programs.")
    owners = []
    for guid, name in ((FINALPASS_GUID, "Hidden/PostProcessing/FinalPass"), (UBER_GUID, "Hidden/PostProcessing/Uber")):
        matches = [row for row in manifest["shaders"] if row["guid"] == guid and row["originalName"] == name]
        if len(matches) != 1:
            raise BuildError("Postprocessing Layer programs lack their original Shader ownership.")
        owners.append(matches[0])
    for row in rows:
        owner = owners[1] if row["originalInterfaceSha256"].startswith("741146") else owners[0]
        if row["originalDxbcSha256"] not in {v["vertexOriginalDxbcSha256"] for v in owner["variants"]}:
            raise BuildError("Postprocessing Layer program is outside its native Shader vertex closure.")
    for row in rows:
        layers = [field for field in row["originalOutputSignature"] if field.get("systemValue") == 4]
        adapters = row.get("outputInterfaceAdapters", [])
        if (len(layers) != 1 or layers[0]["semantic"].upper() != "SV_RENDERTARGETARRAYINDEX"
                or layers[0]["componentType"] != 1 or layers[0]["mask"] != 1 or len(adapters) != 1
                or adapters[0].get("kind") != "native-vertex-layer-to-unity-framebuffer"
                or adapters[0].get("nativeOutput") != "o" + str(layers[0]["register"])):
            raise BuildError("Postprocessing native scalar uint Layer proof differs.")
    return rows, owners


def identity(root, row, guid=False):
    path = asset(root, row["assetPath"])
    value = {"assetPath": row["assetPath"], "sha256": digest(path), "metaSha256": digest(asset(root, row["assetPath"] + ".meta"))}
    if guid:
        if not re.search(r"(?m)^guid: " + re.escape(row["guid"]) + r"$", asset(root, row["assetPath"] + ".meta").read_text()):
            raise BuildError("Postprocessing repair native GUID changed.")
        value["guid"] = row["guid"]
    return value


def verify_ledger(root, ledger):
    if type(ledger.get("schema")) is not int or ledger["schema"] != 1 or ledger.get("scope") != SCOPE + "-identities":
        raise BuildError("Postprocessing unchanged identity ledger has an unrecognized schema.")
    for kind in ("shaders", "materials", "programs"):
        for row in ledger[kind]:
            if identity(root, row, kind != "programs") != row:
                raise BuildError("Unchanged original shader/material/program bytes drifted during postprocessing repair.")


def source_identity_matches(root, row, actual, kind, manifest):
    """Recognize only the three previously audited Unity import upgrades.

    Their original manifest describes the pre-import source. The retained legacy
    receipt and previous fragment ledger separately bind the physical imported
    bytes. Neither historical document is rewritten by this later repair.
    """
    if actual["sha256"] == row["sourceSha256"]:
        return True
    pin = LEGACY_IDENTITIES.get(row["assetPath"])
    if (kind != "shaders" or pin != (row.get("guid"), actual["sha256"])
            or row.get("sourceRestoration") != "retained-source-contract"
            or manifest.get("fragmentStereoInputOrderRepair") != {
                "path": "QuestCampaignEvidence/fragment-stereo-input-order.json",
                "scope": "native-fragment-stereo-input-order", "programCount": 90}):
        return False
    contract = row.get("retainedSourceContract", {})
    origin = contract.get("originalProvenance", {})
    if (contract.get("sourceSha256") != row["sourceSha256"]
            or origin.get("receipt") != LEGACY_RECEIPT
            or origin.get("receiptSha256") != digest(asset(root, LEGACY_RECEIPT))):
        return False
    native = origin.get("shader", {})
    upgrade = native.get("importUpgrade", {})
    legacy = read(root, LEGACY_RECEIPT)
    if (type(legacy.get("schema")) is not int or legacy["schema"] != 1
            or legacy.get("target") != "startup" or legacy.get("unityVersion") != "5.3.5f1"):
        return False
    matches = [value for value in legacy.get("shaders", []) if value.get("assetPath") == row["assetPath"]]
    if (matches != [native] or native.get("guid") != pin[0]
            or native.get("sourceSha256") != row["sourceSha256"]
            or native.get("metaSha256") != actual["metaSha256"]
            or upgrade.get("kind") != "UnityObjectToClipPos" or upgrade.get("sha256") != pin[1]):
        return False
    prior = read(root, "QuestCampaignEvidence/fragment-stereo-input-order.json")
    expected_phase = (read(root, RECEIPT)["manifest"]["beforeSha256"] if manifest.get(MARKER)
                      else digest(asset(root, MANIFEST)))
    if (type(prior.get("schema")) is not int or prior["schema"] != 1
            or prior.get("scope") != "native-fragment-stereo-input-order" or prior.get("graphicsApi") != "Vulkan"
            or prior.get("applied") is not True or type(prior.get("programCount")) is not int or prior["programCount"] != 90
            or prior.get("manifest", {}).get("path") != MANIFEST
            or prior["manifest"].get("afterSha256") != expected_phase):
        return False
    ledger_record = prior.get("identities", {})
    name = "QuestCampaignEvidence/fragment-stereo-input-order-identities.json"
    if ledger_record.get("path") != name or ledger_record.get("sha256") != digest(asset(root, name)):
        return False
    ledger = read(root, name)
    if (type(ledger.get("schema")) is not int or ledger["schema"] != 1
            or ledger.get("scope") != "native-fragment-stereo-input-order-identities"):
        return False
    return [value for value in ledger.get("shaders", []) if value.get("assetPath") == row["assetPath"]] == [actual]


def prior_manifest(manifest, receipt):
    prior = copy.deepcopy(manifest)
    if prior.pop(MARKER, None) != {"path": RECEIPT, "scope": SCOPE, "programCount": 3}:
        raise BuildError("Postprocessing repair has an unrecognized manifest marker.")
    changes = {row["assetPath"]: row for row in receipt["programs"]}
    for row in prior["programs"]:
        if row["assetPath"] in changes:
            if row["sourceSha256"] != changes[row["assetPath"]]["afterSha256"]:
                raise BuildError("Postprocessing repair current manifest program hash differs.")
            row["sourceSha256"] = changes[row["assetPath"]]["beforeSha256"]
    if hash_bytes(encoded(prior)) != receipt["manifest"]["beforeSha256"]:
        raise BuildError("Postprocessing repair changed other original manifest metadata.")
    return prior


def verify_completed(root, manifest=None, driver_dir=None):
    root = _ordinary_owned(root)
    if asset(root, TRANSACTION).exists():
        raise BuildError("Postprocessing repair transaction is incomplete; retain its recorded originals.")
    manifest = manifest or read(root, MANIFEST, 256 * 1024 * 1024)
    receipt = read(root, RECEIPT, 1024 * 1024)
    if (type(receipt.get("schema")) is not int or receipt["schema"] != 1 or receipt.get("scope") != SCOPE or receipt.get("applied") is not True
            or receipt.get("graphicsApi") != "Vulkan" or type(receipt.get("programCount")) is not int or receipt["programCount"] != 3
            or not isinstance(receipt.get("programs"), list) or len(receipt["programs"]) != 3
            or receipt.get("headsetPictureVerified") is not False or receipt.get("originalPixelParityVerified") is not False
            or receipt["manifest"].get("path") != MANIFEST or receipt["manifest"]["afterSha256"] != digest(asset(root, MANIFEST))):
        raise BuildError("Postprocessing repair receipt differs from the applied project.")
    driver_dir = _ordinary_owned(driver_dir or Path(__file__).parent)
    for role, name in (("generator", "full_shaders.py"), ("repairHelper", "stereo_eye_repair.py")):
        if (receipt[role].get("path") != "tools/quest-builder/" + name
                or receipt[role].get("sha256") != digest(asset(driver_dir, name))):
            raise BuildError("Postprocessing repair generator/helper bytes differ from its receipt.")
    rows, owners = target_rows(manifest)
    if receipt.get("shaders") != owner_records(root, owners):
        raise BuildError("Postprocessing repair native Shader identities differ from its receipt.")
    changes = {row["assetPath"]: row for row in receipt["programs"]}
    if len(changes) != 3 or set(changes) != {row["assetPath"] for row in rows}:
        raise BuildError("Postprocessing repaired program census differs.")
    for row in rows:
        change = changes[row["assetPath"]]; raw = asset(root, row["assetPath"]).read_bytes()
        if (hash_bytes(raw) != change["afterSha256"] or hash_bytes(original_bytes(raw)) != change["beforeSha256"]
                or change["afterSha256"] != row["sourceSha256"] or identity(root, row)["metaSha256"] != change["metaSha256"]
                or any(row[key] != change[key] for key in ("originalDxbcSha256", "originalInterfaceSha256"))):
            raise BuildError("Postprocessing repair changed original instructions, interface or metadata.")
    prior = prior_manifest(manifest, receipt)
    for role, name in (("identities", IDENTITIES), ("nativeCompilerInput", INPUT), ("nativeCompilerWitness", WITNESS)):
        if receipt[role].get("path") != name or receipt[role]["sha256"] != digest(asset(root, name)):
            raise BuildError("Postprocessing repair source evidence changed.")
    ledger = read(root, IDENTITIES)
    for kind in ("shaders", "materials", "programs"):
        expected = {row["assetPath"]: row for row in manifest[kind] if kind != "programs" or row["assetPath"] not in changes}
        if len(ledger[kind]) != len(expected) or {row["assetPath"] for row in ledger[kind]} != expected.keys():
            raise BuildError("Postprocessing repair unchanged identity ledger is incomplete.")
        for row in ledger[kind]:
            if kind != "materials" and not source_identity_matches(root, expected[row["assetPath"]], row, kind, manifest):
                raise BuildError("Postprocessing unchanged ledger differs from its native manifest source identity.")
    verify_ledger(root, ledger)
    input_data, witness = read(root, INPUT), read(root, WITNESS)
    verify_witness(input_data, witness, receipt["nativeCompilerInput"]["sha256"])
    if input_data.get("sourceManifestSha256") != receipt["manifest"]["beforeSha256"]:
        raise BuildError("Postprocessing repair compiler input has a different original manifest.")
    verify_owners(root, input_data, owners)
    rewrite_keys = ("assetPath", "beforeSha256", "afterSha256", "metaSha256", "originalDxbcSha256", "originalInterfaceSha256")
    if {tuple(row[key] for key in rewrite_keys) for row in input_data["rewrites"]} != {tuple(row[key] for key in rewrite_keys) for row in receipt["programs"]}:
        raise BuildError("Postprocessing repair differs from its actual native compiler input.")
    previous = receipt.get("priorFragmentRepair")
    if previous is not None:
        if (previous.get("path") != "QuestCampaignEvidence/fragment-stereo-input-order.json"
                or previous["sha256"] != digest(asset(root, previous["path"]))
                or previous["manifestAfterSha256"] != receipt["manifest"]["beforeSha256"]
                or read(root, previous["path"])["manifest"]["afterSha256"] != previous["manifestAfterSha256"]):
            raise BuildError("Postprocessing repair prior fragment proof chain differs.")
    elif prior.get("fragmentStereoInputOrderRepair"):
        raise BuildError("Postprocessing repair lost its prior native fragment proof.")
    return receipt, prior


def owner_records(root, owners):
    records = []
    for row in owners:
        value = identity(root, row, True)
        value.update({key: row[key] for key in ("originalPathId", "originalSerializedFile") if key in row})
        records.append(value)
    return sorted(records, key=lambda row: row["assetPath"])


def verify_owners(root, inputs, owners):
    expected = {row["assetPath"]: row for row in owners}
    values = inputs.get("shaders", [])
    if len(values) != 2 or {row["assetPath"] for row in values} != expected.keys():
        raise BuildError("Postprocessing native compiler Shader owner census differs.")
    for row in values:
        native = expected[row["assetPath"]]
        if (row.get("guid") != native["guid"] or row.get("sourceSha256") != native["sourceSha256"]
                or row.get("sourceSha256") != digest(asset(root, row["assetPath"]))
                or row.get("metaSha256") != digest(asset(root, row["assetPath"] + ".meta"))):
            raise BuildError("Postprocessing native compiler Shader identity differs.")
        identity(root, native, True)
    third_paths = {row["assetPath"] for row in values if row["guid"] == UBER_GUID}
    for row in inputs["rewrites"]:
        owner = next(value for value in values if value["guid"] == (UBER_GUID if row["originalInterfaceSha256"].startswith("741146") else FINALPASS_GUID))
        if ('#include "' + row["assetPath"] + '"').encode() not in asset(root, owner["assetPath"]).read_bytes():
            raise BuildError("Postprocessing native program is not referenced by its original ShaderLab.")
    for row in inputs["variants"]:
        owner = expected.get(row.get("shaderPath") or inputs.get("shaderPath"))
        native = [value for value in owner["variants"] if all(value.get(key) == row.get(key) for key in ("subshader", "pass", "hardwareTier"))
                  and value.get("keywords") == ["STEREO_INSTANCING_ENABLED"]] if owner else []
        if (len(native) != 1 or any(native[0].get(key) != row.get(key) for key in ("vertexOriginalDxbcSha256", "fragmentOriginalDxbcSha256"))):
            raise BuildError("Postprocessing compiler bank differs from its original native pass/tier/program alias.")
        if row.get("originalInterfaceSha256") == "741146a3ced34e312d9020e8dccaa56fbb5f606419ea804f519697b68a5b16be" and row.get("shaderPath") not in third_paths:
            raise BuildError("Postprocessing Uber witness uses a different Shader path.")


def variant_key(row):
    """Normalize only Unity JsonUtility's absent versus empty optional fields."""
    return tuple(row.get(key, default) for key, default in (
        ("subshader", None), ("pass", None), ("hardwareTier", None), ("stereo", None),
        ("vertexOriginalDxbcSha256", None), ("fragmentOriginalDxbcSha256", None),
        ("shaderPath", ""), ("originalInterfaceSha256", ""), ("diagnosticNativeProgramPair", False))) + (tuple(row.get("keywords", [])),)


def verify_witness(inputs, witness, input_hash):
    if (type(inputs.get("schema")) is not int or inputs["schema"] != 1
            or inputs.get("shaderGuid") != FINALPASS_GUID or len(inputs.get("rewrites", [])) != 3
            or type(witness.get("schema")) is not int or witness["schema"] != 1
            or witness.get("scope") != SCOPE or witness.get("unityVersion") != "2021.3.5f1"
            or witness.get("inputSha256") != input_hash
            or any(type(witness.get(key)) is not int or witness[key] != count for key, count in (
                ("baselineRejectedCount", 9), ("positiveCompilerCount", 27), ("monoInstancingExactStageCount", 18), ("rewriteCount", 3)))
            or witness.get("originalDeclarationsAndMathPreserved") is not True
            or witness.get("originalShaderAndMetaPreserved") is not True or witness.get("headsetPictureVerified") is not False
            or witness.get("originalPixelParityVerified") is not False or len(witness.get("banks", [])) != 27
            or [variant_key(bank["variant"]) for bank in witness["banks"]] != [variant_key(row) for row in inputs.get("variants", [])]):
        raise BuildError("Postprocessing repair requires the exact completed original-version compiler witness.")
    seen = set()
    native_dx = {0: "e05376780aeac23832d154e456d37dc27878a5654ec78c11c84e2bc1b8443bfc",
                 1: "fcea0a84dc023896283668e97445eb90b619460c7ba46bc8c1a54a12e84b4a6e"}
    for bank in witness["banks"]:
        row = bank["variant"]; mode = row.get("stereo"); subshader = row.get("subshader")
        uber = bool(row.get("shaderPath"))
        keywords = ["STEREO_INSTANCING_ENABLED"] + ({"instancing": ["STEREO_INSTANCING_ON"], "multiview": ["STEREO_MULTIVIEW_ON"]}.get(mode, []))
        key = (uber, subshader, row.get("hardwareTier"), mode)
        layers = [value for value in bank.get("outputs", []) if value.get("stage") == "vertex" and value.get("builtin") == 9]
        if (mode not in ("mono", "instancing", "multiview") or type(row.get("hardwareTier")) is not int
                or row["hardwareTier"] not in (0, 1, 2) or type(subshader) is not int or subshader not in (0, 1)
                or type(row.get("pass")) is not int or row["pass"] != 0 or row.get("diagnosticNativeProgramPair", False) is not False
                or row.get("vertexOriginalDxbcSha256") != native_dx[1 if uber else subshader] or row.get("keywords") != keywords
                or uber and (subshader != 0 or row.get("originalInterfaceSha256") != "741146a3ced34e312d9020e8dccaa56fbb5f606419ea804f519697b68a5b16be")
                or not uber and row.get("originalInterfaceSha256", "")
                or key in seen or len(layers) != 1 or layers[0].get("components") != 1 or layers[0].get("bitWidth") != 32
                or mode != "multiview" and bank.get("unchangedBaseline") is not True
                or mode == "multiview" and "DEFAULT_UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX" not in bank.get("baselineError", "")
                or any(not isinstance(bank.get(field), str) or not re.fullmatch(r"[0-9a-f]{64}", bank[field]) for field in ("vertexSha256", "fragmentSha256", "bankSha256"))):
            raise BuildError("Postprocessing native compiler layer/mode/unchanged-byte witness is incomplete.")
        seen.add(key)
    expected = {(False, sub, tier, mode) for sub in (0, 1) for tier in range(3) for mode in ("mono", "instancing", "multiview")}
    expected |= {(True, 0, tier, mode) for tier in range(3) for mode in ("mono", "instancing", "multiview")}
    if seen != expected:
        raise BuildError("Postprocessing native compiler mode/tier census is incomplete.")


def atomic(path, raw):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + ".quest-eye-pending")
    with temporary.open("xb") as stream:
        stream.write(raw); stream.flush(); os.fsync(stream.fileno())
    os.replace(temporary, path)


def repair(project, compiler_witness):
    root = _ordinary_owned(project)
    manifest_bytes = asset(root, MANIFEST).read_bytes(); manifest = json.loads(manifest_bytes)
    if manifest.get(MARKER): return verify_completed(root, manifest)[0]
    if (manifest.get("schema") != 1 or manifest.get("scope") != "campaign-compiler" or manifest.get("graphicsApi") != "Vulkan"
            or encoded(manifest) != manifest_bytes or asset(root, TRANSACTION).exists() or asset(root, RECEIPT).exists()):
        raise BuildError("Postprocessing repair requires the exact completed owned Vulkan manifest and no interrupted repair.")
    proof = _ordinary_owned(compiler_witness)
    input_bytes = asset(proof, "WitnessInput.json").read_bytes()
    witness_bytes = asset(proof, "FinalPassWitness/results.json").read_bytes()
    inputs, witness = json.loads(input_bytes), json.loads(witness_bytes)
    verify_witness(inputs, witness, hash_bytes(input_bytes))
    rows, owners = target_rows(manifest)
    if inputs.get("sourceManifestSha256") != hash_bytes(manifest_bytes):
        raise BuildError("Postprocessing actual compiler manifest input differs from this retained project.")
    verify_owners(root, inputs, owners)
    for bank in witness["banks"]:
        for stage in ("vertex", "fragment"):
            if digest(asset(proof / "FinalPassWitness", bank[stage + "File"])) != bank[stage + "Sha256"]:
                raise BuildError("Postprocessing actual native compiler SPIR-V bytes changed.")
    witnessed = {row["assetPath"]: row for row in inputs["rewrites"]}
    changes, replacements = [], []
    for row in rows:
        path = asset(root, row["assetPath"]); before = path.read_bytes(); after = rewrite(before)
        change = {key: row[key] for key in ("assetPath", "originalDxbcSha256", "originalInterfaceSha256")}
        change.update(beforeSha256=hash_bytes(before), afterSha256=hash_bytes(after), metaSha256=digest(asset(root, row["assetPath"] + ".meta")))
        if row["sourceSha256"] != change["beforeSha256"] or any(witnessed.get(row["assetPath"], {}).get(key) != value for key, value in change.items()):
            raise BuildError("Postprocessing native repair differs from its actual compiler witness.")
        changes.append(change); replacements.append((path, before, after)); row["sourceSha256"] = change["afterSha256"]
    paths = {row["assetPath"] for row in changes}
    for kind in ("shaders", "materials", "programs"):
        for row in manifest[kind]:
            if row["assetPath"] in paths: continue
            actual = identity(root, row, kind != "programs")
            if kind != "materials" and not source_identity_matches(root, row, actual, kind, manifest):
                raise BuildError("Postprocessing repair cannot adopt drifted original source bytes.")
    ledger = {"schema": 1, "scope": SCOPE + "-identities", **{kind: [identity(root, row, kind != "programs") for row in manifest[kind]
              if kind != "programs" or row["assetPath"] not in paths] for kind in ("shaders", "materials", "programs")}}
    ledger_bytes = encoded(ledger); manifest[MARKER] = {"path": RECEIPT, "scope": SCOPE, "programCount": 3}; after_manifest = encoded(manifest)
    previous = None
    if manifest.get("fragmentStereoInputOrderRepair"):
        name = "QuestCampaignEvidence/fragment-stereo-input-order.json"; old = read(root, name)
        if old["manifest"]["afterSha256"] != hash_bytes(manifest_bytes):
            raise BuildError("Postprocessing repair cannot rebase an unmatched prior fragment proof.")
        previous = {"path": name, "sha256": digest(asset(root, name)), "manifestAfterSha256": old["manifest"]["afterSha256"]}
    receipt = {"schema": 1, "scope": SCOPE, "graphicsApi": "Vulkan", "applied": True, "programCount": 3, "programs": changes, "shaders": owner_records(root, owners),
               "manifest": {"path": MANIFEST, "beforeSha256": hash_bytes(manifest_bytes), "afterSha256": hash_bytes(after_manifest)},
               "generator": {"path": "tools/quest-builder/full_shaders.py", "sha256": digest(Path(__file__).with_name("full_shaders.py"))},
               "repairHelper": {"path": "tools/quest-builder/stereo_eye_repair.py", "sha256": digest(Path(__file__))},
               "identities": {"path": IDENTITIES, "sha256": hash_bytes(ledger_bytes)}, "nativeCompilerInput": {"path": INPUT, "sha256": hash_bytes(input_bytes)},
               "nativeCompilerWitness": {"path": WITNESS, "sha256": hash_bytes(witness_bytes)}, "priorFragmentRepair": previous,
               "headsetPictureVerified": False, "originalPixelParityVerified": False}
    evidence = [(asset(root, IDENTITIES), ledger_bytes), (asset(root, INPUT), input_bytes), (asset(root, WITNESS), witness_bytes)]
    if any(path.exists() for path, _ in evidence): raise BuildError("Unregistered FinalPass evidence exists; retain it for inspection.")
    verify_ledger(root, ledger)
    if asset(root, MANIFEST).read_bytes() != manifest_bytes: raise BuildError("Postprocessing manifest changed during repair planning.")
    atomic(asset(root, TRANSACTION), encoded({"schema": 1, "scope": SCOPE, "programs": changes, "manifestBeforeSha256": hash_bytes(manifest_bytes)}))
    try:
        for path, before, after in replacements:
            if path.read_bytes() != before: raise BuildError("Postprocessing original program changed during repair.")
            atomic(path, after)
        for path, raw in evidence: atomic(path, raw)
        verify_ledger(root, ledger)
        if asset(root, MANIFEST).read_bytes() != manifest_bytes: raise BuildError("Postprocessing manifest changed during repair.")
        atomic(asset(root, MANIFEST), after_manifest); atomic(asset(root, RECEIPT), encoded(receipt))
    except Exception:
        for path, before, after in replacements:
            if path.read_bytes() == after: atomic(path, before)
        if asset(root, MANIFEST).read_bytes() == after_manifest: atomic(asset(root, MANIFEST), manifest_bytes)
        for path, raw in evidence:
            if path.is_file() and path.read_bytes() == raw: path.unlink()
        raise
    else: asset(root, TRANSACTION).unlink()
    return verify_completed(root)[0]
