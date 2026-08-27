using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.Cards;

/// <summary>
/// Anything a physical HAND SWEEP can elect: a card in the ability fan, a card in the
/// discard/burnt browse arc, an item chip in the item fan. The three fans used to carry three
/// hand-written copies of the same election loop (the copies had already drifted: the item fan
/// ranked by <c>min(tip,palm)</c> where the other two ranked tip-first), so the one the user is
/// happy with could not be "the reference implementation" in any enforceable sense. This
/// interface is the seam that lets <see cref="FanSweep"/> BE that single implementation.
///
/// Deliberately tiny: everything the election needs is a distance probe, an eligibility flag, a
/// live world size (for the scale-relative reach — see <see cref="FanSweep.ResolveReach"/>) and a
/// name for the log. Implemented EXPLICITLY by <see cref="VRCard"/> and
/// <see cref="ItemsPile.ItemChip"/> so neither type widens its public surface.
/// </summary>
internal interface IFanSweepTarget
{
    /// <summary>False while this target must not win (held, rooted, mid-flourish, clipped into a
    /// slot). A dead target that still won would suppress the lift of a live one right next to it.</summary>
    bool SweepEligible { get; }

    /// <summary>The rendered face width in WORLD metres — the target's own live size, including
    /// every scale above it (fan scale, board scale, rig scale). This is what makes the reach
    /// scale-invariant; see <see cref="FanSweep.ResolveReach"/>.</summary>
    float SweepFaceWidthWorld { get; }

    /// <summary>Distance from a world point to this target's grab collider (0 inside), or false
    /// when the collider is missing/disabled. Surface distance, NOT centre distance — a bigger
    /// card is therefore EASIER to touch, not harder.</summary>
    bool TrySweepDistance(Vector3 worldPoint, out float distance);

    /// <summary>Name for the diagnostic line (the GameObject name for both implementers).</summary>
    string SweepName { get; }
}

/// <summary>
/// The reach envelope one hand presents to one fan this frame, in WORLD units, plus the numbers
/// the log needs to explain itself. Produced by <see cref="FanSweep.ResolveReach"/>.
/// </summary>
internal readonly struct FanReach
{
    /// <summary>Index-fingertip candidacy radius off the card's collider surface (world units).</summary>
    internal readonly float Tip;

    /// <summary>Palm candidacy radius off the card's collider surface (world units).</summary>
    internal readonly float Palm;

    /// <summary>Incumbent hysteresis bonus (world units): a rival must be this much closer to steal.</summary>
    internal readonly float Sticky;

    /// <summary>Rig scale — world units per REAL metre. Divide any world distance by this to get
    /// real metres (what the player's hand actually travelled).</summary>
    internal readonly float WorldScale;

    /// <summary>The target's live face width in world units (the size the reaches were derived from).</summary>
    internal readonly float FaceWidthWorld;

    /// <summary>How many "reference cards" wide the target is: 1 for the ability hand fan by
    /// construction, &lt;1 for a fan on a shrunken board, &gt;1 on an enlarged one.</summary>
    internal readonly float RelativeSize;

    internal FanReach(float tip, float palm, float sticky, float worldScale, float faceWidthWorld,
        float relativeSize)
    {
        Tip = tip;
        Palm = palm;
        Sticky = sticky;
        WorldScale = worldScale;
        FaceWidthWorld = faceWidthWorld;
        RelativeSize = relativeSize;
    }

    /// <summary>Real centimetres for a world distance (the unit every sweep log line prints).</summary>
    internal float Cm(float worldDistance) => worldDistance / Mathf.Max(WorldScale, 1e-4f) * 100f;
}

/// <summary>
/// The running result of one frame's single-winner election over one fan: the winner, the
/// runner-up (so a log line can show HOW decisive the win was) and the nearest NON-candidate
/// (so a frame with no winner can say by how much the hand missed). Allocation-free; a plain
/// mutable struct passed by ref through <see cref="FanSweep.Score"/>.
/// </summary>
internal struct FanSweepPick<T> where T : class, IFanSweepTarget
{
    internal T? Winner;
    internal float WinnerTip;   // RAW index-tip distance (world units)
    internal float WinnerPalm;  // RAW palm distance (world units)
    internal float BestScore;   // ranking score, incumbent hysteresis already applied

    internal T? RunnerUp;
    internal float RunnerTip;
    internal float SecondScore;

    /// <summary>Nearest target that failed BOTH reaches — the near-miss diagnostic.</summary>
    internal T? Miss;
    internal float MissTip;
    internal float MissPalm;
    internal float MissContact; // min(tip, palm) of the miss

    internal static FanSweepPick<T> Empty => new()
    {
        BestScore = float.MaxValue,
        SecondScore = float.MaxValue,
        MissContact = float.MaxValue,
    };
}

