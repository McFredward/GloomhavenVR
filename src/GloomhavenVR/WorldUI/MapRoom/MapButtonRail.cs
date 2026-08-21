using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Core;
using GloomhavenVR.Hands;
using GloomhavenVR.Hands.Interact;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.MapRoom;

/// <summary>
/// THE TABLE BUTTONS (worldmap-3d.md phase 6) — the campaign map's own bottom bar, standing on the
/// table rim between the player and the map instead of floating as a window.
///
/// <para>WHAT IT PHYSICALISES. <c>UIGuildmasterHUD</c>'s option bar: enhance, shop, trainer, map,
/// temple, city, town records, mercenary log (decompiled GH.Runtime/UIGuildmasterHUD.cs:52-73).
/// Each is a <c>UIGuildmasterButton</c> — a public component type, so the set is READ off the live
/// HUD rather than hard-coded, and a DLC or version that adds one gets a cap for free.</para>
///
/// <para>THE LOOK IS SAMPLED, NOT MODELLED — user ruling: <i>"Die Knöpfe sollen die selben Symbole
/// haben wie die die im Spiel sind (mit auch den selben Animationen, so leuchtet ein knopf immer
/// wieder auf wenn er gedrückt werden soll weil der host ein Spiel ausgewählt hat zB)."</i>
/// Nothing here re-implements an animation. Every frame each cap copies the LIVE values off the
/// game's own graphics:
/// <list type="bullet">
/// <item>the icon's <c>sprite</c> — set by the game from
///   <c>UIInfoTools.GetGuildmasterModeSprite(mode)</c> and re-set on <c>SetMode</c>, so reading it
///   per frame is what makes a mode switch follow;</item>
/// <item>the icon's live <c>color</c> and its <c>localScale</c>, multiplied by the button's
///   <c>CanvasGroup.alpha</c> — any tint, fade or scale animation the game runs arrives for free;</item>
/// <item>the HIGHLIGHT graphic under <c>highlightAnimator</c> — the object the game
///   <c>SetActive</c>s and drives with <c>LoopAnimator.StartLoop</c> when a button wants pressing
///   (<c>UIGuildmasterButton.RefreshHighlight</c>, :143-157). Its live sprite, colour ALPHA and
///   scale are copied onto a glow quad behind the cap, so the pulse in VR is the same pulse,
///   frame for frame, with no knowledge of the animator's curves;</item>
/// <item>the <c>newNotification</c> tip's active state, as a badge.</item>
/// </list>
/// Sampling beats re-implementing here for the same reason it does in <see cref="ButtonCluster"/>
/// (which mirrors the live TMP label rather than localising anything itself): a copy of an
/// animation drifts from it, a sample cannot.</para>
///
/// <para>SAME CLICK SEAM AS EVERY OTHER PHYSICAL BUTTON IN THIS MOD. A press dispatches
/// <c>ExecuteEvents.pointerClickHandler</c> on the real uGUI <c>Toggle</c>, exactly as
/// <see cref="ButtonCluster"/> does for Ready/Undo/Skip and as the game's own hotkey bridge does
/// (<c>BaseButtons.clickButton</c>). The full guard chain runs — interactability, the game's own
/// <c>canToggle</c> predicate, whatever it does about multiplayer authority — because this is the
/// game's click path and not a shortcut past it. Nothing goes on the wire.</para>
///
/// <para>AND SINCE ModBuild 195, THE OTHER FOUR EVENTS TOO — <b>because a click alone is silent</b>.
/// User ruling: <i>"Immer noch keine Geräusche wenn ich die physischen buttons drücke wie zB
/// 'Händler', ich will das die selben Geräusche kommen die auch im normalen Spiel hörbar sind."</i>
/// READ FROM SOURCE: the guildmaster bar's <c>Toggle</c> is one of the game's audio-carrying toggle
/// classes, and <c>ExtendedToggle.OnPointerClick</c> plays nothing at all — that class has no click
/// item; its sounds hang off <c>OnPointerDown</c>/<c>OnPointerUp</c>/<c>OnPointerEnter</c>/
/// <c>OnPointerExit</c>, and the DOWN sound is additionally gated on <c>isHighlighted</c>, which
/// only <c>OnPointerEnter</c> sets. So a cap that clicks but never hovers is mute by construction,
/// at any listener distance — which is why ModBuild 194's (correct, and confirmed working) ear
/// repair did not fix this. The cap now sends the whole left-mouse sequence through
/// <see cref="NativeUiPress"/>: <c>pointerEnter</c> when the fingertip or the beam arrives,
/// <c>pointerDown</c> → <c>pointerUp</c> → <c>pointerClick</c> on the press, <c>pointerExit</c>
/// when it leaves. <b>The commit is still the click and still exactly one dispatch</b> — see
/// <see cref="NativeUiPress"/> for the item-by-item reading of why none of the other four events
/// can commit an action, and why the wire therefore cannot tell this build from the last one.</para>
///
/// <para>AND THE HUD WINDOW STOPS BEING FLOATED. <c>UIGuildmasterHUD</c> is a permanent flat HUD
/// the game shows and hides continuously; the catch-all floated it, released it and re-floated it
/// until its own churn fuse blew ("a cycling HUD banner, not a waiting decision"). It is now on the
/// catch-all's known-HUD list — the rail IS its VR surface, the same relationship the card fans
/// have to <c>CardsHandManager</c>.</para>
///
/// <para>INPUT: fingertip through the shared <see cref="VRInteractables"/> registry, laser through
/// a geometric <c>Collider.Raycast</c> scan over this rail's own caps. Deliberately NOT through
/// <c>RayInteractor.Mask</c>: the map room keeps that mask narrow on purpose (see
/// <see cref="MapLocationInteractor"/>), and widening it to reach these caps would re-arm the very
/// occlusion refusal that made the window grab bars ungrabbable in ModBuild 178.</para>
/// </summary>
internal sealed class MapButtonRail
{
    private const string Scope = "MapRoom";

    /// <summary>Frames between HUD re-scans — same cadence and same reason as the icon layer's:
    /// the guildmaster bar is rebuilt on a mode switch.</summary>
    private const int RescanIntervalFrames = 15;

