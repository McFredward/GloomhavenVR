#!/usr/bin/env python3
"""GloomhavenVR — HAUNT SOLID: the apparitions' source geometry.

WHY THIS FILE EXISTS
====================
USER RULING, hardware, ModBuild 143 (verbatim, abridged): "mach keine 2D
Fratzen, das sieht man, dass es 2D ist ... lieber wirklich eine Horrorgestalt
die einfach da steht ... generell keine 2D Pappaufsteller ... lieber einen 3D
Kopf und Silhouette die durch Fenster schaut".

The apparitions used to be a baked atlas stretched on a quad. In stereo, at
8-16 m, a textured quad reads as a cardboard standee every single time, and the
user caught it on every one of them. So the apparitions are REAL GEOMETRY now,
and real geometry of a human body is the one thing this project cannot draw out
of boxes and lathes: everybody knows what a person looks like.

FOLLOW-UP USER INSTRUCTION, same round: "Suche nach coolen assets im Internet
fuer ein cooles horror erlebnis statt es zwingend selber zu bauen." So this is
an IMPORT, not a model sheet.

WHAT WAS SEARCHED, AND WHAT WAS TAKEN
=====================================
Two earlier lanes in this project searched for downloadable characters and both
concluded "build it yourself" (the rat lane, ModBuild 140: every CC0 animal pack
ships a rigged FBX needing a SkinnedMeshRenderer and an Animator, which
BuildEnvironments.cs forbids in the bundle and which would run on per-client
time; the haunt lane, ModBuild 141: no CC0 face texture with an alpha channel
exists and the only CC0 human faces are 100k-600k-triangle museum life masks).
NEITHER BLOCKER APPLIES HERE, and that is why the search was worth repeating:

  * nothing here is skinned. The apparitions stand still (the Watcher's whole
    horror is that it does not move), lean, sway once, or cross in 0.34 s. A
    STATIC mesh plus the vertex-shader motion this project already does
    everywhere covers all of it, so a rig is not merely unnecessary, it is the
    thing we would have to strip out anyway;
  * "600k triangles" is a property of a DOWNLOAD, not of an asset. This repo
    already decimates every photoscan it ships (polyhaven_pipeline.py, pymeshlab
    quadric edge collapse). A heavy mesh is source, not a verdict.

Searched, in this order:
  * Poly Haven (the pipeline this repo already has) — no human figures at all.
    It is a materials/props library. Nothing to take.
  * Quaternius, Kenney, KayKit, itch.io CC0 character packs — REJECTED on look,
    not on licence. They are stylised low-poly game characters with big heads
    and blocky hands; dropped into a night forest they are exactly the
    "Kindergeburtstag" register the user threw out in ModBuild 141. A cartoon
    ghost is worse than a good silhouette.
  * Sketchfab, CC0 filter (api.sketchfab.com/v3/search?license=cc0) — the
    SEARCH api is open but every download route needs an OAuth token, so it
    cannot be a reproducible build step. Rejected on process, not on content.
  * Smithsonian Open Access (api.si.edu, DEMO_KEY) — REAL CC0, real download
    URLs, verified: 3d_package resources carry access=CC0 and hand out
    full-resolution and 150k OBJ zips with no authentication. There ARE human
    forms in it, and they were the strongest candidates found anywhere:
      - NPG "Abraham Lincoln" (npg_71_24, the Volk life mask), NPG "George
        Washington", NPG "Rutherford B. Hayes" plaster bust;
      - SAAM "Model of the Greek Slave", "Girl Skating", "The Dying Tecumseh",
        "The Wounded Scout, a Friend in the Swamp", "Helen Ruthven Waterston".
    REJECTED, and the reason is not technical. Every one of them is either an
    identifiable historical person or a 19th-century American sculpture whose
    subject is slavery or the displacement of Native Americans. Putting Lincoln's
    death-mask face behind a tree, or "The Greek Slave" in the woods as a thing
    that watches you, is a joke this mod should not make. A horror apparition
    has to be ANONYMOUS to work anyway — the moment you recognise it, it is a
    statue and not a person.
  * MakeHuman community base mesh hm08 — TAKEN. See below.

WHAT WAS TAKEN, AND ITS LICENCE
===============================
  SOURCE   https://raw.githubusercontent.com/makehumancommunity/makehuman/
           master/makehuman/data/3dobjs/base.obj
  LICENCE  CC0. The statement is inside the file's own header, which is why it
           is quoted here rather than linked: "This asset was explicitly
           released as CC0 in september 2020. ... The copyright holders at the
           point of the release to CC0 were: Copyright (C) 2020 Data Collection
           AB; Copyright (C) 2020 Joel Palmius; Copyright (C) 2020 Jonas
           Hauquier." (The MakeHuman APPLICATION is AGPL3; its ASSETS, this mesh
           among them, are not. We ship no MakeHuman code.)
  WHAT     "basemesh hm08": 19158 vertices, 18486 quads, of which the group
           `body` is the 13378-quad skin; the rest is helper geometry for
           clothing simulation and 100-odd `joint-*` marker cubes.

WHY IT IS THE RIGHT ONE, judged against the room and not against a turntable:
  * ANONYMOUS. It is the average of a scan corpus and looks like nobody. That is
    a defect for a character creator and exactly right for a thing in the dark.
  * IT IS A REAL BODY. Proportion is the whole read at 16 m — the Watcher is
    frightening because it is 2.7 m tall and still has a person's proportions.
    Hand-built tubes get the outline and lose the shoulders, the knees and the
    way a skull sits on a neck.
  * THE JOINT CUBES ARE A SKELETON WITHOUT A RIG. `joint-l-shoulder`,
    `joint-neck`, `joint-l-knee` and the rest are marker cubes in the mesh, so
    the poses below are computed from the asset's own measurements. That is how
    this file poses a static mesh with no bones, no FBX and no Animator: rotate
    a region about a named joint with a smooth falloff, at BAKE TIME, once.
  * NO TEXTURE IS NEEDED. These things are lit by the room's baked rig and are
    nearly black by design; what we take is the FORM. Nothing is added to the
    texture budget at all.

WHERE IT FALLS BACK TO HAND-BUILT GEOMETRY, and why:
  * the ROPE the hanged body hangs from — one tube, and no download is going to
    beat AddTube for a 12 mm cylinder;
  * the two EYESHINES in the understory, which are two ellipsoids;
  * the cellar's HANDPRINTS, which stay a mark on a wet wall: a print IS flat,
    it lies IN the wall plane, and it has correct parallax there. "Keine 2D
    Pappaufsteller" is about things that stand up in the air.
  * the cellar's LOOM/mass has no import either — it is this same body, scaled
    and hunched, because "a featureless mass" that is not built like a person is
    a rock.

OUTPUT -> Assets/Bundle/Environments/Imported/Models/haunt_*.obj
Run:  python3 haunt_figures_pipeline.py
"""
import os
import sys
import urllib.request

