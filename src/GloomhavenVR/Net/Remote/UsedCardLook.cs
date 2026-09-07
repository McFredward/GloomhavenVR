// ═══════════════════════════════════════════════════════════════════════════════════════════════
//  ONE EXPRESSION FOR "WHAT LOOK IS THE OWNER'S CARD WEARING", READ OFF THEIR OWN WIDGET
//
//  WHY THIS FILE EXISTS. The game paints a used ability card with one of two timelines —
//  CardEffects.BurnCardTimeline (the warm char) and GhostOutOnTimeline (the cold grey-out) — and
//  FullAbilityCard.TryPlayBurnAnimation is what chooses between them. A MIRROR cannot run either:
//  RemoteCardArt DestroyImmediates CardEffects off every clone on purpose (its screen-space
//  _PosAndBounds material against a world-space canvas is the "card renders DEEP BLACK" hazard), so
//  every mirrored surface drives RemoteCardArt.SetAbilityCardFxProgress instead — the game's own
//  numbers on materials this mod mints.
//
//  WHAT WAS DUPLICATED. Three surfaces already ask the owner's LIVE widget the same question, and
//  by 2026-09-07 the expression existed twice and a boolean collapse of it a third time:
//    * Net.RemoteBoardCard.ResolveUsedCardLook step 1 — the recess, and the one that spells it in
//      full (Burn for FXTask.BurnCard, Ghost for DiscardMode or LostMode, else None);
//    * Net.RemoteHandFan.TickMirroredPlumes — the same three HasEffect calls OR-ed into one bool,
//      because a plume only needs "is something running";
//    * and the hand fan's own FACE, which asked NOTHING AT ALL until this build — see below.
//  A fourth hand-written copy is exactly what the sharing ruling forbids, so the expression lives
//  here and its callers read it.
//
//  THE DEFECT THAT PROMPTED IT (2026-09-07, full 1:1 re-audit). `grep -n "SetAbility"
//  Net/Remote/RemoteHandFan.cs` returned ZERO hits. The owner's hand card is an adopted LIVE
//  FullAbilityCard, so the game's own BurnCardTimeline chars its whole face; the mirror's slab wears
//  a CLONE with CardEffects destroyed and nothing driving the replacement rig. The only response the
//  fan had was TickMirroredPlumes, which is gated on the owner's [Cards] GameCardParticles bit —
//  and that dial SHIPS FALSE. So on the exact flow this round's E1 is about — a long rest, the game
//  re-Shows the hand over the DISCARD arc, the owner picks a card to burn — their screen charred the
//  card and every peer kept showing the same bright, fresh face.
//
//  AND THE POLICY FILE IS NOT WHERE TO FIX IT. Cards.Art.BurnLookPolicy deliberately excludes the
//  HAND / ROUND / DISCARDED piles from its own rule, on the argument that the GAME paints those. That
//  argument is TRUE for an adopted widget on the owner's board and IMPOSSIBLE for a clone on a
//  mirror: the exclusion is correct where it is written and wrong on every mirrored surface, which is
//  why the remedy is a driver here rather than a widening there.
//
//  NO WIRE FIELD. The trigger is the peer's own live AbilityCardUI.fullAbilityCard — the very widget
//  the mirrored face was cloned from — so the owner's picture and the mirror's agree because they are
//  the same expression over the same object, not because two formulas were made to match. Its known
//  limit is stated where it is read: whether a peer's hidden CardsHandUI widget actually runs its
//  CardEffects coroutine on THIS client is not established, and if it does not, the failure is "no
//  wash" — today's picture — never a wash on the wrong card.
//
//  ═══ ModBuild 479: WHAT THIS FILE ANSWERS IS A LATCH, AND THE GAME DROPS IT EVERY ROUND ═══
//
//  THE PREMISE THAT WAS PUT TO THIS LANE — "CardEffects flags are TRANSIENT; burnTime is a
//  hard-coded 2f, so two seconds after a card is used every HasEffect goes false" — IS FALSE, and
//  the game says so in three lines: `private HashSet<FXTask> toggledEffects` (CardEffects.cs:229)
//  and `HasEffect(e) => toggledEffects.Contains(e)` (:354-357), against writers that only ever ADD
//  on the path a played card takes. The 2 s is the ANIMATION's duration and has nothing to do with
//  the flag. FromWidget therefore does not "stop being able to see the past" on a timer.
//
//  CORRECTION (2026-09-07, re-read in the decompile). The sentence above used to name "the only two
//  writers that ever REMOVE from it, ToggleEffect(active:false) (:432) and RestoreCard() (:468)",
//  and BOTH halves of that were wrong in a way that matters:
//    * :432 is inside ToggleAdditiveEffect, not ToggleEffect (ToggleEffect is :359-403,
//      ToggleAdditiveEffect is :404-443);
//    * and that Remove is DEAD on the public entry point, because ToggleEffect calls RestoreCard()
//      FIRST, ALWAYS (:362), which does toggledEffects.Clear() (:468). By the time :432 runs the
//      set is already empty.
//  So `toggledEffects` is a SINGLE-SLOT register, not a set: every ToggleEffect wipes it and adds
//  exactly one task. That is why FromWidget may test the three tasks in any order it likes — at
//  most one of them can be standing — and it is also why the "burn is asked first because a card
//  can be mid-burn while its discard flag is still standing" argument below is stated as an
//  ORDERING PREFERENCE and never relied on.
//
//  WHAT IT DOES DO IS FOLLOW THE GAME'S OWN ERASURE. FullAbilityCard.SetPile(Hand | Activated)
//  calls cardEffects.RestoreCard() (:325-328) and is reached from AbilityCardUI.UpdateCard()
//  (AbilityCardUI.cs:768-775), i.e. at the next hand refresh — one round later. So this expression
//  is a faithful report of a latch the game itself clears, and every surface that asks ONLY this
//  question draws a spent card clean from the round boundary onward. That is not a bug in FromWidget;
//  it is the reason a DURABLE question had to exist beside it.
//
//  CORRECTION (2026-09-07): THAT CALL IS NOT UNCONDITIONAL, and this file used to say it was. All
//  three FX arms of SetPile sit inside `if (cardPile != newCardPile && cardEffects != null)`
//  (FullAbilityCard.cs:313-315). RestoreCard() fires only on a CHANGE into Hand or Activated,
//  measured against the value the WIDGET last received — and UpdateCard() re-passes the same
//  cardType (:770-773), so on an unchanged card SetPile is a complete no-op on the FX layer. The
//  erasure is real and the remedies built on it are still owed; its TRIGGER is a widget-local edge,
//  not a round boundary. AbilityCardUI.ToggleHighlight writes cardType behind SetPile's back
//  (AbilityCardUI.cs:1096, :1186), which is one way that edge is manufactured without the model
//  moving at all.
//
//  THE DURABLE QUESTION IS Cards.BurnLookPolicy.ForCard / ForActivatedCard, and it lives in Cards/
//  because CALLS GO DOWN: Net/ may read Cards/, and Cards/ must never learn that a mirror exists.
//  FromPolicy below is the ONE place the two enums meet. Use FromWidget where the LIVE ramp is what
//  is wanted (the recess and the hand fan, which mirror the owner's 2 s animation as it plays) and
//  the policy where a SETTLED look is wanted (the piles, the active cells, the held card), which is
//  every surface a viewer can open long after the animation ran.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// <c>FullAbilityCard.TryPlayBurnAnimation</c>'s verdict for a card, read off the OWNER's own live
/// widget — the one term every mirrored card-FX surface shares.
/// </summary>
internal static class UsedCardLook
{
    /// <summary>
    /// How long the game's two card-FX timelines take, and therefore how long every mirrored ramp
    /// takes. <c>CardEffects</c> drives both over two seconds; a mirror that ramped faster or slower
    /// would be a 1:1 breach in TIMING, which the standing ruling counts exactly as it counts a
    /// wrong colour.
    /// </summary>
    internal const float RampSeconds = 2f;

