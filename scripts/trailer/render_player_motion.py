#!/usr/bin/env python3
"""Block purposeful player motion using original masks, rigs and native idle.

The supplied packed scene must be the revised continuous full-scale film world.
All output is private cinematic reconstruction, never runtime gameplay capture.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import shutil
import subprocess
import sys
import time
from pathlib import Path

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector


ROOT = Path('/home/claw/gloomhaven_vr')
MEDIA = ROOT / '.planning/debug/trailer'
OUTPUT_ROOT = MEDIA / 'revision-02/motion'
_spec = importlib.util.spec_from_file_location('native_camera', Path(__file__).with_name('render_gameplay.py'))
native = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(native)
base = native.base
FINGERS = ('Thumb', 'Index', 'Middle', 'Ring', 'Pinky')
RELAXED = {'Thumb': .30, 'Index': .15, 'Middle': .24, 'Ring': .30, 'Pinky': .37}
RESTING = {'Thumb': .55, 'Index': .14, 'Middle': .18, 'Ring': .23, 'Pinky': .28}
GRIP = {'Thumb': 1.0, 'Index': .6, 'Middle': .9, 'Ring': 1.0, 'Pinky': 1.0}


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def smooth(t):
    t = min(1.0, max(0.0, t))
    return t * t * t * (10 + t * (-15 + 6 * t))


def phase(seconds, start, end):
    return smooth((seconds - start) / (end - start))


def hermite(a, b, va, vb, t):
    t = min(1.0, max(0.0, t))
    return ((2 * t ** 3 - 3 * t ** 2 + 1) * Vector(a)
            + (t ** 3 - 2 * t ** 2 + t) * Vector(va)
            + (-2 * t ** 3 + 3 * t ** 2) * Vector(b)
            + (t ** 3 - t ** 2) * Vector(vb))


def set_curls(rig, curls):
    for finger in FINGERS:
        angles = (25, 45, 60) if finger == 'Thumb' else (75, 95, 65)
        for joint, angle in zip(('Root', 'Mid', 'Tip'), angles):
            bone = rig.pose.bones[f'Anchor_{finger}_{joint}']
            bone.rotation_mode = 'QUATERNION'
            # These are the same source-local X curl axes used by the mod rigs.
            bone.rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * curls[finger]))


def rig_hand(assets, style, side, name):
    path = assets / f'Hands/{style}_{side}_rig.fbx'
    base.USED.add(path)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path))
    objects = set(bpy.data.objects) - before
    rig = next(o for o in objects if o.type == 'ARMATURE')
    for obj in objects:
        if obj.animation_data:
            obj.animation_data_clear()
        obj.name = name + ' ' + obj.name
        if obj.type == 'MESH':
            obj.data.materials.clear()
            obj.data.materials.append(base.hand_mat(type('Config', (), {'assets': assets})(), style))
    # Source skin/parent/bind matrices remain intact. Global motion is applied
    # only to the original root armature, and source joints supply finger motion.
    roots = [o for o in objects if o.parent not in objects]
    if roots != [rig]:
        raise ValueError('Unexpected original hand hierarchy; refusing a partial transform')
    rig.rotation_mode = 'QUATERNION'
    rest_world = rig.matrix_world.copy()
    set_curls(rig, RELAXED)
    bpy.context.view_layer.update()
    return {'rig': rig, 'objects': list(objects), 'source': path,
            'rest_world': rest_world, 'style': style, 'side': side}


def pinch_local(hand):
    rig = hand['rig']
    # Bone endpoints are in armature space, avoiding a previous animated world
    # transform contaminating the next frame's source contact calculation.
    thumb = rig.pose.bones['Anchor_Thumb_Tip'].tail.copy()
    index = rig.pose.bones['Anchor_Index_Tip'].tail.copy()
    return thumb, index, (thumb + index) / 2


def pose_root(hand, transform, frame):
    rig = hand['rig']
    rig.matrix_world = transform @ hand['rest_world']
    rig.keyframe_insert('location', frame=frame)
    rig.keyframe_insert('rotation_quaternion', frame=frame)
    rig.keyframe_insert('scale', frame=frame)
    for finger in FINGERS:
        for joint in ('Root', 'Mid', 'Tip'):
            rig.pose.bones[f'Anchor_{finger}_{joint}'].keyframe_insert('rotation_quaternion', frame=frame)


def mask_pose(objects, location, target, roll, frame):
    rotation = (Vector(target) - Vector(location)).to_track_quat('-Y', 'Z')
    rotation = rotation @ Quaternion((0, -1, 0), roll)
    for obj in objects:
        obj.location = location
        obj.rotation_mode = 'QUATERNION'
        obj.rotation_quaternion = rotation
        obj.keyframe_insert('location', frame=frame)
        obj.keyframe_insert('rotation_quaternion', frame=frame)


def reset_native_idle(hero, archive, last_frame):
    # The world-builder can stage a native attack pose for a still. Reset only
    # its cinematic copy to the exact original idle basis before applying motion.
    with np.load(archive, allow_pickle=False) as data:
        if len(hero.data.vertices) != data['positions'].shape[1]:
            raise ValueError('Film-world actor topology differs from original idle')
        faces = np.array([tuple(p.vertices) for p in hero.data.polygons], dtype=np.int32)
        if not np.array_equal(faces, data['triangles']):
            raise ValueError('Film-world actor face order differs from native source')
        hero.data.vertices.foreach_set('co', data['positions'][0].reshape(-1))
        hero.data.update()
        # Follow a stable cohort of original upper-head vertices as the source
        # idle breathes. This is a geometric contact anchor, not an invented rig.
        meta = json.loads(archive.with_suffix('.json').read_text())
        part = next(p for p in meta['parts'] if p['name'] == 'HE_Brute_Mesh')
        first = data['positions'][0]
        indices = np.arange(part['vertex_start'], part['vertex_start'] + part['vertex_count'])
        xyz = first[indices]
        selected = indices[(xyz[:, 2] > 1.66) & (xyz[:, 2] < 1.89)
                           & (np.abs(xyz[:, 0]) < .21) & (xyz[:, 1] < .03)]
        if len(selected) < 16:
            raise ValueError('Original head contact cohort is insufficient')
        centres = data['positions'][:, selected].mean(axis=1)
        centres[:, 2] += .10
    record = native.apply_native_animation([hero], archive, end_frame=last_frame, loop=True)
    return centres, record, len(selected)


def create_player_motion(scene, assets, hero, idle_archive, duration=4.0,
                         grimhorn_position=(-1.1, 2.2, 8.2)):
    """Add one overlapping reach/grip/lift and an intentional second-player entry."""
    count = round(duration * 24)
    scene.frame_start, scene.frame_end, scene.render.fps = 1, count, 24
    for obj in bpy.data.objects:
        if obj.animation_data:
            obj.animation_data_clear()
    hero_rest = hero.matrix_world.copy()
    head_centres, idle, cohort = reset_native_idle(hero, idle_archive, count)
    for obj in list(bpy.data.objects):
        if obj.name.startswith('Original god glove purposeful downward reach'):
            bpy.data.objects.remove(obj, do_unlink=True)
    grimhorn = [o for o in bpy.data.objects if o.name.startswith('Original Grimhorn focused on Brute')]
    if not grimhorn:
        raise ValueError('Revised scene must contain the original Grimhorn mask')
    cfg = type('Config', (), {'assets': assets})()
    if not bpy.data.objects.get('Late visible wooden game table'):
        wood = base.material('Original wood tabletop revealed late',
                             assets / 'TownServices/Textures/dark_wooden_planks_alb.jpg',
                             roughness=.74, box=True)
        tree = wood.node_tree
        coordinates = tree.nodes.new('ShaderNodeTexCoord')
        repeat = tree.nodes.new('ShaderNodeVectorMath')
        repeat.operation = 'SCALE'
        repeat.inputs[3].default_value = .18
        tree.links.new(coordinates.outputs['Object'], repeat.inputs[0])
        for node in tree.nodes:
            if node.type == 'TEX_IMAGE':
                tree.links.new(repeat.outputs['Vector'], node.inputs['Vector'])
        base.cube('Late visible wooden game table', (0, 2, -.70), (34, 46, .70), wood, .16)
    runeveil = base.import_asset(cfg, 'Head/Mask_1.fbx', base.mask_mat(cfg, 1),
                                'Original Runeveil intentional entry', (11.5, 2.5, 8.1), scale=26)
    hands = {name: rig_hand(assets, style, side, name)
             for name, style, side in [('Grimhorn reach', 'VRHand', 'R'),
                                       ('Grimhorn resting', 'VRHand', 'L'),
                                       ('Runeveil resting L', 'VRHandArcane', 'L'),
                                       ('Runeveil resting R', 'VRHandArcane', 'R')]}
    grip = hands['Grimhorn reach']
    set_curls(grip['rig'], GRIP)
    bpy.context.view_layer.update()
    thumb, index, centre = pinch_local(grip)
    align = (index - thumb).rotation_difference(Vector((1, 0, 0)))
    final_rotation = Quaternion((0, 0, 1), math.radians(160)) @ Quaternion((1, 0, 0), math.radians(140)) @ align
    initial_rotation = final_rotation @ Quaternion((0, 1, 0), math.radians(-21))
    scale = 17.0
    trace = []
    for frame in range(1, count + 1):
        seconds = (frame - 1) / 24
        # The 4 s study compresses one coherent action. Timing stays proportional
        # if the same approved blocking is rendered as an 8–12 s review.
        t = seconds * 4 / duration
        lift = .94 * phase(t, 2.0, 2.825) + .06 * phase(t, 2.65, 3.925)
        offset = Vector((.07 * lift, -.04 * lift, 1.35 * lift))
        hero.matrix_world = Matrix.Translation(offset) @ hero_rest
        hero.keyframe_insert('location', frame=frame)
        hero.keyframe_insert('rotation_euler', frame=frame)
        hero.keyframe_insert('scale', frame=frame)
        source_index = (frame - 1) % (len(head_centres) - 1)
        target = hero_rest @ Vector(head_centres[source_index]) + offset
        close = phase(t, 1.25, 2.0)
        curls = {f: RELAXED[f] + (GRIP[f] - RELAXED[f]) * close for f in FINGERS}
        set_curls(grip['rig'], curls)
        bpy.context.view_layer.update()
        thumb, index, centre = pinch_local(grip)
        approach = hermite((2.0, .68, 3.2), (0, 0, 0), (-.7, -.2, -1.5), (0, 0, 0), t / 2.0)
        # One continuous curved approach starts moving immediately. The wrist
        # turns during the approach; the original finger joints close gradually.
        rotation = initial_rotation.slerp(final_rotation, phase(t, .05, 1.95))
        transform = (Matrix.Translation(target + approach) @ rotation.to_matrix().to_4x4()
                     @ Matrix.Scale(scale, 4) @ Matrix.Translation(-centre))
        pose_root(grip, transform, frame)
        grim_lean = phase(t, .10, 2.85)
        grim = Vector(grimhorn_position) + Vector((.10, -.24, -.18)) * grim_lean
        mask_pose(grimhorn, grim, target, math.radians(-1.4 * grim_lean), frame)
        entry = phase(t, 2.75, 3.4)
        rune = Vector((11.5, 2.5, 8.1)).lerp(Vector((4.3, 2.3, 7.85)), entry)
        acknowledge = phase(t, 3.40, 3.70)
        return_attention = phase(t, 3.78, 4.0)
        rune_target = target.lerp(grim, acknowledge * (1 - return_attention))
        nod = math.sin(math.pi * min(1.0, max(0.0, (t - 3.55) / .32))) * math.radians(1.8)
        mask_pose(runeveil, rune, rune_target + Vector((0, 0, -nod * 12)), 0, frame)
        relaxed_positions = {'Grimhorn resting': Vector((-7.0, -3.5, .9)),
                             'Runeveil resting L': Vector((7.0, -2.5, 1.0)),
                             'Runeveil resting R': Vector((8.2, .5, 1.1))}
        for name, hand in hands.items():
            if name == 'Grimhorn reach':
                continue
            set_curls(hand['rig'], RESTING)
            position = relaxed_positions[name]
            if name.startswith('Runeveil'):
                position += rune - Vector((4.3, 2.3, 7.85))
                position += Vector((-.07, .03, -.05)) * phase(t, 3.05, 3.95)
            else:
                position += Vector((.04, -.06, -.10)) * grim_lean
            yaw = -10 if hand['side'] == 'L' else 18
            orientation = (Quaternion((0, 0, 1), math.radians(yaw))
                           @ Quaternion((0, 1, 0), math.radians(-3 if hand['side'] == 'L' else 5))
                           @ Quaternion((1, 0, 0), math.radians(88 if hand['side'] == 'L' else 93)))
            pose_root(hand, Matrix.Translation(position) @ orientation.to_matrix().to_4x4()
                      @ Matrix.Scale(scale, 4), frame)
        bpy.context.view_layer.update()
        thumb_world = grip['rig'].matrix_world @ grip['rig'].pose.bones['Anchor_Thumb_Tip'].tail
        index_world = grip['rig'].matrix_world @ grip['rig'].pose.bones['Anchor_Index_Tip'].tail
        trace.append({'frame': frame, 'seconds': round(seconds, 5), 'hero_offset': list(offset),
                      'head_contact_target': list(target), 'grip_midpoint_world': list((thumb_world + index_world) / 2),
                      'thumb_tip_world': list(thumb_world), 'index_tip_world': list(index_world),
                      'thumb_index_gap_metres': (thumb_world - index_world).length,
                      'finger_curls': curls, 'grimhorn_position': list(grim),
                      'grimhorn_gaze_target': list(target), 'runeveil_position': list(rune),
                      'runeveil_gaze_target': list(rune_target)})
    for obj in bpy.data.objects:
        if obj.animation_data and obj.animation_data.action:
            for curve in obj.animation_data.action.fcurves:
                for point in curve.keyframe_points:
                    point.interpolation = 'LINEAR'
    scene.frame_set(1)
    return {'native_idle': idle, 'head_contact_cohort_vertices': cohort,
            'four_original_hands': list(hands), 'traces': trace,
            'timing': 'living actor first; Grimhorn face before contact; overlapping curved reach, wrist rotation and source finger closure; fluid lift; late meaningful Runeveil entry',
            'scope': 'one corrected review, then production pause; full table/gameplay transition awaits approval'}


def animate_camera(scene, duration):
    cam = scene.camera
    cam.rotation_mode = 'QUATERNION'
    for frame in range(1, scene.frame_end + 1):
        seconds = (frame - 1) / 24
        t = seconds * 4 / duration
        widen = phase(t, .50, 1.35)
        table_reveal = phase(t, 2.15, 4.0)
        position = Vector((.95, -4.4, 1.43)).lerp(Vector((1.4, -10.5, 1.4)), widen)
        cam.location = position.lerp(Vector((-2.3935055, -18.3695061, 8.0991693)), table_reveal)
        target = Vector((0, 0, 1.23)).lerp(Vector((-.15, 2.0, 4.20)), widen)
        target = target.lerp(Vector((-.08946534, .78930675, 4.03857423)), table_reveal)
        cam.rotation_quaternion = (target - cam.location).to_track_quat('-Z', 'Y')
        cam.data.lens = (48 + (25 - 48) * widen) * (1 - table_reveal) + 27.017822 * table_reveal
        cam.data.dof.aperture_fstop = 8
        cam.data.dof.focus_distance = (cam.location - target).length
        cam.keyframe_insert('location', frame=frame)
        cam.keyframe_insert('rotation_quaternion', frame=frame)
        cam.data.keyframe_insert('lens', frame=frame)
        cam.data.keyframe_insert('dof.focus_distance', frame=frame)
    scene.frame_set(1)


def arguments():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--world-scene', type=Path, required=True)
    p.add_argument('--world-manifest', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--assets', type=Path, default=ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle')
    p.add_argument('--idle', type=Path, default=MEDIA / 'motion/brute/HE_Brute_Idle_v001.npz')
    p.add_argument('--duration', type=float, default=4)
    p.add_argument('--width', type=int, default=960)
    p.add_argument('--samples', type=int, default=16)
    p.add_argument('--frames', default='1,48,96')
    p.add_argument('--animation', action='store_true')
    p.add_argument('--save-blend', action='store_true')
    c = p.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for key in ('world_scene', 'world_manifest', 'output', 'assets', 'idle'):
        setattr(c, key, getattr(c, key).resolve())
    if not c.output.is_relative_to(OUTPUT_ROOT.resolve()):
        p.error('All output must belong to main .planning/debug/trailer/revision-02/motion/')
    if c.output.exists():
        p.error('Use a new output version; existing evidence is never overwritten')
    if not 2 <= c.duration <= 12 or c.width < 64 or c.samples < 1:
        p.error('Duration must be 2–12 seconds and render settings must be positive')
    return c


def main():
    c = arguments()
    code = Path(__file__).read_bytes()
    world = json.loads(c.world_manifest.read_text())
    for record in world['sources']:
        if sha(record['path']) != record['sha256']:
            raise ValueError('Revised film-world source hash changed')
    bpy.ops.wm.open_mainfile(filepath=str(c.world_scene))
    c.output.mkdir(parents=True)
    scene = bpy.context.scene
    hero = bpy.data.objects.get('Original Brute living native pose')
    if hero is None:
        raise ValueError('Revised scene must contain the exact original Brute actor')
    grimhorn = next(o for o in scene.objects if o.name.startswith('Original Grimhorn focused on Brute'))
    report = create_player_motion(scene, c.assets, hero, c.idle, c.duration, tuple(grimhorn.location))
    animate_camera(scene, c.duration)
    scene.render.resolution_x, scene.render.resolution_y = c.width, round(c.width * 9 / 16)
    scene.render.resolution_percentage = 100
    scene.render.fps = 24
    scene.eevee.taa_render_samples = c.samples
    scene.eevee.volumetric_samples = 16
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    sources = {p['path']: p for p in world['sources']}
    for path in base.USED:
        sources[str(path)] = {'path': str(path), 'sha256': sha(path)}
    tool_capture = c.output / 'render-player-motion-executed.py'
    tool_capture.write_bytes(code)
    dependencies = []
    for name in ('render_gameplay.py', 'render_opener.py', 'render_assets.py'):
        path = Path(__file__).with_name(name)
        captured = c.output / name
        captured.write_bytes(path.read_bytes())
        dependencies.append({'path': str(captured), 'sha256': sha(captured)})
    data = {'classification': 'Source-model cinematic motion blocking; not runtime gameplay',
            'world_scene': {'path': str(c.world_scene), 'sha256': sha(c.world_scene)},
            'world_manifest': {'path': str(c.world_manifest), 'sha256': sha(c.world_manifest)},
            'tool': {'path': str(tool_capture), 'sha256': hashlib.sha256(code).hexdigest()},
            'executed_dependencies': dependencies,
            'sources': list(sources.values()), 'control': report,
            'fps': 24, 'frames': scene.frame_end, 'samples': c.samples,
            'resolution': [scene.render.resolution_x, scene.render.resolution_y], 'outputs': []}
    if c.save_blend:
        bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)
        bpy.ops.file.pack_all()
        path = c.output / 'player-motion.blend'
        bpy.ops.wm.save_as_mainfile(filepath=str(path), compress=True)
        data['outputs'].append({'path': str(path), 'sha256': sha(path)})
    frames = list(range(1, scene.frame_end + 1)) if c.animation else sorted({int(f) for f in c.frames.split(',')})
    render_dir = c.output / ('encode-frames' if c.animation else 'qc')
    render_dir.mkdir()
    (c.output / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')
    for frame in frames:
        if not 1 <= frame <= scene.frame_end:
            raise ValueError('Frame outside shot')
        scene.frame_set(frame)
        path = render_dir / f'frame-{frame:04d}.png'
        scene.render.filepath = str(path)
        begin = time.monotonic()
        bpy.ops.render.render(write_still=True)
        data['outputs'].append({'path': str(path), 'sha256': sha(path), 'frame': frame,
                                'render_seconds': round(time.monotonic() - begin, 3)})
        print(f'PLAYER_MOTION_FRAME {frame} {data["outputs"][-1]["render_seconds"]}s {path}', flush=True)
        (c.output / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')
    if c.animation:
        movie = c.output / 'player-motion.mp4'
        subprocess.run(['ffmpeg', '-v', 'error', '-n', '-framerate', '24', '-i', str(render_dir / 'frame-%04d.png'),
                        '-an', '-c:v', 'libx264', '-crf', '17', '-preset', 'slow', '-pix_fmt', 'yuv420p',
                        '-movflags', '+faststart', str(movie)], check=True)
        probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-count_frames', '-show_entries',
                                                   'stream=nb_read_frames,r_frame_rate,width,height', '-of', 'json', str(movie)]))
        if int(probe['streams'][0]['nb_read_frames']) != scene.frame_end:
            raise ValueError('Encoded frame count differs')
        qc = c.output / 'qc'
        qc.mkdir()
        for frame in (1, scene.frame_end // 3, 2 * scene.frame_end // 3, scene.frame_end):
            path = qc / f'frame-{frame:04d}.png'
            shutil.copy2(render_dir / path.name, path)
            data['outputs'].append({'path': str(path), 'sha256': sha(path), 'role': 'retained QC'})
        data['outputs'].append({'path': str(movie), 'sha256': sha(movie), 'ffprobe': probe})
        for output in data['outputs']:
            if Path(output['path']).parent == render_dir:
                output['role'] = 'owned encoding cache; removed after verified encode'
        shutil.rmtree(render_dir)
        print(f'PLAYER_MOTION_CLIP {movie}', flush=True)
    (c.output / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')


if __name__ == '__main__':
    main()
