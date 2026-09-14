using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// Plays a remote player's CARD ANIMATIONS locally (user report 6). One event byte from the extras
/// packet (<see cref="NetProtocol.FlagCardFx"/>, decoded by <see cref="NetCardFx"/>) says which two
/// pieces of that player's VR furniture the card travelled between; this class resolves both
/// anchors against the SENDER'S OWN synced poses and flies a card slab along the same arc the
/// local <see cref="VRCard.FlyToPile"/> uses — so a peer sees the card slide into their discard /
/// burnt stack, glide back into their hand fan, or dock into a play slot, at the moment it really
/// happened.
///
/// <para>THE SLAB CARRIES THE CARD'S REAL FRONT when this client can name the card, which since
/// ModBuild 461 is every flight out of a round recess (2026-09-06 report item 5: "Die Animationen
/// bei denen die Karten in den jeweiligen Stapel gehen zeigen beim remote board die Karten mit der
/// Rückseite als Vorderseite, das ist NICHT was der Spieler sieht und ist daher ein 1:1 Bruch").
/// The owner's own flying card is a live <see cref="VRCard"/> that was lying FACE-UP in the recess,
/// and <see cref="VRCard.FlyToPile"/> locks that rotation for the whole arc — so a back was a 1:1
/// breach on both the face and the pose, and both are fixed together (see
/// <c>SlabRotation</c>, which since 2026-09-07 holds the owner's board rotation for EVERY flight
/// rather than only for a faced one — the faceless arm billboarded at the LOCAL viewer's camera,
/// the last client-local geometry term on this board).</para>
///
/// WHY A LOCAL REPLAY AND NOT A POSE STREAM: the flight lasts ~0.4 s. Streaming it would need the
/// full 15 Hz pose channel for its whole duration (~20 B per packet) and would still stutter under
/// loss; replaying it from a 2-byte event costs one packet and is immune to jitter afterwards.
///
/// ANTI-CHEAT: NO CARD IDENTITY IS EVER TRANSMITTED, and that is unchanged — the wire payload is
/// still two semantic anchor ids. What changed in ModBuild 461 is what may be RENDERED: the front is
/// this client's own read of the card its OWN control-board mirror was already drawing face-up in
/// that recess, claimed through <c>RemoteControlBoard.TryTakeDepartedFace</c>, which only ever holds
/// a face this board legitimately showed while <see cref="RevealGate"/> was open — and the gate is
/// re-asked at flight time on top. A flight this client cannot name that way is the card BACK every
/// build before it drew, exactly like <see cref="RemoteHandFan"/>'s default and the held-card slab.
///
/// VISIBILITY: gated on <see cref="RemoteBoardGate.ShowBoardSurface"/> (the shared
/// <see cref="NetModule.RemoteBoards"/> predicate) — every anchor except the hand fan is board
/// furniture, so a player who has chosen not to see a peer's board (Off, or ActionPhaseOnly while
/// that board is hidden through the secret selection phase) does not get cards flying to invisible
/// places either. Also requires the sender's board pose (<c>owner.HasBoard</c>, which the gate
/// checks).
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY — costs 2 wire bytes per event (extras <c>FlagCardFx</c>:
/// <c>fxSeq</c> + <c>fxEndpoints</c>). Flight TRANSFORMS are DELIBERATELY-NOT transmitted: the
/// endpoints are semantic <see cref="CardFxAnchor"/> ids the receiver resolves against the sender's
/// OWN synced hand / board pose, which is what turns ~20 B × 15 Hz for a flight's duration into
/// 2 bytes once. Card identity never rides it either. See INVARIANTS-Net-Rig.md "Net — content
/// classification".</remarks>
internal sealed class RemoteCardFx
{
    /// <summary>Concurrent flights (a turn-clear can launch both round cards at once). Beyond this
    /// the oldest slab is recycled — the animation is cosmetic, never a queue that may back up.</summary>
    private const int MaxFlights = 6;

    /// <summary>Arc height as a fraction of the travelled distance —
    /// <see cref="VRCard.FlyArcHeightFraction"/>, term for term.
    ///
    /// <para>THE COMMENT HERE WAS THE DEFECT (final 1:1 review). It read "the value
    /// VRCard.FlyArcHeightFraction uses locally, so the bow matches" and stood over a
    /// <c>0.28f</c> — while <c>VRCard.FlyArcHeightFraction</c> has been <c>0.55f</c> since it was
    /// raised from 0.35 for user issue 3 ("a taller, clearly followable arch rather than a flat
    /// pass"). The claim was never true at 0.55 and the number was never re-derived, so every
    /// mirrored play, discard and burn flight bowed at 0.28/0.55 = 50.9 % of the height its owner
    /// watched. On a 0.80 m hop at board scale 1 — long enough that the fraction beats the
    /// floor on BOTH sides — the owner's card peaked 440 mm above the chord and the peer's
    /// 224 mm: a skim across the board rather than an arch over it, which is exactly the flat
    /// pass issue 3 removed locally. (Below ~0.47 m of travel the shared floor term hides part of
    /// the gap, which is why short hops looked merely a little low and long ones looked wrong.)
    /// A code LITERAL on both sides, not a dial: no wire field is owed, the two numbers simply
    /// have to be the same number.</para>
    ///
    /// <para>The FLOOR term beside it (<see cref="MinArcCardHeights"/>) does NOT differ and is
    /// left alone: <c>CardsDriver.BoardArcMin</c> is <c>boardScale × CardsConfig.CardHeight ×
    /// 1.5</c>, and the max below already feeds it the OWNER's synced card height (see
    /// <see cref="CardHeight"/>) times the owner's board scale — checked term for term against
    /// <c>CardsDriver.4.Rebuild.BoardArcMin</c> while fixing this fraction.</para></summary>
    private const float ArcFraction = 0.55f;

    /// <summary>Absolute minimum arc peak in board-scaled metres, mirroring
    /// <c>CardsDriver.BoardArcMin</c> (~1.5 card heights) so a short hop still clears the board.</summary>
    private const float MinArcCardHeights = 1.5f;

    private readonly RemoteAvatar _owner;

    private sealed class Flight
    {
        public GameObject? Go;
        public RemoteCardArt? Art;
        public Vector3 From;
        public Vector3 To;
        public Vector3 ArcUp;
        public Quaternion Rotation;
        public bool ActiveTargetCaptured;
        public long Generation;
        public bool SourceTransferred;
        public int TransferredCardId = int.MinValue;
        public int HandCardId = int.MinValue;
        public float Arc;
        public float Elapsed;
        public float ArtworkWait;
        public byte Endpoints, Flags;
        public bool Active;
        /// <summary>True while this slab is carrying the real card FRONT rather than a back. It no
        /// longer decides the slab's ORIENTATION: that forked here until 2026-09-07 and the faceless
        /// arm billboarded at the LOCAL viewer's camera — see <see cref="SlabRotation"/>.</summary>
        public bool HasFace;
        public ScenarioRuleLibrary.CAbilityCard? FaceCard;
        public ScenarioRuleLibrary.CPlayerActor? FaceActor;
        public bool ShortRestBurn;

        /// <summary><c>CAbilityCard.CardInstanceID</c> of the card this slab is carrying INTO THE
        /// ACTIVE MATRIX, or <see cref="int.MinValue"/> for every other flight. See
        /// <see cref="IsFlyingToActive"/> and the initial cell resolve in <see cref="Tick"/>.
        /// </summary>
        public int ActiveCardId = int.MinValue;

        /// <summary>What this slab's card BODY is wearing on its FRONT fan (true = the card back) —
        /// the edge gate for <see cref="SetFrontFace"/>. Seeded true because that is what
        /// <see cref="Acquire"/> builds it with.</summary>
        public bool WearsBack = true;
        public ScenarioRuleLibrary.CPlayerActor? SourceActor;

        /// <summary>The owner's own card WIDTH at this flight's origin and at its destination, in
        /// metres. The slab ramps between them across the arc — see <see cref="WidthForAnchor"/>
        /// and the scale write in <see cref="Tick"/>.</summary>
        public float FromWidth = RemoteHandFan.DefaultCardWidth;
        public float ToWidth = RemoteHandFan.DefaultCardWidth;
    }

    private readonly struct PendingFlight
    {
        internal PendingFlight(byte endpoints, byte flags, CardFlightSource? source, long generation)
        { Endpoints = endpoints; Flags = flags; Source = source; Generation = generation; ReceivedAt = Time.unscaledTime; }
        internal readonly long Generation;
        internal readonly byte Endpoints, Flags;
        internal readonly CardFlightSource? Source;
        internal readonly float ReceivedAt;
    }
    private readonly List<PendingFlight> _pending = new(MaxFlights);
    private const float ResolveSeconds = 2f;

    private readonly List<Flight> _flights = new(MaxFlights);
    private GameObject? _root;
    private int _played;   // diagnostics: how many flights this avatar has played

    // ---- THE FLYING CARD'S SIZE (extension record 28 id 70 + CardFace.VisibleFaceRect) ---------
    //
    // The slab used to be built at the flat nominal 63.5 x 88 mm, which is wrong on BOTH terms the
    // owner's own flying card is drawn from:
    //
    //   1. THE PRINTED RECT. VRCard scales its backing mesh to the rectangle the face actually
    //      PAINTS (SetCanvasSize → CardFace.VisibleFaceRect: 54.04 x 82.72 mm inside the nominal
    //      card), so the owner's card in flight is 54.04 mm wide and this slab was 63.5 — 17.5 %
    //      too wide, the same defect RemoteHandFan fixed for the fan slabs and CardFace.cs
    //      documents in full. (When this was written a flight slab wore the card BACK on both
    //      submeshes, so only the SIZE was wrong. Since ModBuild 461 it can host a real print too,
    //      and the print/body relationship is the one RemoteHeldCardFace.BodyBox/ArtBox sets out —
    //      the face overlay hangs off the flight ROOT at the NOMINAL box, not off the squashed Body
    //      child, so RemoteCardArt's own letterbox-and-inset lands it flush on this rect.)
    //   2. THE OWNER'S CARD WIDTH. [Cards] CardWidth rides record 28 and every other remote card
    //      surface reads it; this one held RemoteHandFan.DefaultCardWidth, the NOMINAL, so a peer
    //      who had tuned their cards watched their own card fly at one size and everyone else
    //      watched the same flight at this client's.
    //
    // Both land as scales, never as a new mesh box: CardMesh.AttachBody keys its shared shaped
    // meshes on (kind, w, h), so cutting one per owner card width would multiply that cache by the
    // number of peers for a scale the transform can carry for free.

    /// <summary>The owner's own <c>[Cards] CardWidth</c> (record 28, id 70), re-read on each event —
    /// a handful per turn, so no revision latch is worth its complexity here. The INITIALISER is the
    /// shipped default, which is what a pre-record or untuned peer's flight is drawn at (held against
    /// <c>[Cards] CardWidth</c> by scripts/check-remote-defaults.py).</summary>
    private float _cardWidth = Defaults.CardWidth;

    /// <summary>…and their card HEIGHT, in the game's own 88:63.5 ratio exactly as
    /// <c>CardsConfig.CardHeight</c> derives it. Used for the arc FLOOR, which the owner computes as
    /// <c>boardScale × CardHeight × 1.5</c> (<c>CardsDriver.BoardArcMin</c>) — so a peer with bigger
    /// cards gets the bigger minimum arch they do.</summary>
    private float CardHeight => _cardWidth * (88f / 63.5f);

    // WidthRatio (_cardWidth / DefaultCardWidth) USED TO LIVE HERE and is gone rather than left
    // unused: the slab no longer has ONE width. It has an origin width and a destination width and
    // ramps between them, exactly as VRCard.FlyToPile ramps its own scale — see WidthForAnchor and
    // the block in Play(). A single ratio is the shape that produced report item 8's "kleine Karte
    // in der Karte".

    public RemoteCardFx(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ play --

    /// <summary>
    /// Start one flight for a decoded event byte. Silently skipped when the endpoints cannot be
    /// resolved (no board pose yet / remote boards hidden / hand not tracked) — a missed cosmetic
    /// flight is always better than a card arcing to the world origin.
    /// </summary>
    public void Play(byte endpoints, byte flags = 0, CardFlightSource? source = null)
    {
        long generation = _owner.NextFlightOwnership();
        if (TryPlay(endpoints, flags, source, generation)) return;
        if (_pending.Count >= MaxFlights) _pending.RemoveAt(0);
        _pending.Add(new PendingFlight(endpoints, flags, source, generation));
    }

    private bool TryPlay(byte endpoints, byte flags, CardFlightSource? source, long generation)
    {
        // THE WIRE FRAME. Play is called synchronously from RemoteAvatar's packet apply the moment
        // the FX sequence changes, so this IS the frame the wire named the flight — see the TIMING
        // clause of the FLIGHT FACE line for what a non-zero difference would mean.
        int wireFrame = Time.frameCount;
        CardFxAnchor from = NetCardFx.From(endpoints);
        CardFxAnchor to = NetCardFx.To(endpoints);

        // VISIBILITY ([Net] RemoteBoards — audit 2026-07). This used to check ONLY for Off, which
        // left ActionPhaseOnly broken: during the secret selection phase the peer's whole board is
        // hidden, yet the very flights that happen then (a card docking into a play SLOT) still
        // played — a lone card back arcing into empty space. Every anchor except the hand fan IS
        // board furniture, so a flight that touches one now needs the board surface to be visible at
        // all; a pure hand-fan flight is avatar content and is never gated.
        bool touchesBoard = from != CardFxAnchor.HandFan || to != CardFxAnchor.HandFan;
        if (touchesBoard && !RemoteBoardGate.ShowBoardSurface(_owner))
        {
            // Event-driven (a handful per turn at most), so this is greppable evidence the setting
            // reached the FX path without being a per-frame line. Grep: "Remote card FX".
            VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} SKIPPED — " +
                              (RemoteBoardScenarioGate.Open
                                  ? $"[Net] RemoteBoards = {RemoteBoardGate.Mode} hides that peer's board "
                                  : "this client is not in a scenario, so no peer's board exists at "
                                    + "all (grep 'Remote board scenario gate' — the dial is not what "
                                    + "shut this) ") +
                              "right now, and this flight starts or ends on their board furniture.");
            return true;
        }
        if (!TryResolve(from, out Vector3 a) || !TryResolve(to, out Vector3 b))
        {
            VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} SKIPPED " +
                              "(endpoint unresolved — no synced board pose or the hand is not tracked).");
            return true;
        }

        ScenarioRuleLibrary.CAbilityCard? departed = null;
        Vector3 departedCell = default;
        // MB490: the cosmetic packet can beat the model's destination-list update. Wait for
        // this exact previous active seat; falling through would use an unrelated recess face
        // and fly from the active column's centre while the source card remains in its cell.
        if (from == CardFxAnchor.Active && !RemoteActiveDepartures.TryTake(_owner.PlayerId,
            source.HasValue ? RemoteBoardFocus.ActorById(source.Value.ActorId)
                : RemoteBoardFocus.DisplayedActor(_owner, out _), to, source, out departed, out departedCell))
            return false;

        ScenarioRuleLibrary.CAbilityCard? returning = null;
        if (to == CardFxAnchor.HandFan && source.HasValue && source.Value.Count > 0)
        {
            var hand = RemoteBoardFocus.ActorById(source.Value.ActorId)?.CharacterClass.HandAbilityCards;
            if (hand == null || hand.Count != source.Value.Count || source.Value.Seat >= hand.Count)
                return false;
            returning = hand[source.Value.Seat];
            // An open fan owns an actual ordered seat; a closed fan lands at the palm anchor
            // already resolved above. Freeze that endpoint just as VRCard.FlyFromPile does.
            if (_owner.HandCardCount > 0 && !_owner.HandFan.TryFlightSeat(returning.CardInstanceID, out b))
                return false;
        }

        Flight f = Acquire();
        if (f.Go == null)
            return true;
        // A RECYCLED SLAB MUST NOT INHERIT THE LAST CARD'S IDENTITY. Acquire() hands back a parked
        // flight, and an ActiveCardId left on it would blank a matrix cell for a card that is not in
        // the air. ResolveFace re-stamps it below when this flight really is one.
        f.ActiveCardId = int.MinValue;
        f.Generation = generation;
        f.SourceTransferred = false;
        f.TransferredCardId = int.MinValue;
        f.HandCardId = returning?.CardInstanceID ?? int.MinValue;
        f.SourceActor = source.HasValue ? RemoteBoardFocus.ActorById(source.Value.ActorId)
            : RemoteBoardFocus.DisplayedActor(_owner, out _);
        f.Endpoints = endpoints; f.Flags = flags; f.ArtworkWait = 0f;
        f.HasFace = false;
        f.FaceCard = null;
        f.FaceActor = null;
        f.ShortRestBurn = CardFlightVisibility.Covered(flags);
        f.Art?.HideFront();

        // The owner's own card size for THIS flight (see the _cardWidth block). Guarded above zero
        // because a wire value is never trusted; the struct's own fallback is already the default.
        _cardWidth = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth);

        // The board AS DRAWN, not the wire target — see DrawnBoardScale. Read once here for
        // the arc FLOOR and the slab's opening size; Tick re-reads it every frame.
        float scale = DrawnBoardScale;
        f.From = a;
        f.To = b;
        // WORLD up, never the owner's BOARD up. This used to be `_owner.BoardRotation * Vector3.up`,
        // which is the argument CardsDriver passes and which VRCard.FlyToPile DELIBERATELY THROWS
        // AWAY — its own sentence, kept here verbatim so the next reader does not "restore" it:
        // "the bow always lifts along WORLD up — toward the player's head / the ceiling — regardless
        // of how the board is tilted. The caller's board-up `arcUp` is intentionally ignored so a
        // tilted board can never lean the arch sideways or into the table." The board root is posed
        // Euler(90 - BoardTilt_{board}, 0, 0) (PlayTray.1.Core), so at the shipped BoardTilt = 30
        // degrees its local +Y stands 60 DEGREES off world up: a mirrored flight bowing along it
        // swung forward across the board face while its owner watched that card arch at the ceiling.
        f.ArcUp = Vector3.up;
        // CardsDriver.BoardArcMin, term for term: boardScale × the OWNER's CardHeight × 1.5. It read
        // this client's nominal card height before, so a peer with taller cards got a shallower
        // minimum arch than the one they were watching.
        f.Arc = Mathf.Max(CardHeight * MinArcCardHeights * scale,
                          Vector3.Distance(a, b) * ArcFraction);
        f.Elapsed = 0f;
        f.Rotation = SlabRotation;
        f.ActiveTargetCaptured = to != CardFxAnchor.Active;
        f.Active = true;
        // ─── THE SLAB IS THE SIZE THE OWNER'S CARD IS, AT BOTH ENDS OF THE ARC (2026-09-07,
        //     report item 8: "Die anderen Spieler am remote board sehen beim Flug kurz die offene
        //     ... kleine Karte in der Karte clippen, und dann zum stapel fliegen.")
        //
        // A "kleine Karte in der Karte" is exactly what a CONSTANT scale produces here, and this
        // line used to write one. The owner's own flight does not hold its size: VRCard.FlyToPile
        // captures `_flyFromScale = transform.localScale` — the DOCKED scale, PlayTray.SlotCardScale
        // — and ramps to `_flyToScale`, the pile slab's width, over the whole arc. So a card leaving
        // a round recess starts at the RECESS card's size on its owner's board, and a short rest's
        // sacrifice flying INTO the recess (Discard -> Slot0) grows into that seat. This mirror drew
        // one fixed size for the whole flight — the owner's HAND card width — so at the recess end
        // the slab was a different card size from the recess card sitting at the very same seat,
        // which is a small card inside a card, clipping through it, for the length of the arc.
        //
        // EVERY SIZE IS ALREADY ON THE WIRE AND ALREADY DRAWN BY THIS CLIENT — see
        // <see cref="WidthForAnchor"/>, which is where each anchor's source is written out. No new
        // wire field, and the 1:1 ruling names SIZE and ANIMATION explicitly.
        //
        // ─── THE PARAGRAPH THAT STOOD HERE DECLINED THE PILE END AND ITS REASON WAS FALSE
        //     (2026-09-07 1:1 re-audit). It read: "THE PILE END IS DELIBERATELY LEFT AT THE HAND
        //     WIDTH. The owner ramps to the PILE SLAB's width, and this client does not hold that
        //     number — nothing on the mirror draws a peer's pile at a known slab size, so writing
        //     one here would be inventing a value rather than mirroring one."
        //
        //     THIS CLIENT HOLDS THAT NUMBER AND DRAWS IT, IN TWO PLACES. RemoteControlBoard's
        //     PileCounter sizes the mirrored stack at `_cardWidth * PileViewer.PileStack.SlabFactor`
        //     and RemoteBrowserFan collapses its browse fan onto the same product. The owner's own
        //     PileViewer.TryGetPileWorld hands FlyToPile exactly
        //     `lossyScale * CardWidth * SlabFactor` (0.62), so a full-size slab was arriving on a
        //     stack this very board draws at 62 % — on EVERY discard and EVERY burn, and in reverse
        //     for a short rest's sacrifice flying Discard -> Slot0, which started at 100 % where its
        //     owner starts at 62 %. An omission argued from a premise nobody re-checked, which is
        //     why the premise is quoted here rather than deleted.
        f.FromWidth = WidthForAnchor(from);
        f.ToWidth = WidthForAnchor(to);
        // Board scale × the owner's card width AT THE ORIGIN; the Body child under it carries the
        // printed-face squash (see the _cardWidth block and Acquire), and Tick ramps this to the
        // destination width across the arc.
        f.Go.transform.localScale =
            Vector3.one * (scale * (f.FromWidth / RemoteHandFan.DefaultCardWidth));

        // ─── THE FACE (2026-09-06 report item 5) ────────────────────────────────────────────────
        string faceRule;
        if (returning != null)
            faceRule = DressFace(f, returning, "the recovered native hand seat", out string? returnRefusal)
                ?? returnRefusal ?? "BACK — recovered card artwork unavailable";
        else if (from == CardFxAnchor.Active && departed != null)
        {
            if (_owner.TryDrawnBoardPose(out Vector3 bp, out Quaternion br, out float bs))
                a = bp + br * (departedCell * bs);
            f.From = a;
            f.Arc = Mathf.Max(CardHeight * MinArcCardHeights * scale,
                Vector3.Distance(a, b) * ArcFraction);
            faceRule = DressFace(f, departed, "the original departed active cell", out string? refused)
                ?? refused ?? "BACK — original active card artwork unavailable";
        }
        else faceRule = ResolveFace(f, from, to, flags);

        // …AND WHAT THE BODY UNDER IT WEARS (user item 10, 2026-09-06 late). ResolveFace is this
        // surface's ONE front/back decision — every arm of it ends with f.HasFace either set or
        // left false — so this is the single point at which the body can be told, and there is no
        // second rule to keep in step. CardMesh.SetBodyFrontFace owns the rule and the fade-aware
        // write; a flight showing a BACK keeps the back on both submeshes, which is the whole of
        // the "do not put an edge ring around a card back" half.
        SetFrontFace(f, showsBack: !f.HasFace);

        f.Go.transform.SetPositionAndRotation(a, f.Rotation);
        TransferVisibleFlight(f);
        f.Go.SetActive(CanDrawFlight(f));

        _played++;
        VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} playing " +
                          $"({NetProtocol.CardFxSeconds:F2}s, arc {f.Arc:F3} m) — flight #{_played}.");
        // HW-VERIFY: the reading that did not exist while four flight curves did. Grep token:
        // FLIGHT CURVE. One line per mirrored flight (a handful per turn), at Note tier because the
        // co-player runs at the shipped default level and either machine can be the one watching.
        // It names the easing FUNCTION and samples it at t = 0.25, because every arc line on both
        // machines printed only the PEAK — identical under any symmetric ease, which is exactly why
        // eight rounds of matching arc readings never once contradicted a 0.8 m divergence. Compare
        // against the owner's own flight: he flies VRCard.SmootherStep + VRCard.FlyArcOffset, and
        // this line prints the values read back out of those same two functions.
        VRLog.Note("Net", $"FLIGHT CURVE [player {_owner.PlayerId}]: their {from} -> {to} flight, "
            + $"{NetProtocol.CardFxSeconds:F2}s. {RemoteFlightCurve.Describe(f.Arc)} SIZE ramps on "
            + $"the same eased term: {f.FromWidth * 1000f:F1} mm -> {f.ToWidth * 1000f:F1} mm.");
        // The DUPLICATE half of report item 5 is a flight from HERE landing on a board RemoteBurnFx
        // has already flown a burn on. Neither class can see the other's line, so both report into
        // one ledger keyed on (board, destination) — this one has no card name to give, by design.
        Cards.CardFlightLedger.Note($"peer {_owner.PlayerId}", to.ToString(), "remote-wire-fx", null,
                                    NearestNamedStack(b, to));
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.FlightSlab, _owner.PlayerId,
                                  f.HasFace ? 1 : 0, f.HasFace ? 0 : 1, faceRule);
        // HW-VERIFY: report items 5, 6 and 8 — "Die Animationen bei denen die Karten in den
        // jeweiligen Stapel gehen zeigen beim remote board die Karten mit der Rückseite als
        // Vorderseite". Grep token: FLIGHT FACE. One line per mirrored flight (a handful per turn),
        // at Note tier because the co-player runs at the shipped default level and either machine
        // can be the one watching.
        //
        // THREE READINGS, IN NUMBERS.
        //  WORKING  = 'FRONT via' on every Slot -> Discard/Burnt flight, and 'FRONT via ... LiveLatch'
        //             on the ones whose 'mirror mask' still shows the recess OCCUPIED. In the
        //             ModBuild 461 log that was 0 of 4; anything above 0 is this build's fix landing.
        //  INERT    = 'BACK ... CAUSE = NEVER LATCHED' while the same interval's PEER CARD FACE
        //             CENSUS reads 'round slots[pN] 2 FRONT' — the recess had the face and no path
        //             carried it to the flight. That is the reading 461 shipped and it fired 4/4.
        //  BEYOND   = 'CAUSE = A DELIBERATE REFUSAL' (both ambiguity verdicts). Those are the safety
        //             working, they are only reachable from a legacy sender that names no recess,
        //             and a build that makes them FRONT has traded a visible defect for an
        //             invisible one. Also beyond: an owner '[Cards] FLIGHT ORIGIN' line with NO
        //             line here at all — a lost or swallowed event, not a face defect.
        //
        // THE TWO ANCHORS ARE ITEM 7. 'wire anchor' is what the sender named; 'flown from' is what
        // this mirror resolved it to. They agree by construction for Slot0/Slot1 (461 fixed the
        // sender and its FLIGHT ORIGIN line named a real seat on all 5 of its flights) — a
        // 'wire anchor Board' here means the SENDER could not name the recess, and a flown-from
        // that reads ~0 mm from the board CENTRE is the "mini card in the middle of the board" the
        // user reported. THE DELAY, ALSO IN NUMBERS: Play() runs synchronously inside the packet
        // apply and writes the slab's transform before returning, so the wire frame and the first
        // drawn frame below are the SAME frame by construction. A non-zero difference would be a
        // new defect; the delay item 7 names belongs to RemoteBurnFx's pile POLL, not here.
        VRLog.Note("Net", $"FLIGHT FACE [player {_owner.PlayerId}]: their {from} -> {to} card "
            + $"flight is drawn as a {(f.HasFace ? "FRONT" : "BACK")} — {faceRule}. ANCHORS: wire "
            + $"anchor {from}, flown from {DescribeFlownAnchor(from)} — {DescribeOrigin(from)}. "
            + $"TIMING: wire frame {wireFrame}, first drawn frame {Time.frameCount} "
            + $"(+{Time.frameCount - wireFrame}); this client's own recess mirror stands at mask "
            + $"0x{_owner.MirroredSlotMask:X} and has done for {_owner.FramesSinceSlotMaskChange} "
            + "frame(s) — a mask that still marks THIS recess occupied means the flight beat the "
            + "mirror, which is the race that made the departed-face memory inert on all four of "
            + "ModBuild 461's flights. Compare the owner's own '[Cards] FLIGHT ORIGIN' line for the "
            + "same flight, which names the recess their card really left. The OWNER always "
            + "watches a FRONT go into the pile (VRCard.FlyToPile locks the face-up rotation the "
            + "card had in the recess for the whole arc), so a BACK here is a 1:1 breach. No card "
            + "identity crossed the wire: the face is this client's own read of the card its OWN "
            + "mirror was drawing in that recess.");
        return true;
    }

    /// <summary>
    /// THE ORIGIN, IN WORDS, for the flight line — a recess SEAT, the board centre, or a piece of
    /// avatar furniture, plus what each one means for report item 5's second half.
    ///
    /// <para>IT IS THE OWNER'S ANSWER, NOT THIS CLIENT'S GUESS, and that is the whole point of
    /// printing it. The anchor arrives on the wire; this mirror resolves it through
    /// <c>RemoteAvatar.BoardAnchorLocal</c> and flies from whatever it names.
    /// <c>CardFxAnchor.Board</c> on a turn-clear therefore indicts the SENDER, not the receiver: it
    /// says that player's client could not name the recess its own card was leaving, which through
    /// ModBuild 460 it never could (<c>CardsDriver.TryStartFlyToPile</c> read <c>_tray.SlotOf</c>
    /// off an occupant array the rebuild had already evicted the card from). Two logs, one line
    /// each, and the pair says which end is at fault.</para>
    /// </summary>
    private static string DescribeOrigin(CardFxAnchor from) => from switch
    {
        CardFxAnchor.Slot0 => "their round recess 1's rendered card SEAT — the point their own card "
                              + "left, so this flight is 1:1 in its start as well as its face",
        CardFxAnchor.Slot1 => "their round recess 2's rendered card SEAT — the point their own card "
                              + "left, so this flight is 1:1 in its start as well as its face",
        CardFxAnchor.Board => "their board CENTRE, because the anchor on the wire is "
                              + "CardFxAnchor.Board — the SENDER could not name the recess its card "
                              + "was leaving. On a turn-clear that is report item 5's origin half "
                              + "still standing on THAT machine (a build before ModBuild 461, or a "
                              + "card whose transform had already left its recess); on a burn with "
                              + "no live VR card left it is the honest answer and nothing is owed",
        CardFxAnchor.HandFan => "their non-dominant palm — a fan flight, which has no recess to "
                                + "leave and is not what report item 5 is about",
        _ => $"their {from} furniture",
    };

    /// <summary>
    /// Put the REAL card front on <paramref name="f"/> when this client can name the card, and
    /// return the short rule that decided it (quoted verbatim into the census and the flight line).
    ///
    /// <para>WHERE THE NAME COMES FROM, and why it is not a wire field. A flight out of a round
    /// recess is a flight of the card this client's OWN mirror was drawing in that recess — an
    /// identity already resolved here, locally, off the host-replicated model, and one this class
    /// used to throw away one tick before the flight asked for it.
    /// <c>RemoteControlBoard.TryTakeDepartedFace</c> is that memory; its doc carries the -1 case and
    /// the reason the wire's origin anchor is always <c>Board</c> in practice.</para>
    ///
    /// <para>ONLY A BOARD-ORIGIN FLIGHT ASKS. A flight OUT OF THE HAND FAN is a card the owner is
    /// playing, not one leaving a recess, and there is no departed face for it — it falls through to
    /// the back this class has always drawn, which is also what the owner's own fan card looks like
    /// to a peer while the selection window is open.</para>
    ///
    /// <para><b>NO SENDER EMITS <c>from == HandFan</c> TODAY, AND THE ONE THAT WOULD MUST NOT ROUTE
    /// THROUGH THIS CLASS</b> (2026-09-07 review R3, F3 — re-derived here rather than taken).
    /// Every announcement site in the tree was enumerated: seven <c>CardsDriver.ReportCardFx</c>
    /// (<c>6.Flows:3590</c>, <c>5.Interactions:1490</c>, <c>4.Rebuild:2692/:2999/:3337/:3915/:3965</c>)
    /// and two bare <c>Net.NetCardFx.Report</c> (<c>5.Interactions:1731/:1853</c>). Their origins are
    /// literals (<c>Discard</c>, <c>Slot0</c>, <c>Board</c>) or <c>CardsDriver.SlotAnchor(int)</c>,
    /// which maps only to <c>Slot0</c>/<c>Slot1</c>/<c>Board</c>. <c>HandFan</c> appears on the wire
    /// only as a DESTINATION, at <c>4.Rebuild:3337</c> (a pick restart returning a card to the hand).
    /// So this arm is not currently reached from any sender in this build.</para>
    ///
    /// <para><b>IT IS KEPT, AND IT IS NOT KEPT FOR ITS OWN COMMENT'S SAKE.</b> Two reasons, both
    /// from outside this method. (1) The <c>from</c> nibble is DECODED FROM THE WIRE
    /// (<c>NetCardFx.From</c>) and <see cref="TryResolve"/> answers <c>HandFan</c> with a real point
    /// — the peer's tracked palm — so a <c>HandFan -&gt;</c> event from any source WILL fly. Without
    /// this arm such a flight would fall into the departed-recess-face path with <c>slot == -1</c>,
    /// asking a memory of "what LEFT recess i" about a card that was never in one. The arm is a
    /// guard on the vocabulary, and its worth does not depend on which sites happen to exist today.
    /// (2) The transition it looks like it was built for — the hand-fan card DOCKING into a round
    /// recess (<c>PlayTray.4.Slots:1204/:1231</c>) — must never be announced here, because this
    /// class replays the WRONG MOTION for it. <see cref="RemoteFlightCurve"/> is
    /// <c>VRCard.SmootherStep</c> along the chord plus a <c>VRCard.FlyArcOffset</c> bow over a fixed
    /// <c>NetProtocol.CardFxSeconds</c> (0.4 s) — i.e. <c>VRCard.FlyToPile</c>. <c>PlaceCard</c> does
    /// not fly at all: it calls <c>VRCard.SetHome(instant: false)</c> and the card runs the
    /// exponential HOME GLIDE, <c>1 - exp(-CardLerpSpeed·dt)</c> (<c>VRCard.cs:2291</c>) — no bow, no
    /// fixed duration, asymptotic. Wiring a sender would replace a pop with a 0.4 s arching parabola
    /// where its owner sees a direct exponential ease: the wrong animation played correctly, which
    /// is a NEW 1:1 breach and not a fix for the old one.</para>
    ///
    /// <para><b>THE POP IS REAL AND IS NOT THIS CLASS'S TO FIX.</b> The mirrored recess is a
    /// stationary slab whose CONTENT changes (<c>RemoteBoardCard</c> writes <c>localPosition</c> in
    /// its constructor), so every card a team-mate docks blinks into existence there. The 1:1 remedy
    /// is the owner's own glide on the owner's own dial, seeded from the peer's fan at the frame the
    /// wire's occupancy nibble goes empty-&gt;occupied — <c>RemoteBoardCard.Move(pos, instant,
    /// lerpSpeed)</c> is that machinery and is already built (see its doc; the active column drives
    /// it). What is missing is the EDGE and the SEED POINT, both of which live in
    /// <c>RemoteControlBoard.SeatSlots</c> and <c>RemoteHandFan</c>. It is deliberately NOT faked
    /// here: a glide this class invented would not be driven by the owner's actual motion, and 1:1
    /// means the same motion at the same moment, not a plausible-looking one.</para>
    /// </summary>
    private string ResolveFace(Flight f, CardFxAnchor from, CardFxAnchor to, byte flags)
    {
        f.HasFace = false;
        f.FaceCard = null;
        f.FaceActor = null;
        // Same-build semantic provenance is authoritative. The owner's current rest UI may
        // already describe a later operation when a delayed ordinary burn event arrives.
        f.ShortRestBurn = to == CardFxAnchor.Burnt && CardFlightVisibility.Covered(flags);
        f.Art?.HideFront();
        // A GUARD ON THE WIRE VOCABULARY, not a live sender's arm — see this method's doc for the
        // enumeration of all nine announcement sites (none has from == HandFan) and for why the one
        // transition that looks like it wants this arm, the hand -> recess dock, must not be
        // announced through this class at all.
        if (from == CardFxAnchor.HandFan)
            return "a flight out of the peer's HAND FAN — no recess face to inherit, so a back, "
                 + "which is what its owner's peers see of that card anyway. NOTE: no sender in "
                 + "this build emits this origin, so seeing this rule quoted at all means a peer "
                 + "announced an anchor pair this build does not produce";
        int slot = from == CardFxAnchor.Slot0 ? 0 : from == CardFxAnchor.Slot1 ? 1 : -1;
        // ─── A FLIGHT INTO A RECESS IS THE OTHER DIRECTION AND HAS ITS OWN NAME ─────────────────
        // 2026-09-06 report item 4. The departed-face memory answers "what LEFT recess i", and for a
        // Discard -> Slot0 flight — a short rest's sacrifice flying out of the pile onto the board —
        // that memory holds the card that was there BEFORE. It correctly refused (both of the host
        // session's two remaining BACKs read `Discard -> Slot0 ... CAUSE = WINDOW EXPIRED`), and no
        // widening of that window could ever have been right. The arriving card is not a memory: it
        // is extension record 39, on the wire, right now, for exactly this recess.
        int arriving = to == CardFxAnchor.Slot0 ? 0 : to == CardFxAnchor.Slot1 ? 1 : -1;
        // WHY THE ARRIVAL PATH DID NOT DRESS THIS FLIGHT — null while it was never TRIED, a named
        // reason once it was. See DressFace: it used to return a bare `null` for three completely
        // different refusals, so the caller below could only fall through to a sentence blaming
        // record 39 for something record 39 may well have answered. That sentence is the whole
        // measurement behind the maintainer's open item 8 ("die offene Karte soll verdeckt sein"
        // during the short-rest flight), and it was pointing at the wrong end of the wire.
        string? arrivalRefusal = null;
        try
        {
            if (slot < 0 && arriving >= 0
                && _owner.TryNameArrivingRecessFace(arriving,
                                                    out ScenarioRuleLibrary.CAbilityCard? incoming)
                && incoming != null)
            {
                string? arrivalRule = DressFace(f, incoming,
                    $"the card record 39 says is arriving in their round recess {arriving + 1}",
                    out arrivalRefusal);
                return arrivalRule ?? arrivalRefusal ?? "BACK — arriving recess artwork unavailable";
            }
            if (!_owner.TryTakeDepartedRecessFace(slot, to, out ScenarioRuleLibrary.CAbilityCard? card,
                                                  out RemoteControlBoard.DepartedFaceVerdict verdict)
                || card == null)
                return $"BACK — no departed face claimable for {from} -> {to}: {Why(verdict)}"
                     + (arriving < 0
                         ? string.Empty
                         : arrivalRefusal != null
                             ? ". THIS IS AN ARRIVAL, NOT A DEPARTURE — the memory quoted above is "
                               + "about the card that LEFT that recess and is the wrong question "
                               + "here. RECORD 39 DID NAME A SEAT and this client resolved it; what "
                               + "refused the front is: " + arrivalRefusal
                             : ". THIS IS AN ARRIVAL, NOT A DEPARTURE — the memory quoted above is "
                               + "about the card that LEFT that recess and is the wrong question "
                               + "here; the right one is extension record 39, and it named no seat "
                               + "for that recess that this client could resolve (read the owner's "
                               + "own 'SHORT REST SEAT' line and its SAMPLER SAYS clause)");
            // ─── THE ONE FACT THE ACTIVE MATRIX CANNOT GET ANYWHERE ELSE ────────────────────────
            // Stamped HERE and not in Play(), because this is the only point at which the flying
            // card has a NAME: the claim above is what resolves it, and it lives in this class's
            // private pool. RemoteActiveCards is rebuilt from the host-replicated ActivatedCards,
            // which holds the card from the moment the model moved it — so for the arc's 0.4 s an
            // observer would draw the card in its CELL and again in the AIR while the owner draws it
            // once (their flying card IS the cell's card). Two copies of one card is the failure this
            // project ranks above a missing animation.
            //
            // IT IS STAMPED BEFORE THE REVEAL GATE IS ASKED, DELIBERATELY. This is a POSITION fact —
            // "that card is in the air right now" — and it is true whether the slab ends up carrying
            // the front or a back. Gating it on the face permission would leave the cell drawn
            // underneath a card-back slab during a peer's selection window, which is the same double
            // with one of the two copies anonymous.
            if (to == CardFxAnchor.Active)
                f.ActiveCardId = card.CardInstanceID;
            ScenarioRuleLibrary.CPlayerActor? actor =
                f.SourceActor ?? RemoteBoardFocus.DisplayedActor(_owner, out _);
            // Cached artwork is a lookup source, not a retained permission. Recheck the phase,
            // and keep a short-rest burn covered for the entire flight, including a phase edge.
            f.FaceCard = card;
            f.FaceActor = actor;
            if (f.ShortRestBurn || RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor,
                                     card.CardInstanceID)
                == RevealGate.CardFaceSource.None)
                return "BACK — RevealGate refused the front at flight time: selection is covered, "
                     + "and a short-rest burn remains covered throughout its flight";
            if (f.Art == null)
                return "BACK — this pooled slab has no face overlay (its GameObject failed to build)";
            RemoteAbilityCardSource.FacePath path =
                RemoteAbilityCardSource.ShowFullFace(f.Art, actor, card);
            if (path == RemoteAbilityCardSource.FacePath.None)
                return "BACK — the card was named but its face CLONE failed to build "
                     + "(RemoteAbilityCardSource found neither a live widget nor a poolable one)";
            f.HasFace = true;
            f.Art.SetNativeAppearance(_owner.PlayerId, actor, card);
            DriveFlightLook(f, actor, card);
            return $"FRONT via RemoteAbilityCardSource.{path}, inherited from the round recess this "
                 + "client's own mirror was drawing that card in one tick ago";
        }
        catch (System.Exception ex)
        {
            f.HasFace = false;
            f.Art?.HideFront();
            return $"BACK — the front resolve threw ({ex.GetType().Name}: {ex.Message}), which is "
                 + "the picture every build before ModBuild 461 drew";
        }
    }

    /// <summary>
    /// Put <paramref name="card"/>'s real front on <paramref name="f"/>, asking the reveal gate and
    /// the clone builder in the same order the departure path does, and return the rule string —
    /// or <c>null</c> when the gate or the builder refused, so the caller falls through to its own
    /// answer rather than reporting an arrival refusal for a departure it has not tried yet.
    ///
    /// <para>Shared with the departure path on purpose: two copies of "ask the gate, then build the
    /// clone" is exactly how one surface ends up with a permission the other does not have, which is
    /// the defect <c>RevealGate.CardFaces</c>'s own file note is written about.</para>
    ///
    /// <para><paramref name="refusal"/> IS THE INSTRUMENT, AND ITS ABSENCE WAS THE DEFECT. This
    /// method has three refusals — the reveal gate said no, this pooled slab has no face overlay,
    /// the face clone failed to build — and it used to answer all three with a bare <c>null</c>.
    /// The caller then printed "extension record 39 named NO seat for this recess", which is a
    /// statement about the SENDER, for three receiver-side outcomes in which record 39 had done its
    /// job perfectly. That string is the only measurement standing behind the maintainer's open item
    /// 8 ("die offene Karte soll verdeckt sein" during the short-rest flight) — the line that looked
    /// like it settled the item was falsified in ModBuild 477 — so it had to stop guessing. Read
    /// this beside the owner's own <c>[Cards] SHORT REST SACRIFICE</c> line and one grep answers it:
    /// a GATE refusal names the phase and is the ruling working (a short-rest sacrifice stays
    /// covered both in its recess and throughout its burn flight), while a BUILDER refusal is a defect on
    /// this client and has nothing to do with any ruling at all.</para>
    ///
    /// <para><c>null</c> in <paramref name="refusal"/> means the arrival path was never TRIED — the
    /// caller distinguishes "not asked" from "asked and refused", which is the distinction the old
    /// single return value flattened.</para>
    /// </summary>
    private string? DressFace(Flight f, ScenarioRuleLibrary.CAbilityCard card, string origin,
                              out string? refusal)
    {
        refusal = null;
        ScenarioRuleLibrary.CPlayerActor? actor = f.SourceActor ?? RemoteBoardFocus.DisplayedActor(_owner, out _);
        // The existing address names the source card; it does not grant face visibility.
        // Arrivals and departures share the phase policy, including accepted short-rest burns.
        f.FaceCard = card;
        f.FaceActor = actor;
        if (RevealGate.CardFaces(RevealGate.PeerCardPopulation.BoardPickSeat, actor,
                                 card.CardInstanceID, out RevealGate.FaceRule rule)
            == RevealGate.CardFaceSource.None || f.ShortRestBurn)
        {
            refusal = $"THE REVEAL GATE, for '{card.Name}' — {RevealGate.RuleText(rule)}. "
                    + "Selection-phase cards and the entire short-rest burn flight stay covered "
                    + "under the user's MB487 visibility rule.";
            return null;
        }
        if (f.Art == null)
        {
            refusal = $"THIS POOLED SLAB HAS NO FACE OVERLAY for '{card.Name}' (its GameObject "
                    + "failed to build) — a receiver-side fault with no ruling behind it";
            return null;
        }
        RemoteAbilityCardSource.FacePath path =
            RemoteAbilityCardSource.ShowFullFace(f.Art, actor, card);
        if (path == RemoteAbilityCardSource.FacePath.None)
        {
            refusal = $"THE FACE CLONE FAILED TO BUILD for '{card.Name}' — the gate was OPEN and "
                    + "RemoteAbilityCardSource found neither a live widget nor a poolable one. A "
                    + "receiver-side fault, and the same one the departure path reports one branch "
                    + "down; nothing about the phase or the sender is implicated";
            return null;
        }
        f.HasFace = true;
        f.Art.SetNativeAppearance(_owner.PlayerId, actor, card);
        DriveFlightLook(f, actor, card);
        return $"FRONT via RemoteAbilityCardSource.{path}, from {origin}";
    }

    /// <summary>
    /// PUT THE CARD'S SETTLED LOOK ON THE SLAB THAT IS FLYING IT — the hole lane B's look census
    /// named as the biggest one left (M10), closed at the two places this class commits a front.
    ///
    /// <para>WHY IT MATTERED. Every Slot -&gt; Discard flight, and every Slot -&gt; Burnt flight that
    /// <c>RemoteBurnFx.ConsumesWireEvent</c> does not swallow, flew a CLEAN face into a pile that
    /// this very mirror draws charred or greyed one tick later. The card did not change between
    /// those two frames; only the surface drawing it did, which is the definition of a 1:1 breach in
    /// the user's own terms — his ruling covers ANIMATION and STATE, not only the resting picture.
    /// The owner never saw it: on their board the flight is the game's own live widget, wearing the
    /// look the game put on it before the flight began.</para>
    ///
    /// <para>IT ASKS THE SAME EXPRESSION EVERY OTHER MIRRORED SURFACE ASKS, and that is the whole
    /// point of it being one line: <see cref="UsedCardLook.FromState"/> reads
    /// <c>Cards.BurnLookPolicy.ForCard</c>, the single durable pile-to-look implementation, DOWN in
    /// <c>Cards/</c>. Progress is committed at <c>1f</c> rather than ramped because a flight is not
    /// where a look is EARNED — the card was already spent when it left the recess, and a ramp here
    /// would animate a state change that happened somewhere else.</para>
    ///
    /// <para>A card the policy calls clean writes <c>None</c>, which is the pooled slab's own rest
    /// state and costs nothing; the write is what stops a RECYCLED slab from wearing the previous
    /// flight's char, which is the failure direction that would have been introduced by only
    /// writing the non-clean cases.</para>
    /// </summary>
    private static void DriveFlightLook(Flight f, ScenarioRuleLibrary.CPlayerActor? owner,
                                        ScenarioRuleLibrary.CAbilityCard? card)
    {
        if (f.Art == null)
            return;
        // THE OWNER IS PASSED because the durable look is LIST membership for one whole class of
        // burn: GameState.Lose1HandCardToAvoidAttack moves the card into LostAbilityCards through
        // CCharacterClass.MoveAbilityCard, which never writes CurrentCardPile — so without an actor
        // to ask, a card burnt to negate damage flies PRISTINE into the burnt stack.
        f.Art.SetAbilityCardFxProgress(UsedCardLook.FromState(owner, card), 1f);
    }

    /// <summary>
    /// WHICH OF THE FOUR CAUSES produced a faceless flight, in the words the FLIGHT FACE line
    /// quotes. It used to list all four in one sentence, which made every BACK read the same and
    /// cost a round: the ModBuild 461 log's four BACKs were all the SAME cause and the line could
    /// not say so.
    /// </summary>
    private static string Why(RemoteControlBoard.DepartedFaceVerdict verdict) => verdict switch
    {
        RemoteControlBoard.DepartedFaceVerdict.NothingLatched =>
            "CAUSE = NEVER LATCHED. This client's mirror of that recess never drew the card "
            + "face-up, so there was never a face to inherit — the reveal gate was shut for it, the "
            + "compaction belt refused the positional walk, or the recess was drawing an anonymous "
            + "back. THE DEFECT IS UPSTREAM OF THIS CLAIM: read 'ROUND SLOT COMPACTION REFUSED' and "
            + "'ANONYMOUS RECESS' for the same peer, not this line",
        RemoteControlBoard.DepartedFaceVerdict.WindowExpired =>
            "CAUSE = WINDOW EXPIRED. A face WAS remembered for that recess and is older than the "
            + "claim window, so the wire event was dropped or arrived seconds late. Nothing on this "
            + "client can fix that; read the owner's own '[Cards] FLIGHT ORIGIN' timing",
        RemoteControlBoard.DepartedFaceVerdict.AmbiguousBothInPile =>
            "CAUSE = A DELIBERATE REFUSAL. Two recesses emptied at once and BOTH their faces are in "
            + "the destination stack on this client, so no fact here says which card this flight "
            + "carries. A back beats a front on the wrong card and this refusal is the safety "
            + "working — do NOT loosen it",
        RemoteControlBoard.DepartedFaceVerdict.AmbiguousNeitherInPile =>
            "CAUSE = A DELIBERATE REFUSAL. Two recesses emptied at once and NEITHER face has "
            + "reached the destination stack in this client's copy of that peer's piles yet, which "
            + "is indistinguishable from 'this face belongs to the other flight'. The refusal is "
            + "the safety working — do NOT loosen it",
        _ => $"CAUSE = {verdict} (unexpected on a refusal path — read this method)",
    };

    // A public flight must not flash a back while artwork is loading. Keep its timeline
    // running while withholding the slab; covered selection/short-rest bodies remain visible.
    private static bool CanDrawFlight(Flight f) => f.ShortRestBurn
        || RevealGate.IsSecretSelectionPhase || f.HasFace;

    private static void RefreshFaceVisibility(Flight f)
    {
        bool allowed = !f.ShortRestBurn && f.FaceCard != null
            && RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, f.FaceActor,
                f.FaceCard.CardInstanceID, scenarioEstablished: true, out _) != RevealGate.CardFaceSource.None;
        if (!allowed)
        {
            f.Art?.HideFront();
            f.HasFace = false;
        }
        else if (!f.HasFace && f.Art != null)
        {
            f.HasFace = RemoteAbilityCardSource.ShowFullFace(f.Art, f.FaceActor, f.FaceCard!)
                != RemoteAbilityCardSource.FacePath.None;
            if (f.HasFace) DriveFlightLook(f, f.FaceActor, f.FaceCard!);
        }
        SetFrontFace(f, showsBack: !f.HasFace);
    }

    /// <summary>
    /// THE ANCHOR THE MIRROR REALLY FLEW FROM, beside the one the wire carried — report item 7 in
    /// one line. The two disagree exactly when this client cannot resolve the seat the sender
    /// named, and the difference is printed in BOARD-LOCAL millimetres from the board CENTRE so a
    /// "the card came out of the middle of the board" report is decidable without a screenshot.
    /// </summary>
    private string DescribeFlownAnchor(CardFxAnchor from)
    {
        Vector3 local = _owner.BoardAnchorLocal(from);
        Vector3 centre = RemoteControlBoard.AnchorLocal(CardFxAnchor.Board);
        float offMm = (local - centre).magnitude * 1000f;
        return $"board-local {local:F3} m, {offMm:F0} mm from the board CENTRE "
               + $"({centre:F3} m)";
    }

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        for (int i = 0; i < _pending.Count;)
        {
            PendingFlight pending = _pending[i];
            if (Time.unscaledTime - pending.ReceivedAt > ResolveSeconds
                || TryPlay(pending.Endpoints, pending.Flags, pending.Source, pending.Generation)) _pending.RemoveAt(i);
            else i++;
        }
        for (int i = 0; i < _flights.Count; i++)
        {
            Flight f = _flights[i];
            if (!f.Active) continue;
            if (f.Go == null)
            {
                f.Active = false;
                f.ActiveCardId = int.MinValue;
                continue;
            }
            if (f.FaceCard == null && !f.ShortRestBurn && !RevealGate.IsSecretSelectionPhase
                && ReferenceEquals(f.SourceActor, RemoteBoardFocus.DisplayedActor(_owner, out _)))
                ResolveFace(f, NetCardFx.From(f.Endpoints), NetCardFx.To(f.Endpoints), f.Flags);
            RefreshFaceVisibility(f);
            bool drawable = CanDrawFlight(f);
            if (drawable) TransferVisibleFlight(f);
            f.Go.SetActive(drawable);
            if (!drawable)
            {
                // Withholding a public back must not consume the whole unseen arc. Allow the
                // correct source to load, then start/resume movement; teardown remains bounded.
                f.ArtworkWait += Mathf.Max(dt, 0f);
                if (f.ArtworkWait >= ResolveSeconds)
                {
                    f.Active = false;
                    f.ActiveCardId = int.MinValue;
                    f.Art?.HideFront();
                }
                continue;
            }
            f.Elapsed += Mathf.Max(dt, 0f);
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01(f.Elapsed / NetProtocol.CardFxSeconds)
                : 1f;
            // Resolve the actual initial cell after the board's content refresh, then retain it
            // for this flight. VRCard.FlyFromPile captures its world endpoint once; a later grid
            // or board movement must not bend only the observer's arc.
            if (!f.ActiveTargetCaptured && f.ActiveCardId != int.MinValue
                && _owner.TryActiveCellLocal(f.ActiveCardId, out Vector3 cell))
            {
                f.To = BoardLocalToWorld(cell);
                f.Arc = Mathf.Max(CardHeight * MinArcCardHeights * DrawnBoardScale,
                    Vector3.Distance(f.From, f.To) * ArcFraction);
                f.ActiveTargetCaptured = true;
            }
            // THE OWNER'S OWN CURVE, CALLED — not "the same shape as VRCard's fly", which is what
            // the sentence that stood here claimed while the code flew a DIFFERENT ONE. This wrote
            // out plain smoothstep along the chord and bowed with sin(pi*t) on the RAW t, against
            // VRCard.FlyToPile's SMOOTHERSTEP and its FlyArcOffset parabola on the EASED s. Same
            // duration, same peak, different path — about 0.8 m apart at t = 0.25 on the arc the
            // ModBuild 476 session measured. RemoteFlightCurve holds the whole argument and the
            // reason no arc reading could ever see it.
            float e = RemoteFlightCurve.Ease(t);
            Vector3 p = RemoteFlightCurve.Pose(e, f.From, f.To, f.ArcUp, f.Arc);
            f.Go.transform.SetPositionAndRotation(p, f.Rotation);
            // …AND THE SIZE RAMPS WITH IT (report item 8 — see the FromWidth/ToWidth block in Play).
            // On the EASED parameter, not the raw one, because VRCard.FlyToPile lerps its
            // own scale on the same eased term it lerps the chord on: a slab that travelled on one
            // curve and resized on another would be a second animation, not a mirror of the first.
            // Re-read per frame because the board SCALE can move under a live flight (the owner may
            // be dragging their diorama), which is the same reason the position write is per frame
            // — and read off the board AS DRAWN, for the reason DrawnBoardScale states.
            f.Go.transform.localScale = Vector3.one
                * (DrawnBoardScale * (Mathf.Lerp(f.FromWidth, f.ToWidth, e) / RemoteHandFan.DefaultCardWidth));
            if (t >= 1f)
            {
                f.Active = false;
                f.HasFace = false;
                f.ActiveCardId = int.MinValue;   // the card has landed; its cell must draw again
                f.Art?.HideFront();
                // …and a parked slab with no print on it goes back to the card BACK on both
                // submeshes. Play() re-decides on every reuse, so this is belt rather than the
                // mechanism — but a pooled slab left wearing an edge front fan would show a
                // frameless back for one frame if a later reuse ever failed before ResolveFace.
                SetFrontFace(f, showsBack: true);
                f.Go.SetActive(false);
                f.HandCardId = int.MinValue;
                _owner.CompleteFlightLanding(NetCardFx.To(f.Endpoints), f.SourceActor, f.Generation);
                if (NetCardFx.To(f.Endpoints) == CardFxAnchor.HandFan) _owner.HandFan.RefreshFlightSeats();
            }
        }
    }

    // Capture and dress precede this handover. Board refreshes may still contain the source,
    // but they cannot draw it again; the arriving seat is withheld until this arc completes.
    private void TransferVisibleFlight(Flight f)
    {
        if (!CanDrawFlight(f) || !ReferenceEquals(f.SourceActor,
            RemoteBoardFocus.DisplayedActor(_owner, out _))) return;
        // Reassert the retained identity after a focus-away/back or native refresh. Never
        // recapture a replacement merely because its predecessor's flight is still running.
        if (!f.SourceTransferred || f.TransferredCardId != int.MinValue)
        {
            int captured = _owner.TransferCardToFlight(NetCardFx.From(f.Endpoints),
                f.TransferredCardId != int.MinValue ? f.TransferredCardId
                    : f.FaceCard?.CardInstanceID ?? int.MinValue, f.SourceActor, f.Generation);
            if (captured != int.MinValue) f.TransferredCardId = captured;
        }
        if (!f.SourceTransferred)
        {
            f.SourceTransferred = true;
            if (NetCardFx.To(f.Endpoints) == CardFxAnchor.HandFan) _owner.HandFan.RefreshFlightSeats();
            if (NetCardFx.To(f.Endpoints) == CardFxAnchor.Slot0) _owner.BeginFlightArrival(0, f.SourceActor, f.Generation);
            if (NetCardFx.To(f.Endpoints) == CardFxAnchor.Slot1) _owner.BeginFlightArrival(1, f.SourceActor, f.Generation);
        }
        CardFxAnchor to = NetCardFx.To(f.Endpoints);
        int destinationSlot = to == CardFxAnchor.Slot0 ? 0 : to == CardFxAnchor.Slot1 ? 1 : -1;
        if (destinationSlot >= 0 && _owner.OwnsFlightSlot(destinationSlot, f.SourceActor, f.Generation))
            _owner.SuppressBurnRecess(destinationSlot);
    }

    internal bool IsFlyingToHand(int cardId)
    {
        foreach (Flight f in _flights)
            if (f.Active && f.Go != null && f.HandCardId == cardId && CanDrawFlight(f)
                && ReferenceEquals(f.SourceActor, RemoteBoardFocus.DisplayedActor(_owner, out _))) return true;
        return false;
    }

    internal bool OwnsRecess(int recess)
    {
        CardFxAnchor slot = recess == 0 ? CardFxAnchor.Slot0 : CardFxAnchor.Slot1;
        foreach (Flight f in _flights)
            if (f.Active && f.Go != null && CanDrawFlight(f)
                && ReferenceEquals(f.SourceActor, RemoteBoardFocus.DisplayedActor(_owner, out _))
                && NetCardFx.To(f.Endpoints) == slot
                && _owner.OwnsFlightSlot(recess, f.SourceActor, f.Generation)) return true;
        return false;
    }

    /// <summary>Tell this flight slab's card BODY what its FRONT fan wears — see
    /// <c>CardMesh.SetBodyFrontFace</c>, which owns the rule and the (fade-aware) write.
    ///
    /// <para>EDGE-GATED, like every other caller of that method: a steady flight never walks
    /// CardMesh's body registry. The gate lives on the Flight rather than on this class because the
    /// pool runs up to <c>MaxFlights</c> slabs at once and they do not agree — a Slot -&gt; Burnt
    /// flight can be carrying a real front while a HandFan -&gt; Slot flight beside it is a back.</para>
    ///
    /// <para>MID-FADE: this class is a <c>PeerBoardFade</c> follower (<c>Always</c>), so a flight
    /// really can be in the air while its owner's board ramps. That is exactly why the write goes
    /// through the seam instead of touching <c>sharedMaterials</c>: <c>SetSubmeshMaterial</c> edits
    /// the remembered authored array AND the installed clone for slot 0 and leaves the ramp alone.
    /// That path is argued from <c>Swap</c>/<c>Restore</c>/<c>SettleDepthState</c> and has NOT been
    /// exercised on hardware — see the commit note.</para></summary>
    private static void SetFrontFace(Flight f, bool showsBack)
    {
        if (f.Go == null || f.WearsBack == showsBack)
            return;
        Cards.CardMesh.SetBodyFrontFace(f.Go.transform, showsBack);
        f.WearsBack = showsBack;
    }

    /// <summary>
    /// Is <paramref name="cardInstanceId"/> in the air on its way into this peer's ACTIVE matrix
    /// right now? <c>RemoteActiveCards</c> asks so it can blank that card's cell for the length of
    /// the arc — see its class note, which specifies this method.
    ///
    /// <para>THIS EXPOSES A FACT AND SUPPRESSES NOTHING. The blanking is the matrix's, through the
    /// same <c>Set(null, …)</c> its held-seat suppression already uses — one hiding mechanism with
    /// two callers rather than two mechanisms to keep in step.</para>
    ///
    /// <para>IT CANNOT LATCH A CELL OFF, AND THAT IS THE ONE FAILURE MODE THAT WOULD MATTER. The
    /// stamp is cleared on three independent paths, none of which is a packet: <see cref="Tick"/>
    /// clears it when the arc's own local clock reaches 1 (a flight has no completion EVENT to lose
    /// — its whole duration is <c>NetProtocol.CardFxSeconds</c> counted on this client),
    /// <see cref="Play"/> clears it on every reuse of a pooled slab, and <see cref="Destroy"/> drops
    /// the pool. The query then adds a belt of its own: a flight whose elapsed time has run past the
    /// arc is not answered for even if it is somehow still marked active, so a starved
    /// <see cref="Tick"/> costs at most one extra arc's worth of blanking and never a permanent
    /// hole.</para>
    ///
    /// <para>AND A LOST <c>-&gt; Active</c> EVENT IS SAFE IN THE OTHER DIRECTION TOO. The extras
    /// stream is unreliable by contract (<c>CARD FX LOST</c> read 1 of 7 on the host this session);
    /// a dropped event simply means no flight starts, this answers false, and the matrix draws the
    /// card in its cell — which is where the model already has it, because that column is a
    /// zero-wire per-actor mirror. The cost of a lost event is a missing animation, never a stranded
    /// or a hidden card.</para>
    /// </summary>
    public bool IsFlyingToActive(int cardInstanceId)
    {
        if (cardInstanceId == int.MinValue)
            return false;
        for (int i = 0; i < _flights.Count; i++)
        {
            Flight f = _flights[i];
            if (!f.Active || f.Go == null || f.ActiveCardId != cardInstanceId)
                continue;
            // THE BELT (see the doc): never answer for an arc that has already outlived itself.
            if (f.Elapsed > NetProtocol.CardFxSeconds)
                continue;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ anchors --

    /// <summary>
    /// WHICH NAMED ANCHOR THIS FLIGHT'S ENDPOINT IS REALLY NEAREST TO, with the miss distance —
    /// the resolved half of <c>CardFlightLedger</c>'s label/endpoint pair (2026-09-07 item 4c).
    ///
    /// <para>WHY A NEAREST-NEIGHBOUR SEARCH AND NOT AN EQUALITY TEST. <see cref="TryResolve"/> is a
    /// pure function of <c>RemoteControlBoard.AnchorLocal</c>, so asking whether it returned the
    /// point for <paramref name="intended"/> would compare a value against the expression that
    /// produced it — a claim that measures itself, and this project has recorded that shape. Asking
    /// which of the SIBLING anchors the endpoint is closest to instead is answerable from the
    /// geometry alone: if the Discard and Burnt stacks ever coincided, or a per-board layout term
    /// went missing on one end, a Discard-labelled flight would report Burnt here.</para>
    ///
    /// <para>The distance is printed because a small non-zero miss on the RIGHT anchor is the
    /// healthy reading — the two stacks are <c>PileSpacing</c> apart (Oak 0.116 m), so a miss well
    /// under half of that is the intended anchor with float noise, and a miss NEAR that spacing on
    /// the named anchor means the endpoint is sitting between the two stacks.</para>
    /// </summary>
    private string NearestNamedStack(Vector3 endpoint, CardFxAnchor intended)
    {
        CardFxAnchor best = intended;
        float bestSq = float.PositiveInfinity;
        bool any = false;
        foreach (CardFxAnchor candidate in NamedStacks)
        {
            if (!TryResolve(candidate, out Vector3 world))
                continue;
            float sq = (world - endpoint).sqrMagnitude;
            if (sq >= bestSq)
                continue;
            bestSq = sq;
            best = candidate;
            any = true;
        }
        if (!any)
            return "no anchor resolvable on this board right now, so the endpoint cannot be named";
        float miss = Mathf.Sqrt(bestSq);
        return best == intended
            ? $"the {best} anchor, {miss * 1000f:F0} mm from its centre — label and endpoint AGREE"
            : $"the {best} anchor ({miss * 1000f:F0} mm from its centre), NOT the {intended} anchor "
              + "this flight is labelled with — label and endpoint DISAGREE, which is item 4c";
    }

    /// <summary>The anchors a flight can legitimately END on, for <see cref="NearestNamedStack"/>.
    /// <c>Board</c> is excluded on purpose: it is the "unknown future id" fallback of
    /// <c>RemoteControlBoard.AnchorLocal</c> and would win the nearest-neighbour search for any
    /// endpoint the layout could not place, hiding the very failure this is meant to name.</summary>
    private static readonly CardFxAnchor[] NamedStacks =
    {
        CardFxAnchor.Discard, CardFxAnchor.Burnt, CardFxAnchor.Items,
        CardFxAnchor.Active, CardFxAnchor.Slot0, CardFxAnchor.Slot1, CardFxAnchor.HandFan,
    };

    /// <summary>
    /// THE OWNER'S OWN CARD WIDTH AT ONE ANCHOR, in metres — the term the flight slab's scale ramp
    /// is built from (see the block in <see cref="Play"/>).
    ///
    /// <para>TWO ANCHORS ARE POINTS WHERE THIS CLIENT DRAWS THE CARD ITSELF, and they are the two
    /// where a size disagreement shows up as one card clipping through, or popping into, another.
    /// A ROUND RECESS is one (<c>RemoteBoardCard</c>, sized from <c>RemoteAvatar.SlotCardWidth</c>);
    /// the ACTIVE MATRIX is the other (<c>RemoteActiveCards</c>, sized from the owner's
    /// <c>CardWidth</c> on a root scaled by their <c>ActiveCardScale</c>). Reading the same fields
    /// those surfaces read means the flight and its destination cannot be two sizes. The sentence
    /// that stood here said the recess was "the ONE anchor" — it was written before this class
    /// could fly to the matrix at all.</para>
    ///
    /// <para>A PILE END is the third: the owner shrinks his card into the stack's SLAB
    /// (<c>PileViewer.PileStack.SlabFactor</c> = 0.62 of the card width), and this board draws its
    /// mirrored stack at exactly that product, so a flight that ended at 100 % was a full-size card
    /// vanishing over a 62 % stack on every discard and every burn.</para>
    ///
    /// <para>Falls back to the owner's hand <c>CardWidth</c> for the HAND FAN and for a peer whose
    /// slot-card record has not arrived — which is the size every build before this one drew the
    /// whole arc at, so a missing record costs nothing that was not already the case.</para>
    /// </summary>
    private float WidthForAnchor(CardFxAnchor anchor)
    {
        // A PILE END IS THE STACK'S SLAB, NOT A CARD. The owner's PileViewer.TryGetPileWorld hands
        // VRCard.FlyToPile `lossyScale x CardWidth x PileStack.SlabFactor` and the card shrinks into
        // it; this client draws that same product twice already — RemoteControlBoard.PileCounter's
        // SlabW and RemoteBrowserFan's collapse target — so this is the mirror reading its own
        // rendered stack size, not a number invented here. See the retired paragraph in Play.
        if (anchor == CardFxAnchor.Discard || anchor == CardFxAnchor.Burnt
            || anchor == CardFxAnchor.Items)
            return _cardWidth * PileViewer.PileStack.SlabFactor;

        // THE ACTIVE MATRIX IS THE SECOND ANCHOR WHERE THIS CLIENT DRAWS THE CARD ITSELF, and it
        // had no arm here at all: every '-> Active' flight arrived at the owner's hand CardWidth
        // while the cell it lands in is drawn at that width times their ActiveCardScale.
        // RemoteActiveCards sizes its cells from layout.ActiveCardWidth (= BoardTuning.CardWidth,
        // the very term _cardWidth holds) carried on a root scaled by layout.ActiveCardScale, so
        // the product below is that surface's own expression and not a second one that has to
        // agree with it. Guarded above zero for the same reason every wire dial here is.
        //
        // IT IS INERT AT THE SHIPPED DEFAULTS AND THAT IS STATED RATHER THAN ASSUMED.
        // Defaults.ActiveCardScale_{Oak,Steel,Bronze} are all 1f, and the ModBuild 476 session's
        // own board census reads 'active=(0.502, 0.000, -0.004)(x1.00)' on both clients — so this
        // line changes nothing anybody has yet watched. It closes a structural gap: the moment a
        // peer moves [Cards] ActiveCardScale_{board} (a wired dial, record 28) their teammates
        // would have watched a slab of one size settle into a cell of another, which is precisely
        // the picture ModBuild 477's item 8 fixed for the recess end of the same ramp.
        if (anchor == CardFxAnchor.Active)
            return _cardWidth * Mathf.Max(0.01f, _owner.BoardTuning.ActiveCardScale);
        if (anchor != CardFxAnchor.Slot0 && anchor != CardFxAnchor.Slot1)
            return _cardWidth;
        float slot = _owner.SlotCardWidth;
        return slot > 0.001f ? slot : _cardWidth;
    }

    /// <summary>
    /// World position of one anchor on the SENDER's furniture. Board anchors are the sender's
    /// synced board pose × the shared board-local layout (<see cref="RemoteControlBoard.AnchorLocal"/>),
    /// which is why the flight lands on that player's piles wherever they parked their board; the
    /// hand-fan anchor is their non-dominant palm plus the fan standoff (mirroring
    /// <see cref="RemoteHandFan"/>'s own pose math).
    /// </summary>
    private bool TryResolve(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (anchor == CardFxAnchor.HandFan)
        {
            Transform? holder = _owner.NonDominantHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            // Via RemoteHandFan's own anchor helper, NOT holder.up: the hand ROOT's +Y points out of
            // the BACK of the hand, so aiming a flight along it landed the card a palm-thickness on
            // the wrong side of the peer's hand — the same frame bug PoseFan documents. One shared
            // helper keeps the flight and the fan on the same point by construction.
            world = RemoteHandFan.FanAnchorPoint(_owner, holder);
            return true;
        }

        if (!_owner.HasBoard)
            return false;
        // THE BOARD AS IT IS DRAWN, NOT THE POSE THE PACKET CARRIED. RemoteControlBoard eases its
        // root toward the wire pose at NetProtocol.InterpolationSharpness (~73 ms of trail), and
        // BoardPosition/BoardRotation/BoardScale are that lerp's TARGET. A flight slab is not
        // parented to the root — it has to outlive a board rebuild and a blank — so while a peer
        // CARRIES or ZOOMS their board, which is exactly when the sender raises extras to 15 Hz,
        // the cards detached and flew beside it. Falls back to the raw composition before this
        // peer's first pose has landed, which is what every build before this one did everywhere.
        if (_owner.TryBoardAnchorWorld(anchor, out world))
            return true;
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(anchor) * scale);
        return true;
    }

    /// <summary>A BOARD-LOCAL point on this peer's board resolved to world against the board AS
    /// DRAWN — the composition <see cref="TryResolve"/> makes for a NAMED anchor, for the one
    /// caller that has solved its own local point (the active CELL). Degrades to the raw wire pose
    /// on exactly the same terms.</summary>
    private Vector3 BoardLocalToWorld(Vector3 boardLocal)
    {
        if (_owner.TryDrawnBoardPose(out Vector3 drawnPos, out Quaternion drawnRot, out float drawnScale))
            return drawnPos + drawnRot * (boardLocal * drawnScale);
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        return _owner.BoardPosition + _owner.BoardRotation * (boardLocal * scale);
    }

    /// <summary>This peer's board SCALE as DRAWN — the eased root's, not the wire target. Same
    /// argument as <see cref="TryResolve"/>'s: during a zoom the slab was sized off a number the
    /// board underneath it had not reached yet.</summary>
    private float DrawnBoardScale =>
        _owner.TryDrawnBoardPose(out _, out _, out float s) && s > 0f
            ? s
            : (_owner.BoardScale > 0f ? _owner.BoardScale : 1f);

    /// <summary>
    /// THE SLAB'S ORIENTATION: the owner's own board rotation, for EVERY flight, faced or faceless.
    /// The peer's own synced pose, re-read per frame because a 0.4 s flight over a board the owner
    /// is dragging must travel with it.
    ///
    /// <para>THIS USED TO FORK ON <c>f.HasFace</c>, AND THE FACELESS ARM BILLBOARDED AT
    /// <c>Camera.main</c> — the LOCAL viewer's head. It was the last client-local geometry term on
    /// the remote board, and it fired: 3 of the host's 8 mirrored flights in the ModBuild 476
    /// session drew a BACK (raw 167489, 167496, 199598), and a back is exactly when that arm was
    /// taken. A term that reads the local camera cannot be 1:1 by construction — the same flight
    /// tumbled differently for every watcher, and matched none of them to the owner, whose
    /// <c>VRCard.FlyToPile</c> LOCKS the captured rotation for the whole arc and says so three
    /// times ("the card slides in flat, as it sat on the board", "orientation locked", "never
    /// rotates or billboards"). The standing ruling is explicit that 1:1 includes ROTATION and
    /// outranks local legibility.</para>
    ///
    /// <para>THE TRADE-OFF IT REPLACED WAS ARGUED FROM A PREMISE THAT DOES NOT SURVIVE ITS OWN
    /// OTHER BRANCH. "An anonymous card back has no pose of its own to be faithful to, and edge-on
    /// it is invisible" — but (1) invisibility is a property of the SLAB, not of what is printed on
    /// it, and the FRONT branch was board-locked on 1:1 grounds in ModBuild 461 over the identical
    /// geometry, so the same plane cannot be invisible for a back and fine for a front; and (2) a
    /// card whose FACE this client could not resolve still has a ROTATION on its owner's board —
    /// "no pose to be faithful to" is an IDENTITY answer standing in for a GEOMETRY question, the
    /// substitution this codebase has paid for before. In practice the board plate sits 30° off
    /// horizontal at the shipped <c>BoardTilt</c>, so a slab lying on it is nothing like edge-on
    /// for a viewer who can see that board at all.</para>
    ///
    /// <para>WHAT A BILLBOARD MAY LEGITIMATELY READ, if a later round wants one: the OWNER'S head,
    /// never this client's. <c>RemoteBrowserFan.TryFanPose</c> is the worked example — it billboards
    /// its fan at <c>_owner.HeadHolder</c>, which reproduces the picture the owner is reading rather
    /// than composing a new one per viewer. <c>scripts/check-mirrors.sh</c>'s "mirrored flight
    /// rotation" group fails the build on a flight surface that reaches for the local camera again.
    /// </para>
    ///
    /// <para>AND IT IS THE DRAWN ROTATION, not the wire one. The board root eases toward the
    /// packet's pose and a flight slab is not parented to it, so a slab holding the TARGET rotation
    /// while the board under it turns through the trail is the rotational half of the raw-vs-eased
    /// split <see cref="TryResolve"/> documents.</para>
    /// </summary>
    private Quaternion SlabRotation =>
        _owner.TryDrawnBoardPose(out _, out Quaternion rot, out _) ? rot : _owner.BoardRotation;

    // ------------------------------------------------------------------ pool --

    private Flight Acquire()
    {
        for (int i = 0; i < _flights.Count; i++)
        {
            if (!_flights[i].Active)
                return _flights[i];
        }
        if (_flights.Count >= MaxFlights)
        {
            // All busy: recycle the OLDEST (largest elapsed) rather than dropping the new event.
            Flight oldest = _flights[0];
            for (int i = 1; i < _flights.Count; i++)
            {
                if (_flights[i].Elapsed > oldest.Elapsed)
                    oldest = _flights[i];
            }
            // A recycled slab must not keep the PREVIOUS flight's card on it: the caller re-resolves
            // a face and a failed resolve has to land on a back, not on somebody else's front.
            oldest.Art?.HideFront();
            oldest.HasFace = false;
            return oldest;
        }

        EnsureRoot();
        var f = new Flight();
        if (_root != null)
        {
            var go = new GameObject($"CardFx{_flights.Count}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);

            // THE BODY IS SIZED TO THE RECT THE OWNER'S CARD PRINTS (see the _cardWidth block), on
            // its own child so the flight ROOT keeps carrying board scale × the owner's card width
            // as a UNIFORM scale — the shape RemoteHandFan and RemoteBrowserFan both use. The rect
            // is re-read per pooled slab at creation; CardFace.ObservedFacePixels settles once per
            // session, well before a peer's first card flight, and a flight lasts 0.4 s, so there
            // is nothing here to keep in sync per frame.
            var body = new GameObject("Body");
            body.transform.SetParent(go.transform, worldPositionStays: false);
            Vector2 vis = CardFace.VisibleFaceRect(RemoteHandFan.DefaultCardWidth,
                                                   RemoteHandFan.DefaultCardHeight);
            body.transform.localScale = new Vector3(vis.x / RemoteHandFan.DefaultCardWidth,
                                                    vis.y / RemoteHandFan.DefaultCardHeight, 1f);
            var mf = body.AddComponent<MeshFilter>();
            // Round 17 (1:1 board rule): a flying card adopts the owner's punched-out ABILITY body
            // via CardMesh.AttachBody (shared cached mesh, never ours to destroy; upgraded in
            // place when the contour is learned). Pooled flights register once each at creation, at
            // the NOMINAL box — AttachBody keys its shared meshes on (kind, w, h).
            CardMesh.AttachBody(mf, CardBodyKind.Ability,
                RemoteHandFan.DefaultCardWidth, RemoteHandFan.DefaultCardHeight);
            var mr = body.AddComponent<MeshRenderer>();
            // Two submeshes (front+rim | back), both wearing the SHARED back material (never ours
            // to destroy). This is the slab UNDERNEATH: a flight this client cannot name shows the
            // back on both faces, and a named one gets the real card art hosted on top by the
            // RemoteCardArt below — the same relationship every other mirrored card surface has.
            Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability);
            mr.sharedMaterials = new[] { back, back };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            VRLayers.Apply(go);
            f.Go = go;
            // THE FACE OVERLAY hangs off the flight ROOT, at the NOMINAL card box — NOT off the
            // squashed Body child. RemoteCardArt letterboxes a cloned face into the box it is handed
            // and insets it by its own BorderFraction, so the nominal box PRINTS exactly the
            // VisibleFaceRect the Body above is cut to: print and body land flush, with no rim of
            // card back around the art. That is the arithmetic RemoteHeldCardFace.BodyBox/ArtBox
            // documents in full, and pointing this at the Body child would apply the squash twice.
            f.Art = new RemoteCardArt(go.transform, RemoteHandFan.DefaultCardWidth,
                                      RemoteHandFan.DefaultCardHeight);
            // NAME THE SURFACE (R2 NOTE 2, 2026-09-07). RemoteBurnFx.Acquire names its own rig
            // Flight; this one named nothing, so both it and RemoteHandFan's used-card driver
            // latched on index 0 as Unnamed and the first of them to arm silenced the other's line
            // for the process. CardFlight is this class's own member — a flight that is NOT a burn.
            f.Art.Surface = RemoteCardArt.FxSurface.CardFlight;
        }
        _flights.Add(f);
        return f;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteCardFx[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        // USER ITEM 7 (2026-09-02), EXTENDED TO THE FLIGHTS — see RemoteItemFan.EnsureRoot. He
        // named the three open fans, not the cards flying between them; taken anyway because a
        // card arriving at full opacity on a board that is faded out is the same wrongness he
        // described, and it is the same one line. If it ever reads as too much, this is the
        // line to remove and the board and its fans keep working.
        PeerBoardFade.Follow(_owner.PlayerId, _root.transform);
        VRLayers.Apply(_root);
    }

    public void Destroy()
    {
        _pending.Clear();
        for (int i = 0; i < _flights.Count; i++)
            _flights[i].Art?.Destroy();
        _flights.Clear();
        // Round 17: the body mesh is CardMesh's SHARED cache (AttachBody) — never ours to destroy.
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}
