#!/usr/bin/env python3
"""Read the DISTRIBUTION of every standing instrument's decisive field, for one hardware round.

WHY THIS EXISTS
---------------
On 2026-09-07 the user reported that a peer's active card turns from grey back to blue one round
after it was used. The answer was already in the log he had just uploaded: `RECESS CARD FX` had
printed, on the SAME recess seat, `look=GHOST, source=pile:Discarded` and `look=NONE, source=widget`
alternating across the session. Nobody read the distribution. The defect cost a hardware round it
did not need to cost.

That is not a one-off. This project's own memory carries the same shape a dozen times over:

  * "Read the whole distribution" — `sort -u | head` shows the mode, not the distribution.
  * "A summary stat is not the field" — `grep -c` over a CHANGE-TRIGGERED instrument answers a
    different question from the one being asked.
  * "The blind spot is the lead" — eight clean scans mean the defect is in what no scan covered, so
    a token that produced ZERO lines is itself a reading and has to be printed as one.
  * "A truncated list is not absence" — an ellipsis-capped census is not evidence that X never
    appeared.

So this script does exactly one thing, mechanically, for every instrument in the registry below:
print the full value distribution of the field that DECIDES that instrument's verdict, with counts,
sorted, never truncated, and print `0 lines` loudly for a token that never fired.

WHAT IT DELIBERATELY DOES NOT DO
--------------------------------
It does not judge. It has no thresholds for "healthy" and it never says PASS. Two reasons, both
learned here:

  * A tool that grades turns a distribution back into a summary statistic, which is the failure it
    exists to prevent. The reader has to see the shape.
  * Six rounds of this project were spent tuning a coverage FRACTION when the field that decided the
    question was a name, not a number ("Name the blocker, not the number").

The one exception is NUMERIC OUTLIERS (`--outliers`), which flags a value far off that field's own
median in the same log. That is not a verdict either — it is a pointer at a line worth reading. It is
in here because the 2026-09-07 giant-text round had `world rect 8,515x0,355 m` sitting in the log
beside two readings of `0,976x0,966 m`, and a jump like that in a field which is otherwise stable is
the cheapest thing in the world to spot mechanically and the easiest thing in the world to miss by
eye.

THE DEFAULT FACTOR IS 5, NOT 10, AND THAT IS A MEASURED CHOICE. That giant-text reading is 8.7x its
own median. "An order of magnitude" — the obvious round number, and what this function shipped with
for about twenty minutes — does not catch the one case the function was written for. Calibrate a
threshold against the reading you already have, never against a round number.

ANCHORING, AND WHY EVERY GREP IN HERE CARRIES `] `
--------------------------------------------------
This mod's log lines quote other instruments' tokens verbatim, and quote the user's own German
sentences verbatim, inside their own explanatory prose. An unanchored count is therefore not a count
of events, it is a count of MENTIONS. Two measured cases from one session:

    DECISION LABEL MASK          unanchored 7 / 11      anchored 0 / 0
    slot-occupancy=model-only    unanchored 317 / 128   anchored 0 / 0

Both times the anchored reading was the true one and both times the unanchored reading named a wrong
cause. So every line this script considers must match `GloomhavenVR] ` first, and the instrument
token must then appear AFTER that prefix — a token inside a quoted sentence later in the line is
still prose, which is why the token match is additionally required to sit inside the first
`--token-window` characters of the message body (default 200). Set it wider for an instrument whose
token genuinely appears late.

USAGE
-----
    python3 scripts/log-triage.py <log> [<log> ...]
    python3 scripts/log-triage.py .planning/debug/Player.log .planning/debug/remote/Player.log
    python3 scripts/log-triage.py --only 'RECESS CARD FX' <log>
    python3 scripts/log-triage.py --field 'source' --token 'RECESS CARD FX' <log>   # ad-hoc
    python3 scripts/log-triage.py --outliers <log>

Each positional argument may be a raw `Player.log` or a pre-filtered extract; both work, because the
anchor is applied either way.

ADDING AN INSTRUMENT
--------------------
Append one `Instrument(...)` to REGISTRY. `token` is the literal string that identifies the line.
`fields` are the `key=value` names whose distribution decides the verdict; leave it empty to get the
distribution of the whole verdict clause via `verdict_re`. Keep the `why` one line — it is printed
above the distribution and is what tells a future reader what a bad shape looks like.
"""

from __future__ import annotations

import argparse
import collections
import os
import re
import statistics
import sys
from dataclasses import dataclass, field as dc_field

ANCHOR = "GloomhavenVR] "


@dataclass
class Instrument:
    """One standing instrument and the field that decides its verdict."""

    token: str
    why: str
    fields: tuple[str, ...] = ()
    verdict_re: str | None = None
    joint: bool = False
    """Report the fields TOGETHER as one tuple rather than one distribution each.

    This matters more than it looks. `RECESS CARD FX` prints both `look=` and `source=`, and the
    defect is a CO-OCCURRENCE — `look=NONE` beside `source=widget` — which two separate
    distributions cannot show. A joint distribution is the only shape that makes it visible.
    """
    numeric_re: str | None = None
    """A capture group over a numeric field to scan for order-of-magnitude outliers."""


