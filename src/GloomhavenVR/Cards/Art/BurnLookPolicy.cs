using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHAT A CARD'S BURN LOOK MUST BE — one rule, every surface, every round.
///
/// <para>USER ITEM 4 (2026-09-07, verbatim): <i>"Bei den aktiven Karten haben diese manchmal ein
/// verbrennen overlay nachdem ich sie aktiv gemacht habe - das verschwindet aber wieder in der
/// nächsten Runde. Ich will hier eine Konsitenz auch über Runden hinweg, lokal und remote!"</i></para>
///
/// <para>AND HIS CORRECTION OF THIS FILE'S FIRST VERSION (2026-09-07, verbatim): <i>"Nein du hast
/// Bahn C falsch interpretiert. Ich meine nicht die Animation von 2 Sekunden, sondern den
/// dauerhaften effekt der über eine verbrannte Karte liegt. Und dieser Effekt war bei manchen
/// Aktiven Karten vorhanden und wurde dort auch angezeigt - aber nur eine Runde - die runde darauf
/// war die Karte wieder blau"</i></para>
///
/// <para>THE FIRST VERSION HAD RULE 1 EXACTLY BACKWARDS and would have shipped a regression: it
/// read "an activated card wears a burn overlay" as a leftover to erase. It is not a leftover. The
/// permanent burnt wash on an activated card is INFORMATION — that card is already spent — and the
/// defect is that it does not SURVIVE the round. The word that should have stopped the first
/// version is his own <b>"manchmal"</b>: the mechanism that version named (the widget keeping its
/// look until the game's next hand refresh) would hit EVERY activation, and the evidence below is
/// 2 of 2. An unexplained qualifier in a user report is a falsifier, and it was sitting in the
/// report before the correction was written.</para>
///
/// <list type="number">
///   <item><description><b>RULE 1a — an ACTIVATED card bound for LOST wears the PERMANENT burnt
///   look, and KEEPS it</b> for as long as it sits in the active area: every round, on the owner's
///   board and on every mirror, identically. "Bound for Lost" is the game's OWN expression, at
///   <c>CCharacterClass.cs:479</c>: <c>eCardPile2 = ((selectedAction != null &amp;&amp;
///   selectedAction.CardPile == ECardPile.Discarded) ? ECardPile.Discarded : ECardPile.Lost)</c> —
///   i.e. anything whose selected action is not Discard-bound. The game already counts such a card
///   as gone: <c>CCharacterClass.cs:302</c> adds the Discard-bound activated cards to the DISCARD
///   total, and <c>CAbility.cs:3082</c> does the mirror-image count for the LOST total. Marking it
///   is what the player needs, not a leftover to clean up.</description></item>
///   <item><description><b>RULE 1b — an ACTIVATED card bound for DISCARD never wears the BURNT
///   look, and wears the PERMANENT GREY instead — which it also KEEPS,</b> for as long as it sits in
///   the active area, on every board. A Discard-bound activated card found LATCHED
///   <c>BurnCard</c>/<c>LostMode</c> is a SECOND, separate defect and is logged as one, and the
///   trigger for THAT half is the LATCH and never the paint, because the ghost timeline writes
///   <c>_GreyOut</c> too (CardEffects.cs:686) — see <see cref="EnforceActivated"/>.
///   <para>THE SECOND SENTENCE OF THIS RULE USED TO READ "its own DiscardMode ghost is the game's
///   business and is left alone", AND THAT WAS THE DEFECT, not a scoping decision. The wipe rule 1a
///   exists to undo — <c>SetPile(Activated)</c> → <c>RestoreCard()</c> — does not ask which look it
///   is erasing, so leaving the grey to "the game" meant leaving it to the writer that destroys it.
///   The user's ruling of 2026-09-07 evening, verbatim: <i>"Einmal grau bleibt die Karte (remote UND
///   lokal) grau solange sie im aktiven Stapel liegt. Gleiches gilt für eine verbrannte Karte
///   dort."</i></para></description></item>
///   <item><description><b>RULE 2 — a LOST / PERMANENTLY LOST card is always FULLY burnt</b>, front
///   visible (<i>"Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite
///   sichtbar sein"</i>), at the settled end state <c>_GreyOut = 1</c> — either because the game's
///   own ramp finished it, or because this policy finished it with the game's own no-ramp arm when
///   the game abandoned the ramp half-way.</description></item>
///   <item><description><b>RULE 3 — there is no third state and no per-round state.</b> A card may
///   be mid-ramp only while <c>CardEffects.coroutine</c> is genuinely running a timeline. Any other
///   partial paint is a leftover.</description></item>
///   <item><description><b>RULE 4 — cards in HAND, ROUND and DISCARDED are not this policy's
///   business.</b> The game's own play flourish (<c>FullAbilityCard.TryPlayBurnAnimation</c>) and
///   its discard ghost run there on the OWNER's adopted widget, and the user has never complained
///   about either. <para>THE EXCLUSION IS ABOUT THIS BOARD AND NOTHING ELSE. On a MIRROR the face is
///   a clone with <c>CardEffects</c> stripped, so "the game paints it" is false for every one of
///   those three piles; a peer's DISCARD arc drew fresh, default-coloured cards for exactly that
///   reason (2026-09-07 evening, item 2a). The mirror asks <see cref="ForCard"/> instead — the
///   durable term — and drives it through its own rig. Share the TERM, not the
///   exclusion.</para></description></item>
/// </list>
///
/// <para>WHO WIPES IT, AND WHY IT IS "nur eine Runde". <c>FullAbilityCard.SetPile</c> is reached
/// only from <c>AbilityCardUI.UpdateCard()</c>, i.e. only when the game refreshes its own 2D hand
/// view — the next card selection. For <c>ECardPile.Activated</c> that branch calls
/// <c>cardEffects.RestoreCard()</c> (FullAbilityCard.cs:325-328), and THAT call zeroes
/// <c>_GreyOut/_Flow/_Dissolve/_Burn</c> without asking where the card is going.</para>
///
/// <para>WHAT REACHING IT IS NOT, corrected 2026-09-07: the branch is not unconditional. All three
/// FX arms sit inside <c>if (cardPile != newCardPile &amp;&amp; cardEffects != null)</c>
/// (FullAbilityCard.cs:313-315), and <c>UpdateCard()</c> re-passes the same <c>cardType</c>
/// (AbilityCardUI.cs:770-773) — so the wipe needs the WIDGET's own last value to change, not the
/// model's. <c>AbilityCardUI.ToggleHighlight</c> writes <c>cardType</c> behind <c>SetPile</c>'s
/// back (:1096, :1186), which manufactures that edge with no model move at all. The measurement
/// below is unaffected: the wash was observed going and coming back on both machines. Only the
/// story about WHEN was wrong.</para>
///
/// <para>So a Lost-bound activated card is painted at play time and un-painted one round later, which is
/// <i>"aber nur eine Runde - die runde darauf war die Karte wieder blau"</i>, word for word. That
/// is why rule 1a is enforced on a CADENCE (<see cref="RecheckSeconds"/>) rather than once: the
/// wipe arrives with no pile change and therefore with no edge to hang a one-shot on.</para>
///
/// <para>THE EVIDENCE, and what it does and does not settle. The ModBuild 476 host log ties the
/// game's burn/ghost artwork to the card that flew into the ACTIVE column, by POSITION, on both
/// activations of the session — <c>Player.log:83670</c>/<c>:83766</c> at
/// <c>(-5.00, 12.06, 16.84)</c> against <c>:83749</c>
/// <c>'VRCard_ABILITY_CARD_TheMindsWeakness': fly-in from (-5.00,12.06,16.84)</c>, and
/// <c>:123923</c> at <c>(-6.60, 11.38, 14.46)</c> against <c>:123973/:123974</c> for
/// <c>GnawingHorde</c>. Under the CORRECTED rule that is the overlay arriving CORRECTLY if those
/// two cards are Lost-bound. NEITHER LOG CAN SAY WHICH THEY WERE: no line in either 71 MB file
/// carries a destination, and the card data lives in the game's addressables rather than in
/// anything readable offline. That blind spot is why this needed a user correction, and it is
/// closed from both ends: <c>[Cards] ACTIVE SET</c>'s MODEL row now carries each activated
/// card's destination (the fact), and <see cref="ReportDestination"/> prints this board's
/// picture against it, change-triggered, paired with the mirror's <c>[Net] REMOTE ACTIVE
/// WASH</c>.</para>
///
/// <para>THE MIRROR DOES THIS TOO, and rule 1a is one expression on both boards.
/// <see cref="ForActivatedCard"/> is asked by this policy for the owner's adopted face and
/// by <c>Net.Remote.RemoteActiveCards</c> → <c>RemoteBoardCard.SetActiveCardLook</c> for every
/// mirrored active cell, over the SAME local <c>CAbilityCard</c> object — not two copies that agree
/// today. The mirror writes the settled end state with <c>RemoteCardArt.SetAbilityCardFxProgress</c>
/// and NEVER a ramp, because the separate 2026-09-06 item 8a ruling forbids the burn animation and
/// sound at the moment of activation; the user drew that line himself when he said <i>"Ich meine
/// nicht die Animation von 2 Sekunden, sondern den dauerhaften effekt"</i>. The write ordering there
/// is hold-then-paint through <see cref="CardHalfTone.HoldCardFxLook"/>, because
/// <c>NormalizeCardFx</c> would otherwise swap the clone's images to a SHARED rest copy that the
/// burn rig's next write would then char for every other clone using it. Since ModBuild 479 that
/// hold is taken by <c>Net.RemoteCardArt</c> itself rather than by each mirrored surface — see its
/// own doc for the write war a per-surface hold produced on the burnt pile fan.</para>
///
/// <para>THIS WRITES PRESENTATION ONLY, THROUGH THE GAME'S OWN CALLS. Rule 1a and rule 2 use
/// <see cref="BurnArtwork.TrySettleBurnLook"/>, which drains
/// <c>CardEffects.BurnCardTimeline(burnAnim: false, …)</c> — the no-ramp arm the game itself uses
/// for the settled end state. Rule 1b uses <c>CardEffects.RestoreCard()</c>, the exact call
/// <c>SetPile(Activated)</c> makes. Neither touches game state and the game's own
/// <c>RestoreCard()</c> undoes both in full if a card is ever recovered.</para>
/// </summary>
internal static class BurnLookPolicy
{
    private const string Scope = "Cards";

