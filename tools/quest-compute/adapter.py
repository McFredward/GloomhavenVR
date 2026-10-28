"""Restore original compute dispatch/UAV interfaces around translated DXBC math."""
from __future__ import annotations

import re
import struct

if __package__:
    from .native import ComputeRecoveryError
    from .addressing import restore_integer_addresses
    from .formats import image_contract
    from .bounds import restore_texture_loads, restore_vectorscope_atomic
else:
    from native import ComputeRecoveryError
    from addressing import restore_integer_addresses
    from formats import image_contract
    from bounds import restore_texture_loads, restore_vectorscope_atomic


def declarations(program: bytes, graphics) -> dict:
    raw, chunks = graphics.dxbc_container(program)
    if raw != program:
        raise ComputeRecoveryError("Compute DXBC has unexplained prefix/trailing bytes.")
    code = chunks.get(b"SHDR", chunks.get(b"SHEX"))
    if code is None or len(code) % 4:
        raise ComputeRecoveryError("Compute code has no complete original instruction bank.")
    words = struct.unpack("<" + "I" * (len(code) // 4), code)
    if len(words) < 2 or words[0] >> 16 != 5 or words[1] != len(words):
        raise ComputeRecoveryError("Original bytecode is not a complete compute shader.")
    position, groups, outputs, instructions = 2, None, {}, {}
    while position < len(words):
        token = words[position]
        opcode, length = token & 2047, token >> 24 & 127
        if opcode == 0x35:
            length = words[position + 1]
        if not length or position + length > len(words):
            raise ComputeRecoveryError("Malformed native compute instruction extent.")
        instructions[opcode] = instructions.get(opcode, 0) + 1
        if opcode == 0x9b:  # D3D11_SB_OPCODE_DCL_THREAD_GROUP
            if length != 4 or groups is not None:
                raise ComputeRecoveryError("Unknown original compute thread group declaration.")
            groups = list(words[position + 1:position + 4])
        if opcode in (0x9c, 0x9d, 0x9e):
            expected = 3 if opcode == 0x9d else 4
            if length != expected or words[position + 1] != 0x11e000:
                raise ComputeRecoveryError("Unsupported original compute output register shape.")
            slot = words[position + 2]
            if slot in outputs:
                raise ComputeRecoveryError("Duplicate original compute output register.")
            outputs[slot] = {"slot": slot, "kind": {0x9c: "typed", 0x9d: "raw", 0x9e: "structured"}[opcode],
                "strideBytes": words[position + 3] if opcode == 0x9e else 0}
            if opcode == 0x9e and outputs[slot]["strideBytes"] not in (4, 16):
                raise ComputeRecoveryError("Native compute output structure needs explicit additional stride recovery.")
        position += length
    if groups is None:
        raise ComputeRecoveryError("Original compute thread group declaration is missing.")
    return {"threadGroups": groups, "outputs": outputs, "instructions": instructions}


def restore(hlsl: str, kernel: dict, graphics) -> tuple[str, dict]:
    native = declarations(kernel["code"], graphics)
    if native["threadGroups"] != kernel["threadGroups"]:
        raise ComputeRecoveryError("Native DXBC and serialized compute thread groups differ.")
    outputs = {row["slot"]: row for row in kernel["outputs"]}
    if len(outputs) != len(kernel["outputs"]) or outputs.keys() != native["outputs"].keys():
        raise ComputeRecoveryError("Native compute output binding table differs from DXBC declarations.")
    observed, names, structures = [], {}, []
    pattern = re.compile(r"((?:RWTexture\w+(?:<[^>]+>)?|RWBuffer<[^>]+>|RWByteAddressBuffer|RWStructuredBuffer<[^>]+>))\s+(u\d+)\s*:\s*register\(u\d+(?:,\s*space0)?\);")
    def output(match):
        slot = int(match[2][1:])
        if slot not in outputs:
            raise ComputeRecoveryError("Translated compute UAV has no original named output.")
        binding, layout = outputs[slot], native["outputs"][slot]
        kind = match[1]
        if layout["kind"] == "structured":
            if kind != "RWBuffer<uint>" or binding["dimension"] != -1:
                raise ComputeRecoveryError("Native structured compute output type/stride differs.")
            count = layout["strideBytes"] // 4
            if count == 1:
                kind = "RWStructuredBuffer<uint>"
            else:
                structure = "QuestOriginalComputeWords" + str(count)
                structures.append("struct " + structure + " { uint words[" + str(count) + "]; };")
                kind = "RWStructuredBuffer<" + structure + ">"
        elif layout["kind"] == "raw":
            if kind != "RWByteAddressBuffer" or binding["dimension"] != -1:
                raise ComputeRecoveryError("Native raw compute output type differs.")
        elif not kind.startswith("RWTexture") or binding["dimension"] not in (2, 3, 5):
            raise ComputeRecoveryError("Unknown native typed compute output dimension.")
        storage = None
        if layout["kind"] == "typed":
            storage = image_contract(kernel["shaderName"], kernel["name"], binding["name"])
            kind = re.sub(r"<float4>$", "<" + storage["hlslElementType"] + ">", kind)
        if binding["samplerSlot"] != -1:
            raise ComputeRecoveryError("Unexpected compute output sampler ownership.")
        names[match[2]] = binding["name"]
        observed.append({**layout, "name": binding["name"], "translatedType": match[1], "restoredType": kind,
                         "dimension": binding["dimension"], "originalWordAddressingRetained": True, "imageStorage": storage})
        return kind + " " + binding["name"] + ";"
    hlsl = pattern.sub(output, hlsl)
    if len(observed) != len(outputs):
        raise ComputeRecoveryError("Translated compute output closure is incomplete.")
    for row in observed:
        if row["kind"] != "structured" or row["strideBytes"] == 4:
            continue
        original = "u" + str(row["slot"])
        count = row["strideBytes"] // 4
        pattern = re.compile(r"\b" + original + r"\[")
        while True:
            match = pattern.search(hlsl)
            if match is None:
                break
            depth, end = 1, match.end()
            while depth and end < len(hlsl):
                depth += (hlsl[end] == '[') - (hlsl[end] == ']')
                end += 1
            if depth:
                raise ComputeRecoveryError("Unbalanced native structured compute address.")
            expression = hlsl[match.end():end - 1]
            if "++" in expression or "--" in expression or "=" in expression:
                raise ComputeRecoveryError("Native structured address has unexplained side effects.")
            replacement = row["name"] + "[(" + expression + ") / " + str(count) + "u].words[(" + expression + ") % " + str(count) + "u]"
            hlsl = hlsl[:match.start()] + replacement + hlsl[end:]
    for original, name in names.items():
        hlsl = re.sub(r"\b" + original + r"\b", name, hlsl)
    for row in observed:
        storage = row["imageStorage"]
        if storage is None or storage["channelCount"] == 4:
            continue
        # Only components that the original R/RG allocation actually stores are
        # selected. All floating point arithmetic remains upstream and unchanged.
        pattern = re.compile(r"^(\s*" + re.escape(row["name"]) + r"\[[^\n]*\]\s*=\s*)(.*);$", re.M)
        mask = ".x" if storage["channelCount"] == 1 else ".xy"
        hlsl, count = pattern.subn(lambda match: match[1] + "(" + match[2] + ")" + mask + ";", hlsl)
        if not count:
            raise ComputeRecoveryError("Typed compute output has no original full-vector store.")
        storage["originalStoredComponentsSelected"] = True
        storage["storeCount"] = count
    hlsl = "\n".join(dict.fromkeys(structures)) + "\n" + hlsl
    hlsl, uniforms = graphics.restore_uniforms(hlsl, kernel["interface"],
        resource_layouts=graphics.native_buffer_layouts(kernel["code"]))
    hlsl, addresses = restore_integer_addresses(hlsl)
    if any(native["instructions"].get(opcode) for opcode in (0x2e, 0xa3)):
        raise ComputeRecoveryError("Native multisample/UAV load needs an explicit additional bounds contract.")
    hlsl, bounds = restore_texture_loads(hlsl, native["instructions"].get(0x2d, 0))
    atomic_bounds = []
    if kernel["shaderName"] == "Vectorscope" and kernel["name"] == "KVectorscopeGather":
        # Original ATOMIC_IADD has no returned value. SPIRV-Cross introduces
        # an unused HLSL out operand; its non-use is verified by the adapter.
        if native["instructions"].get(0xad, 0) != 1 or native["instructions"].get(0xb4, 0):
            raise ComputeRecoveryError("Original vectorscope structured atomic instruction shape changed.")
        hlsl, atomic = restore_vectorscope_atomic(hlsl)
        atomic_bounds.append(atomic)
    groups = re.findall(r"\[numthreads\((\d+), (\d+), (\d+)\)\]", hlsl)
    if groups != [tuple(str(value) for value in kernel["threadGroups"])]:
        raise ComputeRecoveryError("Translated compute dispatch extent differs from the original.")
    if len(re.findall(r"\bvoid main\(", hlsl)) != 1 or not re.search(r"\bvoid comp_main\(\)", hlsl):
        raise ComputeRecoveryError("Translated compute entry point changed unexpectedly.")
    hlsl = re.sub(r"\bvoid main\(", "void " + kernel["name"] + "(", hlsl)
    return hlsl, {"uniformBindings": uniforms, "outputBindings": observed, "integerAddressIdentityProofs": addresses,
        "integerTextureLoadBounds": bounds, "nativeResourceInstructions": {
            "textureLoad": native["instructions"].get(0x2d, 0), "textureLoadMultisample": native["instructions"].get(0x2e, 0),
            "imageLoad": native["instructions"].get(0xa3, 0), "imageStore": native["instructions"].get(0xa4, 0)},
        "structuredAtomicBounds": atomic_bounds,
        "threadGroupDxbcAndMetadataAgree": True, "originalDispatchSystemValuesRetained": True,
        "originalInstructionTranslation": True, "androidCompiled": False, "pixelParityVerified": False}
