using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// The cosmetic ghost of a remote player's PILE BROWSE fan — the readable arc that fans OUT of one
/// of their control-board pile stacks ("Abgelegt" / "Verbrannt", locally <see cref="PileBrowser"/>)
/// and collapses back INTO it on close.
///
/// ROOT CAUSE this exists: the previous multiplayer round replicated the hand fan, the item fan and
/// the per-card flights, but explicitly left the pile browser out — a peer opening their discard
/// pile produced NOTHING on anyone else's screen, even though it is one of the biggest, most
/// obviously-physical gestures in the VR board (a dozen cards blooming out of a stack right next to
/// their board). This is that missing widget, driven by the additive 2-byte pile-browse block
/// (<see cref="NetProtocol.FlagPileBrowse"/>).
///
/// ANTI-CHEAT / bandwidth: the WIRE carries a pile KIND, a COUNT and a placement — never a card
/// identity, never a transform stream, and that is unchanged.
///
/// CARD FRONTS (user ruling 2026-08-08, "Die Oberseiten der Karten des remote Spielers soll auch
/// überall sichtbar sein … NUR in der Auswahlphase sieht man überall nur die Rückseiten"): this arc
/// used to be card BACKS UNCONDITIONALLY, which made the secrecy rule a PLACE rule and put it in
/// direct conflict with the phase rule the round-card slots and the hand fan already obeyed. The
/// slabs now carry real card faces whenever <see cref="RevealGate.ShowRoundCardFronts"/> is open for
/// the displayed actor, drawn by <see cref="RemotePileFronts"/> from the peer's OWN host-replicated
/// piles (<c>CCharacterClass.Discarded/Lost/PermanentlyLostAbilityCards</c> for discard/burnt,
/// <c>Inventory.AllItems</c> for the items browse). Zero new wire bytes: the identities were already
/// on this client, they were simply never drawn.
///
/// WHY THE ANIMATIONS ARE DRIVEN BY THE RECEIVER'S STATE TRANSITION, and not by the card-FX event
/// channel: the receiver already knows everything the two animations need. It knows where the
/// sender's board is (their synced board pose), therefore where each of their pile STACKS is
/// (<see cref="RemoteControlBoard.AnchorLocal"/> — the same shared board-local layout the local
/// board uses), and it knows the fan's own anchor. So "the fan just opened" and "the fan just
/// closed" — two state edges it observes for free from the block appearing/disappearing — are
/// enough to replay both arcs locally, at the LOCAL timings, with zero extra bytes. Routing them
/// through the card-FX channel instead would cost one event per CARD (a 12-card discard pile = 12
/// events × 2 bytes, on an unreliable 5 Hz stream where a dropped event leaves a card behind), and
/// would still not tell the receiver where the arc slots are. This is the same "events, not
/// transforms" reasoning <see cref="RemoteCardFx"/> is built on, taken one step further: here even
/// the event is implied by the state, so the state IS the event.
///
/// TIMINGS ARE MATCHED TO THE LOCAL ONES so both players see the same motion:
///   EMERGE   — the local browser seats every card ON the pile stack and lets <c>VRCard</c>'s home
///              lerp fly it into its arc slot at <c>[Cards] CardLerpSpeed</c> (14, exponential).
///              Reproduced exactly: seed at the stack, then <c>1 - exp(-14·dt)</c> toward the slot.
///   COLLAPSE — the local <c>CardsDriver.StartBrowseCollapse</c> flies every card back into ITS OWN
///              stack with <c>VRCard.FlyToPile</c>: 0.4 s (<see cref="NetProtocol.CardFxSeconds"/>,
///              the shared FlyToPileSeconds), smoothstep along the chord + a sine bow along the
///              board's up axis, orientation held, shrinking toward the pile slab's width.
///              Reproduced with the same shape <see cref="RemoteCardFx"/> uses.
///
/// PILE SWITCH (poking discard while burnt is open) deliberately does NOT collapse: locally that is
/// a plain <c>PileBrowser.Open</c> on the already-open browser, which re-seats the same cards on the
/// NEW stack and re-emerges them. This mirrors that — a kind change restarts the emerge from the new
/// stack.
/// </summary>
/// <remarks>CLASSIFICATION: VR-ONLY (the fan's existence, size and placement) + PER-ACTOR MODEL (its
/// card faces) — costs wire bytes: the extras trailing block (byte A kind + placement bits, byte B
/// count) behind <c>FlagPileBrowse</c>. Card IDENTITY is DELIBERATELY-NOT transmitted; the FACES are
/// resolved locally off the host-replicated model behind <see cref="RevealGate"/>
/// (<see cref="RemotePileFronts"/>). The emerge/collapse ANIMATIONS are DERIVED locally from the same
/// constants the sender uses, so no per-frame transform rides the wire. See INVARIANTS-Net-Rig.md
/// "Net — content classification".</remarks>
internal sealed class RemoteBrowserFan
{
    // ---- geometry (mirror of PileBrowser's arc constants; local copies so this stays independent
    //      of the receiver's live [Cards] config being bound) ------------------------------------
    private const int MaxCards = 16;                    // PileBrowser's own list capacity
    private const float CardW = RemoteHandFan.DefaultCardWidth;   // ability cards, 63.5 × 88
    private const float CardH = RemoteHandFan.DefaultCardHeight;
    private const float MaxArcDegrees = 110f;           // PileBrowser.MaxArcDegrees
    private const float ArchFactor = 0.55f;             // PileBrowser.Relayout's (cos-1)·radius·0.55
    private const float TiltFactor = 0.85f;             // PileBrowser.Relayout's -angle·0.85
    private const float CardScale = 1.3f;               // PileBrowser.CardScale (browse cards are enlarged)
    private const float ZStagger = 0.004f;              // PileBrowser.ZStagger (draw order)
    private const float HandPalmOffset = 0.16f;         // PileBrowser.HandPalmOffset
    private const float BoardFloatHeight = 0.26f;       // PileBrowser.BoardFloatHeight
    private const float BoardFloatProudZ = -0.05f;      // PileBrowser.BoardFloatProudZ

