"""tex_build.py -- one command: build every board texture and LOOK at it.

    python3 unity/board-prep/tex_build.py                 # everything, 2048^2
    python3 unity/board-prep/tex_build.py --size 1024     # fast iteration
    python3 unity/board-prep/tex_build.py --install       # copy into the bundle dir

Stages, in order:
  1. bake the placeholder motifs, so the pipeline runs before any paid image exists
  2. composite the three atlases against out/<style>_uv.json (or the contract default)
  3. verify the seam padding and PRINT THE WORST-CASE NUMBER
  4. render each board in Blender and write the renders where they can be looked at

Stage 4 is not a formality. This project has a standing lesson that a preview
station aimed at nothing renders happily, and another that a render showing the
bug gets explained away. The renders are the point of the stage; the exit code is
not a substitute for opening them.
"""

import argparse
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import tex_atlas as A          # noqa: E402
import tex_common as T         # noqa: E402
import tex_seams as S          # noqa: E402
import tex_symbols as SY       # noqa: E402

BLENDER = "/home/claw/blender-4.2/blender"
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))


def stage_symbols(sym_dir, use_ai_dir=None):
    print("\n### 1. motifs")
    if use_ai_dir and os.path.isdir(use_ai_dir):
        have = [n for n in SY.AI_NAMES if os.path.exists(os.path.join(use_ai_dir, n + ".png"))]
        print(f"  processed AI motifs available in {use_ai_dir}: {len(have)}/{len(SY.AI_NAMES)}"
              f"  {have}")
        missing = [n for n in SY.AI_NAMES if n not in have]
        if missing:
            print(f"  falling back to the procedural stand-in for: {missing}")
        return use_ai_dir
    SY.bake_placeholders(sym_dir)
    print(f"  no processed AI motifs -- baked {len(SY.SYMBOL_SPEC)} PROCEDURAL "
          f"stand-ins into {sym_dir}")
    print(f"  (the {len(SY.AI_NAMES)} 'ai' motifs are stand-ins; the "
          f"{len(SY.PROC_NAMES)} 'proc' motifs are final)")
    return None


def stage_atlas(styles, out_dir, uv_dir, sym_dir, size, seed, pad):
    print("\n### 2-3. composite + seam check")
    results = {}
    worst_all = float("inf")
    all_ok = True
    for style in styles:
        print(f"\n--- {style} ---")
        uv, src = A.load_uv(style, out_dir=uv_dir)
        print(f"  region map: {src}")
        alb, nrm, mr, labels, names, notes = A.build_style(
            style, uv, n=size, seed=seed, symbols_dir=sym_dir, pad=pad)
        for ln in notes:
            print(ln)
        paths = A.write_style(style, out_dir, alb, nrm, mr)
        for p in paths:
            print(f"  WROTE {p}  {os.path.getsize(p)} bytes")
        print(f"  seam check (islands = region rects inset by {pad}px):")
        worst, ok = S.check(labels, names, pad_required=A.MIN_PAD)
        S.check_bleed(alb, labels, pad_required=A.MIN_PAD)
        worst_all = min(worst_all, worst)
        all_ok &= ok
        results[style] = paths
    print(f"\n  WORST SEAM SEPARATION ACROSS ALL STYLES: {worst_all:.2f} px "
          f"(contract floor {A.MIN_PAD} px) -- {'PASS' if all_ok else 'FAIL'}")
    return results, worst_all, all_ok


def find_fbx(style, out_dir, bundle_dir):
    """Prefer the mesh lane's new FBX; fall back to the shipped one and SAY SO."""
    name = A.STYLE_FILES[style]["fbx"]
    for d, tag in ((out_dir, "mesh lane"), (os.path.join(REPO, bundle_dir), "SHIPPED (old UVs)")):
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p, tag
    return None, None


def stage_render(styles, tex_dir, out_dir, render_dir, bundle_dir, modes=("front", "quarter")):
    print("\n### 4. render")
    os.makedirs(render_dir, exist_ok=True)
    script = os.path.join(HERE, "gen_render.py")
    if not os.path.exists(script):
        print(f"  gen_render.py missing at {script}; skipping renders")
        return []
    made = []
    for style in styles:
        fbx, tag = find_fbx(style, out_dir, bundle_dir)
        if not fbx:
            print(f"  {style}: no FBX anywhere; skipped")
            continue
        if tag != "mesh lane":
            print(f"  {style}: using the {tag} mesh {os.path.basename(fbx)} -- its UV")
            print(f"           layout is the OLD photogrammetry one, NOT the contract")
            print(f"           region map, so the atlas will land in the wrong places.")
            print(f"           This exercises the compositor; it is not a final look.")
        base = A.STYLE_FILES[style]["base"]
        alb = os.path.join(tex_dir, f"{base}_albedo.png")
        nrm = os.path.join(tex_dir, f"{base}_normal.png")
        for mode in modes:
            out = os.path.join(render_dir, f"{style}_{mode}.png")
            cmd = [BLENDER, "--background", "--factory-startup", "--python", script,
                   "--", fbx, alb, nrm, out, mode]
            r = subprocess.run(cmd, capture_output=True, text=True)
            if os.path.exists(out):
                print(f"  RENDER {out}  {os.path.getsize(out)} bytes  ({mode}, {tag})")
                made.append(out)
            else:
                print(f"  RENDER FAILED {style}/{mode}")
                sys.stderr.write(r.stdout[-2000:] + "\n" + r.stderr[-2000:] + "\n")
    return made


def stage_install(results, bundle_dir):
    print("\n### install into the bundle source tree")
    dst = os.path.join(REPO, bundle_dir)
    for style, paths in results.items():
        for p in paths:
            t = os.path.join(dst, os.path.basename(p))
            existed = os.path.exists(t)
            shutil.copy2(p, t)
            print(f"  {'REPLACED' if existed else 'ADDED   '} {t}  {os.path.getsize(t)} bytes")
    print("  NOTE the bundle must then be rebuilt in Unity and copied to the rig; it")
    print("  has been byte-identical since ModBuild 250, so this is not a DLL-only install.")


def main():
    ap = argparse.ArgumentParser(description="build and look at the board textures")
    ap.add_argument("--styles", default="oak,steel,bronze")
    ap.add_argument("--size", type=int, default=T.N_DEFAULT)
    ap.add_argument("--seed", type=int, default=20260825)
    ap.add_argument("--pad", type=int, default=A.PAD)
    ap.add_argument("--out", default=os.path.join(HERE, "out"))
    ap.add_argument("--ai-symbols", default=os.path.join(HERE, "out", "symbols_ai"),
                    help="directory of motifs processed from a generated sheet")
    ap.add_argument("--bundle-dir", default=A.BUNDLE_DIR)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--install", action="store_true")
    a = ap.parse_args()

    styles = [s.strip() for s in a.styles.split(",") if s.strip()]
    sym_dir = os.path.join(a.out, "symbols")
    tex_dir = os.path.join(a.out, "tex")
    render_dir = os.path.join(a.out, "renders")

    used_ai = stage_symbols(sym_dir, a.ai_symbols)
    results, worst, ok = stage_atlas(styles, tex_dir, a.out,
                                     used_ai or sym_dir, a.size, a.seed, a.pad)
    if not a.no_render:
        stage_render(styles, tex_dir, a.out, render_dir, a.bundle_dir)
    if a.install:
        stage_install(results, a.bundle_dir)

    print(f"\nSEAM PADDING WORST CASE: {worst:.2f} px  (contract requires >= {A.MIN_PAD})")
    print("Now OPEN the renders in", render_dir)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
