using System.Collections.Generic;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// WHICH RENDERERS THIS MOD IS ACTUALLY FADING, BY NAME — the census that ends the guessing about
/// the decapitated skeleton.
///
/// <para>THE REPORT, fourth round (2026-08-19, hardware, ModBuild 167, verbatim): <i>"Der Schädel
/// ist immer noch nicht sichtbar."</i> The photograph is <c>.planning/debug/skelet.jpg</c>: a full
/// human skeleton slumped on a WOODEN DECK at floor level, leaning back against a low masonry wall,
/// legs splayed across the planks. Ribcage, clavicles, arms, pelvis and legs solid and fully drawn;
/// THE SKULL GONE, a stub of vertebrae above the ribcage; and the low wall directly behind it
/// MID-DISSOLVE, its top edge carrying the ragged noise pattern of the masonry fade.</para>
///
/// <para>WHY THIS FILE EXISTS AT ALL, and it is the deliverable of this round rather than the fix.
/// Four rounds have now been spent on this defect without anyone being able to name the renderer
/// that disappears. In the ModBuild-167 log the <c>PROP UNIT</c> pass printed ZERO lines — it never
/// fired — and the <c>CR_OS_Skeleton_Statue_*</c> renderers the last two rounds were built around
/// are anchored 3.5 and 5.1 wu above the floor with their owners in agreement, i.e. wall statues
/// somewhere else in the level. Nothing named Skull, Bone or Skeleton appears at floor level in any
/// census the mod prints. So the object we are asked to protect has never once been NAMED by a log
/// line, and every round has therefore had to argue from a photograph. That is what this census
/// stops.</para>
///
/// <para>WHAT IT PRINTS, and every field of it is an OUTCOME. It runs AFTER the frame's
/// <c>Apply</c> pass, so it reports renderers this mod has actually written a non-zero fade onto —
/// not renderers it intends to. (ModBuild 164 shipped <c>MESH SWAP: no film mesh handled yet</c>,
/// a line that could only ever print its own initialiser, and it cost a whole round.) For each such
/// renderer: its name, its renderer type, a few levels of ancestor path, its anchor height over the
/// nearest anchored room floor (AABB <c>min.y</c>, the same anchor the mounted census prints), its
/// AABB centre and size, WHICH PATH wrote it — wall renderer, split segment, foliage, asset
/// sibling, wall body mesh, stacked shell, mounted dressing, shared corner piece, and whether the
/// prop-unit pass is what put it there — and the fade its owning segment is carrying.</para>
///
/// <para>SORTED SMALLEST FIRST, because a skull is small. The cap is a real cap and the line says
/// how many rows it dropped; what it must never do is silently truncate the one interesting case,
/// which is exactly what a "first N in dictionary order" census does. TORN UNITS COME FIRST
/// regardless of size: a unit is torn when a fade path wrote SOME of a prop's renderers and left
/// the rest solid, and that is precisely the shape of the photograph — skull gone, ribcage and legs
/// still there. No previous census printed that comparison, which is why four rounds of logs could
/// not settle the question.</para>
///
/// <para>SO DO SPLIT-OWNER UNITS, since ModBuild 258 — the SECOND shape a prop tears in, and one
/// this line could state only by accident before. <c>'CA_ICY_WallLight'</c> put its ice meshes on
/// <c>wall renderer of 'Wall 4'</c> and its blue torch emitters on <c>mounted dressing of
/// 'Wall 1'</c>: two owners, two independent fades, so whichever wall went first the other half of
/// the wall light stayed lit in mid-air (<c>wandproblem3.jpg</c>, <i>"die blaue Flamme ist nun
/// wieder sichtbar ohne dass sie gefaded ist"</i>). Such a unit can be 9/9 written and therefore
/// NOT torn, so without its own term the six-unit cap drops it by size. See
/// <see cref="FadeDriver.FadeUnit.OwnerSplit"/>, and <c>WallSegmentFade.Mounted.cs</c>'s
/// unit-affinity rule, which is what the count is meant to hold at zero.</para>
///
/// <para>HOW A PROP IS GROUPED: ModBuild 167's walk, <see cref="FadeDriver.PropUnitRootOf"/> — the
/// highest still prop-sized ancestor, stopping dead at a segment anchor, at anything carrying or
/// containing a <c>ProceduralWall</c>, and at the first container-scale subtree. A renderer with no
/// such unit is reported as a unit of one, which cannot be torn and says so.</para>
///
/// <para>LOG HYGIENE. Rate-limited AND change-triggered on a signature over the written set and
/// their quantised fades: while nothing changes it prints nothing, and the expensive half (the
/// ancestor walks, the union bounds, the sort, the strings) only runs on a frame where the
/// signature actually moved. The cheap half is a walk of the segment table's lists, which the
/// module already does several times per rescan.</para>
///
/// <para>MULTIPLAYER: a diagnostic. It writes no renderer, no material, no segment and no wire
/// record, and suppressing it cannot change a pixel.</para>
///
/// <para>STEREO: nothing per-eye enters it; it reads CPU-side lists once per frame, after both eyes
/// have been given the same property block. <c>.planning/wall-fade-stereo-rivalry.md</c> (parked)
/// is untouched.</para>
/// </summary>
internal static partial class WallSegmentFade
{
    private sealed partial class FadeDriver
    {
        /// <summary>Shortest gap between two FADE WRITE lines. Two seconds is the rescan cadence:
        /// a faster line could only ever repeat itself, and the census is meant to be read, not
        /// scrolled past.</summary>
        private const float FadeCensusIntervalSeconds = 2f;

