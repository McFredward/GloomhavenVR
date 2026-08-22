using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// THE TWO ACTION HALVES OF A CARD FACE, AND WHY A MOD-BUILT COPY OF ONE USED TO BE GREYER THAN
/// THE GAME'S OWN.
///
/// <para>USER REPORT (2026-08-22, translated): <i>"The hand cards in the fan have a GREYER colour in
/// the SELECTABLE AREAS. I've just noticed that in the card OVERLAYS this is not the case.
/// Independently of just the world map, check this generally and make sure the cards are really
/// displayed with all their colours the way they are in the game too. There should therefore be no
/// divergence between the card overlays and the hand cards in how they look."</i></para>
///
/// <para>THE "SELECTABLE AREAS" ARE A GAME COMPONENT WITH A GAME DIMMER ON IT. An ability face is
/// <c>FullAbilityCard</c> + two <c>FullAbilityCardAction</c>s (<c>topActionButton</c>,
/// <c>bottomActionButton</c>). Each of those owns a serialized <c>CanvasGroup</c> over its
/// <c>cardContentTransform</c>, and the game has exactly ONE writer for it:</para>
/// <code>
///   FullAbilityCardAction.SetInteractable(bool active, bool defaultAction)   // :322
///       if (canvasGroup != null &amp;&amp; !defaultAction)
///           canvasGroup.alpha = (active ? 1f : 0.5f);                        // :334
///       actionButton.interactable = active;  // + infusion / enhancement elements Enable/Disable
/// </code>
/// <para>So "greyer, and only in the selectable areas" is not a shader, not a tint, not the mod's
/// lighting and not a colour space: it is <b>alpha 0.5 on the half's own CanvasGroup</b> (the card
/// header, title and initiative disc sit OUTSIDE that group, which is exactly why they stayed
/// correct), plus the disabled ColorBlock on <c>actionButton</c> whenever that Selectable is
/// enabled. Note also that nothing ever puts the alpha back on its own:
/// <c>FullAbilityCardAction.ResetInteractable()</c> (:382, the call
/// <c>AbilityCardUI.OnReturnedToPool</c> makes) clears the BOOLEANS and leaves the CanvasGroup at
/// whatever the last user left. A pooled widget therefore carries its dimming forward.</para>
///
/// <para>WHAT GATES THE OVERLAY, AND WHAT THE FAN WAS MISSING. Both surfaces the user compared are
/// built out of the SAME pool of <c>AbilityCardUI</c> widgets — the difference is one call:</para>
/// <list type="bullet">
/// <item><description>OVERLAY (the hover preview over a converted window; the map room's ability
/// list is <c>UIPartyCharacterAbilityCardsDisplay</c>, which spawns its rows with
/// <c>AbilityCardUI.Init(card, fullCardHolder, characterID, …)</c> — UIPartyCharacterAbilityCardsDisplay.cs:280/412).
/// That <c>Init</c> runs <c>SetMode(CardHandMode.DeckSelection, {Any})</c>, and the
/// <c>DeckSelection</c> arm of <c>AbilityCardUI.SetMode</c> calls
/// <c>fullAbilityCard.SetInteractable(active: true)</c> (AbilityCardUI.cs:929) — <b>alpha 1 on both
/// halves, both action buttons interactable, infusion and enhancement elements enabled.</b></description></item>
/// <item><description>FAN (the map-room hand, and every other mod surface that prints a REAL card
/// front without owning the game's live widget). Its face is a throwaway <c>Object.Instantiate</c>
/// of a widget BORROWED from the game's pool — <c>Net.RemoteAbilityCardSource.TryPooledClone</c> →
/// <c>AbilityCardUI.Init(card, disableEventDetection: true)</c> → <c>SetMode(CardHandMode.Preview,
/// null)</c>, and the <c>Preview</c> arm does <c>ToggleFullCard(active: true)</c> and <b>nothing
/// else</b> (AbilityCardUI.cs:951-956). <c>SetInteractable</c> is never called on that path, so the
/// clone inherits whatever dimming the pooled template happened to carry. The ModBuild-195 hardware
/// log confirms the path in one line: <c>[Net] Remote card FACE path = PooledBorrow</c> immediately
/// followed by <c>[MapRoom] MAP-ROOM HAND FACES: 10 of 10 …(pooled borrow)</c>.</description></item>
/// </list>
///
/// <para>THE FIX IS THE GATE, NOT THE LOOK. This class does not tune a colour until it matches: it
/// runs <b>the game's own <c>FullAbilityCard.SetInteractable(true)</c></b> on the mod's clone — the
/// identical call <c>SetMode(DeckSelection)</c> makes for the overlay — so the fan takes the same
/// route rather than an imitation of its result. Everything that call touches (both half
/// CanvasGroups, both action Buttons, the default-action Buttons, <c>InfuseElement</c>s and the
/// enhancement infusions) lands the same way on both surfaces, including anything a future game
/// patch adds to it.</para>
///
/// <para>IT IS ONLY EVER APPLIED TO AN OBJECT THE MOD OWNS OUTRIGHT, and that is checked twice: the
/// face must not be registered as an ADOPTED live widget (<see cref="CardArtGuard.IsAdopted"/>) and
/// it must have no <c>AbilityCardUI</c> anywhere in its parent chain — the parent
/// <c>ObjectPool.RecycleCard</c> re-attaches every game-owned face to (ObjectPool.cs:545-547). A
/// clone satisfies both; a live widget can satisfy neither. Nothing is written to a face the game
/// is holding, so no restore contract is created and there is no write war to lose.</para>
///
/// <para>WHY THE ADOPTED (SCENARIO) FAN IS DELIBERATELY NOT WRITTEN TO. A card in the scenario hand
/// fan is the game's REAL <c>FullAbilityCard</c>, re-parented by <see cref="CardFace.Adopt"/> and
/// left in the game's own state. Whatever dimming it shows is the dimming the flat game shows for
/// that same widget in that same moment — the halves of a played card on the control board go to
/// alpha 0.5 through <c>CardsActionControlller.Finish()</c> (CardsActionControlller.cs:422-423) and
/// that is real information about what you have already played. There is nothing to correct there
/// and correcting it would DELETE a game cue; the divergence only exists where the mod manufactures
/// a face of its own. The census below measures that population anyway and prints its numbers, so
/// the claim is a reading and not an assumption.</para>
///
/// <para>THE CENSUS. <see cref="RunCensus"/> samples every <c>FullAbilityCard</c> in the scene —
/// inactive ones included, because a window row's preview is only active while hovered — splits
/// them into GAME-LIVE (the overlay population: a face parented under an <c>AbilityCardUI</c> that
/// is in a UI, i.e. one an <c>Init</c>/<c>SetMode</c> has run on), GAME-POOL (parked under
/// <c>ObjectPool</c>, which is precisely the object a borrowed clone is copied FROM and must not be
/// blurred into the reference), ADOPTED (the scenario fan/tray) and CLONE (every mod-built copy),
/// and prints the measured half state of each population in ONE line: the CanvasGroup alpha RANGE
/// (min…max, never a single sample — a mode is not a distribution), how many halves report
/// <c>interactable</c>, the actual rendered tint the Selectable left on the action image
/// (<c>CanvasRenderer.GetColor()</c>, i.e. the number the eye sees) and the shader the half's image
/// draws with. It always prints the number of faces compared, so "the populations agree" and "the
/// census never ran" can never look alike.</para>
/// </summary>
internal static class CardHalfTone
{
    private const string Scope = "Cards";