    /// <summary>
    /// How often one card's look is re-checked against the rule.
    ///
    /// <para>A CADENCE AND NOT A LATCH, and rule 1a is the reason. The wipe this policy has to undo
    /// (<c>SetPile(Activated) → RestoreCard()</c>) arrives with NO pile change and no timeline, so
    /// there is no edge a one-shot could hang on; a permanent "already verified" entry would be
    /// correct exactly once and then let the card go blue again for the rest of the scenario —
    /// which is the defect, not the fix. Reading the paint is up to ten <c>Material.GetFloat</c>
    /// calls, so twice a second per card is the cost, against a per-frame sweep that this project
    /// keeps finding at the top of its spike lists.</para>
    /// </summary>
    private const float RecheckSeconds = 0.5f;

    /// <summary>
    /// How many corrective writes IN QUICK SUCCESSION one <c>CardEffects</c> may take before this
    /// policy stands down and names the card.
    ///
    /// <para>A RATE AND NOT A LIFETIME COUNT. A lifetime budget would be spent by the fourth
    /// legitimate round-boundary wipe and the card would go blue again — the first version's bug
    /// repeated in the accounting. What this must catch is a SECOND WRITER repainting every frame,
    /// which shows up as writes spaced at the cadence above; a once-a-round wipe is spaced by a
    /// round. So the counter resets after <see cref="WriteWarQuietSeconds"/> of quiet and only a
    /// run of fast writes stands the policy down. The standing ruling is not to win a write war but
    /// to name it.</para>
    /// </summary>
    private const int MaxWritesInARow = 6;

    /// <summary>Quiet period after which a card's fast-write run is forgotten. Comfortably longer
    /// than <see cref="RecheckSeconds"/> and far shorter than a Gloomhaven round.</summary>
    private const float WriteWarQuietSeconds = 3f;

    /// <summary>How far from <c>RestoreCard()</c>'s zero a paint may sit before it counts as a burn
    /// look. Well below anything the eye resolves, well above float noise — the same threshold
    /// <see cref="CardHalfTone"/> uses on the same four terms, for the same reason.</summary>
    private const float RestEpsilon = 0.002f;

    /// <summary>Per-<c>CardEffects</c> bookkeeping: which card and pile the last check was for, when
    /// the next check is due, and the fast-write run described at <see cref="MaxWritesInARow"/>.
    /// Pruned by <see cref="Forget"/> when a card's face is dropped.</summary>
    private struct Track
    {
        public int Card;
        public CBaseCard.ECardPile Pile;
        public float NextCheck;
        public int Writes;
        public float LastWrite;
    }

    private static readonly Dictionary<int, Track> s_track = new(16);

    /// <summary>
    /// The change-gate for <see cref="ReportDestination"/>, and it is a field of its OWN.
    ///
    /// <para>IT USED TO RIDE IN <see cref="Track"/> AND THAT WAS A DEFECT THE GATE SUITE CAUGHT:
    /// <c>check-instrument-writes</c> refused it as a NEW LOAD-BEARING WRITE INSIDE A DIAGNOSTIC —
    /// the logger would have been writing a dictionary the mechanism reads, so retiring or gating
    /// the log line would have changed behaviour. That is the shape that once nearly latched the
    /// wall fade off forever. Instrument state lives apart from mechanism state; nothing outside
    /// this instrument touches it, including <see cref="Forget"/> and <see cref="Reset"/>, which is
    /// why it bounds ITSELF below rather than being cleared from outside.</para>
    /// </summary>
    private static readonly Dictionary<int, (int Card, CBaseCard.ECardPile Dest, bool Wearing)>
        s_reportedDest = new(16);

