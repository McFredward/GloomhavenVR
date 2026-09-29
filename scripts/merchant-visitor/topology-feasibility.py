"""Probe support-loop insertion around the P9 merchant sleeve collisions.

This is an analysis-only Blender script. It does not export a shipping FBX.
The face map comes from the verified P9 Unity triangle/FBX polygon match.
"""

import argparse
import csv
import hashlib
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


ORIGINAL_FBX_SHA256 = '3a48114c6b439b98f08c19cb23080177f4171a18eb7b60e7bf35d81b2ad5140e'


def args():
    parser = argparse.ArgumentParser()
    parser.add_argument('--fbx', type=Path, required=True)
    parser.add_argument('--face-map', type=Path, required=True)
    parser.add_argument('--report', type=Path, required=True)
    parser.add_argument('--preloaded', action='store_true',
                        help='Use the existing scene, e.g. a reconstructed P9 pose')
    parser.add_argument('--pose-bones', type=Path,
                        help='Actor-space bone matrices for a source pose')
    parser.add_argument('--bindposes', type=Path,
                        help='Unity bindpose matrices for source pose calibration')
    return parser.parse_args(sys.argv[sys.argv.index('--') + 1:])


def physical_key(vertex):
    return tuple(round(float(component), 4) for component in vertex.co)


def physical_edges(mesh):
    counts = Counter()
    for polygon in mesh.polygons:
        vertices = list(polygon.vertices)
        for first, second in zip(vertices, vertices[1:] + vertices[:1]):
            counts[tuple(sorted((physical_key(mesh.vertices[first]),
                                 physical_key(mesh.vertices[second]))))] += 1
    return counts


def geometry_snapshot(mesh):
    edges = physical_edges(mesh)
    degenerate = 0
    for face in mesh.polygons:
        if face.area < 1e-12:
            degenerate += 1
    return {
        'vertices': len(mesh.vertices),
        'polygons': len(mesh.polygons),
        'physical_boundary_edges': sum(count == 1 for count in edges.values()),
        'physical_nonmanifold_edges': sum(count > 2 for count in edges.values()),
        'degenerate_polygons': degenerate,
        'shape_keys': [key.name for key in mesh.shape_keys.key_blocks],
        'uv_layers': [layer.name for layer in mesh.uv_layers],
    }


def edge_length(edge):
    return (Vector(edge[1]) - Vector(edge[0])).length


def follows_old_boundary(edge, old_edges):
    first, second = (Vector(point) for point in edge)
    for old in old_edges:
        start, end = (Vector(point) for point in old)
        direction = end - start
        length_squared = direction.length_squared
        if length_squared < 1e-12:
            continue
        parameters = [direction.dot(point - start) / length_squared
                      for point in (first, second)]
        if any(t < -1e-3 or t > 1.001 for t in parameters):
            continue
        if all(((start + t * direction) - point).length < 2e-4
               for t, point in zip(parameters, (first, second))):
            return True
    return False


def evaluated_surface(obj):
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    vertices = [vertex.co.copy() for vertex in mesh.vertices]
    polygons = [list(face.vertices) for face in mesh.polygons]
    normals = [face.normal.copy() for face in mesh.polygons]
    evaluated.to_mesh_clear()
    return vertices, polygons, normals


def row_matrix(row):
    return Matrix([[float(row[f'm{4 * r + c}']) for c in range(4)]
                   for r in range(4)])


def apply_actor_pose(rig, pose_path, bind_path):
    """Map measured Unity bones into the unmodified FBX for a diagnostic pose."""
    actor_from_blender = Matrix(((1, 0, 0, 0), (0, 0, 1, 0),
                                 (0, 1, 0, .65), (0, 0, 0, 1)))
    blender_from_actor = actor_from_blender.inverted()
    imported_model = Matrix(((-100, 0, 0, 0), (0, 0, 100, 0),
                             (0, 100, 0, .65), (0, 0, 0, 1)))
    with bind_path.open() as source:
        bind = {row['name']: row_matrix(row) for row in csv.DictReader(source)}
    with pose_path.open() as source:
        actor_pose = {row['name']: row_matrix(row) for row in csv.DictReader(source)}
    if rig.animation_data:
        rig.animation_data.action = None
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    for bone in rig.data.edit_bones:
        bone.use_connect = False
    bpy.ops.object.mode_set(mode='OBJECT')
    for bone in sorted(rig.pose.bones, key=lambda item: len(item.parent_recursive)):
        if bone.name not in actor_pose:
            continue
        rest = bone.bone.matrix_local
        imported_rest = imported_model @ bind[bone.name].inverted()
        basis = rest.inverted() @ blender_from_actor @ imported_rest
        target = blender_from_actor @ actor_pose[bone.name] @ basis.inverted()
        if bone.parent:
            bone.matrix_basis = bone.bone.convert_local_to_pose(
                target, rest, parent_matrix=bone.parent.matrix,
                parent_matrix_local=bone.parent.bone.matrix_local, invert=True)
        else:
            bone.matrix_basis = bone.bone.convert_local_to_pose(
                target, rest, invert=True)
        bpy.context.view_layer.update()


