namespace GloomhavenVR.WorldUI;

/// <summary>
/// WHICH TERM OF <c>ModalFallback.IsMandatoryDecision</c> MATCHED — and, because the terms do not
/// all answer the same question, WHICH QUESTION the caller is entitled to build on.
///
/// <para><b>WHY THIS EXISTS AT ALL: A DERIVED NET WAS READ AS AN IDENTITY, AND IT COST SEVENTEEN
/// WINDOWS IN ONE HARDWARE SESSION.</b> <c>IsMandatoryDecision</c> answers one question — <i>may
/// the mod offer a way out of this window?</i> — out of two very different kinds of term:</para>
/// <list type="bullet">
///   <item><b>IDENTITY</b> (<see cref="EncounterPanel"/>, <see cref="RewardShowcase"/>,
///   <see cref="ItemCardPicker"/>, <see cref="TakeDamagePanel"/>): a named component was found on
///   the window's own GameObject. Each was enrolled by reading the decompiled class and finding a
///   waiter with no hide fallback, so the term states a FACT ABOUT THIS WINDOW: something is
///   blocked on it until the player answers.</item>
///   <item><b>THE DERIVED NET</b> (<see cref="GameRefusesEscape"/>): <c>escapeKeyAction ==
///   None</c>. The game declines to close the window on ESC. That is a fact about the ESC KEY, not
///   about the window — it says the game withheld a close, never what is waiting on it, and the
///   term's own reason string says so in as many words.</item>
/// </list>
///
/// <para><b>THE FAILURE THIS TYPE PREVENTS.</b> ModBuild 386 added
/// <c>StickinessSpentByAnsweredDecision</c>: a map-room float whose window the game has closed has
/// its stickiness declared SPENT and is released — correct for a window that carried a decision,
/// because the game only hides one of those when it has been ANSWERED. It asked
/// <c>IsMandatoryDecision</c>, i.e. it asked the union, and the merchant and the temple both carry
/// <c>escapeKeyAction == None</c> — the mod prints so itself every time it floats one. So a
/// merchant hidden by the flat game's ordinary single-window discipline (the temple opening, or a
/// map-surface switch running <c>modes[current].Exit()</c>) matched the net, lost its stickiness
/// and was torn down. The 2026-09-03 multiplayer logs count <c>MANDATORY DECISION ANSWERED</c>
/// 1 host / 7 remote for the shop and 1 host / 10 remote for the temple — seventeen windows closed
/// by a rule whose stated scope was the encounter window, and the user's report is the two halves
/// of exactly that: <i>windows must not close when the map is switched</i>, and <i>several windows
/// must be able to stand open in parallel again</i>.</para>
///
/// <para><b>SO THE TWO QUESTIONS ARE NOW TWO METHODS AND THE ANSWER CARRIES ITS TERM.</b>
/// <see cref="MandatoryDecisionTerms.IdentifiesTheWindow"/> is the predicate a caller uses when it
/// needs to know WHAT this window is; the full union stays the right answer for "may this window be
/// closed by the mod", which is what the X gate and the escape chord ask. Adding a second carve-out
/// beside the existing ESC/options one was rejected: a carve-out list grows one entry per report,
/// and every entry is another window that has to be found the hard way first.</para>
///
/// <para><b>AND IT IS A GATED TYPE, NOT A COMMENT.</b> <c>MandatoryDecisionTermVectors</c> in the
/// wire-test project pins every member's classification by name, so a new term added to
/// <c>IsMandatoryDecision</c> fails a gate until somebody states which of the two questions it
/// answers. That is the whole point: this defect was a third widening passing silently, and the
/// commit that shipped it asserted in its own message that the merchant and the temple were
/// untouched.</para>
/// </summary>
internal enum MandatoryDecisionTerm
{
    /// <summary>No term matched — the mod may offer a close on this window.</summary>
    None = 0,

    /// <summary>IDENTITY — <c>UIEventPanel</c> / <c>UIWindowID.EventsPanel</c>, the road/city
    /// encounter window ('Begegnung!'). The reported window.</summary>
    EncounterPanel = 1,

    /// <summary>IDENTITY — <c>UICampaignRewardWindow</c> or <c>UIRewardsManager</c>. Native
    /// continuation completes the campaign process; the guildmaster <c>while (processingRewards)</c> coroutine
    /// is ended by nothing but <c>EndProcess</c>, which is what CALLS the hide.</summary>
    RewardShowcase = 2,

    /// <summary>IDENTITY — <c>ItemCardPicker</c>. Its hide listener clears content and fires
    /// neither <c>onConfirmPressed</c> nor <c>onItemsSelected</c>.</summary>
    ItemCardPicker = 3,

    /// <summary>IDENTITY — <c>TakeDamagePanel</c>. The confirmation is a wire action sent only from
    /// a button press, so a hide leaves the phase unresolved.</summary>
    TakeDamagePanel = 4,

    /// <summary>THE DERIVED NET — <c>escapeKeyAction == None</c>. The game refuses to close this
    /// window on ESC. It names the ESC key's policy and NOT the window, so it must never be read as
    /// "this window carries a decision". Every ordinary map-room destination carries it.</summary>
    GameRefusesEscape = 5,
}

/// <summary>
/// The pure half of <see cref="MandatoryDecisionTerm"/> — no Unity types, so the wire-test project
/// can compile it and hold the classification against a table of names.
/// </summary>
internal static class MandatoryDecisionTerms
{
    /// <summary>
    /// Does this term name WHICH WINDOW this is — a fact about the window itself — as opposed to
    /// merely reporting the game's ESC policy for it?
    ///
    /// <para>A caller that is deciding "is something blocked on this window until the player
    /// answers it" must use THIS and not <c>IsMandatoryDecision</c>. A caller that is deciding "may
    /// the mod offer a way out of this window" wants the union, because the derived net is a
    /// perfectly good answer to that one — it is the reason the net was added.</para>
    ///
    /// <para>The <c>default</c> arm answers FALSE, which is the safe direction on both counts: an
    /// unclassified term still withholds the X (the union matches it) and does NOT spend a float's
    /// stickiness. It is also unreachable in a shipped build — the wire-test gate is what makes
    /// sure of that, rather than a comment claiming it.</para>
    /// </summary>
    internal static bool IdentifiesTheWindow(MandatoryDecisionTerm term)
    {
        switch (term)
        {
            case MandatoryDecisionTerm.EncounterPanel:
            case MandatoryDecisionTerm.RewardShowcase:
            case MandatoryDecisionTerm.ItemCardPicker:
            case MandatoryDecisionTerm.TakeDamagePanel:
                return true;
            case MandatoryDecisionTerm.None:
            case MandatoryDecisionTerm.GameRefusesEscape:
            default:
                return false;
        }
    }

    /// <summary>Did any term match at all — the union, i.e. the original predicate.</summary>
    internal static bool IsMandatory(MandatoryDecisionTerm term) =>
        term != MandatoryDecisionTerm.None;
}
