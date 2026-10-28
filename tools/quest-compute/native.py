"""Read the exact AssetRipper Unity 2021 compute YAML subset without dependencies.

This is intentionally not a general YAML reader. Unknown original kernel layouts,
keywords, renderers and resource fields fail before conversion or project writes.
"""
from __future__ import annotations

import re
import struct


class ComputeRecoveryError(RuntimeError):
    pass


def integer(value: str) -> int:
    if not re.fullmatch(r"-?\d+", value):
        raise ComputeRecoveryError("Invalid native compute integer: " + value)
    return int(value)


def fields(text: str, indent: int, allowed: set[str]) -> dict[str, str]:
    result: dict[str, str] = {}
    for line in text.splitlines():
        if not line.strip():
            continue
        match = re.fullmatch(" " * indent + r"([A-Za-z][A-Za-z0-9_]*):(?: (.*))?", line)
        if not match or match[1] not in allowed or match[1] in result:
            raise ComputeRecoveryError("Unexplained original compute field: " + line[:160])
        result[match[1]] = match[2] or ""
    if set(result) != allowed:
        raise ComputeRecoveryError("Missing original compute fields: " + str(sorted(allowed - result.keys())))
    return result


def named_rows(text: str, indent: int) -> list[tuple[str, str]]:
    if text.strip() == "[]":
        return []
    pattern = re.compile("^" + " " * indent + r"- name: ([A-Za-z_$][A-Za-z0-9_$]*)$", re.M)
    matches = list(pattern.finditer(text))
    if not matches or text[:matches[0].start()].strip():
        raise ComputeRecoveryError("Invalid original compute named sequence.")
    result = []
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        result.append((match[1], text[match.end():end].strip("\n")))
    return result


def sections(text: str, indent: int) -> dict[str, str]:
    pattern = re.compile("^" + " " * indent + r"([A-Za-z][A-Za-z0-9_]*):(?: (.*))?$", re.M)
    matches = list(pattern.finditer(text))
    if not matches or text[:matches[0].start()].strip():
        raise ComputeRecoveryError("Invalid original compute mapping.")
    result = {}
    for index, match in enumerate(matches):
        end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
        body = text[match.end():end].strip("\n")
        if match[1] in result or match[2] and body.strip():
            raise ComputeRecoveryError("Ambiguous original compute section: " + match[1])
        result[match[1]] = match[2] if match[2] is not None else body
    return result


def resources(text: str) -> list[dict]:
    result = []
    for name, body in named_rows(text, 10):
        row = fields(body, 12, {"generatedName", "bindPoint", "samplerBindPoint", "texDimension"})
        if row["generatedName"]:
            raise ComputeRecoveryError("Unexpected native compute generated resource name.")
        result.append({"name": name, "slot": integer(row["bindPoint"]),
                       "samplerSlot": integer(row["samplerBindPoint"]), "dimension": integer(row["texDimension"])})
    return result


def constant_buffers(text: str) -> list[dict]:
    result = []
    for name, body in named_rows(text, 4):
        row = sections(body, 6)
        if set(row) != {"byteSize", "params"}:
            raise ComputeRecoveryError("Unsupported original compute constant buffer.")
        size = integer(row["byteSize"])
        if size <= 0 or size % 16:
            raise ComputeRecoveryError("Invalid original compute constant buffer extent.")
        parameters = []
        for param_name, param_body in named_rows(row["params"], 6):
            param = {key: integer(value) for key, value in fields(param_body, 8,
                {"type", "offset", "arraySize", "rowCount", "colCount"}).items()}
            if param["type"] not in (0, 1) or not 1 <= param["rowCount"] <= 4 or not 1 <= param["colCount"] <= 4:
                raise ComputeRecoveryError("Unsupported original compute parameter type.")
            if param["offset"] < 0 or param["offset"] % 4 or param["arraySize"] < 0:
                raise ComputeRecoveryError("Invalid original compute parameter offset/array.")
            parameters.append({"name": param_name, "type": param["type"], "rows": param["rowCount"],
                "columns": param["colCount"], "matrix": param["rowCount"] > 1,
                "arraySize": param["arraySize"], "byteOffset": param["offset"]})
        result.append({"name": name, "bytes": size, "fields": parameters, "structures": []})
    return result


