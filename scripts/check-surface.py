#!/usr/bin/env python3
"""Census the three things the refactor guard is BLIND to, and fail if any of them disappears.

`scripts/refactor-guard.sh` diffs the DECOMPILED DLL, which is the right instrument for "what
did my change actually do to the compiled code". `.planning/refactor/CHARTER.md` §3b then states,
plainly, the three places it cannot see:

    3. Anything not in the assembly: the Harmony registration wiring (a patch class nobody
       references compiles and ships inert — that has happened twice), config keys disappearing
       from a user's .cfg, log grep tokens the debug workflow depends on.

Two of those three had no checker at all, and both are silent failures on a USER'S machine:

  * A removed **config key** does not error. The player's tuned value in `dev.gloomhavenvr.*.cfg`
    is simply never read again, and their setting reverts without a message. `rebase-defaults.py`
    guards the DEFAULT VALUES; nothing guarded the KEYS.
  * A removed or reworded **log token** costs a hardware round. This project's workflow is: the
    user plays, drops `LogOutput.log`, and the next session greps it for named markers. A marker
    that quietly changed spelling reads as "the feature did not run".

So this script snapshots all three from the source text and diffs two snapshots. It is
deliberately a TEXT tool, not a compiled one: the point is to see the things that do not survive
into the assembly, and to run identically on a checkout of any commit.

WHAT COUNTS AS A FAILURE: a REMOVAL. Additions are free — a new config key or a new log marker is
new work, not a regression. That asymmetry is the whole design; a checker that also complained
about additions would be tripped by every round and would be switched off within a week.

Usage:
    check-surface.py snapshot <out.json>        # census the working tree
    check-surface.py diff <before.json> <after.json>   # exit 1 if anything was removed
    check-surface.py show <snapshot.json>       # print the counts

Typical use around a refactor:
    git stash list                      # (never in a worktree — refs/stash is shared)
    scripts/check-surface.py snapshot /tmp/before.json     # at the pre-refactor commit
    ...refactor...
    scripts/check-surface.py snapshot /tmp/after.json
    scripts/check-surface.py diff /tmp/before.json /tmp/after.json
"""

import json
import os
import re
import sys

# Resolved from THIS FILE, not from the working directory. The tool is invoked from
# scripts/refactor-guard.sh, from CI and by hand, and a cwd-relative "src" quietly
# censuses NOTHING when the caller happens to sit elsewhere — an empty census diffs
# clean against an empty census, so the failure mode is a checker that always passes.
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
SKIP_DIRS = {"obj", "bin"}


# --------------------------------------------------------------------------------------------
#  Comment stripping
#
#  Config keys and log markers both live in STRING LITERALS, and this repository's comments quote
#  both constantly — the build notes alone quote log lines verbatim. Counting a quoted example as
#  a live marker would make every consolidation of a comment look like a removal, which is exactly
#  the noise that gets a checker ignored. So comments come out first, and string literals are
#  preserved through the strip (they are the payload).
# --------------------------------------------------------------------------------------------
def strip_comments(text: str) -> str:
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        # verbatim string: @"..."  — "" is an escaped quote, backslash is NOT an escape
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
            out.append(text[i:j])
            i = j
            continue
        # ordinary string
        if c == '"':
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    j += 1
                    break
                j += 1
            out.append(text[i:j])
            i = j
            continue
        # char literal
        if c == "'":
            j = i + 1
            while j < n:
                if text[j] == "\\":
                    j += 2
                    continue
                if text[j] == "'":
                    j += 1
                    break
                j += 1
            out.append(text[i:j])
            i = j
            continue
        # line comment
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        # block comment
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            out.append(" ")
            continue
        out.append(c)
        i += 1
    return "".join(out)


# --------------------------------------------------------------------------------------------
#  The three censuses
# --------------------------------------------------------------------------------------------

# `Config.Bind("Section", "Key", default, "description")`, across newlines, with or without an
# explicit type argument. Section and key are what land in the user's .cfg file.
BIND = re.compile(r"\.Bind\s*(?:<[^>()]*>)?\s*\(\s*\"([^\"]*)\"\s*,\s*\"([^\"]*)\"")

# Every `[HarmonyPatch(...)]` attribute's argument text, normalised for whitespace. This is the
# registration wiring the guard cannot see: the ATTRIBUTE is what binds a method to a game
# method, and a patch class that loses its attribute still compiles.
HARMONY = re.compile(r"\[HarmonyPatch\s*\(([^\]]*)\)\s*\]", re.S)
HARMONY_BARE = re.compile(r"\[Harmony(Prefix|Postfix|Transpiler|Finalizer)\]")

