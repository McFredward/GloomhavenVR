#!/usr/bin/env python3
"""
Remote-vs-local rendering lint: every FROZEN constant in Net/Remote*.cs must still equal the
config default it copies.

WHY THIS EXISTS
---------------
A remote player's board, fan and buttons are drawn by the Net/Remote* renderers, which cannot
read the SENDER's config — nothing on the wire carries it. They therefore hold frozen copies of
the shipped DEFAULTS, so that two players who have not tuned anything (the overwhelmingly common
case) see the same thing.

That works exactly until someone changes a default. On 2026-07-30 the whole shipped config was
re-based on a tuned setup, and six of these constants silently stopped matching: the remote hand
fan kept the old 0.1792 m radius and 91-degree sweep while the local one moved to 0.2192 / 103,
and three button caps kept their old sizes. Nothing failed, nothing logged; the two players simply
saw different boards.

This is not a test of behaviour — it is the one check that makes a default change tell you it has
a second home. Adding a pair here costs one line; forgetting costs a desync nobody can see from
inside their own headset.

WHAT CHANGED (Defaults refactor)
--------------------------------
Both sides now READ THE SAME `Defaults` entry — the frozen constants are `= Defaults.FanTiltFactor`
rather than a re-typed number — so the drift this file was written to catch can no longer be
expressed. The check stays anyway, and got stricter rather than weaker: it verifies that the
remote constant and the local Bind name the SAME entry, and that the entry is the one carrying
the `// => [Section] Key` annotation for the pair. Pointing at the wrong entry is as wrong as a
stale literal, and it is exactly the mistake a copy-paste makes. If anyone ever puts a literal
back on either side, this fails and says so.

WHAT IT DOES NOT COVER — AND WHAT IT NOW DOES
--------------------------------------------
This used to end with: "A player who TUNES one of these values still desyncs, because the value is
not transmitted. That is a protocol question, not a lint question." The protocol question was
answered by extension record 28 (BOARD TUNING): the sender's own dials now ride the wire whenever
they differ from the shipped default, and `RemoteBoardTuning` resolves them on the receiver.

That does NOT retire this check — it makes it the check on the FALLBACK. The Remote* renderers hold
these values as fields INITIALISED to the shipped default and overwritten from the wire only when
the owner has actually moved that dial. The initialiser is therefore still what an untuned peer is
drawn with, and it must still name the same `Defaults` entry the local Bind draws on. So the pairs
below now match either form:

    private const float PalmOffset = Defaults.FanPalmOffset;   // frozen (board furniture)
    private float _palmOffset       = Defaults.FanPalmOffset;  // wire-overridable fallback

What is genuinely no longer covered: nothing. A default change still has to be made in one place.
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "GloomhavenVR"

# remote file : constant : the config Bind whose default it copies (section, key)
PAIRS = [
    # THE KEYCAP GEOMETRY FAMILY. These were `const` — genuinely frozen — until 2026-08-09, when
    # extension record 28 grew ids 81..98 (+ the turn-flow cap's shape at 228) and they became
    # wire-overridable fallbacks: the constructor seeds each one from RemoteBoardTuning, whose own
    # fallback for an absent field is the very Defaults entry named here. So they stay on this list
    # for exactly the reason the header gives, one keyword different — the initialiser is still what
    # an UNTUNED peer's board is drawn with, and a default that moved without its copy would put two
    # untuned players in front of two different boards with nothing to tell them.
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapW", "BoardButtons", "Width"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapH", "BoardButtons", "Height"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapD", "BoardButtons", "Depth"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_pinCapW", "BoardDashboard", "PinWidth"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapH", "BoardDashboard", "Height"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapD", "BoardDashboard", "Depth"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapD", "RestButtons", "Depth"),
    # The turn-flow (SKIP) cap's SQUARE side lengths. They were not on this list at all before —
    # EIGHT PAIRS LEFT THIS TABLE ON 2026-08-25 — the four [RoundButtons] cap sizes, its travel and
    # the three [ButtonColors] cluster tints. They pinned the mirrored turn-flow SKIP cap's frozen
    # constants against the defaults they copied; that cap is a generic board keycap now and reads
    # the [BoardButtons] / BoardCapTint copies that are still listed below, so there is nothing left
    # to drift.
    # (The note that stood here explained that two of them had once been BARE
    # `Defaults.RoundButtons_Width/Height` reads inlined at the build site, which is
    # the shape of drift this file exists to catch and which nothing could have caught.
    # The REST keycap's square side lengths (record 28 ids 99..100). They existed nowhere in Net/
    # until 2026-08-09 because the mirrored rest cap had no Square branch to need them; it has one
    # now (RemoteBoardFurniture.RestCap dispatches on the owner's [Cards] RestButtonShape_{board}),
    # so they are wire-overridable fallbacks like every size above and belong on this list for the
    # same reason: an untuned peer's square rest cap is drawn at the initialiser.
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapW", "RestButtons", "Width"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapH", "RestButtons", "Height"),
    # THE [ButtonColors] FAMILY (record 28 ids 48..53 / 170). Nineteen float channels — six colours
    # plus the label keyline's width — which this renderer did not read AT ALL until 2026-08-09, and
    # that omission was not neutral: the shipped cap-face tint is 0.5 grey and the LOCAL caps are
    # painted through it, so every mirrored keycap was drawn at TWICE its owner's brightness for two
    # players who had never touched a slider. This list is where that class of bug is supposed to be
    # caught, and it could not catch this one because there was nothing on either end to compare —
    # which is the argument for adding the pairs the same day the renderer grows the reads.
    #   Held as individual float CHANNELS rather than as Color fields precisely so they are
    #   checkable: this script verifies that ONE constant names ONE annotated Defaults entry, and a
    #   Color built inline from three of them would be an "expr" it could only shrug at.
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelR", "ButtonColors", "LabelR"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelG", "ButtonColors", "LabelG"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelB", "ButtonColors", "LabelB"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelOutlineR", "ButtonColors", "LabelOutlineR"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelOutlineG", "ButtonColors", "LabelOutlineG"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelOutlineB", "ButtonColors", "LabelOutlineB"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_labelOutlineWidth", "ButtonColors", "LabelOutlineWidth"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapTintR", "ButtonColors", "BoardCapTintR"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapTintG", "ButtonColors", "BoardCapTintG"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapTintB", "ButtonColors", "BoardCapTintB"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapTintR", "ButtonColors", "DashCapTintR"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapTintG", "ButtonColors", "DashCapTintG"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapTintB", "ButtonColors", "DashCapTintB"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapTintR", "ButtonColors", "RestCapTintR"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapTintG", "ButtonColors", "RestCapTintG"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapTintB", "ButtonColors", "RestCapTintB"),
    # The mirrored keycap ANIMATIONS (2026-08-08 1:1 round). A peer's cap dips its own category's
    # TRAVEL on the synced press edge and crumbles/assembles over the ButtonAnim durations, so all
    # six numbers have a second home in Net/ and belong on this list for exactly the reason the
    # sizes above do: retune the local feel and the remote boards must follow, or a press looks 4 mm
    # deep on one screen and 8 on another. The four TRAVELS ride record 28 (ids 88 / 92 / 96 / 98)
    # and, since ModBuild 304, so do the two ButtonAnim DURATIONS (ids 174 / 175) plus the two
    # switches beside them (234 / 235). The constants below are no longer the CLOCK — the clock is
    # per board now (RemoteBoardFurniture.CapAnim) — they are what an untuned or pre-record sender
    # falls back to, which is exactly the "wire-overridable fallback" case this list is for.
    ("Net/Remote/RemoteBoardFurniture.cs", "_boardCapTravel", "BoardButtons", "Travel"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_dashCapTravel", "BoardDashboard", "Travel"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_restCapTravel", "RestButtons", "Travel"),
    ("Net/Remote/RemoteBoardFurniture.cs", "DissolveSeconds", "ButtonAnim", "DisappearSeconds"),
    ("Net/Remote/RemoteBoardFurniture.cs", "AppearSeconds", "ButtonAnim", "AppearSeconds"),
    # The hand fan's geometry: wire-overridable fields (record 28) whose INITIALISER is what an
    # untuned peer is drawn with — see the header. Same guarantee, one keyword different.
    ("Net/Remote/RemoteHandFan.cs", "_palmOffset", "Cards", "FanPalmOffset"),
    ("Net/Remote/RemoteHandFan.cs", "_radius", "Cards", "FanEffectiveRadius"),
    ("Net/Remote/RemoteHandFan.cs", "_arcSweepDegrees", "Cards", "FanArcSweepDegrees"),
    ("Net/Remote/RemoteHandFan.cs", "_perCardStepDegrees", "Cards", "FanPerCardStepDegrees"),
    ("Net/Remote/RemoteHandFan.cs", "_archFactor", "Cards", "FanFlatCurvatureFactor"),
    ("Net/Remote/RemoteHandFan.cs", "_tiltFactor", "Cards", "FanTiltFactor"),
    ("Net/Remote/RemoteHandFan.cs", "_faceViewer", "Cards", "FanFaceViewer"),
    ("Net/Remote/RemoteHandFan.cs", "_sideDepthCurve", "Cards", "FanSideDepthCurve"),
    ("Net/Remote/RemoteHandFan.cs", "_curvePower", "Cards", "FanCurvePower"),
    ("Net/Remote/RemoteHandFan.cs", "_gazeApexFollow", "Cards", "FanGazeApexFollow"),
    ("Net/Remote/RemoteHandFan.cs", "_splitMultiplier", "Cards", "FanSplitMultiplier"),
    ("Net/Remote/RemoteHandFan.cs", "_splitFalloff", "Cards", "FanSplitFalloff"),
    ("Net/Remote/RemoteHandFan.cs", "_splitScale", "Cards", "FanHoverSplitScale"),
    ("Net/Remote/RemoteHandFan.cs", "_popForward", "Cards", "FanSelectedPopForward"),
    # The hand fan's REVEAL. These were `const OpenSeconds/OpenStagger` — frozen, so a peer's fan
    # opened at THIS client's timing no matter what its owner had tuned. They became
    # wire-overridable fields (record 28, ids 154..155) the moment the record was PAGED and had room
    # again (Net/BoardTunePages.cs); the initialiser is still what an untuned peer is drawn with, so
    # this pair carries exactly the guarantee it always did, one keyword different.
    ("Net/Remote/RemoteHandFan.cs", "_openSeconds", "Cards", "FanOpenDuration"),
    ("Net/Remote/RemoteHandFan.cs", "_openStagger", "Cards", "FanOpenStagger"),
    # The HAND fan's character-SWAP exchange (2026-08-09 — "mach auch hier eine neue coolere
    # Tauschanimation rein die den Fächer austauscht"). Wire-overridable fields (record 28, ids
    # 77..78 / 150..153 / 226..227) whose INITIALISER is what an untuned peer's exchange is drawn
    # with. On this list for the reason the header gives: re-tune how your own hand is exchanged and
    # the mirrored fans must follow, or the swap wipes across the palm in half a second on one
    # screen and a fifth of one on another — and nobody can see that from inside their own headset.
    ("Net/Remote/RemoteHandFan.cs", "_swapDuration", "Cards", "FanSwapDuration"),
    ("Net/Remote/RemoteHandFan.cs", "_swapStagger", "Cards", "FanSwapStagger"),
    ("Net/Remote/RemoteHandFan.cs", "_swapOverlap", "Cards", "FanSwapOverlap"),
    ("Net/Remote/RemoteHandFan.cs", "_swapTravel", "Cards", "FanSwapTravel"),
    ("Net/Remote/RemoteHandFan.cs", "_swapArc", "Cards", "FanSwapArc"),
    ("Net/Remote/RemoteHandFan.cs", "_swapSpinDegrees", "Cards", "FanSwapSpinDegrees"),
    ("Net/Remote/RemoteHandFan.cs", "_swapSeedScale", "Cards", "FanSwapSeedScale"),
    ("Net/Remote/RemoteHandFan.cs", "_swapSettleOvershoot", "Cards", "FanSwapSettleOvershoot"),
    # The ITEM fan's open/close ANIMATION (presence pass, 2026-08-08 — "Ich mag die Animation im
    # Item-Pile sehr aber sie ist (insbesondere in mixed Reality) etwas zu dezent."). Same shape as
    # the hand fan's geometry above: wire-overridable fields (record 28, ids 76 / 144..149 / 197)
    # whose INITIALISER is what an untuned peer's fan is drawn with. All eight belong on this list
    # for the reason the header gives — re-tune the local feel and the mirrored fans must follow, or
    # the cards deal out with a settle on one screen and slide open on another, and nobody can see
    # that from inside their own headset.
    ("Net/Remote/RemoteItemFan.cs", "_openSeconds", "Cards", "ItemFanOpenDuration"),
    ("Net/Remote/RemoteItemFan.cs", "_openStagger", "Cards", "ItemFanOpenStagger"),
    ("Net/Remote/RemoteItemFan.cs", "_openArc", "Cards", "ItemFanOpenArc"),
    ("Net/Remote/RemoteItemFan.cs", "_openSpinDegrees", "Cards", "ItemFanOpenSpinDegrees"),
    ("Net/Remote/RemoteItemFan.cs", "_seedScale", "Cards", "ItemFanSeedScale"),
    ("Net/Remote/RemoteItemFan.cs", "_settleOvershoot", "Cards", "ItemFanSettleOvershoot"),
    ("Net/Remote/RemoteItemFan.cs", "_closeSeconds", "Cards", "ItemFanCloseDuration"),
    ("Net/Remote/RemoteItemFan.cs", "_closeStagger", "Cards", "ItemFanCloseStagger"),
    # The ITEM-USE BERTH's art (2026-08-09 — the mirrored half of "Ueberarbeite das Aussehen des
    # Item-Overlays"). These landed as FROZEN constants because extension record 28 was at its exact
    # 255-byte per-record ceiling and could not carry them; the PAGING round in the same build
    # removed that ceiling and RESERVED ids 80 / 161..169 for exactly these ten. Those reservations
    # are CLAIMED now — all ten are wire-overridable fallback fields like the ItemFan* family above,
    # so an owner who thickens their berth outline or opens its glow is seen doing it. The
    # initialiser is what an UNTUNED peer's berth is still drawn with, which is what keeps them on
    # this list: move a default without moving its copy and two untuned players see two different
    # berths, and neither of them can tell from inside their own headset.
    ("Net/Remote/RemoteBoardFurniture.cs", "_itemBerthRingThickness", "Cards", "ItemBerthRingThickness"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_itemBerthGlow", "Cards", "ItemBerthGlow"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_itemBerthPingSeconds", "Cards", "ItemBerthPingSeconds"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_itemBerthPingReach", "Cards", "ItemBerthPingReach"),
    ("Net/Remote/RemoteBoardFurniture.cs", "_itemBerthRevealSeconds", "Cards", "ItemBerthRevealSeconds"),
    # The CLOSED ITEMS PILE's "an item is usable" cue on a peer's board (2026-08-09 — the 1:1 gap
    # where a peer saw no cue at all). Same story as the berth above, and the same resolution: frozen
    # while record 28 could not carry the dials, wire-overridable fallbacks (ids 161..165) now that
    # paging has removed the ceiling. The initialiser is what an untuned peer's stack beats at.
    ("Net/Remote/RemoteControlBoard.cs", "_itemCueBeatSeconds", "Cards", "ItemCueBeatSeconds"),
    ("Net/Remote/RemoteControlBoard.cs", "_itemCueRingReach", "Cards", "ItemCueRingReach"),
    ("Net/Remote/RemoteControlBoard.cs", "_itemCueRingAlpha", "Cards", "ItemCueRingAlpha"),
    ("Net/Remote/RemoteControlBoard.cs", "_itemCueEmberRate", "Cards", "ItemCueEmberRate"),
    ("Net/Remote/RemoteControlBoard.cs", "_itemCueEmberSize", "Cards", "ItemCueEmberSize"),
    # THE SLOT CARD'S SEAT DEPTH (2026-08-27, wire id 101). It was a private literal --
    # CardOnAnchorProudZ = -0.003f -- and therefore a value NO check could hold: the owner seats a
    # played card at -[Cards] SlotCardInset through the slot's own 1.3x SlotScale, which is 5.2 mm
    # board-local at the shipped default, so every peer drew every card 2.2 mm too shallow before
    # anybody had touched a dial, and by the owner's whole tuning range once they had. It rides
    # record 28 now and lands as a seeded field like the families above; the initialiser is what an
    # untuned or pre-record peer's card is drawn with, which is what it is doing on this list.
    #   Note for whoever reads this next to a diff: RemoteControlBoard.ProudZ = -0.004f is NOT this
    #   value wearing another name. It happens to equal Defaults.SlotCardInset and means something
    #   else entirely -- the flat fallback board's shared content plane, which no dial moves. Do not
    #   collapse the two.
    ("Net/Remote/RemoteControlBoard.cs", "_slotCardInset", "Cards", "SlotCardInset"),
    # The PILE FANS' shape (2026-08-09, the paging round). These four were BARE LITERALS — the exact
    # thing the header calls "a stale literal" — and nothing caught them because a literal with no
    # pair on this list has nothing to be checked against. Worse than stale: RemoteItemFan's radius
    # had been re-typed from FanEffectiveRadius (0.1792) while the local ItemsPile builds its arc
    # from FanRadius (0.16), so every peer's item fan was 12 % wider than its owner's, for every
    # player, tuned or not. They are wire-overridable fallbacks now (record 28, ids 79 / 157..160 /
    # 198..200) and they are on this list so the literal cannot come back.
    #   RemoteItemFan._radius / RemoteBrowserFan._radius are deliberately NOT listed: they are
    #   PRODUCTS (Defaults.FanRadius × Defaults.FanRadiusFactor_*), and this checker verifies a
    #   constant IS one named Defaults entry. Their two factors are pinned individually instead,
    #   which is the same coverage without teaching the checker arithmetic.
    ("Net/Remote/RemoteItemFan.cs", "_maxStepDegrees", "Cards", "FanStepDegrees_Items"),
    ("Net/Remote/RemoteItemFan.cs", "_lerpSpeed", "Cards", "CardLerpSpeed"),
    ("Net/Remote/RemoteBrowserFan.cs", "_maxStepDegrees", "Cards", "FanStepDegrees_Discard"),
    ("Net/Remote/RemoteBrowserFan.cs", "_emergeSharpness", "Cards", "CardLerpSpeed"),
]

DEFAULTS_DIR = SRC / "Defaults"

# One annotated Defaults line: `internal const float FanTiltFactor = 0.85f;  // => [Cards] FanTiltFactor`
ENTRY_RE = re.compile(
    r"internal\s+const\s+float\s+(?P<name>\w+)\s*=\s*(?P<init>-?[\d.]+)f\s*;\s*"
    r"//\s*=>\s*\[(?P<section>[^\]]+)\]\s+(?P<key>\S+)")


def defaults_table():
    """(section, key) -> (entry name, value) for every float default in src/.../Defaults/."""
    out = {}
    for path in sorted(DEFAULTS_DIR.glob("Defaults.*.cs")):
        for m in ENTRY_RE.finditer(path.read_text(encoding="utf-8")):
            out[(m.group("section"), m.group("key"))] = (m.group("name"), float(m.group("init")))
    return out


DEFAULTS = defaults_table()


def bind_reference(section, key):
    """The Defaults entry a Bind("Section", "Key", ...) names as its default, or None.

    A FAMILY of keys bound in a loop — `Bind("Cards", $"FanStepDegrees_{pileNames[p]}",
    stepSeeds[p], …)` — cannot be matched by key text, because the key does not exist as a literal
    anywhere. For those the check falls back to the weaker but still real question: does the local
    side still NAME this Defaults entry at all? A seed array that stopped listing it is exactly the
    drift this file exists to catch, and it is what a rename or a re-typed literal would produce.
    """
    for path in SRC.rglob("*.cs"):
        if path.parent.name == "Defaults":
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        m = re.search(r'Bind\(\s*"' + re.escape(section) + r'"\s*,\s*"' + re.escape(key)
                      + r'"\s*,\s*([^,]+),', text)
        if not m:
            continue
        raw = m.group(1).strip()
        return raw[len("Defaults."):] if raw.startswith("Defaults.") else raw

    # Interpolated / looped bind: `$"Prefix_{something}"` with the entry in a seed array.
    stem = key.rsplit("_", 1)[0]
    for path in SRC.rglob("*.cs"):
        if path.parent.name == "Defaults":
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        if not re.search(r'Bind\(\s*"' + re.escape(section) + r'"\s*,\s*\$"' + re.escape(stem)
                         + r'_\{', text):
            continue
        entry = DEFAULTS.get((section, key))
        if entry and re.search(r"\bDefaults\." + re.escape(entry[0]) + r"\b", text):
            return entry[0]
    return None


def frozen(rel, const):
    """What the Remote renderer's constant/fallback field resolves to: a Defaults name, or a
    literal. Accepts both the frozen `const float X =` form and the wire-overridable
    `private float _x =` form — see the header for why they carry the same guarantee."""
    text = (SRC / rel).read_text(encoding="utf-8")
    m = re.search(r"const float " + const + r" *= *([^;]+);", text)
    if not m:
        m = re.search(r"private (?:readonly )?float " + const + r" *= *([^;]+);", text)
    if not m:
        return None
    raw = m.group(1).strip()
    if raw.startswith("Defaults."):
        return ("ref", raw[len("Defaults."):])
    try:
        return ("literal", float(raw.rstrip("f")))
    except ValueError:
        return ("expr", raw)


bad = []
for rel, const, section, key in PAIRS:
    want = DEFAULTS.get((section, key))
    have = frozen(rel, const)
    if want is None:
        bad.append(f"{rel}: no Defaults entry annotated `// => [{section}] {key}` — the pair "
                   f"names a config identity that no longer has a shipped default")
        continue
    name, value = want
    if have is None:
        bad.append(f"{rel}: constant {const} not found")
        continue
    kind, payload = have
    if kind == "ref":
        # The strong form: the constant IS the default, so it cannot drift. Verify it is the
        # RIGHT one — a reference to the wrong entry is exactly as wrong as a stale literal.
        if payload != name:
            bad.append(f"{rel}: {const} references Defaults.{payload}, but [{section}] {key} "
                       f"is Defaults.{name}")
    elif kind == "literal":
        if abs(payload - value) > 1e-6:
            bad.append(f"{rel}: {const} = {payload:g} but [{section}] {key} now defaults to "
                       f"{value:g} — a remote player would be drawn with the old value")
        else:
            bad.append(f"{rel}: {const} still copies [{section}] {key} as a LITERAL — point it at "
                       f"Defaults.{name} so it cannot drift")
    else:
        bad.append(f"{rel}: {const} = {payload} — cannot tell whether it still matches "
                   f"[{section}] {key}")

    # Belt and braces: the BIND must draw on the same entry. If a bind ever went back to a
    # literal, the Defaults line would stop being the thing the local player sees, and the
    # reference above would be checking against a value nobody uses.
    ref = bind_reference(section, key)
    if ref is None:
        bad.append(f"[{section}] {key}: no Bind found — the pair names an entry that no "
                   f"longer exists")
    elif ref != name:
        bad.append(f"[{section}] {key}: the bind default is `{ref}`, not `Defaults.{name}` — "
                   f"the local player no longer reads the entry the remote copy mirrors")

if bad:
    print("error: remote rendering constants drifted from the defaults they copy:", file=sys.stderr)
    for b in bad:
        print("  " + b, file=sys.stderr)
    sys.exit(1)

print(f"remote defaults: {len(PAIRS)} frozen constants / wire-overridable fallbacks resolve "
      f"to the same Defaults entries as the binds they mirror")
