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
///     (their short/long rest state used to be listed here as its own readout. It is still SHOWN —
///      by the mirrored rest disc CAPS, which the interactive-furniture bullet below covers — but
///      the plate that carried it was deleted on 2026-08-28: the owner has no such widget, so it
///      was a thing every peer could see and its owner could not. See RemoteStatusReadouts.)
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
/// <c>VRInteractables</c> or to any other interaction registry, and a BUILD-TIME sweep
/// (<see cref="RemoteBoardFurniture.StripColliders"/>, six fixed call sites) destroys any collider
/// the game's own prefabs brought along. It is not a runtime guard: content built after those
/// sweeps — the lazy pile-cue rings and embers, the tooltip canvas, the focus outline, the grab-bar
/// rod, every <see cref="RemoteWidgetMirror"/> clone — is never swept, and its inertness rests on
/// each builder creating no collider in the first place (review R2, 2026-09-07).
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

    /// <summary>
    /// The owner's <c>[Cards] SlotCardInset</c> — how deep a played card seats into the physical
    /// slot recess, in AUTHORED slot-local metres toward the viewer, re-read from
    /// <see cref="RemoteBoardTuning.SlotCardInset"/> (record 28, wire id
    /// <see cref="NetProtocol.TuneSlotCardInset"/>) every time this board is built. A GLOBAL dial,
    /// not per-board, which is why it carries no style key; the x<c>SlotScale</c> conversion into
    /// board metres happens at the one place that consumes it, <see cref="SlotCardSeatLocal"/>.
    ///
    /// <para>The INITIALISER is what an untuned or pre-record peer's card is drawn with, which is
    /// what puts this field on <c>scripts/check-remote-defaults.py</c>'s list — the same guarantee
    /// every keycap size on that list carries, and the reason it is a seeded field rather than a
    /// bare <c>_owner.BoardTuning.SlotCardInset</c> read at the use site.</para>
    ///
    /// <para>DO NOT CONFLATE IT WITH <see cref="ProudZ"/>, which is also -0.004f: that one is the
    /// flat fallback board's shared content plane — a different surface, moved by no dial — and the
    /// coincidence of the two numbers is exactly the kind of thing that gets one deleted in favour
    /// of the other.</para>
    /// </summary>
    private float _slotCardInset = Defaults.SlotCardInset;

    /// <summary>
    /// Whether THIS OWNER lets the GAME's own card plume play on their cards (wire id
    /// <see cref="NetProtocol.TuneGameCardParticlesOn"/>, <c>[Cards] GameCardParticles</c>) — the
    /// round-slot twin of <c>RemoteHandFan</c>'s field of the same name, under the identical rule.
    ///
    /// <para>The VIEWER's own copy of this dial is NOT consulted here and must never be: theirs is
    /// answered by <c>Compat.CardParticlesOff</c>, which pins the game's low-spec switch so their
    /// OWN cards spawn no plume. ANDing the two is exactly the defect that left the mirrored card
    /// DUST inert for seven builds — see <c>Cards.CardDustFx.Permission</c>. A viewer who wants
    /// none of this switches the peer's board off wholesale ([Net] RemoteBoards).</para>
    /// </summary>
    private bool _gameCardParticlesOn = Defaults.GameCardParticles;

    /// <summary>One-shot latch for the "plume path is ARMED" line — see
    /// <see cref="TickSlotPlumes"/> for what its presence without a spawn line proves.</summary>
    private bool _plumeArmedLogged;

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
            // The ACTIVE-card matrix (item 8b's flight destination). THE SAME EXPRESSION the mirror
            // seats its column at — RemoteActiveCards' root is `layout.ActiveMount` verbatim — so
            // the flight lands exactly where the card is then drawn, by construction rather than by
            // two formulas that have to agree. The matrix is centred on its mount, so the mount IS
            // the block's midpoint and a one-card column lands dead on its own cell.
            CardFxAnchor.Active => layout.ActiveMount,
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
    /// <summary>
    /// Which round recess is drawing the REAL FACE of <paramref name="cardInstanceId"/>, or -1.
    /// See <c>RemoteAvatar.RecessShowingCard</c> for why a burn needs to know.
    /// </summary>
    internal int RecessShowingCard(int cardInstanceId)
    {
        if (cardInstanceId == int.MinValue)
            return -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (_cards[i] != null && _cards[i].ShownFaceCardInstanceId == cardInstanceId)
                return i;
        }
        return -1;
    }

    /// <summary>Transfer the single visible card to the burn presentation immediately. A covered
    /// sacrifice is not a ShownFaceCardInstanceId, so the former face-only exclusion left its
    /// back underneath the new burn front until the next occupancy refresh (report 5b).</summary>
    internal void SuppressBurnRecess(int recess)
    {
        if (recess < 0 || recess >= SlotCount)
            return;
        _cards[recess]?.Blank();
        _slotFaceMask &= ~(1 << recess);
        _slotPickBackMask &= ~(1 << recess);
        _slotPickSeatMask &= ~(1 << recess);
        _slotAnonMask &= ~(1 << recess);
    }

    /// <summary>
    /// WHICH RECESS A BURNING CARD IS LYING IN, for a mirror that has to draw the burn SOMEWHERE
    /// (<c>Net.RemoteBurnFx</c>) -- 0, 1, or -1 when no fact on this client places it.
    ///
    /// <para>WHY <see cref="RecessShowingCard"/> ALONE WAS NOT ENOUGH, measured. It answers "which
    /// recess is drawing this card's FACE", which conflates a POSITION question with an IDENTITY
    /// one. In the ModBuild 461 host log exactly two burns were mirrored and it answered -1 for
    /// ONE of them -- <c>BURN CARD [peer 2] ... 'ABILITY_CARD_WardingStrength' ... at their board
    /// CENTRE</c> -- while the board-content line beside it read
    /// <c>round-card faces=anon-back/empty, slot-occupancy=0x1</c>. The recess was occupied and
    /// this client simply could not NAME the card in it, so a burn whose face it could name
    /// perfectly well (that same line reads <c>face=REAL</c>) was drawn at the board centre for the
    /// whole 2 s hold. That is report item 7 verbatim: "eine kurze Zeit eine kleine mini Karte in
    /// der Mitte des boards ... statt an der Stelle wo die Karte war". A fallback its author
    /// believed was for "a burn nobody could place" fired on HALF the burns in the round.</para>
    ///
    /// <para>THE FOUR ANSWERS, STRONGEST FIRST, and none of them is a guess between two:</para>
    /// <list type="number">
    /// <item><description>the recess DRAWING that card's face -- an identification;</description></item>
    /// <item><description>the recess whose departed / already-claimed memory holds that card -- the
    /// card left that seat within the last <see cref="DepartedFaceSeconds"/> seconds, which is a
    /// fact this class recorded itself;</description></item>
    /// <item><description>the recess that was drawing that card as the SHORT-REST SACRIFICE within
    /// the same window (<see cref="_sacrificeSeatId"/>). It needs its own arm because the sacrifice
    /// branch deliberately latches no face, and the departure memory above is a copy of that latch —
    /// so the one card guaranteed to burn out of a recess was invisible to answers 1 and 2 the
    /// moment the short rest finalised. That is the 2026-09-06 late report's item 9, second clause,
    /// and the ModBuild 462 host log's burn #5;</description></item>
    /// <item><description>EXACTLY ONE occupied recess and no other claim on it. One candidate is
    /// not a choice -- the same sentence <see cref="TryTakeDepartedFace"/> is built on. The owner
    /// holds the burning card on his board while the game's burn artwork runs on it, so while one
    /// recess is occupied and a burn is being presented, that recess is where his card is.
    /// </description></item>
    /// </list>
    ///
    /// <para>TWO occupied recesses with no identification answers -1 and the caller falls back to
    /// the board centre, deliberately: naming one of two would be a coin flip. NOTE THE ASYMMETRY
    /// WITH A FACE -- this method answers a POSITION. Getting it wrong costs the viewer a card
    /// lifting from the neighbouring recess ~60 mm away; getting a FACE wrong costs him a card
    /// identity he cannot tell is wrong, which is why the face path refuses where this one
    /// infers.</para>
    /// </summary>
    internal int RecessOfBurningCard(int cardInstanceId, out string how)
    {
        how = "no recess on this client places that card";
        if (cardInstanceId == int.MinValue)
            return -1;
        int drawn = RecessShowingCard(cardInstanceId);
        if (drawn >= 0)
        {
            how = $"recess {drawn + 1} is DRAWING that card's own face right now";
            return drawn;
        }
        float now = Time.unscaledTime;
        for (int i = 0; i < SlotCount; i++)
        {
            bool remembered = _departedFace[i] != null
                              && _departedFace[i]!.CardInstanceID == cardInstanceId
                              && now - _departedAt[i] <= DepartedFaceSeconds;
            bool claimed = _claimedFaceId[i] == cardInstanceId
                           && now - _claimedAt[i] <= DepartedFaceSeconds;
            if (!remembered && !claimed)
                continue;
            how = $"recess {i + 1} was drawing that card and emptied within the last "
                  + $"{DepartedFaceSeconds:F0}s";
            return i;
        }
        // ─── AND THE SHORT-REST SACRIFICE, WHICH THE MEMORY ABOVE IS BLIND TO BY CONSTRUCTION ────
        // See _sacrificeSeatId. The sacrifice recess never latches a FACE, so it never stamps a
        // departure either, so the one card in the game that is guaranteed to burn out of a recess
        // was the one the strongest three answers could say nothing about. Same horizon, same kind
        // of claim (a POSITION), asked here because a card that is STILL seated is answered by the
        // drawing test at the top and this arm is for the one that has just left.
        for (int i = 0; i < SlotCount; i++)
        {
            if (_sacrificeSeatId[i] != cardInstanceId
                || now - _sacrificeSeatAt[i] > DepartedFaceSeconds)
                continue;
            how = $"recess {i + 1} was drawing that card as the short-rest SACRIFICE within the "
                  + $"last {DepartedFaceSeconds:F0}s (a position memory; the sacrifice branch never "
                  + "latches a face and so never stamps a departure)";
            return i;
        }
        int occupied = _slotOccupiedMask & ((1 << SlotCount) - 1);
        if (occupied == 1 || occupied == 2)
        {
            int only = occupied == 1 ? 0 : 1;
            how = $"recess {only + 1} is the ONLY occupied one on the owner's board and no recess "
                  + "names the card, so that is where his burning card is lying (one candidate is "
                  + "not a choice); it is a POSITION inferred from the occupancy nibble, never an "
                  + "identity";
            return only;
        }
        how = occupied == 0
            ? "the owner's board reports NO occupied recess, so his card has already left it"
            : "BOTH of the owner's recesses are occupied and none of them names this card, so "
              + "picking one would be a coin flip";
        return -1;
    }

    internal Vector3 AnchorLocalLive(CardFxAnchor anchor)
    {
        // THE TWO SLOTS RETURN THE CARD'S SEAT, not the bare recess anchor (2026-08-27). This is a
        // FLIGHT DESTINATION (RemoteCardFx) and a browse origin, so it has to be the point the card
        // actually renders at, or the slab jumps the moment the flight hands over to the seated
        // mirror. It used to be the anchor plus a flat -0.003 literal, which WAS the seat then; the
        // seat is the owner's dial now and both ends read one accessor, in BOTH branches, so they
        // cannot drift apart again. The glows do NOT come through here any more — they take
        // SlotAnchorBoardLocal, followed by each native glow depth in the furniture.
        if (anchor == CardFxAnchor.Slot0 || anchor == CardFxAnchor.Slot1)
        {
            int slot = anchor == CardFxAnchor.Slot0 ? 0 : 1;
            return SlotAnchorBoardLocal(slot) + SlotCardSeatLocal(slot);
        }
        return AnchorLocal(anchor, _layout);
    }

    /// <summary>
    /// WORLD position of one board anchor ON THE BOARD AS IT IS ACTUALLY DRAWN — the EASED root
    /// this class lerps in <c>Tick</c>, not the raw wire pose the packet carried.
    ///
    /// <para>EVERY board-anchored card FX composed its own world point as
    /// <c>_owner.BoardPosition + _owner.BoardRotation * (BoardAnchorLocal(a) * _owner.BoardScale)</c>,
    /// and those three fields are the TARGET of the lerp a few lines above, not the root. The root
    /// trails them at <c>NetProtocol.InterpolationSharpness</c> — about 73 ms — so while a peer
    /// CARRIES or ZOOMS their board, which is exactly when the sender raises extras to 15 Hz, the
    /// flying cards detached from the board and travelled beside it; during a zoom the same split
    /// hit their SIZE, because <c>_easedScale</c> trails <c>BoardScale</c> the same way. The FX
    /// slabs are deliberately NOT parented to this root (they must outlive a board rebuild and a
    /// blank), so nothing compensated and nothing could have.</para>
    ///
    /// <para><c>TransformPoint</c> already applies the root's <c>localScale</c>, which IS
    /// <c>_easedScale</c>, so a caller must NOT multiply by <c>BoardScale</c> again — that is the
    /// one way to misuse this and it is the reason the raw composition is not simply patched in
    /// place at each site.</para>
    ///
    /// <para>False before the first pose has landed (<c>_poseInit</c>) or while the root is gone.
    /// The caller then keeps its old raw-pose composition, which is what every build before this
    /// one did everywhere, so a degraded answer is never worse than the previous behaviour.</para>
    /// </summary>
    internal bool TryAnchorWorld(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (_root == null || !_poseInit)
            return false;
        world = _root.transform.TransformPoint(AnchorLocalLive(anchor));
        return true;
    }

    /// <summary>The board's LIVE DRAWN pose and uniform scale — the eased root again, for the
    /// same reason as <see cref="TryAnchorWorld"/>. A flight slab is not parented to the root, so
    /// it has to READ these; a slab that took the raw wire rotation while the board it is lying on
    /// turned under an eased one is the rotational half of the same defect.</summary>
    internal bool TryDrawnBoardPose(out Vector3 pos, out Quaternion rot, out float scale)
    {
        pos = default;
        rot = Quaternion.identity;
        scale = 1f;
        if (_root == null || !_poseInit)
            return false;
        Transform rt = _root.transform;
        pos = rt.position;
        rot = rt.rotation;
        scale = _easedScale;
        return true;
    }

    /// <summary>
    /// BOARD-LOCAL seat of the ACTIVE-matrix cell that will draw <paramref name="cardInstanceId"/>,
    /// or false when this board's column is not drawing that card. Same shape and same reason as
    /// <see cref="AnchorLocalLive"/>'s two-slot branch — "this is a FLIGHT DESTINATION … so it has
    /// to be the point the card actually renders at, or the slab jumps the moment the flight hands
    /// over to the seated mirror" — and the active matrix is the one destination where that was
    /// still false. See <c>RemoteActiveCards.TryCellBoardLocal</c>, which owns the expression.
    /// </summary>
    internal bool TryActiveCellLocal(int cardInstanceId, out Vector3 boardLocal)
    {
        boardLocal = default;
        return _active != null && _active.TryCellBoardLocal(cardInstanceId, out boardLocal);
    }

    /// <summary>
    /// Board-local position of slot <paramref name="slot"/>'s recess ANCHOR — the REAL prefab point
    /// once the 3D asset is up, else the authored flat-board layout. The BARE anchor: no proud lift
    /// on it, no seat, no overlay term. <see cref="SlotCardSeatLocal"/> adds what the card needs and
    /// the furniture adds each native glow depth independently.
    /// </summary>
    private Vector3 SlotAnchorBoardLocal(int slot) =>
        _tray != null ? _tray.SlotLocal(slot) : SlotLocal(slot);

    /// <summary>
    /// THE CARD SEAT: where a played card rests relative to its recess ANCHOR, in BOARD-local
    /// metres — the mirror of the owner's <c>PlayTray.SlotHomeOffsetFor(slot)</c>, term for term.
    ///
    /// <para>The owner parks a card at slot-local <c>(ov.x +/- spacing/2, ov.y, -SlotCardInset +
    /// ov.z)</c> UNDER the slot transform, and that transform carries <c>PlayTray.SlotScale</c>
    /// (1.3), so all three terms reach the board frame multiplied by 1.3.
    /// <see cref="RemoteBoardFurniture.SlotOverlayLocal"/> already performs exactly that conversion
    /// for the IN-PLANE half and is shared with the glows, so this composes on top of it rather than
    /// restating it.</para>
    ///
    /// <para>WHY THE DEPTH IS ADDED HERE AND NOT RESTORED INSIDE <c>SlotOverlayLocal</c>: that
    /// accessor has two consumer families and they mirror two DIFFERENT owner-side z bases. A card
    /// seats at <c>-SlotCardInset + ov.z</c>; the owner's glows sit at
    /// <c>SlotGlowBaseZ / WantedGlowBaseZ + ov.z</c> (-0.006 / -0.004, PlayTray.6.Build.cs) — not the
    /// same number, and not a dial at all. Folding the seat depth into the shared expression would
    /// silently drag both glows onto the card's plane and cost the gold-in-front-of-teal ordering
    /// those two base constants exist to give. One expression per FRAME, not per formula.</para>
    ///
    /// <para>The result is BOARD-local throughout. A caller hanging it off the board ROOT adds it
    /// directly; the one that parents to a raw prefab recess anchor converts the WHOLE vector — z
    /// together with x and y — through the board root, because those anchors are not scaled by
    /// SlotScale and a z added after that conversion is a scale error no screenshot can show.</para>
    /// </summary>
    private Vector3 SlotCardSeatLocal(int slot)
    {
        // In-plane half: the owner's SlotOverlayOffset.xy + their pair spread, already xSlotScale.
        // Its z is 0 by that accessor's contract — see its doc for why it stays that way.
        Vector3 seat = RemoteBoardFurniture.SlotOverlayLocal(_owner.BoardTuning, slot);
        seat.z = (-_slotCardInset + _owner.BoardTuning.SlotOverlayOffset.z) * PlayTray.SlotScale;
        return seat;
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

    /// <summary>Idle recovery deadline only. Actual received/model/native content changes are
    /// consumed immediately; an observer preference may not hold visible state behind this timer.</summary>
    private float _nextRefreshAt;
    private uint _contentPresenceRevision;
    private ulong _contentNativeRevision;
    private ulong _contentModelRevision;

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

    /// <summary>Recesses drawing a card that <c>RoundAbilityCards</c> cannot name but extension
    /// record 39 CAN — a short-rest sacrifice or a modal pick's card laid on the board. A subset of
    /// <see cref="_slotFaceMask"/>; its census population is
    /// <c>PeerCardFaceCensus.Surface.BoardPickSeat</c>.</summary>
    private int _slotPickSeatMask;

    /// <summary>Which recesses hold a record-39 card this client HAS resolved and is lawfully
    /// drawing FACE-DOWN (bit per slot) — the short rest inside the selection phase, 2026-09-07
    /// item 6. Kept apart from <see cref="_slotAnonMask"/> because the two are different readings
    /// with different fixes: an anonymous back means nothing could NAME the card, this one means the
    /// card is named and the phase says cover it, and folding them together is how a lawful back
    /// would read as a broken wire for the rest of the project.</summary>
    private int _slotPickBackMask;

    /// <summary>What decided this frame's <see cref="_slotPickSeatMask"/> / <see cref="_slotAnonMask"/>
    /// split, in the short form the census quotes verbatim. Set every frame in
    /// <see cref="SeatSlots"/> so it can never describe an older frame's picture.</summary>
    private string _pickSeatRule = "no recess needed a record-39 seat this frame";

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

            // ZERO IS A READING (see PeerCardFaceCensus). A board that is not being drawn must say
            // so, or its last front/back split would stand in every census line for the rest of the
            // session — and "the board was hidden" and "the slots went to backs" are two different
            // answers to the user's question.
            PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.RoundSlots, _owner.PlayerId, 0, 0,
                "this peer's board is not being drawn (RemoteBoardGate)");
            PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.ActiveMatrix, _owner.PlayerId, 0, 0,
                "this peer's board is not being drawn (RemoteBoardGate)");
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
        // …and WHY a null is a null. `exhausted` separates the JOIN-TIME null (no character
        // assigned yet — the case every clause below was written for) from the peer whose
        // character has been KILLED. The two want opposite things from the wire-fed halves: a
        // joining peer's recesses, stacks and cue are knowable and must be drawn; an exhausted
        // one's must not exist at all (user 2026-09-05 #13). See ApplyExhaustedCardRule.
        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out bool viaFocus,
            out bool exhausted);
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

            // OUTSIDE A SCENARIO the board may not merely be hidden: it may not exist
            // (RemoteBoardScenarioGate — user ruling, ModBuild 232: "In der 3D-Map-Umgebung ist kein
            // board sichtbar von keinem Mitspieler und darf für niemanden sichtbar sein"). Tear it
            // down so its renderer set, its mirrored clones and its MrBacking registrations do not
            // ride through the 3D map room deactivated. INSIDE a scenario keep the deactivate: the
            // reveal gate flips several times a round and a rebuild there would cost a frame at the
            // worst possible moment. Destroy() resets _poseInit and every built-at latch, so the
            // re-entry rebuild snaps to the peer's current pose and layout rather than easing in
            // from the origin.
            if (!RemoteBoardScenarioGate.Open)
                Destroy();
            else
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
        // THE POPULATION's reveal decision for this peer's played cards, taken ONCE per frame here
        // and passed down: showFronts is RevealGate.ShowRoundCardFronts(actor) verbatim — false for
        // a remote actor while the game is in its own secret SelectAbilityCardsOrLongRest phase,
        // true once the selection is locked in and the characters are acting (and always true
        // offline / for our own actor / off-scenario).
        //
        // IT IS NOT THE LAST WORD ON ANY PARTICULAR CARD, AND SAYING SO HERE IS THE POINT. Every
        // recess re-asks RevealGate.CardFaces with the CARD in hand, and the burn exception
        // (RevealGate.IsPubliclyRevealedCard) can open a front this bool refuses — that is the
        // user's ruling, "Beim Verbrennen EGAL AUS WELCHEM GRUND". This value is the population's
        // answer and the input to that call; _slotFaceMask, which SeatSlots writes, is what was
        // actually drawn. The slot only ever CREATES a face object inside its front branch, so the
        // fronts cannot exist a frame early. The actor is handed through purely so the slot can find
        // that player's own card widget to clone — it is never written to.
        // EXHAUSTED: the recesses are forced empty regardless of the owner's occupancy nibble —
        // that mask is the last one they sent while alive, and a latched wire fact is exactly how
        // a "cleared" board keeps two card backs (see ApplyExhaustedCardRule).
        SeatSlots(actor, showFronts, exhausted);
        NoteMirroredSlotMask();

        // THE STANDING PICTURE for the per-population census (see Net/PeerCardFaceCensus): how many
        // of this peer's round-card recesses are showing a real front and how many a back, every
        // frame, beside the change-gated lines the slots keep of their own. _slotFaceMask is the
        // slots that carry a face; the occupied slots that do not are backs, whether identity-known
        // or anonymous.
        // …and the anonymous-recess line re-arms once no recess is anonymous any more, so a SECOND
        // short rest prints a second line instead of being swallowed by the first one's latch.
        if (_slotAnonMask == 0)
            _loggedAnonSlot = -1;

        // ─── A POPULATION MAY NOT COUNT A FACE ANOTHER POPULATION'S RULE CHOSE ─────────────────
        // The ModBuild 470 host log read `round slots[p2] 1 FRONT / 0 BACK —
        // RevealGate.ShowRoundCardFronts(actor)=false`: a front, printed beside the sentence saying
        // fronts were forbidden. Nothing lied. The face belonged to the recess's record-39 card,
        // which is reported below as its OWN population under its OWN rule, and _slotFaceMask
        // carries both — so the one card was counted twice and attributed to the rule that did not
        // choose it. A rule string is only evidence while its numerator is the set of cards that
        // rule actually decided, so the record-39 recesses are subtracted from BOTH terms here.
        int pickMask = _slotPickSeatMask | _slotPickBackMask;
        int seatedSlots = CountBits(_slotOccupiedMask & ~pickMask);
        int facedSlots = CountBits(_slotFaceMask & ~pickMask);
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.RoundSlots, _owner.PlayerId,
            facedSlots, seatedSlots - facedSlots,
            showFronts
                ? "RevealGate.ShowRoundCardFronts(actor)=true — a seated slot with no face is one "
                  + "whose card widget could not be resolved on this client"
                : "RevealGate.ShowRoundCardFronts(actor)=false — the game's own secret "
                  + "SelectAbilityCardsOrLongRest window, or no character resolved");

        // THE CARD LYING ON THE BOARD THAT RoundAbilityCards CANNOT NAME (report item 7's second
        // half, and report item 15's). Reported as its OWN population rather than folded into the
        // round slots above: this one is carved OUT of the phase, so a BACK here is a defect in
        // EVERY phase, and the rule beside it names which half — a seat the owner never sent, or a
        // seat this client could not resolve.
        int pickFronts = CountBits(_slotPickSeatMask);
        // A BACK HERE IS NOW TWO DIFFERENT READINGS AND _pickSeatRule TELLS THEM APART: a recess
        // nothing could NAME (_slotAnonMask — a wire or arithmetic defect) and a recess this client
        // named and is lawfully covering (_slotPickBackMask — a short rest inside the selection
        // phase, 2026-09-07 item 6, which is correct and must not be read as a defect).
        int pickBacks = CountBits(_slotAnonMask | _slotPickBackMask);
        if (pickFronts > 0 || pickBacks > 0)
            PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.BoardPickSeat, _owner.PlayerId,
                pickFronts, pickBacks, _pickSeatRule);

        // …and the GAME's OWN card plume on those very slabs (wire id 236) — the PLAYED/round-slot
        // half of the bit whose HAND half RemoteHandFan.TickMirroredPlumes already pays. PER FRAME,
        // beside the half glow and for the identical reason: the trigger is an EDGE on a live game
        // state, and the 4 Hz content cadence would sample straight past a two-second burn's start.
        // It runs AFTER SeatSlots because it reads what that pass seated — the card, its owner and
        // whether a real face is up — rather than resolving any of it a second time.
        // Owner stream 51 drives the central plume dispatcher.

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

            // WHICH HALF IS ALREADY USED (record 41, report item 8: "welche der beiden Karten
            // bereits benutzt/verbrannt wurde"). Driven here, in the same per-frame loop as the
            // hover/click glow and for the same reason — it describes the same two recesses and
            // must land with the synced edge rather than on the 4 Hz content cadence. Unlike the
            // glow it is meaningful ONLY on a slot showing a real face: a card BACK has no halves
            // to grey, and the slot renderer refuses it there rather than inventing a stand-in.
            _cards[s]?.SetSpentHalves(_owner.PlayerId, s,
                                      _owner.RoundHalfIsSpent(s, top: true),
                                      _owner.RoundHalfIsSpent(s, top: false));
        }
        LogHalfHoverIfChanged();

        // THE OWNER'S POINTER ON THEIR DECISION BUTTONS (record 24 bits 3-4) — driven here, beside
        // the half hover above and for the identical reason: a hover that only repainted on the
        // 4 Hz content cadence would land on options the owner never stopped on. The call is a
        // gate; it returns without touching a Graphic on every frame the owner's pointer has not
        // moved, which is nearly all of them.
        _furniture?.TickDecisionPointer(_owner);

        _tag!.Tick();

        // Character-focus turn cue: green while this peer owns the character at turn AND is
        // looking at it, red while they own it but are looking elsewhere, nothing otherwise.
        // ALL THREE TERMS COME FROM THE WIRE: CharacterFocus.MarkForPeer reads only Peers[playerId]
        // — ActorId, OwnsAttention and AttentionId, all off record 22 — and touches no local read
        // of who is at turn. (This comment used to claim the mark was "re-derived locally every
        // frame from THIS client's read of who is at turn"; the block comment on the focus outline
        // twenty lines below always said the opposite, and it is the one the code matches.)
        _focusOutline?.Tick(visible: true, _tag.AvatarQuad, OwnerTag.AvatarQuadSize);

        // Content (objectives / elements / round / initiative / rest / pile counts / active cards)
        // on the shared cadence. Before the actor exists only the GLOBAL panels refresh (they are
        // bit-identical on every client); the per-actor surfaces stay blank until the host assigns
        // the character.
        //
        // THIS COMMENT USED TO READ "everything below is a MODEL read, not a wire read", AND THAT
        // SENTENCE WAS THE DEFECT. It was true of the objectives, the elements, the initiative
        // track and the active-card column, and it is why the 250 ms gate looked safe — but the
        // furniture refresh reached from here (RemoteBoardFurniture.Refresh) was almost entirely
        // WIRE reads: the owner's live button visibility, their cap states, their cap PRESS, their
        // cap wordings, their decision option states, their use-bar slot states, the FOLLOW/PIN
        // toggle, the wanted glow and the snap-hover rim. Every one of those landed up to 250 ms
        // after the owner saw it, and anything briefer than the gate period — a quick press, a
        // pointer crossing an option — fell between two samples and was never drawn at all. The
        // SENDER had already ruled the other way: NetAvatarDriver pre-empts its own 5 Hz extras
        // gate OUTRIGHT for those records so they land on the peer's next FRAME, and the receiver
        // was spending that guarantee in a queue.
        //
        // The wire half now runs per frame in _furniture.TickWire, immediately after this block
        // (see below). What is still reached from HERE is genuinely cadence work: model reads, and
        // the furniture's own structural half — row rebuilds, a cloned-widget mirror walk, a
        // localized string composition, a TMP re-measure and a walk over this client's live use-bar
        // children. Read RemoteBoardFurniture.Refresh / TickWire for the itemised split.
        ulong nativeRevision = NativeContentRevision();
        ulong modelRevision = ModelContentRevision(actor, showFronts);
        if (_owner.PresenceRevision != _contentPresenceRevision
            || nativeRevision != _contentNativeRevision || modelRevision != _contentModelRevision
            || Time.unscaledTime >= _nextRefreshAt)
        {
            _contentPresenceRevision = _owner.PresenceRevision;
            _contentNativeRevision = nativeRevision;
            _contentModelRevision = modelRevision;
            _nextRefreshAt = Time.unscaledTime + RemoteBoardContent.DefaultRefreshSeconds;
            if (actor != null)
                RefreshContent(actor, showFronts);
            else
                RefreshGlobalContent();
            // A DEAD BOARD HAS NO CARDS. Runs AFTER the refresh on purpose: that pass is what
            // re-paints the wire-fed pile counts and the item cue, so clearing before it would be
            // undone in the same tick. Called UNCONDITIONALLY and both ways, so the rule is
            // reversible by construction (scenario restart, a peer swapping character) rather than
            // a one-way hide nothing ever undoes. The global panels the refresh also drives
            // (objectives, elements, initiative track, round readout) are scenario-wide state and
            // stay: this takes the CARDS, not the board.
            ApplyExhaustedCardRule(exhausted);
            // This pass is what CREATES new surfaces on the board (a card face, a pile front, a
            // readout label). Re-arm the draw-order sweep so it runs at the END of THIS tick
            // rather than up to a cadence later: two independent timers of the same period can be
            // a whole period out of phase, and that phase would be exactly how long a new surface
            // spends at its creation order instead of its board's cluster slot.
            _nextOrderSweepAt = 0f;
        }

        // THE FURNITURE'S WIRE HALF, PER FRAME — the owner's button mask, cap states, cap PRESS,
        // cap wordings, decision option states (hover/press included), use-bar slot states,
        // FOLLOW/PIN, the item-USE cap, the wanted glow and the snap-hover rim. Beside
        // TickDecisionPointer above and for the identical reason it was lifted first: these are
        // EDGES on live state, and a 4 Hz sample is longer than the events last.
        //
        // DELIBERATELY AFTER the cadence block rather than before it. On a cadence frame the
        // structural pass runs first and this pass then paints what it just built in the SAME
        // frame — a row that Refresh rebuilt (which nulls its applied-state gates) never renders
        // one frame in its unpainted default look, and the use-bar drawer is never seated against
        // a decision-row height that has already moved.
        //
        // COST ON THE UNCHANGED PATH: ~60 comparisons, zero allocations, zero GetComponent, zero
        // scene query — the arithmetic is in TickWire's own doc. At a four-player table this runs
        // for the three remote peers: 3 x 90 Hz x ~60 compares is under 0.01 ms of the 11.11 ms
        // budget. The expensive halves (row rebuilds, the widget-mirror walk, the use-bar symbol
        // resolve) stay on the 4 Hz cadence above, where they always were.
        _furniture?.TickWire(_owner, _slotOccupiedMask);

        // THE BOARD TOOLTIP, PER FRAME — beside the furniture's wire half, after the cadence block,
        // and for the identical reason both of those were lifted.
        TickBoardTooltip();

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
            // THE ELEMENT BOARD JOINED THEM (2026-09-06). It has to be a per-FRAME drive and not a
            // cadence one: the "wird erstellt" cell is a GUIAnimator animation the game can start
            // and finish inside one 250 ms content period, so a strip sampled at 4 Hz can miss the
            // whole event — which is the "manchmal komplett weg" half of the user's report.
            _elements?.TickLive();
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
            _nextOrderSweepAt = Time.unscaledTime + RemoteBoardContent.DefaultRefreshSeconds;
            BoardVisual.BoardOrderSweep sweep =
                BoardVisual.AdoptBoardOrder(this, _root.transform, _tag?.Root);
            // One line per REAL change of what this board carries (the split is the gate), never
            // per sweep. Read together with the ladder's own FURNITURE ORDER line, it states the
            // absolute draw order of every surface on this peer's board.
            if (sweep.Signature != _loggedOrderSweep)
            {
                _loggedOrderSweep = sweep.Signature;
                // The old text of this line claimed "a proud dock (the initiative mirror) always
                // covers a shallower one (the synced tooltip)". Its OWN numbers falsified it in
                // every ModBuild-461 sweep: `2:0 (docks) 3:0 (deep dock)` on all 15 lines, because
                // Defaults.InitiativeOffset_{Oak,Steel,Bronze} all ship z = -0.009 today, which
                // TierForDepth rounds to tier 0, while the tooltip's -0.02 is tier 1. The dock has
                // been UNDER the hint on every board since those offsets converged, i.e. the exact
                // relation of user report #5 of 2026-08-09; only the initiative popup was lifted
                // out of the tie (BoardVisual.PopupOverlayTier) and the track/hint inversion is
                // still standing. The line now states the tiers it actually produced.
                VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] draw-order cluster: {sweep}. " +
                                  "The whole board ranks against the converted-panel ladder as one " +
                                  "unit; inside it the tier is the element's own board-local depth " +
                                  "(BoardVisual.TierForDepth, 2 cm per tier), EXCEPT a popup an " +
                                  "overrideSorting canvas asked to sort for itself, which takes the " +
                                  "cluster's top tier " +
                                  $"({BoardVisual.PopupOverlayTier}) so it covers this board's whole " +
                                  "face the way a local board-docked panel covers that board's " +
                                  "furniture. A tier-3 count of 0 while an enemy-info popup is up " +
                                  "means the lift did not reach it.");
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

    /// <summary>
    /// THE PEER'S BOARD TOOLTIP (extension record 9), APPLIED ON THE FRAME THE VALUE CHANGES.
    ///
    /// <para>IT USED TO SIT INSIDE THE 4 Hz CONTENT CADENCE, IN BOTH REFRESH PATHS, AND THE SENDER
    /// HAD ALREADY RULED THE OTHER WAY. <c>NetAvatarDriver</c> samples <c>WorldTooltips.WireText</c>
    /// BEFORE its own extras rate gate and lists <c>tooltipChanged</c> among the terms that PRE-EMPT
    /// that gate outright, with its own comment saying so — "so its appearance/disappearance/
    /// text-change edges pre-empt it". The owner therefore pays to get the edge onto the wire on the
    /// next frame, and this receiver put it back in a queue for up to 250 ms. A hover shorter than
    /// the gate period appeared on the owner's board and NEVER on the mirror at all: the value went
    /// up and back down between two samples. One end of a pair buying responsiveness the other end
    /// spends is not a tuning choice; it is half a decision.</para>
    ///
    /// <para>COST ON THE UNCHANGED PATH, MEASURED FROM THE SOURCE RATHER THAN ASSUMED — because
    /// "cheap" per-frame calls have owned the frame in this project before.
    /// <c>RemoteBoardTooltip.Apply</c> with an unchanged non-empty string is: one
    /// <c>string.IsNullOrEmpty</c>, one static bool (<c>GameSkin.TryEnsure</c> returns on
    /// <c>_sampled</c> before it touches the scene), one ordinal string compare over a line bounded
    /// by <c>NetProtocol</c>'s own byte cap, then a return. No allocation, no
    /// <c>GetComponent</c>, no scene query, no layout. With the record absent it is one length test.
    /// The only path that touches the scene is the skin SAMPLE, which has not happened yet and which
    /// carries its own 15-frame retry gate, so at 90 Hz it attempts at ~6 Hz instead of 4 Hz and
    /// then never again for the session.</para>
    ///
    /// <para>THE PICK BANNER (record 7) DELIBERATELY STAYS ON THE CADENCE, and that is the same
    /// rule applied rather than an inconsistency: its sender does NOT pre-empt. <c>bannerNow</c> is
    /// sampled well AFTER <c>NetAvatarDriver</c>'s rate gate, so the edge is already on the owner's
    /// 5 Hz extras cadence before it leaves that machine and there is nothing here for a per-frame
    /// apply to recover. (The tooltip's own sender comment claims it pre-empts "like the pick
    /// banner's do" — that half of the sentence is false, and the file it is in is not this lane's
    /// to edit; it is reported instead.) Moving this end alone would be exactly the defect this
    /// method fixes, in the other direction.</para>
    ///
    /// <para>AFTER the cadence block, never before: a tooltip coming UP re-measures the board's
    /// corner (<c>RemoteBoardTooltip.Reseat</c>) and that walk must see the geometry this frame's
    /// structural pass has already written, not the previous tick's.</para>
    /// </summary>
    private void TickBoardTooltip() => _boardTooltip?.Apply(_owner.TooltipText);

    private static ulong NativeContentRevision()
    {
        ulong hash = 14695981039346656037UL;
        UIManager? ui = UIManager.Instance;
        MixRevision(ref hash, RemoteBoardContent.NativeRevision(ui != null && ui.MissionObjectiveContainer != null
            ? ui.MissionObjectiveContainer.transform : null));
        MixRevision(ref hash, RemoteBoardContent.NativeRevision(ui != null && ui.ScenarioModifierContainer != null
            ? ui.ScenarioModifierContainer.transform : null));
        InitiativeTrack? track = InitiativeTrack.Instance;
        MixRevision(ref hash, RemoteBoardContent.NativeRevision(track != null ? track.transform : null));
        InfusionBoardUI? elements = InfusionBoardUI.Instance;
        MixRevision(ref hash, RemoteBoardContent.NativeRevision(elements != null ? elements.transform : null));
        return hash;
    }

    private static void MixRevision(ref ulong hash, ulong revision)
    {
        RemoteBoardContent.Mix(ref hash, (int)revision);
        RemoteBoardContent.Mix(ref hash, (int)(revision >> 32));
    }

    private static ulong ModelContentRevision(CPlayerActor? actor, bool showFronts)
    {
        ulong hash = 14695981039346656037UL;
        RemoteBoardContent.Mix(ref hash, CardsGameApi.RoundNumber());
        RemoteBoardContent.Mix(ref hash, (int)PhaseManager.PhaseType);
        RemoteBoardContent.Mix(ref hash, showFronts ? 1 : 0);
        for (int i = 0; i < 6; i++)
        {
            int column;
            try { column = (int)ElementInfusionBoardManager.ElementColumn((ElementInfusionBoardManager.EElement)i); }
            catch { column = -1; } // no scenario model during join/teardown
            RemoteBoardContent.Mix(ref hash, column);
        }
        RemoteElementStrip.ReadOverlay(out int creating, out int reserved, out int available);
        RemoteBoardContent.Mix(ref hash, creating);
        RemoteBoardContent.Mix(ref hash, reserved);
        RemoteBoardContent.Mix(ref hash, available);
        if (actor == null) return hash;
        RemoteBoardContent.Mix(ref hash, NetFigures.StableActorId(actor));
        if (showFronts) RemoteBoardContent.Mix(ref hash, actor.Initiative());
        CCharacterClass? cards = actor.CharacterClass;
        if (cards != null)
        {
            RemoteBoardContent.Mix(ref hash, cards.DiscardedAbilityCards.Count);
            RemoteBoardContent.Mix(ref hash, cards.LostAbilityCards.Count);
            RemoteBoardContent.Mix(ref hash, cards.PermanentlyLostAbilityCards.Count);
            System.Collections.Generic.List<CBaseCard>? active = ActiveCardSet.ActivatedCards(cards);
            if (active != null)
                for (int i = 0; i < active.Count; i++)
                    if (active[i] is CAbilityCard ability)
                        RemoteBoardContent.Mix(ref hash, ability.CardInstanceID);
        }
        RemoteBoardContent.Mix(ref hash, actor.Inventory?.AllItems?.Count ?? 0);
        return hash;
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
            // The board TOOLTIP is NOT applied here any more — it runs per frame beside
            // _furniture.TickWire. See TickBoardTooltip.
            SyncInitiativeBadge();
            // The furniture's SYNCED half (board-UI record: buttons + wanted glow) is wire-fed and
            // must follow the owner's board with or without an actor. The slot flags used to be
            // hard false here because occupancy was a per-ACTOR read; since it rides the wire, an
            // actorless peer's recesses are knowable too, so the masks SeatSlots just resolved are
            // passed through unchanged and the join-time board gets its snap glow like any other.
            _furniture?.Refresh(null, _owner);
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
            // The board TOOLTIP is NOT applied here any more — it runs per frame beside
            // _furniture.TickWire. See TickBoardTooltip.
            _status?.Refresh(actor, showFronts);
            // NO showFronts HANDED DOWN (report item 2b): the active matrix asks the face rule
            // for its OWN population, which is exempt from the selection phase. See
            // RemoteActiveCards.Refresh and RevealGate.PeerCardPopulation.AlreadyPublic.
            _active?.Refresh(actor);
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
            _furniture?.Refresh(actor, _owner);

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

    /// <summary>The original initiative track owns this number. The former remote-only badge
    /// must never appear, including while its native mirror is recovering.</summary>
    private void SyncInitiativeBadge() => _status?.SetShownWhileTrackFallback(false);

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
        string slots = $"{FaceTag(0)}/{FaceTag(1)}";
        // The CHARACTER rides this line too: the pile counts below are that character's, and the
        // owner's board switches which one it presents whenever they focus a teammate. Without the
        // name, "die Zahlen auf seinem Brett stimmen nicht" ("the numbers on his board are
        // wrong") cannot be answered from a peer log.
        string line = $"Remote board content [{_owner.PlayerId}]: " +
                      $"char='{Board.CharacterFocus.Describe(actor)}', " +
                      $"round='{(_status != null ? _status.RoundText : "-")}', " +
                      $"initiative={(_status != null ? _status.InitiativeText : "?")}, " +
                      $"piles d/b/i={discard}/{burnt}/{items}" +
                      $"{(_owner.HasPileCounts ? "(synced)" : "(model)")}, " +
                      $"item-cue={(_owner.ItemsPileUsableCue ? "beating" : "off")}, " +
                      $"round-card faces={slots}, " +
                      $"slot-occupancy={(_owner.SlotOccupancyKnown ? "0x" + _owner.BoardSlotMask.ToString("X1") + "(synced)" : "model-only")}, " +
                      $"active={(_active != null ? _active.Count : 0)} card(s) " +
                      $"({(_active != null ? _active.RealFaceCount : 0)} real face(s)), " +
                      $"objectives={(_objectives != null ? _objectives.RowCount : 0)} row(s) " +
                      $"via {SectionTag(_objectives?.Source, _objectives?.Reason)}, " +
                      // The SPECIAL RULES ride the objectives panel and are counted separately:
                      // a scenario with goals and no rules and a scenario whose rules failed to
                      // mirror both draw goals only, and this census is the only place the two are
                      // told apart from one line. Zero rows with source None is the ordinary
                      // picture for a scenario that has none.
                      $"rules={(_objectives != null ? _objectives.RuleRowCount : 0)} row(s) " +
                      $"via {(_objectives != null ? _objectives.RulesSource : RemoteWidgetMirror.Fidelity.None)}, " +
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
                          "fronts gated per CARD by RevealGate.CardFaces, whose POPULATION term is " +
                          "the fronts=... bool above (RevealGate.ShowRoundCardFronts — false is " +
                          "exactly the game's secret SelectAbilityCardsOrLongRest phase for a remote " +
                          "actor) and whose CARD term is the burn exception " +
                          "(RevealGate.IsPubliclyRevealedCard). SO fronts=False DOES NOT MEAN BACKS " +
                          "ONLY: a card in that character's activated or lost lists still draws its " +
                          "front, which is the user's ruling and not a leak — read the per-recess " +
                          "'CARD FACE RULE' line for which rule chose each face. A round-card face " +
                          "of LiveWidget/PooledBorrow is the REAL " +
                          "game card at full detail; 'panel' is the mod-drawn name+initiative " +
                          "fallback; 'back' means both terms refused; 'anon-back' means the OWNER's " +
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
    /// up, otherwise what the slot is actually showing (mod panel when a front was permitted but
    /// neither source resolved; a card BACK when it was not; nothing at all when the slot is empty).
    /// Pure read of already-computed state — it re-derives no gate of its own.
    ///
    /// <para>IT READS <see cref="_slotFaceMask"/> AND NOT THE BOARD-WIDE <c>showFronts</c>, which is
    /// what it used to do: a recess drawing a BURN-EXCEPTION front whose clone failed was tagged
    /// "back" while the mod panel was on screen, because the population's verdict was false for a
    /// card the CARD's verdict had opened. The mask is what <see cref="SeatSlots"/> actually
    /// seated.</para></summary>
    private string FaceTag(int slot)
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
        return (_slotFaceMask & (1 << slot)) != 0 ? "panel" : "back";
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
    /// <summary>Which recess this board last reported as an anonymous back, so the line below fires
    /// on a real change and never per frame. -1 = none reported.</summary>
    private int _loggedAnonSlot = -1;

    /// <summary>
    /// NAME THE BLOCKER FOR THE SHORT-REST CARD (2026-09-05, user item 15: "Bei einer kurzen Rast
    /// soll es sichtbar sein welche Karte dort liegt — ich sehe nur die Rückseite").
    ///
    /// <para>WHY A MEASUREMENT AND NOT A FIX. This recess is OCCUPIED on the owner's board — the
    /// wire's occupancy nibble says so — and this client cannot name the card in it, so it draws an
    /// anonymous back. Note where that happens: ABOVE the <c>showFronts</c> branch, and completely
    /// independent of it. Opening <see cref="RevealGate"/> for this population would therefore
    /// change nothing at all, which is precisely the "gated remedy never ran" shape — a fix that
    /// passes its own reading and moves no picture. <c>RevealGate.PeerCardPopulation.SacrificedCard</c>
    /// carries the secrecy ruling and says the same thing from the other end.</para>
    ///
    /// <para>WHAT IS ACTUALLY MISSING is an IDENTITY on the wire, and this line is what proves it
    /// rather than asserting it: it prints the peer's live pile counts beside the verdict, so a
    /// short-rest sacrifice reads as "discard went down by one, burnt did not go up, and the recess
    /// filled" — a card in flight between two replicated lists, in neither of them, and never in
    /// <c>RoundAbilityCards</c>. That is a three-number proof that no receiver-side cleverness can
    /// close the gap.</para>
    /// </summary>
    /// <summary>Per recess: the card, the face and the RULE that chose it, as last logged — so the
    /// line below fires on a real change and never per frame. Keyed on all three, because the same
    /// card can legitimately change face (a short-rest sacrifice going from covered to burning) and
    /// the same face can legitimately change rule (the selection phase ending under a card that was
    /// already public).</summary>
    private readonly int[] _loggedFaceCard = { int.MinValue, int.MinValue };
    private readonly int[] _loggedFaceRule = { -1, -1 };
    private readonly bool[] _loggedFaceFront = new bool[SlotCount];

    /// <summary>
    /// WHICH FACE THIS RECESS IS SHOWING AND WHICH RULE CHOSE IT — one line per real change, so the
    /// next report of a wrong face is one grep instead of a round.
    ///
    /// <para>WHY IT IS PER CARD AND NOT PER SURFACE. <c>Net.PeerCardFaceCensus</c> already prints a
    /// rule per POPULATION every 10 s, and that is the granularity that failed: a population's rule
    /// string is true of the population and says nothing about a card whose face a DIFFERENT rule
    /// chose — the burn exception, which is a property of the card and fires inside a window the
    /// population-level rule has just declared shut. The ModBuild 470 host log carries exactly that
    /// reading (see the census attribution note in <see cref="Tick"/>). This line names the ruling
    /// for the one card in front of the user.</para>
    ///
    /// <para>IT IS A STATEMENT ABOUT WHAT WAS DRAWN, taken at the draw site with the value that was
    /// handed to <c>RemoteBoardCard.Set</c> — not a re-derivation, which is the shape that lets an
    /// instrument agree with a picture it never measured.</para>
    /// </summary>
    private void LogRecessFaceRule(int slot, CAbilityCard card, bool front,
                                   RevealGate.FaceRule rule, CPlayerActor? actor)
    {
        if (slot < 0 || slot >= SlotCount)
            return;
        int id = card != null ? card.CardInstanceID : int.MinValue;
        if (_loggedFaceCard[slot] == id && _loggedFaceRule[slot] == (int)rule
            && _loggedFaceFront[slot] == front)
            return;
        _loggedFaceCard[slot] = id;
        _loggedFaceRule[slot] = (int)rule;
        _loggedFaceFront[slot] = front;
        string who = actor != null && actor.Class != null ? actor.Class.ID : "?";
        string name = card != null ? card.Name : "?";
        // HW-VERIFY
        VRLog.Note("Net", $"CARD FACE RULE: peer [{_owner.PlayerId}] recess {slot + 1} is showing "
                        + $"'{name}' ({who}) with its {(front ? "FRONT" : "BACK")} — chosen by "
                        + RevealGate.RuleText(rule)
                        + ". This is the face this client DREW, read off the value handed to the "
                        + "slot, not a second derivation of the rule. A wrong face is therefore one "
                        + "grep: the card is named, the face is named, and the ruling that chose it "
                        + "is quoted — so the question is only ever whether the RULE is the one the "
                        + "user stated for this moment, never which of several rules fired.");
    }

    private void LogAnonymousRecess(int slot, CPlayerActor? actor)
    {
        if (_loggedAnonSlot == slot)
            return;
        _loggedAnonSlot = slot;
        // HW-VERIFY: this line decides report item 15. Grep token: ANONYMOUS RECESS. It is
        // change-gated on WHICH recess, so a short rest produces one line and a settled board
        // produces none. READ IT LIKE THIS: if it fires with the peer's discard count one LOWER
        // than the previous "Pile counts RECEIVED" line and their burnt count unchanged, the card
        // lying there is a short-rest sacrifice in flight between two piles and the identity is on
        // no wire — the fix is a field, not a gate, and the spec is in this method's own note. If
        // it fires during ordinary card SELECTION instead (the peer dropped a card into a recess
        // before the model committed it), that is the pre-existing and correct behaviour and
        // nothing is owed. THE FALSIFIER for any future fix: this line must STOP appearing at a
        // short rest. A build that opens the reveal gate and leaves this line standing has changed
        // a permission and not a picture.
        VRLog.Note("Net", $"ANONYMOUS RECESS [player {_owner.PlayerId}]: round slot {slot + 1} is "
            + "OCCUPIED on the owner's board (the wire's occupancy nibble says so) and this client "
            + "cannot name the card in it, so it draws an anonymous BACK — and it does so ABOVE the "
            + "reveal-gate branch, independent of it. Their piles right now: discard "
            + $"{_owner.PileDiscardCount}, burnt {_owner.PileBurntCount}. A SHORT-REST SACRIFICE is "
            + "NO LONGER one of the ways into this line: extension record 39 names it and "
            + "'SHORT REST SEAT' is printed instead. If a short rest still lands HERE, exactly one "
            + "of four things happened and they are worth naming because the fix differs for each: "
            + "the owner's build predates record 39 (then this is correct and nothing is owed); the "
            + "owner's sampler could not seat the card (their own 'SHORT REST SEAT ... SENT' line "
            + "says 'names nothing'); the two clients' DISCARD arcs disagree in LENGTH, so the belt "
            + "refused a seat that would have shifted; or this client holds no hand for the "
            + "character. READ THE OWNER'S LINE FIRST — it tells the first two apart from the last "
            + "two. WHAT THIS LINE USED TO CLAIM, and it was wrong: that at this instant the card "
            + "is in NEITHER host-replicated pile. It is in DiscardedAbilityCards the whole time it "
            + "lies here — PerformShortRest indexes that list and removes nothing — and the discard "
            + "count above looks one LOWER only because record 15 carries the RENDERED stack label, "
            + "which nets off cards in flight, and our own short-rest presentation makes this card "
            + "one of them. An "
            + $"ordinary selection-phase drop (character '{Board.CharacterFocus.Describe(actor)}' "
            + "putting a card in a recess before the model commits it) also lands here and is "
            + "correct — the pile numbers are what tell the two apart. THE THIRD WAY INTO THIS "
            + "LINE is the compaction belt refusing the walk: if 'ROUND SLOT COMPACTION REFUSED' "
            + "stands beside this line, the card is one this client CAN name but cannot place, "
            + "which is a different defect with a different fix — read that line first.");
    }

    /// <summary>Last reported compaction verdict, as <c>model * 10 + occupied</c> with a sign for
    /// the verdict itself (int.MinValue = never reported), so the line below fires on a real change
    /// and never per frame.</summary>
    private int _loggedCompaction = int.MinValue;

    /// <summary>Change-gated evidence for the compaction belt — see the block in
    /// <see cref="SeatSlots"/> for why a length disagreement may not be walked across.</summary>
    private void LogCompactionIfChanged(bool compact, int modelCount, int occupiedCount)
    {
        int key = (compact ? 1 : -1) * (modelCount * 10 + occupiedCount + 1);
        if (key == _loggedCompaction)
            return;
        bool first = _loggedCompaction == int.MinValue;
        _loggedCompaction = key;
        if (compact)
        {
            if (!first)
                VRLog.Info("Net", $"Remote board [{_owner.PlayerId}] round slots: compaction is "
                    + $"SAFE again — {modelCount} model card(s) for {occupiedCount} occupied "
                    + "recess(es), so recess order and initiative order are the same list.");
            return;
        }
        // HW-VERIFY: grep token ROUND SLOT COMPACTION REFUSED. This is the second, independent way
        // a peer could be shown the WRONG card's front, and it is invisible to every face-state
        // diagnostic beside it because a wrong face reports exactly like a right one. READ IT LIKE
        // THIS: a brief refusal around a burn, a played card or a pick candidate is this client's
        // copy of the peer's round cards lagging their board by a frame and clears itself; a
        // refusal that STANDS means the occupancy nibble and RoundAbilityCards have drifted apart
        // for a reason that is not lag, and the recesses will show latched faces or anonymous backs
        // until they agree. THE FALSIFIER: if this line never appears while the user still reports
        // a wrong card face in a recess, the wrong card is coming from somewhere other than this
        // walk — read ANONYMOUS RECESS and the round-card face states instead.
        VRLog.Note("Net", $"ROUND SLOT COMPACTION REFUSED [player {_owner.PlayerId}]: this client "
            + $"resolves {modelCount} round card(s) for that character while the owner's own board "
            + $"reports {occupiedCount} occupied recess(es). Slab i is only a NAME for card i while "
            + "those two agree, so the walk that hands each recess the next model card is refused "
            + "and each occupied recess falls back to the face THIS board already showed in that "
            + "very recess, or to an anonymous back. Before this build the walk ran anyway and a "
            + "peer with a burn or pick candidate lying in one recess was shown the OTHER round "
            + "card's front there — a confidently wrong face, which is the one failure a player "
            + "cannot read as a failure. Same belt as the hand fan's LENGTH BELT, same reason.");
    }

    /// <summary>Population count of a slot mask — <c>SlotCount</c> is 2, so this is two shifts and
    /// costs nothing; it exists so the census reports a NUMBER rather than a bitmask nobody can read
    /// in a hardware log.</summary>
    private static int CountBits(int mask)
    {
        int n = 0;
        for (int i = 0; i < SlotCount; i++)
            if ((mask & (1 << i)) != 0)
                n++;
        return n;
    }

    /// <summary>
    /// Resolve the SHORT-REST SACRIFICE lying in round recess <paramref name="slot"/> from
    /// extension record 39, or answer false and leave the recess to the walk above.
    ///
    /// <para>WHAT ARRIVES IS A SEAT, NOT A CARD: a source-list id plus an index, which this method
    /// resolves against <c>CardsGameApi.GetPileArcWidgets(hand, burnt: false)</c> — the SAME call
    /// the sender seated it with (<c>LocalRigSampler.SampleSacrificeSeats</c>) and the same call
    /// <c>RemoteHeldCardFace.Resolve</c> uses for record 36's pile arm. One expression, three
    /// consumers; an index is only a name for a card while both machines build the list the same
    /// way.</para>
    ///
    /// <para>FOUR REFUSALS, and every one of them draws the anonymous BACK this record replaces
    /// rather than a guess: no actor to look a hand up on; a list id this build does not resolve
    /// (only DISCARD is defined for this record today); a length disagreement, which means this
    /// client's model lags the owner's and the seat has shifted under it; and a seat past the end.
    /// A back is wrong in a way the player reads as "not loaded"; a confidently wrong FACE is wrong
    /// in a way he cannot read at all and would act on.</para>
    ///
    /// <para>THIS IS A RESOLVE AND NOT A PERMISSION, AND THE SPLIT IS THE 2026-09-07 ITEM 6 FIX.
    /// It used to refuse outright unless <c>RevealGate.IsPublicPopulation</c> answered true for the
    /// two carved-out populations — which was harmless only while those two were unconditionally
    /// exempt. They are not any more (a short rest is the selection phase and is COVERED), and
    /// leaving the permission here would have taken the card's IDENTITY away with its face: the
    /// recess would fall back to an ANONYMOUS back, and <see cref="TryNameArrivingRecessFace"/> —
    /// which is how a burn flight learns which card left a recess — would answer nothing at all.
    /// That would break the ruling that outranks item 6 ("Beim Verbrennen EGAL AUS WELCHEM GRUND
    /// muss die Karte immer mit der Vorderseite sichtbar sein"), because the burn exception can only
    /// fire for a card something can NAME. So: this method names the card, and
    /// <c>RevealGate.CardFaces</c> at the one call site below decides whether that card is drawn
    /// front or back. Resolve, then gate — never gate the resolve.</para>
    ///
    /// <para>THE ANTI-CHEAT BOUNDARY IS STRUCTURAL AND IS UNCHANGED. The record's own vocabulary
    /// refuses the HAND list (see the refusal below), so a card of the two-card commit — the one
    /// secret <c>SelectAbilityCardsOrLongRest</c> exists to keep — is not expressible here even in
    /// principle. Naming a DISCARD or BURNT card grants nothing on its own: the face still has to
    /// pass the gate, and in the secret window it does not.</para>
    /// </summary>
    private bool TryResolveSacrifice(int slot, CPlayerActor? actor, out CAbilityCard? card)
    {
        card = null;
        byte code = _owner.SacrificeSeatCode(slot);
        byte list = NetProtocol.HeldFaceList(code);
        // THE HAND LIST IS REFUSED HERE TOO, and not merely absent from the sender. This is the
        // receiver's own copy of NetProtocol.ExtIdSacrificeSeat's boundary: a peer naming
        // HeldFaceListHand for a recess would be naming a card of the two-card commit, which is the
        // one secret SelectAbilityCardsOrLongRest exists to keep. Refused rather than trusted.
        if (!NetProtocol.HeldFaceNamesCard(code) || !NetProtocol.RecessSeatListAllowed(list))
            return false;
        if (actor == null)
            return false;
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = manager != null ? manager.GetHand(actor) : null;
        if (hand == null)
            return false;
        Cards.CardsGameApi.GetPileArcWidgets(hand, list == NetProtocol.HeldFaceListBurnt,
                                             s_sacrificeBuf);
        int at = NetProtocol.HeldFaceIndex(code);
        bool ok = s_sacrificeBuf.Count == _owner.SacrificeSeatCount(slot) && at < s_sacrificeBuf.Count;
        AbilityCardUI? widget = ok ? s_sacrificeBuf[at] : null;
        s_sacrificeBuf.Clear();
        card = widget != null ? widget.AbilityCard : null;
        return card != null;
    }

    // ============================================================================================
    //  THE DEPARTED FACE — what a recess was legitimately drawing at the instant its card LEFT it.
    //
    //  2026-09-06 report item 5: "Die Animationen bei denen die Karten in den jeweiligen Stapel
    //  gehen zeigen beim remote board die Karten mit der Rückseite als Vorderseite, das ist NICHT
    //  was der Spieler sieht und ist daher ein 1:1 Bruch." The owner's own flight is a live VRCard
    //  that was lying FACE-UP in the recess and whose rotation VRCard.FlyToPile locks for the whole
    //  arc ("the card slides in flat, as it sat on the board") — so the owner watches a FRONT go
    //  into the pile and every peer watched a card BACK.
    //
    //  THE MIRROR COULD NOT NAME THE CARD, and that is why this memory exists rather than a new
    //  wire field. Net.RemoteCardFx replays a 2-byte semantic event (from-anchor, to-anchor) and no
    //  identity rides it — deliberately. But the card that is flying out of a round recess is, by
    //  construction, the card this very board was drawing in that recess one tick ago, and the
    //  identity of THAT card is already resolved here, locally, off the host-replicated model. The
    //  flight does not need a new fact; it needs the fact this class had and threw away.
    //
    //  IT IS DELIBERATELY A LATCH OF THE *APPROVED* FACE. Only _latchedFaces[i] is remembered —
    //  the face this recess drew while the reveal gate was OPEN — so the flight can never show a
    //  front for a card the recess itself was covering. The permission is re-asked at draw time
    //  anyway (RemoteCardFx routes through RevealGate); this makes it impossible to widen by
    //  accident even if that call were ever edited.
    // ============================================================================================

    /// <summary>How long a departed face stays claimable, in seconds. The owner writes the FX event
    /// in the same frame it launches the flight; the extras stream carries it at 5 Hz plus an
    /// on-change send, and this client's own recess mirror runs on the 4 Hz board-content cadence —
    /// so the event can arrive either side of the recess emptying.
    ///
    /// <para>THIS COMMENT USED TO CLAIM THE WINDOW WAS "sized to cover both" SIDES, AND IT WAS
    /// FALSE. An expiry is one-sided by construction: it covers an event arriving LATE and covers
    /// an event arriving EARLY by exactly zero. In the ModBuild 461 host log EVERY flight arrived
    /// early -- all four FLIGHT FACE lines are followed by a board-content line still reporting
    /// slot-occupancy=0x3 -- so this constant never once decided anything and the whole feature was
    /// inert. The EARLY side is now covered by the LIVE LATCH branch in
    /// <see cref="TryTakeDepartedFace"/>, not by widening this number, which would only ever buy a
    /// stale face on a flight whose event was dropped.</para></summary>
    private const float DepartedFaceSeconds = 3f;

    private readonly CAbilityCard?[] _departedFace = new CAbilityCard?[SlotCount];
    private readonly float[] _departedAt = new float[SlotCount];

    /// <summary><c>CardInstanceID</c> of the face a flight has ALREADY claimed out of recess i,
    /// with the time it was claimed (<see cref="int.MinValue"/> = none). It exists only because the
    /// claim may now run BEFORE the recess-emptying edge (see the LIVE LATCH block in
    /// <see cref="TryTakeDepartedFace"/>): without it the edge would afterwards stamp the very card
    /// that has already flown into <see cref="_departedFace"/>, where a second, unrelated flight
    /// could take it. Same three-second horizon as the memory it guards, so nothing latches
    /// forever.</summary>
    private readonly int[] _claimedFaceId = { int.MinValue, int.MinValue };
    private readonly float[] _claimedAt = new float[SlotCount];

    /// <summary><c>CardInstanceID</c> of the SHORT-REST SACRIFICE this recess drew, with the time it
    /// was last seen there. A POSITION memory and nothing else: it is read by
    /// <see cref="RecessOfBurningCard"/> and by nothing at all in any face path, because the
    /// sacrifice branch deliberately refuses to latch a FACE for this recess and that refusal is
    /// correct. Same three-second horizon as <see cref="_departedFace"/>, so nothing latches
    /// forever.</summary>
    private readonly int[] _sacrificeSeatId = { int.MinValue, int.MinValue };
    private readonly float[] _sacrificeSeatAt = new float[SlotCount];

    /// <summary>WHICH of the ways a departed face can be answered actually ran — quoted verbatim
    /// into <c>RemoteCardFx</c>'s <c>FLIGHT FACE</c> line so a BACK names its own cause instead of
    /// listing four. See <see cref="TryTakeDepartedFace"/> for each one.</summary>
    internal enum DepartedFaceVerdict
    {
        /// <summary>The recess-emptying EDGE had already stamped this face and the flight took it —
        /// the path ModBuild 461 built and the only one it had.</summary>
        DepartedMemory,

        /// <summary>The flight beat the edge: nothing was stamped yet, so the face came straight
        /// off the recess's LIVE approved latch. Same card, one board pass earlier.</summary>
        LiveLatch,

        /// <summary>Neither memory nor live latch: this recess never drew that card face-up on this
        /// client at all (the reveal gate was shut for it, the compaction belt refused the walk, or
        /// the recess was drawing an anonymous back). The defect, if any, is UPSTREAM of the
        /// claim — read <c>ROUND SLOT COMPACTION REFUSED</c> and <c>ANONYMOUS RECESS</c>.</summary>
        NothingLatched,

        /// <summary>A face was stamped for this recess and is older than
        /// <see cref="DepartedFaceSeconds"/>. The event was lost or arrived very late.</summary>
        WindowExpired,

        /// <summary>Two recesses emptied at once and this client's copy of that peer's piles holds
        /// BOTH faces in the destination stack. A deliberate refusal, not a failure.</summary>
        AmbiguousBothInPile,

        /// <summary>Two recesses emptied at once and NEITHER face is in the destination stack on
        /// this client yet. Also a deliberate refusal: the model has not caught up and a confident
        /// wrong front is worse than a back.</summary>
        AmbiguousNeitherInPile,
    }

    /// <summary>
    /// NAME THE CARD THAT IS ARRIVING IN ROUND RECESS <paramref name="slot"/> — the other direction
    /// of a recess flight, and the one the departed-face memory was never built for.
    ///
    /// <para>2026-09-06 report item 4, the half the FLIGHT FACE instrument found on its own. Of the
    /// host's five mirrored flights that session, three drew a FRONT and the two BACKs are both
    /// <c>Discard -&gt; Slot0</c> — a card flying OUT of a pile INTO a recess, which is what a short
    /// rest's sacrifice does. <see cref="TryTakeDepartedFace"/> answers that with
    /// <c>CAUSE = WINDOW EXPIRED</c>, and it is right to refuse: what it remembers for recess 0 is
    /// the card that was there BEFORE, and inheriting that would put a confidently wrong face on the
    /// flight. The arriving card is not a memory at all — it is a fact the wire is carrying right
    /// now.</para>
    ///
    /// <para>IT IS RECORD 39 AND NOTHING ELSE. <see cref="TryResolveSacrifice"/> is the one resolve
    /// for "which card is lying in this recess that the round-card model cannot name", it asks the
    /// reveal gate over the two carved-out populations itself, and its list vocabulary refuses the
    /// HAND — so the card of a two-card commit is not expressible here even in principle. Nothing is
    /// consumed and nothing is latched: this is a read of the current packet, so a flight that asks
    /// twice gets the same answer and a flight that asks before the record arrives gets none.</para>
    /// </summary>
    internal bool TryNameArrivingRecessFace(int slot, out CAbilityCard? card)
    {
        card = null;
        if (slot < 0 || slot >= SlotCount)
            return false;
        try
        {
            CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            return TryResolveSacrifice(slot, actor, out card) && card != null;
        }
        catch
        {
            card = null;
            return false;   // a cosmetic flight never takes the board down with it
        }
    }

    /// <summary>
    /// TAKE the face that left recess <paramref name="slot"/> — or, for <paramref name="slot"/> of
    /// -1, whichever unclaimed face belongs in the stack <paramref name="destination"/> names.
    /// CONSUMING: a claimed face is forgotten, so two flights launched by one turn-clear take two
    /// different cards instead of both taking the first.
    ///
    /// <para>WHICH BRANCH RUNS IS NOW THE SENDER'S BUILD, and the history matters because the -1
    /// path used to be the ONLY one. Through ModBuild 460
    /// <c>CardsDriver.TryStartFlyToPile</c> reported its origin as
    /// <c>SlotAnchor(_tray.SlotOf(card))</c>, and by the time that ran the card had already been
    /// evicted from the tray's occupant array — the flight is launched off <c>_lastHalfCards</c>,
    /// the PREVIOUS rebuild's dock membership — so <c>SlotOf</c> answered -1 and the anchor on the
    /// wire was <c>CardFxAnchor.Board</c> every single time. That is measured and not inferred: the
    /// 2026-09-06 logs contain "Board -&gt; Discard" and "Board -&gt; Burnt" and not one
    /// "Slot0 -&gt;" or "Slot1 -&gt;" on either machine. ModBuild 461 reads the card's PHYSICAL
    /// recess instead (<c>PlayTray.RecessSeatOfCard</c> — the eviction is bookkeeping and never
    /// reparents anything), so a current sender names <c>Slot0</c>/<c>Slot1</c> and this method is
    /// asked about ONE recess.</para>
    ///
    /// <para>THE -1 PATH THEREFORE STAYS, AND STAYS EXACTLY AS STRICT. It is what a peer on an older
    /// build still produces, and what a card whose transform has already left its recess produces on
    /// any build. A NAMED slot is strictly more precise than the destination test below — it is the
    /// recess the owner said the card left, so there is nothing to disambiguate and the refusal
    /// cannot be reached; the destination test guards only the ambiguous case it was written for.
    /// Making the pose right did not make the identity looser.</para>
    /// </summary>
    internal bool TryTakeDepartedFace(int slot, CardFxAnchor destination, out CAbilityCard? card,
                                      out DepartedFaceVerdict verdict)
    {
        card = null;
        verdict = DepartedFaceVerdict.NothingLatched;
        float now = Time.unscaledTime;
        int a = -1;
        int b = -1;
        int expired = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            if (slot >= 0 && i != slot)
                continue;
            if (_departedFace[i] != null && now - _departedAt[i] > DepartedFaceSeconds)
                expired++;
            if (_departedFace[i] == null || now - _departedAt[i] > DepartedFaceSeconds)
                continue;
            if (a < 0)
                a = i;
            else
                b = i;
        }
        if (a < 0)
        {
            // --- THE LIVE LATCH: THE CLAIM RUNS BEFORE THE MEMORY IS WRITTEN --------------------
            // MEASURED, NOT ARGUED (ModBuild 461 host log, ALL FOUR of the session's FLIGHT FACE
            // lines): each one is followed by a 'Remote board content' line still reporting
            // slot-occupancy=0x3 with round-card faces=LiveWidget/LiveWidget, and the nibble only
            // falls to 0x0 on the NEXT board pass. The recess had the face, the flight asked for
            // it, and the memory that was supposed to hand it over had not been written yet.
            //
            // THE COMMENT ON DepartedFaceSeconds WAS THE DEFECT and is corrected there. It said the
            // window is "sized to cover both" sides of the recess emptying. An EXPIRY covers only
            // the LATE side: an event arriving before this client's own 4 Hz board pass notices the
            // nibble clear finds _departedFace[slot] null, refuses, and the edge then stamps a
            // memory nobody ever comes back for. Four of four flights this session took that path.
            //
            // IT IS THE SAME VALUE, ONE BOARD PASS EARLIER, and therefore not one grain looser:
            // NoteRecessDeparture's only argument IS _latchedFaces[i], so the memory is a copy of
            // this field taken at the edge. _latchedFaces is filled only under showFronts, cleared
            // the frame the gate shuts or the displayed character changes, and nulled by both the
            // empty and the sacrifice branch -- so a non-null entry here is a face THIS board is
            // drawing, face-up and approved, in THAT recess, right now. RemoteCardFx re-asks
            // RevealGate on top regardless.
            //
            // ONLY FOR A NAMED RECESS. slot < 0 is the legacy / unnameable-transform path, where
            // "which recess emptied" is the very thing that is not known; reaching into both live
            // latches there would be the guess the destination test below exists to refuse. The
            // deliberate refusals below are untouched: with slot >= 0 they were already unreachable.
            if (slot >= 0 && slot < SlotCount && _latchedFaces[slot] != null)
            {
                card = _latchedFaces[slot];
                _claimedFaceId[slot] = card!.CardInstanceID;
                _claimedAt[slot] = now;
                verdict = DepartedFaceVerdict.LiveLatch;
                return true;
            }
            verdict = expired > 0
                ? DepartedFaceVerdict.WindowExpired
                : DepartedFaceVerdict.NothingLatched;
            return false;
        }
        // ─── ONE CANDIDATE IS NOT A CHOICE ──────────────────────────────────────────────────────
        // Nothing to disambiguate, so nothing to refuse over. This is the common case: a turn-clear
        // whose two flights arrive as two separate events, each consuming one memory.
        if (b < 0)
        {
            verdict = DepartedFaceVerdict.DepartedMemory;
            return Take(a, out card);
        }

        // ─── TWO CANDIDATES (ONLY EVER A `Board` ORIGIN): THE DESTINATION DECIDES ──────────────
        // Unreachable when the sender named a recess — `slot >= 0` admits one candidate at most, so
        // everything below is the older-sender / unnameable-transform path and nothing here was
        // loosened to make the origin fix work.
        // A turn-clear can empty BOTH recesses in one frame and send one flight per card, and when
        // one of those is a BURN that RemoteBurnFx presented itself, its event is swallowed and its
        // memory is never claimed — so the discard flight arriving afterwards would find two
        // unclaimed faces and, taken in departure order, could take the BURNT card's. A front drawn
        // on the wrong card is the one failure every record in this project is written to avoid, so
        // the ambiguity is resolved against the peer's OWN host-replicated piles: the card that flew
        // into their discard stack is the one that is IN their discard list.
        CAbilityCard? only = null;
        int onlyIndex = -1;
        for (int i = 0; i < SlotCount; i++)
        {
            if (i != a && i != b)
                continue;
            if (!InDestinationPile(_departedFace[i], destination))
                continue;
            if (only != null)
            {
                verdict = DepartedFaceVerdict.AmbiguousBothInPile;
                return false;   // both are in it — no fact here separates them, so a BACK
            }
            only = _departedFace[i];
            onlyIndex = i;
        }
        // …AND NEITHER MATCHING IS ALSO A REFUSAL, deliberately. This client's copy of that
        // character's piles can lag the extras packet by a frame, and "the model has not caught up"
        // is indistinguishable here from "this face belongs to the other flight". A back is wrong in
        // a way the player reads as not-loaded-yet; a confident wrong front is not.
        if (only == null)
        {
            verdict = DepartedFaceVerdict.AmbiguousNeitherInPile;
            return false;
        }
        verdict = DepartedFaceVerdict.DepartedMemory;
        return Take(onlyIndex, out card);

        bool Take(int index, out CAbilityCard? taken)
        {
            taken = _departedFace[index];
            _departedFace[index] = null;
            return taken != null;
        }
    }

    /// <summary>Is <paramref name="face"/> in the peer's own replicated pile that
    /// <paramref name="destination"/> names? Read-only, allocation-free (the raw backing lists, not
    /// the LINQ projections beside them), and false for any anchor that is not a stack.</summary>
    private bool InDestinationPile(CAbilityCard? face, CardFxAnchor destination)
    {
        if (face == null)
            return false;
        try
        {
            CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
            CCharacterClass? cc = actor != null ? actor.CharacterClass : null;
            if (cc == null)
                return false;
            return destination switch
            {
                CardFxAnchor.Discard => Holds(cc.DiscardedAbilityCards, face),
                CardFxAnchor.Burnt => Holds(cc.LostAbilityCards, face)
                                      || Holds(cc.PermanentlyLostAbilityCards, face),
                _ => false,
            };
        }
        catch
        {
            return false;
        }

        static bool Holds(System.Collections.Generic.List<CAbilityCard>? list, CAbilityCard face)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].CardInstanceID == face.CardInstanceID)
                    return true;
            return false;
        }
    }

    /// <summary>Remember the face recess <paramref name="slot"/> was drawing, at the moment it stops
    /// drawing it. Called on the EDGE only (the latch it reads is nulled in the same breath), so a
    /// recess that stays empty does not keep re-stamping its own clock.</summary>
    private void NoteRecessDeparture(int slot, CAbilityCard? face)
    {
        if (face == null)
            return;
        // ...AND NOT IF IT HAS ALREADY FLOWN. Since the LIVE LATCH branch in TryTakeDepartedFace, a
        // flight can claim this recess's face BEFORE the edge fires -- which the 461 logs say is
        // the ORDINARY ordering, not the rare one. Stamping it into the memory afterwards would
        // leave a face that is already on a slab sitting there for up to three seconds, where a
        // legacy `slot = -1` claim could take it a second time and draw one card on two flights.
        float now = Time.unscaledTime;
        if (_claimedFaceId[slot] == face.CardInstanceID
            && now - _claimedAt[slot] <= DepartedFaceSeconds)
        {
            _claimedFaceId[slot] = int.MinValue;
            return;
        }
        _departedFace[slot] = face;
        _departedAt[slot] = now;
    }

    /// <summary>Reused widget buffer for <see cref="TryResolveSacrifice"/> — the board's content
    /// pass runs at 4 Hz and must stay allocation-free.</summary>
    private static readonly System.Collections.Generic.List<AbilityCardUI> s_sacrificeBuf = new();

    /// <summary>Last recess a sacrifice face was reported for, as <c>slot * 1000 + card id</c>
    /// (int.MinValue = never), so a redraw prints a second line and a settled rest prints none.
    /// </summary>
    private int _loggedSacrifice = int.MinValue;

    /// <summary>
    /// Report item 15 — the RECEIVER edge of the short-rest sacrifice. Grep token:
    /// SHORT REST SEAT, the SAME token the sender prints, so one grep across two logs answers the
    /// 1:1 question in one pass instead of correlating two different tokens.
    ///
    /// <para>Change-gated on the recess AND the card, so a short rest costs one line and a redraw
    /// costs a second — the one mid-rest event worth seeing — while a settled board costs none.</para>
    ///
    /// <para>FALSIFIERS. (1) This line absent all session AND no "SHORT REST SACRIFICE" line on
    /// either machine: no short rest happened, and the round says NOTHING about item 15 — silence
    /// is not success. (2) The owner's "SHORT REST SEAT ... SENT" line naming a seat while this one
    /// is absent: the record arrived and did not resolve — read "ANONYMOUS RECESS" beside it, which
    /// now states which of the four refusals fired. (3) THE 1:1 FAILURE: this line and the owner's
    /// own "SHORT REST SACRIFICE" line naming DIFFERENT cards for the same rest. That cannot happen
    /// through a length disagreement (the belt refuses and this line does not print at all), so it
    /// would mean the two machines build the discard arc differently — one expression has grown a
    /// second copy again, and CardsGameApi.GetPileArcWidgets is the place to look.</para>
    /// </summary>
    private void LogSacrificeSeat(int slot, CAbilityCard card, CPlayerActor? actor, bool front,
                                  RevealGate.FaceRule rule)
    {
        int key = (slot * 1000 + card.ID) * 2 + (front ? 1 : 0);
        if (_loggedSacrifice == key)
            return;
        _loggedSacrifice = key;
        // HW-VERIFY: report item 15, the RECEIVER edge. Note tier because the co-player runs at the
        // shipped default level and either tester can be the one watching.
        //
        // THIS LINE USED TO STATE ITS OWN VERDICT AS A CONSTANT, AND IT WAS FALSE (found 2026-09-07
        // while reading report item 8). It printed "draws the REAL FRONT of 'X'" and "it is drawn
        // face-up even though the game's secret selection phase is open — RevealGate.
        // PeerCardPopulation.SacrificedCard is the named carve-out" on EVERY call, unconditionally,
        // while the branch that calls it had already stopped hardcoding front:true and that carve-out
        // had already been RETIRED by the same 2026-09-07 item 6 that made a short rest covered. So
        // the ModBuild 476 host log's `SHORT REST SEAT [player 2]: round slot 1 draws the REAL FRONT
        // of 'ABILITY_CARD_SpareDagger'` (raw 199601) is NOT evidence that a front was drawn there —
        // it is evidence that this string was never re-derived. Anything concluded from those eight
        // lines about which face a sacrifice recess showed has to be re-measured. It now quotes the
        // term that actually decided, through the same RevealGate.RuleText every other face line
        // uses, and the dedupe key carries the verdict so a recess that FLIPS prints again instead
        // of being swallowed by the key it already printed under.
        VRLog.Note("Net", $"SHORT REST SEAT [player {_owner.PlayerId}]: round slot {slot + 1} draws "
            + $"'{card.Name}' (card id {card.ID}) with its "
            + (front ? "REAL FRONT" : "BACK — an identity-KNOWN back, never an anonymous one")
            + $", by {RevealGate.RuleText(rule)}. A SHORT REST'S SACRIFICE IS COVERED WHILE IT "
            + "MERELY LIES THERE (2026-09-07 item 6, the user's third statement of it: \"Kurze Rast "
            + "= Auswahlphase = verdeckt, Lange Rast = Aktionsphase = alles offen\") and turns "
            + "face-up the instant its owner accepts, because the card lands in LostAbilityCards "
            + "before the burn artwork starts and RevealGate.IsPubliclyRevealedCard answers for it "
            + "from then on. A long-rest or recover pick is the ACTION phase and is open on its "
            + $"own. Resolved from the DISCARD "
            + $"arc at seat {NetProtocol.HeldFaceIndex(_owner.SacrificeSeatCode(slot))} of "
            + $"{_owner.SacrificeSeatCount(slot)} (extension record 39), for character "
            + $"'{Board.CharacterFocus.Describe(actor)}'. WHAT ARRIVED IS A SEAT, never a "
            + "card id and never a card name: the owner's own client indexed the card in "
            + "CardsGameApi.GetPileArcWidgets(hand, burnt: false) and this client re-walked the same "
            + "call, and the seat is refused outright unless both copies of that list are exactly "
            + "as long. Compare this card id with the owner's own 'SHORT REST SEAT ... SENT' seat and with "
            + "their 'SHORT REST SACRIFICE' line: two "
            + "different names for one rest is the 1:1 failure and means the two machines have "
            + "stopped building that list with the same expression.");
    }

    /// <summary>Frame on which <see cref="MirroredSlotMask"/> last CHANGED, and the previous mask,
    /// so a flight can say whether it beat this client's own recess mirror. See
    /// <see cref="FramesSinceSlotMaskChange"/>.</summary>
    private int _slotMaskFrame;
    private int _lastNotedSlotMask = -1;

    /// <summary>The occupancy this mirror is actually DRAWING (bit0 = recess 1) -- not the raw wire
    /// nibble: an exhausted owner is forced empty and a legacy sender is derived from the model.
    /// </summary>
    internal int MirroredSlotMask => _slotOccupiedMask;

    /// <summary>Frames since <see cref="MirroredSlotMask"/> last changed, or -1 before the first
    /// board pass. IT IS THE RACE, IN ONE NUMBER: a flight whose recess is still marked occupied
    /// here arrived BEFORE this client saw the recess empty, which is what made ModBuild 461's
    /// departed-face memory inert on all four of its flights.</summary>
    internal int FramesSinceSlotMaskChange =>
        _lastNotedSlotMask < 0 ? -1 : Time.frameCount - _slotMaskFrame;

    /// <summary>
    /// HOW MANY OF THE OWNER'S OCCUPIED RECESSES HOLD A CARD THIS CLIENT'S ROUND-CARD MODEL DOES
    /// NOT ACCOUNT FOR -- 0, 1 or 2. In practice that is a card the owner has laid down out of his
    /// HAND as one step of a modal pick (avoid damage, card limit, the long rest's burn step): the
    /// game has not moved it out of <c>HandAbilityCards</c>, so it is still in every client's hand
    /// list, while the owner's own VR fan has already dropped it.
    ///
    /// <para>WHY ANY SURFACE OUTSIDE THIS FILE WANTS IT, and it is not about faces at all. The
    /// owner's hand-fan SLAB COUNT on the wire is short by exactly this many cards, on top of
    /// whatever is in his fist. <c>Net.RemoteHandFan</c>'s record-36 seat belt assumed the arc was
    /// short by the FIST ALONE (<c>listLength == count + 1</c>), so the moment one hand card is
    /// lying in a recess every subsequent seat is refused -- 8 <c>FAN RETURN VERDICT ... term=
    /// seatUsable</c> lines in the ModBuild 461 host log, naming seats 0, 1, 4, 6 and 7 of an
    /// 8-card list. That refusal takes the fist NAME down with it, which takes the recess hand-off
    /// down with it (6 releases resolved as "the card went into a ROUND RECESS", 0 hand-offs
    /// armed), which leaves the fan's own length belt with nothing to drop -- and the whole fan
    /// goes to BACKS for as long as the pick is open. Report item 6's second half, in one chain.
    /// </para>
    ///
    /// <para>IT IS NOT A NEW FACT AND COSTS NO WIRE BYTE: the occupancy nibble is the owner's own
    /// (extension record 4) and the round-card count is this client's own resolve -- the identical
    /// pair <see cref="LogCompactionIfChanged"/> already prints. IT IS ALSO NOT A NAME. It says HOW
    /// MANY, never WHICH, so nothing here can reveal a card the owner is holding secret; the
    /// anti-cheat boundary for naming a hand card lying in a recess is
    /// <c>NetProtocol.RecessSeatListAllowed</c>, which refuses <c>HeldFaceListHand</c> AT THE
    /// DECODE, and this build does not touch it.</para>
    ///
    /// <para>RESIDUAL RISK, STATED. If this client's copy of <c>RoundAbilityCards</c> lags while a
    /// COMMITTED round card sits in the recess, this reads 1 when the true answer is 0 and the seat
    /// belt admits a seat it should have refused. It cannot by itself paint a wrong face: naming
    /// the fist ALSO requires <c>listLength == _handBuffer.Count</c>, an independent test against
    /// the local model. The worst it can do alone is send the return glide to a neighbouring arc
    /// seat, which the next fan relayout corrects.</para>
    /// </summary>
    internal int SeatedHandCardExcess { get; private set; }

    private void NoteMirroredSlotMask()
    {
        if (_slotOccupiedMask == _lastNotedSlotMask)
            return;
        _lastNotedSlotMask = _slotOccupiedMask;
        _slotMaskFrame = Time.frameCount;
    }

    private void SeatSlots(CPlayerActor? actor, bool showFronts, bool exhausted)
    {
        // EXHAUSTED OWNER ⇒ BOTH RECESSES EMPTY, whatever the wire last said. The occupancy nibble
        // is a LATCHED fact — the last one its owner sent while their character was alive — so
        // reading it here is exactly how a board that is supposed to be empty keeps two anonymous
        // card backs. Forcing the mask to 0 rather than to -1 matters: -1 would fall through to the
        // legacy MODEL derivation, which for a null actor is empty too but for the wrong reason.
        int wire = exhausted ? 0 : _owner.SlotOccupancyKnown ? _owner.BoardSlotMask : -1;
        SeatedHandCardExcess = 0;   // recomputed below once both counts exist; never stale
        _slotOccupiedMask = 0;
        _slotFaceMask = 0;
        _slotAnonMask = 0;
        _slotPickSeatMask = 0;
        _slotPickBackMask = 0;
        _pickSeatRule = "no recess needed a record-39 seat this frame";

        // TWO resets, and only two (see _latchedFaces / _latchedActor):
        //   • the reveal gate closing — the next secret selection phase / scenario end / actor loss
        //     all answer showFronts == false, and from that frame on nothing latched during the
        //     previous action phase exists any more;
        //   • the DISPLAYED CHARACTER changing — a face approved for one character must never be
        //     repeated in another character's recess just because the recess index matched.
        //
        // …AND THE FIRST OF THE TWO NOW ASKS THE CARD, WHICH IS THE WHOLE OF THE BURN CARVE-OUT ON
        // THIS FIELD (2026-09-07 review, item B4). The latch was WRITTEN under `front` — which has
        // included RevealGate.IsPubliclyRevealedCard since the carve-out shipped — and CLEARED and
        // READ under the bare `showFronts`, so a burn-exception front was latched and erased on the
        // very next frame. The case that costs is the one the carve-out exists for: the game drains
        // RoundAbilityCards under a card that is burning inside the owner's short rest, the walk
        // below can no longer name the recess, the latch has been wiped, and the recess falls to
        // SetAnonymousBack() — an anonymous back for the exact picture the user ruled must be a
        // front ("Beim Verbrennen EGAL AUS WELCHEM GRUND"). Only a card that is STILL in that
        // character's activated / lost / permanently-lost lists survives the gate shutting, so the
        // exception this widens by is the one RevealGate already owns and nothing else: an ordinary
        // round card latched during the action phase is still erased the instant the gate closes.
        if (!ReferenceEquals(actor, _latchedActor))
        {
            for (int i = 0; i < _latchedFaces.Length; i++)
                _latchedFaces[i] = null;
        }
        else if (!showFronts)
        {
            for (int i = 0; i < _latchedFaces.Length; i++)
            {
                CAbilityCard? held = _latchedFaces[i];
                if (held != null
                    && !RevealGate.IsPubliclyRevealedCard(actor, held.CardInstanceID))
                    _latchedFaces[i] = null;
            }
        }
        // THE SACRIFICE SEAT MEMORY FOLLOWS ONLY THE SECOND OF THOSE TWO. It is not gate-scoped —
        // the sacrifice is drawn face-up WHILE the gate is shut, which is the whole of the carve-out
        // below, so clearing it on !showFronts would erase it on every frame it is written. It IS
        // character-scoped, for the same reason the latch is: a seat recorded for one character must
        // never place another character's burning card.
        if (!ReferenceEquals(actor, _latchedActor))
        {
            for (int i = 0; i < SlotCount; i++)
                _sacrificeSeatId[i] = int.MinValue;
        }
        _latchedActor = actor;

        if (wire < 0)
        {
            // LEGACY sender (no occupancy nibble): the model IS the occupancy, per index, exactly
            // as every build before this one rendered it — and therefore the seated-hand-card
            // excess is zero BY CONSTRUCTION, not merely unknown. Set explicitly so a legacy peer
            // can never inherit a stale count from a modern one on a focus switch.
            SeatedHandCardExcess = 0;
            for (int i = 0; i < SlotCount; i++)
            {
                if (_owner.BurnOwnsRecess(i))
                {
                    SuppressBurnRecess(i);
                    continue;
                }
                CAbilityCard? legacy = _ordered[i];
                // THE SAME ONE EXPRESSION THE MODERN BRANCH BELOW USES. This branch spelled a bare
                // `showFronts` and therefore had NO burn carve-out at all — the third copy of the
                // recess face decision in this one method, and the only one that could still draw a
                // burning card as a back. It is a JOIN-WINDOW TRANSIENT (a sender with no occupancy
                // nibble; it drew nothing at all in the ModBuild 476 session), so this is a
                // consistency fix and not a live defect — but a decision written three ways is how
                // the next reader picks the wrong one.
                if (legacy == null)
                {
                    _cards[i].Set(null, showFronts, actor);
                    continue;
                }
                bool legacyFront =
                    RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor,
                                         legacy.CardInstanceID, scenarioEstablished: true,
                                         out RevealGate.FaceRule legacyRule)
                    != RevealGate.CardFaceSource.None;
                _cards[i].Set(legacy, legacyFront, actor);
                LogRecessFaceRule(i, legacy, legacyFront, legacyRule, actor);
                _slotOccupiedMask |= 1 << i;
                if (legacyFront)
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
        // ─── THE COMPACTION BELT: A WALK ACROSS A LENGTH DISAGREEMENT NAMES THE WRONG CARD ──────
        // The walk below hands each occupied recess the next model card that is not null. That is a
        // POSITIONAL ZIP of two lists — the owner's occupancy nibble off the wire, and this client's
        // own RoundAbilityCards — and it is only a NAME for a card while the two agree in LENGTH.
        // The moment they do not, every recess from the first divergence on draws somebody else's
        // face: the owner has a burn or pick candidate lying in one recess while the model still
        // names both round cards, and the peer is shown the OTHER card's front. It is exactly the
        // defect the hand fan's own length belt exists for, one surface over, and the identical
        // remedy applies — a back is wrong in a way the player can read as "not loaded yet", a
        // CONFIDENTLY WRONG FACE is wrong in a way he cannot read at all and would act on.
        //
        // Where the counts agree, compaction is not a guess: initiative order IS the physical order
        // on the owner's board, because CardsDriver.ReconcileInitiative drives the game's initiative
        // to follow whichever card the player put in slot 0. Where they disagree there is no fact
        // here that says which recess holds which card, so the walk is refused outright and each
        // occupied recess falls back to its OWN latched face (a face this board already legitimately
        // showed in that very recess) or to an anonymous back. Nothing guesses.
        int modelCount = 0;
        for (int i = 0; i < SlotCount; i++)
            if (_ordered[i] != null)
                modelCount++;
        int occupiedCount = CountBits(wire & ((1 << SlotCount) - 1));
        // See SeatedHandCardExcess: how many occupied recesses this client's round-card model does
        // not account for. Clamped at zero because the model leading the nibble (a card the model
        // has already moved into a recess the owner has not reported yet) is lag in the OTHER
        // direction and says nothing about the owner's hand.
        SeatedHandCardExcess = Mathf.Clamp(occupiedCount - modelCount, 0, SlotCount);
        bool compact = modelCount == occupiedCount;
        LogCompactionIfChanged(compact, modelCount, occupiedCount);
        // THE HAND-OFF IS A STAND-IN FOR A FACT THAT HAS NOT ARRIVED, so the moment it arrives it
        // gets out of the way. `compact` IS that arrival: this client's own model can name the
        // recesses again and the walk below is authoritative, so anything the fist handed over is
        // now a second answer to a question that already has one. Dropped here rather than left to
        // its own backstop because a latch whose normal exit is a timeout is a latch that outlives
        // its edge.
        if (compact)
            _owner.HandFan.ClearHandoff();
        // …AND THE HAND-OFF'S OWN SAFETY IS NOW DRIVEN FROM HERE TOO (2026-09-07 items 5a/5b). The
        // fan's copy of this test only runs while the fan is TICKING, and RemoteHandFan.Tick returns
        // before it the moment the owner's arc empties — which is precisely when a peer has finished
        // laying their picked card down. The memory has to survive that (or the recess falls to an
        // anonymous back in the action phase), so its expiry may not be the fan's job any more.
        _owner.HandFan.ExpireHandoffAgainst(wire);

        int next = 0;
        for (int i = 0; i < SlotCount; i++)
        {
            // The burn owns the card as soon as its slab is visible, including a sacrifice whose
            // discard seat no longer resolves after acceptance. Do not rebuild a second back
            // under that front between the 4 Hz content refresh and the owner's occupancy edge.
            if (_owner.BurnOwnsRecess(i))
            {
                SuppressBurnRecess(i);
                continue;
            }
            if ((wire & (1 << i)) == 0)
            {
                _cards[i].Set(null, showFronts, actor); // empty recess — and it stays empty
                // …AND THE LATCH GOES WITH IT. _latchedFaces[i] used to survive its own recess
                // emptying, so a face approved in an earlier round could reappear in a recess that
                // has since been cleared — the latch's whole stated purpose is "a recess the model
                // can no longer name KEEPS the face it legitimately showed", and an EMPTY recess is
                // not that case, it is the case where there is nothing to keep. There is no state
                // in which a latched face for an unoccupied recess is wanted.
                //
                // …EXCEPT AS THE FACE THAT JUST LEFT. This is the recess-emptying EDGE — the latch
                // is non-null exactly on the frame it stops being the picture, and that is the one
                // instant at which this client still knows which card the owner is now flying into
                // a pile. Report item 5; see the DEPARTED FACE block above for why the flight
                // cannot ask anywhere else.
                NoteRecessDeparture(i, _latchedFaces[i]);
                _latchedFaces[i] = null;
                continue;
            }
            _slotOccupiedMask |= 1 << i;
            // ─── THE SHORT-REST SACRIFICE, AND IT IS ASKED FIRST ────────────────────────────────
            // Report item 15. The owner's own record 39 names WHICH card is lying in THIS recess,
            // by seat in their discard arc — a more specific fact than the positional zip below,
            // which is why it is consulted before the walk rather than as a fallback after it. It
            // also cannot disturb that walk: it never advances `next`, because a sacrifice recess
            // is not holding a round card.
            //
            // ITS FACE IS THE GATE'S ANSWER AND NOT THIS BRANCH'S — 2026-09-07 item 6, and it is
            // the third statement of the rule: "Kurze Rast = Auswahlphase = verdeckt, Lange Rast =
            // Aktionsphase = alles offen." This branch used to hardcode front:true because
            // SacrificedCard/BoardPickSeat were exempt from the phase; they are not any more
            // (RevealGate.IsPublicPopulation carries the whole derivation), so the identity resolved
            // above is drawn through the same predicate every other card is. Three outcomes, all
            // named in the log line below:
            //   • short rest, still lying there  ⇒ SELECTION PHASE, a BACK — but an identity-KNOWN
            //     back, not an anonymous one, which is why the resolve above is not gated;
            //   • the owner accepts and it burns ⇒ BURN/ACTIVE EXCEPTION, a FRONT, because the card
            //     lands in LostAbilityCards before the burn artwork starts;
            //   • a long-rest / recover pick     ⇒ ACTION PHASE, a FRONT, as it always was.
            if (TryResolveSacrifice(i, actor, out CAbilityCard? sacrifice) && sacrifice != null)
            {
                // THE SAME ONE CALL THE ORDINARY ROUND CARD BELOW MAKES, and that identity is the
                // point rather than a coincidence: now that SacrificedCard is no longer exempt,
                // RevealGate.IsPublicPopulation answers false for it exactly as it does for
                // Selectable, so the two populations have the SAME face rule and asking once means
                // a recess cannot draw one card by one rule and the next by another.
                //
                // IT IS ROUTED THROUGH RevealGate.CardFaces AND THE VERDICT DID NOT MOVE. This was
                // a hand-written ternary - one of two copies of one ladder, and the reason
                // FaceRule.NoContext could not be produced anywhere in the mod. The capability half
                // is passed EXPLICITLY (scenarioEstablished: true) rather than left to
                // RevealGate.InScenario, which is exactly what the ordinary branch's own note below
                // says must not happen here: this board's lifetime is gated on
                // RemoteBoardScenarioGate, and handing CardFaces its InScenario term instead would
                // draw a BACK in the window where the two disagree.
                bool pickFront =
                    RevealGate.CardFaces(RevealGate.PeerCardPopulation.SacrificedCard, actor,
                                         sacrifice.CardInstanceID, scenarioEstablished: true,
                                         out RevealGate.FaceRule pickRule)
                    != RevealGate.CardFaceSource.None;
                if (pickFront)
                    _slotFaceMask |= 1 << i;
                if (pickFront)
                    _slotPickSeatMask |= 1 << i;
                else
                    _slotPickBackMask |= 1 << i;
                _pickSeatRule = "extension record 39 named a seat and this client resolved it in "
                    + "its own copy of that pile arc; the FACE was then chosen by "
                    + RevealGate.RuleText(pickRule);
                LogRecessFaceRule(i, sacrifice, pickFront, pickRule, actor);
                _cards[i].Set(sacrifice, pickFront, actor);
                // NOT LATCHED. _latchedFaces exists so a recess whose ROUND card the model has
                // drained keeps the face it legitimately showed; a sacrifice is the opposite kind
                // of card — it leaves the moment its owner accepts or re-draws, and a latch would
                // keep a burnt card's front lying in an empty recess for the rest of the phase.
                _latchedFaces[i] = null;
                // …BUT ITS SEAT IS REMEMBERED ANYWAY, AND THAT IS A DIFFERENT FACT (2026-09-06 late
                // report, item 9, second clause: "Beim Test ist es nochmal vorgekommen, dass es auch
                // von der direkten Mitte aus gestartet ist, als der Mitspieler eine kurze Rast
                // gemacht und dadurch eine Karte verbrannt hat").
                //
                // THE SENTENCE ABOVE WAS TRUE AND STILL COST A BURN ITS ORIGIN. Nulling the latch is
                // right for a FACE, and NoteRecessDeparture's only argument IS the latch — so a
                // sacrifice recess emptying stamped nothing, and RecessOfBurningCard, which reads
                // that memory, had no fact at all about the one card in the game that is guaranteed
                // to burn out of a recess. The ModBuild 462 host log shows it happening on burn #5
                // and only on burn #5: "'ABILITY_CARD_SpareDagger' — showing it at their board
                // CENTRE, because the owner's board reports NO occupied recess". The short rest
                // finalises and the seat clears within the burn watch's own poll window, so by the
                // time the burn is discovered the occupancy nibble is already 0 and the strongest
                // three answers are all silent.
                //
                // THIS IS A POSITION, NEVER AN IDENTITY, and that is why it is a separate field
                // rather than a wider latch. RecessOfBurningCard's own doc draws the line: getting a
                // POSITION wrong costs a card lifting from the wrong recess 60 mm away, getting a
                // FACE wrong costs an identity the viewer cannot tell is wrong. Nothing reads this
                // pair except that method; _departedFace, _latchedFaces and every face path are
                // untouched, so the burnt front this branch's comment guards against still cannot
                // come back.
                _sacrificeSeatId[i] = sacrifice.CardInstanceID;
                _sacrificeSeatAt[i] = Time.unscaledTime;
                LogSacrificeSeat(i, sacrifice, actor, pickFront, pickRule);
                continue;
            }
            CAbilityCard? card = null;
            while (compact && next < SlotCount && card == null)
                card = _ordered[next++];
            // ACTION-PHASE FACE LATCH (see _latchedFaces): while the gate is OPEN, a recess the
            // model can no longer name — the game drained RoundAbilityCards as the owner used the
            // cards mid-turn — keeps showing the face this board already legitimately showed
            // there, instead of degrading to an anonymous back for the rest of the action phase.
            //
            // THE THREE TERMS ARE NOW ONE TERM, AND THEY USED NOT TO BE. The sentence that stood
            // here — "the latch is only ever FILLED here under showFronts, only ever READ here
            // under showFronts, and cleared above the moment the gate shuts" — was contradicted by
            // its own file: the FILL below is under `front`, which has included the burn exception
            // since that carve-out shipped, so the write and the read disagreed by exactly the set
            // of cards the ruling is about. Read (here), cleared (above) and written (below) now
            // all ask RevealGate.IsPubliclyRevealedCard, so a burning card whose round-card entry
            // the game has drained keeps the front the user ruled it must have, and nothing else
            // does.
            if (card == null)
            {
                CAbilityCard? latched = _latchedFaces[i];
                if (latched != null
                    && (showFronts
                        || RevealGate.IsPubliclyRevealedCard(actor, latched.CardInstanceID)))
                    card = latched;
            }
            // ─── THE FIST'S HAND-OFF, ASKED LAST (report item 5) ────────────────────────────────
            // The walk has been refused and the latch has nothing — which is what a card ARRIVING
            // in this recess looks like, because a recess the model has never named has no face to
            // latch. RemoteHandFan watched this peer's fist empty into this very recess and
            // resolved the card from record 36's seat in this client's OWN hand list, so this is a
            // card that was OBSERVED being handed over rather than the next one off a positional
            // zip. Asked LAST on purpose: the model and the latch are both stronger facts, and the
            // hand-off may only fill a hole, never overrule one of them.
            //
            // IT CHANGES NO PERMISSION. The face goes up under the SAME RevealGate.CardFaces call
            // every other card in this loop is asked through — a recess filling during the secret
            // selection phase still draws a back on every peer unless the BURN EXCEPTION
            // (RevealGate.IsPubliclyRevealedCard) opens it, which is RevealGate's ruling and not
            // this method's to widen. That exception is the ONLY thing that draws a face while the
            // gate is shut, on this recess and on every other surface; the sacrifice carve-out that
            // used to be the other one is retired.
            if (card == null)
                card = _owner.HandFan.HandoffFor(i);
            if (card == null)
            {
                _slotAnonMask |= 1 << i;
                // WHY THIS RECESS IS A BACK, in the form the census quotes. Report item 7's card
                // lying on the board lands HERE when the owner's record 39 said nothing about it,
                // and on the other branch when it said something this client could not resolve —
                // two different fixes, so the rule has to tell them apart.
                // THIS STRING USED TO BLAME RECORD 39 FOR EVERY ANONYMOUS RECESS, INCLUDING THE ONE
                // CASE RECORD 39 CANNOT POSSIBLY ANSWER (2026-09-07 items 5a/5b). An avoid-damage
                // burn lays a HAND card in a recess, and NetProtocol.RecessSeatListAllowed refuses
                // HeldFaceListHand at the DECODE — the format cannot express that card even in
                // principle — so "the sampler wrote no pile seat" was true, unhelpful and pointed a
                // whole round of hardware testing at the wrong end of the wire. The FOURTH resolve,
                // the fist's RECESS HAND-OFF, is the one that owes an answer for a hand card, and it
                // never appeared in this string at all. It does now, so the next log says which of
                // the two mechanisms was silent instead of naming the one that was never asked.
                bool namedSeat = NetProtocol.HeldFaceNamesCard(_owner.SacrificeSeatCode(i));
                bool handoffArmed = _owner.HandFan.HandoffFor(i) != null;
                _pickSeatRule = namedSeat
                    ? "extension record 39 NAMED a seat for this recess and this client could not "
                      + "resolve it — the two copies of that pile arc disagree in LENGTH, or there "
                      + "is no hand for the character (read the owner's own seat line)"
                    : "extension record 39 named NO seat for this recess, so nothing can name the "
                      + "card in it: the owner's model does not list it in RoundAbilityCards and "
                      + "their sampler wrote no pile seat either. THE FOURTH RESOLVE IS THE FIST'S "
                      + "RECESS HAND-OFF and it "
                      + (handoffArmed
                          ? "IS armed for this recess, so it was overruled above and this line is "
                            + "describing the wrong frame — read this method"
                          : "is NOT armed, which for an avoid-damage burn is the WHOLE cause and "
                            + "not a footnote: that card is a HAND card, and record 39 refuses "
                            + "HeldFaceListHand at the decode (NetProtocol.RecessSeatListAllowed), "
                            + "so the sampler was never able to name it. Read 'RECESS HAND-OFF' for "
                            + "this peer, never the owner's 'SHORT REST SEAT' line");
                _cards[i].SetAnonymousBack();
                LogAnonymousRecess(i, actor);
                continue;
            }
            // ─── THE BURN CARVE-OUT REACHES THE RECESS TOO (2026-09-06 report item 6) ──────────
            // "Wenn ein Mitspieler eine Karte verbrennt sehe ich wieder nur die Rueckseite auf dem
            // Board. Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der
            // Vorderseite sichtbar sein." A burn lands in the owner's LostAbilityCards BEFORE the
            // game starts the burn artwork that holds the card in this very recess, so for the
            // whole of the picture he is complaining about, the card is already in a host-
            // replicated list this client walks. RevealGate.IsPubliclyRevealedCard owns that ruling
            // for every surface; this is the one that draws the card while it chars.
            //
            // IT WIDENS AND NEVER NARROWS: showFronts still decides on its own for every card that
            // is not already public, so the selection-phase secret is untouched. The LATCH follows
            // the picture deliberately — a face this recess legitimately showed is exactly what the
            // latch is for, and a flight out of this recess (TryTakeDepartedFace) must be able to
            // inherit a burning card's front or item 6's first half comes straight back.
            // IT IS RevealGate.CardFaces NOW, AND THE VERDICT DID NOT MOVE — the capability half is
            // handed to it EXPLICITLY instead of being left to RevealGate.InScenario, which is what
            // used to make this call impossible here. This board's own lifetime is gated on
            // RemoteBoardScenarioGate, whose doc says in as many words that it is "deliberately NOT
            // RevealGate.InScenario". In the window where the two disagree (the board is up, the
            // save's CurrentGameState has not said Scenario yet) ShowRoundCardFronts answers TRUE by
            // negation and this recess draws a front, while an InScenario-flavoured CardFaces would
            // answer None and draw a BACK — the one thing prohibited outright ("außerhalb der
            // Auswahlphase NIEMALS Rückseiten"). scenarioEstablished: true says that this surface
            // has already answered the capability question somewhere else, so the only thing left
            // for the call to decide is SECRECY, which is the whole point of the split.
            //
            // WHAT IT BUYS OVER THE TERNARY IT REPLACES: the ternary could not produce
            // FaceRule.NoContext, so an actorless recess reported a SECRECY verdict for what is a
            // CAPABILITY failure — this file's oldest recorded defect, in the instrument built to
            // stop it. It also removes the second of two hand-written copies of one ladder; a rule
            // spelled at a call site is a rule two call sites can spell differently.
            bool front = RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor,
                                              card.CardInstanceID, scenarioEstablished: true,
                                              out RevealGate.FaceRule rule)
                         != RevealGate.CardFaceSource.None;
            LogRecessFaceRule(i, card, front, rule, actor);
            _cards[i].Set(card, front, actor);
            if (front)
            {
                _slotFaceMask |= 1 << i;
                _latchedFaces[i] = card; // remember the approved face for this recess
            }
        }
    }

    /// <summary>
    /// Drive the GAME's own card plume on the two round recesses (wire id
    /// <see cref="NetProtocol.TuneGameCardParticlesOn"/>). One gate, evaluated once, handed down —
    /// the slots decide nothing.
    ///
    /// <para>THE GATE HAS EXACTLY THREE TERMS, and each is the OWNER's or the game's, never the
    /// viewer's: the owner's permission bit off the wire, the FACE THIS RECESS ACTUALLY DREW
    /// (<see cref="_slotFaceMask"/>, as <see cref="SeatSlots"/> seated it), and an actor to look
    /// cards up on. The receiver's own <c>[Cards] GameCardParticles</c> is NOT a term; a viewer who
    /// wants none of a peer's board switches the board off ([Net] RemoteBoards), which is the
    /// standing ruling this feature was built under.</para>
    ///
    /// <para>THE SECOND TERM USED TO BE <c>showFronts</c> AND THAT WAS A DEFECT IN THE ONE CASE
    /// THIS FEATURE IS MOST ABOUT (2026-09-07 review, item B4). <c>showFronts</c> is the POPULATION's
    /// verdict; a recess drawing a burn-exception front is drawing it under the CARD's verdict, and
    /// the two differ by exactly the set of burning cards. So the one burn the carve-out exists to
    /// draw face-up — a card burning inside its owner's short rest — was the one burn that got no
    /// smoke, while the ARMED line below asserted the opposite. Reading the mask instead is also
    /// the ModBuild 84 rule kept rather than broken: it is what the slot was HANDED, not a second
    /// derivation of the same question that could disagree with it.</para>
    ///
    /// <para>WHY THE FACE BOUNDS IT rather than merely accompanying it: a plume hanging on ONE
    /// specific face-DOWN recess would name WHICH card the owner is doing something to — the exact
    /// leak the backs-only rule exists to prevent — and a recess showing a front has no such secret
    /// left to give away. the central plume dispatcher then adds a second, structural
    /// guard on top (the face on the slab must have been cloned from the very widget it reads the
    /// effect off).</para>
    ///
    /// <para>THE ARMED LINE IS THE POINT OF THE THREE-STATE LOG. Read together with the slot's own
    /// "Remote card plume SOURCE" line and <see cref="RemoteCardPlume"/>'s spawn line, a hardware
    /// log states which of three things is missing rather than leaving "nothing happened"
    /// indistinguishable from "it worked": no ARMED line = the owner's bit is off or the gate is
    /// shut; ARMED with no SOURCE line = the peer's hand holds no live widget for the seated card
    /// (their face is a pooled borrow, and the trigger would need a wire field); SOURCE with no
    /// spawn line = the widget is there but the game runs no card effect on it on this client, so
    /// again the TRIGGER is what is missing, not the effect.</para>
    /// </summary>
    private void TickSlotPlumes(CPlayerActor? actor)
    {
        bool armed = _gameCardParticlesOn && actor != null;
        bool allowed = armed && _slotFaceMask != 0;

        if (allowed && !_plumeArmedLogged)
        {
            _plumeArmedLogged = true;
            VRLog.Info("Net", $"Remote card plume ARMED [player {_owner.PlayerId}]: this owner has " +
                              "[Cards] GameCardParticles ON (wire id 236) and at least one of their " +
                              "round-card recesses is drawing a FRONT, so a burn/lost/discard on a " +
                              "card lying in that recess hosts the game's own CardSmoke on that slab " +
                              "(RemoteCardPlume). This is the PLAYED/round-slot half of the bit — " +
                              "RemoteHandFan.TickMirroredPlumes is the hand half, and the owner's " +
                              "own picture here comes from Cards.BurnCardFx on their docked VRCard. " +
                              "This line with no 'Remote card plume SOURCE' line means no live " +
                              "AbilityCardUI backs the seated card; SOURCE with no spawn line means " +
                              "the widget exists but runs no card effect on this client. Either way " +
                              "the TRIGGER is what would need a wire field, never the effect.");
        }

        // Native plume presentation is dispatched centrally from owner stream 51.

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
        // The seat depth is CONSTRUCTOR-applied like every other dial on this board, and it needs no
        // rebuild trigger of its own: the revision latch above already tears the board down and
        // rebuilds it on any record-28 change, which is exactly what moving this dial produces.
        _slotCardInset = _owner.BoardTuning.SlotCardInset;
        // …and the owner's say-so for the GAME's own card plume (wire id 236). Read on the same
        // seam and for the same reason: any record-28 change tears this board down, so a
        // constructor-time read is a live read. It is a PERMISSION, never a picture — this client
        // instantiates the prefab itself (RemoteCardPlume), and the VIEWER's own copy of the dial
        // is deliberately never consulted (Cards.CardDustFx.Permission).
        _gameCardParticlesOn = _owner.BoardTuning.GameCardParticlesOn;

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
                Vector3 seatBoardLocal = SlotCardSeatLocal(i);
                Vector3 seatOnAnchor = anchor.InverseTransformVector(
                    _root.transform.TransformVector(seatBoardLocal));
                // The ROUND RECESSES take the owner's materialise (see RemoteBoardCard's
                // constructor): a card really is played into one and taken out of it, so it fades
                // in and crumbles out on the owner's own curve instead of popping. The avatar is
                // also where the dust's permission bit (wire id 233) is read from.
                _cards[i] = new RemoteBoardCard(anchor, seatOnAnchor, cardW, cardH, _owner);
            }
        }
        else
        {
            // Fallback frame: the flat authored layout hangs off the board root, so the board-local
            // seat adds directly — no conversion, same reason the glows need none.
            _cards[0] = new RemoteBoardCard(_root.transform,
                SlotLocal(0) + SlotCardSeatLocal(0), cardW, cardH, _owner);
            _cards[1] = new RemoteBoardCard(_root.transform,
                SlotLocal(1) + SlotCardSeatLocal(1), cardW, cardH, _owner);
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
        // flat card-back quad. Colors are the local stacks' verbatim; the SIZE is the OWNER's own
        // [Cards] CardWidth off the wire (record 28 id 70), never this viewer's copy of the dial —
        // the line that used to stand here said "sizes come from the authored Defaults … regardless
        // of local tuning" and was half right for the wrong reason: it correctly refused the
        // VIEWER's config and silently refused the OWNER's with it. See PileCounter._cardWidth.
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

        // Full-parity panels. The initiative TRACK, the OBJECTIVES panel and — since 2026-09-06 —
        // the ELEMENT BOARD mirror the game's OWN widgets (RemoteWidgetMirror) and keep their
        // mod-drawn versions only as fallbacks; the rest stay mod-drawn and fed from the LOCAL
        // model — see the class note.
        _objectives = new RemoteObjectivesPanel(contentParent, _layout);
        _elements = new RemoteElementStrip(_owner.PlayerId, contentParent, _layout);
        _status = new RemoteStatusReadouts(contentParent, _layout);
        _pickBanner = new RemotePickBanner(contentParent, _layout);
        _boardTooltip = new RemoteBoardTooltip(contentParent, _layout, _owner.BoardTuning);
        _active = new RemoteActiveCards(_owner, _owner.PlayerId, contentParent, _layout);
        _track = new RemoteInitiativeTrack(contentParent, _layout, _owner.BoardTuning);
        // THE GLOW BASE, NOT AnchorLocalLive (2026-08-27). Those two were the same vector until this
        // round and are not any more: AnchorLocalLive is the CARD's seat now (it carries the owner's
        // [Cards] SlotCardInset and the in-plane overlay term), while the furniture adds the in-plane
        // term itself and stacks its two proud offsets on the overlay plane. Handing it the seat
        // would double-count x/y AND pull both glows down to the card's depth.
        _furniture = new RemoteBoardFurniture(_root.transform, _owner.BoardTuning, _tray,
            SlotAnchorBoardLocal(0), SlotAnchorBoardLocal(1),
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

    /// <summary>Last <c>exhausted</c> verdict this board applied; null = never applied one, so the
    /// first tick states it either way and a session with no death is not silent.</summary>
    private bool? _appliedExhausted;

    /// <summary>
    /// A DEAD BOARD HAS NO CARDS — the remote half of the rule, at the one place this board's card
    /// populations are written.
    ///
    /// <para>USER, hardware 2026-09-05, item 13: "Wenn ein Character tot ist ... sollen dort gar
    /// keine Karten mehr liegen." The 1:1 rule makes that a statement about BOTH representations:
    /// whatever the owner's own board becomes, their mirrored board here must become too.</para>
    ///
    /// <para>WHY THE NULL ACTOR IS NOT ENOUGH, and this is the whole reason this method exists.
    /// <see cref="RemoteBoardFocus.DisplayedActor"/> now answers null for an exhausted owner, which
    /// by itself drains every MODEL-read population (the active column, the pile-front fans, the
    /// hand fan's front art). It reaches NONE of the WIRE-FED ones: the slot-occupancy nibble, the
    /// three pile counts and the items cue are LATCHED on this client at the last value their owner
    /// sent, and <see cref="RefreshGlobalContent"/> — the actorless path — deliberately re-paints
    /// all three every cadence, because the case it was written for is a peer who has not been
    /// given a character YET. A board cleared only through the actor would therefore still stand
    /// there with two anonymous card backs and "ABGEWORFEN 2 / VERBRANNT 6". That is the recorded
    /// failure shape (a cascade clears only what it lists) and it is why the clear is enumerated
    /// here rather than inferred.</para>
    ///
    /// <para>THE ENUMERATION — every card population a remote board can carry, and what happens to
    /// each: round-card recesses ⇒ forced empty at <see cref="SeatSlots"/> (the wire mask is
    /// overridden, not consulted); hosted faces ⇒ dropped with <see cref="BlankCardFaces"/>; active
    /// cards + their title ⇒ blanked and the column deactivated; discard / burnt / items stacks ⇒
    /// hidden WITH their captions (<c>PileCounter.SetShown</c>); the items usable cue ⇒ off; the
    /// mirrored hand fan and the pile-front arcs ⇒ already empty, because their content is the
    /// owner's own fan, which the owner's board cleared at ITS choke point
    /// (<c>Board.CharacterFocus.BoardCarriesCards</c>), and their FACE gate reads the null actor.
    /// What is deliberately NOT taken: the board surface, the objectives panel, the element strip,
    /// the initiative track, the round readout and the furniture's keycaps — none of them is a
    /// card, and a dead player still watches the scenario from their board.</para>
    /// </summary>
    private void ApplyExhaustedCardRule(bool exhausted)
    {
        bool? was = _appliedExhausted;
        bool edge = was != exhausted;

        // BEFORE — read off the surfaces themselves, not off the verdict that is about to change
        // them, so the line reports what was DRAWN rather than what was intended.
        int slotsBefore = CountBits(_slotOccupiedMask);
        int activeBefore = _active != null ? _active.Count : 0;
        int discardBefore = _piles[0] != null ? _piles[0]!.Shown : 0;
        int burntBefore = _piles[1] != null ? _piles[1]!.Shown : 0;
        int itemsBefore = _piles[2] != null ? _piles[2]!.Shown : 0;
        int stacksBefore = (_piles[0]?.IsShown == true ? 1 : 0) + (_piles[1]?.IsShown == true ? 1 : 0)
                           + (_piles[2]?.IsShown == true ? 1 : 0);

        if (exhausted)
        {
            BlankCardFaces();               // round-slot faces + active-column faces + the masks
            _active?.SetActive(false);      // …and the column itself, so its title goes with it
            for (int i = 0; i < _piles.Length; i++)
                _piles[i]?.SetShown(false); // slabs, digits AND caption
            _piles[2]?.SetUsableCue(false); // an exhausted character can play no item
        }
        else
        {
            for (int i = 0; i < _piles.Length; i++)
                _piles[i]?.SetShown(true);
            // The active column re-activates itself from RefreshContent's own count test; forcing
            // it on here would raise an empty grid on every board that simply has no active cards.
        }

        // THE STATE IS APPLIED UNCONDITIONALLY, THE LINE ONLY ON THE EDGE. The clear above must not
        // hang off a change gate: a gate is a latch, and a latch that is the ONLY thing keeping a
        // population cleared re-opens the defect the moment anything else re-shows it. Every call
        // above is self-early-returning and allocation-free once applied (BlankCardFaces is already
        // driven per frame from the hidden-board path for exactly that reason), so re-asserting it
        // on the 4 Hz content cadence costs a handful of bool compares.
        if (!edge)
            return;
        _appliedExhausted = exhausted;

        int slotsAfter = CountBits(_slotOccupiedMask);
        int activeAfter = _active != null ? _active.Count : 0;
        int stacksAfter = (_piles[0]?.IsShown == true ? 1 : 0) + (_piles[1]?.IsShown == true ? 1 : 0)
                          + (_piles[2]?.IsShown == true ? 1 : 0);

        // HW-VERIFY
        VRLog.Note("Net", $"EXHAUSTED BOARD [{_owner.PlayerId}]: "
            + (exhausted ? "CLEARED (remote)" : was.HasValue ? "RESTORED (remote)" : "ARMED (remote)")
            + $" — verdict {(was.HasValue ? was.Value.ToString() : "unset")} -> {exhausted}. "
            + $"CARD POPULATIONS BEFORE -> AFTER: round-card recesses {slotsBefore} -> {slotsAfter} "
            + $"(the owner's own wire nibble still says 0x{_owner.BoardSlotMask:X}, known="
            + $"{_owner.SlotOccupancyKnown} — a LATCHED fact, which is why the recesses are forced "
            + $"rather than read); active cards {activeBefore} -> {activeAfter}; pile stacks drawn "
            + $"{stacksBefore} -> {stacksAfter} (discard={discardBefore}, burnt={burntBefore}, "
            + $"items={itemsBefore} at the moment of the change; wire counts "
            + $"{(_owner.HasPileCounts ? $"{_owner.PileDiscardCount}/{_owner.PileBurntCount}/{_owner.PileItemsCount}" : "absent")}); "
            + $"items usable cue {(exhausted ? "forced off" : "left to the wire")}. "
            + "READ IT LIKE THIS: after a CLEARED line every one of those AFTER numbers must be 0 "
            + "and the stacks-drawn count 0 — a non-zero one names the population that survived the "
            + "clear, which is the whole defect. The BOARD, the objectives, the element strip, the "
            + "initiative track and the keycaps are NOT cards and are deliberately still there. "
            + "The mirrored hand fan and the pile-front arcs are not counted here because they "
            + "carry no content of their own: they are the owner's fan, cleared at the owner's own "
            + "choke point, and their front gate reads this same null actor.");
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
        _slotPickSeatMask = 0;
        _slotPickBackMask = 0;
        _pickSeatRule = "no recess needed a record-39 seat this frame";
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
        _elements?.Destroy();
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
    /// flight lands on. Sized from the OWNER's own <c>[Cards] CardWidth</c> (extension record 28,
    /// id 70) × <c>PileViewer.PileStack.SlabFactor</c>, with the shipped default as the fallback for
    /// a sender who has not moved that dial — see <see cref="PileCounter._cardWidth"/>, which also
    /// records the RETIRED policy this paragraph used to state ("the OWNER's [Cards] tuning is local
    /// config and deliberately not applied"). It has been false since record 28 was paged, and it is
    /// the VIEWER's config — never the owner's — that this board must refuse. Collider-free by
    /// construction.
    ///
    /// The count is PUBLIC information — vanilla lets any player open ANY other player's full card
    /// overview straight off the initiative track (<c>InitiativeTrackPlayerAvatar.OnClick</c> →
    /// <c>CardsHandManager.ToggleViewAllCards</c>) — so it needs no reveal gate. Change-gated writes:
    /// a per-tick <c>TMP.text</c> assignment re-triggers auto-size layout.
    /// </summary>
    private sealed class PileCounter
    {
        /// <summary>
        /// The OWNER's own <c>[Cards] CardWidth</c> (extension record 28, id 70) — the metric their
        /// stack is built from, seeded in the constructor and held against the shipped default by
        /// scripts/check-remote-defaults.py.
        ///
        /// <para>IT WAS A <c>const</c> UNTIL THIS ROUND, and the class doc above stated the reason as
        /// standing policy: "the OWNER's [Cards] tuning is local config and deliberately not
        /// applied". That policy was retired when record 28 was PAGED — id 70 has carried this dial
        /// for builds, and every other remote card surface already reads it (RemoteHandFan,
        /// RemoteItemFan, RemoteBrowserFan, RemoteCardFx, RemoteActiveCards). This board was the last
        /// surface still drawing a peer's cards at a number their owner never chose. Exactly the
        /// defect RemoteActiveCards.LegacyCardW spells out, and it survived for the same reason: the
        /// coverage guard watches DIALS and cannot see that the metric underneath them is a
        /// constant.</para>
        ///
        /// <para>MAGNITUDE. The dial is bounded 0.03..0.15 m against a 0.0635 m default (user ruling
        /// 2026-08-13), so the frozen footprint was correct only for an untuned owner and off by up
        /// to 2.4x once they touched it: 39.4 × 54.5 mm here at the shipped default, against
        /// 18.6 × 25.8 mm for an owner at the low bound and 93 × 129 mm at the high one. And it is
        /// not only the slabs — the count/caption fits, the ember emitter's box and the ring seed are
        /// all derived from this metric, exactly as the owner derives theirs.</para>
        ///
        /// <para>A CONSTRUCTOR-TIME READ IS A LIVE READ here: any record-28 change tears this board
        /// down and rebuilds it (<c>_builtTuningRevision</c>, see the rebuild ladder in Tick), which
        /// is the same seam <c>_slotCardInset</c> and the cue dials below are taken on.</para>
        /// </summary>
        private readonly float _cardWidth = Defaults.CardWidth;

        /// <summary>Slab footprint — the owner's card size × <c>PileViewer.PileStack.SlabFactor</c>
        /// (0.62), the same product their own <c>PileStack.Create</c> forms. 39.4 mm at the shipped
        /// default; the height keeps the game's 63.5:88 card aspect (54.5 mm), exactly as
        /// <c>CardsConfig.CardHeight</c> derives it on the owner's side.</summary>
        private float SlabW => _cardWidth * PileViewer.PileStack.SlabFactor;
        private float SlabH => SlabW * (88f / 63.5f);

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

            // …and the METRIC the whole stack is measured in (record 28, id 70), taken on the same
            // seam and for the same reason. Clamped to the dial's OWN shipped bound rather than to a
            // bare Mathf.Max: [Cards] CardWidth is bounded 0.03..0.15 m on the sender, so anything
            // outside that window is a corrupt or hostile packet and must not be able to draw a
            // metre-wide pile on this client's board. An absent field already resolves to
            // Defaults.CardWidth in RemoteBoardTuning, so the clamp only ever sees a real value.
            _cardWidth = Mathf.Clamp(tuning.CardWidth, 0.03f, 0.15f);

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
                    // THE LIGHTING FIX, MIRRORED (round 15 — CardMesh.EmissionFloorFactor). The
                    // owner's own PileStack.Create floors these four materials and this mirror did
                    // not, which is not a tuning difference but an unfinished shader setup: Standard
                    // outputs albedo × incoming light and the VR scenes are dark, so a peer's stacks
                    // rendered at the ~0.16 × albedo residual karten4 measured while the owner's
                    // rendered at albedo × (1.00 floor + 0.16 residual) — a ~7x luminance gap, at
                    // the SHIPPED defaults, on every board but your own. It is a shader-completion
                    // step both sides must perform, not owner state, so it owes no wire field: the
                    // floor is derived from the material's own albedo, which is already identical on
                    // both sides — the per-pile tints handed to this constructor are the local
                    // stacks' verbatim (see EnsureBuilt). No-op on the Sprites/Default fallback,
                    // which is unlit and has no hole to floor.
                    CardMesh.ApplyEmissionFloor(material);
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

        /// <summary>
        /// Show / hide the whole stack — slabs, count digits AND caption, because they are all
        /// children of this one root. The receiver-side twin of <c>PileViewer.SetVisible</c>, which
        /// hides its three <c>PileStack_*</c> objects the same way, so an emptied board never
        /// leaves a stranded "ABGEWORFEN" over nothing. Change-gated; no debounce is needed here
        /// (the verdict that drives it is a character's death, not a one-frame hand dropout).
        /// </summary>
        public void SetShown(bool shown)
        {
            if (_root != null && _root.gameObject.activeSelf != shown)
                _root.gameObject.SetActive(shown);
        }

        /// <summary>Is this stack currently drawn? Read for the evidence line's BEFORE/AFTER
        /// counts.</summary>
        public bool IsShown => _root != null && _root.gameObject.activeSelf;

        /// <summary>The count this stack is displaying, or 0 before its first write — diagnostics
        /// only (the evidence line's BEFORE reading).</summary>
        public int Shown => _shown == int.MinValue ? 0 : _shown;

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
                {
                    _topMaterial.color = want;
                    // The emission floor is DERIVED from the albedo tint, so a tint change must
                    // re-sync it or the greyed-out top slab would keep self-illuminating at the full
                    // colour and read brighter than the pile it caps. Change-gated with the colour
                    // write and idempotent — the same pairing PileStack.SetCount uses.
                    CardMesh.ApplyEmissionFloor(_topMaterial);
                }
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