        /// <summary>How many UNITS one line names. Six is what the standing-prop, mounted and
        /// prop-unit lines settled on — enough to name the offender, short of a wall of text. The
        /// line always states how many units it dropped.</summary>
        private const int FadeCensusUnitCap = 6;

        /// <summary>How many written renderers one unit names before it says "+N more".</summary>
        private const int FadeCensusMembersPerUnit = 4;

        /// <summary>How many still-solid siblings a torn unit names — the other half of the
        /// comparison, and the half that makes "torn" a fact rather than an inference. Raised
        /// from 3 to 6 in ModBuild 258: the two worst offenders in the ModBuild-257 log are
        /// <c>'PCG_FR_Pillar_Tree_Trunk_01_PR' 7/17 written … LEFT SOLID … +6 more</c> and
        /// <c>'CA_ICY_WallLight' 4/9 written … LEFT SOLID: p_fire_torch (8), fx_sparks (1),
        /// distort, +2 more</c>, i.e. both hid the tail of the very list the round turns on.</summary>
        private const int FadeCensusSolidNamesPerUnit = 6;

        /// <summary>One renderer this mod is currently fading, and by which path.</summary>
        private readonly struct FadeWrite
        {
            internal readonly Renderer R;
            /// <summary>Which fade path wrote it — the list it was found in, plus the markers for
            /// a per-renderer split segment and for the prop-unit pass having moved it.</summary>
            internal readonly string Path;
            /// <summary>The owning segment's anchor name.</summary>
            internal readonly string Owner;
            /// <summary>That segment's fade, 0..1 (mounted/stacked/body/sibling pieces ride a
            /// leading ramp off the same value — see <c>MountedFadeLead</c>).</summary>
            internal readonly float Fade;

            internal FadeWrite(Renderer r, string path, string owner, float fade)
            {
                R = r;
                Path = path;
                Owner = owner;
                Fade = fade;
            }
        }

