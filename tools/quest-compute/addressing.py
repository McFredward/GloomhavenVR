"""Recover exact DXBC integer-bit carriers without changing floating point math.

Immutable dispatch expressions and IEEE float-literal bits are propagated.
Unknown integer expressions are captured once in uint temporaries; actual float
consumers stay in place. Float arithmetic invalidates bit aliases. Conditional
writes are invalidated after their scope; no branch-selected value is guessed.
"""
from __future__ import annotations

import ast
import copy
import re
import struct
from dataclasses import dataclass


@dataclass(frozen=True)
class Scalar:
    kind: str  # int, uint, or the unchanged float bits of one of those types
    text: str


def scalarize(expression: str, values: dict[str, list[Scalar | None]]) -> list[Scalar | None] | None:
    expression = re.sub(r"\b(0x[0-9a-fA-F]+|\d+)[uU]\b", r"uint(\1)", expression)
    expression = re.sub(r"\b((?:\d+\.\d*|\.\d+|\d+)(?:[eE][+-]?\d+)?)[fF]\b", r"\1", expression)
    try:
        tree = ast.parse(expression, mode="eval").body
    except SyntaxError:
        return None
    def visit(node):
        if isinstance(node, ast.Constant):
            if type(node.value) is float:
                try:
                    bits = struct.unpack("<I", struct.pack("<f", node.value))[0]
                except OverflowError:
                    return [None]
                return [Scalar("bits-uint", "0u" if bits == 0 else "uint(0x" + format(bits, "08x") + ")")]
            return [Scalar("int", str(node.value))] if type(node.value) is int else [None]
        if isinstance(node, ast.Name):
            if node.id == "gl_LocalInvocationIndex":
                return [Scalar("uint", node.id)]
            if node.id in ("gl_GlobalInvocationID", "gl_LocalInvocationID", "gl_WorkGroupID"):
                return [Scalar("uint", node.id + "." + component) for component in "xyz"]
            return values.get(node.id)
        if isinstance(node, ast.Attribute) and set(node.attr) <= set("xyzw"):
            base = visit(node.value)
            if base is None or any("xyzw".index(component) >= len(base) for component in node.attr):
                return None
            return [base["xyzw".index(component)] for component in node.attr]
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
            name = node.func.id
            arguments = [visit(arg) for arg in node.args]
            if any(argument is None for argument in arguments):
                return None
            if name == "spvBitfieldInsert" and len(arguments) == 4:
                base, insert, offset, count = arguments
                if len(base) != len(insert) or len(offset) != 1 or len(count) != 1:
                    return None
                if any(value is None or value.kind not in ("int", "uint") for argument in arguments for value in argument):
                    return None
                # Keep SPIRV-Cross's original pure integer helper unchanged;
                # recover only the integer result hidden by subsequent bitcasts.
                return [Scalar("uint", "spvBitfieldInsert(uint(" + a.text + "), uint(" + b.text + "), uint(" + offset[0].text + "), uint(" + count[0].text + "))")
                    for a, b in zip(base, insert)]
            if name in ("asint", "asuint", "asfloat") and len(arguments) == 1:
                result = []
                for value in arguments[0]:
                    if value is None:
                        result.append(None)
                    elif name == "asfloat" and value.kind in ("int", "uint"):
                        result.append(Scalar("bits-" + value.kind, value.text))
                    elif name != "asfloat" and value.kind.startswith("bits-"):
                        kind = name[2:]
                        original = value.kind[5:]
                        result.append(Scalar(kind, value.text if kind == original else name + "(" + value.text + ")"))
                    elif name != "asfloat" and value.kind in ("int", "uint"):
                        kind = name[2:]
                        result.append(Scalar(kind, value.text if kind == value.kind else name + "(" + value.text + ")"))
                    else:
                        result.append(None)
                return result
            constructor = re.fullmatch(r"(float|int|uint)([1-4]?)", name)
            if constructor:
                kind, size = constructor[1], int(constructor[2] or 1)
                items = [item for argument in arguments for item in argument]
                if len(items) == 1:
                    items *= size
                if len(items) != size:
                    return None
                result = []
                for value in items:
                    if value is None:
                        result.append(None)
                    elif kind == "float":
                        result.append(value if value.kind.startswith("bits-") else None)
                    elif value.kind in ("int", "uint"):
                        result.append(Scalar(kind, value.text if value.kind == kind else kind + "(" + value.text + ")"))
                    else:
                        result.append(None)
                return result
        if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.USub, ast.UAdd, ast.Invert)):
            if isinstance(node.operand, ast.Constant) and type(node.operand.value) is float and isinstance(node.op, (ast.USub, ast.UAdd)):
                literal = -node.operand.value if isinstance(node.op, ast.USub) else node.operand.value
                return visit(ast.Constant(value=literal))
            argument = visit(node.operand)
            if argument is None:
                return None
            symbol = {ast.USub: "-", ast.UAdd: "+", ast.Invert: "~"}[type(node.op)]
            return [Scalar(value.kind, symbol + "(" + value.text + ")") if value is not None
                and value.kind in ("int", "uint") else None for value in argument]
        if isinstance(node, ast.BinOp) and isinstance(node.op, (ast.Add, ast.Sub, ast.Mult, ast.LShift, ast.RShift, ast.BitAnd, ast.BitOr, ast.BitXor)):
            left, right = visit(node.left), visit(node.right)
            if left is None or right is None:
                return None
            count = max(len(left), len(right))
            if len(left) == 1: left *= count
            if len(right) == 1: right *= count
            if len(left) != len(right): return None
            symbol = {ast.Add: "+", ast.Sub: "-", ast.Mult: "*", ast.LShift: "<<", ast.RShift: ">>",
                ast.BitAnd: "&", ast.BitOr: "|", ast.BitXor: "^"}[type(node.op)]
            result = []
            for a, b in zip(left, right):
                if a is None or b is None or a.kind not in ("int", "uint") or b.kind not in ("int", "uint"):
                    result.append(None)
                else:
                    # HLSL arithmetic has the same usual int/uint conversion as
                    # the original instruction expression. Preserve both casts.
                    kind = a.kind if isinstance(node.op, (ast.LShift, ast.RShift)) else "uint" if "uint" in (a.kind, b.kind) else "int"
                    result.append(Scalar(kind, "(" + a.text + ") " + symbol + " (" + b.text + ")"))
            return result
        return None
    return visit(tree)


