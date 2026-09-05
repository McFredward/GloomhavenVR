using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GloomhavenVR.WorldUI;

namespace GloomhavenVR.WireTests;

/// <summary>
/// WHICH QUESTION A MANDATORY-DECISION TERM ANSWERS — the class rule, held where a gate can fail.
///
/// <para><b>THE DEFECT THIS EXISTS FOR, AND IT SHIPPED WITH A COMMIT MESSAGE ASSERTING THE
/// OPPOSITE.</b> <c>ModalFallback.IsMandatoryDecision</c> is a union of two kinds of term. Four are
/// IDENTITY — a named component on the window's own GameObject, each enrolled by reading its
/// decompiled class and finding a waiter with no hide fallback. The fifth is a DERIVED NET,
/// <c>escapeKeyAction == None</c>, which reports that the GAME declines to close the window on ESC
/// and says nothing whatever about what is waiting on it.</para>
///
/// <para>On 2026-09-03 a new caller — <c>StickinessSpentByAnsweredDecision</c>, which releases a
/// map-room float whose stickiness is spent — asked the UNION. Every ordinary map-room destination
/// carries <c>escapeKeyAction None</c>: the merchant and the temple both do, and the mod prints so
/// itself every time it floats one. So the flat game's completely ordinary single-window discipline
/// matched the net and tore the float down. That build's own commit message said <i>"Sticky itself
/// is unchanged, so the merchant and the temple still stand open together"</i>. The two hardware
/// logs of that session disagree: <c>MANDATORY DECISION ANSWERED</c> appears 1 host / 7 remote for
/// the shop and 1 host / 10 remote for the temple, and the user filed both halves of the
/// consequence — windows closing when the map is switched, and no two windows standing open in
/// parallel any more.</para>
///
/// <para><b>WHY A GATE AND NOT A COMMENT.</b> The widening was invisible in review precisely
/// because it was a one-word call to a predicate whose NAME reads as the narrow question. Nothing
/// in the repository could notice; the symptom needed a two-player hardware session and the cause
/// needed the term-naming string in the log line. So the rule is asserted here in the two places it
/// can be broken again:</para>
/// <list type="number">
///   <item>EVERY MEMBER OF <see cref="MandatoryDecisionTerm"/> IS CLASSIFIED BY NAME, and the
///   member list itself is pinned. A sixth term added to the sweep — the third widening — cannot
///   compile past this file until somebody writes down which of the two questions it answers. That
///   is the only moment at which the decision is cheap.</item>
///   <item>A SOURCE LINT ON THE CALLER, because the enum cannot stop someone calling the union
///   again. <c>StickinessSpentByAnsweredDecision</c>'s body must ask
///   <see cref="MandatoryDecisionTerms.IdentifiesTheWindow"/> and must NOT name
///   <c>IsMandatoryDecision</c>.</item>
/// </list>
///
/// <para>The classification is driven against the SHIPPED method — <c>MandatoryDecisionTerm.cs</c>
/// is compiled into this assembly, not copied into it — so a change to the switch is a change to
/// what runs here.</para>
///
/// <para><b>AND THE NARROWING HAD A SECOND HALF, WHICH IS WHY THE LINT NOW COVERS TWO METHODS
/// (ModBuild 447).</b> Narrowing the caller to the four identity terms was right for the merchant
/// and the temple and left the QUEST WINDOW with no release path at all — the ModBuild 444 log
/// counts <c>MANDATORY DECISION ANSWERED</c> 8x for 'UI Quest Popup', the ModBuild 446 log counts
/// it zero times for anything, and the user's next report was <i>"das Quest-Fenster soll komplett
/// verschwinden, wenn keine Quest aktiv ausgewählt ist"</i>. The remedy deliberately did NOT
/// re-widen anything: <c>StickinessSpentByClearedQuestSelection</c> is a SEPARATE predicate asking
/// a different question — is there still a selection for this view to be a view of — so the
/// merchant and the temple are untouched by it. That makes it the obvious place for the third
/// widening, so it is linted by the same rule as its sibling: neither method may ask the union.
/// A term added to <see cref="MandatoryDecisionTerm"/> still has to be classified above, and this
/// file is still the only thing in the repository that can notice.</para>
/// </summary>
internal static class MandatoryDecisionTermVectors
{
    /// <summary>
    /// THE WHOLE TABLE, BY NAME. Left: the term. Right: does it name WHICH WINDOW this is (an
    /// identity), as opposed to reporting the game's ESC policy for it (a derived net)?
    ///
    /// <para>Written out as names rather than derived from the enum, on purpose: a table derived
    /// from the thing it is testing agrees with every future edit, which is the failure this
    /// project keeps paying for. This one has to be EDITED, by hand, by whoever adds a term.</para>
    /// </summary>
    private static readonly (string Name, bool IdentifiesTheWindow)[] Expected =
    {
        // The four IDENTITY terms. A game-side hide of one of these really does mean the player
        // ANSWERED it, because nothing else in the game closes them.
        ("EncounterPanel", true),    // UIEventPanel — the reported 'Begegnung!' window
        ("RewardShowcase", true),    // UIRewardsManager — the unconditional while() spin
        ("ItemCardPicker", true),    // ItemCardPicker — hide fires neither callback
        ("TakeDamagePanel", true),   // TakeDamagePanel — the wire action is never sent

        // The DERIVED NET. It is a fact about the ESC key, and it is carried by every ordinary
        // map-room destination. Reading it as an identity is the 2026-09-03 defect.
        ("GameRefusesEscape", false),

        // And the absence of a match.
        ("None", false),
    };

