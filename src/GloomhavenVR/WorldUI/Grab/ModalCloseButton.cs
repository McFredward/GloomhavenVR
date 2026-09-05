using System;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI;

/// <summary>
/// Item 3c: a small mod-drawn X (close) button pinned to the TOP-RIGHT corner of what a floated
/// menu window DRAWS (its frame's top-right corner until ModBuild 242 — see the ModBuild 242
/// paragraph below), so the user can dismiss the window directly (in addition to
/// the modal escape chord, <see cref="ModalFallback.TickEscapeChord"/>). It closes the window
/// through the game's OWN escape/hide path (<see cref="ModalFallback.CloseFloatedWindow"/> →
/// <c>UIWindow.Escape()</c>/<c>Hide()</c>), so game state observes the close normally.
///
/// MECHANISM (no new input path): the button is a plain uGUI <see cref="Button"/> parented to
/// the HOST canvas (a sibling of the game window subtree, NOT inside it). The host canvas is
/// already registered with <see cref="UguiPokeSurfaces"/> and hit by the dominant-hand laser
/// (RayUguiDriver), so the fingertip poke AND the laser drive this button's onClick through the
/// same <c>ExecuteEvents</c> path as every other converted widget — nothing extra to register.
/// Anchored to the host rect's CENTRE at a computed offset (<see cref="PlaceAgainstInk"/>), so it
/// tracks the window as the content fit resizes it AND as what the window draws changes under a tab
/// press. Moved onto the dedicated mod layer with the rest of the host (so only the HMD head
/// camera draws it — no UI-Camera double-draw). Because it lives on the host — not the game tree —
/// it is destroyed with the host on <see cref="CanvasConversion.Release"/> and needs no teardown
/// wiring; the exact 2D restore is untouched.
///
/// The "X" glyph is drawn as two crossed <see cref="Image"/> bars (font-free) so it always
/// renders regardless of which TMP font resolved. EXCLUSION: never attached to the
/// Sieg/Niederlage results windows — they float as grabbable modals like everything else now,
/// but the only way out of the end-of-scenario window must remain its native continue/retry
/// buttons (an X would strand the scenario-end flow).
///
/// Item 2 (style): the plate is SUBTLE and on-theme — a small, muted DARK plate
/// (<c>Color(0.07,0.07,0.10)</c>) carrying a BRASS "X" (<c>Color(0.62,0.5,0.28)</c>) that
/// brightens on hover, instead of the old loud bright-red box. Both tones were sampled from
/// the mod's own (since retired) free-floating settings panel, which the user liked. The glyph
/// is mathematically centred on the plate (both bars anchored + pivoted at the plate centre,
/// zero offset).
///
/// <para><b>ModBuild 242 — THE X FOLLOWS WHAT THE WINDOW DRAWS, NOT WHAT IT FRAMES.</b> User report,
/// verbatim (2026-08-24): <i>"Wenn der Balken klein ist weil die Länge des Fensters klein ist muss
/// auch das 'x' zum Schließen an neuen Rand oben links. Aktuell haben wir die Situation, dass das 'x'
/// weit rechts, der Balken klein und zwischen dem linken Teil und dem X unsichtbare Collider für den
/// Laser ist. Wenn kleineres Fenster, dann voll mit verschobenem X und ohne unsichtbaren
/// Collider."</i></para>
///
/// <para>THE ROOT CAUSE IS THE SAME PREMISE ModBuild 234 WROTE DOWN FOR THE GRAB BAR AND 239 FOR THE
/// RE-FACE PIVOT, arriving at the third piece of window chrome: the button was anchored at (1,1) of
/// the HOST rect with a fixed inset, and a converted window's host rect is frequently mostly empty
/// transparent frame. In the ModBuild 241 hardware log the options window is a 1552x1080 px frame
/// whose ink, with the VR tab open, is <c>x -783..-384</c> — so the X sat at host-local (769,533)
/// while the drawn picture ended at x=-384: 1153 px = 891 mm of nothing between the window and its
/// own close button, which is what he photographed in <c>.planning/debug/Optionsbalken.jpg</c>.</para>
///
/// <para><b>WHICH CORNER, AND THE AMBIGUITY THAT HAD TO BE FLAGGED RATHER THAN GUESSED AWAY.</b> He
/// wrote "an neuen Rand oben links", and this button has always been at the TOP-RIGHT. Reading the
/// photograph reconciles the two: the ink IS the left column, so the ink's own top-RIGHT corner is in
/// the upper-LEFT region of the frame. <see cref="PlaceAgainstInk"/> therefore implements <b>the
/// ink's own top-right corner</b> — the same corner the button has always used, measured against the
/// ink instead of the frame. If he meant the ink's top-LEFT literally, that is the one line marked
/// THE CORNER in that method and nothing else changes.</para>
///
/// <para><b>OUTSIDE THE INK, NOT INSET INTO IT.</b> Against the FRAME an inset is free, because the
/// frame is a transparent margin. Against the INK it is not: the ink is a tight box around what is
/// painted, so any inset lands the plate on a drawn row. The button is therefore placed with its
/// whole width clear of the ink's right edge (<see cref="InkGapPx"/>) and its top edge level with the
/// ink's top — the same convention the brass bar already uses one gap BELOW the ink's bottom. It is
/// then clamped back inside the host rect, which is what makes the change provably free for every
/// window whose ink fills its frame: the clamp bites on both axes and the result is
/// <c>(xMax - InsetPx, yMax - InsetPx)</c>, bit-for-bit the shipped placement.</para>
///
/// <para><b>AND IT TAKES THE INVISIBLE HIT PLANE WITH IT.</b> The <c>HitPlane</c> below is a 34x34 px
/// raycast target on its own elevated canvas, registered as a nested poke surface; anchored to the
/// plate, it followed the frame corner into empty forest and is one of the two invisible interactive
/// surfaces in his report. The OTHER one is not in this file and not a collider at all — see
/// <c>GrabbableModal.ReportClosePlacement</c>, which measures it.</para>
/// </summary>
internal static class ModalCloseButton
{
    /// <summary>The plate's GameObject name — the handle <c>GrabbableModal</c> re-finds it by, and
    /// the first entry of <c>PanelInkBounds.ChromeNames</c>. One literal, two readers.</summary>
    internal const string ObjectName = "GloomhavenVR.ModalCloseX";

