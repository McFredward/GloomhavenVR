using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Core.Events;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// World-space tooltips on converted surfaces (ROADMAP P3c #8, config-gated).
///
/// SCENARIO-ONLY (I2, hardware test #5): in <see cref="VRMode.Menu2D"/> the tooltip
/// canvas must stay untouched — it is Screen-Space-Camera on the UICamera, which the
/// FlatScreen redirects into its RenderTexture, so menu tooltips already appear on
/// the quad at native size. Test #5 flipped it to world space in the menu anyway:
/// with no poke anchor the flip never applied a scale, leaving the canvas rect at
/// its pixel dimensions in WORLD METERS (a ~2560 px canvas ≈ 2.5 km), rendered by
/// the head camera — the "giant mouseover" report. The conversion now gates on the
/// VR mode state machine (flips in scenario modes, restores on entering Menu2D) and
/// applies its world scale AT FLIP TIME plus every frame, never only on anchor.
///
/// The trigger side is free: fingertip pokes on converted canvases synthesize REAL
/// <c>pointerEnter/Exit</c> events (Phase-2 UguiPointer), which drive the game's own
/// <c>UITooltipTarget.OnPointerEnter/Exit</c> → the static <c>UITooltip</c> API —
/// no patches needed. What this class owns is PRESENTATION: the tooltip lives on the
/// dedicated persistent <c>tooltipCanvas</c> (Screen-Space-Camera), which is useless
/// in the HMD; while active the canvas is flipped to WorldSpace and parked in the board's
/// TOOLTIP AREA — one fixed reading spot at the CONTROL BOARD's (PlayTray) TOP-LEFT corner
/// (user request 2026-08-04: "ein 'Tooltip'-Bereich ... dessen Position oben links startet"),
/// facing the player and scaling / tilting with the board, adjustable per board via
/// <c>[Cards] HoverHintOffset_&lt;board&gt;</c> (the pre-existing curved-table
/// <see cref="PanelSlot.Tooltip"/> slot is the menu / no-tray fallback). THE AREA IS FOR
/// MOUSEOVER TOOLTIPS ONLY (user ruling 2026-08-04, correcting the ModBuild-46 overreach):
/// only content that appears BECAUSE the pointer hovers something — i.e. this shared
/// <c>UITooltip</c> canvas — parks here. Persistent flow text (the decision dock's prompt
/// HelpBox that <c>Surfaces.DamageTooltipSurface</c> carries, status lines, anything shown
/// by a game flow rather than a hover) stays where its own surface seats it; rerouting the
/// damage-prompt HelpBox through this area tore the decision dock's text away from its
/// buttons and voided the user's tuned <c>[Cards] DecisionGap_*</c> distance. It is NOT
/// anchored to the fingertip (the hover can come from the laser too, and the user wants a
/// stable reading spot, not a spot that jumps around). A short hover grace (user #7b) keeps
/// it from flickering away on micro-jitter off a tiny target: the game's own show/hide fade
/// is widened (<see cref="FadeGraceSeconds"/>) and the parked position is latched for
/// <see cref="HoverGraceSeconds"/> after the content stops showing. The whole presentation
/// is unconditional in every scenario mode: the [WorldUI] Tooltips and ActionElementHints dials
/// (user #7c) were removed by the 2026-08-13 ruling — an explanation the flat game always gives
/// must not be switchable into invisibility in VR.
///
/// FLAT 2D (part A): the game tooltip's content carries baked local-z / local rotation
/// (subtle styling under the perspective UI camera) that becomes literal geometry on a
/// world-space host — the text protruded in 3D past the panel. Routing this SHARED,
/// game-repositioned canvas through <see cref="CanvasConversion.Convert"/> (reparent +
/// content-fit, the DamageTooltip path) would be too invasive for a persistent canvas
/// the game lays out internally, so we apply the same <see cref="CanvasConversion.FlattenSubtree"/>
/// idea in place — zero the baked local-z / rotation on every descendant every frame
/// (tooltip lines are pooled/rebuilt per hover, and the game rewrites them) — and add a
/// <see cref="RectMask2D"/> on the tooltip frame so any 2D overflow is clipped inside the
/// panel. Both are fully reversed on <see cref="Restore"/> (originals restored; a mask we
/// added is destroyed) so the vanilla 2D menu tooltip keeps its styling.
///
/// Scale sanity (I2): world scale = <see cref="WorldUIConfig.CanvasScaleMm"/> (mm per
/// uGUI pixel, default 1) × 0.001 × diorama scale × 0.5 — half the panel framework's
/// meters-per-pixel (CanvasConversion.cs: <c>metersPerPixel = CanvasScaleMm * 0.001f</c>)
/// so a ~400 px tooltip reads ~20 cm at arm's length instead of 40 — times the user's live
/// <see cref="WorldUIConfig.HoverInfoScale"/> dial (normalized against its default, so 1× at
/// the factory value; see the LateTick comment).
///
/// Verified via ilspycmd (GH.Runtime.dll): <c>CanvasManager</c> holds
/// <c>[SerializeField] private Canvas tooltipCanvas;</c> (publicized) and only
/// re-binds <c>worldCamera</c> on scene load — harmless in world mode, we rebind per
/// frame. <c>UITooltip.uiCamera</c> resolves per render mode incl. a world-camera
/// branch (UI-ARCH §6), so its internal placement math keeps working.
///
/// REVERSIBLE: render mode, worldCamera, plane distance, scale and sorting are
/// restored on disable/shutdown AND on every return to Menu2D; a Screen-Space canvas
/// re-drives its own rect once the mode is set back.
/// </summary>
internal sealed class WorldTooltips
{
    /// <summary>Unanchored world-space parking spot (out of every camera's view).</summary>
    private static readonly Vector3 ParkPosition = new(0f, -1000f, 0f);

    // ---- the board TOOLTIP AREA (user request 2026-08-04, replaces the centred #7a spot) --
    // ONE fixed area at the board's TOP-LEFT corner where EVERY board-owned tooltip appears.
    // The shown box is seated with its BOTTOM-LEFT corner a small margin above the board's
    // measured top-left corner, so the area's origin is stable whatever size the box is (a
    // bigger tooltip grows up and to the right, into open air — never down into the board).
    // The anchor is recomputed from the board's LIVE world pose + lossy scale EVERY tick (see
    // TryGetBoardAreaPose), so a tray grab-resize (which rewrites Root.localScale) and any
    // tray move/tilt re-aligns the tooltip immediately.
    //
    // Bug history, in order: (1) an early revision derived the corner from the AUTHORED plate
    // constants; the VISIBLE board (bundled frame + decorations) is LARGER than the plate, so
    // the box sat inside the board — the corner is therefore derived from the tray renderers'
    // combined bounds in board-LOCAL space (PlayTray.MeasureBoardLocalExtents, tilt-tight).
    // (2) The interim "centred above the top edge" spot fixed the intrusion but pinned only
    // the CANVAS pivot, while the game's own placement math kept moving the visible BOX inside
    // the screen-sized canvas per hover — the "Position ist uneinheitlich" report. The area
    // now pins the FRAME (the visible box) itself: the canvas position is back-computed from
    // the frame's offset inside it (see the frame-pinning note in TryResolveTooltipPose), so
    // every tooltip reads at the SAME spot regardless of where the game arranged it.

    /// <summary>Extra clearance above the board's TOP edge, board-local metres × live scale.</summary>
    private const float BoardAnchorMarginY = 0.03f;

    /// <summary>Metres proud toward the viewer (board-local -Z is the viewer side) × live scale.</summary>
    private const float BoardAnchorProudZ = 0.02f;

    // ---- hover grace / stickiness (user #7b) ------------------------------------------
    /// <summary>
    /// Placement latch window: after the game's tooltip content stops showing, keep the
    /// canvas parked at the anchor this long so a micro-jitter off a tiny UITooltipTarget
    /// does not teleport the canvas offscreen-and-back (a jarring flicker of its own) and a
    /// re-hover within the window finds it already anchored.
    /// </summary>
    private const float HoverGraceSeconds = 0.5f;

    /// <summary>
    /// Native fade grace: while converted we widen the game's OWN show/hide fade (its
    /// <c>UITooltip.transition = Fade</c>, <c>transitionDuration</c>) to this so a jitter
    /// exit→enter inside the window is bridged by the game's own tween interruption (the
    /// fade-out is re-tweened back to full) — the tooltip never fully vanishes on tiny
    /// movements. Uses the game's self-cleaning mechanism only — we never fight its alpha /
    /// visual-state machine (it reads its own CanvasGroup alpha back, so forcing it would
    /// strand the shared singleton). Fully restored on <see cref="Restore"/>.
    /// </summary>
    private const float FadeGraceSeconds = 0.4f;

    /// <summary>Local rotation counts as 3D beyond this angle (degrees) off identity.</summary>
    private const float FlattenAngleEpsilon = 0.05f;

    /// <summary>Local z counts as 3D beyond this many uGUI pixels.</summary>
    private const float FlattenZEpsilon = 0.01f;

    /// <summary>Tooltip content is only "shown" (worth placing) above this CanvasGroup alpha.</summary>
    private const float ShownAlphaEpsilon = 0.05f;

    private Canvas? _canvas;
    private bool _converted;

    // Restore data.
    private RenderMode _originalMode;
    private Camera? _originalCamera;
    private float _originalPlaneDistance;
    private Vector3 _originalScale;
    // Sorting is only touched while the hint rides on a floated menu panel; both fields are
    // captured at the flip and written back verbatim in Restore() (reversibility).
    private int _originalSortingOrder;
    private bool _originalOverrideSorting;

    // ---- 2D flatten + frame clip (part A) ---------------------------------------------
    /// <summary>The persistent tooltip content singleton under the canvas (frame + text lines).</summary>
    private UITooltip? _tooltip;

    /// <summary>A RectMask2D WE added to the tooltip frame (null when none / the frame already had one).</summary>
    private RectMask2D? _addedMask;

    // ---- grace + native-fade override state (user #7b) --------------------------------
    /// <summary>Last time the game's tooltip content was genuinely shown (placement latch).</summary>
    private float _lastShownTime = float.NegativeInfinity;

    /// <summary>We widened the game's fade transition; originals for <see cref="Restore"/>.</summary>
    private bool _transitionOverridden;
    private UITooltip.Transition _originalTransition;
    private float _originalTransitionDuration;

    // ---- host ownership (user report 2026-08-03; see ResolveHostOwner) ------------------
    /// <summary>The floated window that owns the CURRENT hint, or null when the CONTROL BOARD does.</summary>
    private ConvertedPanel? _hostWindow;

    /// <summary>
    /// The rect the game anchored the CURRENT hint to (<c>UITooltip.m_AnchorToTarget</c>) — i.e.
    /// THE THING BEING DESCRIBED. Latched next to <see cref="_hostWindow"/> and for the same
    /// reason: the game clears its own anchor at the end of the fade, inside the placement grace
    /// window, and the menu placement needs to keep putting the box beside that row for the last
    /// few frames instead of watching it jump to a fallback spot as it fades out.
    /// </summary>
    private RectTransform? _hoveredRect;

    /// <summary>The host decision last LOGGED (reference-compared, so the change-gated diagnostic
    /// allocates its line only when the answer actually changes — never per frame).</summary>
    private ConvertedPanel? _hostLoggedWindow;

