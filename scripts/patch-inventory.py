#!/usr/bin/env python3
"""Derive the Harmony patch surface from source — inventory + wiring check.

WHY THIS EXISTS
---------------
Two blind spots the compiler and `refactor-guard.sh` both have, by construction:

1. `docs/PATCH-INVENTORY.md` is cited by CHARTER §5 as the authority for
   "is this a patch target?" — i.e. it is consulted before deleting anything.
   It was hand-written once, in Phase 5, and then drifted: it listed 17 patched
   methods while the repository declared 60 `[HarmonyPatch]` attributes. A doc
   nobody can verify is worse than no doc, because it is trusted.

2. A patch class with no `PatchAll(typeof(X))` reference compiles cleanly, ships,
   and does nothing. That has happened twice in this project's history
   (`8567af4 wire(compat): register WallFadeDisable`, `57309c5 wire(compat):
   register InitialInputSkip`). The build is green, the guard diff is empty, and
   the only symptom is on the headset.

Both are answered by parsing the source, which is why this is a script and not a
runtime audit. A runtime audit was designed and rejected (REVIEW-Hands-Board-Core
§P1): it cannot run before `EscMenuInputBlock`'s lazy `PatchAll`, both historical
misses declare `TargetMethod` so it could only ever Warn, and it would add a type
to the shipped DLL. A static parse sees every call site including the lazy one,
fails at commit time, and adds no IL.

MODES
-----
    patch-inventory.py generate   rewrite docs/PATCH-INVENTORY.md from source
    patch-inventory.py check      exit 1 on drift, on an unregistered class,
                                  or on a double registration

DESIGN NOTE — why it hard-errors on an unknown attribute form
-------------------------------------------------------------
A parser that silently skips what it does not understand turns "you forgot to
register a patch" into "the inventory quietly got shorter". Every
`[HarmonyPatch(...)]` argument this script cannot classify is a hard error naming
the file and line, so an unrecognised form stops the commit instead of shrinking
the answer.
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src"
DOC = ROOT / "docs" / "PATCH-INVENTORY.md"

# Harmony's own name-convention fallback when a patch method carries no explicit
# [HarmonyPrefix]/[HarmonyPostfix]/... attribute.
KIND_ATTRS = {
    "HarmonyPrefix": "prefix",
    "HarmonyPostfix": "postfix",
    "HarmonyTranspiler": "transpiler",
    "HarmonyFinalizer": "finalizer",
    "HarmonyReversePatch": "reverse",
}
KIND_NAMES = {
    "Prefix": "prefix",
    "Postfix": "postfix",
    "Transpiler": "transpiler",
    "Finalizer": "finalizer",
}
# Auxiliary methods Harmony calls on a patch class; they are not patch methods.
AUX_NAMES = {"TargetMethod", "TargetMethods", "Prepare", "Cleanup"}

TYPE_KEYWORDS = ("class", "struct", "interface", "record", "enum")


class ParseError(Exception):
    pass


# ---------------------------------------------------------------------------
# 1. Comment stripping that preserves byte offsets (so line numbers stay true)
# ---------------------------------------------------------------------------

def strip_comments(text: str) -> str:
    """Blank out comments, keeping every other character at its original index.

    String and char literals are walked over intact — patch targets for private
    members are string literals ("CommonLoop"), so they must survive.
    """
    out = list(text)
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            while i < n and text[i] != '\n':
                out[i] = ' '
                i += 1
        elif c == '/' and i + 1 < n and text[i + 1] == '*':
            while i < n and not (text[i] == '*' and i + 1 < n and text[i + 1] == '/'):
                if text[i] != '\n':
                    out[i] = ' '
                i += 1
            for _ in range(2):
                if i < n:
                    out[i] = ' '
                    i += 1
        elif c == '@' and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
        elif c == '"':
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
        elif c == "'":
            i += 1
            while i < n:
                if text[i] == '\\':
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
        else:
            i += 1
    return ''.join(out)


# ---------------------------------------------------------------------------
# 2. Declaration scanner
# ---------------------------------------------------------------------------

@dataclass
class Decl:
    kind: str                    # "type" | "method"
    name: str
    outer: tuple[str, ...]       # enclosing type names, outermost first
    file: Path
    line: int
    attrs: list[str] = field(default_factory=list)   # raw attribute texts


def _matching(text: str, start: int, open_c: str, close_c: str) -> int:
    """Index just past the bracket that closes the one at `start`."""
    depth = 0
    i = start
    while i < len(text):
        if text[i] == open_c:
            depth += 1
        elif text[i] == close_c:
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    raise ParseError(f"unbalanced {open_c} at offset {start}")


TYPE_RE = re.compile(
    r'\b(?:' + '|'.join(TYPE_KEYWORDS) + r')\s+([A-Za-z_]\w*)')
# A method declaration: the last `Name(` before the body, with no `=` in between
# (which would make it a field/property initialiser).
METHOD_RE = re.compile(r'([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\($')


def scan_file(path: Path) -> list[Decl]:
    raw = path.read_text(encoding='utf-8')
    text = strip_comments(raw)
    line_of = _line_index(text)

    decls: list[Decl] = []
    stack: list[str] = []          # enclosing TYPE names only
    depth_kinds: list[str] = []    # what each open brace level is
    pending: list[str] = []        # attribute texts awaiting a declaration
    buf_start = 0
    i, n = 0, len(text)

    while i < n:
        c = text[i]
        if c == '[':
            head = text[buf_start:i].strip()
            if head == '' or head.endswith(']'):
                end = _matching(text, i, '[', ']')
                pending.append(text[i:end])
                i = end
                buf_start = i
                continue
            i = _matching(text, i, '[', ']')
            continue
        if c == '(':
            i = _matching(text, i, '(', ')')
            continue
        if c == '{':
            head = text[buf_start:i]
            kind, name = _classify(head)
            if kind == 'type':
                decls.append(Decl('type', name, tuple(stack), path, line_of(i), pending))
                stack.append(name)
                depth_kinds.append('type')
            elif kind == 'method':
                decls.append(Decl('method', name, tuple(stack), path, line_of(i), pending))
                depth_kinds.append('block')
            else:
                depth_kinds.append('block')
            pending = []
            i += 1
            buf_start = i
            continue
        if c == '}':
            if depth_kinds:
                if depth_kinds.pop() == 'type' and stack:
                    stack.pop()
            pending = []
            i += 1
            buf_start = i
            continue
        if c == ';':
            head = text[buf_start:i]
            # expression-bodied member: `... Name(...) => expr;`
            if '=>' in head:
                kind, name = _classify(head.split('=>')[0])
                if kind == 'method':
                    decls.append(Decl('method', name, tuple(stack), path, line_of(i), pending))
            pending = []
            i += 1
            buf_start = i
            continue
        i += 1
    return decls


def _line_index(text: str):
    starts = [0]
    for m in re.finditer('\n', text):
        starts.append(m.end())

    def line_of(off: int) -> int:
        lo, hi = 0, len(starts) - 1
        while lo < hi:
            mid = (lo + hi + 1) // 2
            if starts[mid] <= off:
                lo = mid
            else:
                hi = mid - 1
        return lo + 1
    return line_of


def _classify(head: str) -> tuple[str, str]:
    """Decide what a declaration head (text before `{` or `=>`) declares."""
    h = ' '.join(head.split())
    if not h:
        return ('other', '')
    m = TYPE_RE.search(h)
    if m and '(' not in h[:m.start()]:
        return ('type', m.group(1))
    # Method: the head must end at a parameter list. Because `(` runs are skipped
    # wholesale by the scanner, `head` here never contains the parameter list —
    # it ends right before it. Reconstruct by looking for a trailing identifier.
    if h.endswith(')'):
        # strip the parameter list (balanced) to expose the name
        d = 0
        for idx in range(len(h) - 1, -1, -1):
            if h[idx] == ')':
                d += 1
            elif h[idx] == '(':
                d -= 1
                if d == 0:
                    h = h[:idx]
                    break
        m = METHOD_RE.search(h + '(')
        if m and '=' not in h.split(m.group(1))[0][-2:]:
            return ('method', m.group(1))
    return ('other', '')


# ---------------------------------------------------------------------------
# 3. Attribute argument parsing
# ---------------------------------------------------------------------------

@dataclass
class Target:
    decl_type: str | None = None
    method: str | None = None
    method_type: str | None = None      # Getter / Setter / Constructor / ...
    arg_types: list[str] = field(default_factory=list)
    private: bool = False               # target named by string literal

    def merge(self, other: 'Target') -> 'Target':
        return Target(
            decl_type=other.decl_type or self.decl_type,
            method=other.method or self.method,
            method_type=other.method_type or self.method_type,
            arg_types=other.arg_types or self.arg_types,
            private=other.private or self.private,
        )


def split_args(s: str) -> list[str]:
    args, depth, cur = [], 0, []
    for ch in s:
        if ch in '([{':
            depth += 1
        elif ch in ')]}':
            depth -= 1
        if ch == ',' and depth == 0:
            args.append(''.join(cur).strip())
            cur = []
        else:
            cur.append(ch)
    if ''.join(cur).strip():
        args.append(''.join(cur).strip())
    return args


# `typeof(...)` may contain parentheses of its own — the game's
# `UITextInfoPanel.Show` takes a `(string, string)[]` tuple array — so the inner
# text is matched greedily and balance is confirmed by the anchors.
TYPEOF_RE = re.compile(r'^typeof\(\s*(.+?)\s*\)$', re.S)
NAMEOF_RE = re.compile(r'^nameof\(\s*([\w.]+)\s*\)$')
STRING_RE = re.compile(r'^"([^"]*)"$')
METHODTYPE_RE = re.compile(r'^MethodType\.(\w+)$')
ARGTYPES_RE = re.compile(r'^(?:new\s*(?:Type)?\s*\[\s*\]|new\s+Type\[\d*\])\s*\{(.*)\}$', re.S)


def parse_harmony_patch(attr_body: str, where: str) -> Target:
    """Parse the arguments of one `[HarmonyPatch(...)]`."""
    t = Target()
    body = attr_body.strip()
    if not body:
        return t
    for arg in split_args(body):
        if m := TYPEOF_RE.match(arg):
            inner = m.group(1)
            if t.decl_type is None:
                t.decl_type = inner.split('.')[-1] if '.' in inner else inner
            else:
                t.arg_types.append(inner)
        elif m := NAMEOF_RE.match(arg):
            t.method = m.group(1).split('.')[-1]
        elif m := STRING_RE.match(arg):
            t.method = m.group(1)
            t.private = True
        elif m := METHODTYPE_RE.match(arg):
            t.method_type = m.group(1)
        elif m := ARGTYPES_RE.match(arg):
            t.arg_types = [a.strip() for a in split_args(m.group(1)) if a.strip()]
        else:
            raise ParseError(
                f"{where}: unrecognised [HarmonyPatch] argument {arg!r}.\n"
                "  This script hard-errors rather than skipping, so an unknown\n"
                "  attribute form can never silently shrink the inventory.\n"
                "  Teach parse_harmony_patch() the new form.")
    return t


ATTR_ITEMS_RE = re.compile(r'(Harmony\w*)\s*(\((.*?)\))?\s*(?=,|$)', re.S)


def harmony_attrs(attr_text: str) -> list[tuple[str, str]]:
    """Yield (name, body) for each Harmony* attribute inside one `[...]` group."""
    inner = attr_text.strip()[1:-1].strip()
    out = []
    for piece in split_args(inner):
        piece = piece.strip()
        m = re.match(r'^(Harmony\w*)\s*(?:\((.*)\))?\s*$', piece, re.S)
        if m:
            out.append((m.group(1), m.group(2) or ''))
    return out


# ---------------------------------------------------------------------------
# 4. Model
# ---------------------------------------------------------------------------

@dataclass
class PatchMethod:
    name: str
    kind: str
    target: Target
    line: int


@dataclass
class PatchClass:
    name: str
    outer: tuple[str, ...]
    file: Path
    line: int
    class_target: Target
    methods: list[PatchMethod] = field(default_factory=list)
    has_class_attr: bool = False
    has_target_method: bool = False
    registrations: list[tuple[Path, int]] = field(default_factory=list)

    @property
    def full_name(self) -> str:
        return '.'.join(self.outer + (self.name,))

    @property
    def module(self) -> str:
        rel = self.file.relative_to(SRC / "GloomhavenVR")
        return rel.parts[0] if len(rel.parts) > 1 else "Plugin"


def collect() -> list[PatchClass]:
    classes: list[PatchClass] = []
    for path in sorted(SRC.rglob('*.cs')):
        if 'obj' in path.parts or 'bin' in path.parts:
            continue
        decls = scan_file(path)
        # index types by their (outer, name) so members can find their owner
        by_key: dict[tuple[tuple[str, ...], str], PatchClass] = {}
        for d in decls:
            if d.kind != 'type':
                continue
            groups = [g for a in d.attrs for g in harmony_attrs(a)]
            hp = [g for g in groups if g[0] == 'HarmonyPatch']
            if not hp:
                continue
            tgt = Target()
            for _, body in hp:
                tgt = tgt.merge(parse_harmony_patch(body, f"{path}:{d.line}"))
            pc = PatchClass(d.name, d.outer, path, d.line, tgt, has_class_attr=True)
            by_key[(d.outer, d.name)] = pc
            classes.append(pc)
        for d in decls:
            if d.kind != 'method' or not d.outer:
                continue
            owner = by_key.get((d.outer[:-1], d.outer[-1]))
            groups = [g for a in d.attrs for g in harmony_attrs(a)]
            if owner is not None and d.name in AUX_NAMES:
                owner.has_target_method = True
                continue
            hp = [g for g in groups if g[0] == 'HarmonyPatch']
            kinds = [KIND_ATTRS[g[0]] for g in groups if g[0] in KIND_ATTRS]
            # Harmony recognises a patch method three ways: an explicit kind
            # attribute, a [HarmonyPatch] on the method, or — the most common form
            # in this repo — the bare name convention `Prefix`/`Postfix`/
            # `Transpiler`/`Finalizer` inside a class that carries the target.
            by_convention = owner is not None and d.name in KIND_NAMES
            if not hp and not kinds and not by_convention:
                continue
            if owner is None:
                raise ParseError(
                    f"{path}:{d.line}: method {d.name} carries Harmony attributes but its\n"
                    f"  enclosing type {'.'.join(d.outer)} has no class-level [HarmonyPatch].\n"
                    "  Harmony's PatchClassProcessor ignores such a class entirely — the\n"
                    "  patch would ship INERT. Add [HarmonyPatch] to the class.")
            tgt = owner.class_target
            for _, body in hp:
                tgt = tgt.merge(parse_harmony_patch(body, f"{path}:{d.line}"))
            kind = kinds[0] if kinds else KIND_NAMES.get(d.name, 'unknown')
            owner.methods.append(PatchMethod(d.name, kind, tgt, d.line))
    return classes


PATCHALL_RE = re.compile(r'PatchAll\s*\(\s*typeof\(\s*([\w.]+)\s*\)\s*\)')


def find_registrations(classes: list[PatchClass]) -> None:
    by_simple: dict[str, list[PatchClass]] = {}
    for c in classes:
        by_simple.setdefault(c.name, []).append(c)
    for path in sorted(SRC.rglob('*.cs')):
        if 'obj' in path.parts or 'bin' in path.parts:
            continue
        text = strip_comments(path.read_text(encoding='utf-8'))
        line_of = _line_index(text)
        for m in PATCHALL_RE.finditer(text):
            simple = m.group(1).split('.')[-1]
            hits = by_simple.get(simple)
            if not hits:
                raise ParseError(
                    f"{path}:{line_of(m.start())}: PatchAll(typeof({m.group(1)})) names a type\n"
                    "  that declares no [HarmonyPatch] — it patches nothing.")
            if len(hits) > 1:
                raise ParseError(
                    f"{path}:{line_of(m.start())}: PatchAll(typeof({m.group(1)})) is ambiguous —\n"
                    f"  {len(hits)} patch classes share the simple name {simple!r}.")
            hits[0].registrations.append((path, line_of(m.start())))


# ---------------------------------------------------------------------------
# 5. Rendering
# ---------------------------------------------------------------------------

MODULE_LABEL = {
    "Board": "Board", "Cards": "Cards", "Compat": "Compat",
    "Core": "Core", "Rig": "Rig", "WorldUI": "WorldUI", "Net": "Net",
    "Hands": "Hands",
}


def target_str(t: Target) -> str:
    if t.decl_type is None and t.method is None:
        return "*(resolved at runtime by `TargetMethod`)*"
    ty = t.decl_type or "?"
    name = t.method or "?"
    if t.method_type in ("Getter", "Setter"):
        name = f"{t.method_type.lower()[:3]}_{name}"
    if t.method_type == "Constructor":
        name = ".ctor"
    args = ", ".join(a.replace("typeof(", "").rstrip(")") for a in t.arg_types)
    sig = f"`{ty}.{name}({args})`"
    if t.private:
        sig += " *(private)*"
    return sig


def owner_str(c: PatchClass) -> str:
    if not c.registrations:
        return "**NONE — INERT**"
    return ", ".join(
        f"`{p.stem}`:{ln}" for p, ln in c.registrations)


HEADER = """# Harmony patch inventory

