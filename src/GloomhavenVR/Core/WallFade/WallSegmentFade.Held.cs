using System.Collections.Generic;
using GloomhavenVR.Board.FigureGrab;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// A PROP IN A HAND NEVER FADES — the third standing "this can never occlude" rule of this
/// subsystem, beside FIGURES NEVER FADE (round 7) and FLOOR NEVER FADES
/// (<see cref="WallFloorTile"/>), and written to the same shape as the second of those.
///
/// <para><b>THE RULING</b> (user, 2026-09-06): <i>"Props die in der eigenen Hand oder in der Hand
/// von Mitspielern sind sollen nicht faden - ich hatte den Fall das ein Baum beim Test wegfaded
/// ist. Dinge in der Hand sollen pauschal als niemals sichtblockierend gelten."</i> Both hands
/// count - this client's and a peer's - and the rule is categorical: held-ness is not weighed
/// against coverage, it ends the question.</para>
///
/// <para><b>WHAT WAS ALREADY THERE, AND WHY IT DID NOT HOLD.</b> The exemption itself is not new:
/// <c>FigureRendererGuard.HeldByPlayer</c> (ModBuild 340, remote term 2026-09-05) already answers
/// "is this renderer in ANY hand", local list first and <see cref="NetHeldProps"/> second, and
/// <c>IsFigureOrActorRenderer</c> asks it BEFORE its ancestry memo exactly as the round-340
/// lesson requires. Ten adoption sites in this subsystem call that guard. The ModBuild 461 host
/// log names the site that does not:</para>
/// <code>
/// [WallSegmentFade] FADE WRITE: ... 'PR_Tree_3Hex_Leafless'[mesh] under
///   'L : (966d4052-…)/ThreeHexObstacle : (f8c8bc14-…)/Generated Content/PR_Tree_3Hex_02_PR'
///   anchor 2.92 over floor, AABB c(-7.1,4.6,1.1) ← asset sibling[wall 'L : (966d4052-…)']
///   [prop unit] of 'FR_Stones_10' fade 1.00
/// </code>
/// <para>Eighteen FADE WRITE lines name that renderer in that session and <b>every one of them</b>
/// attributes it to the <c>asset sibling</c> lane - <c>CollectAdoptedSiblings</c>
/// (WallSegmentFade.cs), which hand-rolls its exclusions (<c>ProceduralWall</c>,
/// <c>ActorBehaviour</c>, <c>TileBehaviour</c>, <c>UnityGameEditorDoorProp</c>, floor band, water)
/// and asks the figure guard nowhere. Its own doc already confessed half of this - "this pass
/// hand-rolls three of that predicate's terms and omits the Animator one" - and the term it also
/// omits is the held one. The prop is <c>f8c8bc14</c>, i.e. <c>[Net] [Props] HELD-PROP MIRROR
/// player 2 prop 616057776 'ThreeHexObstacle' in their RIGHT hand</c>: a TREE, in a PEER's hand,
/// dissolved to 1.00 on the host's screen, and <c>anchor 2.92 over floor</c> is the hand.</para>
///
/// <para><b>AND IT IS NOT ONLY THE OBSERVER'S SIDE. BOTH MACHINES DID IT, WHICH IS THE BIGGER
/// HALF OF THIS FINDING.</b> The obvious reading of the report - "a peer's held prop is a mirrored
/// object and the observer cannot know it is held" - is wrong, and the co-player's own ModBuild
/// 461 log falsifies it in one line. During his THIRD hold of that same tree
/// (<c>ThreeHexObstacle : (f8c8bc14-…)</c>, a LOCAL hold on his machine, 1977 frames):</para>
/// <code>
/// [FigureGrab] [Props] HOLD WATCH for 'ThreeHexObstacle' Obstacle (released — glide home) over
///   1977 frame(s): 60 enable/disable TRANSITION(s) across 8 renderer(s); worst
///   'PR_Tree_3Hex_Leafless' flipped 12x and drew on 1673 of 1977 frame(s); 1584 frame(s) with
///   EVERYTHING drawing … sameInstance=8/8 … rootAlive=True, lost=0, appeared=0 … rebuilt=0,
///   dark=0 of 1974, apparanceFrozen=1
/// </code>
/// <para>Read by that line's own key: <c>rebuilt=0</c> with <c>apparanceFrozen=1</c> excludes the
/// Apparance rebuild, and <c>sameInstance=8/8</c> with <c>lost=0, appeared=0</c> excludes
/// re-instantiation — "at full with transitions climbing it is a plain write war on stable
/// renderers and the fix is an ownership guard on the writer". The tree in the OWNER'S OWN HAND
/// was undrawn on 304 of 1977 frames. His first two holds of the same prop (774 and 884 frames)
/// read <c>EVERYTHING drawing</c> on every frame, so this is a hold that crossed a fading wall,
/// not a broken registry. The only <c>Renderer.enabled = false</c> in this subsystem is
/// <c>HideByEnable</c>, which is write primitive 3. So the local exemption was NOT holding either
/// and the user has simply been looking at the peer's tree; a fix that only taught the observer
/// about a remote hold would have left half the defect in place and passed its own test.</para>
///
/// <para><b>SO THE REFUSAL SITS ON THE WRITES, NOT ON THE LANE</b> - the identical argument
/// <see cref="WallFloorTile"/>'s driver half makes, and this round is its evidence. A term added
/// to <c>CollectAdoptedSiblings</c> would fix the lane the log happens to name and leave the next
/// lane to be written without one; ten call sites of the guard were not enough to stop the
/// eleventh. There are exactly four write primitives, verified by the floor rule's own audit and
/// re-verified here, and this rule is now on all four:</para>
/// <list type="number">
/// <item><c>Apply</c>'s <c>seg.Renderers</c> loop (WallSegmentFade.cs) - the wall MPB write.</item>
/// <item><c>DriveProp</c> (WallSegmentFade.Mounted.cs) - every dressing MPB write, and the lane
/// the tree actually came through: Mounted, Stacked, Body, prop-unit dressing, foliage, asset
/// siblings and the corner pieces all deliver through it.</item>
/// <item><c>HideByEnable</c> (WallSegmentFade.HoldQuery.cs) - the only
/// <c>Renderer.enabled = false</c> in the subsystem.</item>
/// <item><c>EnsureDissolveChannel</c> (WallSegmentFade.Dissolve.cs) - the material swap.</item>
/// </list>
///
/// <para><b>REFUSAL IS NOT RESTORATION, and here the gap is measured in the length of a hold.</b>
/// The tree was adopted while it STOOD on the board, faded to 1.00 there, and only then picked
/// up: a guard that merely refuses the next write leaves an invisible tree in a hand until the
/// next rescan, which on this rig is up to two seconds of exactly the picture the user reported.
/// So <see cref="FadeDriver.HeldNeverFades"/>'s refusal inside <c>DriveProp</c> hands the piece
/// back on the same frame, through <c>RestoreProp</c> and therefore through the enable ledger -
/// a renderer the GAME switched off is still never switched on by us. The hand-back is
/// EDGE-GATED ON THE PICTURE and not on a per-scene one-shot (the ModBuild 430 floor mistake):
/// a piece that is already as authored costs one <c>HasPropertyBlock()</c> call and writes
/// nothing, so a hold that lasts 16,000 frames produces one hand-back, not 16,000 ownership-churn
/// transitions.</para>
///
/// <para><b>HELD-NESS IS NEVER MEMOISED</b> (the ModBuild 340 lesson, restated because this file
/// is a new place to forget it): every other term in the figure guard is a property of the
/// ancestor CHAIN, which is what makes an ancestry memo exact. Held-ness is not - the same
/// renderer under the same parents answers differently one frame later. The only memo in this
/// file is the FALSIFIER's "is this a grabbable board prop", which IS a property of the chain.</para>
///
/// <para><b>MULTIPLAYER.</b> No wire field, and none is needed: record 37 (held props) has
/// carried a peer's hold since 2026-09-05 and <see cref="NetHeldProps"/> is its receive side, so
/// both clients can already answer "is this in a hand" from data each of them has. What was
/// missing was a lane asking. The rule is local presentation only, like the whole wall system,
/// and it is symmetric by construction: the owner and every observer refuse the same write for
/// the same renderer, which is what the 1:1 ruling requires of a fade.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        // ---- the rule ------------------------------------------------------------------------

        /// <summary>The reason string every held hand-back carries into <c>RestoreProp</c>, so
        /// the ownership-churn tripwire can tell this release from an un-fade.</summary>
        private const string HeldRestoreReason =
            "held in a hand - a prop in a hand is never sight-blocking (user 2026-09-06)";

        /// <summary>
        /// MUST THIS RENDERER NEVER BE FADED BECAUSE IT IS IN SOMEBODY'S HAND? Consulted by every
        /// write primitive listed in the file header.
        ///
        /// <para>ONE TERM, and it is deliberately the SAME term the ten adoption sites already
        /// ask: <c>HeldProps.OwnsRendererOf</c>, which is the local hold list OR
        /// <see cref="NetHeldProps"/>. A second, independent definition of "held" is how a mirror
        /// maintained by hand drifts, and this subsystem has already paid for one of those
        /// (<c>FigureRendererGuard</c>'s own header: ModBuild 340 stepped the wall copy of the
        /// clause list and not the MixedReality copy).</para>
        ///
        /// <para>Steady-state cost is one <c>List.Count</c> compare and one <c>HashSet.Count</c>
        /// compare - nothing is held on almost every frame of a session, and this is asked on the
        /// per-frame write path. While a prop IS held it is an ancestor walk against at most four
        /// visual roots, terminating at the scene root.</para>
        /// </summary>
        private bool HeldNeverFades(Renderer? r)
        {
            _heldExamined++;
            if (r == null)
                return false;
            if (HeldProps.Count == 0 && !NetHeldProps.Any)
            {
                // NOBODY IS HOLDING ANYTHING THIS MOD KNOWS ABOUT - which is precisely when the
                // falsifier has to keep counting. "The rule saw no hold" and "there was no hold"
                // are the two readings this line must never let a reader confuse, and only the
                // leak probe below can separate them: it counts over the GRABBABLE population,
                // not over the held one. See NoteHeldLeakCandidate.
                NoteHeldLeakCandidate(r);
                return false;
            }
            _heldExaminedWhileHolding++;
            if (!HeldProps.OwnsRendererOf(r.transform))
            {
                NoteHeldLeakCandidate(r);
                return false;
            }
            int id = r.GetInstanceID();
            if (_heldRefusedIds.Add(id))
            {
                _heldSessionRefused++;
                // The sentence is built ONLY for the rows the capped line will print - on net472
                // every interpolated string is a string.Format(string, object[]) and this sits on
                // the per-frame write path.
                if (_heldNames.Count < HeldNameCap)
                {
                    _heldNames.Add("'" + r.name + "' in "
                        + (HeldProps.LocalOwnsRendererOf(r.transform)
                            ? "THIS client's hand (HeldProps)"
                            : "a PEER's hand (NetHeldProps, wire record 37)"));
                }
            }
            return true;
        }

        /// <summary>
        /// HAND ONE PIECE BACK because it is in a hand - the restitution half, called from the
        /// refusal inside <c>DriveProp</c>. Returns true when it actually wrote something.
        ///
        /// <para><b>THE LATCH IS THE PICTURE.</b> Every term below is read off the RENDERER and
        /// off our own ledgers for it, never off a "have I already done this" set: the ModBuild
        /// 430 floor restitution used a per-scene one-shot, spent it on the first piece it saw
        /// whether or not that piece was faded, and left nothing for the one that went dark
        /// afterwards. A piece that is already as authored answers false to all four terms, so a
        /// hold of any length produces exactly one hand-back per piece and feeds the
        /// ownership-churn tripwire exactly one transition.</para>
        ///
        /// <para>The property block is cleared UNCONDITIONALLY after the restore, and that is
        /// load-bearing rather than tidy: <c>RestoreProp</c> clears it only when the piece itself
        /// carried one of our channels, so a block written by write primitive 1 would leave
        /// <c>HasPropertyBlock()</c> true, the latch dirty and this method firing every frame -
        /// the churn shape this repository has an entry for.</para>
        /// </summary>
        private bool RestoreHeldProp(MountedProp p, Renderer r)
        {
            if (!(p.Driven || p.SwapCopies != null || IsHeldHidden(r) || r.HasPropertyBlock()))
                return false;
            RestoreProp(p, null, HeldRestoreReason);
            r.SetPropertyBlock(null);
            _heldHandedBack++;
            _heldSessionHandedBack++;
            if (_heldHandBackNames.Count < HeldNameCap)
                _heldHandBackNames.Add("'" + r.name + "'");
            return true;
        }

        // ---- the falsifier -------------------------------------------------------------------

        /// <summary>
        /// COUNT THE WRITES THIS RULE DID <b>NOT</b> REFUSE THAT LOOK LIKE THE DEFECT ANYWAY.
        ///
        /// <para><b>WHY IT MAY NOT BE BUILT ON THE HELD REGISTRY.</b> The rule's own term is
        /// "<c>HeldProps.OwnsRendererOf</c> says yes". A counter built on that term agrees with
        /// the rule by construction and reads ZERO in exactly the case the rule is blind - a hold
        /// this mod does not know about, a peer whose record 37 never arrived, a prop whose visual
        /// was re-keyed mid-hold. That is a claim measuring itself, and this repository's ledger
        /// has an entry for it. So the population here is a DIFFERENT one:
        /// <c>PropGrab.OwnsRendererOf</c> - every renderer of every board prop that is grabbable
        /// at all, held or not - narrowed by GEOMETRY: airborne by more than
        /// <see cref="HeldAirborneWU"/> world units over the nearest anchored room floor. A
        /// grabbable prop does not levitate on its own; a prop that is 1 wu off the floor is in a
        /// hand whatever any registry says.</para>
        ///
        /// <para>The MEMO is the chain half only ("is this renderer part of a grabbable prop"),
        /// which is a property of the ancestry and therefore cacheable. The airborne half is
        /// re-measured every time, because it is exactly what moves.</para>
        /// </summary>
        private void NoteHeldLeakCandidate(Renderer r)
        {
            int id = r.GetInstanceID();
            if (_heldPropMemo.TryGetValue(id, out byte cached))
            {
                if (cached != HeldMemoIsProp)
                    return;
            }
            else
            {
                if (IsModObject(r))
                {
                    _heldPropMemo[id] = HeldMemoNotProp;
                    return;
                }
                bool isProp = PropGrab.OwnsRendererOf(r.transform);
                _heldPropMemo[id] = isProp ? HeldMemoIsProp : HeldMemoNotProp;
                if (!isProp)
                    return;
            }
            Bounds b = r.bounds;
            if (!NearestAnchoredFloorY(b.min.y, out float floorY))
                return; // no anchored room yet - undecidable, and never guessed
            float foot = b.min.y - floorY;
            if (foot < HeldAirborneWU)
                return; // standing on the board: a fade on it is a different question
            if (!_heldLeakIds.Add(id))
                return;
            _heldSessionLeaked++;
            if (_heldLeakNames.Count < HeldNameCap)
                _heldLeakNames.Add("'" + r.name + "' foot " + foot.ToString("0.00")
                    + " wu over the floor");
        }

        // ---- window state ----------------------------------------------------------------------

        /// <summary>How far off the room floor a grabbable prop has to be before the falsifier
        /// calls it airborne. The subsystem's own airborne rule for wall-mounted dressing is
        /// 1.0 wu and this is the same number on purpose: a threshold invented here would be a
        /// second definition of "off the ground" in one file.</summary>
        private const float HeldAirborneWU = 1.0f;

        private const int HeldNameCap = 6;
        private const float HeldCensusIntervalSeconds = 2f;
        private const float HeldCensusHeartbeatSeconds = 30f;

        private const byte HeldMemoNotProp = 1;
        private const byte HeldMemoIsProp = 2;

        /// <summary>Renderer <c>GetInstanceID</c> -&gt; "is this part of a grabbable board prop".
        /// The CHAIN half of the falsifier only; the held verdict itself is never cached. Cleared
        /// with the census window, so a prop registered or destroyed mid-session is picked up
        /// within <see cref="HeldCensusIntervalSeconds"/>.</summary>
        private readonly Dictionary<int, byte> _heldPropMemo = new(512);

        /// <summary>Distinct renderers REFUSED this window because they are in a hand. A set and
        /// not a counter: the guard is asked many times per frame for the same renderer, and
        /// "how many renderers were refused" must not become "how many writes were attempted".
        /// That second number is <see cref="_heldExamined"/>.</summary>
        private readonly HashSet<int> _heldRefusedIds = new(32);

        /// <summary>Distinct renderers the falsifier flagged this window - see
        /// <see cref="NoteHeldLeakCandidate"/>. A NON-ZERO value here is the whole point of the
        /// line: a grabbable prop, airborne, faded anyway.</summary>
        private readonly HashSet<int> _heldLeakIds = new(32);

        private readonly List<string> _heldNames = new();
        private readonly List<string> _heldLeakNames = new();
        private readonly List<string> _heldHandBackNames = new();

        /// <summary>How many times the guard was ASKED this window - i.e. how many fade writes
        /// were attempted at all. <c>examined 4213, refused 0</c> and <c>examined 0</c> are
        /// completely different reports and neither of them is "the rule is not wired".</summary>
        private int _heldExamined;

        /// <summary>...and how many of those asks happened while a hand actually held something.
        /// THE FIELD THAT SEPARATES THE TWO NULL READINGS: <c>refused 0</c> with this at 0 means
        /// nothing was ever held while anything was fading (the rule had no opportunity), and
        /// <c>refused 0</c> with this above 0 means the rule was asked about a live hold and
        /// answered no every time - which is the defect, not the steady state.</summary>
        private int _heldExaminedWhileHolding;

        /// <summary>Frames this window on which at least one prop was in a hand.</summary>
        private int _heldFrames;

        /// <summary>Pieces handed back to authored this window by <see cref="RestoreHeldProp"/>.</summary>
        private int _heldHandedBack;

        private int _heldSessionRefused;
        private int _heldSessionHandedBack;
        private int _heldSessionLeaked;
        private int _heldPeakLocal;
        private int _heldPeakRemote;

        private float _nextHeldCensus;
        private float _nextHeldHeartbeat;
        private int _heldCensusSig;

        /// <summary>Everything this rule remembers about a SCENE: the chain memo names renderers
        /// that died with the old scene.</summary>
        private void ResetHeldSceneState() => _heldPropMemo.Clear();

        // ---- the census ------------------------------------------------------------------------

        /// <summary>
        /// THE HELD-PROP LINE. Change-triggered, with a <see cref="HeldCensusHeartbeatSeconds"/>
        /// heartbeat so a steady picture still proves the rule is wired. Called once per tick
        /// beside <c>LogFloorGuardCensus</c>; the population sample at the top is taken on EVERY
        /// call, before the cadence gate, because the whole point of the line is that "no prop
        /// faded" and "no prop was held" must be different readings.
        /// </summary>
        private void LogHeldGuardCensus(float now)
        {
            int localNow = HeldProps.Count;
            int remoteNow = NetHeldProps.Count;
            if (localNow > 0 || remoteNow > 0)
                _heldFrames++;
            if (localNow > _heldPeakLocal)
                _heldPeakLocal = localNow;
            if (remoteNow > _heldPeakRemote)
                _heldPeakRemote = remoteNow;

            if (now < _nextHeldCensus)
                return;
            _nextHeldCensus = now + HeldCensusIntervalSeconds;
            int sig = unchecked(_heldRefusedIds.Count * 397 + _heldHandedBack * 31
                                + _heldLeakIds.Count * 7919
                                + (_heldFrames > 0 ? 1 : 0)
                                + (_heldExamined > 0 ? 2 : 0)
                                + (_heldExaminedWhileHolding > 0 ? 4 : 0)
                                + ((localNow + remoteNow) << 8));
            bool heartbeat = now >= _nextHeldHeartbeat;
            if (sig == _heldCensusSig && !heartbeat)
                return;
            _heldCensusSig = sig;
            _nextHeldHeartbeat = now + HeldCensusHeartbeatSeconds;

            var names = new System.Text.StringBuilder();
            foreach (string n in _heldNames)
            {
                if (names.Length > 0)
                    names.Append("; ");
                names.Append(n);
            }
            if (names.Length == 0)
                names.Append("none");

            var back = new System.Text.StringBuilder();
            foreach (string n in _heldHandBackNames)
            {
                if (back.Length > 0)
                    back.Append("; ");
                back.Append(n);
            }
            if (back.Length == 0)
                back.Append("none");

            var leaks = new System.Text.StringBuilder();
            foreach (string n in _heldLeakNames)
            {
                if (leaks.Length > 0)
                    leaks.Append("; ");
                leaks.Append(n);
            }
            if (leaks.Length == 0)
                leaks.Append("none");

            // HW-VERIFY
            VRLog.Note(Name,
                $"HELD NEVER FADES: {localNow} prop(s) in THIS client's hands and {remoteNow} in "
                + $"peers' hands right now (session peak {_heldPeakLocal} local / "
                + $"{_heldPeakRemote} remote); a hand held something on {_heldFrames} tick(s) this "
                + $"window. The rule was ASKED {_heldExamined} time(s) by the four write "
                + $"primitives, {_heldExaminedWhileHolding} of them while a hold was live, "
                + $"REFUSED {_heldRefusedIds.Count} distinct renderer(s) at the write and HANDED "
                + $"BACK to authored {_heldHandedBack}. Session totals: {_heldSessionRefused} "
                + $"refused, {_heldSessionHandedBack} handed back, {_heldSessionLeaked} flagged by "
                + $"the falsifier. THE RULE (user 2026-09-06): 'Props die in der eigenen Hand oder "
                + $"in der Hand von Mitspielern sind sollen nicht faden … Dinge in der Hand sollen "
                + $"pauschal als niemals sichtblockierend gelten' - so held-ness is asked at the "
                + $"four write primitives (the wall MPB loop, DriveProp, HideByEnable, "
                + $"EnsureDissolveChannel) and never on a lane, because the lane that faded a tree "
                + $"in a peer's hand on ModBuild 461 was the ELEVENTH lane and the other ten all "
                + $"asked the guard. HOW TO READ THE NULLS, and there are three different ones: "
                + $"'asked 0' means no lane tried to fade anything at all this window; 'asked > 0 "
                + $"but while-holding 0' means nothing was in a hand while anything was fading, so "
                + $"the rule had no opportunity and this window proves nothing about it; "
                + $"'while-holding > 0 with refused 0' is the DEFECT - the rule was asked about a "
                + $"live hold and said no every time. Named {_heldNames.Count} of "
                + $"{_heldRefusedIds.Count}, dropped "
                + $"{(_heldRefusedIds.Count > _heldNames.Count ? _heldRefusedIds.Count - _heldNames.Count : 0)}"
                + $" (cap {HeldNameCap}), each with the hand that granted it: {names}. Handed back: "
                + $"{back}. | AND THE FALSIFIER, WHICH DOES NOT ASK THE HELD REGISTRY AT ALL: "
                + $"{_heldLeakIds.Count} distinct renderer(s) of a GRABBABLE board prop "
                + $"(PropGrab's registry, not HeldProps) were written to a non-zero fade this "
                + $"window while sitting more than {HeldAirborneWU:0.0} wu off the nearest "
                + $"anchored room floor - a grabbable prop does not levitate, so an airborne one "
                + $"is in a hand whatever any registry says, and a non-zero count here is a hold "
                + $"this mod could not see (a peer whose record 37 never arrived, a prop re-keyed "
                + $"mid-hold, a hand nothing registered). This is deliberately measured on a "
                + $"DIFFERENT population from the rule it tests: a counter built on "
                + $"HeldProps.OwnsRendererOf would agree with the refusal by construction and read "
                + $"zero in exactly the case the rule is blind. Named {_heldLeakNames.Count} of "
                + $"{_heldLeakIds.Count}, dropped "
                + $"{(_heldLeakIds.Count > _heldLeakNames.Count ? _heldLeakIds.Count - _heldLeakNames.Count : 0)}"
                + $" (cap {HeldNameCap}): {leaks}. THE EVIDENCE THIS RULE WAS BUILT FROM, AND IT IS "
                + $"TWO LOGS. HOST (ModBuild 461): eighteen FADE WRITE lines name "
                + $"'PR_Tree_3Hex_Leafless' under 'ThreeHexObstacle : (f8c8bc14-…)' at fade 1.00, "
                + $"all eighteen attributed to the 'asset sibling' lane, while [Net] [Props] "
                + $"HELD-PROP MIRROR had that same prop (616057776) in player 2's RIGHT hand and "
                + $"the tree's anchor read 2.92 wu over the floor. CO-PLAYER (same session, same "
                + $"build), during his own LOCAL hold of that prop: 'HOLD WATCH … over 1977 "
                + $"frame(s): 60 enable/disable TRANSITION(s) … worst PR_Tree_3Hex_Leafless "
                + $"flipped 12x and drew on 1673 of 1977 frame(s) … sameInstance=8/8, rebuilt=0, "
                + $"apparanceFrozen=1'. BOTH SIDES HID IT, so this is not a mirror losing "
                + $"held-ness: the local exemption was not holding either, and a fix aimed only at "
                + $"the observer would have passed its own test with half the defect shipped.");

            _heldExamined = 0;
            _heldExaminedWhileHolding = 0;
            _heldFrames = 0;
            _heldHandedBack = 0;
            _heldRefusedIds.Clear();
            _heldLeakIds.Clear();
            _heldNames.Clear();
            _heldLeakNames.Clear();
            _heldHandBackNames.Clear();
            _heldPropMemo.Clear();
        }
    }
}
