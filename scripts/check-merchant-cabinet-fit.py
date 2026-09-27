#!/usr/bin/env python3
"""Check the exported merchant cabinet's removed side panel and physical seats.

Run with Blender 4.2 in background mode. The runtime anchors are checked against
these same imported surfaces by ValidateTownAssets after Unity integration.
"""
import argparse
import atexit
import os
import sys
from pathlib import Path

import bpy
from mathutils import Vector

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--furniture", type=Path, required=True)
parser.add_argument("--sculpt", type=Path, required=True)
parser.add_argument("--button", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
passed = False


@atexit.register
def fail_closed():
    # Blender itself exits zero after an unhandled Python assertion.
    if not passed:
        os._exit(1)


def imported(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path.resolve()))
    return [obj for obj in set(bpy.data.objects) - before if obj.type == "MESH"]


def ray(obj, origin, direction):
    inverse = obj.matrix_world.inverted()
    hit, point, _, _ = obj.ray_cast(
        inverse @ Vector(origin), (inverse.to_3x3() @ Vector(direction)).normalized())
    assert hit, f"No surface on {obj.name} at {origin}"
    return obj.matrix_world @ point


bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
furniture = imported(args.furniture)
sculpt = imported(args.sculpt)
buttons = imported(args.button)
assert len(sculpt) == 1
assert not any("ClothRunner_MerchantSide" in obj.name for obj in furniture)
assert not any(material.name.startswith("AltarCloth") for obj in furniture
               for material in obj.data.materials if material)
iron = next(obj for obj in furniture if obj.name == "merchant_ForgedIron")

# The sculpted cheek is between both faces of the flange, giving a real mesh
# contact rather than an apparent attachment in a single front projection.
height, depth = 1.600, .088
flange_outer = ray(iron, (3, -depth, height), (-1, 0, 0)).x
flange_inner = ray(iron, (1.3, -depth, height), (1, 0, 0)).x
cheek = ray(sculpt[0], (3, -depth, height), (-1, 0, 0)).x
assert flange_inner < cheek < flange_outer
assert flange_outer - cheek < .006, (flange_outer, cheek)

# Find the six actual recesses along the imported face. Their spacing is
# slightly irregular: a generic 120 mm pitch misses both outside sockets.
intervals = []
start = None
for i in range(781):
    station_x = -1.33 + i * .001
    try:
        z = -ray(sculpt[0], (-station_x, 1, .920), (0, -1, 0)).y
    except AssertionError:
        z = -1
    if z > .105 and start is None:
        start = station_x
    elif z <= .105 and start is not None:
        if station_x - start > .05:
            intervals.append((start, station_x - .001))
        start = None
centres = [(a + b) / 2 for a, b in intervals]
expected = [-1.214, -1.108, -.999, -.883, -.776, -.672]
assert len(centres) == 6, centres
assert all(abs(actual - target) < .007 for actual, target in zip(centres, expected)), centres

points = [obj.matrix_world @ vertex.co for obj in buttons for vertex in obj.data.vertices]
button_front_offset = min(-point.y for point in points)
button_back_offset = max(-point.y for point in points)
assert abs(button_front_offset + .022) < .002
assert abs(button_back_offset - .014) < .002
for x in centres:
    pit = -ray(sculpt[0], (-x, 1, .922), (0, -1, 0)).y
    rim = -ray(sculpt[0], (-x, 1, .850), (0, -1, 0)).y
    face = .118 + button_front_offset
    back = .118 + button_back_offset
    assert abs(face - rim) < .008, (x, face, rim)
    assert back > pit + .012, (x, back, pit)

print("MERCHANT_CABINET_FIT_OK",
      "side_panel=absent", "flange_cheek_gap=%.4f" % (flange_outer - cheek),
      "socket_centres=" + ",".join("%.3f" % value for value in centres))
passed = True
