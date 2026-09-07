using System.IO;
using System.Text.RegularExpressions;

namespace GloomhavenVR.WireTests;

/// <summary>
/// TWO QUESTIONS THAT MUST NEVER BECOME ONE PREDICATE AGAIN — "may a peer SEE this card's face"
/// and "may a prompt NAME this card in words". A source lint, because the thing that can break the
/// rule is a CALL and not a type, and because the only symptom is a card identity on the wire
/// during the game's own secret window.
///
/// <para><b>THE LEAK THIS HOLDS SHUT.</b> ModBuild 477 item 7: the mirrored decision row published
/// <c>confirm='Verbrennen "Zusatzdolch"'</c> to every peer while
/// <c>RevealGate.PeersSeeOurCardFronts</c> was SHUT, and <c>SpareDagger</c> was sitting in that
/// character's <c>DiscardedAbilityCards</c> at the time. It was fixed in 478 by
/// <c>Net.DecisionLabelMask</c>, whose <c>AddCovered</c> collects the names of every covered card —
/// and which SKIPS every card <c>RevealGate.PeersMayNameOurCard</c> permits.</para>
///
/// <para><b>WHY IT IS FRAGILE, AND WHY A GATE RATHER THAN A COMMENT.</b> On 2026-09-07 the user
/// ruled that a peer's DISCARD fan shows FRONTS in every phase, including that same secret window
/// ("Die Fächer der piles werden also ab jetzt immer mit Vorderseiten gezeigt ohne Ausnahme"). The
/// obvious one-line implementation is to widen the card-public predicate the mask reads — and that
/// widening would stop the mask masking the very card a short-rest prompt names, because
/// <c>CardsHandUI.PerformShortRest</c> picks the sacrifice out of <c>DiscardedAbilityCards</c> and
/// REMOVES NOTHING, so it is a discard-pile card for the whole time the prompt is up. The two
/// questions are compatible in SUBSTANCE and only in substance: seeing every card in a peer's
/// discard fan says nothing about WHICH of them the game has singled out, and that selection is the
/// secret. Nothing in the type system can notice the difference; this file can.</para>
///
/// <para><b>THE THIRD RULE, AND IT IS THE ONE A FUTURE ROUND WILL BREAK.</b> The discard exemption
/// on the FACE side is scoped by <c>RevealGate.PileFrontsReach</c>, which refuses it to the two
/// populations that are a decision in flight — the short-rest sacrifice's recess and the modal
/// pick field. Without that term, "a discarded card's face is public" draws the sacrifice face-up
/// inside the secret window and reverses a ruling the user has stated three times ("Kurze Rast =
/// Auswahlphase = verdeckt"). It is pinned below by name.</para>
///
/// <para>Everything here is read from the SHIPPED source text under <c>src/</c>, method body by
/// method body, so a rename fails loudly with the method named rather than passing vacuously.</para>
/// </summary>
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

    /// <summary>
    /// THE SCOPE OF THE DISCARD EXEMPTION ON THE FACE SIDE. Two populations are a decision in
    /// flight and the ruling does not reach them; the term that says so is pinned by the names it
    /// must refuse, so deleting either one fails here with the ruling quoted.
    /// </summary>
    private static void LintThePileScope(Harness t, string gate)
    {
        t.Case("card identity / the pile ruling does not reach a decision in flight");

        if (gate.Length == 0)
            return;   // already reported by LintTheNamingPredicate

        Match expr = Regex.Match(
            gate, @"bool\s+PileFrontsReach\s*\([^)]*\)\s*=>(?<body>[^;]*);");
        t.True(expr.Success,
               "RevealGate.PileFrontsReach was not found. It is the ONE term keeping the "
               + "2026-09-07 evening ruling ('Die Fächer der piles … immer mit Vorderseiten … ohne "
               + "Ausnahme') from colliding with the 2026-09-07 afternoon ruling ('Kurze Rast = "
               + "Auswahlphase = verdeckt') on the same card. Without it the short-rest sacrifice "
               + "draws face-up inside the secret window, because it IS a discard-pile card.");
        if (!expr.Success)
            return;

        string body = expr.Groups["body"].Value;
        t.True(body.Contains("SacrificedCard"),
               "PileFrontsReach must refuse PeerCardPopulation.SacrificedCard. That recess holds a "
               + "card the owner may still re-draw — CardsHandUI.PerformShortRest removes nothing "
               + "and only FinalizeShortRest moves it — so it is a decision in flight and stays "
               + "covered until the accept commits it, at which point the BURN exception (a "
               + "different predicate) opens it.");
        t.True(body.Contains("BoardPickSeat"),
               "PileFrontsReach must refuse PeerCardPopulation.BoardPickSeat. A card laid in a "
               + "recess by a modal pick is the same shape of decision in flight, and the seat that "
               + "travels may name the DISCARD arc — so an unscoped discard exemption reaches it.");

        // The exempt side of IsPublicPopulation, pinned by what it may NOT contain. It is the other
        // half of the same rule: a PLACE must never be added there, and the two places above are
        // exactly the ones a future round would reach for.
        Match pub = Regex.Match(
            gate, @"bool\s+IsPublicPopulation\s*\([^)]*\)\s*=>(?<body>[^;]*);");
        t.True(pub.Success, "RevealGate.IsPublicPopulation was not found as an expression.");
        if (!pub.Success)
            return;

        string pubBody = pub.Groups["body"].Value;
        t.True(!pubBody.Contains("SacrificedCard") && !pubBody.Contains("BoardPickSeat")
               && !pubBody.Contains("PickFan") && !pubBody.Contains("Selectable"),
               "IsPublicPopulation's exempt side must not name SacrificedCard, BoardPickSeat, "
               + "PickFan or Selectable. Those are PLACES (or the whole secret population), and a "
               + "place rule on this expression is what showed the user a front in a short rest. A "
               + "member may only be added here when the thing is public because of WHAT IT IS — "
               + "the active matrix, an item, a decision row's wording — and never because of where "
               + "it happens to be lying.");
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
