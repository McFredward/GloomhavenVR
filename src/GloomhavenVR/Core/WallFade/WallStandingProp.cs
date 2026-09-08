namespace GloomhavenVR.Core;

/// <summary>
/// WHEN A PROP STANDS ON THE FLOOR AND IS THEREFORE NEVER WALL GEOMETRY — the whole geometric
/// test, and nothing else. Free of Unity on purpose (see the bottom of this header).
///
/// <para>THE REPORT, fourth round. 2026-08-19, hardware, ModBuild 167, verbatim: <i>"Der Schädel
/// ist immer noch nicht sichtbar."</i> The photograph is <c>.planning/debug/skelet.jpg</c>, and
/// this is what is in it, read off the image rather than off a log: a full human skeleton is
/// slumped on a WOODEN DECK at floor level, leaning back against a low masonry wall, legs splayed
/// forward across the planks, with burning hexes and scattered parchment around it. Ribcage,
/// clavicles, both arms, pelvis and both legs are solid and fully drawn. THE SKULL IS GONE — above
/// the ribcage there is a stub of vertebrae and nothing else. The low wall directly behind the
/// skeleton is MID-DISSOLVE: its top edge carries the ragged noise pattern of the masonry fade,
/// not an authored broken edge. So: a floor-level prop, one renderer of which vanished together
/// with a fading wall.</para>
///
/// <para>WHAT THE THREE PREVIOUS ROUNDS FIXED, and why none of them was this. ModBuild 157
/// (<c>WallSegmentFade.Standing.cs</c>) refuses a floor-standing FIGURE/ACTOR prop on the wall
/// path. ModBuild 167 (<c>WallSegmentFade.PropUnit.cs</c> + <c>WallPropUnit.cs</c>) gives a
/// multi-part prop torn between two wall units one single owner. Both are real and both stay. But
/// in the ModBuild-167 hardware log the <c>PROP UNIT</c> pass printed ZERO lines — it never fired
/// once — and the <c>CR_OS_Skeleton_Statue_*</c> renderers those rounds were written around are
/// anchored at 3.5 and 5.1 wu above the floor and agree on their owners in that same log. They are
/// wall statues somewhere else in the level, not the thing in the photograph.</para>
///
/// <para>WHAT BLOCKS THE ONE RULE THAT SHOULD HAVE COVERED IT. The ModBuild-157 rule prints its
/// own three terms in the log (<c>LogOutput.log</c>, the <c>STANDING PROP</c> line, verbatim):
/// <i>"figure/actor ancestry AND the unit's union AABB reaches within 1.0 wu of its room's floor
/// AND the unit is prop-sized (span ≤ 6.0 wu, ≤ 24 renderers)"</i>. A scenery skeleton lying on a
/// deck carries no <c>Animator</c>, no <c>ActorBehaviour</c> and no <c>CInteractableActor</c>, so
/// the first term refuses it however well the other two fit. And that term is doing no work the
/// others are not: the same log line states the geometric test's own justification —
/// <i>"wall-MOUNTED dressing never reaches the floor and keeps fading with its masonry"</i>. That
/// is the discriminator. Figure ancestry was only ever the way the first candidate was found.</para>
///
/// <para>THE RULE, therefore, in two arms. The FIGURE arm is ModBuild 157 bit-for-bit and is left
/// alone. The FLOOR arm is new and is what the photograph asks for:</para>
/// <list type="number">
/// <item>THE UNIT reaches the floor: its union AABB's LOWEST point sits within
///   <see cref="FootBandWU"/> of the room's floor plane. Deliberately the union of the whole prop
///   and not of the reported renderer — a skull one metre up looks airborne on its own and does
///   not once it is judged as part of the skeleton it belongs to. That is exactly the split the
///   photograph shows.</item>
/// <item>PROP-SIZED HORIZONTALLY: <see cref="MaxSpanWU"/> across and at most
///   <see cref="MaxRenderers"/> renderers, the caps ModBuild 157 introduced against the one
///   catastrophe this rule can cause — a unit that swallows a room turns every wall renderer
///   under it into protected geometry and wall see-through silently stops working.</item>
/// <item>PROP-SIZED VERTICALLY, and this term is NEW, because without it the floor arm is that
///   catastrophe. A wall course stands on the floor too. The ModBuild-167 hardware log prints the
///   AABB top of every renderer a wall fades (<c>fade ON … 87 toggle-native: Blocks@2.8,
///   Pillar@3.3, Wall@3.3, WallTop@3.4, CR_OS_Pillar_Large_03@3.7 …</c>): in that whole session
///   the LOWEST-topped piece of masonry or architecture reaches 2.8 wu, while the tallest thing
///   the same lists carry that is dressing rather than structure — <c>CR_ST_Shelf_Books_Sparse_02
///   @2.5</c>, <c>EN_CR_LBSkull@2.4</c>, <c>SB_AncCaverns_DemonHead@1.9</c>,
///   <c>CR_ST_Shelf_Alchemy_Balance@1.3</c>, <c>EN_CR_Candle_06@1.2</c> — tops out at 2.5. So
///   <see cref="MaxHeightWU"/> = 2.5 wu sits in a real gap in this tileset's own numbers rather
///   than in an argument. It is a FIRST CUT and it is meant to be moved with evidence: the
///   <c>FADE WRITE</c> census (<c>WallSegmentFade.FadeCensus.cs</c>) now prints every unit's
///   measured height next to its verdict, so the next hardware log states the real number for the
///   skeleton in the photograph instead of leaving it to be guessed a fifth time.</item>
/// </list>
///
/// <para>WHY THE FIGURE ARM KEEPS NO HEIGHT CAP: it is shipped, confirmed behaviour with two
/// hardware rounds behind it, and a figure/actor prop is by construction not masonry. Adding a cap
/// there could only take protection away from something that currently has it.</para>
///
/// <para>TOMBSTONE — THE FREE-STANDING TREE ARM (ModBuild 257), RETIRED IN ModBuild 258 WITH THE
/// MEASUREMENT THAT KILLED IT. DO NOT RE-DERIVE IT. That arm lifted <see cref="MaxHeightWU"/> for
/// a unit that was VEGETATION (any Foliage-shaded renderer under the root) AND SLENDER (height over
/// widest XZ ≥ 2.0). It shipped with its own falsifier — the census was made to print <c>h/w</c>
/// for every unit — and its own instruction: <i>"If the next log shows the trunk unit at or under
/// 2.0 h/w, do not nudge the constant — the discriminator is not shape."</i> The ModBuild-257
/// hardware log (<c>.planning/debug/LogOutput.log</c>, 2026-08-24) answers it twice over:</para>
/// <list type="number">
/// <item>NO TREE REACHED THE BAR. Every trunk unit in the session is UNDER it:
///   <c>'PCG_FR_Pillar_Tree_Trunk_01_PR' [h 3.9 wu / w 2.8 wu = 1.39 h/w]</c>,
///   <c>'…_Trunk_02_PR' [h 4.6 / w 3.2 = 1.46]</c>, <c>'…_Trunk_03_PR' [h 5.8 / w 3.2 = 1.79]</c>.
///   A tree is not slender once the whole PROP is measured: the trunk unit's union AABB contains
///   its own vines and floor bushes, so the widest span is the canopy's, not the bark's.</item>
/// <item>THE ONE UNIT IT DID FIRE ON WAS A WALL. The whole session's h/w distribution, from the
///   <c>STANDING PROP</c> census, is 0.23, 0.80, 0.83, 1.18, 1.28, 1.39, 1.42, 1.43, 1.46, 1.51,
///   1.79, 1.85 and <b>2.11</b>. Exactly one value clears 2.0 and it belongs to
///   <c>'PCG_FR_Wall_Space_04_PR' foot -0.3 wu over floor 0.0, height 4.5 wu, span 2.1x2.1 wu,
///   4 renderer(s) — free-standing TREE [h 4.5 wu / w 2.1 wu = 2.11 h/w, vegetation]</c>. That
///   unit was thereby protected on EVERY path and appears in no <c>FADE WRITE</c> line of the
///   session at all — i.e. it never faded once. It is the 2026-08-24 report, verbatim: <i>"ein
///   Teil der Wand bleibt nun stehen und faded garnicht mehr"</i> (<c>wandproblem3.jpg</c>).</item>
/// </list>
/// <para>So the arm had a 0 % true-positive rate and its single firing was the regression. Shape is
/// not the discriminator, the constant is not the problem, and membership goes back to what
/// ModBuild 256 shipped: FIGURE and FLOOR, nothing else. <see cref="Unit.SlendernessHW"/> and the
/// <c>vegetation</c> flag survive as REPORTED facts only — they cost one divide and one shader
/// lookup, they are what let this tombstone be written from one grep, and neither decides
/// anything. The trees are being taken out of the occlusion DENOMINATOR instead (the coverage grid
/// samples a rectangle around the room rather than the playable tiles), which needs no classifier
/// here.</para>
///
/// <para>ALSO NOT THE RATIO ModBuild 255 RETIRED, and that one stays retired too: <c>IsStandingPiece</c>
/// was a PER-RENDERER aspect test inside the occlusion numerator, it excluded zero pieces on the
/// two wrongly-faded walls, and its only actual exclusions were real capstones (<c>'Wall 3' 10
/// admitted / 4 excluded, widest excluded 'WallTop' 2.4 wu</c>). Two shape discriminators have now
/// been shipped and falsified by the very next hardware log. There is not to be a third.</para>
///
/// <para>THE RULE ADDED IN ITS PLACE IS STRUCTURAL — A UNIT FADES WHOLE OR NOT AT ALL
/// (<see cref="UnitFadesAsOne"/>, ModBuild 258, <c>wandproblem3.jpg</c>). The same log shows the
/// tear from the other side: <c>FADE WRITE: … grouped into 85 prop unit(s), 49 of them TORN</c>,
/// and the worst offender by a factor of four is
/// <c>TORN 'PCG_FR_Wall_Grassy_Verge_Thin_Narrow_01_PR' 4/6 written … LEFT SOLID under the same
/// root: FR_Stones_06 (1), FR_Stones_02 (2)</c> — 105 lines of it. The wall mesh and its three
/// Foliage attachments dissolve; the two ground-hugging stones of the SAME prop stay fully drawn.
/// That is the picture. And it is the collision of two rules that are each individually right: the
/// ground band (<c>GroundExclusionHeightWU</c> = 1.0 wu, 713 renderers in that bucket in this
/// session's <c>WALL-PATH AUDIT</c>) says "anything whose AABB top is inside the band is scenery",
/// while the FLOOR arm has ALREADY judged this very unit <c>height 3.0 wu — architecture, not a
/// floor prop (cap 2.5 wu)</c>. One of them has to win for the whole unit, and it must be the
/// unit-level verdict: a per-renderer band cannot see that the thing it is holding back is the
/// bottom metre of a wall that is dissolving above it. See <see cref="UnitFadesAsOne"/> for the
/// term and for what stops it eating a floor.</para>
///
/// <para>MODBUILD 266 — A FRAGMENT OF A WALL FEATURE IS NOT A PROP, AND THE DEFECT WAS THE UNIT
/// AND NOT THE HEIGHT. User report 2026-08-25, hardware, ModBuild 265: <i>"In dem Level gibt es
/// Bücherregale die als Wandersatz dienen … faden aber nicht in den Tests. Ich möchte, dass auch
/// sie vollständig faden."</i> and <i>"Noch eine Wandhalterung/Regal/Brett faded nicht mit, obwohl
/// es an der Wand hängt. Die Tränke die darauf liegen faden mit, aber Regal selber nicht."</i>
/// The same log answers it with ONE prefab that this rule measures TWICE, at two different
/// roots:</para>
/// <code>
/// TORN 'PCG_Test_Feature_Small_2' 19/21 written, unit y[0.0..3.4] over floor 0.0, widest 3.5 wu:
///     'CR_ST_Shelves_Stone_Wood'[mesh] under 'Wall 4/Generated Content/PCG_Test_Feature_Small_2/…'
///     anchor 2.05 … ← wall renderer of 'Wall 4' fade 1.00
///     'CR_ST_Shelves_Stone_Wood_Shelf'[mesh] anchor 2.20 … ← wall renderer of 'Wall 4' fade 1.00
///     'Blocks (1)'[mesh]  anchor 0.00 over floor, AABB s(1.1,2.8,0.8)   ← the feature's masonry
///     'Pillar'[mesh]      anchor 2.82 over floor                        ← its capital
///
/// 'CR_ST_Shelves_Stone_Wood'[shared ancestor] 2 renderer(s)    ← the SAME prefab, other instance
/// [WALL MEMBER] 'CR_ST_Shelves_Stone_Wood'       foot 0.90 / top 1.31  … drawing over a wall at fade 1.00
/// [FLOATING]    'CR_ST_Shelves_Stone_Wood_Shelf' foot 1.13 / top 1.23  … "part of a prop unit that STANDS ON THE FLOOR"
/// </code>
/// <para>Read the two roots. Where the climb reaches <c>PCG_Test_Feature_Small_2</c> the unit is
/// the WHOLE wall feature — a 2.8 wu masonry block standing at anchor 0.00, a pillar capital, and
/// the shelf bracketed to it — measuring <b>3.4 wu tall</b>, so <see cref="MaxHeightWU"/> already
/// calls it architecture and every renderer under it fades. Where the climb stops one level lower
/// the unit is the 2-renderer SHELF alone, <b>0.41 wu tall</b> with its foot 0.90 wu up, which is
/// a textbook floor prop by these very numbers — and is protected on every path and left drawing
/// over a wall at fade 1.00.</para>
///
/// <para><b>SO THE HEIGHT CAP WAS NEVER WRONG AND NO NUMBER NEEDED MOVING.</b> The rule was
/// judging a FRAGMENT. That is the exact failure this file was written against, in its own words
/// — <i>"a skull one metre up looks airborne on its own and does not once it is judged as part of
/// the skeleton it belongs to"</i> — with the fragment and the whole swapped round. Four
/// thresholds in this subsystem have been shipped from one scenario's numbers and falsified by the
/// next log; this round moves none of them.</para>
///
/// <para><b>THE TERM.</b> FLOOR arm only, two conjuncts, no new constant:
/// <list type="number">
/// <item>the unit's climb passed a WALL ENTITY inside its own bounded window (<c>wallCut</c>,
///   measured by <c>WallSegmentFade.PropUnit.cs</c>'s walk and reported out of it) — i.e. what
///   sits immediately above this unit is a wall, so the unit is a piece of that wall's feature
///   rather than a prop standing in the room; and</item>
/// <item>the unit rises out of the ground band (<see cref="FootBandWU"/>, the same 1.0 wu as
///   <c>GroundExclusionHeightWU</c>) — the guard that keeps floor hexes, grass and ground scatter
///   protected, which this tileset also parents under <c>Wall N/Generated Content/</c>.</item>
/// </list></para>
///
/// <para><b>WHY conjunct 1 IS NOT <c>GetComponentInParent&lt;ProceduralWall&gt;()</c>, and this is
/// the whole care of the round.</b> That probe — what <c>IsWallGeneratedMember</c> and the flags
/// lane's <c>WallGeneratorAncestry</c> both use — climbs to the SCENE ROOT, and in the
/// ModBuild-265 log it answers YES for things no wall built: <c>'right_thigh01'</c> and
/// <c>'left_thigh01'</c> (a rigged skeleton), <c>'LightShaft_Prefab (1)'</c>, and 348 renderers of
/// <c>'CV_Ice_Crystal_Form_02/03'</c> — the ice formation the user expressly allows to stay. The
/// term here rides the unit walk, which is bounded to four levels, so it can only ever be about
/// the unit's own neighbourhood. The crystal's logged path
/// (<c>'L : (…)/Generated Content/Full/PCG_CV_Ice_Clutter_Floor_07_PR/CV_Ice_Crystal_Form_02 (2)/…'</c>)
/// puts four non-wall nodes above the renderer's parent, and <c>Full</c> and
/// <c>Generated Content</c> are SIBLINGS of <c>Walls/</c> rather than children of it, so no node
/// in the window is or contains a wall. That is a property of the loop bound and of a path the log
/// prints in full — not a claim about which components are in <c>m_WallCache</c>, which cannot be
/// measured off a log and is therefore not relied on anywhere in this round.</para>
///
/// <para><b>MODBUILD 268 — THE ModBuild-267 TERM WAS CORRECT AND NEVER FIRED. This is the
/// tombstone of the miss, kept so it is not made a third time.</b> The term below shipped as
/// ModBuild 267 and the user tested it: <i>"Weiterhin faded weder das große Regal, dass wie eine
/// Wand benutzt wird, noch das kleine Wandhalterungsregal an der Wand. Auch mit dem Fix
/// nicht."</i> The defect was NOT the arithmetic here — it was the <c>wallCut</c> input, which
/// <c>WallSegmentFade.PropUnit.cs</c> computed from the unit walk's BREAK REASON. That walk breaks
/// on CONTAINER SCALE one node below the wall, because this tileset always parents a prop as
/// <c>Wall N / Generated Content / …</c> and <c>Generated Content</c> is container-scale by
/// construction. The flag could therefore never be set. The ModBuild-267 log states it three
/// ways:</para>
/// <list type="number">
/// <item>the shape column reads <c>no wall above</c> <b>391</b> times and <c>under a wall</c>
///   <b>zero</b> times, across the whole session;</item>
/// <item>one of those 391 is <c>'PCG_CR_Wall_Thin_Medium' … [h 3.4 wu / w 1.8 wu = 1.93 h/w, no
///   wall above, no vegetation]</c> — a prefab named <i>Wall</i>, inside a wall, reported as
///   having no wall above it;</item>
/// <item>the refusal tag <c>wall-feature fragment</c> appears <b>0</b> times in the log, while the
///   census counter read 2–6 units refused: the handful it did fire on were units whose walk
///   happened to break ON a wall, and none of them was ever printed.</item>
/// </list>
/// <para>THE LESSON, and it is one this repo already has a name for: <i>measuring one step too
/// early</i>. "Where does the unit END" and "what is ABOVE the unit" are two questions, and
/// ModBuild 267 shipped the first one's answer to the second one's caller. The fix is
/// <c>WallInUnitWindow</c> — the same node sequence, the same four-level bound, no early exit.
/// Nothing in THIS file changed for it, which is the point: the arithmetic was never the
/// defect.</para>
///
/// <para>WHAT THAT ALSO MEANS FOR THE NARROWNESS ARGUMENT. Until now it was untested, because the
/// term never fired. From ModBuild 268 the four-level bound is doing real work and is the ONLY
/// thing separating a wall's own dressing from the ice formation the user has ruled may stay. The
/// STANDING PROP census therefore prints, for every PROTECTED row, the renderer's parent path as
/// the window sees it. If the next log shows <c>CV_Ice_Crystal_Form_02/03</c>, a skeleton limb or
/// <c>LightShaft_Prefab (1)</c> as <c>under a wall</c>, the bound does not separate them in this
/// tileset and this term must be WITHDRAWN — not widened, not retuned.</para>
///
/// <para>THE SPAN AND RENDERER CAPS ARE UNCHANGED, and they are what stops the FLOOR arm being the
/// catastrophe the height cap was added against: a whole wall RUN is over 6.0 wu across or over 24
/// renderers and can never be a unit at all.</para>
///
/// <para>WHAT THE RULE DOES NOT DECIDE, and this is the reason the blast radius is small: it
/// decides only whether a renderer may be collected as WALL geometry (and as a wall's foliage
/// dressing), plus — new with this round — whether it may be adopted as wall-MOUNTED dressing.
/// Without that second half the floor arm would be theatre: a skull refused by the wall path is
/// unclaimed, and the mounted sweep runs afterwards, is geometric, and would simply pick it up and
/// fade it anyway. The MOUNTED rule itself is untouched and keeps its census: a prop whose UNIT
/// hangs a metre over the floor is not a unit that reaches the floor, so nothing that rule
/// protects can meet this one.</para>
///
/// <para>FREE OF UNITY, for the reason the wire-test project's header sets out. The failure this
/// arithmetic ships is not an exception and not a log line. Too tight and the skull stays missing —
/// a fifth identical report. Too loose and a wall course becomes permanently solid, i.e. wall
/// see-through stops working in a way that looks like the mod being switched off. Both are seen
/// only by eye, from inside a headset, one photograph per round, and this defect has now cost
/// four. Every input here is a float and a count, so the case in the photograph — a prop root with
/// a low body and a separate skull renderer a metre up, one wall mid-dissolve — is driven through
/// it on the build machine (<c>tests/GloomhavenVR.WireTests/WallStandingPropVectors.cs</c>).</para>
///
/// <para>MULTIPLAYER: decides nothing that goes on the wire. It removes renderers from a LOCAL
/// wall's own lists; every peer runs the identical floats against the identical scene.</para>
///
/// <para>STEREO: no per-eye term exists here and none can — there is no camera, no screen and no
/// eye in this file. <c>.planning/wall-fade-stereo-rivalry.md</c> (parked) is about the masonry
/// shader discarding the same wall differently in each eye; nothing here touches
/// <c>_Cutoff</c>, the dissolve, the occlusion map or the vignette.</para>
/// </summary>
internal static class WallStandingProp
{
    /// <summary>How far above its room's floor plane a unit's LOWEST point may sit and still count
    /// as standing ON that floor. The same 1.0 wu as <c>GroundExclusionHeightWU</c>: that constant
    /// already encodes this tileset's "within this band of the floor is ground, never wall", and a
    /// prop whose foot is inside the ground band is standing on the ground by the same
    /// measurement.</summary>
    internal const float FootBandWU = 1.0f;

