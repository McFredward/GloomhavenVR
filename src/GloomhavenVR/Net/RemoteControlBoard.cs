using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A READ-ONLY cosmetic mirror of a remote player's control board, rendered at their REAL synced
/// world transform (<c>owner.BoardPosition/BoardRotation/BoardScale</c>, valid only while
/// <c>owner.HasBoard</c>). Since the 3D-parity pass the board SURFACE is the REAL bundled
/// control-board asset — <see cref="RemoteTrayVisual"/> clones the same Oak/Steel/Bronze prefab
/// the owner's own <c>PlayTray</c> instantiates, chosen by their synced style — and the old flat
/// "Frame" quad (the user-rejected "komisch 2D" board) survives only as the procedural fallback
/// for when the asset bundle is not resident. It shows that player's TWO round cards, seated in
/// the prefab's REAL slot recesses, so everyone can "see who placed what", in initiative order
/// (<c>InitiativeAbilityCard</c> first — mirrors the local board's
/// <c>PlayTray.SyncFromGameState</c> ordering).
///
/// JOIN-TIME (user requirement: a peer's board must appear the moment they join, not only after
/// the host assigns characters): the board FRAME + pose + style render from the first extras
/// packet that carries <c>HasBoard</c>, with NO <c>NetPlayerActors.ActorFor</c> gate — an
/// actorless peer simply shows an empty board. Only the CONTENT that genuinely needs the
/// host-replicated actor (round cards, initiative, rest state, pile counts, active cards) stays
/// actor-gated; the global panels (objectives, elements, round, initiative track) refresh either
/// way.
///
/// ANTI-CHEAT (the linchpin): the card FRONTS are shown ONLY when
/// <see cref="RevealGate.ShowRoundCardFronts"/> is true — i.e. never during the game's own secret
/// <c>SelectAbilityCardsOrLongRest</c> phase for a remote actor. Otherwise the cards show BACKS.
/// We never transmit card identities; the cards are read locally from the already-host-replicated
/// <c>CPlayerActor.CharacterClass</c>, and turned face-up strictly on the game's authoritative
/// reveal — so this opens no new cheat vector.
///
/// VISIBILITY (<see cref="NetModule.RemoteBoards"/>, a purely LOCAL rendering choice):
///   Off             → render nothing;
///   ActionPhaseOnly → render only once the owner's cards may be shown (i.e. NOT the secret
///                     selection phase — the whole board is hidden during selection);
///   Always          → render the frame always; card BACKS during selection, real faces on reveal.
///
/// CARD FACE ART — FULL DETAIL (user requirement: a player must be able to SHOW their board so the
/// others can READ the cards and advise on the next move). A face-up card here is the game's OWN
/// card widget: full painted art, both action halves with every icon and number, the initiative
/// disc, the level, the enhancement stickers. It is produced by <see cref="RemoteAbilityCardSource"/>
/// — which clones either the peer's own live <c>AbilityCardUI.fullAbilityCard</c> (those widgets
/// exist on our client for EVERY actor, not just the local one) or a widget borrowed from the game's
/// object pool by card id — onto a world-space canvas via <see cref="RemoteCardArt"/>. That class
/// carries the full evidence trail; the earlier claim here that "the full painted art only ever
/// exists for the LOCAL player's own hand" was simply wrong. The mod-drawn NAME + INITIATIVE panel
/// survives only as the last-resort fallback when neither source resolves. Backs reuse the mod's own
/// card-back texture (<c>CardMesh</c>), drawn unlit. The slot widget itself lives in
/// <see cref="RemoteBoardCard"/>, shared with the active-card column.
///
/// NOTHING NEW GOES ON THE WIRE for any of this. The card identities were already available locally
/// in the host-replicated <c>CPlayerActor.CharacterClass</c> — the same read that fed the old
/// name+initiative panel. All that changed is how that identity is DRAWN, so the cheat surface is
/// bit-for-bit the one <see cref="RevealGate"/> already governed.
///
/// FULL BOARD PARITY (standing user requirement: "ALLE Widgets … sollen auch beim fremden
/// Controllboard sichtbar und synchronisiert sein", round 3: "1:1 genau so, wie der Spieler sein
/// eigenes Board sieht — alle Positionen, Animationen, Effekte"). Beyond the two round cards:
///
/// WHERE things sit is no longer guessed. Every dock on this board is seated from
/// <see cref="RemoteBoardLayout"/> — the OWNER's own mount base (read straight out of
/// <c>PlayTray</c>) plus the AUTHORED per-board offset/scale for the board style that peer SYNCED.
/// The board used to reproduce only the base and drop the per-board term, which on a Steel board
/// mis-placed the objectives, the elements, the piles, the active column, the round readout and the
/// initiative track all at once (defect (c) of the parity round).
///
/// WHAT they show: the two panels whose mod-drawn versions the user rejected are now live CLONES of
/// the game's OWN widgets (<see cref="RemoteWidgetMirror"/>) — real portraits, real rows, real
/// progress, driven per frame so the animations play:
///   • the scenario INITIATIVE TRACK      (<see cref="RemoteInitiativeTrack"/> — defect (a)),
///   • the objectives + quest header      (<see cref="RemoteObjectivesPanel"/> — defect (b)).
/// The rest stay MOD-DRAWN, at the same board-local offsets the LOCAL board docks its panels at:
///   • the element infusions               (<see cref="RemoteElementStrip"/>    — GLOBAL),
///   • the round number                    (<see cref="RemoteStatusReadouts"/>  — GLOBAL),
///   • their short/long rest state         (<see cref="RemoteStatusReadouts"/>  — per-actor, split gate),
///   • their discard/burnt/item pile COUNTS on the three stacks (per-actor, public),
///   • their active/persistent cards       (<see cref="RemoteActiveCards"/>     — per-actor, gated),
///   • and every piece of INTERACTIVE FURNITURE the local board wears
///                                         (<see cref="RemoteBoardFurniture"/> — see below).
/// The mod's own "INI" badge is the one thing that is NOT always drawn: it exists only as the
/// stand-in for the peer's initiative while the real track cannot be mirrored, because the owner's
/// board carries no such badge (see <see cref="SyncInitiativeBadge"/>) — parity cuts both ways.
/// NONE of that rides the wire: the global items are bit-identical on every client already, and the
/// per-actor items are read off the host-replicated <c>CPlayerActor</c> exactly like the round cards.
/// See <see cref="RemoteBoardContent"/> for the per-section anti-cheat derivation.
///
/// THE FURNITURE IS DRAWN, AND IT IS INERT. An earlier pass deliberately OMITTED a peer's own
/// controls (the CONFIRM/UNDO keycaps, the turn-flow cluster, the gear, the FOLLOW/PIN toggle, the
/// grab handle, the item-USE recess, the decision drawer, the slot overlays) on the argument that "a
/// button you cannot press is not information". The user rejected that: everything the local control
/// board shows must be shown on a peer's board too — but as a PURE DISPLAY, with nothing on it
/// interactable. <see cref="RemoteBoardFurniture"/> implements exactly that: meshes and text only,
/// no collider is ever created, nothing is added to <c>PlayTray.LaserTargets</c>, to
/// <c>VRInteractables</c> or to any other interaction registry, and a runtime guard
/// (<see cref="RemoteBoardFurniture.StripColliders"/>) destroys anything that ever slips through.
///
/// The transient reading fans (hand fan, item fan, pile browse, card flights) are handled by the
/// dedicated VR-only wire fields (<see cref="RemoteHandFan"/> / <see cref="RemoteItemFan"/> /
/// <see cref="RemoteBrowserFan"/> / <see cref="RemoteCardFx"/>).
///
/// Strict no-op offline / single-player / when the actor is null (<see cref="NetPlayerActors"/>
/// degrades to null there); everything is re-read each <see cref="Tick"/> (pose per frame, content
/// on the <see cref="RemoteBoardContent.RefreshSeconds"/> cadence).
/// </summary>
/// <remarks>CLASSIFICATION: MIXED (VR-ONLY frame + GLOBAL / PER-ACTOR MODEL content). The board's
/// world POSE, SCALE and STYLE are VR-ONLY and ride the wire (extras <c>FlagHasBoard</c> = 24 B,
/// plus trailing-block byte A bits 5..6 for the style). Everything DRAWN on it is zero-wire and
/// carries its own tag: see <see cref="RemoteBoardContent"/> and <see cref="RemoteBoardFurniture"/>.
/// That split is the whole design — the wire pays only for where the board IS, never for what it
/// says. See INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteControlBoard
{
    // Frame geometry in the same "card real-metre" units as the local board (PlayTray BoardW/H),
    // so scaling by the owner's BoardScale reproduces their board's world size.
    private const float BoardW = 0.64f;
    private const float BoardH = 0.32f;

    // BoardHalfW is GONE. It was the anchor every off-edge dock offset from — the remote-only
    // half of "PlayTray.BoardHalfWidthLocal + 0.012 + <a literal>". Those docks now read the
    // OWNER's own mount bases through RemoteBoardLayout, which composes that same half-width with
    // the AUTHORED per-board offset the remote copies used to drop (defect (c)). Reintroducing a
    // remote-side board-geometry constant is how that defect comes back.

    // Two round-card slots. The LEGACY metric below (authored card width × the local board's 1.3
    // SlotScale) is now only the FALLBACK for peers that predate extension record 11: it was
    // billed as "the exact size a card parked in the owner's recess renders at", but it dropped
    // the owner's [Cards] SlotCardFill (default 1.45!) and their CardWidth config entirely, so
    // every remote card rendered 31 % smaller than its owner sees it even between two
    // default-configured clients — the user's "nicht 1:1, ich sehe sie kleiner" report. The live
    // sizes now ride the wire (NetProtocol.ExtIdSlotCardSize) and are read via SlotCardW/SlotCardH
    // below; keep this constant equal to NetProtocol.SlotCardWidthLegacy.
    private const float CardW = 0.0635f * 1.3f;   // Defaults.CardWidth × PlayTray.SlotScale
    private const float CardH = CardW * (88f / 63.5f);

    /// <summary>The width THIS peer's slot cards must render at: their synced effective size
    /// (extension record 11 — <c>CardWidth × SlotScale × SlotCardFill</c>, the exact chain
    /// <c>PlayTray</c> scales a parked card by), or the legacy constant for a pre-record peer.</summary>
    private float SlotCardW => _owner.SlotCardWidth > 0f ? _owner.SlotCardWidth : CardW;
    private float SlotCardH => SlotCardW * (88f / 63.5f);

    /// <summary>The peer's slot FRAME metric (their <c>CardWidth × SlotScale</c>, record 11) — the
    /// size the furniture's glow rims follow, mirroring the local board's slot frames.</summary>
    private float SlotFrameW => _owner.SlotFrameWidth > 0f ? _owner.SlotFrameWidth : CardW;

    /// <summary>The slot-card width the current visual was BUILT at (with <see cref="_builtSlotFrameW"/>,
    /// the change key for the live-config rebuild in Tick — a peer editing their card size mid-game
    /// must re-size here too, and the widgets are constructor-sized).</summary>
    private float _builtSlotCardW = CardW;
    private float _builtSlotFrameW = CardW;
    private const float SlotSpacing = 0.155f;     // PlayTray.SlotSpacing (fallback layout)
    private const float SlotY = 0.015f;           // PlayTray procedural slot height (fallback)
    private const float ProudZ = -0.004f;   // toward the viewer (−Z), proud of the frame face

    /// <summary>Extra proud lift a card seated ON a real slot anchor gets (the anchor sits at the
    /// recess floor; a coplanar quad would z-fight the recess mesh).</summary>
    private const float CardOnAnchorProudZ = -0.003f;

    /// <summary>The shared proud depth every board-local surface sits at (−Z = toward the viewer).
    /// Exposed so the mod-drawn parity panels in <see cref="RemoteBoardContent"/> seat on the same
    /// plane as the round-card slots and the pile stacks.</summary>
    internal const float ProudZLocal = ProudZ;

    // ---- pile stacks (report 6) ---------------------------------------------------------------
    // The three card STACKS that hang off the right edge of the local control board
    // (PlayTray.PileMountBase + PileViewer's per-stack offsets: discard above, burnt below, items
    // two rows down). They exist here for one reason: a card flying into a peer's discard pile
    // (RemoteCardFx) needs a VISIBLE destination on that peer's board — before this, the remote
    // board had slots but no piles at all, so any pile-bound flight would have ended in empty air.
    // Kept deliberately small and back-textured (never a card identity — same anti-cheat stance as
    // everything else here).

    /// <summary>DEFAULT board-local position of a play SLOT (0 = left, 1 = right) — the authored
    /// layout (<c>PlayTray.SlotSpacing</c>), used while no real tray visual is built (procedural
    /// fallback / board hidden). The LIVE layout — the real prefab's measured recess anchors —
    /// is served by the instance <see cref="AnchorLocalLive"/> and reaches the FX/fan consumers
    /// through <c>RemoteAvatar.BoardAnchorLocal</c>.</summary>
    internal static Vector3 SlotLocal(int slot) =>
        new((slot == 0 ? -0.5f : 0.5f) * SlotSpacing, SlotY, ProudZ);

    /// <summary>DEFAULT (Oak-layout) board-local position of a pile stack / the board centre for a
    /// card-FX anchor — kept for callers that have no board style at hand. Prefer the
    /// style-keyed overload, and <see cref="AnchorLocalLive"/> when an instance is available:
    /// that one also substitutes the REAL prefab recess positions for the two slots.</summary>
    internal static Vector3 AnchorLocal(CardFxAnchor anchor) =>
        AnchorLocal(anchor, new RemoteBoardLayout(Cards.ControlBoard.Oak));

    /// <summary>
    /// Board-local position of a pile stack / the board centre, for the board STYLE this peer
    /// synced. It reproduces the LOCAL board's stack layout exactly — <c>PlayTray.PileMountBase</c>
    /// plus the AUTHORED per-board <c>PileOffset</c> (both via <see cref="RemoteBoardLayout"/>),
    /// plus <c>PileViewer</c>'s own per-stack offsets (<c>PileStackOffsetX</c>, and the ±spacing/2 /
    /// −1.5·spacing rows) — so a peer's piles sit where that player's own piles sit, on every board.
    /// Dropping the per-board term is what put the Steel board's stacks 40 mm behind the owner's
    /// (defect (c) of the 1:1-parity round).
    /// </summary>
    internal static Vector3 AnchorLocal(CardFxAnchor anchor, in RemoteBoardLayout layout)
    {
        Vector3 pile = layout.PileMount + new Vector3(Cards.PlayTray.PileStackOffsetX, 0f, 0f);
        float step = layout.PileSpacing;
        return anchor switch
        {
            CardFxAnchor.Slot0 => SlotLocal(0),
            CardFxAnchor.Slot1 => SlotLocal(1),
            CardFxAnchor.Discard => pile + new Vector3(0f, step * 0.5f, 0f),
            CardFxAnchor.Burnt => pile + new Vector3(0f, -step * 0.5f, 0f),
            CardFxAnchor.Items => pile + new Vector3(0f, -step * 1.5f, 0f),
            _ => new Vector3(0f, 0f, ProudZ), // Board (and any unknown future id)
        };
    }

    /// <summary>
    /// LIVE board-local anchor layout: like <see cref="AnchorLocal(CardFxAnchor, in RemoteBoardLayout)"/>,
    /// but the two round-card slots come from the REAL tray prefab's measured recess anchors once
    /// the 3D visual is built — so a card flight (<see cref="RemoteCardFx"/>) and a pile-browse arc
    /// land exactly in/on the rendered recess of whatever board style the peer runs, instead of on
    /// the hardcoded flat-board offsets. Every other anchor comes from the peer's own authored
    /// per-board layout, which is also what the rendered panels/piles are seated at, so flight
    /// destination and rendered destination stay ONE point by construction.
    ///
    /// THE HAND-TUNED LIFT IS GONE. This used to add a per-style "content proud lift" (Steel
    /// −0.05, Bronze −0.015) whose own comment admitted it was "an approximation of the meshes,
    /// not a measurement" — a remote-only fudge invented because the panels dropped the authored
    /// per-board offsets that carry exactly that depth (Steel objectives z −0.042, piles/elements
    /// −0.040, readout −0.044). Now that <see cref="RemoteBoardLayout"/> applies the real offsets,
    /// the fudge would double-count them.
    /// </summary>
    internal Vector3 AnchorLocalLive(CardFxAnchor anchor)
    {
        if (_tray != null)
        {
            if (anchor == CardFxAnchor.Slot0)
                return _tray.SlotLocal(0) + new Vector3(0f, 0f, CardOnAnchorProudZ);
            if (anchor == CardFxAnchor.Slot1)
                return _tray.SlotLocal(1) + new Vector3(0f, 0f, CardOnAnchorProudZ);
        }
        return AnchorLocal(anchor, _layout);
    }

    private readonly RemoteAvatar _owner;

    /// <summary>The peer's authored board layout — every dock seat on this board is derived from it
    /// (see <see cref="RemoteBoardLayout"/>). Re-derived whenever the board is (re)built, which is
    /// also the only moment their synced style can have changed.</summary>
    private RemoteBoardLayout _layout = new(Cards.ControlBoard.Oak);

    private GameObject? _root;
    /// <summary>False until the synced pose was applied once — the first apply SNAPS (a fresh
    /// board must not ease in from the origin); later applies ease (see Tick).</summary>
    private bool _poseInit;

    /// <summary>The board's INTERPOLATED uniform scale — the third component of the eased pose
    /// (see Tick). Seeded by the same first-apply snap that seeds position/rotation.</summary>
    private float _easedScale = 1f;

    /// <summary>Last logged (target, eased) scale pair — the change gate for
    /// <see cref="LogScaleIfChanged"/>.</summary>
    private float _loggedTargetScale = -1f;

    /// <summary>Unscaled time the next scale line may be emitted (a zoom is a continuous stream;
    /// the log states its start, its end and at most a couple of samples in between).</summary>
    private float _nextScaleLogAt;
    /// <summary>How many card slots a control board has. Bound to
    /// <see cref="NetProtocol.BoardUiSlotCount"/> — the SAME two recesses the owner's
    /// <c>PlayTray</c> builds and the wire's occupancy nibble describes — so a board that ever grew
    /// a third recess widens here, on the wire and in PlayTray together, and cannot half-widen.</summary>
    private const int SlotCount = NetProtocol.BoardUiSlotCount;

    private readonly RemoteBoardCard[] _cards = new RemoteBoardCard[SlotCount];
    private OwnerTag? _tag;

    // ---- board STYLE (extras block byte A bits 5..6) --------------------------------------------
    // The REAL 3D board asset for the style THIS peer chose (null → flat-quad fallback while the
    // bundle is absent). A style switch rebuilds the whole board from the new prefab (rare, cheap);
    // the fallback quad instead re-TINTS via _frameMat + _appliedStyle, exactly the pre-3D
    // behaviour, so a bundle-less client keeps working unchanged.
    private RemoteTrayVisual? _tray;
    private Material? _frameMat;
    private int _appliedStyle = -1;

    /// <summary>Next unscaled time to re-probe for the bundle when the board came up on the flat
    /// fallback — so a board built before the asset bundle finished loading upgrades itself to
    /// the real 3D asset instead of staying flat for the session.</summary>
    private float _nextTrayProbeAt;
    private const float TrayProbeSeconds = 5f;

    // ---- full-parity content (all mod-drawn, all zero-wire — see the class note) ----------------
    // Data class per widget — the same closed set as the CLASSIFICATION tags on the types
    // themselves (grep -rn "CLASSIFICATION:" Net/). None of these costs a wire byte; the only
    // wire input on this board is its own pose/scale/style plus the RemoteAvatar handed to
    // _furniture. Keep this column in step with the tags — it is the manifest a reader sees first.
    private RemoteObjectivesPanel? _objectives;   // GLOBAL
    private RemoteElementStrip? _elements;        // GLOBAL
    private RemoteStatusReadouts? _status;        // MIXED — GLOBAL round + PER-ACTOR initiative/rest
    private RemotePickBanner? _pickBanner;        // WIRE (extension record 7) — the owner's pick line
    private RemoteBoardTooltip? _boardTooltip;    // WIRE (extension record 9) — the owner's tooltip, identity-gated at the source
    private RemoteActiveCards? _active;           // PER-ACTOR MODEL — active/persistent cards
    private RemoteInitiativeTrack? _track;        // MIXED — GLOBAL actor list + PER-ACTOR initiative (gated)
    private RemoteBoardFurniture? _furniture;     // MIXED — DELIBERATELY-NOT neutral looks + PER-ACTOR
                                                  //   slots + VR-ONLY-derived pulses; INERT copies of
                                                  //   the board's interactive controls. Reads the wire
                                                  //   (RemoteAvatar) but adds no field to it.
    private readonly PileCounter?[] _piles = new PileCounter?[3]; // PER-ACTOR MODEL — discard / burnt /
                                                  //   items counts, deliberately UNGATED (vanilla lets
                                                  //   anyone open any player's card overview)

    /// <summary>Next content re-read time (unscaled). The POSE follows every frame; the model reads
    /// and the TMP repaints run on the <see cref="RemoteBoardContent.RefreshSeconds"/> cadence so a
    /// four-peer table stays free.</summary>
    private float _nextRefreshAt;

    /// <summary>Change-gate for the "what is this peer's board rendering" diagnostic (see
    /// <see cref="LogContent"/>) — one Info line per actual change, never per tick.</summary>
    private string _loggedContent = string.Empty;

    private readonly CAbilityCard?[] _ordered = new CAbilityCard?[SlotCount];

    /// <summary>Which slots this board is DRAWING a card on right now (bit per slot), as decided by
    /// <see cref="SeatSlots"/>. The furniture layer's snap glow and the legacy wanted-glow
    /// derivation read it, so they can never disagree with what the slots actually show.</summary>
    private int _slotOccupiedMask;

    /// <summary>Which slots are drawing a card FACE-UP (bit per slot) — a strict subset of
    /// <see cref="_slotOccupiedMask"/>. Separate from it because an anonymous back (occupancy known
    /// from the wire, identity not) must never get the face-up half-card divider drawn across it.</summary>
    private int _slotFaceMask;

    /// <summary>Which slots are drawing an ANONYMOUS back (bit per slot) — the wire says a card
    /// lies there and this client holds no identity for it. Diagnostics only; a strict subset of
    /// <see cref="_slotOccupiedMask"/> and disjoint from <see cref="_slotFaceMask"/>.</summary>
    private int _slotAnonMask;

    /// <summary>
    /// ACTION-PHASE FACE LATCH (user report, hardware MP test 2026-08: "Obwohl wir noch in der
    /// Aktionsphase waren … wurden meine Karten den anderen verdeckt angezeigt WÄHREND meines
    /// Zuges"). Per slot: the last card identity this board legitimately showed FACE-UP while the
    /// reveal gate was open.
    ///
    /// ROOT CAUSE THIS SOLVES. <see cref="SeatSlots"/> names an occupied recess exclusively from
    /// the live <c>RoundAbilityCards</c> list (<see cref="OrderRoundCards"/>) — but the game DRAINS
    /// that list DURING the owner's own turn: every used half moves its card out via
    /// <c>CCharacterClass.MoveAbilityCardToPile</c> / <c>DiscardRoundAbilityCards</c>
    /// (CCharacterClass.cs:505-533), long before the action phase ends. The wire's occupancy
    /// nibble still says the cards physically lie in the recesses (they do, on the owner's board),
    /// so the peer degraded them to ANONYMOUS BACKS mid-action-phase — exactly the log's
    /// "LiveWidget/anon-back → anon-back/anon-back" flips at fronts=True (peer log 18436/20409).
    /// The reveal gate itself never closed; the IDENTITY dried up.
    ///
    /// THE POLICY: a face that <see cref="RevealGate"/> has already permitted stays showable for
    /// as long as that same gate stays open — i.e. for the WHOLE action phase, for every player
    /// symmetrically, matching vanilla (whose own played-card panel keeps the revealed cards up
    /// until the next selection phase). The latch is filled ONLY inside SeatSlots' front branch
    /// (which runs strictly under the gate) and cleared the moment the gate answers false — the
    /// next secret selection phase, scenario end, actor loss — so nothing is ever shown that the
    /// gate did not first approve, and nothing survives into the next round's secrecy. Reveals
    /// still flow ONLY through RevealGate; this changes when an approved face may be REPEATED,
    /// never whether one may be shown.
    /// </summary>
    private readonly CAbilityCard?[] _latchedFaces = new CAbilityCard?[SlotCount];

    public RemoteControlBoard(RemoteAvatar owner)
    {
        _owner = owner;
    }

    public void Tick(float dt)
    {
        // The visibility mode is read through the SHARED gate (RemoteBoardGate) rather than off the
        // ConfigEntry directly, because the board is no longer the only thing the setting governs:
        // the transient item / pile-browse fans and the card-flight FX are separate classes with
        // separate roots, and they now ask the same predicate. One expression, one meaning.
        RemoteBoardVisibility vis = RemoteBoardGate.Mode;
        RemoteBoardGate.LogModeIfChanged(vis); // evidence the panel's cycle button reaches the render path

        if (!_owner.HasBoard || vis == RemoteBoardVisibility.Off)
        {
            BlankCardFaces();
            SetActive(false);
            return;
        }

        // Read the owner's actor fresh each frame (null offline / before the host assigns
        // characters / benched). JOIN-TIME REQUIREMENT: the actor is NOT a gate for the board
        // SURFACE any more — a peer's board must appear the moment their first extras packet
        // lands, character assignment or not. An actorless peer has no cards, so there is
        // nothing the reveal gate could need to hide: "no actor" counts as "not in the secret
        // phase" for the visibility rule below.
        CPlayerActor? actor = NetPlayerActors.ActorFor(_owner.PlayerId);

        bool showFronts = actor != null && RevealGate.ShowRoundCardFronts(actor);

        bool showBoard = RemoteBoardGate.SurfaceVisible(vis, actor == null || showFronts);

        if (!showBoard)
        {
            // ANTI-CHEAT: hiding the root is not enough. A hosted card face that survives inside a
            // deactivated board would be re-activated by SetActive(true) on the frame the board
            // comes back — one statement BEFORE the slots re-evaluate the gate. Blanking the slots
            // here means there is no such face to re-activate, in the one case where it matters most
            // (RemoteBoardVisibility.ActionPhaseOnly hides the whole board *because* the gate shut).
            // Self-early-returning and allocation-free once blank, so it is free to run every frame.
            BlankCardFaces();
            SetActive(false);
            return;
        }

        // The peer switched their control board: tear the whole visual down and rebuild from the
        // new prefab — the real asset cannot be re-tinted into another board the way the fallback
        // quad could. Rare (a settings click on their side), and the rebuild is one frame.
        if (_root != null && _tray != null && _tray.Style != _owner.BoardStyle)
        {
            VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] style switch " +
                              $"{_tray.Style} → {_owner.BoardStyle} — rebuilding from the new prefab.");
            Destroy();
        }
        // The peer's synced SLOT-CARD SIZE changed (extension record 11 — a live edit of their
        // [Cards] CardWidth / SlotCardFill, or the record appearing on the first packet after a
        // legacy-sized build). The card panels and the furniture's glow rims are
        // constructor-sized, so the same teardown-rebuild the style switch uses applies; rare
        // (a settings edit on their side) and one frame. 0.4 mm epsilon = the wire's own
        // quantization step, so re-quantized noise can never loop rebuilds.
        else if (_root != null && (Mathf.Abs(SlotCardW - _builtSlotCardW) > 0.0004f
                                   || Mathf.Abs(SlotFrameW - _builtSlotFrameW) > 0.0004f))
        {
            VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] slot-card size change " +
                              $"{_builtSlotCardW * 1000f:0.0} → {SlotCardW * 1000f:0.0} mm " +
                              "(extension record 11) — rebuilding the board visuals at the " +
                              "owner's real card size.");
            Destroy();
        }
        // A board that came up FLAT because the bundle was not resident yet upgrades itself to the
        // real asset once it is (slow probe — a few bundle-list walks per minute, only while flat).
        else if (_root != null && _tray == null && Time.unscaledTime >= _nextTrayProbeAt)
        {
            _nextTrayProbeAt = Time.unscaledTime + TrayProbeSeconds;
            if (RemoteTrayVisual.PrefabAvailable(_owner.BoardStyle))
            {
                VRLog.Info("Net", $"Remote board [{_owner.PlayerId}]: asset bundle now resident — " +
                                  "upgrading the flat fallback board to the real 3D asset.");
                Destroy();
            }
        }

        EnsureBuilt();
        SetActive(true);

        // Place at the REAL synced world transform. The wire carries the OWNER's exact pose
        // (full quantized quaternion — the Frei movement scheme adds no axis the pose doesn't
        // cover). Since mod build 2 the SENDER raises the extras cadence to the rig rate
        // (15 Hz) while the pose is CHANGING (NetAvatarDriver.TickExtrasSend, defect 7
        // "Bewegen kommt nicht flüssig an"), so during an active drag this easing gets the
        // same sample density the head/hands get — the exact pipeline whose smoothness is
        // already accepted — and an idle board still costs only 5 Hz. Ease with the shared
        // avatar sharpness; once the owner releases, the eased pose converges on the exact
        // transmitted one (snap on first build so a fresh board never lerps in from the origin).
        Vector3 wantPos = _owner.BoardPosition;
        Quaternion wantRot = _owner.BoardRotation;
        // SCALE IS PART OF THE POSE — defect (e) of this round ("das Bewegen ist jetzt flüssig,
        // aber das Skalieren/Zoomen des Bretts nicht").
        //
        // The SENDER already treats it as such: TickExtrasSend's "moving" test includes the scale,
        // so resizing a board raises the extras cadence to the rig rate (15 Hz) exactly like
        // dragging it does, and the scale rides in the very same 24-byte board block. The RECEIVER
        // was the asymmetry: position and rotation were eased on the shared avatar sharpness while
        // the scale was ASSIGNED, so a smooth 15 Hz stream of scales was rendered as 15 visible
        // steps per second — a board that glides while it moves and stutters while it zooms, which
        // is precisely what was reported. Easing it on the SAME k (and snapping on the same first
        // apply, so a fresh board never grows in from 1.0) makes zoom and move one motion.
        float wantScale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        Transform rt = _root!.transform;
        if (!_poseInit)
        {
            _poseInit = true;
            rt.SetPositionAndRotation(wantPos, wantRot);
            _easedScale = wantScale;
        }
        else
        {
            float k = 1f - Mathf.Exp(-NetProtocol.InterpolationSharpness * Mathf.Max(dt, 0f));
            rt.SetPositionAndRotation(
                Vector3.Lerp(rt.position, wantPos, k),
                Quaternion.Slerp(rt.rotation, wantRot, k));
            _easedScale = Mathf.Lerp(_easedScale, wantScale, k);
        }
        rt.localScale = Vector3.one * _easedScale;
        LogScaleIfChanged(wantScale);

        // Fallback board only: re-tint the flat frame when this peer switches their control board
        // (the real asset was rebuilt above instead; change-latched int compare either way).
        ApplyBoardStyle();

        if (actor != null)
            OrderRoundCards(actor);
        else
            _ordered[0] = _ordered[1] = null; // actorless peer: an EMPTY board, never stale cards
        // THE reveal decision for this peer's played cards, taken ONCE per frame here and passed
        // down: showFronts is RevealGate.ShowRoundCardFronts(actor) verbatim — false for a remote
        // actor while the game is in its own secret SelectAbilityCardsOrLongRest phase, true once
        // the selection is locked in and the characters are acting (and always true offline / for
        // our own actor / off-scenario). The slot only ever CREATES a face object inside its
        // front branch, so the fronts cannot exist a frame early. The actor is handed through purely
        // so the slot can find that player's own card widget to clone — it is never written to.
        SeatSlots(actor, showFronts);

        _tag!.Tick();

        // Content (objectives / elements / round / initiative / rest / pile counts / active cards)
        // on the shared cadence — everything below is a MODEL read, not a wire read. Before the
        // actor exists only the GLOBAL panels refresh (they are bit-identical on every client);
        // the per-actor surfaces stay blank until the host assigns the character.
        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
            if (actor != null)
                RefreshContent(actor, showFronts);
            else
                RefreshGlobalContent();
        }

        // THE MIRRORED WIDGETS RUN PER FRAME, not on the content cadence. They are clones of live
        // game panels driven from the original (RemoteWidgetMirror), and the user's requirement is
        // explicitly "alle Positionen, ANIMATIONEN, Effekte" — the initiative track's inter-round
        // reorder slide and the objectives' progress fill would step visibly at 4 Hz. The drive is a
        // flat walk over pre-resolved component references with change-gated writes, so this costs
        // a few hundred field compares per board per frame and allocates nothing.
        _track?.TickLive();
        _objectives?.TickLive();
    }

    /// <summary>The actorless subset of <see cref="RefreshContent"/> (join-time, before the host
    /// assigns this peer a character): objectives, element infusions and the initiative track are
    /// GLOBAL scenario state and render fine without an actor; everything per-actor stays blank.</summary>
    private void RefreshGlobalContent()
    {
        try
        {
            _objectives?.Refresh();
            _elements?.Refresh();
            _track?.Refresh();
            _pickBanner?.Apply(_owner.PickBannerText);
            _boardTooltip?.Apply(_owner.TooltipText);
            SyncInitiativeBadge();
            // The furniture's SYNCED half (board-UI record: buttons + wanted glow) is wire-fed and
            // must follow the owner's board with or without an actor. The slot flags used to be
            // hard false here because occupancy was a per-ACTOR read; since it rides the wire, an
            // actorless peer's recesses are knowable too, so the masks SeatSlots just resolved are
            // passed through unchanged and the join-time board gets its snap glow like any other.
            _furniture?.Refresh(null, _owner, showFronts: false,
                _slotOccupiedMask, faceMask: 0);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote board [{_owner.PlayerId}] global content refresh failed: {e.Message}");
        }
    }

    /// <summary>
    /// Re-read every parity surface from the LOCAL game model and repaint what changed. Wrapped as a
    /// whole: a half-initialised scenario state (mid-load, mid-teardown) must degrade to a stale
    /// board, never take down the remote-avatar tick that also drives this peer's head and hands.
    /// </summary>
    private void RefreshContent(CPlayerActor actor, bool showFronts)
    {
        try
        {
            _objectives?.Refresh();
            _elements?.Refresh();
            _pickBanner?.Apply(_owner.PickBannerText);
            _boardTooltip?.Apply(_owner.TooltipText);
            _status?.Refresh(actor, showFronts);
            _active?.Refresh(actor, showFronts);
            _track?.Refresh();
            SyncInitiativeBadge();

            // Mip-bake upkeep for the two round-card slots' hosted faces (the active column does
            // its own inside Refresh above): their Set() is change-gated, so the cadenced rescan
            // that catches the clones' async header art has to be driven from here. Free while the
            // slots show backs (self-early-return).
            _cards[0]?.MaintainMips();
            _cards[1]?.MaintainMips();

            // The inert furniture layer. It is fed the SAME reveal answer and the SAME slot state
            // the board is already rendering (the masks SeatSlots resolved, not a second derivation
            // of its own) — see RemoteBoardFurniture for why nothing derived from those can leak
            // anything the board does not already show.
            _furniture?.Refresh(actor, _owner, showFronts, _slotOccupiedMask, _slotFaceMask);

            // Pile counts — the SAME reads CardsGameApi.DiscardedCount/BurntCount and
            // ItemsPile.Count make for the local board, against this actor instead of the local
            // hand. PUBLIC information: vanilla lets anyone open ANY player's full card overview
            // from the initiative track (InitiativeTrackPlayerAvatar.OnClick →
            // CardsHandManager.ToggleViewAllCards), so a count on a stack reveals nothing new and
            // needs no reveal gate.
            CCharacterClass cc = actor.CharacterClass;
            int discard = cc != null ? cc.DiscardedAbilityCards.Count : 0;
            int burnt = cc != null ? cc.LostAbilityCards.Count + cc.PermanentlyLostAbilityCards.Count : 0;
            CInventory? inv = actor.Inventory;
            int items = inv?.AllItems != null ? inv.AllItems.Count : 0;
            _piles[0]?.Set(discard);
            _piles[1]?.Set(burnt);
            _piles[2]?.Set(items);

            LogContent(discard, burnt, items, showFronts);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote board [{_owner.PlayerId}] content refresh failed: {e.Message}");
        }
    }

    /// <summary>
    /// PARITY, NOT ADDITION: the mod's own "INI" badge is a remote-only stand-in — the owner's board
    /// has no such widget, their initiative lives on the docked initiative TRACK. So it is shown
    /// only while the track has fallen back to the mod-drawn chip strip, and hidden the moment the
    /// REAL track is being mirrored (which shows the same number, in the same place, under the same
    /// vanilla gate). Anything else would put a widget on a peer's board that its owner cannot see.
    /// </summary>
    private void SyncInitiativeBadge()
    {
        bool trackMirrored = _track != null
                             && _track.Source == RemoteWidgetMirror.Fidelity.MirroredWidget;
        _status?.SetShownWhileTrackFallback(!trackMirrored);
    }

    /// <summary>
    /// SCALE INTERPOLATION diagnostic (grep: "Remote board scale") — the evidence a future
    /// "zooming still is not smooth" report needs, without a screenshot: the TARGET scale that
    /// arrived on the wire, the EASED scale actually rendered this frame, and the gap between them
    /// (which is the interpolation doing its job — a gap that never closes means packets stopped,
    /// a gap that is always zero means the easing was bypassed).
    ///
    /// Change-gated on a 1 % dead band AND throttled to 2 Hz: a zoom is a continuous stream, so
    /// this states its start, its end and a couple of samples in between, never one line per frame.
    /// </summary>
    private void LogScaleIfChanged(float target)
    {
        if (Mathf.Abs(target - _loggedTargetScale) < _loggedTargetScale * 0.01f + 0.0005f)
            return;
        float now = Time.unscaledTime;
        if (now < _nextScaleLogAt)
            return;
        _nextScaleLogAt = now + 0.5f;
        _loggedTargetScale = target;
        VRLog.Info("Net", $"Remote board scale [{_owner.PlayerId}]: target {target:F3} " +
                          $"(wire), rendering {_easedScale:F3} (eased), delta " +
                          $"{(target - _easedScale) * 1000f:F1} mm/unit — SCALE is interpolated on " +
                          $"the same sharpness ({NetProtocol.InterpolationSharpness:0}) as position " +
                          "and rotation, and the sender raises the extras cadence to the rig rate " +
                          "while it changes, so a zoom arrives as smoothly as a move.");
    }

    /// <summary>
    /// Change-gated diagnostic so the next hardware log states EXACTLY what a peer's board is
    /// rendering (grep: "Remote board content"). One line per actual change — the cadence tick
    /// itself is silent.
    /// </summary>
    private void LogContent(int discard, int burnt, int items, bool showFronts)
    {
        // FIDELITY + ANTI-CHEAT in one greppable line: which mechanism drew each round card
        // (LiveWidget / PooledBorrow = the REAL game card face; None = the mod-drawn fallback panel),
        // together with the gate answer that allowed a face at all. Grep: "Remote board content".
        string slots = $"{FaceTag(0, showFronts)}/{FaceTag(1, showFronts)}";
        string line = $"Remote board content [{_owner.PlayerId}]: " +
                      $"round='{(_status != null ? _status.RoundText : "-")}', " +
                      $"initiative={(_status != null ? _status.InitiativeText : "?")}, " +
                      $"rest='{(_status != null ? _status.RestText : string.Empty)}', " +
                      $"piles d/b/i={discard}/{burnt}/{items}, " +
                      $"round-card faces={slots}, " +
                      $"slot-occupancy={(_owner.SlotOccupancyKnown ? "0x" + _owner.BoardSlotMask.ToString("X1") + "(synced)" : "model-only")}, " +
                      $"active={(_active != null ? _active.Count : 0)} card(s) " +
                      $"({(_active != null ? _active.RealFaceCount : 0)} real face(s)), " +
                      $"objectives={(_objectives != null ? _objectives.RowCount : 0)} row(s) " +
                      $"via {SectionTag(_objectives?.Source, _objectives?.Reason)}, " +
                      $"elements={(_elements != null ? _elements.ActiveCount : 0)} infused, " +
                      $"track={(_track != null ? _track.Count : 0)} entr(y/ies) " +
                      $"via {SectionTag(_track?.Source, _track?.Reason)}, " +
                      $"furniture[{(_furniture != null ? _furniture.StateLine : "-")}], " +
                      $"layout[{_layout}], " +
                      $"seats[{Seats()}], " +
                      $"highlight[hand={(_owner.HandHighlightIndex >= 0 ? _owner.HandHighlightIndex.ToString() : "none")}, " +
                      $"fan={(_owner.FanHighlightIndex >= 0 ? _owner.FanHighlightIndex.ToString() : "none")}], " +
                      $"scale[target={(_owner.BoardScale > 0f ? _owner.BoardScale : 1f):F3}, " +
                      $"eased={_easedScale:F3}], " +
                      $"fronts={showFronts}";
        if (line == _loggedContent)
            return;
        _loggedContent = line;
        VRLog.Info("Net", line + " — all read LOCALLY from the replicated model (zero wire traffic); " +
                          "fronts gated by RevealGate.ShowRoundCardFronts (false ⇒ BACKS only, which " +
                          "is exactly the game's secret SelectAbilityCardsOrLongRest phase for a " +
                          "remote actor). A round-card face of LiveWidget/PooledBorrow is the REAL " +
                          "game card at full detail; 'panel' is the mod-drawn name+initiative " +
                          "fallback; 'back' means the gate is shut; 'anon-back' means the OWNER's " +
                          "own recess occupancy (board-UI record byte 1 bits 3..4, reported as " +
                          "'slot-occupancy=0x..(synced)') says a card lies there while this client " +
                          "holds no identity for it — a pick candidate, a drop whose SelectCard is " +
                          "still queued, or a peer without an actor yet; 'empty' means the slot is " +
                          "empty. 'slot-occupancy=model-only' is a sender that predates the nibble " +
                          "and is rendered exactly as before. " +
                          "A section 'via MirroredWidget' is a live CLONE of the game's OWN panel " +
                          "(real portraits, real rows, real progress, driven per frame from the " +
                          "original); 'via ModDrawn(reason)' means the widget could not be resolved " +
                          "and the mod's stand-in is up — the reason says which. 'layout[...]' is " +
                          "the peer's AUTHORED per-board dock layout every panel above is seated at " +
                          "(PlayTray mount bases + the shipped per-board offsets for their synced " +
                          "board style), so a mis-placed panel is diagnosable without a screenshot. " +
                          "'seats[...]' resolves that layout one step further — the RESOLVED mount " +
                          "position per dock, the board style it was keyed from, and how each " +
                          "mirrored panel MEASURED itself ('converted host rect' = the owner's own " +
                          "dock rect, so its size and lift are theirs by construction; 'graphics " +
                          "union' = this client's fallback measure). 'highlight[...]' is the synced " +
                          "card lift per fan (positions only — extension record 6 carries no card " +
                          "identity); 'scale[target/eased]' separates the scale that ARRIVED from " +
                          "the one being RENDERED, which is the whole question behind a 'zooming is " +
                          "not smooth' report; and the furniture's 'tray=' says whether the owner's " +
                          "board is PINNED or FOLLOWing (board-UI record byte 1 bit 2).");
    }

    /// <summary>
    /// The RESOLVED mount seat per dock, for the per-peer content line. <see cref="RemoteBoardLayout"/>
    /// already prints the derived offsets; this prints where they actually LANDED on this board —
    /// the live board-local position of each dock root, plus the two mirrored panels' measure path.
    /// A "his objectives/track sit somewhere else than mine" report is then answerable from the log
    /// alone: compare the seat against the owner's own mount, and the measure path explains the size.
    /// </summary>
    private string Seats()
    {
        string track = _track != null ? _track.SeatLine : "-";
        string objectives = _objectives != null ? _objectives.SeatLine : "-";
        return $"style={_layout.Style}, initiative {track}, objectives {objectives}, " +
               $"piles={AnchorLocal(CardFxAnchor.Discard, _layout):F3}, " +
               $"slot0={AnchorLocalLive(CardFxAnchor.Slot0):F3}, " +
               $"slot1={AnchorLocalLive(CardFxAnchor.Slot1):F3}";
    }

    /// <summary>Per-section fidelity tag for <see cref="LogContent"/>: which mechanism is drawing it,
    /// and — when it is not the real widget — WHY. That "why" is the whole point: a log that only
    /// says "2 rows" cannot distinguish "the owner's panel, mirrored" from "the mod's stand-in with
    /// the same row count", which is exactly the ambiguity the last hardware round ran into.</summary>
    private static string SectionTag(RemoteWidgetMirror.Fidelity? source, string? reason)
    {
        if (source == null)
            return "-";
        if (source == RemoteWidgetMirror.Fidelity.MirroredWidget)
            return "MirroredWidget";
        string why = string.IsNullOrEmpty(reason) ? "no source" : reason!;
        return $"ModDrawn({why})";
    }

    /// <summary>Per-slot fidelity tag for <see cref="LogContent"/>: the face path when a real face is
    /// up, otherwise what the slot is actually showing (mod panel when the gate is open but neither
    /// source resolved; a card BACK when the gate is shut; nothing at all when the slot is empty).
    /// Pure read of already-computed state — it re-derives no gate of its own.</summary>
    private string FaceTag(int slot, bool showFronts)
    {
        if ((_slotOccupiedMask & (1 << slot)) == 0)
            return "empty";
        RemoteBoardCard card = _cards[slot];
        RemoteAbilityCardSource.FacePath path = card != null
            ? card.Path
            : RemoteAbilityCardSource.FacePath.None;
        if (path != RemoteAbilityCardSource.FacePath.None)
            return path.ToString();
        // Occupied but no face drawn: either the gate is shut / the panel fell back, or the WIRE
        // says a card lies there and this client has no identity for it at all (a pick candidate,
        // a queued SelectCard, an actorless peer). "anon-back" is that third case — it is the tag a
        // "I see his card back but he sees nothing / vice versa" report is answered from.
        if ((_slotAnonMask & (1 << slot)) != 0)
            return "anon-back";
        return showFronts ? "panel" : "back";
    }

    /// <summary>
    /// Seat this peer's card slots — the one place that decides, per slot, between "empty",
    /// "a card BACK" and "the real card". Sets <see cref="_slotOccupiedMask"/> and
    /// <see cref="_slotFaceMask"/> for the furniture layer and the diagnostics.
    ///
    /// ROOT CAUSE THIS SOLVES (user report, hardware MP test: "Ich will auch sehen wenn eine Karte
    /// abgelegt wurde auf dem controllboard (mit der Rueckseite). Also wo aktuell eine Karte liegt
    /// und wo nicht ... soll vollstaendig synchronisiert werden"). The slots used to be drawn from
    /// the replicated model ALONE — <c>RoundAbilityCards</c>, ordered initiative-first. The game
    /// does replicate that list live during the selection phase (<c>ProxySetStartRoundDeckState</c>),
    /// so the presence of a played card was mostly right, but the PHYSICAL truth of a VR board was
    /// not, in three ways this pass fixes:
    ///   (a) WHICH recess. The owner's board keeps a card in the slot they physically dropped it
    ///       into (<c>PlayTray.SyncFromGameState</c> deliberately preserves free placement); this
    ///       board seated the model's list order instead, so a single card dropped into the RIGHT
    ///       recess appeared in the LEFT one on every other screen.
    ///   (b) Cards the model does not have. Burn / discard / recover candidates
    ///       (<c>PlayTray.PlacePickCard</c>) and the short-rest sacrifice lie in the very same
    ///       recesses and appear in NO game list — they existed on nobody else's board at all.
    ///   (c) Latency. The VR drop parks the card instantly and queues the game's
    ///       <c>SelectCard</c> through <c>CardActionQueue</c>, so the model (and therefore the
    ///       replication) trails the visible placement.
    /// The wire's occupancy nibble is the owner's own recess state, so when it is available it is
    /// authoritative for HOW MANY cards are on the board and WHERE; the model is used only to
    /// answer WHICH card, and only through <see cref="RevealGate"/> as before. Surplus occupied
    /// slots get an anonymous BACK. No identity is added to the wire and no reveal path changes.
    ///
    /// CONVERGENCE: the mask is absolute state re-sent on every board-UI record (5 Hz floor, and an
    /// occupancy change pre-empts the rate gate), never a delta, so a dropped packet self-heals on
    /// the next one and there is no edge to miss. A sender without the field reads as "unknown" and
    /// takes the legacy branch below verbatim.
    /// </summary>
    private void SeatSlots(CPlayerActor? actor, bool showFronts)
    {
        int wire = _owner.SlotOccupancyKnown ? _owner.BoardSlotMask : -1;
        _slotOccupiedMask = 0;
        _slotFaceMask = 0;
        _slotAnonMask = 0;

        // The reveal gate closing is the latch's ONLY reset (see _latchedFaces): the next secret
        // selection phase / scenario end / actor loss all answer showFronts == false, and from
        // that frame on nothing latched during the previous action phase exists any more.
        if (!showFronts)
        {
            for (int i = 0; i < _latchedFaces.Length; i++)
                _latchedFaces[i] = null;
        }

        if (wire < 0)
        {
            // LEGACY sender (no occupancy nibble): the model IS the occupancy, per index, exactly
            // as every build before this one rendered it.
            for (int i = 0; i < SlotCount; i++)
            {
                _cards[i].Set(_ordered[i], showFronts, actor);
                if (_ordered[i] == null)
                    continue;
                _slotOccupiedMask |= 1 << i;
                if (showFronts)
                    _slotFaceMask |= 1 << i;
            }
            return;
        }

        // The owner's recesses decide. Model cards fill the occupied ones in initiative order —
        // which IS their physical order on the owner's board whenever both are occupied, because
        // CardsDriver.ReconcileInitiative drives the game's initiative to follow whatever card the
        // player put in slot 0. A slot the owner has filled but the model cannot name yet (a pick
        // candidate, a drop whose SelectCard is still queued, a peer whose actor we do not have)
        // shows an anonymous card BACK.
        int next = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if ((wire & (1 << i)) == 0)
            {
                _cards[i].Set(null, showFronts, actor); // empty recess — and it stays empty
                continue;
            }
            _slotOccupiedMask |= 1 << i;
            CAbilityCard? card = null;
            while (next < SlotCount && card == null)
                card = _ordered[next++];
            // ACTION-PHASE FACE LATCH (see _latchedFaces): while the gate is OPEN, a recess the
            // model can no longer name — the game drained RoundAbilityCards as the owner used the
            // cards mid-turn — keeps showing the face this board already legitimately showed
            // there, instead of degrading to an anonymous back for the rest of the action phase.
            // Strictly gate-scoped: the latch is only ever FILLED here under showFronts, only
            // ever READ here under showFronts, and cleared above the moment the gate shuts.
            if (card == null && showFronts)
                card = _latchedFaces[i];
            if (card == null)
            {
                _slotAnonMask |= 1 << i;
                _cards[i].SetAnonymousBack();
                continue;
            }
            _cards[i].Set(card, showFronts, actor);
            if (showFronts)
            {
                _slotFaceMask |= 1 << i;
                _latchedFaces[i] = card; // remember the approved face for this recess
            }
        }
    }

    /// <summary>Mirror the local board's ordering: <c>InitiativeAbilityCard</c> first, then the
    /// remaining round card(s). Falls back to list order when the initiative card is not yet set
    /// (e.g. mid-selection) — exactly like <c>PlayTray.SyncFromGameState</c>.</summary>
    private void OrderRoundCards(CPlayerActor actor)
    {
        _ordered[0] = _ordered[1] = null;
        CCharacterClass cc = actor.CharacterClass;
        var round = cc.RoundAbilityCards;
        CAbilityCard? initiative = cc.InitiativeAbilityCard;

        int idx = 0;
        if (initiative != null && round.Contains(initiative))
            _ordered[idx++] = initiative;
        for (int i = 0; i < round.Count && idx < 2; i++)
        {
            CAbilityCard c = round[i];
            if (c == null || c == initiative)
                continue;
            _ordered[idx++] = c;
        }
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject($"GloomhavenVR.RemoteControlBoard[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;

        // The peer's AUTHORED board layout, keyed by the style they synced. Every dock seat below
        // is read out of it, so the remote board can no longer drift from the owner's own mount
        // positions the way it had (defect (c) — see RemoteBoardLayout for the full derivation).
        _layout = new RemoteBoardLayout(_owner.BoardStyle);

        // THE BOARD SURFACE — the REAL bundled 3D asset for the style this peer synced
        // (RemoteTrayVisual: same prefab, same materials, same recesses as their own board),
        // replacing the old flat frame quad. The quad survives ONLY as the fallback for when the
        // bundle is not resident (then the probe in Tick upgrades it as soon as it is).
        _tray = RemoteTrayVisual.Build(_root.transform, _owner.BoardStyle);
        if (_tray == null)
        {
            // Fallback frame: a dark unlit slab, re-tintable to the peer's style (ApplyBoardStyle)
            // — bit-for-bit the pre-3D board, so a bundle-less client loses nothing it had.
            _frameMat = BoardVisual.Unlit(FrameColor(Cards.ControlBoard.Oak));
            BoardVisual.Quad(_root.transform, "Frame", new Vector2(BoardW, BoardH), _frameMat);
            _nextTrayProbeAt = Time.unscaledTime + TrayProbeSeconds;
        }
        _appliedStyle = -1; // force the first Tick to state what it applied (fallback tint path)

        // Round-card slots: ON the real recess anchors when the asset is up (a card then sits IN
        // the recess of whatever board the peer runs), else at the authored fallback layout on the
        // flat frame. Sized to the OWNER's synced slot-card width (extension record 11 — their
        // CardWidth × SlotScale × SlotCardFill), so the card-to-board ratio here is exactly the
        // one they see; the built sizes are latched as the change key for the live rebuild in Tick.
        _builtSlotCardW = SlotCardW;
        _builtSlotFrameW = SlotFrameW;
        float cardW = _builtSlotCardW;
        float cardH = SlotCardH;
        if (_tray != null)
        {
            _cards[0] = new RemoteBoardCard(_tray.SlotAnchor(0),
                new Vector3(0f, 0f, CardOnAnchorProudZ), cardW, cardH);
            _cards[1] = new RemoteBoardCard(_tray.SlotAnchor(1),
                new Vector3(0f, 0f, CardOnAnchorProudZ), cardW, cardH);
        }
        else
        {
            _cards[0] = new RemoteBoardCard(_root.transform, SlotLocal(0), cardW, cardH);
            _cards[1] = new RemoteBoardCard(_root.transform, SlotLocal(1), cardW, cardH);
        }

        // CONTENT hangs straight off the board root now. The old "ContentProud" spacer carried a
        // hand-estimated per-style lift because the panels below dropped the AUTHORED per-board
        // offsets that already encode that depth; they read those offsets again (RemoteBoardLayout),
        // so the spacer would double-count. See AnchorLocalLive for the full note.
        Transform contentParent = _root.transform;

        // The three stacks (report 6 + 1:1 parity defect 3 "die Stapel sehen nicht aus wie auf dem
        // Original-Board"): the destinations a remote card flight lands on, built with the SAME
        // visual construction the owner's own stacks use (PileViewer.PileStack.Create — a 4-slab
        // jittered mini pile at the authored 0.62× card footprint, per-pile tint, count ON the top
        // slab, localized caption beneath, top slab greying out at zero) instead of the old single
        // flat card-back quad. Colors are the local stacks' verbatim; sizes come from the authored
        // Defaults so every client renders a given board identically regardless of local tuning.
        _piles[0] = new PileCounter(contentParent, "DiscardStack", AnchorLocal(CardFxAnchor.Discard, _layout),
            new Color(0.55f, 0.48f, 0.34f), PileViewer.Caption(PileKind.Discard), _layout.PileScale);
        _piles[1] = new PileCounter(contentParent, "BurntStack", AnchorLocal(CardFxAnchor.Burnt, _layout),
            new Color(0.45f, 0.22f, 0.16f), PileViewer.Caption(PileKind.Burnt), _layout.PileScale);
        _piles[2] = new PileCounter(contentParent, "ItemStack", AnchorLocal(CardFxAnchor.Items, _layout),
            new Color(0.30f, 0.42f, 0.26f), PileViewer.Caption(PileKind.Items), _layout.PileScale);

        // Full-parity panels. The initiative TRACK and the OBJECTIVES panel now mirror the game's
        // OWN widgets (RemoteWidgetMirror) and keep their mod-drawn versions only as fallbacks;
        // the rest stay mod-drawn and fed from the LOCAL model — see the class note.
        _objectives = new RemoteObjectivesPanel(contentParent, _layout);
        _elements = new RemoteElementStrip(contentParent, _layout);
        _status = new RemoteStatusReadouts(contentParent, _layout);
        _pickBanner = new RemotePickBanner(contentParent, _layout);
        _boardTooltip = new RemoteBoardTooltip(contentParent, _layout);
        _active = new RemoteActiveCards(contentParent, _layout);
        _track = new RemoteInitiativeTrack(contentParent, _layout);
        _furniture = new RemoteBoardFurniture(_root.transform, _owner.BoardStyle, _tray,
            AnchorLocalLive(CardFxAnchor.Slot0), AnchorLocalLive(CardFxAnchor.Slot1),
            _builtSlotFrameW, _builtSlotCardW);
        _nextRefreshAt = 0f; // repaint on the very next tick

        // Ownership tag pinned just above the board's top-left corner, always facing the head.
        // Y clears the initiative track drawn above the top edge (RemoteInitiativeTrack, y 0.165
        // + half its 0.052 chip = 0.191) so the tag never sits on top of a track entry.
        _tag = new OwnerTag(_owner.PlayerId, _root.transform,
            new Vector3(-BoardW * 0.5f + 0.02f, 0.215f, ProudZ));

        VRLayers.Apply(_root);

        // FINAL INERTNESS GUARANTEE for the WHOLE board, not just the furniture: a remote player's
        // control board is a pure display. Nothing on it — not a keycap, not a card panel, not a
        // pile stack — may be pokeable, laser-targetable or grabbable. Everything above is built
        // from BoardVisual.Quad (collider stripped at creation) and TextMeshPro, and this sweep
        // turns that from a code-review claim into a runtime fact.
        RemoteBoardFurniture.StripColliders(_root, $"RemoteControlBoard[{_owner.PlayerId}]");

        VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] built " +
                          (_tray != null
                              ? $"on the REAL 3D '{_tray.Style}' board asset (the prefab that peer's own PlayTray renders) "
                              : "on the FLAT fallback frame (asset bundle not resident — will upgrade when it loads) ") +
                          $"at the peer's AUTHORED layout [{_layout}] " +
                          "with FULL parity surfaces: " +
                          "2 round-card slots, 3 pile stacks with counts, the game's OWN objectives " +
                          "panel and initiative TRACK mirrored as live clones, elements, " +
                          "round + rest readouts, active-card column, " +
                          "and the complete interactive furniture (Confirm/Undo keycaps on their " +
                          "native dock mounts, turn-flow Skip cap, settings gear, FOLLOW/PIN toggle, " +
                          "grab-handle bar, item-USE recess + USE cap, decision drawer, pick field, " +
                          "slot snap/wanted glows, half-card dividers) — ALL OF IT INERT: no " +
                          "colliders, no laser targets, no poke zones, no grab handles, nothing in " +
                          "any interaction registry. It is a display of a control board, not one.");
    }

    // ------------------------------------------------------------------ board style --

    /// <summary>
    /// FALLBACK frame colour standing in for a control-board MATERIAL (flat-quad board only — the
    /// real 3D asset carries its own bundled materials). Oak keeps EXACTLY the colour this board
    /// has always had, so nothing changes for a peer on the default board (or on an older build,
    /// whose zeroed style bits also read as Oak); Steel is a cool gunmetal grey and Bronze a warm
    /// dark copper, i.e. the same three materials the real boards read as, at a glance and from
    /// across the table.
    /// </summary>
    private static Color FrameColor(Cards.ControlBoard style) => style switch
    {
        Cards.ControlBoard.Steel => new Color(0.13f, 0.14f, 0.17f, 1f),
        Cards.ControlBoard.Bronze => new Color(0.16f, 0.10f, 0.05f, 1f),
        _ => new Color(0.10f, 0.09f, 0.08f, 1f), // Oak — today's colour, unchanged
    };

    /// <summary>
    /// FALLBACK-BOARD path only: tint the flat frame quad to the board style the peer chose
    /// (received in the extras block's byte A bits 5..6). Change-latched: a no-op int compare
    /// until they actually switch. When the REAL 3D asset is up this is a strict no-op — a style
    /// switch there rebuilds the whole board from the new prefab instead (see Tick), because the
    /// real Oak/Steel/Bronze boards are different meshes, not different tints.
    /// </summary>
    private void ApplyBoardStyle()
    {
        Cards.ControlBoard style = _owner.BoardStyle;
        if (_tray != null || (int)style == _appliedStyle || _frameMat == null)
            return;
        _appliedStyle = (int)style;
        _frameMat.color = FrameColor(style);
        VRLog.Info("Net", $"Remote FALLBACK board [{_owner.PlayerId}] re-tinted to the '{style}' " +
                          "control board (byte A bits 5..6, zero extra bytes) — the real prefab is " +
                          "not available on this client yet.");
    }

    /// <summary>Drop every hosted card face on this board (round slots + active column) and reset the
    /// slots' change gates, so the next visible frame re-decides from scratch. No-op before the board
    /// has ever been built.</summary>
    private void BlankCardFaces()
    {
        for (int i = 0; i < _cards.Length; i++)
            _cards[i]?.Blank();
        _active?.Blank();
        // The resolved slot masks describe what the slots are DRAWING; the slots were just blanked,
        // so the masks must go with them or the furniture would keep a snap glow / half divider up
        // over nothing, and the diagnostic would claim a card that is no longer rendered.
        _slotOccupiedMask = 0;
        _slotFaceMask = 0;
        _slotAnonMask = 0;
        _loggedContent = string.Empty; // the next visible refresh must re-state what is drawn
    }

    private void SetActive(bool active)
    {
        if (_root != null && _root.activeSelf != active)
            _root.SetActive(active);
    }

    public void Destroy()
    {
        _tag?.Destroy();
        _tag = null;
        // Drop every hosted card face FIRST. The clones are children of the board root and would die
        // with it anyway, but "we own the clone, we destroy the clone" is the contract these widgets
        // are built on (see RemoteAbilityCardSource) and it must not depend on Unity's destruction
        // order — nor on the board root still existing when a peer leaves mid-teardown.
        for (int i = 0; i < _cards.Length; i++)
            _cards[i]?.Destroy();
        _active?.Destroy();
        // Same contract for the MIRRORED game panels: we own the clone, we destroy the clone. They
        // are children of the board root and would die with it, but the ownership must not depend
        // on Unity's destruction order (see RemoteWidgetMirror / RemoteAbilityCardSource).
        _track?.Destroy();
        _objectives?.Destroy();
        _poseInit = false; // a rebuilt board (style switch / bundle upgrade) snaps again
        _easedScale = 1f;
        _loggedTargetScale = -1f;
        _nextScaleLogAt = 0f;
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
        // Every parity surface is a CHILD of _root and dies with it — drop the handles so a rebuilt
        // board (peer re-join / scene change) can never repaint through a destroyed transform, and
        // so the change-gates start clean.
        _objectives = null;
        _elements = null;
        _status = null;
        _active = null;
        _track = null;
        _furniture = null;
        _piles[0] = _piles[1] = _piles[2] = null;
        _loggedContent = string.Empty;
        _nextRefreshAt = 0f;
        // The frame material belongs to the destroyed quad; drop the handle and the style latch so a
        // rebuilt board re-applies the peer's style from scratch instead of trusting a stale int.
        // The tray visual is a child of _root and died with it — dropping the handle here is what
        // makes the next EnsureBuilt re-instantiate the (possibly different-style) prefab.
        _tray = null;
        _frameMat = null;
        _appliedStyle = -1;
        _nextTrayProbeAt = 0f;
    }

    // ------------------------------------------------------------------ pile stack --

    /// <summary>
    /// One of the three pile stacks on a peer's board (discard / burnt / items): the SAME 4-slab
    /// mini pile the owner's own board wears (mirror of <c>PileViewer.PileStack.Create</c> — four
    /// thin jittered slabs stepping into the board, per-pile tint with darkened lower slabs, the
    /// live COUNT on the top slab, the localized caption beneath, and the top slab greying out at
    /// zero exactly like the local stack). It is also the destination a <see cref="RemoteCardFx"/>
    /// flight lands on. Sized from the authored Defaults (<c>Defaults.CardWidth</c> ×
    /// <c>PileViewer.PileStack.SlabFactor</c>) — the OWNER's [Cards] tuning is local config and
    /// deliberately not applied, as everywhere on this board. Collider-free by construction.
    ///
    /// The count is PUBLIC information — vanilla lets any player open ANY other player's full card
    /// overview straight off the initiative track (<c>InitiativeTrackPlayerAvatar.OnClick</c> →
    /// <c>CardsHandManager.ToggleViewAllCards</c>) — so it needs no reveal gate. Change-gated writes:
    /// a per-tick <c>TMP.text</c> assignment re-triggers auto-size layout.
    /// </summary>
    private sealed class PileCounter
    {
        /// <summary>Authored slab footprint — the local stack's <c>CardsConfig.CardWidth ×
        /// SlabFactor</c> at the shipped default.</summary>
        private const float SlabW = Defaults.CardWidth * PileViewer.PileStack.SlabFactor;
        private const float SlabH = SlabW * (88f / 63.5f);

        private readonly TextMeshPro _count;
        private readonly Material? _topMaterial;
        private readonly Color _baseColor;
        private int _shown = int.MinValue;

        public PileCounter(Transform parent, string name, Vector3 localPos, Color color,
            string caption, float scale)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, worldPositionStays: false);
            root.localPosition = localPos;
            // The AUTHORED per-board pile scale, keyed by the peer's synced style — the same factor
            // the owner's own PileViewer stack carries.
            root.localScale = Vector3.one * (scale > 0f ? scale : 1f);
            _baseColor = color;

            // Stack body — the local recipe verbatim (PileViewer.PileStack.Create): 4 thin slabs,
            // each a step behind the previous (+Z is into the board) with a small alternating
            // jitter/tilt so it reads as a real pile; lower slabs darkened 45 %.
            Shader? shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
            for (int i = 0; i < 4; i++)
            {
                var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                slab.name = $"Slab{i}";
                Object.Destroy(slab.GetComponent<Collider>());
                slab.transform.SetParent(root, worldPositionStays: false);
                slab.transform.localScale = new Vector3(SlabW, SlabH, 0.0018f);
                float jitter = (i % 2 == 0 ? 1f : -1f) * 0.0015f;
                slab.transform.localPosition = new Vector3(jitter, -jitter, 0.0022f * (3 - i));
                slab.transform.localRotation = Quaternion.Euler(0f, 0f, (i % 2 == 0 ? -1f : 1f) * 2.5f);
                if (shader != null)
                {
                    var material = new Material(shader)
                    {
                        color = i == 3 ? color : Color.Lerp(color, Color.black, 0.45f),
                    };
                    slab.GetComponent<MeshRenderer>().sharedMaterial = material;
                    if (i == 3)
                        _topMaterial = material;
                }
            }

            // Count on the top slab + caption beneath — same font/fit calls as the local stack.
            var countGo = new GameObject("Count");
            countGo.transform.SetParent(root, worldPositionStays: false);
            countGo.transform.localPosition = new Vector3(0f, 0f, -0.0025f); // viewer side (-Z)
            _count = countGo.AddComponent<TextMeshPro>();
            _count.text = "-";
            _count.alignment = TextAlignmentOptions.Center;
            _count.color = new Color(1f, 0.95f, 0.8f);
            WorldUI.NativeButtonSkin.ApplyFont(_count);
            TmpFit.Fit(_count, SlabW * 0.9f, SlabH * 0.62f, maxFontSize: 0.30f, wrap: false);

            var captionGo = new GameObject("Caption");
            captionGo.transform.SetParent(root, worldPositionStays: false);
            captionGo.transform.localPosition = new Vector3(0f, -SlabH * 0.5f - 0.016f, -0.0025f);
            var captionTmp = captionGo.AddComponent<TextMeshPro>();
            captionTmp.text = caption.ToUpperInvariant();
            captionTmp.alignment = TextAlignmentOptions.Center;
            captionTmp.color = new Color(0.85f, 0.8f, 0.7f);
            WorldUI.NativeButtonSkin.ApplyFont(captionTmp);
            TmpFit.Fit(captionTmp, 0.095f, 0.024f, maxFontSize: 0.22f, wrap: false);
            WorldUI.MrBacking.Label(captionTmp); // below the slabs → sky/room behind it in MR
        }

        /// <summary>Write the count (change-gated); an empty pile greys its TOP SLAB out, exactly
        /// like the local board's stack dims at zero (<c>PileStack.SetCount</c>).</summary>
        public void Set(int count)
        {
            if (count == _shown)
                return;
            _shown = count;
            _count.text = count.ToString();
            if (_topMaterial != null)
            {
                Color want = count > 0 ? _baseColor : Color.Lerp(_baseColor, Color.gray, 0.7f);
                if (_topMaterial.color != want)
                    _topMaterial.color = want;
            }
        }
    }
}