    /// <summary>False until the first host decision was logged (null == board is a real answer).</summary>
    private bool _hostLoggedOnce;

    // ---- diagnostics (dedupe so a per-frame path logs once) ---------------------------
    private bool _parkedLogged;
    private Vector3 _parkedLogPos;

    /// <summary>[Optimize] TooltipScanGate: earliest frame the CanvasManager may be searched for
    /// again while the tooltip canvas is still unresolved.</summary>
    private int _canvasSearchNextFrame;

    /// <summary>Frames between two CanvasManager searches (~1/6 s at 90 Hz) — a hover needs the
    /// canvas within human reaction time, not within one frame.</summary>
    private const int CanvasSearchIntervalFrames = 15;

    /// <summary>Extents captured in the last pose resolve (park diagnostic — prove the panel clears the board).</summary>
    private float _lastBoardTopEdgeWorldY;
    private Vector2 _lastTooltipHalfWorld;

    /// <summary>Descendant transforms flattened this session (original local z + rotation for Restore).</summary>
    private readonly List<FlattenEntry> _flattened = new(32);

    /// <summary>
    /// Graphics whose <c>raycastTarget</c> WE cleared while the canvas is world-space
    /// (all were true when cleared - the scan only records true->false flips), for
    /// verbatim restore. The tooltip is DISPLAY-ONLY: nothing under
    /// <c>CanvasManager.tooltipCanvas</c> carries a click/drag handler (verified in the
    /// decompiled sources - <c>UITooltip</c>/<c>UITooltipLines</c> hold no Button or
    /// IPointerClickHandler, and its line texts are already spawned with
    /// <c>raycastTarget = false</c>, UITooltip.cs:673), so making the remaining
    /// graphics (frame background, anchor graphic, icons) ray-transparent loses
    /// nothing. WHY: in world space the canvas is rendered by the HEAD camera, so the
    /// game's own InControl mouse module - which raycasts EVERY enabled
    /// GraphicRaycaster each frame at the parked mouse pixel - can suddenly "hover" the
    /// floating hint box and report pointer-over-UI while it is up; and no future
    /// surface registration can ever turn the hint into a hover thief for the mod's
    /// pointers either. Mutate-and-restore in the established ActorBars style.
    /// </summary>
    private readonly List<Graphic> _raycastCleared = new(16);

    /// <summary>Reused per-frame scan buffer (no steady-state allocation).</summary>
    private static readonly List<RectTransform> RectScratch = new(64);

    /// <summary>Reused raycast-neutralize scan buffer (no steady-state allocation).</summary>
    private static readonly List<Graphic> GraphicScratch = new(32);

    /// <summary>Reused world-corner buffer for measuring the tooltip's rendered size.</summary>
    private static readonly Vector3[] CornerScratch = new Vector3[4];

    /// <summary>Second world-corner buffer — the menu placement measures the WINDOW rect and the
    /// HOVERED rect in the same expression, so one shared buffer would clobber itself.</summary>
    private static readonly Vector3[] CornerScratchB = new Vector3[4];

    // ---- mixed reality: an opaque plate behind the game's own tooltip frame ------------------
    /// <summary>The MR backing registrant (see <see cref="TooltipBacking"/>); one per presentation.</summary>
    private readonly TooltipBacking _mrBacking;

    /// <summary>True while <see cref="_mrBacking"/> is registered with <see cref="MrBacking"/>.</summary>
    private bool _mrRegistered;

    /// <summary>Raised on every tick that ends with the box genuinely placed AND its own content
    /// still showing — the plate's visibility gate, polled by <c>MrBacking.TickSurfaces</c>. See
    /// <see cref="TooltipBacking.BackingAlpha"/>: this is deliberately NOT the placement grace the
    /// rest of this class runs on.</summary>
    private bool _backingShown;

    /// <summary>Change-gated: the transparency evidence line was already written for this session.</summary>
    private bool _mrEvidenceLogged;

    public WorldTooltips() => _mrBacking = new TooltipBacking(this);

    /// <summary>
    /// MIXED REALITY — AN OPAQUE PLATE BEHIND THE GAME'S OWN TOOLTIP FRAME (user report
    /// 2026-08-09, verbatim: "Der Hintergrund der Tooltipps erscheint grün im mixed-reality Modus.
    /// Dies kann auch damit zusammenhängen das es leicht transparent ist - falls das stimmt sorge
    /// im mixed reality Modus dafür das der Hintergrund der Tooltipps nicht transparent ist (und
    /// auch nicht grün) - der original-Spiel-Rahmen soll aber weiterhin genutzt werden.")
    ///
    /// <para>WHY GREEN IS THE SAME BUG AS TRANSLUCENT. MR here is a CHROMA KEY: the mod renders the
    /// key colour as real green pixels and the compositor swaps them for passthrough. A
    /// semi-transparent panel drawn OVER those pixels blends toward them — <c>dst = key·(1−a) +
    /// art·a</c> — so a translucent dark frame reads as a green tint, and any pixel whose blend
    /// lands back inside the compositor's similarity threshold punches a real passthrough hole in
    /// the middle of the text. The user's own diagnosis, and it is confirmable from the game's
    /// source rather than inferred:</para>
    /// <list type="bullet">
    /// <item><description>The frame is ONE <c>Image</c> on the tooltip root
    /// (<c>UITooltip.Awake</c>: <c>m_image = GetComponent&lt;Image&gt;()</c>, decompiled
    /// UITooltip.cs:375) drawing a 9-SLICED SPRITE — so its per-pixel alpha comes from the atlas
    /// texture, which no code path can force to 1.</description></item>
    /// <item><description>The whole widget hangs under a <c>CanvasGroup</c> the game TWEENS
    /// (<c>EvaluateAndTransitionToState</c> → <c>StartAlphaTween(1f, m_TransitionDuration)</c>,
    /// UITooltip.cs:556/581) — so frame AND text are provably translucent for the entire fade,
    /// and THIS class widened that fade from the authored 0.15 s to
    /// <see cref="FadeGraceSeconds"/> = 0.4 s for the hover-jitter grace (user #7b). Roughly
    /// 0.8 s of every hover is a guaranteed green-tinted frame even if the sprite were
    /// opaque at rest.</description></item>
    /// </list>
    ///
    /// <para>WHY A PLATE AND NOT AN ALPHA FORCE. <c>MrBacking.Opacify</c> was the other candidate
    /// and it is wrong here on three counts, each fatal on its own: (1) it writes
    /// <c>Graphic.color.a</c>, which cannot reach a SPRITE's own texture alpha; (2) it could not
    /// defeat the CanvasGroup tween above, which multiplies everything below it; (3) that method's
    /// contract is "only mod-owned objects are ever registered", and this widget is the game's
    /// shared singleton whose alpha the game reads back to drive its own visual-state machine and
    /// line cleanup (<c>OnTweenFinished</c> → <c>InternalOnHide</c>) — forcing it would strand the
    /// singleton, which is exactly why <see cref="EnsureFadeGrace"/> already refuses to touch it.
    /// A plate BEHIND the frame answers all of it without writing one byte of game state: the
    /// blend source stops being the key colour and becomes MrBacking's dark, deliberately
    /// key-colour-safe neutral, so the composite is opaque and can never read green — while the
    /// game's original frame art, border and 9-slice draw on top of it EXACTLY as before, which is
    /// the "der original-Spiel-Rahmen soll aber weiterhin genutzt werden" half of the request.</para>
    ///
    /// <para>MR-ONLY BY CONSTRUCTION: <c>MrBacking.Tick</c> is a single bool check while
    /// <c>MixedReality.BackingsWanted</c> is false, so with MR off no plate is ever built and the
    /// non-MR look is bit-identical. The anchor is the tooltip's OWN rect (the same GameObject the
    /// background <c>Image</c> sits on, so plate and frame are the same rectangle by construction,
    /// not by a measurement that could drift), which means the plate rides every placement this
    /// class computes for free — board area, floated window, or the game's own re-arrangement
    /// mid-hover.</para>
    ///
    /// <para>SHOWN, NOT MERELY ALIVE, AND NOT MERELY PARKED: the plate is gated on the tick actually
    /// ending in a placed box whose OWN content is still showing — never on the parked canvas, never
    /// on a pose that failed to resolve, and (round 2, the "Streifen" report) never on the placement
    /// grace window that outlives the content BY DESIGN. The "short pop at the start of the fade is
    /// the accepted cost" this paragraph used to claim is gone as well: the plate rides the game's
    /// own tween through <see cref="TooltipBacking.BackingAlpha"/>, so it appears and disappears
    /// with the frame instead of snapping around it.
    /// The plate is deliberately built for the box WHETHER OR NOT the game enabled its background
    /// image (<c>UITooltipTarget.hideBackground</c> → <c>m_image.enabled</c>): a bare caption over
    /// live passthrough is precisely the unreadable case MrBacking exists for, and it is the one
    /// tooltip shape that has NO frame art of its own to survive on.</para>
    /// </summary>
    private sealed class TooltipBacking : MrBacking.IBackedSurface, MrBacking.IFadedBacking
    {
        private readonly WorldTooltips _owner;

        internal TooltipBacking(WorldTooltips owner) => _owner = owner;

        /// <summary>
        /// THE PLATE FADES ON THE GAME'S OWN TWEEN (user report 2026-08-09, MR: "Es verschwindet,
        /// hinterlässt aber einen Streifen im Mixed-Reality-Modus der ca. 1 Sekunde da ist und dann
        /// verschwindet. Der Streifen hat die selbe Hintergrundfarbe wie die anderen Elemente die im
        /// Mixed-Reality-Modus nicht mehr transparent gemacht wurden.").
        ///
        /// <para>THE GAME FADES, IT DOES NOT SNAP — confirmed from its source, not inferred:
        /// <see cref="EnsureFadeGrace"/> puts the widget in <c>UITooltip.Transition.Fade</c>, so
        /// hiding it runs <c>EvaluateAndTransitionToState(false)</c> →
        /// <c>StartAlphaTween(0f, m_TransitionDuration)</c> (decompiled UITooltip.cs:558-563), i.e.
        /// a CanvasGroup ramp 1→0 over the <see cref="FadeGraceSeconds"/> this class widened.
        /// Reporting that ramp here is what makes the plate ONE OBJECT with the frame it backs: it
        /// fades in with the box on the show tween (the plate used to POP in at full opacity the
        /// frame the alpha crossed <see cref="ShownAlphaEpsilon"/>) and out with it on the hide, and
        /// <c>MrBacking</c> switches it off entirely once the ramp reaches its cutoff.</para>
        ///
        /// <para>A <c>CanvasGroup</c> could never have done this for us: it multiplies
        /// <c>CanvasRenderer</c> graphics, and the plate is a world MeshRenderer parented under the
        /// frame — which is precisely why it sat at full opacity while everything around it faded.
        /// Zero when the widget is gone, so a torn-down tooltip reads as absent rather than as
        /// opaque.</para>
        /// </summary>
        public float BackingAlpha => _owner._tooltip != null ? _owner._tooltip.alpha : 0f;