    /// <summary>Largest horizontal extent a unit may have and still be a PROP — the
    /// anti-catastrophe cap. An Animator (or a shared ancestor) sitting on a room-sized container
    /// must never turn that whole container into a protected prop. 6.0 wu is several times a
    /// statue's footprint and well under this tileset's wall runs and room containers (the
    /// report's rooms measure 6.7 x 6.9 wu and up, walls tens of wu long).</summary>
    internal const float MaxSpanWU = 6.0f;

    /// <summary>Most renderers a unit may own and still be a PROP — the second half of the same
    /// cap. The crypt statue has 4; the report's revealed map tiles carry 30-520 renderers each,
    /// so nothing structural can slip under this.</summary>
    internal const int MaxRenderers = 24;

    /// <summary>Tallest a unit may be and still be a floor-standing PROP rather than a course of
    /// masonry — the FLOOR arm only, and the term without which that arm would protect walls. See
    /// the file header for the two columns of hardware numbers this sits between (architecture
    /// from 2.8 wu upward, floor-band dressing to 2.5 wu). A first cut, printed with every verdict
    /// by the FADE WRITE census so the next log can move it on evidence.</summary>
    internal const float MaxHeightWU = 2.5f;

    /// <summary>The leading tag of the ModBuild-266 WALL-FRAGMENT refusal sentence. Since
    /// ModBuild 279 (PERF E) the census counts this refusal off
    /// <c>FloorVerdict.WallFeatureFragment</c> and never off the string — see
    /// <c>WallSegmentFade.Standing.cs</c>'s census — so what the constant is for now is that the
    /// sentence has exactly ONE spelling, which is what stops it and the wire vector that pins it
    /// (<c>tests/GloomhavenVR.WireTests/WallStandingPropVectors.cs</c>) drifting apart. Also what
    /// to grep the next hardware log for.</summary>
    internal const string WallFragmentTag = "wall-feature fragment";

