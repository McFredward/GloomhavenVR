using System.Collections.Generic;
using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI;

// CanvasConversion part 8 (per-frame distance draw order for converted panels). NEW members
// only - appended after parts 1-4 in the filename sort, so the existing member/static-initializer
// order (which the refactor guard tracks and part 1's header explains) is untouched.

internal static partial class CanvasConversion
{
    // ---- panels compose by DISTANCE, not by depth stamps -------------------------------------
    //
    // USER RULING (third attempt; the first two shipped and failed on hardware), verbatim:
    // "Such eine Loesung in der die Perspektive gewahrt bleibt aber eine vollstaendige Transparenz
    // herrscht - auch zu anderen Elementen wie dem Infoboard oder Menu das dahinter ist. Bisher
    // ist es nur bei diesen Elementen sichtbar; im Level sieht es ja vollstaendig transparent aus,
    // also muss das ja auch irgendwie moeglich sein."
    // Two things at once: a panel spatially IN FRONT of another must occlude it, AND a panel's
    // transparent areas must let everything behind them through - other converted panels included.
    //
    // ROOT CAUSE OF THE TWO FAILED ATTEMPTS. Both stamped an invisible DEPTH-WRITING mesh at the
    // panel plane (queue 2999, before all ~3000 canvas content) so later-drawn panel content would
    // fail ZTest behind it: CanvasConversion.5.Depth.cs per host, GrabbableModal.BuildDepthMask per
    // floated menu, ModalCloseButton's plate stamp. A depth stamp is a BINARY, per-quad statement
    // ("everything behind this box is gone"), and a panel's transparency is per PIXEL. The user
    // diagnosed it exactly: against the LEVEL it looked perfect, only against other PANELS did a
    // block appear - level geometry is opaque and already in the framebuffer at queue <=2500, so a
    // stamp cannot erase it, but a LATER panel's pixels ARE discarded inside the stamp and the
    // already-drawn level shows through a hard-edged rectangle
    // (.planning/debug/initiativereihenfolge_transparenz.png: a flat grey rectangle around the two
    // initiative portraits with the pause-menu row "SPIEL VERLASSEN" cut out of it).
    //
    // Attempt 2 shrank the quads to the measured "ink" (the deleted CanvasConversion.7.Ink.cs).
    // The hardware log is the proof that a rectangle can never express the transparency the user
    // sees, because that transparency lives INSIDE the box (rounded corners, the gap between a
    // badge and its frame, a bar's alpha ramp):
    //     HOST DEPTH-MASK INK: stamp 128685 of 131541 host px^2 (2 % less ...) Panel_InitiativeTrack
    //     HOST DEPTH-MASK INK: stamp  11479 of  12697 host px^2 (10 % less ...) Panel_ActorBar
    // 2 % and 10 %. A bounding box around a portrait IS the portrait's rect.
    //
    // THE FIX: NO DEPTH WRITING ANYWHERE BETWEEN PANELS - ORDER THEM INSTEAD. Every converted host
    // is a FLAT RectTransform plate that writes no depth. Painting the plates FAR TO NEAR with plain
    // alpha blending is the painter's algorithm, and for surfaces that do not cross each other the
    // painter's algorithm is not an approximation of correct compositing - it IS correct
    // compositing. And because nothing writes depth, a transparent pixel is transparent all the way
    // down: through the panel behind it, through the menu behind that, to the level. That is the
    // user's second requirement, met by construction rather than by ever-tighter approximation.
    //
    // Unity sorts transparent renderers by sortingLayer -> sortingOrder -> distance. This pass
    // therefore rewrites every converted host's Canvas.sortingOrder each frame from its measured
    // eye distance, farthest = lowest order. The regression the depth stamps were originally built
    // for is the same ordering question and is fixed here too: the floated menu no longer sits on a
    // dominant tier that beats distance, so a menu BEHIND the initiative track is now painted
    // BEFORE the portraits and can no longer blend through them.
    //
    // WHAT THIS TRADES AWAY, STATED HONESTLY. The old rejection argument (at the top of the deleted
    // CanvasConversion.5.Depth.cs) claimed order "can never resolve interpenetrating/oblique panels
    // per pixel". That half of it is TRUE, and a survey of every placement path in this module says
    // converted plates really can cross - they are NOT a set of guaranteed-parallel billboards:
    //   * the head-relative family is yaw-only (PanelPlacement.Facing flattens .y), the board-docked
    //     family carries the PlayTray mount's rotation (TablePanelSurfaces.Place), and ActorBars
    //     billboard with a FULL 3-axis LookAt (ActorBars.cs, `fromHead` is not flattened). Three
    //     unrelated rotation sources means generically non-parallel planes.
    //   * nothing keeps them apart at runtime. The only anti-overlap machinery is a SPAWN-time,
    //     best-effort, modal-only AABB resolve that is documented to give up
    //     (ModalFallback.9.Spawn.cs: "Best-effort: nothing cleared inside the view cone"), is
    //     switched off for stacked secondaries, and never runs per frame - and a grabbed window is
    //     written straight onto the host (GrabbableModal.SyncHostToFrame), so the player can push a
    //     menu through the initiative track by hand.
    // Where two plates genuinely cross, one order for the whole canvas is one answer where the
    // honest answer would be two. THAT IS STILL THE BETTER TRADE, because the thing it replaces
    // never delivered the per-pixel answer either: a depth stamp is a per-QUAD statement, so in the
    // crossing case it produced a per-quad answer AND, in every non-crossing case, a guaranteed
    // hard-edged hole. The new failure mode is a wrong-but-STABLE whole-panel order in a geometry
    // the player has to construct deliberately; the old one was a grey block around the initiative
    // portraits every single time the pause menu was open. The diagnostic below prints the resolved
    // ladder precisely so a "wrong side" report can be told apart from a "hole" report at a glance.
    //
    // WHY THE ORDER DOES NOT FLICKER (the other half of the old rejection). The historical flicker
    // (ModalFallback.ModalHostSortingOrder's doc) was an EQUAL-order tie broken by Unity's own
    // per-canvas distance, recomputed from scratch every frame: two overlapping order-0 hosts whose
    // camera distances differ by a millimetre swap every time the head micro-moves. This pass never
    // recomputes an order from scratch. It keeps a PERSISTENT far-to-near sequence and only ever
    // lets two ADJACENT panels change places when BOTH gates are passed: their measured distances
    // must disagree with the current sequence by more than <see cref="OrderSwapMarginMeters"/>, and
    // that disagreement must persist for <see cref="OrderSwapStableFrames"/> consecutive frames.
    // Head micro-motion is millimetres and sub-frame; it clears neither gate, so the sequence is
    // bit-stable while the player just looks around, and a genuine move (grabbing a window and
    // pulling it toward you) re-sorts within ~0.1 s.
    //
    // WHAT IS EXEMPT FROM THE DISTANCE LADDER, AND WHY:
    //   * Nested canvases the game or this mod keeps at overrideSorting=TRUE. Two classes, both
    //     deliberately above every panel: a uGUI Dropdown's transient "Dropdown List"/"Blocker"
    //     (re-based to 4000/3999 by CanvasConversion.2.Adopt.cs - an open dropdown must cover the
    //     menu that spawned it, and it dies on close), and the ModalCloseButton HIT plane (1100,
    //     invisible: it exists only to win a raycast tie, see below). Every OTHER nested canvas is
    //     adopted with overrideSorting cleared, so it inherits its host's ladder order for free.
    //   * The laser beam / hit dot (RayInteractor.RayVisualSortingOrder = 5000) and the mod's own
    //     opaque furniture. The pointer must be visible over whatever it points at; it is a cursor,
    //     not a panel.
    //   * Anything a panel OWNS that must draw at a fixed offset from its own plate - the grab bar,
    //     the close X - is not exempt at all: it registers as an ORDER FOLLOWER
    //     (<see cref="RegisterOrderFollower(ConvertedPanel, Canvas, int)"/>) and rides the ladder
    //     with its owner. <see cref="PanelOrderStep"/> leaves room for those offsets between two
    //     adjacent panels, so a NEARER panel still outranks the farther panel's own decorations.
    //
    // RAYCASTING IS DELIBERATELY NOT AFFECTED. UguiPointer.Beats compares sortingOrder to resolve a
    // hit between a HOST and its own nested canvases (host content vs the initiative track's
    // order-40 inner canvas, vs the element board's -1 underlay, vs the X's 1100 hit plane). Those
    // comparisons are calibrated against the order the host was CONVERTED with, so the ladder would
    // silently invert them. <see cref="BaseSortingOrderOf"/> hands the pointer that conversion-time
    // value instead of the live one, which makes every raycast decision bit-identical to the
    // shipped builds while the DRAW order moves freely.

