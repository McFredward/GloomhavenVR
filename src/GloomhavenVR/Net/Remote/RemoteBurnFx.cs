using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Net;

/// <summary>
/// A PEER'S BURN, ON THE RIGHT CARD, AT THE RIGHT MOMENT — the receiving half of the burn flow
/// (2026-09-05 multiplayer report, items 11a and 11b).
///
/// <para>WHAT HE REPORTED, VERBATIM: "Immer noch sieht der Mitspieler eine andere Karte die
/// verbrannt wurde als ich. Und auch sieht er nicht die Feuer Animation die ich sehe. Verletzung
/// der 1:1 Regel."</para>
///
/// <para>THE CAUSE IS STRUCTURAL AND THE WIRE SAYS IT IN ITS OWN WORDS. Everything a peer was
/// given about a burn is <see cref="NetCardFx"/>'s two-byte event: a low nibble FROM anchor, a
/// high nibble TO anchor, and a wrapping sequence. <see cref="RemoteCardFx"/>'s own doc states the
/// consequence — "the slab is a BACK on both faces… No card identity is ever transmitted or
/// rendered here." So the peer flew a blank card back. He could not see WHICH card burned because
/// nothing named one, and he could not see the fire because a back slab has no face to burn. Both
/// halves of the report are the same missing fact.</para>
///
/// <para>AND THE STANDING ARGUMENT FOR WHY IT COULD NOT BE FIXED IS FALSE. <c>RemoteAvatar</c>'s
/// held-card note says of this very surface: "a flight is an EVENT, not a state, and the card it
/// carries is in transit BETWEEN two lists — the model has usually already moved it by the time
/// the receiver replays the arc, so there is no list position that names it for the duration of
/// the flight. It needs a different record." That is true of a DISCARD and false of a BURN, and
/// the difference is the whole of this class. A burned card is not in transit: the game commits it
/// into <c>CCharacterClass.LostAbilityCards</c> BEFORE it starts the card's burn artwork (that
/// ordering is the reason the owner's own <c>CardsDriver.TickBurnToPile</c> has to hold the card
/// on the board at all), and it stays in that list for the rest of the scenario. That list is
/// HOST-REPLICATED and this client already walks it — <see cref="RemotePileFronts.Resolve"/> reads
/// exactly it, through exactly the same <c>CardsGameApi.GetPileWidgets</c> call, to draw the
/// peer's burnt-pile fan. A card ARRIVING in it is a burn, and naming it costs nothing.</para>
///
/// <para>SO NO WIRE FIELD IS OWED, and adding one would have been the wrong fix twice over: it
/// would have put a card identity on the wire for the first time (the anti-cheat line every remote
/// card surface is built to hold) to transmit a fact the receiver already has. The identity is
/// resolved LOCALLY here, gated by the identical <see cref="RevealGate.ShowRoundCardFronts"/> call
/// the board slots, the hand fan, the pile fans and the held-card face all make.</para>
///
/// <para>THE CHOREOGRAPHY MIRRORS THE OWNER'S, TERM FOR TERM. The owner's card lies on his board
/// while the game's <c>BurnCardTimeline</c> plays on it (<c>burnTime = 2 s</c>) and only then
/// flies into his Burnt stack on a <see cref="VRCard.FlyToPile"/> arc. This plays the same two
/// phases against the SENDER'S OWN synced board pose: hold at their board anchor with the burn
/// ramping on the real face, then the same arc to their Burnt stack over
/// <see cref="NetProtocol.CardFxSeconds"/>. Both clocks start from the same host-replicated pile
/// change, so they are in step by construction rather than by a timestamp we would have had to
/// send.</para>
///
/// <para>THE WIRE EVENT IS NOT REMOVED — it is CONSUMED. While this mirror is presenting a burn for
/// this owner, <see cref="ConsumesWireEvent"/> swallows the matching <c>Board -&gt; Burnt</c>
/// event so there is never a second flight; when this mirror cannot present one (their board is
/// hidden, the reveal gate is shut, no hand on this client, the face would not clone) the event
/// passes through and the peer gets exactly the back slab he got before. Degrading to the old
/// picture is the failure mode, never nothing.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. Every list is walked read-only, the face is a throwaway
/// CLONE built by <see cref="RemoteCardArt"/>, the burn look is rebuilt on materials that overlay
/// mints and destroys (never the game's own <c>CardEffects</c>, which cannot run on a detached
/// clone and would write a pooled widget — see <c>RemoteCardArt.BuildBurnRig</c>), and the body
/// mesh comes out of <see cref="CardMesh"/>'s shared cache.</para>
/// </summary>
/// <remarks>CLASSIFICATION: PER-ACTOR MODEL — ZERO wire. The burned card's identity is resolved
/// from the host-replicated <c>CCharacterClass.LostAbilityCards</c> list this client already reads
/// for the peer's burnt-pile fan, never from a packet, and every face is gated by
/// <see cref="RevealGate"/>. No new wire field, no new record, no byte added to any packet. See
/// INVARIANTS-Net-Rig.md "Net — content classification".</remarks>
internal sealed class RemoteBurnFx
{
    /// <summary>
    /// How long the burned card lies on the owner's board before it flies, in seconds.
    ///
    /// <para>MIRRORED CONSTANT, and the thing it mirrors is the GAME's, not ours:
    /// <c>CardEffects.BurnCardTimeline</c> opens with <c>float burnTime = 2f</c>
    /// (CardEffects.cs:515) and that loop is the whole visible burn. The owner's own hold ends when
    /// that timeline's coroutine handle goes null (<c>CardsDriver.BurnArtworkActive</c>), so this
    /// is the same duration reached from the other side rather than a second dial. It is
    /// deliberately NOT <c>CardsDriver.BurnEffectMaxHoldSeconds</c> (3 s): that is the owner's
    /// belt for a burn whose artwork never starts, and copying a CEILING as if it were a duration
    /// is what made every burn on ModBuild 447 run the full 3 s.</para>
    /// </summary>
    private const float HoldSeconds = 2f;