    /// <summary>Root follow sharpness, same as every other remote card visual.</summary>
    private const float Smoothing = 14f;

    // ---- THE OWNER'S OWN BROWSE-FAN SHAPE (extension record 28, ids 79 / 158..160 / 198..200).
    //
    // These three were `const Radius = 0.16f * 1.7f`, `const MaxStepDegrees = 10f` and
    // `const EmergeSharpness = 14f` — BARE LITERALS re-typed from the shipped defaults, which is the
    // exact failure mode scripts/check-remote-defaults.py was written to catch and which nothing had
    // caught here because the pairs were never listed. They were literals rather than wire fields
    // for one reason: record 28 stood at exactly 255 payload bytes, the extension tail's one-byte
    // length ceiling, and there was nowhere to put them. Paging removed that ceiling (see
    // NetProtocol.BoardTunePages), so an owner who widens or flattens their pile browse fan is now
    // seen doing it — which is what the 1:1 ruling requires ("Ändert ein Spieler also die Positionen
    // für sich selber, so sollen alle anderen diese Position bei seinem board auch sehen").
    //
    // Refreshed by SyncTuning() on the owner's tuning revision and read as plain floats in between:
    // the layout loop touches them per slab per frame and RemoteBoardTuning is a wide struct. The
    // INITIALISER is the shipped default, which is what an untuned peer is still drawn with.
    private float _radius = Defaults.FanRadius * Defaults.FanRadiusFactor_Discard;
    private float _maxStepDegrees = Defaults.FanStepDegrees_Discard;

    /// <summary>EMERGE sharpness — <c>[Cards] CardLerpSpeed</c>, the exponential the local browse
    /// cards actually fly out of the stack on. Wire-overridable (id 157) for the same reason as the
    /// two above: the owner's cards arrive at the owner's rate on every screen, not at ours.</summary>
    private float _emergeSharpness = Defaults.CardLerpSpeed;

    /// <summary>The <see cref="RemoteAvatar.BoardTuningRevision"/> the three dials above were last
    /// refreshed at, plus the pile KIND they were resolved for — the radius factor and the angular
    /// step are per-pile, so switching pile re-resolves them exactly like a dial move does.</summary>
    private int _tuningRevision = -1;
    private int _tuningKind = -1;

    /// <summary>How long the emerge keeps easing before the slots are simply asserted. The
    /// exponential above is ~99.9 % settled well inside this; it exists only so a settled fan stops
    /// paying for the lerp.</summary>
    private const float EmergeSettleSeconds = 0.7f;

    /// <summary>Arc height as a fraction of the collapse distance — <c>VRCard.FlyArcHeightFraction</c>.</summary>
    private const float CollapseArcFraction = 0.28f;

