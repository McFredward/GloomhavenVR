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
// ═══════════════════════════════════════════════════════════════════════════════════════════════

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
