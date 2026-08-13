#!/usr/bin/env python3
"""GloomhavenVR — Poly Haven asset download + processing pipeline (v2).

- models come from the clean glTF exports (single mesh, consistent wedge UVs);
  heavy photoscans are decimated UV-preservingly (pymeshlab quadric-with-texture)
  and written as OBJ; light meshes are written as OBJ too (uniform importer path).
- albedo = Diffuse x AO (softened), foliage gets the Alpha map merged into
  the albedo alpha channel (PNG); everything else is JPG.
- everything from Poly Haven is CC0 (https://polyhaven.com/license).

Output -> unity/GloomhavenVR.Assets/Assets/Bundle/Environments/Imported/{Models,Textures}
"""
import json
import os
import shutil
import sys
import urllib.request

import numpy as np
from PIL import Image

# Lives at unity/GloomhavenVR.Assets/Assets/Editor/ — downloads go to a cache
# dir next to the repo checkout (override with PH_CACHE), outputs into the
# bundle's Imported/ folder relative to this file.
_HERE = os.path.dirname(os.path.abspath(__file__))
DL = os.environ.get("PH_CACHE") or os.path.join(_HERE, "..", "..", "..", "..", ".ph-cache")
OUT = os.path.normpath(os.path.join(_HERE, "..", "Bundle", "Environments", "Imported"))
UA = {"User-Agent": "GloomhavenVR-env-build/1.0"}


def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return dest
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as r, open(dest + ".part", "wb") as f:
        f.write(r.read())
    os.replace(dest + ".part", dest)
    print(f"  dl {os.path.basename(dest)} {os.path.getsize(dest)/1e6:.2f}MB")
    return dest


def files_json(asset):
    dest = os.path.join(DL, "_api", asset + ".json")
    fetch(f"https://api.polyhaven.com/files/{asset}", dest)
    with open(dest) as f:
        return json.load(f)


def map_url(fj, map_key, res, fmt):
    node = fj[map_key][res]
    if fmt in node:
        return node[fmt]["url"]
    return node[list(node.keys())[0]]["url"]


# --------------------------------------------------------------------- recipes
# d/n/ao/alpha: SOURCE resolutions committed to the repo (bundle size is
# controlled by Unity import caps in BuildEnvironments.cs, not here).
# tris: decimation target (None = keep full mesh).
MODELS = {
    # ---- cellar props ----
    "wine_barrel_01":        dict(d="2k", n="1k", ao="1k", tris=None),
    "wooden_crate_01":       dict(d="1k", n="1k", ao="1k", tris=None),
    "small_wooden_table_01": dict(d="1k", n="1k", ao="1k", tris=None),
    "wooden_stool_02":       dict(d="1k", n="1k", ao="1k", tris=5000),
    "wooden_bookshelf_worn": dict(d="1k", n="1k", ao="1k", tris=None),
    "wooden_bucket_01":      dict(d="1k", n="1k", ao="1k", tris=None),
    "jug_01":                dict(d="1k", n="1k", ao="1k", tris=None),
    # ---- night forest (replaced the swamp, user ruling 2026-08-13 "statt Moor
    #      mach eventuell doch lieber einen gruseligen Wald") ----
    # Deadfall + trunks lying on the forest floor. Decimation targets are much
    # lower than the cellar props': these live 3-20 m away in moonlight and fog,
    # and the tri budget is spent on the TREE RING instead (~150k/room cap).
    "dead_tree_trunk":       dict(d="2k", n="1k", ao="1k", tris=4000),
    "dead_tree_trunk_02":    dict(d="2k", n="1k", ao="1k", tris=4000),
    "tree_stump_01":         dict(d="1k", n="1k", ao="1k", tris=2500),
    "tree_stump_02":         dict(d="1k", n="1k", ao="1k", tris=2500),
    "root_cluster_02":       dict(d="1k", n="1k", ao="1k", tris=2500),
    "single_root":           dict(d="1k", n="1k", ao="1k", tris=1200),
    "rock_moss_set_01":      dict(d="2k", n="1k", ao="1k", tris=3000),
    "rock_moss_set_02":      dict(d="1k", n="1k", ao="1k", tris=2500),
    "dry_branches_medium_01": dict(d="1k", n="1k", ao="1k", tris=2000),
    # Understory. These are scanned as thin blade/frond geometry — decimating
    # them much below the values here eats the silhouettes, so instance counts
    # (not tri targets) are the budget knob for foliage.
    "grass_medium_02":       dict(d="1k", n=None, ao="1k", alpha="1k", tris=5000),
    "fern_02":               dict(d="1k", n=None, ao="1k", alpha="1k", tris=3500),
    "shrub_03":              dict(d="1k", n=None, ao="1k", alpha="1k", tris=3500),
    "moss_01":               dict(d="1k", n=None, ao="1k", alpha="1k", tris=None),
    # the story beat at the clearing edge: an axe left in a stump
    "wooden_axe_02":         dict(d="1k", n="1k", ao="1k", tris=3000),
}