REGISTRY: list[Instrument] = [
    Instrument(
        token="RECESS CARD FX",
        why="which resolver answered, and what look it gave. `look=NONE, source=widget` on a card "
            "that was used is the durable state being overwritten by a finished animation.",
        fields=("look", "source", "applied"),
        joint=True,
    ),
    Instrument(
        token="CARD FACE RULE",
        why="which rule chose a peer card's face. A rule that NEVER appears is unreachable; that is "
            "how ModBuild 478 found three of six FaceRule values had no caller.",
        verdict_re=r"chosen by ([A-Z][A-Z ]+?) —",
    ),
    Instrument(
        token="PEER CARD FACE GAP",
        why="OPEN / CLOSED / STUCK per slab. STUCK is a card that will stay blank for the life of "
            "the print. Read the named images: the mod's own PokePads are NOT missing art.",
        verdict_re=r"— (OPEN|CLOSED|STUCK)|(CLOSED) after",
    ),
    Instrument(
        token="ARC ORDER NOT APPLIED",
        why="why a peer's hand-fan order was not applied. 'not a permutation' is the serious one; "
            "'no fronts' and 'none stated' each mean something different and are easy to conflate.",
        fields=("reason",),
        verdict_re=r"reason=([a-z ]+?) \(",
    ),
    Instrument(
        token="FAN ARC ORDER SENT",
        why="SENT vs WITHHELD on the producer side. Compare against the receiver's "
            "ARC ORDER NOT APPLIED — the two have contradicted each other before.",
        verdict_re=r"FAN ARC ORDER SENT: ([A-Z]+)",
    ),
    Instrument(
        token="FAN ORDER MIRROR",
        why="DIVERGED means the owner's arc is not the order every observer re-derives, i.e. the "
            "rightmost card differs between machines.",
        verdict_re=r"FAN ORDER MIRROR: ([A-Z]+)",
    ),
    Instrument(
        token="ANONYMOUS RECESS",
        why="a peer round slot drawn as an anonymous BACK because this client cannot name the card. "
            "Read the pile counts on the same line before calling it a defect.",
        verdict_re=r"round slot (\d+) is OCCUPIED",
    ),
    Instrument(
        token="DOCK MIRROR",
        why="whether the mirrored use-bar symbols resolved. A refusal means the tiles are anonymous, "
            "which is 'ich sehe die Symbole nicht'.",
        verdict_re=r"DOCK MIRROR: ([a-z ].{0,60}?)(?: —|\.|,)",
    ),
    Instrument(
        token="PICK FLOW SUSPENDED",
        why="ModBuild 478's deadlock remedy firing. Zero lines in a session with a burn pick means "
            "the remedy never ran, which makes any 'no improvement' reading meaningless.",
        verdict_re=r"PICK FLOW SUSPENDED",
    ),
    Instrument(
        token="USE BARS: docked",
        why="the docked bar's world rect. A jump here is a fit that did not commit — the "
            "giant-text failure mode. The 2026-09-07 round had 8,515 m beside two of 0,976 m.",
        numeric_re=r"world rect ([0-9]+[.,][0-9]+)x[0-9]+[.,][0-9]+ m",
    ),
    Instrument(
        token="PANEL SUPERSAMPLE failed",
        why="a throw in the per-frame sync. ONE LINE IS NOT ONE THROW: the counter behind it was a "
            "session-wide bool until ModBuild 479, so a throw repeating every frame printed once "
            "while paying a full teardown and rebuild forever. Read the ENGAGE lines after it.",
        verdict_re=r"(PANEL SUPERSAMPLE failed)",
    ),
    Instrument(
        token="DECISION LABEL MASK",
        why="the short-rest card-name mask. ZERO lines across a session where a short rest ran means "
            "the mask never masked anything — the 2026-09-07 item 7 leak read exactly that way.",
        verdict_re=r"(DECISION LABEL MASK)",
    ),
    Instrument(
        token="MIRRORED ARC ORDER",
        why="the receiver's view of a peer's fan. `recordListLen=0` beside a non-zero `arc=` is the "
            "receiver holding no wire order at all for a fan it is drawing.",
        fields=("arc", "model", "recordListLen", "held", "suppressed"),
        joint=True,
    ),
]

KV_RE_CACHE: dict[str, re.Pattern[str]] = {}


def kv_re(name: str) -> re.Pattern[str]:
    """`name=value`, where a value runs to the next comma, semicolon, or sentence end."""
    if name not in KV_RE_CACHE:
        # A value runs to the next comma/semicolon/sentence end, and may be TWO words ("none
        # stated", "no fronts") — but the second word is refused if it looks like the next key,
        # i.e. if it contains an '='. Without that guard every value swallows the field after it.
        KV_RE_CACHE[name] = re.compile(
            rf"\b{re.escape(name)}=([^,;.\s]+(?:\s+(?![^\s=]*=)[^,;.\s]+)?)")
    return KV_RE_CACHE[name]