    /// <summary>Minimum collapse arc peak in card heights, mirroring <c>CardsDriver.BoardArcMin</c>.</summary>
    private const float CollapseMinArcCardHeights = 1.5f;

    private readonly RemoteAvatar _owner;

    private GameObject? _root;
    private readonly List<GameObject> _cards = new(MaxCards);

    /// <summary>The FRONT layer over those slabs (user ruling 2026-08-08) — one overlay per slab,
    /// gated on <see cref="RevealGate.ShowRoundCardFronts"/> and fed from the peer's own replicated
    /// pile. Owned here, destroyed with the fan.</summary>
    private readonly RemotePileFronts _fronts;

    private int _builtCount = -1;

    // Open-state tracking: what the fan is CURRENTLY showing, so the two animations can be driven
    // off the transitions (open edge → emerge, close edge → collapse, kind change → re-emerge).
    private bool _open;
    private int _shownKind = -1;
    private bool _poseInit;

    /// <summary>One-shot latch for the "hidden by the remote-board setting" line (see Tick).</summary>
    private bool _gateHiddenLogged;

    // EMERGE: seconds since the fan opened (-1 = settled / not emerging).
    private float _emergeElapsed = -1f;

    // COLLAPSE: seconds into the fly-back (-1 = not collapsing). While collapsing the root pose is
    // FROZEN and the cards are driven in WORLD space toward the pile stack, exactly like the local
    // collapse re-parents its cards out of the browser root before flying them.
    private float _collapseElapsed = -1f;
    private Vector3 _collapseTo;
    private Vector3 _collapseArcUp = Vector3.up;
    private float _collapseArc;
    private float _collapseTargetScale = 1f;
    private readonly List<Vector3> _collapseFrom = new(MaxCards);
    private int _collapseKind = -1;

    public RemoteBrowserFan(RemoteAvatar owner)
    {
        _owner = owner;
        _fronts = new RemotePileFronts(owner, "pile browse fan");
    }

    /// <summary>
    /// Pull the owner's OWN browse-fan shape off <see cref="RemoteAvatar.BoardTuning"/> (extension
    /// record 28, ids 79 / 158..160 / 198..200 — see the field declarations for why they were bare
    /// literals until the record was paged).
    ///
    /// <para>Re-resolves on the owner's tuning revision OR on a pile switch: the radius factor and
    /// the angular step are PER PILE, so browsing "Verbrannt" after "Abgelegt" is as much a change
    /// of these numbers as a dial move is. Everything falls back to the shipped default for the dial
    /// the owner has not moved, which is the same value this client compiled in — so an untuned peer
    /// is drawn exactly as the previous build drew them.</para>
    ///
    /// <para>Guarded against a wire value of zero: a zero radius would collapse the arc onto a point
    /// and a zero step would stack every card on one, neither of which a config range can produce —
    /// but a wire value is never trusted.</para>
    /// </summary>
    private void SyncTuning()
    {
        if (_tuningRevision == _owner.BoardTuningRevision && _tuningKind == _shownKind)
            return;
        _tuningRevision = _owner.BoardTuningRevision;
        _tuningKind = _shownKind;
        RemoteBoardTuning t = _owner.BoardTuning;

        float factor = _shownKind switch
        {
            NetProtocol.PileBrowseKindBurnt => t.FanRadiusFactorBurnt,
            NetProtocol.PileBrowseKindItems => t.FanRadiusFactorItems,
            _ => t.FanRadiusFactorDiscard,
        };
        float step = _shownKind switch
        {
            NetProtocol.PileBrowseKindBurnt => t.FanStepDegreesBurnt,
            NetProtocol.PileBrowseKindItems => t.FanStepDegreesItems,
            _ => t.FanStepDegreesDiscard,
        };
        _radius = Mathf.Max(0.02f, t.FanRadius * factor);
        _maxStepDegrees = Mathf.Max(0.5f, step);
        _emergeSharpness = Mathf.Max(0.5f, t.CardLerpSpeed);
    }

    /// <summary>Map the wire's pile-kind byte onto the model source the front layer reads.</summary>
    private static RemotePileFronts.Content ContentFor(int kind) => kind switch
    {
        NetProtocol.PileBrowseKindBurnt => RemotePileFronts.Content.Burnt,
        NetProtocol.PileBrowseKindItems => RemotePileFronts.Content.Items,
        _ => RemotePileFronts.Content.Discard,
    };

