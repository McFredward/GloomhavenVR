#!/usr/bin/env python3
"""
1:1 WIRE-COVERAGE LINT: every dial that changes how a player's CONTROL BOARD LOOKS must either ride
extension record 28, or be listed below with a reason.

WHY THIS EXISTS
---------------
The standing ruling (user, 2026-08-09, verbatim):

    "Die 1:1 Regel besagt dass alle remote Spieler immer 1:1 das am Board (mit den expliziten
     ausgemachten Ausnahmen) sieht. Ändert ein Spieler also die Positionen für sich selber, so
     sollen alle anderen diese Position bei seinem board auch sehen."

That is a GUARANTEE, and a guarantee that only a human remembers is not one. Its sibling
`check-remote-defaults.py` already catches a frozen remote constant DRIFTING from the default it
mirrors. Nothing caught the other half — a NEW board-affecting dial being added with no wire field
at all. That half is the one that actually kept happening: the pile browse fan's radius and step,
the hand fan's reveal timing and the item cue / item berth sets were all added as ordinary config
entries, drawn on every peer's board, and never went near record 28. Nobody could see it, because
from inside your own headset your board is always right.

So this script asserts the OTHER direction. It reads:

  * every annotated shipped default (`// => [Section] Key` in src/GloomhavenVR/Defaults/),
  * every wire field id in NetProtocol.cs and the `[Section] Key` its doc comment names,
  * which of those ids `BoardTuningSampler.Sample` actually writes,

and fails when a dial in a BOARD SECTION is neither covered by a written wire field nor listed in
EXEMPT with a reason.

    Adding a pair here costs one line; forgetting costs a desync nobody can see from inside their
    own headset.

WHERE THE LINE IS DRAWN — the test EXEMPT entries have to pass
--------------------------------------------------------------
"Would a peer LOOKING AT THAT PLAYER'S BOARD see a difference?"

  If yes, it goes on the wire. A dock offset, a scale, a keycap size, an animation's timing, an
  arc's radius — all yes.

  If no, it is legitimately local and gets an EXEMPT line saying WHICH of the four reasons applies:
    COMFORT   — the player's own body/settings, invisible on their board (spawn recipe, grab
                button, gaze smoothing, sounds they hear).
    DERIVED   — the RESULT is already synced by another mechanism, so syncing the recipe would be
                redundant or actively wrong (board pose/scale, fan anchors, slot card size).
    NO-OP     — the dial has no rendered effect even locally (legacy keys kept for cfg
                back-compat, one-shot migration markers).
    PENDING   — genuinely board-affecting, genuinely not covered yet, with the reason it is parked
                and what unblocks it. THESE ARE DEBTS, NOT DECISIONS. The count is printed on every
                run so it cannot quietly grow.

A PENDING line is the only kind that is allowed to be uncomfortable, and that is deliberate: the
alternative to letting one exist is a script nobody can make pass, which is a script somebody
deletes.
"""
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
SRC = ROOT / "src" / "GloomhavenVR"

# Sections whose entries describe THE CONTROL BOARD AND ITS FURNITURE. Everything annotated into one
# of these is board-affecting until an EXEMPT line says otherwise — the default answer is "on the
# wire", which is the direction the ruling points.
BOARD_SECTIONS = {
    "Cards",            # the board itself, its docks, its fans, its cards
    "BoardButtons",     # the Confirm/Undo keycaps standing on the board
    "BoardDashboard",   # the gear / Fixiert caps on the board's dashboard strip
    "RestButtons",      # the short/long rest discs
    "RoundButtons",     # the round-phase cluster caps
    "ButtonAnim",       # how every one of those caps crumbles and assembles
    "ButtonColors",     # what they are tinted and lettered in
}

# Sections that LOOK board-ish but are pure legacy migration sources: bound only inside
# ButtonTuning.MigrateLegacy as locals, read by nothing, kept so an old cfg still imports.
LEGACY_SECTIONS = {"SquareCaps", "TransientButtons"}

