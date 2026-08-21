// THE FAN HALF of the map room's loadout hand — and, since ModBuild 192, it is barely a fan half at
// all: it is a CARD SOURCE. The fan is the scenario's own.
//
// ─── THE USER'S REJECTION OF MODBUILD 191, VERBATIM (2026-08-21) ──────────────────────────────
// "1) Die Karten sind dauerhaft da und reagieren nicht auf der roll der Hand. Es gibt keine
//  Animation kein Highlighting. Die Characterinfo am Handgelenk sieht anders aus. Ich will das es
//  sich hier 1:1 genauso verhält wie im Szenario selber. Am Besten nutzt du auch die selben Code
//  Segmente. Es soll sich nicht vom Szenario unterscheiden wie sich die Karten verhalten! Auch beim
//  Characterwechsel soll es die entsprechende Animation geben etc."
//
// 191 built a PARALLEL fan: its own slabs, its own arc, its own hand pose, and no reveal gesture at
// all. Every one of the five complaints is a direct consequence of that shape, so the shape is what
// changed. THIS FILE NO LONGER LAYS OUT, ANIMATES, HIGHLIGHTS, REVEALS OR HIDES ANYTHING. It builds
// <see cref="VRCard"/>s for the selected character's loadout, prints the real card faces on them,
// and hands the list to <c>Cards.CardsDriver</c> — the same driver, the same
// <c>Cards.CardFan</c> instance, the same <c>Hands.Interact.PalmGate</c> that a scenario uses.
// There is exactly one implementation of "a hand of cards" in this mod again, so the map room
// CANNOT diverge from the scenario: they are the same lines.
//
// ─── WHAT WAS ACTUALLY GATING THE SCENARIO MACHINERY OUT OF THE MAP PHASE ─────────────────────
// READ FROM SOURCE, and it is NOT the palm gate — that was the surprise:
//
//   * THE PALM GATE IS ALREADY LIVE HERE. The map room resolves to VRMode.TableIdle
//     (Core/Events/VRModeStateMachine.Recompute's `!_inScenario ? VRMode.TableIdle` arm, reachable
//     because MapRoomDriver.Engage pushes SetModRoom(true)), and TableIdle's interactor row is
//     `Poke | Grab | PalmGate` (VRModeStateMachine.cs:238). VRHand.SetInteractorPolicy turns that
//     into PalmGate.Enabled = true, so the wrist roll has been measured in the map room all along.
//   * WHAT IS MISSING IS CARDS. CardsDriver.CurrentHand() opens with
//     `if (!CardsGameApi.InScenario) return null;` (CardsDriver.2.Update.cs:916), and InScenario is
//     `Choreographer.s_Choreographer != null` (CardsGameApi.cs:3186) — the campaign map has no
//     Choreographer BY CONSTRUCTION (it is the very fact MapRoomDriver's mode predicate is positive
//     about). So Rebuild takes its `hand == null` arm (CardsDriver.4.Rebuild.cs:276) into
//     RebuildFakeOrClear, which — with the dev fake hand off — does
//     `_fanBuffer.Clear(); _fan.SetCards(_fanBuffer);` (CardsDriver.6.Flows.cs:1938-1939).
//   * AND AN EMPTY FAN CANNOT OPEN. UpdatePalmGate's `allowFan` is
//     `_fanBuffer.Count > 0 || _fan.Cards.Count > 0 || _fan.HasLeavingCards`
//     (CardsDriver.2.Update.cs:1009) and `shouldOpen = allowFan && revealed && !gateHandHolds`
//     (:1029). With no cards, `shouldOpen` is false in every frame of the map phase.
//
// So the fix is one sentence: GIVE THAT FAN CARDS. Everything else already exists and already runs.
// The seam is CardsDriver.OffScenarioFanCards (see the region at the end of
// Cards/CardsDriver.6.Flows.cs, which states the whole contract); this file is its only user.
//
// ─── WHAT NOW DRIVES EACH OF THE FIVE COMPLAINTS — ALL OF IT SCENARIO CODE ────────────────────
//   (a) "dauerhaft da"      → CardsDriver.UpdatePalmGate: _fan.Open/_fan.Close on the gate edge,
//                             plus PlayFanEdgeSound. The fan is DOWN until you roll your palm up.
//   (b) "reagiert nicht auf → Hands/Interact/PalmGate measures the wrist roll; the driver forwards
//       die Roll der Hand"    [Cards] RevealEnterDegrees / RevealExitDegrees / RevealAlways /
//                             RevealIgnoreWhenGrabbing. The player's own scenario dials.
//   (c) "keine Animation"   → CardFan.Open's fan-out reveal ([Cards] FanOpenDuration), Close's
//                             reverse collapse (FanCloseDuration), the eased palm follow, the
//                             billboard, VRCard's per-card home lerp and pop.
//       "kein Highlighting" → CardFan.UpdateFingertipHover (fingertip pop + fan split),
//                             CardsDriver.UpdateFanLaser (laser hover, haptic tick, beam clamp),
//                             UpdateHandContactArbitration (the single elected card),
//                             UpdateFanHoverSplit (the whole-fan split around it).
//   (e) "Characterwechsel   → CardFan.BeginSwapOut + SetCards(swap: true) — the exchange the driver
//        Animation"           plays when the presented character changes in a scenario. This file
//                             raises the same edge through CardsDriver.OffScenarioFanSwap.
//   (d) the wrist plate is MapRoomHand.3.Wrist.cs — see that file's header.
//
// ─── THE INSPECTION-ONLY GUARANTEE, IN ITS NEW SHAPE ──────────────────────────────────────────
// USER RULING: "Zwar kann man sonst nicht damit interagieren, aber so kann man sich die aktuell
// ausgewählten Karten vor einem Szenario nochmal anschauen."
//
// 191's guarantee was "the driver never sees these objects". That is gone — the driver now owns
// their interaction routing — so the guarantee is restated as the thing that is actually true, and
// it is STRUCTURAL rather than a refusal:
//
//   1. THE CARDS HOLD NO GAME WIDGET. <c>VRCard.AttachGameCard</c> is never called on them, so
//      <c>VRCard.GameCard</c> is null for their whole life. Every commit seam in the driver is
//      reached only through a widget or a resolved <c>CardsHandUI</c>:
//        * OnCardPoked returns on its first line (`_fakeActive || card.GameCard == null`);
//        * MaybeReopenPickSelection returns on `CurrentHand() == null` and again on
//          `card.GameCard == null`;
//        * OnCardReleased's ONLY reachable branch is `if (card.InspectOnly)` — which the fan itself
//          stamps (CardFan.StampMode under FanMode.Inspect) and which sits BEFORE CurrentHand() —
//          and it does exactly one thing: `_fan.Add(card)`, an animated return home. Even with that
//          flag cleared, the next line is `if (gameHand == null || card.GameCard == null)
//          { _fan.Add(card); return; }`. TWO INDEPENDENT BELTS, both structural.
//   2. THE FAN IS IN <c>CardFan.FanMode.Inspect</c>, the mode the user's 2026-08-08 ruling created:
//      grab, carry, swap hands, read — and never place. PokeSelectEnabled is forced false in EVERY
//      mode by StampMode, so a fingertip touch can never commit a card.
//   3. NO TRAY, NO SLOTS, NO PILES, NO FIELD. The off-scenario branch runs inside
//      RebuildFakeOrClear, which has already hidden the piles and the active column, cleared the
//      pick field and the short-rest display, and it then hides the tray and the half selection. A
//      drop cannot find a target because no target is built.
//   4. THIS FILE NEVER WRITES <c>CMapCharacter.HandAbilityCardIDs</c>. It READS the list. The only
//      writers in the whole game are UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect (:536)
//      and .OnAbilityCardDeselect (:558), neither of which is reachable from anything here.
//
// ─── THE ONE DOCUMENTED DECISION THIS ROUND REVERSES, STATED OUT LOUD ─────────────────────────
// 191's header claimed, in capitals, "NOTHING HERE READS THE HEAD: there is no billboard and no
// per-card toe-in". That is no longer true and MUST NOT BE RESTORED, because the scenario fan DOES
// billboard its root at the head (CardFan.cs:856-884) and toe each card in on top of it — and the
// user's ruling this round is that the map room may not differ from the scenario in how the cards
// behave. The standing "nothing may re-orient with head movement" rule is about THE MAP: the world,
// the parchment and the diorama stay FIXIERT while the head moves (see the "mitzoomen" note in
// .planning/STATE.md). A hand of cards held in your own hand is not the map; it is a hand of cards,
// and in this mod a hand of cards faces its reader. Re-adding a no-billboard special case here
// would be re-introducing exactly the divergence that was rejected.
//
// ─── SCALE ────────────────────────────────────────────────────────────────────────────────────
// Nothing special is needed for the map room's ~198 units/metre diorama, and that is a CONSEQUENCE
// of sharing rather than a coincidence: CardFan derives its standoff from `hand.WorldScale` and its
// parent from VRRigDriver.RigRoot, both of which are the same relationship in a scenario (whose rig
// is also scaled to its board). The card therefore subtends the same angle at the eye in both, and
// the mistake this repo has shipped before — mixing metres and world units in a second layout
// pass — has no second layout pass left to live in.
//
// ─── MULTIPLAYER ──────────────────────────────────────────────────────────────────────────────
// ZERO NEW WIRE BYTES, and card identity still never goes on the wire: the faces are read LOCALLY
// from the game's own ObjectPool by card ID (Net.RemoteAbilityCardSource's pooled-borrow path).
// ONE OBSERVABLE CHANGE, stated plainly because it is not nothing: the map fan IS a Cards.CardFan
// now, so <c>CardFan.Current</c> is non-null while it is open and NetAvatarDriver's existing,
// always-sent PresenceState.HandCardCount byte (sampled at NetAvatarDriver.cs:842) carries the real
// count instead of 0. Peers therefore see a fan of CARD BACKS on our avatar's non-dominant hand —
// RemoteHandFan gates every FRONT on `RevealGate.InScenario` (RemoteHandFan.cs:888), which is false
// on the map, so no identity can be shown even in principle. No field was added, no record changed;
// an existing count went from a lie (0 while we hold cards) to the truth. Peer fans are still NOT
// drawn BY us in this room — peers are unseated and pile up at one world point, which is plan phase
// 8 (MapRoomDriver.cs:40-42) and deliberately untouched.
//
// ─── UNITS ────────────────────────────────────────────────────────────────────────────────────
// This file no longer poses anything, so it forms no world-unit product at all. Card size is
// [Cards] CardWidth in REAL METRES, and CardFan parents its root to VRRigDriver.RigRoot whose lossy
// scale IS the diorama scale — so a 63.5 mm card is 63.5 mm at the eye at every map zoom, by the
// same construction that makes it 63.5 mm on a scenario board. The one world-unit product on the
// path (the palm standoff) lives at Cards/CardFan.cs:808 and is documented there.