        /// <summary>One grouped prop as the census sees it.</summary>
        private sealed class FadeUnit
        {
            public string Label = "?";
            /// <summary>The unit root. Held only for the duration of ONE census call and dropped
            /// in <see cref="Reset"/> — Apparance rebirths these subtrees constantly, and a
            /// transform kept across calls is a dangling reference within a couple of seconds
            /// (<c>WallSegmentFade.Standing.cs</c> pays for that lesson already).</summary>
            public Transform? Root;
            public readonly List<FadeWrite> Written = new(8);
            /// <summary>Names of the unit's renderers that NO fade path wrote — the comparison
            /// that makes a torn prop visible.</summary>
            public readonly List<string> Solid = new(8);
            public int TotalRenderers;
            public float SizeRank;
            public float MinY;
            public float MaxY;
            public float FloorY;
            public bool SingleRenderer;
            /// <summary>Computed ONCE per census (see <see cref="OwnerSplit"/>), because the sort
            /// below reads it and a comparator that allocates a StringBuilder per comparison is a
            /// diagnostic that costs more than the thing it diagnoses.</summary>
            public string Owners = string.Empty;

            public void Reset()
            {
                Label = "?";
                Root = null;
                Written.Clear();
                Solid.Clear();
                TotalRenderers = 0;
                SizeRank = 0f;
                MinY = 0f;
                MaxY = 0f;
                FloorY = 0f;
                SingleRenderer = false;
                Owners = string.Empty;
            }

            public bool Torn => WallStandingProp.IsTorn(Written.Count, TotalRenderers);

            /// <summary>
            /// THE OTHER WAY A PROP TEARS, and the ModBuild-257 log could not state it: not
            /// "some of me was written" but "I have TWO OWNERS, on two independent fades". The
            /// census printed the owner per renderer and then truncated at four members, so the
            /// split in <c>'CA_ICY_WallLight' 4/9</c> — ice meshes on <c>wall renderer of
            /// 'Wall 4'</c>, torch emitters on <c>mounted dressing of 'Wall 1'</c> — had to be
            /// reconstructed by hand across two different lines of the log. A unit with two
            /// owners half-survives every fade, whichever wall goes first, and after the
            /// ModBuild-258 unit-affinity rule (<c>WallSegmentFade.Mounted.cs</c>,
            /// <c>_mountedUnitHome</c>) there should be none. Stated per unit so one grep
            /// falsifies that.
            /// </summary>
            public string OwnerSplit()
            {
                var sb = new System.Text.StringBuilder();
                int distinct = 0;
                // Index loops on purpose: FadeWrite is a readonly STRUCT, so an identity test
                // between two copies would box and never be true. "First occurrence" is a
                // position, not a reference.
                for (int i = 0; i < Written.Count; i++)
                {
                    string owner = Written[i].Owner;
                    bool seen = false;
                    for (int j = 0; j < i; j++)
                    {
                        if (string.Equals(Written[j].Owner, owner, System.StringComparison.Ordinal))
                        {
                            seen = true;
                            break;
                        }
                    }
                    if (seen)
                        continue;
                    int n = 0;
                    float fade = 0f;
                    for (int j = 0; j < Written.Count; j++)
                    {
                        if (!string.Equals(Written[j].Owner, owner, System.StringComparison.Ordinal))
                            continue;
                        n++;
                        fade = Written[j].Fade;
                    }
                    distinct++;
                    if (sb.Length > 0)
                        sb.Append(", ");
                    sb.Append('\'').Append(owner).Append("'×").Append(n)
                      .Append("@fade ").Append(fade.ToString("0.00"));
                }
                return distinct > 1 ? sb.ToString() : string.Empty;
            }
        }

        private readonly List<FadeWrite> _fadeWrites = new(128);
        private readonly List<FadeUnit> _fadeUnits = new(32);
        private readonly List<FadeUnit> _fadeUnitPool = new(32);
        private readonly Dictionary<Transform, int> _fadeUnitByRoot = new(32);
        private readonly List<Renderer> _fadeCensusScratch = new(32);
        private float _nextFadeCensus;
        private int _fadeCensusSig = -1;

