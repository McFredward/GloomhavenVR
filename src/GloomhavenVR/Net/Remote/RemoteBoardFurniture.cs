using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The INTERACTIVE FURNITURE of a peer's control board, reproduced on their remote board as
/// PURELY INERT VISUALS.
///
/// WHY THIS EXISTS AT ALL. An earlier pass rendered only the informational surfaces (objectives,
/// elements, round, initiative badge, rest badge, active cards, pile counters) and DELIBERATELY
/// left the furniture out, on the argument that "a button you cannot press is not information".
/// The user rejected that outright ("Das will ich NICHT, ALLES von den aufgeführten Elementen soll
/// dargestellt werden. Aber wichtig: Nichts davon soll man interagieren können auf dem fremden
/// Board, es ist eine reine Darstellung."). A peer's board must LOOK like a control board — the
/// keycaps, the rest discs, the pin toggle, the handle bar, the turn-flow cap, the item-use recess
/// and the decision drawer are what makes it read as one — while remaining completely untouchable.
///
/// THE CAPS ARE ANIMATED, not just drawn (2026-08-08 ruling: "alle Interaktionen, ANIMATIONEN und
/// Anzeigen des Controllboards … so wie der Spieler sie sieht"). A local keycap crumbles into a dust
/// burst when it is taken away, assembles back out of that dust when it returns, dips its full
/// authored travel on a press and springs back, and switches between four state colours. Every one
/// of those used to be a POP or a fixed colour on a peer's board. They are reproduced here by
/// <see cref="RemoteCapFx"/> — and three of the four cost NOTHING on the wire, because the
/// transitions they animate (show/hide, state) were already synced by the board-UI record; only the
/// PRESS is an event with no state behind it, and it rides five reserved bits of a record that
/// already exists. An ANIMATION IS NOT INTERACTIVITY: the animator writes a transform, a scale and
/// material colours, and the inertness guarantee below is unchanged and unweakened.
///
/// SINCE THE 3D-PARITY PASS ("komisch 2D" rejection) the caps are REAL 3D GEOMETRY, built from the
/// very meshes the local board's <c>PlayTray.BoardButton</c> uses — the beveled keycap
/// (<c>CardMesh.BuildBeveledKeycap</c>: state-coloured plateau, bright chamfer ring, dark warm
/// walls) and the smooth round disc (<c>CardMesh.GetRoundCap</c>) — skinned through the SAME
/// <c>PlayTray.NewKeycapMaterial</c> (BoardLit + carved-grain texture) and labelled with the same
/// engraved parchment type (<c>NativeButtonSkin.StyleEngravedLabel</c>). What is NOT reproduced is
/// everything that made the local widgets buttons: no collider, no press travel, no
/// <c>PokeableBehaviour</c>, no registration anywhere.
///
/// AND SINCE THE COLOURS-AND-SHAPES PASS (2026-08-09 — "Bitte implementier auch die Farben und
/// Formen der Knöpfe, dass sie über die Leitung gehen - so dass das remote Board 1:1 das anzeigt was
/// der Spieler sieht"), the caps are also PAINTED and SHAPED like the owner's. Two separate things
/// had to land for that, and the order matters: FIRST this renderer had to read the [ButtonColors]
/// vocabulary AT ALL — it read none of it, so a mirrored cap was drawn at the raw state palette
/// while every local cap is drawn at palette × a 0.5 face TINT, i.e. at TWICE its owner's brightness
/// for two players who had never touched a slider, and its label was left at TMP's default white
/// where the owner's is warm parchment — and only THEN could the owner's own values ride record 28
/// on top (ids 48..53 / 170 / 229..230). The same order applied to the SHAPES: the mirrored rest
/// pair had no Square branch and the Confirm/Undo column no Round one, so their dials were a stated
/// PENDING debt rather than an un-sampled field, and the branches (<see cref="RestCap"/> /
/// <see cref="GenericCap"/>) had to exist before ids 231/232 could honestly claim to cover them.
///
/// SEATING. When the REAL tray asset is up (<see cref="RemoteTrayVisual"/>), the caps sit on the
/// prefab's own anchors (the button seats <c>ButtonSeat1/2</c> — legacy spelling
/// <c>ConfirmButton/UndoButton</c>, both resolve through <c>Cards.BoardAnchors</c> — plus
/// <c>ShortRestToken/LongRestToken</c>) plus the
/// OWNER's own per-board offsets, resolved through <see cref="RemoteBoardTuning"/>: the value they
/// set where they have moved a dial (extension record 28) and the authored
/// <c>Defaults.ConfirmUndoOffset_*</c> / <c>RestButtonOffset_*</c> / … for their synced style where
/// they have not — which is the same number, so an untuned peer's board is unchanged. The
/// DELIBERATELY-NOT note that stood here ("their re-tuning stays off the wire; every client renders
/// a given style at its shipped layout") was retired with record 28: under the 1:1 ruling a cap the
/// owner has moved belongs where they moved it on every screen. On the flat fallback board (bundle
/// absent) the caps keep the legacy Oak board-local constants.
///
/// NON-INTERACTIVE IS A HARD REQUIREMENT, and it is enforced three ways, not one:
///   1. CONSTRUCTION — nothing here ever creates a <c>Collider</c>, a <c>Rigidbody</c> or a
///      <c>GrabbableBehaviour</c>; primitives have their colliders destroyed at creation.
///   2. REGISTRATION — this file never calls <c>PlayTray.RegisterLaserTarget</c>, never implements
///      <c>IPokeable</c>/<c>IGrabbable</c>, never touches <c>VRInteractables</c>,
///      <c>UguiPokeSurfaces</c> or <c>LaserTargets</c>. It has no click callbacks at all.
///   3. BELT AND BRACES — <see cref="StripColliders"/> walks the finished hierarchy and destroys
///      anything that still carries a collider, logging a warning if it ever finds one.
///
/// STATE FIDELITY. Since the 1:1-parity round (mod build 2) the owner broadcasts their live
/// BOARD-UI STATE (extras extension record 4, ~4 B at 5 Hz + on-change): which controls their
/// board currently shows (confirm/undo/use recess + cap/rest discs/skip/decision drawer) and the
/// wanted-slot glow mask. When that record is present it is AUTHORITATIVE — this board shows
/// exactly the controls the owner sees, in the same frames. A sender that predates the record
/// falls back to the previous behaviour (everything drawn, states derived from the
/// host-replicated <c>CPlayerActor</c> model and the already-synced VR extras).
///
/// THE DECISION DISPLAY IS SYNCED WHOLE (user ruling 2026-08-08): its button wordings (record 12),
/// each option's OFFERED / GREYED / CHOSEN state (record 23) and the prompt LINE above them are all
/// mirrored — the line composed on this machine from the record's variant id, because the owner's
/// own sentence can embed active-bonus card names (see <see cref="RemoteDecisionPrompt"/>). And it
/// disappears whole: while the owner has focused another character their board shows nothing at
/// that seat, so the records stop riding and this copy empties with it. The "LOCAL-ONLY neutral
/// look" list this paragraph used to carry — button enabled-states, live Confirm wordings, the
/// follow/pin preference — is empty now; every entry on it became a synced field.
///
/// The modal PICK FIELD the flat board used to draw is
/// GONE — the local board removed its pick field outright, so a copy of it had become a picture
/// of a widget that no longer exists.
///
/// ANTI-CHEAT is unchanged: nothing here reads a card identity, and the pieces that DO depend on the
/// peer's card state (the wanted-slot pulse and the snap-glow hover rim) are slot POSITIONS the
/// remote board already draws — see their notes. The HALF-CARD DIVIDER that used to be the third
/// such piece is gone: it drew a hairline across every face-up round card, standing in for poke
/// zones the owner's own board deliberately never draws, so under the 1:1 rule it was a widget peers
/// saw and the owner did not. Deleted, not gated — there is no owner-side state to gate it by.
///
/// COST. Built once, torn down with the board root, and refreshed on the shared
/// <see cref="RemoteBoardContent.RefreshSeconds"/> (4 Hz) cadence with change-gated writes. The
/// per-frame work is the single <see cref="RemoteGlowPulse"/> component (only while a pulse is
/// visible) plus one <see cref="RemoteCapFx"/> per cap, which early-returns on the first line
/// unless that cap is mid-press or mid-dissolve — an idle board does no per-frame work at all, and
/// nothing here logs per frame.
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (PER-ACTOR MODEL + VR-ONLY-derived + one small record of its
/// own). The old "NEUTRAL LOOKS" / "LOCAL-ONLY STATE" reading of this file — button
/// interactability, the Confirm wording, the drawer's contents being
/// "knowable-but-not-worth-a-field" — is GONE, one member at a time and finally as a rule: the
/// FOLLOW/PIN toggle left it through the board-UI record's byte 1 bit 2, the cap wordings through
/// record 13, the decision buttons through records 12 and 29 (since ModBuild 105 the take-damage
/// row is a CLONE OF THE GAME'S OWN WIDGETS, not a reproduction — see RemoteDecisionWidgets; the
/// plates below stand in for the prompts whose widgets a peer does not own), and their
/// OFFERED / GREYED / CHOSEN states
/// plus the prompt line through record 23 and this round's ruling ("alle Interaktionen,
/// Animationen und Anzeigen des Controllboards … so wie der Spieler sie sieht" — see the class
/// definition in <see cref="RemoteBoardContent"/>). What is left of DELIBERATELY-NOT here is the
/// safety half alone: no card identity, ever. Personal TUNING offsets are not that class either —
/// every client renders a given board style at its shipped layout, which is a rendering
/// convention, not a hidden display. The cap STATES and the snap-glow HOVER left the list this
/// round, through the board-UI record's byte 2 and its byte 1 bits 6..7; the cap PRESS left it
/// through record 14's reserved byte-0 bits; and the UNDO / item-USE wordings through record 13's
/// mask bits 2..3. Slot occupancy and the pile stacks are PER-ACTOR MODEL; the wanted-slot pulse is
/// DERIVED from state the board already draws. The wire inputs are the already-synced
/// <see cref="RemoteAvatar"/> passed to <c>Refresh</c> and the board STYLE the ctor keys the
/// authored layout from — neither costs a new byte. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteBoardFurniture
{
    // ---------------------------------------------------------------- layout (board-local) --
    // FALLBACK-board constants: the LOCAL Oak board's authored BASE offsets, copied from PlayTray
    // so a peer's furniture sits where that player's own furniture sits when no real tray asset
    // (and therefore no prefab anchor) is available. With the real asset up, the caps seat on the
    // prefab anchors + the AUTHORED per-style offsets instead (see StyleOffsets below).

    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    /// <summary>PlayTray.ButtonZoneX — the right-hand control column.</summary>
    private const float ButtonZoneX = 0.235f;

    /// <summary>PlayTray "ContinueMount" (0.235, 0.045, −0.006) — the CONFIRM keycap / native
    /// Continue dock (fallback board).</summary>
    private static readonly Vector3 ConfirmMount = SeatMount(0);

    /// <summary>The UNDO keycap's fallback seat — the middle of the three.</summary>
    private static readonly Vector3 UndoMount = SeatMount(1);

    /// <summary>The turn-flow SKIP keycap's fallback seat — the bottom of the three. It exists at
    /// all only since the skip joined the generic cluster (2026-08-25); before that the mirror drew
    /// that cap from the retired [RoundButtons] column solve instead.</summary>
    private static readonly Vector3 SkipMount = SeatMount(2);

    /// <summary>
    /// The procedural fallback anchor of button seat <paramref name="seat"/> — VERBATIM the
    /// expression <c>PlayTray.BuildButtons</c> synthesises when a board supplies no anchors of its
    /// own: <c>ButtonZoneX</c>, evenly spaced by <see cref="Defaults.StackPitchFallback"/> about the
    /// midpoint the old hardcoded Confirm/Undo pair straddled (−0.0075), a hair proud of the face.
    ///
    /// <para>It has to be the same expression on both sides and not merely the same NUMBERS,
    /// because this is the one board where nothing is measured: the owner and the peer are each
    /// inventing an anchor, and the only thing that makes them invent the same one is that they
    /// compute it the same way. (On every bundled board this is unreachable — the anchors are
    /// authored and both sides resolve them through <c>Cards.BoardAnchors</c>.)</para>
    /// </summary>
    private static Vector3 SeatMount(int seat) => new(
        ButtonZoneX,
        -0.0075f + Defaults.StackPitchFallback * ((Cards.BoardAnchors.ButtonSeatCount - 1) * 0.5f - seat),
        -0.006f);

    // The VR-settings gear cap is gone from both boards: the mod's settings live in the game's own
    // options window now, so there is no local button for a remote board to mirror.

    /// <summary>PlayTray.PinBase (BoardW/2 − 0.045, −BoardH/2 − 0.030, −FixedProudZ) — FOLLOW/PIN.
    /// The local board uses this same board-local base for EVERY board style (plus the per-style
    /// PinOffset default, applied below).</summary>
    private static readonly Vector3 PinMount = new(BoardW * 0.5f - 0.045f, -BoardH * 0.5f - 0.030f, -0.005f);

    /// <summary>PlayTray.BuildHandle's bar (0, −BoardH/2 − 0.030, +0.004) — board-local on every
    /// style, like the local board's own handle. NOTE the local handle also carries a 62 %-wide
    /// trigger BoxCollider — the remote copy is the BAR ONLY, no zone, no <c>PanelGrabHandle</c>.</summary>
    private static readonly Vector3 HandleMount = new(0f, -BoardH * 0.5f - 0.030f, 0.004f);

    // THE WHOLE TURN-FLOW CLUSTER MIRROR IS GONE FROM THIS FILE (2026-08-25). Five constants stood
    // here — the column anchor, the 0.7 dock shrink, the proud lift and the [RoundButtons] group
    // offset — and their only job was to reproduce, term for term, where WorldUI/ButtonCluster drew
    // the SKIP cap on the owner's board. The user retired that group ("Ich möchte daher, dass die
    // Button-Gruppe der 'Überspringen Buttons' komplett verschwindet"). The skip cap is a generic
    // board keycap on the board's own ButtonSeat3 recess now, so this file mirrors it the way it
    // already mirrors Confirm and Undo: same anchor table, same clamp, same [BoardButtons] size —
    // which is both less code and a stronger 1:1 guarantee, because the three caps can no longer be
    // solved differently from one another.

    /// <summary>PlayTray.ItemUseSlotBase (ButtonZoneX, −BoardH/2 − 0.095, −0.020).</summary>
    private static readonly Vector3 ItemUseMount = new(ButtonZoneX, -BoardH * 0.5f - 0.095f, -0.020f);

    /// <summary>The shared decision drawer's mount seat, read STRAIGHT from the local board
    /// (<see cref="Cards.PlayTray.DecisionMountBase"/>) rather than hand-copied — the
    /// <see cref="BarClearanceMeters"/> precedent below. Its Y absorbed the 157 mm that used to
    /// arrive through <c>[Cards] DecisionOffset_*.y</c> when that dial went live (ModBuild 90), and
    /// a hand-copied −0.29 here would have parked every peer's drawer 157 mm above the owner's.</summary>
    private static readonly Vector3 DecisionMount = Cards.PlayTray.DecisionMountBase;

    /// <summary>Half height of the local grab-bar's trigger zone (<c>PlayTray.BuildHandle</c>
    /// box.size.y 0.05 / 2) — the bar-bottom reference the local decision dock hangs its widget
    /// block from.</summary>
    private const float HandleZoneHalfY = 0.025f;

    /// <summary>The clearance between the grab-bar bottom and the prompt reference the owner's
    /// dock anchors under — read STRAIGHT from the local dock
    /// (<see cref="WorldUI.Surfaces.DecisionDockSurface.BarClearanceMeters"/>) since the 1:1
    /// mirror round, so it can no longer drift from the original it copies.</summary>
    private const float BarClearanceMeters = WorldUI.Surfaces.DecisionDockSurface.BarClearanceMeters;

    // ---- KEYCAP GEOMETRY: wire-overridable fallbacks (extension record 28, ids 81..98 + 228) ----
    //
    // THESE USED TO BE `const`, AND THE COMMENT ABOVE THEM USED TO SAY WHY: "the live ButtonTuning
    // entries are the LOCAL player's own config; a peer's caps are drawn at the AUTHORED defaults so
    // every remote board looks the same regardless of local tuning." Half of that sentence was
    // always right and still is — a peer's board must never be drawn from the VIEWER's config — but
    // the conclusion it reached was wrong under the 1:1 ruling (2026-08-09, "Ändert ein Spieler also
    // die Positionen für sich selber, so sollen alle anderen diese Position bei seinem board auch
    // sehen"): the OWNER's own cap sizes and press travels are exactly what a mirrored board owes
    // them, and freezing them meant a player who re-shaped their keycaps was the only person alive
    // who could see it. scripts/check-wire-coverage.py carried all of them as one PENDING debt whose
    // reason read "the renderer is owned by a parallel round — wire it when that lands". It landed.
    //
    // So each of these is now the fallback INITIALISER of a field the constructor overwrites from
    // RemoteBoardTuning — and since RemoteBoardTuning's own fallback for an absent field is the same
    // Defaults entry, an untuned peer is drawn byte-for-byte as they were before this change. The
    // initialiser is still what scripts/check-remote-defaults.py pins, for the reason its header
    // gives: move a default without moving its copy and two untuned players see two different
    // boards, and neither of them can tell from inside their own headset.
    private readonly float _boardCapW = Defaults.BoardButtons_Width;
    private readonly float _boardCapH = Defaults.BoardButtons_Height;
    private readonly float _boardCapD = Defaults.BoardButtons_Depth;
    private readonly float _pinCapW = Defaults.PinWidth;
    private readonly float _dashCapH = Defaults.BoardDashboard_Height;
    private readonly float _dashCapD = Defaults.BoardDashboard_Depth;
    private readonly float _restCapD = Defaults.RestButtons_Depth;
    private readonly float _restCapW = Defaults.RestButtons_Width;
    private readonly float _restCapH = Defaults.RestButtons_Height;

    /// <summary>
    /// THE PEER'S OWN BOARD STYLE — which of the three keycap atlases their caps are cut from, and
    /// which board's rest motifs those caps echo (<c>Cards.CapSymbols</c>).
    ///
    /// <para>It is NOT a new wire field. The style already rides record 28 (<c>RemoteBoardTuning
    /// .Style</c>) because a peer's board PREFAB is chosen from it, and this peer clones the same
    /// prefab out of the same bundle — so resolving the atlas from it is the same derivation the
    /// owner's own client makes, from the same inputs. A bronze player is seen with bronze keys
    /// carrying the bronze board's own plaited crescent, and nothing had to be sent to say so.</para>
    /// </summary>
    private readonly Cards.ControlBoard _style;

    // ---- CAP SHAPES: which of the two meshes each family is built from (record 28, ids 228/231/232)
    // Two of these three were HARDWIRED, not frozen — the mirror had no Square branch for the rest
    // pair and no Round branch for the Confirm/Undo column at all, which is why their wire fields
    // were a stated PENDING debt rather than a missing sampler line (a shape byte no renderer can
    // act on is worse than no shape byte: it reports "covered" while the peer sees the wrong cap).
    // Both branches exist below now, so both dials ride.
    //
    // The [0] on the initialisers is a COMPILE-TIME SEED ONLY and is never what a board is drawn
    // with: the constructor overwrites both from RemoteBoardTuning, which resolved them against the
    // PEER's synced style. It is written as index 0 rather than as a bare literal so that a future
    // per-board shape default cannot be introduced without this line pointing at the same table the
    // resolver reads. (All three styles ship the same member today — Round rests, Square generics.)
    private readonly Cards.ButtonShape _restShape = Defaults.RestButtonShape_ByBoard[0];
    private readonly Cards.ButtonShape _genericShape = Defaults.GenericButtonShape_ByBoard[0];

    // ---- THE [ButtonColors] FAMILY: wire-overridable fallbacks (record 28, ids 48..53 / 170 / 229..230)
    //
    // THE DEFECT THESE CLOSE IS NOT "the owner's tuning does not arrive". It is that this renderer
    // read NONE of the twenty-one [ButtonColors] entries — not even at their shipped values — while
    // every LOCAL cap is painted through them. The shipped cap-face tint is 0.5 grey and
    // PlayTray.BoardButton.StateColor multiplies the state palette by it, so a mirrored keycap was
    // drawn at TWICE the brightness of the very cap it is a copy of, for two players who had never
    // opened the debug menu. An earlier audit called this family "a look decision, not a wire gap"
    // on the ground that the mirror never read it; the user overruled that ("so dass das remote
    // Board 1:1 das anzeigt was der Spieler sieht", 2026-08-09), and reading the family is the half
    // that had to land FIRST — two untuned players must be identical BY CONSTRUCTION, not because
    // nobody has touched a slider yet.
    //
    // Held as individual FLOAT channels rather than as Color values on purpose: that is the form
    // scripts/check-remote-defaults.py can pin against the shipped entry, and pinning them is the
    // whole point — a default that moved without its copy would put two untuned players in front of
    // two differently-coloured boards, which is exactly the bug being fixed here, one build later.
    private readonly float _labelR = Defaults.LabelR;
    private readonly float _labelG = Defaults.LabelG;
    private readonly float _labelB = Defaults.LabelB;
    private readonly float _labelOutlineR = Defaults.LabelOutlineR;
    private readonly float _labelOutlineG = Defaults.LabelOutlineG;
    private readonly float _labelOutlineB = Defaults.LabelOutlineB;
    private readonly float _labelOutlineWidth = Defaults.LabelOutlineWidth;
    private readonly bool _labelOutlineOn = Defaults.LabelOutline;
    private readonly bool _labelUnderlayOn = Defaults.LabelUnderlay;
    private readonly float _boardCapTintR = Defaults.BoardCapTintR;
    private readonly float _boardCapTintG = Defaults.BoardCapTintG;
    private readonly float _boardCapTintB = Defaults.BoardCapTintB;
    private readonly float _dashCapTintR = Defaults.DashCapTintR;
    private readonly float _dashCapTintG = Defaults.DashCapTintG;
    private readonly float _dashCapTintB = Defaults.DashCapTintB;
    private readonly float _restCapTintR = Defaults.RestCapTintR;
    private readonly float _restCapTintG = Defaults.RestCapTintG;
    private readonly float _restCapTintB = Defaults.RestCapTintB;

    /// <summary>The owner's [ButtonColors] cap-FACE tint for the Confirm / Undo / item-USE column —
    /// the multiplier <c>PlayTray.BoardButton.StateColor</c> applies to the shared state palette.</summary>
    private Color BoardCapTint => new(_boardCapTintR, _boardCapTintG, _boardCapTintB, 1f);

    /// <summary>…for the follow/pin plate (<c>CapCategory.Dashboard</c>).</summary>
    private Color DashCapTint => new(_dashCapTintR, _dashCapTintG, _dashCapTintB, 1f);

    /// <summary>…for the short/long rest pair.</summary>
    private Color RestCapTint => new(_restCapTintR, _restCapTintG, _restCapTintB, 1f);

    /// <summary>The owner's engraved-label FILL colour. The mirrored labels used to be left at
    /// TMP's own default (pure white) because <c>InertCap.BuildLabel</c> never assigned one, while
    /// the local <c>BoardButton</c> assigns <c>NativeButtonSkin.LabelColor</c> — so every remote cap
    /// was lettered in white where its owner's was warm parchment #FBF3E0.</summary>
    private Color LabelFill => new(_labelR, _labelG, _labelB, 1f);

    /// <summary>The owner's engraved-label KEYLINE colour.</summary>
    private Color LabelOutline => new(_labelOutlineR, _labelOutlineG, _labelOutlineB, 1f);

    // ---- PRESS TRAVEL, per cap category (same story as the sizes above) ------------------------
    // How far the OWNER's cap of each category sinks under a press (the [*] Travel entries) — the
    // depth a mirrored cap dips on the synced press edge. Frozen until this build, which meant a
    // re-tuned press looked 4 mm deep on one screen and 8 on another.
    private readonly float _boardCapTravel = Defaults.BoardButtons_Travel;
    private readonly float _dashCapTravel = Defaults.BoardDashboard_Travel;
    private readonly float _restCapTravel = Defaults.RestButtons_Travel;

    /// <summary>Authored seconds a vanishing cap's dust dissolve runs
    /// (<c>[ButtonAnim] DisappearSeconds</c> — the duration <c>PlayTray.BoardButton.SetVisible</c>
    /// shrinks the local cap out over). Authored, not the viewer's tuned value, for the same reason
    /// every geometry constant here is authored.</summary>
    internal const float DissolveSeconds = Defaults.DisappearSeconds;

    /// <summary>Authored seconds an appearing cap's materialize-from-dust fade runs
    /// (<c>[ButtonAnim] AppearSeconds</c>).</summary>
    internal const float AppearSeconds = Defaults.AppearSeconds;

    /// <summary>LEGACY slot metric (authored card width × the local board's 1.3 SlotScale) — the
    /// fallback the live fields below take when the owner's real sizes are not on the wire.
    /// Identical to <see cref="NetProtocol.SlotCardWidthLegacy"/> by construction.</summary>
    private const float CardW = 0.0635f * 1.3f;
    private const float CardH = CardW * (88f / 63.5f);

    /// <summary>The owner's live slot FRAME metric (their <c>CardWidth × SlotScale</c>, extension
    /// record 11) — what the wanted/snap glow rims are sized from, exactly like the local board's
    /// slot frames. Falls back to <see cref="CardW"/> for pre-record peers.</summary>
    private readonly float _slotFrameW;
    private readonly float _slotFrameH;

    // The owner's live CARD width (record 11) is no longer read here: its only consumer was the
    // half-poke divider, which the 1:1 round deleted. The ctor still ACCEPTS it — the parameter is
    // part of the board's construction contract and the frame metric beside it is very much in use
    // — it simply has nothing to size any more.

    /// <summary>Authored card size for the item-use RECESS (the recess is card-sized at the
    /// UNSCALED card metric on the local board).</summary>
    private const float ItemCardW = 0.0635f;
    private const float ItemCardH = ItemCardW * (88f / 63.5f);

    // ---- palette (verbatim from the local widgets so the boards match) -------------------------
    private static readonly Color ConfirmColor = new(0.35f, 0.46f, 0.28f); // muted sage "go"
    private static readonly Color UndoColor = new(0.44f, 0.31f, 0.20f);    // worn leather
    /// <summary>PINNED (accented) FOLLOW/PIN cap — the <c>_accentColor</c> the local
    /// <c>PlayTray.BuildDashboardControls</c> hands its pin button (aged brass).</summary>
    private static readonly Color PinAccentColor = new(0.58f, 0.46f, 0.26f);

    /// <summary>FOLLOW (idle) FOLLOW/PIN cap — the colour every ENABLED, un-accented keycap rests
    /// at. The remote cap used to be built in the ACCENT colour and left there, so a peer's board
    /// showed the PINNED look with the FOLLOW label permanently — half of defect (a).
    ///
    /// <para>IT IS NO LONGER A MIRROR, and that is a strict improvement: it CALLS the owner's own
    /// <c>PlayTray.BoardIdleColor</c>. It used to be a hand-copied <c>(0.60, 0.51, 0.35)</c> next to
    /// a comment saying "verbatim", which is precisely the arrangement this project keeps paying
    /// for. Round 2 made that colour PER BOARD, so a copy would have had to reproduce three values
    /// and a board→colour mapping instead of one literal.</para></summary>
    private static Color PinIdleColor(Cards.ControlBoard? style) => Cards.PlayTray.BoardIdleColor(style);
    private static readonly Color SkipColor = new(0.37f, 0.44f, 0.56f);    // slate
    private static readonly Color HandleColor = new(0.62f, 0.50f, 0.28f);  // brass bar
    private static readonly Color ShortRestColor = new(0.62f, 0.52f, 0.30f); // parchment-gold
    private static readonly Color LongRestColor = new(0.37f, 0.44f, 0.56f);  // antique slate-blue

    // ---- the STATE palette every mirrored keycap now switches through (gap: "every cap looks
    //      enabled and un-accented"). The local caps have four looks and a peer saw ONE: the
    //      colour the cap was constructed with. These three are the shared statics
    //      PlayTray.BoardButton.StateColor picks between; they are private there and are Color
    //      values, which scripts/check-mirrors.sh (a float/string extractor) cannot lint — so they
    //      are called out as MIRRORS in both doc comments instead.
    //      THE IDLE ONE STOPPED BEING A MIRROR IN ROUND 2: it is now a CALL into PlayTray, because
    //      the colour went per board and a three-way copy is a three-way drift. The DISABLED and
    //      CONFIRMED pair below are still copies; promoting them is the same one-line change the
    //      day either of them goes per board.

    /// <summary>The idle field colour of an enabled, un-accented keycap on THIS board —
    /// <c>PlayTray.BoardIdleColor</c>, called rather than copied (see <see cref="PinIdleColor"/>),
    /// so the owner's three per-board values and a peer's cannot drift.</summary>
    private static Color CapIdleColor(Cards.ControlBoard? style) => Cards.PlayTray.BoardIdleColor(style);

    /// <summary>Verbatim <c>PlayTray.BoardButton.DisabledColor</c> — plain dark wood, "an unlit
    /// carved plaque". What a rest disc that is up but dead looks like on the owner's board.</summary>
    private static readonly Color CapDisabledColor = new(0.21f, 0.16f, 0.11f);

    /// <summary>Verbatim <c>PlayTray.BoardButton.ConfirmedColor</c> — worn brass, the "you ARE
    /// ready, pressing this REVOKES" look of the CONFIRM cap.</summary>
    private static readonly Color CapConfirmedColor = new(0.68f, 0.52f, 0.24f);

    // ---------------------------------------------------------------- the owner's own seats --
    // These used to be a switch per dial over the SHIPPED per-board layout
    // (Defaults.*_Oak/Steel/Bronze) keyed by the PEER's synced style, with a DELIBERATELY-NOT note
    // saying that the owner's re-tuning of them stays local. They are now read off
    // <see cref="RemoteBoardTuning"/>, which is the same shipped constant for every dial the owner
    // has NOT moved and their real value for every dial they have (extension record 28). The
    // per-board DESIGN half of the old argument is unchanged and still applies: a Steel peer's
    // confirm column belongs at the Steel spot (x +0.462 off the anchor!), not the Oak one.
    //
    // ONE DIAL USED TO BE DELIBERATELY NOT APPLIED — the ButtonCluster mount's POSITION
    // (ClusterOffset_*) — on the ground that the local rigid dock reads the mount's ROTATION and
    // SCALE only, so mirroring the position "would move the copy where the original never goes".
    // The observation was correct and the conclusion was the wrong half: the original never went
    // there because the LOCAL renderer had dropped the term when the lag fix reparented the cluster
    // off the mount, which is precisely the user's ModBuild-97 report ("Die Offsets bei den
    // Überspringen-Tasten haben keinen Einfluss"). ButtonCluster.AttachDocked applies it again, so
    // the dial is on the wire (id 16) and the skip cap's seat below adds it, like its SCALE twin.

    /// <summary>
    /// THE SLOT-OVERLAY SEAT — defect (d) of this round ("die Kartenoverlays haben einen Versatz
    /// auf der X-Achse, sie werden zu weit links dargestellt").
    ///
    /// The LOCAL glows are built as CHILDREN of the slot transform at slot-local
    /// <c>(SlotOverlayOffset.x ± SlotOverlaySpacing/2, SlotOverlayOffset.y, base + …z)</c>
    /// (<c>PlayTray.BuildSlotHighlights</c> / <c>BuildWantedHighlights</c>, and a card parked in
    /// the recess takes the SAME offset through <c>SlotHomeOffsetFor</c> — that is the whole point
    /// of the debug menu's "Overlays" element: glow and resting card move together). The remote
    /// copies were seated on the bare recess ANCHOR and dropped that term entirely — the exact
    /// same class of omission <see cref="RemoteBoardLayout"/> was created to end for the docks.
    ///
    /// On Oak the shipped values are <c>(0.002, −0.002, 0.004)</c> with a −0.008 pair spacing, so
    /// slot 0's overlay belongs 6 mm to the RIGHT of the anchor and slot 1's 2 mm to the LEFT —
    /// before the slot's own 1.3× <c>SlotScale</c>, which multiplies both because the local quads
    /// hang under the scaled slot. That is the reported leftward shift, and it is asymmetric,
    /// which is why it reads as "off" rather than as a uniform nudge.
    ///
    /// Returned in BOARD-local metres (the frame the remote overlays live in): the authored
    /// slot-local values × <c>PlayTray.SlotScale</c>. The Z term is deliberately EXCLUDED — the
    /// remote glows already carry their own proud offsets relative to the card plane, and the
    /// authored z is the local build's equivalent of exactly that.
    ///
    /// As everywhere on this board, these are the OWNER's own values (extension record 28) where
    /// they have moved the dial and the SHIPPED per-board default keyed by their SYNCED style
    /// where they have not — the two coincide for every untuned player.
    /// </summary>
    internal static Vector3 SlotOverlayLocal(in RemoteBoardTuning t, int slot)
    {
        Vector3 ov = t.SlotOverlayOffset;
        float spread = (slot == 0 ? -0.5f : 0.5f) * t.SlotOverlaySpacing;
        return new Vector3(ov.x + spread, ov.y, 0f) * Cards.PlayTray.SlotScale;
    }

    // ---------------------------------------------------------------- built pieces --

    private readonly Transform _root;

    /// <summary>The board's local see-through driver (user request 15) — null only if the board
    /// root died between construction and now. Inert while the setting is Off.</summary>
    private readonly PeerBoardFade? _fade;

    private readonly InertCap _confirm;
    private readonly InertCap _undo;
    private readonly InertCap _use;
    private readonly InertCap _pin;
    private readonly InertCap _skip;
    private readonly InertCap? _shortRest; // real-tray board only (needs the prefab rest anchors)
    private readonly InertCap? _longRest;

    private readonly Transform _itemUse;

    /// <summary>The mirrored berth's arrival/departure driver — see <see cref="BuildItemUseRecess"/>.
    /// Null only in a shader-less environment where the generated cue art could not be built; the
    /// recess then falls back to the plain activeSelf flip it had before.</summary>
    private readonly WorldUI.SoftCueReveal? _itemUseReveal;

    /// <summary>What <see cref="SetItemUseShown"/> was last asked for, as opposed to what is on
    /// screen while the departure animation is still running — the receiver-side twin of the owner's
    /// <c>PlayTray._itemUseSlotWanted</c>, and for the same reason: the root outlives the decision by
    /// the length of the collapse, so its <c>activeSelf</c> is not the state to gate on.</summary>
    private bool _itemUseWanted = true;

    /// <summary>
    /// The mirrored item-USE RECESS's own transform — the frame a card the owner has laid into that
    /// recess is drawn IN (extension record 26, <see cref="RemoteItemFan"/>).
    ///
    /// <para>WHY THE FRAME AND NOT A POSE. The owner's card is a CHILD of their recess
    /// (<c>ItemsPile.ItemChip.ClipIntoSlot</c>): the hierarchy holds it there rigidly at an exact
    /// zero local pose, which is what stopped it swimming behind head movement. Handing the peer's
    /// renderer the same frame reproduces that property for free — the mirrored card rides this
    /// board's easing, style, scale and visibility with no per-frame work and no residual error,
    /// and it LIES IN the recess instead of billboarding to anybody's head. Routing it through a
    /// synced pose slot (the held-card path) was considered and rejected for exactly that: that
    /// receiver billboards its slab to the owner's head, so the card would float rather than
    /// lie.</para>
    ///
    /// <para>Consumers must re-check <c>activeInHierarchy</c> every frame rather than latching:
    /// the recess is shown and hidden from the board-UI record's own bit
    /// (<see cref="NetProtocol.BoardUiItemRecessBit"/>), and a card must never be left lying on a
    /// recess the owner's board no longer shows.</para>
    /// </summary>
    internal Transform ItemUseRecess => _itemUse;

    /// <summary>CLEAR-AREA factor of the mirrored item-use berth — MIRROR of
    /// <c>Cards.ItemsPile.UseSlotInnerFactor</c>, the box a card laid into the recess is fitted
    /// into, on the owner's board and on its copy alike; the two numbers are linted together
    /// (scripts/check-mirrors.sh).
    ///
    /// <para>IT IS NO LONGER A PLATE, ON EITHER BOARD. This used to be the size of an opaque
    /// near-black "FrameInner" quad inside a 1.12× gold frame, and the doc named it that way. The
    /// 2026-08-09 re-art deleted the plate from both builders (see <see cref="BuildItemUseRecess"/>
    /// for the geometric reason — this berth hangs below the board with the player's room behind
    /// it), so what survives here is the number's REAL job, which the plate only happened to
    /// coincide with: the clear area the card has to sit inside. The berth's outline is drawn just
    /// OUTSIDE it (1.08×) and its warm field just inside it (1.03×), so a seated card never covers
    /// the cue that named its destination.</para></summary>
    internal const float UseSlotInnerFactor = 1.04f;

    /// <summary>Board-local size of that clear area on THIS board — what a mirrored card lying in
    /// the recess is fitted into (<see cref="RemoteItemFan"/>). Derived from the very constants
    /// <see cref="BuildItemUseRecess"/> lays the berth out from, so the fit and the art cannot
    /// drift apart.</summary>
    internal const float ItemUseInnerWidth = ItemCardW * UseSlotInnerFactor;

    /// <inheritdoc cref="ItemUseInnerWidth"/>
    internal const float ItemUseInnerHeight = ItemCardH * UseSlotInnerFactor;
    /// <summary>The mirrored decision AREA's CEILING in board-root-local metres — the owner's
    /// <c>WorldUI.Surfaces.DecisionDockSurface.AreaCeilingUp</c> expressed in this frame. Every piece
    /// of the mirrored display (prompt line, button row, use-bar drawer) descends from it, so a
    /// peer's copy has the same prompt-independent top edge the owner's board has.</summary>
    private readonly float _decisionCeilingY;

    /// <summary>Whether the mirrored PROMPT LINE is currently shown — the one thing that decides
    /// where the mirrored row sits under the ceiling (see <see cref="ApplyDecisionSeat"/>).</summary>
    private bool _decisionPromptShown;

    private readonly Transform _decision;

    /// <summary>The OWNER's resolved tuning this furniture was built for — keys the decision row's
    /// scale/gap when the synced content is rebuilt after construction. Their own values where they
    /// have moved a dial (extension record 28), the shipped per-board defaults where they have
    /// not.</summary>
    private RemoteBoardTuning _decisionTuning;

    /// <summary>The captioned IDLE drawer under <see cref="_decision"/> — shown while the owner
    /// has a docked prompt whose labels this client does not hold (legacy sender).</summary>
    private Transform? _drawerIdle;

    /// <summary>The SYNCED button row under <see cref="_decision"/> (wire record 12), rebuilt by
    /// <see cref="SetDecisionLines"/>; null while no labels are synced.</summary>
    private Transform? _decisionRow;

    /// <summary>True when <see cref="_decisionRow"/> was built with the SAMPLED GAME BUTTON SPRITE
    /// (the 1:1 look); false while it wears the pre-sample procedural fallback, which
    /// <see cref="SetDecisionLines"/> upgrades as soon as a live button has been harvested.</summary>
    private bool _decisionRowNative;

    /// <summary>
    /// THE REAL THING (ModBuild 105): a live clone of THIS client's own <c>TakeDamagePanel</c>
    /// widget row, driven from wire records 12/24/29 — the game's own button art, burn icons,
    /// damage icon and wordings, in the VIEWER's own language. It is the PRIMARY renderer of a
    /// peer's take-damage decision; <see cref="_decisionRow"/>'s mod-drawn plates are what stands in
    /// when it cannot be built (another prompt kind, a sender predating record 29, a prefab this
    /// build cannot resolve). See <see cref="RemoteDecisionWidgets"/> for the whole argument.
    /// </summary>
    private readonly RemoteDecisionWidgets? _decisionWidgets;

    /// <summary>True while a decision row of EITHER kind stands at the seat — the mirrored game
    /// widgets or the mod-drawn plates. What the use-bar drawer stacks below.</summary>
    private bool DecisionRowUp =>
        (_decisionWidgets != null && _decisionWidgets.Showing) || _shownDecisionLines != null;

    /// <summary>Board-local height of whatever row is standing: the mirrored widgets' MEASURED fit
    /// when they are up, the authored plate height otherwise. The use-bar stack hangs its own
    /// clearance below this, exactly as the owner's stack hangs below their measured row.</summary>
    private float DecisionRowHeight =>
        _decisionWidgets != null && _decisionWidgets.Showing
            ? _decisionWidgets.RowHeight
            : DecisionButtonH * _decisionTuning.DecisionScale;

    /// <summary>One mirrored option plate's repaintable parts — the pieces
    /// <see cref="ApplyDecisionOptionStates"/> writes when the owner's option states move (a toggle
    /// flips, the game re-asserts a gate) WITHOUT rebuilding the row. Face is the 9-sliced game
    /// button sprite when one has been sampled, otherwise the procedural rim/body pair.</summary>
    private readonly struct DecisionPlate
    {
        public DecisionPlate(SpriteRenderer? face, Material? rim, Material? body,
            TextMeshPro label, GameObject chosenRim)
        {
            Face = face;
            RimMat = rim;
            BodyMat = body;
            Label = label;
            ChosenRim = chosenRim;
        }

        public readonly SpriteRenderer? Face;
        public readonly Material? RimMat;
        public readonly Material? BodyMat;
        public readonly TextMeshPro Label;
        public readonly GameObject ChosenRim;
    }

    /// <summary>The mirrored option plates of the current row, in wire order (index i is line i of
    /// record 12 and option i of record 23 — the sender walked the widgets once for both).</summary>
    private readonly System.Collections.Generic.List<DecisionPlate> _decisionPlates = new(4);

    /// <summary>The option states last APPLIED to <see cref="_decisionPlates"/> (wire record 23);
    /// null = nothing applied yet, so the first refresh after a rebuild always paints.</summary>
    private byte[]? _shownOptionStates;

    /// <summary>
    /// PER-FRAME forward to the mirrored decision row's pointer drive — the owner's hover and press
    /// on their own decision buttons (wire record 24 bits 3 and 4).
    ///
    /// <para>Separate from <see cref="Refresh"/> and called from the board's per-frame block rather
    /// than its 4 Hz content pass, for the same reason the half-card hover beside it is: a pointer
    /// sampled four times a second reaches a viewer as a stutter. See
    /// <c>RemoteDecisionWidgets.TickPointer</c>, which is a gate and returns on nearly every frame
    /// without touching anything.</para>
    /// </summary>
    public void TickDecisionPointer(RemoteAvatar owner) => _decisionWidgets?.TickPointer(owner);

    /// <summary>The composed prompt line last shown above the mirrored row (null = none). Change
    /// gate: a TMP write re-triggers auto-size layout, the badge-flicker lesson.</summary>
    private string? _shownPromptText;

    /// <summary>The mirrored PROMPT TEXT above the decision row — the owner's HelpBox line,
    /// composed on THIS machine by <see cref="RemoteDecisionPrompt"/>; hidden while there is
    /// none.</summary>
    private readonly TextMeshPro _decisionPrompt;

    /// <summary>Change gate for <see cref="SetDecisionLines"/> (the '\n'-joined labels last
    /// built; null = idle drawer).</summary>
    private string? _shownDecisionLines;

    /// <summary>The mirrored USE-BAR drawer (wire record 25) — the SECOND drawer, below the
    /// decision row. Built empty and hidden; rebuilt by <see cref="SetUseBars"/> whenever the
    /// owner's bar structure changes.</summary>
    private readonly Transform _useBars;

    /// <summary>One mirrored bar's repaintable tiles + its sub-picker badge, so a state change (a
    /// slot toggling on, the game re-asserting a gate) repaints instead of rebuilding.</summary>
    private readonly struct UseBarRow
    {
        public UseBarRow(Transform root, Material[] tiles, GameObject[] chosenRims, GameObject picker,
                         SpriteRenderer[] symbols)
        {
            Root = root;
            Tiles = tiles;
            ChosenRims = chosenRims;
            Picker = picker;
            Symbols = symbols;
        }

        public readonly Transform Root;
        public readonly Material[] Tiles;
        public readonly GameObject[] ChosenRims;
        public readonly GameObject Picker;

        /// <summary>One per slot: THE GAME'S OWN ICON for that slot, resolved locally by
        /// <see cref="RemoteUseBarSymbols"/> and never received. Disabled while it cannot be
        /// resolved, which leaves the anonymous tile every build before this one drew.</summary>
        public readonly SpriteRenderer[] Symbols;
    }

    /// <summary>The mirrored bar rows currently built, in the owner's own stack order (top to
    /// bottom) — i.e. record 25's bit order.</summary>
    private readonly System.Collections.Generic.List<UseBarRow> _useBarRows = new(4);

    /// <summary>Which bar indices <see cref="_useBarRows"/> was built for, parallel to it — the
    /// caption of row <c>r</c> comes from bar index <c>_useBarRowIndices[r]</c>.</summary>
    private readonly System.Collections.Generic.List<int> _useBarRowIndices = new(4);

    /// <summary>STRUCTURE gate for <see cref="SetUseBars"/>: the mask + per-bar slot counts the
    /// current rows were built from (−1 = nothing built). Only a structure change rebuilds; the
    /// per-slot STATES repaint through <see cref="ApplyUseBarStates"/>, exactly the split the
    /// decision row uses between its labels and its option states.</summary>
    private int _shownUseBarStructure = -1;

    /// <summary>The per-slot state bytes last PAINTED onto <see cref="_useBarRows"/> (null =
    /// nothing painted yet, so the first refresh after a rebuild always paints).</summary>
    private byte[]? _shownUseBarStates;

    /// <summary>The per-bar sub-picker flags last painted (same contract as
    /// <see cref="_shownUseBarStates"/>).</summary>
    private byte[]? _shownUseBarFlags;

    /// <summary>Last synced CONFIRM wording applied to the cap (wire record 13; null = the
    /// neutral GUI_CONFIRM fallback is applied). Reset by <see cref="ApplyLabels"/> so a language
    /// switch re-derives the fallback without losing a live synced label.</summary>
    private string? _appliedConfirmWire;

    /// <summary>Last synced SKIP wording applied to the cap — same contract as
    /// <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedSkipWire;

    /// <summary>Last synced UNDO wording applied to the cap (record 13 mask bit 2) — same contract
    /// as <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedUndoWire;

    /// <summary>Last synced item-USE wording applied to the cap (record 13 mask bit 3) — same
    /// contract as <see cref="_appliedConfirmWire"/>.</summary>
    private string? _appliedUseWire;
    private readonly GameObject?[] _wanted = new GameObject?[2];
    private readonly GameObject?[] _snap = new GameObject?[2];

    // ---- change gates (a TMP/material write per tick is exactly the churn the 4 Hz cadence is
    //      there to avoid; every setter below no-ops until the value really moves) --------------
    private bool _shownArmed;
    private int _shownWantedMask = -1;
    private int _shownSnapMask = -1;
    /// <summary>Last applied synced buttons mask (-2 = nothing applied yet, -1 = legacy sender).</summary>
    private int _shownButtonsMask = -2;
    private string _langShown = string.Empty;

    /// <summary>Last applied cap-STATE byte (record 4 byte 2); -2 = nothing applied yet, -1 = a
    /// sender without the byte (every cap keeps its built colour).</summary>
    private int _shownCapStates = -2;

    /// <summary>The last cap-press key (cap | seq &lt;&lt; 8) this board ANIMATED. The wire field is
    /// a latch that rides several packets, so the dip plays on the value CHANGING; -1 = none seen.
    /// Seeded from the owner's current key on the first refresh so a board built mid-press does not
    /// replay a press that already happened.</summary>
    private int _playedPressKey = -1;

    /// <summary>False until the first <see cref="Refresh"/> has seeded every cap's visibility. The
    /// mirror of the local button's own <c>_ticked</c> guard: a board is BUILT with its caps shown
    /// and the first refresh applies the owner's real mask, which without this gate would crumble
    /// four caps into dust the instant a peer's board appears.</summary>
    private bool _settled;

    /// <summary>Per-slot unscaled time the snap glow was lit (a card just landed there). Negative
    /// infinity = never.</summary>
    private readonly float[] _snapLitAt = { float.NegativeInfinity, float.NegativeInfinity };

    /// <summary>How long a snap glow stays lit after a card lands in a slot. Matches the "the card
    /// will zap here" telegraph duration the local board's gold glow is visible for around a drop.</summary>
    private const float SnapGlowSeconds = 0.6f;

    /// <summary>Round-card occupancy at the previous refresh — the edge detector that lights the
    /// snap glow (see <see cref="Refresh"/>).</summary>
    private readonly bool[] _wasFilled = new bool[2];

    /// <summary>One-line state summary for the change-gated diagnostic in
    /// <see cref="RemoteControlBoard.LogContent"/> (grep: "Remote board furniture").</summary>
    public string StateLine { get; private set; } = string.Empty;

    // ---------------------------------------------------------------- construction --

    /// <summary>
    /// Build the inert furniture for a board of <c>style</c>. With a real
    /// <paramref name="tray"/>, caps seat on the prefab anchors + the authored per-style offsets
    /// (see the class note); without one, on the legacy flat-board constants.
    /// <paramref name="slot0CardLocal"/>/<paramref name="slot1CardLocal"/> are the board-local
    /// positions the two round CARDS render at — the slot overlays (wanted pulse / snap glow)
    /// centre on them so glow and card agree on every board style.
    /// </summary>
    public RemoteBoardFurniture(Transform boardRoot, in RemoteBoardTuning tuning, RemoteTrayVisual? tray,
        Vector3 slot0CardLocal, Vector3 slot1CardLocal,
        float slotFrameWidth = 0f, float slotCardWidth = 0f)
    {
        // The owner's synced slot metrics (extension record 11); 0 = not on the wire, keep the
        // legacy constant — the exact size every build before the record drew. Only the FRAME
        // metric is consumed now (the glow rims); see the field block above for where the CARD
        // metric's consumer went.
        _ = slotCardWidth;
        _slotFrameW = slotFrameWidth > 0f ? slotFrameWidth : CardW;
        _slotFrameH = _slotFrameW * (88f / 63.5f);

        _root = new GameObject("Furniture").transform;
        _root.SetParent(boardRoot, worldPositionStays: false);

        // PEER-BOARD SEE-THROUGH (user request 15, 2026-08: "dass die Boards transparent werden
        // oder verschwinden wenn sie Teile des Spielfeldes verdecken aus der aktuellen View").
        // Attached HERE because this constructor is the one per-board seam that is handed the
        // board ROOT, and the driver needs exactly that: it measures the whole board's occluder
        // box and drives the whole board's opacity. Idempotent, self-contained, and inert while
        // the setting is Off (its first statement returns) — see PeerBoardFade.
        _fade = PeerBoardFade.Attach(boardRoot);

        // ---- the OWNER's keycap geometry, ONCE, before anything is built ------------------------
        // Extension record 28 ids 81..98 + 228 (see the field block above for the debt this pays).
        // Every value is floored the way the local builders floor their own: a WIRE number is never
        // trusted to be sane, and a zero or negative side length would build a degenerate mesh the
        // renderer cannot recover from. An absent field already resolved to the shipped default in
        // RemoteBoardTuning, so these clamps only ever fire on a corrupt sender.
        _boardCapD = Mathf.Max(0.002f, tuning.BoardCapDepth);
        _boardCapTravel = Mathf.Max(0f, tuning.BoardCapTravel);
        // …AND THE GENERIC CAP IS FITTED TO THIS BOARD'S SEAT RECESS, term for term with
        // PlayTray.BuildButtons. The owner's keycaps shrink to fit the button recess of the board
        // style they are on (the three re-authored boards cut them at 74.6 × 64.3 / 81.0 × 70.1 /
        // 61.2 × 51.9 mm of usable floor, and the tuned 73 × 73 mm cap does not fit two of them).
        // The fit is DERIVED, never sent: this peer clones the SAME prefab out of the SAME bundle,
        // so it measures the same recess and calls the same BoardAnchors.FitCapSize with the same
        // margin rule — which is exactly why a third seat and a fitted cap cost no wire field. The
        // wire still carries only the owner's tuned [BoardButtons] W/H (ids 89/90), i.e. the CEILING
        // the fit is measured against, so an owner who retunes is still seen retuning.
        float seatMargin = Mathf.Clamp(_boardCapTravel, 0.001f, 0.008f);
        Vector2 fittedCap = Cards.BoardAnchors.FitCapSize(
            new Vector2(Mathf.Max(0.002f, tuning.BoardCapWidth), Mathf.Max(0.002f, tuning.BoardCapHeight)),
            tray?.SeatMinHalf, seatMargin);
        _boardCapW = fittedCap.x;
        _boardCapH = fittedCap.y;
        _pinCapW = Mathf.Max(0.002f, tuning.DashPinWidth);
        _dashCapH = Mathf.Max(0.002f, tuning.DashCapHeight);
        _dashCapD = Mathf.Max(0.002f, tuning.DashCapDepth);
        _dashCapTravel = Mathf.Max(0f, tuning.DashCapTravel);
        _restCapD = Mathf.Max(0.002f, tuning.RestCapDepth);
        _restCapTravel = Mathf.Max(0f, tuning.RestCapTravel);
        _restCapW = Mathf.Max(0.002f, tuning.RestCapWidth);
        _restCapH = Mathf.Max(0.002f, tuning.RestCapHeight);
        _restShape = tuning.RestCapShape;
        _genericShape = tuning.GenericCapShape;
        _style = tuning.Style;

        // ---- …and the owner's CAP COLOURS, before the first material is minted ------------------
        // Record 28 ids 48..53 / 170 / 229..230 (see the field block above for the defect these
        // close). Clamped the way the OWNER's own ButtonTuning accessors clamp before they paint:
        // a colour channel outside 0..1 renders as an HDR over-bright on a peer's board that the
        // owner is not seeing, and the wire is never trusted to be in range. An absent field already
        // resolved to the shipped value in RemoteBoardTuning, so these clamps only fire on a corrupt
        // sender — which is exactly when a mirrored board must degrade to "the same as everyone
        // else's" rather than to something nobody authored.
        _labelR = Mathf.Clamp01(tuning.LabelColor.r);
        _labelG = Mathf.Clamp01(tuning.LabelColor.g);
        _labelB = Mathf.Clamp01(tuning.LabelColor.b);
        _labelOutlineR = Mathf.Clamp01(tuning.LabelOutlineColor.r);
        _labelOutlineG = Mathf.Clamp01(tuning.LabelOutlineColor.g);
        _labelOutlineB = Mathf.Clamp01(tuning.LabelOutlineColor.b);
        _labelOutlineWidth = Mathf.Clamp01(tuning.LabelOutlineWidth);
        _labelOutlineOn = tuning.LabelOutlineOn;
        _labelUnderlayOn = tuning.LabelUnderlayOn;
        _boardCapTintR = Mathf.Clamp01(tuning.BoardCapTint.r);
        _boardCapTintG = Mathf.Clamp01(tuning.BoardCapTint.g);
        _boardCapTintB = Mathf.Clamp01(tuning.BoardCapTint.b);
        _dashCapTintR = Mathf.Clamp01(tuning.DashCapTint.r);
        _dashCapTintG = Mathf.Clamp01(tuning.DashCapTint.g);
        _dashCapTintB = Mathf.Clamp01(tuning.DashCapTint.b);
        _restCapTintR = Mathf.Clamp01(tuning.RestCapTint.r);
        _restCapTintG = Mathf.Clamp01(tuning.RestCapTint.g);
        _restCapTintB = Mathf.Clamp01(tuning.RestCapTint.b);

        // The label style the OWNER's caps wear, handed to every InertCap below. One struct rather
        // than five parameters threaded through two builders, because the set has to travel intact:
        // a cap lettered with this peer's fill and the VIEWER's keyline would be a subtler version
        // of the very bug this closes.
        var labels = new CapLabelStyle(LabelFill, LabelOutline, _labelOutlineWidth,
                                       _labelOutlineOn, _labelUnderlayOn);

        // ---- right-hand control column: CONFIRM / [USE] / UNDO / SKIP ------------------------
        // Real tray: on the prefab's own BUTTON SEATS 0, 1 and 2 (RemoteTrayVisual resolves them
        // through Cards.BoardAnchors, so ButtonSeat1/2/3 and the legacy
        // ConfirmButton/UndoButton/SkipButton spellings all land here) + the shared seat nudge + the
        // stack spread — exactly PlayTray.GenericSeatY, which is the SAME BoardAnchors.StackDelta
        // call on both sides and is identically zero at the shipped spacing of 1. Fallback: the
        // procedural mounts, which are the owner's own synthesised anchors expression.
        //
        // SEAT 2 IS DRAWN HERE NOW — the turn-flow SKIP, built by the SAME GenericCap call as its
        // two siblings, on the SAME anchor table, through the SAME clamp. That is the whole of what
        // "moving it into the cluster" cost on this side, and it is what makes record 28's ids
        // 81..88 unnecessary rather than merely changed: there is no second solve left to keep in
        // step with the owner's.
        Vector3 cuOff = tuning.ConfirmUndoOffset;
        float cuSpacing = tuning.ButtonStackSpacing;
        // The board's OWN recess pitch, measured off this peer's clone of the very prefab the owner
        // instantiated — so it needs no wire field and cannot disagree (RemoteTrayVisual.SeatPitch).
        float? measuredPitch = tray?.SeatPitch;
        float seatPitch = measuredPitch ?? Defaults.StackPitchFallback;
        Transform confirmParent = tray?.ConfirmAnchor ?? _root;
        Transform undoParent = tray?.UndoAnchor ?? _root;
        // SEAT 2 ON A TWO-ANCHOR BOARD — the case the old bundle still on somebody's disk produces,
        // and the one place this file could silently break the 1:1 rule on the control the whole
        // round is about. The owner does not fall back to an authored mount there: PlayTray
        // EXTRAPOLATES the missing recess from the two the board does supply, at the board's own
        // measured pitch. Doing anything else here would draw the peer's skip cap somewhere the
        // owner's never goes, so this is the same BoardAnchors.SeatExtrapolation call, from the same
        // pitch, off the same anchor.
        bool skipExtrapolated = tray != null && tray.SkipAnchor == null
                                && tray.UndoAnchor != null && measuredPitch != null;
        Vector3 skipExtra = skipExtrapolated
            ? Cards.BoardAnchors.SeatExtrapolation(2, 1, measuredPitch!.Value)
            : Vector3.zero;
        Transform skipParent = tray?.SkipAnchor ?? (skipExtrapolated ? tray!.UndoAnchor! : _root);
        // …AND THE SEAT POSE GOES THROUGH THE ONE CLAMP, term for term with
        // PlayTray.SetConfirmUndoOffset: on a board whose recesses the assembler measured, the
        // in-plane part of the synced offset and the whole spacing term are bounded by the slack
        // between this cap and the recess wall (Z untouched); on a board with no measurement nothing
        // is bounded and this is the previous build's arithmetic exactly. It has to happen HERE and
        // not only on the owner's side, because the owner's ConfirmUndoOffset_Steel of +0.462 is the
        // number that rides the wire — a peer applying it raw would draw the mirrored cluster 35 cm
        // off the board while the owner's sits in its recess. Derived, not sent: same prefab, same
        // function, same answer.
        var capSize = new Vector2(_boardCapW, _boardCapH);
        Vector3 confirmPos = tray?.ConfirmAnchor != null
            ? Cards.BoardAnchors.ClampSeatPose(cuOff, SeatY(0), tray.SeatMinHalf, capSize)
            : ConfirmMount;
        Vector3 undoPos = tray?.UndoAnchor != null
            ? Cards.BoardAnchors.ClampSeatPose(cuOff, SeatY(1), tray.SeatMinHalf, capSize)
            : UndoMount;
        Vector3 skipPos = tray?.SkipAnchor != null
            ? Cards.BoardAnchors.ClampSeatPose(cuOff, SeatY(2), tray.SeatMinHalf, capSize)
            : skipExtrapolated
                // The extrapolation is added OUTSIDE the clamp on purpose: it is not a tuned nudge
                // but the seat itself, and the clamp bounds a cap inside a recess this board does
                // not have. (A board that supplies no ButtonSeat3 also carries no SeatExtent3, so
                // tray.SeatMinHalf is null there and the clamp is inert anyway — this only makes the
                // reason explicit rather than incidental.)
                ? Cards.BoardAnchors.ClampSeatPose(cuOff, SeatY(2), tray!.SeatMinHalf, capSize) + skipExtra
                : SkipMount;

        float SeatY(int seat) => Cards.BoardAnchors.StackDelta(
            seat, Cards.BoardAnchors.ButtonSeatCount, seatPitch, cuSpacing);
        // BUILT AT THE IDLE COLOUR, NOT THE ACCENT — half of the "every cap looks accented" gap,
        // and it costs nothing. The colour a local keycap is CREATED with is its _accentColor, the
        // look it wears only while SetState(accent: true); its resting look is the shared parchment
        // IdleColor. These two mirrors were built in the accent and left there, so a peer's CONFIRM
        // sat permanently in the sage "go" accent and their UNDO in worn leather — a colour the
        // owner's undo cap never wears at all, since every SetState on it is (enabled, !accent).
        // The accent is passed alongside so the state pass can switch back to it.
        //
        // …AND IN THE OWNER'S SHAPE (record 28 id 232). This column was built Square unconditionally
        // — the shipped [Cards] GenericButtonShape_{board} is Square, so it looked right until
        // somebody turned the dial, and then only they could see it. The local builder's round
        // branch takes [BoardButtons] WIDTH as its diameter and the same depth/travel
        // (PlayTray.BuildButtons), so that is term for term what the round branch here does.
        _confirm = GenericCap(confirmParent, "Confirm", confirmPos, CapIdleColor(_style), ConfirmColor, labels,
                              Cards.CapRole.Confirm);
        _undo = GenericCap(undoParent, "Undo", undoPos, CapIdleColor(_style), UndoColor, labels,
                           Cards.CapRole.Undo);

        // The item "USE" confirm SHARES THE CONFIRM SEAT — the same seat, term for term, that
        // PlayTray.SetConfirmUndoOffset now writes to both of the owner's caps
        // (PlayTray.GenericPrimarySlot). It used to be drawn at the geometric MIDPOINT of Confirm and
        // Undo, on the reading that it was a third stack member between them; it never was one on the
        // owner's board (there it sat on the Confirm ANCHOR, ~52 mm higher than this midpoint), and
        // since the user's alignment ruling it is not a separate member at all — a placed item hides
        // Confirm, so the two are mutually exclusive and occupy one seat. Reusing `confirmPos`
        // verbatim, on `confirmParent` exactly as the owner does, is what keeps the mirror 1:1: this
        // seat is DERIVED on every peer from the synced [Cards] ConfirmUndoOffset/GenericButtonSpacing
        // (BoardTuning record), never carried as a pose on the wire.
        // …and this one IS accented, permanently: every SetState on the local item-USE cap is
        // (enabled: true, accent: true) — "always pressable while shown (no game gate)" — so its
        // look is a BUILD fact, not a state fact, and it costs no wire bit (see
        // NetProtocol.BoardUiCapConfirmAccentBit's "what is not here" note).
        _use = GenericCap(confirmParent, "ItemUse", confirmPos, ConfirmColor, ConfirmColor, labels,
                          Cards.CapRole.ItemUse);
        // Starts hidden and in step with the _shownArmed seed below: the local cluster only holds
        // this member while an item decision is pending, and Refresh() early-outs while nothing
        // changed — so a board that never sees an item fan must not be left showing a USE cap.
        _use.SetShown(false);

        // ---- rest discs (real-tray board only — they seat in the prefab's rest notches) --------
        // The local board's short/long rest BoardButtons: round discs at the ShortRestToken /
        // LongRestToken anchors + the authored per-style offset ± spacing/2 (RestControls
        // EnsureBuilt/SetOffset).
        //
        // THE "NEUTRAL LOOK" NOTE THAT USED TO STAND HERE IS GONE. It said the discs are "drawn at
        // their authored accent colours; whether the peer has actually selected a rest is a
        // separate readout, not a cap state". That was the defect: RestControls.TickStatus drives
        // these two caps with SetState(canShort, accent: shortSelected) — three visibly different
        // looks (dark-wood dead, parchment available, accented selected) — and a peer saw the
        // ACCENT one always, i.e. every visible rest disc read as "selected". The states ride the
        // board-UI record's cap-state byte now and are applied in ApplyCapStates; the discs are
        // built IDLE and switch to their authored accent when the owner's do.
        if (tray?.ShortRestAnchor != null && tray.LongRestAnchor != null)
        {
            // …AND IN THE OWNER'S SHAPE (record 28 id 231). "The mirrored rest cap is hardwired
            // round (no Square branch)" was a stated PENDING debt with two dials parked behind it
            // ([RestButtons] Width/Height). The branch is here now and both dials ride: a ROUND
            // disc keeps the per-board DIAMETER while a SQUARE cap takes the [RestButtons] W/H,
            // which is exactly the split RestControls.EnsureBuilt makes on the owner's own board.
            Vector3 restOff = tuning.RestButtonOffset;
            float restSpacing = tuning.RestStackSpacing;
            float restPitch = Cards.BoardAnchors.StackPitch(tray.ShortRestAnchor, tray.LongRestAnchor)
                              ?? Defaults.StackPitchFallback;
            float restD = tuning.RestButtonDiameter;
            // …AND FITTED + CLAMPED TO THE AUTHORED PAD, term for term with RestControls: the tuned
            // diameters overhang two of the three pads, and RestButtonOffset_Steel/_Bronze carry the
            // same 44 cm mirror compensation the button offsets do. Both are solved from THIS peer's
            // copy of the same prefab, so owner and peer land on the same disc in the same pad.
            float restMargin = Mathf.Clamp(_restCapTravel, 0.001f, 0.008f);
            var restSize = _restShape == Cards.ButtonShape.Round
                ? new Vector2(restD, restD)
                : new Vector2(_restCapW, _restCapH);
            restSize = Cards.BoardAnchors.FitCapSize(restSize, tray.RestMinHalf, restMargin);
            if (_restShape == Cards.ButtonShape.Round)
            {
                restD = Mathf.Min(restSize.x, restSize.y);
                restSize = new Vector2(restD, restD);
            }
            else
            {
                // Only the SQUARE branch reads these; leaving them alone under a round shape keeps
                // the fitted diameter the single thing the round path depends on.
                _restCapW = restSize.x;
                _restCapH = restSize.y;
            }
            // The two clamped poses are named rather than inlined because the ENGRAVED CAPTIONS
            // below have to sit off the same numbers: a caption placed from an unclamped pose would
            // drift away from the disc it names on exactly the boards whose tuned offsets the clamp
            // exists to bound.
            Vector3 shortPose = Cards.BoardAnchors.ClampSeatPose(
                restOff, Cards.BoardAnchors.StackDelta(0, 2, restPitch, restSpacing),
                tray.RestMinHalf, restSize);
            Vector3 longPose = Cards.BoardAnchors.ClampSeatPose(
                restOff, Cards.BoardAnchors.StackDelta(1, 2, restPitch, restSpacing),
                tray.RestMinHalf, restSize);
            _shortRest = RestCap(tray.ShortRestAnchor, "ShortRest", shortPose,
                restD, ShortRestColor, labels, Cards.CapRole.ShortRest);
            _longRest = RestCap(tray.LongRestAnchor, "LongRest", longPose,
                restD, LongRestColor, labels, Cards.CapRole.LongRest);

            // THE ENGRAVED REST CAPTIONS, on the peer's board exactly as on the owner's. With the
            // pad's own motif carved into the disc the disc carries no word, so the word is cut
            // into the board beside it — and if it were cut on one board and not the other, a
            // teammate would be looking at two unlabelled discs while their owner reads "KURZE
            // RAST" under theirs. Same anchors, same offsets from BoardEngraving, same strings,
            // and the SAME clamped disc pose (below), so every dial the owner turns moves both
            // copies together.
            //
            // The caption is a child of the ANCHOR, not of the cap, so a cap teardown leaves it
            // alone and the board never accumulates a stack of identical carvings.
            if (Cards.CapSymbols.TryAtlas(_style, out _, out _))
            {
                // THE DEPTH IS THE OWNER'S TOO, and it is the half of this that was wrong on BOTH
                // boards until now: the anchor these hang from is the rest pad's RECESS FLOOR, and
                // the caption is carried out of that recess onto a panel 3.6-4.0 mm prouder, so at
                // the shared 0.8 mm it was seated inside the wood and drew nothing anywhere. See
                // Cards.BoardEngraving.RestCaptionProudLocalZ.
                //
                // The owner's per-caption NUDGE rides ids 17/18 (BoardTuning) — a caption he pushed
                // into his board's top margin has to be in the mirror's top margin too, or a
                // teammate reads a word sitting off its own disc.
                Vector3 shortNudge = tuning.ShortRestCaptionOffset;
                Vector3 longNudge = tuning.LongRestCaptionOffset;
                _shortRestEngraving = Cards.BoardEngraving.Create(tray.ShortRestAnchor,
                    "ShortRestEngraving",
                    new Vector3(shortPose.x + shortNudge.x,
                                shortPose.y + Cards.BoardEngraving.RestCaptionOffsetY(restSize.y)
                                    + shortNudge.y, 0f),
                    Cards.BoardEngraving.RestCaptionBox, Cards.BoardEngraving.CaptionMaxFontSize, _style,
                    proudZ: Cards.BoardEngraving.RestCaptionProudLocalZ + shortNudge.z);
                Cards.BoardEngraving.SetText(_shortRestEngraving,
                    Loc.Mod("short_rest").ToUpperInvariant());
                _longRestEngraving = Cards.BoardEngraving.Create(tray.LongRestAnchor,
                    "LongRestEngraving",
                    new Vector3(longPose.x + longNudge.x,
                                longPose.y - Cards.BoardEngraving.RestCaptionOffsetY(restSize.y)
                                    + longNudge.y, 0f),
                    Cards.BoardEngraving.RestCaptionBox, Cards.BoardEngraving.CaptionMaxFontSize, _style,
                    proudZ: Cards.BoardEngraving.RestCaptionProudLocalZ + longNudge.z);
                Cards.BoardEngraving.SetText(_longRestEngraving,
                    Loc.Game("GUI_LONG_REST", "Long rest").ToUpperInvariant());
            }
        }

        // FOLLOW/PIN toggle: built in the FOLLOW (idle) look, then driven from the owner's synced
        // state every refresh (SetPinned) — label AND cap colour, exactly like their own cap.
        // Built in the FOLLOW look, which is also the FOLGEN symbol (two footprints); SetPinned
        // swaps BOTH the colour and the symbol to the anchor the moment the owner's pinned bit
        // arrives, from the one read, exactly as the owner's own toggle does.
        _pin = InertCap.Square(_root, "FollowToggle", PinMount + tuning.PinOffset,
            new Vector2(_pinCapW, _dashCapH), _dashCapD, PinIdleColor(_style), DashCapTint, labels,
            travel: _dashCapTravel, accent: PinAccentColor,
            role: Cards.CapRole.FixedFollow, style: _style);
        // …and the word for it, cut into the board above the toggle. Seated off the SAME mount the
        // cap is (PinMount + the owner's tuned PinOffset) and lifted by the shared
        // BoardEngraving.PinCaptionLiftY, so it tracks the owner's dial the way the cap does.
        // SetPinned writes its text on the first refresh, from the owner's synced pinned bit.
        //
        // AND ITS DEPTH IS THE OWNER'S ARITHMETIC, WRITTEN OUT. This caption hangs off the board
        // ROOT rather than off an anchor, and BoardEngraving.Create overwrites Z in its PARENT's
        // frame — so passing the bare seat constant here would put the mirror's caption at that
        // constant while the owner's sits at (anchor Z + the constant), 5 mm apart on boards nobody
        // tuned. The mount's own Z, the owner's tuned PinOffset.z and the seat constant are all
        // three of them terms of the owner's depth, so all three are added here.
        if (Cards.CapSymbols.TryAtlas(_style, out _, out _))
            _pinEngraving = Cards.BoardEngraving.Create(_root, "FollowEngraving",
                PinMount + tuning.PinOffset + new Vector3(0f, Cards.BoardEngraving.PinCaptionLiftY, 0f),
                Cards.BoardEngraving.PinCaptionBox, Cards.BoardEngraving.CaptionMaxFontSize, _style,
                proudZ: PinMount.z + tuning.PinOffset.z + Cards.BoardEngraving.PinCaptionProudLocalZ);

        // ---- grab-handle bar -------------------------------------------------------------------
        // The local handle is a brass Cube PLUS a 62 %-wide trigger BoxCollider and a
        // WorldUI.PanelGrabHandle that carries/rotates/resizes the board. The remote copy is the
        // 3D BAR (the same cube the local board renders): no collider, no grab zone, no handle
        // component. A peer's board can never be picked up — it follows the pose THEY broadcast
        // and nothing else.
        var handle = new GameObject("HandleBar").transform;
        handle.SetParent(_root, worldPositionStays: false);
        handle.localPosition = HandleMount;
        LitCube(handle, "Bar", new Vector3(BoardW * 0.55f, 0.024f, 0.024f), HandleColor);

        // ---- the turn-flow SKIP cap — seat 2 of the generic cluster ---------------------------
        // Built by the SAME GenericCap call as Confirm and Undo, so it is their size, their depth,
        // their travel, their shape and their [ButtonColors] tint by construction. Ninety lines of
        // column-anchor / dock-scale / proud-lift / [RoundButtons] arithmetic stood here to
        // reproduce a solve that no longer exists on the owner's side either; the whole of it is
        // this one line now, and that is the point rather than a side effect — two caps that are
        // built by one expression cannot be positioned or shaped differently from each other.
        //
        // Its ACCENT is SkipColor — the (0.37, 0.44, 0.56) antique slate-blue the retired cluster
        // built its skip cap in, which this palette already held verbatim and which
        // PlayTray.BuildButtons now hands the local cap. So the one thing a player recognises the
        // control by survives the move, on both screens, from the same triple. Its state arrives on
        // the board-UI record's cap-state byte exactly as before.
        _skip = GenericCap(skipParent, "TurnFlowSkip", skipPos, CapIdleColor(_style), SkipColor, labels,
                           Cards.CapRole.Skip);
        Core.VRLog.Info("Net", "SKIP CAP: mirrored on the board's own ButtonSeat3 recess as an " +
            $"ordinary generic keycap ({_genericShape}, {_boardCapW * 1000f:F1} x " +
            $"{_boardCapH * 1000f:F1} mm before the seat fit) — the owner builds it from the same " +
            "[BoardButtons] family and seats it through the same BoardAnchors clamp, so the two " +
            "pictures agree without a geometry family or a wire field of its own. The retired " +
            "[RoundButtons] column solve (record 28 ids 81..88 + shape 228) is gone from both ends.");

        // ---- item-USE clip-in recess ----------------------------------------------------------
        // The owner's own berth dials FIRST (record 28, ids 80 / 166..169): BuildItemUseRecess reads
        // them as it lays the berth out, and the berth is built once per board — a tuning change
        // rebuilds the whole board, so this is the only moment they are read. Guarded the same way
        // the local PlayTray guards its own copies, because a WIRE value is never trusted: a zero or
        // negative reveal collapses the animation and a negative band inverts the outline quad.
        _itemBerthRingThickness = Mathf.Max(0.0008f, tuning.ItemBerthRingThickness);
        _itemBerthGlow = Mathf.Clamp01(tuning.ItemBerthGlow);
        _itemBerthPingSeconds = Mathf.Max(0f, tuning.ItemBerthPingSeconds);
        _itemBerthPingReach = Mathf.Max(1f, tuning.ItemBerthPingReach);
        _itemBerthRevealSeconds = Mathf.Max(0.01f, tuning.ItemBerthRevealSeconds);
        _itemUse = BuildItemUseRecess(ItemUseMount + tuning.ItemUseSlotOffset, out _itemUseReveal);

        // ---- shared decision drawer -----------------------------------------------------------
        // ANCHORED WHERE THE OWNER'S DOCK REALLY HANGS (task 2 — the detached "ENTSCHEIDUNGEN"
        // plate): the local DecisionDockSurface does NOT place its widget block at the decision
        // MOUNT's y — it anchors the block TOP a configured gap below the grab-bar BOTTOM
        // (Place(): promptRef = bar bottom − BarClearance; block top = promptRef − DecisionGap +
        // the offset's own up displacement, which the raw mount Y otherwise cancels out of the
        // solve). The old drawer sat at the RAW mount seat (y −0.447) — 0.18 m below where the
        // owner's buttons actually are. The mirror now derives the same top edge from the same
        // references: the handle bar's authored seat, its zone half-height, the shared clearance
        // and the AUTHORED per-board DecisionGap. The mount contributes its authored X/Z (sideways
        // + proud) and, since ModBuild 90, the owner's offset Y on top of that reference — exactly
        // as it does locally.
        //
        // AT THE OWNER'S SCALE, TOO (ModBuild 89): the owner's two seat terms are mount-local
        // metres multiplied by the MOUNT's lossyScale — root × [Cards] DecisionScale — while this
        // root frame is the board root, so both have to carry the dock scale here or the mirrored
        // row hangs (1 − scale) × (BarClearance + DecisionGap) too HIGH (30 mm at the shipped 1.6×).
        // Every other length in this drawer already carries it (plate size, widths, the prompt
        // line); these two were the exception.
        //
        // …AND THE OWNER'S OFFSET MOVES THE WHOLE AREA, Y INCLUDED (ModBuild 90). X/Z have always
        // slid this drawer because they slide the seat it is built at; Y did not, because the seat
        // is derived from the grab bar and the mount's Y never entered it — exactly the local
        // cancellation (DecisionDockSurface.MountOffsetUp). It is added to the PROMPT REFERENCE
        // here, which is the one place that carries it into all three pieces at once: the drawer,
        // the use-bar drawer under it and the prompt line above it all descend from this Y. Board
        // root-local metres, unscaled by the dock scale — the owner's offset is the mount's own
        // localPosition in the same frame, and its X/Z are applied unscaled two lines down for the
        // same reason.
        //
        // …AND SINCE ModBuild 91 IT HANGS FROM THE AREA'S CEILING, TOP-DOWN (user: "Ich hätte
        // erwartet, dass der höchste Punkt bei den Initiativ-Schuhen auch der höchste Punkt ist, an
        // dem der Text angezeigt wird … also dass die Offsets für die gesamte Area gelten"). The
        // owner's three decision surfaces now derive every seat from ONE height —
        // WorldUI.Surfaces.DecisionDockSurface.AreaCeilingUp: the drawer zone's own top edge,
        // DecisionMountMaxHeight/2 above the (offset-carrying) mount, clamped so it can never rise
        // into the grab bar — and lay themselves out downward from it. This mirror reproduces the
        // same three terms in board-root-local metres: the authored mount Y plus the owner's offset
        // Y, the zone half-height at the owner's dock scale, and the same bar-bottom clamp. What
        // hangs where is then decided by whether the owner's prompt has a text line (see
        // ApplyDecisionSeat), exactly as it is on their board.
        _decisionTuning = tuning;
        Vector3 decisionOff = tuning.DecisionOffset;
        float decisionScale = tuning.DecisionScale;
        float barBottomY = HandleMount.y - HandleZoneHalfY;
        // The owner's clamp is measured from the mount they have ALREADY displaced, so in this frame
        // it is the un-offset absolute board-lower-edge reference — the offset does not move the bar.
        float promptRefY = barBottomY - BarClearanceMeters * decisionScale;
        _decisionCeilingY = Mathf.Min(
            DecisionMount.y + decisionOff.y
                + Cards.PlayTray.DecisionMountMaxHeight * 0.5f * decisionScale,
            promptRefY);
        // Built at the no-text seat (the ceiling itself); ApplyDecisionSeat drops it by the prompt
        // line's height + the gap for as long as the owner's prompt shows one.
        _decision = BuildDecisionDrawer(new Vector3(
            DecisionMount.x + decisionOff.x, _decisionCeilingY, DecisionMount.z + decisionOff.z));
        // …and the REAL widget mirror that hangs at the same seat and normally replaces the plates
        // above (ModBuild 105). Built empty and hidden; it claims the seat on the first refresh
        // where the owner's prompt is a take-damage one and record 29 named its widgets.
        _decisionWidgets = new RemoteDecisionWidgets(_decision, decisionScale);
        // ---- the SECOND drawer: the mirrored use-slot bars (wire record 25) -------------------
        // The owner's UseBarsSurface stacks its bars BELOW the decision row: while a row is docked
        // the stack top hangs DecisionClearance under the row's measured bottom edge, otherwise it
        // takes the drawer zone's own top. The mirror derives the same two cases from the pieces it
        // already has — the decision root above (whose synced row is exactly one plate tall) and
        // the authored decision mount — see SetUseBars for the term-by-term derivation. Built empty
        // at the same X/Z as the decision drawer; content grows DOWN from its origin.
        //
        // ITS ORIGIN IS THE AREA'S CEILING and stays there (ModBuild 91): with no decision row the
        // bars ARE the top of the display and start at 0 in this frame — the owner's initiative-boots
        // case — and with a row up SetUseBars measures down from the row's live seat instead. Keeping
        // this root fixed is what lets the row move (a prompt line appearing above it) without every
        // bar row having to be rebuilt at a new origin.
        _useBars = new GameObject("UseBarsDrawer").transform;
        _useBars.SetParent(_root, worldPositionStays: false);
        _useBars.localPosition = new Vector3(DecisionMount.x + decisionOff.x, _decisionCeilingY,
            DecisionMount.z + decisionOff.z);
        _useBars.gameObject.SetActive(false);

        // …and the PROMPT TEXT above it, at the seat the owner's own tip takes — WHICH IS NOW THE
        // ROW'S OWN SEAT PLUS THE GAP (ModBuild 89; the owner reported the line behaving "wie ein
        // Element das nicht zu dem Bereich dazugehört"). Their DamageTooltipSurface used to park
        // the HelpBox a fixed 0.11 m over the decision MOUNT, so — unlike the row, whose Y solve
        // cancels the mount out — it was the one piece of the decision area that followed the
        // mount's Y offset, and this mirror faithfully reproduced that split. Both ends are now
        // hung off the row's top edge: the line's BOTTOM sits one DecisionGap above it, which is
        // exactly the prompt reference (the board's lower edge). The label is centre-anchored, so
        // half its authored height converts that edge into its seat. Built empty and hidden;
        // filled from the wire-driven variant on every refresh (SetDecisionPrompt).
        //
        // …AND ITS TOP EDGE IS THE CEILING (ModBuild 91). The owner's line is the TOPMOST element of
        // a prompt that has one, so it takes the area ceiling and their row hangs one DecisionGap
        // below its bottom edge. The label is centre-anchored, so half its authored height converts
        // that top edge into a seat.
        _decisionPrompt = BuildDecisionPrompt(new Vector3(
            DecisionMount.x + decisionOff.x,
            _decisionCeilingY - 0.5f * PromptLineHeight * decisionScale,
            DecisionMount.z + decisionOff.z), in tuning);

        // ---- slot overlays: wanted pulse + snap glow ------------------------------------------
        // Centred on the CARD positions handed in by the board (the real recess anchors when the
        // 3D asset is up) PLUS the authored per-board SLOT-OVERLAY seat — see SlotOverlayLocal for
        // why dropping that term is what pushed every overlay off-centre inside the recess.
        //
        // THE HALF-POKE DIVIDER IS GONE (this round, deliberately, zero wire). A third quad used to
        // be drawn across the middle of every face-up round card here — a hairline standing in for
        // the HalfSelection poke zones, on the argument that "a literally faithful copy would draw
        // nothing at all and the element would be missing from the peer's board". Under the 1:1
        // ruling that argument inverts: the local zones are INVISIBLE BY DESIGN ("the game's own
        // on-card highlight is the only hover/selection feedback"), so drawing nothing is not a
        // missing element, it IS the element — and the divider was a widget every peer could see
        // and the owner could not. The half states themselves are not lost: hover and click both
        // ride record 14 and are drawn as the game's own two-state on-card highlight, which is
        // exactly what the owner sees. Deleted rather than gated, because there is no owner-side
        // state that could gate it: the zones are never visible.
        for (int i = 0; i < 2; i++)
        {
            Vector3 card = (i == 0 ? slot0CardLocal : slot1CardLocal) + SlotOverlayLocal(in tuning, i);
            // SIZE = the OWNER's [Cards] SlotOverlayScale_{board} (tuning field 171), not the 1.36 /
            // 1.24 literals this used to carry. Those literals were the local build's, and the local
            // build no longer has them: since 2026-08-11 the wanted-glow is EXACTLY the size of the
            // card that lands in it (one dial for both — user: "exakt ausfüllen") and the snap glow
            // keeps the shipped ratio to it. A peer who left the dial alone sees precisely what
            // shipped; a peer who moved it sees their own overlay and their own card in register,
            // which is the tuning guarantee. _slotFrameW is the owner's CardWidth × SlotScale from
            // record 11, so this is the same product as the local quad's.
            float wantedScale = tuning.SlotOverlayScale;
            _wanted[i] = BuildSlotGlow($"WantedGlow{i}", card, wantedScale, -0.003f,
                new Color(0.25f, 0.85f, 0.60f, 0.70f), pulse: true);
            _snap[i] = BuildSlotGlow($"SnapGlow{i}", card, wantedScale * Cards.PlayTray.SnapGlowRatio, -0.005f,
                new Color(1f, 0.85f, 0.30f, 0.95f), pulse: false);
            // (The mirrored SlotSeatLiner used to be built here. RETIRED with the owner's own —
            //  see Cards/PlayTray.4.Slots.cs 'recess seat liner: RETIRED'.)
        }

        ApplyLabels();
        StripColliders(_root.gameObject, "RemoteBoardFurniture");
    }

    // ---------------------------------------------------------- per-family shape dispatch --
    //
    // TWO SMALL HELPERS RATHER THAN FOUR INLINE TERNARIES, because the trap they exist to avoid is
    // the two branches drifting apart: a shape dial whose Square branch takes a size the Round
    // branch does not is a peer seeing a cap that its owner never can, and the whole point of
    // wiring a shape is that BOTH members are drawable. Each helper states the local builder it
    // reproduces so the pair can be checked against one place.

    /// <summary>
    /// One cap of the generic Confirm / Undo / item-USE column, in the OWNER's [Cards]
    /// GenericButtonShape_{board}. Reproduces <c>PlayTray.BuildButtons</c> term for term: both
    /// branches take the same depth and travel, and both take the SEAT-FITTED W/H solved in the
    /// constructor — the ROUND branch's diameter is <c>min(W, H)</c>, because a disc has to fit the
    /// recess in BOTH axes and the button recesses are wider than they are tall on all three boards.
    /// (It used to be WIDTH alone, on the reading that [BoardButtons] Width sizes both shapes since
    /// [Cards] ConfirmUndoSize_{board} was retired. That is still where the number comes from; it
    /// was only ever correct because nothing constrained it, and the local builder has the same
    /// min() now.)
    /// </summary>
    private InertCap GenericCap(Transform parent, string name, Vector3 localPos,
                                Color rest, Color accent, in CapLabelStyle labels,
                                Cards.CapRole role) =>
        _genericShape == Cards.ButtonShape.Round
            ? InertCap.Round(parent, name, localPos, Mathf.Min(_boardCapW, _boardCapH), _boardCapD, rest,
                             BoardCapTint, labels, travel: _boardCapTravel, accent: accent,
                             role: role, style: _style)
            : InertCap.Square(parent, name, localPos, new Vector2(_boardCapW, _boardCapH),
                              _boardCapD, rest, BoardCapTint, labels,
                              travel: _boardCapTravel, accent: accent,
                              role: role, style: _style);

    /// <summary>
    /// One of the short/long rest keycaps, in the OWNER's [Cards] RestButtonShape_{board}.
    /// Reproduces <c>RestControls.EnsureBuilt</c>'s split: a ROUND disc keeps the per-board
    /// DIAMETER (<paramref name="diameter"/>, itself the owner's [Cards] RestButtonDiameter_{board}
    /// off the wire) while a SQUARE cap takes the [RestButtons] Width/Height — ids 99..100, which
    /// were parked behind this very branch until it existed.
    /// </summary>
    private InertCap RestCap(Transform parent, string name, Vector3 localPos, float diameter,
                             Color accent, in CapLabelStyle labels, Cards.CapRole role) =>
        _restShape == Cards.ButtonShape.Round
            ? InertCap.Round(parent, name, localPos, diameter, _restCapD, CapIdleColor(_style),
                             RestCapTint, labels, travel: _restCapTravel, accent: accent,
                             role: role, style: _style)
            : InertCap.Square(parent, name, localPos, new Vector2(_restCapW, _restCapH),
                              _restCapD, CapIdleColor(_style), RestCapTint, labels,
                              travel: _restCapTravel, accent: accent,
                              role: role, style: _style);

    // ---------------------------------------------------------------- refresh --

    /// <summary>
    /// Re-read everything knowable and repaint what changed. Called on the shared 4 Hz content
    /// cadence from <see cref="RemoteControlBoard"/> — with or without an actor (the synced
    /// board-UI state below is wire-fed, so an actorless peer's board still mirrors its owner's
    /// controls; only the slot-occupancy-derived pieces need the actor-fed slot flags).
    ///
    /// <paramref name="slotMask"/> says which of that peer's card slots currently hold a card — the
    /// mask <c>RemoteControlBoard.SeatSlots</c> already resolved for the slots themselves, handed
    /// down rather than re-derived, so nothing here can disagree with the cards or leak anything
    /// the board does not already show. It feeds ONE thing now: the LEGACY snap-glow fallback for a
    /// sender that carries no board-UI record (a synced sender's gold rim follows their actual
    /// hover — see the snap block below).
    ///
    /// <para>The <c>showFronts</c> / <c>faceMask</c> pair this method used to take is GONE with the
    /// half-card divider that was their only consumer (see the ctor's slot-overlay block for why
    /// that widget was deleted). The reveal gate itself is untouched — it still governs the CARDS,
    /// upstream in <see cref="RemoteControlBoard"/>, exactly as before.</para>
    /// </summary>
    public void Refresh(CPlayerActor? actor, RemoteAvatar owner, int slotMask)
    {
        bool slot0 = (slotMask & 1) != 0;
        bool slot1 = (slotMask & 2) != 0;
        // Diagnostic label only — the see-through driver decides everything else from geometry.
        _fade?.Note(owner.PlayerId);
        // A language switch invalidates every cached label (the local board self-heals the same way).
        string lang = Loc.CurrentLanguage;
        if (lang != _langShown)
        {
            _langShown = lang;
            ApplyLabels();
        }

        // ---- SYNCED BOARD-UI (extension record 4 — defect 5 "genau die Buttons, die der
        //      Besitzer sieht"). When the owner broadcasts their live control visibility, the
        //      caps mirror it EXACTLY: confirm/undo/use/rest/skip appear and disappear on this
        //      board in the same frames they do on the owner's (5 Hz + on-change). A sender that
        //      predates the record (HasBoardUi false) gets the legacy always-drawn furniture, so
        //      nothing regresses cross-version.
        bool synced = owner.HasBoardUi;
        int buttons = synced ? owner.BoardButtonsMask : -1;
        if (buttons != _shownButtonsMask)
        {
            _shownButtonsMask = buttons;
            // THE SHOW/HIDE ANIMATION IS ZERO-WIRE (this round's first gap). Locally a keycap does
            // not pop: it crumbles into a sideways dust burst on the way out and assembles back out
            // of that dust on the way in (PlayTray.BoardButton.SetVisible). Every peer saw a pop —
            // yet the transition itself was ALREADY synced, right here, by the visibility bits.
            // So the same two animations are simply played on this copy off the same edge. Nothing
            // new goes on the wire, and nothing on this board becomes interactive: the animator is
            // a rendering component (see RemoteCapFx).
            //
            // Suppressed on the FIRST refresh (_settled): the caps are built shown, so the first
            // application of the owner's real mask is state SEEDING, not a transition the owner
            // made — the same reason the local button silences its own animation until it has
            // ticked once.
            bool animate = _settled;
            if (synced)
            {
                _confirm.SetShown((buttons & NetProtocol.BoardUiConfirmBit) != 0, animate);
                _undo.SetShown((buttons & NetProtocol.BoardUiUndoBit) != 0, animate);
                _shortRest?.SetShown((buttons & NetProtocol.BoardUiShortRestBit) != 0, animate);
                _longRest?.SetShown((buttons & NetProtocol.BoardUiLongRestBit) != 0, animate);
                // The rest ENGRAVINGS go with their discs. A caption for a control that is not
                // there is a word on an empty patch of board, and the owner's board hides the pair
                // together for exactly that reason. Plain SetActive rather than the dust dissolve:
                // the caps crumble because they are objects standing ON the board, and a cut IN the
                // board is not an object — while there is nothing to name, it is simply not carved.
                SetShown(_shortRestEngraving, (buttons & NetProtocol.BoardUiShortRestBit) != 0);
                SetShown(_longRestEngraving, (buttons & NetProtocol.BoardUiLongRestBit) != 0);
                _skip.SetShown((buttons & NetProtocol.BoardUiSkipBit) != 0, animate);
                SetItemUseShown((buttons & NetProtocol.BoardUiItemRecessBit) != 0, animate);
                // The decision drawer: drawn only while a prompt is actually docked on the
                // owner's board — an idle local board shows nothing at that mount.
                SetShown(_decision, (buttons & NetProtocol.BoardUiDecisionBit) != 0);
            }
            else
            {
                // Legacy sender: the pre-record look (everything drawn, drawer always out).
                _confirm.SetShown(true, animate);
                _undo.SetShown(true, animate);
                _shortRest?.SetShown(true, animate);
                _longRest?.SetShown(true, animate);
                SetShown(_shortRestEngraving, true);
                SetShown(_longRestEngraving, true);
                _skip.SetShown(true, animate);
                SetItemUseShown(true, animate);
                SetShown(_decision, true);
            }
        }

        // ---- SYNCED CAP STATES (record 4 byte 2 — "every cap looks enabled and un-accented") --
        ApplyCapStates(owner);

        // ---- SYNCED CAP PRESS (record 14 byte 0 bits 3..7) ------------------------------------
        ApplyCapPress(owner);

        // ---- SYNCED CAP LABELS (wire record 13 — task 4 "der Button-Text muss immer korrekt
        //      synchronisiert sein"). The owner's actually-displayed CONFIRM/SKIP wording, shown
        //      verbatim in their language; absent record = the neutral ApplyLabels fallback.
        SetCapLabels(owner);

        // ---- SYNCED DECISION DISPLAY (wire records 12 + 23 — task 2 "die Entscheidungsbuttons
        //      1:1", extended by the 2026-08-08 ruling to the WHOLE decision display: "alle
        //      Interaktionen, Animationen und Anzeigen des Controllboards … so wie der Spieler sie
        //      sieht"). Three passes, in the order the owner's own dock builds them:
        //        • the LABELS of the row their dock really shows, as inert plates at the same
        //          bar-anchored seat (record 12; without labels the captioned idle drawer stands in);
        //        • their per-option STATES — greyed / dimmed / chosen (record 23), repainted without
        //          rebuilding the row, because those move on every click while the wordings do not;
        //        • the PROMPT TEXT above the plates, composed HERE from the record's variant id and
        //          this client's own localization (the words never ride the wire — see
        //          RemoteDecisionPrompt).
        //      All three vanish together the moment the owner's row does — including when they
        //      focus another character and their own board goes blank at this seat.
        //
        //      SINCE ModBuild 105 THE FIRST PASS IS A FALLBACK, NOT THE MAIN PATH (user report
        //      2026-08-09: "Die Entscheidungsbuttons sollen auch 1:1 aussehen … Es sah so aus als
        //      wären die Buttons und der Text eigens nachgebaut und hier nicht die Spielelemente
        //      genutzt"). For the take-damage prompt this board now clones THIS CLIENT'S OWN
        //      TakeDamagePanel widgets and drives them from wire record 29 — real button art, real
        //      damage/fatal icons, real damage number, every wording in the VIEWER's own language
        //      (see RemoteDecisionWidgets). The mod-drawn plates below are what stands in when that
        //      cannot be done: a prompt whose widgets do not exist on a peer (the short-rest Yes/No,
        //      a DialogPopup), or a sender predating record 29.
        bool realWidgets = _decisionWidgets != null && _decisionWidgets.Refresh(owner);
        SetDecisionLines(realWidgets ? null : owner.DecisionLines);
        ApplyDecisionOptionStates(realWidgets ? null : owner.DecisionOptionStates);
        // The idle drawer is SetDecisionLines' own "a prompt is docked but I have no labels" look;
        // with the real widgets up it would sit behind them, so it is forced down here.
        if (realWidgets && _drawerIdle != null && _drawerIdle.gameObject.activeSelf)
            _drawerIdle.gameObject.SetActive(false);
        SetDecisionPrompt(actor, owner, realWidgets);

        // ---- SYNCED USE-BAR DRAWER (wire record 25 — the SECOND drawer, the same 2026-08-08
        //      ruling). Two passes, the same structure/state split the decision row uses:
        //        • the bar STRUCTURE — which of the four bars the owner has up and how many slots
        //          each shows — as inert tile rows below the mirrored decision row;
        //        • their per-slot STATES (offered / dimmed / chosen) and each bar's open
        //          element/option sub-picker, repainted without rebuilding the rows.
        //      Both vanish the moment the owner's bars do — including when the owner focuses
        //      another character and a bar render-hides on their own board.
        SetUseBars(owner);
        ApplyUseBarStates(owner);
        // …AND THE SLOT SYMBOLS (user 2026-08-13: "alle anderen Dinge wie entscheidungen wegen
        // Gegenständen etc. sieht man nur eine box … das gleiche Symbol vom Spiel"). A THIRD pass,
        // for the same reason the states are a second one: the game re-decorates a slot in place
        // while the bar structure stands still.
        ApplyUseBarSymbols(actor, owner);

        // ---- FOLLOW / PIN toggle (defect (a)) -------------------------------------------------
        // The owner's tray anchor mode now rides the board-UI record (byte 1 bit 2), so this cap
        // shows their ACTUAL state instead of one fixed look: "FIXIERT"/"PINNED" on the accented
        // brass cap while their board is world-anchored, "FOLGEN"/"FOLLOW" on the parchment idle
        // cap while it follows their rig — the same label/colour pair their own BoardButton wears
        // (PlayTray: SetLabel(follow/pinned) + SetState(accent: !TrayFollow)). A sender that
        // predates the bit reads as FOLLOW, which is the look every previous build already drew.
        SetPinned(owner.TrayPinned);

        // ---- item-use USE cap -----------------------------------------------------------------
        // SYNCED: the USE cap is its own wire bit (it exists on the owner's board only while a
        // card is clipped into the recess). LEGACY (pre-record sender): the old knowable proxy —
        // armed while their item fan is open.
        //
        // THE RECESS'S OWN "ARMED" REPAINT IS GONE, and its absence is parity rather than a loss.
        // This branch used to swing a glow-rim material between a bright and a dim colour, mirroring
        // a local look that no longer exists: since the 2026-08-09 re-art the owner's berth is an
        // open two-tone outline whose material is written once at build and never again (their
        // `_itemUseSlotGlow` is the field quad's material and nothing reads it back). Their berth
        // therefore does not change appearance when a card is clipped in — the armed state is
        // carried by the USE keycap alone — so neither may its mirror.
        bool armed = synced
            ? (buttons & NetProtocol.BoardUiItemUseCapBit) != 0
            : owner.ItemCardCount > 0;
        if (armed != _shownArmed)
        {
            _shownArmed = armed;
            // The USE cap is a real keycap that appears and disappears with the item decision, so
            // it takes the same mirrored dust transition as the rest of the column.
            _use.SetShown(armed, _settled);
        }

        // ---- wanted-slot pulse ----------------------------------------------------------------
        // SYNCED (defect 4 "das Blinken soll synchron sein"): the owner's live wanted-glow mask
        // rides the board-UI record, so the teal rim pulses on exactly the slots the owner's own
        // board pulses — including every local gate (pick flows, short-rest choice, overlay gate)
        // this board could never re-derive. The blink ANIMATION stays on the local clock at the
        // shared 3.2 rad/s period (RemoteGlowPulse == PlayTray.SlotPulse): synced state, locally
        // animated, zero per-frame traffic.
        //
        // LEGACY senders keep the old derivation: "slot empty during the secret selection phase".
        //
        // ANTI-CHEAT: this reveals nothing, on two independent grounds. (a) It is the strict
        // COMPLEMENT of what this very board already draws — an occupied slot already shows a card
        // BACK during selection, so "empty" was already visible. (b) VANILLA BROADCASTS THE SAME
        // FACT ANYWAY, twice over: the multiplayer ready tracker shows a per-character ready marker
        // for the whole selection phase (UIScenarioMultiplayerController.ShowReadyTracker →
        // UIReadyTrackerBar.RefreshReady → UIReadyTracker.ShowReady), and the hand tabs print every
        // player's live "selected/2" count with no IsUnderMyControl gate
        // (CardsHandManager.OnSelectedCardsNumberChanged ← the bolt StartRoundCards replication).
        // No card IDENTITY is involved here and the synced mask carries none either.
        int wantedMask;
        if (synced)
        {
            wantedMask = owner.WantedGlowMask;
        }
        else
        {
            bool selecting = RevealGate.InScenario && RevealGate.IsSecretSelectionPhase;
            wantedMask = 0;
            if (selecting)
            {
                if (!slot0) wantedMask |= 1;
                if (!slot1) wantedMask |= 2;
            }
        }
        SetWanted(wantedMask);

        // ---- snap glow: the HOVER TELEGRAPH, synced ------------------------------------------
        // THE COMMENT THAT USED TO STAND HERE SAID "a remote hover is not reproduced (and cannot
        // be)". Both halves of that were wrong, and the sentence is what kept the defect alive.
        //
        // It is not the same event. Locally the gold rim is a HOVER telegraph — "the held card
        // lands HERE on release" (PlayTray.SetHighlightedSlot ← CardsDriver.UpdateSlotHighlight):
        // the owner sees it while they are still holding the card, it FOLLOWS their hand across the
        // two recesses, and it goes out again if they pull away without dropping. This mirror lit
        // its rim on the model's empty→occupied edge instead and faded it after 0.6 s, so a peer
        // saw the telegraph AFTER the drop it was telegraphing, and never at all for a hover that
        // ended without one.
        //
        // And it was never impossible: the hovered recess is one small integer the owner's own
        // board already renders, and the record it belongs in had two RESERVED bits sitting in the
        // very byte the wanted-glow mask rides. It costs zero extra bytes (board-UI byte 1 bits
        // 6..7). Anti-cheat is unchanged and strictly weaker than the occupancy nibble two bits
        // below it: a recess POSITION for a card the peer is already watching the owner carry.
        //
        // LEGACY senders (no board-UI record at all) keep the old occupancy-edge flash, so an
        // old peer's board renders exactly as it always did.
        int snapMask;
        if (synced)
        {
            int hovered = owner.SnapGlowSlot;
            snapMask = hovered >= 0 && hovered < 2 ? 1 << hovered : 0;
            // Keep the occupancy edge detector fed so a mid-session fallback (a sender that stops
            // carrying the record) resumes from a truthful state rather than re-flashing history.
            _wasFilled[0] = slot0;
            _wasFilled[1] = slot1;
        }
        else
        {
            snapMask = 0;
            for (int i = 0; i < 2; i++)
            {
                bool filled = i == 0 ? slot0 : slot1;
                if (filled && !_wasFilled[i])
                    _snapLitAt[i] = Time.unscaledTime;
                _wasFilled[i] = filled;
                if (Time.unscaledTime - _snapLitAt[i] < SnapGlowSeconds)
                    snapMask |= 1 << i;
            }
        }
        SetSnap(snapMask);

        _settled = true;

        StateLine = $"use={(armed ? "armed" : "idle")}, " +
                    $"buttons={(synced ? "0x" + owner.BoardButtonsMask.ToString("X2") : "legacy")}, " +
                    $"capStates={(owner.HasCapStates ? "0x" + owner.CapStateMask.ToString("X2") : "legacy")}, " +
                    $"wanted={wantedMask}{(synced ? "(synced)" : string.Empty)}, " +
                    $"snap={snapMask}{(synced ? "(hover)" : "(occupancy edge)")}, " +
                    $"tray={(owner.TrayPinned ? "PINNED" : "FOLLOW")}{(synced ? "(synced)" : "(default)")}, " +
                    $"capLabels[confirm={(owner.ConfirmCapLabel != null ? "'" + owner.ConfirmCapLabel + "'" : "neutral")}, " +
                    $"skip={(owner.SkipCapLabel != null ? "'" + owner.SkipCapLabel + "'" : "neutral")}, " +
                    $"undo={(owner.UndoCapLabel != null ? "'" + owner.UndoCapLabel + "'" : "neutral")}, " +
                    $"use={(owner.ItemUseCapLabel != null ? "'" + owner.ItemUseCapLabel + "'" : "neutral")}], " +
                    // WHICH RENDERER IS DRAWING THE DECISION — the one fact a "1:1 sieht falsch aus"
                    // report needs from a hardware log without a screenshot. 'GAME WIDGETS' means
                    // this board shows a clone of THIS client's own TakeDamagePanel (records
                    // 12/24/29, the ModBuild 105 path); 'plates' means the mod-drawn fallback, and
                    // it says WHY.
                    $"decision={(_decisionWidgets != null && _decisionWidgets.Showing ? "GAME WIDGETS (cloned, records 12/24/29)" : "plates: " + (_decisionWidgets != null ? _decisionWidgets.Reason : "-"))}" +
                    $", plates={(_shownDecisionLines != null ? _shownDecisionLines.Split('\n').Length + " synced button(s)" : "drawer")}" +
                    $"[{DescribeStates(_shownOptionStates, _decisionPlates.Count)}]" +
                    // Single quotes around the line, like the cap labels above: a nested \" inside
                    // an interpolation hole trips the patch-inventory source scanner.
                    $", prompt={(_shownPromptText != null ? "'" + StripRichText(_shownPromptText) + "'" : "none")}" +
                    $", useBars={(owner.UseBarsMask == 0 ? "none" : "0x" + owner.UseBarsMask.ToString("X2") + " (" + _useBarRows.Count + " row(s))")}";
    }

    /// <summary>
    /// THE ENGRAVED BOARD CAPTIONS on a peer's board — the words for the three controls whose caps
    /// carry a symbol and no text. Null on a bundle with no keycap atlas, where those caps keep
    /// their own labels and there is nothing to engrave.
    ///
    /// <para>They are cut, positioned and lettered by the OWNER'S OWN builder
    /// (<c>Cards.BoardEngraving</c>) from the OWNER'S OWN strings, at offsets that live in that one
    /// class. There is no second recipe and no mirrored constant to drift — which matters here more
    /// than anywhere, because the failure mode is silent: an engraving 4 mm out of place looks fine
    /// until somebody puts the two boards side by side.</para>
    /// </summary>
    private readonly TMPro.TextMeshPro? _shortRestEngraving, _longRestEngraving, _pinEngraving;

    /// <summary>Last applied FOLLOW/PIN state (null = nothing written yet, so the first refresh
    /// always states it). Change-gated because both writes it drives — a TMP label and three
    /// material colours — are exactly the per-tick churn the 4 Hz cadence exists to avoid.</summary>
    private bool? _shownPinned;

    /// <summary>Apply the owner's tray anchor mode to the inert FOLLOW/PIN cap: the local board's
    /// own label pair (<c>Loc.Mod("follow")</c> / <c>Loc.Mod("pinned")</c>) and its own colour pair
    /// (idle parchment / accent brass). See the call site in <see cref="Refresh"/>.</summary>
    private void SetPinned(bool pinned)
    {
        if (_shownPinned == pinned)
            return;
        _shownPinned = pinned;
        // BOTH HALVES OF THE STATE, FROM ONE READ, exactly as the owner's RefreshFollowEngraving
        // does it: the SYMBOL on the cap (an anchor while pinned, two footprints while following)
        // and the WORD cut into the board beside it. The cap's own string is still written even
        // when its renderer is off — it is what a bundle without the keycap atlas falls back to,
        // and it is what this board's diagnostic line reports.
        _pin.SetLabel(pinned ? Loc.Mod("pinned") : Loc.Mod("follow"));
        _pin.SetCapRole(pinned ? Cards.CapRole.FixedPinned : Cards.CapRole.FixedFollow);
        if (_pinEngraving != null)
        {
            Cards.BoardEngraving.SetText(_pinEngraving,
                (pinned ? Loc.Mod("pinned") : Loc.Mod("follow")).ToUpperInvariant());
            Cards.BoardEngraving.Restyle(_pinEngraving, _style);
        }
        _pin.SetTint(pinned ? PinAccentColor : PinIdleColor(_style));
    }

    /// <summary>
    /// Apply the owner's SYNCED cap wordings (wire record 13): the confirm cap and the skip cap
    /// read EXACTLY what the owner's do, verbatim in their language; a null (record absent —
    /// control hidden, or a pre-record sender) falls back to the neutral localized seed. Both
    /// writes are change-gated against the WIRE value, and <see cref="ApplyLabels"/> re-arms the
    /// gates on a language switch so the fallback re-localizes without clobbering a live label.
    /// </summary>
    private void SetCapLabels(RemoteAvatar owner)
    {
        string? confirm = owner.ConfirmCapLabel;
        if (confirm != _appliedConfirmWire)
        {
            _appliedConfirmWire = confirm;
            _confirm.SetLabel(confirm ?? Loc.Game("GUI_CONFIRM", "Confirm"));
        }
        string? skip = owner.SkipCapLabel;
        if (skip != _appliedSkipWire)
        {
            _appliedSkipWire = skip;
            _skip.SetLabel(skip ?? Loc.Game("GUI_SKIP_MOVEMENT", "Skip"));
        }
        // THE TWO WORDINGS THAT NEVER TRAVELLED (record 13 mask bits 2/3, new this round).
        //   • UNDO carries the pick flow's dialog-CANCEL override while the event-discard confirm
        //     dialog is open ("Wähle eine andere Karte") — in that flow this cap IS the popup's
        //     second button, and every peer read a flat "Rückgängig" instead.
        //   • The item-USE cap carries an item-SURRENDER demand's own wording, precisely so that
        //     "the user must never read a surrender as an ordinary use" — and the mirror wrote a
        //     hardcoded GUI_USE, so peers watching a player hand an item over saw them USE it.
        string? undo = owner.UndoCapLabel;
        if (undo != _appliedUndoWire)
        {
            _appliedUndoWire = undo;
            _undo.SetLabel(undo ?? Loc.Game("GUI_UNDO", "Undo"));
        }
        string? use = owner.ItemUseCapLabel;
        if (use != _appliedUseWire)
        {
            _appliedUseWire = use;
            _use.SetLabel(use ?? Loc.Mod("item_use_area").ToUpperInvariant());
        }
    }

    /// <summary>
    /// Apply the owner's live cap STATES (board-UI record byte 2): the CONFIRM cap's accent /
    /// confirmed look, both rest discs' enabled + accent pair, and the SKIP cap's interactability.
    ///
    /// <para>WHAT A PEER USED TO SEE. Every mirrored cap wore the ONE colour it was constructed
    /// with, for the whole session. A greyed-out rest disc, a rest disc the owner had SELECTED and
    /// an available one were the same picture; so were a brass-accented pick-flow CONFIRM and a
    /// gold "you are ready, press to revoke" CONFIRM; and a dead SKIP looked pressable. The local
    /// caps have four looks (<c>BoardButton.SetState</c> → <c>StateColor</c>) and the cluster cap
    /// two, all of which this now resolves out of the same palette in the same precedence.</para>
    ///
    /// <para>WHAT IS DELIBERATELY NOT HERE: UNDO and the item-USE cap. Every <c>SetState</c> call
    /// on them in the whole mod is a constant — <c>(enabled, !accent)</c> for UNDO,
    /// <c>(enabled, accent)</c> for USE — so their look is a BUILD fact and is applied by the
    /// constructor for zero bits. The FOLLOW/PIN cap's accent is byte 1 bit 2 and is applied by
    /// <see cref="SetPinned"/>, where it has ridden since the pinned bit shipped.</para>
    ///
    /// <para>A sender without the byte (<c>HasCapStates</c> false) leaves every cap exactly where
    /// the constructor put it.</para>
    /// </summary>
    private void ApplyCapStates(RemoteAvatar owner)
    {
        int states = owner.HasCapStates ? owner.CapStateMask : -1;
        if (states == _shownCapStates)
            return;
        _shownCapStates = states;
        if (states < 0)
            return;
        _confirm.SetCapState(
            enabled: true, // the board HIDES an unpressable confirm rather than greying it
            accent: (states & NetProtocol.BoardUiCapConfirmAccentBit) != 0,
            confirmed: (states & NetProtocol.BoardUiCapConfirmReadyBit) != 0);
        _shortRest?.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapShortRestEnabledBit) != 0,
            accent: (states & NetProtocol.BoardUiCapShortRestAccentBit) != 0,
            confirmed: false);
        _longRest?.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapLongRestEnabledBit) != 0,
            accent: (states & NetProtocol.BoardUiCapLongRestAccentBit) != 0,
            confirmed: false);
        // THE SKIP CAP IS AN ORDINARY BOARD KEYCAP NOW, so its dead look is the board keycaps' own
        // disabled plaque rather than the retired cluster's accent-preserving lerp toward dark wood.
        // Kept ACCENTED while enabled: the owner's TickStatus shows this cap only while the game is
        // showing the control, and drives it with SetState(CanSkip(), accent: false) — the accent
        // here is what carries its authored brass, exactly as the item-USE cap's does.
        _skip.SetCapState(
            enabled: (states & NetProtocol.BoardUiCapSkipEnabledBit) != 0,
            accent: true,
            confirmed: false);
        VRLog.Info("Net", $"Remote cap states applied: 0x{states:X2} — confirm=" +
                          ((states & NetProtocol.BoardUiCapConfirmReadyBit) != 0 ? "CONFIRMED"
                              : (states & NetProtocol.BoardUiCapConfirmAccentBit) != 0 ? "accent"
                              : "idle") +
                          ", shortRest=" +
                          ((states & NetProtocol.BoardUiCapShortRestEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          ((states & NetProtocol.BoardUiCapShortRestAccentBit) != 0 ? "+accent" : string.Empty) +
                          ", longRest=" +
                          ((states & NetProtocol.BoardUiCapLongRestEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          ((states & NetProtocol.BoardUiCapLongRestAccentBit) != 0 ? "+accent" : string.Empty) +
                          ", skip=" +
                          ((states & NetProtocol.BoardUiCapSkipEnabledBit) != 0 ? "enabled" : "DIMMED") +
                          " (board-UI record byte 2). Colours only — the caps stay colliderless.");
    }

    /// <summary>
    /// Replay the owner's keycap PRESS on this copy (record 14 byte 0 bits 3..7). The wire field is
    /// a LATCH that rides several packets per press, so the dip plays when the (cap, sequence) pair
    /// CHANGES, never merely when it is set — otherwise the same press would replay on every packet
    /// of its hold window.
    ///
    /// <para>The first refresh SEEDS the key without animating: a board built while a press is
    /// still latched must not open with a dip for something that already happened.</para>
    ///
    /// <para>An ANIMATION, not an interaction: the mapped cap sinks and springs back and nothing is
    /// invoked. No collider, no registration, nothing added to the copy at all — the dip is a
    /// transform write inside <see cref="RemoteCapFx"/>.</para>
    /// </summary>
    private void ApplyCapPress(RemoteAvatar owner)
    {
        int key = owner.CapPressKey;
        if (key < 0 || key == _playedPressKey)
            return;
        bool seed = !_settled;
        _playedPressKey = key;
        if (seed)
            return;
        byte cap = (byte)(key & 0xFF);
        InertCap? target = cap switch
        {
            NetProtocol.CapPressConfirm => _confirm,
            NetProtocol.CapPressUndo => _undo,
            NetProtocol.CapPressItemUse => _use,
            NetProtocol.CapPressShortRest => _shortRest,
            NetProtocol.CapPressLongRest => _longRest,
            NetProtocol.CapPressSkip => _skip,
            NetProtocol.CapPressFollowPin => _pin,
            _ => null,
        };
        if (target == null)
            return;
        target.Press();
        VRLog.Info("Net", $"Remote cap press animated: wire cap {cap} (sequence {(key >> 8) & 0x03}) " +
                          "— the mirrored cap sinks its full authored travel and springs back at the " +
                          "owner's own decay rate. Nothing was invoked and nothing became pressable: " +
                          "the copy is still a picture of a button.");
    }

    /// <summary>Change-safe activeSelf flip for a plain furniture root.</summary>
    /// <summary>Show/hide one engraved caption. Change-gated: this runs on the 4 Hz content
    /// cadence and a SetActive that is already right is exactly the churn that cadence exists to
    /// avoid.</summary>
    private static void SetShown(TMPro.TextMeshPro? label, bool shown)
    {
        if (label != null && label.gameObject.activeSelf != shown)
            label.gameObject.SetActive(shown);
    }

    private static void SetShown(Transform root, bool shown)
    {
        if (root != null && root.gameObject.activeSelf != shown)
            root.gameObject.SetActive(shown);
    }

    /// <summary>
    /// Show/hide the mirrored item-use BERTH — the receiver-side twin of
    /// <c>PlayTray.SetItemUseSlotVisible</c>, and the reason this is not the plain
    /// <see cref="SetShown"/> the recess used to get.
    ///
    /// <para>NOTHING POPS, on the peer's board either. The owner's berth grows in with a back-ease
    /// overshoot and collapses out; a mirror that blinked on the same edge would be showing a
    /// different widget. The edge itself is already exact: <c>PlayTray.ItemUseSlotShown</c> reports
    /// the LOGICAL state (<c>_itemUseSlotWanted</c>) rather than the root's <c>activeSelf</c>,
    /// specifically so <see cref="NetProtocol.BoardUiItemRecessBit"/> flips when the owner's own
    /// animation STARTS — not when it finishes, which is when an activeSelf-derived bit would have
    /// flipped on the way out and would have left this copy a fifth of a second late.</para>
    ///
    /// <para><paramref name="animate"/> is the furniture's usual first-refresh suppression: the
    /// recess is BUILT shown, so the first application of the owner's real mask is state SEEDING,
    /// not a transition they made — it snaps, exactly as the keycaps skip their dust burst on the
    /// same frame.</para>
    ///
    /// <para>ON HIDE THE ROOT STAYS ACTIVE FOR THE LENGTH OF THE COLLAPSE, because
    /// <see cref="WorldUI.SoftCueReveal"/> switches it off at the END of the out-animation (a
    /// component cannot animate its own disappearance from inside a deactivated GameObject). The one
    /// consumer that reads this root's <c>activeInHierarchy</c> — <see cref="RemoteItemFan"/>'s clip
    /// resolver, which decides whether the owner's placed card may lie in the recess — therefore
    /// sees it true a moment longer. That is the correct direction: the card's own authority is the
    /// owner's <c>ItemUseClipIndex</c> (record 26), which goes to −1 on the same edge, so the card
    /// flies home while the berth collapses under it rather than being orphaned in mid-air by a
    /// recess that vanished first.</para>
    /// </summary>
    private void SetItemUseShown(bool shown, bool animate)
    {
        if (_itemUse == null)
            return;
        bool active = _itemUse.gameObject.activeSelf;

        // SEEDING PASS — deliberately NOT change-gated, and that is the point. The berth is built
        // ACTIVE but its reveal has never run, so it is drawn at the authored size while the
        // component still believes it is at phase 0. Skipping the seed because "shown already
        // matches" would leave that disagreement in place, and the FIRST real Hide() would then
        // collapse from phase 0 — i.e. deactivate in one frame, which is the pop this whole method
        // exists to remove. Snapping here makes the component's state and the picture agree before
        // any owner edge can arrive.
        if (!animate)
        {
            _itemUseWanted = shown;
            if (active != shown)
                _itemUse.gameObject.SetActive(shown);
            if (shown)
                _itemUseReveal?.SnapShown();
            return;
        }

        // Re-assert a SHOW whose root went inactive under us; the wanted flag alone would latch the
        // berth away for the rest of the owner's decision.
        if (shown == _itemUseWanted && (!shown || active))
            return;
        _itemUseWanted = shown;
        if (_itemUseReveal == null)
        {
            if (active != shown)
                _itemUse.gameObject.SetActive(shown); // shader-less fallback: no art, no reveal
            return;
        }
        if (shown)
        {
            if (!active)
                _itemUse.gameObject.SetActive(true);
            _itemUseReveal.Show();
        }
        else
        {
            _itemUseReveal.Hide(); // deactivates the root once the collapse has played out
        }
    }

    // ---------------------------------------------------------------- labels --

    /// <summary>
    /// (Re)write every cap label in the current language. All literals go through the game's own
    /// loc keys where one exists, so a peer's board reads in the local player's language exactly
    /// like their own board does.
    ///
    /// THE "NEUTRAL LOOKS" LIST THIS METHOD USED TO CARRY IS EMPTY. It declared that "CONFIRM /
    /// UNDO / SKIP / REST enabled-vs-disabled … are drawn ENABLED (the authored base colour), never
    /// dimmed", on the argument that the states are recomputed per frame on the owner's client
    /// only. That is true of the COMPUTATION and irrelevant to the RESULT: the result is four
    /// distinct colours on a labelled control, and a peer seeing one of them while the owner sees
    /// another is exactly the disagreement the 1:1 rule forbids. Seven bits of the board-UI record's
    /// cap-state byte carry every one of them now (see <see cref="ApplyCapStates"/>), read off the
    /// flags the owner's own renderer obeys.
    ///
    /// The FOLLOW/PIN toggle left that list earlier: its label and accent are SYNCED (board-UI
    /// record byte 1 bit 2) and applied in <see cref="SetPinned"/>, so this method only seeds the
    /// wording. Calling <c>[Cards] TrayFollow</c> "a private VR preference" was the mistake — it is
    /// a labelled two-state control on a board the user requires to read 1:1 like its owner's.
    /// The CONFIRM and SKIP wordings left the list the same way (user report 2026-08-04: "mein
    /// Mitspieler las 'Fortfahren', ich sehe 'Bestätigen'"): the owner's actually-displayed text
    /// rides wire record 13 and is applied in <see cref="SetCapLabels"/> — this method only seeds
    /// the no-record fallback.
    /// </summary>
    private void ApplyLabels()
    {
        _confirm.SetLabel(Loc.Game("GUI_CONFIRM", "Confirm"));
        _undo.SetLabel(Loc.Game("GUI_UNDO", "Undo"));
        // The CONFIRM/SKIP wordings are SYNCED state now (wire record 13, see SetCapLabels):
        // this method only seeds the neutral fallback, and re-arming the gates here makes the
        // next refresh re-assert whichever synced label is live in place of the reseed.
        _appliedConfirmWire = null;
        _appliedSkipWire = null;
        _appliedUndoWire = null;
        _appliedUseWire = null;
        _use.SetLabel(Loc.Mod("item_use_area").ToUpperInvariant());
        // FOLLOW/PIN is SYNCED state now (see SetPinned), so a language switch must re-state the
        // CURRENT mode's word, not the FOLLOW one — and must re-arm the change gate so the next
        // refresh re-applies it in the new language.
        _pin.SetLabel(_shownPinned == true ? Loc.Mod("pinned") : Loc.Mod("follow"));
        _shownPinned = null;
        // GUI_SKIP_MOVEMENT is the key SkipButton.Start() seeds its own label from; the live
        // button swaps in GUI_SKIP_ABILITY / GUI_SKIP_ATTACK per situation — that live wording
        // rides wire record 13 now (SetCapLabels overrides this seed whenever it is present).
        _skip.SetLabel(Loc.Game("GUI_SKIP_MOVEMENT", "Skip"));
        // Same strings the local RestControls caps wear (no game key exists for the short rest).
        _shortRest?.SetLabel(Loc.Mod("short_rest"));
        _longRest?.SetLabel(Loc.Game("GUI_LONG_REST", "Long rest"));
        // …AND THE ENGRAVINGS, in the same two strings and the same casing the owner's board cuts
        // them in. This is the half a texture could never have: a peer switching to English has to
        // see "SHORT REST" carved into their own copy of a team-mate's board, in the VIEWER's
        // language, because the engraving is a teaching aid for whoever is looking at it. (The cap
        // LABELS above are the opposite case and stay as they are: those carry the OWNER's live
        // wording off wire record 13, because they say what that player's press will do.)
        Cards.BoardEngraving.SetText(_shortRestEngraving, Loc.Mod("short_rest").ToUpperInvariant());
        Cards.BoardEngraving.SetText(_longRestEngraving,
            Loc.Game("GUI_LONG_REST", "Long rest").ToUpperInvariant());
        if (_shortRestEngraving != null) Cards.BoardEngraving.Restyle(_shortRestEngraving, _style);
        if (_longRestEngraving != null) Cards.BoardEngraving.Restyle(_longRestEngraving, _style);
        if (_pinEngraving != null) Cards.BoardEngraving.Restyle(_pinEngraving, _style);
    }

    // ---------------------------------------------------------------- sub-builders --

    // ---- the mirrored item-use BERTH's geometry (mirror of PlayTray.4.Slots' own constants) ----

    /// <summary>The berth OUTLINE's rectangle, as a factor of the card box — mirror of
    /// <c>PlayTray.ItemBerthRectFactor</c>. Just outside the 1.04× clear area a placed card is
    /// fitted into (<see cref="UseSlotInnerFactor"/>) and the 0.94 of it the card actually fills,
    /// so the outline stays visible all the way round a seated card.</summary>
    private const float ItemBerthRectFactor = 1.08f;

    /// <summary>The warm FIELD inside the berth, as a factor of the card box — mirror of
    /// <c>PlayTray.ItemBerthFieldFactor</c>.</summary>
    private const float ItemBerthFieldFactor = 1.03f;

    /// <summary>Corner rounding of the berth outline as a factor of the card WIDTH — mirror of
    /// <c>PlayTray.ItemBerthCornerFactor</c>. Item cards are rounded rectangles; a berth with square
    /// corners reads as a picture frame hung around them rather than as the slot they belong in.</summary>
    private const float ItemBerthCornerFactor = 0.10f;

    // Board-local Z of the three berth layers, verbatim from PlayTray.4.Slots. +Z is INTO the board
    // on BOTH boards, so all three sit BEHIND the z = 0 plane a clipped-in card is parented at
    // (RemoteItemFan seats the mirrored card at localPosition zero on this very root) — the card
    // lies ON the berth, and the berth's own layers never fight each other for depth.
    private const float ItemBerthFieldZ = 0.0035f;
    private const float ItemBerthPingZ = 0.0030f;
    private const float ItemBerthOutlineZ = 0.0025f;

    // ---- the owner's own BERTH dials (extension record 28, ids 80 / 166..169) -------------------
    // WIRE-OVERRIDABLE FALLBACKS, exactly like RemoteHandFan's geometry and RemoteItemFan's
    // animation: the value the owner set where they moved the dial, this client's shipped constant
    // where they did not — which is the same number, so an untuned peer's berth is drawn exactly as
    // this build ships it. Seeded from the tuning in the constructor, before BuildItemUseRecess
    // reads them; a change to the owner's tuning rebuilds the whole board (RemoteControlBoard
    // compares _builtTuningRevision), so there is no later refresh to miss.
    //
    // THEY WERE `const` UNTIL THIS ROUND, and only for a capacity reason: record 28 stood at exactly
    // its 255-byte per-record ceiling when the berth re-art landed, so its five dials could not ride
    // and a peer drew the berth at the shipped defaults whatever its owner had tuned. The paging
    // round removed the ceiling (Net/BoardTunePages.cs) and reserved these ids for exactly these
    // dials, so the reason expired and the 1:1 ruling applies with nothing left to weigh against it:
    // "Ändert ein Spieler also die Positionen für sich selber, so sollen alle anderen diese Position
    // bei seinem board auch sehen" (2026-08-09).
    //
    // Naming the Defaults entries rather than re-typing the numbers is still what keeps an untuned
    // table in agreement when a default moves; the pairs stay pinned in
    // scripts/check-remote-defaults.py, which accepts this form for that exact reason.
    private float _itemBerthRingThickness = Defaults.ItemBerthRingThickness;
    private float _itemBerthGlow = Defaults.ItemBerthGlow;
    private float _itemBerthPingSeconds = Defaults.ItemBerthPingSeconds;
    private float _itemBerthPingReach = Defaults.ItemBerthPingReach;
    private float _itemBerthRevealSeconds = Defaults.ItemBerthRevealSeconds;

    /// <summary>
    /// The item-USE clip-in BERTH plus the localized "USE" caption below it — the mirror of
    /// <c>PlayTray.BuildItemUseSlot</c> as it stands after the 2026-08-09 re-art, minus anything
    /// droppable.
    ///
    /// <para>WHAT THIS USED TO BUILD, AND WHY IT HAD TO GO. Until now it was a faithful copy of the
    /// OLD local recess: an opaque gold 1.12× frame, an opaque near-black 1.04× inner plate and a
    /// 1.28× glow quad that switched between a bright and a dim colour. The owner's side deleted
    /// exactly those three quads, and the argument for deleting them applies to this copy WORD FOR
    /// WORD, because the geometry is the same on both boards: this berth hangs BELOW the board's
    /// lower edge with nothing behind it, so in mixed reality its backdrop is the viewer's own room
    /// — and near the BLACK chroma-key preset a dark plate is not a rectangle at all, it is a hole
    /// punched through to the passthrough camera. The two play-slot recesses this composition was
    /// copied from lie ON the board's opaque slab, which is what makes their dark inner plate read;
    /// this one never had that slab. So the plate is gone here too.</para>
    ///
    /// <para>WHAT IT BUILDS NOW — the owner's four pieces, in the same generated art
    /// (<see cref="WorldUI.SoftCueArt"/>), so the two boards are the same widget:</para>
    /// <list type="number">
    /// <item>"A CARD GOES HERE" — a card-shaped, constant-thickness, rounded soft OUTLINE at the
    ///   size a card actually lands at. TWO-TONE (bright gold core, dark shoulder either side),
    ///   which is what keeps it legible over a white wall and a dark room alike: a cue drawn in ONE
    ///   tone is only visible where it differs in luminance from a background nobody controls, and
    ///   on a PEER's board that background is even less controlled than on the owner's.</item>
    /// <item>The middle filled with LIGHT rather than darkness — <c>BuildUseGhost</c>'s pale warm
    ///   wash at the berth's resting level, so the room shows through and the berth is the drop
    ///   ghost's rest state rather than a second visual idea.</item>
    /// <item>"NOW" — an INWARD <see cref="WorldUI.SoftCuePing"/> closing onto the outline: the exact
    ///   mirror of the outward ring the items pile throws ("look here" ↔ "put it in here"), and the
    ///   replacement for the border sine the old glow quad breathed on.</item>
    /// <item>An ARRIVAL and a DEPARTURE (<see cref="WorldUI.SoftCueReveal"/>). This is the half that
    ///   needed the wire, and it already had it: the owner's <c>ItemUseSlotShown</c> reports the
    ///   LOGICAL edge — what <c>SetItemUseSlotVisible</c> was last asked for — rather than the root's
    ///   <c>activeSelf</c>, precisely because the root now outlives the decision by the length of the
    ///   collapse animation. So <see cref="NetProtocol.BoardUiItemRecessBit"/> flips at the instant
    ///   the owner's own animation STARTS, and this mirror can play a matching one instead of
    ///   popping a frame late. (Verified at the source: PlayTray.4.Slots'
    ///   <c>ItemUseSlotShown => _itemUseSlot != null &amp;&amp; _itemUseSlotWanted</c>.)</item>
    /// </list>
    ///
    /// <para>THE ARMED REPAINT IS GONE WITH THE GLOW MATERIAL, and that is parity rather than a
    /// loss: the owner's berth no longer changes appearance when a card is clipped in — their
    /// <c>_itemUseSlotGlow</c> is now the field quad's material and nothing writes it after build.
    /// The armed state is carried by the USE keycap alone, on both boards.</para>
    /// </summary>
    private Transform BuildItemUseRecess(Vector3 mount, out WorldUI.SoftCueReveal? reveal)
    {
        var root = new GameObject("ItemUseRecess").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = mount;

        // EVERYTHING THAT ANIMATES HANGS OFF ONE NODE (the owner's structure, kept): the arrival and
        // the departure are then a single motion of a single object, and the ROOT keeps the plain
        // activeSelf semantics RemoteItemFan reads when it decides whether the mirrored card may lie
        // in this recess at all.
        var berthGo = new GameObject("Berth");
        berthGo.transform.SetParent(root, worldPositionStays: false);
        berthGo.transform.localPosition = Vector3.zero;
        berthGo.transform.localRotation = Quaternion.identity;
        Transform berth = berthGo.transform;
        var rev = berthGo.AddComponent<WorldUI.SoftCueReveal>();
        rev.DeactivateTarget = root.gameObject;
        rev.Configure(_itemBerthRevealSeconds);
        reveal = rev;

        float rectW = ItemCardW * ItemBerthRectFactor;
        float rectH = ItemCardH * ItemBerthRectFactor;
        float band = Mathf.Max(0.0008f, _itemBerthRingThickness);
        float corner = ItemCardW * ItemBerthCornerFactor;
        // The berth's gold, run through the chroma-key guard exactly as the owner's is, so no key
        // preset can turn a peer's berth into a hole through to their passthrough room. KeySafe
        // reads the LOCAL player's key colour, which is correct: this is drawn on their headset.
        Color berthGold = WorldUI.SoftCueArt.KeySafe(new Color(1f, 0.80f, 0.36f, 0.92f));

        // 1. THE FIELD — light in the berth, never a dark plate. (0 = a completely open berth.)
        float glow = Mathf.Clamp01(_itemBerthGlow);
        if (glow > 0.002f)
        {
            GameObject field = WorldUI.SoftCueArt.FieldQuad("Field", berth,
                new Vector3(0f, 0f, ItemBerthFieldZ),
                ItemCardW * ItemBerthFieldFactor, ItemCardH * ItemBerthFieldFactor,
                new Color(0.92f, 0.85f, 0.5f, glow)); // BuildUseGhost's own wash, at rest level
            rev.Track(field);
        }

        // 2. THE OUTLINE — the card-shaped destination itself.
        GameObject outline = WorldUI.SoftCueArt.RectOutlineQuad("Outline", berth,
            new Vector3(0f, 0f, ItemBerthOutlineZ), rectW, rectH, band, corner, berthGold);
        rev.Track(outline);

        // 3. THE INWARD PING — "put it in HERE", on the shared item beat, from 1.5× onto 1.0×.
        float pingSeconds = _itemBerthPingSeconds;
        float pingReach = Mathf.Max(1f, _itemBerthPingReach);
        if (pingSeconds > 0.01f && pingReach > 1.001f)
        {
            GameObject ping = WorldUI.SoftCueArt.RectOutlineQuad("Ping", berth,
                new Vector3(0f, 0f, ItemBerthPingZ), rectW, rectH, band, corner, berthGold);
            ping.AddComponent<WorldUI.SoftCuePing>().Init(
                ping.GetComponent<MeshRenderer>(), berthGold,
                new Vector3(rectW * pingReach, rectH * pingReach, 1f),
                new Vector3(rectW, rectH, 1f),
                pingSeconds);
        }

        // LABEL, not keycap — the mirror of the local restyle (user report 2026-08-08, "Das 'Use'
        // unten drunter erscheint eher wie ein button"): the pile captions' muted parchment tone,
        // their 0.095 × 0.024 fit box at a 0.22 ceiling, and NO bold. The peer's board must show the
        // owner's board, so the two builders keep the same numbers — the full styling rationale is
        // written once, at PlayTray.BuildItemUseSlot.
        //
        // IT HANGS OFF THE ANIMATED NODE, like the owner's: the caption grows in and collapses out
        // WITH the outline it names instead of blinking beside a widget that is animating. It rides
        // the reveal's SCALE only — SoftCueReveal deliberately never writes a TMP's colour, because
        // a TextMeshPro draws through one font-atlas material shared with every label in the game
        // and its MR backing plate through one shared plate material (see SoftCueReveal.Track).
        TextMeshPro caption = RemoteBoardContent.Label(berth, "Label",
            new Vector3(0f, -(ItemCardH * 0.5f + 0.026f), -0.001f),
            new Vector2(0.095f, 0.024f), 0.22f,
            new Color(0.85f, 0.8f, 0.7f), TextAlignmentOptions.Center);
        caption.text = Loc.Mod("item_use_area").ToUpperInvariant();
        // MR readability parity with the owner's board: the local item-use caption below the
        // recess is MrBacking.Label'd (PlayTray.4.Slots), because it hangs below the recess in
        // open air — over the passthrough room in MR. Same treatment for its mirror; the fitted
        // plate renders only while MR is on, so normal mode stays bit-identical.
        WorldUI.MrBacking.Label(caption);
        return root;
    }

    /// <summary>
    /// The SHARED DECISION DOCK mirror: the strip below the board where the take-damage burn
    /// choice, the burn-confirm dialog and every other in-scenario prompt dock their REAL widgets
    /// on the local board (<c>DecisionDockSurface</c> on <c>PlayTray.DecisionMount</c>).
    ///
    /// The root's local origin is the WIDGET-BLOCK TOP EDGE the owner's dock anchors at — since
    /// ModBuild 91 the decision area's CEILING when their prompt has no text line, and one prompt
    /// line plus one DecisionGap under it when it does (<see cref="ApplyDecisionSeat"/>) —
    /// horizontally centred like the local dock; content grows DOWN from it. Two mutually exclusive
    /// children:
    ///   • <see cref="_drawerIdle"/> — the slim captioned drawer, shown while the owner HAS a
    ///     docked prompt (board-UI decision bit) but this client holds no labels for it (legacy
    ///     sender, or a row whose labels could not be read);
    ///   • <see cref="_decisionRow"/> — the MIRRORED BUTTON ROW (wire record 12): one inert
    ///     antique plate per label the owner's dock really shows, rebuilt in
    ///     <see cref="SetDecisionLines"/>.
    /// </summary>
    private Transform BuildDecisionDrawer(Vector3 topEdgeLocal)
    {
        var root = new GameObject("DecisionDrawer").transform;
        root.SetParent(_root, worldPositionStays: false);
        root.localPosition = topEdgeLocal;

        // Idle drawer, hung with its TOP edge at the root origin (the same anchor the real
        // buttons use, so legacy and synced looks sit at the same spot).
        _drawerIdle = new GameObject("Idle").transform;
        _drawerIdle.SetParent(root, worldPositionStays: false);
        _drawerIdle.localPosition = new Vector3(0f, -0.0275f, 0f);

        MeshRenderer drawerPlate = BoardVisual.Quad(_drawerIdle, "Plate", new Vector2(0.42f, 0.055f),
            BoardVisual.Unlit(new Color(0.10f, 0.09f, 0.08f, 0.80f)));
        drawerPlate.transform.localPosition = new Vector3(0f, 0f, 0.001f);
        WorldUI.MrBacking.Opacify(drawerPlate.sharedMaterial); // 0.80 → 1 while MR is on
        // A thin lip along the top edge so the empty drawer reads as a drawer and not as a shadow.
        BoardVisual.Quad(_drawerIdle, "Lip", new Vector2(0.42f, 0.004f),
            BoardVisual.Unlit(new Color(0.36f, 0.31f, 0.20f, 1f)))
            .transform.localPosition = new Vector3(0f, 0.0275f, 0f);

        RemoteBoardContent.Label(_drawerIdle, "Caption", new Vector3(0f, 0f, -0.001f),
            new Vector2(0.36f, 0.026f), 0.05f,
            new Color(0.72f, 0.68f, 0.58f), TextAlignmentOptions.Center)
            .text = Loc.Mod("decision_dock").ToUpperInvariant();
        return root;
    }

    /// <summary>Authored height of the mirrored prompt line's label rect, board-local metres before
    /// the owner's dock scale. The seat above is solved for the line's BOTTOM edge (one DecisionGap
    /// over the mirrored row, exactly as the owner's own text hangs off their measured row top), and
    /// a centre-anchored label needs half of this to turn that edge into a position — so the number
    /// lives here instead of twice inside <see cref="BuildDecisionPrompt"/>.
    ///
    /// <para><b>0.075 -> 0.0168, AND IT IS A FALLBACK NOW RATHER THAN THE ANSWER (user report,
    /// 2026-08-15 three-player session, schadenstext_remote.jpg: "Der remote text vom Schadenstext
    /// ist größer dargestellt als beim Spieler selber, womit auch die Position der buttons nicht
    /// 1:1 richtig synchronisiert wird. Das Problem habe ich bereits einmal angesprochen.").</b>
    /// The three logs of that session measure the same sentence on both sides, converted to one
    /// unit through the board-root scale 14.66 that the two logged gaps agree on:</para>
    /// <list type="bullet">
    /// <item>OWNER: 394 mm world = <b>26.9 mm board-local</b>, ONE line, button row 55.9 mm below
    /// the ceiling.</item>
    /// <item>OBSERVER: <b>120 mm board-local reserved</b> (this constant at 0.075 times the 1.6 dock
    /// scale), the glyphs auto-sized UP into that box and wrapping to roughly three lines, button
    /// row 149 mm down. Text about 3.4x too tall, buttons about 93 mm too low.</item>
    /// </list>
    /// <para>So the box was 4.5x the height the owner's text occupies, and TMP's auto-sizing did
    /// exactly what it was told: it grew the glyphs to fill it. 26.9 mm at dock scale 1.6 is
    /// 0.0168 board-local, which is what this constant now holds.</para>
    /// <para><b>WHY IT ONLY BOUNDS THE FALLBACK.</b> The previous attempt at this report (ModBuild
    /// 105, e4ccf8c/33d56be, and a05cac7 re-measuring the row to 720x48) replaced the mod-drawn
    /// plates with clones of the game's real buttons and re-measured the ROW — it never touched the
    /// prompt TEXT, and `git log -S "0.17f * scale"` returns exactly one commit. It fixed the widget
    /// BESIDE the defect, which is why it did not hold. A second authored constant would fail the
    /// same way the first did, so the seat below no longer trusts this number at all: it measures
    /// the label's rendered height and uses that. This value only sizes the rect the text is fitted
    /// INTO, i.e. it caps how large auto-sizing may grow the glyphs, and stands in for one line
    /// before the first measurement exists.</para></summary>
    private const float PromptLineHeight = 0.0168f;

    /// <summary>The prompt label's LAST MEASURED rendered height, board-local metres AFTER the dock
    /// scale — <c>TextMeshPro.renderedHeight</c> off a forced mesh update, i.e. what the
    /// glyphs actually occupy rather than what was authored for them. Zero until the first non-empty
    /// prompt has been laid out; <see cref="ApplyDecisionSeat"/> falls back to the authored line
    /// height then. This exists because the owner's own side has always measured (DamageTooltipSurface
    /// solves its seat from the fitted rect and DecisionDockSurface hangs the buttons off the
    /// measured text bottom), and the mirror was the only one of the two using a constant.</summary>
    private float _promptMeasuredHeight;

    /// <summary>
    /// The mirrored PROMPT TEXT of the decision dock — the line the owner reads above their docked
    /// buttons ("Schadensphase: Erleide entweder Schaden, verbrenne …"), composed on THIS machine
    /// from the wire-carried variant id (see <see cref="RemoteDecisionPrompt"/> for why the text
    /// itself may never ride the wire). Built once, empty and hidden; a game-HUD-font label with
    /// the help box's own gold/grey rich-text colouring, MR-backed like every other line that hangs
    /// below the board in open air. Display-only: one TMP, no collider, nothing to press.
    /// </summary>
    private TextMeshPro BuildDecisionPrompt(Vector3 local, in RemoteBoardTuning tuning)
    {
        float scale = tuning.DecisionScale;
        TextMeshPro label = RemoteBoardContent.Label(_root, "DecisionPrompt", local,
            new Vector2(Cards.PlayTray.DecisionMountWidth * scale, PromptLineHeight * scale),
            0.17f * scale, new Color(0.82f, 0.80f, 0.76f),
            TextAlignmentOptions.Center, wrap: true);
        WorldUI.NativeButtonSkin.ApplyFont(label); // the game's HUD font, depth-honest material
        label.richText = true;                     // the help box's own gold title / grey body
        WorldUI.MrBacking.Label(label);
        label.gameObject.SetActive(false);
        return label;
    }

    /// <summary>
    /// Show (or hide) the mirrored prompt line for the owner's docked decision. The text is
    /// COMPOSED here from the wire's prompt-kind + text-variant pair and this client's own
    /// localization — never received — so the mandatory-use variant's active-bonus card names
    /// cannot travel; see <see cref="RemoteDecisionPrompt"/>. Change-gated on the composed string
    /// (a per-tick TMP write re-triggers auto-size layout).
    /// </summary>
    private void SetDecisionPrompt(CPlayerActor? actor, RemoteAvatar owner, bool realWidgets)
    {
        // No visible row on the owner's board ⇒ no line, whatever the last state record said. The
        // test is "a row of EITHER kind stands here": since ModBuild 105 the take-damage prompt is
        // normally drawn by the mirrored GAME widgets, which leaves _shownDecisionLines null.
        string? text = !realWidgets && _shownDecisionLines == null
            ? null
            : RemoteDecisionPrompt.Compose(owner.DecisionPromptKind, owner.DecisionTextVariant, actor);
        if (text == _shownPromptText)
            return;
        _shownPromptText = text;
        if (_decisionPrompt == null)
            return;
        bool show = !string.IsNullOrEmpty(text);
        if (show)
            _decisionPrompt.text = text;
        if (_decisionPrompt.gameObject.activeSelf != show)
            _decisionPrompt.gameObject.SetActive(show);
        // MEASURE, DO NOT ASSUME. The owner's side has always measured — DamageTooltipSurface solves
        // its seat from the fitted rect and DecisionDockSurface hangs the buttons off the text's
        // measured bottom — and the mirror was the only one of the two using an authored constant
        // for a sentence whose length it cannot know. That is why the buttons sat 93 mm low. TMP
        // lays out lazily, so the mesh has to be forced before renderedHeight means anything; the
        // label must be active for that, which is why this sits after SetActive.
        if (show)
        {
            _decisionPrompt.ForceMeshUpdate();
            _promptMeasuredHeight = _decisionPrompt.renderedHeight;
        }
        ApplyDecisionSeat(show);
        VRLog.Info("Net", show
            ? $"Remote decision prompt: line composed LOCALLY for prompt kind " +
              $"{owner.DecisionPromptKind} / text variant {owner.DecisionTextVariant} — " +
              $"\"{StripRichText(text!)}\" — its bottom edge one DecisionGap " +
              $"({_decisionTuning.DecisionGap * _decisionTuning.DecisionScale * 1000f:F0} mm) above " +
              $"the mirrored row's top edge at board-local y {_decision.localPosition.y:F3}, which " +
              "is the seat the owner's own HelpBox takes over their own buttons (they hang it off " +
              "the row's measured top edge at the same gap). The wire carried the VARIANT, never " +
              "the words (no card identity, ever)."
            : "Remote decision prompt: no line (no visible decision row, a prompt that has none, or " +
              "a sender predating record 23).");
    }

    /// <summary>
    /// Seat the mirrored BUTTON ROW under the area ceiling, the way the owner's own row seats itself
    /// (ModBuild 91): AT the ceiling when their prompt draws no text line, and one authored prompt
    /// line plus one <c>DecisionGap</c> below it when it does. The use-bar drawer's root stays on the
    /// ceiling and measures down from this row, so only one transform moves.
    ///
    /// <para>Rebuilding the bars is not optional when this moves: <see cref="SetUseBars"/> is
    /// change-gated on the owner's bar STRUCTURE, and its rows are laid out from the row's seat, so a
    /// seat that moved while the structure held would leave the mirrored bars behind. Dropping the
    /// structure gate forces exactly one rebuild on the frame the owner's prompt line appears or
    /// goes.</para>
    /// </summary>
    private void ApplyDecisionSeat(bool promptLineShown)
    {
        if (promptLineShown == _decisionPromptShown)
            return;
        _decisionPromptShown = promptLineShown;
        float scale = _decisionTuning.DecisionScale;
        // THE LINE HEIGHT IS MEASURED WHERE ONE EXISTS. `renderedHeight` is already in the label's
        // own board-local metres AFTER the dock scale, so it is NOT multiplied by `scale` again —
        // the authored fallback is, because it is stored pre-scale. Getting that wrong is how a
        // two-line prompt would push the row twice as far as it should.
        float lineH = _promptMeasuredHeight > 0f ? _promptMeasuredHeight : PromptLineHeight * scale;
        float y = promptLineShown
            ? _decisionCeilingY - lineH - _decisionTuning.DecisionGap * scale
            : _decisionCeilingY;
        if (_decision != null)
        {
            Vector3 p = _decision.localPosition;
            _decision.localPosition = new Vector3(p.x, y, p.z);
        }
        // ...and the label itself is CENTRE-anchored, so it needs half of the same height to turn
        // the ceiling into a position. It was seated once at build time from the authored constant;
        // with a measured height that seat has to move with it or the text and the row it pushed
        // would disagree about where the line ends.
        if (_decisionPrompt != null)
        {
            Vector3 lp = _decisionPrompt.transform.localPosition;
            _decisionPrompt.transform.localPosition =
                new Vector3(lp.x, _decisionCeilingY - 0.5f * lineH, lp.z);
        }
        _shownUseBarStructure = int.MinValue; // force the bars to re-derive from the moved row
        VRLog.Info("Net", $"Remote decision seat: the mirrored area's CEILING is board-local y " +
                          $"{_decisionCeilingY:F3} (the owner's drawer-zone top at their offset/scale, " +
                          "clamped at the grab bar) — the topmost element is " +
                          (promptLineShown
                              ? $"the PROMPT LINE, so the button row drops to y {y:F3}: " +
                                (_promptMeasuredHeight > 0f
                                    ? $"the line's MEASURED rendered height ({lineH:F4} m, " +
                                      $"TMP renderedHeight after a forced mesh update — the authored " +
                                      $"fallback would have said {PromptLineHeight * scale:F4} m)"
                                    : $"one authored line ({lineH:F4} m — NOT MEASURED YET, which " +
                                      "means the label had no text when this ran)") +
                                $" plus one DecisionGap ({_decisionTuning.DecisionGap * scale:F3} m) " +
                                "below the ceiling. THE OWNER'S OWN SIDE MEASURES TOO (DamageTooltip" +
                                "Surface fits the real HelpBox and DecisionDockSurface hangs the row " +
                                "off the measured text bottom), so these two numbers are now produced " +
                                "the same way; the 2026-08-15 session measured 26.9 mm board-local on " +
                                "the owner against 120 mm reserved here, and the buttons 55.9 mm " +
                                "against 149 mm. A next log whose measured height is still several " +
                                "times the owner's means the text is WRAPPING where the owner's does " +
                                "not, and the lever is then the label's WIDTH, not this seat"
                              : $"the BUTTON ROW itself, seated at the ceiling (y {y:F3}) because the " +
                                "owner's prompt draws no text line") +
                          ". The use-bar drawer measures down from that row, so the whole mirrored " +
                          "display hangs from one prompt-independent top edge — as the owner's does.");
    }

    /// <summary>Strip TMP colour tags for a log line (the composed prompt is rich text).</summary>
    private static string StripRichText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        bool inTag = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString();
    }

    // The decision text↔button GAP (Defaults.DecisionGap_*, board-local metres below the prompt
    // reference) and the dock SCALE (Defaults.DecisionScale_*, 1.6 on every shipped board — the
    // "readable under pressure" enlargement the local mount carries) both come off the owner's
    // resolved tuning now, so a player who has moved either reads the same to everyone.

    // ---- decision-row geometry (all board-local metres, scaled by the owner's dock scale) ----
    // The local row is the game's own widget row fitted into PlayTray.DecisionMountWidth ×
    // DecisionMountMaxHeight at the mount's 1.6× scale; the mirror reproduces that envelope with
    // one plate per synced label — content-true widths inside the same budget.

    /// <summary>Plate height at scale 1 (a game option button's ~90 px at the dock density).</summary>
    private const float DecisionButtonH = 0.048f;

    /// <summary>Gap between two plates, board-local metres (at scale 1).</summary>
    private const float DecisionButtonGap = 0.008f;

    /// <summary>Per-plate width ceiling at scale 1 — a lone confirm button must not stretch
    /// across the whole 0.42 budget the way an equal split would.</summary>
    private const float DecisionButtonMaxW = 0.20f;

    /// <summary>Per-plate width FLOOR at scale 1 — a two-letter option ("Ja"/"Nein") must still
    /// read as a button, not as a sliver, when it shares the row with a long burn wording.</summary>
    private const float DecisionButtonMinW = 0.055f;

    /// <summary>Padding added to a label's content share when the row is sized content-true (the
    /// game's option buttons are a ContentSizeFitter around the wording plus a fixed inset, so a
    /// short option is a SHORT button — the equal-slot split the first mirror used was the most
    /// visible size difference against the owner's real row).</summary>
    private const float DecisionButtonPadW = 0.030f;

    /// <summary>
    /// (Re)build the mirrored decision-button row from the owner's synced labels (wire record
    /// 12; null = none). Change-gated on the joined string — a rebuild is a handful of quads and
    /// labels, and it only happens when the owner's dock content really changed. The idle
    /// captioned drawer shows exactly while the decision bit is set WITHOUT labels, so a legacy
    /// sender keeps its familiar look.
    ///
    /// DEMOTED TO A FALLBACK IN ModBuild 105 (user report 2026-08-09, verbatim: "Die
    /// Entscheidungsbuttons sollen auch 1:1 aussehen, aktuell scheint das kaputt zu sein … Es sah so
    /// aus als wären die Buttons und der Text eigens nachgebaut und hier nicht die Spielelemente
    /// genutzt"). Everything below is still the closest a REPRODUCTION can get, and a reproduction
    /// was the wrong answer: the take-damage prompt is now mirrored as a clone of THIS client's own
    /// <c>TakeDamagePanel</c> widgets driven by wire record 29 (see
    /// <see cref="RemoteDecisionWidgets"/>) — real button art, real damage/fatal icons, the real
    /// damage number, and every wording in the VIEWER's own language. This builder runs only when
    /// that cannot be done: another prompt kind (a short-rest Yes/No, a DialogPopup, whose widgets
    /// do not exist on a peer in the state their owner sees), or a sender predating record 29.
    ///
    /// 1:1 WITH WHAT THE DECIDING PLAYER SEES (user 2026-08-07, verbatim: "Die
    /// Entscheidungsbuttons sollen 1:1 genau so aussehen (Position und Größe und Erscheinungsbild)
    /// und genau das beinhalten was der Spieler sieht"). The owner's dock does NOT build buttons —
    /// <c>WorldUI.Surfaces.DecisionDockSurface</c> re-hosts the GAME's own prompt widgets on the
    /// board and restyles them in <c>AdjustDockedRow</c>: the shared 9-sliced game button sprite
    /// multiplied by <c>DecisionDockSurface.AntiqueTint</c>, labels recoloured to
    /// <c>NativeButtonSkin.LabelColor</c> in the game's HUD font. The mirror therefore reproduces
    /// that same look from the same sources instead of approximating it:
    /// <list type="bullet">
    ///   <item>FACE — <c>NativeButtonSkin.CreateFace</c>, i.e. a 9-sliced <c>SpriteRenderer</c>
    ///     wearing the very sprite <c>NativeButtonSkin</c> harvested off the live game UI (every
    ///     client owns the same assets, so nothing about the button art needs to ride the wire),
    ///     tinted with the local dock's own <c>AntiqueTint</c> constant. The flat gold-rim/dark-body
    ///     quads it replaces are kept only as the fallback for the window before a live button has
    ///     been sampled.</item>
    ///   <item>LABEL — the game HUD font (<c>NativeButtonSkin.ApplyFont</c>), the dock's
    ///     <c>LabelColor</c>, and the shared un-renderable-glyph strip, so the wording reads
    ///     identically to the owner's caption.</item>
    ///   <item>SIZE — plates are CONTENT-TRUE (the game fits each option button to its wording),
    ///     laid out inside the very envelope the owner's dock fits its row into
    ///     (<c>PlayTray.DecisionMountWidth</c> × the authored dock scale), so a short "Schaden
    ///     erhalten" is a short button next to a long burn wording.</item>
    ///   <item>POSITION — unchanged: the row already hangs from the widget-block top edge the
    ///     owner's own dock anchors at (bar bottom − <c>BarClearanceMeters</c> − the authored
    ///     <c>DecisionGap</c>), centred on the dock axis.</item>
    /// </list>
    /// Display-only by construction: no collider, no <c>IPokeable</c>, registered with no laser or
    /// poke router, and <see cref="StripColliders"/> sweeps the finished row.
    /// </summary>
    private void SetDecisionLines(string? lines)
    {
        // Change gate, with ONE exception: a row that had to fall back to the procedural plate
        // (no live game button sampled yet when it was built) is rebuilt as soon as
        // NativeButtonSkin has one, so the first prompt of a session cannot get stuck on the
        // approximate look the 1:1 rule replaced.
        bool upgrade = _decisionRow != null && !_decisionRowNative && WorldUI.NativeButtonSkin.HasSprite;
        if (lines == _shownDecisionLines && !upgrade)
            return;
        _shownDecisionLines = lines;

        if (_decisionRow != null)
        {
            Object.Destroy(_decisionRow.gameObject);
            _decisionRow = null;
        }
        _decisionPlates.Clear();
        _shownOptionStates = null; // a new row repaints its states from scratch
        if (_drawerIdle != null && _drawerIdle.gameObject.activeSelf != (lines == null))
            _drawerIdle.gameObject.SetActive(lines == null);
        if (lines == null)
            return;

        float scale = _decisionTuning.DecisionScale;
        string[] labels = lines.Split('\n');
        int n = labels.Length;

        _decisionRow = new GameObject("SyncedRow").transform;
        _decisionRow.SetParent(_decision, worldPositionStays: false);
        _decisionRow.localPosition = Vector3.zero;

        // One horizontal row, centred on the dock axis like the game's own option rows, inside the
        // owner's own width budget — with CONTENT-TRUE plate widths (see the member doc).
        float budget = Cards.PlayTray.DecisionMountWidth * scale;
        float gap = DecisionButtonGap * scale;
        float h = DecisionButtonH * scale;
        float[] widths = DecisionPlateWidths(labels, budget, gap, scale);
        float rowW = (n - 1) * gap;
        for (int i = 0; i < n; i++)
            rowW += widths[i];

        bool native = WorldUI.NativeButtonSkin.HasSprite;
        _decisionRowNative = native;
        Color gold = WorldUI.NativeButtonSkin.HasFont
            ? WorldUI.NativeButtonSkin.LabelColor
            : new Color(0.91f, 0.82f, 0.62f);
        float x = -rowW * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float w = widths[i];
            var plate = new GameObject($"Button{i}").transform;
            plate.SetParent(_decisionRow, worldPositionStays: false);
            plate.localPosition = new Vector3(x + w * 0.5f, -h * 0.5f, 0f);
            x += w + gap;

            // The owner's docked widget IS the game's 9-sliced button sprite multiplied by
            // AntiqueTint — so wear the same sprite and the same constant here.
            SpriteRenderer? face = native
                ? WorldUI.NativeButtonSkin.CreateFace(plate, new Vector2(w, h), localZ: 0.001f,
                    sortingOrder: 0)
                : null;
            Material? rimMat = null;
            Material? bodyMat = null;
            if (face != null)
            {
                face.color = WorldUI.Surfaces.DecisionDockSurface.AntiqueTint;
            }
            else
            {
                // Pre-sample fallback (no live button harvested yet): the flat gold-rim/dark-body
                // plate of the first mirror, kept so an early prompt is never an empty hole.
                rimMat = BoardVisual.Unlit(new Color(0.55f, 0.45f, 0.22f, 1f));
                BoardVisual.Quad(plate, "Rim", new Vector2(w, h), rimMat)
                    .transform.localPosition = new Vector3(0f, 0f, 0.0015f);
                bodyMat = BoardVisual.Unlit(new Color(0.23f, 0.18f, 0.12f, 1f));
                BoardVisual.Quad(plate, "Face", new Vector2(w - 0.006f * scale, h - 0.006f * scale),
                    bodyMat)
                    .transform.localPosition = new Vector3(0f, 0f, 0.001f);
            }

            // The CHOSEN telegraph: a thin accent frame behind the plate, shown only while the
            // owner has that option toggled on (wire record 23). Inert like everything here, and
            // built once per plate so the state repaint never allocates.
            GameObject chosenRim = BoardVisual.Quad(plate, "ChosenRim",
                new Vector2(w + 0.008f * scale, h + 0.008f * scale),
                BoardVisual.Unlit(new Color(1f, 0.85f, 0.35f, 0.95f))).gameObject;
            chosenRim.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            chosenRim.SetActive(false);

            TextMeshPro label = RemoteBoardContent.Label(plate, "Label", new Vector3(0f, 0f, -0.001f),
                new Vector2(w * 0.86f, h * 0.72f), 0.23f * scale, gold,
                TextAlignmentOptions.Center, wrap: true);
            WorldUI.NativeButtonSkin.ApplyFont(label); // game HUD font, depth-honest material
            label.color = gold;                        // ApplyFont must not undo the dock colour
            label.text = WorldUI.NativeButtonSkin.SanitizeLabel(label, labels[i]);
            _decisionPlates.Add(new DecisionPlate(face, rimMat, bodyMat, label, chosenRim));
        }
        StripColliders(_decisionRow.gameObject, "RemoteBoardFurniture.SyncedRow");

        VRLog.Info("Net", $"Remote decision dock: {n} mirrored button(s) " +
                          $"(\"{lines.Replace('\n', '|')}\") — row {rowW:F3} m wide, content-true " +
                          $"plate widths [{string.Join(", ", System.Array.ConvertAll(widths, v => v.ToString("F3")))}] " +
                          $"× {h:F3} m at the authored ×{scale:F2} dock scale, face = " +
                          $"{(native ? "the sampled GAME button sprite (9-sliced) × DecisionDockSurface.AntiqueTint" : "procedural fallback plate (no live button sampled yet)")}, " +
                          $"top edge at board-local y {_decision.localPosition.y:F3} (bar bottom − " +
                          $"(clearance + the owner's own DecisionGap for {_decisionTuning.Style}) × their " +
                          $"×{scale:F2} dock scale — the scale term the owner's mount carries and this " +
                          "seat used to drop) — the seat, look and " +
                          "wording the OWNER's own docked row shows. Display-only: colliderless.");
    }

    /// <summary>Procedural-fallback plate colours (only used before a live game button has been
    /// sampled) — kept as constants so the state repaint can restore them exactly.</summary>
    private static readonly Color FallbackRimColor = new(0.55f, 0.45f, 0.22f, 1f);
    private static readonly Color FallbackBodyColor = new(0.23f, 0.18f, 0.12f, 1f);

    /// <summary>How far a GREYED option is pushed toward the board's shadow — the mirror of what a
    /// non-interactable uGUI Selectable looks like on the owner's dock (its ColorTint transition
    /// multiplies the disabled colour onto the CanvasRenderer).</summary>
    private const float GreyedFactor = 0.55f;

    /// <summary>Alpha a DIMMED option renders at — the game's own
    /// <c>TakeDamagePanel.UnactiveButtonTransparency</c> (0.7), the "your character does not have
    /// the cards for this" look. Deliberately a SEPARATE axis from greyed: the game shows the two
    /// independently and collapsing them would lose the distinction the owner can see.</summary>
    private const float DimmedAlpha = 0.7f;

    /// <summary>The label gold the mirrored plates letter in — the dock's own
    /// <c>NativeButtonSkin.LabelColor</c> when a live button has been sampled.</summary>
    private static Color BaseLabelGold() => WorldUI.NativeButtonSkin.HasFont
        ? WorldUI.NativeButtonSkin.LabelColor
        : new Color(0.91f, 0.82f, 0.62f);

    /// <summary>
    /// Paint the owner's OPTION STATES (wire record 23) onto the mirrored plates: greyed where the
    /// owner cannot press, dimmed where the game dims, and a lit accent frame on the option they
    /// have already chosen.
    ///
    /// <para>WHY IT IS A SEPARATE PASS from <see cref="SetDecisionLines"/>: the wordings are
    /// constant for a whole prompt while the states move on every click — a toggle flips, the game
    /// re-asserts a gate (<c>CardsDriver.TickTakeDamageOptions</c>). Rebuilding the row for that
    /// would rebuild a handful of quads several times per decision; this repaints four material
    /// colours instead, and only when the bytes actually change.</para>
    ///
    /// <para>A sender that predates record 23 delivers no states: every plate then keeps the plain
    /// look every build before this one drew — never a guess at which option is live, which would
    /// be a lie about somebody else's decision.</para>
    /// </summary>
    private void ApplyDecisionOptionStates(byte[]? states)
    {
        if (_decisionPlates.Count == 0)
        {
            _shownOptionStates = states;
            return;
        }
        if (SameStates(states, _shownOptionStates))
            return;
        _shownOptionStates = states;

        Color gold = BaseLabelGold();
        for (int i = 0; i < _decisionPlates.Count; i++)
        {
            DecisionPlate plate = _decisionPlates[i];
            byte flags = states != null && i < states.Length ? states[i] : (byte)0;
            bool known = states != null && i < states.Length;
            // Unknown (pre-record sender) ⇒ the plain look: offered, undimmed, unchosen.
            bool offered = !known || (flags & NetProtocol.DecisionOptionOfferedBit) != 0;
            bool dimmed = known && (flags & NetProtocol.DecisionOptionDimmedBit) != 0;
            bool chosen = known && (flags & NetProtocol.DecisionOptionChosenBit) != 0;

            float tint = offered ? 1f : GreyedFactor;
            float alpha = dimmed ? DimmedAlpha : 1f;
            if (plate.Face != null)
            {
                Color c = WorldUI.Surfaces.DecisionDockSurface.AntiqueTint;
                plate.Face.color = new Color(c.r * tint, c.g * tint, c.b * tint, c.a * alpha);
            }
            if (plate.RimMat != null)
                plate.RimMat.color = new Color(FallbackRimColor.r * tint, FallbackRimColor.g * tint,
                    FallbackRimColor.b * tint, FallbackRimColor.a * alpha);
            if (plate.BodyMat != null)
                plate.BodyMat.color = new Color(FallbackBodyColor.r * tint,
                    FallbackBodyColor.g * tint, FallbackBodyColor.b * tint,
                    FallbackBodyColor.a * alpha);
            if (plate.Label != null)
                plate.Label.color = new Color(gold.r * tint, gold.g * tint, gold.b * tint,
                    gold.a * alpha);
            if (plate.ChosenRim != null && plate.ChosenRim.activeSelf != chosen)
                plate.ChosenRim.SetActive(chosen);
        }
        VRLog.Info("Net", $"Remote decision states applied: {_decisionPlates.Count} plate(s) — " +
                          $"{DescribeStates(states, _decisionPlates.Count)} (wire record 23). " +
                          "Greyed/dim/chosen read exactly as on the owner's own dock; still inert — " +
                          "no collider, no raycast target, nothing to press.");
    }

    /// <summary>Value equality for the applied option-state bytes (the repaint's change gate).</summary>
    private static bool SameStates(byte[]? a, byte[]? b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    /// <summary>Human-readable option states for the diagnostic line.</summary>
    private static string DescribeStates(byte[]? states, int plates)
    {
        if (states == null)
            return "no state record (pre-record sender ⇒ every plate keeps the plain look)";
        var sb = new System.Text.StringBuilder(48);
        for (int i = 0; i < plates; i++)
        {
            if (i > 0)
                sb.Append(", ");
            if (i >= states.Length)
            {
                sb.Append('#').Append(i).Append("=unstated");
                continue;
            }
            byte f = states[i];
            sb.Append('#').Append(i).Append('=')
              .Append((f & NetProtocol.DecisionOptionOfferedBit) != 0 ? "OFFERED" : "greyed");
            if ((f & NetProtocol.DecisionOptionDimmedBit) != 0)
                sb.Append("+dim");
            if ((f & NetProtocol.DecisionOptionChosenBit) != 0)
                sb.Append("+CHOSEN");
            if ((f & NetProtocol.DecisionOptionHoveredBit) != 0)
                sb.Append("+HOVER");
            if ((f & NetProtocol.DecisionOptionPressedBit) != 0)
                sb.Append("+PRESS");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Content-true plate widths for one mirrored decision row. The game fits each option button to
    /// its wording (ContentSizeFitter + inset), so an equal split is visibly wrong beside the
    /// owner's real row; widths are shared out in proportion to the label lengths, each clamped to
    /// [<see cref="DecisionButtonMinW"/>, <see cref="DecisionButtonMaxW"/>] and the whole row then
    /// scaled to fit the owner's width budget. Never returns a non-positive width.
    /// </summary>
    private static float[] DecisionPlateWidths(string[] labels, float budget, float gap, float scale)
    {
        int n = labels.Length;
        var widths = new float[n];
        float minW = DecisionButtonMinW * scale;
        float maxW = DecisionButtonMaxW * scale;
        float padW = DecisionButtonPadW * scale;
        float free = Mathf.Max(minW * n, budget - (n - 1) * gap);

        // Share the content budget (what is left after each plate's fixed inset) by label length.
        float contentBudget = Mathf.Max(0f, free - n * padW);
        int totalChars = 0;
        for (int i = 0; i < n; i++)
            totalChars += Mathf.Max(1, labels[i].Length);

        float sum = 0f;
        for (int i = 0; i < n; i++)
        {
            float share = totalChars > 0 ? contentBudget * Mathf.Max(1, labels[i].Length) / totalChars : 0f;
            widths[i] = Mathf.Clamp(padW + share, minW, maxW);
            sum += widths[i];
        }
        // The clamps can push the row past the budget — shrink uniformly rather than overflow the
        // owner's dock envelope (a mirror that is WIDER than the original is the one thing the 1:1
        // rule cannot tolerate).
        if (sum > free && sum > 0f)
        {
            float k = free / sum;
            for (int i = 0; i < n; i++)
                widths[i] = Mathf.Max(0.001f, widths[i] * k);
        }
        return widths;
    }

    // ---- the mirrored USE-BAR drawer (wire record 25) ---------------------------------------
    // Geometry, all board-local metres at scale 1 and multiplied by the authored DecisionScaleFor
    // like the decision row above it, so the two drawers read as ONE connected decision area on
    // the mirror exactly as they do on the owner's board.

    /// <summary>Height of one mirrored bar row (the owner's bar strip is a single row of square
    /// slot symbols, fitted into the same DecisionMountWidth budget as the decision row).</summary>
    private const float UseBarRowH = 0.040f;

    /// <summary>Gap between two stacked bar rows. ALIASED, not copied, from the local stack's own
    /// constant — one value, nothing to drift (the <c>BarClearanceMeters</c> precedent, see
    /// <c>scripts/check-mirrors.sh</c>).</summary>
    private const float UseBarRowGap = WorldUI.Surfaces.UseBarsSurface.StackGap;

    /// <summary>Clearance between the decision row's bottom edge and the bar stack top — the
    /// constant that keeps the two drawers reading as ONE decision area instead of two (screenshot
    /// abstand.png). Aliased from the local stack for the same reason as
    /// <see cref="UseBarRowGap"/>.</summary>
    private const float UseBarDecisionClearance =
        WorldUI.Surfaces.UseBarsSurface.DecisionClearance;

    /// <summary>Side of one mirrored slot tile.</summary>
    private const float UseBarTile = 0.026f;

    /// <summary>How much of a mirrored slot tile the game's own icon fills, measured on its longer
    /// side. 0.86 leaves the tile's own plate reading as the slot frame around the symbol, which is
    /// what the game's slot does with its icon inside its button background — a symbol drawn edge to
    /// edge would read as a sticker on the drawer instead of a widget in it.</summary>
    private const float UseBarSymbolFill = 0.86f;

    /// <summary>Gap between two tiles in a row.</summary>
    private const float UseBarTileGap = 0.006f;

    /// <summary>Width of the caption column left of a bar's tiles (the bar's NAME, localized HERE
    /// from the bar bit — the record-24 text-variant solution, applied to a drawer whose slots have
    /// no wordings at all).</summary>
    private const float UseBarCaptionW = 0.150f;

    /// <summary>Tile colour of an OFFERED slot — the same antique family the mirrored decision
    /// plates wear, so the two drawers are visibly one surface.</summary>
    private static readonly Color UseBarTileColor = new(0.38f, 0.31f, 0.20f, 1f);

    /// <summary>Backing plate of one mirrored bar row (the owner's bar strip has its own dark
    /// backing under the MR plate).</summary>
    private static readonly Color UseBarPlateColor = new(0.10f, 0.09f, 0.08f, 0.80f);

    /// <summary>Localized caption of a record-25 BAR INDEX. The bar NAME is composed on THIS
    /// machine from the bar BIT — the wire never carries a word, and the slots it describes have no
    /// word to carry (see <see cref="NetProtocol.ExtIdUseBars"/>).</summary>
    private static string UseBarCaption(int bar) => bar switch
    {
        0 => Loc.Mod("use_bar_bonuses"),
        1 => Loc.Mod("use_bar_abilities"),
        2 => Loc.Mod("use_bar_augments"),
        _ => Loc.Mod("use_bar_items"),
    };

    /// <summary>
    /// The STRUCTURE fingerprint of the owner's published drawer — mask + per-bar slot counts, AND
    /// whether a mirrored decision row currently stands above it. A change here rebuilds the rows;
    /// a change in the STATES alone only repaints.
    ///
    /// <para>The decision row belongs in this key even though it is not part of record 25: the
    /// stack TOP is derived from it (row up ⇒ hang below the row, row down ⇒ take the drawer zone's
    /// own top edge, the two cases the owner's <c>UseBarsSurface.StackDocked</c> distinguishes), so
    /// a row appearing or undocking under unchanged bars must re-seat the stack — otherwise the
    /// mirrored bars would keep hanging where a row no longer is.</para>
    /// </summary>
    private int UseBarStructure(RemoteAvatar owner)
    {
        if (owner.UseBarsMask == 0)
            return 0;
        // A ROW IS UP either way — the mod-drawn plates OR the mirrored GAME widgets — and the
        // widget row's MEASURED height is part of the key, so a re-fit re-seats the bars under
        // it instead of leaving them under the height the plates used to have.
        int key = owner.UseBarsMask | (DecisionRowUp ? 1 << 8 : 0)
                  | (Mathf.RoundToInt(DecisionRowHeight * 10000f) << 9);
        for (int b = 0; b < NetProtocol.UseBarsCount; b++)
        {
            int n = owner.UseBarSlotCounts != null && b < owner.UseBarSlotCounts.Length
                ? owner.UseBarSlotCounts[b] : 0;
            key = key * 31 + n;
        }
        return key;
    }

    /// <summary>
    /// (Re)build the mirrored use-bar drawer from the owner's synced structure (wire record 25;
    /// mask 0 = no bars, which is also what a sender predating the record produces).
    ///
    /// <para>WHAT IT DRAWS, AND WHY THAT IS THE 1:1 ANSWER. The owner's bars are strips of ICON
    /// tiles: <c>UIUseSlot&lt;T&gt;</c> has no label at all, and each concrete slot decorates itself
    /// with a sprite taken straight off the item / bonus / ability art
    /// (<c>UIUseItemScenario.Decorate</c> → <c>UIInfoTools.GetItemConfig(item.YMLData.Art)
    /// .miniIcon</c>). That art IS card identity, and card identity never rides this wire in any
    /// form — so the mirror shows the drawer's STRUCTURE and STATE faithfully (which bars, how many
    /// slots, which are live, dim or chosen, whether a sub-picker stands open) and captions each bar
    /// from its BAR BIT in the VIEWER's language. It is the same rule
    /// <see cref="RemoteItemCardSource"/> ships for a peer's item faces: structure travels, identity
    /// is resolved locally or not at all.</para>
    ///
    /// <para>SEAT — derived term-for-term from the owner's own stack (<c>UseBarsSurface.
    /// StackDocked</c>): while a decision row is up, the stack top hangs
    /// <see cref="UseBarDecisionClearance"/> below that row's bottom edge (here: the mirrored row's
    /// own single-plate height below its live seat, which <see cref="ApplyDecisionSeat"/> has already
    /// dropped by the prompt line when the owner shows one); with no row, it takes the decision
    /// area's CEILING, which is this drawer root's own origin. Rows then stack downward with
    /// <see cref="UseBarRowGap"/>, in the owner's own bar order — which is the wire's bit order, so
    /// nothing has to describe it.</para>
    ///
    /// <para>Display-only by construction: no collider, no <c>IPokeable</c>, registered with no
    /// laser or poke router, and <see cref="StripColliders"/> sweeps the finished drawer.</para>
    /// </summary>
    private void SetUseBars(RemoteAvatar owner)
    {
        int structure = UseBarStructure(owner);
        if (structure == _shownUseBarStructure)
            return;
        _shownUseBarStructure = structure;

        for (int r = 0; r < _useBarRows.Count; r++)
        {
            if (_useBarRows[r].Root != null)
                Object.Destroy(_useBarRows[r].Root.gameObject);
        }
        _useBarRows.Clear();
        _useBarRowIndices.Clear();
        _shownUseBarStates = null; // a new drawer repaints its states from scratch
        _shownUseBarFlags = null;

        byte mask = owner.UseBarsMask;
        if (mask == 0)
        {
            if (_useBars.gameObject.activeSelf)
                _useBars.gameObject.SetActive(false);
            VRLog.Info("Net", "Remote use bars: none — the mirrored bar drawer is empty (no bar " +
                              "docked and visible on the owner's board, or a sender predating " +
                              "record 25).");
            return;
        }

        // The use-bar drawer rides the DECISION dock's own scale and offset, which since record
        // 28 come from the OWNER's tuning rather than from the shipped defaults — otherwise a
        // tuned player's mirrored bars would stack at a different pitch from their mirrored row.
        float scale = _decisionTuning.DecisionScale;
        float rowH = UseBarRowH * scale;
        float rowGap = UseBarRowGap * scale;
        float budget = Cards.PlayTray.DecisionMountWidth * scale;

        // Stack top, in the drawer root's own local frame (its origin IS the decision row's top
        // edge, so a docked row's bottom sits exactly one mirrored plate below it).
        // ModBuild 91: this root's origin IS the area ceiling, so the two cases are one subtraction.
        // With a mirrored row up the stack hangs one plate height + the shared clearance below THAT
        // ROW'S LIVE SEAT (which ApplyDecisionSeat has already dropped by the prompt line when the
        // owner shows one) — never below a seat assumed here, which is how the mirror used to drift
        // from the owner the moment a prompt added an element. With no row the bars ARE the top of
        // the display and start at the ceiling itself, i.e. 0 in this frame.
        float top = DecisionRowUp
            ? _decision.localPosition.y - _useBars.localPosition.y
              - DecisionRowHeight - UseBarDecisionClearance * scale
            : 0f;

        int rows = 0;
        int totalSlots = 0;
        float cursor = top;
        for (int b = 0; b < NetProtocol.UseBarsCount; b++)
        {
            if ((mask & (1 << b)) == 0)
                continue;
            int n = owner.UseBarSlotCounts != null && b < owner.UseBarSlotCounts.Length
                ? owner.UseBarSlotCounts[b] : 0;
            if (n > NetProtocol.UseBarsMaxSlots)
                n = NetProtocol.UseBarsMaxSlots;

            var row = new GameObject($"UseBar{b}").transform;
            row.SetParent(_useBars, worldPositionStays: false);
            row.localPosition = new Vector3(0f, cursor - rowH * 0.5f, 0f);
            cursor -= rowH + rowGap;

            MeshRenderer plate = BoardVisual.Quad(row, "Plate", new Vector2(budget, rowH),
                BoardVisual.Unlit(UseBarPlateColor));
            plate.transform.localPosition = new Vector3(0f, 0f, 0.001f);
            WorldUI.MrBacking.Opacify(plate.sharedMaterial); // 0.80 → 1 while MR is on

            RemoteBoardContent.Label(row, "Caption",
                new Vector3(-budget * 0.5f + UseBarCaptionW * scale * 0.5f, 0f, -0.001f),
                new Vector2(UseBarCaptionW * scale, rowH * 0.7f), 0.14f * scale,
                new Color(0.72f, 0.68f, 0.58f), TextAlignmentOptions.Left)
                .text = UseBarCaption(b).ToUpperInvariant();

            // Sub-picker badge: a small accent pip at the row's right edge, lit while the owner has
            // an element/option picker standing open in THIS bar (record 25's bar flags). Built once
            // per row so the state repaint never allocates.
            GameObject picker = BoardVisual.Quad(row, "PickerBadge",
                new Vector2(UseBarTile * scale * 0.45f, UseBarTile * scale * 0.45f),
                BoardVisual.Unlit(new Color(1f, 0.85f, 0.35f, 0.95f))).gameObject;
            picker.transform.localPosition =
                new Vector3(budget * 0.5f - UseBarTile * scale * 0.4f, 0f, -0.001f);
            picker.SetActive(false);

            var tiles = new Material[n];
            var rims = new GameObject[n];
            var symbols = new SpriteRenderer[n];
            float tile = UseBarTile * scale;
            float tileGap = UseBarTileGap * scale;
            float tilesW = n > 0 ? n * tile + (n - 1) * tileGap : 0f;
            float x = -budget * 0.5f + UseBarCaptionW * scale;
            // Centre the tiles in what is left of the row after the caption column, and shrink the
            // pitch rather than overflow the owner's own width budget.
            float free = budget - UseBarCaptionW * scale - tile;
            if (tilesW > free && tilesW > 0f)
            {
                float k = free / tilesW;
                tile *= k;
                tileGap *= k;
                tilesW = free;
            }
            x += Mathf.Max(0f, (budget - UseBarCaptionW * scale - tilesW) * 0.5f);
            for (int s = 0; s < n; s++)
            {
                var cell = new GameObject($"Slot{s}").transform;
                cell.SetParent(row, worldPositionStays: false);
                cell.localPosition = new Vector3(x + tile * 0.5f, 0f, 0f);
                x += tile + tileGap;

                // The CHOSEN telegraph: an accent frame behind the tile, shown only while the owner
                // has that slot toggled on — the same language the mirrored decision plates use.
                GameObject rim = BoardVisual.Quad(cell, "ChosenRim",
                    new Vector2(tile + 0.005f * scale, tile + 0.005f * scale),
                    BoardVisual.Unlit(new Color(1f, 0.85f, 0.35f, 0.95f))).gameObject;
                rim.transform.localPosition = new Vector3(0f, 0f, -0.0005f);
                rim.SetActive(false);

                Material face = BoardVisual.Unlit(UseBarTileColor);
                BoardVisual.Quad(cell, "Face", new Vector2(tile, tile), face)
                    .transform.localPosition = new Vector3(0f, 0f, -0.001f);

                // THE SYMBOL (user 2026-08-13: "das gleiche Symbol vom Spiel"). Built EMPTY and
                // disabled; ApplyUseBarSymbols lights it the moment this client can prove which
                // icon the owner's slot is wearing — see RemoteUseBarSymbols for the three-way gate.
                // A SpriteRenderer rather than a quad + material: the game's icons are Sprites, and
                // a SpriteRenderer honours their pivot, border and packing without a second atlas
                // lookup. Inert like everything in this drawer; StripColliders sweeps it anyway.
                var symbolGo = new GameObject($"Symbol{s}");
                symbolGo.transform.SetParent(cell, worldPositionStays: false);
                symbolGo.transform.localPosition = new Vector3(0f, 0f, -0.0015f);
                var symbol = symbolGo.AddComponent<SpriteRenderer>();
                symbol.enabled = false;
                symbols[s] = symbol;

                tiles[s] = face;
                rims[s] = rim;
            }
            totalSlots += n;
            _useBarRows.Add(new UseBarRow(row, tiles, rims, picker, symbols));
            _useBarRowIndices.Add(b);
            rows++;
        }

        if (!_useBars.gameObject.activeSelf)
            _useBars.gameObject.SetActive(true);
        StripColliders(_useBars.gameObject, "RemoteBoardFurniture.UseBarsDrawer");

        VRLog.Info("Net", $"Remote use bars: {rows} mirrored bar row(s), {totalSlots} slot tile(s) " +
                          $"(mask 0x{mask:X2}) — stacked from board-local y " +
                          $"{_useBars.localPosition.y + top:F3} downward, {budget:F3} m wide at the " +
                          $"authored ×{scale:F2} dock scale, " +
                          $"{(_shownDecisionLines != null ? "hung below the mirrored decision row" : "at the drawer zone top (no decision row up)")}. " +
                          "Bar captions are composed HERE from the bar bit; the SLOT SYMBOLS are " +
                          "resolved locally by RemoteUseBarSymbols against this client's own copy " +
                          "of the same bar — nothing about the art rides this wire. Display-only: " +
                          "colliderless.");
    }

    /// <summary>Slot-symbol scratch (max slots per bar), so the 4 Hz pass allocates nothing.</summary>
    private readonly Sprite?[] _symbolScratch = new Sprite?[NetProtocol.UseBarsMaxSlots];

    /// <summary>Last (barIndex, resolved-count) pair the symbol pass logged, so a steady bar costs
    /// one line and not four a second.</summary>
    private int _shownSymbolKey = -1;

    /// <summary>
    /// Put THE GAME'S OWN ICON on each mirrored slot tile — the answer to "das gleiche Symbol vom
    /// Spiel", and the reason it needs no wire field is written once in
    /// <see cref="RemoteUseBarSymbols"/>: the four use bars are per-client singletons that the game
    /// raises from REPLICATED messages, so a peer's own copy of the owner's bar is already standing
    /// there with the same rows, in the same order, wearing the same art.
    ///
    /// <para>Refused unless the local bar is provably the owner's (its owner set contains this
    /// board's actor) AND its visible slot count equals the one record 25 carried, walked by the
    /// sender's own rule. On a refusal every symbol is switched OFF and the tile shows exactly what
    /// it showed in every build before this one — an anonymous plate — because a symbol from
    /// somebody else's decision would be worse than none.</para>
    ///
    /// <para>Runs on the content cadence beside <see cref="ApplyUseBarStates"/> rather than at build
    /// time: the game re-decorates a slot in place (an item is spent, a bonus is consumed) without
    /// the bar's STRUCTURE changing, and the structure key is what gates the rebuild.</para>
    /// </summary>
    private void ApplyUseBarSymbols(CPlayerActor? actor, RemoteAvatar owner)
    {
        if (_useBarRows.Count == 0)
            return;
        int lit = 0;
        int litBar = -1;
        for (int r = 0; r < _useBarRows.Count; r++)
        {
            UseBarRow row = _useBarRows[r];
            SpriteRenderer[] symbols = row.Symbols;
            if (symbols == null || symbols.Length == 0)
                continue;
            int bar = r < _useBarRowIndices.Count ? _useBarRowIndices[r] : -1;
            System.Array.Clear(_symbolScratch, 0, _symbolScratch.Length);
            int resolved = bar >= 0
                ? RemoteUseBarSymbols.Resolve(bar, actor, symbols.Length, _symbolScratch)
                : 0;
            for (int s = 0; s < symbols.Length; s++)
            {
                SpriteRenderer sr = symbols[s];
                if (sr == null)
                    continue;
                Sprite? sprite = s < resolved ? _symbolScratch[s] : null;
                bool on = sprite != null;
                if (on && !ReferenceEquals(sr.sprite, sprite))
                    sr.sprite = sprite;
                if (sr.enabled != on)
                    sr.enabled = on;
                if (!on)
                    continue;
                lit++;
                litBar = bar;
                // FIT THE TILE, KEEP THE ASPECT. The tile is the drawer's own square cell (the seat
                // and pitch the user has already tuned); the sprite is scaled into it by its longer
                // side so a wide icon is never stretched — a squashed symbol is not the same symbol.
                Bounds b = sprite!.bounds;
                float longest = Mathf.Max(b.size.x, b.size.y);
                float k = longest > 0.0001f ? UseBarTile * _decisionTuning.DecisionScale * UseBarSymbolFill / longest : 1f;
                var want = new Vector3(k, k, 1f);
                if (sr.transform.localScale != want)
                    sr.transform.localScale = want;
            }
        }
        int key = lit == 0 ? 0 : (litBar + 1) * 1000 + lit;
        if (key == _shownSymbolKey)
            return;
        _shownSymbolKey = key;
        VRLog.Info("Net", lit > 0
            ? $"DOCK MIRROR: {lit} mirrored use-bar slot(s) now wear THE GAME'S OWN symbol " +
              $"(bar {litBar}, owner '{Board.CharacterFocus.Describe(actor)}') — taken off this " +
              "client's own copy of that bar, gated on the bar's owner being this board's character " +
              "and on its visible slot count matching record 25's. Nothing about the art travelled; " +
              "the tiles are still inert."
            : "DOCK MIRROR: no mirrored use-bar symbol resolved — this client's own copy of the " +
              "owner's bar is absent, belongs to another character, or shows a different number of " +
              "slots than record 25 reported. The tiles stay anonymous, which is what every build " +
              "before this one drew. The wire carried no art either way.");
    }

    /// <summary>
    /// Paint the owner's per-slot STATES and open sub-pickers (wire record 25) onto the mirrored
    /// tiles: greyed where the owner cannot click, dimmed where the game dims (its
    /// <c>UIUseSlot.disabledAlpha</c> look), an accent frame on a slot they have chosen, and the
    /// row's pip lit while an element/option picker stands open in that bar.
    ///
    /// <para>A SEPARATE PASS from <see cref="SetUseBars"/> for the same reason the decision row
    /// splits its labels from its option states: the structure is constant for a whole bar while the
    /// states move on every click. This repaints a handful of material colours; a rebuild would
    /// re-create quads several times per decision.</para>
    /// </summary>
    private void ApplyUseBarStates(RemoteAvatar owner)
    {
        if (_useBarRows.Count == 0)
        {
            _shownUseBarStates = owner.UseBarSlotStates;
            _shownUseBarFlags = owner.UseBarFlags;
            return;
        }
        byte[]? states = owner.UseBarSlotStates;
        byte[]? flags = owner.UseBarFlags;
        if (SameStates(states, _shownUseBarStates) && SameStates(flags, _shownUseBarFlags))
            return;
        _shownUseBarStates = states;
        _shownUseBarFlags = flags;

        for (int r = 0; r < _useBarRows.Count; r++)
        {
            UseBarRow row = _useBarRows[r];
            int bar = _useBarRowIndices[r];
            byte f = flags != null && bar < flags.Length ? flags[bar] : (byte)0;
            bool picker = (f & (NetProtocol.UseBarElementPickerBit
                                | NetProtocol.UseBarOptionPickerBit)) != 0;
            if (row.Picker != null && row.Picker.activeSelf != picker)
                row.Picker.SetActive(picker);

            int at = bar * NetProtocol.UseBarsMaxSlots;
            for (int s = 0; s < row.Tiles.Length; s++)
            {
                byte st = states != null && at + s < states.Length ? states[at + s] : (byte)0;
                bool known = states != null && at + s < states.Length;
                // Unknown (a short/absent state array) ⇒ the plain look: offered, undimmed,
                // unchosen — never a guess at somebody else's live choice.
                bool offered = !known || (st & NetProtocol.UseSlotOfferedBit) != 0;
                bool dimmed = known && (st & NetProtocol.UseSlotDimmedBit) != 0;
                bool chosen = known && (st & NetProtocol.UseSlotChosenBit) != 0;

                float tint = offered ? 1f : GreyedFactor;
                float alpha = dimmed ? DimmedAlpha : 1f;
                if (row.Tiles[s] != null)
                    row.Tiles[s].color = new Color(UseBarTileColor.r * tint, UseBarTileColor.g * tint,
                        UseBarTileColor.b * tint, UseBarTileColor.a * alpha);
                if (row.ChosenRims[s] != null && row.ChosenRims[s].activeSelf != chosen)
                    row.ChosenRims[s].SetActive(chosen);
            }
        }

        VRLog.Info("Net", $"Remote use-bar states applied: {_useBarRows.Count} row(s) — " +
                          $"{DescribeUseBarStates(states, flags)} (wire record 25). Greyed/dim/chosen " +
                          "and the open-sub-picker pip read exactly as on the owner's own drawer; " +
                          "still inert — no collider, no raycast target, nothing to press.");
    }

    /// <summary>Human-readable use-bar states for the diagnostic line.</summary>
    private string DescribeUseBarStates(byte[]? states, byte[]? flags)
    {
        if (states == null && flags == null)
            return "no state bytes (every tile keeps the plain look)";
        var sb = new System.Text.StringBuilder(96);
        for (int r = 0; r < _useBarRows.Count; r++)
        {
            int bar = _useBarRowIndices[r];
            if (sb.Length > 0)
                sb.Append("; ");
            sb.Append(UseBarCaption(bar));
            byte f = flags != null && bar < flags.Length ? flags[bar] : (byte)0;
            if ((f & NetProtocol.UseBarElementPickerBit) != 0)
                sb.Append(" +element picker OPEN");
            if ((f & NetProtocol.UseBarOptionPickerBit) != 0)
                sb.Append(" +option picker OPEN");
            int at = bar * NetProtocol.UseBarsMaxSlots;
            for (int s = 0; s < _useBarRows[r].Tiles.Length; s++)
            {
                byte st = states != null && at + s < states.Length ? states[at + s] : (byte)0;
                sb.Append(" #").Append(s).Append('=')
                  .Append((st & NetProtocol.UseSlotOfferedBit) != 0 ? "OFFERED" : "greyed");
                if ((st & NetProtocol.UseSlotDimmedBit) != 0)
                    sb.Append("+dim");
                if ((st & NetProtocol.UseSlotChosenBit) != 0)
                    sb.Append("+CHOSEN");
            }
        }
        return sb.ToString();
    }

    // THE MIRRORED SEAT LINER IS GONE, in the same build as the owner's own. User report
    // 2026-08-13, verbatim: "Dieses Brett das zu den overlays gehört soll komplett weg, das
    // brauchen wir nicht. Die overlays reichen und können auf dem asset des controllboards ohne
    // etwas zugehöriges positioniert werden." A remote board is a picture of its owner's board, so
    // a plank the owner no longer has may not survive here either — that is the 1:1 rule read in
    // the only direction it can be read. The old builder took no field and had no children, so its
    // removal is the deletion of one call and one method; the two constants it borrowed
    // (PlayTray.SlotLinerSeatRatio / SlotLinerColor) went with the local builder.

    /// <summary>A collider-free glow rim behind a round-card slot (the teal "wanted" pulse and the
    /// gold snap flash share this shape, exactly as on the local board — different hue, different
    /// rim size, the teal a hair less proud so the gold always draws in front of it). Sized to the
    /// remote card metric so the rim frames the rendered card.</summary>
    private GameObject BuildSlotGlow(string name, Vector3 cardLocal, float scale, float proud,
        Color color, bool pulse)
    {
        var mat = BoardVisual.Unlit(color);
        MeshRenderer mr = BoardVisual.Quad(_root, name,
            new Vector2(_slotFrameW * scale, _slotFrameH * scale), mat);
        mr.transform.localPosition = new Vector3(cardLocal.x, cardLocal.y, cardLocal.z + proud);
        if (pulse)
            mr.gameObject.AddComponent<RemoteGlowPulse>().Init(mat, color);
        mr.gameObject.SetActive(false);
        return mr.gameObject;
    }

    // BuildHalfDivider / SetHalves are GONE (see the ctor's slot-overlay block for the argument):
    // they drew a hairline across every face-up round card on a peer's board, standing in for poke
    // zones the OWNER's board deliberately never draws. Under the 1:1 rule that is a widget peers
    // see and the owner does not, and there is no owner-side state that could gate it, so it was
    // deleted rather than gated. Zero wire either way.

    /// <summary>A collider-free LIT cube (BoardLit → Standard fallback) — the 3D handle bar and
    /// any future solid furniture piece, shaded like the local board's own primitives instead of
    /// the flat unlit quads of the 2D era.</summary>
    private static void LitCube(Transform parent, string name, Vector3 size, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = size;
        Shader? shader = Cards.PlayTray.BoardLitShader()
                         ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
                         ?? Shader.Find("Sprites/Default");
        if (shader != null)
            go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = color };
    }

    // ---------------------------------------------------------------- change-gated setters --

    private void SetWanted(int mask)
    {
        if (mask == _shownWantedMask)
            return;
        _shownWantedMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_wanted[i] != null && _wanted[i]!.activeSelf != on)
                _wanted[i]!.SetActive(on);
        }
    }

    private void SetSnap(int mask)
    {
        if (mask == _shownSnapMask)
            return;
        _shownSnapMask = mask;
        for (int i = 0; i < 2; i++)
        {
            bool on = (mask & (1 << i)) != 0;
            if (_snap[i] != null && _snap[i]!.activeSelf != on)
                _snap[i]!.SetActive(on);
        }
    }


    // ---------------------------------------------------------------- inert cap --

    /// <summary>
    /// One INERT keycap in REAL 3D: the same base plate + beveled/round cap MESH the local board's
    /// <c>PlayTray.BoardButton</c> renders — dark-wood base, cap body through
    /// <c>PlayTray.NewKeycapMaterial</c> (BoardLit + carved-grain <c>_MainTex</c>, so the walls
    /// shade even in the unlit scenes), engraved parchment label — with everything button-like
    /// omitted: NO collider, NO press travel, NO <c>IPokeable</c>, registered NOWHERE. It is a
    /// solid picture of a button.
    ///
    /// Geometry constants below are copies of <c>PlayTray.BoardButton</c>'s authored privates
    /// (promoting them would widen PlayTray's internal surface — the same trade the layout consts
    /// at the top of this file already make); the three float lerps are linted against drift by
    /// <c>scripts/check-mirrors.sh</c>.
    /// </summary>
    /// <summary>
    /// The OWNER's engraved-label look, carried as one value from the board's tuning down to each
    /// mirrored cap: fill colour, keyline colour and width, and the two switches
    /// ([ButtonColors] LabelR/G/B, LabelOutlineR/G/B, LabelOutlineWidth, LabelOutline,
    /// LabelUnderlay — record 28 ids 48 / 49 / 170 / 229 / 230).
    ///
    /// <para>It is a struct passed by <c>in</c> rather than five parameters because the set has to
    /// travel INTACT. The bug it replaces is instructive: <c>BuildLabel</c> used to call the no-arg
    /// <c>NativeButtonSkin.StyleEngravedLabel</c>, which reads the LOCAL player's config — so a peer's
    /// cap wore the VIEWER's keyline — and it never assigned <c>label.color</c> at all, so the fill
    /// stayed at TMP's default white while the local cap's is warm parchment. Two different failures,
    /// one for each half of the label's look, and mixing this peer's fill with that viewer's keyline
    /// would have been a third.</para>
    /// </summary>
    private readonly struct CapLabelStyle
    {
        public readonly Color Fill;
        public readonly Color OutlineColor;
        public readonly float OutlineWidth;
        public readonly bool OutlineOn;
        public readonly bool UnderlayOn;

        public CapLabelStyle(Color fill, Color outlineColor, float outlineWidth,
                             bool outlineOn, bool underlayOn)
        {
            Fill = fill;
            OutlineColor = outlineColor;
            OutlineWidth = outlineWidth;
            OutlineOn = outlineOn;
            UnderlayOn = underlayOn;
        }
    }

    private sealed class InertCap
    {
        /// <summary>Mirror of PlayTray.BoardButton.CapRestZ — the cap's seat toward the viewer.</summary>
        private const float CapRestZ = -0.004f;

        // THE MIRRORED 7 mm CHAMFER RETIRED WITH THE OWNER'S (round 2, 2026-08-25). The board keycap
        // is a SIGNET PLATE now — outer chamfer, flat rim land, inner chamfer, recessed field — and
        // its bezel is a FRACTION of the cap's own short side rather than a length in meters, so
        // there is nothing left here to copy and therefore nothing left to drift.
        // CardMesh.BuildBeveledKeycap owns it, and both boards call that one builder.

        // THE MIRRORED ButtonCluster GEOMETRY CONSTANTS ARE GONE (2026-08-25). Eight of them stood
        // here — well thickness, well margin, cap standoff, label proud, the three label-box numbers
        // and the round disc height — copied out of WorldUI/ButtonCluster's own BuildProcedural so
        // that the mirrored turn-flow SKIP cap could be built to that cluster's proportions rather
        // than the board keycaps'. There is no cluster to copy any more: the skip is a generic board
        // keycap, so it takes the beveled-keycap branch below with Confirm and Undo. A copy whose
        // original has been deleted is the worst kind of constant to leave behind.

        // Mirrors of PlayTray.BoardButton's wall/bevel tint recipe (checked by check-mirrors.sh).
        private const float WallTintFactor = 0.50f;
        private const float WallWarmLerp = 0.42f;
        private const float BevelLerp = 0.48f;
        private static readonly Color WallWarm = new(0.17f, 0.11f, 0.06f);
        private static readonly Color BevelHighlight = new(0.66f, 0.53f, 0.32f);

        private readonly GameObject _go;
        private readonly TextMeshPro _label;

        /// <summary>This cap's caption fit box in METERS, kept so <see cref="SetLabel"/> can re-run
        /// the solved layout on every new wording. Zero on a cap built before the box was known,
        /// which simply skips the re-fit rather than fitting into nothing.</summary>
        private Vector2 _labelBox;

        /// <summary>Which cap this is, for the caption-does-not-fit log line. The mirror names
        /// itself "peer &lt;cap&gt;" so a report from a session with two boards on screen says which
        /// board the unfitted caption was on.</summary>
        private string _labelContext = "peer cap";
        private string _shown = string.Empty;

        /// <summary>The travelling CAP holder — the transform the local
        /// <c>BoardButton.Update</c> sinks along local +Z on a press, here driven by
        /// <see cref="RemoteCapFx"/> from the synced press edge.</summary>
        private Transform? _capMesh;

        /// <summary>This cap's per-frame animator (press dip, dust dissolve, materialize fade). A
        /// pure RENDERING component: it writes a transform, a scale and material colours, adds no
        /// collider and registers nowhere. Null only in a shader-less environment where nothing
        /// could animate anyway.</summary>
        private RemoteCapFx? _fx;

        /// <summary>The colour this cap was BUILT with, i.e. the <c>_accentColor</c> the local
        /// <c>BoardButton.Create</c> was handed — the ACCENT entry of the state palette, and the
        /// only one of the four looks a peer's board used to be able to show.</summary>
        private Color _accentColor;

        /// <summary>Last applied (enabled, accent, confirmed) triple, packed — the change gate for
        /// <see cref="SetCapState"/>. -1 = nothing applied yet, so the first refresh always paints.</summary>
        private int _shownState = -1;

        /// <summary>The engraved label's authored colour, so the cluster-style disabled fade can be
        /// applied and undone without drifting.</summary>
        private Color _labelBase = Color.white;

        /// <summary>The cap's three live material instances (top plateau / bright bevel / dark
        /// warm wall) so a STATE colour change can be re-applied to all three at once — the local
        /// <c>BoardButton.SetCapColor</c> drives exactly the same trio, which is what makes an
        /// accented remote cap read identically to an accented local one. Null entries on the
        /// round discs (one material) and whenever no cap shader resolved.</summary>
        private Material? _topMat, _bevelMat, _wallMat;

        /// <summary>WHICH control this mirrored cap is, and whose board it belongs to — the cell of
        /// the peer's own keycap atlas its face samples. Kept so the follow/pin toggle can swap its
        /// symbol in place when the owner's pinned bit changes, which is the one mirrored cap whose
        /// meaning is not fixed.</summary>
        private Cards.CapRole _capRole = Cards.CapRole.Plain;
        private Cards.ControlBoard? _capStyle;

        /// <summary>Point this mirrored cap's face at a different role, through the owner's own
        /// helper (<c>PlayTray.SetKeycapRole</c>) so the two boards resolve the cell identically.
        /// Two floats on a material instance — no rebuild, so the mirrored dust dissolve never
        /// fires for a state change the owner did not dissolve for either.</summary>
        public void SetCapRole(Cards.CapRole role)
        {
            if (_capRole == role || _capStyle == null)
                return;
            if (Cards.PlayTray.SetKeycapRole(_topMat, role, _capStyle.Value))
                _capRole = role;
        }

        /// <summary>Change gate for <see cref="SetTint"/> — a material write per 4 Hz refresh is
        /// exactly the churn the cadence exists to avoid. Holds the APPLIED colour, i.e. after
        /// <see cref="_capTint"/>, so the gate compares what was actually written.</summary>
        private Color _tint = new(-1f, -1f, -1f, -1f);

        /// <summary>
        /// The OWNER's [ButtonColors] cap-FACE tint for this cap's category — the multiplier
        /// <c>PlayTray.BoardButton.StateColor</c> applies to the shared state palette (and
        /// <c>ButtonCluster.PhysicalButton</c> applies to its accent). White = identity.
        ///
        /// <para>ITS ABSENCE WAS A TWO-TIMES BRIGHTNESS ERROR ON EVERY MIRRORED KEYCAP. The shipped
        /// tint is 0.5 grey, not white, so a local cap rests at HALF the palette colour and this
        /// class was writing the palette colour raw — for two players who had never tuned anything.
        /// It is applied in <see cref="SetTint"/> rather than folded into the colours the builders
        /// are handed, so that a state change arriving later goes through the same multiply and the
        /// four state looks cannot drift apart from each other.</para>
        /// </summary>
        private Color _capTint = Color.white;

        private InertCap(GameObject go, TextMeshPro label)
        {
            _go = go;
            _label = label;
        }

        /// <summary>
        /// The square keycap: dark base plate + the 3-submesh SIGNET cap mesh (state field /
        /// bright rim land / dark warm walls), <c>capThick = Max(0.012, depth)</c> — term for term
        /// what <c>PlayTray.BoardButton</c> builds.
        ///
        /// <para>The bevel is NOT a number this side passes any more. It used to be
        /// <c>CapBevel</c>, mirrored against <c>PlayTray.SquareCapBevel</c> and machine-checked as
        /// a <c>check-mirrors.sh</c> group; ModBuild 286 deleted both, because a fixed 7 mm chamfer
        /// on caps from 53.2x43.9 to 63.0x62.1 mm is 0.113 of one cap's short side and 0.159 of
        /// another's — "one constant" was never one proportion. The five-zone profile lives in
        /// <c>Cards.CapFaceLayout</c> as FRACTIONS and is computed inside
        /// <c>CardMesh.BuildBeveledKeycap</c>, which both boards already call, so there is one
        /// recipe and nothing left to keep in step.</para>
        ///
        /// <para>IT USED TO HAVE TWO SHAPES, and losing the second one is the point rather than a
        /// simplification. The other branch built a plain <c>PrimitiveType.Cube</c> — one material,
        /// flat top, sharp edges, no bevel ring, no thickness clamp, over a (capW x 1.2) x 0.012
        /// well — because the turn-flow SKIP cap mirrored a
        /// <c>WorldUI.ButtonCluster.PhysicalButton</c>, which was a different object from a board
        /// keycap and had to be reproduced as one (user report 2026-08-13: "Die Überspringen Knöpfe
        /// sehen nicht 1:1 genauso aus, wie auf dem echten board, etwas andere Form"). The user then
        /// retired the group outright and asked for the opposite property — "so dass all diese
        /// buttons gleich aussehen" — so the skip cap IS a board keycap on both sides now, and one
        /// branch is the accurate mirror of one original.</para>
        /// </summary>
        public static InertCap Square(Transform parent, string name, Vector3 localPos, Vector2 size,
            float depth, Color color, Color capTint, in CapLabelStyle labels,
            float travel = 0f, Color? accent = null,
            Cards.CapRole role = Cards.CapRole.Plain, Cards.ControlBoard? style = null)
        {
            GameObject go = NewRoot(parent, name, localPos);
            // WHICH CONTROL, AND WHOSE BOARD. The style is the PEER's synced board style, so a peer
            // on the bronze board is drawn with bronze keys carrying the bronze board's own rest
            // motifs — the same resolution their own client makes, from the same prefab and the
            // same atlas, with no wire field for either.
            bool hasSymbol = style != null && role != Cards.CapRole.Plain
                             && Cards.CapSymbols.TryAtlas(style.Value, out _, out _);
            // The bezel/wall cell, through the SAME resolver the owner's BoardButton.Create uses.
            // This is the square branch, so it resolves to the square band — but it is not written
            // as a literal, because round and square are one decision and this mirror is the half
            // of it that no compiler ties to the other.
            Cards.CapRole plainCell = Cards.CapCellMath.PlainCell(round: false);
            // The owner's cap-face tint — see InertCap._capTint — SEATED like every other cap
            // colour in this mod (2026-08-09 round 3): the build writes this straight onto the
            // materials, so it must clear the WELL behind it before SetTint ever runs.
            Color face = WorldUI.ButtonTuning.SeatedCapColor(color * capTint);

            // Base plate: the recessed well the cap sits in — BoardButton's own non-round branch.
            var basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "Base";
            Object.Destroy(basePlate.GetComponent<Collider>());
            basePlate.transform.SetParent(go.transform, worldPositionStays: false);
            basePlate.transform.localScale = new Vector3(size.x + 0.008f, size.y + 0.008f, 0.006f);
            basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            TintLit(basePlate, WorldUI.ButtonTuning.CapWellColor); // the colour SeatedCapColor floors against

            float capThick = Mathf.Max(0.012f, depth);
            var capMesh = new GameObject("CapMesh");
            capMesh.transform.SetParent(go.transform, worldPositionStays: false);
            capMesh.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            Shader? shader = CapShader();
            Material? top = null, bevel = null, wall = null;
            capMesh.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.BuildBeveledKeycap(size.x, size.y, capThick, style);
            MeshRenderer mr = capMesh.AddComponent<MeshRenderer>();
            if (shader != null)
            {
                // ONE CALL, TWO BOARDS. This is PlayTray.NewKeycapMaterial — the owner's own
                // builder — so the per-board atlas, the role cell and the fallback to the shared
                // grain are all resolved by the same code on both sides. The symbol goes on the
                // TOP plateau only; the bevel ring and the walls take the plain cell of the same
                // atlas, exactly as BoardButton.Create does it.
                top = Cards.PlayTray.NewKeycapMaterial(shader, face, role, style);                                 // [0] top plateau
                bevel = Cards.PlayTray.NewKeycapMaterial(shader, BevelTint(face), plainCell, style);                // [1] bright bevel
                wall = Cards.PlayTray.NewKeycapMaterial(shader, WallTint(face), plainCell, style);                  // [2] dark warm wall
                mr.sharedMaterials = new[] { top, bevel, wall };
            }

            // The LABEL hangs off the CAP holder on the local board precisely so it travels with
            // the cap on a press ("it used to hang off the static root while only the cap sank,
            // reading as detached"). Same parenting here, so the mirrored dip moves the same parts.
            // The caption floats just proud of the RECESSED FIELD, exactly as on the owner's board
            // (BoardButton.Create) — the field is one step BACK from the rim land now, and a label
            // seated at the old plane would hover a visible gap in front of the plate.
            Cards.CardMesh.CapProfile(Mathf.Min(size.x, size.y), Mathf.Min(size.x, size.y) * 0.5f,
                                      capThick, out _, out _, out float capStep);
            TextMeshPro label = BuildLabel(capMesh.transform, size,
                new Vector3(0f, 0f, -capThick + capStep - Cards.CardMesh.LabelProudOfField),
                in labels, name, role, hasSymbol, out Vector2 fitBox);
            var cap = new InertCap(go, label)
            {
                _topMat = top,
                _bevelMat = bevel,
                _wallMat = wall,
                _labelBox = fitBox,
                _labelContext = "peer " + name,
                _capTint = capTint,
                _tint = face,
                _capMesh = capMesh.transform,
                _accentColor = accent ?? color,
                _labelBase = label.color,
                _capRole = role,
                _capStyle = style,
            };
            cap.AttachFx(travel, Mathf.Max(size.x, size.y));
            return cap;
        }

        /// <summary>The round disc cap (rest discs, and any generic keycap whose board style is
        /// Round): recessed well ring + smooth generated disc, in the same carved-grain keycap
        /// material family.
        ///
        /// <para>THE CLUSTER-STYLE DEPTH BRANCH IS GONE (2026-08-25). It hard-coded an 18 mm disc
        /// height for the turn-flow SKIP cap because WorldUI/ButtonCluster's own round branch
        /// ignored its depth dial and built <c>GetRoundCap(capW, 2f * 0.009f)</c> — copied rather
        /// than corrected, because 1:1 means "what the owner sees". The owner sees a board keycap
        /// now, so the honest mirror is this method's ordinary thickness.</para></summary>
        public static InertCap Round(Transform parent, string name, Vector3 localPos, float diameter,
            float thickness, Color color, Color capTint, in CapLabelStyle labels,
            float travel = 0f, Color? accent = null,
            Cards.CapRole role = Cards.CapRole.Plain, Cards.ControlBoard? style = null)
        {
            GameObject go = NewRoot(parent, name, localPos);
            bool hasSymbol = style != null && role != Cards.CapRole.Plain
                             && Cards.CapSymbols.TryAtlas(style.Value, out _, out _);
            // THE BEZEL/WALL CELL, AND WHY IT IS NOT CapRole.Plain HERE. Cell 0's gold band is
            // registered against a SQUARE, so a disc sampling it drew a mitred rectangle inside a
            // round cap — the square-looking texture the user reported on his own board, which
            // this mirror reproduced faithfully because it had copied the literal. Resolved through
            // the same CapCellMath.PlainCell the owner's BoardButton.Create calls, so the owner's
            // rest disc and every peer's copy of it cannot end up on different cells.
            Cards.CapRole plainCell = Cards.CapCellMath.PlainCell(round: true);
            // The owner's cap-face tint — see InertCap._capTint — SEATED like every other cap
            // colour in this mod (2026-08-09 round 3): the build writes this straight onto the
            // materials, so it must clear the WELL behind it before SetTint ever runs.
            Color face = WorldUI.ButtonTuning.SeatedCapColor(color * capTint);

            var basePlate = new GameObject("Base");
            basePlate.transform.SetParent(go.transform, worldPositionStays: false);
            basePlate.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            basePlate.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.GetRoundCap(diameter + 0.006f, 0.006f);
            var baseMr = basePlate.AddComponent<MeshRenderer>();

            float capThick = Mathf.Max(0.002f, thickness);
            var capDisc = new GameObject("CapMesh");
            capDisc.transform.SetParent(go.transform, worldPositionStays: false);
            capDisc.transform.localPosition = new Vector3(0f, 0f, CapRestZ);
            // ROUND 2: the disc is a SIGNET PLATE on both boards — same profile, same three
            // submeshes as the square cap. GetRoundKeycap, not GetRoundCap: the plain disc is still
            // what the BASE plate above (and the map room's rail) wants, and it has one submesh.
            capDisc.AddComponent<MeshFilter>().sharedMesh =
                Cards.CardMesh.GetRoundKeycap(diameter, capThick, style);
            var capMr = capDisc.AddComponent<MeshRenderer>();

            Shader? shader = CapShader();
            Material? disc = null, discBevel = null, discWall = null;
            if (shader != null)
            {
                baseMr.sharedMaterial = new Material(shader) { color = WorldUI.ButtonTuning.CapWellColor };
                disc = Cards.PlayTray.NewKeycapMaterial(shader, face, role, style);                            // [0] field
                discBevel = Cards.PlayTray.NewKeycapMaterial(shader, BevelTint(face), plainCell, style);          // [1] bezel — ROUND-registered band
                discWall = Cards.PlayTray.NewKeycapMaterial(shader, WallTint(face), plainCell, style);            // [2] wall — ROUND-registered band
                capMr.sharedMaterials = new[] { disc, discBevel, discWall };
            }

            Cards.CardMesh.CapProfile(diameter, diameter * 0.5f, capThick,
                                      out _, out _, out float discStep);
            TextMeshPro label = BuildLabel(capDisc.transform, new Vector2(diameter, diameter),
                new Vector3(0f, 0f, -capThick * 0.5f + discStep - Cards.CardMesh.LabelProudOfField),
                in labels, name, role, hasSymbol, out Vector2 fitBox);
            var cap = new InertCap(go, label)
            {
                // The disc carries the SAME three materials as the square cap since round 2 — the
                // local round BoardButton does too, so SetCapColor drives the same three on both.
                _topMat = disc,
                _bevelMat = discBevel,
                _wallMat = discWall,
                _labelBox = fitBox,
                _labelContext = "peer " + name,
                _capTint = capTint,
                _tint = face,
                _capMesh = capDisc.transform,
                _accentColor = accent ?? color,
                _labelBase = label.color,
                _capRole = role,
                _capStyle = style,
            };
            cap.AttachFx(travel, diameter);
            return cap;
        }

        private static GameObject NewRoot(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            return go;
        }

        /// <summary>
        /// The engraved parchment label the local caps wear (<c>NativeButtonSkin.StyleEngravedLabel</c>
        /// + TmpFit inside the cap face) — IN THE OWNER'S [ButtonColors] LOOK, not this client's.
        ///
        /// <para>TWO DRIFTS LIVED IN THE THREE LINES THIS REPLACES, and neither could be seen from
        /// inside a headset. (1) It never assigned <c>tmp.color</c>, so every mirrored keycap letter
        /// was TMP's default pure WHITE while the local <c>BoardButton.Create</c> assigns
        /// <c>NativeButtonSkin.LabelColor</c> — the warm parchment #FBF3E0. (2) It called the no-arg
        /// <c>StyleEngravedLabel</c>, which reads the LOCAL config, so the keyline and drop-shadow on
        /// a PEER's board followed the VIEWER's [ButtonColors] dials: turn your own keyline red and
        /// every team-mate's board grew red keylines on your screen alone. The overload exists for
        /// exactly this call site.</para>
        ///
        /// <para>Colour BEFORE style, and both before the fit: <c>StyleEngravedLabel</c> writes the
        /// per-label font-material instance, which only exists once a font is assigned, and TmpFit's
        /// auto-size is what the label is finally measured at.</para>
        /// </summary>
        /// <summary>
        /// A mirrored cap's engraved label.
        ///
        /// <para>THE OTHER HALF OF THE SKIP REPORT (user 2026-08-13: "der Text ist etwas
        /// transparenter"). Three causes, all of them here, none of them a colour constant — the
        /// fill alpha was already 1.0 on both sides:</para>
        /// <list type="number">
        ///   <item><b>No <c>ApplyFont</c>.</b> Every original — <c>PlayTray.BoardButton</c>
        ///     (PlayTray.7.Nested) and <c>ButtonCluster.PhysicalButton</c> alike — calls
        ///     <c>NativeButtonSkin.ApplyFont</c>, which swaps in the HARVESTED GAME HUD FONT and
        ///     then makes its material depth-honest (renderQueue 3000, ZTest LEqual). This mirror
        ///     did not, so it kept TMP's default face and default material: a different SDF with a
        ///     different gradient scale, thinner stems, and — worse — <c>StyleEngravedLabel</c> then
        ///     wrote the keyline and underlay onto THAT material, where the same numbers buy much
        ///     less coverage. Washed-out glyphs are exactly what that produces.</item>
        ///   <item><b>No sorting order.</b> The originals set <c>sortingOrder = 3</c> on the label
        ///     renderer so it always resolves in front of the cap face; without it the label
        ///     arbitrates by depth alone against an opaque plateau one or two millimetres away.</item>
        /// </list>
        ///
        /// <para>A third cause used to be listed here — the skip cap was fitted into the BOARD
        /// keycap box while its original used the cluster's own smaller 0.105 x 0.045 at maxFont
        /// 0.30 — and it retired with the cluster: every cap this mirror draws is a board keycap
        /// now, so there is one fit box and it is the right one for all of them.</para>
        /// </summary>
        private static TextMeshPro BuildLabel(Transform parent, Vector2 size, Vector3 localPos,
                                              in CapLabelStyle labels, string context,
                                              Cards.CapRole role, bool hasSymbol,
                                              out Vector2 fitBox)
        {
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(parent, worldPositionStays: false);
            // THE SYMBOL AND THE CAPTION SHARE ONE FACE, resolved through the same CapSymbols the
            // owner's BoardButton.Create resolves it through — not through a copy of its numbers.
            // That is what makes the mirrored cap's caption land in the same band as the owner's,
            // under the same carved symbol, with nothing left to drift.
            float labelDy = Cards.CapSymbols.LabelCentreY(role, hasSymbol) * size.y;
            Vector2 labelBox = Cards.CapSymbols.LabelBox(role, hasSymbol);
            labelGo.transform.localPosition = localPos + new Vector3(0f, labelDy, 0f);
            var tmp = labelGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            // The local cap falls back to white when no HUD font has been harvested yet, because
            // the parchment fill is only legible on the skinned face — same ladder here, so the two
            // boards agree in the un-skinned case as well as the skinned one.
            tmp.color = WorldUI.NativeButtonSkin.HasFont ? labels.Fill : Color.white;
            // FONT FIRST, ENGRAVING SECOND — the order both originals use. StyleEngravedLabel
            // writes onto the label's CURRENT material, so a font swap after it would discard the
            // keyline; a font swap before it is what makes the two boards' glyphs the same object.
            WorldUI.NativeButtonSkin.ApplyFont(tmp);
            tmp.color = WorldUI.NativeButtonSkin.HasFont ? labels.Fill : Color.white; // ApplyFont must not undo the fill
            WorldUI.NativeButtonSkin.StyleEngravedLabel(tmp, labels.OutlineColor, labels.OutlineWidth,
                                                        labels.OutlineOn, labels.UnderlayOn);
            var labelRenderer = tmp.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
                labelRenderer.sortingOrder = 3; // the originals' own order, above the cap face
            fitBox = new Vector2(size.x * labelBox.x, size.y * labelBox.y);
            // THE SOLVED LAYOUT, NOT AUTO-SIZE + TRUNCATE — the owner's own call, so a caption that
            // wraps to two lines on his cap wraps to two lines on the mirror. This is the seam the
            // ModBuild 281 truncation would have crossed unchanged: both sides went through
            // TmpFit.Fit, so both sides said "AUSWAHL BEEN".
            TmpFit.FitCapLabel(tmp, fitBox.x, fitBox.y, maxFontSize: 0.40f, context: context);
            // A SYMBOL-ONLY CAP DRAWS NO CAPTION, exactly as on the owner's board: the rest discs
            // and the follow/pin toggle carry their symbol alone and their WORD is engraved into
            // the board beside them. The renderer is disabled rather than the object destroyed, so
            // the mirrored string is still there to come back if this peer's bundle turns out to
            // have no keycap atlas.
            if (hasSymbol && Cards.CapSymbols.SymbolOnly(role) && labelRenderer != null)
                labelRenderer.enabled = false;
            return tmp;
        }

        /// <summary>BoardLit (shades walls even in the unlit scenes) with the same fallback ladder
        /// as <c>PlayTray.BoxCapShader</c>.</summary>
        private static Shader? CapShader() =>
            Cards.PlayTray.BoardLitShader()
            ?? Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse")
            ?? Shader.Find("Sprites/Default");

        private static void TintLit(GameObject go, Color color)
        {
            Shader? shader = CapShader();
            if (shader != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = color };
        }

        private static Color WallTint(Color top)
        {
            var dark = new Color(top.r * WallTintFactor, top.g * WallTintFactor, top.b * WallTintFactor, top.a);
            Color w = Color.Lerp(dark, WallWarm, WallWarmLerp);
            w.a = top.a;
            return w;
        }

        private static Color BevelTint(Color top)
        {
            Color b = Color.Lerp(top, BevelHighlight, BevelLerp);
            b.a = top.a;
            return b;
        }

        /// <summary>Change-gated label write (a per-tick TMP assignment re-triggers auto-size).
        /// Runs the shared tofu strip first (WorldUI.NativeButtonSkin.SanitizeLabel): the chip
        /// labels are the same translated strings the LOCAL keycaps show, so a glyph the mod
        /// font cannot render would box identically on the peer's mirror — both sides must
        /// show exactly the same thing (MP rule), including the same clean fallback.</summary>
        public void SetLabel(string text)
        {
            text = WorldUI.NativeButtonSkin.SanitizeLabel(_label, text);
            if (text == _shown)
                return;
            _shown = text;
            _label.text = text;
            // RE-FIT, EVERY TIME THE STRING CHANGES — the owner's BoardButton.SetLabel does exactly
            // this, and for exactly this reason: a caption fitted once at build time is a caption
            // fitted for a string this cap no longer shows. Every wording on this mirror arrives
            // through this method (NetProtocol.ExtIdCapLabels), so without the re-fit the mirror
            // would be the ONLY board still wearing the ModBuild 281 defect.
            if (_labelBox.x > 0f && _labelBox.y > 0f)
                TmpFit.FitCapLabel(_label, _labelBox.x, _labelBox.y, maxFontSize: 0.40f,
                                   context: _labelContext);
        }

        /// <summary>
        /// Give this cap its per-frame animator. <paramref name="travel"/> is the AUTHORED press
        /// travel of the cap's category (0 = a cap whose presses are not mirrored — it still gets
        /// the show/hide dust, which needs no travel); <paramref name="footprint"/> sizes the dust
        /// burst exactly as the local button sizes its own from its trigger box.
        /// </summary>
        private void AttachFx(float travel, float footprint)
        {
            _fx = _go.AddComponent<RemoteCapFx>();
            _fx.Init(_capMesh, CapRestZ, travel, footprint, CurrentStateColor, PaintCap);
        }

        /// <summary>Replay the owner's press dip on this copy (synced press edge). A no-op on a cap
        /// with no animator or no travel. NOTHING is invoked — this is the animation, not the
        /// button.</summary>
        public void Press() => _fx?.Press();

        /// <summary>
        /// Show or hide the cap. <paramref name="animate"/> false pops it (the build-time seeding
        /// and the first refresh, mirroring the local button's own <c>_ticked</c> suppression of
        /// the build-then-settle storm); true plays the owner's own transition — the dust dissolve
        /// on the way out, the materialize-from-dust on the way in.
        /// </summary>
        public void SetShown(bool shown, bool animate = false)
        {
            if (_go.activeSelf == shown && !(shown && animate && _fx != null && _fx.Hiding))
                return;
            if (!animate || _fx == null || !WorldUI.ButtonTuning.ButtonAnimEnabled)
            {
                _fx?.CancelAnimations();
                if (_go.activeSelf != shown)
                    _go.SetActive(shown);
                return;
            }
            if (shown)
            {
                if (!_go.activeSelf)
                    _go.SetActive(true);
                _fx.PlayAppear();
            }
            else
            {
                // The local hide is LOGICAL first and visual after; there is no logical half here
                // (nothing was ever interactive), so this is the visual half alone — the cap stays
                // active for the shrink and the animator deactivates it at the end.
                if (!_go.activeInHierarchy)
                {
                    _go.SetActive(false);
                    return;
                }
                _fx.PlayDissolve();
            }
        }

        /// <summary>
        /// Apply the owner's live cap STATE — the inert counterpart of
        /// <c>PlayTray.BoardButton.SetState</c> + <c>UpdateColor</c>, resolving the same four looks
        /// in the same precedence (disabled beats confirmed beats accent beats idle) out of the
        /// same palette — the turn-flow SKIP cap included, since it became one of them.
        /// Change-gated on the packed triple.
        /// </summary>
        public void SetCapState(bool enabled, bool accent, bool confirmed)
        {
            int key = (enabled ? 1 : 0) | (accent ? 2 : 0) | (confirmed ? 4 : 0);
            if (key == _shownState)
                return;
            _shownState = key;
            SetTint(StateColor(enabled, accent, confirmed));
        }

        /// <summary>The colour this cap should rest at for a state triple — see
        /// <see cref="SetCapState"/>.</summary>
        private Color StateColor(bool enabled, bool accent, bool confirmed)
        {
            return !enabled ? CapDisabledColor
                : confirmed ? CapConfirmedColor
                : accent ? _accentColor
                : CapIdleColor(_capStyle);
        }

        /// <summary>The cap's resting STATE colour right now — what the materialize fade ramps up
        /// to and what the dust burst is coloured with, exactly like the local button's
        /// <c>CurrentCapColor</c>.</summary>
        private Color CurrentStateColor() => _tint;

        /// <summary>
        /// Write the ASSEMBLY ramp onto the cap's live materials WITHOUT touching the change gate —
        /// the animator uses this, so completing a transition restores the true state colour through
        /// <see cref="SetTint"/>'s gate rather than fighting it.
        ///
        /// <para>Mirror of <c>PlayTray.BoardButton.ApplyAssembly</c>, and deliberately built out of
        /// the SAME <c>WorldUI.ButtonTuning</c> helpers rather than a copy of the curve: the peer's
        /// cap therefore crumbles and assembles with the owner's own bevel-leads / walls-trail
        /// stagger, at the owner's own dust colour, by construction. <paramref name="k"/> is the
        /// overall progress (0 = pure dust, 1 = settled); <paramref name="rest"/> is the settled TOP
        /// colour the bevel/wall tints are derived from, exactly as at rest.</para>
        /// </summary>
        private void PaintCap(Color rest, float k)
        {
            if (_topMat != null)
                _topMat.color = WorldUI.ButtonTuning.AssemblyColor(rest,
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Top));
            if (_bevelMat != null)
                _bevelMat.color = WorldUI.ButtonTuning.AssemblyColor(BevelTint(rest),
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Bevel));
            if (_wallMat != null)
                _wallMat.color = WorldUI.ButtonTuning.AssemblyColor(WallTint(rest),
                    WorldUI.ButtonTuning.AssemblyPhase(k, WorldUI.ButtonTuning.CapPart.Wall));
        }

        /// <summary>
        /// Re-tint the cap to a STATE colour — the inert counterpart of the local
        /// <c>BoardButton.SetCapColor</c>, driving the same three submesh materials with the same
        /// two derived tints, so an accented cap on a peer's board is the same colour as the
        /// accented cap on its owner's. Change-gated; a no-op on a cap whose shader never resolved.
        ///
        /// <para><paramref name="color"/> is the UNTINTED palette entry, exactly as the local
        /// <c>StateColor()</c> selects it; the owner's per-category [ButtonColors] face tint is
        /// applied HERE, in the one place, which is what puts every one of the four state looks and
        /// every external re-tint (the FOLLOW/PIN toggle) through the same multiply. The change gate
        /// compares the APPLIED colour so it is gating what was actually written to the material.</para>
        /// </summary>
        public void SetTint(Color color)
        {
            // SEATED, exactly as on the owner's own board (2026-08-09 round 3): the per-category
            // face tint is applied here, in the one place — so the floor that keeps a cap from
            // rendering darker than the WELL behind it belongs here too, or a peer's copy of the
            // board would still show the hole-with-a-label the owner's no longer can. Same shared
            // WorldUI.ButtonTuning helper both local builders call, so the 1:1 mirror rule holds by
            // construction rather than by a second derivation that can drift.
            Color applied = WorldUI.ButtonTuning.SeatedCapColor(color * _capTint);
            if (applied == _tint)
                return;
            _tint = applied;
            color = applied;
            // A state change that lands DURING a mirrored appear/dissolve re-aims the ramp instead of
            // painting the settled colour over it — otherwise a peer's cap flashes finished inside
            // its own arrival. Mirror of PlayTray.BoardButton.UpdateColor's re-aim, and the reason
            // the owner's and the peer's cap survive a mid-transition state flip identically.
            if (_fx != null && _fx.Transitioning)
            {
                _fx.ReAim(color);
                return;
            }
            if (_topMat != null) _topMat.color = color;
            if (_bevelMat != null) _bevelMat.color = BevelTint(color);
            if (_wallMat != null) _wallMat.color = WallTint(color);
        }
    }

    // ---------------------------------------------------------------- inertness guarantee --

    /// <summary>
    /// BELT AND BRACES for the "nothing on a peer's board may be interactable" rule: walk the
    /// finished hierarchy and destroy any <c>Collider</c> that made it in. Today none can — every
    /// builder above strips primitive colliders at creation (and <see cref="RemoteTrayVisual"/>
    /// silently strips the prefab's expected ones before this guard ever runs) — but a future edit
    /// that adds a plain <c>CreatePrimitive</c> would silently make a peer's board pokeable, and
    /// that is exactly the regression this catches. The warning it logs is deliberately loud
    /// (grep: "remote board furniture carried").
    /// </summary>
    internal static void StripColliders(GameObject root, string who)
    {
        Collider[] found = root.GetComponentsInChildren<Collider>(includeInactive: true);
        if (found == null || found.Length == 0)
            return;
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] != null)
                Object.Destroy(found[i]);
        }
        VRLog.Warn("Net", $"INERT-GUARD: {who} carried {found.Length} collider(s) — destroyed. " +
                          "A remote player's control board is a pure display: nothing on it may be " +
                          "pokeable, laser-targetable or grabbable.");
    }

    /// <summary>
    /// Release what this furniture owns beyond the board root's own subtree — today exactly one
    /// thing: the mirrored decision row's clone, which is registered with <c>MrBacking</c> and so
    /// must be released explicitly rather than left to Unity's destruction order (the same
    /// ownership contract <c>RemoteControlBoard</c> already honours for the track and objectives
    /// mirrors). Everything else here is a child of the board root and dies with it.
    /// </summary>
    public void Destroy() => _decisionWidgets?.Destroy();
}

