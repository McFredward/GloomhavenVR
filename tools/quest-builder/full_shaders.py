"""Recover compiled shader interfaces without inventing rendering defaults.

Unity's original D3D11 programs lack DXBC RDEF names. The per-program serialized
parameter table still records each native cbuffer field and resource binding.
This module recovers those exact interfaces before an open-source DXBC compiler
translates the instruction stream. Unknown layouts stop conversion explicitly.
"""
import hashlib
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
        structures = reader.count()
        if structures:
            raise ShaderRecoveryError("Original shader contains a nested structured cbuffer; its layout needs explicit recovery.")
        buffers.append({"name": name, "bytes": size, "fields": fields})
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
                     "componentType": component_type, "register": register, "mask": mask & 255})
    return rows


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
    result = subprocess.run([str(spirv_cross), str(spirv), "--hlsl", "--shader-model", "50"], capture_output=True, text=True)
    if result.returncode:
        raise ShaderRecoveryError("Original instruction stream HLSL translation failed: " + result.stderr[-2000:])
    hlsl.write_text(result.stdout)
    return {"originalDxbcSha256": key, "spirvSha256": hashlib.sha256(spirv.read_bytes()).hexdigest(),
            "translatedHlslSha256": hashlib.sha256(result.stdout.encode()).hexdigest(), "hlslPath": str(hlsl),
            "inputSignature": signature(chunks[b"ISGN"]) if b"ISGN" in chunks else [],
            "outputSignature": signature(chunks[b"OSGN"]) if b"OSGN" in chunks else [],
            "unityUniformsRestored": False, "androidShaderCompiled": False, "pixelParityVerified": False}


def merge_interface(common, names, delta):
    """Combine original partial common parameters and per-variant deltas."""
    names = {int(index): name for name, index in names}
    buffers = {}
    def add_buffer(name, size, fields):
        if name not in buffers:
            buffers[name] = {"name": name, "bytes": size, "fields": []}
        if buffers[name]["bytes"] != size:
            raise ShaderRecoveryError("Original common/delta cbuffer byte sizes disagree: " + name)
        seen = {field["byteOffset"]: field for field in buffers[name]["fields"]}
        for field in fields:
            previous = seen.get(field["byteOffset"])
            if previous is not None and previous != field:
                raise ShaderRecoveryError("Original common/delta cbuffer fields overlap: " + field["name"])
            if previous is None:
                buffers[name]["fields"].append(field)
                seen[field["byteOffset"]] = field
    for buffer in common.get("m_ConstantBuffers", []):
        if buffer.get("m_StructParams"):
            raise ShaderRecoveryError("Nested original common cbuffer structure requires explicit recovery.")
        fields = []
        for field in buffer.get("m_VectorParams", []):
            fields.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": 1,
                           "columns": field["m_Dim"], "matrix": False, "arraySize": field["m_ArraySize"],
                           "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
        for field in buffer.get("m_MatrixParams", []):
            fields.append({"name": names[field["m_NameIndex"]], "type": field["m_Type"], "rows": field["m_RowCount"],
                           "columns": 4, "matrix": True, "arraySize": field["m_ArraySize"],
                           "byteOffset": field.get("m_OffsetInConstantBuffer") if field.get("m_OffsetInConstantBuffer") is not None else field["m_Index"]})
        add_buffer(names[buffer["m_NameIndex"]], buffer["m_Size"], fields)
    for buffer in delta["buffers"]:
        add_buffer(buffer["name"], buffer["bytes"], buffer["fields"])
    bindings = [{"name": names[binding["m_NameIndex"]], "kind": "cbuffer", "slot": binding["m_Index"]}
                for binding in common.get("m_ConstantBufferBindings", [])]
    for binding in common.get("m_TextureParams", []):
        bindings.append({"name": names[binding["m_NameIndex"]], "kind": "texture", "slot": binding["m_Index"],
                         "samplerSlot": binding["m_SamplerIndex"], "dimension": binding["m_Dim"] * 2})
    bindings.extend(delta["bindings"])
    unique = {}
    for binding in bindings:
        key = (binding["kind"], binding["slot"])
        if key in unique and unique[key] != binding:
            raise ShaderRecoveryError("Original common/delta resource bindings disagree.")
        unique[key] = binding
    return {"buffers": list(buffers.values()), "bindings": list(unique.values())}


BUILTIN = {"_Time", "_SinTime", "_CosTime", "unity_DeltaTime", "_WorldSpaceCameraPos", "_ProjectionParams",
           "_ScreenParams", "_ZBufferParams", "_OrthoParams", "_WorldSpaceLightPos0", "_LightColor0",
           "_LightMatrix0", "_LightSplitsNear", "_LightSplitsFar", "_ShadowMapTexture_TexelSize"}