    // Small + tasteful (was 46 px). Sits inset from the host's top-right corner.
    private const float ButtonSizePx = 34f;

    /// <summary>Clear space between the ink's right edge and the plate's LEFT edge, in the window's
    /// own authored px (~9 mm at the 0.773 mm/px the ModBuild 241 log reports for the options
    /// window). Small enough to read as belonging to the window, large enough that the plate never
    /// touches a drawn row.</summary>
    private const float InkGapPx = 12f;

    /// <summary>
    /// <b>THE X IS COPLANAR WITH ITS WINDOW, AND THE 4 px VIEWER NUDGE THAT USED TO LIFT IT IS GONE
    /// (ModBuild 439).</b> User, 2026-09-05, verbatim: <i>"Den 'X' Button zum Schließen des
    /// Kampflogs treffe ich nur aus einem guten geraden Winkel. Wenn ich zB von unten mit dem Laser
    /// draufzeige kollidiert der Laser nicht und ich kann es nicht drücken. Bei den anderen Fenstern
    /// ist mir das nicht aufgefallen."</i> — and the useful half of that report is the last sentence:
    /// it is ONE implementation (this file) behaving differently on one panel, not two
    /// implementations.
    ///
    /// <para><b>THE MECHANISM, and it is arithmetic rather than a guess.</b> The far ray picks a
    /// converted panel by intersecting the HOST canvas plane (<c>RayUguiDriver.TryIntersect</c>),
    /// then converts that crossing point to a screen position and raycasts the merged
    /// raycasters there (<c>UguiPointer.TryRaycast</c>). One screen point, taken on the HOST plane,
    /// for every nested canvas whatever its depth. The plate — and with it its invisible
    /// <c>HitPlane</c> child, which is what the beam actually has to hit — floated 4 px in FRONT of
    /// that plane. So a beam aimed at the drawn glyph crossed the host plane
    /// <c>4 x tan(theta)</c> px further along, where theta is the angle between the laser and the
    /// panel's normal: 7 px at 60 degrees, 17 px at 77. That walk points AWAY from wherever the
    /// player is aiming from — pointing up from below walks it UP.</para>
    ///
    /// <para><b>AND THAT IS WHY ONLY THIS PANEL.</b> The plate is seated
    /// <see cref="InsetPx"/> = 7 px below the window's own top edge. A floated modal usually seats
    /// it against the INK instead, well inside the frame, so the walk lands the crossing point on
    /// bare window and the beam still CLAMPS — the player sees an X that did not react. The combat
    /// log deliberately uses the FRAME path (<c>CombatLogSurface.SyncCloseX</c>: its host is pinned
    /// at the game's full window layout, so "the frame IS the picture"), which puts the plate at the
    /// very corner with those 7 px of margin and nothing above it. Past roughly 60 degrees the
    /// crossing point leaves the host's hit rect ALTOGETHER, <c>TryIntersect</c> returns false, this
    /// canvas never becomes the winner and the beam does not stop at all — which is exactly the
    /// symptom reported, down to the word <i>kollidiert</i>. The combat log is also the panel that
    /// lives low, at the board, so an oblique aim is the normal way to point at it.</para>
    ///
    /// <para><b>WHY REMOVING THE NUDGE IS SAFE, and why it is a leftover rather than a trade.</b>
    /// The nudge's own doc already said it: <i>"the nudge alone does NOT decide the draw — see
    /// XOrderOffset for what does now, and for why the depth stamp that used to is gone."</i> The X
    /// rides its own <c>overrideSorting</c> canvas on the panel's ladder at
    /// <see cref="XOrderOffset"/>; two canvases at different sorting orders are drawn in that order
    /// and never z-fight, so a coplanar plate is drawn over the window's content exactly as a lifted
    /// one was. The POKE path is unaffected either way — it projects the fingertip ORTHOGONALLY onto
    /// the canvas plane, so it never had this parallax and its depth test is against the host plane,
    /// not the plate. What changes is only the far ray, and only in the direction of being exact:
    /// with the plate on the host plane the crossing point IS the aim point at every angle.</para>
    ///
    /// <para>The constant is deleted rather than set to zero: a frozen literal with no consumer is
    /// a thing the next round has to re-derive. The number was 4 px.</para>
    /// </summary>
    private const float InsetPx = 7f;