SURFACES = {
    # medieval_blocks_05: weathered grey stone blocks — the cellar walls
    # (castle_brick_07 was tried first and REMOVED: red-brick oven look)
    "medieval_blocks_05":    dict(d="2k", n="2k", ao="2k"),
    "monastery_stone_floor": dict(d="2k", n="2k", ao="2k"),   # cellar floor
    "dark_wooden_planks":    dict(d="2k", n="2k", ao="2k"),   # cellar ceiling/beams
    "forest_ground_04":      dict(d="2k", n="2k", ao="2k"),   # forest floor (needles/dirt)
    "forest_leaves_04":      dict(d="2k", n="2k", ao="2k"),   # forest floor (leaf litter)
    "pine_bark":             dict(d="2k", n="2k", ao="2k"),   # procedural conifer trunks
    "bark_brown_02":         dict(d="1k", n="1k", ao="1k"),   # second trunk species
}

# ------------------------------------------------------------------- foliage
# Poly Haven's scanned conifers (pine_tree_01 / fir_tree_01) ship as ~500 MB-1 GB
# multi-material photoscans — far past this bundle's budget, and their alpha
# twig cards do not survive decimation. But their TWIG ATLASES are exactly the
# asset a card-built forest needs: real photoscanned fir sprigs and a bare
# branch on a clean alpha, 1k, CC0. Downloaded standalone (no mesh, no .bin) and
# merged into one RGBA card sheet; BuildEnvironmentRooms.cs cuts sub-rects out
# of it (FirCards) for the crowns and the canopy.
CARDS = {
    "fir_twig": dict(asset="fir_tree_01", diff="twig_diff", alpha="twig_alpha", res="1k"),
}


def load_rgb(path):
    im = Image.open(path)
    return im.convert("RGB") if im.mode != "RGB" else im


def process_albedo(diff_path, ao_path, alpha_path, out_path):
    d = load_rgb(diff_path)
    if ao_path:
        ao = load_rgb(ao_path).resize(d.size, Image.LANCZOS).convert("L")
        a = np.asarray(d).astype(np.float32) / 255.0
        m = (np.asarray(ao).astype(np.float32) / 255.0)[..., None]
        m = 0.25 + 0.75 * m  # softened AO multiply
        d = Image.fromarray((a * m * 255.0 + 0.5).astype("uint8"))
    if alpha_path:
        al = Image.open(alpha_path).convert("L").resize(d.size, Image.LANCZOS)
        d = d.convert("RGBA")
        d.putalpha(al)
        d.save(out_path)
    else:
        d.save(out_path, quality=92)
    print(f"  wrote {os.path.basename(out_path)} {d.size}")


def gltf_download(name, fj):
    """Download the 1k glTF + its buffer + referenced textures; return gltf path."""
    node = fj["gltf"]["1k"]["gltf"]
    gdir = os.path.join(DL, name, "gltf")
    gltf = fetch(node["url"], os.path.join(gdir, os.path.basename(node["url"])))
    for rel, inc in node.get("include", {}).items():
        fetch(inc["url"], os.path.join(gdir, rel))
    return gltf