    /// <summary>
    /// Draw order of the FARTHEST converted panel. The whole ladder sits above every OTHER
    /// transparent order this mod and this game use, which is what makes a panel a panel:
    ///   * the game's own canvas orders (seen: -1, 0, 1, 40);
    ///   * MrBacking's opaque per-host plate — which since the MR perspective round (2026-08-05)
    ///     is NOT a fixed order 0 any more but an offset-0 ORDER FOLLOWER of its own panel: it
    ///     shares its panel's slot and only its earlier renderQueue (2998 vs ~3000) draws it
    ///     under that panel's content. At order 0 a FARTHER panel's content (order ≥ this base)
    ///     painted over a NEARER panel's plate — menus shone through each other's backings in MR;
    ///   * the control board's transparent furniture - keycap face sprites (&lt;=1), the button dust
    ///     FX (2), the engraved keycap labels (3). Those already carry "under panel canvases"
    ///     comments; before this round the order-0 HUD hosts contradicted them and a keycap face
    ///     painted over a panel that was spatially in front of it, which is the same "order beats
    ///     distance" defect this file exists to remove. Part 9 has since put that furniture ON the
    ///     ladder as a distance-ranked GROUP (user 2026-08-04: the board's status placard was
    ///     painted over by the options menu BEHIND the board) - with no panel behind the board its
    ///     band tops out at PanelOrderBase-1, i.e. still under every panel, the shipped contract.
    /// Cards need nothing from the ladder in either direction: a fan/tray card's backing slab is
    /// depth-writing AlphaTest geometry at queue 2450, so it stamps its own footprint before any
    /// canvas draws and resolves against panels by real depth, per pixel, whatever the orders say.
    /// THE ONE EXCEPTION, NAMED SO IT IS NOT REDISCOVERED: a card body wearing the FACE-HOSTED mesh
    /// (<c>Cards.CardMesh.SetBodyFaceHosted</c>) has had its front fan removed and stamps NOTHING
    /// over its own face, so a panel behind it wins by order after all. That is granted only to a
    /// slab in a peer board's fade set (<c>Net.PeerBoardFade.BelongsToAFadeSet</c>) — never to a
    /// local card, which is the population this exemption was written about. The
    /// <c>CARD BODY DEPTH STAMP</c> log line reads a real body back and says which of the two a
    /// given card is.
    /// </summary>
    private const int PanelOrderBase = 100;

    /// <summary>
    /// Order gap between two adjacent panels on the ladder. The 15 values between them belong to the
    /// nearer-of-the-two panel's own decorations (<see cref="RegisterOrderFollower(ConvertedPanel,
    /// Canvas, int)"/>): a window's close X at +2 draws over that window's own backing, the grab bar
    /// at +4 over both, and the game's hover tooltip laid on a menu plane rides at
    /// <c>WorldTooltips.MenuPanelSortingLift</c> (+10, read live off the host canvas) - while the
    /// NEXT panel on the ladder still starts a full step above all of them, so a nearer panel is
    /// never pierced by a farther panel's furniture. The step must stay ABOVE every registered
    /// follower offset for that guarantee to hold.
    ///
    /// <para><b>INTERNAL, AND THE INVARIANT IS NOW CHECKED (ModBuild 439, survey row R41).</b> This
    /// was <c>private const</c>, so the offsets that must stay under it lived as ten separate
    /// literals in ten files — <c>GrabBarLayout.BarOrderOffset</c> 4,
    /// <c>ModalCloseButton.XOrderOffset</c> 2, <c>WorldTooltips.MenuPanelSortingLift</c> 10, the
    /// table surfaces' 12, <c>WindowMaterialiseDebris</c>'s +/-1, plus prose restatements in
    /// <c>WristHud</c>, <c>FreeLabelOrder</c>, <c>HandGhost</c>, <c>BoardVisual</c> and
    /// <c>MapRoomHand.3.Wrist</c> that each quote "(16)" as a number. 2 &lt; 4 &lt; 10 &lt; 12 &lt;
    /// 16 is consistent today and there was no assert: an offset written at 16 or above would pierce
    /// the next panel on the ladder SILENTLY, which is the one failure mode this whole file exists
    /// to prevent.</para>
    ///
    /// <para>The check is at the REGISTRATION SEAM (<see cref="CheckFollowerOffset"/>) rather than a
    /// compile-time assertion over a hand-kept list of constants, because a hand-kept list is the
    /// same defect one level up — it would have to be edited by the person who forgot. Every
    /// follower in the mod goes through <c>RegisterOrderFollower</c>, whatever file its offset was
    /// authored in, so the seam sees all ten and anything a later round adds.</para>
    /// </summary>
    internal const int PanelOrderStep = 16;

