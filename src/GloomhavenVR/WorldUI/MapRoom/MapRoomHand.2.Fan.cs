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
// ─── MODBUILD 193: ONE CARD, NOT THE WHOLE HAND ───────────────────────────────────────────────
// USER ASK, VERBATIM (2026-08-21, item 8): "Wenn man eine Karte ändert während man seinen
// Kartenfächer in der Hand betrachtet soll die Karte per Animation auftauchen oder verschwinden
// damit der Fächer immer aktuell ist."
//
// 192 had exactly ONE update path — <see cref="RebuildFan"/> — and it retires every slab and builds
// every slab again. For a CHARACTER change that is right (the whole hand really was exchanged, and
// the exchange wipe is the animation for it). For a single tick in the card-selection screen it is
// wrong twice over: eleven cards move to report one, and the one card that actually changed is the
// only thing on screen that does NOT read as an event.
//
// SO THE UPDATE IS NOW A DIFF (<see cref="UpdateFanCards"/>), and the two animations it asks for are
// the SCENARIO'S OWN — the standing ruling for this whole feature is "Es soll sich nicht vom Szenario
// unterscheiden wie sich die Karten verhalten!", so nothing new was invented:
//
//   UNCHANGED → keeps its slab, its printed face and its identity. It is re-published in loadout
//               order and the fan's own CardFan.SetCards -> Relayout(instant: false) glides it to its
//               new arc slot. No card is destroyed and rebuilt to make room for another one.
//   ADDED     → a new VRCard is built by the same BuildCard as any other, published, and the DRIVER
//               materializes it with VRCard.PlayAppear at the one seam that has already asserted its
//               home (CardsDriver.6.Flows.MaterializeNewOffScenarioCards). That is verbatim the
//               "emerge from dust" a scenario plays for a docked action card / a slot occupant that
//               appears for a newly active character (CardsDriver.4.Rebuild.cs:860 and :880).
//   REMOVED   → CardsDriver.OffScenarioFanLeave: CardFan.Remove (the fan's own single-card exit —
//               "remaining cards close the gap", CardFan.cs:460) plus VRCard.Vanish, the
//               "crumble to dust" a scenario plays for a card that leaves every zone with no pile to
//               fly to (CardsDriver.4.Rebuild.cs:794). A pile FLIGHT was deliberately not reused:
//               the map phase has no discard or burnt pile, and inventing a destination would be
//               inventing an animation.
//
// PRECEDENCE, because both edges can land in one poll: A CHARACTER CHANGE ALWAYS WINS. Reconcile
// routes to RebuildFan (retire-all + BeginSwapOut + SetCards(swap: true)) whenever the resolved
// CMapCharacter is not the one the fan was built for, whatever else changed in the same tick — the
// exchange already carries every card of both hands, so a diff on top of it could only fight it.
//
// AND A CARD HE IS HOLDING IS NEVER TAKEN OUT OF HIS HAND. The removal pass skips a held slab, keeps
// it published, and re-runs on the next poll; the card leaves with the ordinary crumble the moment he
// lets go. This is the same standing rule CardFan.StampMembership and CardFan.BeginSwapOut both
// state from their side ("while a card IsHeld this code writes nothing a hold depends on").
//
// ─── MODBUILD 196: THE FAN READS LEFT-TO-RIGHT BY INITIATIVE ──────────────────────────────────
// USER REPORT, VERBATIM (2026-08-22): "Die Kartenreihenfolge soll von links nach rechts nach der
// INITIATIVE der Karten sortiert sein — und ist es am Anfang auch. Aber wenn man Karten HINZUFÜGT,
// tauchen sie immer am RECHTEN RAND auf statt sich einzusortieren."
//
// The order this file publishes was ALWAYS the order of CMapCharacter.HandAbilityCardIDs, and that
// list is append-ordered by the game's own party screen (OnAbilityCardSelect does
// HandAbilityCardIDs.Add, UIPartyCharacterAbilityCardsDisplay.cs:536). So a joining card landed at
// the right edge on BOTH paths — the full RebuildFan and the single-card diff alike; the diff was
// not the cause, it merely inherited it. The fix is one sort at the one place the models are
// resolved (MapRoomHand.ResolveLoadout, which carries the whole argument, the key, the tie-break
// and the reading of the game's own comparison). NOTHING about the animations moved: a joining card
// is now BUILT at its sorted index, so the same VRCard.PlayAppear dusts it in where it belongs and
// the same Relayout(instant: false) glides its neighbours apart to make room.
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

    /// <summary>
    /// The fade handle on each printed face, index-aligned with <see cref="_faces"/> — see
    /// <see cref="TryCaptureFaceGroup"/> for why this exists at all and what the real fix is.
    /// </summary>
    private readonly List<CanvasGroup?> _faceGroups = new(MaxCards);

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

    /// <summary>
    /// How long a card removed by a SINGLE-CARD diff is kept alive. Long enough for the whole
    /// crumble (<c>VRCard.DockVanishSeconds</c>) plus a comfortable margin, and no longer: unlike
    /// the character exchange there is no wave to wait for, and the card hides itself the instant
    /// its own vanish ends (the completion callback in
    /// <c>CardsDriver.LeaveOffScenarioFan</c>), so this bound only ever decides when the invisible
    /// object is freed. A card the player is HOLDING re-arms it in <see cref="SweepRetired"/>, as
    /// every other retirement does.
    /// </summary>
    private static float LeaveGraceSeconds => VRCard.DockVanishSeconds + 0.25f;

    // ---- diff scratch (see UpdateFanCards). Instance-owned and REUSED: the diff runs at the poll
    //      rate — 20 Hz while the fan is up — and a per-poll allocation in a path that frequent is
    //      exactly the kind of thing that later shows up in a frame-time capture.
    private readonly List<VRCard> _diffCards = new(MaxCards);
    private readonly List<CAbilityCard> _diffModels = new(MaxCards);
    private readonly List<RemoteCardArt?> _diffFaces = new(MaxCards);
    private readonly List<CanvasGroup?> _diffGroups = new(MaxCards);
    private readonly List<bool> _diffClaimed = new(MaxCards);
    private readonly List<int> _diffAdded = new(MaxCards);
    private readonly List<int> _diffRemoved = new(MaxCards);
    private readonly List<int> _diffDeferred = new(MaxCards);

    /// <summary>
    /// A card left the loadout while the player was HOLDING it, so its removal was deferred rather
    /// than performed under his hand. While this is set the poll re-runs the diff even when the
    /// loadout signature has not moved — otherwise the retirement would wait for the NEXT edit,
    /// which may never come.
    /// </summary>
    private bool _deferredLeave;

    // ==========================================================================================
    //  THE PRINTED FACE'S FADE — and the cross-file defect it works around
    // ==========================================================================================
    //
    // READ THIS BEFORE TOUCHING ANY OF IT. <c>VRCard.Vanish</c> and <c>VRCard.PlayAppear</c> — the
    // two animations ModBuild 193 reuses — carry the card visually with TWO writes: they hide the
    // backing slab's Renderers (VRCard.SetBodyVisible) and they fade the card's ART
    // (VRCard.SetVisualAlpha, a CanvasGroup on VRCard's OWN "FaceCanvas" child). That is complete
    // for a SCENARIO card, whose face is the game's real AbilityCardUI ADOPTED INTO that very canvas
    // (VRCard.AttachGameCard -> CardFace.Adopt(card, _canvasRect)).
    //
    // IT IS NOT COMPLETE FOR A MAP-ROOM CARD. This feature never calls AttachGameCard — that is the
    // whole basis of the inspection-only guarantee — so VRCard's FaceCanvas is EMPTY here and the
    // visible front is a <c>Net.RemoteCardArt</c> clone hosted on a SIBLING world-space canvas
    // parented to the card transform, which VRCard's CanvasGroup cannot reach. Left alone, a leaving
    // map card would hide its slab, puff its dust, and keep a 100 % opaque printed front standing
    // for the whole 0.30 s crumble — and then cut to nothing when the vanish's completion callback
    // hides it. A hard cut is exactly the "popping is unacceptable" the animation exists to remove.
    //
    // THE REAL FIX IS ONE LINE AND IT IS NOT IN THIS LANE'S FILES: VRCard.SetVisualAlpha should
    // apply its CanvasGroup to every canvas under the card (or VRCard should expose a fade the face
    // provider can hook), so ANY face mechanism follows the card's own animation. That is
    // Cards/VRCard.cs, which this lane does not own — see this lane's report.
    //
    // THE LOCAL WORKAROUND, which is what the code below is: this file already OWNS the moment each
    // face is created, so it puts a CanvasGroup on that face's host itself and drives it with the
    // same curve (VRCard.SmootherStep over VRCard.DockAppearSeconds / DockVanishSeconds). Every
    // printed face fades IN — which is also strictly better than the pop the pooled borrow used to
    // land with — and a face on a leaving card fades OUT with its card's crumble.

    /// <summary>One running face fade. Independent of the card lists on purpose: a diff reorders
    /// those every poll, and a fade that had to be re-indexed with them would be one more lockstep
    /// invariant to get wrong.</summary>
    private readonly struct FaceFade
    {
        internal FaceFade(CanvasGroup group, float start, float duration, bool fadeIn)
        {
            Group = group;
            Start = start;
            Duration = duration;
            FadeIn = fadeIn;
        }

        internal CanvasGroup Group { get; }
        internal float Start { get; }
        internal float Duration { get; }
        internal bool FadeIn { get; }
    }

    private readonly List<FaceFade> _faceFades = new(MaxCards);

    /// <summary>
    /// The <see cref="CanvasGroup"/> that fades <paramref name="card"/>'s printed front, created on
    /// the host <c>Net.RemoteCardArt</c> just parented under the card. Null when no host appeared
    /// (then there is nothing to fade and the caller degrades to no fade at all).
    ///
    /// <para>WHY THE HOST IS FOUND BY POSITION AND NOT BY NAME: <c>RemoteCardArt.EnsureHost</c>
    /// parents exactly ONE new GameObject to the slab (its clone goes under THAT, never under the
    /// card), so the child that appeared across the print IS the host. Matching on its name would
    /// couple this file to a private literal in another file for no extra safety; the Canvas check
    /// below is the real assertion, and a miss simply means no fade.</para>
    /// </summary>
    private static CanvasGroup? TryCaptureFaceGroup(VRCard card, int childrenBefore)
    {
        Transform t = card.transform;
        if (t.childCount <= childrenBefore)
            return null;
        Transform host = t.GetChild(t.childCount - 1);
        if (host == null || host.GetComponent<Canvas>() == null)
            return null;
        CanvasGroup group = host.GetComponent<CanvasGroup>();
        if (group == null)
            group = host.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    /// <summary>Arm a fade on one face. A null group (no host, or a destroyed one) is a no-op, which
    /// is how every path here degrades: no fade, never an exception.</summary>
    private void ArmFaceFade(CanvasGroup? group, bool fadeIn, float duration)
    {
        if (group == null)
            return;
        group.alpha = fadeIn ? 0f : 1f;
        for (int i = _faceFades.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_faceFades[i].Group, group))
                _faceFades.RemoveAt(i);   // one fade per face; a new one supersedes
        }
        _faceFades.Add(new FaceFade(group, Time.unscaledTime, Mathf.Max(0.01f, duration), fadeIn));
    }

    /// <summary>Advance every running face fade. Allocation-free; a group whose card was destroyed
    /// drops out silently (a destroyed component compares equal to null).</summary>
    private void TickFaceFades()
    {
        if (_faceFades.Count == 0)
            return;
        float now = Time.unscaledTime;
        for (int i = _faceFades.Count - 1; i >= 0; i--)
        {
            FaceFade fade = _faceFades[i];
            CanvasGroup group = fade.Group;
            if (group == null)
            {
                _faceFades.RemoveAt(i);
                continue;
            }
            float t = Mathf.Clamp01((now - fade.Start) / fade.Duration);
            float s = VRCard.SmootherStep(t);
            group.alpha = fade.FadeIn ? s : 1f - s;
            if (t >= 1f)
                _faceFades.RemoveAt(i);
        }
    }

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
            _faceGroups.Add(null);
        }

        Publish(swap);
    }

    // ==========================================================================================
    //  THE SINGLE-CARD DIFF (ModBuild 193 — see this file's header for the ruling it serves)
    // ==========================================================================================

    /// <summary>
    /// Bring the standing fan up to date with <see cref="_loadout"/> WITHOUT rebuilding it: compare
    /// by card identity, keep every slab that is still wanted, and animate only the difference.
    /// Called from <see cref="Reconcile"/> for a loadout edit on the character the fan is already
    /// showing; a CHARACTER change goes to <see cref="RebuildFan"/> instead and always wins.
    ///
    /// <para>WHY IDENTITY AND NOT POSITION. <c>CMapCharacter.HandAbilityCardIDs</c> is mutated IN
    /// PLACE by the game's own screen (UIPartyCharacterAbilityCardsDisplay.OnAbilityCardSelect
    /// appends at :536, OnAbilityCardDeselect removes at :558), so every card after a removal
    /// changes index without changing identity. A positional diff would report the whole tail as
    /// "changed" and animate eleven cards to report one — precisely the failure this replaces.</para>
    ///
    /// <para>THE PASSES, in this order because the second depends on the first:
    /// <list type="number">
    /// <item>BUILD THE NEW SET IN LOADOUT ORDER. Each wanted ID claims the first unclaimed slab that
    /// carries it (so a duplicate ID, which the game does not produce today, cannot claim one slab
    /// twice); an unclaimed ID gets a brand-new <see cref="BuildCard"/>. The card's PLACE therefore
    /// follows <c>_loadout</c>, which since ModBuild 196 is INITIATIVE ORDER and no longer the
    /// player's append order — see <c>MapRoomHand.ResolveLoadout</c> for the report that changed it
    /// and the tie-break it chose. A joining card is consequently built at its SORTED index here,
    /// which is what makes it materialize in its place: <c>CardFan.SetCards -&gt;
    /// Relayout(instant: false)</c> glides the cards on either side apart around it and
    /// <c>VRCard.PlayAppear</c> (which seeds itself at the card's asserted HOME) dusts it in there.
    /// The animation is untouched — only where it plays moved.</item>
    /// <item>EVERY UNCLAIMED SLAB HAS LEFT. It is handed to <c>CardsDriver.OffScenarioFanLeave</c>
    /// (fan exit + crumble) and retired — unless the player is HOLDING it, in which case it stays
    /// published and the diff runs again on the next poll.</item>
    /// </list></para>
    ///
    /// <para>NOTHING IS PUBLISHED WHEN NOTHING MOVED. A retry poll that finds the same set returns
    /// before it touches the driver, so the deferred-removal retry cannot turn into a rebuild
    /// request every 50 ms.</para>
    /// </summary>
    private void UpdateFanCards()
    {
        _deferredLeave = false;

        // THE LOCKSTEP THE WHOLE PASS ASSUMES, checked BEFORE anything has a side effect. Every
        // append to these four lists is paired, so they can only disagree if a future edit breaks
        // the pairing — and the failure mode of a silent misalignment is the wrong art on the wrong
        // card, which is precisely why _cardModels is not an index into _loadout in the first place.
        if (_cardModels.Count != _cards.Count || _faces.Count != _cards.Count
            || _faceGroups.Count != _cards.Count)
        {
            VRLog.Warn(Scope, "MAP-ROOM HAND: the card/model/face lists are out of lockstep "
                + $"({_cards.Count}/{_cardModels.Count}/{_faces.Count}/{_faceGroups.Count}), so the "
                + "single-card diff was REFUSED and the whole hand is rebuilt instead. CONSEQUENCE: "
                + "this one loadout edit plays the character-exchange animation rather than a single "
                + "card joining or leaving; nothing is lost and nothing is left standing. If this "
                + "line ever appears, a paired append was missed in MapRoomHand.2.Fan.");
            RebuildFan();
            return;
        }

        int have = _cards.Count;
        int want = _loadout.Count;

        _diffClaimed.Clear();
        for (int i = 0; i < have; i++)
            _diffClaimed.Add(false);
        _diffCards.Clear();
        _diffModels.Clear();
        _diffFaces.Clear();
        _diffGroups.Clear();
        _diffAdded.Clear();
        _diffRemoved.Clear();
        _diffDeferred.Clear();

        Transform holder = Holder();
        GameObject? backing = CardsDriver.CardBackingPrefab;

        // (1) THE NEW SET, IN LOADOUT ORDER.
        for (int j = 0; j < want; j++)
        {
            CAbilityCard model = _loadout[j];
            if (model == null)
                continue;
            int claim = -1;
            for (int i = 0; i < have; i++)
            {
                CAbilityCard mine = _cardModels[i];
                if (_diffClaimed[i] || _cards[i] == null || mine == null || mine.ID != model.ID)
                    continue;
                claim = i;
                break;
            }
            if (claim >= 0)
            {
                _diffClaimed[claim] = true;
                _diffCards.Add(_cards[claim]);
                _diffModels.Add(_cardModels[claim]);
                _diffFaces.Add(_faces[claim]);
                _diffGroups.Add(_faceGroups[claim]);
                continue;
            }
            VRCard? card = BuildCard(_diffCards.Count, model, holder, backing);
            if (card == null)
                continue;   // BuildCard has already warned and named the consequence
            _diffCards.Add(card);
            _diffModels.Add(model);
            _diffFaces.Add(null);   // printed by PrintPendingFaces on its first visible frame
            _diffGroups.Add(null);
            _diffAdded.Add(model.ID);
        }

        // (2) EVERYTHING UNCLAIMED HAS LEFT THE LOADOUT.
        for (int i = 0; i < have; i++)
        {
            if (_diffClaimed[i])
                continue;
            VRCard card = _cards[i];
            RemoteCardArt? face = _faces[i];
            CanvasGroup? group = _faceGroups[i];
            CAbilityCard model = _cardModels[i];
            // The id goes through IdOf rather than a null test HERE on purpose: the element is
            // DECLARED non-nullable (both writers refuse a null model), and testing the local would
            // narrow it, turning the lockstep Add below into a nullable-warning site with no
            // meaningful model to substitute. IdOf carries the belt without touching this local.
            int id = IdOf(model);
            if (card == null)
            {
                face?.Destroy();
                continue;
            }
            if (card.IsHeld)
            {
                // NEVER OUT OF HIS HAND. He lifted this card to read it (CardFan.FanMode.Inspect)
                // and the menu behind him just deselected it; taking it away mid-look is the one
                // thing this feature may not do. It stays a published fan card — CardFan.
                // StampMembership writes nothing to a held card except the InspectOnly tightening —
                // and the retry below removes it the moment he lets go. Appended at the END of the
                // new set because it no longer HAS a place in the loadout; that index is never used
                // while it is held (every layout loop in CardFan skips IsHeld).
                _deferredLeave = true;
                _diffDeferred.Add(id);
                _diffCards.Add(card);
                _diffModels.Add(model);
                _diffFaces.Add(face);
                _diffGroups.Add(group);
                continue;
            }
            _diffRemoved.Add(id);
            // THE SCENARIO'S OWN LEAVE PATH: CardFan.Remove (the survivors glide the gap shut) plus
            // VRCard.Vanish (crumble to dust, in place, no slide and no re-orient) — and the printed
            // front fades with it, which VRCard's own art fade cannot do here (see the face-fade
            // region's header for the cross-file defect that stands behind this line).
            ArmFaceFade(group, fadeIn: false, VRCard.DockVanishSeconds);
            CardsDriver.OffScenarioFanLeave(card);
            _retired.Add(new Retired(card, face, Time.unscaledTime + LeaveGraceSeconds));
        }

        bool changed = _diffAdded.Count > 0 || _diffRemoved.Count > 0
                       || _diffCards.Count != _cards.Count;
        if (!changed)
        {
            for (int i = 0; i < _diffCards.Count; i++)
            {
                if (!ReferenceEquals(_diffCards[i], _cards[i]))
                {
                    changed = true;
                    break;
                }
            }
        }
        if (!changed)
            return;   // a deferred-removal retry with nothing to do: do not disturb the driver

        bool wasOpen = CardsDriver.OffScenarioFanIsOpen;
        bool exchanging = CardsDriver.OffScenarioFanExchanging;

        // NAMED BEFORE THE COMMIT, and that ordering is load-bearing: NameOf reads _loadout (which
        // holds the ids that JOINED) and _cardModels (which still holds the ones that LEFT). One
        // line further down _cardModels becomes the new set and the removed cards' names are gone.
        string addedNames = Ids(_diffAdded);
        string removedNames = Ids(_diffRemoved);
        string deferredNames = Ids(_diffDeferred);

        _cards.Clear();
        _cards.AddRange(_diffCards);
        _cardModels.Clear();
        _cardModels.AddRange(_diffModels);
        _faces.Clear();
        _faces.AddRange(_diffFaces);
        _faceGroups.Clear();
        _faceGroups.AddRange(_diffGroups);
        _frontsShown = 0;
        for (int i = 0; i < _faces.Count; i++)
        {
            if (_faces[i] != null)
                _frontsShown++;
        }
        if (_diffAdded.Count > 0)
        {
            // The new slab must show its REAL front, not a card back, while it materializes: arm
            // the printer for the very next tick instead of waiting out its 5 Hz cadence.
            _facesLogged = false;
            _nextFacePrintAt = 0f;
        }

        // NEVER swap: the exchange is the CHARACTER edge and nothing else (see Reconcile's
        // precedence). The driver plays the join animation itself, at the seam that has already
        // asserted the new card's home.
        Publish(swap: false);
        LogDiff(addedNames, removedNames, deferredNames, wasOpen, exchanging);
    }

    /// <summary>
    /// THE DIFF LINE. One per edit that actually moved something, written so a hardware log can be
    /// judged on the user's ask directly: what joined, what left, which animation each one took,
    /// how stale the fan could have been, and whether he was looking at it at the time.
    /// </summary>
    private void LogDiff(string added, string removed, string deferred, bool wasOpen, bool exchanging)
    {
        float window = Time.unscaledTime - _lastPollAt;

        VRLog.Info(Scope,
            "MAP-ROOM HAND DIFF (a loadout edit, NOT a character change).\n"
            + $"  added    : {added} -> "
            + (_diffAdded.Count == 0 ? "nothing joined"
               : wasOpen && !exchanging
                   ? "VRCard.PlayAppear, the scenario's own dust MATERIALIZE "
                     + $"({VRCard.DockAppearSeconds:F2}s), played by the driver once CardFan.SetCards "
                     + "asserted the card's arc home. Grep 'Off-scenario fan JOIN' for the driver's "
                     + "own confirmation of the same event."
                   : "NO join animation: " + (exchanging
                       ? "a character exchange is still in the air and owns every card's pose"
                       : "the fan was CLOSED, so the palm-gate reveal owns the entrance instead")
                     + " — the card is in the fan either way.")
            + "\n"
            + $"  removed  : {removed} -> "
            + (_diffRemoved.Count == 0 ? "nothing left"
               : "CardFan.Remove (the survivors GLIDE the gap shut) + VRCard.Vanish, the scenario's "
                 + $"own dust CRUMBLE ({VRCard.DockVanishSeconds:F2}s) in place — no slide, no "
                 + "re-orient. The slab is freed "
                 + $"{LeaveGraceSeconds:F2}s later by SweepRetired, long after it is invisible.")
            + "\n"
            + $"  held back: {deferred} — a card the player is HOLDING is never removed under his "
            + "hand. It stays a published fan card and leaves with the ordinary crumble on the poll "
            + "after he lets go. Empty is the normal case.\n"
            + $"  kept     : {_cards.Count - _diffAdded.Count} card(s) keep their slab, their printed "
            + "face and their identity; they only re-lay out around the change "
            + "(CardFan.SetCards -> Relayout(instant: false), the same glide any hand-fan change "
            + "uses). NOTHING WAS REBUILT — that is the whole point of this path.\n"
            + $"  fan open : {(wasOpen ? "YES — he is looking at it, so the animations above are the ones he sees"
                                       : "no — his palm is down; the fan is CURRENT the moment he raises it")}\n"
            + $"  latency  : <= {window * 1000f:F0} ms (the poll window that caught this edit; the "
            + "poll runs at 20 Hz while the fan is OPEN and 4 Hz while it is down — there is no event "
            + "to subscribe to, UIPartyCharacterAbilityCardsDisplay mutates HandAbilityCardIDs in "
            + "place and raises nothing).\n"
            + "  wire     : NOTHING. Card identity never goes on the wire. The only observable is "
            + "PresenceState.HandCardCount, which follows the fan's count — so a peer sees our fan of "
            + "card BACKS gain or lose one back as the animation STARTS, up to ~0.3 s before it "
            + "finishes here. RemoteHandFan still gates every front on RevealGate.InScenario, false "
            + "on the map.\n"
            + "  DISPROOF : the card appears/disappears with no animation -> the driver's own JOIN "
            + "line is missing and 'fan open' above says no. The WRONG card animates -> the match is "
            + "by CAbilityCard.ID, so read the ids above against the party screen. A card the player "
            + "was holding vanished out of his hand -> 'held back' would have named it and did not.");
    }

    /// <summary>A model's card id, or -1 for the null that <see cref="_cardModels"/> is not supposed
    /// to be able to hold. See its one call site for why the check lives here.</summary>
    private static int IdOf(CAbilityCard? model) => model != null ? model.ID : -1;

    /// <summary>Render a diff bucket for the log: ids with the game's own card names where they can
    /// be read. <c>CBaseCard.Name</c> is a guarded YML lookup that returns an empty string rather
    /// than throwing, but it is wrapped anyway — this runs inside the map room's tick.</summary>
    private string Ids(List<int> ids)
    {
        if (ids.Count == 0)
            return "none";
        var sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < ids.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append('#').Append(ids[i]);
            string? name = NameOf(ids[i]);
            if (!string.IsNullOrEmpty(name))
                sb.Append(" '").Append(name).Append('\'');
        }
        return sb.ToString();
    }

    /// <summary>The card's own name, or null. Looked up in <see cref="_loadout"/> first (the ids we
    /// just added are there) and then in <see cref="_cardModels"/> (the ids we just removed are).</summary>
    private string? NameOf(int id)
    {
        try
        {
            for (int i = 0; i < _loadout.Count; i++)
            {
                if (_loadout[i] != null && _loadout[i].ID == id)
                    return _loadout[i].StrictName;
            }
            for (int i = 0; i < _cardModels.Count; i++)
            {
                if (_cardModels[i] != null && _cardModels[i].ID == id)
                    return _cardModels[i].StrictName;
            }
        }
        catch (System.Exception)
        {
            // A card whose YML row is missing is not worth a line of its own — the ID is in the log.
        }
        return null;
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
        PublishInitiatives();
        if (swap)
            CardsDriver.OffScenarioFanSwap = true;
        CardsDriver.RequestRebuild();
    }

    /// <summary>The initiative of each published card, index-aligned with <see cref="_cards"/> —
    /// see <see cref="PublishInitiatives"/>.</summary>
    private readonly List<int> _initiatives = new(MaxCards);

    /// <summary>
    /// Hand the driver the SORT KEY behind the published order, index-aligned with the card list.
    ///
    /// <para>WHY THE DRIVER NEEDS IT AT ALL: a map-room card deliberately never calls
    /// <c>VRCard.AttachGameCard</c> (that is the basis of the inspection-only guarantee stated in
    /// this file's header), so <c>VRCard.GameCard</c> is null for its whole life and the driver —
    /// which reads a scenario card's initiative straight off <c>GameCard.AbilityCard</c> — has no
    /// way to ask. This list is what lets <c>CardsDriver.FanInitiative</c> answer for BOTH fans, so
    /// its order diagnostic is one implementation rather than two.</para>
    ///
    /// <para>It is DERIVED, never maintained: rebuilt from <see cref="_cardModels"/> (which is
    /// already appended in lockstep with <see cref="_cards"/>) at every publish, so it cannot become
    /// a fifth lockstep invariant to get wrong. A card whose model is missing publishes
    /// <see cref="NoInitiative"/>, which the driver renders as <c>?</c> and excludes from its
    /// sortedness verdict rather than treating as a number.</para>
    ///
    /// <para>MULTIPLAYER: nothing here goes near the wire. The initiative is a LOCAL number used by
    /// a LOCAL layout and a LOCAL log line; card identity still never leaves this machine.</para>
    /// </summary>
    private void PublishInitiatives()
    {
        _initiatives.Clear();
        if (_cards.Count == 0)
        {
            CardsDriver.OffScenarioFanInitiatives = null;
            return;
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            CAbilityCard? model = i < _cardModels.Count ? _cardModels[i] : null;
            _initiatives.Add(model != null ? model.Initiative : CardsDriver.NoInitiative);
        }
        CardsDriver.OffScenarioFanInitiatives = _initiatives;
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
        TickFaceFades();
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
                // Child count BEFORE the print: RemoteCardArt parents its host here, and that is
                // how the fade handle is captured — see TryCaptureFaceGroup.
                int childrenBefore = card.transform.childCount;
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
                // FADE IT IN rather than let the pooled borrow land at full opacity. This is the
                // scenario's own materialize duration, and it covers three cases with one rule:
                // the first reveal (the faces used to POP in at 5 Hz as the borrows landed), a card
                // arriving in a character exchange, and — the case ModBuild 193 exists for — a card
                // JOINING the hand, whose slab is hidden and whose dust is converging while this
                // runs. See the region header for the cross-file defect this stands in for.
                if (i < _faceGroups.Count)
                {
                    _faceGroups[i] = TryCaptureFaceGroup(card, childrenBefore);
                    ArmFaceFade(_faceGroups[i], fadeIn: true, VRCard.DockAppearSeconds);
                }
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
        _faceGroups.Clear();
        _frontsShown = 0;
        _facesLogged = false;
        _nextFacePrintAt = 0f;   // the new set may print in the very next frame
        // A whole-hand retirement supersedes any single-card removal that was waiting for the
        // player to let go: the card it was waiting on is in _retired now, and SweepRetired keeps
        // re-arming its grace for exactly as long as the hold lasts.
        _deferredLeave = false;
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
        _faceFades.Clear();   // every group they pointed at died with its card

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
