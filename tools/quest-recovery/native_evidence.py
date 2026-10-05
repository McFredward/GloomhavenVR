"""Capture and merge source-derived redirect and stripped-field evidence."""
import json
from pathlib import Path
import shutil

from recover import RecoveryError, sha256


def environment(directory):
    root = Path(directory) / "QuestRecovery"
    root.mkdir(parents=True, exist_ok=True)
    return {"QUEST_EXPORT_REDIRECT_IDENTITIES": str(root / "native-redirect-identities.jsonl"),
            "QUEST_EXPORT_NATIVE_RECIPES": str(root / "NativeRecipes")}


def merge(project, evidence):
    """Preserve exact native recipes, never the random exported GUID graph."""
    project, evidence = Path(project), Path(evidence)
    target = project / "QuestRecovery"
    target.mkdir(exist_ok=True)
    files = []
    for filename, keys in (("native-redirect-identities.jsonl", ("collection", "pathId")),
                           ("NativeRecipes/index.jsonl", ("collection", "pathId"))):
        source = evidence / "QuestRecovery" / filename
        if not source.is_file():
            continue
        destination = target / filename
        destination.parent.mkdir(parents=True, exist_ok=True)
        values = {}
        for path in (destination, source):
            if not path.is_file():
                continue
            for line in path.read_text().splitlines():
                row = json.loads(line)
                key = tuple(row[name].casefold() if name == "collection" else row[name] for name in keys)
                previous = values.get(key)
                if previous is not None and previous != row:
                    raise RecoveryError("Native source evidence disagrees across export batches: " + repr(key))
                values[key] = row
                if filename.startswith("NativeRecipes/"):
                    relative = Path(row["yamlPath"])
                    if relative.name != str(relative) or relative.suffix != ".yaml":
                        raise RecoveryError("Native recipe escaped its captured evidence directory.")
                    original = path.parent / relative
                    copied = destination.parent / relative
                    if sha256(original) != row["yamlSha256"]:
                        raise RecoveryError("Captured original native recipe changed.")
                    if copied.exists() and sha256(copied) != row["yamlSha256"]:
                        raise RecoveryError("Original native recipe changed across bounded exports.")
                    if original.resolve() != copied.resolve():
                        shutil.copyfile(original, copied)
                    files.append(copied)
        destination.write_text("".join(json.dumps(row, sort_keys=True) + "\n" for _, row in sorted(values.items())))
        files.append(destination)
    return [{"path": path.relative_to(project).as_posix(), "sha256": sha256(path), "bytes": path.stat().st_size}
            for path in sorted(set(files))]


def recipes(project):
    directory = Path(project) / "QuestRecovery/NativeRecipes"
    index = directory / "index.jsonl"
    if not index.is_file():
        return []
    result = []
    for line in index.read_text().splitlines():
        row = json.loads(line)
        relative = Path(row["yamlPath"])
        if relative.name != str(relative) or relative.suffix != ".yaml":
            raise RecoveryError("Native recipe escaped its original evidence directory.")
        path = directory / relative
        if sha256(path) != row["yamlSha256"]:
            raise RecoveryError("Original native recipe changed after recovery.")
        result.append({**row, "yamlPath": str(path)})
    return result
