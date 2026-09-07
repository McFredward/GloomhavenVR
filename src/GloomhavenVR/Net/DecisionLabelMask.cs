using System.Collections.Generic;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// THE ONE APPROVED EXCEPTION TO THE 1:1 RULE — the card IDENTITY is taken out of every peer-bound
/// wording that could carry it (extension records 12 and 13) while, and only while, the face rule
/// says that card is covered.
///
/// <para>USER, VERBATIM (2026-09-07, follow-up to item 6): "wegen dem Anti-Cheat-System in der
/// Auswahlphase muss hier ein genehmigte Ausnahme der 1:1 Regel greifen, der Name der Karte in dem
/// Dialog im remote board muss ausgeblendet werden. Nutz eine immersive Art das ausblenden und
/// bleib trotzdem so nah wie möglich am Dialog den der Spieler auch sieht."</para>
///
/// <para>IT IS AN ANTI-CHEAT BOUNDARY AND NOT A PRESENTATION PREFERENCE, and the distinction is the
/// reason this file exists rather than a render-time tweak. The standing ruling is that a peer must
/// see what the owner sees; the standing ruling ABOVE it is that during the game's own
/// <c>SelectAbilityCardsOrLongRest</c> window a player may not learn which cards another player
/// holds. Where those two meet, the second wins, and the user has now said so explicitly. A future
/// round that finds this and thinks "1:1 is being violated here, restore it" would re-open the leak:
/// it is violated ON PURPOSE, with permission, and this paragraph is the permission.</para>
///
/// <para>WHY THE MASK IS APPLIED AT THE SENDER. A receiver that is handed the name has been told the
/// secret, whatever it then chooses to draw — so masking at the render end would leave
/// <see cref="RevealGate"/>'s opening promise ("We never transmit card identities over our side
/// channel") false for this one record, which is exactly the state the 2026-09-05 session caught it
/// in. The identity does not go on the wire. As a second consequence both byte caps
/// (<c>NetProtocol.DecisionLinesMaxBytes</c> 160 B, <c>NetProtocol.CapLabelMaxBytes</c> 48 B) see
/// the ALREADY-MASKED string, so a truncation can never cut a mask off and leave the name — a
/// recorded finding in this project ("a cap sized in English is a codec, never layout"), and the one
/// ordering mistake that would have made this fix cost a round.</para>
///
/// <para>MEASURED, IN BOTH LANGUAGES, BECAUSE THAT FINDING IS ABOUT GERMAN. The longest masked burn
/// wording is <c>&lt;sprite name="LOST"&gt; Verbrennen "eine versiegelte Karte"</c> at 56 B UTF8
/// against 46-55 B for the unmasked originals, and 41 B in English against 42 B. Record 12 holds it
/// with 104 B to spare. Record 13 does NOT — but it did not hold the UNMASKED wordings either, and
/// the cause is not the mask: 21 of those bytes are a <c>&lt;sprite&gt;</c> tag that the cap's own
/// render path (<c>WorldUI.NativeButtonSkin.SanitizeLabel</c>) strips again on arrival, so a third
/// of a 48 B budget is spent on markup that is thrown away. That is a pre-existing LEGIBILITY defect
/// on record 13 and is stated here with numbers rather than fixed here, because changing what that
/// record carries is a wire-semantics change and this class's job is the identity. The mask's own
/// log line prints the byte count against both caps so the truncation is measured and not
/// silent.</para>
///
/// <para>WHY MASKING THE WIRE IS SUFFICIENT, WHICH IS NOT OBVIOUS AND WAS CHECKED RATHER THAN
/// ASSUMED. A peer renders a mirrored decision row two different ways, and only one of them reads
/// record 12:</para>
/// <list type="bullet">
/// <item><description>TAKE-DAMAGE and SHORT-REST YES/NO prompts are drawn from the RECEIVER's own
/// game widgets (<c>RemoteDecisionWidgets</c> — "the art, the icons and the words come from the
/// receiver's own game"), and their labels are only RECOLOURED, never re-lettered. Their wordings
/// come from generic <c>GUI_*</c> keys ("Verbrennen", "2 abgelegte Karten verbrennen", "Ja"/"Nein",
/// and the literal key <c>GUI_SHORT_REST_CONFIRMATION</c>) and carry no card name at all — so there
/// is nothing to mask on that path and nothing this class could reach if there were.</description></item>
/// <item><description>The DIALOG-POPUP prompt — the burn / lose-card prompt, the one whose confirm
/// label reads <c>Verbrennen "In die Nacht"</c> and names a card — is built from the receiver's own
/// button prefab and LETTERED WITH THIS RECORD (<c>RemoteDialogOptions</c>: "a DialogOption.text is
/// a runtime string and not a localization key", which is precisely why the wording has to travel).
/// That is the leak, and it is fed by exactly one string. Masking it here closes it, and the
/// receiver has no second source it could re-derive the name from.</description></item>
/// </list>
///
/// <para>AND RECORD 12 WAS NOT THE ONLY CHANNEL — the sweep this fix owed found a second one with no
/// gate at all. Extension record 13's CONFIRM and UNDO cap labels (bits 0 and 2) fall through, during
/// a modal card pick, to <c>Cards.CardsGameApi.PickDialogOptionLabel</c>, which reads
/// <c>button.ExtendedButton.buttonText.text</c> off the very same live <c>DialogPopup</c> option —
/// the identical <c>Verbrennen "…"</c> string — and a peer letters their mirrored confirm cap with
/// it (<c>RemoteBoardFurniture.SetCapLabels</c>). Masking record 12 alone would have moved the leak
/// one surface over and left this fix as theatre, which is the shape this project records as "the
/// blind spot is the lead". Both records are masked, at their own sender seams, through this one
/// class — and the OWNER's own keycap, which reads the same accessor, is untouched, because the wire
/// is the boundary and not the accessor.</para>
///
/// <para>IT SHIPPED AND IT NEVER FIRED ONCE — THE ROOT CAUSE OF THE 2026-09-07 REPORT, ITEM 7.
/// USER, VERBATIM: "Der Text auf dem Button der den Namen der Karte verrät ist voll in der kurzen
/// Rast auf dem remote board für alle anderen Spieler lesbar und damit ist hier dein anti-cheat
/// nicht aktiv geworden."</para>
///
/// <para>HE IS RIGHT AND THE PREDICATE WAS NOT THE PROBLEM. The phase term was CORRECT for the
/// short rest — the peer's own log states it in the instrument's own words at raw line 181268,
/// <c>DECISION LABEL INSIDE THE SECRET WINDOW: record 12 is publishing "&lt;sprite name="Lost"&gt;
/// Verbrennen "Zusatzdolch"|Neu ziehen: …" while RevealGate.PeersSeeOurCardFronts is SHUT</c>. So
/// the gate was shut, this class RAN, and the name went out anyway. What was wrong is one line of
/// arithmetic: <see cref="AddCovered"/> collected <c>CAbilityCard.Name</c>, which is NOT a card's
/// name in any language a player reads. It is the YML/localization KEY —
/// <c>ScenarioRuleClient.SRLYML.AbilityCards.Single(s =&gt; s.ID == ID).Name</c>, CBaseCard.cs:52-61,
/// i.e. the string <c>ABILITY_CARD_SpareDagger</c>, which <c>CBaseCard.StrictName</c> exists to trim
/// the prefix off. The label it was searched in carries <c>FullAbilityCard.Title</c>
/// (<c>CardsHandUI.OnLoseCardClick</c> and <c>PerformShortRest</c> both do
/// <c>string.Format(GetTranslation("GUI_LOSE_CARD"), cardUI.fullAbilityCard.Title)</c>), and Title
/// is <c>titleText.text</c>, assigned in <c>FullAbilityCard.SetCardName</c> as
/// <c>LocalizationManager.GetTranslation(cardName)</c> — the TRANSLATED key, "Zusatzdolch". The two
/// strings can never be equal, so <c>string.Replace</c> matched nothing, <c>masked</c> stayed 0, and
/// the change-gated log below never printed. THAT SILENCE IS THE PROOF AND IT WAS ALREADY IN THE
/// LOGS: across a 71 MB host log and a 44 MB peer log there are ZERO <c>DECISION LABEL MASK</c>
/// lines, against two record-12 publishes inside the secret window one of which quotes a card. A
/// mask that has never masked anything reads exactly like a mask with nothing to do.</para>
///
/// <para>AND THAT LAST SENTENCE WAS STILL TRUE OF THE INSTRUMENTS AFTER THE FIX, WHICH IS WHY THERE
/// IS NOW A THIRD LINE (2026-09-07 evening). The ModBuild 478 hardware round produced ZERO
/// <c>DECISION LABEL MASK</c> lines on BOTH machines again — the identical reading the leak had
/// before it was fixed — and the fix could not be told apart from the defect by grepping. It is the
/// FIRST reading and not the second, established from the surrounding instruments rather than
/// assumed: there are ZERO anchored <c>[Net] DECISION LABEL INSIDE THE SECRET WINDOW</c> lines on
/// either machine, so no decision row was ever docked while <see cref="RevealGate
/// .PeersSeeOurCardFronts"/> was shut and <see cref="Apply"/> returned on its fast path every single
/// call; the only record-12 wording that travelled all session was the generic
/// <c>"1 verfügbare Karte verbrennen|Schaden erhalten|2 abgeworfene Karten verbrennen"</c> (5 sends
/// on one machine, 4 on the other, none of them naming a card), <c>Cap labels SENT</c> never carried
/// anything but <c>'Fortfahren'</c> or <c>&lt;hidden&gt;</c>, and <c>SHORT REST SEAT</c> shows the
/// sacrifice recess never held a nameable card. THE MASK IS UNVERIFIED, NOT BROKEN. (The "282 short
/// rest lines" a first pass counted are 158 <c>PEER CARD FACE CENSUS</c> and 83 <c>ANONYMOUS
/// RECESS</c> rows QUOTING the token in their own prose — this project's standing grep-discipline
/// finding, and there are zero anchored <c>[Cards] … SHORT REST</c> lines.) <see cref="LogState"/>
/// is the remedy: it prints the mask's STATE on every edge of it, so "armed with nothing to do"
/// and "armed and matching nothing" stop reading the same.</para>
///
/// <para>THE FIX IS TO MASK THE STRING THE PLAYER ACTUALLY SEES, resolved through the same accessor
/// the game itself used to letter the dialog: <c>Loc.Game(card.Name, …)</c> →
/// <c>LocalizationManager.TryGetTranslation</c>. The raw key is kept in the search set as well, at
/// no cost, so a wording built from the key rather than from the title is covered too.</para>
///
/// <para>AND AN UNRESOLVABLE NAME NOW WITHHOLDS THE LABEL. If a covered card's DISPLAYED name
/// cannot be resolved, this class cannot tell whether the wording names it — and "I could not
/// check" must never be the reason a card identity is published. That is the same direction the
/// catch below already fails in, moved to the one hole it did not cover: a silent no-match. The
/// same rule now covers a covered SET that cannot be enumerated at all (no
/// <c>CardsHandManager</c>, no hand list, a hand with no <c>CharacterClass</c>) — states that
/// cannot occur inside <c>SelectAbilityCardsOrLongRest</c>, which is precisely why refusing them
/// costs a real session nothing.</para>
///
/// <para>THE OWNERSHIP TERM WAS WIDENED IN THE SAME PASS, and it is a second fail-open of the same
/// shape. <c>CActor.IsUnderMyControl</c> is a cached per-client bool whose FFSNet writers are
/// asymmetric (<see cref="RevealGate"/>'s <c>LocallyControls</c> doc carries the derivation), so it
/// can read stale FALSE on the very client that owns the hand — and a hand skipped here is a hand
/// whose card names are never collected and never masked. A hand is now taken as ours when EITHER
/// the flag or <c>Cards.CardsGameApi.LocalControlsActor</c> says so. Over-collecting cannot hurt:
/// the only effect of an extra name in the set is that a wording which NAMES that card gets it
/// replaced, and a wording of ours that names somebody else's covered card is a leak either way.
/// </para>
///
/// <para>THE PHASE PREDICATE IS UNCHANGED AND DELIBERATELY SO. <see cref="RevealGate"/> was not
/// edited by this fix: <see cref="RevealGate.IsSecretSelectionPhase"/> already answers TRUE for the
/// whole of a short rest (the game only offers one in that phase — <c>CardsHandUI.UpdateShortRest</c>
/// shows the button under <c>PhaseType == SelectAbilityCardsOrLongRest</c>), and widening the window
/// to "fix" the short rest would have closed the long rest, which resolves as an ACTION and is ruled
/// fully open.</para>
///
/// <para>THE MASK IS IN THE SENDER'S LANGUAGE, DELIBERATELY. Every other word on this path is
/// already the sender's — that is the accepted property of a record that carries rendered runtime
/// text — so localizing the mask to the VIEWER would produce one word disagreeing with the sentence
/// around it. Same language, same sentence.</para>
/// </summary>
internal static class DecisionLabelMask
{
    /// <summary>Reused across calls — this runs on the 0.25 s decision cadence while a row is
    /// docked, and a per-tick allocation on a sampler is how a cosmetic surface becomes a frame
    /// cost. Sized for TWO entries per covered card (the displayed title and the raw YML key) over a
    /// full hand plus discard plus round pile.</summary>
    private static readonly List<string> NameScratch = new(64);