    /// <summary>Concurrent burn presentations. A two-card damage burn commits both cards in the
    /// SAME frame — the 2026-09-05 host log shows exactly that pair — so one is not enough; beyond
    /// this the oldest is recycled, because a burn animation is cosmetic and must never be a queue
    /// that backs up.</summary>
    private const int MaxBurns = 3;

    /// <summary>Slack added to (hold + flight) before this mirror stops claiming the owner's
    /// <c>Board -&gt; Burnt</c> wire event. The owner reports the flight when HIS hold releases,
    /// which is up to his own 3 s ceiling after the pile change this mirror triggered on — so the
    /// window has to outlast the difference or a claimed burn would still get a second back slab.
    /// </summary>
    private const float ClaimSlackSeconds = 3f;

    private readonly RemoteAvatar _owner;

    private sealed class Burn
    {
        public GameObject? Go;
        public RemoteCardArt? Art;
        public float Elapsed;
        public bool Active;
        public bool HasFace;
        public Vector3 From;
        public Vector3 To;
        public float Arc;

        /// <summary><c>CAbilityCard.CardInstanceID</c> of the card being burned, so the recess that
        /// is drawing it can be re-identified every frame rather than once.</summary>
        public int CardId;

        /// <summary>The recess this card was lying in when the burn was learned about (0/1), or -1.
        /// It decides both the ORIGIN of the flight and whether this presentation draws a slab at
        /// all during the hold — see <see cref="Drive"/>.</summary>
        public int Recess;
    }

    private readonly List<Burn> _burns = new(MaxBurns);
    private GameObject? _root;

    // ---- the model watch (the whole identity story) ------------------------------------------
    private readonly List<AbilityCardUI> _burntBuf = new(16);
    private readonly HashSet<AbilityCardUI> _known = new();
    private readonly List<AbilityCardUI> _pruneScratch = new(4);
    private int _watchActor;                 // stable id of the actor the baseline belongs to
    private bool _seeded;
    private float _nextWalkAt;

    /// <summary>Unscaled time of the most recent presentation start — the claim window for
    /// <see cref="ConsumesWireEvent"/>.</summary>
    private float _lastPresentedAt = float.NegativeInfinity;

    /// <summary>How many of the owner's <c>Board -&gt; Burnt</c> events this mirror still owes a
    /// swallow, i.e. how many burns it has presented whose wire event has not arrived yet.
    ///
    /// <para>A COUNT AND NOT JUST A TIME WINDOW, because the two failure modes are different burns.
    /// The window has to outlast the owner's own hold (he reports the flight when HIS hold
    /// releases, up to his 3 s ceiling after the pile change this mirror triggered on), and a bare
    /// window that wide would also swallow the event for a SECOND burn that this mirror could not
    /// present at all — costing the player the anonymous back slab he would otherwise still get.
    /// One token per presentation means each presented burn eats exactly its own event, and an
    /// unpresented one always falls through.</para></summary>
    private int _claims;

