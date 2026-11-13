"""Preserve DXBC integer bits carried by SPIRV-Cross float register temporaries.

DXBC MOV is untyped. HLSL float assignments can flush subnormals or canonicalize
NaNs, so later asuint/asint cannot recover the original integer bits reliably.
Keep a parallel uint temporary only for registers whose bits are extracted (and
their raw MOV dependencies), while leaving every original float consumer intact.
"""
from __future__ import annotations

import re
import struct


class IntegerBitsError(RuntimeError):
    pass


TEMP = r'[A-Za-z_]\w*'
REF = r'(?P<name>' + TEMP + r')(?P<index>(?:\[[^\]\n]+\])*)(?P<swizzle>\.[xyzwrgba]{1,4})?'


def arguments(value):
    depth, start, result = 0, 0, []
    for index, char in enumerate(value):
        depth += char in '(['
        depth -= char in ')]'
        if depth < 0:
            raise IntegerBitsError('Original integer carrier expression is unbalanced.')
        if char == ',' and not depth:
            result.append(value[start:index].strip()); start = index + 1
    if depth:
        raise IntegerBitsError('Original integer carrier expression is incomplete.')
    return [*result, value[start:].strip()]


def call(value, names):
    match = re.match(r'(' + names + r')\s*\(', value)
    if not match:
        return None
    depth = 1
    for index in range(match.end(), len(value)):
        depth += value[index] == '('
        depth -= value[index] == ')'
        if not depth:
            if value[index + 1:].strip():
                return None
            return match[1], value[match.end():index]
    raise IntegerBitsError('Original bitcast carrier call is incomplete.')


def rewrite_calls(value, convert):
    pattern = re.compile(r'\b(asuint|asint)\s*\(')
    start, pieces = 0, []
    while True:
        match = pattern.search(value, start)
        if match is None:
            return ''.join([*pieces, value[start:]])
        depth, end = 1, match.end()
        while end < len(value) and depth:
            depth += value[end] == '('
            depth -= value[end] == ')'
            end += 1
        if depth:
            raise IntegerBitsError('Original integer extraction is unbalanced.')
        expression = rewrite_calls(value[match.end():end - 1], convert)
        pieces += [value[start:match.start()], convert(match[1], expression)]
        start = end


def primary_swizzle(expression):
    match = re.fullmatch(r'(.+?)((?:\.[xyzwrgba]{1,4})+)', expression.strip())
    if not match:
        return None
    base = match[1].strip()
    literal = re.fullmatch(r'[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fF]?', base)
    if re.fullmatch(REF, base) or literal or call(base, r'[\w.]+'):
        return base, match[2]
    # A trailing operand swizzle in an arithmetic expression belongs to that
    # operand; it must never be moved onto the whole expression.
    return None