import numpy as np

try:
    import pymeshlab
except ImportError:  # pragma: no cover - build machine only
    sys.exit("pymeshlab is required: pip install pymeshlab")

_HERE = os.path.dirname(os.path.abspath(__file__))
DL = os.environ.get("PH_CACHE") or os.path.join(_HERE, "..", "..", "..", "..", ".ph-cache")
OUT = os.path.normpath(os.path.join(_HERE, "..", "Bundle", "Environments", "Imported", "Models"))
SRC_URL = ("https://raw.githubusercontent.com/makehumancommunity/makehuman/"
           "master/makehuman/data/3dobjs/base.obj")
# MakeHuman authors in decimetres; the whole of this project is metres.
MH_TO_M = 0.1


# --------------------------------------------------------------------- fetch
def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return dest
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers={"User-Agent": "GloomhavenVR-env-build/1.0"})
    with urllib.request.urlopen(req, timeout=120) as r, open(dest + ".part", "wb") as f:
        f.write(r.read())
    os.replace(dest + ".part", dest)
    print(f"  dl {os.path.basename(dest)} {os.path.getsize(dest)/1e6:.2f}MB")
    return dest


def parse_obj(path):
    """Vertices, plus every `g` group's face list. Quads are kept as quads here
    and triangulated once, at the end, so the pose code can work on the source
    topology."""
    verts, groups, cur = [], {}, None
    with open(path) as f:
        for line in f:
            if line.startswith("v "):
                verts.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("g "):
                cur = line.split(None, 1)[1].strip()
                groups.setdefault(cur, [])
            elif line.startswith("f "):
                groups[cur].append([int(t.split("/")[0]) - 1 for t in line.split()[1:]])
    return np.asarray(verts, dtype=np.float64), groups