def restore_integer_addresses(hlsl: str) -> tuple[str, list[dict]]:
    if re.search(r"\b(for|while|do)\b", hlsl):
        # The audited 36 original banks are fully unrolled. A future bytecode
        # bank containing loops needs a loop-aware reaching-definitions proof;
        # never propagate a first-iteration value into later iterations.
        raise ValueError("Integer compute identity restoration requires the audited unrolled original instruction bank.")
    if "spvBitfieldInsert(" in hlsl:
        helpers = re.findall(r"uint[1-4]? spvBitfieldInsert\([^\n]+\)\s*\{([^}]+)\}", hlsl)
        expected = "uint Mask = Count == 32 ? 0xffffffff : (((1u << Count) - 1) << (Offset & 31)); return (Base & ~Mask) | ((Insert << Offset) & Mask);"
        if len(helpers) != 4 or any(" ".join(body.split()) != expected for body in helpers):
            raise ValueError("Original pure integer bitfield helper body changed.")
    values: dict[str, list[Scalar | None]] = {}
    scopes = []
    proofs = []
    result = []
    shared = set(re.findall(r"groupshared uint (\w+)\[", hlsl))
    outputs = set(re.findall(r"(?:RWTexture\w+(?:<[^>]+>)?|RWStructuredBuffer<[^>]+>)\s+(\w+)\s*;", hlsl))
    targets = shared | outputs
    widths = {}
    for line in hlsl.splitlines():
        original_line = line
        declaration = re.match(r"\s*(?:float|int|uint)([1-4]?)\s+(\w+)\b", original_line)
        if declaration:
            widths[declaration[2]] = int(declaration[1] or 1)
        assignment = re.fullmatch(r"\s*(?:(?:float|int|uint)[1-4]?\s+)?(\w+)(?:\.([xyzw]+))?\s*=\s*(.*);\s*", original_line)
        captured = None
        if assignment:
            name, mask, expression = assignment.groups()
            outer = re.fullmatch(r"asfloat\((.*)\)", expression)
            known = scalarize(expression, values)
            if outer and (known is None or any(value is None for value in known)):
                # DXBC stores integer bits in untyped registers. Retaining those
                # bits in float temporaries lets FXC flush denormals or assume
                # away NaNs, even before a later asuint/asint extracts the bits.
                # Capture the original integer expression exactly once in an
                # integer temporary; preserve the original float value as well
                # for any actual float consumer. No arithmetic is reconstructed.
                width = len(mask) if mask else widths.get(name)
                if width is None:
                    raise ValueError("Original integer-bit assignment width is unproven: " + name)
                alias = "QuestOriginalIntegerBits_" + str(len(proofs))
                kind = "uint" + (str(width) if width > 1 else "")
                capture = re.match(r"\s*", line)[0] + kind + " " + alias + " = " + kind + "(" + outer[1] + ");"
                # Recover any earlier exact integer extraction in this captured
                # expression before FXC sees a float-bit alias again.
                for match in list(re.finditer(r"\b(asint|asuint)\((\w+(?:\.[xyzw]+)?)\)", capture)):
                    prior = scalarize(match[0], values)
                    if prior and all(value is not None and value.kind in ("int", "uint") for value in prior):
                        restored = prior[0].text if len(prior) == 1 else match[1][2:] + str(len(prior)) + "(" + ", ".join(value.text for value in prior) + ")"
                        capture = capture.replace(match[0], "(" + restored + ")", 1)
                result.append(capture)
                line = line[:line.rfind(expression)] + "asfloat(" + alias + ");"
                captured = [Scalar("bits-uint", alias + ("." + component if width > 1 else "")) for component in "xyzw"[:width]]
                proofs.append({"resource": "integer-register-capture", "originalIntegerExpression": outer[1],
                    "capturedIntegerVariable": alias, "vectorWidth": width, "exactIntegerExpressionCapturedOnce": True,
                    "integerBitcastIdentityOnly": True, "immutableDispatchIdsAndConstantsOnly": False})
        # The same bitcast identity must be visible in guards selecting the sole
        # writer of a reduction result. Otherwise FXC reports a false UAV race
        # after the independently proven address has already been restored.
        for match in list(re.finditer(r"\b(asint|asuint)\((\w+(?:\.[xyzw]+)?)\)", line)):
            address = scalarize(match[0], values)
            if address and all(value is not None and value.kind in ("int", "uint") for value in address):
                kind = match[1][2:]
                restored = address[0].text if len(address) == 1 else kind + str(len(address)) + "(" + ", ".join(value.text for value in address) + ")"
                if len(restored) <= 8192 and restored != match[0]:
                    line = line.replace(match[0], "(" + restored + ")", 1)
                    proofs.append({"resource": "integer-register-extraction", "originalAddress": match[0], "restoredAddress": restored,
                        "integerBitcastIdentityOnly": True, "immutableDispatchIdsAndConstantsOnly": True})
        for match in list(re.finditer(r"\b(" + "|".join(re.escape(target) for target in sorted(targets)) + r")\[([^\[\]]+)\]", line)) if targets else []:
            address = scalarize(match[2], values)
            if address and all(value is not None and value.kind in ("int", "uint") for value in address):
                if len(address) == 1:
                    restored = address[0].text
                else:
                    restored = "int" + str(len(address)) + "(" + ", ".join("int(" + value.text + ")" for value in address) + ")"
                if len(restored) <= 8192 and restored != match[2]:
                    line = line.replace(match[0], match[1] + "[" + restored + "]", 1)
                    proofs.append({"resource": match[1], "originalAddress": match[2], "restoredAddress": restored,
                        "integerBitcastIdentityOnly": True, "immutableDispatchIdsAndConstantsOnly": True})
        if assignment:
            name, mask, expression = assignment.groups()
            value = captured if captured is not None else scalarize(expression, values)
            if mask:
                previous = list(values.get(name, [None] * 4))
                while len(previous) < 4: previous.append(None)
                for index, component in enumerate(mask):
                    previous["xyzw".index(component)] = value[index] if value and index < len(value) else None
                values[name] = previous
            else:
                values[name] = value if value is not None else [None] * 4
        mutation = re.fullmatch(r"\s*(\w+)(?:\.([xyzw]+))?\s*(?:[+*/&|^-]=|\+\+|--).*;\s*", original_line)
        if mutation:
            name, mask = mutation.groups()
            previous = list(values.get(name, [None] * 4))
            if mask:
                while len(previous) < 4: previous.append(None)
                for component in mask: previous["xyzw".index(component)] = None
                values[name] = previous
            else:
                values[name] = [None] * len(previous)
        if original_line.strip() == "{":
            scopes.append(copy.deepcopy(values))
        if original_line.strip() == "}" and scopes:
            before = scopes.pop()
            for name in set(values) | set(before):
                current, earlier = values.get(name, []), before.get(name, [])
                values[name] = [current[index] if index < len(current) and index < len(earlier)
                    and current[index] == earlier[index] else None
                    for index in range(max(len(current), len(earlier), 1))]
        result.append(line)
    return "\n".join(result) + "\n", proofs
