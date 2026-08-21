using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// The location icons and the party token, re-drawn for the HEAD camera.
///
/// <para>WHY THEY ARE NOT SIMPLY THERE. Every map location's icon is a
/// <c>ThreeEyedGames.Decalicious</c> deferred decal — it draws in the deferred G-buffer pass and
/// contributes NOTHING to a forward camera, which is what the mod's head camera is. So a map room
/// with a working parchment and no icon layer is a blank sheet of paper. The flat path already
/// solved this by re-drawing each decal as a textured quad into a <c>CommandBuffer</c>
/// (<c>FlatScreenStereo.3.Map.DrawMapIcons</c>); the work here is to hang the same idea on the
/// head camera instead of a private capture camera.</para>
///
/// <para>TWO DELIBERATE DIFFERENCES FROM THE FLAT PATH, both because the target camera is now the
/// one the player looks through:</para>
/// <list type="number">
///   <item>NO DEPTH CLEAR. The flat path clears depth at <c>AfterForwardAlpha</c> so a cloud
///   particle can never reject an icon. On the head camera that same clear would throw away the
///   depth of everything the player is standing in — hands, panels, the parchment itself — for
///   whatever draws afterwards. It is also unnecessary: the icons are lifted clear of the
///   parchment, so ordinary <c>ZTest LEqual</c> already puts them where they belong.</item>
///   <item>THE QUAD FOOTPRINT COMES FROM THE DECAL TRANSFORM, not from the renderer's world AABB.
///   Decalicious draws a decal as a unit cube through <c>transform.localToWorldMatrix</c>, so the
///   AUTHORED footprint is <c>lossyScale.x</c> × <c>lossyScale.z</c>. Using the world AABB and then
///   re-applying the decal's 90° yaw transposes non-square icons — that is the "vereinzelt Icons
///   gequetscht" report, and its fix is copied here as a value rather than re-derived.</item>
/// </list>
///
/// <para>WHERE THE BUFFER HANGS, AND WHY IT IS THE WHOLE MECHANISM (user report against ModBuild 188:
/// "Allerdings sehe ich die Symbole auf der map durch die mouseovers durch. Wie immer soll alles
/// die Perspektive respektieren"). Through ModBuild 188 this buffer hung at
/// <c>CameraEvent.AfterForwardAlpha</c> — AFTER every transparent thing the forward camera draws,
/// which includes every floated mod window. The mod's floated windows are world-space uGUI panels
/// that deliberately write NO depth (the rule <c>CanvasConversion.8.Order.cs</c> states in its own
/// PANEL DRAW ORDER line: "no panel writes depth, so every transparent pixel shows what is behind
/// it"), and that rule is not negotiable — it is what makes a translucent menu pixel honest. A
/// surface that writes no depth can only ever cover something by being PAINTED AFTER it. So with
/// the icons painted last, no panel could ever cover them, at any pose, in any frame. That is the
/// report, in full, and it is a fact about the ORDER, not about the material.</para>
///
/// <para>Two things follow, and both are load-bearing:</para>
/// <list type="bullet">
///   <item>THE FIX IS THE EVENT. <see cref="IconEvent"/> is now
///   <c>CameraEvent.BeforeForwardAlpha</c>: after all forward-OPAQUE geometry (so the parchment,
///   queue 2000 with the bundled MapUnlit forward pass, is already in colour and in depth — the
///   icons still sit on the parchment exactly as before) and BEFORE the transparent queue that
///   carries the panels. The panels now paint after the icons and therefore occlude them, which is
///   the perspective the player asked for. Nothing else moved.</item>
///   <item>THE RENDER QUEUE NEVER DID ANYTHING HERE. <c>_mat.renderQueue</c> is INERT for a
///   <c>CommandBuffer.DrawMesh</c>: the command is issued at the camera event with the material's
///   render state, it is not entered into the camera's sorted queues. The pre-fix comment claiming
///   the queue "still decides the particle overlap exactly as it does in flat" was wrong — the
///   camera event decided it, alone. The assignment is kept (see <see cref="EnsureResources"/>)
///   only because it costs nothing and documents the material's intent; do not reason from it.</item>
/// </list>
///
/// <para>WHY <c>ZWrite</c> STAYS OFF. The other half of an occlusion argument is the far side: a
/// panel BEHIND the icons must not paint over them, and with no depth written by either surface
/// nothing would stop it. It is not a reachable pose. The icons are lifted
/// <see cref="IconLiftWorld"/> = 0.10 world units above the parchment's top face, the parchment is
/// OPAQUE and writes depth across its whole extent, and the rig runs at ~198 world units per real
/// metre — so "behind the icons but in front of the parchment" is a 0.5 mm slab (real) that no
/// floated window can be inside of. The parchment's own depth already rejects everything below the
/// icon plane. Turning ZWrite on would also need a different shader (the built-in
/// <c>Unlit/Transparent</c> hardcodes <c>ZWrite Off</c>) and an alpha-blended quad writing depth
/// would punch its fully TRANSPARENT corners into the depth buffer as well — strictly worse.</para>
///
/// <para>WHAT THE MOVE COSTS, AND WHY IT IS NOT PAID BACK BY MOVING IT AGAIN. Painting before the
/// transparent queue means the queue's OTHER occupants also now paint over the icons — chiefly the
/// map's Wind/Clouds ambiance particles, which at <c>AfterForwardAlpha</c> could never win. That is
/// the same perspective the report asked for (a cloud between your eye and the map genuinely is in
/// front of it), but it is also the mechanism behind the older "thick see-through streaks over the
/// location icons" finding on the FLAT path, whose remedy is <c>[WorldUI] MapWindOpacity</c> — and
/// the room does not apply it, because the flat map render (and with it
/// <c>FlatScreenStereo.TuneMapWindParticles</c>) stands down while <c>MapRoomOwnsParchment</c>. It
/// could not be settled from source whether any of those systems is even on a layer the head camera
/// renders in the room, so <see cref="LogRenderOrder"/> COUNTS them and prints the number: the next
/// hardware log answers it instead of an argument. If it turns out to bite, the fix is to dim those
/// systems in the room as well — not to put this buffer back after the panels, which would restore
/// the reported bug.</para>
///
/// <para>MULTIPASS. The buffer is attached to the CAMERA, not registered as a per-eye callback:
/// Unity replays the same recorded command list in every rendering pass of that camera, so
/// MultiPass draws it once per eye from one recording. Everything in the recording is
/// eye-independent — world-space TRS matrices built from the decals' own transforms, textures and
/// a colour, no view/projection state and no per-eye branch — so there is no expression here that
/// could evaluate differently in the left and the right eye. A one-eyed result would have to come
/// from Unity replaying the list for only one pass, which would equally have broken the icons
/// before this change.</para>
///
/// <para>SIZE DIALS (user report against ModBuild 188: "Die Symbole auf der Map sind sehr klein — kannst
/// du im Debugmenu eine Option einbauen das ich sie größere machen kann? (Nur in der 3D Umgebung)
/// — dabei soll die Größe des Gloomhaven-Symbols gesondert eingestellt werden können"). Live
/// factors multiply the quad footprint and nothing else. They are read on every tick, so a dial
/// turned in the menu is visible on the next frame without a reload, and they exist only on this
/// path — the flat map's icon draw (<c>FlatScreenStereo.3.Map.DrawMapIcons</c>) never sees them.
/// See <see cref="IsCapital"/> for how the Gloomhaven icon is identified, and
/// <see cref="ScaleForDrawnQuad"/> for the contract the hover-pad lane needs.</para>
///
/// <para>ONE DIAL PER ICON POPULATION — WHY THERE ARE THREE (user report against ModBuild 192:
/// "Trenne die Größe des Symbole auf der Weltkarte und die Symbole auf der Karte für Gloomhaven.
/// Die müssen separat justiert werden."). Through ModBuild 192 there were two, and the split was
/// along the wrong axis: <c>IconScale</c> governed every non-capital icon on WHICHEVER map was on
/// screen, so his tuned 2.30 applied to the world map's villages AND to the city map's shopfronts,
/// which are authored at different sizes and want different factors. READ FROM SOURCE, the game
/// draws exactly two maps and never both:</para>
/// <list type="bullet">
///   <item><b>WORLD map</b> — <c>MapChoreographer.worldMap</c> (decompiled MapChoreographer.cs:64),
///   raised in <c>OpenWorldMap</c> (:3727-3731). Its population is every location whose quest is
///   not a City quest plus the villages that are not city locations, and — explicitly, at :3854 —
///   the capital's own <c>HeadquartersLocation</c> marker.</item>
///   <item><b>CITY map</b> — <c>MapChoreographer.cityMap</c> (:70), raised in <c>OpenCityMap</c>
///   (:3754-3755). Its population is <c>m_CityLocations</c> (:107 — the Merchant/Enhancer/Temple/
///   Trainer shopfronts, added at :614/:652/:692) plus the quests of type <c>City</c>
///   (<c>IsVisibleInMap</c> :3873). The Gloomhaven marker is explicitly HIDDEN here (:3839).</item>
/// </list>
/// <para>The two populations cannot overlap in a drawn frame: <c>RefreshShownLocationsByMap</c>
/// (:3761) calls <c>HideLocation</c> on everything that does not belong to the map on screen, and
/// <c>HideLocation</c> ends in <c>gameObject.SetActive(false)</c> (decompiled MapLocation.cs:931)
/// — while this layer collects with <c>includeInactive: false</c> and skips any decal that is not
/// <c>activeInHierarchy</c>. So "which map is on screen" IS "which population is drawn", and the
/// dial can be chosen per SURFACE rather than per icon. That is also the only choice that can
/// classify a decal the fallback sweep found with no <c>MapLocation</c> above it at all.</para>
/// <list type="number">
///   <item><c>[MapRoom] IconScale</c> — the WORLD map's general location icons. UNCHANGED KEY,
///   unchanged default, and its meaning is deliberately kept: the value the user tuned to 2.30 was
///   measured on the world map (hardware log, ModBuild 192: "IconScale x2.30 hit 37 icon(s)" with
///   the Headquarters marker present, i.e. the world map), so it keeps governing exactly those
///   icons. A dropped .cfg value in this project is always against the newest build; repurposing
///   this key would silently move a hand-tuned number to a different meaning.</item>
///   <item><c>[MapRoom] GloomhavenIconScale</c> — the capital's own marker, world map only.
///   Unchanged.</item>
///   <item><c>[MapRoom] CityIconScale</c> — NEW: every icon drawn while the GLOOMHAVEN CITY map is
///   on screen. It starts at 1.0, which means the city icons go back to their authored size on the
///   first boot of this build until he tunes it — that is the point of the report, not a
///   regression.</item>
/// </list>
///
/// <para>THE PARTY MARKER IS SCALED ON THE GAME'S OWN TRANSFORM SINCE ModBuild 195, AND THAT IS THE
/// SECOND GAME-COMPONENT WRITE THIS LAYER MAKES — SAY SO PLAINLY (user report against ModBuild 194,
/// verbatim: "Der Slider für den 'Gruppen-Marker' verändert nichts. Egal wie ich es umstelle, es
/// bleibt gleich klein."). THE ModBuild 194 INSTRUMENT ANSWERED ITS OWN QUESTION: its census read
/// "PartyMarkerScale x1.80 on 1 submesh draw(s), 1 of which could NOT take it" — the refusal fired
/// on the ONLY draw there is, so the dial was wired correctly and had nothing to act on.</para>
/// <para>WHY THE DRAW-MATRIX ROUTE COULD NOT BE REPAIRED IN PLACE. 194 re-drew the token with
/// <c>CommandBuffer.DrawMesh</c> and a conjugated scale matrix, which needs a mesh we can honestly
/// hand to <c>DrawMesh</c>; the token's single renderer has none (either it is a
/// <c>SkinnedMeshRenderer</c>, whose <c>sharedMesh</c> is the BIND POSE and would draw a T-posed
/// figure, or it is a renderer with no <c>MeshFilter</c> at all — the census below now prints WHICH,
/// by runtime type, so this is never guessed again). Three ways out were weighed:</para>
/// <list type="number">
///   <item>A MATRIX OVERLOAD OF <c>DrawRenderer</c>. CHECKED AGAINST THE SHIPPING ASSEMBLY rather
///   than against memory — the game's own <c>UnityEngine.CoreModule.dll</c> (Unity 2021.3.5f1)
///   exposes exactly three: <c>DrawRenderer(Renderer, Material)</c>,
///   <c>(Renderer, Material, int)</c> and <c>(Renderer, Material, int, int)</c>. THERE IS NO MATRIX
///   PARAMETER IN THIS UNITY VERSION, so the clean answer does not exist here. REJECTED as
///   unavailable, not as unattractive.</item>
///   <item><c>SkinnedMeshRenderer.BakeMesh</c> into a scratch mesh, drawn with a matrix. This fixes
///   ONLY the skinned branch and does nothing for a renderer with no <c>MeshFilter</c>, i.e. it
///   might not fix this token at all; and the marker ANIMATES (it walks the route during travel,
///   MapChoreographer → MapMovementFlow), so the bake would have to be re-run EVERY FRAME the token
///   is in view — a full CPU skinning pass per frame, per eye-independent buffer refill, to change
///   one number. REJECTED: worse cost, narrower coverage, and it still leaves the "could not take
///   it" branch reachable.</item>
///   <item>WRITE <c>PartyToken.transform.localScale</c>. TAKEN. It is the only lever that covers
///   BOTH branches, it costs one float compare per tick, and the evidence that it is safe was
///   already on the table and unused: <c>PartyToken</c> is 143 lines that write only
///   <c>transform.position</c> (:46, :65, :105) and <c>transform.LookAt</c> (:22, :48, :118); its
///   five callers (<c>MapChoreographer</c>, <c>MapMovementFlow</c>, <c>MapTimedMovementFlow</c>,
///   <c>MapPointsMovementFlow</c>, <c>IMapFlowConfig</c>) write only <c>transform.position</c>
///   (MapChoreographer.cs:740, :749, :2739) and read only <c>transform.position</c> for the camera
///   focal point; and a grep of the WHOLE decompile finds no writer of this transform's
///   <c>localScale</c> anywhere. Nothing in the game reads its scale either — the zoom-to-party path
///   drives <c>CameraController.m_ExtraMinimumFOV</c> from a config FOV, never from the token's
///   bounds. So this is a number with no other writer, the same shape as <c>widthMultiplier</c>
///   below, and it takes the same protocol.</item>
/// </list>
/// <para>THE PROTOCOL, WHICH IS THE PART THAT HAS TO BE RIGHT — see <see cref="ApplyTokenScale"/>
/// and <see cref="RestoreTokenScale"/>:</para>
/// <list type="number">
///   <item>THE ORIGINAL IS RECORDED BEFORE THE FIRST WRITE, once per token transform, at scan time.
///   The AUTHORED value is multiplied — not <c>Vector3.one</c> — so a token the artist did not build
///   at unit scale keeps its proportions.</item>
///   <item>THE WRITE IS LEVEL-TRIGGERED. A settled frame compares and writes nothing; the census
///   prints the write count, so "somebody else is writing this transform" is a number that fails to
///   fall to zero rather than a thing to argue about.</item>
///   <item>AT x1.00 THE ORIGINAL IS WHAT IS WRITTEN, so a default install leaves the token at
///   exactly the scale the game authored and performs no write at all.</item>
///   <item>RESTORED ON STAND-DOWN AND ON TEARDOWN, and ONLY WHILE IT IS STILL OURS: the restore is
///   skipped when the live value is not the one we last wrote, because taking a transform back from
///   whatever has since written it is the write war this project has already lost once. Multiplayer:
///   this is local presentation on an object the local client already has; nothing goes on the wire,
///   and the stand-down restore is what keeps a peer's map untouched.</item>
/// </list>
/// <para>THE DRAW IS PLAIN <c>DrawRenderer</c> AGAIN, for every token renderer, exactly as it was
/// through ModBuild 193. It needs no matrix now: <c>DrawRenderer</c> records the renderer's own
/// transform, so the scale on the root is already in the picture, and a skinned token skins
/// correctly because the engine still does the skinning. The 194 mesh-resolution pass is gone from
/// the draw path and survives only as the census's renderer-kind classification.</para>
///
/// <para>THE DRAWN ROUTE TAKES A DIAL TOO, AND THAT ONE DOES WRITE A GAME COMPONENT — SAY SO
/// PLAINLY. READ FROM SOURCE, the route between two map locations is a <c>LineRenderer</c>:
/// <c>MapLocation</c> instantiates <c>m_NodeLineRendererPrefab</c> under its own
/// <c>lineRenderers</c> child, once for the ACTIVE path to the hovered/selected destination
/// (decompiled MapLocation.cs:435-438) and once per permanent VILLAGE ROAD
/// (MapLocation.cs:1015-1054). Both are generated by the same routine: 31 points lerped between the
/// two <c>CenterPosition</c>s with a damped random perpendicular jitter, and a width written as a
/// 31-key <c>AnimationCurve</c> whose every key is <c>Random.Range(m_MinPathWith=0.2,
/// m_MaxPathWidth=0.4)</c> WORLD UNITS — that is what makes the road look hand-drawn
/// (MapLocation.cs:783-806 and :1036-1052). A <c>LineRenderer</c> has no mesh to re-draw and its
/// geometry is generated inside the engine, so there is no matrix trick available here: the honest
/// lever is the renderer's own width.</para>
/// <para>THE NUMBER WE OWN IS <c>widthMultiplier</c>, AND THE GAME NEVER WRITES IT. Unity's final
/// width is <c>widthCurve.Evaluate(t) * widthMultiplier</c>, two independent properties. The game
/// writes only the CURVE, at exactly two sites in the whole decompiled tree (MapLocation.cs:805 and
/// :1052), and only when a path is RECALCULATED — never per frame (<c>SetRenderer</c> early-returns
/// on an unchanged destination, MapLocation.cs:723-730). A grep of the whole decompile for
/// <c>widthMultiplier</c> finds it only in <c>ClientScenarioManager</c> (the in-scenario movement
/// lines) and <c>RFX4_ParticleTrail</c> — never on a map line. So this is not "conceding a flag and
/// owning the number" after a fight; it is a number with no other writer at all, and a curve rewrite
/// underneath us leaves our multiplier standing. It is still a write to a GAME COMPONENT, so it
/// follows the full ownership protocol: the ORIGINAL multiplier is recorded the first time each
/// renderer is seen, the value is re-asserted LEVEL-TRIGGERED (only when the live value differs from
/// what we want, so an idle frame writes nothing at all), and <see cref="Release"/> puts every
/// original back. THE RISK, stated: if a future game build starts writing <c>widthMultiplier</c> per
/// frame this becomes a write war, and the symptom would be a route whose width alternates — the
/// census counts our writes per tick, so a number that never falls to 0 in a settled frame IS that
/// diagnosis.</para>
///
/// <para>MIPMAPS FOR THE ICON QUADS (user report against ModBuild 193: "Die Symbole auf der map
/// haben auch ziemlich krasses aliasing, kannst du auch bei denen deinen Fix anwenden, der bei den
/// Karten schon geholfen hat?"). The icons sample the GAME'S OWN decal material texture, drawn
/// heavily minified on a parchment at ~198 world units per real metre, and a texture with no mip
/// chain under that much minification is exactly the shimmer the card faces had. The fix is the
/// SHARED cache — <c>Cards.CardFaceMipBake.BakedTextureFor</c>, one session-lifetime bake per
/// texture with a VRAM ceiling, a content-identity dedupe and its own refusal logging. No second
/// cache is written here; this layer only asks. Three things guard it:
/// <list type="number">
///   <item>MEASURE BEFORE BELIEVING. <see cref="LogIconSampling"/> prints the same quantity
///   <c>PanelSamplingProbe</c> reports for panels — SOURCE TEXELS PER RENDERED PIXEL, the max of the
///   two axes, measured through the LEFT eye's projection against the real per-eye target — together
///   with each texture's <c>mipmapCount</c>, filter mode and aniso level. If the icon textures turn
///   out to be mipped already, the bake is refused by the cache, the line says so, and the aliasing
///   is something else (see that method's doc for what the numbers would then mean).</item>
///   <item>A BAKE IS A HITCH, SO IT IS CAPPED. <see cref="MaxIconBakesPerFrame"/> first-asks per
///   frame, the same discipline as <c>PanelMipBake</c> (which caps at 2). A texture that cannot be
///   afforded this frame is DEFERRED — drawn from its original, mipless texture for that frame and
///   retried on the next one — and the deferred count is in the census. A hitch in VR is worse than
///   the aliasing, so the icon is never held back waiting for its bake.</item>
///   <item>REPEAT ASKS ARE FREE. The cache answers from a dictionary after the first ask, so the
///   per-frame path costs one lookup per icon and no allocation.</item>
/// </list></para>
///
/// <para>AND THE ICONS ARRIVE THE FRAME THEY EXIST, NOT UP TO A SECOND LATER (same report: "Auch
/// hier soll es direkt geladen und angezeigt werden, nicht erst nach 1,2 Sekunden nachdem es schon
/// zu sehen war"). THIS WAS ALREADY MEASURED IN OUR OWN LOG and was read past: the ModBuild 193
/// census climbs 0 → 4 → 6 → 16 → 27 → 47 across nine separate prints of a line that is throttled to
/// one per 0.5 s, i.e. the icon population took SECONDS to fill in, and a re-entry into the room
/// still shows 0 → 11 → 41. Two mechanisms produced that, and both are fixed here:
/// <list type="bullet">
///   <item>THE SCAN COLLECTED ONLY ACTIVE DECALS. <c>includeInactive: false</c> means a decal the
///   game's loader has spawned but not yet enabled is INVISIBLE to this layer until some later
///   rescan happens to catch it. It is now <c>includeInactive: true</c> — the same move
///   <c>PanelMipBake</c>'s arrival watch makes for exactly the same reason ("swap while the game's
///   loader still has the object disabled"). The DRAW loop is unchanged and still skips anything
///   not <c>activeInHierarchy</c>, so nothing new is drawn; what changes is that the decal is
///   already in hand, already baked, on the very frame the game enables it.</item>
///   <item>THE RESCAN CADENCE WAS A POLL. Fifteen frames is ~0.2 s of latency for a decal that has
///   just been created, and a decal whose material texture is still loading was re-checked at that
///   same rate. The material is now polled EVERY frame for every held decal (it already was, for
///   decals in the list — the arrival test is a reference compare against
///   <see cref="_seenTexByDecal"/>, one
///   reference compare), and the SCAN itself runs every frame while the population is still
///   settling (<see cref="WarmupHoldFrames"/>), falling back to the 15-frame cadence once nothing
///   has changed for that long. That is bounded work for about a second after entering the room and
///   after every map switch, and free in the steady state.</item>
/// </list></para>
///
/// <para>TEARDOWN: <see cref="Release"/> detaches the buffer from whatever camera holds it, destroys
/// the mesh and material, and RESTORES every route <c>widthMultiplier</c> it wrote. Apart from that
/// one documented, recorded-and-restored component write, nothing is ever left on a game object —
/// the decals, the token and their textures are only READ.</para>
/// </summary>
internal sealed class MapIconLayer
{
    private const string Scope = "MapRoom";