    /// <summary>Seconds between two censuses. The sweep is a whole-scene component scan, so it is
    /// cadenced hard; it only runs at all while the mod is pumping card faces (it is driven from
    /// <see cref="CardFace.Offer"/>), which is exactly when the comparison means anything.</summary>
    private const float CensusIntervalSeconds = 10f;

    /// <summary>Unscaled time of the next census.</summary>
    private static float s_nextCensus;

    /// <summary>Last verdict string printed — the line repeats only when the numbers change.</summary>
    private static string? s_lastVerdict;

    /// <summary>Censuses run so far, so a repeat-suppressed line can still be dated.</summary>
    private static int s_censuses;

    /// <summary>Clones normalised this session, for the verdict line.</summary>
    private static int s_normalizedCount;

    /// <summary>True once the "what this fixed" line has been printed.</summary>
    private static bool s_fixLogged;

    /// <summary>True once a refused <c>SetInteractable</c> has been reported.</summary>
    private static bool s_refusalLogged;

    private static bool s_errorLogged;

    /// <summary>
    /// Offer one ability face. Called from <see cref="CardFace.Offer"/> — the single per-face pump
    /// BOTH card paths already run (the adopted local face through
    /// <c>CardFace.Adopt</c>/<c>MaintainArtArrival</c>, a mod clone through
    /// <c>Net.RemoteCardArt</c>'s build-time and cadenced rescans) — so no new update loop exists to
    /// fall out of step with that one.
    /// </summary>
    internal static void Observe(FullAbilityCard? face)
    {
        if (face == null)
            return;
        try
        {
            if (IsModOwnedCopy(face) && NeedsCorrection(face))
                Normalize(face);
            MaybeCensus();
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("observe", ex);
        }
    }