    /// <summary>Highest ladder rank that still gets its own order slot; deeper panels share the top
    /// slot. Keeps the whole ladder (100 + 180*16 = 2980) below the adopted dropdown overlays
    /// (3999/4000, CanvasConversion.2.Adopt.cs) and the ray visuals (5000) even in a pathological
    /// scene - ~30 live panels is the realistic maximum.</summary>
    private const int PanelOrderMaxRank = 180;

    /// <summary>
    /// How far apart (metres, eye to the nearest point of each panel's rect) two adjacent panels'
    /// distances must disagree with the current sequence before a swap is even considered. Head
    /// micro-motion moves a panel's measured distance by well under a millimetre per frame; 2 cm is
    /// far outside that and far inside any deliberate reposition (a grabbed window travels 5-30 cm
    /// per frame - hardware log, GrabbableModal.LateSyncHost).
    ///
    /// <para>THIS IS THE AUTHORED REAL-WORLD VALUE AND IT IS NOT WHAT ANY COMPARISON MAY USE. Every
    /// distance on this ladder is a WORLD distance, and the two are the same number only outside a
    /// scenario. Use <see cref="OrderSwapMargin"/>; see <see cref="s_orderSwapMarginWorld"/> for the
    /// defect that reading this constant directly produced at ~198 world units per metre. The
    /// paragraph above only becomes TRUE once the conversion is applied — before it, "2 cm is far
    /// outside head micro-motion" described 0.1 mm.</para>
    /// </summary>
    private const float OrderSwapMarginMeters = 0.02f;

    /// <summary>
    /// <see cref="OrderSwapMarginMeters"/> CONVERTED INTO THE UNIT EVERY COMPARISON ON THIS LADDER IS
    /// ACTUALLY MADE IN, refreshed once per pass at the top of <see cref="TickPanelOrder"/>.
    ///
    /// <para>THE DEFECT THIS REMOVES. <see cref="PanelEyeDistance"/> returns
    /// <c>Vector3.Distance</c> between two WORLD points, so every distance on this ladder — and every
    /// <c>eyeDistance</c> the <see cref="OrderAboveDistance"/> callers hand in, which they compute the
    /// same way — is in WORLD units. The map room's diorama runs at ~198 world units per real metre
    /// (<see cref="PanelLayout.WorldScale"/> is the rig's lossy scale), so comparing those numbers
    /// against a bare 0.02 made the anti-flicker margin 0.02/198 = 0.1 mm of APPARENT distance: not a
    /// conservative margin, an absent one. The constant's own doc claims "head micro-motion moves a
    /// panel's measured distance by well under a millimetre per frame; 2 cm is far outside that" —
    /// at 198x, one real millimetre of head motion moves the number by ~0.2 world units, TEN TIMES
    /// the ungated value. Only <see cref="OrderSwapStableFrames"/> was still holding the sequence
    /// together, and a SUSTAINED head move (leaning back, looking up at a window mounted high) clears
    /// a six-frame streak trivially — so two panels at nearly the same distance could swap draw order
    /// on head POSITION. Two further consequences, both of them silent: <c>tiedAndDominant</c> at
    /// insertion (the one rule that still puts a modal in front of the HUD panel it is a dialog for)
    /// required |dd| ≤ 0.02 WORLD units and could essentially never fire in the map room, and
    /// <see cref="OrderAboveDistance"/>'s documented "a tie means the surface I must draw over" rule
    /// was dead for every caller there (WristHud, CardGlow, BoardVisual, the quest surfaces).</para>
    ///
    /// <para>Same bug class as the laser drawn 0.15 mm wide at 198x rig scale: a bound named "…Meters"
    /// compared against a world-unit product. The constant above keeps the authored REAL-WORLD value
    /// — that is the number a human tunes — and this is the only value any comparison may use.</para>
    ///
    /// <para>ALL SIX COMPARISON SITES WERE SWITCHED OVER IN THE SAME BUILD, which matters because two
    /// tolerances answering the same question is how this defect survived in the first place: the four
    /// in parts 9 and 9b (<c>CanvasConversion.9.Furniture.cs</c> and
    /// <c>CanvasConversion.9b.SeeThrough.cs</c>) run INSIDE this pass, at steps 5 and 6 of
    /// <see cref="TickPanelOrder"/>, so the value below is already fresh for them and nothing else was
    /// needed. <b>Any NEW comparison against a <see cref="PanelEyeDistance"/> result must use
    /// <see cref="OrderSwapMargin"/>, never the authored constant.</b></para>
    /// </summary>
    private static float s_orderSwapMarginWorld = OrderSwapMarginMeters;

    /// <summary>The live swap margin, in WORLD units — see <see cref="s_orderSwapMarginWorld"/>. Read
    /// per comparison; re-derived once per <see cref="TickPanelOrder"/> pass, never per panel.</summary>
    private static float OrderSwapMargin => s_orderSwapMarginWorld;

    /// <summary>The world scale the live margin was derived from, kept for the diagnostic line so the
    /// number in the log can be checked rather than believed.</summary>
    private static float s_orderSwapMarginScale = 1f;

    /// <summary>Consecutive frames the margin must be exceeded before the swap is applied. The
    /// second, independent flicker gate: a one-frame excursion (a tween overshoot, a single stale
    /// pose from GrabbableModal's own LateUpdate, which has no execution-order relation to this
    /// pass) can never reorder anything. Six frames is ~0.07 s at 90 Hz - invisible as a delay.</summary>
    private const int OrderSwapStableFrames = 6;

    /// <summary>Panels named in one diagnostic line (log hygiene; the count is always reported).</summary>
    private const int OrderDiagMaxListed = 12;

