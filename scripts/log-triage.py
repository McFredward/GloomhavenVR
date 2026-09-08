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

A READING'S BUILD IS PART OF THE READING (added 2026-09-08, after the review round)
-----------------------------------------------------------------------------------
Four of the five reviews of 2026-09-07 turned on a log being read as current when it was not.

  * R1: the two ModBuild **478** drops contain 100 pile-browse census rows naming
    `ShowRoundCardFronts(actor)=false`. That is the **pre-fix** behaviour ModBuild 479 replaced.
    "Do not read those rows as evidence about the current build: they measure the code the 479
    pile-fan change replaced."
  * R4: the use-bar icons DREW on 478 through a fallback that 479 deleted. The 478 reading cannot
    be re-taken, and read as current it says the opposite of the truth.
  * R5: an audit dated ModBuild 371 had every claim re-derived at 479, and §5 lists where it had
    gone wrong in the meantime.

So this script now reads the build banner (`GloomhavenVR ModBuild <N>`) out of each log, prints it
in the file header, refuses quietly-wrong comparisons with `--expect-build`, and says so loudly when
two logs from "the same session" are not the same build. A log with NO banner is reported as such
rather than assumed current — the banner is Note-level and survives every verbosity that prints at
all, so its absence is itself a finding.

SILENT ON A QUESTION IS NOT THE SAME AS ANSWERING IT (added 2026-09-08)
-----------------------------------------------------------------------
The existing SILENT section covers a token that never fired. R1 found the sharper case: an
instrument that fired **seventeen times** and asked the question **zero** times.

    17 of 17 `[Cards] Pile fan content` lines read `0 left on the control board`. The divergence
    F1 needs has not occurred once. So F1 is not falsified by these logs — it is UNASKED by them.

A distribution alone reads that as a clean sweep. An instrument may therefore declare `decisive` —
the pattern that means this line was in the state that DECIDES the question — and a token with
lines but no decisive line is reported as SILENT ON THE QUESTION, with the count, so "we looked and
it was fine" cannot be written down when nothing looked.

