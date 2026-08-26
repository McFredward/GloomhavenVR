#!/usr/bin/env python3
"""Which diagnostics carry load-bearing state — i.e. which of them cannot be switched off.

WHY THIS EXISTS
---------------
Instrumentation is the largest identifiable mass in this mod: 391 methods and ~14 900 code lines
named `Log*` / `Census*` / `Report*` / `Audit*` / `Diag*`, about 9.5 % of all method code, and
seven of the fifteen largest methods in the tree. Every hardware round adds one and almost none
are retired.

Retiring or gating one is also the single most dangerous edit available here, and the project has
already paid for it: deleting a spent `Log*` method once nearly latched the wall fade off forever,
because the method's body carried a load-bearing write. `.planning/refactor/PLAN-2026-08.md` §0.3.

So the question this answers is exactly:

    if this diagnostic stopped running, would anything ELSE behave differently?

which is true when the diagnostic WRITES a field that something that is NOT a diagnostic READS.

WHAT COUNTS AS "A DIAGNOSTIC"
----------------------------
Not the method's name alone. The name pattern (`Log*`, `Census*`, `Report*`, `Audit*`, …) is only
the SEED; a method is then promoted into the instrument set when every one of its callers is
already in it, iterated to a fixpoint.

That matters because the misses are systematic. The first run's reader methods split cleanly into
log-text builders and cadence gates — `AppendSkipClause`, `EmitShowEdgeAudit`, `CostClause`,
`WantSizeLog`, `PathAuditTallySignature` — and genuine mechanism: `CollectWallMountedProps`,
`StepRescanCycle`, `LateUpdate`, `Build`. Widening the regex to catch the first group means
guessing at prefixes the second group may use tomorrow. The call graph decides it instead.

The call search is textual and by bare method name, so a same-named method in another type ADDS
callers — which can only prevent a promotion, never cause one. A method with no visible caller
(Unity messages, Harmony patch bodies, reflection targets) is never promoted.

WHAT IS A READ, AND WHY THAT DEFINITION IS THE WHOLE TOOL
--------------------------------------------------------
A first version of this counted any mention of the field outside a diagnostic as a read, and
produced an upper bound of 197 fields that was mostly noise. Two systematic errors, both of which
matter and both of which are handled here:

  * `_a = _b = _c = 0;` — a chained reset. Only `_a` sits at the start of the line, so `_b` and
    `_c` were counted as READS of a field the diagnostic had just written. They are writes.
  * `ResetReport()` — a reset helper whose name does not match the diagnostic pattern, so its
    writes counted as outside reads. Again: writes.

An occurrence is therefore a READ unless it is the target of a plain or compound assignment
(anywhere in a `=` chain), the operand of `++`/`--`, or its own declaration. A compound assignment
counts as a write even though `_x += y` technically reads `_x`, because that value flows straight
back into `_x` and never escapes the instrument.

NOT A HARD GATE ON EXISTING CODE
--------------------------------
The load-bearing writes reported here are real code that ships and works. Failing the build on
them would mean breaking working code to satisfy a linter, which is not a trade this project
makes. So the accepted set lives in `.planning/refactor/INSTRUMENT-WRITES.baseline` and this
script fails only on something NEW.

The baseline is meant to shrink. Phase 5 of the plan works through it, and every entry ends as
one of four outcomes: gated and kept, retired, the write separated out into the mechanism, or
renamed so the name stops lying about what the method does.

USAGE
-----
    scripts/check-instrument-writes.py             exit 1 on a pair not in the baseline
    scripts/check-instrument-writes.py --baseline  rewrite the baseline from the current tree
    scripts/check-instrument-writes.py --report    print every pair with its reader sites
"""

from __future__ import annotations

import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
BASELINE = os.path.join(ROOT, ".planning", "refactor", "INSTRUMENT-WRITES.baseline")
SKIP_DIRS = {"obj", "bin"}

DIAG_NAME = re.compile(
    r"^(Log|Census|Report|Dump|Describe|Diag|Trace|Probe|Audit|Explain|Print)[A-Z_]|"
    r"^(Log|Census|Report|Dump|Describe|Diag|Trace|Probe|Audit|Explain|Print)$")