    /// <summary>
    /// WHERE THE ICON BUFFER HANGS ON THE HEAD CAMERA — the single lever that decides whether a
    /// floated window can cover an icon. <c>BeforeForwardAlpha</c> = after all forward-opaque
    /// geometry (the parchment is drawn and in depth), before the transparent queue (the panels
    /// are not drawn yet, and will therefore paint OVER the icons). The full argument, including
    /// why the material's render queue is not the lever, is in the class doc.
    ///
    /// <para>Declared once as a constant because attach and detach must never be able to name
    /// different events: <c>RemoveCommandBuffer</c> is keyed on the event, so a divergence would
    /// leave a live buffer on the game's camera after <see cref="Release"/> — the exact failure the
    /// class doc promises cannot happen ("Nothing is ever left on a game object").</para>
    /// </summary>
    private const CameraEvent IconEvent = CameraEvent.BeforeForwardAlpha;

    /// <summary>Lift above the parchment's top face, world units. The parchment mesh is ~0.13
    /// thick and carries animated foliage on its surface; this clears both without being visible
    /// as a float at map scale.</summary>
    private const float IconLiftWorld = 0.10f;

    /// <summary>Rig scale, world units per real metre (<c>MapRoomSeat</c>); used only to state the
    /// icon lift in real millimetres in the one-shot render-order log line, so the next hardware
    /// log carries the number the ZWrite argument rests on instead of a claim.</summary>
    private const float WorldUnitsPerMetre = 198f;

    /// <summary>Hard floor/ceiling for ALL THREE size dials, applied on TOP of the bind-site
    /// <c>AcceptableValueRange</c>. Belt and braces: a hand-edited .cfg can carry a value BepInEx
    /// never clamped (it clamps what it parses, not what a later hand-edit puts back), and a factor
    /// of 0 would erase every location marker on the map — i.e. remove the only way to pick a
    /// scenario. A setting may configure comfort, never reachability.</summary>
    private const float MinIconScale = 0.5f;

    /// <inheritdoc cref="MinIconScale"/>
    private const float MaxIconScale = 4f;

    /// <summary>Frames between decal re-scans IN THE SETTLED STATE. Same cadence as the flat path's
    /// icon cache — locations are destroyed and respawned by <c>MapChoreographer.InitMap</c>, so a
    /// cached set must be short-lived. It is NOT the cadence while the map is still filling in; see
    /// <see cref="WarmupHoldFrames"/>, which is the half of the late-loading report this constant
    /// used to own on its own.</summary>
    private const int RescanIntervalFrames = 15;

    /// <summary>
    /// How long the decal scan keeps running EVERY FRAME after the last time anything about the
    /// drawn population changed — the fix for "nicht erst nach 1,2 Sekunden".
    ///
    /// <para>WHY A POLL CANNOT SIMPLY BE DELETED HERE, unlike in <c>PanelMipBake</c>. That class
    /// watches uGUI graphics that already EXIST and are merely re-pointed at new art, so holding
    /// them with <c>includeInactive: true</c> and comparing one sprite reference per frame catches
    /// every arrival with no scanning at all. Map decals are different: <c>MapLocation.Init</c>
    /// INSTANTIATES the decal prefab (decompiled MapLocation.cs:401), so a decal can come into
    /// existence at any moment and there is no pre-existing object to have been watching. Something
    /// must therefore still look for new objects — the question is only how often.</para>
    ///
    /// <para>90 frames ≈ 1.25 s at 72 Hz. The cost of one scan is two
    /// <c>GetComponentsInChildren</c> calls over the map's two parents (a few dozen objects each);
    /// paying that per frame for about a second after entering the room, and for about a second
    /// after each change, is far below anything that could hitch — and it is FREE in the steady
    /// state, where the 15-frame cadence takes over again. Refreshed on every change rather than
    /// counted down from room entry, because the population also changes on a world↔city switch and
    /// on every <c>InitMap</c>, not only when the room opens.</para>
    /// </summary>
    private const int WarmupHoldFrames = 90;