        /// <summary>
        /// Collect every renderer the frame's fade paths just wrote and, when that set has changed,
        /// print it. Called at the end of <c>Tick</c>, after <c>Apply</c> and
        /// <c>ApplyCornerPieces</c> — so every number is a result.
        /// </summary>
        private void LogFadeWriteCensus(float now)
        {
            if (now < _nextFadeCensus)
                return;
            _nextFadeCensus = now + FadeCensusIntervalSeconds;

            _fadeWrites.Clear();
            foreach (Segment seg in _segments.Values)
            {
                if (seg.Fade <= 0f)
                    continue;
                string owner = seg.Anchor != null ? seg.Anchor.name : "<dead>";
                // A segment whose anchor IS a renderer is one of the deliberate per-renderer
                // splits (RefreshSplitWall / NeutralizeEngulfingSegments). The prop-unit pass
                // steps around those on purpose, so the census has to say when a write came from
                // one — otherwise "why did the prop-unit pass not heal this" has no answer.
                string wallPath = IsPerRendererSplit(seg) ? "wall renderer[split segment]"
                                                          : "wall renderer";
                foreach (MeshRenderer r in seg.Renderers)
                    NoteFadeWrite(r, wallPath, owner, seg.Fade);
                foreach (MeshRenderer r in seg.Foliage)
                    NoteFadeWrite(r, "foliage", owner, seg.Fade);
                foreach (MeshRenderer r in seg.Siblings)
                    NoteFadeWrite(r, "asset sibling", owner, seg.Fade);
                foreach (MountedProp p in seg.Body)
                    NoteFadeWrite(p.Renderer, "wall body mesh", owner, seg.Fade);
                foreach (MountedProp p in seg.Stacked)
                    NoteFadeWrite(p.Renderer, "stacked shell", owner, seg.Fade);
                foreach (MountedProp p in seg.Mounted)
                    NoteFadeWrite(p.Renderer, "mounted dressing", owner, seg.Fade);
                // ModBuild 259: the whole-unit arm. Without this line every member the prop-unit
                // pass gave a dissolve channel to would still be counted LEFT SOLID and the TORN
                // number would not move — the census would report the fix as the defect.
                foreach (MountedProp p in seg.UnitDressing)
                    NoteFadeWrite(p.Renderer, "prop-unit dressing", owner, seg.Fade);
            }
            foreach (CornerPiece cp in _cornerPieces)
            {
                float fade = cp.B == null ? cp.A.Fade : Mathf.Min(cp.A.Fade, cp.B.Fade);
                if (fade <= 0f)
                    continue;
                string owner = cp.A.Anchor != null ? cp.A.Anchor.name : "<dead>";
                NoteFadeWrite(cp.Prop.Renderer, "shared corner piece", owner, fade);
            }

            // Change trigger. Quantised to 1/16 of a fade so a ramp does not reprint every line of
            // its own transition, but a piece arriving or leaving does.
            int sig = _fadeWrites.Count * 397;
            foreach (FadeWrite w in _fadeWrites)
            {
                sig = unchecked(sig * 31 + w.R.GetInstanceID());
                sig = unchecked(sig * 31 + Mathf.RoundToInt(w.Fade * 16f));
                sig = unchecked(sig * 31 + w.Path.GetHashCode());
                // ModBuild 258: the OWNER is part of the signature. A prop changing hands between
                // two walls at the same fade is now the defect this line reports, and without
                // this term the line stays silent through exactly that transition.
                sig = unchecked(sig * 31 + w.Owner.GetHashCode());
            }
            if (sig == _fadeCensusSig)
                return;
            _fadeCensusSig = sig;
            if (_fadeWrites.Count == 0)
                return;

            BuildFadeUnits();
            EmitFadeWriteCensus();
        }

        /// <summary>Record one write, skipping renderers that are gone or ours.</summary>
        private void NoteFadeWrite(Renderer? r, string path, string owner, float fade)
        {
            if (r == null || IsModObject(r))
                return;
            if (_propUnitTouched.Contains(r))
                path += "[prop unit]";
            _fadeWrites.Add(new FadeWrite(r, path, owner, fade));
        }

