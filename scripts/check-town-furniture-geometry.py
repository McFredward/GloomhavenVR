#!/usr/bin/env python3
"""Check actual authored curved cloth and merchant crank clearances in Blender.

Run after author-town-furniture.py with Blender 4.2 --background --python this
script -- --input-dir DIR. The script reads the generated .blend meshes, so it
does not merely repeat constants from the production authoring function.
"""
import argparse
import math
import sys
from pathlib import Path

import bpy


def station(vertex):
    # Undo author-town-furniture.xyz's FBX handedness conversion.
    return -vertex.x, vertex.z, -vertex.y


def front(x, kind):
    radius_x, radius_z, centre_z = ((.81, .38, 0) if kind == 'priestess'
                                    else (.86, .46, .03))
    return centre_z - radius_z * math.sqrt(max(0, 1 - (x / radius_x) ** 2))


def check_cloth(folder, kind, count):
    bpy.ops.wm.open_mainfile(filepath=str(folder / (kind + '.blend')))
    runners = [obj for obj in bpy.data.objects if obj.name.startswith('ClothRunner_')]
    assert len(runners) == count, (kind, 'runner count', len(runners))
    checked = 0
    for obj in runners:
        assert len(obj.data.vertices) >= 25 * 13, (kind, obj.name, 'missing woven grid')
        # The source grid occupies the first 325 positions. Solidify adds its
        # back layer after those positions; test the rendered top surface.
        for row in range(25):
            for col in range(13):
                x, y, z = station(obj.data.vertices[row * 13 + col].co)
                lip = front(x, kind)
                if row == 0:
                    assert .954 <= y <= .960, (kind, obj.name, row, y)
                    assert z >= lip + .135, (kind, obj.name, 'rear seam has no supported top', x, z, lip)
                if row <= 10:
                    assert y >= .954, (kind, obj.name, 'top is sunk into table', row, y)
                    assert z >= lip - .005, (kind, obj.name, 'top floats beyond table lip', row, x, z, lip)
                if 12 <= row <= 14:
                    assert z <= lip + .003, (kind, obj.name, 'hanging cloth buried in table', row, x, z, lip)
                checked += 1
    print('TOWN_CLOTH_GEOMETRY', kind, 'runners', count, 'tested_vertices', checked)


def check_crank(folder):
    bpy.ops.wm.open_mainfile(filepath=str(folder / 'merchant_crank.blend'))
    handle = next(obj for obj in bpy.data.objects if obj.name.startswith('Handle_DarkWood'))
    grip = [-.57 + station(vertex.co)[0] for vertex in handle.data.vertices]
    assert min(grip) < -.45, ('grip is not reachable', min(grip))
    # The ledger's left edge is X=-.356; retain >=15 mm physical separation.
    assert max(grip) < -.371, ('grip intersects ledger', max(grip))
    axle = next(obj for obj in bpy.data.objects if obj.name.startswith('merchant_crank_ForgedIron'))
    seat = [-.57 + station(vertex.co)[0] for vertex in axle.data.vertices]
    assert min(abs(x - (-.525)) for x in seat) < .01, ('spindle misses cabinet cheek', seat)
    print('TOWN_CRANK_GEOMETRY grip', round(min(grip), 4), round(max(grip), 4),
          'cabinet_seat', round(min(seat), 4), round(max(seat), 4))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input-dir', type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    folder = args.input_dir.resolve()
    check_cloth(folder, 'priestess', 2)
    check_cloth(folder, 'enchantress', 1)
    check_crank(folder)


if __name__ == '__main__':
    main()
