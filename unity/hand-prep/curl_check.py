# curl_check.py — verify a hand rig FBX against the runtime CURL CONTRACT without going
# through Blender (whose FBX import can regenerate bone rolls).
#
# It reads the FBX node transforms exactly as an engine would (fbx_raw), rebuilds the rest
# pose, then replays the EXACT rotations src/GloomhavenVR/Hands/FingerCurler.cs applies:
#
#   root.localRotation = base_root * Euler(max.x*curl, 0, rootRz)   # rootRz: glove pinky only
#   mid .localRotation = base_mid  * Euler(max.y*curl, 0, 0)
#   tip .localRotation = base_tip  * Euler(max.z*curl, 0, 0)
#   fingers max = (75, 95, 65) deg, thumb max = (25, 45, 60) deg   (FingerCurler.cs:37-38)
#
# Frame (FBX space, after Blender's Z-up -> Y-up export conversion):
#   +Y = along the fingers, +Z = palm normal (out of the palm), -Z = back of the hand.
# Unity's import mirrors Z, which flips BOTH the axes and the rotation handedness, so a
# right-handed +X rotation here is Unity's left-handed +X rotation there: the palm-ward
# test below is frame-independent.
#
#   python3 curl_check.py <rig.fbx> [curl]
import sys

import numpy as np

import fbx_raw

FINGER_MAX = np.array([75.0, 95.0, 65.0])   # FingerCurler.DefaultFingerMaxAngles
THUMB_MAX = np.array([25.0, 45.0, 60.0])    # FingerCurler.DefaultThumbMaxAngles
PINKY_ABDUCT = 14.0                          # DefaultGlovePinkyCounterAbductionDeg
FINGERS = ["Thumb", "Index", "Middle", "Ring", "Pinky"]
JOINTS = ["Root", "Mid", "Tip"]


def unity_euler(x, y, z):
    """Unity's Quaternion.Euler(x,y,z) as a 3x3 matrix: intrinsic Z, then X, then Y."""
    rx, ry, rz = np.radians([x, y, z])

    def R(ax, a):
        c, s = np.cos(a), np.sin(a)
        return {0: np.array([[1, 0, 0], [0, c, -s], [0, s, c]]),
                1: np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]]),
                2: np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])}[ax]

    return R(1, ry) @ R(0, rx) @ R(2, rz)


class Rig:
    def __init__(self, path):
        root = fbx_raw.parse(path)
        ms, conns = fbx_raw.models(root)
        par = fbx_raw.model_parents(ms, conns)
        self.name = {i: ms[i][0] for i in ms}
        self.id = {ms[i][0]: i for i in ms}
        self.parent = {ms[i][0]: (ms[par[i]][0] if par.get(i, 0) in ms else None) for i in ms}
        self.base = {ms[i][0]: fbx_raw.local_matrix(ms[i][1]) for i in ms}
        self.local = dict(self.base)

    def reset(self):
        self.local = dict(self.base)

    def world(self, name):
        M = np.eye(4)
        chain = []
        n = name
        while n is not None:
            chain.append(n)
            n = self.parent[n]
        for n in reversed(chain):
            M = M @ self.local[n]
        return M

    def pose(self, curl, pinky_sign=0.0):
        """Apply the FingerCurler rotations for a uniform curl on every finger."""
        self.reset()
        for f in FINGERS:
            mx = THUMB_MAX if f == "Thumb" else FINGER_MAX
            for j, jn in enumerate(JOINTS):
                b = f"Anchor_{f}_{jn}"
                rz = pinky_sign * PINKY_ABDUCT * curl if (f == "Pinky" and j == 0) else 0.0
                M = self.base[b].copy()
                M[:3, :3] = M[:3, :3] @ unity_euler(mx[j] * curl, 0.0, rz)
                self.local[b] = M


def report(path, curl=1.0, pinky_sign=0.0):
    r = Rig(path)
    palm = r.world("Anchor_Palm")
    palm_n = palm[:3, :3] @ np.array([0, 1.0, 0])
    palm_n /= np.linalg.norm(palm_n)
    print(f"== {path}")
    print(f"   Anchor_Palm local +Y (palm normal) in FBX space: {np.round(palm_n, 4)}")
    print(f"   {'bone':22s} {'localX(world)':26s} {'tail move @+10deg':26s}  dot(palm_n)  verdict")
    bad = []
    for f in FINGERS:
        for jn in JOINTS:
            b = f"Anchor_{f}_{jn}"
            W = r.world(b)
            ax = W[:3, :3] @ np.array([1.0, 0, 0])
            ax /= np.linalg.norm(ax)
            # tail = child head in local space, or the bone's own +Y * length fallback
            kids = [n for n in r.parent if r.parent[n] == b]
            tail_local = r.base[kids[0]][:3, 3] if kids else np.array([0, 0.02, 0])
            p0 = (W @ np.append(tail_local, 1.0))[:3]
            M = r.base[b].copy()
            M[:3, :3] = M[:3, :3] @ unity_euler(10.0, 0, 0)
            saved = r.local[b]
            r.local[b] = M
            p1 = (r.world(b) @ np.append(tail_local, 1.0))[:3]
            r.local[b] = saved
            d = p1 - p0
            dn = d / max(np.linalg.norm(d), 1e-12)
            dot = float(dn @ palm_n)
            ok = dot > 0.3
            if not ok:
                bad.append((b, dot))
            print(f"   {b:22s} {str(np.round(ax,3)):26s} {str(np.round(dn,3)):26s}  {dot:+.3f}      "
                  f"{'palm-ward OK' if ok else '*** WRONG WAY ***' if dot < -0.3 else '?? sideways'}")
    # fingertip closure at full curl
    print(f"   -- full-curl (curl={curl}) fingertip vs palm anchor --")
    r.pose(curl, pinky_sign)
    pc = palm[:3, 3]
    for f in FINGERS:
        W = r.world(f"Anchor_{f}_Tip")
        kids = [n for n in r.parent if r.parent[n] == f"Anchor_{f}_Tip"]
        tl = r.base[kids[0]][:3, 3] if kids else np.array([0, 0.02, 0])
        tip = (W @ np.append(tl, 1.0))[:3]
        rest = Rig(path)
        Wr = rest.world(f"Anchor_{f}_Tip")
        tip0 = (Wr @ np.append(tl, 1.0))[:3]
        side = "PALM" if (tip - pc) @ palm_n > 0 else "BACK"
        print(f"   {f:8s} tip rest={np.round(tip0*1000,1)}mm  posed={np.round(tip*1000,1)}mm "
              f"dist_to_palm={np.linalg.norm(tip-pc)*1000:6.1f}mm  ends on the {side} side")
    return bad


if __name__ == "__main__":
    path = sys.argv[1]
    curl = float(sys.argv[2]) if len(sys.argv) > 2 else 1.0
    sign = float(sys.argv[3]) if len(sys.argv) > 3 else 0.0
    bad = report(path, curl, sign)
    print("   VIOLATIONS:", ", ".join(f"{b} ({d:+.2f})" for b, d in bad) or "none")