def parse(text: str) -> dict:
    if not re.search(r"^--- !u!72 &7200000$", text, re.M):
        raise ComputeRecoveryError("Original compute is not class72/local7200000.")
    marker = "  m_CompilationContext:\n"
    if text.count(marker) != 1 or text.count("  variants:\n") != 1:
        raise ComputeRecoveryError("Unknown original compute document structure.")
    prefix, variants = text.split("  variants:\n", 1)
    variants = variants.split(marker, 1)[0]
    name = re.search(r"^  m_Name: ([A-Za-z_][A-Za-z0-9_]*)$", prefix, re.M)
    if not name or re.findall(r"^  - serializedVersion: (\d+)$", variants, re.M) != ["2"]:
        raise ComputeRecoveryError("Original compute requires one known renderer variant.")
    variant = sections(variants.split("  - serializedVersion: 2\n", 1)[1], 4)
    expected = {"targetRenderer", "targetLevel", "kernels", "constantBuffers", "resourcesResolved", "compilerPlatform", "needsReflectionData"}
    if set(variant) != expected or variant["targetRenderer"] != "2" or variant["targetLevel"] != "0":
        raise ComputeRecoveryError("Original compute is not the audited D3D11 variant.")
    buffers = constant_buffers(variant["constantBuffers"])
    kernel_blocks = re.split(r"^    - serializedVersion: 2\n", variant["kernels"], flags=re.M)
    if kernel_blocks[0].strip() or len(kernel_blocks) < 2:
        raise ComputeRecoveryError("Unknown original compute kernel sequence.")
    kernels = []
    for block in kernel_blocks[1:]:
        kernel = sections(block, 6)
        if set(kernel) != {"name", "variantMap", "globalKeywords", "localKeywords"} or kernel["globalKeywords"] != "[]" or kernel["localKeywords"] != "[]":
            raise ComputeRecoveryError("Original compute keyword variants require explicit recovery.")
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", kernel["name"]) or not kernel["variantMap"].startswith("        :\n"):
            raise ComputeRecoveryError("Unknown original compute kernel name/variant key.")
        program = sections(kernel["variantMap"][len("        :\n"):], 10)
        program_keys = {"serializedVersion", "cbVariantIndices", "cbs", "textures", "builtinSamplers", "inBuffers", "outBuffers", "code", "threadGroupSize", "requirements", "keywords", "isCompiled"}
        if set(program) != program_keys or program["serializedVersion"] != "2" or program["keywords"] != "[]" or program["isCompiled"] != "0":
            raise ComputeRecoveryError("Unsupported original native compute program fields.")
        code = program["code"]
        groups = program["threadGroupSize"]
        indices = program["cbVariantIndices"]
        if not re.fullmatch(r"[0-9a-f]+", code) or len(code) % 2 or not code.startswith("44584243"):
            raise ComputeRecoveryError("Original compute code is not native DXBC.")
        if not re.fullmatch(r"[0-9a-f]{24}", groups):
            raise ComputeRecoveryError("Invalid original compute thread group extent.")
        threadgroups = struct.unpack("<3I", bytes.fromhex(groups))
        if not all(0 < value <= 1024 for value in threadgroups) or threadgroups[0] * threadgroups[1] * threadgroups[2] > 1024:
            raise ComputeRecoveryError("Invalid original compute thread group dimensions.")
        if indices == "":
            selected = []
        elif re.fullmatch(r"(?:[0-9a-f]{8})+", indices):
            selected = list(struct.unpack("<" + "I" * (len(indices) // 8), bytes.fromhex(indices)))
        else:
            raise ComputeRecoveryError("Invalid original compute constant buffer indices.")
        cbs = resources(program["cbs"])
        if len(selected) != len(cbs) or any(index >= len(buffers) for index in selected):
            raise ComputeRecoveryError("Native compute constant buffer selection differs.")
        active = []
        bindings = []
        for resource, index in zip(cbs, selected):
            buffer = buffers[index]
            if resource["name"] != buffer["name"] or resource["dimension"] != -1 or resource["samplerSlot"] != -1:
                raise ComputeRecoveryError("Native compute constant buffer identity differs.")
            active.append(buffer)
            bindings.append({"kind": "cbuffer", "name": resource["name"], "slot": resource["slot"]})
        for kind, key in (("texture", "textures"), ("buffer", "inBuffers")):
            for resource in resources(program[key]):
                bindings.append({**resource, "kind": kind, "arraySize": 1})
        samplers = named_rows(program["builtinSamplers"], 10) if program["builtinSamplers"] == "[]" else None
        if samplers is None:
            # Built-in samplers use integer native sampler-state rows, not names.
            matches = re.findall(r"^          - sampler: (\d+)\n            bindPoint: (\d+)$", program["builtinSamplers"], re.M)
            consumed = "\n".join("          - sampler: " + state + "\n            bindPoint: " + slot for state, slot in matches)
            if not matches or consumed != program["builtinSamplers"].strip("\n"):
                raise ComputeRecoveryError("Unknown original compute builtin sampler layout.")
            bindings.extend({"kind": "sampler", "slot": int(slot), "nativeState": int(state)} for state, slot in matches)
        kernels.append({"name": kernel["name"], "code": bytes.fromhex(code), "threadGroups": list(threadgroups),
            "interface": {"buffers": active, "bindings": bindings}, "outputs": resources(program["outBuffers"]),
            "requirements": integer(program["requirements"])})
    if len({kernel["name"] for kernel in kernels}) != len(kernels):
        raise ComputeRecoveryError("Duplicate original compute kernel names.")
    return {"name": name[1], "localFileId": 7200000, "kernels": kernels}
