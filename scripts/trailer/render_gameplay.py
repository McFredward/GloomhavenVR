#!/usr/bin/env python3
"""Animate original assets locally for a cinematic reconstruction, not gameplay.

Run through Blender 4.2. The packed lift scene supplies the exact approved world.
The held hero returns to its original idle cell before the montage camera moves.
No API calls, game-state writes or changes to read-only source banks are made.
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
from mathutils import Matrix, Quaternion, Vector


ROOT = Path('/home/claw/gloomhaven_vr')
MEDIA = ROOT / '.planning/debug/trailer'
SHOTS = {'release': 36, 'table_orbit': 72, 'figure_dolly': 60}
BOARD_CELL = Vector((0, .17, .005))
spec = importlib.util.spec_from_file_location('opener', Path(__file__).with_name('render_opener.py'))
opener = importlib.util.module_from_spec(spec)
spec.loader.exec_module(opener)
base = opener.base


def sha(path):
    return hashlib.file_digest(Path(path).open('rb'), 'sha256').hexdigest()


def arguments():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--source-scene', type=Path, default=MEDIA / 'opener/plates/godmask_lift.blend')
    p.add_argument('--source-manifest', type=Path, default=MEDIA / 'opener/plates/opener-manifest-reveal.json')
    p.add_argument('--assets', type=Path, default=ROOT / 'unity/GloomhavenVR.Assets/Assets/Bundle')
    p.add_argument('--native-scene', type=Path, default=MEDIA / 'native-scene')
    p.add_argument('--shots', default='release,table_orbit,figure_dolly')
    p.add_argument('--width', type=int, default=1920)
    p.add_argument('--samples', type=int, default=24)
    p.add_argument('--frames', default='1,last', help='Preview frames, comma separated; last is supported')
    p.add_argument('--animation', action='store_true', help='Render every frame and encode a silent H.264 clip')
    p.add_argument('--save-blend', action='store_true')
    c = p.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
    for key in ('output', 'source_scene', 'source_manifest', 'assets', 'native_scene'):
        setattr(c, key, getattr(c, key).resolve())
    c.shots = c.shots.split(',')
    if any(s not in SHOTS for s in c.shots):
        p.error('Unknown shot')
    if not c.output.is_relative_to((MEDIA / 'cinematic').resolve()):
        p.error('All output must be under the main checkout .planning/debug/trailer/cinematic/')
    if c.width < 64 or c.samples < 1:
        p.error('Positive resolution and sample count required')
    if any((c.output / shot).exists() for shot in c.shots):
        p.error('Use a new output version; existing shot evidence is never overwritten')
    if not c.source_scene.is_file() or not c.source_manifest.is_file():
        p.error('Packed source scene and its source manifest are required')
    c.output.mkdir(parents=True, exist_ok=True)
    return c


def key_transform(obj, frame, matrix):
    obj.matrix_world = matrix
    obj.keyframe_insert('location', frame=frame)
    obj.keyframe_insert('rotation_euler', frame=frame)
    obj.keyframe_insert('scale', frame=frame)


def replace_grip(c):
    """Keep the original live skin/rig, matching the packed endpoint precisely."""
    baked = [o for o in bpy.data.objects if o.name.startswith('Exact leather glove thumb/index grip')]
    if len(baked) != 1:
        raise ValueError('Expected one original baked leather grip')
    grip_matrix = baked[0].matrix_world.copy()
    baked_world = [grip_matrix @ v.co for v in baked[0].data.vertices]
    before = set(bpy.data.objects)
    source = opener.source(c.assets / 'Hands/VRHand_R_rig.fbx')
    bpy.ops.import_scene.fbx(filepath=str(source))
    imported = set(bpy.data.objects) - before
    armature = next(o for o in imported if o.type == 'ARMATURE')
    curls = {'Thumb': 1.0, 'Index': .6, 'Middle': .9, 'Ring': 1.0, 'Pinky': 1.0}
    for obj in imported:
        if obj.animation_data:
            obj.animation_data_clear()
    for finger, curl in curls.items():
        angles = (25, 45, 60) if finger == 'Thumb' else (75, 95, 65)
        for joint, angle in zip(('Root', 'Mid', 'Tip'), angles):
            bone = armature.pose.bones[f'Anchor_{finger}_{joint}']
            bone.rotation_mode = 'QUATERNION'
            bone.rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * curl))
    bpy.context.view_layer.update()
    # The imported mesh and armature keep their mutual bind-space relation.
    old_world = {o: o.matrix_world.copy() for o in imported}
    def depth(obj):
        return 0 if obj.parent is None else 1 + depth(obj.parent)
    for obj in sorted(imported, key=depth):
        obj.matrix_world = grip_matrix @ old_world[obj]
        obj.name = 'Original release rig ' + obj.name
        if obj.type == 'MESH':
            obj.data.materials.clear()
            obj.data.materials.append(baked[0].data.materials[0])
    bpy.context.view_layer.update()
    graph = bpy.context.evaluated_depsgraph_get()
    actual_world = []
    for obj in imported:
        if obj.type == 'MESH':
            actual_world.extend(obj.matrix_world @ v.co for v in obj.evaluated_get(graph).data.vertices)
    if len(actual_world) != len(baked_world):
        raise ValueError('Original grip vertex count changed')
    error = max((a - b).length for a, b in zip(actual_world, baked_world))
    if error > 1e-5:
        raise ValueError(f'Live source rig does not match packed grip: {error:g} m')
    bpy.data.objects.remove(baked[0], do_unlink=True)
    return armature, curls, error


def configure_world(c, shot):
    bpy.ops.wm.open_mainfile(filepath=str(c.source_scene))
    scene = bpy.context.scene
    scene.name = shot
    for obj in bpy.data.objects:
        if obj.animation_data:
            obj.animation_data_clear()
    base.USED.clear()
    base.ASSET_REPORT.clear()
    hero = [o for o in bpy.data.objects if o.name.startswith('Original native brute ')]
    if not hero:
        raise ValueError('The source scene has no original Brute')
    rig, curls, grip_error = replace_grip(c)
    # Four tracked hands, as required by the two-player avatar contract.
    base.import_asset(c, 'Hands/VRHand_L_rig.fbx', base.hand_mat(c, 'VRHand'),
                      'Exact Grimhorn left', (-.54, .12, .26),
                      euler=(90, 8, 15), scale=1.8, curl=.16)
    opener.native_actor(c, 'livingbones', (.24, -.04, .005), height=.19, yaw=-150)
    # The approved source surround ends outside its initial view. Continue only
    # the same distant set dressing so the crane does not expose a black edge.
    stone = bpy.data.materials['Native monastery stone surface']
    for row in range(13):
        for col in tuple(range(-4, 0)) + tuple(range(16, 20)):
            base.cube(f'Distant tavern masonry extension {row} {col}',
                      ((col - 7.5) * .5 + (row % 2) * .25, 3.6, row * .27 - 1.2),
                      (.49, .16, .265), stone, .007)
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.eevee.taa_render_samples = c.samples
    scene.render.resolution_x, scene.render.resolution_y = c.width, round(c.width * 9 / 16)
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGB'
    scene.render.fps = 24
    scene.frame_start, scene.frame_end = 1, SHOTS[shot]
    scene.frame_set(1)
    cam = scene.camera
    cam.data.dof.aperture_fstop = 9
    cam.data.dof.focus_distance = (cam.location - Vector((0, .17, .31))).length
    return scene, hero, rig, curls, grip_error


def animate_release(scene, hero, rig, curls):
    initial = {o: o.matrix_world.copy() for o in hero + [rig]}
    land = 22
    down = Matrix.Translation(Vector((0, 0, -.262)))
    for obj in hero:
        key_transform(obj, 1, initial[obj])
        key_transform(obj, land, down @ initial[obj])
        key_transform(obj, scene.frame_end, down @ initial[obj])
    key_transform(rig, 1, initial[rig])
    key_transform(rig, land, down @ initial[rig])
    retreat = Matrix.Translation(Vector((-.12, .04, .16)))
    key_transform(rig, scene.frame_end, retreat @ down @ initial[rig])
    for finger, curl in curls.items():
        angles = (25, 45, 60) if finger == 'Thumb' else (75, 95, 65)
        for joint, angle in zip(('Root', 'Mid', 'Tip'), angles):
            bone = rig.pose.bones[f'Anchor_{finger}_{joint}']
            bone.rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * curl))
            bone.keyframe_insert('rotation_quaternion', frame=1)
            bone.keyframe_insert('rotation_quaternion', frame=land)
            bone.rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * .16))
            bone.keyframe_insert('rotation_quaternion', frame=scene.frame_end)
    return {'landing_frame': land, 'idle_board_cell': list(BOARD_CELL),
            'held_height_metres': .262, 'native_action': 'none; idle-only release',
            'finger_motion': 'original source armature curl; opening after landing'}


def place_idle(hero, rig, curls):
    down = Matrix.Translation(Vector((0, 0, -.262)))
    for obj in hero:
        obj.matrix_world = down @ obj.matrix_world
    rig.matrix_world = Matrix.Translation(Vector((-.12, .04, -.102))) @ rig.matrix_world
    for finger in curls:
        angles = (25, 45, 60) if finger == 'Thumb' else (75, 95, 65)
        for joint, angle in zip(('Root', 'Mid', 'Tip'), angles):
            rig.pose.bones[f'Anchor_{finger}_{joint}'].rotation_quaternion = Quaternion((1, 0, 0), math.radians(angle * .16))


def camera_motion(scene, shot):
    cam = scene.camera
    if shot == 'table_orbit':
        start, end = Vector((.40, -2.70, 1.68)), Vector((-.92, -2.40, 1.92))
        target = Vector((.36, .58, .61))
        cam.data.lens = 38
        focus = Vector((.35, .50, .50))
    elif shot == 'figure_dolly':
        start, end = Vector((-.07, -.59, .11)), Vector((.17, -.53, .13))
        target = Vector((.085, .12, .112))
        cam.data.lens = 52
        focus = Vector((0, .17, .11))
        cam.data.dof.aperture_fstop = 6
    else:
        return
    # Key each camera frame to a smooth rail with a continuously evaluated aim.
    # This avoids Euler interpolation creating an unintended sideways glance.
    for frame in range(1, scene.frame_end + 1):
        t = (frame - 1) / (scene.frame_end - 1)
        t = t * t * (3 - 2 * t)
        cam.location = start.lerp(end, t)
        base.look_at(cam, target)
        cam.data.dof.focus_distance = (cam.location - focus).length
        cam.keyframe_insert('location', frame=frame)
        cam.keyframe_insert('rotation_euler', frame=frame)
        cam.data.keyframe_insert('dof.focus_distance', frame=frame)


def receipt(c, shot, scene, control, grip_error, directory):
    original = json.loads(c.source_manifest.read_text())
    sources = {item['path']: item for item in original['sources']}
    for path in sorted(base.USED):
        sources[str(path)] = {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha(path)}
    # Every source in the inherited receipt must still match. This protects the
    # new render from silently relying on a different export or source revision.
    for entry in sources.values():
        if sha(entry['path']) != entry['sha256']:
            raise ValueError(f"Source hash changed: {entry['path']}")
    pose_samples = []
    for frame in sorted({1, 22 if shot == 'release' else scene.frame_end // 2, scene.frame_end}):
        scene.frame_set(frame)
        bounds = [o.matrix_world @ v.co for o in scene.objects
                  if o.name.startswith('Original native brute ') for v in o.data.vertices]
        pose_samples.append({'frame': frame,
                             'hero_bounds_world': [[min(v[i] for v in bounds) for i in range(3)],
                                                   [max(v[i] for v in bounds) for i in range(3)]],
                             'camera_location': list(scene.camera.location),
                             'camera_rotation_radians': list(scene.camera.rotation_euler)})
    scene.frame_set(1)
    return {'presentation': 'Local cinematic reconstruction with original meshes; not runtime capture',
            'shot': shot, 'frames': scene.frame_end, 'fps': 24,
            'resolution': [scene.render.resolution_x, scene.render.resolution_y],
            'samples': c.samples, 'blender_version': bpy.app.version_string,
            'source_scene': {'path': str(c.source_scene), 'sha256': sha(c.source_scene)},
            'source_manifest': {'path': str(c.source_manifest), 'sha256': sha(c.source_manifest)},
            'tool': {'path': str(Path(__file__).resolve()), 'sha256': sha(__file__)},
            'sources': list(sources.values()), 'new_asset_instances': base.ASSET_REPORT,
            'release_control': control, 'live_grip_matches_packed_mesh_max_error_metres': grip_error,
            'control_pose_samples': pose_samples,
            'objects_at_start': [{'name': o.name, 'type': o.type, 'matrix_world': [list(row) for row in o.matrix_world]}
                                 for o in scene.objects if o.type in ('MESH', 'ARMATURE', 'CAMERA')],
            'outputs': [], 'timings_seconds': {}}


def main():
    c = arguments()
    for shot in c.shots:
        directory = c.output / shot
        directory.mkdir()
        scene, hero, rig, curls, grip_error = configure_world(c, shot)
        if shot == 'release':
            control = animate_release(scene, hero, rig, curls)
        else:
            place_idle(hero, rig, curls)
            control = {'idle_board_cell': list(BOARD_CELL), 'native_action': 'none; static original idle actors'}
            camera_motion(scene, shot)
        scene.frame_set(1)
        data = receipt(c, shot, scene, control, grip_error, directory)
        if c.save_blend:
            # FBX importers retain unused source materials and unresolved legacy
            # texture filenames. Purge only unused datablocks before packing the
            # actual adapted materials and their original textures.
            bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)
            bpy.ops.file.pack_all()
            target = directory / f'{shot}.blend'
            bpy.ops.wm.save_as_mainfile(filepath=str(target), compress=True)
            data['outputs'].append({'path': str(target), 'sha256': sha(target)})
        if c.animation:
            frames = list(range(1, scene.frame_end + 1))
            render_dir = directory / 'encode-frames'
        else:
            frames = sorted(set(scene.frame_end if x == 'last' else int(x) for x in c.frames.split(',')))
            if any(not 1 <= f <= scene.frame_end for f in frames):
                raise ValueError('Preview frame outside shot')
            render_dir = directory / 'qc'
        render_dir.mkdir()
        (directory / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')
        for frame in frames:
            scene.frame_set(frame)
            target = render_dir / f'frame-{frame:04d}.png'
            scene.render.filepath = str(target)
            begin = time.monotonic()
            bpy.ops.render.render(write_still=True)
            data['timings_seconds'][str(frame)] = round(time.monotonic() - begin, 3)
            data['outputs'].append({'path': str(target), 'sha256': sha(target), 'frame': frame})
            print(f'FRAME_READY {shot} {frame} {data["timings_seconds"][str(frame)]}s {target}', flush=True)
            (directory / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')
        if c.animation:
            target = directory / f'{shot}.mp4'
            subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-n', '-framerate', '24',
                            '-i', str(render_dir / 'frame-%04d.png'), '-an', '-c:v', 'libx264',
                            '-preset', 'slow', '-crf', '16', '-pix_fmt', 'yuv420p',
                            '-movflags', '+faststart', str(target)], check=True)
            probe = json.loads(subprocess.check_output(['ffprobe', '-v', 'error', '-count_frames',
                            '-show_entries', 'stream=nb_read_frames,width,height,r_frame_rate',
                            '-of', 'json', str(target)]))
            stream = probe['streams'][0]
            if int(stream['nb_read_frames']) != scene.frame_end or stream['r_frame_rate'] != '24/1':
                raise ValueError('Encoded frame count or frame rate differs')
            data['outputs'].append({'path': str(target), 'sha256': sha(target), 'ffprobe': probe})
            qc = directory / 'qc'
            qc.mkdir()
            keep = {1, scene.frame_end, scene.frame_end // 2, 22} if shot == 'release' else {1, scene.frame_end // 2, scene.frame_end}
            for frame in sorted(keep):
                retained = qc / f'frame-{frame:04d}.png'
                shutil.copy2(render_dir / retained.name, retained)
                data['outputs'].append({'path': str(retained), 'sha256': sha(retained),
                                        'frame': frame, 'role': 'retained QC capture'})
            for output in data['outputs']:
                if Path(output['path']).parent == render_dir:
                    output['role'] = 'regenerable encoding frame; removed after verification'
            data['cleanup'] = 'Owned regenerable encoding frames removed after verified encode; selected QC capture evidence retained'
            shutil.rmtree(render_dir)
            print(f'CLIP_READY {target}', flush=True)
        (directory / 'manifest.json').write_text(json.dumps(data, indent=2) + '\n')


if __name__ == '__main__':
    main()
