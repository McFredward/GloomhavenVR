"""Stage the complete original native pointer and packed-Sprite closure."""
import json
from pathlib import Path
import shutil

from export_identity import object_index
import native_evidence
import packed_sprites
import pointer_recovery
from recover import RecoveryError, audit_asset_references, sha256, write_json


def restore(project, game_data, rows, owners, *, unitypy=None):
    """Repair native fields and bake exact atlas drawing state in a fresh stage.

    All inputs come from the pinned source exporter and the user's original
    game. An absent recipe fails instead of depending on a developer cache.
    """
    if unitypy is None:
        import UnityPy as unitypy
    project, game_data = Path(project), Path(game_data)
    owners = {key.casefold(): value for key, value in owners.items()}
    recipes = native_evidence.recipes(project)
    redirects = project / "QuestRecovery/native-redirect-identities.jsonl"
    workspace = project.with_name(project.name + "-native-restoration")
    if workspace.exists():
        raise RecoveryError("Native staging needs a fresh private repair workspace: " + str(workspace))
    repair = pointer_recovery.prepare(project, game_data, rows, owners, workspace,
                unitypy=unitypy, redirects=redirects if redirects.is_file() else None,
                atlas_recipes=[row for row in recipes if row["classId"] == 687078895],
                native_recipes=[row for row in recipes if row["classId"] == 114])
    if repair["complete"] is not True:
        raise RecoveryError("Full original native pointer recovery is incomplete; inspect " + str(workspace / "native-pointer-repair.json"))
    pointer_recovery.apply(project, workspace, repair)
    pointer_manifest = project / "QuestRecovery/full-native-pointer-repair.json"
    shutil.copyfile(workspace / "native-pointer-repair.json", pointer_manifest)
    rows = list(rows)
    for target in repair["additionalNativeTargets"]:
        rows.append({"guid": target["guid"], "path": target["path"],
                     "exportCollection": "original-native-source-restoration",
                     "objects": [{name: target[name] for name in ("collection", "pathId", "fileId", "classId")} | {
                         "className": "SpriteAtlas" if target["classId"] == 687078895 else "MonoScript",
                         "originalPath": "", "proof": target["proof"]}]})
    objects = object_index(rows)
    packed, atlas_rows = [], []
    for target in repair["additionalNativeTargets"]:
        if target["classId"] != 687078895:
            continue
        source = game_data / owners.get(target["collection"], target["collection"])
        environment = pointer_recovery.load_native(unitypy, source)
        original = [obj for obj in environment.objects if
                    (obj.assets_file.name.casefold(), int(obj.path_id)) == (target["collection"], target["pathId"])]
        if len(original) != 1:
            raise RecoveryError("Restored packed atlas lost its exact original source identity.")
        sprite_output = workspace / ("packed-" + target["guid"])
        receipt = packed_sprites.restore(project, game_data, original[0], target, objects, owners, sprite_output, unitypy=unitypy)
        for path in sorted((sprite_output / "Overlay").rglob("*")):
            if path.is_file():
                relative = path.relative_to(sprite_output / "Overlay")
                row = next(item for item in receipt["restoredMembers"] if item["assetPath"] == relative.as_posix())
                if sha256(project / relative) != row["beforeSha256"] or sha256(path) != row["sha256"]:
                    raise RecoveryError("Packed Sprite overlay's witnessed source/output changed.")
                shutil.copyfile(path, project / relative)
        receipt_name = "packed-sprites-" + target["guid"] + ".json"
        shutil.copyfile(sprite_output / receipt_name, project / "QuestRecovery" / receipt_name)
        packed.extend(receipt["restoredMembers"])
        atlas_rows.append({"assetPath": target["path"], "guid": target["guid"],
                           "sourceCollection": target["collection"], "sourcePathId": target["pathId"],
                           "spriteCount": receipt["originalMemberCount"], "receiptPath": "QuestRecovery/" + receipt_name})
    write_json(project / "QuestRecovery/original-asset-identities.json", {"schema": 1, "identities": rows})
    packed_manifest = project / "Assets/QuestOriginalCampaign/packed-sprites.json"
    write_json(packed_manifest, {"schema": 1, "spriteCount": len(packed), "atlases": atlas_rows, "sprites": packed,
                              "unityImportVerified": False, "headsetPictureVerified": False})
    references = audit_asset_references(project)
    if references["missingGuidCount"] or references["duplicateGuidCount"]:
        raise RecoveryError("Full original native source closure remains unresolved after exact repair.")
    result = {"nativePointerRestoration": {"manifest": pointer_manifest.relative_to(project).as_posix(),
                   "sha256": sha256(pointer_manifest), "nativePointerCount": repair["nativePointerCount"],
                   "restoredPointerCount": repair["restoredPointerCount"],
                   "remainingMissingPointerCount": repair["remainingMissingPointerCount"],
                   "sourceNativeAtlasCount": len(atlas_rows), "unityImportVerified": False},
              "nativePackedSpriteRestoration": {"manifest": packed_manifest.relative_to(project).as_posix(),
                   "sha256": sha256(packed_manifest), "nativeSpriteCount": len(packed),
                   "unityImportVerified": False, "headsetPictureVerified": False}}
    # Exact output hashes and original source recipes are retained in the stage.
    # The temporary duplicated YAML overlay is no longer needed after closure.
    shutil.rmtree(workspace)
    return rows, result