TYPE_DECL = re.compile(r"\b(?:class|struct|record)\s+([A-Za-z_]\w*)")
FIELD_DECL = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:(?:private|internal|public|protected|static|readonly|volatile|new|unsafe)\s+)+)"
    r"(?P<type>[A-Za-z_][\w\.\<\>\?\[\], ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*(?P<tail>[=;])")
METHOD_SIG = re.compile(r"^[^=;]*?\b(?P<name>[A-Za-z_]\w*)\s*(?:<[^()<>]*>)?\s*\((?P<rest>[^;]*)$")
CTRL = {"if", "for", "foreach", "while", "switch", "catch", "using", "lock", "fixed", "return",
        "new", "do", "else", "yield", "throw", "get", "set", "nameof", "typeof", "sizeof",
        "default", "checked", "unchecked", "when", "where", "select", "from", "add", "remove"}

COMPOUND = ("+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=", ">>=", "??=")
STATS = {"seed": 0, "promoted": 0, "total": 0, "methods": 0}
IDENT = re.compile(r"(?<![\w.])([A-Za-z_]\w*)")


# ------------------------------------------------------------------------------------------
#  Text handling
# ------------------------------------------------------------------------------------------
def code_lines(lines):
    """-> list of code-only text per line ('' for a blank or a comment line)."""
    out, inblock = [], False
    for raw in lines:
        s = raw
        if inblock:
            if "*/" in s:
                s = s.split("*/", 1)[1]
                inblock = False
            else:
                out.append("")
                continue
        while "/*" in s:
            before, after = s.split("/*", 1)
            if "*/" in after:
                s = before + after.split("*/", 1)[1]
            else:
                s, inblock = before, True
                break
        out.append(strip_line(s))
    return out


def strip_line(s):
    """Drop `//` comments and string/char literal CONTENT (keep the quotes as a placeholder)."""
    out, i, n, instr = [], 0, len(s), None
    while i < n:
        c = s[i]
        if instr:
            if c == "\\" and instr != "@":
                i += 2
                continue
            if instr == "@" and c == '"':
                if i + 1 < n and s[i + 1] == '"':
                    i += 2
                    continue
                instr = None
                out.append('"')
            elif c == instr:
                instr = None
                out.append('"' if c == '"' else "'")
            i += 1
            continue
        if c == "/" and i + 1 < n and s[i + 1] == "/":
            break
        if c == '"':
            instr = "@" if (i and s[i - 1] == "@") else '"'
            out.append('"')
            i += 1
            continue
        if c == "'":
            instr = "'"
            out.append("'")
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def split_assignments(stmt: str):
    """-> (targets, value_text) for one statement.

    Splits on top-level `=` that is not part of ==, !=, <=, >=, =>, or a compound operator, so a
    chain `_a = _b = _c = v` yields three targets. Text inside (), [], <> or quotes is never a
    target. A compound assignment yields its left side as a target and its right side as value.
    """
    depth_p = depth_b = 0
    instr = None
    parts, last = [], 0
    i, n = 0, len(stmt)
    while i < n:
        c = stmt[i]
        if instr:
            if c == instr:
                instr = None
            i += 1
            continue
        if c in "\"'":
            instr = c
            i += 1
            continue
        if c == "(":
            depth_p += 1
        elif c == ")":
            depth_p -= 1
        elif c == "[":
            depth_b += 1
        elif c == "]":
            depth_b -= 1
        elif c == "=" and depth_p == 0 and depth_b == 0:
            prev = stmt[i - 1] if i else ""
            nxt = stmt[i + 1] if i + 1 < n else ""
            if nxt == "=" or prev in "=!<>+-*/%&|^":
                i += 2 if nxt == "=" else 1
                continue
            if nxt == ">":
                i += 2
                continue
            parts.append(stmt[last:i])
            last = i + 1
        i += 1
    parts.append(stmt[last:])
    if len(parts) == 1:
        return [], parts[0]
    return parts[:-1], parts[-1]


