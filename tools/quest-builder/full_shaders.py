"""Recover compiled shader interfaces without inventing rendering defaults.

Unity's original D3D11 programs lack DXBC RDEF names. The per-program serialized
parameter table still records each native cbuffer field and resource binding.
This module recovers those exact interfaces before an open-source DXBC compiler
translates the instruction stream. Unknown layouts stop conversion explicitly.
"""
import ast
import hashlib
import json
import collections
from pathlib import Path
import re
import struct
import subprocess


class ShaderRecoveryError(RuntimeError):
    pass


class Reader:
    def __init__(self, raw, position=0):
        self.raw, self.position = raw, position

    def values(self, layout):
        size = struct.calcsize(layout)
        if self.position < 0 or self.position + size > len(self.raw):
            raise ShaderRecoveryError("Truncated original compiled shader interface.")
        value = struct.unpack_from(layout, self.raw, self.position)
        self.position += size
        return value[0] if len(value) == 1 else value

    def text(self):
        length = self.values("<i")
        if not 0 <= length <= 65536 or self.position + length > len(self.raw):
            raise ShaderRecoveryError("Invalid original shader parameter name.")
        value = self.raw[self.position:self.position + length].decode("utf-8")
        self.position += length
        self.position = (self.position + 3) & ~3
        return value

    def count(self):
        value = self.values("<i")
        if not 0 <= value <= 65536:
            raise ShaderRecoveryError("Invalid original shader interface count.")
        return value


def parameter_delta(raw, offset):
    """Read the Unity 2021 D3D11 subprogram's complete appended interface."""
    reader = Reader(raw, offset)
    source_map = reader.values("<i")
    channels = [reader.values("<2i") for _ in range(reader.count())]
    buffers = []
    for _ in range(reader.count()):
        name, size = reader.text(), reader.values("<i")
        if size < 0 or size % 16:
            raise ShaderRecoveryError("Invalid original cbuffer byte size.")
        fields = []
        for _ in range(reader.count()):
            field_name = reader.text()
            kind, rows, columns, matrix, array_size, byte_offset = reader.values("<6i")
            if kind not in (0, 1) or not 1 <= rows <= 4 or not 1 <= columns <= 4 or matrix not in (0, 1) or array_size < 0 or byte_offset < 0:
                raise ShaderRecoveryError("Unsupported original compiled cbuffer field layout: " + field_name)
            fields.append({"name": field_name, "type": kind, "rows": rows, "columns": columns,
                           "matrix": bool(matrix), "arraySize": array_size, "byteOffset": byte_offset})
        structures = []
        for _ in range(reader.count()):
            struct_name = reader.text()
            byte_offset, array_size, stride = reader.values("<3i")
            if byte_offset < 0 or array_size < 0 or stride <= 0 or stride % 16:
                raise ShaderRecoveryError("Invalid original structured cbuffer layout.")
            members = []
            for _ in range(reader.count()):
                member_name = reader.text()
                kind, rows, columns, matrix, member_array, offset = reader.values("<6i")
                if kind not in (0, 1) or not 1 <= rows <= 4 or not 1 <= columns <= 4 or matrix not in (0, 1) or member_array < 0 or offset < 0:
                    raise ShaderRecoveryError("Unsupported original structured cbuffer member: " + member_name)
                members.append({"name": member_name, "type": kind, "rows": rows, "columns": columns,
                                "matrix": bool(matrix), "arraySize": member_array, "byteOffset": offset})
            structures.append({"name": struct_name, "byteOffset": byte_offset, "arraySize": array_size,
                               "stride": stride, "fields": members})
        buffers.append({"name": name, "bytes": size, "fields": fields, "structures": structures})
    bindings = []
    for _ in range(reader.count()):
        name, kind = reader.text(), reader.values("<i")
        if kind == 1:
            slot, unused = reader.values("<2i")
            if unused != 0:
                raise ShaderRecoveryError("Unsupported original cbuffer binding flags.")
            bindings.append({"name": name, "kind": "cbuffer", "slot": slot})
        elif kind == 0:
            texture, sampler, dimension = reader.values("<3i")
            if dimension not in (2, 4, 6, 8, 10, 12):
                raise ShaderRecoveryError("Unknown original compiled texture dimension.")
            bindings.append({"name": name, "kind": "texture", "slot": texture,
                             "samplerSlot": sampler, "dimension": dimension})
        elif kind == 2:
            slot, array_size = reader.values("<2i")
            if slot < 0 or array_size <= 0:
                raise ShaderRecoveryError("Invalid original structured GPU buffer binding.")
            bindings.append({"name": name, "kind": "buffer", "slot": slot, "arraySize": array_size})
        else:
            raise ShaderRecoveryError("Unsupported original shader resource binding kind: " + str(kind))
    if reader.position != len(raw):
        raise ShaderRecoveryError("Original shader interface contains unexplained trailing bytes.")
    return {"sourceMap": source_map, "channels": channels, "buffers": buffers, "bindings": bindings,
            "allOriginalInterfaceBytesConsumed": True}


def dxbc_container(program):
    start = program.find(b"DXBC")
    if start < 0 or start + 32 > len(program):
        raise ShaderRecoveryError("Original program is not a D3D11 DXBC container.")
    size = struct.unpack_from("<I", program, start + 24)[0]
    if size < 32 or start + size > len(program):
        raise ShaderRecoveryError("Malformed original DXBC container length.")
    raw = program[start:start + size]
    count = struct.unpack_from("<I", raw, 28)[0]
    if count > 64 or 32 + 4 * count > len(raw):
        raise ShaderRecoveryError("Malformed original DXBC chunk directory.")
    chunks = {}
    for index in range(count):
        offset = struct.unpack_from("<I", raw, 32 + 4 * index)[0]
        if offset + 8 > len(raw):
            raise ShaderRecoveryError("DXBC chunk lies outside the original program.")
        name, size = raw[offset:offset + 4], struct.unpack_from("<I", raw, offset + 4)[0]
        if offset + 8 + size > len(raw) or name in chunks:
            raise ShaderRecoveryError("Malformed/duplicate original DXBC chunk.")
        chunks[name] = raw[offset + 8:offset + 8 + size]
    return raw, chunks


def signature(chunk):
    """Restore actual original vertex semantics lost by generic SPIR-V IO."""
    if len(chunk) < 8:
        raise ShaderRecoveryError("Truncated original DXBC semantic signature.")
    count, header = struct.unpack_from("<2I", chunk)
    if header != 8 or 8 + count * 24 > len(chunk):
        raise ShaderRecoveryError("Unsupported original DXBC semantic signature layout.")
    rows = []
    for index in range(count):
        name_offset, semantic_index, system_value, component_type, register, mask = struct.unpack_from("<6I", chunk, 8 + index * 24)
        end = chunk.find(b"\0", name_offset)
        if name_offset >= len(chunk) or end < 0:
            raise ShaderRecoveryError("Original DXBC semantic name lies outside its signature.")
        name = chunk[name_offset:end].decode("ascii")
        rows.append({"semantic": name, "semanticIndex": semantic_index, "systemValue": system_value,
                     "componentType": component_type, "register": register, "mask": mask & 255,
                     "readWriteMask": (mask >> 8) & 255})
    return rows