    /// <summary>Self-bound for the instrument dictionary above. A wipe only costs one repeated
    /// line per card; it can never cost correctness, because nothing reads it but the log.</summary>
    private const int MaxReportedFaces = 128;

    private static bool s_keptLogged;
    private static bool s_settleLogged;
    private static bool s_refusedLogged;
    private static bool s_strayLogged;
    private static bool s_ghostKeptLogged;
    private static bool s_ghostRefusedLogged;
    private static bool s_budgetLogged;

    /// <summary>
    /// Bring one adopted card face's burn look in line with the rules above. Called every frame from
    /// <see cref="BurnCardFx.Tick"/>, which is the one per-card tick that already holds the adopted
    /// <c>FullAbilityCard</c>. Costs one dictionary lookup and one float compare on the frames
    /// between checks.
    /// </summary>
    internal static void Enforce(FullAbilityCard? full) => Enforce(full, null);

    internal static void Enforce(FullAbilityCard? full, AbilityCardUI? widget)
    {
        if (full == null)
            return;
        CardEffects? fx = BurnArtwork.EffectsOf(full);
        if (fx == null)
            return;

        CAbilityCard? card;
        CBaseCard.ECardPile pile;
        int cardId;
        bool running;
        try
        {
            // Preview/hand widgets do not initialize FullAbilityCard.AbilityCard; after pooling it
            // may still name an earlier action card. The adopted AbilityCardUI owns this face.
            card = widget != null ? widget.AbilityCard : full.AbilityCard;
            pile = card != null ? card.CurrentCardPile : CBaseCard.ECardPile.None;
            // The CARD half of the tracking key. A CardEffects belongs to a POOLED widget, so its
            // instance id alone would carry a verdict from a previous card into the next borrow of
            // that widget.
            cardId = card != null ? card.CardInstanceID : 0;
            running = fx.coroutine != null;
        }
        catch { return; }
        if (card == null)
            return;

        int id = fx.GetInstanceID();
        float now = Time.unscaledTime;
        s_track.TryGetValue(id, out Track t);
        if (t.Card != cardId)
        {
            // A different card is on this widget now — nothing carried over may be believed.
            t = default;
            t.Card = cardId;
        }

        if (running)
        {
            // A real timeline owns the card. Never cut across the game's own ramp, and check again
            // as soon as it is over: what it leaves behind is what the rule has to be judged on.
            t.Pile = pile;
            t.NextCheck = 0f;
            s_track[id] = t;
            return;
        }
        if (t.Pile == pile && now < t.NextCheck)
            return;
        t.Pile = pile;
        t.NextCheck = now + RecheckSeconds;
        s_track[id] = t;

        if (IsActivated(full, card, widget?.PlayerActor))
            EnforceActivated(fx, card, id, now);
        else if (IsLost(full, card, widget?.PlayerActor))
            EnforceLost(fx, card, id, now);
    }

    /// <summary>Drop a face's bookkeeping when its VR card lets go of it (teardown / re-adoption).
    /// Keeps <see cref="s_track"/> the size of the live card population rather than of the
    /// session.</summary>
    internal static void Forget(FullAbilityCard? full)
    {
        CardEffects? fx = BurnArtwork.EffectsOf(full);
        if (fx != null)
            s_track.Remove(fx.GetInstanceID());
    }

    /// <summary>Reset every latch and every tracked face (scenario teardown / hot reload).</summary>
    internal static void Reset()
    {
        s_track.Clear();
        s_keptLogged = false;
        s_settleLogged = false;
        s_refusedLogged = false;
        s_strayLogged = false;
        s_ghostKeptLogged = false;
        s_ghostRefusedLogged = false;
        s_budgetLogged = false;
    }

    // ------------------------------------------------------------------ the model questions --