/// <summary>
/// THE MIRRORED KEYCAP ANIMATIONS — the inert twin of what <c>PlayTray.BoardButton.Update</c> and
/// <c>SetVisible</c> do to a real board button, run on a peer's colliderless copy.
///
/// <para>Locally a cap sinks under a fingertip, springs back on a click, crumbles into dust when it
/// is taken away and assembles out of dust when it returns. On a peer's board every one of those
/// was a POP. Under the 1:1 ruling ("alle Interaktionen, ANIMATIONEN und Anzeigen des
/// Controllboards … so wie der Spieler sie sieht") the transitions have to look the same, and three
/// of the four need no wire at all: the show/hide edge is already synced by the board-UI record, so
/// the receiver plays the owner's own dissolve/materialize off a transition it can already see.
/// Only the PRESS is an event with no state behind it, and that is the one field that was added
/// (record 14 byte 0 bits 3..7).</para>
///
/// <para>THE PRESS SHAPE is not copied from the original any more, it IS the original: both sides
/// advance a phase in seconds from the press edge and read
/// <c>WorldUI.ButtonStroke.Depth01</c>, which is one attack, one detent and one spring-back
/// past rest. The cap sits at <c>CapRestZ + Travel × depth</c> on both boards, and the depth is
/// the same number. The FINGER-FOLLOW half of the local motion
/// (the cap tracking penetration depth continuously while a fingertip hovers) is deliberately NOT
/// reproduced: it is a per-frame function of the owner's fingertip position, it would cost a
/// per-frame stream to sync, and what it exists to telegraph — the commit — is exactly what the
/// press edge already delivers.</para>
///
/// <para>IT IS A RENDERING COMPONENT AND NOTHING ELSE. It writes a transform, a local scale and
/// material colours, and it emits into the SHARED, pooled, purely-visual
/// <c>WorldUI.ButtonDissolveFx</c> particle system the local buttons use. It creates no collider,
/// no rigidbody and no registration of any kind, so <c>RemoteBoardFurniture.StripColliders</c> has
/// nothing to find — an ANIMATION is not interactivity.</para>
///
/// <para>The viewer's <c>[ButtonAnim] Enable</c> switch gates it, exactly as it gates their own
/// board's caps: a player who has turned keycap animation off has turned it off, and a remote board
/// is not the place to re-impose it. The DURATIONS, by contrast, are the AUTHORED ones and not the
/// viewer's tuning — the same rule every geometry constant on this board follows.</para>
/// </summary>
internal sealed class RemoteCapFx : MonoBehaviour
{
    // THE PRESS SHAPE IS NO LONGER MIRRORED — IT IS SHARED, and deleting the second copy is the
    // resolution scripts/check-mirrors.sh's own header keeps recommending. This class used to
    // carry `PressDecayPerSecond = 6f`, a hand-kept copy of the owner's inline
    // `Mathf.MoveTowards(_press, 0f, Time.deltaTime * 6f)`, and that pair was one of the lint's
    // nineteen groups. Both sides now advance a PHASE IN SECONDS and ask
    // WorldUI.ButtonStroke.Depth01 what depth that phase is, so the mirrored press IS the
    // owner's press rather than a number that has to agree with it — the same fix AssemblyColor
    // and DecisionDockSurface.BarClearanceMeters already got, and the group is deleted from the
    // lint rather than re-pointed. The stroke itself is new (attack, detent, spring-back past
    // rest); see that function for what it replaced and why it runs on the UNSCALED clock.