# ---------------------------------------------------------------- posing kit
def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-9), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def rotate_about(V, pivot, axis, angle, weight):
    """Rodrigues about `pivot`, per-vertex `weight` in 0..1.

    THE FALLOFF IS THE WHOLE TRICK. A hard region cut would tear the shoulder
    open and the decimator would then weld the tear shut into a spike; a smooth
    weight is what a one-bone skin cluster does, computed here once instead of
    every frame on the headset."""
    axis = np.asarray(axis, dtype=np.float64)
    axis = axis / np.linalg.norm(axis)
    q = V - pivot
    ang = angle * weight[:, None]
    c, s = np.cos(ang), np.sin(ang)
    cross = np.cross(np.broadcast_to(axis, q.shape), q)
    dot = (q @ axis)[:, None]
    return pivot + q * c + cross * s + np.broadcast_to(axis, q.shape) * dot * (1.0 - c)


def arm_weight(V, shoulder, side):
    """1 out along the arm, 0 across the chest. `side` is +1 for the model's
    left (+x) arm."""
    x = V[:, 0] * side
    return smoothstep(shoulder[0] * side - 0.15, shoulder[0] * side + 0.75, x)


def above_weight(V, y0, y1):
    return smoothstep(y0, y1, V[:, 1])


def below_weight(V, y0, y1):
    return smoothstep(y0, y1, -V[:, 1])


# ------------------------------------------------------------------- export
def export(V, faces, path, target_tris, close_holes=True):
    """Triangulate, close, decimate, re-normal, write.

    CLOSED AND OUTWARD is a build gate on the C# side (AssertClosedAndOutward —
    this project has shipped inward-wound geometry three times), so the holes
    are closed HERE, where a hole is still a hole and not a shading artefact.
    The base mesh is open at the eye sockets, the mouth cavity and wherever a
    variant below cuts a neck or a chest off."""
    tris = []
    for f in faces:
        for k in range(1, len(f) - 1):
            tris.append([f[0], f[k], f[k + 1]])
    tris = np.asarray(tris, dtype=np.int32)

    # compact to the vertices this variant actually uses
    used = np.unique(tris)
    remap = np.full(len(V), -1, dtype=np.int32)
    remap[used] = np.arange(len(used))
    m = pymeshlab.Mesh(vertex_matrix=V[used], face_matrix=remap[tris])
    ms = pymeshlab.MeshSet()
    ms.add_mesh(m, "src")
    ms.meshing_remove_duplicate_vertices()
    ms.meshing_remove_unreferenced_vertices()
    if close_holes:
        # 900: the mouth cavity and a cut chest are both far bigger than the
        # default 30-edge limit, and a body with an open chest is a body you can
        # see the inside of the moment it turns.
        try:
            ms.meshing_close_holes(maxholesize=900, newfaceselected=False)
        except Exception as exc:                       # noqa: BLE001
            print(f"    close_holes skipped: {exc}")
    before = ms.current_mesh().face_number()
    if before > target_tris:
        ms.meshing_decimation_quadric_edge_collapse(
            targetfacenum=target_tris, preserveboundary=True, preservenormal=True,
            preservetopology=True, planarquadric=True, autoclean=True)
    mesh = ms.current_mesh()
    W, T = mesh.vertex_matrix().copy(), mesh.face_matrix().copy()
    # SEW THE LAST HOLES SHUT HERE and not in MeshLab. The decimator may break
    # topology (preservetopology=False buys a far better silhouette per
    # triangle) and leaves the odd three-edge hole where two folds met — the head
    # variant came out with exactly three unpaired edges. MeshLab's own repair
    # filter SEGFAULTS on these meshes, and C#'s AssertClosedAndOutward counts
    # unpaired edges rather than "nearly closed", so the loops are chained and
    # fanned by hand below. It is thirty lines and it cannot crash the build.
    W, T = sew(W, T)
    # AND THEN THE SLIVERS. C#'s AssertClosedAndOutward does not only count
    # unpaired edges, it also counts triangles whose FACE normal disagrees with
    # the sum of their own vertex normals — and a sliver of a few square microns
    # on a convex fold fails that every time, because its own normal is numerical
    # noise while its vertices' normals are area-weighted averages of the real
    # surface around it. Six of the bust's 760 triangles failed the bake on
    # exactly this. Dropping them opens tiny holes, so the sew runs again after.
    T = np.asarray([t for t in T
                    if np.linalg.norm(np.cross(W[t[1]] - W[t[0]], W[t[2]] - W[t[0]])) > 4e-8],
                   dtype=np.int32)
    W, T = sew(W, T)
    N = vertex_normals(W, T)
    W, T, N = reconcile(W, T, N)
    fn = np.cross(W[T[:, 1]] - W[T[:, 0]], W[T[:, 2]] - W[T[:, 0]])
    vn = N[T[:, 0]] + N[T[:, 1]] + N[T[:, 2]]
    wrong = int((np.einsum("ij,ij->i", fn, vn) < 0).sum())
    write_obj(path, W, T, N)
    lo, hi = W.min(0), W.max(0)
    vol = float(np.einsum("ij,ij->i", W[T[:, 0]], np.cross(W[T[:, 1]], W[T[:, 2]])).sum() / 6.0)
    print(f"  {os.path.basename(path):24s} {before:6d} -> {len(T):5d} tris, "
          f"{len(W):5d} verts, bbox "
          f"({lo[0]:.3f},{lo[1]:.3f},{lo[2]:.3f})..({hi[0]:.3f},{hi[1]:.3f},{hi[2]:.3f}) m, "
          f"volume {vol * 1e3:.1f} l, {open_edges(W, T)} unpaired edges, "
          f"{wrong} triangles against their normal")


