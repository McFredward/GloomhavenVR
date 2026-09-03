using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// BOARD VOLUME — an OBSERVATION of where the player's head is relative to the board, the
/// RETIREMENT RECORD of the two policies that were built on it, and (ModBuild 271) the ONE latch
/// derived from it that does gate a fade. Read the whole record before touching any of it: this
/// is the third attempt at this feature and the first two were rejected on hardware.
///
/// <para>WHAT WAS TRIED, AND WHAT THE HARDWARE SAID.
/// <list type="number">
/// <item>ModBuild 241-250 — A RAISED BAR. While the head read INSIDE, the coverage pair
///   0.98/0.90 was substituted for the live bars. It was inert: <c>BlockedFraction</c>'s
///   head-inside-AABB shortcut returned a hard <c>1f</c> from OPEN FLOOR, because the AABB it
///   tested was the segment's union box, and 1.00 clears 0.98 exactly as easily as it clears
///   0.10. 75 diag samples in the ModBuild 250 log carry that signature.</item>
/// <item>ModBuild 251 — A HARD STAND-DOWN. While the head read INSIDE, this client's own
///   occlusion decision was forced OFF for every wall at once, with a mesh-keyed carve-out. It
///   worked exactly as designed and the user rejected it in one build:
///   <i>"Seit deiner letzten Änderung gibt es in der Map nur noch zwei Stati: Entweder alle
///   grünen Wände verschwinden auf einmal, oder alle sind da. Ich will aber das jede Wand
///   einzeln verschwinden kann und andere bleiben."</i></item>
/// </list></para>
///
/// <para>WHY BOTH FAILED — AND THE 2026-08-25 CORRECTION TO THAT DIAGNOSIS. The verdict written
/// here after ModBuild 251 was "a scene-wide switch cannot express a per-wall fact". That is
/// half of it, and it is the half that was WRONG about the mechanism. The record's own numbers
/// say the rest, and they say it about the TRIGGER. In the ModBuild 251
/// log the verdict flips YES/no four times in one session, and the deciding term is
/// <c>FOOTPRINT</c> as often as <c>HEIGHT</c> (27 against 26):
/// <c>INSIDE THE MAP: YES — head (0.1, 2.78, 12.4) … deciding term FOOTPRINT (the head is over
/// the board) … 0.34 m above the floor plane, walls 0.61 m tall</c>. Those are real metres: the
/// board is a diorama with 61 cm walls and his eyes are a third of a metre above its floor
/// plane. "INSIDE THE MAP" was firing when he leaned over his own table. Each rising edge then
/// ran a release across ALL segments and each pass forced them ALL solid —
/// <c>WALLS FORCED SOLID (INSIDE): 6 wall(s) held … 6 wall(s) were released on entry</c> — which
/// is exactly the "alle auf einmal" he reported, and is also a prime suspect for the un-animated
/// return he reported in the same breath.</para>
///
/// <para>SO: 0.61 m WALLS ARE A MAN LEANING OVER HIS OWN TABLE. The board at a tabletop zoom is
/// a diorama, its volume is knee-high in real metres, and the head enters it during ordinary
/// play. Compare the ModBuild 270 log, taken while he really had zoomed himself in:
/// <c>INSIDE THE MAP: YES [EDGE] … deciding term HEIGHT … at rig scale 1.66 wu per metre:
/// 1.06 m above the floor plane, walls 1.62 m tall</c>, and a second at rig scale 1.42 reading
/// <c>1.57 m above the floor plane, walls 1.88 m tall</c>. Two populations, no overlap: 0.61 m
/// against 1.62-1.88 m. The discriminator the old trigger lacked is THE BOARD'S SCALE IN REAL
/// METRES, and it was printed on every one of those lines while nothing read it.</para>
///
/// <para>WHAT REPLACED IT OUTSIDE THE WALK-IN MODE: NOTHING, and that is still true. The
/// per-wall coverage metric is the whole decision everywhere the walk-in latch is not engaged,
/// which is
/// what the user asked for in his own words — <i>"Es sollte anhand der verdeckten Boden-tiles des
/// jeweiligen Raumes berechnet werden, oder?"</i> — and what the subsystem was always designed to
/// do. The narrow case the stand-down existed to protect (his head genuinely inside masonry) is
/// already handled PER WALL by the ordinary ray test: a ray whose origin is inside a piece's
/// bounds intersects it at distance 0, so a wall the head is buried in blocks exactly the
/// samples it really covers and dissolves on its own coverage. ModBuild 255 deleted the hard
/// <c>1f</c> shortcut that used to assert this instead of measuring it —
/// <see cref="FadeDriver.HeadInsideWallMesh"/> survives only as the observation printed on the
/// PER-WALL line. A global rule was never needed for any of it.</para>
///
/// <para>THE THIRD ATTEMPT — ModBuild 271, AND THE REQUEST THAT AUTHORISED IT. The standing rule
/// that used to sit here said this subsystem may not acquire a scene-wide fade switch. The user
/// lifted it himself on 2026-08-25, in as many words: <i>"Wenn ein Spieler IN das Spielfeld geht
/// weil er so nah ranzoomed und dann im Spielfeld ist will ich, dass ein spezieller Modus
/// aktiviert wird in dem ausnahmslos alle Wände sichtbar sind und nichts mehr faded. Das soll in
/// Erweitert deaktivierbar sein."</i> The rule is therefore lifted DELIBERATELY, once, for this
/// one mode — not forgotten. Everything outside the mode is still decided per wall, by that
/// wall's own coverage, and both of his earlier rulings still hold there.</para>
///
/// <para>WHAT THE THIRD ATTEMPT DOES DIFFERENTLY. Not the mechanism: forcing every wall's state
/// off and letting the ordinary ramp animate them back is exactly what ModBuild 251 did and the
/// record above says it "worked exactly as designed". What changes is the TRIGGER.
/// <see cref="FadeDriver.UpdateWalkInside"/> derives a second, strictly narrower latch —
/// <c>_walkInside</c> — that requires ALL of:
/// <list type="number">
/// <item>the ordinary <c>_insideBoard</c> verdict — its Schmitt pair became the live dials
///   <see cref="WallFadeTuning.InsideEnterDepthFraction"/> /
///   <see cref="WallFadeTuning.InsideExitDepthFraction"/> at ModBuild 272, shipped at exactly
///   the constants they replaced, so this term is unchanged unless the user moves it;</item>
/// <item>the board's wall crest at <see cref="WallFadeTuning.WalkInMinCrestMetres"/> or more IN
///   REAL METRES (<c>_live.BoardCrestWU / rigScale</c>), default 1.20 m, with its own release band at
///   <see cref="WallFadeTuning.WalkInCrestReleaseFraction"/> (0.85) of the bar. THIS is the term
///   the two failures lacked, and it is the one that refuses a 0.61 m tabletop outright — the
///   user can switch it off by setting the bar to 0, which hands the 251 behaviour back and is
///   named as such in both the bound description and the falsifier line;</item>
/// <item>the HEIGHT slab genuinely inside (<c>_lastInsideMarginY &lt;
///   -WalkInHeadBelowCrestFraction * C</c>, shipped fraction 0, so <c>&lt; 0</c>) — the FOOTPRINT
///   term alone, which decided the 251 verdict 27 times against HEIGHT's 26, means "the head is
///   over the board" and is never enough;</item>
/// <item>a readable rig scale, because a metre column computed from an unreadable scale is not a
///   measurement.</item>
/// </list>
/// It is switchable off at <c>[WallFade] WalkInStandDown</c> (Erweitert ▸ Bild &amp; Darstellung), which
/// is the second half of the same request, and both terms are printed live on the
/// <c>INSIDE THE MAP</c> line with PASS/FAIL — including, when the latch refuses while INSIDE
/// holds, the NAME and VALUE of the term that refused.</para>
///
/// <para>ModBuild 272 — THE TRIGGER HANDED OVER. The user, 2026-08-25: <i>"Bitte gebe mir eine
/// Einstellmöglich in dem ich die parameter selber tunen kann wann der Modus aktiv wird, in dem
/// man IN einem Spielfeld ist und die Wände nicht mehr faden."</i> Every number that decides WHEN
/// the latch engages is a <c>[WallFade]</c> config entry now — the crest bar and its release
/// band, the INSIDE Schmitt pair, the latch's own two dwells, and how far below the crest plane
/// the head must be — each read LIVE per evaluation and each printed on the falsifier line as its
/// CONFIGURED value, so a tuned install never reads a log describing the shipped build. Nothing
/// was retuned: every default equals the constant it replaced, so a fresh install behaves exactly
/// as ModBuild 271 did. What the hardware gave him to aim at: that session's crest readings were
/// 2.31 m (x9) and 1.42 m (x1), both over the bar, where the mode engaged twice; and 0.94 m (x3)
/// and 0.82 m (x2), both under it, where it refused. The boundary he now owns sits in that gap.
/// The crest term can also be switched OFF outright (bar = 0). That is the ModBuild 251
/// behaviour, rejected in one session — offered because he asked to own these numbers, refused
/// as a default, and named in full both in the bound description and every time the log prints
/// the term.</para>
///
/// <para>THE RULE THAT SURVIVES: no OTHER scene-wide fade switch. This one exists because the
/// user asked for a mode, by name, and gave it a config switch in the same sentence. If a future
/// symptom that is NOT this mode seems to want a global rule, it is still a per-wall measurement
/// that is wrong — two builds were spent proving that and neither is being re-run.</para>
///
/// <para>WHAT SURVIVES, AND WHY. The board volume and the INSIDE verdict are kept as a pure
/// DIAGNOSTIC. Their commit phase is already priced in the BUDGET line, the test is ~15 float
/// ops, and "where was his head relative to the board, in real metres" is the single question
/// that would have caught both failures above in round one — the metre column is what shows a
/// 0.61 m wall and a head 0.34 m up — the very column ModBuild 271 promoted to a decider. The
/// <c>INSIDE THE MAP</c> line still says explicitly that THIS verdict gates nothing, and now
/// also carries the walk-in latch that does, term by term, so neither can be mistaken for the
/// other.</para>
///
/// <para>THE TEST ITSELF (unchanged, diagnostic only). INSIDE means the head is inside the BOARD
/// VOLUME — the union XZ footprint of every decision-valid room and its walls, from the board's
/// floor plane up to the median WALL CREST. The measured quantity is the signed distance of the
/// head to that box (negative inside), through a Schmitt pair expressed as fractions of the
/// crest height C so they scale with the board: enter at <c>sd &lt;= -0.10·C</c>, leave at
/// <c>sd &gt;= +0.35·C</c>, dwells 0.20 s in and <see cref="WallFadeTuning.DwellMoved"/> out.
/// Those two fractions are <see cref="WallFadeTuning.InsideEnterDepthFraction"/> and
/// <see cref="WallFadeTuning.InsideExitDepthFraction"/> since ModBuild 272 — live dials shipped
/// at exactly the constants they replaced.
/// The median (not the max) crest keeps a keep's stacked superstructure from putting the crest
/// plane above every wall in the scenario. The 3D map room has no occlusion volumes at all, so
/// the volume is never valid there.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    /// <summary>
    /// IS THE PLAYER STANDING INSIDE THE DIORAMA RIGHT NOW? The single read-only view of
    /// <c>FadeDriver._walkInside</c> — the STRICTLY NARROWER latch (board volume AND a readable rig
    /// scale AND a crest of at least <c>WalkInMinCrestMetres</c> in REAL metres AND the head
    /// genuinely below that crest plane), not the looser <c>_insideBoard</c> observation, which is
    /// true while merely leaning over a tabletop and by its own doc comment "still gates nothing".
    ///
    /// <para>Added for <c>Board/FigureGrab</c> (user, ModBuild 286: "Wenn ich mich im Modus befinde,
    /// dass ich IN der Welt drin bin … möchte ich optional das highlighting der figuren deaktivieren
    /// können"). The wall topic was formally closed at ModBuild 284 and this is deliberately the
    /// whole of the change to it: one accessor, no behaviour, no new diagnostics, no new state. Read
    /// through the driver singleton in the same shape as <c>SampleFadedWallKeys</c>, so it is false
    /// whenever the subsystem is not standing rather than throwing or latching.</para>
    /// </summary>
    internal static bool WalkInsideEngaged => _driver != null && _driver.WalkInsideActive;

    private sealed partial class FadeDriver
    {
        /// <summary>See <see cref="WalkInsideEngaged"/>, the only reader outside this type.</summary>
        internal bool WalkInsideActive => _walkInside;

        // --- INSIDE-THE-MAP bars (see class header) ------------------------------------------
        // Both bars are FRACTIONS OF THE CREST HEIGHT C (the board's own median wall height in
        // world units), never absolute distances: the board is the yardstick, so the boundary
        // means the same thing on a low ruin and on a keep. They were `private const 0.10f` and
        // `0.35f` until ModBuild 272 promoted them to [WallFade] InsideEnterDepthFraction /
        // InsideExitDepthFraction on the user's request to tune the walk-in trigger himself. They
        // stay FRACTIONS deliberately — that is the property that makes one setting work on every
        // board — and they are read LIVE on every evaluation, never cached at bind, so a value
        // changed in the in-VR menu takes effect on the next frame.
        /// <summary>How many walls the per-wall falsifier names individually before it starts
        /// counting the rest. The whole point of that line is to show the walls DISAGREEING, so
        /// it has to name enough of them to see a split.</summary>
        private const int PerWallNameCap = 8;
        /// <summary>Floor under the derived crest height, so a degenerate or half-generated
        /// board can never produce a zero-height volume that the head is trivially outside.</summary>
        private const float BoardCrestMinWU = 0.5f;
        /// <summary>Cadence of the falsifier line while INSIDE holds. Edges print unthrottled.</summary>
        private const float InsideLogIntervalSeconds = 2f;
        // WALK-IN hysteresis on the REAL-METRE crest term (ModBuild 271) was a `private const
        // 0.85f` here; ModBuild 272 promoted it to [WallFade] WalkInCrestReleaseFraction. It is
        // still a FRACTION and not a second metre bar, for the same reason the INSIDE bars are
        // fractions: the bar it bands is itself a live dial, and a band expressed as a fraction
        // of it moves with it instead of silently inverting when the bar is lowered past it.

        // --- cached board volume (rebuilt once per rescan, read O(1) per frame) --------------
        private int _boardVolumeWalls;
        /// <summary>Per-segment crest samples; a field so the commit phase allocates nothing.</summary>
        private readonly List<float> _crestScratch = new();
        // Announce-once signature (rooms, walls, and the volume quantized to 0.1 wu) so the
        // BOARD VOLUME line prints on a real change — a reveal, a fresh scenario — and not
        // every two seconds forever.
        private int _bvSigRooms = -1, _bvSigWalls = -1, _bvSigCrest, _bvSigX, _bvSigZ;

        // --- INSIDE state ------------------------------------------------------------------
        private bool _insideBoard;
        private bool _insidePending;
        private float _insidePendingSince;
        /// <summary>Last signed distance of the head to the board volume (wu, negative inside).</summary>
        private float _lastInsideSigned;
        /// <summary>Last per-axis slabs of that distance — which TERM decided the verdict.</summary>
        private float _lastInsideMarginY;
        private float _lastInsideMarginXZ;
        private float _nextInsideLogTime;

        // --- WALK-IN STAND-DOWN state (ModBuild 271 — see the class header) -----------------
        /// <summary>THE ONLY LATCH IN THIS SUBSYSTEM THAT GATES A FADE. True while the player has
        /// zoomed himself INTO the play field; every wall is then held solid. Strictly narrower
        /// than <see cref="_insideBoard"/>, which still gates nothing.</summary>
        private bool _walkInside;
        private bool _walkInsidePending;
        private float _walkInsidePendingSince;
        /// <summary>Live value of every term the latch is a conjunction of, kept as fields so the
        /// falsifier can print WHICH term refused and at WHAT value — not merely that it did.
        /// A change-gated line whose reason string is constant prints once and then reads like a
        /// dead instrument, so all of these travel on the line as numbers.</summary>
        private float _walkCrestMetres = -1f;
        private float _walkRigScale;
        private float _walkCrestBar;
        /// <summary>The crest bar the CURRENT pass actually had to clear — the bar itself while
        /// the mode is off, the bar times its release fraction while it holds, 0 while the term
        /// is switched off. A field so the log prints the number that was USED, never a literal
        /// or a re-derivation that could drift from it.</summary>
        private float _walkCrestNeed;
        /// <summary>True while <c>[WallFade] WalkInMinCrestMetres</c> is 0 — the crest term is
        /// switched off outright and the mode will fire on a tabletop diorama. Printed on every
        /// falsifier line, because a session behaving like the rejected ModBuild 251 build must
        /// never be mistakable for a bug.</summary>
        private bool _walkCrestDisabled;
        /// <summary>The Y-slab value the CURRENT pass had to be below: minus
        /// <c>[WallFade] WalkInHeadBelowCrestFraction</c> times the crest height, so 0 at the
        /// shipped default and the ModBuild 271 test exactly.</summary>
        private float _walkHeightNeed;
        private bool _walkTermScale, _walkTermCrest, _walkTermHeight;
        /// <summary>The first term that refused, by NAME and VALUE, whenever the walk-in latch is
        /// false. "-" only while it holds.</summary>
        private string _walkRefusal = "not evaluated yet";
        /// <summary>Set on a latch EDGE, consumed after the decision loop — the census it carries
        /// (how many walls the latch held, how many of them were hidden at that moment) can only
        /// be counted by the loop itself.</summary>
        private bool _walkEdgePending;
        /// <summary>Per-pass census written by the walk-in branch of the decision loop.</summary>
        private int _walkHeld, _walkHeldHidden;
        /// <summary>Session tally of walk-in edges, so a log with no edges is distinguishable
        /// from a log where the instrument never ran.</summary>
        private int _walkEdges;

        // --- R2 PER-WALL INDEPENDENCE CENSUS (measured per pass, never intended) ------------
        /// <summary>Fadeable walls judged this pass, and how many of them ended up faded. The
        /// whole point of the falsifier is that these two disagree: 0 or N means every wall
        /// reached the same verdict, anything between them is the independence the user asked
        /// for, demonstrated rather than asserted.</summary>
        private int _pwTotal;
        private int _pwFaded;
        /// <summary>Per-wall detail, in the order the segment table yields it: name, own room,
        /// own smoothed coverage, own verdict. This is the line that answers "jede Wand
        /// einzeln".</summary>
        private readonly List<string> _pwNames = new();
        /// <summary>Session tallies: passes where the walls DISAGREED versus passes where they
        /// were unanimous. A session with zero mixed passes is the defect he reported.</summary>
        private int _pwMixedPasses;
        private int _pwUniformPasses;
        /// <summary>Widest and narrowest smoothed coverage seen this pass, with names — the
        /// spread that proves the walls are being measured separately.</summary>
        private float _pwMaxSmooth, _pwMinSmooth;
        private string _pwMaxWall = "-", _pwMinWall = "-";
        /// <summary>Walls whose fade came from somewhere other than their own decision.</summary>
        private int _pwPeerDriven, _pwGateDriven;
        private float _nextPerWallLogTime;
        /// <summary>Split-run members named on the PER-WALL line under the run that decides them
        /// (ModBuild 259). Their own coverage is still measured and still printed; what they no
        /// longer do is count as independent walls in "faded X of Y".</summary>
        private readonly List<string> _pwRunPieceNames = new();
        /// <summary>
        /// MODBUILD 269 — HOW MANY split-run pieces were decision-eligible this pass, as opposed
        /// to how many the line could name. This counter exists because the ModBuild-267 log's
        /// split-run clause named FOUR pieces out of 253, in <c>_live.Segments</c> insertion order,
        /// with NO truncation marker of any kind — while the very same line ends its per-wall
        /// clause with an honest <c>+28 more</c>. A reader who greps that log therefore sees a
        /// complete-looking two-to-four-entry list and reasons from it as if it were the
        /// population; that is exactly what happened this round, and the conclusion drawn from it
        /// ("the shelf's run votes solid while the rest of its wall fades") was false in three
        /// separate ways that the missing marker hid. Same lesson as
        /// <c>a-summary-stat-is-not-the-field</c> and <c>read-the-whole-distribution</c>, one
        /// clause later.
        /// </summary>
        private int _pwRunPieceTotal;
        /// <summary>Raised from 4 in ModBuild 269. Four names of 253 pieces is a sample of 1.6 %
        /// taken in dictionary order, i.e. always the same four pieces, and it is not a sample of
        /// anything the reader chose. Twelve still fits the line's budget and — with
        /// <see cref="_pwRunPieceTotal"/> printed beside it — can no longer be mistaken for the
        /// whole set.</summary>
        private const int PerWallRunPieceCap = 12;

        // --- R1 ANIMATION-PATH CENSUS ------------------------------------------------------
        /// <summary>Wall renderers mid-DISSOLVE on the noise map — the only path that produces
        /// intermediate pixels — versus those sitting in the HELD state on the occluded map,
        /// where the noise term is multiplied by zero and the result is binary. The residual
        /// discontinuity in the subsystem is the boundary between the two at Fade == 1.</summary>
        private int _animSmooth;
        private int _animStepped;
        private readonly List<string> _animSteppedNames = new();

        // --- R1 FOLIAGE CHANNEL CENSUS -----------------------------------------------------
        /// <summary>THE BLIND SPOT THIS ROUND CLOSED. The ModBuild 253 ANIMATION line read
        /// "0 on the stepped two-texture path: every transition in flight is animated end to
        /// end" while the user was watching walls pop. It was true and it was not the picture:
        /// it counted only the WALL renderers, and the DISSOLVE CENSUS it sat next to has no
        /// foliage bucket either. 345 foliage attachments — the visible mass of every scrub
        /// wall — were animated by nobody's measurement. These count them by the channel each
        /// piece actually got.</summary>
        private int _folNative, _folSwapped, _folOwnChannel, _folNoChannel;
        private readonly List<string> _folNoChannelNames = new();

        // --- R1 STEP CENSUS: discontinuities BOLTED ONTO the ramp ---------------------------
        /// <summary>
        /// WHY A SECOND ANIMATION INSTRUMENT. The ANIMATION line measures the SLOPE — it proves
        /// every fade in flight is moving through intermediate values. It cannot see a step
        /// applied on the ramp's first or last frame, and in ModBuild 254 it read
        /// "every foliage piece in flight is animated, not switched" while the user was watching
        /// walls pop. A symmetric ramp cannot produce an asymmetric artifact, so the pop had to
        /// be an EDGE, and no instrument could see edges. These count them, by which end of the
        /// transition they land on.
        ///
        /// <para>OUT-EDGE events happen as the fade leaves 0 — while the wall still looks solid,
        /// so anything applied here is maximally visible. IN-EDGE events happen as it reaches 0,
        /// when the wall is already solid and nobody can see them. A build where OUT is non-zero
        /// and IN is non-zero for the same cause is the asymmetry the user reported.</para>
        /// </summary>
        private int _stepOutBlockInstalled, _stepInBlockCleared;
        private int _stepOutSwapInstalled, _stepInSwapRemoved;
        private readonly List<string> _stepOutNames = new();

        // --- R2 NUMERATOR ADMISSION CENSUS -------------------------------------------------
        /// <summary>Per wall: how many of its pieces the standing test admitted to the occlusion
        /// numerator and how many it excluded as ground dressing, with the widest excluded piece
        /// named. This is what settles "which renderer inflated Wall 2" without another round.
        /// </summary>
        private readonly List<string> _admitNames = new();
        /// <summary>Which wall's masonry currently contains the head, if any — an observation
        /// since ModBuild 255, when the hard-1f shortcut it used to feed was deleted.</summary>
        private string _headInMasonryWall = "-";
        /// <summary>Floor-grid cell count of the room the censused walls were judged against —
        /// the denominator whose reciprocal is the smallest coverage difference the metric can
        /// express at all.</summary>
        private int _pwCells;
        /// <summary>Head position of the pass being censused, so the observation above is taken
        /// against the same pose the verdicts were.</summary>
        private Vector3 _lastHeadPos;

        // --- SPLIT RUNS: one wall, one verdict (ModBuild 259) --------------------------------
        /// <summary>
        /// THE DECISION UNIT OF A SPLIT WALL. <see cref="NeutralizeEngulfingSegments"/> carves a
        /// room-engulfing wall run into one segment per renderer so that its union AABB — the box
        /// that CONTAINS the room's own floor samples — can never be the occluder box. That fixed
        /// the ground defect and created this one: ~40 pieces of ONE wall each ran their own
        /// Schmitt trigger, so the wall stopped being a thing that appears or disappears.
        ///
        /// <para>THE USER'S RULING (2026-08-24): <i>"Entweder verschwindet die ganze Wand mit
        /// ALLEM was dazu gehört (Bäume, Gestrüp, etc.) oder sie ist vollständig da. So ein
        /// Zwischending soll es nicht geben."</i> Both of his rulings hold at once: walls decide
        /// INDEPENDENTLY of each other (2026-08-24, ModBuild 252) and a wall takes everything
        /// belonging to it with it. A run is one wall; its pieces are not other walls.</para>
        ///
        /// <para>WHAT THE HARDWARE SAID. ModBuild 258 log, last diag: <c>'Wall 2' blk16/16 ON</c>
        /// beside <c>'FR_Pillar_Tree_Trunk_01' blk2/16 off</c> and <c>'FR_Tree_01 (1)' blk2/16
        /// off</c>. Session fade-ON counts: the four unsplit walls switch 7 times BETWEEN THEM
        /// ('Wall 4' 3, 'Wall 3' 3, 'Wall 2' 1) while individual trunks, stumps and verges switch
        /// 69. The heartbeat names the mechanism outright — <c>171 from the wall cache + 4 ADOPTED
        /// by shader … 1 room-engulfing wall(s) split per renderer</c>: only FOUR renderers in the
        /// whole session were adopted, so this population is the SPLIT, not the adoption sweep.
        /// </para>
        ///
        /// <para>WHAT THIS CHANGES AND WHAT IT DOES NOT. Every piece still measures its own
        /// coverage against its own renderer — <c>BlockedFraction</c> is called exactly as before,
        /// no renderer moves between lists, no numerator is re-based. What changes is the number
        /// the Schmitt trigger reads: the RUN's coverage is the UNION of its members' blocked
        /// cells over the room grid, which is precisely what the unsplit wall's mesh-accurate
        /// narrow phase (<see cref="RayHitsWallMesh"/>, per-renderer AABBs) would have produced —
        /// minus the <c>Contains()</c> claims the engulfing union box used to make. Strictly the
        /// same metric, strictly the same bars, one decider instead of forty.</para>
        ///
        /// <para>THE MAX OVER ROOMS is not a new rule: it is the seam-wall precedent already in
        /// <see cref="BlockedFraction"/> — a run bordering two rooms fades from either side.</para>
        ///
        /// <para>ONE-WAY BY CONSTRUCTION. A run reads its members' cells; a member reads its run's
        /// state. An unsplit <c>ProceduralWall</c> has no <see cref="Segment.RunOwner"/>, is never
        /// visited by <see cref="EvaluateSplitRuns"/>, and cannot be reached by any piece of any
        /// run. 'Wall 4' — which the user confirmed correct this round — is in that class.</para>
        /// </summary>
        private sealed class WallRun
        {
            public Component? Anchor;
            public float Smooth;
            public bool SmoothInit;
            public bool PendingRaw;
            public float PendingSince;
            public bool State;
            /// <summary>Pieces that contributed a measurement this pass (fail-safe-held members
            /// are excluded — they are not deciders and are named by the leftover audit).</summary>
            public int Deciders;
            /// <summary>Pieces carrying this run's key at all, deciders or not.</summary>
            public int Members;
            /// <summary>ModBuild 261: of the non-deciders, the ones that own NO renderer because
            /// the wall choke point refused them (<see cref="Segment.GeometryRefusedWhy"/>). They
            /// are NOT "held solid by a fail-safe" — they were never wall geometry, and calling
            /// them fail-safe-held is what pointed ModBuild 260's report at the wrong rule.</summary>
            public int Refused;
            /// <summary>ModBuild 261: members recruited by <c>SplitRunAdoptGroundScenery</c>.
            /// They ride the verdict and never contribute a cell to it — see
            /// <see cref="Segment.RunPassenger"/>.</summary>
            public int Passengers;
            /// <summary>Union size, room total and room index of the winning room.</summary>
            public int Blocked;
            public int Total;
            public int Room = -1;
            /// <summary>Widest single-member reading this pass — the number that proves the union
            /// is doing work: a run whose union equals its best member gained nothing.</summary>
            public int BestMember;
            public string BestMemberName = "-";
            /// <summary>Evaluation generation this run was last seen in (pruning).</summary>
            public int Seen = -1;
            /// <summary>ModBuild 261 IMMEDIACY AUDIT (user ruling 2026-08-24: "direkt wieder
            /// unfaded wenn es keine spielbaren tiles verdeckt"). Wall-clock this run has spent
            /// FADED with its UNION at or above the exit bar while its widest single member is
            /// already BELOW it — i.e. time the union alone is holding the wall down. Continuous
            /// stretch and session worst. Raw coverage on both sides, deliberately not the EMA:
            /// the members have no run-comparable EMA, and an unmatched pair would be a
            /// comparison of two different filters. Zero here means the union costs nothing on
            /// the exit and the delay is entirely bar + dwell.</summary>
            public float UnionHoldSeconds;
            public float UnionHoldWorst;
            /// <summary>Last evaluation time, for the dt this audit integrates.</summary>
            public float LastEval;
            /// <summary>Highest fade any member has reached — the audit's "is this run actually
            /// gone from the picture yet", so a sweep landing mid-ramp reports nothing.</summary>
            public float MaxFade;
            /// <summary>Blocked cells, packed as (room &lt;&lt; 20 | cell) so one set spans every
            /// room a seam run borders without a second dictionary.</summary>
            public readonly HashSet<long> Cells = new();
            /// <summary>Distinct blocked cells per room — the per-room numerators.</summary>
            public readonly Dictionary<int, int> RoomHits = new();
        }

        private readonly Dictionary<Component, WallRun> _runs = new();
        private readonly List<Component> _runDeadKeys = new();
        private int _runGeneration;
        /// <summary>Split pieces whose run anchor is gone (Apparance destroyed the wall between
        /// rescans). They fall back to their OWN decision — never to a latch — and are counted
        /// here so an unexpectedly large orphan population is visible instead of silent.</summary>
        private int _runOrphans;
        private int _runsTotal, _runsFaded, _runMembersTotal, _runMembersHeld;
        /// <summary>ModBuild 261: the refused subset of <see cref="_runMembersHeld"/> — pieces
        /// that own no renderer at all. See <see cref="WallRun.Refused"/>.</summary>
        private int _runMembersRefused;
        /// <summary>ModBuild 261: members recruited by <c>SplitRunAdoptGroundScenery</c> this
        /// pass — zero while the dial is off, which is how it ships.</summary>
        private int _runMembersPassenger;
        /// <summary>ModBuild 261 immediacy audit, scene-level: the widest run's live union
        /// numerator/denominator and its widest single member, sampled EVERY evaluation so the
        /// 2 s census carries the pair instead of only the edges carrying it. Plus the current
        /// and session-worst seconds a union alone has held a faded run down (WallRun).</summary>
        private int _runUnionBlocked, _runUnionTotal, _runUnionBest;
        private float _runUnionHoldNow, _runUnionHoldWorst;
        private float _nextRunLogTime;
        /// <summary>Session tally of run-level fade edges — the counterpart of the per-piece
        /// `fade ON` census that showed 69 switches across 12 pieces of ONE wall.</summary>
        private int _runFadeEdges;

        /// <summary>The state a run-driven member must take. Falls back to the member's own last
        /// state if its run vanished between the pre-pass and the loop (impossible in one frame,
        /// but this may not be the thing that latches a wall).</summary>
        private bool RunStateOf(Segment seg)
        {
            if (seg.RunOwner != null && _runs.TryGetValue(seg.RunOwner, out WallRun? run))
                return run.State;
            return seg.State;
        }

        /// <summary>The run's smoothed coverage — the live number that decides a run-driven
        /// member, and therefore the one the latch watchdog must judge it against.</summary>
        private float RunSmoothOf(Segment seg) =>
            seg.RunOwner != null && _runs.TryGetValue(seg.RunOwner, out WallRun? run)
                ? run.Smooth
                : seg.Smooth;

        /// <summary>Dial turned off mid-session: hand every piece back its own decision and drop
        /// the run table, so nothing keeps driving a member from a stale verdict.</summary>
        private void ClearSplitRunDrive()
        {
            if (_runs.Count > 0)
                _runs.Clear();
            _runsTotal = 0;
            _runsFaded = 0;
            _runMembersTotal = 0;
            _runMembersHeld = 0;
            _runMembersRefused = 0;
            _runMembersPassenger = 0;
            _runOrphans = 0;
            _runLeftover = 0;
            _runLeftoverNames.Clear();
            _runLeftoverByReason.Clear();
            _runLeftoverByClass.Clear();
            _runLeftoverAllowed.Clear();
            foreach (Segment seg in _live.Segments.Values)
                seg.RunDriven = false;
        }

        /// <summary>
        /// Measure every split-run member, union their blocked cells per run, and drive ONE
        /// Schmitt trigger + dwell per run. Called from the tick before the decision loop, on
        /// evaluation frames only. Allocation-free after warm-up: the sets and the per-run
        /// dictionaries keep their capacity.
        /// </summary>
        private void EvaluateSplitRuns(Vector3 headPos, float now, float fracStep,
            float onFraction, float offFraction, float exitDwell)
        {
            _runGeneration++;
            _runOrphans = 0;
            _runMembersTotal = 0;
            _runMembersHeld = 0;
            _runMembersRefused = 0;
            _runMembersPassenger = 0;

            // ---- pass 1: measure each piece, union its cells into its run ------------------
            foreach (Segment seg in _live.Segments.Values)
            {
                seg.RunDriven = false;
                if (!seg.FromSplitRun)
                    continue;
                // The run must still BE a split run right now — `_live.SplitAnchors` is the live
                // register and it is cleared on a scene load. A stale RunOwner from a group that
                // has since been re-merged must not keep driving anything.
                if (seg.RunOwner == null || !_live.SplitAnchors.Contains(seg.RunOwner))
                {
                    // ORPHAN: the run this piece was carved from is destroyed. Keep its own
                    // decision (the pre-259 behaviour) rather than holding it solid — a piece
                    // frozen solid in front of the board is the very complaint, and a piece
                    // frozen faded would delete geometry with no owner to bring it back.
                    _runOrphans++;
                    continue;
                }
                _runMembersTotal++;
                if (!_runs.TryGetValue(seg.RunOwner, out WallRun? run))
                {
                    run = new WallRun { Anchor = seg.RunOwner };
                    _runs.Add(seg.RunOwner, run);
                }
                if (run.Seen != _runGeneration)
                {
                    run.Seen = _runGeneration;
                    run.Cells.Clear();
                    run.RoomHits.Clear();
                    run.Deciders = 0;
                    run.Members = 0;
                    run.Refused = 0;
                    run.Passengers = 0;
                    run.BestMember = 0;
                    run.BestMemberName = "-";
                }
                run.Members++;
                // The fail-safe branches of the decision loop own these pieces (boundless,
                // unsplittable-engulfing, doorway, room without a valid floor grid). They are
                // held SOLID by rules older than this one and must not vote — but a solid piece
                // beside a faded run IS a leftover, so the audit below names every one of them.
                if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                    || !RoomDecisionValid(seg.RoomIndex))
                {
                    _runMembersHeld++;
                    // ModBuild 261: split the held population at its real seam. A piece the wall
                    // choke point REFUSED owns no renderer, so it is not a wall being held solid
                    // — it is a floor prop that never entered the wall path. Counting the two
                    // together is why ModBuild 260's report read "99 held by an older fail-safe".
                    if (seg.GeometryRefusedWhy != null)
                    {
                        _runMembersRefused++;
                        run.Refused++;
                    }
                    continue;
                }
                // PASSENGER (ModBuild 261, [WallFade] SplitRunAdoptGroundScenery). Recruited
                // ground scenery takes the run's verdict — that is the whole point of recruiting
                // it — but it must never steer one: a bush standing a metre inside the room would
                // otherwise hide floor it has no business voting on, and the trigger would have
                // moved in the same build as the population. RunDriven, deliberately NOT a
                // Decider, and no cells into the union. Falsified by: RUN FADE union/best-single
                // numbers that differ between the two dial positions on the same scenario.
                if (seg.RunPassenger)
                {
                    run.Passengers++;
                    _runMembersPassenger++;
                    seg.RunDriven = true;
                    continue;
                }
                float fraction = BlockedFraction(seg, headPos);
                seg.LastRaw = fraction;
                // The piece's OWN EMA keeps running so the diag and the PER-WALL line still
                // report what each piece measures — the numbers that made this change are
                // exactly these, and they must stay readable after it.
                if (!seg.SmoothInit)
                {
                    seg.SmoothInit = true;
                    seg.Smooth = fraction;
                }
                else
                {
                    seg.Smooth += (fraction - seg.Smooth) * fracStep;
                }
                run.Deciders++;
                seg.RunDriven = true;
                // THE CELLS BELONG TO seg.RoomIndex, NOT TO seg.LastDecidingRoom. BlockedFraction
                // attributes cells during the OWN-room pass only (`_attributeCells = false` around
                // every alt-room pass), so for a seam piece whose ALT room won the max,
                // LastDecidingRoom names the alt room while LastBlockedCells still holds the own
                // room's indices. Unioning them under the alt room's key would mix two point sets.
                //
                // WHAT THIS COSTS, STATED: a run member's alt-room coverage does not reach the
                // union. It is not lost to the run as a whole — members standing on the other side
                // of a seam carry that room in their OWN RoomIndex and the per-room MAX below picks
                // it up — but a single piece that fades "from the other side" contributes nothing.
                // Fixing that properly means per-alt-room cell attribution in BlockedFraction,
                // which is a change to the shared metric and not this round's.
                int room = seg.RoomIndex;
                if (room < 0)
                    continue;
                for (int i = 0; i < seg.LastBlockedCells.Count; i++)
                {
                    long key = ((long)room << 20) | (uint)seg.LastBlockedCells[i];
                    if (!run.Cells.Add(key))
                        continue;
                    run.RoomHits.TryGetValue(room, out int hits);
                    run.RoomHits[room] = hits + 1;
                }
                // Comparable to the union by construction: both count OWN-room attributed cells.
                // seg.LastBlocked would not be — for a seam piece it is the alt room's count.
                if (seg.LastBlockedCells.Count > run.BestMember)
                {
                    run.BestMember = seg.LastBlockedCells.Count;
                    run.BestMemberName = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                }
            }

            // ---- pass 2: one verdict per run ----------------------------------------------
            _runsTotal = 0;
            _runsFaded = 0;
            _runUnionBlocked = 0;
            _runUnionTotal = 0;
            _runUnionBest = 0;
            _runUnionHoldNow = 0f; // per pass; the WORST is a session figure and never resets
            _runDeadKeys.Clear();
            foreach (KeyValuePair<Component, WallRun> kv in _runs)
            {
                WallRun run = kv.Value;
                if (run.Seen != _runGeneration)
                {
                    _runDeadKeys.Add(kv.Key); // no live member carries this key any more
                    continue;
                }
                // MAX over the rooms this run borders — the seam-wall precedent from
                // BlockedFraction, not a new rule: whichever room the run is currently hiding
                // is the room the player wants opened.
                float fraction = 0f;
                run.Blocked = 0;
                run.Total = 0;
                run.Room = -1;
                foreach (KeyValuePair<int, int> hit in run.RoomHits)
                {
                    if (hit.Key < 0 || hit.Key >= _live.RoomSampleCount.Count)
                        continue;
                    int total = _live.RoomSampleCount[hit.Key];
                    if (total <= 0)
                        continue;
                    float f = hit.Value / (float)total;
                    if (run.Room >= 0 && f <= fraction)
                        continue;
                    fraction = f;
                    run.Blocked = hit.Value;
                    run.Total = total;
                    run.Room = hit.Key;
                }
                // The run's EMA is the segment's EMA — one definition, in Core/OcclusionFade.cs.
                OcclusionFade.AdvanceCoverage(fraction, fracStep, ref run.Smooth, ref run.SmoothInit);
                // ---- IMMEDIACY AUDIT (see WallRun.UnionHoldSeconds) -------------------------
                // Does the UNION delay the un-fade? The RUN FADE line answers this only AT an
                // edge, where the union has already dropped under the bar by construction — so
                // it can never show the delay it is being asked about. Integrated here instead,
                // every evaluation, and reported on the 2 s census.
                float bestRaw = run.Total > 0 ? run.BestMember / (float)run.Total : 0f;
                float dtRun = run.LastEval > 0f ? Mathf.Min(now - run.LastEval, 0.5f) : 0f;
                run.LastEval = now;
                if (run.State && fraction >= offFraction && bestRaw < offFraction)
                {
                    run.UnionHoldSeconds += dtRun;
                    if (run.UnionHoldSeconds > run.UnionHoldWorst)
                        run.UnionHoldWorst = run.UnionHoldSeconds;
                }
                else
                {
                    run.UnionHoldSeconds = 0f;
                }
                if (run.Blocked > _runUnionBlocked || run.Total != _runUnionTotal)
                {
                    _runUnionBlocked = run.Blocked;
                    _runUnionTotal = run.Total;
                    _runUnionBest = run.BestMember;
                }
                if (run.UnionHoldSeconds > _runUnionHoldNow)
                    _runUnionHoldNow = run.UnionHoldSeconds;
                if (run.UnionHoldWorst > _runUnionHoldWorst)
                    _runUnionHoldWorst = run.UnionHoldWorst;
                bool raw = OcclusionFade.Above(run.Smooth, run.State, onFraction, offFraction);
                if (OcclusionFade.StepDwell(raw, now, EnterDwellSeconds, exitDwell,
                        ref run.PendingRaw, ref run.PendingSince, ref run.State))
                {
                    _runFadeEdges++;
                    LogRunFadeEdge(run);
                }
                _runsTotal++;
                if (run.State)
                    _runsFaded++;
            }
            for (int i = 0; i < _runDeadKeys.Count; i++)
                _runs.Remove(_runDeadKeys[i]);
            _runDeadKeys.Clear();
        }

        /// <summary>
        /// ONE line per RUN edge, replacing the ~40 per-piece `fade ON` lines the same event used
        /// to emit. Deliberately shaped so the next log answers the ModBuild 258 question — "how
        /// many things switched, and were they one wall or forty" — in a single grep.
        /// </summary>
        private void LogRunFadeEdge(WallRun run)
        {
            string wall = run.Anchor != null ? run.Anchor.name : "<dead>";
            VRLog.Info(Name,
                $"RUN FADE {(run.State ? "ON" : "OFF")} '{wall}' — {run.Members} piece(s) of ONE "
                + $"split wall run move together ({run.Deciders} of them measured, "
                + $"{run.Passengers} riding as PASSENGERS (recruited ground scenery — they take "
                + "this verdict and never vote on it), "
                + $"{run.Refused} REFUSED as wall geometry and owning no renderer at all, "
                + $"{run.Members - run.Deciders - run.Refused - run.Passengers} held solid by an "
                + "older fail-safe WITH geometry — ModBuild 261 split those apart, because 260 "
                + "reported them as one number and that pointed the whole round at the boundless "
                + "fail-safe instead of at the choke point). Run coverage "
                + $"{run.Blocked}/{run.Total} cell(s) of room {run.Room} = "
                + (run.Total > 0 ? (run.Blocked / (float)run.Total).ToString("F2") : "n/a")
                + $" (ema {run.Smooth:F2}) against bars {WallFadeTuning.On:F2}/"
                + $"{WallFadeTuning.Off:F2}; the widest SINGLE piece read {run.BestMember}/"
                + $"{run.Total} ('{run.BestMemberName}') — the gap between those two numbers is "
                + "exactly what the union bought, and a run where they are equal gained nothing. "
                + $"Session run edges: {_runFadeEdges}. Before ModBuild 259 this event was "
                + "up to one edge PER PIECE, which is the 69-switch tree census of the 258 log.");
        }

        /// <summary>
        /// THE FALSIFIER, READ OFF THE RENDERERS (requirement of this round; the ModBuild 252
        /// mistake was an instrument that watched the driver). For every run at full fade, ask
        /// each of its members' renderers — through <see cref="IsActuallyDrawing"/>, the same
        /// predicate the LEFTOVER audit already uses — whether it is still putting pixels on the
        /// screen while the run is gone. A non-empty list IS the photograph
        /// (neues_wandproblem.jpg), and each entry carries the REASON, because every remaining
        /// way to be left standing is a named older rule rather than this one.
        ///
        /// <para>Run at the mounted-dressing cadence (once per rescan) and reported on the SAME
        /// <c>LEFTOVER OVER A FADED WALL</c> line, so one grep still finds every leftover class.
        /// </para>
        ///
        /// <para><b>MODBUILD 261 — THE INSTRUMENT WAS BLIND TO THE POPULATION IT EXISTED FOR.</b>
        /// The ModBuild 260 log printed <c>SPLIT-RUN PIECES: 0</c> on all 122 sweeps while the
        /// same log's own RUN FADE line said 99 of 140 pieces of 'Wall 1' never move, and the
        /// video (wände_problem4.mp4) shows a standing hedge. Both were true: this sweep asked
        /// <c>seg.Renderers</c> and <c>seg.Foliage</c>, and a refused split piece has BOTH LISTS
        /// EMPTY — <see cref="RefreshSplitWall"/> adds the renderer only when
        /// <see cref="CollectWallFadeInfo"/> accepts it, and the nearest-piece foliage bind skips
        /// pieces without bounds. So the sweep polled exactly the 41 members that already work
        /// and answered honestly about them: the ModBuild 252 scar, one layer further in.</para>
        ///
        /// <para>THE FIX IS STRUCTURAL, NOT A THRESHOLD: a split piece's dictionary key AND its
        /// <c>Anchor</c> ARE its MeshRenderer (both split sites construct it that way), so the
        /// piece's geometry is reachable without any list. The sweep now asks the anchor whenever
        /// the lists are empty. A ZERO from this method now means the picture is clean; before it
        /// only meant the ledger was.</para>
        ///
        /// <para><b>MODBUILD 262 — TWO LISTS WAS STILL NOT THE SEGMENT.</b> The 261 sweep read
        /// <c>seg.Renderers</c>, <c>seg.Foliage</c> and (only when both were empty) the anchor,
        /// and it STOPPED AT THE FIRST DRAWING ENTRY — one verdict per segment, describing one
        /// renderer. A segment owns five more ledgers: <c>Mounted</c>, <c>Stacked</c>,
        /// <c>Body</c>, <c>UnitDressing</c> and <c>Siblings</c>, and the ModBuild 260 leftover
        /// population lives in the MOUNTED one. So the sweep answered honestly about at most one
        /// renderer of eight possible sources — the ModBuild 252 scar for the third time. It now
        /// enumerates every list the segment owns plus the anchor, deduplicated, and issues a
        /// class verdict for EVERY renderer that is drawing. <c>_runLeftover</c> therefore counts
        /// RENDERERS from this build on; <c>_runLeftoverSegments</c> keeps the old per-piece
        /// number so the two logs stay comparable.</para>
        /// </summary>
        private void SweepRunLeftovers()
        {
            _runLeftover = 0;
            _runLeftoverSegments = 0;
            _runLeftoverNames.Clear();
            _runLeftoverByReason.Clear();
            _runLeftoverByClass.Clear();
            _runLeftoverAllowed.Clear();
            if (_runs.Count == 0)
                return;
            // PASS 1: how far has each run actually got? The audit may only fire for a run some
            // member of which has COMPLETED its ramp — otherwise a sweep landing mid-dissolve
            // would report every piece of a perfectly healthy fade as a leftover, and the
            // headline claim of this round would be noise. Same >=0.99 predicate the mounted
            // leftover audit already uses for "this wall is gone".
            foreach (WallRun r in _runs.Values)
                r.MaxFade = 0f;
            foreach (Segment s in _live.Segments.Values)
            {
                if (s.FromSplitRun && s.RunOwner != null
                    && _runs.TryGetValue(s.RunOwner, out WallRun? owner) && s.Fade > owner.MaxFade)
                    owner.MaxFade = s.Fade;
            }
            foreach (Segment seg in _live.Segments.Values)
            {
                if (!seg.FromSplitRun || seg.RunOwner == null)
                    continue;
                if (!_runs.TryGetValue(seg.RunOwner, out WallRun? run) || !run.State)
                    continue;
                if (run.MaxFade < FoliageHideFade)
                    continue; // the whole run is still mid-ramp — nothing has been left behind yet
                if (seg.Fade >= FoliageHideFade)
                    continue; // this piece went with its run — nothing to report
                // EVERY LIST THE SEGMENT OWNS (ModBuild 262 — see the method doc), plus the
                // anchor, which is the whole piece for a choke-point refusal that owns nothing.
                // Deduplicated because a renderer can legitimately sit in two ledgers across a
                // rescan boundary, and a double-count here would inflate the very number the
                // user's verdict is read off.
                CollectSegmentLeftoverCandidates(seg);
                bool anyDrawing = false;
                for (int ci = 0; ci < _leftoverCandidates.Count; ci++)
                {
                    Renderer shown = _leftoverCandidates[ci];
                    if (!IsActuallyDrawing(shown))
                        continue;
                    anyDrawing = true;
                    _runLeftover++;
                    // ORDER IS THE DIAGNOSIS. The refusal is asked FIRST because boundless is its
                    // CONSEQUENCE — a refused renderer is never collected, so there is nothing to
                    // build an AABB from, and no room association either. ModBuild 260 asked
                    // HasBounds first and therefore reported the symptom as the cause for every
                    // one of these pieces. Falsified by: a piece printing the refusal reason that
                    // the STANDING PROP census does not also name.
                    //
                    // TWO STRINGS, ON PURPOSE. `tag` is the tally KEY and must be short and free
                    // of any per-piece number — a fade value in a key turns one class of 99 into
                    // 99 classes of one, and the distribution is the whole point of the line.
                    // `why` is the long form for the (capped) names list.
                    string tag, why;
                    if (seg.GeometryRefusedWhy != null)
                    {
                        tag = ReferenceEquals(seg.GeometryRefusedWhy, SplitPieceFigureRefusalReason)
                            ? "REFUSED as wall geometry (FIGURE arm, never relaxed)"
                            : "REFUSED as wall geometry (floor-standing prop)";
                        why = seg.GeometryRefusedWhy;
                    }
                    else if (!seg.HasBounds)
                    {
                        // ModBuild 262: 261 asserted here that such a piece "owns renderers or
                        // foliage", which was a guess the code could not make — the piece may be
                        // drawing out of any of the six ledgers this sweep now reads, or be the
                        // bare anchor. State what is known and nothing else.
                        tag = "no decision AABB, and NOT a choke-point refusal";
                        why = "no decision AABB — boundless fail-safe (round 14) with NO recorded "
                              + "choke-point refusal. Since ModBuild 262 the third split-run "
                              + "creation site (AdoptShaderMatchedWalls) records its refusals too, "
                              + "so a piece still landing here was refused by nothing and lost its "
                              + "bounds some other way";
                    }
                    else if (seg.Engulfing)
                    {
                        tag = "ENGULFING single mesh — undecidable as one unit";
                        why = "single mesh that ENGULFS its own room — held solid, undecidable as "
                              + "one unit (NeutralizeEngulfingSegments)";
                    }
                    else if (seg.DoorRoot != null)
                    {
                        tag = "doorway — never fades (user ruling 2026-08-02)";
                        why = tag;
                    }
                    else if (!RoomDecisionValid(seg.RoomIndex))
                    {
                        tag = "room has no valid floor grid — FAIL-SAFE solid";
                        why = $"room {seg.RoomIndex} has no valid floor grid — FAIL-SAFE solid";
                    }
                    else if (!seg.RunDriven)
                    {
                        tag = "NOT run-driven though it carries the run key";
                        why = "NOT run-driven though it carries the run key — the distribution "
                              + "missed it, and THAT would be a distribution bug";
                    }
                    else
                    {
                        tag = "run-driven, below full fade — mid-ramp or no dissolve channel";
                        why = tag + " (see the DISSOLVE CENSUS for this piece)";
                    }
                    _runLeftoverByReason.TryGetValue(tag, out int seen);
                    _runLeftoverByReason[tag] = seen + 1;
                    string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    // THE USER'S CLASSES. A leftover is not automatically a defect since the
                    // 2026-08-24 refinement — measured against the room the RUN decided on,
                    // because that is the floor he is looking at when the wall goes.
                    int room = run.Room >= 0 ? run.Room : seg.RoomIndex;
                    string cls = ClassifyLeftover(shown, room, out int blockedSamples,
                                                  out float foot, out float top,
                                                  out int visibleSamples);
                    _runLeftoverByClass.TryGetValue(cls, out int clsSeen);
                    _runLeftoverByClass[cls] = clsSeen + 1;
                    // THE RENDERER IS NAMED, NOT JUST ITS SEGMENT (ModBuild 262). 261 printed the
                    // segment anchor's name for a verdict measured on some renderer inside it,
                    // which for a mounted prop is a different object entirely.
                    string geom = $"'{shown.name}' foot {foot:F2} wu / top {top:F2} wu over room "
                                  + $"{room}'s floor, hides {blockedSamples} of "
                                  + $"{visibleSamples} in-view playable-tile sample(s)"
                                  + LeftoverExemptionNote(shown);
                    // ALLOWED pieces get their own list so a large ALLOWED population can never be
                    // read as a large defect — which is precisely the mistake the previous two
                    // rounds made with the 99 and the 111.
                    if (cls == "ALLOWED")
                    {
                        if (_runLeftoverAllowed.Count < MountedLeftoverCap)
                            _runLeftoverAllowed.Add($"on '{wall}': {geom}");
                        continue;
                    }
                    if (_runLeftoverNames.Count >= MountedLeftoverCap)
                        continue;
                    string owner = run.Anchor != null ? run.Anchor.name : "<dead>";
                    _runLeftoverNames.Add(
                        $"[{cls}] {geom} — carried by piece '{wall}' at fade {seg.Fade:F2} while "
                        + $"its run '{owner}' is at {run.MaxFade:F2} — {why}");
                }
                if (anyDrawing)
                    _runLeftoverSegments++;
            }
        }

        /// <summary>
        /// EVERY RENDERER ONE SEGMENT OWNS — the eight sources a leftover can be drawing out of,
        /// deduplicated and in a stable order. ModBuild 261's sweep read two of them and stopped
        /// at the first hit; the steady ModBuild 260 leftover population lives in
        /// <see cref="Segment.Mounted"/>, which it never touched.
        ///
        /// <para>COST: this walks lists the segment already holds — no scene query, no
        /// <c>GetComponent</c>, no allocation past the two scratch containers, which are fields.
        /// It runs once per split-run piece per rescan (the ModBuild 260 scenario: 140 pieces of
        /// the largest run, at the 2 s rescan cadence), so it is not in the frame.</para>
        /// </summary>
        private void CollectSegmentLeftoverCandidates(Segment seg)
        {
            _leftoverCandidates.Clear();
            _leftoverSeen.Clear();
            AddLeftoverCandidate(seg.Anchor as Renderer);
            for (int i = 0; i < seg.Renderers.Count; i++)
                AddLeftoverCandidate(seg.Renderers[i]);
            for (int i = 0; i < seg.Foliage.Count; i++)
                AddLeftoverCandidate(seg.Foliage[i]);
            for (int i = 0; i < seg.Siblings.Count; i++)
                AddLeftoverCandidate(seg.Siblings[i]);
            for (int i = 0; i < seg.Mounted.Count; i++)
                AddLeftoverCandidate(seg.Mounted[i].Renderer);
            for (int i = 0; i < seg.Stacked.Count; i++)
                AddLeftoverCandidate(seg.Stacked[i].Renderer);
            for (int i = 0; i < seg.Body.Count; i++)
                AddLeftoverCandidate(seg.Body[i].Renderer);
            for (int i = 0; i < seg.UnitDressing.Count; i++)
                AddLeftoverCandidate(seg.UnitDressing[i].Renderer);
        }

        private void AddLeftoverCandidate(Renderer? r)
        {
            if (r == null || !_leftoverSeen.Add(r))
                return;
            _leftoverCandidates.Add(r);
        }

        private readonly List<Renderer> _leftoverCandidates = new();
        private readonly HashSet<Renderer> _leftoverSeen = new();

        /// <summary>
        /// A standing NOTE, never a class. Two user rulings already say a specific piece may stay
        /// — the fountain (2026-08-09, brunnen.png) and the doorway arch (2026-08-02) — and both
        /// are enforced as spatial rects that pull the piece back off its wall. A geometric
        /// FLOATING verdict on such a piece is CORRECT about the geometry and moot as a defect,
        /// and without this note the next round would chase it. Deliberately not folded into the
        /// class: the classes are the user's three words and nothing else may be smuggled into
        /// them.
        ///
        /// <para>MODBUILD 264: the two rects here are also the two exclusion terms of
        /// <see cref="IsWallGeneratedMember"/>, so an EXEMPT piece can never be classified WALL
        /// MEMBER. That is not a special case bolted onto the class — an exemption rect IS a
        /// statement that some other owner holds the piece, which is the same question the
        /// membership test asks. If a third exemption rect is ever added, add it in BOTH places or
        /// the new one will be reported as the defect it is exempt from.</para>
        /// </summary>
        private string LeftoverExemptionNote(Renderer r)
        {
            Bounds b = r.bounds;
            if (IsWaterProtected(b))
                return " [EXEMPT: water feature, user ruling 2026-08-09 — allowed to stay]";
            if (IsArchProtected(b, r.name))
                return " [EXEMPT: doorway arch, user ruling 2026-08-02 — allowed to stay]";
            return string.Empty;
        }

        private int _runLeftover;
        /// <summary>ModBuild 262: how many split-run PIECES had at least one drawing renderer —
        /// the number ModBuild 261's <c>_runLeftover</c> actually held, kept so the two logs can
        /// be compared. <see cref="_runLeftover"/> now counts RENDERERS.</summary>
        private int _runLeftoverSegments;
        private readonly List<string> _runLeftoverNames = new();
        /// <summary>ModBuild 261: leftover count per REASON. The shared LEFTOVER line names at
        /// most <see cref="PerWallNameCap"/> pieces, which for a 99-piece population is a sample
        /// and not a distribution — the in-repo lesson is read-the-whole-distribution. This map
        /// is printed complete on the SPLIT-RUN LEFTOVER line below.</summary>
        private readonly Dictionary<string, int> _runLeftoverByReason = new();
        /// <summary>ModBuild 261: the same leftovers again, by the user's OWN three classes (see
        /// <see cref="ClassifyLeftover"/>). Separate from the reason tally because a reason says
        /// which rule holds a piece and a class says whether he minds.</summary>
        private readonly Dictionary<string, int> _runLeftoverByClass = new();
        private readonly List<string> _runLeftoverAllowed = new();

        /// <summary>
        /// THE USER'S THREE CLASSES (ruling 2026-08-24, refining the same day's "alles muss
        /// faden"): "Es gibt Dinge die stehen bleiben dürfen. zB der Brunnen … oder auch dieses
        /// Steingebilde … weil es auch niedrig ist und nicht die Sicht verdeckt. Aber es dürfen
        /// keine Elemente 'herumfliegen' weil die Wand die es gehalten hat nicht mehr da ist."
        ///
        /// <list type="bullet">
        /// <item>FLOATING — its foot does not reach the floor band. It was carried by the wall and
        ///   the wall is gone. He calls this out most sharply; it must never happen.</item>
        /// <item>WALL MEMBER — the wall generator BUILT it (ModBuild 264). A defect whatever its
        ///   height and whatever it hides; see <see cref="IsWallGeneratedMember"/>.</item>
        /// <item>OBSTRUCTING — it stands on the floor and still hides a playable tile.</item>
        /// <item>ALLOWED — it belongs to NO wall, stands on the floor and hides nothing. A well, a
        ///   low stone formation, a crystal on the ground. Not part of the wall at all, in his
        ///   words. Since ModBuild 264 that first clause is a test and not a hope.</item>
        /// </list>
        ///
        /// <para>NO NEW CONSTANT IS INTRODUCED, deliberately — four thresholds in this subsystem
        /// have been falsified by the next hardware log. FLOATING reuses
        /// <see cref="WallStandingProp.FootBandWU"/>, the incumbent foot band every standing-prop
        /// verdict is already measured with. OBSTRUCTING is not a height at all: it asks the
        /// subsystem's own question — does this piece intercept the head→sample ray of any
        /// FRUSTUM-VISIBLE playable-tile sample of the room? A piece that blocks zero samples does
        /// not obstruct, by the definition the fade trigger itself runs on, and "low enough" then
        /// needs no number. The blocked-sample COUNT is reported rather than a bare bool, so the
        /// next log shows how many pieces sit at the 0/1 boundary — i.e. whether a rule built on
        /// this test would need hysteresis or is comfortably separated.</para>
        ///
        /// <para><b>MODBUILD 262 — IT DEGRADES OUT LOUD, BECAUSE THIS IS ONE SCENARIO OF MANY.</b>
        /// (User, 2026-08-24: <i>"Das Level was ich jetzt gerade die ganze Zeit teste ist nur
        /// eines von vielen — natürlich erwarte ich dass der Code generisch auf alle Szenarios und
        /// Räume im gesamten Spiel funktioniert."</i>) The two CLASS DEFINITIONS are generic —
        /// they are pure geometry against the room's own floor and the room's own playable-tile
        /// samples, and neither mentions a tileset, a shader family or a room size. The INPUTS are
        /// not guaranteed, and every one of them fails toward a confident wrong ALLOWED:
        /// <list type="number">
        /// <item>a room with no ANCHORED floor plane — <c>_live.RoomFloorY</c> holds a number for every
        ///   room, anchored or not, so reading it blind produces a foot height measured against a
        ///   guess. Asked with <see cref="RoomDecisionValid"/>, the same predicate the fade
        ///   decision itself runs on.</item>
        /// <item>a room with NO sample grid at all — a scenario with more revealed rooms than the
        ///   <c>MaxTotalSamples</c> budget covers hands late rooms <c>_live.RoomSampleCount = 0</c> and
        ///   holds their walls solid. Zero samples means zero blocked, which would have read as
        ///   ALLOWED for every piece in the room.</item>
        /// <item>no sample of the room FRUSTUM-VISIBLE this tick — "it hides nothing from him"
        ///   is then a statement about where his head happens to point, not about the piece.
        ///   ALLOWED is a permanent-sounding verdict and must not be issued from a momentary
        ///   one.</item>
        /// </list>
        /// Each returns its own UNJUDGED string naming the missing input. A wrong ALLOWED is worse
        /// than an explicit refusal to judge: ALLOWED is the class that says "leave it alone".
        /// </para>
        /// </summary>
        private string ClassifyLeftover(Renderer r, int room, out int blockedSamples,
            out float foot, out float top)
            => ClassifyLeftover(r, room, out blockedSamples, out foot, out top, out _);

        private string ClassifyLeftover(Renderer r, int room, out int blockedSamples,
            out float foot, out float top, out int visibleSamples)
        {
            Bounds b = r.bounds;
            blockedSamples = 0;
            visibleSamples = 0;
            foot = 0f;
            top = 0f;
            if (room < 0 || room >= _live.RoomFloorY.Count || !RoomDecisionValid(room))
                return "UNJUDGED (no anchored floor plane for this room)";
            float floorY = _live.RoomFloorY[room];
            foot = b.min.y - floorY;
            top = b.max.y - floorY;
            if (foot > WallStandingProp.FootBandWU)
                return "FLOATING";
            // MODBUILD 264 — MEMBERSHIP BEFORE GEOMETRY, and deliberately ahead of BOTH remaining
            // UNJUDGED arms as well as the sample test: provenance needs no sample grid and no
            // frustum-visible sample to be true, so a wall member is judged even in the two states
            // ModBuild 262 rightly refuses to judge OBSTRUCTING/ALLOWED in. It cannot be asked
            // before the anchored-floor arm above, because both of its exclusion terms are
            // measured against that plane. See IsWallGeneratedMember for the number that forced
            // this ordering.
            if (IsWallGeneratedMember(r, floorY))
                return "WALL MEMBER";
            visibleSamples = PieceBlockedSamples(r, room, out blockedSamples);
            if (visibleSamples < 0)
                return "UNJUDGED (this room has no playable-tile sample grid)";
            if (visibleSamples == 0)
                return "UNJUDGED (no playable-tile sample of this room is in view this tick)";
            return blockedSamples > 0 ? "OBSTRUCTING" : "ALLOWED";
        }

        /// <summary>
        /// MODBUILD 264 — DOES A WALL GENERATOR OWN THIS RENDERER? The one test that decides
        /// whether a leftover is ALLOWED or a defect, and it is a MEMBERSHIP question rather than
        /// a geometric one.
        ///
        /// <para><b>THE NUMBER THAT FORCED IT.</b> The ModBuild-261 log classifies its leftovers
        /// <c>BY THE USER'S THREE CLASSES: 99 × ALLOWED</c> on 21 of 22 passes and
        /// <c>98 × ALLOWED; 1 × OBSTRUCTING</c> on the twenty-second — a healthy scene by the
        /// geometric rule — while mauerproblem_erneut2.jpg shows a wall run at fade 1.00 behind a
        /// solid band of ferns, ivy curtains and vine-covered stumps. The user's reply:
        /// <i>"Das Gestrüp an der hinteren Wand ist immer noch nicht weg — das soll vollständig
        /// alles mit-weg-faden"</i>, and <i>"Leite von den Regeln dieses Raumes weitere ab für
        /// alle Szenarios … sondern überall funktioniert"</i>. So "is it low and does it block a
        /// sample" is not his question. His is: does this belong to the wall? Scrub, ivy, ferns
        /// and trunks the wall generator produced belong to it and go with it however low they
        /// are; a crystal formation standing on the floor does not.</para>
        ///
        /// <para>ModBuild 262 and 263 widened WHAT this classifier sees (all eight lists a piece
        /// owns, a verdict per drawing renderer, and the UNJUDGED arms that stop a missing input
        /// reading as ALLOWED) and every one of those numbers stands. They do not touch the
        /// question this test asks, which is the one the geometric classes cannot express.</para>
        ///
        /// <para><b>WHY IT IS GENERIC AND NOT A NAME LIST.</b> The discriminator is the tileset's
        /// OWN CONSTRUCTION, not an asset family: every biome's wall generator parents its
        /// dressing under <c>Walls/Wall N/Generated Content/…</c>. That is where the same log
        /// finds the offenders — <c>'CR_FR_Wall_Grassy_Verge_Ivy_01'</c>,
        /// <c>'CR_FR_Wall_LS_PlantsBushes_03'</c>, <c>'CR_FR_Wall_Log_Structure_Stump_03'</c> and
        /// 180 more ancestry rows under <c>'Walls/Wall 1/Generated Content/…'</c> — and it is NOT
        /// where it finds the one piece he says may stay: <c>'CV_Ice_Crystal_Form_02'</c> appears
        /// in no ancestry row, in no <c>FADE WRITE</c> row and in no prop unit of any wall. The
        /// test names no tileset, no shader family, no size and no coordinate.</para>
        ///
        /// <para><b>THE TRAP, AND THE TWO TERMS THAT DISARM IT — a hierarchy path is not
        /// membership.</b> Floor hexes are ALSO parented under <c>Wall N/Generated Content/</c>;
        /// <see cref="WallStandingProp"/> records the exact path
        /// (<c>Wall N/Generated Content/PCG_CR_Floor_BaseHex_Plain/EN_CR_Floor_BaseHex_Plain</c>)
        /// and the ModBuild-261 STANDING PROP line catches them being claimed —
        /// <c>848 claim(s) refused this rescan: 'FR_Floor_Grass_Half_02' → 'Wall 1',
        /// 'FR_Floor_Scatter_Grass_Medium_05' → 'Wall 1', 'FR_Floor_Grass' → 'Wall 1', …</c>.
        /// So the test carries the SAME two terms the fade path itself already uses to decide a
        /// wall does not own a piece, and introduces no third:
        /// <list type="number">
        /// <item>the ground band <see cref="FadeDriver.GroundExclusionHeightWU"/> — the rule
        ///   <see cref="FadeDriver.StripGroundRenderers"/> removes ground-lying renderers from a
        ///   segment by, verbatim. Every floor piece the 261 log names is comfortably inside it:
        ///   tops −0.24, −0.20, −0.15, −0.19 wu over the floor, and
        ///   <c>PCG_FR_Floor_Grass_Hex_Split_PR</c> at foot −0.3 / height 0.5, against 1.0 wu;</item>
        /// <item>the water rect, <see cref="FadeDriver.IsWaterProtected"/> — the pond, its basin,
        ///   bank, rim and emitters are the water feature's own unit (user ruling 2026-08-09);
        ///   </item>
        /// <item>the doorway-arch rect, <see cref="FadeDriver.IsArchProtected"/> — a doorway never
        ///   fades (user ruling 2026-08-02) and that rect already pulls its masonry back off the
        ///   wall, so the wall does not own it either. Without this term a correctly-behaving
        ///   doorway would be reported as the defect this class exists to find, and the user says
        ///   the masonry at the door behaves exactly as he wants.</item>
        /// </list>
        /// NO NEW CONSTANT, which is the standing discipline in this file: four thresholds here
        /// have been shipped from a single scenario's numbers and each was falsified by the next
        /// hardware log.</para>
        ///
        /// <para>MULTIPLAYER: this is a diagnostic classification of local scene hierarchy. No
        /// decision, no wire record, no peer-visible state.</para>
        ///
        /// <para>COST: one hierarchy climb per LEFTOVER — never per frame and never a scene sweep.
        /// It is the same call the sibling adoption sweep already makes per candidate
        /// (<c>r.GetComponentInParent&lt;ProceduralWall&gt;()</c>), and it runs inside the audit
        /// ModBuild 262 already sliced against the 1.5 ms budget.</para>
        ///
        /// <para>FALSIFIED BY: a WALL MEMBER entry naming a floor hex or a pond rim — then the
        /// band or the rect is the wrong term, not the membership rule; or the user reporting
        /// scrub while this class reads zero — then the wall generator did not build that scrub
        /// and provenance is not what identifies it.</para>
        /// </summary>
        private bool IsWallGeneratedMember(Renderer r, float floorY)
        {
            Bounds b = r.bounds;
            if (b.max.y <= floorY + GroundExclusionHeightWU)
                return false;               // ground band — the wall never owned it
            if (IsWaterProtected(b))
                return false;               // the water feature's own unit owns it
            if (IsArchProtected(b, r.name))
                return false;               // the doorway owns it, and doorways never fade
            return r.GetComponentInParent<ProceduralWall>() != null;
        }

        /// <summary>
        /// How many of a room's FRUSTUM-VISIBLE playable-tile samples this one renderer hides from
        /// the head — the broad phase of <c>RoomBlockedFraction</c> applied to the renderer's own
        /// world AABB instead of a segment's union box, with the same "clearly before the point"
        /// rule and the SAME constants (<c>BlockEpsDistFraction</c>, <c>BlockEpsMin/MaxWorld</c>).
        /// Nothing new is tuned here. Sixteen samples per room in the ModBuild 260 scenario — but
        /// the count is <c>min(grid², playable hexes)</c> and <c>grid</c> itself falls to 3, 2 and
        /// finally 1 as the revealed room count rises, so it is 16 in that scenario and NOT a
        /// property of this code. Run once per leftover per rescan, so the cost is not in the
        /// frame at any of those sizes.
        ///
        /// <para>RETURNS the number of samples that were VISIBLE and therefore askable, or -1 when
        /// the room has no grid at all; <paramref name="blocked"/> is how many of those this
        /// renderer intercepts. Two numbers because 0-of-0 and 0-of-16 are opposite findings and
        /// the ModBuild 261 signature could not tell them apart.</para>
        /// </summary>
        private int PieceBlockedSamples(Renderer r, int room, out int blocked)
        {
            blocked = 0;
            if (room < 0 || room >= _live.RoomSampleCount.Count)
                return -1;
            int total = _live.RoomSampleCount[room];
            if (total <= 0)
                return -1;
            Bounds b = r.bounds;
            float thicknessEps = Mathf.Clamp(0.5f * Mathf.Min(b.size.x, b.size.z),
                                             BlockEpsMinWorld, BlockEpsMaxWorld);
            int start = _live.RoomSampleStart[room];
            int end = Mathf.Min(start + total,
                                Mathf.Min(_live.AllSamples.Count, _sampleVisible.Length));
            Vector3 headPos = _lastHeadPos;
            int visible = 0;
            for (int i = start; i < end; i++)
            {
                if (!_sampleVisible[i])
                    continue; // out of view direction — it cannot be hiding this from him
                Vector3 sample = _live.AllSamples[i];
                Vector3 to = sample - headPos;
                float dist = to.magnitude;
                if (dist < 0.001f)
                    continue;
                visible++;
                float eps = Mathf.Max(thicknessEps, BlockEpsDistFraction * dist);
                var ray = new Ray(headPos, to / dist);
                if (b.IntersectRay(ray, out float d) && (d < dist - eps || b.Contains(sample)))
                    blocked++;
            }
            return visible;
        }

        /// <summary>
        /// ModBuild 261 — THE COMPLETE DISTRIBUTION of everything still drawing beside a faded
        /// run, one entry per reason with its count, nothing truncated. It is a separate line
        /// from the shared <c>LEFTOVER OVER A FADED WALL</c> warn on purpose: that line caps its
        /// names at <see cref="PerWallNameCap"/> = 8, and 8 of 99 is a mode, not a distribution
        /// (the in-repo lesson that cost a build: "sort -u | head shows the mode").
        ///
        /// <para>WHAT TO READ OFF IT. If the refusal reason dominates, the boundless fail-safe is
        /// innocent and the lever is the choke point's FLOOR arm — those pieces own no renderer,
        /// so no change to the fail-safe can move them. If any OTHER reason carries a large
        /// count, that reason is the lever instead and this line says so by name. Silent when
        /// nothing is left over, so a clean session prints nothing.</para>
        /// </summary>
        private void LogSplitRunLeftoverBreakdown()
        {
            if (_runLeftoverByReason.Count == 0)
                return;
            _runReasonSb.Length = 0;
            foreach (KeyValuePair<string, int> kv in _runLeftoverByReason)
            {
                if (_runReasonSb.Length > 0)
                    _runReasonSb.Append("; ");
                _runReasonSb.Append(kv.Value).Append(" × ").Append(kv.Key);
            }
            _runClassSb.Length = 0;
            foreach (KeyValuePair<string, int> kv in _runLeftoverByClass)
            {
                if (_runClassSb.Length > 0)
                    _runClassSb.Append("; ");
                _runClassSb.Append(kv.Value).Append(" × ").Append(kv.Key);
            }
            VRLog.Warn(Name,
                $"SPLIT-RUN LEFTOVER: {_runLeftover} RENDERER(S) across {_runLeftoverSegments} "
                + "piece(s) of a FADED run are actually drawing — read off every renderer the "
                + "piece owns (anchor + Renderers + Foliage + Siblings + Mounted + Stacked + "
                + "Body + UnitDressing, deduplicated), never off one list, which is the ModBuild "
                + "261 blind spot this line closes: that build read two of the eight sources and "
                + "STOPPED AT THE FIRST HIT, so one verdict stood for a whole piece and the "
                + "MOUNTED ledger — where the steady 260 leftover population lives — was never "
                + "asked. The renderer count is the new number; the piece count is what ModBuild "
                + "261 printed here. "
                // THE CLASSIFICATION IS THE HEADLINE, NOT THE COUNT (user ruling 2026-08-24, and
                // it retires "0 leftovers" as a target): FLOATING and OBSTRUCTING are the defect,
                // ALLOWED is not. A big ALLOWED number here is a HEALTHY reading.
                + $"BY THE USER'S THREE CLASSES: {_runClassSb}. FLOATING = its foot is above the "
                + $"{WallStandingProp.FootBandWU:0.0} wu floor band, i.e. the wall was holding it "
                + "up and is gone — the one he says must never happen. OBSTRUCTING = on the floor "
                + "and still hiding at least one FRUSTUM-VISIBLE playable-tile sample, measured "
                + "with the fade trigger's own ray test, so 'low enough' needs no height constant. "
                + "WALL MEMBER = the wall generator built it (inside a ProceduralWall subtree, "
                + "above the ground band, outside every water rect) — a defect at fade "
                + $"≥{FoliageHideFade:0.00} whatever its height and whatever it hides, which is "
                + "the class ModBuild 264 added because 99 x ALLOWED read as a healthy scene "
                + "while the photograph showed a solid hedge. "
                + "ALLOWED = belongs to NO wall, on the floor, hides nothing — the well, the low "
                + "stone formation and the crystal on the ground, "
                + $"which he does not regard as part of the wall: {_runLeftoverAllowed.Count} "
                + "named ["
                + string.Join("; ", _runLeftoverAllowed)
                + "]. COMPLETE per-reason distribution over ALL three classes (which RULE holds "
                + "each piece, as opposed to whether he minds), nothing truncated "
                + $"({_runLeftoverByReason.Count} distinct reason(s)): {_runReasonSb}. Named "
                + $"defects (up to {MountedLeftoverCap}): "
                + string.Join("; ", _runLeftoverNames)
                + ". A renderer tagged [EXEMPT] carries a standing user ruling of its own (the "
                + "fountain 2026-08-09, the doorway arch 2026-08-02) and is allowed to stay "
                + "whatever its geometry says.");
        }

        private readonly System.Text.StringBuilder _runReasonSb = new();
        private readonly System.Text.StringBuilder _runClassSb = new();

        /// <summary>
        /// The SPLIT RUN census, printed on the PER-WALL cadence. Its whole job is to make the
        /// difference between "one wall decided" and "forty pieces decided" a number rather than
        /// an intention — and to expose the orphan population, which is the one class this
        /// mechanism cannot own.
        /// </summary>
        /// <summary>
        /// SPLIT PRE-FILTER — the ModBuild-386 remedy's own falsifier, at the tier the shipped
        /// default prints.
        ///
        /// <para>WHAT IT ANSWERS, and it is one question. Until ModBuild 386
        /// <c>RefreshSplitWall</c>'s gate was the shader-NAME test while the choke point behind it
        /// admits on NAME <b>or</b> a live toggle, so a toggle-native renderer under an
        /// engulf-split wall was dropped before a <c>Segment</c> existed and was then invisible to
        /// every instrument in this subsystem — no owner row, no reject row, no audit row. That is
        /// what <c>'CR_OS_Pillar_Large_02' … NO WALL SEGMENT OWNS IT [UNOWNED]</c> was in the
        /// ModBuild-385 log (user 2026-09-03, säulen.jpg). This line prints how many renderers the
        /// widened gate KEPT that the old one would have dropped.</para>
        ///
        /// <para>IT PRINTS ZERO ON PURPOSE. A line that only appears when the number is non-zero
        /// cannot say "the widening did not run", and "no improvement" from a remedy that never
        /// executed carries zero information — the failure this project has already paid for. A
        /// zero here means the pillar's cause is NOT this gate and the next round must read the
        /// SOLID BLOCKER line's [UNOWNED] arm again, not retune anything.</para>
        ///
        /// <para>IT ASSERTS NOTHING IT CANNOT SEE. It counts admissions at one <c>if</c>. It does
        /// not claim any of them faded — that is the FADE WRITE and SOLID BLOCKER lines' business,
        /// and this line must never be edited into saying so.</para>
        ///
        /// <para>SPENT WHEN ANSWERED. One hardware round with a non-zero count and the pillars
        /// gone retires this line; a probe that has answered and keeps printing is its own defect.
        /// </para>
        ///
        /// <para>MULTIPLAYER: a diagnostic. It writes no renderer, no material, no segment and no
        /// wire record.</para>
        /// </summary>
        private void EmitSplitPreFilterLine()
        {
            string named = _splitToggleRescuedNames.Count == 0
                ? "none named"
                : string.Join(", ", _splitToggleRescuedNames);
            // HW-VERIFY
            VRLog.Note(Name,
                $"SPLIT PRE-FILTER: {_splitToggleRescued} renderer(s) were kept at the split "
                + "wall's gate this rescan that ModBuild 385 would have DROPPED — toggle-native "
                + "meshes (a live wall-fade toggle, no 'WallFade' in the shader name) under a wall "
                + "the engulf rule had split. Before 386 that gate asked the shader NAME only "
                + "while the choke point behind it admits NAME or TOGGLE, so such a renderer left "
                + "before a Segment existed and could then appear in NO diagnostic at all: no "
                + "owner row, no reject row, no audit row. ZERO HERE IS A READING, not a "
                + "non-event: it means the widening found nothing and the pillar report's "
                + "[UNOWNED] arm has a different cause. This counts ADMISSIONS at one gate and "
                + "says nothing about whether any of them then faded — read the FADE WRITE and "
                + $"SOLID BLOCKER lines for that. Named {_splitToggleRescuedNames.Count} of "
                + $"{_splitToggleRescued} (cap {SplitToggleRescuedNameCap}, freshest first, and a "
                + $"truncated list is not absence): {named}. FALSIFIER, and it is a NAME test on "
                + "the list above and nowhere else: the three assets of the 2026-08-24 ruling — the "
                + "two ice-crystal formations and the numbered light shaft — appearing there means "
                + "the widening reached geometry the user ruled must STAY, and that is a withdrawal "
                + "of ModBuild 386 rather than a retune. THEIR NAMES ARE DELIBERATELY NOT SPELLED "
                + "HERE (ModBuild 390): 386 printed them in this sentence, so a grep for any of the "
                + "three returned 48 hits in the ModBuild-388 log and every one was this line "
                + "quoting itself — the same self-quoting defect 386 had just fixed on another "
                + "marker, reintroduced by its own falsifier. Grep the named list, not the prose.");
        }

        private void LogSplitRuns(float now)
        {
            if (_runsTotal == 0 && _runOrphans == 0)
                return;
            if (now < _nextRunLogTime)
                return;
            _nextRunLogTime = now + InsideLogIntervalSeconds;
            LogSplitRunLeftoverBreakdown();
            EmitSplitPreFilterLine();
            VRLog.Info(Name,
                $"SPLIT RUN: {_runsFaded} of {_runsTotal} split wall run(s) faded, driving "
                + $"{_runMembersTotal} piece(s) as whole walls ({_runMembersRefused} of those "
                + "own NO renderer because the wall choke point REFUSED them as floor-standing "
                + "props — they were never wall geometry and no fail-safe is holding them; a "
                + "further "
                + $"{_runMembersHeld - _runMembersRefused} are held solid by an OLDER fail-safe "
                + "WITH geometry — boundless for some other reason, room-engulfing single mesh, "
                + "doorway, or a room with no valid floor grid — and are the only members that "
                + "can legitimately stay while their run goes; the SPLIT-RUN LEFTOVER line below "
                + "gives the complete per-reason distribution, the LEFTOVER line a sample). "
                + $"{_runOrphans} orphan(s): pieces whose run anchor is destroyed, which keep "
                + "their OWN decision (the pre-259 behaviour, fail-open — never a latch). A run "
                + "is a ProceduralWall/tile-observer group carved up by "
                + "NeutralizeEngulfingSegments; every member is a child of that one object, so "
                + "no unsplit wall can be reached from here. Unsplit walls ('Wall 2', 'Wall 3', "
                + "'Wall 4' in the ModBuild 258 log) are absent from this line BY CONSTRUCTION "
                + "and their code path is byte-for-byte the ModBuild 258 one. Live dial "
                + $"[WallFade] SplitRunUnified = {WallFadeTuning.SplitRunUnifiedOn} — a false "
                + "here means this whole mechanism did not run and any verdict about it is void. "
                + "Live dial [WallFade] SplitRunAdoptGroundScenery = "
                + $"{WallFadeTuning.AdoptGroundScenery} ({_runMembersPassenger} passenger(s) "
                + "recruited this pass — the scrub/stones/low bushes the FLOOR arm used to refuse; "
                + "a FALSE here with a large refused count above means the recruitment did not "
                + "run and any verdict about the standing hedge is void). "
                // IMMEDIACY (user ruling 2026-08-24: "direkt wieder unfaded wenn es keine
                // spielbaren tiles verdeckt"). Three delays sit between "covers nothing" and
                // "solid again", and this clause states all three so the next log can rank them
                // instead of a lane guessing. The samples ARE playable tiles already: the grid is
                // CNode.Walkable and not CNode.Blocked (see the SAMPLE GRID line).
                + $"UN-FADE: union {_runUnionBlocked}/{_runUnionTotal} vs widest single member "
                + $"{_runUnionBest}/{_runUnionTotal}, sampled NOW and not at an edge (at an edge "
                + "the union is under the bar by construction, which is why the RUN FADE pair "
                + "could never measure this). The union alone has held a faded run down for "
                + $"{_runUnionHoldNow:F1}s right now, worst {_runUnionHoldWorst:F1}s this "
                + $"session — zero means the union costs the un-fade nothing. Exit bar "
                + $"{WallFadeTuning.Off:F2} of the room's playable hexes (a run stays faded while "
                + "it still covers that much, so 'covers nothing' is NOT the release condition), "
                + $"then dwell {WallFadeTuning.DwellMoved:0.0}s if the perspective moved / "
                + $"{WallFadeTuning.DwellStationary:0.0}s if the head only rotated, on top of the "
                + "EMA lag. All three are live [WallFade] dials.");
        }

        /// <summary>
        /// COMMIT PHASE 24 — see <c>RescanCore</c>. Rebuild the cached board volume. Deliberately
        /// LAST: it reads the room registry AND every segment's final decision AABB, so it must
        /// run after the gate-bounds guarantee that is itself deliberately last.
        /// </summary>
        private void CommitBoardVolume()
        {
            _live.BoardVolumeValid = false;
            _live.BoardVolumeRooms = 0;
            _boardVolumeWalls = 0;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            float floorY = float.MaxValue;
            for (int i = 0; i < _live.RoomBounds.Count; i++)
            {
                // Only rooms the coverage decision itself trusts (tile-anchored plane, non-empty
                // grid). A guessed frame must not define where the player is standing either.
                if (!RoomDecisionValid(i))
                    continue;
                Bounds b = _live.RoomBounds[i];
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                if (i < _live.RoomFloorY.Count && _live.RoomFloorY[i] < floorY) floorY = _live.RoomFloorY[i];
                _live.BoardVolumeRooms++;
            }
            if (_live.BoardVolumeRooms == 0 || floorY == float.MaxValue)
            {
                // No decision-valid room: the 3D map room and every pre-generation frame. The
                // volume stays invalid and the INSIDE rule can never fire.
                AnnounceBoardVolume();
                return;
            }

            // The walls belong to the footprint too — the room renderers are the FLOOR proxies,
            // so a player standing inside a perimeter wall would otherwise read as outside the
            // board, which is the one place the head-in-stone escape hatch has to work.
            _crestScratch.Clear();
            foreach (Segment seg in _live.Segments.Values)
            {
                if (!seg.HasBounds || !RoomDecisionValid(seg.RoomIndex))
                    continue;
                Bounds b = seg.Bounds;
                if (b.min.x < minX) minX = b.min.x;
                if (b.max.x > maxX) maxX = b.max.x;
                if (b.min.z < minZ) minZ = b.min.z;
                if (b.max.z > maxZ) maxZ = b.max.z;
                _boardVolumeWalls++;
                // Doorways and engulfing pieces are in the FOOTPRINT but not in the crest
                // sample: an arch top is not a wall height, and an engulfing AABB is the very
                // shape the coverage metric already refuses to trust.
                if (seg.DoorRoot == null && !seg.Engulfing)
                    _crestScratch.Add(b.max.y - floorY);
            }
            if (_crestScratch.Count == 0)
            {
                AnnounceBoardVolume();
                return;
            }

            // MEDIAN, not max: a keep's stacked superstructure is a legitimate segment AABB
            // three stories tall, and taking the tallest would put the crest plane above every
            // wall in the scenario — the volume would then swallow a player who is plainly
            // looking down at the board from outside.
            _crestScratch.Sort();
            float crest = Mathf.Max(_crestScratch[_crestScratch.Count / 2], BoardCrestMinWU);
            _live.BoardCrestWU = crest;
            _live.BoardFloorY = floorY;
            _live.BoardVolume = new Bounds(
                new Vector3((minX + maxX) * 0.5f, floorY + crest * 0.5f, (minZ + maxZ) * 0.5f),
                new Vector3(Mathf.Max(maxX - minX, 0f), crest, Mathf.Max(maxZ - minZ, 0f)));
            _live.BoardVolumeValid = true;
            AnnounceBoardVolume();
        }

        /// <summary>
        /// Print the board volume whenever it materially changes (new scenario, room reveal).
        /// This is the line that proves the rule was ARMED with real numbers even in a session
        /// where the player never went inside — an instrument that only speaks when it triggers
        /// cannot distinguish "did not happen" from "never ran".
        /// </summary>
        private void AnnounceBoardVolume()
        {
            int sigCrest = Mathf.RoundToInt(_live.BoardCrestWU * 10f);
            int sigX = _live.BoardVolumeValid ? Mathf.RoundToInt(_live.BoardVolume.size.x * 10f) : 0;
            int sigZ = _live.BoardVolumeValid ? Mathf.RoundToInt(_live.BoardVolume.size.z * 10f) : 0;
            if (_live.BoardVolumeRooms == _bvSigRooms && _boardVolumeWalls == _bvSigWalls
                && sigCrest == _bvSigCrest && sigX == _bvSigX && sigZ == _bvSigZ)
                return;
            _bvSigRooms = _live.BoardVolumeRooms;
            _bvSigWalls = _boardVolumeWalls;
            _bvSigCrest = sigCrest;
            _bvSigX = sigX;
            _bvSigZ = sigZ;
            if (!_live.BoardVolumeValid)
            {
                VRLog.Info(Name,
                    "BOARD VOLUME: none — "
                    + $"{_live.BoardVolumeRooms} decision-valid room(s), {_boardVolumeWalls} wall(s) "
                    + "with a decision AABB. The WALK-IN stand-down cannot fire until a room is "
                    + "tile-anchored with a non-empty floor grid; this is also the steady state "
                    + "of the 3D map room, which has no occlusion volumes at all, so the mode "
                    + "can never engage there.");
                return;
            }
            Vector3 mn = _live.BoardVolume.min, mx = _live.BoardVolume.max;
            VRLog.Info(Name,
                $"BOARD VOLUME: x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, "
                + $"z {mn.z:F1}..{mx.z:F1} world units — union footprint of "
                + $"{_live.BoardVolumeRooms} decision-valid room(s) and {_boardVolumeWalls} wall(s), "
                + $"floor plane {_live.BoardFloorY:F2}, MEDIAN wall crest {_live.BoardCrestWU:F2} wu over "
                + $"{_crestScratch.Count} wall(s). INSIDE bars: enter at signed distance "
                + $"<= {-WallFadeTuning.InsideEnterDepthFraction * _live.BoardCrestWU:F2} wu, leave at "
                + $">= {WallFadeTuning.InsideExitDepthFraction * _live.BoardCrestWU:F2} wu (live "
                + $"[WallFade] InsideEnterDepthFraction "
                + $"{WallFadeTuning.InsideEnterDepthFraction:F2} / InsideExitDepthFraction "
                + $"{WallFadeTuning.InsideExitDepthFraction:F2} x this crest; dwell "
                + $"{EnterDwellSeconds:F2}s in / {WallFadeTuning.DwellMoved:F2}s out). That "
                + "verdict is DIAGNOSTIC and gates no fade by itself. What gates, since ModBuild "
                + "271, is the strictly narrower WALK-IN latch built on top of it: [WallFade] "
                + $"WalkInStandDown {(WallFadeTuning.WalkInStandDown ? "ON" : "OFF")}, its own "
                + $"dwells {WallFadeTuning.WalkInEnterDwellSeconds:F2}s in / "
                + $"{WallFadeTuning.WalkInExitDwellSeconds:F2}s out, and it additionally needs "
                + "the HEIGHT slab at least "
                + $"{WallFadeTuning.WalkInHeadBelowCrestFraction:F2} x crest "
                + $"({-WallFadeTuning.WalkInHeadBelowCrestFraction * _live.BoardCrestWU:F2} wu) below "
                + "the crest plane AND this board's crest to measure "
                + (WallFadeTuning.WalkInMinCrestMetres > 0f
                    ? $"at least {WallFadeTuning.WalkInMinCrestMetres:F2} m in REAL METRES — the "
                      + "term that tells standing in a room from leaning over a diorama"
                    : "NOTHING AT ALL in real metres — [WallFade] WalkInMinCrestMetres is 0, so "
                      + "the term that tells standing in a room from leaning over a diorama is "
                      + "SWITCHED OFF and the mode may fire on a tabletop")
                + ". Outside that "
                + "mode every wall is decided solely by its own coverage against the live bars "
                + $"{WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}.");
        }

        /// <summary>
        /// THE INSIDE TEST — signed distance of the head to the cached board volume, through the
        /// Schmitt bars and dwells. O(1) and allocation-free by construction (see class header),
        /// so it is called every frame rather than on the evaluation cadence: the boundary
        /// crossing is a deliberate act and its edge must not wait out a skipped evaluation.
        /// Returns the debounced verdict.
        /// </summary>
        private bool UpdateInsideBoard(Vector3 headPos, float now, float rigScale)
        {
            if (!_live.BoardVolumeValid)
            {
                if (_insideBoard || _insidePending)
                    ResetInsideBoardState();
                return false;
            }

            Vector3 c = _live.BoardVolume.center, e = _live.BoardVolume.extents;
            float qx = Mathf.Abs(headPos.x - c.x) - e.x;
            float qy = Mathf.Abs(headPos.y - c.y) - e.y;
            float qz = Mathf.Abs(headPos.z - c.z) - e.z;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f), oz = Mathf.Max(qz, 0f);
            // Exact point-to-box signed distance: the outside part is Euclidean, the inside part
            // is the least-negative slab (how far the head is from the NEAREST face).
            float sd = Mathf.Sqrt(ox * ox + oy * oy + oz * oz)
                       + Mathf.Min(Mathf.Max(qx, Mathf.Max(qy, qz)), 0f);
            _lastInsideSigned = sd;
            _lastInsideMarginY = qy;
            _lastInsideMarginXZ = Mathf.Max(qx, qz);

            // LIVE, not cached: both bars are [WallFade] dials since ModBuild 272 and are read
            // per evaluation, so a value typed in the in-VR menu decides the very next frame.
            bool want = _insideBoard
                ? sd < WallFadeTuning.InsideExitDepthFraction * _live.BoardCrestWU
                : sd <= -WallFadeTuning.InsideEnterDepthFraction * _live.BoardCrestWU;
            // Same two-sided dwell as every other latch in this subsystem — Core/OcclusionFade.cs.
            // Different bars and a different clock source; identical debounce.
            if (OcclusionFade.StepDwell(want, now, EnterDwellSeconds, WallFadeTuning.DwellMoved,
                    ref _insidePending, ref _insidePendingSince, ref _insideBoard))
            {
                    // NO RELEASE. ModBuild 251 handed every faded wall back here in one step;
                    // that is the "alle auf einmal" the user rejected, and it bypassed each
                // wall's own exit dwell. Crossing this boundary is now an observation with
                // no side effects at all.
                LogInsideState(headPos, rigScale, edge: true);
                _nextInsideLogTime = now + InsideLogIntervalSeconds;
            }
            return _insideBoard;
        }

        /// <summary>
        /// THE WALK-IN LATCH (ModBuild 271) — the one verdict in this subsystem that gates a
        /// fade. It is <see cref="_insideBoard"/> AND three more terms, and the third of them is
        /// the whole reason a third attempt was allowed at all: the board's wall crest converted
        /// to REAL METRES by the live rig scale. Read the class header before touching any of it.
        ///
        /// <para>THE TERMS, all four required, all four printed by <see cref="LogInsideState"/>:
        /// <list type="number">
        /// <item>the debounced <see cref="_insideBoard"/> verdict holds — its Schmitt pair is
        ///   <see cref="WallFadeTuning.InsideEnterDepthFraction"/> /
        ///   <see cref="WallFadeTuning.InsideExitDepthFraction"/>, live dials since ModBuild 272
        ///   but shipped at the constants they replaced;</item>
        /// <item>the crest is at least <see cref="WallFadeTuning.WalkInMinCrestMetres"/> in real
        ///   metres, with its own release band at
        ///   <see cref="WallFadeTuning.WalkInCrestReleaseFraction"/> of that. THIS is the term
        ///   that kills the tabletop false positive: 0.61 m walls are a man leaning over his own
        ///   diorama, 1.6-1.9 m walls are a man standing in a room. Setting the metre bar to 0
        ///   switches the term OFF and hands the 251 behaviour back — deliberately reachable,
        ///   loudly named on the falsifier line, never a default;</item>
        /// <item>the HEIGHT slab is far enough inside — <c>_lastInsideMarginY &lt;
        ///   -WalkInHeadBelowCrestFraction * crest</c>, which at the shipped fraction of 0 is the
        ///   ModBuild 271 test <c>&lt; 0</c> to the bit. Being merely OVER the board, which is
        ///   what the FOOTPRINT term alone says and what decided the ModBuild 251 verdict 27
        ///   times against HEIGHT's 26, is never enough;</item>
        /// <item>the rig scale is readable. An unreadable scale makes the metre column
        ///   meaningless, and a latch that engages on a meaningless number is the ModBuild 241
        ///   failure with different arithmetic.</item>
        /// </list></para>
        ///
        /// <para>DWELLS. ModBuild 271 REUSED the INSIDE verdict's pair rather than inventing one
        /// (<c>EnterDwellSeconds</c> in, <see cref="WallFadeTuning.DwellMoved"/> out). ModBuild
        /// 272 gave the latch its OWN pair —
        /// <see cref="WallFadeTuning.WalkInEnterDwellSeconds"/> and
        /// <see cref="WallFadeTuning.WalkInExitDwellSeconds"/> — shipped at exactly those two
        /// values, so the promotion is invisible at the defaults. The one behaviour it does
        /// change: the walk-in release dwell no longer FOLLOWS a hand-tuned
        /// <c>ExitDwellMovedSeconds</c>; it is its own number now. Turning the dial off, or
        /// losing the board volume, still releases WITHOUT any exit dwell — neither is a moving
        /// head, so neither is what that dwell debounces.</para>
        ///
        /// <para>EVERY BAR IS READ LIVE, per evaluation, never cached at bind: a number typed in
        /// the in-VR menu decides the next frame, and the falsifier line prints the value that
        /// was actually used rather than the one the build shipped with.</para>
        /// </summary>
        private bool UpdateWalkInside(float now, float rigScale)
        {
            _walkRigScale = rigScale;
            _walkCrestBar = WallFadeTuning.WalkInMinCrestMetres;

            // GROUND GONE, not a boundary crossing. The 3D map room is the standing case for the
            // second arm: it owns no occlusion volumes at all, so CommitBoardVolume never marks
            // the volume valid there and this mode can never engage in it.
            if (!WallFadeTuning.WalkInStandDown || !_live.BoardVolumeValid)
            {
                _walkTermScale = rigScale > 1e-4f;
                _walkTermCrest = false;
                _walkTermHeight = false;
                _walkCrestMetres = -1f;
                // Do not leave the previous pass's bars on the fields — a stale number on the
                // falsifier line is worse than no number, because it reads like a measurement.
                _walkCrestDisabled = _walkCrestBar <= 0f;
                _walkCrestNeed = _walkCrestBar;
                _walkHeightNeed = 0f;
                _walkRefusal = !WallFadeTuning.WalkInStandDown
                    ? "CONFIG — [WallFade] WalkInStandDown is OFF"
                    : $"BOARD — the board volume is invalid ({_live.BoardVolumeRooms} decision-valid "
                      + "room(s)); the 3D map room has no occlusion volumes at all, so the mode "
                      + "can never engage there";
                if (_walkInside || _walkInsidePending)
                    ReleaseWalkInside();
                return false;
            }

            _walkTermScale = rigScale > 1e-4f;
            _walkCrestMetres = _walkTermScale ? _live.BoardCrestWU / rigScale : -1f;
            // THE CREST TERM CAN BE SWITCHED OFF, by setting its metre bar to 0. That is the one
            // dial that decides "standing in a room" against "leaning over a 61 cm diorama", so
            // switching it off restores exactly the ModBuild 251 behaviour the user rejected in
            // one session — the bound description says so in as many words. It is offered because
            // he asked to own these numbers, and the log below names it OFF whenever it is, so a
            // session that behaves like 251 can never be mistaken for a bug.
            _walkCrestDisabled = _walkCrestBar <= 0f;
            _walkCrestNeed = _walkCrestDisabled
                ? 0f
                : (_walkInside ? _walkCrestBar * WallFadeTuning.WalkInCrestReleaseFraction
                               : _walkCrestBar);
            _walkTermCrest = _walkTermScale
                             && (_walkCrestDisabled || _walkCrestMetres >= _walkCrestNeed);
            // HEIGHT, with a live depth requirement. At the shipped 0 this is `margin < 0` —
            // the ModBuild 271 test, unchanged to the bit.
            _walkHeightNeed = -WallFadeTuning.WalkInHeadBelowCrestFraction * _live.BoardCrestWU;
            _walkTermHeight = _lastInsideMarginY < _walkHeightNeed;

            bool want = _insideBoard && _walkTermScale && _walkTermCrest && _walkTermHeight;

            // LOG FAILURES, NOT ONLY SUCCESSES. Name the FIRST term that refused, with its live
            // value — a reason string that cannot change is an instrument that prints once and
            // then looks dead.
            _walkRefusal = want
                ? "-"
                : !_insideBoard
                    ? "INSIDE — the head is not inside the board volume (signed distance "
                      + $"{_lastInsideSigned:F2} wu, needs <= "
                      + $"{-WallFadeTuning.InsideEnterDepthFraction * _live.BoardCrestWU:F2}, i.e. "
                      + $"[WallFade] InsideEnterDepthFraction "
                      + $"{WallFadeTuning.InsideEnterDepthFraction:F2} x crest "
                      + $"{_live.BoardCrestWU:F2} wu)"
                    : !_walkTermScale
                        ? $"SCALE — the rig scale is unreadable ({rigScale:G4} wu per metre)"
                        : !_walkTermCrest
                            ? $"CREST — the board's walls are {_walkCrestMetres:F2} m tall, under "
                              + $"the {_walkCrestNeed:F2} m bar ([WallFade] WalkInMinCrestMetres "
                              + $"{_walkCrestBar:F2}"
                              + (_walkInside
                                  ? $" x [WallFade] WalkInCrestReleaseFraction "
                                    + $"{WallFadeTuning.WalkInCrestReleaseFraction:F2} release band"
                                  : string.Empty)
                              + "); this is a board you are LEANING OVER, not one you stand in — "
                              + "set WalkInMinCrestMetres nearer this reading, or to 0, if you "
                              + "want the mode here anyway"
                            : $"HEIGHT — the eye is not far enough below the wall crest (Y slab "
                              + $"{_lastInsideMarginY:F2} wu, needs < {_walkHeightNeed:F2} = "
                              + "-[WallFade] WalkInHeadBelowCrestFraction "
                              + $"{WallFadeTuning.WalkInHeadBelowCrestFraction:F2} x crest "
                              + $"{_live.BoardCrestWU:F2} wu); being merely OVER the board is the "
                              + "FOOTPRINT term, and that is never enough";

            // LIVE dwells, its OWN pair since ModBuild 272 — shipped at exactly the values the
            // latch used to borrow (EnterDwellSeconds 0.20 s in, DwellMoved 2.50 s out), so the
            // promotion changes nothing at the defaults. The debounce itself is the shared one
            // (Core/OcclusionFade.cs), which is what makes "its own pair" mean only the pair.
            if (OcclusionFade.StepDwell(want, now, WallFadeTuning.WalkInEnterDwellSeconds,
                    WallFadeTuning.WalkInExitDwellSeconds,
                    ref _walkInsidePending, ref _walkInsidePendingSince, ref _walkInside))
            {
                _walkEdgePending = true;
                _walkEdges++;
            }
            return _walkInside;
        }

        /// <summary>
        /// ModBuild 278 — STOP MEASURING WHAT THE NEXT BRANCH OVERRULES.
        ///
        /// <para><b>THE REQUEST</b>, user, 2026-08-25, verbatim: <i>"In dem Modus in dem man IM
        /// dem Level ist, kann das 'Abtasten' komplett deaktiviert werden so lange man in dem
        /// Modus ist um hier auch Performance zu sparen."</i> It arrived in the same message as
        /// the two cadence dials and it is the sharpest of the three, because inside this mode
        /// the work is not merely frequent — it is provably discarded.</para>
        ///
        /// <para><b>WHY IT IS SAFE, STATED AS A PROPERTY OF THE CODE AND NOT AS AN INTENTION.</b>
        /// While <c>_walkInside</c> holds, the decision loop's walk-in branch runs for EVERY
        /// segment and its whole body is <c>State = false; PendingRaw = false;
        /// SmoothInit = false</c>. It sits ABOVE the split-run branch and above the coverage
        /// branch, so no segment can reach a coverage number at all. Three things therefore
        /// compute an answer nothing reads: <c>UpdateSampleVisibility</c> (projects every room's
        /// floor samples through the head camera), <c>BlockedFraction</c> per segment, and the
        /// periodic rescan cycle that rebuilds the membership those two are measured over.</para>
        ///
        /// <para><b>WHAT IS DELIBERATELY NOT SUSPENDED.</b> The per-segment fade ramp and its
        /// material write keep running every frame. That is the ModBuild 271 ruling — the walls
        /// this mode holds solid come back through the ordinary ANIMATED un-fade, nothing snaps
        /// — and it is also why the suspension cannot be implemented by simply returning early
        /// from the tick. So is <c>UpdateWalkInside</c> itself, and <c>UpdateInsideBoard</c>
        /// under it: they are ~15 float ops and they are what NOTICES the release, so gating
        /// them on their own verdict would be a latch that can never let go — the "a claim must
        /// not measure itself" failure this project has a ledger entry for.</para>
        ///
        /// <para><b>SUSPEND, DO NOT BREAK.</b> A rescan cycle already in flight is allowed to
        /// RUN TO COMPLETION rather than being abandoned. The abandon path
        /// (<c>AbandonRescanCycle</c> → <c>ClearSurveyState</c>) is correct and atomic, but it
        /// also drops <c>_committedSigValid</c> and the drift ring — so the first cycle after
        /// the release would find no table to compare against, refuse the skip on the "no table
        /// yet" term, and pay a GUARANTEED ~90 ms commit on the very frame the player zooms back
        /// out. That is the hitch this whole build exists to remove, relocated to the worst
        /// possible moment. Letting the in-flight cycle finish costs at most one commit that was
        /// already scheduled and leaves the banked signature intact, so the release cycle can
        /// skip like any other. The segment table is never half-rebuilt either way: only the
        /// COMMIT stage mutates it and the commit is atomic within one frame.</para>
        ///
        /// <para><b>THE RELEASE EDGE IS IMMEDIATE, BY LEAVING THE CLOCKS ALONE.</b> Neither
        /// <c>_nextRescan</c> nor <c>_nextPathAudit</c> nor <c>_nextEvalTime</c> is pushed
        /// forward while suspended, so all three are already in the past when the latch drops
        /// and the first tick after the release evaluates, audits and opens a cycle without
        /// waiting out anything. No forced commit, and no separate release path that could rot.
        /// The suspension itself lags the latch by exactly one frame — this method runs after
        /// <c>UpdateWalkInside</c>, which runs after the rescan block — which is 11 ms against a
        /// 2 s cadence and a 2.5 s exit dwell. Moving the latch above the rescan block to close
        /// that would reorder a hard-won per-frame sequence for nothing.</para>
        ///
        /// <para><b>THE SPLIT RUNS ARE RE-SEEDED, and this is the one non-obvious consequence.</b>
        /// <c>EvaluateSplitRuns</c> runs on evaluation frames only, so suspending evaluation
        /// freezes every run's EMA. The walk-in branch's own comment is explicit about why that
        /// would be wrong — <i>"A FROZEN EMA IS THE 251 SYMPTOM WITH A DELAY … every wall would
        /// come out of the mode holding the SAME minute-old reading and could re-fade together
        /// on the next dwell. That is exactly the 'alle auf einmal' he rejected"</i> — and it
        /// solved it for unsplit walls by dropping <c>SmoothInit</c>, noting that split runs
        /// needed no equivalent because their evaluation kept running. It does not keep running
        /// any more, so this method supplies the equivalent: <c>ClearSplitRunDrive</c> on the
        /// engaging edge drops the whole run table, and the first evaluation after the release
        /// rebuilds every run from its members' OWN live coverage. Same end state as the unsplit
        /// arm, reached the same way — the walls leave the mode disagreeing, as they entered
        /// it.</para>
        ///
        /// <para><b>MULTIPLAYER.</b> Nothing here touches the wire. Own fades are broadcast by
        /// <c>WallSegmentFade.Net.cs</c> from the segment STATE, and while the mode holds every
        /// state is false by decree — which is what a peer should be told, because it is what is
        /// true on this machine. A peer whose own player is not walked in runs his own driver,
        /// his own latch and his own decision; <c>PeerBoardFade</c> is a different subsystem
        /// entirely (a teammate's control board, not the walls). Suspending a receiver's local
        /// SAMPLING cannot desync anything, because sampling is not a wire input.</para>
        ///
        /// <para><b>AND IT LOGS BOTH EDGES WITH COUNTS.</b> This project has lost builds to
        /// remedies that never ran; "the code is there" is not evidence. Grep a hardware log for
        /// <c>SAMPLING SUSPENDED</c> / <c>SAMPLING RESUMED</c>.</para>
        /// </summary>
        /// <returns>True while the sampling is stood down this frame.</returns>
        private bool UpdateSamplingSuspension(bool walkInside, float now)
        {
            bool want = walkInside && WallFadeTuning.WalkInSuspendSamplingOn;
            if (want == _samplingSuspended)
                return _samplingSuspended;

            if (want)
            {
                _samplingSuspended = true;
                _suspendEdges++;
                _suspendedSince = now;
                _suspendedEvaluations = 0;
                _suspendedCycles = 0;
                _suspendedNextTick = Mathf.Max(_nextRescan, now);
                // See THE SPLIT RUNS ARE RE-SEEDED above. Done on the EDGE and not per frame:
                // the table is rebuilt lazily by EvaluateSplitRuns from _live.Segments, so one clear
                // is enough and repeating it would be a per-frame dictionary clear for nothing.
                ClearSplitRunDrive();
                VRLog.Info(Name,
                    $"SAMPLING SUSPENDED [EDGE #{_suspendEdges}] — the walk-in stand-down holds "
                    + $"all {_live.Segments.Count} tracked wall segment(s) fully solid by decree "
                    + "(the WALK-IN STAND-DOWN line beside this one carries the held/hidden "
                    + "split for the pass it landed on; _walkHeld is written by the decision "
                    + "loop LATER in this same tick, so quoting it here would print the "
                    + "previous frame's count, which was taken while the mode was still off), "
                    + "so the coverage "
                    + "sampling, the fade decision and the rescan cadence are all stood down "
                    + "until it releases. STILL RUNNING: the per-segment fade ramp and its "
                    + "material write (every frame, so nothing snaps), the walk-in latch itself "
                    + "and the inside-the-board test that will notice the release, and a room "
                    + "reveal — which still opens a rescan cycle immediately, whatever this "
                    + $"says. A cycle in flight right now (stage {_rescanStage}) is allowed to "
                    + "finish rather than being torn up: abandoning it would drop the banked "
                    + "signature and force a guaranteed ~90ms commit on the frame you zoom back "
                    + "out. Split-run drive dropped so every run re-seeds its coverage from a "
                    + "LIVE reading on release instead of a stale one. Switch at [WallFade] "
                    + "WalkInSuspendSampling.");
            }
            else
            {
                ReleaseSamplingSuspension(now, "the walk-in stand-down released");
            }
            return _samplingSuspended;
        }

        /// <summary>
        /// Drop the suspension WITHOUT waiting for the ordinary edge, and say so.
        ///
        /// <para>WHY THIS EXISTS AND WHY IT IS NOT PARANOIA. <c>UpdateSamplingSuspension</c> runs
        /// LATE in the tick — after <c>UpdateWalkInside</c>, which is after the early return
        /// <c>if (_live.Segments.Count == 0 || _live.RoomBounds.Count == 0) return;</c> — while the flag it
        /// sets is read EARLY, at the rescan-cadence gate. That asymmetry is a latch that can
        /// never let go, and it is reachable: suspend inside a scenario, load a new one, and the
        /// segment table is empty while the flag is still true. The gate then refuses to open a
        /// cycle, the cycle is the only thing that could refill the table, and the tick returns
        /// at the empty-table guard before ever reaching the code that would clear the flag. The
        /// wall fade would be dead for the rest of the session with nothing in the log.
        ///
        /// <para>This project's ledger calls that shape "a claim must not measure itself" — a
        /// latch whose only release path is gated by the latch. Every site that force-releases
        /// the walk-in latch therefore force-releases this too, at the same instant, and the
        /// resume line names which one did it.</para>
        ///
        /// <para>ModBuild 284 — RENAMED FROM <c>LogSamplingResumed</c>, AND THAT IS THE WHOLE
        /// POINT OF THE RENAME. This method IS the release: <c>_samplingSuspended = false</c>
        /// happens here, and the log line is a passenger. Under its old name it sat in a file
        /// full of <c>Log*</c> methods that really are log-only, one grep away from being
        /// retired in a diagnostics cleanup — and retiring it would have latched the wall fade
        /// off for the rest of the session with nothing in the log, which is exactly the
        /// deadlock the paragraph above describes. This project's ledger already has an entry
        /// for a fix that lived inside the instrument meant to test it. DO NOT put the write
        /// back behind a <c>Log</c> name, and do not gate this call on
        /// <c>PerfConfig.Quiet</c>.</para>
        /// </para></summary>
        private void ReleaseSamplingSuspension(float now, string cause)
        {
            if (!_samplingSuspended)
                return;
            _samplingSuspended = false;
            _suspendEdges++;
            float held = now - _suspendedSince;
            VRLog.Info(Name,
                    $"SAMPLING RESUMED [EDGE #{_suspendEdges}] — {cause}. Stood down for {held:F1}s, in "
                    + $"which the coverage sampling was skipped on {_suspendedEvaluations} "
                    + $"FRAME(S) and {_suspendedCycles} rescan cadence tick(s) went by unused. "
                    + "READ BOTH FIGURES NARROWLY. The frame count is frames, not evaluations: "
                    + "with [WallFade] EvalIntervalSeconds above 0 not every one of them would "
                    + "have evaluated anyway, so it is an UPPER bound on the decisions removed. "
                    + $"The cycle count is periods of the live {WallFadeTuning.RescanIntervalSeconds:0.00}s "
                    + "cadence, i.e. the number of ~85-134ms commit OPPORTUNITIES this mode "
                    + "removed and not the number of commits it saved — most cycles skip their "
                    + "commit anyway, see the BUDGET line's SKIP clause. NOTHING WAITS: "
                    + "_nextRescan, _nextPathAudit and _nextEvalTime were all left in the past, "
                    + "so this very frame evaluates and the next tick opens a cycle. Every wall "
                    + "is back on its own coverage against the live bars "
                    + $"{WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}, and every split run "
                    + "re-seeds from its own live reading."
                    + (WallFadeTuning.WalkInSuspendSamplingOn
                        ? string.Empty
                        : " (This edge is [WallFade] WalkInSuspendSampling being switched OFF, "
                          + "not the player stepping out.)"));
        }

        /// <summary>Drop the walk-in latch without waiting out an exit dwell (dial off, board
        /// volume gone, scenario teardown). The edge still prints: a mode that stops holding
        /// walls must never do so silently.</summary>
        private void ReleaseWalkInside()
        {
            if (_walkInside)
            {
                _walkEdgePending = true;
                _walkEdges++;
            }
            _walkInside = false;
            _walkInsidePending = false;
            _walkInsidePendingSince = 0f;
            // ModBuild 278 — the suspension goes with the latch it belongs to, at the same
            // instant and never one tick later. See ReleaseSamplingSuspension for the deadlock this
            // closes; it is a no-op whenever the suspension was not engaged.
            ReleaseSamplingSuspension(Time.unscaledTime,
                "the walk-in latch was force-released (dial off, board volume gone, or scenario "
                + "teardown) — not the player stepping out");
        }

        /// <summary>
        /// THE WALK-IN EDGE LINE — unthrottled, printed after the decision loop of the pass the
        /// edge landed on, so the counts it carries are what the latch ACTUALLY did on that pass
        /// and not what it intended to do. <c>held</c> is every segment the walk-in branch forced
        /// solid; <c>of which hidden</c> is how many of those were faded or mid-ramp at that
        /// moment — i.e. exactly what the user is about to watch reappear.
        /// </summary>
        private void LogWalkInsideEdge(Vector3 headPos)
        {
            _walkEdgePending = false;
            VRLog.Info(Name,
                $"WALK-IN STAND-DOWN: {(_walkInside ? "ENGAGED" : "RELEASED")} "
                + $"[EDGE #{_walkEdges}] — {_walkHeld} wall segment(s) held fully solid on this "
                + $"pass, of which {_walkHeldHidden} were faded or mid-ramp at the moment it "
                + $"{(_walkInside ? "engaged" : "released")} (those are the ones that "
                + $"{(_walkInside ? "reappear" : "may fade again")}, through the ordinary "
                + "animated ramp — nothing snaps). Head "
                + $"({headPos.x:F1}, {headPos.y:F2}, {headPos.z:F1}); board crest "
                + $"{_live.BoardCrestWU:F2} wu = {_walkCrestMetres:F2} m at rig scale "
                + $"{_walkRigScale:F2} wu/m, bar "
                + (_walkCrestDisabled
                    ? "OFF ([WallFade] WalkInMinCrestMetres is 0 — the diorama safeguard is "
                      + "switched off)"
                    : $"{_walkCrestBar:F2} m (release below "
                      + $"{_walkCrestBar * WallFadeTuning.WalkInCrestReleaseFraction:F2} m)")
                + $"; dwells {WallFadeTuning.WalkInEnterDwellSeconds:F2}s in / "
                + $"{WallFadeTuning.WalkInExitDwellSeconds:F2}s out; INSIDE "
                + $"{(_insideBoard ? "YES" : "no")}, signed distance {_lastInsideSigned:F2} wu, "
                + $"Y slab {_lastInsideMarginY:F2}, XZ slab {_lastInsideMarginXZ:F2}. "
                + (_walkInside
                    ? "While this holds, EVERY wall is solid without exception — coverage, split "
                      + "runs, a teammate's synced fade and a gate lift are all overruled."
                    : $"Refused by {_walkRefusal}. Every wall is back on its own coverage "
                      + $"against the live bars {WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}."));
        }

        /// <summary>
        /// Drop the cached volume AND the verdict that was measured against it. Called on a
        /// scene load: a box measured in the previous scenario is not evidence about this one,
        /// and between the load and the first commit the head could sit anywhere relative to it.
        /// The next commit rebuilds both and the BOARD VOLUME line re-announces them.
        /// </summary>
        private void InvalidateBoardVolume()
        {
            _live.BoardVolumeValid = false;
            _live.BoardVolumeRooms = 0;
            _boardVolumeWalls = 0;
            _live.BoardCrestWU = 0f;
            _bvSigRooms = -1;
            _bvSigWalls = -1;
            ResetInsideBoardState();
        }

        /// <summary>Drop the INSIDE verdict (scenario teardown, toggle off, no valid board).
        /// Purely diagnostic state now — nothing downstream reads it.</summary>
        private void ResetInsideBoardState()
        {
            _insideBoard = false;
            _insidePending = false;
            _insidePendingSince = 0f;
            // The walk-in latch (ModBuild 271) is a strict refinement of the verdict above, so
            // it can never outlive it.
            ReleaseWalkInside();
        }

        /// <summary>
        /// Reset the per-pass independence census. Called every frame from the tick, so the
        /// numbers the falsifier prints always describe the pass that just ran.
        /// </summary>
        private void BeginPerWallCensus()
        {
            _pwTotal = 0;
            _pwFaded = 0;
            _pwNames.Clear();
            _pwMaxSmooth = 0f;
            _pwMinSmooth = float.MaxValue;
            _pwMaxWall = "-";
            _pwMinWall = "-";
            _pwPeerDriven = 0;
            _pwGateDriven = 0;
            _animSmooth = 0;
            _animStepped = 0;
            _animSteppedNames.Clear();
            _folNative = 0;
            _folSwapped = 0;
            _folOwnChannel = 0;
            _folNoChannel = 0;
            _folNoChannelNames.Clear();
            _admitNames.Clear();
            _pwRunPieceNames.Clear();
            _pwRunPieceTotal = 0;
            // ModBuild 271: the walk-in census is strictly PER PASS — the edge line is printed
            // from the very pass that flipped the latch, so these must describe that pass only.
            _walkHeld = 0;
            _walkHeldHidden = 0;
            // NB: the STEP counters are deliberately NOT reset here. An edge is a rare event —
            // seven fade-ON events in the whole ModBuild 254 session — and this census resets
            // every frame while the falsifier prints every two seconds, so per-frame counters
            // would miss essentially every step that ever happened. They are SESSION totals;
            // only the freshest names are rotated, so the line always carries the history.
            _headInMasonryWall = "-";
        }

        /// <summary>Record a discontinuity applied to a wall's own renderers at one END of the
        /// ramp: our property block going on (fade leaving 0) or coming off (fade reaching 0).
        /// The block asserts ToggleWallFade / _ToggleWallfade / _WallFade_On in one frame, so if
        /// opening that branch changes how the material shades, this is where it shows.</summary>
        private void NoteBlockEdge(Segment seg, bool installed)
        {
            if (installed)
            {
                _stepOutBlockInstalled++;
                if (_stepOutNames.Count >= PerWallNameCap)
                    _stepOutNames.RemoveAt(0); // keep the freshest, never grow unbounded
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _stepOutNames.Add($"'{wall}' block installed at fade {seg.Fade:F3}");
            }
            else
            {
                _stepInBlockCleared++;
            }
        }

        /// <summary>Record a MATERIAL SWAP taking effect or being undone. A swap replaces the
        /// piece's shader outright, so it is the largest possible single-frame change and the
        /// prime suspect whenever a transition reads as a pop in one direction only.</summary>
        private void NoteSwapEdge(Segment seg, MountedProp p, bool installed)
        {
            if (installed)
            {
                _stepOutSwapInstalled++;
                if (_stepOutNames.Count >= PerWallNameCap)
                    _stepOutNames.RemoveAt(0);
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string piece = p.Renderer != null ? p.Renderer.name : "<dead>";
                _stepOutNames.Add($"'{piece}' on '{wall}' SWAPPED to the masonry fade shader "
                    + $"at fade {seg.Fade:F3}");
            }
            else
            {
                _stepInSwapRemoved++;
            }
        }

        /// <summary>
        /// R1 FALSIFIER, THE HALF THAT SEES EDGES. Silent when no step happened, so the line's
        /// presence is itself the finding.
        /// </summary>
        private void LogStepEdges()
        {
            int outs = _stepOutBlockInstalled + _stepOutSwapInstalled;
            int ins = _stepInBlockCleared + _stepInSwapRemoved;
            if (outs == 0 && ins == 0)
                return;
            VRLog.Info(Name,
                $"STEP (session totals): {outs} discontinuit(y/ies) applied at the START of a fade-OUT "
                + $"({_stepOutBlockInstalled} property block installed, {_stepOutSwapInstalled} "
                + $"material swap) and {ins} at the END of a fade-IN ({_stepInBlockCleared} block "
                + $"cleared, {_stepInSwapRemoved} swap removed). A step at the START of a "
                + "fade-out is visible on solid geometry and is what reads as a POP; the same "
                + "step at the end of a fade-in lands on already-solid geometry and is invisible. "
                + "ModBuild 256 made the block install itself VISUALLY inert: the cutoff ramp "
                + "now starts at -0.15, so the first observable frame (Fade ≈ 0.09, since "
                + "fadeStep is ~0.088 at 90 Hz) still carries a NEGATIVE cutoff and clips no "
                + "fragment at all. A non-zero out-edge count here is therefore expected and no "
                + "longer means a visible pop — what would still mean one is the material-swap "
                + "column being non-zero."
                + (_stepOutNames.Count > 0
                    ? " Most recent out-edges: " + string.Join(", ", _stepOutNames) + "."
                    : " No out-edge has been recorded yet this session."));
        }

        /// <summary>
        /// Record the dissolve channel ONE foliage piece actually received this frame — read off
        /// the record <c>EnsureDissolveChannel</c> just filled in, so it reports what the piece
        /// got rather than what it was meant to get. A piece with no channel at all cannot
        /// dissolve; it can only switch off, and it is named.
        /// </summary>
        private void NoteFoliageChannel(Segment seg, MountedProp p)
        {
            if (p.NativeFade)
            {
                if (p.SwapCopies != null)
                    _folSwapped++;
                else
                    _folNative++;
                return;
            }
            if (p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0 || p.System != null)
            {
                _folOwnChannel++;
                return;
            }
            _folNoChannel++;
            if (_folNoChannelNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                string piece = p.Renderer != null ? p.Renderer.name : "<dead>";
                _folNoChannelNames.Add($"'{piece}' on '{wall}'");
            }
        }

        /// <summary>
        /// R2 FALSIFIER, second half. Record how ONE wall's pieces split between the occlusion
        /// numerator and the ground dressing the standing test excludes, and name the widest
        /// excluded piece — the one the user asked about directly
        /// (<i>"größere nicht begehbare Flächen … kann es sein, dass diese Flächen irgendeine
        /// Rolle bei dem Problem spielen?"</i>). Measured by re-reading the live bounds, so it
        /// reports the same verdict the numerator used.
        /// </summary>
        private void NoteAdmission(Segment seg)
        {
            if (!seg.HasBounds || seg.DoorRoot != null || _admitNames.Count >= PerWallNameCap)
                return;
            int admitted = 0, excluded = 0;
            float widestExcluded = 0f;
            string widestName = "-";
            CountAdmission(seg.Renderers, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Foliage, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            CountAdmission(seg.Siblings, ref admitted, ref excluded, ref widestExcluded,
                ref widestName);
            if (admitted == 0 && excluded == 0)
                return;
            string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            _admitNames.Add($"'{wall}' {admitted + excluded} piece(s), {excluded} FLAT"
                + (excluded > 0 ? $", widest flat '{widestName}' {widestExcluded:F1} wu" : ""));
        }

        private static void CountAdmission(List<MeshRenderer> list, ref int admitted,
            ref int excluded, ref float widestExcluded, ref string widestName)
        {
            for (int i = 0; i < list.Count; i++)
            {
                MeshRenderer r = list[i];
                if (r == null)
                    continue;
                Bounds b = r.bounds;
                if (IsStandingPiece(b))
                {
                    admitted++;
                    continue;
                }
                excluded++;
                float widest = Mathf.Max(b.size.x, b.size.z);
                if (widest > widestExcluded)
                {
                    widestExcluded = widest;
                    widestName = r.name;
                }
            }
        }

        /// <summary>
        /// Record ONE wall's own verdict, taken from its own coverage of its own room. Only
        /// decision-eligible walls are counted: a doorway, an engulfing segment, a boundless one
        /// or a wall whose room has no valid floor grid is held solid by a different rule, and
        /// counting those would let the falsifier claim independence it did not measure.
        /// </summary>
        private void NotePerWallVerdict(Segment seg)
        {
            if (!seg.HasBounds || seg.Engulfing || seg.DoorRoot != null
                || !RoomDecisionValid(seg.RoomIndex))
                return;
            // ModBuild 259: a split-run member is NOT an independent decider and counting it as
            // one is how "faded 1 of 46" came to describe four walls and forty-two fragments of a
            // fifth. Its own measurement is still printed — that is the number this round turned
            // on — but under the run that owns it, and the SPLIT RUN line carries the verdict.
            if (seg.RunDriven)
            {
                _pwRunPieceTotal++;
                if (_pwRunPieceNames.Count < PerWallRunPieceCap)
                {
                    string piece = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                    string owner = seg.RunOwner != null ? seg.RunOwner.name : "<orphan>";
                    // MODBUILD 269 — TWO FIELDS THAT WERE READ AS SOMETHING ELSE ENTIRELY.
                    //
                    // (a) `r{RoomIndex}` was an unlabelled letter-and-number, and the ModBuild-267
                    //     log's two shelf entries read 'Wall 1' r3 and 'Wall 1' r0. That was taken
                    //     for two RUNS of one wall — "why is r3 a separate run from the run that
                    //     fades" — and a whole round's remedy was designed against a split that
                    //     does not exist. There is exactly ONE run per ProceduralWall
                    //     (RefreshSplitWall stamps `sub.RunOwner = wall`); r is the piece's ROOM,
                    //     and a run legitimately spans several. Spelled out here, once.
                    //
                    // (b) `blk X/Y` is the PIECE's own reading, and the same log's shelf entry
                    //     reads blk 0/16 — which was then quoted as the RUN's vote ("its run votes
                    //     'I hide nothing'"). The clause already said "(verdict from the run)" and
                    //     that was not enough, because the only number on the line was the
                    //     piece's. The run's own coverage, ema and state now stand beside it, so
                    //     the two can never be read as one again.
                    string runNote = "<no live run>";
                    if (seg.RunOwner != null && _runs.TryGetValue(seg.RunOwner, out WallRun? run))
                    {
                        runNote = $"RUN {run.Blocked}/{run.Total} of room {run.Room} ema "
                            + $"{run.Smooth:F2} " + (run.State ? "FADED" : "solid");
                    }
                    _pwRunPieceNames.Add($"'{piece}' of run '{owner}' PIECE room {seg.RoomIndex} "
                        + $"(a room index, NOT a run id) ema {seg.Smooth:F2} blk "
                        + $"{seg.LastBlocked}/{seg.LastRoomTotal} "
                        + (seg.State ? "FADED" : "solid")
                        + $" — verdict from the run, which reads {runNote}");
                }
                return;
            }
            _pwTotal++;
            if (seg.LastRoomTotal > 0)
                _pwCells = seg.LastRoomTotal;
            if (seg.State)
                _pwFaded++;
            if (seg.Smooth > _pwMaxSmooth)
            {
                _pwMaxSmooth = seg.Smooth;
                _pwMaxWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (seg.Smooth < _pwMinSmooth)
            {
                _pwMinSmooth = seg.Smooth;
                _pwMinWall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
            }
            if (_pwNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                // PER-CELL ATTRIBUTION. "blocks 2 of 16" has been the unanswerable question in
                // every round since ModBuild 250; naming the cells and the piece that took the
                // first one settles whether a low reading is real floor or geometry residue.
                //
                // WHAT A CELL INDEX MEANS CHANGED IN ModBuild 258. It is still the room-relative
                // sample index in lattice order (ix outer, iz inner), but the sample now sits on
                // the nearest PLAYABLE HEX to that lattice position rather than on the lattice
                // position itself — so a cell can no longer be a corner of the room's bounding
                // rectangle with no tile under it. That is the whole ModBuild 257 defect: all
                // four green walls pinned at a different corner of that rectangle (#0,#1,#4,#8 /
                // #7,#11,#14,#15 / #2,#3 / #12), each taken by a prop standing off the field.
                // Cross-build comparisons of these indices against a pre-258 log are therefore
                // comparisons of two different point sets; the SAMPLE GRID line reports how far
                // each room's positions had to move. See WallSegmentFade.cs, RebuildSamples.
                string cells = seg.LastBlockedCells.Count == 0
                    ? "none"
                    : "#" + string.Join(",#", seg.LastBlockedCells);
                // WHICH LIST AND WHICH KIND OF HIT (ModBuild 257). ModBuild 256's name settled
                // WHAT decides each wall and could not settle WHERE the fix goes — 'Renderers'
                // means the tileset parented the piece under the wall run and the membership
                // classifier owns it, 'Stacked'/'Siblings' means an adoption sweep claimed it and
                // that sweep owns it. 'contains' vs 'ray' is the ratchet: a Contains hit does not
                // depend on the head, so every cell taken that way is a floor this wall can never
                // fall below. See Segment.LastBlockerList and Segment.LastContainsCells.
                string blocker = seg.LastBlockerPiece != null
                    ? $" first by '{seg.LastBlockerPiece.name}' [{seg.LastBlockerList}/"
                      + (seg.LastBlockerByContains ? "contains" : "ray") + "]"
                    : seg.LastBlockerList != "-" ? $" first by [{seg.LastBlockerList}]" : "";
                // The head-INDEPENDENT part of this wall's coverage, spelled out next to the bars
                // it is measured against. A wall whose contains-count alone clears the exit bar
                // is latched by construction and no amount of walking around can release it.
                string latch = seg.LastContainsCells > 0
                    ? $" ({seg.LastContainsCells} of them by Contains — head-INDEPENDENT, "
                      + $"a coverage floor of "
                      + (seg.LastRoomTotal > 0
                          ? (seg.LastContainsCells / (float)seg.LastRoomTotal).ToString("F2")
                          : "n/a")
                      + $" against exit bar {WallFadeTuning.Off:F2})"
                    : "";
                _pwNames.Add($"'{wall}' r{seg.RoomIndex} ema {seg.Smooth:F2} "
                    + $"blk {seg.LastBlocked}/{seg.LastRoomTotal} "
                    + (seg.State ? "FADED" : "solid")
                    + $" cells {cells}{blocker}{latch}");
            }
            NoteAdmission(seg);
            // Kept as an OBSERVATION after ModBuild 255 deleted the hard-1f shortcut it used to
            // feed. "Is his head actually in the masonry" is still the right question to be able
            // to answer when a wall dissolves unexpectedly — it just may not override a
            // measurement any more.
            if (_headInMasonryWall == "-" && seg.Bounds.Contains(_lastHeadPos)
                && HeadInsideWallMesh(seg, _lastHeadPos, out string hitMesh))
            {
                string w = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _headInMasonryWall = $"'{w}' (mesh '{hitMesh}')";
            }
        }

        /// <summary>Record where a wall's fade came from when it was not its own decision.</summary>
        private void NotePerWallOutcome(Segment seg, bool remoteFade, bool gateLift, int peerId)
        {
            if (seg.DoorRoot != null)
                return;
            if (remoteFade)
                _pwPeerDriven++;
            else if (gateLift && !seg.State)
                _pwGateDriven++;
        }

        /// <summary>
        /// Record which animation path a fade in flight actually took this frame. Called from
        /// <c>Apply</c>, so it reports the branch that ran rather than the branch intended.
        /// </summary>
        private void NoteAnimationPath(Segment seg, bool smooth)
        {
            if (smooth)
            {
                _animSmooth++;
                return;
            }
            _animStepped++;
            if (_animSteppedNames.Count < PerWallNameCap)
            {
                string wall = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                _animSteppedNames.Add($"'{wall}' "
                    + (seg.VariantLow && seg.VariantHigh ? "HIGH+LOW" : "LOW")
                    + $" fade {seg.Fade:F2}");
            }
        }

        /// <summary>
        /// R2 FALSIFIER (user ruling 2026-08-24: <i>"Ich will aber das jede Wand einzeln
        /// verschwinden kann und andere bleiben"</i>). One line that shows the walls DISAGREEING
        /// — every number measured on the pass that just ran, and the per-wall detail printed so
        /// the disagreement is visible rather than asserted.
        ///
        /// <para>THE NUMBER THAT MATTERS is "faded X of Y". The defect he reported has X equal to
        /// 0 or to Y on every single pass; independence means X spends time strictly between
        /// them. The session tallies below count exactly that, so one grep answers it: a session
        /// with <c>0 mixed</c> has reproduced the defect, whatever the rest of the line says.</para>
        ///
        /// <para>The coverage SPREAD (widest against narrowest, with names) is the second half of
        /// the proof: walls can only disagree about the verdict if they are being measured
        /// separately, and a spread of zero would mean they are not.</para>
        /// </summary>
        private void LogPerWallIndependence()
        {
            if (_pwTotal == 0 && _runsTotal == 0)
                return;
            if (_pwTotal == 0)
            {
                // Every decision-eligible segment this pass belongs to a split run. The
                // independence claim is then entirely the SPLIT RUN line's to make, and asserting
                // a spread over an empty population would be the ModBuild 252 mistake again.
                VRLog.Info(Name,
                    $"PER-WALL: 0 independently-deciding wall(s) this pass — all "
                    + $"{_runMembersTotal} decision-eligible piece(s) belong to "
                    + $"{_runsTotal} split run(s), of which {_runsFaded} faded. See the SPLIT RUN "
                    + "line; per-wall independence is a statement about RUNS, not about the "
                    + "fragments one run was carved into.");
                return;
            }
            bool mixed = _pwFaded > 0 && _pwFaded < _pwTotal;
            if (mixed)
                _pwMixedPasses++;
            else
                _pwUniformPasses++;
            float minSmooth = _pwMinSmooth == float.MaxValue ? 0f : _pwMinSmooth;
            VRLog.Info(Name,
                $"PER-WALL: faded {_pwFaded} of {_pwTotal} independently-deciding wall(s) this "
                + $"pass (plus {_runsFaded} of {_runsTotal} SPLIT RUN(s) driving "
                + $"{_runMembersTotal} piece(s) — ModBuild 259; the pieces are named at the end "
                + "of this line and no longer inflate the count, which is why Y dropped from 46) "
                + "— "
                + (mixed
                    ? "MIXED, so the walls are disagreeing and each one is deciding for itself"
                    : _pwFaded == 0
                        ? "all solid (unanimous: no wall's own coverage clears its bar)"
                        : "all faded (unanimous — check the spread below before calling it a "
                          + "global switch; unanimity is legitimate when every wall really is "
                          + "in the way)")
                + $". Session so far: {_pwMixedPasses} mixed pass(es) vs {_pwUniformPasses} "
                + "unanimous — a session that never goes mixed is the defect reported on "
                + "2026-08-24. Coverage spread this pass: widest "
                + $"'{_pwMaxWall}' {_pwMaxSmooth:F2}, narrowest '{_pwMinWall}' {minSmooth:F2}, "
                + $"bars {WallFadeTuning.On:F2}/{WallFadeTuning.Off:F2}"
                // THE ARITHMETIC, SPELLED OUT. A threshold finer than the grid's own quantum
                // cannot be expressed: with 16 cells the quantum is 0.0625, so an exit bar of
                // 0.10 really means "at most ONE blocked cell", which is what made the old band
                // a one-way ratchet. Printing both numbers means the next reader checks this in
                // one line instead of deriving it from the source.
                + $" against a {_pwCells}-cell floor grid (quantum "
                + (_pwCells > 0 ? (1f / _pwCells).ToString("F4") : "n/a")
                + $" — the exit bar is {(_pwCells > 0 ? Mathf.CeilToInt(WallFadeTuning.Off * _pwCells) : 0)} "
                + "cell(s), the enter bar "
                + $"{(_pwCells > 0 ? Mathf.CeilToInt(WallFadeTuning.On * _pwCells) : 0)}). "
                + "Head inside masonry: "
                + $"{_headInMasonryWall} — an OBSERVATION; since ModBuild 255 this no longer "
                + "forces any wall's coverage to 1.00, the ray test measures what such a wall "
                + "actually covers. Not its own decision: "
                + $"{_pwPeerDriven} peer-driven, {_pwGateDriven} gate-lifted. Per wall: "
                + string.Join(" | ", _pwNames)
                + (_pwTotal > _pwNames.Count ? $" | +{_pwTotal - _pwNames.Count} more" : "")
                + ". Shape census — EVERY piece below is in the numerator (the standing-ratio "
                + "gate was retired in ModBuild 255: StripGroundRenderers already removes "
                + "anything topping out within 1.0 wu of the floor, and the ratio only ever "
                + "excluded real capstones). 'flat' here is a WARNING, not an exclusion: it "
                + "means something under 0.5 height-per-width survived the ground strip and can "
                + "claim samples through Contains(): " + string.Join(" | ", _admitNames)
                // MODBUILD 269 — THE MISSING TRUNCATION MARKER. The per-wall clause directly
                // above has carried "+N more" since it was written; this clause never did, and in
                // the ModBuild-267 log it printed FOUR names while `_pwRunPieceTotal` was 253. A
                // list that stops without saying so is not a sample, it is a wrong answer with a
                // confident shape — and it is the whole reason this round opened on a premise
                // that the same log's SPLIT RUN line contradicts in one grep.
                + (_pwRunPieceNames.Count > 0
                    ? $". Split-run pieces — {_pwRunPieceTotal} decision-eligible this pass, "
                      + $"{_pwRunPieceNames.Count} named below in dictionary order (measured "
                      + "individually, DECIDED by their run — ModBuild 259; each entry now "
                      + "carries its RUN's own coverage as well as its own, because those two "
                      + "numbers were read as one in the 267 log): "
                      + string.Join(" | ", _pwRunPieceNames)
                      + (_pwRunPieceTotal > _pwRunPieceNames.Count
                          ? $" | +{_pwRunPieceTotal - _pwRunPieceNames.Count} more NOT NAMED — "
                            + "this list is TRUNCATED and the pieces it omits are not a random "
                            + "sample; read the SPLIT RUN and RUN FADE lines for the population"
                          : " | complete — nothing omitted")
                    : ". No split-run piece was decision-eligible this pass.")
                + ".");
        }

        /// <summary>
        /// R1 FALSIFIER (user ruling 2026-08-24: <i>"Wände sollen niemals einfach so auftauchen
        /// und wieder verschwinden … IMMER mit der Animation, niemals ohne"</i>). Reports which
        /// animation path every fade IN FLIGHT actually took this frame — measured in
        /// <c>Apply</c>, not inferred.
        ///
        /// <para>A wall on the CONTINUOUS path sweeps one cutoff across the occluded map's
        /// world-Y gradient from fully solid to exactly the held look, with no texture swap and
        /// no step. A wall on the STEPPED path is a LOW or mixed shader variant, whose two
        /// branches do not meet at <c>Fade == 1</c>: it is fully clipped just below 1 and gets
        /// its foundation band back at 1. Those are named, because a non-zero stepped count is
        /// the remaining un-animated transition in the subsystem and it needs a shader change
        /// rather than a tuning.</para>
        ///
        /// <para>Silent when nothing is fading, so the line's presence already means a
        /// transition was in flight when it printed.</para>
        /// </summary>
        private void LogAnimationPaths()
        {
            int foliage = _folNative + _folSwapped + _folOwnChannel + _folNoChannel;
            if (_animSmooth == 0 && _animStepped == 0 && foliage == 0)
                return;
            VRLog.Info(Name,
                $"ANIMATION: {_animSmooth + _animStepped} wall renderer fade(s) in flight this "
                + $"frame — {_animSmooth} mid-DISSOLVE on the noise map (the only path that "
                + "yields intermediate pixels: m = 1-r varies per texel, so a rising cutoff "
                + $"retires the wall progressively), {_animStepped} sitting in the HELD state on "
                + "the occluded map, where the noise is multiplied by ZERO and the picture is "
                + "binary in the shader's world-Y term. The residual discontinuity is the "
                + "boundary between those two, at Fade==1: the foundation band winks as the map "
                + "swaps. ModBuild 252 tried to remove it by running the whole ramp on the "
                + "occluded map and made the ENTIRE wall binary instead — reverted in 255, "
                + "falsified by frame-by-frame video (single 33 ms step, zero intermediate "
                + "frames, in BOTH directions)"
                + (_animStepped > 0 && _animSteppedNames.Count > 0
                    ? ": " + string.Join(", ", _animSteppedNames)
                    : "")
                // THE HALF THE ModBuild 253 LINE DID NOT COUNT. It reported only the numbers
                // above, said "every transition in flight is animated end to end", and was
                // believed — while the largest population in the scene was measured by nobody.
                + $". FOLIAGE this frame: {foliage} attachment(s) — {_folNative} on the wall's "
                + $"own native ramp, {_folSwapped} on swapped masonry-fade copies, "
                + $"{_folOwnChannel} on their own alpha/cutoff/particle channel, "
                + (_folNoChannel == 0
                    ? "and 0 on the STAGGERED path."
                    : $"and {_folNoChannel} on the STAGGERED path — no material channel of their "
                      + "own, so each switches off at its own stable point of the ramp instead "
                      + "of being material-swapped (ModBuild 255: the swap was the pop). A high "
                      + "count here is EXPECTED and not a fault; what would be a fault is a "
                      + "non-zero swap count on the STEP line. Sample: "
                      + string.Join(", ", _folNoChannelNames)
                      + (_folNoChannel > _folNoChannelNames.Count
                          ? $", +{_folNoChannel - _folNoChannelNames.Count} more" : "")));
        }
        /// <summary>
        /// THE FALSIFIER. One line that can disagree with itself: the verdict, the term that
        /// decided it, the head against the board volume in world units AND in real metres, the
        /// live bars, how many walls the raised bar spared this evaluation, and the coverage of
        /// the worst offender among them. Every number is an outcome, not an intention.
        ///
        /// <para>The real-metre column WAS a pure conversion of the world-unit numbers and read
        /// by nothing. Since ModBuild 271 one of its terms — the crest in metres — is a DECIDER:
        /// it is term (b) of the walk-in latch, and it is the term that separates a man standing
        /// between 1.6-1.9 m walls from a man leaning over a 0.61 m diorama. The INSIDE bars
        /// themselves are still world units against world-unit geometry.</para>
        ///
        /// <para>THE LINE MUST BE ABLE TO DISAGREE WITH ITSELF, so it carries the walk-in verdict
        /// AND every one of its four terms with its live value and its own PASS/FAIL, and when
        /// the latch is off while INSIDE holds it names the term that refused. A change-gated
        /// line whose reason string is constant prints once and then reads like a dead
        /// instrument; every number here is an outcome, not an intention.</para>
        /// </summary>
        private void LogInsideState(Vector3 headPos, float rigScale, bool edge)
        {
            if (!_live.BoardVolumeValid)
                return;
            Vector3 mn = _live.BoardVolume.min, mx = _live.BoardVolume.max;
            bool byY = _lastInsideMarginY >= _lastInsideMarginXZ;
            string term = _insideBoard
                ? (byY ? "HEIGHT (the eye is below the wall crest, and that is the tighter of "
                       + "the two terms)"
                       : "FOOTPRINT (the head is over the board, and that is the tighter of the "
                       + "two terms)")
                : (byY ? "HEIGHT (the eye is at or above the wall crest)"
                       : "FOOTPRINT (the head is beyond the board edge)");
            string metres = rigScale > 1e-4f
                ? $"{(headPos.y - _live.BoardFloorY) / rigScale:F2} m above the floor plane, walls "
                  + $"{_live.BoardCrestWU / rigScale:F2} m tall, signed distance "
                  + $"{_lastInsideSigned / rigScale:F2} m"
                : "n/a (rig scale unreadable)";
            VRLog.Info(Name,
                $"INSIDE THE MAP: {(_insideBoard ? "YES" : "no")}"
                + $"{(edge ? " [EDGE]" : "")} — head "
                + $"({headPos.x:F1}, {headPos.y:F2}, {headPos.z:F1}) vs board volume "
                + $"[x {mn.x:F1}..{mx.x:F1}, y {mn.y:F2}..{mx.y:F2}, z {mn.z:F1}..{mx.z:F1}] "
                + $"world units, signed distance {_lastInsideSigned:F2} wu "
                + $"(slabs: Y {_lastInsideMarginY:F2}, XZ {_lastInsideMarginXZ:F2}); "
                + $"deciding term {term}. Crest {_live.BoardCrestWU:F2} wu, bars enter "
                + $"<= {-WallFadeTuning.InsideEnterDepthFraction * _live.BoardCrestWU:F2} / leave "
                + $">= {WallFadeTuning.InsideExitDepthFraction * _live.BoardCrestWU:F2} wu (live "
                + $"[WallFade] InsideEnterDepthFraction "
                + $"{WallFadeTuning.InsideEnterDepthFraction:F2} / InsideExitDepthFraction "
                + $"{WallFadeTuning.InsideExitDepthFraction:F2}). In real metres at rig "
                + $"scale {rigScale:F2} wu per metre: {metres}. "
                + "THIS VERDICT STILL GATES NOTHING BY ITSELF — two builds were spent proving a "
                + "scene-wide switch on THIS term cannot express a per-wall fact (see the record "
                + "on WallSegmentFade.Inside.cs). What gates is the strictly narrower WALK-IN "
                + $"latch: {(_walkInside ? "ENGAGED" : "off")} (edges this session {_walkEdges}"
                + $"{(_walkInside ? $", holding {_walkHeld} wall(s) solid on this pass, "
                    + $"{_walkHeldHidden} of them still faded or mid-ramp" : string.Empty)}). "
                + "Its four terms right now — "
                + $"(a) INSIDE {(_insideBoard ? "PASS" : "FAIL")}; "
                + $"(b) CREST {(_walkTermCrest ? "PASS" : "FAIL")} at "
                + $"{(_walkCrestMetres >= 0f ? $"{_walkCrestMetres:F2}" : "n/a")} m against the "
                + (_walkCrestDisabled
                    ? "bar SWITCHED OFF ([WallFade] WalkInMinCrestMetres is 0, so this term "
                      + "always passes and the mode can fire on a tabletop diorama — that is the "
                      + "ModBuild 251 behaviour, chosen here on purpose)"
                    : $"{_walkCrestBar:F2} m bar ([WallFade] WalkInMinCrestMetres, release below "
                      + $"{_walkCrestBar * WallFadeTuning.WalkInCrestReleaseFraction:F2} m = "
                      + "WalkInCrestReleaseFraction "
                      + $"{WallFadeTuning.WalkInCrestReleaseFraction:F2}) — THE term that "
                      + "separates standing in a room from leaning over a 61 cm diorama")
                + "; "
                + $"(c) HEIGHT slab {(_walkTermHeight ? "PASS" : "FAIL")} at "
                + $"{_lastInsideMarginY:F2} wu (must be < {_walkHeightNeed:F2} = -[WallFade] "
                + $"WalkInHeadBelowCrestFraction {WallFadeTuning.WalkInHeadBelowCrestFraction:F2}"
                + $" x crest — the FOOTPRINT term alone never counts); "
                + $"(d) SCALE {(_walkTermScale ? "PASS" : "FAIL")} at {rigScale:G4} wu "
                + $"per metre. Dials: [WallFade] WalkInStandDown "
                + $"{(WallFadeTuning.WalkInStandDown ? "ON" : "OFF")}, dwells "
                + $"{WallFadeTuning.WalkInEnterDwellSeconds:F2}s in / "
                + $"{WallFadeTuning.WalkInExitDwellSeconds:F2}s out"
                + (_walkInside
                    ? ". While the latch holds, EVERY wall is held solid without exception and "
                      + "no coverage, split run, peer fade or gate lift can hide one."
                    : $". REFUSED BY {_walkRefusal}. Every wall is therefore decided solely by "
                      + $"its own coverage against the live bars {WallFadeTuning.On:F2}/"
                      + $"{WallFadeTuning.Off:F2} — the PER-WALL line carries that pass's "
                      + "verdicts."));
        }
    }
}