using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Net;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

internal sealed partial class MapRoomHand
{
    /// <summary>
    /// How long an OUTGOING card is kept alive after it has been replaced. It is not decoration:
    /// on a character switch <c>CardFan.BeginSwapOut</c> takes the old cards into its outgoing
    /// wave and FLIES them off the arc, and destroying them in the same frame would be exactly the
    /// pop the exchange exists to remove. Comfortably longer than the whole exchange
    /// (<c>SwapArriveDelay + FanSwapDuration + FanSwapStagger x n</c>, ~0.9 s at the shipped
    /// dials); the cards are invisible long before it expires, because the driver's DrainSwapExit
    /// parks each one into the inactive card pool the instant its own flight lands.
    /// </summary>
    private const float RetireGraceSeconds = 3f;

    /// <summary>The live fan cards. This list IS what <c>CardsDriver.OffScenarioFanCards</c> points
    /// at while the room stands — the driver copies it, it is never handed over.</summary>
    private readonly List<VRCard> _cards = new(MaxCards);

    /// <summary>The card model each entry of <see cref="_cards"/> was built for, appended in
    /// LOCKSTEP with it. Deliberately NOT an index into <see cref="_loadout"/>: a card that fails
    /// to build is skipped, and an index would then be one short for every card after it — the
    /// silent-misalignment shape that puts the wrong art on the wrong card.</summary>
    private readonly List<CAbilityCard> _cardModels = new(MaxCards);