    // ------------------------------------------------------------------ per frame --

    public void Tick(float dt)
    {
        dt = Mathf.Max(dt, 0f);

        bool wantOpen = _owner.PileBrowseOpen && _owner.PileBrowseCardCount > 0;
        int wantKind = wantOpen ? _owner.PileBrowseKind : -1;

        // VISIBILITY ([Net] RemoteBoards — audit 2026-07). A BOARD-ANCHORED browse fan is the peer's
        // board reading its own discard/burnt/item stack: it hangs at a fixed board-local spot and
        // blooms out of one of that board's pile stacks. It used to ignore the setting completely,
        // so with "Aus" (or "Aktionsphase" mid-selection) a dozen enlarged card backs still fanned
        // open in the void where the hidden board was. Gated on the SHARED predicate now, and hidden
        // INSTANTLY rather than collapsed — the collapse flies the cards into the very pile stack the
        // gate just hid. A HAND-HELD browse fan rides the sender's palm and is avatar content, so it
        // is deliberately untouched (the setting is about boards).
        if (wantOpen && !_owner.PileBrowseHeld && !RemoteBoardGate.ShowBoardSurface(_owner))
        {
            if (!_gateHiddenLogged)
            {
                _gateHiddenLogged = true;
                VRLog.Info("Net", $"Remote pile browse [player {_owner.PlayerId}]: board-anchored " +
                                  $"{KindName(wantKind)} fan HIDDEN by [Net] RemoteBoards = " +
                                  $"{RemoteBoardGate.Mode} (it blooms out of that peer's board, which " +
                                  "this client is not drawing).");
            }
            if (_open || _collapseElapsed >= 0f || (_root != null && _root.activeSelf))
            {
                _open = false;
                _shownKind = -1;
                _collapseKind = -1;
                Hide(); // also clears the emerge/collapse timers AND every cloned front
            }
            return;
        }
        _gateHiddenLogged = false;

        // ---- state edges -------------------------------------------------------------------
        if (wantOpen && (!_open || wantKind != _shownKind))
        {
            BeginEmerge(wantKind, Mathf.Clamp(_owner.PileBrowseCardCount, 1, MaxCards));
        }
        else if (!wantOpen && _open)
        {
            BeginCollapse();
        }

        // A collapse runs to completion on its own (the fan is already logically closed).
        if (_collapseElapsed >= 0f)
        {
            TickCollapse(dt);
            return;
        }

        if (!_open || _root == null)
            return;

        int count = Mathf.Clamp(_owner.PileBrowseCardCount, 1, MaxCards);
        if (count != _builtCount)
            Rebuild(count); // cards plucked out of / returned to the arc mid-browse: no re-emerge

        if (!TryResolveAnchor(out Vector3 target, out Quaternion rot, out float rootScale))
            return; // hand not tracked / no board pose yet — hold the last pose rather than snapping

        if (!Mathf.Approximately(_root.transform.localScale.x, rootScale))
            _root.transform.localScale = Vector3.one * rootScale;

        Transform t = _root.transform;
        if (_poseInit)
        {
            _poseInit = false;
            t.SetPositionAndRotation(target, rot);
        }
        else
        {
            float k = 1f - Mathf.Exp(-Smoothing * dt);
            t.SetPositionAndRotation(Vector3.Lerp(t.position, target, k), Quaternion.Slerp(t.rotation, rot, k));
        }

        Layout(count, dt);

        // THE FRONT LAYER (user ruling 2026-08-08). Runs after the layout so a face is only ever
        // asked for on a slab that already sits where it belongs. The gate inside is evaluated every
        // frame; the model resolve behind it rides the board-content cadence — see RemotePileFronts.
        _fronts.Tick(ContentFor(_shownKind));
    }

    // ------------------------------------------------------------------ open / emerge --