    /// <summary>
    /// FIRST-TIME mip bakes this layer may ASK FOR in one frame. A first ask costs a full GPU
    /// readback of the source texture inside <c>CardFaceMipBake</c>; repeat asks are a dictionary
    /// lookup and are not counted here. Two per frame is the cap <c>PanelMipBake</c> settled on for
    /// the same reason and is deliberately the same number: A HITCH IN VR IS WORSE THAN THE
    /// ALIASING, so an icon whose bake cannot be afforded this frame is drawn from its ORIGINAL
    /// texture — aliased, but present and on time — and is retried on the next frame. The census
    /// prints how many were deferred, so "the fix did not reach this icon" is never silent.
    /// </summary>
    private const int MaxIconBakesPerFrame = 2;

    /// <summary>
    /// Texels-per-rendered-pixel at or above which an icon is called out as UNDERSAMPLED in
    /// <see cref="LogIconSampling"/>. Copied as a value from <c>PanelSamplingProbe</c>'s
    /// <c>SuspectMinification</c>/<c>BakeMinification</c> so the icon report and the panel report
    /// are read against the same threshold — a number that means one thing on one surface and
    /// another thing on another surface is not a measurement.
    /// </summary>
    private const float SuspectMinification = 1.35f;

    /// <summary>Seconds between icon-sampling reports. Same cadence as
    /// <c>PanelSamplingProbe</c>'s summary, and the measurement itself is only computed on the tick
    /// that is about to print — the per-frame draw path never pays for it.</summary>
    private const float SamplingReportIntervalSeconds = 10f;

    private static readonly int IconMainTex = Shader.PropertyToID("_MainTex");
    private static readonly int IconColor = Shader.PropertyToID("_Color");

    private static System.Type? _decalType;
    private static System.Reflection.PropertyInfo? _decalCurMatProp;
    private bool _decalTypeMissing;

    private CommandBuffer? _cmd;
    private Camera? _cam;
    private Mesh? _quad;
    private Material? _mat;

    private readonly List<Component> _decals = new(64);
    private readonly List<Renderer?> _decalRenderers = new(64);

    /// <summary>The <c>MapLocation</c> that OWNS each collected decal, index-parallel to
    /// <see cref="_decals"/>; null for a decal that belongs to no location (the scene-wide
    /// fallback sweep can pick those up). See <see cref="IsCapital"/>.</summary>
    private readonly List<global::MapLocation?> _decalOwners = new(64);

    /// <summary>
    /// The texture each held decal was last seen carrying, keyed by the DECAL'S instance id. THE
    /// ARRIVAL TEST: the stored reference differing from the live one is true on exactly the frame
    /// the game's loader points that decal's material at different art — the same shape as
    /// <c>PanelMipBake</c>'s per-graphic <c>Seen</c> snapshot.
    ///
    /// <para>KEYED, NOT INDEX-PARALLEL, and that is not a stylistic choice. The scan list is rebuilt
    /// every frame while the population is settling (see <see cref="WarmupHoldFrames"/>); a parallel
    /// list would be reset by every one of those rebuilds, so every decal would read as "arrived"
    /// on every warm-up frame and the count would measure the scan rather than the loader. A
    /// dictionary carries the snapshot across rebuilds, which is what <c>PanelMipBake</c>'s
    /// <c>CarrySeen</c> does for the same reason.</para>
    /// </summary>
    private readonly Dictionary<int, Texture?> _seenTexByDecal = new(64);

    /// <summary>
    /// Texture instance ids already OFFERED to <c>CardFaceMipBake</c>. A repeat ask is a dictionary
    /// lookup and free, so this set exists only to spend <see cref="MaxIconBakesPerFrame"/> on FIRST
    /// asks — the same job <c>PanelMipBake</c>'s <c>Asked</c> does.
    ///
    /// <para>IT IS STATIC AND IT IS NEVER CLEARED, WHICH IS THE POINT. The set has to have the SAME
    /// lifetime as the cache it is rationing access to, and that cache is session-lifetime by
    /// contract. Clearing it on <see cref="Release"/> would have re-imposed the per-frame cap on
    /// every re-entry into the map room and on every transient release (a world↔city switch releases
    /// this layer for a frame), so ~47 already-baked icons would have been metered back in two per
    /// frame — drawn from their mipless originals for the better part of a second each time. That is
    /// precisely the "erst nach 1,2 Sekunden" symptom this build exists to remove, re-introduced by
    /// the fix for it. With the set static, a re-entry pays nothing at all.</para>
    /// </summary>
    private static readonly HashSet<int> BakeAsked = new(64);

    private readonly List<Renderer> _tokenRenderers = new(8);

    /// <summary>
    /// WHAT KIND OF RENDERER THE TOKEN ACTUALLY IS, in words, rebuilt once per scan and printed by
    /// the census. This is the ModBuild 194 mesh-resolution pass turned into a DIAGNOSIS: 194 could
    /// only report "1 draw could not take the dial" and left the reader to guess which of two causes
    /// it was, which cost a hardware round trip. The string names each token renderer's runtime type
    /// and whether it has a <c>MeshFilter</c> mesh, so the next log ANSWERS that question instead of
    /// re-asking it. Nothing in the draw path reads it — since ModBuild 195 the scale rides the
    /// token's transform and every renderer is drawn with plain <c>DrawRenderer</c>, so no renderer
    /// kind can be refused any more.
    /// </summary>
    private string _tokenKinds = "<not scanned>";

    /// <summary>The party token's own root transform — the transform the marker dial SCALES. See the
    /// class doc for the full argument that this is a number with no other writer in the game, and
    /// <see cref="ApplyTokenScale"/> for the record-once / level-triggered / restore-if-still-ours
    /// protocol that write follows.</summary>
    private Transform? _tokenRoot;

    /// <summary>The transform whose authored <c>localScale</c> is held in
    /// <see cref="_tokenOriginalScale"/>. Kept as its own reference rather than assumed to be
    /// <see cref="_tokenRoot"/>, so that a token the game destroys and rebuilds re-records its own
    /// authored value instead of inheriting the dead one's — and so the restore can tell whether the
    /// record it holds still belongs to the object in front of it.</summary>
    private Transform? _tokenScaleOwner;

    /// <summary>The token root's <c>localScale</c> as the game authored it, read ONCE before our
    /// first write. The dial multiplies THIS, not <c>Vector3.one</c>, so a token that was not built
    /// at unit scale keeps its proportions at every dial value.</summary>
    private Vector3 _tokenOriginalScale = Vector3.one;

    /// <summary>The exact value we last wrote to the token's <c>localScale</c>, so
    /// <see cref="RestoreTokenScale"/> can tell "still ours" from "somebody else has written it
    /// since" and decline to stomp the second case.</summary>
    private Vector3 _tokenWrittenScale = Vector3.one;

    /// <summary>Have we written the token's scale at all since the original was recorded? Until this
    /// is true there is nothing to restore and nothing to compare against, and a default install
    /// (dial at x1.00) never sets it — the level-triggered write finds the wanted value already
    /// there and returns.</summary>
    private bool _tokenScaleWritten;

    /// <summary>Every route <c>LineRenderer</c> this layer has found, and the <c>widthMultiplier</c>
    /// each one had BEFORE we first wrote it. Kept as a dictionary rather than a list parallel to
    /// the scan, because it must survive a rescan: the original is what <see cref="Release"/> puts
    /// back, and a rebuilt scan list would lose it. Destroyed renderers stay as fake-null keys and
    /// are skipped on restore — the managed key object is still a valid dictionary key.</summary>
    private readonly Dictionary<LineRenderer, float> _pathOriginalWidth = new(64);

    /// <summary>The route renderers found by the most recent scan (the ones we act on this tick).
    /// Rebuilt per scan; <see cref="_pathOriginalWidth"/> is the durable half.</summary>
    private readonly List<LineRenderer> _pathRenderers = new(64);

    /// <summary>Scratch for the per-scan <c>MapLocation</c> sweep — a field so the scan allocates no
    /// list per pass.</summary>
    private readonly List<global::MapLocation> _locationScratch = new(64);

    /// <summary>Scratch for the per-location <c>LineRenderer</c> sweep. Same reason.</summary>
    private readonly List<LineRenderer> _lineScratch = new(8);

    /// <summary>True when the route renderers came from the FALLBACK sweep of the active map object
    /// rather than from the locations themselves — i.e. the <c>lineRenderers</c> holder is not
    /// parented under its <c>MapLocation</c> in this build. Log material: the census names which
    /// route the number came from, so a zero is attributable.</summary>
    private bool _pathFromFallback;

    private readonly List<MaterialPropertyBlock> _mpbPool = new(64);
    private int _scanFrame = int.MinValue;

    /// <summary>Frames of every-frame scanning still owed — see <see cref="WarmupHoldFrames"/>.</summary>
    private int _warmFramesLeft;

    /// <summary>Drawn icon count at the last tick, so a CHANGE can refresh the warm-up hold. The
    /// population filling in is exactly what "still settling" means.</summary>
    private int _lastDrawnCount = -1;

    /// <summary>Signature of the last census line printed, so the line re-prints when — and only
    /// when — one of the numbers in it actually changed (icon count, token draws, either applied
    /// scale, or how many icons each scale hit).</summary>
    private int _censusSignature;

    /// <summary>Whether a census has been printed at all since the last <see cref="Release"/>. A
    /// separate flag rather than a sentinel value of <see cref="_censusSignature"/>, because every
    /// int is a signature a real census could produce — a sentinel would silently swallow the FIRST
    /// line for exactly one unlucky state, and the first line is the one that proves the scan ran.
    /// Same sentinel-collision class as the frame-count latches elsewhere in this codebase, caught
    /// here before it could ship.</summary>
    private bool _censusPrinted;

    /// <summary>Earliest time the census line may print again. A stepper held down in the options
    /// pane would otherwise emit one line per repeat; 0.5 s keeps every distinct value the user
    /// rests on and drops only the ones that flew past.</summary>
    private float _censusNextAllowed;

    private const float CensusMinIntervalSeconds = 0.5f;

    /// <summary>Earliest time the icon-SAMPLING line may print again, and the flag that decides
    /// whether the draw loop measures at all this frame. The measurement costs three
    /// <c>WorldToViewportPoint</c> calls per icon, so it is only done on the tick that is about to
    /// print it — every other frame the draw loop skips the whole block.</summary>
    private float _samplingNextAllowed;

    /// <inheritdoc cref="_samplingNextAllowed"/>
    private bool _measureThisTick;

    // The worst-sampled icon of the tick being measured, plus the population counts that go with
    // it. Fields rather than a struct passed around, because they are written from inside the draw
    // loop and read once by LogIconSampling at the end of the same tick.
    private float _worstMinification;
    private string _worstIconWhat = string.Empty;
    private int _measuredIcons;
    private int _underSampledIcons;
    private int _miplessIcons;
    private int _bakedIcons;
    private int _nonTexture2DIcons;

    /// <summary>
    /// WHICH OF THE GAME'S TWO CAMPAIGN MAPS IS ON SCREEN — the axis the size dials are split
    /// along (see the class doc). <c>Unknown</c> is a real, reachable state: the choreographer has
    /// not been found yet, or both map GameObjects are down mid-transition
    /// (<c>MapChoreographer.OpenCityMap</c> deactivates one before activating the other).
    /// </summary>
    internal enum MapSurface
    {
        Unknown,
        World,
        City,
    }

    /// <summary>
    /// The surface resolved for <see cref="_surfaceFrame"/>, and whether one has been resolved at
    /// all. Two fields rather than a sentinel frame number, the same reason
    /// <see cref="_censusPrinted"/> is its own flag: every int is a frame count some real frame
    /// has, so a sentinel would silently swallow one unlucky frame. Nothing here ever does
    /// arithmetic on the frame number — only <c>==</c> — so there is no overflow to get wrong.
    /// </summary>
    private static bool _surfaceResolved;

    /// <inheritdoc cref="_surfaceResolved"/>
    private static int _surfaceFrame;

    /// <inheritdoc cref="_surfaceResolved"/>
    private static MapSurface _surface = MapSurface.Unknown;

    /// <summary>
    /// The map currently on screen, resolved AT MOST ONCE PER FRAME and shared by everything that
    /// needs it in that frame.
    ///
    /// <para>WHY IT IS CACHED PER FRAME AND NOT PER CALL. <see cref="ScaleForDrawnQuad"/> is called
    /// once per hover pad per frame by <c>MapIconHoverPads.PosePad</c> — three or four dozen calls
    /// in a frame — and every one of them must return the SAME answer as the one the draw loop
    /// used, or a pad and its icon would be sized from different maps for one frame during a
    /// world↔city switch. One resolution per frame makes that impossible by construction rather
    /// than by call ordering, which is the part that would otherwise be fragile: the pads and the
    /// icon layer are ticked from two different places in <c>MapRoomDriver.TickActive</c>.</para>
    ///
    /// <para><see cref="Tick"/> seeds it from the choreographer it was HANDED (authoritative, and
    /// it runs first); a caller that arrives in a frame Tick did not run in resolves it from
    /// <c>MapRoomDriver.Choreographer</c> instead. Never throws — a failure resolves to
    /// <c>Unknown</c>, which the dial choice treats as the world map, i.e. exactly the behaviour
    /// that shipped before this split existed.</para>
    /// </summary>
    internal static MapSurface CurrentSurface =>
        _surfaceResolved && _surfaceFrame == Time.frameCount
            ? _surface
            : ResolveSurface(MapRoomDriver.Choreographer);

