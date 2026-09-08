#!/usr/bin/env python3
"""Partial types: no static field initialiser may depend on another PART of its own type.

WHY THIS EXISTS
---------------
`.planning/refactor/LOG.md`, batch F, records the defect and the measurement behind it:

    MSBuild sorts glob results OrdinalIgnoreCase. `FlatScreen.Pointer.cs` sorts *before*
    `FlatScreen.cs` — so the obvious naming scheme silently reorders initialisers.

Static field initialisers run in **declaration order**, and across the parts of a partial type
that order is **compile order**, i.e. the order MSBuild's glob happened to produce. So renaming a
part, adding a part, or moving a file between directories can reorder two initialisers that have
never been near each other in a diff.

It has already happened once here. `CanvasConversion`'s decompiled field table came back with
`LastMaskExclusions` and `DiagSb` swapped after a split. The guard classified it `MOVED` — a
permutation of the same lines — and the pass condition of the day accepted it. Only tightening
the rule to an EMPTY diff caught it.

WHAT THIS CHECKS, AND WHY IT IS THE STRONGER PROPERTY
----------------------------------------------------
The first refactor's answer was a convention: digit-prefix the parts (`Foo.1.Bar.cs`,
`Foo.2.Baz.cs`) so the glob reproduces the intended order. That is a good convention and six
families in the tree do not follow it (`WallSegmentFade` has fifteen parts, none prefixed).

But a convention makes the order *stable*; it does not make the code *independent of* the order,
and it is enforced by nobody. So this checker asserts the stronger property instead:

    no static field initialiser in a multi-part type reaches — directly, or through a static
    method or expression-bodied member of the same type — a NON-CONST STATIC FIELD WITH AN
    INITIALISER that is declared in a different part.

When that holds, compile order cannot matter, and every part may be renamed, added or moved
freely. Today it holds for the whole tree.

Three exclusions carry the check, and each of them was forced by a false positive:

* **`const` is not a dependency.** A compile-time constant has no initialisation order to get
  wrong. `Defaults.HeldRollDegrees_ByStyle` looks like a violation and is not — the three values
  it reads are `const` (and in its own file besides).
* **An expression-bodied member is not a field initialiser.** `static bool Foo => _bar;` is
  evaluated on each READ. Written without excluding `=>`, this checker's first run reported seven
  of them and all seven were false. They are traversed instead, which is where they do matter: a
  real field initialiser that reads such a property evaluates it right there.
* **A cross-part METHOD CALL is not a dependency.** Methods have no initialisation order. The
  checker's first genuine hit was `VROptionsTab::DependencyParents <- VROptionsTab::Id`, where
  `Id` is `section + "/" + key` — a pure function that reads nothing. The traversal follows the
  method and judges what it *reads*; only a field can be reported.

WHAT IT DOES NOT CHECK
----------------------
* Static constructors. A `static Foo()` body runs as one unit after the field initialisers of its
  own part, and reasoning about it needs the type's whole lifecycle, not a text scan. None of the
  multi-part types in this tree has one; if one appears, this comment is the place to extend.
* Instance field initialisers. They run per construction in declaration order too, but a partial
  MonoBehaviour in this codebase is constructed by Unity from one place, so the ordering hazard
  that motivated this check does not arise.
* Whether the order is *correct* where a dependency is declared allowed. That is what the
  allowlist's `reason` field is for: it is a human's argument, recorded, not a machine's proof.
* A static field with NO initialiser. It is zero-initialised at type load before any initialiser
  runs, so reading it early yields the same default it would have anyway — there is no order to
  violate. Whether reading a default there is *correct* is an ordinary code question.

VERIFIED, NOT ASSUMED
---------------------
A checker that is green on the whole tree has to be shown capable of being red. This one was run
against a synthetic two-part type covering all four cases: a direct cross-part read (fires), a
`const` read (silent), a read through a static method (fires, naming the FIELD rather than the
method), and a call to a pure cross-part method (silent). Its first version passed the whole tree
while being structurally unable to fail — see the note on the `mods` groups.

ALLOWLIST
---------
If a cross-part dependency is genuinely wanted, it goes in `.planning/refactor/PARTIAL-ORDER.allow`
as one `Type::Member <- Type::Member | reason` line per dependency. An entry is a Tier-3 style
decision: it says "this type now depends on its own file names", and the reason has to say why
that was cheaper than moving the two members into one part.

USAGE
-----
    scripts/check-partial-order.py            exit 1 on an undeclared cross-part dependency
    scripts/check-partial-order.py --list     print every multi-part type and its compile order
"""