USAGE
-----
    python3 scripts/log-triage.py <log> [<log> ...]
    python3 scripts/log-triage.py --expect-build 480 <log>   # refuse a stale drop
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
    decisive: str | None = None
    """The pattern that means this line was in the state that DECIDES the question.

    A token can fire hundreds of times and never once be in the state the question is about.
    R1 measured exactly that: 17 of 17 `Pile fan content` lines read `0 left on the control
    board`, so the divergence its finding needed had not occurred once — the log was SILENT ON
    the question, not exonerating for it. Without this field a distribution of seventeen
    identical healthy values reads as a clean sweep, which is how that reading nearly became
    "we checked and it was fine".
    """
    decisive_means: str = ""
    """One line: what a decisive line would show, printed when there are none."""


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
            "which is 'ich sehe die Symbole nicht'. R4 F1: THIS LINE READS LIKE SUCCESS WHEN HALF "
            "THE BAR IS ANONYMOUS — it reports the slots it DID light and has no field for the "
            "ones the sender withheld, so `1 … 1 of them resolved from the OWNER'S OWN record-45 "
            "id` is what a two-slot prompt showing one icon and one blank tile prints. Read the "
            "count against how many slots the prompt actually had; the line cannot tell you.",
        verdict_re=r"DOCK MIRROR: (\d+ mirrored use-bar slot\(s\)|no mirrored use-bar symbol"
                   r"|use-bar SYMBOLS resolved locally)",
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

    # ── ADDED 2026-09-08 from the five reviews of 2026-09-07. Each of these is a falsifier a
    # review NAMED and could not read, and each is here so the next round does not have to
    # re-derive the grep. The `why` says which finding it settles.
    Instrument(
        token="Pile fan content",
        why="R1 F1's convicting reading, and it states the defect in ONE line: a non-zero "
            "'left on the control board' while a discard fan is open IS the owner/mirror "
            "divergence. Nothing else has to be correlated to see it.",
        numeric_re=r"; (\d+) left on the control board",
        decisive=r"; [1-9][0-9]* left on the control board",
        decisive_means="a pile fan opened while one of its cards' visuals was still on the "
                       "control board. R1 measured 17 of 17 reading ZERO across the two "
                       "ModBuild 478 drops — so those logs are SILENT on F1, not exonerating, "
                       "and the hardware action (open a discard fan during a short rest) is "
                       "still required.",
    ),
    Instrument(
        token="PEER CARD FACE CENSUS",
        why="the observer half of R1 F1. A `pile browse[pN] 0 FRONT / M BACK` row whose rule is "
            "the CountMismatch sentence is the belt tripping. BEWARE THE BUILD: the 100 "
            "all-backs rows in the ModBuild 478 drops name a DIFFERENT rule "
            "(ShowRoundCardFronts(actor)=false) and are the pre-fix behaviour 479 replaced.",
        verdict_re=r"(pile browse\[p\d+\] \d+ FRONT / \d+ BACK)",
    ),
    Instrument(
        token="Pick banner SENT",
        why="R1 F3: extension record 7 publishes PlayTray.PickBannerText verbatim and is the one "
            "string channel of five that is neither masked nor identity-gated. A banner quoting "
            "a card NAME while the secret window is shut is the leak. There is no log line today "
            "that pairs record 7 with the phase, so this must be read against a census row's "
            "timestamp.",
        verdict_re=r"Pick banner SENT:? ?(.{0,70})",
    ),
    Instrument(
        token="BURN MIRROR SKIPPED",
        why="R3 F5's first half. Count it in the same window as CARD FX LOST — R3 could not rank "
            "that finding above PLAUSIBLE precisely because it could not establish the arrival "
            "ordering from source alone.",
        verdict_re=r"(BURN MIRROR SKIPPED)",
    ),
    Instrument(
        token="CARD FX LOST",
        why="R3 F5's second half, and the reason its own '1 loss in 4 this session' reading is "
            "not usable: one session, no denominator, no build stamp. Read the count here "
            "against the BURN MIRROR SKIPPED count from the SAME log.",
        verdict_re=r"(CARD FX LOST)",
    ),
    Instrument(
        token="GATE 3 (slot count)",
        why="R4 F3. THE VERDICT STRING IS WRONG and reading it as written costs a round: it "
            "blames 'a stale or mid-rebuild local bar; the next cadence tick normally clears "
            "it', and nothing in Resolve samples a timestamp, a rebuild epoch or a tick. The "
            "real cause is a permanent walk asymmetry (the receiver's walk lacks the sender's "
            "IsPlainRenderHidden term), which no cadence tick will ever clear. A REPEATING "
            "count here falsifies the string's own prediction.",
        verdict_re=r"(shows \d+ visible slot\(s\) against record 25's \d+)",
    ),
    Instrument(
        token="BURN ANIM STUCK",
        why="R5 F6's only reading — and R5's standing ruling is that a deadlock whose only "
            "handling is a log line is itself a defect. Whether the remedy covers every case is "
            "the next question and this count answers it.",
        verdict_re=r"(BURN ANIM STUCK)",
    ),
    Instrument(
        token="row not converted",
        why="R5 F4's DISCRIMINATING READING, and it is an ABSENCE — the hardest shape to grep. "
            "A session in which the dock docks and the row is invisible with ZERO of these warn "
            "lines is unreachable by construction unless F4 is what happened. So a SILENT "
            "reading here is the evidence, not the all-clear.",
        verdict_re=r"(row not converted)",
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


# ANCHORED LIKE EVERY OTHER GREP IN THIS FILE, and it was not. The docstring's own rule is that
# a line only counts once `GloomhavenVR] ` has been seen, because BepInEx interleaves every
# plugin's output into one file and a foreign line naming our banner would be read as ours. This
# one matched anywhere in the line, so a peer's message quoted inside another plugin's log line —
# `[Message: BepInEx] … GloomhavenVR ModBuild 478` — would count as a SECOND build and turn a
# clean single-build drop into "2 DIFFERENT builds in one log — the log spans a reinstall".
# Latent today (no shipped line prints that shape; VersionGuard prints `(Build N)`, DesyncWatch
# `build   : ModBuild N`, RemoteBoardFurniture `peer ModBuild N`), and left latent is exactly how
# the next such line gets written.
BUILD_RE = re.compile(re.escape(ANCHOR) + r".*?GloomhavenVR ModBuild (\d+)")

# ---- WHAT TIER IS THIS TOKEN WRITTEN AT, AND CAN THE DEFAULT LOG LEVEL PRINT IT? -------------
#
# VRLog.Note is visible from VRLogLevel.Info, which is the SHIPPED default (Defaults.Plugin.cs).
# VRLog.Info, .Warn and .Debug are visible only from VRLogLevel.Debug. So a token written with
# VRLog.Info does not appear in a default-level drop at all — and this script would then report
# it as SILENT, whose printed explanation offers three causes ("nothing exercised it, or it is
# unreachable, or its gate never opened") and not the fourth, real one: the tester's log level
# could not carry it. Four registry tokens are in that position today (`Pile fan content`,
# `USE BARS: docked`, `Pick banner SENT`, `DOCK MIRROR` at one of its two sites), including the
# one instrument that declares `decisive` — so `SILENT ON THE QUESTION` could never fire for it
# on a default drop, and the SILENT list would read as a behavioural finding.
#
# The tier is read from the SOURCE rather than declared beside each Instrument, because a
# declaration is a second copy that goes stale the day someone changes the call. Absent sources
# (this script is often run against a log alone) degrade to "unknown" and print nothing.
SRC = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "src")
DEFAULT_LEVEL_TIERS = {"Note", "Alert", "Error"}