    // THE APPEAR/DISSOLVE SURFACE RAMP IS NO LONGER MIRRORED — IT IS SHARED. This class used to
    // carry `AppearFadeFloor = 0.15f`, a hand-kept copy of the local `Mathf.SmoothStep(0.15f, 1f, k)`
    // that scripts/check-mirrors.sh could only describe in prose (the local half was an inline
    // literal, so its float extractor could not reach it). The 2026-08-09 invisible-cap round
    // replaced that multiply-toward-black fade with the assembly ramp in
    // <c>WorldUI.ButtonTuning.AssemblyColor</c>/<c>AssemblyPhase</c>, and this side simply CALLS it —
    // so owner and peer are the same code rather than two numbers that have to agree. Same
    // resolution <c>DecisionDockSurface.BarClearanceMeters</c> got, and the one check-mirrors.sh
    // itself recommends: delete the second copy instead of linting it. The DURATIONS stay frozen
    // <c>Defaults</c>-backed constants (machine-checked by scripts/check-remote-defaults.py) and the
    // press spring keeps its own mirrored decay rate below.

    private Transform? _capMesh;
    private float _restZ;
    private float _travel;
    private float _footprint;
    private System.Func<Color>? _stateColor;

    /// <summary>Paints the cap's three submeshes at an assembly progress k (0 = pure dust, 1 =
    /// settled), around the given settled TOP colour — the mirror of
    /// <c>PlayTray.BoardButton.ApplyAssembly</c>, wired to <see cref="RemoteBoardFurniture.InertCap"/>
    /// so the bevel/wall derivation stays with the cap that owns those materials.</summary>
    private System.Action<Color, float>? _paint;