def export_mesh(name, fj, target_tris, out_dir):
    """glTF -> (optional decimation) -> OBJ with normals + wedge UVs."""
    import pymeshlab
    gltf = gltf_download(name, fj)
    ms = pymeshlab.MeshSet()
    ms.load_new_mesh(gltf)
    # merge multi-mesh scenes (grass planes, rock sets) into one mesh
    if ms.mesh_number() > 1:
        ms.generate_by_merging_visible_meshes(mergevisible=True)
    n0 = ms.current_mesh().face_number()
    if target_tris and n0 > target_tris:
        ms.meshing_decimation_quadric_edge_collapse_with_texture(
            targetfacenum=target_tris, preserveboundary=True,
            boundaryweight=2.0, planarquadric=True)
    ms.compute_normal_per_vertex(weightmode=2)  # area-weighted
    # wedge UVs -> per-vertex UVs (splits seam vertices), then write the OBJ
    # ourselves — pymeshlab's saver insists on copying material textures.
    try:
        ms.compute_texcoord_transfer_wedge_to_vertex()
    except Exception:
        ms.apply_filter("convert_perwedge_uv_into_pervertex_uv")
    m = ms.current_mesh()
    vm = m.vertex_matrix()
    fm = m.face_matrix()
    vn = m.vertex_normal_matrix()
    vt = m.vertex_tex_coord_matrix()
    n1 = fm.shape[0]
    out_obj = os.path.join(out_dir, name + ".obj")
    with open(out_obj, "w") as fo:
        fo.write(f"# GloomhavenVR import — Poly Haven '{name}' (CC0), processed by ph_pipeline.py\n")
        for i in range(vm.shape[0]):
            fo.write(f"v {vm[i,0]:.6f} {vm[i,1]:.6f} {vm[i,2]:.6f}\n")
        for i in range(vt.shape[0]):
            fo.write(f"vt {vt[i,0]:.6f} {vt[i,1]:.6f}\n")
        for i in range(vn.shape[0]):
            fo.write(f"vn {vn[i,0]:.4f} {vn[i,1]:.4f} {vn[i,2]:.4f}\n")
        for i in range(n1):
            a, b, c = fm[i, 0] + 1, fm[i, 1] + 1, fm[i, 2] + 1
            fo.write(f"f {a}/{a}/{a} {b}/{b}/{b} {c}/{c}/{c}\n")
    print(f"  mesh {name}: {n0} -> {n1} tris ({os.path.getsize(out_obj)/1e6:.1f}MB obj)")
    return n1


def process_card(name, r, tex_out):
    """Standalone twig/leaf atlas from a model asset: diffuse + alpha -> RGBA PNG.

    Used for photoscanned foliage whose MESH is unaffordable but whose alpha
    card atlas is exactly what a card-built tree needs (see CARDS)."""
    fj = files_json(r["asset"])
    adl = os.path.join(DL, r["asset"])
    dp = fetch(map_url(fj, r["diff"], r["res"], "jpg"),
               os.path.join(adl, "%s_%s.jpg" % (r["asset"], r["diff"])))
    ap = fetch(map_url(fj, r["alpha"], r["res"], "png"),
               os.path.join(adl, "%s_%s.png" % (r["asset"], r["alpha"])))
    d = load_rgb(dp)
    al = Image.open(ap).convert("L").resize(d.size, Image.LANCZOS)
    out = d.convert("RGBA")
    out.putalpha(al)
    out.save(os.path.join(tex_out, name + "_alb.png"))
    print("  wrote %s_alb.png %s" % (name, out.size))


def prune(out_dir, keep):
    """Delete outputs (and their .meta) that no recipe produces any more —
    dropped assets must not linger in the bundle."""
    for f in sorted(os.listdir(out_dir)):
        base = f[:-5] if f.endswith(".meta") else f
        if base in keep:
            continue
        os.remove(os.path.join(out_dir, f))
        print("  pruned", f)


