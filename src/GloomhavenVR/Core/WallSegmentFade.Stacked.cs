using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// STACKED WALL SUPERSTRUCTURE — the fort/keep shell class (user report 2026-08-02,
/// keine_ausblendung.png: a multi-story stone keep stands fully solid while the player hovers
/// above it; "man muss von oben senkrecht runterschauen um überhaupt etwas zu sehen").
///
/// WHAT THE KEEP ACTUALLY IS (hardware log, scene 'ProcGen', ModBuild 57): the visible mass of
/// the fort is NOT wall geometry to either discovery source. Its bottom course is ordinary
/// <c>ProceduralWall</c> runs (tracked fine — 'Wall 2/4/6', AABB tops 2.42–2.75 wu), but the
/// stories ABOVE — 'TO_Fort_WallTop02', 'TO_Fort_LowWall_01/_Narrow', 'TO_SB02_WallTop_Narrow',
/// 'polySurface1/2' (rock corbels), AABB bottoms 3.0–3.5 wu — are plain scenario meshes WITHOUT
/// a WallFade-family shader. The wall cache never lists them and the shader-adoption sweep can
/// never match them, so they are invisible to the fade table; the log shows them only as
/// wall-mounted-DRESSING candidates, where the sconce-scale rules rightly reject them
/// ("no wall within reach / outside its span": MountedLinkMaxAboveTopWU 0.6 &lt; their 0.6–1.1 wu
/// rise over the wall top, MountedMaxSpanWU 3.0 &lt; a battlement run's length).
///
/// WHY THE TRACKED WALLS NEVER FADED EITHER (the second stacked cause): the room-coverage
/// metric measures the WALL SEGMENT's AABB against head→floor-sample rays. From any elevated
/// viewpoint (log: headY 5–15 over samples at 0.05) every ray clears a 2.75-wu slab — raw
/// coverage stayed 0.00 for minutes on end. The geometry that actually hides the floor is the
/// 3–7 wu superstructure, which was part of NO segment's AABB. The metric was never wrong; it
/// was starved of the occluder. So the fix is occluder DISCOVERY, not a new trigger:
///
/// THE RULE (config <c>[WallFade] StackedShellFade</c>, shipped ON): a plain mesh that
/// CONTINUES a tracked wall upward — horizontal gap to the wall AABB ≤
/// <see cref="FadeDriver.StackLinkMaxXZ"/> and its AABB bottom within
/// [wall top − <see cref="FadeDriver.StackMaxOverlapDownWU"/>,
///  wall top + <see cref="FadeDriver.StackMaxRiseWU"/>] — is adopted as a STACKED SHELL PIECE
/// of that wall (nearest wall wins, one owner per renderer). Adoption iterates to a fixpoint
/// (<see cref="FadeDriver.StackMaxRounds"/> rounds) so a battlement standing on a low-wall
/// standing on the wall chains through the whole column. Each adopted piece:
/// <list type="bullet">
/// <item>EXTENDS the segment's occlusion AABB (<c>Segment.Bounds</c>) — from then on the
///   existing room-coverage metric, EMA, Schmitt trigger and dwell rules see the full-height
///   shell and fire exactly like they do for any tall wall. No new trigger math.</item>
/// <item>RIDES the wall's fade through the established mounted-prop delivery
///   (<see cref="FadeDriver.DriveProp"/>/<see cref="FadeDriver.RestoreProp"/>: cutoff or alpha
///   ramp where the material offers one — the log already proves these meshes classify as
///   [mesh→cutoff] — and the guaranteed renderer-disable at the end of the wall's dissolve,
///   restored bit-for-bit on unfade). Same 0→1 <c>seg.Fade</c> as the wall's own cutoff sweep,
///   so shell and wall dissolve together; the sconce-scale torch dressing hanging ON the shell
///   is then caught by the ordinary mounted pass, because that pass runs later against the
///   extended AABB.</item>
/// </list>
///
/// RULES INHERITED WHOLESALE (nothing re-implemented, the piece just joins the segment):
/// Schmitt trigger + EMA + perspective-anchored dwells (the piece has no decision of its own),
/// DOORWAY exemption (doorway segments never stack — the keep's gate face stays solid, user
/// ruling 2026-08-02), fail-safe solid (only segments with a tile-anchored room grid stack),
/// Lights NEVER touched (delivery writes renderers/MPBs only), everything restored on unfade /
/// segment death / toggle-off / teardown via the shared mounted ledger + orphan guard.
///
/// GUARDS: pieces must be airborne over their room's floor plane (ground band never fades —
/// same 1 wu bar as everywhere), actors/tile logic/UI are excluded, and a piece whose adoption
/// would make the wall's extended AABB XZ-contain ≥ <see cref="FadeDriver.EngulfSampleFraction"/>
/// of its own room's floor grid is REJECTED (the engulf lesson: a ring-shaped shell around the
/// room would read 100% coverage forever). Scenes without stacked shells adopt nothing and
/// keep today's behaviour bit-for-bit; every adoption and every near-miss is logged
/// ("STACKED SHELL" census) so the next hardware log proves which stage claimed or dropped
/// each fort renderer.
///
/// MULTIPLAYER: local rendering only (property blocks + renderer.enabled on local scenery),
/// nothing synced, peers unaffected — same contract as every other WallSegmentFade attachment.
///
/// HARDWARE ROUND 2 (2026-08-05 log — keep still solid, three findings, all fixed here):
/// <list type="bullet">
/// <item>BAND ANCHOR: the fixpoint adopted only the TALLEST piece per wall ('polySurface2',
///   top ~5.0) — raising the live AABB top made every sibling with base 3.3–3.5 read as
///   "inside/below the wall body" (&gt; StackMaxOverlapDownWU under the RAISED top). The
///   band's lower bound now anchors on the wall's ORIGINAL course top
///   (<c>Segment.StackOrigTop</c>, snapshotted before any extension); only the upper bound
///   tracks the grown column.</item>
/// <item>DOORWAY MASKING: adoption always fell back to the nearest NON-doorway wall in range
///   (doorways are skipped in the search), but the near-miss classifier reported the
///   geometrically nearest wall of ANY kind — gate-top pieces failing the (buggy) band test
///   against their flanking wall were logged as "nearest wall is a DOORWAY", masking the real
///   reason. The classifier now grades against the nearest ELIGIBLE wall and mentions a
///   doorway only when no fadeable wall is in reach at all.</item>
/// <item>MOUNTED STEAL: the pieces this pass rejected were then accepted by the LATER mounted
///   pass against the extended AABB (fade ON 'Wall 2' carried the fort as "+22 mounted
///   prop(s)") — riding the fade while contributing ZERO occlusion, so the trigger kept
///   starving. The mounted pass now has an architecture-scale mesh guard
///   (<see cref="FadeDriver.MountedMaxMeshVolumeWU3"/>) so this class cannot recur.</item>
/// </list>
///
/// HARDWARE ROUND 5 (2026-08-05 23:19 — decision chain proven end-to-end, 'Wall 2' raw1.00
/// ON 1.00 with S41/S48, yet the user saw a fully solid keep; all delivery-side, fixed here):
/// <list type="bullet">
/// <item>HELD-STATE EARLY-OUT: `StackedState == 2 → return` skipped every piece that ARRIVED
///   while the wall was already held faded. Apparance regenerates the shell continuously
///   (census 93→87→48→41 in one session), so within seconds of the flip the whole shell was
///   back — visible forever. All four attachment appliers now enforce the held state per
///   frame (steady cost: one enabled compare per piece).</item>
/// <item>FAST RECLAIM: a regenerated piece is a NEW renderer the 2s rescan re-claims too
///   late at regen cadence — <see cref="FadeDriver.FastReclaimRegeneratedShell"/> sweeps
///   every 0.25s while a stack-carrying wall is held faded and hides fresh matches within
///   a frame of appearing.</item>
/// <item>ENGULF → RIDE-ONLY and cap 48→128 (see the respective doc comments): real shell
///   mass was being left solid by the recalibrated-room guard and the per-column cap.</item>
/// <item>DELIVERY TRUTH (this tileset): the cache walls' only fade-shader renderers are
///   torch/shelf props — the masonry itself carries NO WallFade shader, so the MPB
///   map/cutoff path changes nearly nothing on screen; <c>renderer.enabled = false</c> on
///   the stacked/mounted pieces IS the mechanism that visibly opens the keep, and the
///   cutoff MPB ramp on these shaders is best-effort (unverified — a silent no-op ends in
///   the same guaranteed disable). The heartbeat now re-arms when the fade-capable census
///   changes, so the unfadeable-wall TRIPWIRE (shader names of such masonry) finally
///   reaches the log.</item>
/// </list>
///
/// ROUND 15 SUPERSEDES THE "DELIVERY TRUTH" BULLET ABOVE. <c>renderer.enabled = false</c> is
/// still the final guarantee, but it is no longer the visible mechanism: every stacked piece
/// now gets a real dissolve first (see WallSegmentFade.Dissolve.cs). The masonry DOES carry the
/// Amp fade subgraph behind a live <c>_WallFade_On</c> toggle — it was simply being driven with
/// the foliage <c>_Cutoff</c> lerp instead of the wall renderers' map/_Cutoff ramp, which is
/// exactly why the gate's courses popped. Toggle-native pieces are driven natively now, the
/// rest get the material swap, and the per-segment DISSOLVE CENSUS line names anything that
/// still cannot dissolve, with the reason.
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>STACKED SHELL pieces (fort/keep superstructure meshes without a fade
        /// shader — see the file header): they extend this wall's occlusion AABB and dissolve
        /// and restore with its fade. Never contains a Light.</summary>
        public readonly List<MountedProp> Stacked = new();
        public readonly List<MountedProp> PrevStacked = new();
        /// <summary>0 = restored/untouched, 1 = dissolving, 2 = hidden.</summary>
        public int StackedState;
        /// <summary>FACE DOMAIN (round 7, defect c — far-wall merlons flickered): the wall's
        /// ORIGINAL pre-stack XZ footprint expanded by <see cref="FadeDriver.FaceMarginWU"/>.
        /// A piece may only join this wall if its XZ center lies inside this rect or its AABB
        /// overlaps it, and the decision AABB is CLAMPED to it — chaining grows the column in
        /// Y, never around corners. Merlon→merlon gaps along a parapet are always within the
        /// link range, so an unclamped chain walked the whole ring from the faded face onto
        /// the opposite wall (hidden there while that wall blocked nothing, flickering under
        /// regen churn).</summary>
        public float FaceMinX = float.PositiveInfinity, FaceMaxX = float.NegativeInfinity;
        public float FaceMinZ = float.PositiveInfinity, FaceMaxZ = float.NegativeInfinity;

        /// <summary>The wall's ORIGINAL course top (AABB max.y BEFORE any stacked piece
        /// extended it this rescan) — the anchor of the stack band's LOWER bound. Hardware
        /// round 2 proved the live top is the wrong anchor: the fixpoint adopted the tallest
        /// piece first (top 2.75 → ~5.0), and every sibling whose base (3.3–3.5) then sat
        /// &gt; StackMaxOverlapDownWU below the RAISED top was rejected as "inside/below the
        /// wall body" — the fort's real mass stayed solid. A piece is a continuation iff its
        /// base clears the original masonry course; how far the column has already grown is
        /// irrelevant to that question.</summary>
        public float StackOrigTop = float.NegativeInfinity;
    }

    private sealed partial class FadeDriver
    {
        /// <summary>Max horizontal gap (wu) between a shell piece and the wall column it
        /// continues — same reach as the mounted-dressing link: a stacked story hugs its wall
        /// (log: adopted fort pieces at gap 0.00–0.36), the next parallel wall run is ≥ a hex
        /// (~1.72 wu) away.</summary>
        private const float StackLinkMaxXZ = 0.9f;
        /// <summary>How far (wu) above the wall's current AABB top a piece's BOTTOM may start
        /// and still count as the next story. The log's fort pieces rise 0.25–1.1 wu over
        /// their wall tops (interlocking course offsets); a full story is ≥ ~2 wu, so 1.25
        /// cannot skip across one.</summary>
        private const float StackMaxRiseWU = 1.25f;
        /// <summary>How far (wu) a piece's bottom may reach DOWN into the wall body (stories
        /// interlock) — anything deeper is parallel geometry, not a continuation.</summary>
        private const float StackMaxOverlapDownWU = 1.2f;
        /// <summary>Fixpoint rounds: each round can add one more story onto the growing
        /// column (battlement on low-wall on wall = 3; one spare).</summary>
        private const int StackMaxRounds = 4;
        /// <summary>Runaway guard — no wall column carries more shell pieces than this.
        /// Round 5 raised 48 → 128: the keep session had 93+ real shell pieces and the old
        /// cap visibly sliced Wall 2's column at S48; the guard now only catches genuine
        /// runaway (a column cannot plausibly have 128 real courses).</summary>
        private const int StackMaxPerSegment = 128;
        /// <summary>Caps on the census/near-miss log lists (log hygiene).</summary>
        private const int StackCensusCap = 12;
        private const int StackRejectCap = 16;
        /// <summary>Diagnostic radius (wu): an unadopted candidate this close to a wall is
        /// logged with its rejection reason (mirrors the mounted near-miss discipline).</summary>
        private const float StackNearMissXZ = 2.5f;
        /// <summary>FACE DOMAIN margin (wu, round 7): how far beyond the wall's original XZ
        /// footprint its adoption domain and decision AABB may reach. Covers corbel overhang
        /// (observed adoption gaps ≤ 0.47) without letting a parapet chain turn a corner.</summary>
        private const float FaceMarginWU = 1.5f;

        /// <summary>Is the piece within this wall's OWN FACE (round 7, defect c)? Center
        /// inside the face rect, or AABB overlapping it.</summary>
        private static bool InFaceDomain(Segment seg, Bounds b)
        {
            Vector3 c = b.center;
            if (c.x >= seg.FaceMinX && c.x <= seg.FaceMaxX
                && c.z >= seg.FaceMinZ && c.z <= seg.FaceMaxZ)
                return true;
            return b.max.x >= seg.FaceMinX && b.min.x <= seg.FaceMaxX
                && b.max.z >= seg.FaceMinZ && b.min.z <= seg.FaceMaxZ;
        }

        /// <summary>Union of a bounds and another AABB, without mutating either.</summary>
        private static Bounds EncapsulateCopy(Bounds a, Bounds b)
        {
            a.Encapsulate(b);
            return a;
        }

        /// <summary>Clamp an extended decision AABB's XZ to the wall's face domain (round 7):
        /// the column may grow in Y without limit, but never around a corner — the !FAT ring
        /// boxes both mis-adopted (chain reach) and mis-triggered (coverage from geometry on
        /// other faces).</summary>
        private static Bounds ClampExtensionToFace(Segment seg, Bounds ext)
        {
            Vector3 min = ext.min, max = ext.max;
            min.x = Mathf.Max(min.x, seg.FaceMinX);
            max.x = Mathf.Min(max.x, seg.FaceMaxX);
            min.z = Mathf.Max(min.z, seg.FaceMinZ);
            max.z = Mathf.Min(max.z, seg.FaceMaxZ);
            var clamped = new Bounds();
            clamped.SetMinMax(min, max);
            return clamped;
        }

        /// <summary>SHARED CORNER PIECE (round 7, defect b — the two isolated towers): a
        /// piece within stack reach of one or two walls but outside every wall's face domain
        /// (it sits BETWEEN faces). It hides only when ALL its adjacent walls are faded —
        /// the tower between two open faces opens too, but stands while either neighbor
        /// stands; with a single neighbor it simply rides that wall.</summary>
        private sealed class CornerPiece
        {
            public MountedProp Prop = null!;
            public Segment A = null!;
            public Segment? B;
        }

        private readonly List<CornerPiece> _cornerPieces = new();
        private readonly List<CornerPiece> _prevCorners = new();
        private int _lastLoggedCornerCount = -1;

        // ---- ownership-churn tracker (round 11 — the "mal ist es da, mal ist es weg"
        // neighbor asset): any renderer whose owner/protection changes more than
        // OwnershipChurnMax times inside OwnershipChurnWindowSeconds is named in a WARN,
        // so the next log identifies WHAT flaps and BETWEEN WHICH owners.
        private const int OwnershipChurnMax = 2;
        private const float OwnershipChurnWindowSeconds = 60f;

        private sealed class OwnershipRecord
        {
            public float WindowStart;
            public int Changes;
            public string LastOwner = "";
            public string History = "";
            public bool Warned;
        }

        private readonly Dictionary<Renderer, OwnershipRecord> _ownershipChanges = new();

        /// <summary>Record an ownership transition for a renderer (adoption by a named owner
        /// or "released"); WARNs once per window when a piece flaps.</summary>
        private void NoteOwnershipChange(Renderer r, string owner)
        {
            if (r == null)
                return;
            float now = Time.unscaledTime;
            if (!_ownershipChanges.TryGetValue(r, out OwnershipRecord? rec))
            {
                if (_ownershipChanges.Count >= 96)
                    _ownershipChanges.Clear(); // bounded scratch — worst case a fresh window
                rec = new OwnershipRecord { WindowStart = now, LastOwner = owner, History = owner };
                _ownershipChanges[r] = rec;
                return;
            }
            if (owner == rec.LastOwner)
                return; // steady ownership is not churn
            if (now - rec.WindowStart > OwnershipChurnWindowSeconds)
            {
                rec.WindowStart = now;
                rec.Changes = 0;
                rec.History = rec.LastOwner;
                rec.Warned = false;
            }
            rec.Changes++;
            if (rec.History.Length < 160)
                rec.History += " → " + owner;
            rec.LastOwner = owner;
            if (rec.Changes > OwnershipChurnMax && !rec.Warned)
            {
                rec.Warned = true;
                VRLog.Warn(Name,
                    $"OWNERSHIP CHURN: '{r.name}' changed owner {rec.Changes} times in "
                    + $"{OwnershipChurnWindowSeconds:0}s ({rec.History}) — this is the "
                    + "flapping-asset tripwire (round 11).");
            }
        }

        /// <summary>Renderers owned by a stacked list THIS rescan (one owner per renderer).</summary>
        private readonly HashSet<Renderer> _stackedOwned = new();
        /// <summary>Candidates rejected for cause mid-round (engulf / game logic) — never
        /// retried in later rounds and excluded from the generic near-miss classification.</summary>
        private readonly HashSet<Renderer> _stackDead = new();
        private readonly List<MeshRenderer> _stackCandidates = new();
        private readonly List<string> _stackCensus = new();
        private readonly List<string> _stackRejects = new();
        private int _censusStacked;
        private int _censusStackedRejected;
        /// <summary>Round-7 figure guard census: candidates excluded because they belong to a
        /// FIGURE (never touched — Lights-rule severity), with a few names for the log.</summary>
        private int _censusFigureGuarded;
        private int _lastLoggedFigureGuarded = -1;
        private readonly List<string> _figureGuardNames = new();
        private int _lastLoggedStackedCount = -1;
        private int _lastLoggedStackedRejected = -1;

        /// <summary>Restore ALL of a segment's stacked shell pieces — called on every path
        /// where the segment stops owning them (unfade, segment drop, group split, toggle-off,
        /// teardown), so no keep story can stay hidden without an owner.</summary>
        private void RestoreSegmentStacked(Segment seg)
        {
            if (seg.StackedState == 0)
                return;
            seg.StackedState = 0;
            foreach (MountedProp p in seg.Stacked)
                RestoreProp(p);
        }

        /// <summary>
        /// Drive the segment's stacked shell alongside its fade — the same 0→1 the wall's own
        /// cutoff sweep runs on (no particle lead: these are architecture meshes). The renderer
        /// is disabled at the very end as the guarantee that nothing survives; everything
        /// reverses exactly on unfade. Shares the mounted ledger, so the orphan guard covers
        /// these pieces too.
        /// </summary>
        private void ApplyStacked(Segment seg)
        {
            if (seg.Stacked.Count == 0)
                return;
            int want = seg.Fade >= FoliageHideFade ? 2 : seg.Fade > 0f ? 1 : 0;
            if (want == 0)
            {
                RestoreSegmentStacked(seg);
                return;
            }
            // NO held-state early-out (round 5): Apparance regenerates the shell content
            // continuously (stacked census fluctuated 93→87→48→41 within one session), so a
            // piece adopted or re-enabled while the segment is ALREADY held faded must be
            // hidden THIS frame — the old `StackedState == 2 → return` skipped exactly those,
            // and the user saw a fully solid keep while the decision loop reported ON 1.00.
            // In the held steady state the loop below is one enabled-flag compare per piece.
            bool lost = false;
            foreach (MountedProp p in seg.Stacked)
            {
                if (p.Renderer == null)
                {
                    lost = true;
                    continue;
                }
                if (want == 2)
                {
                    if (p.Renderer.enabled)
                    {
                        // Fresh arrival during the held state: park the material/particle
                        // ramp at the hidden end first, then the guaranteed disable. The
                        // channel is established HERE too (round 15) so the piece is parked in
                        // the native held look and its RETURN edge animates from frame one.
                        _mountedTouched[p.Renderer] = p;
                        EnsureDissolveChannel(p);
                        DriveProp(p, 1f);
                        p.Renderer.enabled = false;
                    }
                }
                else
                {
                    _mountedTouched[p.Renderer] = p;
                    EnsureDissolveChannel(p); // round 15: everything that fades animates
                    DriveProp(p, seg.Fade);
                    if (!p.Renderer.enabled)
                        p.Renderer.enabled = true;
                }
            }
            if (lost)
                _nextRescan = 0f; // piece regenerated away mid-fade — re-collect promptly
            seg.StackedState = want;
        }

        // ---- fast reclaim (Apparance regen churn, round 5) --------------------------------

        /// <summary>Between-rescan sweep cadence while a wall is held faded. The full 2s
        /// rescan is far too slow against Apparance's regen churn: a regenerated shell piece
        /// arrives fresh and VISIBLE, and at regen cadence ≤ rescan cadence the keep never
        /// visibly disappears although the decision loop holds ON 1.00.</summary>
        private const float FastReclaimIntervalSeconds = 0.25f;

        private float _nextFastReclaim;
        private int _fastReclaimTotal;
        private float _nextFastReclaimLog;
        private readonly List<Segment> _fastSegScratch = new();
        /// <summary>Round-12: per-sweep snapshot of every segment-listed renderer — the fast
        /// path must refuse them exactly like the regular sweep (the churn fix).</summary>
        private readonly HashSet<Renderer> _fastOwnedScratch = new();

        /// <summary>
        /// FAST RECLAIM (round 5): while at least one stack-carrying wall is HELD FADED,
        /// sweep the scene every 0.25s for fresh visible meshes inside a faded wall's stack
        /// band and hide them within a frame of appearing — the structural answer to
        /// Apparance regenerating shell content between 2s rescans. Cost: one
        /// FindObjectsOfType&lt;MeshRenderer&gt; per 0.25s ONLY while a wall is faded (the
        /// full rescan already does a heavier sweep every 2s); per renderer the hot path is
        /// one gap compare against the few faded segments. Adopted pieces follow the exact
        /// rescan rules (band, ground, engulf→ride-only, game-logic guards) and land in the
        /// shared ledger, so restore/orphan discipline is unchanged.
        /// </summary>
        private void FastReclaimRegeneratedShell(float now)
        {
            if (!WallFadeTuning.StackedShells || now < _nextFastReclaim)
                return;
            // PERF S1: same one-pass figure memo the rescan opens (see FigureAncestryMemo) —
            // this sweep classifies every scene renderer four times a second.
            BeginFigureMemo();
            try { FastReclaimSweep(now); }
            finally { EndFigureMemo(); }
        }

        private void FastReclaimSweep(float now)
        {
            _nextFastReclaim = now + FastReclaimIntervalSeconds;
            // PERF S1: its own scope. This sweep is a full-scene FindObjectsOfType FOUR TIMES
            // A SECOND while any stack-carrying wall is held faded — i.e. precisely in the
            // zoomed-out overview — and it has never been measured separately (the 2026-08-09
            // capture's WallFade.Late total is too small for it to have run at all in that
            // session, so it is a latent cost, not a proven one). It stays at 0.25 s: the
            // cadence is the whole point of the fix it implements (Apparance regenerates shell
            // content between 2 s rescans and a fresh piece arrives VISIBLE), so moving it to
            // the fade edge would leave regenerated pieces standing inside a faded wall for up
            // to two seconds — a look change, which this round forbids.
            using var _fastScope = PerfMonitor.Scope("WallFade.FastReclaim");
            _fastSegScratch.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (seg.HasBounds && seg.Fade >= FoliageHideFade
                    && (seg.Stacked.Count > 0 || seg.Body.Count > 0)
                    && StackEligible(seg) && seg.Stacked.Count < StackMaxPerSegment)
                    _fastSegScratch.Add(seg);
            }
            if (_fastSegScratch.Count == 0)
                return;

            // ROUND-12 CHURN FIX (the tripwire's 20 WARNs: gate embedding pieces cycled
            // 'stacked-fast → released → stacked-fast …'): those pieces are ANOTHER WALL's
            // own renderers ('polySurface2 … already the wall renderer of Wall 3' — the
            // gatehouse wall, toggle-native). The regular sweep correctly refuses them via
            // IsSegmentListedRenderer, but this fast path lacked that check, claimed them,
            // and the next rescan's sticky re-add rejected + released them — the flicker.
            // Snapshot every segment-listed renderer ONCE per sweep (set lookup per
            // candidate) and refuse them here exactly like the regular sweep.
            _fastOwnedScratch.Clear();
            foreach (Segment seg in _segments.Values)
            {
                foreach (MeshRenderer sr in seg.Renderers)
                    if (sr != null) _fastOwnedScratch.Add(sr);
                foreach (MeshRenderer sf in seg.Foliage)
                    if (sf != null) _fastOwnedScratch.Add(sf);
                foreach (MeshRenderer ss in seg.Siblings)
                    if (ss != null) _fastOwnedScratch.Add(ss);
                foreach (MountedProp bp in seg.Body)
                    if (bp.Renderer != null) _fastOwnedScratch.Add(bp.Renderer);
                foreach (MountedProp mp in seg.Mounted)
                    if (mp.Renderer != null) _fastOwnedScratch.Add(mp.Renderer);
            }

            // PERF S1 — UNION PREFILTER, and why it cannot change a single adoption. Every
            // path below that can claim a renderer (the face-domain 'best' branch AND the
            // corner branch) requires, for SOME faded segment, all three of:
            //   HorizontalGap(seg.Bounds, b) <= StackLinkMaxXZ,
            //   b.min.y >= that segment's band floor, and
            //   b.min.y <= seg.Bounds.max.y + StackMaxRiseWU.
            // A gap within StackLinkMaxXZ implies the candidate's XZ AABB lies within that
            // margin of the segment's, so failing the UNION of all faded segments' XZ rects
            // (grown by the link reach) means failing every individual one; likewise a
            // b.min.y below the LOWEST band floor or above the HIGHEST band ceiling can
            // satisfy no segment. The test is therefore a necessary condition for adoption:
            // everything it rejects, the per-segment loop rejected too. What it saves is the
            // work that used to run FIRST for all ~3000 scene renderers, four times a second
            // — IsModObject (which reads r.name, i.e. an interop string ALLOCATION per
            // renderer per sweep), two set probes, and the arch/water rect scans. Those are
            // pure predicates, so hoisting the cheap AABB compare above them is a reordering
            // of side-effect-free guards and leaves the outcome identical.
            float bandFloor = float.PositiveInfinity, bandCeil = float.NegativeInfinity;
            float unionMinX = float.PositiveInfinity, unionMaxX = float.NegativeInfinity;
            float unionMinZ = float.PositiveInfinity, unionMaxZ = float.NegativeInfinity;
            foreach (Segment seg in _fastSegScratch)
            {
                float lower = seg.Renderers.Count == 0 && seg.Body.Count > 0
                    ? _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU
                    : seg.StackOrigTop - StackMaxOverlapDownWU;
                // The unconditional ground-band floor applies to every segment as well.
                lower = Mathf.Max(lower, _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU);
                if (lower < bandFloor)
                    bandFloor = lower;
                float ceil = seg.Bounds.max.y + StackMaxRiseWU;
                if (ceil > bandCeil)
                    bandCeil = ceil;
                if (seg.Bounds.min.x < unionMinX) unionMinX = seg.Bounds.min.x;
                if (seg.Bounds.max.x > unionMaxX) unionMaxX = seg.Bounds.max.x;
                if (seg.Bounds.min.z < unionMinZ) unionMinZ = seg.Bounds.min.z;
                if (seg.Bounds.max.z > unionMaxZ) unionMaxZ = seg.Bounds.max.z;
            }
            unionMinX -= StackLinkMaxXZ;
            unionMaxX += StackLinkMaxXZ;
            unionMinZ -= StackLinkMaxXZ;
            unionMaxZ += StackLinkMaxXZ;

            MeshRenderer[] all = UnityEngine.Object.FindObjectsOfType<MeshRenderer>();
            int claimed = 0;
            foreach (MeshRenderer r in all)
            {
                if (r == null || !r.enabled)
                    continue;
                Bounds b = r.bounds;
                if (b.max.x < unionMinX || b.min.x > unionMaxX
                    || b.max.z < unionMinZ || b.min.z > unionMaxZ
                    || b.min.y < bandFloor || b.min.y > bandCeil)
                    continue; // outside every faded segment's reach — see the prefilter note
                if (IsModObject(r))
                    continue;
                if (_mountedTouched.ContainsKey(r))
                    continue; // already ours (hidden or ramped)
                if (_fastOwnedScratch.Contains(r))
                    continue; // another segment's renderer/attachment — never fast-claimed
                if (IsArchProtected(b, r.name))
                    continue; // the doorway's arch stays solid (user ruling 2026-08-07)
                if (IsWaterProtected(b))
                    continue; // fountain/pond stays solid (user ruling 2026-08-09, brunnen.png)
                Segment? best = null;
                float bestGap = float.PositiveInfinity;
                Segment? corner = null, cornerB = null;
                float cornerGap = float.PositiveInfinity;
                foreach (Segment seg in _fastSegScratch)
                {
                    float gap = HorizontalGap(seg.Bounds, b);
                    if (gap > StackLinkMaxXZ || gap >= bestGap)
                        continue;
                    // BODY walls (round 6, enabled-only masonry): a regenerated course can
                    // sit anywhere in the wall column, so the band's lower bound is the
                    // ground exclusion, not the original course top.
                    float lower = seg.Renderers.Count == 0 && seg.Body.Count > 0
                        ? _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU
                        : seg.StackOrigTop - StackMaxOverlapDownWU;
                    if (b.min.y < lower
                        || b.min.y > seg.Bounds.max.y + StackMaxRiseWU)
                        continue;
                    if (b.min.y < _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU)
                        continue;
                    if (!InFaceDomain(seg, b))
                    {
                        // Round 7: outside every face domain but within stack reach of a
                        // FADED wall — a regenerated corner piece. All segments in this
                        // sweep are held faded, so the corner hide condition already holds.
                        if (corner == null || gap < cornerGap)
                        {
                            cornerB = corner;
                            corner = seg;
                            cornerGap = gap;
                        }
                        continue;
                    }
                    best = seg;
                    bestGap = gap;
                }
                if (best == null && corner != null)
                {
                    if (RendererUsesWallFade(r) || RendererUsesFoliage(r)
                        || IsFigureOrActorRenderer(r)
                        || r.GetComponentInParent<TileBehaviour>() != null
                        || r.GetComponentInParent<Canvas>() != null
                        || r.GetComponent<TMPro.TMP_Text>() != null)
                        continue;
                    MountedProp cprop = ClassifyProp(r);
                    _cornerPieces.Add(new CornerPiece { Prop = cprop, A = corner, B = cornerB });
                    _stackedOwned.Add(r);
                    NoteOwnershipChange(r,
                        $"corner-fast:'{(corner.Anchor != null ? corner.Anchor.name : "?")}'");
                    _mountedTouched[r] = cprop;
                    EnsureDissolveChannel(cprop); // round 15: parked native, animates on return
                    DriveProp(cprop, 1f);
                    r.enabled = false;
                    claimed++;
                    continue;
                }
                if (best == null)
                    continue;
                if (RendererUsesWallFade(r) || RendererUsesFoliage(r))
                    continue; // cached shader verdicts — cheap
                if (IsFigureOrActorRenderer(r))
                    continue; // FIGURES are never touched (round-7 ruling)
                if (r.GetComponentInParent<TileBehaviour>() != null
                    || r.GetComponentInParent<Canvas>() != null
                    || r.GetComponent<TMPro.TMP_Text>() != null)
                    continue;
                Bounds ext = ClampExtensionToFace(best, EncapsulateCopy(best.Bounds, b));
                bool extend = !(InsideRoomFraction(ext, best.RoomIndex) >= EngulfSampleFraction
                    && InsideOwnRoomFraction(best) < EngulfSampleFraction);
                MountedProp prop = ClassifyProp(r);
                best.Stacked.Add(prop);
                if (extend)
                    best.Bounds = ext;
                _stackedOwned.Add(r);
                NoteOwnershipChange(r,
                    $"stacked-fast:'{(best.Anchor != null ? best.Anchor.name : "?")}'");
                _mountedTouched[r] = prop;
                EnsureDissolveChannel(prop); // round 15: parked native, animates on return
                DriveProp(prop, 1f);
                r.enabled = false;
                claimed++;
            }
            if (claimed > 0)
            {
                _fastReclaimTotal += claimed;
                if (now >= _nextFastReclaimLog)
                {
                    _nextFastReclaimLog = now + 5f;
                    VRLog.Info(Name,
                        $"FAST-RECLAIM: {claimed} regenerated shell piece(s) re-hidden within "
                        + $"{FastReclaimIntervalSeconds:0.00}s of appearing (Apparance regen "
                        + $"churn; session total {_fastReclaimTotal}).");
                }
            }
        }

        /// <summary>May this segment carry stacked shell pieces? Doorways never fade (user
        /// ruling 2026-08-02) so their superstructure must stay with them; engulfing and
        /// fail-safe segments make no fade decision a piece could ride.</summary>
        private bool StackEligible(Segment seg) =>
            seg.HasBounds && seg.DoorRoot == null && !seg.Engulfing
            && RoomDecisionValid(seg.RoomIndex);

        /// <summary>
        /// Adopt, per rescan, every plain mesh that continues a tracked wall upward (see the
        /// file header for the rule and the evidence). Runs AFTER ground strip + engulf
        /// neutralization (needs final base AABBs and room grids) and BEFORE the mounted
        /// pass (which must see the extended AABBs so shell-hung torches attach). Leavers
        /// are restored here; orphans by the shared mounted orphan guard.
        /// </summary>
        /// <param name="sceneRenderers">The rescan's single scene sweep (shared with the
        /// adoption + mounted passes — still one FindObjectsOfType per rescan).</param>
        private void CollectStackedShellPieces(Renderer[] sceneRenderers)
        {
            _stackedOwned.Clear();
            _stackDead.Clear();
            _stackCandidates.Clear();
            _stackCensus.Clear();
            _stackRejects.Clear();
            _censusStacked = 0;
            _censusStackedRejected = 0;
            _censusFigureGuarded = 0;
            _figureGuardNames.Clear();

            foreach (Segment seg in _segments.Values)
            {
                seg.PrevStacked.Clear();
                seg.PrevStacked.AddRange(seg.Stacked);
                seg.Stacked.Clear();
                // Snapshot the ORIGINAL course top before sticky pieces or adoptions extend
                // the AABB — the stack band's lower bound anchors here (see StackOrigTop) —
                // and the FACE DOMAIN rect (round 7): the pre-stack XZ footprint + margin
                // that clamps both adoption and the decision AABB to this wall's own face.
                seg.StackOrigTop = seg.HasBounds ? seg.Bounds.max.y : float.NegativeInfinity;
                if (seg.HasBounds)
                {
                    seg.FaceMinX = seg.Bounds.min.x - FaceMarginWU;
                    seg.FaceMaxX = seg.Bounds.max.x + FaceMarginWU;
                    seg.FaceMinZ = seg.Bounds.min.z - FaceMarginWU;
                    seg.FaceMaxZ = seg.Bounds.max.z + FaceMarginWU;
                }
                else
                {
                    seg.FaceMinX = seg.FaceMinZ = float.PositiveInfinity;
                    seg.FaceMaxX = seg.FaceMaxZ = float.NegativeInfinity;
                }
            }

            bool enabled = WallFadeTuning.StackedShells;
            if (enabled && sceneRenderers != null && _segments.Count > 0)
            {
                // PERF S1: the membership index IsSegmentListedRenderer reads, built once
                // here — see BuildSegmentListedIndex for why it answers identically.
                BuildSegmentListedIndex();

                // STICKY OWNERSHIP while the wall is mid-fade or held faded (the mounted
                // lesson): pieces are carried over untested — and their AABBs re-extend the
                // bounds so the coverage decision stays consistent across rescans — because
                // releasing a piece while its wall is gone is a visible blink.
                foreach (Segment seg in _segments.Values)
                {
                    if (seg.StackedState == 0 && seg.Fade <= 0f)
                        continue;
                    foreach (MountedProp p in seg.PrevStacked)
                    {
                        if (p.Renderer == null || !_stackedOwned.Add(p.Renderer))
                            continue;
                        // A fast-reclaimed mesh that the body collection has since taken
                        // over (round 6) belongs to the BODY now — do not double-own it.
                        if (p.Renderer is MeshRenderer bm && IsSegmentListedRenderer(bm))
                            continue;
                        // Figures are NEVER carried, sticky or not (round-7 ruling).
                        if (IsFigureOrActorRenderer(p.Renderer))
                            continue;
                        seg.Stacked.Add(p);
                        _censusStacked++;
                        if (seg.HasBounds)
                        {
                            Bounds ext = seg.Bounds;
                            ext.Encapsulate(p.Renderer.bounds);
                            seg.Bounds = ClampExtensionToFace(seg, ext); // Y grows, XZ face-clamped
                        }
                    }
                }

                // Corner stickiness (round 7): while a hidden corner piece's neighbors are
                // still faded, carry it — its renderer is DISABLED and would otherwise miss
                // the candidate prefilter, get orphan-restored and flicker (the merlon bug).
                _prevCorners.Clear();
                _prevCorners.AddRange(_cornerPieces);
                _cornerPieces.Clear();
                foreach (CornerPiece cp in _prevCorners)
                {
                    if (cp.Prop.Renderer == null
                        || !_mountedTouched.ContainsKey(cp.Prop.Renderer)
                        || cp.A.Anchor == null
                        || IsFigureOrActorRenderer(cp.Prop.Renderer))
                        continue;
                    if (_stackedOwned.Add(cp.Prop.Renderer))
                        _cornerPieces.Add(cp);
                }
                _prevCorners.Clear();

                CollectStackCandidates(sceneRenderers);
                RunStackAdoptionRounds();
                CollectCornerPieces();
                ClassifyStackNearMisses();
            }

            // Leavers: restore anything a segment held that it no longer owns (config off /
            // piece no longer qualifies). Nothing may stay hidden without an owner.
            foreach (Segment seg in _segments.Values)
            {
                if (seg.StackedState != 0)
                {
                    foreach (MountedProp prev in seg.PrevStacked)
                    {
                        if (prev.Renderer != null && !seg.Stacked.Contains(prev))
                            RestoreProp(prev);
                    }
                    if (seg.Stacked.Count == 0)
                        seg.StackedState = 0;
                }
                seg.PrevStacked.Clear();
            }

            if (_censusStacked != _lastLoggedStackedCount
                || _censusStackedRejected != _lastLoggedStackedRejected)
                LogStackedCensus();

            // Round-7 figure-guard proof line: the next hardware log must show the guard
            // FIRING, not just existing. Logged whenever the count is nonzero and changed.
            if (_censusFigureGuarded > 0 && _censusFigureGuarded != _lastLoggedFigureGuarded)
            {
                _lastLoggedFigureGuarded = _censusFigureGuarded;
                VRLog.Info(Name,
                    $"FIGURE-GUARD: {_censusFigureGuarded} adoption candidate(s) excluded as "
                    + $"figure/actor renderers (skinned OR under ActorBehaviour/"
                    + $"CInteractableActor/Animator) — figures are NEVER touched by any wall "
                    + $"system (round-7 ruling, Lights-rule severity): "
                    + $"{string.Join(", ", _figureGuardNames)}.");
            }
        }

        /// <summary>
        /// Cheap prefilter over the scene sweep: plain MeshRenderers that could possibly be
        /// shell stories — airborne over the lowest anchored floor (the ground band never
        /// fades), not mod-owned, not already tracked by any segment list, and not a wall
        /// (WallFade shader) or foliage (those have their own attachment types).
        /// </summary>
        private void CollectStackCandidates(Renderer[] sceneRenderers)
        {
            float minFloorY = float.PositiveInfinity;
            for (int i = 0; i < _roomFloorY.Count && i < _roomFloorAnchored.Count; i++)
            {
                if (_roomFloorAnchored[i] && _roomFloorY[i] < minFloorY)
                    minFloorY = _roomFloorY[i];
            }
            if (float.IsInfinity(minFloorY))
                return; // no anchored room — every wall is fail-safe solid anyway

            float bar = minFloorY + GroundExclusionHeightWU;
            foreach (Renderer any in sceneRenderers)
            {
                if (any is not MeshRenderer r || r == null || !r.enabled)
                    continue;
                if (IsModObject(r))
                    continue; // mod-owned visual (layer OR 'GloomhavenVR.' name — round 3:
                              // the MR backing plate leaked into the near-miss census)
                if (IsFigureOrActorRenderer(r))
                {
                    // FIGURES ARE NEVER TOUCHED (round-7 ruling, Lights-rule severity):
                    // excluded before any geometric test, counted for the census.
                    _censusFigureGuarded++;
                    if (_figureGuardNames.Count < 6 && !_figureGuardNames.Contains(r.name))
                        _figureGuardNames.Add(r.name);
                    continue;
                }
                if (r.bounds.min.y < bar)
                    continue; // touches the ground band — not a stacked story
                if (_stackedOwned.Contains(r))
                    continue; // sticky-owned this rescan
                if (RendererUsesWallFade(r) || RendererUsesFoliage(r))
                    continue; // walls/foliage have their own tracking
                if (IsSegmentListedRenderer(r))
                    continue; // already some segment's renderer/foliage/sibling/mounted prop
                _stackCandidates.Add(r);
            }
        }

        /// <summary>Is the renderer already tracked in any segment attachment list? (The
        /// stacked pass runs before the mounted pass rebuilds <c>_attachmentOwned</c>, so it
        /// checks the live lists directly — tens of segments, short lists.) A MOUNTED entry
        /// blocks adoption only while its owner is actually fading: a shell piece the mounted
        /// pass mis-filed as dressing in an earlier rescan (log: 'polySurface2' [mesh→cutoff]
        /// → 'Wall 4') is untouched at fade 0 and must be RECLASSIFIABLE as stacked shell —
        /// the mounted pass then sees it in <c>_mountedOwned</c> and lets it go cleanly.</summary>
        private bool IsSegmentListedRenderer(MeshRenderer r) => _segmentListedIndex.Contains(r);

        /// <summary>Membership index behind <see cref="IsSegmentListedRenderer"/>.</summary>
        private readonly HashSet<Renderer> _segmentListedIndex = new();

        /// <summary>
        /// PERF S1 (2026-08-09): flatten the predicate above into one set, once per stacked
        /// pass, instead of re-walking every segment's five lists per candidate. The old shape
        /// was O(candidates × segments × list length) inside the candidate prefilter, which
        /// runs over every scene renderer — quadratic work in exactly the two quantities the
        /// big room grew (renderers 1583 → 3051, tracked segments with them).
        ///
        /// <para>SAME ANSWER, GUARANTEED. The predicate reads five per-segment lists
        /// (<c>Renderers</c>, <c>Foliage</c>, <c>Siblings</c>, <c>Body</c>, and
        /// <c>Mounted</c> when that segment's dressing is live) and this builds the union of
        /// exactly those, under exactly the same per-segment condition. It is rebuilt at the
        /// top of the stacked pass, and NONE of those five lists is written between that
        /// point and the pass's last query: the sticky/candidate/adoption/corner stages only
        /// ever touch <c>Stacked</c>, <c>Bounds</c> and <c>_cornerPieces</c>. The mounted and
        /// sibling passes that DO rewrite those lists run strictly after this pass ends.</para>
        /// </summary>
        private void BuildSegmentListedIndex()
        {
            _segmentListedIndex.Clear();
            foreach (Segment seg in _segments.Values)
            {
                foreach (MeshRenderer sr in seg.Renderers)
                {
                    if (sr != null) _segmentListedIndex.Add(sr);
                }
                foreach (MeshRenderer sf in seg.Foliage)
                {
                    if (sf != null) _segmentListedIndex.Add(sf);
                }
                foreach (MeshRenderer ss in seg.Siblings)
                {
                    if (ss != null) _segmentListedIndex.Add(ss);
                }
                // Wall BODY meshes (round 6) are the wall itself — never stack candidates.
                foreach (MountedProp p in seg.Body)
                {
                    if (p.Renderer != null) _segmentListedIndex.Add(p.Renderer);
                }
                // A MOUNTED entry blocks adoption only while its owner is actually fading —
                // the reclassification rule the original predicate encoded, kept verbatim.
                if (seg.MountedState != 0 || seg.Fade > 0f)
                {
                    foreach (MountedProp p in seg.Mounted)
                    {
                        if (p.Renderer != null) _segmentListedIndex.Add(p.Renderer);
                    }
                }
            }
        }

        /// <summary>
        /// The fixpoint adoption: each round scans all unowned candidates against the CURRENT
        /// (already-extended) wall AABBs, adopts the qualifying ones onto their nearest wall
        /// and grows that wall's AABB — so the next round can chain the story above. Stops
        /// when a round adopts nothing.
        /// </summary>
        private void RunStackAdoptionRounds()
        {
            for (int round = 0; round < StackMaxRounds; round++)
            {
                bool adoptedAny = false;
                foreach (MeshRenderer c in _stackCandidates)
                {
                    if (c == null || _stackedOwned.Contains(c) || _stackDead.Contains(c))
                        continue;
                    Bounds b = c.bounds;
                    // ARCH PROTECTION (user ruling 2026-08-07): the rectangular arch around
                    // a door is the doorway ruling's permanently-solid remainder. The reject
                    // prints the XZ containment fraction (round 10) so a hardware log can
                    // tell a tight rect from an over-broad one at a glance.
                    if (IsArchProtected(b, c.name, out float archFrac))
                    {
                        _stackDead.Add(c);
                        NoteStackReject(c, 0f,
                            $"ARCH of a doorway (XZ containment {archFrac:0.00} — permanently "
                            + "solid, user ruling 2026-08-07)");
                        continue;
                    }
                    // WATER FEATURE (user ruling 2026-08-09, brunnen.png): a fountain's basin
                    // is a plain low mesh standing next to masonry — exactly the shape this
                    // pass adopts as a "shell piece". It never fades: its water plane has no
                    // fade channel at all, so hiding the stone leaves the water in mid-air.
                    if (IsWaterProtected(b))
                    {
                        _stackDead.Add(c);
                        NoteStackReject(c, 0f,
                            "WATER FEATURE (fountain/pond — permanently solid, user ruling "
                            + "2026-08-09: 'er ist tief genug, dass er die Sicht nicht blockiert')");
                        continue;
                    }

                    Segment? best = null;
                    float bestGap = float.PositiveInfinity;
                    foreach (Segment seg in _segments.Values)
                    {
                        if (!StackEligible(seg) || seg.Stacked.Count >= StackMaxPerSegment)
                            continue;
                        float gap = HorizontalGap(seg.Bounds, b);
                        if (gap > StackLinkMaxXZ || gap >= bestGap)
                            continue;
                        // STACK BAND (hardware round 2 fix): the LOWER bound anchors on the
                        // wall's ORIGINAL course top — whether the column already grew past
                        // the piece is irrelevant to "does it continue the masonry". Only
                        // the UPPER bound tracks the live top, so chained stories connect.
                        if (b.min.y < seg.StackOrigTop - StackMaxOverlapDownWU
                            || b.min.y > seg.Bounds.max.y + StackMaxRiseWU)
                            continue; // not a course of THIS column
                        if (b.min.y < _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU)
                            continue; // ground band of the wall's own room never fades
                        if (!InFaceDomain(seg, b))
                            continue; // round 7: chain in Y, never around corners
                        bestGap = gap;
                        best = seg;
                    }
                    if (best == null)
                        continue; // near-miss classification runs once after the rounds

                    // ENGULF GUARD on the would-be extended AABB — round 5 recalibration:
                    // a piece whose adoption would make the wall XZ-contain ≥40% of its
                    // room's grid (permanent 100% coverage) is no longer REJECTED, it is
                    // adopted RIDE-ONLY: it dissolves/hides with the wall but does NOT
                    // extend the decision AABB. Rejecting outright left real shell mass
                    // permanently solid once the logical-room merge turned the guard's
                    // sample-fraction reference into the WHOLE keep interior (round-5 log:
                    // dozens of 'adoption would engulf' near-misses on true fort courses
                    // while the user saw a solid keep in a faded state). The coverage slab
                    // stays decidable; the shell still opens.
                    Bounds ext = ClampExtensionToFace(best, EncapsulateCopy(best.Bounds, b));
                    bool extend = !(InsideRoomFraction(ext, best.RoomIndex) >= EngulfSampleFraction
                        && InsideOwnRoomFraction(best) < EngulfSampleFraction);
                    // Live game logic / worldspace UI is never scenery (mounted-pass rule);
                    // the figure guard re-checks here too (belt over the prefilter — an
                    // actor can be re-parented between candidate collection and adoption).
                    if (IsFigureOrActorRenderer(c))
                    {
                        _stackDead.Add(c);
                        _censusFigureGuarded++;
                        NoteStackReject(c, bestGap,
                            "FIGURE (never touched — round-7 ruling, Lights-rule severity)");
                        continue;
                    }
                    if (c.GetComponentInParent<TileBehaviour>() != null
                        || c.GetComponentInParent<Canvas>() != null
                        || c.GetComponent<TMPro.TMP_Text>() != null)
                    {
                        _stackDead.Add(c);
                        NoteStackReject(c, bestGap, "game logic / worldspace UI");
                        continue;
                    }

                    // Reuse the ledger's record if we are currently ramping this renderer —
                    // re-classifying would snapshot our own ramp as "authored".
                    if (!_mountedTouched.TryGetValue(c, out MountedProp? prop))
                        prop = ClassifyProp(c);
                    best.Stacked.Add(prop);
                    if (extend)
                        best.Bounds = ext;
                    _stackedOwned.Add(c);
                    NoteOwnershipChange(c,
                        $"stacked:'{(best.Anchor != null ? best.Anchor.name : "?")}'");
                    _censusStacked++;
                    adoptedAny = true;
                    if (_stackCensus.Count < StackCensusCap)
                    {
                        string wall = best.Anchor != null ? best.Anchor.name : "<dead>";
                        _stackCensus.Add(
                            $"'{c.name}'[→{prop.Tier}{(extend ? "" : ", ride-only")}] "
                            + $"base {b.min.y:F1} top {b.max.y:F1} "
                            + $"gap {bestGap:F2} → '{wall}'");
                    }
                }
                if (!adoptedAny)
                    break;
            }
        }

        /// <summary>
        /// CORNER COLLECTION (round 7, defect b): after the face-clamped adoption rounds,
        /// every leftover candidate that is within stack reach (gap/band/ground) of one or
        /// two walls yet inside NO wall's face domain becomes a shared corner piece of its
        /// (up to two) nearest such walls — hidden only when all of them are faded.
        /// </summary>
        private void CollectCornerPieces()
        {
            foreach (MeshRenderer c in _stackCandidates)
            {
                if (c == null || _stackedOwned.Contains(c) || _stackDead.Contains(c))
                    continue;
                Bounds b = c.bounds;
                if (IsArchProtected(b, c.name))
                    continue; // arch stays solid (already rejected+logged by the adoption pass)
                if (IsWaterProtected(b))
                    continue; // fountain/pond stays solid (user ruling 2026-08-09, brunnen.png)
                Segment? a = null, second = null;
                float aGap = float.PositiveInfinity, secondGap = float.PositiveInfinity;
                foreach (Segment seg in _segments.Values)
                {
                    if (!StackEligible(seg) || seg.Stacked.Count >= StackMaxPerSegment)
                        continue;
                    float gap = HorizontalGap(seg.Bounds, b);
                    if (gap > StackLinkMaxXZ)
                        continue;
                    float lower = seg.Renderers.Count == 0 && seg.Body.Count > 0
                        ? _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU
                        : seg.StackOrigTop - StackMaxOverlapDownWU;
                    if (b.min.y < lower || b.min.y > seg.Bounds.max.y + StackMaxRiseWU)
                        continue;
                    if (b.min.y < _roomFloorY[seg.RoomIndex] + GroundExclusionHeightWU)
                        continue;
                    if (InFaceDomain(seg, b))
                        continue; // face pieces were adoption's business, not a corner
                    if (gap < aGap)
                    {
                        second = a;
                        secondGap = aGap;
                        a = seg;
                        aGap = gap;
                    }
                    else if (gap < secondGap)
                    {
                        second = seg;
                        secondGap = gap;
                    }
                }
                if (a == null)
                    continue;
                if (c.GetComponentInParent<TileBehaviour>() != null
                    || c.GetComponentInParent<Canvas>() != null
                    || c.GetComponent<TMPro.TMP_Text>() != null)
                    continue;
                if (!_mountedTouched.TryGetValue(c, out MountedProp? prop))
                    prop = ClassifyProp(c);
                _cornerPieces.Add(new CornerPiece { Prop = prop, A = a, B = second });
                _stackedOwned.Add(c);
                NoteOwnershipChange(c,
                    $"corner:'{(a.Anchor != null ? a.Anchor.name : "?")}'");
            }

            if (_cornerPieces.Count != _lastLoggedCornerCount)
            {
                _lastLoggedCornerCount = _cornerPieces.Count;
                if (_cornerPieces.Count > 0)
                {
                    // Round-11 census fix: the names come from the LIVE list at log time —
                    // the old add-at-collection buffer was empty whenever sticky carry-over
                    // skipped fresh collection, which printed "6 corner piece(s): ." with no
                    // names and left round-11's tower question unanswerable from the log.
                    var sb = new System.Text.StringBuilder();
                    int listed = 0;
                    foreach (CornerPiece cp in _cornerPieces)
                    {
                        Renderer r = cp.Prop.Renderer;
                        if (r == null)
                            continue;
                        if (listed++ >= 8) { sb.Append("; …"); break; }
                        if (sb.Length > 0) sb.Append("; ");
                        Bounds cb = r.bounds;
                        string an = cp.A.Anchor != null ? cp.A.Anchor.name : "<dead>";
                        string bn = cp.B == null ? "-"
                            : cp.B.Anchor != null ? cp.B.Anchor.name : "<dead>";
                        sb.Append($"'{r.name}' y[{cb.min.y:F1}..{cb.max.y:F1}] ↔ '{an}'/'{bn}'");
                    }
                    VRLog.Info(Name,
                        $"CORNER PIECES: {_cornerPieces.Count} shared corner piece(s) between "
                        + $"wall faces (hidden only while ALL adjacent walls are faded — the "
                        + $"tower between two open faces opens too; single-neighbor pieces "
                        + $"ride that wall): {sb}.");
                }
            }
        }

        /// <summary>Mark every corner piece as owned for the mounted pass's bookkeeping
        /// (called from CollectWallMountedProps — one owner per renderer, orphan-guard
        /// coverage while hidden).</summary>
        private void RegisterCornerOwnership()
        {
            foreach (CornerPiece cp in _cornerPieces)
            {
                if (cp.Prop.Renderer == null)
                    continue;
                _mountedOwned.Add(cp.Prop.Renderer);
                _attachmentOwned[cp.Prop.Renderer] = new OwnerRef(cp.A, "shared corner piece");
            }
        }

        /// <summary>
        /// Per-frame corner delivery (called from Tick): ramp/hide with the MIN fade of the
        /// adjacent walls — held-state enforced per frame like every attachment (regen
        /// churn), restored the moment any neighbor returns.
        /// </summary>
        private void ApplyCornerPieces()
        {
            if (_cornerPieces.Count == 0)
                return;
            bool lost = false;
            foreach (CornerPiece cp in _cornerPieces)
            {
                Renderer r = cp.Prop.Renderer;
                if (r == null)
                {
                    lost = true;
                    continue;
                }
                float fade = cp.B == null ? cp.A.Fade : Mathf.Min(cp.A.Fade, cp.B.Fade);
                if (fade <= 0f)
                {
                    if (_mountedTouched.ContainsKey(r))
                        RestoreProp(cp.Prop);
                    continue;
                }
                _mountedTouched[r] = cp.Prop;
                EnsureDissolveChannel(cp.Prop); // round 15: corner pieces animate too
                DriveProp(cp.Prop, fade);
                if (fade >= FoliageHideFade)
                {
                    if (r.enabled)
                        r.enabled = false;
                }
                else if (!r.enabled)
                {
                    r.enabled = true;
                }
            }
            if (lost)
                _nextRescan = 0f;
        }

        /// <summary>
        /// One pass over the leftovers: every unadopted candidate near a wall gets its exact
        /// rejection reason into the census — the line that answers "why is THAT keep story
        /// still solid" from the log alone (the mounted near-miss discipline).
        ///
        /// Round-2 lesson: classification runs against the nearest ELIGIBLE (fadeable) wall,
        /// not the nearest wall of any kind. The adoption loop always considered every
        /// non-doorway wall in range (nearest ELIGIBLE wins — the doorway "fallback" was
        /// built in from the start), but the old classifier reported whatever was
        /// geometrically closest, so a gate-top piece rejected on the band test against its
        /// FLANKING wall was logged as "nearest wall is a DOORWAY" — masking the real reason
        /// and reading as if doorway proximity chained the piece to permanent solidity.
        /// </summary>
        private void ClassifyStackNearMisses()
        {
            foreach (MeshRenderer c in _stackCandidates)
            {
                if (c == null || _stackedOwned.Contains(c) || _stackDead.Contains(c))
                    continue;
                Bounds b = c.bounds;
                Segment? near = null;              // nearest ELIGIBLE wall — the one adoption tried
                float nearGap = float.PositiveInfinity;
                Segment? nearAny = null;           // nearest wall of any kind (diag anchor)
                float nearAnyGap = float.PositiveInfinity;
                foreach (Segment seg in _segments.Values)
                {
                    if (!seg.HasBounds)
                        continue;
                    float gap = HorizontalGap(seg.Bounds, b);
                    if (gap < nearAnyGap)
                    {
                        nearAnyGap = gap;
                        nearAny = seg;
                    }
                    if (StackEligible(seg) && gap < nearGap)
                    {
                        nearGap = gap;
                        near = seg;
                    }
                }
                if (nearAny == null || nearAnyGap > StackNearMissXZ)
                    continue; // not near any wall — not a shell candidate at all
                string why;
                if (near == null || nearGap > StackNearMissXZ)
                {
                    why = nearAny.DoorRoot != null
                        ? "only a DOORWAY nearby (permanently solid — user ruling 2026-08-02); "
                          + "no fadeable wall within reach to fall back to"
                        : nearAny.Engulfing
                            ? "only a held-solid (engulfing) wall nearby"
                            : "only a FAIL-SAFE-solid wall nearby (room unanchored/no floor grid)";
                }
                else if (nearGap > StackLinkMaxXZ)
                {
                    why = $"gap {nearGap:F2} to the nearest fadeable wall is beyond the stack "
                        + $"link range {StackLinkMaxXZ:F2}";
                }
                else if (b.min.y > near.Bounds.max.y + StackMaxRiseWU)
                {
                    why = $"base floats {b.min.y - near.Bounds.max.y:F1} over the fadeable "
                        + "wall's column top — outside the stack band";
                }
                else if (b.min.y < near.StackOrigTop - StackMaxOverlapDownWU)
                {
                    why = $"base sits below the fadeable wall's ORIGINAL course top "
                        + $"{near.StackOrigTop:F1} — parallel geometry, not a continuation";
                }
                else
                {
                    why = "fadeable wall at capacity or ground-band check failed";
                }
                NoteStackReject(c, near != null && nearGap <= StackNearMissXZ ? nearGap : nearAnyGap, why);
            }
        }

        private void NoteStackReject(MeshRenderer c, float gap, string why)
        {
            _censusStackedRejected++;
            if (_stackRejects.Count < StackRejectCap)
            {
                Bounds b = c.bounds;
                // Full y-band on every reject (round 2): the piece TOPS are what decide
                // whether the trigger geometry can ever work — they must be readable from
                // the log without another blind hardware round.
                _stackRejects.Add(
                    $"'{c.name}' y[{b.min.y:F1}..{b.max.y:F1}] gap {gap:F2}: {why}");
            }
        }

        /// <summary>
        /// Heartbeat forensics for the keep class: WHICH shell pieces extend WHICH wall (with
        /// their dissolve channel), and which near candidate was rejected and why. Re-logged
        /// whenever the adopted/rejected counts change (Apparance streams the shell in over
        /// several rescans).
        /// </summary>
        private void LogStackedCensus()
        {
            _lastLoggedStackedCount = _censusStacked;
            _lastLoggedStackedRejected = _censusStackedRejected;
            if (_censusStacked == 0 && _censusStackedRejected == 0)
                return;
            string riding = _stackCensus.Count > 0 ? string.Join("; ", _stackCensus) : "none new";
            string misses = _stackRejects.Count > 0
                ? " | NEAR-MISS (stays solid): " + string.Join("; ", _stackRejects)
                : string.Empty;
            VRLog.Info(Name,
                $"STACKED SHELL: {_censusStacked} superstructure piece(s) extend their wall's "
                + $"occlusion AABB and dissolve WITH it (plain meshes continuing a wall column "
                + $"upward — XZ gap ≤{StackLinkMaxXZ:0.00} wu, base ≥ ORIGINAL course top "
                + $"−{StackMaxOverlapDownWU:0.0} wu and ≤ grown column top "
                + $"+{StackMaxRiseWU:0.00} wu, chained over ≤{StackMaxRounds} stories; "
                + $"doorways skipped in favor of the nearest fadeable wall; "
                + $"engulf → ride-only (piece hides with the wall, AABB unextended); "
                + $"regen fast-reclaim every {FastReclaimIntervalSeconds:0.00}s while faded "
                + $"(session total {_fastReclaimTotal}); ownership sticky "
                + $"while faded; Lights are NEVER written to; live config [WallFade] "
                + $"StackedShellFade): {riding}{misses} ({_censusStackedRejected} near-miss "
                + "total).");
        }
    }
}