        /// <summary>Group the writes into prop units, measure each one, and find the renderers of
        /// those units that NOTHING wrote — the torn-prop comparison.</summary>
        private void BuildFadeUnits()
        {
            foreach (FadeUnit u in _fadeUnits)
            {
                u.Reset();
                _fadeUnitPool.Add(u);
            }
            _fadeUnits.Clear();
            _fadeUnitByRoot.Clear();

            foreach (FadeWrite w in _fadeWrites)
            {
                Transform? root = StandingFloorUnitRootOf(w.R);
                bool single = root == null;
                if (single)
                    root = w.R.transform;
                if (!_fadeUnitByRoot.TryGetValue(root!, out int at))
                {
                    FadeUnit made = RentFadeUnit();
                    made.Label = root!.name;
                    made.Root = root;
                    made.SingleRenderer = single;
                    MeasureFadeUnit(root!, made);
                    at = _fadeUnits.Count;
                    _fadeUnits.Add(made);
                    _fadeUnitByRoot[root!] = at;
                }
                _fadeUnits[at].Written.Add(w);
            }

            // The still-solid half. A renderer under the unit's root that nothing wrote is what
            // makes the unit TORN; the names are what make it readable.
            foreach (FadeUnit u in _fadeUnits)
            {
                if (u.Written.Count >= u.TotalRenderers || u.SingleRenderer || u.Root == null)
                    continue;
                // The subtree is re-read into a LOCAL array rather than the shared scratch list:
                // the loop below walks it while reading u.Written, and a scratch list shared
                // between two live iterations is how one gets cleared underneath the other.
                foreach (Renderer piece in u.Root.GetComponentsInChildren<Renderer>(false))
                {
                    if (piece == null || IsModObject(piece)
                        || u.Solid.Count >= FadeCensusSolidNamesPerUnit)
                    {
                        continue;
                    }
                    bool written = false;
                    foreach (FadeWrite w in u.Written)
                    {
                        if (ReferenceEquals(w.R, piece)) { written = true; break; }
                    }
                    if (!written)
                        u.Solid.Add(piece.name);
                }
            }

            // The owner split, once per unit — the sort below reads it. See FadeUnit.Owners.
            foreach (FadeUnit u in _fadeUnits)
                u.Owners = u.OwnerSplit();

            // TORN OR SPLIT first (those are the two shapes of the report), then SMALLEST first
            // (a skull is small). Insertion sort: the list is tens of entries and this runs at
            // most once every two seconds, on a frame where something actually changed.
            for (int i = 1; i < _fadeUnits.Count; i++)
            {
                FadeUnit key = _fadeUnits[i];
                int j = i - 1;
                while (j >= 0 && FadeUnitOrder(key, _fadeUnits[j]) < 0)
                {
                    _fadeUnits[j + 1] = _fadeUnits[j];
                    j--;
                }
                _fadeUnits[j + 1] = key;
            }
        }

        /// <summary>Negative when <paramref name="a"/> must be printed before
        /// <paramref name="b"/>.</summary>
        private static int FadeUnitOrder(FadeUnit a, FadeUnit b)
        {
            // A unit with two owners can be 9/9 written and therefore NOT torn, and it is still
            // the defect (CA_ICY_WallLight — see FadeUnit.OwnerSplit). Without this term the
            // six-unit cap drops it by size and the line answers a question nobody asked.
            bool ab = a.Torn || a.Owners.Length > 0;
            bool bb = b.Torn || b.Owners.Length > 0;
            if (ab != bb)
                return ab ? -1 : 1;
            return a.SizeRank < b.SizeRank ? -1 : a.SizeRank > b.SizeRank ? 1 : 0;
        }

        private FadeUnit RentFadeUnit()
        {
            if (_fadeUnitPool.Count == 0)
                return new FadeUnit();
            FadeUnit u = _fadeUnitPool[_fadeUnitPool.Count - 1];
            _fadeUnitPool.RemoveAt(_fadeUnitPool.Count - 1);
            u.Reset();
            return u;
        }