/// <summary>
/// THE single implementation of "sweep a hand through a fan and light up exactly one card",
/// shared by the ability hand fan (<c>CardsDriver.UpdateHandContactArbitration</c>), the
/// discard/burnt browse arc (<see cref="PileBrowser"/>) and the item fan (<see cref="ItemsPile"/>).
///
/// WHY THIS EXISTS (user report 2026-08-02: "sweeping the pile fans by hand is not as smooth as the
/// card hand — only every 2nd or 3rd card highlights; please make the pile fans work like the hand
/// cards"). The hand fan was doing THREE things the pile fans were not, and each one on its own
/// breaks a sweep:
/// <list type="number">
/// <item><b>Tiling colliders.</b> The hand fan shrinks every card's grab collider to its VISIBLE
/// strip (<see cref="VRCard.SetColliderRegion"/>, driven by <see cref="StripWidth"/>), so the
/// per-card regions TILE the arc and the "nearest surface" metric is a clean partition: every
/// point in space belongs to exactly one card. The pile fans kept FULL-width colliders on cards
/// that overlap by 40-60 %, so a fingertip inside the overlap measured 0.0 cm to two or three
/// cards at once. Ties do not switch the winner (<c>score &lt; best</c> is false for equals) and
/// the incumbent additionally carries the hysteresis bonus — so the lift stuck to whichever card
/// won first and only let go once the finger left its whole box: cards were SKIPPED. The
/// 2026-08-02 hardware log shows the tie verbatim: <c>'ItemChip_Flügelschuhe' — contact 0,0 cm;
/// runner-up 'ItemChip_Großer Heiltrank' (0,0 cm)</c>.</item>
/// <item><b>Tip-first ranking.</b> Candidacy may come from the palm (wide, forgiving), but the
/// WINNER is ranked by the index fingertip alone, so the card nearest the pointing finger wins and
/// the between-two-cards midpoint resolves deterministically. The item fan had drifted to
/// <c>min(tip,palm)</c>, which lets a chip the palm brushes beat the chip the finger is on.</item>
/// <item><b>Scale-relative reach.</b> See <see cref="ResolveReach"/> — the reason the two players
/// on the same build got different behaviour.</item>
/// </list>
/// Allocation-free throughout; every method is called per frame per hand.
/// </summary>
internal static class FanSweep
{
    // ---- reach ------------------------------------------------------------------------

    /// <summary>Fingertip candidacy reach at the REFERENCE card size, in real metres. This is the
    /// ability hand fan's proven number (CardFan.FingertipHoverReach / the old
    /// CardsDriver.ContactTipReach / PileBrowser.ContactTipReach / ItemsPile.ContactTipReach — four
    /// copies of it, now one).</summary>
    internal const float TipReachMeters = 0.035f;

    /// <summary>
    /// Palm candidacy reach, in real metres. DELIBERATELY NOT card-relative — unlike the tip reach
    /// and the hysteresis below, this one must stay a real-metre constant because it mirrors
    /// <c>ProximityGrabber.ReachMeters</c> (0.13 m × world scale), which is what actually decides
    /// the GRAB. Candidacy therefore means exactly "this hand could grab this card", so a card the
    /// trigger would take can never be a card the sweep left dark: pop and grab stay aligned at
    /// every board scale. What is card-relative is which candidate WINS.
    /// </summary>
    internal const float PalmReachMeters = 0.13f;

    /// <summary>Incumbent hysteresis at the REFERENCE card size, in real metres.</summary>
    internal const float StickyMarginMeters = 0.02f;

    /// <summary>Clamp on <see cref="FanReach.RelativeSize"/>. The lower bound is a floor against a
    /// degenerate/not-yet-measured face (a zero-width card must not produce a zero reach and become
    /// untouchable); the upper bound stops a hugely enlarged board from turning the palm reach into
    /// a metre-wide vacuum cleaner that swallows the whole arc.</summary>
    private const float MinRelativeSize = 0.30f;
    private const float MaxRelativeSize = 2.50f;

