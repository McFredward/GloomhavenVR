using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP TABLE'S LEGS AND ITS UNDERSIDE — four legs, one at each corner of the table the campaign
/// map lies on, plus a panel that closes the game slab's open bottom, built PROCEDURALLY at runtime
/// as ONE mesh, standing on the floor of the bundled 3D environment and present ONLY in the two
/// bundled 3D environments.
///
/// <para>USER, verbatim (translated), against ModBuild 197: "There really are no benches, I must
/// have dreamt that. But I don't like your benches — remove them again and close the topic for now.
/// Instead I want the TABLE to get TABLE LEGS at its 4 CORNERS, and these should STAND ON THE FLOOR
/// of the environment! The texture should be the SAME as the table's. The table legs should only be
/// visible in the two 3D environments — in mixed reality, in NO environment, or in the DEFAULT
/// environment they should not be there." Every clause of that sentence is a constraint below, and
/// each one is answered by a MEASUREMENT rather than by an assumption.</para>
///
/// <para>THE TABLE IS A REAL GAME OBJECT, AND THAT CORRECTS TWO SHIPPED COMMENTS. Until ModBuild 197
/// this room modelled "the table" as the parchment's world AABB widened by a 0.20 m rim, because the
/// <c>MAP SCENE REPORT</c> prints <c>TOTAL 0 renderer(s)</c> under the map roots besides the
/// parchment. That census is scoped to the map ROOTS. The bench class's one-shot neighbour survey —
/// built precisely to falsify the claim — swept the whole scene instead and found the table
/// standing right there, a sibling rather than a child:</para>
/// <code>
///   'GH_Map_TableTop_Lg' L0 size (306.88, 29.29, 454.81) = 1.55 x 0.15 x 2.30 m,
///                        centre offset (0.00, -0.08, 0.00) m from the map's centre/top
/// </code>
/// <para>A 1.55 x 2.30 m wooden slab 0.15 m thick whose TOP FACE sits 6 mm under the parchment: that
/// is the tabletop the player sees, it belongs to the GAME (layer 0, excluded from nothing, drawn by
/// the same forward head camera that draws everything else here), and it has no legs. So the legs
/// are placed at the corners of THAT renderer's world bounds and are skinned with THAT renderer's own
/// material object, so "the same texture as the table" is an identity and not a match.
/// <see cref="TryFindTable"/> re-derives it by measurement every time this builds and never by
/// name.</para>
///
/// <para>MODBUILD 198 STOOD THEM ON THE MAP INSTEAD, AND ALL FOUR REJECTED FAULTS ARE THAT ONE
/// MISTAKE. Its own hardware line names the renderer it chose: <c>'GH_Campaign_Map' ... 0.96 x 0.00 x
/// 1.20 m ... material 'GloomhavenVR.MapRoom.MapUnlit.0'</c> — the PARCHMENT, wearing the unlit
/// override this mod itself puts on it. The sweep had no test for "is not the parchment" (its doc
/// claimed one; its code had none) and broke ties by preferring the SMALLEST footprint, which the map
/// wins. Everything the user reported follows mechanically:</para>
/// <list type="bullet">
///   <item>"die 4 Ecken der Karte, nicht die 4 Ecken des Holztisches" — the corners came from the
///   map's 0.96 x 1.20 m AABB instead of the table's 1.55 x 2.30 m one.</item>
///   <item>"ich will das die Beine eben diese Textur der Holzplatte haben" — the legs took submesh 0
///   of that renderer, i.e. the CAMPAIGN MAP's unlit material. The pale cream in the photograph is
///   the parchment's border. The UV window was never the problem; the material was.</item>
///   <item>"die Tischbeine schauen etwas oben raus" — the head was welded 5 mm up from the underside
///   of a 0.6 mm sheet, i.e. 4.4 mm above the map's visible face.</item>
/// </list>
/// <para>Two independent measurements now make that impossible: identity (the parchment renderer, its
/// transform and its whole chain are excluded) and thickness (a candidate must be a BOARD of at least
/// <see cref="MinTableThicknessMeters"/>, against a 0.6 mm map and a 148 mm table). Either one alone
/// would have changed 198's answer.</para>
///
/// <para>MODBUILD 200: THE GAP, AND WHY 199'S ARITHMETIC WAS RIGHT AND ITS PREMISE WRONG. The user
/// photographed daylight between the legs and the table (<c>.planning/debug/Tischbeine_Lücke.jpg</c>)
/// against a build whose own line reads <c>the leg tops end at y=-29.62, which is 5.0 mm UP INTO the
/// slab from its underside (y=-30.61)</c>. Both statements are true, because they are about different
/// surfaces. <c>Renderer.bounds.min.y</c> is the lowest point ANYWHERE in the mesh; the leg has to
/// meet the underside over a CORNER. Any apron, skirt, moulding, bevel or slight tilt in
/// <c>GH_Map_TableTop_Lg</c> puts the box's floor below the wood, and the leg stops short by exactly
/// that difference. The photograph is what distinguishes the two: the near leg's TOP CAP is visible
/// and lit — a bright horizontal bar confined to precisely the leg's own screen width, sitting under
/// the slab's dark side face — and an up-facing face 5 mm inside a 148 mm board cannot be seen from a
/// camera above that board. The fault is on BOTH near and far legs, not only the far one; the far
/// one's cap is simply unlit and reads as dark-on-dark.</para>
///
/// <para>THE PROFILE CANNOT BE SAMPLED, SO THE REFERENCE FACE IS CHANGED INSTEAD. The tabletop's mesh
/// is NOT CPU-readable — this class's own texture line says so — so its triangles cannot be walked.
/// Two things are done about that, and the second is what makes the fix structural:</para>
/// <list type="number">
///   <item>PER LEG, EACH CORNER IS PROBED. <see cref="TryMeasureUnderside"/> casts a ray up under the
///   corner against the table's own collider and <see cref="TryMeasureFloor"/> casts one down against
///   the room's, so where colliders exist the underside and the ground are MEASURED where the leg
///   actually is. The four legs no longer share a length.</item>
///   <item>WHERE NOTHING ANSWERS, THE HEAD IS HUNG FROM THE **TOP** FACE. The top face is
///   unambiguous and is confirmed independently every build (the parchment lies on it; the sweep
///   prints the residual, 7 mm). <see cref="HeadInsetBelowTopFaceMeters"/> puts the head 20 mm below
///   it — 128 mm above the box's floor at the real tabletop — so whatever the true underside is, as
///   long as it lies inside the slab's own AABB the head is inside the wood and the leg emerges
///   exactly at it. A gap is then not unlikely; it is unrepresentable.</item>
/// </list>
/// <para><see cref="DescribeLegs"/> prints one row per leg — floor Y, underside Y, length, and the
/// residual gap in millimetres — plus the comparison count, so "no gap" and "never measured" cannot
/// print the same thing.</para>
///
/// <para>THEY STAND ON THE ROOM'S FLOOR, NOT ON THE PLAYER'S. Those are two different planes and the
/// gap between them is why the bench class buried its feet. The room is placed by the diorama rule
/// (<c>SkyAlternative.TryPlaceRoom</c>: floor = board underside − FloatGap) and the seat by
/// <c>MapRoomSeat.TableTopHeightMeters</c>, and the captured session prints the disagreement:
/// "floor dropped ... to y -178.45; the player's real floor is y -154.55, i.e. 23.90 world units =
/// perceived 0.12 m above the room floor". A bench mostly hides its own feet, so 197 cut them 0.20 m
/// below the PLAYER'S floor and called it done. A table leg's whole job is to be seen reaching the
/// ground, so that trade is not available here: the legs are stood on the ROOM's plane, read from
/// the room root's own transform, which <c>BuildEnvironmentRooms</c> authors the floor at
/// ("Both rooms have CLOSED opaque floors around the origin ... floor at y=0") and which
/// <c>TryPlaceRoom</c> sets to <c>floorY</c> and then never writes again ("WORLD-FIXED from now on").
/// <see cref="FootSinkMeters"/> is 25 mm and covers the terrain RELIEF at the table's corners, not a
/// disagreement between two planes — see its own doc for the arithmetic.</para>
///
/// <para>THE STYLE GATE IS LIVE, AND IT IS EVALUATED EVERY FRAME. <see cref="Tick"/> reads
/// <c>SkyAlternative.Style</c> and <c>MixedReality.BackingsWanted</c> on every call and builds or
/// tears down on the next frame, so changing the environment in the VR options panel takes effect
/// where the player is standing rather than at the next room entry. Present for
/// <see cref="SkyStyle.Cellar"/> and <see cref="SkyStyle.SwampNight"/> — the two bundled 3D rooms,
/// and the only two styles that HAVE a floor to stand on — and absent for
/// <see cref="SkyStyle.Default"/>, <see cref="SkyStyle.OffBlack"/> and under mixed reality. The
/// MR clause is not merely obedience to the ruling: a passthrough world has the player's REAL floor
/// in it, several metres from wherever this mod thinks the room floor is, so a leg drawn to a
/// virtual floor plane would visibly miss the real one. <c>MixedReality.BackingsWanted</c> is the
/// same predicate the MR readability treatment keys off, so there is no second switch to drift.</para>
///
/// <para>IT MOVES NOTHING, AND IT CANNOT MOVE A WINDOW. This class only READS: the table renderer's
/// bounds, material and lighting flags, the environment room root's position, the environment's own
/// <c>_MoonDir</c> constant (through <c>sharedMaterials</c>, NEVER <c>materials</c> — the latter
/// instantiates a per-renderer copy and would leave the environment wearing clones this class then
/// leaks), the parchment's bounds, the solved seat's scale, the head camera's culling mask, and the
/// scene's lights with their transforms. It writes no game transform,
/// no game material, no rig value, and it has no Update of its own — it is world-fixed furniture, so
/// after the build frame <see cref="Tick"/> is two field reads and a reference compare. No collider,
/// deliberately: the laser's pick path must not start finding furniture. The two per-leg probes
/// ModBuild 200 adds are <c>Physics.RaycastAll</c> QUERIES — they read the physics scene and write
/// nothing to it, they run on the build frame only (eight casts, once), and a hit is only believed
/// when its collider belongs to the tabletop's or the room's own hierarchy, so nothing the player
/// carries and nothing a window owns can be mistaken for a table or a floor.</para>
///
/// <para>THAT MATTERS BECAUSE OF WHAT ELSE SHIPPED IN 198: every floating window moved to below the
/// table in the same build. It was not this class. Nothing here names or can reach a Canvas, a
/// <c>ConvertedPanel</c>, a <c>GrabbableModal</c>, <c>ModalFallback</c>, <c>PanelPlacement</c>, the
/// seat's POSE (only its <c>Scale</c> and <c>FloorPosition</c> are read, never written) or the rig.
/// The only objects it creates are its own root and one child holding a MeshFilter and a
/// MeshRenderer, and the only property it sets on anything pre-existing is none. The report line
/// states this so a future round does not have to re-derive it.</para>
///
/// <para>AND THE LEGS SHARE THE TABLE'S LAYER, not the mod layer every other prop in this room uses.
/// ModBuild 198 shipped that difference as a written-down open risk ("a realtime light whose culling
/// mask excludes the mod layer would light the two differently"). A leg is part of the table and
/// wears the table's own material object; the only way two such objects are guaranteed to shade alike
/// is to be reachable by one set of lights. <see cref="ChooseLayer"/> takes the table's layer after
/// READING the head camera's mask to confirm it is drawn, and <see cref="DescribeLights"/> prints the
/// census that says whether the difference ever mattered.</para>
///
/// <para>MULTIPLAYER: nothing on the wire and nothing to disagree about. Every input is game-scene
/// state plus this client's own local style dial, so two clients build byte-identical legs with zero
/// packets — and a client on a different environment simply has none, which is a presentation
/// difference exactly like the environment itself already is.</para>
///
/// <para>MODBUILD 201, FAULT 2: THE TABLE HAD NO BOTTOM, AND THE MAP SHOWED THROUGH IT. The user,
/// verbatim (translated): "The table has no real underside — you can see through it from below, and
/// you also see the map lying on the table. I want it to have a tabletop from below as well, so you
/// cannot see through." (<c>.planning/debug/tisch_unten.jpg</c>.) <c>GH_Map_TableTop_Lg</c> draws a
/// top face and four side faces and nothing underneath, so from below the board is an open shell;
/// and what shows through it is the parchment, which <c>MapParchment</c> draws UNLIT — an unlit
/// surface is at full brightness from either side, which is exactly why the map reads as a lit panel
/// hanging inside the table. <see cref="Underside"/> closes it with one CLOSED box of
/// <see cref="UndersideTriangleCount"/> triangles in the same mesh, the same material and the same
/// draw call, living entirely INSIDE the slab's own AABB so it cannot be seen from above, cannot
/// z-fight and cannot change the silhouette the user has already accepted. It is NOT welded to the
/// parchment: the map is still excluded by identity and by the 30 mm board test, both of which still
/// run, and ModBuild 199 was burnt on exactly that confusion.</para>
///
/// <para>MODBUILD 201, FAULT 1: THE LIGHTING IS NOT THIS PROP'S TO FIX, AND THE CENSUS NOW PROVES
/// IT. The user: "the table legs AND THE SIDE OF THE TABLE are lit from the other side ... I would
/// like the lighting to match the environment." ModBuild 200's census answers the first half in one
/// reading — <c>3 enabled light(s) in the scene; 1 of them light layer 0 and layer 27 DIFFERENTLY.
/// 'Map Directional Light' Directional intensity 1.20 mask 0x700DFE37 → table layer 0 LIT</c>, and
/// the other two enabled lights (mask <c>0x00020100</c>) reach NEITHER layer. So exactly one light
/// reaches the tabletop, the legs stand on the tabletop's layer wearing the tabletop's own material,
/// and THE LEGS AND THE TABLE AGREE. What they disagree with is the ROOM — and the room has no
/// realtime light at all: <c>SkyAlternative</c> creates none, and its moon is the authored constant
/// <c>EnvironmentsBuilder.MoonDir</c> baked into the bundle's shaders. A game light versus a baked
/// moon is not a difference a prop can close by shading itself differently; closing it means
/// re-aiming that GAME light along MoonDir while a 3D style is live and restoring it when the style
/// closes, which belongs to the class that owns both the moon and the style lifecycle. This file
/// does the two things it honestly can: it copies the tabletop's own lighting INPUTS onto the prop
/// (<see cref="MirrorTableShading"/>) so the two cannot drift, and it MEASURES the disagreement —
/// <see cref="TryMeasureMoonDirection"/> reads the room's own <c>_MoonDir</c> and
/// <see cref="DescribeLights"/> prints the angle between it and the light that actually reaches the
/// table, so the next round argues from a number instead of from a photograph.</para>
///
/// <para>COST: one combined mesh, <see cref="TriangleCount"/> triangles (<c>4 x 12</c> for the legs
/// plus <see cref="UndersideTriangleCount"/> for the underside), ONE MeshRenderer with ONE material
/// = ONE draw call, built once, no per-frame allocation and no shadow pass. Four legs of four
/// different lengths cost exactly the same as four of one length — the extra shaft ModBuild 200
/// buries in the slab is hidden geometry, not extra geometry.</para>
/// </summary>
internal sealed class MapTableLegs
{
    private const string Scope = "MapRoom";

    /// <summary>The prop root's GameObject name. It is a CONSTANT rather than a literal because
    /// <see cref="TryFindTable"/> now has to recognise this prop's own geometry: the legs may stand on
    /// the game's layer (see <see cref="ChooseLayer"/>), so the sweep's mod-layer test no longer
    /// excludes them and a rebuild could otherwise consider a leg as a tabletop candidate.</summary>
    private const string RootName = "GloomhavenVR.MapTableLegs";

    // ---- THE LEG, IN REAL METRES -------------------------------------------------------------
    // Everything here is multiplied by the rig scale EXACTLY ONCE, in Build, and never again. This
    // room runs at ~198 world units per metre and mixing the two has shipped as a bug here before.

    /// <summary>Side of the leg's square section, real metres. AUTHORED. The tabletop measures
    /// 1.55 x 2.30 m and is 0.15 m thick — a heavy board — so a 10 cm post is what carries it; a
    /// thinner leg reads as a folding table and a thicker one as a pillar.</summary>
    internal const float LegSideMeters = 0.10f;

    /// <summary>How far the leg's OUTER face stands in from the tabletop's edge, real metres.
    /// AUTHORED. Small and non-zero: flush would z-fight with the top's own side face at grazing
    /// angles, and a large inset reads as a pedestal rather than as a corner leg.</summary>
    internal const float EdgeInsetMeters = 0.02f;

