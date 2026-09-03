#!/usr/bin/env python3
"""
User-facing docs must exist in English AND German, and the two must not drift apart.

WHY THIS EXISTS
---------------
The mod's own UI ships English and German, and the person it is built for reads German. So the
docs a PLAYER reads ship in both too (README, install guide, playing guide, and the INSTALL text
inside the release zip). Docs for developers or for the AI stay English-only, by ruling.

A translated page rots in three silent ways, and all three look fine in review:

  1. someone adds a section to the English file and not to the German one, so the German reader
     never learns the thing exists;
  2. someone renames a heading and the in-page table of contents starts scrolling nowhere;
  3. a German page links onward into an English page that HAS a German twin, and the reader is
     dropped back into English mid-guide for no reason.

None of these break a build, none of them throw, and the only person who ever finds out is the
German reader — who is the one the German was written for. This check is what makes them tell you.

WHAT IT DOES NOT DO
-------------------
It does not diff prose and it does not judge a translation. It compares HEADING COUNTS, which is
cheap and honest: it catches a whole section going missing without pretending to know whether a
paragraph says the same thing in both languages. A human reads the German.

Usage: python3 scripts/check-docs-i18n.py       (from anywhere; paths resolve against the repo)
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The user-facing set. Adding a doc a PLAYER reads means adding it here and writing the twin.
# Everything not listed is developer- or AI-facing and is English-only ON PURPOSE:
# docs/CAMERA-POLICY.md, docs/CI-CD.md, docs/DEVELOPING.md, docs/INTERFACES-*.md,
# docs/NET-ACTION-SURFACE.md, docs/PATCH-INVENTORY.md (generated), docs/PATCH-NOTES.md,
# docs/TESTING-*.md, docs/img/README.md, packaging/release-highlights/README.md,
# packaging/THIRD-PARTY.txt, LICENSE, .planning/**, unity/**.
MARKDOWN = [
    # (english path, german path, path from the file's own directory to docs/img)
    ("README.md",       "README.de.md",       "docs/img"),
    ("INSTALL.md",      "INSTALL.de.md",      "docs/img"),
    ("docs/PLAYING.md", "docs/PLAYING.de.md", "img"),
]

# Plain text shipped inside the release zip — no switcher, no links, but still a pair. Each must
# name the file the other language renders to, so a player who opens the wrong one sees the other.
PLAIN = [
    ("packaging/INSTALL.txt.in", "INSTALL-DEUTSCH.txt",
     "packaging/INSTALL.de.txt.in", "INSTALL.txt"),
]

# How far into a file the switcher may sit and still count as "in the header". README.md opens
# with a logo block and a badge row before it; the two guides put it directly under their title.
HEADER_LINES = 40

bad = []


def read(rel):
    with open(os.path.join(ROOT, rel), encoding="utf-8") as fh:
        return fh.read()


def switcher(imgdir, lang, twin):
    """The one canonical switcher block. Every user-facing page carries this exact shape, so
    that it reads as one mechanism rather than as three similar-looking headers."""
    en = '<img src="%s/flag-en.png" width="24" alt="English">' % imgdir
    de = '<img src="%s/flag-de.png" width="24" alt="Deutsch">' % imgdir
    if lang == "en":
        left, right = "%s&nbsp;<b>English</b>" % en, '<a href="%s">%s&nbsp;Deutsch</a>' % (twin, de)
    else:
        left, right = '<a href="%s">%s&nbsp;English</a>' % (twin, en), "%s&nbsp;<b>Deutsch</b>" % de
    return ('<p align="center">\n  %s\n  &nbsp;&nbsp;|&nbsp;&nbsp;\n  %s\n</p>' % (left, right))


def headings(text):
    """ATX headings outside fenced code blocks. Returns the raw heading text."""
    out, fenced = [], False
    for line in text.splitlines():
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if not fenced:
            m = re.match(r"^(#{1,6})\s+(.*?)\s*$", line)
            if m:
                out.append(m.group(2))
    return out


def slug(heading):
    """GitHub's anchor slug: strip inline markup, lowercase, drop everything that is not a word
    character, a space or a hyphen, then spaces to hyphens. Unicode letters survive, so an
    umlaut in a German heading is part of its anchor."""
    s = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", heading)   # [text](url) -> text
    s = re.sub(r"<[^>]+>", "", s)                          # inline HTML
    s = s.replace("`", "").replace("*", "")
    s = s.lower()
    s = re.sub(r"[^\w\- ]", "", s, flags=re.UNICODE)
    return s.replace(" ", "-")


def anchors(text):
    """Every anchor a reader can land on, with GitHub's -1/-2 suffixes for duplicates."""
    seen, out = {}, set()
    for h in headings(text):
        s = slug(h)
        n = seen.get(s, 0)
        seen[s] = n + 1
        out.add(s if n == 0 else "%s-%d" % (s, n))
    return out


