using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>Guard the separate sender naming policy and the MB487 face policy against source
/// regressions. A model-public name does not grant a selection-phase face. Tests inspect method
/// bodies with comments stripped so historical rationale cannot satisfy a live-code assertion.</summary>
internal static class CardIdentityMaskVectors
{
    private const string GateRelPath = "src/GloomhavenVR/Net/RevealGate.cs";
    private const string MaskRelPath = "src/GloomhavenVR/Net/DecisionLabelMask.cs";

    internal static void Run(Harness t, string repoRoot)
    {
        string gate = ReadOrEmpty(repoRoot, GateRelPath);
        string mask = ReadOrEmpty(repoRoot, MaskRelPath);

        LintTheMask(t, mask);
        LintTheNamingPredicate(t, gate);
        LintThePileScope(t, gate);
    }

    /// <summary>
    /// THE MASK ASKS THE NAMING PREDICATE AND NOTHING ELSE. Scoped to <c>AddCovered</c>'s own body:
    /// the file legitimately discusses the face side in its doc comments and a whole-file grep
    /// would have to be weakened until it stopped meaning anything.
    /// </summary>
    private static void LintTheMask(Harness t, string mask)
    {
        t.Case("card identity / the decision-label mask asks the NAMING predicate");

        if (mask.Length == 0)
        {
            t.True(false, MaskRelPath + " is missing — the source lint could not run at all. If the "
                          + "class moved, move this lint with it rather than deleting it: it is the "
                          + "only thing in the repository that can notice the ModBuild 477 item 7 "
                          + "leak coming back.");
            return;
        }

        // COMMENTS STRIPPED FIRST, AND THAT IS NOT A CONVENIENCE. AddCovered's body carries a long
        // note NAMING the predicates it must not ask — written there precisely so the next reader
        // sees the trap at the call site — and a raw text search finds those mentions and fails on
        // the documentation of the rule it is enforcing. (It did, on the first run of this lint.)
        // What is being asserted is a CALL, so only code counts.
        string body = StripComments(MethodBody(mask, "AddCovered"));
        t.True(body.Length > 0,
               "DecisionLabelMask.AddCovered was not found. It is the method that decides which of "
               + "our card names are withheld from a peer-bound wording; if it was renamed, update "
               + "this lint rather than deleting it.");
        if (body.Length == 0)
            return;

        t.True(body.Contains("PeersMayNameOurCard"),
               "AddCovered must skip a card through RevealGate.PeersMayNameOurCard — the NAMING "
               + "question. It is deliberately narrower than the face question and must stay so.");

        foreach (string forbidden in new[] { "IsDiscardedCard", "CardFaces", "IsPublicPopulation" })
        {
            t.True(!body.Contains(forbidden),
                   "AddCovered must NOT ask RevealGate." + forbidden + ". That is the FACE side, "
                   + "which the 2026-09-07 pile-fan ruling widened to the discard pile — and the "
                   + "short-rest sacrifice is a discard-pile card while the prompt that NAMES it is "
                   + "on screen (CardsHandUI.PerformShortRest indexes DiscardedAbilityCards and "
                   + "removes nothing). Asking it here re-opens the ModBuild 477 item 7 leak: "
                   + "confirm='Verbrennen \"Zusatzdolch\"' published to every peer inside the "
                   + "secret window.");
        }
    }

    /// <summary>
    /// THE NAMING PREDICATE ITSELF, AS TEXT. Its whole value is that it did NOT follow the face
    /// side when the face side widened, so the expression is pinned rather than trusted.
    /// </summary>
    private static void LintTheNamingPredicate(Harness t, string gate)
    {
        t.Case("card identity / PeersMayNameOurCard did not follow the face side");

        if (gate.Length == 0)
        {
            t.True(false, GateRelPath + " is missing — the source lint could not run at all.");
            return;
        }

        Match expr = Regex.Match(
            gate, @"bool\s+PeersMayNameOurCard\s*\([^)]*\)\s*=>(?<body>[^;]*);");
        t.True(expr.Success,
               "RevealGate.PeersMayNameOurCard was not found as an expression-bodied predicate. It "
               + "is the owner-seat NAMING gate — the one DecisionLabelMask.AddCovered reads and "
               + "the one the two tooltip surfaces read. If it was renamed or given a block body, "
               + "update this lint; do not delete it.");
        if (!expr.Success)
            return;

        string body = expr.Groups["body"].Value;
        t.True(body.Contains("PeersSeeOurCardFronts"),
               "PeersMayNameOurCard must keep the PHASE term.");
        t.True(body.Contains("IsPubliclyRevealedCard"),
               "PeersMayNameOurCard must keep the burn/active exception — a card its owner has "
               + "already destroyed or played face-up may be named, and the user's ruling for the "
               + "burn is the strongest he has given any face.");
        t.True(!body.Contains("IsDiscardedCard"),
               "PeersMayNameOurCard must NOT read RevealGate.IsDiscardedCard. That is the pile-fan "
               + "ruling and it belongs to the FACE side only: a peer may SEE every card in our "
               + "discard fan and must still not be TOLD which one the short rest singled out.");
    }

    /// <summary>MB487 removes historical face exemptions while retaining naming isolation.</summary>
    private static void LintThePileScope(Harness t, string gate)
    {
        t.Case("card identity / every card artwork population follows the phase");
        if (gate.Length == 0) return;
        Match reach = Regex.Match(gate, @"bool\s+PileFrontsReach\s*\([^)]*\)\s*=>(?<body>[^;]*);");
        t.True(reach.Success, "historical pile face seam remains explicit");
        if (!reach.Success) return;
        string body = reach.Groups["body"].Value.Trim();
        t.True(body == "false", "discard membership cannot reopen a covered selection phase");
        t.True(!body.Contains("SacrificedCard") && !body.Contains("BoardPickSeat"),
            "phase coverage has no surface-specific exceptions");
        Match pub = Regex.Match(gate, @"bool\s+IsPublicPopulation\s*\([^)]*\)\s*=>(?<body>[^;]*);");
        t.True(pub.Success, "historical public population seam remains explicit");
        if (pub.Success)
            t.True(pub.Groups["body"].Value.Trim() == "false", "active, lost and item artwork cannot bypass phase coverage");
    }

    /// <summary>
    /// Drop <c>//</c> line comments and <c>/* */</c> block comments. Deliberately crude — it does
    /// not understand string literals, and it does not need to: every check above is "does this
    /// method CALL X", and no string literal in these two files spells a predicate name followed by
    /// an open paren. What it must not do is let a comment satisfy or break a code assertion, which
    /// is the failure it was written for.
    /// </summary>
    private static string StripComments(string source)
    {
        if (source.Length == 0)
            return source;
        string noBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(noBlocks, @"//[^\n]*", " ");
    }

    private static string ReadOrEmpty(string repoRoot, string relPath)
    {
        string abs = Path.Combine(repoRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(abs) ? File.ReadAllText(abs) : string.Empty;
    }

    /// <summary>
    /// The text between a method's opening brace and its matching close — brace-counting rather
    /// than a regex, because the body carries braces of its own and a lazy match would stop at the
    /// first one and pass a lint it never actually ran. (Same helper, same reason, as
    /// <see cref="MandatoryDecisionTermVectors"/>; it is six lines and duplicating it beats making
    /// two unrelated vector files depend on each other.)
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