    /// <summary>The leading tag of the WALL-BUILT SECTION refusal sentence — the FIGURE arm's
    /// counterpart to <see cref="WallFragmentTag"/>, and deliberately a DIFFERENT string so the
    /// two can never be counted as one another in a log. Grep the next hardware log for it. See
    /// <see cref="IsWallBuiltSection"/> for the term and for what it may not reach.</summary>
    internal const string WallSectionTag = "wall-built wall section";

    /// <summary>A prop unit's measured extent — the union AABB of every renderer under its root,
    /// which is the whole point: a skull is judged as part of its skeleton, never on its own.</summary>
    internal readonly struct Unit
    {
        /// <summary>Lowest point of the union AABB (world units).</summary>
        internal readonly float MinY;
        /// <summary>Highest point of the union AABB (world units).</summary>
        internal readonly float MaxY;
        internal readonly float SpanX;
        internal readonly float SpanZ;
        /// <summary>How many renderers the unit root owns (inactive included — an inactive piece
        /// is still part of the prop).</summary>
        internal readonly int RendererCount;

        internal Unit(float minY, float maxY, float spanX, float spanZ, int rendererCount)
        {
            MinY = minY;
            MaxY = maxY;
            SpanX = spanX;
            SpanZ = spanZ;
            RendererCount = rendererCount;
        }

        internal float Height => MaxY - MinY;
        internal float WidestSpanXZ => SpanX > SpanZ ? SpanX : SpanZ;

        /// <summary>Height over WIDEST horizontal extent — how many times taller than wide this
        /// unit is. REPORTED ONLY since ModBuild 258: no verdict in this file reads it any more
        /// (see the TREE-arm tombstone in the file header — it is the column that killed that arm,
        /// and it stays printed so the next reader does not have to re-derive the distribution
        /// 0.23…2.11 from a scene nobody on the build machine can open). The WIDEST span and not
        /// the narrower one, because a wall RUN is wide in one axis and thin in the other and
        /// dividing by the thin axis scores it as slender as a trunk. A unit with no measurable
        /// footprint scores 0, never infinity.</summary>
        internal float SlendernessHW => WidestSpanXZ > 0.001f ? Height / WidestSpanXZ : 0f;
    }