def target_root(seg: str):
    """The field a target segment assigns to, or None.

    Only the ROOT identifier is written; anything inside an index or a call in the target
    (`_map[_key] = v`) is read, and is returned separately by the caller via `value` handling.
    """
    seg = seg.strip()
    for op in COMPOUND:
        if seg.endswith(op[:-1]):
            seg = seg[: -(len(op) - 1)].strip()
            break
    m = re.match(r"^([A-Za-z_]\w*)", seg)
    if not m:
        return None, seg
    return m.group(1), seg[m.end():]


# ------------------------------------------------------------------------------------------
#  Parse
# ------------------------------------------------------------------------------------------
class Part:
    __slots__ = ("rel", "type", "methods", "fields")

    def __init__(self, rel, type_):
        self.rel, self.type = rel, type_
        self.methods = []          # (name, a, b, is_diag)
        self.fields = {}           # name -> access ('private' | 'other')


def parse():
    parts = []
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in sorted(files):
            if not f.endswith(".cs"):
                continue
            full = os.path.join(root, f)
            rel = os.path.relpath(full, ROOT)
            with open(full, encoding="utf-8", errors="replace") as fh:
                lines = fh.readlines()
            code = code_lines(lines)
            parts.extend(parse_file(rel, code))
    return parts


def parse_file(rel, code):
    """Walk brace depth, attributing fields and methods to the innermost enclosing type."""
    found = []
    stack = []                     # entries: ('type', Part) or ('method', name, startline) or None
    depth = 0
    pending = None                 # ('type', name) | ('method', name, line)
    by_type = {}

    for ln, line in enumerate(code, 1):
        bare = line.strip()
        if bare:
            if pending is None:
                tm = TYPE_DECL.search(line)
                if tm:
                    pending = ("type", tm.group(1))
                else:
                    mm = METHOD_SIG.match(line)
                    if mm:
                        nm = mm.group("name")
                        head = line[: mm.start("name")]
                        if (nm not in CTRL and "=" not in head and "=>" not in line.split("(")[0]
                                and not bare.startswith(".") and not bare.startswith("[")):
                            pending = ("method", nm, ln)

            # a field declaration, attributed to the innermost open type
            fm = FIELD_DECL.match(line)
            if fm and stack:
                for ent in reversed(stack):
                    if ent and ent[0] == "type":
                        acc = "private" if "private" in fm.group("mods") else "other"
                        ent[1].fields.setdefault(fm.group("name"), acc)
                        break

        opens, closes = line.count("{"), line.count("}")
        for _ in range(opens):
            depth += 1
            if pending is not None and pending[0] == "type":
                p = by_type.get((rel, pending[1]))
                if p is None:
                    p = Part(rel, pending[1])
                    by_type[(rel, pending[1])] = p
                    found.append(p)
                stack.append(("type", p))
                pending = None
            elif pending is not None and pending[0] == "method":
                stack.append(("method", pending[1], pending[2]))
                pending = None
            else:
                stack.append(None)
        if opens:
            pending = None
        if ";" in line and pending is not None and not opens:
            pending = None
        for _ in range(closes):
            if stack:
                ent = stack.pop()
                if ent and ent[0] == "method":
                    for e2 in reversed(stack):
                        if e2 and e2[0] == "type":
                            nm = ent[1]
                            e2[1].methods.append((nm, ent[2], ln, bool(DIAG_NAME.match(nm))))
                            break
            depth = max(0, depth - 1)
    return found