    /// <summary>Seconds since the mirrored press EDGE arrived, or negative when no stroke is
    /// running — the same field, meaning and clock as the owner's <c>BoardButton._pressPhase</c>.</summary>
    private float _pressPhase = -1f;
    private float _hideLeft;
    private float _showLeft;
    private Vector3 _shownScale = Vector3.one;
    private Color _appearTarget = Color.white;

    // ---- SURFACE-FADE WATCHDOG (2026-08-09 invisible-cap round) --------------------------------
    //
    // Mirror of the fix on the owner's own caps (see the long header on
    // PlayTray.BoardButton's _showDeadline). The materialize fade below paints the cap at
    // AppearFadeFloor (15%) of its colour and the ONLY thing that ever repaints the true colour is
    // this countdown reaching zero — so a countdown that stops advancing leaves a peer's mirrored
    // cap invisible under a fully readable mirrored label, which is precisely the defect the owner
    // reported on their own board. The countdowns run on the UNSCALED clock (every other animation
    // in this mod does, because the game stops simulation time behind menus/dialogs and during card
    // phases) and carry a wall-clock deadline that force-completes them through the same completion
    // path. The mirrored DURATIONS are untouched: DissolveSeconds / AppearSeconds are still the
    // frozen Defaults-backed constants machine-checked by scripts/check-remote-defaults.py.
    //
    // (This paragraph used to end "and the press spring deliberately keeps its Time.deltaTime so it
    // stays byte-identical to the local `Time.deltaTime * 6f` that check-mirrors.sh names". That is
    // no longer true in EITHER half and the correction is worth keeping: the press stroke is now a
    // shared function of an UNSCALED phase, so the scaled clock the sentence was defending is gone
    // and so is the constant it was defending it for. Byte-identity to the owner is now structural
    // rather than numerical.)
    private float _hideDeadline = float.PositiveInfinity;
    private float _showDeadline = float.PositiveInfinity;