def token_tiers() -> dict[str, set[str]]:
    """token -> the set of VRLog methods that write a string containing it, from src/."""
    tiers: dict[str, set[str]] = {}
    if not os.path.isdir(SRC):
        return tiers
    call = re.compile(r"VRLog\.(Note|Info|Warn|Debug|Alert|Error)\s*\(", re.S)
    tokens = [i.token for i in REGISTRY]
    for root, dirs, files in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin")]
        for f in files:
            if not f.endswith(".cs"):
                continue
            try:
                with open(os.path.join(root, f), encoding="utf-8", errors="replace") as fh:
                    body = fh.read()
            except OSError:
                continue
            for tok in tokens:
                start = 0
                while True:
                    at = body.find(tok, start)
                    if at < 0:
                        break
                    start = at + 1
                    # the nearest VRLog.<tier>( that opens before this literal, within one
                    # statement's reach — long interpolated messages run to a few hundred chars
                    hits = list(call.finditer(body, max(0, at - 2000), at))
                    if hits:
                        tiers.setdefault(tok, set()).add(hits[-1].group(1))
    return tiers


def builds_in(path: str) -> list[int]:
    """Every distinct ModBuild the banner claims in this log, in first-seen order.

    Plugin.cs prints `GloomhavenVR ModBuild <N> (assembly …)` at Note level, which survives every
    verbosity that can print at all — it was added after a hardware round was accidentally run on
    the previous build and its log misread as a fix failure. More than one value means the log
    spans a reinstall, and every reading in it needs a side.
    """
    seen: list[int] = []
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            m = BUILD_RE.search(line)
            if m:
                value = int(m.group(1))
                if value not in seen:
                    seen.append(value)
    return seen


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
           factor: float, expect_build: int | None) -> list[int]:
    builds = builds_in(path)
    print("=" * 100)
    print(f"{path}   ({os.path.getsize(path):,} bytes)")
    if not builds:
        print("   BUILD: NOT STATED. This log carries no `GloomhavenVR ModBuild <N>` banner, and")
        print("   the banner is Note-level — it survives every verbosity that prints anything at")
        print("   all. So this log cannot say which build produced it, and no reading in it may be")
        print("   quoted as being about the current one.")
    elif len(builds) == 1:
        print(f"   BUILD: ModBuild {builds[0]}")
    else:
        print(f"   BUILD: {len(builds)} DIFFERENT builds in one log — "
              + ", ".join(str(b) for b in builds))
        print("   The log spans a reinstall. Every reading below is a mixture until you split it,")
        print("   and a fix's 'before' and 'after' are both in here with nothing separating them.")
    if expect_build is not None and builds != [expect_build]:
        # "STALE" is only right when the drop is OLDER. A newer one is not stale, it is a drop
        # of code this question was not asked about — same refusal, accurate word, because the
        # remedy differs: an older drop wants a re-test, a newer one wants a re-read of what
        # changed in between.
        newer = bool(builds) and min(builds) > expect_build
        print(f"   *** {'NEWER THAN ASKED FOR' if newer else 'STALE OR WRONG DROP'}: asked for "
              f"ModBuild {expect_build}, this log says "
              + (", ".join(str(b) for b in builds) if builds else "nothing") + ".")
        print("   *** A reading from an earlier build is not a weaker reading, it is a reading")
        print("   *** about DIFFERENT CODE. R1 found 100 census rows in the ModBuild 478 drops")
        print("   *** measuring exactly the code the 479 change replaced.")
    print("=" * 100)
    silent: list[str] = []
    unasked: list[tuple[str, int, str]] = []
    for inst in REGISTRY:
        if only and only.lower() not in inst.token.lower():
            continue
        lines = anchored_lines(path, inst.token, window)
        if not lines:
            silent.append(inst.token)
            continue
        decisive_count = None
        if inst.decisive:
            rx = re.compile(inst.decisive)
            decisive_count = sum(1 for body in lines if rx.search(body))
            if decisive_count == 0:
                unasked.append((inst.token, len(lines), inst.decisive_means))
        suffix = "" if decisive_count is None else \
            f", {decisive_count} of them in the state that decides the question"
        print(f"\n── {inst.token}  ({len(lines)} anchored line(s){suffix})")
        print(f"   {inst.why}")
        for value, count in sorted(distribution(lines, inst).items(),
                                   key=lambda kv: (-kv[1], kv[0])):
            print(f"     {count:6d}  {value}")
        if want_outliers:
            for value, body in outliers(lines, inst, factor):
                print(f"     OUTLIER {value}  {body}")
    if unasked:
        print("\n── SILENT ON THE QUESTION — the instrument fired and never once asked it.")
        print("   This is NOT the same as a clean reading and must never be written down as one.")
        print("   A distribution of identical healthy values here means the state that would have")
        print("   shown the defect did not occur, so the log neither confirms nor falsifies it.")
        for token, count, means in unasked:
            print(f"     {token}: {count} line(s), 0 decisive.")
            if means:
                print(f"       a decisive line would be: {means}")
    if silent:
        tiers = token_tiers()
        print("\n── SILENT — zero anchored lines. A token that never fired IS a reading:")
        print("   nothing exercised it, or it is unreachable, or its gate never opened — or the")
        print("   drop's log level could not carry it (see the tier note below). Any verdict that")
        print("   depends on one of these is a verdict about nothing.")
        below = []
        for token in silent:
            wrote = tiers.get(token, set())
            if wrote and not (wrote & DEFAULT_LEVEL_TIERS):
                below.append((token, sorted(wrote)))
                print(f"     {token}   [written with VRLog.{'/'.join(sorted(wrote))} — "
                      f"DEBUG TIER ONLY]")
            else:
                print(f"     {token}")
        if below:
            print("\n   *** THE BRACKETED ONES ARE NOT A FINDING. VRLog.Note prints from the SHIPPED")
            print("   *** default level; VRLog.Info/Warn/Debug print only at Debug. Their silence")
            print("   *** here says the log level was default, not that the code did not run. Ask")
            print("   *** for a Debug-level drop, or mark the site // HW-VERIFY and move it to")
            print("   *** VRLog.Note (scripts/check-hw-verify.py then holds it to that).")
    return builds


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
    ap.add_argument("--expect-build", type=int,
                    help="the ModBuild these logs are supposed to be from. A drop that says "
                         "anything else is reported loudly — a reading from an earlier build is "
                         "not a weaker reading, it is a reading about different code.")
    ap.add_argument("--outlier-factor", type=float, default=5.0,
                    help="how many times its own median a value must be to be flagged (default 5; "
                         "10 was measured to miss the 8.7x case this exists for)")
    args = ap.parse_args()

    if args.token:
        inst = Instrument(token=args.token, why="ad-hoc", fields=tuple(args.field), joint=True)
        for path in args.logs:
            # --expect-build IS HONOURED HERE TOO. This branch used to return before report(),
            # which is where the build banner is read — so `--token X --expect-build 480` printed
            # a distribution and no build line at all, silently answering a question about the
            # wrong build. The ad-hoc path is the one a person reaches for mid-investigation,
            # i.e. exactly when the drop's provenance matters most.
            if args.expect_build is not None:
                found = builds_in(path)
                if found != [args.expect_build]:
                    print(f"\n*** STALE OR WRONG DROP: asked for ModBuild {args.expect_build}, "
                          f"{path} says "
                          + (", ".join(str(b) for b in found) if found else "nothing") + ".")
                    print("*** A reading from another build is not a weaker reading, it is a")
                    print("*** reading about DIFFERENT CODE.")
                else:
                    print(f"\n{path}: ModBuild {args.expect_build}, as asked.")
            lines = anchored_lines(path, inst.token, args.token_window)
            print(f"\n{path}: {inst.token} — {len(lines)} anchored line(s)")
            if not lines:
                print("     SILENT — zero anchored lines.")
                continue
            for value, count in sorted(distribution(lines, inst).items(),
                                       key=lambda kv: (-kv[1], kv[0])):
                print(f"     {count:6d}  {value}")
        return 0

    per_log: list[tuple[str, list[int]]] = []
    for path in args.logs:
        if not os.path.exists(path):
            print(f"missing: {path}", file=sys.stderr)
            return 2
        per_log.append((path, report(path, args.only, args.token_window, args.outliers,
                                     args.outlier_factor, args.expect_build)))

    # TWO MACHINES ARE ONLY ONE SESSION IF THEY RAN THE SAME BUILD. The mod's own MP handshake
    # compares this exact number and blocks a mismatched table, so two drops that disagree are
    # either not from one session or one of them predates the install — and a host/peer
    # comparison across them is a comparison of two different programs.
    if len(per_log) > 1:
        stamps = {tuple(b) for _, b in per_log}
        if len(stamps) > 1:
            print("\n" + "=" * 100)
            print("*** THESE LOGS ARE NOT THE SAME BUILD. Do not compare them field by field.")
            for path, b in per_log:
                print(f"      {path}: " + (", ".join(str(x) for x in b) if b else "no banner"))
            print("*** The MP handshake compares this number and blocks a mismatched table, so")
            print("*** either these are not one session, or one drop predates its own install.")
            print("=" * 100)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