    /// <summary>
    /// MP round 2 root cause ("Kontrolle uebergeben" STILL had no X) and the TRANSPARENCY ROUND
    /// answer to it. The blacklist flip DID attach the X to the transfer-control player picker - no
    /// attach failure in the log - but the X never became VISIBLE: its canvas sat at the SAME order
    /// as the window content, and Unity breaks an equal-order tie by camera distance measured to the
    /// CANVAS, not per pixel. The X canvas sits at the host's top-right CORNER, ~half a panel
    /// diagonal off-axis, so it measured FARTHER than the window canvas centre and the window's
    /// opaque backing painted over it. Every other X-carrying float ships with its backing stripped,
    /// which is why only the player picker showed the defect.
    ///
    /// <para>Round 2 fixed that with a colour-invisible DEPTH-WRITING quad behind the plate. That
    /// stamp is now GONE: it is a 34x34 px box of "everything behind this is deleted", so it cut a
    /// hard-edged hole into whatever panel happened to lie behind the corner of a window - the same
    /// defect, one element smaller, as the per-host and per-modal stamps this round removes. The X
    /// no longer needs it: its visible canvas is an ORDER FOLLOWER of its own panel
    /// (<see cref="XOrderOffset"/>), so it beats its own window's content by ORDER instead of by a
    /// distance tie, deterministically and with no depth written anywhere. And because the offset
    /// stays under <c>CanvasConversion.PanelOrderStep</c>, a genuinely NEARER panel still outranks
    /// it - the issue-#8 "the X must not pierce nearer panels" ruling is preserved by construction
    /// rather than by a ZTest.</para>
    /// </summary>
    private const int XOrderOffset = 2;
    // The "X" occupies the middle ~44 % of the plate — a compact glyph with clear margins.
    private const float BarLengthFraction = 0.44f;
    private const float BarThicknessPx = 3.5f;

