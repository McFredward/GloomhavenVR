"""Preserve native Direct3D integer texture-read zero semantics on GLES.

The audited original compute bank uses only 2D/3D float4 LD instructions at
literal mip zero, without an offset operand. Other shapes fail for re-audit.
Image stores already discard invalid coordinates under GLES 3.1 section 8.22.
Normalized sampling/gathers retain their original sampler addressing unchanged.
"""
from __future__ import annotations

import re

if __package__:
    from .native import ComputeRecoveryError
else:
    from native import ComputeRecoveryError


def split_arguments(expression: str) -> list[str]:
    depth, start, result = 0, 0, []
    for index, character in enumerate(expression):
        depth += (character == '(') - (character == ')')
        if depth < 0:
            raise ComputeRecoveryError("Unbalanced original texture-read expression.")
        if character == ',' and depth == 0:
            result.append(expression[start:index].strip())
            start = index + 1
    if depth:
        raise ComputeRecoveryError("Unbalanced original texture-read expression.")
    result.append(expression[start:].strip())
    return result


def without_parentheses(value: str) -> str:
    while value.startswith('(') and value.endswith(')'):
        depth = 0
        for index, character in enumerate(value):
            depth += (character == '(') - (character == ')')
            if depth == 0 and index != len(value) - 1:
                return value
        value = value[1:-1].strip()
    return value


def restore_texture_loads(hlsl: str, native_ld_count: int) -> tuple[str, list[dict]]:
    """Wrap each original LD once, querying the bound resource's real dimensions.

    This defines only the original D3D zero result for invalid reads. It does not
    clamp coordinates, substitute edge texels or change an in-bounds value.
    Even an existing viewport guard is independent of the bound image's extent.
    """
    declarations = {name: dimension for dimension, name in re.findall(
        r"(?m)^Texture(2D|3D)<float4>\s+(\w+)\s*;", hlsl)}
    calls, rows = [], {}
    for match in re.finditer(r"\b(\w+)\.Load\(", hlsl):
        name = match[1]
        if name not in declarations:
            raise ComputeRecoveryError("Original compute LD dimension/type needs explicit bounds recovery: " + name)
        depth, end = 1, match.end()
        while depth and end < len(hlsl):
            depth += (hlsl[end] == '(') - (hlsl[end] == ')')
            end += 1
        if depth:
            raise ComputeRecoveryError("Unbalanced original compute LD operand.")
        arguments = split_arguments(hlsl[match.end():end - 1])
        size = 3 if declarations[name] == "2D" else 4
        constructor = re.fullmatch(r"int" + str(size) + r"\((.*)\)", arguments[0], re.S) if len(arguments) == 1 else None
        if constructor is None:
            raise ComputeRecoveryError("Original compute LD overload/coordinate shape changed.")
        coordinates = split_arguments(constructor[1])
        mip = without_parentheses(coordinates[-1])
        if len(coordinates) < 2 or mip not in ("0", "0u", "asint(0u)"):
            # Do not infer the number of accessible mip levels from base size:
            # resources may contain a partial mip chain. GLES3.1 has no core
            # textureQueryLevels, and this original bank never reads other mips.
            raise ComputeRecoveryError("Original compute LD mip is not the audited literal zero.")
        helper = "QuestOriginalLoadZero_" + name
        calls.append((match.start(), end, helper + "(" + arguments[0] + ")"))
        row = rows.setdefault(name, {"name": name, "dimension": declarations[name], "elementType": "float4",
            "mipLevel": 0, "originalLoadCount": 0, "helper": helper,
            "actualBoundResourceDimensionsQueried": True, "coordinatesEvaluatedOnce": True,
            "outOfBoundsResult": [0, 0, 0, 0], "inBoundsValueUnchanged": True})
        row["originalLoadCount"] += 1
    if len(calls) != native_ld_count:
        raise ComputeRecoveryError("Original DXBC LD and translated integer texture-read census differ.")
    if not calls:
        return hlsl, []
    for start, end, replacement in reversed(calls):
        hlsl = hlsl[:start] + replacement + hlsl[end:]
    helpers = []
    for row in rows.values():
        dimension = 2 if row["dimension"] == "2D" else 3
        coordinate_size, mask = dimension + 1, "xy" if dimension == 2 else "xyz"
        outputs = ", ".join("extent." + component for component in mask)
        helpers.append("float4 " + row["helper"] + "(int" + str(coordinate_size) + " location)\n{\n"
            "    uint" + str(dimension) + " extent;\n"
            "    " + row["name"] + ".GetDimensions(" + outputs + ");\n"
            "    [branch] if (any(location." + mask + " < 0) || any(uint" + str(dimension) + "(location." + mask + ") >= extent))\n"
            "        return float4(0, 0, 0, 0);\n"
            "    return " + row["name"] + ".Load(location);\n}\n")
    position = hlsl.find("void comp_main()")
    if position < 0:
        raise ComputeRecoveryError("Original compute entry function is missing.")
    hlsl = hlsl[:position] + "\n".join(helpers) + "\n" + hlsl[position:]
    return hlsl, list(rows.values())


def restore_vectorscope_atomic(hlsl: str) -> tuple[str, dict]:
    """Keep the original unused-result atomic and discard invalid buffer writes.

    The native RGB->scope projection can map an endpoint to size rather than
    size-1, even with saturated RGB. Changing that colour/rounding math would
    alter the original plot; only D3D's invalid-write discard is restored.
    """
    if not re.search(r"(?m)^RWStructuredBuffer<uint> _VectorscopeBuffer;$", hlsl):
        raise ComputeRecoveryError("Original vectorscope buffer stride/type changed.")
    matches = list(re.finditer(r"(?m)^(\s*)InterlockedAdd\(([^\n]+)\);$", hlsl))
    if len(matches) != 1:
        raise ComputeRecoveryError("Original vectorscope atomic census changed.")
    match = matches[0]
    arguments = split_arguments(match[2])
    address = re.fullmatch(r"_VectorscopeBuffer\[(.*)\]", arguments[0]) if len(arguments) == 3 else None
    if address is None or arguments[1] != "1u" or not re.fullmatch(r"_\d+", arguments[2]) \
            or len(re.findall(r"\b" + re.escape(arguments[2]) + r"\b", hlsl)) != 2:
        raise ComputeRecoveryError("Original vectorscope atomic operand or unused-result contract changed.")
    helper = "QuestOriginalVectorscopeAddWithinBuffer"
    hlsl = hlsl[:match.start()] + match[1] + helper + "(" + address[1] + ", " + arguments[1] + ", " + arguments[2] + ");" + hlsl[match.end():]
    function = "void " + helper + "(uint index, uint value, out uint unusedOriginalResult)\n{\n" \
        "    uint count, stride;\n" \
        "    _VectorscopeBuffer.GetDimensions(count, stride);\n" \
        "    unusedOriginalResult = 0u;\n" \
        "    [branch] if (index < count)\n" \
        "        InterlockedAdd(_VectorscopeBuffer[index], value, unusedOriginalResult);\n}\n\n"
    position = hlsl.find("void comp_main()")
    if position < 0: raise ComputeRecoveryError("Original vectorscope entry function missing.")
    hlsl = hlsl[:position] + function + hlsl[position:]
    return hlsl, {"name": "_VectorscopeBuffer", "strideBytes": 4, "originalAtomicCount": 1,
        "actualBoundBufferCountQueried": True, "indexEvaluatedOnce": True,
        "originalReturnedValueUnusedVerified": True, "outOfBoundsWriteDiscarded": True,
        "originalColourMathAndInBoundsAtomicUnchanged": True}