    /// <summary>The printed faces, index-aligned with <see cref="_cards"/>. An entry stays null
    /// until the card is first ACTIVE IN THE HIERARCHY — see <see cref="PrintPendingFaces"/> for
    /// why the print is deferred and not done at build time.</summary>
    private readonly List<RemoteCardArt?> _faces = new(MaxCards);

    /// <summary>One-shot per card set: the "faces printed" line has been emitted. Reset by every
    /// rebuild, so a hardware log gets exactly one such line per character.</summary>
    private bool _facesLogged;

    /// <summary>A card replaced by a rebuild, and the moment it may be destroyed. See
    /// <see cref="RetireGraceSeconds"/>.</summary>
    private readonly struct Retired
    {
        internal Retired(VRCard card, RemoteCardArt? face, float destroyAt)
        {
            Card = card;
            Face = face;
            DestroyAt = destroyAt;
        }

        internal VRCard Card { get; }
        internal RemoteCardArt? Face { get; }
        internal float DestroyAt { get; }
    }

    private readonly List<Retired> _retired = new(MaxCards);

    /// <summary>Cards parked under this while they wait for the fan to adopt them. INACTIVE, and
    /// that is the point: a card created straight into the world would be visible at the rig origin
    /// for the frame between this file building it and the driver's next rebuild handing it to the
    /// fan. Same device as <c>VRCardFactory.PoolRoot</c>, for the same reason.</summary>
    private Transform? _holder;