    /// <summary>
    /// How far the leg's head pushes up into the SLAB, measured from a PROBED underside, real metres.
    /// Parts that share a face exactly z-fight along it; 5 mm of overlap is invisible and guarantees
    /// no daylight at the joint.
    ///
    /// <para>SINCE ModBuild 200 THIS APPLIES ONLY WHERE <see cref="TryMeasureUnderside"/> ACTUALLY
    /// FOUND THE FACE under that corner. Where it did not — the captured table carries an unreadable
    /// mesh and may carry no collider either — the head is hung from the slab's TOP face instead and
    /// this constant is not used for that leg; see <see cref="HeadInsetBelowTopFaceMeters"/> for why
    /// 199's "5 mm up from <c>bounds.min.y</c>" was measuring from a surface that is not the
    /// underside.</para>
    ///
    /// <para>THE REFERENCE FACE IS THE UNDERSIDE, AND ModBuild 198 SHIPPED THE OTHER ONE. There it
    /// read <c>top.min.y + WeldMeters</c> where <c>top</c> was — because of the selection bug fixed
    /// in <see cref="TryFindTable"/> — the PARCHMENT's bounds, a 0.6 mm decal. 5 mm up from the
    /// underside of a 0.6 mm sheet is 4.4 mm ABOVE its visible face, which is precisely the four pale
    /// rectangles the user photographed sitting ON the map ("Die Tischbeine schauen etwas oben raus").
    /// Against the REAL slab the same arithmetic is safe by two orders of magnitude — 5 mm into a
    /// 148 mm board leaves the leg head 143 mm BELOW the top face — and
    /// <see cref="WeldMaxThicknessFraction"/> makes that structural rather than lucky.</para>
    /// </summary>
    internal const float WeldMeters = 0.005f;

    /// <summary>
    /// The weld is additionally capped at this fraction of the slab's MEASURED thickness, so the leg
    /// head can never reach the top face no matter what the sweep hands back. At the real tabletop
    /// (148 mm) the cap is 37 mm and <see cref="WeldMeters"/>'s 5 mm wins; at a 10 mm ledge the cap
    /// wins and the head stops 2.5 mm up. A leg can only ever emerge from the top of a slab if this
    /// number is raised to 1.
    /// </summary>
    internal const float WeldMaxThicknessFraction = 0.25f;

    /// <summary>
    /// HOW DEEP THE HEAD IS BURIED, MEASURED DOWN FROM THE SLAB'S **TOP** FACE, real metres — and
    /// this constant is ModBuild 200's whole fix.
    ///
    /// <para>THE GAP ModBuild 199 SHIPPED. Its own hardware line reads <c>the leg tops end at
    /// y=-29.62, which is 5.0 mm UP INTO the slab from its underside (y=-30.61)</c>, and the
    /// arithmetic closes perfectly — yet the user photographed daylight between the leg and the
    /// table. The photograph settles which of the two is wrong: the near leg's TOP CAP is visible and
    /// lit (<c>.planning/debug/Tischbeine_Lücke.jpg</c>, a bright horizontal bar confined to exactly
    /// the leg's own screen width, sitting under the slab's dark side face). An up-facing face that is
    /// 5 mm INSIDE a 148 mm board cannot be seen from a camera above the board. So the number that is
    /// wrong is <c>top.min.y</c>: <c>Renderer.bounds</c> is an axis-aligned box and its
    /// <c>min.y</c> is the LOWEST POINT ANYWHERE IN THE MESH, not the height of the underside over the
    /// CORNERS. Any apron, skirt, moulding, bevel or slight tilt in <c>GH_Map_TableTop_Lg</c> — a
    /// single unreadable mesh, see the texture line, so its profile cannot be sampled — puts the box's
    /// floor below the wood the leg actually has to meet, and the leg stops in mid-air by exactly that
    /// difference.</para>
    ///
    /// <para>THE CORRECTION IS TO CHANGE THE REFERENCE FACE, not to add a fudge. The TOP face is
    /// unambiguous and is confirmed by a second, independent measurement every build: the parchment
    /// lies ON it, and the sweep prints the residual (7 mm in the captured session). The bottom face
    /// is a guess about a profile nobody has measured. So the head is placed
    /// <see cref="HeadInsetBelowTopFaceMeters"/> DOWN FROM THE TOP FACE, which at the real tabletop
    /// puts it 20 mm below the top and 128 mm ABOVE the box's floor. Whatever the true underside is,
    /// as long as it lies anywhere inside the slab's own AABB the head is inside the wood and the leg
    /// EMERGES exactly at it. A gap is then not unlikely, it is unrepresentable — and the extra
    /// 128 mm of shaft is hidden inside the board, at zero cost (the same 48 triangles).</para>
    ///
    /// <para>IT STILL CANNOT POKE THROUGH THE TOP, which is the fault ModBuild 198 shipped and 199
    /// fixed: 20 mm below the top face is 27 mm below the map's visible surface, and the inset is
    /// additionally capped at <see cref="HeadInsetMaxThicknessFraction"/> of the measured thickness so
    /// a thin board narrows the burial instead of pushing the head out of the bottom.</para>
    ///
    /// <para>20 mm rather than 5: it must also clear a CHAMFER on the top edge. The leg's outer face
    /// stands only <see cref="EdgeInsetMeters"/> = 20 mm inside the slab's edge, so a head buried only
    /// a few millimetres could be exposed by a bevelled corner. 20 mm down and 20 mm in is a 45°
    /// chamfer's worth of cover.</para>
    /// </summary>
    internal const float HeadInsetBelowTopFaceMeters = 0.020f;

    /// <inheritdoc cref="HeadInsetBelowTopFaceMeters"/>
    internal const float HeadInsetMaxThicknessFraction = 0.40f;

    /// <summary>How far BELOW the slab's AABB floor the per-leg underside probe starts, real metres.
    /// The probe is an upward ray under each corner (see <see cref="TryMeasureUnderside"/>); it starts
    /// clear of the box so a collider face exactly on the box floor is still in front of it.</summary>
    private const float UndersideProbeMarginMeters = 0.05f;

    /// <summary>How far ABOVE and BELOW the room's floor plane the per-leg floor probe looks, real
    /// metres. Sized against the relief the plane's own doc quotes (about +/-19 mm in SwampNight), with
    /// two orders of magnitude of slack, and NOT so far that it could find the tabletop above or a
    /// cellar below. See <see cref="TryMeasureFloor"/>.</summary>
    private const float FloorProbeUpMeters = 0.30f;

    /// <inheritdoc cref="FloorProbeUpMeters"/>
    private const float FloorProbeDownMeters = 0.30f;

    /// <summary>A residual at or below this many millimetres is reported as ZERO gap. It is a
    /// print-rounding threshold, not a tolerance the geometry is allowed to spend.</summary>
    private const float GapFreeMillimetres = 0.05f;

    /// <summary>
    /// The minimum THICKNESS a candidate tabletop must have, real metres — and this one constant is
    /// what makes the difference between the four faults the user reported and none of them.
    ///
    /// <para>ModBuild 198's sweep accepted anything "slab-shaped" (thickness at most
    /// <see cref="SlabThicknessFactor"/> of its smaller horizontal extent) and then preferred the
    /// SMALLEST survivor. The parchment satisfies both: it is 0.962 x 1.200 m and 0.6 mm thick, so it
    /// is the most slab-shaped and the smallest thing in the scene that contains its own footprint —
    /// and the class had no test that excluded it. The hardware log is unambiguous:
    /// <c>'GH_Campaign_Map' ... 0.96 x 0.00 x 1.20 m ... material 'GloomhavenVR.MapRoom.MapUnlit.0'</c>
    /// — the legs were stood at the MAP's corners and skinned with the MOD's own map material. All of
    /// (a) wrong corners, (b) pale, (c) poking through follow from that one line.</para>
    ///
    /// <para>30 mm is chosen against the two real measurements and not between them: the parchment is
    /// 0.6 mm (50x under) and the tabletop is 148 mm (5x over), so no plausible re-authoring of either
    /// asset crosses it. A TABLE IS A BOARD; A MAP IS A DECAL, and that is a difference of kind.</para>
    /// </summary>
    internal const float MinTableThicknessMeters = 0.03f;

    /// <summary>
    /// How far below the room's floor PLANE each foot is cut, real metres — and this number is 25 mm
    /// rather than the bench class's 200 mm because it is covering something much smaller.
    ///
    /// <para>The plane itself is exact (see the class doc): the room root's transform Y is the floor.
    /// What is NOT exact is the floor ART at the table's corners, because the bake gives both rooms a
    /// gentle relief outside their dead-flat play disc. Read off <c>BuildEnvironmentRooms</c>:
    /// <c>ForestY</c> is identically zero inside r = 1.7 authored m and ramps in over 1.7..4.6 m;
    /// the table's corner radius is 2.31 authored m (the corner is 1.39 perceived m out and the swamp
    /// room stands at 118.87 world units per authored metre against a 198.12 rig scale), where the
    /// ramp is only 0.114 of full strength, giving a relief of about +36/−31 mm authored = about
    /// ±19 mm perceived. <c>CellarFloorY</c> is <c>0.012·Fbm − 0.006</c>, i.e. ±6 mm authored =
    /// about ±5 mm perceived. 25 mm therefore swallows the worst bump in either room.</para>
    ///
    /// <para>THE ASYMMETRY IS THE POINT, and it is the opposite of the bench's: a foot sunk 25 mm
    /// into the ground is a table standing on soft floor, while a foot floating 19 mm above it is a
    /// table hovering. So the error is spent downward, and it is spent in millimetres rather than in
    /// the bench's 20 cm, because these legs are meant to be looked at.</para>
    /// </summary>
    internal const float FootSinkMeters = 0.025f;

    // ---- THE UNDERSIDE, IN REAL METRES -------------------------------------------------------
    // The user, verbatim (translated): "The table has no real underside — you can see through it
    // from below, and you also see the map lying on the table. I want it to have a tabletop from
    // below as well, so you cannot see through." The game's GH_Map_TableTop_Lg draws a top face and
    // four side faces and nothing at the bottom, so from underneath the board is an open shell and
    // the parchment — which this mod draws UNLIT, i.e. at full brightness from either side — shows
    // straight through it. See .planning/debug/tisch_unten.jpg.
    //
    // THE PANEL IS A CLOSED BOX INSIDE THE SLAB'S OWN AABB, and every one of the three numbers below
    // exists to keep it there. It is not welded to the parchment and it never touches it: ModBuild
    // 199 was burnt treating the decal as the table, and the parchment is excluded from this class
    // by identity in TryFindTable and by the 30 mm board test, both of which still run.

    /// <summary>
    /// How far ABOVE the tabletop slab's own <c>bounds.min.y</c> the underside panel's bottom face
    /// sits, real metres.
    ///
    /// <para>NOT below it, which is the whole point. <c>bounds.min.y</c> is the LOWEST POINT ANYWHERE
    /// in the slab's mesh, so a panel placed at or above it can never stand proud of the board and
    /// can never make the table look thicker than the one the user has already accepted. 1 mm
    /// (0.2 world units at this rig scale) is also enough to keep it off the plane of any bottom face
    /// the slab may carry but not draw — a back-facing polygon is culled rather than z-fought, but a
    /// coincident plane is a coin toss this class does not need to enter.</para>
    /// </summary>
    private const float UndersideClearanceMeters = 0.001f;

    /// <summary>
    /// The underside panel's own board thickness, real metres — it is a BOX and not a bare quad, and
    /// that is what closes the rim.
    ///
    /// <para>A single down-facing quad inset from the slab's edge leaves an open slot all the way
    /// round: a ray coming up through that slot enters the hollow board, meets the top face from
    /// BEHIND (culled) and lands on the unlit parchment — i.e. exactly the bright leak being fixed,
    /// reduced to a hairline. The four side walls of a box close it for every ray that does not
    /// already start inside the wood. 12 mm is a plausible board and stays two orders of magnitude
    /// inside the slab's measured 148 mm, so the box lives entirely within the slab's AABB.</para>
    /// </summary>
    private const float UndersideThicknessMeters = 0.012f;

    /// <summary>
    /// How far the underside panel's rim stands INSIDE the slab's side faces, real metres.
    ///
    /// <para>Two conflicting requirements meet here and 2 mm is where they cross. Flush (0 mm) would
    /// put the panel's four side walls exactly on the slab's own side faces — coplanar, same-facing,
    /// z-fighting along the whole rim. Deeply inset would open the slot the box exists to close and
    /// would also read as a shrunken underside. 2 mm is invisible on a 1.549 m table, cannot
    /// z-fight, and cannot poke out unless the slab's side face is recessed from its own AABB by more
    /// than 2 mm — which the photograph rules out: the side face runs flush and vertical from the top
    /// chamfer to the bottom moulding (.planning/debug/tisch_falsches_licht.jpg, near corner).</para>
    ///
    /// <para>It is deliberately SMALLER than <see cref="EdgeInsetMeters"/> (20 mm), so the panel's
    /// walls are never coplanar with a leg's outer face either. The legs stand 18 mm proud of it.</para>
    /// </summary>
    private const float UndersideEdgeInsetMeters = 0.002f;

    /// <summary>Bounds on the derived leg height, real metres. Shorter than this and the table is
    /// sitting on the floor; taller and something measured the wrong plane. Either way the numbers
    /// are wrong, and a refusal with a log line is worth more than geometry stretching to the
    /// horizon.</summary>
    internal const float MinLegHeightMeters = 0.15f;

    /// <inheritdoc cref="MinLegHeightMeters"/>
    internal const float MaxLegHeightMeters = 1.60f;

    /// <summary>Fallback real metres per UV unit, used only when the tabletop's own texel scale
    /// cannot be measured (unreadable mesh, no UV0). 1.0 m per UV unit is an ordinary wood tiling.
    /// </summary>
    internal const float FallbackMetresPerUv = 1.0f;

    /// <summary>Bounds on the MEASURED metres-per-UV-unit before it is trusted. A tabletop whose UVs
    /// imply a 5 cm or a 20 m texture repeat has a mapping this class cannot reason about; clamping
    /// keeps a strange asset as slightly wrong grain rather than as a single stretched texel.</summary>
    private const float MinMetresPerUv = 0.05f;

    /// <inheritdoc cref="MinMetresPerUv"/>
    private const float MaxMetresPerUv = 20f;

    /// <summary>
    /// How much of the 0..1 sheet the tabletop's own UVs must span, per axis, before the texture is
    /// judged to be the table's OWN and therefore safe to repeat. Above this the legs TILE it at the
    /// top's texel density; below it the texture is one page of an atlas and the legs FIT inside that
    /// page instead. See <see cref="AdoptTableUvs"/> for why those are the only two honest options.
    /// </summary>
    private const float FullSheetThreshold = 0.90f;

    /// <summary>Fraction trimmed off each side of an ATLAS PAGE's measured UV rectangle before the
    /// legs are fitted into it — a bilinear-bleed margin only, not a search window. Small, because in
    /// FIT mode nothing can leave the rectangle anyway; it only keeps the filter from reaching a
    /// texel of the neighbouring page.</summary>
    private const float AtlasPageInset = 0.02f;

    /// <summary>How many texture properties per material the material dump names before it stops.
    /// The dump exists to answer "which texture is actually on the legs", and a game shader can carry
    /// a dozen unused slots.</summary>
    private const int TexturePropCap = 12;

    /// <summary>How many scene lights the light census names before it stops.</summary>
    private const int LightCap = 8;

    /// <summary>A candidate tabletop must be a SLAB: its thickness at most this fraction of its
    /// smaller horizontal extent. This is what rejects the map's fog volume, which is as tall as it
    /// is wide.</summary>
    private const float SlabThicknessFactor = 0.30f;

    /// <summary>A candidate tabletop's TOP face must lie within this many real metres of the
    /// parchment's own top plane — the map lies ON the table, so the two surfaces are flush to
    /// within the map's own thickness. (Measured in the captured session: 6 mm.)</summary>
    private const float TopBandMeters = 0.12f;

    /// <summary>How much larger than the parchment a candidate tabletop may be before it is judged
    /// to be a ground plane rather than a table.</summary>
    private const float MaxTableFactor = 4f;

    /// <summary>Slack allowed when testing that a candidate CONTAINS the parchment, real metres.</summary>
    private const float ContainMarginMeters = 0.02f;

    /// <summary>Frames between attempts while the legs are wanted but something they need (the
    /// table, the room) is not there yet. The scan is a scene sweep, so it is throttled; once the
    /// legs stand, nothing scans again.</summary>
    private const int RetryIntervalFrames = 12;

    /// <summary>How many rejected candidates the survey names before it stops.</summary>
    private const int CandidateCap = 6;

    /// <summary>One leg at each corner of the tabletop.</summary>
    internal const int LegCount = 4;

    /// <summary>Triangles in the UNDERSIDE panel: one closed box, 6 quads x 2.</summary>
    internal const int UndersideTriangleCount = 12;

    /// <summary>Triangles in the finished prop: 4 legs x 6 quads x 2, plus the underside box.</summary>
    internal const int TriangleCount = LegCount * 12 + UndersideTriangleCount;