    /// <summary>Drop all state (scene teardown / hot reload), mirroring <c>CardArtGuard.Reset</c>.</summary>
    internal static void Reset()
    {
        s_nextCensus = 0f;
        s_lastVerdict = null;
    }

    // ------------------------------------------------------------------- the gate --

    /// <summary>
    /// Is this clone's half state still different from the overlay's? The write is CHANGE-GATED
    /// rather than remembered-once, which is what keeps it free of per-clone bookkeeping (a set of
    /// instance ids would grow for the whole session — the map-room fan rebuilds its fronts) and
    /// what makes it safe to run on the face pump: once corrected the test is false and nothing is
    /// written again, so this can never become a per-frame write war.
    ///
    /// <para>The rendered-tint half of the test is asked ONLY of a DISABLED Selectable. An enabled
    /// one is driven by Unity's own colour transition every state change, so testing its
    /// <c>CanvasRenderer</c> colour against white would be a permanent disagreement with the engine
    /// — the exact shape of a write war this project has already paid for once. A disabled
    /// Selectable is driven by nobody, so one write closes the gate for good.</para>
    /// </summary>
    private static bool NeedsCorrection(FullAbilityCard face) =>
        NeedsCorrection(face.topActionButton) || NeedsCorrection(face.bottomActionButton);

    private static bool NeedsCorrection(FullAbilityCardAction? half)
    {
        if (half == null)
            return false;
        if (half.canvasGroup != null && half.canvasGroup.alpha < 0.999f)
            return true;
        return NeedsCorrection(half.actionButton) || NeedsCorrection(half.defaultActionButton);
    }

    private static bool NeedsCorrection(Selectable? selectable)
    {
        if (selectable == null)
            return false;
        if (!selectable.interactable)
            return true;
        if (selectable.enabled)
            return false;   // Unity owns this one's tint — never argue with it
        Graphic? target = selectable.targetGraphic;
        if (target == null || target.canvasRenderer == null)
            return false;
        Color c = target.canvasRenderer.GetColor();
        return c.r < 0.999f || c.g < 0.999f || c.b < 0.999f || c.a < 0.999f;
    }

    // ---------------------------------------------------------------- ownership --