    /// <summary>
    /// The mirror's name for <see cref="Cards.BurnLookPolicy.Look"/> — the ONE place the durable
    /// answer crosses from <c>Cards/</c> into <c>Net/</c>.
    ///
    /// <para>TWO ENUMS AND NOT ONE, DELIBERATELY. The sharing ruling's direction of dependency is
    /// that <c>Net/</c> calls DOWN into <c>Cards/</c> and code never goes up, so the policy cannot
    /// name <see cref="RemoteCardArt.CardFxLook"/> — that type is the mirror's rig, and a
    /// <c>Cards/</c> file referencing it would be the owner's board learning that a mirror exists.
    /// The cost is this three-line map; the alternative was a fourth hand-written copy of the
    /// look decision on every mirrored surface, which is what this file was created to stop.</para>
    /// </summary>
    internal static RemoteCardArt.CardFxLook FromPolicy(Cards.BurnLookPolicy.Look look) => look switch
    {
        Cards.BurnLookPolicy.Look.Burn => RemoteCardArt.CardFxLook.Burn,
        Cards.BurnLookPolicy.Look.Ghost => RemoteCardArt.CardFxLook.Ghost,
        _ => RemoteCardArt.CardFxLook.None,
    };

    /// <summary>
    /// THE DURABLE LOOK for a card drawn on a mirror, in the mirror's own enum — the settled
    /// answer <see cref="FromWidget"/> cannot give once the game has cleared its latch.
    ///
    /// <para>One call, so that a mirrored surface needing a settled look has nothing to write by
    /// hand. See <see cref="Cards.BurnLookPolicy.ForCard(CAbilityCard?)"/> for the term and for its
    /// blind spots — a card a rest handed back to the hand still reads <c>Discarded</c>, which every
    /// caller whose population can contain a hand card must answer before asking.</para>
    ///
    /// <para>PASS THE OWNER WHEREVER YOU HAVE ONE. The other blind spot — a card burnt to NEGATE
    /// DAMAGE, which <c>GameState.Lose1HandCardToAvoidAttack</c> moves into <c>LostAbilityCards</c>
    /// without ever writing <c>CurrentCardPile</c> — is answered only by the overload that names the
    /// owner, because the answer is list membership and a bare <c>CAbilityCard</c> cannot name a
    /// list. Without it a mirrored surface draws that card PRISTINE while its owner sees it charred,
    /// against <i>"Beim Verbrennen EGAL AUS WELCHEM GRUND muss die Karte immer mit der Vorderseite
    /// sichtbar sein"</i>.</para>
    /// </summary>
    internal static RemoteCardArt.CardFxLook FromState(CAbilityCard? card)
        => FromPolicy(Cards.BurnLookPolicy.ForCard(card));

