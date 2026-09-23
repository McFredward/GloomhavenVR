#!/usr/bin/env python3
"""Render the assembled, textured town resident at neutral and head-turn extremes.

This offline Blender view checks authoring output; Unity/D3D/stereo validation
remains a separate gate. No source portraits or game data are changed.
"""
import argparse
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--wrists', action='store_true', help='Review wrist pronation and sleeve overlap instead of the head')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    args.output.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(args.input))
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.render.resolution_x = 850
    scene.render.resolution_y = 1000
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Neutral review')
    scene.world.color = (.13, .13, .13)
    rig = next(obj for obj in scene.objects if obj.type == 'ARMATURE')
    rig.animation_data_clear()
    for bone in rig.pose.bones:
        bone.rotation_mode = 'QUATERNION'
        bone.rotation_quaternion = Quaternion()
    eyes = [obj for obj in scene.objects if obj.type == 'EMPTY' and obj.name.startswith('Eye')]
    bind = {obj: obj.matrix_world.copy() for obj in eyes}
    for obj in scene.objects:
        if obj.type == 'MESH':
            obj.hide_render = obj.name.startswith(('LOD1', 'LOD2'))
    for location, energy, size in [((-1.5, -2, 2.8), 110, 2), ((1.5, -.8, 2), 35, 1.2)]:
        light = bpy.data.lights.new('Review', 'AREA')
        light.energy, light.size = energy, size
        obj = bpy.data.objects.new('Review', light)
        scene.collection.objects.link(obj)
        obj.location = location
        obj.rotation_euler = (Vector((0, 0, 1.5)) - obj.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('Review')
    data.type, data.ortho_scale = 'ORTHO', .57
    camera = bpy.data.objects.new('Review', data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    if args.wrists:
        data.ortho_scale = .25
        centre = rig.matrix_world @ rig.data.bones['Hand.R'].head_local
        views = [('dorsal', centre+Vector((.35, -.55, .25))), ('palmar', centre+Vector((-.35, -.5, -.2)))]
        poses = [('neutral', 0), ('palm-up', 90), ('palm-down', -90)]
    else:
        centre = Vector((0, 0, 1.50))
        views = [('front', (0, -2.2, 1.55)), ('oblique', (.9, -1.8, 1.55))]
        poses = [('neutral', 0), ('turn-left', -45), ('turn-right', 45)]
    for pose, yaw in poses:
        rig.pose.bones['Hand.R' if args.wrists else 'Head'].rotation_quaternion = Quaternion((0, 1, 0), math.radians(yaw))
        bpy.context.view_layer.update()
        delta = rig.pose.bones['Head'].matrix @ rig.data.bones['Head'].matrix_local.inverted()
        for obj in eyes:
            obj.matrix_world = rig.matrix_world @ delta @ rig.matrix_world.inverted() @ bind[obj]
        for view, location in views:
            camera.location = location
            camera.rotation_euler = (centre - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(args.output / (pose + '-' + view + '.png'))
            bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    main()