    /// <summary>
    /// DOES THIS UNIT FADE AS ONE PIECE — i.e. has the mod already judged it WALL GEOMETRY, so
    /// that no per-renderer rule inside it may hold a member back? ModBuild 258,
    /// <c>wandproblem3.jpg</c>: <i>"ein Teil der Wand bleibt nun stehen und faded garnicht
    /// mehr"</i>.
    ///
    /// <para>It is the exact negation of <see cref="StandsOnFloor"/> and that is the whole point:
    /// there is ONE unit-level verdict, and both answers are total. Protected ⇒ nothing under the
    /// root fades, on any path (the skeleton in <c>skelet.jpg</c>, the floor-grass hexes the
    /// ModBuild-257 census protects as whole units). Not protected ⇒ everything under the root
    /// fades with the unit's owner, INCLUDING the members a per-renderer band would otherwise keep
    /// solid. No new number is introduced and none could be: the two shape constants this file has
    /// shipped were both falsified by the next hardware log (see the tombstone), so the third
    /// attempt is deliberately structural.</para>
    ///
    /// <para>WHAT MAKES THIS SAFE AGAINST THE DEFECT THE GROUND BAND EXISTS FOR — the jungle floor,
    /// where fading a wall took the room-edge ground hexes with it and ate the floor. Those hexes
    /// are never unit members: <c>WallSegmentFade.PropUnit.cs</c>'s walk starts at the renderer's
    /// PARENT and stops dead at a segment anchor or a <c>ProceduralWall</c>, and this tileset
    /// parents a floor tile as <c>Wall N/Generated Content/PCG_CR_Floor_BaseHex_Plain/EN_CR_Floor_
    /// BaseHex_Plain</c> — a one-renderer wrapper under a container-scale node, which yields NO
    /// unit at all. The floor assets that DO form a unit are protected by the FLOOR arm as whole
    /// units and therefore have no claimed member and no owner to be recruited by
    /// (<c>'PCG_FR_Floor_Grass_Hex_Split_PR' … height 0.5 wu … floor prop</c>, ModBuild 257). So
    /// the lift can only ever reach a member of a unit that the FLOOR arm has already called
    /// architecture — which is the scrub wall, and is the picture.</para>
    /// </summary>
    /// <param name="why">The measured reason, printed by the PROP UNIT census. For the reported
    /// unit it reads <c>height 3.0 wu — architecture, not a floor prop (cap 2.5 wu)</c>.</param>
    internal static bool UnitFadesAsOne(in Unit unit, float floorY, bool figureAncestry,
                                        out string why)
        => !StandsOnFloor(unit, floorY, figureAncestry, out why);

    /// <inheritdoc cref="UnitFadesAsOne(in Unit, float, bool, out string)"/>
    /// <param name="vegetation">Reported only — see the other overload.</param>
    /// <param name="wallCut">See
    /// <see cref="StandsOnFloor(in Unit, float, bool, bool, bool, out string)"/>.</param>
    internal static bool UnitFadesAsOne(in Unit unit, float floorY, bool figureAncestry,
                                        bool vegetation, bool wallCut, out string why)
        => !StandsOnFloor(unit, floorY, figureAncestry, vegetation, wallCut, out why);

    /// <summary>
    /// Does this unit STAND ON THE FLOOR of the room whose plane is <paramref name="floorY"/> —
    /// and is it therefore never wall geometry, on any path, whatever shader or material slot its
    /// renderers carry?
    /// </summary>
    /// <param name="unit">The union of the WHOLE prop, never of the one renderer that happened to
    /// be reported. This is the term the photograph turns on.</param>
    /// <param name="floorY">The nearest ANCHORED room floor plane. Rooms can be terraced, so the
    /// lowest floor in the scene is the wrong reference for a prop on an upper terrace.</param>
    /// <param name="figureAncestry">True = the ModBuild-157 FIGURE arm (shipped, confirmed, no
    /// height cap). False = the FLOOR arm added for skelet.jpg, which must also be prop-sized
    /// vertically or it protects masonry.</param>
    /// <param name="why">The measured reason, printed verbatim by the STANDING PROP census — for
    /// a refusal as well as for a verdict, because "the rule did not fire" without "and by which
    /// number" is what cost this defect its fourth round.</param>
    internal static bool StandsOnFloor(in Unit unit, float floorY, bool figureAncestry,
                                       out string why)
        => StandsOnFloor(unit, floorY, figureAncestry, vegetation: false, out why);

