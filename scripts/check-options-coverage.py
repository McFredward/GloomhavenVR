#!/usr/bin/env python3
"""Fail the build when a setting exists but cannot be found in the VR options menu.

WHY THIS EXISTS. ModBuild 339 added `[WorldUI] BarHeightOffset` because the user asked for it
by hand ("die Lebensbalken sitzen zu hoch"). It shipped bound, defaulted, documented — and
uncurated, so it landed at the bottom of Erweitert ▸ Menüs & Tafeln in the "Allgemein" grab-bag
that the topic tree above it exists to empty. He tested ModBuild 340 and reported, verbatim:
"Weiterhin finde ich den offset für die healthbar nicht". The feature was delivered and
unreachable, and nothing in the build said so.

Nothing here judges taste. It checks four things a machine can decide, each of which has already
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

WHAT IS NOT CHECKED, AND WHY. "Every key is reachable" is not checked because it is TRUE BY
CONSTRUCTION and checking it would be checking the wrong thing: Erweitert is the catalog's own
index and the catalog is a reflection walk over the live BepInEx registry
(`ConfigCatalog.Rebuild`), so a bound entry appears there whether anyone remembers it or not.
The only keys that leave the menu are the ones ConfigCatalog deliberately withholds
(`NotOffered`, `RetiredMarkers`), each with its reason written beside it. What is NOT true by
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

        # Placeholder resolution, file-local: loop variables over an enum or a string array.
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

        for section, template in BIND_INTERP.findall(body):
            holes = HOLE.findall(template)
            if len(holes) == 1:
                inner = holes[0][1:-1].strip()
                inner = re.sub(r'^\(\s*int\s*\)\s*', '', inner)
                if inner in subs:
                    for value in subs[inner]:
                        keys.add((section, template.replace(holes[0], value)))
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


def loc_ids():
    ids = set()
    for name in os.listdir(LOC_DIR):
        if not name.endswith(".cs"):
            continue
        with open(os.path.join(LOC_DIR, name), encoding="utf-8") as fh:
            body = strip_comments(fh.read())
        ids.update(re.findall(r'\[\s*"([^"]+)"\s*\]\s*=', body))
    return ids


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
KNOWN_ORPHANS = {
    ("Board", "TouchRange"),
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
    ("Cards", "TrayDown"),
    ("Cards", "TrayForward"),
    ("Cards", "TrayPitch"),
    ("Cards", "TrayRight"),
    ("Cards", "TrayTilt"),
    ("Cards", "TrayYaw"),
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
        print(f"deliberate second doors: {len(DUPLICATE_ALLOWED)}   "
              f"known family orphans (frozen backlog): {len(KNOWN_ORPHANS)}")
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
    print("options coverage: every curated and topic-tree key exists, every caption resolves, "
          f"{len(DUPLICATE_ALLOWED)} deliberate second door(s), "
          f"{len(KNOWN_ORPHANS)} known family orphan(s) in the frozen backlog, no NEW split family.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