def open_edges(V, F):
    """Unpaired directed edges, counted THE WAY C# COUNTS THEM — on positions
    welded to 10 microns, not on indices. reconcile() splits vertices that share
    a position, so an index-based count reports holes that are not there."""
    weld, ids = {}, []
    for v in V:
        k = tuple(np.round(np.asarray(v) * 1e5).astype(np.int64))
        ids.append(weld.setdefault(k, len(weld)))
    seen = {}
    for tri in F:
        for e in range(3):
            i, j = ids[int(tri[e])], ids[int(tri[(e + 1) % 3])]
            if i == j:
                continue
            if seen.get((j, i), 0) > 0:
                seen[(j, i)] -= 1
            else:
                seen[(i, j)] = seen.get((i, j), 0) + 1
    return sum(seen.values())


def sew(V, F):
    """Chain every unpaired directed edge into a loop and fan it onto its own
    centroid. Runs to a fixed point, because closing one loop can expose the
    next when two holes share a vertex."""
    V, F = V.copy(), [list(map(int, t)) for t in F]
    for _ in range(8):
        seen = {}
        for tri in F:
            for e in range(3):
                i, j = tri[e], tri[(e + 1) % 3]
                if i == j:
                    continue
                if seen.get((j, i), 0) > 0:
                    seen[(j, i)] -= 1
                else:
                    seen[(i, j)] = seen.get((i, j), 0) + 1
        boundary = [k for k, n in seen.items() if n > 0]
        if not boundary:
            break
        nxt = {}
        for i, j in boundary:
            nxt.setdefault(i, []).append(j)
        while nxt:
            start = next(iter(nxt))
            loop, cur = [start], start
            while cur in nxt and nxt[cur]:
                nxt_v = nxt[cur].pop()
                if not nxt[cur]:
                    del nxt[cur]
                if nxt_v == start:
                    break
                loop.append(nxt_v)
                cur = nxt_v
            if len(loop) < 3:
                continue
            c = len(V)
            V = np.vstack([V, V[loop].mean(0)])
            # THE PATCH RUNS AGAINST THE LOOP. The unpaired edges are the ones
            # the existing faces traverse i->j, so the triangle that pairs with
            # each of them has to traverse j->i — (c, loop[k+1], loop[k]) and not
            # (c, loop[k], loop[k+1]). Getting that backwards does not fail: it
            # adds a second copy of the same open boundary, the next pass sews
            # THAT, and the head came out with three edges shared by nine faces
            # and a spike where the fans piled up.
            for k in range(len(loop)):
                F.append([c, loop[(k + 1) % len(loop)], loop[k]])
    return V, np.asarray(F, dtype=np.int32)


def vertex_normals(V, F):
    """Area-weighted smooth normals, then RECONCILED WITH THE FACES.

    C#'s AssertClosedAndOutward rejects any triangle whose face normal disagrees
    with the sum of its own three vertex normals, and a plain area-weighted
    average fails that in a handful of places on any low-poly body: inside the
    mouth, between the fingers, in the ear, and on the flat caps this file sews
    over the cut neck. Those are real concave folds, not winding errors — the
    volume is positive and there are no unpaired edges — but the gate cannot tell
    the difference and it should not have to. So the few disagreeing vertices are
    pulled toward the faces they belong to until nobody disagrees. It converges in
    two or three passes and it moves nothing anywhere else."""
    fn = np.cross(V[F[:, 1]] - V[F[:, 0]], V[F[:, 2]] - V[F[:, 0]])
    n = np.zeros_like(V)
    for k in range(3):
        np.add.at(n, F[:, k], fn)
    n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
    return n


