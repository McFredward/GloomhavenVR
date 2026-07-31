#!/usr/bin/env python3
"""
Re-base the SHIPPED defaults onto a tuned setup — mechanically, from the cfg files.

WHY THIS EXISTS
---------------
The mod ships ~550 config defaults. Tuning happens in VR, lands in BepInEx/config/*.cfg, and
then has to travel back into the source so a FRESH INSTALL starts where the tuning ended. That
used to be a hand-sweep of 18 config classes; twice it silently missed entries.

Since the Defaults refactor every default lives on one annotated line under
src/GloomhavenVR/Defaults/, so the whole pass is now a mechanical join between two tables:

    .planning/debug/default/*.cfg     [Section] Key = value        (what the user tuned)
    src/GloomhavenVR/Defaults/*.cs    ... = <literal>;  // => [Section] Key

USAGE
    python3 scripts/rebase-defaults.py check    # report mismatches, exit 1 if any
    python3 scripts/rebase-defaults.py apply    # rewrite the Defaults lines, print a table
    python3 scripts/rebase-defaults.py check --cfg-dir SOMEWHERE

WHAT IT REFUSES TO DO
---------------------
It never guesses. A cfg entry it cannot map to exactly one annotated Defaults line is REPORTED,
not silently skipped and not approximated:

  * seeded entries — a handful of per-style keys take their bind default from ANOTHER entry's
    live value (FigureGrab's legacy globals, and the Rot* trio seeded from their predecessors).
    Those have no literal of their own; writing one would change the seeding, not the default.
    They are listed as NOTICE lines naming the entry that actually carries the value.
  * entries present in the cfg but absent from the code (retired keys BepInEx still round-trips)
    and vice versa — reported as UNMAPPED / not-in-cfg, never invented.

Entries that exist in the code but not in the cfg are left exactly as they are: a cfg is a
snapshot of one machine, and absence there is not a statement about the default.
"""
import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULTS_DIR = ROOT / "src" / "GloomhavenVR" / "Defaults"
SRC = ROOT / "src" / "GloomhavenVR"
CFG_DIR = ROOT / ".planning" / "debug" / "default"

# one Defaults entry: `    internal const float TrayForward = 0.77584f;   // => [Cards] TrayForward`
# with an optional trailing marker: `// => [Cards] FanArcDegrees  (legacy: ...)`
ENTRY_RE = re.compile(
    r"^(?P<head>\s*internal\s+(?:const|static\s+readonly)\s+(?P<type>[A-Za-z0-9_.]+)\s+"
    r"(?P<name>[A-Za-z0-9_]+)\s*=\s*)(?P<init>.+?)(?P<tail>;\s*//\s*=>\s*\[(?P<section>[^\]]+)\]\s+"
    r"(?P<key>\S+)(?:\s+\((?P<marker>legacy|pinned):\s*(?P<why>[^)]*)\))?\s*)$")

# How close two floats have to be to count as the SAME shipped default.
#
# The cfgs are float32 round-trips of values tuned by holding a stick: `0.77584f` in the source
# comes back as `0.7758396`, `-0.053f` as `-0.0530001`, `0f` as `-1.11759e-10`. Those are not
# tunings, they are the storage. Treating them as differences would make every single run
# rewrite half the table and bury the handful of real changes — so anything inside ~10 float32
# ULPs is equal, and a real tuning (which moves a value by a menu step, never by an ULP) is not.
FLOAT_TOL_REL = 1e-5
FLOAT_TOL_FLOOR = 1e-2      # magnitude below which the tolerance stops shrinking

NUM = r"-?(?:\d[\d_]*)(?:\.\d+)?(?:[eE][-+]?\d+)?"
VEC = re.compile(r"^new\s+(Vector2|Vector3|Color)\((?P<args>[^)]*)\)$")