# A string literal, so markers can be pulled out of the payload only.
STRING = re.compile(r"@\"(?:[^\"]|\"\")*\"|\"(?:\\.|[^\"\\])*\"", re.S)

# A GREP TOKEN: a run of two or more SHOUTED words. That is precisely the shape this project uses
# for the markers a human searches for — "HAUNT FIGURES", "ENV SOUND WIND LEAK", "DARKENING
# LEVER", "NO NODE", "CULLED". Single words are deliberately excluded: they are overwhelmingly
# ordinary prose in caps ("YES", "NO", "OK") and would bury the signal.
TOKEN = re.compile(r"\b[A-Z][A-Z0-9_]{1,}(?:[ \-][A-Z0-9_]{2,})+\b")


def sources():
    """Absolute path to walk with, repo-relative path to RECORD with.

    The snapshot names a file beside every entry, and that name is read by a human in a failure
    message. An absolute path is both unreadable and machine-specific — two checkouts of the same
    commit would produce snapshots that differ in every line while describing the same source.
    """
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in sorted(files):
            if f.endswith(".cs"):
                full = os.path.join(root, f)
                yield full, os.path.relpath(full, ROOT)


def snapshot() -> dict:
    keys, patches, tokens = {}, {}, {}
    for full, path in sorted(sources(), key=lambda t: t[1]):
        with open(full, encoding="utf-8", errors="replace") as fh:
            raw = fh.read()
        code = strip_comments(raw)

        for m in BIND.finditer(code):
            keys.setdefault(f"[{m.group(1)}] {m.group(2)}", path)

        for m in HARMONY.finditer(code):
            arg = " ".join(m.group(1).split())
            patches.setdefault(arg, path)
        for m in HARMONY_BARE.finditer(code):
            patches.setdefault(f"<bare {m.group(1)}> {path}", path)

        for s in STRING.finditer(code):
            for m in TOKEN.finditer(s.group(0)):
                tokens.setdefault(m.group(0), path)

    return {"configKeys": keys, "harmonyPatches": patches, "logTokens": tokens}


def diff(before: dict, after: dict) -> int:
    worst = 0
    for kind, label in (
        ("configKeys", "CONFIG KEY — a user's persisted setting silently stops being read"),
        ("harmonyPatches", "HARMONY PATCH — the class still compiles and ships INERT"),
        ("logTokens", "LOG GREP TOKEN — the next hardware round cannot find it"),
    ):
        b, a = before.get(kind, {}), after.get(kind, {})
        gone = sorted(set(b) - set(a))
        added = sorted(set(a) - set(b))
        print(f"=== {kind}: {len(b)} -> {len(a)}  ({len(gone)} removed, {len(added)} added) ===")
        if gone:
            worst = 1
            print(f"  REMOVED — {label}:")
            for g in gone:
                print(f"    - {g}    (was in {b[g]})")
        if added:
            for x in added[:200]:
                print(f"    + {x}")
            if len(added) > 200:
                print(f"    + ... and {len(added) - 200} more")
    print()
    print("REMOVALS ARE THE FAILURE; additions are new work." if worst else
          "nothing was removed from the three surfaces the compiled-form guard cannot see.")
    return worst


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    cmd = sys.argv[1]
    if cmd == "snapshot":
        snap = snapshot()
        out = sys.argv[2] if len(sys.argv) > 2 else "-"
        text = json.dumps(snap, indent=1, sort_keys=True)
        if out == "-":
            print(text)
        else:
            with open(out, "w", encoding="utf-8") as fh:
                fh.write(text)
        print(f"config keys {len(snap['configKeys'])}, "
              f"harmony patches {len(snap['harmonyPatches'])}, "
              f"log tokens {len(snap['logTokens'])}"
              + ("" if out == "-" else f"  ->  {out}"), file=sys.stderr)
        return 0
    if cmd == "diff":
        with open(sys.argv[2], encoding="utf-8") as fh:
            before = json.load(fh)
        with open(sys.argv[3], encoding="utf-8") as fh:
            after = json.load(fh)
        return diff(before, after)
    if cmd == "show":
        with open(sys.argv[2], encoding="utf-8") as fh:
            snap = json.load(fh)
        for k, v in snap.items():
            print(f"{k}: {len(v)}")
        return 0
    print(__doc__)
    return 2


if __name__ == "__main__":
    sys.exit(main())
