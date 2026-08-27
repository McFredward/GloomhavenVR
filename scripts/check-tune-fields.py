#!/usr/bin/env python3
"""Board-tuning (extras record 28) field-id invariants — the check that was missing.

WHY THIS EXISTS. ModBuild 299 added one dial to record 28 and got two things wrong in the same
line, and every gate stayed green:

  1. the id (248) was OUTSIDE every width range, so NetProtocol.BoardTuneFieldWidth answered 0.
     A reader stops dead at an id whose length it cannot know, and the SENDER's own pager
     (BoardTunePages.MeasurePage -> PageCount) returns -1 for the whole field list, so WritePage
     writes nothing at all. While that dial was set, the ENTIRE board tuning stopped reaching
     every peer -- not just the new bit;
  2. it was appended BEFORE two lower ids, breaking the ascending-id layout contract that lets a
     page state the id RANGE it is complete for.

Neither is visible in a build log, in a wire golden vector, or in the config surface. Both are
purely structural, which is exactly what a static check is for. It also stayed invisible in
testing because the dial defaults OFF and the record is SPARSE: the broken field was emitted only
for the one player who had turned the dial on -- the very player the build's test instructions
asked to turn it on.

WHAT IS CHECKED

  A. Every `public const byte Tune*` on NetProtocol (bar the range markers `*IdMin`/`*IdMax`)
     falls inside one of the declared width ranges.
  B. The ids appended by BoardTuning.Sample are STRICTLY ASCENDING, which is that method's own
     stated layout contract.
  C. No id is sampled twice, and every sampled id is a declared constant.

The width ranges are PARSED OUT OF NetProtocol.cs rather than written down here. A second copy of
those four numbers would be exactly the mirrored-constant defect scripts/check-mirrors.sh exists
to hunt: this file must read the single source, not agree with it.

Exit 0 when every invariant holds, 1 otherwise.
"""

import os
import re
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROTOCOL = os.path.join(REPO, "src", "GloomhavenVR", "Net", "NetProtocol.cs")
SAMPLER = os.path.join(REPO, "src", "GloomhavenVR", "Net", "Board", "BoardTuning.cs")

CONST = re.compile(r"public\s+const\s+byte\s+(Tune[A-Za-z0-9_]+)\s*=\s*(\d+)\s*;")
RANGE_SUFFIX = ("IdMin", "IdMax")

# A constant whose name carries this marker is a TOMBSTONE, not a field: an id that was burned by
# a mistake and is declared only so the number can never be handed out again. It is exempt from the
# width rule by definition — being outside every range is the whole reason it exists — and because
# it is not a field, the sampler must never write it, which invariant C enforces for free (it is
# not in field_ids, so a reference to it reads as an undeclared write).
TOMBSTONE = "NeverLive"


def fail(msg):
    print("FAIL  " + msg)
    return 1


def read(path):
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def parse_constants(source):
    """Every `public const byte Tune… = N;` in NetProtocol, by name."""
    return {name: int(value) for name, value in CONST.findall(source)}


def parse_ranges(consts):
    """The declared width ranges, taken from the constants themselves.

    Mirrors NetProtocol.BoardTuneFieldWidth's own structure: vec / colour / (length..angle) /
    count. The length and angle ranges are contiguous and share a width, which is why that
    function tests `TuneLengthIdMin .. TuneAngleIdMax` as one span; reproduced here from the same
    four constants rather than from a fifth number written down in this file.
    """
    needed = ["TuneVecIdMin", "TuneVecIdMax", "TuneColorIdMin", "TuneColorIdMax",
              "TuneLengthIdMin", "TuneAngleIdMax", "TuneCountIdMin", "TuneCountIdMax"]
    missing = [n for n in needed if n not in consts]
    if missing:
        print("FAIL  NetProtocol is missing range markers: " + ", ".join(missing))
        print("      This checker reads the ranges from the source; it cannot run without them.")
        sys.exit(1)
    return [
        ("vec", consts["TuneVecIdMin"], consts["TuneVecIdMax"], 6),
        ("colour", consts["TuneColorIdMin"], consts["TuneColorIdMax"], 3),
        ("length/angle", consts["TuneLengthIdMin"], consts["TuneAngleIdMax"], 2),
        ("count", consts["TuneCountIdMin"], consts["TuneCountIdMax"], 1),
    ]


