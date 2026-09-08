using GloomhavenVR.Core;
using ScenarioRuleLibrary;

namespace GloomhavenVR.Net;

/// <summary>
/// THE PROMPT TEXT OF A PEER'S DECISION DOCK, composed on THIS machine.
///
/// The take-damage decision is not just three buttons: the sentence above them ("Schadensphase:
/// Erleide entweder Schaden, verbrenne eine deiner verfügbaren Karten oder verbrenne zwei deiner
/// abgelegten Karten") is what says what the buttons are FOR, and the user ruling of 2026-08-08
/// puts it on the remote board with everything else ("alle Interaktionen, Animationen und Anzeigen
/// des Controllboards … so wie der Spieler sie sieht"). The owner draws it by converting the game's
/// <c>HelpBox</c> (<c>WorldUI.Surfaces.DamageTooltipSurface</c>); a peer has no such window to
/// convert, because the game hid its own copy of the panel (<c>TakeDamagePanel.ShowOtherPlayer</c>
/// ends in <c>myWindow.Hide(instant: true)</c>) and put a "waiting for X" line in its help box
/// instead.
///
/// ─── WHY THE TEXT IS COMPOSED HERE AND NOT SENT ────────────────────────────────────────────────
/// Every other text a remote board shows — the pick placard (record 7), the board tooltip (9), the
/// decision button labels (12), the cap wordings (13) — rides the wire verbatim, because each is
/// produced by LOCAL UI state no receiver can reproduce. This one is different in both directions:
///
///   • IT CANNOT BE SENT. <c>ShowDamageTooltip</c>'s mandatory-use branch builds its sentence by
///     prefixing the NAMES OF ACTIVE-BONUS CARDS (TakeDamagePanel.cs:325-333). The standing rule
///     for this wire is absolute — no card identity, ever; reveals only through
///     <see cref="RevealGate"/> — so that string may never travel, and a text record with a
///     "except in that branch" exception would be a rule with a hole in it.
///   • IT DOES NOT NEED TO BE. Every input of the game's branch selection except the branch itself
///     is already replicated to this client: in an online game the non-controlling clients receive
///     the same damage message and their own <c>TakeDamagePanel.ShowOtherPlayer</c> stores the
///     attacked actor, the damaging ability and the numbers before hiding the window; the character
///     the decision belongs to is the very actor whose remote board we are drawing. What a peer
///     genuinely cannot know is which branch the OWNER's client took, because two of the four
///     conditions are that client's own UI state (their active-bonus bar selection, their currently
///     toggled burn option).
///
/// So exactly that — a three-bit variant id — travels (record 23 flags bits 3..5,
/// <see cref="NetProtocol.ExtIdDecisionState"/>), and this class turns it back into the same
/// sentence out of the RECEIVER's own localization table. Zero text bytes on the wire, no identity
/// channel of any kind, and the line reads in each player's own language, exactly as the game's own
/// help boxes do.
///
/// NO DELIBERATE DIFFERENCE FROM THE OWNER'S LINE ANY MORE: the mandatory-use variant prefixes the
/// active-bonus card NAMES exactly as the owner's does — record 33 carries their KEYS since
/// ModBuild 307, gated on <c>RevealGate.PeersSeeOurCardFronts</c>, and <c>Compose</c> localizes
/// them here (see the <c>DecisionTextMandatoryUse</c> arm below). This header used to say the
/// hint "renders ALONE, without the names … less information than the owner has, never more";
/// that was true before 307 and a 1:1 breach, and review R2 of 2026-09-07 found the stale sentence
/// still contradicting the code beneath it.
///
/// FORMATTING is the game's own (<c>HelpBoxLine.ShowTranslated</c>): a parchment-gold title, a
/// colon, then the body in light grey, as one rich-text string. Reproducing the colours here rather
/// than inventing new ones is what makes the mirrored line read as the same widget.
/// </summary>
internal static class RemoteDecisionPrompt
{
    /// <summary>Title colour of a game help box (<c>HelpBoxLine</c>: <c>#eacf8c</c>).</summary>
    private const string TitleColor = "#eacf8c";

    /// <summary>Body colour of a game help box (<c>HelpBoxLine</c>: <c>#d0d0d0</c>).</summary>
    private const string BodyColor = "#d0d0d0";

