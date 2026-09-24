#!/usr/bin/env python3
"""Bind overlapping anatomical skin and original cuffs to one wrist transition.

Run in Blender 4.2 against the approved source blend. Geometry, shape keys,
materials, UVs, animation and every vertex outside the wrist interval are retained.
"""
import argparse
import json
import sys
from pathlib import Path

import bpy


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--name', required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(args.input))
    rig = next(obj for obj in bpy.data.objects if obj.type == 'ARMATURE')
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for side in ('L', 'R'):
        fore = rig.data.edit_bones['Forearm.' + side]
        for n in range(1, 4):
            bone = rig.data.edit_bones.get('ForearmTwist' + str(n) + '.' + side)
            if bone is None:
                bone = rig.data.edit_bones.new('ForearmTwist' + str(n) + '.' + side)
            bone.head = fore.head.lerp(fore.tail, n / 3)
            bone.tail = bone.head + (fore.tail - fore.head).normalized() * .03
            bone.roll = fore.roll
            bone.parent = fore
            bone.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    report = []
    for obj in bpy.data.objects:
        if obj.type != 'MESH' or not any(m.type == 'ARMATURE' and m.object == rig for m in obj.modifiers):
            continue
        protected = {v.index: tuple((g.group, g.weight) for g in v.groups) for v in obj.data.vertices}
        changed = []
        for side in ('L', 'R'):
            bone = rig.data.bones['Hand.' + side]
            direction = (bone.tail_local - bone.head_local).normalized()
            hand, fore = (obj.vertex_groups.get(name + '.' + side) for name in ('Hand', 'Forearm'))
            if hand is None or fore is None:
                continue
            for vertex in obj.data.vertices:
                delta = vertex.co - bone.head_local
                along = delta.dot(direction)
                if not (-.085 < along < .026) or (delta - direction * along).length > .073:
                    continue
                weights = {g.group: g.weight for g in vertex.groups}
                # Existing influence identifies real arm skin/cloth; a nearby belt,
                # chest, face, cape or lower-body vertex cannot enter this repair.
                if weights.get(hand.index, 0) + weights.get(fore.index, 0) < .999:
                    continue
                t = min(1., max(0., (along + .008) / .033))
                t = t * t * (3. - 2. * t)
                for group in list(vertex.groups):
                    obj.vertex_groups[group.group].remove([vertex.index])
                if t > 0:
                    hand.add([vertex.index], t, 'REPLACE')
                if t < 1:
                    fore.add([vertex.index], 1 - t, 'REPLACE')
                changed.append(vertex.index)
                protected.pop(vertex.index, None)
            # Twist supports rotate around the same centreline. Interpolating
            # neighbours limits every skin segment to one third of the total roll,
            # rather than collapsing a 180-degree elbow/forearm blend.
            fore_bone = rig.data.bones['Forearm.' + side]
            axis = fore_bone.tail_local - fore_bone.head_local
            twists = [obj.vertex_groups.get('ForearmTwist' + str(n) + '.' + side)
                      or obj.vertex_groups.new(name='ForearmTwist' + str(n) + '.' + side)
                      for n in range(1, 4)]
            supports = [fore] + twists
            arm_names = {'UpperArm.' + side, 'Forearm.' + side, 'Hand.' + side}
            for vertex in obj.data.vertices:
                weights = {obj.vertex_groups[g.group].name: g.weight for g in vertex.groups}
                weight = weights.get(fore.name, 0)
                if weight < 1e-7 or any(name not in arm_names and value > 1e-7 for name, value in weights.items()):
                    continue
                at = min(3., max(0., (vertex.co - fore_bone.head_local).dot(axis) / axis.length_squared * 3))
                low = min(2, int(at))
                blend = at - low
                fore.remove([vertex.index])
                if blend < 1:
                    group = supports[low]
                    group.add([vertex.index], weight * (1 - blend), 'REPLACE')
                if blend > 0:
                    group = supports[low + 1]
                    group.add([vertex.index], weight * blend, 'REPLACE')
                assert len(vertex.groups) <= 4
                changed.append(vertex.index)
                protected.pop(vertex.index, None)
        for index, weights in protected.items():
            assert tuple((g.group, g.weight) for g in obj.data.vertices[index].groups) == weights
        report.append({'mesh': obj.name, 'changed_vertices': len(set(changed)),
                       'protected_vertices': len(protected), 'other_weights_unchanged': True})
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output / 'rig-source.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.data.objects:
        if obj.type in ('MESH', 'ARMATURE', 'EMPTY'):
            obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.context.scene.frame_set(1)
    bpy.ops.export_scene.fbx(filepath=str(args.output / (args.name + '_rig.fbx')),
        use_selection=True, object_types={'MESH', 'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
        bake_anim=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0,
        axis_forward='-Z', axis_up='Y', path_mode='STRIP')
    (args.output / 'wrist-weights.json').write_text(json.dumps(report, indent=2) + '\n')
    print('TOWN_WRIST_WEIGHTS_OK', sum(row['changed_vertices'] for row in report))


if __name__ == '__main__':
    main()
