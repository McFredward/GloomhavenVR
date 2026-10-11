#!/usr/bin/env python3
"""Check imported merchant furniture against actual Unity-skinned torso surfaces.

Run in Blender with -- --skin service1-skin.bin --furniture merchant.fbx --report JSON.
ArmGeometry's station coordinates are used directly. This checks surface penetration,
not subjective animation quality or hand contact; the activity suite covers the latter.
"""
import argparse
import json
import struct
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree


def mesh_tree(objects):
    vertices, triangles = [], []
    for obj in objects:
        obj.data.calc_loop_triangles()
        offset = len(vertices)
        for vertex in obj.data.vertices:
            p = obj.matrix_world @ vertex.co
            # The authoring exporter compensates FBX X handedness for Unity.
            vertices.append((-p.x, p.z, -p.y))
        triangles.extend(tuple(offset + i for i in face.vertices) for face in obj.data.loop_triangles)
    points = np.asarray(vertices)
    return BVHTree.FromPolygons(vertices, triangles, all_triangles=True), points.min(axis=0), points.max(axis=0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--skin', type=Path, required=True)
    parser.add_argument('--furniture', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--report-only', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(args.furniture.resolve()))
    objects = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    assert objects, 'No actual furniture imported'
    tree, low, high = mesh_tree(objects)
    report = {'furniture': str(args.furniture), 'frames': 0, 'penetrating_frames': 0, 'maximum_pairs': 0, 'negative_detected': False}
    # The former rear edge reached .51m. A real thick wooden slab recreates that
    # defect; this must intersect the imported torso, not merely a target marker.
    negative_vertices = [(x, y, z) for x in (-.35, .35) for y in (.909, .955) for z in (-.05, .51)]
    negative_faces = [(0,1,3,2),(4,6,7,5),(0,4,5,1),(2,3,7,6),(0,2,6,4),(1,5,7,3)]
    negative = BVHTree.FromPolygons(negative_vertices, negative_faces)
    with args.skin.open('rb') as stream:
        count, faces, frames = struct.unpack('<iii', stream.read(12))
        records = np.frombuffer(stream.read(faces * 13), dtype=[('label','u1'),('face','<i4',(3,))])
        torso = records['face'][records['label'] == 3]
        assert len(torso) > 1000, 'Actual torso skin is missing'
        for frame in range(frames):
            clock, attention = struct.unpack('<ff', stream.read(8))
            positions = np.frombuffer(stream.read(count * 12), dtype='<f4').reshape((-1, 3))
            triangles = positions[torso]
            selected = np.flatnonzero(np.all(triangles.max(axis=1) >= low, axis=1) & np.all(triangles.min(axis=1) <= high, axis=1))
            pairs = []
            if len(selected):
                indices, inverse = np.unique(torso[selected], return_inverse=True)
                body = BVHTree.FromPolygons(positions[indices], inverse.reshape((-1, 3)), all_triangles=True)
                pairs = tree.overlap(body)
                if not report['negative_detected']:
                    report['negative_detected'] = bool(negative.overlap(body))
            report['frames'] += 1
            if pairs:
                report['penetrating_frames'] += 1
                if len(pairs) > report['maximum_pairs']:
                    report['maximum_pairs'] = len(pairs)
                    report['worst'] = {'frame': frame, 'clock': clock, 'attention': attention}
        assert not stream.read(1), 'Unexpected geometry payload'
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print('TOWN_FURNITURE_CONTACT', json.dumps(report))
    assert report['negative_detected'], 'Former table penetration escaped the actual-skin detector'
    if not args.report_only:
        assert report['penetrating_frames'] == 0, 'Furniture intersects the actual merchant torso'


if __name__ == '__main__':
    main()