    /// <inheritdoc cref="StandsOnFloor(in Unit, float, bool, out string)"/>
    /// <param name="vegetation">Does any renderer under the unit root sit on a Foliage-family
    /// shader. REPORTED ONLY since ModBuild 258 — it decides nothing and the two overloads
    /// therefore return the identical verdict for identical geometry. It is kept because it is
    /// half of the column that retired the TREE arm (see the file header's tombstone), and a fact
    /// that costs one cached shader lookup and settles a whole round from one grep is worth its
    /// keep.</param>
    internal static bool StandsOnFloor(in Unit unit, float floorY, bool figureAncestry,
                                       bool vegetation, out string why)
        => StandsOnFloor(unit, floorY, figureAncestry, vegetation, wallCut: false, out why);

    /// <inheritdoc cref="StandsOnFloor(in Unit, float, bool, bool, out string)"/>
    /// <param name="wallCut">MODBUILD 266 — the unit's climb passed a WALL ENTITY inside its own
    /// bounded four-level window, so what sits immediately above this unit is a wall and the unit
    /// is a FRAGMENT of that wall's feature. Measured by the caller off the unit walk itself, not
    /// by an unbounded <c>GetComponentInParent&lt;ProceduralWall&gt;()</c> climb — see the file
    /// header for the three subjects that probe gets wrong.</param>
    internal static bool StandsOnFloor(in Unit unit, float floorY, bool figureAncestry,
                                       bool vegetation, bool wallCut, out string why)
    {
        FloorVerdict verdict = Judge(unit, floorY, figureAncestry, wallCut);
        why = Describe(unit, floorY, figureAncestry, vegetation, wallCut, verdict);
        return verdict == FloorVerdict.StandsOnFloor;
    }

    /// <summary>
    /// WHICH TERM DECIDED — the verdict of <see cref="StandsOnFloor"/> without its sentence.
    ///
    /// <para><b>PERF E (ModBuild 279). WHY THIS SPLIT EXISTS AND WHAT IT MAY NOT DO.</b> The
    /// user's escalation is <i>"Ich will aber eigentlich gar keine spürbaren Ruckler - nicht nur
    /// seltenere"</i>, and the wall-fade commit measured 94.8 ms at ModBuild 277 (72.84 ms after
    /// ModBuild 281 — WALL-FADE-CLOSEOUT.md §3) with a coefficient of variation
    /// under 1 % — a fixed amount of work over a fixed population. Part of that fixed amount is
    /// diagnostic: <see cref="StandsOnFloor"/> was called once per child renderer of every cache
    /// wall (thousands of times per commit) and built a <c>shape</c> column plus one of five
    /// sentences on EVERY call, on net472 where every <c>$"…"</c> is a
    /// <c>string.Format(string, object[])</c> — an array allocation plus a box per float. The
    /// memo above the call site caches the MEASUREMENT (the unit), never the verdict, so the
    /// sentence was rebuilt for every renderer of every unit and then, for all but a handful,
    /// dropped on the floor.</para>
    ///
    /// <para><b>NOT ONE DECISION PREDICATE MOVES.</b> The five terms below are the five
    /// <c>if</c>s of the ModBuild-266/258 body, in the same order, with the same constants and
    /// the same operands; <see cref="Describe"/> holds the five sentences verbatim, character
    /// for character, including the <c>shape</c> column and its ", vegetation]" suffix that the
    /// shipped wire vectors pin. <see cref="StandsOnFloor"/> is now those two calls and nothing
    /// else, so every existing caller — and every wire vector — is bit-identical. What a caller
    /// gains is the ability to ask for the verdict alone and to build the sentence only for the
    /// rows a capped census will actually print.</para>
    ///
    /// <para><c>vegetation</c> is deliberately absent here: it is REPORTED ONLY
    /// (ModBuild 258) and decides nothing, which is exactly why it belongs to
    /// <see cref="Describe"/> and not to this method. If it ever acquires a term, it must be
    /// added HERE and the two halves re-joined — a fact that decides something and lives only in
    /// the description is the "a default value names an unbuilt thing" failure wearing new
    /// clothes.</para>
    /// </summary>
    internal static FloorVerdict Judge(in Unit unit, float floorY, bool figureAncestry,
                                       bool wallCut)
    {
        if (unit.RendererCount <= 0 || unit.RendererCount > MaxRenderers)
            return FloorVerdict.NotPropSizedByCount;
        if (unit.WidestSpanXZ > MaxSpanWU)
            return FloorVerdict.NotPropSizedBySpan;
        if (unit.MinY - floorY > FootBandWU)
            return FloorVerdict.WallMountedDressing;
        // WALL-FEATURE FRAGMENT, the FLOOR arm only — ModBuild 266, bücherregale1/2.jpg and
        // regal_brett.jpg. See the file header for the two log lines this is read off. In short:
        // the identical shelf prefab measures 3.4 wu as the wall feature it belongs to (and is
        // correctly refused by the height cap below) and 0.41 wu as a 2-renderer fragment of that
        // same feature — and as a fragment it passes every term here and is protected on every
        // path. The rule was judging a piece of a wall, which is the inverse of the split this
        // file exists to fix ("a skull … does not [look airborne] once it is judged as part of
        // the skeleton it belongs to").
        //
        // Asked AFTER the foot band, because a unit whose foot is already above the band is
        // refused there for the right reason and with the better sentence; asked BEFORE the
        // height cap, because a fragment is small BY CONSTRUCTION and would sail through it —
        // that is what being a fragment means.
        //
        // CONJUNCT 2, `unit.MaxY - floorY > FootBandWU`, is the guard that stops this eating the
        // floor, and it is load-bearing rather than decorative: this tileset really does parent
        // ground cover under walls, e.g.
        // 'L : (…)/Walls/Wall 2/Generated Content/PCG_FR_Floor_Grass_Hex_Half_PR/FR_Floor_Grass_Half_01'
        // in the same log. Those units top out ~0.2 wu over the floor, stay inside the band, and
        // keep their protection. No new constant: it is the same 1.0 wu as GroundExclusionHeightWU,
        // which is the number this whole subsystem already means "ground, never wall" by.
        //
        // WHAT THIS CANNOT REACH, stated as consequences rather than hopes:
        //   * every FIGURE and actor prop — the FIGURE arm never asks this question, and the
        //     round-7 ruling (figures are never touched) is untouched;
        //   * the fountain and the doorway arch — their rects are asked by the caller before
        //     wallCut can be set, and are independently enforced at three other choke points;
        //   * anything with NO authored wall-fade channel — this term removes a VETO at the
        //     collection choke point, it does not grant admission. A renderer still has to carry
        //     a WallFade-family shader name or a live _WallFade_On/_ToggleWallfade gate to be
        //     collected at all, and the shelf demonstrably does (both its renderers are logged as
        //     "wall renderer of 'Wall 4'" in the instance that fades).
        //
        // FALSIFIER: the STANDING PROP census now prints "under a wall" / "no wall above" for
        // every unit and a per-subject roll-call for the five named cases. If the next log shows
        // 'CV_Ice_Crystal_Form_02', a skeleton limb or 'LightShaft_Prefab (1)' as "under a wall",
        // the four-level window is NOT narrower than the unbounded climb in this tileset and this
        // term must be withdrawn — not retuned.
        if (!figureAncestry && wallCut && unit.MaxY - floorY > FootBandWU)
            return FloorVerdict.WallFeatureFragment;
        // HEIGHT, the FLOOR arm only. No escape hatch since ModBuild 258: the TREE arm that used
        // to lift this cap fired once in a whole hardware session and lifted it for a WALL.
        if (!figureAncestry && unit.Height > MaxHeightWU)
            return FloorVerdict.Architecture;
        return FloorVerdict.StandsOnFloor;
    }