    /// <summary>
    /// The prompt line a peer's decision dock should show, or null when there is none to draw.
    ///
    /// <paramref name="kind"/> / <paramref name="variant"/> come straight off wire record 23;
    /// <paramref name="boardActor"/> is the character whose remote board this is — the only name
    /// substitution the common variants need, and one this client reads from the replicated model
    /// rather than from anything on the wire.
    ///
    /// <para>Returns null for a prompt that really has no text line (a pick confirm), for a sender
    /// that predates the record, and for any lookup that fails: a missing line is cosmetic, a wrong
    /// one would be a lie about somebody else's decision.</para>
    ///
    /// <para>THE SHORT REST IS THE ONE CASE THAT DEPENDS ON <paramref name="widgetsMirrored"/>, and
    /// the reason is worth stating because it is easy to get backwards. That prompt DOES have a
    /// question — "GUI_SHORT_REST_CONFIRMATION" — but the mirrored clone already carries it: the
    /// dock mirrors the whole dialog <c>box</c>, and the description text is inside it, lettered by
    /// the RECEIVER's own game. Composing a second copy here would print the question twice, once
    /// above the clone and once inside it. So the line is produced only for the FALLBACK row, where
    /// the mod-drawn plates carry the two options and nothing carries the question.</para>
    /// </summary>
    internal static string? Compose(byte kind, byte variant, CPlayerActor? boardActor,
                                    bool widgetsMirrored, string? names = null)
    {
        if (kind == NetProtocol.DecisionKindShortRestYesNo)
        {
            // See the remarks: the clone brings its own question, the plates do not.
            if (widgetsMirrored)
                return null;
            try
            {
                return Line(null, Loc.Game("GUI_SHORT_REST_CONFIRMATION",
                    "Do you really want to take a short rest?"));
            }
            catch (System.Exception e)
            {
                VRLog.Warn("Net", "Remote decision prompt: composing the short-rest question " +
                                  $"failed ({e.Message}) — the mirrored plates stand without it.");
                return null;
            }
        }
        if (kind != NetProtocol.DecisionKindTakeDamage || variant == NetProtocol.DecisionTextNone)
            return null;
        try
        {
            switch (variant)
            {
                case NetProtocol.DecisionTextDealDamage:
                    return Line(Loc.Game("GUI_TOOLTIP_TITLE_DEAL_DAMAGE", "Damage phase"),
                                Loc.Game("GUI_TOOLTIP_DEAL_DAMAGE",
                                    "Take the damage, burn one available card, or burn two discarded cards."));

                case NetProtocol.DecisionTextWounded:
                    // The game formats this with the ATTACKED actor's name; for a wound that actor
                    // is the character this board belongs to, which this client already knows.
                    return Line(null, Format(
                        Loc.Game("GUI_TOOLTIP_PLAYER_WOUNDED", "{0} is wounded."),
                        ActorName(boardActor)));

                case NetProtocol.DecisionTextSummon:
                    return Line(Loc.Game("GUI_TOOLTIP_DEAL_DAMAGE_SUMMON_TITLE", "Damage phase"),
                                Loc.Game("GUI_TOOLTIP_DEAL_DAMAGE_SUMMON",
                                    "A summon is taking damage."));

                case NetProtocol.DecisionTextCompanion:
                    // Companion wording: <summon name>, <summoner name>. The summoner is this
                    // board's character; the summon's own name comes from the local model when the
                    // attacked actor really is one of theirs, and the plain summon wording stands
                    // in when it cannot be resolved (never a name from another decision).
                    string? summon = CompanionName(boardActor);
                    if (summon == null)
                        goto case NetProtocol.DecisionTextSummon;
                    return Line(Loc.Game("GUI_TOOLTIP_TITLE_DEAL_DAMAGE", "Damage phase"),
                                Format(Loc.Game("GUI_TOOLTIP_DEAL_DAMAGE_COMPANION",
                                        "{0} ({1}) is taking damage."),
                                    summon, ActorName(boardActor)));

                case NetProtocol.DecisionTextMandatoryUse:
                    // THE OWNER'S LINE PREFIXES THIS HINT WITH THE CARD NAMES, and since
                    // ModBuild 307 so does this one. The comment that stood here said those names
                    // "are not on the wire and are not invented here — the hint stands alone", and
                    // that was true and it was a 1:1 breach: a peer read "a mandatory bonus must be
                    // used first" without ever learning WHICH, which is unanswerable the moment the
                    // owner has two. Record 33 carries the KEYS (never the words), gated on
                    // RevealGate.PeersSeeOurCardFronts, and they are localized RIGHT HERE — so the
                    // names read in the VIEWER's language, exactly as the rest of this class works.
                    return Line(Loc.Game("GUI_TOOLTIP_TITLE_DEAL_DAMAGE", "Damage phase"),
                                MandatoryNames(names)
                                + Loc.Game("GUI_TOOLTIP_DEAL_DAMAGE_MANDATORY_USE",
                                    "A mandatory bonus must be used first."));
            }
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote decision prompt: composing variant {variant} failed " +
                              $"({e.Message}) — the mirrored dock shows its plates without a text line.");
        }
        return null;
    }

    /// <summary>
    /// The card-name prefix of the mandatory-use line — the game's own construction
    /// (TakeDamagePanel.cs:325-333) rebuilt out of the KEYS record 33 carried.
    ///
    /// <para>Each key is localized HERE, so the names read in this viewer's language; they are
    /// joined with this viewer's own "AND" wording and wrapped in the game's own red, and the whole
    /// prefix ends with the trailing space the game leaves before the hint. Reproducing the
    /// construction rather than shipping the sender's rendered string is what makes a German host
    /// and an English guest each read their own — the same reason nothing else in this class
    /// travels as words.</para>
    ///
    /// <para>The FONT tag the game adds (<c>MarcellusSC-Regular SDF</c>) is deliberately not
    /// reproduced: the mirrored line is drawn in a TextMeshPro this mod owns, whose font asset is
    /// not that one, and naming a face that is not loaded makes TMP fall back with a warning per
    /// frame. The colour carries the emphasis.</para>
    ///
    /// <para>Empty for a sender that carried no names — which is every prompt but this one, every
    /// sender predating record 33, and any moment the reveal gate was shut.</para>
    /// </summary>
    private static string MandatoryNames(string? keys)
    {
        if (string.IsNullOrEmpty(keys))
            return string.Empty;
        try
        {
            string[] parts = keys!.Split('\n');
            var sb = new System.Text.StringBuilder(64);
            for (int i = 0; i < parts.Length; i++)
            {
                string key = parts[i];
                if (string.IsNullOrWhiteSpace(key))
                    continue;
                if (sb.Length > 0)
                    sb.Append(Loc.Game("AND", "and")).Append(' ');
                // MultiLookupLocalization is what the game itself resolves a card name with; a key
                // it cannot resolve comes back as the key, which is still more than no name at all.
                string shown = LocalizationNameConverter.MultiLookupLocalization(key, out _);
                sb.Append("<color=\"red\">").Append(string.IsNullOrEmpty(shown) ? key : shown)
                  .Append("</color> ");
            }
            return sb.ToString();
        }
        catch (System.Exception e)
        {
            VRLog.Warn("Net", $"Remote decision prompt: localizing the mandatory-use card names " +
                              $"failed ({e.Message}) — the hint renders without them, as it did " +
                              "before ModBuild 307.");
            return string.Empty;
        }
    }

    /// <summary>The game's own help-box line format: gold title, colon, light-grey body (one of the
    /// two may be absent, exactly as <c>HelpBoxLine.ShowTranslated</c> handles it).</summary>
    private static string? Line(string? title, string? body)
    {
        bool hasTitle = !string.IsNullOrEmpty(title);
        bool hasBody = !string.IsNullOrEmpty(body);
        if (!hasTitle && !hasBody)
            return null;
        if (!hasTitle)
            return $"<color={BodyColor}>{body}</color>";
        if (!hasBody)
            return $"<color={TitleColor}>{title}</color>";
        return $"<color={TitleColor}>{title}</color>: <color={BodyColor}>{body}</color>";
    }

    /// <summary>Format guard: a translation with stray braces must degrade to the raw wording, not
    /// take down the board refresh.</summary>
    private static string Format(string pattern, params object[] args)
    {
        try
        {
            return string.Format(pattern, args);
        }
        catch (System.Exception)
        {
            return pattern;
        }
    }

    /// <summary>The localized display name of an actor (the game's own <c>ActorLocKey</c> path), or
    /// an empty string when it cannot be resolved — a nameless sentence, never a wrong name.</summary>
    private static string ActorName(CActor? actor)
    {
        if (actor == null)
            return string.Empty;
        try
        {
            string key = actor.ActorLocKey();
            return string.IsNullOrEmpty(key) ? string.Empty : Loc.Game(key, key);
        }
        catch (System.Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The name of the COMPANION SUMMON the open take-damage decision is about, when that summon
    /// belongs to <paramref name="summoner"/> — this board's character. Read from the local
    /// <c>TakeDamagePanel</c>, which the game populated on THIS client from the same replicated
    /// damage message (<c>ShowOtherPlayer</c>). Null when the panel holds nothing, holds a different
    /// decision, or its summon belongs to somebody else: the caller then falls back to the plain
    /// summon wording rather than naming an actor from an unrelated prompt.
    /// </summary>
    private static string? CompanionName(CPlayerActor? summoner)
    {
        if (summoner == null)
            return null;
        TakeDamagePanel? panel = Singleton<TakeDamagePanel>.IsInitialized
            ? Singleton<TakeDamagePanel>.Instance
            : null;
        if (panel == null || panel.actorBeingAttacked is not CHeroSummonActor summon)
            return null;
        if (!ReferenceEquals(summon.Summoner, summoner))
            return null;
        string name = ActorName(summon);
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