    // ---- geometry, REAL METRES (every one is multiplied by the rig scale at build) -----------

    /// <summary>Cap face size. A comfortable fingertip target at a table.</summary>
    private const float CapSizeMeters = 0.055f;

    /// <summary>Gap between neighbouring caps, edge to edge.</summary>
    private const float CapGapMeters = 0.018f;

    /// <summary>Cap body depth (the collider's thickness along its own normal).</summary>
    private const float CapDepthMeters = 0.012f;

    /// <summary>How far OUTSIDE the map's near edge the rail stands, toward the player. The seat is
    /// <c>MapRoomSeat.EdgeStandoffMeters</c> (0.45 m) out, so this lands the rail at about arm's
    /// reach without covering any of the map.</summary>
    private const float RailInsetMeters = 0.075f;

    /// <summary>Rail height above the tabletop plane — just clear of the surface.</summary>
    private const float RailLiftMeters = 0.006f;

    /// <summary>
    /// Tilt of the cap faces above the TABLE PLANE, degrees. 0 = lying flat, face straight up.
    ///
    /// <para>User ruling (ModBuild 182): <i>"sie liegen immer noch nicht flach auf dem Tisch"</i>.
    /// 179–181 stood them up at 35° from vertical, reading them as a console panel; he wants
    /// buttons lying ON the table, pressed from above. Kept as a named constant rather than
    /// inlined because it is the one number a later round is likely to want back.</para>
    /// </summary>
    private const float CapTiltDegrees = 0f;

    /// <summary>Fraction of the cap the game's own icon occupies on the face. Nearly the whole top:
    /// with the sampled button frame gone (see BuildCap) the icon IS the button's face.</summary>
    private const float IconFraction = 0.88f;

    /// <summary>The glow quad's size relative to the cap — the game's highlight art overspills its
    /// button, and a glow clipped to the face would not read as the same effect.</summary>
    private const float GlowFraction = 1.35f;

    /// <summary>Badge size relative to the cap, drawn in the upper-right corner.</summary>
    private const float BadgeFraction = 0.26f;

    /// <summary>How far the cap sinks into its socket when pressed, real metres. Deliberately
    /// generous — at a table the travel is read from a metre away, and a 2 mm dip is invisible
    /// there. Matches the order of the tray keycaps' authored travel.</summary>
    private const float TravelMeters = 0.007f;

    /// <summary>Seconds the cap stays down after a press, before it springs back.</summary>
    private const float PressHoldSeconds = 0.07f;

    /// <summary>Spring-back time constant. Down is fast (a press is instant), up is softer.</summary>
    private const float PressDownSeconds = 0.02f;
    private const float PressUpSeconds = 0.11f;

    /// <summary>The socket disc's diameter relative to the cap — the ring of well visible around
    /// the pressed cap is what makes it read as a button that moves rather than a decal.</summary>
    private const float SocketFraction = 1.22f;

    /// <summary>Socket depth, real metres.</summary>
    private const float SocketDepthMeters = 0.010f;

    private sealed class Cap
    {
        internal UIGuildmasterButton Button = null!;
        internal GameObject Go = null!;
        internal BoxCollider Collider = null!;

        // The game-side graphics this cap SAMPLES (never writes).
        internal Toggle? Toggle;
        internal Image? IconImage;
        internal CanvasGroup? Group;
        internal GameObject? HighlightGo;
        internal Image? HighlightImage;
        internal Vector3 HighlightBaseScale = Vector3.one;
        internal GameObject? NotificationGo;
        internal Image? NotificationImage;

        // The world-side copies.
        internal MeshRenderer? BodyRenderer;
        internal Color BodyBaseColor = Color.white;
        internal SpriteRenderer? Icon;
        internal SpriteRenderer? Glow;
        internal SpriteRenderer? Badge;
        internal TMP_Text? Fallback;

        internal MapButtonPoke Poke = null!;
        internal float IconWorldSize;
        internal bool Interactable;
        internal bool Hovered;

        /// <summary>The GAME GameObject this cap drives — the real <c>Toggle</c>'s object when the
        /// bar has one, the <c>UIGuildmasterButton</c>'s otherwise. Cached at build so the press and
        /// hover paths can never disagree about the target.</summary>
        internal GameObject? Target;

        /// <summary>How many mod pointers (left fingertip, right fingertip, laser) are on this cap.
        /// The 0→1 edge sends the game a <c>pointerEnter</c> and the 1→0 edge a <c>pointerExit</c>;
        /// two hands on one cap therefore still hover it ONCE. <see cref="GameHovered"/> is the
        /// resulting claim, and it must be handed back on teardown.</summary>
        internal int HoverRefs;
        internal bool GameHovered;

        /// <summary>Presses made on this cap (log material — a doubled press shows as a jump of 2).</summary>
        internal int Presses;

        /// <summary>The travelling part — the cap body and everything printed on it. The socket
        /// stays put, which is what makes the travel legible.</summary>
        internal Transform? Body;
        internal float PressedUntil;
        internal float Depth;      // current travel, world units
        internal float TravelWorld;
    }

    private static FieldInfo? _toggleField;
    private static FieldInfo? _iconField;
    private static FieldInfo? _groupField;
    private static FieldInfo? _highlightField;
    private static FieldInfo? _notificationField;
    private static bool _reflectionTried;

    private readonly List<Cap> _caps = new(8);
    private readonly List<UIGuildmasterButton> _scratch = new(8);
    private GameObject? _root;
    private int _scanFrame = int.MinValue;
    private float _scale = 1f;
    private bool _reported;
    private bool _emptyReported;
    private Cap? _laserHover;

    /// <summary>Caps currently standing (log material).</summary>
    internal int CapCount => _caps.Count;