# --------------------------------------------------------------------------- the code side
class Entry:
    def __init__(self, path, lineno, m):
        self.path, self.lineno, self.m = path, lineno, m
        self.name = m.group("name")
        self.type = m.group("type")
        self.init = m.group("init").strip()
        self.section = m.group("section")
        self.key = m.group("key")
        self.marker = m.group("marker")     # None | "legacy" | "pinned"
        self.why = m.group("why")

    @property
    def ident(self):
        return (self.section, self.key)

    def value(self):
        """The shipped default as a comparable python value, or None if not a plain literal."""
        return parse_cs(self.init, self.type)

    def rendered(self, value):
        """This line with `value` (a python value) written into it, annotation kept in column."""
        head, tail = self.m.group("head"), self.m.group("tail")
        new = render_cs(value, self.type, self.init)
        pad = len(head) + len(self.init) - (len(head) + len(new))    # how much shorter it got
        m = re.match(r"^;(?P<gap> *)(?P<rest>.*)$", tail, re.S)
        if m:
            gap = " " * max(2, len(m.group("gap")) + pad)
            tail = ";" + gap + m.group("rest")
        return head + new + tail


def load_entries():
    out, dupes = {}, []
    for path in sorted(DEFAULTS_DIR.glob("Defaults.*.cs")):
        for i, line in enumerate(path.read_text(encoding="utf-8").splitlines()):
            m = ENTRY_RE.match(line)
            if not m:
                continue
            e = Entry(path, i, m)
            if e.ident in out:
                dupes.append(e.ident)
            out[e.ident] = e
    return out, dupes


def parse_cs(init, typ):
    """C# literal -> python value. None when the initialiser is not a literal we own."""
    t = init.strip()
    if t in ("true", "false"):
        return t == "true"
    if re.fullmatch(NUM + "f", t):
        return float(t[:-1].replace("_", ""))
    if re.fullmatch(r"-?\d[\d_]*", t):
        return int(t.replace("_", ""))
    if re.fullmatch(r'"(?:[^"\\]|\\.)*"', t):
        return t[1:-1]
    m = re.fullmatch(r"nameof\(([^)]*)\)", t)
    if m:
        return m.group(1).rsplit(".", 1)[-1]
    m = VEC.match(t)
    if m:
        parts = [p.strip() for p in m.group("args").split(",")]
        return tuple(float(p[:-1] if p.endswith("f") else p) for p in parts)
    if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.]*\.[A-Za-z_][A-Za-z0-9_]*", t):
        return t.rsplit(".", 1)[-1]        # an enum member: compare by member NAME
    return None


def fnum(x):
    """Render a float the way a human would have typed it.

    A cfg value is a float32 round-trip, so it arrives as `0.7758396` or
    `-0.44999998807907104` where the source said `0.77584f` / `-0.45f`. Writing those back
    verbatim would be correct to the bit and unreadable to everyone — and it would make every
    rebase pass churn the file again. So: take the SHORTEST decimal that still lands inside the
    tolerance the comparison uses, which is exactly the number a person would have written.
    """
    x = float(x)
    for digits in range(1, 10):
        cand = float(f"{x:.{digits}g}")
        if near(cand, x):
            x = cand
            break
    if x == int(x) and abs(x) < 1e15:
        return f"{int(x)}f"
    s = repr(x)
    if s.endswith(".0"):
        s = s[:-2]
    return s + "f"


def render_cs(value, typ, old_init):
    if typ == "bool":
        return "true" if value else "false"
    if typ == "int":
        return str(int(value))
    if typ == "float":
        return fnum(float(value))
    if typ == "string":
        if old_init.startswith("nameof("):
            # nameof() is a rename-safe spelling of a name; a cfg cannot re-spell it.
            return old_init
        return '"' + str(value).replace("\\", "\\\\").replace('"', '\\"') + '"'
    if typ in ("Vector2", "Vector3", "Color"):
        return f"new {typ}(" + ", ".join(fnum(v) for v in value) + ")"
    # an enum: keep the type spelling the file already uses, swap the member
    return old_init.rsplit(".", 1)[0] + "." + str(value)