# ---------------------------------------------------------------------------------------------
#  EXEMPT — (section, key) -> (KIND, reason).  Key may end in "_{board}" to cover the per-board
#  family (_Oak/_Steel/_Bronze) in one line, exactly like the wire ids' own doc comments do.
# ---------------------------------------------------------------------------------------------
EXEMPT = {
    # ---- DERIVED: the RESULT is already on the wire, so the recipe must not be -----------------
    ("Cards", "BoardScale_{board}"): ("DERIVED", "the board's world SCALE rides the synced board transform"),
    ("Cards", "BoardTilt_{board}"): ("DERIVED", "folded into the synced board ROTATION"),
    ("Cards", "BoardYaw_{board}"): ("DERIVED", "folded into the synced board ROTATION"),
    ("Cards", "BoardPosOffset_{board}"): ("DERIVED", "folded into the synced board POSITION"),
    ("Cards", "AssetRotation_{board}"): ("DERIVED", "superseded by AssetPitch/Yaw/Roll, which ARE on the wire (ids 192..194)"),
    ("Cards", "BoardPitchMin_{board}"): ("DERIVED", "a clamp on the grab pitch; only the clamped pose is rendered, and it is synced"),
    ("Cards", "BoardPitchMax_{board}"): ("DERIVED", "as BoardPitchMin"),
    ("Cards", "BoardMinWidthMeters"): ("DERIVED", "a clamp on the apparent width; the clamped scale is synced"),
    ("Cards", "BoardMaxWidthMeters"): ("DERIVED", "as BoardMinWidthMeters"),
    ("Cards", "TrayForward"): ("DERIVED", "seats the board; the resulting pose is synced"),
    ("Cards", "TrayDown"): ("DERIVED", "seats the board; the resulting pose is synced"),
    ("Cards", "TrayRight"): ("DERIVED", "seats the board; the resulting pose is synced"),
    ("Cards", "TrayYaw"): ("DERIVED", "seats the board; the resulting pose is synced"),
    ("Cards", "TrayPitch"): ("DERIVED", "seats the board; the resulting pose is synced"),
    ("Cards", "TrayScale"): ("DERIVED", "seats the board; the resulting SCALE is synced"),
    ("Cards", "TrayFollow"): ("DERIVED", "an anchor MODE; the pin cap's label rides record 13 and the pose rides the board transform"),
    ("Cards", "BoardMoveMode"): ("DERIVED", "its own bind says it: local cosmetics only, peers see the resulting board pose"),
    # ("Cards", "SlotCardFill") is GONE with its dial (retired 2026-08-11). It was DERIVED because
    # the product it fed rode record 11 — true while the card was the only thing it sized. Its
    # per-board successor SlotOverlayScale_{board} also sizes the blinking slot overlays, which are
    # NOT cards and are built from a factor on the peer's side, so it carries a field of its own
    # (id 171) instead of an exemption.
    ("Cards", "BrowseFanOffset"): ("DERIVED", "the browse fan's board-local anchor rides extension record 5 while the fan is open"),
    ("Cards", "Board"): ("DERIVED", "the board STYLE rides the extras block, byte A bits 5..6"),
    # RECLASSIFIED FROM PENDING (2026-08-09). Its reason read "a bool, and record 28 has no bool
    # kind; the wanted-slot glow is not mirrored at all yet" — and BOTH halves were wrong. The record
    # has a bool kind now (BoardTuningSampler.Bool8, ids 229..230), and the glow was never
    # unmirrored: RemoteBoardFurniture builds the wanted pulse from the mask on record 14. The dial
    # gates the mask AT THE SOURCE — CardsDriver.UpdateWantedSlots calls SetWantedSlots(0) when it
    # is off — so a player who turns it off already broadcasts an empty mask and every peer's copy
    # of their board goes dark with theirs. Syncing the recipe on top of the result would be the
    # redundancy this category exists to name.
    ("Cards", "WantedSlotHint"): ("DERIVED", "the wanted-slot MASK it gates rides extension record 14; turning it "
                                             "off makes the owner broadcast a 0 mask, so peers already stop "
                                             "drawing the glow"),
    # RECLASSIFIED FROM PENDING (2026-08-27). Its reason read "HALF covered — the open item fan's
    # anchor rides record 5, but the same dial also nudges the HELD item card, which has no wire
    # path", and the first half of that sentence is not true of the code. The dial is read in
    # EXACTLY ONE place: ItemsPile.Tick sets the fan ROOT's board-local position to
    # BoardAnchorBase + BrowseFanOffset + ItemFanOffset (Cards/Piles/ItemsPile.cs:908), and that
    # root position is precisely what record 5 transmits — ItemsPile.BoardLocalAnchor returns
    # _root.localPosition (ItemsPile.cs:412), NetAvatarDriver samples it into extras.FanAnchorLocal
    # (Net/Avatar/NetAvatarDriver.cs:1837-1843) and PresenceState writes the record
    # (PresenceState.cs:1656). The tuned offset therefore crosses the wire in full.
    #
    # The HELD item card never sees the dial at all. Grabbing a chip reparents it onto the hand's
    # GrabAnchor (Hands/Interact/VRInteractables.cs:246), so the fan root's localPosition stops
    # reaching it the instant it is picked up, and ItemChip.GetHeldPose (ItemsPile.cs:5842) builds
    # the reading pose out of the thumb/index pinch, InspectScale and the [Cards] Held* dials
    # without ever consulting ItemCardOffset. The old parenthesis was right about the second half,
    # and that is what makes the point moot twice over: the held chip is sampled as a WORLD pose
    # (LocalRigSampler.cs:224) and peers draw the slab at that pose (RemoteAvatar.cs:1576), so
    # anything that DID move the held card would already be baked into what travels.
    ("Cards", "ItemCardOffset_{board}"): ("DERIVED", "the only thing it moves is the item fan's ROOT, and record 5 "
                                                     "transmits that root's live board-local position with the offset "
                                                     "already in it; the held item card hangs off the hand's GrabAnchor, "
                                                     "not the fan root, and syncs its own world pose"),

    # ---- COMFORT: the player's own body, input and ears — invisible on their board -------------
    ("Cards", "SpawnLeftOfHead"): ("COMFORT", "where THEIR board first appears relative to THEIR head; the pose is synced"),
    ("Cards", "SpawnSideMeters"): ("COMFORT", "as SpawnLeftOfHead"),
    ("Cards", "SpawnForwardMeters"): ("COMFORT", "as SpawnLeftOfHead"),
    ("Cards", "SpawnDownMeters"): ("COMFORT", "as SpawnLeftOfHead"),
    ("Cards", "RevealMode"): ("COMFORT", "WHEN their fan opens; the open/closed STATE itself is synced"),
    ("Cards", "RevealEnterDegrees"): ("COMFORT", "as RevealMode"),
    ("Cards", "RevealExitDegrees"): ("COMFORT", "as RevealMode"),
    ("Cards", "RevealIgnoreWhenGrabbing"): ("COMFORT", "as RevealMode"),
    ("Cards", "FanFollowSmoothing"): ("COMFORT", "how lazily THEIR fan chases THEIR palm; the mirror follows the synced hand"),
    ("Cards", "FanFollowDeadzone"): ("COMFORT", "as FanFollowSmoothing"),
    # RECLASSIFIED COMFORT -> PENDING (2026-08-27), found while wiring its two neighbours. The old
    # reason -- "driven by THEIR head; the mirror re-derives from the synced head pose" -- describes
    # an INPUT, and the dial does not merely scale an input: CardFan reads it live and rotates the
    # WHOLE FAN ROOT by an eased yaw (`_root.rotation = AngleAxis(biasYaw, up) * baseFacing`), and
    # RemoteHandFan implements none of that. Its own KNOWN GAPS note says so and names the single
    # missing input: whether the SENDER has the toggle on. So an owner who switches it on yaws their
    # fan on their own screen and on nobody else's.
    #
    # PENDING and not wired here, deliberately: the RENDERER does not exist yet, and adding the bit
    # first is the FanCloseDuration trap this file's own notes warn about -- a field whose receiver
    # ignores it turns this checker green while the picture stays wrong, which is the exact failure
    # mode the three entries around it were just fixed for.
    ("Cards", "FanGazeBias"): ("PENDING", "board-affecting and NOT covered: CardFan yaws the whole fan root by an eased "
                                          "gaze bias, and RemoteHandFan implements none of it -- its own KNOWN GAPS note "
                                          "says the only missing input is the one bit saying whether the sender has the "
                                          "toggle on. Unblocked by giving the mirror the yaw FIRST (renderer first, field "
                                          "second); the bit is then one COUNT-range id"),
    # [Cards] FanGazeSmoothing STOOD HERE AS COMFORT ("as FanGazeBias") AND IS NOW WIRED (id 179,
    # 2026-08-27). Worth one line on the way out: it was exempt by ASSOCIATION -- it pointed at a
    # neighbouring entry's reasoning instead of stating its own -- and the neighbour's reasoning
    # ("driven by THEIR head; the mirror re-derives from the synced head pose") is true of the gaze
    # TARGET and says nothing about the ease RATE, which RemoteHandFan held as its own literal at
    # 8/s against a shipped 6/s. An exemption that borrows another dial's argument inherits its
    # blind spots as well as its reasoning.
    # [Cards] FanCurveByFill STOOD HERE AS COMFORT AND IS NOW WIRED (id 237, 2026-08-28). Its reason
    # -- "a local hand-fill heuristic feeding dials that ARE on the wire" -- was the MIRROR IMAGE of
    # what the code does, and the inversion is worth keeping because it is subtle. The dial does not
    # FEED the wired dials; the wired dials (FanCurvePower 135 and FanCurveMinCards 224) cross RAW,
    # and the RECEIVER performs the fill multiply itself, unconditionally. So the gate never crossed
    # at all and an owner who switched it off flattened their own fan and nobody else's. An
    # exemption that says "it feeds something that is covered" has to be checked in the direction of
    # the data flow: here it flowed the other way.
    ("Cards", "HeldForward"): ("COMFORT", "how a card sits in THEIR hand; the held card's POSE is synced"),
    ("Cards", "HeldOffPalm"): ("COMFORT", "as HeldForward"),
    ("Cards", "HeldPinchOffset"): ("COMFORT", "as HeldForward"),
    ("Cards", "HeldFaceBias"): ("COMFORT", "as HeldForward"),
    # [Cards] InspectScale STOOD HERE AS COMFORT AND IS NOW WIRED (id 178, 2026-08-27). ITS REASON
    # WAS NOT STALE, IT WAS FALSE, and stating the difference is the point of keeping this line.
    # It read "the held slab is drawn at the synced card width" -- an assertion about a MECHANISM,
    # checkable against the code, and the opposite of what the code did: RemoteAvatar built the slab
    # at RemoteHandFan.DefaultCardWidth, a bare literal, and never read the synced width at all. So
    # a peer's inspected card was wrong TWICE (no magnification, and no tuned width) while this
    # table said it was fine. An exemption that describes a mechanism can be checked against the
    # mechanism -- and this one never was.
    ("Cards", "CardSoundsEnabled"): ("COMFORT", "master switch over the sounds THEY hear; every client plays its own"),
    ("Cards", "FanRevealSound"): ("COMFORT", "a sound THEY hear; every client plays its own"),
    ("Cards", "FanHideSound"): ("COMFORT", "as FanRevealSound"),
    ("Cards", "CardGrabSound"): ("COMFORT", "as FanRevealSound"),
    ("Cards", "CardPlaceSound"): ("COMFORT", "as FanRevealSound"),
    ("Cards", "CardTakeBackSound"): ("COMFORT", "as FanRevealSound"),
    ("Cards", "FaceMipBake"): ("COMFORT", "texture quality of THEIR rendering; no geometry, no layout"),
    ("Cards", "DevFakeHand"): ("COMFORT", "a debug toggle; never on a shipped player's board"),

    # ---- NO-OP: nothing renders from it, even locally ------------------------------------------
    ("Cards", "FanArcDegrees"): ("NO-OP", "LEGACY — superseded by FanArcSweepDegrees, read by nothing"),
    ("Cards", "HeldTiltDegrees"): ("NO-OP", "LEGACY — read by nothing"),
    ("Cards", "TrayTilt"): ("NO-OP", "LEGACY — superseded by BoardTilt_{board}"),
    ("Cards", "BoardPitchMinDegrees"): ("NO-OP", "LEGACY — superseded by BoardPitchMin_{board}"),
    ("Cards", "BoardPitchMaxDegrees"): ("NO-OP", "LEGACY — superseded by BoardPitchMax_{board}"),
    ("Cards", "RoundButtonDiameter"): ("NO-OP", "LEGACY — superseded by [RoundButtons] CapSize"),
    ("Cards", "RoundButtonThickness"): ("NO-OP", "LEGACY — superseded by [RoundButtons] Depth"),
    ("Cards", "RestButtonInsetX"): ("NO-OP", "LEGACY — superseded by RestButtonSpacing_{board}"),
    ("Cards", "ConfirmUndoInsetX"): ("NO-OP", "LEGACY — superseded by GenericButtonSpacing_{board}"),
    ("Cards", "BoardScaleDefault04Applied"): ("NO-OP", "a one-shot migration marker; must start false on a fresh install"),
    ("Cards", "DecisionOffsetYRebased"): ("NO-OP", "a one-shot migration marker"),
    # ("Cards", "ClusterOffset_{board}") STOOD HERE as a NO-OP and is gone (ModBuild 97). The line
    # read "ButtonCluster.AttachDocked reads the mount's ROTATION and SCALE only — the position
    # never moves the rendered cluster, locally or remotely". Every word of that was true, and it
    # was a BUG REPORT written as an exemption: the user's "Die Offsets bei den Überspringen-Tasten
    # haben keinen Einfluss" is that sentence seen from inside the headset. NO-OP is for dials with
    # no rendered effect BY DESIGN (legacy keys, migration markers) — not for a live debug-menu
    # stepper that lost its consumer in a refactor. The consumer is back on both ends, so the dial
    # rides id 16 and needs no exemption.

    # ---- PENDING: real gaps, parked with the reason and what unblocks them ---------------------
    # These are DEBTS. The count is printed on every run so it cannot creep upward unnoticed.
    # RestButtonShape_{board} / GenericButtonShape_{board} STOOD HERE and are gone (2026-08-09).
    # The line read "an enum; RemoteBoardFurniture builds rest caps ROUND with no Square branch at
    # all, so the wire field needs a RENDERER change first". That is the FanCloseDuration rule
    # applied correctly, and the way to retire it was to grow the branch rather than to sample the
    # dial anyway: RemoteBoardFurniture.RestCap / GenericCap now dispatch on the owner's shape, so
    # ids 231/232 carry it and [RestButtons] Width/Height (parked behind the same branch) ride too.
    # [Cards] SlotCardInset STOOD HERE AND IS PAID (2026-08-27, ModBuild 309). Its reason had
    # already been sharpened once, from "NOTHING in Net/ reads it, so a wire field would have no
    # consumer until the recess renderer grows one" to the truth: the mirror DID seat cards in the
    # real prefab recess all along, at a private literal CardOnAnchorProudZ = -0.003f, while the
    # owner seats at -SlotCardInset through the slot's own 1.3x SlotScale. THAT IS THE PART WORTH
    # KEEPING once the entry goes. "No consumer" was measured on the IDENTIFIER -- no file under
    # Net/ contained the string SlotCardInset -- and a frozen literal is a consumer that no grep
    # for a dial name can find. The divergence it hid was 5.2 mm against 3 mm AT THE SHIPPED
    # DEFAULT, so it was never a gap only a tuned player could see; it was wrong for everyone,
    # which is exactly the class of defect a coverage table is meant to surface and this one could
    # not.
    #
    # PAID AS ID 101 -- BUT NOT BY THE PLAN THE RETIRED LINE WROTE DOWN, and the difference is the
    # second thing worth keeping. That plan said "restore the z term in SlotOverlayLocal, re-basing
    # the two glow call sites". It was wrong and it would have shipped a regression, which the lane
    # that built this caught by reading the OWNER's side instead of trusting the note:
    # SlotOverlayLocal has TWO consumer families mirroring two DIFFERENT owner-side z bases. A card
    # seats at -SlotCardInset + ov.z; the owner's glows sit at PlayTray.SlotGlowBaseZ /
    # WantedGlowBaseZ + ov.z (-0.006 / -0.004, code literals and not a dial at all). Folding the
    # seat depth into the shared expression would have dragged both glows onto the card's plane and
    # cost the gold-in-front-of-teal ordering those two constants exist to give. The depth went into
    # its own accessor instead (RemoteControlBoard.SlotCardSeatLocal) and SlotOverlayLocal keeps
    # z = 0 by contract. The plan also misnamed the constant: the seat was CardOnAnchorProudZ, not
    # ProudZ -- two different surfaces whose values merely coincide, which is exactly the confusion
    # the retired line warned about and then committed.
    #
    # ONE MORE THING THE OLD LINE GOT WRONG, worth knowing before someone reads this as a no-op:
    # a peer that predates id 101 does NOT keep rendering what it renders today. Every mirrored
    # card moves 2.2 mm deeper at the SHIPPED defaults, for every pairing, tuned or not. That is
    # the defect being fixed, but it is a visible change and not a silent one.
    # ("Cards", "PileViewer") / ("Cards", "ActivePile") are GONE (user ruling 2026-08-11): the
    # dials were removed outright — both features are unconditional now, so there is no config
    # entry left to exempt (their PENDING lines retired with them).
    # [Cards] GameCardParticles STOOD HERE AND IS PAID (2026-08-27, ModBuild 309). The debt was
    # real; the BLOCKER it named was false, and that is the half worth carrying forward. It read
    # "a peer's mirrored cards are mod slabs with no game particle system to switch on", which
    # pictures the dial as a component sitting on a card. It never was one: the plume is a PREFAB,
    # GlobalSettings.Instance.VisualEffects.CardSmoke, a public field on a Resources-loaded
    # singleton every client has, reachable with no scene object, no local player and no card on
    # screen. THIS IS THE THIRD STANDING "cannot be done" NOTE IN SIX BUILDS TO FALL THE MOMENT
    # SOMEBODY READ THE DECOMPILED SOURCE (after the rest-cap Square branch and the fan collapse).
    # A blocker written from memory of an API is a hypothesis, and it ages worse than the code it
    # describes. Paid as id 236 plus the receiver-side FX (Net/Remote/RemoteCardPlume.cs).
    #
    # AND THE CORRECTED LINE HAD A FALSEHOOD OF ITS OWN, which is the reason this paragraph is
    # longer than the entry it replaces. When this debt was re-argued it claimed "BurnCardFx
    # already instantiates locally" and cited it as the ready-made template. It does not, and has
    # not since commit 71883140: the method that DID spawn the prefab, SpawnConsumedPlume, was
    # REMOVED for shipping the field-covering fog, and today's BurnCardFx only BINDS the game's own
    # already-spawned instance and reparents it. The difference is the whole engineering problem. A
    # reparent shrinks the entire child hierarchy for free by leaving the screen-card scale behind;
    # a spawn path has no scale to leave behind and must clamp EVERY emitter (the removed attempt
    # used GetComponentInChildren and left the others at world scale), cap start lifetime, and pin
    # the root's world scale. So: the debt's blocker was false, AND the argument that retired it
    # cited a template that had been deleted. Both errors were mine, and both were caught by
    # reading the actual file rather than the note about it -- which is the same lesson twice.
    # THE KEYCAP SIZE / SEAT / TRAVEL FAMILY IS GONE FROM THIS TABLE (2026-08-09). Eighteen lines
    # stood here saying "mirrored as a FROZEN constant in RemoteBoardFurniture, so a re-tune
    # desyncs; the renderer is owned by a parallel round — wire it when that lands". It landed, and
    # the debt was paid rather than restated: record 28 ids 81..98 carry [RoundButtons] OffsetX/Y/Z
    # + CapSize/Width/Height/Depth/Travel, [BoardButtons] W/H/D/Travel, [BoardDashboard]
    # PinWidth/Height/Depth/Travel and [RestButtons] Depth/Travel, and id 228 carries the turn-flow
    # cap's SHAPE. What is left below is what genuinely still has no receiver.
    # [RestButtons] Width / Height STOOD HERE TOO, parked behind the rest cap's missing Square
    # branch with "wire it with RestButtonShape_{board}, not before". They were wired WITH it
    # (ids 99..100), which is what that line asked for.
}

