#!/usr/bin/env python3
"""AN ARRAY SIZED BY A LITERAL, INDEXED BY AN ENUM THAT GREW.

WHY THIS EXISTS
---------------
Review R2 of 2026-09-07, finding F1 (shipped in ModBuild 479):

`Net/Remote/RemoteCardArt.cs` declares an `FxSurface` enum naming the surfaces a mirrored
card's FX can be drawn on, and three one-shot latch arrays keyed by it:

    private static readonly bool[] s_burnRigLogged     = new bool[4];   // :2167
    private static readonly bool[] s_burnRigRefused    = new bool[4];   // :2168
    private static readonly bool[] s_burnRigNoFxImages = new bool[4];   // :2169

indexed unguarded at `:3178`, `:3216` and `:3236` as `s_burnRigLogged[(int)surface]`.
ModBuild 479 added `Active = 4` and `Held = 5` to the enum and did not touch the arrays.
`git log -S` places the two new members in `c14e5220` and the `new bool[4]` arrays in
`8e72856f`, a build earlier.

The consequence is not an index warning. `ReportBurnRigOnce` is called from inside
`BuildBurnRig`, whose `catch` sets `_burnImages = null` and logs at the **Debug** tier —
silent at the shipped level. So for the two newest surfaces the burn rig throws, is
swallowed, and `SetAbilityCardFxProgress` then refuses every subsequent frame because
`_burnImages == null`. **Two of six card-FX surfaces are simply dead**, and both had
already taken `CardHalfTone.HoldCardFxLook` on the way in, so they hold the one-writer
lock for a look they will never paint.

The fourth array of the family, `s_flameDrawnLogged` (`:2801`), IS bounds-guarded
(`s < 0 || s >= s_flameDrawnLogged.Length`, `:2843`) — so instead of throwing it silently
never logs for `Active` and `Held`. R2's NOTE 1: *"Growing the three arrays in F1 without
adding the same guard leaves the next enum member in the same trap."* **A guard is
therefore not an exemption here and this gate does not treat it as one.** A latch array
whose length disagrees with its key enum is wrong whichever way it fails.

Two of the file's own doc comments encode the stale 4 ("four surfaces times three
outcomes is at most twelve lines"), which is why review did not catch it either: the
comment agreed with the code and both were wrong.

THE RULE
--------
An array whose declared length is an INTEGER LITERAL and which is indexed by a cast of an
enum declared in this repository must have that literal equal to the enum's member count.

The remedy the message asks for is to stop writing a literal at all:

    private static readonly bool[] s_burnRigLogged =
        new bool[System.Enum.GetValues(typeof(FxSurface)).Length];

which is the only form that cannot come back. Equalising the literal passes this gate and
is the weaker fix; the message says so.

HOW THE ENUM BEHIND AN INDEX IS FOUND, and where it gives up
------------------------------------------------------------
Resolution is per-file and deliberately shallow:

  * `field[(int)expr]` — `expr` is resolved by looking for a declaration of that
    identifier in the same file: a parameter or local `<Enum> expr`, a property
    `<Enum> Expr { get`, or a field `<Enum> _expr`.
  * `int v = (int)expr; … field[v]` — one hop through an `int` local, which is how the
    guarded fourth site is written.
  * The identifier must resolve to **exactly one** enum declared under `src/`. Zero
    candidates, two candidates, an enum from the game's assemblies, a computed index, a
    `Length`-derived index: all are skipped in silence. A miss costs nothing; a wrong
    accusation would get this file deleted.

FALSE POSITIVES — THE POLICY
----------------------------
* Comments and string literals are blanked first. This repository documents its own array
  sizes in prose beside them, and it has a named bug class for a lint that reads its own
  documentation as code.
* **Only a DISAGREEMENT fails.** A literal that happens to equal the member count today is
  reported by `report` and passes. Demanding `Enum.GetValues(...).Length` everywhere would
  be a repo-wide rewrite this gate has no mandate for, and a gate whose first run rewrites
  200 declarations is a gate that gets reverted.
* `.planning/refactor/ENUM-ARRAYS.allow` carries the sites a review has already convicted
  and a lane is repairing, so the gate can exist before the fix lands. An allow entry whose
  site is no longer a disagreement FAILS — deleting the line is the last step of the fix.

    check-enum-arrays.py           verify
    check-enum-arrays.py report    every literal-sized array whose index resolved to an enum
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "GloomhavenVR"
ALLOW = ROOT / ".planning" / "refactor" / "ENUM-ARRAYS.allow"

_BLOCK = re.compile(r"/\*.*?\*/", re.S)
_LINE = re.compile(r"//[^\n]*")
_STRING = re.compile(r'"(?:[^"\\\n]|\\.)*"')


def _blank(match: re.Match) -> str:
    return re.sub(r"[^\n]", " ", match.group(0))


def strip_noncode(source: str) -> str:
    return _STRING.sub(_blank, _LINE.sub(_blank, _BLOCK.sub(_blank, source)))


ENUM_DECL = re.compile(r"\benum\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*\w+\s*)?\{",)

# `T[] Name = new T[12];` — the literal is what this gate is about, so a sized-from-an-
# expression declaration simply does not match and is not a finding.
ARRAY_DECL = re.compile(
    r"\b(?P<elem>[A-Za-z_][A-Za-z0-9_.]*)\s*\[\s*\]\s+(?P<field>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"
    r"new\s+(?P=elem)\s*\[\s*(?P<size>\d+)\s*\]")


def enum_members(body: str) -> int:
    """Count the members of an enum body (text between its braces, exclusive)."""
    count = 0
    for part in body.split(","):
        part = part.strip()
        if part:
            count += 1
    return count


def enums_in(code: str) -> dict[str, int]:
    """{enum name: member count} for every enum declared in this file."""
    found: dict[str, int] = {}
    for match in ENUM_DECL.finditer(code):
        open_at = match.end() - 1
        depth = 0
        for i in range(open_at, len(code)):
            if code[i] == "{":
                depth += 1
            elif code[i] == "}":
                depth -= 1
                if depth == 0:
                    found[match.group("name")] = enum_members(code[open_at + 1:i])
                    break
    return found


def all_enums() -> dict[str, tuple[int, str]]:
    """{enum name: (member count, declaring file)} across src/. Ambiguous names dropped."""
    seen: dict[str, list[tuple[int, str]]] = {}
    for path in sorted(SRC.rglob("*.cs")):
        code = strip_noncode(path.read_text(encoding="utf-8", errors="replace"))
        for name, count in enums_in(code).items():
            seen.setdefault(name, []).append((count, path.relative_to(SRC).as_posix()))
    # A name declared twice cannot be resolved from an identifier alone, so it is dropped
    # rather than guessed at.
    return {n: v[0] for n, v in seen.items() if len(v) == 1}


def resolve_enum(code: str, ident: str, enums: dict[str, tuple[int, str]]) -> str | None:
    """The enum type of `ident`, if exactly one enum name declares it in this file."""
    candidates = set()
    for match in re.finditer(
            r"\b(?P<type>[A-Za-z_][A-Za-z0-9_]*)\s+" + re.escape(ident) + r"\b(?!\s*\()", code):
        if match.group("type") in enums:
            candidates.add(match.group("type"))
    return candidates.pop() if len(candidates) == 1 else None


def sites():
    """(rel file, line, field, literal, enum, count) for each resolvable indexed array."""
    enums = all_enums()
    out = []
    for path in sorted(SRC.rglob("*.cs")):
        rel = path.relative_to(SRC).as_posix()
        code = strip_noncode(path.read_text(encoding="utf-8", errors="replace"))
        arrays = {m.group("field"): (int(m.group("size")),
                                     code.count("\n", 0, m.start()) + 1)
                  for m in ARRAY_DECL.finditer(code)}
        if not arrays:
            continue
        # One hop: `int v = (int)expr;` lets `field[v]` name the enum behind `v`.
        hops = {m.group("var"): m.group("src") for m in re.finditer(
            r"\bint\s+(?P<var>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*\(\s*int\s*\)\s*"
            r"(?P<src>[A-Za-z_][A-Za-z0-9_]*)\b", code)}
        for field, (size, decl_line) in arrays.items():
            found: set[str] = set()
            for match in re.finditer(
                    re.escape(field) + r"\s*\[\s*(?:\(\s*int\s*\)\s*)?"
                    r"(?P<idx>[A-Za-z_][A-Za-z0-9_]*)\s*\]", code):
                ident = match.group("idx")
                ident = hops.get(ident, ident)
                name = resolve_enum(code, ident, enums)
                if name:
                    found.add(name)
            if len(found) != 1:
                continue                      # unresolved or contradictory — say nothing
            name = found.pop()
            out.append((rel, decl_line, field, size, name, enums[name][0]))
    return out


def load_allow() -> tuple[dict[tuple[str, str], str], list[str]]:
    entries: dict[tuple[str, str], str] = {}
    problems: list[str] = []
    if not ALLOW.is_file():
        return entries, problems      # absence is fine; it means nothing is excused
    for number, raw in enumerate(ALLOW.read_text(encoding="utf-8").splitlines(), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("|")]
        if len(parts) != 3:
            problems.append(f"{ALLOW.name}:{number}: expected `file | field | reason`")
            continue
        if len(parts[2]) < 30:
            problems.append(f"{ALLOW.name}:{number}: the reason must name the review finding "
                            f"and the build it shipped in.")
            continue
        entries[(parts[0], parts[1])] = parts[2]
    return entries, problems


def main() -> int:
    found = sites()
    allowed, problems = load_allow()
    bad = [s for s in found if s[3] != s[5]]
    unexcused = [s for s in bad if (s[0], s[2]) not in allowed]
    stale = [k for k in allowed if k not in {(s[0], s[2]) for s in bad}]

    for problem in problems:
        print(f"error: {problem}", file=sys.stderr)

    if unexcused:
        print("error: a latch array is sized by a LITERAL against an enum of a different size.",
              file=sys.stderr)
        for rel, line, field, size, name, count in unexcused:
            print(f"         {rel}:{line}  {field} = new […][{size}]  but "
                  f"{name} has {count} member(s)", file=sys.stderr)
        print("       The index runs past the end for every member the literal does not cover. "
              "That is\n"
              "       not an index warning: in Net/Remote/RemoteCardArt.cs the throw is swallowed "
              "by a\n"
              "       catch that logs at the DEBUG tier and nulls the rig, so two of six card-FX "
              "surfaces\n"
              "       were simply dead from ModBuild 479 with nothing in the log at the shipped "
              "level\n"
              "       (review R2 F1, 2026-09-07). A bounds guard is not the fix either — it turns "
              "the\n"
              "       throw into a silent no-op for the same members.\n"
              "       WRITE THE SIZE FROM THE ENUM:\n"
              "         new bool[System.Enum.GetValues(typeof(" + unexcused[0][4] + ")).Length]\n"
              "       Equalising the literal also passes here, and is the fix that comes back.",
              file=sys.stderr)

    if stale:
        print("error: ENUM-ARRAYS.allow excuses a site that is no longer a disagreement:",
              file=sys.stderr)
        for rel, field in sorted(stale):
            print(f"         {rel}  {field}", file=sys.stderr)
        print("       Delete the line — it is the last step of the fix.", file=sys.stderr)

    if problems or unexcused or stale:
        return 1

    # A FLOOR, for the reason the other two now carry one: an empty scan reports "0 literal-sized
    # arrays" and exits 0, which reads exactly like "nothing disagrees". This tree has had at
    # least fifteen such arrays since the gate was written for the ModBuild 479 defect.
    if len(found) < 5:
        print(f"error: only {len(found)} literal-sized enum-indexed array(s) were found under "
              f"{SRC.relative_to(ROOT)} — this tree has ~15.", file=sys.stderr)
        print("       Nothing was measured; a scan that finds no sites cannot find a disagreement.",
              file=sys.stderr)
        return 1

    print(f"enum arrays: {len(found)} literal-sized array(s) index an enum declared in this "
          f"repository; {len(found) - len(bad)} agree with their enum's member count, "
          f"{len(bad)} excused in {ALLOW.name}.")
    return 0


def report() -> int:
    for rel, line, field, size, name, count in sites():
        mark = "OK " if size == count else "BAD"
        print(f"{mark} {rel}:{line}  {field} = new […][{size}]   {name} = {count} member(s)")
    return 0


if __name__ == "__main__":
    sys.exit(report() if len(sys.argv) > 1 and sys.argv[1] == "report" else main())
