# verify_aim.py — prove that aim_curl_axes.py changed the CURL AXES and nothing else.
#
# Asserted, not assumed (both files read raw, no Blender scene import):
#   1. mesh identity ....... vertices, polygon indices, custom normals (+ index pool), UVs,
#                            material assignment: bit-for-bit equal
#   2. skin identity ....... same bones, same vertex indices, same weights, every vertex
#                            still carries a normalised weight set
#   3. rig safety .......... 19/19 Anchor_* bones, every joint HEAD within 1e-4 mm at rest,
#                            rest-pose bounding box unchanged
#   4. rest pose ........... the SKINNED rest mesh (bind matrices applied) is identical to
#                            the micrometre — the accepted open hand cannot have moved
#   5. contract ............ every joint's local +X still curls palm-ward, and now within
#                            0.1 deg of perpendicular to its own digit
#
#   python3 verify_aim.py <before.fbx> <after.fbx>
import sys

import numpy as np

import fbx_raw
import curl_check as cc
import fbx_skin
import splay_check as sp

A, B = sys.argv[1], sys.argv[2]
ok = True


def check(name, cond, detail=""):
    global ok
    ok = ok and bool(cond)
    print(f"   [{'OK ' if cond else 'FAIL'}] {name} {detail}")


ra, rb = fbx_raw.parse(A), fbx_raw.parse(B)
ga = ra.find("Objects")[0].find("Geometry")[0]
gb = rb.find("Objects")[0].find("Geometry")[0]
print(f"== mesh identity  {A}  vs  {B}")
for tag, path in [("Vertices", ["Vertices"]), ("PolygonVertexIndex", ["PolygonVertexIndex"]),
                  ("Normals", ["LayerElementNormal", "Normals"]),
                  ("NormalsIndex", ["LayerElementNormal", "NormalsIndex"]),
                  ("UV", ["LayerElementUV", "UV"]),
                  ("UVIndex", ["LayerElementUV", "UVIndex"]),
                  ("Materials", ["LayerElementMaterial", "Materials"]),
                  ("Smoothing", ["LayerElementSmoothing", "Smoothing"])]:
    na, nb = ga, gb
    for p in path:
        na, nb = na.find(p)[0], nb.find(p)[0]
    va, vb = np.array(na.props[0]), np.array(nb.props[0])
    check(f"{tag:20s} n={len(va)}", va.shape == vb.shape and np.array_equal(va, vb))

print("== skin identity")
ca, cb = sp.clusters(ra), sp.clusters(rb)
check("same bone set", set(ca) == set(cb))
same = all(np.array_equal(ca[b][0], cb[b][0]) and np.array_equal(ca[b][1], cb[b][1])
           for b in ca)
check("indices+weights identical", same)
_, V = sp.geometry(ra)
w = np.zeros(len(V))
for b in ca:
    i, ww = ca[b]
    np.add.at(w, i, ww)
check("every vertex weighted (sum≈1)", np.abs(w - 1).max() < 1e-4,
      f"max |sum-1| = {np.abs(w - 1).max():.2e}")

print("== rig safety")
riga, rigb = cc.Rig(A), cc.Rig(B)
anchors = [n for n in riga.parent if n.startswith("Anchor_")]
check("bone count", len(anchors) == 19 and set(anchors) == {n for n in rigb.parent
                                                            if n.startswith("Anchor_")},
      f"{len(anchors)}/19")
maxd = max(np.linalg.norm(riga.world(n)[:3, 3] - rigb.world(n)[:3, 3]) for n in anchors)
check("joint heads unmoved", maxd < 1e-7, f"max {maxd*1e6:.4f} µm")

print("== rest pose (skinned with the bind matrices)")
sa, sb = fbx_skin.Skin(A), fbx_skin.Skin(B)
va, na_ = sa.posed(0.0)
vb, nb_ = sb.posed(0.0)
dv = np.abs(va - vb).max()
dn = np.abs(na_ - nb_).max()
# tolerance = the float32 quantisation of the bind matrices stored in the ORIGINAL file
# (the rewrite stores exact float64); 1 um is 5e-6 of the hand's 190 mm extent.
check("rest vertices identical", dv < 1e-6, f"max {dv*1e9:.1f} nm")
check("rest normals identical", dn < 1e-5, f"max {dn:.2e}")
ea = np.ptp(va, axis=0)
eb = np.ptp(vb, axis=0)
check("rest extent unchanged", np.abs(ea - eb).max() < 1e-6,
      f"{np.round(ea*1000,3)} mm")

print("== curl contract (after)")
bad = cc.report(B, 1.0, 0.0)
check("all joints curl palm-ward", not bad, str(bad))
rows = sp.analyse(B, verbose=False)
for r in rows:
    f, cone = r[0], r[8]
    check(f"{f:8s} hinge ⟂ digit", abs(cone) < 0.05, f"coning {cone:+.4f} deg")

print("\nRESULT:", "ALL CHECKS PASSED" if ok else "FAILURES PRESENT")
sys.exit(0 if ok else 1)