# ---- 1. every user-facing doc has its twin, and vice versa --------------------------------
for pair in [(e, g) for e, g, _ in MARKDOWN] + [(e, g) for e, _, g, _ in PLAIN]:
    for this, other in (pair, pair[::-1]):
        if os.path.isfile(os.path.join(ROOT, this)) and not os.path.isfile(os.path.join(ROOT, other)):
            bad.append("%s exists but %s does not — every user-facing doc ships in English AND "
                       "German; write the twin, or move the doc out of the user-facing set at "
                       "the top of this script" % (this, other))
    for f in pair:
        if not os.path.isfile(os.path.join(ROOT, f)):
            bad.append("%s is listed as user-facing but does not exist" % f)

if bad:
    print("error: user-facing docs are missing a translation:", file=sys.stderr)
    for b in bad:
        print("  " + b, file=sys.stderr)
    sys.exit(1)

for en_rel, de_rel, imgdir in MARKDOWN:
    en, de = read(en_rel), read(de_rel)

    # ---- 2. the switcher header, in the one canonical shape, near the top ------------------
    for rel, text, lang, twin in ((en_rel, en, "en", os.path.basename(de_rel)),
                                  (de_rel, de, "de", os.path.basename(en_rel))):
        want = switcher(imgdir, lang, twin)
        if want not in text:
            bad.append("%s: missing the language switcher, or its shape differs from the other "
                       "pages. It must contain exactly:\n      %s"
                       % (rel, want.replace("\n", "\n      ")))
        elif want not in "\n".join(text.splitlines()[:HEADER_LINES]):
            bad.append("%s: the switcher is further down than line %d — it belongs in the page "
                       "header, before any prose, so the reader picks a language before reading "
                       "an English sentence" % (rel, HEADER_LINES))
        for flag in ("flag-en.png", "flag-de.png"):
            img = os.path.normpath(os.path.join(ROOT, os.path.dirname(rel), imgdir, flag))
            if not os.path.isfile(img):
                bad.append("%s: the switcher points at %s/%s, which does not exist"
                           % (rel, imgdir, flag))

    # ---- 3. heading counts must agree -----------------------------------------------------
    n_en, n_de = len(headings(en)), len(headings(de))
    if n_en != n_de:
        bad.append("%s has %d headings, %s has %d — a section was added or dropped on one side "
                   "only; the German must carry every section the English has, in the same order"
                   % (en_rel, n_en, de_rel, n_de))

    # ---- 4. a German page must not send the reader back into English needlessly ------------
    #        (the switcher's own link to the twin is raw HTML, so it is not scanned here)
    for target in re.findall(r"\[[^\]]*\]\(([^)#\s]+)(?:#[^)]*)?\)", de):
        if not target.endswith(".md") or target.endswith(".de.md") or "://" in target:
            continue
        twin = target[:-3] + ".de.md"
        if os.path.isfile(os.path.normpath(os.path.join(ROOT, os.path.dirname(de_rel), twin))):
            bad.append("%s links to %s, but %s exists — a German page links to the German twin "
                       "wherever there is one" % (de_rel, target, twin))

    # ---- 5. every in-page anchor must resolve ---------------------------------------------
    for rel, text in ((en_rel, en), (de_rel, de)):
        have = anchors(text)
        for frag in re.findall(r"\[[^\]]*\]\(#([^)]+)\)", text):
            if frag not in have:
                bad.append("%s: the link to #%s resolves to no heading on the page — a table of "
                           "contents that scrolls nowhere is worse than none" % (rel, frag))
    # Cross-file fragments into the twin (README.de.md -> PLAYING.de.md#...) must resolve too.
    # README's own nav row is raw HTML, and a nav link that scrolls nowhere rots exactly as
    # quietly as a markdown one, so both link syntaxes are scanned here. Rule 4 above stays
    # markdown-only on purpose: the switcher's link to the English twin is HTML and is correct.
    for rel, text in ((en_rel, en), (de_rel, de)):
        pairs = (re.findall(r"\[[^\]]*\]\(([^)#\s]+)#([^)]+)\)", text)
                 + re.findall(r'<a href="([^"#\s]+)#([^"]+)"', text))
        for target, frag in pairs:
            path = os.path.normpath(os.path.join(ROOT, os.path.dirname(rel), target))
            if target.endswith(".md") and os.path.isfile(path):
                with open(path, encoding="utf-8") as fh:
                    if frag not in anchors(fh.read()):
                        bad.append("%s: the link to %s#%s resolves to no heading in that file"
                                   % (rel, target, frag))

# ---- the plain-text pair: each must name the other, so opening the wrong one is a glance ----
for rel, names, twin_rel, twin_names in ((p[0], p[1], p[2], p[3]) for p in PLAIN):
    for this, other in ((rel, names), (twin_rel, twin_names)):
        head = "\n".join(read(this).splitlines()[:8])
        if other not in head:
            bad.append("%s does not name %s in its first 8 lines — a player who opens the wrong "
                       "language must see at a glance that the other one is next to it"
                       % (this, other))

if bad:
    print("error: user-facing docs and their German twins have drifted:", file=sys.stderr)
    for b in bad:
        print("  " + b, file=sys.stderr)
    sys.exit(1)

pairs = len(MARKDOWN) + len(PLAIN)
print("docs i18n: %d user-facing docs ship in English and German (%d files) — switchers, heading "
      "counts, anchors and German-to-German links all agree"
      % (pairs, pairs * 2))