    /// <summary>Which term of <see cref="Judge"/> decided. <see cref="StandsOnFloor"/> is
    /// <c>== StandsOnFloor</c> and nothing else, so the enum cannot drift from the verdict.
    /// The names are the refusal sentences' own subjects, so a reader of a census line and a
    /// reader of a counter are looking at the same word.</summary>
    internal enum FloorVerdict
    {
        /// <summary>Every term passed: a floor prop (FLOOR arm) or a figure/actor prop
        /// (FIGURE arm). This is the only value <see cref="StandsOnFloor"/> returns true for.
        /// </summary>
        StandsOnFloor = 0,
        /// <summary>0 renderers, or more than <see cref="MaxRenderers"/>.</summary>
        NotPropSizedByCount,
        /// <summary>Wider than <see cref="MaxSpanWU"/> in XZ.</summary>
        NotPropSizedBySpan,
        /// <summary>Its foot hangs more than <see cref="FootBandWU"/> over the floor.</summary>
        WallMountedDressing,
        /// <summary>ModBuild 266, FLOOR arm only: a wall sits immediately above it inside the
        /// unit walk's own window — see <see cref="WallFragmentTag"/>.</summary>
        WallFeatureFragment,
        /// <summary>FLOOR arm only: taller than <see cref="MaxHeightWU"/>.</summary>
        Architecture,
    }

    /// <summary>
    /// THE SENTENCE for a verdict — the five strings that used to be built inline in
    /// <see cref="StandsOnFloor"/>, moved here unchanged so a caller can decide whether the
    /// census will print this row BEFORE paying for it.
    ///
    /// <para>Every character is the ModBuild-266 text: the same operands, the same format
    /// specifiers, the same <c>shape</c> column with PROVENANCE before the vegetation flag (the
    /// shipped wire vectors pin that column to END in ", vegetation]" / ", no vegetation]", so a
    /// new column may be inserted there but never appended). Two wire vectors assert on
    /// <c>why.Contains("MOUNTED")</c> and <c>why.Contains("architecture")</c> and both still
    /// read the same sentence.</para>
    /// </summary>
    internal static string Describe(in Unit unit, float floorY, bool figureAncestry,
                                    bool vegetation, bool wallCut, FloorVerdict verdict)
    {
        // SHAPE, printed on EVERY line this method produces — verdict and refusal alike. This is
        // the column that killed the ModBuild-257 TREE arm in one grep (h/w 0.23…2.11 across the
        // session, the single value over 2.0 belonging to a WALL). It decides nothing and stays
        // printed for exactly that reason: reading the mode instead of the distribution has cost
        // this project a round before.
        string shape = $"h {unit.Height:0.0} wu / w {unit.WidestSpanXZ:0.0} wu = "
                       + $"{unit.SlendernessHW:0.00} h/w"
                       // PROVENANCE goes BEFORE the vegetation flag deliberately: the shipped
                       // wire vectors pin this column to END in ", vegetation]" / ", no
                       // vegetation]" (that suffix is the TREE arm's tombstone), so a new column
                       // may be inserted here but never appended.
                       + (wallCut ? ", under a wall" : ", no wall above")
                       + (vegetation ? ", vegetation" : ", no vegetation");
        float foot = unit.MinY - floorY;
        switch (verdict)
        {
            case FloorVerdict.NotPropSizedByCount:
                return $"{unit.RendererCount} renderer(s) — not prop-sized (cap {MaxRenderers}) "
                       + $"[{shape}]";
            case FloorVerdict.NotPropSizedBySpan:
                return $"span {unit.SpanX:0.0}x{unit.SpanZ:0.0} wu — not prop-sized "
                       + $"(cap {MaxSpanWU:0.0} wu) [{shape}]";
            case FloorVerdict.WallMountedDressing:
                return $"foot {foot:0.0} wu over floor {floorY:0.0} — wall-MOUNTED dressing, keeps "
                       + $"fading with its masonry (band {FootBandWU:0.0} wu) [{shape}]";
            case FloorVerdict.WallFeatureFragment:
                return $"{WallFragmentTag}: foot {foot:0.0} wu over floor {floorY:0.0}, top "
                       + $"{unit.MaxY - floorY:0.0} wu, height {unit.Height:0.0} wu over "
                       + $"{unit.RendererCount} renderer(s) — a WALL sits immediately above this unit "
                       + $"inside the unit walk's own window, so this is a FRAGMENT of that wall's "
                       + $"feature and not a prop standing in the room; it rises out of the ground "
                       + $"band ({FootBandWU:0.0} wu), so the whole unit fades with the wall "
                       + $"[{shape}]";
            case FloorVerdict.Architecture:
                return $"height {unit.Height:0.0} wu — architecture, not a floor prop "
                       + $"(cap {MaxHeightWU:0.0} wu), so the WHOLE unit fades with its wall, base "
                       + $"included [{shape}]";
            default:
                return $"foot {foot:0.0} wu over floor {floorY:0.0}, height {unit.Height:0.0} wu, span "
                       + $"{unit.SpanX:0.0}x{unit.SpanZ:0.0} wu, {unit.RendererCount} renderer(s) — "
                       + (figureAncestry ? "figure/actor prop" : "floor prop")
                       + $" [{shape}]";
        }
    }

