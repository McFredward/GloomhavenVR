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
internal sealed class RemoteControlBoard : WorldUI.IFurnitureOrderAnchor
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
    // the owner's [Cards] SlotOverlayScale_{board} (default 1.45!) and their CardWidth config entirely, so
    // every remote card rendered 31 % smaller than its owner sees it even between two
    // default-configured clients — the user's "nicht 1:1, ich sehe sie kleiner" report. The live
    // sizes now ride the wire (NetProtocol.ExtIdSlotCardSize) and are read via SlotCardW/SlotCardH
    // below; keep this constant equal to NetProtocol.SlotCardWidthLegacy.
    private const float CardW = 0.0635f * 1.3f;   // Defaults.CardWidth × PlayTray.SlotScale
    private const float CardH = CardW * (88f / 63.5f);

    /// <summary>The width THIS peer's slot cards must render at: their synced effective size
    /// (extension record 11 — <c>CardWidth × SlotScale × SlotOverlayScale</c>, the exact chain
    /// <c>PlayTray</c> scales a parked card by), or the legacy constant for a pre-record peer.</summary>
    private float SlotCardW => _owner.SlotCardWidth > 0f ? _owner.SlotCardWidth : CardW;
    private float SlotCardH => SlotCardW * (88f / 63.5f);

    /// <summary>The peer's slot FRAME metric (their <c>CardWidth × SlotScale</c>, record 11) — the
    /// size the furniture's glow rims follow, mirroring the local board's slot frames.</summary>
    private float SlotFrameW => _owner.SlotFrameWidth > 0f ? _owner.SlotFrameWidth : CardW;

    /// <summary>The slot-card width the current visual was BUILT at (with <see cref="_builtSlotFrameW"/>,
    /// the change key for the live-config rebuild in Tick — a peer editing their card size mid-game
    /// must re-size here too, and the widgets are constructor-sized).</summary>
    /// <summary>The <see cref="RemoteAvatar.BoardTuningRevision"/> this board's constructor-sized
    /// visuals were built at. A mismatch means the owner moved a dial (extension record 28) and the
    /// board is torn down and rebuilt at their new layout — the same trigger the slot-card size and
    /// the style switch use, and just as rare (a settings edit on their side).</summary>
    private int _builtTuningRevision = -1;

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

    /// <summary>
    /// The mirrored item-USE RECESS's transform, or null while this peer's board is not built —
    /// forwarded from <see cref="RemoteBoardFurniture.ItemUseRecess"/> so
    /// <see cref="RemoteItemFan"/> can lay the clipped item card IN it (extension record 26).
    ///
    /// <para>A TRANSFORM rather than an anchor Vector3, unlike every seat above it, and that is the
    /// point: a card in the recess must ride the recess's whole frame — its rotation, its board
    /// scale, its shown/hidden state — exactly as the owner's card rides theirs by being a child of
    /// it. Handing out a position would put the card in the right spot facing the wrong way, which
    /// is the very trap (a slab billboarded to a head instead of lying flat) this seam exists to
    /// avoid.</para>
    /// </summary>
    internal Transform? ItemUseRecess => _furniture?.ItemUseRecess;

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

    /// <summary>Character-focus turn cue for this peer (feature "free character focus"): the
    /// blinking green/red frame around their board and the ring around their Steam avatar, from
    /// the SAME <c>FocusCue</c> palette the local board and the initiative rings use. Built with
    /// the board, driven from <see cref="Tick"/>, dies with it.</summary>
    private RemoteFocusOutline? _focusOutline;

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

    /// <summary>Next unscaled time this board re-adopts its transparent subtree into its
    /// draw-order cluster (<see cref="BoardVisual.AdoptBoardOrder"/>). The content cadence: the
    /// refresh above is what CREATES most of what needs adopting, so sweeping right after it — in
    /// the same tick — is the shortest window a new element can spend at its creation order.</summary>
    private float _nextOrderSweepAt;

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

    /// <summary>
    /// The character <see cref="_latchedFaces"/> was filled for. THE SECOND RESET, added with the
    /// focus-following board (ModBuild 84): the latch above is keyed on the RECESS, not on the
    /// character, so when this peer switches which character their board is about
    /// (<see cref="RemoteBoardFocus"/>) a face latched for the previous one would otherwise be
    /// re-shown in the new one's recess for the rest of the action phase — a card that character
    /// never played. The gate-close reset cannot catch it: the gate stays open across a focus
    /// switch, which is precisely when a switch is allowed at all.
    /// </summary>
    private CPlayerActor? _latchedActor;

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

        // Read the DISPLAYED character fresh each frame (null offline / before the host assigns
        // characters / benched). JOIN-TIME REQUIREMENT: the actor is NOT a gate for the board
        // SURFACE any more — a peer's board must appear the moment their first extras packet
        // lands, character assignment or not. An actorless peer has no cards, so there is
        // nothing the reveal gate could need to hide: "no actor" counts as "not in the secret
        // phase" for the visibility rule below.
        //
        // FOLLOWS THE OWNER'S FOCUS since ModBuild 84 (user: "Jegliche Anzeige eines Boards soll
        // 1:1 synchronisiert werden im Remote-Board, auch wenn der Remote-Spieler einen anderen
        // Character ausgewählt hat"). This ONE call is where the whole requirement lands: every
        // per-actor surface below — status readouts, active pile, the two round-card recesses,
        // the furniture, the model-fallback pile counts — is parameterised by this actor and
        // therefore follows the peer's focus without a line of its own. It resolves to their
        // OWNED character whenever the focus is absent, unresolvable or suppressed, so the
        // pre-ModBuild-84 behaviour is exactly the fallback. See RemoteBoardFocus for the three
        // rules (secrecy wins, the viewer's gate still decides, never blank).
        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out bool viaFocus);
        _ = viaFocus; // the state is stated in RemoteBoardFocus' own change-gated log line

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
        // [Cards] CardWidth / SlotOverlayScale, or the record appearing on the first packet after a
        // legacy-sized build). The card panels and the furniture's glow rims are
        // constructor-sized, so the same teardown-rebuild the style switch uses applies; rare
        // (a settings edit on their side) and one frame. 0.4 mm epsilon = the wire's own
        // quantization step, so re-quantized noise can never loop rebuilds.
        // The peer moved one of their own dials (extension record 28 — a debug-menu drag or a
        // config edit on their side). Every dock seat, cap seat, overlay and mesh pose on this
        // board is CONSTRUCTOR-sized from the layout, so the same teardown-rebuild applies.
        // Revision-latched rather than value-compared: the resolve already happens once per real
        // change in RemoteAvatar, so there is nothing here to re-diff and no epsilon to pick.
        else if (_root != null && _builtTuningRevision != _owner.BoardTuningRevision)
        {
            VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] tuning change (extension record " +
                              $"28): {_owner.BoardTuning} — rebuilding the board visuals at the " +
                              "layout the owner actually sees.");
            Destroy();
        }
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

        // HALF HOVER + SELECTION (extension record 14): glow the action half the OWNER's pointer
        // is on (pulsing) and the half they have CLICKED (steady — the game's own presentation
        // split), on the same slot cards of this mirrored board — per frame (the drive is
        // change-gated flips plus one pulsing colour write) so the glow lands with the synced
        // edge, not on the 4 Hz content cadence. Rendered whether the slot shows a face, an
        // identity-known back or an anonymous back: the owner is hovering/clicking a REGION of
        // their board, and that region exists here in all three states. An empty slot cannot
        // glow (the quads live under the slot's hidden root).
        for (int s = 0; s < SlotCount; s++)
        {
            int hover = _owner.HalfHoverSlot == s ? (_owner.HalfHoverTop ? 1 : 0) : -1;
            int selWire = s == 0 ? _owner.HalfSelect0 : _owner.HalfSelect1;
            int sel = selWire == NetProtocol.HalfSelectTop ? 1
                : selWire == NetProtocol.HalfSelectBottom ? 0 : -1;
            // WHICH REGION of that half (record 14 byte 2 — item 6): the big action half, or the
            // small standard-action chip inside it. The slot's renderer drives the game's own
            // highlight for whichever one the owner named, which is the entire fix: the chip has
            // its own CardActionHighlight on the very same widget.
            bool hoverDef = hover >= 0 && _owner.HalfHoverDefault;
            bool selDef = sel >= 0 && (s == 0 ? _owner.HalfSelect0Default : _owner.HalfSelect1Default);
            _cards[s]?.SetHalfStates(hover, sel, hoverDef, selDef);
        }
        LogHalfHoverIfChanged();

        _tag!.Tick();

        // Character-focus turn cue: green while this peer owns the character at turn AND is
        // looking at it, red while they own it but are looking elsewhere, nothing otherwise.
        // The mark is re-derived locally every frame from THIS client's read of who is at turn,
        // so only their focus (record 22) is taken from the wire.
        _focusOutline?.Tick(visible: true, _tag.AvatarQuad, OwnerTag.AvatarQuadSize);

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
            // This pass is what CREATES new surfaces on the board (a card face, a pile front, a
            // readout label). Re-arm the draw-order sweep so it runs at the END of THIS tick
            // rather than up to a cadence later: two independent timers of the same period can be
            // a whole period out of phase, and that phase would be exactly how long a new surface
            // spends at its creation order instead of its board's cluster slot.
            _nextOrderSweepAt = 0f;
        }

        // THE MIRRORED WIDGETS RUN PER FRAME, not on the content cadence. They are clones of live
        // game panels driven from the original (RemoteWidgetMirror), and the user's requirement is
        // explicitly "alle Positionen, ANIMATIONEN, Effekte" — the initiative track's inter-round
        // reorder slide and the objectives' progress fill would step visibly at 4 Hz. The drive is a
        // flat walk over pre-resolved component references with change-gated writes, so this costs
        // a few hundred field compares per board per frame and allocates nothing.
        // TRACK HOVER (extension record 16) — handed to the mirror BEFORE its per-frame drive so
        // the same frame that copies the source also applies the PEER's hover on top (and strips
        // the LOCAL player's — defect (b) of the initiative-mouseover report).
        _track?.SetPeerHover(_owner.TrackHoverActorId, _owner.TrackHoverPopup);
        // CHARACTER FOCUS (extension record 22) — handed in on the same seam and for the same
        // reason: which entry this peer is LOOKING at, which entry THE GAME IS WAITING ON for them,
        // and the mark that relates the two. The board's own green/red frame (RemoteFocusOutline)
        // and these track rings are two renderings of ONE state, from one palette, so a peer's board
        // can never say "wrong character" while their track says nothing. Since 2026-08-08 the mark
        // is a pure function of the record (CharacterFocus.MarkForPeer) rather than a local turn
        // read: a peer answering a prompt during an enemy's action has no actor at turn on ANY
        // machine, and their decision is invisible on this one (TakeDamagePanel.cs:1133).
        _track?.SetPeerFocus(Board.CharacterFocus.FocusIdForPeer(_owner.PlayerId),
                             Board.CharacterFocus.AttentionIdForPeer(_owner.PlayerId),
                             Board.CharacterFocus.MarkForPeer(_owner.PlayerId));
        // TRACK SELECTION (extension record 23) — the third per-viewer fact of this widget, and
        // the one that had been answered with the WRONG source: vanilla's own selection frame is
        // not the character focus (record 22) and comes apart from it the moment an enemy is at
        // turn or a mod focus is taken. Handed in on the same seam so the drive, the hover
        // override, the frame override and the rings all settle in one frame.
        _track?.SetPeerSelection(_owner.TrackSelectionIds, _owner.TrackSelectionCount);
        // TRACK ORDER (extension record 27) — the fourth per-viewer fact of this widget, and the
        // one that had been classified as GLOBAL: vanilla sorts PLAYER entries by IsUnderMyControl
        // while online and in the card-selection phase, so this client's own track shows a
        // different arrangement from the owner's and a clone of it was showing THIS client's. The
        // same record's owned mask also decides whose portraits may wear the amber "still has to
        // choose" ring, which rode the clone in exactly the same way. Handed in on the same seam
        // so the drive, all four overrides and the rings settle in one frame.
        _track?.SetPeerTrackOrder(_owner.TrackOrderIds, _owner.TrackOrderCount,
                                  _owner.TrackOrderOwnedMask);
        // THE MEASURED SEAM (2026-08-09 multiplayer perf pass). These two live clones are, between
        // them, the largest per-peer per-frame cost the mod has: the initiative track's clone was
        // 767 nodes in the hardware log and was driven node by node every frame, whether the branch
        // was switched on or not. RemoteWidgetMirror now jumps whole switched-off subtrees; this
        // scope plus the Mirror.NodesDriven / Mirror.NodesSkipped counters are how the next
        // hardware log states — rather than implies — whether that was enough.
        using (Core.PerfMonitor.Scope("Net.BoardMirrors"))
        {
            _track?.TickLive();
            _objectives?.TickLive();
        }

        // DRAW ORDER: adopt whatever this board has grown since the last sweep into its cluster,
        // so the whole board keeps ranking against the converted-panel ladder as ONE unit and its
        // own plane keeps ranking by its own depth (BoardVisual's header — user reports #3/#5 of
        // 2026-08-09). Deliberately AFTER the content refresh and the mirror drive above: those
        // are what create the cards, chips and clones this adopts, so a new element is seated in
        // the same tick it appears. The write itself happens in the WorldUI ladder pass
        // (CanvasConversion.TickFurnitureOrder), which is the only place any order is assigned.
        if (Time.unscaledTime >= _nextOrderSweepAt && _root != null)
        {
            // The CONTENT cadence (live-tunable like every other cadence on this board), for the
            // reason stated at _nextOrderSweepAt.
            _nextOrderSweepAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;
            BoardVisual.BoardOrderSweep sweep =
                BoardVisual.AdoptBoardOrder(this, _root.transform, _tag?.Root);
            // One line per REAL change of what this board carries (the split is the gate), never
            // per sweep. Read together with the ladder's own FURNITURE ORDER line, it states the
            // absolute draw order of every surface on this peer's board.
            if (sweep.Signature != _loggedOrderSweep)
            {
                _loggedOrderSweep = sweep.Signature;
                VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] draw-order cluster: {sweep}. " +
                                  "The whole board ranks against the converted-panel ladder as one " +
                                  "unit; inside it the tier is the element's own board-local depth, " +
                                  "so a proud dock (the initiative mirror) always covers a shallower " +
                                  "one (the synced tooltip) and never the other way round.");
            }
        }
    }

    /// <summary>Change gate for the draw-order sweep line (see <see cref="Tick"/>).</summary>
    private int _loggedOrderSweep = -1;

    // ------------------------------------------------------- IFurnitureOrderAnchor (draw order) --

    string WorldUI.IFurnitureOrderAnchor.FurnitureOrderName => $"remote control board [{_owner.PlayerId}]";

    /// <summary>Alive while the board root exists — NOT gated on visibility, exactly like the local
    /// board's group: a board hidden by the reveal gate keeps its registrations and comes back with
    /// correct orders instead of paying a re-adopt at the worst possible moment.</summary>
    bool WorldUI.IFurnitureOrderAnchor.FurnitureOrderAlive => _root != null;

    /// <summary>Extra rect margin (board-local metres) around the frame when measuring this
    /// cluster's eye distance: a remote board's docks hang well off its face (the initiative
    /// mirror grows ~0.2 m past the top edge, the piles and the active column off the right), and
    /// the measure has to cover the furnished apron or a menu tucked behind an overhanging piece
    /// could out-measure the board. The local board's own value, so a peer's board and yours are
    /// ranked by the same rule.</summary>
    private const float OrderApronMeters = 0.18f;

    /// <summary>
    /// The cluster's eye distance: nearest point of the board's furnished face rect, measured in
    /// BOARD-LOCAL units and transformed back out — so the peer's synced board scale is carried by
    /// the transform rather than by an arithmetic correction here. The same clamp-into-rect measure
    /// <c>CanvasConversion.PanelEyeDistance</c> uses for panels and <c>PlayTray</c> for the local
    /// board, which is what makes the three directly comparable numbers.
    /// </summary>
    float WorldUI.IFurnitureOrderAnchor.FurnitureEyeDistance(Vector3 eye)
    {
        if (_root == null)
            return float.PositiveInfinity;
        Transform rt = _root.transform;
        Vector3 local = rt.InverseTransformPoint(eye);
        float halfW = BoardW * 0.5f + OrderApronMeters;
        float halfH = BoardH * 0.5f + OrderApronMeters;
        var onFace = new Vector3(
            Mathf.Clamp(local.x, -halfW, halfW),
            Mathf.Clamp(local.y, -halfH, halfH),
            0f);
        return Vector3.Distance(eye, rt.TransformPoint(onFace));
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
            _furniture?.Refresh(null, _owner, _slotOccupiedMask);
            // Pile counts are wire-fed too (extension record 15), so an ACTORLESS peer's stacks
            // can already show the owner's real numbers — before this the actor path was the
            // only writer and a join-time board stood at 0/0/0 regardless.
            if (_owner.HasPileCounts)
            {
                _piles[0]?.Set(_owner.PileDiscardCount);
                _piles[1]?.Set(_owner.PileBurntCount);
                _piles[2]?.Set(_owner.PileItemsCount);
            }
            // The ITEMS stack's usable cue (board-UI byte 2 bit 7) — applied on BOTH refresh paths,
            // because an actorless peer's board is exactly the case this global path exists for and
            // the cue is no more actor-dependent than the counts above it are.
            _piles[2]?.SetUsableCue(_owner.ItemsPileUsableCue);
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

            // The inert furniture layer. It is fed the SAME slot state the board is already
            // rendering (the mask SeatSlots resolved, not a second derivation of its own) — see
            // RemoteBoardFurniture for why nothing derived from it can leak anything the board does
            // not already show. It no longer needs the reveal answer or the face mask: their only
            // consumer was the half-card divider, which the 1:1 round deleted (the owner's own
            // half-poke zones are invisible by design, so a peer must not see a marker either).
            _furniture?.Refresh(actor, _owner, _slotOccupiedMask);

            // Pile counts. PREFERRED SOURCE since the count-lag defect (hardware MP test
            // 2026-08-04, 'ABGEWORFEN 0' standing while the owner's board read 2): the OWNER's
            // OWN displayed numbers, off the wire (extension record 15) — the session logs
            // proved the model read below lags a whole choreographer turn on observers (the
            // owner's model applies their action immediately; ours re-derives it only as the
            // turn plays back). FALLBACK for pre-record senders (and senders whose stacks are
            // hidden): the legacy model read — the SAME reads CardsGameApi.DiscardedCount/
            // BurntCount and ItemsPile.Count make for the local board, against this actor.
            // PUBLIC information either way: vanilla lets anyone open ANY player's full card
            // overview from the initiative track (InitiativeTrackPlayerAvatar.OnClick →
            // CardsHandManager.ToggleViewAllCards), so a count on a stack reveals nothing new
            // and needs no reveal gate.
            int discard, burnt, items;
            if (_owner.HasPileCounts)
            {
                discard = _owner.PileDiscardCount;
                burnt = _owner.PileBurntCount;
                items = _owner.PileItemsCount;
            }
            else
            {
                CCharacterClass cc = actor.CharacterClass;
                discard = cc != null ? cc.DiscardedAbilityCards.Count : 0;
                burnt = cc != null ? cc.LostAbilityCards.Count + cc.PermanentlyLostAbilityCards.Count : 0;
                CInventory? inv = actor.Inventory;
                items = inv?.AllItems != null ? inv.AllItems.Count : 0;
            }
            _piles[0]?.Set(discard);
            _piles[1]?.Set(burnt);
            _piles[2]?.Set(items);
            // THE ITEMS STACK'S USABLE CUE (board-UI byte 2 bit 7 — the 1:1 gap this round closed).
            // Straight off the owner's own rendered answer: their stack beats while an equipped item
            // can be played, and until now that animation existed on nobody else's screen. It is
            // change-gated inside SetUsableCue, so a per-refresh call costs a bool compare, and the
            // beat itself runs on THIS client's clock (no traffic between the two edges).
            _piles[2]?.SetUsableCue(_owner.ItemsPileUsableCue);

            LogContent(discard, burnt, items, showFronts, actor);
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote board [{_owner.PlayerId}] content refresh failed: {e.Message}");
        }
    }

    /// <summary>Last stated half state (hover slot|top plus the two selection fields, packed;
    /// int.MinValue never) — the change gate for the render-path evidence line below.</summary>
    private int _loggedHalfHover = int.MinValue;

    /// <summary>Change-gated evidence that the synced half hover AND click state reached the
    /// render path (grep: "Remote board half-hover").</summary>
    private void LogHalfHoverIfChanged()
    {
        int hoverKey = _owner.HalfHoverSlot >= 0
            ? _owner.HalfHoverSlot | (_owner.HalfHoverTop ? 1 << 8 : 0)
            : -1;
        int now = (hoverKey + 2) | (_owner.HalfSelect0 << 16) | (_owner.HalfSelect1 << 20)
                  | (_owner.HalfHoverDefault ? 1 << 24 : 0)
                  | (_owner.HalfSelect0Default ? 1 << 25 : 0)
                  | (_owner.HalfSelect1Default ? 1 << 26 : 0);
        if (now == _loggedHalfHover)
            return;
        _loggedHalfHover = now;
        // REGION WORDING IS THE SENDER'S, VERBATIM ("TOP half" / "TOP standard-action field"), so
        // the owner's "Half hover SENT" / "Half selection SENT" lines and this one can be compared
        // side by side without converting anything — that comparison is what item 6 needed and did
        // not have (the observer could only ever print "half").
        string Sel(int v, bool def) => v == NetProtocol.HalfSelectTop
            ? (def ? "TOP standard-action field" : "TOP half")
            : v == NetProtocol.HalfSelectBottom
                ? (def ? "BOTTOM standard-action field" : "BOTTOM half")
                : "none";
        VRLog.Info("Net", $"Remote board half-hover [{_owner.PlayerId}]: " +
                          (hoverKey >= 0
                              ? $"slot {_owner.HalfHoverSlot + 1} " +
                                $"{(_owner.HalfHoverTop ? "TOP" : "BOTTOM")} " +
                                $"{(_owner.HalfHoverDefault ? "standard-action field" : "half")} pulses"
                              : "no hover") +
                          $"; clicked: slot 1 = {Sel(_owner.HalfSelect0, _owner.HalfSelect0Default)}, " +
                          $"slot 2 = {Sel(_owner.HalfSelect1, _owner.HalfSelect1Default)} (steady) — " +
                          "extension record 14: slot positions, halves and which of each half's two " +
                          "regions, no card identity; pulse = the owner's pointer, steady = their " +
                          "committed click (cleared by their undo), the same two-state split their " +
                          "own card shows.");
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
    private void LogContent(int discard, int burnt, int items, bool showFronts, CPlayerActor? actor)
    {
        // FIDELITY + ANTI-CHEAT in one greppable line: which mechanism drew each round card
        // (LiveWidget / PooledBorrow = the REAL game card face; None = the mod-drawn fallback panel),
        // together with the gate answer that allowed a face at all. Grep: "Remote board content".
        string slots = $"{FaceTag(0, showFronts)}/{FaceTag(1, showFronts)}";
        // The CHARACTER rides this line too: the pile counts below are that character's, and the
        // owner's board switches which one it presents whenever they focus a teammate. Without the
        // name, "die Zahlen auf seinem Brett stimmen nicht" cannot be answered from a peer log.
        string line = $"Remote board content [{_owner.PlayerId}]: " +
                      $"char='{Board.CharacterFocus.Describe(actor)}', " +
                      $"round='{(_status != null ? _status.RoundText : "-")}', " +
                      $"initiative={(_status != null ? _status.InitiativeText : "?")}, " +
                      $"rest='{(_status != null ? _status.RestText : string.Empty)}', " +
                      $"piles d/b/i={discard}/{burnt}/{items}" +
                      $"{(_owner.HasPileCounts ? "(synced)" : "(model)")}, " +
                      $"item-cue={(_owner.ItemsPileUsableCue ? "beating" : "off")}, " +
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

        // TWO resets, and only two (see _latchedFaces / _latchedActor):
        //   • the reveal gate closing — the next secret selection phase / scenario end / actor loss
        //     all answer showFronts == false, and from that frame on nothing latched during the
        //     previous action phase exists any more;
        //   • the DISPLAYED CHARACTER changing — a face approved for one character must never be
        //     repeated in another character's recess just because the recess index matched.
        if (!showFronts || !ReferenceEquals(actor, _latchedActor))
        {
            for (int i = 0; i < _latchedFaces.Length; i++)
                _latchedFaces[i] = null;
        }
        _latchedActor = actor;

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

    /// <summary>
    /// Which round card belongs in which recess. TAKEN FROM THE OWNER when they state it
    /// (record 18, <see cref="RemoteAvatar.SlotOrderKnown"/>), derived only when they cannot.
    ///
    /// <para>WHY THE OWNER HAS TO STATE IT (user report 2026-08-15, item 8: "Die Position der
    /// Karten (linke Karte/rechte Karte) war in einem Test verdreht wenn ich einen Character
    /// anklicke die einem anderen Spieler gehört. Die Reihenfolge MUSS zwingend identisch sein wie
    /// es der jenige Spieler auch sieht."). This method's old rule —
    /// <c>InitiativeAbilityCard</c> first, then the rest of <c>RoundAbilityCards</c> — is only ONE
    /// of three rules the mod uses for the same two cards, and it is not the one the owner's own
    /// ACTION-phase dock uses: that dock takes the iteration order of the owner's
    /// <c>CardsHandUI.cardsUI</c> list (<c>CardsDriver.CollectRoundCards</c> →
    /// <c>HalfSelection.SetCards</c>). The two rules agree only when the printed-initiative sort
    /// happens to put the initiative card first. Session evidence, one session on three machines:
    /// the owner of 'Hilde Die 2Te' had UnbridledPower LEFT during selection
    /// (<c>remote2/LogOutput.log:46564</c>) and FatalFury LEFT once his cards docked
    /// (<c>:46580</c>) — while this method kept UnbridledPower on the left on both watchers.</para>
    ///
    /// <para>The wire fact is one BIT against a reference every client resolves identically, so
    /// nothing here changed about how the cards themselves are resolved (still the replicated
    /// model, still gated by <see cref="RevealGate"/>) — only which recess each one goes in. When
    /// the record is absent (the owner could not answer, or a peer predating it) the legacy
    /// derivation below runs unchanged, so this can only ever replace a guess with the truth.</para>
    /// </summary>
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

        // THE OWNER'S OWN ANSWER, applied last: the pair above is now (initiative, other), which
        // is exactly the arrangement the swap bit is defined against. Only a full pair can be
        // swapped — a single card has no order, and the seating loop puts it in the first occupied
        // recess either way.
        if (_owner.SlotOrderKnown && _owner.SlotOrderSwapped
            && _ordered[0] != null && _ordered[1] != null)
        {
            (_ordered[0], _ordered[1]) = (_ordered[1], _ordered[0]);
        }
        LogSlotOrderIfChanged();
    }

    /// <summary>Last stated slot-order key (-1 derived, 0 stated unswapped, 1 stated swapped;
    /// int.MinValue never) — the change gate for the line below.</summary>
    private int _loggedSlotOrder = int.MinValue;

    /// <summary>Change-gated evidence that the owner's OWN left/right order reached this board's
    /// seating (grep: "Remote board slot order"). Printed in the same words the sender's "Slot
    /// order SENT" line uses, so the two logs compare without arithmetic.</summary>
    private void LogSlotOrderIfChanged()
    {
        int now = _owner.SlotOrderKnown ? (_owner.SlotOrderSwapped ? 1 : 0) : -1;
        if (now == _loggedSlotOrder)
            return;
        _loggedSlotOrder = now;
        VRLog.Info("Net", _owner.SlotOrderKnown
            ? $"Remote board slot order [{_owner.PlayerId}]: LEFT recess (slot 1) holds the " +
              $"{(_owner.SlotOrderSwapped ? "NON-INITIATIVE" : "INITIATIVE")} round card — STATED " +
              "by the owner (extension record 18), not derived here. This board now seats the pair " +
              "the way its owner physically placed it; the cards themselves are still resolved " +
              "from the replicated model through the reveal gate, so no identity came off the wire."
            : $"Remote board slot order [{_owner.PlayerId}]: not stated — falling back to this " +
              "client's own InitiativeAbilityCard-first derivation (a sender that could not answer, " +
              "or one predating record 18). The pair may sit reversed against the owner's screen; " +
              "that is the pre-2026-08-15 behaviour, kept rather than replaced by a second guess.");
    }

    private void EnsureBuilt()
    {
        if (_root != null)
            return;

        _root = new GameObject($"GloomhavenVR.RemoteControlBoard[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;

        // The peer's OWN board layout — their synced style, plus every dial they have moved off
        // the shipped default (extension record 28). Every dock seat below is read out of it, so
        // the remote board can no longer drift from the owner's own mount positions the way it had
        // (defect (c) — see RemoteBoardLayout for the full derivation).
        _builtTuningRevision = _owner.BoardTuningRevision;
        _layout = new RemoteBoardLayout(_owner.BoardTuning);

        // THE BOARD SURFACE — the REAL bundled 3D asset for the style this peer synced
        // (RemoteTrayVisual: same prefab, same materials, same recesses as their own board),
        // replacing the old flat frame quad. The quad survives ONLY as the fallback for when the
        // bundle is not resident (then the probe in Tick upgrades it as soon as it is).
        _tray = RemoteTrayVisual.Build(_root.transform, _owner.BoardTuning);
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
        // CardWidth × SlotScale × SlotOverlayScale), so the card-to-board ratio here is exactly the
        // one they see; the built sizes are latched as the change key for the live rebuild in Tick.
        _builtSlotCardW = SlotCardW;
        _builtSlotFrameW = SlotFrameW;
        float cardW = _builtSlotCardW;
        float cardH = SlotCardH;
        // THE PAIR SPREAD BELONGS ON THE CARDS TOO (2026-08-11). The owner's round cards do not sit
        // at the bare recess anchor: every local slot-home path adds SlotHomeOffsetFor(slot), whose
        // X carries [Cards] SlotOverlayOffset plus HALF the [Cards] SlotOverlaySpacing pair spread
        // with opposite sign per slot — that is the whole point of the debug menu's "Overlays"
        // element, glow and resting card move together. The remote GLOWS already took that seat
        // (RemoteBoardFurniture.SlotOverlayLocal); the remote CARDS were built at the anchor with
        // nothing added, so a peer saw the owner's pair sitting wider than the owner does and off
        // its own glow — on Oak's shipped values, 10.4 mm board-local, ≈5.5 mm world. It is the same
        // omission the owner's own docked cards had until this round (HalfSelection.SetCards).
        //
        // WHY THE FRAME CONVERSION, and do not simplify it away: SlotOverlayLocal returns BOARD-LOCAL
        // metres (the authored slot-local value already multiplied by PlayTray.SlotScale), because
        // the LOCAL slot transform is scaled by SlotScale in PlayTray.1.Core (`slot.localScale *=
        // SlotScale`) and everything parented under it inherits that. The REMOTE anchors are the raw
        // prefab transforms — RemoteTrayVisual never scales them — so their local frame is NOT the
        // local build's slot frame, and adding board-local metres to a child of one would be a
        // silent scale error. Converting through the board root is exact and needs no assumption
        // about what scale the prefab anchor happens to carry, which is scene data we cannot read.
        // The glows need no conversion because they hang off the board ROOT already.
        if (_tray != null)
        {
            for (int i = 0; i < 2; i++)
            {
                Transform anchor = _tray.SlotAnchor(i);
                Vector3 seatBoardLocal = RemoteBoardFurniture.SlotOverlayLocal(_owner.BoardTuning, i);
                Vector3 seatOnAnchor = anchor.InverseTransformVector(
                    _root.transform.TransformVector(seatBoardLocal));
                _cards[i] = new RemoteBoardCard(anchor,
                    seatOnAnchor + new Vector3(0f, 0f, CardOnAnchorProudZ), cardW, cardH);
            }
        }
        else
        {
            // Fallback frame: the flat authored layout hangs off the board root, so the board-local
            // seat adds directly — no conversion, same reason the glows need none.
            _cards[0] = new RemoteBoardCard(_root.transform,
                SlotLocal(0) + RemoteBoardFurniture.SlotOverlayLocal(_owner.BoardTuning, 0), cardW, cardH);
            _cards[1] = new RemoteBoardCard(_root.transform,
                SlotLocal(1) + RemoteBoardFurniture.SlotOverlayLocal(_owner.BoardTuning, 1), cardW, cardH);
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
        // …and the OWNER's own item-cue dials ride along (record 28, ids 161..165): only the items
        // stack ever builds the cue, but all three are handed the tuning so the day another stack
        // grows one there is no second place to remember.
        _piles[0] = new PileCounter(contentParent, "DiscardStack", AnchorLocal(CardFxAnchor.Discard, _layout),
            new Color(0.55f, 0.48f, 0.34f), PileViewer.Caption(PileKind.Discard), _layout.PileScale,
            _owner.BoardTuning);
        _piles[1] = new PileCounter(contentParent, "BurntStack", AnchorLocal(CardFxAnchor.Burnt, _layout),
            new Color(0.45f, 0.22f, 0.16f), PileViewer.Caption(PileKind.Burnt), _layout.PileScale,
            _owner.BoardTuning);
        _piles[2] = new PileCounter(contentParent, "ItemStack", AnchorLocal(CardFxAnchor.Items, _layout),
            new Color(0.30f, 0.42f, 0.26f), PileViewer.Caption(PileKind.Items), _layout.PileScale,
            _owner.BoardTuning);

        // Full-parity panels. The initiative TRACK and the OBJECTIVES panel now mirror the game's
        // OWN widgets (RemoteWidgetMirror) and keep their mod-drawn versions only as fallbacks;
        // the rest stay mod-drawn and fed from the LOCAL model — see the class note.
        _objectives = new RemoteObjectivesPanel(contentParent, _layout);
        _elements = new RemoteElementStrip(contentParent, _layout);
        _status = new RemoteStatusReadouts(contentParent, _layout);
        _pickBanner = new RemotePickBanner(contentParent, _layout);
        _boardTooltip = new RemoteBoardTooltip(contentParent, _layout, _owner.BoardTuning);
        _active = new RemoteActiveCards(contentParent, _layout);
        _track = new RemoteInitiativeTrack(contentParent, _layout);
        _furniture = new RemoteBoardFurniture(_root.transform, _owner.BoardTuning, _tray,
            AnchorLocalLive(CardFxAnchor.Slot0), AnchorLocalLive(CardFxAnchor.Slot1),
            _builtSlotFrameW, _builtSlotCardW);
        _nextRefreshAt = 0f; // repaint on the very next tick

        // Ownership tag pinned just above the board's top-left corner, always facing the head.
        // Y clears the initiative track drawn above the top edge (RemoteInitiativeTrack, y 0.165
        // + half its 0.052 chip = 0.191) so the tag never sits on top of a track entry.
        _tag = new OwnerTag(_owner.PlayerId, _root.transform,
            new Vector3(-BoardW * 0.5f + 0.02f, 0.215f, ProudZ));

        // Character-focus turn cue (frame + avatar ring). Built AFTER the tag so the ring can
        // seat itself against the tag's avatar quad on the first tick that has one.
        _focusOutline = new RemoteFocusOutline(_owner.PlayerId, _root.transform,
                                               new Vector2(BoardW, BoardH));

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
        _focusOutline?.Destroy();
        _focusOutline = null;
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
        // …and the third one: the furniture's mirrored DECISION ROW (ModBuild 105). It is a clone of
        // this client's own TakeDamagePanel widgets, registered with MrBacking, so it must be
        // released explicitly rather than left to the board root's destruction.
        _furniture?.Destroy();
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
        _builtTuningRevision = -1;
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

        // ---- THE MIRRORED "AN ITEM IS USABLE" CUE (board-UI byte 2 bit 7) ------------------------
        //
        // WHY IT EXISTS AT ALL. Until this change a peer's mirrored items stack was three inert
        // slabs and a number: the owner's stack puffs gold embers and throws rings of light on the
        // shared item heartbeat while an equipped item can be played, and NONE of that crossed the
        // wire. That is a straight breach of the standing 1:1 ruling — "alle Interaktionen,
        // ANIMATIONEN und Anzeigen des Controllboards" — and a pre-existing one rather than
        // something the 2026-08-09 re-art introduced; the old ember drift was equally invisible to
        // peers. The whole point of the cue is that it is readable without being looked at, and on
        // every screen but its owner's it did not exist at any amplitude.
        //
        // WHAT IS MIRRORED: the recipe of PileViewer.PileStack.BuildUsableRings /
        // .BuildUsableEmbers, term for term, off the SHIPPED defaults (see the frozen constants
        // below and scripts/check-remote-defaults.py). Two SoftCuePing rings sharing one period at
        // opposite phases so a ring leaves the stack on every beat, plus the ember emitter whose
        // loop duration IS the beat, so one burst at t=0 puffs on every heartbeat with no driver.
        //
        // DRIVEN ON THIS CLIENT'S CLOCK, from the wire bit's EDGES only — the same synced-state /
        // locally-animated split the wanted-slot glow and the mirrored keycap dust already use. Two
        // edges per decision, not a stream: the cue beats roughly twice a second and costs nothing.
        //
        // BUILT LAZILY AND ONLY ON THE ITEMS STACK, exactly like the local one: the discard and
        // burnt stacks are never handed a true here, so they never pay for a particle system and
        // two generated band textures they would not show.
        private ParticleSystem? _usableEmbers;
        private WorldUI.SoftCueReveal? _usableRings;
        private bool _usableCueOn;
        private readonly Transform _root;

        // ---- the owner's own CUE dials (extension record 28, ids 161..165) -----------------------
        // WIRE-OVERRIDABLE FALLBACKS, the same shape RemoteHandFan's geometry and RemoteItemFan's
        // animation already use: the value the owner set where they moved the dial, this client's
        // shipped constant where they did not — which is the same number, so an untuned peer's cue
        // beats exactly as this build ships it. Seeded in the constructor; the rings and embers are
        // built LAZILY on the first SetUsableCue(true), by which time these are long since set, and
        // a change to the owner's tuning rebuilds the whole board (_builtTuningRevision).
        //
        // THEY WERE `const` UNTIL THIS ROUND, for a capacity reason and no other: record 28 stood at
        // exactly its 255-byte per-record ceiling when this cue landed, so its dials could not ride
        // and every peer beat at the shipped tempo whatever its owner had tuned. Paging removed the
        // ceiling (Net/BoardTunePages.cs) and reserved these ids for exactly these dials, so the
        // reason expired — and this cue is precisely what the 1:1 ruling is about: "Ändert ein
        // Spieler also die Positionen für sich selber, so sollen alle anderen diese Position bei
        // seinem board auch sehen" (2026-08-09), read together with the older ruling that names
        // ANIMATIONS outright.
        //
        // Naming the Defaults entries rather than re-typing the numbers is still what keeps an
        // untuned table in agreement when a default moves; the pairs stay pinned in
        // scripts/check-remote-defaults.py, which accepts this form for that exact reason.
        private float _itemCueBeatSeconds = Defaults.ItemCueBeatSeconds;
        private float _itemCueRingReach = Defaults.ItemCueRingReach;
        private float _itemCueRingAlpha = Defaults.ItemCueRingAlpha;
        private float _itemCueEmberRate = Defaults.ItemCueEmberRate;
        private float _itemCueEmberSize = Defaults.ItemCueEmberSize;

        /// <summary>Ring line thickness as a fraction of its own starting diameter — verbatim
        /// <c>PileViewer.PileStack.RingBandFraction</c>: thick enough that the two-tone edge
        /// survives the motion-blurred passthrough feed, thin enough to stay a ring.</summary>
        private const float RingBandFraction = 0.11f;

        /// <summary>The mod's telegraph gold warmed toward the initiative ring's amber — verbatim
        /// <c>PileViewer.PileStack.EmberColor</c>. Alpha is the ember's CEILING; the lifetime
        /// gradient never lets a mote hold it.</summary>
        private static readonly Color EmberColor = new(1f, 0.80f, 0.36f, 0.85f);

        public PileCounter(Transform parent, string name, Vector3 localPos, Color color,
            string caption, float scale, in RemoteBoardTuning tuning)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, worldPositionStays: false);
            root.localPosition = localPos;
            _root = root;
            // The AUTHORED per-board pile scale, keyed by the peer's synced style — the same factor
            // the owner's own PileViewer stack carries.
            root.localScale = Vector3.one * (scale > 0f ? scale : 1f);
            _baseColor = color;

            // The OWNER's own cue dials (record 28, ids 161..165), taken HERE and not where the cue
            // is built: the rings and embers are built lazily on the first SetUsableCue(true), an
            // edge that may arrive many seconds later, while the tuning is a build-time fact (a
            // change to it rebuilds the whole board). Guarded the way the local PileViewer guards
            // its own copies, because a WIRE value is never trusted — a zero beat divides.
            _itemCueBeatSeconds = Mathf.Max(0.2f, tuning.ItemCueBeatSeconds);
            _itemCueRingReach = Mathf.Max(1f, tuning.ItemCueRingReach);
            _itemCueRingAlpha = Mathf.Clamp01(tuning.ItemCueRingAlpha);
            _itemCueEmberRate = Mathf.Max(0f, tuning.ItemCueEmberRate);
            _itemCueEmberSize = Mathf.Max(0.1f, tuning.ItemCueEmberSize);

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

        /// <summary>
        /// USABLE-HIGHLIGHT, MIRRORED — start (or stop) this stack's "something in here is playable"
        /// cue from the owner's own wire bit (board-UI byte 2 bit 7). The receiver-side twin of
        /// <c>PileViewer.PileStack.SetUsableHighlight</c>, change-gated the same way so nothing is
        /// re-triggered per refresh.
        ///
        /// <para>Switching OFF stops ember EMISSION only, so the motes already in flight finish their
        /// fade instead of vanishing mid-air (a hard clear is what reads as a bug when the owner's
        /// turn ends), and the rings COLLAPSE OUT through <see cref="WorldUI.SoftCueReveal"/> rather
        /// than blinking away — the standing "nothing pops" rule, on the peer's board as on the
        /// owner's.</para>
        ///
        /// <para>Mod-owned children of a mod-owned stack: hidden with the board, destroyed with it,
        /// nothing game-side touched, nothing pokeable (both roots are built after the board's
        /// collider strip but neither creates a collider — the ring quads and the particle renderer
        /// are collider-free by construction).</para>
        /// </summary>
        /// <summary>
        /// Run (or stop) the mirrored "something in this pile is playable" heartbeat.
        ///
        /// <para>UNREACHABLE UNTIL ModBuild 137+1 — user report 2026-08-13, verbatim: "Die
        /// Item-Animation auf dem Pile bei der Spieler sieht, dass Gegenstände nutzbar sind, wird
        /// nicht synchronisiert - auch diese soll voll synchronisiert werden so wie alle anderen
        /// Animationen auch." Every line below shipped in ModBuild 121 and is term-for-term the
        /// local recipe, but the boolean that drives it could only ever be false: the sender packed
        /// the cue into board-UI byte 2 and then never copied that byte into the packet (see the
        /// BoardCapStateMask note in <c>NetAvatarDriver</c>). The two ModBuild-137 logs prove both
        /// halves — the owners' own "ITEM highlight: 3/7 item(s) usable now … stack cue ON" against
        /// the receivers' "item-cue=off" on every single content tick.</para>
        /// </summary>
        public void SetUsableCue(bool on)
        {
            if (on == _usableCueOn)
                return;
            _usableCueOn = on;
            Core.VRLog.Info("Net", $"PILE ANIM: mirrored items stack cue {(on ? "ON" : "off")} — " +
                                   "the owner's own edge (board-UI byte 2 bit 7), running the ember " +
                                   "puffs and outward rings on this client's clock at the owner's " +
                                   "tuned beat. No item identity rides that bit.");

            if (_usableEmbers == null && on)
                _usableEmbers = BuildUsableEmbers(); // null in a shader-less environment: degrade, never crash
            if (_usableEmbers != null)
            {
                if (on)
                    _usableEmbers.Play();
                else
                    _usableEmbers.Stop(withChildren: false, ParticleSystemStopBehavior.StopEmitting);
            }

            if (_usableRings == null && on)
                _usableRings = BuildUsableRings();
            if (_usableRings == null)
                return;
            if (on)
            {
                if (!_usableRings.gameObject.activeSelf)
                    _usableRings.gameObject.SetActive(true);
                _usableRings.Show();
            }
            else
            {
                _usableRings.Hide();
            }
        }

        /// <summary>
        /// The mirrored RING emitter — two <see cref="WorldUI.SoftCuePing"/> quads sharing one period
        /// at opposite phases, so a ring leaves the stack on every beat and the cue is never
        /// continuous and never silent for long. Mirror of <c>PileViewer.PileStack.BuildUsableRings</c>.
        ///
        /// <para>ROUND, and TWO-TONE, for the reasons written once at <c>WorldUI.SoftCueArt</c>: a
        /// frame around the stack would be the rectangle of light the user rejected, and a cue drawn
        /// in ONE tone can only be seen where it differs in luminance from a background nobody
        /// controls — least of all here, where the backdrop of a peer's floating board in mixed
        /// reality is the viewer's own room. Outward travel is the "look over here" sentence; the
        /// item-use berth on the same board says the mirror-image "put it in here" inward.</para>
        ///
        /// <para>Parked a hair proud of the top slab and BEHIND the count label's plane (−0.0018 vs
        /// the label's −0.0025), so a ring can never fog the number it flies around.</para>
        /// </summary>
        private WorldUI.SoftCueReveal? BuildUsableRings()
        {
            float alpha = Mathf.Clamp01(_itemCueRingAlpha);
            float reach = Mathf.Max(1f, _itemCueRingReach);
            if (alpha <= 0.002f || reach <= 1.001f)
                return null; // dialled off by the OWNER (or in the shipped defaults) — build nothing
            float beat = Mathf.Max(0.2f, _itemCueBeatSeconds);
            float seed = Mathf.Max(SlabW, SlabH) * 1.05f; // just around the stack's own footprint

            var root = new GameObject("UsableRings");
            root.transform.SetParent(_root, worldPositionStays: false);
            root.transform.localPosition = new Vector3(0f, 0f, -0.0018f);
            root.transform.localRotation = Quaternion.identity;
            // The arrival/departure driver goes on FIRST: SoftCuePing caches its parent reveal in
            // Init, and it is what fades the rings in and out instead of letting them blink.
            var reveal = root.AddComponent<WorldUI.SoftCueReveal>();
            reveal.DeactivateTarget = root;
            reveal.Configure(RingRevealSeconds);

            Color gold = WorldUI.SoftCueArt.KeySafe(new Color(1f, 0.80f, 0.36f, alpha));
            for (int i = 0; i < 2; i++)
            {
                GameObject ring = WorldUI.SoftCueArt.RingQuad($"Ring{i}", root.transform,
                    Vector3.zero, seed, seed * RingBandFraction, gold);
                // DRAW ORDER: none written here. This board's cluster sweep seats the ring at the
                // tier its own board-local depth earns (BoardVisual.AdoptBoardOrder) — it hugs the
                // board face at z = −0.0018, i.e. the furniture tier this used to assert.
                var mr = ring.GetComponent<MeshRenderer>();
                if (mr == null)
                    continue; // no renderer, nothing to ping — degrade, never crash
                var ping = ring.AddComponent<WorldUI.SoftCuePing>();
                ping.Phase = i * 0.5f; // the two rings split the period between them
                ping.Init(mr, gold,
                    new Vector3(seed, seed, 1f),
                    new Vector3(seed * reach, seed * reach, 1f),
                    beat * 2f, duty: 0.85f);
            }

            VRLayers.Apply(root); // mod-owned FX on the mod layer, so the owned head camera renders it
            reveal.Show();
            return reveal;
        }

        /// <summary>Seconds the mirrored ring cue takes to grow in / collapse out — verbatim the
        /// 0.28 s <c>PileViewer.PileStack.BuildUsableRings</c> configures its reveal with. Not a
        /// config dial on either side, so there is no Defaults entry to name.</summary>
        private const float RingRevealSeconds = 0.28f;

        /// <summary>
        /// The mirrored EMBER emitter — mirror of <c>PileViewer.PileStack.BuildUsableEmbers</c>,
        /// module for module. Local simulation space so the motes ride the peer's board when they
        /// carry it (world space would smear them into a trail behind it), a flattened box shape over
        /// the whole pile face, and velocity authored rather than taken from the shape normal because
        /// the stack lies FLAT: "up" for this cue is the board's own +Y with a lean toward the viewer.
        ///
        /// <para>ONE LOOP IS ONE BEAT — <c>main.duration = beat</c> — which is what makes a single
        /// burst at time 0 puff on every heartbeat with no per-frame driver and no clock of its own to
        /// drift against the rings'. The thin continuous bed between puffs keeps the pile alive; the
        /// puff is the transient peripheral vision actually answers to.</para>
        ///
        /// <para>Material: <c>Sprites/Default</c> textured with <c>SoftCueArt.MoteTexture</c>, because
        /// untextured that shader draws hard SQUARES — the exact look the round-mote texture exists to
        /// replace. Returns null only when even that shader is missing.</para>
        /// </summary>
        private ParticleSystem? BuildUsableEmbers()
        {
            Shader? shader = Shader.Find("Sprites/Default") ?? Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                return null;

            var go = new GameObject("UsableEmbers");
            go.transform.SetParent(_root, worldPositionStays: false);
            // Just proud of the top slab (which spans ±0.0009 about z 0) so the motes are never born
            // inside the pile, but behind the count/caption text at −0.0025 so they never fog it.
            go.transform.localPosition = new Vector3(0f, 0f, -0.0016f);
            go.transform.localRotation = Quaternion.identity;

            float beat = Mathf.Max(0.2f, _itemCueBeatSeconds);
            float rate = Mathf.Max(0f, _itemCueEmberRate);
            float emberSize = Mathf.Max(0.1f, _itemCueEmberSize);

            var ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // follows the per-style pile scale
            main.playOnAwake = false;
            main.loop = true;
            main.duration = beat; // one loop is one beat — see the doc
            main.maxParticles = 128;
            main.startSpeed = 0f; // drift comes from velocityOverLifetime below
            main.gravityModifier = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(
                SlabW * 0.045f * emberSize, SlabW * 0.11f * emberSize);
            main.startColor = EmberColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rate * 0.35f;
            int puff = Mathf.Clamp(Mathf.RoundToInt(rate * 0.65f * beat), 0, 60);
            emission.SetBursts(puff > 0
                ? new[] { new ParticleSystem.Burst(0f, (short)puff) }
                : System.Array.Empty<ParticleSystem.Burst>());

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(SlabW * 0.85f, SlabH * 0.85f, 0.0001f); // a flat sheet over the face
            shape.randomDirectionAmount = 0f;

            ParticleSystem.VelocityOverLifetimeModule vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-0.008f, 0.008f);
            vel.y = new ParticleSystem.MinMaxCurve(0.022f, 0.048f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.018f, -0.006f);

            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.strength = new ParticleSystem.MinMaxCurve(0.006f);
            noise.frequency = 0.35f;
            noise.scrollSpeed = new ParticleSystem.MinMaxCurve(0.12f);
            noise.damping = true;

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.55f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            var mat = new Material(shader) { mainTexture = WorldUI.SoftCueArt.MoteTexture() };
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // DRAW ORDER: none written here either (see the ping rings above). The motes rise out
            // of the pile stack and are seated with it by this board's cluster sweep; their own
            // toward-the-viewer velocity keeps them in front of the stack's decor on the distance
            // tie-break, which is what the local emitter's +2 expresses on the owner's board.

            VRLayers.Apply(go); // mod-owned FX on the mod layer (no children — recursion-safe)
            return ps;
        }
    }
}
