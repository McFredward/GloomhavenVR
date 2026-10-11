#!/usr/bin/env python3
"""Intersect imported cabinet/holder triangles along exported production Unity poses.

Run in Blender with -- --poses roller-geometry.csv --furniture merchant.fbx
--cassette merchant_cassette.fbx --report report.json. No authored assets are modified.
The CSV comes from check-town-service-catalog.py's actual production motion functions.
"""
import argparse
import csv
import json
import sys
from pathlib import Path
import bpy
from mathutils import Quaternion, Vector
from mathutils.bvhtree import BVHTree


def geometry(objects):
    vertices, triangles = [], []
    for obj in objects:
        obj.data.calc_loop_triangles()
        offset = len(vertices)
        for vertex in obj.data.vertices:
            p = obj.matrix_world @ vertex.co
            vertices.append(Vector((-p.x, p.z, -p.y)))
        triangles.extend(tuple(offset + i for i in face.vertices) for face in obj.data.loop_triangles)
    return vertices, triangles


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--poses', type=Path, required=True)
    parser.add_argument('--furniture', type=Path, required=True)
    parser.add_argument('--cassette', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(args.furniture.resolve()))
    furniture = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    bpy.ops.import_scene.fbx(filepath=str(args.cassette.resolve()))
    imported = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH' and obj not in furniture]
    row_groups = [[obj for obj in imported if obj.parent and obj.parent.name == 'Row' + str(row)] for row in range(3)]
    assert all(row_groups), 'Actual articulated holder meshes are missing'
    static_parts = [obj for obj in imported if not any(obj in group for group in row_groups)]
    points, triangles = geometry(furniture)
    offset = Vector((-.95, 1.220, .035))
    fixed = BVHTree.FromPolygons(points, triangles, all_triangles=True)
    # Fixed cassette sides travel in depth with the assembly, but never rotate with a row.
    side_vertices, side_faces = geometry(static_parts)
    rows = [geometry(group) for group in row_groups]
    report = {'poses': str(args.poses), 'furniture': str(args.furniture), 'cassette': str(args.cassette),
              'sampled_rows': 0, 'intersections': 0, 'side_intersections': 0, 'row_intersections': 0, 'negative_detected': False, 'examples': []}
    side_depth = None
    row_trees = []
    with args.poses.open() as stream:
        for record in csv.DictReader(stream):
            phase = float(record['phase'])
            if round(phase * 1000) % 5: continue
            row = int(record['row'])
            rotation = Quaternion(tuple(float(record[name]) for name in ('qw','qx','qy','qz')))
            position = Vector(tuple(float(record[name]) for name in ('x','y','z')))
            depth = float(record['depth'])
            home = Vector((0, (row - 1) * .17, 0))
            vertices, faces = rows[row]
            transformed = [rotation @ (v-home) + position + offset + Vector((0,0,depth)) for v in vertices]
            holder = BVHTree.FromPolygons(transformed, faces, all_triangles=True)
            if row == 0: row_trees.clear()
            row_pairs = sum(len(other.overlap(holder)) for other in row_trees)
            row_trees.append(holder)
            pairs = fixed.overlap(holder)
            if depth != side_depth:
                side_depth = depth
                sides = BVHTree.FromPolygons([v+offset+Vector((0,0,depth)) for v in side_vertices],side_faces,all_triangles=True)
            side_pairs = sides.overlap(holder)
            report['sampled_rows'] += 1
            report['intersections'] += bool(pairs)
            report['side_intersections'] += bool(side_pairs)
            report['row_intersections'] += bool(row_pairs)
            if (pairs or side_pairs or row_pairs) and len(report['examples']) < 12:
                report['examples'].append({'phase': phase, 'direction': int(record['direction']), 'row': row,
                                           'cabinet_pairs': len(pairs), 'side_pairs': len(side_pairs), 'row_pairs': row_pairs})
            if not report['negative_detected'] and .12 < phase < .4:
                # The original un-cleared roller path must hit the real fascia/header.
                old = BVHTree.FromPolygons([v-Vector((0,0,depth)) for v in transformed],faces,all_triangles=True)
                report['negative_detected'] = bool(fixed.overlap(old))
    args.report.write_text(json.dumps(report, indent=2) + '\n')
    print('TOWN_CABINET_ROLL', json.dumps(report))
    assert report['negative_detected'], 'Uncleared roller corruption escaped the imported geometry check'
    assert not report['intersections'] and not report['side_intersections'] and not report['row_intersections'], 'Actual holder geometry intersects cabinet or fixed rails'


if __name__ == '__main__':
    main()
