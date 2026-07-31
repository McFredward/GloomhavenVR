# fbx_skin.py — linear-blend skinning of a hand rig FBX, done from the RAW FBX data
# (fbx_raw), so the posed mesh is exactly what an engine computes from the shipped file.
# Blender is used only to RENDER the resulting OBJ: no FBX import, no bone-roll
# regeneration, no custom-normal round trip in the verification path.
#
# CLI:
#   python3 fbx_skin.py <rig.fbx> <out.obj> [--curl 1.0] [--pinky-rz 0|14] [--style glove]
#
# The pose is the exact FingerCurler one (see curl_check.py).
import sys

import numpy as np

import fbx_raw
import curl_check as cc


class Skin:
    def __init__(self, path):
        self.root = fbx_raw.parse(path)
        self.rig = cc.Rig(path)
        objs = self.root.find("Objects")[0]
        g = objs.find("Geometry")[0]
        self.V = np.array(g.find("Vertices")[0].props[0], float).reshape(-1, 3)
        self.PVI = np.array(g.find("PolygonVertexIndex")[0].props[0], int)
        ln = g.find("LayerElementNormal")[0]
        self.N = np.array(ln.find("Normals")[0].props[0], float).reshape(-1, 3)
        self.N_map = ln.find("MappingInformationType")[0].props[0]
        ni = ln.find("NormalsIndex")
        self.NI = np.array(ni[0].props[0], int) if ni else None
        assert self.N_map == "ByPolygonVertex", self.N_map
        lu = g.find("LayerElementUV")[0]
        self.UV = np.array(lu.find("UV")[0].props[0], float).reshape(-1, 2)
        uvi = lu.find("UVIndex")
        self.UVI = np.array(uvi[0].props[0], int) if uvi else None
        # skin weights, per bone
        ms, conns = fbx_raw.models(self.root)
        defs = {d.props[0]: d for d in objs.find("Deformer")}
        bone_of = {p: ms[c][0] for c, p in conns if c in ms and p in defs}
        self.W, self.bind = {}, {}
        for cid, d in defs.items():
            if cid not in bone_of:
                continue
            idx, w = d.find("Indexes"), d.find("Weights")
            if idx and w:
                self.W[bone_of[cid]] = (np.array(idx[0].props[0], int),
                                        np.array(w[0].props[0], float))
            # FBX skinning: v_world = sum_b w * bone_world_now * Transform * v_meshlocal.
            # Blender writes the cluster's `Transform` as inverse(bone_world_at_bind) *
            # mesh_world_at_bind (verified against both rigs), so it IS the bind matrix —
            # and using it keeps this correct for rigs whose mesh node is not identity (the
            # styled hands carry a -90 deg X on the mesh node; the glove does not).
            TL = np.array(d.find("TransformLink")[0].props[0], float).reshape(4, 4).T
            T = np.array(d.find("Transform")[0].props[0], float).reshape(4, 4).T
            assert np.abs(TL - self.rig.world(bone_of[cid])).max() < 1e-5
            self.bind[bone_of[cid]] = T
        self.rest_world = {n: self.rig.world(n) for n in self.rig.parent}

    def weight_matrix(self):
        bones = sorted(self.W)
        M = np.zeros((len(self.V), len(bones)))
        for j, b in enumerate(bones):
            i, w = self.W[b]
            np.add.at(M[:, j], i, w)
        return bones, M

    def posed(self, curl, pinky_rz=0.0):
        self.rig.pose(curl, pinky_rz / cc.PINKY_ABDUCT if cc.PINKY_ABDUCT else 0.0)
        bones, M = self.weight_matrix()
        out = np.zeros_like(self.V)
        nrm = np.zeros_like(self.N)
        Vh = np.hstack([self.V, np.ones((len(self.V), 1))])
        # per-vertex normals for the normal transform: accumulate corner normals later
        for j, b in enumerate(bones):
            T = self.rig.world(b) @ self.bind[b]
            w = M[:, j]
            hit = w > 0
            if not hit.any():
                continue
            out[hit] += w[hit, None] * (Vh[hit] @ T.T)[:, :3]
        # corner normals: use the corner's vertex skinning matrix (rotation part)
        vidx = np.where(self.PVI < 0, -self.PVI - 1, self.PVI)
        R = np.zeros((len(self.V), 3, 3))
        for j, b in enumerate(bones):
            T = self.rig.world(b) @ self.bind[b]
            w = M[:, j]
            R += w[:, None, None] * T[:3, :3][None]
        # corner normals live in an IndexToDirect pool: transform each CORNER's normal with
        # its own vertex's skinning matrix (the pool is shared, so it must be expanded).
        Rc = R[vidx]
        Ncorner = self.N[self.NI] if self.NI is not None else self.N
        n = np.einsum("nij,nj->ni", Rc, Ncorner)
        ln = np.linalg.norm(n, axis=1, keepdims=True)
        nrm = n / np.maximum(ln, 1e-12)
        self.rig.reset()
        return out, nrm

    def write_obj(self, path, verts, normals, mtl=None):
        vidx = np.where(self.PVI < 0, -self.PVI - 1, self.PVI)
        uvi = self.UVI if self.UVI is not None else np.arange(len(self.PVI))
        with open(path, "w") as f:
            if mtl:
                f.write(f"mtllib {mtl}\nusemtl hand\n")
            for v in verts:
                f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")
            for t in self.UV:
                f.write(f"vt {t[0]:.6f} {t[1]:.6f}\n")
            for n in normals:
                f.write(f"vn {n[0]:.6f} {n[1]:.6f} {n[2]:.6f}\n")
            face = []
            for k, p in enumerate(self.PVI):
                face.append(f"{vidx[k]+1}/{uvi[k]+1}/{k+1}")
                if p < 0:
                    f.write("f " + " ".join(face) + "\n")
                    face = []


if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    def opt(n, d):
        return float(sys.argv[sys.argv.index(n) + 1]) if n in sys.argv else d
    s = Skin(src)
    v, n = s.posed(opt("--curl", 1.0), opt("--pinky-rz", 0.0))
    s.write_obj(out, v, n, mtl=sys.argv[sys.argv.index("--mtl") + 1] if "--mtl" in sys.argv else None)
    print(f"[skin] {src} curl={opt('--curl',1.0)} -> {out} ({len(v)} verts, {len(s.PVI)} corners)")