    /// <summary>
    /// The reach envelope for one hand against one target size.
    ///
    /// ROOT CAUSE THIS FIXES (user report 2026-08-02: one player could barely touch or laser an
    /// item chip, the other could not reproduce it at all — same build, ModBuild 20). The reach
    /// constants above are REAL-metre constants: every call site multiplied them by
    /// <c>hand.WorldScale</c> (the rig scale, world units per real metre), so they were already
    /// zoom-invariant — the "fixed real-cm threshold vs a diorama-scaled chip" hypothesis is
    /// REFUTED as stated. What was NOT accounted for is a SECOND, independent scale: the ability
    /// hand fan hangs off <c>hand.Rig.PalmCenter</c>, so its local units ARE real metres, but the
    /// pile fans and the item fan are parented under the CONTROL BOARD
    /// (<c>PlayTray.Current.Root</c>) and therefore inherit the board's scale
    /// (<c>ClampedTrayScale × BoardScale</c>) on top of the rig scale. The two hardware logs prove
    /// it: <c>Control board placed (Steel: … scale 0,32×)</c> on the player who could not hit
    /// anything versus <c>scale 0.80×</c> (the shipped default) on the player who could. His fan
    /// was 2.5× physically smaller — 2.5 cm chips on a 6 cm-wide arc — while the reach stayed
    /// 3.5 cm / 13 cm real. Measured in CARD WIDTHS, his palm reach was 5.1 card widths where the
    /// hand fan's is 2.0: candidacy carried no information at all, every chip in the fan qualified
    /// at once, and the winner was decided by millimetre differences between overlapping full-size
    /// colliders. That is the "I highlight one chip and grab another" report.
    ///
    /// The fix is to express the DISCRIMINATING quantities — the fingertip reach and the incumbent
    /// hysteresis — in the fan's OWN units: a fixed number of card widths.
    /// <paramref name="faceWidthWorld"/> is the target's LIVE world width, so they scale with the
    /// board, the rig and any future container alike, and every player gets geometrically identical
    /// behaviour at any board scale or zoom. Concretely, on the 0,32× board a 3,5 cm fingertip reach
    /// was 1,4 card widths (where the hand fan's is 0,55) and a 2 cm hysteresis was 0,8 of a whole
    /// card — the incumbent could not be displaced until the finger had left it by nearly a card,
    /// which is the "only every 2nd or 3rd card highlights" complaint stated in numbers.
    ///
    /// The PALM reach stays a real-metre constant on purpose (see <see cref="PalmReachMeters"/>):
    /// it is the candidacy gate and must keep mirroring the grabber's own reach.
    ///
    /// The ability hand fan is the reference by construction: its card width IS
    /// <c>CardsConfig.CardWidth × worldScale</c> (the fan root hangs off the palm), so
    /// <see cref="FanReach.RelativeSize"/> = 1 and every number it gets back is bit-identical to
    /// the constants it used before — the one fan the user is happy with cannot move.
    /// </summary>
    /// <param name="worldScale">The hand's rig scale (world units per real metre).</param>
    /// <param name="faceWidthWorld">The target's rendered face width in world units.</param>
    internal static FanReach ResolveReach(float worldScale, float faceWidthWorld)
    {
        float scale = Mathf.Max(worldScale, 1e-4f);
        float referenceWidth = Mathf.Max(CardsConfig.CardWidth.Value, 1e-4f) * scale;
        float relative = Mathf.Clamp(faceWidthWorld / referenceWidth, MinRelativeSize, MaxRelativeSize);
        return new FanReach(
            TipReachMeters * scale * relative,
            PalmReachMeters * scale,
            StickyMarginMeters * scale * relative,
            scale, faceWidthWorld, relative);
    }

    // ---- election ---------------------------------------------------------------------

    /// <summary>
    /// Fold one target into the running single-winner election. Candidacy: the index tip within
    /// <see cref="FanReach.Tip"/> OR the palm within <see cref="FanReach.Palm"/> of the target's
    /// collider SURFACE. Ranking: the index-fingertip distance alone when
    /// <paramref name="tipFirst"/> (every FAN — the palm is wide and noisy and mixing it into the
    /// winner choice is what let two adjacent cards flip-flop at the midpoint), else
    /// <c>min(tip,palm)</c> (the board dock / pick field, whose cards sit far enough apart not to
    /// oscillate). The incumbent gets the <see cref="FanReach.Sticky"/> bonus on whichever metric
    /// is used, so the lift holds until a rival is decisively closer.
    ///
    /// A target that fails BOTH reaches is recorded as the near-miss candidate instead — that is
    /// what turns a silent frame into "the hand was 19 cm out, the reach was 13 cm" in the log.
    /// </summary>
    internal static void Score<T>(T? target, Vector3 tip, Vector3 palm, in FanReach reach,
        T? incumbent, bool tipFirst, ref FanSweepPick<T> pick) where T : class, IFanSweepTarget
    {
        if (target == null || (target is Object unityObject && unityObject == null))
            return;
        if (!target.SweepEligible)
            return;
        if (!target.TrySweepDistance(tip, out float tipDistance)
            || !target.TrySweepDistance(palm, out float palmDistance))
            return;

        if (tipDistance > reach.Tip && palmDistance > reach.Palm)
        {
            float missContact = Mathf.Min(tipDistance, palmDistance);
            if (missContact < pick.MissContact)
            {
                pick.MissContact = missContact;
                pick.Miss = target;
                pick.MissTip = tipDistance;
                pick.MissPalm = palmDistance;
            }
            return;
        }

        float score = tipFirst ? tipDistance : Mathf.Min(tipDistance, palmDistance);
        if (ReferenceEquals(target, incumbent))
            score -= reach.Sticky;

        if (score < pick.BestScore)
        {
            pick.RunnerUp = pick.Winner;
            pick.SecondScore = pick.BestScore;
            pick.RunnerTip = pick.WinnerTip;
            pick.Winner = target;
            pick.BestScore = score;
            pick.WinnerTip = tipDistance;
            pick.WinnerPalm = palmDistance;
        }
        else if (score < pick.SecondScore)
        {
            pick.RunnerUp = target;
            pick.SecondScore = score;
            pick.RunnerTip = tipDistance;
        }
    }