    private int _played;

    internal RemoteBurnFx(RemoteAvatar owner)
    {
        _owner = owner;
    }

    // ------------------------------------------------------------------ the wire hand-off --

    /// <summary>
    /// Should the owner's <c>Board -&gt; Burnt</c> card-FX event be SWALLOWED because this mirror is
    /// already showing that burn on the real card? Any other endpoint pair is never claimed.
    ///
    /// <para>Time-boxed on purpose: a claim that outlived its presentation would silently delete a
    /// later burn's animation, and a burn this mirror never saw must still reach the player as the
    /// old back slab.</para>
    /// </summary>
    internal bool ConsumesWireEvent(byte endpoints)
    {
        if (NetCardFx.To(endpoints) != CardFxAnchor.Burnt)
            return false;
        if (_claims <= 0)
            return false;
        // The window is the STALE-TOKEN sweep, not the claim itself: an event that never arrived
        // (a dropped packet on an unreliable stream, an owner whose report was suppressed by a
        // read-only character focus) must not leave a token behind to eat the NEXT burn's event.
        if (Time.unscaledTime - _lastPresentedAt > HoldSeconds + NetProtocol.CardFxSeconds + ClaimSlackSeconds)
        {
            _claims = 0;
            return false;
        }
        _claims--;
        return true;
    }

    // ------------------------------------------------------------------ per frame --

    internal void Tick(float dt)
    {
        try
        {
            Watch();
        }
        catch (System.Exception ex)
        {
            // Never throw out of the avatar tick: an unguarded throw there starves VR input.
            _seeded = false;
            VRLog.Warn("Net", $"Remote burn watch [player {_owner.PlayerId}] threw ({ex.Message}) — " +
                              "the baseline is re-seeded silently, so no burn storms when it recovers.");
        }
        Drive(dt);
    }

    /// <summary>
    /// Diff the displayed character's BURNT pile against the previous walk. A new entry is a burn
    /// this client has just learned about, at the same instant the owner's own watcher learns it —
    /// both read the same host-replicated list.
    ///
    /// <para>THE BASELINE IS SEEDED SILENTLY on the first walk and on every actor change, exactly
    /// as <c>CardsDriver.TickBurnToPile</c> re-seeds <c>_knownBurntWidgets</c> on a hand change and
    /// for the same reason: a character's long-burned cards must never animate retroactively when
    /// the board starts presenting them.</para>
    ///
    /// <para>It keeps walking while the peer's board is HIDDEN and simply presents nothing, so
    /// switching <c>[Net] RemoteBoards</c> back on cannot replay a scenario's worth of burns.</para>
    /// </summary>
    private void Watch()
    {
        if (Time.unscaledTime < _nextWalkAt)
            return;
        _nextWalkAt = Time.unscaledTime + RemoteBoardContent.RefreshSeconds;

        CPlayerActor? actor = RemoteBoardFocus.DisplayedActor(_owner, out _);
        int actorId = NetFigures.StableActorId(actor);
        CardsHandManager manager = CardsHandManager.Instance;
        CardsHandUI? hand = actor != null && manager != null ? manager.GetHand(actor) : null;
        if (hand == null)
        {
            // No hand on this client for that character — forget the baseline rather than diffing
            // against a stale one when it comes back.
            _seeded = false;
            _known.Clear();
            _watchActor = 0;
            return;
        }
        if (actorId != _watchActor)
        {
            _watchActor = actorId;
            _seeded = false;
            _known.Clear();
        }

        _burntBuf.Clear();
        CardsGameApi.GetPileWidgets(hand, burnt: true, _burntBuf);

        if (!_seeded)
        {
            _seeded = true;
            for (int i = 0; i < _burntBuf.Count; i++)
            {
                if (_burntBuf[i] != null)
                    _known.Add(_burntBuf[i]);
            }
            return;
        }

        for (int i = 0; i < _burntBuf.Count; i++)
        {
            AbilityCardUI widget = _burntBuf[i];
            if (widget == null || !_known.Add(widget))
                continue;
            // The SAME membership expression the owner's own browse arc and the mirrored pile fan
            // apply — a long-rest placeholder or a widget with no model card is not a burn.
            if (!CardsGameApi.PileWidgetIsArcMember(widget))
                continue;
            Present(widget, actor);
        }

        // Drop anything that LEFT the pile (a recovered lost card) so a re-burn animates again.
        // Through a reused scratch list rather than RemoveWhere: this runs per peer at the board
        // cadence and a lambda that captures `this` allocates a delegate on every call.
        _pruneScratch.Clear();
        // Membership in the game's own list is the whole test: a widget the game has destroyed is
        // no longer in it either, so a separate Unity-null test would only add a branch (and a
        // nullable-flow suppression) for a case this already covers.
        foreach (AbilityCardUI k in _known)
        {
            if (!_burntBuf.Contains(k))
                _pruneScratch.Add(k);
        }
        for (int i = 0; i < _pruneScratch.Count; i++)
            _known.Remove(_pruneScratch[i]);
        _pruneScratch.Clear();
    }