    private const string CallerRelPath = "src/GloomhavenVR/WorldUI/Modal/ModalFallback.4.Tick.cs";
    private const string CallerMethod = "StickinessSpentByAnsweredDecision";

    /// <summary>
    /// ModBuild 447's sibling predicate, in the same file. It releases a floated quest window when
    /// the map table has settled on deciding no quest, and it must reach that answer WITHOUT the
    /// union: its whole reason to exist is that the union released the merchant and the temple.
    /// It does not ask <c>IdentifiesTheWindow</c> either — it is not a mandatory-decision question
    /// at all — so only the negative half of the lint applies to it.
    /// </summary>
    private const string SelectionCallerMethod = "StickinessSpentByClearedQuestSelection";

    internal static void Run(Harness t, string repoRoot)
    {
        PinTheMembers(t);
        PinTheClassification(t);
        LintTheCaller(t, repoRoot);
    }

    /// <summary>
    /// THE MEMBER LIST ITSELF. A new term, a removed term or a renamed term fails here with the
    /// name in the message, which is the point: the next person to widen the sweep is told, by a
    /// gate and in one line, that a decision is owed.
    /// </summary>
    private static void PinTheMembers(Harness t)
    {
        t.Case("mandatory-decision terms / the member list");

        var actual = new List<string>(Enum.GetNames(typeof(MandatoryDecisionTerm)));
        actual.Sort(StringComparer.Ordinal);
        var expected = new List<string>();
        foreach ((string name, bool _) in Expected)
            expected.Add(name);
        expected.Sort(StringComparer.Ordinal);

        t.Equal(string.Join(",", expected), string.Join(",", actual),
                "MandatoryDecisionTerm's members are exactly the ones classified in this file. A "
                + "term that appears here and not in the table above is a widening of the "
                + "mandatory-decision sweep with nobody having said whether it names a WINDOW (an "
                + "identity, safe for StickinessSpentByAnsweredDecision to spend a float's "
                + "stickiness on) or merely the game's ESC POLICY for it (a derived net, which the "
                + "merchant and the temple both carry). Add it to Expected with its answer.");
    }

    /// <summary>
    /// EVERY MEMBER, AGAINST THE SHIPPED SWITCH. Driven by NAME through
    /// <c>Enum.Parse</c> so the table above and the enum cannot drift apart silently.
    /// </summary>
    private static void PinTheClassification(Harness t)
    {
        t.Case("mandatory-decision terms / identity vs derived net");

        foreach ((string name, bool identity) in Expected)
        {
            if (!Enum.TryParse(name, out MandatoryDecisionTerm term))
                continue; // PinTheMembers has already failed and named it.

            t.Equal(identity, MandatoryDecisionTerms.IdentifiesTheWindow(term),
                    $"MandatoryDecisionTerms.IdentifiesTheWindow({name}) — "
                    + (identity
                        ? "this term names a component on the window's own GameObject, so a "
                          + "game-side hide of it means the player ANSWERED it"
                        : "this term does NOT name the window. Treating it as an identity is what "
                          + "released seventeen merchant and temple floats in the 2026-09-03 "
                          + "session"));
        }

        t.Equal(false, MandatoryDecisionTerms.IsMandatory(MandatoryDecisionTerm.None),
                "None is not a match — the union predicate must answer false for it.");
        t.Equal(true, MandatoryDecisionTerms.IsMandatory(MandatoryDecisionTerm.GameRefusesEscape),
                "The derived net IS still a match for the UNION. It has to be: withholding the X "
                + "from a window the game itself will not close on ESC is the job it was added "
                + "for (ModBuild 381), and narrowing the wrong caller must not narrow that one.");
    }