def restore(hlsl):
    declaration_matches = list(re.finditer(r'\bstatic\s+float([1-4]?)\s+(' + TEMP + r')\s*((?:\[\d+\])*)\s*;', hlsl))
    # SPIRV-Cross uses both static input/cbuffer carriers and function-local
    # DXBC temporaries. Material uniforms outside functions must stay uniforms.
    functions = []
    for function in re.finditer(r'(?m)^\w+\s+\w+\s*\([^;{}]*\)\s*\{', hlsl):
        depth, end = 1, function.end()
        while end < len(hlsl) and depth:
            depth += hlsl[end] == '{'; depth -= hlsl[end] == '}'; end += 1
        if depth:
            raise IntegerBitsError('Original native function body is incomplete.')
        functions.append((function.end(), end - 1))
    for declaration in re.finditer(r'(?m)^\s*float([1-4]?)\s+(' + TEMP + r')\s*((?:\[\d+\])*)\s*(?:=[^;]*)?;', hlsl):
        if any(start <= declaration.start(2) < end for start, end in functions):
            declaration_matches.append(declaration)
    if len({m[2] for m in declaration_matches}) != len(declaration_matches):
        raise IntegerBitsError('Native float carrier has overlapping declaration scopes.')
    declarations = {m[2]: int(m[1] or 1) for m in declaration_matches}
    extents = {m[2]: m[3] for m in declaration_matches}
    if not declarations:
        return hlsl, {'registerCount': 0, 'mirroredWrites': 0, 'capturedBitcasts': 0}
    assignments = list(re.finditer(r'(?m)^(?P<indent>\s*)(?P<declaration>float[1-4]?\s+)?(?P<lhs>' + REF + r')\s*=\s*(?P<rhs>[^;]+);', hlsl))
    needed = set()
    def discover(kind, expression):
        match = re.fullmatch(REF, expression.strip())
        if match and match['name'] in declarations:
            needed.add(match['name'])
        return kind + '(' + expression + ')'
    rewrite_calls(hlsl, discover)

    def copies(expression):
        swizzle = primary_swizzle(expression)
        if swizzle and not re.fullmatch(REF, expression.strip()):
            return copies(swizzle[0])
        match = re.fullmatch(REF, expression.strip())
        if match and match['name'] in declarations:
            return {match['name']}
        constructor = call(expression.strip(), r'float[1-4]')
        return set().union(*(copies(arg) for arg in arguments(constructor[1]))) if constructor else set()
    while True:
        before = set(needed)
        for assignment in assignments:
            if assignment['name'] in needed:
                needed.update(copies(assignment['rhs']))
        if needed == before:
            break
    if not needed:
        return hlsl, {'registerCount': 0, 'mirroredWrites': 0, 'capturedBitcasts': 0}

    def expression_width(expression):
        expression = expression.strip()
        reference = re.fullmatch(REF, expression)
        if reference:
            return len(reference['swizzle'][1:]) if reference['swizzle'] else declarations.get(reference['name'])
        swizzle = primary_swizzle(expression)
        if swizzle:
            return len(swizzle[1].rsplit('.', 1)[-1])
        constructor = call(expression, r'(?:float|uint|int)[1-4]')
        if constructor:
            return int(constructor[0][-1])
        if re.fullmatch(r'[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fF]?', expression):
            return 1
        return None

    def bits(expression, literal_is_float=True, width=None):
        expression = expression.strip()
        reference = re.fullmatch(REF, expression)
        if reference and reference['name'] in needed:
            return 'QuestOriginalBits_' + reference['name'] + reference['index'] + (reference['swizzle'] or '')
        swizzle = primary_swizzle(expression)
        if swizzle and not reference:
            # Unknown builtin/material references may be fixed/half. Their
            # original float assignment includes a numeric conversion; retain
            # that conversion on the actual selected lanes before bit capture.
            return '(' + bits(swizzle[0], literal_is_float, expression_width(swizzle[0])) + ')' + swizzle[1]
        bitcast = call(expression, 'asfloat')
        if bitcast:
            # asuint/asint of an integer expression reinterprets its signedness;
            # the original float storage is absent from this exact bit path.
            return 'asuint(' + bitcast[1] + ')'
        constructor = call(expression, r'float[1-4]')
        if constructor:
            args = arguments(constructor[1])
            return constructor[0].replace('float', 'uint') + '(' + ', '.join(
                bits(arg, width=expression_width(arg) or (int(constructor[0][-1]) if len(args) == 1 else None))
                for arg in args) + ')'
        if re.fullmatch(r'[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fF]?', expression):
            if not literal_is_float and re.fullmatch(r'[+-]?\d+', expression):
                return str(int(expression) & 0xffffffff) + 'u'
            raw = struct.unpack('<I', struct.pack('<f', float(expression.rstrip('fF'))))[0]
            return str(raw) + 'u'
        if width:
            shape = 'float' + (str(width) if width > 1 else '')
            return 'asuint(' + shape + '(' + expression + '))'
        return 'asuint(' + expression + ')'

    extracted = 0
    def extraction(kind, expression):
        nonlocal extracted
        result = bits(expression, literal_is_float=False)
        original = 'asuint(' + expression + ')'
        if result != original:
            extracted += 1
        return result if kind == 'asuint' else 'asint(' + result + ')'

    # Keep evaluation order: capture bits while the old register is intact,
    # perform its original float write, then update the parallel uint register.
    # Native texture/arithmetic expressions have no side effects; original
    # loop/branch structure and every native float consumer remain unchanged.
    changes, mirrored = [], 0
    for assignment in assignments:
        if assignment['name'] not in needed:
            continue
        rhs = rewrite_calls(assignment['rhs'].strip(), extraction)
        width = len(assignment['swizzle'][1:]) if assignment['swizzle'] else declarations[assignment['name']]
        temporary = 'QuestCapturedBits_' + str(mirrored)
        shape = 'uint' + (str(width) if width > 1 else '')
        indent = assignment['indent']
        mirror = 'QuestOriginalBits_' + assignment['lhs']
        replacement = (indent + shape + ' ' + temporary + ' = ' + bits(rhs, width=width) + ';\n' +
                       indent + (assignment['declaration'] or '') + assignment['lhs'] + ' = ' + rhs + ';\n' + indent + mirror + ' = ' + temporary + ';')
        changes.append((assignment.start(), assignment.end(), replacement)); mirrored += 1
    for start, end, replacement in reversed(changes):
        hlsl = hlsl[:start] + replacement + hlsl[end:]
    hlsl = rewrite_calls(hlsl, extraction)
    shadows = '\n'.join('static uint' + (str(declarations[name]) if declarations[name] > 1 else '') + ' QuestOriginalBits_' + name + extents[name] + ';' for name in sorted(needed))
    return shadows + '\n' + hlsl, {'registerCount': len(needed), 'mirroredWrites': mirrored, 'capturedBitcasts': extracted}
