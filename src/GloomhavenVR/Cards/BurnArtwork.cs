using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.Cards;

/// <summary>
/// ONE COMPLETION SIGNAL FOR EVERY BURN, ON EVERY BOARD — the single place that answers "is the
/// game's burn artwork still on this card?", "has it ever been painted on this card?" and "paint
/// the settled end-state now".
///
/// <para>WHY IT EXISTS. The 2026-09-07 round asked for the burn sequence a THIRD time, verbatim:
/// "Bei der kurzen Rast wenn eine Karte verbrannt wurde, wurde nicht ausreichend gewartet bis die
/// Verbrennen Animation fertig ist. Erst dann soll die Flug-Animation starten und es weitergehen.
/// Das soll so synchron mit den anderen Spielern sein … Prüfe das nochmal bei allen
/// Verbrennen-Flows!" The enumeration that request asks for came back with SIX producers and THREE
/// different completion signals, which is why fixing one flow per round never converged:</para>
/// <list type="number">
///   <item><description><c>CardsDriver.TryTakeBurnFlightSlot</c> — the game's own running
///   <c>CardEffects.coroutine</c> ANDed with <c>HasEffect(BurnCard|LostMode)</c>, bounded by a
///   0.5 s start grace and a 3 s ceiling. Drives the short-rest sacrifice, the rebuild park sweep,
///   the burnt-pile watch and the card-back slab fallback.</description></item>
///   <item><description>NOTHING AT ALL — <c>CardsDriver.TryStartFlyToPile</c> (a played card whose
///   model fate is Lost/PermanentlyLost at turn clear) actively CLEARS the pending hold, and
///   <c>FlushBurnHolds</c> deliberately bypasses the gate on a hand switch. Both are real burns
///   under the standing ruling and neither waits.</description></item>
///   <item><description>THE OWNER'S RECESS OCCUPANCY — <c>RemoteBurnFx.Drive</c> held its mirrored
///   presentation for exactly as long as the mirrored recess kept DRAWING the card. That is a proxy
///   for the artwork, not the artwork, and the ModBuild 474 logs measure the difference on all three
///   burns of the session (see <see cref="Released"/>).</description></item>
/// </list>
///
/// <para>THIS TYPE IS THE FIRST SIGNAL, LIFTED OUT SO THERE IS ONE COPY. It holds the predicate and
/// both bounds, so the owner's hold and every mirror evaluate the SAME expression over the SAME
/// widget on the SAME machine — which is what makes "synchron mit den anderen Spielern" a property
/// of the code rather than of two dials that happen to agree.</para>
///
/// <para>THE MODEL IS LOCAL, WHICH IS WHY A MIRROR MAY ASK AT ALL. A peer's burnt card is not
/// replicated presentation: <c>CCharacterClass.LostAbilityCards</c> is host-replicated and this
/// client walks it already (<c>RemoteBurnFx.Watch</c>, <c>RemotePileFronts.Resolve</c>), and the
/// <c>AbilityCardUI</c> it names is a real widget on THIS machine with its own
/// <see cref="CardEffects"/>. No wire field is owed for any of this and none is added.</para>
///
/// <para>NOTHING HERE WRITES GAME STATE. <see cref="Playing"/>, <see cref="Latched"/> and
/// <see cref="SettledBurnPainted"/> are read-only. <see cref="TrySettleBurnLook"/> writes only
/// PRESENTATION, and it writes it by running the game's OWN
/// <c>CardEffects.BurnCardTimeline(burnAnim: false, …)</c> — the no-ramp arm the game itself uses
/// for the settled end state — rather than by rebuilding an approximation of it. It never touches
/// <c>toggledEffects</c>, never starts a coroutine, and the game's own <c>RestoreCard()</c> (which
/// resets <c>_GreyOut</c>/<c>_Flow</c>/<c>_Dissolve</c>/<c>_Burn</c>/<c>_FXAnim</c> to zero,
/// CardEffects.cs:466-505) undoes it in full if the card is ever recovered.</para>
/// </summary>
internal static class BurnArtwork
{
    /// <summary>
    /// How long a burn may wait for its artwork to START before the flight goes anyway, in seconds.
    ///
    /// <para>MIRRORED FROM <c>CardsDriver.BurnEffectStartGraceSeconds</c> and it must stay equal to
    /// it: the whole point of this type is that the owner's hold and every mirror release on one
    /// expression. The ModBuild 474 peer log measures it directly — the short-rest sacrifice
    /// 'ABILITY_CARD_SpareDagger' reported <c>BURN ANIM: … (effect not started yet)</c> and then
    /// <c>BURN HOLD: … waited 0,50s</c>, i.e. this constant WAS the entire wait for that flow.</para>
    /// </summary>
    internal const float StartGraceSeconds = 0.5f;