    /// <summary>
    /// Open edge (or pile switch): (re)build the slabs, snap the fan to its anchor, then SEED every
    /// card on the sender's pile STACK so the per-card ease below flies them out of it — the same
    /// one-shot trick <c>PileBrowser.Relayout</c> plays with <c>_emergePending</c>. When the stack
    /// cannot be resolved (no board pose yet) the seed degrades to the fan centre, so the worst case
    /// is a spread-open instead of a stack-emerge — never a pop.
    /// </summary>
    private void BeginEmerge(int kind, int count)
    {
        _collapseElapsed = -1f;
        bool switching = _open;
        _open = true;
        _shownKind = kind;

        EnsureRoot();
        if (_root == null)
            return;
        if (count != _builtCount)
            Rebuild(count);
        if (!_root.activeSelf)
            _root.SetActive(true);

        bool anchored = TryResolveAnchor(out Vector3 pos, out Quaternion rot, out float rootScale);
        if (anchored)
        {
            _root.transform.localScale = Vector3.one * rootScale;
            _root.transform.SetPositionAndRotation(pos, rot);
            _poseInit = false;
        }
        else
        {
            _poseInit = true; // snap as soon as the anchor resolves, never ease in from a stale pose
        }

        // Seed on the stack, in the fan's LOCAL frame (the fan is what we lerp within). The seed is
        // only meaningful once the root actually SITS at its anchor — converting a world point
        // through a stale root frame would fling the cards somewhere arbitrary — so an unresolved
        // anchor degrades to the centre seed (a spread-open) rather than a wrong emerge.
        Vector3 seedLocal = Vector3.zero;
        bool seeded = false;
        if (anchored && TryPileStackWorld(kind, out Vector3 stackWorld))
        {
            seedLocal = _root.transform.InverseTransformPoint(stackWorld);
            seeded = true;
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            Transform c = _cards[i].transform;
            c.localPosition = seedLocal + new Vector3(0f, 0f, -ZStagger * i); // keep the draw order stable
            c.localRotation = Quaternion.identity;
            c.localScale = Vector3.one * CardScale;
        }
        _emergeElapsed = 0f;

        VRLog.Info("Net", $"Remote pile browse [player {_owner.PlayerId}]: {KindName(kind)} fan OPEN with " +
                          $"{count} card(s), {(_owner.PileBrowseHeld ? $"held in their {(_owner.PileBrowseLeftHand ? "LEFT" : "RIGHT")} hand" : "above their board")} " +
                          $"— cards emerge {(seeded ? "out of that pile stack" : "from the fan centre (no board pose yet)")}" +
                          $"{(switching ? " (pile switch — matches the local re-emerge, no collapse)" : string.Empty)}. " +
                          "Whether the slabs show FRONTS or BACKS is stated separately by the " +
                          "\"Remote pile browse fan faces\" line (RevealGate decides, per phase).");
    }

