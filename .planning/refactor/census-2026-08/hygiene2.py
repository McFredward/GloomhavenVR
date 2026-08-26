#!/usr/bin/env python3
"""Second pass: rank methods by CODE lines (comments and blanks excluded).

The first pass ranked by raw span, which in a tree that is 48 % comment measures
documentation, not complexity. It also mis-read multi-line attributes as
signatures. Both are corrected here; the attribute case is dropped by requiring
the line before '(' to not end inside a '[' .
"""
import io, os, re, sys, json, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hygiene_lib as H

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'


def main():
    rows = []
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
        for fn in sorted(filenames):
            if not fn.endswith('.cs'):
                continue
            p = os.path.join(dirpath, fn)
            rel = os.path.relpath(p, ROOT)
            lines = io.open(p, encoding='utf-8', errors='replace').readlines()
            codemask = H.code_mask(lines)
            for kind, name, a, b, d, nest, nargs in H.walk(p):
                if kind != 'method':
                    continue
                if a - 1 < len(lines) and lines[a - 1].lstrip().startswith('['):
                    continue
                cl = sum(1 for i in range(a - 1, min(b, len(lines))) if codemask[i])
                rows.append((cl, b - a + 1, nest, nargs, rel, name, a))
    json.dump(rows, open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'methods.json'), 'w'))
    rows.sort(reverse=True)
    print('=== METHOD size by CODE lines (%d methods) ===' % len(rows))
    for lo, hi in [(0, 20), (21, 50), (51, 100), (101, 200), (201, 10 ** 6)]:
        n = sum(1 for m in rows if lo <= m[0] <= hi)
        print('  %4d..%-6s %5d  %5.1f%%' % (lo, hi if hi < 10 ** 6 else 'inf', n, 100.0 * n / max(1, len(rows))))
    print('\n=== 40 largest by CODE lines (span in parens) ===')
    for cl, sp, nest, nargs, rel, name, a in rows[:40]:
        print('  %5d code (%5d span) nest%-3d %s:%d  %s' % (cl, sp, nest, rel, a, name))
    print('\n=== per-directory: methods over 100 code lines ===')
    per = collections.Counter()
    tot = collections.Counter()
    for cl, sp, nest, nargs, rel, name, a in rows:
        d = rel.split('/')[0] if '/' in rel else '(root)'
        tot[d] += 1
        if cl > 100:
            per[d] += 1
    for d in sorted(tot, key=lambda k: -per[k]):
        print('  %-12s %4d of %5d  (%.1f%%)' % (d, per[d], tot[d], 100.0 * per[d] / tot[d]))


main()
