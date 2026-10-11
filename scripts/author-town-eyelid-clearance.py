#!/usr/bin/env python3
"""Fit closed eyelid shape keys over the exported physical cornea, offline only.

Neutral geometry, all other expressions, skin weights and imported actions are
preserved. The actual Unity open/closed optical masks remain the acceptance gate.
"""
import argparse
import json
from pathlib import Path
import shutil
import sys

import bpy
sys.path.insert(0, str(Path(__file__).resolve().parent))
from town_npc_eyelid_clearance import repair




def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--name', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(args.input / 'rig-source.blend'))
    report = repair()
    for filename in ('facial-rig.json', 'face_albedo.png', 'leg-weights.json'):
        if (args.input / filename).exists():
            shutil.copy2(args.input / filename, args.output / filename)
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output / 'rig-source.blend'))
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.context.scene.objects:
        if obj.type in ('MESH', 'ARMATURE', 'EMPTY'):
            obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.context.scene.frame_set(1)
    bpy.ops.export_scene.fbx(filepath=str(args.output / (args.name + '_rig.fbx')),
        use_selection=True, object_types={'MESH', 'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0,
        axis_forward='-Z', axis_up='Y', path_mode='STRIP')
    (args.output / 'eyelid-clearance.json').write_text(json.dumps(report, indent=2) + '\n')
    print('TOWN_EYELID_CLEARANCE_OK', json.dumps(report))


if __name__ == '__main__':
    main()