        /// <summary>The world-space presentation is up. On <see cref="Restore"/> this goes false
        /// AND the owner calls <c>MrBacking.Release</c> — the flag alone would only be noticed on
        /// the next MR-on tick, and the plate must not outlive the presentation even with MR off
        /// (it hangs under a GAME rect; see MrBacking.Release).</summary>
        public bool BackingAlive => _owner._converted;

        public Transform? BackingAnchor => _owner.BackingFrame;

        public bool BackingVisible => _owner._backingShown;

        /// <summary>Anchor-local units are the tooltip canvas's uGUI pixels here — exactly the
        /// contract the panel plates use with a host rect.</summary>
        public Vector2 BackingSize
        {
            get
            {
                RectTransform? frame = _owner.BackingFrame;
                return frame != null ? frame.rect.size : Vector2.zero;
            }
        }

        /// <summary><c>Rect.center</c> already carries the frame's pivot (the game re-pivots the
        /// box to whichever corner it anchors from, <c>UITooltip.SetPivot</c>), so this tracks a
        /// pivot flip without a special case.</summary>
        public Vector2 BackingCenter
        {
            get
            {
                RectTransform? frame = _owner.BackingFrame;
                return frame != null ? frame.rect.center : Vector2.zero;
            }
        }

        /// <summary>Read LIVE off the canvas: this class re-slots the tooltip on the converted-panel
        /// distance ladder every frame (menu lift, or <c>OrderAboveDistance</c> when board-owned),
        /// and a plate that kept a stale slot would be painted over by a farther panel — the exact
        /// bleed-through MrBacking's ladder contract exists to prevent.</summary>
        public int BackingOrder => _owner._canvas != null ? _owner._canvas.sortingOrder : 0;
    }

    /// <summary>The tooltip box's own rect — the plate anchor, and the same GameObject the game's
    /// background <c>Image</c> lives on.</summary>
    private RectTransform? BackingFrame =>
        _tooltip != null ? _tooltip.transform as RectTransform : null;

    /// <summary>
    /// One line, once per presentation, stating the MEASURED transparency of the game's own
    /// tooltip frame — the evidence for the report above, so the next hardware log answers "was it
    /// really translucent" with a number instead of a deduction. Written on the first tick the
    /// plate is actually wanted (MR on + a shown box), because that is the first tick on which the
    /// widget is guaranteed to be built and skinned.
    /// </summary>
    private void LogMrEvidence()
    {
        if (_mrEvidenceLogged || _tooltip == null || !MrBacking.WantOpaque)
            return;
        _mrEvidenceLogged = true;
        Image? img = _tooltip.GetComponent<Image>();
        Color key = MixedReality.KeyColor.Value;
        VRLog.Info("WorldUI",
            "MR tooltip backing ON — the game's own frame is measured as: background image " +
            $"{(img == null ? "<none>" : img.enabled ? "enabled" : "DISABLED (hideBackground)")}, " +
            $"colour alpha {(img != null ? img.color.a.ToString("0.###") : "n/a")}, sprite " +
            $"'{(img != null && img.sprite != null ? img.sprite.name : "<none>")}' " +
            $"({(img != null ? img.type.ToString() : "?")} — a sprite's per-pixel alpha comes from " +
            "the atlas, which no colour write can reach), CanvasGroup alpha " +
            $"{_tooltip.alpha:0.###} mid-tween of a {FadeGraceSeconds:0.##}s fade this class " +
            $"widened for the hover grace. Key colour RGB {key.r:0.##},{key.g:0.##},{key.b:0.##} — " +
            "anything translucent over it blends toward the key, which is the reported green. An " +
            "opaque, key-safe MrBacking plate is now seated behind the frame; the game's frame art " +
            "itself is untouched and still draws on top of it.");
    }

    private struct FlattenEntry
    {
        public Transform Transform;
        public float OriginalLocalZ;
        public Quaternion OriginalLocalRotation;
    }

    /// <summary>
    /// THERE IS ONLY ONE TOOLTIP IN THE GAME. <c>CanvasManager.tooltipCanvas</c> is a single shared
    /// canvas, so wherever the player hovers — a figure, a card, a row of the mod's VR Options tab —
    /// the same canvas has to answer, and the right answer depends on WHO OWNS the thing being
    /// hovered (<see cref="ResolveHostOwner"/>, the ownership rule). Three cases, and the previous
    /// attempts each got one of them right:
    ///
    /// <list type="bullet">
    /// <item><description>The hover belongs to the BOARD — a card lying in a slot, a decision button
    /// docked under it, a world/hex tooltip: the canvas is world-space and parked above the control
    /// board, at the board's own angle, plus the user's per-board
    /// <c>[Cards] HoverHintOffset_&lt;board&gt;</c>.</description></item>
    /// <item><description>The hover belongs to a floated WINDOW or MENU — what a scenario does: the
    /// canvas is world-space and laid ONTO that panel, with the visible BOX pinned beside the
    /// hovered row and clamped inside the window (<see cref="ResolveMenuFrameCenter"/>; the pixel/
    /// metre confusions in the game's anchored placement that made this necessary are dissected in
    /// <see cref="TryResolveMenuPose"/>). Earlier
    /// attempts parked it above the board instead, so the hint left the menu and hung in the room
    /// facing the player; another sent it to screen space, and in a scenario the screen is not being
    /// shown at all, so it vanished outright.</description></item>
    /// <item><description>Menu on the FLAT SCREEN (ModalStyle=screen, or a conversion that failed):
    /// screen space is correct — that screen is what the player is looking at, exactly as in the
    /// main menu. Menu2D never converts in the first place, so the main menu was always this
    /// case.</description></item>
    /// </list>
    ///
    /// <para>The board/window split used to be "is ANY window floating" — the regression this class
    /// now avoids, because a tutorial box floating for minutes stole every card hint. See
    /// <see cref="ResolveHostOwner"/> and <see cref="ModalFallback.FindOwningWindow"/>.</para>
    /// </summary>
    private static bool MenuOnFlatScreen()
    {
        if (ModalFallback.MenuPanelHost != null)
            return false; // floated as a world panel — the panel case, not the screen case
        if (!Singleton<ESCMenu>.IsInitialized)
            return false;
        ESCMenu? menu = Singleton<ESCMenu>.Instance;
        return menu != null && menu.IsOpen;
    }

    /// <summary>
    /// The window's plane, scale and draw slot for a hint laid ON a floated menu panel. The
    /// POSITION is not this method's answer any more — see <see cref="ResolveMenuCanvasPose"/>.
    ///
    /// <para>WHAT THIS METHOD USED TO CLAIM, AND WHY IT WAS WRONG (user report 2026-08-09, verbatim:
    /// "Im Pausenmenu wenn ich über die Tabs hovere sollten die Tooltipps alle in der Nähe
    /// erscheinen, manchmal erschienen sie ziemlich weit weg (aber auf der selben Ausrichtung wie
    /// das Menu ausgerichtet ist) aber in x-Koordinate zu weit seitlich links außerhalb des
    /// Fensters."). The old contract was "both canvases hold the SAME screen rect, so give the
    /// tooltip canvas the panel's exact transform and the game's own SCREEN-SPACE placement lands
    /// the box beside the hovered row". Every clause of that is false once a window is converted,
    /// and the decompiled source says so outright:</para>
    ///
    /// <list type="bullet">
    /// <item><description>THE GAME DOES NOT PLACE IN SCREEN SPACE WHEN ANCHORED. <c>UITooltipTarget</c>
    /// always anchors (<c>UITooltip.AnchorToRect(base.transform, corner)</c>,
    /// UITooltipTarget.cs:139) and the anchored branch writes a WORLD position straight out of the
    /// target's world corners: <c>m_AnchorToTarget.GetWorldCorners(array); base.transform.position =
    /// vector2 + array[targetCorner];</c> (UITooltip.cs:433-440). For a converted window that rect
    /// lives in world metres — so <c>vector2</c>, the authored <c>anchoredOffset</c> in PIXELS, is
    /// added to it as METRES. An offset of a few px authored for a 1080p screen becomes a few
    /// METRES of sideways displacement, in the panel's own basis, which is precisely "same
    /// orientation as the menu, x far too far left, outside the window".</description></item>
    /// <item><description>IT IS COMPUTED ONCE, AGAINST WHEREVER THIS CANVAS HAPPENED TO BE. The
    /// write above runs inside <c>Internal_Show</c> (UITooltip.cs:516), i.e. in the pointer-enter
    /// Update — one frame BEFORE this LateUpdate step moves the canvas. Setting a child's world
    /// position fixes its LOCAL offset, so every metre the canvas travels afterwards drags the box
    /// with it. Whatever pose the canvas held on the show frame — parked at
    /// <see cref="ParkPosition"/>, sitting in the board area, or already on this very window —
    /// became the error, which is the whole of the user's "manchmal": a re-hover inside the
    /// placement grace finds the canvas already on the window and lands correctly, a first hover
    /// after the canvas was parked does not.</description></item>
    /// <item><description>TWO MORE PIXEL/METRE CONFUSIONS RIDE ALONG. <c>UpdatePivot</c> compares a
    /// WORLD coordinate against <c>Screen.width / 2</c> (UITooltip.cs:471-474), so in world space
    /// the auto-corner always resolves to the same corner and can never flip a box back inside a
    /// window; and <c>screenBound</c> adds
    /// <c>RectTransform.DeltaWorldPositionToFitTheScreen(worldCamera, margin)</c>, whose
    /// <c>camera.ScreenToWorldPoint(Vector2)</c> carries z = 0 and therefore returns the CAMERA
    /// POSITION — snapping the box's corner onto the head's world x/y, a metre-scale jump applied
    /// twice (immediately and again from <c>DelayedRefreshScreenBound</c> a frame later).</description></item>
    /// </list>
    ///
    /// <para>So the window case now does what the BOARD case has always done and pins the FRAME, not
    /// the canvas pivot (see <see cref="TryResolveTooltipPose"/> and
    /// <see cref="FrameCenterOffsetWorld"/>): we decide where the visible box belongs and
    /// back-compute the canvas position from the frame's live offset inside it, every tick.
    /// Whatever the game's arithmetic did to the box — pixel offsets read as metres, a stale canvas
    /// pose, a screen-bound snap toward the head, a re-arrangement mid-hover — is absorbed the same
    /// frame, because the correction measures the RESULT instead of trusting the input.</para>
    ///
    /// <para>The panel is the one that OWNS the hover (<see cref="ModalFallback.FindOwningWindow"/>),
    /// never merely the topmost float — see that method for the regression this distinction fixes.</para>
    /// </summary>
    private static bool TryResolveMenuPose(ConvertedPanel? panel, out RectTransform? host,
                                           out Quaternion rotation,
                                           out Vector3 scale, out int sortingOrder)
    {
        host = panel != null && panel.IsAlive ? panel.HostRect : null;
        if (panel == null || host == null)
        {
            rotation = Quaternion.identity;
            scale = Vector3.one;
            sortingOrder = 0;
            return false;
        }

        rotation = host.rotation;
        scale = host.lossyScale;
        // GEOMETRY IS NOT ENOUGH (hover-hint bug, second half): both canvases are drawn by the
        // SAME head camera in the transparent queue, where Unity sorts by sortingLayer →
        // sortingOrder → distance, and the game's shared tooltip canvas keeps its authored (much
        // lower) order, so the hint drew BEHIND the menu no matter how proud of the plane it sat.
        // Ride just above the host — restored verbatim in Restore().
        //
        // TRANSPARENCY ROUND: the host's order is no longer a constant. It is rewritten every
        // LateUpdate from the panel's eye distance (CanvasConversion.8.Order.cs), so this must keep
        // reading it LIVE (it does) and the lift must stay BELOW CanvasConversion.PanelOrderStep —
        // otherwise a hint laid on a far panel would climb into the next panel's slot and cover a
        // panel that is genuinely nearer than the menu it belongs to.
        sortingOrder = panel.HostCanvas != null ? panel.HostCanvas.sortingOrder + MenuPanelSortingLift : 0;
        return true;
    }