# THE [ButtonColors] BLANKET EXEMPTION IS GONE (2026-08-09), and how it fell is worth keeping.
# It read: "the remote furniture draws an AUTHORED palette and never read these even for the DEFAULT
# case, which makes closing this a design decision about what a mirrored board looks like, not a
# wire gap" — one rule standing in for 21 identical PENDING lines. The user overruled it verbatim:
#
#     "Bitte implementier auch die Farben und Formen der Knöpfe, dass sie über die Leitung gehen -
#      so dass das remote Board 1:1 das anzeigt was der Spieler sieht"
#
# And the premise was itself the bug. "The mirror never read them, even at their defaults" is not
# evidence that a dial family is local — it is a SECOND defect stacked on the first, and it was:
# the shipped cap-face tint is 0.5 grey and the local caps are painted through it, so every
# mirrored keycap was drawn at twice its owner's brightness for two players who had never touched
# a slider. All 21 entries are on the wire now (ids 48..53 for the six colours, 170 for the outline
# width, 229..230 for the two switches) and the mirror reads them, so there is nothing left for a
# blanket rule to cover. A NEW [ButtonColors] dial will now fail this script by default, which is
# the behaviour every other board section already has and the reason not to leave the rule behind
# as a catch-all.

DEFAULTS_DIR = SRC / "Defaults"
NET_PROTOCOL = SRC / "Net" / "NetProtocol.cs"
SAMPLER = SRC / "Net" / "Board" / "BoardTuning.cs"

