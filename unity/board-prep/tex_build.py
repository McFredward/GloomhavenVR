"""tex_build.py -- one command: build every board texture and LOOK at it.

    python3 unity/board-prep/tex_build.py                 # everything, 2048^2
    python3 unity/board-prep/tex_build.py --size 1024     # fast iteration
    python3 unity/board-prep/tex_build.py --install       # copy into the bundle dir

Stages, in order:
  1. process the two generated sheets into motifs, and VERIFY every cell of both
     against the source image (tex_symbols.verify_sheet). A sheet that does not
     pass stops the build -- the motifs are the input to everything after this
     and a silently wrong one is what shipped last time.
  2. composite the three atlases against out/<style>_uv.json and the real mesh
  3. verify the seam padding and PRINT THE WORST-CASE NUMBER
  4. render each board in Blender, CALIBRATED, and write the renders where they
     can be looked at

Stage 4 is not a formality. This project has a standing lesson that a preview
station aimed at nothing renders happily, and another that a render showing the
bug gets explained away. The renders are the point of the stage; the exit code is
not a substitute for opening them. They are made by tex_render.py, not
gen_render.py -- see tex_render's docstring for the measurement that retired
gen_render as a judging instrument.
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


def stage_symbols(out_root, sym_dir):
    """Process BOTH delivered sheets and hold every cell against its source."""
    print("\n### 1. motifs")
    ok = True
    got = 0
    for sheet in SY.SHEETS:
        png = os.path.join(out_root, sheet["id"] + ".png")
        if not os.path.exists(png):
            print(f"  {png} is not here -- no generated sheet to process")
            continue
        good, _rows = SY.verify_sheet(
            png, os.path.join(out_root, sheet["id"].replace("sheet_", "motifs_")),
            sheet["id"])
        ok &= good
        got += 1
    if not got:
        SY.bake_placeholders(sym_dir)
        print(f"  no generated sheet -- baked {len(SY.SYMBOL_SPEC)} PROCEDURAL "
              f"stand-ins into {sym_dir}")
        print(f"  (the {len(SY.AI_NAMES)} 'ai' motifs are stand-ins; the "
              f"{len(SY.PROC_NAMES)} 'proc' motifs are final)")
        return None, True
    return out_root, ok


def stage_atlas(styles, out_dir, uv_dir, motif_root, size, seed, pad, bundle_dir):
    print("\n### 2-3. composite + seam check")
    results = {}
    worst_all = float("inf")
    all_ok = True
    for style in styles:
        print(f"\n--- {style} ---")
        uv, src = A.load_uv(style, out_dir=uv_dir)
        print(f"  region map: {src}")
        if motif_root:
            sym, chosen = A.style_motifs(style, motif_root, motif_root)
            for k in sorted(chosen):
                print(f"    {k:16s} <- {chosen[k]}")
        else:
            sym = os.path.join(uv_dir, "symbols")
        fbx, tag = find_fbx(style, uv_dir, bundle_dir)
        alb, nrm, mr, labels, names, notes = A.build_style(
            style, uv, n=size, seed=seed, symbols_dir=sym, pad=pad, fbx=fbx)
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
    """The board to measure and to render on. The bundle copy is the shipped
    asset and is now the REBUILT mesh, so it is preferred; out/ is only a
    staging area a mesh lane may leave a newer one in."""
    name = A.STYLE_FILES[style]["fbx"]
    for d, tag in ((os.path.join(REPO, bundle_dir), "bundle"), (out_dir, "out/ staging")):
        p = os.path.join(d, name)
        if os.path.exists(p):
            return p, tag
    return None, None


def stage_render(styles, tex_dir, out_dir, render_dir, bundle_dir):
    print("\n### 4. render (CALIBRATED -- read the grey-card line, not the exit code)")
    os.makedirs(render_dir, exist_ok=True)
    script = os.path.join(HERE, "tex_render.py")
    made = []
    for style in styles:
        fbx, tag = find_fbx(style, out_dir, bundle_dir)
        if not fbx:
            print(f"  {style}: no FBX anywhere; skipped")
            continue
        out = os.path.join(render_dir, f"{style}_board.png")
        cmd = [BLENDER, "--background", "--factory-startup", "--python", script, "--",
               "--tex", tex_dir, "--style", style, "--fbx", fbx, "--out", out]
        r = subprocess.run(cmd, capture_output=True, text=True)
        for ln in r.stdout.splitlines():
            if ln.startswith("PREVIEW"):
                print("  " + ln)
        if os.path.exists(out):
            print(f"  RENDER {out}  {os.path.getsize(out)} bytes  (mesh from the {tag})")
            made.append(out)
        else:
            print(f"  RENDER FAILED {style}")
            sys.stderr.write(r.stdout[-2000:] + "\n" + r.stderr[-2000:] + "\n")
    return made


def stage_install(results, bundle_dir):
    """Install the maps the mod actually resolves.

    The tray materials (PlayTray*.mat) bind exactly two textures, _MainTex and
    _BumpMap, through the custom BoardLit shader; nothing reads a packed
    metallic/roughness map. So the albedo and the normal are installed for every
    style, and the packed map only where the bundle already carries one -- which
    is Oak's PlayTray_mr.png and nowhere else. Adding two new PNGs the shader
    cannot read would be two new Unity GUIDs and 3 MB of bundle for no pixels."""
    print("\n### install into the bundle source tree")
    dst = os.path.join(REPO, bundle_dir)
    for style, paths in results.items():
        for p in paths:
            t = os.path.join(dst, os.path.basename(p))
            existed = os.path.exists(t)
            if not existed and p.endswith("_mr.png"):
                print(f"  SKIPPED  {os.path.basename(p)} -- no packed map in the bundle "
                      f"for {style} and no shader binding for one")
                continue
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
    ap.add_argument("--motifs", default=os.path.join(HERE, "out"),
                    help="where sheet_a.png / sheet_b.png live and where motifs_a/ "
                         "and motifs_b/ are written")
    ap.add_argument("--bundle-dir", default=A.BUNDLE_DIR)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--install", action="store_true")
    a = ap.parse_args()

    styles = [s.strip() for s in a.styles.split(",") if s.strip()]
    sym_dir = os.path.join(a.out, "symbols")
    tex_dir = os.path.join(a.out, "tex")
    render_dir = os.path.join(a.out, "renders")

    motif_root, sheets_ok = stage_symbols(a.motifs, sym_dir)
    if not sheets_ok:
        print("\nSHEET ACCEPTANCE FAILED -- not compositing. The motifs are the input "
              "to\neverything after this, and a silently wrong one is what shipped last "
              "time.")
        return 1
    results, worst, ok = stage_atlas(styles, tex_dir, a.out, motif_root,
                                     a.size, a.seed, a.pad, a.bundle_dir)
    if not a.no_render:
        stage_render(styles, tex_dir, a.out, render_dir, a.bundle_dir)
    if a.install:
        stage_install(results, a.bundle_dir)

    print(f"\nSEAM PADDING WORST CASE: {worst:.2f} px  (contract requires >= {A.MIN_PAD})")
    print("Now OPEN the renders in", render_dir)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