# ---------------------------------------------------------------------------- the cfg side
CFG_SECTION = re.compile(r"^\[(?P<name>[^\]]+)\]\s*$")
CFG_ENTRY = re.compile(r"^(?P<key>[^=#\s][^=]*?)\s*=\s*(?P<value>.*?)\s*$")


def load_cfgs(cfg_dir):
    out = {}
    for path in sorted(cfg_dir.glob("*.cfg")):
        section = None
        for line in path.read_text(encoding="utf-8").splitlines():
            s = line.strip()
            if not s or s.startswith("#"):
                continue
            m = CFG_SECTION.match(s)
            if m:
                section = m.group("name")
                continue
            m = CFG_ENTRY.match(line)
            if m and section:
                out[(section, m.group("key").strip())] = (m.group("value"), path)
    return out


def coerce(raw, typ):
    """A cfg's serialised value -> python, in the shape `typ` compares in."""
    raw = raw.strip()
    if typ == "bool":
        if raw.lower() in ("true", "false"):
            return raw.lower() == "true"
        raise ValueError(f"not a bool: {raw!r}")
    if typ == "int":
        return int(raw)
    if typ == "float":
        return float(raw)
    if typ == "string":
        return raw
    if typ == "Color":
        # BepInEx serialises a Color as 8 hex digits, RRGGBBAA — one byte per channel, so the
        # comparison below quantises the source literal the same way before comparing.
        if not re.fullmatch(r"[0-9a-fA-F]{8}", raw):
            raise ValueError(f"not an RRGGBBAA colour: {raw!r}")
        return tuple(int(raw[i:i + 2], 16) / 255.0 for i in (0, 2, 4, 6))
    if typ in ("Vector2", "Vector3"):
        # BepInEx serialises these as {"x":0.1,"y":0.2,...} (JSON, via its TomlTypeConverter)
        nums = re.findall(r"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?", raw)
        want = {"Vector2": 2, "Vector3": 3}[typ]
        if len(nums) != want:
            raise ValueError(f"not a {typ}: {raw!r}")
        return tuple(float(n) for n in nums)
    return raw          # enum: the cfg stores the member name


def near(a, b):
    return abs(a - b) <= FLOAT_TOL_REL * max(FLOAT_TOL_FLOOR, abs(a), abs(b))


def same(a, b, typ):
    if typ == "float":
        return near(a, b)
    if typ == "Color":
        # a cfg colour only carries 8 bits per channel — compare where it can be compared
        return all(round(min(max(x, 0.0), 1.0) * 255) == round(min(max(y, 0.0), 1.0) * 255)
                   for x, y in zip(a, b))
    if typ in ("Vector2", "Vector3"):
        return all(near(x, y) for x, y in zip(a, b))
    return a == b


# ------------------------------------------------------------------------------- seed map
def seeded_identities():
    """(section, key) -> the entry whose LIVE value seeds it.

    A handful of binds take their default from another entry rather than a literal (the
    per-style FigureGrab keys seed off the legacy globals, and the Rot* trio off their own
    predecessors). Those keys have no literal to rebase; found here so the report can name
    what does carry the value instead of pretending the key is unmapped.
    """
    out = {}
    styles = ["Glove", "Plate", "Arcane"]
    text = (SRC / "Board" / "FigureGrab" / "FigureGrabConfig.cs").read_text(encoding="utf-8")
    for m in re.finditer(r'Bind\(\s*"(?P<sec>[^"]+)",\s*\$"\{s\}(?P<key>[A-Za-z0-9_]+)",\s*'
                         r'(?P<seed>[A-Za-z0-9_]+)(?:\[i\])?\.Value', text):
        seed = m.group("seed")
        for s in styles:
            src = seed[len("Style"):] if seed.startswith("Style") else seed
            out[(m.group("sec"), s + m.group("key"))] = \
                f"[{m.group('sec')}] {(s + src) if seed.startswith('Style') else src}"
    return out