ANNOTATION_RE = re.compile(r"//\s*=>\s*\[(?P<section>[^\]]+)\]\s+(?P<key>\S+)")
TUNE_ID_RE = re.compile(r"public const byte (?P<name>Tune\w+)\s*=\s*(?P<id>\d+)\s*;")
DOC_KEY_RE = re.compile(r"\[(?P<section>[A-Za-z]+)\]\s+(?P<key>[A-Za-z0-9_{}]+)")

BOARD_SUFFIXES = ("_Oak", "_Steel", "_Bronze")

# A COLOUR field id covers THREE config keys, exactly as a `_{board}` id covers three boards, and
# for the same reason: one wire field, several entries behind it. Record 28's colour range carries
# an RGB triple in one 3-byte field (NetProtocol.TuneColorIdMin), so its doc comment names
# `Label{rgb}` and this expands that to LabelR/LabelG/LabelB. Without it the six colour ids would
# each report as an ORPHAN naming a key no shipped default has — which is precisely what a wrong doc
# comment looks like, so the expansion has to be explicit rather than a substring match.
CHANNEL_SUFFIXES = ("R", "G", "B")


def annotated_defaults():
    """(section, key) for every shipped default carrying a `// => [Section] Key` comment."""
    out = set()
    for path in sorted(DEFAULTS_DIR.glob("Defaults.*.cs")):
        for line in path.read_text(encoding="utf-8").splitlines():
            m = ANNOTATION_RE.search(line)
            if m:
                out.add((m.group("section"), m.group("key")))
    return out