    /// <summary>
    /// The two BUNDLED 3D environments — the only styles that put a room with a floor around the
    /// player, and (per the user's ruling) the only ones the legs appear in. <c>Default</c> keeps the
    /// game's own sky and has no floor at all; <c>OffBlack</c> is deliberately "no environment"; and
    /// mixed reality is handled by its own clause in <see cref="Tick"/> because it OVERRIDES the
    /// style dial (<c>MixedReality.Tick</c>: "MR ON ⇒ the sky is ALWAYS off ... whatever [Sky] Style
    /// says").
    /// </summary>
    internal static bool StyleShowsLegs(SkyStyle style) =>
        style == SkyStyle.Cellar || style == SkyStyle.SwampNight;

    // ---- state -------------------------------------------------------------------------------

    private GameObject? _root;
    private Mesh? _mesh;
    private Material? _material;
    private bool _ownsMaterial;
    private MeshRenderer? _builtAgainstParchment;
    private MeshRenderer? _builtAgainstTable;
    private readonly List<Vector3> _verts = new(128);
    private readonly List<Vector3> _norms = new(128);
    private readonly List<Vector2> _uvs = new(128);
    private readonly List<int> _tris = new(192);

    // ---- ONE ROW PER LEG. Every one of these is measured UNDER ITS OWN CORNER; the legs no longer
    // share a length. Allocated once with the class, written on the build frame only. ------------
    private readonly float[] _legX = new float[LegCount];
    private readonly float[] _legZ = new float[LegCount];
    /// <summary>The floor Y under this leg — the probed ground if <see cref="_legFloorMeasured"/>,
    /// otherwise the room's floor plane.</summary>
    private readonly float[] _legFloorY = new float[LegCount];
    /// <summary>The slab underside Y above this leg — the probed face if
    /// <see cref="_legHeadMeasured"/>, otherwise the slab AABB's floor, which is a hard LOWER BOUND on
    /// the underside anywhere and therefore the worst case the residual is proved against.</summary>
    private readonly float[] _legUndersideY = new float[LegCount];
    private readonly float[] _legHeadY = new float[LegCount];
    private readonly float[] _legFootY = new float[LegCount];
    private readonly float[] _legHeight = new float[LegCount];
    /// <summary>Millimetres of open air between this leg's head and the underside reference above it.
    /// Must read 0.0 on every leg.</summary>
    private readonly float[] _legGapMm = new float[LegCount];
    private readonly bool[] _legHeadMeasured = new bool[LegCount];
    private readonly bool[] _legFloorMeasured = new bool[LegCount];

    private Rect _uvRect = new(0f, 0f, 1f, 1f);
    private Vector2 _uvCentre = new(0.5f, 0.5f);
    private float _uvWorldPerU = 1f;
    private float _uvWorldPerV = 1f;
    /// <summary>TILE mode (the texture is the table's own and repeats) rather than FIT mode (it is
    /// one page of an atlas and the legs are scaled to sit inside it). See <see cref="AdoptTableUvs"/>.
    /// </summary>
    private bool _uvTile = true;
    private int _retryFrame = int.MinValue;
    private SkyStyle _lastStyle = (SkyStyle)(-1);
    private bool _lastMixedReality;
    private bool _lastStanding;
    private bool _gateLogged;
    private string _lastRefusal = "";

    /// <summary>
    /// Per-frame upkeep while the map room stands. THE GATE IS EVALUATED HERE, EVERY FRAME — two
    /// field reads — so a style change in the VR options panel builds or tears the legs down on the
    /// next frame rather than on the next room entry. When the answer has not changed and the prop
    /// already stands, this is a reference compare and nothing else.
    /// </summary>
    internal void Tick()
    {
        SkyStyle style = SkyAlternative.Style != null ? SkyAlternative.Style.Value : SkyStyle.Default;
        bool mixedReality = MixedReality.BackingsWanted;
        bool wanted = !mixedReality && StyleShowsLegs(style);

        if (style != _lastStyle || mixedReality != _lastMixedReality)
        {
            _lastStyle = style;
            _lastMixedReality = mixedReality;
            _gateLogged = false;
            _lastRefusal = "";
            _retryFrame = int.MinValue;
        }

        if (!wanted)
        {
            if (_root != null)
            {
                Release($"the style gate closed — {DescribeGate(style, mixedReality, false)}");
                // Release's own line already carries the gate verdict; the tail below would only
                // repeat it.
                _gateLogged = true;
                _lastStanding = false;
            }
        }
        else if (_root != null)
        {
            // THE ONE THING THAT CAN INVALIDATE A STANDING PROP: a world<->city switch re-finds the
            // parchment, and a different map could stand on a different table. Cheap reference
            // compares (Unity's == also catches a destroyed renderer), no allocation, no scan.
            if (MapRoomDriver.ParchmentRenderer != _builtAgainstParchment || _builtAgainstTable == null)
            {
                Release("the map's parchment or its table changed underneath the prop "
                        + "(world<->city switch) — it rebuilds against the new one");
                _gateLogged = true;
                _lastStanding = false;
            }
        }
        else if (_retryFrame == int.MinValue || Time.frameCount - _retryFrame >= RetryIntervalFrames)
        {
            _retryFrame = Time.frameCount;
            // GUARDED, because this is the one path that touches the GAME's objects — it sweeps the
            // scene for the tabletop and reads its mesh. MapRoomDriver.TickActive runs inside
            // VRRigDriver's per-frame body, and an exception escaping into that starves the player's
            // input for as long as it keeps throwing (WorldUI's oldest recorded defect). A throw here
            // must cost the legs and nothing else, and must not leave half a prop standing.
            try
            {
                Build(style, mixedReality);
            }
            catch (System.Exception ex)
            {
                Release($"the build threw ({ex.GetType().Name})");
                Refuse($"NOT BUILT: the build threw — {ex.GetType().Name}: {ex.Message}. Nothing else "
                       + "in the map room is affected; the legs simply do not exist this session "
                       + "unless the cause clears. THE STACK IS IN Player.log (ModBuild 136+ restores "
                       + "stack traces process-wide), so read it rather than theorising.");
            }
        }

        bool standing = _root != null;
        if (_gateLogged && standing == _lastStanding)
            return;
        _gateLogged = true;
        _lastStanding = standing;
        if (!standing)
            VRLog.Info(Scope, $"MAP TABLE LEGS: {DescribeGate(style, mixedReality, false)}"
                              + (_lastRefusal.Length > 0 ? $" {_lastRefusal}" : ""));
    }

    /// <summary>Tear the legs down. Idempotent; the only exit.</summary>
    internal void Release(string reason)
    {
        bool had = _root != null;
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        if (_mesh != null)
            Object.Destroy(_mesh);
        _mesh = null;
        // ONLY a material this class MADE is destroyed. In the normal case _material IS the game
        // tabletop's own sharedMaterial — destroying that would take the table's surface with it,
        // and every other renderer sharing it. This flag is the whole guard.
        if (_ownsMaterial && _material != null)
            Object.Destroy(_material);
        _material = null;
        _ownsMaterial = false;
        _builtAgainstParchment = null;
        _builtAgainstTable = null;
        _retryFrame = int.MinValue;
        if (had)
        {
            VRLog.Info(Scope, $"MAP TABLE LEGS released ({reason}) — the prop and its mesh are "
                              + "destroyed. The game's tabletop, its material and the environment "
                              + "room are untouched: this class only ever READ them.");
        }
    }

    /// <summary>
    /// Record why a build attempt declined, and arm ONE log line for it — but only when the reason
    /// has actually changed. The build retries on <see cref="RetryIntervalFrames"/> while the legs
    /// are wanted and something they need has not arrived, so an unconditional line here would put
    /// six identical paragraphs a second into the log for as long as the condition lasts. The text
    /// is composed from measured values, so "the same reason" really does mean the same reason.
    /// </summary>
    private void Refuse(string reason)
    {
        if (_lastRefusal == reason)
            return;
        _lastRefusal = reason;
        _gateLogged = false;
    }

    /// <summary>Why the legs are or are not wanted, in one clause. Composed only when the answer
    /// changes, never per frame.</summary>
    private static string DescribeGate(SkyStyle style, bool mixedReality, bool standing)
    {
        if (mixedReality)
            return "MIXED REALITY is on, so NO legs — a passthrough world already has the player's "
                   + "real floor in it and a leg drawn to a virtual floor plane would visibly miss "
                   + $"it. ([Sky] Style is {style}, and MR overrides it either way.)";
        if (!StyleShowsLegs(style))
            return $"[Sky] Style is {style}, so NO legs — the user's ruling is the two BUNDLED 3D "
                   + "environments only (Cellar, SwampNight). Default keeps the game's own sky and "
                   + "OffBlack is deliberately no environment; neither has a floor to stand on.";
        return $"[Sky] Style is {style} — one of the two bundled 3D rooms, so the legs are WANTED"
               + (standing ? " and they stand." : ", but they are not standing yet.");
    }

    // ---- build -------------------------------------------------------------------------------