    /// <summary>
    /// THE FIGURE ARM'S WALL-SECTION TERM — "die Großen Bücherregale die als ganze Wandsektion
    /// verwendet werden vom Spiel und nicht ausblenden" (user, 2026-08-25, hardware, ModBuild 274).
    ///
    /// <para><b>THE SUBJECT, from that log verbatim.</b> Four renderers under one root:
    /// <c>'CR_ST_WallShelf_Stone_Wood' FIGURE arm, under a wall, HAS a fade channel, PROTECTED
    /// @ Wall 4/Generated Content/PCG_CR_ST_WallShelf_Stone_Wood/CR_ST_WallShelf_Stone_Wood</c>,
    /// measured as <c>foot 0.0 wu over floor 0.0, height 3.3 wu, span 2.3x1.8 wu, 4 renderer(s) —
    /// figure/actor prop</c>. A 3.3 wu bookshelf standing on the floor INSIDE a wall's own
    /// <c>Generated Content</c>, refused to <c>'Wall 4'</c> 23 rescans in a row by this file's
    /// FIGURE arm, and therefore left drawing over a wall whose fade is 1.00.</para>
    ///
    /// <para><b>WHY THE ModBuild-266 TERM DID NOT ALREADY CATCH IT, and this is the whole finding.</b>
    /// <see cref="WallFragmentTag"/> is written <c>!figureAncestry &amp;&amp; wallCut &amp;&amp; …</c>
    /// — the FLOOR arm only. The same tileset hangs an <c>Animator</c> over this shelf (ModBuild
    /// 157 measured exactly that for <c>CR_ST_WallShelf_Stone_Bone</c>), so it takes the FIGURE
    /// arm, where neither the wall-fragment term nor <see cref="MaxHeightWU"/> is asked at all. The
    /// rule was judging a piece of wall by a rule that has no wall term on the arm it took. Two
    /// gates further on than the four sites ModBuild 266 lifted, and the same class of miss.</para>
    ///
    /// <para><b>THE TERM, and every conjunct is one that already exists.</b> This method carries
    /// the two the caller can afford to ask for every renderer:</para>
    /// <list type="number">
    /// <item>THE FIGURE ARM — this term exists precisely because the FLOOR arm's is unreachable
    ///   here, so it fires nowhere else and cannot double-refuse anything.</item>
    /// <item><paramref name="wallCut"/> — a wall inside the unit walk's own BOUNDED window
    ///   (<c>WallSegmentFade.PropUnit.cs</c>, <c>WallInUnitWindow</c>). Never
    ///   <c>GetComponentInParent&lt;ProceduralWall&gt;()</c>: that probe climbs to the scene root
    ///   and in this very session answers YES for a rigged skeleton's thighs, a light shaft and
    ///   the ice formation the user has ruled may stay. Same argument, same evidence, as the
    ///   FLOOR arm's conjunct 1 — read that comment.</item>
    /// <item>THE UNIT RISES OUT OF THE GROUND BAND (<see cref="FootBandWU"/>). This is the
    ///   conjunct that keeps the FLOOR of the world out of the term, and it is load-bearing rather
    ///   than decorative here too: the ModBuild-274 log has FIGURE-arm floor cover parented under
    ///   walls — <c>'FR_Floor_Grass_Half_01' FIGURE arm, under a wall … PROTECTED @ Walls/Wall 1/
    ///   Generated Content/PCG_FR_Floor_Grass_Hex_Half_PR</c> — and those units top out ~0.2 wu
    ///   over the floor. No new constant: the same 1.0 wu this subsystem already means "ground,
    ///   never wall" by.</item>
    /// </list>
    ///
    /// <para><b>THE THIRD CONJUNCT IS THE CALLER'S AND IS NOT OPTIONAL:</b>
    /// <c>FadeDriver.IsWallGeneratedDressing</c> — the wall generator built it AND no
    /// <c>ActorBehaviour</c> / <c>CInteractableActor</c> sits anywhere above it. THE ROUND-7
    /// RULING IS NOT RELAXED (figures are NEVER touched, Lights-rule severity): that actor chain
    /// stays an ABSOLUTE VETO and provenance is added ON TOP of it, never instead of it. It is
    /// asked LAST, in the caller, because it is the only ancestor walk in the conjunction and the
    /// three terms above have already cut the population to a handful per rescan.</para>
    ///
    /// <para><b>WHAT THIS CANNOT REACH, as consequences rather than hopes.</b> Every unit on the
    /// FLOOR arm (the skeleton in skelet.jpg reads <c>FLOOR arm</c> in this log, both feet);
    /// anything with no wall in its own bounded window, which is where
    /// <c>CV_Ice_Crystal_Form_02/03</c> and <c>LightShaft_Prefab (1)</c> sit and which is also why
    /// they resolve no prop unit at all (user ruling 2026-08-24 — the crystal formation STAYS);
    /// the fountain and the doorway arch, whose rects are applied by the caller BEFORE
    /// <paramref name="wallCut"/> can be set (2026-08-09, 2026-08-02); ground cover, by conjunct 3;
    /// and anything with no authored wall-fade channel — this term removes a VETO at the
    /// collection choke point, it does not grant admission.</para>
    ///
    /// <para><b>FALSIFIED BY:</b> the census's <c>[WALL-SECTION]</c> roster naming a hero, a
    /// monster, a summon, a floor hex, the crystal formation or a light shaft. Then one of the
    /// three conjuncts is not the discriminator and the term must be WITHDRAWN, not retuned.</para>
    /// </summary>
    /// <param name="why">Always measured, never a constant. When this returns TRUE it is the
    /// refusal sentence the caller prints once provenance also holds; when it returns FALSE it
    /// names WHICH term refused and the number it refused on, which is what the census's FAILURE
    /// arm prints. "The rule did not fire" without "and by which number" is what cost this defect
    /// its fourth round once already.</param>
    internal static bool IsWallBuiltSection(in Unit unit, float floorY, bool figureAncestry,
                                            bool wallCut, bool vegetation, out string why)
        => IsWallBuiltSection(unit, floorY, figureAncestry, wallCut, vegetation,
                              enclosingTop: float.NaN, enclosingLabel: string.Empty,
                              enclosingRenderers: 0, out why);