def main():
    os.makedirs(DL, exist_ok=True)
    models_out = os.path.join(OUT, "Models")
    tex_out = os.path.join(OUT, "Textures")
    for d in (models_out, tex_out):
        os.makedirs(d, exist_ok=True)

    total_tris = {}
    for name, r in MODELS.items():
        print(f"== model {name}")
        fj = files_json(name)
        adl = os.path.join(DL, name)
        total_tris[name] = export_mesh(name, fj, r.get("tris"), models_out)
        dp = fetch(map_url(fj, "Diffuse", r["d"], "jpg"), os.path.join(adl, f"{name}_diff.jpg"))
        aop = fetch(map_url(fj, "AO", r["ao"], "jpg"), os.path.join(adl, f"{name}_ao.jpg")) \
            if r.get("ao") and "AO" in fj else None
        alp = fetch(map_url(fj, "Alpha", r["alpha"], "png"), os.path.join(adl, f"{name}_alpha.png")) \
            if r.get("alpha") and "Alpha" in fj else None
        ext = ".png" if alp else ".jpg"
        process_albedo(dp, aop, alp, os.path.join(tex_out, f"{name}_alb{ext}"))
        if r.get("n") and "nor_gl" in fj:
            np_ = fetch(map_url(fj, "nor_gl", r["n"], "jpg"), os.path.join(adl, f"{name}_nor.jpg"))
            load_rgb(np_).save(os.path.join(tex_out, f"{name}_nrm.jpg"), quality=95)

    # flame sprite (from brass_candleholders, CC0) for the procedural candles
    fj = files_json("brass_candleholders")
    if "flame_diff" in fj:
        fp = fetch(map_url(fj, "flame_diff", "1k", "jpg"),
                   os.path.join(DL, "brass_candleholders", "flame_diff.jpg"))
        load_rgb(fp).resize((256, 256), Image.LANCZOS).save(
            os.path.join(tex_out, "candle_flame_alb.jpg"), quality=92)
        print("  wrote candle_flame_alb.jpg")

    for name, r in SURFACES.items():
        print(f"== surface {name}")
        fj = files_json(name)
        adl = os.path.join(DL, name)
        dp = fetch(map_url(fj, "Diffuse", r["d"], "jpg"), os.path.join(adl, f"{name}_diff.jpg"))
        aop = fetch(map_url(fj, "AO", r["ao"], "jpg"), os.path.join(adl, f"{name}_ao.jpg")) \
            if r.get("ao") and "AO" in fj else None
        process_albedo(dp, aop, None, os.path.join(tex_out, f"{name}_alb.jpg"))
        if r.get("n"):
            np_ = fetch(map_url(fj, "nor_gl", r["n"], "jpg"), os.path.join(adl, f"{name}_nor.jpg"))
            load_rgb(np_).save(os.path.join(tex_out, f"{name}_nrm.jpg"), quality=95)

    for name, r in CARDS.items():
        print("== card %s" % name)
        process_card(name, r, tex_out)

    # ---- prune outputs no recipe produces any more ----
    keep_tex = {"candle_flame_alb.jpg"}
    for name in list(MODELS) + list(SURFACES):
        keep_tex |= {name + "_alb.jpg", name + "_alb.png", name + "_nrm.jpg"}
    keep_tex |= {name + "_alb.png" for name in CARDS}
    prune(tex_out, keep_tex)
    prune(models_out, {name + ".obj" for name in MODELS})

    print("== tri counts:", json.dumps(total_tris, indent=1))
    disk = 0
    for root, _, fs in os.walk(OUT):
        disk += sum(os.path.getsize(os.path.join(root, f)) for f in fs)
    print(f"DONE — Imported/ disk size {disk/1e6:.1f}MB")


if __name__ == "__main__":
    sys.exit(main())