    /// <summary>
    /// THE CALLER, AS TEXT. The enum cannot stop the union being asked again — that is a call, not
    /// a type — so the one method that must ask the narrow question is read from source and
    /// checked. Scoped to that method's own body: the file has other, legitimate mentions of
    /// <c>IsMandatoryDecision</c> in its doc comments, and a whole-file grep would have to be
    /// weakened until it stopped meaning anything.
    /// </summary>
    private static void LintTheCaller(Harness t, string repoRoot)
    {
        t.Case("mandatory-decision terms / the caller asks the narrow question");

        string abs = Path.Combine(repoRoot, CallerRelPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(abs))
        {
            t.True(false, $"{CallerRelPath} is missing — the source lint could not run at all. If "
                          + "the method moved, move this lint with it rather than deleting it.");
            return;
        }

        string body = MethodBody(File.ReadAllText(abs), CallerMethod);
        if (body.Length == 0)
        {
            t.True(false, $"{CallerMethod} was not found in {CallerRelPath}. It is the method that "
                          + "decides whether a map-room float's stickiness is spent; if it was "
                          + "renamed or moved, update CallerMethod/CallerRelPath here — do not "
                          + "delete this check, it is the only thing holding the class rule.");
            return;
        }

        t.True(body.Contains("IdentifiesTheWindow"),
               $"{CallerMethod} must decide with MandatoryDecisionTerms.IdentifiesTheWindow, i.e. "
               + "with the four IDENTITY terms only.");
        t.True(!body.Contains("IsMandatoryDecision"),
               $"{CallerMethod} must NOT ask IsMandatoryDecision. That predicate is the UNION, and "
               + "its last term is the derived net `escapeKeyAction == None` which every ordinary "
               + "map-room destination carries — asking it here is exactly the 2026-09-03 "
               + "regression: the merchant and the temple were released by the flat game's "
               + "ordinary single-window hide and by a peer's map switch.");

        // ModBuild 447 — THE SIBLING PREDICATE, held to the same negative rule. It is the natural
        // place for the next widening precisely because it is the one that got the quest window
        // released again, and "just ask the union here instead" would put the merchant and the
        // temple straight back into the 2026-09-03 failure by a different door.
        string selectionBody = MethodBody(File.ReadAllText(abs), SelectionCallerMethod);
        t.True(selectionBody.Length > 0,
               $"{SelectionCallerMethod} was not found in {CallerRelPath}. It is the predicate that "
               + "gives up a floated quest window once the map table has settled on deciding no "
               + "quest — the ModBuild 447 remedy for 'das Quest-Fenster soll komplett "
               + "verschwinden, wenn keine Quest aktiv ausgewählt ist'. If it was renamed or moved, "
               + "update SelectionCallerMethod here rather than deleting this check.");
        t.True(!selectionBody.Contains("IsMandatoryDecision"),
               $"{SelectionCallerMethod} must NOT ask IsMandatoryDecision either. It exists BECAUSE "
               + "the union released the merchant and the temple; reaching for the union inside the "
               + "very predicate that repaired that narrowing would restore the defect through a "
               + "second door and with a name that reads as the narrow question.");
    }

    /// <summary>
    /// The text between a method's opening brace and its matching close. Brace-counting rather
    /// than a regex, because the body contains braces (interpolated strings and a block) and a
    /// lazy match would stop at the first one and pass a lint it never actually ran.
    /// </summary>
    private static string MethodBody(string source, string method)
    {
        Match decl = Regex.Match(source, @"\b" + Regex.Escape(method) + @"\s*\([^)]*\)\s*\{");
        if (!decl.Success)
            return string.Empty;
        int open = decl.Index + decl.Length - 1;
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return source.Substring(open, i - open + 1);
            }
        }
        return string.Empty;
    }
}