def native_buffer_layouts(program):
    """Witness each structured/raw resource stride from original DXBC tokens."""
    _, chunks = dxbc_container(program)
    code = chunks.get(b"SHDR", chunks.get(b"SHEX"))
    if code is None or len(code) % 4:
        raise ShaderRecoveryError("Original buffer layout requires a DXBC instruction bank.")
    words = struct.unpack("<" + "I" * (len(code) // 4), code)
    if len(words) < 2 or words[1] != len(words):
        raise ShaderRecoveryError("Original DXBC instruction extent differs.")
    position, result = 2, []
    while position < len(words):
        opcode, length = words[position] & 2047, (words[position] >> 24) & 127
        if opcode == 0x35:  # D3D10 custom data carries its length in word two.
            length = words[position + 1]
        if not length or position + length > len(words):
            raise ShaderRecoveryError("Truncated original DXBC instruction.")
        if opcode in (0xa1, 0xa2):  # dcl_resource_raw / dcl_resource_structured
            expected = 3 if opcode == 0xa1 else 4
            if length != expected or words[position + 1] != 0x107000:
                raise ShaderRecoveryError("Unsupported original indexed GPU buffer declaration.")
            stride = 0 if opcode == 0xa1 else words[position + 3]
            if stride < 0 or stride % 4 or stride > 4096:
                raise ShaderRecoveryError("Original GPU buffer stride is invalid.")
            result.append({"slot": words[position + 2], "kind": "raw" if opcode == 0xa1 else "structured", "strideBytes": stride})
        position += length
    return result


def portable_layer_interface(spirv, outputs):
    """Retain vertex layer math while Unity supplies Quest framebuffer routing.

    DXBC post-processing emits SV_RenderTargetArrayIndex from _DepthSlice.
    GLES multiview routes layers through the native ViewID, rather than this
    unsupported desktop vertex output. Relocate only that interface decoration
    to an ordinary, unused output; every original instruction remains intact.
    """
    expected = {row["register"] for row in outputs if row["semantic"].upper() == "SV_RENDERTARGETARRAYINDEX"}
    if not expected:
        return spirv, []
    words = list(struct.unpack("<" + "I" * (len(spirv) // 4), spirv))
    if words[0] != 0x07230203:
        raise ShaderRecoveryError("Layer adapter requires a valid original translated SPIR-V module.")
    names, decorations, stages = {}, [], []
    position = 5
    while position < len(words):
        size, opcode = words[position] >> 16, words[position] & 65535
        if size == 0 or position + size > len(words):
            raise ShaderRecoveryError("Truncated original SPIR-V instruction in native layer adapter.")
        values = words[position + 1:position + size]
        if opcode == 5:  # OpName
            names[values[0]] = struct.pack("<" + "I" * (len(values) - 1), *values[1:]).split(b"\0", 1)[0].decode()
        elif opcode == 15:  # OpEntryPoint
            stages.append(values[0])
        elif opcode == 71:  # OpDecorate
            decorations.append((position, values))
        position += size
    if stages != [0]:
        raise ShaderRecoveryError("Quest layer adapter only supports an original vertex entry point.")
    used = {values[2] for _, values in decorations if len(values) == 3 and values[1] == 30}
    relocated = []
    for position, values in decorations:
        if len(values) != 3 or values[1:] != [11, 9]:  # BuiltIn Layer
            continue
        name = names.get(values[0])
        match = re.fullmatch(r"o(\d+)", name or "")
        if match is None or int(match[1]) not in expected:
            raise ShaderRecoveryError("Native layer output lost its original DXBC register identity.")
        location = next(index for index in range(32) if index not in used)
        used.add(location)
        words[position + 2:position + 4] = [30, location]  # Location, original instructions untouched
        relocated.append({"kind": "native-vertex-layer-to-unity-framebuffer", "nativeOutput": name,
                          "originalSemantic": "SV_RenderTargetArrayIndex", "portableLocation": location,
                          "originalInstructionsChanged": False, "questLayerRouting": "Unity stereo macros / OVR ViewID",
                          "headsetLayerRoutingVerified": False})
    if {int(row["nativeOutput"][1:]) for row in relocated} != expected:
        raise ShaderRecoveryError("Original layer signature and SPIR-V builtin decorations differ.")
    return struct.pack("<" + "I" * len(words), *words), relocated


def translate(program, output, vkd3d="vkd3d-compiler", spirv_cross="spirv-cross"):
    """Translate exact DXBC math; output still needs recovered Unity uniforms."""
    raw, chunks = dxbc_container(program)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha256(raw).hexdigest()
    dxbc, spirv, hlsl = (output / (key + suffix) for suffix in (".dxbc", ".spv", ".hlsl"))
    dxbc.write_bytes(raw)
    result = subprocess.run([str(vkd3d), "-x", "dxbc-tpf", "-b", "spirv-binary", "-o", str(spirv), str(dxbc)],
                            capture_output=True, text=True)
    if result.returncode:
        raise ShaderRecoveryError("Exact original DXBC translation failed: " + result.stderr[-2000:])
    outputs = signature(chunks[b"OSGN"]) if b"OSGN" in chunks else []
    portable, adapters = portable_layer_interface(spirv.read_bytes(), outputs)
    compiler_input = spirv
    if adapters:
        compiler_input = output / (key + ".portable-io.spv")
        compiler_input.write_bytes(portable)
    result = subprocess.run([str(spirv_cross), str(compiler_input), "--hlsl", "--shader-model", "50"], capture_output=True, text=True)
    if result.returncode:
        raise ShaderRecoveryError("Original instruction stream HLSL translation failed: " + result.stderr[-2000:])
    hlsl.write_text(result.stdout)
    return {"originalDxbcSha256": key, "spirvSha256": hashlib.sha256(spirv.read_bytes()).hexdigest(),
            "translatedHlslSha256": hashlib.sha256(result.stdout.encode()).hexdigest(), "hlslPath": str(hlsl),
            "inputSignature": signature(chunks[b"ISGN"]) if b"ISGN" in chunks else [],
            "outputSignature": outputs, "outputInterfaceAdapters": adapters, "originalBufferLayouts": native_buffer_layouts(raw),
            "compilerSpirvSha256": hashlib.sha256(portable).hexdigest(), "hlslShaderModel": 50,
            "unityUniformsRestored": False, "androidShaderCompiled": False, "pixelParityVerified": False}


def merge_interface(common, names, delta):
    """Combine original partial common parameters and per-variant deltas."""
    names = {int(index): name for name, index in names}
    buffers = {}
    def add_buffer(name, size, fields, structures=()):
        if name not in buffers:
            buffers[name] = {"name": name, "bytes": size, "fields": [], "structures": []}
        # Unity's common table is the shared parameter prefix. A keyword
        # delta can extend that same original cbuffer with additional fields;
        # its serialized size is not necessarily equal to the common prefix.
        # Keep both native extents, and bind only scalars read by the actual
        # DXBC declaration later. All overlapping field layouts still agree.
        buffers[name]["bytes"] = max(buffers[name]["bytes"], size)
        seen = {field["byteOffset"]: field for field in buffers[name]["fields"]}
        for field in fields:
            previous = seen.get(field["byteOffset"])
            if previous is not None and previous != field:
                raise ShaderRecoveryError("Original common/delta cbuffer fields overlap: " + field["name"])
            if previous is None:
                buffers[name]["fields"].append(field)
                seen[field["byteOffset"]] = field
        buffers[name]["structures"].extend(structures)
    for buffer in common.get("m_ConstantBuffers", []):
        fields = []
        for field in buffer.get("m_VectorParams", []):
            fields.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": 1,
                           "columns": field["m_Dim"], "matrix": False, "arraySize": field["m_ArraySize"],
                           "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
        for field in buffer.get("m_MatrixParams", []):
            fields.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": field["m_RowCount"],
                           "columns": 4, "matrix": True, "arraySize": field["m_ArraySize"],
                           "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
        structures = []
        for structure in buffer.get("m_StructParams", []) or []:
            members = []
            for field in structure.get("m_VectorMembers", []):
                members.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": 1,
                                "columns": field["m_Dim"], "matrix": False, "arraySize": field["m_ArraySize"],
                                "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
            for field in structure.get("m_MatrixMembers", []):
                members.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": field["m_RowCount"],
                                "columns": 4, "matrix": True, "arraySize": field["m_ArraySize"],
                                "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
            structures.append({"name": names[structure["m_NameIndex"]], "byteOffset": structure["m_Index"],
                               "arraySize": structure["m_ArraySize"], "stride": structure["m_StructSize"], "fields": members})
        add_buffer(names[buffer["m_NameIndex"]], buffer["m_Size"], fields, structures)
    for buffer in delta["buffers"]:
        add_buffer(buffer["name"], buffer["bytes"], buffer["fields"], buffer.get("structures", []))
    bindings = [{"name": names[binding["m_NameIndex"]], "kind": "cbuffer", "slot": binding["m_Index"]}
                for binding in common.get("m_ConstantBufferBindings", [])]
    for binding in common.get("m_TextureParams", []):
        bindings.append({"name": names[binding["m_NameIndex"]], "kind": "texture", "slot": binding["m_Index"],
                         "samplerSlot": binding["m_SamplerIndex"], "dimension": binding["m_Dim"] * 2})
    for binding in common.get("m_BufferParams", []):
        bindings.append({"name": names[binding["m_NameIndex"]], "kind": "buffer", "slot": binding["m_Index"],
                         "arraySize": binding["m_ArraySize"]})
    for binding in common.get("m_Samplers", []):
        bindings.append({"kind": "sampler", "slot": binding["bindPoint"], "nativeState": binding["sampler"]})
    bindings.extend(delta["bindings"])
    unique = {}
    for binding in bindings:
        key = (binding["kind"], binding["slot"])
        if key in unique and unique[key] != binding:
            raise ShaderRecoveryError("Original common/delta resource bindings disagree.")
        unique[key] = binding
    return {"buffers": list(buffers.values()), "bindings": list(unique.values())}


# Exact Unity 2021.3.5f1 Windows compiler calibration. See the reproducible
# QuestShaderSamplerCalibration fixture and native-inline-samplers proof.
NATIVE_INLINE_SAMPLERS = {0: "point_repeat", 1: "linear_repeat", 2: "trilinear_repeat",
                         84: "point_clamp", 85: "linear_clamp", 86: "trilinear_clamp",
                         168: "point_mirror", 169: "linear_mirror", 170: "trilinear_mirror",
                         252: "point_mirroronce", 253: "linear_mirroronce", 254: "trilinear_mirroronce"}

BUILTIN = {"_Time", "_SinTime", "_CosTime", "unity_DeltaTime", "_WorldSpaceCameraPos", "_ProjectionParams",
           "_ScreenParams", "_ZBufferParams", "_WorldSpaceLightPos0", "_LightColor0",
           "_LightSplitsNear", "_LightSplitsFar",
           "_LightShadowData", "_LightPositionRange", "_LightProjectionParams",
           "glstate_lightmodel_ambient", "glstate_matrix_transpose_modelview0"}
MATRIX_ALIASES = {"unity_MatrixVP": "UNITY_MATRIX_VP", "unity_MatrixV": "UNITY_MATRIX_V",
                  "unity_MatrixP": "UNITY_MATRIX_P", "glstate_matrix_projection": "UNITY_MATRIX_P"}
BUILTIN_TEXTURES = {"unity_SpecCube0", "unity_SpecCube1", "unity_ProbeVolumeSH", "unity_Lightmap",
                    "unity_LightmapInd", "unity_DynamicLightmap", "unity_DynamicDirectionality",
                    "unity_DynamicNormal", "unity_ShadowMask"}
ENGINE_SHADER_GUIDS = {"0" * 16 + marker + "0" * 15 for marker in "ef"}


def field_components(field):
    """Return each actual original cbuffer scalar and its Unity uniform source."""
    name = MATRIX_ALIASES.get(field["name"], field["name"])
    result = {}
    count = max(1, field["arraySize"])
    for element in range(count):
        source = name + ("[" + str(element) + "]" if field["arraySize"] else "")
        offset = field["byteOffset"] // 4
        if field["byteOffset"] % 4:
            raise ShaderRecoveryError("Original cbuffer scalar is not four-byte aligned.")
        if field["matrix"]:
            for column in range(field["columns"]):
                for row in range(field["rows"]):
                    expression = "transpose(" + source + ")[" + str(column) + "][" + str(row) + "]"
                    result[offset + element * field["columns"] * 4 + column * 4 + row] = expression
        else:
            for component in range(field["columns"]):
                expression = source + ("." + "xyzw"[component] if field["columns"] > 1 else "")
                if field["type"] == 1:
                    expression = "asfloat(" + expression + ")"
                result[offset + element * 4 + component] = expression
    return result


def vector_component(expression, index):
    """Scalarize native vector integer SSA without changing its equations."""
    expression = re.sub(r"(\d+)[uU]\b", r"\1", expression)
    expression = re.sub(r"\b(?:asint|asuint|asfloat|int|uint)\s*\(", "(", expression)
    try: root = ast.parse(expression, mode="eval").body
    except SyntaxError: return None
    def scalar(node):
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and re.fullmatch(r"(?:float|int|uint)[1-4]", node.func.id):
            if len(node.args) == 1: return node.args[0]
            if index < len(node.args): return node.args[index]
            return None
        if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and node.attr and set(node.attr) <= set("xyzw"):
            if len(node.attr) == 1: return node
            if index < len(node.attr): return ast.Attribute(value=node.value, attr=node.attr[index], ctx=ast.Load())
            return None
        if isinstance(node, ast.Constant): return node
        if isinstance(node, ast.Name): return ast.Attribute(value=node, attr="xyzw"[index], ctx=ast.Load())
        if isinstance(node, ast.BinOp):
            left, right = scalar(node.left), scalar(node.right)
            if left is not None and right is not None: return ast.BinOp(left=left, op=node.op, right=right)
        if isinstance(node, ast.UnaryOp):
            value = scalar(node.operand)
            if value is not None: return ast.UnaryOp(op=node.op, operand=value)
        return None
    result = scalar(root)
    return ast.unparse(result) if result is not None else None


def index_residues(expression, prefix, modulus, depth=0, loop_domains=None):
    """Prove dynamic original integer-index residues from actual assignments.

    Unknown data keeps every residue. Multiplication by a native structure
    stride and explicit offsets narrow the domain without assuming an instance
    number, dropping a swizzle or treating unread alignment as shader input.
    """
    unknown = set(range(modulus))
    if depth > 512:
        return unknown
    loop_domains = loop_domains or {}
    expression = re.sub(r"\b0\.0+f\b", "0", expression)
    expression = re.sub(r"(\d+)[uU]\b", r"\1", expression)
    expression = re.sub(r"\b(?:asint|asuint|asfloat|int|uint)\s*\(", "(", expression)
    try:
        tree = ast.parse(expression, mode="eval").body
    except SyntaxError:
        return unknown
    def visit(node):
        if isinstance(node, ast.Constant) and type(node.value) is int:
            return {node.value % modulus}
        if isinstance(node, (ast.Name, ast.Attribute)):
            name = ast.unparse(node)
            if name in loop_domains:
                return {value % modulus for value in loop_domains[name]}
            pattern = re.compile(r"(?m)^\s*(?:(?:float|int|uint)[1-4]?\s+)?" + re.escape(name) + r"\s*=\s*([^;]+);")
            matches = [(m.start(), m[1], m) for m in pattern.finditer(prefix)]
            if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and len(node.attr) == 1 and node.attr in "xyzw":
                whole = re.compile(r"(?m)^\s*(?:(?:float|int|uint)[1-4]?\s+)?" + re.escape(node.value.id) + r"\s*=\s*([^;]+);")
                for match in whole.finditer(prefix):
                    value = vector_component(match[1], "xyzw".index(node.attr))
                    if value is not None:
                        matches.append((match.start(), value, match))
            if not matches:
                return unknown
            _, value, match = max(matches, key=lambda row: row[0])
            return index_residues(value, prefix[:match.start()], modulus, depth + 1, loop_domains)
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
            values = visit(node.operand)
            return values if isinstance(node.op, ast.UAdd) else {(-v) % modulus for v in values}
        if isinstance(node, ast.BinOp):
            left, right = visit(node.left), visit(node.right)
            if isinstance(node.op, ast.Add): return {(a + b) % modulus for a in left for b in right}
            if isinstance(node.op, ast.Sub): return {(a - b) % modulus for a in left for b in right}
            if isinstance(node.op, ast.Mult): return {(a * b) % modulus for a in left for b in right}
            if isinstance(node.op, ast.LShift) and isinstance(node.right, ast.Constant) and type(node.right.value) is int and 0 <= node.right.value < 32:
                return {(a << node.right.value) % modulus for a in left}
        return unknown
    return visit(tree)


def native_loop_domains(body, read_offset):
    """Recognize exact original bounded integer loops, otherwise keep unknown."""
    domains = {}
    for loop in re.finditer(r"for\s*\(\s*;\s*;\s*\)\s*\{", body):
        opening = body.find("{", loop.start())
        nesting, closing = 1, opening + 1
        while closing < len(body) and nesting:
            nesting += (body[closing] == "{") - (body[closing] == "}")
            closing += 1
        if nesting or not opening < read_offset < closing:
            continue
        content = body[opening + 1:closing - 1]
        guard = re.search(r"(\w+\.[xyzw])\s*=\s*asfloat\(\(asint\((\w+\.[xyzw])\)\s*>=\s*(\d+)\)\s*\?\s*4294967295u\s*:\s*0u\);\s*if\s*\(asuint\(\1\)\s*!=\s*0u\)\s*\{\s*break;\s*\}", content)
        if guard is None:
            continue
        counter, limit = guard[2], int(guard[3])
        if not 0 < limit <= 4096:
            continue
        increments = list(re.finditer(re.escape(counter) + r"\s*=\s*asfloat\(asint\(" + re.escape(counter) + r"\)\s*\+\s*1\);", content))
        writes = list(re.finditer(re.escape(counter) + r"\s*=", content))
        initial = list(re.finditer(re.escape(counter) + r"\s*=\s*([^;]+);", body[:loop.start()]))
        if len(increments) != 1 or len(writes) != 1 or not initial or initial[-1][1].strip() not in ("0.0f", "asfloat(0)", "asfloat(0u)"):
            continue
        domains[counter] = set(range(limit))
    return domains


def restore_uniforms(hlsl, interface, input_signature=(), resource_layouts=()):
    """Bind translated original math to recovered Unity material/builtin data.

    Every cbuffer component read by the actual program must have original
    metadata. Only unread padding receives zero; missing used uniforms fail.
    """
    # SPIRV-Cross inserts this Vulkan-to-HLSL adapter, which is not part
    # of the original DXBC interface. Native HLSL SV_InstanceID/SV_VertexID
    # already have the original D3D system-value contract; Vulkan draw-base
    # subtraction must not create an unbound private constant buffer.
    adapter = re.compile(r"cbuffer SPIRV_Cross_VertexInfo\s*\{\s*int SPIRV_Cross_BaseVertex;\s*int SPIRV_Cross_BaseInstance;\s*\};", re.S)
    if adapter.search(hlsl):
        hlsl = adapter.sub("", hlsl)
        hlsl = re.sub(r"\bSPIRV_Cross_Base(?:Vertex|Instance)\b", "0", hlsl)
    buffers = {buffer["name"]: buffer for buffer in interface["buffers"]}
    bindings = {(binding["kind"], binding["slot"]): binding for binding in interface["bindings"]}
    declarations, initializers, observed = {}, [], []
    pattern = re.compile(r"cbuffer \w+\s*:\s*register\(b(\d+)(?:,\s*space0)?\)\s*\{\s*float4 (\w+)\[(\d+)\]\s*:\s*packoffset\(c0\);\s*\};", re.S)
    matches = list(pattern.finditer(hlsl))
    for match in matches:
        variable, size = match[2], int(match[3])
        # vkd3d uses one unified SPIR-V descriptor namespace and SPIRV-Cross
        # exposes those remapped binding numbers. Its debug variable retains
        # the original DXBC cbuffer register (cbN), which identifies the source
        # interface; the generated register(bN) is not original provenance.
        original_slot = re.fullmatch(r"cb(\d+)_(?:\d+_)+m\d+", variable)
        if original_slot is None:
            raise ShaderRecoveryError("Translated cbuffer lost its original register identity.")
        slot = int(original_slot[1])
        binding = bindings.get(("cbuffer", slot))
        if binding is None or binding["name"] not in buffers:
            raise ShaderRecoveryError("Translated cbuffer has no original binding: b" + str(slot))
        buffer = buffers[binding["name"]]
        components = {}
        for field in buffer["fields"]:
            for component, expression in field_components(field).items():
                if component in components and components[component] != expression:
                    raise ShaderRecoveryError("Recovered original cbuffer fields overlap.")
                components[component] = expression
            name = field["name"]
            if (not name.startswith("unity_") or name in {"unity_Projector", "unity_WorldToLight"}) and name not in BUILTIN and name not in MATRIX_ALIASES:
                scalar_type = "int" if field["type"] == 1 else "float"
                shape = (str(field["rows"]) + "x" + str(field["columns"])) if field["matrix"] else (str(field["columns"]) if field["columns"] > 1 else "")
                declaration = scalar_type + shape + " " + name + ("[" + str(field["arraySize"]) + "]" if field["arraySize"] else "") + ";"
                if name in declarations and declarations[name] != declaration:
                    raise ShaderRecoveryError("Original uniform has conflicting types: " + name)
                declarations[name] = declaration
        for structure in buffer.get("structures", []):
            for element in range(max(1, structure["arraySize"])):
                for field in structure["fields"]:
                    nested = {**field, "name": structure["name"] + "[" + str(element) + "]." + field["name"],
                              "byteOffset": structure["byteOffset"] + element * structure["stride"] + field["byteOffset"]}
                    for component, expression in field_components(nested).items():
                        if component in components and components[component] != expression:
                            raise ShaderRecoveryError("Original structured cbuffer fields overlap.")
                        components[component] = expression
            if not structure["name"].startswith("unity_"):
                # Original material instancing arrays are engine-populated
                # Unity buffers; a private uniform copy would lose per-instance
                # material properties. Retain their actual buffer/field names.
                prefix = "UnityInstancing_"
                if not buffer["name"].startswith(prefix) or structure["name"] != buffer["name"][len(prefix):] + "Array":
                    raise ShaderRecoveryError("Unknown original nonbuiltin structured cbuffer.")
                instance_name = buffer["name"][len(prefix):]
                native = ["UNITY_INSTANCING_BUFFER_START(" + instance_name + ")"]
                for field in structure["fields"]:
                    if field["arraySize"]:
                        raise ShaderRecoveryError("Nested original material instancing arrays require explicit Unity binding.")
                    kind = "int" if field["type"] == 1 else "float"
                    shape = (str(field["rows"]) + "x" + str(field["columns"])) if field["matrix"] else (str(field["columns"]) if field["columns"] > 1 else "")
                    native.append("UNITY_DEFINE_INSTANCED_PROP(" + kind + shape + ", " + field["name"] + ")")
                native.append("UNITY_INSTANCING_BUFFER_END(" + instance_name + ")")
                declarations[buffer["name"]] = "\n".join(native)
        body = hlsl[match.end():]
        reads = re.finditer(r"\b" + re.escape(variable) + r"\[([^]]+)\](?:\.([xyzw]+))?", body)
        used = set()
        for read in reads:
            number = re.fullmatch(r"(\d+)[uU]?", read[1])
            if number is None:
                # Dynamic vector indexing preserves its actual read swizzle.
                # Reading x/y in every instance never reads z/w alignment gaps.
                mask = read[2] or "xyzw"
                structures = buffer.get("structures", [])
                strides = {row["stride"] // 16 for row in structures if row["stride"] % 16 == 0}
                residues = None
                loop_domains = native_loop_domains(body, read.start())
                if len(strides) == 1 and len(structures) == 1 and structures[0]["byteOffset"] == 0:
                    stride = next(iter(strides))
                    residues = index_residues(read[1], body[:read.start()], stride, loop_domains=loop_domains)
                elif loop_domains:
                    stride = size
                    residues = index_residues(read[1], body[:read.start()], stride, loop_domains=loop_domains)
                used.update(index * 4 + "xyzw".index(component)
                            for index in range(size) if residues is None or index % stride in residues
                            for component in mask)
            else:
                index = int(number[1])
                used.update(index * 4 + "xyzw".index(component) for component in (read[2] or "xyzw"))
        missing = sorted(used - components.keys())
        if missing:
            raise ShaderRecoveryError("Original metadata cannot explain used cbuffer scalars: " + binding["name"] + " " + repr(missing))
        observed.append({"slot": slot, "originalBuffer": binding["name"], "usedScalars": len(used), "usedScalarIndices": sorted(used), "allUsedScalarsBound": True})
        structures = buffer.get("structures", [])
        if structures:
            if len(structures) != 1 or structures[0]["byteOffset"] or buffer["fields"]:
                raise ShaderRecoveryError("Original instance buffer needs a proven homogeneous structure.")
            structure = structures[0]
            stride = structure["stride"] // 16
            instance_body = hlsl[match.end():]
            def structured_read(read):
                expression, mask = read[1], read[2] or "xyzw"
                residues = index_residues(expression, instance_body[:read.start()], stride,
                                          loop_domains=native_loop_domains(instance_body, read.start()))
                cases = []
                for residue in sorted(residues):
                    values = {}
                    for field in structure["fields"]:
                        dynamic = {**field, "name": structure["name"] + "[((" + expression + ") / " + str(stride) + ")]." + field["name"]}
                        values.update(field_components(dynamic))
                    components_used = [residue * 4 + "xyzw".index(component) for component in mask]
                    if any(index not in values for index in components_used):
                        raise ShaderRecoveryError("Original dynamic instance read reaches unexplained padding.")
                    result = [values[index] for index in components_used]
                    result = result[0] if len(result) == 1 else "float" + str(len(result)) + "(" + ", ".join(result) + ")"
                    cases.append((residue, result))
                value = cases[-1][1]
                for residue, result in reversed(cases[:-1]):
                    value = "(((" + expression + ") % " + str(stride) + ") == " + str(residue) + " ? " + result + " : " + value + ")"
                return "(" + value + ")"
            # Unity's two-element compiler array is a flexible native GPU
            # instance buffer, not a two-object limit. Read its actual runtime
            # element directly instead of copying only elements zero and one.
            hlsl = hlsl[:match.end()] + re.sub(r"\b" + re.escape(variable) + r"\[([^]]+)\](?:\.([xyzw]+))?", structured_read, instance_body)
        else:
            for index in range(size):
                values = [components.get(index * 4 + component, "0.0") for component in range(4)]
                initializers.append(variable + "[" + str(index) + "] = float4(" + ", ".join(values) + ");")
    hlsl = pattern.sub(lambda match: "static float4 " + match[2] + "[" + match[3] + "];", hlsl)
    if re.search(r"\bcbuffer\b", hlsl):
        raise ShaderRecoveryError("Translated shader contains an unsupported original cbuffer declaration.")
    textures = {}
    layouts = {row["slot"]: row for row in resource_layouts}
    typed_buffers = {}
    def typed_buffer(match):
        original_slot = re.fullmatch(r"t(\d+)", match[1])
        slot = int(original_slot[1]) if original_slot else -1
        binding, layout = bindings.get(("buffer", slot)), layouts.get(slot)
        if binding is None or layout is None:
            raise ShaderRecoveryError("Original typed GPU buffer lacks its native register/stride witness.")
        typed_buffers[match[1]] = (binding["name"], layout)
        count = layout["strideBytes"] // 4
        if layout["kind"] == "raw":
            return "ByteAddressBuffer " + binding["name"] + ";"
        if count == 1:
            return "StructuredBuffer<uint> " + binding["name"] + ";"
        structure = "QuestNativeBufferWords" + str(count)
        declarations[structure] = "struct " + structure + " { uint words[" + str(count) + "]; };"
        return "StructuredBuffer<" + structure + "> " + binding["name"] + ";"
    hlsl = re.sub(r"Buffer<uint4>\s+(\w+)\s*:\s*register\(t\d+(?:,\s*space0)?\);", typed_buffer, hlsl)
    for variable, (name, layout) in typed_buffers.items():
        pattern = re.compile(r"\b" + re.escape(variable) + r"\.Load\(")
        while True:
            match = pattern.search(hlsl)
            if match is None:
                break
            depth, end = 1, match.end()
            while depth and end < len(hlsl):
                depth += (hlsl[end] == '(') - (hlsl[end] == ')')
                end += 1
            if depth or hlsl[end:end + 2] != ".x":
                raise ShaderRecoveryError("Original typed buffer has an unsupported non-scalar word read.")
            expression = hlsl[match.end():end - 1]
            count = layout["strideBytes"] // 4
            if layout["kind"] == "raw":
                value = name + ".Load((" + expression + ") * 4u)"
            elif count == 1:
                value = name + "[" + expression + "]"
            else:
                value = name + "[(" + expression + ") / " + str(count) + "u].words[(" + expression + ") % " + str(count) + "u]"
            hlsl = hlsl[:match.start()] + value + hlsl[end + 2:]
    def texture(match):
        original_slot = re.fullmatch(r"t(\d+)", match[2])
        if original_slot is None:
            raise ShaderRecoveryError("Translated texture lost its original DXBC register identity.")
        slot = int(original_slot[1])
        binding = bindings.get(("texture", slot))
        if binding is None:
            raise ShaderRecoveryError("Translated texture has no original resource binding.")
        textures[match[2]] = binding["name"]
        if binding["name"] in BUILTIN_TEXTURES:
            return "// Native Unity include supplies " + binding["name"] + "."
        return match[1] + " " + binding["name"] + ";"
    hlsl = re.sub(r"(Texture\w+(?:<[^>]+>)?)\s+(\w+)\s*:\s*register\(t(\d+)(?:,\s*space0)?\);", texture, hlsl)
    def structured_buffer(match):
        original_slot = re.fullmatch(r"t(\d+)", match[2])
        binding = bindings.get(("buffer", int(original_slot[1]))) if original_slot else None
        if binding is None:
            raise ShaderRecoveryError("Translated GPU buffer lost its original native resource binding.")
        textures[match[2]] = binding["name"]
        return match[1] + " " + binding["name"] + ";"
    hlsl = re.sub(r"((?:StructuredBuffer|ByteAddressBuffer)(?:<[^>]+>)?)\s+(\w+)\s*:\s*register\(t(\d+)(?:,\s*space0)?\);", structured_buffer, hlsl)
    sampler_names = {}
    def sampler(match):
        original_slot = re.fullmatch(r"s(\d+)", match[2])
        if original_slot is None:
            raise ShaderRecoveryError("Translated sampler lost its original DXBC register identity.")
        slot = int(original_slot[1])
        names = {binding["name"] for binding in interface["bindings"] if binding["kind"] == "texture" and binding["samplerSlot"] == slot}
        if not names:
            native = bindings.get(("sampler", slot))
            if native is None or native["nativeState"] not in NATIVE_INLINE_SAMPLERS:
                raise ShaderRecoveryError("Translated sampler has no proven original sampler binding.")
            name = "QuestOriginal_" + NATIVE_INLINE_SAMPLERS[native["nativeState"]] + "_sampler"
            sampler_names[match[2]] = name
            return match[1] + " " + name + ";"
        # Unity associates an independent sampler with its texture by the
        # literal sampler<TextureName> convention, including its underscore.
        if len(names) != 1:
            raise ShaderRecoveryError("Original sampler has ambiguous texture ownership.")
        source_name = next(iter(names))
        name = "sampler" + source_name
        sampler_names[match[2]] = name
        if source_name in BUILTIN_TEXTURES:
            return "// Native Unity include supplies " + name + "."
        return match[1] + " " + name + ";"
    hlsl = re.sub(r"(Sampler(?:Comparison)?State)\s+(\w+)\s*:\s*register\(s(\d+)(?:,\s*space0)?\);", sampler, hlsl)
    if re.search(r":\s*register\s*\(", hlsl):
        raise ShaderRecoveryError("Translated program retains an unbound original resource register.")
    for original, restored in {**textures, **sampler_names}.items():
        hlsl = re.sub(r"\b" + re.escape(original) + r"\b", restored, hlsl)
    for field in input_signature:
        if field["systemValue"] == 0:
            register = field["register"]
            semantic = field["semantic"] + str(field["semanticIndex"])
            hlsl = re.sub(r"(\bv" + str(register) + r"\s*:\s*)TEXCOORD\d+", lambda match: match[1] + semantic, hlsl)
    entry = re.search(r"(void \w+_main\(\)\s*\{)", hlsl)
    if entry is None:
        raise ShaderRecoveryError("Translated program lacks its original entry function.")
    hlsl = hlsl[:entry.end()] + "\n    " + "\n    ".join(initializers) + hlsl[entry.end():]
    return "\n".join(declarations.values()) + "\n" + hlsl, observed


def original_programs(shader, unitypy=None):
    """Read actual D3D11 program blocks and retain every pass/keyword alias."""
    if unitypy is None:
        import UnityPy as unitypy
    from UnityPy.helpers import CompressionHelper
    from UnityPy.export.ShaderConverter import ShaderSubProgram
    from UnityPy.streams import EndianBinaryReader
    import attrs
    matches = [index for index, platform in enumerate(shader.platforms) if int(platform) == 4]
    if len(matches) != 1:
        raise ShaderRecoveryError("Original shader needs exactly one native D3D11 program bank.")
    platform = matches[0]
    compressed = bytes(shader.compressedBlob)
    offsets, lengths, sizes = shader.offsets[platform], shader.compressedLengths[platform], shader.decompressedLengths[platform]
    if not isinstance(offsets, list):
        offsets, lengths, sizes = [offsets], [lengths], [sizes]
    if len(offsets) != len(lengths) or len(offsets) != len(sizes):
        raise ShaderRecoveryError("Original shader compressed segment counts disagree.")
    segments = []
    for offset, length, size in zip(offsets, lengths, sizes):
        if offset < 0 or length <= 0 or offset + length > len(compressed):
            raise ShaderRecoveryError("Original shader segment lies outside its native program bank.")
        segments.append(CompressionHelper.decompress_lz4(compressed[offset:offset + length], size))
    directory = segments[0]
    count = struct.unpack_from("<i", directory)[0]
    if count < 0 or 4 + count * 12 > len(directory):
        raise ShaderRecoveryError("Malformed original shader program directory.")
    table = [struct.unpack_from("<3i", directory, 4 + index * 12) for index in range(count)]
    cache, records = {}, []
    for subshader_index, subshader in enumerate(shader.m_ParsedForm.m_SubShaders):
        for pass_index, shader_pass in enumerate(subshader.m_Passes):
            for stage, attribute in (("vertex", "progVertex"), ("fragment", "progFragment"),
                                     ("geometry", "progGeometry"), ("hull", "progHull"), ("domain", "progDomain")):
                owner = getattr(shader_pass, attribute, None)
                if owner is None:
                    continue
                for variant in owner.m_SubPrograms:
                    # Original desktop variants can have several hardware-tier
                    # aliases for one actual bytecode block. Keep those aliases
                    # but decode each original block only once.
                    index = variant.m_BlobIndex
                    if index not in cache:
                        if not 0 <= index < len(table):
                            raise ShaderRecoveryError("Original pass points outside its native program directory.")
                        offset, length, segment = table[index]
                        if not 0 <= segment < len(segments) or offset < 0 or length <= 0 or offset + length > len(segments[segment]):
                            raise ShaderRecoveryError("Original shader program range is invalid.")
                        raw = segments[segment][offset:offset + length]
                        reader = EndianBinaryReader(raw, endian="<")
                        program = ShaderSubProgram(reader)
                        dxbc, chunks = dxbc_container(program.m_ProgramCode)
                        delta = parameter_delta(raw, reader.Position)
                        cache[index] = {"raw": raw, "dxbc": dxbc, "delta": delta,
                                        "dxbcSha256": hashlib.sha256(dxbc).hexdigest(),
                                        "programVersion": program.m_Version, "programType": int(program.m_ProgramType)}
                    actual = cache[index]
                    common = attrs.asdict(owner.m_CommonParameters) if owner.m_CommonParameters is not None else {}
                    interface = merge_interface(common, shader_pass.m_NameIndices, actual["delta"])
                    keywords = [shader.m_ParsedForm.m_KeywordNames[value] for value in (variant.m_KeywordIndices or [])]
                    records.append({"subshader": subshader_index, "pass": pass_index, "stage": stage,
                                    "blobIndex": index, "hardwareTier": variant.m_ShaderHardwareTier,
                                    "keywords": sorted(keywords), "interface": interface,
                                    "originalDxbcSha256": actual["dxbcSha256"], "raw": actual["raw"], "dxbc": actual["dxbc"],
                                    "programVersion": actual["programVersion"], "programType": actual["programType"]})
    return records


def _json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, sort_keys=True, indent=2) + "\n", encoding="utf-8")


def _hash(path):
    digest = hashlib.sha256()
    with Path(path).open("rb") as source:
        for chunk in iter(lambda: source.read(4 * 1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _objects(identities):
    if isinstance(identities, (str, Path)):
        value = json.loads(Path(identities).read_text())
        identities = value.get("identities", value) if isinstance(value, dict) else value
    objects = {}
    for row in identities:
        for obj in row["objects"]:
            key = (obj["collection"].casefold(), int(obj["pathId"]))
            item = {**obj, "guid": row["guid"], "path": row["path"]}
            if key in objects and objects[key] != item:
                raise ShaderRecoveryError("Conflicting original object export identities.")
            objects[key] = item
    return objects


def native_assets(game_data, identities, cab_bundles, unitypy=None, classes=(48,)):
    """Yield exact original native objects selected by captured export identities."""
    if unitypy is None:
        import UnityPy as unitypy
    game_data = Path(game_data).resolve()
    if isinstance(cab_bundles, (str, Path)):
        cab_bundles = json.loads(Path(cab_bundles).read_text())
    owners = {key.casefold(): value for key, value in cab_bundles.items()}
    groups = collections.defaultdict(dict)
    for key, obj in _objects(identities).items():
        if obj["classId"] not in classes:
            continue
        relative = owners.get(key[0], obj["collection"])
        source = (game_data / relative).resolve()
        if game_data not in source.parents or not source.is_file():
            raise ShaderRecoveryError("Original shader container is absent/outside owned game: " + relative)
        groups[relative][key] = obj
    for relative, targets in sorted(groups.items()):
        environment = unitypy.load(str(game_data / relative))
        found = set()
        for original in environment.objects:
            key = (original.assets_file.name.casefold(), int(original.path_id))
            if key not in targets:
                continue
            if int(original.type) != targets[key]["classId"]:
                raise ShaderRecoveryError("Captured original pathID changed native type.")
            found.add(key)
            yield targets[key], original, relative
        if found != targets.keys():
            raise ShaderRecoveryError("Original shader identities are missing from their actual CAB: " + repr(sorted(targets.keys() - found)))


def inventory(project, game_data, identities, cab_bundles, cache, *, unitypy=None,
              bind_programs=False, vkd3d="vkd3d-compiler", spirv_cross="spirv-cross"):
    """Inventory every original pass/stage and optionally translate every bank.

    Shader instruction/interface failures remain explicit entries; this report
    never marks a placeholder or an uncompiled bank ready for an Android player.
    """
    import attrs
    project, cache = Path(project).resolve(), Path(cache).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    import inspect
    binder_source = "\n".join(inspect.getsource(function) for function in
                              (field_components, vector_component, index_residues, native_loop_domains, restore_uniforms))
    binder_source += repr((sorted(BUILTIN), sorted(MATRIX_ALIASES.items()), sorted(BUILTIN_TEXTURES), sorted(NATIVE_INLINE_SAMPLERS.items())))
    binder_sha256 = hashlib.sha256(binder_source.encode()).hexdigest()
    bound_cache = {}
    shaders, errors, unique_programs, total_aliases = [], [], set(), 0
    for obj, original, source_container in native_assets(game_data, identities, cab_bundles, unitypy):
        shader = original.read()
        form = attrs.asdict(shader.m_ParsedForm)
        record = {"guid": obj["guid"], "assetPath": obj["path"], "originalName": shader.m_ParsedForm.m_Name,
                  "originalSerializedFile": obj["collection"], "originalPathId": obj["pathId"],
                  "originalSourceContainer": source_container,
                  "originalObjectSha256": hashlib.sha256(original.get_raw_data()).hexdigest(),
                  "originalParsedFormSha256": hashlib.sha256(json.dumps(form, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
                  "sourceSha256": _hash(project / obj["path"]), "originalDxbcSha256": [], "variants": [],
                  "nativePasses": [], "status": "original-placeholder-not-restored",
                  "androidShaderCompiled": False, "pixelParityVerified": False}
        for si, subshader in enumerate(form["m_SubShaders"]):
            for pi, shader_pass in enumerate(subshader["m_Passes"]):
                record["nativePasses"].append({"subshader": si, "pass": pi, "type": shader_pass["m_Type"],
                                             "state": shader_pass["m_State"], "tags": shader_pass["m_Tags"],
                                             "useName": shader_pass["m_UseName"], "textureName": shader_pass["m_TextureName"]})
        try:
            programs = original_programs(shader, unitypy)
            total_aliases += len(programs)
            _json(cache / "forms" / (record["originalParsedFormSha256"] + ".json"), form)
            for program in programs:
                unique_programs.add(program["originalDxbcSha256"])
                _, chunks = dxbc_container(program["dxbc"])
                outputs = signature(chunks[b"OSGN"]) if b"OSGN" in chunks else []
                variant = {key: value for key, value in program.items() if key not in ("raw", "dxbc", "interface")}
                variant["fragmentOutput"] = "color" if any(row["systemValue"] == 64 or row["semantic"].upper() == "SV_TARGET" for row in outputs) else \
                    "depth" if any(row["systemValue"] in (65, 67, 68) or row["semantic"].upper().startswith("SV_DEPTH") for row in outputs) else "none"
                variant["originalInputSignature"] = signature(chunks[b"ISGN"]) if b"ISGN" in chunks else []
                variant["originalOutputSignature"] = outputs
                interface_key = hashlib.sha256(json.dumps(program["interface"], sort_keys=True, separators=(",", ":")).encode()).hexdigest()
                interface_path = cache / "interfaces" / (interface_key + ".json")
                if not interface_path.exists():
                    _json(interface_path, program["interface"])
                variant["originalInterfaceSha256"] = interface_key
                if bind_programs:
                    bound_path = cache / "bound" / (program["originalDxbcSha256"] + "-" + interface_key + ".hlsl")
                    proof_path = bound_path.with_suffix(".json")
                    proof_key = (program["originalDxbcSha256"], interface_key)
                    if proof_key in bound_cache:
                        proof = bound_cache[proof_key]
                    elif bound_path.exists() and proof_path.exists():
                        proof = json.loads(proof_path.read_text())
                        if _hash(bound_path) != proof["boundHlslSha256"]:
                            raise ShaderRecoveryError("Recovered shader bank cache changed.")
                        if (proof.get("originalDxbcSha256") != program["originalDxbcSha256"] or
                            proof.get("originalInterfaceSha256") != interface_key or not proof.get("unityUniformsRestored")):
                            raise ShaderRecoveryError("Recovered shader cache changes its native instruction/interface identity.")
                    else:
                        proof = None
                    if proof is None or proof.get("binderSha256") != binder_sha256:
                        previous = proof
                        # Rebind a previously witnessed instruction translation
                        # without rerunning external tools when its bytes match.
                        # Every native interface and binder change is reapplied.
                        translated_path = cache / "translated" / (program["originalDxbcSha256"] + ".hlsl")
                        if previous and translated_path.is_file() and _hash(translated_path) == previous["translatedHlslSha256"]:
                            translated = {key: value for key, value in previous.items() if key not in
                                          ("binderSha256", "originalInterfaceSha256", "boundHlslSha256", "usedOriginalBuffers")}
                            translated["hlslPath"] = str(translated_path)
                        else:
                            translated = translate(program["dxbc"], cache / "translated", vkd3d, spirv_cross)
                        translated["originalBufferLayouts"] = native_buffer_layouts(program["dxbc"])
                        bound, used = restore_uniforms(Path(translated["hlslPath"]).read_text(), program["interface"], translated["inputSignature"], translated["originalBufferLayouts"])
                        bound_path.parent.mkdir(parents=True, exist_ok=True)
                        bound_path.write_text(bound)
                        proof = {**translated, "originalInterfaceSha256": interface_key,
                                 "boundHlslSha256": _hash(bound_path), "usedOriginalBuffers": used,
                                 "unityUniformsRestored": True, "binderSha256": binder_sha256}
                        _json(proof_path, proof)
                    bound_cache[proof_key] = proof
                    variant["boundHlslPath"] = str(bound_path)
                    variant["boundHlslSha256"] = proof["boundHlslSha256"]
                    variant["outputInterfaceAdapters"] = proof.get("outputInterfaceAdapters", [])
                record["variants"].append(variant)
            record["originalDxbcSha256"] = sorted({row["originalDxbcSha256"] for row in programs})
            record["allOriginalInstructionsExtracted"] = True
            record["allOriginalInterfacesBound"] = bool(bind_programs)
        except (ShaderRecoveryError, ValueError, KeyError, TypeError, IndexError, struct.error) as error:
            record["status"] = "original-instruction-recovery-blocked"
            record["recoveryError"] = str(error)
            errors.append({"guid": obj["guid"], "name": record["originalName"], "assetPath": obj["path"], "error": str(error)})
        shaders.append(record)
        _json(cache / "progress.json", {"schema": 1, "shaderCount": len(shaders), "blockedShaderCount": len(errors),
                                       "uniqueOriginalProgramCount": len(unique_programs), "originalProgramAliasCount": total_aliases})
    _json(cache / "original-shader-stages.json", {"schema": 1, "shaders": shaders, "errors": errors,
                                                 "uniqueOriginalProgramCount": len(unique_programs),
                                                 "originalProgramAliasCount": total_aliases})
    materials, binary_materials = [], []
    object_index = _objects(identities)
    for obj in object_index.values():
        if obj["classId"] != 21:
            continue
        try:
            text = (project / obj["path"]).read_text()
        except UnicodeDecodeError:
            binary_materials.append(obj)
            continue
        shader = re.search(r"^  m_Shader: \{fileID: (-?\d+), guid: ([0-9a-f]{32}), type: \d+\}$", text, re.M)
        if shader is None:
            if re.search(r"^  m_Shader: \{fileID: 0\}$", text, re.M):
                binary_materials.append(obj)
                continue
            raise ShaderRecoveryError("Original material has no actual shader PPtr: " + obj["path"])
        builtin = shader[2] in ENGINE_SHADER_GUIDS
        materials.append({"guid": obj["guid"], "assetPath": obj["path"], "shaderGuid": shader[2],
                          **({"originalEngineBuiltinShader": True, "shaderFileId": int(shader[1])} if builtin else {}),
                          "originalSerializedFile": obj["collection"], "originalPathId": obj["pathId"]})
    if binary_materials:
        rows = [{"guid": obj["guid"], "path": obj["path"], "objects": [obj]} for obj in binary_materials]
        for obj, original, _ in native_assets(game_data, rows, cab_bundles, unitypy, (21,)):
            pointer = original.read().m_Shader
            if not pointer.m_PathID:
                materials.append({"guid": obj["guid"], "assetPath": obj["path"], "shaderGuid": None,
                                  "originalShaderNull": True, "originalSerializedFile": obj["collection"],
                                  "originalPathId": obj["pathId"]})
                continue
            collection = original.assets_file.name
            if pointer.m_FileID:
                external = original.assets_file.externals[pointer.m_FileID - 1].path.replace("\\", "/")
                collection = external.rsplit("/", 1)[-1]
            target = object_index.get((collection.casefold(), int(pointer.m_PathID)))
            if target is None and collection.casefold() == "unity default resources" and pointer.m_PathID == 10101:
                # Native Font importers use Unity's fixed engine text shader;
                # its actual original external pointer is not a game asset.
                materials.append({"guid": obj["guid"], "assetPath": obj["path"],
                                  "shaderGuid": "0" * 16 + "e" + "0" * 15, "shaderFileId": 10101,
                                  "originalEngineBuiltinShader": True, "nativeFontImporterSubObject": True,
                                  "originalSerializedFile": obj["collection"], "originalPathId": obj["pathId"]})
                continue
            if target is None or target["classId"] != 48:
                raise ShaderRecoveryError("Native font material lost its original shader identity: " + obj["path"] + " " + collection + ":" + str(pointer.m_PathID))
            materials.append({"guid": obj["guid"], "assetPath": obj["path"], "shaderGuid": target["guid"],
                              "nativeFontImporterSubObject": True,
                              "originalSerializedFile": obj["collection"], "originalPathId": obj["pathId"]})
    report = {"schema": 1, "scope": "campaign", "shaders": shaders, "materials": materials, "renderCases": [],
              "shaderCount": len(shaders), "materialCount": len(materials), "uniqueOriginalProgramCount": len(unique_programs),
              "originalProgramAliasCount": total_aliases, "blockedShaderCount": len(errors), "errors": errors,
              "allPlaceholdersRestored": False, "androidShaderCompiled": False, "pixelParityVerified": False}
    _json(cache / "original-shader-inventory.json", report)
    return report


def native_stage_interface(hlsl, signatures, direction):
    """Restore exact native stage semantics, including packed register fields.

    DXBC interpolators link by semantic rather than register number. One native
    register may pack several semantics. SPIRV-Cross exposes that whole register
    as one field; split/reassemble only its actual translated components.
    """
    structure_name = "SPIRV_Cross_" + ("Input" if direction == "input" else "Output")
    structure = re.search(r"struct " + structure_name + r"\s*\{(?P<body>.*?)\};", hlsl, re.S)
    if structure is None:
        return hlsl
    symbol = "v" if direction == "input" else "o"
    by_register = collections.defaultdict(list)
    for row in signatures:
        if not row["systemValue"] and row["semantic"].upper() not in {"SV_TARGET", "SV_DEPTH", "SV_DEPTHGREATEREQUAL", "SV_DEPTHLESSEQUAL"}:
            by_register[row["register"]].append(row)
    transfers = {}
    def declaration(match):
        qualifier, source_kind, width_text, variable, emitted_semantic = match.groups()
        numbered = re.fullmatch(symbol + r"(\d+)", variable)
        generic_location = re.fullmatch(r"TEXCOORD(\d+)", emitted_semantic, re.I)
        if numbered:
            slot = int(numbered[1])
        elif generic_location:
            # SPIRV-Cross separates a mixed native register/system value into
            # an anonymous field. Its unchanged generic TEXCOORD location is
            # the original vkd3d register location, not a material-name guess.
            slot = int(generic_location[1])
        elif emitted_semantic.upper().startswith("SV_"):
            return match[0]
        else:
            raise ShaderRecoveryError("Translated anonymous stage field lacks native register identity: " + variable)
        # Original Unity also uses SV-prefixed names as ordinary user varyings
        # (systemValue=0), notably the object instance transported to fragments.
        # Preserve actual engine system inputs/outputs, not a spelling guess.
        if emitted_semantic.upper().startswith("SV_") and not by_register.get(slot):
            return match[0]
        width = int(width_text or 1)
        rows = by_register.get(slot, [])
        if not rows:
            if any(row["register"] == slot and row["semantic"].upper() == "SV_RENDERTARGETARRAYINDEX" for row in signatures):
                return match[0]  # Explicit portable native-layer adapter.
            raise ShaderRecoveryError("Translated stage field lacks original semantic identity: " + variable)
        occupied, declarations, values = set(), [], {}
        for row in rows:
            components = [index for index in range(width) if row["mask"] & (1 << index)]
            if not components:
                # Native signatures can declare unwritten/unread packed fields;
                # the actual instruction bank has already omitted those lanes.
                continue
            if occupied & set(components):
                raise ShaderRecoveryError("Original packed stage semantics overlap.")
            occupied.update(components)
            kind = {1: "uint", 2: "int", 3: "float"}.get(row["componentType"])
            if kind is None:
                raise ShaderRecoveryError("Original stage semantic has an unknown component type.")
            semantic = row["semantic"] + str(row["semanticIndex"])
            name = "questNative_" + semantic
            shape = kind + (str(len(components)) if len(components) > 1 else "")
            # Integer user varyings retain flat interpolation on both stages.
            # An unqualified vertex integer can be packed beside a floating
            # varying by the D3D front-end, causing HLSLcc to mark that whole
            # output register flat while the native fragment remains smooth.
            declaration_qualifier = qualifier
            if kind in ("int", "uint") and not re.search(r"\bnointerpolation\b", declaration_qualifier):
                declaration_qualifier = "nointerpolation " + declaration_qualifier
            declarations.append(declaration_qualifier + shape + " " + name + " : " + semantic + ";")
            for index, component in enumerate(components):
                value = "stage_input." + name + ("." + "xyzw"[index] if len(components) > 1 else "")
                if kind != source_kind:
                    value = {"float": "asfloat", "int": "asint", "uint": "asuint"}[source_kind] + "(" + value + ")"
                values[component] = value
            source = variable + ("." + "".join("xyzw"[component] for component in components) if width > 1 else "")
            if kind != source_kind:
                source = {"float": "asfloat", "int": "asint", "uint": "asuint"}[kind] + "(" + source + ")"
            transfers.setdefault(variable, []).append("stage_output." + name + " = " + source + ";")
        if not occupied:
            raise ShaderRecoveryError("Native instruction field has no witnessed semantic components.")
        if direction == "input":
            components = [values.get(index, "0") for index in range(width)]
            value = components[0] if width == 1 else source_kind + str(width) + "(" + ", ".join(components) + ")"
            transfers[variable] = variable + " = " + value + ";"
        return "\n    ".join(declarations)
    pattern = re.compile(r"((?:(?:nointerpolation|noperspective|centroid|sample|linear)\s+)*)(float|int|uint)([1-4]?)\s+(\w+)\s*:\s*(\w+)\s*;")
    body = pattern.sub(declaration, structure["body"])
    hlsl = hlsl[:structure.start("body")] + body + hlsl[structure.end("body"):]
    for variable, statements in transfers.items():
        if direction == "input":
            pattern = r"\b" + re.escape(variable) + r"\s*=\s*stage_input\." + re.escape(variable) + r"\s*;"
            replacement = statements
        else:
            pattern = r"\bstage_output\." + re.escape(variable) + r"\s*=\s*" + re.escape(variable) + r"\s*;"
            replacement = "\n    ".join(statements)
        hlsl, count = re.subn(pattern, lambda match: replacement, hlsl)
        if count != 1:
            raise ShaderRecoveryError("Native packed interface transfer is missing or ambiguous: " + variable)
    return hlsl


def portable_sampling_interface(hlsl):
    """Apply the engine's exact cube-shadow platform sampling convention.

    Unity2021.3.5 HLSLSupport.cginc UNITY_SAMPLE_TEXCUBE_SHADOW uses
    SampleCmp on GL/GLES/Vulkan/Switch, because GLSL has no explicit-LOD
    cube comparison overload. Desktop DXBC retains SampleCmpLevelZero.
    Preserve the witnessed texture and comparison sampler, changing only
    that same engine platform choice; 2D comparison instructions are intact.
    """
    cubes = set(re.findall(r"\bTextureCube(?:_[A-Za-z]+)?\s*(?:<[^>]+>)?\s+(\w+)\s*;", hlsl))
    adapters = []
    for texture in sorted(cubes):
        pattern = r"\b" + re.escape(texture) + r"\.SampleCmpLevelZero\s*\("
        hlsl, count = re.subn(pattern, texture + ".QUEST_NATIVE_CUBE_SHADOW_COMPARE(", hlsl)
        if count:
            adapters.append({"kind": "unity-native-cube-shadow-platform-sampling", "texture": texture,
                             "originalOperation": "SampleCmpLevelZero", "portableOperation": "SampleCmp",
                             "instructionCount": count,
                             "source": "Unity2021.3.5f1/CGIncludes/HLSLSupport.cginc:UNITY_SAMPLE_TEXCUBE_SHADOW"})
    if adapters:
        hlsl = ("#if defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_VULKAN) || defined(SHADER_API_SWITCH)\n"
                "#define QUEST_NATIVE_CUBE_SHADOW_COMPARE SampleCmp\n#else\n"
                "#define QUEST_NATIVE_CUBE_SHADOW_COMPARE SampleCmpLevelZero\n#endif\n" + hlsl)
    return hlsl, adapters


def stereo_wrapper(hlsl, stage, output_adapters=(), input_signature=(), output_signature=(), graphics_api="GLES3"):
    if graphics_api not in ("GLES3", "Vulkan"):
        raise ShaderRecoveryError("Unsupported native graphics interface backend.")
    if graphics_api == "Vulkan":
        for adapter in output_adapters:
            if adapter["kind"] != "native-vertex-layer-to-unity-framebuffer" or not re.fullmatch(r"o\d+", adapter["nativeOutput"]):
                raise ShaderRecoveryError("Unproven original Vulkan output-interface adapter.")
            name = re.escape(adapter["nativeOutput"])
            native_outputs = [row for row in output_signature if row["semantic"].upper() == "SV_RENDERTARGETARRAYINDEX" and
                              row["register"] == int(adapter["nativeOutput"][1:])]
            if len(native_outputs) != 1 or native_outputs[0]["componentType"] != 1 or native_outputs[0]["mask"] != 1:
                raise ShaderRecoveryError("Original Vulkan layer lacks its exact native scalar uint signature.")
            pattern = r"\b(?P<type>uint|int)\s+" + name + r"\s*:\s*TEXCOORD" + str(adapter["portableLocation"]) + r";"
            matches = list(re.finditer(pattern, hlsl))
            hlsl, count = re.subn(pattern, "uint " + adapter["nativeOutput"] + " : SV_RenderTargetArrayIndex;", hlsl)
            if count != 1:
                raise ShaderRecoveryError("Original Vulkan layer cannot recover its witnessed native output.")
            if matches[0]["type"] == "int":
                # SPIRV-Cross represents the Vulkan Layer builtin as int, while
                # its original DXBC OSGN and FXC SV_RenderTargetArrayIndex require
                # uint. Retain every original signed internal instruction and
                # restore only the exact interface bits at the output boundary.
                assignment = r"stage_output\." + name + r"\s*=\s*" + name + r";"
                hlsl, count = re.subn(assignment, "stage_output." + adapter["nativeOutput"] + " = asuint(" + adapter["nativeOutput"] + ");", hlsl)
                if count != 1:
                    raise ShaderRecoveryError("Original signed Layer carrier lost its exact uint output assignment.")
    if input_signature:
        hlsl = native_stage_interface(hlsl, input_signature, "input")
    if output_signature:
        hlsl = native_stage_interface(hlsl, output_signature, "output")
    if stage == "fragment" and input_signature:
        # SPIRV-Cross omits declarations that the original input signature keeps
        # (including unused SV_Position). The Unity front-end's stage packing
        # must retain those exact native declarations/register order, otherwise
        # HLSLcc associates a neighbouring integer's flat interpolation with a
        # floating output. Do not invent a read of any absent native input.
        pattern = re.compile(r"struct SPIRV_Cross_Input\s*\{(?P<body>.*?)\};", re.S)
        structure = pattern.search(hlsl)
        if structure:
            field_pattern = re.compile(r"(?:(?:nointerpolation|noperspective|centroid|sample|linear)\s+)*(?:float|int|uint|bool)[1-4]?\s+\w+\s*:\s*(\w+)\s*;")
            def semantic_key(value):
                match = re.fullmatch(r"(.*?)(\d*)", value.upper())
                return match[1], int(match[2] or 0)
            fields = {semantic_key(field[1]): field[0] for field in field_pattern.finditer(structure['body'])}
            rows = []
            for signature in input_signature:
                key = (signature['semantic'].upper(), signature['semanticIndex'])
                if key in fields:
                    rows.append(fields.pop(key)); continue
                if signature.get('readWriteMask', 0) & signature['mask']:
                    raise ShaderRecoveryError('Original read input is absent from the translated instruction interface: ' + str(key))
                kind = {1: 'uint', 2: 'int', 3: 'float'}[signature['componentType']]
                width = signature['mask'].bit_count()
                semantic = signature['semantic'] + str(signature['semanticIndex'])
                rows.append(('nointerpolation ' if kind in ('int', 'uint') else '') + kind + (str(width) if width > 1 else '') + ' questNativeUnused_' + semantic + ' : ' + semantic + ';')
            if fields:
                raise ShaderRecoveryError('Translated fragment input lacks native signature identity.')
            hlsl = hlsl[:structure.start('body')] + '\n    ' + '\n    '.join(rows) + '\n' + hlsl[structure.end('body'):]
    """Route both native stereo eyes before original uniform reconstruction."""
    if stage not in ("vertex", "fragment"):
        raise ShaderRecoveryError("Quest stereo wrapper only accepts native vertex/fragment programs.")
    entry = "QuestOriginalVertex" if stage == "vertex" else "QuestOriginalFragment"
    main = re.search(r"(?P<return>\w+) main\((?P<input>SPIRV_Cross_Input stage_input)?\)\s*\{", hlsl)
    if main is None:
        raise ShaderRecoveryError("Original translated stage has an unsupported native entry signature.")
    if not main["input"]:
        hlsl = hlsl[:main.start()] + "struct SPIRV_Cross_Input {\n};\n\n" + hlsl[main.start():].replace("main()", "main(SPIRV_Cross_Input stage_input)", 1)
        main = re.search(r"(?P<return>\w+) main\(SPIRV_Cross_Input stage_input\)\s*\{", hlsl)
    hlsl = hlsl[:main.start()] + hlsl[main.start():].replace(" main(", " " + entry + "(", 1)
    def add_fields(structure, addition):
        nonlocal hlsl
        pattern = r"(struct " + structure + r"\s*\{)(.*?)(\n\};)"
        matches = list(re.finditer(pattern, hlsl, re.S))
        if len(matches) != 1:
            raise ShaderRecoveryError("Native stage lost its original IO structure: " + structure)
        actual = matches[0]
        fields = actual[2]
        if stage == "vertex" and structure == "SPIRV_Cross_Input":
            fields = re.sub(r"\s*uint \w+\s*:\s*SV_InstanceID;", "", fields)
        hlsl = hlsl[:actual.start()] + actual[1] + fields + "\n    " + addition + actual[3] + hlsl[actual.end():]
    if stage == "vertex":
        add_fields("SPIRV_Cross_Input", "UNITY_VERTEX_INPUT_INSTANCE_ID")
        add_fields("SPIRV_Cross_Output", "UNITY_VERTEX_OUTPUT_STEREO_EYE_INDEX" if graphics_api == "Vulkan" and output_adapters else "UNITY_VERTEX_OUTPUT_STEREO")
        # Unity derives the eye and the true object instance from the native
        # SV_InstanceID; the original desktop instruction stream expects only
        # the object instance, including the native base-instance offset.
        hlsl = re.sub(r"gl_InstanceIndex = (?:int\()?stage_input\.\w+\)?;",
                      "#if defined(QUEST_NATIVE_STEREO_INSTANCE_ID)\n    gl_InstanceIndex = int((unity_InstanceID - unity_BaseInstanceID) * 2 + unity_StereoEyeIndex);\n#else\n    gl_InstanceIndex = int(unity_InstanceID - unity_BaseInstanceID);\n#endif", hlsl)
        hlsl = re.sub(r"(" + entry + r"\(SPIRV_Cross_Input stage_input\)\s*\{)",
                      r"\1\n    UNITY_SETUP_INSTANCE_ID(stage_input);", hlsl)
        layer = []
        for adapter in ([] if graphics_api == "Vulkan" else output_adapters):
            if adapter["kind"] != "native-vertex-layer-to-unity-framebuffer" or not re.fullmatch(r"o\d+", adapter["nativeOutput"]):
                raise ShaderRecoveryError("Unproven original output-interface adapter.")
            layer += ["#if defined(UNITY_STEREO_INSTANCING_ENABLED)",
                      "    stage_output.stereoTargetEyeIndexAsRTArrayIdx = stage_output." + adapter["nativeOutput"] + ";", "#endif"]
        hlsl = hlsl.replace("return stage_output;", ("UNITY_INITIALIZE_OUTPUT_STEREO_EYE_INDEX(stage_output);" if graphics_api == "Vulkan" and output_adapters else "UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(stage_output);") + "\n" + "\n".join(layer) + "\n    return stage_output;")
    else:
        add_fields("SPIRV_Cross_Input", "UNITY_VERTEX_OUTPUT_STEREO")
        hlsl = re.sub(r"(" + entry + r"\(SPIRV_Cross_Input stage_input\)\s*\{)",
                      r"\1\n    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(stage_input);", hlsl)
    return hlsl


COMPARE = {0: "Off", 1: "Never", 2: "Less", 3: "Equal", 4: "LEqual", 5: "Greater", 6: "NotEqual", 7: "GEqual", 8: "Always"}
BLEND = {0: "Zero", 1: "One", 2: "DstColor", 3: "SrcColor", 4: "OneMinusDstColor", 5: "SrcAlpha",
         6: "OneMinusSrcColor", 7: "DstAlpha", 8: "OneMinusDstAlpha", 9: "SrcAlphaSaturate", 10: "OneMinusSrcAlpha"}
BLEND_OP = {0: "Add", 1: "Sub", 2: "RevSub", 3: "Min", 4: "Max"}
STENCIL_OP = {0: "Keep", 1: "Zero", 2: "Replace", 3: "IncrSat", 4: "DecrSat", 5: "Invert", 6: "IncrWrap", 7: "DecrWrap"}


def state_value(field, mapping=None):
    name = field["name"]
    if name and name != "<noninit>":
        if not re.fullmatch(r"[A-Za-z_]\w*", name):
            raise ShaderRecoveryError("Original render state contains an invalid property binding.")
        return "[" + name + "]"
    value = field["val"]
    if mapping is not None:
        if value != int(value) or int(value) not in mapping:
            raise ShaderRecoveryError("Unsupported original native render-state enum: " + str(value))
        return mapping[int(value)]
    return format(value, ".9g")


def tags(value):
    return "Tags { " + " ".join(json.dumps(key) + "=" + json.dumps(item) for key, item in value.get("tags", [])) + " }"


def render_state(state):
    """Retain original dynamic and constant blend/depth/cull/stencil states."""
    rows = []
    if state.get("m_Name"):
        rows.append("Name " + json.dumps(state["m_Name"]))
    if state.get("m_LOD"):
        rows.append("LOD " + str(state["m_LOD"]))
    rows += [tags(state["m_Tags"]), "Cull " + state_value(state["culling"], {0: "Off", 1: "Front", 2: "Back"}),
             "ZTest " + state_value(state["zTest"], COMPARE), "ZWrite " + state_value(state["zWrite"], {0: "Off", 1: "On"}),
             "AlphaToMask " + state_value(state["alphaToMask"], {0: "Off", 1: "On"}),
             "Offset " + state_value(state["offsetFactor"]) + ", " + state_value(state["offsetUnits"]),
             "Lighting " + ("On" if state["lighting"] else "Off")]
    for field, command in (("zClip", "ZClip"), ("conservative", "Conservative")):
        if state.get(field) is not None:
            rows.append(command + " " + state_value(state[field], {0: "Off", 1: "On"}))
    count = 8 if state["rtSeparateBlend"] else 1
    for index in range(count):
        blend = state["rtBlend" + str(index)]
        target = " " + str(index) if state["rtSeparateBlend"] else ""
        rows.append("Blend" + target + " " + state_value(blend["srcBlend"], BLEND) + " " + state_value(blend["destBlend"], BLEND) +
                    ", " + state_value(blend["srcBlendAlpha"], BLEND) + " " + state_value(blend["destBlendAlpha"], BLEND))
        rows.append("BlendOp" + target + " " + state_value(blend["blendOp"], BLEND_OP) +
                    ", " + state_value(blend["blendOpAlpha"], BLEND_OP))
        mask = blend["colMask"]
        if mask["name"] and mask["name"] != "<noninit>":
            color = state_value(mask)
        else:
            value = int(mask["val"])
            if value != mask["val"] or value < 0 or value > 15:
                raise ShaderRecoveryError("Original pass color mask is invalid.")
            color = "".join(letter for bit, letter in ((8, "R"), (4, "G"), (2, "B"), (1, "A")) if value & bit) or "0"
        rows.append("ColorMask " + color + target)
    rows += ["Stencil {", "Ref " + state_value(state["stencilRef"]),
             "ReadMask " + state_value(state["stencilReadMask"]), "WriteMask " + state_value(state["stencilWriteMask"])]
    for suffix, native in (("Front", "stencilOpFront"), ("Back", "stencilOpBack")):
        actual = state.get(native) or state["stencilOp"]
        for field, command, mapping in (("comp", "Comp", COMPARE), ("pass_", "Pass", STENCIL_OP),
                                        ("fail", "Fail", STENCIL_OP), ("zFail", "ZFail", STENCIL_OP)):
            rows.append(command + suffix + " " + state_value(actual[field], mapping))
    rows.append("}")
    if state.get("fogMode", -1) == 0:
        rows.append("Fog { Mode Off }")
    elif state.get("fogMode", -1) != -1:
        raise ShaderRecoveryError("Original fixed-function fog state requires a separate portable proof.")
    return "\n".join(rows)