    /// <summary>Change-gate for the diagnostic: the last label this class actually masked, so a
    /// prompt that stands for ten seconds prints one line and not forty.</summary>
    private static string _lastLogged = string.Empty;

    /// <summary>
    /// THE IMMERSIVE FORM, and the choice is justified rather than asserted.
    ///
    /// <para>WHAT IT MUST NOT BE: a redaction. Not <c>[hidden]</c>, not a bar, not asterisks, not
    /// "unknown card" — those announce that something was taken away, which is the opposite of "so
    /// nah wie möglich am Dialog den der Spieler auch sieht".</para>
    ///
    /// <para>WHAT IT IS: the world's own name for the object the viewer is ACTUALLY looking at. The
    /// card this label is about is lying face-down in the owner's recess — that is the very picture
    /// the rest of this round's fix puts there — so the dialog now says the same thing the table
    /// says, and the sentence stays a sentence: <c>Verbrennen "eine versiegelte Karte"</c>. It
    /// agrees with the card beside it, which is what makes it read as the game rather than as a
    /// censor bar.</para>
    ///
    /// <para>WHY NO GLYPH AND NO SPRITE TAG, though a wax-seal or card-back sprite was the obvious
    /// first idea: this string is lettered onto the RECEIVER's font atlas, and a character or
    /// <c>&lt;sprite&gt;</c> name that atlas does not carry renders as a missing-glyph box. A box is
    /// strictly worse than a word — it reads as broken rather than as sealed, and it would break the
    /// "so nah wie möglich" requirement in the one way the owner could never see from their own
    /// side. Plain letters that already occur in both shipped languages cannot fail that way.</para>
    ///
    /// <para>LENGTH IS SIZED IN LAYOUT AND NOT IN BYTES. The receiver's option plate is measured to
    /// its string (<c>RemoteDialogOptions.LayoutCaption</c>: the plate width is <c>Max(need, …)</c>,
    /// <c>enableWordWrapping = false</c>, <c>overflowMode = Overflow</c> under the standing "never
    /// truncate" ruling), so a longer mask widens the plate instead of clipping the word — and that
    /// class already prints the MEASURED line count and the drawn string per option, which is the
    /// falsifier for this choice in both languages. Both forms are kept inside the range real card
    /// names occupy ("In die Nacht", "Verdorbene Schneide") so the row barely moves.</para>
    /// </summary>
    private const string MaskLocId = "mp_sealed_card";