    /// <summary>
    /// Issue #8 (X unclickable) ROOT CAUSE + FIX. The X plate lived on the HOST canvas at
    /// <c>ModalFallback.ModalHostSortingOrder = 1000</c> — the SAME sortingOrder the adopted
    /// game-window content reports (verified in the runtime log: the Options window's adopted
    /// canvas raycasts at <c>sortingOrder=1000</c>). <see cref="UguiPointer.Beats"/> awards a
    /// coplanar raycast TIE to the nested game content (<c>challenger.sortingOrder &gt;=
    /// incumbent.sortingOrder</c>), so wherever any game raycast target sat behind the X — a
    /// full-window frame image covers the whole menu — the game graphic won the hit and the X's
    /// own <see cref="Button"/> never fired (the log shows game widgets clicking fine but NOT a
    /// single "MODAL CLOSE (X button)" line all session). Fix: an elevated nested canvas at this
    /// order, registered as a nested surface of the host, so the poke/laser (<see
    /// cref="UguiPointer.TryRaycast"/> merges <see cref="UguiPokeSurfaces.NestedOf"/>) hit it and
    /// it WINS the tie (1100 &gt; 1000).
    ///
    /// <para>This order carries ONLY the invisible HitPlane, and it is deliberately a FIXED value,
    /// not a ladder follower. It is compared against the host's CONVERSION tier, which
    /// <c>CanvasConversion.BaseSortingOrderOf</c> keeps reporting to <c>UguiPointer.Beats</c>
    /// unchanged while the DRAW order moves with distance - so 1100 &gt; 1000 stays exactly the
    /// comparison the shipped builds made, and no raycast decision anywhere changed with the
    /// transparency round. The VISIBLE X is a separate canvas riding the ladder
    /// (<see cref="XOrderOffset"/>): raycast priority and draw priority genuinely need different
    /// orders here, hence two canvases.</para>
    /// </summary>
    private const int CloseButtonSortingOrder = 1100;

    // On-theme palette, sampled from the mod's own retired free-floating settings panel: muted
    // dark plate + brass glyph. Kept as literals because that panel is gone (the VR settings are
    // an options-window tab now, VROptionsTab) — there is no shared palette left to point at.
    private static readonly Color PlateColor = new(0.09f, 0.09f, 0.12f, 0.82f);  // dark, semi-transparent
    private static readonly Color GlyphColor = new(0.62f, 0.5f, 0.28f, 0.95f);   // brass grab-bar tone