    /// <summary>
    /// Per-frame upkeep while the map room stands. Builds the rail once the HUD exists, samples
    /// every cap's live look off the game's own graphics, and runs the laser hover/press.
    /// </summary>
    internal void Tick()
    {
        // The int.MinValue term is load-bearing: Time.frameCount - int.MinValue OVERFLOWS negative,
        // so without it the first test fails and the scan never runs. That exact slip cost ModBuild
        // 178's whole map-location feature — see MapLocationInteractor.Tick.
        if (_scanFrame == int.MinValue || Time.frameCount - _scanFrame >= RescanIntervalFrames)
        {
            _scanFrame = Time.frameCount;
            Rescan();
        }
        if (_caps.Count == 0)
            return;

        SampleState();
        TickLaser();
    }

    /// <summary>Tear the rail down. Idempotent; the only exit.</summary>
    internal void Release(string reason)
    {
        ClearLaserHover();
        // Hand every pointerEnter back to the game BEFORE the caps go away. The game's own bar
        // button would otherwise stay highlighted forever, and the mod-wide UguiHoverTracker
        // refcount would never drain.
        DropAllHovers(reason);
        for (int i = 0; i < _caps.Count; i++)
        {
            if (_caps[i].Poke != null)
                VRInteractables.UnregisterPokeable(_caps[i].Poke);
        }
        int had = _caps.Count;
        _caps.Clear();
        if (_root != null)
            Object.Destroy(_root);
        _root = null;
        _scanFrame = int.MinValue;
        _reported = false;
        if (had > 0)
            VRLog.Info(Scope, $"MAP TABLE BUTTONS released ({reason}) — {had} cap(s) destroyed, poke "
                              + "registrations dropped. Nothing on the game's own HUD was modified: the "
                              + "caps only ever READ its graphics and dispatched clicks into them.");
    }

    // ---- build ------------------------------------------------------------------------------

    private void Rescan()
    {
        _scratch.Clear();
        if (Singleton<UIGuildmasterHUD>.IsInitialized)
        {
            UIGuildmasterHUD hud = Singleton<UIGuildmasterHUD>.Instance;
            if (hud != null)
                hud.GetComponentsInChildren(includeInactive: true, _scratch);
        }
        // Fallback for a HUD that is not the singleton yet (or a version that parents the bar
        // elsewhere) — the component type is public, so this needs no name matching.
        if (_scratch.Count == 0)
        {
            UIGuildmasterButton[] sweep = Object.FindObjectsOfType<UIGuildmasterButton>(true);
            _scratch.AddRange(sweep);
        }

        if (SameSet())
            return;

        // The set changed (a mode switch rebuilds the bar) — rebuild from scratch rather than
        // reconciling: eight caps are cheap, and a partial reconcile is where stale references live.
        Release("the guildmaster bar changed");
        if (_scratch.Count == 0)
        {
            if (!_emptyReported)
            {
                _emptyReported = true;
                VRLog.Info(Scope, "MAP TABLE BUTTONS: no UIGuildmasterButton in the scene yet — the "
                                  + "guildmaster bar has not been built. The rail stands up by itself the "
                                  + "moment it is; this line is not an error, and it is printed once.");
            }
            return;
        }
        _emptyReported = false;
        Build();
    }

    private bool SameSet()
    {
        if (_scratch.Count != _caps.Count)
            return false;
        for (int i = 0; i < _caps.Count; i++)
        {
            if (_caps[i].Button == null || !_scratch.Contains(_caps[i].Button))
                return false;
        }
        return true;
    }

    private void Build()
    {
        if (!MapRoomDriver.TrySolveSeat(out MapRoomSeat.Seat seat, out _))
            return;
        MeshRenderer? parchment = MapRoomDriver.ParchmentRenderer;
        if (parchment == null)
            return;

        _scale = Mathf.Max(seat.Scale, 0.0001f);
        Bounds b = parchment.bounds;

        // WHERE THE RAIL STANDS. Straight out from the map's near edge along the seat's own view
        // side — the direction MapRoomSeat already solved as "which side of the table the player
        // reads this map from" — so the caps are always between the player and the map, whichever
        // side that turns out to be. World-fixed from here: like the room itself this is furniture
        // and must never follow the head (the ModBuild-131 ruling).
        Vector3 side = seat.ViewSide;
        float halfAlongSide = MapRoomSeat.HalfExtentAlong(b.size, side);
        var origin = new Vector3(b.center.x, seat.TopY + RailLiftMeters * _scale, b.center.z)
                     + side * (halfAlongSide + RailInsetMeters * _scale);

        _root = new GameObject("GloomhavenVR.MapButtonRail");
        _root.transform.SetPositionAndRotation(origin, seat.Rotation);

        // THE CAP FRAME (ModBuild 181 — "Die Buttons sind verdreht", and they were).
        //
        // THE RAIL'S +Z POINTS AT THE MAP, NOT AT THE PLAYER. seat.Rotation is the yaw that makes
        // the player FACE the map centre from the seat, so applied to this root its forward runs
        // seat → map. 179 and 180 both had that backwards in their comments and in their maths, so
        // the faces were aimed away from the player and tilted the wrong way; his screenshot shows
        // a row of caps leaning over with their backs out.
        //
        // So the visible face must look toward -Z (out at the player) and UP by CapTiltDegrees:
        //     faceDir = (0, sin, -cos)
        // A SpriteRenderer's front is its OWN -Z (the default camera looks along +Z and sees a
        // sprite from the sprite's -Z side), so the cap's -Z must BE faceDir, i.e. its +Z is
        // -faceDir = (0, -sin, cos). That also makes local +X come out as world +X — the caps lay
        // out left-to-right as read, instead of mirrored — and makes +Z "into the table", which is
        // exactly the press-travel direction.
        // At CapTiltDegrees = 0 the face points straight UP, so the cap's forward is straight DOWN
        // and Vector3.up would be a degenerate up-hint for LookRotation. The hint is therefore the
        // rail's own +Z (away from the player), which becomes the icon's "up" on the table — the
        // reading orientation for someone standing at this edge — and stays well-conditioned at
        // every tilt from flat to upright.
        float tilt = CapTiltDegrees * Mathf.Deg2Rad;
        var capForward = new Vector3(0f, -Mathf.Cos(tilt), Mathf.Sin(tilt));
        var capUpHint = new Vector3(0f, Mathf.Sin(tilt), Mathf.Cos(tilt));
        Quaternion capLocalRot = Quaternion.LookRotation(capForward, capUpHint);

        float cap = CapSizeMeters * _scale;
        float gap = CapGapMeters * _scale;
        float depth = CapDepthMeters * _scale;
        float pitch = cap + gap;
        float span = pitch * _scratch.Count - gap;
        float x0 = -span * 0.5f + cap * 0.5f;

        int built = 0;
        int withIcon = 0;
        int withGlow = 0;
        for (int i = 0; i < _scratch.Count; i++)
        {
            UIGuildmasterButton button = _scratch[i];
            if (button == null)
                continue;
            Cap c = BuildCap(button, new Vector3(x0 + pitch * i, 0f, 0f), capLocalRot, cap, depth);
            _caps.Add(c);
            built++;
            if (c.Icon != null) withIcon++;
            if (c.Glow != null) withGlow++;
        }
        VRLayers.Apply(_root);

        if (!_reported)
        {
            _reported = true;
            VRLog.Info(Scope, $"MAP TABLE BUTTONS: {built} cap(s) standing on the table rim at {origin}, "
                              + $"{RailInsetMeters:F3} m (real) outside the map's near edge on the seat's "
                              + $"own view side {side}, {CapSizeMeters * 100f:F0} mm faces tilted "
                              + $"{CapTiltDegrees:F0}° up, rig scale {_scale:F2}. The set was READ off the "
                              + "live UIGuildmasterHUD (component type, not a name list). "
                              + $"{withIcon}/{built} carry the game's own icon Image and {withGlow}/{built} "
                              + "carry its highlight graphic — those two are SAMPLED every frame (sprite, "
                              + "colour, alpha, scale), so a mode switch and the press-me pulse arrive "
                              + "here as the same animation rather than a copy of it. A press sends the "
                              + "game's own left-mouse sequence on the real Toggle — pointerEnter on "
                              + "hover, pointerDown -> pointerUp -> pointerClick on the press, "
                              + "pointerExit on leaving — and COMMITS on the click alone, exactly one "
                              + "dispatch, so its guards still decide and nothing goes on the wire. The "
                              + "other four exist because the game's press SOUND is gated on them "
                              + "(ModBuild 195). "
                              + "World-fixed — the rail never follows the head.");
        }
    }