    // ---- geometry shared with the fan layouts -----------------------------------------

    /// <summary>
    /// The VISIBLE strip of a fanned card in CARD-LOCAL metres: the chord between neighbouring card
    /// centres. Cards later in the arc draw in front (more negative Z) and cover this card's right
    /// side, so shrinking the collider to this width — anchored at the exposed LEFT edge, see
    /// <see cref="StripOffset"/> — makes the per-card grab regions TILE instead of overlap.
    ///
    /// This is the single most important half of "sweep card by card": with tiling regions the
    /// nearest-surface metric becomes a partition of the space around the arc, so the winner
    /// changes exactly once per card boundary the finger crosses. With overlapping regions several
    /// cards measure 0.0 cm at once, ties never switch the incumbent, and cards get skipped.
    /// </summary>
    /// <param name="cardCount">Cards in the arc (a single card is fully exposed).</param>
    /// <param name="radiusLocal">Arc radius in FAN-local metres.</param>
    /// <param name="stepDegrees">Angular pitch between neighbouring cards.</param>
    /// <param name="fullWidthLocal">The card's own full face width in CARD-local metres.</param>
    /// <param name="cardLocalScale">The card's local scale inside the fan (the pile fans enlarge
    /// their cards, so a fan-local chord must be divided by it to become card-local).</param>
    internal static float StripWidth(int cardCount, float radiusLocal, float stepDegrees,
        float fullWidthLocal, float cardLocalScale)
    {
        if (cardCount < 2)
            return fullWidthLocal;
        float chordCardLocal = ArcChord(radiusLocal, stepDegrees) / Mathf.Max(cardLocalScale, 1e-4f);
        // Floor at a quarter card: a very tight arc must still leave a strip a finger can find.
        return Mathf.Clamp(chordCardLocal, fullWidthLocal * 0.25f, fullWidthLocal);
    }

    /// <summary>Distance between neighbouring card centres on an arc of
    /// <paramref name="radiusLocal"/> with a <paramref name="stepDegrees"/> pitch, in the arc's own
    /// (fan-local) metres. The one place that formula lives.</summary>
    internal static float ArcChord(float radiusLocal, float stepDegrees)
        => 2f * radiusLocal * Mathf.Sin(stepDegrees * 0.5f * Mathf.Deg2Rad);

    /// <summary>Collider centre offset for a <see cref="StripWidth"/> strip: the strip sits on the
    /// card's exposed LEFT edge (the right side is covered by the next card in the arc).</summary>
    internal static float StripOffset(float fullWidthLocal, float stripWidthLocal)
        => -(fullWidthLocal - stripWidthLocal) * 0.5f;

    /// <summary>
    /// Sideways push (fan-local metres) applied to card <paramref name="signedSlots"/> slots away
    /// from the hovered one, opening the fan's signature GAP around the highlight. Extracted from
    /// <c>CardFan.SplitOffset</c> verbatim so the pile fans split with the same shape, the same
    /// falloff and the same live <c>[Cards] FanSplit*</c> tuning as the hand fan — the visible half
    /// of "one card at a time": the winner is the pivot and its neighbours physically step aside.
    /// </summary>
    internal static float SplitOffset(int signedSlots)
    {
        float distance = Mathf.Abs(signedSlots);
        float falloff = Mathf.Max(0.0001f, CardsConfig.FanSplitFalloff.Value);
        float x = distance / falloff;
        float splitScale = Mathf.Max(0f, CardsConfig.FanHoverSplitScale.Value);
        return Mathf.Sign(signedSlots) * Mathf.Exp(-x * x)
               * CardsConfig.FanSplitMultiplier.Value * splitScale;
    }

    // ---- laser: angular near-miss rescue ----------------------------------------------

    /// <summary>
    /// Minimum angular HALF-size (degrees) a fan card is granted against the aim ray. The fan
    /// raycasts test the card's exact rect, which is correct at the default board scale but turns
    /// into a coin flip on a shrunken one: at <c>board 0,32×</c> an item chip is 2.5 × 3.5 real cm,
    /// so at a ~50 cm aim distance it subtends 1.4° × 2.0° half-angle — inside the aim jitter of a
    /// hand-held controller, which is why one player reported he "could not reliably laser-hover
    /// the chips" while the player on the 0.80× default board (5.7° × 8.0°) never saw it. A card is
    /// therefore allowed to present at least this angular half-size to the beam, which makes the
    /// laser's effective target angle constant at ANY board scale and aim distance — the same
    /// invariance the hand sweep gets from <see cref="ResolveReach"/>.
    ///
    /// Applied ONLY as a second pass, after an exact-rect pass found nothing (see
    /// <see cref="ItemsPile.TryLaserRaycast"/> / <see cref="PileBrowser.TryRaycast"/>): while the
    /// beam is genuinely ON a card nothing changes at all, and the fan OCCLUDER keeps the exact
    /// rect so a rescued near-miss can never start hiding game UI behind the fan.
    /// </summary>
    internal const float LaserMinHalfAngleDegrees = 1.75f;

