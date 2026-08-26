#!/usr/bin/env python3
"""Rough structural census of the C# tree.

Not a compiler. A brace-depth walker that is good enough to rank files and
methods by size, nesting and parameter count. Every number it prints is a
POINTER to something to read, never a verdict on its own.
"""
import io, os, re, sys, json, collections

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'

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


def main():
    methods, types, perfile = [], [], collections.Counter()
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in filenames:
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dirpath, fn)
            rel = os.path.relpath(p, ROOT)
            for kind, name, a, b, d, nest, nargs in walk(p):
                if kind == 'method':
                    methods.append((b - a + 1, nest, nargs, rel, name, a))
                    perfile[rel] += 1
                else:
                    types.append((b - a + 1, rel, kind, name, a))
    json.dump({'methods': methods, 'types': types}, open(
        os.path.join(os.path.dirname(os.path.abspath(__file__)), 'hygiene.json'), 'w'))

    methods.sort(reverse=True)
    print('=== METHOD LENGTH distribution (%d methods) ===' % len(methods))
    for lo, hi in [(0, 20), (21, 50), (51, 100), (101, 200), (201, 400), (401, 10 ** 6)]:
        n = sum(1 for m in methods if lo <= m[0] <= hi)
        print('  %4d..%-6s %5d  %5.1f%%' % (lo, hi if hi < 10 ** 6 else 'inf', n, 100.0 * n / max(1, len(methods))))
    print('\n=== 30 LONGEST methods ===')
    for L, nest, nargs, rel, name, a in methods[:30]:
        print('  %5d lines  nest%-3d args%-3d %s:%d  %s' % (L, nest, nargs, rel, a, name))

    print('\n=== 20 DEEPEST nesting ===')
    for L, nest, nargs, rel, name, a in sorted(methods, key=lambda m: -m[1])[:20]:
        print('  nest %-3d %5d lines  %s:%d  %s' % (nest, L, rel, a, name))

    print('\n=== 15 most PARAMETERS ===')
    for L, nest, nargs, rel, name, a in sorted(methods, key=lambda m: -m[2])[:15]:
        print('  args %-3d %5d lines  %s:%d  %s' % (nargs, L, rel, a, name))

    types.sort(reverse=True)
    print('\n=== 25 LARGEST types ===')
    for L, rel, kind, name, a in types[:25]:
        print('  %6d lines  %-9s %s:%d  %s' % (L, kind, rel, a, name))


main()
