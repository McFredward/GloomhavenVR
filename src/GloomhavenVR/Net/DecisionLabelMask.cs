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
/// <para>THE MASK IS IN THE SENDER'S LANGUAGE, DELIBERATELY. Every other word on this path is
/// already the sender's — that is the accepted property of a record that carries rendered runtime
/// text — so localizing the mask to the VIEWER would produce one word disagreeing with the sentence
/// around it. Same language, same sentence.</para>
/// </summary>
internal static class DecisionLabelMask
{
    /// <summary>Reused across calls — this runs on the 0.25 s decision cadence while a row is
    /// docked, and a per-tick allocation on a sampler is how a cosmetic surface becomes a frame
    /// cost.</summary>
    private static readonly List<string> NameScratch = new(32);

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
    /// case, and is one predicate read.
    /// </summary>
    /// <param name="label">One decision-row wording, already flattened to a single line.</param>
    /// <param name="masked">How many distinct card names were replaced.</param>
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
                return label;

            NameScratch.Clear();
            CollectCoveredNames(NameScratch);
            if (NameScratch.Count == 0)
                return label;

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
    /// <see cref="RevealGate.PeersMaySeeOurCard"/>, so a card that becomes public (it is burnt, it
    /// is activated) stops being masked on the very tick it does.
    ///
    /// <para>THE THREE LISTS ARE THE COVERED ONES BY CONSTRUCTION, and the per-card call is still
    /// made rather than skipped: hand / discard / round are exactly the piles a burn-or-lose prompt
    /// draws its subject from, and the lists the burn exception exempts (lost, permanently lost,
    /// activated) are deliberately NOT walked here. Asking the predicate anyway is what keeps this
    /// class from becoming a second copy of the rule — if the exemption ever widens, this follows it
    /// without an edit.</para>
    /// </summary>
    private static void CollectCoveredNames(List<string> into)
    {
        CardsHandManager manager = CardsHandManager.Instance;
        List<CardsHandUI>? hands = manager != null ? manager.CardHandsUI : null;
        if (hands == null)
            return;
        for (int h = 0; h < hands.Count; h++)
        {
            CardsHandUI hand = hands[h];
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            if (actor == null || !actor.IsUnderMyControl)
                continue;   // a peer's own cards are not ours to mask or to leak
            CCharacterClass? cc = actor.CharacterClass;
            if (cc == null)
                continue;
            AddCovered(into, actor, cc.HandAbilityCards);
            AddCovered(into, actor, cc.DiscardedAbilityCards);
            AddCovered(into, actor, cc.RoundAbilityCards);
        }
    }

    /// <summary>Add the names of the cards in <paramref name="list"/> our peers may not see. Longest
    /// first is NOT needed — the names are replaced independently and a card name is never a strict
    /// prefix of another in a way that changes the result — but a null or empty name is skipped,
    /// because <c>string.Replace(string.Empty, …)</c> throws.</summary>
    private static void AddCovered(List<string> into, CPlayerActor actor,
                                   List<CAbilityCard>? list)
    {
        if (list == null)
            return;
        for (int i = 0; i < list.Count; i++)
        {
            CAbilityCard card = list[i];
            if (card == null)
                continue;
            string name = card.Name;
            if (string.IsNullOrEmpty(name))
                continue;
            if (RevealGate.PeersMaySeeOurCard(actor, card.CardInstanceID))
                continue;   // already public — the burn exception, and it outranks the phase
            if (!into.Contains(name))
                into.Add(name);
        }
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
                        + "force ONLY while RevealGate.PeersMaySeeOurCard is false for that card — "
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