> **GENERATED FILE — do not edit by hand.**
> Regenerate with `scripts/patch-inventory.sh generate`;
> `scripts/patch-inventory.sh check` (run by `scripts/refactor-guard.sh check`)
> fails if this file drifts from the source.
>
> The previous, hand-maintained version of this document listed 17 patched
> methods while the repository declared 60 `[HarmonyPatch]` attributes. Since
> `CHARTER.md` §5 cites this file as the authority for "is this a patch target?"
> — i.e. it is consulted before deleting anything — a stale version is actively
> dangerous, not merely untidy. Hence: generated.
>
> All patches go through the single shared `Harmony("dev.gloomhavenvr")` instance
> and are removed collectively by `Plugin.OnDestroy → UnpatchSelf()`.
>
> **Prose a parser cannot derive** — the per-patch effect/gate notes and the
> cross-module contention rules — lives in `docs/PATCH-NOTES.md`, which was split
> out of this file when it became generated. Per-patch *contracts* live in
> `.planning/refactor/INVARIANTS-Hands-Board-Core.md` §13. The three documents
> answer different questions and all three should survive.

## How to read the Registered-by column

A patch class only takes effect if some module calls `PatchAll(typeof(X))` on it.
A class with **NONE** is compiled, shipped, and completely inert — that has
happened twice (`8567af4`, `57309c5`). `check` fails on it.

