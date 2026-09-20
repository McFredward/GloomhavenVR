#!/usr/bin/env python3
"""Author a small deterministic station rig on prepared NPC LODs; Blender 4.2 only.

The coordinate-based weights deliberately agree across duplicated UV seam vertices.
This is a bounded idle/gesture rig, not an automatic general-purpose human autorigger.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import sys
import runpy

import bpy
import numpy as np
from mathutils import Quaternion, Vector

PROFILES = {
    'merchant': {'shoulder': (0.235, 0.0, 1.395), 'elbow': (0.410, -0.015, 1.170),
                 'wrist': (0.567, -0.025, 0.957), 'finger_length': 0.105, 'body_width': 0.255},
    'priestess': {'shoulder': (0.170, 0.0, 1.385), 'elbow': (0.310, -0.015, 1.170),
                  'wrist': (0.437, -0.025, 0.949), 'finger_length': 0.108, 'body_width': 0.180},
    'enchantress': {'shoulder': (0.170, 0.0, 1.350), 'elbow': (0.316, -0.015, 1.170),
                    'wrist': (0.445, -0.025, 0.978), 'finger_length': 0.107, 'body_width': 0.182},
}


def args():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input-dir', type=Path, required=True, help='Output of prepare-npc-assets.py')
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--name', choices=PROFILES, required=True)
    parser.add_argument('--render', action='store_true', help='Render front/oblique idle and gesture evidence')
    parser.add_argument('--samples', type=int, default=24)
    return parser.parse_args(sys.argv[sys.argv.index('--') + 1:])


def smooth(a, b, value):
    x = np.clip((value - a) / (b - a), 0, 1)
    return x * x * (3 - 2 * x)


def create_rig(profile):
    data = bpy.data.armatures.new('TownSkeleton')
    rig = bpy.data.objects.new('Skeleton', data)
    bpy.context.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    definitions = {}

    def bone(name, head, tail, parent=None):
        b = data.edit_bones.new(name)
        b.head, b.tail = head, tail
        if parent:
            b.parent = data.edit_bones[parent]
        definitions[name] = {'head': list(head), 'tail': list(tail), 'parent': parent}
        return b

    bone('Root', (0, 0, 0), (0, 0, 0.12))
    bone('Hips', (0, 0, 0.80), (0, 0, 1.02), 'Root')
    bone('Spine', (0, 0, 1.02), (0, 0, 1.23), 'Hips')
    bone('Chest', (0, 0, 1.23), (0, 0, 1.43), 'Spine')
    bone('Neck', (0, 0, 1.43), (0, 0, 1.51), 'Chest')
    bone('Head', (0, 0, 1.51), (0, 0, 1.70), 'Neck')
    for side, sign in (('L', 1), ('R', -1)):
        reflect = lambda v: Vector((sign * v[0], v[1], v[2]))
        shoulder, elbow, wrist = [reflect(profile[k]) for k in ('shoulder', 'elbow', 'wrist')]
        direction = Vector((sign * 0.39, -0.04, -0.92)).normalized()
        palm = wrist + direction * 0.060
        bone('Clavicle.' + side, (sign * 0.04, 0, 1.40), shoulder, 'Chest')
        bone('UpperArm.' + side, shoulder, elbow, 'Clavicle.' + side)
        bone('Forearm.' + side, elbow, wrist, 'UpperArm.' + side)
        bone('Hand.' + side, wrist, palm, 'Forearm.' + side)
        # Four fingers fan across palm depth. Their placement is a bounded starting rig;
        # the generated meshes do not provide named finger landmarks or anatomy metadata.
        for digit, offset, length_scale in (('Index', -0.032, 0.91), ('Middle', -0.010, 1.0),
                                             ('Ring', 0.012, 0.92), ('Little', 0.032, 0.73)):
            start = palm + Vector((0, offset, 0))
            length = profile['finger_length'] * length_scale
            parent = 'Hand.' + side
            for part, fraction in enumerate((0.43, 0.33, 0.24)):
                end = start + direction * length * fraction
                name = '%s%d.%s' % (digit, part + 1, side)
                bone(name, start, end, parent)
                start, parent = end, name
        thumb_start = wrist + direction * 0.025 + Vector((-sign * 0.020, -0.036, 0))
        thumb_direction = Vector((-sign * 0.22, -0.08, -0.97)).normalized()
        parent = 'Hand.' + side
        for part in range(3):
            end = thumb_start + thumb_direction * (0.028 if part == 0 else 0.021)
            name = 'Thumb%d.%s' % (part + 1, side)
            bone(name, thumb_start, end, parent)
            thumb_start, parent = end, name
        # Feet remain planted in these authored station actions; no locomotion claim.
        bone('Thigh.' + side, (sign * 0.11, 0, 0.86), (sign * 0.12, 0, 0.47), 'Hips')
        bone('Shin.' + side, (sign * 0.12, 0, 0.47), (sign * 0.12, 0, 0.12), 'Thigh.' + side)
        bone('Foot.' + side, (sign * 0.12, 0, 0.12), (sign * 0.12, -0.14, 0.05), 'Shin.' + side)
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig, definitions


def weights(obj, rig, definitions, profile):
    coords = np.empty(len(obj.data.vertices) * 3, dtype=np.float64)
    obj.data.vertices.foreach_get('co', coords)
    coords = coords.reshape((-1, 3))
    # Quantize only the weight lookup, never the geometry. Coincident UV seam vertices
    # receive exactly the same deformation even if their index/material islands differ.
    coords = np.round(coords, decimals=5)
    names = list(definitions)
    index = {name: i for i, name in enumerate(names)}
    result = np.zeros((len(coords), len(names)), dtype=np.float64)
    z, x, y = coords[:, 2], coords[:, 0], coords[:, 1]
    transitions = [smooth(a, b, z) for a, b in ((0.87, 1.12), (1.12, 1.33), (1.40, 1.49), (1.48, 1.55))]
    remaining = np.ones(len(coords))
    for i, name in enumerate(('Hips', 'Spine', 'Chest', 'Neck')):
        result[:, index[name]] = remaining * (1 - transitions[i])
        remaining *= transitions[i]
    result[:, index['Head']] = remaining
    for side, sign in (('L', 1), ('R', -1)):
        shoulder, elbow, wrist = [np.array(definitions[k + '.' + side]['head']) for k in ('UpperArm', 'Forearm', 'Hand')]
        outward = x * sign
        # A soft armpit gate and a back-cloak exclusion prevent cape/hip accessories
        # following an arm merely because their world positions happen to be nearby.
        arm_gate = smooth(profile['body_width'] - 0.035, profile['body_width'] + 0.070, outward)
        arm_gate *= smooth(wrist[2] - 0.19, wrist[2] - 0.16, z) * (1 - smooth(1.40, 1.49, z))
        cloak_gate = 1 - smooth(wrist[1] + 0.075, wrist[1] + 0.12, y) * (1 - smooth(1.26, 1.40, z))
        arm_gate *= cloak_gate
        capsule_distances = []
        tip = wrist + (wrist - elbow) / np.linalg.norm(wrist - elbow) * 0.19
        for a, b in ((shoulder, elbow), (elbow, tip)):
            delta = b - a
            along = np.clip(np.sum((coords - a) * delta, axis=1) / np.dot(delta, delta), 0, 1)
            capsule_distances.append(np.linalg.norm(coords - a - along[:, None] * delta, axis=1))
        radius = np.minimum(*capsule_distances)
        arm_gate *= 1 - smooth(0.085, 0.15, radius) * (1 - smooth(abs(elbow[0]) + 0.02, abs(wrist[0]) - 0.03, outward))
        arm_length = np.linalg.norm(elbow - shoulder)
        t = np.sum((coords - shoulder) * ((elbow - shoulder) / arm_length), axis=1)
        elbow_mix = smooth(arm_length - 0.065, arm_length + 0.065, t)
        fore_direction = (wrist - elbow) / np.linalg.norm(wrist - elbow)
        wrist_t = np.sum((coords - wrist) * fore_direction, axis=1)
        hand_mix = smooth(-0.045, 0.024, wrist_t)
        side_weights = np.zeros_like(result)
        side_weights[:, index['UpperArm.' + side]] = 1 - elbow_mix
        side_weights[:, index['Forearm.' + side]] = elbow_mix * (1 - hand_mix)
        side_weights[:, index['Hand.' + side]] = elbow_mix * hand_mix
        # Blend only distal hand vertices into nearest digit capsules. No finger motion
        # larger than three degrees is authored until individual landmarks are reviewed.
        distal = smooth(0.055, 0.085, wrist_t) * elbow_mix * hand_mix
        finger_names = [n for n in names if n.endswith('.' + side) and n[:-2].startswith(('Thumb', 'Index', 'Middle', 'Ring', 'Little'))]
        distances = []
        for name in finger_names:
            a, b = np.array(definitions[name]['head']), np.array(definitions[name]['tail'])
            direction = b - a
            parameter = np.clip(np.sum((coords - a) * direction, axis=1) / np.dot(direction, direction), 0, 1)
            distances.append(np.sum((coords - (a + parameter[:, None] * direction)) ** 2, axis=1))
        distances = np.array(distances).T
        nearest = np.argsort(distances, axis=1)[:, :2]
        values = 1 / np.maximum(np.take_along_axis(distances, nearest, axis=1), 1e-6) ** 2
        values /= values.sum(axis=1)[:, None]
        side_weights[:, index['Hand.' + side]] -= distal
        rows = np.arange(len(coords))
        for k in range(2):
            cols = np.array([index[finger_names[j]] for j in nearest[:, k]])
            side_weights[rows, cols] += distal * values[:, k]
        result *= (1 - arm_gate[:, None])
        result += side_weights * arm_gate[:, None]
    # Four influences match Unity's quality contract and prevent platform truncation.
    dominant = np.argsort(result, axis=1)[:, -4:]
    masked = np.zeros_like(result)
    np.put_along_axis(masked, dominant, np.take_along_axis(result, dominant, axis=1), axis=1)
    masked /= masked.sum(axis=1)[:, None]
    if not np.all(np.isfinite(masked)) or np.max(abs(masked.sum(axis=1) - 1)) > 1e-6:
        raise RuntimeError('Invalid skin weights')
    for i, name in enumerate(names):
        group = obj.vertex_groups.new(name=name)
        for vertex in np.flatnonzero(masked[:, i] > 1e-6):
            group.add([int(vertex)], float(masked[vertex, i]), 'REPLACE')
    modifier = obj.modifiers.new('Town skin', 'ARMATURE')
    modifier.object = rig
    modifier.use_deform_preserve_volume = False  # Match Unity linear blend skinning.
    obj.parent = rig
    return {'vertices': len(coords), 'maxInfluences': int((masked > 1e-6).sum(axis=1).max()),
            'maxWeightSumError': float(abs(masked.sum(axis=1) - 1).max()),
            'weightedBones': int((masked.sum(axis=0) > 0).sum())}


def animate(rig):
    rig.animation_data_create()
    durations = {'Idle': 4.0, 'Greeting': 2.4, 'Gesture': 3.0, 'ReturnToIdle': 0.8}
    for name, duration in durations.items():
        action = bpy.data.actions.new(name)
        rig.animation_data.action = action
        end = round(duration * 30) + 1
        action.use_frame_range = True
        action.frame_start, action.frame_end = 1, end
        for frame in range(1, end + 1, 3):
            t = (frame - 1) / (end - 1)
            pulse = math.sin(math.pi * t) ** 2
            breath = math.sin(2 * math.pi * t) if name == 'Idle' else 0
            rotations = {'Chest': ((1, 0, 0), 0.6 * breath), 'Neck': ((0, 0, 1), 0.8 * breath),
                         'Head': ((1, 0, 0), (-8 if name == 'Greeting' else -2 if name == 'Gesture' else 0) * pulse)}
            for side, sign in (('L', 1), ('R', -1)):
                extra = (-8 * pulse if name == 'Gesture' and side == 'R' else 0)
                rotations['UpperArm.' + side] = ((0, 1, 0), sign * (17 + extra))
                rotations['Forearm.' + side] = ((1, 0, 0), -7 - (24 * pulse if name in ('Greeting', 'Gesture') and side == 'R' else 0))
                rotations['Hand.' + side] = ((0, 0, 1), sign * (3 * pulse if name == 'Gesture' else 0))
                for digit in ('Thumb', 'Index', 'Middle', 'Ring', 'Little'):
                    for part in (1, 2, 3):
                        rotations['%s%d.%s' % (digit, part, side)] = ((0, 1, 0), sign * (2 * pulse if name == 'Gesture' else 0))
            for bone in rig.pose.bones:
                bone.rotation_mode = 'QUATERNION'
                axis, degrees = rotations.get(bone.name, ((0, 0, 1), 0))
                local_axis = bone.bone.matrix_local.to_quaternion().inverted() @ Vector(axis)
                bone.rotation_quaternion = Quaternion(local_axis, math.radians(degrees))
                bone.keyframe_insert('rotation_quaternion', frame=frame, group=bone.name)
        for curve in action.fcurves:
            for key in curve.keyframe_points:
                key.interpolation = 'BEZIER'
        action.use_fake_user = True
    rig.animation_data.action = bpy.data.actions['Idle']
    bpy.context.scene.frame_set(1)
    return durations


def render_evidence(args, rig, meshes, output):
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = args.samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new('Studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (0.3, 0.32, 0.35, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = 0.6
    for position, energy, size in (((-3, -4, 4), 700, 4), ((3, -2, 3), 500, 3), ((0, 3, 4), 650, 3)):
        data = bpy.data.lights.new('StudioLight', 'AREA')
        data.energy, data.size = energy, size
        light = bpy.data.objects.new(data.name, data)
        scene.collection.objects.link(light)
        light.location = position
        light.rotation_euler = (Vector((0, 0, 1)) - light.location).to_track_quat('-Z', 'Y').to_euler()
    data = bpy.data.cameras.new('EvidenceCamera')
    data.type, data.ortho_scale = 'ORTHO', 2.05
    camera = bpy.data.objects.new(data.name, data)
    scene.collection.objects.link(camera)
    scene.camera = camera
    for obj in meshes:
        obj.hide_render = not obj.name.startswith('LOD0')
    for action, frame in (('Idle', 1), ('Greeting', 37), ('Gesture', 46)):
        rig.animation_data.action = bpy.data.actions[action]
        scene.frame_set(frame)
        for view, angle in (('front', 0), ('oblique', 40)):
            a = math.radians(angle)
            camera.location = (5 * math.sin(a), -5 * math.cos(a), 1.05)
            camera.rotation_euler = (Vector((0, 0, 0.875)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(output / ('evidence_%s_%s.png' % (action, view)))
            bpy.ops.render.render(write_still=True)


def main():
    options = args()
    source, output = options.input_dir.resolve(), options.output_dir.resolve()
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output directory must be empty')
    output.mkdir(parents=True, exist_ok=True)
    manifest = json.loads((source / 'manifest.json').read_text())
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30
    source_item = next(d for d in manifest['derivatives'] if d['name'].endswith('_source'))
    bpy.ops.import_scene.gltf(filepath=str(source / source_item['glb']))
    originals = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    for obj in originals:
        obj.data.transform(obj.matrix_world)
        obj.parent = None
        obj.matrix_world.identity()
    for obj in list(bpy.context.scene.objects):
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
    weld_lod_source = runpy.run_path(str(Path(__file__).with_name('prepare-npc-assets.py')))['weld_lod_source']
    welding = [weld_lod_source(obj) for obj in originals]
    source_triangles = sum(len(o.data.polygons) for o in originals)
    meshes = []
    for level, target in enumerate((80000, 30000, 10000)):
        for i, original in enumerate(originals):
            obj = original.copy()
            obj.data = original.data.copy()
            obj.name = 'LOD%d_%d' % (level, i)
            bpy.context.collection.objects.link(obj)
            bpy.ops.object.select_all(action='DESELECT')
            obj.select_set(True)
            bpy.context.view_layer.objects.active = obj
            if source_triangles > target:
                modifier = obj.modifiers.new('Welded-source LOD candidate', 'DECIMATE')
                modifier.ratio = target / source_triangles
                modifier.use_collapse_triangulate = True
                bpy.ops.object.modifier_apply(modifier=modifier.name)
            meshes.append(obj)
    for obj in originals:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    profile = dict(PROFILES[options.name])
    reference = np.array([v.co[:] for v in meshes[0].data.vertices])
    for landmark in ('shoulder', 'elbow', 'wrist'):
        point = list(profile[landmark])
        nearby = reference[(abs(abs(reference[:, 0]) - point[0]) < 0.025) & (abs(reference[:, 2] - point[2]) < 0.020)]
        if len(nearby) > 20:
            point[1] = float(np.mean(np.quantile(nearby[:, 1], [0.1, 0.9])))
        profile[landmark] = point
    rig, definitions = create_rig(profile)
    records = {obj.name: weights(obj, rig, definitions, profile) for obj in meshes}
    durations = animate(rig)
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'rig-source.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(filepath=str(output / (options.name + '_rig.fbx')), use_selection=True,
        object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False, bake_anim=True,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0, axis_forward='-Z', axis_up='Y', path_mode='STRIP')
    report = {'name': options.name, 'blender': bpy.app.version_string,
              'authoringScriptSha256': hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
              'lodHelperSha256': hashlib.sha256(Path(__file__).with_name('prepare-npc-assets.py').read_bytes()).hexdigest(),
              'preparedInput': str(source), 'preparedManifestSha256': hashlib.sha256((source / 'manifest.json').read_bytes()).hexdigest(),
              'heightMetres': 1.75, 'derivedSourceWelding': welding, 'bones': definitions, 'meshWeights': records, 'animations': durations,
              'limitations': ['Authored bounded station animation; no general locomotion or grasping claim.',
                              'Finger landmarks are proportional estimates; large finger curls are not authored.',
                              'No facial blend shapes, mouth animation or eye tracking.'],
              'fbx': options.name + '_rig.fbx'}
    (output / 'rig-manifest.json').write_text(json.dumps(report, indent=2) + '\n')
    if options.render:
        render_evidence(options, rig, meshes, output)


if __name__ == '__main__':
    main()
