# splay_check.py — measure, per finger, the angle between the bone chain (whose local +X
# the runtime rotates) and the finger's ACTUAL mesh tube, i.e. the SPLAY error of a fist.
#
# Reads the FBX directly (fbx_raw): geometry vertices + skin cluster weights + bone nodes,
# so it reports what the shipped asset does, not what Blender re-imports.
#
# For each finger:
#   chain dir  c  = normalize(tip_end - mcp_head)          (world/FBX space)
#   mesh dir   m  = PCA axis of the vertices skinned to that finger's 3 bones (distal 2/3)
#   flexion axis the runtime uses    a_bone    = the root bone's local +X in world
#   anatomical flexion axis          a_mesh    = normalize(m x n), n = palm normal
#     (rotating about a_mesh by +theta moves the tip along +n = into the palm, and keeps the
#      motion in the finger's OWN plane span(m, n) -> no sideways splay)
#   SPLAY ERROR = angle(a_bone, a_mesh) = angle(c, m) projected appropriately.
#
#   python3 splay_check.py <rig.fbx>
import sys

import numpy as np

import fbx_raw
import curl_check as cc


def geometry(root):
    """(geometry node, vertices in WORLD space).

    The vertices are stored in mesh-node-local space and the styled rigs put a -90 deg X
    on that node (the glove does not), so they must be pushed through the mesh node's own
    transform before they can be compared with bone world positions."""
    objs = root.find("Objects")[0]
    g = objs.find("Geometry")[0]
    V = np.array(g.find("Vertices")[0].props[0], float).reshape(-1, 3)
    ms, conns = fbx_raw.models(root)
    par = fbx_raw.model_parents(ms, conns)
    mesh = [i for i in ms if ms[i][0].endswith("_mesh")]
    if mesh:
        M, n = np.eye(4), mesh[0]
        chain = []
        while n in ms:
            chain.append(n)
            n = par.get(n, 0)
        for n in reversed(chain):
            M = M @ fbx_raw.local_matrix(ms[n][1])
        V = (np.hstack([V, np.ones((len(V), 1))]) @ M.T)[:, :3]
    return g, V


def clusters(root):
    """bone name -> (indices, weights) from the skin SubDeformers."""
    objs = root.find("Objects")[0]
    ms, conns = fbx_raw.models(root)
    defs = {d.props[0]: d for d in objs.find("Deformer")}
    # cluster -> bone model
    bone_of = {}
    for c, p in conns:
        if c in ms and p in defs:
            bone_of[p] = ms[c][0]
    out = {}
    for cid, d in defs.items():
        if cid not in bone_of:
            continue
        idx = d.find("Indexes")
        w = d.find("Weights")
        if not idx or not w:
            continue
        out[bone_of[cid]] = (np.array(idx[0].props[0], int), np.array(w[0].props[0], float))
    return out


def pca(P):
    C = P.mean(0)
    u, s, vt = np.linalg.svd(P - C, full_matrices=False)
    return C, vt[0]


def centerline(P, head, c, L, lo=0.15, hi=1.0, nslice=10):
    """Digit direction from CROSS-SECTION CENTROIDS along the seed chain.

    More trustworthy than a PCA over the whole tube: the tube's radius varies (knuckle
    bulge at the base, tapering + rounded cap at the tip) and a volume PCA tilts toward
    the fat end. Returns (centroid, unit direction) from a count-weighted total-least-
    squares line fit through the slice centroids."""
    s = (P - head) @ c
    edges = np.linspace(lo * L, hi * L, nslice + 1)
    cent, wts = [], []
    for a, b in zip(edges[:-1], edges[1:]):
        m = (s >= a) & (s < b)
        if m.sum() >= 8:
            cent.append(P[m].mean(0))
            wts.append(m.sum())
    cent, wts = np.array(cent), np.array(wts, float)
    C = (cent * wts[:, None]).sum(0) / wts.sum()
    _, _, vt = np.linalg.svd((cent - C) * np.sqrt(wts)[:, None], full_matrices=False)
    u = vt[0]
    if u @ c < 0:
        u = -u
    return C, u, len(cent)


def analyse(path, verbose=True):
    root = fbx_raw.parse(path)
    rig = cc.Rig(path)
    _, V = geometry(root)
    cl = clusters(root)
    palm = rig.world("Anchor_Palm")
    n = palm[:3, :3] @ np.array([0, 1.0, 0])
    n /= np.linalg.norm(n)
    rows = []
    if verbose:
        print(f"== {path}  ({len(V)} verts, {len(cl)} skin clusters), palm normal {np.round(n,3)}")
        print(f"   {'finger':8s} {'chain dir':24s} {'mesh dir':24s} tube-vs-chain  "
              f"{'a_bone(local+X)':24s} {'a_mesh(anat.)':24s} AXIS ERR")
    for f in cc.FINGERS:
        bones = [f"Anchor_{f}_{j}" for j in cc.JOINTS]
        # Seed chain from the joint HEADS plus the mesh's own reach, never from a bone
        # direction: a frame change must not move the measurement window, or before/after
        # comparisons stop being comparable.
        head = rig.world(bones[0])[:3, 3]
        c = rig.world(bones[2])[:3, 3] - head
        c /= np.linalg.norm(c)
        w = np.zeros(len(V))
        for b in bones:
            if b in cl:
                i, ww = cl[b]
                np.add.at(w, i, ww)
        sel = V[w > 0.5]
        L = float(np.percentile((sel - head) @ c, 98))
        _, m, nsl = centerline(sel, head, c, L)
        s = (sel - head) @ c
        _, mp = pca(sel[(s > 0.33 * L) & (s < 1.05 * L)])
        if mp @ c < 0:
            mp = -mp
        self_pca = mp
        a_bone = rig.world(bones[0])[:3, :3] @ np.array([1.0, 0, 0])
        a_mesh = np.cross(m, n)
        a_mesh /= np.linalg.norm(a_mesh)
        ang_cm = np.degrees(np.arccos(np.clip(c @ m, -1, 1)))
        ang_ax = np.degrees(np.arccos(np.clip(a_bone @ a_mesh, -1, 1)))
        # CONING = how far the hinge axis is from perpendicular to the digit it drives.
        # 0 => planar flexion; anything else sweeps a cone => sideways splay in a fist.
        cone = 90.0 - np.degrees(np.arccos(np.clip(a_bone @ m, -1, 1)))
        cone_pca = 90.0 - np.degrees(np.arccos(np.clip(a_bone @ self_pca, -1, 1)))
        rows.append((f, c, m, ang_cm, a_bone, a_mesh, ang_ax, len(sel), cone, cone_pca))
        if verbose:
            print(f"   {f:8s} {str(np.round(c,3)):24s} {str(np.round(m,3)):24s} "
                  f"{ang_cm:6.1f} deg    {str(np.round(a_bone,3)):24s} "
                  f"{str(np.round(a_mesh,3)):24s} {ang_ax:6.1f} deg  "
                  f"CONING {cone:+6.2f} (pca {cone_pca:+6.2f})  n={len(sel)}/{nsl}")
    return rows


if __name__ == "__main__":
    for p in sys.argv[1:]:
        analyse(p)
