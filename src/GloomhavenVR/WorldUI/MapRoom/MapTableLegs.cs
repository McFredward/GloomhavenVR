using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE MAP TABLE'S LEGS AND ITS UNDERSIDE — four legs, one at each corner of the table the campaign
/// map lies on, plus a panel that closes the game slab's open bottom, built PROCEDURALLY at runtime
/// as ONE mesh. The LEGS stand on the floor of a bundled 3D environment and exist only in the two
/// bundled 3D environments; the UNDERSIDE exists in EVERY environment and under mixed reality, and
/// since USER REPORT 11 those are two separate gates rather than one.
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
/// where the player is standing rather than at the next room entry. The LEGS are present for
/// <see cref="SkyStyle.Cellar"/> and <see cref="SkyStyle.SwampNight"/> — the two bundled 3D rooms,
/// and the only two styles that HAVE a floor to stand on — and absent for
/// <see cref="SkyStyle.Default"/>, <see cref="SkyStyle.OffBlack"/> and under mixed reality. The
/// MR clause is not merely obedience to the ruling: a passthrough world has the player's REAL floor
/// in it, several metres from wherever this mod thinks the room floor is, so a leg drawn to a
/// virtual floor plane would visibly miss the real one. <c>MixedReality.BackingsWanted</c> is the
/// same predicate the MR readability treatment keys off, so there is no second switch to drift.
/// THE UNDERSIDE HAS ITS OWN GATE (<see cref="UndersideWanted"/>) AND IT IS ALWAYS OPEN — see the
/// report-11 paragraph below for why the two questions are different ones.</para>
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
/// <para>USER REPORT 11 (against ModBuild 225), verbatim: "Die Unterseite des Tischs auch bei den
/// anderen Umgebungen (wie Mixed Reality, keine und Default) mit einbauen, dass man von unten nicht
/// durchsehen kann. Hierbei die Unterseiten-Platte ein bisschen nach oben schieben - aktuell ist da
/// eine sichtbare Lücke, durch die man durchschauen kann: siehe Tisch_Lücke.jpg." Two faults, and
/// they are independent of each other.</para>
///
/// <list type="number">
///   <item>THE GATE WAS SHARED, AND THE TWO PARTS ANSWER DIFFERENT QUESTIONS. Until report 11 the
///   underside rode on <see cref="StyleShowsLegs"/> and on the <c>!mixedReality</c> clause, so in
///   Default, in OffBlack and under passthrough the tabletop went back to being an open shell with
///   an unlit map glowing inside it. That was never a decision — it was the underside inheriting a
///   gate written for LEGS. A LEG NEEDS A FLOOR: it is measured against a probed ground, it has to
///   be seen reaching it, and under passthrough the player's REAL floor is somewhere this mod
///   cannot know. AN UNDERSIDE NEEDS NOTHING BUT THE SLAB: every number in it comes from the
///   tabletop renderer's own bounds, it hangs inside that slab's AABB, and it stands on nothing. So
///   the gates are split — <see cref="StyleShowsLegs"/> is untouched and
///   <see cref="UndersideWanted"/> is true in every style and under MR. AND UNDER MR AN OPAQUE
///   UNDERSIDE IS THE CORRECT ANSWER, not an exception to the see-through ruling: that ruling is
///   about not covering the passthrough room with mod geometry, and this panel covers nothing but
///   the inside of a slab the GAME already draws opaque. The user asked for it in those words
///   ("wie Mixed Reality, keine und Default"), and you cannot see through a real table either. It
///   is placed the way MR expects — inside the tabletop's own AABB, on the tabletop's own layer,
///   wearing the tabletop's own material OBJECT — so whatever the MR treatment does to the table it
///   does to this panel in the same draw call, and this class writes no <c>_Cull</c>, no blend state
///   and no material anywhere. NO GEOMETRY IS WELDED to anything, which is the other half of the
///   standing MR ruling.</item>
///   <item>THE PLATE WAS AT THE BOTTOM OF A HOLLOW AND LEFT AN OPEN RING ABOVE IT. The photograph
///   (<c>.planning/debug/Tisch_Lücke.jpg</c>) is taken from under the table: the slab's apron runs
///   across the top of the frame and under it there is a BRIGHT HORIZONTAL SLIT with the room — and
///   the parchment — visible through it. That is the diagnosis in one image.
///   <c>bounds.min.y</c> is the LOWEST POINT ANYWHERE in the slab's mesh, i.e. the bottom edge of
///   its apron, NOT the height of the wood over the middle of the table. This class has known that
///   since ModBuild 200 and it is the same mistake in a second place: 200 stopped anchoring the LEG
///   HEADS to <c>bounds.min.y</c>, and 201 then built the UNDERSIDE PANEL from it anyway. A 12 mm
///   board sitting 1 mm above the apron's bottom edge closes the bottom 13 mm of a hollow that is
///   over a hundred millimetres deep, and everything above it is open: at a grazing angle you look
///   in through the <see cref="UndersideEdgeInsetMeters"/> rim slot, up into the open shell, and
///   straight out at the unlit map. THE REPAIR IS NOT TO MOVE A THIN PLATE UP — that would only
///   move the slit, and moving it far enough to close it would make the plate stand proud of the
///   board. THE BOX SPANS THE HOLLOW INSTEAD: its bottom face stays at
///   <c>bounds.min.y + </c><see cref="UndersideClearanceMeters"/>, so the table can never look
///   thicker than the one he has already accepted, and its TOP face is welded up into the MEASURED
///   underside. See <see cref="Underside"/> for the arithmetic and for the one residual, which is a
///   number rather than a hope.</item>
/// </list>
///
/// <para>THE UNDERSIDE IS NOW MEASURED IN ITS OWN RIGHT, at <see cref="UndersideProbeCount"/>
/// points — the four corners (which the legs already probed) plus THE CENTRE, which is the part of
/// the underside the player in the photograph is actually looking at and which no probe covered
/// before. <see cref="TryMeasureUnderside"/> is unchanged; it is simply called at a fifth place and
/// its samples are reduced with MAX rather than MIN. Max is the safe direction and that is derived,
/// not preferred: a box top ABOVE the local wood is buried inside the board and invisible from every
/// eye, while a box top BELOW it is the open ring the user photographed.</para>
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
/// <para>COST: one combined mesh, <see cref="TrianglesFor"/> triangles — <c>4 x 12</c> for the legs
/// plus <see cref="UndersideTriangleCount"/> for the underside where BOTH parts are built, and
/// <see cref="UndersideTriangleCount"/> alone in Default, OffBlack and MR — ONE MeshRenderer with
/// ONE material = ONE draw call, built once, no per-frame allocation and no shadow pass. Four legs
/// of four different lengths cost exactly the same as four of one length, and a box that spans the
/// hollow costs exactly what a 12 mm one did — the extra shaft ModBuild 200 buries in the slab and
/// the extra height report 11 gives the box are hidden geometry, not extra geometry. EVERY COUNT IN
/// THE LOG IS DERIVED from which parts were actually built; a literal there would state the legs'
/// triangles in a build that has no legs.</para>
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
    //
    // REPORT 11 CORRECTS ONE OF THE THREE. The box's THICKNESS is no longer an authored 12 mm board:
    // 12 mm at the bottom of a >100 mm hollow left an open ring above it, which is the bright slit in
    // .planning/debug/Tisch_Lücke.jpg. The box now SPANS the hollow — same bottom face, top face
    // welded up into the MEASURED underside — and 12 mm survives only as a FLOOR on the thickness for
    // the case where the slab really is that shallow. The clearance and the rim inset are unchanged
    // and their reasoning below is unchanged with them.

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
    /// The MINIMUM thickness of the underside box, real metres — it is a BOX and not a bare quad,
    /// and that is what closes the rim.
    ///
    /// <para>A single down-facing quad inset from the slab's edge leaves an open slot all the way
    /// round: a ray coming up through that slot enters the hollow board, meets the top face from
    /// BEHIND (culled) and lands on the unlit parchment — i.e. exactly the bright leak being fixed,
    /// reduced to a hairline. The four side walls of a box close it for every ray that does not
    /// already start inside the wood.</para>
    ///
    /// <para>THE FACT THIS CONSTANT USED TO STATE BECAME FALSE IN REPORT 11, and the correction is
    /// worth writing down rather than deleting. ModBuild 201 shipped it as "the underside panel's own
    /// board thickness — 12 mm is a plausible board and stays two orders of magnitude inside the
    /// slab's measured 148 mm". Both halves were true and the conclusion was still wrong: a 12 mm
    /// board placed 1 mm above <c>bounds.min.y</c> closes the bottom 13 mm of the hollow, and the
    /// slab is 148 mm deep, so 135 mm of open shell sat above it. That ring is the bright slit in
    /// <c>.planning/debug/Tisch_Lücke.jpg</c>: at a grazing angle you look in through the
    /// <see cref="UndersideEdgeInsetMeters"/> rim slot, up past the little board and out at the unlit
    /// map. THE BOX NOW SPANS THE HOLLOW (see <see cref="Underside"/>), so its thickness is
    /// DERIVED — measured-underside minus <c>bounds.min.y</c> — and 12 mm is only the floor under
    /// that derivation, for a slab that genuinely is shallower than a plausible board.</para>
    /// </summary>
    private const float UndersideMinThicknessMeters = 0.012f;

    /// <summary>
    /// How many places the slab's underside is probed for the PANEL: the <see cref="LegCount"/>
    /// corners plus THE CENTRE.
    ///
    /// <para>The centre probe is new in report 11 and it is the one that matters for this fault. The
    /// four corner probes exist for the LEGS — they answer "how high is the wood where this leg has
    /// to meet it" — and a corner is exactly where an apron makes the wood lowest. The player in
    /// <c>Tisch_Lücke.jpg</c> is looking at the MIDDLE of the table, where there is no apron and the
    /// underside can be a hundred millimetres higher. Probing only the corners and believing the
    /// answer is the ModBuild 200 mistake with a different reference face; probing the centre as
    /// well costs one raycast on the build frame.</para>
    /// </summary>
    internal const int UndersideProbeCount = LegCount + 1;

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
    ///
    /// <para>REPORT 11 RE-EXAMINED IT AND KEPT IT AT 2 mm, WITH THE RESIDUAL WRITTEN DOWN AS A
    /// NUMBER. The 2 mm inset is the one path left into the shell once the box spans the hollow: a
    /// ray can enter the ring between the panel's outer wall and the slab's side plane from below and
    /// reach the parchment ONLY if it stays inside that ring for the whole height of the box, i.e.
    /// only if its slope from vertical is under <c>atan(inset / boxThickness)</c>. At the measured
    /// table that is <c>atan(2 / ~130) = 0.9 deg</c>, and to look up such a ray an eye 0.5 m below the
    /// rim must be within <c>0.5 m x tan(0.9 deg) = 8 mm</c> of the vertical line through it — and
    /// would then see a 2 mm-wide sliver. <see cref="Underside"/> computes and PRINTS that angle every
    /// build so the next round argues from it. Before the box spanned the hollow the same arithmetic
    /// gave <c>atan(2 / 13) = 8.8 deg</c> over a ring that then opened into the whole cross-section —
    /// which is why the slit was a slit and not a sliver.</para>
    ///
    /// <para>SHRINKING IT TO ZERO WOULD CLOSE THE RING EXACTLY, AND IS REJECTED: flush puts the
    /// panel's four side walls on the slab's own side planes, coplanar and same-facing, z-fighting
    /// along the whole visible rim of the table — a defect over 7.7 m of edge to remove a 2 mm sliver
    /// that needs the eye within 8 mm of a line. The other exact closure is a STEPPED solid: keep the
    /// walls 2 mm in and give the bottom face a flange out to the full AABB footprint, so every ray
    /// crossing the panel's bottom plane inside the footprint meets wood. That costs 12 more
    /// triangles and gives up this constant's own guarantee — the flange rim would poke out if the
    /// slab's side face is recessed from its AABB by any amount at all. It is the move to make IF a
    /// hardware round still shows a sliver at the very rim, and not before.</para>
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

    /// <summary>Triangles in ONE closed box: 6 quads x 2.</summary>
    internal const int BoxTriangleCount = 12;

    /// <summary>Triangles in the UNDERSIDE panel: one closed box.</summary>
    internal const int UndersideTriangleCount = BoxTriangleCount;

    /// <summary>Triangles in the LEGS alone: one closed box each.</summary>
    internal const int LegTriangleCount = LegCount * BoxTriangleCount;

    /// <summary>Triangles in the FULL prop — legs and underside. NOTHING READS THIS ANY MORE, and
    /// that is the point of keeping it: until report 11 it WAS the count, and the report and the
    /// class doc both quoted it. Since the gate split, the prop is built with or without legs and a
    /// literal would have the log claim 48 leg triangles in a build that has none, so every count now
    /// goes through <see cref="TrianglesFor"/>. It survives as the name a future reader will grep for
    /// after finding it in an old Player.log.</summary>
    internal const int TriangleCount = LegTriangleCount + UndersideTriangleCount;

    /// <summary>Triangles in the prop THIS build actually made. Every count printed anywhere goes
    /// through here.</summary>
    internal static int TrianglesFor(bool withLegs) =>
        (withLegs ? LegTriangleCount : 0) + UndersideTriangleCount;

    /// <summary>
    /// The two BUNDLED 3D environments — the only styles that put a room with a floor around the
    /// player, and (per the user's ruling) the only ones the LEGS appear in. <c>Default</c> keeps the
    /// game's own sky and has no floor at all; <c>OffBlack</c> is deliberately "no environment"; and
    /// mixed reality is handled by <see cref="LegsWanted"/> because it OVERRIDES the style dial
    /// (<c>MixedReality.Tick</c>: "MR ON ⇒ the sky is ALWAYS off ... whatever [Sky] Style says").
    ///
    /// <para>UNCHANGED BY REPORT 11, DELIBERATELY. Report 11 asks for the UNDERSIDE in every
    /// environment; it does not reopen the ruling about legs, and this predicate is still exactly
    /// the sentence the user wrote against ModBuild 197 ("The table legs should only be visible in
    /// the two 3D environments — in mixed reality, in NO environment, or in the DEFAULT environment
    /// they should not be there"). See <see cref="UndersideWanted"/> for the other half.</para>
    /// </summary>
    internal static bool StyleShowsLegs(SkyStyle style) =>
        style == SkyStyle.Cellar || style == SkyStyle.SwampNight;

    /// <summary>
    /// ARE THE LEGS WANTED — the style ruling AND the mixed-reality override, in one place so the
    /// build path and the gate line cannot answer differently.
    /// </summary>
    internal static bool LegsWanted(SkyStyle style, bool mixedReality) =>
        !mixedReality && StyleShowsLegs(style);

    /// <summary>
    /// IS THE UNDERSIDE WANTED — always, in every style and under mixed reality, and that is report
    /// 11's ruling in one line: "Die Unterseite des Tischs auch bei den anderen Umgebungen (wie Mixed
    /// Reality, keine und Default) mit einbauen, dass man von unten nicht durchsehen kann."
    ///
    /// <para>IT TAKES THE SAME TWO ARGUMENTS AS <see cref="LegsWanted"/> AND IGNORES BOTH, ON
    /// PURPOSE. The point of report 11 is that the two parts answer different questions, and a
    /// predicate that cannot see the style is a predicate no future round can accidentally couple
    /// back to it. A LEG NEEDS A FLOOR — it is measured against a probed ground, it has to be seen
    /// reaching it, and under passthrough the player's real floor is somewhere this mod cannot know.
    /// AN UNDERSIDE NEEDS NOTHING BUT THE TABLETOP SLAB: bottom face, top face, rim and thickness are
    /// all read off that one renderer's bounds and its own collider, so there is no environment state
    /// it could depend on. Under MR an opaque underside is not an exception to the see-through ruling
    /// (which is about mod geometry covering the passthrough room): this panel covers nothing but the
    /// inside of a slab the GAME already draws opaque, the user asked for it in those words, and you
    /// cannot see through a real table either.</para>
    ///
    /// <para>The parameters are kept so the signature states what it was ALLOWED to look at and
    /// chose not to; the compiler discards them.</para>
    /// </summary>
    internal static bool UndersideWanted(SkyStyle style, bool mixedReality) => true;

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

    // ---- THE UNDERSIDE PANEL'S OWN ROW. Written on the build frame only, and printed whether or
    // not the legs were built — since report 11 those are two independent parts. -----------------
    /// <summary>Whether THIS build made the four legs. The gate is split, so the prop can stand as
    /// an underside alone; every count and every row that names a leg is guarded by this.</summary>
    private bool _builtWithLegs;
    /// <summary>The slab underside over the table's CENTRE — the part of the wood the player in
    /// <c>Tisch_Lücke.jpg</c> is looking at, and the one place no probe covered before report 11.
    /// </summary>
    private float _undersideCentreY;
    private bool _undersideCentreMeasured;
    /// <summary>How many of the <see cref="UndersideProbeCount"/> probes answered.</summary>
    private int _undersideProbesMeasured;
    /// <summary>The ceiling the panel is built up to: the HIGHEST measured underside sample when any
    /// answered (max, not min — a box top above the local wood is buried and invisible, a box top
    /// below it is the ring the user photographed), otherwise <c>bounds.min.y</c>, which is a hard
    /// LOWER BOUND on the underside anywhere and therefore the worst case the residual is proved
    /// against.</summary>
    private float _undersideCeilingY;
    /// <summary>Whether that ceiling is a MEASUREMENT OF THE WOOD rather than a restatement of the
    /// AABB floor. A plain BoxCollider spanning the slab's bounds answers every upward probe with
    /// <c>bounds.min.y</c>, which is a hit, is not a lie, and carries no information — see
    /// <see cref="Underside"/>. The two must never print the same word.</summary>
    private bool _undersideCeilingInformative;
    private float _undersideBottomY;
    private float _undersideTopY;
    /// <summary>Millimetres of OPEN HOLLOW left between the panel's top face and the underside
    /// reference above it. Must read 0.0 — this is the ring in <c>Tisch_Lücke.jpg</c>, measured.
    /// </summary>
    private float _undersideOpenMm;
    /// <summary>Half-angle, in degrees, of the only cone that can still reach the parchment from
    /// below: <c>atan(rim inset / panel thickness)</c>. See <see cref="UndersideEdgeInsetMeters"/>.
    /// </summary>
    private float _undersideLeakDegrees;

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
    ///
    /// <para>SINCE REPORT 11 THERE ARE TWO GATE ANSWERS, NOT ONE, and this is where the difference
    /// is spent. <see cref="UndersideWanted"/> is always true, so <c>wanted</c> is always true and
    /// the prop is never torn down for a style change alone. What a style change CAN do is flip
    /// <see cref="LegsWanted"/> under a standing prop — Cellar to Default drops the legs, Default to
    /// Cellar adds them — and the mesh is built once, so that flip is a REBUILD. It is detected by
    /// comparing the live answer against <see cref="_builtWithLegs"/>, i.e. against what this build
    /// actually made, rather than against a remembered dial: "the prop stands" and "the prop stands
    /// with legs" are different facts and only the second one can be read off the geometry.</para>
    /// </summary>
    internal void Tick()
    {
        SkyStyle style = SkyAlternative.Style != null ? SkyAlternative.Style.Value : SkyStyle.Default;
        bool mixedReality = MixedReality.BackingsWanted;
        bool legsWanted = LegsWanted(style, mixedReality);
        bool undersideWanted = UndersideWanted(style, mixedReality);
        bool wanted = legsWanted || undersideWanted;

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
                Release($"the style gate closed — {DescribeGate(style, mixedReality, false, false)}");
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
            // ...AND SINCE REPORT 11, THE OTHER ONE: the LEG half of the gate flipped while the
            // underside half stayed open. The two parts share one mesh, so there is no way to add or
            // drop 48 triangles in place; the prop is released and rebuilt on the next retry, which
            // is the same one-frame cost a style change already had before the split.
            else if (_builtWithLegs != legsWanted)
            {
                Release(legsWanted
                        ? "the LEG gate opened while the underside was already standing — "
                          + $"[Sky] Style is now {style} and mixed reality is "
                          + $"{(mixedReality ? "ON" : "off")}, so the prop rebuilds WITH legs"
                        : "the LEG gate closed while the underside stays — "
                          + $"[Sky] Style is now {style} and mixed reality is "
                          + $"{(mixedReality ? "ON" : "off")}, so the prop rebuilds as the UNDERSIDE "
                          + "PANEL ALONE. The table keeps its bottom in every environment (user "
                          + "report 11); only the legs answer the two-3D-rooms ruling");
                _gateLogged = false;
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
            VRLog.Info(Scope, $"MAP TABLE LEGS: {DescribeGate(style, mixedReality, false, false)}"
                              + (_lastRefusal.Length > 0 ? $" {_lastRefusal}" : ""));
    }

    /// <summary>Tear the prop — legs and/or underside — down. Idempotent; the only exit.</summary>
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
        // WHAT THIS BUILD MADE IS FORGOTTEN WITH THE GEOMETRY. Tick compares the live leg gate
        // against this flag to decide whether a standing prop is still the right one; leaving it set
        // after a release would let a rebuilt underside-only prop claim it has legs.
        bool hadLegs = _builtWithLegs;
        _builtWithLegs = false;
        if (had)
        {
            VRLog.Info(Scope, $"MAP TABLE LEGS released ({reason}) — the prop "
                              + $"({(hadLegs ? $"{LegCount} leg(s) AND the underside panel" : "the UNDERSIDE PANEL alone")}) "
                              + "and its mesh are destroyed. The game's tabletop, its material and the "
                              + "environment room are untouched: this class only ever READ them.");
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

    /// <summary>
    /// WHICH PARTS ARE WANTED AND WHY — the LEG verdict and the UNDERSIDE verdict, separately,
    /// because since report 11 they can disagree and a single sentence about "the prop" would be
    /// false in three of the five style/MR combinations. Composed only when the answer changes,
    /// never per frame.
    ///
    /// <para>The five combinations this has to be truthful about: MR on (underside only, whatever
    /// the style dial says); Cellar and SwampNight with MR off (both parts); Default and OffBlack
    /// with MR off (underside only).</para>
    /// </summary>
    private static string DescribeGate(SkyStyle style, bool mixedReality, bool standing,
                                       bool builtWithLegs)
    {
        string legs;
        if (mixedReality)
            legs = "MIXED REALITY is on, so NO legs — a passthrough world already has the player's "
                   + "real floor in it and a leg drawn to a virtual floor plane would visibly miss "
                   + $"it. ([Sky] Style is {style}, and MR overrides it either way.)";
        else if (!StyleShowsLegs(style))
            legs = $"[Sky] Style is {style}, so NO legs — the user's ruling is the two BUNDLED 3D "
                   + "environments only (Cellar, SwampNight). Default keeps the game's own sky and "
                   + "OffBlack is deliberately no environment; neither has a floor to stand on.";
        else
            legs = $"[Sky] Style is {style} — one of the two bundled 3D rooms, so the legs are WANTED"
                   + (standing
                      ? (builtWithLegs
                         ? " and they stand."
                         : " but the prop standing right now was built WITHOUT them — the gate flipped "
                           + "this frame and it rebuilds on the next retry.")
                      : ", but they are not standing yet.");

        string underside =
            " THE UNDERSIDE PANEL IS WANTED REGARDLESS, in every style and under mixed reality "
            + "(user report 11: \"Die Unterseite des Tischs auch bei den anderen Umgebungen (wie "
            + "Mixed Reality, keine und Default) mit einbauen\"). It stands on nothing — every number "
            + "in it comes from the tabletop renderer's own bounds and its own collider — so unlike a "
            + "leg it needs no environment floor and no environment at all. Under MR that is not an "
            + "exception to the see-through ruling: the panel covers nothing but the inside of a slab "
            + "the GAME already draws opaque, and you cannot see through a real table either."
            + (standing
               ? $" It is standing{(builtWithLegs ? " with the legs" : " ALONE")}."
               : " It is not standing yet.");

        return legs + underside;
    }

    // ---- build -------------------------------------------------------------------------------

    /// <summary>
    /// Build the prop. WHICH PARTS ARE BUILT IS DECIDED HERE, ONCE, and everything downstream reads
    /// <c>wantLegs</c> rather than re-asking the gate — a second evaluation is a second chance to
    /// disagree, and the report has to be able to say what was actually made.
    ///
    /// <para>THE PRECONDITIONS ARE SPLIT WITH THE GATE. A leg needs the tabletop AND the environment
    /// room's floor plane; an underside needs the tabletop and nothing else. So
    /// <see cref="TryFindRoomFloor"/> is only REQUIRED when legs are wanted, and in Default, OffBlack
    /// and MR — where the room root does not exist at all — the panel is built without it and the
    /// prop's vertical origin becomes the slab's own <c>bounds.min.y</c> instead. Before report 11 a
    /// missing room refused the whole build, which is precisely why those three presentations had no
    /// underside.</para>
    /// </summary>
    private void Build(SkyStyle style, bool mixedReality)
    {
        bool wantLegs = LegsWanted(style, mixedReality);
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

        // THE ROOM FLOOR IS A **LEG** PRECONDITION AND NOTHING ELSE'S. Asking for it when no legs are
        // wanted would refuse the underside in exactly the three presentations report 11 is about —
        // Default and OffBlack have no room root, and under MR SkyAlternative is not running at all.
        float floorY = 0f;
        Transform? roomRoot = null;
        string floorSource =
            "NOT NEEDED this build — no legs are wanted, and the underside panel stands on nothing: "
            + "its bottom face, its top face, its rim and its thickness are all read off the tabletop "
            + "renderer's own bounds and its own collider. No room root was looked for.";
        bool haveFloor = false;
        if (wantLegs)
        {
            haveFloor = TryFindRoomFloor(style, out floorY, out roomRoot, out floorSource);
            if (!haveFloor)
            {
                Refuse("NOT BUILT: " + floorSource);
                return;
            }
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

        // ---- ONE MEASUREMENT PASS, BEFORE ANY GameObject EXISTS --------------------------------
        // Each leg gets its OWN floor, its OWN head and therefore its OWN length. Nothing is created
        // until all four have passed the plausibility window, so a refusal cannot leave half a prop
        // standing. All probes run on the build frame only and all of them are read-only queries.
        //
        // THE UNDERSIDE PROBE RUNS WHETHER OR NOT LEGS ARE WANTED, and that is report 11's other
        // half: the panel's top face is welded into the MEASURED underside, so in Default, OffBlack
        // and MR — where there are no legs to hang it off — the same four corner casts still have to
        // happen. The FLOOR probe is the one that is skipped, because a floor is a leg's question.
        int headsMeasured = 0, floorsMeasured = 0, gapFree = 0;
        float tallest = 0f, shortest = float.MaxValue;
        float ceiling = top.min.y;
        _undersideProbesMeasured = 0;
        for (int i = 0; i < LegCount; i++)
        {
            float sx = (i & 1) == 0 ? -1f : 1f;
            float sz = (i & 2) == 0 ? -1f : 1f;
            _legX[i] = top.center.x + cornerX * sx;
            _legZ[i] = top.center.z + cornerZ * sz;

            // THE SLAB'S UNDERSIDE OVER **THIS** CORNER. Probed if the table carries a collider;
            // otherwise the head is anchored to the TOP face, which needs no knowledge of the
            // underside at all, and the residual is proved against top.min.y — a hard lower bound on
            // where the underside can possibly be.
            _legHeadMeasured[i] = TryMeasureUnderside(table, _legX[i], _legZ[i], top, scale,
                                                      out float undersideY);
            if (_legHeadMeasured[i])
            {
                headsMeasured++;
                _undersideProbesMeasured++;
                _legUndersideY[i] = undersideY;
                // MAX, not min, and the reasoning is in the class doc: the panel is built up to the
                // HIGHEST wood any probe found, because a box top above the local wood is buried
                // inside the board and invisible from every eye, while a box top below it is the open
                // ring the user photographed.
                ceiling = Mathf.Max(ceiling, undersideY);
            }
            else
            {
                _legUndersideY[i] = top.min.y;
            }

            if (!wantLegs)
            {
                // NO LEG HERE. The row still carries the probe result — it is what the panel is
                // built from — but nothing is derived from a floor that was never read, and
                // DescribeLegs prints the corners as PROBE POINTS rather than as legs.
                _legFloorMeasured[i] = false;
                _legFloorY[i] = 0f;
                _legFootY[i] = 0f;
                _legHeadY[i] = 0f;
                _legHeight[i] = 0f;
                _legGapMm[i] = 0f;
                continue;
            }

            // THE FLOOR UNDER **THIS** CORNER. The plane is exact but the floor ART is not (both
            // rooms have a gentle relief outside their play disc), so the ground itself is probed
            // first and the plane is the fallback.
            _legFloorMeasured[i] = TryMeasureFloor(roomRoot, _legX[i], _legZ[i], floorY, scale,
                                                   out float groundY);
            _legFloorY[i] = _legFloorMeasured[i] ? groundY : floorY;
            _legFootY[i] = _legFloorY[i] - FootSinkMeters * scale;
            if (_legFloorMeasured[i])
                floorsMeasured++;

            // Up into the wood by the weld, but never nearer the top face than the weld itself:
            // "cannot emerge from the top" survives a measurement that lands anywhere.
            _legHeadY[i] = _legHeadMeasured[i]
                ? Mathf.Min(_legUndersideY[i] + weld, top.max.y - weld)
                : anchoredHeadY;

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

        // THE FIFTH PROBE, AND THE ONE REPORT 11 TURNS ON: THE CENTRE. The four above are at the
        // CORNERS, which is exactly where an apron makes the wood lowest; the player in
        // Tisch_Lücke.jpg is looking at the MIDDLE of the table, where there is no apron and the
        // underside can be a hundred millimetres higher. Building the panel from corner samples alone
        // would be ModBuild 200's mistake with a different reference face. One cast, once.
        _undersideCentreMeasured = TryMeasureUnderside(table, top.center.x, top.center.z, top, scale,
                                                       out float centreY);
        _undersideCentreY = _undersideCentreMeasured ? centreY : top.min.y;
        if (_undersideCentreMeasured)
        {
            _undersideProbesMeasured++;
            ceiling = Mathf.Max(ceiling, centreY);
        }
        _undersideCeilingY = ceiling;
        // A "shortest leg" of float.MaxValue is not a number the report may print. With no legs both
        // ends of the range are zero and every line that quotes them is guarded by wantLegs anyway.
        if (!wantLegs)
        {
            tallest = 0f;
            shortest = 0f;
        }

        if (wantLegs)
        {
            for (int i = 0; i < LegCount; i++)
            {
                if (_legHeight[i] >= MinLegHeightMeters * scale
                    && _legHeight[i] <= MaxLegHeightMeters * scale)
                    continue;
                Refuse($"NOT BUILT: leg {i} at ({_legX[i]:F2}, {_legZ[i]:F2}) derives a height of "
                       + $"{_legHeight[i] / scale:F3} m ({_legHeight[i]:F1} world units), outside the "
                       + $"plausible {MinLegHeightMeters:F2}..{MaxLegHeightMeters:F2} m window — its "
                       + $"head is y={_legHeadY[i]:F2} (the slab spans y={top.min.y:F2}.."
                       + $"{top.max.y:F2}) and its foot is y={_legFootY[i]:F2} (floor "
                       + $"y={_legFloorY[i]:F2}, {floorSource}). One of those two planes is not what "
                       + "this class thinks it is; NOTHING is built rather than a wrong prop — NOT "
                       + "EVEN THE UNDERSIDE PANEL, which would otherwise stand alone under a table "
                       + "whose geometry this class has just admitted it cannot read. No GameObject "
                       + "has been created at this point.");
                return;
            }
        }

        // THE FRAME. Origin at the TABLETOP's horizontal centre, unrotated. Vertically it is the ROOM
        // FLOOR PLANE when there are legs — the table's own AABB is the frame the legs belong to, and
        // anchoring the vertical to the floor plane is what makes "standing on the floor" structural
        // rather than arithmetic that can drift, so each leg's own foot and head are offsets from that
        // one plane and the four rows in the log and the four boxes in the mesh are the same numbers.
        // WITH NO LEGS THERE IS NO FLOOR PLANE TO ANCHOR TO and asking for one would refuse the whole
        // prop, so the origin drops to the slab's own bounds.min.y — which is the only plane the
        // underside panel is measured from anyway, and which exists in every style and under MR.
        float originY = wantLegs ? floorY : top.min.y;
        var origin = new Vector3(top.center.x, originY, top.center.z);
        _root = new GameObject(RootName);
        _root.transform.SetPositionAndRotation(origin, Quaternion.identity);

        _verts.Clear();
        _norms.Clear();
        _uvs.Clear();
        _tris.Clear();

        // Pick the SKIN before the UVs, because the UV decision depends on the texture that skin
        // carries (its wrap mode decides whether the grain may repeat at all).
        Material? skin = PickTableMaterial(table, out int skinIndex, out string materialSource);
        // THE REACH FED TO THE UV FITTER IS THE BIGGEST BOX IN THE MESH, NOT THE TALLEST LEG. In FIT
        // mode AdoptTableUvs coarsens the texel scale until a box's whole half-reach maps inside the
        // atlas page, and the UNDERSIDE PANEL is 1.5 m across — an order of magnitude past the
        // tallest leg. Passing `tallest` alone was survivable while the legs were always there to
        // dominate nothing and the clamp merely squashed the panel's mapping; with the legs gone in
        // Default, OffBlack and MR it would be zero, and a zero reach makes the fitter a no-op. The
        // slab's own footprint is the honest bound and it exists in every build.
        float uvReach = Mathf.Max(tallest,
                                  Mathf.Max(Mathf.Abs(top.size.x), Mathf.Abs(top.size.z)));
        AdoptTableUvs(table, skin, top, scale, uvReach, side, out string uvSource);

        if (wantLegs)
        {
            for (int i = 0; i < LegCount; i++)
            {
                var centre = new Vector3(_legX[i] - top.center.x,
                                         (_legFootY[i] + _legHeadY[i]) * 0.5f - originY,
                                         _legZ[i] - top.center.z);
                Box(centre, new Vector3(side, _legHeight[i], side));
            }
        }

        // THE UNDERSIDE, in the SAME mesh, the SAME material and the SAME draw call as the legs —
        // and, since report 11, the ONLY thing in that mesh whenever the legs are gated out.
        Underside(top, originY, scale, weld, anchoredHeadY, side, out string undersideSource);

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

        // THE PROP GOES ON THE TABLE'S OWN LAYER, and that RESOLVES ModBuild 198's stated open risk
        // rather than measuring it again. See ChooseLayer. It matters to the UNDERSIDE for a second
        // reason report 11 adds: under mixed reality the panel has to be drawn by whatever draws the
        // tabletop, or the table would have a bottom in three presentations and not in the fourth.
        int layer = ChooseLayer(table, out string layerSource);
        SetLayerRecursive(_root.transform, layer);

        _builtAgainstParchment = parchment;
        _builtAgainstTable = table;
        _builtWithLegs = wantLegs;
        _lastRefusal = "";
        Report(style, mixedReality, wantLegs, haveFloor, seat, parch, table, top, floorY, side,
               cornerX, cornerZ, scale, weld, headInset, anchoredHeadY, slabThickness, tallest,
               shortest, headsMeasured, floorsMeasured, gapFree, skinIndex, layer, floorSource,
               materialSource, uvSource, layerSource, candidates, undersideSource, shadingSource);
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
    /// <see cref="UndersideMinThicknessMeters"/>.</para>
    ///
    /// <para>REPORT 11: THE BOX SPANS THE HOLLOW, AND THAT IS THE WHOLE FIX. ModBuild 201 gave the
    /// box an AUTHORED 12 mm thickness and sat it on <c>bounds.min.y</c>. Both numbers are read off
    /// the same misunderstanding this class already paid for once in ModBuild 200:
    /// <c>bounds.min.y</c> is the LOWEST POINT ANYWHERE in the slab's mesh — the bottom edge of its
    /// apron — and not the height of the wood over the middle of the table. So the 12 mm board closed
    /// the bottom 13 mm of a 148 mm slab and left an OPEN RING above it, and a ray entering the
    /// <see cref="UndersideEdgeInsetMeters"/> rim slot at a grazing angle went up past the little
    /// board, into the open shell, through the top face from behind (culled) and out at the unlit
    /// parchment. That ring is the bright horizontal slit in
    /// <c>.planning/debug/Tisch_Lücke.jpg</c>.</para>
    ///
    /// <para>THE REPAIR IS NOT TO PUSH A THIN PLATE UP. He asked for that ("die Unterseiten-Platte
    /// ein bisschen nach oben schieben") and it is the wrong shape of fix: raising the plate moves
    /// the ring rather than closing it, and raising it far enough to close it would put the plate's
    /// BOTTOM face above the apron — i.e. the ring would simply reopen underneath, or, pushed
    /// further, the panel would stand proud of the board and thicken a table he has already accepted.
    /// Instead the BOTTOM FACE STAYS EXACTLY WHERE IT IS and the TOP FACE goes up:</para>
    /// <list type="bullet">
    ///   <item>bottom = <c>bounds.min.y + </c><see cref="UndersideClearanceMeters"/>. Unchanged, and
    ///   unchanged for the reason that constant already gives: at or above the mesh's lowest point
    ///   the panel can never stand proud of the board.</item>
    ///   <item>top = the MEASURED underside plus the same <c>weld</c> the leg heads use, capped at
    ///   <paramref name="anchoredHeadY"/> so it can no more emerge from the tabletop than a leg can.
    ///   The ceiling is the MAXIMUM over <see cref="UndersideProbeCount"/> samples — four corners and
    ///   the centre — because a box top above the local wood is buried and invisible while a box top
    ///   below it is the ring.</item>
    ///   <item>where nothing USEFUL answered, the top falls back to <paramref name="anchoredHeadY"/>,
    ///   the very plane the four LEG HEADS hang from when their own probes fail. That is deliberately
    ///   NOT ModBuild 201's 12 mm: 12 mm is the defect, and shipping it as the fallback would
    ///   reproduce the photograph on every table this class cannot probe.
    ///   <see cref="HeadInsetBelowTopFaceMeters"/> already carries the argument that that plane is
    ///   inside the wood — 20 mm below the top face, capped at
    ///   <see cref="HeadInsetMaxThicknessFraction"/> of the measured thickness — and this panel is
    ///   welded into it on exactly the same terms. The report says MEASURED or FALLBACK in words, so
    ///   the two can never be confused.</item>
    /// </list>
    ///
    /// <para>"NOTHING USEFUL" IS NOT THE SAME AS "NO HIT", and that distinction is the trap this
    /// whole repair would otherwise fall into. <see cref="TryMeasureUnderside"/> takes the LOWEST
    /// qualifying hit going up. If the tabletop's collider is a plain <c>BoxCollider</c> spanning its
    /// AABB — the ordinary case for a prop nobody expects to be walked on — then all five casts hit,
    /// all five return <c>bounds.min.y</c>, and a naive reading would rebuild ModBuild 201's 12 mm
    /// board and stamp MEASURED on it. So a ceiling that is indistinguishable from <c>bounds.min.y</c>
    /// is treated as NO ceiling. That is safe in the other direction too: if the wood genuinely does
    /// end at <c>bounds.min.y</c> the slab is solid, and a panel built up to the head plane is then
    /// buried inside it — buried is invisible.</para>
    ///
    /// <para>THE CAP IS THE LEG-HEAD PLANE AND NOT <c>bounds.max.y - weld</c>, BECAUSE OF THE TOP
    /// CHAMFER. The slab's side face runs flush and vertical from a top chamfer down to a bottom
    /// moulding (<c>.planning/debug/tisch_falsches_licht.jpg</c>, near corner). Above where that
    /// chamfer starts the surface recedes INWARD from the AABB, and this panel's wall stands only
    /// <see cref="UndersideEdgeInsetMeters"/> = 2 mm inside the AABB — a tenth of a leg's 20 mm — so
    /// a panel taken up to 5 mm under the tabletop would stand OUTSIDE the bevel and show a rim from
    /// above. <see cref="HeadInsetBelowTopFaceMeters"/> is this class's existing answer to exactly
    /// that question ("20 mm down and 20 mm in is a 45 deg chamfer's worth of cover"), so the panel
    /// stops at the same plane rather than inventing a second number. The 15 mm of extra closure that
    /// buys is not worth a visible rim, and it changes nothing: the wood is far below that plane.</para>
    ///
    /// <para>THE INVARIANT, AND THE ONE RESIDUAL. NO RAY ENTERING FROM BELOW CAN REACH THE
    /// PARCHMENT, except through the rim slot, and the slot is now a full-height ring rather than a
    /// door. A ray that enters between the panel's outer wall and the slab's side plane must stay
    /// inside a <see cref="UndersideEdgeInsetMeters"/>-wide gap for the panel's whole thickness, so
    /// its slope from vertical is bounded by <c>atan(inset / thickness)</c>: about 0.9 deg at the
    /// measured table against 8.8 deg before, over a ring that then opened into the entire
    /// cross-section. To look up such a ray an eye 0.5 m below the rim must be within 8 mm of the
    /// vertical line through it and would see a 2 mm sliver. That angle is COMPUTED AND PRINTED every
    /// build; <see cref="UndersideEdgeInsetMeters"/> documents the two ways to close it exactly and
    /// why neither is worth its cost yet.</para>
    ///
    /// <para>IT DOES NOT TOUCH THE PARCHMENT, AND THAT IS DELIBERATE. ModBuild 199 was burnt treating
    /// the decal as the table; the parchment is still excluded from <see cref="TryFindTable"/> by
    /// IDENTITY and by the <see cref="MinTableThicknessMeters"/> board test, and this box is measured
    /// from the slab's bounds only. It is not welded to, parented to, or offset from the map.</para>
    ///
    /// <para>THE LEGS PASS THROUGH IT, which is what makes them read as joined. Each leg's head is at
    /// or near <paramref name="anchoredHeadY"/>, and the leg's shaft pierces the bottom face; the
    /// part inside the box is enclosed by opaque geometry and the part above it is inside the slab.
    /// No hole is cut and none is needed. Since report 11 the box reaches up to the same wood the
    /// heads are welded into, so the shaft is now enclosed for nearly its whole buried length rather
    /// than for 12 mm of it — which changes nothing visible and is worth saying only because it means
    /// the two parts still cannot leave a seam between them.</para>
    ///
    /// <para>COST: <see cref="UndersideTriangleCount"/> triangles, no collider, no second material and
    /// no second renderer — and exactly the same count now that the box is ten times taller, because
    /// height is free.</para>
    /// </summary>
    private void Underside(Bounds top, float originY, float scale, float weld, float anchoredHeadY,
                           float legSide, out string source)
    {
        float inset = UndersideEdgeInsetMeters * scale;
        float clearance = UndersideClearanceMeters * scale;
        float bottom = top.min.y + clearance;

        // THE CAP FIRST, so nothing below can argue past it, AND IT IS THE LEG-HEAD PLANE RATHER
        // THAN bounds.max.y - weld. That choice is the top chamfer, and it is the one place where
        // this panel's 2 mm rim inset is WEAKER than a leg's 20 mm one. The slab's side face runs
        // flush and vertical from a top chamfer down to a bottom moulding
        // (.planning/debug/tisch_falsches_licht.jpg, near corner); above the chamfer's start the
        // surface recedes INWARD from the AABB, so a wall only 2 mm inside the AABB would stand
        // OUTSIDE it there. HeadInsetBelowTopFaceMeters already commits this class to a number for
        // exactly that question — "20 mm down and 20 mm in is a 45 deg chamfer's worth of cover" —
        // so the panel stops at the same plane the leg heads do rather than inventing a second one.
        // A box top 5 mm under the tabletop would be more closed and would risk poking through the
        // bevel; the extra 15 mm of closure is not worth a visible rim.
        float capY = Mathf.Max(anchoredHeadY, bottom + clearance);
        // THE FLOOR UNDER THE THICKNESS: ModBuild 201's 12 mm survives as a MINIMUM, for a slab that
        // genuinely is that shallow. It is itself capped, so a thin board narrows the panel rather
        // than pushing it through the top.
        float minTopY = Mathf.Min(bottom + UndersideMinThicknessMeters * scale, capY);

        // THE TOP FACE. Welded up into the MEASURED underside; otherwise the leg-head anchor plane,
        // which HeadInsetBelowTopFaceMeters already argues is inside the wood. NOT 12 mm — 12 mm is
        // the defect in Tisch_Lücke.jpg, and shipping it as the fallback would reproduce the
        // photograph on any table this class cannot probe.
        //
        // A PROBE THAT LANDS ON bounds.min.y IS NOT A MEASUREMENT OF THE WOOD, and this is the trap
        // the whole repair would otherwise fall into. TryMeasureUnderside takes the LOWEST qualifying
        // hit going up; if the tabletop's collider is a plain BoxCollider spanning its AABB — which
        // is the ordinary case for a prop nobody expects to be walked on — every one of the
        // UndersideProbeCount casts answers with the box's own floor, i.e. with bounds.min.y, and the
        // panel would be rebuilt at ModBuild 201's 12 mm with a "MEASURED" label on it. A reading
        // indistinguishable from the AABB floor is therefore treated as NO reading, and the fallback
        // takes over. That is safe in the other direction too: if the wood really does end at
        // bounds.min.y the slab is solid, the panel is then buried inside it, and buried is invisible.
        //
        // ONE COPLANAR PAIR IS ACCEPTED HERE, KNOWINGLY. In the fallback case the panel's top face
        // lands on anchoredHeadY, and a LEG whose own probe also failed has its top CAP on that same
        // plane — two coincident up-facing quads, the classic z-fight. It is accepted because it
        // cannot be observed: both are inside the slab's AABB, 20 mm under a top face the game draws
        // opaque, and there is no eye position from which either is visible. Moving the panel a
        // millimetre off the plane to "fix" it would spend a millimetre of closure on an artefact
        // nobody can see.
        bool anyProbe = _undersideProbesMeasured > 0;
        bool informative = anyProbe && _undersideCeilingY > top.min.y + clearance;
        float wanted = informative ? _undersideCeilingY + weld : capY;
        float topY = Mathf.Clamp(wanted, minTopY, capY);
        float thickness = topY - bottom;

        // A slab so small that the rim inset would cross is clamped to the leg section rather than
        // inverted; that is ugly and bounded, and TryFindTable's own tests make it unreachable.
        float sizeX = Mathf.Max(Mathf.Abs(top.size.x) - 2f * inset, legSide);
        float sizeZ = Mathf.Max(Mathf.Abs(top.size.z) - 2f * inset, legSide);
        var centre = new Vector3(0f, bottom + thickness * 0.5f - originY, 0f);
        Box(centre, new Vector3(sizeX, thickness, sizeZ));

        _undersideBottomY = bottom;
        _undersideTopY = topY;
        _undersideCeilingInformative = informative;
        // THE RESIDUAL: open hollow left between the panel's top face and the underside reference
        // above it. This is the ring in Tisch_Lücke.jpg, measured, and it must read 0.0. Where a
        // probe answered the reference is that measurement; where none did it is bounds.min.y, a hard
        // LOWER BOUND on where the wood can possibly be — the same convention the per-leg rows use,
        // and the report says so in words rather than letting a 0.0 stand on its own.
        float reference = informative ? _undersideCeilingY : top.min.y;
        _undersideOpenMm = Mathf.Max(0f, reference - topY) / scale * 1000f;
        // THE ONE PATH LEFT, AS AN ANGLE: a ray entering the rim slot from below reaches the
        // parchment only if it stays inside a gap of `inset` for the panel's whole thickness.
        _undersideLeakDegrees = Mathf.Atan2(inset, Mathf.Max(thickness, 1e-6f)) * Mathf.Rad2Deg;

        source = $"one CLOSED box of {UndersideTriangleCount} triangles, {sizeX / scale:F3} x "
                 + $"{thickness / scale:F3} x {sizeZ / scale:F3} m ({sizeX:F1} x {thickness:F1} x "
                 + $"{sizeZ:F1} world units). IT SPANS THE HOLLOW: bottom face y={bottom:F2}, top face "
                 + $"y={topY:F2}. The bottom is {UndersideClearanceMeters * 1000f:F0} mm ABOVE the "
                 + $"slab's own bounds.min.y (y={top.min.y:F2}) — unchanged, so the panel can never "
                 + "stand proud of the board and the table cannot look thicker than the one the user "
                 + $"accepted — and the top is {(top.max.y - topY) / scale * 1000f:F1} mm below the "
                 + $"slab's top face (y={top.max.y:F2}), with the rim "
                 + $"{UndersideEdgeInsetMeters * 1000f:F0} mm inside the slab's side faces. "
                 + $"THE CEILING IS {(informative ? "MEASURED" : "A FALLBACK")}: "
                 + (informative
                    ? $"{_undersideProbesMeasured} of {UndersideProbeCount} probes answered "
                      + $"({LegCount} corners + THE CENTRE, which reads "
                      + $"{(_undersideCentreMeasured ? $"y={_undersideCentreY:F2}, i.e. {(_undersideCentreY - top.min.y) / scale * 1000f:F1} mm above bounds.min.y" : "NOTHING — the centre probe found no collider")}"
                      + $"), and the HIGHEST sample is y={_undersideCeilingY:F2}, "
                      + $"{(_undersideCeilingY - top.min.y) / scale * 1000f:F1} mm above bounds.min.y. "
                      + "MAX and not min on purpose: a box top above the local wood is buried inside "
                      + "the board and invisible from every eye, a box top below it is the open ring"
                    : (anyProbe
                       ? $"{_undersideProbesMeasured} of {UndersideProbeCount} probes ANSWERED but "
                         + $"their highest sample is y={_undersideCeilingY:F2}, only "
                         + $"{(_undersideCeilingY - top.min.y) / scale * 1000f:F1} mm above "
                         + $"bounds.min.y (y={top.min.y:F2}) — INDISTINGUISHABLE FROM THE AABB FLOOR, "
                         + "which is exactly what a plain BoxCollider spanning the slab's bounds "
                         + "returns and which says nothing at all about where the wood is. A reading "
                         + "like that is treated as NO reading, because believing it would rebuild "
                         + "ModBuild 201's 12 mm board and label it MEASURED"
                       : $"NONE of the {UndersideProbeCount} probes answered — the tabletop carries "
                         + "no collider this class may believe")
                      + $". So the top face is the LEG-HEAD ANCHOR plane y={anchoredHeadY:F2}, "
                      + $"{HeadInsetBelowTopFaceMeters * 1000f:F0} mm below the slab's top face, which "
                      + "HeadInsetBelowTopFaceMeters already argues is inside the wood and clear of "
                      + "the top chamfer. It is deliberately NOT ModBuild 201's "
                      + $"{UndersideMinThicknessMeters * 1000f:F0} mm board: that IS the defect in "
                      + "Tisch_Lücke.jpg, and shipping it as the fallback would reproduce the "
                      + "photograph on every table this class cannot probe")
                 + $". RESIDUAL OPEN HEIGHT {_undersideOpenMm:F1} mm"
                 + (_undersideOpenMm <= GapFreeMillimetres ? "" : "  <-- THE RING IS BACK")
                 + (informative
                    ? " against the measured ceiling"
                    : " against bounds.min.y, which is a LOWER BOUND on the underside anywhere, so "
                      + "this figure proves the worst case and not the real one")
                 + ". EVERY POINT OF IT IS INSIDE THE SLAB'S OWN AABB, so it cannot be seen from any "
                 + "eye at or above the tabletop (the slab's drawn top and side faces are in the way), "
                 + "it cannot change the silhouette the user accepted, and it cannot z-fight: it "
                 + "shares no plane with the slab, with the parchment or with a leg (a leg's outer "
                 + $"face stands {EdgeInsetMeters * 1000f:F0} mm in, this rim "
                 + $"{UndersideEdgeInsetMeters * 1000f:F0} mm). It is a BOX and not a quad because a "
                 + "bare quad leaves an open slot round the rim and a ray up through that slot lands "
                 + "on the UNLIT parchment. THE ONE PATH LEFT is that rim slot, and it is now a "
                 + $"FULL-HEIGHT ring: a ray must stay inside {UndersideEdgeInsetMeters * 1000f:F0} mm "
                 + $"for {thickness / scale * 1000f:F0} mm of climb, i.e. within "
                 + $"{_undersideLeakDegrees:F2} deg of vertical, so an eye 0.5 m below the rim must be "
                 + $"within {500f * Mathf.Tan(_undersideLeakDegrees * Mathf.Deg2Rad):F1} mm of the "
                 + "vertical line through it to see a 2 mm sliver. ModBuild 201's 12 mm board gave "
                 + $"{Mathf.Atan2(inset, UndersideMinThicknessMeters * scale) * Mathf.Rad2Deg:F1} deg "
                 + "over a ring that then opened into the WHOLE cross-section, which is why the user "
                 + "photographed a slit. THE PARCHMENT IS NOT TOUCHED: this is measured from the "
                 + "tabletop's bounds and its own collider only, and the map is still excluded by "
                 + $"identity and by the {MinTableThicknessMeters * 1000f:F0} mm board test. Where "
                 + "legs are built they PIERCE the bottom face and are enclosed above it, which is "
                 + "why no hole is cut";
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
    private void Report(SkyStyle style, bool mixedReality, bool builtWithLegs, bool haveFloor,
                        MapRoomSeat.Seat seat, Bounds parch,
                        MeshRenderer table, Bounds top, float floorY, float side, float cornerX,
                        float cornerZ, float scale, float weld, float headInset, float anchoredHeadY,
                        float slabThickness, float tallest, float shortest, int headsMeasured,
                        int floorsMeasured, int gapFree, int skinIndex, int layer, string floorSource,
                        string materialSource, string uvSource, string layerSource, string tableSurvey,
                        string undersideSource, string shadingSource)
    {
        // THE WINDING GATE, ON THE FINISHED SOLID. Cheap (12 or 60 triangles) and it runs once.
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
        float playerAboveRoomFloor = haveFloor ? (seat.FloorPosition.y - floorY) / scale : 0f;

        VRLog.Info(Scope,
            $"MAP TABLE {(builtWithLegs ? "LEGS" : "UNDERSIDE")} built: "
            + (builtWithLegs
               ? $"{LegCount} leg(s), one at each CORNER of the game's own tabletop, PLUS an UNDERSIDE "
                 + "panel"
               : "the UNDERSIDE PANEL ALONE — no legs this build, and that is the gate and not a "
                 + "failure")
            + $", {TrianglesFor(builtWithLegs)} triangles "
            + $"({(builtWithLegs ? $"{LegTriangleCount} legs + " : "0 legs + ")}"
            + $"{UndersideTriangleCount} underside) in ONE combined mesh on ONE MeshRenderer with ONE "
            + "material = 1 DRAW CALL, no collider, no Update, world-fixed (nothing here follows the "
            + "head).\n"
            + $"  parts     : LEGS {(builtWithLegs ? "BUILT" : "NOT built")}, UNDERSIDE BUILT. Those "
            + "are two gates since user report 11 and they can disagree; every count on this line and "
            + "every row below is DERIVED from which parts were actually made, so a build with no "
            + $"legs cannot print {LegTriangleCount} leg triangles.\n"
            + $"  gate      : {DescribeGate(style, mixedReality, true, builtWithLegs)} The gate is "
            + "re-evaluated EVERY FRAME (two field reads), so switching [Sky] Style at runtime builds "
            + "or tears the LEGS down on the NEXT FRAME, not on the next room entry; the underside "
            + "half never closes, so a style change with a standing prop is a rebuild of the same "
            + "mesh with or without 48 triangles in it.\n"
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
            + (builtWithLegs
               ? $"  leg       : {LegSideMeters:F3} x {LegSideMeters:F3} m section, "
                 + $"{shortest / scale:F3}..{tallest / scale:F3} m tall ({shortest:F1}..{tallest:F1} "
                 + "world units) — EACH LEG HAS ITS OWN LENGTH, see the four rows below. Corners at "
                 + $"+/-{cornerX / scale:F3} x +/-{cornerZ / scale:F3} m (+/-{cornerX:F1} x "
                 + $"+/-{cornerZ:F1} world units) from the tabletop's centre, i.e. its outer face "
                 + $"{EdgeInsetMeters * 1000f:F0} mm inside the top's edge; section {side:F1} world "
                 + "units.\n"
               : "  leg       : NONE. The four corner positions were still computed and still PROBED "
                 + "— the underside panel is built from those probes — but no leg box was emitted, no "
                 + "floor was looked for and no leg height was derived. See the 'gate' line for which "
                 + "style and MR state decided that.\n")
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
            + $"head is capped at {weld / scale * 1000f:F1} mm below the top face as well."
            + (builtWithLegs
               ? "\n"
               : " (NO LEGS THIS BUILD — this line describes the plane that WOULD have been used, and "
                 + "it is also the underside panel's fallback ceiling, so it is not idle.)\n")
            + $"  per leg   : {DescribeLegs(top, floorY, scale, builtWithLegs, haveFloor, headsMeasured, floorsMeasured, gapFree)}\n"
            + $"  underside : {undersideSource}.\n"
            + $"  UNDERSIDE VERDICT, ONE LINE: parts built = "
            + $"{(builtWithLegs ? "LEGS + UNDERSIDE" : "UNDERSIDE ONLY")}; decided by [Sky] Style "
            + $"{style} and mixed reality {(mixedReality ? "ON" : "off")} "
            + $"(legs need a floor and a bundled 3D room, the underside needs only the slab); ceiling "
            + $"{(_undersideCeilingInformative ? $"MEASURED y={_undersideCeilingY:F2} from {_undersideProbesMeasured} of {UndersideProbeCount} probes (centre probe {(_undersideCentreMeasured ? $"y={_undersideCentreY:F2}" : "no answer")})" : $"FALLBACK y={anchoredHeadY:F2} — {(_undersideProbesMeasured > 0 ? $"{_undersideProbesMeasured} of {UndersideProbeCount} probes answered but only with the AABB floor, which is no answer at all" : $"none of the {UndersideProbeCount} probes answered")}")}"
            + $", AABB floor y={top.min.y:F2}; box bottom y={_undersideBottomY:F2}, top "
            + $"y={_undersideTopY:F2}, thickness "
            + $"{(_undersideTopY - _undersideBottomY) / scale * 1000f:F1} mm; RESIDUAL OPEN HEIGHT "
            + $"{_undersideOpenMm:F1} mm"
            + (_undersideOpenMm <= GapFreeMillimetres
               ? " — ZERO, which is the invariant: no ray entering from below can reach the parchment "
                 + "except inside a "
                 + $"{_undersideLeakDegrees:F2} deg cone up the {UndersideEdgeInsetMeters * 1000f:F0} "
                 + "mm rim slot."
               : "  <-- THE RING IS BACK: this is the fault in Tisch_Lücke.jpg and the number is the "
                 + "height of the open hollow above the panel.")
            + "\n"
            + "  the reports: (a) ModBuild 201, \"the table has no real underside — you can see "
            + "through it from below, and you also see the map lying on the table\" "
            + "(.planning/debug/tisch_unten.jpg). The game's slab draws a top face and four sides and "
            + "NOTHING underneath, and what shows through is the parchment, which this mod draws "
            + "UNLIT — an unlit surface is at full brightness from either side, which is why the map "
            + "reads as a lit panel hanging inside the table. (b) REPORT 11, \"Die Unterseite des "
            + "Tischs auch bei den anderen Umgebungen (wie Mixed Reality, keine und Default) mit "
            + "einbauen ... Hierbei die Unterseiten-Platte ein bisschen nach oben schieben - aktuell "
            + "ist da eine sichtbare Lücke\" (.planning/debug/Tisch_Lücke.jpg) — two faults: the "
            + "underside rode on the LEGS' gate, and the 12 mm plate sat at the bottom of a "
            + $"{slabThickness / scale * 1000f:F0} mm hollow with an open ring above it. DISPROOF: if "
            + "you can still see through the table from below, either this box is not being built "
            + $"(the triangle count above would read {(builtWithLegs ? LegTriangleCount : 0)} and not "
            + $"{TrianglesFor(builtWithLegs)}) or the RESIDUAL OPEN HEIGHT above is not 0.0 — and that "
            + "number names the fault in millimetres instead of needing a second photograph. If it "
            + "reads 0.0 and the slit is still there, the leak is the rim slot and the cone angle "
            + "above is the number to argue from: shrink UndersideEdgeInsetMeters, or give the bottom "
            + "face a flange out to the full AABB footprint (12 more triangles, exact closure, and it "
            + "gives up the guarantee that nothing can poke out of a recessed side face). If a rim of "
            + "the underside is visible from ABOVE, the slab's side face is recessed from its own AABB "
            + $"by more than {UndersideEdgeInsetMeters * 1000f:F0} mm and THAT is the number to raise. "
            + "If the table now looks THICKER than the one the user accepted, the box's BOTTOM face "
            + "moved, and it cannot have: it is bounds.min.y + "
            + $"{UndersideClearanceMeters * 1000f:F0} mm and report 11 did not touch it.\n"
            + $"  material  : {materialSource}.\n"
            + $"{DescribeMaterials(table, "the TABLETOP renderer:", "              ")}\n"
            + $"              the prop wears mat[{skinIndex}] of that list, the same object — which "
            + "is also why the underside panel needs no special handling under MIXED REALITY: "
            + "whatever the MR treatment does to the tabletop it does to this panel, in the same draw "
            + "call, and this class writes no _Cull, no blend state and no material anywhere.\n"
            + $"  texture   : {uvSource}. At that scale the "
            + (builtWithLegs
               ? $"{tallest / scale:F2} m leg carries {tallest / _uvWorldPerV:F2} UV unit(s) of grain "
                 + $"down its length and {side / _uvWorldPerU:F2} across its face"
               : $"underside panel carries {Mathf.Abs(top.size.x) / _uvWorldPerU:F2} x "
                 + $"{Mathf.Abs(top.size.z) / _uvWorldPerV:F2} UV unit(s) of grain across itself")
            + "; mapping mode "
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
            + (haveFloor
               ? $"  the floor : each foot is cut {FootSinkMeters * 1000f:F0} mm "
                 + $"({FootSinkMeters * scale:F1} world units) under the ground READ AT ITS OWN CORNER "
                 + $"— the four values are in the per-leg rows above, and {floorsMeasured} of "
                 + $"{LegCount} were probed against the room's own colliders rather than taken from "
                 + $"the plane. SOURCE: {floorSource}. The player's own tracking floor is "
                 + $"y={seat.FloorPosition.y:F2}, i.e. {playerAboveRoomFloor * 1000f:F0} mm ABOVE the "
                 + "room floor — the two planes this room has always disagreed on, and the reason the "
                 + "legs are stood on the ROOM's one: they are meant to be seen reaching the ground. "
                 + "RESIDUAL: the plane is exact, but both rooms have a gentle floor relief outside "
                 + "their dead-flat play disc (BuildEnvironmentRooms: ForestY is identically 0 inside "
                 + "r=1.7 authored m and ramps over 1.7..4.6; CellarFloorY is +/-6 mm authored), which "
                 + "at this table's corner radius is about +/-19 mm (swamp) and +/-5 mm (cellar) "
                 + "perceived. Where the probe answered, that relief is MEASURED and not swallowed; "
                 + $"where it did not, the {FootSinkMeters * 1000f:F0} mm sink spends the error "
                 + "downward on purpose: a sunk foot reads as soft ground, a floating one as a bug.\n"
               : $"  the floor : {floorSource} The prop's vertical ORIGIN is therefore the slab's own "
                 + $"bounds.min.y (y={top.min.y:F2}) rather than a room floor plane, so nothing here "
                 + "depends on SkyAlternative having placed a room — which is exactly why the "
                 + "underside now exists in Default, in OffBlack and under mixed reality. Before user "
                 + "report 11 a missing room root refused the WHOLE build, and that refusal is the "
                 + "first half of his report.\n")
            + $"  heights   : "
            + (haveFloor
               ? $"tabletop top {(top.max.y - floorY) / scale:F3} m above the room floor and "
               : "no room floor this build, so: tabletop top ")
            + $"{(top.max.y - seat.FloorPosition.y) / scale:F3} m above the player's; parchment "
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
            + (builtWithLegs
               ? ""
               : "  NOTE      : the lines above that talk about legs — 'the head', 'the floor', the "
                 + "per-leg rows' relief and length columns — describe a part this build did NOT make. "
                 + "They are kept because the underside panel is measured with the SAME probes at the "
                 + "SAME four corners and its fallback ceiling is the leg-head plane, so the numbers "
                 + "are load-bearing even with no leg in the mesh. Nothing here claims a leg exists: "
                 + $"the mesh line reads {TrianglesFor(false)} triangles, not "
                 + $"{TrianglesFor(true)}.\n")
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
            + "so raising it moves the head DOWN and lowering it moves the head UP into the board). "
            + "AND IF THE TABLE IS STILL SEE-THROUGH FROM BELOW, none of that applies: go to the "
            + "UNDERSIDE VERDICT line, which names the parts built, the style and MR state that "
            + "decided it, the measured-versus-fallback ceiling, the box's bottom and top Y and the "
            + "residual open height in millimetres. Exactly one of those five is wrong.");
    }

    /// <summary>
    /// THE FOUR CORNER ROWS, and the counts that keep "no gap" from looking like "never measured".
    /// For each corner: the floor Y under it, the slab underside reference above it, the resulting
    /// length, and the residual gap at the head IN MILLIMETRES. All four gap figures must read
    /// 0.0 mm; the trailing count says how many of the four actually do, so a build in which the loop
    /// never ran cannot print the same thing as a build in which it ran and passed.
    ///
    /// <para>SINCE REPORT 11 THE ROWS ARE PRINTED EVEN WHEN NO LEG WAS BUILT, because the four
    /// corners are still PROBED — the underside panel's ceiling is the maximum over those probes plus
    /// the centre one — and a probe whose result is used must be visible in the log. What changes is
    /// what the row is allowed to claim: with no legs there is no floor, no foot, no head and no
    /// length, and printing zeroes in those columns would be a lie dressed as a measurement. The row
    /// says PROBE POINT instead and carries only the underside reading.</para>
    /// </summary>
    private string DescribeLegs(Bounds top, float planeY, float scale, bool builtWithLegs,
                                bool haveFloor, int headsMeasured, int floorsMeasured, int gapFree)
    {
        var sb = new StringBuilder(640);
        if (builtWithLegs)
        {
            sb.Append($"{gapFree} of {LegCount} leg(s) read ZERO gap at the head "
                      + $"(threshold {GapFreeMillimetres:F2} mm); {headsMeasured} of {LegCount} took a "
                      + "PROBED slab underside and the rest the top-face anchor, and "
                      + $"{floorsMeasured} of {LegCount} took a PROBED ground and the rest the room's "
                      + $"floor plane y={planeY:F2}. {LegCount} comparison(s) were made, so a silent "
                      + "skip cannot look like a pass.");
        }
        else
        {
            sb.Append($"NO LEGS were built this build, so there is no gap column to read: the "
                      + $"{LegCount} rows below are PROBE POINTS, not legs. {headsMeasured} of "
                      + $"{LegCount} corner probes answered and they feed the UNDERSIDE panel's "
                      + "ceiling together with the centre probe — see the UNDERSIDE VERDICT line. No "
                      + "floor was looked for"
                      + (haveFloor ? "" : " and no room root was found or needed")
                      + ", so the floor, foot, head and length columns are omitted rather than "
                      + "printed as zeroes.");
        }
        for (int i = 0; i < LegCount; i++)
        {
            string cx = (i & 1) == 0 ? "-x" : "+x";
            string cz = (i & 2) == 0 ? "-z" : "+z";
            if (!builtWithLegs)
            {
                sb.Append($"\n              probe {i} ({cx},{cz}) at ({_legX[i]:F2}, {_legZ[i]:F2}): "
                          + $"underside y={_legUndersideY[i]:F2} "
                          + $"({(_legHeadMeasured[i] ? $"PROBED, {(_legUndersideY[i] - top.min.y) / scale * 1000f:F1} mm above the slab AABB floor" : "NO ANSWER — the AABB floor y=" + top.min.y.ToString("F2") + " stands in, a LOWER BOUND")})");
                continue;
            }
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
        // THE FIFTH ROW, AND IT IS PRINTED IN BOTH MODES: the centre probe is the one report 11 adds
        // and the one the photographed fault is about. A row that only appears in one mode is a row
        // a reader learns to stop looking for.
        sb.Append($"\n              probe C (centre) at ({top.center.x:F2}, {top.center.z:F2}): "
                  + $"underside y={_undersideCentreY:F2} "
                  + (_undersideCentreMeasured
                     ? $"(PROBED, {(_undersideCentreY - top.min.y) / scale * 1000f:F1} mm above the "
                       + $"slab AABB floor y={top.min.y:F2} and "
                       + $"{(top.max.y - _undersideCentreY) / scale * 1000f:F1} mm below its top face)"
                     : $"(NO ANSWER — the AABB floor y={top.min.y:F2} stands in, a LOWER BOUND)")
                  + ". THIS IS THE HEIGHT OF THE VISIBLE UNDERSIDE OVER THE MIDDLE OF THE TABLE, which "
                  + "is what the player in Tisch_Lücke.jpg is looking at and which no probe covered "
                  + "before report 11. If it reads far above the AABB floor, the slab really is a "
                  + "hollow shell with an apron and the panel had to grow to close it.");
        return sb.ToString();
    }
}