    private Cap BuildCap(UIGuildmasterButton button, Vector3 localPos, Quaternion localRot,
                         float cap, float depth)
    {
        var go = new GameObject($"Cap_{button.GuildmasterMode}");
        go.transform.SetParent(_root!.transform, worldPositionStays: false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(cap, cap, depth);
        col.isTrigger = false;

        var c = new Cap
        {
            Button = button,
            Go = go,
            Collider = col,
            IconWorldSize = cap * IconFraction,
            TravelWorld = TravelMeters * _scale,
        };
        BindGameGraphics(c, button);
        // The one game object this cap drives, for BOTH hover and press. Resolved once here rather
        // than per event so a fingertip enter and a laser click can never land on different objects.
        c.Target = c.Toggle != null ? c.Toggle.gameObject : button.gameObject;

        // THE SOCKET — a static, darker disc a fifth wider than the cap. It never moves, and that
        // is its whole job: a cap that sinks against nothing reads as a shrinking picture, while a
        // cap that sinks into a visible well reads as a button. (Same reason ButtonCluster gives
        // its travelling keycap a base plate.)
        var socket = new GameObject("Socket");
        socket.transform.SetParent(go.transform, worldPositionStays: false);
        socket.transform.localPosition = new Vector3(0f, 0f, SocketDepthMeters * _scale * 0.5f);
        socket.AddComponent<MeshFilter>().sharedMesh =
            Cards.CardMesh.GetRoundCap(cap * SocketFraction, SocketDepthMeters * _scale);
        socket.AddComponent<MeshRenderer>().sharedMaterial = LitMaterial(ButtonTuning.CapWellColor);

        // THE TRAVELLING BODY — the disc plus everything printed on it. Parenting the face, icon,
        // glow and badge UNDER it is what makes the press animation cost nothing per frame beyond
        // one localPosition write: the whole assembly moves as one object, exactly as a real
        // keycap does.
        var body = new GameObject("Body");
        body.transform.SetParent(go.transform, worldPositionStays: false);
        body.transform.localPosition = Vector3.zero;
        body.AddComponent<MeshFilter>().sharedMesh = Cards.CardMesh.GetRoundCap(cap, depth);
        var bodyRenderer = body.AddComponent<MeshRenderer>();
        // Its OWN material instance (NewKeycapMaterial returns a fresh one per call), so each cap
        // can be tinted for hover/disabled without a MaterialPropertyBlock and without touching a
        // shared asset. Released with the rail.
        bodyRenderer.sharedMaterial = LitMaterial(ButtonTuning.CapWellColor * 1.6f);
        c.Body = body.transform;
        c.BodyRenderer = bodyRenderer;
        c.BodyBaseColor = bodyRenderer.sharedMaterial.color;

        // PROUD OF THE DISC, NOT ON IT (ModBuild 181 — "die Symbole flackern darauf").
        // GetRoundCap(diameter, height) puts the disc's front face at exactly -height/2, and 180
        // placed the sprite face at exactly -depth/2 — COPLANAR with an opaque, depth-writing
        // surface. Sprites do not write depth but they do depth-TEST, so every pixel of the icon
        // was a coin flip against the cap it sits on, resolved differently per eye and per frame.
        // That is the flicker. Each layer now stands a clear step off the disc, and off each
        // other, in real millimetres carried by the rig scale.
        float step = 0.0008f * _scale;          // 0.8 mm real between layers
        float front = -depth * 0.5f;            // the disc's own front plane
        c.Glow = c.HighlightImage != null
            ? MakeSprite(body.transform, "Glow", front - step * 0.5f, cap * GlowFraction, -1)
            : null;
        // NO SAMPLED BUTTON FRAME (ModBuild 182). NativeButtonSkin.CreateFace put the game's
        // 9-sliced UI button sprite under the icon — on a flat uGUI bar that IS the button, but on
        // a physical cap it is a second button drawn on top of the first: "die Symbole haben nun
        // einen viereckigen Rahmen statt direkt auf dem button zu sitzen". The cap's own lit disc
        // is the button now, and the icon sits straight on it; hover/disabled tinting moved to the
        // disc's material (SampleState), which is where a physical button's state belongs anyway.
        if (c.IconImage != null)
            c.Icon = MakeSprite(body.transform, "Icon", front - step * 2f, c.IconWorldSize, 1);
        if (c.NotificationImage != null || c.NotificationGo != null)
        {
            c.Badge = MakeSprite(body.transform, "Badge", front - step * 3f, cap * BadgeFraction, 2);
            c.Badge.transform.localPosition = new Vector3(cap * 0.34f, cap * 0.34f, front - step * 3f);
        }

        if (c.IconImage == null)
        {
            // No icon readable — name the button rather than shipping a blank cap. The enum member
            // is not localized, and that is stated here rather than hidden.
            var textGo = new GameObject("Fallback");
            textGo.transform.SetParent(body.transform, worldPositionStays: false);
            textGo.transform.localPosition = new Vector3(0f, 0f, front - step * 2f);
            TextMeshPro label = textGo.AddComponent<TextMeshPro>();
            label.text = button.GuildmasterMode.ToString();
            label.fontSize = cap * 8f;
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.sizeDelta = new Vector2(cap, cap);
            NativeButtonSkin.ApplyFont(label);
            c.Fallback = label;
        }

        c.Poke = go.AddComponent<MapButtonPoke>();
        c.Poke.Bind(this, c.Button);
        VRInteractables.RegisterPokeable(c.Poke, col);
        return c;
    }

    /// <summary>
    /// A lit, depth-honest material for the cap bodies — the same helper path
    /// <see cref="ButtonCluster"/> uses for its keycaps, so the table buttons are made of the same
    /// material as every other physical button in the mod (carved-grain <c>_MainTex</c> × tint when
    /// the bundle ships it, plain tint otherwise). Depth-writing and LEqual, so a cap is occluded
    /// by anything genuinely in front of it instead of floating over the room.
    /// </summary>
    private static Material LitMaterial(Color color)
    {
        Shader? lit = Cards.PlayTray.BoardLitShader();
        return lit != null
            ? Cards.PlayTray.NewKeycapMaterial(lit, color)
            : WorldUIAssets.CreateFlatMaterial(color);
    }

    /// <summary>
    /// Drive the press travel. One localPosition write per animating cap and nothing at all once a
    /// cap is at rest — the whole assembly is parented under the body, so the face, icon, glow and
    /// badge come along for free.
    ///
    /// <para>Down fast, up soft: a press must feel instant, a release must not look like a bounce.
    /// The hold keeps the cap seated for <see cref="PressHoldSeconds"/> so a press is visible even
    /// when the trigger is tapped in a single frame.</para>
    /// </summary>
    private static void TickTravel(Cap c)
    {
        if (c.Body == null)
            return;
        bool down = Time.unscaledTime < c.PressedUntil;
        float target = down ? c.TravelWorld : 0f;
        if (Mathf.Approximately(c.Depth, target))
            return;
        float tau = down ? PressDownSeconds : PressUpSeconds;
        c.Depth = Mathf.MoveTowards(c.Depth, target,
                                    c.TravelWorld * Time.unscaledDeltaTime / Mathf.Max(tau, 1e-4f));
        if (Mathf.Abs(c.Depth - target) < c.TravelWorld * 0.01f)
            c.Depth = target;
        // +Z is INTO the socket: the cap's -Z faces the player (see the frame note in Build).
        c.Body.localPosition = new Vector3(0f, 0f, c.Depth);
    }

    private SpriteRenderer MakeSprite(Transform parent, string name, float localZ, float size, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0f, localZ);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = new Vector2(size, size);
        sr.sortingOrder = order;
        return sr;
    }