# ------------------------------------------------------------------------------------------
#  Classify
# ------------------------------------------------------------------------------------------
def analyse():
    parts = parse()

    # type -> its parts, and type -> {field: access}
    per_type = defaultdict(list)
    fields_of = defaultdict(dict)
    for p in parts:
        per_type[p.type].append(p)
        fields_of[p.type].update(p.fields)

    # re-read code once, keyed by file
    code_of = {}
    for p in parts:
        if p.rel in code_of:
            continue
        with open(os.path.join(ROOT, p.rel), encoding="utf-8", errors="replace") as fh:
            code_of[p.rel] = code_lines(fh.readlines())

    # ---- who is an instrument: name pattern as the SEED, then a call-graph fixpoint ----
    all_methods = set()
    for p in parts:
        for nm, _a, _b, _isd in p.methods:
            all_methods.add(nm)
    instrument = {nm for p in parts for nm, _a, _b, isd in p.methods if isd}
    STATS["seed"] = len(instrument)

    # method extents per file, so a line can be attributed
    spans = defaultdict(list)
    for p in parts:
        for nm, a, b, isd in p.methods:
            spans[p.rel].append((a, b, nm, isd, p.type))

    def enclosing_name(rel, ln):
        best = None
        for a, b, nm, _isd, _ty in spans[rel]:
            if a <= ln <= b and (best is None or (a >= best[0] and b <= best[1])):
                best = (a, b, nm)
        return best[2] if best else None

    # callers[name] = {enclosing method names of every textual call site}
    callers = defaultdict(set)
    call_pat = re.compile(r"(?<![\w.])([A-Za-z_]\w*)\s*\(")
    for rel, code in code_of.items():
        for ln, line in enumerate(code, 1):
            if not line.strip():
                continue
            host = enclosing_name(rel, ln)
            for m in call_pat.finditer(line):
                nm = m.group(1)
                if nm in all_methods and nm != host:
                    callers[nm].add(host)          # None host = type-scope initialiser

    changed = True
    while changed:
        changed = False
        for nm in all_methods - instrument:
            cs = callers.get(nm)
            if cs and all(c in instrument for c in cs):
                instrument.add(nm)
                changed = True

    def is_instrument_span(entry):
        return entry is not None and entry[2] in instrument

    STATS["methods"] = len(all_methods)
    STATS["total"] = len(instrument)
    STATS["promoted"] = len(instrument) - STATS["seed"]

    def innermost(rel, ln):
        best = None
        for a, b, nm, isd, ty in spans[rel]:
            if a <= ln <= b and (best is None or (a >= best[0] and b <= best[1])):
                best = (a, b, nm, isd, ty)
        return best

    # ---- pass 1: fields written inside a diagnostic ----
    written = defaultdict(set)                 # (type, field) -> {(rel, method, line)}
    for p in parts:
        code = code_of[p.rel]
        for nm, a, b, isd in p.methods:
            if nm not in instrument:
                continue
            for ln in range(a, min(b, len(code)) + 1):
                line = code[ln - 1]
                if not line.strip():
                    continue
                inner = innermost(p.rel, ln)
                if not is_instrument_span(inner):
                    continue                    # a nested non-diagnostic local function
                for stmt in line.split(";"):
                    targets, _value = split_assignments(stmt)
                    for seg in targets:
                        rootname, _rest = target_root(seg)
                        if rootname and rootname in fields_of[p.type]:
                            written[(p.type, rootname)].add((p.rel, nm, ln))
                    for m in re.finditer(r"(?<![\w.])([A-Za-z_]\w*)\s*(?:\+\+|--)", stmt):
                        if m.group(1) in fields_of[p.type]:
                            written[(p.type, m.group(1))].add((p.rel, nm, ln))

    # ---- pass 2: reads of those fields outside any diagnostic ----
    result = {}
    for (ty, field), wsites in sorted(written.items()):
        access = fields_of[ty].get(field, "other")
        scope = {p.rel for p in per_type[ty]} if access == "private" else set(code_of)
        pat = re.compile(r"(?<![\w.])" + re.escape(field) + r"(?![\w])")
        readers = []
        for rel in sorted(scope):
            code = code_of[rel]
            for ln, line in enumerate(code, 1):
                if not line.strip() or not pat.search(line):
                    continue
                inner = innermost(rel, ln)
                if is_instrument_span(inner):
                    continue                    # inside the instrument: not an escape
                if FIELD_DECL.match(line) and re.search(re.escape(field) + r"\s*[=;]", line):
                    continue                    # the declaration itself
                # subtract every write target on this line, then see if the name survives
                consumed = 0
                for stmt in line.split(";"):
                    targets, value = split_assignments(stmt)
                    for seg in targets:
                        rootname, rest = target_root(seg)
                        if rootname == field:
                            consumed += 1
                        consumed += len(pat.findall(seg)) - len(pat.findall(rest)) - (
                            1 if rootname == field else 0)
                    for m in re.finditer(r"(?<![\w.])" + re.escape(field) + r"\s*(?:\+\+|--)", stmt):
                        consumed += 1
                    for m in re.finditer(r"(?:\+\+|--)\s*" + re.escape(field) + r"(?![\w])", stmt):
                        consumed += 1
                if len(pat.findall(line)) - consumed > 0:
                    readers.append((rel, ln, " ".join(line.split())[:110],
                                    inner[2] if inner else "<type scope>"))
        result[(ty, field)] = (sorted(wsites), readers)
    return result