    /// <summary>
    /// Replace every COVERED card name in <paramref name="label"/> with the sealed-card wording.
    /// Returns the label unchanged when nothing is covered — which is the overwhelmingly common
    /// case, and is one predicate read — and returns the EMPTY string with
    /// <paramref name="masked"/> = -1 when the covered set could not be fully read, which the two
    /// callers render as their own generic wording.
    /// </summary>
    /// <param name="label">One decision-row wording, already flattened to a single line.</param>
    /// <param name="masked">How many distinct name STRINGS were replaced (a covered card
    /// contributes two — its displayed title and its raw YML key — so this is an upper bound on the
    /// number of CARDS, not a count of them), or -1 when the wording was withheld.</param>
    internal static string Apply(string label, out int masked)
    {
        masked = 0;
        if (string.IsNullOrEmpty(label))
            return label;
        try
        {
            // THE FAST PATH IS THE RULE ITSELF. Outside the secret window every card of ours is
            // public, so there is nothing to mask and nothing to walk — one static property read on
            // a 4 Hz sampler.
            if (RevealGate.PeersSeeOurCardFronts)
            {
                LogState(windowOpen: true, coveredNames: -1, replaced: -1);
                return label;
            }

            NameScratch.Clear();
            if (!CollectCoveredNames(NameScratch))
            {
                LogState(windowOpen: false, coveredNames: -1, replaced: -1);
                // BLIND IS NOT CLEAN. The covered set could not be enumerated, or a covered card's
                // DISPLAYED name could not be resolved — so this class cannot say whether the
                // wording names it, and an unchecked wording must not be published inside the
                // window. Same direction as the catch below, on the hole the catch never covered:
                // a silent no-match, which is exactly how this mask ran for a whole session
                // without masking one string.
                LogWithheld(label);
                masked = -1;
                return string.Empty;
            }
            if (NameScratch.Count == 0)
            {
                LogState(windowOpen: false, coveredNames: 0, replaced: 0);
                return label;
            }

            string sealedCard = Loc.Mod(MaskLocId);
            string result = label;
            for (int i = 0; i < NameScratch.Count; i++)
            {
                string name = NameScratch[i];
                if (name.Length == 0 || result.IndexOf(name, System.StringComparison.Ordinal) < 0)
                    continue;
                result = result.Replace(name, sealedCard);
                masked++;
            }
            LogState(windowOpen: false, coveredNames: NameScratch.Count, replaced: masked);
            if (masked > 0)
                LogIfChanged(label, result, masked);
            return result;
        }
        catch (System.Exception e)
        {
            // A MASK THAT THROWS MUST NOT PUBLISH THE NAME. Every other degradation in this system
            // shows LESS; this one has to as well, so a failure withholds the wording entirely
            // rather than letting the unmasked string through. The peer's row falls back to the
            // mod-drawn plates for that tick, which is a picture they already have a path for.
            VRLog.Warn("Net", "DECISION LABEL MASK: the identity mask threw "
                            + $"({e.GetType().Name}: {e.Message}) — the wording is WITHHELD for this "
                            + "tick rather than published unmasked. A mask that cannot run is not a "
                            + "reason to send a card identity into the secret window.");
            masked = -1;
            return string.Empty;
        }
    }