    /// <summary><see cref="FromState(CAbilityCard?)"/> with the card's OWNER named — the form every
    /// mirrored surface that can resolve the peer's actor should call. A null owner degrades to the
    /// bare form exactly, never to a worse answer.</summary>
    internal static RemoteCardArt.CardFxLook FromState(CPlayerActor? owner, CAbilityCard? card)
        => FromPolicy(Cards.BurnLookPolicy.ForCard(owner, card));

    /// <summary>
    /// Which of the game's two card-FX looks <paramref name="full"/> is running right now, or
    /// <see cref="RemoteCardArt.CardFxLook.None"/> for a card nobody has touched.
    ///
    /// <para>THE GAME'S OWN TASK FLAGS AND NOTHING ELSE, AND THE COLLAPSE USED TO BE ON THE WRONG
    /// SIDE. <c>CardEffects.ToggleAdditiveEffect</c>'s switch is three arms and the odd one out is
    /// <c>DiscardMode</c>, not <c>LostMode</c> — read out of the decompile on 2026-09-07:
    /// <code>
    /// 419:  case FXTask.DiscardMode: GhostOutOn(ghostAnim: true);              break;   // the GREY
    /// 422:  case FXTask.LostMode:    BurnCard(burnAnim: true, playOnDisabled); break;   // the CHAR
    /// 425:  case FXTask.BurnCard:    BurnCard(burnAnim: true, playOnDisabled); break;   // the CHAR
    /// </code>
    /// <c>LostMode</c> and <c>BurnCard</c> are the SAME CALL. Until ModBuild 480 this method mapped
    /// <c>LostMode</c> to <see cref="RemoteCardArt.CardFxLook.Ghost"/> and the doc block that stood
    /// here asserted <i>"DiscardMode and LostMode both run GhostOutOnTimeline (CardEffects.cs:422-423)"</i>
    /// while citing the two lines that refute it — 422-423 IS the <c>LostMode → BurnCard</c> arm.</para>
    ///
    /// <para>WHY IT WAS THE ORDINARY BURN AND NOT A CORNER. <c>FullAbilityCard.SetPile</c> raises
    /// <c>LostMode</c> — never <c>BurnCard</c> — for every card the model routes into a burnt pile
    /// (FullAbilityCard.cs:321-323, the <c>Lost || PermanentlyLost</c> arm). <c>BurnCard</c> is
    /// raised in exactly two places: the short-rest sacrifice (CardsHandUI.cs:955) and
    /// <c>TryPlayBurnAnimation</c>'s already-resolved arm (FullAbilityCard.cs:587-589). So a peer
    /// burning a card that reached the pile through <c>SetPile</c> — a "lost" symbol played, a card
    /// burnt to negate damage — charred warm brown-black on his own headset and washed cold blue-grey
    /// in his mirrored hand fan on every other one. Same card, same instant, two different burns:
    /// the 1:1 ruling counts a wrong ANIMATION exactly as it counts a wrong colour.</para>
    ///
    /// <para>Burn is asked first as an ordering preference only. <c>toggledEffects</c> is cleared by
    /// <c>ToggleEffect</c>'s own <c>RestoreCard()</c> on every entry (CardEffects.cs:362, :468), so
    /// at most one of the three flags can be standing and no order can change the answer.</para>
    ///
    /// <para>NEVER THROWS, and a null widget or a stripped <c>CardEffects</c> answers
    /// <see cref="RemoteCardArt.CardFxLook.None"/> — the clean card, which is the safe direction for
    /// every caller: a missing wash is a small divergence, a wash on a card the owner has not used
    /// is a lie about their board.</para>
    /// </summary>
    internal static RemoteCardArt.CardFxLook FromWidget(FullAbilityCard? full)
    {
        try
        {
            CardEffects? fx = full != null ? full.cardEffects : null;
            if (fx == null)
                return RemoteCardArt.CardFxLook.None;
            // LostMode BELONGS ON THIS SIDE. CardEffects.cs:422-423 runs BurnCard(burnAnim: true)
            // for it, character for character the same call as the FXTask.BurnCard arm at :425-426;
            // only DiscardMode reaches GhostOutOn (:419-420).
            if (fx.HasEffect(CardEffects.FXTask.BurnCard)
                || fx.HasEffect(CardEffects.FXTask.LostMode))
                return RemoteCardArt.CardFxLook.Burn;
            if (fx.HasEffect(CardEffects.FXTask.DiscardMode))
                return RemoteCardArt.CardFxLook.Ghost;
            return RemoteCardArt.CardFxLook.None;
        }
        catch (System.Exception)
        {
            return RemoteCardArt.CardFxLook.None;
        }
    }
}
