# fist_metrics.py — numbers for the fist a rig actually produces, measured on the SKINNED
# mesh (fbx_skin), not on the bones.
#
# Per finger, the fingertip cloud = verts owned (>0.7) by that finger's Tip bone.
#   spread   : tip centroid spacing along the knuckle line (x in this frame). A natural
#              fist has neighbouring tips ~one finger width apart and the pinky tip INSIDE
#              the knuckle line; splay shows up as a growing gap and an outboard pinky.
#   closure  : distance from the tip centroid to Anchor_Palm (how closed the fist is)
#   lateral  : how far the tip moved sideways (along the knuckle line) between rest and
#              fist — a true hinge moves a fingertip almost purely in the curl plane, so
#              this is the direct measure of the coning error.
#
#   python3 fist_metrics.py <rig.fbx> [more.fbx ...] [--curl 1.0] [--pinky-rz 0]
import sys

import numpy as np

import curl_check as cc
import fbx_skin
import splay_check as sp


def metrics(path, curl=1.0, pinky_rz=0.0):
    s = fbx_skin.Skin(path)
    cl = sp.clusters(s.root)
    rest, _ = s.posed(0.0)
    fist, _ = s.posed(curl, pinky_rz)
    palm = s.rig.world("Anchor_Palm")[:3, 3]
    out = {}
    for f in cc.FINGERS:
        b = f"Anchor_{f}_Tip"
        w = np.zeros(len(s.V))
        if b in cl:
            i, ww = cl[b]
            np.add.at(w, i, ww)
        sel = w > 0.7
        out[f] = dict(rest=rest[sel].mean(0), fist=fist[sel].mean(0),
                      closure=float(np.linalg.norm(fist[sel].mean(0) - palm) * 1000))
    return out


if __name__ == "__main__":
    def opt(n, d):
        return float(sys.argv[sys.argv.index(n) + 1]) if n in sys.argv else d
    curl, rz = opt("--curl", 1.0), opt("--pinky-rz", 0.0)
    for p in [a for a in sys.argv[1:] if not a.startswith("--")
              and not a.replace(".", "").isdigit()]:
        m = metrics(p, curl, rz)
        print(f"== {p}  curl={curl} pinky_rz={rz}")
        print(f"   {'finger':8s} {'tip@rest (mm)':26s} {'tip@fist (mm)':26s} "
              f"{'lateral move':>13s} {'closure':>9s} {'gap to prev':>12s}")
        prev = None
        for f in ["Index", "Middle", "Ring", "Pinky"]:
            d = m[f]
            lat = (d["fist"][0] - d["rest"][0]) * 1000
            gap = np.linalg.norm(d["fist"] - prev) * 1000 if prev is not None else np.nan
            prev = d["fist"]
            print(f"   {f:8s} {str(np.round(d['rest']*1000,1)):26s} "
                  f"{str(np.round(d['fist']*1000,1)):26s} {lat:+10.1f} mm "
                  f"{d['closure']:7.1f}mm {gap:10.1f}mm")
        xs = [m[f]["fist"][0] * 1000 for f in ["Index", "Middle", "Ring", "Pinky"]]
        xr = [m[f]["rest"][0] * 1000 for f in ["Index", "Middle", "Ring", "Pinky"]]
        print(f"   fingertip span across the fist: rest {max(xr)-min(xr):.1f} mm  ->  "
              f"fist {max(xs)-min(xs):.1f} mm")