    private void Build(SkyStyle style, bool mixedReality)
    {
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return;
        if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _))
            return;
        float scale = Mathf.Max(seat.Scale, 0.0001f);
        Bounds parch = parchment.bounds;
        if (Mathf.Max(Mathf.Abs(parch.size.x), Mathf.Abs(parch.size.z)) < MapRoomSeat.MinUsableExtent)
            return;

        if (!TryFindTable(parchment, parch, scale, out MeshRenderer? table, out string candidates)
            || table == null)
        {
            Refuse("NOT BUILT: no tabletop was found. " + candidates);
            return;
        }
        if (!TryFindRoomFloor(style, out float floorY, out Transform? roomRoot, out string floorSource))
        {
            Refuse("NOT BUILT: " + floorSource);
            return;
        }

        Bounds top = table.bounds;
        float slabThickness = Mathf.Abs(top.size.y);
        // The weld is only used where an underside was actually MEASURED under the corner; see below.
        float weld = Mathf.Min(WeldMeters * scale, slabThickness * WeldMaxThicknessFraction);
        // THE HEAD'S REFERENCE FACE IS THE SLAB'S **TOP**, AND THAT IS ModBuild 200'S WHOLE FIX.
        // ModBuild 199 welded 5 mm up from top.min.y and its arithmetic closed — but top.min.y is the
        // lowest point ANYWHERE in an unreadable mesh, not the underside over a CORNER, so any apron,
        // moulding, bevel or tilt left the head short by that difference. The user photographed the
        // near leg's lit top CAP, which cannot be seen at all if the head is inside the board.
        // See HeadInsetBelowTopFaceMeters.
        float headInset = Mathf.Min(HeadInsetBelowTopFaceMeters * scale,
                                    slabThickness * HeadInsetMaxThicknessFraction);
        float anchoredHeadY = top.max.y - headInset;

        float side = LegSideMeters * scale;
        float inset = (LegSideMeters * 0.5f + EdgeInsetMeters) * scale;
        // Guard a table so small the insets cross: the legs then sit on the centre line rather than
        // outside the top, which is ugly but bounded.
        float cornerX = Mathf.Max(Mathf.Abs(top.size.x) * 0.5f - inset, side * 0.5f);
        float cornerZ = Mathf.Max(Mathf.Abs(top.size.z) * 0.5f - inset, side * 0.5f);

        // ---- ONE MEASUREMENT PASS PER LEG, BEFORE ANY GameObject EXISTS ------------------------
        // Each leg gets its OWN floor, its OWN head and therefore its OWN length. Nothing is created
        // until all four have passed the plausibility window, so a refusal cannot leave half a prop
        // standing. Two probes per leg, on the build frame only, and both are read-only queries.
        int headsMeasured = 0, floorsMeasured = 0, gapFree = 0;
        float tallest = 0f, shortest = float.MaxValue;
        for (int i = 0; i < LegCount; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            _legX[i] = top.center.x + cornerX * sx;
            _legZ[i] = top.center.z + cornerZ * sz;

            // THE FLOOR UNDER **THIS** CORNER. The plane is exact but the floor ART is not (both
            // rooms have a gentle relief outside their play disc), so the ground itself is probed
            // first and the plane is the fallback.
            _legFloorMeasured[i] = TryMeasureFloor(roomRoot, _legX[i], _legZ[i], floorY, scale,
                                                   out float groundY);
            _legFloorY[i] = _legFloorMeasured[i] ? groundY : floorY;
            _legFootY[i] = _legFloorY[i] - FootSinkMeters * scale;
            if (_legFloorMeasured[i])
                floorsMeasured++;

            // THE SLAB'S UNDERSIDE OVER **THIS** CORNER. Probed if the table carries a collider;
            // otherwise the head is anchored to the TOP face, which needs no knowledge of the
            // underside at all, and the residual is proved against top.min.y — a hard lower bound on
            // where the underside can possibly be.
            _legHeadMeasured[i] = TryMeasureUnderside(table, _legX[i], _legZ[i], top, scale,
                                                      out float undersideY);
            if (_legHeadMeasured[i])
            {
                headsMeasured++;
                _legUndersideY[i] = undersideY;
                // Up into the wood by the weld, but never nearer the top face than the weld itself:
                // "cannot emerge from the top" survives a measurement that lands anywhere.
                _legHeadY[i] = Mathf.Min(undersideY + weld, top.max.y - weld);
            }
            else
            {
                _legUndersideY[i] = top.min.y;
                _legHeadY[i] = anchoredHeadY;
            }

            _legHeight[i] = _legHeadY[i] - _legFootY[i];
            // THE RESIDUAL, IN MILLIMETRES: open air between the head and the underside reference
            // above it. Negative means the head is INSIDE the wood, which is the intent, so it is
            // reported as zero gap; a positive number is the ModBuild 199 defect coming back.
            _legGapMm[i] = Mathf.Max(0f, _legUndersideY[i] - _legHeadY[i]) / scale * 1000f;
            if (_legGapMm[i] <= GapFreeMillimetres)
                gapFree++;
            tallest = Mathf.Max(tallest, _legHeight[i]);
            shortest = Mathf.Min(shortest, _legHeight[i]);
        }

        for (int i = 0; i < LegCount; i++)
        {
            if (_legHeight[i] >= MinLegHeightMeters * scale && _legHeight[i] <= MaxLegHeightMeters * scale)
                continue;
            Refuse($"NOT BUILT: leg {i} at ({_legX[i]:F2}, {_legZ[i]:F2}) derives a height of "
                   + $"{_legHeight[i] / scale:F3} m ({_legHeight[i]:F1} world units), outside the "
                   + $"plausible {MinLegHeightMeters:F2}..{MaxLegHeightMeters:F2} m window — its head "
                   + $"is y={_legHeadY[i]:F2} (the slab spans y={top.min.y:F2}..{top.max.y:F2}) and its "
                   + $"foot is y={_legFootY[i]:F2} (floor y={_legFloorY[i]:F2}, {floorSource}). One of "
                   + "those two planes is not what this class thinks it is; NOTHING is built rather "
                   + "than a wrong prop, and no GameObject has been created at this point.");
            return;
        }

        // THE FRAME. Origin at the TABLETOP's horizontal centre and at the ROOM FLOOR PLANE
        // vertically, unrotated. The table's own AABB is the frame the legs belong to — they are its
        // legs — and anchoring the vertical to the floor plane is what makes "standing on the floor"
        // structural rather than arithmetic that can drift. Each leg's own foot and head are then
        // offsets from that one plane, so the four rows in the log and the four boxes in the mesh are
        // the same numbers.
        var origin = new Vector3(top.center.x, floorY, top.center.z);
        _root = new GameObject(RootName);
        _root.transform.SetPositionAndRotation(origin, Quaternion.identity);

        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _tris.Clear();

        // Pick the SKIN before the UVs, because the UV decision depends on the texture that skin
        // carries (its wrap mode decides whether the grain may repeat at all).
        Material? skin = PickTableMaterial(table, out int skinIndex, out string materialSource);
        AdoptTableUvs(table, skin, top, scale, tallest, side, out string uvSource);

        for (int i = 0; i < LegCount; i++)
        {
            var centre = new Vector3(_legX[i] - top.center.x,
                                     (_legFootY[i] + _legHeadY[i]) * 0.5f - floorY,
                                     _legZ[i] - top.center.z);
            Box(centre, new Vector3(side, _legHeight[i], side));
        }

        // THE UNDERSIDE, in the SAME mesh, the SAME material and the SAME draw call as the legs.
        Underside(top, floorY, scale, anchoredHeadY, side, out string undersideSource);

        _mesh = new Mesh { name = "GloomhavenVR.MapTableLegs" };
        _mesh.SetVertices(_verts);
        _mesh.SetNormals(_norms);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_tris, 0, calculateBounds: true);
        // The tabletop's material is the game's own and may well sample a normal map through a
        // tangent frame; a mesh without tangents lights by an undefined basis. Recalculated once,
        // here, never per frame.
        _mesh.RecalculateTangents();

        var geo = new GameObject("Geo");
        geo.transform.SetParent(_root.transform, worldPositionStays: false);
        geo.AddComponent<MeshFilter>().sharedMesh = _mesh;
        var mr = geo.AddComponent<MeshRenderer>();
        _material = skin;
        _ownsMaterial = false;
        if (_material == null)
        {
            _material = WoodFallbackMaterial();
            _ownsMaterial = true;
        }
        mr.sharedMaterial = _material;
        // HOW THIS PROP IS **LIT** IS COPIED FROM THE TABLETOP; HOW IT **LIGHTS OTHERS** IS NOT.
        // See MirrorTableShading for the split and why it is drawn there.
        string shadingSource = MirrorTableShading(table, mr);
        // NO COLLIDER, deliberately: the laser's pick path must not start finding furniture.

        // THE LEGS GO ON THE TABLE'S OWN LAYER, and that RESOLVES ModBuild 198's stated open risk
        // rather than measuring it again. See ChooseLayer.
        int layer = ChooseLayer(table, out string layerSource);
        SetLayerRecursive(_root.transform, layer);

        _builtAgainstParchment = parchment;
        _builtAgainstTable = table;
        _lastRefusal = "";
        Report(style, mixedReality, seat, parch, table, top, floorY, side, cornerX, cornerZ, scale,
               weld, headInset, anchoredHeadY, slabThickness, tallest, shortest, headsMeasured,
               floorsMeasured, gapFree, skinIndex, layer, floorSource, materialSource, uvSource,
               layerSource, candidates, undersideSource, shadingSource);
    }

    // ---- the two per-leg probes ------------------------------------------------------------------

    /// <summary>
    /// THE SLAB'S UNDERSIDE DIRECTLY OVER ONE CORNER, by an upward ray against the TABLE'S OWN
    /// collider — the measurement ModBuild 199 did not have and whose absence is the gap the user
    /// photographed.
    ///
    /// <para>WHY A RAY AND NOT THE MESH. <c>GH_Map_TableTop_Lg</c>'s mesh is NOT CPU-readable — this
    /// class's own texture line says so in the captured session ("the tabletop's mesh is not
    /// CPU-readable"), which is also why the legs fall back to the whole 0..1 sheet — so its triangles
    /// cannot be walked. A collider CAN be queried whether or not the mesh is readable, so this is the
    /// only honest way to ask "how high is the wood, HERE".</para>
    ///
    /// <para>ONLY THE TABLE'S OWN COLLIDERS COUNT. The hit's transform must be the tabletop's or a
    /// descendant of it; an ancestor is deliberately NOT accepted, because a map-root picking volume
    /// is an ancestor and would answer with its own box. The LOWEST qualifying hit wins — going up
    /// from under the slab, the first face met is its underside — and the hit must lie inside the
    /// slab's own AABB, so a collider that does not model this board cannot move the head.</para>
    ///
    /// <para>If the table has no collider the probe simply says no, the head is anchored to the TOP
    /// face instead (see <see cref="HeadInsetBelowTopFaceMeters"/>), and the log prints
    /// "0 of 4 measured" so a reader can never mistake "no gap" for "never looked". Read-only: a
    /// raycast queries the physics scene and writes nothing to it. One cast per leg, on the build
    /// frame only.</para>
    /// </summary>
    private static bool TryMeasureUnderside(MeshRenderer table, float x, float z, Bounds top,
                                            float scale, out float undersideY)
    {
        undersideY = 0f;
        try
        {
            Transform tableTf = table.transform;
            float margin = UndersideProbeMarginMeters * scale;
            var from = new Vector3(x, top.min.y - margin, z);
            float distance = Mathf.Abs(top.size.y) + 2f * margin;
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.up, distance, ~0,
                                                   QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = 0f;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null)
                    continue;
                Transform t = c.transform;
                if (t != tableTf && !t.IsChildOf(tableTf))
                    continue;
                float y = hits[i].point.y;
                if (y < top.min.y - margin * 0.5f || y > top.max.y)
                    continue;
                if (!found || y < best)
                {
                    best = y;
                    found = true;
                }
            }
            if (!found)
                return false;
            undersideY = best;
            return true;
        }
        catch
        {
            // A physics query cannot normally throw, but this whole path runs inside the map room's
            // per-frame body and an escape there starves the player's input. The legs lose one
            // measurement; nothing else notices.
            return false;
        }
    }

    /// <summary>
    /// THE GROUND UNDER ONE CORNER, by a downward ray against the ENVIRONMENT ROOM'S own colliders.
    ///
    /// <para>The room root's Y is the floor PLANE and is exact (see <see cref="TryFindRoomFloor"/>),
    /// but the floor ART is not flat: both rooms carry a gentle relief outside their dead-flat play
    /// disc — about +/-19 mm perceived at this table's corner radius in SwampNight and about +/-5 mm
    /// in the Cellar. <see cref="FootSinkMeters"/> exists to swallow exactly that, downward. This
    /// probe removes the need to swallow it at all WHEN the room has a collider: the foot is then cut
    /// under the ground that is actually there, per corner, and the sink is spent on soft ground
    /// rather than on an unknown.</para>
    ///
    /// <para>Only colliders under the room root count, and only within
    /// <see cref="FloorProbeUpMeters"/>/<see cref="FloorProbeDownMeters"/> of the plane, so the
    /// tabletop above and anything under the floor cannot answer. The HIGHEST qualifying hit wins —
    /// coming down, that is the surface the leg would rest on. Falls back to the plane, and the log
    /// says which of the four legs used which.</para>
    /// </summary>
    private static bool TryMeasureFloor(Transform? room, float x, float z, float planeY, float scale,
                                        out float groundY)
    {
        groundY = planeY;
        if (room == null)
            return false;
        try
        {
            float up = FloorProbeUpMeters * scale, down = FloorProbeDownMeters * scale;
            var from = new Vector3(x, planeY + up, z);
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, up + down, ~0,
                                                   QueryTriggerInteraction.Ignore);
            bool found = false;
            float best = 0f;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null)
                    continue;
                Transform t = c.transform;
                if (t != room && !t.IsChildOf(room))
                    continue;
                float y = hits[i].point.y;
                if (y > planeY + up || y < planeY - down)
                    continue;
                if (!found || y > best)
                {
                    best = y;
                    found = true;
                }
            }
            if (!found)
                return false;
            groundY = best;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// The fallback skin, used ONLY when the tabletop renderer has no material at all — the same
    /// wood the table's button caps are made of. <c>GloomhavenVR/BoardLit</c> is baked-lit (it has an
    /// ambient floor and its own studio key), which is why it is the fallback: the map room has no
    /// guaranteed lighting and an ordinary lit shader renders black in it.
    /// </summary>
    private static Material WoodFallbackMaterial()
    {
        const float woodBoost = 1.9f;
        Shader? lit = Cards.PlayTray.BoardLitShader();
        return lit != null
            ? Cards.PlayTray.NewKeycapMaterial(lit, ButtonTuning.CapWellColor * woodBoost)
            : WorldUIAssets.CreateFlatMaterial(ButtonTuning.CapWellColor * woodBoost);
    }

    // ---- the skin ------------------------------------------------------------------------------

    /// <summary>
    /// WHICH OF THE TABLETOP'S MATERIALS THE LEGS WEAR — the first one that actually carries a main
    /// texture, and its index is logged.
    ///
    /// <para>ModBuild 198 took <c>sharedMaterial</c>, i.e. submesh 0, unconditionally. On the renderer
    /// it wrongly picked that was <c>GloomhavenVR.MapRoom.MapUnlit.0</c> — one of the FOUR unlit
    /// materials this mod itself puts on the parchment — so the legs were painted with the campaign
    /// map. Submesh 0 of a multi-material prop is not reliably its surface material either (it is
    /// often a trim or an edge), so the choice is made by a MEASUREMENT: a material that draws no
    /// texture cannot carry the wood, and the first one that does is taken.</para>
    ///
    /// <para>The result is the game's OWN material object, shared not copied, so the legs cannot drift
    /// from the table's look and cost no extra material. <see cref="Release"/>'s
    /// <c>_ownsMaterial</c> flag is what keeps this class from ever destroying it.</para>
    /// </summary>
    private static Material? PickTableMaterial(MeshRenderer table, out int index, out string source)
    {
        index = -1;
        Material[] mats;
        try { mats = table.sharedMaterials; }
        catch (System.Exception ex)
        {
            source = $"reading the tabletop's materials threw ({ex.GetType().Name}: {ex.Message}), so "
                     + "the legs wear the FALLBACK wood and will NOT match the table";
            return null;
        }

        Material? textured = null, anyMaterial = null;
        int texturedIndex = -1, anyIndex = -1;
        for (int i = 0; i < mats.Length; i++)
        {
            Material? m = mats[i];
            if (m == null)
                continue;
            if (anyMaterial == null) { anyMaterial = m; anyIndex = i; }
            if (textured != null)
                continue;
            Texture? t = null;
            try { t = m.mainTexture; }
            catch { /* a shader with no _MainTex throws nothing, but a broken one might */ }
            if (t != null) { textured = m; texturedIndex = i; }
        }

        Material? pick = textured ?? anyMaterial;
        index = textured != null ? texturedIndex : anyIndex;
        if (pick == null)
        {
            source = $"the tabletop renderer carries {mats.Length} material slot(s) and every one of "
                     + "them is NULL, which should be impossible for a visible slab — the legs wear "
                     + "the FALLBACK wood (GloomhavenVR/BoardLit over the button-rail cap colour) and "
                     + "will NOT match the table. Read this line before tuning anything else.";
            return null;
        }
        source = $"the tabletop's OWN material object mat[{index}] '{pick.name}' "
                 + $"(shader '{(pick.shader != null ? pick.shader.name : "<null>")}'), shared and not "
                 + $"copied, chosen out of {mats.Length} slot(s) because it is the FIRST that actually "
                 + (textured != null
                    ? "draws a main texture — a slot with no texture cannot be carrying the wood"
                    : "exists; NONE of the slots draws a main texture, so the legs will take whatever "
                      + "flat colour this material is and the grain will be missing")
                 + ". ModBuild 198 took slot 0 of the WRONG RENDERER and got "
                 + "'GloomhavenVR.MapRoom.MapUnlit.0' — this mod's own map material — which is why the "
                 + "legs came out pale";
        return pick;
    }

    /// <summary>
    /// FULL DUMP of a renderer's materials, shaders, keywords and every texture slot with the
    /// texture's name, size and wrap mode. This is the instrument that decides "wrong material" from
    /// "wrong UV window" in one reading, and it is printed for the tabletop AND for the finished legs
    /// so the two can be compared without a hardware round.
    /// </summary>
    private static string DescribeMaterials(MeshRenderer? r, string who, string indent)
    {
        if (r == null)
            return $"{indent}{who}: <no renderer>";
        var sb = new StringBuilder(256);
        Material[] mats;
        try { mats = r.sharedMaterials; }
        catch (System.Exception ex)
        {
            return $"{indent}{who} '{r.name}': reading sharedMaterials threw ({ex.GetType().Name}).";
        }
        sb.Append($"{indent}{who} '{r.name}' L{r.gameObject.layer} enabled={r.enabled} "
                  + $"{mats.Length} material slot(s)");
        for (int i = 0; i < mats.Length; i++)
        {
            Material? m = mats[i];
            if (m == null)
            {
                sb.Append($"\n{indent}  mat[{i}] <null>");
                continue;
            }
            Shader? sh = m.shader;
            string keywords;
            try { keywords = string.Join(" ", m.shaderKeywords); }
            catch { keywords = "<unreadable>"; }
            // THE PASS NAMES MATTER HERE. The head camera renders FORWARD while the game's own map
            // camera is DeferredShading, and this room has already lost a renderer to that difference
            // once (MapParchment exists because the parchment has no forward pass). The table is
            // visible in the user's photograph, so its material demonstrably HAS one — this prints
            // the proof rather than relying on the photograph.
            string passes;
            try
            {
                var ps = new StringBuilder(48);
                for (int q = 0; q < m.passCount; q++)
                    ps.Append(q > 0 ? ", " : "").Append(m.GetPassName(q));
                passes = ps.ToString();
            }
            catch { passes = "<unreadable>"; }
            sb.Append($"\n{indent}  mat[{i}] '{m.name}' shader '{(sh != null ? sh.name : "<null>")}' "
                      + $"queue {m.renderQueue} passes [{passes}] mainScale ({m.mainTextureScale.x:F3},"
                      + $"{m.mainTextureScale.y:F3}) mainOffset ({m.mainTextureOffset.x:F3},"
                      + $"{m.mainTextureOffset.y:F3}) keywords [{keywords}]");
            if (sh == null)
                continue;
            int count;
            try { count = sh.GetPropertyCount(); }
            catch { continue; }
            int shown = 0;
            for (int p = 0; p < count && shown < TexturePropCap; p++)
            {
                if (sh.GetPropertyType(p) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                    continue;
                string pn = sh.GetPropertyName(p);
                Texture? t;
                try { t = m.GetTexture(pn); }
                catch { continue; }
                shown++;
                // isReadable IS THE ATLAS QUESTION. The legs are mapped over the whole 0..1 sheet
                // because the slab's mesh is not CPU-readable, so they land on arbitrary atlas pages
                // (the mitred-corner and inset-panel lines on the near leg). Choosing a plain plank
                // sub-rectangle instead would have to be PROVED from the texture, and this flag says
                // whether the texture can be inspected at all at runtime. If it ever reads
                // CPU-readable, one mip-level GetPixels is enough to find the flattest wood page.
                string readable = t is Texture2D t2
                    ? (t2.isReadable ? "CPU-READABLE" : "not CPU-readable")
                    : "not a Texture2D";
                sb.Append($"\n{indent}    {pn} = "
                          + (t == null
                             ? "<null>"
                             : $"'{t.name}' {t.width}x{t.height} {t.GetType().Name} wrap={t.wrapMode} "
                               + $"filter={t.filterMode} mips={t.mipmapCount} {readable}"));
            }
        }
        return sb.ToString();
    }

    // ---- the layer -----------------------------------------------------------------------------

    /// <summary>
    /// THE LEGS GO ON THE TABLE'S OWN LAYER — which closes ModBuild 198's one stated open risk by
    /// CONSTRUCTION instead of leaving it for the next photograph.
    ///
    /// <para>198 put them on the mod layer because every other prop this room builds does, and then
    /// wrote the risk down: "a realtime light whose own culling mask excludes the mod layer would
    /// light the two differently ... legs of the right wood at visibly the wrong BRIGHTNESS". A leg
    /// is not a mod prop that happens to sit near a table — it is PART OF the table, wearing the
    /// table's own material object, and the only way two objects with one material are guaranteed to
    /// shade identically is for them to be on one layer. So the table's layer is taken and the risk
    /// stops existing.</para>
    ///
    /// <para>IT IS ONLY TAKEN IF IT IS VISIBLE, and that is read rather than assumed:
    /// <c>MapRoomDriver.ResolveMapMask</c> is the exact mask the head camera runs with (the game map
    /// camera's own mask OR the mod layer), and the tabletop's layer is in it if and only if the table
    /// itself is drawn — which it demonstrably is, since the user photographed it. If the mask ever
    /// says otherwise the mod layer is used instead and the log says so, so the worst case is 198's
    /// behaviour and never an invisible prop.</para>
    ///
    /// <para>NOTHING ELSE KEYS OFF THE MOD LAYER FOR THIS PROP: it has no collider (so no raycast
    /// path can find it either way), it is not a WorldUI panel, and <see cref="TryFindTable"/> — the
    /// one place that filters by layer — excludes this prop by identity as well, so legs on layer 0
    /// cannot be mistaken for a tabletop on a later rebuild.</para>
    /// </summary>
    private static int ChooseLayer(MeshRenderer table, out string source)
    {
        int mod = VRLayers.ModLayer;
        int tableLayer = table.gameObject.layer;
        int mask = MapRoomDriver.ResolveMapMask(null, out _);
        if (tableLayer == mod)
        {
            source = $"layer {tableLayer} — the tabletop is ALREADY on the mod layer, so there is "
                     + "nothing to reconcile";
            return tableLayer;
        }
        if ((mask & (1 << tableLayer)) == 0)
        {
            source = $"layer {mod} (the MOD layer) — the tabletop is on layer {tableLayer}, but the "
                     + $"head camera's culling mask 0x{mask:X8} does NOT contain that layer, so legs "
                     + "put there would be invisible. Falling back to the mod layer reproduces "
                     + "ModBuild 198's behaviour exactly, INCLUDING its open lighting risk: if the "
                     + "legs then read as the right wood at the wrong brightness, that is this line.";
            return mod;
        }
        source = $"layer {tableLayer} — THE TABLETOP'S OWN, not the mod layer {mod} that every other "
                 + "prop in this room uses. The legs wear the table's own material object, and two "
                 + "objects with one material shade identically only if one light set reaches both; "
                 + "sharing the layer makes that true by construction instead of by hope. The head "
                 + $"camera's mask 0x{mask:X8} was READ (MapRoomDriver.ResolveMapMask) and contains "
                 + "the layer, so the prop is drawn. This RESOLVES the open risk ModBuild 198 wrote "
                 + "down rather than measuring it again";
        return tableLayer;
    }

    /// <summary>
    /// COPY HOW THE TABLETOP IS **LIT**; DO NOT COPY HOW IT **LIGHTS OTHERS**. That split is the
    /// whole rule here, and it is the one lighting change this file can honestly make.
    ///
    /// <para>The prop wears the tabletop's own material object and stands on the tabletop's own
    /// layer (see <see cref="ChooseLayer"/>), so it already sees the same lights. What it did NOT
    /// share until now is the rest of the per-renderer lighting contract: ModBuild 198..200 forced
    /// <c>receiveShadows = false</c> and left <c>lightProbeUsage</c> / <c>reflectionProbeUsage</c> at
    /// the defaults Unity gives a freshly created MeshRenderer. Those are INPUTS — they decide how
    /// much light this surface collects — and if the tabletop's differ, two objects with one material
    /// under one light set still come out at different brightnesses. They are copied.</para>
    ///
    /// <para><c>shadowCastingMode</c> is deliberately NOT copied and stays <c>Off</c>. It is an
    /// OUTPUT: it decides what this prop does to the GAME's other renderers, and this class's whole
    /// standing claim is that it changes nothing outside itself. Leaving it off also keeps the cost
    /// claim true — no second pass over the mesh — and costs nothing visually, because the one light
    /// that reaches this layer does not reach the environment room's floor (its mask is printed in
    /// the census two lines below).</para>
    ///
    /// <para>IT ALSO MEASURES THE ONE THING THAT COULD STILL SPLIT THEM: whether the tabletop is
    /// LIGHTMAPPED. A baked renderer takes its ambient from a lightmap chart that a procedural mesh
    /// with no UV2 cannot share, and copying the index would sample someone else's chart. So the
    /// state is reported rather than copied, and the report says what it would mean.</para>
    /// </summary>
    private static string MirrorTableShading(MeshRenderer table, MeshRenderer mine)
    {
        var before = new System.Text.StringBuilder(64);
        try
        {
            before.Append($"receiveShadows={mine.receiveShadows}, lightProbes={mine.lightProbeUsage}, "
                          + $"reflectionProbes={mine.reflectionProbeUsage}");
            mine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mine.receiveShadows = table.receiveShadows;
            mine.lightProbeUsage = table.lightProbeUsage;
            mine.reflectionProbeUsage = table.reflectionProbeUsage;
            int lm = table.lightmapIndex;
            // Unity's two sentinels for "no lightmap": -1 and 65535. 65534 is the "lightmapped but
            // the map is not loaded" marker, which is still a baked renderer.
            bool lightmapped = lm >= 0 && lm < 65535;
            return $"the prop is LIT by exactly the tabletop's own rules — receiveShadows="
                   + $"{mine.receiveShadows}, lightProbeUsage={mine.lightProbeUsage}, "
                   + $"reflectionProbeUsage={mine.reflectionProbeUsage}, all COPIED from "
                   + $"'{table.name}' (a fresh MeshRenderer would have had {before}). Those three are "
                   + "INPUTS: they decide how much light this surface collects, and two objects with "
                   + "one material under one light set still differ if they differ. shadowCastingMode "
                   + "is deliberately NOT copied and stays Off — it is an OUTPUT, it would change what "
                   + "this prop does to the GAME's renderers, and this class changes nothing outside "
                   + $"itself. THE TABLETOP'S lightmapIndex IS {lm}, i.e. it is "
                   + (lightmapped
                      ? "BAKED. That is a difference this class CANNOT close: a baked renderer takes "
                        + "its ambient from a lightmap chart, and a procedural mesh with no UV2 has no "
                        + "chart of its own — copying the index would sample the TABLE's chart at the "
                        + "legs' coordinates, i.e. arbitrary baked light. If the legs read at a "
                        + "visibly different brightness from the slab they hold up, THIS LINE IS THE "
                        + "REASON and the fix is a bake, not a flag"
                      : "NOT baked, so its shading is entirely realtime and the legs, on its layer "
                        + "with its material and now its lighting flags, are shaded by exactly the "
                        + "same arithmetic. Any remaining difference between the prop and the ROOM is "
                        + "therefore not about this prop — see the census below");
        }
        catch (System.Exception ex)
        {
            return $"copying the tabletop's lighting flags threw ({ex.GetType().Name}: {ex.Message}), "
                   + "so the prop keeps a fresh MeshRenderer's defaults and may collect a different "
                   + "amount of ambient than the slab it holds up";
        }
    }

    /// <summary>Set a whole (two-deep) subtree to one layer. <c>VRLayers.Apply</c> would force the
    /// MOD layer, which is exactly the thing <see cref="ChooseLayer"/> decides against.</summary>
    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i), layer);
    }

    /// <summary>The environment shaders' authored "direction TOWARD the moon", in the carrying
    /// renderer's OBJECT space. Declared by <c>EnvStars.shader</c>, <c>EnvStarPoints.shader</c> and
    /// <c>EnvPuddle.shader</c>, and written once at bake time from
    /// <c>EnvironmentsBuilder.MoonDir</c>. See <see cref="TryMeasureMoonDirection"/>.</summary>
    private static readonly int MoonDirId = Shader.PropertyToID("_MoonDir");

    /// <summary>
    /// THE ROOM'S OWN MOON DIRECTION, MEASURED — not assumed, and not copied into this file.
    ///
    /// <para>The user's complaint about the lighting is a statement about a DIRECTION ("although the
    /// moon shines from the other side, the table legs and the side of the table are lit from the
    /// other side"), and until now this census could only print which LAYERS a light reached. So the
    /// moon is read from the environment itself: <c>SkyAlternative</c>'s sky and room branches are
    /// found by the names that class authors — the same by-name compromise
    /// <see cref="TryFindRoomFloor"/> already documents — and the first shared material under either
    /// that declares <c>_MoonDir</c> is asked for it. That value is the authored constant
    /// <c>EnvironmentsBuilder.MoonDir</c> baked into the bundle, so this reads the SAME number the
    /// moon sprite, the light shafts and the water glints read; it is not a second copy that can
    /// drift, which is the frequency-scrub bug class this project has already paid for.</para>
    ///
    /// <para>It is a direction in the carrying renderer's object space and the sky branch is rotated
    /// to the board's yaw, so it is turned into world space by the renderer's ROTATION. Rotation and
    /// not the full matrix on purpose: a direction pushed through a non-uniformly scaled
    /// <c>localToWorldMatrix</c> comes out skewed, and the shader itself consumes the constant in
    /// object space where only the rotation separates the two frames. The renderer's lossy scale is
    /// printed so a reader can see whether that assumption held.</para>
    ///
    /// <para>READ-ONLY, and that word is load-bearing: <c>sharedMaterials</c>, never
    /// <c>materials</c> — the latter INSTANTIATES a copy per renderer and would leave the
    /// environment wearing clones this class then leaks. One sweep, on the build frame, and it stops
    /// at the first answer.</para>
    /// </summary>
    internal static bool TryMeasureMoonDirection(SkyStyle style, out Vector3 world, out string source)
    {
        world = Vector3.up;
        source = "no environment branch in the scene carries a _MoonDir, so the room's own moon "
                 + "direction could not be measured this build";
        string[] roots =
        {
            "GloomhavenVR.SkyAlternative.Sky." + style,
            "GloomhavenVR.SkyAlternative.Room." + style,
        };
        try
        {
            for (int n = 0; n < roots.Length; n++)
            {
                GameObject? go = GameObject.Find(roots[n]);
                if (go == null)
                    continue;
                Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rs.Length; i++)
                {
                    Renderer r = rs[i];
                    if (r == null)
                        continue;
                    Material[] mats = r.sharedMaterials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        Material? mat = mats[m];
                        if (mat == null || !mat.HasProperty(MoonDirId))
                            continue;
                        Vector4 v = mat.GetVector(MoonDirId);
                        var local = new Vector3(v.x, v.y, v.z);
                        if (local.sqrMagnitude < 1e-6f)
                            continue;
                        world = (r.transform.rotation * local).normalized;
                        Vector3 ls = r.transform.lossyScale;
                        source = $"MEASURED off the environment itself: '{mat.name}' on renderer "
                                 + $"'{r.name}' under '{roots[n]}' declares _MoonDir "
                                 + $"({local.x:F3}, {local.y:F3}, {local.z:F3}) in object space — the "
                                 + "authored EnvironmentsBuilder.MoonDir baked into the bundle, i.e. "
                                 + "the very number the moon sprite, the light shafts and the water "
                                 + $"glints read. Rotated into world space by that renderer it points "
                                 + $"{Bearing(world)}. (Renderer lossy scale ({ls.x:F2}, {ls.y:F2}, "
                                 + $"{ls.z:F2}); the rotation alone is used, so a non-uniform scale "
                                 + "there would be the one thing that could bend this reading.)";
                        return true;
                    }
                }
            }
        }
        catch (System.Exception ex)
        {
            source = $"reading the environment's _MoonDir threw ({ex.GetType().Name}: {ex.Message}), "
                     + "so the room's own moon direction is unknown this build";
        }
        return false;
    }

    /// <summary>A world direction as a compass bearing and an elevation, both in degrees, because
    /// "lit from the other side" is a statement about angles and a vector is not readable as one.
    /// Azimuth is measured from +Z through +X, exactly as Unity's own yaw is.</summary>
    private static string Bearing(Vector3 d)
    {
        Vector3 n = d.sqrMagnitude > 1e-9f ? d.normalized : Vector3.up;
        float az = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
        float alt = Mathf.Asin(Mathf.Clamp(n.y, -1f, 1f)) * Mathf.Rad2Deg;
        return $"az {az:F1} deg, alt {alt:F1} deg ({n.x:F3}, {n.y:F3}, {n.z:F3})";
    }

    /// <summary>
    /// THE LIGHT CENSUS — and since ModBuild 201 it answers WHICH WAY, not only which layer, because
    /// that is the question the user actually asked.
    ///
    /// <para>ModBuild 199 shipped this census to settle one thing: do the legs disagree with the
    /// TABLE? It answers that completely — every enabled light with its culling mask, and a count of
    /// the lights that reach the tabletop's layer and the mod layer DIFFERENTLY. What it could not
    /// answer is the user's actual sentence, which is about a direction: "although the moon shines
    /// from the other side, the table legs AND THE SIDE OF THE TABLE are lit from the other side".
    /// So every light now also prints the direction it comes FROM as a bearing and an elevation, the
    /// room's own moon is measured (<see cref="TryMeasureMoonDirection"/>), and the angle between the
    /// strongest light that reaches the tabletop and that moon is printed as a single number.</para>
    ///
    /// <para>THE WHOLE DISTRIBUTION IS PRINTED, up to <see cref="LightCap"/>, and the tail is counted
    /// rather than dropped silently — a census that shows only the interesting rows is a census that
    /// agrees with whatever you already believe.</para>
    /// </summary>
    private static string DescribeLights(int tableLayer, int modLayer, SkyStyle style, string indent)
    {
        Light[] lights;
        try { lights = Object.FindObjectsOfType<Light>(); }
        catch (System.Exception ex)
        {
            return $"{indent}the light census threw ({ex.GetType().Name}) — no verdict.";
        }

        bool haveMoon = TryMeasureMoonDirection(style, out Vector3 moon, out string moonSource);

        int tableBit = 1 << tableLayer, modBit = 1 << modLayer;
        int discriminating = 0, enabledCount = 0, named = 0, litTableCount = 0;
        float strongest = -1f;
        Vector3 strongestDir = Vector3.up;
        string strongestName = "<none>";
        var sb = new StringBuilder(384);
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy)
                continue;
            enabledCount++;
            bool litsTable = (l.cullingMask & tableBit) != 0;
            bool litsMod = (l.cullingMask & modBit) != 0;
            bool splits = litsTable != litsMod;
            if (splits)
                discriminating++;
            // The direction light ARRIVES FROM, i.e. the direction you look to see the source. For a
            // directional light that is -forward; for a point/spot it is only meaningful at a place,
            // so the bearing is printed for directionals and the position for the rest.
            Vector3 from = -l.transform.forward;
            if (litsTable)
            {
                litTableCount++;
                if (l.type == LightType.Directional && l.intensity > strongest)
                {
                    strongest = l.intensity;
                    strongestDir = from;
                    strongestName = l.name;
                }
            }
            if (named >= LightCap)
                continue;
            named++;
            Color c = l.color;
            sb.Append($"\n{indent}  '{l.name}' {l.type} intensity {l.intensity:F2} colour "
                      + $"({c.r:F2}, {c.g:F2}, {c.b:F2}) shadows {l.shadows} mask 0x{l.cullingMask:X8}"
                      + $" — comes FROM {(l.type == LightType.Directional ? Bearing(from) : $"position ({l.transform.position.x:F1}, {l.transform.position.y:F1}, {l.transform.position.z:F1}), range {l.range:F1}")}"
                      + $" → table layer {tableLayer} {(litsTable ? "LIT" : "not lit")}, mod layer "
                      + $"{modLayer} {(litsMod ? "LIT" : "not lit")}"
                      + (splits ? "  <-- DISCRIMINATES" : ""));
        }

        string verdict;
        if (litTableCount == 0)
        {
            verdict = $"NOTHING lights layer {tableLayer} at all, so the tabletop and this prop are "
                      + "both on ambient alone — if the wood reads flat, that is why.";
        }
        else if (!haveMoon || strongest < 0f)
        {
            verdict = $"{litTableCount} light(s) reach layer {tableLayer}, and the prop is on that "
                      + "layer with the tabletop's own material and lighting flags, so the PROP AND "
                      + "THE TABLE AGREE BY CONSTRUCTION. Whether they agree with the ROOM could not "
                      + "be decided this build: " + moonSource;
        }
        else
        {
            float angle = Vector3.Angle(strongestDir, moon);
            verdict = $"{litTableCount} light(s) reach layer {tableLayer}; the strongest directional "
                      + $"one is '{strongestName}' at intensity {strongest:F2}, arriving from "
                      + $"{Bearing(strongestDir)}. THE ROOM'S MOON STANDS AT {Bearing(moon)}. THE TWO "
                      + $"ARE {angle:F0} DEGREES APART. The prop is on the tabletop's layer, wears the "
                      + "tabletop's material object and now copies the tabletop's lighting flags, so "
                      + "the PROP AND THE TABLE AGREE; what that angle measures is the TABLE against "
                      + "the ROOM, and it is a GAME light against a BAKED moon. This class cannot "
                      + "close it: the environment carries no realtime light at all (SkyAlternative "
                      + "creates none — the moon is an authored constant baked into the bundle's "
                      + "shaders), and the light that does reach the tabletop belongs to the game's "
                      + "map scene. Closing it means re-aiming that game light along MoonDir while a "
                      + "3D style is live, and restoring it when the style closes — which is "
                      + "SkyAlternative's business, since it owns both the moon and the style "
                      + "lifecycle, and is NOT reachable from this file. "
                      + (angle > 45f
                         ? "AT THIS ANGLE THE USER'S REPORT IS CONFIRMED IN NUMBERS: the table and its "
                           + "legs really are lit from somewhere the moon is not."
                         : "At this angle the two are broadly in agreement, so a remaining complaint "
                           + "about the lighting is about intensity or colour and not direction.");
        }

        return $"{indent}{enabledCount} enabled light(s) in the scene; {discriminating} of them "
               + $"light layer {tableLayer} and layer {modLayer} DIFFERENTLY. "
               + (discriminating == 0
                  ? "So the layer never changed the shading here and ModBuild 198's stated risk was "
                    + "real but unrealised — the pale legs were the MATERIAL, and only the material. "
                  : "So ModBuild 198's legs WERE lit by a different light set than the table they held "
                    + "up, exactly as that build's open-risk note predicted; putting them on the "
                    + "table's own layer is what removes it. ")
               + verdict
               + $"\n{indent}  the moon : {moonSource}"
               + $"\n{indent}  ambient  : mode {RenderSettings.ambientMode}, intensity "
               + $"{RenderSettings.ambientIntensity:F2}, sky ({RenderSettings.ambientSkyColor.r:F2}, "
               + $"{RenderSettings.ambientSkyColor.g:F2}, {RenderSettings.ambientSkyColor.b:F2}), flat "
               + $"({RenderSettings.ambientLight.r:F2}, {RenderSettings.ambientLight.g:F2}, "
               + $"{RenderSettings.ambientLight.b:F2}) — this is the floor every DOWN-facing face "
               + "gets, including the new underside panel, and it is shared with the tabletop."
               + (named < enabledCount
                  ? $"\n{indent}  ({enabledCount - named} further enabled light(s) not named; the cap "
                    + $"is {LightCap}. The counts above cover ALL {enabledCount}.)"
                  : "")
               + sb;
    }

    // ---- finding the table ---------------------------------------------------------------------

    /// <summary>
    /// WHICH RENDERER IS THE TABLETOP — by MEASUREMENT, never by name, so a renamed or a
    /// per-map-variant asset still resolves and a coincidence cannot.
    ///
    /// <para>MODBUILD 198 GOT THIS WRONG, AND ALL FOUR REPORTED FAULTS ARE THAT ONE MISTAKE. Its
    /// hardware line reads: <c>'GH_Campaign_Map' on layer 0 is the only/smallest non-mod SLAB ...
    /// 0.96 x 0.00 x 1.20 m ... material 'GloomhavenVR.MapRoom.MapUnlit.0'</c>. That renderer IS the
    /// parchment — the very object <c>MapParchment</c> holds this mod's own unlit override on. Two
    /// omissions let it win: the class's doc claimed the candidate must not be "the parchment itself"
    /// but no line of code tested it, and the tie-break prefers the SMALLEST footprint, which the map
    /// (1.15 m²) is against the real table (3.57 m²). Everything the user photographed follows:
    /// legs at the MAP's corners, painted with the MAP's texture, and standing 4.4 mm proud of a
    /// 0.6 mm sheet. Two independent tests now make it impossible — identity (5) and thickness (6).</para>
    ///
    /// <para>A candidate qualifies when all of these hold, and each is there to reject something the
    /// captured scene actually contains:</para>
    /// <list type="number">
    ///   <item>it is DRAWN — enabled and active in the hierarchy. (This rejects <c>cityMap</c>
    ///   'GH_Campaign_Map_Gloomhaven', which the MAP SCENE REPORT shows sitting inactive at exactly
    ///   the world map's bounds — a perfect decoy for every other test here.)</item>
    ///   <item>it is the game's, i.e. not on the mod layer, and it is not this prop's own geometry.
    ///   (The legs may now stand on layer 0 with the table — see <see cref="ChooseLayer"/> — so the
    ///   layer test alone no longer excludes them.)</item>
    ///   <item>its horizontal footprint CONTAINS the parchment's — the map lies on the table, so the
    ///   table is at least as big. (This is what rejects <c>FogTarget</c>, which is wider than the
    ///   map in X but narrower in Z.)</item>
    ///   <item>it is a SLAB: thickness at most <see cref="SlabThicknessFactor"/> of its smaller
    ///   horizontal extent, and no more than <see cref="MaxTableFactor"/> times the map across.</item>
    ///   <item>IT IS NOT THE PARCHMENT — not that renderer, not its GameObject, and not an ancestor or
    ///   descendant of it. Identity, so no threshold can be tuned into letting it through.</item>
    ///   <item>IT IS A BOARD, NOT A DECAL: thickness at least <see cref="MinTableThicknessMeters"/>.
    ///   This is the measurement that separates a 148 mm tabletop from a 0.6 mm map, and it would have
    ///   rejected 198's pick on its own even without (5).</item>
    ///   <item>its TOP FACE is flush with the parchment's top plane to within
    ///   <see cref="TopBandMeters"/>, and is not ABOVE it. A table under the map, not a lid over it.</item>
    /// </list>
    /// Among survivors the SMALLEST horizontal footprint wins, so a tabletop nested inside a larger
    /// platform is preferred to the platform.
    ///
    /// <para>COST: one <c>FindObjectsOfType</c>, on the build frame only, and never again while the
    /// legs stand — the same order of cost as the map scene report the room already emits on entry,
    /// and deliberately not the per-frame kind.</para>
    /// </summary>
    internal static bool TryFindTable(MeshRenderer? parchmentRenderer, Bounds parchment, float scale,
                                      out MeshRenderer? table, out string survey)
    {
        table = null;
        var sb = new StringBuilder(256);
        int considered = 0, named = 0, skippedParchment = 0, skippedThin = 0;
        float best = float.MaxValue;
        try
        {
            int mod = VRLayers.ModLayer;
            float margin = ContainMarginMeters * scale;
            float band = TopBandMeters * scale;
            float minThick = MinTableThicknessMeters * scale;
            Transform? parchTf = parchmentRenderer != null ? parchmentRenderer.transform : null;
            float parchWidest = Mathf.Max(Mathf.Abs(parchment.size.x), Mathf.Abs(parchment.size.z));
            MeshRenderer[] all = Object.FindObjectsOfType<MeshRenderer>();
            for (int i = 0; i < all.Length; i++)
            {
                MeshRenderer r = all[i];
                if (r == null || r.gameObject.layer == mod)
                    continue;
                // Not this prop's own geometry. The legs may share the table's layer now, so the layer
                // test above no longer covers them; the name is authored two lines apart in Build.
                if (r.transform.parent != null && r.transform.parent.name == RootName)
                    continue;
                // NOT DRAWN, NOT A TABLE. 'GH_Campaign_Map_Gloomhaven' (the city map) sits inactive at
                // exactly the world map's bounds and would otherwise pass every geometric test.
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;
                Bounds b = r.bounds;
                float sizeX = Mathf.Abs(b.size.x), sizeZ = Mathf.Abs(b.size.z);
                float thick = Mathf.Abs(b.size.y);
                bool contains = b.min.x <= parchment.min.x + margin && b.max.x >= parchment.max.x - margin
                             && b.min.z <= parchment.min.z + margin && b.max.z >= parchment.max.z - margin;
                if (!contains)
                    continue;
                considered++;

                // (5) IDENTITY, and it is checked before any threshold so nothing can be tuned into
                // letting the map through. ModBuild 198 had this sentence in its doc and not in its code.
                if (parchTf != null && (r == parchmentRenderer
                                        || r.transform == parchTf
                                        || r.transform.IsChildOf(parchTf)
                                        || parchTf.IsChildOf(r.transform)))
                {
                    skippedParchment++;
                    if (named < CandidateCap)
                    {
                        named++;
                        sb.Append($"\n              REJECTED '{r.name}' L{r.gameObject.layer} "
                                  + $"{sizeX / scale:F2} x {thick / scale:F3} x {sizeZ / scale:F2} m — "
                                  + "IT IS THE PARCHMENT (or shares its transform). This is the exact "
                                  + "renderer ModBuild 198 stood the legs on");
                    }
                    continue;
                }

                bool board = thick >= minThick;
                bool slab = thick <= SlabThicknessFactor * Mathf.Min(sizeX, sizeZ);
                bool sized = Mathf.Max(sizeX, sizeZ) <= MaxTableFactor * parchWidest;
                bool flush = Mathf.Abs(b.max.y - parchment.max.y) <= band && b.max.y <= parchment.max.y + margin;
                if (!board)
                    skippedThin++;
                if (board && slab && sized && flush)
                {
                    float area = sizeX * sizeZ;
                    if (area < best)
                    {
                        best = area;
                        table = r;
                    }
                    continue;
                }
                if (named < CandidateCap)
                {
                    named++;
                    sb.Append($"\n              REJECTED '{r.name}' L{r.gameObject.layer} "
                              + $"{sizeX / scale:F2} x {thick / scale:F3} x {sizeZ / scale:F2} m, top "
                              + $"{(b.max.y - parchment.max.y) / scale:F3} m from the map's plane — "
                              + $"{(board ? "" : $"a DECAL, not a board ({thick / scale * 1000f:F1} mm thick, needs {MinTableThicknessMeters * 1000f:F0}); ")}"
                              + $"{(slab ? "" : "not a slab; ")}{(sized ? "" : "too big for a table; ")}"
                              + $"{(flush ? "" : "top not flush with (or is above) the map")}");
                }
            }
        }
        catch (System.Exception ex)
        {
            survey = $"the tabletop sweep threw ({ex.GetType().Name}: {ex.Message}).";
            return false;
        }

        if (table != null)
        {
            Bounds b = table.bounds;
            survey = $"MEASURED, not named: '{table.name}' on layer {table.gameObject.layer} is the "
                     + "only/smallest DRAWN, non-mod, non-parchment BOARD that contains the map's "
                     + "footprint and whose top face is flush under the map's own plane — "
                     + $"{Mathf.Abs(b.size.x) / scale:F2} x {Mathf.Abs(b.size.y) / scale:F3} x "
                     + $"{Mathf.Abs(b.size.z) / scale:F2} m ({Mathf.Abs(b.size.x):F1} x "
                     + $"{Mathf.Abs(b.size.y):F1} x {Mathf.Abs(b.size.z):F1} world units), top face "
                     + $"{(parchment.max.y - b.max.y) / scale * 1000f:F0} mm UNDER the map's plane, "
                     + $"world centre ({b.center.x:F2}, {b.center.y:F2}, {b.center.z:F2}). "
                     + $"{considered} renderer(s) contained the map and were tested; "
                     + $"{skippedParchment} rejected as THE PARCHMENT ITSELF and {skippedThin} as a "
                     + $"decal under {MinTableThicknessMeters * 1000f:F0} mm — the two tests ModBuild "
                     + $"198 lacked, and either one alone would have changed its answer.{sb}";
            return true;
        }
        survey = $"{considered} drawn non-mod renderer(s) contain the map's footprint and NONE of them "
                 + $"is a BOARD (at least {MinTableThicknessMeters * 1000f:F0} mm thick), slab-shaped "
                 + "and flush under the map's own top plane; a further "
                 + $"{skippedParchment} were the parchment itself and {skippedThin} were decals. "
                 + "Without the tabletop this class has neither the corners to stand legs at nor the "
                 + "material to skin them with, so it builds NOTHING — a leg placed against a guessed "
                 + "footprint is worse than no leg, which is exactly what ModBuild 198 shipped. "
                 + "If the game's map screen really has changed, the rejections below are the "
                 + $"numbers to correct this file's thresholds from.{sb}";
        return false;
    }

    /// <summary>
    /// THE ENVIRONMENT ROOM'S FLOOR PLANE, exactly — the room root's own world Y.
    ///
    /// <para>That value is not an estimate. <c>SkyAlternative.TryPlaceRoom</c> computes
    /// <c>floorY = boardUndersideY − FloatGap</c> and writes it straight into the room's transform
    /// position, then never writes the transform again ("WORLD-FIXED from now on: no per-frame
    /// writes, no rig-scale tracking, no re-seat of any kind"); and the bake authors both rooms with
    /// their floor at local y = 0 ("Both rooms have CLOSED opaque floors around the origin ...
    /// floor at y=0"). So the room root's Y IS the plane, to the bit.</para>
    ///
    /// <para>IT IS FOUND BY NAME, AND THAT IS A COMPROMISE WORTH NAMING. <c>SkyAlternative</c> keeps
    /// its room GameObject private and exposes no floor accessor, and this lane does not own that
    /// file — so the object is looked up by the name that file gives it,
    /// <c>"GloomhavenVR.SkyAlternative.Room." + style</c>. The cost of a rename there is that the
    /// legs stop being built and say so in this log rather than standing in the wrong place. A
    /// two-line <c>internal static bool TryRoomFloorY(out float)</c> on <c>SkyAlternative</c> would
    /// retire the lookup; it is flagged in this round's report.</para>
    /// </summary>
    internal static bool TryFindRoomFloor(SkyStyle style, out float floorY, out Transform? roomRoot,
                                          out string source)
    {
        floorY = 0f;
        roomRoot = null;
        string name = "GloomhavenVR.SkyAlternative.Room." + style;
        GameObject? room = GameObject.Find(name);
        if (room == null)
        {
            source = $"the environment room '{name}' is not in the scene (or not active) yet, so "
                     + "there is no floor plane to stand the legs on. The style says it should be "
                     + "there, so this is almost certainly the frame or two before "
                     + "SkyAlternative.TryPlaceRoom lands it; the build retries. If it persists, the "
                     + "room is refusing to place (its own log line says why) or that class renamed "
                     + "its root.";
            return false;
        }
        roomRoot = room.transform;
        floorY = room.transform.position.y;
        source = $"the room root '{name}' transform.position.y = {floorY:F2} — SkyAlternative writes "
                 + "the computed floor plane straight into it and never writes it again, and the "
                 + "bake authors both rooms with their floor at local y=0, so this IS the plane";
        return true;
    }

    /// <summary>
    /// TAKE THE TABLE'S TEXTURE COORDINATES, NOT JUST ITS TEXTURE — by TILING the grain, not by
    /// clamping it into a window.
    ///
    /// <para>WHY THE CLAMP IS GONE. ModBuild 198 mapped every leg vertex at the top's texel scale and
    /// then CLAMPED it into the top's UV rectangle trimmed 15 % a side. That is not a safe operation:
    /// a clamp applied per VERTEX is not a clamp applied per pixel. Vertex UVs interpolate, so as soon
    /// as one corner of a face is clamped and another is not, the whole face's mapping is squashed
    /// toward the window edge — and if a face's whole span exceeds the window, the face collapses onto
    /// one row of texels and renders as a single flat colour. A 15 % trim off a 1.0-wide window leaves
    /// 0.70; a 0.78 m leg at 1.0 m per UV unit spans 0.78 of that, so 198's legs were within a hair of
    /// exactly that failure even before the material was wrong. There is no threshold that makes a
    /// per-vertex clamp correct, so it is replaced rather than retuned.</para>
    ///
    /// <para>TWO HONEST MODES, chosen by measurement:</para>
    /// <list type="bullet">
    ///   <item>TILE — the tabletop's own UVs cover at least <see cref="FullSheetThreshold"/> of the
    ///   0..1 sheet in both axes, so the texture is the table's OWN and repeating it is exactly what
    ///   the asset is for. The legs get CONTINUOUS UVs at the top's measured metres-per-UV-unit and
    ///   the sampler wraps them, so the grain on a leg is the same size and the same wood as on the
    ///   top, with no interpolation artefact anywhere. This is the expected case.</item>
    ///   <item>FIT — the UVs occupy a sub-rectangle, i.e. the wood is one page of an atlas and
    ///   repeating it would drag in the neighbouring page. Each leg is then mapped so that its WHOLE
    ///   box lands inside that page (inset by <see cref="AtlasPageInset"/> for bilinear bleed),
    ///   continuously and centred. The grain size is then whatever fits, the log says by what factor
    ///   it differs from the top's, and no sample can leave the page.</item>
    /// </list>
    /// <para>The mode is also forced to FIT when the material's own texture is set to
    /// <c>TextureWrapMode.Clamp</c>, because tiling a clamped texture stretches its edge row exactly
    /// the way the old per-vertex clamp did.</para>
    ///
    /// <para>Falls back to the whole 0..1 sheet at <see cref="FallbackMetresPerUv"/> when the mesh is
    /// unreadable or carries no UVs, and says so — the parchment's mesh was not readable in the
    /// ModBuild 198 session, so the tabletop's may not be either.</para>
    /// </summary>
    private void AdoptTableUvs(MeshRenderer table, Material? skin, Bounds top, float scale,
                               float legHeight, float legSide, out string source)
    {
        _uvRect = new Rect(0f, 0f, 1f, 1f);
        _uvCentre = new Vector2(0.5f, 0.5f);
        _uvWorldPerU = _uvWorldPerV = FallbackMetresPerUv * scale;
        _uvTile = true;

        // The wrap mode of the texture the legs will actually sample. A clamped texture cannot be
        // tiled without smearing its edge row, so it forces FIT whatever the UV span says.
        TextureWrapMode wrap = TextureWrapMode.Repeat;
        string wrapNote = "no main texture to read a wrap mode from";
        try
        {
            Texture? t = skin != null ? skin.mainTexture : null;
            if (t != null)
            {
                wrap = t.wrapMode;
                wrapNote = $"the skin's main texture '{t.name}' {t.width}x{t.height} wraps {wrap}";
            }
        }
        catch { /* keep the Repeat default; the dump below names the texture either way */ }
        bool mayTile = wrap == TextureWrapMode.Repeat || wrap == TextureWrapMode.Mirror;

        // The largest half-extent any leg vertex reaches from its own leg centre: the leg is
        // legSide x legHeight x legSide and legHeight dominates by an order of magnitude.
        float halfReach = 0.5f * Mathf.Max(legHeight, legSide);

        try
        {
            var mf = table.GetComponent<MeshFilter>();
            Mesh? mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                _uvTile = mayTile;
                if (!_uvTile)
                    FitIntoWindow(halfReach, AtlasPageInset);
                source = $"the tabletop's mesh is {(mesh == null ? "absent" : "not CPU-readable")}, so "
                         + "neither its texel scale nor its UV window can be measured. The legs are "
                         + $"mapped over the FULL 0..1 sheet at {FallbackMetresPerUv:F2} m per UV unit "
                         + $"in {(_uvTile ? "TILE" : "FIT")} mode ({wrapNote}). DISPROOF: if the wood on "
                         + "the legs is the right colour but a visibly finer or coarser grain than the "
                         + $"top's, this line is why — correct {nameof(FallbackMetresPerUv)} against "
                         + "the texture size named in the material dump below";
                return;
            }
            Vector2[] uv = mesh.uv;
            if (uv == null || uv.Length == 0)
            {
                _uvTile = mayTile;
                if (!_uvTile)
                    FitIntoWindow(halfReach, AtlasPageInset);
                source = "the tabletop's mesh carries no UV0 at all, so the legs are mapped over the "
                         + $"full 0..1 sheet at {FallbackMetresPerUv:F2} m per UV unit in "
                         + $"{(_uvTile ? "TILE" : "FIT")} mode (whatever the table's shader does with "
                         + $"texture coordinates, it is not reading channel 0). {wrapNote}";
                return;
            }
            int stride = Mathf.Max(1, uv.Length / 4096);
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;
            int sampled = 0;
            for (int i = 0; i < uv.Length; i += stride)
            {
                Vector2 t = uv[i];
                if (float.IsNaN(t.x) || float.IsNaN(t.y))
                    continue;
                sampled++;
                if (t.x < minU) minU = t.x;
                if (t.x > maxU) maxU = t.x;
                if (t.y < minV) minV = t.y;
                if (t.y > maxV) maxV = t.y;
            }
            if (sampled == 0 || !(maxU > minU) || !(maxV > minV))
            {
                _uvTile = mayTile;
                if (!_uvTile)
                    FitIntoWindow(halfReach, AtlasPageInset);
                source = $"the tabletop's {uv.Length} UV(s) are degenerate, so the legs are mapped over "
                         + $"the full 0..1 sheet at {FallbackMetresPerUv:F2} m per UV unit in "
                         + $"{(_uvTile ? "TILE" : "FIT")} mode. {wrapNote}";
                return;
            }

            // THE TEXEL SCALE. The top is a slab, so its two horizontal extents are what its UV
            // rectangle is stretched across; which extent went to U and which to V is unknowable
            // without walking the triangles, and it does not matter for a SIZE — the average of the
            // two is right to within the slab's own aspect and the clamp catches anything absurd.
            float uSpan = maxU - minU, vSpan = maxV - minV;
            float mU = Mathf.Clamp(Mathf.Abs(top.size.x) / scale / uSpan, MinMetresPerUv, MaxMetresPerUv);
            float mV = Mathf.Clamp(Mathf.Abs(top.size.z) / scale / vSpan, MinMetresPerUv, MaxMetresPerUv);
            float metresPerUv = 0.5f * (mU + mV);
            _uvWorldPerU = _uvWorldPerV = metresPerUv * scale;
            _uvRect = Rect.MinMaxRect(minU, minV, maxU, maxV);
            _uvCentre = _uvRect.center;

            bool fullSheet = uSpan >= FullSheetThreshold && vSpan >= FullSheetThreshold;
            _uvTile = fullSheet && mayTile;
            if (_uvTile)
            {
                // TILE: the sheet is the table's own. Continuous UVs at the top's density, sampler
                // wraps. The window is irrelevant and is widened so nothing can ever bite.
                _uvRect = Rect.MinMaxRect(-1e6f, -1e6f, 1e6f, 1e6f);
                source = $"MEASURED off the tabletop's own mesh '{mesh.name}' ({uv.Length} UV(s), "
                         + $"{sampled} sampled): they span ({minU:F3}..{maxU:F3}, {minV:F3}..{maxV:F3}) "
                         + $"across a {Mathf.Abs(top.size.x) / scale:F2} x "
                         + $"{Mathf.Abs(top.size.z) / scale:F2} m top, i.e. {mU:F3} / {mV:F3} m per UV "
                         + $"unit. That span covers at least {FullSheetThreshold:P0} of the 0..1 sheet "
                         + "in both axes and " + wrapNote + ", so the texture is the TABLE'S OWN and "
                         + $"TILE mode applies: the legs get continuous UVs at {metresPerUv:F3} m per "
                         + "UV unit — the SAME grain size as the top, the same wood, and no per-vertex "
                         + "clamp anywhere to squash a face";
            }
            else
            {
                float before = _uvWorldPerU;
                FitIntoWindow(halfReach, AtlasPageInset);
                source = $"MEASURED off the tabletop's own mesh '{mesh.name}' ({uv.Length} UV(s), "
                         + $"{sampled} sampled): they span ({minU:F3}..{maxU:F3}, {minV:F3}..{maxV:F3}) "
                         + $"across a {Mathf.Abs(top.size.x) / scale:F2} x "
                         + $"{Mathf.Abs(top.size.z) / scale:F2} m top, i.e. {mU:F3} / {mV:F3} m per UV "
                         + $"unit. FIT mode, because "
                         + (fullSheet
                            ? "the texture may not be repeated (" + wrapNote + ")"
                            : $"that span is under {FullSheetThreshold:P0} of the 0..1 sheet, i.e. the "
                              + "table's wood is ONE PAGE of an atlas and tiling it would drag the "
                              + "neighbouring page onto the legs")
                         + $": each leg's whole box is mapped continuously INSIDE the page "
                         + $"({_uvRect.xMin:F3}..{_uvRect.xMax:F3}, {_uvRect.yMin:F3}..{_uvRect.yMax:F3}), "
                         + $"which costs {_uvWorldPerU / Mathf.Max(before, 1e-6f):F2}x the top's grain "
                         + "size. Right wood, coarser grain — and nothing can leave the page";
            }
        }
        catch (System.Exception ex)
        {
            _uvRect = new Rect(0f, 0f, 1f, 1f);
            _uvCentre = new Vector2(0.5f, 0.5f);
            _uvWorldPerU = _uvWorldPerV = FallbackMetresPerUv * scale;
            _uvTile = mayTile;
            if (!_uvTile)
                FitIntoWindow(halfReach, AtlasPageInset);
            source = $"reading the tabletop's UVs threw ({ex.GetType().Name}: {ex.Message}), so the "
                     + $"legs are mapped over the full 0..1 sheet at {FallbackMetresPerUv:F2} m per "
                     + $"UV unit in {(_uvTile ? "TILE" : "FIT")} mode";
        }
    }

    /// <summary>
    /// FIT MODE'S ONE PIECE OF ARITHMETIC: inset the measured UV window by
    /// <paramref name="inset"/> a side, then COARSEN the metres-per-UV-unit until a leg's whole
    /// half-reach maps inside it. Because the density is chosen so the geometry fits, the mapping
    /// stays CONTINUOUS across every face — which is the whole difference from ModBuild 198, where a
    /// per-vertex clamp could squash one face while leaving its neighbour alone.
    /// </summary>
    private void FitIntoWindow(float halfReach, float inset)
    {
        float insetU = _uvRect.width * inset, insetV = _uvRect.height * inset;
        _uvRect = Rect.MinMaxRect(_uvRect.xMin + insetU, _uvRect.yMin + insetV,
                                  _uvRect.xMax - insetU, _uvRect.yMax - insetV);
        _uvCentre = _uvRect.center;
        float halfU = Mathf.Max(_uvRect.width * 0.5f, 1e-5f);
        float halfV = Mathf.Max(_uvRect.height * 0.5f, 1e-5f);
        // world units per UV unit must be at least halfReach / halfSpan for the reach to fit.
        _uvWorldPerU = Mathf.Max(_uvWorldPerU, halfReach / halfU);
        _uvWorldPerV = Mathf.Max(_uvWorldPerV, halfReach / halfV);
    }

    // ---- geometry ----------------------------------------------------------------------------

    /// <summary>
    /// THE UNDERSIDE — one closed box that gives the game's hollow tabletop a bottom, appended to the
    /// legs' own mesh so the whole prop is still ONE draw call.
    ///
    /// <para>THE FAULT. <c>GH_Map_TableTop_Lg</c> draws a top face and four side faces and nothing
    /// underneath, so seen from below the board is an open shell. Worse, what shows through it is the
    /// campaign map: <c>MapParchment</c> puts this mod's own UNLIT material on the parchment, and an
    /// unlit surface is at full brightness from either side, so the map reads as a lit panel floating
    /// inside the table (.planning/debug/tisch_unten.jpg). The user's ruling: "I want it to have a
    /// tabletop from below as well, so you cannot see through."</para>
    ///
    /// <para>WHERE IT GOES, AND WHY THAT PLACE CANNOT BE SEEN FROM ABOVE. The box lives entirely
    /// inside the slab's own AABB: its bottom face is <see cref="UndersideClearanceMeters"/> ABOVE
    /// <c>bounds.min.y</c> — the lowest point anywhere in the slab's mesh — and its rim is
    /// <see cref="UndersideEdgeInsetMeters"/> inside the slab's side faces. Every point of it is
    /// therefore below the slab's drawn top face and inside its drawn side faces, so from any eye at
    /// or above the tabletop the slab itself is in the way. It also cannot change the silhouette the
    /// user has already accepted: nothing of it reaches the AABB.</para>
    ///
    /// <para>WHY A BOX AND NOT A QUAD. A bare quad inset from the edge leaves an open slot round the
    /// rim, and a ray up through that slot enters the hollow board and lands on the unlit parchment —
    /// the same bright leak, reduced to a hairline. The four side walls close it. See
    /// <see cref="UndersideThicknessMeters"/>.</para>
    ///
    /// <para>IT DOES NOT TOUCH THE PARCHMENT, AND THAT IS DELIBERATE. ModBuild 199 was burnt treating
    /// the decal as the table; the parchment is still excluded from <see cref="TryFindTable"/> by
    /// IDENTITY and by the <see cref="MinTableThicknessMeters"/> board test, and this box is measured
    /// from the slab's bounds only. It is not welded to, parented to, or offset from the map.</para>
    ///
    /// <para>THE LEGS PASS THROUGH IT, which is what makes them read as joined. Each leg's head is at
    /// <paramref name="anchoredHeadY"/>, far above this box, and the leg's shaft pierces the bottom
    /// face; the part inside the box is enclosed by opaque geometry and the part above it is inside
    /// the slab. No hole is cut and none is needed.</para>
    ///
    /// <para>COST: <see cref="UndersideTriangleCount"/> triangles, no collider, no second material and
    /// no second renderer.</para>
    /// </summary>
    private void Underside(Bounds top, float floorY, float scale, float anchoredHeadY, float legSide,
                           out string source)
    {
        float inset = UndersideEdgeInsetMeters * scale;
        float bottom = top.min.y + UndersideClearanceMeters * scale;
        // The board is capped so it can never reach the leg-head plane, i.e. it stays inside the
        // slab whatever bounds the sweep hands back.
        float headroom = Mathf.Max(anchoredHeadY - bottom, UndersideClearanceMeters * scale);
        float thickness = Mathf.Min(UndersideThicknessMeters * scale, headroom);
        // A slab so small that the rim inset would cross is clamped to the leg section rather than
        // inverted; that is ugly and bounded, and TryFindTable's own tests make it unreachable.
        float sizeX = Mathf.Max(Mathf.Abs(top.size.x) - 2f * inset, legSide);
        float sizeZ = Mathf.Max(Mathf.Abs(top.size.z) - 2f * inset, legSide);
        var centre = new Vector3(0f, bottom + thickness * 0.5f - floorY, 0f);
        Box(centre, new Vector3(sizeX, thickness, sizeZ));

        source = $"one CLOSED box of {UndersideTriangleCount} triangles, {sizeX / scale:F3} x "
                 + $"{thickness / scale:F3} x {sizeZ / scale:F3} m ({sizeX:F1} x {thickness:F1} x "
                 + $"{sizeZ:F1} world units), bottom face at y={bottom:F2} — that is "
                 + $"{UndersideClearanceMeters * 1000f:F0} mm ABOVE the slab's own bounds.min.y "
                 + $"(y={top.min.y:F2}) and {(top.max.y - bottom) / scale * 1000f:F0} mm below its top "
                 + $"face (y={top.max.y:F2}), with its rim {UndersideEdgeInsetMeters * 1000f:F0} mm "
                 + "inside the slab's side faces. EVERY POINT OF IT IS INSIDE THE SLAB'S OWN AABB, so "
                 + "it cannot be seen from any eye at or above the tabletop (the slab's drawn top and "
                 + "side faces are in the way), it cannot change the silhouette the user accepted, and "
                 + "it cannot z-fight: it shares no plane with the slab, with the parchment or with a "
                 + $"leg (a leg's outer face stands {EdgeInsetMeters * 1000f:F0} mm in, this rim "
                 + $"{UndersideEdgeInsetMeters * 1000f:F0} mm). It is a BOX and not a quad because a "
                 + "bare quad leaves an open slot round the rim and a ray up through that slot lands "
                 + "on the UNLIT parchment — the same bright leak as a hairline. THE PARCHMENT IS NOT "
                 + "TOUCHED: this is measured from the tabletop's bounds only, and the map is still "
                 + "excluded by identity and by the 30 mm board test. The four legs PIERCE the bottom "
                 + "face and are enclosed above it, which is why no hole is cut";
    }

    /// <summary>
    /// One closed axis-aligned box: six quads, 24 vertices, 12 triangles, hard edges (each face
    /// carries its own normals). <paramref name="size"/> is a full size per axis, in world units,
    /// and <paramref name="centre"/> doubles as the box's UV origin so each leg carries its own copy
    /// of the grain rather than four legs sampling one column of it.
    /// </summary>
    private void Box(Vector3 centre, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3 hx = Vector3.right * h.x, hy = Vector3.up * h.y, hz = Vector3.forward * h.z;
        Face(centre + hx, Vector3.right, hz, hy, centre);
        Face(centre - hx, Vector3.left, hz, hy, centre);
        Face(centre + hy, Vector3.up, hx, hz, centre);
        Face(centre - hy, Vector3.down, hx, hz, centre);
        Face(centre + hz, Vector3.forward, hx, hy, centre);
        Face(centre - hz, Vector3.back, hx, hy, centre);
    }

    /// <summary>
    /// One quad, wound so that it faces <paramref name="outward"/>.
    ///
    /// <para>THE WINDING IS DERIVED, NOT HOPED FOR, and this project has earned that sentence: seven
    /// meshes have shipped wound against the side they are seen from and one was invisible for ten
    /// builds. For the corner order below, triangles (0,1,2) and (0,2,3) give a geometric normal of
    /// Cross(hu, hv) — the same convention Unity's own documented quad uses — so when that disagrees
    /// with the outward direction the V half-axis is NEGATED. <see cref="Report"/> then checks the
    /// finished solid both ways (positive signed volume, and every triangle agreeing with its own
    /// vertex normal) and says so in the log.</para>
    ///
    /// <para>AND THE UV AXES ARE CAPTURED BEFORE THAT FLIP — a small correction to the bench class
    /// this file replaces, which took them after it. Flipping V mirrors the texture on that one face:
    /// invisible on the tiling grain of TILE mode, but in FIT mode it would walk the sample off the
    /// bottom of the atlas page it is fitted into.</para>
    /// </summary>
    private void Face(Vector3 centre, Vector3 outward, Vector3 hu, Vector3 hv, Vector3 uvOrigin)
    {
        Vector3 uAxis = hu.normalized, vAxis = hv.normalized;
        if (Vector3.Dot(Vector3.Cross(hu, hv), outward) < 0f)
            hv = -hv;
        int b = _verts.Count;
        AddVert(centre - hu - hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre + hu - hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre + hu + hv, outward, uAxis, vAxis, uvOrigin);
        AddVert(centre - hu + hv, outward, uAxis, vAxis, uvOrigin);
        _tris.Add(b); _tris.Add(b + 1); _tris.Add(b + 2);
        _tris.Add(b); _tris.Add(b + 2); _tris.Add(b + 3);
    }

    private void AddVert(Vector3 p, Vector3 n, Vector3 uAxis, Vector3 vAxis, Vector3 uvOrigin)
    {
        _verts.Add(p);
        _norms.Add(n);
        // uv from the vertex's own OFFSET FROM ITS LEG, at the table's own texel scale. Position-
        // derived, so a winding flip cannot move it; per-leg origin, so all four legs carry the grain
        // rather than one column of it.
        //
        // THE MAPPING IS CONTINUOUS AND THE CLAMP CANNOT BITE, which is the correction to ModBuild
        // 198. In TILE mode _uvRect is +/-1e6, i.e. the clamp is not there at all and the sampler's
        // own wrap does the repeating. In FIT mode AdoptTableUvs has already coarsened
        // _uvWorldPerU/V so that a leg's whole half-reach maps inside the window, so the clamp is a
        // numerical backstop and never a shaping operation. A clamp that SHAPES the mapping squashes
        // whole faces onto one texel row, because vertex UVs interpolate — see AdoptTableUvs.
        Vector3 d = p - uvOrigin;
        _uvs.Add(new Vector2(
            Mathf.Clamp(_uvCentre.x + Vector3.Dot(d, uAxis) / _uvWorldPerU, _uvRect.xMin, _uvRect.xMax),
            Mathf.Clamp(_uvCentre.y + Vector3.Dot(d, vAxis) / _uvWorldPerV, _uvRect.yMin, _uvRect.yMax)));
    }

    // ---- the report ---------------------------------------------------------------------------

    /// <summary>
    /// THE ONE TABLE-LEGS LINE. Everything a hardware round needs to decide whether this is right
    /// WITHOUT putting the headset on: the style gate and its decision, every dimension in BOTH real
    /// metres and world units, the triangle and draw-call counts, which renderer was identified as
    /// the table and which material was taken from it, which plane the feet stand on and the
    /// residual left over, and the mesh's own winding gate.
    /// </summary>
    private void Report(SkyStyle style, bool mixedReality, MapRoomSeat.Seat seat, Bounds parch,
                        MeshRenderer table, Bounds top, float floorY, float side, float cornerX,
                        float cornerZ, float scale, float weld, float headInset, float anchoredHeadY,
                        float slabThickness, float tallest, float shortest, int headsMeasured,
                        int floorsMeasured, int gapFree, int skinIndex, int layer, string floorSource,
                        string materialSource, string uvSource, string layerSource, string tableSurvey,
                        string undersideSource, string shadingSource)
    {
        // THE WINDING GATE, ON THE FINISHED SOLID. Cheap (48 triangles) and it runs once.
        float volume = 0f;
        int disagreeing = 0;
        for (int i = 0; i + 2 < _tris.Count; i += 3)
        {
            Vector3 p0 = _verts[_tris[i]], p1 = _verts[_tris[i + 1]], p2 = _verts[_tris[i + 2]];
            volume += Vector3.Dot(p0, Vector3.Cross(p1, p2)) / 6f;
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), _norms[_tris[i]]) <= 0f)
                disagreeing++;
        }
        float volumeCubicMetres = volume / (scale * scale * scale);
        bool woundRight = volume > 0f && disagreeing == 0;
        if (!woundRight)
            VRLog.Warn(Scope, $"MAP TABLE LEGS: the winding gate FAILED — signed volume "
                              + $"{volumeCubicMetres:F5} m^3 (must be positive) and {disagreeing} of "
                              + $"{_tris.Count / 3} triangle(s) disagree with their own vertex normal. "
                              + "The prop will be inside-out or partly invisible. This is the winding "
                              + "bug class this project has shipped seven times; read Face() before "
                              + "changing anything else.");

        // A SCALE CROSS-CHECK, not a new fact: re-derived from the world-space numbers this build
        // actually used, this must come back as MapRoomSeat.TableTopHeightMeters (0.78).
        float topAbovePlayerFloor = (parch.max.y - seat.FloorPosition.y) / scale;
        float playerAboveRoomFloor = (seat.FloorPosition.y - floorY) / scale;

        VRLog.Info(Scope,
            $"MAP TABLE LEGS built: {LegCount} leg(s), one at each CORNER of the game's own tabletop, "
            + $"PLUS an UNDERSIDE panel, {TriangleCount} triangles ({LegCount * 12} legs + "
            + $"{UndersideTriangleCount} underside) in ONE combined mesh on ONE MeshRenderer with ONE "
            + "material = 1 DRAW CALL, no collider, no Update, world-fixed (nothing here follows the "
            + "head).\n"
            + $"  gate      : {DescribeGate(style, mixedReality, true)} The gate is re-evaluated EVERY "
            + "FRAME (two field reads), so switching [Sky] Style at runtime builds or tears these "
            + "down on the NEXT FRAME, not on the next room entry.\n"
            + $"  the table : {tableSurvey}\n"
            + $"  footprint : the tabletop measures {Mathf.Abs(top.size.x) / scale:F3} x "
            + $"{Mathf.Abs(top.size.z) / scale:F3} m ({Mathf.Abs(top.size.x):F1} x "
            + $"{Mathf.Abs(top.size.z):F1} world units), {slabThickness / scale:F3} m "
            + $"({slabThickness:F1} world units) thick, world centre ({top.center.x:F2}, "
            + $"{top.center.y:F2}, {top.center.z:F2}). The map on it is "
            + $"{Mathf.Abs(parch.size.x) / scale:F3} x {Mathf.Abs(parch.size.z) / scale:F3} m at "
            + $"({parch.center.x:F2}, {parch.center.y:F2}, {parch.center.z:F2}) — the table is "
            + $"{Mathf.Abs(top.size.x) / Mathf.Max(Mathf.Abs(parch.size.x), 1e-4f):F2}x the map across "
            + $"and {Mathf.Abs(top.size.z) / Mathf.Max(Mathf.Abs(parch.size.z), 1e-4f):F2}x along, so "
            + "THESE ARE THE TABLE'S CORNERS AND NOT THE MAP'S. If those two factors ever read 1.00 "
            + "the sweep has picked the parchment again, which is exactly the ModBuild 198 defect.\n"
            + $"  leg       : {LegSideMeters:F3} x {LegSideMeters:F3} m section, "
            + $"{shortest / scale:F3}..{tallest / scale:F3} m tall ({shortest:F1}..{tallest:F1} world "
            + $"units) — EACH LEG HAS ITS OWN LENGTH, see the four rows below. Corners at "
            + $"+/-{cornerX / scale:F3} x +/-{cornerZ / scale:F3} m (+/-{cornerX:F1} x "
            + $"+/-{cornerZ:F1} world units) from the tabletop's centre, i.e. its outer face "
            + $"{EdgeInsetMeters * 1000f:F0} mm inside the top's edge; section {side:F1} world units.\n"
            + $"  the head  : referenced to the slab's **TOP** face y={top.max.y:F2}, "
            + $"{headInset / scale * 1000f:F1} mm down (head plane y={anchoredHeadY:F2}), which is "
            + $"{(anchoredHeadY - top.min.y) / scale * 1000f:F1} mm ABOVE the slab AABB's floor "
            + $"(y={top.min.y:F2}) and {(parch.max.y - anchoredHeadY) / scale * 1000f:F1} mm below the "
            + "map's visible surface. THAT REFERENCE FACE IS ModBuild 200'S FIX: 199 welded "
            + $"{WeldMeters * 1000f:F0} mm up from top.min.y, and top.min.y is the LOWEST POINT ANYWHERE "
            + "in an unreadable mesh, not the underside over a CORNER — any apron, moulding, bevel or "
            + "tilt left the head short by the difference, which is the daylight in "
            + "Tischbeine_Lücke.jpg (the near leg's lit top CAP is visible there, and a cap 5 mm inside "
            + "a 148 mm board cannot be). Measuring DOWN FROM THE TOP needs no knowledge of the "
            + "underside at all: wherever the wood ends, the leg emerges exactly there. NOTHING CAN "
            + $"EMERGE UPWARD either — the inset is min({HeadInsetBelowTopFaceMeters * 1000f:F0} mm, "
            + $"{HeadInsetMaxThicknessFraction:P0} of the measured "
            + $"{slabThickness / scale * 1000f:F0} mm thickness), and where an underside WAS probed the "
            + $"head is capped at {weld / scale * 1000f:F1} mm below the top face as well.\n"
            + $"  per leg   : {DescribeLegs(top, floorY, scale, headsMeasured, floorsMeasured, gapFree)}\n"
            + $"  underside : {undersideSource}. THE USER'S REPORT: \"the table has no real underside "
            + "— you can see through it from below, and you also see the map lying on the table\" "
            + "(.planning/debug/tisch_unten.jpg). The game's slab draws a top face and four sides and "
            + "NOTHING underneath, and what shows through is the parchment, which this mod draws "
            + "UNLIT — an unlit surface is at full brightness from either side, which is why the map "
            + "reads as a lit panel hanging inside the table. DISPROOF: if you can still see through "
            + "the table from below, this box is not being built (the triangle count above would read "
            + $"{LegCount * 12} and not {TriangleCount}) or the slab's bounds are not the board. If a "
            + "rim of the underside is visible from ABOVE, the slab's side face is recessed from its "
            + $"own AABB by more than {UndersideEdgeInsetMeters * 1000f:F0} mm and that is the number "
            + "to raise. If a hairline of the MAP shows round the rim from below, the box's walls are "
            + "too short and UndersideThicknessMeters is the number to raise.\n"
            + $"  material  : {materialSource}.\n"
            + $"{DescribeMaterials(table, "the TABLETOP renderer:", "              ")}\n"
            + $"              the legs wear mat[{skinIndex}] of that list, the same object.\n"
            + $"  texture   : {uvSource}. At that scale the {tallest / scale:F2} m leg carries "
            + $"{tallest / _uvWorldPerV:F2} UV unit(s) of grain down its length and "
            + $"{side / _uvWorldPerU:F2} across its face; mapping mode "
            + $"{(_uvTile ? "TILE (sampler wraps, no window)" : $"FIT (window {_uvRect.xMin:F3}..{_uvRect.xMax:F3}, {_uvRect.yMin:F3}..{_uvRect.yMax:F3})")}.\n"
            + "  atlas     : OPEN, AND DELIBERATELY LEFT OPEN. Because the slab's mesh is not "
            + "CPU-readable its UV window cannot be measured, so the prop is mapped over the WHOLE "
            + "0..1 sheet and samples whatever atlas pages fall under it — the mitred-corner and "
            + "inset-panel lines the user can see on the near leg. Right wood, wrong page. Picking a "
            + "plain plank sub-rectangle instead would have to be PROVED from the texture rather than "
            + "guessed, and the texture is a GAME asset that is not in this repository; the "
            + "'CPU-readable' flag printed for each texture in the dump above is the one runtime test "
            + "that can settle it. If _MainTex ever reads CPU-READABLE, one GetPixels at a coarse mip "
            + "is enough to find the flattest wood block and FIT the prop into it; while it reads NOT "
            + "CPU-readable, any sub-rectangle would be a guess, and a guess that lands on the "
            + "atlas's background would be worse than the wrong page. THE UNDERSIDE PANEL INHERITS "
            + "THIS: it is far larger than a leg, so at TILE density it repeats the sheet across "
            + "itself. It is also the darkest surface on the prop and is seen only from below.\n"
            + $"  the floor : each foot is cut {FootSinkMeters * 1000f:F0} mm "
            + $"({FootSinkMeters * scale:F1} world units) under the ground READ AT ITS OWN CORNER — the "
            + $"four values are in the per-leg rows above, and {floorsMeasured} of {LegCount} were "
            + "probed against the room's own colliders rather than taken from the plane. SOURCE: "
            + $"{floorSource}. The player's own tracking floor is y={seat.FloorPosition.y:F2}, i.e. "
            + $"{playerAboveRoomFloor * 1000f:F0} mm ABOVE the room floor — the two planes this room "
            + "has always disagreed on, and the reason the legs are stood on the ROOM's one: they are "
            + "meant to be seen reaching the ground. RESIDUAL: the plane is exact, but both rooms "
            + "have a gentle floor relief outside their dead-flat play disc (BuildEnvironmentRooms: "
            + "ForestY is identically 0 inside r=1.7 authored m and ramps over 1.7..4.6; CellarFloorY "
            + $"is +/-6 mm authored), which at this table's corner radius is about +/-19 mm (swamp) "
            + $"and +/-5 mm (cellar) perceived. Where the probe answered, that relief is MEASURED and "
            + $"not swallowed; where it did not, the {FootSinkMeters * 1000f:F0} mm sink spends the "
            + "error downward on purpose: a sunk foot reads as soft ground, a floating one as a bug.\n"
            + $"  heights   : tabletop top {(top.max.y - floorY) / scale:F3} m above the room floor "
            + $"and {(top.max.y - seat.FloorPosition.y) / scale:F3} m above the player's; parchment "
            + $"top {topAbovePlayerFloor:F3} m above the player's floor — CROSS-CHECK, that last one "
            + $"must read {MapRoomSeat.TableTopHeightMeters:F3}, and if it does not then the rig scale "
            + "used here and the one the seat was solved with disagree and every metre in this line "
            + $"is worth nothing. Rig scale {scale:F2} world units per real metre.\n"
            + $"  mesh      : {_verts.Count} verts, {_tris.Count / 3} tris, signed volume "
            + $"{volumeCubicMetres:F5} m^3, {disagreeing} triangle(s) disagreeing with their own "
            + $"normal — winding gate {(woundRight ? "PASSED" : "FAILED")}.\n"
            + $"  layer     : {layerSource}.\n"
            + $"  shading   : {shadingSource}.\n"
            + $"{DescribeLights(layer, VRLayers.ModLayer, style, "              ")}\n"
            + "  the player: NOT MOVED and not moveable from here — this class reads the seat, the "
            + "table, the room, the room's own _MoonDir (through sharedMaterials, never materials, so "
            + "no clone is created) and the scene's lights, and writes to NONE of them. The only "
            + "writes this build makes anywhere are to ITS OWN MeshRenderer's lighting flags. IT ALSO "
            + "TOUCHES NO WINDOW: it never "
            + "names a Canvas, a ConvertedPanel, a GrabbableModal or ModalFallback, holds no reference "
            + "that could reach one, and creates exactly one GameObject of its own with a MeshFilter "
            + "and a MeshRenderer on it. ModBuild 198's 'all floating windows moved below the table' "
            + "cannot have come from here and cannot come from here now.\n"
            + "  DISPROOF  : if the legs look like doll furniture or like pillars, the metre column "
            + "above is wrong while the world-unit column looks fine — that is a rig-scale slip, not "
            + "an art problem. If they float or sink, compare 'the floor' line's two planes. If they "
            + "stand under the MIDDLE of the table, the 'footprint' line's two size factors will read "
            + "1.00 and the sweep picked the map again. If they are the wrong wood, the TABLETOP "
            + "renderer dump above names every material, shader and texture the table actually draws "
            + "with, and 'material' names which of them the legs took — the two together settle "
            + "'wrong material' against 'wrong UV window' without a second photograph. If they poke "
            + "out of the top, 'the head' line already says in millimetres that they cannot. If they "
            + "are inside-out or half-missing, the winding gate line says so. If they appear where "
            + "they should not, the 'gate' line says which style was read. AND IF A LEG STILL SHOWS "
            + "DAYLIGHT AT THE TABLE, read the per-leg rows: the gap column is per corner and in "
            + "millimetres, so it names WHICH leg and BY HOW MUCH — and if all four read 0.0 mm while "
            + "the headset shows a gap, then the head is inside the slab's AABB and the wood above it "
            + "is not, i.e. the tabletop renderer's box is larger than the board it draws, and the "
            + "next number to raise is HeadInsetBelowTopFaceMeters (it is measured DOWN FROM THE TOP, "
            + "so raising it moves the head DOWN and lowering it moves the head UP into the board).");
    }

    /// <summary>
    /// THE FOUR ROWS — one per leg, and the counts that keep "no gap" from looking like "never
    /// measured". For each corner: the floor Y under it, the slab underside reference above it, the
    /// resulting length, and the residual gap at the head IN MILLIMETRES. All four gap figures must
    /// read 0.0 mm; the trailing count says how many of the four actually do, so a build in which the
    /// loop never ran cannot print the same thing as a build in which it ran and passed.
    /// </summary>
    private string DescribeLegs(Bounds top, float planeY, float scale, int headsMeasured,
                                int floorsMeasured, int gapFree)
    {
        var sb = new StringBuilder(512);
        sb.Append($"{gapFree} of {LegCount} leg(s) read ZERO gap at the head "
                  + $"(threshold {GapFreeMillimetres:F2} mm); {headsMeasured} of {LegCount} took a "
                  + "PROBED slab underside and the rest the top-face anchor, and "
                  + $"{floorsMeasured} of {LegCount} took a PROBED ground and the rest the room's floor "
                  + $"plane y={planeY:F2}. {LegCount} comparison(s) were made, so a silent skip cannot "
                  + "look like a pass.");
        for (int i = 0; i < LegCount; i++)
        {
            string cx = (i & 1) == 0 ? "-x" : "+x";
            string cz = (i & 2) == 0 ? "-z" : "+z";
            sb.Append($"\n              leg {i} ({cx},{cz}) at ({_legX[i]:F2}, {_legZ[i]:F2}): "
                      + $"floor y={_legFloorY[i]:F2} "
                      + $"({(_legFloorMeasured[i] ? "PROBED" : "plane")}"
                      + $"{(_legFloorMeasured[i] ? $", {(_legFloorY[i] - planeY) / scale * 1000f:+0.0;-0.0;0.0} mm of relief" : "")}"
                      + $"), foot y={_legFootY[i]:F2}, underside y={_legUndersideY[i]:F2} "
                      + $"({(_legHeadMeasured[i] ? "PROBED" : "slab AABB floor — a LOWER BOUND, so this row proves the worst case")}"
                      + $"), head y={_legHeadY[i]:F2} "
                      + $"({(_legHeadY[i] - _legUndersideY[i]) / scale * 1000f:F1} mm into the wood, "
                      + $"{(top.max.y - _legHeadY[i]) / scale * 1000f:F1} mm below the top face), "
                      + $"length {_legHeight[i] / scale:F3} m ({_legHeight[i]:F1} world units), "
                      + $"GAP {_legGapMm[i]:F1} mm{(_legGapMm[i] <= GapFreeMillimetres ? "" : "  <-- DAYLIGHT")}");
        }
        return sb.ToString();
    }
}