    /// <summary>Wall-clock grace before the watchdog force-completes a mirrored fade — mirror of
    /// <c>PlayTray.BoardButton.FadeWatchdogSlack</c>.</summary>
    private const float FadeWatchdogSlack = 0.35f;

    /// <summary>True while the dust dissolve is still shrinking the cap out — the window in which a
    /// re-show has to CANCEL the shrink rather than no-op on "already active".</summary>
    internal bool Hiding => _hideLeft > 0f;

    /// <summary>True while EITHER transition is repainting the cap's surface, i.e. while the
    /// animator — not the state gate — owns its colour. See <see cref="ReAim"/>.</summary>
    internal bool Transitioning => _hideLeft > 0f || _showLeft > 0f;

    /// <summary>
    /// Point the running transition at a NEW settled colour and repaint at the progress it has
    /// already reached. Called when the owner's cap changes state mid-animation (a confirm going
    /// accented as it arrives, say): the mirrored cap then finishes assembling into the colour it is
    /// actually becoming, instead of the peer seeing the settled look punched in for one frame and
    /// the ramp resuming from the old one. Mirror of the re-aim in
    /// <c>PlayTray.BoardButton.UpdateColor</c>.
    /// </summary>
    internal void ReAim(Color rest)
    {
        _appearTarget = rest;
        float k = _hideLeft > 0f
            ? Mathf.Clamp01(_hideLeft / RemoteBoardFurniture.DissolveSeconds)
            : 1f - Mathf.Clamp01(_showLeft / RemoteBoardFurniture.AppearSeconds);
        _paint?.Invoke(rest, k);
    }

