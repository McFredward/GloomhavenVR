"""Keep witnessed preparation prefixes across exact observer-only tool updates.

The real input key still owns the mutable archive, settings and final Player.
Compatibility here only permits the ordered preparation journal to retain
completed conversions. Game, profile, templates, assets and producer logic
remain part of its scope; unknown producer changes take ordinary regeneration.
"""
from __future__ import annotations

import ast
import copy
import hashlib
from pathlib import Path
import re

from storage import BuildError, _ordinary_owned, value_hash

BUILDER = "tools/quest-builder/builder.py"
IDENTITY = "tools/quest-builder/preparation_identity.py"
RECIPE_IDENTITY = "tools/quest-builder/recovery_resume.py"
DELIVERY_PREFIXES = ("tools/quest-wizard/", "tools/quest-wizard-ui/", "tools/quest-installer/")
HEX = re.compile(r"[0-9a-f]{64}\Z")

# Exact producer profiles reviewed against the original movie/audio outputs.
# These are aliases for this repair, never a general exclusion from identity.
# The UI manifest gains physical-byte provenance, so only the committed prefix
# before that producer can migrate across its transport repair.
UI_TRANSPORT_PREVIOUS = {
    "path": "tools/quest-builder/ui_assets.py", "size": 15502,
    "sha256": "3a7deb92b1984d0c15ab2821b1ad6f211673f2d587b4da8548c5c048efd3de24",
}
UI_TRANSPORT_FIXED = {
    "path": "tools/quest-builder/ui_assets.py", "size": 17347,
    "sha256": "7c56bb464fe316aeaeeb7c42f029d7d0a789bf52cfb79b47298bf94c3a6519f6",
}
STARTUP_PROGRESS_PREVIOUS = {
    "tools/quest-builder/startup.py": {
        "path": "tools/quest-builder/startup.py", "size": 17374,
        "sha256": "7b37a9dfe70d39d45c642b17037dad65c2f7a4cefb77ffe1616e72e4fac9484e"},
    "tools/quest-builder/full_sprites.py": {
        "path": "tools/quest-builder/full_sprites.py", "size": 7248,
        "sha256": "fb587ef18e4113cd5484bf9050ab350b2d67b9748502c382d5c53d70365ea8d2"},
    "tools/quest-builder/audio.py": {
        "path": "tools/quest-builder/audio.py", "size": 21531,
        "sha256": "cc8fc63e44010d73f89dfde75f5c5d78ae00d9d5e43d7d131131302fe5177c02"},
}
STARTUP_PROGRESS_FIXED = {
    "tools/quest-builder/startup.py": {
        "path": "tools/quest-builder/startup.py", "size": 19926,
        "sha256": "0203bae66b4156a17f394c78961185d4f0c922afbb7cd524b57d020c3ad18233"},
    "tools/quest-builder/full_sprites.py": {
        "path": "tools/quest-builder/full_sprites.py", "size": 9217,
        "sha256": "28d4822eec0e4684f237004c63a595c9c9fe6bed419e85fdea015e09fa33fc0f"},
    "tools/quest-builder/audio.py": {
        "path": "tools/quest-builder/audio.py", "size": 22646,
        "sha256": "167a595a97ec838bf9d88bdb2378969f9175f7e231043f1d1f6a679b8c443b05"},
}


def _reviewed_startup_rows(rows):
    by_path = {row["path"]: row for row in rows}
    if (STARTUP_PROGRESS_FIXED and all(by_path.get(name) == fixed
                                      for name, fixed in STARTUP_PROGRESS_FIXED.items())):
        rows = [STARTUP_PROGRESS_PREVIOUS.get(row["path"], row) for row in rows]
    return [UI_TRANSPORT_PREVIOUS if row == UI_TRANSPORT_FIXED else row for row in rows]


def _transport_prefix(value, target):
    names = ["base-project", "post-effects", "loading-resources", "startup-movies"]
    if target == "game": names.append("native-sprites")
    names += ["loading-sprite", "startup-audio"]
    steps = value.get("steps")
    if not isinstance(steps, list) or len(steps) > len(names): return False
    for index, step in enumerate(steps):
        if (not isinstance(step, dict) or step.get("name") != names[index]
                or step.get("operation") != ("project-files" if index == 0 else "startup-content")):
            return False
    pending = value.get("pending")
    if pending is None: return True
    next_name = names[len(steps)] if len(steps) < len(names) else "ui-recipes" if target == "game" else "startup-ui"
    return (isinstance(pending, dict) and pending.get("name") == next_name
            and pending.get("operation") == ("project-files" if not steps else "startup-content"))


LOAD = ast.parse('preparation_identity = _local_helper("preparation_identity")').body[0]
REBIND = ast.parse(
    "prior_preparation_key = preparation_identity.rebind_key("
    "output, project, inputs, source, target=args.target, recipe=RECIPE, recovery=recovery_resume)").body[0]
PREPARATION_CALL = ast.parse("prepare_resume.Preparation").body[0].value