def wire_fields():
    """Tune* id name -> (section, key) taken from the id's OWN doc comment.

    The doc comment is the single source of truth for what a field id means; it is what a human
    reads when adding one, so making the guard read the same line is what keeps the two honest.
    """
    text = NET_PROTOCOL.read_text(encoding="utf-8")
    lines = text.splitlines()
    out = {}
    for i, line in enumerate(lines):
        m = TUNE_ID_RE.search(line)
        if not m:
            continue
        # Walk back over the doc block above the declaration and take the first [Section] Key in it.
        for j in range(i - 1, max(-1, i - 12), -1):
            prev = lines[j]
            if not prev.strip().startswith("///"):
                if prev.strip().startswith("//") or not prev.strip():
                    continue
                break
            d = DOC_KEY_RE.search(prev)
            if d:
                out[m.group("name")] = (d.group("section"), d.group("key"))
                break
    return out


def sampled_ids():
    """The Tune* ids BoardTuningSampler.Sample actually writes. A declared-but-unwritten id is a
    reservation, not coverage — which is the distinction that makes reserving ids for a parallel
    round safe."""
    text = SAMPLER.read_text(encoding="utf-8")
    start = text.find("internal static int Sample(")
    if start < 0:
        return set()
    body = text[start:]
    return set(re.findall(r"NetProtocol\.(Tune\w+)", body))


