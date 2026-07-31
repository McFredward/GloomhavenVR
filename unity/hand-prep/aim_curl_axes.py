# aim_curl_axes.py — rewrite a hand rig FBX so every finger joint's local +X is a TRUE
# HINGE for the digit it drives, WITHOUT moving a single vertex or a single joint pivot.
#
# WHY ------------------------------------------------------------------------------------
# FingerCurler.cs rotates each joint about its LOCAL +X. A hinge only bends a segment in a
# plane when its axis is perpendicular to that segment; otherwise the segment sweeps a CONE
# and the finger swings sideways as it curls. The glove's finger chains are dead straight
# along world +Y while its mesh digits lean out of that line, so measured on the shipped
# asset (splay_check.py, slice-centroid fit):
#       pinky +15.2 deg, index -10.5 deg, thumb -9.0 deg, middle -0.3, ring +0.4  (left)
#       pinky -15.3 deg, index +10.1 deg, thumb +9.2 deg, middle -0.3, ring -0.5  (right)
# of hinge-vs-digit error — the "komisch gespreizte" fist, and the reason the runtime
# carries the GlovePinkyCounterAbduction hack for the worst of the five.
#
# WHAT THIS CHANGES ----------------------------------------------------------------------
# ONLY the ORIENTATION of the finger/thumb bone nodes (and, to keep them exactly where they
# were, the local translations of their children plus the matching bind matrices):
#     a_new = normalize(a_old - m (a_old . m))     m = the digit's measured centreline
#     frame X = a_new,  Y = m,  Z = X x Y
#   * every joint HEAD keeps its world position to the micrometre (accepted knuckle line),
#   * mesh vertices / normals / UVs / weights are copied through byte-for-byte,
#   * cluster Transform+TransformLink and the BindPose matrices are re-derived from the new
#     rest frames, so the REST POSE RENDERS IDENTICALLY (only the curl axis moved).
#
# The file is read and written with Blender's own FBX binary codec (io_scene_fbx.parse_fbx /
# encode_bin) — no scene import, so nothing regenerates bone rolls or custom normals.
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python aim_curl_axes.py -- \
#       <in.fbx> <out.fbx> [--fingers Thumb,Index,Middle,Ring,Pinky] [--report]
import math
import os
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, "/home/claw/blender-4.2/4.2/scripts/addons_core")

from io_scene_fbx import encode_bin, parse_fbx  # noqa: E402

import fbx_raw  # noqa: E402
import curl_check as cc  # noqa: E402
import aim_axes  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC, DST = pos[0], pos[1]
FING = (argv[argv.index("--fingers") + 1].split(",") if "--fingers" in argv
        else ["Thumb", "Index", "Middle", "Ring", "Pinky"])

D = np.float64


def euler_xyz(R):
    """Inverse of fbx_raw._rot(order=0): R = Rz*Ry*Rx -> (x, y, z) degrees."""
    sy = -R[2, 0]
    sy = min(1.0, max(-1.0, sy))
    y = math.asin(sy)
    if abs(sy) < 1 - 1e-9:
        x = math.atan2(R[2, 1], R[2, 2])
        z = math.atan2(R[1, 0], R[0, 0])
    else:                                   # gimbal lock
        x = math.atan2(-R[1, 2], R[1, 1])
        z = 0.0
    return [math.degrees(v) for v in (x, y, z)]


# ---- element helpers (parse_fbx gives tuples: (id, props, props_type, children)) --------
def kids(e, name):
    return [c for c in e[3] if c[0] == name]


def kid(e, name):
    k = kids(e, name)
    return k[0] if k else None


def set_p70(model, key, values):
    """Set (or insert) a Properties70 'P' entry with 3 doubles."""
    p70 = kid(model, b"Properties70")
    for p in kids(p70, b"P"):
        if p[1][0] == key:
            for i in range(3):
                p[1][4 + i] = float(values[i])
            return
    props = [key, key, b"", b"A", float(values[0]), float(values[1]), float(values[2])]
    ptypes = bytearray(b"SSSSDDD")
    # keep FBX's own ordering habit: transforms first
    p70[3].insert(0, (b"P", props, ptypes, []))


def set_matrix(elem, M):
    """Overwrite a 4x4 'Matrix'-style double array (column-major in FBX)."""
    flat = np.asarray(M, D).T.reshape(-1)
    elem[1][0] = np.array(flat, dtype=np.float64)


# ---- convert a parse_fbx tuple tree into an encode_bin tree -----------------------------
# NOTE: FBX 'B' is a bool and 'C' is a CHAR (Blender's data_types names them that way).
# Mapping 'C' onto add_bool silently rewrites the type byte and desynchronises every
# reader downstream of it — verify_aim.py catches it, but do not reintroduce it.
ADD = {
    ord('Y'): "add_int16", ord('Z'): "add_int8", ord('I'): "add_int32",
    ord('F'): "add_float32", ord('D'): "add_float64", ord('L'): "add_int64",
    ord('f'): "add_float32_array", ord('d'): "add_float64_array",
    ord('l'): "add_int64_array", ord('i'): "add_int32_array",
    ord('b'): "add_bool_array", ord('R'): "add_bytes",
}


