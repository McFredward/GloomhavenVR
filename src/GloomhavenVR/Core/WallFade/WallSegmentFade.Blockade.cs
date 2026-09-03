using System.Collections.Generic;
using ScenarioRuleLibrary;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// DOORWAYS, BY THE GAME'S OWN LINE (ModBuild 413; user on 412, teilweises_faden.jpg:
/// <i>"Der Torbogen faded nun wieder. Das DARF NICHT SEIN. Der eingestürzte Bereich ist KEIN
/// Torbogen — das ist zwar eine eingestürzte Tür, aber nicht als solche zu betrachten … Ich will,
/// dass dieser Bereich als EIN einziges zusammenhängendes Stück inklusive der Kristalle entweder
/// komplett faded oder gar nicht."</i>).
///
/// <para><b>WHAT SEPARATES A TORBOGEN FROM A COLLAPSED DOOR IS NOT A NAME, A HEALTH BAR OR A
/// DISTANCE — IT IS WHETHER THE SCENARIO HAS A DOOR OBJECT FOR IT.</b> The 412 log holds FIVE
/// <c>ThickDoor : (guid)</c> roots against a census of <c>3 ORDINARY door(s)</c> and
/// <c>0 prop(s) configured for health</c>; the user: <i>"Es hat weder eine Health-Bar noch kann
/// man es angreifen … da ist eine zerstörte Tür drin und das ist, soviel ich sehe, reine
/// Dekoration."</i> The game's own door code says how it tells them apart:
/// <c>UnityGameEditorDoorProp.OnCursorEnter</c> binds the prop to
/// <c>ScenarioManager.CurrentScenarioState.DoorProps.OfType&lt;CObjectDoor&gt;().SingleOrDefault(s
/// =&gt; s.InstanceName == gameObject.name)</c>, and <c>CObjectProp.InstanceName</c> is
/// <c>PrefabName + " : (" + PropGuid + ")"</c> — the very GameObject name. A ThickDoor root the
/// scenario state names is a SCENARIO DOOR: the game opens it, it is a passage, and its whole
/// <c>ProceduralDoorway</c> assembly (frame, leaf, plate, gate wall, pillars, rocks, scatter —
/// everything under the subtree) never fades; its gate column stands solid with the arch
/// (ModBuild 412). A ThickDoor root the scenario state does NOT name — or a
/// <c>ProceduralDoorway</c> with no door prop at all — is DECORATION: a broken door placed as
/// scenery, and its whole assembly is ONE fade unit by subtree.</para>
///
/// <para><b>ALL OR NOTHING.</b> The 412 build clustered the collapsed area by a 0.5 wu XZ gap
/// into three units (<c>'CV_Pillar_Generic_01' 9 renderer(s) … 21 renderer(s) …
/// 'CV_Floor_Scatter_09 (11)' 24 renderer(s)</c>), each judged on its own coverage — the partial
/// fade in the screenshot. A decoration doorway is now ONE segment keyed on the doorway's own
/// transform: every mesh under the subtree is a member (size guards do not apply — a unit is
/// defined by the subtree; only the ground band, figures, non-occluders and water are excluded),
/// the map tile's shader-adopted crystal group members inside its footprint are pulled out of
/// that group and into the unit (<see cref="FadeDriver.PullTileGroupMembers"/>), and the
/// mounted riders inside/hugging the footprint follow. One coverage reading, one Schmitt
/// trigger, one dwell, one ramp. The door prop's own visual — the leaf/plate under the prop with
/// no PCG_ placement root below it, the same partition ActorPropBody uses — is left visible, as
/// the 407 build the user accepted did ("nur die Kristalle haben gefehlt").</para>
///
/// <para><b>COST.</b> One memoised ancestry walk per renderer parent on rescan
/// (<see cref="FadeDriver.DoorwayOf"/>), one scenario-state lookup per door prop per rescan;
/// nothing per frame.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class Segment
    {
        /// <summary>ModBuild 413: non-null on a decoration-doorway unit — the label the clause
        /// prints ('ThickDoor : (…)' or the doorway's own name) with the evidence term.</summary>
        public string? BlockadeLabel;
        /// <summary>How many renderers this unit pulled out of a shader-adopted tile group this
        /// rescan (the crystals).</summary>
        public int FreeStandingPulled;
    }

    private sealed partial class FadeDriver
    {
        private enum DoorwayKind : byte
        {
            /// <summary>The scenario state names this door prop: a passage the game opens.
            /// Never fades, whole assembly.</summary>
            ScenarioDoor,
            /// <summary>No scenario door object behind it: scenery. One unit by subtree.</summary>
            Decoration,
        }

        private readonly Dictionary<Transform, ProceduralDoorway?> _doorwayOfMemo = new(512);
        private readonly Dictionary<ProceduralDoorway, DoorwayKind> _doorwayKindMemo = new();
        private readonly Dictionary<ProceduralDoorway, string> _doorwayEvidence = new();
        private readonly Dictionary<ProceduralDoorway, List<MeshRenderer>> _blockadeMembers = new();
        private readonly List<ProceduralDoorway> _doorwayOrder = new();
        private readonly List<Transform> _doorwayWalkScratch = new(16);
        private readonly List<string> _doorwayCensus = new();
        private const int DoorwayCensusCap = 6;
        private int _censusBlockadeUnits;
        private int _censusBlockadeMembers;
        private int _censusBlockadePulled;
        private int _censusBlockadeVisualKept;
        private int _censusBlockadeBand;

        private void BeginDoorwayScan()
        {
            _doorwayOfMemo.Clear();
            _doorwayKindMemo.Clear();
            _doorwayEvidence.Clear();
            _blockadeMembers.Clear();
            _doorwayOrder.Clear();
            _doorwayCensus.Clear();
            _censusBlockadeUnits = 0;
            _censusBlockadeMembers = 0;
            _censusBlockadePulled = 0;
            _censusBlockadeVisualKept = 0;
            _censusBlockadeBand = 0;
        }

        /// <summary>The nearest <see cref="ProceduralDoorway"/> above a transform, memoised per
        /// node for the rescan — every node on a walked path is stored, so the whole scene costs
        /// one GetComponent per distinct transform.</summary>
        private ProceduralDoorway? DoorwayOf(Transform? t)
        {
            if (t == null)
                return null;
            if (_doorwayOfMemo.TryGetValue(t, out ProceduralDoorway? memo))
                return memo;
            _doorwayWalkScratch.Clear();
            ProceduralDoorway? found = null;
            Transform? node = t;
            while (node != null)
            {
                if (_doorwayOfMemo.TryGetValue(node, out ProceduralDoorway? cached))
                {
                    found = cached;
                    break;
                }
                _doorwayWalkScratch.Add(node);
                found = node.GetComponent<ProceduralDoorway>();
                if (found != null)
                    break;
                node = node.parent;
            }
            foreach (Transform walked in _doorwayWalkScratch)
                _doorwayOfMemo[walked] = found;
            return found;
        }

        /// <summary>The door prop a doorway belongs to: the parent's component (<c>ThickDoor :
        /// (guid)</c> → <c>HexDoor(Clone)</c>) or an immediate child — both shapes
        /// <c>ProceduralDoorway.ApplyVisibility</c> handles.</summary>
        private static UnityGameEditorDoorProp? DoorPropOf(ProceduralDoorway dw)
        {
            Transform t = dw.transform;
            UnityGameEditorDoorProp? dp = t.parent != null ? t.parent.GetComponent<UnityGameEditorDoorProp>() : null;
            if (dp != null)
                return dp;
            dp = t.GetComponent<UnityGameEditorDoorProp>();
            if (dp != null)
                return dp;
            for (int i = 0; i < t.childCount; i++)
            {
                dp = t.GetChild(i).GetComponent<UnityGameEditorDoorProp>();
                if (dp != null)
                    return dp;
            }
            return null;
        }

        /// <summary>The game's own test, lifted from <c>UnityGameEditorDoorProp.OnCursorEnter</c>
        /// (and <c>CObjectProp</c>'s activated-prop fallback): is there a scenario door object
        /// whose InstanceName is this prop's GameObject name?</summary>
        private static bool IsScenarioDoor(UnityGameEditorDoorProp dp, out string evidence)
        {
            string name = dp.gameObject.name;
            ScenarioState? state = null;
            try { state = ScenarioManager.CurrentScenarioState; }
            catch { /* rule library not ready — read as no state */ }
            if (state == null)
            {
                evidence = "no CurrentScenarioState to ask — read as DECORATION this rescan";
                return false;
            }
            int doorCount = 0;
            CObjectProp? match = null;
            try
            {
                List<CObjectProp>? doors = state.DoorProps;
                if (doors != null)
                {
                    doorCount = doors.Count;
                    foreach (CObjectProp p in doors)
                    {
                        if (p != null && p.InstanceName == name)
                        {
                            match = p;
                            break;
                        }
                    }
                }
                if (match == null && state.ActivatedProps != null)
                {
                    foreach (CObjectProp p in state.ActivatedProps)
                    {
                        if (p != null && p.ObjectType == ScenarioManager.ObjectImportType.Door
                            && p.InstanceName == name)
                        {
                            match = p;
                            break;
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                evidence = $"scenario door list unreadable ({e.GetType().Name}) — read as DECORATION this rescan";
                return false;
            }
            if (match != null)
            {
                evidence = $"named by ScenarioState.DoorProps ({doorCount} door object(s)) — type {dp.m_DoorType}, "
                    + $"lock {dp.m_LockType}, entrance {dp.m_IsDungeonEntrance}, exit {dp.m_IsDungeonExit}";
                return true;
            }
            evidence = $"no scenario door object named '{name}' among {doorCount} door object(s) — type "
                + $"{dp.m_DoorType}, lock {dp.m_LockType}, entrance {dp.m_IsDungeonEntrance}, exit {dp.m_IsDungeonExit}";
            return false;
        }

        private DoorwayKind ClassifyDoorway(ProceduralDoorway dw, out string evidence)
        {
            if (_doorwayKindMemo.TryGetValue(dw, out DoorwayKind kind))
            {
                evidence = _doorwayEvidence[dw];
                return kind;
            }
            UnityGameEditorDoorProp? dp = DoorPropOf(dw);
            string label = DoorwayLabel(dw);
            if (dp == null)
            {
                kind = DoorwayKind.Decoration;
                evidence = "no UnityGameEditorDoorProp above or beside the ProceduralDoorway — nothing the scenario could open";
            }
            else
            {
                kind = IsScenarioDoor(dp, out string why) ? DoorwayKind.ScenarioDoor : DoorwayKind.Decoration;
                evidence = why;
            }
            _doorwayKindMemo[dw] = kind;
            _doorwayEvidence[dw] = evidence;
            if (_doorwayCensus.Count < DoorwayCensusCap)
            {
                _doorwayCensus.Add(kind == DoorwayKind.ScenarioDoor
                    ? $"'{label}' — SCENARIO DOOR (never fades) [{evidence}]"
                    : $"'{label}' — DECORATION (one unit) [{evidence}]");
            }
            if (kind == DoorwayKind.Decoration && !_blockadeMembers.ContainsKey(dw))
            {
                _blockadeMembers[dw] = new List<MeshRenderer>();
                _doorwayOrder.Add(dw);
            }
            return kind;
        }

        private static string DoorwayLabel(ProceduralDoorway dw)
        {
            UnityGameEditorDoorProp? dp = DoorPropOf(dw);
            return dp != null ? dp.gameObject.name : dw.gameObject.name;
        }

        /// <summary>The door prop's own visual: under the prop with no PCG_ placement root
        /// between the renderer and the prop (ActorPropBody's partition — the leaf, the plate,
        /// the hinge, the sign). Stays visible on a decoration unit, like a figure would.</summary>
        private static bool IsDoorPropVisual(Renderer r, ProceduralDoorway dw)
        {
            UnityGameEditorDoorProp? dp = DoorPropOf(dw);
            if (dp == null)
                return false;
            Transform stop = dp.transform;
            bool underProp = false;
            for (Transform? node = r.transform; node != null; node = node.parent)
            {
                if (node == stop)
                {
                    underProp = true;
                    break;
                }
                if (node.name.StartsWith("PCG_", System.StringComparison.Ordinal))
                    return false;
            }
            return underProp;
        }

        /// <summary>
        /// THE DOORWAY PASS — runs at the head of the unit lane, before the XZ candidate sweep.
        /// Every mesh under a ProceduralDoorway is sorted by its doorway's kind: a scenario door's
        /// content is left alone (the sweep, the riders and the hanging-plant pass refuse it
        /// through <see cref="IsDoorwayAssembly"/>); a decoration doorway's content becomes the
        /// member list of ONE unit, formed by <see cref="FormBlockadeUnits"/>.
        /// </summary>
        private void CollectBlockadeMembers()
        {
            for (int fi = 0; fi < _factCount; fi++)
            {
                ref RendererFact f = ref _facts[fi];
                if (f.Mesh == null || f.Mod)
                    continue;
                MeshRenderer r = f.Mesh!;
                if (r == null)
                    continue;
                ProceduralDoorway? dw = DoorwayOf(r.transform.parent != null ? r.transform.parent : r.transform);
                if (dw == null)
                    continue;
                if (ClassifyDoorway(dw, out _) != DoorwayKind.Decoration)
                    continue;
                if (f.Figure || IsFigureOrActorRenderer(r))
                    continue; // never touched
                if (f.WaterSurface || IsWaterProtected(r.bounds))
                    continue;
                if (!r.enabled && !_mountedTouched.ContainsKey(r))
                    continue; // the GAME disabled it — not ours
                if (IsNonOccludingRenderer(r))
                    continue; // hides nothing, never fades
                if (IsDoorPropVisual(r, dw))
                {
                    _censusBlockadeVisualKept++;
                    continue; // the leaf/plate stays visible, as in the accepted 407 state
                }
                _blockadeMembers[dw].Add(r);
            }
        }

        /// <summary>One unit per decoration doorway: union of the subtree, the ground band
        /// stripped against the unit's own room floor, the engulf guard kept, then the shared
        /// fill (<see cref="FillFreeStandingUnit"/>) with the doorway transform as the anchor.</summary>
        private void FormBlockadeUnits()
        {
            foreach (ProceduralDoorway dw in _doorwayOrder)
            {
                if (dw == null || !_blockadeMembers.TryGetValue(dw, out List<MeshRenderer>? all)
                    || all.Count == 0)
                {
                    continue;
                }
                Bounds union = default;
                bool have = false;
                foreach (MeshRenderer m in all)
                {
                    if (m == null)
                        continue;
                    Bounds b = m.bounds;
                    if (!have) { union = b; have = true; }
                    else union.Encapsulate(b);
                }
                if (!have)
                    continue;
                int room = NearestRoomFor(union);
                if (room < 0 || !RoomDecisionValid(room))
                {
                    _censusFreeRefusedNoRoom++;
                    continue;
                }
                float floorY = _live.RoomFloorY[room];
                float band = floorY + GroundExclusionHeightWU;
                float foot = floorY + GroundExclusionHeightWU;
                _freeMembers.Clear();
                int floorFooted = 0;
                union = default;
                have = false;
                foreach (MeshRenderer m in all)
                {
                    if (m == null)
                        continue;
                    Bounds b = m.bounds;
                    if (b.max.y <= band)
                    {
                        _censusBlockadeBand++;
                        continue; // the ground band stays solid — the floor slab, the floor scatter
                    }
                    if (b.min.y < foot)
                        floorFooted++;
                    _freeMembers.Add(m);
                    if (!have) { union = b; have = true; }
                    else union.Encapsulate(b);
                }
                if (!have)
                    continue;
                if (InsideRoomFraction(union, room) >= EngulfSampleFraction)
                {
                    _censusFreeRefusedEngulf++;
                    continue;
                }
                string label = DoorwayLabel(dw);
                Segment? seg = FillFreeStandingUnit(_freeMembers, union, floorFooted, dw.transform,
                    label + " [" + _doorwayEvidence[dw] + "]");
                if (seg == null)
                    continue;
                _censusBlockadeUnits++;
                _censusBlockadeMembers += _freeMembers.Count;
                _censusBlockadePulled += seg.FreeStandingPulled;
            }
        }

        /// <summary>The doorway census for the clause: every doorway this rescan classified, its
        /// kind and the evidence term, plus the blockade totals. A readable zero.</summary>
        private string DoorwayCensusClause()
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append(" DOORWAYS (ModBuild 413 — by the game's line: a ThickDoor root the scenario ")
              .Append("state names in DoorProps is a SCENARIO DOOR whose whole assembly never fades; ")
              .Append("one it does not name, or a ProceduralDoorway with no door prop, is DECORATION ")
              .Append("and ONE unit by subtree): ");
            if (_doorwayKindMemo.Count == 0)
            {
                sb.Append("no ProceduralDoorway above any mesh this rescan");
            }
            else
            {
                sb.Append(_doorwayKindMemo.Count).Append(" doorway(s) — ")
                  .Append(string.Join(" | ", _doorwayCensus));
                if (_doorwayKindMemo.Count > _doorwayCensus.Count)
                    sb.Append(" | +").Append(_doorwayKindMemo.Count - _doorwayCensus.Count).Append(" more");
                sb.Append(". BLOCKADE UNITS formed: ").Append(_censusBlockadeUnits)
                  .Append(" (").Append(_censusBlockadeMembers).Append(" member(s) by subtree, ")
                  .Append(_censusBlockadePulled).Append(" crystal(s) pulled from the tile group, ")
                  .Append(_censusBlockadeVisualKept).Append(" door-visual renderer(s) kept visible, ")
                  .Append(_censusBlockadeBand).Append(" ground-band renderer(s) left solid)");
            }
            // The ModBuild 412 terms are named here so a grep for them still lands: they are
            // retired, not lost — a scenario door's leaf and frame are refused with its whole
            // assembly, a decoration's are members of its unit.
            sb.Append(". [ModBuild 412's 'DOOR VISUAL' and 'ARCH/FRAME BY NAME' terms are retired: ")
              .Append("no name test decides a doorway any more]");
            sb.Append('.');
            return sb.ToString();
        }
    }
}