    /// <summary>Build the X button on <paramref name="panel"/>'s host, closing <paramref name="window"/>.</summary>
    /// <param name="onClose">
    /// THE OWNER'S OWN CLOSE, or null for the game's escape/hide path — which is what every floated
    /// <see cref="ModalFallback"/> window passes and therefore what shipped.
    ///
    /// <para><b>WHY IT EXISTS (user, 2026-09-05, verbatim): <i>"der X Button ist anders"</i>.</b> The
    /// combat log is a converted panel with a host canvas exactly like a floated window, and it had
    /// grown its OWN X — a loud red 3D <c>PlayTray.BoardButton</c> reading the letter "X" — because
    /// this method's only close action was <c>UIWindow.Escape()</c>, and the combat log's exit is not
    /// an escape: it is that surface's session-visibility seam (<c>SetUserVisible(false, …)</c>),
    /// which releases the conversion back to its 2D home and remembers that the player asked for it.
    /// Escaping the game's window instead would close the game's combat log for the flat UI as well
    /// and leave the mod's own gate still asking for it.</para>
    ///
    /// <para>An <see cref="Action"/> parameter defaulting to today's behaviour is a strictly smaller
    /// change than a second X implementation, and it is the whole reason the second one could be
    /// deleted. The EXIT STILL ENDS IN A VISIBLE CONFIRMATION either way (standing ruling): the
    /// default path closes the window, and the combat log's path releases the panel on the very next
    /// tick, so the press is answered by the panel leaving.</para>
    /// </param>
    internal static void Attach(ConvertedPanel panel, UIWindow window, Action? onClose = null)
    {
        if (panel == null || panel.HostRect == null || panel.HostCanvas == null || window == null)
            return;
        // Match the host's layer (the mod layer after Convert's ApplyModLayer) so the head
        // camera draws it and the game UI Camera does not double-draw it.
        int layer = panel.HostGo != null ? panel.HostGo.layer : 5;
        UIWindow target = window;
        try
        {
            Action? ownerClose = onClose;
            Build(panel, panel.HostRect, panel.HostCanvas, layer, () =>
            {
                if (ownerClose != null)
                {
                    // The owner's own exit. A SEPARATE line rather than the one below with a word
                    // swapped, because the line below asserts an Escape/Hide that did not happen here
                    // and a falsifier that greps for it must not be answered by a press that took a
                    // different path.
                    // HW-VERIFY
                    VRLog.Note("WorldUI", $"MODAL CLOSE (X button): PRESSED for '{target.name}' (ID {target.ID}) " +
                                          "— running the OWNER'S OWN close action instead of Escape/Hide, " +
                                          "so the surface that put this panel up stays the one authority " +
                                          "on whether it is up.");
                    ownerClose();
                    return;
                }
                // Diagnostic (issue #8): prove the click reached the X and WHICH window it targets —
                // distinguishes "click never hit the X" (no line) from "close failed" (this line, then
                // CloseFloatedWindow's own result line). The two together are the full X-close trace.
                // HW-VERIFY
                VRLog.Note("WorldUI", $"MODAL CLOSE (X button): PRESSED for '{target.name}' (ID {target.ID}) " +
                                      "— closing exactly this window (Escape/Hide), other open windows untouched.");
                ModalFallback.CloseFloatedWindow(target);
            });
            // Attach evidence (MP round 2 lesson): the X built silently, so "no X visible" could
            // not be told apart from "no X attached" in the hardware log. One line per attach.
            // HW-VERIFY
            VRLog.Note("WorldUI", $"MODAL CLOSE (X button): attached to '{window.name}' (ID {window.ID}) " +
                                  "- top-right plate riding the panel's draw order +2 (over the window's " +
                                  "own backing, still under any panel that is genuinely nearer). It is " +
                                  "built at the FRAME's top-right corner and re-seated against the ink's " +
                                  "from GrabbableModal's follow tick as soon as a union exists — the " +
                                  "MODAL CLOSE X ON THE INK line for this window says which of the two " +
                                  "is deciding it right now. THE PLATE IS COPLANAR WITH ITS WINDOW " +
                                  "(ModBuild 439): the 4 px viewer nudge is retired, because the far " +
                                  "ray judges every nested canvas at ONE screen point taken on the " +
                                  "HOST plane, so any depth on this plate became a lateral error of " +
                                  "depth x tan(incidence) — 7 px at 60 degrees, which is the whole " +
                                  "margin between this plate and the frame's top edge on a panel " +
                                  "seated by the FRAME path. THIS PAIR OF LINES IS THE FALSIFIER: " +
                                  "an attach line with no PRESSED line for the same window after a " +
                                  "session of trying means the beam never reached the plate, and a " +
                                  "PRESSED line that is not followed by the window leaving means it " +
                                  "reached it and the close failed.");
        }
        catch (Exception ex)
        {
            VRLog.Warn("WorldUI", $"MODAL CLOSE (X button): could not build the X for '{window.name}' " +
                                  $"({ex.GetType().Name}: {ex.Message}) — the escape chord still closes it.");
        }
    }

    /// <summary>
    /// WHERE THE PLATE ENDED UP, so the falsifier can state it without re-deriving it from the same
    /// terms that produced it (<c>[[a-claim-must-not-measure-itself]]</c>).
    /// </summary>
    internal struct XPlacement
    {
        /// <summary>Host-local uGUI px of the plate's TOP-RIGHT corner — its pivot.</summary>
        internal Vector2 Corner;
        /// <summary>The ink moved the plate off the frame's own corner on at least one axis.</summary>
        internal bool OnInk;
        /// <summary>The ink reached (or passed) the frame's right edge, so the frame decided x.</summary>
        internal bool ClampedRight;
        /// <summary>The ink reached (or passed) the frame's top edge, so the frame decided y.</summary>
        internal bool ClampedTop;
    }