# ------------------------------------------------------------------------------------------
#  Report
# ------------------------------------------------------------------------------------------
def key_of(ty, field, wsites):
    methods = sorted({m for _r, m, _l in wsites})
    return f"{ty}::{field} <- {','.join(methods)}"


def load_baseline():
    if not os.path.exists(BASELINE):
        return set()
    out = set()
    with open(BASELINE, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if line and not line.startswith("#"):
                out.add(line)
    return out


def main() -> int:
    res = analyse()
    bearing = {k: v for k, v in res.items() if v[1]}
    clean = {k: v for k, v in res.items() if not v[1]}

    if "--report" in sys.argv:
        for (ty, field), (wsites, readers) in sorted(bearing.items()):
            print(f"{ty}::{field}   written by {sorted({m for _r, m, _l in wsites})}")
            for rel, ln, text, meth in readers[:6]:
                print(f"    read  {rel}:{ln}  in {meth}()   {text}")
            if len(readers) > 6:
                print(f"    ... {len(readers) - 6} more reads")
        return 0

    keys = {key_of(ty, f, w) for (ty, f), (w, r) in bearing.items()}

    if "--baseline" in sys.argv:
        os.makedirs(os.path.dirname(BASELINE), exist_ok=True)
        with open(BASELINE, "w", encoding="utf-8") as fh:
            fh.write(HEADER)
            for k in sorted(keys):
                fh.write(k + "\n")
        print(f"instrument writes: baseline rewritten with {len(keys)} load-bearing pairs")
        return 0

    base = load_baseline()
    new = sorted(keys - base)
    gone = sorted(base - keys)
    print(f"instrument writes: {len(res)} fields written by diagnostics, "
          f"{len(bearing)} load-bearing, {len(clean)} instrument-only; baseline {len(base)}")
    print(f"  instrument set: {STATS['seed']} by name + {STATS['promoted']} promoted "
          f"by the call graph = {STATS['total']} of {STATS['methods']} methods")
    if gone:
        print(f"  {len(gone)} baseline entries no longer load-bearing (good — rebaseline when convenient)")
    if not new:
        return 0
    print()
    print("NEW LOAD-BEARING WRITE INSIDE A DIAGNOSTIC. This method can no longer be gated off or")
    print("retired without changing behaviour — the exact shape that once nearly latched the wall")
    print("fade off forever. Move the write into the mechanism, or accept it into the baseline")
    print(f"({os.path.relpath(BASELINE, ROOT)}) with --baseline and say why in the commit.")
    for k in new:
        ty, rest = k.split("::", 1)
        field = rest.split(" <- ")[0]
        _w, readers = res[(ty, field)]
        print(f"  {k}")
        for rel, ln, text, meth in readers[:3]:
            print(f"      read at {rel}:{ln} in {meth}()")
    return 1


HEADER = """\
# Diagnostics that carry load-bearing state — the accepted set.
#
# One line per (type::field <- the diagnostic methods that write it). An entry means: this
# `Log*`/`Census*`/`Report*` method CANNOT be switched off or deleted, because something that is
# not a diagnostic reads a field it writes. That is the shape that once nearly latched the wall
# fade off forever.
#
# This file is a WORK LIST, not a permission slip. Phase 5 of PLAN-2026-08.md works through it and
# every entry should end as one of: gated and kept, retired, the write moved into the mechanism,
# or the method renamed so its name stops lying. The file is expected to SHRINK.
#
# Regenerate with:  scripts/check-instrument-writes.py --baseline
# Inspect with:     scripts/check-instrument-writes.py --report
"""


if __name__ == "__main__":
    sys.exit(main())