    /// <summary>
    /// HARD CAP on the angular pad, as a fraction of the card's own half-extent. The pad below
    /// grows LINEARLY with the ray distance, and the distance it is handed is the distance to the
    /// card's PLANE — which goes to infinity as the beam approaches parallel with the face. The
    /// 2026-08-03 hardware log has the runaway verbatim: <c>Pile-browse laser (Right): MISS —
    /// nearest 'VRCard_…_ProvokingRoar' was 8415.9 cm outside its face with only 203.7 cm of
    /// angular pad</c> — a 2 m pad around a 4,9 cm card. Two metres of forgiveness is not aim
    /// jitter, it is a different card (or no card at all), and had the overshoot come out just
    /// under it the arc would have claimed a hover the player was nowhere near pointing at.
    /// Jitter forgiveness is bounded by the TARGET, never by how far away its plane happens to
    /// be crossed: half a half-extent is the most a card may ever grow against the beam.
    /// </summary>
    private const float LaserPadMaxHalfExtentFraction = 0.5f;

    /// <summary>
    /// How far outside its exact rect a card may still be accepted, in WORLD metres, so that it
    /// presents <see cref="LaserMinHalfAngleDegrees"/> of half-angle at <paramref name="rayDistance"/>.
    /// Zero once the card is already big enough on screen — a comfortable target is never widened —
    /// and never more than <see cref="LaserPadMaxHalfExtentFraction"/> of the card's own half
    /// extent (see that constant for the grazing-ray runaway this bounds).
    /// </summary>
    internal static float LaserPad(float rayDistance, float halfExtentWorld)
        => Mathf.Clamp(
            Mathf.Max(
                rayDistance * Mathf.Tan(LaserMinHalfAngleDegrees * Mathf.Deg2Rad) - halfExtentWorld,
                halfExtentWorld * LaserPadMinHalfExtentFraction),
            0f, halfExtentWorld * LaserPadMaxHalfExtentFraction);

    /// <summary>
    /// FLOOR under the pad, as a fraction of the card's own half-extent — the aim slack that exists
    /// no matter how large the card subtends.
    ///
    /// <para>ROOT CAUSE (hardware log 2026-08-03, the measurement that killed the angular theory).
    /// With the incidence angle finally printed, every miss reads:</para>
    /// <code>
    /// MISS - nearest '...ProvokingRoar' was 0.1 cm outside its face with only 0.0 cm of angular
    ///        pad. Card 4.9 cm wide ... met the face at 17 deg off its normal (dot 0.96)
    /// </code>
    /// <para>17-25 degrees off the normal is essentially head-on, so the beam was NOT being refused
    /// for its angle — the user's own follow-up said as much ("besonders schlecht wenn man von
    /// vorne draufguckt"). What the same line shows is that the tolerance was <c>0.0 cm</c> and the
    /// beam was <c>0.1 cm</c> — one millimetre — outside the face. The formula above answers "is
    /// this card too small to aim at?": it grants slack only while the card subtends less than
    /// <see cref="LaserMinHalfAngleDegrees"/>, and a browse card at reading distance subtends about
    /// twice that, so the answer was a hard zero. But that is the wrong question. The question the
    /// pick actually needs is "how far may the beam be off and still mean this card?", and that
    /// never has a zero answer: a controller held at arm's length has tremor, the card pops under
    /// the beam, and the player cannot see a millimetre of overshoot at all. So the two questions
    /// are separated: the subtend term stays for genuinely tiny cards, and this floor supplies the
    /// slack every card needs. Expressed as a fraction of the card's own half-extent, it is
    /// automatically correct at every board size and diorama scale — about 3.7 mm on the 4.9 cm
    /// card in the log, which covers the measured 0.1-0.4 cm misses and leaves the 1.4 cm and
    /// 10.1 cm ones in the same log correctly refused as "the player was pointing elsewhere".</para>
    /// </summary>
    private const float LaserPadMinHalfExtentFraction = 0.15f;

