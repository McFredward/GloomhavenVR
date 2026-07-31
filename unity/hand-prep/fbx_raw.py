# fbx_raw.py — minimal FBX *binary* reader used to verify what UNITY will read out of a
# hand rig FBX, independent of Blender's import heuristics (which can regenerate bone
# rolls). Exposes the node tree and a helper that rebuilds every Model's local transform
# from its Lcl* / *Offset / *Pivot properties exactly as an FBX consumer would.
#
# Usage as a library:
#   import fbx_raw
#   root = fbx_raw.parse("file.fbx")
#   models, conns = fbx_raw.models(root)
import struct, zlib, sys

import numpy as np


class Node:
    __slots__ = ("name", "props", "children")

    def __init__(self, name, props, children):
        self.name, self.props, self.children = name, props, children

    def find(self, name):
        return [c for c in self.children if c.name == name]

    def __repr__(self):
        return f"<{self.name} props={len(self.props)} kids={len(self.children)}>"


def _read_prop(f):
    t = f.read(1).decode("ascii")
    if t == "Y":
        return struct.unpack("<h", f.read(2))[0]
    if t == "C":                      # char (1 byte)
        return struct.unpack("<c", f.read(1))[0]
    if t == "B":                      # bool (1 byte)
        return struct.unpack("<?", f.read(1))[0]
    if t == "Z":
        return struct.unpack("<b", f.read(1))[0]
    if t == "I":
        return struct.unpack("<i", f.read(4))[0]
    if t == "F":
        return struct.unpack("<f", f.read(4))[0]
    if t == "D":
        return struct.unpack("<d", f.read(8))[0]
    if t == "L":
        return struct.unpack("<q", f.read(8))[0]
    if t in "fdlbi":
        n, enc, cl = struct.unpack("<III", f.read(12))
        raw = f.read(cl)
        if enc:
            raw = zlib.decompress(raw)
        dt = {"f": "<f4", "d": "<f8", "l": "<i8", "i": "<i4", "b": "|b1"}[t]
        return np.frombuffer(raw, dtype=dt, count=n)
    if t == "S" or t == "R":
        n = struct.unpack("<I", f.read(4))[0]
        raw = f.read(n)
        return raw.decode("utf-8", "replace") if t == "S" else raw
    raise ValueError(f"unknown prop type {t!r} at {f.tell()}")


def _read_node(f, ver):
    if ver >= 7500:
        end, nprops, plen = struct.unpack("<QQQ", f.read(24))
        nlen = struct.unpack("<B", f.read(1))[0]
        hdr = 25
    else:
        end, nprops, plen = struct.unpack("<III", f.read(12))
        nlen = struct.unpack("<B", f.read(1))[0]
        hdr = 13
    if end == 0:
        return None
    name = f.read(nlen).decode("utf-8", "replace")
    props = [_read_prop(f) for _ in range(nprops)]
    kids = []
    while f.tell() < end:
        k = _read_node(f, ver)
        if k is None:
            break
        kids.append(k)
    f.seek(end)
    return Node(name, props, kids)


def parse(path):
    with open(path, "rb") as f:
        magic = f.read(23)
        assert magic.startswith(b"Kaydara FBX Binary"), magic
        ver = struct.unpack("<I", f.read(4))[0]
        kids = []
        while True:
            n = _read_node(f, ver)
            if n is None:
                break
            kids.append(n)
            # footer / padding sentinel
            if f.tell() > len(open(path, "rb").read()) - 200:
                break
    return Node("ROOT", [ver], kids)


def props70(model):
    """{name: [values...]} of a Model's Properties70 block."""
    out = {}
    for p70 in model.find("Properties70"):
        for p in p70.find("P"):
            out[p.props[0]] = list(p.props[4:])
    return out


def model_parents(ms, conns):
    """child_model_id -> parent_model_id (0 = scene root).

    A bone Model is ALSO connected as the child of its skin cluster, so the raw
    connection list holds more than one entry per bone; only Model->Model (or
    Model->root) links describe the transform hierarchy."""
    out = {}
    for c, p in conns:
        if c in ms and (p == 0 or p in ms) and c not in out:
            out[c] = p
    return out


def models(root):
    """(id -> (name, Node)), connections list [(child_id, parent_id)]"""
    objs = root.find("Objects")[0]
    ms = {}
    for m in objs.find("Model"):
        mid = m.props[0]
        nm = m.props[1].split("\x00\x01")[0]
        ms[mid] = (nm, m)
    conns = []
    for c in root.find("Connections")[0].find("C"):
        if c.props[0] == "OO":
            conns.append((c.props[1], c.props[2]))
    return ms, conns


def _rot(order, e):
    """Euler (degrees, XYZ values) -> 3x3 matrix. FBX RotationOrder 0 = XYZ (R = Rz*Ry*Rx)."""
    rx, ry, rz = [np.radians(v) for v in e]

    def R(ax, a):
        c, s = np.cos(a), np.sin(a)
        if ax == 0:
            return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
        if ax == 1:
            return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
        return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])

    assert order == 0, f"rotation order {order} unsupported"
    return R(2, rz) @ R(1, ry) @ R(0, rx)


def local_matrix(model):
    """4x4 local transform of a Model node from its FBX properties (no pivots used by
    Blender's exporter, but assert that)."""
    p = props70(model)
    t = np.array(p.get("Lcl Translation", [0.0, 0.0, 0.0]), float)
    r = np.array(p.get("Lcl Rotation", [0.0, 0.0, 0.0]), float)
    s = np.array(p.get("Lcl Scaling", [1.0, 1.0, 1.0]), float)
    order = int(p.get("RotationOrder", [0])[0])
    for k in ("PreRotation", "PostRotation", "RotationOffset", "RotationPivot",
              "ScalingOffset", "ScalingPivot", "GeometricTranslation",
              "GeometricRotation", "GeometricScaling"):
        v = p.get(k)
        if v is not None and any(abs(float(x)) > 1e-12 for x in v):
            if k == "GeometricScaling" and all(abs(float(x) - 1) < 1e-12 for x in v):
                continue
            raise AssertionError(f"{model.props[1]}: nonzero {k} = {v} (unsupported)")
    M = np.eye(4)
    M[:3, :3] = _rot(order, r) @ np.diag(s)
    M[:3, 3] = t
    return M


if __name__ == "__main__":
    root = parse(sys.argv[1])
    ms, conns = models(root)
    parent = model_parents(ms, conns)
    for mid, (nm, node) in ms.items():
        pid = parent.get(mid, 0)
        pn = ms[pid][0] if pid in ms else "(root)"
        M = local_matrix(node)
        print(f"{nm:24s} parent={pn:24s} t={np.round(M[:3,3],6)}")
