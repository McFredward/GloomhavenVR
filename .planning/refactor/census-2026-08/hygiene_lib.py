#!/usr/bin/env python3
"""Rough structural census of the C# tree.

Not a compiler. A brace-depth walker that is good enough to rank files and
methods by size, nesting and parameter count. Every number it prints is a
POINTER to something to read, never a verdict on its own.
"""
import io, os, re, sys, json, collections


SIG = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?P<mods>(?:public|private|protected|internal|static|sealed|abstract|virtual|override|extern|unsafe|async|new|partial|readonly|const|ref|explicit|implicit|operator)\s+)*'
    r'(?P<rest>.+)$')

TYPE_DECL = re.compile(r'\b(class|struct|interface|enum|record)\s+([A-Za-z_]\w*)')
# a method signature: <type> <name>(<args>)  with no '=' before '(' and not a control keyword
METHOD = re.compile(r'^[^=;]*?\b(?P<name>[A-Za-z_]\w*)\s*(?:<[^()<>]*>)?\s*\((?P<args>[^;]*)$')
CTRL = {'if', 'for', 'foreach', 'while', 'switch', 'catch', 'using', 'lock', 'fixed', 'return',
        'new', 'do', 'else', 'yield', 'throw', 'get', 'set', 'nameof', 'typeof', 'sizeof',
        'default', 'checked', 'unchecked', 'when', 'where', 'select', 'from'}


def strip_line(s):
    """Remove // comments and string literals so braces inside them do not count."""
    out, i, n = [], 0, len(s)
    instr = None
    while i < n:
        c = s[i]
        if instr:
            if c == '\\' and instr != '@':
                i += 2
                continue
            if instr == '@' and c == '"':
                if i + 1 < n and s[i + 1] == '"':
                    i += 2
                    continue
                instr = None
            elif c == instr:
                instr = None
            i += 1
            continue
        if c == '/' and i + 1 < n and s[i + 1] == '/':
            break
        if c == '"':
            instr = '@' if (i and s[i - 1] == '@') else '"'
            i += 1
            continue
        if c == "'":
            instr = "'"
            i += 1
            continue
        out.append(c)
        i += 1
    return ''.join(out)


def walk(path):
    """Yield (kind, name, startline, endline, depth, maxnest, nargs)."""
    with io.open(path, encoding='utf-8', errors='replace') as fh:
        lines = fh.readlines()
    depth = 0
    stack = []          # (kind, name, startline, depth_at_open, maxnest)
    pending = None      # (kind, name, line, nargs) awaiting its '{'
    inblock = False
    for ln, raw in enumerate(lines, 1):
        s = raw
        if inblock:
            if '*/' in s:
                s = s.split('*/', 1)[1]
                inblock = False
            else:
                continue
        while '/*' in s:
            before, after = s.split('/*', 1)
            if '*/' in after:
                s = before + after.split('*/', 1)[1]
            else:
                s = before
                inblock = True
                break
        code = strip_line(s)
        bare = code.strip()
        if not bare:
            continue

        if pending is None:
            m = TYPE_DECL.search(code)
            if m and not bare.startswith('//'):
                pending = (m.group(1), m.group(2), ln, 0)
            else:
                mm = METHOD.match(code)
                if mm:
                    name = mm.group('name')
                    head = code[:mm.start('name')]
                    if (name not in CTRL and '=>' not in code.split('(')[0]
                            and '=' not in head and not bare.startswith('.')
                            and not bare.startswith('return')):
                        args = mm.group('args')
                        nargs = 0 if args.strip().startswith(')') else args.count(',') + 1
                        pending = ('method', name, ln, nargs)

        opens = code.count('{')
        closes = code.count('}')
        for _ in range(opens):
            depth += 1
            if pending is not None:
                stack.append([pending[0], pending[1], pending[2], depth, depth, pending[3]])
                pending = None
            else:
                stack.append(None)
            for fr in stack:
                if fr and depth > fr[4]:
                    fr[4] = depth
        if opens and pending is not None:
            pending = None
        if ';' in code and pending is not None and not opens:
            pending = None
        for _ in range(closes):
            if stack:
                fr = stack.pop()
                if fr:
                    yield (fr[0], fr[1], fr[2], ln, fr[3], fr[4] - fr[3], fr[5])
            depth = max(0, depth - 1)




def code_mask(lines):
    """True for lines that carry code (comments and blanks excluded)."""
    mask = [False] * len(lines)
    inblock = False
    for i, raw in enumerate(lines):
        s = raw
        if inblock:
            if '*/' in s:
                s = s.split('*/', 1)[1]
                inblock = False
            else:
                continue
        while '/*' in s:
            before, after = s.split('/*', 1)
            if '*/' in after:
                s = before + after.split('*/', 1)[1]
            else:
                s = before
                inblock = True
                break
        if strip_line(s).strip():
            mask[i] = True
    return mask
