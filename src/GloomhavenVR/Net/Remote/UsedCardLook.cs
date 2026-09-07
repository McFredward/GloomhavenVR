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
//  the game says so in three lines: `private HashSet<FXTask> toggledEffects` (CardEffects.cs:229),
//  `HasEffect(e) => toggledEffects.Contains(e)` (:354-357), and the only two writers that ever
//  REMOVE from it, ToggleEffect(active:false) (:432) and RestoreCard() (:468). The 2 s is the
//  ANIMATION's duration and has nothing to do with the flag. FromWidget therefore does not "stop
//  being able to see the past" on a timer.
//
//  WHAT IT DOES DO IS FOLLOW THE GAME'S OWN ERASURE. FullAbilityCard.SetPile(Hand | Activated)
//  calls cardEffects.RestoreCard() unconditionally (:325-328) and is reached from
//  AbilityCardUI.UpdateCard(), i.e. at the next hand refresh — one round later. So this expression
//  is a faithful report of a latch the game itself clears, and every surface that asks ONLY this
//  question draws a spent card clean from the round boundary onward. That is not a bug in FromWidget;
//  it is the reason a DURABLE question had to exist beside it.
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
    /// hand. See <see cref="Cards.BurnLookPolicy.ForCard"/> for the term and for its one blind spot
    /// (a card a rest handed back to the hand still reads <c>Discarded</c>), which every caller
    /// whose population can contain a hand card must answer before asking.</para>
    /// </summary>
    internal static RemoteCardArt.CardFxLook FromState(CAbilityCard? card)
        => FromPolicy(Cards.BurnLookPolicy.ForCard(card));

    /// <summary>
    /// Which of the game's two card-FX looks <paramref name="full"/> is running right now, or
    /// <see cref="RemoteCardArt.CardFxLook.None"/> for a card nobody has touched.
    ///
    /// <para>THE GAME'S OWN TASK FLAGS AND NOTHING ELSE. <c>FXTask.BurnCard</c> is
    /// <c>BurnCardTimeline</c>; <c>DiscardMode</c> and <c>LostMode</c> both run
    /// <c>GhostOutOnTimeline</c> (CardEffects.cs:422-423), which is why they collapse to one answer
    /// here. Burn is asked FIRST because a card can be mid-burn while its discard flag is still
    /// standing, and the char is the later, stronger look.</para>
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
            if (fx.HasEffect(CardEffects.FXTask.BurnCard))
                return RemoteCardArt.CardFxLook.Burn;
            if (fx.HasEffect(CardEffects.FXTask.DiscardMode)
                || fx.HasEffect(CardEffects.FXTask.LostMode))
                return RemoteCardArt.CardFxLook.Ghost;
            return RemoteCardArt.CardFxLook.None;
        }
        catch (System.Exception)
        {
            return RemoteCardArt.CardFxLook.None;
        }
    }
}
