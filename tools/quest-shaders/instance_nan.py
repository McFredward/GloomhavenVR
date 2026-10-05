"""Retain exact native NaN tests while making readonly instance loads explicit.

FXC internally aborts on an inline dynamic struct-CB field passed to isnan in
the recovered Amp_Low fragment. Loading the same typed field into a local
first preserves its address, value and NaN/min/max math. Only source-witnessed
original instance fields used by an actual isnan operand qualify.
"""
from __future__ import annotations
import re


class InstanceReadError(RuntimeError): pass


def restore(source, interface):
    fields = {}
    for buffer in interface['buffers']:
        for structure in buffer.get('structures', []):
            for field in structure['fields']:
                key = (structure['name'], field['name'])
                if key in fields and fields[key][1] != field:
                    raise InstanceReadError('Original instance field identities are ambiguous.')
                fields[key] = (buffer['name'], field)
    if not fields or 'isnan(' not in source: return source, []
    pattern = re.compile(r'(?P<structure>\w+)\[(?P<index>[^\]\n]+)\]\.(?P<field>\w+)(?P<swizzle>\.[xyzwrgba]{1,4})?')
    output, proofs = [], []
    for number, line in enumerate(source.splitlines(), 1):
        operands = []
        for call in re.finditer(r'\bisnan\s*\(', line):
            depth, end = 1, call.end()
            while end < len(line) and depth:
                depth += line[end] == '('; depth -= line[end] == ')'; end += 1
            if depth: raise InstanceReadError('Original NaN operand crosses an unaudited statement boundary.')
            operands.append((call.end(), end - 1))
        reads = {}
        for match in pattern.finditer(line):
            key = match['structure'], match['field']
            if key not in fields or not any(begin <= match.start() and match.end() <= end for begin, end in operands): continue
            buffer_name, field = fields[key]
            if field['type'] != 0 or field['rows'] != 1 or field['matrix'] or field['arraySize']:
                raise InstanceReadError('Original NaN instance operand lacks an audited floating scalar/vector layout.')
            width = len(match['swizzle']) - 1 if match['swizzle'] else field['columns']
            if width not in (1, 2, 3, 4): raise InstanceReadError('Original NaN instance operand width is unsupported.')
            reads[match[0]] = (buffer_name, width, field, match['structure'], match['index'])
        if reads and (not line.rstrip().endswith(';') or re.match(r'\s*(?:for|while)\b', line)):
            raise InstanceReadError('Original NaN instance read requires a proven ordinary native statement.')
        for expression, (buffer_name, width, field, structure, index) in reads.items():
            variable = 'QuestNativeNaNInstanceRead_' + str(len(proofs))
            if re.search(r'\b' + variable + r'\b', source): raise InstanceReadError('Native instance read temporary collides with original code.')
            kind = 'float' + (str(width) if width > 1 else '')
            indent = line[:len(line) - len(line.lstrip())]
            output.append(indent + kind + ' ' + variable + ' = ' + expression + ';')
            proofs.append(dict(originalBuffer=buffer_name, originalStructure=structure, originalField=field['name'],
                originalByteOffset=field['byteOffset'], originalIndexExpression=index, originalAccess=expression,
                valueType=kind, temporary=variable, sourceLine=number, replacements=line.count(expression),
                originalNaNMathChanged=False, originalAddressMathChanged=False))
            line = line.replace(expression, variable)
        output.append(line)
    return '\n'.join(output) + ('\n' if source.endswith('\n') else ''), proofs
