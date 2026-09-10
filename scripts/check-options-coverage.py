#!/usr/bin/env python3
"""Fail the build when a setting exists but cannot be found in the VR options menu.

WHY THIS EXISTS. ModBuild 339 added `[WorldUI] BarHeightOffset` because the user asked for it
by hand ("die Lebensbalken sitzen zu hoch"). It shipped bound, defaulted, documented — and
uncurated, so it landed at the bottom of Erweitert ▸ Menüs & Tafeln in the "Allgemein" grab-bag
that the topic tree above it exists to empty. He tested ModBuild 340 and reported, verbatim:
"Weiterhin finde ich den offset für die healthbar nicht". The feature was delivered and
unreachable, and nothing in the build said so.

Nothing here judges taste. It checks seven things a machine can decide, each of which has already
cost this project a round:

  1. ADVERTISED BUT ABSENT. Every (Section, Key) named in `VROptionsTab.4.Curated.cs`,
     `VROptionsTab.6.BoardTopic.cs` and `VROptionsTab.7.TopicTrees.cs` must be a key the mod
     actually binds. A typo there is silent at runtime: `Lookup` returns null and
     `BuildCurated` simply skips the row — the menu advertises a setting it never draws.
  2. TWO DOORS, ON PURPOSE OR BY ACCIDENT. A (Section, Key) curated twice is a design decision
     (the wall see-through has a deliberate second door, by user ruling) or a copy-paste. The
     deliberate ones are listed below WITH the ruling; anything else fails.
  3. A CAPTION THAT NAMES NOTHING. `CuratedEntry.CaptionKey` is a Loc id. A key Loc.cs does not
     hold makes `Loc.Mod` return the id itself, so the row is captioned "vr_o_barheight".
     The empty string is the DOCUMENTED fallback (use the config display name) and is allowed;
     a non-empty id that does not resolve is not.
  4. A SPLIT FAMILY. `[WorldUI] BarSizeScale`, `BarFixedSize` and `BarsOccluded` were curated
     and `BarHeightOffset` was not — one family, one heading, one member missing. That IS the
     ModBuild 340 defect. So: if a config Section has curated members sharing a leading word,
     every sibling with the same Section and leading word must be curated too, or be named in
     KNOWN_ORPHANS with the reason it is not. That set is FROZEN at the state the rule was
     written against (ModBuild 340), so the gate is on the delta: the check passes today and
     fails the moment a NEW key joins a curated family without a row.
  5. ONE ROW, TWO NAMES. A setting reachable from the everyday page AND from Erweitert carries
     two captions: the curated row's `CaptionKey` (a `Loc.Mod` id) and `Loc.ConfigDisplayName`
     (`Loc.ConfigNames.cs`). `Loc.ConfigNames.cs:161` already states the rule — "kept identical
     on purpose: ONE ROW, ONE NAME, BOTH DOORS" — and four rows were aligned by hand to obey it.
     Nothing enforced it, so by the 2026-09 redundancy audit TWENTY of the 82 rows that carry
     both names disagreed: `[Hands] LaserFingerOrigin` was "Laser-Ursprung" on the everyday page
     and "Laser ab Fingerspitze" under Erweitert, `[WorldUI] BarsOccluded` was "Lebensbalken
     hinter Wänden" and "Balken hinter Wänden", and six `BoardPitchMin/Max_*` rows carried the
     unit on one door only. So: for every curated row whose (Section, Key) also resolves through
     `ConfigNames` (exact key, then the per-style/per-board/per-pile family wildcard), the two
     EN strings and the two DE strings must be equal, or the pair must be named in
     NAME_DIFFERS_ON_PURPOSE with the reason. This does NOT merge the two tables: the everyday
     caption is allowed to be shorter than the browser's descriptive name — it just has to be a
     DECISION rather than an accident.
  6. A ROW WHOSE VALUES ARE C# IDENTIFIERS. Checks 3 and 5 look at the row's NAME. Nothing looked
     at what its DROPDOWN SAYS, and for an enum-typed entry that is a programmer's string:
     `ConfigCatalog.Classify` maps `t.IsEnum` to `ConfigKind.Choice`, and `BuildChoiceRow` labels
     each option with `choices[i].ToString()` — the raw member name, identical in both languages.
     `[Cards] BoardMoveMode` and `[WorldUI] WindowFacing` each needed a hand-built row to escape
     it ("the raw member names … English, in a German menu, on a row a player is meant to choose
     from", VROptionsTab.4.Curated.cs), and `[PeerBoardFade] Mode` was found the same way a third
     time — by the user, reading "Off" in a German menu ("Die Board-Transparenz Option im Dropdown
     'Off' sollte 'Permanent' heißen stattdessen"). Two hand-written fixes and no gate is how a
     defect class gets fixed three times. So: every OFFERED config entry whose type is one of the
     mod's enums must be built by a localizing row — a `HasSpecialRow` branch or a
     `HasVariantTiles` strip — or be named in ENUM_LABELS_NOT_LOCALIZED. Same shape as
     KNOWN_ORPHANS: the set is frozen at the state the rule was written against, so the gate is on
     the DELTA and the next enum dropdown cannot ship English into the German menu unnoticed.

     NOT COVERED, deliberately: the OTHER way a row gets identifier-shaped options, an
     `AcceptableValueList` or a `ConfigCatalog.CuratedChoices` set over strings ("auto"/"vdxr",
     "tilt"/"always", "window"/"screen"). Those values are what the player's .cfg must literally
     contain and what the code compares against, so the label and the stored value are the same
     object today; separating them is a design change per entry, not a missing translation. The
     five affected keys are listed at ENUM_LABELS_NOT_LOCALIZED's foot so the size of that class
     is written down rather than merely unmeasured.
  7. A ROW HANDED TO A PAGE THAT DOES NOT DRAW IT. `VROptionsTab.8.Dependencies.OwnPageRows` names
     the keys the catalog's own listings must NOT draw because a hand-built page owns them. The one
     key it has ever held, `[FigureGrab] OcclusionMapOffOnHeadCamera`, was drawn on Erweitert >
     Test-Ausloeser because filing it by its section put it under Haende > "Figuren-Offsets" and the
     user could not find it ("Ich konnte die OcclusionMapOffOnHeadCamera in Erweitert nirgends
     finden, wo ist sie?"); that A/B answered on ModBuild 467 and the key is gone, so the set is
     EMPTY at the time of writing. The hand-off has exactly one failure mode: the page stops drawing
     a key and the filter keeps hiding it, and the setting is then reachable from NEITHER door with
     nothing saying so. So: every key in that set must be bound, and must be named literally by some
     other file under Options/ — the page that took responsibility for it.

     AND THE CHECK MUST STAY MEANINGFUL WHILE THE SET IS EMPTY, which is a different requirement
     from the one above. `own_page_rows()` returns (found, keys): a set it could no longer LOCATE
     (renamed, moved, reformatted) is a FAILURE, and a set it found and read as empty is a state the
     success line SAYS — "the OwnPageRows set was FOUND and is EMPTY". Before that split, both
     printed "0 row(s) handed to a page of their own", so a reader could not tell "no own-page keys"
     from "this check stopped looking", and a checker that silently stopped looking is the failure
     this file exists to prevent, pointed at itself.

WHAT IS NOT CHECKED, AND WHY. "Every key is reachable" is not checked because it is TRUE BY
CONSTRUCTION and checking it would be checking the wrong thing: Erweitert is the catalog's own
index and the catalog is a reflection walk over the live BepInEx registry
(`ConfigCatalog.Rebuild`), so a bound entry appears there whether anyone remembers it or not.
The only keys that leave the menu are the ones ConfigCatalog deliberately withholds
(`NotOffered`, `RetiredMarkers`), each with its reason written beside it, and the ones a hand-built
page has taken over (`OwnPageRows`) — which do not leave the menu at all, only the catalog's
listings, and which check 7 holds to that promise. What is NOT true by
construction — and what actually failed — is that a key is findable by someone who does not
know its name. That is what 1-4 measure.

Usage:
    check-options-coverage.py             # check; exit 1 on any failure
    check-options-coverage.py --report    # check, and print the coverage tables
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "src")
OPTIONS = os.path.join(SRC, "GloomhavenVR", "WorldUI", "Options")
LOC_DIR = os.path.join(SRC, "GloomhavenVR", "Core", "Loc")
SKIP_DIRS = {"obj", "bin"}


# ============================================================================================
#  Comment stripping (same reason as check-surface.py: this repo quotes keys in prose
#  constantly, and a quoted example counted as a binding makes the census lie).
# ============================================================================================

def strip_comments(text: str) -> str:
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            # verbatim string
            if i >= 1 and text[i - 1] == '@':
                out.append(c)
                i += 1
                while i < n:
                    if text[i] == '"':
                        if i + 1 < n and text[i + 1] == '"':
                            out.append('""')
                            i += 2
                            continue
                        out.append('"')
                        i += 1
                        break
                    out.append(text[i])
                    i += 1
                continue
            out.append(c)
            i += 1
            while i < n:
                if text[i] == '\\' and i + 1 < n:
                    out.append(text[i:i + 2])
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == '/' and i + 1 < n and text[i + 1] == '/':
            while i < n and text[i] != '\n':
                i += 1
            continue
        if c == '/' and i + 1 < n and text[i + 1] == '*':
            i += 2
            while i + 1 < n and not (text[i] == '*' and text[i + 1] == '/'):
                i += 1
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def cs_files():
    for base, dirs, names in os.walk(SRC):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for name in names:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


# ============================================================================================
#  1. The real key surface
#
#  `scripts/check-surface.py` counts only `.Bind("Section", "Key"` — both arguments literal.
#  That is the right instrument for ITS job (diffing two snapshots for a REMOVAL) and the wrong
#  one here: it sees 396 keys where the mod binds ~611, and every key it misses is a key this
#  checker would then call "advertised but absent". The two blind spots are both resolvable
#  from the source text:
#
#    * a per-module helper that supplies the section itself (Rig/ComfortSettings.Bind<T>), and
#    * interpolated keys — `$"{style}Scale"`, `$"BoardPitchMin_{board}"` — whose placeholder is
#      a loop variable over a literal string array or an enum.
#
#  Both are expanded below. An unexpandable interpolation degrades to a WILDCARD for its
#  section rather than to nothing: an unresolved `$"…{x}…"` must never turn into a false
#  "this curated key does not exist".
# ============================================================================================

BIND_LITERAL = re.compile(r'\.B[i]nd\s*(?:<[^>()]*>)?\s*\(\s*"([^"]*)"\s*,\s*"([^"]*)"')
BIND_INTERP = re.compile(r'\.B[i]nd\s*(?:<[^>()]*>)?\s*\(\s*"([^"]*)"\s*,\s*\$"([^"]*)"')
ENUM_DECL = re.compile(r'\benum\s+(\w+)\s*\{([^}]*)\}')
STR_ARRAY = re.compile(r'\bstring\s*\[\s*\]\s*(\w+)\s*=\s*(?:new\s*(?:string)?\s*\[\s*\]\s*)?\{([^}]*)\}')
FOREACH_ENUM = re.compile(r'\bforeach\s*\(\s*(\w+)\s+(\w+)\s+in\s+(?:System\.)?Enum\.GetValues')
ELEM_ASSIGN = re.compile(r'\bstring\s+(\w+)\s*=\s*(\w+)\s*\[\s*\w+\s*\]')
HOLE = re.compile(r'\{[^}]*\}')


def enum_members():
    """Every enum in the mod, name -> member list. Used to expand `foreach (ControlBoard b …)`."""
    found = {}
    for path in cs_files():
        with open(path, encoding="utf-8", errors="replace") as fh:
            body = strip_comments(fh.read())
        for name, members in ENUM_DECL.findall(body):
            values = []
            for raw in members.split(","):
                raw = raw.split("=")[0].strip()
                if re.fullmatch(r"\w+", raw or ""):
                    values.append(raw)
            if values:
                found.setdefault(name, values)
    return found


def interp_subs(body, enums):
    """Placeholder resolution, file-local: loop variables over an enum or a string array.

    Extracted so check 6's census resolves `$"RestButtonShape_{board}"` exactly the way check 1's
    key surface does — the two disagreeing about which keys exist is the one way check 6 could
    accuse a key nothing binds."""
    subs = {}
    for _type, var in FOREACH_ENUM.findall(body):
        enum_name = re.search(
            r'foreach\s*\(\s*\w+\s+' + re.escape(var) + r'\s+in\s+(?:System\.)?Enum\.GetValues\s*\(\s*typeof\s*\(\s*(\w+)',
            body)
        if enum_name and enum_name.group(1) in enums:
            subs[var] = enums[enum_name.group(1)]
    arrays = {name: [v.strip().strip('"') for v in vals.split(",") if v.strip()]
              for name, vals in STR_ARRAY.findall(body)}
    for var, arr in ELEM_ASSIGN.findall(body):
        if arr in arrays:
            subs[var] = arrays[arr]
    return subs


def expand_template(template, subs):
    """`RestButtonShape_{board}` -> ['RestButtonShape_Oak', …], or None when unresolvable."""
    holes = HOLE.findall(template)
    if len(holes) != 1:
        return None
    inner = re.sub(r'^\(\s*int\s*\)\s*', '', holes[0][1:-1].strip())
    if inner not in subs:
        return None
    return [template.replace(holes[0], value) for value in subs[inner]]


def key_surface():
    """(literal keys, wildcard patterns per section, per-section counts)."""
    enums = enum_members()
    keys = set()
    wild = set()          # (section, compiled-pattern-source) for unresolvable interpolations

    for path in cs_files():
        with open(path, encoding="utf-8", errors="replace") as fh:
            body = strip_comments(fh.read())

        for section, key in BIND_LITERAL.findall(body):
            if section and key:
                keys.add((section, key))

        # A module whose helper supplies the section: `private const string SectionName = "X";`
        # plus `Xxx = Bind("Key", …)`. Only ComfortSettings does this today; the shape, not the
        # file name, is what is matched, so the next module to do it is covered without an edit.
        m = re.search(r'const\s+string\s+SectionName\s*=\s*"(\w+)"', body)
        if m:
            section = m.group(1)
            for key in re.findall(r'=\s*B[i]nd\s*(?:<[^>()]*>)?\s*\(\s*"(\w+)"\s*,', body):
                keys.add((section, key))

        subs = interp_subs(body, enums)

        for section, template in BIND_INTERP.findall(body):
            expanded = expand_template(template, subs)
            if expanded is not None:
                for key in expanded:
                    keys.add((section, key))
                continue
            # Unresolved: keep it as a wildcard so a curated key that matches is never
            # called missing. A checker that guesses "absent" here would fail the build on
            # correct code, which is how a checker gets switched off.
            wild.add((section, "^" + re.escape(template).replace(r'\{', '{').replace(
                r'\}', '}').replace("{", "").replace("}", "") + "$"))
            wild.add((section, HOLE.sub(".*", re.escape(template).replace(r"\{", "{").replace(r"\}", "}"))))
    return keys, wild


def known(section, key, keys, wild):
    if (section, key) in keys:
        return True
    for wsec, pattern in wild:
        if wsec == section and re.fullmatch(pattern, key):
            return True
    return False


# ============================================================================================
#  2. What the menu advertises
# ============================================================================================

CURATED_FILE = os.path.join(OPTIONS, "VROptionsTab.4.Curated.cs")
TREE_FILES = [os.path.join(OPTIONS, "VROptionsTab.6.BoardTopic.cs"),
              os.path.join(OPTIONS, "VROptionsTab.7.TopicTrees.cs")]

# Three arguments (section, key, caption LOC KEY) or five (…, English, German — a caption written
# on the entry itself when Loc.cs is not the right home for it; see CuratedEntry.Say). Matching
# only the three-argument form would silently drop the five-argument rows from every count here,
# and the row this whole check exists for is one of them.
CUR_ENTRY = re.compile(
    r'new\s*\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*,\s*"([^"]*)"\s*(?:,\s*"[^"]*"\s*,\s*"[^"]*"\s*)?\)')
# `perBoard` DEFAULTS TO TRUE and the tree then renders one row per control board by appending
# "_<board>" to the key (VROptionsTab.6.BoardTopic.BuildBoardRow). A checker that took the key
# literally would call every per-board entry missing — 38 false failures on correct code, which
# is how a checker gets switched off in its first week.
BOARD_REF = re.compile(r'new\s+BoardRef\s*\(\s*"([^"]*)"\s*,\s*"([^"]*)"\s*(?:,\s*perBoard\s*:\s*(true|false))?\s*\)')
LOCKEY = re.compile(r'LocKey\s*=\s*"([^"]*)"')
LABEL_DE = re.compile(r'\bDe\s*=\s*"([^"]*)"')
SECTIONS_MARK = re.compile(r'Sections\s*=\s*new\s+CuratedSection')
ENTRIES_MARK = re.compile(r'Entries\s*=\s*new\s+CuratedEntry')


def curated_tree():
    """[(category, section, config_section, key, caption_key, line)] in declaration order."""
    with open(CURATED_FILE, encoding="utf-8") as fh:
        raw = fh.read()
    body = strip_comments(raw)
    start = body.index("CuratedCategory[] Curated")
    body = body[start:]

    # One token scan: a LocKey belongs to the category if the next structural marker is
    # `Sections =`, to a section if it is `Entries =`. Categories and sections both use
    # target-typed `new()`, so there is no type name to key on.
    events = []
    for m in LOCKEY.finditer(body):
        events.append((m.start(), "lockey", m.group(1)))
    for m in LABEL_DE.finditer(body):
        events.append((m.start(), "de", m.group(1)))
    for m in SECTIONS_MARK.finditer(body):
        events.append((m.start(), "cat", None))
    for m in ENTRIES_MARK.finditer(body):
        events.append((m.start(), "sec", None))
    for m in CUR_ENTRY.finditer(body):
        events.append((m.start(), "entry", (m.group(1), m.group(2), m.group(3))))
    events.sort()

    rows = []
    pending_key = pending_de = None
    category = section = "?"
    cat_key = sec_key = "?"
    for _pos, kind, payload in events:
        if kind == "lockey":
            pending_key, pending_de = payload, None
        elif kind == "de":
            pending_de = payload
        elif kind == "cat":
            cat_key = pending_key or "?"
            category = pending_de or cat_key
            pending_key = pending_de = None
        elif kind == "sec":
            sec_key = pending_key or "?"
            section = pending_de or sec_key
            pending_key = pending_de = None
        elif kind == "entry":
            rows.append((category, cat_key, section, sec_key,
                         payload[0], payload[1], payload[2]))
    return rows


def tree_refs(boards):
    refs = []
    for path in TREE_FILES:
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as fh:
            body = strip_comments(fh.read())
        for section, key, per in BOARD_REF.findall(body):
            if per != "false":
                for board in boards:
                    refs.append((os.path.basename(path), section, f"{key}_{board}"))
            else:
                refs.append((os.path.basename(path), section, key))
    return refs


# ============================================================================================
#  2b. Which offered rows are edited by a dropdown full of C# identifiers (check 6)
#
#  The entry's TYPE is not visible at the bind site — every default in this mod comes from
#  `Defaults.*`, so `.Bind("PeerBoardFade", "Mode", Defaults.PeerBoardFade_Mode, …)` says nothing
#  about the enum. The FIELD DECLARATION does: `ConfigEntry<PeerBoardFadeMode>? FadeMode`. So the
#  census is a two-step join inside each file — declaration name -> enum type, then assignment of
#  that name from a `.Bind(…)` -> (section, key). Both halves are file-local, which is what makes
#  a regex honest here: the mod binds every entry in the same file that declares it.
# ============================================================================================

# `ConfigEntry<Foo> Name`, `ConfigEntry<Foo>? Name`, `ConfigEntry<Foo>[] Name` (the per-board
# families are arrays). `[\w.]+` keeps a qualified type (`Hands.HandStyle`); only its last
# segment is matched against the enum census.
ENTRY_DECL = re.compile(r'ConfigEntry<\s*([\w.]+)\s*>\s*(?:\[\s*\])?\s*\??\s*(\w+)\s*(?:=|;|\))')
# `Name = <anything>.Bind("Sec", "Key"` / `Name[i] = _file.Bind("Sec", $"Key_{board}"`. DOTALL,
# because the multi-line call is the common shape (Plugin.cs binds LogLevel over four lines).
ASSIGN_BIND = re.compile(
    r'\b(\w+)\s*(?:\[[^\]]*\])?\s*=\s*(?:[\w.]+\s*\.\s*)?B[i]nd\s*(?:<[^>()]*>)?\s*\(\s*'
    r'"([^"]*)"\s*,\s*(\$?)"([^"]*)"', re.S)
# The two localizing row tables, both written as chains of
# `string.Equals(item.Section, "X", …) && string.Equals(item.Key, "Y", …)`.
LOCALIZED_ROW = re.compile(r'item\.(Section|Key)\s*,\s*"([^"]*)"')
NOT_OFFERED_ENTRY = re.compile(r'\[\s*"([^"/]+)/([^"]+)"\s*\]\s*=')


def enum_typed_keys():
    """{(section, key): enum name} for every config entry whose stored type is a mod enum."""
    enums = enum_members()
    found = {}
    for path in cs_files():
        with open(path, encoding="utf-8", errors="replace") as fh:
            body = strip_comments(fh.read())
        if "ConfigEntry<" not in body:
            continue

        declared = {}
        for type_name, field in ENTRY_DECL.findall(body):
            leaf = type_name.rsplit(".", 1)[-1]
            if leaf in enums:
                declared[field] = leaf
        if not declared:
            continue

        subs = interp_subs(body, enums)
        for field, section, interp, template in ASSIGN_BIND.findall(body):
            if field not in declared or not section:
                continue
            for key in (expand_template(template, subs) or []) if interp else [template]:
                if key:
                    found[(section, key)] = declared[field]
    return found


def localized_rows():
    """(Section, Key) pairs that VROptionsTab / VariantTiles build with a translated control."""
    pairs = set()
    for path, marker in ((CURATED_FILE, "bool HasSpecialRow"),
                         (os.path.join(OPTIONS, "VariantTiles.cs"), "bool HasVariantTiles")):
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as fh:
            body = strip_comments(fh.read())
        at = body.find(marker)
        if at < 0:
            continue
        table = body[at:body.find(";", at)]
        section = None
        for which, value in LOCALIZED_ROW.findall(table):
            if which == "Section":
                section = value
            elif section is not None:
                pairs.add((section, value))
                section = None
    return pairs


def not_offered():
    """(Section, Key) the catalog withholds from the menu entirely (ConfigCatalog.NotOffered)."""
    path = os.path.join(OPTIONS, "ConfigCatalog.cs")
    with open(path, encoding="utf-8") as fh:
        body = strip_comments(fh.read())
    at = body.find("NotOffered = new(")
    if at < 0:
        return set()
    return {(m.group(1), m.group(2))
            for m in NOT_OFFERED_ENTRY.finditer(body[at:body.find("};", at)])}


OWN_PAGE_ENTRY = re.compile(r'"([^"/]+)/([^"/]+)"')


def own_page_rows():
    """(found, {(Section, Key)}) the catalog's listings skip because a hand-built page draws them.

    VROptionsTab.8.Dependencies.OwnPageRows. Read from the STRIPPED body for the same reason
    every other reader here does: the set is documented in prose that quotes the very key it
    names, and a comment counted as an entry makes the census lie.

    THE FIRST RETURN VALUE IS THE WHOLE POINT OF THE SIGNATURE. The set is legitimately EMPTY
    whenever no diagnostic is standing on the Test-Ausloeser page — it was emptied in ModBuild
    468, after the occlusion A/B answered and the user asked for that page cleared. An empty set
    and a set this reader could no longer FIND (renamed, moved, reformatted) both produced the
    same `set()` before, and check 7 then reported "0 rows handed to a page of their own" for
    both. That is the shape of a check that has quietly stopped looking, and this file has paid
    for one of those. `found` separates them: not-found is a FAILURE, empty is a state the
    success line names out loud.
    """
    path = os.path.join(OPTIONS, "VROptionsTab.8.Dependencies.cs")
    with open(path, encoding="utf-8") as fh:
        body = strip_comments(fh.read())
    at = body.find("OwnPageRows = new(")
    if at < 0:
        return False, set()
    return True, {(m.group(1), m.group(2))
                  for m in OWN_PAGE_ENTRY.finditer(body[at:body.find("};", at)])}


def drawn_outside_the_filter(section, key):
    """Is this key named literally by a page under Options/, i.e. does something draw it?"""
    for name in sorted(os.listdir(OPTIONS)):
        if not name.endswith(".cs") or name == "VROptionsTab.8.Dependencies.cs":
            continue
        with open(os.path.join(OPTIONS, name), encoding="utf-8") as fh:
            body = strip_comments(fh.read())
        if f'"{key}"' in body and f'"{section}"' in body:
            return name
    return None


def loc_ids():
    ids = set()
    for name in os.listdir(LOC_DIR):
        if not name.endswith(".cs"):
            continue
        with open(os.path.join(LOC_DIR, name), encoding="utf-8") as fh:
            body = strip_comments(fh.read())
        ids.update(re.findall(r'\[\s*"([^"]+)"\s*\]\s*=', body))
    return ids


# `["id"] = Pair("English", "Deutsch")` — the ONE shape both string tables use (Loc.cs:192,
# Loc.ConfigNames.cs). Multi-line and concatenated bodies (the long help prose) do not match and
# are simply not compared; every caption in both tables is a single literal pair.
LOC_PAIR = re.compile(r'\[\s*"([^"]+)"\s*\]\s*=\s*Pair\(\s*'
                      r'"((?:[^"\\]|\\.)*)"\s*,\s*"((?:[^"\\]|\\.)*)"\s*\)', re.S)


def loc_pairs(filename):
    """{id: (english, german)} for one Loc table file."""
    path = os.path.join(LOC_DIR, filename)
    with open(path, encoding="utf-8") as fh:
        body = strip_comments(fh.read())
    return {m.group(1): (m.group(2), m.group(3)) for m in LOC_PAIR.finditer(body)}


# Loc.FamilyKey's three families, mirrored (Loc.ConfigDescriptions.cs:66-113). Kept as literals
# rather than parsed, because the C# arrays mirror HandStyle / ControlBoard / CardsConfig and a
# parse of the parse would not be more truthful — check 1 already fails if a variant key vanishes.
STYLE_PREFIXES = ("Glove", "Plate", "Arcane")
FAMILY_SUFFIXES = ("_Oak", "_Steel", "_Bronze", "_Items", "_Discard", "_Burnt")


def family_key(key):
    """The wildcard key a per-style / per-board / per-pile entry shares with its siblings, or
    None. Same order and same 'exact key first' contract as Loc.FamilyKey."""
    for prefix in STYLE_PREFIXES:
        if len(key) > len(prefix) and key.startswith(prefix):
            return "*" + key[len(prefix):]
    for suffix in FAMILY_SUFFIXES:
        if len(key) > len(suffix) and key.endswith(suffix):
            return key[:len(key) - len(suffix) + 1] + "*"
    return None


# ============================================================================================
#  3. The deliberate exceptions, each with the ruling that makes it one
# ============================================================================================

# A (Section, Key) curated in TWO tabs on purpose. "A curated row is an extra door, never a
# wall" — but a second door has to be argued, or it is a copy-paste nobody noticed.
DUPLICATE_ALLOWED = {
    # User ruling, quoted in VROptionsTab.4.Curated.cs at Komfort ▸ Sichtbarkeit: "the wall
    # see-through is a comfort-relevant, user-facing feature and must be findable HERE, not
    # only under Grafik". The second door moved with the world block to Umgebung; both rows
    # write the same ConfigEntry, so the cost is one line in the curated file.
    ("Compat", "WallFade"): "user ruling — findable under Komfort AND with the world block",
    ("WallFade", "StackedShellFade"): "ruling 18 — sits directly beside WallFade in both doors",
    ("WallFade", "WalkInStandDown"): "ruling 18 — sits directly beside WallFade in both doors",
}

# A curated row whose everyday caption differs from its Erweitert name ON PURPOSE. The rule it
# relaxes is Loc.ConfigNames.cs:161 — "one row, one name, both doors". A shorter everyday caption
# under a heading that already supplies the context is a legitimate reason; "nobody noticed" is not.
# Keyed (Section, Key); the value is the reason, and it is printed by --report.
NAME_DIFFERS_ON_PURPOSE = {
    # (empty — the 2026-09 audit's twenty disagreements were all resolved to one name. Two rows
    # that LOOKED like entries here instead got their own caption key, because their old one also
    # named a heading: [Hands] HandStyle -> vr_o_handstyle, [MixedReality] Enabled -> vr_o_mrenabled.)
}

# A curated family whose sibling is NOT curated. The rule this relaxes is check 4: "a family
# that shares a name should share a heading" (VROptionsTab.4.Curated.cs says exactly that,
# twice).
#
# THIS IS A BACKLOG, NOT AN APPROVAL, and the distinction is the whole design. Applied cold at
# ModBuild 340 the rule fires 89 times, because the everyday view has always been a hand-picked
# list over a ~600-key tuning surface and most of those 89 siblings genuinely belong one level
# deeper. Making a rule that fires 89 times a hard gate means writing 89 justifications in one
# sitting — 89 pieces of prose written at the moment of LEAST knowledge about each key, which
# this project has learned to distrust ("a filed debt is prose written at the moment of least
# knowledge and then trusted like a measurement"). So the set below is frozen at the state the
# rule was written against, exactly the way check-instrument-writes.py freezes its baseline, and
# the gate is on the DELTA: a key added to a curated family tomorrow without a curated row fails
# the build. The list may only shrink.
#
# TO FIX ONE: curate the row, then delete its line here. Regenerate the whole set after a
# deliberate restructure with `check-options-coverage.py --bless`, and say so in the commit.
#
# "THE LIST MAY ONLY SHRINK" HAS ONE SANCTIONED EXCEPTION AND 2026-09-05 IS IT: a DEMOTION ordered
# by the user grows this set on purpose, because a row leaving the curated page while a sibling
# stays is precisely a new orphan. Four entries were added that day (the three [Cards] Spawn*
# arrival dials and [Comfort] LaserCarryReelSpeed), each carrying the ruling that moved it and the
# Erweitert heading it lands on. What the rule still forbids — and still catches — is a NEW key
# joining a curated family with no row and no line here. Added by hand rather than by --bless, so
# that every addition had to be argued instead of blessed in a batch.
KNOWN_ORPHANS = {
    ("Board", "TouchRange"),
    # 2026-09-05 - THREE DEMOTIONS THE USER RULED ON DIRECTLY, each a sibling of a family
    # whose everyday member stays curated. Asked whether "offsets etc. gehoeren da nicht hin"
    # also covers these, he answered: "Ja auch die Hoehe der Lebensbalken und die Grenzen sind
    # Experteneinstellungen und gehoeren in Erweitert."
    #   * [Comfort] ScaleMin/ScaleMax clamp the two-hand pinch. The GESTURE switch
    #     ([Comfort] ScaleEnabled, "Welt skalieren") is the curated family member and stays;
    #     these two are raw multipliers a player discovers by pinching, not by typing.
    #   * [WorldUI] BarHeightOffset is one metre offset beside the curated bar family
    #     (BarFixedSize, BarsOccluded), which are on/off rows a player can judge instantly.
    # None of the three is unreachable: all sit on a named Erweitert heading, and
    # BarHeightOffset gained the Loc.ConfigNames entry it never had, so it lists under its
    # German name rather than a raw key.
    ("Comfort", "ScaleMax"),
    ("Comfort", "ScaleMin"),
    ("WorldUI", "BarHeightOffset"),
    ("Cards", "BoardMaxWidthMeters"),
    ("Cards", "BoardMinWidthMeters"),
    ("Cards", "BoardPitchMaxDegrees"),
    ("Cards", "BoardPitchMinDegrees"),
    ("Cards", "BoardPosOffset_Bronze"),
    ("Cards", "BoardPosOffset_Oak"),
    ("Cards", "BoardPosOffset_Steel"),
    # Grab-written state, not a dial — the same reason TrayYaw/TrayPitch/TrayForward/TrayDown/
    # TrayRight are one level deeper, and BoardApparentWidth_{board} is the size half of exactly
    # that set: the two-hand resize writes it (PlayTray.RecordAuthoredApparentWidth), nothing else
    # may, and the row a player looks for when he wants to CHANGE the size is
    # [Cards] SpawnBoardWidthDegrees, which is curated beside the other two arrival rows.
    ("Cards", "BoardApparentWidth_Bronze"),
    ("Cards", "BoardApparentWidth_Oak"),
    ("Cards", "BoardApparentWidth_Steel"),
    ("Cards", "BoardScaleDefault04Applied"),
    ("Cards", "BoardScale_Bronze"),
    ("Cards", "BoardScale_Oak"),
    ("Cards", "BoardScale_Steel"),
    ("Cards", "BoardTilt_Bronze"),
    ("Cards", "BoardTilt_Oak"),
    ("Cards", "BoardTilt_Steel"),
    ("Cards", "BoardYaw_Bronze"),
    ("Cards", "BoardYaw_Oak"),
    ("Cards", "BoardYaw_Steel"),
    ("Cards", "CardDust"),
    ("Cards", "CardGrabSound"),
    ("Cards", "CardLerpSpeed"),
    ("Cards", "CardPlaceSound"),
    ("Cards", "CardTakeBackSound"),
    ("Cards", "InHandGraspSeconds"),
    ("Cards", "InHandPinchOffset"),
    ("Cards", "InHandPitch"),
    ("Cards", "RevealEnterDegrees"),
    ("Cards", "RevealExitDegrees"),
    ("Cards", "RevealIgnoreWhenGrabbing"),
    ("Cards", "SpawnDownMeters"),
    ("Cards", "SpawnForwardMeters"),
    ("Cards", "SpawnSideMeters"),
    # ADDED 2026-09-05 — the three numeric ARRIVAL dials, demoted off the curated Brett & Karten
    # page by user ruling (b): "In den Nicht-Erweitert Kategorien sind wieder viel zu viele
    # detaillierte Einstellungen gelandet. Sowas wie Offsets etc. gehört da nicht hin - Denke immer
    # daran das die einfachen Spieler die Zielgruppe sind." They join the three Spawn*Meters
    # offsets directly above, which were never curated for exactly the same reason: all six are
    # read once, at the moment the board arrives, so turning one while looking at the board does
    # nothing. The curated 'Spawn' family is now [Cards] SpawnLeftOfHead alone — the decision, not
    # its measurements. NOT a curation gap: all three are listed by hand on Erweitert ▸ Karten &
    # Fächer ▸ Steuerbrett (VROptionsTab.7.TopicTrees.cs), beside these three.
    ("Cards", "SpawnBoardWidthDegrees"),
    ("Cards", "SpawnMaxBearingDegrees"),
    ("Cards", "SpawnMaxReachMeters"),
    ("Cards", "TrayDown"),
    ("Cards", "TrayForward"),
    ("Cards", "TrayPitch"),
    ("Cards", "TrayRight"),
    ("Cards", "TrayTilt"),
    ("Cards", "TrayYaw"),
    # ADDED 2026-09-05 by the same ruling — the reel's SPEED constant in m/s. The switch
    # ([Comfort] LaserCarryReel) stays curated on Bild ▸ Fenster & Tafeln; its calibration does
    # not, which is the pattern that section already applies to [WorldUI] PanelSupersampleFactor
    # and PanelMipLodOffset further down this list. [Comfort] is under
    # ConfigCatalog.SectionSplitThreshold, so it lands in the section's single group on
    # Erweitert ▸ Bewegung, directly beside the switch.
    ("Comfort", "LaserCarryReelSpeed"),
    ("FigureGrab", "GrabProps"),
    ("Hands", "ArcaneForwardOffset"),
    ("Hands", "ArcaneGripPitchDegrees"),
    ("Hands", "ArcaneGripRollDegrees"),
    ("Hands", "ArcaneGripYawDegrees"),
    ("Hands", "ArcaneLateralOffset"),
    ("Hands", "ArcaneSpreadOffset"),
    ("Hands", "ArcaneVerticalOffset"),
    ("Hands", "CurlMiddle"),
    ("Hands", "CurlProximal"),
    ("Hands", "CurlTip"),
    ("Hands", "GloveForwardOffset"),
    ("Hands", "GloveGripPitchDegrees"),
    ("Hands", "GloveGripRollDegrees"),
    ("Hands", "GloveGripYawDegrees"),
    ("Hands", "GloveLateralOffset"),
    ("Hands", "GlovePinkyCounterAbduction"),
    ("Hands", "GloveSpreadOffset"),
    ("Hands", "GloveVerticalOffset"),
    ("Hands", "HandColor"),
    ("Hands", "LaserFingerOffsetMeters"),
    ("Hands", "PlateForwardOffset"),
    ("Hands", "PlateGripPitchDegrees"),
    ("Hands", "PlateGripRollDegrees"),
    ("Hands", "PlateGripYawDegrees"),
    ("Hands", "PlateLateralOffset"),
    ("Hands", "PlateSpreadOffset"),
    ("Hands", "PlateVerticalOffset"),
    ("RenderQuality", "ForceFullTextureResolution"),
    ("RenderQuality", "ForceTextureStreamingOff"),
    ("WallFade", "WalkInCrestReleaseFraction"),
    ("WallFade", "WalkInEnterDwellSeconds"),
    ("WallFade", "WalkInExitDwellSeconds"),
    ("WallFade", "WalkInHeadBelowCrestFraction"),
    ("WallFade", "WalkInMinCrestMetres"),
    ("WallFade", "WalkInSuspendSampling"),
    # ("WorldUI", "BarHeightOffset") was here and is FIXED — it is a curated row on
    # Brett & Karten ▸ Lebensbalken and a listed row on Erweitert ▸ Menüs & Tafeln ▸
    # Lebensbalken. This is what shrinking the backlog looks like; leave the tombstone.
    ("WorldUI", "CombatLogFollowSeat"),
    ("WorldUI", "CombatLogForward"),
    ("WorldUI", "CombatLogRight"),
    ("WorldUI", "CombatLogScale"),
    ("WorldUI", "CombatLogUp"),
    # ("WorldUI", "CombatLogUserClosed") was here and the KEY IS GONE (2026-09-05) — it was a
    # persisted latch, not a setting, and it held the combat log shut for good once the only
    # writer that could clear it was deleted. This is what shrinking the backlog looks like.
    ("WorldUI", "HexHintDistance"),
    ("WorldUI", "HexHintDrop"),
    ("WorldUI", "HexHintSide"),
    ("WorldUI", "MapRoomWindowBarHeightMeters"),
    ("WorldUI", "MapWindOpacity"),
    ("WorldUI", "PanelMipBake"),
    ("WorldUI", "PanelMipLodOffset"),
    ("WorldUI", "PanelSupersampleFactor"),
    ("WorldUI", "PokePressDepthMm"),
    ("WorldUI", "ScreenDepthStrength"),
    ("WorldUI", "ScreenParallaxScale"),
    ("WorldUI", "WindowMaterialise"),
    ("WorldUI", "WindowMaterialiseAppearSeconds"),
    ("WorldUI", "WindowMaterialiseIntensity"),
    ("WorldUI", "WindowMaterialiseVanishSeconds"),
}

# An OFFERED enum-typed entry whose dropdown still shows the raw C# member names. The rule this
# relaxes is check 6 above.
#
# FROZEN AT ModBuild 445, THE SAME WAY KNOWN_ORPHANS IS FROZEN, and for the same reason: applied
# cold the rule fires nine times, and writing nine translations in one sitting means choosing nine
# player-facing vocabularies at the moment of least knowledge about each. The gate is on the DELTA
# — a NEW enum-typed setting must arrive with a localizing row or with a line here. THE LIST MAY
# ONLY SHRINK.
#
# TO FIX ONE: add a `HasSpecialRow` branch with a `BuildPresetRow` over Loc ids (the pattern is
# three branches deep in VROptionsTab.4.Curated.cs now), then delete its line here.
ENUM_LABELS_NOT_LOCALIZED = {
    # Round / Square, six rows (two groups × three boards) under Erweitert ▸ Brett. The two Loc ids
    # ARE ALREADY WRITTEN and unused — Loc.cs carries ["round"] = ("Round","Rund") and ["square"] =
    # ("Square","Eckig") with no consumer in the mod. Six rows, one branch, two existing ids.
    ("Cards", "GenericButtonShape_Oak"): "Round/Square — Loc ids 'round'/'square' exist unused",
    ("Cards", "GenericButtonShape_Steel"): "Round/Square — Loc ids 'round'/'square' exist unused",
    ("Cards", "GenericButtonShape_Bronze"): "Round/Square — Loc ids 'round'/'square' exist unused",
    ("Cards", "RestButtonShape_Oak"): "Round/Square — Loc ids 'round'/'square' exist unused",
    ("Cards", "RestButtonShape_Steel"): "Round/Square — Loc ids 'round'/'square' exist unused",
    ("Cards", "RestButtonShape_Bronze"): "Round/Square — Loc ids 'round'/'square' exist unused",
    # Off / ActionPhaseOnly / Always on a CURATED everyday row ("Mitspieler-Bretter"), and the worst
    # of the remaining entries on its own terms: "ActionPhaseOnly" is not even English prose, it is a member name
    # with the middle word capitalized, offered to a player as one of three things to pick.
    ("Net", "RemoteBoards"): "Off/ActionPhaseOnly/Always — a curated everyday row, the same defect "
                             "class the user reported for [PeerBoardFade] Mode",
    # Off / Error / Warning / Info / Debug — the one entry in this set with an argument for staying
    # as it is: it names LOG TIERS that appear verbatim in Player.log, and a player who reads that
    # file to report a bug is better served by the row and the file agreeing. Not an approval, a
    # reason to decide it deliberately rather than by default.
    ("General", "LogLevel"): "log tiers appear verbatim in Player.log; translating the row would "
                             "disagree with the file it configures — decide, do not default",
}

# THE ADJACENT CLASS, measured and written down rather than left unmeasured (see check 6's
# docstring for why it is not gated). These rows are Choice rows whose options are STRINGS the
# player's .cfg must literally contain, so the label and the stored value are one object:
#   [Core] RuntimePriority   auto / default / vdxr / steamvr / oculus
#   [Hands] PrimaryHand      Right / Left
#   [WorldUI] ModalStyle     window / screen
#   [Cards] RevealMode       tilt / always
#   [Cards] Fan*/Card* sounds  five game audio METHOD NAMES (deliberately raw — see CuratedChoices)
# Giving any of them a translated label means adding an index map between what is shown and what is
# stored, which is a design change per entry rather than a missing string.


# ============================================================================================
#  4. The checks
# ============================================================================================

def leading_word(key: str) -> str:
    """"BarHeightOffset" -> "Bar"; "FanArcSweepDegrees" -> "Fan". The catalog's own rule
    (ConfigCatalog.GroupAll), reused so the family this checker sees is the family the menu
    would build."""
    m = re.match(r'[A-Z][a-z0-9]*', key)
    return m.group(0) if m else key


def main():
    report = "--report" in sys.argv
    keys, wild = key_surface()
    boards = enum_members().get("ControlBoard", [])
    curated = curated_tree()
    refs = tree_refs(boards)
    loc = loc_ids()

    failures = []

    # ---- 1. advertised but absent -----------------------------------------------------------
    for cat, _ck, sec, _sk, section, key, _cap in curated:
        if not known(section, key, keys, wild):
            failures.append(f"CURATED KEY DOES NOT EXIST: [{section}] {key} "
                            f"(curated under {cat} / {sec}) — the row is silently skipped at "
                            f"runtime (VROptionsTab.3.Content.BuildCurated → Lookup returns null).")
    for filename, section, key in refs:
        if not known(section, key, keys, wild):
            failures.append(f"TOPIC-TREE KEY DOES NOT EXIST: [{section}] {key} ({filename}) — "
                            f"the tree names a key nothing binds; it draws nothing.")

    # ---- 7. a row handed to a page that does not draw it -------------------------------------
    own_page_found, own_page = own_page_rows()
    if not own_page_found:
        failures.append(
            "OWN-PAGE SET NOT FOUND: VROptionsTab.8.Dependencies.cs no longer contains "
            "'OwnPageRows = new(', so check 7 is reading nothing and would pass whatever the "
            "menu does. Point this reader at the set's new name, or delete the check with the "
            "hand-off mechanism it guards — do not leave it looking at an empty file.")
    for section, key in sorted(own_page):
        if not known(section, key, keys, wild):
            failures.append(f"OWN-PAGE KEY DOES NOT EXIST: [{section}] {key} "
                            f"(VROptionsTab.8.Dependencies.OwnPageRows) — the filter hides a key "
                            f"nothing binds; delete the entry.")
            continue
        if drawn_outside_the_filter(section, key) is None:
            failures.append(f"OWN-PAGE KEY IS DRAWN NOWHERE: [{section}] {key} — OwnPageRows keeps "
                            f"it out of every catalog listing, and no other file under "
                            f"WorldUI/Options names it, so it is reachable from NEITHER door. "
                            f"Either the page that owned it stopped drawing it, or the entry "
                            f"should go.")

    # ---- 2. two doors -----------------------------------------------------------------------
    seen = {}
    for cat, _ck, sec, _sk, section, key, _cap in curated:
        seen.setdefault((section, key), []).append(f"{cat} / {sec}")
    for pair, where in sorted(seen.items()):
        if len(where) > 1 and pair not in DUPLICATE_ALLOWED:
            failures.append(f"CURATED TWICE WITHOUT A REASON: [{pair[0]}] {pair[1]} in "
                            f"{' + '.join(where)} — add it to DUPLICATE_ALLOWED with the ruling, "
                            f"or delete one of the rows.")

    # ---- 3. a caption that names nothing -----------------------------------------------------
    for cat, _ck, sec, _sk, section, key, cap in curated:
        if cap and cap not in loc:
            failures.append(f"CAPTION KEY NOT IN Loc: '{cap}' on [{section}] {key} "
                            f"({cat} / {sec}) — Loc.Mod returns the id, so the row is "
                            f"captioned with a programmer's string.")

    # ---- 5. one row, two names ---------------------------------------------------------------
    everyday = loc_pairs("Loc.cs")
    erweitert = loc_pairs("Loc.ConfigNames.cs")
    two_names = 0
    for cat, _ck, sec, _sk, section, key, cap in curated:
        if not cap:
            continue                       # the documented fallback: the row USES ConfigNames
        mine = everyday.get(cap)
        if mine is None:
            continue                       # check 3 owns "the caption resolves"
        theirs = erweitert.get(section + "/" + key)
        if theirs is None:
            family = family_key(key)
            theirs = erweitert.get(section + "/" + family) if family else None
        if theirs is None:
            continue                       # Erweitert falls back to the spaced-out key; not a name clash
        two_names += 1
        if mine == theirs or (section, key) in NAME_DIFFERS_ON_PURPOSE:
            continue
        failures.append(
            f"ONE ROW, TWO NAMES: [{section}] {key} ({cat} / {sec}) is captioned "
            f"EN '{mine[0]}' / DE '{mine[1]}' on the everyday page (Loc.cs '{cap}') and "
            f"EN '{theirs[0]}' / DE '{theirs[1]}' under Erweitert "
            f"(Loc.ConfigNames.cs '{section}/{key}'). Loc.ConfigNames.cs:161: one row, one "
            f"name, both doors. Align the two, or name the pair in NAME_DIFFERS_ON_PURPOSE "
            f"with the reason the everyday caption is deliberately different.")

    # ---- 6. a row whose values are C# identifiers --------------------------------------------
    enum_typed = enum_typed_keys()
    localized = localized_rows()
    withheld = not_offered()
    raw_enum_rows = []
    for (section, key), enum_name in sorted(enum_typed.items()):
        if (section, key) in localized or (section, key) in withheld:
            continue
        raw_enum_rows.append((section, key, enum_name))
        if (section, key) in ENUM_LABELS_NOT_LOCALIZED:
            continue
        members = " / ".join(enum_members().get(enum_name, [])) or "its members"
        failures.append(
            f"RAW ENUM NAMES IN A DROPDOWN: [{section}] {key} is a {enum_name}, so "
            f"ConfigCatalog.Classify makes it a Choice and BuildChoiceRow labels its options "
            f"'{members}' — the C# member names, identical in English and German. Give it a "
            f"HasSpecialRow branch with a BuildPresetRow over Loc ids (VROptionsTab.4.Curated.cs "
            f"has three), or name it in ENUM_LABELS_NOT_LOCALIZED with the reason it may stay a "
            f"programmer's string.")
    stale = sorted(set(ENUM_LABELS_NOT_LOCALIZED) - {(s, k) for s, k, _e in raw_enum_rows})
    for section, key in stale:
        failures.append(
            f"ENUM_LABELS_NOT_LOCALIZED IS STALE: [{section}] {key} is listed as showing raw "
            f"member names, but it now has a localizing row (or is no longer offered). Delete the "
            f"line — the list may only shrink, and a stale entry hides the next real one.")

    # ---- 4. a split family -------------------------------------------------------------------
    curated_pairs = {(s, k) for _c, _ck, _s2, _sk, s, k, _cap in curated}
    families = {}
    for section, key in curated_pairs:
        families.setdefault((section, leading_word(key)), set()).add(key)
    orphans = []
    for (section, word), members in sorted(families.items()):
        siblings = {k for (s, k) in keys if s == section and leading_word(k) == word}
        for sibling in sorted(siblings - members):
            if (section, sibling) in KNOWN_ORPHANS:
                continue
            orphans.append((section, word, sibling))
    if "--bless" in sys.argv:
        lines = sorted({(s, k) for s, _w, k in orphans} | set(KNOWN_ORPHANS))
        print("KNOWN_ORPHANS = {")
        for sec, key in lines:
            print(f'    ("{sec}", "{key}"),')
        print("}")
        return 0

    for section, word, sibling in orphans:
        failures.append(f"SPLIT FAMILY: [{section}] {sibling} is a sibling of the curated "
                        f"'{word}' family and is not curated — the ModBuild 340 defect exactly. "
                        f"Curate it beside its family, or name it in KNOWN_ORPHANS with the "
                        f"reason it stays one level deeper.")

    # ---- report ------------------------------------------------------------------------------
    if report:
        cats = []
        for cat, ck, sec, sk, section, key, cap in curated:
            if not cats or cats[-1][0] != cat:
                cats.append([cat, ck, [], 0])
            if not cats[-1][2] or cats[-1][2][-1][0] != sec:
                cats[-1][2].append([sec, sk, 0])
            cats[-1][2][-1][2] += 1
            cats[-1][3] += 1
        print(f"config keys the mod binds (literal + helper + expanded per-variant): {len(keys)}")
        print(f"unresolved interpolation patterns (wildcards): {len(wild) // 2}")
        print(f"curated rows: {len(curated)}   distinct keys: {len(curated_pairs)}   "
              f"tabs: {len(cats)}   sections: {sum(len(c[2]) for c in cats)}")
        print(f"rows named on BOTH doors: {two_names}   "
              f"deliberately different: {len(NAME_DIFFERS_ON_PURPOSE)}")
        print(f"deliberate second doors: {len(DUPLICATE_ALLOWED)}   "
              f"known family orphans (frozen backlog): {len(KNOWN_ORPHANS)}")
        print(f"enum-typed rows: {len(enum_typed)}   localized by a hand-built row: "
              f"{len(enum_typed) - len(raw_enum_rows) - len(withheld & set(enum_typed))}   "
              f"still raw member names (frozen backlog): {len(raw_enum_rows)}")
        for section, key, enum_name in raw_enum_rows:
            print(f"      [{section}] {key} ({enum_name}) — "
                  f"{ENUM_LABELS_NOT_LOCALIZED.get((section, key), '?')}")
        print()
        for cat, ck, sections, total in cats:
            print(f"  {cat}  ({ck}) — {total} row(s)")
            for sec, sk, n in sections:
                print(f"      {sec}  ({sk}) — {n}")
        print()
        raw_only = len({(s, k) for (s, k) in keys} - curated_pairs)
        print(f"reachable ONLY through Erweitert (the catalog index): {raw_only}")

    if failures:
        print()
        for line in failures:
            print("error: " + line)
        print(f"\n{len(failures)} option-menu coverage failure(s).")
        return 1
    # An EMPTY set is a legitimate state and a BROKEN reader is not, and the two used to print the
    # same "0 row(s)". Say which one this is.
    own_page_note = (f"{len(own_page)} row(s) handed to a page of their own and drawn there"
                     if own_page
                     else "the OwnPageRows set was FOUND and is EMPTY (no key is handed to a page "
                          "of its own today, so check 7 held nothing — it did not stop looking)")
    print("options coverage: every curated and topic-tree key exists, every caption resolves, "
          f"{own_page_note}, "
          f"{two_names} row(s) named on both doors agree, "
          f"{len(DUPLICATE_ALLOWED)} deliberate second door(s), "
          f"{len(KNOWN_ORPHANS)} known family orphan(s) in the frozen backlog, no NEW split "
          f"family, {len(raw_enum_rows)} enum row(s) still labelled with raw member names "
          f"(frozen) and no new one.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