    /// <summary>
    /// True only for a face the mod built for itself and nobody else can be holding: NOT a
    /// registered adopted live widget, and with no <c>AbilityCardUI</c> anywhere above it. The
    /// second half of the test is the game's own invariant, not a guess — <c>ObjectPool.RecycleCard</c>
    /// re-parents every game-owned <c>fullAbilityCard</c> back under its row
    /// (<c>fullAbilityCard.transform.SetParent(component.transform)</c>, ObjectPool.cs:545-547), and
    /// the only faces that escape that parent are the ones <see cref="CardFace.Adopt"/> re-hosted,
    /// which the first half already excludes.
    /// </summary>
    private static bool IsModOwnedCopy(FullAbilityCard face)
    {
        if (CardArtGuard.IsAdopted(face))
            return false;
        // includeInactive: a clone is configured under an INACTIVE host, which is the one moment a
        // correction lands before a first drawn frame.
        return face.GetComponentInParent<AbilityCardUI>(includeInactive: true) == null;
    }

    // ------------------------------------------------------------- the same gate --

    /// <summary>
    /// Put a mod-owned clone in the state the OVERLAY is put in, by making the overlay's call.
    /// <c>FullAbilityCard.SetInteractable(true)</c> is what <c>SetMode(CardHandMode.DeckSelection)</c>
    /// runs (AbilityCardUI.cs:929); running it here is the whole fix.
    ///
    /// <para>The trailing CanvasRenderer write is not a second opinion about the colour — it is the
    /// state Unity's own <c>Selectable.InstantClearState()</c> leaves behind (white = NO tint), and
    /// it is needed because the borrowed source runs <c>DisableEventDetection(true)</c>
    /// (<c>actionButton.enabled = false</c>, AbilityCardUI.cs:584-588). A DISABLED Selectable never
    /// re-runs its colour transition, so flipping <c>interactable</c> alone would leave a cloned
    /// disabled-grey tint frozen on the action image. On an ENABLED Selectable this is a no-op that
    /// Unity overwrites with its own normal colour on the next transition.</para>
    /// </summary>
    private static void Normalize(FullAbilityCard face)
    {
        // READ IT BEFORE WRITING IT. Once the correction lands, every later census reports the
        // CORRECTED value — so the number that PROVES the divergence only exists in this instant.
        // It is kept as a running range over every clone corrected this session (a range, not the
        // one sample that happened to be first: a mode is not a distribution).
        NoteBefore(face.topActionButton);
        NoteBefore(face.bottomActionButton);
        string before = $"halfCanvasGroup.alpha {s_beforeMinAlpha:F2}..{s_beforeMaxAlpha:F2}, " +
                        $"{s_beforeNonInteractable} of {s_beforeHalves} half/halves reported " +
                        "actionButton.interactable = false";

        bool viaGameCall = false;
        try
        {
            face.SetInteractable(active: true);
            viaGameCall = true;
        }
        catch (System.Exception ex)
        {
            // The game call reaches InfuseElement / CardEnhancementElements collections; a clone
            // that lost one of them must still come out un-dimmed, so fall back to the one value
            // that IS the reported symptom.
            if (!s_refusalLogged)
            {
                s_refusalLogged = true;
                VRLog.Debug(Scope, $"CARD HALF TONE: FullAbilityCard.SetInteractable(true) refused on a mod " +
                                   $"clone ({ex.GetType().Name}: {ex.Message}) — writing the half CanvasGroups " +
                                   "directly. If the census keeps reporting a rising 'corrections' count, THIS " +
                                   "is why the gate never closes.");
            }
            ForceHalfAlpha(face.topActionButton);
            ForceHalfAlpha(face.bottomActionButton);
        }

        ClearTint(face.topActionButton);
        ClearTint(face.bottomActionButton);
        s_normalizedCount++;

        if (s_fixLogged)
            return;
        s_fixLogged = true;
        VRLog.Info(Scope, $"CARD HALF TONE: first mod-built card front normalised — MEASURED BEFORE THE " +
                          $"WRITE: {before}" +
                          (viaGameCall ? "; corrected through the game's own FullAbilityCard.SetInteractable(true)"
                                       : "; corrected through the CanvasGroup fallback (the game call refused)") +
                          " — the SAME call AbilityCardUI.SetMode(DeckSelection) makes for the hover " +
                          "OVERLAY (AbilityCardUI.cs:929). Without it a pooled-borrow clone keeps the " +
                          "template's leftover FullAbilityCardAction.canvasGroup.alpha (0.5 = the " +
                          "reported grey in the two selectable areas) because ResetInteractable() " +
                          "clears the booleans and never the alpha. Adopted LIVE faces are never " +
                          "written to — read the CARD HALF TONE CENSUS line for their numbers.");
    }

