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
/// — dabei soll die Größe des Gloomhaven-Symbols gesondert eingestellt werden können"). Two live
/// factors multiply the quad footprint and nothing else: <c>[MapRoom] IconScale</c> for every
/// location icon and <c>[MapRoom] GloomhavenIconScale</c> for the capital's own icon. They are
/// read on every tick, so a dial turned in the menu is visible on the next frame without a reload,
/// and they exist only on this path — the flat map's icon draw
/// (<c>FlatScreenStereo.3.Map.DrawMapIcons</c>) never sees them. See
/// <see cref="IsCapital"/> for how the Gloomhaven icon is identified, and
/// <see cref="ScaleForDrawnQuad"/> for the contract the hover-pad lane needs.</para>
///
/// <para>TEARDOWN: <see cref="Release"/> detaches the buffer from whatever camera holds it and
/// destroys the mesh and material. Nothing is ever left on a game object — the decals themselves
/// are only READ.</para>
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

    /// <summary>Hard floor/ceiling for both size dials, applied on TOP of the bind-site
    /// <c>AcceptableValueRange</c>. Belt and braces: a hand-edited .cfg can carry a value BepInEx
    /// never clamped (it clamps what it parses, not what a later hand-edit puts back), and a factor
    /// of 0 would erase every location marker on the map — i.e. remove the only way to pick a
    /// scenario. A setting may configure comfort, never reachability.</summary>
    private const float MinIconScale = 0.5f;

    /// <inheritdoc cref="MinIconScale"/>
    private const float MaxIconScale = 4f;

    /// <summary>Frames between decal re-scans. Same cadence as the flat path's icon cache —
    /// locations are destroyed and respawned by <c>MapChoreographer.InitMap</c>, so a cached set
    /// must be short-lived, and ~0.2 s is far below the time any map change takes to be seen.</summary>
    private const int RescanIntervalFrames = 15;

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

    private readonly List<Renderer> _tokenRenderers = new(8);
    private readonly List<MaterialPropertyBlock> _mpbPool = new(64);
    private int _scanFrame = int.MinValue;

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
    /// <para>See <see cref="IsCapital"/> for the Gloomhaven identification and
    /// <see cref="ClampScale"/> for the floor.</para>
    /// </summary>
    internal static float ScaleForDrawnQuad(Component? decal)
    {
        ConfigEntry<float>? entry = IsCapital(OwnerOf(decal))
            ? Plugin.MapGloomhavenIconScale
            : Plugin.MapIconScale;
        return ClampScale(entry != null ? entry.Value : 1f);
    }

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

        // Read ONCE per tick, not once per icon: both dials are live (a menu step must show on the
        // next frame with no reload) but they must not be able to change value halfway through a
        // buffer refill, which would put two different sizes in one frame's recording.
        float generalScale = ClampScale(Plugin.MapIconScale != null ? Plugin.MapIconScale.Value : 1f);
        float capitalScale = ClampScale(Plugin.MapGloomhavenIconScale != null
            ? Plugin.MapGloomhavenIconScale.Value
            : 1f);

        float planeY = parchment.bounds.max.y + IconLiftWorld;
        int drawn = 0;
        int capitalHits = 0;
        int ownerless = 0;
        for (int i = 0; i < _decals.Count; i++)
        {
            Component d = _decals[i];
            if (d == null || !d.gameObject.activeInHierarchy)
                continue;
            if (_decalCurMatProp?.GetValue(d) is not Material cm)
                continue;
            Texture? tex = cm.HasProperty(IconMainTex) ? cm.GetTexture(IconMainTex) : cm.mainTexture;
            if (tex == null || _decalRenderers[i] == null)
                continue;

            // The owner is re-READ (not re-found) every frame on purpose: MapLocation.MapLocationType
            // is assigned inside MapLocation.Init, which can run a frame or two after the decal
            // exists, so a flag latched at scan time could be stale for the icon's whole life.
            global::MapLocation? owner = _decalOwners[i];
            bool isCapital = IsCapital(owner);
            float f = isCapital ? capitalScale : generalScale;
            if (isCapital)
                capitalHits++;
            else if (owner == null)
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
            mpb.SetTexture(IconMainTex, tex);
            mpb.SetColor(IconColor, Color.white);
            _cmd.DrawMesh(_quad, Matrix4x4.TRS(pos, rot, scale), _mat, 0, 0, mpb);
            drawn++;
        }

        int tokenDraws = 0;
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
                _cmd.DrawRenderer(tr, mats[sm], sm, -1); // -1 = the material's own valid passes
                tokenDraws++;
            }
        }

        DrawnCount = drawn;
        TokenDrawCount = tokenDraws;
        LogCensus(drawn, tokenDraws, capitalHits, ownerless, planeY, generalScale, capitalScale);
    }

    /// <summary>
    /// The icon census — printed on the FIRST tick that produced a buffer and on every later tick
    /// whose numbers differ from the last one printed (throttled).
    ///
    /// <para>It prints AT ZERO. The pre-fix version only reported when it had drawn at least one
    /// icon, which makes "the scan ran and the map genuinely has no icons yet" and "the scan never
    /// ran / found nothing it could read" the same silence in the log. Those are opposite bugs and
    /// the line now names which one it is.</para>
    /// </summary>
    private void LogCensus(int drawn, int tokenDraws, int capitalHits, int ownerless, float planeY,
                           float generalScale, float capitalScale)
    {
        int signature = drawn * 397
                        ^ tokenDraws * 31
                        ^ capitalHits * 7
                        ^ ownerless * 3
                        ^ generalScale.GetHashCode()
                        ^ capitalScale.GetHashCode();
        if (_censusPrinted && signature == _censusSignature)
            return;
        float now = Time.unscaledTime;
        if (_censusPrinted && now < _censusNextAllowed)
            return;
        _censusSignature = signature;
        _censusPrinted = true;
        _censusNextAllowed = now + CensusMinIntervalSeconds;

        int generalHits = drawn - capitalHits;
        VRLog.Info(Scope,
            $"MAP ROOM icons: {drawn} location icon(s) + {tokenDraws} party-token submesh draw(s) queued "
            + $"for the head camera at y={planeY:F2} ({IconLiftWorld:F2} above the parchment top), drawn at "
            + $"{IconEvent}. Footprint = decal lossyScale.xz at the decal's own yaw, times the size dial. "
            + $"SIZE: [MapRoom] IconScale x{generalScale:F2} hit {generalHits} icon(s); "
            + $"[MapRoom] GloomhavenIconScale x{capitalScale:F2} hit {capitalHits} icon(s) "
            + "(the location whose MapLocation.MapLocationType is Headquarters — exactly one exists per map, "
            + "see MapChoreographer.HeadquartersLocation). "
            + $"{ownerless} drawn icon(s) had NO owning MapLocation and took the general dial."
            + (drawn == 0
                ? " ZERO ICONS is a real state, not a silent scan: the decal scan RAN this tick and found "
                  + "nothing it could draw. Either the map has no active locations yet (fresh campaign, "
                  + "mid-InitMap) or every decal's CurrentMaterial carried no texture — the two are told "
                  + "apart by the MAP ROOM location input line, which counts MapLocation components."
                : capitalHits == 0
                    ? " NO Headquarters icon among them — expected on the CITY map and while the capital's "
                      + "own location object is inactive; on the world map its absence means the separate "
                      + "Gloomhaven dial has nothing to act on and the report should say so."
                    : string.Empty));
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

    /// <summary>Both size dials, clamped in code as well as at the bind site — see
    /// <see cref="MinIconScale"/> for why the floor is not optional.</summary>
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

    /// <summary>The <c>MapLocation</c> that spawned this decal, or null. See
    /// <see cref="IsCapital"/> for why containment is the right question here.</summary>
    private static global::MapLocation? OwnerOf(Component? decal) =>
        decal != null ? decal.GetComponentInParent<global::MapLocation>() : null;

    /// <summary>Detach the buffer and destroy everything this layer owns. Idempotent.</summary>
    internal void Release(string reason)
    {
        bool had = _cam != null;
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
        _tokenRenderers.Clear();
        _mpbPool.Clear();
        _scanFrame = int.MinValue;
        _censusSignature = 0;
        _censusPrinted = false;
        _censusNextAllowed = 0f;
        DrawnCount = 0;
        TokenDrawCount = 0;
        if (had)
            VRLog.Info(Scope, $"MAP ROOM icon layer released ({reason}) — command buffer detached, mesh and " +
                              "material destroyed; the game's decals were only ever read.");
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

    private void Rescan(global::MapChoreographer choreo)
    {
        bool due = _scanFrame == int.MinValue || Time.frameCount - _scanFrame >= RescanIntervalFrames;
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
            // scene-wide sweep the flat path keeps for that case.
            UnityEngine.Object[] all = Object.FindObjectsOfType(_decalType);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] is not Component c || !c.gameObject.activeInHierarchy)
                    continue;
                _decals.Add(c);
                _decalRenderers.Add(c.GetComponent<Renderer>());
                _decalOwners.Add(OwnerOf(c));
            }
        }
        _tokenRenderers.Clear();
        PartyToken? token = choreo.m_PartyToken;
        if (token != null)
            token.GetComponentsInChildren(includeInactive: false, _tokenRenderers);
    }

    private void Collect(GameObject? rootGo)
    {
        if (rootGo == null || _decalType == null)
            return;
        Component[] found = rootGo.GetComponentsInChildren(_decalType, includeInactive: false);
        for (int i = 0; i < found.Length; i++)
        {
            _decals.Add(found[i]);
            _decalRenderers.Add(found[i] != null ? found[i].GetComponent<Renderer>() : null);
            // Resolved at SCAN time (~4x a second) rather than per frame — the parent chain of a
            // decal never changes, only the TYPE the owner reports does, and that is re-read in the
            // draw loop. Keeps the per-frame path free of GetComponentInParent walks.
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