    /// <summary>Start one burn presentation for <paramref name="widget"/>.</summary>
    private void Present(AbilityCardUI widget, CPlayerActor? actor)
    {
        string name = CardsGameApi.CardName(widget);

        // BOARD FURNITURE. Both endpoints of this presentation are on the owner's board, so a
        // viewer who has switched that board off must not get a card burning in mid-air — the same
        // rule and the same predicate RemoteCardFx applies to every flight that touches the board.
        if (!RemoteBoardGate.ShowBoardSurface(_owner) || !_owner.HasBoard)
        {
            LogSkipped(name, "this viewer is not showing that peer's board right now");
            return;
        }

        // ─── WHERE THE OWNER'S CARD ACTUALLY IS (2026-09-06 follow-up) ───────────────────────────
        // It is in a RECESS, not at the board centre: CardsDriver holds the played card on its slot
        // anchor while the game's burn artwork runs on it and only then flies it to the stack. This
        // mirror used to present at CardFxAnchor.Board because nothing named the recess — but this
        // client SEATED that card there itself, so it can simply ask. No wire field is owed: the
        // anchor vocabulary already has Slot0/Slot1 and RemoteControlBoard.AnchorLocalLive resolves
        // both to the rendered card SEAT of whatever board style the peer runs.
        int cardId = CardInstanceIdOf(widget);
        int recess = _owner.RecessShowingCard(cardId);
        CardFxAnchor origin = recess == 0 ? CardFxAnchor.Slot0
            : recess == 1 ? CardFxAnchor.Slot1
            : CardFxAnchor.Board;

        if (!TryAnchor(origin, out Vector3 from)
            || !TryAnchor(CardFxAnchor.Burnt, out Vector3 to))
        {
            LogSkipped(name, "their board pose has not arrived yet, so there is nowhere to burn it");
            return;
        }

        Burn b = Acquire();
        if (b.Go == null)
            return;
        b.CardId = cardId;
        b.Recess = recess;

        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        float cardWidth = Mathf.Max(0.01f, _owner.BoardTuning.CardWidth);
        float widthRatio = cardWidth / RemoteHandFan.DefaultCardWidth;
        float cardHeight = cardWidth * (88f / 63.5f);

        b.Elapsed = 0f;
        b.Active = true;
        b.From = from;
        b.To = to;
        // CardsDriver.BoardArcMin and VRCard.FlyArcHeightFraction, the same two terms RemoteCardFx
        // resolves for every other mirrored flight — read its ArcFraction note before touching
        // either number, they are code literals that simply have to be the same on both sides.
        b.Arc = Mathf.Max(cardHeight * 1.5f * scale, Vector3.Distance(from, to) * VRCard.FlyArcHeightFraction);
        b.Go.transform.localScale = Vector3.one * (scale * widthRatio);
        // LYING ON THEIR BOARD, not billboarded at us: the owner's card rests in a recess of a
        // board this client already knows the rotation of, and FlyToPile holds that orientation for
        // the whole flight ("orientation locked"). Facing it at the local head instead would be a
        // pose the owner never sees.
        b.Go.transform.SetPositionAndRotation(from, _owner.BoardRotation);
        // …and it stays HIDDEN while the recess is the one drawing this card. Two copies of one
        // card is a worse divergence than the one this class was built to fix, and the recess copy
        // is the better of the two by construction: it is the card the owner is looking at, in the
        // recess he is looking at, wearing the char RemoteBoardCard.DriveUsedCardFx is ramping on
        // it. Drive() re-asks every frame and reveals the slab the instant the recess stops.
        bool recessDraws = recess >= 0;
        if (b.Go.activeSelf != !recessDraws)
            b.Go.SetActive(!recessDraws);

        // THE FACE — resolved locally, gated exactly as every other remote card surface is.
        b.HasFace = false;
        bool fronts = false;
        try
        {
            fronts = actor != null && RevealGate.ShowRoundCardFronts(actor);
            FullAbilityCard? full = fronts && widget != null ? widget.fullAbilityCard : null;
            if (full != null && b.Art != null)
                b.HasFace = b.Art.ShowFront(full);
        }
        catch (System.Exception ex)
        {
            b.HasFace = false;
            VRLog.Warn("Net", $"Remote burn face [player {_owner.PlayerId}]: '{name}' front resolve " +
                              $"failed ({ex.Message}) — burning a card BACK, which is what every build " +
                              "before this one showed.");
        }
        if (!b.HasFace)
            b.Art?.HideFront();

        _played++;
        _lastPresentedAt = Time.unscaledTime;
        _claims++;
        LogAttribution(name, fronts, b.HasFace, actor, recess);
    }

