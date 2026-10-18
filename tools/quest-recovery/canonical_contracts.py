"""Source-proven stable identities required by existing Quest UI restorers.

These six source object contracts replace a private B614 export prerequisite.
The player's original serialized bytes must match before a canonical GUID/path
is assigned. Random exporter GUIDs and matching display names are insufficient.
"""
from pathlib import Path
import hashlib

from canonical_guids import apply
from export_identity import object_index
from recover import RecoveryError, sha256

CONTRACTS = [
    {"collection": "sharedassets1.assets", "pathId": 872, "name": "Custom/SimpleGrabPassBlur",
     "canonicalGuid": "a0b681ed431356a47b5287e6441c1c11", "canonicalPath": "Assets/Shader/Custom_SimpleGrabPassBlur.shader",
     "originalObjectSha256": "e07bf2c9a859537a96ad8a309d29341c47a074b8c1f45724e46768b729f1ddd8"},
    {"collection": "sharedassets1.assets", "pathId": 873, "name": "Splash Screen Shader",
     "canonicalGuid": "36fec7f4d3bfafd409fd42ffef9eec70", "canonicalPath": "Assets/Shader/Splash Screen Shader.shader",
     "originalObjectSha256": "073674998f030b7783205b6a963dcd2b716ab5e46cf3634ddeb6d92b5a816ea3"},
    {"collection": "sharedassets2.assets", "pathId": 132, "name": "Hidden/BrightPassFilter2",
     "canonicalGuid": "93f40d5ea0c0a7945a5782e2dcd23833", "canonicalPath": "Assets/Shader/Hidden_BrightPassFilter2.shader",
     "originalObjectSha256": "caea55873dc98289218f0ba4078cb8354681a377d568b9a5fd61c22eab722e21"},
    {"collection": "sharedassets2.assets", "pathId": 135, "name": "Hidden/BlendForBloom",
     "canonicalGuid": "30881e480b10c1b46a3d99ec13496f5e", "canonicalPath": "Assets/Shader/Hidden_BlendForBloom.shader",
     "originalObjectSha256": "66a825cb38c77906d545b4c04f4ad1876e77e73826c487bdf3504dc290531c57"},
    {"collection": "sharedassets2.assets", "pathId": 136, "name": "UI/Dissolve mask",
     "canonicalGuid": "ee924f72fbf9fcd4a9ea6e1dceb11982", "canonicalPath": "Assets/Shader/UI_Dissolve mask.shader",
     "originalObjectSha256": "6ffe299b69a809eafbdd147702c511f48b7e6f7d5a6d401f55ea5e9587ae2fbe"},
    {"collection": "sharedassets2.assets", "pathId": 137, "name": "Hidden/BlurAndFlares",
     "canonicalGuid": "29d4384c2ae952c4597a9d894d381163", "canonicalPath": "Assets/Shader/Hidden_BlurAndFlares.shader",
     "originalObjectSha256": "d61cf33a6efeb7a000c229e141c0730d3ede780465a26626201ba3c0d3e844ac"},
]


def witness(game_data, project, identities, unitypy=None):
    if unitypy is None:
        try:
            import UnityPy as unitypy
        except ImportError as error:
            raise RecoveryError("UnityPy is required to verify original native UI shader contracts.") from error
    game_data, project = Path(game_data), Path(project)
    exported = object_index(identities)
    proofs = []
    for collection in sorted({row["collection"] for row in CONTRACTS}):
        source = game_data / collection
        environment = unitypy.load(str(source))
        original = {obj.path_id: obj for obj in environment.objects if obj.assets_file.name == collection}
        for contract in [row for row in CONTRACTS if row["collection"] == collection]:
            obj = original.get(contract["pathId"])
            target = exported.get((collection, contract["pathId"]))
            if obj is None or target is None or obj.type.name != "Shader" or target["classId"] != 48:
                raise RecoveryError("Original canonical shader object is absent: " + contract["name"])
            raw_hash = hashlib.sha256(obj.get_raw_data()).hexdigest()
            if raw_hash != contract["originalObjectSha256"] or obj.read().m_ParsedForm.m_Name != contract["name"]:
                raise RecoveryError("Original native shader version changed; its interface must be reviewed: " + contract["name"])
            proofs.append({**contract, "newGuid": target["guid"], "newPath": target["path"],
                           "sourceContainerSha256": sha256(source),
                           "originalObjects": [{"collection": collection, "pathId": contract["pathId"], "fileId": target["fileId"]}]})
    return {"schema": 1, "association": "exact-original-native-object-bytes-and-CAB-pathID",
            "proofs": proofs, "mappings": {row["newGuid"]: row["canonicalGuid"] for row in proofs},
            "rejected": [], "mappedOriginalObjectCount": len(proofs), "privateB614CacheRequired": False}


def restore(game_data, project, identities, unitypy=None):
    proof = witness(game_data, project, identities, unitypy)
    return apply(project, identities, proof), proof
