"""Preserve neutral anatomy while fitting closed lids to the physical cornea."""
import hashlib
import math
import struct
import bpy


def protected_digest(obj):
    digest = hashlib.sha256()
    for vertex in obj.data.vertices:
        digest.update(struct.pack('<3f', *vertex.co))
        for group in vertex.groups:
            digest.update(struct.pack('<if', group.group, group.weight))
    for key in obj.data.shape_keys.key_blocks:
        if key.name in ('BlinkLeft', 'BlinkRight'):
            continue
        for point in key.data:
            digest.update(struct.pack('<3f', *point.co))
    return digest.hexdigest()


def repair():
    bpy.context.view_layer.update()
    report = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or not obj.data.shape_keys:
            continue
        before = protected_digest(obj)
        for side in ('Left', 'Right'):
            eye = bpy.data.objects['Eye' + side]
            cornea = bpy.data.objects['Eye' + side + 'Cornea']
            optical = eye.matrix_world.inverted()
            # The optical +Z frame was retained by the all-object FBX export.
            apex = max((optical @ cornea.matrix_world @ vertex.co).z for vertex in cornea.data.vertices)
            radius = .0078
            centre = apex - radius
            key = obj.data.shape_keys.key_blocks['Blink' + side]
            basis = obj.data.shape_keys.key_blocks['Basis']
            changed, maximum = 0, 0.
            for index, point in enumerate(key.data):
                if (point.co - basis.data[index].co).length < .00005:
                    continue
                world = obj.matrix_world @ point.co
                local = optical @ world
                radial = local.x * local.x + local.y * local.y
                if radial >= radius * radius or not .006 < local.z < .030:
                    continue
                envelope = centre + math.sqrt(radius * radius - radial) + .0007
                if local.z >= envelope - 1e-7:
                    continue
                correction = envelope - local.z
                local.z = envelope
                point.co = obj.matrix_world.inverted() @ eye.matrix_world @ local
                changed += 1
                maximum = max(maximum, correction)
            assert maximum < .006, (obj.name, side, 'Unexpected source pose or optical coordinates')
            report.append(dict(mesh=obj.name, shape=key.name, vertices=changed, maximumMeters=maximum))
        assert protected_digest(obj) == before, 'Neutral geometry, other expressions or weights changed'
        obj.data.update()
    assert len(report) == 6, 'Expected two closed lids on three editable source detail levels'
    return report
