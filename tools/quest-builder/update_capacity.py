"""Use the wizard's actual incremental-space policy before late APK migration.

No global module aliases, browser dependencies, fixed compact-code reserve or
full retained-file hash passes are introduced. The ordinary builder's output
directory and existing bounded cache-credit recipe own the same qualification.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path


class _CapacityError(RuntimeError):
    def __init__(self, code, en, de=None, **parameters):
        super().__init__(en)
        self.code, self.parameters = code, parameters
        self.message = {"en": en, "de": de or en}


def qualification_module(builder):
    path = Path(__file__).resolve().parents[1] / "quest-wizard/qualification.py"
    spec = importlib.util.spec_from_file_location("quest_update_private_capacity", path)
    module = importlib.util.module_from_spec(spec)
    module._HOST_CAPACITY_BINDINGS = (_CapacityError, builder.write_json, builder._ordinary_owned, builder.value_hash)
    spec.loader.exec_module(module)
    return module


def require_incremental_space(builder, output, game_root, repo):
    """Keep the current output/cache roots and qualify only incremental work."""
    output = builder._ordinary_owned(Path(output))
    module = qualification_module(builder)
    try:
        result = module.qualify(output, game_root=game_root, repo=repo, mode="build", helper=builder, build_output=output)
    except _CapacityError as error:
        builder.write_json(output / "logs/update-rebuild-capacity.json", {
            "schema": 1, "scope": "cached-full-build-update", "status": error.code,
            "message": error.message, **error.parameters})
        raise builder.BuildError(str(error)) from error
    builder.write_json(output / "logs/update-rebuild-capacity.json", {
        "schema": 1, "scope": "cached-full-build-update", "status": "qualified", **result})
    return result
