"""Preserve witnessed D3D mip-zero Texture2D.Load zero outside its extent."""
import re

from integer_bits import arguments, call


class LoadBoundsError(RuntimeError):
    pass


def zero(value):
    value = re.sub(r'(?:\.[xyzwrgba]{1,4})+$', '', value.strip())
    return bool(re.fullmatch(r'[+-]?0(?:\.0*)?(?:[eE][+-]?\d+)?[fFuU]?', value))


def mip_zero(coordinate, prior):
    constructor = call(coordinate.strip(), 'int3')
    if not constructor:
        return False
    parts = arguments(constructor[1])
    mip = parts[-1]
    if zero(mip):
        return True
    cast = call(mip, 'asint|asuint')
    mip = cast[1].strip() if cast else mip
    reference = re.fullmatch(r'(\w+)\.w', mip)
    if not reference:
        return False
    name = reference[1]
    writes = list(re.finditer(r'\b' + re.escape(name) + r'(?P<mask>\.[xyzwrgba]{1,4})?\s*=\s*(?P<rhs>[^;]+);', prior))
    for write in reversed(writes):
        mask = write['mask']
        if mask and 'w' not in mask and 'a' not in mask:
            continue
        if mask in ('.w', '.a'):
            return zero(write['rhs'])
        vector = call(write['rhs'].strip(), 'float4')
        if vector and not mask:
            values = arguments(vector[1])
            return len(values) == 4 and zero(values[3])
        return False
    return False


def restore(hlsl):
    textures = {m[2]: m[1] for m in re.finditer(r'\b(Texture\w+)\s*<\s*float4\s*>\s+(\w+)\s*;', hlsl)}
    changes, proofs = [], {}
    for match in re.finditer(r'\b(\w+)\.Load\s*\(', hlsl):
        resource = match[1]
        if resource not in textures:
            continue  # A raw/structured buffer has its separate exact contract.
        if textures[resource] != 'Texture2D':
            raise LoadBoundsError('Native graphics Load dimension needs an exact adapter: ' + textures[resource])
        depth, end = 1, match.end()
        while end < len(hlsl) and depth:
            depth += hlsl[end] == '('; depth -= hlsl[end] == ')'; end += 1
        coordinate = hlsl[match.end():end - 1]
        if depth or len(arguments(coordinate)) != 1 or not mip_zero(coordinate, hlsl[:match.start()]):
            raise LoadBoundsError('Native graphics Load mip/overload is not proven zero: ' + resource)
        changes.append((match.start(), end, 'QuestNativeLoadZero_' + resource + '(' + coordinate + ')'))
        proofs[resource] = proofs.get(resource, 0) + 1
    for start, end, replacement in reversed(changes):
        hlsl = hlsl[:start] + replacement + hlsl[end:]
    helpers = []
    for resource in sorted(proofs):
        helpers.append('float4 QuestNativeLoadZero_' + resource + '(int3 position) {\n'
                       '    uint width, height;\n    ' + resource + '.GetDimensions(width, height);\n'
                       '    if (position.z != 0 || any(position.xy < 0) || uint(position.x) >= width || uint(position.y) >= height)\n'
                       '        return float4(0.0, 0.0, 0.0, 0.0);\n'
                       '    return ' + resource + '.Load(position);\n}\n')
    # The resource must be declared before its helper; helpers must precede the
    # original stage functions that call them. Native static/IO declarations
    # contain no reads and provide the stable insertion boundary.
    if helpers:
        declarations = list(re.finditer(r'\bTexture\w+\s*<\s*float4\s*>\s+\w+\s*;', hlsl))
        position = max(declaration.end() for declaration in declarations)
        hlsl = hlsl[:position] + '\n\n' + '\n'.join(helpers) + hlsl[position:]
    return hlsl, [{'resource': resource, 'dimension': 'Texture2D', 'mip': 0, 'originalLoadCount': count,
                   'outOfBounds': 'original-d3d-zero', 'coordinatesEvaluatedOnce': True} for resource, count in sorted(proofs.items())]