    /// <summary>
    /// The canvas pose that puts the visible tooltip BOX beside the hovered row and INSIDE the
    /// floated window — the window-side twin of <see cref="TryResolveTooltipPose"/>, and the fix
    /// for the "tooltips land far off to the left of the pause menu" report (the mechanism is
    /// dissected in <see cref="TryResolveMenuPose"/>).
    ///
    /// <para>Measure the frame, decide where the frame should be, subtract the frame's own offset
    /// inside the canvas — that is the whole trick, and it is idempotent by construction: moving
    /// the canvas does not change the frame's LOCAL offset, so the same input produces the same
    /// answer on the next tick instead of drifting.</para>
    /// </summary>
    private Vector3 ResolveMenuCanvasPose(RectTransform host, Quaternion rotation)
    {
        GetTooltipHalfExtents(out float ttHalfW, out float ttHalfH);
        Vector3 frameCenter = ResolveMenuFrameCenter(host, rotation, ttHalfW, ttHalfH);
        return frameCenter - FrameCenterOffsetWorld(rotation);
    }

    /// <summary>
    /// WHERE THE BOX BELONGS ON A FLOATED WINDOW: beside the rect it describes, top edges aligned,
    /// and clamped so it can never leave the window. Two rules, in this order, because the user
    /// asked for both — "die Tooltipps alle in der Nähe erscheinen" (near the thing) and not
    /// "außerhalb des Fensters" (never outside the window), and only the clamp can guarantee the
    /// second one for a box the game may have re-sized to any width.
    ///
    /// <list type="bullet">
    /// <item><description>NEAR: the box sits one gap to the RIGHT of the hovered rect with their
    /// TOP edges aligned, so it reads as belonging to that row and never covers the row itself.
    /// It flips to the LEFT side when the right side would hang out of the window — the flip the
    /// game's own auto-corner can no longer perform here, because
    /// <c>UITooltip.UpdatePivot</c> compares a world metre against <c>Screen.width / 2</c>.
    /// The gap is expressed in the WINDOW'S OWN PIXELS times its live scale, so it reads the same
    /// whether the window was fitted large or small and after a grab-resize.</description></item>
    /// <item><description>INSIDE: the wanted centre is projected onto the window's in-plane basis
    /// and clamped to half the window minus half the box, per axis. Rebuilding the result from the
    /// window centre plus those two in-plane offsets also drops any out-of-plane component the
    /// hovered rect may carry, so the box is always exactly in the window's plane before the proud
    /// offset lifts it toward the viewer. A box LARGER than the window clamps to the window centre
    /// (the max() floors) rather than flipping sign — overflowing symmetrically is the readable
    /// failure.</description></item>
    /// </list>
    ///
    /// <para>Without a hovered rect (a cursor-follow tooltip that still resolved to this window)
    /// the box takes the window's own inside TOP-LEFT corner — the same "starts top-left, grows
    /// into open air" contract the board tooltip area uses, so an anchorless hint has ONE stable
    /// reading spot instead of wherever the game's mouse math last left it.</para>
    /// </summary>
    private Vector3 ResolveMenuFrameCenter(RectTransform host, Quaternion rotation,
                                           float ttHalfW, float ttHalfH)
    {
        Vector3 right = rotation * Vector3.right;
        Vector3 up = rotation * Vector3.up;
        // TOWARD the viewer: the floated panel's +Z points AWAY from the head (PanelPlacement
        // convention — uGUI fronts render along −forward), so subtracting forward moves the
        // tooltip onto the viewer's side of the panel plane.
        Vector3 forward = rotation * Vector3.forward;

        // The window rect in world metres (0 = bottom-left, 1 = top-left, 2 = top-right,
        // 3 = bottom-right — Unity's GetWorldCorners order).
        host.GetWorldCorners(CornerScratch);
        Vector3 winCenter = (CornerScratch[0] + CornerScratch[2]) * 0.5f;
        float winHalfW = Vector3.Distance(CornerScratch[0], CornerScratch[3]) * 0.5f;
        float winHalfH = Vector3.Distance(CornerScratch[0], CornerScratch[1]) * 0.5f;

        // Window pixels → world metres (the host's own scale is the panel's px→m factor).
        float gap = MenuHintGapPixels * Mathf.Max(Mathf.Abs(host.lossyScale.x), 1e-6f);

        Vector3 wanted;
        RectTransform? row = _hoveredRect;
        if (row != null)
        {
            row.GetWorldCorners(CornerScratchB);
            Vector3 rowCenter = (CornerScratchB[0] + CornerScratchB[2]) * 0.5f;
            // Projected onto the WINDOW's basis, not measured as raw distances: a row inside a
            // rotated/scrolled sub-rect must still be sized along the axes we place along.
            float rowHalfW = Mathf.Abs(Vector3.Dot(CornerScratchB[3] - CornerScratchB[0], right)) * 0.5f;
            float rowHalfH = Mathf.Abs(Vector3.Dot(CornerScratchB[1] - CornerScratchB[0], up)) * 0.5f;

            wanted = rowCenter + right * (rowHalfW + gap + ttHalfW) + up * (rowHalfH - ttHalfH);
            if (Vector3.Dot(wanted - winCenter, right) + ttHalfW > winHalfW)
                wanted = rowCenter - right * (rowHalfW + gap + ttHalfW) + up * (rowHalfH - ttHalfH);
        }
        else
        {
            wanted = winCenter - right * (winHalfW - ttHalfW) + up * (winHalfH - ttHalfH);
        }

        Vector3 off = wanted - winCenter;
        float limX = Mathf.Max(0f, winHalfW - ttHalfW);
        float limY = Mathf.Max(0f, winHalfH - ttHalfH);
        float offX = Mathf.Clamp(Vector3.Dot(off, right), -limX, limX);
        float offY = Mathf.Clamp(Vector3.Dot(off, up), -limY, limY);
        return winCenter + right * offX + up * offY - forward * MenuPanelProudZ;
    }

    /// <summary>Gap between the hovered row and the hint box, in the WINDOW's own uGUI pixels
    /// (scaled by the window's live px→m factor at the read site) — near enough to read as one
    /// unit, far enough that the box's frame does not touch the row's.</summary>
    private const float MenuHintGapPixels = 12f;

    /// <summary>Sorting steps the tooltip rides above the floated menu host it is laid on. Must
    /// stay under <c>CanvasConversion.PanelOrderStep</c> (16) — see the note at the read site.</summary>
    private const int MenuPanelSortingLift = 10;

    /// <summary>Metres in front of the floated menu panel the tooltip is laid, so it never z-fights.</summary>
    private const float MenuPanelProudZ = 0.004f;

    /// <summary>
    /// Decide WHO OWNS the hint that is on screen right now — a floated window/menu, or the
    /// CONTROL BOARD — and hold that answer for as long as the box is visible.
    ///
    /// <para>THE RULE (user report 2026-08-03, verbatim): "Sind die overlay-hints von einer Karte
    /// die auf dem Controllboard liegen, sollen sie auch am Controllboard angezeigt werden … Sind
    /// sie Teil eines Windows oder Menus, dann sollen sie auch innerhalb des Window/Menus angezeigt
    /// werden … Auch Hints die explizit mit Buttons (z.B. Decision-Buttons) zu tun haben die am
    /// Controllboard fixiert sind, sollen am Controllboard angezeigt werden." Ownership, not
    /// topmost-ness, and not a name match.</para>
    ///
    /// <para>THE SIGNAL IS STRUCTURAL AND ALREADY IN THE GAME'S DATA. <c>UITooltipTarget</c> anchors
    /// the shared box to the RectTransform the pointer entered
    /// (<c>UITooltip.AnchorToRect(base.transform, corner)</c>, decompiled UITooltipTarget.cs:139),
    /// and keeps it until its fade has fully finished (<c>InternalOnHide</c> nulls
    /// <c>m_AnchorToTarget</c>, UITooltip.cs:611). Converted windows OWN their content by
    /// re-parenting: <c>CanvasConversion.Convert</c> puts the game rect under the mod host
    /// (CanvasConversion.1.Core.cs:221). So the owner is whatever floated host is an ANCESTOR of
    /// that anchor rect — <see cref="ModalFallback.FindOwningWindow"/> — with three consequences
    /// that are exactly the three cases the user listed:</para>
    ///
    /// <list type="bullet">
    /// <item><description>An ability card lying in a board slot is game UI that was never
    /// converted (the VR card is a mesh; <c>Cards.CardFaceRaycaster</c> synthesizes the pointer
    /// events onto the game's own 2D card, which stays on its own canvas) → no floated ancestor →
    /// the BOARD anchor.</description></item>
    /// <item><description>A row of a floated window or menu — the item-unlock window, the pause /
    /// Options family, the mod's own VR options tab inside it — sits under that window's host →
    /// laid flat ON that window.</description></item>
    /// <item><description>A DECISION BUTTON docked under the board is converted too, but by
    /// <c>Surfaces.DecisionDockSurface</c>, which is NOT a floated modal (ModalFallback skips every
    /// window the dock claims, see <c>DecisionDock.ClaimsWindow</c>) → no floated ancestor → the
    /// BOARD anchor, next to the buttons the player is reading.</description></item>
    /// </list>
    ///
    /// <para>A NULL ANCHOR IS A REAL ANSWER, not a failure: the map's hex tooltips deliberately
    /// anchor to null so the box follows the cursor (<c>TileBehaviour.cs:66</c>
    /// <c>UITooltip.AnchorToRect(null, …)</c>). Those belong to the world, not to a window, and
    /// null resolves to the board anchor — the pre-regression behaviour.</para>
    ///
    /// <para>WHY THE ANSWER IS LATCHED THROUGH THE GRACE WINDOW. The placement latch
    /// (<see cref="HoverGraceSeconds"/>) deliberately keeps the box parked for a moment after the
    /// content stops showing, and the game clears <c>m_AnchorToTarget</c> inside exactly that
    /// window (at the end of its fade). Re-deriving there would read null and yank a fading WINDOW
    /// hint back onto the board for its last frames — a visible jump at the worst moment. So the
    /// decision is only re-taken while content is genuinely shown, and it is dropped when the
    /// grace expires.</para>
    /// </summary>
    private void ResolveHostOwner(bool contentShown, bool withinGrace)
    {
        if (!contentShown)
        {
            if (!withinGrace)
            {
                _hostWindow = null; // hint gone for good — next hover decides afresh
                _hoveredRect = null;
            }
            return;
        }

        Transform? hovered = _tooltip != null ? _tooltip.m_AnchorToTarget : null;
        ConvertedPanel? owner = ModalFallback.FindOwningWindow(hovered);
        _hostWindow = owner;
        _hoveredRect = hovered as RectTransform;

        // Change-gated (reference compare, so the steady state allocates nothing): one line per
        // genuine change of host, naming the hovered object — that is the whole evidence chain a
        // hardware log needs to settle "why did this hint go there".
        if (_hostLoggedOnce && ReferenceEquals(_hostLoggedWindow, owner))
            return;
        _hostLoggedOnce = true;
        _hostLoggedWindow = owner;
        string hoveredName = hovered != null ? hovered.name : "<mouse-follow (no anchor rect)>";
        if (owner != null)
        {
            VRLog.Info("WorldUI",
                $"Hover hint OWNER = floated window '{(owner.HostGo != null ? owner.HostGo.name : "?")}' " +
                $"(hovered '{hoveredName}' sits inside its converted host) — the hint is laid FLAT ON " +
                "that window, in its plane and at its scale.");
        }
        else
        {
            VRLog.Info("WorldUI",
                $"Hover hint OWNER = the CONTROL BOARD (hovered '{hoveredName}' is under no floated " +
                "window host — a card in a slot, a board-docked button, or a cursor-anchored world " +
                "tooltip) — the hint parks in the board's TOOLTIP AREA (top-left corner) at the " +
                $"board's own angle, plus [Cards] HoverHintOffset_{{board}} (live).");
        }
    }