def anchored_lines(path: str, token: str, window: int) -> list[str]:
    """Every line whose MESSAGE BODY (after the `GloomhavenVR] ` anchor) contains `token` early.

    The window is the whole defence against prose. A line that mentions another instrument's token
    inside its explanatory tail is not an instance of that instrument, and this project has twice
    named a wrong cause by counting exactly those.
    """
    out: list[str] = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            cut = line.find(ANCHOR)
            if cut < 0:
                continue
            body = line[cut + len(ANCHOR):]
            at = body.find(token)
            if 0 <= at <= window:
                out.append(body.rstrip("\n"))
    return out


def distribution(lines: list[str], inst: Instrument) -> collections.Counter:
    counter: collections.Counter = collections.Counter()
    if inst.fields:
        for body in lines:
            values = []
            for name in inst.fields:
                m = kv_re(name).search(body)
                values.append(f"{name}={m.group(1)}" if m else f"{name}=<absent>")
            if inst.joint:
                counter[", ".join(values)] += 1
            else:
                for v in values:
                    counter[v] += 1
        return counter
    if inst.verdict_re:
        rx = re.compile(inst.verdict_re)
        for body in lines:
            m = rx.search(body)
            counter[next((g for g in m.groups() if g), "<no capture>") if m else "<unparsed>"] += 1
        return counter
    if inst.numeric_re:
        rx = re.compile(inst.numeric_re)
        for body in lines:
            m = rx.search(body)
            counter[m.group(1) if m else "<unparsed>"] += 1
        return counter
    counter["<line>"] = len(lines)
    return counter


def outliers(lines: list[str], inst: Instrument,
             factor: float) -> list[tuple[float, str]]:
    """Values an order of magnitude off this field's own median, in this same log.

    Deliberately median-relative and deliberately crude. It is a pointer, not a verdict: a field
    with a genuinely bimodal population will light up here and that is fine, because the cost of
    reading two extra lines is nil and the cost of missing an 8.5 m window is a hardware round.
    """
    if not inst.numeric_re:
        return []
    rx = re.compile(inst.numeric_re)
    vals: list[tuple[float, str]] = []
    for body in lines:
        m = rx.search(body)
        if not m:
            continue
        try:
            vals.append((float(m.group(1).replace(",", ".")), body[:160]))
        except ValueError:
            continue
    if len(vals) < 3:
        return []
    med = statistics.median(v for v, _ in vals)
    if med == 0:
        return []
    return [(v, b) for v, b in vals if v / med >= factor or (v and med / v >= factor)]


def report(path: str, only: str | None, window: int, want_outliers: bool,
           factor: float) -> None:
    print("=" * 100)
    print(f"{path}   ({os.path.getsize(path):,} bytes)")
    print("=" * 100)
    silent: list[str] = []
    for inst in REGISTRY:
        if only and only.lower() not in inst.token.lower():
            continue
        lines = anchored_lines(path, inst.token, window)
        if not lines:
            silent.append(inst.token)
            continue
        print(f"\n── {inst.token}  ({len(lines)} anchored line(s))")
        print(f"   {inst.why}")
        for value, count in sorted(distribution(lines, inst).items(),
                                   key=lambda kv: (-kv[1], kv[0])):
            print(f"     {count:6d}  {value}")
        if want_outliers:
            for value, body in outliers(lines, inst, factor):
                print(f"     OUTLIER {value}  {body}")
    if silent:
        print("\n── SILENT — zero anchored lines. A token that never fired IS a reading:")
        print("   nothing exercised it, or it is unreachable, or its gate never opened. Any verdict")
        print("   that depends on one of these is a verdict about nothing.")
        for token in silent:
            print(f"     {token}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("logs", nargs="+")
    ap.add_argument("--only", help="substring of an instrument token; report only that one")
    ap.add_argument("--token", help="ad-hoc: an arbitrary instrument token not in the registry")
    ap.add_argument("--field", action="append", default=[],
                    help="ad-hoc: a key=value field name to distribute (repeatable)")
    ap.add_argument("--token-window", type=int, default=200,
                    help="how far into the message body the token may sit (default 200)")
    ap.add_argument("--outliers", action="store_true",
                    help="also flag numeric values far off their own median")
    ap.add_argument("--outlier-factor", type=float, default=5.0,
                    help="how many times its own median a value must be to be flagged (default 5; "
                         "10 was measured to miss the 8.7x case this exists for)")
    args = ap.parse_args()

    if args.token:
        inst = Instrument(token=args.token, why="ad-hoc", fields=tuple(args.field), joint=True)
        for path in args.logs:
            lines = anchored_lines(path, inst.token, args.token_window)
            print(f"\n{path}: {inst.token} — {len(lines)} anchored line(s)")
            if not lines:
                print("     SILENT — zero anchored lines.")
                continue
            for value, count in sorted(distribution(lines, inst).items(),
                                       key=lambda kv: (-kv[1], kv[0])):
                print(f"     {count:6d}  {value}")
        return 0

    for path in args.logs:
        if not os.path.exists(path):
            print(f"missing: {path}", file=sys.stderr)
            return 2
        report(path, args.only, args.token_window, args.outliers, args.outlier_factor)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