def reconcile(V, F, N):
    """Split the handful of triangles whose face normal disagrees with their own
    smooth vertex normals, and give the copies the FACE normal.

    C#'s AssertClosedAndOutward rejects any such triangle, and a plain
    area-weighted average produces a few dozen of them on any low-poly body: the
    sewn-shut mouth cavity, the gap between two fingers, the inside of an ear,
    the flat cap over a cut neck. Those are real concave folds and not winding
    errors — the volume is positive and there are no unpaired edges — but the
    gate cannot tell the difference and it should not have to.

    Averaging the normals toward the faces does NOT work and was tried first: on
    a fold where the surface doubles back, pulling a vertex toward one face
    pushes it away from its neighbour and the count oscillates instead of falling.
    Splitting is exact. The copies sit at exactly the same POSITION, so the
    closed-and-outward weld (which welds on position to 10 microns) still pairs
    every edge, and 4 % more vertices on a 1100-triangle body is nothing."""
    V = list(V)
    N = list(N)
    F = [list(map(int, t)) for t in F]
    for t in F:
        p, q, r = np.asarray(V[t[0]]), np.asarray(V[t[1]]), np.asarray(V[t[2]])
        fn = np.cross(q - p, r - p)
        ln = np.linalg.norm(fn)
        if ln < 1e-12:
            continue
        fn = fn / ln
        vs = np.asarray(N[t[0]]) + np.asarray(N[t[1]]) + np.asarray(N[t[2]])
        # A MARGIN, not "greater than zero". The C# gate runs on the apparition
        # AFTER it has been placed, and several of them are non-uniformly scaled
        # (the Watcher is 0.62 as wide as its height wants, the Stair figure
        # 0.82). Positions and normals take the same inverse transpose so the
        # agreement is preserved in exact arithmetic, but a triangle sitting at
        # dot = 1e-7 crosses over in float. 0.25 of the vertex-normal sum is
        # about nine degrees of headroom and it cost two more split triangles.
        if float(np.dot(fn, vs)) >= 0.25 * float(np.linalg.norm(vs)):
            continue
        for k in range(3):
            V.append(V[t[k]])
            N.append(fn)
            t[k] = len(V) - 1
    return np.asarray(V), np.asarray(F, dtype=np.int32), np.asarray(N)


def write_obj(path, V, F, N):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        f.write("# GloomhavenVR haunt figure — CC0, MakeHuman hm08 base mesh.\n"
                "# See Assets/Editor/haunt_figures_pipeline.py for provenance.\n")
        for v in V:
            f.write(f"v {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n")
        for v in N:
            f.write(f"vn {v[0]:.5f} {v[1]:.5f} {v[2]:.5f}\n")
        for t in F:
            a, b, c = int(t[0]) + 1, int(t[1]) + 1, int(t[2]) + 1
            f.write(f"f {a}//{a} {b}//{b} {c}//{c}\n")


def crop(faces, keep):
    """Faces all of whose corners are kept."""
    return [f for f in faces if all(keep[i] for i in f)]


