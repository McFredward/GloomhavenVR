#!/usr/bin/env python3
"""Module dependency graph, by type reference.

Builds: which top-level directory declares each type, then for each file counts
references to types declared in a DIFFERENT directory. Textual and therefore
approximate — a reference inside a comment is excluded (comments are masked) but
a type name that is also an ordinary English word will over-count. Types whose
name is shorter than 4 characters or that appear in more than one directory are
dropped rather than guessed at.
"""
import io, os, re, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hygiene_lib as H

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
DECL = re.compile(r'\b(?:class|struct|interface|enum|record)\s+([A-Z]\w{3,})')

owner = {}
dupes = set()
files = []
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
    for fn in sorted(filenames):
        if not fn.endswith('.cs'):
            continue
        p = os.path.join(dirpath, fn)
        rel = os.path.relpath(p, ROOT)
        mod = rel.split('/')[0] if '/' in rel else '(root)'
        files.append((rel, mod, p))
        lines = io.open(p, encoding='utf-8', errors='replace').readlines()
        mask = H.code_mask(lines)
        for i, raw in enumerate(lines):
            if not mask[i]:
                continue
            for m in DECL.finditer(H.strip_line(raw)):
                t = m.group(1)
                if t in owner and owner[t] != mod:
                    dupes.add(t)
                owner.setdefault(t, mod)
for t in dupes:
    owner.pop(t, None)

edges = collections.Counter()
detail = collections.defaultdict(collections.Counter)
bymod = collections.Counter()
for rel, mod, p in files:
    bymod[mod] += 1
    lines = io.open(p, encoding='utf-8', errors='replace').readlines()
    mask = H.code_mask(lines)
    seen = collections.Counter()
    for i, raw in enumerate(lines):
        if not mask[i]:
            continue
        for m in re.finditer(r'\b([A-Z]\w{3,})\b', H.strip_line(raw)):
            t = m.group(1)
            o = owner.get(t)
            if o and o != mod:
                seen[t] += 1
    for t, n in seen.items():
        edges[(mod, owner[t])] += n
        detail[(mod, owner[t])][t] += n

mods = sorted(bymod, key=lambda m: -bymod[m])
print('=== module -> module reference counts (own types only, %d types attributed, %d ambiguous dropped) ===' % (len(owner), len(dupes)))
w = max(len(m) for m in mods) + 1
print(' ' * w + ''.join('%9s' % m[:8] for m in mods))
for a in mods:
    print('%-*s' % (w, a) + ''.join('%9s' % (edges[(a, b)] or '.') for b in mods))

print('\n=== cycles between modules (both directions non-zero) ===')
for i, a in enumerate(mods):
    for b in mods[i + 1:]:
        if edges[(a, b)] and edges[(b, a)]:
            print('  %s <-> %s   (%d / %d)' % (a, b, edges[(a, b)], edges[(b, a)]))
            for lbl, (x, y) in (('  %s->%s' % (a, b), (a, b)), ('  %s->%s' % (b, a), (b, a))):
                top = ', '.join('%s(%d)' % (t, n) for t, n in detail[(x, y)].most_common(6))
                print('      %-16s %s' % (lbl.strip() + ':', top))
