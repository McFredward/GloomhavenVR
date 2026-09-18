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
/// Sampling beats re-implementing here for the same reason it does on the control board
/// (which mirrors the live TMP label rather than localising anything itself): a copy of an
/// animation drifts from it, a sample cannot.</para>
///
/// <para>SAME CLICK SEAM AS EVERY OTHER PHYSICAL BUTTON IN THIS MOD. A press dispatches
/// <c>ExecuteEvents.pointerClickHandler</c> on the real uGUI <c>Toggle</c>, exactly as
/// the control board's keycaps do for Ready/Undo/Skip and as the game's own hotkey bridge does
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
/// <para>=====================================================================</para>
/// <para>THE SYMBOLS ARE MIP-BAKED (ModBuild 199) — user report: <i>"Wende den selben
/// Aliasing-Fix auf die SYMBOLE AUF DEN BUTTONS an (die Animation soll aber erhalten bleiben) —
/// sie sind spürbar die einzigen Elemente mit noch extremem Aliasing."</i></para>
///
/// <para>IT IS NOT THE WINDOW FIX, AND THAT IS THE POINT. ModBuild 198's window fix raised
/// <c>PanelSupersample</c>'s factor so a converted canvas is rendered into a RenderTexture above
/// its authored resolution and the eye lands on a real mip level of that TARGET. There is no
/// canvas and no render target on a cap: a symbol here is a world-space <see cref="SpriteRenderer"/>
/// quad textured DIRECTLY from the game's uGUI art. The game imports that art as "Sprite (2D and
/// UI)", which generates no mip chain — every one of the 49 distinct game textures the shared bake
/// cache has measured on hardware reported <c>mips 1</c>. Against a mipless source, raising a
/// render resolution makes the aliasing WORSE, not better: the quad's footprint in source texels is
/// unchanged while its pixel footprint shrinks, so more source texels are dropped per pixel. The
/// defect is in the DATA and so is the fix — a mip chain, trilinear filtering and anisotropy, via
/// the same <c>CardFaceMipBake</c> cache the card faces, the panels and the map icons already
/// share (one VRAM ceiling, one set of refusal rules, and an atlas a card already paid for is
/// free here).</para>
///
/// <para>ANISOTROPY IS THE TERM THAT MATTERS MOST ON THESE CAPS, and it is why a mip chain alone
/// would not have been enough. <see cref="CapTiltDegrees"/> is 0 — the user's own ruling that the
/// buttons must lie FLAT on the table — so a cap face is NEVER seen head-on. It is always read at
/// a grazing angle from a seat at the table edge, which stretches the sampling footprint along one
/// axis; trilinear alone must then pick the mip for the worse axis and blur the better one. The
/// baked copies carry aniso 8.</para>
///
/// <para>THE ANIMATION IS UNTOUCHED — the user's explicit condition. Nothing about WHAT is sampled
/// changed: <see cref="SampleState"/> still reads the game's <c>Image.sprite</c>, <c>color</c>,
/// <c>CanvasGroup.alpha</c> and <c>localScale</c> every frame, the highlight pulse is still copied
/// live off the object the game's <c>LoopAnimator</c> drives, and the press travel
/// (<see cref="TickTravel"/>) is not in this path at all. Only the sprite INSTANCE written to our
/// own renderer changed, to a pixel-identical copy on a mipmapped texture with the same rect,
/// pivot, pixels-per-unit and 9-slice border. AND THE BAKE CANNOT GO STALE: there is no cached
/// "the icon for this cap". The cache is keyed by the GAME's sprite instance and is asked again
/// the moment that reference changes, so a runtime symbol swap
/// (<c>UIGuildmasterButton.SetMode</c> → <c>Initialize</c>), a new highlight sprite or a
/// notification pip resolves in the frame the game assigns it — see
/// <see cref="ResolveSprite"/>.</para>
///
/// <para>MEASURED, NOT ASSUMED: <c>MAP BUTTON ICON SAMPLING</c> prints source texels per rendered
/// pixel for every cap symbol on screen, against the same 1.35x threshold and through the same
/// per-eye projection as <c>MAP ICON SAMPLING</c> and <c>PANEL SAMPLING</c>, with the comparison
/// count, the worst value and the threshold on one line.</para>
///
/// <para>=====================================================================</para>
/// <para>THE CAP LOOKED DEAD WHILE THE PRESS LANDED (ModBuild 200) — user report, verbatim:
/// <i>"4) Du hast ja verändert, das man den Händler und co. jederzeit mit dem button aufrufen kann,
/// auch wenn gerade eine Quest ausgewählt ist: Die Animationen des buttons zeigen aber es sei
/// ausgegraut und nicht drückbar - fix das. Weiterhin soll ein erneuter Druck auf den button zB vom
/// Händler obwohl das Fenster offen ist, das offene Fenster wieder schließen."</i></para>
///
/// <para>WHY BOTH HALVES OF THAT SENTENCE WERE TRUE AT ONCE, AND IT WAS NOT
/// <c>Toggle.IsInteractable()</c>. The cap's grey and the cap's collider came from ONE term,
/// <c>live</c> in <see cref="SampleState"/>, and its FIRST factor is <c>UIGuildmasterButton.IsActive</c>
/// — which is <c>gameObject.activeInHierarchy</c> (decompiled UIGuildmasterButton.cs:68). Select a
/// quest on the map and <c>AdventureMapUIManager.OnSelectedMapLocation</c> (:357) calls
/// <c>UIGuildmasterHUD.EnableHeadquartersOptions(this, false)</c> (:693-712), which reaches
/// <c>RefreshVisibilityHeadquartersOptions</c> (:739-751) and runs
/// <c>optionsContainer.SetActive(false)</c>. In the flat game that is not a grey-out at all — the
/// whole bar is REMOVED from the screen. The map room cannot remove the caps: they are furniture
/// bolted to the table rim. So the mod rendered "there is no bar" as "eight greyed, inert caps", a
/// state the flat game never shows, and switched every collider off with it.</para>
///
/// <para>AND THAT DISABLED COLLIDER IS EXACTLY WHY THE PRESS STILL WORKED — through a second,
/// unintended route. With the cap inert the laser passed straight THROUGH it and landed on the
/// tabletop 6 mm below; <c>MapLocationInteractor.TickDeselect</c> reads a trigger there as "on the
/// map or the table and on no location icon" and runs the game's own <c>MapLocation.Deselect</c>;
/// <c>AdventureMapUIManager.OnDeselectedMapLocation</c> (:396-406) then calls
/// <c>EnableHeadquartersOptions(this, true)</c> and the bar comes back SYNCHRONOUSLY. Because
/// <c>MapRoomDriver.Tick</c> runs <c>Locations.Tick()</c> (:369) BEFORE <c>Buttons.Tick()</c>
/// (:373), the very same frame's <see cref="SampleState"/> found <c>live</c> true again, re-enabled
/// the collider, and <see cref="TickLaser"/> — still inside the same <c>TriggerDown</c> edge — hit
/// the cap and pressed it. HARDWARE PROOF, .planning/debug/Player.log: line 6816 deselects at
/// <c>5.6 cm (real) outside the parchment's own edge</c> — the rail centre stands at
/// <see cref="RailInsetMeters"/> = 7.5 cm and a 5.5 cm cap spans 4.75-10.25 cm, so the beam was
/// aimed AT the merchant cap — and line 6824, with nothing between them but the shop's own
/// area-manager lines, reports <c>MAP TABLE BUTTON 'Merchant' pressed (Right trigger)</c> with
/// <i>"the pointerEnter was already outstanding from this cap's own hover"</i>, i.e. the hover was
/// taken in that same <c>TickLaser</c> call. The identical pair sits at 6355/6363 (5.0 cm). One aim,
/// one trigger, merchant open — off a cap that was drawn dead and whose collider was off.</para>
///
/// <para>ONE TERM MOVED: <c>live</c> IS NOW THE MOD'S PREDICATE, <see cref="Pressable"/>. Nothing
/// else about the sampling ruling changed — the sprite, the tint, the <c>CanvasGroup</c> alpha, the
/// icon's scale animation, the highlight pulse and the notification badge are all still read live
/// off the game's own graphics on their own lines in <see cref="SampleState"/>, untouched. What
/// changed is only the boolean that decides "greyed / inert" versus "lit / solid", and it is now
/// derived from what a press in THIS ROOM actually does:</para>
/// <list type="number">
/// <item>the game says yes (<c>Toggle.IsInteractable()</c>) — unchanged, the common case;</item>
/// <item><b>2026-09-05 — THE REVEAL CLAUSE IS GONE, and this entry is kept as its headstone.</b>
///   It read: "the bar is hidden ONLY because a map location is selected, so the press is still
///   deliverable — <c>TryRevealBar</c> drops that selection first through the game's own
///   <c>AdventureMapUIManager.DeselectCurrentMapLocation()</c> and the bar returns." Two things
///   were wrong with it. Its PREDICATE was a fact about the MAP, identical for every cap, so a
///   selected quest lit all eight caps at once. Its ACTION brought the bar back in single player
///   only: online, <c>UIMapMultiplayerController</c> holds a second <c>disableOptionsRequests</c>
///   entry (:405) that the mod may not remove, so the press ate the player's quest selection and
///   delivered nothing — twice in his host log, verbatim. See <see cref="Deliverable"/>.
///   <b>2026-09-05, SECOND ROUND:</b> the CLAUSE is back and the REVEAL is still gone. He rejected
///   the dim-and-refuse residual in the same words ("Alle Buttons wie Händler und co. sollen
///   drückbar bleiben bis zum point of no return"), and the way out is that both earlier answers
///   were arguing over <c>optionsContainer.activeSelf</c> — the game's flag, with three of its own
///   writers. It is conceded. The clause now reads "this button is UNLOCKED and only the bar above
///   it is off" (<c>activeSelf &amp;&amp; !activeInHierarchy</c>, the exact split between the game's
///   two writers) and the press is delivered through <see cref="SelectThroughTheGamesOwnApi"/> —
///   <c>UIGuildmasterButton.Select()</c>, a public method call that needs no pointer event and no
///   active GameObject. Nothing is written to the game's UI state and the map is never touched.</item>
/// <item>this cap's own destination is STANDING in the room. Until 2026-09-05 this asked
///   <c>toggle.isOn</c>, i.e. the game's single-mode <c>ToggleGroup</c>, and the game marks that
///   toggle non-interactable on purpose (<c>UIGuildmasterButton.RefreshSelected</c>, :209:
///   <c>toggle.interactable = !toggle.isOn</c>, plus the grayscale material on :216) because a flat
///   second click has nothing to do. In the room it DOES have something to do: it closes the window.
///   But the game's mode enum can only ever name ONE open destination, and this room's whole point
///   is that several stand open in parallel — so the term is now
///   <c>GuildmasterDestinations.IsStanding</c>, the mod's own float set, object-keyed. The press
///   routes to <c>GuildmasterDestinations.HandleCapPress</c> exactly as before.</item>
/// </list>
///
/// <para>THE SECOND PRESS CLOSES, AND IT IS THE X's OWN ROUTE, ALL OF IT (ModBuild 230). The whole
/// decision is <c>GuildmasterDestinations.Decide</c>, made ONCE per press against what is observably
/// standing — the live world-space panel first, the game's <c>UIWindow.IsOpen</c> second, the mode
/// enum only as a tiebreaker — and the close is <c>ModalFallback.CloseFloatedWindow</c>, the single
/// routine that releases the map room's parallel float AND runs the game-side
/// <c>UIGuildmasterHUD.UpdateCurrentMode</c> → <c>modes[current].Exit()</c>, which is still the only
/// thing that takes the party display back out of selection mode. ModBuild 222/226 had those as two
/// branches and the log proves each press only ever took one of them, so closing took two presses:
/// see GuildmasterDestinations, section 8, for the four occurrences in his ModBuild 229 log. The six
/// modes that ARE windows toggle; <c>WorldMap</c> and <c>City</c> are the map surface this room is
/// built on and "closing" one of them would mean silently swapping the player between the world map
/// and the city map, so <c>Decide</c> answers <c>Refused</c> for them and says so.</para>
///
/// <para>MULTIPLAYER. A cap's appearance is local presentation and always was. The close drives one
/// <c>pointerClick</c> on the bar's own map Toggle — the same dispatch the window X has sent since
/// ModBuild 184 — and <c>UpdateCurrentMode</c> → <c>Exit</c> is local UI state; the purchase,
/// blessing or enhancement a destination may have committed was committed by ITS own button on its
/// own action path, so a second press cannot re-commit it: it can only leave. Since 2026-09-05 no
/// path in this file writes to the map at all: the reveal that called <c>MapLocation.Deselect</c>
/// is gone (see <see cref="Deliverable"/>), so a press can no longer touch this player's quest
/// selection, and a PEER's mode mirror can no longer touch it either.</para>
///
/// <para>REJECTED. (0, 2026-09-05) Keeping the reveal for SINGLE player, where it does work: it
/// would have to decide per frame whether dropping the selection would actually bring the bar back,
/// which needs <c>disableOptionsRequests</c> — a private <c>HashSet&lt;Component&gt;</c> — read by
/// reflection for every cap; and on the machine where it does not work it would still be a press
/// that silently undid what the player just did. A cap that cannot deliver a click is now simply
/// dim, which is what the flat game shows in the same state. (1) Hard-coding the caps to look
/// enabled: that is the symptom, and it would have
/// left the beam passing through them and silently dropping his quest selection — and note that
/// with the caps SOLID again (2026-09-05, second round) that beam no longer reaches the tabletop at
/// all, so aiming at a cap can no longer drop a selection even by accident. (2) Writing
/// <c>optionsContainer.SetActive(true)</c> ourselves to bring the bar back: a direct edit of game UI
/// state against a level-triggered game writer, i.e. a write war, and it lies about
/// <c>disableOptionsRequests</c>. (3) Calling <c>EnableHeadquartersOptions(us, true)</c>: it only
/// removes OUR request, which we never added, so with the map manager's request still in the set it
/// is a measured no-op. (4) Closing by <c>UIWindow.Hide()</c> on the destination: ModBuild 184
/// already proved that leaves the mode active and the party display dead. (5) Making WorldMap/City
/// closable for symmetry: see above — it is a teleport, not a close.</para>
///
/// <para>INPUT: fingertip through the shared <see cref="VRInteractables"/> registry, laser through
/// a geometric <c>Collider.Raycast</c> scan over this rail's own caps. Deliberately NOT through
/// <c>RayInteractor.Mask</c>: the map room keeps that mask narrow on purpose (see
/// <see cref="MapLocationInteractor"/>), and widening it to reach these caps would re-arm the very
/// occlusion refusal that made the window grab bars ungrabbable in ModBuild 178.</para>
///
/// <para><b>WHAT COUNTS AS A PRESS, AND IT IS NOT THIS FILE'S ANSWER ANY MORE (R5/R11,
/// 2026-09-05).</b> These caps and <c>PlayTray.BoardButton</c> are the same affordance built twice
/// — a physical keycap lying flat on a table — and they used to disagree about the only question
/// that matters: a board keycap fired at 90 % of its travel with a shared cooldown, while a cap
/// here fired on 8 mm of fingertip CONTACT with neither. Brushing CONFIRM did nothing; brushing
/// <i>Händler</i> opened the merchant. The board's principle is the reference because it is the one
/// that carries a reason, and it now lives in <see cref="KeycapPress"/> where both families read
/// it: <see cref="TickPokeDepths"/> is the depth-fire, <see cref="Press"/> is the shared debounce
/// and the commit pulse. The LOOK stays this room's own — sampled game art, the hold-and-spring
/// travel, the table's generous 7 mm — because none of that is what a press IS.</para>
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

    /// <summary>
    /// Gap between the two ROWS of the rail, edge to edge — deliberately wider than
    /// <see cref="CapGapMeters"/>.
    ///
    /// <para>The user asked for the second row "zur optischen Trennung", so the gap has to READ as a
    /// break rather than as more of the same lattice. At 18 mm — the column gap — two rows of flat
    /// caps read as one 2x4 block; at 30 mm they read as two groups. It is a separate constant and
    /// not a multiple of the column gap because it is answering a different question: the column gap
    /// is about not fat-fingering the neighbouring button, this one is about telling two kinds of
    /// button apart at a glance.</para>
    /// </summary>
    private const float RowGapMeters = 0.030f;

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

    /// <summary>
    /// Fingertip contact radius — mirror of <c>PokeInteractor.FingertipRadius</c>, added 2026-09-05
    /// with the depth-fire (R5). It is a COPY and not a reference on that constant's own written
    /// ruling, quoted from its declaration: merging it "would either leak an interactor private
    /// onto the frozen P2 surface or hide the number in a Core file nobody opens when tuning". The
    /// price of that decision is that the copies must be tuned together, and
    /// <c>scripts/check-mirrors.sh</c> is what collects that price — this rail is the fourth site
    /// in the "fingertip contact radius (INVARIANTS §3)" group.
    ///
    /// <para>It has to be the same number as the interactor's, not merely a similar one: the
    /// interactor decides at this radius that a finger is TOUCHING the cap (and therefore that the
    /// enter has happened at all), and <see cref="TickPokeDepths"/> measures the cap's travel from
    /// the same radius. A mismatch would mean a cap that starts moving before it is hovered, or one
    /// that is hovered and cannot reach its own fire depth.</para>
    /// </summary>
    private const float FingertipRadius = 0.008f;

    /// <summary>Spring-back time constant. Down is fast (a press is instant), up is softer.</summary>
    private const float PressDownSeconds = 0.02f;
    private const float PressUpSeconds = 0.11f;

    /// <summary>The socket disc's diameter relative to the cap — the ring of well visible around
    /// the pressed cap is what makes it read as a button that moves rather than a decal.</summary>
    private const float SocketFraction = 1.22f;

    /// <summary>Socket depth, real metres.</summary>
    private const float SocketDepthMeters = 0.010f;

    // ---- the aliasing fix and its instrument (ModBuild 199) -----------------------------------

    /// <summary>
    /// Source texels per rendered pixel at or above which a surface is dropping source texels every
    /// frame. Copied AS A VALUE from <c>MapIconLayer.SuspectMinification</c> and
    /// <c>PanelSamplingProbe</c> so the button report, the map-icon report and the panel report are
    /// all the same quantity against the same threshold and can be read side by side.
    /// </summary>
    private const float SuspectMinification = 1.35f;

    /// <summary>Seconds between MAP BUTTON ICON SAMPLING lines. Long, because the caps do not move
    /// and neither does the seat — this is a state report, not a trace.</summary>
    private const float SamplingReportSeconds = 20f;

    /// <summary>
    /// NEW bake first-asks this rail may START in one frame. Same value and same reason as
    /// <c>PanelMipBake.MaxArrivalBakesPerFrame</c>: a first-sight sprite off an atlas this session
    /// has not read back yet costs a full-atlas GPU readback, and in VR a stutter is worse than the
    /// aliasing it removes. Over the cap the cap keeps the game's ORIGINAL sprite for that frame —
    /// aliased but ON TIME, never blank — and its source reference is left stale so the very next
    /// frame retries it. Eight caps share one guildmaster atlas in practice, so this binds at most
    /// on the frame the rail is first built.
    /// </summary>
    private const int MaxIconBakesPerFrame = 2;

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

        /// <summary>The GAME sprite each world copy was last RESOLVED from (ModBuild 199 mip swap).
        /// The world renderer no longer wears the game's own sprite instance, so the old
        /// "renderer.sprite != image.sprite" change gate would fire every single frame; the gate is
        /// now this remembered SOURCE reference. Left deliberately STALE when a bake is deferred by
        /// the per-frame rate cap, which is what makes the next frame retry that exact graphic.</summary>
        internal Sprite? IconSource;
        internal Sprite? GlowSource;
        internal Sprite? BadgeSource;

        internal MapButtonPoke Poke = null!;
        internal float IconWorldSize;
        internal bool Interactable;
        internal bool Hovered;

        /// <summary>The GAME GameObject this cap drives — the real <c>Toggle</c>'s object when the
        /// bar has one, the <c>UIGuildmasterButton</c>'s otherwise. Cached at build so the press and
        /// hover paths can never disagree about the target.</summary>
        internal GameObject? Target;

        /// <summary>
        /// The <c>UIWindow</c> this cap's destination owns, or null for a map surface and for a mode
        /// the HUD has no window for. Cached because <see cref="Pressable"/> asks per frame whether
        /// that window is standing, and resolving it goes through the HUD singleton and a
        /// <c>GetComponent</c> — which is exactly the per-frame cost ModBuild 226 and 230 refused to
        /// pay here, and the only part of the question that was ever expensive.
        ///
        /// <para>Re-resolved on every rail rescan rather than once at build: the HUD's serialized
        /// window references are populated in its own <c>Awake</c>, so a rail that stood up in the
        /// same frame as the HUD would otherwise cache a null for the session. A null is retried,
        /// never remembered as an answer.</para>
        /// </summary>
        internal UIWindow? DestinationWindow;

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

        /// <summary>
        /// THE PRESS VOCABULARY (R5/R11, 2026-09-05) — the shared depth-fire hysteresis and the
        /// shared cross-path debounce, the control board's own two rules, held here by value.
        ///
        /// <para>Until this landed, a table cap fired on 8 mm of fingertip CONTACT: no travel
        /// requirement, no cooldown, and a 7 mm stroke that was a 70 ms animation STARTED BY the
        /// press rather than the thing the press was measured from. Brushing CONFIRM on the
        /// control board did nothing; brushing <i>Händler</i> on this table opened the merchant.
        /// See <see cref="KeycapPress"/> for why the board's principle is the reference.</para>
        /// </summary>
        internal KeycapPressGate Gate;

        /// <summary>This frame's fingertip travel on this cap, 0..1 of <see cref="TravelWorld"/>.
        /// Written by <c>TickPokeDepths</c>, read by <c>TickTravel</c> in the next pass — it is a
        /// field only so the two halves of one frame's work can stay two methods.</summary>
        internal float FollowDepth;

        /// <summary>Which hand's fingertip is currently driving this cap's travel, or null. Set on
        /// the poke ENTER and cleared on the exit, so the finger-follow is armed on exactly the
        /// hand PokeInteractor says is on the cap (it keeps one hovered target per hand and pairs
        /// enter/exit strictly, which is what makes this safe to hold across frames).</summary>
        internal VRHand? PokeHand;
    }

    private static FieldInfo? _toggleField;
    private static FieldInfo? _iconField;
    private static FieldInfo? _groupField;
    private static FieldInfo? _highlightField;
    private static FieldInfo? _notificationField;
    private static bool _reflectionTried;

    private readonly List<Cap> _caps = new(8);
    private readonly List<UIGuildmasterButton> _scratch = new(8);

    /// <summary>Caps whose fingertip reached the fire depth this frame, held between the two phases
    /// of <see cref="TickPokeDepths"/> so the commit happens outside the loop over
    /// <see cref="_caps"/>. Reused, never reallocated; normally empty.</summary>
    private readonly List<(UIGuildmasterButton Button, VRHand Hand)> _pendingPress = new(2);
    private GameObject? _root;
    private int _scanFrame = int.MinValue;
    private float _scale = 1f;
    private bool _reported;
    private bool _emptyReported;
    private bool _fitWarned; // One significant missing-support warning per rail instance/session.
    private Cap? _laserHover;

    /// <summary>Which of <see cref="Rescan"/>'s two scans produced the current set — log material
    /// for the order line. The set is the same either way; the ORDER used to depend on it, which is
    /// the whole of the ModBuild 226 report.</summary>
    private string _scanSource = "(not scanned yet)";

    /// <summary>Reused by the order line so a rail build allocates one string, not nine.</summary>
    private static readonly System.Text.StringBuilder OrderSb = new(256);

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
            Rescan();
            // Rescan may Release an old set before geometry is ready. Record the attempt after
            // that reset so a missing parchment/support never turns scene discovery into 90 Hz.
            _scanFrame = Time.frameCount;
        }
        if (_caps.Count == 0)
            return;

        // THE DEPTH PASS RUNS FIRST AND ON ITS OWN (R5). SampleState reads FollowDepth to drive the
        // travel, so the measurement has to be complete before it starts — and the commit has to
        // happen OUTSIDE any loop over _caps, which is why it is a two-phase method rather than one
        // more statement inside SampleState's loop: a press dispatches into the game, which can
        // switch guildmaster mode, and this rail's own Release() clears _caps.
        TickPokeDepths();
        SampleState();
        TickLaser();

        // THE ALIASING INSTRUMENT (ModBuild 199). On its own slow cadence and only when a head
        // camera exists — it measures, it never treats, so a tick that finds nothing is a finding
        // and says so on its own line.
        float now = Time.unscaledTime;
        if (now >= _nextSamplingReport)
        {
            _nextSamplingReport = now + SamplingReportSeconds;
            MeasureIcons(Rig.VRRigDriver.HeadCamera);
            LogIconSampling();
        }
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
        _scanSource = "no UIGuildmasterButton was found at all";
        if (Singleton<UIGuildmasterHUD>.IsInitialized)
        {
            UIGuildmasterHUD hud = Singleton<UIGuildmasterHUD>.Instance;
            if (hud != null)
            {
                hud.GetComponentsInChildren(includeInactive: true, _scratch);
                if (_scratch.Count > 0)
                    _scanSource = "the subtree of Singleton<UIGuildmasterHUD>.Instance";
            }
        }
        // Fallback for a HUD that is not the singleton yet (or a version that parents the bar
        // elsewhere) — the component type is public, so this needs no name matching.
        if (_scratch.Count == 0)
        {
            UIGuildmasterButton[] sweep = Object.FindObjectsOfType<UIGuildmasterButton>(true);
            _scratch.AddRange(sweep);
            if (_scratch.Count > 0)
                _scanSource = "an Object.FindObjectsOfType<UIGuildmasterButton>(true) SCENE SWEEP "
                              + "(the HUD singleton was cold)";
        }

        // THE ORDER STOPS BEING THE SCAN'S (ModBuild 226) — user report, verbatim: "Die
        // Button-Reihenfolge am Tisch vor der Map ist nicht bei jedem Spieler die gleiche - das soll
        // nicht sein - jeder soll die gleiche Reihenfolge sehen."
        //
        // Neither line above produces an order the DATA states. The first is the depth-first
        // hierarchy order of whichever UIGuildmasterHUD won the singleton race — and the reporting
        // player's own log proves there were TWO of them in that scene (DESTINATIONS DISCOVERY
        // BASELINE at Player.log:3087: the sweep and the singleton "returned a DIFFERENT object"),
        // while the second player's agreed. The second is FindObjectsOfType, whose order Unity
        // documents as undefined and which is in practice a function of load order and instance ids,
        // i.e. of the PROCESS; the same session took it for 39 consecutive scans on one client and
        // for four whole 30 s windows on the other, so it is not a corner case either.
        //
        // The sort is applied HERE rather than in Build so that it also holds for the _scratch list
        // SameSet compares against — SameSet asks about membership only, which is why an order that
        // changed under a later rescan would never have rebuilt the rail anyway.
        _scratch.Sort(CompareByDeclaredRank);

        // Two empty sets are not proof that the native HUD was acquired. Build 530's early
        // SameSet return hid that case from the existing one-shot discovery diagnostic.
        if (_scratch.Count == 0)
        {
            if (_caps.Count != 0) Release("the guildmaster bar changed");
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
        if (SameSet())
        {
            RefreshDestinationWindows();
            return;
        }

        // The set changed (a mode switch rebuilds the bar) — rebuild from scratch rather than
        // reconciling: eight caps are cheap, and a partial reconcile is where stale references live.
        Release("the guildmaster bar changed");
        Build();
        RefreshDestinationWindows();
    }

    /// <summary>
    /// Re-resolve each destination cap's own <c>UIWindow</c> — the ONE expensive part of "is this
    /// destination standing", paid once per rescan (every <see cref="RescanIntervalFrames"/> frames)
    /// instead of per cap per frame. See <see cref="Cap.DestinationWindow"/>.
    ///
    /// <para>A null is never cached as an answer: the HUD populates its serialized window references
    /// in its own <c>Awake</c>, so a rail that stood up in the same frame would otherwise be blind
    /// for the session. Re-asking costs one HUD field read and one <c>GetComponent</c> per
    /// destination cap, at 6 Hz.</para>
    /// </summary>
    private void RefreshDestinationWindows()
    {
        for (int i = 0; i < _caps.Count; i++)
        {
            Cap c = _caps[i];
            if (c.Button == null)
                continue;
            c.DestinationWindow = GuildmasterDestinations.ModeWindow(c.Button.GuildmasterMode);
        }
    }

    /// <summary>
    /// THE RAIL'S ORDER, AS A COMPARISON. Low rank stands at local −X (the reading start for a
    /// player at this table edge — see the frame note in <see cref="Build"/>).
    ///
    /// <para>The rank itself lives in <c>GuildmasterDestinations.DeclaredOrder</c> and nowhere else,
    /// so "what order do the buttons stand in" is answered by ONE table that every client reads
    /// rather than by whatever the local process's scan happened to hand over. See section 6 of that
    /// class's doc for the two scans this replaces and for the log evidence that they disagreed.</para>
    ///
    /// <para>THE TIE-BREAK IS <c>name</c>, ORDINAL, and it is deliberately not an instance id: two
    /// buttons carrying the SAME mode is only reachable when the scene really does hold two
    /// guildmaster bars (which the reporting player's log says it did) and the sweep fallback picked
    /// up both. An instance id would sort them differently on every client, i.e. exactly the defect
    /// this method exists to remove; the GameObject names are prefab constants and are not
    /// localized. If even the names tie, the order between those two is genuinely undecidable from
    /// the data and the rail says so on its order line rather than pretending otherwise.</para>
    /// </summary>
    private static int CompareByDeclaredRank(UIGuildmasterButton a, UIGuildmasterButton b)
    {
        if (ReferenceEquals(a, b))
            return 0;
        // BOTH-NULL FIRST, and that is not defensive noise: Unity's operator== reports a DESTROYED
        // object as null, so two dead entries would otherwise both answer "I come after you" and
        // List<T>.Sort throws IComparer returns inconsistent results on a comparison that is not a
        // strict weak ordering. Dead entries sort to the end, where Build already skips them.
        if (a == null)
            return b == null ? 0 : 1;
        if (b == null)
            return -1;
        int ra = GuildmasterDestinations.Rank(a.GuildmasterMode);
        int rb = GuildmasterDestinations.Rank(b.GuildmasterMode);
        if (ra != rb)
            return ra < rb ? -1 : 1;
        return string.CompareOrdinal(a.name, b.name);
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

        // Guildmaster's native knife/bench layout leaves no supported near-edge rail. Keep
        // campaign placement unchanged and fit this mode's caps to the right-hand tabletop.
        bool guildmaster = GuildmasterRoomGeometry.Active;
        GuildmasterRoomLayout.Rail sideRail = default;
        bool supportedRail = true;
        if (guildmaster)
        {
            supportedRail = GuildmasterRoomGeometry.TryRail(parchment, seat, _scratch.Count,
                CapSizeMeters * _scale, CapGapMeters / CapSizeMeters, GlowFraction, out sideRail);
            if (!supportedRail && !_fitWarned)
            {
                _fitWarned = true;
                VRLog.Warn(Scope, "MAP TABLE BUTTONS: Guildmaster tabletop fit is unavailable; "
                    + "keeping the native actions on the right-side fallback rail. Furniture is unchanged.");
            }
            origin = new Vector3(0f, seat.TopY + RailLiftMeters * _scale, 0f);
        }

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

        float cap = guildmaster ? sideRail.Cap : CapSizeMeters * _scale;
        float gap = CapGapMeters * _scale;
        float depth = CapDepthMeters * _scale;
        float pitch = cap + gap;

        // THE SECOND ROW (user request, 2026-09-05): "die 'Gloomhaven' und 'Worldmap' Buttons
        // sollen in einer zweiten Reihe unterhalb der anderen sitzen" — for visual separation.
        //
        // "BELOW" IS A STEP IN THE RAIL'S OWN −Z, NOT IN Y, and the sign is worked out from this
        // method's own frame note rather than guessed. The caps lie FLAT (CapTiltDegrees = 0), so
        // there is no "below" in height at all — a second row in +Y would be a shelf floating over
        // the first one. On a table read from a seat, "below" is NEARER THE PLAYER. The root's
        // rotation is seat.Rotation, whose forward runs SEAT → MAP (the frame note above; 179 and
        // 180 both had this backwards and shipped caps facing away from the player), so +Z points
        // at the map and −Z points at the player. Row 1 therefore stands at −rowStep.
        //
        // The two rows are each CENTRED ON THEIR OWN SPAN, not left-aligned to a shared origin: six
        // caps over two would otherwise read as a row with a gap in it rather than as two groups.
        //
        // WHICH cap goes in which row is GuildmasterDestinations.RailRow — declared beside the order
        // table, never scanned — and within a row the caps keep their declared rank order, because
        // _scratch is already sorted and this loop preserves it.
        int[] rowCount = new int[GuildmasterDestinations.RailRowCount];
        for (int i = 0; i < _scratch.Count; i++)
        {
            if (_scratch[i] == null)
                continue;
            int r = GuildmasterDestinations.RailRow(_scratch[i].GuildmasterMode);
            if (r >= 0 && r < rowCount.Length)
                rowCount[r]++;
        }
        float[] rowX = new float[rowCount.Length];
        for (int r = 0; r < rowCount.Length; r++)
        {
            float span = pitch * rowCount[r] - gap;
            rowX[r] = -span * 0.5f + cap * 0.5f;
        }
        int[] rowPlaced = new int[rowCount.Length];
        float rowStep = cap + RowGapMeters * _scale;

        int built = 0;
        int withIcon = 0;
        int withGlow = 0;
        for (int i = 0; i < _scratch.Count; i++)
        {
            UIGuildmasterButton button = _scratch[i];
            if (button == null)
                continue;
            int row = GuildmasterDestinations.RailRow(button.GuildmasterMode);
            if (row < 0 || row >= rowCount.Length)
                row = 0;
            var localPos = new Vector3(rowX[row] + pitch * rowPlaced[row], 0f, -rowStep * row);
            rowPlaced[row]++;
            if (guildmaster)
            {
                sideRail.Position(built, out float x, out float z);
                localPos = new Vector3(x, 0f, z);
            }
            Cap c = BuildCap(button, localPos, capLocalRot, cap, depth);
            _caps.Add(c);
            built++;
            if (c.Icon != null) withIcon++;
            if (c.Glow != null) withGlow++;
        }
        VRLayers.Apply(_root);
        LogResolvedOrder(guildmaster, sideRail.Columns);

        if (!_reported)
        {
            _reported = true;
            if (guildmaster)
            {
                if (VRLog.WantsDebug)
                    VRLog.Info(Scope, $"MAP TABLE BUTTONS: {built} Guildmaster caps on the right "
                        + $"{(supportedRail ? "tabletop" : "fallback rail")}, "
                        + $"{sideRail.Columns} columns, {cap / _scale * 1000f:F1} mm faces, "
                        + $"first seat-frame centre ({sideRail.X:F2}, {sideRail.Z:F2}). Native HUD callbacks unchanged.");
                return;
            }
            VRLog.Info(Scope, $"MAP TABLE BUTTONS: {built} cap(s) standing on the table rim at {origin}, "
                              + $"{RailInsetMeters:F3} m (real) outside the map's near edge on the seat's "
                              + $"own view side {side}, {CapSizeMeters * 1000f:F0} mm faces tilted "
                              + $"{CapTiltDegrees:F0}° up, rig scale {_scale:F2}, in "
                              + $"{GuildmasterDestinations.RailRowCount} ROWS "
                              + $"({rowCount[0]} destination cap(s) in the far row nearest the map, "
                              + $"{rowCount[1]} map-surface cap(s) in the near row {RowGapMeters * 1000f:F0} mm "
                              + "toward the player — 'below' on a table of flat caps is a step in the "
                              + "rail's own −Z, not in Y; see MAP TABLE BUTTON ORDER for the frame). "
                              + "The set was READ off the "
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

    /// <summary>
    /// ONE LINE, ONE RAIL BUILD: the order the caps actually ended up in, left to right, so two
    /// players' logs can be diffed against each other directly instead of being reasoned about.
    ///
    /// <para>WHY THIS LINE HAD TO BE ADDED BEFORE THE FIX COULD BE JUDGED. The ModBuild 225 session
    /// carries two full logs of the same game and NEITHER records the rail's order — the build line
    /// says "8 cap(s) standing" and stops there. The difference had to be inferred from the
    /// MAP BUTTON ICON SAMPLING report's WORST cap, which is the cap FARTHEST from the eye: with an
    /// identical rail origin (-110.16, 1.17, 0.18) and an identical rig scale (198.12) on both
    /// clients, the reporting player's worst was 'Merchant' 59 times and 'TownRecords' 34 and never
    /// once 'WorldMap', while the second player's was 'WorldMap' 46 times and 'TownRecords' 14 and
    /// never once 'Merchant'. Disjoint ends, same eight caps. That is enough to believe the report
    /// and not enough to state the two orders, which is what this line is for.</para>
    ///
    /// <para>It also names the SCAN that produced the set, and flags any mode the declared table
    /// does not rank and any two caps that tied — the three ways this can still go wrong.</para>
    /// </summary>
    private void LogResolvedOrder(bool guildmaster, int columns)
    {
        System.Text.StringBuilder sb = OrderSb;
        sb.Length = 0;
        sb.Append("MAP TABLE BUTTON ORDER: ");
        if (guildmaster)
        {
            sb.Append($"Guildmaster right tabletop, {columns} column(s), far to near: ");
            for (int i = 0; i < _caps.Count; i++)
            {
                if (i > 0) sb.Append(i % columns == 0 ? " | " : ", ");
                sb.Append(_caps[i].Button.GuildmasterMode);
            }
            VRLog.Note(Scope, sb.ToString());
            return;
        }
        int unranked = 0;
        int ties = 0;
        int lastRank = int.MinValue;
        // ONE PASS PER ROW, so the line reads the way the table looks. Walking _caps once would
        // interleave the rows (the list is in declared-rank order, and rank does not respect the row
        // partition), and a line that prints WorldMap between Trainer and Temple would be describing
        // a rail nobody can see — which is the failure the ModBuild 226 order line exists to end.
        for (int row = 0; row < GuildmasterDestinations.RailRowCount; row++)
        {
            int inRow = 0;
            for (int i = 0; i < _caps.Count; i++)
            {
                UIGuildmasterButton button = _caps[i].Button;
                EGuildmasterMode mode = button != null ? button.GuildmasterMode : EGuildmasterMode.None;
                if (GuildmasterDestinations.RailRow(mode) != row)
                    continue;
                int rank = GuildmasterDestinations.Rank(mode);
                if (!GuildmasterDestinations.IsRanked(mode))
                    unranked++;
                if (inRow > 0 && rank == lastRank)
                    ties++;
                lastRank = rank;
                if (inRow == 0)
                    sb.Append(row == 0 ? "ROW 0 (far, nearest the map): " : "  ||  ROW 1 (near, "
                                                                            + "toward the player): ");
                else
                    sb.Append(" | ");
                sb.Append(inRow + 1).Append(' ').Append(mode.ToString());
                inRow++;
            }
            if (inRow == 0)
                sb.Append(row == 0 ? "ROW 0 (far, nearest the map): <empty>"
                                   : "  ||  ROW 1 (near, toward the player): <empty>");
        }
        sb.Append(". READ IT AS: within each row, the caps left to right along the rail's own +X, "
                  + "which is the reading direction for a player at this table edge. THE ROWS RUN "
                  + "TOWARD THE PLAYER: the rail root's forward is seat.Rotation, i.e. seat -> map, "
                  + "so +Z points AT THE MAP and each further row steps in −Z, which on a table of "
                  + "caps lying flat (CapTiltDegrees = 0) is what 'below' means to someone reading "
                  + "it from the seat. ROW 1 IS EXACTLY THE TWO MAP SURFACES (WorldMap, City) — "
                  + "user request 2026-09-05, 'zur optischen Trennung' — and it is the partition the "
                  + "press path already had: those are the two modes that are not windows and cannot "
                  + "be closed, only switched between. BOTH THE ORDER AND THE ROW ARE DECLARED, NOT "
                  + "SCANNED (ModBuild 226; the row since 2026-09-05): they are "
                  + "GuildmasterDestinations.DeclaredOrder and .RailRow, two tables every client "
                  + "compiles in, so two players' rails are identical by construction and this line "
                  + "can be diffed between two logs directly. Before 226 the order was "
                  + "whatever the local scan handed over — a transform hierarchy walk of whichever "
                  + "UIGuildmasterHUD won the singleton race, or, while that singleton was cold, an "
                  + "Object.FindObjectsOfType sweep whose order Unity documents as undefined. ");
        sb.Append("The set for THIS build came from ").Append(_scanSource)
          .Append(" (the set is the same either way; only the order used to depend on it). ");
        sb.Append(unranked == 0
            ? "Every mode on the bar is named by the declared table."
            : $"{unranked} cap(s) carry a mode the declared table does NOT name — they are appended "
              + "after the named ones in enum order, which is still identical on every client, but "
              + "if a bar button shows up at the far end unexpectedly this is why and the table is "
              + "where to add it.");
        if (ties > 0)
            sb.Append($" {ties} adjacent pair(s) share a rank, i.e. two buttons carry the SAME mode — "
                      + "that only happens when the scene really does hold two guildmaster bars and "
                      + "the sweep fallback picked up both. They are ordered by GameObject name; if "
                      + "the names also tie, their relative order is not decidable from the data.");
        // HW-VERIFY
        // PROMOTED TO Note 2026-09-05 WITH THE SECOND ROW. Until this build the line described one
        // strip and the only thing it had to say was an order two logs could be diffed on. It now
        // states a GEOMETRIC claim — that row 1 steps in the rail's own −Z, i.e. toward the player —
        // and that claim has a sign that can be wrong. If it is wrong the second row stands BEHIND
        // the first, half over the map, and this line is the only thing in the log that says which
        // sign shipped; the alternative is another photograph. Printed once per rail build.
        VRLog.Note(Scope, sb.ToString());
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
    /// <c>Cards.PlayTray.BoardButton</c> uses for its keycaps, so the table buttons are made of the same
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
    private static void TickTravel(Cap c, float follow01)
    {
        if (c.Body == null)
            return;
        // THE FINGER OWNS THE CAP WHILE IT IS ON IT (R5, 2026-09-05). The travel used to be
        // driven ONLY by the post-press hold below, i.e. the cap moved because a press had already
        // been committed. It moves under the finger now, which is what makes the depth-fire
        // legible: the player sees the key going down and feels it fire at the bottom, instead of
        // seeing it flick after something already happened. The hold is unchanged and still owns
        // the LASER press and the springback, so a press with no finger on it still shows.
        //
        // MAX, not sum: the two sources are two accounts of the same one cap, and a finger resting
        // at the bottom during the 70 ms hold must not push it through the socket floor.
        bool down = Time.unscaledTime < c.PressedUntil;
        float target = Mathf.Max(down ? c.TravelWorld : 0f, follow01 * c.TravelWorld);
        if (Mathf.Approximately(c.Depth, target))
            return;
        // The finger's own contribution is not eased — it IS the finger, and easing it would put
        // the cap somewhere the fingertip is not. Only the hold's rise and fall are timed.
        if (follow01 > 0f && target > c.Depth)
        {
            c.Depth = target;
        }
        else
        {
            float tau = down ? PressDownSeconds : PressUpSeconds;
            c.Depth = Mathf.MoveTowards(c.Depth, target,
                                        c.TravelWorld * Time.unscaledDeltaTime / Mathf.Max(tau, 1e-4f));
            if (Mathf.Abs(c.Depth - target) < c.TravelWorld * 0.01f)
                c.Depth = target;
        }
        // +Z is INTO the socket: the cap's -Z faces the player (see the frame note in Build).
        c.Body.localPosition = new Vector3(0f, 0f, c.Depth);
    }

    /// <summary>
    /// <b>THE DEPTH-FIRE, ON THE MAP TABLE'S CAPS.</b> Measure how far each cap's own fingertip has
    /// pushed it, step the shared gate, and commit at the bottom — the control board's rule
    /// (<see cref="KeycapPress"/>) applied to the family that had no rule at all.
    ///
    /// <para><b>WHY THERE IS NO GRIP CHORD HERE, unlike the board's.</b> The board's fingertip
    /// commit additionally requires the same hand's grip held and empty, because a tray keycap
    /// calls a non-undoable game API (END TURN) and because dragging the CONTROL BOARD ITSELF puts
    /// the dragging hand's fingertip inches from its own keycaps. Neither premise holds at the map
    /// table: a cap here dispatches a pointerClick into the game's own guildmaster Toggle, every
    /// destination it opens can be closed again by pressing the same cap (the ModBuild 200/226/230
    /// ruling, quoted in <see cref="Press"/>), and the table is not a grabbable the player carries
    /// past its own buttons. Adding the chord would mean a player has to hold grip to press a
    /// button that lies flat on a table in front of him, which is not what the requirement asked
    /// for — it asked that an ACCIDENTAL press be impossible, and the travel requirement is what
    /// delivers that here.</para>
    /// </summary>
    /// <summary><b>PHASE 1 — measure every cap, commit nothing.</b> Kept separate from the
    /// dispatch below because a press reaches into the game (a guildmaster mode switch, a window
    /// open or close) and this rail's own <see cref="Release"/> clears <c>_caps</c>: firing from
    /// inside a loop over that list is a mutation-during-iteration waiting for the one frame the
    /// room tears down under a fingertip. The caps that fired are collected first and pressed
    /// after the loop has closed, from a snapshot; <see cref="Press"/> re-resolves its own cap and
    /// tolerates one that has since gone.</summary>
    private void TickPokeDepths()
    {
        _pendingPress.Clear();
        for (int i = 0; i < _caps.Count; i++)
        {
            Cap c = _caps[i];
            if (c.Go == null || c.Button == null)
                continue;

            VRHand? hand = c.PokeHand;
            if (hand == null || !Pressable(c))
            {
                // Nothing is pushing this cap, so it has retracted by definition.
                c.Gate.Rearm();
                c.FollowDepth = 0f;
                continue;
            }

            // The board's own measurement (WorldUI.KeycapPress.FollowDepth01 — one implementation,
            // both families) on this rail's own mirrored contact radius. The MEASUREMENT is shared;
            // the NUMBERS going into it are each family's own, which is why the map table keeps its
            // 7 mm travel and the board keeps its configurable 4 mm.
            c.FollowDepth = KeycapPress.FollowDepth01(c.Collider, hand, FingertipRadius,
                                                      c.TravelWorld, c.Go.transform.lossyScale.z);
            if (c.Gate.AtFireDepth(c.FollowDepth) && c.Gate.TryFireFromDepth())
                _pendingPress.Add((c.Button, hand));
        }

        // PHASE 2 — dispatch. Normally empty; at most one entry per hand.
        for (int i = 0; i < _pendingPress.Count; i++)
        {
            (UIGuildmasterButton button, VRHand hand) = _pendingPress[i];
            Press(button, $"{hand.Side} fingertip (depth-fire)", hand: hand);
        }
        _pendingPress.Clear();
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

            TickTravel(c, c.FollowDepth);

            float groupAlpha = c.Group != null ? c.Group.alpha : 1f;
            // ModBuild 200: THE ONE TERM THAT MOVED. Was
            //     c.Button.IsActive && c.Toggle != null && c.Toggle.IsInteractable()
            // — the GAME's flat-bar predicate, which greys a cap for two states the map room does not
            // share: a bar the game has REMOVED from the flat screen (a quest is selected) and the
            // mode that is currently OPEN (which in the room is the one a second press closes). It is
            // now the ROOM's predicate. Everything else below — sprite, tint, CanvasGroup alpha, the
            // icon's scale animation, the highlight pulse, the badge — is untouched and still sampled
            // live off the game's own graphics, exactly as the user's ruling requires. See the class
            // doc for the hardware evidence and the rejected alternatives.
            bool live = Pressable(c);
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
                    DropHover(c, GuildmasterDestinations.IsMapSurfaceMode(c.Button.GuildmasterMode)
                        ? "this map surface is the one the room is standing on — its cap is inert"
                        : "the game turned this button off");
                }
            }

            // THE SYMBOL. Read per frame, not once: the game re-assigns it from
            // UIInfoTools.GetGuildmasterModeSprite on every SetMode.
            if (c.Icon != null && c.IconImage != null)
            {
                // ModBuild 199: the sprite the world quad wears is the MIP-BAKED equivalent of
                // whatever the game currently has on its Image (see ResolveSprite). Everything below —
                // tint, alpha, the scale animation — is unchanged and still read live, so the swap
                // changes only WHICH TEXELS are sampled, never what is animated.
                ResolveSprite(c.Icon, c.IconImage.sprite, ref c.IconSource);
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
                    ResolveSprite(c.Glow, c.HighlightImage!.sprite, ref c.GlowSource);
                    Color gc = c.HighlightImage.color;
                    gc.a *= groupAlpha;
                    if (c.Glow.color != gc)
                        c.Glow.color = gc;
                    float ratio = c.HighlightBaseScale.x > 1e-4f
                        ? c.HighlightImage.transform.localScale.x / c.HighlightBaseScale.x
                        : 1f;
                    float size = c.IconWorldSize / IconFraction * GlowFraction * ratio;
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
                    ResolveSprite(c.Badge, c.NotificationImage.sprite, ref c.BadgeSource);
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
                // R15: the dim factor is NativeButtonSkin.DisabledLabelDim now — the same number the
                // control board's keycaps started dimming their captions by on 2026-09-05. This side
                // is unchanged; it was the reference.
                c.Fallback.color = live ? NativeButtonSkin.LabelColor
                                        : NativeButtonSkin.LabelColor * NativeButtonSkin.DisabledLabelDim;
            }
        }
    }

    // ---- WHAT A PRESS IN THIS ROOM ACTUALLY DOES (ModBuild 200) -------------------------------

    /// <summary>
    /// THE ROOM'S PRESSABILITY PREDICATE — the single source of both the cap's LOOK and its
    /// COLLIDER, and the same three questions <see cref="Press"/> answers in the same order. They
    /// share this method rather than agreeing by inspection: the whole ModBuild 200 defect was an
    /// appearance and a behaviour computed from two different things.
    ///
    /// <para>Read it as: can a press on this cap reach the game and make it do something? It is the
    /// AND of two questions that are deliberately separate methods since 2026-09-05, because they
    /// had started answering for each other:</para>
    /// <list type="number">
    ///   <item><see cref="Deliverable"/> — can a pointer event REACH this button at all;</item>
    ///   <item><see cref="HasSomethingToDo"/> — would the game, or this room, do anything with
    ///   it.</item>
    /// </list>
    ///
    /// <para><c>Toggle.IsInteractable()</c> on an inactive object returns the CACHED group flag
    /// (uGUI's <c>Selectable.m_GroupsAllowInteraction</c> is only recomputed in
    /// <c>OnCanvasGroupChanged</c>, which does not run while the object is off), so it is read only
    /// AFTER the deliverability question — never as a proxy for it.</para>
    ///
    /// <para>ModBuild 226 DELIBERATELY DID NOT ADD A FOURTH CLAUSE HERE for its own new close case
    /// (the mode's window still standing while the mode machine has moved on — see
    /// <see cref="Press"/>). Clause 2 already covers it and covers it exactly: the game marks the
    /// toggle non-interactable only WHILE the mode is on (<c>RefreshSelected</c>, :209), so in the
    /// leftover-window state the toggle is interactable and this method already answers yes. Adding
    /// the test would have put <c>ModalFallback</c>'s converted-window scan into a predicate that
    /// runs for every cap on every frame — the ModBuild 196 mistake, for a boolean that is already
    /// true.</para>
    ///
    /// <para>ModBuild 230 KEEPS THAT CALL, AND IT IS NOW LOAD-BEARING RATHER THAN MERELY THRIFTY.
    /// <c>GuildmasterDestinations.Decide</c> samples the float set, and it does so ONCE PER TRIGGER
    /// PULL. If this predicate — which runs for every cap on every frame — asked the same question,
    /// the room would pay a converted-window walk per cap per frame for a value that decides nothing
    /// until a press happens. Nothing in the ModBuild 230 change is per-frame; see section 8 of
    /// <c>GuildmasterDestinations</c> for the cost statement.</para>
    ///
    /// <para><b>AND 2026-09-05 DOES ASK PART OF IT PER FRAME, WITH THE COST PAID DOWN RATHER THAN
    /// WAVED THROUGH.</b> <see cref="HasSomethingToDo"/>'s standing term is
    /// <c>GuildmasterDestinations.IsStanding</c>, which walks the converted set. What ModBuild
    /// 226/230 refused was <c>Sample</c> — which resolves the destination window through the HUD
    /// singleton and a <c>GetComponent</c> on every call. That resolution is now done ONCE per rail
    /// rescan and cached on the cap (<see cref="Cap.DestinationWindow"/>), so what is left per cap
    /// per frame is one early-exit walk of a list that holds at most a handful of floats, one
    /// dictionary probe and one bool field. The reason it has to be per frame at all is that it is
    /// the answer to "is this destination on the table", and that changes without any press.</para>
    /// </summary>
    private static bool Pressable(Cap c) => Deliverable(c) && HasSomethingToDo(c);

    /// <summary>
    /// CAN A POINTER EVENT REACH THIS BUTTON AT ALL? <c>ExecuteEvents</c> silently drops everything
    /// aimed at an inactive GameObject, so this is a hard no and it is asked FIRST.
    ///
    /// <para><b>THIS USED TO HAVE AN ESCAPE HATCH AND IT WAS A LIE (user item 6, 2026-09-05).</b>
    /// The clause read <c>!c.Button.IsActive &amp;&amp; !CanRevealBar()</c>, and <c>CanRevealBar()</c>
    /// answered "a map location is selected" — a fact about the MAP, identical for every cap on the
    /// rail. So the moment the player selected a quest, the game removed its whole option bar
    /// (<c>AdventureMapUIManager.OnSelectedMapLocation</c> :357 →
    /// <c>EnableHeadquartersOptions(this, false)</c> → <c>optionsContainer.SetActive(false)</c>) and
    /// this predicate lit ALL EIGHT CAPS AT ONCE and gave every one of them a collider. His words:
    /// <i>"Solange der point of no return nicht überschritten ist soll das Öffnen einer Quest gar
    /// keinen Einfluss auf die Aktivierung der Buttons haben."</i></para>
    ///
    /// <para><b>AND THE PRESS THAT FOLLOWED ATE HIS SELECTION AND DELIVERED NOTHING.</b>
    /// <see cref="Press"/> dropped the selected location through the game's own
    /// <c>DeselectCurrentMapLocation</c> and only then re-tested deliverability. In SINGLE player the
    /// bar comes back on that call and the press lands; IN MULTIPLAYER IT DOES NOT, and the reason is
    /// in the decompile rather than in a guess: <c>disableOptionsRequests</c> is a SET, and while a
    /// quest is selected online <c>UIMapMultiplayerController.ToggleReadyUpUI(show: true)</c> (:405)
    /// has put the multiplayer controller in it as a second entry, which nothing this mod may touch
    /// removes. His host log has the whole sequence twice, for 'Trainer' and for 'Enchantress': the
    /// deselect line, then <c>"…its game object 'Trainer Button' is INACTIVE, so no pointer event can
    /// be delivered to it at all … and nothing was dispatched."</c> One trigger pull, quest selection
    /// gone, nothing opened.</para>
    ///
    /// <para><b>SO THE HATCH IS DELETED, NOT NARROWED.</b> A cap that cannot deliver a click must not
    /// look pressable — the standing ruling about exit controls, generalised: a click on a control
    /// must end in the thing it promises or in a visible refusal, never in silence. With the bar
    /// down, the caps are dim and inert, which is exactly what the flat game shows in the same state
    /// (it removes the bar outright).</para>
    ///
    /// <para><b>WHAT THIS COSTS, STATED, BECAUSE IT IS A CHANGE AGAINST AN EARLIER RULING.</b>
    /// ModBuild 200 answered <i>"man den Händler und co. jederzeit mit dem button aufrufen kann,
    /// auch wenn gerade eine Quest ausgewählt ist"</i> with the reveal. That is now gone in SINGLE
    /// player too, where it did work: with a quest selected, the destination caps are dim until the
    /// selection is dropped — which the player still does from the table
    /// (<c>MapLocationInteractor</c>, ModBuild 183). Keeping it for single player only was rejected:
    /// it would need the private <c>disableOptionsRequests</c> set read per frame to decide whether
    /// the reveal would work, and it would leave the same press quietly eating a selection on the
    /// one machine where it does. The residual is honest and visible; the old behaviour was neither.
    /// </para>
    ///
    /// <para><b>AND 2026-09-05 (SECOND ROUND) PUT THE HATCH BACK — WITHOUT THE WRITE THAT MADE THE
    /// FIRST ONE A LIE.</b> He rejected the residual above in the same words he had already used
    /// once: <i>"Alle Buttons wie Händler und co. sollen drückbar bleiben bis zum point of no
    /// return. Aktuell grauen alle buttons aus wenn eine Quest ausgewählt ist."</i> Both previous
    /// answers moved the SAME term — first light the cap and drop his selection to make the press
    /// land, then darken the cap and refuse the press — and both were arguing about
    /// <c>optionsContainer.activeSelf</c>, which is the GAME'S flag, written by three of its own
    /// methods (<c>RefreshVisibilityHeadquartersOptions</c> :739-751, <c>HandleTempleState</c>
    /// :581-593, <c>HandleMerchantState</c> :595-606). The standing ruling for that shape is
    /// <i>do not win a write war — concede the flag and own the number</i>, and the number here is
    /// not the container's active state at all: it is <c>UIGuildmasterHUD.currentMode</c>. See
    /// <see cref="SelectThroughTheGamesOwnApi"/>, which reaches that number through
    /// <c>UIGuildmasterButton.Select()</c> — a plain public method call that needs no active
    /// GameObject, no pointer event and no <c>ExecuteEvents</c>, so the bar can stay exactly as
    /// down as the game wants it.</para>
    ///
    /// <para><b>THE DISCRIMINATOR IS EXACT AND IT IS THE GAME'S OWN TWO WRITERS.</b> A cap must NOT
    /// come alive for a mode the game has LOCKED, and until now one term covered both cases because
    /// <c>IsActive</c> is <c>activeInHierarchy</c>. They are two different writers touching two
    /// different objects:</para>
    /// <list type="bullet">
    ///   <item><b>THIS BUTTON'S OWN GameObject</b> — <c>GuildmasterMode.RefreshUnlocked</c>
    ///   (decompiled GuildmasterMode.cs:53-56) is the ONLY line in the game that writes it:
    ///   <c>button.gameObject.SetActive(IsUnlocked)</c>. <c>activeSelf</c> false therefore means
    ///   THIS MODE IS LOCKED, and the cap stays dim and inert exactly as it does today.</item>
    ///   <item><b>AN ANCESTOR</b> — <c>optionsContainer</c>, i.e. the whole bar, which is a fact
    ///   about the MAP and identical for all eight caps. That is the state the user says must not
    ///   reach the buttons.</item>
    /// </list>
    /// <para>So <c>activeSelf &amp;&amp; !activeInHierarchy</c> is not a heuristic: it is "this
    /// button is unlocked and something above it was switched off", and the only thing above it the
    /// game switches is the bar.</para>
    ///
    /// <para>THE OLD RESIDUAL IS GONE WITH ITS CAUSE. Nothing on this path calls
    /// <c>DeselectCurrentMapLocation</c> any more and nothing writes to the map — the press cannot
    /// touch the player's quest selection in single player or online, which is what made the
    /// ModBuild 200 reveal unsafe. The <c>disableOptionsRequests</c> set is never read, never
    /// removed from and never guessed at.</para>
    /// </summary>
    private static bool Deliverable(Cap c) =>
        c.Button != null && (c.Button.IsActive || BarIsDown(c.Button));

    /// <summary>
    /// Is this button unreachable ONLY because the game has taken the whole option bar off the
    /// screen — as opposed to because this mode is locked? See <see cref="Deliverable"/> for the
    /// two writers this separates. A cheap pair of native property reads, no allocation.
    /// </summary>
    private static bool BarIsDown(UIGuildmasterButton button) =>
        button.gameObject.activeSelf && !button.gameObject.activeInHierarchy;

    /// <summary>
    /// WOULD THE GAME, OR THIS ROOM, DO ANYTHING WITH THE PRESS? The three mode-shaped rules,
    /// unchanged in intent since ModBuild 200/231 — with the LAST one moved off the game's mode
    /// enum, which cannot answer it any more.
    /// </summary>
    private static bool HasSomethingToDo(Cap c)
    {
        // ModBuild 231, belt and braces past the point of no return (user ruling: "zu diesem
        // Zeitpunkt ist der 'Point of Return' schon überschritten, d.h. zB Händler und co. darf man
        // zu diesem Zeitpunkt nicht mehr öffnen können"). The primary enforcement is the GAME'S own
        // state — StoryComposite drives UIGuildmasterButton.ToggleGreyOut, and the clause below
        // already reads c.Toggle.IsInteractable(), so the cap goes dead and dark by itself and its
        // pulse stops. This line exists because that is a restore-bearing mutation of someone
        // else's component: if a teardown ever left one button interactable, the cap must still
        // refuse rather than dispatch into a flow that has passed its commit point.
        if (StoryComposite.PointOfNoReturn)
            return false;
        if (c.Button == null || c.Toggle == null)
            return false;

        // ModBuild 231 — A MAP-SURFACE CAP ANSWERS TO THE SURFACE, NOT TO THE CURRENT MODE.
        //
        // User, verbatim: "Wird der Händler gedrückt, reagiert der Button der World-Map, er wird
        // ausgegraut und wieder nicht. Da sollte es gar keine Wechselwirkung mehr geben."
        //
        // THE REAL CHAIN, and it is NOT the one the report was filed against. The obvious suspect is
        // the close's own dispatch — GuildmasterDestinations.ReturnHome presses the bar's map Toggle
        // through PressMode, which lands in Press() below, and Press() moved the cap. That IS a real
        // coupling and it is fixed there (the travel is now gated on a PHYSICAL press). But it is a
        // cap going DOWN and coming back up, not a cap greying, and "ausgegraut" is the word he used.
        //
        // The greying is this method, and it needs no dispatch at all. The game keeps its bar as a
        // uGUI ToggleGroup and every button greys ITSELF when it becomes the selected one:
        // UIGuildmasterButton.RefreshSelected (decompiled UIGuildmasterButton.cs:206-219) runs
        // `toggle.interactable = !toggle.isOn` and `icon.material = disabledGrayscaleMaterial`.
        // So the moment the merchant becomes the current mode, the World-Map button is DEselected,
        // its RefreshSelected runs with isOn=false, and its toggle turns interactable — clause 2
        // below then said "pressable" and the cap lit up. Close the merchant, the map is selected
        // again, interactable goes false, clause 3 asks IsClosableMode(WorldMap) which is FALSE by
        // design (a map is not a window; "closing" it would mean switching the player to the other
        // map), and the cap greys. Grey, lit, grey — once per merchant press, with nothing ever
        // touching that cap. The destination caps do not show it because clause 3 answers TRUE for
        // them, which is the ModBuild 200 fix.
        //
        // THE FIX IS TO ASK THE RIGHT QUESTION. WorldMap and City are not toggles; they are a
        // mutually-exclusive PAIR — which surface is this room standing on. That fact is
        // GuildmasterDestinations.HomeSurface, it is sticky across destination modes by construction
        // (TrackHomeMode only writes it for the two surfaces), and it changes only when the player
        // actually switches maps. So: the cap for the surface you are ON is inert and dimmed; the cap
        // for the OTHER surface is live and takes you there. Opening or closing the merchant moves
        // neither of them, which is what he asked for.
        //
        // LOOK AND BEHAVIOUR STILL COME FROM ONE PREDICATE, which is the invariant this method
        // exists to keep (ModBuild 200's defect was an appearance and a behaviour computed from two
        // different things). Every press a PLAYER can make goes through the collider this returns:
        //   * cap live  ⇔ this surface is not home ⇒ it is not the current mode either ⇒
        //     GuildmasterDestinations.Decide answers Open and the press dispatches. Honest.
        //   * cap dim   ⇔ this surface IS home ⇒ collider off ⇒ no physical press exists to be
        //     refused. Honest.
        // Decide() is therefore UNCHANGED: the only caller that can still reach it for a map surface
        // is PressMode, which is the close's own game-side dispatch and bypasses the collider on
        // purpose. That dispatch MUST keep going out — it is the only thing that runs the mode's
        // Exit, and without Exit the party display stays in selection mode with every character slot
        // non-interactable (UIGuildmasterHUD.UpdateCurrentMode:435-460; ModBuild 184/195).
        //
        // WHAT THIS COSTS, STATED. While a destination is open, the home cap is now inert, so the
        // player can no longer press "World Map" to leave the merchant — he closes the merchant with
        // the merchant's own cap (the toggle contract since ModBuild 230) or with the window's X.
        // That route was used exactly ONCE in the whole ModBuild 230 session (Player.log:10396) and
        // it is the route that produces an ORPHANED float: it exits the mode game-side while the map
        // room's sticky parallel float survives the release loop, which is the ModBuild 229 defect
        // this family has already been fixed for twice. Removing it removes a way back into it.
        // `IsInteractable()` is still required, so a surface the GAME greys (an un-unlocked city,
        // UIGuildmasterHUD.IsAvailable) stays dim and inert exactly as it does today.
        if (GuildmasterDestinations.IsMapSurfaceMode(c.Button.GuildmasterMode))
            return c.Button.GuildmasterMode != GuildmasterDestinations.HomeSurface
                   && c.Toggle.IsInteractable();

        if (c.Toggle.IsInteractable())
            return true;

        // THE SECOND-PRESS-CLOSES CLAUSE, AND ITS TERM MOVED OFF THE GAME'S MODE ENUM
        // (user item 4, 2026-09-05).
        //
        // It read `c.Toggle.isOn && IsClosableMode(mode)`. `isOn` is the game's bar ToggleGroup and
        // that group is SINGLE-MODE by construction: UIGuildmasterButton.RefreshSelected (decompiled
        // :209) runs `toggle.interactable = !toggle.isOn`, and UIGuildmasterHUD keeps exactly one
        // mode current. So the pair of clauses asked, in effect, "is this the one mode the game
        // says is open?" — and the whole point of this room, restored the same day in
        // ModalFallback's StickinessSpentByAnsweredDecision, is that SEVERAL destinations stand open
        // in parallel here. He asked for it in those words: the merchant and the sorceress together.
        // The game's enum cannot name two, so for every standing destination but one the answer came
        // out of clause 2 instead, which is a different question that happens to agree — until it
        // does not, and then a cap sits dark over a panel that is plainly on the table.
        //
        // "IS THIS DESTINATION STANDING" IS THE MOD'S OWN QUESTION AND IT HAS ONE DEFINITION:
        // GuildmasterDestinations.IsStanding, the same three signals Decide closes on, object-keyed,
        // reading the float set rather than the mode. Look and behaviour therefore still come from
        // one place — the ModBuild 200 invariant this method exists to keep — but they now come from
        // the place that can answer for a room with two windows in it.
        //
        // The window reference is CACHED on the cap (see Cap.DestinationWindow), so nothing here
        // resolves anything through the HUD singleton; see the cost paragraph in Pressable's doc.
        return IsClosableMode(c.Button.GuildmasterMode)
               && GuildmasterDestinations.IsStanding(c.DestinationWindow);
    }

    /// <summary>
    /// Is this cap's mode a WINDOW that a second press can close, or is it the map itself?
    ///
    /// <para>THE SIX THAT ARE WINDOWS: <c>Merchant</c>, <c>Temple</c>, <c>Trainer</c>,
    /// <c>Enchantress</c>, <c>TownRecords</c> and <c>MercenaryLog</c> — the five destination
    /// <c>UIWindow</c>s <c>GuildmasterDestinations.IsDestination</c> matches, plus MercenaryLog,
    /// which is a sixth MODE sharing <c>UITownRecordsWindow</c> (UIGuildmasterHUD.cs:248-254) and so
    /// has no window class of its own to be matched by. Closing any of them means returning to the
    /// map, which is what the window's own X already does.</para>
    ///
    /// <para>THE TWO THAT ARE NOT: <c>WorldMap</c> and <c>City</c> are map SURFACES, not windows —
    /// this entire room is built on one of them. "Closing" a map has no meaning, and the only thing
    /// the mode machine could do instead is switch to the other one, i.e. silently move the player
    /// between the world map and the city on a second press. Refused, by name, and the refusal is
    /// logged the first time a cap of that kind is pressed while it is the current mode.</para>
    ///
    /// <para>Everything else the enum carries (<c>None</c>, <c>QuestAccept</c>,
    /// <c>CityEncounter</c>, <c>MultiplayerQuest</c>) is not a bar button at all — no cap exists for
    /// it — and is refused for the same reason: this method answers only for modes that have a
    /// window to close.</para>
    ///
    /// <para>ModBuild 226: the six-way table itself moved to
    /// <c>GuildmasterDestinations.IsWindowMode</c>, unchanged member for member. It had to become
    /// one table rather than two, because that class now asks the same question on its own account
    /// — the standing-window sample must know which modes HAVE a window before it can ask that
    /// window anything — and two copies of a six-way switch is the shape a later round gets
    /// half-right. The reasoning above is the reasoning for that table and stays here.</para>
    ///
    /// <para>ModBuild 230: this method's ONLY remaining caller is <see cref="Pressable"/>. The press
    /// path no longer consults it — <c>GuildmasterDestinations.Decide</c> reads the same
    /// <c>IsWindowMode</c> table directly and answers <c>Refused</c> for the two map surfaces — so
    /// there is still exactly one table and the rail and the destination class still cannot
    /// disagree about which modes have a window.</para>
    /// </summary>
    private static bool IsClosableMode(EGuildmasterMode mode) =>
        GuildmasterDestinations.IsWindowMode(mode);

    // ---- THE ALIASING FIX: mip-baked copies of the game's own symbols -------------------------

    /// <summary>Source sprite ids this rail has already offered to the shared bake cache. A FIRST
    /// ask may trigger a full-atlas GPU readback and therefore costs one of the per-frame bake
    /// slots; every later ask is a dictionary hit inside the cache and is free. Mirrors
    /// <c>PanelMipBake.Asked</c> exactly, including its bounded growth — the guildmaster bar has
    /// eight modes and one highlight sprite.</summary>
    private static readonly HashSet<int> AskedSprites = new(16);

    private static int _bakeBudgetFrame = -1;
    private static int _bakeBudget;

    // Instrument counters — see LogIconSampling.
    private float _nextSamplingReport;
    private int _deferredBakes;
    private int _measuredIcons;
    private int _underSampledIcons;
    private int _miplessIcons;
    private int _bakedIcons;
    private float _worstMinification;
    private string _worstIconWhat = string.Empty;

    /// <summary>
    /// Point one world quad at the MIP-BAKED equivalent of the game sprite it mirrors, and remember
    /// which GAME sprite that resolution came from.
    ///
    /// <para>WHY THE GATE MOVED. Before ModBuild 199 the gate was
    /// <c>renderer.sprite != image.sprite</c> — fine while the two were the same object. Now the
    /// renderer wears a DIFFERENT (baked) instance, so that test would be true on every frame
    /// forever. The gate is the remembered SOURCE reference instead, which is the same shape
    /// <c>PanelMipBake</c>'s arrival watch uses and is exactly one reference compare per graphic per
    /// frame in the steady state — no allocation, no cache lookup, nothing.</para>
    ///
    /// <para>THE ANIMATION IS UNTOUCHED, and this is the mechanism by which that is true. The
    /// game's <c>Image.sprite</c> is still read every single frame; when the game swaps a symbol
    /// (<c>UIGuildmasterButton.SetMode</c> → <c>Initialize</c>) the reference changes, this fires,
    /// and the cap follows in that frame. There is NO cached "the icon for this cap" — the cache is
    /// keyed by the game's own sprite instance, so a bake can never go stale: a sprite the game
    /// never assigns again is simply never asked for again, and a sprite the game DOES assign
    /// resolves through the same lookup as the first time. Tint, alpha, the icon's scale animation
    /// and the highlight pulse are read on their own lines and are not touched here at all.</para>
    ///
    /// <para>DEFERRED, NEVER BLANK. When the per-frame first-ask budget is spent the ORIGINAL
    /// sprite is assigned immediately — the cap shows the right symbol on the right frame, merely
    /// aliased — and <paramref name="source"/> is NOT recorded, so the next frame retries it and
    /// upgrades in place. The count of deferrals is on the report line.</para>
    /// </summary>
    private void ResolveSprite(SpriteRenderer target, Sprite? source, ref Sprite? lastSource)
    {
        if (ReferenceEquals(source, lastSource))
            return;
        bool settled = TrySharpen(source, out Sprite? use);
        if (!ReferenceEquals(target.sprite, use))
            target.sprite = use;
        if (settled)
            lastSource = source;   // resolved for good; asked about again only on the next game swap
        else
            _deferredBakes++;      // budget spent this frame — lastSource stays stale, retried next
    }

    /// <summary>
    /// The mip-baked equivalent of <paramref name="source"/>, or the source itself.
    ///
    /// <para>WHY A BAKE AND NOT THE WINDOW FIX. <c>PanelSupersample</c> raises the RESOLUTION a
    /// canvas is rendered into so the eye lands on a real mip level of the RENDER TARGET. There is
    /// no canvas and no render target here: a cap symbol is a world-space <see cref="SpriteRenderer"/>
    /// quad textured directly from the game's uGUI art, which the game imports as "Sprite (2D and
    /// UI)" with mip generation off — every one of the 49 distinct game textures the shared bake
    /// cache has measured on hardware reported <c>mips 1</c>. Against a MIPLESS source no render
    /// target resolution helps: raising it only shrinks the footprint further and drops MORE source
    /// texels. The defect is in the data, so the fix is in the data — a mip chain, trilinear
    /// filtering and anisotropy, which is additionally the term that matters most here because
    /// these caps lie FLAT on the table (<see cref="CapTiltDegrees"/> = 0) and are therefore always
    /// read at a grazing angle, the exact case trilinear alone cannot serve.</para>
    ///
    /// <para>Returns false when the per-frame first-ask budget is spent, in which case the caller
    /// shows the original. The cache's own refusals (an already-mipped source; a rotated or
    /// tight-packed atlas placement whose rectangular copy would drag in its neighbours' pixels —
    /// the v3 card-corruption rule) are NOT deferrals: they are final, they return the game's own
    /// sprite, and the slot settles so nothing is asked twice.</para>
    /// </summary>
    private static bool TrySharpen(Sprite? source, out Sprite? use)
    {
        use = source;
        if (source == null)
            return true;
        if (WorldUIConfig.PanelMipBake == null || !WorldUIConfig.PanelMipBake.Value)
            return true; // OFF is exactly the pre-199 build: the game's own sprite, settled
        if (Cards.CardFaceMipBake.IsBakedSprite(source))
            return true; // already one of ours (a re-entrant read) — nothing to do

        int id = source.GetInstanceID();
        if (!AskedSprites.Contains(id))
        {
            int frame = Time.frameCount;
            if (_bakeBudgetFrame != frame)
            {
                _bakeBudgetFrame = frame;
                _bakeBudget = MaxIconBakesPerFrame;
            }
            if (_bakeBudget <= 0)
                return false; // deferred — caller shows the original this frame and retries next
            _bakeBudget--;
            AskedSprites.Add(id);
        }

        try
        {
            Sprite? baked = Cards.CardFaceMipBake.ReplacementFor(source);
            if (baked != null)
                use = baked;
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"MAP TABLE BUTTON icon '{source.name}' could not be mip-baked "
                              + $"({ex.GetType().Name}: {ex.Message}) — the cap keeps the game's own "
                              + "mipless sprite, i.e. the pre-199 look, never worse. The slot is settled "
                              + "so this is not retried every frame.");
        }
        return true;
    }

    /// <summary>
    /// THE MEASUREMENT — source texels per rendered pixel for every cap symbol on screen, through
    /// the LEFT eye's projection against the real per-eye target. The formula is copied AS A VALUE
    /// from <c>MapIconLayer.MeasureIcon</c> (which copied it from <c>PanelSamplingProbe</c>), so
    /// this number, the map icons' number and the panels' number are the same quantity and can be
    /// compared directly.
    ///
    /// <para>THE TEXEL COUNT IS THE SPRITE'S OWN RECT, not the atlas dimensions — a 96x96 symbol on
    /// a 2048x2048 atlas minifies as 96x96. Both are printed so the two can never be confused.</para>
    ///
    /// <para>A corner behind the near plane is SKIPPED rather than reported (a viewport point behind
    /// the eye is mirrored garbage), and a quad under one pixel is skipped as unmeasurable. Both
    /// show up as a shortfall between the cap count and the measured count on the report line.</para>
    /// </summary>
    private void MeasureIcons(Camera? head)
    {
        _measuredIcons = 0;
        _underSampledIcons = 0;
        _miplessIcons = 0;
        _bakedIcons = 0;
        _worstMinification = 0f;
        _worstIconWhat = string.Empty;
        if (head == null || !EyeTarget(out float eyePxW, out float eyePxH))
            return;

        Camera.MonoOrStereoscopicEye eye = UnityEngine.XR.XRSettings.isDeviceActive
            ? Camera.MonoOrStereoscopicEye.Left
            : Camera.MonoOrStereoscopicEye.Mono;

        for (int i = 0; i < _caps.Count; i++)
        {
            Cap c = _caps[i];
            SpriteRenderer? sr = c.Icon;
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null)
                continue;

            // The quad's own corners. size is the SpriteRenderer's sliced draw size in the
            // renderer's local frame; lossyScale carries any scale on the chain (the rail root is
            // unit-scaled, so this is 1 today and would still be right if it stopped being).
            Transform t = sr.transform;
            Vector3 ls = t.lossyScale;
            Vector3 right = t.right * (sr.size.x * ls.x * 0.5f);
            Vector3 up = t.up * (sr.size.y * ls.y * 0.5f);
            Vector3 p = t.position;
            Vector3 v00 = head.WorldToViewportPoint(p - right - up, eye);
            Vector3 v10 = head.WorldToViewportPoint(p + right - up, eye);
            Vector3 v01 = head.WorldToViewportPoint(p - right + up, eye);
            if (v00.z <= 0f || v10.z <= 0f || v01.z <= 0f)
                continue;
            float pxW = PixelDistance(v00, v10, eyePxW, eyePxH);
            float pxH = PixelDistance(v00, v01, eyePxW, eyePxH);
            if (pxW < 1f || pxH < 1f)
                continue;

            Sprite shown = sr.sprite;
            float texelsW = shown.rect.width;
            float texelsH = shown.rect.height;
            float min = Mathf.Max(texelsW / Mathf.Max(pxW, 0.01f), texelsH / Mathf.Max(pxH, 0.01f));

            _measuredIcons++;
            if (min >= SuspectMinification)
                _underSampledIcons++;
            bool baked = Cards.CardFaceMipBake.IsBakedSprite(shown);
            Texture2D? tex = shown.texture;
            int mips = tex != null ? tex.mipmapCount : 1;
            if (baked)
                _bakedIcons++;
            else if (mips <= 1)
                _miplessIcons++;

            if (min <= _worstMinification)
                continue;
            _worstMinification = min;
            _worstIconWhat =
                $"cap '{(c.Button != null ? c.Button.GuildmasterMode.ToString() : "?")}' sprite "
                + $"'{shown.name}' {texelsW:F0}x{texelsH:F0} texels (on a "
                + $"{(tex != null ? tex.width : 0)}x{(tex != null ? tex.height : 0)} texture) into "
                + $"{pxW:F0}x{pxH:F0} px = {min:F2}x, mips={mips} "
                + $"{(tex != null ? tex.filterMode.ToString() : "?")} aniso "
                + $"{(tex != null ? tex.anisoLevel : 0)}"
                + (baked ? ", MIP-BAKED" : ", NOT BAKED");
        }
    }

    /// <summary>The per-eye render target this frame, in pixels — copied AS A VALUE from
    /// <c>MapIconLayer.EyeTarget</c> so every sampling report in the mod divides by the same
    /// denominator. Falls back to the desktop window when XR is not running, which is what makes
    /// the number readable in a flat-screen dev run.</summary>
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
        return pxW >= 2f && pxH >= 2f;
    }

    private static float PixelDistance(Vector3 a, Vector3 b, float eyePxW, float eyePxH)
    {
        float dx = (b.x - a.x) * eyePxW;
        float dy = (b.y - a.y) * eyePxH;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// THE LINE THAT DECIDES THE BUTTON-ICON QUESTION WITH NUMBERS. It carries, on one line, the
    /// comparison COUNT, the LARGEST value and the THRESHOLD — so "never ran", "ran and found
    /// nothing" and "found something" can never look alike.
    ///
    /// <para>HOW TO READ IT, in the order the numbers settle the question:</para>
    /// <list type="number">
    ///   <item><b>measured 0 of N caps</b> — the instrument RAN but nothing was projectable: the
    ///   rail is off screen, behind the eye, or under a pixel. It is not a silent scan; the cap
    ///   count on the same line says how many existed.</item>
    ///   <item><b>mips=1 and NOT BAKED on the worst cap</b> — the swap did not happen for that
    ///   sprite. Either [WorldUI] PanelMipBake is off, or the shared cache REFUSED it (a rotated or
    ///   tight-packed atlas placement, or the VRAM ceiling), and the MIP BAKE / MIP BAKE SKIP lines
    ///   above name which.</item>
    ///   <item><b>MIP-BAKED with the minification still high</b> — this is the WORKING state, not a
    ///   failure. The number says how far the symbol is minified; the mip chain plus aniso is what
    ///   makes that safe. Only if he still reports shimmer at this state is the cause something
    ///   else, and then the next lever is the symbol's authored size on the cap
    ///   (<c>IconFraction</c>), not another filter.</item>
    ///   <item><b>deferred &gt; 0 persistently</b> — the per-frame first-ask cap, not the seam, is
    ///   the bottleneck; raise <see cref="MaxIconBakesPerFrame"/> only if the Perf SPIKE lines show
    ///   no new hitch.</item>
    /// </list>
    /// </summary>
    private void LogIconSampling()
    {
        VRLog.Info(Scope,
            $"MAP BUTTON ICON SAMPLING: {_measuredIcons} of {_caps.Count} cap symbol(s) measured, "
            + $"{_underSampledIcons} at or above the {SuspectMinification:F2}x threshold; WORST "
            + $"{_worstMinification:F2}x — "
            + (_worstIconWhat.Length > 0 ? _worstIconWhat : "nothing measurable this tick (the rail is "
                                                            + "off screen or behind the eye)")
            + $". OF THE MEASURED: {_bakedIcons} sample a MIP-BAKED trilinear/aniso copy, "
            + $"{_miplessIcons} are still drawn from a MIPLESS game texture; {_deferredBakes} bake(s) "
            + $"deferred so far by the {MaxIconBakesPerFrame}-first-ask/frame cap (a deferred cap shows "
            + "the game's ORIGINAL sprite that frame — aliased but on time, never blank — and is "
            + "upgraded on the next frame). "
            + "THE QUANTITY IS source texels per rendered pixel, max of the two axes, through the LEFT "
            + "eye's projection against the real per-eye target — the SAME quantity MAP ICON SAMPLING "
            + "and PANEL SAMPLING report, so the three are directly comparable. The texel count is the "
            + "SPRITE'S OWN RECT, not the atlas it lives on; both are printed. "
            + "WHY A BAKE AND NOT THE WINDOW SUPERSAMPLE: a cap symbol is a world-space SpriteRenderer "
            + "quad textured straight from the game's uGUI art, not a canvas rendered into a target — "
            + "raising a render resolution against a MIPLESS source only drops more source texels. "
            + "ANISO IS THE TERM THAT MATTERS MOST HERE: these caps lie FLAT on the table "
            + $"({CapTiltDegrees:F0}° tilt), so they are always read at a grazing angle, which is "
            + "exactly the case trilinear alone cannot serve. "
            + "Bake budget now " + Cards.CardFaceMipBake.BudgetSummary + ".");
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

    /// <summary>
    /// Fingertip hover, routed from <see cref="MapButtonPoke"/> so the finger and the beam share
    /// one refcount and one dispatch path.
    ///
    /// <para><b>IT ALSO ARMS AND DISARMS THE FINGER-FOLLOW</b> (R5): the hand recorded here is the
    /// one <c>TickPokeDepth</c> measures the cap's travel against. PokeInteractor keeps ONE hovered
    /// target per hand and pairs enter/exit strictly, so this reference cannot leak past the
    /// fingertip that owns it.</para>
    ///
    /// <para><b>AND IT TICKS THE HAND</b> (R21). The LASER hover on these caps has fired
    /// <c>HapticPreset.HoverTick</c> since the rail was written; the FINGERTIP hover fired nothing,
    /// so hovering <i>Händler</i> with the beam ticked and hovering the same cap with your finger
    /// did not. One cap, one hover vocabulary, whichever pointer arrives — the same rule
    /// <c>PlayTray.BoardButton.OnPokeEnter</c> and <c>PileViewer.PileStack.OnPokeEnter</c> already
    /// follow. Only on a cap the room would actually accept a press on: a tick on a dead cap is a
    /// promise the press then refuses.</para>
    /// </summary>
    internal void SetPokeHover(UIGuildmasterButton button, VRHand hand, bool hovered, string source)
    {
        if (button == null)
            return;
        Cap? c = CapOf(button);
        if (c == null)
            return;
        if (hovered)
        {
            c.PokeHand = hand;
            AddHover(c, source);
            if (Pressable(c))
                hand.SendHaptic(HapticPreset.HoverTick);
        }
        else
        {
            // Only drop the follow this hand actually owns — the OTHER hand may have taken the cap
            // over in the same frame, and an unconditional clear would stall its travel.
            if (ReferenceEquals(c.PokeHand, hand))
            {
                c.PokeHand = null;
                c.Gate.Rearm();
                c.FollowDepth = 0f;
            }
            RemoveHover(c, source);
        }
    }

    // ---- laser -------------------------------------------------------------------------------

    /// <summary>How far the beam may reach a table cap, in real metres before the diorama scale.
    /// A map table is read from across the room, so this is the full beam budget rather than the
    /// control board's arm's-length reach — the same 20 m <c>RayInteractor.MaxDistanceMeters</c>,
    /// <c>RayUguiDriver</c> and <c>RayGrabDriver</c> give the beam, mirrored here because this
    /// scan is geometric and never touches the physics pick. It was a bare inline <c>20f</c> until
    /// 2026-09-05, i.e. the one site of the six that <c>scripts/check-mirrors.sh</c> could not see
    /// at all; naming it is what puts it in the lint.</summary>
    private const float MaxCapLaserMeters = 20f;

    /// <summary>Occlusion epsilon for the solid-occluder veto below — shared value with
    /// <c>RayUguiDriver.OcclusionEpsilonMeters</c> and <c>RayGrabDriver</c>'s, for the same reason:
    /// a cap COPLANAR with (or proud of) the surface that occludes it must not fall into that
    /// surface's own shadow.</summary>
    private const float SolidOccluderEpsilonMeters = 0.005f;

    /// <summary>
    /// Geometric laser test over this rail's own caps — the same shape as <c>RayGrabDriver</c>'s
    /// bar scan, and for the same reason: it needs no layer/mask coupling, so it cannot disturb the
    /// deliberately narrow pick mask the map room keeps.
    ///
    /// <para><b>THE TWO VETOES THIS SCAN OWES ITS SIBLINGS (2026-09-05).</b> Every other beam scan
    /// in the mod rejects a target that sits BEHIND something the player can see; this one did not,
    /// and the map room is the room where that matters most, because <b>the map-room hand fan IS a
    /// <c>Cards.CardFan</c></b> (<c>MapRoomHand.1.Core</c>, since ModBuild 192). So
    /// <c>RayInteractor.ComputeFanOccluder</c> measures those raised cards every frame and
    /// publishes them through <c>Ray.SolidOccluderDistance</c> — and this scan looked straight
    /// past them. Aim at a raised map-room hand card with a table cap behind it and the cap
    /// hovered, the beam was clamped ONTO the cap and drawn THROUGH the card
    /// (<c>UiHitOverride</c> below), and the trigger pressed the cap instead of taking the card.
    /// That is verbatim the defect <c>CardsDriver.UpdateBoardLaser</c> spent four hardware rounds
    /// on and closed at this same seam; <c>CombatLogSurface.TickCapLaser</c> — this method's own
    /// documented copy — carried the veto from the day it was written. A held hand is refused for
    /// the same reason it is there: a hand carrying something is not pointing.</para>
    ///
    /// <para><b>NO MODAL COMMIT GATE, AND THIS IS THE ENTRY THAT SAYS SO.</b> Every other press
    /// path in the mod suppresses its COMMIT while a blocking modal stands
    /// (<c>ModalFallback.HardCommitLockActive</c>; the control board's is
    /// <c>CardsDriver.UpdateBoardLaser</c>'s <c>_modalInputBlocked</c> branch). This rail omits it,
    /// and until 2026-09-05 it omitted it silently, which is the only part that was wrong. The
    /// reason it must keep omitting it: <b>in this room the floated destination window IS what the
    /// cap closes.</b> User ruling, three times over and quoted in <see cref="Press"/> — ModBuild
    /// 200 <i>"soll ein erneuter Druck auf den button zB vom Händler obwohl das Fenster offen ist,
    /// das offene Fenster wieder schließen"</i>, 226 <i>"Ein erneuter Druck auf einen Button z.B.
    /// Händler obwohl das Fenster SCHON DA IST soll das jeweilige Fenster wieder schließen"</i>,
    /// 230 <i>"Die Tasten soll Tiggles sein"</i>. A commit gate keyed on "a modal is open" would
    /// refuse exactly the press the ruling requires — the merchant is up, so the Händler cap goes
    /// dead, and the window can no longer be dismissed from the table. The board's gate exists
    /// because a tray keycap calls a non-undoable game API (END TURN) past the 2D raycast blocker;
    /// a map cap dispatches a <c>pointerClick</c> into the game's own guildmaster Toggle, which is
    /// the very widget a blocking modal is supposed to arbitrate, and <see cref="Pressable"/>
    /// already asks the game whether it would accept the press at all.</para>
    ///
    /// <para><b>WHY <c>Ray.HasFreshUiHit</c> IS NOT ONE OF THEM.</b> This scan is a PRODUCER of
    /// that signal, not a consumer — <c>RayInteractor</c>'s provenance block names "the map rail"
    /// in the list of world lasers whose clamp raises it. <c>HasFreshUiHit</c> is true for the
    /// frame a clamp is set AND the one after, so a scan that both raises it and gates on it would
    /// latch itself off after its own first hover frame and never hover again. The two vetoes above
    /// are the ones that carry real information about somebody ELSE's surface, and they are the
    /// same pair <c>TickCapLaser</c> applies.</para>
    /// </summary>
    private void TickLaser()
    {
        VRHand? hand = VRHands.Primary;
        if (hand == null || !hand.HasPose || !hand.Ray.Active || hand.Grabber.Held != null
            || hand.RayGrab.OwnsPointerFrame)
        {
            ClearLaserHover();
            return;
        }

        hand.GetAimRay(out Vector3 origin, out Vector3 direction);
        var ray = new Ray(origin, direction);
        float best = MaxCapLaserMeters * hand.WorldScale;
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
        // table button behind it (the same precedence RayGrabDriver applies to its bars) — and so
        // does a nearer SOLID mod surface, which in THIS room is the player's own raised hand of
        // ability cards (see the method doc). Both read exactly as CombatLogSurface.TickCapLaser
        // reads them, off the ray's own precomputed terms; neither is inferred here.
        // Window bars are trigger colliders outside this cap-only scan. Test the same hand's
        // geometric ray before granting either hover or click behind a foreground bar.
        float bar = RayGrabDriver.OccludingBarDistance(origin, direction, MaxCapLaserMeters * hand.WorldScale);
        if (!LaserPointerPolicy.TargetBeforeBlocker(best, bar)
            || (hand.RayUgui.HasHit && hand.RayUgui.HitDistance < best)
            || hand.Ray.SolidOccluderDistance < best - SolidOccluderEpsilonMeters * hand.WorldScale)
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
            Press(hit.Button, $"{hand.Side} trigger", hand: hand);
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
    /// <param name="physical">TRUE when a hand or a laser actually pushed this cap. FALSE for a
    /// PROGRAMMATIC dispatch — <see cref="PressMode"/>, i.e. the game-side half of closing some
    /// OTHER destination, and the multiplayer surface-mirror in <c>RemoteMapRoom</c>.
    ///
    /// <para>ModBuild 231 — IT GATES THE TRAVEL ANIMATION AND NOTHING ELSE, and it is the second
    /// half of "Da sollte es gar keine Wechselwirkung mehr geben". Closing the merchant runs
    /// <c>GuildmasterDestinations.ReturnHome</c>, which presses the bar's map Toggle through
    /// <see cref="PressMode"/> — and this method then depressed the WORLD-MAP CAP, 0.007 m, for
    /// 0.07 s, with nobody near it. The ModBuild 230 log shows that dispatch on its own line every
    /// time (Player.log:7872, 9515, 9641, 9759, 10461 …: "MAP TABLE BUTTON 'WorldMap' pressed (X
    /// button on 'UI Shop Item Window')"). A cap is a physical affordance: it must move when it is
    /// pushed and stay still when it is not. The dispatch itself is untouched — the game cannot tell
    /// the difference and MUST NOT, because that press is the only thing that runs the closing mode's
    /// Exit (see <c>GuildmasterDestinations.LeaveMode</c>).</para>
    ///
    /// <para><c>Presses</c> keeps counting both kinds, because it is the double-dispatch detector
    /// and a programmatic dispatch IS a dispatch; the log line names which kind this was.</para></param>
    internal void Press(UIGuildmasterButton button, string source, bool physical = true,
                        VRHand? hand = null)
    {
        if (button == null)
            return;
        Cap? cap = CapOf(button);

        // THE SHARED DEBOUNCE (R5, 2026-09-05) — the control board's PokePressCooldownSeconds,
        // applied to this family for the first time. It covers BOTH physical paths from one clock
        // (the depth-fire has already pre-checked the same window, so an honest push always passes
        // here and re-stamps it), which is what kills the cross-path double a poke and a laser
        // click inside the same window used to fire, and the PokeInteractor hover flicker the
        // travel hysteresis structurally cannot see.
        //
        // A PROGRAMMATIC dispatch is NOT debounced and must not be: it is the game-side half of
        // closing some other destination (PressMode -> GuildmasterDestinations.ReturnHome) and the
        // multiplayer surface mirror. Those are consequences of a press somewhere else that already
        // passed a gate, and refusing one would leave a mode half-exited.
        if (physical && cap != null && !cap.Gate.TryCommit())
        {
            VRLog.Info(Scope, $"MAP TABLE BUTTON '{button.GuildmasterMode}' press DEBOUNCED "
                              + $"({source}) — within the {ButtonTuning.PokePressCooldownSeconds:F2}s "
                              + "press cooldown this family shares with the control board's keycaps. "
                              + "One physical push is one press.");
            return;
        }

        // THE CAP GOES DOWN WHETHER OR NOT THE GAME ACCEPTS THE PRESS. A button that does not move
        // when you push it reads as broken input, not as a refusal — and the refusal is already
        // communicated by the cap being dimmed and inert in the first place. It does NOT go down for
        // a press nobody made: see the `physical` parameter.
        if (cap != null)
        {
            if (physical)
                cap.PressedUntil = Time.unscaledTime + PressHoldSeconds;
            cap.Presses++;
        }

        // ONE PRESS, ONE PULSE (R11). A laser press on a table cap was SILENT to the hand — the
        // rail's only SendHaptic was the hover tick — while the identical gesture on a control-board
        // keycap has pulsed at its commit point since the board was built. It fires HERE, at the
        // single commit both physical paths share and after the debounce, so a refused or debounced
        // attempt never buzzes; and never for a programmatic dispatch, which no hand made.
        if (physical && hand != null)
            hand.SendHaptic(HapticPreset.ClickPulse);
        Toggle? toggle = ToggleOf(button);
        GameObject target = toggle != null ? toggle.gameObject : button.gameObject;

        // (1) DELIVERABLE? ExecuteEvents drops everything aimed at an inactive GameObject, so a
        //     hidden option bar is not a "refusal" the game would print — it is a press that lands
        //     nowhere. THE PRESS NOW REFUSES INSTEAD OF CLEARING THE WAY (user item 6, 2026-09-05).
        //
        //     WHAT STOOD HERE. `TryRevealBar` dropped the player's SELECTED MAP LOCATION through the
        //     game's own AdventureMapUIManager.DeselectCurrentMapLocation and then re-tested. That
        //     was ModBuild 200 making the beam's old accident deliberate, and in single player it
        //     works — the bar comes back on that call. IN MULTIPLAYER IT DOES NOT.
        //     `disableOptionsRequests` is a SET (UIGuildmasterHUD.cs:111) and while a quest is
        //     selected online UIMapMultiplayerController.ToggleReadyUpUI(show: true) has put the
        //     multiplayer controller in it as a second entry (:405), removed only by that same
        //     method's show:false branch (:451) — nothing this mod may touch. So the press dropped
        //     his quest selection AND delivered nothing. His host log has the pair verbatim, twice,
        //     for 'Trainer' and for 'Enchantress': the deselect line, then the INACTIVE warning
        //     below.
        //
        //     "SOLANGE DER POINT OF NO RETURN NICHT ÜBERSCHRITTEN IST SOLL DAS ÖFFNEN EINER QUEST
        //     GAR KEINEN EINFLUSS AUF DIE AKTIVIERUNG DER BUTTONS HABEN." A press that silently
        //     undoes the thing the player just did is the worst reading of that, so the reveal is
        //     gone from this path entirely — including for a PROGRAMMATIC dispatch (PressMode, the
        //     multiplayer surface mirror), which is the other reason this had to be removed here and
        //     not merely guarded in Pressable: a peer's map switch must never touch this player's
        //     selection either.
        //
        //     2026-09-05, SECOND ROUND — AND NOW IT NEITHER REFUSES NOR CLEARS THE WAY. He rejected
        //     the refusal ("Alle Buttons wie Händler und co. sollen drückbar bleiben bis zum point
        //     of no return"), and the reveal is still unsafe for every reason written above. Both of
        //     those answers were about optionsContainer.activeSelf, which is the GAME'S flag with
        //     three of its own writers. The flag is conceded; the press now reaches the NUMBER —
        //     UIGuildmasterHUD.currentMode — through the game's own public UIGuildmasterButton.Select(),
        //     which is a method call and not a pointer event, so an inactive GameObject is no obstacle
        //     to it. See SelectThroughTheGamesOwnApi for the mechanism and its limits.
        //
        //     WHAT STILL REFUSES HERE, and it is the one case the old single term hid: this button's
        //     OWN GameObject being off. GuildmasterMode.RefreshUnlocked (decompiled
        //     GuildmasterMode.cs:53-56) is the only writer of that, and it means the mode is LOCKED.
        //     Selecting a locked mode through any route would be the mod doing what the flat game
        //     refuses, which is the line this family must not cross.
        //
        //     THE ORDER MATTERS AND IT IS WHY THIS IS NO LONGER AN EARLY RETURN. Clause (2) below is
        //     the "second press closes it" contract, and at the point of no return the mod's OWN
        //     close dispatch arrives here (CloseFloatedWindow -> LeaveMode -> ReturnHome ->
        //     PressGuildmasterMode) with the bar already down, because a quest is selected by then.
        //     The ModBuild 447 host log has that exact failure on one line — 84182, "MAP TABLE BUTTON
        //     'WorldMap' pressed (X button on 'UI Shop Item Window') but its game object 'Map Button'
        //     is INACTIVE … nothing was dispatched", four lines before the point-of-no-return sweep
        //     hid the shop window anyway. The window went; the Merchant MODE did not, and only the
        //     mode's Exit takes the party display back out of selection mode (ModBuild 184/195).
        bool barDown = !target.activeInHierarchy;
        if (barDown && !button.gameObject.activeSelf)
        {
            VRLog.Warn(Scope, $"MAP TABLE BUTTON '{button.GuildmasterMode}' pressed ({source}) but its "
                              + $"game object '{target.name}' is INACTIVE, so no pointer event can be "
                              + "delivered to it at all (ExecuteEvents refuses an inactive target) and "
                              + "nothing was dispatched. THE PLAYER'S MAP SELECTION WAS NOT TOUCHED: "
                              + "before 2026-09-05 this path dropped a selected quest through "
                              + "DeselectCurrentMapLocation first, which brings the bar back in single "
                              + "player and does NOT in multiplayer (UIMapMultiplayerController holds a "
                              + "second disableOptionsRequests entry, :405, that the mod may not remove) "
                              + "— so the press ate the selection and still landed nowhere. The bar is "
                              + "down for a reason this room cannot lift: "
                              + "UIGuildmasterHUD.RefreshVisibilityHeadquartersOptions runs "
                              + "optionsContainer.SetActive(false) while disableOptionsRequests is "
                              + "non-empty. SINCE 2026-09-05 THIS LINE MEANS SOMETHING NARROWER THAN IT "
                              + "USED TO: a bar that is merely DOWN no longer reaches it — that press is "
                              + "delivered through the game's own UIGuildmasterButton.Select() instead "
                              + "(look for MAP TABLE BUTTON DELIVERED OFF-BAR). This line now means this "
                              + "BUTTON'S OWN GameObject is inactive, which GuildmasterMode.RefreshUnlocked "
                              + "(:53-56) writes and only for a LOCKED mode, so the refusal is the flat "
                              + "game's own and the cap should already have been dim: Deliverable() is "
                              + "false for a locked mode, so if the source names a hand or the laser the "
                              + "cap was a frame behind the game.");
            return;
        }

        // (2) SECOND PRESS ON THE MODE THAT IS OPEN = CLOSE IT (ModBuild 200 — "Weiterhin soll ein
        //     erneuter Druck auf den button zB vom Händler obwohl das Fenster offen ist, das offene
        //     Fenster wieder schließen."). Tested BEFORE the interactability refusal because the game
        //     turns that toggle off on purpose (RefreshSelected: toggle.interactable = !toggle.isOn)
        //     and a repeat click could not commit anyway (toggleGroup.allowSwitchOff is false while a
        //     mode is active, so uGUI's Toggle.Set merely re-asserts isOn). The close is NOT a click
        //     on this toggle: it is the map button's own press, i.e. the window X's route.
        //
        //     ModBuild 226 — AND "OPEN" IS NOW ASKED OF THE WINDOW, NOT OF THE MODE. He reported
        //     this again, in these words: "Ein erneuter Druck auf einen Button z.B. Händler obwohl
        //     das Fenster SCHON DA IST soll das jeweilige Fenster wieder schließen." The 222 close
        //     path exists and fires — five of its lines are in the reporting player's log
        //     (Player.log:64665, 64917, 65840, 163131, 163275), each followed by the window
        //     actually closing — but it is keyed on `toggle.isOn`, which is the game's CURRENT
        //     MODE. In this room that is not the same question: ModalFallback.MapRoomParallel keeps
        //     a destination floated after the game's single-window toggle has hidden it, and the
        //     same log names that state twice over (Player.log:66110 "game had already hidden it
        //     (single-window toggle); releasing the parallel VR float only", and above it "MAP TABLE
        //     BUTTON 'City' pressed (X button on 'UI Shop Item Window') while it is ALREADY the
        //     current mode … nothing was dispatched"). With the merchant still on the table and the
        //     mode machine back at the map, a press here took the OPEN branch below and re-entered
        //     the mode instead of closing what he was looking at. The window is asked directly —
        //     226's IsLeftoverWindowStanding, which read UIWindow.IsOpen and the mod's own float set
        //     (it is GuildmasterDestinations.Sample/Decide since 230). NOTHING IS REMEMBERED: a flag
        //     saying "this cap opened it" would be wrong the moment he uses the window's own X,
        //     which in this room he can, always.
        //
        //     ModBuild 230 — AND THE TWO STATES WERE TWO PRESSES. He reported it a third time:
        //     "Die Fenster … gehen erst durch zwei-maliges erneutes Drücken auf den Tasten wieder
        //     zu. Die Tasten soll Tiggles sein." The block that stood here asked the MODE first
        //     (toggle.isOn) and only fell through to the panel when the mode had already left — so
        //     press 2 took the mode branch, exited the mode, and left the map room's STICKY parallel
        //     float standing and re-shown, and press 3 took the panel branch and released it. His
        //     ModBuild 229 log has that pair four times over, ten to twenty lines apart with a full
        //     frame of panel maintenance between them (Player.log:27933 then 27943 for the merchant;
        //     the reading is written out in full in GuildmasterDestinations, section 8).
        //
        //     There is ONE question now and the panel is asked first, because the panel is what he
        //     is pressing the button to get rid of: GuildmasterDestinations.Decide returns Open,
        //     Close, TabSwitch or Refused from one sample of the observable state, and HandleCapPress
        //     performs it and prints the single GUILDMASTER WINDOW PRESS line that names the
        //     destination, the state observed, the branch taken and the state left behind. The rail
        //     keeps exactly one job here: dispatch the game's own press when the answer is "open".
        //     toggle.isOn is passed in for the LOG only — it is the fact ModBuild 222 keyed the close
        //     on, and printing it beside the panel state is what makes the next disagreement between
        //     the two visible in one line instead of costing another test round.
        EGuildmasterMode mode = button.GuildmasterMode;
        if (GuildmasterDestinations.HandleCapPress(mode, source, toggle != null && toggle.isOn))
            return;

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
        // (4) DISPATCH. Two routes, and the bar's own state picks between them — see clause (1).
        //     While the bar is down there is no pointer route at all, so the press goes through the
        //     game's own selection API instead of being dropped.
        if (barDown)
        {
            SelectThroughTheGamesOwnApi(button, source, physical);
            return;
        }

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
                          + (physical
                              ? "THIS WAS A PHYSICAL PRESS (a hand or the laser pushed the cap), so "
                                + "the cap travelled. "
                              : "THIS WAS A PROGRAMMATIC DISPATCH — nobody touched this cap. It is "
                                + "the game-side half of closing some other destination (ReturnHome "
                                + "-> PressMode) or the multiplayer surface mirror, and since "
                                + "ModBuild 231 the CAP DOES NOT MOVE for it: a cap that depresses "
                                + "with no hand on it is the 'Wechselwirkung' report. The dispatch "
                                + "itself is unchanged and must be — it is what runs the closing "
                                + "mode's Exit. ")
                          + $"capPresses={(cap != null ? cap.Presses : -1)}, dispatch totals: "
                          + NativeUiPress.Counters + ". THE SOUND: it is played by the game's own handler "
                          + "off the game's own serialized item — see the one-shot 'PHYSICAL BUTTON SOUND "
                          + "STATE' line for this button, which names each item and whether AudioController "
                          + "knows it. Nothing here picks or plays a clip.");
    }

    /// <summary>
    /// DELIVER THE PRESS WHILE THE GAME HAS THE OPTION BAR OFF THE SCREEN — user item 4,
    /// 2026-09-05: <i>"Alle Buttons wie Händler und co. sollen drückbar bleiben bis zum point of no
    /// return. Aktuell grauen alle buttons aus wenn eine Quest ausgewählt ist."</i>
    ///
    /// <para><b>THIS IS "CONCEDE THE FLAG, OWN THE NUMBER" AND NOTHING ELSE.</b> The flag is
    /// <c>optionsContainer.activeSelf</c>. It has three writers inside the game
    /// (<c>RefreshVisibilityHeadquartersOptions</c> :739-751, <c>HandleTempleState</c> :581-593,
    /// <c>HandleMerchantState</c> :595-606) and its input is a private <c>HashSet&lt;Component&gt;</c>
    /// this mod may neither read nor remove from, so every previous answer that tried to change it
    /// — writing <c>SetActive(true)</c>, calling <c>EnableHeadquartersOptions(us, true)</c>, or
    /// dropping the player's map selection to make the game write it back — was a write war, a
    /// measured no-op, or a press that ate what the player had just done. The class doc lists all
    /// three as rejected and they stay rejected. This route does not change container visibility
    /// or native tutorial/travel locks.</para>
    ///
    /// <para><b>THE NUMBER IS <c>UIGuildmasterHUD.currentMode</c>, and the game hands it out.</b>
    /// Setting the native <c>Toggle.isOn</c> emits its complete <c>onValueChanged</c> event:
    /// the button's own callback selects the mode, and separate tutorial listeners advance
    /// the current FTUE step. <c>UIGuildmasterButton.Select()</c> is not equivalent: its
    /// <c>SetValue</c> extension suppresses external listeners. Build 506 therefore opened
    /// WorldMap while leaving BuyItem active. A property assignment works with an inactive
    /// bar, without bypassing the button's native callback or activating its container.
    /// The mode's own <c>Enter()</c> opens the destination window for normal VR conversion.</para>
    ///
    /// <para><b>WHY THE GROUP HAS TO BE UNWOUND BY HAND, and it is the game's own rule.</b> uGUI's
    /// <c>Toggle.Set</c> notifies its <c>ToggleGroup</c> only while <c>m_Group.isActiveAndEnabled
    /// &amp;&amp; IsActive()</c>, and the group lives ON the bar (<c>toggleGroup =
    /// optionsContainer.GetComponentInChildren&lt;ToggleGroup&gt;()</c>, :156), so with the bar down
    /// neither term holds and the previously selected toggle would be left ON beside the new one.
    /// The loop below turns the other owned toggles off with notification, preserving their
    /// complete native event lists in the same order as a real click: old off, then new on.
    /// The target is silently normalized first so even a stale-on toggle emits one true event.
    /// When the bar returns, its toggles reflect the selected mode.</para>
    ///
    /// <para><b>WHAT IS NOT DONE, STATED.</b> No <c>pointerEnter/Down/Up/Click/Exit</c> is sent, so
    /// the game's own press SOUND — which is played by those handlers off the button's serialized
    /// items — does not play on this route. That is a missing click, not a missing action, and it is
    /// the honest cost of a bar that is not on screen to be clicked. The haptic pulse, the cap's
    /// travel and the debounce all already fired in <see cref="Press"/> before this is reached.</para>
    ///
    /// <para><b>MULTIPLAYER.</b> Unchanged in kind: a guildmaster mode switch is local presentation
    /// on the client that made it, exactly as it is through the pointer route, and this method sends
    /// nothing and reads nothing from the wire. A peer's surface mirror
    /// (<c>RemoteMapRoom</c> → <c>PressMode</c>) arrives here as a programmatic dispatch and now
    /// LANDS on that client instead of being dropped when its bar happens to be down.</para>
    /// </summary>
    private void SelectThroughTheGamesOwnApi(UIGuildmasterButton button, string source, bool physical)
    {
        EGuildmasterMode mode = button.GuildmasterMode;
        Toggle? selectedToggle = ToggleOf(button);
        if (selectedToggle == null)
            return;
        int deselected = 0;
        string turnedOff = string.Empty;
        try
        {
            // THE GROUP'S JOB, DONE BY HAND — see the doc. Only the caps this rail owns are walked:
            // they are the eight modes the game's own bar carries, and a mode with no cap has no
            // toggle for this room to have turned on in the first place.
            for (int i = 0; i < _caps.Count; i++)
            {
                Cap other = _caps[i];
                if (other.Button == null || ReferenceEquals(other.Button, button))
                    continue;
                if (other.Toggle == null || !other.Toggle.isOn)
                    continue;
                other.Toggle.isOn = false;
                deselected++;
                turnedOff = turnedOff.Length == 0
                    ? other.Button.GuildmasterMode.ToString()
                    : turnedOff + ", " + other.Button.GuildmasterMode;
            }

            // Deliver the real toggle event, including native tutorial listeners. Build 506
            // used UIGuildmasterButton.Select(), whose SetValue extension temporarily replaces
            // onValueChanged with an EMPTY event and manually calls only the mode callback.
            // WorldMap consequently opened, but BuyItem never completed: its help stayed up
            // and the tutorial retained the travel lock. A hidden ancestor prevents pointer
            // dispatch and ToggleGroup arbitration, not Toggle.isOn's event delivery. The
            // sibling loop above supplies the group arbitration; this one value change runs
            // both the native mode callback and its tutorial listeners exactly once.
            selectedToggle.SetIsOnWithoutNotify(false);
            selectedToggle.isOn = true;
        }
        catch (System.Exception ex)
        {
            // UIGuildmasterButton.RefreshSelected dereferences EventSystem.current and
            // UIInfoTools.Instance (:190-203). Both exist on a live campaign map and neither is this
            // mod's to guarantee, so a teardown mid-press must cost one press and not the room.
            VRLog.Error(Scope, $"MAP TABLE BUTTON '{mode}' off-bar selection FAILED ({source}) — "
                               + $"{ex.GetType().Name}: {ex.Message}. Nothing was written to the game "
                               + "and the guildmaster mode is unchanged; the game's own bar is "
                               + "off-screen, so the player sees no half-finished state.");
            return;
        }

        // HW-VERIFY
        // ITEM 4, 2026-09-05 — THE ANSWER LINE. One line per press taken on this route, never per
        // frame, and it prints only when the bar was actually down (the ordinary pointer route logs
        // its own 'dispatched on' line as before).
        //
        // THE FALSIFIER, and it is the one the brief asked for by name. A CHANGE-TRIGGERED line
        // would be silent both when the fix works and when the feature never ran, so this is not
        // change-triggered: it fires on every off-bar press. The reading that means THE FIX IS
        // INERT is the OTHER line — 'MAP TABLE BUTTON … is INACTIVE, so no pointer event can be
        // delivered' — still appearing for a Merchant/Temple/Trainer/Enchantress/TownRecords cap,
        // because that now means this button's OWN GameObject was off, i.e. Deliverable() let a
        // press through for a LOCKED mode and the discriminator is wrong. The reading that means THE
        // CAPS ARE STILL GREYING is neither line present at all while a quest is selected, together
        // with no cap press in the log for that interval: that is Deliverable() still answering
        // false, i.e. the collider never came back. And 'modeBefore' is the field that says whether
        // the press did anything: modeBefore == this cap's own mode means UpdateCurrentMode
        // early-returned (:437-440) and the press was a no-op, which is a defect in the close path
        // above this, not here.
        VRLog.Note(Scope, $"MAP TABLE BUTTON DELIVERED OFF-BAR: '{mode}' pressed ({source}) while the "
                          + "game has its whole option bar OFF THE SCREEN, and the press LANDED. "
                          + "USER RULING (item 4, 2026-09-05): 'Alle Buttons wie Händler und co. "
                          + "sollen drückbar bleiben bis zum point of no return. Aktuell grauen alle "
                          + "buttons aus wenn eine Quest ausgewählt ist.' HOW: not a pointer event — "
                          + "ExecuteEvents refuses an inactive target — but the game's own public "
                          + "Toggle.isOn=true, including all native onValueChanged listeners "
                          + "(RefreshSelected, OnSelected.Invoke -> "
                          + "UIGuildmasterHUD.UpdateCurrentMode -> the old mode's Exit and this "
                          + $"mode's Enter). {deselected} sibling toggle(s) were turned off by hand "
                          + $"first{(deselected > 0 ? " (" + turnedOff + ")" : string.Empty)}, because "
                          + "uGUI's ToggleGroup notification is skipped while the group's own object "
                          + "is inactive and the bar is where the group lives. NOTHING WAS WRITTEN TO "
                          + "THE GAME'S UI STATE: optionsContainer is not touched, "
                          + "disableOptionsRequests is not read or removed from, and the player's map "
                          + "selection is NOT dropped — the ModBuild 200 reveal that did drop it is "
                          + "gone and is not coming back. THE ONE THING THIS ROUTE CANNOT DO is play "
                          + "the game's own press sound, which its pointer handlers own. "
                          + $"physical={physical} (false is the mod's own dispatch — the game-side "
                          + "half of a close through ReturnHome, or a peer's surface mirror; that is "
                          + "the case the 447 host log dropped on the floor at line 84182).");
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
    /// <para>ModBuild 231: <c>physical: false</c>. Every caller of this method is the mod dispatching
    /// on the player's behalf — the game-side half of a close, or the multiplayer surface mirror —
    /// and none of them is a hand on this cap. The dispatch is identical; only the cap's own travel
    /// animation is suppressed. See <see cref="Press"/>'s <c>physical</c> parameter.</para>
    internal bool PressMode(EGuildmasterMode mode, string source)
    {
        for (int i = 0; i < _caps.Count; i++)
        {
            UIGuildmasterButton button = _caps[i].Button;
            if (button == null || button.GuildmasterMode != mode)
                continue;
            Press(button, source, physical: false);
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
            _rail.SetPokeHover(_button, hand, hovered: true, $"{hand.Side} fingertip");
    }

    public void OnPokeExit(VRHand hand)
    {
        if (_rail != null && _button != null)
            _rail.SetPokeHover(_button, hand, hovered: false, $"{hand.Side} fingertip left");
    }

    /// <summary>
    /// <b>CONTACT NO LONGER PRESSES (R5, 2026-09-05), and this empty body IS the fix.</b> This
    /// method used to call <c>MapButtonRail.Press</c> outright, so a cap committed the moment a
    /// fingertip came within the interactor's 8 mm contact radius: no travel requirement, no
    /// cooldown, and therefore no way for the player to change his mind. Brushing <i>Händler</i>
    /// opened the merchant.
    ///
    /// <para>The press is <c>MapButtonRail.TickPokeDepth</c>'s now — it fires when the cap has
    /// actually been pushed to the bottom of its travel, which is the control board's rule and the
    /// only one of the two that can tell a push from a brush. This is the exact shape
    /// <c>PlayTray.BoardButton.OnPoke</c> takes for the same reason: the enter callback arms the
    /// finger-follow, and the per-frame depth machinery owns the commit.</para>
    /// </summary>
    public void OnPoke(VRHand hand)
    {
    }
}
