using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Cards;

/// <summary>
/// WHAT A CARD'S BURN LOOK MUST BE — one rule, every surface, every round.
///
/// <para>USER ITEM 4 (2026-09-07, verbatim): <i>"Bei den aktiven Karten haben diese manchmal ein
/// verbrennen overlay nachdem ich sie aktiv gemacht habe - das verschwindet aber wieder in der
/// nächsten Runde. Ich will hier eine Konsitenz auch über Runden hinweg, lokal und remote!"</i></para>
///
/// <para>He is not saying the overlay is always wrong. He is saying it is INCONSISTENT — present on
/// one activation and not the next, gone again a round later, and different on his board from the
/// mirror. So the deliverable is a RULE, written down, that every surface and every round can be
/// judged against. This is it:</para>
///
/// <list type="number">
///   <item><description><b>An ACTIVATED card is never burnt.</b> While the card is in
///   <c>CCharacterClass.ActivatedCards</c> it wears the card's REST look — no <c>_GreyOut</c>, no
///   <c>_Flow</c>, no <c>_Dissolve</c>, no <c>_Burn</c>, no <c>fgFx</c> flame overlay, no smoke —
///   on the owner's own board, on every mirror, in the round it was activated and in every round
///   after it. An activated card is a card that is STILL IN PLAY; the burn belongs to the moment it
///   leaves.</description></item>
///   <item><description><b>A LOST / PERMANENTLY LOST card is always FULLY burnt</b>, front visible
///   (<i>"Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite sichtbar
///   sein"</i>), at the SETTLED end state <c>_GreyOut = 1</c> — either because the game's own ramp
///   finished it, or because this policy finished it with the game's own no-ramp arm when the game
///   abandoned the ramp half-way.</description></item>
///   <item><description><b>There is no third state and no per-round state.</b> A card may be
///   mid-ramp only while <c>CardEffects.coroutine</c> is genuinely running a timeline. Any other
///   partial paint is a leftover, and a leftover is exactly what "manchmal" looks like.</description></item>
///   <item><description><b>Cards in HAND, ROUND and DISCARDED are not this policy's business.</b>
///   The game's own play flourish (<c>FullAbilityCard.TryPlayBurnAnimation</c>) and its discard
///   ghost run there and the user has never complained about either. Rule 1 starts the instant the
///   model says <c>Activated</c>.</description></item>
/// </list>
///
/// <para>WHY THE GAME DOES NOT ALREADY DO THIS, AND THE ASSERTION THAT SAID IT DID.
/// <c>Net/Remote/RemoteBoardCard.cs:1121-1123</c> states, of the activation path: <i>"The
/// ActiveBonuses clause OVERRIDES the action's own pile, so the card goes to the ACTIVE area,
/// SetPile(Activated) restores it, and the owner's card is clean."</i> The ModBuild 476 host log
/// falsifies it on BOTH activations of the session, and it falsifies it by POSITION rather than by
/// state, which is the only way this class of claim can be checked:</para>
/// <list type="bullet">
///   <item><description><c>Player.log:83670</c> and <c>:83766</c> —
///   <c>Burn/ghost effect playing ON the dock card at world (-5.00, 12.06, 16.84)</c>, twice, i.e.
///   the effect went off and came back. <c>:83749</c> — <c>Card anim
///   'VRCard_ABILITY_CARD_TheMindsWeakness': fly-in from (-5.00,12.06,16.84)</c> to the ACTIVE
///   column. Same coordinate, to the centimetre: the card that flew into the active column was
///   wearing the game's burn/ghost artwork as it went.</description></item>
///   <item><description><c>Player.log:123923</c> — the same line at
///   <c>(-6.60, 11.38, 14.46)</c>; <c>:123973</c> — <c>'VRCard_ABILITY_CARD_GnawingHorde': fly-in
///   from (-6.60,11.38,14.46)</c>, <c>:123974</c> <c>ACTIVE FLIGHT … went ACTIVE</c>.</description></item>
/// </list>
/// <para>Two activations, two hits, no misses. The mechanism is an ORDER problem, not a missing
/// call: <c>FullAbilityCard.SetPile</c> is the only thing that ever calls <c>RestoreCard()</c> for
/// an activated card, it early-outs on <c>cardPile != newCardPile</c>, and it is reached ONLY from
/// <c>AbilityCardUI.UpdateCard()</c> — i.e. only when the game next refreshes its own 2D hand view.
/// Between the model's activation and that refresh the widget keeps whatever
/// <c>TryPlayBurnAnimation</c> left on it. The refresh happens at the next card selection, which is
/// the user's <i>"das verschwindet aber wieder in der nächsten Runde"</i>, word for word.</para>
///
/// <para>AND THAT IS ALSO THE WHOLE OF "lokal und remote". The mirror does NOT show the overlay:
/// a mod-built front has its <c>CardEffects</c> stripped and
/// <see cref="CardHalfTone"/> puts its materials back at <c>RestoreCard()</c>'s rest values before
/// the first drawn frame. So the peer has been drawing rule 1 all along and the owner has not —
/// one fact, two surfaces, and the surface that was wrong is the owner's. No wire field is owed
/// (the model is local: <c>ActivatedCards</c> is on every client) and none is added.</para>
///
/// <para>THIS WRITES PRESENTATION ONLY, THROUGH THE GAME'S OWN CALLS. Rule 1 is enforced with
/// <c>CardEffects.RestoreCard()</c> — the exact call <c>SetPile(Activated)</c> makes
/// (FullAbilityCard.cs:325-328), so this is the game's own transition executed at the model's
/// instant instead of at the next hand refresh. Rule 2 is enforced with
/// <see cref="BurnArtwork.TrySettleBurnLook"/>, which drains
/// <c>BurnCardTimeline(burnAnim: false, …)</c>. Neither touches game state, both are undone in full
/// by the game's own <c>RestoreCard()</c> if a card is ever recovered, and both are BUDGETED
/// (<see cref="MaxWritesPerCard"/>) so that a second writer shows up in the log as a named blocker
/// instead of as a per-frame write war.</para>
/// </summary>
internal static class BurnLookPolicy
{
    private const string Scope = "Cards";