    /// <summary>Resolve and latch the surface for THIS frame. See <see cref="CurrentSurface"/>.</summary>
    private static MapSurface ResolveSurface(global::MapChoreographer? choreo)
    {
        MapSurface s;
        try
        {
            // The mod's single source of truth for "which map is up" — the same call
            // MapParchment.Acquire makes to decide which parchment to override, so the icon dial
            // and the parchment can never disagree about which map the player is looking at.
            GameObject? active = MapParchment.ResolveActiveMapGo(choreo, out bool isCity);
            s = active == null ? MapSurface.Unknown : isCity ? MapSurface.City : MapSurface.World;
        }
        catch (System.Exception)
        {
            // Degrade, never throw: this is reached from a per-frame Update path, and an unguarded
            // exception in one of those starves VR input for the whole rig.
            s = MapSurface.Unknown;
        }
        _surface = s;
        _surfaceFrame = Time.frameCount;
        _surfaceResolved = true;
        return s;
    }

    /// <summary>Icons drawn on the most recent rebuild (diagnostics).</summary>
    internal int DrawnCount { get; private set; }

    /// <summary>Party-token submesh draws on the most recent rebuild (diagnostics).</summary>
    internal int TokenDrawCount { get; private set; }

    /// <summary>
    /// The size factor this layer applies to the drawn quad of <paramref name="decal"/> — the
    /// contract for anything that must stay the same size as the DRAWN icon rather than the
    /// authored decal.
    ///
    /// <para>WHY IT IS PUBLIC. <c>MapIconHoverPads</c> builds its pick pad "at exactly the quad
    /// MapIconLayer draws — same centre, same yaw, same footprint" (its own class doc) and derives
    /// that footprint independently from <c>decal.lossyScale.xz</c>. It does not know about these
    /// dials, so with a factor other than 1 the pad and the drawn icon are no longer the same
    /// rectangle. This method is the one number that lane needs to multiply by; nothing inside this
    /// class calls it (the draw loop reads both dials once per tick instead of once per icon).</para>
    ///
    /// <para>IT ANSWERS FOR THE MAP THAT IS ON SCREEN. Since ModBuild 193 the dial depends on the
    /// SURFACE as well as on the icon (class doc, "one dial per icon population"), so a pad built
    /// while the world map is up and a pad built while the city map is up get different numbers
    /// from the same code. The surface comes from <see cref="CurrentSurface"/>, which is resolved
    /// once per frame and shared with the draw loop — so the pad and the quad it shadows are
    /// guaranteed to have used the same map, the same dial and the same clamp, which is the whole
    /// point of this method existing instead of the pad lane reading the config entries itself.</para>
    ///
    /// <para>See <see cref="IsCapital"/> for the Gloomhaven identification and
    /// <see cref="ClampScale"/> for the floor.</para>
    /// </summary>
    internal static float ScaleForDrawnQuad(Component? decal) =>
        ScaleFor(CurrentSurface, IsCapital(OwnerOf(decal)));

    /// <summary>
    /// THE DIAL CHOICE, in one place so the draw loop and the hover pads cannot drift apart.
    ///
    /// <para>On the CITY map every drawn icon takes <c>[MapRoom] CityIconScale</c> — including,
    /// hypothetically, the capital's marker, which the game hides there
    /// (MapChoreographer.cs:3839) but which would be a city icon if it ever appeared. On the WORLD
    /// map the capital takes its own dial and everything else takes <c>[MapRoom] IconScale</c>.
    /// <c>Unknown</c> is folded into the world case: it is what a missing choreographer or a
    /// mid-transition frame produces, and treating it as the world map reproduces exactly what
    /// shipped before the split.</para>
    /// </summary>
    private static float ScaleFor(MapSurface surface, bool isCapital)
    {
        ConfigEntry<float>? entry = surface == MapSurface.City
            ? Plugin.MapCityIconScale
            : isCapital
                ? Plugin.MapGloomhavenIconScale
                : Plugin.MapIconScale;
        return ClampScale(entry != null ? entry.Value : 1f);
    }

    /// <summary>
    /// <c>[MapRoom] PartyMarkerScale</c>, clamped — the size of the marker showing where the party
    /// currently is. Read live once per tick like every other dial here, so a step in the options
    /// pane shows on the next frame. See the class doc for why this one DOES write the game's own
    /// transform and what that costs, and <see cref="ApplyTokenScale"/> for the protocol.
    /// </summary>
    private static float PartyMarkerScale() =>
        ClampScale(Plugin.MapPartyMarkerScale != null ? Plugin.MapPartyMarkerScale.Value : 1f);

    /// <summary>
    /// <c>[MapRoom] PathWidthScale</c>, clamped — the width of the drawn route between two
    /// locations. Multiplies the game's own hand-drawn width CURVE through the untouched
    /// <c>widthMultiplier</c>, so the road keeps its ragged authored profile and only gets thicker
    /// or thinner as a whole. See the class doc for the ownership protocol this one needs and does
    /// not skip.
    /// </summary>
    private static float PathWidthScale() =>
        ClampScale(Plugin.MapPathWidthScale != null ? Plugin.MapPathWidthScale.Value : 1f);

    /// <summary>
    /// Rebuild the icon command buffer for this frame and make sure it is attached to
    /// <paramref name="head"/>. Called every frame while the map room is up: the icons move with
    /// the map's own state, and the buffer is cheap to refill (a few dozen <c>DrawMesh</c> calls).
    /// </summary>
    internal void Tick(Camera? head, MeshRenderer? parchment, global::MapChoreographer? choreo)
    {
        if (head == null || parchment == null || choreo == null)
        {
            Release("no head camera / no parchment / no choreographer");
            return;
        }
        if (!EnsureResources())
            return;

        if (!ReferenceEquals(_cam, head))
        {
            Detach();
            head.AddCommandBuffer(IconEvent, _cmd);
            _cam = head;
            LogRenderOrder(head);
        }

        _cmd!.Clear();
        Rescan(choreo);

        // WHICH MAP, resolved from the choreographer we were HANDED and latched for this frame, so
        // the hover pads that pose later in the same frame read the identical answer (see
        // CurrentSurface). Done before the dials are read: the surface decides which of them the
        // icons will actually take.
        MapSurface surface = ResolveSurface(choreo);
        bool cityMapShown = surface == MapSurface.City;

        // Read ONCE per tick, not once per icon: all three dials are live (a menu step must show on
        // the next frame with no reload) but they must not be able to change value halfway through a
        // buffer refill, which would put two different sizes in one frame's recording. All three are
        // read even though at most two can hit anything this frame — the census line reports the
        // idle one's VALUE too, so "the dial is at 2.30 and hit nothing" is distinguishable from
        // "the dial is at 1.00", which is the difference between a wrong map and a wrong dial.
        float worldScale = ScaleFor(MapSurface.World, isCapital: false);
        float capitalScale = ScaleFor(MapSurface.World, isCapital: true);
        float cityScale = ScaleFor(MapSurface.City, isCapital: false);
        float markerScale = PartyMarkerScale();
        float pathScale = PathWidthScale();

        // THE MEASUREMENT IS ONLY TAKEN ON THE TICK THAT PRINTS IT. Three WorldToViewportPoint calls
        // per icon is nothing once every ten seconds and is not nothing every frame at 72 Hz in two
        // eyes, so the draw loop asks this flag before it computes anything the picture does not
        // need. Everything the measurement reads (the quad's own corners, the texture) is already in
        // hand at that point in the loop, which is why it lives there rather than in a second pass.
        float now = Time.unscaledTime;
        _measureThisTick = now >= _samplingNextAllowed;
        if (_measureThisTick)
        {
            _worstMinification = 0f;
            _worstIconWhat = string.Empty;
            _measuredIcons = 0;
            _underSampledIcons = 0;
            _miplessIcons = 0;
            _bakedIcons = 0;
            _nonTexture2DIcons = 0;
        }

        // ONE bake budget for the whole tick, spent on FIRST asks only (see MaxIconBakesPerFrame).
        int bakeBudget = MaxIconBakesPerFrame;
        int deferredBakes = 0;

        float planeY = parchment.bounds.max.y + IconLiftWorld;
        int drawn = 0;
        int capitalHits = 0;
        int cityHits = 0;
        int ownerless = 0;
        int arrivals = 0;
        for (int i = 0; i < _decals.Count; i++)
        {
            Component d = _decals[i];
            if (d == null)
                continue;
            Texture? tex = null;
            if (_decalCurMatProp?.GetValue(d) is Material cm)
                tex = cm.HasProperty(IconMainTex) ? cm.GetTexture(IconMainTex) : cm.mainTexture;

            // ARRIVAL WATCH, and it runs for INACTIVE decals too — that is the whole point. The scan
            // now holds every decal under the map (includeInactive: true, see Rescan), so the frame
            // the game's loader points a still-disabled decal's material at its art, we see the
            // reference change and bake it HERE, before it is ever shown. That is the equivalent of
            // PanelMipBake swapping while the loader still has the graphic disabled, and it is what
            // turns "aliased for 1.2 s, then correct" into "correct on the first frame it is drawn".
            int decalId = d.GetInstanceID();
            if (!_seenTexByDecal.TryGetValue(decalId, out Texture? seen) || !ReferenceEquals(tex, seen))
            {
                _seenTexByDecal[decalId] = tex;
                if (tex != null)
                    arrivals++;
            }

            // THE VISIBLE MAP IS SERVED FIRST. The scan holds BOTH maps' decals now
            // (includeInactive: true), and the list is in scan order, not in "what the player can
            // see" order — so without this gate the hidden map's icons could spend the whole
            // per-frame bake budget while the icons actually on screen kept their aliased texture.
            // Inactive decals are pre-warmed AFTER this loop, with whatever budget is left over.
            if (!d.gameObject.activeInHierarchy)
                continue;

            Texture? drawTex = MippedOrOriginal(tex, ref bakeBudget, ref deferredBakes);
            if (drawTex == null || _decalRenderers[i] == null)
                continue;

            // The owner is re-READ (not re-found) every frame on purpose: MapLocation.MapLocationType
            // is assigned inside MapLocation.Init, which can run a frame or two after the decal
            // exists, so a flag latched at scan time could be stale for the icon's whole life.
            global::MapLocation? owner = _decalOwners[i];
            bool isCapital = IsCapital(owner);
            // Surface first, capital second — the same order ScaleFor uses, and the reason the two
            // cannot disagree is that this IS the same decision written twice for speed: the loop
            // must not pay a config read per icon, so it selects between three numbers it already
            // holds. Any change here belongs in ScaleFor as well or the hover pads drift.
            float f = cityMapShown ? cityScale : isCapital ? capitalScale : worldScale;
            if (cityMapShown)
                cityHits++;
            else if (isCapital)
                capitalHits++;
            if (owner == null)
                ownerless++;

            Transform dt = d.transform;
            Vector3 ds = dt.lossyScale;
            Vector3 dp = dt.position;
            var pos = new Vector3(dp.x, planeY, dp.z);
            // The floor is applied to the AUTHORED footprint and the dial multiplies the result, so
            // the dial is a clean multiple of what shipped: at 1.0 this is bit-for-bit the pre-dial
            // matrix. Order matters — flooring the PRODUCT would silently ignore the dial on any
            // decal the floor caught.
            var scale = new Vector3(
                Mathf.Max(Mathf.Abs(ds.x), 0.01f) * f,
                1f,
                Mathf.Max(Mathf.Abs(ds.z), 0.01f) * f);
            var rot = Quaternion.Euler(0f, dt.eulerAngles.y, 0f);
            MaterialPropertyBlock mpb = RentMpb(drawn);
            mpb.SetTexture(IconMainTex, drawTex);
            mpb.SetColor(IconColor, Color.white);
            _cmd.DrawMesh(_quad, Matrix4x4.TRS(pos, rot, scale), _mat, 0, 0, mpb);
            drawn++;

            if (_measureThisTick)
                MeasureIcon(head, owner, tex, drawTex, pos, rot, scale);
        }

        // PRE-WARM THE MAP THE PLAYER IS NOT LOOKING AT, with whatever budget the visible one left.
        // This is the other half of "direkt geladen": the city map's shopfront icons get their bake
        // while the world map is still on screen, so stepping into the city shows them sharp on the
        // first frame instead of paying for 20-odd bakes at the moment of the switch. Nothing is
        // drawn here — the bake is a cache warm and the picture is untouched.
        int prewarmed = 0;
        for (int i = 0; i < _decals.Count && bakeBudget > 0; i++)
        {
            Component d = _decals[i];
            if (d == null || d.gameObject.activeInHierarchy)
                continue;
            if (_decalCurMatProp?.GetValue(d) is not Material cm)
                continue;
            Texture? tex = cm.HasProperty(IconMainTex) ? cm.GetTexture(IconMainTex) : cm.mainTexture;
            if (tex == null)
                continue;
            int before = bakeBudget;
            MippedOrOriginal(tex, ref bakeBudget, ref deferredBakes);
            if (bakeBudget != before)
                prewarmed++;
        }

        // ORDER IS LOAD-BEARING: the scale goes on the token's transform FIRST, and the draw is
        // recorded second. DrawRenderer captures the renderer's matrix at EXECUTION time rather than
        // at record time, so strictly speaking either order would look right — but writing first
        // keeps the recording and the transform in step for any future path that does read the
        // matrix here, and it costs nothing.
        int tokenWrites = ApplyTokenScale(markerScale);
        int tokenDraws = DrawToken();
        int pathWrites = ApplyPathWidth(pathScale, head.cullingMask, out int pathDrawing, out int pathSeen);

        // The population is still filling in whenever the drawn count moves; hold the every-frame
        // scan cadence open for another WarmupHoldFrames from that moment. See the class doc for
        // why a poll cannot simply be removed here the way it was for the panels.
        if (drawn != _lastDrawnCount)
        {
            _lastDrawnCount = drawn;
            _warmFramesLeft = WarmupHoldFrames;
        }
        else if (_warmFramesLeft > 0)
        {
            _warmFramesLeft--;
        }

        DrawnCount = drawn;
        TokenDrawCount = tokenDraws;
        LogCensus(drawn, tokenDraws, capitalHits, cityHits, ownerless, planeY,
                  surface, worldScale, capitalScale, cityScale,
                  markerScale, tokenWrites, pathScale, pathWrites, pathDrawing, pathSeen,
                  arrivals, deferredBakes, prewarmed);
        if (_measureThisTick)
        {
            _samplingNextAllowed = now + SamplingReportIntervalSeconds;
            LogIconSampling();
        }
    }