MATRIX_ALIASES = {"unity_MatrixVP": "UNITY_MATRIX_VP", "unity_MatrixV": "UNITY_MATRIX_V",
                  "unity_MatrixP": "UNITY_MATRIX_P", "glstate_matrix_projection": "UNITY_MATRIX_P"}
BUILTIN_TEXTURES = {"unity_SpecCube0", "unity_SpecCube1", "unity_ProbeVolumeSH", "unity_Lightmap",
                    "unity_LightmapInd", "unity_DynamicLightmap", "unity_DynamicDirectionality",
                    "unity_DynamicNormal", "unity_ShadowMask"}


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


def restore_uniforms(hlsl, interface, input_signature=()):
    """Bind translated original math to recovered Unity material/builtin data.

    Every cbuffer component read by the actual program must have original
    metadata. Only unread padding receives zero; missing used uniforms fail.
    """
    buffers = {buffer["name"]: buffer for buffer in interface["buffers"]}
    bindings = {(binding["kind"], binding["slot"]): binding for binding in interface["bindings"]}
    declarations, initializers, observed = {}, [], []
    pattern = re.compile(r"cbuffer \w+\s*:\s*register\(b(\d+)\)\s*\{\s*float4 (\w+)\[(\d+)\]\s*:\s*packoffset\(c0\);\s*\};", re.S)
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
            if not name.startswith("unity_") and name not in BUILTIN and name not in MATRIX_ALIASES:
                scalar_type = "int" if field["type"] == 1 else "float"
                shape = (str(field["rows"]) + "x" + str(field["columns"])) if field["matrix"] else (str(field["columns"]) if field["columns"] > 1 else "")
                declaration = scalar_type + shape + " " + name + ("[" + str(field["arraySize"]) + "]" if field["arraySize"] else "") + ";"
                if name in declarations and declarations[name] != declaration:
                    raise ShaderRecoveryError("Original uniform has conflicting types: " + name)
                declarations[name] = declaration
        body = hlsl[match.end():]
        reads = re.finditer(r"\b" + re.escape(variable) + r"\[([^]]+)\](?:\.([xyzw]+))?", body)
        used = set()
        for read in reads:
            number = re.fullmatch(r"(\d+)[uU]?", read[1])
            if number is None:
                # Dynamic indexing can read any scalar in its original bound.
                used.update(range(size * 4))
            else:
                index = int(number[1])
                used.update(index * 4 + "xyzw".index(component) for component in (read[2] or "xyzw"))
        missing = sorted(used - components.keys())
        if missing:
            raise ShaderRecoveryError("Original metadata cannot explain used cbuffer scalars: " + binding["name"] + " " + repr(missing))
        observed.append({"slot": slot, "originalBuffer": binding["name"], "usedScalars": len(used), "allUsedScalarsBound": True})
        for index in range(size):
            values = [components.get(index * 4 + component, "0.0") for component in range(4)]
            initializers.append(variable + "[" + str(index) + "] = float4(" + ", ".join(values) + ");")
    hlsl = pattern.sub(lambda match: "static float4 " + match[2] + "[" + match[3] + "];", hlsl)
    if re.search(r"\bcbuffer\b", hlsl):
        raise ShaderRecoveryError("Translated shader contains an unsupported original cbuffer declaration.")
    textures = {}
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
    hlsl = re.sub(r"(Texture\w+(?:<[^>]+>)?)\s+(\w+)\s*:\s*register\(t(\d+)\);", texture, hlsl)
    sampler_names = {}
    def sampler(match):
        original_slot = re.fullmatch(r"s(\d+)", match[1])
        if original_slot is None:
            raise ShaderRecoveryError("Translated sampler lost its original DXBC register identity.")
        slot = int(original_slot[1])
        names = {binding["name"] for binding in interface["bindings"] if binding["kind"] == "texture" and binding["samplerSlot"] == slot}
        if not names:
            raise ShaderRecoveryError("Translated sampler has no original sampler binding.")
        # Unity associates an independent sampler with its texture by the
        # literal sampler<TextureName> convention, including its underscore.
        source_name = sorted(names)[0]
        name = "sampler" + source_name
        sampler_names[match[1]] = name
        if source_name in BUILTIN_TEXTURES:
            return "// Native Unity include supplies " + name + "."
        return "SamplerState " + name + ";"
    hlsl = re.sub(r"SamplerState\s+(\w+)\s*:\s*register\(s(\d+)\);", sampler, hlsl)
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