from __future__ import annotations

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
ALLOW = os.path.join(ROOT, ".planning", "refactor", "PARTIAL-ORDER.allow")
SKIP_DIRS = {"obj", "bin"}

PARTIAL_DECL = re.compile(r"\bpartial\s+(?:class|struct|record)\s+([A-Za-z_]\w*)")

# NOTE ON THE `mods` GROUPS BELOW: the `+` is INSIDE the named group on purpose. Written the
# obvious way — `(?P<mods>(?:static|readonly|...)\s+)+` — Python's repeated named group keeps
# only the LAST repetition, so `internal static readonly int Seed` yields mods='readonly ' and
# every `static readonly` field in the tree becomes invisible. The checker then reports "no
# violations" over the whole mod while being structurally unable to find one. Caught by a
# synthetic two-part positive control, not by the tree.
# A static field WITH AN INITIALISER; `const` is matched too so it can be EXCLUDED by name.
#
# `=(?![=>])` is load-bearing, and getting it wrong was this checker's first result: without
# excluding `>`, every EXPRESSION-BODIED member (`static bool Foo => _bar;`) reads as a field
# initialiser. Seven of them did, and all seven were false — an expression body is evaluated on
# each read, so it has no initialisation order to get wrong. They are picked up as members below
# instead, where they DO matter: a real field initialiser that calls such a property evaluates it
# at initialisation time, and the other part's field may not be assigned yet at that point.
FIELD = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:(?:private|internal|public|protected|static|readonly|const|volatile|new|unsafe)\s+)+)"
    r"(?P<type>[A-Za-z_][\w\.\<\>\?\[\], ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*=(?![=>])")
MEMBER = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:(?:private|internal|public|protected|static|readonly|const|volatile|new|unsafe|"
    r"abstract|virtual|override|sealed|async|extern|partial)\s+)+)"
    r"(?P<type>[A-Za-z_][\w\.\<\>\?\[\], ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*(?P<tail>[=;\(\{])")
EXPR_BODIED = re.compile(
    r"^\s*(?:\[[^\]]*\]\s*)*"
    r"(?P<mods>(?:(?:private|internal|public|protected|static|readonly|new|unsafe|"
    r"abstract|virtual|override|sealed|extern)\s+)+)"
    r"(?P<type>[A-Za-z_][\w\.\<\>\?\[\], ]*?)\s+"
    r"(?P<name>[A-Za-z_]\w*)\s*(?:\([^;{]*\))?\s*=>")
IDENT = re.compile(r"(?<![\w.])([A-Za-z_]\w*)")


STRING_LIT = re.compile(r'@"(?:[^"]|"")*"|"(?:\\.|[^"\\])*"', re.S)


def self_qualified(text: str, type_name: str) -> list[str]:
    """Identifiers reached through the type's OWN name — `WallSegmentFade.Foo`, and the
    namespace-qualified `GloomhavenVR.Core.WallSegmentFade.Foo`.

    IDENT deliberately refuses anything preceded by a dot, because `other.Thing` is a member of
    something else and following it would make this checker chase the whole tree. But a static
    member of the type being examined may be written either way inside its own body, and the
    multi-part types here do write the qualified form — it is how you disambiguate a static from
    a local. `static readonly int A = WallSegmentFade.B + 1;` is exactly the dependency this
    gate exists to find, and it was invisible: the 2026-09 tooling review planted it in a
    two-part type and the gate passed. Only the type's own name is followed; every other
    qualification stays out of scope, which is what keeps the traversal finite.

    STRING LITERALS ARE BLANKED HERE AND NOWHERE ELSE, and the first version of this function
    did not do it. strip_comments keeps strings on purpose — for IDENT that costs only false
    positives nobody sees, because a bare word in a string is rarely also a static member name.
    A DOTTED name in a string is a different population: this tree writes phase names and log
    tokens as `"ModalFallback.PreConvertHide"`, which is character-for-character what a
    self-qualified member reference looks like. Adding the qualified scan without this line
    turned `private static readonly string[] TickPhaseNames = { "ModalFallback.PreConvertHide",
    … }` into FOURTEEN cross-part dependencies on a tree that has none. Caught by running the
    real tree as the negative control immediately after the synthetic positive passed.
    """
    return re.findall(r"(?<![\w.])(?:[A-Za-z_]\w*\.)*" + re.escape(type_name) + r"\.([A-Za-z_]\w*)",
                      STRING_LIT.sub('""', text))


