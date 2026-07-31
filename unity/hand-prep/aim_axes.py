# aim_axes.py — compute (and optionally emit) CORRECTED bone frames for a hand rig FBX so
# that every finger joint's local +X is a true hinge for that finger's ACTUAL mesh tube.
#
# WHY (2026-07 glove fist round): the runtime rotates each joint about its local +X
# (FingerCurler.cs). A hinge only produces planar flexion when its axis is PERPENDICULAR to
# the segment it drives. The glove's finger chains are dead straight along world +Y while
# the mesh digits lean out of that line (pinky 20.7 deg, index 13.4 deg of tube-vs-chain),
# so local +X is 15.2 deg / 10.5 deg away from perpendicular to the digit it actually
# moves: the finger sweeps a CONE instead of a plane and the fist reads splayed. (That is
# the same measurement the runtime GlovePinkyCounterAbduction hack compensates on one
# finger.) Numbers are the slice-centroid fit of splay_check.py, VRHand_L_rig.fbx.
#
# THE FIX IS A FRAME CHANGE ONLY:
#   a_new = normalize(a_old - m (a_old . m))      # smallest rotation making the hinge
#                                                 # perpendicular to the mesh tube m
#   frame  X = a_new, Y = m, Z = X x Y
# Joint HEAD POSITIONS ARE NOT TOUCHED (the accepted knuckle line and every pivot stay
# exactly where they are) and the MESH IS NOT TOUCHED, so the rest pose renders identically;
# only the axis the runtime rotates about changes.
#
#   python3 aim_axes.py <rig.fbx> [--json out.json]
import json
import sys

import numpy as np

import fbx_raw
import curl_check as cc
import splay_check as sp

FINGERS = cc.FINGERS


def frames(path, verbose=True):
    """{bone: 3x3 new world rotation} + diagnostics per finger."""
    rig = cc.Rig(path)
    rows = sp.analyse(path, verbose=False)
    palm = rig.world("Anchor_Palm")
    n = palm[:3, :3] @ np.array([0, 1.0, 0])
    n /= np.linalg.norm(n)
    out, diag = {}, []
    for f, c, m, ang_cm, a_bone, a_mesh, ang_ax, cnt, cone, cone_pca in rows:
        cone_old = 90.0 - np.degrees(np.arccos(np.clip(a_bone @ m, -1, 1)))
        a = a_bone - m * (a_bone @ m)
        a /= np.linalg.norm(a)
        y = m.copy()
        z = np.cross(a, y)
        z /= np.linalg.norm(z)
        R = np.column_stack([a, y, z])
        assert abs(np.linalg.det(R) - 1) < 1e-9, np.linalg.det(R)
        turn = np.degrees(np.arccos(np.clip(a_bone @ a, -1, 1)))
        # palm-ward check: +X rotation must move a point on the finger toward +n
        d = np.cross(a, m)
        palm_dot = float(d @ n)
        for j in cc.JOINTS:
            out[f"Anchor_{f}_{j}"] = R
        diag.append(dict(finger=f, cone_before=float(cone_old), cone_after=0.0,
                         axis_turn=float(turn), palm_dot=palm_dot,
                         mesh_dir=[round(float(v), 4) for v in m],
                         axis_old=[round(float(v), 4) for v in a_bone],
                         axis_new=[round(float(v), 4) for v in a]))
        if verbose:
            print(f"   {f:8s} hinge-vs-digit error {cone_old:+6.2f} deg -> 0.00  "
                  f"(axis turned {turn:5.2f} deg)  palm-ward dot {palm_dot:+.3f}  "
                  f"axis {np.round(a_bone,3)} -> {np.round(a,3)}")
    return out, diag


def head_offsets(path):
    """Perpendicular distance from each joint HEAD to its finger's fitted tube axis —
    how far the (unmoved) pivots sit off the digit they swing."""
    rig = cc.Rig(path)
    s = sp.analyse(path, verbose=False)
    root = fbx_raw.parse(path)
    _, V = sp.geometry(root)
    cl = sp.clusters(root)
    res = {}
    for f, c, m, *_ in s:
        bones = [f"Anchor_{f}_{j}" for j in cc.JOINTS]
        w = np.zeros(len(V))
        for b in bones:
            if b in cl:
                i, ww = cl[b]
                np.add.at(w, i, ww)
        sel = V[w > 0.5]
        head = rig.world(bones[0])[:3, 3]
        kids = [k for k in rig.parent if rig.parent[k] == bones[2]]
        tl = rig.base[kids[0]][:3, 3] if kids else np.array([0, 0.02, 0])
        L = np.linalg.norm((rig.world(bones[2]) @ np.append(tl, 1.0))[:3] - head)
        s_ = (sel - head) @ m
        sel = sel[s_ > 0.33 * L]
        C = sel.mean(0)
        d = []
        for b in bones:
            h = rig.world(b)[:3, 3]
            r = (h - C) - m * ((h - C) @ m)
            d.append(float(np.linalg.norm(r) * 1000))
        res[f] = d
    return res


if __name__ == "__main__":
    p = sys.argv[1]
    print(f"== corrected hinge frames for {p}")
    fr, diag = frames(p)
    print("   pivot(head) offset from the fitted digit axis, mm (root/mid/tip):")
    for f, d in head_offsets(p).items():
        print(f"   {f:8s} {[round(x,1) for x in d]}")
    if "--json" in sys.argv:
        with open(sys.argv[sys.argv.index("--json") + 1], "w") as fh:
            json.dump({b: R.tolist() for b, R in fr.items()}, fh, indent=1)