    /// <summary>Minimum seconds between two diagnostic lines even when the sequence keeps changing
    /// (a dragged window would otherwise log every few frames).</summary>
    private const float OrderDiagMinIntervalSeconds = 1.5f;

    /// <summary>Seconds after which the resolved order is re-stated even if nothing changed - the
    /// hardware log must always carry a recent ladder to read a report against.</summary>
    private const float OrderDiagHeartbeatSeconds = 20f;

    /// <summary>
    /// The persistent far-to-near sequence. NOT rebuilt per frame: that is the whole flicker
    /// argument above. Panels are inserted at their distance-correct place when they convert and
    /// removed when they die; between those events only the hysteresis-gated adjacent swap in
    /// <see cref="TickPanelOrder"/> may change it.
    /// </summary>
    private static readonly List<ConvertedPanel> OrderedPanels = new(32);

    private static float s_orderDiagNextAllowed;
    private static float s_orderDiagHeartbeatAt;
    private static int s_orderDiagLastHash;

    // ---- work counters (S2 perf round) --------------------------------------------------------
    // COUNT WORK, NOT TIME (PerfMonitor.Count's own doctrine): the hardware log prices this step at
    // ~0.45 ms/frame with 29 panels, and a millisecond figure cannot say whether that is the 29
    // distance measures, the sortingOrder writes a churning ladder produces, or the diagnostic hash.
    // These four counters make the next capture answer it arithmetically. They are plain ints
    // accumulated in the frame and handed to PerfMonitor ONCE per frame, so the per-item cost is an
    // increment and never a dictionary lookup.
    private static int s_orderDistanceMeasures;
    private static int s_orderWrites;
    private static int s_orderResorts;
    private static int s_orderHashNodes;

    /// <summary>
    /// THE LADDER'S ONE INVARIANT, CHECKED WHERE IT CAN BE (survey row R41). A follower offset must
    /// stay in <c>[0, <see cref="PanelOrderStep"/>)</c>: at or above the step it lands in the NEXT
    /// panel's slot and paints over a window that is genuinely nearer, and below 0 it ties with the
    /// furniture band whose top is slot-1. Neither is visible — it is a sorting number, so the only
    /// symptom is a panel painted over by a farther panel's decoration, which is exactly the defect
    /// class this file was written for.
    ///
    /// <para>Reported once per offending offset value (not per registration), at the Alert tier
    /// because it is player-visible, and NOT refused: clamping would hide the mistake behind
    /// almost-correct behaviour, and the follower still has to draw. The line names the number and
    /// the bound so the fix is the offset, not this check.</para>
    /// </summary>
    private static readonly HashSet<int> ReportedBadOffsets = new();

    private static void CheckFollowerOffset(int offset, string what)
    {
        if (offset >= 0 && offset < PanelOrderStep)
            return;
        if (!ReportedBadOffsets.Add(offset))
            return;
        // HW-VERIFY
        VRLog.Alert("WorldUI",
            $"PANEL ORDER FOLLOWER OUT OF BAND: '{what}' registered at offset {offset}, which is "
            + $"outside [0, {PanelOrderStep}). The panel ladder gives each converted window a slot "
            + $"{PanelOrderStep} orders wide and its own decorations the {PanelOrderStep - 1} values "
            + "above it; an offset at or above the step lands in the NEXT panel's slot, so this "
            + "decoration will paint OVER a window that is genuinely nearer than the one it belongs "
            + "to, and a negative one ties with the furniture band under its own panel. Nothing is "
            + "clamped: the fix is the offset. This is the silent failure survey row R41 named.");
    }

    /// <summary>
    /// Register a mod-owned <see cref="Canvas"/> that must ride <paramref name="panel"/>'s ladder
    /// order at a fixed <paramref name="offset"/> (1..<see cref="PanelOrderStep"/>-1: above its own
    /// panel's content, still below the next panel on the ladder). Idempotent per canvas; the entry
    /// is dropped automatically once the canvas is destroyed. Used by
    /// <see cref="ModalCloseButton"/> for the visible X plate.
    ///
    /// <para>OFFSET 0 is also legal — for a follower that must draw in the panel's OWN slot but
    /// resolve UNDER the panel's content via its earlier material renderQueue (Unity's transparent
    /// sort: sortingLayer → sortingOrder → renderQueue → distance). That is the MR backing plate's
    /// contract (<see cref="MrBacking"/>, queue 2998 vs the content's ~3000): above every farther
    /// panel and above the furniture band (whose top is slot−1 — a NEGATIVE offset would tie with
    /// it, so negative offsets stay forbidden), yet still behind its own content.</para>
    /// </summary>
    internal static void RegisterOrderFollower(ConvertedPanel panel, Canvas canvas, int offset)
    {
        if (panel == null || canvas == null)
            return;
        CheckFollowerOffset(offset, canvas.name);
        for (int i = 0; i < panel.OrderFollowers.Count; i++)
        {
            if (ReferenceEquals(panel.OrderFollowers[i].Canvas, canvas))
                return;
        }
        panel.OrderFollowers.Add(new OrderFollower { Canvas = canvas, Offset = offset });
        ApplyPanelOrder(panel, panel.DrawSortingOrder); // seat it immediately; no one-frame gap
    }

    /// <summary>
    /// Register a mod-owned <see cref="Renderer"/> that must ride <paramref name="panel"/>'s ladder
    /// order (see the Canvas overload, offset-0 rule included). Used by <see cref="GrabbableModal"/>
    /// for the brass grab bar, which lives on a SCENE-ROOT holder rather than under the host - the
    /// ladder is a sortingOrder value, not a hierarchy relation, so that makes no difference here -
    /// and by <see cref="MrBacking"/> for the per-panel MR backing plate at offset 0.
    /// </summary>
    internal static void RegisterOrderFollower(ConvertedPanel panel, Renderer renderer, int offset)
    {
        if (panel == null || renderer == null)
            return;
        CheckFollowerOffset(offset, renderer.name);
        for (int i = 0; i < panel.OrderFollowers.Count; i++)
        {
            if (ReferenceEquals(panel.OrderFollowers[i].Renderer, renderer))
                return;
        }
        panel.OrderFollowers.Add(new OrderFollower { Renderer = renderer, Offset = offset });
        ApplyPanelOrder(panel, panel.DrawSortingOrder);
    }