# ---------------------------------------------------------------------------------- main
def run(mode, cfg_dir, verbose):
    entries, dupes = load_entries()
    if dupes:
        print("error: two Defaults lines claim the same config identity:", file=sys.stderr)
        for d in dupes:
            print(f"  [{d[0]}] {d[1]}", file=sys.stderr)
        return 2
    if not cfg_dir.is_dir():
        print(f"error: no cfg directory at {cfg_dir}", file=sys.stderr)
        return 2
    cfgs = load_cfgs(cfg_dir)
    seeds = seeded_identities()

    mismatches, unmapped, notices, refused = [], [], [], []
    for ident, (raw, cfgpath) in sorted(cfgs.items()):
        e = entries.get(ident)
        if e is None:
            if ident in seeds:
                notices.append((ident, f"seeded from {seeds[ident]} — no literal of its own"))
            else:
                unmapped.append((ident, cfgpath.name, raw))
            continue
        have = e.value()
        if have is None:
            refused.append((ident, f"{e.path.name}:{e.lineno + 1} initialiser is not a literal: "
                                   f"{e.init}"))
            continue
        try:
            want = coerce(raw, e.type)
        except ValueError as ex:
            refused.append((ident, f"cannot read {cfgpath.name} value {raw!r} as {e.type} ({ex})"))
            continue
        if same(have, want, e.type):
            continue
        if e.marker:
            notices.append((ident, f"{e.marker}: {e.why} — cfg says {show(want)}, "
                                   f"shipped default stays {show(have)}"))
            continue
        mismatches.append((ident, e, have, want))

    missing_in_cfg = sorted(set(entries) - set(cfgs))

    if mode == "apply" and mismatches:
        by_file = {}
        for _, e, _, want in mismatches:
            by_file.setdefault(e.path, []).append((e, want))
        for path, items in by_file.items():
            lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
            for e, want in items:
                nl = "\n" if lines[e.lineno].endswith("\n") else ""
                lines[e.lineno] = e.rendered(want).rstrip() + nl
            path.write_text("".join(lines), encoding="utf-8")

    # ---------------------------------------------------------------------------- report
    verb = "rebased" if mode == "apply" else "differ"
    if mismatches:
        print(f"=== {len(mismatches)} defaults {verb} ===")
        w = max(len(f"[{s}] {k}") for (s, k), *_ in mismatches)
        for (s, k), e, have, want in mismatches:
            print(f"  {f'[{s}] {k}'.ljust(w)}  {show(have)}  ->  {show(want)}   ({e.name})")
    else:
        print(f"=== every cfg value already equals the shipped default "
              f"({len(cfgs) - len(unmapped) - len(notices) - len(refused)} entries checked) ===")

    if notices:
        print(f"--- {len(notices)} entries left alone (seeded / legacy / pinned) ---")
        for (s, k), why in notices:
            print(f"  [{s}] {k}: {why}")
    if unmapped:
        print(f"--- {len(unmapped)} cfg entries with no Defaults line (NOT guessed at) ---")
        for (s, k), f, raw in unmapped:
            print(f"  [{s}] {k} = {raw}   ({f})")
    if refused:
        print(f"--- {len(refused)} entries REFUSED ---")
        for (s, k), why in refused:
            print(f"  [{s}] {k}: {why}")
    if verbose and missing_in_cfg:
        print(f"--- {len(missing_in_cfg)} defaults absent from the cfgs (left alone) ---")
        for s, k in missing_in_cfg:
            print(f"  [{s}] {k}")

    if refused:
        return 2
    if mode == "check" and mismatches:
        return 1
    return 0


def show(v):
    if isinstance(v, tuple):
        return "(" + ", ".join(f"{x:g}" for x in v) + ")"
    if isinstance(v, float):
        return f"{v:g}"
    return str(v)


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("mode", choices=["check", "apply"])
    ap.add_argument("--cfg-dir", type=pathlib.Path, default=CFG_DIR)
    ap.add_argument("-v", "--verbose", action="store_true",
                    help="also list defaults that no cfg mentions")
    a = ap.parse_args()
    sys.exit(run(a.mode, a.cfg_dir, a.verbose))


if __name__ == "__main__":
    main()