        /// <summary>Union AABB, renderer count and floor reference for one unit — the same
        /// measurement the standing rule makes, so the two lines cannot disagree about a prop's
        /// size or its foot.</summary>
        private void MeasureFadeUnit(Transform root, FadeUnit unit)
        {
            _fadeCensusScratch.Clear();
            root.GetComponentsInChildren(includeInactive: false, _fadeCensusScratch);
            Bounds union = default;
            bool have = false;
            int kept = 0;
            foreach (Renderer piece in _fadeCensusScratch)
            {
                if (piece == null || IsModObject(piece))
                    continue;
                kept++;
                if (!have) { union = piece.bounds; have = true; }
                else union.Encapsulate(piece.bounds);
            }
            _fadeCensusScratch.Clear();
            unit.TotalRenderers = kept;
            if (!have)
                return;
            unit.MinY = union.min.y;
            unit.MaxY = union.max.y;
            unit.SizeRank = WallStandingProp.SizeRank(union.size.x, union.size.y, union.size.z);
            if (!NearestAnchoredFloorY(union.min.y, out float floorY))
                floorY = 0f;
            unit.FloorY = floorY;
        }

        /// <summary>Build and emit the line.</summary>
        private void EmitFadeWriteCensus()
        {
            int torn = 0, lone = 0, split = 0;
            foreach (FadeUnit u in _fadeUnits)
            {
                if (u.Torn)
                    torn++;
                if (u.SingleRenderer)
                    lone++;
                if (u.Owners.Length > 0)
                    split++;
            }
            int shown = Mathf.Min(FadeCensusUnitCap, _fadeUnits.Count);
            var rows = new System.Text.StringBuilder();
            for (int i = 0; i < shown; i++)
            {
                FadeUnit u = _fadeUnits[i];
                if (rows.Length > 0)
                    rows.Append(" | ");
                rows.Append(u.Torn ? "TORN " : string.Empty)
                    .Append('\'').Append(u.Label).Append('\'')
                    .Append(u.SingleRenderer ? "[no prop unit — a unit of one]" : string.Empty)
                    .Append(' ').Append(u.Written.Count).Append('/').Append(u.TotalRenderers)
                    .Append(" written, unit y[").Append(u.MinY.ToString("0.0")).Append("..")
                    .Append(u.MaxY.ToString("0.0")).Append("] over floor ")
                    .Append(u.FloorY.ToString("0.0")).Append(", widest ")
                    .Append(u.SizeRank.ToString("0.0")).Append(" wu: ");
                int members = Mathf.Min(FadeCensusMembersPerUnit, u.Written.Count);
                for (int m = 0; m < members; m++)
                {
                    if (m > 0)
                        rows.Append(", ");
                    AppendWrittenRow(rows, u.Written[m], u.FloorY);
                }
                if (u.Written.Count > members)
                    rows.Append(", +").Append(u.Written.Count - members).Append(" more written");
                if (u.Solid.Count > 0)
                {
                    rows.Append(" — LEFT SOLID under the same root: ")
                        .Append(string.Join(", ", u.Solid));
                    int rest = u.TotalRenderers - u.Written.Count - u.Solid.Count;
                    if (rest > 0)
                        rows.Append(", +").Append(rest).Append(" more");
                }
                if (u.Owners.Length > 0)
                {
                    rows.Append(" — TWO OWNERS on independent fades: ").Append(u.Owners)
                        .Append(" (whichever goes first, the other half of this prop survives it)");
                }
            }

            VRLog.Info(Name,
                $"FADE WRITE: {_fadeWrites.Count} renderer(s) carry a non-zero wall fade right "
                + $"now, grouped into {_fadeUnits.Count} prop unit(s), {torn} of them TORN — a "
                + $"fade path wrote part of a prop and left the rest solid, which is the shape of "
                + $"the report (user 2026-08-19, skelet.jpg: 'Der Schädel ist immer noch nicht "
                + $"sichtbar' — a skeleton on a deck with its ribcage, arms and legs drawn and its "
                + $"skull gone, against a wall mid-dissolve). Torn units first, then SMALLEST "
                + $"first because a skull is small; {shown} of {_fadeUnits.Count} unit(s) shown, "
                + $"{_fadeUnits.Count - shown} dropped. Grouping: highest still prop-sized "
                + $"ancestor (the ModBuild-167 walk, stops at any wall entity or segment anchor); "
                + $"{lone} of these unit(s) have NO prop unit at all and are judged as a unit of "
                + $"one — a torn prop cannot be detected there and the standing-prop FLOOR arm "
                + $"cannot fire on it, so a missing piece that shows up in THAT bucket means the "
                + $"tileset parented it flat and the grouping, not the geometry, is what needs "
                + $"widening next. "
                + $"{split} unit(s) have TWO OR MORE OWNERS on independent fades — the second way "
                + $"a prop tears, and the one this census could not state until ModBuild 258: "
                + $"'CA_ICY_WallLight' put its ice meshes on 'Wall 4' as wall renderers and its "
                + $"blue torch emitters on 'Wall 1' as mounted dressing, so half of it survived "
                + $"every fade either wall made (wandproblem3.jpg, 'die blaue Flamme ist nun "
                + $"wieder sichtbar ohne dass sie gefaded ist'). The unit-affinity rule in "
                + $"WallSegmentFade.Mounted.cs is meant to hold this at ZERO; any non-zero value "
                + $"here names the units it missed. "
                + $"A write on the 'prop-unit dressing' path is the ModBuild-259 arm: a member "
                + $"with no wall-fade channel that the prop-unit pass gave one to rather than "
                + $"leaving it standing (the ModBuild-258 line's '106 left visible'). TORN is the "
                + $"number this build moves: 24 of 72, 22 of 42 and 28 of 90 per pass in the "
                + $"ModBuild-258 log, and a torn unit whose solid half is Foliage-family is the "
                + $"one shape that must now be gone. "
                + $"Anchor = AABB min.y over the nearest anchored room floor, the same anchor the "
                + $"mounted census prints. {rows}");
        }