def width_of(ranges, value):
    for _, lo, hi, width in ranges:
        if lo <= value <= hi:
            return width
    return 0


def strip_comments(text):
    """Blank out // and /* */ comments, keeping line structure.

    LOAD-BEARING, and the first version of this checker did not do it. Field ids are named in
    PROSE all over this codebase — a comment explaining why a dial sits where it does mentions the
    ids around it — so a scan that reads comments reports duplicates and descents that exist only
    in English. It reported five, every one of them false, before this function existed.
    """
    out = []
    i, n = 0, len(text)
    while i < n:
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i))
            i = j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append("".join(c if c == "\n" else " " for c in text[i:j]))
            i = j
        elif text[i] == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(" " * (j - i))
            i = j
        else:
            out.append(text[i])
            i += 1
    return "".join(out)


def sampled_ids(source, field_ids):
    """The Tune* FIELD ids referenced inside BoardTuning.Sample, in source order.

    Two filters, and the checker was wrong without either of them:

      * comments are stripped first (see strip_comments);
      * only names that are declared `public const byte` count. NetProtocol also carries Tune*
        constants that are not ids at all — TuneItemCueEmberRateScale is a float — and reading one
        as an id reported it as an undeclared field.

    Sample is delimited by its own signature and the next member declaration at the same
    indentation.
    """
    start = source.find("internal static int Sample(")
    if start < 0:
        print("FAIL  BoardTuning.Sample not found — this checker is pointed at the wrong file.")
        sys.exit(1)
    nxt = re.search(r"\n    (?:internal|private|public)\s", source[start + 10:])
    end = start + 10 + nxt.start() if nxt else len(source)
    body = strip_comments(source[start:end])
    return [n for n in re.findall(r"NetProtocol\.(Tune[A-Za-z0-9_]+)", body) if n in field_ids]


def main():
    protocol = read(PROTOCOL)
    consts = parse_constants(protocol)
    ranges = parse_ranges(consts)

    field_ids = {name: value for name, value in consts.items()
                 if not name.endswith(RANGE_SUFFIX) and TOMBSTONE not in name}
    if len(field_ids) < 100:
        return fail(f"only {len(field_ids)} field-id constants found — the scan is not reaching "
                    "NetProtocol; a passing run would be meaningless.")

    problems = 0

    # ---- A. every declared id has a width ----------------------------------------------------
    for name, value in sorted(field_ids.items(), key=lambda kv: kv[1]):
        if width_of(ranges, value) == 0:
            spans = ", ".join(f"{label} {lo}..{hi}" for label, lo, hi, _ in ranges)
            problems += fail(
                f"{name} = {value} lies in NO width range ({spans}). "
                "BoardTuneFieldWidth answers 0 for it, so a reader abandons the record at that "
                "field AND the sender's pager refuses the whole list — see "
                "NetProtocol.TuneNeverLive248.")

    # ---- B/C. the sampler appends in strictly ascending, declared, unique id order ------------
    sampler = read(SAMPLER)
    names = sampled_ids(sampler, field_ids)
    if len(names) < 100:
        return fail(f"only {len(names)} ids referenced in BoardTuning.Sample — the body scan is "
                    "not reaching the field writes; a passing run would be meaningless.")

    seen = {}
    previous_name, previous_value = None, -1
    for name in names:
        if name not in field_ids:
            problems += fail(f"BoardTuning.Sample writes {name}, which is not a declared "
                             "`public const byte Tune…` on NetProtocol.")
            continue
        value = field_ids[name]
        if name in seen:
            problems += fail(f"BoardTuning.Sample writes {name} (id {value}) twice.")
        seen[name] = value
        if value <= previous_value:
            problems += fail(
                f"BoardTuning.Sample writes {name} = {value} after {previous_name} = "
                f"{previous_value} — the field list must be STRICTLY ASCENDING (that method's own "
                "layout contract: it is what lets a page state the id range it is complete for).")
        previous_name, previous_value = name, value

    if problems:
        print(f"\ntune fields: {problems} problem(s) in {len(field_ids)} declared ids / "
              f"{len(names)} sampled fields.")
        return 1

    print(f"tune fields: {len(field_ids)} declared ids all inside a declared width range; "
          f"BoardTuning.Sample writes {len(names)} of them in strictly ascending order.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