    /// <summary>Advance every live presentation: hold with the burn ramping on, then the arc.</summary>
    private void Drive(float dt)
    {
        float step = Mathf.Max(dt, 0f);
        for (int i = 0; i < _burns.Count; i++)
        {
            Burn b = _burns[i];
            if (!b.Active || b.Go == null)
                continue;
            b.Elapsed += step;

            // THE OWNER'S BOARD IS A MOVING FRAME, and this presentation is 2.4 s long — long
            // enough that a board the owner pulls toward himself mid-burn would leave the card
            // hanging in the air where the board used to be. Both endpoints are therefore
            // re-resolved every frame against their LIVE synced pose (RemoteCardFx resolves once
            // because its flights last 0.4 s; that shortcut does not survive a 2 s hold). A frame
            // in which the pose cannot be resolved keeps the last one rather than snapping.
            // …and the ORIGIN is the owner's RECESS whenever this client can still see the card
            // seated there. Re-asked every frame rather than latched: the recess empties the moment
            // the owner's occupancy nibble clears, which is the same instant HIS card leaves it, and
            // that is the hand-over this presentation has to survive. It falls back to the board
            // centre only for a burn nobody could place — the picture every build before this one
            // drew.
            CardFxAnchor origin = b.Recess == 0 ? CardFxAnchor.Slot0
                : b.Recess == 1 ? CardFxAnchor.Slot1
                : CardFxAnchor.Board;
            if (TryAnchor(origin, out Vector3 liveFrom))
                b.From = liveFrom;
            if (TryAnchor(CardFxAnchor.Burnt, out Vector3 liveTo))
                b.To = liveTo;

            if (b.Elapsed < HoldSeconds)
            {
                // PHASE 1 — it lies on their board and chars, exactly as it does on theirs.
                //
                // AND IT LIES IN THEIR RECESS, drawn by the recess itself, for as long as the recess
                // still has it. RemoteBoardCard.DriveUsedCardFx is ramping the very same
                // RemoteCardArt rig on that seated face, off the owner's own effect state, so a slab
                // here would be a SECOND copy of one card. Hidden, not skipped: the moment the
                // recess stops showing it (their card left early, the reveal gate shut, the face
                // could not be cloned) the slab comes back at the recess anchor and this phase
                // finishes the way it always did.
                bool recessDraws = b.CardId != int.MinValue
                                   && _owner.RecessShowingCard(b.CardId) == b.Recess
                                   && b.Recess >= 0;
                if (b.Go.activeSelf == recessDraws)
                    b.Go.SetActive(!recessDraws);
                if (recessDraws)
                    continue;
                b.Go.transform.SetPositionAndRotation(b.From, _owner.BoardRotation);
                if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(b.Elapsed / HoldSeconds))
                    b.HasFace = false; // the rig refused: keep the fresh face, keep the flight
                continue;
            }

            // THE HAND-OVER. The flight always draws the slab, and it draws it ALREADY CHARRED: a
            // fresh card lifting out of a recess the viewer just watched blacken is the one way this
            // split could read worse than the single slab it replaced. Writing the settled state
            // every frame of the flight is the same idempotent call the burnt-pile fan makes.
            if (!b.Go.activeSelf)
                b.Go.SetActive(true);
            if (b.HasFace && b.Art != null && !b.Art.SetAbilityBurnProgress(1f))
                b.HasFace = false;

            // PHASE 2 — the same over-the-board arc every other pile flight uses.
            float t = NetProtocol.CardFxSeconds > 0f
                ? Mathf.Clamp01((b.Elapsed - HoldSeconds) / NetProtocol.CardFxSeconds)
                : 1f;
            float e = t * t * (3f - 2f * t);
            // WORLD up, never the owner's board up — VRCard.FlyToPile deliberately throws the
            // caller's board-up away so a tilted board can never lean the arch sideways, and
            // RemoteCardFx carries that sentence verbatim. Same rule here.
            Vector3 p = Vector3.Lerp(b.From, b.To, e) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * b.Arc);
            b.Go.transform.position = p;
            if (t >= 1f)
            {
                b.Active = false;
                b.Art?.HideFront();
                b.HasFace = false;
                b.Go.SetActive(false);
            }
        }
    }

    // ------------------------------------------------------------------ anchors + pool --

    /// <summary><c>CAbilityCard.CardInstanceID</c> of a pile widget's model card, or
    /// <see cref="int.MinValue"/> when it has none. It is the key <c>RemoteBoardCard.Set</c> stores
    /// for the card it seated, so the two surfaces are comparing the same identifier rather than two
    /// that happen to agree.</summary>
    private static int CardInstanceIdOf(AbilityCardUI? widget)
    {
        try
        {
            CAbilityCard? card = widget != null ? widget.AbilityCard : null;
            return card != null ? card.CardInstanceID : int.MinValue;
        }
        catch (System.Exception)
        {
            return int.MinValue;
        }
    }

    private bool TryAnchor(CardFxAnchor anchor, out Vector3 world)
    {
        world = default;
        if (!_owner.HasBoard)
            return false;
        float scale = _owner.BoardScale > 0f ? _owner.BoardScale : 1f;
        world = _owner.BoardPosition + _owner.BoardRotation * (_owner.BoardAnchorLocal(anchor) * scale);
        return true;
    }

    private Burn Acquire()
    {
        for (int i = 0; i < _burns.Count; i++)
        {
            if (!_burns[i].Active)
                return _burns[i];
        }
        if (_burns.Count >= MaxBurns)
        {
            Burn oldest = _burns[0];
            for (int i = 1; i < _burns.Count; i++)
            {
                if (_burns[i].Elapsed > oldest.Elapsed)
                    oldest = _burns[i];
            }
            oldest.Art?.HideFront();
            oldest.HasFace = false;
            return oldest;
        }

        EnsureRoot();
        var b = new Burn();
        if (_root != null)
        {
            var go = new GameObject($"Burn{_burns.Count}");
            go.transform.SetParent(_root.transform, worldPositionStays: false);
            float w = RemoteHandFan.DefaultCardWidth;
            float h = RemoteHandFan.DefaultCardHeight;
            var filter = go.AddComponent<MeshFilter>();
            // The owner's punched-out ABILITY body, out of CardMesh's shared cache (never ours to
            // destroy) — the same body every other mirrored card slab wears.
            CardMesh.AttachBody(filter, CardBodyKind.Ability, w, h);
            var renderer = go.AddComponent<MeshRenderer>();
            Material back = CardMesh.CreateBackMaterial(CardBodyKind.Ability);
            renderer.sharedMaterials = new[] { back, back };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            VRLayers.Apply(go);
            b.Go = go;
            b.Art = new RemoteCardArt(go.transform, w, h);
        }
        _burns.Add(b);
        return b;
    }

    private void EnsureRoot()
    {
        if (_root != null)
            return;
        _root = new GameObject($"GloomhavenVR.RemoteBurnFx[{_owner.PlayerId}]");
        Object.DontDestroyOnLoad(_root);
        _root.hideFlags = HideFlags.HideAndDontSave;
        _root.transform.localScale = Vector3.one;
        // Same board fade every other mirrored surface follows (user item 7, 2026-09-02): a card
        // burning at full opacity over a board that is faded out is the wrongness he described.
        PeerBoardFade.Follow(_owner.PlayerId, _root.transform);
        VRLayers.Apply(_root);
    }

    internal void Destroy()
    {
        for (int i = 0; i < _burns.Count; i++)
            _burns[i].Art?.Destroy();
        _burns.Clear();
        _known.Clear();
        _burntBuf.Clear();
        _pruneScratch.Clear();
        _seeded = false;
        _claims = 0;
        if (_root != null)
        {
            // The body meshes are CardMesh's SHARED cache — never ours to destroy.
            Object.Destroy(_root);
            _root = null;
        }
    }

    // ------------------------------------------------------------------ instruments --

    /// <summary>
    /// HARDWARE VERIFICATION (2026-09-05 item 11a): the RECEIVER's answer to "which card was burned". Grep
    /// token <c>BURN CARD</c> — deliberately the SAME token the owner's
    /// <c>CardsDriver.LogBurnAttribution</c> prints, so one grep across the two hardware logs
    /// decides the 1:1 question with no arithmetic: for each burn the owner's line and this line
    /// must name the same card.
    ///
    /// <para>Event-driven (a handful per scenario), never per frame.</para>
    ///
    /// <para>PROOF the fix landed: a <c>[peer]</c> line naming the SAME card as the owner's
    /// <c>[owner/…]</c> line for that burn, with <c>face=REAL</c>.</para>
    ///
    /// <para>FALSIFIERS, and they say different things. (1) The two lines naming DIFFERENT cards:
    /// the identity resolve is wrong and 11a is NOT fixed — the next lead is
    /// <c>RemotePileFronts.Resolve</c>, which walks the same list. (2) No <c>[peer]</c> line at all
    /// beside an owner line: this mirror never armed, so the peer still gets the old back slab
    /// through <see cref="RemoteCardFx"/> — read the <c>BURN MIRROR SKIPPED</c> line for the
    /// reason. (3) The right card with <c>face=BACK</c>: the reveal gate or the clone refused, and
    /// only the FACE half of the fix is inert; the flight and the timing are still right.</para>
    /// </summary>
    private void LogAttribution(string name, bool fronts, bool face, CPlayerActor? actor, int recess)
    {
        string where = recess >= 0
            ? $"in their round recess {recess + 1} — the seat this client already put that card in, "
              + "so the char is drawn BY that recess (see its RECESS CARD FX line) and this flight "
              + "leaves from there rather than from the board centre"
            : "at their board CENTRE, because no recess on this client is drawing that card's face "
              + "right now; that is the pre-2026-09-06 picture and the fallback, not the intent";
        // HW-VERIFY: grep token "BURN CARD" — the same token the OWNER's
        // CardsDriver.LogBurnAttribution prints, so one grep across the two hardware logs decides
        // the 1:1 question. See this method's doc for the three falsifiers.
        VRLog.Note("Net", $"BURN CARD [peer {_owner.PlayerId}]: that player burned '{name}' — " +
                          $"showing it {where}, for {HoldSeconds:F1}s while it chars, then flying " +
                          $"it into their Burnt stack ({NetProtocol.CardFxSeconds:F2}s). " +
                          $"face={(face ? "REAL" : "BACK")}, revealGate={(fronts ? "open" : "shut")}, " +
                          $"char='{Board.CharacterFocus.Describe(actor)}', burn #{_played}. The identity " +
                          "was read from THIS client's own copy of that character's host-replicated " +
                          "LostAbilityCards list (the same CardsGameApi.GetPileWidgets call the mirrored " +
                          "burnt-pile fan uses) — NO card identity crossed the wire and no wire field " +
                          "was added. Compare with the owner's 'BURN CARD [owner/...]' line for the " +
                          "same burn: they must name the same card.");
    }

    /// <summary>
    /// HARDWARE VERIFICATION: why a burn this client KNOWS about was not presented. Without it, "the peer saw
    /// nothing" and "the peer saw the old back slab" are indistinguishable in a log. One line per
    /// skipped burn, event-driven.
    /// </summary>
    private void LogSkipped(string name, string reason)
    {
        // HW-VERIFY: grep token "BURN MIRROR SKIPPED" — it is what separates "the peer saw nothing"
        // from "the peer saw the old anonymous back slab", which a log otherwise cannot tell apart.
        VRLog.Note("Net", $"BURN MIRROR SKIPPED [player {_owner.PlayerId}]: '{name}' burned, but " +
                          $"{reason}. The owner's own Board→Burnt card-FX event is therefore NOT " +
                          "claimed and still plays as the anonymous card-back slab (RemoteCardFx), " +
                          "which is exactly the picture every build before this one showed.");
    }
}