    /// <summary>
    /// Minimum dot(ray direction, card face normal) a fan card must present before its plane is
    /// intersected at all — cos 78°, i.e. the beam must meet the face within 78° of head-on.
    ///
    /// ROOT CAUSE (user report 2026-08-03, "der Laser geht einfach durch die Karten"): the fan
    /// raycasts only rejected an EXACTLY parallel ray (<c>denom &lt; 1e-5</c>). A ray that merely
    /// GRAZES the face still crosses its infinite plane — arbitrarily far away — and the pick then
    /// measured that crossing as if it were a near miss on the card. The hardware log shows the
    /// nonsense the arc reported while the player pointed elsewhere: misses of 371 cm, 2418 cm,
    /// 5473 cm and 8415 cm "outside its face", each computed from a plane crossing metres behind
    /// the arc. Those bogus records also OUTRANKED the genuine sub-centimetre near miss on the card
    /// the player actually aimed at (the pick keeps the SMALLEST overshoot, and a grazing crossing
    /// on a NEARER card can undercut it), and they came paired with the metre-scale pad above.
    /// A card seen edge-on presents no target: refusing the plane test outright is both cheaper and
    /// honest — the visible face is what the beam may hit.
    /// <para>ROUND 2 — THE CONE WAS THE WRONG SHAPE OF GUARD, and it cost real hits (user report
    /// 2026-08-03: "Hier geht der Laser an verschiedenen Stellen in der Karte immer mal wieder
    /// durch. Aus allen Laserwinkeln soll er überall auf der Karte colliden ... hat das eventuell
    /// damit zu tun, dass die Karte automatisch nach meinem Kopf rotiert?"). It did, and the
    /// mechanism is exactly the one the user guessed at. A browse card BILLBOARDS TO THE HEAD, but
    /// the beam leaves the HAND. Those are different points: hold the controller out to the side,
    /// or low, or close to your chest, and the beam meets a head-facing card well off its normal
    /// while the reticle sits dead centre on the visible face. Past 78° the card was skipped
    /// OUTRIGHT — the beam went through it. The angle grew with hand-to-eye offset, so the dead
    /// zones moved around as the player moved: "an verschiedenen Stellen ... immer mal wieder".</para>
    ///
    /// <para>THE INSIGHT THE FIRST ROUND MISSED: an angular cone is not needed to decide a HIT at
    /// all. If the ray/plane crossing lands inside the FINITE rect, the beam passes through the
    /// visible card — that is a hit at any incidence, by definition, and a card is never invisible
    /// enough to skip while its face is being crossed. The cone was only ever a proxy for the real
    /// defect, which was in the NEAR-MISS bookkeeping: a nearly parallel ray crosses the infinite
    /// plane arbitrarily far away and was recorded as a "miss by 5473 cm". So the two jobs are
    /// separated now: this constant shrinks to pure numerical stability (a denominator that small
    /// puts the crossing kilometres away, where float precision is gone anyway), and the absurd
    /// records are bounded directly by <see cref="LaserMaxMissOvershootFactor"/> — measured in card
    /// widths, which is what "near" means for a near miss.</para>
    /// </summary>
    internal const float LaserMinFaceDenominator = 1e-3f;

    /// <summary>
    /// How far outside its own face a crossing may land and still be recorded as a NEAR MISS, as a
    /// multiple of the card's larger half-extent. Beyond this the beam is not aimed at the card at
    /// all — it merely crossed the card's infinite plane on its way somewhere else, which is what
    /// produced the 371 cm / 2418 cm / 5473 cm / 8415 cm records in the hardware log and let them
    /// outrank the genuine sub-centimetre near miss on the card the player was actually pointing at.
    /// Three half-extents is generous for an aim correction and still refuses anything that is not
    /// plausibly the intended target.
    /// </summary>
    internal const float LaserMaxMissOvershootFactor = 3f;

    /// <summary>
    /// Accept margin on a fan card's half-extents for the LASER pick — the browse/item arcs' copy
    /// of <c>CardFan.TryRaycast</c>'s long-proven 1.10 (and of
    /// <c>CardsDriver.LiftHitMargin</c>). The browse arc deliberately ran EXACT half-extents; with
    /// the angular pad measured against a 4,9 cm card at reading distance evaluating to exactly
    /// 0,0 cm (log: <c>only 0.0 cm of angular pad</c> on every miss inside ~1 m), that left the
    /// browse fan with literally zero tolerance, and the log's hit/miss alternation at 0,1 / 0,2 /
    /// 0,4 cm outside the face is the result: the beam dropping off the card edge on controller
    /// jitter and on the card's own hover pop. 10 % of a half-extent is ~2,5 mm on a browse card —
    /// below the gap between neighbouring cards, so it can never pick a different card than the one
    /// under the dot, and it scales with the card because the extents are the card's live world
    /// half-extents.
    /// </summary>
    internal const float LaserAcceptMargin = 1.10f;

    /// <summary>What one fan raycast decided, kept for the diagnostic line (the pick itself runs
    /// per frame per hand and must stay silent).</summary>
    internal struct FanLaserPick
    {
        /// <summary>A card was picked.</summary>
        internal bool Hit;

        /// <summary>The pick came from the angular rescue pass, not the exact rect.</summary>
        internal bool Rescued;

        /// <summary>Picked card's name, or the nearest MISSED card's name when <see cref="Hit"/> is false.</summary>
        internal string Name;

        /// <summary>Distance along the ray to the pick / nearest miss (world units).</summary>
        internal float Distance;

        /// <summary>How far outside the exact rect the ray crossed the card's plane (world units;
        /// 0 = dead on the face).</summary>
        internal float Overshoot;

        /// <summary>The angular pad that was available at that distance (world units).</summary>
        internal float Pad;

        /// <summary>The card's face width in world units — its REAL size once divided by the rig scale.</summary>
        internal float FaceWidthWorld;