    // ---- multiplayer wire text (extras record 9) ---------------------------------------

    /// <summary>
    /// The text of the board-owned tooltip the local player is reading RIGHT NOW, already passed
    /// through the card-identity gate — or null (no tooltip / window-owned / gate suppressed).
    /// Read by <c>Net.NetAvatarDriver</c> every extras tick and put on the wire as extension
    /// record <see cref="Net.NetProtocol.ExtIdBoardTooltip"/>; peers render it at the REMOTE
    /// board's tooltip area. Null the moment the tooltip hides (the grace window keeps only the
    /// local PLACEMENT latched, never the wire — "written only while actually shown").
    /// </summary>
    internal static string? WireText { get; private set; }

    /// <summary>The lines template the current <see cref="_wireComposedText"/> was composed from
    /// (reference-compared: the game builds a NEW template per hover, so a reference change is
    /// exactly "the content changed" — no per-frame string work in the steady state).</summary>
    private UITooltipLines? _wireComposedTemplate;
    private string? _wireComposedText;

    /// <summary>Last WireText a wire-gate diagnostic was logged for (change-gated logging).</summary>
    private string? _wireLoggedText;
    private bool _wireLoggedSuppressed;

    /// <summary>
    /// Publish (or withdraw) the tooltip text for the multiplayer wire. Non-null exactly when a
    /// board-owned tooltip is genuinely SHOWN (not merely within the placement grace) AND its
    /// content is already public to peers (<see cref="ContentPublicToPeers"/> — the identity
    /// gate). Window-owned tooltips never ride the wire: a peer has no copy of that window, and
    /// its content (options rows, unlock dialogs) is not board state.
    /// </summary>
    private void UpdateWireText(bool contentShown, bool onMenuPanel)
    {
        string? text = null;
        bool suppressed = false;
        if (contentShown && !onMenuPanel && _hostWindow == null)
        {
            Transform? hovered = _tooltip != null ? _tooltip.m_AnchorToTarget : null;
            if (ContentPublicToPeers(hovered))
            {
                text = ComposeShownText();
            }
            else
            {
                suppressed = true;
                // Change-gated: one line per suppressed hover, naming the anchor — the evidence a
                // hardware log needs to verify the gate fired where it should (and only there).
                if (!_wireLoggedSuppressed)
                {
                    _wireLoggedSuppressed = true;
                    VRLog.Info("WorldUI",
                        $"Board tooltip NOT sent to peers (identity gate): hovered " +
                        $"'{(hovered != null ? hovered.name : "?")}' resolves to a CARD, and " +
                        "Net.RevealGate.PeersSeeOurCardFronts is false — i.e. the game's own secret " +
                        "SelectAbilityCardsOrLongRest window, the ONE phase in which peers see nothing " +
                        "but our card backs (user ruling 2026-08-08). Suppression is the designed " +
                        "failure direction.");
                }
            }
        }
        if (!suppressed)
            _wireLoggedSuppressed = false;

        WireText = text; // the compose cache hands back the SAME string per hover — no churn

        if (WireText != _wireLoggedText)
        {
            _wireLoggedText = WireText;
            if (WireText != null)
                VRLog.Info("WorldUI",
                    $"Board tooltip published for the wire ({WireText.Length} chars, board-owned, " +
                    "identity gate passed) — peers show it at their copy's tooltip area.");
        }
    }

    /// <summary>
    /// THE CARD-IDENTITY GATE (binding wire rule: no card identity on the wire, ever; reveals
    /// only through <c>Net.RevealGate</c>). A tooltip is TEXT ABOUT the hovered thing, and for a
    /// card that text names the card as surely as its face does — so the tooltip may only ride
    /// the wire when the hovered thing is ALREADY public to peers.
    ///
    /// ─── THE PLACE TEST IS RETIRED (user ruling, 2026-08-08) ───────────────────────────────────
    /// "Die Oberseiten der Karten des remote Spielers soll auch überall sichtbar sein, sei es Karten
    /// in der Hand, der Hand-Karten-Pile oder einer der Piles aus dem Board (Items/Abgeworfen/
    /// Verbrannt). … NUR in der Auswahlphase sieht man überall nur die Rückseiten von remote
    /// spielern, in allen anderen Phasen, ist alles sichtbar."
    ///
    /// <para>This method used to ask WHERE the card was: public only while parked in a round-card
    /// SLOT, "the one place peers draw our cards face-up", and never for the hand fan, item fan, pile
    /// browser or a held card. That was a true statement about the renderers of the day and it is now
    /// false — <c>Net.RemoteHandFan</c>, <c>Net.RemoteBrowserFan</c> and <c>Net.RemoteItemFan</c> all
    /// draw FRONTS behind the same <c>Net.RevealGate</c> the slots use. Keeping the place test would
    /// have left the two halves of one feature contradicting each other: a peer reading our discard
    /// pile card-for-card while our tooltip about that very card was withheld as a secret.</para>
    ///
    /// <para>So the question is no longer a place, it is the PHASE — asked of the one class that owns
    /// it, <c>Net.RevealGate.PeersSeeOurCardFronts</c>, which is <c>ShowRoundCardFronts</c> read from
    /// a peer's seat. Card or not, the tooltip rides the wire exactly when a peer can see the fronts
    /// of our cards at all.</para>
    ///
    /// <list type="bullet">
    /// <item><description>No anchor (the map's cursor-follow hex tooltips) or board furniture
    /// (keycaps, decision buttons, piles, element board): public — every client renders these
    /// from replicated state, in every phase.</description></item>
    /// <item><description>Anything that resolves to a CARD — a <see cref="VRCard"/> ancestor of the
    /// anchor (the card-face raycaster reparents the game's 2D card under the VR card, so the
    /// ancestry is the ownership), or a bare <c>FullAbilityCard</c> ancestor whose VR host cannot be
    /// resolved: public exactly while <c>Net.RevealGate.PeersSeeOurCardFronts</c> is true. The bare
    /// card face no longer needs its own "ambiguous ownership ⇒ suppress" clause, because the answer
    /// no longer depends on WHOSE card it is or where it lies: during the secret window nobody's
    /// card fronts are shown to anybody, and outside it everybody's are.</description></item>
    /// </list>
    ///
    /// <para>The failure direction is unchanged and still suppression: a missing remote tooltip is
    /// cosmetic, a leaked identity breaks the game's hidden-information rule. The one surface a peer
    /// still renders as a BACK outside the secret window is the transient HELD-card slab
    /// (<c>Net.RemoteAvatar</c>'s card in flight), whose identity genuinely is not derivable on a
    /// receiver from anything but a packet — and it is not a secret either: vanilla lets any player
    /// open any other player's full card overview from the initiative track
    /// (<c>InitiativeTrackPlayerAvatar.OnClick → CardsHandManager.ToggleViewAllCards</c>), so outside
    /// the selection window there is nothing about a card identity left to protect.</para>
    /// </summary>
    private static bool ContentPublicToPeers(Transform? hovered)
    {
        if (hovered == null)
            return true; // cursor-anchored world tooltip (hex/map info — on every client's screen)

        bool isCard = hovered.GetComponentInParent<VRCard>() != null
                      || hovered.GetComponentInParent<FullAbilityCard>() != null;
        if (!isCard)
            return true; // board furniture / docked buttons — always public

        // ONE predicate, owned by Net.RevealGate, so "what a peer renders" and "what we may say about
        // it" can never drift apart again.
        return Net.RevealGate.PeersSeeOurCardFronts;
    }

    /// <summary>
    /// Compose the SHOWN tooltip's text from the game's own lines template
    /// (<c>UITooltip.m_LinesTemplate</c> — set while the box is up, cleared by its
    /// <c>CleanupLines</c> on hide): one line per template line, left column plus the right
    /// column where present, joined with newlines. TMP rich-text markup is sent verbatim — the
    /// receiving label renders rich text too, and stripping it here would also strip legitimate
    /// emphasis. Cached on the template REFERENCE (rebuilt per hover), so the steady state of a
    /// held hover allocates nothing.
    /// </summary>
    private string? ComposeShownText()
    {
        UITooltipLines? template = _tooltip != null ? _tooltip.m_LinesTemplate : null;
        if (template == null || template.lineList == null || template.lineList.Count == 0)
            return null;
        if (ReferenceEquals(template, _wireComposedTemplate))
            return _wireComposedText;
        _wireComposedTemplate = template;

        var sb = new System.Text.StringBuilder(128);
        for (int i = 0; i < template.lineList.Count; i++)
        {
            UITooltipLines.Line? line = template.lineList[i];
            if (line == null)
                continue;
            bool hasLeft = !string.IsNullOrEmpty(line.left);
            bool hasRight = !string.IsNullOrEmpty(line.right);
            if (!hasLeft && !hasRight)
                continue;
            if (sb.Length > 0)
                sb.Append('\n');
            if (hasLeft)
                sb.Append(line.left);
            if (hasLeft && hasRight)
                sb.Append("  ");
            if (hasRight)
                sb.Append(line.right);
        }
        _wireComposedText = sb.Length > 0 ? sb.ToString() : null;
        return _wireComposedText;
    }