def main():
    src = fetch(SRC_URL, os.path.join(DL, "_haunt", "makehuman_base.obj"))
    V0, groups = parse_obj(src)
    body = groups["body"]
    joints = {}
    for name, faces in groups.items():
        if name.startswith("joint-"):
            idx = sorted({i for f in faces for i in f})
            joints[name[6:]] = V0[idx].mean(0)
    print(f"  source: {len(V0)} verts, body {len(body)} quads, {len(joints)} joints")

    # ---- to room space: metres, feet on y = 0, facing +Z ---------------------
    V = V0 * MH_TO_M
    ground = joints["ground"][1] * MH_TO_M
    V[:, 1] -= ground
    J = {k: (v * MH_TO_M - np.array([0.0, ground, 0.0])) for k, v in joints.items()}
    height = V[sorted({i for f in body for i in f})][:, 1].max()
    print(f"  standing height {height:.3f} m")

    # ---- ARMS DOWN ----------------------------------------------------------
    # hm08 stands in an A-pose: the hands are 0.26 m out and 0.28 m down from the
    # shoulders, i.e. the arms hang at 43 degrees. A figure with its arms held
    # out is a figure DOING something; the Watcher's whole content is that it is
    # doing nothing, so the arms come down to 8 degrees off vertical.
    def arms_down(V, deg=-35.0):
        out = V.copy()
        for side, jn in ((+1.0, "l-shoulder"), (-1.0, "r-shoulder")):
            w = arm_weight(out, J[jn], side)
            # about the front-back axis, so the arm swings in the coronal plane
            out = rotate_about(out, J[jn], (0, 0, 1), np.deg2rad(deg) * side, w)
        return out

    standing = arms_down(V)

    # ---- the variants -------------------------------------------------------
    ymax = standing[:, 1].max()
    neck_y = J["neck"][1]
    jaw_y = J["jaw"][1]

    # HEAD: cut just under the jaw. Used for the face behind the trunk, the face
    # at floor level and the face over the bookshelf — three events whose subject
    # is a head and nothing else.
    keep_head = standing[:, 1] > neck_y - 0.02
    export(standing - np.array([0.0, (neck_y + ymax) * 0.5, 0.0]),
           crop(body, keep_head),
           os.path.join(OUT, "haunt_head.obj"), 420)

    # BUST: head, neck, both shoulders, cut at the sternum. This is the thing at
    # the barred window — "3D Kopf und Silhouette die durch Fenster schaut" —
    # and the cut is where the window sill crops it anyway.
    chest_y = J["spine-1"][1] + 0.06
    keep_bust = standing[:, 1] > chest_y
    export(standing - np.array([0.0, (chest_y + ymax) * 0.5, 0.0]),
           crop(body, keep_bust),
           os.path.join(OUT, "haunt_bust.obj"), 760)

    # FIGURE: the whole standing body, feet at y = 0. The Watcher.
    export(standing, body, os.path.join(OUT, "haunt_figure.obj"), 1100)

    # STRIDER: the same body mid-stride, for the two events that CROSS an
    # opening. One leg forward, one back, the near arm swung against them —
    # 0.34 s of it is all you get, and a standing figure sliding sideways for a
    # third of a second is the one thing that would read as a sprite.
    strider = standing.copy()
    for side, hip, knee in ((+1.0, "l-shoulder", "l-knee"), (-1.0, "r-shoulder", "r-knee")):
        w = below_weight(strider, -J["spine-4"][1], -(J["spine-4"][1] - 0.25))
        w = w * (strider[:, 0] * side > 0)
        strider = rotate_about(strider, J[knee] * np.array([1.0, 0.0, 1.0])
                               + np.array([0.0, J["spine-4"][1], 0.0]),
                               (1, 0, 0), np.deg2rad(24.0) * side, w)
        wa = arm_weight(strider, J[hip], side)
        strider = rotate_about(strider, J[hip], (1, 0, 0), np.deg2rad(-20.0) * side, wa)
    export(strider, body, os.path.join(OUT, "haunt_strider.obj"), 1100)

    # HANGED: "eine Silhouette ... von jemandem der sich erhaengt hat an einem
    # Baum". By the NECK, not by the feet — the previous round had it upside
    # down, which is a different (and sillier) picture. The pose is what a body
    # under its own weight does: the head is pulled up and lolled hard to one
    # side, the shoulders ride up, the arms hang dead, the legs are straight and
    # slightly crossed and the feet point down.
    hanged = arms_down(V, deg=-42.0)
    wh = above_weight(hanged, neck_y - 0.10, jaw_y + 0.05)
    hanged = rotate_about(hanged, J["neck"], (0, 0, 1), np.deg2rad(34.0), wh)
    hanged = rotate_about(hanged, J["neck"], (1, 0, 0), np.deg2rad(-16.0), wh)
    wl = below_weight(hanged, -J["spine-4"][1], -(J["spine-4"][1] - 0.55))
    hanged = rotate_about(hanged, J["spine-4"], (0, 0, 1), np.deg2rad(-7.0), wl)
    wf = below_weight(hanged, -0.28, -0.02)
    hanged = rotate_about(hanged, np.array([0.0, 0.10, 0.0]), (1, 0, 0), np.deg2rad(38.0), wf)
    export(hanged, body, os.path.join(OUT, "haunt_hanged.obj"), 1100)

    print("  done. Licence: CC0 (MakeHuman hm08 base mesh) — see this file's header.")


if __name__ == "__main__":
    main()