def expand(section, key):
    """A `Key_{board}` entry stands for the per-board family; a `Key{rgb}` entry for the three
    channels of one colour (see CHANNEL_SUFFIXES)."""
    if key.endswith("_{board}"):
        stem = key[: -len("_{board}")]
        return {(section, stem + s) for s in BOARD_SUFFIXES}
    if key.endswith("{rgb}"):
        stem = key[: -len("{rgb}")]
        return {(section, stem + s) for s in CHANNEL_SUFFIXES}
    return {(section, key)}


def main():
    defaults = annotated_defaults()
    fields = wire_fields()
    written = sampled_ids()

    covered = set()
    for name, (section, key) in fields.items():
        if name in written:
            covered |= expand(section, key)

    exempt = {}
    for (section, key), reason in EXEMPT.items():
        for pair in expand(section, key):
            exempt[pair] = reason

    board = {(s, k) for (s, k) in defaults if s in BOARD_SECTIONS}

    missing = []
    pending = []
    for pair in sorted(board):
        if pair in covered:
            continue
        reason = exempt.get(pair)
        if reason is None:
            missing.append(pair)
        elif reason[0] == "PENDING":
            pending.append((pair, reason[1]))

    # A stale EXEMPT line is its own defect: it means the dial was wired (or deleted) and the
    # exemption is now a lie somebody will read as policy. Report it — an exemption that no longer
    # applies is exactly how a table like this rots.
    stale = [p for p in exempt if p not in board or p in covered]

    # And a declared wire id whose doc comment names a [Section] Key that no shipped default has:
    # either the id's doc is wrong or the dial was renamed out from under it.
    orphan = sorted(
        f"{name} -> [{s}] {k}"
        for name, (s, k) in fields.items()
        if name in written and not (expand(s, k) & defaults))

    bad = []
    for section, key in missing:
        bad.append(f"[{section}] {key}: board-affecting, but no record-28 field writes it and no "
                   f"EXEMPT line explains why. Wire it (a Tune* id + one line in "
                   f"BoardTuningSampler.Sample + one member on RemoteBoardTuning), or add an "
                   f"EXEMPT entry saying which of COMFORT / DERIVED / NO-OP / PENDING it is.")
    for pair in sorted(stale):
        bad.append(f"[{pair[0]}] {pair[1]}: EXEMPT names a dial that is now covered by the wire or "
                   f"no longer exists — drop the line rather than leave a stale exemption standing.")
    for line in orphan:
        bad.append(f"{line}: a WRITTEN record-28 field names a [Section] Key with no annotated "
                   f"shipped default. Fix the id's doc comment, or the dial moved and the field "
                   f"is now sampling something else.")

    if bad:
        print("error: the 1:1 board guarantee has holes:", file=sys.stderr)
        for b in bad:
            print("  " + b, file=sys.stderr)
        print("\n  The ruling (2026-08-09): \"Ändert ein Spieler also die Positionen für sich "
              "selber,\n  so sollen alle anderen diese Position bei seinem board auch sehen.\"",
              file=sys.stderr)
        return 1

    print(f"wire coverage: {len(covered & board)} board dial(s) on extension record 28, "
          f"{len(board) - len(covered & board)} exempt "
          f"({len(pending)} of them PENDING debts), {len(board)} board dials in "
          f"{len(BOARD_SECTIONS)} sections.")
    if pending:
        print("  PENDING (board-affecting, not yet on the wire — these are debts, not decisions):")
        for (section, key), why in pending:
            print(f"    [{section}] {key}: {why}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