    /// <summary>How many cards are currently showing a real printed front (state-line material).</summary>
    private int _frontsShown;

    /// <summary>True while this file has cards published to the driver.</summary>
    internal bool HasFan => _cards.Count > 0;

    /// <summary>True while an outgoing character-exchange wave is still waiting to be swept. NOT
    /// covered by <see cref="HasFan"/>: a retired card has already left the published list.</summary>
    internal bool HasRetiredCards => _retired.Count > 0;

    // ==========================================================================================
    //  BUILD
    // ==========================================================================================

    /// <summary>
    /// Rebuild the card set for <see cref="_loadout"/> and publish it. Called ONLY from
    /// <see cref="Reconcile"/>, i.e. only when the selected character or its loadout actually
    /// changed — which is also exactly the edge the exchange animation exists for.
    /// </summary>
    private void RebuildFan()
    {
        // THE SWAP EDGE. Same three conditions the scenario derives from
        // CharacterFocus.PresentedActorId (CardsDriver.4.Rebuild.cs:257-260): a character was
        // already being shown, a different one is being shown now, and there is something to move.
        // The driver adds the fourth ("the fan is OPEN") itself, because only it knows that.
        bool swap = _cards.Count > 0 && _loadout.Count > 0;

        // ALWAYS with the grace, never 0. A card the fan may still be holding must not be destroyed
        // in the frame this runs: the driver's rebuild — the thing that takes the old list OUT of
        // the fan — happens on the driver's own Update, which is a different MonoBehaviour and may
        // not have run yet. Destroying first would leave the fan iterating a fake-null for a frame.
        // The zero-grace path exists only in ReleaseFan, which drops the fan SYNCHRONOUSLY first.
        RetireCards(RetireGraceSeconds);

        int count = _loadout.Count;
        if (count == 0)
        {
            Publish(swap: false);
            return;
        }

        Transform holder = Holder();
        GameObject? backing = CardsDriver.CardBackingPrefab;

        for (int i = 0; i < count; i++)
        {
            CAbilityCard model = _loadout[i];
            if (model == null)
                continue;
            VRCard? card = BuildCard(i, model, holder, backing);
            if (card == null)
                continue;
            _cards.Add(card);
            _cardModels.Add(model);
            _faces.Add(null);   // printed on the first frame the card is active — see TickFan
        }

        Publish(swap);
    }