        /// <summary>One written renderer, with everything needed to identify it in a scene nobody
        /// on the build machine can open: name, type, ancestry, foot height over its room's floor,
        /// AABB, the path that wrote it and the fade it is carrying.</summary>
        private static void AppendWrittenRow(System.Text.StringBuilder sb, FadeWrite w, float floorY)
        {
            if (w.R == null)
            {
                sb.Append("<destroyed mid-frame>");
                return;
            }
            Bounds b = w.R.bounds;
            sb.Append('\'').Append(w.R.name).Append("'[").Append(RendererKind(w.R))
              .Append("] under '").Append(AncestorPath(w.R.transform))
              .Append("' anchor ").Append((b.min.y - floorY).ToString("0.00"))
              .Append(" over floor, AABB c(")
              .Append(b.center.x.ToString("0.0")).Append(',')
              .Append(b.center.y.ToString("0.0")).Append(',')
              .Append(b.center.z.ToString("0.0")).Append(") s(")
              .Append(b.size.x.ToString("0.0")).Append(',')
              .Append(b.size.y.ToString("0.0")).Append(',')
              .Append(b.size.z.ToString("0.0")).Append(") ← ").Append(w.Path)
              .Append(" of '").Append(w.Owner).Append("' fade ")
              .Append(w.Fade.ToString("0.00"));
        }

        /// <summary>A few levels of ancestor path, top-down, for a renderer — enough to tell two
        /// instances of the same asset apart and to see what a prop hangs under, without printing
        /// a path to the scene root for every row.</summary>
        private static string AncestorPath(Transform t)
        {
            var parts = new List<string>(4);
            Transform? node = t.parent;
            for (int i = 0; node != null && i < 4; i++)
            {
                parts.Add(node.name);
                node = node.parent;
            }
            parts.Reverse();
            return parts.Count == 0 ? "<scene root>" : string.Join("/", parts);
        }
    }
}