    public void LateTick()
    {
        // MR PLATE GATE, CLEARED FIRST (user report 2026-08-09, the streak that outlived the
        // tooltip). This flag is the plate's ONLY visibility signal, so it must be lowered before
        // anything in this method can decide not to reach the end of it: every `return` below —
        // the tooltip canvas not resolved yet, no head camera, the interval-gated CanvasManager
        // search, the not-placed branch — used to leave the PREVIOUS frame's `true` standing, and a
        // plate whose gate is stale is an opaque rectangle parked at the last hover's spot with
        // nothing to back. Raised again at the far end of the method, in the same LateUpdate, and
        // MrBacking.Tick reads it in the NEXT frame's Update (WorldUIModule.BuildTickSteps), so no
        // observer can ever see the intermediate false — this cannot flicker, it can only fail
        // closed. One bool store per frame.
        _backingShown = false;

        // Menu2D keeps the vanilla 2D tooltip path (UICamera → FlatScreen RT); every
        // scenario mode (incl. ModalUI/BoardTargeting — Recompute() only leaves
        // Menu2D while a scenario runs) gets the world-space presentation.
        // [WorldUI] Tooltips AND [WorldUI] ActionElementHints are GONE (user ruling 2026-08-13):
        // the flat game raises both on hover and offers no way to switch them off, while the mod's
        // dials did — and their OFF left the tooltip canvas at its 2D screen position, i.e. every
        // condition / element / ability explanation the game gives became unreadable in VR. The
        // world-space presentation is now decided by the MODE alone (user #7c's toggle is gone
        // with them, and the gate-off diagnostic that named it went with the branch).
        bool want = WorldUIConfig.ConversionActive
                    && VRModeStateMachine.CurrentMode != VRMode.Menu2D
                    && !MenuOnFlatScreen();

        if (!want)
        {
            Restore();
            return;
        }

        if (_canvas == null)
        {
            // [Optimize] TooltipScanGate (2026-07 perf pass): this full-scene FindObjectOfType had
            // NO interval gate, so on any scene where the CanvasManager is absent or not yet built
            // it re-scanned the whole scene EVERY FRAME, forever. Retrying a handful of times a
            // second finds it just as fast in human terms (the canvas is needed for a hover, not
            // for a frame) and costs ~1/10 of the scans.
            if (PerfConfig.TooltipGateOn && Time.frameCount < _canvasSearchNextFrame)
                return;
            _canvasSearchNextFrame = Time.frameCount + CanvasSearchIntervalFrames;
            var manager = Object.FindObjectOfType<CanvasManager>();
            _canvas = manager != null ? manager.tooltipCanvas : null;
            if (_canvas == null)
                return;
        }

        // Scale WITH THE BOARD (user #7a): the control board's live lossy scale (diorama ×
        // tray-grab resize) when it exists, else the diorama scale (menu / no-tray fallback).
        float scale = ResolveWorldScale();
        // USER SIZE DIAL ("Infotafel-Größe", settings tab "Tafeln"): one factor for every mouseover info panel.
        // The prop/text hover cards apply it as their ABSOLUTE world factor (its default IS their
        // old hard-coded 0.6), so here it is NORMALIZED against that same default — at the factory
        // value the term is exactly 1 and this canvas keeps its established metres-per-pixel
        // (CanvasScaleMm × 0.001 × board scale × 0.5), i.e. nothing changes until the user tunes
        // the dial, and one stepper then scales BOTH families of hover panels by the same ratio.
        // Read every tick (like CanvasScaleMm above) so a stepper nudge resizes the SHOWN tooltip
        // immediately; the anchor math measures the tooltip's own world corners afterwards, so the
        // "clearly above the board top edge" placement re-derives itself from the NEW size for free.
        float sizeDial = WorldUIConfig.HoverInfoScaleLive() / WorldUIConfig.DefaultHoverInfoScale;
        Vector3 worldScale =
            Vector3.one * (WorldUIConfig.CanvasScaleMm.Value * 0.001f * scale * 0.5f * sizeDial);

        if (!_converted)
        {
            _originalMode = _canvas.renderMode;
            _originalCamera = _canvas.worldCamera;
            _originalPlaneDistance = _canvas.planeDistance;
            _originalScale = _canvas.transform.localScale;
            _originalSortingOrder = _canvas.sortingOrder;
            _originalOverrideSorting = _canvas.overrideSorting;
            _canvas.renderMode = RenderMode.WorldSpace;
            // Scale + park IMMEDIATELY: an unanchored flip must never leave the
            // canvas rect at pixel size in world meters (test-#5 giant tooltip).
            _canvas.transform.localScale = worldScale;
            _canvas.transform.position = ParkPosition;
            _converted = true;
            CanvasConversion.AddMaskRequest();
            VRLog.Info("WorldUI", "Tooltip canvas flipped to world space (scenario mode).");
        }

        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        if (_canvas.worldCamera != head)
            _canvas.worldCamera = head;

        // Resolve the persistent tooltip content (singleton under the canvas) for the
        // flatten pass, frame clip, the visibility gate AND the ownership test below.
        if (_tooltip == null)
            _tooltip = _canvas.GetComponentInChildren<UITooltip>(includeInactive: true);

        // MIXED REALITY (user report 2026-08-09, the green tooltip background): register the box
        // for an opaque plate behind the game's own frame — see TooltipBacking for the whole
        // mechanism and why a plate, not an alpha force. Registration is MR-AGNOSTIC and costs one
        // list entry: no plate exists, and nothing is polled, until MR is actually on.
        if (!_mrRegistered && _tooltip != null)
        {
            _mrRegistered = true;
            MrBacking.Surface(_mrBacking);
        }

        // FIXED PLACEMENT (user #7a): while a tooltip is shown — OR within the placement
        // grace window just after it stopped (user #7b) — park the canvas at the anchor its
        // OWNER dictates. Otherwise leave it out of view — never at the fingertip.
        bool contentShown = _tooltip != null && _tooltip.IsActive() && _tooltip.alpha > ShownAlphaEpsilon;
        if (contentShown)
            _lastShownTime = Time.unscaledTime;
        bool withinGrace = _tooltip != null && Time.unscaledTime - _lastShownTime <= HoverGraceSeconds;
        bool visible = contentShown || withinGrace;

        // WHO OWNS THIS HINT decides where it goes — resolved BEFORE the pose/scale, because the
        // answer picks between two different pixels-to-metres factors (see ResolveHostOwner).
        ResolveHostOwner(contentShown, withinGrace);

        // ON THE MENU PANEL the scale is the PANEL's, not the board's: the two canvases only
        // coincide — and the game's own placement only lands where it means to — if they share a
        // pixels-to-metres factor. Off the menu, the board-derived scale above stands.
        bool onMenuPanel = TryResolveMenuPose(_hostWindow, out RectTransform? menuHost,
                                              out Quaternion menuRot,
                                              out Vector3 menuScale, out int menuSorting);
        if (onMenuPanel)
        {
            worldScale = menuScale;
            // Draw IN FRONT of the panel it is laid on (see TryResolveMenuPose) — the 4 mm of
            // geometric clearance alone loses to the host's sortingOrder.
            if (_canvas.sortingOrder != menuSorting)
                _canvas.sortingOrder = menuSorting;
        }
        // BOARD-OWNED sorting is resolved AFTER the pose below: its ladder slot depends on the
        // tooltip's eye distance, which needs the resolved area position first.

        // Re-assert the scale every frame (config/diorama scale are live; the game
        // may rewrite the transform) — independent of placement.
        if (_canvas.transform.localScale != worldScale)
            _canvas.transform.localScale = worldScale;

        // FLATTEN + CLIP (part A): kill the baked local-z / rotation that renders as 3D
        // depth on a world-space host, and clip 2D overflow inside the frame. Both are
        // undone on Restore().
        //
        // [Optimize] TooltipScanGate (2026-07 perf pass): FlattenSubtree walks the WHOLE tooltip
        // canvas subtree (GetComponentsInChildren, includeInactive) and used to run on every frame
        // of every scenario — including the ~99 % of frames where no tooltip is shown at all and
        // the canvas is parked out of view. The visibility test that already exists two lines down
        // now gates it, so the walk happens exactly while the flattening can matter. Nothing the
        // player sees changes: the canvas is at ParkPosition whenever this gate is false, and the
        // flatten is re-asserted every frame it IS true.
        if (!PerfConfig.TooltipGateOn || visible)
        {
            FlattenSubtree();
            // RAY-TRANSPARENT WHILE WORLD-SPACE (see _raycastCleared): re-run on every
            // visible frame because the game pools/rebuilds the line objects per hover -
            // a fresh icon graphic must lose its raycastTarget the frame it appears.
            NeutralizeRaycasts();
            // MIP BAKE (aliasing round 4, "Raender der Mouse-Overlay-Hints"): the frame /
            // line sprites sample the game's mipless UI atlases, which shimmer on this
            // world-space canvas exactly like the card faces did. Re-run on the same
            // visible frames because the lines are pooled per hover — a fresh icon must
            // swap the frame it appears; the scan is change-gated dictionary hits once
            // warm and uses reused scratch lists (no per-frame allocation). Restored with
            // everything else in Restore().
            PanelMipBake.Rescan(_canvas, "Tooltip");
        }
        EnsureFrameClip();
        // HOVER GRACE (user #7b): widen the game's own show/hide fade so a jitter off a
        // tiny target is bridged by its native tween. Undone on Restore().
        EnsureFadeGrace();

        // The window wins the placement only when it OWNS the hover (ResolveHostOwner); otherwise
        // the board anchor answers, which is what a card lying in a slot and a docked decision
        // button need even while some unrelated window floats.
        bool placed = onMenuPanel;
        Vector3 pos = Vector3.zero;
        Quaternion rot = menuRot;
        if (placed)
        {
            // FRAME-PINNED, exactly like the board area (see ResolveMenuCanvasPose): resolved AFTER
            // the world scale was re-asserted above, because it measures the frame's live world
            // corners and those carry that scale.
            pos = ResolveMenuCanvasPose(menuHost!, menuRot);
        }
        else
        {
            placed = TryResolveTooltipPose(out pos, out rot);
        }

        // MULTIPLAYER (extras record 9): publish the text of a SHOWN, BOARD-owned tooltip for
        // NetAvatarDriver — gated on content that is already public to peers (the card-identity
        // rule; see UpdateWireText). Runs whether or not a pose resolved: the wire question is
        // "what is the owner reading", not "where is it parked".
        UpdateWireText(contentShown, onMenuPanel);

        if (!(contentShown || withinGrace) || !placed)
        {
            // Not showing: hand the authored order straight back instead of leaving a lift
            // latched on the game's shared canvas (same reversibility contract as Restore).
            if (!onMenuPanel && _canvas.sortingOrder != _originalSortingOrder)
                _canvas.sortingOrder = _originalSortingOrder;
            if (_canvas.transform.position != ParkPosition)
            {
                _canvas.transform.position = ParkPosition;
                _parkedLogged = false; // re-arm the park diagnostic for the next appearance
            }
            return;
        }

        if (!onMenuPanel)
        {
            // BOARD-OWNED ("sometimes covered" fix): the game's authored order (~0) sits BELOW
            // the whole converted-panel ladder (base 100, CanvasConversion.8.Order.cs), so any
            // panel painted over the tooltip even when the tooltip was clearly in front of it.
            // Slot the canvas into the ladder architecture at the tooltip's own eye distance —
            // above every panel behind/at the board (the tie rule covers the board-docked
            // surfaces the area floats 2 cm proud of), below every panel genuinely nearer, with
            // the same sub-step lift the menu-laid case rides (MenuPanelSortingLift < the
            // ladder's PanelOrderStep, so it can never climb into the next panel's slot).
            int boardOrder = CanvasConversion.OrderAboveDistance(
                Vector3.Distance(head.transform.position, pos), MenuPanelSortingLift);
            if (_canvas.sortingOrder != boardOrder)
                _canvas.sortingOrder = boardOrder;
        }

        _canvas.transform.SetPositionAndRotation(pos, rot);

        // THE PLATE FOLLOWS THE BOX, NOT THE PLACEMENT LATCH — the whole "Streifen" fix (user report
        // 2026-08-09). This used to be an unconditional `true`, reached whenever the canvas was
        // placed, and "placed" is true for the ENTIRE hide sequence: the ~0.4 s FadeGraceSeconds
        // ramp the tooltip's alpha runs down PLUS the 0.5 s HoverGraceSeconds placement latch that
        // deliberately keeps the canvas parked at the anchor afterwards so a hover jitter does not
        // teleport it. That is the reported ~1 second, exactly, and the shape is the giveaway: the
        // game's own OnTweenFinished → InternalOnHide (decompiled UITooltip.cs:595-618) destroys the
        // text lines and resets the rect to m_DefaultWidth, so a plate still sized from
        // BackingSize == frame.rect.size collapses to a full-width, near-zero-height bar — the
        // STRIPE the user saw, in the plate's own neutral, and MR-only because with MR off no plate
        // is ever built at all. `contentShown` is the box's own alpha, so the latch may keep
        // parking the canvas for the next hover while the backing goes when the content goes.
        _backingShown = contentShown;
        LogMrEvidence();

        // Diagnostic (user #7a): one line when the hint parks at the resolved board anchor
        // (deduped; re-logs if the anchor drifts > ~2 cm, e.g. the board was grabbed/moved).
        if (!_parkedLogged || (pos - _parkedLogPos).sqrMagnitude > 0.0004f)
        {
            _parkedLogged = true;
            _parkedLogPos = pos;
            if (onMenuPanel)
            {
                // Names the OWNING window (_hostWindow), not the topmost float — naming
                // MenuPanel here used to print a window the hint was never laid on.
                GetTooltipHalfExtents(out float mHalfW, out float mHalfH);
                VRLog.Info("WorldUI",
                    $"Tooltip laid FLAT ON its OWNING floated window "
                    + $"'{(_hostWindow?.HostGo != null ? _hostWindow!.HostGo.name : "?")}' "
                    + $"— canvas anchor {pos:F3} (panel scale {menuScale.x:F5} m/px, sortingOrder "
                    + $"{menuSorting} — in front of the host), box half {mHalfW:F3}×{mHalfH:F3} m "
                    + $"beside '{(_hoveredRect != null ? _hoveredRect.name : "<no anchor rect>")}'. "
                    + "FRAME-PINNED: the canvas anchor is back-computed from the box's live offset "
                    + "inside it, so the game's anchored placement — which writes a WORLD position "
                    + "out of the hovered rect's corners plus a PIXEL offset, once, against "
                    + "whatever pose this canvas held on the show frame — can no longer push the "
                    + "box metres out of the window (user report 2026-08-09).");
                return;
            }

            bool onBoard = PlayTray.Current != null && PlayTray.Current.IsVisible;
            // Prove the box sits in the AREA: the frame's bottom-left is seated (margin) above
            // the board's measured top-LEFT corner. Logs the corner's world Y, the tooltip
            // half-extents and the final canvas anchor for the hardware log.
            VRLog.Info("WorldUI",
                $"Tooltip parked {(onBoard ? "in the board TOOLTIP AREA (top-left corner)" : "at fallback slot")} " +
                $"canvas anchor {pos:F3} (board scale {scale:F3}, area-origin world Y {_lastBoardTopEdgeWorldY:F3} m, " +
                $"tooltip half {_lastTooltipHalfWorld.x:F3}×{_lastTooltipHalfWorld.y:F3} m, " +
                $"sortingOrder {_canvas.sortingOrder} (distance-ladder slot)" +
                // The user dial only takes part in the BOARD anchor; the no-board fallback slot is
                // a fixed table pose, so naming an offset there would be a lie in the log.
                (onBoard
                    ? $", user offset [Cards] HoverHintOffset_" +
                      $"{(CardsConfig.Board != null ? CardsConfig.CurrentBoard.ToString() : "?")} " +
                      $"{BoardHintOffset()} board-local m"
                    : "") + ").");
        }
    }

