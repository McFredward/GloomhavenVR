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
    ///   * MrBacking's opaque per-host plate at 0, whose contract is "below every host canvas";
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
    /// </summary>
    private const int PanelOrderStep = 16;

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
    /// </summary>
    private const float OrderSwapMarginMeters = 0.02f;

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

    /// <summary>
    /// Register a mod-owned <see cref="Canvas"/> that must ride <paramref name="panel"/>'s ladder
    /// order at a fixed <paramref name="offset"/> (1..<see cref="PanelOrderStep"/>-1: above its own
    /// panel's content, still below the next panel on the ladder). Idempotent per canvas; the entry
    /// is dropped automatically once the canvas is destroyed. Used by
    /// <see cref="ModalCloseButton"/> for the visible X plate.
    /// </summary>
    internal static void RegisterOrderFollower(ConvertedPanel panel, Canvas canvas, int offset)
    {
        if (panel == null || canvas == null)
            return;
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
    /// order (see the Canvas overload). Used by <see cref="GrabbableModal"/> for the brass grab bar,
    /// which lives on a SCENE-ROOT holder rather than under the host - the ladder is a sortingOrder
    /// value, not a hierarchy relation, so that makes no difference here.
    /// </summary>
    internal static void RegisterOrderFollower(ConvertedPanel panel, Renderer renderer, int offset)
    {
        if (panel == null || renderer == null)
            return;
        for (int i = 0; i < panel.OrderFollowers.Count; i++)
        {
            if (ReferenceEquals(panel.OrderFollowers[i].Renderer, renderer))
                return;
        }
        panel.OrderFollowers.Add(new OrderFollower { Renderer = renderer, Offset = offset });
        ApplyPanelOrder(panel, panel.DrawSortingOrder);
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
        for (int i = 0; i < Active.Count; i++)
        {
            ConvertedPanel panel = Active[i];
            if (!panel.IsAlive || panel.HostCanvas == null || panel.HostRect == null)
                continue;
            panel.OrderDistance = PanelEyeDistance(panel, eye);
            if (panel.OrderListed)
                continue;
            int at = OrderedPanels.Count;
            for (int j = 0; j < OrderedPanels.Count; j++)
            {
                ConvertedPanel other = OrderedPanels[j];
                // Far to near. A tie inside the swap margin falls to the CONVERSION tier, which is
                // the one thing ModalHostSortingOrder still decides: two panels the player cannot
                // tell apart in depth put the modal in front of the HUD panel it is a dialog for.
                bool nearerThanOther = panel.OrderDistance < other.OrderDistance - OrderSwapMarginMeters;
                bool tiedAndDominant = !nearerThanOther
                                       && panel.OrderDistance <= other.OrderDistance + OrderSwapMarginMeters
                                       && panel.BaseSortingOrder > other.BaseSortingOrder;
                if (nearerThanOther || tiedAndDominant)
                    continue;
                at = j;
                break;
            }
            OrderedPanels.Insert(at, panel);
            panel.OrderListed = true;
            panel.OrderSwapPeer = null;
            panel.OrderSwapStreak = 0;
        }

        // (3) ONE hysteresis-gated adjacent-swap pass (see OrderSwapMarginMeters /
        // OrderSwapStableFrames). One pass per frame is enough because the list is only ever
        // slightly out of order: newcomers are inserted correctly, and everything else drifts.
        for (int i = 0; i + 1 < OrderedPanels.Count; i++)
        {
            ConvertedPanel far = OrderedPanels[i];
            ConvertedPanel near = OrderedPanels[i + 1];
            if (far.OrderDistance >= near.OrderDistance - OrderSwapMarginMeters)
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
            i++; // the pair just settled - do not re-test it in the same pass
        }

        // (4) Assign. Change-gated inside ApplyPanelOrder.
        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            int rank = i < PanelOrderMaxRank ? i : PanelOrderMaxRank;
            ApplyPanelOrder(OrderedPanels[i], PanelOrderBase + rank * PanelOrderStep);
        }

        // (5) Non-canvas transparent furniture (the control board's placard/labels/glows)
        // ranks against the same measured distances - see part 9's root-cause header.
        TickFurnitureOrder(eye);

        LogPanelOrder();
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
            panel.HostCanvas.sortingOrder = order;
        for (int i = panel.OrderFollowers.Count - 1; i >= 0; i--)
        {
            OrderFollower f = panel.OrderFollowers[i];
            if (f.Canvas != null)
            {
                int want = order + f.Offset;
                if (f.Canvas.sortingOrder != want)
                    f.Canvas.sortingOrder = want;
                continue;
            }
            if (f.Renderer != null)
            {
                int want = order + f.Offset;
                if (f.Renderer.sortingOrder != want)
                    f.Renderer.sortingOrder = want;
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
        int hash = 17;
        for (int i = 0; i < OrderedPanels.Count; i++)
        {
            ConvertedPanel p = OrderedPanels[i];
            hash = hash * 31 + (p.HostGo != null ? p.HostGo.GetInstanceID() : 0);
        }
        float now = Time.unscaledTime;
        bool changed = hash != s_orderDiagLastHash;
        bool heartbeat = now >= s_orderDiagHeartbeatAt;
        if (!changed && !heartbeat)
            return;
        if (changed && now < s_orderDiagNextAllowed && !heartbeat)
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
            if (p.RenderHidden)
                sb.Append(" (hidden)");
        }
        VRLog.Info("WorldUI", $"PANEL DRAW ORDER ({OrderedPanels.Count} panel(s), far->near, " +
                              $"{(changed ? "RESORTED" : "steady")}):{sb}" +
                              (OrderedPanels.Count > listed ? $"; +{OrderedPanels.Count - listed} more." : ".") +
                              " Nearer = higher order = painted later; no panel writes depth, so every " +
                              "transparent pixel shows what is behind it.");
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
