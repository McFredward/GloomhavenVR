"""Portable evidence for the actual host driver and staged Unity Editor sources.

This describes build inputs, not successful preparation or device execution.
The caller includes ``value_hash(capture(...))`` in its derivative build identity
and compares a second capture before accepting the player build.
"""

from pathlib import Path
import re

from storage import BuildError, record_file


_HASH = re.compile(r"[0-9a-f]{64}")
_COMMIT = re.compile(r"(?:[0-9a-f]{40}|[0-9a-f]{64})")
_UNITY = re.compile(r"20\d{2}\.\d+\.\d+[abfp]\d+")
_EDITOR = Path("Assets/Quest/Editor")
_TEMPLATE = Path("unity/GloomhavenVR.Quest")
_DRIVER = Path("tools/quest-builder")
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
    return {"schema": 1, "runtime": runtime, "toolchain": tools,
            "buildDriverModules": driver_records, "stagedEditorSources": editor_records}
