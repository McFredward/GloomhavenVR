using System.Collections.Generic;
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
            if (r == null || IsModObject(r))
                return false;
            int id = r.GetInstanceID();
            if (_floorMemo.TryGetValue(id, out byte cached))
            {
                if (cached != FloorMemoIsFloor)
                    return false;
                if (_floorRefusedIds.Add(id))
                    _floorSessionRefused++;
                return true;
            }
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
            _floorRestituted.Clear();
        }

        // ---- restitution ---------------------------------------------------------------------

        private readonly List<MountedProp> _floorPurgeScratch = new();
        private readonly List<Renderer> _floorShowScratch = new();

        /// <summary>
        /// Renderers this sweep has already handed back — ONE RESTITUTION PER RENDERER PER SCENE.
        ///
        /// <para><b>WITHOUT THIS THE RULE WOULD BE THE CHURN.</b> The lanes' COLLECTORS are not
        /// guarded (deliberately — the guard is at the write, which is the whole point of this
        /// file), so a floor tile keeps being enrolled in a wall's <c>Mounted</c> /
        /// <c>UnitDressing</c> list and keeps being entered into <c>_mountedTouched</c> by the
        /// applier one line before the refused <c>DriveProp</c>. A purge that restored every floor
        /// renderer it found in that ledger would therefore fire again on EVERY commit, and each
        /// <c>RestoreProp</c> feeds the ownership-churn tripwire a "released" transition — two in
        /// sixty seconds is its bar, and this would produce thirty. The project has twice found
        /// its own work to be the churn it was measuring; this is the same shape, caught before
        /// it shipped.</para>
        ///
        /// <para>ONE RESTITUTION IS ENOUGH, and that is a property of the guards rather than an
        /// assumption: after the sweep nothing can put this renderer back into a faded state,
        /// because all four write primitives refuse it. If its verdict ever flips to NOT-floor
        /// (a re-anchored board, a re-measured room), it simply fades normally and there is
        /// nothing to restore. Cleared with the scene.</para>
        /// </summary>
        private readonly HashSet<int> _floorRestituted = new(128);

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
            if (_mountedTouched.Count > 0)
            {
                _floorPurgeScratch.Clear();
                foreach (MountedProp p in _mountedTouched.Values)
                {
                    // _floorRestituted FIRST — see its declaration: without the one-shot this
                    // sweep re-fires every commit and BECOMES the ownership churn it is measured
                    // against. Contains() before the predicate so the guard's "examined" count
                    // still reflects candidates it actually had to judge.
                    if (p.Renderer != null && !_floorRestituted.Contains(p.Renderer.GetInstanceID())
                        && FloorNeverFades(p.Renderer))
                    {
                        _floorPurgeScratch.Add(p);
                    }
                }
                // ***THE RESTITUTION ITSELF. NOT A DIAGNOSTIC.*** See the summary above.
                foreach (MountedProp p in _floorPurgeScratch)
                {
                    if (p.Renderer != null)
                        _floorRestituted.Add(p.Renderer.GetInstanceID());
                    RestoreProp(p, null, "floor tile — a floor tile occludes nothing and never fades");
                    _floorHandedBack++;
                    _floorSessionHandedBack++;
                }
                _floorPurgeScratch.Clear();
            }
            if (_hidByEnable.Count == 0)
                return;
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
                                + (_floorSweeps > 0 ? 2 : 0));
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
                + $" (cap {FloorNameCap}), each with the term that identified it: {names}");

            _floorExamined = 0;
            _floorClassified = 0;
            _floorHandedBack = 0;
            _floorRefusedIds.Clear();
            _floorNames.Clear();
        }
    }
}
