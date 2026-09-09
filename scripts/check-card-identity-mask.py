#!/usr/bin/env python3
"""TWO QUESTIONS THAT MUST NEVER BECOME ONE PREDICATE — and the gate that says so IN CI.

WHY THIS FILE EXISTS AT ALL, GIVEN THAT THE LINT ALREADY EXISTED
----------------------------------------------------------------
`tests/GloomhavenVR.WireTests/CardIdentityMaskVectors.cs` is the only guard in this
repository against the ModBuild 477 card-identity leak returning. Review R1 of
2026-09-07 (finding F2, CONFIRMED by the integrator) established that it has never
once run in CI and never will where it lives:

    .github/workflows/ci.yml       "Wire tests (compile only — see docs/CI-CD.md)"
    .github/workflows/release.yml  "Wire tests (compile only — vectors need the real game DLL)"

Both steps run `dotnet build` and print a notice saying the assertions are NOT executed.
That decision is correct for the file's neighbours: the golden wire vectors load the
game's real `UnityEngine.CoreModule.dll` because `Mathf.RoundToInt`'s banker's rounding
sits on the quantization path, and a reference assembly cannot be executed (the CLR
refuses it with `BadImageFormatException`). But `CardIdentityMaskVectors` touches no
Unity type at all — it `File.ReadAllText`s two `.cs` files and runs three regexes. It is
a PURE TEXT LINT that was blocked from CI purely by CO-LOCATION.

R1 ran its three regexes by hand against the shipped source and found them GREEN. The
rule holds today and is enforced by nothing. This script is the enforcement.

    The C# file is NOT moved and NOT edited. It stays as the belt-and-braces local run
    (`scripts/wire-tests.sh`). This is the braces. They are meant to agree, and where
    they deliberately do not, the difference is documented under FALSE POSITIVES below.

THE LEAK THIS HOLDS SHUT
------------------------
ModBuild 477 item 7: a mirrored decision row published `confirm='Verbrennen
"Zusatzdolch"'` to every peer while `RevealGate.PeersSeeOurCardFronts` was SHUT and
`SpareDagger` was sitting in that character's `DiscardedAbilityCards`. Fixed in
ModBuild 478 by `Net.DecisionLabelMask`, whose `AddCovered` skips exactly the cards
`RevealGate.PeersMayNameOurCard` permits.

THE WAY IT COMES BACK
---------------------
On 2026-09-07 the user ruled that a peer's DISCARD fan shows FRONTS in every phase,
including the game's own secret window ("Die Fächer der piles werden also ab jetzt immer
mit Vorderseiten gezeigt ohne Ausnahme"). The obvious one-line implementation of that
ruling is to widen the card-public predicate the MASK reads. That widening re-opens 477:
`CardsHandUI.PerformShortRest` picks the short-rest sacrifice out of
`DiscardedAbilityCards` and REMOVES NOTHING, so the sacrifice is a discard-pile card for
the whole time the prompt that names it is on screen.

Seeing every card in a peer's discard fan says nothing about WHICH of them the game has
singled out. That selection is the secret. Nothing in the type system can notice the
difference between the two questions; this script can.

THE FOUR RULES, and each is pinned by the NAMES it must or must not contain:

  1. `DecisionLabelMask.AddCovered` asks `RevealGate.PeersMayNameOurCard` — the NAMING
     question — and asks none of `IsDiscardedCard`, `CardFaces`, `IsPublicPopulation`,
     which are the FACE side.
  2. `RevealGate.PeersMayNameOurCard` keeps the PHASE term (`PeersSeeOurCardFronts`) and
     the burn/active exception (`IsPubliclyRevealedCard`), and does NOT read
     `IsDiscardedCard` — i.e. it did not follow the face side when the face side widened.
  3. `RevealGate.PileFrontsReach` remains false: the MB487 user rule supersedes all
     historical discard exceptions for artwork.
  4. `RevealGate.IsPublicPopulation` remains false: active, lost and item cards follow
     the same selection/action face policy. Naming remains a separate question.

FALSE POSITIVES — THE POLICY
----------------------------
Every assertion here is about a CALL or an ENUM MEMBER in CODE. So both comments and
string literals are blanked before matching, in that order:

  * COMMENTS FIRST. `AddCovered`'s body carries a long note NAMING the predicates it must
    not ask — written at the call site precisely so the next reader sees the trap. A raw
    text search finds those mentions and fails on the documentation of the rule it is
    enforcing. (It did, on the first run of the C# lint; its own comment says so.)
  * STRING LITERALS SECOND, and this is where this script is deliberately STRICTER than
    the C# original, which strips comments only. This repository has a named bug class for
    skipping the string pass — "a token quoted in its own explanation" — and
    `scripts/check-mirrors.sh` documents nine log lines in the neighbouring files that
    spell a predicate name inside a string. Neither of these two files contains such a
    line today, so the two lints agree character for character on the shipped tree; the
    strip is there so that ADDING a log line that quotes `IsDiscardedCard` cannot fail
    this gate. Blanking can only REMOVE text, so it costs recall and never precision.
    The order matters: `AddCovered`'s comment block contains a `"`-quoted card name, so a
    string pass run first would eat the code after it.

Neither file contains a verbatim (`@"…"`) string today; the strip does not model one, and
this is asserted rather than assumed — a `@"` in either file fails the run with an
explanation instead of matching wrongly.

A MEMBER THAT IS RENAMED OR GIVEN A BLOCK BODY FAILS LOUDLY, with the member named and
with an instruction to update the lint rather than delete it. A gate that passes
vacuously when the thing it guards was renamed is the failure this project calls
"an instrument shipped and lying".

    check-card-identity-mask.py          verify
    check-card-identity-mask.py show     print the four bodies as the lint reads them
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

GATE_REL = "src/GloomhavenVR/Net/RevealGate.cs"
MASK_REL = "src/GloomhavenVR/Net/DecisionLabelMask.cs"

# ---------------------------------------------------------------------------------------
# Text handling
# ---------------------------------------------------------------------------------------

_BLOCK_COMMENT = re.compile(r"/\*.*?\*/", re.S)
_LINE_COMMENT = re.compile(r"//[^\n]*")
# A non-verbatim C# string. Escapes are honoured so that a `\"` inside a literal does not
# end it early. Char literals ('"') are left alone: neither file contains one, and a
# quote-carrying char literal would be reported by the verbatim guard's sibling check.
_STRING = re.compile(r'"(?:[^"\\\n]|\\.)*"')


def strip_noncode(source: str) -> str:
    """Blank comments then string literals, preserving newlines so line numbers survive.

    Substitution, never deletion — a caller that wants to report a line number gets the
    file's own numbering back.
    """
    def blank(match: re.Match) -> str:
        return re.sub(r"[^\n]", " ", match.group(0))

    no_block = _BLOCK_COMMENT.sub(blank, source)
    no_line = _LINE_COMMENT.sub(blank, no_block)
    return _STRING.sub(blank, no_line)


def method_body(source: str, method: str) -> str:
    """The text between a method's opening brace and its matching close.

    Brace-counting rather than a regex, because the body carries braces of its own and a
    lazy match would stop at the first one and pass a lint it never actually ran. Same
    helper and same reason as the C# original.
    """
    decl = re.search(r"\b" + re.escape(method) + r"\s*\([^)]*\)\s*\{", source, re.S)
    if not decl:
        return ""
    open_at = decl.end() - 1
    depth = 0
    for i in range(open_at, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[open_at:i + 1]
    return ""


def expression_body(source: str, member: str) -> str | None:
    """The right-hand side of an expression-bodied `bool Member(...) => …;`.

    None means "not found as an expression-bodied predicate", which is itself a failure:
    the whole value of these four members is that their CONTENTS are pinned as text.
    """
    match = re.search(
        r"bool\s+" + re.escape(member) + r"\s*\([^)]*\)\s*=>(?P<body>[^;]*);", source, re.S)
    return match.group("body") if match else None


# ---------------------------------------------------------------------------------------
# Reporting
# ---------------------------------------------------------------------------------------

FAILURES: list[str] = []


def require(ok: bool, message: str) -> None:
    if not ok:
        FAILURES.append(message)


def read(rel: str) -> str:
    path = ROOT / rel
    if not path.is_file():
        FAILURES.append(
            f"{rel} is missing — the source lint could not run at all. If the class moved, "
            f"move this lint with it rather than deleting it: with "
            f"tests/GloomhavenVR.WireTests compile-only in CI, this script is the only thing "
            f"in the repository that can notice the ModBuild 477 item 7 leak coming back.")
        return ""
    text = path.read_text(encoding="utf-8")
    if "@\"" in text:
        FAILURES.append(
            f"{rel} has grown a verbatim (@\"…\") string. This lint's string-blanking pass "
            f"does not model verbatim literals, so it would stop reading the file correctly. "
            f"Teach strip_noncode() about @\"…\" (and \"\" escapes) before landing that string; "
            f"do not switch the gate off.")
    return text


# ---------------------------------------------------------------------------------------
# The four rules
# ---------------------------------------------------------------------------------------

def lint_the_mask(mask: str) -> str:
    """RULE 1 — the mask asks the NAMING predicate and nothing else."""
    if not mask:
        return ""
    body = strip_noncode(method_body(mask, "AddCovered"))
    require(bool(body.strip()),
            "DecisionLabelMask.AddCovered was not found. It is the method that decides which "
            "of our card names are withheld from a peer-bound wording; if it was renamed, "
            "update this lint rather than deleting it.")
    if not body.strip():
        return ""

    require("PeersMayNameOurCard" in body,
            "AddCovered must skip a card through RevealGate.PeersMayNameOurCard — the NAMING "
            "question. It is deliberately narrower than the face question and must stay so.")

    for forbidden in ("IsDiscardedCard", "CardFaces", "IsPublicPopulation"):
        require(forbidden not in body,
                f"AddCovered must NOT ask RevealGate.{forbidden}. That is the FACE side, which "
                f"the 2026-09-07 pile-fan ruling widened to the discard pile — and the "
                f"short-rest sacrifice is a discard-pile card while the prompt that NAMES it is "
                f"on screen (CardsHandUI.PerformShortRest indexes DiscardedAbilityCards and "
                f"removes nothing). Asking it here re-opens the ModBuild 477 item 7 leak: "
                f"confirm='Verbrennen \"Zusatzdolch\"' published to every peer inside the "
                f"secret window.")
    return body


def lint_the_naming_predicate(gate: str) -> str:
    """RULE 2 — the naming predicate did not follow the face side."""
    if not gate:
        return ""
    body = expression_body(strip_noncode(gate), "PeersMayNameOurCard")
    require(body is not None,
            "RevealGate.PeersMayNameOurCard was not found as an expression-bodied predicate. "
            "It is the owner-seat NAMING gate — the one DecisionLabelMask.AddCovered reads and "
            "the one the two tooltip surfaces read. If it was renamed or given a block body, "
            "update this lint; do not delete it.")
    if body is None:
        return ""

    require("PeersSeeOurCardFronts" in body,
            "PeersMayNameOurCard must keep the PHASE term (PeersSeeOurCardFronts).")
    require("IsPubliclyRevealedCard" in body,
            "PeersMayNameOurCard must keep the burn/active exception — a card its owner has "
            "already destroyed or played face-up may be named, and the user's ruling for the "
            "burn is the strongest he has given any face.")
    require("IsDiscardedCard" not in body,
            "PeersMayNameOurCard must NOT read RevealGate.IsDiscardedCard. That is the pile-fan "
            "ruling and it belongs to the FACE side only: a peer may SEE every card in our "
            "discard fan and must still not be TOLD which one the short rest singled out.")
    return body


def lint_the_pile_scope(gate: str) -> tuple[str, str]:
    """MB487: no spent/active/item population or membership can bypass selection coverage."""
    if not gate:
        return "", ""
    code = strip_noncode(gate)
    reach = expression_body(code, "PileFrontsReach")
    require(reach is not None, "RevealGate.PileFrontsReach historical seam is missing.")
    if reach is not None:
        require(reach.strip() == "false", "MB487: discard membership must not reopen a covered face.")
        require("SacrificedCard" not in reach and "BoardPickSeat" not in reach,
                "MB487 covers every card surface, not only selected recess populations.")
    public = expression_body(code, "IsPublicPopulation")
    require(public is not None, "RevealGate.IsPublicPopulation historical seam is missing.")
    if public is not None:
        require(public.strip() == "false", "MB487: no population bypasses selection-phase artwork coverage.")
    return reach or "", public or ""


# ---------------------------------------------------------------------------------------

def main() -> int:
    gate = read(GATE_REL)
    mask = read(MASK_REL)

    lint_the_mask(mask)
    lint_the_naming_predicate(gate)
    lint_the_pile_scope(gate)

    if FAILURES:
        print("error: the card-identity mask rule is broken — a peer could be TOLD which card "
              "the game singled out inside its own secret window.", file=sys.stderr)
        print(f"       {len(FAILURES)} assertion(s) failed:", file=sys.stderr)
        for i, message in enumerate(FAILURES, 1):
            print(f"\n  [{i}] {message}", file=sys.stderr)
        print("\n       This is ModBuild 477 item 7. See "
              "tests/GloomhavenVR.WireTests/CardIdentityMaskVectors.cs, which asserts the same "
              "four rules locally and is compile-only in CI (review R1 F2, 2026-09-07).",
              file=sys.stderr)
        return 1

    print("card identity: the mask asks the NAMING predicate; PeersMayNameOurCard keeps its "
          "phase and burn naming terms; PileFrontsReach and IsPublicPopulation permit no face "
          "exceptions under the MB487 phase rule. 4 rules, 11 assertions.")
    return 0


def show() -> int:
    gate = read(GATE_REL)
    mask = read(MASK_REL)
    print("--- DecisionLabelMask.AddCovered (as the lint reads it) ---")
    print(strip_noncode(method_body(mask, "AddCovered")).strip() or "<not found>")
    for member in ("PeersMayNameOurCard", "PileFrontsReach", "IsPublicPopulation"):
        print(f"\n--- RevealGate.{member} ---")
        body = expression_body(strip_noncode(gate), member)
        print((body or "<not found>").strip())
    return 0


if __name__ == "__main__":
    sys.exit(show() if len(sys.argv) > 1 and sys.argv[1] == "show" else main())