def builder_producer_digest(raw):
    """Hash the complete Builder AST except these exact journal identity calls.

    This avoids a self-referential whole-file hash. No producer function, global
    constant, call order or unrelated constructor argument is omitted. Even an
    unknown change elsewhere in prepare() produces a different identity.
    """
    try:
        tree = ast.parse(raw.decode("utf-8"))
    except (UnicodeError, SyntaxError) as error:
        raise BuildError("Preparation Builder source cannot be parsed.") from error
    tree.body = [node for node in tree.body
                 if ast.dump(node, include_attributes=False) != ast.dump(LOAD, include_attributes=False)]
    for prepare in tree.body:
        if not isinstance(prepare, ast.FunctionDef) or prepare.name != "prepare":
            continue
        prepare.body = [node for node in prepare.body
                        if ast.dump(node, include_attributes=False) != ast.dump(REBIND, include_attributes=False)]
        for generate in prepare.body:
            if not isinstance(generate, ast.FunctionDef) or generate.name != "generate":
                continue
            for node in generate.body:
                if not isinstance(node, ast.Assign) or not isinstance(node.value, ast.Call):
                    continue
                call = node.value
                if (len(node.targets) != 1 or not isinstance(node.targets[0], ast.Name)
                        or node.targets[0].id != "resume"
                        or ast.dump(call.func, include_attributes=False) != ast.dump(PREPARATION_CALL, include_attributes=False)):
                    continue
                call.keywords = [keyword for keyword in call.keywords
                                 if not (keyword.arg == "compatible_input_key"
                                         and isinstance(keyword.value, ast.Name)
                                         and keyword.value.id == "prior_preparation_key")]
    return hashlib.sha256(ast.dump(tree, include_attributes=False).encode("utf-8")).hexdigest()


def _builder_bytes(root, records):
    record = records.get(BUILDER)
    if record is None:
        raise BuildError("Preparation compatibility requires its immutable Builder source record.")
    path = _ordinary_owned(Path(root) / BUILDER)
    if not path.is_file():
        raise BuildError("Previous immutable Builder snapshot is missing; existing preparation was retained.")
    raw = path.read_bytes()
    if len(raw) != record["bytes"] or hashlib.sha256(raw).hexdigest() != record["sha256"]:
        raise BuildError("Preparation Builder snapshot differs from its immutable source manifest.")
    return raw


def _scope(inputs, source, recovery):
    records = recovery._records(inputs["mod"]["files"], "size")
    builder = builder_producer_digest(_builder_bytes(source, records))
    rows = _reviewed_startup_rows(recovery.preparation_source_rows(inputs["mod"]["files"]))
    # These modules validate/select identity; neither generates game assets.
    # Delivery tools are never read by generate_files or its conversion tools.
    rows = [row for row in rows if row["path"] not in (BUILDER, IDENTITY, RECIPE_IDENTITY)
            and not row["path"].startswith(DELIVERY_PREFIXES)]
    scope = copy.deepcopy({key: value for key, value in inputs.items() if key not in ("inputKey", "mod")})
    scope["mod"] = {"modBuild": inputs["mod"].get("modBuild"),
                    "producerFiles": rows, "builderProducerAstSha256": builder}
    return scope


def rebind_key(output, project, inputs, source, *, target, recipe, recovery):
    """Return one fully scoped preceding key; never modify journals or assets."""
    output, project = Path(output), _ordinary_owned(Path(project))
    journal = _ordinary_owned(output / "cache/prepare-resume" / project.name / "journal.json")
    if not journal.exists():
        return None
    value = recovery._read(journal)
    previous_key = value.get("inputKey")
    if (value.get("schema") != 1 or value.get("owner") != "Quest preparation substage journal"
            or value.get("project") != project.relative_to(output).as_posix()
            or value.get("target") != target or type(value.get("recipe")) is not int or value.get("recipe") != recipe
            or not isinstance(previous_key, str) or not HEX.fullmatch(previous_key)):
        raise BuildError("Preparation journal identity differs; existing project was retained.")
    if previous_key == inputs["inputKey"]:
        return None
    previous = recovery._manifest(output / "manifests" / (previous_key + ".json"))
    current = recovery._manifest(output / "manifests" / (inputs["inputKey"] + ".json"))
    if current != inputs:
        raise BuildError("Current preparation inputs differ from their immutable manifest.")
    previous_source = _ordinary_owned(output / "inputs/mod" / previous["mod"]["key"])
    old_rows = {name: {"path": name, "size": row["bytes"], "sha256": row["sha256"]}
                for name, row in recovery._records(previous["mod"]["files"], "size").items()}
    new_rows = {name: {"path": name, "size": row["bytes"], "sha256": row["sha256"]}
                for name, row in recovery._records(inputs["mod"]["files"], "size").items()}
    before_ui, after_ui = old_rows.get(UI_TRANSPORT_PREVIOUS["path"]), new_rows.get(UI_TRANSPORT_FIXED["path"])
    if before_ui != after_ui:
        if (before_ui != UI_TRANSPORT_PREVIOUS or after_ui != UI_TRANSPORT_FIXED
                or not _transport_prefix(value, target)):
            return None
    before, after = _scope(previous, previous_source, recovery), _scope(inputs, source, recovery)
    if value_hash(before) != value_hash(after):
        return None
    print("preparation resume: retaining witnessed conversions across reviewed compatible tool update; "
          "mutable content and final settings retain the current input key", flush=True)
    return previous_key