def main():
    options = args()
    if not options.preloaded:
        if hashlib.sha256(options.fbx.read_bytes()).hexdigest() != ORIGINAL_FBX_SHA256:
            raise RuntimeError('The source is not the original merchant FBX')
        bpy.ops.object.select_all(action='SELECT')
        bpy.ops.object.delete(use_global=False)
        bpy.ops.import_scene.fbx(filepath=str(options.fbx))
    if bool(options.pose_bones) != bool(options.bindposes):
        raise RuntimeError('Pose bones and bindposes must be supplied together')
    if options.pose_bones:
        apply_actor_pose(bpy.data.objects['Skeleton'], options.pose_bones,
                         options.bindposes)
    obj = bpy.data.objects['LOD0_0']
    mesh = obj.data
    original = geometry_snapshot(mesh)
    original_boundary = {edge for edge, count in physical_edges(mesh).items() if count == 1}
    seeds = {int(row['blenderPolygon']) for row in
             csv.DictReader(options.face_map.open())}

    # FBX carries separate vertices at UV and normal seams. Expand by physical
    # edges so both halves of every split edge get the same support loop.
    edge_faces = defaultdict(set)
    for face in mesh.polygons:
        verts = list(face.vertices)
        for first, second in zip(verts, verts[1:] + verts[:1]):
            edge = tuple(sorted((physical_key(mesh.vertices[first]),
                                 physical_key(mesh.vertices[second]))))
            edge_faces[edge].add(face.index)
    ring = set(seeds)
    for face in list(seeds):
        verts = list(mesh.polygons[face].vertices)
        for first, second in zip(verts, verts[1:] + verts[:1]):
            edge = tuple(sorted((physical_key(mesh.vertices[first]),
                                 physical_key(mesh.vertices[second]))))
            ring.update(edge_faces[edge])
    ring = {face for face in ring if mesh.polygons[face].material_index == 0}
    original_vertices = [vertex.co.copy() for vertex in mesh.vertices]
    original_faces = [list(face.vertices) for face in mesh.polygons]
    original_surface = BVHTree.FromPolygons(original_vertices, original_faces)
    posed_original_vertices, posed_original_faces, posed_original_normals = evaluated_surface(obj)
    posed_original_surface = BVHTree.FromPolygons(posed_original_vertices,
                                                  posed_original_faces)
    original_vertex_count = len(mesh.vertices)
    split_edges = set()
    for face_index in ring:
        verts = list(mesh.polygons[face_index].vertices)
        for first, second in zip(verts, verts[1:] + verts[:1]):
            split_edges.add(tuple(sorted((physical_key(mesh.vertices[first]),
                                          physical_key(mesh.vertices[second])))))
    matching_edges = {edge.index for edge in mesh.edges
                      if tuple(sorted((physical_key(mesh.vertices[edge.vertices[0]]),
                                       physical_key(mesh.vertices[edge.vertices[1]]))))
                      in split_edges}

    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for edge in mesh.edges:
        edge.select = edge.index in matching_edges
    bpy.context.tool_settings.mesh_select_mode = (False, True, False)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.subdivide(number_cuts=1, smoothness=0, quadcorner='STRAIGHT_CUT')
    bpy.ops.object.mode_set(mode='OBJECT')
    modified = geometry_snapshot(mesh)
    modified_boundary = {edge for edge, count in physical_edges(mesh).items() if count == 1}
    unexplained_boundary = [edge for edge in modified_boundary - original_boundary
                            if not follows_old_boundary(edge, original_boundary)]
    minimum_normal_dot = 1.0
    modified_faces = 0
    maximum_surface_error = 0.0
    for face in mesh.polygons:
        if not any(index >= original_vertex_count for index in face.vertices):
            continue
        modified_faces += 1
        centre = face.center.copy()
        nearest = original_surface.find_nearest(centre)
        if nearest[0] is None:
            raise RuntimeError('A subdivided face lost its original surface')
        maximum_surface_error = max(maximum_surface_error, float(nearest[3]))
        minimum_normal_dot = min(minimum_normal_dot, face.normal.dot(nearest[1]))
    posed_new_vertices, posed_new_faces, posed_new_normals = evaluated_surface(obj)
    minimum_posed_normal_dot = 1.0
    minimum_parent_posed_normal_dot = 1.0
    maximum_posed_surface_error = 0.0
    worst_posed_faces = []
    for face_index, face in enumerate(posed_new_faces):
        if not any(index >= original_vertex_count for index in face):
            continue
        centre = sum((posed_new_vertices[index] for index in face), Vector()) / len(face)
        direction = posed_new_normals[face_index]
        nearest = posed_original_surface.find_nearest(centre)
        if nearest[0] is None:
            raise RuntimeError('A posed subdivided face lost its original surface')
        maximum_posed_surface_error = max(maximum_posed_surface_error, float(nearest[3]))
        minimum_posed_normal_dot = min(minimum_posed_normal_dot,
                                      direction.dot(nearest[1]))
        parent = original_surface.find_nearest(mesh.polygons[face_index].center)[2]
        parent_dot = direction.dot(posed_original_normals[parent])
        minimum_parent_posed_normal_dot = min(minimum_parent_posed_normal_dot, parent_dot)
        if parent_dot < .15:
            worst_posed_faces.append((face_index, parent, parent_dot, tuple(centre)))

    # Existing shapes and the unmodified exterior have to survive. Blender's
    # subdivision should interpolate every shape key and vertex group.
    group_weights = []
    for vertex in mesh.vertices:
        if vertex.groups:
            group_weights.append(sum(weight.weight for weight in vertex.groups))
    group_zero = sum(weight < .9 for weight in group_weights)
    max_group_error = max((abs(weight - 1) for weight in group_weights), default=0)
    shape_key_lengths = {key.name: len(key.data) for key in mesh.shape_keys.key_blocks}
    report = {
        'source': str(options.fbx), 'seed_polygons': len(seeds),
        'ring_polygons': len(ring), 'split_edge_instances': len(matching_edges),
        'physical_split_edges': len(split_edges), 'before': original, 'after': modified,
        'shape_key_lengths': shape_key_lengths,
        'underweighted_vertices': group_zero,
        'maximum_weight_sum_error': max_group_error,
        'new_vertex_faces': modified_faces,
        'minimum_rest_normal_dot': minimum_normal_dot,
        'maximum_rest_surface_error_m': maximum_surface_error,
        'minimum_posed_normal_dot': minimum_posed_normal_dot,
        'minimum_parent_posed_normal_dot': minimum_parent_posed_normal_dot,
        'bad_posed_faces': worst_posed_faces[:30],
        'maximum_posed_surface_error_m': maximum_posed_surface_error,
        'boundary_delta': modified['physical_boundary_edges'] - original['physical_boundary_edges'],
        'boundary_length_delta_m': sum(map(edge_length, modified_boundary)) -
                                   sum(map(edge_length, original_boundary)),
        'unexplained_new_boundary_edges': len(unexplained_boundary),
        'nonmanifold_delta': modified['physical_nonmanifold_edges'] - original['physical_nonmanifold_edges'],
        'degenerate_delta': modified['degenerate_polygons'] - original['degenerate_polygons'],
    }
    options.report.parent.mkdir(parents=True, exist_ok=True)
    options.report.write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report, indent=2), flush=True)
    if (report['unexplained_new_boundary_edges'] or
            abs(report['boundary_length_delta_m']) > .0005 or
            report['nonmanifold_delta'] or
            report['degenerate_delta'] or group_zero or
            minimum_normal_dot < .15 or maximum_surface_error > .001 or
            minimum_parent_posed_normal_dot < .15 or maximum_posed_surface_error > .001 or
            original['shape_keys'] != modified['shape_keys'] or
            original['uv_layers'] != modified['uv_layers'] or
            any(size != len(mesh.vertices) for size in shape_key_lengths.values())):
        raise RuntimeError('Support-loop subdivision failed the topology contract')


if __name__ == '__main__':
    main()