    // ---- sampling the live game graphics ------------------------------------------------------

    /// <summary>
    /// Per-frame mirror. EVERY value here is READ off the game's own components — none is modelled,
    /// none is animated by this class. See the class doc for why that is the whole point.
    /// </summary>
    private void SampleState()
    {
        for (int i = 0; i < _caps.Count; i++)
        {
            Cap c = _caps[i];
            if (c.Go == null || c.Button == null)
                continue;

            TickTravel(c);

            float groupAlpha = c.Group != null ? c.Group.alpha : 1f;
            bool live = c.Button.IsActive && c.Toggle != null && c.Toggle.IsInteractable();
            if (live != c.Interactable)
            {
                c.Interactable = live;
                // HONEST AFFORDANCE: a cap the game would refuse is physically inert, so neither a
                // fingertip nor the laser can promise a press that cannot happen.
                c.Collider.enabled = live;
                // A cap that just went inert must also hand back any hover it holds on the game's
                // button: the laser scan and the poke interactor both skip a disabled collider, so
                // neither of them will ever send the matching exit by itself.
                if (!live)
                {
                    if (ReferenceEquals(_laserHover, c))
                        _laserHover = null;
                    DropHover(c, "the game turned this button off");
                }
            }

            // THE SYMBOL. Read per frame, not once: the game re-assigns it from
            // UIInfoTools.GetGuildmasterModeSprite on every SetMode.
            if (c.Icon != null && c.IconImage != null)
            {
                if (c.Icon.sprite != c.IconImage.sprite)
                    c.Icon.sprite = c.IconImage.sprite;
                Color tint = c.IconImage.color;
                tint.a *= groupAlpha * (live ? 1f : 0.35f);
                if (c.Icon.color != tint)
                    c.Icon.color = tint;
                // A scale animation on the game's icon is reproduced proportionally.
                float s = c.IconImage.transform.localScale.x;
                var want = new Vector2(c.IconWorldSize * s, c.IconWorldSize * s);
                if (c.Icon.size != want)
                    c.Icon.size = want;
            }

            // THE PULSE. The game SetActives this object and drives it with a LoopAnimator when the
            // button wants pressing; its live alpha and scale ARE the animation, so copying them is
            // exact and needs no knowledge of the curves.
            if (c.Glow != null)
            {
                bool glowing = c.HighlightGo != null && c.HighlightGo.activeInHierarchy
                               && c.HighlightImage != null && c.HighlightImage.enabled;
                if (c.Glow.enabled != glowing)
                    c.Glow.enabled = glowing;
                if (glowing)
                {
                    if (c.Glow.sprite != c.HighlightImage!.sprite)
                        c.Glow.sprite = c.HighlightImage.sprite;
                    Color gc = c.HighlightImage.color;
                    gc.a *= groupAlpha;
                    if (c.Glow.color != gc)
                        c.Glow.color = gc;
                    float ratio = c.HighlightBaseScale.x > 1e-4f
                        ? c.HighlightImage.transform.localScale.x / c.HighlightBaseScale.x
                        : 1f;
                    float size = CapSizeMeters * _scale * GlowFraction * ratio;
                    var want = new Vector2(size, size);
                    if (c.Glow.size != want)
                        c.Glow.size = want;
                }
            }

            if (c.Badge != null)
            {
                bool badge = c.NotificationGo != null && c.NotificationGo.activeInHierarchy;
                if (c.Badge.enabled != badge)
                    c.Badge.enabled = badge;
                if (badge && c.NotificationImage != null)
                {
                    if (c.Badge.sprite != c.NotificationImage.sprite)
                        c.Badge.sprite = c.NotificationImage.sprite;
                    Color bc = c.NotificationImage.color;
                    bc.a *= groupAlpha;
                    if (c.Badge.color != bc)
                        c.Badge.color = bc;
                }
            }

            // The CAP ITSELF carries the state now that there is no sprite frame: dimmed when the
            // game would refuse it, warmed when the laser or a fingertip is on it.
            if (c.BodyRenderer != null && c.BodyRenderer.sharedMaterial != null)
            {
                Color want = !live ? c.BodyBaseColor * 0.45f
                           : c.Hovered ? Color.Lerp(c.BodyBaseColor, NativeButtonSkin.LabelColor, 0.45f)
                           : c.BodyBaseColor;
                want.a = c.BodyBaseColor.a;
                if (c.BodyRenderer.sharedMaterial.color != want)
                    c.BodyRenderer.sharedMaterial.color = want;
            }
            if (c.Fallback != null)
            {
                c.Fallback.color = live ? NativeButtonSkin.LabelColor
                                        : NativeButtonSkin.LabelColor * 0.45f;
            }
        }
    }