    /// <summary>
    /// The TOOLTIP AREA pose for a panel of the given world half-extents: the pose whose CENTER
    /// seats that panel's bottom-left corner a small margin above the CONTROL BOARD's measured
    /// top-LEFT corner, in the board plane, proud toward the viewer, plus the user's live
    /// per-board <c>[Cards] HoverHintOffset_&lt;board&gt;</c> dial. Recomputed from the board's
    /// LIVE pose + lossy scale on every call, so it re-aligns the instant the board is moved,
    /// resized or tilted, and a debug-menu nudge moves a tooltip that is already open.
    ///
    /// STATIC ON PURPOSE: this is the ONE definition of "the tooltip area" (user request
    /// 2026-08-04), and it serves MOUSEOVER TOOLTIPS ONLY (the follow-up ruling that same
    /// day — see the class doc): the hover hint resolves through it here, and the remote
    /// peer's copy (<c>Net.RemoteBoardLayout.TooltipMount</c>) mirrors the same corner.
    /// Persistent flow text — notably the decision-prompt HelpBox of
    /// <c>Surfaces.DamageTooltipSurface</c>, which ModBuild 46 briefly and wrongly routed
    /// through this call — must NOT seat itself here. The corner comes from
    /// <see cref="PlayTray.MeasureBoardLocalExtents"/> (renderer bounds in board-LOCAL space):
    /// "top left" is COMPUTED from the visible board, never guessed from the authored plate,
    /// which the bundled frame overhangs. <paramref name="areaOrigin"/> reports the measured
    /// corner for diagnostics. False when no visible board exists (menu / Cards module off).
    /// </summary>
    internal static bool TryGetBoardAreaPose(float panelHalfWidth, float panelHalfHeight,
                                             out Vector3 center, out Quaternion rotation,
                                             out Vector3 areaOrigin)
    {
        center = Vector3.zero;
        rotation = Quaternion.identity;
        areaOrigin = Vector3.zero;
        if (!TryGetBoardRoot(out Transform root))
            return false;

        // Board basis (unit vectors; Transform.* ignore scale) and live lossy scale.
        Quaternion boardRot = root.rotation;
        Vector3 up = boardRot * Vector3.up;           // board +Y  (top / far edge)
        Vector3 right = boardRot * Vector3.right;     // board +X  (the board's right)
        Vector3 forward = boardRot * Vector3.forward; // board +Z  (away from viewer)

        Vector3 lossy = root.lossyScale;
        float scaleX = Mathf.Max(Mathf.Abs(lossy.x), 0.0001f);
        float scaleY = Mathf.Max(Mathf.Abs(lossy.y), 0.0001f);
        float scaleZ = Mathf.Max(Mathf.Abs(lossy.z), 0.0001f);

        // The REAL top-left corner of the visible board, from the tray's combined renderer
        // bounds (degrades to the authored constants while the board has no renderers yet).
        PlayTray.MeasureBoardLocalExtents(root, out float topLocalY, out float halfLocalX);
        areaOrigin = root.TransformPoint(new Vector3(-halfLocalX, topLocalY, 0f));

        // Clearance above the edge + proud toward the viewer, both proportional to board scale.
        float margin = BoardAnchorMarginY * scaleY;
        float proud = BoardAnchorProudZ * scaleZ;

        // The USER'S OWN per-board offset (read LIVE — that is what makes the debug-menu dial
        // move an open tooltip). Same convention as every other per-board offset: X/Y in the
        // board plane, Z proud toward the player (negative = prouder), board-local metres scaled
        // by the live lossy scale so a grab-resize does not change how far it sits.
        Vector3 tune = BoardHintOffset();
        Vector3 tuneWorld = boardRot * new Vector3(
            tune.x * scaleX, tune.y * scaleY, tune.z * scaleZ);

        // Bottom-left of the panel at (corner + margin up): center = corner + right·halfW +
        // up·(margin + halfH). A panel of ANY size therefore STARTS at the same top-left spot
        // and grows up/right into open air — the "starts top-left" contract.
        center = areaOrigin + right * panelHalfWidth + up * (margin + panelHalfHeight)
                 - forward * proud + tuneWorld;
        rotation = boardRot; // docked-panel facing (faces the player)
        return true;
    }

    /// <summary>
    /// Resolve the world pose the tooltip CANVAS parks at so that the visible tooltip FRAME sits
    /// in the board's tooltip area (<see cref="TryGetBoardAreaPose"/>).
    ///
    /// FRAME-PINNING (the "Position ist uneinheitlich" fix): the game's own placement math keeps
    /// positioning the box INSIDE its screen-sized canvas — beside the hovered row, under the
    /// cursor, wherever its 2D rules land it — so pinning only the canvas pivot left the visible
    /// box at a different spot per hover. The canvas position is therefore back-computed from
    /// the frame's current offset within the canvas (<see cref="FrameCenterOffsetWorld"/>):
    /// wherever the game put the box this frame, the canvas is moved so the BOX lands exactly at
    /// the area. Re-run every tick, so the game re-arranging the box mid-hover is corrected the
    /// same frame. Falls back to the pre-existing <see cref="PanelSlot.Tooltip"/> table slot
    /// when no board is present (menu / Cards module off).
    /// </summary>
    private bool TryResolveTooltipPose(out Vector3 position, out Quaternion rotation)
    {
        // The tooltip's OWN rendered half-extents (world). Scales with the canvas world scale
        // we set this tick, so it tracks a board resize for free.
        GetTooltipHalfExtents(out float ttHalfW, out float ttHalfH);
        if (TryGetBoardAreaPose(ttHalfW, ttHalfH, out Vector3 frameCenter, out rotation,
                                out Vector3 areaOrigin))
        {
            position = frameCenter - FrameCenterOffsetWorld(rotation);
            _lastBoardTopEdgeWorldY = areaOrigin.y;
            _lastTooltipHalfWorld = new Vector2(ttHalfW, ttHalfH);
            return true;
        }

        // Safe fallback: the original curved-table slot (top-right, above initiative).
        _lastBoardTopEdgeWorldY = 0f;
        _lastTooltipHalfWorld = Vector2.zero;
        return PanelLayout.TryGetPose(PanelSlot.Tooltip, out position, out rotation);
    }

    /// <summary>
    /// The tooltip FRAME's center offset from the canvas pivot, expressed in WORLD metres as it
    /// will be once the canvas carries <paramref name="targetRotation"/> (the offset is intrinsic
    /// — measured in canvas-local space, then re-expressed under the target basis with the
    /// canvas's current, already re-asserted scale). Subtracting it from the desired frame pose
    /// yields the canvas pose that puts the frame there — the frame-pinning trick. Zero when the
    /// frame does not exist yet (the canvas pivot then parks at the area unadjusted).
    /// </summary>
    private Vector3 FrameCenterOffsetWorld(Quaternion targetRotation)
    {
        if (_canvas == null || _tooltip == null || _tooltip.transform is not RectTransform frame)
            return Vector3.zero;
        frame.GetWorldCorners(CornerScratch); // 0 = bottom-left, 2 = top-right
        Vector3 frameCenterWorld = (CornerScratch[0] + CornerScratch[2]) * 0.5f;
        Transform canvasTf = _canvas.transform;
        Vector3 local = canvasTf.InverseTransformPoint(frameCenterWorld);
        return targetRotation * Vector3.Scale(local, canvasTf.localScale);
    }

