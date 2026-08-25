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
        alb, nrm, mr, mrs, labels, names, notes = A.build_style(
            style, uv, n=size, seed=seed, symbols_dir=sym, pad=pad, fbx=fbx)
        for ln in notes:
            print(ln)
        paths = A.write_style(style, out_dir, alb, nrm, mr, mrs)
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


def _render_one(script, args, out, label):
    r = subprocess.run([BLENDER, "--background", "--factory-startup", "--python", script,
                        "--"] + args, capture_output=True, text=True)
    for ln in r.stdout.splitlines():
        if ln.startswith("PREVIEW"):
            print("  " + ln)
    if os.path.exists(out):
        print(f"  RENDER {out}  {os.path.getsize(out)} bytes  ({label})")
        return out
    print(f"  RENDER FAILED {label}")
    sys.stderr.write(r.stdout[-2000:] + "\n" + r.stderr[-2000:] + "\n")
    return None


def stage_render(styles, tex_dir, out_dir, render_dir, bundle_dir, pbr=False):
    """Stage 4 renders through the SHADER THAT SHIPS.

    It used to render only through a Principled BSDF bound to <base>_mr.png.
    That is a real instrument for judging the maps as PBR data and it is still
    available under --pbr-render, but it is not what the game draws: the game
    draws BoardLit, whose _MRSMap is a different file in a different channel
    order and whose lighting is two baked directions with no environment at all.
    Four rounds of this rebuild were judged off the Principled render and every
    metallic highlight in all of them was a term the game never evaluated.

    Two views per board, because they answer different questions. `flat` is
    directly comparable with Unity's own PreviewBoard shot (same camera, same
    framing, same board-pixel statistics). `seats` is the close crop, which is
    the only one that shows what the material looks like at the distance the
    board is actually held."""
    print("\n### 4. render THROUGH BoardLit (--shipped) -- open the PNGs, not the exit code")
    os.makedirs(render_dir, exist_ok=True)
    script = os.path.join(HERE, "tex_render.py")
    made = []
    for style in styles:
        fbx, tag = find_fbx(style, out_dir, bundle_dir)
        if not fbx:
            print(f"  {style}: no FBX anywhere; skipped")
            continue
        for view in ("flat", "seats"):
            out = os.path.join(render_dir, f"{style}_shipped_{view}.png")
            got = _render_one(script,
                              ["--shipped", "--tex", tex_dir, "--style", style,
                               "--fbx", fbx, "--view", view, "--out", out],
                              out, f"{style} {view}, mesh from the {tag}")
            if got:
                made.append(got)
        if pbr:
            out = os.path.join(render_dir, f"{style}_pbr.png")
            got = _render_one(script,
                              ["--tex", tex_dir, "--style", style, "--fbx", fbx,
                               "--out", out],
                              out, f"{style} Principled/PBR, NOT what ships")
            if got:
                made.append(got)
    return made


# WHAT GETS INSTALLED, as an explicit allowlist rather than a rule about what is
# already there.
#
# The previous rule was "install the packed map only where the bundle already
# carries one", on the stated argument that "nothing reads a packed
# metallic/roughness map". THAT ARGUMENT IS NOW FALSE. PlayTray*.mat bind
# _MRSMap and set _SpecStrength = 0.85, so the shipped material samples a packed
# map on all three boards. The old rule would have silently skipped exactly the
# file the shader had just started reading -- and it would have done it while
# printing the word SKIPPED next to a sentence saying nothing reads it, which is
# the most expensive kind of wrong.
#
# _mr.png is the glTF ORM pack (R=occlusion, G=roughness, B=metallic). It is
# written to out/ because tex_render.py's Principled path reads it as PBR data,
# and it is NOT installed: no material binds it, its channel order is not
# BoardLit's, and the one stale copy that used to sit in the bundle
# (PlayTray_mr.png, bound by nothing) has been removed.
INSTALL_SUFFIXES = ("_albedo.png", "_normal.png", "_mrs.png")


def stage_install(results, bundle_dir):
    """Install the maps the shipped material actually samples.

    PlayTray*.mat bind three textures through BoardLit: _MainTex (<base>_albedo),
    _BumpMap (<base>_normal) and _MRSMap (<base>_mrs, R=metallic G=roughness).
    Those three, for all three styles, and nothing else -- see INSTALL_SUFFIXES."""
    print("\n### install into the bundle source tree")
    dst = os.path.join(REPO, bundle_dir)
    for style, paths in results.items():
        for p in paths:
            name = os.path.basename(p)
            if not name.endswith(INSTALL_SUFFIXES):
                print(f"  not installed  {name} -- no material binds it "
                      f"(it is the PBR instrument's input, see tex_render.py)")
                continue
            t = os.path.join(dst, name)
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
    ap.add_argument("--motifs", default=os.path.join(HERE, "out"),
                    help="where sheet_a.png / sheet_b.png live and where motifs_a/ "
                         "and motifs_b/ are written")
    ap.add_argument("--bundle-dir", default=A.BUNDLE_DIR)
    ap.add_argument("--no-render", action="store_true")
    ap.add_argument("--pbr-render", action="store_true",
                    help="ALSO render each board through the Principled BSDF and "
                         "<base>_mr.png. That judges the maps as PBR data; it is "
                         "not what the game draws (see stage_render).")
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
        stage_render(styles, tex_dir, a.out, render_dir, a.bundle_dir, pbr=a.pbr_render)
    if a.install:
        stage_install(results, a.bundle_dir)

    print(f"\nSEAM PADDING WORST CASE: {worst:.2f} px  (contract requires >= {A.MIN_PAD})")
    print("Now OPEN the renders in", render_dir)
    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
