#!/usr/bin/env python3
"""gen_anchorcheck.py -- re-import the SHIPPED FBX and reproduce, in Unity coordinates,
exactly what `BuildBoard.cs` and `BoardFrame.cs` compute from the anchors.

Measuring the file rather than the generator is the point: the axis convention survives a
Blender->FBX->Unity round trip that flips X and swaps two axes, and every previous board
bug in this area came from reasoning about that chain instead of reading it back.

What it asserts, all of it taken from the code that consumes the asset:

  * Unity local = (-X_fbx, Y_fbx, Z_fbx)   -- Unity's FBX importer negates X.
  * n = cross(Slot2-Slot1, ShortRest-LongRest) must point out of the UNDECORATED BACK
    (BuildBoard.cs: "n = cross(u,v) points out the UNDECORATED back (verified by render)").
  * The decorated face must therefore be the MAX-Y_fbx end of the mesh.
  * Slot1->Slot2 must be the long axis, ShortRest->LongRest the short one, and the two must
    be perpendicular -- they are the board's orientation basis, not decorative markers.
  * ButtonSeat1/2/3 must run TOP to BOTTOM along the short axis, evenly spaced.
  * The six-term placement centroid BuildBoard.AnchorPlaneCentre uses (four frame anchors +
    first and last seat) must sit on the board's centre in the board plane.

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_anchorcheck.py -- [<fbx> ...]
"""

import os
import sys

import bpy

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
TABLE = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Table")
DEFAULT = ["PlayTray_prepped.fbx", "PlayTray_9capjqp6.fbx", "PlayTray_16vm268h.fbx"]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1],
            a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def norm(a):
    m = (a[0] ** 2 + a[1] ** 2 + a[2] ** 2) ** 0.5
    return (a[0] / m, a[1] / m, a[2] / m) if m else a


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    files = args or [os.path.join(TABLE, f) for f in DEFAULT]
    bad = 0
    for path in files:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=path)
        mesh = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]

        # THE ONE COORDINATE CONVERSION THAT MATTERS, and the first version of this file got
        # it wrong: reading `matrix_local` assumes the Y-up correction sits on the ROOT
        # OBJECT, which is only true for FBXs that bake it.  Read WORLD space instead, which
        # is correct whichever way the exporter chose:
        #     blender_world = Rx(+90) . fbx_world            (the importer's correction)
        #  => fbx_world     = (x, z, -y) of blender_world
        #  => unity         = (-X, Y, Z) of fbx_world        (Unity negates X)
        #  => unity         = (-x, z, -y) of blender_world
        def to_unity(p):
            return (-p.x, p.z, -p.y)

        A = {}
        for ob in bpy.context.scene.objects:
            if ob.type == 'EMPTY':
                A[ob.name] = to_unity(ob.matrix_world.translation)
        vy = [to_unity(mesh.matrix_world @ v.co)[1] for v in mesh.data.vertices]
        print("=== %s" % os.path.basename(path))
        print("    mesh local Y (Unity Y, the board normal): %.4f .. %.4f" % (min(vy), max(vy)))

        missing = [n for n in ("Slot1", "Slot2", "ShortRestToken", "LongRestToken",
                               "ButtonSeat1", "ButtonSeat2", "ButtonSeat3",
                               "ConfirmButton", "UndoButton", "SkipButton") if n not in A]
        if missing:
            print("    FAIL missing anchors: %s" % missing)
            bad += 1
            continue

        u = norm(sub(A["Slot2"], A["Slot1"]))
        sv = norm(sub(A["ShortRestToken"], A["LongRestToken"]))
        n = norm(cross(u, sv))
        perp = abs(dot(u, sv))
        face_y = max(vy)
        # n points out of the UNDECORATED back; the decorated face is -n.  The decorated
        # face is the MAX-Y end iff (-n).y > 0 iff n.y < 0.
        ok_n = n[1] < -0.999
        ok_u = abs(u[0]) > 0.999
        ok_s = abs(sv[2]) > 0.999
        ok_perp = perp < 1e-6
        print("    u = Slot1->Slot2      = (%+.4f,%+.4f,%+.4f)   long axis: %s" %
              (u + (("OK" if ok_u else "FAIL"),)))
        print("    s = LongRest->ShortRest= (%+.4f,%+.4f,%+.4f)  short axis: %s  perp |u.s|=%.2e %s" %
              (sv + (("OK" if ok_s else "FAIL"), perp, "OK" if ok_perp else "FAIL")))
        print("    n = cross(u,s)        = (%+.4f,%+.4f,%+.4f)   points out the BACK: %s" %
              (n + (("OK" if ok_n else "FAIL"),)))
        print("    decorated face at MAX local Y = %+.4f  (recess floors below it): %s" %
              (face_y, "OK" if ok_n else "FAIL"))

        # buttons on the +u side, rest pads on -u (BuildBoard: "u -> +X keeps the rest zone
        # left / buttons right")
        along = lambda p: dot(p, u)
        ok_side = along(A["ButtonSeat1"]) > 0 and along(A["ShortRestToken"]) < 0
        print("    buttons on the +u side / rest pads on -u: %s  (seat %.3f, rest %.3f)"
              % ("OK" if ok_side else "FAIL", along(A["ButtonSeat1"]), along(A["ShortRestToken"])))

        # seats top -> bottom along the short axis, evenly spaced
        ys = [dot(A["ButtonSeat%d" % k], sv) for k in (1, 2, 3)]
        d1, d2 = ys[0] - ys[1], ys[1] - ys[2]
        ok_seat = d1 > 0 and d2 > 0 and abs(d1 - d2) < 1e-5
        print("    seats along +s: %.4f %.4f %.4f  pitch %.4f / %.4f  top-first & even: %s"
              % (ys[0], ys[1], ys[2], d1, d2, "OK" if ok_seat else "FAIL"))

        ok_alias = all(A[a] == A[b] for a, b in (("ConfirmButton", "ButtonSeat1"),
                                                 ("UndoButton", "ButtonSeat2"),
                                                 ("SkipButton", "ButtonSeat3")))
        print("    legacy aliases coincide with their seats: %s" % ("OK" if ok_alias else "FAIL"))

        # BuildBoard.AnchorPlaneCentre: four frame anchors + first and last seat
        terms = [A[k] for k in ("Slot1", "Slot2", "ShortRestToken", "LongRestToken",
                                "ButtonSeat1", "ButtonSeat3")]
        c = tuple(sum(t[i] for t in terms) / len(terms) for i in range(3))
        off = (abs(dot(c, u)) ** 2 + abs(dot(c, sv)) ** 2) ** 0.5
        ok_c = off < 1e-6
        print("    six-term placement centroid in the board plane: %.3e m from centre: %s"
              % (off, "OK" if ok_c else "FAIL"))

        if not all((ok_n, ok_u, ok_s, ok_perp, ok_side, ok_seat, ok_alias, ok_c)):
            bad += 1
    print("=== %d board(s) checked, %d FAILED" % (len(files), bad))


if __name__ == "__main__":
    main()