    /// <summary>
    /// The draw order a NON-panel canvas at <paramref name="eyeDistance"/> metres from the eye must
    /// use to composite correctly WITH the panel ladder: the highest ladder order among panels that
    /// are FARTHER than (or tied with) that distance, plus <paramref name="lift"/> — so the caller
    /// draws over everything behind it and under every panel genuinely nearer. This is the board
    /// tooltip's slot in the ladder architecture (<c>WorldTooltips</c>, board-owned case): the
    /// game's shared tooltip canvas is not a converted panel — it has no <see cref="ConvertedPanel"/>
    /// to register a follower on — but it IS a plate at a measurable distance, and the ladder's own
    /// far-to-near rule answers where such a plate belongs. Before this hook the board tooltip kept
    /// the game's authored order (≈0), so ANY converted panel (order ≥ <see cref="PanelOrderBase"/>)
    /// painted over it even when the tooltip was clearly in front — the "sometimes covered" defect.
    ///
    /// <para>The tie rule is deliberate: a panel within <see cref="OrderSwapMarginMeters"/> of the
    /// caller counts as "behind it", because the callers of this method sit PROUD of the surface
    /// they annotate (the tooltip floats 2 cm off the board plane the board-docked panels lie in) —
    /// a tie means "the surface I must draw over". <paramref name="lift"/> must stay under
    /// <see cref="PanelOrderStep"/> (same contract as every order follower) so the result never
    /// climbs into the next panel's slot. With no farther panel at all the base sits one step BELOW
    /// the whole ladder — still above the game's own canvases and the board furniture.</para>
    ///
    /// <para>Reads the PREVIOUS frame's measured distances/orders (this runs in callers' LateTicks,
    /// before <see cref="TickPanelOrder"/>) — a one-frame lag on a hysteresis-damped ladder is not
    /// observable.</para>
    /// </summary>
    internal static int OrderAboveDistance(float eyeDistance, int lift)
        => FartherPanelOrder(eyeDistance) + lift;