    /// <summary>
    /// Seat <paramref name="rect"/>'s top-right corner against the window's INK when there is one,
    /// and against its FRAME when there is not. Idempotent and allocation-free: the caller may run it
    /// every tick, and it only writes when the corner actually moved.
    ///
    /// <para>THE FRAME PATH IS THE SHIPPED PLACEMENT, EXACTLY. <c>(xMax - InsetPx, yMax - InsetPx)</c>
    /// is what anchor (1,1) + <c>anchoredPosition (-InsetPx,-InsetPx)</c> resolved to, so a window
    /// with no measured ink — and every window whose ink fills its frame, through the clamp — is
    /// numerically unchanged. That is what makes an unchanged reading on those windows evidence.</para>
    ///
    /// <para>THE UPWARD CLAMP IS NOT COSMETIC. An ink union may reach far ABOVE the frame: the same
    /// log's options window measures <c>y -540..1287</c> against a frame that ends at y=540, because
    /// a <c>Container/Pointer</c> is drawn 747 px above it. Seating the X at <c>ink.yMax</c> would
    /// hang it 747 px over the window's own top, off in the room. The rule is the mirror of the brass
    /// bar's ("never raised above the frame's own bottom edge, whatever the ink says",
    /// <c>GrabbableModal</c>): the X is never pushed above the frame's own top edge. The lower clamp
    /// is a survival floor for a degenerate ink — the plate must stay inside the frame, because it is
    /// only reachable through a laser hit on the host plane (its canvas is a NESTED poke surface).</para>
    /// </summary>
    internal static XPlacement PlaceAgainstInk(RectTransform rect, Rect hostRect, bool inkValid,
        Rect ink)
    {
        var placement = default(XPlacement);
        float frameX = hostRect.xMax - InsetPx;
        float frameY = hostRect.yMax - InsetPx;
        float cornerX = frameX;
        float cornerY = frameY;
        if (inkValid && ink.width > 0f && ink.height > 0f)
        {
            // THE CORNER. `ink.xMax + gap + width` puts the plate's LEFT edge one gap clear of the
            // ink's RIGHT edge; `ink.yMax` levels its top with the ink's top. Flipping this to the
            // ink's top-LEFT is `ink.xMin - InkGapPx` here and nothing else (see the class comment's
            // ambiguity note).
            float wantX = ink.xMax + InkGapPx + ButtonSizePx;
            float wantY = ink.yMax;
            placement.ClampedRight = wantX >= frameX;
            placement.ClampedTop = wantY >= frameY;
            cornerX = Mathf.Clamp(wantX, hostRect.xMin + ButtonSizePx, frameX);
            cornerY = Mathf.Clamp(wantY, hostRect.yMin + ButtonSizePx, frameY);
            placement.OnInk = !placement.ClampedRight || !placement.ClampedTop;
        }
        placement.Corner = new Vector2(cornerX, cornerY);

        // Anchored to the host rect's CENTRE rather than its top-right corner, because the corner is
        // now a computed point and not a corner. anchoredPosition is a Vector2 write: it leaves
        // anchoredPosition3D.z alone, so whatever local z Build left on the plate survives every
        // re-seat — that is ZERO since ModBuild 439, see the retired viewer nudge.
        var anchor = new Vector2(0.5f, 0.5f);
        Vector2 wanted = placement.Corner - hostRect.center;
        if (rect.anchorMin != anchor || rect.anchorMax != anchor)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
        }
        if ((rect.anchoredPosition - wanted).sqrMagnitude > 0.0001f)
            rect.anchoredPosition = wanted;
        return placement;
    }

    /// <summary>The plate on <paramref name="panel"/>'s host, or null when this window has none (the
    /// scenario-end windows are deliberately excluded, and a window converted before
    /// <see cref="Attach"/> ran has not built one yet). A direct-child lookup on a host with a
    /// handful of children — never a scene sweep.</summary>
    internal static RectTransform? FindPlate(ConvertedPanel? panel)
    {
        if (panel == null || panel.HostRect == null)
            return null;
        Transform? t = panel.HostRect.Find(ObjectName);
        return t as RectTransform;
    }

    private static void Build(ConvertedPanel panel, RectTransform host, Canvas hostCanvas, int layer,
        Action onClose)
    {
        var go = new GameObject(ObjectName) { layer = layer };
        // THE OWNERSHIP MARKER, AND IT REPLACES A CONVENTION THAT FAILED TWICE (2026-09-04, round 2).
        // CanvasConversion's host-destroy guard has to know whether an object under a float host is
        // the mod's or the game's. Until now the only answer was the `GloomhavenVR.` name prefix, and
        // the ModBuild 418 log lost that bet on the plate's own `HitPlane` while the ModBuild 419 log
        // lost it again, 24 times, on this plate's `XBar` children — created eight lines below in
        // THIS file, by the author who had just fixed the sibling. One mark on the SUBTREE ROOT
        // answers for the plate, its hit target, its two bars and anything a later round adds under
        // them; sealed (MayHoldGameContent: false) because nothing of the game's is ever parented
        // here. See ModOwnedContent for the whole argument.
        ModOwnedContent.Mark(go);
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(host, worldPositionStays: false);
        rect.pivot = new Vector2(1f, 1f);
        rect.sizeDelta = new Vector2(ButtonSizePx, ButtonSizePx);
        // The FRAME placement, byte-for-byte what anchor (1,1) + anchoredPosition (-7,-7) resolved
        // to. GrabbableModal re-seats it against the ink from its follow tick once a union exists;
        // going through the same method here means the two can never drift apart.
        PlaceAgainstInk(rect, host.rect, inkValid: false, ink: default);
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

        var img = go.AddComponent<Image>();
        img.color = PlateColor; // muted dark board-styled plate
        img.raycastTarget = false; // clicks land on the invisible HitPlane below, not the visuals

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        // Multiplies the plate colour: dim at rest, a touch brighter on hover, dimmer while pressed
        // — a subtle affordance, no loud colour flash.
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = new Color(1.05f, 1.05f, 1.05f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        // ONE PIECE OF FEEL, NOT THREE LITERALS (ModBuild 439, survey row R20). This was a
        // hand-written 0.08f, and so were VROptionsTab's rows and VariantTiles' tiles — three files
        // that must answer a hover at the same rate or the close X reads as a different kind of
        // object from the row beside it. The constant lives in a neutral place both may reference:
        // the options menu is a CONSUMER of this feel and must not own it.
        colors.fadeDuration = UguiTintFeel.HoverTintFadeSeconds;
        button.colors = colors;
        button.onClick.AddListener(() =>
        {
            try { onClose(); }
            catch (Exception ex) { VRLog.Error("WorldUI", $"Modal X button action threw: {ex}"); }
        });

        // DRAW and RAYCAST split onto two canvases, because they need DIFFERENT orders.
        //
        // The VISUALS ride the owning panel's distance-derived draw order at a small offset, so the
        // X is unambiguously over its own window and unambiguously under any panel that is
        // genuinely nearer than that window (see XOrderOffset). The plate is COPLANAR with the
        // window: the 4 px viewer nudge it used to carry is what made the X unhittable from a
        // shallow angle, and ORDER is what decides the draw. The whole derivation is on the retired
        // ViewerNudgePx doc above; it is the answer to "von unten kollidiert der Laser nicht".
        var xCanvas = go.AddComponent<Canvas>();
        xCanvas.overrideSorting = true;
        // Seed only; RegisterOrderFollower below puts it on the panel's live ladder order + offset.
        xCanvas.sortingOrder = panel.DrawSortingOrder + XOrderOffset;
        if (hostCanvas != null)
            xCanvas.worldCamera = hostCanvas.worldCamera; // match the host's event camera (adoption pattern)

        // NO DEPTH WRITE HERE ANY MORE. The plate's local z stays the 0 set above, which is what
        // makes the far ray's pick exact: RayUguiDriver takes ONE screen point, on the HOST plane,
        // and raycasts every nested canvas at it — so any depth this object carries becomes a
        // lateral error of `depth x tan(angle of incidence)` in what the beam is judged to have hit.
        // The head-camera sign test that used to pick the nudge's DIRECTION went with it: there is
        // no side to be on when the offset is zero.

        // Beat the window's own coplanar content by ORDER, not by a depth stamp and not by a
        // distance tie: the X canvas follows its panel's ladder order at XOrderOffset (see that
        // constant). Registered here so the very first frame is already ordered.
        if (panel != null)
            CanvasConversion.RegisterOrderFollower(panel, xCanvas, XOrderOffset);

        // The RAYCAST keeps the elevated order (issue #8), on an INVISIBLE plate: Beats()
        // compares sortingLayer then sortingOrder and never distance, so this is what lets the X
        // win the cross-raycaster tie against the adopted game content behind it — WITHOUT also
        // winning the draw against a nearer info panel. Registered as a nested surface exactly
        // like before; released with the host, so no teardown of its own.
        // THE `GloomhavenVR.` PREFIX IS LOAD-BEARING, NOT DECORATION (2026-09-04). It is the ONLY
        // thing that tells CanvasConversion's FindGameContent whether an object under a float host
        // belongs to the mod or to the game: the walk descends through mod-owned nodes and stops at
        // the first name without the prefix, calling it the game's. This plate was named bare
        // "HitPlane", so every single window close read "the float host still holds the GAME object
        // 'HitPlane'", deferred the host destroy, and then freed the host to the scene root — one
        // leaked GameObject per close, and the HOST DESTROY DEFERRED warning on every close in every
        // hardware log. Nothing looks this object up by name; the prefix is the whole fix.
        var hitGo = new GameObject("GloomhavenVR.HitPlane") { layer = layer };
        var hitRect = hitGo.AddComponent<RectTransform>();
        hitRect.SetParent(rect, worldPositionStays: false);
        hitRect.anchorMin = Vector2.zero;
        hitRect.anchorMax = Vector2.one;
        hitRect.offsetMin = Vector2.zero;
        hitRect.offsetMax = Vector2.zero;
        hitRect.localScale = Vector3.one;
        hitRect.localRotation = Quaternion.identity;
        hitRect.localPosition = new Vector3(hitRect.localPosition.x, hitRect.localPosition.y, 0f);
        var hitImg = hitGo.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0f); // invisible; Graphic raycasting ignores alpha
        hitImg.raycastTarget = true;
        var hitCanvas = hitGo.AddComponent<Canvas>();
        hitCanvas.overrideSorting = true;
        hitCanvas.sortingOrder = CloseButtonSortingOrder;
        if (hostCanvas != null)
            hitCanvas.worldCamera = hostCanvas.worldCamera;
        hitGo.AddComponent<GraphicRaycaster>();
        if (hostCanvas != null)
            UguiPokeSurfaces.RegisterNested(hostCanvas, hitCanvas);

        // Two crossed bars form the "X" (font-free → always visible). Children of the plate → they
        // draw on the X's own canvas, on top of the menu, with the plate.
        CrossBar(rect, layer, 45f);
        CrossBar(rect, layer, -45f);
    }

    private static void CrossBar(RectTransform parent, int layer, float angleDeg)
    {
        // THE PREFIX IS THE FALLBACK ANSWER TO THE OWNERSHIP QUESTION, AND IT IS RESTORED HERE
        // (2026-09-04, round 2). This object was named a bare "XBar", so CanvasConversion's
        // FindGameContent read it as the GAME's and the ModBuild 419 hardware log carries
        // "HOST DESTROY DEFERRED (release): … still holds the GAME object 'XBar'" 24 times — once
        // per window close, each one deferring a host destroy and freeing a mod bar to the scene
        // root. The real fix is the ModOwnedContent marker on the plate above, which covers this
        // object whether or not its name is ever right again; the prefix is kept in step with it so
        // the two answers can never disagree. Nothing looks this object up by name.
        var go = new GameObject("GloomhavenVR.XBar") { layer = layer };
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ButtonSizePx * BarLengthFraction, BarThicknessPx);
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
        // Item 4 (centering fix): anchor (0.5,0.5) + anchoredPosition zero already puts the bar's
        // pivot at the PLATE CENTER. The old `localPosition = (0,0,0)` here OVERRODE that and snapped
        // the bar to the parent's LOCAL ORIGIN — which is the plate's PIVOT corner (top-right, the
        // plate pivots at (1,1)), NOT its center — so the whole X sat offset up-and-right (the
        // reported "still not centered"). Only normalize z; keep the centered x/y from the anchor.
        rect.localPosition = new Vector3(rect.localPosition.x, rect.localPosition.y, 0f);

        var img = go.AddComponent<Image>();
        img.color = GlyphColor; // brass "X"
        img.raycastTarget = false; // pokes/laser hit the plate, not the glyph
    }
}