    /// <summary>
    /// Every ability-card NAME belonging to a character we control whose identity our peers may not
    /// currently know — asked one card at a time through
    /// <see cref="RevealGate.PeersMayNameOurCard"/>, so a card that becomes public (it is burnt, it
    /// is activated) stops being masked on the very tick it does. Returns FALSE when the covered set
    /// could not be enumerated at all, which the caller turns into a withheld label.
    ///
    /// <para>THE THREE LISTS ARE THE COVERED ONES BY CONSTRUCTION, and the per-card call is still
    /// made rather than skipped: hand / discard / round are exactly the piles a burn-or-lose prompt
    /// draws its subject from, and the lists the burn exception exempts (lost, permanently lost,
    /// activated) are deliberately NOT walked here. Asking the predicate anyway is what keeps this
    /// class from becoming a second copy of the rule — if the exemption ever widens, this follows it
    /// without an edit.</para>
    /// </summary>
    private static bool CollectCoveredNames(List<string> into)
    {
        CardsHandManager manager = CardsHandManager.Instance;
        List<CardsHandUI>? hands = manager != null ? manager.CardHandsUI : null;
        if (hands == null)
            return false;   // cannot enumerate what is covered — the caller withholds
        bool complete = true;
        for (int h = 0; h < hands.Count; h++)
        {
            CardsHandUI hand = hands[h];
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            if (actor == null)
                continue;   // an empty tab, not a hand of ours we failed to read
            if (!LocallyOwned(actor))
                continue;   // a peer's own cards are not ours to mask or to leak
            CCharacterClass? cc = actor.CharacterClass;
            if (cc == null)
            {
                // OURS, AND UNREADABLE. Its cards are covered by the phase and we cannot name one
                // of them, so we cannot check the wording against them either.
                complete = false;
                continue;
            }
            complete &= AddCovered(into, actor, cc.HandAbilityCards);
            complete &= AddCovered(into, actor, cc.DiscardedAbilityCards);
            complete &= AddCovered(into, actor, cc.RoundAbilityCards);
        }
        return complete;
    }