    /// <summary>
    /// The user's per-board hover-hint offset in BOARD-LOCAL metres
    /// (<c>[Cards] HoverHintOffset_&lt;board&gt;</c>), or zero while the Cards config is not bound
    /// (the board anchor is only reachable with a live tray, but this must never throw on a
    /// half-initialised session — an NRE in a LateUpdate step costs the whole step, see the
    /// WorldUI TickGuard). Read on every resolve, never cached: that is what makes the debug-menu
    /// dial move an OPEN hint.
    /// </summary>
    private static Vector3 BoardHintOffset()
    {
        if (CardsConfig.Board == null)
            return Vector3.zero; // Cards config never bound (module off) — no per-board dial exists
        BepInEx.Configuration.ConfigEntry<Vector3>? entry =
            CardsConfig.HoverHintOffset(CardsConfig.CurrentBoard);
        return entry != null ? entry.Value : Vector3.zero;
    }

    /// <summary>
    /// Measure the tooltip frame's rendered size in WORLD metres (half-width / half-height).
    /// Uses the frame's world corners, which carry the canvas world scale we set this tick, so
    /// the measurement tracks a board resize. Returns zeros when there is no frame yet (the
    /// anchor then lifts only by the margin above the top edge — still above the board).
    /// </summary>
    private void GetTooltipHalfExtents(out float halfWidth, out float halfHeight)
    {
        halfWidth = 0f;
        halfHeight = 0f;
        if (_tooltip == null || _tooltip.transform is not RectTransform frame)
            return;
        frame.GetWorldCorners(CornerScratch); // 0 = bottom-left, 1 = top-left, 3 = bottom-right
        halfWidth = Vector3.Distance(CornerScratch[0], CornerScratch[3]) * 0.5f;
        halfHeight = Vector3.Distance(CornerScratch[0], CornerScratch[1]) * 0.5f;
    }

    /// <summary>The live, visible control board root, or false (menu / no tray).</summary>
    private static bool TryGetBoardRoot(out Transform root)
    {
        root = null!;
        PlayTray? tray = PlayTray.Current;
        if (tray == null || !tray.IsVisible)
            return false;
        Transform? r = tray.Root;
        if (r == null)
            return false;
        root = r;
        return true;
    }

    /// <summary>World scale for the tooltip canvas: the board's live lossy scale when the
    /// board exists (so the hint scales with it — user #7a), else the diorama scale.</summary>
    private static float ResolveWorldScale() =>
        TryGetBoardRoot(out Transform root) ? Mathf.Max(root.lossyScale.x, 0.01f) : PanelLayout.WorldScale;

    /// <summary>
    /// Widen the game's OWN tooltip fade so brief hover jitter is bridged (user #7b). We
    /// switch it to <see cref="UITooltip.Transition.Fade"/> at <see cref="FadeGraceSeconds"/>
    /// and store the originals — the game only ever sets these from the prefab at load, so a
    /// one-shot override holds. Fully reverted in <see cref="Restore"/>. We deliberately do
    /// NOT touch the tooltip's alpha / CanvasGroup: the game reads its own alpha back to
    /// drive its visual-state machine and clean up its content, so forcing it would strand
    /// the shared singleton — letting its native fade run long is safe and self-cleaning.
    /// </summary>
    private void EnsureFadeGrace()
    {
        if (_tooltip == null || _transitionOverridden)
            return;
        _originalTransition = _tooltip.transition;
        _originalTransitionDuration = _tooltip.transitionDuration;
        _tooltip.transition = UITooltip.Transition.Fade;
        _tooltip.transitionDuration = FadeGraceSeconds;
        _transitionOverridden = true;
    }

    /// <summary>
    /// Neutralize REAL 3D inside the tooltip subtree (part A): every descendant carrying a
    /// non-identity local rotation or non-zero local z is recorded once and clamped
    /// (rotation → identity, z → 0). X/Y are never touched (the game's fade/slide
    /// animations keep playing flat). Re-run every frame — tooltip lines are pooled and
    /// rebuilt per hover and the game rewrites them — with destroyed entries pruned so the
    /// record list stays bounded to the live subtree. Mirrors
    /// <see cref="CanvasConversion.FlattenSubtree"/> for this shared, un-converted canvas.
    /// </summary>
    private void FlattenSubtree()
    {
        if (_canvas == null)
            return;

        // Prune destroyed entries (pooled tooltip lines come and go per hover).
        for (int i = _flattened.Count - 1; i >= 0; i--)
        {
            if (_flattened[i].Transform == null)
                _flattened.RemoveAt(i);
        }

        RectScratch.Clear();
        _canvas.GetComponentsInChildren(includeInactive: true, RectScratch);
        Transform canvasTf = _canvas.transform;
        for (int i = 0; i < RectScratch.Count; i++)
        {
            RectTransform rect = RectScratch[i];
            // The canvas root's own pose is ours (scale + placement above) — flatten only
            // the content below it.
            if (rect == null || ReferenceEquals(rect, canvasTf))
                continue;

            Vector3 lp = rect.localPosition;
            Quaternion lr = rect.localRotation;
            bool tiltedRot = Quaternion.Angle(lr, Quaternion.identity) > FlattenAngleEpsilon;
            bool tiltedZ = Mathf.Abs(lp.z) > FlattenZEpsilon;
            if (!tiltedRot && !tiltedZ)
                continue;

            if (!IsFlattenRecorded(rect))
            {
                _flattened.Add(new FlattenEntry
                {
                    Transform = rect,
                    OriginalLocalZ = lp.z,
                    OriginalLocalRotation = lr,
                });
            }
            if (tiltedRot)
                rect.localRotation = Quaternion.identity;
            if (tiltedZ)
                rect.localPosition = new Vector3(lp.x, lp.y, 0f);
        }
        RectScratch.Clear();
    }

    private bool IsFlattenRecorded(Transform rect)
    {
        for (int i = 0; i < _flattened.Count; i++)
        {
            if (ReferenceEquals(_flattened[i].Transform, rect))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Clear <c>raycastTarget</c> on every graphic under the tooltip canvas that still
    /// has it set, recording each flip for <see cref="Restore"/> (see the
    /// <see cref="_raycastCleared"/> doc for why the tooltip must be ray-transparent
    /// while world-space). Only true->false flips are recorded, so graphics the game
    /// already spawns ray-transparent (the line texts) are never touched or restored.
    /// Destroyed entries are pruned so the record list stays bounded to the live
    /// pooled subtree - the same discipline as the flatten record above.
    /// </summary>
    private void NeutralizeRaycasts()
    {
        if (_canvas == null)
            return;

        for (int i = _raycastCleared.Count - 1; i >= 0; i--)
        {
            if (_raycastCleared[i] == null)
                _raycastCleared.RemoveAt(i);
        }

        GraphicScratch.Clear();
        _canvas.GetComponentsInChildren(includeInactive: true, GraphicScratch);
        for (int i = 0; i < GraphicScratch.Count; i++)
        {
            Graphic g = GraphicScratch[i];
            if (g == null || !g.raycastTarget)
                continue;
            g.raycastTarget = false;
            _raycastCleared.Add(g);
        }
        GraphicScratch.Clear();
    }

    /// <summary>
    /// Add a <see cref="RectMask2D"/> on the tooltip frame so text is clipped inside the
    /// panel (part A). Idempotent; if the frame already carries one we leave it alone and
    /// never destroy it on Restore.
    /// </summary>
    private void EnsureFrameClip()
    {
        if (_tooltip == null || _addedMask != null)
            return;
        var frame = _tooltip.transform as RectTransform;
        if (frame == null)
            return;
        RectMask2D existing = frame.GetComponent<RectMask2D>();
        if (existing != null)
            return; // game already clips this frame — don't touch/destroy it
        _addedMask = frame.gameObject.AddComponent<RectMask2D>();
    }

    private void Restore()
    {
        if (!_converted)
            return;
        _converted = false;
        CanvasConversion.RemoveMaskRequest();

        // MIXED REALITY: destroy the plate NOW rather than letting it be deactivated. It is the
        // one MrBacking plate parented under a GAME rect (the tooltip widget's own), and this
        // method's whole contract is that the vanilla 2D menu tooltip is handed back exactly as
        // found — an inactive mod quad left hanging under it would be the single piece of that
        // teardown that never happened. MrBacking.Release is idempotent and works with MR off,
        // which the normal deactivate-on-MR-off path cannot do (its sweep does not tick then).
        MrBacking.Release(_mrBacking);
        _mrRegistered = false;
        _backingShown = false;
        _mrEvidenceLogged = false;

        // Mip-baked graphics back to the game's originals BEFORE the canvas returns to
        // screen space (mutate-and-restore contract; the 2D menu tooltip keeps its
        // authored sprites/textures — the baked copies are a VR presentation detail).
        PanelMipBake.Restore(_canvas);

        // Un-flatten: original local z + rotation back per live recorded transform.
        for (int i = 0; i < _flattened.Count; i++)
        {
            Transform tf = _flattened[i].Transform;
            if (tf == null)
                continue;
            Vector3 lp = tf.localPosition;
            tf.localPosition = new Vector3(lp.x, lp.y, _flattened[i].OriginalLocalZ);
            tf.localRotation = _flattened[i].OriginalLocalRotation;
        }
        _flattened.Clear();

        // Remove only a mask we added.
        if (_addedMask != null)
        {
            Object.Destroy(_addedMask);
            _addedMask = null;
        }

        // Hand back raycastTarget on every graphic we cleared (all were true when
        // recorded - see NeutralizeRaycasts), so the vanilla 2D menu tooltip keeps
        // whatever hit-testing the game authored.
        for (int i = 0; i < _raycastCleared.Count; i++)
        {
            Graphic g = _raycastCleared[i];
            if (g != null)
                g.raycastTarget = true;
        }
        _raycastCleared.Clear();

        // Revert the widened fade (user #7b) so the vanilla 2D menu tooltip keeps its
        // authored transition — before we drop the reference.
        if (_transitionOverridden && _tooltip != null)
        {
            _tooltip.transition = _originalTransition;
            _tooltip.transitionDuration = _originalTransitionDuration;
        }
        _transitionOverridden = false;

        _tooltip = null;
        _lastShownTime = float.NegativeInfinity;
        _parkedLogged = false;
        // The wire must never carry a torn-down presentation's text (extras record 9 is
        // "written only while a board-owned tooltip is actually shown").
        WireText = null;
        _wireComposedTemplate = null;
        _wireComposedText = null;
        _wireLoggedText = null;
        _wireLoggedSuppressed = false;
        // Host ownership is per-hint state — a torn-down presentation must never hand the next
        // session a stale window reference (its panel is released by then), and the change-gated
        // diagnostic re-arms so the first decision after a restore is logged again.
        _hostWindow = null;
        _hostLoggedWindow = null;
        _hostLoggedOnce = false;

        if (_canvas != null)
        {
            _canvas.renderMode = _originalMode;
            _canvas.worldCamera = _originalCamera;
            _canvas.planeDistance = _originalPlaneDistance;
            _canvas.transform.localScale = _originalScale;
            _canvas.sortingOrder = _originalSortingOrder;
            _canvas.overrideSorting = _originalOverrideSorting;
            VRLog.Info("WorldUI", "Tooltip canvas restored to screen space (flatten + frame clip reverted).");
        }
    }

    public void Shutdown() => Restore();
}