    internal void Init(Transform? capMesh, float restZ, float travel, float footprint,
        System.Func<Color> stateColor, System.Action<Color, float> paint)
    {
        _capMesh = capMesh;
        _restZ = restZ;
        _travel = travel;
        _footprint = footprint;
        _stateColor = stateColor;
        _paint = paint;
        _shownScale = transform.localScale;
    }

    /// <summary>Replay the owner's press dip. Caps with no authored travel (nothing to sink) still
    /// accept the call and simply have nothing to show.</summary>
    internal void Press()
    {
        if (_travel <= 0f || _capMesh == null)
            return;
        _pressPhase = 0f;
    }

    /// <summary>Crumble the cap away, then deactivate it. Sized and coloured exactly like the local
    /// burst: the cap's footprint through its own world scale, in its current state colour.</summary>
    internal void PlayDissolve()
    {
        _showLeft = 0f;
        _shownScale = transform.localScale;
        _hideLeft = RemoteBoardFurniture.DissolveSeconds;
        _hideDeadline = Time.unscaledTime + _hideLeft + FadeWatchdogSlack; // watchdog (see the field header)
        _showDeadline = float.PositiveInfinity;
        _appearTarget = Current(); // the settled colour the crumble runs BACK from (shared with the appear)
        WorldUI.ButtonTuning.LogAnim(name, "disappear (dust dissolve) — MIRRORED");
        WorldUI.ButtonDissolveFx.Play(CapWorldCenter(), -transform.forward,
            _footprint * Mathf.Abs(transform.lossyScale.x), Current());
    }