    // ---- hover, reference-counted across the two hands and the beam ---------------------------

    /// <summary>
    /// A mod pointer arrived on / left this cap. TWO THINGS HAPPEN, and only one of them is new:
    /// the cap's own warm tint (as before), and — since ModBuild 195 — a real
    /// <c>pointerEnter</c>/<c>pointerExit</c> into the GAME's button.
    ///
    /// <para>WHY THE GAME NEEDS THE HOVER AT ALL, given the user asked about PRESS sounds: both of
    /// the game's audio-carrying button classes gate their DOWN sound on <c>isHighlighted</c>
    /// (ExtendedButton.cs:208 and the same test in ExtendedToggle), and <c>isHighlighted</c> is set
    /// only from <c>OnPointerEnter</c>. Without the enter there is no press sound even when the
    /// press event arrives. The hover sound the game plays on the way in is a bonus, and it is the
    /// game's own item — exactly what the flat game does when the mouse crosses that button.</para>
    ///
    /// <para>REFERENCE-COUNTED so two hands on one cap enter it once: the count is per cap here, and
    /// <see cref="NativeUiPress"/> additionally arbitrates per GameObject through the mod-wide
    /// <c>UguiHoverTracker</c>, so even the laser and a fingertip agreeing on the same cap cannot
    /// double it.</para>
    ///
    /// <para>ONE VISIBLE SIDE EFFECT, and it is the game's own and the right one: the enter also
    /// reaches <c>UIGuildmasterButton.OnPointerEnter</c> (the same GameObject — its
    /// <c>[RequireComponent]</c> partner <c>GuildmasterModeSelectable</c> resolves the toggle with a
    /// plain <c>GetComponent&lt;Selectable&gt;()</c>), whose <c>SetHovered</c> → <c>RefreshHighlight</c>
    /// STOPS the "press me" loop animation while the button is hovered. Because the cap SAMPLES that
    /// highlight object, the cap's glow stops with it — which is exactly what the flat game shows
    /// when the mouse rests on a pulsing button, and it comes back on the exit. Nothing here
    /// re-implements that; it is the sampled animation doing what the game told it to do.</para>
    /// </summary>
    private void AddHover(Cap c, string source)
    {
        c.HoverRefs++;
        c.Hovered = true;
        if (c.HoverRefs != 1 || c.GameHovered)
            return;
        c.GameHovered = true;
        NativeUiPress.SetHovered(c.Target, hovered: true, CapName(c) + " (" + source + ")");
    }

    private void RemoveHover(Cap c, string source)
    {
        if (c.HoverRefs > 0)
            c.HoverRefs--;
        c.Hovered = c.HoverRefs > 0;
        if (c.HoverRefs != 0 || !c.GameHovered)
            return;
        c.GameHovered = false;
        NativeUiPress.SetHovered(c.Target, hovered: false, CapName(c) + " (" + source + ")");
    }

    /// <summary>Hand back EVERY outstanding claim on one cap at once, whatever the count. Used when
    /// the cap stops being pressable at all (the game turned its Toggle off) and on teardown — an
    /// enter that is never balanced leaves the game's own bar button highlighted for the session.</summary>
    private void DropHover(Cap c, string reason)
    {
        c.HoverRefs = 0;
        c.Hovered = false;
        if (!c.GameHovered)
            return;
        c.GameHovered = false;
        NativeUiPress.SetHovered(c.Target, hovered: false, CapName(c) + " (" + reason + ")");
    }

    private void DropAllHovers(string reason)
    {
        for (int i = 0; i < _caps.Count; i++)
            DropHover(_caps[i], reason);
    }

    private static string CapName(Cap c) =>
        "map table cap '" + (c.Button != null ? c.Button.GuildmasterMode.ToString() : "?") + "'";