    /// <summary>
    /// Does THIS client control <paramref name="actor"/>? The game's own controllable list where it
    /// can answer, the cached flag where it cannot — the two witnesses
    /// <c>Cards.CardsGameApi.IsLocalHand</c> uses, except that here they are OR-ed rather than one
    /// preferred over the other.
    ///
    /// <para>THE OR IS THE POINT, AND IT IS THIS FILE'S DIRECTION OF FAILURE. Everywhere else in the
    /// mod a wrong "ours" draws a wrong picture; here a wrong "NOT ours" SKIPS A HAND, so that
    /// hand's card names are never collected and a wording naming one of them is published in the
    /// clear. <c>CActor.IsUnderMyControl</c> can read stale FALSE on the owning client (asymmetric
    /// FFSNet writers — <see cref="RevealGate"/>'s <c>LocallyControls</c> carries the derivation),
    /// so either witness saying "ours" is taken as ours. Over-collecting is free: an extra name only
    /// changes an outgoing wording that NAMES that card, and a wording of ours naming a covered card
    /// is the very thing this class exists to remove.</para>
    /// </summary>
    private static bool LocallyOwned(CPlayerActor actor)
    {
        if (actor.IsUnderMyControl)
            return true;
        bool byList = Cards.CardsGameApi.LocalControlsActor(actor, out bool answerable);
        return answerable && byList;
    }

