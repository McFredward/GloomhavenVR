#!/usr/bin/env python3
"""Duplicate-block census, second attempt.

The first attempt reported 499 groups and every one of the top twenty was FALSE.
Two defects, both mine:

  1. It stripped string literals before comparing. In a localisation table every
     line is `["key"] = Pair("en", "de"),` — with the strings gone they all
     normalise to the SAME text, so Loc.cs reported itself as a 60-line clone of
     itself three times over. The strings ARE the payload there.
  2. It filtered bare `}` and `};` but not `},`. A nested initialiser tree
     (VROptionsTab's curated page) is a wall of `},` — so the tool found long
     runs of closing punctuation and called them duplicated logic.

Corrected: string literals are kept but folded to a stable digest of their
content (so two different strings differ and the same string matches), and any
line whose alphanumeric content is empty is dropped.
"""
import io, os, re, sys, hashlib, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hygiene_lib as H

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
WINDOW = int(sys.argv[2]) if len(sys.argv) > 2 else 12
STR = re.compile(r'@?"(?:[^"\\]|\\.)*"')
ALNUM = re.compile(r'[A-Za-z0-9_]')


def fold(raw):
    """Strip // comments only; keep strings but fold each to a short digest."""
    s = raw
    # remove a trailing // comment that is not inside a string
    out, i, n, instr = [], 0, len(s), None
    while i < n:
        c = s[i]
        if instr:
            if c == '\\' and instr == '"':
                out.append(s[i:i + 2]); i += 2; continue
            if c == instr:
                instr = None
            out.append(c); i += 1; continue
        if c == '/' and i + 1 < n and s[i + 1] == '/':
            break
        if c in '"\'':
            instr = c
        out.append(c); i += 1
    s = ''.join(out)
    s = STR.sub(lambda m: '"#%s"' % hashlib.md5(m.group(0).encode()).hexdigest()[:8], s)
    return ' '.join(s.split())


files = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
    for fn in sorted(filenames):
        if fn.endswith('.cs'):
            files.append(os.path.join(dirpath, fn))

norm = {}
for p in files:
    rel = os.path.relpath(p, ROOT)
    lines = io.open(p, encoding='utf-8', errors='replace').readlines()
    mask = H.code_mask(lines)
    out = []
    for i, raw in enumerate(lines):
        if not mask[i]:
            continue
        s = fold(raw)
        if not s or not ALNUM.search(s):
            continue          # pure punctuation: braces, `},`, `);`
        out.append((i + 1, s))
    norm[rel] = out

index = collections.defaultdict(list)
for rel, out in norm.items():
    for i in range(len(out) - WINDOW + 1):
        key = hashlib.md5('\n'.join(s for _, s in out[i:i + WINDOW]).encode()).hexdigest()
        index[key].append((rel, out[i][0], out[i + WINDOW - 1][0]))

groups, seen = [], set()
for k, v in index.items():
    if len(v) < 2:
        continue
    sig = tuple(sorted((r, a) for r, a, b in v))
    if sig in seen:
        continue
    seen.add(sig)
    groups.append(v)

groups.sort(key=lambda v: -(v[0][2] - v[0][1]))
kept, covered = [], collections.defaultdict(list)
for v in groups:
    if any(a >= ca and b <= cb for rel, a, b in v for ca, cb in covered[rel]):
        continue
    kept.append(v)
    for rel2, a2, b2 in v:
        covered[rel2].append((a2, b2))

print('=== duplicate runs of >= %d normalised code lines: %d groups ===' % (WINDOW, len(kept)))
cross = [v for v in kept if len({r for r, a, b in v}) > 1]
same = [v for v in kept if len({r for r, a, b in v}) == 1]
print('    %d span more than one file, %d are within a single file\n' % (len(cross), len(same)))
tot = 0
for v in kept[:45]:
    span = v[0][2] - v[0][1] + 1
    tot += span * (len(v) - 1)
    print('  %3d lines x %d sites:' % (span, len(v)))
    for rel, a, b in v[:5]:
        print('        %s:%d-%d' % (rel, a, b))
    if len(v) > 5:
        print('        ... and %d more' % (len(v) - 5))
print('\nredundant span in the listed groups: ~%d lines' % tot)