    /// <summary>
    /// The texture the quad should SAMPLE: the shared mip-baked copy when one exists or can be
    /// afforded this frame, otherwise the game's own original.
    ///
    /// <para>NEVER BLOCKS THE PICTURE. A texture whose first bake cannot be afforded on this frame
    /// returns the ORIGINAL and is counted in <paramref name="deferred"/> — the icon is drawn on
    /// time and aliased for a frame or two rather than held back, because a hitch in VR is worse
    /// than the aliasing. It is retried next frame, since nothing was recorded for it.</para>
    ///
    /// <para>THE CACHE DECIDES, NOT THIS METHOD. <c>CardFaceMipBake.BakedTextureFor</c> already
    /// refuses an already-mipped texture, an oversized one and one past the VRAM ceiling, caches the
    /// refusal so it is never retried, and logs its own reason. Repeat asks are a dictionary lookup;
    /// <see cref="_bakeAsked"/> exists only so the per-frame cap is spent on FIRST asks.</para>
    /// </summary>
    private Texture? MippedOrOriginal(Texture? tex, ref int bakeBudget, ref int deferred)
    {
        if (tex == null)
            return null;
        // Only a Texture2D can be baked. A RenderTexture or a texture array is left exactly as it
        // is — the same rule PanelSamplingProbe states for panels ("RT, never bakeable").
        if (tex is not Texture2D flat || flat == null)
            return tex;
        int id = flat.GetInstanceID();
        if (!BakeAsked.Contains(id))
        {
            if (bakeBudget <= 0)
            {
                deferred++;
                return tex;
            }
            bakeBudget--;
            BakeAsked.Add(id);
        }
        Texture2D? baked = Cards.CardFaceMipBake.BakedTextureFor(flat);
        return baked != null ? baked : tex;
    }

    /// <summary>
    /// Draw the party token into this frame's buffer and report how many submesh draws that was.
    ///
    /// <para>PLAIN <c>DrawRenderer</c> FOR EVERY RENDERER, WHATEVER KIND IT IS — the same call that
    /// shipped through ModBuild 193, restored at 195 after 194's matrix path proved unable to touch
    /// this particular token at all. There is no size decision left in here: the marker dial is
    /// applied to the token's own transform by <see cref="ApplyTokenScale"/> before this runs, and
    /// <c>DrawRenderer</c> records the renderer's own matrix, so the scale is already in the picture.
    /// That is also why no renderer kind can be refused any more — a <c>SkinnedMeshRenderer</c> skins
    /// correctly here because the ENGINE does the skinning, and a renderer with no <c>MeshFilter</c>
    /// never needed a mesh from us in the first place.</para>
    ///
    /// <para>-1 as the pass index means "every valid pass of the material", which is what makes a
    /// multi-pass token render the way the game's own camera renders it.</para>
    /// </summary>
    private int DrawToken()
    {
        int draws = 0;
        for (int i = 0; i < _tokenRenderers.Count; i++)
        {
            Renderer tr = _tokenRenderers[i];
            if (tr == null || !tr.enabled || !tr.gameObject.activeInHierarchy)
                continue;
            Material[] mats = tr.sharedMaterials;
            for (int sm = 0; sm < mats.Length; sm++)
            {
                if (mats[sm] == null)
                    continue;
                _cmd!.DrawRenderer(tr, mats[sm], sm, -1);
                draws++;
            }
        }
        return draws;
    }

    /// <summary>
    /// How far the token's <c>localScale</c> may drift from the wanted value before it is rewritten,
    /// as a squared distance in local units. Loose enough that the float we wrote last tick compares
    /// equal to itself forever (so a settled frame writes nothing), tight enough that the smallest
    /// step the dial can take — the config stepper moves in hundredths against an authored scale of
    /// order 1 — always lands as a write.
    /// </summary>
    private const float TokenScaleEpsilonSq = 1e-8f;

    /// <summary>
    /// Put <c>[MapRoom] PartyMarkerScale</c> on the party token's own transform, LEVEL-TRIGGERED, and
    /// return how many writes that actually took this tick (0 or 1).
    ///
    /// <para>THIS IS THE SECOND — AND LAST — GAME COMPONENT THIS LAYER WRITES, and the class doc
    /// carries the whole argument for why <c>PartyToken.transform.localScale</c> is a number with no
    /// other writer in the game and no reader either. What matters here is the protocol:</para>
    /// <list type="number">
    ///   <item>THE ORIGINAL WAS RECORDED AT SCAN TIME, before any write (see the token block in
    ///   <see cref="Rescan"/>), and it is the AUTHORED scale that gets multiplied — so a token built
    ///   at something other than unit scale keeps its proportions.</item>
    ///   <item>NOTHING IS WRITTEN WHEN THE VALUE IS ALREADY THERE. At x1.00 the wanted value IS the
    ///   authored value, so a default install performs no write at all, ever.</item>
    ///   <item>A NON-ZERO WRITE COUNT ON A SETTLED FRAME IS THE WRITE-WAR DIAGNOSIS, which is why the
    ///   census prints this number beside the route's.</item>
    /// </list>
    /// <para>NO ROOT IS A REAL STATE, NOT AN ERROR: the choreographer has no <c>m_PartyToken</c> yet,
    /// or the map is mid-rebuild. It writes nothing and the census says the dial had nothing to act
    /// on, which is a finding rather than a silent frame.</para>
    /// </summary>
    private int ApplyTokenScale(float scale)
    {
        Transform? t = _tokenRoot;
        if (t == null || !ReferenceEquals(t, _tokenScaleOwner))
            return 0;
        Vector3 want = _tokenOriginalScale * scale;
        if ((t.localScale - want).sqrMagnitude <= TokenScaleEpsilonSq)
            return 0;
        t.localScale = want;
        _tokenWrittenScale = want;
        _tokenScaleWritten = true;
        return 1;
    }

    /// <summary>
    /// Put the party token's authored <c>localScale</c> back, and forget the record. Returns what
    /// happened, in words, for the stand-down log — the three outcomes are genuinely different and
    /// collapsing them into a bool is how a skipped restore becomes invisible.
    ///
    /// <para>ONLY WHILE IT IS STILL OURS. If the live scale is not the value we last wrote, then
    /// something else has written this transform since — and putting OUR idea of its original back
    /// on top of THEIR value is the write war, not the fix. In that case the record is dropped and
    /// the log says so, because a silent skip here would leave a mystery on the next map entry.</para>
    /// </summary>
    private string RestoreTokenScale()
    {
        Transform? t = _tokenScaleOwner;
        Vector3 original = _tokenOriginalScale;
        bool written = _tokenScaleWritten;
        Vector3 lastWritten = _tokenWrittenScale;
        _tokenScaleOwner = null;
        _tokenOriginalScale = Vector3.one;
        _tokenWrittenScale = Vector3.one;
        _tokenScaleWritten = false;

        if (!written)
            return "the party marker's transform was never written (dial at x1.00, or no token was "
                   + "ever found), so there was nothing to put back";
        if (t == null)
            return "the party marker's transform was destroyed under us before it could be restored "
                   + "— normal on a map rebuild, and a rebuilt token is born at its authored scale";
        if ((t.localScale - lastWritten).sqrMagnitude > TokenScaleEpsilonSq)
            return $"the party marker's transform was NOT restored: it now reads {t.localScale} but we "
                   + $"last wrote {lastWritten}, so something else has written it since and putting "
                   + $"our record of {original} back on top of that would be a write war. The record "
                   + "was dropped instead. IF THE MARKER LOOKS WRONG ON THE FLAT 2D MAP AFTER THIS "
                   + "LINE, that other writer is the thing to find";
        t.localScale = original;
        return $"the party marker's transform was restored to its authored localScale {original} "
               + "(recorded before the first write, and the live value still matched what we last "
               + "wrote, so it was still ours to give back)";
    }

    /// <summary>
    /// Put <paramref name="scale"/> on every route <c>LineRenderer</c> we hold, LEVEL-TRIGGERED, and
    /// report how many writes it actually took, how many routes are drawing anything
    /// (<paramref name="drawing"/>), and how many of those are on a layer the head camera renders
    /// (<paramref name="seenByHead"/>) — the last of which is what decides whether this dial can do
    /// anything at all, because the route is the game's own Renderer and is NOT re-drawn by us.
    ///
    /// <para>THIS IS THE ONE PLACE THIS CLASS WRITES A GAME COMPONENT, and the class doc states the
    /// full argument for why <c>widthMultiplier</c> is a number with no other writer in the game.
    /// The protocol here is the part that has to be right:</para>
    /// <list type="number">
    ///   <item>THE ORIGINAL IS RECORDED BEFORE THE FIRST WRITE, once per renderer, into
    ///   <see cref="_pathOriginalWidth"/> — which survives every rescan, so
    ///   <see cref="Release"/> always has the true authored value to put back.</item>
    ///   <item>THE WRITE IS LEVEL-TRIGGERED. A settled frame compares two floats and writes nothing;
    ///   only a changed dial, a re-created renderer or somebody else's write causes an assignment.
    ///   The census prints this count, so "we are in a write war with the game over this property"
    ///   is a number that fails to fall to zero rather than a thing to argue about.</item>
    ///   <item>AT SCALE 1 THE ORIGINAL IS WHAT IS WRITTEN, so the default build leaves every route
    ///   at exactly the width the game authored.</item>
    /// </list>
    /// </summary>
    private int ApplyPathWidth(float scale, int headCullingMask, out int drawing, out int seenByHead)
    {
        drawing = 0;
        seenByHead = 0;
        int writes = 0;
        for (int i = 0; i < _pathRenderers.Count; i++)
        {
            LineRenderer lr = _pathRenderers[i];
            if (lr == null)
                continue;
            if (!_pathOriginalWidth.TryGetValue(lr, out float original))
            {
                original = lr.widthMultiplier;
                _pathOriginalWidth[lr] = original;
            }
            if (lr.enabled && lr.positionCount > 0 && lr.gameObject.activeInHierarchy)
            {
                drawing++;
                // THE QUESTION THIS DIAL RESTS ON. Unlike the icons, the route is NOT re-drawn by
                // this layer — it is an ordinary Renderer, so it only reaches the player's eye if
                // its layer is one the head camera renders. If this count is 0 while 'drawing' is
                // not, the route is invisible in the room and no width dial can change that; the
                // fix would then be the camera's mask, not this factor. Measuring it costs one
                // shift and one AND per route.
                if ((headCullingMask & (1 << lr.gameObject.layer)) != 0)
                    seenByHead++;
            }
            float want = original * scale;
            if (!Mathf.Approximately(lr.widthMultiplier, want))
            {
                lr.widthMultiplier = want;
                writes++;
            }
        }
        return writes;
    }