    /// <summary>Add the names of the cards in <paramref name="list"/> our peers may not see. Longest
    /// first is NOT needed — the names are replaced independently and a card name is never a strict
    /// prefix of another in a way that changes the result — but a null or empty name is skipped,
    /// because <c>string.Replace(string.Empty, …)</c> throws.
    ///
    /// <para>TWO STRINGS PER CARD, AND THE ONE THAT MATTERS IS THE TRANSLATED ONE. This is the whole
    /// of the 2026-09-07 item 7 fix. <c>CAbilityCard.Name</c> is the YML/localization KEY
    /// (<c>ABILITY_CARD_SpareDagger</c>) and never a word a player reads; the wording being searched
    /// carries <c>FullAbilityCard.Title</c>, which the game letters as
    /// <c>LocalizationManager.GetTranslation(card.Name)</c> (<c>FullAbilityCard.SetCardName</c>).
    /// Resolving through <see cref="Loc.Game"/> asks the same table the same way, on the same
    /// client, in the same language — so the two strings are equal by construction rather than by
    /// hope. The RAW key is kept in the set as well, at no cost, so a wording built from the key
    /// rather than from the title is covered too.</para>
    ///
    /// <para>Returns FALSE when a covered card's DISPLAYED name could not be resolved. That is not a
    /// no-op: it is the state in which this class cannot say whether the wording names that card,
    /// and the caller then withholds the whole wording rather than publish an unchecked one. The
    /// burn exception is asked BEFORE the resolve, so a card that is already public costs nothing
    /// and can never withhold a label.</para>
    /// </summary>
    private static bool AddCovered(List<string> into, CPlayerActor actor,
                                   List<CAbilityCard>? list)
    {
        if (list == null)
            return true;
        bool complete = true;
        for (int i = 0; i < list.Count; i++)
        {
            CAbilityCard card = list[i];
            if (card == null)
                continue;
            // ALREADY PUBLIC — the burn/active exception, and it outranks the phase.
            //
            // IT ASKS THE *NAMING* PREDICATE AND NOT THE *FACE* ONE, AND THAT IS THE WHOLE OF THE
            // 2026-09-07 EVENING CARE (item 3). This line SKIPS every card the predicate permits, so
            // whatever the predicate permits stops being masked. On the same day the user ruled that
            // a peer's DISCARD fan shows fronts in every phase — which widened what a peer may SEE,
            // and RevealGate implements it on the FACE side (IsDiscardedCard, reached through
            // CardFaces). Had that widening landed on the predicate THIS line reads, the mask would
            // have stopped masking the short-rest sacrifice: PerformShortRest picks it out of
            // DiscardedAbilityCards and removes nothing, so it is a discard-pile card while the
            // prompt names it, and ModBuild 477 item 7 — confirm='Verbrennen "Zusatzdolch"' with
            // SpareDagger sitting in that list — would be live again. Seeing a peer's whole discard
            // fan is not being told WHICH of those cards the game has singled out; the second is the
            // secret and this line is what keeps it. Do not point this at CardFaces or at
            // IsDiscardedCard.
            if (RevealGate.PeersMayNameOurCard(actor, card.CardInstanceID))
                continue;
            string name = card.Name;
            if (string.IsNullOrEmpty(name))
            {
                complete = false;   // a covered card we cannot name at all
                continue;
            }
            if (!into.Contains(name))
                into.Add(name);
            // THE STRING THE DIALOG ACTUALLY SHOWS. Empty means the table would not answer, and a
            // covered card whose displayed name we do not know is blind, not clean.
            string title = Loc.Game(name, string.Empty);
            if (title.Length == 0)
            {
                complete = false;
                continue;
            }
            if (!into.Contains(title))
                into.Add(title);
        }
        return complete;
    }