    /// <summary>
    /// Deadline: the longest a burned card may lie on any board, in seconds. Mirrored from
    /// <c>CardsDriver.BurnEffectMaxHoldSeconds</c>. A burned card must never be stranded because its
    /// artwork did not play.
    /// </summary>
    internal const float MaxHoldSeconds = 3f;

    /// <summary>Settled value of <c>_GreyOut</c> at the end of the game's burn timeline; anything at
    /// or above half of it is "this card has been painted", anything below is "the timeline never
    /// ran on this client". Read-only discriminator, see <see cref="SettledBurnPainted"/>.</summary>
    private const float PaintedGreyOut = 0.5f;

    private static readonly int GreyOutId = Shader.PropertyToID("_GreyOut");

    /// <summary>
    /// The <see cref="CardEffects"/> that paints one pile/hand widget, or null. THE ONE PLACE that
    /// knows the hop — <c>AbilityCardUI</c> does not own the component, its
    /// <c>fullAbilityCard</c> does, and every caller here starts from the widget because that is
    /// what <c>CardsGameApi.GetPileWidgets</c> hands back.
    /// </summary>
    internal static CardEffects? EffectsOf(AbilityCardUI? widget)
    {
        FullAbilityCard? full = widget != null ? widget.fullAbilityCard : null;
        return EffectsOf(full);
    }

    /// <summary>The <see cref="CardEffects"/> that paints one adopted card face, or null.</summary>
    internal static CardEffects? EffectsOf(FullAbilityCard? full) => full != null ? full.cardEffects : null;

    /// <summary>
    /// TRUE while the game is PLAYING its own burn/lost timeline on this widget — the picture, not
    /// the state.
    ///
    /// <para>BOTH TERMS ARE LOAD-BEARING and the reason is recorded in
    /// <c>CardsDriver.BurnArtworkActive</c>, whose body this is: <c>HasEffect</c> alone is
    /// <c>toggledEffects.Contains(task)</c> over a <see cref="System.Collections.Generic.HashSet{T}"/>
    /// that never clears for a LOST card, so it is a latched display state with no end and it made
    /// every burn on ModBuild 447 run the full ceiling; <c>coroutine</c> alone is also the GHOST
    /// (discard) timeline's handle, so the state test is what says the running timeline is a burn.
    /// </para>
    /// </summary>
    internal static bool Playing(CardEffects? fx)
    {
        if (fx == null)
            return false;
        try
        {
            bool burning = fx.HasEffect(CardEffects.FXTask.BurnCard)
                           || fx.HasEffect(CardEffects.FXTask.LostMode);
            return burning && fx.coroutine != null;
        }
        catch
        {
            return false; // a game-side shape change must never strand a card on a board
        }
    }

