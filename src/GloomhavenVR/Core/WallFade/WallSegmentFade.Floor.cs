using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// FLOOR NEVER FADES — the driver half of <see cref="WallFloorTile"/>.
///
/// <para><b>THE RULING</b> (user, 2026-09-05, <c>.planning/debug/fehlende_boden_tiles.jpg</c>):
/// <i>"Boden-tiles können per definition NIEMALS die Sicht auf irgendetwas verdecken und dürfen
/// daher niemals ausgeblendet werden."</i> A floor tile occludes nothing, so no lane of this
/// subsystem may ever fade one.</para>
///
/// <para><b>WHY THE REFUSAL IS AT THE WRITE AND NOT ON A LANE.</b> The lane actually caught in the
/// ModBuild-429 log is the UNION RULE (<c>WallSegmentFade.Mounted.cs</c>, <c>UnionFade</c>): the
/// doorway's own floor halves have an owner at fade 0.00 and the union rule hands them a FOREIGN
/// wall's 1.00 —
/// <c>'CR_EXT_Stone_Floor_01_Half_02'[mesh, prop-unit dressing] owner 'ThickDoor : (ee3cc9b8-…)'
/// fade 0.00 → rides 'Wall 2' fade 1.00</c>. But a rule written there would have to be written
/// again in Stacked, Mounted, PropUnit, Body, Hanging, FreeStanding, Blockade and every lane
/// added after this one — this repository has measured that price twice (the standing-prop rule
/// costs eight re-assertions; a gate placed in FRONT of a choke point instead of AT it hid 86 % of
/// its population). So the refusal sits on the four primitives every fade write in this subsystem
/// passes through, and there are exactly four:</para>
/// <list type="number">
/// <item><c>Apply</c>'s <c>seg.Renderers</c> loop (WallSegmentFade.cs) — the wall MPB write.</item>
/// <item><c>DriveProp</c> (WallSegmentFade.Mounted.cs) — every dressing MPB write, and the
/// native/masonry ramp <c>DriveNativeProp</c> is reached only from it.</item>
/// <item><c>HideByEnable</c> (WallSegmentFade.HoldQuery.cs) — the ONLY
/// <c>Renderer.enabled = false</c> in the subsystem, verified by grep.</item>
/// <item><c>EnsureDissolveChannel</c> (WallSegmentFade.Dissolve.cs) — the material SWAP,
/// which is a fade write in disguise.</item>
/// </list>
///
/// <para><b>RESTITUTION, not just refusal.</b> A guard alone leaves whatever is already faded
/// faded. <c>PurgeFloorRenderers</c> is modelled line for line on
/// <c>PurgeFigureRenderers</c> (round 7, the Lights-rule severity): it sweeps the shared ledgers
/// and hands every floor renderer back to solid THROUGH the same ledger that wrote it, so a
/// renderer the game itself disabled is still never switched on by us.</para>
///
/// <para><b>"NEVER FADES" MEANS "ALWAYS VISIBLE", NOT "NEVER WRITTEN"</b> (user, 2026-09-05,
/// <c>boden_tiles_ausgeblendet.jpg</c>: <i>"Die Bodentiles faden nicht mehr mit den Wänden sehr
/// gut. Allerdings sind jetzt zwei Boden tiles unter den ersten Türen dauerhaft ausgeblendet"</i>).
/// ModBuild 430 got the refusal right and the restitution wrong: it restored UNCONDITIONALLY and
/// therefore had to be limited to ONE RESTITUTION PER RENDERER PER SCENE, which spends the one shot
/// on the first commit that sees a tile — healthy or not — and leaves nothing for the tile that
/// goes dark afterwards. Its own log says so: <c>SWEPT 16 … REFUSED 13 … HANDED BACK to solid
/// 13</c> once, then <c>HANDED BACK to solid 0</c> for the rest of the session, while SHOW EDGE
/// reads <c>'CR_EXT_Stone_Floor_01_Half_02' [cutoff] at fade 0.41: its prop unit 'HexDoor(Clone)'
/// returned IN PIECES</c> and the same for <c>'CR_TC_Floor_Basic_Half_02'</c> — the two tiles in
/// the photograph, both of them ALSO named on the FLOOR NEVER FADES line as protected floor tiles.
/// Refused and invisible at once.</para>
///
/// <para>So the latch is now THE PICTURE (<see cref="FadeDriver.FloorChannelStuckIn"/> over
/// <see cref="WallFloorTile.ChannelOf"/>): a rescue fires once per renderer per FADED OBSERVATION,
/// never once per scene. A floor renderer that is already at its authored appearance costs one
/// <c>HasPropertyBlock()</c> call and NO write, so a steady picture feeds the ownership-churn
/// tripwire nothing at all — which is a stronger guarantee than the one-shot's, because it does
/// not rest on a claim about what the four write primitives can and cannot let through. A REPEAT
/// rescue means one of them did let something through, and the census line's RE-RESCUED field
/// says so out loud instead of a latch hiding it.</para>
///
/// <para><b>MULTIPLAYER:</b> local presentation only, like the whole wall system. No wire field,
/// no per-sub-feature switch — the whole-board <c>[Net] RemoteBoards</c> switch is unchanged.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // ---- the memo ------------------------------------------------------------------------

        /// <summary>Renderer <c>GetInstanceID</c> → floor verdict. The fade write path runs every
        /// frame over hundreds of renderers and this test reads an AABB and a material list, so
        /// the answer is derived once and read thereafter — the instance-id memo idiom
        /// <c>_bankedFacts</c> already uses.
        ///
        /// <para>CLEARED AT THE TOP OF <see cref="PurgeFloorRenderers"/> — i.e. once per commit,
        /// which is also the pass that re-derives it — AND ON A SCENE LOAD.
        /// Per-commit rather than per-session on purpose: the verdict is measured in world units
        /// against a room floor plane, so it is only as stable as the board's pose and scale, and
        /// a memo that outlived a re-anchored board would be a stale measurement wearing a cached
        /// answer's clothes. Re-deriving a few hundred verdicts every two seconds is the same cost
        /// class as the standing-prop memos beside it.</para></summary>
        private readonly Dictionary<int, byte> _floorMemo = new(512);

        private const byte FloorMemoNotFloor = 1;
        private const byte FloorMemoIsFloor = 2;

        /// <summary>Distinct renderers refused this census window. A SET and not a counter: the
        /// guard is asked many times per frame for the same renderer and "how many renderers the
        /// floor test refused" must not become "how many times a write was attempted".</summary>
        private readonly HashSet<int> _floorRefusedIds = new(128);

        /// <summary>How many times the guard was ASKED this window, memo hits included — i.e. how
        /// many fade writes were attempted at all. <c>examined 4213, refused 0</c> and
        /// <c>examined 0</c> are completely different reports, and neither of them is "the rule
        /// is not wired": that is <see cref="_floorSweeps"/>, which is the unconditional one. A
        /// rule that had fired zero times for six builds has happened in this project before, and
        /// the three fields exist so this one can never be read that way by mistake.</summary>
        private int _floorExamined;

        /// <summary>How many verdicts were DERIVED this window (memo misses).</summary>
        private int _floorClassified;

        /// <summary>How many restitution sweeps ran this window. THE UNCONDITIONAL liveness field:
        /// <see cref="_floorExamined"/> can legitimately be 0 when nothing anywhere is fading (no
        /// write is attempted, so the guard is never asked), and that is a different thing again
        /// from the rule not being wired at all. This counter moves once per commit whatever the
        /// picture is doing, so <c>sweeps 0</c> and only <c>sweeps 0</c> means "this rule did not
        /// run".</summary>
        private int _floorSweeps;

        /// <summary>How many renderers were handed back to solid this window by
        /// <see cref="PurgeFloorRenderers"/>.</summary>
        private int _floorHandedBack;

        /// <summary>How many renderers the FADE WRITE census's write loop found in a fading
        /// segment's membership lists that are floor tiles and therefore carry no fade. Reset by
        /// that census, read by it — see <c>NoteFadeWrite</c>.</summary>
        private int _floorHeldInFadingSegments;

        /// <summary>Session totals, so a window that refused nothing can still show the rule has
        /// been doing work.</summary>
        private int _floorSessionRefused;
        private int _floorSessionHandedBack;

        /// <summary>The first few refusals of the window, each with the term that identified it.
        /// Capped, and the cap is stated on the line — a truncated list is not absence.</summary>
        private readonly List<string> _floorNames = new();

        private const int FloorNameCap = 6;

        // ---- "is any floor renderer invisible right now?" -------------------------------------
        //
        // The fields behind the second half of the FLOOR NEVER FADES line. ModBuild 430's line
        // could say how many writes it REFUSED and could not say whether anything was actually
        // on screen, which is the question the user's report is about.

        /// <summary>Scratch for reading a property block BACK off a renderer. One instance for the
        /// life of the driver — <c>GetPropertyBlock</c> fills it in place — so the audit allocates
        /// nothing. Same idiom as <c>_showEdgeMpb</c> beside it.</summary>
        private MaterialPropertyBlock? _floorAuditMpb;

        /// <summary>Floor renderers whose APPEARANCE was audited this window.</summary>
        private int _floorAudited;

        /// <summary>Of those, how many were found not at their authored appearance.</summary>
        private int _floorNotAsAuthored;

        /// <summary>How many of this window's not-as-authored findings were for a renderer that
        /// had already been rescued earlier IN THIS SCENE. The only shape that could become churn,
        /// and therefore a field rather than a latch — see <see cref="_floorRescues"/>.</summary>
        private int _floorReRescued;

        private int _floorWorstRescueCount;
        private string? _floorWorstRescueName;

        /// <summary>Session totals for the audit half, so a quiet window still shows the work.</summary>
        private int _floorSessionAudited;
        private int _floorSessionNotAsAuthored;

        /// <summary>The first few not-as-authored renderers of the window, each with the CHANNEL
        /// it was stuck in and the number that said so. Same cap as <see cref="_floorNames"/>.</summary>
        private readonly List<string> _floorStuckNames = new();

        /// <summary>Cost of the last sweep, and the worst since the session began, in
        /// <see cref="Stopwatch"/> ticks. This project's standing rule is that a claimed
        /// performance property is measured and printed, never asserted.</summary>
        private long _floorSweepTicks;
        private long _floorSweepWorstTicks;

        /// <summary>Ticks → microseconds, done once per printed line.</summary>
        private static double Micros(long ticks) => ticks * 1e6d / Stopwatch.Frequency;

        /// <summary>Census cadence, matching <c>FadeCensusIntervalSeconds</c>.</summary>
        private const float FloorCensusIntervalSeconds = 2f;

        /// <summary>Longest silence the line may keep. Past this it prints whether or not anything
        /// changed, so "the rule refused nothing" and "the rule never ran" are always
        /// distinguishable in a log. See <see cref="_floorExamined"/>.</summary>
        private const float FloorCensusHeartbeatSeconds = 30f;

        private float _nextFloorCensus;
        private float _nextFloorHeartbeat;
        private int _floorCensusSig = int.MinValue + 1;

        // ---- the test ------------------------------------------------------------------------

        /// <summary>
        /// MUST THIS RENDERER NEVER BE FADED BECAUSE IT IS A FLOOR TILE? Consulted by every write
        /// primitive listed in the file header.
        ///
        /// <para>Two terms, and both must hold. GEOMETRY (<see cref="WallFloorTile.Judge"/>): a
        /// flat plate lying at the room's floor plane. IDENTITY: the mesh is NOT on the game's own
        /// wall-fade shader family — a mesh the game put on that family is a mesh the game itself
        /// calls wall, and that is the term that separates a floor hex from a wall's base trim
        /// without a name substring anywhere.</para>
        ///
        /// <para>Undecidable cases return false and are NOT memoized: before any room is anchored
        /// there is no floor plane to measure against, and caching "not floor" then would freeze
        /// the wrong answer for the rest of the rescan cycle.</para>
        /// </summary>
        private bool FloorNeverFades(Renderer? r)
        {
            _floorExamined++;
            if (r == null)
                return false;
            int id = r.GetInstanceID();
            // PERF S6 (2026-09-05) — THE MEMO IS PROBED BEFORE IsModObject, AND THE ORDER IS
            // OUTPUT-IDENTICAL RATHER THAN MERELY SAFE. The only two writes to _floorMemo are
            // below, and both sit past the IsModObject guard, so NO mod renderer is ever a key
            // in it: a memo hit therefore cannot be answering for a renderer the guard would
            // have rejected, and a memo miss falls through to that guard unchanged. What the
            // swap removes is the guard's `r.name` — an interop call that ALLOCATES a managed
            // string — from every memo hit.
            //
            // WHY IT IS WORTH A REORDER. This is write primitive 1 of 4 and it is on the
            // PER-FRAME path: Apply's renderer loop and DriveProp both reach it for every piece
            // of every fading segment, every frame. The ModBuild 435 log prices the population
            // itself — "its floor test EXAMINED 77505 write candidate(s) (1094 of them derived
            // fresh, the rest read off the per-renderer memo)" — i.e. 98.6 % of the calls were
            // memo hits, and every one of them allocated a string to reach the memo.
            if (_floorMemo.TryGetValue(id, out byte cached))
            {
                if (cached != FloorMemoIsFloor)
                    return false;
                if (_floorRefusedIds.Add(id))
                    _floorSessionRefused++;
                return true;
            }
            // The guard itself, verbatim and in the same place in the ANSWER — only the memo
            // hit now reaches its verdict without paying for it. A mod object still never
            // enters the memo, which is the property the swap above rests on.
            if (IsModObject(r))
                return false;
            Bounds b = r.bounds;
            if (!NearestAnchoredFloorY(b.min.y, out float floorY))
                return false; // no anchored room yet — undecidable, and deliberately not memoized
            Vector3 s = b.size;
            var plate = new WallFloorTile.Plate(b.min.y, b.max.y, s.x, s.z);
            WallFloorTile.Verdict v = WallFloorTile.Judge(plate, floorY);
            if (v == WallFloorTile.Verdict.FloorTile && RendererIsGameWallMasonry(r))
                v = WallFloorTile.Verdict.GameCallsItWall;
            _floorClassified++;
            bool floor = v == WallFloorTile.Verdict.FloorTile;
            _floorMemo[id] = floor ? FloorMemoIsFloor : FloorMemoNotFloor;
            if (!floor)
                return false;
            if (_floorRefusedIds.Add(id))
                _floorSessionRefused++;
            // The sentence is built ONLY for the rows the capped line will print — on net472 every
            // interpolated string is a string.Format(string, object[]) and this sits on the
            // per-frame write path. See WallFloorTile.Judge's header.
            if (_floorNames.Count < FloorNameCap)
                _floorNames.Add("'" + r.name + "' " + WallFloorTile.Describe(plate, floorY, v));
            return true;
        }

        /// <summary>Has the GAME put this mesh on its own wall-fade shader family?
        ///
        /// <para>Read off the AUTHORED materials whenever this driver has swapped copies onto the
        /// renderer. Reading our own copy back would make every piece we have ever driven answer
        /// "yes" — a claim measuring a value the mod itself writes, which is a failure mode this
        /// project has an entry for.</para></summary>
        private bool RendererIsGameWallMasonry(Renderer r)
        {
            if (_mountedTouched.TryGetValue(r, out MountedProp? p)
                && p.SwapCopies != null && p.SwapOriginals != null)
                return AnyWallFadeShader(p.SwapOriginals);
            return r is MeshRenderer mr && mr != null && RendererUsesWallFade(mr);
        }

        /// <summary>The <c>RendererUsesWallFade</c> loop over an explicit material array, sharing
        /// the same per-<c>Shader</c> verdict cache so the two can never disagree.</summary>
        private bool AnyWallFadeShader(Material[] mats)
        {
            foreach (Material m in mats)
            {
                if (m == null)
                    continue;
                Shader sh = m.shader;
                if (sh == null)
                    continue;
                if (!_shaderVerdict.TryGetValue(sh, out bool capable))
                {
                    capable = IsWallFadeShaderName(sh.name);
                    _shaderVerdict[sh] = capable;
                }
                if (capable)
                    return true;
            }
            return false;
        }

        /// <summary>Drop the per-renderer verdicts. Called from <see cref="PurgeFloorRenderers"/>
        /// (once per commit) — see <see cref="_floorMemo"/> for why the short lifetime is
        /// deliberate.</summary>
        private void ResetFloorMemo() => _floorMemo.Clear();

        /// <summary>Everything this rule remembers about a SCENE. Called on a scene load: the
        /// verdicts were measured in the old scene's world units and the restitution ledger names
        /// renderers that died with it.</summary>
        private void ResetFloorSceneState()
        {
            _floorMemo.Clear();
            _floorRescues.Clear();
        }

        // ---- restitution ---------------------------------------------------------------------

        private readonly List<MountedProp> _floorPurgeScratch = new();
        private readonly List<Renderer> _floorShowScratch = new();

        /// <summary>
        /// How many times each floor renderer has been rescued THIS SCENE, keyed on
        /// <c>GetInstanceID</c>. REPORTING ONLY — it gates nothing.
        ///
        /// <para><b>WHAT USED TO BE HERE, AND WHY IT WAS THE BUG.</b> ModBuild 430 kept a
        /// <c>_floorRestituted</c> SET and allowed ONE RESTITUTION PER RENDERER PER SCENE, because
        /// the sweep restored every floor renderer it found in the ledgers UNCONDITIONALLY and a
        /// per-commit unconditional restore would have fed the ownership-churn tripwire ~30
        /// "released" transitions a minute — the mod becoming the churn it measures. The
        /// one-shot's justification was that "after the sweep nothing can put this renderer back
        /// into a faded state, because all four write primitives refuse it".</para>
        ///
        /// <para>That justification was wrong in the only way that matters, and the user's
        /// photograph is the falsification (2026-09-05, <c>boden_tiles_ausgeblendet.jpg</c>:
        /// <i>"zwei Boden tiles unter den ersten Türen dauerhaft ausgeblendet"</i>). Because the
        /// restore was unconditional, THE ONE SHOT WAS SPENT ON THE FIRST COMMIT THAT SAW THE
        /// TILE — whether or not it was faded — and it is spent on a healthy piece almost every
        /// time, since the applier enters a floor tile into <c>_mountedTouched</c> one line before
        /// the refused <c>DriveProp</c>. Any tile that reached a faded state AFTERWARDS was then
        /// refused forever with no restitution left, i.e. permanently invisible. The ModBuild-430
        /// log shows exactly that: <c>SWEPT 16 … REFUSED 13 … HANDED BACK to solid 13</c> once,
        /// then <c>REFUSED 19 … HANDED BACK to solid 0</c> for the rest of the session, while
        /// SHOW EDGE names <c>'CR_EXT_Stone_Floor_01_Half_02'</c> and
        /// <c>'CR_TC_Floor_Basic_Half_02'</c> of prop unit <c>'HexDoor(Clone)'</c> at
        /// <c>[cutoff] fade 0.41</c>.</para>
        ///
        /// <para><b>THE LATCH IS NOW THE PICTURE</b> (<see cref="FloorChannelStuckIn"/>): a rescue
        /// fires once per renderer per FADED OBSERVATION rather than once per scene. That satisfies
        /// both constraints at the same time, and the churn argument is stronger than the one the
        /// one-shot gave, because it does not rest on a claim about the guards:</para>
        /// <list type="number">
        /// <item>A steady picture writes NOTHING. The sweep restores only a renderer whose own
        /// property block, materials or enable bit say it is not as authored, so re-enrolment in
        /// a wall's membership lists — the thing the one-shot was really defending against —
        /// costs zero <c>RestoreProp</c> calls and therefore zero "released" transitions.</item>
        /// <item>A rescue is IDEMPOTENT against its own audit: <c>RestoreProp</c> puts the
        /// authored materials back, clears the block and restores the enable bit, so the very next
        /// audit of that renderer reads <see cref="WallFloorTile.Channel.AsAuthored"/> and the
        /// commit after it does nothing.</item>
        /// <item>So a REPEAT rescue can only mean a write primitive really did re-fade the tile —
        /// a genuine gap, which is a thing to see and not a thing to suppress. It is counted here
        /// and printed on the census line rather than silently swallowed by a latch.</item>
        /// </list>
        /// <para>Cleared with the scene, like the memo beside it.</para>
        /// </summary>
        private readonly Dictionary<int, int> _floorRescues = new(64);

        /// <summary>
        /// FLOOR RESTITUTION — hand back to solid every floor renderer this driver is currently
        /// holding faded, through the same ledgers that wrote it.
        ///
        /// <para>Modelled on <c>PurgeFigureRenderers</c>, including its hard-won split: the
        /// RESTORE LOOPS ARE SEPARATE FROM THE MEASUREMENT LOOPS AND MUST STAY THAT WAY. A pass
        /// that folded the restore back into the scan would be a fix living inside the instrument
        /// meant to test it, and this repository has already nearly latched the wall fade off
        /// forever by deleting one of those.</para>
        ///
        /// <para>Two ledgers, because a renderer can be in either. <c>_mountedTouched</c> holds
        /// every piece with a property block, a material swap or a particle ramp; <c>_hidByEnable</c>
        /// additionally holds renderers switched off with no piece of their own. The prop sweep
        /// runs first: <c>RestoreProp</c> already calls <c>ShowIfWeHid</c>, so nothing is counted
        /// twice.</para>
        /// </summary>
        private void PurgeFloorRenderers()
        {
            // The memo's whole window is one commit, and this is the first pass of it — so the
            // verdicts every write of the next two seconds reads are re-derived HERE, against a
            // board pose and a room registry that are current. See _floorMemo.
            ResetFloorMemo();
            _floorSweeps++; // liveness, unconditionally — see the field
            long t0 = Stopwatch.GetTimestamp();
            if (_mountedTouched.Count > 0)
            {
                _floorPurgeScratch.Clear();
                foreach (MountedProp p in _mountedTouched.Values)
                {
                    Renderer? pr = p.Renderer;
                    if (pr == null || !FloorNeverFades(pr))
                        continue;
                    // THE LATCH IS THE PICTURE, not a per-scene one-shot — see _floorRescues for
                    // the whole argument and for what the one-shot cost. A tile that is already
                    // at its authored appearance costs one HasPropertyBlock() call and no write.
                    _floorAudited++;
                    _floorSessionAudited++;
                    WallFloorTile.Channel ch = FloorChannelStuckIn(pr, p, out WallFloorTile.Appearance look);
                    if (ch == WallFloorTile.Channel.AsAuthored)
                        continue;
                    NoteFloorNotAsAuthored(pr, look, ch);
                    _floorPurgeScratch.Add(p);
                }
                // ***THE RESTITUTION ITSELF. NOT A DIAGNOSTIC.*** See the summary above.
                foreach (MountedProp p in _floorPurgeScratch)
                {
                    RestoreProp(p, null, "floor tile — a floor tile occludes nothing and never fades");
                    _floorHandedBack++;
                    _floorSessionHandedBack++;
                }
                _floorPurgeScratch.Clear();
            }
            if (_hidByEnable.Count > 0)
            {
                _floorShowScratch.Clear();
                foreach (Renderer r in _hidByEnable)
                {
                    if (r != null && FloorNeverFades(r))
                        _floorShowScratch.Add(r);
                }
                foreach (Renderer r in _floorShowScratch)
                {
                    if (r == null)
                        continue;
                    // The audit is asked here too, so the census speaks one vocabulary — but note
                    // it can legitimately answer AS AUTHORED for a row of this ledger: a STALE row
                    // (we hid it, something else switched it back on) is a renderer that is
                    // already drawing, and counting that as a rescue would inflate the very field
                    // this round exists to make trustworthy. The ShowIfWeHid below still runs, so
                    // the stale row is dropped from the ledger either way.
                    _floorAudited++;
                    _floorSessionAudited++;
                    WallFloorTile.Channel ch = FloorChannelStuckIn(r, null, out WallFloorTile.Appearance look);
                    if (ch != WallFloorTile.Channel.AsAuthored)
                        NoteFloorNotAsAuthored(r, look, ch);
                    // Through the ledger, never around it: a renderer the GAME switched off stays off.
                    if (ShowIfWeHid(r))
                    {
                        _floorHandedBack++;
                        _floorSessionHandedBack++;
                    }
                    r.SetPropertyBlock(null);
                }
                _floorShowScratch.Clear();
            }
            long ticks = Stopwatch.GetTimestamp() - t0;
            _floorSweepTicks = ticks;
            if (ticks > _floorSweepWorstTicks)
                _floorSweepWorstTicks = ticks;
        }

        /// <summary>
        /// IS THIS FLOOR RENDERER VISIBLE AS AUTHORED RIGHT NOW, AND IF NOT, IN WHICH CHANNEL IS
        /// IT STUCK? Every term is read off the RENDERER — its enable ledger row, its materials
        /// and the property block that will be sampled this frame — never off the state machine
        /// that decided it. That is the same rule <c>AuditShowEdge</c> is written to, and it is
        /// what makes this a restitution rather than a claim: ModBuild 430's sweep asked only
        /// "have I already handed this one back?", which is a question about the mod's bookkeeping
        /// and agreed with a permanently invisible tile.
        ///
        /// <para>COST, and it is the reason the terms are in this order. The common case by far is
        /// a floor tile that is perfectly fine: a set lookup, a null test and ONE
        /// <c>HasPropertyBlock()</c> — no block read, no allocation. The block is only pulled back
        /// for a piece that HAS one AND has a channel of ours to compare it against, and
        /// <c>_floorAuditMpb</c> is reused for the life of the driver so even that path allocates
        /// nothing. The sweep runs once per commit (~2 s) inside the existing
        /// <c>CommitPhase.Figures</c> bucket, never on the per-frame write path, and its measured
        /// cost is printed on the FLOOR NEVER FADES line rather than asserted here.</para>
        /// </summary>
        private WallFloorTile.Channel FloorChannelStuckIn(
            Renderer r, MountedProp? p, out WallFloorTile.Appearance look)
        {
            // BOTH terms, and in this order. The ledger alone cannot say the renderer is dark —
            // a row left over from a hide something else has already undone names a renderer that
            // is drawing perfectly. `!r.enabled` alone cannot say WE turned it off, and switching
            // on what the game switched off is the stale-restore defect the ledger exists to
            // prevent. Only the conjunction means "invisible, and ours to fix".
            bool disabled = _hidByEnable.Contains(r) && !r.enabled;
            bool swapped = p != null && p.SwapCopies != null;
            bool nativeRamp = p != null && p.NativeFade;
            // Guarded: a renderer torn down mid-commit answers false and is treated as authored,
            // which is the only safe direction — there is nothing left to restore on a dead one.
            bool hasBlock = !disabled && !swapped && r.HasPropertyBlock();
            float authoredAlpha = float.NaN, blockAlpha = float.NaN;
            float authoredCutoff = float.NaN, blockCutoff = float.NaN;
            float authoredDissolve = float.NaN, blockDissolve = float.NaN;
            if (hasBlock && !nativeRamp && p != null
                && (p.ColorId >= 0 || p.CutoffId >= 0 || p.DissolveControlId >= 0))
            {
                _floorAuditMpb ??= new MaterialPropertyBlock();
                _floorAuditMpb.Clear();
                r.GetPropertyBlock(_floorAuditMpb);
                // Only the channels this piece actually HAS are compared. An id the block never
                // set reads back as 0 through GetFloat, and 0 is a plausible fade value — see
                // WallFloorTile.Appearance for why an absent channel must stay NaN.
                if (p.ColorId >= 0)
                {
                    authoredAlpha = p.BaseColor.a;
                    blockAlpha = _floorAuditMpb.GetColor(p.ColorId).a;
                }
                if (p.CutoffId >= 0)
                {
                    authoredCutoff = p.BaseCutoff;
                    blockCutoff = _floorAuditMpb.GetFloat(p.CutoffId);
                }
                if (p.DissolveControlId >= 0)
                {
                    authoredDissolve = p.BaseDissolveControl;
                    blockDissolve = _floorAuditMpb.GetFloat(p.DissolveControlId);
                }
            }
            look = new WallFloorTile.Appearance(
                disabled, swapped, hasBlock, nativeRamp,
                authoredAlpha, blockAlpha, authoredCutoff, blockCutoff,
                authoredDissolve, blockDissolve);
            return WallFloorTile.ChannelOf(look);
        }

        /// <summary>Book one floor renderer found NOT as authored, and name the first few for the
        /// census line. The sentence is built only under the cap — on net472 every interpolated
        /// string is a <c>string.Format(string, object[])</c>, and a truncated list is not absence,
        /// so the cap is printed beside it.</summary>
        private void NoteFloorNotAsAuthored(
            Renderer r, in WallFloorTile.Appearance look, WallFloorTile.Channel ch)
        {
            _floorNotAsAuthored++;
            _floorSessionNotAsAuthored++;
            int id = r.GetInstanceID();
            _floorRescues.TryGetValue(id, out int before);
            _floorRescues[id] = before + 1;
            if (before > 0)
            {
                _floorReRescued++;
                if (before + 1 > _floorWorstRescueCount)
                {
                    _floorWorstRescueCount = before + 1;
                    _floorWorstRescueName = r.name;
                }
            }
            if (_floorStuckNames.Count < FloorNameCap)
                _floorStuckNames.Add("'" + r.name + "' " + WallFloorTile.DescribeChannel(look, ch));
        }

        // ---- the census ----------------------------------------------------------------------

        /// <summary>
        /// FLOOR NEVER FADES — one line per window, and its liveness is UNCONDITIONAL: it prints
        /// on any change and, failing that, every <see cref="FloorCensusHeartbeatSeconds"/>
        /// regardless, so a session in which the rule refused nothing is always distinguishable
        /// from one in which it never ran.
        /// </summary>
        private void LogFloorGuardCensus(float now)
        {
            if (now < _nextFloorCensus)
                return;
            _nextFloorCensus = now + FloorCensusIntervalSeconds;
            int sig = unchecked(_floorRefusedIds.Count * 397 + _floorHandedBack * 31
                                + (_floorHeldInFadingSegments << 8)
                                + (_floorExamined > 0 ? 1 : 0)
                                + (_floorSweeps > 0 ? 2 : 0)
                                // The audit half moves the signature too, or the line would sit
                                // on its 30 s heartbeat while a floor tile was invisible.
                                + _floorNotAsAuthored * 7919 + _floorReRescued * 104729
                                + (_floorAudited > 0 ? 4 : 0));
            bool heartbeat = now >= _nextFloorHeartbeat;
            if (sig == _floorCensusSig && !heartbeat)
                return;
            _floorCensusSig = sig;
            _nextFloorHeartbeat = now + FloorCensusHeartbeatSeconds;

            var names = new System.Text.StringBuilder();
            foreach (string n in _floorNames)
            {
                if (names.Length > 0)
                    names.Append("; ");
                names.Append(n);
            }
            if (names.Length == 0)
                names.Append("none");

            var stuck = new System.Text.StringBuilder();
            foreach (string n in _floorStuckNames)
            {
                if (stuck.Length > 0)
                    stuck.Append("; ");
                stuck.Append(n);
            }
            if (stuck.Length == 0)
                stuck.Append("none — every floor renderer audited this window is drawing as authored");

            // HW-VERIFY
            VRLog.Note(Name,
                $"FLOOR NEVER FADES: this window the rule SWEPT {_floorSweeps} time(s) and its "
                + $"floor test EXAMINED {_floorExamined} write "
                + $"candidate(s) ({_floorClassified} of them derived fresh, the rest read off the "
                + $"per-renderer memo), REFUSED {_floorRefusedIds.Count} distinct renderer(s) at "
                + $"the write, and HANDED BACK to solid {_floorHandedBack}; "
                + $"{_floorHeldInFadingSegments} renderer(s) sit in a FADING segment's membership "
                + $"lists and carry no fade because of this rule (which is why the FADE WRITE and "
                + $"SOLID BLOCKER lines must not read them as a tear). Session totals: "
                + $"{_floorSessionRefused} refused, {_floorSessionHandedBack} handed back. "
                + $"THE RULE (user 2026-09-05, fehlende_boden_tiles.jpg): 'Boden-tiles können per "
                + $"definition NIEMALS die Sicht auf irgendetwas verdecken und dürfen daher "
                + $"niemals ausgeblendet werden' — a floor tile occludes nothing, so no lane may "
                + $"fade one, and the refusal sits on all four write primitives (the wall MPB "
                + $"loop, DriveProp, HideByEnable, EnsureDissolveChannel) rather than on any lane. "
                + $"SWEPT IS THE UNCONDITIONAL LIVENESS FIELD — it moves once per commit whatever "
                + $"the picture is doing, so 'swept 0' and only that means the rule did not run; "
                + $"'examined 0' beside a non-zero sweep count means no lane tried to fade "
                + $"anything this window, which is a third thing again and not a failure. "
                + $"THE TEST IS GEOMETRY "
                + $"PLUS ONE GAME-OWNED TERM — a flat plate at the room floor plane (top ≤ "
                + $"{WallFloorTile.FloorBandWU:0.00} wu over it, h ≤ "
                + $"{WallFloorTile.PlateHeightMaxWU:0.00} wu and ≤ "
                + $"{WallFloorTile.PlateFlatnessMax:0.00}× its own short axis, short axis ≥ "
                + $"{WallFloorTile.PlateMinSpanWU:0.00} wu, aspect ≤ "
                + $"{WallFloorTile.PlateAspectMax:0.0}, widest ≤ "
                + $"{WallFloorTile.PlateMaxSpanWU:0.0} wu) that is NOT on the game's own wall-fade "
                + $"shader family; the game's occlusion model was read first and does not separate "
                + $"floor from occluder (TilesOcclusionVolume.Renderers are invisible proxy boxes "
                + $"spanning the whole room column). Named {_floorNames.Count} of "
                + $"{_floorRefusedIds.Count}, dropped "
                + $"{(_floorRefusedIds.Count > _floorNames.Count ? _floorRefusedIds.Count - _floorNames.Count : 0)}"
                + $" (cap {FloorNameCap}), each with the term that identified it: {names}"
                // ---- appended ModBuild 431: IS ANY FLOOR RENDERER INVISIBLE RIGHT NOW? ----
                // Nothing above is reworded; the fields above answer "what did the rule refuse",
                // which is a different question and was the one that could not see this defect.
                + $" | AND IS ANY FLOOR RENDERER INVISIBLE RIGHT NOW? The sweep AUDITED "
                + $"{_floorAudited} floor renderer(s) against their AUTHORED appearance this "
                + $"window, found {_floorNotAsAuthored} NOT AS AUTHORED and RESCUED "
                + $"{_floorHandedBack} (the same number as HANDED BACK above — one rescue is one "
                + $"hand-back). Session totals: {_floorSessionAudited} audited, "
                + $"{_floorSessionNotAsAuthored} not as authored. RE-RESCUED {_floorReRescued} "
                + $"this window — renderer(s) found faded AGAIN after an earlier rescue in this "
                + $"scene (worst so far '{_floorWorstRescueName ?? "none"}' × "
                + $"{_floorWorstRescueCount}); a non-zero and RISING RE-RESCUED count is the one "
                + $"shape that could become churn and the one shape that means a write primitive "
                + $"still lets a floor tile through, so it is a field and not a latch. "
                + $"REFUSAL IS NOT RESTORATION (user 2026-09-05, boden_tiles_ausgeblendet.jpg: "
                + $"'zwei Boden tiles unter den ersten Türen dauerhaft ausgeblendet'). ModBuild "
                + $"430 restituted ONCE PER RENDERER PER SCENE and restored UNCONDITIONALLY, so "
                + $"the one shot was spent on the first commit that saw a tile whether or not it "
                + $"was faded, and any tile faded afterwards was refused forever with no "
                + $"restitution left: its log reads SWEPT 16 REFUSED 13 HANDED BACK 13 once and "
                + $"HANDED BACK 0 for the rest of the session, while SHOW EDGE names "
                + $"'CR_EXT_Stone_Floor_01_Half_02' and 'CR_TC_Floor_Basic_Half_02' of prop unit "
                + $"'HexDoor(Clone)' at [cutoff] fade 0.41. The latch is now the PICTURE — read "
                + $"off the renderer's enable ledger row, its materials and the property block "
                + $"that will be sampled this frame, never off the ledger that decided it — so a "
                + $"rescue fires once per FADED OBSERVATION, a steady picture writes nothing at "
                + $"all, and re-enrolment in a wall's membership lists costs zero RestoreProp "
                + $"calls and therefore zero ownership-churn transitions. NOTE ON SWEPT: it is "
                + $"cumulative for the session, not per window — the census window can close "
                + $"between two commits, so only 'swept 0' means the rule never ran. COST, "
                + $"measured and not asserted: the last sweep took {Micros(_floorSweepTicks):0.0} "
                + $"µs and the worst of the session {Micros(_floorSweepWorstTicks):0.0} µs, over "
                + $"{_floorSweeps} sweep(s) at one per commit (~{FloorCensusIntervalSeconds:0} s) "
                + $"inside the existing CommitPhase.Figures bucket — the audit adds one "
                + $"HasPropertyBlock() call per floor renderer and reads a block back only for a "
                + $"piece that has one AND has a channel of ours to compare it against. Named "
                + $"{_floorStuckNames.Count} of {_floorNotAsAuthored}, dropped "
                + $"{(_floorNotAsAuthored > _floorStuckNames.Count ? _floorNotAsAuthored - _floorStuckNames.Count : 0)}"
                + $" (cap {FloorNameCap}), each with the CHANNEL it was stuck in: {stuck}");

            _floorExamined = 0;
            _floorClassified = 0;
            _floorHandedBack = 0;
            _floorAudited = 0;
            _floorNotAsAuthored = 0;
            _floorReRescued = 0;
            _floorRefusedIds.Clear();
            _floorNames.Clear();
            _floorStuckNames.Clear();
        }
    }
}