        /// <summary>
        /// Incidence: <c>dot(rayDirection, faceNormal)</c> at the pick / nearest miss, i.e. how
        /// squarely the beam met the face (1 = head-on, 0 = edge-on).
        ///
        /// <para>Logged because the user's report is specifically ANGULAR and specifically
        /// ASYMMETRIC — "Von unten hat der Collider super funktioniert, von oben kaum bis gar
        /// nicht" — while the card billboards to the HEAD and the beam leaves the HAND. Printing
        /// the incidence turns that from a hypothesis into a measurement: if the misses cluster at
        /// a low dot from above and a high one from below, the remaining cause is still angular; if
        /// the dot is healthy in both and the beam still misses, it is not.</para>
        /// </summary>
        internal float FaceDot;

        internal static FanLaserPick None => new() { Name = "none", Distance = 0f };
    }

    /// <summary>
    /// Which branch of a fan laser pick refused the card the beam came NEAREST to. Recorded so a
    /// hardware log names the rejecting test instead of leaving it to be inferred from a distance.
    /// </summary>
    internal enum LaserReject
    {
        /// <summary>Nothing refused it - the card was hit (dead-on or angular-rescued).</summary>
        None,

        /// <summary>The arc holds no testable card at all (closed, all held/inactive).</summary>
        ArcEmpty,

        /// <summary>Both poses were degenerate or the card is parentless (no rect to test).</summary>
        NoRect,

        /// <summary>The beam is numerically parallel to the face (see <see cref="LaserMinFaceDenominator"/>).</summary>
        ParallelPlane,

        /// <summary>The plane crossing lies BEHIND the ray origin.</summary>
        BehindOrigin,

        /// <summary>Crossed the plane outside the accepted rect, and outside the angular pad.</summary>
        OutsideRect,

        /// <summary>Crossed so far outside the face it was not plausibly aimed at this card
        /// (see <see cref="LaserMaxMissOvershootFactor"/>).</summary>
        BeyondMissCap,

        /// <summary>A near miss the angular pad was too small to rescue.</summary>
        PadTooSmall,
    }

    /// <summary>
    /// FULL geometric record of ONE browse-laser evaluation - everything needed to decide a
    /// head-position-dependent defect from a hardware log without re-deriving anything.
    ///
    /// <para>WHY THIS EXISTS. Four rounds of fixes were argued from a diagnostic that printed only
    /// a scalar overshoot and a scalar pad, throttled to one line per second - about fourteen lines
    /// in a whole session. That is enough to say "the beam was 0.7 cm off" and nothing at all about
    /// WHERE on the card, along WHICH axis, from WHICH ray, against WHICH of the card's two tested
    /// poses, or WHY the frame was refused. The user's report is specifically that HEAD POSITION
    /// changes the outcome while the beam leaves the HAND, and the only way that shows up is in
    /// CARD-LOCAL coordinates: a head-driven defect walks the crossing point systematically across
    /// the face, while controller jitter scatters it. So the crossing is recorded normalised to the
    /// card's own half-extents (<c>|u| &lt;= 1 &amp;&amp; |v| &lt;= 1</c> is ON the face, u = +1 is
    /// exactly the right edge) for BOTH tested poses, next to the ray, the head and both rects.</para>
    ///
    /// Plain struct, filled by <see cref="PileBrowser.TryRaycast"/> on every evaluation; nothing is
    /// formatted unless the verdict actually CHANGES (see <c>CardsDriver.LogBrowseLaser</c>).
    /// </summary>
    internal struct FanLaserTrace
    {
        /// <summary>False until a pick has filled this in (nothing to print).</summary>
        internal bool Valid;

        /// <summary>The ray the pick was handed - the SAME ray the beam is drawn along.</summary>
        internal Vector3 RayOrigin;
        internal Vector3 RayDirection;

        /// <summary>HMD position this frame - the quantity the user says must not matter.</summary>
        internal Vector3 HeadPosition;

        /// <summary>The card this trace describes (the hit, the rescue, or the nearest miss).</summary>
        internal string CardName;

        /// <summary>Which branch refused it (<see cref="LaserReject.None"/> = it was picked).</summary>
        internal LaserReject Reject;

        /// <summary>RESTING pose (home pos/rot, pop excluded) - rect and crossing.</summary>
        internal bool RestValid;
        internal Vector3 RestCenter;
        internal Vector3 RestNormal;
        internal float RestHalfW;
        internal float RestHalfH;
        internal float RestDistance;

        /// <summary>Crossing on the RESTING face in half-extent units: |u| &lt;= 1 is on the face.</summary>
        internal float RestU;
        internal float RestV;

        /// <summary>LIVE pose (the card WHERE IT VISIBLY IS, pop included) - rect and crossing.</summary>
        internal bool LiveValid;
        internal Vector3 LiveCenter;
        internal Vector3 LiveNormal;
        internal float LiveHalfW;
        internal float LiveHalfH;
        internal float LiveDistance;

        /// <summary>Crossing on the LIVE face in half-extent units: |u| &lt;= 1 is on the face.</summary>
        internal float LiveU;
        internal float LiveV;
    }

    // ---- diagnostics -------------------------------------------------------------------