def to_encode(e):
    out = encode_bin.FBXElem(e[0])
    for v, t in zip(e[1], e[2]):
        if t == ord('S'):
            out.add_string(v if isinstance(v, bytes) else str(v).encode("utf8"))
        elif t == ord('R'):
            out.add_bytes(bytes(v))
        elif t in (ord('d'), ord('f'), ord('l'), ord('i'), ord('b')):
            dt = {ord('d'): np.float64, ord('f'): np.float32, ord('l'): np.int64,
                  ord('i'): np.int32, ord('b'): bool}[t]
            arr = v if isinstance(v, np.ndarray) else np.array(v, dtype=dt)
            getattr(out, ADD[t])(np.ascontiguousarray(arr, dtype=dt))
        elif t == ord('C'):
            out.add_char(v if isinstance(v, bytes) else bytes([v]))
        elif t == ord('B'):
            out.add_bool(bool(v))
        else:
            getattr(out, ADD[t])(v)
    for c in e[3]:
        out.elems.append(to_encode(c))
    return out


def main():
    rig = cc.Rig(SRC)
    frames, diag = aim_axes.frames(SRC, verbose=True)
    frames = {b: R for b, R in frames.items() if b.split("_")[1] in FING}

    # 1) new WORLD matrices: rotation replaced, head position untouched.
    world_new = {}
    for n in rig.parent:
        W = rig.world(n).copy()
        if n in frames:
            W[:3, :3] = frames[n]
        world_new[n] = W
    # 2) new LOCAL matrices for every node whose own or whose parent's frame moved.
    touched = {}
    for n in rig.parent:
        p = rig.parent[n]
        if n not in frames and (p is None or p not in frames):
            continue
        P = world_new[p] if p is not None else np.eye(4)
        touched[n] = np.linalg.inv(P) @ world_new[n]

    root, version = parse_fbx.parse(SRC, use_namedtuple=False)
    objs = kid(root, b"Objects")
    models, name_of = {}, {}
    for m in kids(objs, b"Model"):
        mid = m[1][0]
        nm = m[1][1].decode("utf8").split("\x00\x01")[0]
        models[nm] = m
        name_of[mid] = nm

    # --- Model local transforms
    for n, L in touched.items():
        # the shipped matrices are only float32-accurate; re-orthonormalise (polar
        # decomposition) so the euler we write back is exact for a pure rotation.
        u, _, vt = np.linalg.svd(L[:3, :3])
        R = u @ vt
        assert abs(np.linalg.det(R) - 1) < 1e-9, (n, np.linalg.det(R))
        assert np.abs(R - L[:3, :3]).max() < 1e-5, (n, np.abs(R - L[:3, :3]).max())
        set_p70(models[n], b"Lcl Rotation", euler_xyz(R))
        set_p70(models[n], b"Lcl Translation", L[:3, 3])

    # --- skin cluster bind matrices (Transform = inverse of the bone's rest world here,
    #     because the mesh node's own transform is identity — asserted below)
    conns = [(c[1][1], c[1][2]) for c in kids(kid(root, b"Connections"), b"C")
             if c[1][0] == b"OO"]
    defs = {d[1][0]: d for d in kids(objs, b"Deformer")}
    nclus = 0
    for c, p in conns:
        if p in defs and c in name_of and name_of[c] in frames:
            d = defs[p]
            W = world_new[name_of[c]]
            old_link = np.array(kid(d, b"TransformLink")[1][0], D).reshape(4, 4).T
            # the shipped file stores float32-rounded values; 1e-5 (0.01 mm / 1e-3 deg)
            # is the precision floor of the ORIGINAL data, not of this rewrite.
            dev = max(np.abs(old_link - rig.world(name_of[c])).max(),
                      np.abs(np.array(kid(d, b"Transform")[1][0], D).reshape(4, 4).T
                             - np.linalg.inv(old_link)).max())
            assert dev < 1e-5, (name_of[c], dev)
            set_matrix(kid(d, b"TransformLink"), W)
            set_matrix(kid(d, b"Transform"), np.linalg.inv(W))
            nclus += 1

    # --- bind pose matrices
    npose = 0
    for pose in kids(objs, b"Pose"):
        for pn in kids(pose, b"PoseNode"):
            nid = kid(pn, b"Node")[1][0]
            nm = name_of.get(nid)
            if nm in touched:
                set_matrix(kid(pn, b"Matrix"), world_new[nm])
                npose += 1

    encode_bin.write(DST, to_encode(root), version)
    print(f"[aim] {SRC} -> {DST}: {len(touched)} node transforms, {nclus} clusters, "
          f"{npose} bind-pose matrices rewritten (fingers {','.join(FING)})")


main()