    /// <summary>Change-gate for <see cref="LogState"/> — the STATE of the mask, not its edges. Held
    /// as a tuple of ints so the steady state is a struct compare and the line is never COMPOSED
    /// unless it is going to be new; <c>Apply</c> runs on the 0.25 s decision cadence for as long as
    /// a row is docked.</summary>
    private static (int Window, int Covered, int Replaced) _lastState = (-2, -2, -2);

    /// <summary>
    /// WHETHER THIS MASK IS ARMED, AND WITH WHAT — the reading that was missing when ModBuild 479's
    /// round asked whether the 478 fix works and neither log could answer.
    ///
    /// <para>THE PROBLEM IT SOLVES, VERBATIM FROM THE ROUND THAT FOUND IT: "a mask that has never
    /// masked anything reads exactly like a mask with nothing to do". Both instruments beside this
    /// one are EDGE instruments — <see cref="LogIfChanged"/> prints only when a name was actually
    /// replaced, <see cref="LogWithheld"/> only when a wording was refused — so in a session where
    /// no decision row was ever docked inside the secret window (which is exactly the ModBuild 478
    /// session: ZERO anchored <c>DECISION LABEL INSIDE THE SECRET WINDOW</c> lines on both machines,
    /// and the only record-12 wording that travelled at all was the generic "1 verfügbare Karte
    /// verbrennen|Schaden erhalten|2 abgeworfene Karten verbrennen") the whole class is silent and
    /// the silence proves nothing either way. That is this project's recorded "a held instrument
    /// reads as dead" shape, and it cost the 477 leak a round to find.</para>
    ///
    /// <para>HOW TO READ IT (grep token <c>DECISION LABEL MASK STATE</c>). <c>WINDOW=OPEN</c> is the
    /// ordinary state and means the class ran and had nothing to do BY RULING, which is a pass and
    /// not a silence. <c>WINDOW=SHUT, covered=N, replaced=0</c> for a wording that names a card is
    /// the ModBuild 477 defect's exact signature and the reason this line exists: the mask was armed,
    /// it enumerated N names, and it matched none of them. <c>covered=-1</c> is the withheld path.
    /// Any <c>replaced&gt;0</c> has <see cref="LogIfChanged"/> beside it with both strings.</para>
    ///
    /// <para>IT IS NOT A SECOND COPY OF THE VERDICT. It reports the three numbers the class already
    /// computed on the path it took; it decides nothing and no branch reads it.</para>
    /// </summary>
    private static void LogState(bool windowOpen, int coveredNames, int replaced)
    {
        var key = (windowOpen ? 1 : 0, coveredNames, replaced);
        if (key == _lastState)
            return;
        _lastState = key;
        // HW-VERIFY: grep token "DECISION LABEL MASK STATE" — see this method's doc for the three
        // readings and which one is the ModBuild 477 leak's signature.
        VRLog.Note("Net", "DECISION LABEL MASK STATE: WINDOW="
                        + (windowOpen ? "OPEN" : "SHUT")
                        + $", covered={coveredNames}, replaced={replaced}. This line is the mask's "
                        + "STATE and not one of its edges, and it exists because the two edge lines "
                        + "beside it ('DECISION LABEL MASK', 'DECISION LABEL MASK: … WITHHELD') are "
                        + "silent both when this class is working with nothing to do AND when it is "
                        + "broken — the reading that cost ModBuild 477 item 7 a whole round. "
                        + "WINDOW=OPEN means RevealGate.PeersSeeOurCardFronts is open, so every card "
                        + "of ours is public by ruling and there is nothing to mask: a PASS. "
                        + "WINDOW=SHUT with covered=-1 is the withheld path (the covered set could "
                        + "not be read). WINDOW=SHUT with covered=N and replaced=0 beside a wording "
                        + "that NAMES a card is the defect: the mask was armed, enumerated N name "
                        + "strings and matched none — check the STRING FORM first (CAbilityCard.Name "
                        + "is the YML key, the wording carries the translated title), then the LIST. "
                        + "Cross-read it against 'DECISION LABEL INSIDE THE SECRET WINDOW', which "
                        + "quotes the whole of what actually left this client.");
    }

    /// <summary>Change-gate for the withheld-label diagnostic, kept apart from
    /// <see cref="_lastLogged"/> so a standing prompt that is masked and one that is withheld cannot
    /// silence each other.</summary>
    private static string _lastWithheld = string.Empty;