    /// <summary>Arc the slabs into the browse layout, easing out of the emerge seed on the same
    /// exponential the local browse cards use. Mirrors <c>PileBrowser.Relayout</c>'s arc math.</summary>
    private void Layout(int n, float dt)
    {
        SyncTuning();
        float step = n > 1 ? Mathf.Min(_maxStepDegrees, MaxArcDegrees / (n - 1)) : 0f;
        float start = -step * (n - 1) * 0.5f;

        bool easing = _emergeElapsed >= 0f;
        float k = 0f;
        if (easing)
        {
            _emergeElapsed += dt;
            k = 1f - Mathf.Exp(-_emergeSharpness * dt);
            if (_emergeElapsed >= EmergeSettleSeconds)
                _emergeElapsed = -1f; // settled: assert the slots exactly from here on (see below)
        }

        // WHICH CARD THE OWNER IS SINGLING OUT in this arc (extension record 6 — defect (f), the
        // "pile fan" half of "das Hervorheben ... soll komplett synchronisiert werden"). Locally the
        // hand sweep elects exactly ONE browse card and lifts it via VRCard.SetFingertipHover — the
        // same pop the laser gives — and pop-suppresses the rest. Reproduced from the synced INDEX
        // alone. Clamped against OUR live slab count: a packet may arrive a frame off the count it
        // was measured against.
        int hovered = _owner.FanHighlightIndex;
        if (hovered < 0 || hovered >= _cards.Count)
            hovered = -1;

        for (int i = 0; i < _cards.Count; i++)
        {
            float angle = start + step * i;
            float rad = angle * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Sin(rad) * _radius,
                                  (Mathf.Cos(rad) - 1f) * _radius * ArchFactor,
                                  -ZStagger * i);
            var rot = Quaternion.Euler(0f, 0f, -angle * TiltFactor);
            // The LIFT, on top of the finished arc pose exactly as VRCard applies it: toward the
            // viewer along the card's own −Z, a touch up its +Y, 18 % bigger. The 0..1 ramp runs on
            // the LOCAL clock at VRCard's own rate, so the wire carries an index and never an
            // animation. Deliberately NO whole-fan split here — the local browse arc does not split
            // either (only the hand fan does), and inventing one would be a widget the owner lacks.
            float popT = PopAmount(i, hovered, dt);
            if (popT > 0f)
                pos += rot * new Vector3(0f, PopUp * popT, -PopForward * popT);
            float scale = CardScale * (1f + PopScale * popT);
            Transform t = _cards[i].transform;
            if (easing)
            {
                t.localPosition = Vector3.Lerp(t.localPosition, pos, k);
                t.localRotation = Quaternion.Slerp(t.localRotation, rot, k);
            }
            else
            {
                t.localPosition = pos;
                t.localRotation = rot;
                t.localScale = Vector3.one * scale;
            }
        }
        LogHighlightIfChanged(hovered);
    }

    // ---------------------------------------------------------------- highlight (record 6) --

    /// <summary>CardsConfig.FanSelectedPopForward — how far a lifted card comes toward the viewer
    /// (VRCard's pop). Local copy of the AUTHORED default, like every other constant in this file:
    /// the sender's live [Cards] tuning is theirs and never rides the wire.</summary>
    private const float PopForward = Defaults.FanSelectedPopForward;

    /// <summary>VRCard's pop: the small upward component riding with the forward lift.</summary>
    private const float PopUp = 0.012f;

    /// <summary>VRCard's pop: the extra size a lifted card takes (+18 %).</summary>
    private const float PopScale = 0.18f;

    /// <summary>VRCard's pop RAMP rate (units/second, MoveTowards).</summary>
    private const float PopRate = 8f;

    /// <summary>Per-slab pop ramp (0..1), index-aligned with <c>_cards</c> — kept per slab so a
    /// lift MOVING between cards has the old one relaxing while the new one rises.</summary>
    private readonly List<float> _pop = new(32);

    /// <summary>Last highlighted index stated in the log (−2 = never).</summary>
    private int _loggedHighlight = -2;

    /// <summary>Advance and return slab <paramref name="i"/>'s pop ramp toward 1 while it is the
    /// highlighted card and toward 0 otherwise. Grows the ramp list with the arc.</summary>
    private float PopAmount(int i, int hovered, float dt)
    {
        while (_pop.Count <= i)
            _pop.Add(0f);
        _pop[i] = Mathf.MoveTowards(_pop[i], i == hovered ? 1f : 0f, Mathf.Max(dt, 0f) * PopRate);
        return _pop[i];
    }

    /// <summary>Drop every pop ramp (arc closed / rebuilt) so a re-opened browse never starts with
    /// a stale card already lifted.</summary>
    private void ClearPops()
    {
        for (int i = 0; i < _pop.Count; i++)
            _pop[i] = 0f;
        _loggedHighlight = -2;
    }

    /// <summary>Change-gated evidence that the synced browse highlight reached the render path
    /// (grep: "Remote browse fan highlight").</summary>
    private void LogHighlightIfChanged(int hovered)
    {
        if (hovered == _loggedHighlight)
            return;
        _loggedHighlight = hovered;
        Core.VRLog.Info("Net", $"Remote browse fan highlight [{_owner.PlayerId}]: " +
                               $"index {(hovered >= 0 ? hovered.ToString() : "none")} of " +
                               $"{_cards.Count} slab(s) (wire index {_owner.FanHighlightIndex}) — " +
                               "the card lifts on VRCard's own pop, from the INDEX alone " +
                               "(extension record 6: no card identity).");
    }

    // ------------------------------------------------------------------ close / collapse --

    /// <summary>
    /// Close edge: fly every slab back INTO the sender's pile stack instead of hiding the fan. The
    /// root pose is frozen for the duration and the slabs are driven in WORLD space (the local
    /// collapse re-parents its cards out of the browser root for the same reason: the arc they fly
    /// from must not keep moving under them). Degrades to an instant hide only when the stack cannot
    /// be resolved at all.
    /// </summary>
    private void BeginCollapse()
    {
        _open = false;
        _emergeElapsed = -1f;
        int kind = _shownKind;
        _shownKind = -1;

        if (_root == null || !_root.activeSelf || _cards.Count == 0)
        {
            Hide();
            return;
        }
        if (!TryPileStackWorld(kind, out Vector3 stackWorld))
        {
            VRLog.Info("Net", $"Remote pile browse [player {_owner.PlayerId}]: {KindName(kind)} fan CLOSED — " +
                              "hidden instantly (the sender's board pose is unknown, so there is no stack to collapse into).");
            Hide();
            return;
        }

        float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        _collapseKind = kind;
        _collapseTo = stackWorld;
        _collapseArcUp = _owner.HasBoard ? _owner.BoardRotation * Vector3.up : Vector3.up;
        _collapseArc = Mathf.Max(CardH * CollapseMinArcCardHeights * bs,
                                 Vector3.Distance(_cards[0].transform.position, stackWorld) * CollapseArcFraction);

        // Shrink toward the pile SLAB's real width (the local FlyToPile does exactly this, which is
        // what makes the card read as slotting into the pile rather than sinking through the board).
        float rootScale = _root.transform.localScale.x > 0f ? _root.transform.localScale.x : 1f;
        _collapseTargetScale = bs * PileViewer.PileStack.SlabFactor / rootScale;

        _collapseFrom.Clear();
        for (int i = 0; i < _cards.Count; i++)
            _collapseFrom.Add(_cards[i].transform.position);
        _collapseElapsed = 0f;

        VRLog.Info("Net", $"Remote pile browse [player {_owner.PlayerId}]: {KindName(kind)} fan CLOSED — " +
                          $"{_cards.Count} card(s) collapse back into that stack ({NetProtocol.CardFxSeconds:F2}s, " +
                          $"arc {_collapseArc:F3} m over their board), matching the local browse collapse.");
    }

    /// <summary>Drive the collapse: smoothstep along each card's own chord plus a shared sine bow
    /// along the board's up axis, orientation held — the shape <c>VRCard.FlyToPile</c> flies and
    /// <see cref="RemoteCardFx"/> already replays.</summary>
    private void TickCollapse(float dt)
    {
        _collapseElapsed += dt;
        float u = NetProtocol.CardFxSeconds > 0f
            ? Mathf.Clamp01(_collapseElapsed / NetProtocol.CardFxSeconds)
            : 1f;
        float e = u * u * (3f - 2f * u);
        float bow = Mathf.Sin(u * Mathf.PI) * _collapseArc;

        for (int i = 0; i < _cards.Count && i < _collapseFrom.Count; i++)
        {
            Transform t = _cards[i].transform;
            t.position = Vector3.Lerp(_collapseFrom[i], _collapseTo, e) + _collapseArcUp * bow;
            t.localScale = Vector3.one * Mathf.Lerp(CardScale, _collapseTargetScale, e);
        }

        if (u < 1f)
            return;
        _collapseElapsed = -1f;
        VRLog.Info("Net", $"Remote pile browse [player {_owner.PlayerId}]: {KindName(_collapseKind)} collapse " +
                          "finished — the fan is back in the stack.");
        _collapseKind = -1;
        Hide();
    }

    // ------------------------------------------------------------------ anchors --

    /// <summary>
    /// Where the fan floats this frame, mirroring <c>PileBrowser</c>'s two modes: HAND-HELD → a palm
    /// standoff above the grabbing hand (which hand rides the wire — the receiver must not guess);
    /// otherwise → the fixed spot above the sender's synced control board.
    ///
    /// <paramref name="rootScale"/> reproduces WHICH transform the local fan hangs under: the
    /// board-anchored fan is a child of the board root and therefore inherits the sender's BOARD
    /// scale, while the held fan hangs off their palm and inherits their RIG scale. Since the
    /// 1:1 parity round the SENDER's live board-local fan position (which includes their
    /// <c>[Cards] BrowseFanOffset</c> tuning) rides the wire as extension record 5 and is
    /// preferred here; the authored default survives for pre-record senders. The near-twin
    /// <c>RemoteItemFan.TryResolvePose</c> now follows the identical scale + anchor rules.
    /// </summary>
    private bool TryResolveAnchor(out Vector3 pos, out Quaternion rot, out float rootScale)
    {
        pos = default;
        rot = Quaternion.identity;
        rootScale = 1f;

        if (_owner.PileBrowseHeld)
        {
            Transform? holder = _owner.PileBrowseLeftHand ? _owner.LeftHandHolder : _owner.RightHandHolder;
            if (holder == null || !holder.gameObject.activeInHierarchy)
                return false;
            rootScale = _owner.AppliedScale;
            pos = holder.position + holder.up * (HandPalmOffset * rootScale);
        }
        else
        {
            if (!_owner.HasBoard)
                return false;
            rootScale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
            // Defect 6: prefer the SYNCED board-local anchor (extension record 5 — the owner's
            // real fan spot incl. their [Cards] BrowseFanOffset tuning); the authored default
            // survives only for pre-record senders / while the record is absent.
            Vector3 local = _owner.HasFanAnchor
                ? _owner.FanAnchorLocal
                : new Vector3(0f, PlayTray.BoardTopLocalY + BoardFloatHeight, BoardFloatProudZ);
            pos = _owner.BoardPosition + _owner.BoardRotation * (local * rootScale);
        }

        // Fronts (−Z) toward the owner, backs toward everyone else — the shared card convention that
        // also makes the local billboard (which faces the OWNER's head) read the same way for us.
        Transform? head = _owner.HeadHolder;
        if (head != null)
        {
            Vector3 away = pos - head.position;
            if (away.sqrMagnitude > 1e-6f)
                rot = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
        return true;
    }

    /// <summary>World position of the browsed pile's STACK on the sender's board — the point the
    /// cards emerge from and collapse into. Resolved through the SHARED board-local stack layout
    /// (<see cref="RemoteControlBoard.AnchorLocal"/>) against the sender's own synced board pose, so
    /// the arc starts and ends on that player's real pile wherever they parked their board.</summary>
    private bool TryPileStackWorld(int kind, out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float bs = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(AnchorFor(kind)) * bs);
        return true;
    }

    private static CardFxAnchor AnchorFor(int kind) => kind switch
    {
        NetProtocol.PileBrowseKindBurnt => CardFxAnchor.Burnt,
        NetProtocol.PileBrowseKindItems => CardFxAnchor.Items,
        _ => CardFxAnchor.Discard,
    };

    private static string KindName(int kind) => kind switch
    {
        NetProtocol.PileBrowseKindBurnt => "BURNT",
        NetProtocol.PileBrowseKindItems => "ITEMS",
        NetProtocol.PileBrowseKindDiscard => "DISCARD",
        _ => "?",
    };

    // ------------------------------------------------------------------ build / teardown --

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteBrowserFan[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        _root.SetActive(false);
        VRLayers.Apply(_root);
    }

    private void Rebuild(int count)
    {
        for (int i = _cards.Count - 1; i >= 0; i--)
        {
            if (_cards[i] != null)
                Object.Destroy(_cards[i]);
        }
        _cards.Clear();
        _collapseFrom.Clear();
        ClearPops(); // a rebuilt arc must never open with a stale card already lifted

        Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability); // SHARED cache — never ours to destroy
        for (int i = 0; i < count; i++)
        {
            var card = new GameObject($"Browse{i}");
            card.transform.SetParent(_root!.transform, worldPositionStays: false);
            card.transform.localScale = Vector3.one * CardScale; // browse cards read enlarged
            var mf = card.AddComponent<MeshFilter>();
            // Round 17 (1:1 board rule): the browse slab adopts the owner's punched-out ABILITY
            // body via CardMesh.AttachBody (shared cached mesh, never ours to destroy; upgraded in
            // place when the contour is learned). Ability kind: a browsed pile fans ability cards.
            CardMesh.AttachBody(mf, CardBodyKind.Ability, CardW, CardH);
            var mr = card.AddComponent<MeshRenderer>();
            // Two submeshes (front+rim | back), both wearing the shared back material: the arc
            // deliberately shows the BACK on both faces, exactly like the old two-quad slab.
            mr.sharedMaterials = new[] { back, back };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _cards.Add(card);
        }
        _builtCount = count;
        VRLayers.Apply(_root!);
        // Re-bind the front overlays onto the NEW slabs (the old ones died with their hosts above).
        // The card size handed over is the UNSCALED slab size: the overlay is a child of the slab, so
        // it inherits CardScale and the pop enlargement for free, exactly like the slab's own mesh.
        _fronts.Rebuild(_cards, CardW, CardH);
    }

    private void Hide()
    {
        _emergeElapsed = -1f;
        _collapseElapsed = -1f;
        _collapseFrom.Clear();
        _fronts.HideAll(); // a hidden fan keeps no game-widget clones alive
        if (_root != null && _root.activeSelf)
            _root.SetActive(false);
    }

    public void Destroy()
    {
        _fronts.Destroy();
        _cards.Clear();
        _collapseFrom.Clear();
        _builtCount = -1;
        _open = false;
        _shownKind = -1;
        // Round 17: the body mesh is CardMesh's SHARED cache (AttachBody) — never ours to destroy.
        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
        }
    }
}