    /// <summary>
    /// The ladder order of the NEAREST panel that is still behind <paramref name="eyeDistance"/>
    /// (tie = behind, see <see cref="OrderAboveDistance"/>), or one step below the whole ladder
    /// when there is none. Split out so the cluster-aware variant
    /// (<see cref="OrderAboveDistanceAndClusters"/>) can reason about the GAP above that slot —
    /// where every non-panel plate lives — instead of only about "slot + lift".
    /// </summary>
    private static int FartherPanelOrder(float eyeDistance)
    {
        int order = PanelOrderBase - PanelOrderStep;
        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            if (p == null || !p.IsAlive)
                continue;
            // WORLD units on both sides: p.OrderDistance is PanelEyeDistance's world measure and
            // every caller computes its eyeDistance the same way. See s_orderSwapMarginWorld.
            if (p.OrderDistance >= eyeDistance - OrderSwapMargin && p.DrawSortingOrder > order)
                order = p.DrawSortingOrder;
        }
        return order;
    }

    /// <summary>
    /// The sortingOrder a converted host was CONVERTED with, for the one consumer that must not see
    /// the live ladder value: UguiPointer.Beats. See the file header ("RAYCASTING IS DELIBERATELY
    /// NOT AFFECTED"). Returns false when <paramref name="raycasterGo"/> is not a converted host's
    /// own raycaster - nested canvases and non-panel canvases keep reporting their own order, which
    /// is exactly what the shipped comparison expects.
    /// </summary>
    internal static bool BaseSortingOrderOf(GameObject? raycasterGo, out int baseOrder)
    {
        baseOrder = 0;
        if (raycasterGo == null)
            return false;
        for (int i = 0; i < Active.Count; i++)
        {
            ConvertedPanel panel = Active[i];
            if (panel.HostGo != null && ReferenceEquals(panel.HostGo, raycasterGo))
            {
                baseOrder = panel.BaseSortingOrder;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Per-frame service, run LAST in the WorldUI LateUpdate chain (WorldUIModule): assign every
    /// live converted panel a draw order derived from its eye distance, farthest first. Runs after
    /// every pose writer in the frame - the board-docked surfaces re-place their hosts in their own
    /// LateTick, and a stale pose here would be measured one frame late - and before the render
    /// loop, which is where sortingOrder is read.
    ///
    /// <para>Cost: one InverseTransformPoint + one TransformPoint per panel (~30 at most), one
    /// adjacent-pair pass, and change-gated writes. A steady scene writes nothing at all.</para>
    /// </summary>
    internal static void TickPanelOrder()
    {
        Camera? cam = WorldCamera;
        if (cam == null)
            return;
        Vector3 eye = cam.transform.position;

        // THE MARGIN IS AUTHORED IN REAL METRES AND EVERY DISTANCE BELOW IS A WORLD DISTANCE.
        // Convert once per pass, before the first comparison — steps (3)…(6) and every
        // OrderAboveDistance caller in the frame read the same value. See s_orderSwapMarginWorld for
        // the defect this removes and for the four sites in parts 9/9b that still have to follow.
        s_orderSwapMarginScale = Mathf.Max(PanelLayout.WorldScale, 1e-4f);
        s_orderSwapMarginWorld = OrderSwapMarginMeters * s_orderSwapMarginScale;

        s_orderDistanceMeasures = 0;
        s_orderWrites = 0;
        s_orderResorts = 0;
        s_orderHashNodes = 0;

        // (1) Drop entries whose panel died or was released. Cheap flag, no set lookup: Release and
        // the dead-panel prune clear OrderListed (CanvasConversion.4.Lifecycle.cs).
        for (int i = OrderedPanels.Count - 1; i >= 0; i--)
        {
            ConvertedPanel p = OrderedPanels[i];
            if (p == null || !p.OrderListed || !p.IsAlive || p.HostCanvas == null || p.HostRect == null)
            {
                if (p != null)
                {
                    p.OrderListed = false;
                    p.OrderSwapPeer = null;
                    p.OrderSwapStreak = 0;
                }
                OrderedPanels.RemoveAt(i);
            }
        }

        // (2) Measure every live panel, and INSERT newcomers at their distance-correct place. A
        // freshly floated window must land in the right slot on its first frame - it is revealed at
        // its final pose (ConvertedPanel.RevealPending), so there is nothing to converge toward and
        // no reason to make it climb the ladder one swap at a time.
        //
        // SUB-SCOPE (S2 perf round). 'CanvasConversion.Order' measured 0.45 ms/frame with 29 panels
        // on hardware (ModBuild 102), and a single number cannot say whether that is the distance
        // measures (two Unity transform interop calls plus a RectTransform.rect read per panel), the
        // sortingOrder writes a churning ladder produces, or the foreign-surface pass. Three nested
        // scopes split it, and PerfMonitor.Measure is an allocation-free struct whose whole cost off
        // the clock is one static bool test.
        using (Core.PerfMonitor.Scope("Order.Measure"))
        {
            for (int i = 0; i < Active.Count; i++)
            {
                ConvertedPanel panel = Active[i];
                if (!panel.IsAlive || panel.HostCanvas == null || panel.HostRect == null)
                    continue;
                panel.OrderDistance = PanelEyeDistance(panel, eye);
                s_orderDistanceMeasures++;
                StampShownPose(panel);
                if (panel.OrderListed)
                    continue;
                int at = OrderedPanels.Count;
                for (int j = 0; j < OrderedPanels.Count; j++)
                {
                    ConvertedPanel other = OrderedPanels[j];
                    // Far to near. A tie inside the swap margin falls to the CONVERSION tier, which
                    // is the one thing ModalHostSortingOrder still decides: two panels the player
                    // cannot tell apart in depth put the modal in front of the HUD panel it is a
                    // dialog for.
                    bool nearerThanOther = panel.OrderDistance < other.OrderDistance - OrderSwapMargin;
                    bool tiedAndDominant = !nearerThanOther
                                           && panel.OrderDistance <= other.OrderDistance + OrderSwapMargin
                                           && panel.BaseSortingOrder > other.BaseSortingOrder;
                    if (nearerThanOther || tiedAndDominant)
                        continue;
                    at = j;
                    break;
                }
                OrderedPanels.Insert(at, panel);
                s_orderResorts++;
                panel.OrderListed = true;
                panel.OrderSwapPeer = null;
                panel.OrderSwapStreak = 0;
            }
        }

        // (3) ONE hysteresis-gated adjacent-swap pass (see OrderSwapMarginMeters /
        // OrderSwapStableFrames). One pass per frame is enough because the list is only ever
        // slightly out of order: newcomers are inserted correctly, and everything else drifts.
        for (int i = 0; i + 1 < OrderedPanels.Count; i++)
        {
            ConvertedPanel far = OrderedPanels[i];
            ConvertedPanel near = OrderedPanels[i + 1];
            if (far.OrderDistance >= near.OrderDistance - OrderSwapMargin)
            {
                far.OrderSwapStreak = 0;
                far.OrderSwapPeer = null;
                continue;
            }
            if (!ReferenceEquals(far.OrderSwapPeer, near))
            {
                far.OrderSwapPeer = near;
                far.OrderSwapStreak = 0;
            }
            if (++far.OrderSwapStreak < OrderSwapStableFrames)
                continue;
            far.OrderSwapStreak = 0;
            far.OrderSwapPeer = null;
            OrderedPanels[i] = near;
            OrderedPanels[i + 1] = far;
            s_orderResorts++;
            i++; // the pair just settled - do not re-test it in the same pass
        }

        // (4) Assign. Change-gated inside ApplyPanelOrder.
        using (Core.PerfMonitor.Scope("Order.Apply"))
        {
            for (int i = 0; i < OrderedPanels.Count; i++)
            {
                int rank = i < PanelOrderMaxRank ? i : PanelOrderMaxRank;
                ApplyPanelOrder(OrderedPanels[i], PanelOrderBase + rank * PanelOrderStep);
            }
        }

        // (5) Non-canvas transparent furniture (the control board's placard/labels/glows)
        // ranks against the same measured distances - see part 9's root-cause header.
        using (Core.PerfMonitor.Scope("Order.Furniture"))
        {
            TickFurnitureOrder(eye);
        }

        // (6) FOREIGN transparent surfaces the mod cannot re-author rank against the same
        // numbers, and for the same reason (user 2026-08-09: with MR off the see-through
        // undiscovered tiles hide the decision symbols, the initiative track and some board text;
        // with MR on the same tiles, now opaque, fail to hide the board's quest text). Everything
        // in the game's fog-of-war stack ships at sortingOrder 0, so it is painted before every
        // panel and before the board's furniture band whatever the geometry says; ranking it by
        // measured distance paints it in the right place instead, which reads as see-through while
        // the surface is translucent and as occluding while it is opaque - one rule, both states.
        //
        // Two steps, in this order and HERE, after (4) and (5), because both read the orders those
        // two just assigned: the ladder is snapshotted ONCE into the small sorted table part 9b
        // documents, and every foreign surface then resolves against that table with no Unity call
        // at all - see Core.UnseenTileOrder and part 9b.
        using (Core.PerfMonitor.Scope("Order.SeenThrough"))
        {
            BuildSeenThroughLadder(eye);
        }
        Core.UnseenTileOrder.Tick(eye);

        // (7) THE MR BACKING PLATES RE-COPY THE ORDER OF WHAT THEY BACK — and this is the only
        // place in the frame where that copy is guaranteed to be the value that RENDERS.
        //
        // An MR plate is opaque (Blend One Zero, ZWrite 1) and sits a couple of millimetres behind
        // its content, so it stays readable only while it shares that content's sortingOrder — its
        // earlier renderQueue (2998 vs ~3000) is what draws it just UNDER the glyphs inside the
        // shared slot. MrBacking ticks in UPDATE, and steps (4)-(6) above are in LATE UPDATE, so
        // every plate whose content is ranked HERE spent the frame carrying the PREVIOUS frame's
        // order: whenever the new value was lower, the plate ended the frame ranked ABOVE the text
        // and painted it out for exactly that frame. The board's pile captions are the shipped case
        // — nobody writes their order in Update at all, ApplyFurnitureOrder in step (5) does — and
        // the ModBuild 107 hardware log counts 172 furniture-band re-seats in one session.
        //
        // Called from HERE rather than appended to WorldUIModule's late list on purpose: the
        // guarantee is then LOCAL and self-evident ("the orders were just assigned; re-seat the
        // plates that copy them") instead of depending on a step staying last in a list in another
        // file. Full root cause, cost and rejected alternatives: MrBacking.SyncPlateOrders.
        MrBacking.SyncPlateOrders();

        LogPanelOrder();

        // One handoff per frame, four counters, all no-ops while [Perf] Attribution is off
        // (PerfMonitor.Count early-outs on a static bool). 'Order.Panels' is the denominator every
        // other number is read against; 'Order.Writes' is the one that lands in the frame's BLOCKED
        // span, because a Canvas.sortingOrder write is what re-sorts a canvas.
        Core.PerfMonitor.Count("Order.Panels", OrderedPanels.Count);
        Core.PerfMonitor.Count("Order.Distances", s_orderDistanceMeasures);
        Core.PerfMonitor.Count("Order.Writes", s_orderWrites);
        Core.PerfMonitor.Count("Order.Resorts", s_orderResorts);
        Core.PerfMonitor.Count("Order.HashNodes", s_orderHashNodes);
    }

    /// <summary>
    /// Eye distance of a panel: the distance to the CLOSEST POINT OF ITS FINITE RECT, not to its
    /// centre. Two reasons, both from this repo's own hardware history:
    ///
    /// <para>(a) CORRECTNESS FOR BIG PLATES. ModalFallback.ModalHostSortingOrder's doc records what
    /// a centre distance does to a full-screen modal: "a ~32x17 m plane placed 1.2 m in front of the
    /// head that spans ~13 m of depth". Its centre is 1.2 m away while its corners measure ~18 m,
    /// so ANY centre- or bounds-based comparison against a small panel overlapping its edge answers
    /// the wrong question. The nearest point of the rect answers the right one: how close does this
    /// plate actually come to the eye.</para>
    ///
    /// <para>(b) STABILITY. The measure depends only on the eye POSITION and the panel's pose and
    /// size - not on where the head is looking. Turning the head, which is the motion that produced
    /// the historical order flicker, does not move this number at all, so the hysteresis gates only
    /// ever have real motion to reject.</para>
    /// </summary>
    /// <summary>
    /// <b>The last frame in which the shown-pose stamp ran at all.</b> Read by
    /// <c>WindowMaterialise.PlayOut</c> to tell "this panel was not drawn on the previous frame"
    /// (a verdict) from "the stamp has not been running" (no instrument, no verdict): the order
    /// pass early-outs without a world camera, and a rule that refused every vanish because its
    /// instrument was asleep would delete the effect the user asked for.
    /// </summary>
    internal static int ShownStampFrame { get; private set; }

    /// <summary>
    /// <b>Record that this panel was render-visible this frame, and where its host stood.</b> One
    /// place, every frame, for every live panel: the order pass is the LAST WorldUI LateUpdate step
    /// (WorldUIModule.cs), so a stamp taken here describes the pose the eye is about to be shown.
    /// The terms are the mod's OWN visibility switches — the reveal gate, the conversion's render
    /// hide, the owning surface's hide, the host's activity and its canvas — never the game's tweened
    /// alpha, which the close-edge hold deliberately confounds (see
    /// <c>ModalFallback.HasNothingToDissolve</c>). Cost: four flag reads and, on a shown panel, one
    /// world-pose read; no allocation.
    /// </summary>
    private static void StampShownPose(ConvertedPanel panel)
    {
        ShownStampFrame = Time.frameCount;
        if (panel.RenderHidden || panel.OwnerRenderHidden || panel.RevealPending)
            return;
        GameObject? go = panel.HostGo;
        if (go == null || !go.activeInHierarchy)
            return;
        if (panel.HostCanvas != null && !panel.HostCanvas.enabled)
            return;
        RectTransform host = panel.HostRect;
        panel.LastShownFrame = Time.frameCount;
        panel.LastShownPosition = host.position;
        panel.LastShownRotation = host.rotation;
        panel.LastShownLossyScale = host.lossyScale;
    }

    private static float PanelEyeDistance(ConvertedPanel panel, Vector3 eye)
    {
        RectTransform host = panel.HostRect;
        Vector3 local = host.InverseTransformPoint(eye);
        Rect r = host.rect;
        var onPlate = new Vector3(
            Mathf.Clamp(local.x, r.xMin, r.xMax),
            Mathf.Clamp(local.y, r.yMin, r.yMax),
            0f);
        return Vector3.Distance(eye, host.TransformPoint(onPlate));
    }

    /// <summary>Write one panel's ladder order onto its host canvas and every registered follower
    /// (change-gated; dead followers are pruned here rather than needing their own teardown).</summary>
    private static void ApplyPanelOrder(ConvertedPanel panel, int order)
    {
        panel.DrawSortingOrder = order;
        if (panel.HostCanvas != null && panel.HostCanvas.sortingOrder != order)
        {
            panel.HostCanvas.sortingOrder = order;
            s_orderWrites++;
        }
        for (int i = panel.OrderFollowers.Count - 1; i >= 0; i--)
        {
            OrderFollower f = panel.OrderFollowers[i];
            if (f.Canvas != null)
            {
                int want = order + f.Offset;
                if (f.Canvas.sortingOrder != want)
                {
                    f.Canvas.sortingOrder = want;
                    s_orderWrites++;
                }
                continue;
            }
            if (f.Renderer != null)
            {
                int want = order + f.Offset;
                if (f.Renderer.sortingOrder != want)
                {
                    f.Renderer.sortingOrder = want;
                    s_orderWrites++;
                }
                continue;
            }
            panel.OrderFollowers.RemoveAt(i); // destroyed with its owner
        }
    }

    /// <summary>
    /// THE line the next hardware test is read against: the resolved far-to-near ladder with each
    /// panel's name, measured eye distance and assigned order. A report of the form "X still draws
    /// over Y although it is behind it" is answered directly by this line - either the ladder has
    /// them the wrong way round (a distance/measure problem) or it has them right and the artefact
    /// is not an ordering artefact at all. Logged whenever the SEQUENCE changes (rate-limited) and
    /// as a heartbeat every <see cref="OrderDiagHeartbeatSeconds"/>, so a steady scene still carries
    /// a recent ladder in the log.
    /// </summary>
    private static void LogPanelOrder()
    {
        float now = Time.unscaledTime;
        bool heartbeat = now >= s_orderDiagHeartbeatAt;

        // THROTTLE FIRST, HASH SECOND (S2 perf round). The hash is one Unity GetInstanceID interop
        // call PER PANEL and it exists for exactly one purpose: to decide whether to emit a line
        // that is rate-limited to OrderDiagMinIntervalSeconds anyway. In the shipped hardware scene
        // the ladder reports RESORTED on every single diagnostic line, i.e. the hash changes
        // essentially every frame and is then thrown away ~130 frames out of every 135.
        //
        // PROVABLY IDENTICAL OUTPUT. Below BOTH throttles the old body could only reach one of its
        // two returns — `!changed && !heartbeat` or `changed && now < nextAllowed && !heartbeat` —
        // and neither of them wrote a field or logged anything. So the value of the hash was
        // unobservable there, in the log AND in this class's own state. Hoisting the throttle above
        // the hash therefore changes when the hash is COMPUTED and nothing else: the same frames
        // log, with the same text, and s_orderDiagLastHash still carries the hash of the last frame
        // that was allowed to log — which is the only frame it was ever compared against.
        if (!heartbeat && now < s_orderDiagNextAllowed)
            return;

        int hash = 17;
        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            hash = hash * 31 + (p.HostGo != null ? p.HostGo.GetInstanceID() : 0);
        }
        s_orderHashNodes = OrderedPanels.Count;
        bool changed = hash != s_orderDiagLastHash;
        if (!changed && !heartbeat)
            return;
        s_orderDiagLastHash = hash;
        s_orderDiagNextAllowed = now + OrderDiagMinIntervalSeconds;
        s_orderDiagHeartbeatAt = now + OrderDiagHeartbeatSeconds;
        if (OrderedPanels.Count == 0)
            return;

        var sb = new System.Text.StringBuilder(160);
        int listed = OrderedPanels.Count < OrderDiagMaxListed ? OrderedPanels.Count : OrderDiagMaxListed;
        for (int i = 0; i < listed; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            string name = p.HostGo != null ? p.HostGo.name : "<dead>";
            sb.Append(i > 0 ? "; " : " ")
              .Append('\'').Append(name).Append("' d=").Append(p.OrderDistance.ToString("F2"))
              .Append("m order=").Append(p.DrawSortingOrder);
            // Diagnostic label only — the ladder itself deliberately keeps ranking hidden panels
            // (a panel must have its slot the instant it becomes visible again, and both hides are
            // transient). Naming WHICH hide is what makes the next hardware log readable: "gate"
            // is the reveal gate, "focus" is the surface-owned hide of a row that belongs to a
            // character the player is not looking at.
            if (p.RenderHidden)
                sb.Append(ModalFallback.IsDormantPanel(p)
                    ? " (hidden: DORMANT — drawing nothing, seat and pose kept)"
                    : " (hidden: reveal gate)");
            else if (p.OwnerRenderHidden)
                sb.Append(" (hidden: character focus)");
        }
        VRLog.Info("WorldUI", $"PANEL DRAW ORDER ({OrderedPanels.Count} panel(s), far->near, " +
                              $"{(changed ? "RESORTED" : "steady")}):{sb}" +
                              (OrderedPanels.Count > listed ? $"; +{OrderedPanels.Count - listed} more." : ".") +
                              " Nearer = higher order = painted later; no panel writes depth, so every " +
                              "transparent pixel shows what is behind it.");
        LogOrderMargin();
    }

    /// <summary>
    /// THE FALSIFIER FOR THE UNIT FIX — the margin in BOTH units, the scale it was derived from, and
    /// the closest pair of panel distances it is actually being asked to separate. Rides
    /// <see cref="LogPanelOrder"/>'s throttle, so it appears exactly as often as the ladder line and
    /// never on its own cadence.
    ///
    /// <para>WHAT FALSIFIES WHAT. If <c>world</c> is ~0.02 while <c>scale</c> reads ~198, the
    /// conversion did not run and the margin is still 0.1 mm of apparent distance — the defect is
    /// back. If the closest pair's separation is SMALLER than the margin, those two panels are inside
    /// the hysteresis band and their relative order is being held by the persistent sequence alone,
    /// which is the intended behaviour and the thing that stops head motion from re-sorting them.</para>
    /// </summary>
    private static void LogOrderMargin()
    {
        float nearest = -1f, secondNearest = -1f;
        string nearestName = "<none>", secondName = "<none>";
        int n = OrderedPanels.Count;
        if (n >= 1)
        {
            ConvertedPanel p = OrderedPanels[n - 1];
            nearest = p.OrderDistance;
            nearestName = p.HostGo != null ? p.HostGo.name : "<dead>";
        }
        if (n >= 2)
        {
            ConvertedPanel p = OrderedPanels[n - 2];
            secondNearest = p.OrderDistance;
            secondName = p.HostGo != null ? p.HostGo.name : "<dead>";
        }
        float sep = n >= 2 ? Mathf.Abs(nearest - secondNearest) : -1f;
        VRLog.Info("WorldUI",
            $"PANEL ORDER MARGIN: {OrderSwapMarginMeters:F3} m authored, {OrderSwapMargin:F4} world "
            + $"units live, derived from world scale {s_orderSwapMarginScale:F2} world units per real "
            + $"metre. The two NEAREST panels on the ladder are '{secondName}' at "
            + $"{secondNearest:F3} and '{nearestName}' at {nearest:F3} "
            + $"world units from the eye, separated by {sep:F4} world units, i.e. "
            + $"{(n >= 2 && sep < OrderSwapMargin ? "INSIDE" : "outside")} the margin. "
            + $"Stability streak required: {OrderSwapStableFrames} frames. READ IT LIKE THIS: the live "
            + "margin MUST be the authored value times the scale — PanelEyeDistance measures world "
            + "units and the constant is authored in real metres, and comparing them raw is what made "
            + "the anti-flicker gate 0.1 mm of apparent distance in a 198x diorama. A live margin that "
            + "still reads ~0.020 while the scale reads ~198 means the per-pass conversion did not "
            + "run and every order-swap decision is again carried by the six-frame streak alone, "
            + "which a sustained head move clears trivially. A separation reported INSIDE the margin "
            + "is the gate DOING ITS JOB: those two panels keep the order the persistent sequence "
            + "already gave them, whatever the head does.");
    }
}

/// <summary>
/// A mod-owned renderer or canvas that must draw at a FIXED OFFSET from its owning panel's ladder
/// order (see <see cref="CanvasConversion.RegisterOrderFollower(ConvertedPanel, Canvas, int)"/>) -
/// the grab bar and the close X, which belong to their window and must stay with it as the ladder
/// moves. Exactly one of the two references is set.
/// </summary>
internal struct OrderFollower
{
    public Canvas? Canvas;
    public Renderer? Renderer;
    public int Offset;
}