def strip_comments(text: str) -> str:
    """Remove comments, keep string literals (an identifier inside a string is not a reference,
    but keeping them costs only false positives and removing them needs a full lexer)."""
    out, i, n = [], 0, len(text)
    while i < n:
        c = text[i]
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            j = i + 2
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        j += 2
                        continue
                    j += 1
                    break
                j += 1
            out.append(text[i:j]); i = j; continue
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2; continue
                if text[j] == '"':
                    j += 1; break
                j += 1
            out.append(text[i:j]); i = j; continue
        if c == "'":
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2; continue
                if text[j] == "'":
                    j += 1; break
                j += 1
            out.append(text[i:j]); i = j; continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
            continue
        out.append(c); i += 1
    return "".join(out)


def sources():
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in sorted(files):
            if f.endswith(".cs"):
                full = os.path.join(root, f)
                yield full, os.path.relpath(full, ROOT)


def balance(s: str) -> int:
    return s.count("{") - s.count("}")


def scan():
    """-> (types, unscannable)

    types: name -> {part_rel -> {'fields': {n: (expr, is_const, line)},
                                 'members': {n: is_const},
                                 'methods': {n: body_text}}}
    unscannable: [(rel, line, type_name)] — a partial type whose body opens AND closes on one
    line. The walker is line-based, so such a body is invisible to it; reporting it is the only
    honest option, because silently skipping is indistinguishable from finding nothing.
    """
    types: dict[str, dict] = {}
    unscannable: list[tuple[str, int, str]] = []
    for full, rel in sources():
        with open(full, encoding="utf-8", errors="replace") as fh:
            raw = fh.read()
        code = strip_comments(raw)
        lines = code.splitlines()

        # which partial types does this file declare, and at what brace depth do they open
        open_types: list[tuple[str, int]] = []
        depth = 0
        pending: str | None = None
        for ln, line in enumerate(lines, 1):
            m = PARTIAL_DECL.search(line)
            if m and pending is None:
                pending = m.group(1)
            opens = line.count("{")
            closes = line.count("}")
            for _ in range(opens):
                depth += 1
                if pending is not None:
                    open_types.append((pending, depth))
                    pending = None
            if opens and pending is not None:
                pending = None
            for _ in range(closes):
                if open_types and open_types[-1][1] == depth:
                    popped, _d = open_types.pop()
                    if opens and depth >= 1:
                        # opened and closed within this same line
                        unscannable.append((rel, ln, popped))
                depth = max(0, depth - 1)

            if not open_types:
                continue
            tname = open_types[-1][0]
            bucket = types.setdefault(tname, {}).setdefault(
                rel, {"fields": {}, "members": {}, "methods": {}})

            fm = FIELD.match(line)
            if fm:
                mods = fm.group("mods")
                if "static" in mods or "const" in mods:
                    name = fm.group("name")
                    is_const = "const" in mods
                    # gather the initialiser expression, possibly across lines
                    expr = line.split("=", 1)[1]
                    k = ln
                    while ";" not in expr and k < len(lines):
                        expr += "\n" + lines[k]
                        k += 1
                    bucket["members"][name] = is_const
                    if not is_const:
                        bucket["fields"][name] = (expr, is_const, ln)
                continue

            # An expression-bodied member: `static T Foo => expr;` (the expression may wrap).
            # Recorded as a traversable body, because a field initialiser that reads it evaluates
            # it right there and then the order DOES matter.
            em = EXPR_BODIED.match(line)
            if em and "static" in em.group("mods"):
                name = em.group("name")
                bucket["members"][name] = False
                body, k = line.split("=>", 1)[1], ln
                while ";" not in body and k < len(lines):
                    body += "\n" + lines[k]
                    k += 1
                bucket["methods"][name] = body
                continue

            mm = MEMBER.match(line)
            if mm and "static" in mm.group("mods"):
                name = mm.group("name")
                bucket["members"][name] = "const" in mm.group("mods")
                if mm.group("tail") in "({":
                    # capture the body so a dependency THROUGH a static method is seen
                    body, d, k = "", 0, ln - 1
                    started = False
                    while k < len(lines):
                        body += lines[k] + "\n"
                        d += balance(lines[k])
                        if "{" in lines[k]:
                            started = True
                        if started and d <= 0:
                            break
                        if not started and ";" in lines[k]:
                            break
                        k += 1
                        if k - ln > 400:
                            break
                    bucket["methods"][name] = body
    return types, unscannable