Classes marked **degrades by design** resolve their own target through a
`TargetMethod` that may return `null`; those are *allowed* to patch nothing at
runtime, which is why a runtime audit could never do this job (see
`.planning/refactor/REVIEW-Hands-Board-Core.md` §P1).
"""


def render(classes: list[PatchClass]) -> str:
    lines = [HEADER, ""]
    total_methods = sum(len(c.methods) for c in classes)
    lines.append(f"**{len(classes)} patch classes, {total_methods} patched methods.**")
    lines.append("")

    by_module: dict[str, list[PatchClass]] = {}
    for c in classes:
        by_module.setdefault(c.module, []).append(c)

    for module in sorted(by_module):
        lines.append(f"## {MODULE_LABEL.get(module, module)}")
        lines.append("")
        lines.append("| Patch class | Target | Kind | Registered by |")
        lines.append("|---|---|---|---|")
        for c in sorted(by_module[module], key=lambda x: (str(x.file), x.line)):
            note = " *(degrades by design)*" if c.has_target_method else ""
            rel = c.file.relative_to(ROOT)
            if not c.methods:
                lines.append(
                    f"| `{c.full_name}`{note}<br/><sub>{rel}:{c.line}</sub> "
                    f"| {target_str(c.class_target)} | — | {owner_str(c)} |")
                continue
            first = True
            for m in sorted(c.methods, key=lambda x: x.line):
                label = (f"`{c.full_name}`{note}<br/><sub>{rel}:{c.line}</sub>"
                         if first else "&nbsp;")
                lines.append(
                    f"| {label} | {target_str(m.target)} | {m.kind} | "
                    f"{owner_str(c) if first else '&nbsp;'} |")
                first = False
        lines.append("")

    lines.append("## Registration sites")
    lines.append("")
    lines.append("| Module file | Patch classes registered |")
    lines.append("|---|---|")
    sites: dict[Path, list[str]] = {}
    for c in classes:
        for p, _ in c.registrations:
            sites.setdefault(p, []).append(c.name)
    for p in sorted(sites, key=str):
        names = ", ".join(f"`{n}`" for n in sorted(sites[p]))
        lines.append(f"| `{p.relative_to(ROOT)}` | {names} |")
    lines.append("")
    lines.append(
        "The preloader (`GloomhavenVR.Preload.dll`) patches **no** assemblies "
        "(`TargetDLLs` is empty); it only installs the OpenXR natives + "
        "UnitySubsystems manifest.")
    lines.append("")
    return "\n".join(lines)


# ---------------------------------------------------------------------------
# 6. Entry points
# ---------------------------------------------------------------------------

def build() -> list[PatchClass]:
    classes = collect()
    find_registrations(classes)
    return sorted(classes, key=lambda c: (str(c.file), c.line))


def cmd_generate() -> int:
    classes = build()
    DOC.write_text(render(classes), encoding='utf-8')
    n = sum(len(c.methods) for c in classes)
    print(f"docs/PATCH-INVENTORY.md: {len(classes)} patch classes, {n} patched methods")
    return 0


def cmd_check() -> int:
    classes = build()
    fail = False

    for c in classes:
        if not c.registrations:
            fail = True
            print(
                f"error: {c.file.relative_to(ROOT)}:{c.line}: `{c.full_name}` declares Harmony\n"
                f"  patches but NO module registers it — it is INERT.\n"
                f"  Add `VRSession.Harmony?.PatchAll(typeof({c.full_name}));` to the owning\n"
                f"  module's Init. (This has shipped twice: 8567af4, 57309c5.)",
                file=sys.stderr)
        elif len(c.registrations) > 1:
            fail = True
            where = ", ".join(f"{p.relative_to(ROOT)}:{ln}" for p, ln in c.registrations)
            print(
                f"error: `{c.full_name}` is registered {len(c.registrations)} times "
                f"({where}) — Harmony would apply it twice.", file=sys.stderr)

    want = render(classes)
    have = DOC.read_text(encoding='utf-8') if DOC.exists() else ""
    if want != have:
        fail = True
        print(
            "error: docs/PATCH-INVENTORY.md is out of date with the source.\n"
            "  Run `scripts/patch-inventory.sh generate` and commit the result.",
            file=sys.stderr)

    if not fail:
        n = sum(len(c.methods) for c in classes)
        print(f"patch surface: {len(classes)} classes, {n} methods, all registered exactly once")
    return 1 if fail else 0


def main(argv: list[str]) -> int:
    mode = argv[1] if len(argv) > 1 else "check"
    try:
        if mode == "generate":
            return cmd_generate()
        if mode == "check":
            return cmd_check()
    except ParseError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    print("usage: patch-inventory.py {generate|check}", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main(sys.argv))