    /// <summary>Assemble the cap out of dust in place — no scale pop, matching the local appear.</summary>
    internal void PlayAppear()
    {
        _hideLeft = 0f;
        transform.localScale = _shownScale;
        _showLeft = RemoteBoardFurniture.AppearSeconds;
        _showDeadline = Time.unscaledTime + _showLeft + FadeWatchdogSlack; // watchdog (see the field header)
        _hideDeadline = float.PositiveInfinity;
        _appearTarget = Current();
        // Frame ZERO of the assembly, painted here rather than on the next Update — otherwise the
        // peer sees one frame of the finished cap before it starts arriving, which is the pop the
        // whole animation exists to remove (mirror of the same line in BoardButton.SetVisible).
        _paint?.Invoke(_appearTarget, 0f);
        WorldUI.ButtonTuning.LogAnim(name, "appear (assemble out of dust) — MIRRORED");
        if (WorldUI.ButtonTuning.AppearParticlesEnabled)
            WorldUI.ButtonDissolveFx.PlayMaterialize(CapWorldCenter(), -transform.forward,
                _footprint * Mathf.Abs(transform.lossyScale.x), _appearTarget);
    }

    /// <summary>Abandon any running transition and restore the cap's true scale and colour — used
    /// when the animation is switched off, and on the silent build-time seeding.</summary>
    internal void CancelAnimations()
    {
        _hideLeft = 0f;
        _showLeft = 0f;
        _hideDeadline = float.PositiveInfinity;
        _showDeadline = float.PositiveInfinity;
        _pressPhase = -1f;
        transform.localScale = _shownScale;
        _paint?.Invoke(Current(), 1f); // 1 = fully settled material, no dust
        SeatCap(0f);
    }