    /// <inheritdoc cref="IsWallBuiltSection(in Unit, float, bool, bool, bool, out string)"/>
    /// <param name="enclosingTop">MODBUILD 291 — THE TOP OF THE WALL-BUILT UNIT THIS RENDERER
    /// BELONGS TO, over the same floor plane; <c>NaN</c> when the four-level prop-unit walk
    /// resolves no unit around it. Conjunct 3 is asked of <c>max(top, enclosingTop)</c>.
    ///
    /// <para><b>THE REPORT.</b> User, 2026-08-25, <c>sollte_faden.jpg</c>: <i>"In der Map fandet
    /// ein Wandteil nicht … Eventuell weil da auch Kristall dabei ist? Das hier ist aber nicht der
    /// Kristall auf dem Boden der nicht faden soll (den gibts in dem Level auch), sondern das was
    /// wirklich als Wand vor den Figuren zu sehen ist mit dem Gestein daneben. Das sollte wie jedes
    /// andere Element auch faden."</i> A dark stone column with a large crystal slab beside it,
    /// standing between him and the figures, at a wall whose fade is 1.00.</para>
    ///
    /// <para><b>WHY THE OLD CONJUNCT 3 REFUSED IT, from the ModBuild-290 log verbatim.</b>
    /// <c>'CV_Ice_Crystal_Form_04' GEOMETRY half refused: FIGURE arm under a wall, but the unit
    /// does NOT rise out of the ground band: top 0.9 wu over floor 0.0 ≤ 1.0 wu, height 1.0 wu
    /// over 1 renderer(s)</c> — and that one renderer is a knee-high crystal at the FOOT of
    /// <c>Wall 1/Generated Content/PCG_CV_Ice_Feature_Medium_02_PR</c>, which the very same log
    /// measures, through the FLOOR arm, as <c>foot -0.5 wu … top 3.5 wu, height 4.0 wu over 22
    /// renderer(s)</c>. Two units, one physical wall feature. The FIGURE arm's root is
    /// <c>FigurePropRootOf</c> — the nearest <c>Animator</c> ancestor — and for this tileset's
    /// crystals that is the crystal's own node, so the arm judged a FRAGMENT and read the
    /// fragment's height. This is the ModBuild-266 finding one gate further on: the rule was
    /// judging a piece of a wall. Nothing about the ruling changed; the measurement did.</para>
    ///
    /// <para><b>WHY WIDENING THE MEASURED UNIT CANNOT EAT THE FLOOR.</b> The enclosing unit comes
    /// from <c>WallSegmentFade.PropUnit.cs</c>'s bounded four-level walk, which stops dead at a
    /// wall entity, at a segment anchor and at container scale. For the ground cover this tileset
    /// really does parent under walls the walk resolves either NO unit at all (a one-renderer
    /// wrapper — <c>Wall N/Generated Content/PCG_CR_Floor_BaseHex_Plain/EN_CR_Floor_BaseHex_Plain</c>,
    /// see the note on <see cref="StandsOnFloor"/>) or a unit that is itself ground cover
    /// (<c>PCG_FR_Floor_Grass_Hex_Half_PR</c>, top ~0.2 wu). Enlarging the measured unit lifts the
    /// verdict only where the unit a fragment belongs to ACTUALLY rises out of the ground band —
    /// which is what "the game is using this as a wall section" means. <c>max</c> and never a
    /// substitution, so a figure root that already spans MORE than its prop unit keeps its own
    /// number.</para>
    ///
    /// <para><b>THE OTHER THREE CONJUNCTS ARE UNTOUCHED</b> and each is independently fatal to the
    /// protected formation: the FLOOR crystal formation the user rules must STAY is parented
    /// <c>Generated Content/Full/PCG_CV_Ice_Clutter_Floor_0N_PR/CV_Ice_Crystal_Form_02 (…)</c> —
    /// <c>Full</c> and <c>Generated Content</c> are SIBLINGS of <c>Walls/</c>, so
    /// <paramref name="wallCut"/> is FALSE for it and conjunct 2 refuses it before this line is
    /// reached (its roll-call row reads <c>'CV_Ice_Clutter_01' FLOOR arm, no wall above … PROTECTED</c>
    /// in the ModBuild-290 log). The caller's <c>IsWallGeneratedDressing</c> keeps the
    /// <c>ActorBehaviour</c> / <c>CInteractableActor</c> chain as an ABSOLUTE VETO (round-7 ruling,
    /// Lights-rule severity, NOT relaxed).</para>
    ///
    /// <para><b>FALSIFIED BY:</b> a <c>[WALL-SECTION]</c> roster row whose path does NOT contain a
    /// <c>Wall</c> node — a hero, a monster, a summon, a floor hex, or a
    /// <c>PCG_CV_Ice_Clutter_Floor_*</c> member. The name of the asset is NOT the falsifier and
    /// never was: this level uses <c>CV_Ice_Crystal_Form_02</c> BOTH as protected floor clutter
    /// under <c>Generated Content/Full/</c> AND as wall furniture under
    /// <c>Wall N/Generated Content/</c>, so the ModBuild-275 falsifier as written ("the roster
    /// naming … the crystal formation") fires on the correct behaviour. It is the PARENT that
    /// adjudicates, which is why the roster prints the path.</para></param>
    /// <param name="enclosingLabel">The enclosing unit root's name, printed so the sentence says
    /// WHICH unit carried the verdict rather than only that one did.</param>
    /// <param name="enclosingRenderers">How many renderers that unit owns — the column that makes
    /// "a fragment of one" and "the feature of twenty-two" tellable apart at a glance.</param>
    internal static bool IsWallBuiltSection(in Unit unit, float floorY, bool figureAncestry,
                                            bool wallCut, bool vegetation, float enclosingTop,
                                            string enclosingLabel, int enclosingRenderers,
                                            out string why)
    {
        string shape = $"h {unit.Height:0.0} wu / w {unit.WidestSpanXZ:0.0} wu = "
                       + $"{unit.SlendernessHW:0.00} h/w"
                       + (wallCut ? ", under a wall" : ", no wall above")
                       + (vegetation ? ", vegetation" : ", no vegetation");
        float top = unit.MaxY - floorY;
        if (!figureAncestry)
        {
            why = $"FLOOR arm — the '{WallFragmentTag}' term already covers this arm, so this one "
                  + $"is never asked here [{shape}]";
            return false;
        }
        if (!wallCut)
        {
            why = $"FIGURE arm but NO wall inside the unit walk's own bounded window — top "
                  + $"{top:0.0} wu over floor {floorY:0.0}, height {unit.Height:0.0} wu over "
                  + $"{unit.RendererCount} renderer(s); an unbounded ProceduralWall climb is NOT "
                  + $"substituted here (it answers YES for the ice formation, a light shaft and a "
                  + $"skeleton's thighs) [{shape}]";
            return false;
        }
        // MODBUILD 291 — WHICH UNIT THE GROUND-BAND QUESTION IS ASKED OF. Never a substitution:
        // the figure root's own top still counts, and a figure root that spans more than its prop
        // unit keeps its own number. The enclosing term can only ever RELEASE, never protect.
        bool haveEnclosing = !float.IsNaN(enclosingTop);
        float decidingTop = haveEnclosing && enclosingTop > top ? enclosingTop : top;
        // The clause the census has to be able to name for the reported segment: WHICH unit was
        // measured, how big it is, and what its top was. A refusal that prints only the number it
        // refused on cannot say that the number came off the wrong body.
        string unitColumn = haveEnclosing
            ? $"measured on the wall-built unit '{enclosingLabel}' "
              + $"({enclosingRenderers} renderer(s), top {enclosingTop:0.0} wu) that this "
              + $"{unit.RendererCount}-renderer fragment (top {top:0.0} wu) belongs to"
            : $"measured on this unit alone ({unit.RendererCount} renderer(s), top {top:0.0} wu) "
              + "— the four-level prop-unit walk resolves no unit around it";
        if (decidingTop <= FootBandWU)
        {
            why = $"FIGURE arm under a wall, but the unit does NOT rise out of the ground band: "
                  + $"top {decidingTop:0.0} wu over floor {floorY:0.0} ≤ {FootBandWU:0.0} wu, "
                  + $"height {unit.Height:0.0} wu over {unit.RendererCount} renderer(s) — this is "
                  + $"floor cover parented under a wall, and it keeps its protection; "
                  + $"{unitColumn} [{shape}]";
            return false;
        }
        why = $"{WallSectionTag}: foot {unit.MinY - floorY:0.0} wu over floor {floorY:0.0}, top "
              + $"{decidingTop:0.0} wu, height {unit.Height:0.0} wu, span {unit.SpanX:0.0}x"
              + $"{unit.SpanZ:0.0} wu over {unit.RendererCount} renderer(s) — the WALL GENERATOR "
              + $"built this and no actor component sits above it, a wall is inside the unit "
              + $"walk's own window and the unit rises clear of the ground band "
              + $"({FootBandWU:0.0} wu), so the game is using it AS a wall section and the whole "
              + $"unit fades with that wall; {unitColumn} [{shape}]";
        return true;
    }

    /// <summary>How big a unit is for the FADE WRITE census's ordering. The largest of the three
    /// AABB extents, and the census sorts ASCENDING on it: a skull is small, and four rounds have
    /// now been spent on a census that could truncate the one interesting row. Deliberately not a
    /// volume — a flat sheet of parchment has almost none and is not what is being looked
    /// for.</summary>
    internal static float SizeRank(float spanX, float spanY, float spanZ)
    {
        float m = spanX > spanY ? spanX : spanY;
        return m > spanZ ? m : spanZ;
    }

    /// <summary>
    /// A prop is TORN when a fade path wrote SOME of its renderers and left the rest solid. That
    /// is the exact shape of skelet.jpg — skull gone, ribcage and legs still there — and it is the
    /// comparison no previous census printed. One definition, here, so the census and any future
    /// guard cannot drift apart on what "torn" means.
    ///
    /// <para><b>THE PRECONDITION, and it is not optional (ModBuild 271).</b>
    /// <paramref name="written"/> and <paramref name="total"/> MUST be counted over the SAME
    /// population of renderers, and <paramref name="written"/> must be DISTINCT renderers. This
    /// predicate cannot check that and will happily file a fully-written prop as a defect if the
    /// caller does not: ModBuild 270's census passed a numerator counted on one transform and a
    /// denominator counted over that transform's whole subtree, and the result was that the five
    /// most frequently reported torn props of an entire hardware session — 166, 54, 31, 26 and 18
    /// occurrences — were all intact. See <c>WallSegmentFade.FadeCensus.cs</c>,
    /// <c>BuildFadeUnits</c>, which is where the invariant is established and documented.</para>
    /// </summary>
    internal static bool IsTorn(int written, int total) => written > 0 && written < total;
}