    /// <summary>
    /// The winner-changed line, identical in shape for every fan (user requirement: "the log alone
    /// tells us which of the candidate causes it was"). Prints, in REAL centimetres, the winner and
    /// its two probe distances, the runner-up, the EFFECTIVE reaches actually used this frame, the
    /// card's real size and the two scales — so a hardware log can be read without arithmetic and
    /// without knowing the constants: if the winner's tip distance is inside the effective tip
    /// reach the election is healthy; if the effective reach is far off the card's real size the
    /// scale resolution is the suspect; if winner and runner-up are both 0.0 cm the colliders are
    /// overlapping again.
    /// </summary>
    internal static void LogWinner<T>(string fan, string hand, in FanSweepPick<T> pick,
        in FanReach reach) where T : class, IFanSweepTarget
    {
        T? winner = pick.Winner;
        if (winner == null)
            return;
        string runnerUp = pick.RunnerUp != null
            ? $"'{pick.RunnerUp.SweepName}' (tip {reach.Cm(pick.RunnerTip):F1} cm)"
            : "none";
        VRLog.Info("Cards",
            $"{fan} hand sweep ({hand}): WINNER '{winner.SweepName}' — tip {reach.Cm(pick.WinnerTip):F1} cm / " +
            $"palm {reach.Cm(pick.WinnerPalm):F1} cm; runner-up {runnerUp}. " +
            $"Effective reach tip {reach.Cm(reach.Tip):F1} cm / palm {reach.Cm(reach.Palm):F1} cm " +
            $"(card {reach.Cm(reach.FaceWidthWorld):F1} cm wide = {reach.RelativeSize:F2}× the hand-fan card; " +
            $"world scale {reach.WorldScale:F1}×). Ranked tip-first, one winner, colliders tiled.");
    }

    /// <summary>
    /// The nothing-won line: which card the hand came NEAREST without qualifying, both probes, and
    /// the reaches they failed — all in real centimetres. This is the line that separates "the
    /// sweep is broken" from "the hand was never close enough" on the next hardware log.
    /// </summary>
    internal static void LogNearMiss<T>(string fan, string hand, in FanSweepPick<T> pick,
        in FanReach reach) where T : class, IFanSweepTarget
    {
        T? miss = pick.Miss;
        if (miss == null)
            return;
        VRLog.Info("Cards",
            $"{fan} hand sweep ({hand}): NO winner — nearest '{miss.SweepName}' at tip {reach.Cm(pick.MissTip):F1} cm / " +
            $"palm {reach.Cm(pick.MissPalm):F1} cm, effective reach tip {reach.Cm(reach.Tip):F1} cm / " +
            $"palm {reach.Cm(reach.Palm):F1} cm (card {reach.Cm(reach.FaceWidthWorld):F1} cm wide = " +
            $"{reach.RelativeSize:F2}× the hand-fan card; world scale {reach.WorldScale:F1}×). " +
            "The hand was outside BOTH reaches — this is distance, not arbitration.");
    }

    /// <summary>
    /// The laser line: whether the ray hit, WHAT it hit, whether it needed the angular rescue, how
    /// far off the exact rect it was against the pad that was available, the card's real size — and
    /// the press verdict handed in by the caller (delivered / suppressed by a modal / no trigger).
    /// Together with <see cref="LogWinner{T}"/> this is the whole decision chain for one fan in two
    /// lines: pointed at nothing, pointed at the wrong card, or pointed correctly and the press was
    /// eaten downstream.
    /// </summary>
    internal static void LogLaser(string fan, string hand, in FanLaserPick pick, float worldScale,
        string pressVerdict)
    {
        float scale = Mathf.Max(worldScale, 1e-4f);
        string what = pick.Hit
            ? $"HIT '{pick.Name}' at {pick.Distance / scale * 100f:F1} cm along the ray" +
              (pick.Rescued
                  ? $" (angular RESCUE: {pick.Overshoot / scale * 100f:F1} cm outside the exact face, " +
                    $"pad {pick.Pad / scale * 100f:F1} cm at {LaserMinHalfAngleDegrees:F2}° half-angle)"
                  : " (dead on the face)")
            : $"MISS — nearest '{pick.Name}' was {pick.Overshoot / scale * 100f:F1} cm outside its face " +
              $"with only {pick.Pad / scale * 100f:F1} cm of angular pad";
        // Incidence, in degrees off the face normal — the measurement that decides whether an
        // "it misses from above" report is still an angular problem (see FanLaserPick.FaceDot).
        string incidence = pick.FaceDot > 0f
            ? $", met the face at {Mathf.Acos(Mathf.Clamp01(pick.FaceDot)) * Mathf.Rad2Deg:F0}° " +
              $"off its normal (dot {pick.FaceDot:F2})"
            : string.Empty;
        VRLog.Info("Cards",
            $"{fan} laser ({hand}): {what}. Card {pick.FaceWidthWorld / scale * 100f:F1} cm wide, " +
            $"world scale {scale:F1}×{incidence}. Press: {pressVerdict}.");
    }
}
