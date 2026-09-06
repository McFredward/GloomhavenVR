using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
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
/// <c>RotationFor</c>).</para>
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
        public float Arc;
        public float Elapsed;
        public bool Active;
        /// <summary>True while this slab is carrying the real card FRONT rather than a back. It
        /// also decides the slab's ORIENTATION — see the FaceHeadRotation note.</summary>
        public bool HasFace;
    }

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

    /// <summary>How much bigger/smaller than the nominal body the owner's cards are.</summary>
    private float WidthRatio => _cardWidth / RemoteHandFan.DefaultCardWidth;

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
    public void Play(byte endpoints)
    {
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
            return;
        }
        if (!TryResolve(from, out Vector3 a) || !TryResolve(to, out Vector3 b))
        {
            VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} SKIPPED " +
                              "(endpoint unresolved — no synced board pose or the hand is not tracked).");
            return;
        }

        Flight f = Acquire();
        if (f.Go == null)
            return;

        // The owner's own card size for THIS flight (see the _cardWidth block). Guarded above zero
        // because a wire value is never trusted; the struct's own fallback is already the default.
        _cardWidth = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth);

        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
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
        f.Active = true;
        // Board scale × the owner's card width; the Body child under it carries the printed-face
        // squash (see the _cardWidth block and Acquire).
        f.Go.transform.localScale = Vector3.one * (scale * WidthRatio);

        // ─── THE FACE (2026-09-06 report item 5) ────────────────────────────────────────────────
        string faceRule = ResolveFace(f, from, to);

        f.Go.transform.SetPositionAndRotation(a, RotationFor(f, a));
        if (!f.Go.activeSelf)
            f.Go.SetActive(true);

        _played++;
        VRLog.Info("Net", $"Remote card FX [player {_owner.PlayerId}]: {from} -> {to} playing " +
                          $"({NetProtocol.CardFxSeconds:F2}s, arc {f.Arc:F3} m) — flight #{_played}.");
        PeerCardFaceCensus.Report(PeerCardFaceCensus.Surface.FlightSlab, _owner.PlayerId,
                                  f.HasFace ? 1 : 0, f.HasFace ? 0 : 1, faceRule);
        // HW-VERIFY: report item 5 — "Die Animationen bei denen die Karten in den jeweiligen Stapel
        // gehen zeigen beim remote board die Karten mit der Rückseite als Vorderseite". Grep token:
        // FLIGHT FACE. One line per mirrored flight (a handful per turn), at Note tier because the
        // co-player runs at the shipped default level and either machine can be the one watching.
        // READ IT AGAINST THE OWNER'S OWN 'Fly-to-pile'/'BURN ANIM' line for the same card: the
        // owner's flying card is a live VRCard that was lying FACE-UP in the recess and whose
        // rotation VRCard.FlyToPile locks for the whole arc, so a FRONT here is 1:1 and a BACK is
        // the breach. WORKING = 'FRONT'; INERT = 'BACK — no departed face' on a Slot/Board origin
        // while the same interval's PEER CARD FACE CENSUS shows 'round slots ... 2 FRONT' (the
        // recess had the face and the latch did not reach the flight); BEYOND THE INSTRUMENT = a
        // 'Fly-to-pile' line on the owner with NO line here at all, which is a lost or swallowed
        // event and not a face defect.
        VRLog.Note("Net", $"FLIGHT FACE [player {_owner.PlayerId}]: their {from} -> {to} card "
            + $"flight is drawn as a {(f.HasFace ? "FRONT" : "BACK")} — {faceRule}. The OWNER always "
            + "watches a FRONT go into the pile (VRCard.FlyToPile locks the face-up rotation the "
            + "card had in the recess for the whole arc), so a BACK here is the 1:1 breach report "
            + "item 5 names. No card identity crossed the wire: the face is this client's own read "
            + "of the card its OWN mirror was drawing in that recess one tick ago.");
    }

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
    /// </summary>
    private string ResolveFace(Flight f, CardFxAnchor from, CardFxAnchor to)
    {
        f.HasFace = false;
        f.Art?.HideFront();
        if (from == CardFxAnchor.HandFan)
            return "a flight out of the peer's HAND FAN — no recess face to inherit, so a back, "
                 + "which is what its owner's peers see of that card anyway";
        int slot = from == CardFxAnchor.Slot0 ? 0 : from == CardFxAnchor.Slot1 ? 1 : -1;
        try
        {
            if (!_owner.TryTakeDepartedRecessFace(slot, to, out ScenarioRuleLibrary.CAbilityCard? card)
                || card == null)
                return $"BACK — no departed face claimable for {from} -> {to}: this client's mirror "
                     + "of that recess never drew the card face-up (the reveal gate was shut for it, "
                     + "or the model could not name it), the flight arrived more than the claim "
                     + "window after the recess emptied, or TWO recesses emptied at once and this "
                     + "client's copy of that peer's piles could not say which card landed in this "
                     + "stack — a back beats a front on the wrong card";
            ScenarioRuleLibrary.CPlayerActor? actor =
                RemoteBoardFocus.DisplayedActor(_owner, out _);
            // THE PERMISSION IS RE-ASKED, not inherited. The memory only ever holds a face this
            // board drew while the gate was OPEN, so this can hardly refuse — and it is asked anyway
            // because a face permission that is carried rather than evaluated is the shape of defect
            // RevealGate's own file note is written about.
            if (RevealGate.CardFaces(RevealGate.PeerCardPopulation.Selectable, actor)
                == RevealGate.CardFaceSource.None)
                return "BACK — RevealGate refused the front at flight time: the game's own secret "
                     + "SelectAbilityCardsOrLongRest window for a remote character";
            if (f.Art == null)
                return "BACK — this pooled slab has no face overlay (its GameObject failed to build)";
            RemoteAbilityCardSource.FacePath path =
                RemoteAbilityCardSource.ShowFullFace(f.Art, actor, card);
            if (path == RemoteAbilityCardSource.FacePath.None)
                return "BACK — the card was named but its face CLONE failed to build "
                     + "(RemoteAbilityCardSource found neither a live widget nor a poolable one)";
            f.HasFace = true;
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

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        for (int i = 0; i < _flights.Count; i++)
        {
            Flight f = _flights[i];
            if (!f.Active || f.Go == null)
                continue;
            f.Elapsed += Mathf.Max(dt, 0f);
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01(f.Elapsed / NetProtocol.CardFxSeconds)
                : 1f;
            // Same shape as VRCard's fly: smoothstep along the chord + a sine bow along WORLD up
            // (see the ArcUp assignment in Play), so the card visibly clears the board instead of
            // skimming it.
            float e = t * t * (3f - 2f * t);
            Vector3 p = Vector3.Lerp(f.From, f.To, e) + f.ArcUp * (Mathf.Sin(t * Mathf.PI) * f.Arc);
            f.Go.transform.SetPositionAndRotation(p, RotationFor(f, p));
            if (t >= 1f)
            {
                f.Active = false;
                f.HasFace = false;
                f.Art?.HideFront();
                f.Go.SetActive(false);
            }
        }
    }

    // ------------------------------------------------------------------ anchors --

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
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(anchor) * scale);
        return true;
    }

    /// <summary>
    /// THE SLAB'S ORIENTATION, and it depends on whether this flight is carrying a real face.
    ///
    /// <para>A FACELESS slab BILLBOARDS at the local head, as every build before ModBuild 461 did:
    /// an anonymous card back has no pose of its own to be faithful to, and edge-on it is invisible.
    /// </para>
    ///
    /// <para>A slab carrying the card's own FRONT LIES ON THE OWNER'S BOARD instead. That is not a
    /// preference, it is the same 1:1 ruling <c>RemoteBurnFx</c> already carries in as many words:
    /// the owner's card is a live <c>VRCard</c> lying flat in a recess and
    /// <c>VRCard.FlyToPile</c> LOCKS that rotation for the whole arc ("the card slides in flat, as
    /// it sat on the board", "orientation locked"), so facing the card at the local head would be a
    /// pose its owner never sees — a new 1:1 breach introduced by fixing an old one. The board
    /// rotation is the peer's own synced pose, re-read per frame because a 0.4 s flight over a board
    /// the owner is dragging must travel with it.</para>
    /// </summary>
    private Quaternion RotationFor(Flight f, Vector3 at) =>
        f.HasFace ? _owner.BoardRotation : FaceHeadRotation(at);

    /// <summary>Billboard the slab so its BACK faces the local viewer (the mod's card convention:
    /// +Z points away from the reader). A flying card is only ever seen edge-on otherwise.</summary>
    private static Quaternion FaceHeadRotation(Vector3 at)
    {
        Camera? head = VRRigDriver.HeadCamera != null ? VRRigDriver.HeadCamera : Camera.main;
        if (head == null)
            return Quaternion.identity;
        Vector3 away = at - head.transform.position;
        return away.sqrMagnitude > 1e-6f
            ? Quaternion.LookRotation(away.normalized, Vector3.up)
            : Quaternion.identity;
    }

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