    /// <summary>Find the cap that drives this game button, or null. Linear over at most eight.</summary>
    private Cap? CapOf(UIGuildmasterButton button)
    {
        for (int i = 0; i < _caps.Count; i++)
        {
            if (ReferenceEquals(_caps[i].Button, button))
                return _caps[i];
        }
        return null;
    }

    /// <summary>Fingertip hover, routed from <see cref="MapButtonPoke"/> so the finger and the beam
    /// share one refcount and one dispatch path.</summary>
    internal void SetPokeHover(UIGuildmasterButton button, bool hovered, string source)
    {
        if (button == null)
            return;
        Cap? c = CapOf(button);
        if (c == null)
            return;
        if (hovered)
            AddHover(c, source);
        else
            RemoveHover(c, source);
    }

    // ---- laser -------------------------------------------------------------------------------

    /// <summary>
    /// Geometric laser test over this rail's own caps — the same shape as <c>RayGrabDriver</c>'s
    /// bar scan, and for the same reason: it needs no layer/mask coupling, so it cannot disturb the
    /// deliberately narrow pick mask the map room keeps.
    /// </summary>
    private void TickLaser()
    {
        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.HasPose || !hand.Ray.Active)
        {
            ClearLaserHover();
            return;
        }

        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        var ray = new Ray(origin, direction);
        float best = 20f * hand.WorldScale;
        Cap? hit = null;
        Vector3 hitPoint = default;

        for (int i = 0; i < _caps.Count; i++)
        {
            Cap c = _caps[i];
            if (c.Collider == null || !c.Collider.enabled || !c.Go.activeInHierarchy)
                continue;
            if (c.Collider.Raycast(ray, out RaycastHit rh, best))
            {
                hit = c;
                best = rh.distance;
                hitPoint = rh.point;
            }
        }

        if (hit == null)
        {
            ClearLaserHover();
            return;
        }
        // A nearer game-UI hit wins: a click on a floated window's widget must never also press a
        // table button behind it (the same precedence RayGrabDriver applies to its bars).
        if (hand.RayUgui.HasHit && hand.RayUgui.HitDistance < best)
        {
            ClearLaserHover();
            return;
        }

        if (!ReferenceEquals(hit, _laserHover))
        {
            ClearLaserHover();
            _laserHover = hit;
            AddHover(hit, "laser");
            hand.SendHaptic(HapticPreset.HoverTick);
        }
        hand.Ray.UiHitOverride = hitPoint;