    /// <summary>
    /// SOURCE TEXELS PER RENDERED PIXEL for one drawn icon — the same quantity
    /// <c>PanelSamplingProbe.Minification</c> reports for panel graphics, computed here because that
    /// probe measures <c>RectTransform</c>s on converted canvases and an icon is a world-space quad
    /// in a command buffer, which it has no way to see.
    ///
    /// <para>THE FORMULA IS COPIED AS A VALUE, deliberately, so the two reports are comparable: the
    /// max of the two axes (shimmer on either axis is shimmer), rendered size taken as the EDGE
    /// LENGTHS between projected corners rather than a bounding box (the quad is yawed on the
    /// parchment, and a box would over-report its width), through the LEFT eye's projection against
    /// the real per-eye render target. No rig-scale correction is applied or needed — the quad and
    /// the camera live in the same ~198-units-per-metre world, and a projection cancels the scale
    /// out. A corner behind the eye is skipped rather than reported, because a viewport point behind
    /// the near plane is mirrored garbage.</para>
    ///
    /// <para>WHAT THE NUMBER DECIDES. Above ~1.35 texels per pixel the surface is dropping source
    /// texels every frame, which is what mipmaps exist to fix and what the card faces were fixed
    /// with. If the icons measure BELOW that and still shimmer, the mip bake is not the answer for
    /// them and the report says so — <see cref="LogIconSampling"/> spells out what each combination
    /// of the printed numbers means.</para>
    /// </summary>
    private void MeasureIcon(Camera head, global::MapLocation? owner, Texture? source, Texture? drawn,
                             Vector3 pos, Quaternion rot, Vector3 scale)
    {
        if (source == null || head == null)
            return;
        if (!EyeTarget(out float eyePxW, out float eyePxH))
            return;

        // The quad's own corners: the mesh is the unit XZ square built in EnsureResources, so the
        // half-extents are scale.x/2 along the yawed right axis and scale.z/2 along the yawed
        // forward axis. Three corners are enough for two edge lengths.
        Vector3 right = rot * Vector3.right * (scale.x * 0.5f);
        Vector3 fwd = rot * Vector3.forward * (scale.z * 0.5f);
        Vector3 c00 = pos - right - fwd;
        Vector3 c10 = pos + right - fwd;
        Vector3 c01 = pos - right + fwd;

        Camera.MonoOrStereoscopicEye eye = UnityEngine.XR.XRSettings.isDeviceActive
            ? Camera.MonoOrStereoscopicEye.Left
            : Camera.MonoOrStereoscopicEye.Mono;
        Vector3 v00 = head.WorldToViewportPoint(c00, eye);
        Vector3 v10 = head.WorldToViewportPoint(c10, eye);
        Vector3 v01 = head.WorldToViewportPoint(c01, eye);
        if (v00.z <= 0f || v10.z <= 0f || v01.z <= 0f)
            return;
        float pxW = PixelDistance(v00, v10, eyePxW, eyePxH);
        float pxH = PixelDistance(v00, v01, eyePxW, eyePxH);
        if (pxW < 1f || pxH < 1f)
            return;

        float texelsW = source.width;
        float texelsH = source.height;
        float min = Mathf.Max(texelsW / Mathf.Max(pxW, 0.01f), texelsH / Mathf.Max(pxH, 0.01f));

        _measuredIcons++;
        if (min >= SuspectMinification)
            _underSampledIcons++;
        bool baked = drawn != null && !ReferenceEquals(drawn, source);
        if (baked)
            _bakedIcons++;
        if (source is Texture2D flat && flat != null)
        {
            if (flat.mipmapCount <= 1 && !baked)
                _miplessIcons++;
        }
        else
        {
            _nonTexture2DIcons++;
        }

        if (min <= _worstMinification)
            return;
        _worstMinification = min;
        Texture? shown = drawn ?? source;
        int mips = shown is Texture2D st && st != null ? st.mipmapCount : 1;
        _worstIconWhat =
            $"'{(owner != null ? owner.name : "(no MapLocation)")}' texture '{source.name}' "
            + $"{texelsW:F0}x{texelsH:F0} texels into {pxW:F0}x{pxH:F0} px = {min:F2}x, "
            + $"mips={mips} {shown.filterMode} aniso {shown.anisoLevel}"
            + (baked ? ", MIP-BAKED" : source is Texture2D ? ", NOT BAKED" : ", not a Texture2D");
    }

    /// <summary>The per-eye render target this frame, in pixels — copied as a value from
    /// <c>PanelSamplingProbe</c> so both reports divide by the same denominator. Falls back to the
    /// desktop window when XR is not running, which is what makes the number readable in a
    /// flat-screen dev run.</summary>
    private static bool EyeTarget(out float pxW, out float pxH)
    {
        float viewport = Mathf.Clamp(UnityEngine.XR.XRSettings.renderViewportScale, 0.01f, 1f);
        int w = UnityEngine.XR.XRSettings.eyeTextureWidth;
        int h = UnityEngine.XR.XRSettings.eyeTextureHeight;
        if (w < 2 || h < 2)
        {
            w = Screen.width;
            h = Screen.height;
            viewport = 1f;
        }
        pxW = w * viewport;
        pxH = h * viewport;
        return pxW >= 1f && pxH >= 1f;
    }

