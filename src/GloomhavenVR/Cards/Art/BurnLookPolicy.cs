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
///   look.</b> Its own <c>DiscardMode</c> ghost is the game's business and is left alone (see
///   <see cref="EnforceActivated"/> for why the trigger is the LATCH and never the paint: the ghost
///   timeline writes <c>_GreyOut</c> too, CardEffects.cs:686). A Discard-bound activated card found
///   LATCHED <c>BurnCard</c>/<c>LostMode</c> is a SECOND, separate defect and is logged as
///   one.</description></item>
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
///   its discard ghost run there and the user has never complained about either.</description></item>
/// </list>
///
/// <para>WHO WIPES IT, AND WHY IT IS "nur eine Runde". <c>FullAbilityCard.SetPile</c> is reached
/// only from <c>AbilityCardUI.UpdateCard()</c>, i.e. only when the game refreshes its own 2D hand
/// view — the next card selection. For <c>ECardPile.Activated</c> that branch calls
/// <c>cardEffects.RestoreCard()</c> (FullAbilityCard.cs:325-328), which zeroes
/// <c>_GreyOut/_Flow/_Dissolve/_Burn</c> unconditionally. It does not ask where the card is going.
/// So a Lost-bound activated card is painted at play time and un-painted one round later, which is
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
/// closed by <see cref="ReportDestination"/>, which prints the game's own expression per activated
/// card, change-triggered.</para>
///
/// <para>THE MIRROR IS NOT YET DOING THIS, and this file cannot make it. A mod-built front has its
/// <c>CardEffects</c> stripped (<c>Net.RemoteCardArt.StripFragileEffects</c>) and
/// <see cref="CardHalfTone"/> resets its FX materials to <c>RestoreCard()</c>'s rest values before
/// the first drawn frame — so a peer's mirrored active card is drawn FRESH, which under rule 1a is
/// now WRONG for a Lost-bound one. The producer that would fix it lives in
/// <c>Net/Remote/RemoteActiveCards.cs</c> (<c>RemoteCardArt.SetAbilityBurnProgress(1f)</c>, the
/// same call <c>RemoteBurnFx</c> already makes on a burnt slab) and is another lane's file. What
/// this lane CAN ship is the half that stops the two writers fighting:
/// <see cref="CardHalfTone.HoldBurntLook"/>, the exact shape of the existing
/// <c>HoldMirroredDim</c> hold, so the mirror can announce the faces it is deliberately painting
/// burnt and <c>NormalizeCardFx</c> stands down for exactly those.</para>
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
    private static bool s_budgetLogged;

    /// <summary>
    /// Bring one adopted card face's burn look in line with the rules above. Called every frame from
    /// <see cref="BurnCardFx.Tick"/>, which is the one per-card tick that already holds the adopted
    /// <c>FullAbilityCard</c>. Costs one dictionary lookup and one float compare on the frames
    /// between checks.
    /// </summary>
    internal static void Enforce(FullAbilityCard? full)
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
            card = full.AbilityCard;
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

        if (IsActivated(full, card))
            EnforceActivated(fx, card, id, now);
        else if (IsLost(card))
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
    private static bool IsActivated(FullAbilityCard full, CAbilityCard card)
    {
        try
        {
            CPlayerActor? owner = CardsGameApi.CardOwner(full);
            if (owner != null)
                return ActiveCardSet.IsActive(owner, card);
            return card.CurrentCardPile == CBaseCard.ECardPile.Activated;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLost(CAbilityCard card)
    {
        try
        {
            return card.CurrentCardPile == CBaseCard.ECardPile.Lost
                   || card.CurrentCardPile == CBaseCard.ECardPile.PermanentlyLost;
        }
        catch
        {
            return false;
        }
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
    private static CBaseCard.ECardPile Destination(CAbilityCard card)
    {
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
            // RULE 1b. THE TRIGGER IS THE LATCH AND NEVER THE PAINT, and that distinction is
            // load-bearing: CardEffects.GhostOutOnTimeline drives the SAME _GreyOut property
            // (CardEffects.cs:686), so a paint-based test would erase the discard ghost the game
            // is entitled to draw on a Discard-bound activated card. HasEffect names the task.
            if (!latched)
                return;
            if (!TakeBudget(fx, card, id, now, "RULE 1b (a discard-bound activated card is not burnt)"))
                return;
            try { fx.RestoreCard(); }
            catch { return; }
            if (s_strayLogged)
                return;
            s_strayLogged = true;
            // HW-VERIFY (2026-09-07 item 4, the SECOND defect the corrected rule can name): an
            // activated card whose selected action is DISCARD-bound was latched BurnCard/LostMode.
            // Grep token: "ACTIVE BURN STRAY".
            //
            // EXPECTED READING: INERT. The user reported the burnt wash on "manche" activated cards
            // and reported it as CORRECT there; nothing in either ModBuild 476 log says a
            // discard-bound one ever wore it, because no log line carried a destination at all.
            // If this line DOES appear, it is a defect of its own — the game latching a burn on a
            // card it will discard — and the lead is FullAbilityCard.TryPlayBurnAnimation, whose
            // BurnCard arm is gated on the ACTION's CardPile and not on the card's.
            VRLog.Note(Scope, $"ACTIVE BURN STRAY: '{Name(card)}' is ACTIVATED and DISCARD-bound " +
                              "(CCharacterClass.cs:479: its SelectedAction.CardPile is Discarded) " +
                              "yet the game had it latched BurnCard/LostMode — the permanent burnt " +
                              "wash belongs only to an activated card bound for LOST. Cleared with " +
                              "the game's own CardEffects.RestoreCard(). Its DiscardMode ghost, if " +
                              "any, is untouched: that trigger is the LATCH and never the paint, " +
                              "because GhostOutOnTimeline writes the same _GreyOut.");
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
                          "FullAbilityCard.SetPile(Activated) calls RestoreCard() unconditionally " +
                          "and never asks where the card is going, and it is reached only from " +
                          "AbilityCardUI.UpdateCard(), i.e. at the next hand refresh — the user's " +
                          "'aber nur eine Runde - die runde darauf war die Karte wieder blau'. " +
                          "RULE, and every later round is judged against it: an ACTIVATED card " +
                          "wears the permanent burnt look IF AND ONLY IF it is bound for Lost, and " +
                          "keeps it for as long as it sits in the active area, on every board; a " +
                          "LOST card is always FULLY burnt, front visible; there is no third state.");
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
    /// NAME EVERY ACTIVATED CARD'S DESTINATION, change-triggered, per card.
    ///
    /// <para>THE BLIND SPOT THIS CLOSES. <c>[Cards] ACTIVE SET</c> prints the model list and each
    /// seat's list, so it can answer "do the surfaces agree" — but it carries no destination, and
    /// therefore NO LINE in either 71 MB ModBuild 476 log can say which activated cards should be
    /// marked. That is why the first version of this rule was inverted and why it took a user
    /// correction rather than a grep to catch: the two activations the logs DO tie to a burn effect
    /// (<c>TheMindsWeakness</c>, <c>GnawingHorde</c>) cannot be classified after the fact from
    /// anything on disk. One line per activation closes it.</para>
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
        // HW-VERIFY (2026-09-07 item 4): WHICH activated cards are bound for Lost, and whether each
        // is actually wearing the permanent burnt wash. Grep token: "ACTIVE DESTINATION".
        //
        // THE ONE GREP THAT ANSWERS ITEM 4 NEXT ROUND. A Lost-bound card reading wearing=False is
        // the reported defect; a Discard-bound card reading wearing=True is the second defect
        // 'ACTIVE BURN STRAY' names. Both were unanswerable in the ModBuild 476 logs.
        VRLog.Note(Scope, $"ACTIVE DESTINATION: '{Name(card)}' is ACTIVATED and bound for {dest} " +
                          "(the game's own expression, CCharacterClass.cs:479: SelectedAction " +
                          "Discard-bound → Discarded, otherwise Lost), and it is currently " +
                          $"wearing a burn look = {wearing} (latched={latched}, _GreyOut " +
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