    private Color Current() => _stateColor != null ? _stateColor() : Color.white;

    private Vector3 CapWorldCenter() => _capMesh != null ? _capMesh.position : transform.position;

    private void SeatCap(float depth01)
    {
        if (_capMesh == null || _travel <= 0f)
            return;
        Vector3 p = _capMesh.localPosition;
        float z = _restZ + _travel * depth01;
        if (Mathf.Approximately(p.z, z))
            return;
        p.z = z;
        _capMesh.localPosition = p;
    }

    private void Update()
    {
        // An IDLE cap does no per-frame work — this is one branch on three floats, and it is the
        // common case by a wide margin (a board's caps are mid-animation for a fraction of a second
        // at a time). Nothing in here logs, per frame or otherwise.
        if (_hideLeft <= 0f && _showLeft <= 0f && _pressPhase < 0f)
            return;

        // Dust dissolve: the cap shrinks out under the burst, then really goes away.
        if (_hideLeft > 0f)
        {
            _hideLeft -= Time.unscaledDeltaTime;
            if (_hideLeft > 0f && Time.unscaledTime >= _hideDeadline)
                _hideLeft = 0f; // watchdog: never leave a peer's cap parked half-shrunk
            float k = Mathf.Max(0f, _hideLeft / RemoteBoardFurniture.DissolveSeconds);
            transform.localScale = _shownScale * k;
            _paint?.Invoke(_appearTarget, k); // the assembly ramp, run backwards — walls first, brass frame last
            if (_hideLeft <= 0f)
            {
                _hideDeadline = float.PositiveInfinity;
                transform.localScale = _shownScale; // restore for the next show
                _paint?.Invoke(Current(), 1f);      // leave the exact state colour behind
                gameObject.SetActive(false);
            }
            return;
        }

        // Assemble: full scale IN PLACE while the opaque surface cools out of the warm dust into the
        // true state colour, which is then re-asserted exactly.
        if (_showLeft > 0f)
        {
            _showLeft -= Time.unscaledDeltaTime;
            if (_showLeft > 0f && Time.unscaledTime >= _showDeadline)
                _showLeft = 0f; // watchdog: never leave a peer's cap parked mid-assembly
            float k = 1f - Mathf.Max(0f, _showLeft / RemoteBoardFurniture.AppearSeconds);
            transform.localScale = _shownScale;
            _paint?.Invoke(_appearTarget, k);
            if (_showLeft <= 0f)
            {
                _showDeadline = float.PositiveInfinity;
                _paint?.Invoke(Current(), 1f);
            }
        }

        // THE PRESS STROKE, off the owner's own function. Nothing here shapes anything: the
        // phase advances on the unscaled clock and WorldUI.ButtonStroke.Depth01 says where the
        // cap is, exactly as it does on the owner's board. SeatCap takes the signed value so the
        // rebound past rest survives — clamping it at 0 here would drop the one part of the stroke
        // that makes a mirrored press read as a key rather than a slide.
        if (_pressPhase >= 0f)
        {
            _pressPhase += Time.unscaledDeltaTime;
            if (_pressPhase >= WorldUI.ButtonStroke.StrokeSeconds)
            {
                _pressPhase = -1f;
                SeatCap(0f);
            }
            else
            {
                SeatCap(WorldUI.ButtonStroke.Depth01(_pressPhase));
            }
        }
    }
}

/// <summary>
/// Self-animated soft pulse for the remote board's "wanted slot" hint — the mirror of
/// <c>PlayTray.SlotPulse</c> (same 3.2 rad/s breath between 0.30 and 0.85), kept local to the Net
/// module so the remote board never reaches into the local board's private visuals. Runs only while
/// its quad is active, i.e. only while a peer really has an unfilled slot during selection.
///
/// It is a RENDERING component: it writes a material colour and nothing else. No input, no
/// collider, no registry.
/// </summary>
internal sealed class RemoteGlowPulse : MonoBehaviour
{
    private Material? _material;
    private Color _base;

    internal void Init(Material material, Color baseColor)
    {
        _material = material;
        _base = baseColor;
    }

    private void Update()
    {
        if (_material == null)
            return;
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3.2f);
        float k = Mathf.Lerp(0.30f, 0.85f, t);
        _material.color = new Color(_base.r * k, _base.g * k, _base.b * k, k);
    }
}