    /// <summary>
    /// TRUE while this card is in the ACTIVE area, asked of the MODEL and of nothing else.
    ///
    /// <para><see cref="ActiveCardSet.IsActive(CPlayerActor, CBaseCard)"/> is the project's
    /// designated authority and this is its FIRST caller — <c>ActiveCardSet</c>'s own doc says
    /// <i>"Sibling lanes that need 'is this card currently active' — the burn-presentation trigger
    /// (activated-but-not-yet-lost must not look or sound burnt) … call IsActive(…)"</i>, and until
    /// this file nobody did. (That parenthesis is itself now out of date: the user's correction
    /// says an activated card bound for Lost MUST look burnt.)</para>
    ///
    /// <para>The card's own <c>CurrentCardPile</c> stamp is the FALLBACK, for a widget with no
    /// actor to ask. It is trustworthy for exactly this transition:
    /// <c>CCharacterClass.MoveAbilityCardToPile</c> (:452) — the end-of-turn drain that produces an
    /// activation — is one of the few places the field is written at all, which is the finding
    /// <c>RemoteBoardCard.cs:1140-1155</c> records at length.</para>
    /// </summary>
    private static bool IsActivated(FullAbilityCard full, CAbilityCard card, CPlayerActor? widgetOwner = null)
    {
        try
        {
            CPlayerActor? owner = widgetOwner ?? CardsGameApi.CardOwner(full);
            if (owner != null)
                return ActiveCardSet.IsActive(owner, card);
            return card.CurrentCardPile == CBaseCard.ECardPile.Activated;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// IS THIS CARD BURNT? The owner's own two LOST LISTS first, and the card's
    /// <c>CurrentCardPile</c> stamp only as the fallback for a widget with no actor to ask.
    ///
    /// <para>WHY THE LISTS AND NOT THE STAMP (2026-09-07 review, F2, re-derived in the decompile).
    /// <c>GameState.Lose1HandCardToAvoidAttack</c> (GameState.cs:1503-1507) and
    /// <c>Lose2DiscardCardsToAvoidAttack</c> (:1509-1515) — the damage-negation burns — call
    /// <c>CCharacterClass.MoveAbilityCard</c> DIRECTLY (:273-309). That method edits the two
    /// <c>List&lt;CAbilityCard&gt;</c> and NEVER writes <c>CurrentCardPile</c>; grepped across the
    /// whole rule library, the only writers of that property are
    /// <c>MoveAbilityCardToPile</c> (CCharacterClass.cs:452),
    /// <c>RestoreCachedAugmentOrSongAbilityCard</c> (:548), <c>Reset</c> (:1198, :1207) and
    /// <c>CBaseCard</c>'s own serialisation/copy paths (CBaseCard.cs:127, :152, :311, :466 — the
    /// last of which COPIES the stale value into a state snapshot rather than correcting it). So
    /// after a card is burnt to negate damage its stamp still reads <c>Hand</c> (1-card variant) or
    /// <c>Discarded</c> (2-card variant) for the rest of the scenario, while the card itself sits in
    /// <c>LostAbilityCards</c>.</para>
    ///
    /// <para>WHAT THAT COST. This predicate is rule 2's gate — <i>"a lost card is always FULLY
    /// burnt, front visible"</i>, the user's <i>"Beim Verbrennen EGAL AUS WELCHEM GRUND muss die
    /// Karte immer mit der Vorderseite sichtbar sein"</i> — so a damage-negation burn was the one
    /// burn rule 2 never ran on, on the OWNER's own board.</para>
    ///
    /// <para>THE LIST IS THE AUTHORITY THIS TREE ALREADY USES for exactly this question:
    /// <c>Net.RevealGate.IsPubliclyRevealedCard</c> walks the same two lists to decide the card's
    /// FACE, which is why the face was right while the look was wrong. Membership is tested by
    /// <c>CardInstanceID</c> rather than by reference, for the same reason it is there.</para>
    ///
    /// <para>NOT <c>GameState.CardsBurnedToAvoidDamage</c>, and that was checked before it was
    /// rejected: it is <c>Clear()</c>ed at the head of every damage-avoidance episode
    /// (<c>ContinueActorDamagedAfterSelectingPlayerActorToBurnCards</c>, GameState.cs:1225) and
    /// therefore holds only the CURRENT episode's cards — the very next attack that offers the
    /// choice wipes it. It is also a <c>volatile List</c> mutated on the SRL worker thread. It is a
    /// transient message payload, not a durable record, and a look built on it would go clean at
    /// the next attack.</para>
    /// </summary>
    private static bool IsLost(FullAbilityCard? full, CAbilityCard card, CPlayerActor? widgetOwner = null)
    {
        try
        {
            CPlayerActor? owner = widgetOwner ?? (full != null ? CardsGameApi.CardOwner(full) : null);
            // Membership outranks a stale pile stamp in both directions, including recovery.
            if (owner?.CharacterClass != null)
                return HeldInLostList(owner, card);
            return card.CurrentCardPile == CBaseCard.ECardPile.Lost
                   || card.CurrentCardPile == CBaseCard.ECardPile.PermanentlyLost;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Is <paramref name="card"/> lying in either of <paramref name="owner"/>'s burnt
    /// lists? False for a null owner, which is the fallback case the caller answers from the
    /// stamp — never an assertion that the card is clean.</summary>
    private static bool HeldInLostList(CPlayerActor? owner, CAbilityCard card)
    {
        CCharacterClass? cc = owner != null ? owner.CharacterClass : null;
        if (cc == null)
            return false;
        int id = card.CardInstanceID;
        return HoldsCard(cc.LostAbilityCards, id) || HoldsCard(cc.PermanentlyLostAbilityCards, id);
    }

    /// <summary>Walked by index and never with LINQ: this runs on the 0.5 s policy cadence for
    /// every adopted card and on every mirrored settled-look resolve.</summary>
    private static bool HoldsCard(List<CAbilityCard>? list, int cardInstanceId)
    {
        if (list == null)
            return false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].CardInstanceID == cardInstanceId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// WHERE AN ACTIVATED CARD IS GOING — <c>Lost</c> or <c>Discarded</c> — under the game's own
    /// expression, character for character.
    ///
    /// <para><c>CCharacterClass.cs:478-480</c>, inside <c>case ECardPile.Activated:</c>:
    /// <c>CAction selectedAction = abilityCard.SelectedAction; eCardPile2 = ((selectedAction != null
    /// &amp;&amp; selectedAction.CardPile == CBaseCard.ECardPile.Discarded) ? ECardPile.Discarded :
    /// ECardPile.Lost);</c> — so the default is LOST and only an explicitly Discard-bound selected
    /// action escapes it. The value is available for the whole time the card sits in the active
    /// area: <c>MoveAbilityCardToPile</c> calls <c>SetSelectedAction(null)</c> on the Lost, Discard
    /// and PermanentlyLost arms but NOT on the Activated arm (:483-497), so
    /// <c>SelectedAction</c> is intact for exactly the population this policy asks about.</para>
    /// </summary>
    internal static CBaseCard.ECardPile Destination(CAbilityCard? card)
    {
        if (card == null)
            return CBaseCard.ECardPile.Lost;
        try
        {
            CAction? selected = card.SelectedAction;
            return selected != null && selected.CardPile == CBaseCard.ECardPile.Discarded
                ? CBaseCard.ECardPile.Discarded
                : CBaseCard.ECardPile.Lost;
        }
        catch
        {
            return CBaseCard.ECardPile.Lost;   // the game's own default arm
        }
    }

    /// <summary>
    /// DOES THIS ACTIVATED CARD WEAR THE PERMANENT BURNT WASH? Rule 1a, as a single expression, so
    /// the owner's board and every mirror ask the SAME question rather than two that happen to
    /// agree today.
    ///
    /// <para>THE SHAPE IS DELIBERATE AND IT IS <see cref="BurnArtwork.Released"/>'s. That constant
    /// pair was split across two files once and the ModBuild 474 logs measured the drift on three
    /// burns in one session (+0.33 / −0.48 / −1.45 s). The mirror's caller —
    /// <c>Net.Remote.RemoteActiveCards</c> → <c>RemoteBoardCard.SetActiveBurntWash</c> — asks THIS
    /// method over the SAME <c>CAbilityCard</c> object, because the model is local: the peer's
    /// activated list and its cards' <c>SelectedAction</c> are on every client already. No wire
    /// field is owed for any of it and none is added.</para>
    ///
    /// <para>The caller supplies "this card is in the ACTIVE area" — the mirror already has it (its
    /// buffer IS <c>ActiveCardSet.ActivatedCards</c>) and repeating the membership walk here would
    /// be a second source for a fact the caller holds.</para>
    /// </summary>
    internal static bool ActiveCardWearsBurntWash(CAbilityCard? card)
        => Destination(card) == CBaseCard.ECardPile.Lost;

    /// <summary>
    /// THE THREE LOOKS the game's two <c>CardEffects</c> timelines produce, named once so the
    /// owner's board and every mirror can pass one answer around instead of a boolean per surface.
    ///
    /// <para>IT IS DELIBERATELY NOT <c>Net.RemoteCardArt.CardFxLook</c>, and that is the sharing
    /// ruling rather than an oversight: CALLS GO DOWN. <c>Net/</c> may read this file; <c>Cards/</c>
    /// must never learn that a mirror exists. <c>Net.UsedCardLook.FromPolicy</c> is the one-line
    /// map between the two and is the ONLY place the two enums meet.</para>
    /// </summary>
    internal enum Look
    {
        /// <summary>A clean card. Nobody has used it, or the game has restored it.</summary>
        None,

        /// <summary>The cold grey-out — <c>CardEffects.GhostOutOnTimeline</c>, which
        /// <c>FXTask.DiscardMode</c> runs.</summary>
        Ghost,

        /// <summary>The warm char — <c>CardEffects.BurnCardTimeline</c>, which
        /// <c>FXTask.BurnCard</c> and <c>FXTask.LostMode</c> both run.</summary>
        Burn,
    }

    /// <summary>
    /// RULE 1 AS ONE ANSWER: what an ACTIVATED card wears. <see cref="Look.Burn"/> for a Lost-bound
    /// one (rule 1a), <see cref="Look.Ghost"/> for a Discard-bound one (rule 1b).
    ///
    /// <para>THERE IS NO THIRD ANSWER, and that is the 2026-09-07 evening ruling rather than an
    /// inference: <i>"Einmal grau bleibt die Karte (remote UND lokal) grau solange sie im aktiven
    /// Stapel liegt. Gleiches gilt für eine verbrannte Karte dort."</i> A card is in the active area
    /// only because its action was PLAYED, so a clean activated card is never right; the first
    /// version of rule 1b left the ghost to the game and the game wiped it one round later, which is
    /// the defect measured on both machines this round.</para>
    ///
    /// <para>The caller supplies "this card is in the ACTIVE area", exactly as
    /// <see cref="ActiveCardWearsBurntWash"/> does and for the same reason.</para>
    /// </summary>
    internal static Look ForActivatedCard(CAbilityCard? card)
        => ActiveCardWearsBurntWash(card) ? Look.Burn : Look.Ghost;

    /// <summary>
    /// THE DURABLE LOOK a card is owed WHEREVER IT IS DRAWN, from the card's own state and from
    /// nothing that is running this frame.
    ///
    /// <para>WHY IT IS STATE AND NOT AN EFFECT FLAG. <c>CardEffects.HasEffect</c> is a
    /// <c>HashSet&lt;FXTask&gt;</c> membership test (CardEffects.cs:354-357) that <c>RestoreCard()</c>
    /// clears, and <c>FullAbilityCard.SetPile(Hand|Activated)</c> calls <c>RestoreCard()</c>
    /// (:325-328). So the widget's own answer is a faithful mirror of a latch the GAME drops —
    /// which is why a surface that can only ask the widget draws a spent card clean afterwards, and
    /// why every surface that has a durable question available should ask this instead.</para>
    ///
    /// <para>CORRECTION (2026-09-07): that call is NOT unconditional, as this paragraph used to
    /// say. All three FX arms of <c>SetPile</c> sit inside
    /// <c>if (cardPile != newCardPile &amp;&amp; cardEffects != null)</c> (FullAbilityCard.cs:313-315),
    /// so <c>RestoreCard()</c> fires only on a CHANGE into Hand or Activated measured against the
    /// value the WIDGET last received. The erasure is real and this method is still owed; its
    /// trigger is a widget-local edge, not the round boundary.</para>
    ///
    /// <para><c>CBaseCard.CurrentCardPile</c> is the field: serialized (CBaseCard.cs:103/:126) and
    /// checked by the game's own multiplayer state comparison (:382-403, mismatch code 2804), so it
    /// cannot disagree between two machines. ITS BLIND SPOT is every move that goes through
    /// <c>CCharacterClass.MoveAbilityCard</c> rather than <c>MoveAbilityCardToPile</c>: that method
    /// edits the two LISTS and never writes the field. Two consequences, and they need different
    /// answers:
    /// <list type="bullet">
    ///   <item>A card a rest handed back to the HAND still reads <c>Discarded</c>. Nothing here can
    ///   see that, so a caller whose population may contain hand cards must answer the population
    ///   itself before asking — the recess resolver and the held-card face both do.</item>
    ///   <item>A card BURNT TO NEGATE DAMAGE still reads <c>Hand</c> (or <c>Discarded</c> for the
    ///   2-card variant) while it sits in <c>LostAbilityCards</c> — see <see cref="IsLost"/> for
    ///   the derivation. That one IS answered here, by the owner-taking overload below:
    ///   passing the card's owner lets this method ask the LIST, which is the same authority
    ///   <c>Net.RevealGate.IsPubliclyRevealedCard</c> uses for the card's FACE. WITHOUT an owner
    ///   this method still reads the stale stamp and answers <c>None</c> / <c>Ghost</c> for a card
    ///   the owner sees charred, which is the 2026-09-07 F2 picture; every caller that can name the
    ///   owner should.</item>
    /// </list></para>
    /// </summary>
    internal static Look ForCard(CAbilityCard? card) => ForCard(null, card);

    /// <summary>
    /// <see cref="ForCard(CAbilityCard?)"/> with the card's OWNER named, so the burnt lists can be
    /// asked before the <c>CurrentCardPile</c> stamp — the only form that is right for a card burnt
    /// to negate damage. A null <paramref name="owner"/> degrades to the stamp, which is exactly
    /// the older behaviour and never a worse answer than it.
    /// </summary>
    internal static Look ForCard(CPlayerActor? owner, CAbilityCard? card)
    {
        if (card == null)
            return Look.None;
        try
        {
            // THE LIST OUTRANKS THE STAMP, and only in this direction: a card the owner's burnt
            // list holds is burnt no matter what the stamp says, because MoveAbilityCard moved it
            // there without writing the stamp. The reverse is never asserted — a card the list does
            // not hold falls through to the stamp rather than being declared clean, so an actor
            // this client cannot resolve costs nothing.
            if (HeldInLostList(owner, card))
                return Look.Burn;
            switch (card.CurrentCardPile)
            {
                case CBaseCard.ECardPile.Lost:
                case CBaseCard.ECardPile.PermanentlyLost:
                    return Look.Burn;
                case CBaseCard.ECardPile.Discarded:
                    return Look.Ghost;
                case CBaseCard.ECardPile.Activated:
                    return ForActivatedCard(card);
                default:
                    return Look.None;
            }
        }
        catch
        {
            // An unreadable card draws CLEAN, which is the safe direction every mirrored surface
            // already takes: a missing wash is a small divergence, a wash on a card the owner has
            // not used is a lie about their board.
            return Look.None;
        }
    }

    // ------------------------------------------------------------------------ the two rules --

    /// <summary>RULES 1a and 1b — the activated card, split by where it is going.</summary>
    private static void EnforceActivated(CardEffects fx, CAbilityCard card, int id, float now)
    {
        CBaseCard.ECardPile dest = Destination(card);
        bool latched = BurnArtwork.Latched(fx);
        float painted = BurnArtwork.PaintProgress(fx);

        ReportDestination(card, id, dest, latched, painted);

        if (dest == CBaseCard.ECardPile.Discarded)
        {
            // -- RULE 1b, HALF ONE -- THE STRAY BURN -------------------------------------------
            // THE TRIGGER IS THE LATCH AND NEVER THE PAINT, and that distinction is load-bearing:
            // CardEffects.GhostOutOnTimeline drives the SAME _GreyOut property (CardEffects.cs:686),
            // so a paint-based test could not tell a stray CHAR from the discard GHOST this card is
            // entitled to. HasEffect names the task; the paint cannot.
            bool strayCleared = false;
            if (latched)
            {
                if (!TakeBudget(fx, card, id, now,
                                "RULE 1b (a discard-bound activated card is not burnt)"))
                    return;
                try { fx.RestoreCard(); }
                catch { return; }
                // RestoreCard() writes 0 to all four terms (CardEffects.cs:474-484), so the reading
                // taken before it is stale from here on and half two must not judge on it. An
                // UNREADABLE widget stays unreadable, though: -1 is not "unpainted", it is "no
                // image on this face carries _GreyOut at all", and turning it into a 0 here would
                // send half two to settle a look on a widget it cannot see.
                painted = painted < 0f ? painted : 0f;
                strayCleared = true;
                ReportActiveBurnStray(card);
            }

            // -- RULE 1b, HALF TWO -- THE GREY IS DURABLE TOO (ModBuild 479, user item 4) -------
            // THE SENTENCE THAT USED TO STAND HERE WAS "its own DiscardMode ghost is the game's
            // business and is left alone", and this round's ruling retires it, verbatim: "Einmal
            // grau bleibt die Karte (remote UND lokal) grau solange sie im aktiven Stapel liegt.
            // Gleiches gilt fuer eine verbrannte Karte dort."
            //
            // THE WIPE IS THE SAME ONE RULE 1a ALREADY UNDOES, and that is why leaving this half
            // out was never defensible: FullAbilityCard.SetPile(Activated) -> RestoreCard()
            // (:325-328) does not ask which look it is erasing. Rule 1a put the CHAR back and
            // nobody put the GREY back, so a Lost-bound activated card was consistent across rounds
            // and a Discard-bound one went blue -- the user's "Nach einer Runde wurde die aktive
            // Karte von grau (verbraucht) blau", exactly.
            //
            // MEASURED, ModBuild 478, both machines of the 2026-09-07 evening session, on the same
            // card: '[Cards] ACTIVE WASH' for 'ABILITY_CARD_TheMindsWeakness', ACTIVATED and bound
            // for Discarded, reads "wearing = True (latched=False, _GreyOut 1.00)" and then, later
            // in the same session, "wearing = False (latched=False, _GreyOut 0.00)" -- user log
            // lines 23355 and 26305, peer log lines 14953 and 42556. latched=False on both readings
            // is what says the look it lost is a GHOST and not a char, i.e. exactly the half this
            // arm owns. Over the same window 'ACTIVE BURN KEPT' fires once for the Lost-bound
            // 'ABILITY_CARD_WardingStrength' (peer log 22847, between its own 0.00 and 1.00
            // readings) -- rule 1a catching the identical wipe and putting its look back.
            if (painted < 0f || painted >= BurnArtwork.FinishedGreyOut)
                return;                   // unreadable, or already correct
            if (!strayCleared
                && !TakeBudget(fx, card, id, now,
                               "RULE 1b (a discard-bound activated card stays grey)"))
                return;
            if (!BurnArtwork.TrySettleGhostLook(fx))
            {
                if (s_ghostRefusedLogged)
                    return;
                s_ghostRefusedLogged = true;
                // HW-VERIFY (2026-09-07 item 4, the GHOST half): the remedy was OWED and REFUSED.
                // Grep token: "GHOST LOOK REFUSED".
                //
                // A gated remedy that never runs reports nothing, so the refusal gets its own line,
                // exactly as the burn half's does. TrySettleGhostLook refuses a widget the game
                // never ToggleEffect'd (txtAffected / imgComp still null), one with a live
                // coroutine, and one whose private fgFx overlay is missing -- the no-ramp arm
                // dereferences it unguarded, so that case throws and is caught rather than tested.
                VRLog.Note(Scope, $"GHOST LOOK REFUSED: '{Name(card)}' is ACTIVATED and " +
                                  $"DISCARD-bound, its paint reads _GreyOut {painted:F2} of 1.00, " +
                                  "and the settle refused - CardEffects.GhostOutOnTimeline's " +
                                  "no-ramp arm indexes txtAffected and imgComp and dereferences " +
                                  "fgFx, so the widget was never ToggleEffect'd/Initialize'd on " +
                                  "this client or carries no _uiFxOverlay. The card stays blue and " +
                                  "the rule is NOT being enforced on it.");
                return;
            }
            if (s_ghostKeptLogged)
                return;
            s_ghostKeptLogged = true;
            // HW-VERIFY (2026-09-07 evening, item 4: "Einmal grau bleibt die Karte (remote UND
            // lokal) grau solange sie im aktiven Stapel liegt"): the permanent GREY was missing
            // from a discard-bound activated card and has been put back. Grep token:
            // "ACTIVE GHOST KEPT".
            //
            // PROOF the rule is doing its job: this line, with painted well under 1.00, on a card
            // the ACTIVE WASH line beside it names as Discard-bound. A reading near 0.00 is the
            // "wieder blau" state itself, measured.
            // FALSIFIER: this line never appears across a session that contains a discard-bound
            // activation, which would mean the grey survives on its own and the reversion the
            // ACTIVE WASH pair measured has some other trigger.
            // SECOND FALSIFIER: 'BURN LOOK BUDGET' for the same card, i.e. a second writer.
            VRLog.Note(Scope, $"ACTIVE GHOST KEPT: '{Name(card)}' is ACTIVATED and bound for " +
                              "DISCARD (CCharacterClass.cs:479), so the PERMANENT grey-out belongs " +
                              $"on it - but its paint read _GreyOut {painted:F2} of 1.00, i.e. the " +
                              "grey was missing or half gone. Restored to the settled end state " +
                              "with the game's OWN no-ramp arm " +
                              "(GhostOutOnTimeline(ghostAnim: false)), front visible. The wipe is " +
                              "the same one rule 1a already undoes for the char: " +
                              "FullAbilityCard.SetPile(Activated) calls RestoreCard() and never " +
                              "asks which look it is erasing. It is reached only from " +
                              "AbilityCardUI.UpdateCard() and only when the WIDGET's own cardPile " +
                              "actually changes (FullAbilityCard.cs:313-315) - not on every hand " +
                              "refresh, which is a correction of what this line used to claim. " +
                              "RULE: an ACTIVATED card wears the permanent BURNT look if " +
                              "it is bound for Lost and the permanent GREY if it is bound for " +
                              "Discard, and keeps it for as long as it sits in the active area, on " +
                              "every board; there is no third state and no clean activated card.");
            return;
        }

        // RULE 1a — the permanent burnt look is OWED on this card and must survive the round.
        if (painted < 0f || painted >= BurnArtwork.FinishedGreyOut)
            return;                       // unreadable, or already correct
        if (!TakeBudget(fx, card, id, now, "RULE 1a (a lost-bound activated card stays burnt)"))
            return;
        if (!BurnArtwork.TrySettleBurnLook(fx))
        {
            if (s_refusedLogged)
                return;
            s_refusedLogged = true;
            // HW-VERIFY (2026-09-07 item 4): the remedy was OWED and REFUSED. Grep token:
            // "BURN LOOK REFUSED".
            //
            // A gated remedy that never runs reports nothing, so the refusal gets its own line.
            // TrySettleBurnLook refuses a widget the game never ToggleEffect'd (txtAffected /
            // imgComp still null) and one with a live coroutine. Neither should be reachable here
            // — Enforce already refused a running timeline, and a card that reached the active
            // pile went through TryPlayBurnAnimation — so this line appearing means one of those
            // two premises is false, and THAT is the next round's lead.
            VRLog.Note(Scope, $"BURN LOOK REFUSED: '{Name(card)}' is ACTIVATED and LOST-bound, its " +
                              $"paint reads _GreyOut {painted:F2} of 1.00, and the settle refused — " +
                              "CardEffects.BurnCardTimeline's no-ramp arm indexes txtAffected and " +
                              "imgComp, so the widget was never ToggleEffect'd/Initialize'd on this " +
                              "client. The card stays unmarked and the rule is NOT being enforced " +
                              "on it.");
            return;
        }

        if (s_keptLogged)
            return;
        s_keptLogged = true;
        // HW-VERIFY (2026-09-07 item 4, the user's correction: "dieser Effekt war bei manchen
        // Aktiven Karten vorhanden und wurde dort auch angezeigt - aber nur eine Runde - die runde
        // darauf war die Karte wieder blau"): the permanent burnt wash was MISSING from a
        // lost-bound activated card and has been put back. Grep token: "ACTIVE BURN KEPT".
        //
        // PROOF the rule is doing its job: this line, with painted well under 1.00, on a card the
        // ACTIVE DESTINATION line below names as Lost-bound. A reading near 0.00 is the "wieder
        // blau" state itself, measured — that is FullAbilityCard.SetPile(Activated) →
        // RestoreCard() having wiped it at the round boundary (FullAbilityCard.cs:325-328).
        // FALSIFIER: this line never appears across a session with a lost-bound activation in it,
        // which would mean the wash survives on its own and the wipe has some other trigger.
        // SECOND FALSIFIER: 'BURN LOOK BUDGET' for the same card, i.e. a second writer.
        VRLog.Note(Scope, $"ACTIVE BURN KEPT: '{Name(card)}' is ACTIVATED and bound for LOST " +
                          "(CCharacterClass.cs:479), so the PERMANENT burnt wash belongs on it — " +
                          $"but its paint read _GreyOut {painted:F2} of 1.00, i.e. the wash was " +
                          "missing or half gone. Restored to the settled end state with the game's " +
                          "OWN no-ramp arm, front visible. The wipe is the game's: " +
                          "FullAbilityCard.SetPile(Activated) calls RestoreCard() and never asks " +
                          "where the card is going. It is reached only from " +
                          "AbilityCardUI.UpdateCard() and only when the WIDGET's own cardPile " +
                          "actually changes (FullAbilityCard.cs:313-315) - not on every hand " +
                          "refresh, which is a correction of what this line used to claim — the user's " +
                          "'aber nur eine Runde - die runde darauf war die Karte wieder blau'. " +
                          "RULE, and every later round is judged against it: an ACTIVATED card " +
                          "wears the permanent burnt look IF AND ONLY IF it is bound for Lost, and " +
                          "keeps it for as long as it sits in the active area, on every board; a " +
                          "LOST card is always FULLY burnt, front visible; there is no third state.");
    }

    /// <summary>RULE 1b's first half, reported once: a DISCARD-bound activated card the game had
    /// latched burnt. Split out of <see cref="EnforceActivated"/> when that method gained its second
    /// half, so clearing a stray no longer returns before the grey the card is actually owed.</summary>
    private static void ReportActiveBurnStray(CAbilityCard card)
    {
        if (s_strayLogged)
            return;
        s_strayLogged = true;
        // HW-VERIFY (2026-09-07 item 4, the SECOND defect the corrected rule can name): an
        // activated card whose selected action is DISCARD-bound was latched BurnCard/LostMode.
        // Grep token: "ACTIVE BURN STRAY".
        //
        // EXPECTED READING: INERT. The user reported the burnt wash on "manche" activated cards
        // and reported it as CORRECT there; nothing in either ModBuild 476 log says a
        // discard-bound one ever wore it, because no log line carried a destination at all, and
        // both ModBuild 478 logs are inert on it as well.
        // If this line DOES appear, it is a defect of its own -- the game latching a burn on a
        // card it will discard -- and the lead is FullAbilityCard.TryPlayBurnAnimation, whose
        // BurnCard arm is gated on the ACTION's CardPile and not on the card's.
        VRLog.Note(Scope, $"ACTIVE BURN STRAY: '{Name(card)}' is ACTIVATED and DISCARD-bound " +
                          "(CCharacterClass.cs:479: its SelectedAction.CardPile is Discarded) " +
                          "yet the game had it latched BurnCard/LostMode - the permanent burnt " +
                          "wash belongs only to an activated card bound for LOST. Cleared with " +
                          "the game's own CardEffects.RestoreCard(), and the GREY the card IS " +
                          "owed is painted immediately afterwards by rule 1b's second half.");
    }

    /// <summary>RULE 2 — a lost card is fully burnt, whatever the game did with its own ramp.</summary>
    private static void EnforceLost(CardEffects fx, CAbilityCard card, int id, float now)
    {
        if (!BurnArtwork.Latched(fx))
            return;                       // the game has not claimed this card's look at all
        // A running timeline was already refused by Enforce — never cut across the game's own ramp.

        float painted = BurnArtwork.PaintProgress(fx);
        if (painted < 0f || painted >= BurnArtwork.FinishedGreyOut)
            return;                       // unreadable, or already finished

        if (!TakeBudget(fx, card, id, now, "RULE 2 (a lost card is fully burnt)"))
            return;
        if (!BurnArtwork.TrySettleBurnLook(fx))
            return;

        if (s_settleLogged)
            return;
        s_settleLogged = true;
        // HW-VERIFY (2026-09-07 item 8, "erst wieder blau dann wieder braun/ausgegraut aber kein
        // verbrennen effekt darauf festellen können"): the fraction of the burn ramp the game
        // actually painted before it abandoned the timeline. Grep token: "BURN RAMP ABANDONED".
        //
        // THIS IS THE FIELD THE ROUND WAS MISSING. BurnCardTimeline's animated arm runs a hard-coded
        // burnTime = 2 s (CardEffects.cs:511) and drives _GreyOut = Clamp01(dTime) (:571), so the
        // paint IS the ramp's progress. ModBuild 476's peer log 60504 reports the same burn as
        // "released by: ARTWORK END — the game's own BurnCardTimeline handle went null" after
        // 0,69s. A finished 2 s ramp cannot be 0.69 s long, so that arm names an END it cannot
        // observe: CardEffects.coroutine is ALSO nulled by RestoreCard() and by every
        // ToggleAdditiveEffect (:404-407, :469-472), and the short-rest flow calls both several
        // times on the same card (CardsHandUI.AnimateCardsLost:1029/:1066 → AbilityCardUI.UpdateCard
        // → FullAbilityCard.SetPile).
        //
        // PROOF: this line with a value well under 1.00 — that number IS "kein verbrennen effekt",
        // stated as a fraction. FALSIFIER: it never appears, which would mean every burn ramp on
        // this client finished on its own and the missing effect has some other cause (the card not
        // being DRAWN during the ramp is the next suspect, and it is a different lane's surface).
        // NOTE what this does NOT do: it does not give the ramp back. It guarantees the END STATE,
        // so the card is never handed to the flight half-painted or un-burnt.
        VRLog.Note(Scope, $"BURN RAMP ABANDONED: '{Name(card)}' is LOST and latched burnt, its " +
                          $"BurnCardTimeline handle is gone, and the paint stopped at _GreyOut " +
                          $"{painted:F2} of 1.00 — i.e. the game cancelled its own 2 s ramp at " +
                          $"{painted * 100f:F0}% and nothing was ever going to finish it. Settled to " +
                          "the full burnt end state with the game's OWN no-ramp arm " +
                          "(BurnCardTimeline(burnAnim: false)), front visible, so the card looks the " +
                          "same on every board and in every round. A handle going null is NOT an " +
                          "artwork ending — RestoreCard() and ToggleAdditiveEffect null it too — and " +
                          "this reading is what separates the two.");
    }

    // ----------------------------------------------------------------------- the instrument --

    /// <summary>
    /// THE OWNER'S OWN PICTURE OF AN ACTIVATED CARD, change-triggered, per card — and the near half
    /// of the 1:1 pair.
    ///
    /// <para>WHY THIS IS NOT A DUPLICATE OF <c>[Cards] ACTIVE SET</c>'s new destination column.
    /// That column is on the MODEL row and states a FACT: where each activated card is bound. This
    /// line states the PICTURE on this board against that fact — whether the card is actually
    /// wearing the wash, and how far its paint reads — which a model row is not entitled to carry.
    /// Fact and picture on one line is what makes it gradeable without cross-referencing a census
    /// that may be fifteen seconds away.</para>
    ///
    /// <para>AND IT IS DELIBERATELY THE SAME SHAPE AS <c>[Net] REMOTE ACTIVE WASH</c>, the mirror's
    /// half, so the owner's log and the peer's log diff literally for one card:
    /// <c>ACTIVE WASH … bound for Lost … wearing=True</c> here must pair with
    /// <c>REMOTE ACTIVE WASH … wash=True</c> there. Both sides read the SAME local model object
    /// through the SAME expression, so a disagreement is a LOCAL GATE and never a lost packet.</para>
    ///
    /// <para>THE BLIND SPOT THE PAIR CLOSES. Before this round <c>ACTIVE SET</c> printed the model
    /// list and each seat's list, so it could answer "do the surfaces agree" — and carried no
    /// destination and no wash reading, so NO LINE in either 71 MB ModBuild 476 log could say which
    /// activated cards should be marked or whether they were. That is why the first version of this
    /// rule shipped inverted and why it took a user correction rather than a grep to catch: the two
    /// activations those logs DO tie to a burn effect (<c>TheMindsWeakness</c>,
    /// <c>GnawingHorde</c>) cannot be classified after the fact from anything on disk.</para>
    ///
    /// <para>Change-triggered on (destination, wearing-a-burn-look) per card, so a card that sits
    /// active for ten rounds prints once — and prints again the moment either half moves, which is
    /// exactly the transition "nur eine Runde" describes.</para>
    /// </summary>
    private static void ReportDestination(CAbilityCard card, int id, CBaseCard.ECardPile dest,
                                          bool latched, float painted)
    {
        bool wearing = latched || painted > RestEpsilon;
        if (s_reportedDest.TryGetValue(id, out (int Card, CBaseCard.ECardPile Dest, bool Wearing) last)
            && last.Card == card.CardInstanceID && last.Dest == dest && last.Wearing == wearing)
            return;
        if (s_reportedDest.Count >= MaxReportedFaces)
            s_reportedDest.Clear();
        s_reportedDest[id] = (card.CardInstanceID, dest, wearing);
        // HW-VERIFY (2026-09-07 item 4): whether an ACTIVATED card is actually wearing the
        // permanent burnt wash on THIS board, beside where the game says it is bound. Grep token:
        // "ACTIVE WASH" — and the mirror's half is "REMOTE ACTIVE WASH", same shape, so one card's
        // two lines diff literally across the two logs.
        //
        // THE ONE GREP THAT ANSWERS ITEM 4 NEXT ROUND. A Lost-bound card reading wearing=False is
        // the reported defect ("die runde darauf war die Karte wieder blau"); a Discard-bound card
        // reading wearing=True is the second defect 'ACTIVE BURN STRAY' names. Both were
        // unanswerable in the ModBuild 476 logs.
        VRLog.Note(Scope, $"ACTIVE WASH: '{Name(card)}' is ACTIVATED and bound for {dest} " +
                          "(the game's own expression, CCharacterClass.cs:479: SelectedAction " +
                          "Discard-bound → Discarded, otherwise Lost — the same call the ACTIVE SET " +
                          "MODEL row and the peer's REMOTE ACTIVE WASH line both make), and this " +
                          $"board is wearing a burn look = {wearing} (latched={latched}, _GreyOut " +
                          $"{painted:F2}). THE RULE: bound for Lost ⇒ the permanent burnt wash " +
                          "belongs on it, every round, on every board; bound for Discarded ⇒ it " +
                          "never does. Change-triggered per card on exactly that pair.");
    }

    /// <summary>Spend one corrective write, or refuse and name the card once. See
    /// <see cref="MaxWritesInARow"/> for why this is a rate and not a lifetime count.</summary>
    private static bool TakeBudget(CardEffects fx, CAbilityCard card, int id, float now, string rule)
    {
        _ = fx;
        s_track.TryGetValue(id, out Track t);
        if (now - t.LastWrite > WriteWarQuietSeconds)
            t.Writes = 0;                 // a quiet card starts its run again
        if (t.Writes >= MaxWritesInARow)
        {
            s_track[id] = t;
            if (!s_budgetLogged)
            {
                s_budgetLogged = true;
                // HW-VERIFY (2026-09-07 item 4): a SECOND writer is undoing this policy on a named
                // card, within seconds rather than at a round boundary. Grep token:
                // "BURN LOOK BUDGET".
                //
                // A blocker line, not a statistic: it names the card and the rule that kept losing,
                // so the next round looks for the other writer instead of re-tuning this one.
                // INERT is the expected reading — the legitimate wipe is once per round and the
                // counter forgets after WriteWarQuietSeconds.
                VRLog.Note(Scope, $"BURN LOOK BUDGET: '{Name(card)}' took {MaxWritesInARow} " +
                                  $"corrective writes for {rule} inside " +
                                  $"{WriteWarQuietSeconds:F0}s each and the look came back every " +
                                  "time, so a SECOND writer owns this card's CardEffects and this " +
                                  "policy is standing down on it rather than fighting. The next " +
                                  "round's lead is that writer, not this budget. (A once-a-round " +
                                  "wipe can never reach here: the run resets after the quiet " +
                                  "period.)");
            }
            return false;
        }
        t.Writes++;
        t.LastWrite = now;
        s_track[id] = t;
        return true;
    }

    private static string Name(CAbilityCard card)
    {
        try { return card.Name ?? "(unnamed)"; }
        catch { return "(unnamed)"; }
    }
}