    /// <summary>
    /// One card. Built EXACTLY as the driver's own cards are — <c>VRCard.Build</c> on the bundled
    /// CardBacking prefab (<c>CardsDriver.CardBackingPrefab</c>, the same asset
    /// <c>VRCardFactory.CreateBlank</c> uses), so a map-room card and a scenario card are the same
    /// object with the same silhouette, the same materials and the same collider.
    ///
    /// <para>BUILT INACTIVE, then parked under the inactive holder. <c>GrabbableBehaviour.OnEnable</c>
    /// demands a Collider on its own GameObject and declines to register without one, and VRCard
    /// creates that collider inside <c>Build</c> — which cannot run before AddComponent. Building
    /// under an inactive object is what <c>VRCardFactory</c> does for the same reason.</para>
    ///
    /// <para>NO INTERACTION FLAGS ARE STAMPED HERE, deliberately. <c>CardFan.SetMode</c> +
    /// <c>StampMembership</c> write Grabbable / InspectOnly / AllowsGateHand / PokeSelectEnabled
    /// when the card enters the fan, and those are the SAME writes a scenario card gets. A second
    /// stamp here could only ever disagree with them.</para>
    ///
    /// <para>NO SIZE IS PASSED EITHER: <c>VRCard.Build</c> reads [Cards] CardWidth/CardHeight
    /// itself, which is precisely why a map-room card is the size the player's scenario cards are
    /// without this file restating the dial.</para>
    /// </summary>
    private VRCard? BuildCard(int index, CAbilityCard model, Transform holder, GameObject? backing)
    {
        try
        {
            var go = new GameObject($"MapRoomCard[{index}] id={model.ID}");
            go.SetActive(false);
            go.transform.SetParent(holder, worldPositionStays: false);
            VRCard card = go.AddComponent<VRCard>();
            card.Build(backing);
            go.SetActive(true);   // active-SELF: the holder above keeps it out of the hierarchy
            return card;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MAP-ROOM HAND: card {index} (id {model.ID}) could not be built "
                + $"({ex.GetType().Name}: {ex.Message}). CONSEQUENCE: the fan shows one card fewer "
                + "than the loadout has; everything else is unaffected.");
            return null;
        }
    }

    /// <summary>The inactive parking root. Created on demand, destroyed with the feature.</summary>
    private Transform Holder()
    {
        if (_holder == null)
        {
            var go = new GameObject("GloomhavenVR.MapRoomHand.Pending");
            go.SetActive(false);   // nothing under it renders, ticks or raycasts
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _holder = go.transform;
        }
        return _holder;
    }

    /// <summary>
    /// Hand the current card list to the shared driver and ask for the rebuild that consumes it.
    /// The driver copies the list into its own fan buffer, hooks each card and calls
    /// <c>CardFan.SetCards</c>; from that moment the cards belong to the scenario's own fan.
    /// </summary>
    private void Publish(bool swap)
    {
        CardsDriver.OffScenarioFanCards = _cards.Count > 0 ? _cards : null;
        if (swap)
            CardsDriver.OffScenarioFanSwap = true;
        CardsDriver.RequestRebuild();
    }

    // ==========================================================================================
    //  PER FRAME
    // ==========================================================================================

    /// <summary>
    /// The only per-frame work this file still does: print a face on any card that has just become
    /// visible, keep the printed faces' mip bake current, and let retired cards die once their
    /// exchange has finished. NOTHING here poses, lays out, animates or highlights anything — the
    /// driver's tick does all of that, on this very fan.
    /// </summary>
    private void TickFan()
    {
        PrintPendingFaces();
        for (int i = 0; i < _faces.Count; i++)
            _faces[i]?.MaintainMipBake();
        for (int i = 0; i < _retired.Count; i++)
            _retired[i].Face?.MaintainMipBake();
        SweepRetired();
    }

    /// <summary>
    /// Print the real card front on every card that is ACTIVE IN THE HIERARCHY and has none yet.
    ///
    /// <para>WHY THE PRINT IS DEFERRED RATHER THAN DONE AT BUILD TIME, and it is a correctness
    /// point and not an optimisation: <c>Net.RemoteCardArt.ShowFront</c> activates its host LAST
    /// and then writes the clone's final pose (<c>FitClone</c>) — its own comment records that the
    /// order matters, because the cloned widget's <c>OnEnable</c> repositions itself and must
    /// therefore run BEFORE that write. <c>OnEnable</c> does not run under an inactive root, so a
    /// face printed while the card was still parked would be fitted against a widget that had not
    /// laid itself out yet. Waiting until the card is genuinely in the hierarchy — which happens
    /// the first time the player rolls their palm up and the fan opens — makes the documented order
    /// hold. It also means a map visit in which the hand is never raised costs zero card clones.</para>
    ///
    /// <para>A slot the pool refuses keeps its card BACK, which is the fail-safe, and is counted in
    /// the state line so a hardware log can tell "the pool refused" from "the gate refused" — there
    /// IS no gate in this phase (see <see cref="MapRoomHand"/>'s header).</para>
    /// </summary>
    private void PrintPendingFaces()
    {
        if (_cards.Count == 0 || _frontsShown >= _cards.Count)
            return;
        // CADENCED, not per frame. A pool borrow that REFUSES leaves its slot pending, and an
        // un-throttled retry would then spawn-and-return a card widget every frame for every
        // refusing slot — the exact per-frame clone cost RemoteAbilityCardSource's own COST NOTE
        // warns about. 5 Hz is well inside the fan's own ~0.3 s reveal, so a face still lands while
        // the card is still flying out of the centre stack.
        if (Time.unscaledTime < _nextFacePrintAt)
            return;
        _nextFacePrintAt = Time.unscaledTime + FacePrintInterval;

        for (int i = 0; i < _cards.Count && i < _cardModels.Count; i++)
        {
            if (_faces[i] != null)
                continue;
            VRCard card = _cards[i];
            if (card == null || !card.gameObject.activeInHierarchy)
                continue;
            CAbilityCard model = _cardModels[i];
            if (model == null)
                continue;
            try
            {
                var art = new RemoteCardArt(card.transform, CardWidthMeters, CardHeightMeters);
                // actor: null forces the POOLED path. There is no CPlayerActor in the map phase at
                // all — CMapCharacter.GetActor() (CMapCharacter.cs:1116) reads
                // ScenarioManager.Scenario, which is null here, so asking for one would THROW.
                // Nothing re-checks a reveal gate, and that is correct rather than an omission:
                // RevealGate.ShowRoundCardFronts folds in RevealGate.InScenario, which is FALSE on
                // the map, so the gate is open by its own definition for every actor.
                if (RemoteAbilityCardSource.ShowFullFace(art, null, model)
                    == RemoteAbilityCardSource.FacePath.None)
                {
                    art.Destroy();
                    continue;   // stays a card BACK, and will be retried next frame
                }
                _faces[i] = art;
                _frontsShown++;
            }
            catch (System.Exception ex)
            {
                VRLog.Debug(Scope, $"Map-room hand: card id {model.ID} kept its back ({ex.Message}).");
            }
        }

        // ONE line per card set, on the FIRST print rather than on the last. Keyed on the first so
        // it still fires when one slot's pool borrow permanently refuses — that case is exactly
        // when the count in it is worth reading, and a "wait for all of them" trigger would go
        // silent precisely then.
        if (_facesLogged || _frontsShown == 0)
            return;
        _facesLogged = true;
        VRLog.Info(Scope, $"MAP-ROOM HAND FACES: {_frontsShown} of {_cards.Count} card(s) printing "
            + "their REAL front (Net.RemoteAbilityCardSource pooled borrow); any remainder is "
            + "retried at 5 Hz and shows a card BACK until it lands. THIS LINE FIRES ON THE FIRST "
            + "FRAME THE CARDS ARE VISIBLE — i.e. the first time the palm gate REVEALED the fan — so "
            + "its presence is also the proof that (a) 'die Karten sind dauerhaft da' and (b) 'reagiert "
            + "nicht auf die Roll der Hand' are fixed. If it NEVER appears while the rebuild line "
            + "reports cards, the fan never opened: read the driver's 'fan state' line for "
            + "gateEnabled / revealed / open, in that order.");
    }

    /// <summary>Seconds between face-print passes. See <see cref="PrintPendingFaces"/>.</summary>
    private const float FacePrintInterval = 0.2f;

    /// <summary>Unscaled time of the next face-print pass.</summary>
    private float _nextFacePrintAt;

    // ==========================================================================================
    //  RETIREMENT AND RELEASE
    // ==========================================================================================

    /// <summary>
    /// Move the current cards to the retired list (or destroy them outright with a zero grace).
    /// They are NOT unpublished here: the driver's very next rebuild replaces the fan's contents
    /// with the new list, and a card that is mid-exchange must still exist while it flies.
    /// </summary>
    private void RetireCards(float grace)
    {
        float destroyAt = Time.unscaledTime + grace;
        for (int i = 0; i < _cards.Count; i++)
        {
            VRCard card = _cards[i];
            RemoteCardArt? face = i < _faces.Count ? _faces[i] : null;
            if (card == null)
            {
                face?.Destroy();
                continue;
            }
            if (grace <= 0f)
            {
                DestroyCard(card, face);
                continue;
            }
            _retired.Add(new Retired(card, face, destroyAt));
        }
        _cards.Clear();
        _cardModels.Clear();
        _faces.Clear();
        _frontsShown = 0;
        _facesLogged = false;
        _nextFacePrintAt = 0f;   // the new set may print in the very next frame
    }

    /// <summary>
    /// Let a retired card die once its exchange has landed. A card the player is HOLDING is never
    /// destroyed under their hand — the same rule the driver applies to its own deferred face
    /// restore ("the hold may last as long as it likes") — so its grace is re-armed until they let
    /// go. Destroying a card asks for a rebuild, which re-states the fan's contents from the live
    /// list and drops any stale reference in one pass.
    /// </summary>
    private void SweepRetired()
    {
        if (_retired.Count == 0)
            return;
        float now = Time.unscaledTime;
        bool destroyedAny = false;
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            Retired entry = _retired[i];
            VRCard card = entry.Card;
            if (card == null)
            {
                entry.Face?.Destroy();
                _retired.RemoveAt(i);
                continue;
            }
            if (card.IsHeld)
            {
                _retired[i] = new Retired(card, entry.Face, now + RetireGraceSeconds);
                continue;
            }
            if (now < entry.DestroyAt)
                continue;
            DestroyCard(card, entry.Face);
            _retired.RemoveAt(i);
            destroyedAny = true;
        }
        if (destroyedAny)
            CardsDriver.RequestRebuild();
    }

    /// <summary>Destroy one card and its printed face. The face dies WITH the card, never before
    /// it: a card that lost its print a moment before it lost its body reads as a bug.</summary>
    private static void DestroyCard(VRCard card, RemoteCardArt? face)
    {
        if (card != null)
        {
            try
            {
                card.Holder?.Grabber.CancelAll();   // never destroy an object out of a live grab
            }
            catch (System.Exception)
            {
                // A grabber that is already gone is exactly the state we want; nothing to do.
            }
        }
        face?.Destroy();
        if (card != null)
            Object.Destroy(card.gameObject);
    }

    /// <summary>
    /// Tear the whole fan down. ORDER IS LOAD-BEARING and is the reason the driver exposes a
    /// synchronous drop at all: the fan must give the cards up BEFORE they are destroyed, or it
    /// would hold destroyed objects — and their driver subscriptions would outlive them — for as
    /// long as it takes a rebuild to notice.
    /// </summary>
    private void ReleaseFan(string reason)
    {
        // 1. Unpublish and hand the fan back, synchronously. This empties the fan, unhooks every
        //    card and drops every driver-side reference to them; the palm gate closes the (now
        //    empty) fan on its next tick, which is the ordinary close animation.
        CardsDriver.OffScenarioFanCards = null;
        CardsDriver.OffScenarioFanSwap = false;
        CardsDriver.DropOffScenarioFan(reason);

        // 2. Now nothing points at them.
        RetireCards(0f);
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            Retired entry = _retired[i];
            if (entry.Card != null)
                DestroyCard(entry.Card, entry.Face);
            else
                entry.Face?.Destroy();
        }
        _retired.Clear();

        if (_holder != null)
        {
            Object.Destroy(_holder.gameObject);
            _holder = null;
        }
    }

    // ==========================================================================================
    //  CONFIG READS
    // ==========================================================================================

    /// <summary>The card's real width in METRES — the player's own [Cards] CardWidth, so a map-room
    /// card is exactly the size their scenario cards are.</summary>
    private static float CardWidthMeters => Cfg(CardsConfig.CardWidth, Defaults.CardWidth);

    /// <summary>The card's real height in METRES, at the printed 63.5 x 88 mm aspect —
    /// <c>CardsConfig.CardHeight</c>'s own derivation, restated so it survives an unbound dial.</summary>
    private static float CardHeightMeters => CardWidthMeters * (88f / 63.5f);

    /// <summary>Read a float dial, tolerating the window before <c>CardsConfig.Bind</c> has run (the
    /// map room can, in principle, engage first). An unbound dial reads as the shipped default, so
    /// the fan looks the same either way.</summary>
    private static float Cfg(BepInEx.Configuration.ConfigEntry<float>? entry, float fallback)
        => entry != null ? entry.Value : fallback;
}