def load_allow() -> dict[str, str]:
    allowed = {}
    if not os.path.exists(ALLOW):
        return allowed
    with open(ALLOW, encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            key = line.split("|", 1)[0].strip()
            allowed[key] = line.split("|", 1)[1].strip() if "|" in line else ""
    return allowed


def main() -> int:
    types, unscannable = scan()
    multi = {t: parts for t, parts in types.items() if len(parts) > 1}

    if "--list" in sys.argv:
        for t in sorted(multi):
            print(f"{t}  ({len(multi[t])} parts, in MSBuild OrdinalIgnoreCase glob order)")
            for rel in sorted(multi[t], key=lambda r: r.lower()):
                f = multi[t][rel]
                print(f"    {rel}    {len(f['fields'])} static initialisers")
        return 0

    if unscannable:
        print("A PARTIAL TYPE'S BODY OPENS AND CLOSES ON ONE LINE, and this checker walks lines,")
        print("so its members were never examined. Put the body on its own lines — a checker that")
        print("silently skips is indistinguishable from one that finds nothing.")
        for rel, ln, name in unscannable:
            print(f"  {rel}:{ln}  partial {name}")
        return 1

    allowed = load_allow()
    violations = []
    for t, parts in sorted(multi.items()):
        # `home` is every static member, so an identifier can be recognised as belonging to this
        # type at all. `init_home` is the subset that actually has an ORDER: a non-const static
        # field WITH an initialiser. Only that subset can be reported — see the note below.
        home: dict[str, str] = {}
        is_const: dict[str, bool] = {}
        methods: dict[str, tuple[str, str]] = {}
        init_home: dict[str, tuple[str, int]] = {}
        for rel, f in parts.items():
            for n, c in f["members"].items():
                home.setdefault(n, rel)
                is_const[n] = c
            for n, body in f["methods"].items():
                methods.setdefault(n, (rel, body))
            for n, (_expr, _c, fln) in f["fields"].items():
                init_home.setdefault(n, (rel, fln))

        for rel, f in parts.items():
            for fname, (expr, _c, ln) in f["fields"].items():
                # transitive closure over same-type static methods, one type only
                seen_m: set[str] = set()
                frontier = [expr]
                refs: set[str] = set()
                while frontier:
                    text = frontier.pop()
                    for ident in IDENT.findall(text) + self_qualified(text, t):
                        if ident not in home:
                            continue
                        refs.add(ident)
                        if ident in methods and ident not in seen_m:
                            seen_m.add(ident)
                            frontier.append(methods[ident][1])
                for r in sorted(refs):
                    if is_const.get(r, False):
                        continue          # compile-time: no initialisation order exists
                    if r == fname:
                        continue
                    # A METHOD (or expression-bodied member) has no initialisation order of its
                    # own — calling one across a part boundary is harmless however MSBuild sorted
                    # the glob. It was already TRAVERSED above, so anything it reads is judged on
                    # its own. Only a field with an initialiser can hold a default here.
                    if r not in init_home:
                        continue
                    where, _decl_line = init_home[r]
                    if where == rel:
                        continue          # same part: ordinary declaration order, visible in one diff
                    key = f"{t}::{fname} <- {t}::{r}"
                    if key in allowed:
                        continue
                    violations.append((t, fname, rel, ln, r, where, key))

    # A FLOOR: zero multi-part types is not a clean tree, it is a tree nobody read. With no
    # types there are no initialisers, no violations and a green exit — the same shape as the
    # relative-path defect this project has already paid for once (LOG-2026-08 Phase 1). This
    # tree has had at least twenty multi-part types since the 2026-08 folder restructure.
    if len(multi) < 10:
        print(f"error: only {len(multi)} multi-part types were found under "
              f"{os.path.relpath(SRC, ROOT)} — this tree has ~20.", file=sys.stderr)
        print("       A census that found nothing cannot fail, so this is a path problem, not a",
              file=sys.stderr)
        print("       clean bill of health.", file=sys.stderr)
        return 1

    print(f"partial order: {len(multi)} multi-part types, "
          f"{sum(len(p) for p in multi.values())} parts, "
          f"{len(allowed)} declared cross-part dependencies")
    if not violations:
        print("no static field initialiser depends on another part of its own type.")
        return 0

    print()
    print("UNDECLARED CROSS-PART STATIC INITIALISER DEPENDENCY — compile order (i.e. the FILE NAMES)")
    print("now decides a value. Move the two members into one part, make the dependency `const`, or")
    print(f"declare it with a reason in {os.path.relpath(ALLOW, ROOT)}.")
    for t, fname, rel, ln, r, where, key in violations:
        print(f"  {key}")
        print(f"      initialiser  {rel}:{ln}")
        print(f"      depends on   {where}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