    /// <summary>
    /// How many corrective writes one <c>CardEffects</c> may take before this policy stops and says
    /// so. A correct card takes ONE: the guard below closes the moment the look matches the rule.
    /// A card that keeps coming back has a second writer, and the project's standing ruling is not
    /// to win a write war but to name it — so the budget exists to turn "the fix does nothing" into
    /// a log line that says WHICH card and WHICH rule kept losing.
    /// </summary>
    private const int MaxWritesPerCard = 4;

    /// <summary>Corrective writes spent per <c>CardEffects</c> instance id. Pruned by
    /// <see cref="Forget"/> when a card's face is dropped, so it cannot grow across a scenario.</summary>
    private static readonly Dictionary<int, int> s_spent = new(16);

    /// <summary>
    /// The model pile for which each <c>CardEffects</c>'s look was last found to MATCH the rule.
    ///
    /// <para>THE STEADY-STATE GATE, and it is here for cost. Reading the paint means
    /// <c>Material.GetFloat</c> over up to ten face images; doing that for every lost card on every
    /// frame of a burnt-pile browse is exactly the kind of per-frame sweep this project keeps
    /// finding at the top of its spike lists. A verified card costs one dictionary lookup and one
    /// enum compare instead. The entry is dropped again the moment the card's pile changes or the
    /// game starts a timeline on it, which are the only two things that can make the look wrong
    /// again.</para>
    /// </summary>
    private static readonly Dictionary<int, (int Card, CBaseCard.ECardPile Pile)> s_verified = new(16);

    private static bool s_activeLogged;
    private static bool s_settleLogged;
    private static bool s_budgetLogged;

