#!/usr/bin/env python3
"""Re-derive EnvRoomBuilder.FireMesh's ArtE table for ModBuild 150's erosion.

WHY THIS EXISTS RATHER THAN AN EDIT TO fire_atlas_pipeline.py. ArtE holds the
DRAWN ENERGY of every fire in both rooms constant across a change to the
erosion, and the pipeline computes it by simulating EnvFlame's exact fragment
expression (`simulate_erosion`). ModBuild 150 changes ErodeTileU/V, ErodeScroll,
ErodeAmount and ErodeBase, so the shipped ArtE line is stale and every fire in
both rooms would silently change brightness.

The pipeline's ERODE_* constants are module-level and are used for NOTHING ELSE
than that simulation — the atlas PNG's alpha cells and its RGB erosion field are
derived from the source masks alone — so the table can be re-derived by importing
the module, overriding the five constants and calling the same code path. The
PNG is written to a scratch path and thrown away; the shipped
`fire_atlas_alb.png` is not touched and is byte-identical to the one on main.

The fire lane does not own `Assets/Editor/fire_atlas_pipeline.py`. Its ERODE_*
block has to be brought into step with EnvRoomBuilder's by whoever does own it —
the exact edit is named in this round's report.

Usage:
    python3 .planning/fire-arte-recompute.py
"""
import importlib.util
import os
import sys

# Importing a module out of Assets/Editor otherwise drops a __pycache__ there,
# which Unity then meta-stamps and which lands in `git status` as a stray.
sys.dont_write_bytecode = True

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PIPE = os.path.join(ROOT, "unity/GloomhavenVR.Assets/Assets/Editor/fire_atlas_pipeline.py")
# The two .unitypackage files are gitignored drops (License.md's standing rule
# 1: the raw Asset Store sources may never enter this repository). In a worktree
# they arrive as the `ressources` symlink that scripts/worktree-setup.sh makes.
# They are NOT linked into a worktree by scripts/worktree-setup.sh (that links
# a `ressources` directory of game DLLs, a different thing with a similar name),
# so point GHVR_FIRE_SRC at the drop directory of the main checkout:
#   GHVR_FIRE_SRC=<main repo>/.planning/debug/ressources
PKGS = ("Free Fire VFX - HDRP.unitypackage", "Fire 001.unitypackage")
CAND = [os.environ["GHVR_FIRE_SRC"]] if os.environ.get("GHVR_FIRE_SRC") else []
CAND += [os.path.join(ROOT, ".planning/debug/ressources")]
SRC = next((p for p in CAND
            if all(os.path.exists(os.path.join(p, f)) for f in PKGS)), None)
if SRC is None:
    raise SystemExit("the two .unitypackage drops were not found; set "
                     "GHVR_FIRE_SRC to the directory holding them. Tried: "
                     + ", ".join(CAND))

# ---- the ModBuild 150 values, mirrored from EnvRoomBuilder's Erode* block ----
NEW = dict(ERODE_W=1.08, ERODE_E=0.78, ERODE_B=0.34, ERODE_G=0.26,
           ERODE_UV=(0.95, 1.35))

spec = importlib.util.spec_from_file_location("fire_atlas_pipeline", PIPE)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)

old = {k: getattr(mod, k) for k in NEW}
print("shipped ERODE_*:", old)
print("ModBuild 150   :", NEW)
for k, v in NEW.items():
    setattr(mod, k, v)

out = os.environ.get("TMPDIR", "/tmp") + "/fire_atlas_scratch.png"
mod.main(SRC, out)
print("\n(scratch atlas written to %s and not used; the shipped PNG is untouched)" % out)