    /// <summary>
    /// The reading that says a wording was REFUSED rather than cleaned, and the falsifier for the
    /// safe direction added on 2026-09-07: this line appearing at all means the covered set could
    /// not be fully read on this client, and a peer got a generic cap where a wording was due.
    /// </summary>
    private static void LogWithheld(string label)
    {
        if (label == _lastWithheld)
            return;
        _lastWithheld = label;
        // HW-VERIFY
        VRLog.Note("Net", "DECISION LABEL MASK: the covered card set could not be fully read on "
                        + "this client (no CardsHandManager, no hand list, a hand of ours with no "
                        + "CharacterClass, or a covered card whose displayed name the localization "
                        + "table would not answer), so the wording \""
                        + label.Replace('\n', '|')
                        + "\" is WITHHELD for this tick rather than published unchecked. A wording "
                        + "this class cannot CHECK against the covered names is not a wording it "
                        + "may publish inside the secret window — the peer letters their cap with "
                        + "their own GUI_CONFIRM / GUI_UNDO and record 12's row falls back to the "
                        + "mod-drawn plates. THIS LINE IS THE FALSIFIER FOR THE 2026-09-07 item 7 "
                        + "FIX: none of the states named above can occur inside "
                        + "SelectAbilityCardsOrLongRest with hands on the table, so if this prints "
                        + "in real play the enumeration in CollectCoveredNames is missing a case "
                        + "and that case is one of the four this sentence lists. It is NOT the leak "
                        + "reading — a leak reads as a card NAME inside 'Decision lines SENT' or "
                        + "'Cap labels SENT'.");
    }

    /// <summary>The measurement the exemption owes, change-gated on the label so a standing prompt
    /// costs one line. It prints BOTH forms and both LENGTHS, because the two traps this fix had to
    /// clear are a name surviving the mask and a mask changing the row's size.</summary>
    private static void LogIfChanged(string before, string after, int masked)
    {
        if (before == _lastLogged)
            return;
        _lastLogged = before;
        int afterBytes = System.Text.Encoding.UTF8.GetByteCount(after);
        int beforeBytes = System.Text.Encoding.UTF8.GetByteCount(before);
        // HW-VERIFY
        VRLog.Note("Net", $"DECISION LABEL MASK: {masked} card identit(y/ies) removed from a peer-"
                        + "bound wording (records 12 and 13 share this mask) "
                        + $"before it went on the wire — sending \"{after.Replace('\n', '|')}\" "
                        + $"({after.Length} chars, {afterBytes} B UTF8) in place of a wording that "
                        + $"named a card ({before.Length} chars, {beforeBytes} B). CAPS: record 12 "
                        + $"holds {NetProtocol.DecisionLinesMaxBytes} B (fits: "
                        + $"{afterBytes <= NetProtocol.DecisionLinesMaxBytes}), record 13 holds "
                        + $"{NetProtocol.CapLabelMaxBytes} B per label (fits: "
                        + $"{afterBytes <= NetProtocol.CapLabelMaxBytes}). A 'False' on the record-13 "
                        + "line is a PRE-EXISTING truncation and NOT a leak — the identity is "
                        + "already gone before the codec runs, which is the whole reason the mask "
                        + "is applied at the sampler and not at publish time. It is a LEGIBILITY "
                        + "defect that predates this mask: a burn wording is 46-55 B before masking "
                        + "and the 48 B cap already cut it, because 21 of those bytes are a "
                        + "<sprite> tag that the cap's own render path (NativeButtonSkin"
                        + ".SanitizeLabel) throws away again. THIS IS THE ONE APPROVED EXCEPTION TO THE 1:1 "
                        + "RULE (user, 2026-09-07: \"wegen dem Anti-Cheat-System in der Auswahlphase "
                        + "muss hier ein genehmigte Ausnahme der 1:1 Regel greifen\"), and it is in "
                        + "force ONLY while RevealGate.PeersMayNameOurCard is false for that card — "
                        + "the same predicate the recess beside it draws its face from, so the card "
                        + "and the sentence about it uncover together. READ IT LIKE THIS: the text "
                        + "printed above is the WHOLE of what left this client, so if a card name "
                        + "still appears in it the mask missed a list and this is the line that "
                        + "proves it. The two lengths are the layout falsifier — the receiver sizes "
                        + "its plate to the string and prints its own MEASURED line count and drawn "
                        + "text per option ('Remote dialog option row'), so read that line in BOTH "
                        + "languages for lines=1 before believing the row still fits.");
    }
}