    /// <summary>Running pre-correction range over every clone half this session.</summary>
    private static float s_beforeMinAlpha = float.PositiveInfinity;
    private static float s_beforeMaxAlpha = float.NegativeInfinity;
    private static int s_beforeHalves;
    private static int s_beforeNonInteractable;

    private static void NoteBefore(FullAbilityCardAction? half)
    {
        if (half == null)
            return;
        s_beforeHalves++;
        float alpha = half.canvasGroup != null ? half.canvasGroup.alpha : 1f;
        if (alpha < s_beforeMinAlpha) s_beforeMinAlpha = alpha;
        if (alpha > s_beforeMaxAlpha) s_beforeMaxAlpha = alpha;
        if (half.actionButton != null && !half.actionButton.interactable)
            s_beforeNonInteractable++;
    }

    private static void ForceHalfAlpha(FullAbilityCardAction? half)
    {
        if (half == null || half.canvasGroup == null)
            return;
        half.canvasGroup.alpha = 1f;
    }

    private static void ClearTint(FullAbilityCardAction? half)
    {
        if (half == null)
            return;
        ClearTint(half.actionButton);
        ClearTint(half.defaultActionButton);
    }

    private static void ClearTint(Selectable? selectable)
    {
        // Only for a Selectable Unity is NOT driving — see NeedsCorrection. An enabled one gets its
        // normal colour from the engine's own transition the moment interactable flips.
        if (selectable == null || selectable.enabled)
            return;
        Graphic? target = selectable.targetGraphic;
        if (target != null && target.canvasRenderer != null)
            target.canvasRenderer.SetColor(Color.white);
    }

    // -------------------------------------------------------------- the measurement --

    /// <summary>Accumulated half-state readings for one population.</summary>
    private struct Bucket
    {
        public int Faces;
        public int Halves;
        public float MinAlpha;
        public float MaxAlpha;
        public float SumAlpha;
        public int Interactable;
        public int ButtonsEnabled;
        public float MinTint;
        public float MaxTint;
        public float SumTint;
        public string? Shader;
        public bool MixedShaders;

        public void Seed()
        {
            MinAlpha = float.PositiveInfinity;
            MaxAlpha = float.NegativeInfinity;
            MinTint = float.PositiveInfinity;
            MaxTint = float.NegativeInfinity;
        }

        public void AddHalf(float alpha, bool interactable, bool buttonEnabled, float tint, string? shader)
        {
            Halves++;
            if (alpha < MinAlpha) MinAlpha = alpha;
            if (alpha > MaxAlpha) MaxAlpha = alpha;
            SumAlpha += alpha;
            if (interactable) Interactable++;
            if (buttonEnabled) ButtonsEnabled++;
            if (tint < MinTint) MinTint = tint;
            if (tint > MaxTint) MaxTint = tint;
            SumTint += tint;
            if (shader == null)
                return;
            if (Shader == null) Shader = shader;
            else if (Shader != shader) MixedShaders = true;
        }