    /// <summary>
    /// TRUE when the game has LATCHED this widget as burnt/lost, whether or not it ever painted it.
    ///
    /// <para>THIS IS THE HALF THAT SURVIVES A BAIL, and that is the whole of report item 10.
    /// <c>ToggleEffect</c> adds to <c>toggledEffects</c> and only THEN starts
    /// <c>BurnCardTimeline</c>, whose very first statement is
    /// <c>if (!gameObject.activeInHierarchy &amp;&amp; !playOnDisabled) { coroutine = null; yield
    /// break; }</c> (CardEffects.cs:508-513). A card burned while this client's 2D card UI was
    /// presenting a DIFFERENT character therefore comes out latched and unpainted, and nothing ever
    /// paints it afterwards.</para>
    /// </summary>
    internal static bool Latched(CardEffects? fx)
    {
        if (fx == null)
            return false;
        try
        {
            return fx.HasEffect(CardEffects.FXTask.BurnCard)
                   || fx.HasEffect(CardEffects.FXTask.LostMode);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Has the game's burn timeline actually PAINTED this widget? Reads <c>_GreyOut</c> off the
    /// widget's own face images — the one property both arms of <c>BurnCardTimeline</c> write and
    /// <c>RestoreCard</c> clears.
    /// </summary>
    /// <param name="greyOut">The value read, or -1 when no image could be read at all (the widget
    /// was never <c>Initialize</c>d, or the game changed shape). -1 is UNKNOWN and must never be
    /// treated as unpainted.</param>
    /// <returns>True only for a positive reading at or above <see cref="PaintedGreyOut"/>.</returns>
    internal static bool SettledBurnPainted(CardEffects? fx, out float greyOut)
    {
        greyOut = -1f;
        if (fx == null)
            return false;
        try
        {
            Image[]? imgs = fx.imgComp;
            if (imgs == null)
                return false;
            for (int i = 0; i < imgs.Length; i++)
            {
                Image? img = imgs[i];
                Material? mat = img != null ? img.material : null;
                if (mat == null || !mat.HasProperty(GreyOutId))
                    continue;
                float v = mat.GetFloat(GreyOutId);
                if (v > greyOut)
                    greyOut = v;
            }
        }
        catch
        {
            greyOut = -1f;
            return false;
        }
        return greyOut >= PaintedGreyOut;
    }

    /// <summary>
    /// Paint the SETTLED burn look on a widget the game latched but never painted, by running the
    /// game's own timeline in its no-ramp arm. Returns true when the paint ran.
    ///
    /// <para>THE REAL THING, NOT AN APPROXIMATION — the standing ruling. <c>BurnCardTimeline</c>
    /// takes <c>burnAnim</c>; with it FALSE the method writes the full static burn block
    /// (<c>_Burn</c>, <c>_Burn_ColourTint</c>, <c>_Flow_Offset</c>, <c>_Flow_Speed</c>, the noise
    /// tiling, the dissolve gradient and the whole <c>fgFx</c> overlay), then the end state
    /// (<c>_GreyOut</c>=1, <c>_Flow</c>=1, <c>_Dissolve</c>=<c>mc_Dissolve</c>, <c>_FXAnim</c>=0.5,
    /// texts to mid-grey) and returns — WITHOUT A SINGLE <c>yield</c> (CardEffects.cs:592-616). So
    /// draining the enumerator here runs the whole of it in this call, on this frame, with no
    /// coroutine and no ramp. A ramp would be worse than the bug: a burn STARTING in a browse fan
    /// minutes later is a picture the owner never had.</para>
    ///
    /// <para>REFUSED WHILE THE GAME IS BUSY. If a timeline is running (<c>coroutine != null</c>)
    /// this does nothing — cutting across the game's own ramp is exactly the write war the project
    /// has a standing ruling against. It also refuses a widget the game never
    /// <c>Initialize</c>d, because the no-ramp arm indexes <c>txtAffected</c> and
    /// <c>imgComp</c>.</para>
    ///
    /// <para>AND IT SUPPRESSES THE PARTICLE. The no-ramp arm calls <c>SpawnParticle()</c>; the
    /// owner's card sprayed its embers at BURN TIME, so a puff in a fan opened later is, again, a
    /// picture nobody had. <c>HideParticle()</c> is the game's own undo for it and runs immediately
    /// after.</para>
    /// </summary>
    internal static bool TrySettleBurnLook(CardEffects? fx)
    {
        if (fx == null || fx.coroutine != null)
            return false;
        try
        {
            if (fx.imgComp == null || fx.txtAffected == null)
                return false; // never Initialize()d — the no-ramp arm would index a null array
            System.Collections.IEnumerator settle = fx.BurnCardTimeline(burnAnim: false, playOnDisabled: true);
            while (settle.MoveNext())
            {
                // burnAnim:false never yields; the guard is against a game-side shape change
                // turning this into a real coroutine and spinning here forever.
                break;
            }
            fx.HideParticle();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// THE ONE RELEASE EXPRESSION. May a burn whose artwork state is <paramref name="playing"/> and
    /// which has been held for <paramref name="held"/> seconds fly now?
    ///
    /// <para>This is <c>CardsDriver.TryTakeBurnFlightSlot</c>'s condition, character for character,
    /// so that a mirror asking it gets the owner's answer rather than an equivalent-looking one.
    /// The ModBuild 474 logs measure what "equivalent-looking" cost: the owner's hold and the
    /// mirror's hold for the SAME burn, both clocked from the same host-replicated pile change,
    /// never once agreed —</para>
    /// <list type="bullet">
    ///   <item><description><c>ShieldBash</c> — owner 0.67 s (remote/Player.log 151241), mirror
    ///   1.00 s (Player.log 170102). Mirror LATE by 0.33 s.</description></item>
    ///   <item><description><c>SpareDagger</c>, the short-rest sacrifice — owner 0.50 s
    ///   (remote/Player.log 177185), mirror <b>0.02 s</b> (Player.log 197139, "their recess never
    ///   drew this card at all"). Mirror EARLY by 0.48 s: the card never lay still at all, which is
    ///   the reported defect in its own words.</description></item>
    ///   <item><description><c>FeedbackLoop</c> — owner 2.01 s (Player.log 249415), mirror 0.56 s
    ///   (remote/Player.log 225114). Mirror EARLY by 1.45 s.</description></item>
    /// </list>
    /// <para>Three burns, three different signs and magnitudes: that is a design fault and not a
    /// dial, which is why the remedy is one expression and not a tuned offset.</para>
    /// </summary>
    internal static bool Released(bool playing, float held)
    {
        if (held >= MaxHoldSeconds)
            return true;             // deadline: always go
        if (playing)
            return false;            // still burning ON the card — that is the whole point of the wait
        return held >= StartGraceSeconds; // never started, or already done
    }
}