    /// <summary>
    /// Bring one adopted card face's burn look in line with the rule above. Called every frame from
    /// <see cref="BurnCardFx.Tick"/>, which is the one per-card tick that already holds the adopted
    /// <c>FullAbilityCard</c>. Costs two enum reads and one float compare when the card is correct,
    /// which it is on every frame but the one that fixes it.
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
            // The CARD half of the verdict key. A CardEffects belongs to a POOLED widget, so its
            // instance id alone would carry a verdict from a previous card into the next borrow of
            // that widget — "the set that shrank", the other way round.
            cardId = card != null ? card.CardInstanceID : 0;
            running = fx.coroutine != null;
        }
        catch { return; }
        if (card == null)
            return;

        int id = fx.GetInstanceID();
        if (running)
        {
            // A real timeline owns the card. Never cut across it — and forget any earlier verdict,
            // because what it leaves behind is precisely what the rule has to be re-checked against.
            s_verified.Remove(id);
            return;
        }
        if (s_verified.TryGetValue(id, out (int Card, CBaseCard.ECardPile Pile) verified)
            && verified.Card == cardId && verified.Pile == pile)
            return;

        if (IsActivated(full, card))
            EnforceActivated(full, fx, card);
        else if (IsLost(card))
            EnforceLost(full, fx, card);
        s_verified[id] = (cardId, pile);
    }

    /// <summary>Drop a face's budget when its VR card lets go of it (teardown / re-adoption). Keeps
    /// <see cref="s_spent"/> the size of the live card population rather than of the session.</summary>
    internal static void Forget(FullAbilityCard? full)
    {
        CardEffects? fx = BurnArtwork.EffectsOf(full);
        if (fx == null)
            return;
        int id = fx.GetInstanceID();
        s_spent.Remove(id);
        s_verified.Remove(id);
    }

    /// <summary>
    /// TRUE while this card is in the ACTIVE area, asked of the MODEL and of nothing else.
    ///
    /// <para><see cref="ActiveCardSet.IsActive(CPlayerActor, CBaseCard)"/> is the project's
    /// designated authority and this is its FIRST caller — <c>ActiveCardSet</c>'s own doc says
    /// <i>"Sibling lanes that need 'is this card currently active' — the burn-presentation trigger
    /// (activated-but-not-yet-lost must not look or sound burnt) … call IsActive(…)"</i>, and until
    /// this file nobody did. A sentence describing a consumer that does not exist is the class of
    /// false comment this project keeps finding; it is now true.</para>
    ///
    /// <para>The card's own <c>CurrentCardPile</c> stamp is the FALLBACK, for the case where the
    /// widget has no actor to ask. It is trustworthy for exactly this transition and no other:
    /// <c>CCharacterClass.MoveAbilityCardToPile</c> (:452) — the end-of-turn drain that produces an
    /// activation — is one of the few places the field is written at all, which is the finding
    /// <c>RemoteBoardCard.cs:1140-1155</c> records at length. It is NOT consulted for anything
    /// else here.</para>
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

    /// <summary>RULE 1 — an activated card is never burnt.</summary>
    private static void EnforceActivated(FullAbilityCard full, CardEffects fx, CAbilityCard card)
    {
        float painted = BurnArtwork.PaintProgress(fx);
        bool wrong = BurnArtwork.Latched(fx) || painted > RestEpsilon;
        if (!wrong)
            return;
        if (!TakeBudget(fx, full, card, "RULE 1 (an activated card is never burnt)"))
            return;

        try { fx.RestoreCard(); }
        catch { return; }

        if (s_activeLogged)
            return;
        s_activeLogged = true;
        // HW-VERIFY (2026-09-07 item 4, "Bei den aktiven Karten haben diese manchmal ein verbrennen
        // overlay nachdem ich sie aktiv gemacht habe"): the ACTIVATED card that was wearing a burn
        // look, and how far the paint had got. Grep token: "ACTIVE BURN LOOK".
        //
        // PROOF the rule is doing its job: this line appears at all, with painted > 0 — the ModBuild
        // 476 host log has the picture-side evidence (:83670/:83749 and :123923/:123973, the burn FX
        // line and the fly-in line at the SAME world coordinate) but no reading of the card itself.
        // FALSIFIER: this line never appears across a session with activations in it, which would
        // mean the two coordinate pairs above had some other cause and rule 1 was never breached.
        // SECOND FALSIFIER: 'BURN LOOK BUDGET' below appearing for the same card, which would mean
        // the restore is being undone by a writer this policy has not found.
        VRLog.Note(Scope, $"ACTIVE BURN LOOK: '{Name(card)}' was ACTIVATED and still wearing the " +
                          $"game's burn artwork (latched={BurnArtwork.Latched(fx)}, _GreyOut " +
                          $"{painted:F2} of 1.00) — restored to the card's rest look with the game's " +
                          "own CardEffects.RestoreCard(), which is the exact call " +
                          "FullAbilityCard.SetPile(Activated) makes and which had not yet run " +
                          "because SetPile is reached only from AbilityCardUI.UpdateCard(), i.e. only " +
                          "when the game next refreshes its 2D hand view — the next round, which is " +
                          "the user's own 'das verschwindet aber wieder in der nächsten Runde'. " +
                          "RULE, and every later round is judged against it: an ACTIVATED card is " +
                          "never burnt, on any board; a LOST card is always FULLY burnt, front " +
                          "visible; there is no third state. The peer's mirror already drew this " +
                          "(CardHalfTone rests a mod-built front's materials), so this is the owner's " +
                          "board catching up with the mirror, not the other way round.");
    }

    /// <summary>RULE 2 — a lost card is fully burnt, whatever the game did with its own ramp.</summary>
    private static void EnforceLost(FullAbilityCard full, CardEffects fx, CAbilityCard card)
    {
        if (!BurnArtwork.Latched(fx))
            return;                       // the game has not claimed this card's look at all
        // A running timeline was already refused by Enforce — never cut across the game's own ramp.

        float painted = BurnArtwork.PaintProgress(fx);
        if (painted < 0f || painted >= BurnArtwork.FinishedGreyOut)
            return;                       // unreadable, or already finished

        if (!TakeBudget(fx, full, card, "RULE 2 (a lost card is fully burnt)"))
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

    /// <summary>How far from <c>RestoreCard()</c>'s zero a paint may sit before it counts as a burn
    /// look. Well below anything the eye resolves, well above float noise — the same threshold
    /// <see cref="CardHalfTone"/> uses on the same four terms, for the same reason.</summary>
    private const float RestEpsilon = 0.002f;

    /// <summary>Spend one corrective write, or refuse and name the card once.</summary>
    private static bool TakeBudget(CardEffects fx, FullAbilityCard full, CAbilityCard card, string rule)
    {
        int id = fx.GetInstanceID();
        s_spent.TryGetValue(id, out int spent);
        if (spent >= MaxWritesPerCard)
        {
            if (!s_budgetLogged)
            {
                s_budgetLogged = true;
                // HW-VERIFY (2026-09-07 item 4): a SECOND writer is undoing this policy on a named
                // card. Grep token: "BURN LOOK BUDGET".
                //
                // This is a blocker line, not a statistic: it names the card and the rule that kept
                // losing, so the next round looks for the other writer instead of re-tuning this
                // one. INERT is the expected reading — the guards above close after one write.
                VRLog.Note(Scope, $"BURN LOOK BUDGET: '{Name(card)}' has taken {MaxWritesPerCard} " +
                                  $"corrective writes for {rule} and the look came back every time, " +
                                  "so a SECOND writer owns this card's CardEffects and this policy " +
                                  "is now standing down on it rather than fighting per frame. The " +
                                  "next round's lead is that writer, not this budget.");
            }
            return false;
        }
        s_spent[id] = spent + 1;
        _ = full; // the face is the caller's anchor; nothing here needs it beyond the log's identity
        return true;
    }

    private static string Name(CAbilityCard card)
    {
        try { return card.Name ?? "(unnamed)"; }
        catch { return "(unnamed)"; }
    }
}