    /// <inheritdoc cref="EyeTarget"/>
    private static float PixelDistance(Vector3 a, Vector3 b, float eyePxW, float eyePxH)
    {
        float dx = (b.x - a.x) * eyePxW;
        float dy = (b.y - a.y) * eyePxH;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// The icon census — printed on the FIRST tick that produced a buffer and on every later tick
    /// whose numbers differ from the last one printed (throttled).
    ///
    /// <para>It prints AT ZERO. The pre-fix version only reported when it had drawn at least one
    /// icon, which makes "the scan ran and the map genuinely has no icons yet" and "the scan never
    /// ran / found nothing it could read" the same silence in the log. Those are opposite bugs and
    /// the line now names which one it is.</para>
    ///
    /// <para>EVERY DIAL IS NAMED EVERY TIME, WITH ITS VALUE AND ITS HIT COUNT, AND SO IS THE MAP ON
    /// SCREEN. With one dial per icon population (class doc) at most two of the three can hit
    /// anything in a given frame, so a dial reading "hit 0" is the NORMAL state of the other map's
    /// dial — and "this dial does nothing" and "there is nothing for this dial to do" are opposite
    /// bugs that only this line separates. The rule for reading it: a zero-hit dial is FINE when
    /// the surface named at the front of the line is not the surface that dial governs, and is a
    /// REAL FAULT when it is.</para>
    /// </summary>
    private void LogCensus(int drawn, int tokenDraws, int capitalHits, int cityHits, int ownerless,
                           float planeY, MapSurface surface,
                           float worldScale, float capitalScale, float cityScale,
                           float markerScale, int tokenWrites,
                           float pathScale, int pathWrites, int pathDrawing, int pathSeen,
                           int arrivals, int deferredBakes, int prewarmed)
    {
        int signature = drawn * 397
                        ^ tokenDraws * 31
                        ^ capitalHits * 7
                        ^ cityHits * 11
                        ^ ownerless * 3
                        ^ (int)surface * 1021
                        ^ worldScale.GetHashCode()
                        ^ capitalScale.GetHashCode()
                        ^ cityScale.GetHashCode()
                        ^ markerScale.GetHashCode()
                        ^ _tokenKinds.Length * 613
                        ^ (_tokenScaleWritten ? 2749 : 0)
                        ^ pathScale.GetHashCode()
                        ^ _pathRenderers.Count * 17
                        ^ pathDrawing * 53
                        ^ pathSeen * 59
                        // pathWrites and arrivals/deferrals are TRANSIENTS: including them in the
                        // signature would make the line re-print on every settled frame in which one
                        // of them happens to be non-zero, which is the opposite of a change report.
                        // They are still PRINTED — the throttle is what keeps that affordable.
                        ^ (deferredBakes > 0 ? 7919 : 0);
        if (_censusPrinted && signature == _censusSignature)
            return;
        float now = Time.unscaledTime;
        if (_censusPrinted && now < _censusNextAllowed)
            return;
        _censusSignature = signature;
        _censusPrinted = true;
        _censusNextAllowed = now + CensusMinIntervalSeconds;

        int worldHits = drawn - capitalHits - cityHits;
        string shown = surface switch
        {
            MapSurface.City => "CITY (Gloomhaven)",
            MapSurface.World => "WORLD",
            _ => "UNKNOWN — no MapChoreographer, or both map objects are down mid-transition; the "
                 + "world map's dials are used, which is what shipped before the split",
        };
        bool city = surface == MapSurface.City;
        VRLog.Info(Scope,
            $"MAP ROOM icons: {drawn} location icon(s) + {tokenDraws} party-token submesh draw(s) queued "
            + $"for the head camera at y={planeY:F2} ({IconLiftWorld:F2} above the parchment top), drawn at "
            + $"{IconEvent}. Footprint = decal lossyScale.xz at the decal's own yaw, times the size dial. "
            + $"MAP SHOWN: {shown} (MapChoreographer.worldMap/cityMap, whichever is activeInHierarchy — the "
            + "same test MapParchment uses to pick the parchment, so the two cannot disagree). "
            + "SIZE — one dial per icon population, ALL THREE listed every time so a zero is readable: "
            + $"[MapRoom] IconScale x{worldScale:F2} hit {worldHits} icon(s) (world map, everything but the "
            + $"capital); [MapRoom] GloomhavenIconScale x{capitalScale:F2} hit {capitalHits} icon(s) (world "
            + "map, the location whose MapLocation.MapLocationType is Headquarters — exactly one exists per "
            + $"map, see MapChoreographer.HeadquartersLocation); [MapRoom] CityIconScale x{cityScale:F2} hit "
            + $"{cityHits} icon(s) (city map, ALL of its icons: the shopfronts in m_CityLocations plus the "
            + "City-type quests). "
            + $"{ownerless} drawn icon(s) had NO owning MapLocation; they take the dial of the map on screen. "
            + $"PARTY MARKER: [MapRoom] PartyMarkerScale x{markerScale:F2} on {tokenDraws} submesh draw(s). "
            + "SINCE ModBuild 195 THIS DIAL WRITES A GAME TRANSFORM — PartyToken.transform.localScale — "
            + "and that is the SECOND of the two game-component writes this layer makes (the route width is "
            + "the other). It was moved there because ModBuild 194's draw-matrix route could not touch this "
            + "token at all: 194 needed a mesh to hand CommandBuffer.DrawMesh, this token's renderer has "
            + $"none, and Unity {Application.unityVersion}'s CommandBuffer.DrawRenderer has no matrix "
            + "overload to fall back on (checked against the shipping UnityEngine.CoreModule.dll, not from "
            + $"memory). WHAT THE TOKEN IS, so this is never guessed again: {_tokenKinds}. "
            + $"OWNERSHIP: authored localScale recorded before the first write as {_tokenOriginalScale}, "
            + $"target = that x {markerScale:F2}, {tokenWrites} write(s) this tick, "
            + $"{(_tokenScaleWritten ? $"last written value {_tokenWrittenScale}" : "never written yet (x1.00 wants the authored value, so a default install writes nothing at all)")}, "
            + "restored on stand-down and on teardown and ONLY while the live value is still the one we "
            + "wrote. A tokenWrites that never falls to 0 on a settled frame means a second writer of this "
            + "transform appeared and we are in a write war — that, and not the dial, would be the bug. "
            + (_tokenRoot == null
                ? "NO PARTY TOKEN ON THE CHOREOGRAPHER RIGHT NOW, so this dial has nothing to act on this "
                  + "tick — the scan RAN and found no m_PartyToken. WHAT TO DO ABOUT IT: nothing, if the map "
                  + "is still opening (the token is created with the map); if this persists while the marker "
                  + "is visibly on the parchment, the marker being drawn is not the choreographer's token and "
                  + "THAT is the thing to find. "
                : tokenDraws == 0
                    ? "THE TOKEN EXISTS BUT DREW NOTHING THIS TICK: every renderer under it is disabled or "
                      + "inactive, so the dial is being applied to a marker nobody can see. WHAT TO DO ABOUT "
                      + "IT: this is expected on the city map, where the party marker is hidden; on the world "
                      + "map it means the marker you are looking at belongs to some other object. "
                    : string.Empty)
            + $"ROUTE: [MapRoom] PathWidthScale x{pathScale:F2} on {_pathRenderers.Count} LineRenderer(s) "
            + $"({(_pathFromFallback ? "found by the FALLBACK sweep of the active map object — the lineRenderers "
                                     + "holder is not parented under its MapLocation in this build"
                                     : "found under the MapLocations themselves")}), "
            + $"{pathDrawing} of them drawing anything right now, of which {pathSeen} sit on a layer THIS HEAD "
            + $"CAMERA RENDERS, and {pathWrites} widthMultiplier write(s) this tick. "
            + "THIS IS THE ONE GAME COMPONENT WRITE THIS LAYER MAKES: the original multiplier is recorded "
            + "before the first write and restored on stand-down, and the write is level-triggered — so "
            + "pathWrites should be 0 on a settled frame. A pathWrites that never falls to 0 means something "
            + "else started writing widthMultiplier and we are in a write war. "
            + (pathDrawing > 0 && pathSeen == 0
                ? "ROUTES DRAW BUT NONE IS ON A RENDERED LAYER: the route is invisible in the room and no width "
                  + "dial can change that — the fix would be the head camera's culling mask, not this factor. "
                : string.Empty)
            + (_pathRenderers.Count == 0
                ? "ZERO ROUTE RENDERERS: neither the MapLocations nor the active map object held a "
                  + "LineRenderer, so PathWidthScale has nothing to act on. That is a finding, not a silent "
                  + "scan — the scan RAN this tick. "
                : string.Empty)
            + $"MIP BAKE: {arrivals} texture arrival(s) seen this tick, {deferredBakes} bake(s) deferred to a "
            + $"later frame, {prewarmed} first-ask(s) spent PRE-WARMING the map that is NOT on screen (cap "
            + $"{MaxIconBakesPerFrame} first-asks/frame in total, visible icons served first — a deferred icon "
            + "is drawn from its ORIGINAL texture, aliased but on time, never held back). Budget now "
            + Cards.CardFaceMipBake.BudgetSummary + ". "
            + "HOW TO READ A ZERO: "
            + (city
                ? "the CITY map is up, so IconScale and GloomhavenIconScale hitting 0 is CORRECT and expected "
                  + "(their population is deactivated, MapChoreographer.RefreshShownLocationsByMap → "
                  + "MapLocation.HideLocation → SetActive(false)); CityIconScale at 0 with drawn>0 would be "
                  + "the real fault."
                : "the WORLD map is up, so CityIconScale hitting 0 is CORRECT and expected (the city "
                  + "shopfronts are deactivated); IconScale at 0 with drawn>0 would be the real fault. "
                  + "GloomhavenIconScale at 0 means the capital's own location object is inactive right now — "
                  + "the separate Gloomhaven dial has nothing to act on and this report says so rather than "
                  + "leaving it to be guessed.")
            + (drawn == 0
                ? " ZERO ICONS ALTOGETHER is a real state, not a silent scan: the decal scan RAN this tick and "
                  + "found nothing it could draw. Either the map has no active locations yet (fresh campaign, "
                  + "mid-InitMap) or every decal's CurrentMaterial carried no texture — the two are told "
                  + "apart by the MAP ROOM location input line, which counts MapLocation components."
                : string.Empty));
    }

    /// <summary>
    /// THE ALIASING REPORT — the icons' measured sampling rate, printed every
    /// <see cref="SamplingReportIntervalSeconds"/> so the next hardware log DECIDES the question
    /// with numbers instead of an opinion.
    ///
    /// <para>HOW TO READ IT, in the order the numbers settle the question:</para>
    /// <list type="number">
    ///   <item><b>mips=1 on the worst icon</b> — the texture has NO mip chain and the aliasing is
    ///   the ordinary undersampling the card faces had. That is what the bake fixes, and the same
    ///   line will read <c>MIP-BAKED</c> once the bake has landed for that texture.</item>
    ///   <item><b>mips&gt;1 and still MIPLESS-looking aliasing</b> — the textures were ALREADY
    ///   mipped and the bake is refused by the cache (it refuses an already-mipped source by
    ///   design). Then the cause is one of the other three the report separates: a
    ///   <c>Point</c>/<c>Bilinear</c> filter mode (printed), <c>aniso 0-1</c> on a surface seen at a
    ///   grazing angle across a table (printed), or a minification so extreme that even a mip chain
    ///   shimmers between levels. In that case the fix is NOT another bake.</item>
    ///   <item><b>the minification number itself</b> — at or above 1.35x the surface is dropping
    ///   source texels every frame. BELOW 1.35x with a visible shimmer, texture sampling is measured
    ///   OUT as the cause for the icons, exactly as it was measured out for the panels at
    ///   ModBuild 191, and the next round must look elsewhere (the quad's own size against its texel
    ///   density, or the parchment underneath it).</item>
    ///   <item><b>0 icon(s) measured</b> — nothing was drawn, or every icon's corners projected
    ///   behind the eye / smaller than a pixel. It is not a silent failure: the census line printed
    ///   just before this one says how many icons were drawn at all.</item>
    /// </list>
    /// </summary>
    private void LogIconSampling()
    {
        VRLog.Info(Scope,
            $"MAP ICON SAMPLING: {_measuredIcons} drawn icon(s) measured, {_underSampledIcons} at or above "
            + $"{SuspectMinification:F2}x minification (source texels per rendered pixel, max of the two axes, "
            + "through the LEFT eye's projection against the real per-eye target — the same quantity "
            + "PANEL SAMPLING reports, so the two are comparable). "
            + $"{_miplessIcons} icon(s) are still drawn from a MIPLESS texture, {_bakedIcons} from a MIP-BAKED "
            + $"copy, {_nonTexture2DIcons} from something that is not a Texture2D and can never be baked. "
            + $"WORST: {(_worstIconWhat.Length > 0 ? _worstIconWhat : "none — nothing measured this tick")}. "
            + "HOW TO READ IT: mips=1 on the worst icon means no mip chain and the bake is the fix; mips>1 "
            + "means the art was ALREADY mipped, the cache refuses it by design, and the cause is the printed "
            + "filter mode, the printed aniso level, or a minification so extreme that a mip chain still "
            + $"shimmers. Below {SuspectMinification:F2}x with a visible shimmer, texture sampling is measured "
            + "OUT for the icons and the next round must look at the quad's size against its texel density, "
            + "not at another bake. "
            + $"Bake budget now {Cards.CardFaceMipBake.BudgetSummary}.");
    }

    /// <summary>
    /// The render-order decision, printed ONCE per attach with the numbers it rests on — so the
    /// next hardware log proves which path is live instead of leaving it to be inferred from a
    /// screenshot. Everything it states is measured or a compile-time constant of this file.
    /// </summary>
    private void LogRenderOrder(Camera head)
    {
        int queue = _mat != null ? _mat.renderQueue : -1;
        float liftMm = IconLiftWorld / WorldUnitsPerMetre * 1000f;
        int windVisible = CountWindParticlesInMask(head, out int windTotal);
        VRLog.Info(Scope,
            $"MAP ROOM icon layer attached to '{head.name}' at {IconEvent} (was AfterForwardAlpha through "
            + "ModBuild 188). ORDER IS THE MECHANISM: the mod's floated windows are world-space uGUI panels "
            + "that write NO depth by design (CanvasConversion 'PANEL DRAW ORDER'), so a panel can only ever "
            + "cover the icons by being PAINTED AFTER them — and panels paint in the forward ALPHA queue. "
            + "Drawing at AfterForwardAlpha put the icons after every panel, which is the reported "
            + "'Symbole durch die mouseovers durch'; drawing before it hands the order back to the panels. "
            + $"The icon material's renderQueue is {queue} and is INERT for a CommandBuffer draw (the command "
            + "is issued at this event, never entered into a sorted queue) — the event decides alone. "
            + $"ZWrite stays OFF: the icons sit {IconLiftWorld:F2} world units = {liftMm:F2} mm (real, at "
            + $"{WorldUnitsPerMetre:F0} units/m) above an OPAQUE parchment that writes depth, so 'a panel "
            + "behind the icons' is not a reachable pose. No depth clear, as before. "
            + "MultiPass: the buffer hangs on the CAMERA and is replayed per eye pass from one recording of "
            + "eye-independent world-space matrices, so it cannot land in one eye only. "
            + $"THE ONE THING THIS MOVE COSTS, MEASURED: {windVisible} of {windTotal} Wind/Clouds ambiance "
            + "particle system(s) in the scene sit on a layer this head camera renders. Those particles draw "
            + "in the forward alpha queue, i.e. now AFTER the icons — so any of them BETWEEN the eye and an "
            + "icon will smear over it, where at AfterForwardAlpha the icons always won. That is the correct "
            + "perspective and it is what was asked for, but it is also the mechanism behind the older "
            + "'dicke Schlieren über den Ortssymbolen' report on the FLAT path, whose remedy is "
            + "[WorldUI] MapWindOpacity — which the room does NOT apply (the flat map render, and with it "
            + "TuneMapWindParticles, stands down while MapRoomOwnsParchment). "
            + (windVisible == 0
                ? "AT ZERO the question is closed: no wind system is on a rendered layer, so nothing can "
                  + "smear and the move costs nothing at all."
                : "ABOVE ZERO it is an open observable: if the icons now read as smeared in the room, the "
                  + "answer is to dim these systems in the room too, NOT to move this buffer back."));
    }

    /// <summary>
    /// How many of the map's Wind/Clouds ambiance particle systems sit on a layer
    /// <paramref name="head"/> actually renders — the one number that decides whether moving the
    /// icons in front of the transparent queue costs anything.
    ///
    /// <para>Read-only and run ONCE per attach (i.e. once per entry into the map room), never per
    /// frame. The match is the flat path's own, copied as a value rather than re-derived: material
    /// name OR GameObject name containing "Wind"/"Cloud" (<c>FlatScreenStereo.TuneMapWindParticles</c>
    /// records that a root-scoped, GO-name-only scan found 0, which is why both names are tested and
    /// the sweep is scene-wide).</para>
    /// </summary>
    private static int CountWindParticlesInMask(Camera head, out int total)
    {
        total = 0;
        int visible = 0;
        ParticleSystemRenderer[] all = Object.FindObjectsOfType<ParticleSystemRenderer>();
        for (int i = 0; i < all.Length; i++)
        {
            ParticleSystemRenderer r = all[i];
            if (r == null)
                continue;
            Material? m = r.sharedMaterial;
            string mn = m != null ? m.name : string.Empty;
            string gn = r.gameObject.name;
            bool isWind = mn.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0
                          || mn.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0
                          || gn.IndexOf("Wind", System.StringComparison.OrdinalIgnoreCase) >= 0
                          || gn.IndexOf("Cloud", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (!isWind)
                continue;
            total++;
            if ((head.cullingMask & (1 << r.gameObject.layer)) != 0)
                visible++;
        }
        return visible;
    }

    /// <summary>Every size dial, clamped in code as well as at the bind site — see
    /// <see cref="MinIconScale"/> for why the floor is not optional. The floor is what keeps the
    /// standing ruling true for the new city dial too: its minimum shrinks the shopfront icons, it
    /// can never erase them, so no value of any of these dials can make the merchant unreachable.</summary>
    private static float ClampScale(float v) =>
        float.IsNaN(v) ? 1f : Mathf.Clamp(v, MinIconScale, MaxIconScale);

    /// <summary>
    /// Is this the GLOOMHAVEN icon — the capital's own marker?
    ///
    /// <para>HOW THE GLOOMHAVEN SYMBOL IS IDENTIFIED, AND WHY THAT IS ROBUST. Not by name, not by
    /// texture, not by a location ID string: by the game's own classification. <c>MapLocation.Init</c>
    /// assigns <c>MapLocationType = EMapLocationType.Headquarters</c> exactly when the location state
    /// it was initialised with is a <c>CHeadquartersState</c> (MapLocation.cs, the
    /// <c>if (!(location is CHeadquartersState))</c> ladder), and the headquarters IS Gloomhaven —
    /// the game says so in its own field names, e.g. <c>MapChoreographer</c> plays
    /// <c>m_OutroGloomhavenLines</c> on the branch guarded by
    /// <c>MovingToLocation.MapLocationType == Headquarters</c>. There is exactly ONE per map:
    /// <c>MapChoreographer.HeadquartersLocation</c> is
    /// <c>m_Villages.SingleOrDefault(x =&gt; x.MapLocationType == Headquarters)</c>, and a
    /// <c>SingleOrDefault</c> the game itself relies on is a stronger uniqueness statement than
    /// anything this mod could assert. The test therefore survives translation (no display string),
    /// re-authored art (no texture or material name), a renamed prefab, and any save state.</para>
    ///
    /// <para>The owner lookup is <c>GetComponentInParent</c>, which this codebase otherwise warns
    /// about because containment is not identity. Here containment IS the question being asked:
    /// <c>MapLocation.Init</c> instantiates the decal prefab as a child of its own serialized
    /// <c>MeshParent</c>, so "the MapLocation above this decal" is precisely "the location this
    /// decal was spawned to draw". A decal with no MapLocation above it (the scene-wide fallback
    /// sweep can find such) is not the capital and takes the general dial.</para>
    /// </summary>
    private static bool IsCapital(global::MapLocation? owner) =>
        owner != null && owner.MapLocationType == global::MapLocation.EMapLocationType.Headquarters;

    /// <summary>
    /// The <c>MapLocation</c> that spawned this decal, or null. See <see cref="IsCapital"/> for why
    /// containment is the right question here.
    ///
    /// <para><c>includeInactive: true</c> IS LOAD-BEARING SINCE THE SCAN STARTED HOLDING INACTIVE
    /// DECALS. The parameterless <c>GetComponentInParent&lt;T&gt;()</c> skips inactive parents, so a
    /// decal belonging to the map that is currently hidden would resolve to NO owner — and then, for
    /// the frames between a world↔city switch and the next rescan, the capital's marker would be
    /// classified as an ordinary icon and take the wrong dial. Asking for inactive parents makes the
    /// owner correct from the moment the decal exists, whichever map it belongs to.</para>
    /// </summary>
    private static global::MapLocation? OwnerOf(Component? decal) =>
        decal != null ? decal.GetComponentInParent<global::MapLocation>(includeInactive: true) : null;

    /// <summary>
    /// Detach the buffer, destroy everything this layer owns, and PUT BACK every route
    /// <c>widthMultiplier</c> that was written. Idempotent.
    ///
    /// <para>The restore runs FIRST and before anything else is cleared, because it is the only step
    /// that touches a game component and the class doc promises it: a route left at a modded width
    /// after the room has stood down would show on the flat 2D map, which this feature explicitly
    /// does not govern. Destroyed renderers are fake-null and skipped; the dictionary is cleared
    /// either way so a stale original can never be re-applied to a recycled object.</para>
    /// </summary>
    internal void Release(string reason)
    {
        bool had = _cam != null;
        int restored = RestorePathWidths();
        // The token restore runs in the SAME place and for the same reason as the route restore:
        // before anything is cleared, because both are writes to game components that must not
        // survive the room. It returns words rather than a bool — "never written", "destroyed under
        // us" and "somebody else owns it now" are three different situations and only one of them is
        // a fault, so collapsing them would hide the one that matters.
        string tokenRestore = RestoreTokenScale();
        Detach();
        if (_cmd != null)
        {
            _cmd.Release();
            _cmd = null;
        }
        if (_quad != null)
        {
            Object.Destroy(_quad);
            _quad = null;
        }
        if (_mat != null)
        {
            Object.Destroy(_mat);
            _mat = null;
        }
        _decals.Clear();
        _decalRenderers.Clear();
        _decalOwners.Clear();
        _seenTexByDecal.Clear();
        // BakeAsked is deliberately NOT cleared — see its own doc. It rations access to a cache with
        // session lifetime, so it must have session lifetime too, or every re-entry into the room
        // would meter already-baked icons back in two per frame.
        _tokenRenderers.Clear();
        _tokenRoot = null;
        _tokenKinds = "<not scanned>";
        _pathRenderers.Clear();
        _pathFromFallback = false;
        _locationScratch.Clear();
        _lineScratch.Clear();
        _mpbPool.Clear();
        _scanFrame = int.MinValue;
        _warmFramesLeft = 0;
        _lastDrawnCount = -1;
        _censusSignature = 0;
        _censusPrinted = false;
        _censusNextAllowed = 0f;
        _samplingNextAllowed = 0f;
        _measureThisTick = false;
        _worstMinification = 0f;
        _worstIconWhat = string.Empty;
        _measuredIcons = 0;
        _underSampledIcons = 0;
        _miplessIcons = 0;
        _bakedIcons = 0;
        _nonTexture2DIcons = 0;
        // The surface latch is STATIC (the hover pads share it), so it outlives this instance and
        // must be invalidated here or a pad posed on the first frame of the NEXT map room could be
        // sized from the map of the last one. Clearing the flag, not the value: the next reader
        // re-resolves from the live choreographer.
        _surfaceResolved = false;
        _surface = MapSurface.Unknown;
        DrawnCount = 0;
        TokenDrawCount = 0;
        if (had)
            VRLog.Info(Scope, $"MAP ROOM icon layer released ({reason}) — command buffer detached, mesh and "
                              + $"material destroyed, and {restored} route LineRenderer(s) put back to their "
                              + "authored widthMultiplier. THIS LAYER WRITES EXACTLY TWO GAME COMPONENTS and "
                              + "both are undone here: the route widths ([MapRoom] PathWidthScale) just named, "
                              + $"and the party marker ([MapRoom] PartyMarkerScale) — {tokenRestore}. Everything "
                              + "else (the decals, the token's renderers, every texture) was only READ. A route "
                              + "number here below the route count in the last census means renderers were "
                              + "destroyed under us, which is normal on a map rebuild and not a leak. "
                              + "MULTIPLAYER: both restores are what keep this a purely local presentation — "
                              + "nothing was ever sent, and after this line the game's own map objects are back "
                              + "to exactly the state a peer's copy is in.");
    }

    /// <summary>Put every recorded route width back and forget the records. Returns how many
    /// renderers were still alive to receive it — the census's route count minus this number is how
    /// many the game destroyed under us, which is normal on a map rebuild.</summary>
    private int RestorePathWidths()
    {
        int restored = 0;
        foreach (KeyValuePair<LineRenderer, float> kv in _pathOriginalWidth)
        {
            LineRenderer lr = kv.Key;
            if (lr == null)
                continue;
            lr.widthMultiplier = kv.Value;
            restored++;
        }
        _pathOriginalWidth.Clear();
        return restored;
    }

    private void Detach()
    {
        if (_cam != null && _cmd != null)
            _cam.RemoveCommandBuffer(IconEvent, _cmd);
        _cam = null;
    }

    private bool EnsureResources()
    {
        if (_decalTypeMissing)
            return false;
        if (_decalType == null)
        {
            // The Decal type lives in an unreferenced assembly (ThreeEyedGames Decalicious) —
            // reached by name, exactly as the flat path does.
            _decalType = HarmonyLib.AccessTools.TypeByName("Decal");
            if (_decalType == null)
            {
                _decalTypeMissing = true;
                VRLog.Warn(Scope, "MAP ROOM icons: the Decalicious 'Decal' type is not present — location " +
                                  "icons cannot be re-drawn and the map will show the parchment only.");
                return false;
            }
            _decalCurMatProp = _decalType.GetProperty("CurrentMaterial");
        }
        _cmd ??= new CommandBuffer { name = "GloomhavenVR.MapRoom.Icons" };
        if (_quad == null)
        {
            _quad = new Mesh { name = "GloomhavenVR.MapRoom.IconQuad" };
            _quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
            };
            _quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            _quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _quad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _quad.RecalculateBounds();
        }
        if (_mat == null)
        {
            Shader? sh = Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
            if (sh == null)
            {
                _decalTypeMissing = true;
                VRLog.Warn(Scope, "MAP ROOM icons: no Unlit/Transparent or Sprites/Default shader — icons skipped.");
                return false;
            }
            _mat = new Material(sh) { name = "GloomhavenVR.MapRoom.IconMat" };
            // DOCUMENTATION ONLY — this number changes nothing on this path. A material's render
            // queue orders draws inside the camera's own sorted queues; a CommandBuffer.DrawMesh is
            // issued where the buffer is attached (see IconEvent) and is never entered into them.
            // Kept at the value it shipped with so a .cfg-less diff of this file stays honest about
            // what this change did and did not touch; the class doc says why it is inert.
            _mat.renderQueue = 4000;
        }
        return true;
    }

    /// <summary>
    /// Re-collect the decals, the token's renderers and the route line renderers.
    ///
    /// <para>THE CADENCE IS THE LATE-LOADING FIX, half of it. In the settled state this still runs
    /// every <see cref="RescanIntervalFrames"/>; while the population is still changing it runs
    /// EVERY FRAME (<see cref="WarmupHoldFrames"/>), because a decal that has just been instantiated
    /// is otherwise invisible to this layer for up to 15 frames and then has to wait again for its
    /// material's texture. The other half is <c>includeInactive: true</c> below: a decal the game's
    /// loader has spawned but not yet enabled is now HELD and BAKED before it is ever drawn, exactly
    /// as <c>PanelMipBake</c> swaps a panel graphic while the loader still has it disabled.</para>
    /// </summary>
    private void Rescan(global::MapChoreographer choreo)
    {
        bool due = _scanFrame == int.MinValue
                   || _warmFramesLeft > 0
                   || Time.frameCount - _scanFrame >= RescanIntervalFrames;
        if (!due)
        {
            for (int i = 0; i < _decals.Count && !due; i++)
                due = _decals[i] == null; // a destroyed decal forces an early rescan
        }
        if (!due)
            return;
        _scanFrame = Time.frameCount;
        _decals.Clear();
        _decalRenderers.Clear();
        _decalOwners.Clear();
        Collect(choreo.m_ScenariosParent);
        Collect(choreo.m_VillagesParent);
        if (_decals.Count == 0 && _decalType != null)
        {
            // Safety net for a save/version that parents its map icons elsewhere — the same
            // scene-wide sweep the flat path keeps for that case. includeInactive: true for the same
            // reason Collect uses it: a decal that is not enabled YET is exactly the one we want to
            // have baked by the time it is.
            UnityEngine.Object[] all = Object.FindObjectsOfType(_decalType, includeInactive: true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c)
                    continue;
                _decals.Add(c);
                _decalRenderers.Add(c.GetComponent<Renderer>());
                _decalOwners.Add(OwnerOf(c));
            }
        }
        _tokenRenderers.Clear();
        _tokenRoot = null;
        PartyToken? token = choreo.m_PartyToken;
        if (token != null)
        {
            _tokenRoot = token.transform;
            token.GetComponentsInChildren(includeInactive: false, _tokenRenderers);
            _tokenKinds = DescribeTokenRenderers();

            // THE AUTHORED SCALE IS RECORDED HERE, BEFORE ANY WRITE — the first step of the protocol
            // in the class doc. Guarded on transform IDENTITY rather than on a "have we recorded"
            // flag: the game destroys and rebuilds the token across a map rebuild, and a record taken
            // from the previous instance would be restored onto the new one. The identity test also
            // means a re-scan of the SAME token (which happens every frame during the warm-up hold)
            // can never re-record OUR OWN written value as if it were the authored one — the bug that
            // would have made the restore a no-op and left the marker permanently resized.
            if (!ReferenceEquals(_tokenRoot, _tokenScaleOwner))
            {
                RestoreTokenScale();               // hand the PREVIOUS token back before adopting this one
                _tokenScaleOwner = _tokenRoot;
                _tokenOriginalScale = _tokenRoot.localScale;
                _tokenWrittenScale = _tokenOriginalScale;
                _tokenScaleWritten = false;
            }
        }
        else
        {
            _tokenKinds = "no PartyToken on the MapChoreographer yet";
        }
        CollectPathRenderers(choreo);
    }

    /// <summary>
    /// NAME WHAT THE TOKEN'S RENDERERS ACTUALLY ARE, once per scan, for the census.
    ///
    /// <para>THIS IS THE ANSWER ModBuild 194's LOG COULD NOT GIVE. That build could only say "1 draw
    /// could NOT take the dial" — true, but it left the reader choosing between two causes
    /// (skinned / no <c>MeshFilter</c>) that would have needed different fixes, and the choice cost a
    /// hardware round trip. The scale no longer depends on the answer, but the answer is still worth
    /// printing: it is what makes "the marker is one skinned figure" a fact in the log rather than an
    /// inference, and it is the first thing to look at if the marker ever stops responding again.</para>
    ///
    /// <para>Built at SCAN time and cached in <see cref="_tokenKinds"/>: a runtime type name and a
    /// <c>GetComponent</c> per renderer is nothing once per scan and is not nothing per frame.</para>
    /// </summary>
    private string DescribeTokenRenderers()
    {
        if (_tokenRenderers.Count == 0)
            return "no Renderer under the token at all";
        var sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < _tokenRenderers.Count; i++)
        {
            Renderer? r = _tokenRenderers[i];
            if (i > 0)
                sb.Append(", ");
            if (r == null)
            {
                sb.Append("<destroyed>");
                continue;
            }
            MeshFilter? mf = r.GetComponent<MeshFilter>();
            bool hasMesh = mf != null && mf.sharedMesh != null;
            sb.Append('\'').Append(r.name).Append("' is a ").Append(r.GetType().Name)
              .Append(hasMesh ? " WITH a MeshFilter mesh" : " with NO MeshFilter mesh");
        }
        return sb.ToString();
    }

    /// <summary>
    /// Every <c>LineRenderer</c> that draws a route on the map — the ACTIVE path to the selected
    /// destination and the permanent VILLAGE ROADS, which the game builds from the same prefab under
    /// the same per-location holder (decompiled MapLocation.cs:435-438 and :1021).
    ///
    /// <para>PRIMARY ROUTE: from the locations themselves. <c>MapLocation</c>'s serialized
    /// <c>lineRenderers</c> holder is a child of the location prefab, so
    /// <c>GetComponentsInChildren</c> on each location finds exactly its own two populations and
    /// nothing else — no other kind of line can be swept up by accident.</para>
    ///
    /// <para>FALLBACK: if that finds nothing at all, the holder is not parented under its location in
    /// this build, and the sweep falls back to the ACTIVE MAP object. That is still bounded to the
    /// map — it can never reach the in-scenario movement lines, which live on a different root — and
    /// the census says which of the two routes answered, so a zero is attributable rather than a
    /// mystery.</para>
    /// </summary>
    private void CollectPathRenderers(global::MapChoreographer choreo)
    {
        _pathRenderers.Clear();
        _pathFromFallback = false;
        CollectLocations(choreo.m_ScenariosParent);
        CollectLocations(choreo.m_VillagesParent);
        for (int i = 0; i < _locationScratch.Count; i++)
        {
            global::MapLocation loc = _locationScratch[i];
            if (loc == null)
                continue;
            _lineScratch.Clear();
            loc.GetComponentsInChildren(includeInactive: true, _lineScratch);
            for (int j = 0; j < _lineScratch.Count; j++)
            {
                if (_lineScratch[j] != null)
                    _pathRenderers.Add(_lineScratch[j]);
            }
        }
        _locationScratch.Clear();
        if (_pathRenderers.Count > 0)
            return;

        GameObject? map = MapParchment.ResolveActiveMapGo(choreo, out bool _);
        if (map == null)
            return;
        _lineScratch.Clear();
        map.GetComponentsInChildren(includeInactive: true, _lineScratch);
        for (int j = 0; j < _lineScratch.Count; j++)
        {
            if (_lineScratch[j] != null)
                _pathRenderers.Add(_lineScratch[j]);
        }
        _pathFromFallback = _pathRenderers.Count > 0;
    }

    /// <summary>Append every <c>MapLocation</c> under <paramref name="rootGo"/> to the scan scratch.
    /// <c>includeInactive: true</c> so a location the game has hidden still has its route width
    /// re-asserted — otherwise a road would snap back to its authored width for the frames between
    /// being re-shown and the next scan.</summary>
    private void CollectLocations(GameObject? rootGo)
    {
        if (rootGo == null)
            return;
        LocationBuffer.Clear();
        rootGo.GetComponentsInChildren(includeInactive: true, LocationBuffer);
        for (int i = 0; i < LocationBuffer.Count; i++)
        {
            if (LocationBuffer[i] != null)
                _locationScratch.Add(LocationBuffer[i]);
        }
    }

    /// <summary>Shared scratch for <see cref="CollectLocations"/> — one list for the whole class so
    /// the per-scan sweep allocates nothing.</summary>
    private static readonly List<global::MapLocation> LocationBuffer = new(64);

    private void Collect(GameObject? rootGo)
    {
        if (rootGo == null || _decalType == null)
            return;
        // includeInactive: TRUE since ModBuild 194 — the late-loading half of the icon report. The
        // draw loop still refuses to draw anything that is not activeInHierarchy, so this changes
        // nothing about the picture; what it changes is that a decal the game has spawned but not
        // yet enabled is already held, already watched and already mip-baked on the frame it is
        // finally shown, instead of being discovered by some later scan.
        Component[] found = rootGo.GetComponentsInChildren(_decalType, includeInactive: true);
        for (int i = 0; i < found.Length; i++)
        {
            _decals.Add(found[i]);
            _decalRenderers.Add(found[i] != null ? found[i].GetComponent<Renderer>() : null);
            // Resolved at SCAN time rather than per frame — the parent chain of a decal never
            // changes, only the TYPE the owner reports does, and that is re-read in the draw loop.
            // Keeps the per-frame path free of GetComponentInParent walks.
            _decalOwners.Add(OwnerOf(found[i]));
        }
    }

    private MaterialPropertyBlock RentMpb(int slot)
    {
        while (_mpbPool.Count <= slot)
            _mpbPool.Add(new MaterialPropertyBlock());
        return _mpbPool[slot];
    }
}
