#!/usr/bin/env python3
"""Which writes inside a diagnostic are LOAD-BEARING?

A `Log*`/`Census*`/`Report*` method that writes a field is not automatically a
problem: an instrument is allowed to own its own cadence stamp and its own
counters. It becomes load-bearing — and un-retirable, un-gateable — the moment
something that is NOT an instrument READS one of those fields.

So the test is:

    for every field F written inside a diagnostic method,
        is there a read of F anywhere outside a diagnostic method?

That is the exact question "can this instrument be switched off or deleted".

The reader search is textual and per-type-name-scoped: a field `_foo` declared in
type T is searched for within every file that declares a part of T. Textual, so
it OVER-reports (a same-named field in a sibling type counts). Over-reporting is
the correct direction: a false "load-bearing" costs a read, a false "safe" costs
a regression.
"""
import io, os, re, sys, collections

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import hygiene_lib as H

ROOT = sys.argv[1] if len(sys.argv) > 1 else '.'
DIAG = re.compile(r'^(Log|Census|Report|Dump|Describe|Diag|Trace|Probe|Audit|Explain|Print)')
DECL_LINE = re.compile(
    r'^\s*(?:(?:var|readonly|const|static|out|ref)\s+)*'
    r'[A-Za-z_][\w\.\<\>\?\[\], ]*?[\?\]\>\w]\s+[a-z_]\w*\s*(?:=[^=]|;|\bin\b)')
WRITE = re.compile(r'^\s*(?P<t>_?[A-Za-z_][\w\.\[\]\?]*)\s*(?:\+|-|\*|/|\|)?=(?!=)')
INCDEC = re.compile(r'^\s*(?P<t>_?[A-Za-z_][\w\.\[\]\?]*)\s*(?:\+\+|--)')

# ---------- pass 1: collect every method extent, and which are diagnostics ----------
extents = collections.defaultdict(list)     # rel -> [(a, b, name, isdiag)]
codemask, alllines = {}, {}
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if d not in ('obj', 'bin')]
    for fn in sorted(filenames):
        if not fn.endswith('.cs'):
            continue
        p = os.path.join(dirpath, fn)
        rel = os.path.relpath(p, ROOT)
        lines = io.open(p, encoding='utf-8', errors='replace').readlines()
        alllines[rel] = lines
        codemask[rel] = H.code_mask(lines)
        for kind, name, a, b, d, nest, nargs in H.walk(p):
            if kind == 'method':
                extents[rel].append((a, b, name, bool(DIAG.match(name))))

# ---------- pass 2: fields written inside a diagnostic ----------
written = collections.defaultdict(set)      # fieldname -> {(rel, method)}
for rel, spans in extents.items():
    lines, mask = alllines[rel], codemask[rel]
    for a, b, name, isdiag in spans:
        if not isdiag:
            continue
        # innermost wins: skip lines that belong to a nested non-diagnostic method
        for i in range(a, min(b, len(lines)) + 1):
            if not mask[i - 1]:
                continue
            s = H.strip_line(lines[i - 1])
            if DECL_LINE.match(s):
                continue
            m = WRITE.match(s) or INCDEC.match(s)
            if not m:
                continue
            t = m.group('t')
            root = re.split(r'[\.\[\?]', t)[0]
            if not root.startswith('_'):
                continue                    # only private fields; props/statics read separately
            written[root].add((rel, name, i))

# ---------- pass 3: readers outside any diagnostic ----------
loadbearing, selfcontained = {}, {}
for field, sites in written.items():
    owners = {rel for rel, _, _ in sites}
    pat = re.compile(r'(?<![\w.])' + re.escape(field) + r'(?![\w])')
    outside = []
    for rel, lines in alllines.items():
        mask = codemask[rel]
        spans = extents[rel]
        for i, raw in enumerate(lines, 1):
            if not mask[i - 1]:
                continue
            s = H.strip_line(raw)
            if not pat.search(s):
                continue
            # is this line a WRITE we already counted, inside a diagnostic?
            indiag = any(a <= i <= b and isdiag for a, b, nm, isdiag in spans)
            if indiag:
                continue
            # a declaration of the field itself is not a read
            if re.match(r'^\s*(?:(?:private|internal|public|protected|static|readonly|const|volatile)\s+)+', s) \
               and re.search(re.escape(field) + r'\s*(?:=|;)', s):
                continue
            outside.append((rel, i, ' '.join(s.split())[:120]))
    (loadbearing if outside else selfcontained)[field] = (sites, outside)

print('=== fields written inside diagnostic-named methods ===')
print('    %d fields total' % len(written))
print('    %d are READ from outside any diagnostic  -> LOAD-BEARING, cannot be gated off or deleted' % len(loadbearing))
print('    %d are touched only by diagnostics       -> self-contained instrument state\n' % len(selfcontained))

print('=== LOAD-BEARING, grouped by the diagnostic that writes them ===')
bym = collections.defaultdict(list)
for field, (sites, outside) in loadbearing.items():
    for rel, name, i in sites:
        bym[(rel, name)].append((field, len(outside), outside[0]))
for (rel, name), fs in sorted(bym.items(), key=lambda kv: -len(kv[1]))[:30]:
    print('  %s  ::  %s' % (rel, name))
    for field, n, first in sorted(fs)[:5]:
        print('        %-32s read %2d x outside, e.g. %s:%d' % (field, n, first[0], first[1]))
    if len(fs) > 5:
        print('        ... %d more fields' % (len(fs) - 5))