        if (hand.TriggerDown)
        {
            hand.Ray.SuppressFarClick();
            Press(hit.Button, $"{hand.Side} trigger");
        }
    }

    private void ClearLaserHover()
    {
        if (_laserHover != null)
        {
            Cap c = _laserHover;
            _laserHover = null;           // cleared FIRST: RemoveHover must not re-enter this
            RemoveHover(c, "laser left");
        }
    }

    // ---- the click ---------------------------------------------------------------------------

    /// <summary>
    /// Press a button. ONE commit — <c>pointerClick</c> into the game's own uGUI Toggle, exactly as
    /// before ModBuild 195 — preceded by the <c>pointerDown</c>/<c>pointerUp</c> pair (and, when the
    /// prop is not already hovering, by a synthesized <c>pointerEnter</c>/<c>pointerExit</c> around
    /// the whole thing) so the game's OWN press sound is reachable at all. See the class doc and
    /// <see cref="NativeUiPress"/> for the reading of why the four added events cannot commit
    /// anything and why nothing new goes on the wire.
    /// </summary>
    internal void Press(UIGuildmasterButton button, string source)
    {
        if (button == null)
            return;
        // THE CAP GOES DOWN WHETHER OR NOT THE GAME ACCEPTS THE PRESS. A button that does not move
        // when you push it reads as broken input, not as a refusal — and the refusal is already
        // communicated by the cap being dimmed and inert in the first place.
        Cap? cap = CapOf(button);
        if (cap != null)
        {
            cap.PressedUntil = Time.unscaledTime + PressHoldSeconds;
            cap.Presses++;
        }
        Toggle? toggle = ToggleOf(button);
        GameObject target = toggle != null ? toggle.gameObject : button.gameObject;
        if (toggle != null && !toggle.IsInteractable())
        {
            VRLog.Info(Scope, $"MAP TABLE BUTTON '{button.GuildmasterMode}' pressed ({source}) but its "
                              + "real Toggle is not interactable — refused, exactly as the flat game "
                              + "would refuse it. The cap's collider should already have been off; if "
                              + "this line appears, the mirror was one frame behind the game.");
            return;
        }

        // ALREADY HOVERED? Then the pointerEnter is already outstanding (the fingertip or the beam
        // sent it on arrival) and must NOT be sent again — that would be the double this brief
        // warns about. Only a press with no hover behind it (PressMode from GuildmasterDestinations,
        // or a poke whose enter was lost to a rebuilt rail) synthesizes its own enter/exit pair.
        bool hovered = cap != null && cap.GameHovered;
        NativeUiPress.Press(target, hovered, CapName(cap, button) + " (" + source + ")");

        VRLog.Info(Scope, $"MAP TABLE BUTTON '{button.GuildmasterMode}' pressed ({source}) — dispatched on "
                          + $"'{target.name}' as {(hovered ? "pointerDown -> pointerUp -> pointerClick (the "
                              + "pointerEnter was already outstanding from this cap's own hover)"
                              : "pointerEnter -> pointerDown -> pointerUp -> pointerClick -> pointerExit "
                              + "(synthesized: nothing was hovering this cap)")}, i.e. exactly what a left "
                          + "mouse press on the game's own bar button sends. HOW TO READ THIS LINE: the "
                          + "GAME ACTION commits on the pointerClick and on nothing else "
                          + "(Toggle.OnPointerClick -> InternalToggle, ugui Toggle.cs:312-329) — the other "
                          + "events are highlight state, a scale tween and the game's own PlaySound calls, "
                          + "so this is still ONE press and still one thing on the wire. A DOUBLE PRESS "
                          + "would show as two of these lines, or as capPresses jumping by 2. "
                          + $"capPresses={(cap != null ? cap.Presses : -1)}, dispatch totals: "
                          + NativeUiPress.Counters + ". THE SOUND: it is played by the game's own handler "
                          + "off the game's own serialized item — see the one-shot 'PHYSICAL BUTTON SOUND "
                          + "STATE' line for this button, which names each item and whether AudioController "
                          + "knows it. Nothing here picks or plays a clip.");
    }

    private static string CapName(Cap? c, UIGuildmasterButton button) =>
        c != null ? CapName(c) : $"map table cap '{button.GuildmasterMode}'";

    /// <summary>
    /// Press the bar button that carries this mode, if the bar has one. Same dispatch as a
    /// fingertip press on its cap — the game cannot tell the difference, and every guard it runs
    /// still runs. Used to LEAVE a guildmaster destination (see <c>GuildmasterDestinations</c>):
    /// the game's mode machine has no "close", only "switch to another mode", so returning to the
    /// map IS the close, and it is what runs the destination's own Exit.
    /// </summary>
    internal bool PressMode(EGuildmasterMode mode, string source)
    {
        for (int i = 0; i < _caps.Count; i++)
        {
            UIGuildmasterButton button = _caps[i].Button;
            if (button == null || button.GuildmasterMode != mode)
                continue;
            Press(button, source);
            return true;
        }
        return false;
    }

    // ---- reading the game's privates ---------------------------------------------------------

    /// <summary>
    /// Bind the five game-side graphics a cap samples. They are all private [SerializeField]s on
    /// <c>UIGuildmasterButton</c> (decompiled UIGuildmasterButton.cs:24-48), so they are read by
    /// NAME — and every one has a stated fallback, because a version that renames a field must
    /// degrade to a plainer button rather than to an exception.
    /// </summary>
    private static void BindGameGraphics(Cap c, UIGuildmasterButton button)
    {
        EnsureReflection();
        c.Toggle = _toggleField?.GetValue(button) as Toggle;
        if (c.Toggle == null)
            c.Toggle = button.GetComponentInChildren<Toggle>(true);

        c.IconImage = _iconField?.GetValue(button) as Image;
        c.Group = _groupField?.GetValue(button) as CanvasGroup;
        if (c.Group == null)
            c.Group = button.GetComponent<CanvasGroup>();

        if (_highlightField?.GetValue(button) is Component highlight && highlight != null)
        {
            c.HighlightGo = highlight.gameObject;
            c.HighlightImage = highlight.GetComponent<Image>();
            if (c.HighlightImage == null)
                c.HighlightImage = highlight.GetComponentInChildren<Image>(true);
            if (c.HighlightImage != null)
                c.HighlightBaseScale = c.HighlightImage.transform.localScale;
        }

        if (_notificationField?.GetValue(button) is Component tip && tip != null)
        {
            c.NotificationGo = tip.gameObject;
            c.NotificationImage = tip.GetComponent<Image>();
            if (c.NotificationImage == null)
                c.NotificationImage = tip.GetComponentInChildren<Image>(true);
        }
    }

    private static Toggle? ToggleOf(UIGuildmasterButton button)
    {
        EnsureReflection();
        Toggle? viaField = _toggleField?.GetValue(button) as Toggle;
        return viaField != null ? viaField : button.GetComponentInChildren<Toggle>(true);
    }

    private static void EnsureReflection()
    {
        if (_reflectionTried)
            return;
        _reflectionTried = true;
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        System.Type t = typeof(UIGuildmasterButton);
        _toggleField = t.GetField("toggle", Flags);
        _iconField = t.GetField("icon", Flags);
        _groupField = t.GetField("canvasGroup", Flags);
        _highlightField = t.GetField("highlightAnimator", Flags);
        _notificationField = t.GetField("newNotification", Flags);
        if (_toggleField == null || _iconField == null || _highlightField == null)
        {
            VRLog.Warn(Scope, "MAP TABLE BUTTONS: some UIGuildmasterButton fields were not found by "
                              + $"name (toggle={_toggleField != null}, icon={_iconField != null}, "
                              + $"highlightAnimator={_highlightField != null}, "
                              + $"canvasGroup={_groupField != null}, "
                              + $"newNotification={_notificationField != null}). The caps degrade to "
                              + "whatever is still readable — a missing icon becomes a text label, a "
                              + "missing highlight simply never pulses. If a symbol or the press-me "
                              + "pulse is absent in the headset, this line is the reason.");
        }
    }
}

/// <summary>
/// Fingertip adapter for one table cap. Holds nothing: hover AND press route back through the rail
/// so the finger and the laser can never disagree about what a cap does.
///
/// <para>ModBuild 195: the enter/exit callbacks are no longer empty. <c>PokeInteractor</c> guarantees
/// the order <c>OnPokeEnter → (OnPoke)* → OnPokeExit</c> and keeps ONE hovered target per hand
/// (PokeInteractor.cs:322-333, which exits the old target before entering the new one), so these two
/// calls are strictly paired per hand; two hands on one cap are reconciled by the rail's own
/// reference count. That pairing is what lets the cap send the game a real <c>pointerEnter</c> — and
/// without one the game's own DOWN sound is gated off, which is the whole ModBuild 195 defect.</para>
/// </summary>
internal sealed class MapButtonPoke : MonoBehaviour, IPokeable
{
    private MapButtonRail? _rail;
    private UIGuildmasterButton? _button;

    internal void Bind(MapButtonRail rail, UIGuildmasterButton button)
    {
        _rail = rail;
        _button = button;
    }

    public void OnPokeEnter(VRHand hand)
    {
        if (_rail != null && _button != null)
            _rail.SetPokeHover(_button, hovered: true, $"{hand.Side} fingertip");
    }

    public void OnPokeExit(VRHand hand)
    {
        if (_rail != null && _button != null)
            _rail.SetPokeHover(_button, hovered: false, $"{hand.Side} fingertip left");
    }

    public void OnPoke(VRHand hand)
    {
        if (_rail != null && _button != null)
            _rail.Press(_button, $"{hand.Side} fingertip");
    }
}