        public readonly string Describe(string name)
        {
            if (Halves == 0)
                return $"{name} {Faces} face(s), no readable half";
            return $"{name} {Faces} face(s)/{Halves} half/halves: " +
                   $"halfCanvasGroup.alpha {MinAlpha:F2}..{MaxAlpha:F2} (avg {SumAlpha / Halves:F2}), " +
                   $"interactable {Interactable}/{Halves}, buttonEnabled {ButtonsEnabled}/{Halves}, " +
                   $"renderedTint {MinTint:F2}..{MaxTint:F2} (avg {SumTint / Halves:F2}), " +
                   $"shader '{Shader ?? "n/a"}'{(MixedShaders ? " (MIXED)" : string.Empty)}";
        }
    }

    private static void MaybeCensus()
    {
        if (Time.unscaledTime < s_nextCensus)
            return;
        s_nextCensus = Time.unscaledTime + CensusIntervalSeconds;
        RunCensus();
    }

    /// <summary>
    /// One whole-scene reading of every ability face, split into the three populations, printed as
    /// one line. <c>Resources.FindObjectsOfTypeAll</c> rather than <c>FindObjectsOfType</c> on
    /// purpose: a window row's full-card preview is only ACTIVE while the pointer is on it, and the
    /// overlay's half state is written by <c>Init</c>/<c>SetMode</c> long before that — so the
    /// reference population would otherwise be invisible to the census in almost every frame it
    /// runs. Assets and prefabs are skipped (<c>gameObject.scene.IsValid()</c>), which also keeps the
    /// pool TEMPLATE instance — the object a clone is copied from — out of the comparison unless it
    /// really is in the scene.
    /// </summary>
    private static void RunCensus()
    {
        FullAbilityCard[] all;
        try
        {
            all = Resources.FindObjectsOfTypeAll<FullAbilityCard>();
        }
        catch (System.Exception ex)
        {
            LogErrorOnce("census sweep", ex);
            return;
        }

        Bucket game = default, adopted = default, clone = default, pool = default;
        game.Seed();
        adopted.Seed();
        clone.Seed();
        pool.Seed();
        int skipped = 0;

        for (int i = 0; i < all.Length; i++)
        {
            FullAbilityCard face = all[i];
            if (face == null || !face.gameObject.scene.IsValid())
            {
                skipped++;
                continue;
            }
            try
            {
                bool isAdopted = CardArtGuard.IsAdopted(face);
                bool owned = !isAdopted
                             && face.GetComponentInParent<AbilityCardUI>(includeInactive: true) == null;
                // A widget PARKED IN THE POOL is game-owned but is nobody's overlay: it has been
                // through no Init/SetMode since it was handed back, and OnReturnedToPool's
                // ResetInteractable() clears the booleans without restoring the alpha. It is
                // therefore the exact object a pooled-borrow clone is copied FROM, and mixing it
                // into the overlay's reference population would blur the very comparison this
                // census exists to make. Its own numbers are printed instead.
                bool pooled = !isAdopted && !owned
                              && face.GetComponentInParent<ObjectPool>(includeInactive: true) != null;
                ref Bucket bucket = ref (isAdopted
                    ? ref adopted
                    : ref (owned ? ref clone : ref (pooled ? ref pool : ref game)));
                bucket.Faces++;
                Sample(ref bucket, face.topActionButton);
                Sample(ref bucket, face.bottomActionButton);
            }
            catch (System.Exception ex)
            {
                skipped++;
                LogErrorOnce("census sample", ex);
            }
        }

        s_censuses++;
        int compared = game.Faces + adopted.Faces + clone.Faces + pool.Faces;
        string verdict = Verdict(game, adopted, clone);
        string line = $"CARD HALF TONE CENSUS #{s_censuses}: compared {compared} ability face(s) " +
                      $"({skipped} skipped as assets/unreadable); " +
                      $"{game.Describe("GAME-LIVE(the overlay's population)")}; " +
                      $"{pool.Describe("GAME-POOL(parked, = what a clone is copied from)")}; " +
                      $"{adopted.Describe("ADOPTED(scenario fan/tray)")}; " +
                      $"{clone.Describe("CLONE(mod-built fronts)")}; corrections written {s_normalizedCount} " +
                      "(this counts WRITES, not clones — the write is change-gated, so a count that keeps " +
                      "rising while CLONE faces stay dim means the correction is not sticking)" +
                      (s_beforeHalves > 0
                          ? $"; the corrected halves read alpha {s_beforeMinAlpha:F2}..{s_beforeMaxAlpha:F2} " +
                            $"and {s_beforeNonInteractable}/{s_beforeHalves} non-interactable BEFORE the write"
                          : string.Empty) + ". " + verdict;
        if (line == s_lastVerdict)
            return;
        s_lastVerdict = line;
        VRLog.Info(Scope, line);
    }

    /// <summary>Name the divergence as a sentence, or say plainly that there is none — including
    /// when a population was empty, so an absent comparison never reads as a passing one.</summary>
    private static string Verdict(in Bucket game, in Bucket adopted, in Bucket clone)
    {
        if (game.Halves == 0)
            return "VERDICT: no GAME-LIVE face was in the scene (no window was showing ability rows), " +
                   "so nothing was compared against the overlay this pass — this is NOT a pass, it is " +
                   "a missing reference. Read the GAME-POOL numbers instead: they are what a clone " +
                   "would have inherited.";
        float gameAlpha = game.SumAlpha / game.Halves;
        var parts = new List<string>(2);
        if (clone.Halves > 0 && Mathf.Abs(clone.SumAlpha / clone.Halves - gameAlpha) > 0.01f)
            parts.Add($"CLONE halves average {clone.SumAlpha / clone.Halves:F2} against the overlay's " +
                      $"{gameAlpha:F2} — that difference IS the reported grey");
        if (adopted.Halves > 0 && Mathf.Abs(adopted.SumAlpha / adopted.Halves - gameAlpha) > 0.01f)
            parts.Add($"ADOPTED halves average {adopted.SumAlpha / adopted.Halves:F2} against the " +
                      $"overlay's {gameAlpha:F2}; the adopted face IS the game's own live widget, so " +
                      "this is the game's own state for that card (a played half on the control board " +
                      "is alpha 0.5 by CardsActionControlller.Finish) and is NOT written to here");
        return parts.Count == 0
            ? "VERDICT: every population matches the overlay on half alpha."
            : "VERDICT: " + string.Join("; ", parts.ToArray()) + ".";
    }

    private static void Sample(ref Bucket bucket, FullAbilityCardAction? half)
    {
        if (half == null)
            return;
        float alpha = half.canvasGroup != null ? half.canvasGroup.alpha : 1f;
        Button? button = half.actionButton;
        bool interactable = button != null && button.interactable;
        bool enabled = button != null && button.enabled;
        float tint = 1f;
        string? shader = null;
        Graphic? target = button != null ? button.targetGraphic : null;
        if (target != null)
        {
            if (target.canvasRenderer != null)
            {
                Color c = target.canvasRenderer.GetColor();
                // One number for "how grey/dim is it": the rendered luminance times the rendered
                // alpha, i.e. what actually reaches the eye through the Selectable's tint.
                tint = (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) * c.a;
            }
            Material mat = target.materialForRendering;
            if (mat != null && mat.shader != null)
                shader = mat.shader.name;
        }
        bucket.AddHalf(alpha, interactable, enabled, tint, shader);
    }

    private static void LogErrorOnce(string what, System.Exception ex)
    {
        if (s_errorLogged)
            return;
        s_errorLogged = true;
        VRLog.Warn(Scope, $"CARD HALF TONE {what} failed ({ex.GetType().Name}: {ex.Message}) — card " +
                          "fronts keep the game's own half state.");
    }
}
