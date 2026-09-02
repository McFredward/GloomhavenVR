using System.Collections.Generic;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using GloomhavenVR.Hands.Interact;
using MapRuleLibrary.Adventure;
using MapRuleLibrary.MapState;
using MapRuleLibrary.Party;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Shared implementation for scenario panels that live in a fixed
/// <see cref="PanelSlot"/> of the curved table layout.
/// </summary>
internal abstract class SlotPanelSurface : WorldSurface
{
    protected abstract PanelSlot Slot { get; }

    protected override void Place()
    {
        if (Panel == null || !PanelLayout.TryGetPose(Slot, out Vector3 pos, out Quaternion rot))
            return;
        CanvasConversion.PlaceHost(Panel, pos, rot, PanelLayout.WorldScale);
    }
}

/// <summary>
/// Test #15 dashboard seam: a converted panel DOCKED onto a <see cref="PlayTray"/>
/// mount anchor. The host POSE-FOLLOWS the mount every tick (position/rotation/
/// scale copied — it is never re-parented under the tray: the host carries live
/// GAME UI, and a tray/rig teardown must never cascade into destroying game-owned
/// canvases). Sized by the SHARED tray density (test #16): the panel's world size
/// follows its content pixel size at <see cref="PlayTray.TrayPixelsPerMeter"/>,
/// scaled DOWN (never up) when it would overflow its dock area — so text and
/// portraits stay consistent across all docked panels instead of each panel being
/// stretched to fill its dock. Everything is in tray-local meters × the mount's
/// lossy scale, so it survives tray grab/move/resize and diorama scaling for
/// free. While the tray is hidden the host hides with it; when no mount exists at
/// all (Cards module off, tray destroyed) the panel falls back to the old
/// floating slot layout.
/// </summary>
internal abstract class TrayMountedPanelSurface : SlotPanelSurface
{
    /// <summary>
    /// Density-scale guards (test #16). Max 1: a SMALL host (the 100 px objectives
    /// placeholder) must keep its content-true size, not balloon to fill the dock —
    /// that was the giant objectives text. Min 0.5: if the content measurement ever
    /// misfires huge again (fullscreen junk in the union), the panel overflows its
    /// dock somewhat instead of shrinking below readability — the tiny initiative
    /// portraits were worse than a slightly oversized panel.
    /// </summary>
    private const float MaxDensityScale = 1f;
    private const float MinDensityScale = 0.5f;

    /// <summary>
    /// Per-panel multiplier on the shared tray density (test #17). 1 = the shared
    /// <see cref="PlayTray.TrayPixelsPerMeter"/> as-is; below 1 renders the SAME
    /// content pixels onto MORE tray meters (0.6 ⇒ ~1.67× bigger text). The dock
    /// fit clamp below still applies, so a lowered density never overflows the
    /// panel's dock budget by more than the shared MinDensityScale allowance.
    /// </summary>
    protected virtual float DensityScale => 1f;

    /// <summary>
    /// Does the mount WIDTH budget take part in the uniform dock fit?
    ///
    /// ROOT CAUSE this hook exists for (user: "Breite und Größe scheinen sich gleich zu verhalten"):
    /// the fit below is UNIFORM — one metres-per-pixel for both axes — and its width term is
    /// <c>MountWidth · density / contentWidth</c>. For a panel whose content is simply MEASURED that
    /// is right: content and budget are independent, so the term only ever shrinks an oversized
    /// panel. But for a panel that FORCES its content to the very same budget
    /// (<see cref="ObjectivesSurface.ApplyContentWidth"/> writes <c>wantPx = MountWidth · density</c>
    /// onto the objectives container root) the two sides of that fraction move TOGETHER, and what is
    /// left over is the CONSTANT overhang <c>c</c> that the measured graphics union carries past the
    /// forced column (the quest header's Background — 94 px in the hardware log: forced 374 px,
    /// settled 468 px). The term then reads
    /// <code>fitScale = wantPx / (wantPx + c) = 1 / (1 + c / (MountWidth · density))</code>
    /// which RISES towards 1 as the width budget grows. Hardware proof (Player.log, the 0.8 → 0.9
    /// step of the 'Breite' dial): 300/356 = 0.84 → 337/356 = 0.95, host lossyScale 0.0122 → 0.0137.
    /// One click of a WIDTH dial made every glyph 12 % BIGGER — i.e. 'Breite' behaved like 'Größe',
    /// and at the low end the same fraction shrank the text back ("so gestaucht wie zuvor").
    ///
    /// Overriding this to false drops the width term: the panel's metres-per-pixel is then the panel
    /// density alone (its <see cref="DensityScale"/> × the shared tray density) with only the HEIGHT
    /// budget left as an overflow guard, so the forced content width can move the WRAP COLUMN — and
    /// with it the panel's horizontal extent — while the glyph size stays exactly put. Correct by
    /// construction: a panel that forces its content to the width budget already satisfies that
    /// budget, so re-checking it can only mis-measure the constant overhang.
    /// </summary>
    protected virtual bool FitWidthToMount => true;

    /// <summary>Live mount anchor (null/destroyed → floating fallback).</summary>
    protected abstract Transform? Mount { get; }

    /// <summary>Target panel width, tray-local meters.</summary>
    protected abstract float MountWidth { get; }

    /// <summary>Max panel height, tray-local meters (caps the scale for tall content).</summary>
    protected abstract float MountMaxHeight { get; }

    /// <summary>Which way the panel extends from the mount origin (unit XY in mount space).</summary>
    protected abstract Vector2 GrowDirection { get; }

    /// <summary>
    /// HIDING IS DEBOUNCED, SHOWING IS NOT — how many CONSECUTIVE frames the mount may read
    /// invisible before a docked panel actually hides.
    ///
    /// <para>THE REPORT (user, hardware, ModBuild 106, verbatim): "Der Text der persönlichen Quest
    /// flackert immer mal wieder auf (verschwindet für ein frame und taucht dann sofort wieder
    /// auf)." Two mechanisms could produce a ONE-FRAME blank on that label and both are real; the
    /// draw-order one is answered in <see cref="ObjectivesSurface.RankQuestLabel"/>, and THIS is the
    /// other. The quest label's own visibility gate is <c>Panel.HostGo.activeInHierarchy</c>
    /// (<see cref="ObjectivesSurface.TickQuestLabel"/>), and this method is what writes it — from a
    /// single unfiltered read of <c>mount.gameObject.activeInHierarchy</c>. One frame in which that
    /// read is false takes the whole docked panel AND the label hanging off it down and back up.
    /// Both are neutralised in the same build ON PURPOSE: a hardware test costs the user a game
    /// launch and headset time, and a run that only tells us which of two hypotheses was right
    /// burns it (his standing instruction: "Versuch selber zu evaluieren, dass du mir immer nur
    /// solche Tests gibst von denen du dir sicher bist, dass du das Problem gelöst hast").</para>
    ///
    /// <para>THAT ONE-FRAME READ IS STRUCTURALLY REACHABLE, from source: the mount's
    /// <c>activeInHierarchy</c> is governed by the tray ROOT (<c>PlayTray.SetVisible</c>:
    /// <c>show = visible &amp;&amp; _placed</c>), and both of its terms can drop for a moment.
    /// <c>_placed</c> is cleared and re-earned by the placement deferral and by the watchdog
    /// rebuild (<c>PlayTray.2.Watchdog</c> ends with "a recovery must never leave the board
    /// hidden", i.e. it can find it hidden); <c>visible</c> comes from the CardsDriver rebuild,
    /// whose no-active-hand branch hides the tray when <c>CardsGameApi.InScenario</c> reads false —
    /// and a lost hand binding is the SAME pooled-<c>CardsHandUI</c> re-bind window ModBuild 105
    /// already had to defend the goal text against. I did NOT catch this firing in the hardware
    /// log — there is no line that would have shown it — so it is a reachable path, not an observed
    /// one. The debounce closes it whichever of those inputs blinked, which is the point of fixing
    /// it structurally rather than chasing the blink.</para>
    ///
    /// <para>WHY 2. The observed defect is ONE frame, so a grace of 1 would close exactly the
    /// reported case with no margin at all — and a two-frame dropout (two ticks landing inside one
    /// gap, a hitch, a double-buffered activation) would sail straight through it. 2 buys a frame
    /// of margin over the only case we have evidence for while the TOTAL hide latency stays at 3
    /// frames ≈ 33 ms at 90 Hz, comfortably under the ~67 ms this project already established as
    /// "under the threshold where a delay reads as lag" (the figure-grab dwell, 76daf29). Anything
    /// larger would start to be a policy about teardown timing rather than noise rejection.</para>
    ///
    /// <para>THE SHOW PATH IS UNTOUCHED, deliberately: <c>mountVisible</c> forces the panel visible
    /// in the same frame, with no grace on the way in. Appearing late is its own defect on this
    /// project ("everything that fades or moves does so WITH the animation") and a debounce that
    /// worked in both directions would have traded one pop for another.</para>
    ///
    /// <para>THIS IS A SHARED BASE PATH — it applies to every <see cref="TrayMountedPanelSurface"/>,
    /// i.e. the three control-board docks: <see cref="InitiativeTrackSurface"/>,
    /// <see cref="ElementBoardSurface"/> and <see cref="ObjectivesSurface"/>. Harmless for all
    /// three, and the reasoning is the same for each: (1) the hide is a bare <c>SetActive</c> with
    /// no animation and no game-side side effect — these hosts CARRY game canvases but the game's
    /// own widgets are never touched by it — so two extra frames change nothing but how long a
    /// static panel is on screen; (2) a genuine teardown lasts far longer than 33 ms, so nothing
    /// that should disappear stays up perceptibly; (3) the DESTRUCTIVE case does not come through
    /// here at all — a destroyed mount is Unity-null and takes the floating-fallback branch above,
    /// so scenario exit and board teardown are unaffected by this timer; (4) the MR backing plates
    /// follow <c>panel.HostGo.activeInHierarchy</c> in <c>MrBacking.TickPanels</c>, so plate and
    /// panel stay in lockstep for the same two frames and no plate is left standing alone.</para>
    ///
    /// <para>THE ONE CASE I CANNOT RULE OUT FROM SOURCE, stated rather than smoothed over: if some
    /// caller ever toggled the tray's visibility ON AND OFF every other frame, this grace would
    /// hold the panels permanently visible instead of letting them strobe. I found no such caller —
    /// every writer traced above is an event-driven state change, not a per-frame gate — and a
    /// strobing tray would be a defect in its own right. But that is an argument from the callers I
    /// read, not a guarantee the type can make about itself.</para>
    /// </summary>
    private const int MountHideGraceFrames = 2;

    /// <summary>Last <see cref="Time.frameCount"/> at which the mount read visible; 0 = never seen
    /// visible, which must hide immediately (a panel that has never been shown has nothing to
    /// protect). See <see cref="MountHideGraceFrames"/>.</summary>
    private int _mountVisibleFrame;

    protected override void Place()
    {
        if (Panel == null)
            return;

        Transform? mount = Mount; // Unity-null aware: destroyed mounts fall through
        if (mount == null)
        {
            _dockLoggedMountId = 0; // undocked — the next dock logs its rect again
            Panel.OrderCluster = null; // floating — distance arbitrates against everything again
            if (!Panel.HostGo.activeSelf)
                Panel.HostGo.SetActive(true);
            base.Place(); // old floating layout (fallback per the mount-seam contract)
            return;
        }

        // Docked on the control board: join the board's draw-order cluster, so the board's
        // transparent furniture (the Statustafel placard, labels, glows) stays STRUCTURALLY
        // below this panel instead of letting two coplanar distance measures flip with the
        // viewing angle — see ConvertedPanel.OrderCluster.
        Panel.OrderCluster = PlayTray.Current;

        // Tray hidden (out-of-scenario transitions, hands down) → panel hides too. SHOWING IS
        // IMMEDIATE, HIDING IS EARNED — see MountHideGraceFrames for the report this asymmetry
        // answers and why it is safe for every panel that mounts here.
        bool mountVisible = mount.gameObject.activeInHierarchy;
        if (mountVisible)
            _mountVisibleFrame = Time.frameCount;
        bool visible = mountVisible
                       || (_mountVisibleFrame != 0
                           && Time.frameCount - _mountVisibleFrame <= MountHideGraceFrames);
        if (Panel.HostGo.activeSelf != visible)
            Panel.HostGo.SetActive(visible);
        if (!visible)
            return;

        Rect rect = Panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width < 1f || rect.height < 1f)
            return;

        // UNIFORM density (test #16): tray-local meters per uGUI pixel is the SAME
        // for every docked panel (1/TrayPixelsPerMeter), clamped down only when the
        // content would overflow this panel's dock budget. The old per-panel
        // dock-FIT scale made effective densities differ 17× between panels. The
        // mount's lossy scale carries BOTH the tray-grab scale and the diorama
        // scale, so the density holds at tray scale 1 and multiplies uniformly.
        float trayScale = mount.lossyScale.x;
        float density = PlayTray.TrayPixelsPerMeter * DensityScale;
        // The WIDTH term is skipped for panels that force their content to the width budget
        // themselves (see FitWidthToMount) — including it would make their width dial drag the
        // glyph scale along. Everything else fits both axes exactly as before.
        float heightFit = MountMaxHeight * density / rect.height;
        float fitScale = FitWidthToMount
            ? Mathf.Min(MountWidth * density / rect.width, heightFit)
            : heightFit;
        fitScale = Mathf.Clamp(fitScale, MinDensityScale, MaxDensityScale);
        float metersPerPx = fitScale / density;
        AppliedFitScale = fitScale;
        AppliedMetersPerPixel = metersPerPx;

        Vector2 grow = GrowDirection;
        Vector3 offset = new Vector3(
            grow.x * rect.width * metersPerPx * 0.5f,
            grow.y * rect.height * metersPerPx * 0.5f,
            0f) * trayScale;

        Transform host = Panel.HostTransform;
        host.SetPositionAndRotation(mount.position + mount.rotation * offset, mount.rotation);
        host.localScale = Vector3.one * (metersPerPx * trayScale);

        LogDockedRect(mount);
    }

    /// <summary>
    /// RE-PLACE at the end of the frame so a docked panel is as rigid on the board as the card
    /// piles are (user, hardware MP test: "Die Initiativreihenfolge über dem board und der
    /// Aufgabentext links ziehen immer ein wenig nach wenn man das board hin und her schleudert.
    /// Rechts die piles sind zB wie angewurzelt - das soll auch so sein ... allgemein bei allen
    /// Elementen die an dem Controllboard dran sind").
    ///
    /// ROOT CAUSE: pure FRAME ORDERING — see <see cref="WorldSurface.LateTick"/> for the full
    /// derivation. <see cref="Place"/> copies <c>mount.position/rotation/lossyScale</c>, and the
    /// board's carry writer (<c>PanelGrabHandle.Update</c>) is an ordinary MonoBehaviour
    /// <c>Update</c> with no execution-order relation to <c>WorldUIModule.Update</c>. Whenever it
    /// runs later in the frame than the surface tick, the panel renders at LAST frame's board
    /// pose. The piles have no such problem because they are children of the tray root.
    ///
    /// WHY NOT PARENT THE HOST TO THE MOUNT (the other candidate fix, and the more rigid one by
    /// construction): the host is not a mod-owned decoration — it CARRIES LIVE GAME UI. The
    /// initiative track, the objectives container and the element board are the game's own
    /// canvases, re-parented into the host by <see cref="CanvasConversion"/> and handed back
    /// verbatim by <c>Release</c>. Making the host a child of the tray would make its lifetime a
    /// child of the tray's: <c>PlayTray.Destroy</c> does <c>Object.DestroyImmediate(_root)</c>
    /// (scenario exit, board switch, module teardown), and Unity destroys the whole subtree —
    /// which would take the game's HUD canvases with it, permanently, with no way to restore
    /// them. That is exactly the cascade the mount-seam contract was written to prevent (see this
    /// class's summary: "it is never re-parented under the tray"). The peers' MIRRORS may and do
    /// parent (<c>RemoteWidgetMirror</c> hosts are mod-drawn copies under the peer board root,
    /// which is why a remote board is already rigid) — the local seam cannot, because the content
    /// is not ours. Fixing the ORDERING keeps the ownership contract and buys the same rigidity.
    ///
    /// Idempotent by construction: <see cref="Place"/> derives the pose from the mount and the
    /// (fitted) host rect only — it accumulates nothing, so running it a second time in the same
    /// frame just overwrites the Update-phase result with the same-or-fresher one. The Update
    /// pass is KEPT so that everything reading the host pose during Update (the objectives quest
    /// label, MR backing plates, the ray/poke plane) still sees a placed panel on the very frame
    /// a panel converts. Mount gone (floating fallback), tray hidden, board re-created on scenario
    /// load and a board rescale all flow through the same <see cref="Place"/> — this pass adds no
    /// state and therefore no new failure mode, and it NEVER writes the board itself: it only ever
    /// reads <c>mount</c> and writes the panel host (the "das Board darf sich niemals von selbst
    /// bewegen" — "the board may never move by itself" — invariant is untouched).
    /// </summary>
    public override void LateTick()
    {
        if (Panel != null)
            Place();
    }

    /// <summary>
    /// Metres per uGUI pixel applied by the last <see cref="Place"/> — the panel's GLYPH SCALE
    /// before the mount's own scale (which is where a 'Größe' dial lives). Exposed because a
    /// width-vs-size complaint can only be settled by seeing this number NOT move while the
    /// width does; the surfaces log it next to their width numbers.
    /// </summary>
    protected float AppliedMetersPerPixel { get; private set; }

    /// <summary>
    /// The fit factor that produced <see cref="AppliedMetersPerPixel"/>: 1 = pure panel density
    /// (nothing shrunk), below 1 = the content was scaled down to stay inside the mount budget.
    /// </summary>
    protected float AppliedFitScale { get; private set; }

    // ---- dock world-rect diagnostics (test #19 item 3) ---------------------------------
    private static readonly Vector3[] DockCornerScratch = new Vector3[4];
    private int _dockLoggedMountId;
    private Vector2 _dockLoggedWorldSize;

    /// <summary>
    /// Test #19 item 3: log the world rect the docked host canvas — the exact plane
    /// the laser/poke intersect (RayUguiDriver.TryIntersect uses the same corners) —
    /// actually spans: once per (re-)dock and once more per material size change, so
    /// a future "the laser misses the panel" is diagnosable from the log alone
    /// (compare the logged rect against where the content visibly sits on the tray).
    /// Info level on purpose: BepInEx's default disk config drops Debug entirely
    /// (test #19: zero 'Ray-uGUI canvas' lines in LogOutput.log while laser clicks
    /// demonstrably ran), and a diagnostic that never reaches the hardware log
    /// diagnoses nothing. Change-deduped on mount identity + world size (2 % + 1 mm):
    /// tray grabs and diorama rescales move the panel every tick, so POSITION is
    /// logged but never re-triggers — a handful of lines per session.
    /// </summary>
    private void LogDockedRect(Transform mount)
    {
        if (Panel == null)
            return;
        Panel.HostRect.GetWorldCorners(DockCornerScratch);
        float w = (DockCornerScratch[3] - DockCornerScratch[0]).magnitude;
        float h = (DockCornerScratch[1] - DockCornerScratch[0]).magnitude;
        int mountId = mount.GetInstanceID();
        if (mountId == _dockLoggedMountId
            && Mathf.Abs(w - _dockLoggedWorldSize.x) < _dockLoggedWorldSize.x * 0.02f + 0.001f
            && Mathf.Abs(h - _dockLoggedWorldSize.y) < _dockLoggedWorldSize.y * 0.02f + 0.001f)
            return;
        _dockLoggedMountId = mountId;
        _dockLoggedWorldSize = new Vector2(w, h);
        Rect px = Panel.HostRect.rect;
        // The glyph scale rides along (fit + mm per uGUI pixel): a "the dial changed the wrong
        // thing" report is only answerable if the log separates the panel's EXTENT from its
        // TEXT SIZE — see FitWidthToMount for the width-drags-scale root cause.
        VRLog.Info("WorldUI", $"Docked '{Panel.HostGo.name}' on '{mount.name}': " +
                              $"world rect {w:F3}x{h:F3} m ({px.width:F0}x{px.height:F0} px), " +
                              $"glyph scale {AppliedMetersPerPixel * 1000f:F4} mm/px " +
                              $"(fit {AppliedFitScale:F3}), " +
                              $"BL={DockCornerScratch[0]:F3} TR={DockCornerScratch[2]:F3}.");
    }
}

/// <summary>
/// The game's REAL initiative track, docked to the tray dashboard's TOP edge
/// (test #15; the free-floating world panel is gone — 'die Elemente fliegen
/// aktuell rum'). Target verified via ilspycmd (GH.Runtime.dll):
/// <c>public class InitiativeTrack : MonoBehaviour</c> with <c>public static
/// InitiativeTrack Instance { get; private set; }</c> — the component sits at the
/// track's uGUI root, so its own RectTransform is the conversion target. Avatars
/// stay fully interactive (initiative swap pokes go through the host
/// GraphicRaycaster). The vanilla not-locked-in display comes with it for free:
/// <c>InitiativeTrackActorAvatar</c> (decompiled GH.Runtime, ilspycmd) owns it —
/// <c>private const string Unknown = "?"</c>, <c>ViewDigitInitiative()</c> shows
/// <c>m_DigitViewer.ViewSpecialSymbol("?")</c> for initiative 0, and
/// <c>InitiativeTrackPlayerAvatar.CalculateInitiative</c> returns "?" for online
/// players who have not locked in (<c>!actor.IsUnderMyControl</c> during
/// <c>SelectAbilityCardsOrLongRest</c>).
///
/// PORTRAIT CLICKS SWITCH CHARACTERS (test #18). The host canvas registers with
/// <see cref="Hands.Interact.UguiPokeSurfaces"/> (Convert's pokeable default), so
/// the dominant laser (RayUguiDriver) and the fingertip poke (PokeInteractor)
/// synthesize real pointer events on it. The 2D click path (verified via ilspycmd,
/// decompiled GH.Runtime): each row's <c>ExtendedButton avatarButton</c>
/// (InitiativeTrackActorBehaviour.cs:14, a <c>Button</c> subclass) handles
/// <c>OnPointerClick</c> — gated by
/// <c>InteractabilityManager.ShouldAllowClickForExtendedButton</c>
/// (ExtendedButton.cs:168-173) — whose onClick reaches
/// <c>InitiativeTrackPlayerAvatar.OnClick</c> (<c>isSelectableByClick</c>: only
/// heroes the player controls) → <c>InitiativeTrack.Select(actorUI)</c>
/// (<c>IsSelectable</c> phase gate) → <c>avatar.Select()</c> →
/// <c>CardsHandManager.SwitchHand((CPlayerActor)m_Actor)</c>
/// (InitiativeTrackPlayerAvatar.cs:21-28) — the same switch a miniature click
/// performs. <c>UguiPointer.Release</c> fires
/// <c>ExecuteEvents.pointerClickHandler</c>, which IS <c>Button.OnPointerClick</c>,
/// so the whole chain runs and the game enforces control ownership itself. Clicks
/// stayed dead through test #19 because the track's own NESTED CANVAS
/// (<c>[SerializeField] private Canvas canvas</c>, <c>sortingOrder = 40</c>,
/// InitiativeTrack.cs:53) rode into the host: every portrait Graphic registered
/// with IT instead of the host canvas, so the host GraphicRaycaster raycast an
/// empty set (zero uGUI-click lines), and its sorting override drew the track over
/// the depth-non-writing hands regardless of depth — both fixed by
/// <see cref="CanvasConversion"/> neutralizing nested canvases (test #19; the #18
/// PhaseBanner soft lock was real but not the only blocker). No
/// pointer-over-UI feedback loop:
/// UIManager_IsPointerOverUI_Patch reports over-UI only while the beam/fingertip is
/// actually latched onto a registered surface, and the game never hides the
/// initiative track on over-UI (unlike the stat panels, which are therefore
/// converted non-pokeable).
/// </summary>
internal sealed class InitiativeTrackSurface : TrayMountedPanelSurface, IDepthPortraitPicker
{
    public override string Name => "InitiativeTrack";
    // ALWAYS ON (user ruling 2026-08-13; the initiative order was his own named example):
    // the [WorldUI] InitiativeTrack dial is gone, so the turn order can no longer be released
    // back to its 2D home — where, in VR, nobody can see it.
    protected override bool ConfigEnabled => true;
    protected override PanelSlot Slot => PanelSlot.InitiativeTrack;
    protected override Transform? Mount => PlayTray.Current?.InitiativeMount;
    protected override float MountWidth => PlayTray.InitiativeMountWidth;
    protected override float MountMaxHeight => PlayTray.InitiativeMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.up; // bottom edge on the mount

    protected override RectTransform? FindTarget() =>
        InitiativeTrack.Instance != null ? InitiativeTrack.Instance.transform as RectTransform : null;

    // ---- portrait depth normalization (user #3) ---------------------------------------
    //
    // The game authors the initiative row with real 3D DEPTH: transforms NESTED inside
    // each portrait (the avatar image, and the selection frame that pops the acting
    // actor forward) carry a serialized local z — NOT the portrait's direct
    // <c>initiativeTrackHolder</c> child, whose local z are ~equal (an earlier remap of
    // only those direct children did nothing and never went flat at 0). So the row
    // RECEDES/steps in depth and the selected portrait is raised.
    // On the flat perspective UI camera that reads as gentle 2D styling, but on the
    // world-space host the z is multiplied by the host scale
    // (<see cref="WorldUIConfig.CanvasScaleMm"/> mm per uGUI pixel × the tray/diorama
    // scale) into LITERAL geometry — an EXTREME, head-parallaxing spread (user #3:
    // "the effect is too strong"). It also broke the LASER: RayUguiDriver clamps the
    // beam to (and derives its GraphicRaycaster screen point from) the FLAT host
    // plane, but a z-displaced portrait projects to a DIFFERENT screen position under
    // perspective, so the pick resolved to a neighbour instead of the portrait the
    // beam visually touches.
    //
    // We KEEP the depth (the user likes the recession) but CLAMP the row's TOTAL
    // front-to-back spread to a small hard maximum: whatever the authored range, the
    // deepest and shallowest portrait may differ by at most
    // <see cref="WorldUIConfig.InitiativeDepthMaxSpreadPx"/>. Each portrait's authored z is remapped
    // proportionally by a single factor (order/direction/relative spacing preserved)
    // so the raw spread (max − min z) is scaled down to land at exactly the cap and
    // the rest scale with it — never amplified (a row already flatter than the cap is
    // left alone). Normalizing on the FULL spread (not the largest |z|) is what makes
    // the cap a true hard ceiling on the extremes' separation: the earlier per-|z|
    // band left the front-to-back total at up to twice the band, which the user still
    // found too strong. The compressed spread reads as subtle recession, and because
    // the residual parallax scales with z it stays well under a portrait width, so the
    // flat-plane screen point once again lands inside the correct portrait's projected
    // rect and the GraphicRaycaster (which already distance-sorts hits) resolves the
    // one being pointed at.
    //
    // Applied every tick while converted, computed from the RECORDED raw z (not the
    // live, already-compressed value) so it is idempotent; the raw z is restored on
    // release so the 2D UI is left exactly as the game authored it (the framework's
    // root-only restore never touches these deep children).
    //
    // Cap the row's TOTAL front↔back depth spread. Live-tunable via the debug menu
    // (Panels -> Initiative) — read fresh each tick from
    // WorldUIConfig.InitiativeDepthMaxSpreadPx (default 10 px ≈ ±0.5 cm at 1 mm/px × scale).

    /// <summary>Raw spread (max − min z) below this (px) counts as flat — a row with no authored depth is a no-op.</summary>
    private const float DepthEpsilonPixels = 0.5f;

    /// <summary>Authored (raw) local z per portrait transform, for idempotent remap + restore.</summary>
    private readonly Dictionary<Transform, float> _rawDepth = new(16);

    /// <summary>A depth-bearing transform found this tick, WITH the authored z the walk already
    /// read out of <see cref="_rawDepth"/>. Carrying the value avoids a third dictionary lookup per
    /// node in the apply loop — and a <c>Dictionary&lt;Transform,…&gt;</c> lookup is not free: the
    /// default comparer goes through <c>UnityEngine.Object</c>'s Equals/GetHashCode override.</summary>
    private struct DepthNode
    {
        public Transform T;
        public float Raw;
    }

    /// <summary>Depth-bearing transforms this tick (reused; no per-frame allocation).</summary>
    private readonly List<DepthNode> _depthScratch = new(64);

    /// <summary>DFS work stack for the per-portrait subtree walk (reused; no per-frame allocation).</summary>
    private readonly List<Transform> _depthStack = new(64);

    /// <summary>Next unscaled time <see cref="NormalizeDepth"/> may run when
    /// <c>[Optimize] InitiativeDepthEvalInterval</c> is non-zero. Inert at the default 0.</summary>
    private float _nextDepthEval;

    /// <summary>
    /// Host canvas this surface registered its per-portrait depth picker against
    /// (user #3 follow-up), captured so it can be unregistered on release even after
    /// the framework has Unity-destroyed the host GameObject.
    /// </summary>
    private Canvas? _depthPickHost;

    // ---- inter-round reorder animation (let the game's slide play out) ---------------------
    /// <summary>
    /// True while the game's initiative REORDER ANIMATION is running (or one is queued behind the
    /// current one). Between rounds the game re-sorts the track and plays
    /// <c>InitiativeTrack.AnimateInitiativeReorder</c>: it DISABLES the holder's
    /// <c>HorizontalLayoutGroup</c> + <c>ContentSizeFitter</c>, records each entry's CURRENT
    /// <c>transform.position.x</c>, sorts, then <c>LeanTween.moveX</c>-slides every entry to its
    /// sorted slot over <c>trackReorderDuration</c> (verified decompiled InitiativeTrack.cs).
    /// <c>LeanTween.moveX</c> tweens each portrait's WORLD <c>transform.position.x</c>
    /// (LTDescr.setMoveX). Our per-tick passes fight that: the central content re-fit
    /// (<see cref="CanvasConversion.FitHostToContent"/>) shifts the track's
    /// <c>Target.anchoredPosition</c> and resizes the host when the (layout-disabled) row's bounds
    /// change during the slide, moving the WORLD reference frame out from under those recorded
    /// world-x targets — the slide only hints, then flickers, then the coroutine's end re-enables
    /// the layout group and snaps to the final order. So while the track is animating we HOLD OFF
    /// both interfering passes (freeze the fit, skip depth normalization); they resume on the final,
    /// settled order once the slide completes.
    ///
    /// HOLDING THEM OFF WAS NECESSARY BUT NOT SUFFICIENT (user, follow-up hardware test: "es sieht
    /// immer so aus, als ob die Animation mitten drin geskipped/abgebrochen wird"). Two defects in
    /// the VANILLA coroutine survive on a world-space panel — its tween is world-axis locked, and
    /// its window runs on a DIFFERENT CLOCK than its tween — so the row still snapped part-way.
    /// <see cref="InitiativeReorderSlide"/> now owns the slide outright while the track is adopted;
    /// this flag is OR'd with its <see cref="InitiativeReorderSlide.Active"/> so the fit/depth hold
    /// covers the whole visible animation, including the stretch after the game's own window has
    /// already closed. Full derivation on that class.
    /// </summary>
    private bool _reorderActive;

    /// <summary>
    /// Owns the reorder slide while the track is adopted (see <see cref="InitiativeReorderSlide"/>).
    /// Driven from <see cref="LateTick"/>: after every Update-phase transform writer in the process,
    /// LeanTween's own updater included.
    /// </summary>
    private readonly InitiativeReorderSlide _slide = new();

    /// <summary>
    /// Pins every portrait on the row's Y axis while the track is adopted, by zeroing vanilla's own
    /// hover/press AMPLITUDES so its three geometry writers all write the rect's rest value — and
    /// watches the result (see <see cref="InitiativePortraitPin"/>). Driven from
    /// <see cref="LateTick"/> after the slide, which owns the entry roots this measures against.
    /// </summary>
    private readonly InitiativePortraitPin _pin = new();

    // ---- fit hold: the row must NOT move when a portrait is hovered ------------------------
    /// <summary>
    /// ROOT CAUSE of "Die Initiativreihenfolge 'Hüpft' ein klein wenig nach oben und nach unten
    /// wenn man über die Bilder hovered - sie soll fix stehen bleiben" (user, hardware MP test).
    ///
    /// The portraits REACT to the pointer, and they do it by changing GEOMETRY. Each entry's
    /// clickable <c>avatarButton</c> is an <c>ExtendedButton</c>, and its
    /// <c>ToggleHighlight</c> (decompiled GH.Runtime/ExtendedButton.cs:445-470, reached from
    /// <c>OnPointerEnter</c> → <c>OnHighlight</c>, ExtendedButton.cs:325-331)
    /// <c>LeanTween.scale</c>s the button's target rect to <c>highlightScaleFactor</c> and back to
    /// 1 on exit, plus an optional <c>hoverMovement</c> offset; <c>OnPointerDown/Up</c> write
    /// further scales (ExtendedButton.cs:215/233). The VR laser drives exactly those pointer
    /// events (UguiPointer → the host GraphicRaycaster), so hovering a portrait grows/moves it.
    ///
    /// That geometry is INSIDE the measured content: this surface scopes the content fit to the
    /// game's own <c>initiativeTrackHolder</c> (see <see cref="OnConverted"/>), and
    /// <see cref="CanvasConversion.FitHostToContent"/> measures the union of the VISIBLE GRAPHICS
    /// under it. A hover therefore changes the measured union, the union change exceeds the fit's
    /// 2 % dirty threshold, and GROWTH deliberately fast-paths past the churn damping — so the host
    /// rect is re-sized and the content re-centred inside it mid-hover. <c>Place</c> then
    /// derives the panel pose from that very rect (<c>offset = grow · rect · metersPerPx / 2</c>,
    /// with <see cref="GrowDirection"/> = up for this panel), so the whole row steps up/down. On
    /// hover-out it steps back — but only after the shrink damping's stability window, which is why
    /// it reads as a little hop rather than a smooth follow. This is the SAME family as the docked
    /// use-bar symbols that jumped on hover/press (see <see cref="UseBarsSurface"/>'s FIT STABILITY
    /// doc), and it has the same answer.
    ///
    /// THE FIX — hold the fit frozen unless the LAYOUT TRUTH changed. Time-based hysteresis cannot
    /// work (a hover lasts seconds and would simply "stabilise" into a re-fit). So
    /// <see cref="ConvertedPanel.FitEnabled"/> is a surface-side policy here: it is armed only for a
    /// short settle window after something that genuinely changes what the row CONTAINS — a
    /// different set of entries, a finished reorder slide, or the first conversion — and is off the
    /// rest of the time. Between those windows the host rect is LATCHED, so <c>Place</c>
    /// reproduces the identical pose every frame no matter what the pointer does to a portrait.
    ///
    /// ─── ROUND 2 (hardware 2026-08-08): "Beim DRÜCKEN auf ein Bild … rücken die Bilder minimal
    /// nach oben und unten, aber merkbar" ───────────────────────────────────────────────────────
    ///
    /// The round-1 hold was correct but it left the ARMED window reachable from a pointer action,
    /// and the armed window is a window in which anything that changes the measured union moves the
    /// whole dock. Two corrections, both derived from the fresh log:
    ///
    /// <list type="number">
    /// <item><b>The selected actor is no longer part of the signature.</b> It used to be, so a
    ///   character switch — a CLICK — re-armed the fit for the full 2 s. Inside that window sat the
    ///   press-up scale write, the pointer-exit tween back to 1, AND the mod's own cue rings.
    ///   MEASURED, not inferred — the fresh hardware log (2026-08-08) carries <b>37</b> "row layout
    ///   changed" arms and <b>26 APPLIED</b> re-fits of this one panel, oscillating
    ///   182 ↔ 188 ↔ 182 ↔ 190 ↔ 186 px host height, and the rects named as the union's bottom edge
    ///   in EVERY one of them are <c>'Avatar/GloomhavenVR.FocusRing'</c> and
    ///   <c>'Avatar/GloomhavenVR.SelectionRing'</c> — measured at 146x156, 147x157, 161x172 and
    ///   143x153, 152x163, 155x166, 156x167 px across those lines, i.e. the same ring caught at
    ///   different points of its swell. <c>Board.UiRing</c> breathes by
    ///   <c>FocusCue.ScalePulseAmount</c> = 5 % ⇒ ±7.8 px on a 178 px union — 4.4 %, past the fit's
    ///   2 % dirty threshold, with GROWTH fast-pathing the churn damping. Each applied re-fit moves
    ///   the dock by half the height change × 0.4167 mm/px ≈ 3 mm: "minimal aber merkbar", 26 times
    ///   a session, and re-armed by every click. Selection changes only toggle the entry's own
    ///   selection frame, a graphic that already sits inside the union the rings bound; the row's
    ///   real size is decided by how many entries it holds, and THAT still arms the fit.</item>
    /// <item><b>An armed window now ends at the first APPLIED fit</b> (<c>FitAppliedGeneration</c>),
    ///   not at the 2 s timeout. One genuine layout change deserves exactly one re-measure and one
    ///   re-place; the remaining time was only ever an opportunity for a blinking ring or an
    ///   in-flight tween to be measured a second time. The timeout stays as the upper bound for the
    ///   case where nothing measurable ever lands.</item>
    /// </list>
    ///
    /// Both are belt to <see cref="InitiativePortraitPin"/>'s braces: with the hover/press
    /// amplitudes zeroed the pointer cannot change the union at all any more, so an armed window is
    /// no longer dangerous — it is merely no longer needed on a click.
    ///
    /// The signature is deliberately built from LAYOUT TRUTH only (entry count, active row
    /// children) — cheap reads of the game's own state that hover, press, tween and cue rings can
    /// never touch — mirroring the use-bar precedent (the bar's slot container).
    /// </summary>
    private const float FitSettleSeconds = 2f;

    /// <summary>Layout-truth signature of the row at the last armed re-fit (-1 = nothing seen yet).</summary>
    private int _fitSignature = -1;

    /// <summary><see cref="Time.unscaledTime"/> until which the content fit stays armed.</summary>
    private float _fitArmedUntil;

    /// <summary><see cref="ConvertedPanel.FitAppliedGeneration"/> as of the last ARMING. The window
    /// closes the moment it advances — one genuine layout change, one applied re-fit, one
    /// re-place (-1 = no window has been opened against the current host).</summary>
    private int _fitArmGeneration = -1;

    /// <summary>
    /// Mip-bake rescan cadence while converted (mirrors <c>CardFace.MipRescanInterval</c>, the
    /// proven card-face value): the portraits arrive ASYNC from the misc_characterportraits
    /// bundle and the game re-registers every avatar per round
    /// (<c>SetAttributesDirect</c> -&gt; <c>CharacterPortraitsProvider.RegisterNewUser</c> -&gt;
    /// <c>UpdateTexture</c> puts the ORIGINAL mipless texture back), so a one-shot swap at
    /// conversion would silently decay. 1 s keeps the re-assert cost negligible; the scan
    /// itself is change-gated (see <see cref="PanelMipBake.Rescan"/>).
    /// </summary>
    private const float MipRescanInterval = 1f;
    private float _nextMipRescan;

    public override void Tick()
    {
        bool wasConverted = Panel != null;
        base.Tick();
        if (Panel != null)
        {
            // Let the game's reorder slide play out un-stomped (see _reorderActive docs), and keep
            // the fit frozen against hover-driven content wobble the rest of the time (see the
            // FitSettleSeconds docs): both policies write the SAME switch, so they are decided in
            // one place instead of fighting over ConvertedPanel.FitEnabled.
            UpdateFitHold();
            // Sub-scope (S2 perf round): 'Surface:InitiativeTrackSurface' measured 0.30 ms EVERY
            // frame on hardware and this DFS over every active portrait subtree is the only
            // O(entries × nodes) thing in the step. Naming it makes the next capture attribute the
            // step arithmetically instead of by inference. PerfMonitor.Measure is an
            // allocation-free struct; off the clock it costs one static bool test.
            //
            // [Optimize] InitiativeDepthEvalInterval throttles it. DEFAULT 0 ⇒ the condition is
            // `0f <= 0f` for the interval branch, i.e. the pass runs on exactly the frames it runs
            // on today and the shipped behaviour is bit-identical. The knob exists because the pass
            // is IDEMPOTENT (every target is re-derived from the RECORDED authored z, never from
            // the live compressed value), so a slower cadence can only delay by at most one
            // interval when a newly pooled portrait is first flattened — it can never land a
            // portrait anywhere else.
            if (!_reorderActive)
            {
                float depthInterval = PerfConfig.InitiativeDepthInterval;
                if (depthInterval <= 0f || Time.unscaledTime >= _nextDepthEval)
                {
                    _nextDepthEval = Time.unscaledTime + depthInterval;
                    using (PerfMonitor.Scope("InitTrack.NormalizeDepth"))
                    {
                        NormalizeDepth();
                    }
                }
            }
            // Aliasing round 4: keep the portraits/frames on their mip-baked copies (the game
            // swaps the mipless originals back on round changes and async art arrival).
            if (Time.unscaledTime >= _nextMipRescan)
            {
                _nextMipRescan = Time.unscaledTime + MipRescanInterval;
                PanelMipBake.Rescan(InitiativeTrack.Instance, Name);
            }
        }
        else if (wasConverted)
        {
            _slide.Abort("panel released"); // layout writers back to the game before anything else
            _pin.Release("panel released"); // …and the buttons their authored hover/press amplitudes
            _reorderActive = false; // host gone — the next conversion starts a fresh hold
            _fitSignature = -1;     // …and a fresh settle window for the new host
            _fitArmedUntil = 0f;
            _fitArmGeneration = -1;
            RestoreDepth(); // panel released this tick — hand the 2D row its authored z back
            RestoreEnemyInfoFlatten(); // …and the hover popup its authored rotation/z
            UnregisterDepthPick();
            // Full-restore contract: the 2D track gets its authored sprites/textures back the
            // moment the canvas returns to the game (baked copies are a VR presentation detail).
            PanelMipBake.Restore(InitiativeTrack.Instance);
        }
    }

    /// <summary>
    /// Re-place the docked host (base contract), then advance the mod-owned reorder slide.
    ///
    /// DELIBERATELY IN THE LATE PASS: the slide writes the row entries' <c>localPosition</c>, and the
    /// writers it has to beat — LeanTween's updater (an ordinary MonoBehaviour <c>Update</c> with no
    /// execution-order relation to <see cref="WorldUIModule"/>) and any layout rebuild the game
    /// schedules — all live in the Update phase. Unity runs every <c>LateUpdate</c> after every
    /// <c>Update</c>, so writing here is ordering-proof by rule instead of by luck; the same
    /// reasoning that put <see cref="TrayMountedPanelSurface.LateTick"/> in the late pass.
    /// Placement first: the slide reads nothing from the host pose, but a panel placed after its
    /// content moved would render one frame stale.
    /// </summary>
    public override void LateTick()
    {
        base.LateTick();
        if (Panel != null)
        {
            _slide.Tick(InitiativeTrack.Instance);
            // AFTER the slide: it owns the entry roots, and the pin's integrity watch measures the
            // portraits against the pose the slide (or the layout group) left behind. The pin's own
            // writes never touch an entry root — see InitiativePortraitPin.IsRowFrame.
            _pin.Tick(InitiativeTrack.Instance, _reorderActive || _slide.Active);
            FlattenEnemyInfo();
        }
        else
        {
            _slide.Abort("panel not converted");
        }
    }

    // ---- enemy-info popup coplanarity (user item 5) --------------------------------------
    /// <summary>
    /// THE ALLY BANNER ABOVE THE ENEMY-INFO POPUP, and the four independent occluders it took to
    /// make it fully visible. Reports: "Der Text auf der Gegnerinfo beim Hovern über das Bild in
    /// der Initiativreihenfolge ist 3D schief aus der Karte rausgeragt", then three rounds of "der
    /// Text darüber wo 'Verbündeter' steht ist halb abgeschnitten" — the last one verbatim: "Es
    /// scheint mir so also ob eine unsichtbare Leiste der iniativreihenfolge den Text halb
    /// verdeckt und er einfach 'nur' in den Vordergrund muss."
    ///
    /// WHAT THE POPUP IS AND WHERE IT LIVES (read from source): hovering an enemy portrait opens
    /// that entry's <c>MonsterBaseUI</c>. It is NOT a child of the portrait: every
    /// <c>InitiativeTrackEnemyBehaviour</c> is handed the track's serialized
    /// <c>enemyCardsHolder</c> (InitiativeTrack.cs:652 → <c>Init</c>), whose
    /// <c>SetCardHolder</c> (InitiativeTrackEnemyBehaviour.cs:188-195) REPARENTS the popup into it
    /// with <c>SetParent(monsterBaseHolder, worldPositionStays: false)</c>. <c>enemyCardsHolder</c>
    /// is a SIBLING of <c>initiativeTrackHolder</c>, so <see cref="NormalizeDepth"/> — which walks
    /// the row holder — has never reached the popup at all. And even where it does reach, it only
    /// ever writes <c>localPosition.z</c>; the "schief" part is a local ROTATION, which no pass on
    /// this surface touched. <see cref="EnemyRevealSurface"/> converts the same holder with
    /// <c>Flatten2D</c> during the reveal PHASE, but the HOVER path leaves it under this surface's
    /// own target, where nothing was flattening it.
    ///
    /// The banner is <c>MonsterBaseUI</c>'s <c>roleGameObject</c>/<c>roleText</c>, activated ONLY
    /// for <c>CActor.EType.Ally</c>, <c>Enemy2</c> and <c>Neutral</c> and force-hidden for plain
    /// enemies (MonsterBaseUI.SetBaseStats, MonsterBaseUI.cs:162-180). It hangs ABOVE the card's
    /// frame, so it is the only part of the popup that reaches up out of the card body — which is
    /// why every occluder below showed on ALLY hovers and never on enemy ones.
    ///
    /// THE FOUR MECHANISMS, all still live; each closed a real occluder and would regress alone:
    /// <list type="number">
    /// <item><b>PER-FRAME COPLANARITY (this pass).</b> A one-shot flatten cannot hold:
    ///   <c>MonsterRoundCardUI</c> writes <c>transform.localRotation = Quaternion.Euler(0, -360·n
    ///   + 90, 0)</c> and <c>LeanTween.rotateAround(rect, Vector3.up, …)</c>
    ///   (MonsterRoundCardUI.cs:42-44) — a live Y-axis card flip, literal geometry on a
    ///   world-space canvas; <c>MonsterBaseUI</c> respawns the round card from the pool with
    ///   <c>resetLocalRotation: false</c> (MonsterBaseUI.cs:140), so a recycled card arrives
    ///   mid-flip; and every generation destroys and re-instantiates <c>contentHolder</c>'s
    ///   children (MonsterBaseUI.cs:293/304). The walk covers the popup AND its holder→target
    ///   ancestor chain, which <see cref="NormalizeDepth"/> never reaches. NOT
    ///   <c>Flatten2D =&gt; true</c> on this surface: that zeroes local z across the WHOLE target
    ///   subtree in LateUpdate — after <see cref="NormalizeDepth"/> — and would destroy the row's
    ///   deliberately kept portrait recession (user ruling #3).</item>
    /// <item><b>DRAW-ORDER LIFT (<see cref="LiftEnemyInfoBranch"/>).</b> The game paints this
    ///   window from TWO canvases: the popups live on the ROOT <c>InitiativeModule</c> canvas,
    ///   while the row + its dark band live under a NESTED 'InitiativeTrack' canvas with
    ///   <c>overrideSorting=true</c> whose order the game toggles 40↔0. The conversion's
    ///   nested-canvas adoption CLEARS that override by design, which merges both trees into ONE
    ///   canvas where raw hierarchy order decides — and a band branch that follows the popup
    ///   branch paints the band OVER the banner. The lift holds the popup's root branch as the
    ///   LAST root sibling while converted (sibling index restored on release), so the merged
    ///   canvas says with hierarchy what the game said with sorting.</item>
    /// <item><b>ZTest-ALWAYS MATERIAL CLONES (<see cref="OnTopUiGraphics"/>).</b> The remaining
    ///   cut ran exactly along the CONTROL BOARD's raised wooden rail — the user's "unsichtbare
    ///   Leiste". World-side occlusion: UI shaders depth-test (<c>unity_GUIZTestMode</c> LEqual),
    ///   and opaque depth-writing furniture drawn in the opaque queue z-rejects UI fragments
    ///   behind it. While a popup is SHOWN every Graphic in its subtree is swapped onto a
    ///   ZTest-Always clone of its own material; see that class for the mechanism.</item>
    /// <item><b>CLIP DETACH (<see cref="UnmaskedUiGraphics"/>).</b> With 1-3 provably running
    ///   (the log shows the lift firing, "33 graphic(s) swapped onto ZTest-Always clones", and the
    ///   coplanarity walk reporting "0 node(s) to local z 0") the cut was pixel-identical — which
    ///   eliminates paint order, plane pose and the depth test together. The remaining occluder
    ///   class must ignore sibling order, renderQueue, ZTest AND z, and UI CLIPPING is exactly
    ///   that: <see cref="RectMask2D"/> clips per RENDERER (<c>CanvasRenderer.EnableRectClipping</c>
    ///   feeds <c>_ClipRect</c> to the shader whichever material instance is bound — the round-3
    ///   clone keeps the keyword — and CULLS renderers that leave the rect), and a stencil
    ///   <see cref="Mask"/> wraps every maskable child's <c>materialForRendering</c>. A clipper is
    ///   provably there: the popups hang under <c>enemyCardsHolder</c>, the CONTENT of the "Main
    ///   Area" ScrollRect, and <c>EnsureScrollClipping</c> logged NOTHING for this panel — by its
    ///   own code that silence means the viewport ALREADY HAD a working clipper. The fix is
    ///   <c>maskable=false</c> + <c>RecalculateClipping()</c> per graphic, both calls load-bearing;
    ///   graphics under the popup's OWN internal clippers are skipped (their clipping is design).
    ///   One clip boundary explains every observation of all four rounds.</item>
    /// </list>
    ///
    /// REJECTED, both when the rail was the suspect: <b>lifting the popup off the panel plane</b> —
    /// the offset would have to exceed the rail's protrusion ALONG THE VIEW RAY, which no log
    /// measures and which moves with the board tilt and head position, i.e. a guessed constant in
    /// centimetres bought with real parallax/scale distortion; and <b>a dedicated overrideSorting
    /// canvas on the popup branch</b> — sorting orders draws WITHIN the transparent pass, while the
    /// rail wrote DEPTH in the opaque pass long before any canvas draws, so it fixes nothing
    /// without the on-top material and is then mechanism 3 plus an extra canvas.
    ///
    /// WHAT ZTest-Always COSTS, stated honestly: while (and only while) a popup is hovered, its
    /// pixels also draw over the player's HANDS and a HELD MINI if those are between the eye and
    /// the panel — a scoped, deliberate exception to the "perspective must hold" ruling (whose
    /// enforcement reverted the held mini's own queue bump, FigureGrabbable.ApplyRenderOnTop): the
    /// popup is a transient READING surface the user summoned to the foreground by pointing at it,
    /// it is restored the moment the hover ends, and the alternative is the reported half-cut text.
    /// The queue is deliberately NOT changed: the popup keeps the canvas's ~3000, so the board HUD
    /// widgets (4000, ZTest Always) and the ray visuals (5000) still paint over it — the pointer
    /// the user is hovering WITH can never vanish behind the popup it summoned.
    ///
    /// REVERSIBILITY WITHOUT AN OWNERSHIP FIGHT: a node is recorded ONLY on the tick it is found
    /// DEVIATING, so a node <see cref="EnemyRevealSurface"/>'s own flatten already holds at
    /// identity is never claimed here and the two passes can never disagree about "the original".
    /// While the reveal surface has reparented the holder off this target, <c>IsChildOf</c> fails
    /// and this pass does nothing. Materials the game re-assigns while treated are ceded to the
    /// game (reference-checked restore); everything else comes back through
    /// <see cref="RestoreEnemyInfoFlatten"/>. Driven from <see cref="LateTick"/> rather than from a
    /// hover hook, so <c>FigureIntentPeek</c> opening the same popup via <c>SetHilighted(true)</c>
    /// — and any future opener — is covered for free.
    ///
    /// HONESTY NOTE (source vs inference): the popup's structure, the writers above and the
    /// clipper's existence are READ FROM SOURCE; which occluder produced which screenshot is
    /// INFERRED — prefab data is not in the decompiled sources and no log line measures the
    /// ancestor chain's z, the branches' sibling order or the clip rect. That is why each round
    /// closed a whole family at once instead of shipping a hypothesis test, and why
    /// <see cref="DiagnoseBannerClip"/> ships with the fix: a one-shot "BANNER-CLIP DIAG" line
    /// printing every ancestor clipper's rect vs the banner's, canvas overrides and renderer cull
    /// flags, so the next hardware log carries the proof either way.
    /// </summary>
    private const float EnemyInfoAngleEpsilon = 0.05f; // degrees, CanvasConversion.FlattenAngleEpsilon
    private const float EnemyInfoZEpsilon = 0.01f;     // uGUI px, CanvasConversion.FlattenZEpsilon

    /// <summary>At most one coplanarity log per this many seconds (this is per-frame code).</summary>
    private const float EnemyInfoLogInterval = 5f;

    /// <summary>A node's authored pose, captured the first tick this pass found it deviating.</summary>
    private struct FlatRecord
    {
        public Quaternion Rotation;
        public float Z;
    }

    /// <summary>
    /// Above this many claimed nodes the record is pruned of Unity-destroyed keys.
    /// <c>MonsterBaseUI.GenerateCard</c> destroys and re-instantiates <c>contentHolder</c>'s
    /// children on EVERY generation (MonsterBaseUI.cs:293/304), so without a prune a long session
    /// would accumulate one dead key per node per card — a slow leak in a per-frame structure.
    /// </summary>
    private const int EnemyInfoRecordCap = 512;

    private readonly Dictionary<Transform, FlatRecord> _enemyInfoFlat = new(64);
    private readonly List<RectTransform> _enemyInfoScratch = new(128);
    private readonly List<Transform> _enemyInfoDead = new(64);
    private int _enemyInfoLogged;
    private float _enemyInfoLogNext;

    /// <summary>The popup root branch (<c>EnemyCardsHolder</c>) this surface lifted to the last
    /// root sibling (round 2, occluder 1) — kept so the release can hand the game its authored
    /// sibling order back. Null while nothing is lifted.</summary>
    private Transform? _enemyInfoBranch;

    /// <summary>The lifted branch's authored sibling index (see <see cref="_enemyInfoBranch"/>).</summary>
    private int _enemyInfoBranchIndex = -1;

    /// <summary>One lift log per conversion (the re-assert path is per-frame).</summary>
    private bool _enemyInfoLiftLogged;

    /// <summary>Round 3 ("noch nicht behoben … 'nur' in den Vordergrund"): the shown popups'
    /// graphics ride ZTest-Always clones of their own materials so the banner stops losing the
    /// depth test against the control board's raised rail — see the round-3 doc section above
    /// and <see cref="OnTopUiGraphics"/> for the mechanism, safety belts and restore contract.</summary>
    private readonly OnTopUiGraphics _enemyInfoOnTop = new();

    /// <summary>Round 4 ("unverändert"): the shown popups' graphics are detached from ancestor
    /// clippers (RectMask2D clip rect + renderer cull + stencil Mask) — the one occluder family
    /// immune to rounds 1–3 — see the round-4 doc section above and <see cref="UnmaskedUiGraphics"/>
    /// for the mechanism, the internal-clipper guard and the restore contract.</summary>
    private readonly UnmaskedUiGraphics _enemyInfoUnmask = new();

    /// <summary>One <see cref="DiagnoseBannerClip"/> line per conversion (armed until the first
    /// popup that actually SHOWS the role banner — enemy popups keep it inactive).</summary>
    private bool _bannerClipDiagLogged;

    /// <summary>Scratch for <see cref="CanvasSpaceRect"/> (single-threaded Unity main loop).</summary>
    private static readonly Vector3[] RectCornerScratch = new Vector3[4];

    /// <summary>Force every node of a SHOWN enemy-info popup coplanar with the panel: identity
    /// local rotation, zero local z. X/Y are never touched, so the card's own slide/fade-in
    /// animations keep playing — flat. See the doc block above for the full derivation.</summary>
    private void FlattenEnemyInfo()
    {
        InitiativeTrack track = InitiativeTrack.Instance;
        Transform? holder = track != null ? track.enemyCardsHolder : null;
        RectTransform? target = Panel != null ? Panel.Target : null;
        if (holder == null || target == null || !holder.IsChildOf(target))
        {
            // No track, or EnemyRevealSurface has adopted the holder — not ours this tick. The
            // pose records deliberately STAY (round 2: record-only-while-deviating keeps the two
            // surfaces from fighting over an "original"), but the on-top materials must NOT
            // follow the holder onto the reveal panel: that surface never asked for ZTest-Always
            // content, and the popups it shows are its own presentation to govern.
            _enemyInfoOnTop.RestoreAll("enemyCardsHolder left the initiative panel");
            // Round 4: nor the unmask — the reveal panel's popups must clip as the game authored.
            _enemyInfoUnmask.RestoreAll("enemyCardsHolder left the initiative panel");
            return;
        }

        // Round 2, occluder 1 (merged-canvas draw order): the popup branch paints LAST.
        LiftEnemyInfoBranch(holder, target);

        int flattenedRot = 0;
        int flattenedZ = 0;
        float worstAngle = 0f;
        float worstZ = 0f;
        string worstNode = string.Empty;

        // Round 2, occluder 2: the popup's ANCESTOR CHAIN (the ScrollRect content up to — but
        // excluding — the converted target root) is held coplanar too. Nodes INSIDE the popup at
        // local z 0 buy nothing if an ancestor's authored z parks the whole plane behind the MR
        // plate. ≤ a handful of nodes, same epsilons, same record/restore as everything below;
        // x/y are never touched, so the live ScrollRect's content writes are never fought.
        for (Transform? link = holder; link != null && !ReferenceEquals(link, target); link = link.parent)
        {
            FlattenPoseNode(link, ref flattenedRot, ref flattenedZ,
                ref worstAngle, ref worstZ, ref worstNode);
        }

        // Only SHOWN popups. Their roots are the holder's direct children, and a hidden one cannot
        // be seen tilted; walking just the active ones keeps this off the per-frame budget on a
        // panel that already carries a live mirror. The popup is switched on in the Update phase
        // (pointer enter / SetHilighted), so this LateUpdate still catches its very first frame.
        for (int c = 0; c < holder.childCount; c++)
        {
            Transform popup = holder.GetChild(c);
            if (!popup.gameObject.activeSelf)
                continue;

            _enemyInfoScratch.Clear();
            popup.GetComponentsInChildren(includeInactive: true, _enemyInfoScratch);
            for (int i = 0; i < _enemyInfoScratch.Count; i++)
            {
                RectTransform rect = _enemyInfoScratch[i];
                if (rect == null)
                    continue;
                FlattenPoseNode(rect, ref flattenedRot, ref flattenedZ,
                    ref worstAngle, ref worstZ, ref worstNode);
            }

            // Round 4, PROOF FIRST: one-shot ancestor-clipper dump for the live banner, taken
            // BEFORE the unmask below detaches anything — the next hardware log must be able to
            // name the occluder even if the fix already hides it visually.
            DiagnoseBannerClip(popup);

            // Round 4: a SHOWN popup ignores ancestor clippers — the RectMask2D clip rect /
            // renderer cull / stencil family that rounds 1-3 could not touch (see the round-4
            // doc section). Same idempotence and lifetime as the on-top pass below.
            _enemyInfoUnmask.Apply(popup, "initiative hover popup");

            // Round 3: a SHOWN popup wins the depth test — banner, frame, portrait, texts, all
            // of it (see the round-3 doc section). Idempotent (one hash probe per already-seen
            // graphic); new graphics from a card re-generation are picked up on their first
            // shown LateTick, i.e. before their first visible frame ends.
            _enemyInfoOnTop.Apply(popup, "initiative hover popup");
        }
        _enemyInfoScratch.Clear();

        // Bounded record: drop keys the game has since destroyed (see EnemyInfoRecordCap). A dead
        // key has nothing left to restore, so pruning it is loss-free.
        if (_enemyInfoFlat.Count > EnemyInfoRecordCap)
        {
            _enemyInfoDead.Clear();
            foreach (KeyValuePair<Transform, FlatRecord> kv in _enemyInfoFlat)
            {
                if (kv.Key == null) // Unity fake-null: destroyed, but still a real dictionary key
                    _enemyInfoDead.Add(kv.Key!);
            }
            for (int i = 0; i < _enemyInfoDead.Count; i++)
                _enemyInfoFlat.Remove(_enemyInfoDead[i]);
            _enemyInfoDead.Clear();
            _enemyInfoLogged = Mathf.Min(_enemyInfoLogged, _enemyInfoFlat.Count);
        }

        // Change-gated + throttled: one line when the claimed set grows (a fresh card generation
        // brings new nodes), never one per frame and never one per node.
        if (_enemyInfoFlat.Count <= _enemyInfoLogged || Time.unscaledTime < _enemyInfoLogNext)
            return;
        _enemyInfoLogged = _enemyInfoFlat.Count;
        _enemyInfoLogNext = Time.unscaledTime + EnemyInfoLogInterval;
        VRLog.Info("WorldUI",
            $"Enemy-info coplanarity: forced {flattenedRot} node(s) to identity local rotation " +
            $"(worst {worstAngle:F1}° on '{(worstNode.Length == 0 ? "-" : worstNode)}') and " +
            $"{flattenedZ} node(s) to local z 0 (worst {worstZ:F1} px) under the track's " +
            $"enemyCardsHolder; {_enemyInfoLogged} node(s) recorded for restore. The hover popup " +
            "is reparented OUT of the row into that holder (InitiativeTrackEnemyBehaviour." +
            "SetCardHolder), so the row's depth pass never saw it, and MonsterRoundCardUI keeps " +
            "writing a Y-axis flip rotation (plus a pooled respawn with resetLocalRotation:false) " +
            "— which is why this is a per-frame late pass and not a one-shot. The row's own " +
            "portrait recession is untouched: this is scoped to the popup subtree AND its holder " +
            "chain up to the target root (round 2: an ancestor's authored z parked the plane " +
            "behind the MR plate, which z-rejects everything behind it — the cut ally banner).");
    }

    /// <summary>
    /// Flatten ONE node onto the panel plane — identity local rotation, local z 0, x/y untouched —
    /// recording its authored pose in <see cref="_enemyInfoFlat"/> on the tick it is first found
    /// deviating (see the class doc: record-only-while-deviating is what keeps this pass and
    /// <see cref="EnemyRevealSurface"/>'s from claiming the same node's "original"). Shared by the
    /// popup-subtree walk and the round-2 holder-chain walk so both provably apply one policy.
    /// </summary>
    private void FlattenPoseNode(Transform node, ref int flattenedRot, ref int flattenedZ,
        ref float worstAngle, ref float worstZ, ref string worstNode)
    {
        Vector3 lp = node.localPosition;
        Quaternion rot = node.localRotation;
        float angle = Quaternion.Angle(rot, Quaternion.identity);
        bool tiltedRot = angle > EnemyInfoAngleEpsilon;
        bool tiltedZ = Mathf.Abs(lp.z) > EnemyInfoZEpsilon;
        if (!tiltedRot && !tiltedZ)
            return;

        // Recorded ONLY while deviating — that is what keeps this pass and
        // EnemyRevealSurface's from ever claiming the same node's "original".
        if (!_enemyInfoFlat.ContainsKey(node))
            _enemyInfoFlat[node] = new FlatRecord { Rotation = rot, Z = lp.z };

        if (tiltedRot)
        {
            node.localRotation = Quaternion.identity;
            flattenedRot++;
            if (angle > worstAngle)
            {
                worstAngle = angle;
                worstNode = node.name;
            }
        }
        if (tiltedZ)
        {
            node.localPosition = new Vector3(lp.x, lp.y, 0f);
            flattenedZ++;
            if (Mathf.Abs(lp.z) > Mathf.Abs(worstZ))
                worstZ = lp.z;
        }
    }

    /// <summary>
    /// Round 2, occluder 1 ("VERBÜNDETER halb abgeschnitten"): hold the popup's ROOT BRANCH — the
    /// <paramref name="holder"/>'s ancestor that is a DIRECT child of the converted target — as the
    /// LAST root sibling while this surface owns the track. The game paints the popups and the row
    /// band from two canvases (root vs the nested overrideSorting 'InitiativeTrack' canvas) and the
    /// conversion's adoption merges them into one, where raw hierarchy order let the band paint
    /// over the ally banner; last-sibling says with hierarchy what the game said with sorting.
    /// Re-asserted per LateTick (one integer compare in steady state — the game never re-sorts the
    /// root's children, but a re-created sibling could land after ours); the authored index is
    /// recorded once and handed back by <see cref="RestoreEnemyInfoFlatten"/>. The content fit is
    /// scoped to <c>initiativeTrackHolder</c> (a sibling branch), so the lift can never change the
    /// measured union, the host rect or the panel pose.
    /// </summary>
    private void LiftEnemyInfoBranch(Transform holder, RectTransform target)
    {
        Transform branch = holder;
        while (branch.parent != null && !ReferenceEquals(branch.parent, target))
            branch = branch.parent;
        Transform? parent = branch.parent;
        if (parent == null)
            return; // belt only — the caller already proved holder.IsChildOf(target)

        int last = parent.childCount - 1;
        int index = branch.GetSiblingIndex();
        if (index == last)
            return; // already painting on top — steady state, nothing to write

        if (_enemyInfoBranch == null)
        {
            _enemyInfoBranch = branch;     // authored order, captured on the FIRST lift only
            _enemyInfoBranchIndex = index; // (a re-assert must not overwrite it with our own value)
        }
        branch.SetAsLastSibling();
        if (_enemyInfoLiftLogged)
            return;
        _enemyInfoLiftLogged = true;
        VRLog.Info("WorldUI",
            $"Enemy-info draw-order lift: moved the track's '{branch.name}' branch (the hover " +
            $"popup's holder) from root sibling {index} to LAST ({last}) so the popup paints " +
            "ABOVE the row band. The game splits this window across two canvases — the popups on " +
            "the ROOT canvas, the row+band under the nested 'InitiativeTrack' canvas whose " +
            "overrideSorting (order 40 in 2D, InitiativeTrack.Awake → ToggleSortingOrder) the " +
            "panel adoption clears — so on the merged world-space canvas hierarchy order decides, " +
            "and the band was painting over the ally 'VERBÜNDETER' banner. Authored sibling order " +
            "is restored on release; portrait picking is unaffected (depth-aware TryPickPortrait " +
            "bypasses paint order).");
    }

    /// <summary>Give every node this pass claimed its authored rotation/z back — and the lifted
    /// popup branch its authored sibling order (un-convert / shutdown) — the same full-restore
    /// contract the depth pass honours. Round 3: the popups' authored materials come back through
    /// the same door (reference-checked — a material the game re-assigned meanwhile is ceded).</summary>
    private void RestoreEnemyInfoFlatten()
    {
        _enemyInfoOnTop.RestoreAll("initiative panel released");
        _enemyInfoUnmask.RestoreAll("initiative panel released"); // round 4 — same lifetime
        _bannerClipDiagLogged = false; // conversions are rare — one diag line per conversion

        if (_enemyInfoBranch != null)
        {
            Transform branch = _enemyInfoBranch; // Unity fake-null aware: destroyed → skip
            if (branch != null && branch.parent != null && _enemyInfoBranchIndex >= 0)
                branch.SetSiblingIndex(Mathf.Min(_enemyInfoBranchIndex, branch.parent.childCount - 1));
            _enemyInfoBranch = null;
        }
        _enemyInfoBranchIndex = -1;
        _enemyInfoLiftLogged = false; // conversions are rare — one lift line per conversion is signal

        if (_enemyInfoFlat.Count == 0)
            return;
        foreach (KeyValuePair<Transform, FlatRecord> kv in _enemyInfoFlat)
        {
            Transform t = kv.Key;
            if (t == null)
                continue;
            t.localRotation = kv.Value.Rotation;
            Vector3 lp = t.localPosition;
            t.localPosition = new Vector3(lp.x, lp.y, kv.Value.Z);
        }
        _enemyInfoFlat.Clear();
        _enemyInfoLogged = 0;
    }

    /// <summary>
    /// Round 4's PROOF line — one-shot per conversion, fired the first tick a shown popup has its
    /// role banner ACTIVE (ally / enemy2 / neutral hovers only; MonsterBaseUI.SetBaseStats:162-180
    /// keeps it off for plain enemies, so an enemy-only session leaves the shot armed). Logs, with
    /// grep prefix <c>BANNER-CLIP DIAG</c>, everything the clip family could hide behind: the
    /// banner's rect, <c>maskable</c>, renderer cull flag and stencil depth; the RectMask2D that
    /// GOVERNS it per uGUI's own resolution (<c>MaskUtilities.GetRectMaskForClippable</c> — the
    /// component that actually writes its clip rect); and the full ancestor chain's clippers
    /// (RectMask2D/Mask with enabled state and rects), nested canvases (overrideSorting/order) and
    /// culled renderers. All rects are in ROOT-CANVAS space so "the clip top edge sits N px below
    /// the banner top" can be read straight off the line. Taken BEFORE the unmask detaches
    /// anything (call order in <see cref="FlattenEnemyInfo"/>), so the log names the occluder even
    /// though the same tick's fix already hides it visually — the round-2/3 lesson: the next
    /// hardware log must attribute, not just show, or the round after this one starts blind.
    /// </summary>
    private void DiagnoseBannerClip(Transform popup)
    {
        if (_bannerClipDiagLogged)
            return;
        MonsterBaseUI mb = popup.GetComponent<MonsterBaseUI>();
        if (mb == null)
            return;
        GameObject role = mb.roleGameObject;
        if (role == null || !role.activeInHierarchy)
            return; // enemy popup — no banner shown; stay armed for the first ally/neutral hover

        // The banner graphic: the role label rides a TextLocalizedListener that RequireComponents
        // a TextMeshProUGUI on the same node (decompiled TextLocalizedListener.cs); fall back to
        // the first maskable graphic under roleGameObject (its backdrop) if the ref is unwired.
        MaskableGraphic? banner = mb.roleText != null ? mb.roleText.GetComponent<TMP_Text>() : null;
        if (banner == null)
            banner = role.GetComponentInChildren<MaskableGraphic>(includeInactive: true);
        Canvas? canvas = banner != null ? banner.canvas : null;
        if (banner == null || canvas == null)
            return; // no drawable banner yet (mid-generation) — try again next shown tick
        _bannerClipDiagLogged = true;

        Canvas rootCanvas = canvas.rootCanvas;
        Matrix4x4 toCanvas = rootCanvas.transform.worldToLocalMatrix;
        var sb = new System.Text.StringBuilder(1024);
        Rect bannerRect = CanvasSpaceRect((RectTransform)banner.transform, toCanvas);
        sb.Append("BANNER-CLIP DIAG: banner '").Append(banner.name)
            .Append(banner is TMP_Text label ? "' (\"" + label.text + "\")" : "'")
            .Append(" rect ").Append(FormatRect(bannerRect))
            .Append(" px (root-canvas space), maskable=").Append(banner.maskable)
            .Append(", canvasRenderer.cull=").Append(banner.canvasRenderer.cull)
            .Append(", stencilDepth=")
            .Append(MaskUtilities.GetStencilDepth(banner.transform, rootCanvas.transform));

        RectMask2D? governing = MaskUtilities.GetRectMaskForClippable(banner);
        if (governing != null)
        {
            Rect clip = CanvasSpaceRect((RectTransform)governing.transform, toCanvas);
            sb.Append(" | GOVERNING RectMask2D '").Append(governing.name).Append("' rect ")
                .Append(FormatRect(clip)).Append(" — its top edge is ")
                .Append((bannerRect.yMax - clip.yMax).ToString("F0"))
                .Append(" px BELOW the banner top (positive = that many banner px are clipped off)");
        }
        else
        {
            sb.Append(" | no governing RectMask2D (uGUI resolution)");
        }

        sb.Append(" | ANCESTOR CHAIN (banner → root canvas):");
        for (Transform? t = banner.transform.parent; t != null; t = t.parent)
        {
            RectMask2D? rm = t.GetComponent<RectMask2D>();
            Mask? stencil = t.GetComponent<Mask>();
            Canvas? cv = t.GetComponent<Canvas>();
            CanvasRenderer? cr = t.GetComponent<CanvasRenderer>();
            bool culled = cr != null && cr.cull;
            bool isRoot = ReferenceEquals(t, rootCanvas.transform);
            if (rm != null || stencil != null || cv != null || culled)
            {
                sb.Append(" '").Append(t.name).Append("':");
                if (rm != null)
                    sb.Append(" RectMask2D(").Append(rm.enabled ? "ENABLED" : "disabled")
                        .Append(", rect ")
                        .Append(FormatRect(CanvasSpaceRect((RectTransform)t, toCanvas))).Append(')');
                if (stencil != null)
                {
                    string sprite = stencil.graphic is Image img && img.sprite != null
                        ? img.sprite.name : "<none>";
                    sb.Append(" Mask(").Append(stencil.enabled ? "ENABLED" : "disabled")
                        .Append(", graphic ")
                        .Append(stencil.graphic != null && stencil.graphic.enabled ? "on" : "off")
                        .Append(", sprite '").Append(sprite).Append("', rect ")
                        .Append(FormatRect(CanvasSpaceRect((RectTransform)t, toCanvas))).Append(')');
                }
                if (cv != null)
                    sb.Append(" Canvas(overrideSorting=").Append(cv.overrideSorting)
                        .Append(", order=").Append(cv.sortingOrder).Append(')');
                if (culled)
                    sb.Append(" CanvasRenderer.cull=TRUE");
                sb.Append(';');
            }
            if (isRoot)
                break;
        }
        sb.Append(" — a clipper whose edge matches the reported cut is the occluder rounds 1-3 " +
                  "(paint order / plane z / ZTest) provably could not touch; the UNMASK pass " +
                  "detaches the popup from it this same tick.");
        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>A RectTransform's axis-aligned bounds in ROOT-CANVAS local space (the same frame
    /// <c>MaskableGraphic.rootCanvasRect</c> culls in), via its world corners.</summary>
    private static Rect CanvasSpaceRect(RectTransform rt, Matrix4x4 toCanvas)
    {
        rt.GetWorldCorners(RectCornerScratch);
        Vector3 p = toCanvas.MultiplyPoint(RectCornerScratch[0]);
        float minX = p.x, maxX = p.x, minY = p.y, maxY = p.y;
        for (int i = 1; i < 4; i++)
        {
            p = toCanvas.MultiplyPoint(RectCornerScratch[i]);
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Corner-pair formatting for <see cref="DiagnoseBannerClip"/>'s rects — min/max
    /// corners read better against "the cut is at y=…" than Unity's x/y/w/h ToString.</summary>
    private static string FormatRect(Rect r)
        => $"({r.xMin:F0},{r.yMin:F0})..({r.xMax:F0},{r.yMax:F0})";

    /// <summary>
    /// Decide whether the central content fit may run this tick, and log every edge.
    ///
    /// Two reasons to hold it, both writing the one switch:
    /// <list type="bullet">
    /// <item>the game's REORDER ANIMATION is in flight — a mid-slide re-fit shifts
    ///   <c>Target.anchoredPosition</c> and resizes the host, moving the world frame out from under
    ///   the tween's recorded world-x slots (the flicker documented on <see cref="_reorderActive"/>).
    ///   The signal is the game's own <c>isAnimating</c> OR'd with <c>animationDelayed</c> so a
    ///   back-to-back re-sort holds continuously instead of thawing for the one-frame gap;</item>
    /// <item>nothing about the row's LAYOUT TRUTH has changed recently — then a measured change can
    ///   only be a pointer transient (hover/press scaling), and re-fitting on it moves the whole
    ///   dock (the "Hüpft" report — see the <see cref="FitSettleSeconds"/> docs).</item>
    /// </list>
    /// A real change re-arms the fit for <see cref="FitSettleSeconds"/>, which is long enough for
    /// the pooled-in entries to lay out and for the fit's own first-fit delay and damping to land.
    /// </summary>
    private void UpdateFitHold()
    {
        if (Panel == null)
            return;

        InitiativeTrack track = InitiativeTrack.Instance;
        float now = Time.unscaledTime;

        // OR'd with the mod-owned slide: the game's own flags drop when its (Chronos-clocked) window
        // closes, which can be well before the visible slide has finished — see
        // InitiativeReorderSlide. Holding until the LAST of the two is what makes the hold cover the
        // whole animation the user actually watches.
        bool animating = (track != null && (track.isAnimating || track.animationDelayed)) || _slide.Active;
        if (animating != _reorderActive)
        {
            _reorderActive = animating;
            if (animating)
            {
                VRLog.Info("WorldUI", "Initiative reorder animation detected — deferring content re-fit " +
                                      "and depth normalization so the game's slide plays out un-stomped.");
            }
            else
            {
                // The settled order IS a real content change (entries added/removed/re-sorted):
                // arm the fit so the final row gets measured once.
                _fitArmedUntil = now + FitSettleSeconds;
                _fitArmGeneration = Panel.FitAppliedGeneration;
                VRLog.Info("WorldUI", "Initiative reorder animation complete — resuming content re-fit " +
                                      "and depth normalization on the final order.");
            }
        }

        int signature = RowLayoutSignature(track);
        if (signature != 0 && signature != _fitSignature)
        {
            bool first = _fitSignature == -1;
            _fitSignature = signature;
            _fitArmedUntil = now + FitSettleSeconds;
            _fitArmGeneration = Panel.FitAppliedGeneration;
            if (!first)
            {
                VRLog.Info("WorldUI", "Initiative row layout changed (entry set) — content fit " +
                                      $"re-armed for at most {FitSettleSeconds:F1} s, and only until the " +
                                      "FIRST applied re-fit. Outside these windows the fitted rect is " +
                                      "LATCHED, so neither a portrait's highlight scaling nor a blinking " +
                                      "focus/turn ring can move the docked track (user: 'sie soll fix " +
                                      "stehen bleiben' / 'sie sollen sich gar nicht bewegen').");
            }
        }

        // Compared against the LIVE flag, never a shadow copy: the fit machinery owns this switch
        // too (it freezes a committed one-shot rect), so a shadow would eventually disagree with
        // reality and hand the panel a state nobody asked for.
        bool applied = Panel.FitAppliedGeneration != _fitArmGeneration;
        bool arm = !_reorderActive && now < _fitArmedUntil && !applied;
        if (Panel.FitEnabled != arm)
        {
            Panel.FitEnabled = arm;
            if (!arm && applied && !_reorderActive)
            {
                VRLog.Info("WorldUI", "Initiative content fit disarmed after ONE applied re-fit " +
                                      $"({_fitArmedUntil - now:F1} s of the window unused). The rest of " +
                                      "the window could only ever have re-measured the SAME row through " +
                                      "a blinking focus/turn ring or an in-flight hover tween, which is " +
                                      "what made the dock bob after a click.");
            }
        }
    }

    /// <summary>
    /// Cheap signature of everything about the row that legitimately changes its measured size:
    /// how many entries the track holds (<c>actorsUI</c> — the game's own list, the row's layout
    /// truth) and how many of the holder's direct children are actually shown (covers the pooled-in
    /// avatars and the gamepad hotkey tips toggling with the input device). Nothing here can be
    /// reached by a POINTER: hover, press and the mod's cue rings never add, remove, show or hide an
    /// entry. Returns 0 while the track is not built yet, which the caller treats as "no
    /// information", never as a change.
    ///
    /// THE SELECTED ACTOR IS DELIBERATELY NOT IN HERE ANY MORE (round 2, see the fit-hold block):
    /// it made a CLICK — the character switch — open a 2 s armed window, and an armed window is a
    /// window in which the mod's own blinking focus/turn ring (the rect the fresh log names as the
    /// union's bottom edge) re-fits and re-places the whole dock. Selection only toggles the entry's
    /// selection frame, which already lies inside the union the rings bound, so its size effect is
    /// nil while its cost was the reported bob.
    /// </summary>
    private static int RowLayoutSignature(InitiativeTrack? track)
    {
        if (track == null)
            return 0;
        Transform? holder = track.initiativeTrackHolder;
        if (holder == null)
            return 0;
        int active = 0;
        for (int i = 0; i < holder.childCount; i++)
        {
            if (holder.GetChild(i).gameObject.activeSelf)
                active++;
        }
        int entries = track.actorsUI != null ? track.actorsUI.Count : 0;
        // 0 is the caller's "not built yet" sentinel, so the fold is forced away from it rather
        // than assumed to miss it.
        int sig = unchecked((entries + 1) * 31 + active * 7);
        return sig == 0 ? 1 : sig;
    }

    public override void Shutdown()
    {
        _slide.Abort("surface shutdown"); // layout writers back to the game while the row still lives
        _pin.Release("surface shutdown"); // …same window: the buttons are still alive here
        _reorderActive = false; // the panel is about to be released — drop any active hold
        _fitSignature = -1;     // …and let the next conversion measure the row from scratch
        _fitArmedUntil = 0f;
        _fitArmGeneration = -1;
        RestoreDepth(); // before base releases the panel (holder still alive here)
        RestoreEnemyInfoFlatten(); // …same window for the hover popup's authored rotation/z
        UnregisterDepthPick();
        PanelMipBake.Restore(InitiativeTrack.Instance); // originals back before the release
        base.Shutdown();
    }

    private void UnregisterDepthPick()
    {
        if (_depthPickHost is not null) // reference check: the host may be Unity-destroyed
        {
            DepthPortraitPicks.Unregister(_depthPickHost);
            _depthPickHost = null;
        }
    }

    /// <summary>
    /// Clamp the row's TOTAL front-to-back depth spread to <see cref="WorldUIConfig.InitiativeDepthMaxSpreadPx"/>
    /// (see the field docs): walk every active portrait's SUBTREE and scale each authored
    /// non-zero local z by the single factor that maps the raw protrusion spread (from the
    /// flat holder plane) onto the cap — so cap 0 collapses the whole row (portraits AND
    /// their selection frames) onto one plane. Change-gated writes; nothing to fight since
    /// the game never animates portrait z (Select/Deselect toggle material FX + a child
    /// selection object's visibility, never a transform z/scale).
    /// </summary>
    private void NormalizeDepth()
    {
        Transform? holder = InitiativeTrack.Instance != null
            ? InitiativeTrack.Instance.initiativeTrackHolder
            : null;
        if (holder == null)
            return;

        // The row's visible recession/emphasis is authored NOT on the holder's DIRECT
        // children (their local z are ~equal — remapping only those did nothing and 0 was
        // never flat) but on transforms NESTED inside each portrait: the avatar image, and
        // the selection frame whose forward z pops the acting actor toward the head ("manche
        // hervorgehoben"). The game's own selection path never moves a portrait transform in
        // z or scale (Select → selectionObject.SetActive + material _FXAnim only), so the
        // pop is authored geometry, not an animated offset — a subtree walk reaches all of
        // it. Zeroing local z on this single world-space uGUI canvas removes ONLY the
        // geometric protrusion; draw order is hierarchy-based, so the selection glow stays
        // visible (whose turn it is is never hidden) — the row just goes flat.
        _depthScratch.Clear();
        int visited = 0;
        float rawMin = 0f; // the holder plane (local z == 0) is the shallow reference
        float rawMax = 0f;
        // INDEXED, not `foreach (Transform rootChild in holder)` (S2 perf round). Transform's
        // enumerator is a CLASS returned as a non-generic IEnumerator, so the foreach allocated one
        // object per tick on a per-frame path — gen0 pressure for nothing. GetChild(i) in index
        // order visits exactly the same children in exactly the same order.
        int rootCount = holder.childCount;
        for (int r = 0; r < rootCount; r++)
        {
            Transform rootChild = holder.GetChild(r);
            if (!rootChild.gameObject.activeSelf)
                continue;
            // The ENTRY ROOTS themselves are deliberately NOT candidates (round 2). Their authored
            // z are ~equal and flat — remapping only them was tried and did nothing — but they ARE
            // the transforms vanilla's world-x writes leak an out-of-plane offset onto (see
            // InitiativeReorderSlide's round-2 note). Left in the candidate set, the FIRST frame
            // that carried such a leak would be recorded here as that entry's "authored" depth and
            // then re-asserted forever, which both cements the leak and turns the row's real
            // recession into noise (the remap factor is cap / spread). The row's axis guard owns
            // an entry root's z; this pass owns the depth NESTED inside each portrait, which is
            // where the game actually authored it.
            // childCount hoisted here and at the DFS node below: it is a Unity interop property
            // read, it was evaluated on EVERY loop iteration, and neither loop body can change the
            // child count of the transform it is reading (both only push onto _depthStack). Same
            // children, same order, ~one interop call per node instead of one per node PLUS one per
            // child.
            int seedCount = rootChild.childCount;
            for (int k = 0; k < seedCount; k++)
            {
                Transform seed = rootChild.GetChild(k);
                if (seed.gameObject.activeSelf)
                    _depthStack.Add(seed);
            }
            while (_depthStack.Count > 0)
            {
                int last = _depthStack.Count - 1;
                Transform t = _depthStack[last];
                _depthStack.RemoveAt(last);

                visited++;

                // Record the authored z once; thereafter the remap reads from here, so a
                // prior frame's compressed value never becomes the new baseline. Only
                // depth-BEARING transforms are tracked (|z| >= epsilon) — a flat transform's
                // target is always 0, so tracking it would only add pointless writes.
                //
                // ONE dictionary lookup per node instead of two (S2 perf round). The shipped body
                // asked TryGetValue and then ContainsKey for the very same key on every node of
                // every portrait subtree, every frame. The three cases are enumerated and each is
                // bit-identical to the shipped answer: key present ⇒ tracked with the stored raw;
                // key absent and |z| ≥ epsilon ⇒ recorded, then tracked with that z (which is what
                // ContainsKey found); key absent and |z| < epsilon ⇒ not recorded, not tracked.
                bool tracked;
                if (_rawDepth.TryGetValue(t, out float raw))
                {
                    tracked = true;
                }
                else
                {
                    raw = t.localPosition.z;
                    tracked = Mathf.Abs(raw) >= DepthEpsilonPixels;
                    if (tracked)
                        _rawDepth[t] = raw;
                }
                if (tracked)
                {
                    _depthScratch.Add(new DepthNode { T = t, Raw = raw });
                    if (raw < rawMin)
                        rawMin = raw;
                    if (raw > rawMax)
                        rawMax = raw;
                }

                int childCount = t.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform c = t.GetChild(i);
                    if (c.gameObject.activeSelf)
                        _depthStack.Add(c);
                }
            }
        }

        float rawSpread = rawMax - rawMin;
        PerfMonitor.Count("InitDepth.Nodes", visited);
        PerfMonitor.Count("InitDepth.Tracked", _depthScratch.Count);
        if (_depthScratch.Count == 0 || rawSpread < DepthEpsilonPixels)
            return; // genuinely flat row (or depth not yet laid out) — nothing to compress

        // Single proportional factor: remap the FULL protrusion spread (deepest ↔ shallowest,
        // measured from the flat holder plane) onto the cap so the extremes never differ by
        // more than the (live) cap, order/direction and relative spacing preserved. Compress
        // only, never amplify. At cap 0 the factor is 0 → every tracked transform's local z
        // collapses to 0, so EVERY portrait (and its selection frame) sits on the one flat
        // plane: a truly flat row at all times. Read the cap fresh each tick so a debug-menu
        // change re-clamps next tick (idempotent — target always from the recorded RAW z).
        float maxSpread = Mathf.Max(0f, WorldUIConfig.InitiativeDepthMaxSpreadPx.Value);
        float scale = Mathf.Min(1f, maxSpread / rawSpread);
        int writes = 0;
        for (int i = 0; i < _depthScratch.Count; i++)
        {
            DepthNode node = _depthScratch[i];
            // node.Raw IS _rawDepth[node.T] — the walk above only ever records a node with the
            // value it just read out of (or wrote into) that dictionary, so dropping the lookup
            // here changes nothing but the cost.
            float target = node.Raw * scale;
            Vector3 lp = node.T.localPosition;
            if (Mathf.Abs(lp.z - target) > 0.001f)
            {
                node.T.localPosition = new Vector3(lp.x, lp.y, target);
                writes++;
            }
        }
        PerfMonitor.Count("InitDepth.Writes", writes);
    }

    /// <summary>Restore each recorded portrait's authored z (reversibility) and forget them.</summary>
    private void RestoreDepth()
    {
        if (_rawDepth.Count == 0)
            return;
        foreach (KeyValuePair<Transform, float> kv in _rawDepth)
        {
            Transform t = kv.Key;
            if (t == null)
                continue;
            Vector3 lp = t.localPosition;
            t.localPosition = new Vector3(lp.x, lp.y, kv.Value);
        }
        _rawDepth.Clear();
    }

    /// <summary>
    /// Test #16: measure ONLY the visible portrait row. The InitiativeTrack root
    /// canvas is a full 1920x1080 window whose union of visible graphics measured
    /// ~1714x959 px — siblings of the row (the <c>enemyCardsHolder</c> card-reveal
    /// area, the <c>enemyCardsBlocker</c> raycast blocker, the <c>helpBox</c>,
    /// gamepad hotkey tips) span nearly the whole screen, and dock-fitting that
    /// rect made the actually-visible row minuscule on the tray. The game's own
    /// serialized <c>initiativeTrackHolder</c> (publicized field; verified in
    /// decompiled/GH.Runtime/InitiativeTrack.cs — the HorizontalLayoutGroup +
    /// ContentSizeFitter holder every avatar is pooled under, incl. their '?'
    /// digit labels) IS the row by construction, so scoping the content fit to it
    /// excludes the fullscreen junk by rule, not by name matching.
    /// </summary>
    protected override void OnConverted()
    {
        if (Panel != null && InitiativeTrack.Instance != null)
            Panel.FitContentRoot = InitiativeTrack.Instance.initiativeTrackHolder as RectTransform;

        // A brand-new host starts at the conversion rect and MUST get one real measurement before
        // the hover-stability hold freezes it (see the FitSettleSeconds docs). Arming here rather
        // than relying on the layout signature covers every conversion path — including a re-dock
        // onto a track whose signature is unchanged, where the signature alone would arm nothing.
        _fitSignature = -1;
        _fitArmedUntil = Time.unscaledTime + FitSettleSeconds;

        // Register per-portrait depth-aware laser picking against the live host canvas
        // (user #3 follow-up). RayUguiDriver intersects (and hands UguiPointer) exactly
        // this HostCanvas, so keying the picker on it scopes the depth path to this panel.
        if (Panel != null)
        {
            _depthPickHost = Panel.HostCanvas;
            DepthPortraitPicks.Register(_depthPickHost, this);
        }

        LogTrackTextureDiag();

        // Aliasing round 4: first swap right at conversion (whatever art is already in), then
        // the Tick cadence keeps re-asserting as pooled avatars and async portraits arrive.
        _nextMipRescan = Time.unscaledTime + MipRescanInterval;
        PanelMipBake.Rescan(InitiativeTrack.Instance, Name);
    }

    // ---- rendered-texture diagnostics (aliasing round 3) --------------------------------

    /// <summary>Latched once a conversion had portrait textures to report (avatars are
    /// pooled in over the first frames — retry on later conversions until one does).</summary>
    private static bool s_texDiagLogged;

    /// <summary>
    /// One-line diag for the initiative-track shimmer investigation (user report: MSAA
    /// helps the board but NOT the initiative track). The portraits/frames are GAME
    /// sprites on an adopted world-space canvas — their edge shimmer is TEXTURE-space,
    /// which MSAA (geometry edges only) cannot touch. Log the biggest source textures'
    /// mip/aniso/filter state once per session so the hardware log PROVES whether mips
    /// exist: mipmapCount == 1 ⇒ the atlas ships MIPLESS, minification shimmer is baked
    /// into the data, and the honest lever is eye-texture supersampling
    /// ([RenderQuality] EyeResolutionScale, e.g. 1.3).
    /// </summary>
    private void LogTrackTextureDiag()
    {
        if (s_texDiagLogged || InitiativeTrack.Instance == null)
            return;
        try
        {
            var seen = new HashSet<int>();
            var sb = new System.Text.StringBuilder(256);
            int count = 0;
            Image[] images = InitiativeTrack.Instance.GetComponentsInChildren<Image>(includeInactive: false);
            foreach (Image img in images)
            {
                Sprite? sprite = img != null ? img.sprite : null;
                Texture2D? tex = sprite != null ? sprite.texture : null;
                if (tex == null || !seen.Add(tex.GetInstanceID()))
                    continue;
                if (count > 0)
                    sb.Append(", ");
                sb.Append('\'').Append(tex.name).Append("' ").Append(tex.width).Append('x')
                  .Append(tex.height).Append(" mips=").Append(tex.mipmapCount)
                  .Append(" aniso=").Append(tex.anisoLevel).Append(' ').Append(tex.filterMode);
                if (++count >= 8)
                    break;
            }
            if (count == 0)
                return; // portraits not pooled in yet — retry next conversion
            s_texDiagLogged = true;
            VRLog.Info("WorldUI", "INITIATIVE TEXTURE DIAG (game sprites the adopted track samples): " +
                                  $"{sb} — mips=1 ⇒ MIPLESS source data: MSAA/aniso cannot stop the " +
                                  "shimmer. [WorldUI] PanelMipBake now swaps these onto mip-baked " +
                                  "trilinear/aniso copies — see the MIP BAKE lines for what was baked.");
        }
        catch (System.Exception ex)
        {
            s_texDiagLogged = true; // never spam a throwing path
            VRLog.Warn("WorldUI", $"Initiative texture diag skipped ({ex.Message}).");
        }
    }

    // ---- per-portrait pick geometry (user #3 follow-up) -------------------------------
    private static readonly Vector3[] PickCorners = new Vector3[4];

    /// <summary>
    /// Depth-aware laser pick over the initiative portraits (<see cref="IDepthPortraitPicker"/>).
    /// Each portrait is a direct child of <c>initiativeTrackHolder</c> carrying its own
    /// (normalized) local z, so its world rect sits at the portrait's ACTUAL position and
    /// depth. We intersect the true aim ray with each active portrait's world rect and
    /// return the NEAREST hit's clickable <c>avatarButton</c> — so pointing at a portrait
    /// selects THAT portrait, respecting its height/depth, where the flat host-plane pick
    /// projected onto a depth-displaced neighbour. Non-portrait siblings under the holder
    /// (the gamepad hotkey tips) carry no <c>InitiativeTrackActorBehaviour</c>/button and
    /// are skipped by rule. Ray misses fall back to the ordinary flat pick in UguiPointer.
    /// </summary>
    bool IDepthPortraitPicker.TryPickPortrait(Vector3 rayOrigin, Vector3 rayDirection,
        out GameObject target, out Vector3 worldHit)
    {
        target = null!;
        worldHit = default;

        Transform? holder = InitiativeTrack.Instance != null
            ? InitiativeTrack.Instance.initiativeTrackHolder
            : null;
        if (holder == null)
            return false;

        float bestDist = float.PositiveInfinity;
        foreach (Transform child in holder)
        {
            if (!child.gameObject.activeSelf || child is not RectTransform rect)
                continue;
            InitiativeTrackActorBehaviour behaviour = child.GetComponent<InitiativeTrackActorBehaviour>();
            if (behaviour == null || behaviour.avatarButton == null)
                continue; // not a portrait (hotkey tip / separator) — no click target
            GameObject buttonGo = behaviour.avatarButton.gameObject;
            if (!buttonGo.activeInHierarchy)
                continue;

            // Portrait cell world rect (child inherits the stepped z → real depth).
            if (TryIntersectRect(rect, rayOrigin, rayDirection, bestDist, out float dist, out Vector3 point))
            {
                bestDist = dist;
                target = buttonGo;
                worldHit = point;
            }
        }

        return target != null;
    }

    /// <summary>
    /// Ray ∩ a portrait's world rect via its actual WORLD-SPACE corners (mirrors
    /// RayUguiDriver.TryIntersect's host-plane math, applied per portrait): the plane is
    /// spanned by the real corners at the portrait's true position/depth, so a hit means
    /// the beam geometrically lands inside that portrait. Front-side only (uGUI faces
    /// -normal); a nearer portrait wins via the shrinking <paramref name="maxDist"/>.
    /// </summary>
    private static bool TryIntersectRect(RectTransform rect, Vector3 origin, Vector3 direction,
        float maxDist, out float dist, out Vector3 point)
    {
        dist = 0f;
        point = default;

        rect.GetWorldCorners(PickCorners); // 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 right = PickCorners[3] - PickCorners[0];
        Vector3 up = PickCorners[1] - PickCorners[0];
        float rightLen2 = right.sqrMagnitude;
        float upLen2 = up.sqrMagnitude;
        if (rightLen2 < 1e-12f || upLen2 < 1e-12f)
            return false; // degenerate / not laid out yet

        Vector3 normal = Vector3.Cross(right, up).normalized;
        float denom = Vector3.Dot(direction, normal);
        if (denom < 1e-5f)
            return false; // parallel or back-side

        float d = Vector3.Dot(PickCorners[0] - origin, normal) / denom;
        if (d <= 0f || d >= maxDist)
            return false;

        Vector3 p = origin + direction * d;
        Vector3 rel = p - PickCorners[0];
        float u = Vector3.Dot(rel, right) / rightLen2;
        float v = Vector3.Dot(rel, up) / upLen2;
        if (u < 0f || u > 1f || v < 0f || v > 1f)
            return false;

        dist = d;
        point = p;
        return true;
    }
}

/// <summary>
/// Element infusion board, docked in the tray dashboard's LEFT column below the
/// objectives panel (test #20; the free-floating world panel is gone — it hung over
/// the table as an orphaned 'circle with squiggles'; floating slot layout only as
/// the no-tray fallback). Behaves exactly like the objectives dock: pose-follows
/// its mount, shared tray density, moves/scales/pins with the tray.
/// Verified: <c>public class InfusionBoardUI : MonoBehaviour</c> with
/// <c>public static InfusionBoardUI Instance { get; private set; }</c>.
/// </summary>
internal sealed class ElementBoardSurface : TrayMountedPanelSurface
{
    public override string Name => "ElementBoard";
    // ALWAYS ON (user ruling 2026-08-13): the [WorldUI] ElementBoard dial is gone — the
    // element infusions are state you must read to spend them.
    protected override bool ConfigEnabled => true;
    protected override PanelSlot Slot => PanelSlot.ElementBoard;
    protected override Transform? Mount => PlayTray.Current?.ElementMount;
    protected override float MountWidth => PlayTray.ElementMountWidth;
    protected override float MountMaxHeight => PlayTray.ElementMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.left; // right edge on the mount, same column as the objectives

    /// <summary>
    /// The element icons are glanced at from board distance, not read like text —
    /// at the shared density they render smaller than that job warrants. 0.8×
    /// draws them 1.25× bigger; the dock fit clamp still bounds the board to its
    /// mount budget like every docked panel.
    /// </summary>
    protected override float DensityScale => 0.8f;

    protected override RectTransform? FindTarget() =>
        InfusionBoardUI.Instance != null ? InfusionBoardUI.Instance.transform as RectTransform : null;

    /// <summary>
    /// Scope the content fit to the game's own serialized <c>elementsHolder</c>
    /// (publicized field; verified in decompiled/GH.Runtime/InfusionBoardUI.cs —
    /// every <c>InfusionElementUI</c> is instantiated under it in Awake), the same
    /// by-construction rule the initiative track uses: fullscreen siblings on the
    /// root canvas can never leak into the measured rect.
    /// </summary>
    protected override void OnConverted()
    {
        if (Panel != null && InfusionBoardUI.Instance != null)
            Panel.FitContentRoot = InfusionBoardUI.Instance.elementsHolder as RectTransform;
    }
}

/// <summary>
/// Scenario objectives ('das Ziel'), docked to the tray dashboard's LEFT edge
/// (test #15; floating slot layout only as fallback).
/// Verified: <c>UIManager.MissionObjectiveContainer</c> property
/// (<c>public MissionObjectiveContainer MissionObjectiveContainer =&gt;
/// missionObjectiveContainer;</c>) — populated by <c>UIManager.InitScenario</c>.
/// The container's own rect is ZERO-sized (its objective entries overflow it), so
/// it converts via the 100 px placeholder path — the content fit measures the
/// real text bounds through <see cref="ConvertedPanel.FitFrameDegenerate"/>
/// (test #16: a 100 px host stretched to the dock made the text giant).
/// </summary>
internal sealed class ObjectivesSurface : TrayMountedPanelSurface
{
    public override string Name => "Objectives";
    // ALWAYS ON (user ruling 2026-08-13): the [WorldUI] Objectives dial is gone — the
    // scenario goal is what the whole scenario is for.
    protected override bool ConfigEnabled => true;
    protected override PanelSlot Slot => PanelSlot.Objectives;
    protected override Transform? Mount => PlayTray.Current?.ObjectivesMount;

    /// <summary>
    /// Task-panel WIDTH ('Breite' in the debug menu): the base
    /// <see cref="PlayTray.ObjectivesMountWidth"/> scaled by the per-board <c>ObjectivesWidth</c>
    /// multiplier. Unlike every other docked panel this is NOT a fit ceiling — it is the WRAP COLUMN
    /// itself: <see cref="ApplyContentWidth"/> forces it onto the objectives container root as a
    /// pixel width, so the text re-wraps into it and the progress bars stretch to it. The panel
    /// grows LEFTWARD from the board edge into open space (GrowDirection = left ⇒ no board overlap /
    /// run-off). Read live each tick, so a debug-menu change re-wraps next frame — no mount rebuild.
    ///
    /// Because the width budget is FORCED rather than measured, it must not also feed the uniform
    /// dock fit — that is what made 'Breite' behave like 'Größe'. See <see cref="FitWidthToMount"/>.
    /// </summary>
    protected override float MountWidth =>
        PlayTray.ObjectivesMountWidth * CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard).Value;
    protected override float MountMaxHeight => PlayTray.ObjectivesMountMaxHeight;
    protected override Vector2 GrowDirection => Vector2.left; // right edge on the mount

    /// <summary>
    /// WIDTH AND SIZE ARE SEPARATE DIALS HERE (user, round 4: "Bei der Breite will ich wirklich nur
    /// die Breite einstellen, ohne die ganze Größe zu verändern").
    ///
    /// ROOT CAUSE of the coupling — see the full derivation on
    /// <see cref="TrayMountedPanelSurface.FitWidthToMount"/>: the shared dock fit is UNIFORM, and
    /// its width term divides the width budget by the MEASURED content width. Since round 3 this
    /// panel FORCES its content to exactly that budget, so the term degenerated to
    /// <c>wantPx / (wantPx + c)</c> with <c>c</c> = the constant 94 px header overhang — a fraction
    /// that climbs toward 1 as the budget grows. Every 'Breite' click therefore also re-scaled the
    /// glyphs (hardware log: 0.8× → 0.9× moved the host lossyScale 0.0122 → 0.0137, +12 %), and at
    /// the low end it squeezed them back down ("so gestaucht wie zuvor"). The dial was, in effect, a
    /// second size dial with a wrap side-effect.
    ///
    /// THE DECOUPLING: drop the width term for this panel. The content is written to the budget by
    /// construction, so nothing about the width still needs fitting; what remains is the HEIGHT
    /// budget alone as an overflow guard. Metres-per-pixel is then <c>1 / density</c> — a CONSTANT
    /// (0.694 mm/px at the shared 2400 px/m × this panel's 0.6 DensityScale) that no width change
    /// can move. Wider budget ⇒ the same glyphs re-wrap into fewer, longer lines and the panel
    /// extends further left; narrower ⇒ more, shorter lines, same glyphs. Overall SIZE stays with
    /// <c>ObjectivesScale</c>, which is the objectives MOUNT's localScale (PlayTray.BuildMounts) and
    /// multiplies the whole panel through <c>mount.lossyScale</c> — a completely different factor in
    /// <see cref="TrayMountedPanelSurface.Place"/>, so the two dials can no longer touch each other.
    ///
    /// The height guard cannot smuggle the coupling back in at any realistic content size: the
    /// budget is 0.32 m × 1440 px/m = 461 px and the hardware log settles this panel at 80–98 px
    /// tall — even the narrowest 0.5× setting (≈ 187 px column, roughly double the line count) stays
    /// far below it, so the guard only ever engages for a pathological measurement, where a bounded
    /// panel beats a board-covering one.
    ///
    /// Side effect, deliberate: with the fit at 1 instead of ~0.8, the objectives render at the size
    /// they had BEFORE the width forcing existed — back then the fit saturated at
    /// <c>MaxDensityScale</c> = 1, which is the same metres-per-pixel. This restores the test #17
    /// approved type size rather than inventing a new one.
    /// </summary>
    protected override bool FitWidthToMount => false;

    /// <summary>Last objectives width budget we logged (change-gated so a per-tick re-fit stays quiet).</summary>
    private float _loggedWidth = -1f;

    protected override void Place()
    {
        // Log the applied objectives width budget on (re)layout — once, and again whenever the
        // debug-menu multiplier changes — so a "the task/progress bar is too narrow" report is
        // diagnosable from the hardware log alone (Info: BepInEx's default disk config drops Debug).
        // NOTE: this line only proves the BUDGET moved. Whether anything VISIBLE moved is proven by
        // the 'OBJECTIVES WIDTH' / 'OBJECTIVES WIDTH APPLIED' lines (ApplyContentWidth) — the old
        // hardware log had 31 of these budget lines sweeping 364→624 mm and NOT ONE re-logged
        // 'Docked GloomhavenVR.Panel_Objectives' rect in between, which is exactly the bug.
        float w = MountWidth;
        bool budgetChanged = Mathf.Abs(w - _loggedWidth) > 1e-4f;
        base.Place(); // re-fits first, so the glyph scale below is THIS tick's applied value
        if (budgetChanged)
        {
            _loggedWidth = w;
            VRLog.Info("WorldUI", $"Objectives dock width budget {w * 1000f:F0} mm " +
                                  $"({CardsConfig.ObjectivesWidth(CardsConfig.CurrentBoard).Value:F2}× base " +
                                  $"{PlayTray.ObjectivesMountWidth * 1000f:F0} mm) — forced onto the objective " +
                                  "rows as a pixel width; see the 'OBJECTIVES WIDTH' lines. Glyph scale is " +
                                  $"{AppliedMetersPerPixel * 1000f:F4} mm/px (fit {AppliedFitScale:F3}) and MUST " +
                                  "NOT move with this budget — 'Größe' (ObjectivesScale) owns the size.");
        }
    }

    // ---- forced CONTENT width: the real lever behind the 'Breite' dial ---------------------

    /// <summary>
    /// ROOT CAUSE of the user report "die Aufgabenbreite hat immer noch überhaupt keinen Einfluss"
    /// (third attempt) — this time established from the game's own PREFABS instead of guessed.
    ///
    /// THE ACTUAL HIERARCHY (UnityPy dump of GH_Data; row prefab 'UI Mission Objective' in
    /// sharedassets6.assets, container 'Mission Objective Container' in level6):
    /// <code>
    /// Mission Objective Container   RectTransform + VerticalLayoutGroup(ctrlW=1, expandW=1, pad 0)
    ///                               anchors (0,0)-(0,0)  sizeDelta (0,0)   ← MissionObjectiveContainer
    ///   UI Quest Type               LayoutElement(prefH 36)                ← questHeader
    ///   Container                   RectTransform + VerticalLayoutGroup(ctrlW=1, expandW=1, padL 11)
    ///                               anchors (0,0)-(0,0)  sizeDelta (0,0)   ← objectiveContainer
    ///     UI Mission Objective (×n) HorizontalLayoutGroup(ctrlW=1, expandW=1, padL 25, padR 5)
    ///                             + ContentSizeFitter(horizontal = Unconstrained, vertical = Preferred)
    ///                               anchors (0,0)-(0,0)  sizeDelta (503,0)
    ///       Image Progress bar      anchors (0,0)-(1,1) sizeDelta (-22.5,0), LayoutElement.ignore=1
    ///       Check                   20×20, LayoutElement.ignore=1
    ///       TextMeshPro Text        anchors (0,0)-(0,0) sizeDelta (0,0), NO LayoutElement  ← the text
    ///       Space                   LayoutElement(minW 82)
    ///       Marker                  36×36, LayoutElement.ignore=1
    /// </code>
    ///
    /// So the wrap column is NOT authored anywhere — it is DERIVED, top-down, by three nested
    /// layout groups: <c>MissionObjectiveContainer</c>'s VerticalLayoutGroup drives
    /// <c>objectiveContainer</c>'s width, which drives every row's width, whose
    /// HorizontalLayoutGroup finally hands the leftover to 'TextMeshPro Text'
    /// (≈ rowWidth − 11 − 25 − 5 − 82). The row's own ContentSizeFitter is horizontally
    /// UNCONSTRAINED, so a row NEVER sizes itself — it is always the parent's width. The progress
    /// bar is stretch-anchored (0→1) with a −22.5 inset, so its length is the row width, minus a
    /// constant, for free. ONE rect therefore decides the whole column: the container ROOT.
    ///
    /// WHY IT WAS 0 PX WIDE (the "0 full-width rect(s)" in the last hardware log): that root's
    /// authored rect is literally (0,0) — its width normally comes from ITS parent's layout in the
    /// game's 2D HUD. Conversion re-parents it under the world-space host, where nothing drives it
    /// any more, so <see cref="CanvasConversion"/> falls back to its degenerate-rect placeholder:
    /// <c>size = max(size, 100)</c> ⇒ the container converts 100 px WIDE (LogOutput.log:250,
    /// "Docked 'GloomhavenVR.Panel_Objectives' … (100x100 px)"). 100 px cascades down to a
    /// ~89 px row, and 89 − 11 − 30 − 82 leaves the text a NEGATIVE budget, i.e. its layout minimum
    /// — which is exactly the "ONE WORD PER LINE" in .planning/debug/position_gegnerinfo.png.
    ///
    /// WHY THE PREVIOUS FIX WROTE NOTHING: it scaled against <c>Panel.HostRect.rect.width</c> = 194
    /// px (the fitted union of visible GRAPHICS — bigger than the 100 px container because the quest
    /// header's Background stretches 70 px past it) and then only accepted rects between 0.6× and 4×
    /// of that, i.e. 116…776 px. The real rects are 100 px (container), 89 px (row), &lt;60 px
    /// (text) — ALL BELOW THE 116 px FLOOR — and the progress bar is stretch-anchored, which the
    /// filter skipped by design. Every single candidate was rejected: "0 full-width rect(s)", no
    /// write, no visible change. The 194 px base was never the wrap column; it was a symptom.
    ///
    /// THE FIX — widen the ONE rect the column is derived from: <c>Panel.Target</c>, i.e.
    /// <c>MissionObjectiveContainer</c>'s own RectTransform. After conversion its anchors are
    /// collapsed to the host centre (CanvasConversion.Convert), so its width IS its
    /// <c>sizeDelta.x</c> and nothing else drives it — writing it sticks. The two VerticalLayoutGroups
    /// then propagate the new width to the list and to every row, the row's HorizontalLayoutGroup
    /// re-hands the leftover to the TMP text (TMP re-wraps at the wider measure), and the
    /// stretch-anchored progress bar gets longer. That is the exact OPPOSITE of the old pass, which
    /// skipped stretched rects and DISABLED <c>childControlWidth</c> — i.e. it dismantled the very
    /// mechanism that carries the width downward. Nothing below the root is touched any more, so
    /// icons, check marks and digits cannot distort.
    ///
    /// The dial also becomes literal — but ONLY once the dock fit is kept out of the width axis.
    /// The shared fit computes <c>fitScale = MountWidth·density / contentW</c> and clamps it to
    /// [0.5, 1]; with the content forced to <c>wantPx = MountWidth·density</c> the measured union is
    /// <c>wantPx + c</c> (c = the constant 94 px header overhang, hardware log: forced 374 px →
    /// settled 468 px). That fraction is NOT constant — it is <c>1/(1 + c/wantPx)</c>, which climbs
    /// with the budget (0.84 at 0.8×, 0.95 at 0.9×) — so round 3 shipped a width dial that also
    /// re-scaled the text, which is exactly the round-4 report "Breite und Größe verhalten sich
    /// gleich". Round 4 therefore overrides <see cref="FitWidthToMount"/> to false: the forced width
    /// needs no fitting (it IS the budget), metres-per-pixel collapses to the constant
    /// <c>1/density</c>, and the budget shows up purely as horizontal extent — the wrap column, the
    /// row width and the stretch-anchored progress bar — at unchanged glyph size. (Two rounds ago,
    /// before any forcing, fitScale was 2.7 — 194 px content vs a 524 px budget — saturated at the
    /// clamp, so the applied geometry was a constant: 31 budget lines swept 364→624 mm in
    /// LogOutput.log with the panel frozen at 194×164 px / 2.305 m.)
    ///
    /// REVERSIBILITY: the container's <c>sizeDelta.x</c> as the conversion installed it is recorded
    /// at capture and written back in <see cref="RestoreContentWidth"/> — but ONLY while the live
    /// value is still the one WE wrote. <c>CanvasConversion.Release</c> restores the pristine 2D
    /// <c>OriginalSizeDelta</c> and runs BEFORE our released-this-tick restore, so that guard is what
    /// keeps us from stomping the game's authored rect back to the conversion placeholder.
    ///
    /// MULTIPLAYER: purely local presentation on the local player's HUD — no game state, no
    /// simulation input, nothing serialized. Remote clients are unaffected by construction.
    /// </summary>
    /// <remarks>Delay before the applied-width verification / subtree dump (lets layout settle).</remarks>
    private const float WidthVerifyDelay = 1.5f;

    /// <summary>Hard cap on the one-shot subtree dump so a pathological tree cannot flood the log.</summary>
    private const int WidthDumpMaxRects = 64;

    /// <summary>The rect we widen (the converted <c>MissionObjectiveContainer</c> root).</summary>
    private RectTransform? _widthLever;

    /// <summary>Its <c>sizeDelta.x</c> as captured — handed back verbatim on restore.</summary>
    private float _widthAuthoredX;

    /// <summary>The last <c>sizeDelta.x</c> WE wrote — the guard that makes the restore safe.</summary>
    private float _widthForcedX;

    /// <summary>A horizontal ContentSizeFitter on the lever itself would drive our write away.</summary>
    private ContentSizeFitter? _widthFitter;
    private ContentSizeFitter.FitMode _widthFitterMode;

    private bool _widthCaptured;
    private float _widthAppliedPx = -1f;
    private float _widthVerifyAt;
    private bool _widthDumped;
    private float _widthDumpAt;
    private static bool s_widthErrorLogged;

    /// <summary>
    /// The game's own serialized objective LIST holder (publicized
    /// <c>MissionObjectiveContainer.objectiveContainer</c> — every <c>MissionObjectiveUI</c> is
    /// instantiated under it, decompiled GH.Runtime/MissionObjectiveContainer.cs:123). Only used to
    /// know WHEN rows exist (for the one-shot subtree dump) — the width lever is its PARENT, see
    /// <see cref="ApplyContentWidth"/>.
    /// </summary>
    private RectTransform? ObjectiveList()
    {
        UIManager manager = UIManager.Instance;
        MissionObjectiveContainer? container = manager != null ? manager.MissionObjectiveContainer : null;
        return container != null ? container.objectiveContainer as RectTransform : null;
    }

    /// <summary>
    /// Force the configured width budget onto the objectives container root (see the big
    /// <see cref="WidthVerifyDelay"/> comment for the hierarchy evidence, the root cause and the
    /// full contract). Runs every tick while converted: two float comparisons in the steady state,
    /// and idempotent by construction — the target is derived from the CONFIG, never from the live
    /// (already widened) width, so re-running can never compound.
    /// </summary>
    /// <summary>Next unscaled time <see cref="StyleObjectivesText"/> may sweep the objectives
    /// subtree, and how many labels the last sweep had to relieve.</summary>
    private float _nextObjectivesStyleAt;

    private int _objectivesStyled;

    /// <summary>Sweep cadence. The game rebuilds these rows on every objective change and on every
    /// language change, and a rebuilt row is a fresh TMP with a fresh shared material — so this
    /// cannot be a one-shot. Four times a second over a subtree of ~10 nodes is nothing; per frame
    /// would be a per-frame <c>GetComponentsInChildren</c> allocation, which is the shape of defect
    /// this project has shipped three times.</summary>
    private const float ObjectivesStyleInterval = 0.25f;

    /// <summary>
    /// LEGIBILITY RELIEF ON THE GAME'S OWN OBJECTIVES TEXT (user request 5, 2026-09-03).
    ///
    /// <para><b>WHICH TEXT.</b> "Versengter Gipfel / - Tötet den Drakenfürsten." in his screenshot
    /// is not drawn by this mod at all — it is the game's own <c>TextMeshProUGUI</c> rows inside the
    /// objectives canvas this surface re-hosts in world space. Until now the mod touched only their
    /// container's WIDTH. Measured off text-board.jpg with the glyphs masked out, that line runs at
    /// 17.28:1 over the black leaf litter and 7.90:1 against the worst ground it crosses — so in
    /// THIS screenshot it is not the failing one, and saying so is the point: the relief is applied
    /// here for consistency with the three labels beside it and because a scenario with a bright
    /// floor puts this row in the same position the battle goal is already in, NOT because a
    /// measurement caught it failing. The measurement that did catch a failure is on the battle
    /// goal (2.24:1) and, far worse, on the board's own engraved captions (1.64:1).</para>
    ///
    /// <para><b>WHICH SIDE OF THE 1:1 RULE THIS IS ON, AND IT IS THE OWNER'S.</b>
    /// <c>Net.Remote.RemoteObjectivesPanel</c> mirrors this panel by <c>Object.Instantiate</c> of
    /// the owner's live subtree (<c>RemoteWidgetMirror</c>: "the source still owns every glyph,
    /// colour, sprite and progress fill"), so a relief written HERE is carried to every peer by the
    /// clone itself — no wire field, no second rule to keep in step. The mirror applies the SAME
    /// constant recipe on its own clone as well, and that is not a viewer dial being ANDed with an
    /// owner dial: there is no dial. It is one fixed constant applied on both sides so that a clone
    /// taken before the owner's font landed cannot end up showing a different picture from the one
    /// the owner is looking at.</para>
    ///
    /// <para><b>WHY IT WRITES TO A GAME-OWNED COMPONENT AND WHY THAT IS SAFE.</b> TMP's
    /// <c>fontMaterial</c> accessor mints a PER-LABEL material instance the first time it is read;
    /// the game's shared font asset is never touched, and the mod already relies on exactly this
    /// for every label it owns (<c>NativeButtonSkin.MakeLabelDepthHonest</c>). Nothing is written
    /// to the label's text, colour, size or layout — the row is the game's row, with a rim.</para>
    /// </summary>
    private void StyleObjectivesText()
    {
        if (Time.unscaledTime < _nextObjectivesStyleAt)
            return;
        _nextObjectivesStyleAt = Time.unscaledTime + ObjectivesStyleInterval;
        RectTransform? lever = Panel?.Target;
        if (lever == null)
            return;
        int styled = 0;
        foreach (TMP_Text t in lever.GetComponentsInChildren<TMP_Text>(includeInactive: true))
        {
            if (NativeButtonSkin.HasWorldReadableRelief(t))
                continue;
            NativeButtonSkin.StyleWorldReadableLabel(t);
            if (NativeButtonSkin.HasWorldReadableRelief(t))
                styled++;
        }
        if (styled <= 0 || styled == _objectivesStyled)
            return;
        _objectivesStyled = styled;
        // HW-VERIFY
        VRLog.Note("WorldUI", $"OBJECTIVES RELIEF: {styled} of the game's own objectives labels " +
            "under '" + lever.name + "' were re-lettered with the world-readable keyline this " +
            "sweep (see the WORLD-LABEL LEGIBILITY line for the recipe and the measured contrast). " +
            "Change-gated on the COUNT, so this re-prints when the game rebuilds the rows and goes " +
            "quiet while it does not — a repeat of the same number is the panel being rebuilt at " +
            "the same size, a rising number is rows being added, and SILENCE after the first line " +
            "is the steady state. Zero lines at all means the sweep found no TMP under that " +
            "container, which is the objectives panel not being converted rather than the relief " +
            "failing.");
    }

    private void ApplyContentWidth()
    {
        if (Panel == null)
            return;

        try
        {
            RectTransform lever = Panel.Target;
            if (lever == null)
                return;

            // A re-conversion hands us a different rect (or the same rect reset to the 100 px
            // placeholder): drop the stale record so the next capture reads the fresh authored one.
            if (_widthCaptured && !ReferenceEquals(lever, _widthLever))
                RestoreContentWidth();
            if (!_widthCaptured)
                CaptureContentWidth(lever);

            float density = PlayTray.TrayPixelsPerMeter * DensityScale;
            float wantPx = MountWidth * density;

            bool changed = false;

            // Re-assert every tick (change-gated): a ContentSizeFitter DRIVES SizeDeltaX, so one
            // game-side rebuild with it re-enabled would silently stomp the width we wrote and the
            // dial would look dead again. Vertical fitting is untouched — the rows must keep growing
            // in height as the text re-wraps.
            if (_widthFitter != null && _widthFitter.horizontalFit != ContentSizeFitter.FitMode.Unconstrained)
            {
                _widthFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                changed = true;
            }

            if (Mathf.Abs(lever.sizeDelta.x - wantPx) > 0.5f)
            {
                lever.sizeDelta = new Vector2(wantPx, lever.sizeDelta.y);
                _widthForcedX = wantPx;
                changed = true;
            }

            // Arm the one-shot subtree dump as soon as the game has actually instantiated rows —
            // dumping an empty list would name nothing (the count-only diagnostic is what cost the
            // last two rounds).
            RectTransform? list = ObjectiveList();
            if (!_widthDumped && _widthDumpAt <= 0f && list != null && list.childCount > 0)
                _widthDumpAt = Time.unscaledTime + WidthVerifyDelay;

            if (!changed)
                return;

            // Re-wrap NOW: the layout groups re-run top-down and TMP re-measures at the new column,
            // so the objectives host is already the right size when the central TickFit next runs.
            LayoutRebuilder.ForceRebuildLayoutImmediate(lever);

            if (Mathf.Abs(wantPx - _widthAppliedPx) > 0.5f)
            {
                _widthAppliedPx = wantPx;
                _widthVerifyAt = Time.unscaledTime + WidthVerifyDelay;
                VRLog.Info("WorldUI", $"OBJECTIVES WIDTH: container root '{lever.name}' forced to " +
                                      $"{wantPx:F0} px (was {_widthAuthoredX:F0} px authored) for the " +
                                      $"{MountWidth * 1000f:F0} mm budget at {density:F0} px/m — the two " +
                                      "VerticalLayoutGroups carry it to every row, the row's " +
                                      "HorizontalLayoutGroup re-hands the leftover to the TMP text.");
            }
        }
        catch (System.Exception ex)
        {
            // A game-side surprise must never starve the WorldUI tick (the unguarded-Update lesson):
            // hand the rect back, stop trying, and log once.
            if (!s_widthErrorLogged)
            {
                s_widthErrorLogged = true;
                VRLog.Warn("WorldUI", $"OBJECTIVES WIDTH: forcing the task width failed ({ex.Message}) — " +
                                      "the objectives keep their authored layout.");
            }
            RestoreContentWidth();
        }
    }

    /// <summary>
    /// Record the lever's PRISTINE horizontal state — the <c>sizeDelta.x</c> the conversion
    /// installed, plus a horizontal ContentSizeFitter if one ever appears on it — so
    /// <see cref="RestoreContentWidth"/> can put it back exactly. Nothing below the root is
    /// recorded because nothing below the root is written: every descendant width is DERIVED by the
    /// game's own layout groups (see the hierarchy in the <see cref="WidthVerifyDelay"/> comment).
    /// </summary>
    private void CaptureContentWidth(RectTransform lever)
    {
        _widthLever = lever;
        _widthAuthoredX = lever.sizeDelta.x;
        _widthForcedX = float.NaN; // nothing written yet — the restore guard must not match
        _widthFitter = lever.GetComponent<ContentSizeFitter>();
        _widthFitterMode = _widthFitter != null
            ? _widthFitter.horizontalFit
            : ContentSizeFitter.FitMode.Unconstrained;
        _widthAppliedPx = -1f; // force the next apply to log against the fresh capture
        _widthDumped = false;
        _widthDumpAt = 0f;
        _widthCaptured = true;
    }

    /// <summary>
    /// Hand the objectives container its captured horizontal layout back and forget the capture.
    /// Called on release, on shutdown, on re-conversion and on any failure, so the game UI is never
    /// left modified (the framework's root-only restore in <c>CanvasConversion.Release</c> restores
    /// the pristine 2D rect, which is a DIFFERENT value than the conversion placeholder we captured).
    ///
    /// The write is guarded on the live value still being the one we forced: on a release
    /// <c>CanvasConversion.Release</c> has ALREADY restored the pristine 2D <c>OriginalSizeDelta</c>
    /// by the time this runs, and writing our captured placeholder over it would leave the game's
    /// own HUD container 100 px wide for the rest of the session.
    /// </summary>
    private void RestoreContentWidth()
    {
        if (_widthCaptured && _widthLever != null
            && !float.IsNaN(_widthForcedX)
            && Mathf.Abs(_widthLever.sizeDelta.x - _widthForcedX) <= 0.5f)
        {
            _widthLever.sizeDelta = new Vector2(_widthAuthoredX, _widthLever.sizeDelta.y);
        }
        if (_widthFitter != null)
            _widthFitter.horizontalFit = _widthFitterMode;

        _widthLever = null;
        _widthFitter = null;
        _widthCaptured = false;
        _widthAuthoredX = 0f;
        _widthForcedX = float.NaN;
        _widthAppliedPx = -1f;
        _widthVerifyAt = 0f;
        _widthDumpAt = 0f;
        _widthDumped = false;
    }

    /// <summary>
    /// Proof lines for the next hardware log. Two of them, on purpose:
    /// <list type="bullet">
    /// <item>'OBJECTIVES WIDTH APPLIED' — the SETTLED host rect in pixels and metres a moment after
    ///   a width change. Grep it and compare two consecutive entries: if the dial works, both
    ///   numbers move with it. (The very first diagnostic only proved the BUDGET moved, which is
    ///   why a dead dial looked alive in the log for two rounds.)</item>
    /// <item>'OBJECTIVES TREE' — a ONE-SHOT dump of the real rect chain (see
    ///   <see cref="LogObjectiveSubtree"/>). If the width STILL does not move, this line names the
    ///   rect that ate it instead of leaving the next round to guess again.</item>
    /// </list>
    /// </summary>
    private void LogWidthVerification()
    {
        if (Panel == null)
            return;

        float now = Time.unscaledTime;

        if (_widthVerifyAt > 0f && now >= _widthVerifyAt)
        {
            _widthVerifyAt = 0f;
            Rect px = Panel.HostRect.rect;
            Panel.HostRect.GetWorldCorners(QuestCorners); // 0=BL, 1=TL, 2=TR, 3=BR
            float w = (QuestCorners[3] - QuestCorners[0]).magnitude;
            float h = (QuestCorners[1] - QuestCorners[0]).magnitude;
            // DECOUPLING PROOF, in one line: the width numbers (px / world rect) MUST move with
            // 'Breite', while 'glyph scale' MUST stay at 1/density (0.694 mm/px at 1440 px/m) for
            // the whole dial range — that is the difference between a width dial and a size dial.
            // Only 'Größe' (ObjectivesScale, the mount's localScale) may move the world scale.
            VRLog.Info("WorldUI", $"OBJECTIVES WIDTH APPLIED: content settled at {px.width:F0}x{px.height:F0} px " +
                                  $"(forced {_widthAppliedPx:F0} px, budget {MountWidth * 1000f:F0} mm) — " +
                                  $"world rect {w:F3}x{h:F3} m, glyph scale " +
                                  $"{AppliedMetersPerPixel * 1000f:F4} mm/px (fit {AppliedFitScale:F3}, " +
                                  $"world {Panel.HostTransform.localScale.x * 1000f:F4} mm/px incl. tray+size). " +
                                  "The width numbers MUST move when 'Breite' changes; the glyph scale MUST NOT.");
        }

        if (!_widthDumped && _widthDumpAt > 0f && now >= _widthDumpAt)
        {
            _widthDumpAt = 0f;
            _widthDumped = true;
            if (Panel.Target != null)
                LogObjectiveSubtree(Panel.Target);
        }
    }

    /// <summary>
    /// One-shot dump of the objectives subtree, in ONE log record: per rect its name, RESOLVED
    /// width×height, horizontal anchor span (a stretched rect follows its parent and cannot be
    /// sized directly), <c>sizeDelta.x</c>, the layout components it carries (VLG/HLG with their
    /// <c>childControlWidth</c>/<c>childForceExpandWidth</c> flags, ContentSizeFitter's horizontal
    /// mode, LayoutElement's min/preferred/flexible width) and whether it holds TMP text. That is
    /// exactly the information needed to tell WHICH rect sets the wrap column — the count-only
    /// diagnostic that preceded it ("0 full-width rect(s)") identified nothing and cost two rounds.
    /// </summary>
    private void LogObjectiveSubtree(RectTransform root)
    {
        var sb = new System.Text.StringBuilder(2048);
        sb.Append("OBJECTIVES TREE (widths are RESOLVED px; the FIRST line is the rect the 'Breite' ")
          .Append("dial writes — every deeper width is derived from it by the layout groups):");
        int budget = WidthDumpMaxRects;
        AppendRectLine(sb, root, 0, ref budget);
        if (budget <= 0)
            sb.Append("\n  … truncated at ").Append(WidthDumpMaxRects).Append(" rects.");
        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>Recursive worker for <see cref="LogObjectiveSubtree"/> (inactive children included).</summary>
    private static void AppendRectLine(System.Text.StringBuilder sb, RectTransform? rect, int depth, ref int budget)
    {
        if (rect == null || budget <= 0)
            return;
        budget--;

        Rect r = rect.rect;
        sb.Append('\n').Append(' ', 2 + depth * 2)
          .Append(rect.name).Append("  ").Append(r.width.ToString("F0")).Append('x')
          .Append(r.height.ToString("F0"))
          .Append("  anchorX ").Append(rect.anchorMin.x.ToString("F2")).Append("..")
          .Append(rect.anchorMax.x.ToString("F2"))
          .Append(rect.anchorMax.x - rect.anchorMin.x > 0.01f ? " STRETCHED" : "")
          .Append("  sizeDeltaX ").Append(rect.sizeDelta.x.ToString("F0"));

        var group = rect.GetComponent<HorizontalOrVerticalLayoutGroup>();
        if (group != null)
            sb.Append("  [").Append(group is VerticalLayoutGroup ? "VLG" : "HLG")
              .Append(" ctrlW=").Append(group.childControlWidth ? '1' : '0')
              .Append(" expandW=").Append(group.childForceExpandWidth ? '1' : '0')
              .Append(" padL=").Append(group.padding.left)
              .Append(" padR=").Append(group.padding.right).Append(']');

        var fitter = rect.GetComponent<ContentSizeFitter>();
        if (fitter != null)
            sb.Append("  [CSF h=").Append(fitter.horizontalFit).Append(" v=").Append(fitter.verticalFit).Append(']');

        var element = rect.GetComponent<LayoutElement>();
        if (element != null)
            sb.Append("  [LE ignore=").Append(element.ignoreLayout ? '1' : '0')
              .Append(" minW=").Append(element.minWidth.ToString("F0"))
              .Append(" prefW=").Append(element.preferredWidth.ToString("F0"))
              .Append(" flexW=").Append(element.flexibleWidth.ToString("F0")).Append(']');

        var tmp = rect.GetComponent<TMP_Text>();
        if (tmp != null)
            sb.Append("  [TMP wrap=").Append(tmp.enableWordWrapping ? '1' : '0')
              .Append(" \"").Append(tmp.text.Length > 24 ? tmp.text.Substring(0, 24) + "…" : tmp.text).Append("\"]");

        if (!rect.gameObject.activeSelf)
            sb.Append("  (inactive)");

        for (int i = 0; i < rect.childCount && budget > 0; i++)
            AppendRectLine(sb, rect.GetChild(i) as RectTransform, depth + 1, ref budget);
    }

    /// <summary>
    /// Test #17: at the shared density the objectives text read too small (the
    /// initiative track at the SAME density was verdict-perfect — do not touch it).
    /// 0.6× density renders the objectives ~1.67× bigger; the dock fit clamp keeps
    /// them inside the mount budget.
    /// </summary>
    protected override float DensityScale => 0.6f;

    protected override RectTransform? FindTarget()
    {
        UIManager manager = UIManager.Instance;
        return manager != null && manager.MissionObjectiveContainer != null
            ? manager.MissionObjectiveContainer.transform as RectTransform
            : null;
    }

    // ---- battle goal line (per-scenario secret goal) -------------------------------------

    /// <summary>
    /// The CURRENT character's SECRET BATTLE GOAL for the running scenario ("persönliche
    /// Quest für das Szenario" — NOT the campaign retirement quest, which the first cut
    /// showed by mistake), shown as a compact mod-drawn TMP block anchored just BELOW the
    /// converted objectives panel (left of the control board). The objectives host carries
    /// live GAME UI (MissionObjectiveContainer), so appending content INSIDE it would be
    /// invasive — a standalone world-space label that pose-follows the host every tick is
    /// robust against reconversion and tray moves.
    ///
    /// Data path (verified in decompiled GH.Runtime/ActorStatPanel.cs:566-571 — the exact
    /// resolution the game's own stat panel uses):
    /// <c>AdventureState.MapState.InProgressQuestState.GetChosenBattleGoal(actor.Class.ID)</c>
    /// → <c>CBattleGoalState</c> (MapRuleLibrary.Party); its <c>BattleGoal</c> property
    /// resolves the <c>BattleGoalYMLData</c> record whose <c>LocalisedName</c> /
    /// <c>LocalisedDescription</c> are the SAME localization keys the game's
    /// <c>UIBattleGoalProgress.SetBattleGoal</c> feeds through TextLocalizedListener.
    /// Progress mirrors <c>UIScenarioBattleGoalProgress</c>:
    /// <c>BattleGoalConditionState.CurrentProgress / TotalConditionsAndTargets</c>.
    ///
    /// SECRECY (multiplayer): battle goals are secret. The game's own gate
    /// (BattleGoalContainer.Show / ActorStatPanel.cs:566): online, a goal renders ONLY for
    /// actors under my control (<c>!FFSNetwork.IsOnline || actor.IsUnderMyControl</c>) —
    /// mirrored verbatim here, and the current character is always the LOCAL tab-switchable
    /// hand (<c>CardsGameApi.ActiveHand().PlayerActor</c> — CardsHandManager.CurrentHand),
    /// so remote players' goals can never render. Exists in campaign AND guildmaster
    /// (CampaignOnly goal cards are filtered at deal time by the game itself,
    /// CQuestState.RollAndAssignBattleGoals) — no IsCampaign gate. Empty until the player
    /// picks a goal on the scenario intro; updates via the 0.5 s refresh.
    /// </summary>
    private const float QuestRefreshInterval = 0.5f;

    /// <summary>
    /// How long an UNANSWERABLE battle-goal poll (the hand is mid-rebuild, so there is nothing to
    /// ask) may keep the last resolved text on screen before the label gives up and hides. See the
    /// flicker block in <see cref="TickQuestLabel"/> for why only that kind of empty is held.
    ///
    /// <para>Two refresh intervals: long enough that a hand rebuild — which is over within a frame
    /// or two — never reaches it, short enough that a real teardown clears the label about as fast
    /// as the panel it hangs off does. It is deliberately NOT a config entry: it is the width of an
    /// engine-side gap, not a preference, and a dial here would only invite tuning a symptom.</para>
    /// </summary>
    private const float QuestBlankHoldSeconds = QuestRefreshInterval * 2f;
    /// <summary>Label rect height as a fraction of the panel width (label-local units).</summary>
    private const float QuestRectHeightFrac = 0.34f;
    /// <summary>Gap between the panel's bottom edge and the label top (fraction of width).</summary>
    private const float QuestGapFrac = 0.03f;

    /// <summary>
    /// Sub-step lift of the battle-goal label above the ladder slot of the nearest panel BEHIND
    /// it (<see cref="CanvasConversion.OrderAboveDistance"/>). Must stay under
    /// <c>CanvasConversion.PanelOrderStep</c> (16) so the label can never climb into the next
    /// panel's slot; 12 is the value the whole family of off-ladder plates already uses
    /// (<c>Net.BoardVisual.TagPanelLift</c>, <c>Cards.CardCueOrder.CuePanelLift</c>,
    /// <c>WristHud.PanelLift</c>) — a label that is genuinely in front of a window covers that
    /// window's own decorations too (close X +2, grab bar +4, menu-laid tooltip +10).
    /// </summary>
    private const int QuestPanelLift = 12;

    /// <summary>Minimum gap between two lines of the ladder-churn diagnostic (see
    /// <see cref="LogQuestOrderChurn"/>) — the raw change events can arrive several times a second
    /// while the head drifts, and a rate is what the next hardware log has to answer with.</summary>
    private const float QuestOrderLogIntervalSeconds = 10f;

    /// <summary>How many ladder-churn lines one session may spend. After this the churn is
    /// established and further lines would only be noise in a log that is read by hand.</summary>
    private const int QuestOrderLogCap = 8;

    private static readonly Vector3[] QuestCorners = new Vector3[4];
    private static bool s_questErrorLogged;

    /// <summary>One-shot latch for the frame-order assertion (<see cref="AssertPlateOrderSynced"/>).
    /// Static: the invariant is a property of the tick list, not of a surface instance, so a scene
    /// reload must not re-arm it into a repeating line.</summary>
    private static bool s_questPlateOrderWarned;

    private GameObject? _questGo;
    private TextMeshPro? _questTmp;
    private Renderer? _questRenderer;

    /// <summary>The MR backing plate's renderer, found READ-ONLY under the label and cached — the
    /// frame-order assertion's only instrument (<see cref="AssertPlateOrderSynced"/>). Never
    /// written to: <c>MrBacking</c> is and stays the plate's one writer.</summary>
    private Renderer? _questPlateRenderer;

    /// <summary>Last ladder order written onto <see cref="_questRenderer"/> (change-gate).
    /// <c>int.MinValue</c> = never written, so a freshly built label is seated on its first
    /// placed tick — the authored 0 is the defect band itself.</summary>
    private int _questAppliedOrder = int.MinValue;

    private float _nextQuestRefresh;

    /// <summary>Unscaled time the current run of UNANSWERABLE polls began, or 0 while the goal is
    /// resolving normally (see <see cref="QuestBlankHoldSeconds"/>).</summary>
    private float _questBlankSince;
    private string _questShown = "";

    /// <summary>Ladder-churn diagnostic (<see cref="LogQuestOrderChurn"/>): unscaled time the next
    /// line may be written, how many order changes have accumulated since the last one, and how
    /// much of the line budget is spent.</summary>
    private float _questOrderLogAt;
    private int _questOrderChanges;
    private int _questOrderLines;

    public override void Tick()
    {
        bool wasConverted = Panel != null;
        base.Tick();
        if (Panel != null)
        {
            ApplyContentWidth(); // the 'Breite' dial's real lever — see ApplyContentWidth
            LogWidthVerification();
            StyleObjectivesText(); // user request 5 — see the method

        }
        else if (wasConverted)
        {
            // Released this tick: the host is gone but the game's rows are alive again in their
            // 2D home — hand them their authored horizontal layout back before we lose the records.
            RestoreContentWidth();
        }
        TickQuestLabel();
    }

    /// <summary>
    /// The docked panel AND its hanging battle-goal label are re-placed at the end of the frame, so
    /// both sit on the board's CURRENT pose while it is being flung around (user: "der Aufgabentext
    /// links zieht immer ein wenig nach"). The label follows the host's world rect, so leaving it
    /// on the Update pass would just move the one-frame lag from the panel onto the label.
    ///
    /// <para>POSE ONLY — the label's DRAW ORDER is deliberately NOT re-derived here. See the
    /// ONE-FRAME BLANK block on <see cref="RankQuestLabel"/>: the order has exactly one legal frame
    /// phase, and it is the Update pass.</para>
    /// </summary>
    public override void LateTick()
    {
        base.LateTick();
        PlaceQuestLabel();
    }

    public override void Shutdown()
    {
        RestoreContentWidth(); // before base releases the panel (the rows are still alive here)
        if (_questGo != null)
            Object.Destroy(_questGo);
        _questGo = null;
        _questTmp = null;
        _questRenderer = null;
        _questPlateRenderer = null;
        _questAppliedOrder = int.MinValue;
        _questShown = "";
        base.Shutdown();
    }

    private void TickQuestLabel()
    {
        // FIRST, before anything this frame is written: does the plate still carry the order the
        // label ended the LAST frame with? That is the whole frame-order invariant, asked from the
        // one side that can see both numbers. See AssertPlateOrderSynced.
        AssertPlateOrderSynced();

        bool panelVisible = Panel != null && Panel.HostGo != null && Panel.HostGo.activeInHierarchy;
        if (!panelVisible)
        {
            if (_questGo != null && _questGo.activeSelf)
                _questGo.SetActive(false); // hides with the tray/panel, like the dock itself
            return;
        }

        if (Time.unscaledTime >= _nextQuestRefresh)
        {
            _nextQuestRefresh = Time.unscaledTime + QuestRefreshInterval;
            // THE RELIEF, RE-APPLIED UNTIL IT TAKES. NativeButtonSkin harvests the game's HUD font
            // off a live widget, so a label built before one existed has font == null and the
            // legibility recipe written at build time wrote to nothing at all — the "gated remedy
            // never ran" shape exactly. This costs one float read off a material once per refresh
            // tick and stops asking the moment the material reads back as already relieved, so it
            // is not a probe that outlives its answer either.
            if (_questTmp != null && !NativeButtonSkin.HasWorldReadableRelief(_questTmp))
                NativeButtonSkin.StyleWorldReadableLabel(_questTmp);
            string text = BuildQuestText(out bool unanswerable);
            if (text.Length > 0)
            {
                bool fresh = EnsureQuestLabel(); // rebuilds after scene unloads (Unity-null aware)
                if (fresh || text != _questShown)
                    _questTmp!.text = text;
                _questShown = text;
                _questBlankSince = 0f;
            }
            // ---- THE FLICKER (user, ModBuild 105) -----------------------------------------
            // "Der Text der persönlichen Quest flackert immer mal wieder auf (verschwindet für ein
            // frame und taucht dann sofort wieder auf)."
            //
            // ROOT CAUSE: one empty poll used to blank the label outright. BuildQuestText resolves
            // through CardsGameApi.ActiveHand(), and that hand is momentarily null while the game
            // rebuilds it — a character switch, a hand re-deal, the frame a pooled CardsHandUI is
            // re-bound. An empty answer therefore meant BOTH "there is no battle goal" and "ask me
            // again in a moment", and the second reading was being rendered as the first.
            //
            // SO THE TWO ARE TOLD APART AT THE SOURCE (the `unanswerable` flag), and only the
            // transient kind is held. That distinction is what keeps the hold honest: it is NOT a
            // grace period on the answer, it is a refusal to act on a NON-answer. A DEFINITE empty
            // — the secrecy gate said no, or this character has not chosen a goal yet — still hides
            // the label in the same tick it always did, so the goal of a merc you just switched
            // away from can never linger, and an online peer's goal can never appear at all.
            else if (!unanswerable)
            {
                _questShown = string.Empty;   // a real "no goal" — hide now, exactly as before
                _questBlankSince = 0f;
            }
            else
            {
                // Unanswerable: keep showing what was last resolved, but not forever — if the hand
                // never comes back (teardown, scenario end) the label must not stand there stale.
                if (_questBlankSince <= 0f)
                    _questBlankSince = Time.unscaledTime;
                else if (Time.unscaledTime - _questBlankSince >= QuestBlankHoldSeconds)
                    _questShown = string.Empty;
            }
        }

        bool show = _questShown.Length > 0 && _questGo != null;
        if (_questGo != null && _questGo.activeSelf != show)
            _questGo.SetActive(show);
        // THE ONLY PLACE THE LABEL'S DRAW ORDER IS EVER WRITTEN, and it must stay in the UPDATE
        // pass — see the ONE-FRAME BLANK block on RankQuestLabel. The pose is placed again in
        // LateTick (rigidity); the RANK deliberately is not.
        if (show && PlaceQuestLabel())
            RankQuestLabel();
    }

    /// <summary>
    /// Pose-follow the battle-goal label onto the objectives host's world rect. Split out of
    /// <see cref="TickQuestLabel"/> so it can be re-run from <see cref="LateTick"/>: the label
    /// hangs off the HOST's world corners, so it has to be written in the same frame phase as the
    /// host itself or it inherits exactly the one-frame drag the late placement exists to remove
    /// (see <see cref="TrayMountedPanelSurface.LateTick"/>). Cheap and stateless — reading four
    /// corners and writing one transform.
    ///
    /// <para>POSE ONLY. It used to end with <see cref="RankQuestLabel"/>, which made the draw order
    /// a LateUpdate write and cost the label one blank frame every time the order moved — see the
    /// ONE-FRAME BLANK block on <see cref="RankQuestLabel"/>. Returns whether a pose was actually
    /// written, so the Update-pass caller only ranks a label that is really laid out (ranking a
    /// zero-width or unplaced label would measure a distance to nowhere).</para>
    /// </summary>
    private bool PlaceQuestLabel()
    {
        if (Panel == null || _questGo == null || !_questGo.activeSelf)
            return false;

        // Anchored below the host's world rect (the exact plane the converted objectives render
        // on), sized proportional to the panel width so it rides tray grabs/resizes and diorama
        // zoom for free.
        Panel.HostRect.GetWorldCorners(QuestCorners); // 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 bl = QuestCorners[0];
        float width = (QuestCorners[3] - bl).magnitude;
        if (width < 1e-4f)
            return false; // not laid out yet
        Vector3 up = (QuestCorners[1] - bl).normalized;
        Vector3 bottomCenter = (bl + QuestCorners[3]) * 0.5f;
        Transform t = _questGo!.transform;
        t.rotation = Panel.HostTransform.rotation;
        t.localScale = Vector3.one * width;
        t.position = bottomCenter - up * (width * (QuestGapFrac + QuestRectHeightFrac * 0.5f));
        return true;
    }

    /// <summary>
    /// PERSPECTIVE FOR THE BATTLE-GOAL LINE (user 2026-08-09, verbatim: "Noch ein Element
    /// gefunden, dass die Perspektive nicht respektiert — wenn ich die Gegnerinfo, die erscheint
    /// wenn ich eine Figur hochhebe, hinter den Quest-Text (Character-Quest) halte verschwindet
    /// der Text. Da der Text vor der Info ist sollte er weiterhin davon sichtbar sein.").
    ///
    /// <para>ROOT CAUSE — the mod's standing defect for depth-less transparents. This label is a
    /// mod-built <see cref="TextMeshPro"/> on a SCENE-ROOT GameObject
    /// (<see cref="EnsureQuestLabel"/>), so it is neither a converted panel nor part of the
    /// board's furniture group: it hangs off the objectives HOST's world rect, not under the
    /// tray, so <c>PlayTray.AdoptFurniture</c> — which walks the tray subtree — never saw it, and
    /// nothing ever wrote its <c>sortingOrder</c>. It therefore drew at the default <b>0</b>,
    /// while the figure-grab info card (<c>StatPanelSurface</c>, converted at
    /// <c>ModalFallback.ModalHostSortingOrder</c> and then ranked live on the distance ladder)
    /// draws at <b>≥ CanvasConversion.PanelOrderBase (100)</b> — the hardware log has it at
    /// 148…276. Unity resolves transparents by sortingLayer → sortingOrder → renderQueue →
    /// distance, and neither surface writes depth, so 0 vs 148+ decided it outright: the info
    /// card painted over the goal line no matter which of the two was actually nearer.</para>
    ///
    /// <para>THE FIX IS THE LADDER, NOT A BIGGER NUMBER — the seam every other off-ladder plate
    /// already uses (<c>WorldTooltips</c>, <c>Net.BoardVisual</c> identity tags,
    /// <c>Cards.CardCueOrder</c>, <c>WristHud</c>): rank by MEASURED eye distance, so an info card
    /// held BEHIND the goal line is painted before it (line stays readable) and one held IN FRONT
    /// of it still covers it. Consistency, which is what the report asks for — not "the text
    /// wins".</para>
    ///
    /// <para>The distance measure is <c>CanvasConversion.PanelEyeDistance</c>'s, verbatim: the
    /// eye to the CLOSEST POINT of the label's finite rect. The panels it is ranked against are
    /// measured that way, so anything else would compare two different questions — and this label
    /// is wide and thin, where a centre distance can be tens of centimetres off its near edge.
    /// It also reads only the eye POSITION, so head rotation cannot move it.</para>
    ///
    /// <para>The MR backing plate needs nothing here: <c>MrBacking</c> copies its label
    /// renderer's LIVE sortingOrder every tick (MrBacking.TickLabels), so it rides along at the
    /// same slot and its earlier renderQueue keeps it just under the glyphs.</para>
    ///
    /// <para>Reads the PREVIOUS frame's ladder (<c>CanvasConversion.TickPanelOrder</c> runs last
    /// in the WorldUI LateUpdate chain) — a one-frame lag on a hysteresis-damped ladder is not
    /// observable, the same trade every other <c>OrderAboveDistance</c> caller accepts. Cost is
    /// one clamp, one distance, one walk of the listed panels and a CHANGE-GATED int write, and
    /// only while the label is actually shown.</para>
    ///
    /// <para>---- THE ONE-FRAME BLANK (user, hardware, ModBuild 106, verbatim: "Der Text der
    /// persönlichen Quest flackert immer mal wieder auf (verschwindet für ein frame und taucht dann
    /// sofort wieder auf)." — the SECOND round on this report; ModBuild 105's answer, the
    /// unanswerable-poll hold in <see cref="TickQuestLabel"/>, was in the build he tested.) ----</para>
    ///
    /// <para>WHY THE POLL COULD NEVER HAVE BEEN THE WHOLE STORY, and the evidence that says so: the
    /// text is re-derived every <see cref="QuestRefreshInterval"/> = 0.5 s and the label's shown/
    /// hidden state is a pure function of the last poll. A poll-driven blank therefore lasts AT
    /// LEAST half a second — it cannot produce "one frame, and back". The user's wording is
    /// precise and it rules the whole poll path out; what it points at is the RENDER.</para>
    ///
    /// <para>ROOT CAUSE — A FRAME-PHASE ASYMMETRY BETWEEN THIS LABEL AND ITS OPAQUE MR BACKING
    /// PLATE. <see cref="EnsureQuestLabel"/> registers the label with <c>MrBacking.Label</c>, and
    /// the hardware log for this very run says the plate was live for the whole session
    /// (LogOutput.log:80, "MR backings ON", with no OFF line after it). That plate is a Quad on the
    /// SHARED opaque material: <c>Blend One Zero</c>, <c>_ZWrite 1</c>, renderQueue 2998, parented
    /// under the label and seated 2 mm behind it. It is kept UNDER its own glyphs by ONE thing —
    /// sharing their <c>sortingOrder</c>, where its earlier renderQueue is the tie-break
    /// (MrBacking, PLATE SORTING ORDER). Unity resolves transparents by sortingLayer →
    /// sortingOrder → renderQueue, so the instant the plate's order is HIGHER than the label's, the
    /// plate paints AFTER the glyphs; TMP writes no depth, so nothing stops it and an opaque dark
    /// rectangle lands exactly on the text. The text does not fade or move — it is simply gone for
    /// that one rendered frame.</para>
    ///
    /// <para>AND THE TWO ORDERS WERE WRITTEN IN DIFFERENT FRAME PHASES.
    /// <c>MrBacking.TickLabels</c> copies the LABEL renderer's live order in the UPDATE pass
    /// (WorldUIModule.BuildTickSteps: "MrBacking" is the last UPDATE step, after
    /// <c>Surface:ObjectivesSurface</c>). This method used to be called from
    /// <see cref="PlaceQuestLabel"/>, which runs in BOTH passes — so the value that actually
    /// RENDERED was the one written in <see cref="LateTick"/>, a phase the plate never sees.
    /// Per frame: Update writes O_u and the plate copies O_u; LateUpdate overwrites the label with
    /// O_l; the frame renders label=O_l, plate=O_u. Whenever O_l &lt; O_u the plate outranks its own
    /// text for that frame, and the next Update re-syncs both — "verschwindet für ein frame und
    /// taucht dann sofort wieder auf", exactly.</para>
    ///
    /// <para>WHY O_u AND O_l DIFFER AT ALL, which is what makes it INTERMITTENT rather than
    /// constant: <c>OrderAboveDistance</c> is a STEP function of the measured eye distance with no
    /// hysteresis of its own (CanvasConversion.8.Order.cs, <c>FartherPanelOrder</c>: the answer is
    /// the highest ladder order among panels within <c>OrderSwapMarginMeters</c>-tolerant "farther
    /// than me", and the ladder steps by 16). Both passes read the SAME ladder snapshot — it is
    /// rebuilt only at the very end of LateUpdate — so the only moving input is the label's own
    /// distance, and both of its terms move between the passes: the label is RE-POSED off the
    /// board's fresher pose in <see cref="LateTick"/>, and the head camera is driven by a
    /// <c>TrackedPoseDriver</c> in <c>UpdateAndBeforeRender</c> mode, i.e. by a MonoBehaviour
    /// <c>Update</c> with no execution-order relation to <c>WorldUIModule.Update</c>. Millimetres
    /// are enough when the distance is sitting on a step edge — and this label hangs a few
    /// centimetres under a panel that is ITSELF on the ladder, so an edge is exactly where it
    /// lives. INFERRED, not read from a log: the mod has never logged this order. Hence the
    /// churn diagnostic below, which makes the next hardware run answer it as a yes/no.</para>
    ///
    /// <para>THE FIX IS THE PHASE, NOT A BIGGER NUMBER. The rank now happens ONCE per frame, in the
    /// UPDATE pass, from <see cref="TickQuestLabel"/> — before <c>MrBacking.Tick</c> in the same
    /// pass — and nothing writes it afterwards, so the value that renders is by construction the
    /// value the plate copied. DO NOT move this call back into <see cref="PlaceQuestLabel"/> "so it
    /// matches the fresh pose": that is precisely the defect. The pose still gets its late write;
    /// only the ORDER is pinned to the phase its follower reads. That ordering lives in another
    /// file, so it is asserted at runtime from this one — see <see cref="AssertPlateOrderSynced"/>.</para>
    ///
    /// <para>THIS IS ONE OF TWO ONE-FRAME BLANKS, AND BOTH ARE CLOSED IN THIS BUILD. The other is
    /// the unfiltered mount-visibility read that gates the whole docked panel, now debounced in
    /// <see cref="TrayMountedPanelSurface.MountHideGraceFrames"/> — that comment carries its own
    /// evidence. Neither was shipped as "the likely one with a diagnostic for the other": a
    /// hardware run is expensive enough that two candidates which both fit the evidence get
    /// neutralised together, and the churn line below was demoted to confirmation accordingly.</para>
    ///
    /// <para>WHAT THIS COSTS, stated honestly: the order is now derived from the Update-pass pose,
    /// so while the board is being flung the label's slot can be one frame behind its own position.
    /// That is the identical trade the ladder itself already makes (it reads the previous frame's
    /// distances) and it is invisible at 16-per-step granularity; a mis-ordered frame during a
    /// throw is not a blanked frame while sitting still.</para>
    ///
    /// <para>REJECTED ALTERNATIVES. (1) Stamping the plate's <c>sortingOrder</c> directly from here
    /// by finding the child named <c>MrBacking.PlateObjectName</c> — that name is documented as
    /// DIAGNOSTIC ONLY and <c>MrBacking</c> is documented as the plate's one writer; two writers on
    /// one renderer is how the next flicker gets built. (2) Giving the plate a dead-band or
    /// hysteresis so the order stops churning — that treats the churn as the defect, but churn is
    /// legitimate (the label really does cross other panels' distances) and a synchronised order is
    /// correct at every churn rate. (3) Dropping the label's ladder rank altogether — that is the
    /// ModBuild-90 regression this method exists to prevent (the figure-grab info card painting
    /// over the goal line). (4) Suppressing the MR plate for this label — it would hand the
    /// passthrough room back through the one backing that keeps the line readable.</para>
    /// </summary>
    private void RankQuestLabel()
    {
        if (_questTmp == null || _questRenderer == null)
            return;
        Camera? cam = CanvasConversion.WorldCamera;
        if (cam == null)
            return;

        Vector3 eye = cam.transform.position;
        RectTransform rect = _questTmp.rectTransform;
        Vector3 local = rect.InverseTransformPoint(eye);
        Rect r = rect.rect;
        var onPlate = new Vector3(
            Mathf.Clamp(local.x, r.xMin, r.xMax),
            Mathf.Clamp(local.y, r.yMin, r.yMax),
            0f);
        float eyeDistance = Vector3.Distance(eye, rect.TransformPoint(onPlate));

        int order = CanvasConversion.OrderAboveDistance(eyeDistance, QuestPanelLift);
        if (order == _questAppliedOrder)
            return;
        int previous = _questAppliedOrder;
        _questAppliedOrder = order;
        _questRenderer.sortingOrder = order;
        _questOrderChanges++;
        LogQuestOrderChurn(order, previous, eyeDistance);
    }

    /// <summary>
    /// WHICH DEFECT WAS ACTUALLY FIRING — confirmation, NOT the thing that decides the next round.
    ///
    /// <para>Two mechanisms could each blank this label for exactly one frame, and BOTH are closed
    /// in this build: the draw-order desync above, and the unfiltered mount-visibility read now
    /// debounced in <see cref="TrayMountedPanelSurface.MountHideGraceFrames"/>. That is deliberate
    /// and it is the reason this line is no longer a question: shipping a diagnostic that merely
    /// tells the two apart would spend a hardware run — a game launch plus headset time — on
    /// learning which hypothesis was right instead of on a fixed game.</para>
    ///
    /// <para>What it still earns its place for: the fix above rests on one INFERRED link — that
    /// this label's ladder order really does move while the player sits at the table (see
    /// RankQuestLabel, "WHY O_u AND O_l DIFFER AT ALL"). Everything else in that chain is read from
    /// source, but nothing has ever logged this number. So the line states a RATE: how many order
    /// changes happened since the last one. A busy rate says the draw-order mechanism was live and
    /// is what the phase fix removed; a flat zero says the mount blink was the one that mattered
    /// and the ladder was never involved. Either way the flicker is already gone — this only tells
    /// the next reader which comment was load-bearing. Rate-limited to
    /// <see cref="QuestOrderLogIntervalSeconds"/> and capped at <see cref="QuestOrderLogCap"/>
    /// lines, because a log that scrolls is a log nobody reads; Info level on purpose (BepInEx's
    /// default disk config drops Debug entirely).</para>
    /// </summary>
    private void LogQuestOrderChurn(int order, int previous, float eyeDistance)
    {
        if (_questOrderLines >= QuestOrderLogCap)
            return;
        float now = Time.unscaledTime;
        if (_questOrderLogAt > 0f && now < _questOrderLogAt)
            return;
        _questOrderLogAt = now + QuestOrderLogIntervalSeconds;
        _questOrderLines++;
        int changes = _questOrderChanges;
        _questOrderChanges = 0;
        string from = previous == int.MinValue ? "fresh label" : previous.ToString();
        string tail = _questOrderLines >= QuestOrderLogCap
            ? " (last line of this session's budget — the rate is established by now)"
            : string.Empty;
        VRLog.Info("WorldUI", $"BATTLE-GOAL LADDER: {changes} draw-order change(s) since the last " +
                              $"line — now {order} (from {from}), label {eyeDistance:0.000} m from " +
                              "the eye. This is the flicker diagnostic: the order is written in the " +
                              "UPDATE pass ONLY, so the label's opaque MR backing plate (which " +
                              "copies it later in that SAME pass) can never end a frame ranked " +
                              "ABOVE the glyphs it backs. A one-frame blank reported while these " +
                              "lines show zero changes around it means the ladder is NOT the cause." +
                              tail);
    }

    /// <summary>
    /// THE FRAME-ORDER ASSERTION, and it IS detectable from this side — no reach into
    /// <c>MrBacking</c> is needed and none is made.
    ///
    /// <para>WHAT IT DEFENDS: the fix in <see cref="RankQuestLabel"/> is correct only because the
    /// label's order is written EARLIER IN THE SAME UPDATE PASS than <c>MrBacking.Tick</c>, which
    /// copies it onto the opaque backing plate. That ordering lives in
    /// <c>WorldUIModule.BuildTickSteps</c> — another file, and one this class cannot constrain.
    /// Move <c>MrBacking</c> above the slot surfaces and the one-frame blank comes back with
    /// nothing to say it did.</para>
    ///
    /// <para>HOW IT DETECTS THAT WITHOUT A SECOND WRITER: the plate is a direct child of the label
    /// (<c>MrBacking.CreatePlate</c> parents it to the TMP's own rect) under the documented name
    /// <c>MrBacking.PlateObjectName</c>, so its renderer can simply be READ. At the top of the tick,
    /// before this frame writes anything, plate order and label order must be EQUAL — that is what
    /// "the plate copied the value that rendered" means, expressed as a state instead of as an
    /// ordering. If <c>MrBacking</c> ever ran first, it would copy the previous frame's value and
    /// this comparison would catch it on the first frame the order actually moved, which is exactly
    /// the first frame it could have blanked the text. Reading a name that its owner calls
    /// diagnostic-only is precisely the sanctioned use; the REJECTED alternative was writing
    /// through it (see RankQuestLabel).</para>
    ///
    /// <para>NO FALSE POSITIVES, by construction: it asks only while MR actually wants plates
    /// (<c>MrBacking.WantOpaque</c> — with MR off no plate exists and the whole check is one bool),
    /// only once a plate has been found, only while that plate is <c>activeInHierarchy</c> (MrBacking
    /// deactivates plates when MR is off or the label is hidden, and a deactivated plate's order is
    /// stale by design), and only after the label has been ranked at least once. A freshly rebuilt
    /// label has no plate child yet and is skipped until one exists. Latched to ONE line for the
    /// session — this is an invariant breach, not a rate.</para>
    /// </summary>
    private void AssertPlateOrderSynced()
    {
        if (s_questPlateOrderWarned || !MrBacking.WantOpaque)
            return;
        if (_questGo == null || _questRenderer == null || _questAppliedOrder == int.MinValue)
            return;
        if (_questPlateRenderer == null) // Unity-null aware: also re-finds after a label rebuild
        {
            Transform? plate = _questGo.transform.Find(MrBacking.PlateObjectName);
            if (plate == null)
                return; // not built yet — MrBacking creates it lazily on the first visible tick
            _questPlateRenderer = plate.GetComponent<Renderer>();
            if (_questPlateRenderer == null)
                return;
        }
        if (!_questPlateRenderer.gameObject.activeInHierarchy)
            return;
        int plateOrder = _questPlateRenderer.sortingOrder;
        if (plateOrder == _questRenderer.sortingOrder)
            return;
        s_questPlateOrderWarned = true;
        VRLog.Warn("WorldUI", "FRAME-ORDER BREACH (battle-goal label): its MR backing plate is at " +
                              $"sortingOrder {plateOrder} while the label it backs is at " +
                              $"{_questRenderer.sortingOrder}. The plate is OPAQUE, so whenever it " +
                              "ranks HIGHER it paints over the text for that frame — the one-frame " +
                              "quest-text flicker, back again. The invariant is that the label's " +
                              "order is written EARLIER IN THE SAME UPDATE PASS than MrBacking.Tick, " +
                              "which copies it: check WorldUIModule.BuildTickSteps still lists " +
                              "'MrBacking' AFTER the slot surfaces, and that nothing has moved " +
                              "RankQuestLabel back into a LateUpdate caller. Logged once per session.");
    }

    /// <summary>Build (or rebuild after a scene unload) the quest TMP label. True when fresh.</summary>
    private bool EnsureQuestLabel()
    {
        if (_questGo != null && _questTmp != null)
            return false;
        if (_questGo != null)
            Object.Destroy(_questGo); // half-built remnant — never expected, but never leak
        _questGo = new GameObject("GloomhavenVR.BattleGoal");
        _questTmp = _questGo.AddComponent<TextMeshPro>();
        // Ladder ranking (RankQuestLabel): cache the TMP's own MeshRenderer once and force the
        // first placed tick to seat it — a rebuilt label starts at the authored order 0, which
        // is precisely the band this ranking exists to leave.
        _questRenderer = _questTmp.GetComponent<Renderer>();
        _questAppliedOrder = int.MinValue;
        _questPlateRenderer = null; // the old plate died with the old label — re-find under the new one
        _questTmp.alignment = TextAlignmentOptions.Top;
        // THE FILL COLOUR IS DELIBERATELY UNCHANGED (user request 5, 2026-09-03). He asked for
        // legibility "indem du die Schriftfarbe entsprechend wählst" AND for it to stay immersive —
        // and measuring his screenshot showed the fill is not the term that is wrong: this text
        // crosses ground running from L=0.0003 to L=0.3365 WITHIN ONE LINE, so the same cream that
        // reads at 17.2:1 over the leaf litter reads at 2.24:1 over the lit stone a few glyphs
        // later — under the 3:1 floor, while the rest of the sentence is fine.
        // What it lacks is a rim of its own, not a different colour. See StyleWorldReadableLabel.
        _questTmp.color = new Color(0.92f, 0.88f, 0.76f);
        NativeButtonSkin.ApplyFont(_questTmp); // native HUD font, like the pile captions
        // …AND THE RIM. Opt-in by name — ApplyFont still outlines nothing (NativeButtonSkin's own
        // note). Re-applied by RankQuestLabel's tick, because ApplyFont has no font to mint a
        // material from until the mod has harvested one off a live game widget, and a relief
        // written to a null material is a fix that never ran.
        NativeButtonSkin.StyleWorldReadableLabel(_questTmp);
        // Label-local units: scale = panel width, so 0.96 ≈ full panel width; auto-size
        // shrinks/wraps long localized strings inside the block (TmpFit policy).
        TmpFit.Fit(_questTmp, 0.96f, QuestRectHeightFrac, maxFontSize: 0.65f);
        MrBacking.Label(_questTmp); // hangs below the objectives panel, nothing behind it in MR
        VRLayers.Apply(_questGo);
        return true;
    }

    /// <summary>
    /// Completion-state colors, matching the read of the game's own battle-goal tracker
    /// (UIScenarioBattleGoalProgress: green check when completed, red X when failed).
    /// </summary>
    private const string CheckColorHex = "#58C24E"; // green — completedCheck equivalent
    private const string CrossColorHex = "#D9453C"; // red — failedCheck equivalent

    /// <summary>
    /// Completion glyph for the requirement line, mirroring the game's char-info tracker
    /// (<c>UIScenarioBattleGoalProgress.RefreshState</c>/<c>UpdateProgress</c> — the
    /// green <c>completedCheck</c> / red <c>failedCheck</c> objects) as colored TMP text:
    /// - <c>Failed</c> (the goal's fail condition tripped) → red ✗;
    /// - fulfilled → green ✓. Fulfilled mirrors the game's two paths exactly: a
    ///   single-condition goal (total ≤ 1) completes the moment progress reaches the
    ///   total; a multi-condition goal additionally requires a POSITIVE condition
    ///   (the game's OnCompletedProgress listener refuses NegativeCondition goals,
    ///   which only "complete" by surviving to scenario end);
    /// - otherwise (in progress) → no glyph, the "(x/y)" progress already narrates.
    /// Returns a trailing-space-suffixed rich-text prefix or "".
    /// </summary>
    private string QuestStateGlyph(CUnlockConditionState? cond)
    {
        if (cond == null)
            return "";
        int total = cond.TotalConditionsAndTargets;
        int current = cond.CurrentProgress;
        if (cond.Failed)
            return $"<color={CrossColorHex}>{PickGlyph('✗', 'X')}</color> ";
        bool fulfilled = total > 0 && current >= total && (total <= 1 || !cond.NegativeCondition);
        return fulfilled ? $"<color={CheckColorHex}>{PickGlyph('✓', '+')}</color> " : "";
    }

    /// <summary>
    /// The preferred glyph when the label's font (incl. fallbacks) can render it, else the
    /// ASCII stand-in — a missing glyph would draw as a hollow box. ✓ is proven on hardware
    /// in this same harvested HUD font (the tray's "✓ READY" label); ✗ gets the same check.
    /// Unresolvable while the label/font is still building → stand-in; the 0.5 s refresh
    /// re-evaluates and the changed string re-applies automatically.
    /// </summary>
    private char PickGlyph(char preferred, char fallback)
    {
        TMP_FontAsset? font = _questTmp != null ? _questTmp.font : null;
        try
        {
            return font != null && font.HasCharacter(preferred, searchFallbacks: true)
                ? preferred
                : fallback;
        }
        catch (System.Exception)
        {
            return fallback; // font asset mid-teardown — never break the quest line over a glyph
        }
    }

    /// <summary>
    /// "title\n[✓/✗ ]requirement (progress/target)" for the current character's chosen SECRET
    /// BATTLE GOAL, or "" (hidden): no running quest / no goal chosen yet / remote actor
    /// online (secrecy gate — see the class doc). The ✓ (green) / ✗ (red) completion glyph
    /// mirrors the game's own char-info tracker — see <see cref="QuestStateGlyph"/>.
    /// Guarded — a game-side surprise must
    /// never starve the WorldUI tick (the unguarded-Update lesson); failures log once
    /// and render nothing.
    /// </summary>
    /// <param name="unanswerable">
    /// True when an empty result means "there is nothing to ASK right now" rather than "the answer
    /// is no goal": the hand the goal is resolved through is mid-rebuild, or the map state is not
    /// up yet. <see cref="TickQuestLabel"/> holds the last text through that and hides on every
    /// other empty — the two used to be indistinguishable, which is what made the label flicker.
    /// Meaningless when the returned text is non-empty.
    /// </param>
    private string BuildQuestText(out bool unanswerable)
    {
        unanswerable = false;
        try
        {
            CardsHandUI? hand = CardsGameApi.ActiveHand();
            CPlayerActor? actor = hand != null ? hand.PlayerActor : null;
            if (actor == null || actor.Class == null)
            {
                // NOT an answer: CardsGameApi.ActiveHand() is null for the frames in which the game
                // re-binds a pooled CardsHandUI (character switch, re-deal), and a hand without a
                // PlayerActor is the same window seen one step later. Nothing about the battle goal
                // changed — we simply asked while the phone was off the hook.
                unanswerable = true;
                return "";
            }
            var mapState = AdventureState.MapState;
            MapRuleLibrary.MapState.CQuestState? questState =
                mapState != null ? mapState.InProgressQuestState : null;
            if (questState == null)
            {
                // Also not an answer while the map state is still coming up (scenario load). Once
                // it IS up and simply has no running quest — level editor, pre-scenario — the
                // holder above times out and the label hides; there is no hand to come back.
                unanswerable = mapState == null;
                return "";
            }
            // SECRECY — ASKED OF THE ONE CLASS THAT OWNS IT, not re-stated here. This site used to
            // open-code the game's gate (BattleGoalContainer.Show / ActorStatPanel.cs:566) as
            // `FFSNetwork.IsOnline && !actor.IsUnderMyControl`, which is precisely the negation of
            // Net.RevealGate.ShowBattleGoal for a non-null actor — and `actor` is non-null here, the
            // early return above guarantees it. The two spellings agreed, and that is exactly the
            // hazard: a mirrored secrecy rule only has to be edited on ONE side to start leaking a
            // battle goal, and this project has the mirrored-constant lesson written down. The user
            // ruling that permits any hidden-information exception at all ("die einzige Ausnahme ist
            // hier die geheime Quest des characters …") lives in Net/RevealGate, so the decision does
            // too. ActiveHand is the local player's hand, so in practice this is belt-and-braces —
            // but the game enforces it this exact way and so do we, through one predicate.
            if (!Net.RevealGate.ShowBattleGoal(actor))
                return "";
            CBattleGoalState? goal = questState.GetChosenBattleGoal(actor.Class.ID);
            var data = goal != null ? goal.BattleGoal : null;
            if (goal == null || data == null)
                return ""; // not chosen yet (intro picker still open) — hidden like the game's tracker
            string title = Loc.Game(data.LocalisedName, "Battle goal");
            string body = Loc.Game(data.LocalisedDescription, "");
            // Progress the way the game's UIScenarioBattleGoalProgress renders it.
            var cond = goal.BattleGoalConditionState;
            if (cond != null && cond.TotalConditionsAndTargets > 0)
            {
                string progress = $"{cond.CurrentProgress}/{cond.TotalConditionsAndTargets}";
                body = body.Length > 0 ? body + "  (" + progress + ")" : "(" + progress + ")";
            }
            // Green ✓ / red ✗ before the requirement line, exactly like the char-info panel
            // (updates live: the 0.5 s refresh re-derives the state and re-applies on change).
            string glyph = QuestStateGlyph(cond);
            if (glyph.Length > 0)
                body = body.Length > 0 ? glyph + body : glyph.TrimEnd();
            return body.Length > 0 ? title + "\n" + body : title;
        }
        catch (System.Exception ex)
        {
            if (!s_questErrorLogged)
            {
                s_questErrorLogged = true;
                VRLog.Warn("WorldUI", $"Battle-goal line unavailable ({ex.Message}) — hidden.");
            }
            return "";
        }
    }
}

/// <summary>
/// SELECTION-PHASE "who still has to choose" cue, rendered ON THE INITIATIVE ORDER BAR (the
/// <see cref="InitiativeTrackSurface"/> docked to the control-board tray) instead of on the board
/// minis. A small amber RING pulses AROUND the portrait of every actor the local player controls
/// that has not yet finished card selection, so a glance at the initiative bar shows which
/// characters still need choosing. The instant an actor commits (two cards / long rest) its ring
/// clears, and the whole set clears when the selection phase ends.
///
/// WHY A FRAME AROUND THE PORTRAIT, NOT A FILL OVER IT (user feedback): the first cut washed a flat
/// amber field over the whole initiative entry and the user saw NOTHING and disliked the "flat area
/// that doesn't fit". Two things sank it: it was parented to the <c>InitiativeTrackActorBehaviour</c>
/// (the layout/click NODE), whose RectTransform is not the visible avatar rect — a fill+few-px
/// overlay on it lands off the actual portrait art — and a low-alpha wash reads as nothing anyway.
/// This version attaches a hollow, 9-sliced amber border sprite as a child of the AVATAR PORTRAIT
/// itself (<c>InitiativeTrackActorAvatar.m_AvatarImage</c>, the <c>RawImage</c> the game draws the
/// character face on), sized to the portrait rect + an outset, so the glow sits in the margin band
/// straddling the portrait edge — visibly framing the face rather than covering it, and correctly
/// placed/scaled because it inherits the portrait's own rect, depth, and layer.
///
/// The initiative bar is the game's OWN <c>InitiativeTrack</c> (adopted into world space by
/// <see cref="InitiativeTrackSurface"/>, not copied); each actor's entry is the
/// <c>InitiativeTrackActorBehaviour</c> resolved by the game's public
/// <c>InitiativeTrack.FindInitiativeTrackActor(actor)</c> (its <c>.Actor</c> is the very
/// <c>CPlayerActor</c> from the scenario, so the match is reference-exact). The ring:
/// <list type="bullet">
/// <item>has <c>raycastTarget = false</c>, so it never intercepts the portrait's character-switch
///   click (the whole point of the pokeable track);</item>
/// <item>is a hollow (fillCenter=false) 9-sliced border — a thin amber outline in the portrait's
///   margin — so the face stays fully visible; it breathes (alpha + a subtle scale pulse) on
///   <c>Time.unscaledTime</c> so it keeps animating while the game is time-paused during the
///   selection camera move;</item>
/// <item>is a nested descendant of the portrait (not a direct <c>initiativeTrackHolder</c> child) at
///   local z 0, so it is invisible to <see cref="InitiativeTrackSurface"/>'s per-portrait depth
///   normalization (z==0 ⇒ ignored) and depth-aware laser pick (which only scans direct holder
///   children), while inheriting the portrait's compressed depth for correct occlusion;</item>
/// <item>copies the portrait's CURRENT layer every tick, so it rides the surface's mod-layer
///   re-layering across convert/release cycles and renders on exactly the cameras the portrait does.</item>
/// </list>
/// Entries are pooled/reused by the game, so we re-resolve pending → entry every tick and hide any
/// ring whose entry is no longer pending; rings are kept (deactivated) for cheap reuse and only
/// destroyed on <see cref="Reset"/> (module hot-reload).
///
/// <para>MULTIPLAYER — THIS CUE IS PER-VIEWER, and it leaked (1:1 board audit, 2026-08-08). The
/// pending set comes from <c>SelectionReadyHighlighter</c> filtered by
/// <c>IsUnderControlOrSingle()</c>, so these rings exist ONLY for actors the LOCAL player controls.
/// They are plain <c>Image</c>s parented under the track's portraits, and a peer's mirrored board
/// clones that whole track (<c>Net.RemoteWidgetMirror</c>) — so <c>Instantiate</c> copied them and
/// <c>Pair.Apply</c> drove their active flag and colour, putting the OBSERVER's ring set on every
/// peer's board while the board owner's never appeared. <c>Net.RemoteInitiativeTrack</c> fixes both
/// halves: it forces the cloned ring OFF (resolved by reference through
/// <see cref="LiveRingRectOf"/>, never by name) and lights its own for the OWNER's still-choosing
/// characters, using <see cref="PendingRingTint"/> and <see cref="RingOutsetPixels"/> so the copy
/// is the same amber, the same size and the same breath as this original. Anything added here that
/// changes what the ring LOOKS like should go through those two members, or the mirror will drift
/// from the cue it is a picture of.</para>
/// </summary>
internal static class InitiativeSelectionGlow
{
    /// <summary>Warm amber "still waiting" ring; only alpha (× the sprite's own falloff) is tinted.</summary>
    private static readonly Color GlowColor = new Color(1f, 0.72f, 0.20f);

    /// <summary>Ring outset (uGUI px) past the portrait rect, so the frame sits in the margin band.</summary>
    private const float OutsetPixels = 8f;

    /// <summary>The same outset, for the ONE other surface that has to reproduce this cue at the
    /// same size: a peer's MIRRORED initiative track (<c>Net.RemoteInitiativeTrack</c>), which
    /// builds the board owner's ring with <c>Board.UiRing</c> and must not draw it at that class's
    /// own wider 13 px band — see <c>UiRing.Build</c>'s outset parameter.</summary>
    internal const float RingOutsetPixels = OutsetPixels;

    /// <summary>
    /// The ring colour at its current breathing alpha — the exact pixel this cue is showing on the
    /// LOCAL track this frame, for the one surface that has to REPRODUCE it: a peer's mirrored
    /// track. One palette (<see cref="GlowColor"/>) and one clock
    /// (<c>SelectionReadyHighlighter.PulseAlpha</c>), so the mirrored ring and the local one can
    /// never breathe out of step or in different ambers.
    /// </summary>
    internal static Color PendingRingTint()
    {
        Color c = GlowColor;
        c.a = Board.SelectionReadyHighlighter.PulseAlpha;
        return c;
    }

    /// <summary>
    /// The LIVE ring this cue has built on <paramref name="entry"/>, or null when it never lit one
    /// (the common case for an entry the LOCAL player does not control — which is exactly the
    /// asymmetry that made this cue leak).
    ///
    /// <para>Its ONE caller is <c>Net.RemoteInitiativeTrack</c>, which needs the SOURCE node in
    /// order to ask <c>RemoteWidgetMirror.CloneOf</c> for the clone of it and force that clone OFF:
    /// the mirror clones the whole track subtree, this ring included, and <c>Pair.Apply</c> copies
    /// its active flag and colour like any other Graphic — so without this the OBSERVER's amber
    /// rings stood on every peer's board while the owner's never did. Resolved by REFERENCE from
    /// this class's own table, never by searching the hierarchy for a name.</para>
    /// </summary>
    internal static RectTransform? LiveRingRectOf(InitiativeTrackActorBehaviour? entry)
    {
        if (entry == null)
            return null;
        return s_rings.TryGetValue(entry, out Image ring) && ring != null
            ? ring.transform as RectTransform
            : null;
    }

    /// <summary>Peak of the breathing scale pulse (1 = portrait+outset size). Subtle, in-theme.</summary>
    private const float ScalePulse = 0.05f;

    /// <summary>Breaths per second of the ring's scale pulse (matches the driver's alpha period).</summary>
    private const float PulseHz = 1f / 1.5f;

    // Ring Image per entry we have ever lit (entry may be Unity-destroyed on scene change → pruned).
    private static readonly Dictionary<InitiativeTrackActorBehaviour, Image> s_rings = new(16);
    private static readonly HashSet<InitiativeTrackActorBehaviour> s_active = new();
    private static readonly List<InitiativeTrackActorBehaviour> s_stale = new(16);

    // One-shot on-screen diagnostic per entry so a future "I see no ring" is answerable from the log.
    private static readonly HashSet<InitiativeTrackActorBehaviour> s_diagLogged = new();
    private static readonly Vector3[] s_diagCorners = new Vector3[4];

    /// <summary>
    /// Pulse the ring around every pending actor's portrait at <paramref name="alpha"/>, and hide the
    /// ring on every entry that is no longer pending. Safe to call with an empty list (hides
    /// everything). No-op-safe when the track is not built yet.
    /// </summary>
    public static void Apply(List<CPlayerActor> pending, float alpha)
    {
        s_active.Clear();
        // Breathing scale, in phase with the driver's alpha pulse; unscaledTime so it animates even
        // while TimeManager is paused during the selection camera move.
        float breath = 1f + ScalePulse * (Mathf.Sin(Time.unscaledTime * (2f * Mathf.PI * PulseHz)) + 1f) * 0.5f;

        InitiativeTrack track = InitiativeTrack.Instance;
        if (track != null && pending != null)
        {
            for (int i = 0; i < pending.Count; i++)
            {
                CPlayerActor actor = pending[i];
                if (actor == null)
                    continue;
                InitiativeTrackActorBehaviour entry = track.FindInitiativeTrackActor(actor);
                if (entry == null || !entry.gameObject.activeInHierarchy)
                    continue; // entry not spawned / not on screen — light nothing this frame

                Image ring = EnsureRing(entry);
                if (ring == null)
                    continue; // portrait not resolvable yet — retry next tick
                s_active.Add(entry);

                // Ride the portrait's current layer (mod layer when the surface is converted).
                GameObject go = ring.gameObject;
                int layer = ring.transform.parent != null ? ring.transform.parent.gameObject.layer : go.layer;
                if (go.layer != layer)
                    go.layer = layer;

                Color c = GlowColor;
                c.a = alpha;
                ring.color = c;
                ring.transform.localScale = new Vector3(breath, breath, 1f);
                if (!go.activeSelf)
                    go.SetActive(true);

                LogRingDiagOnce(entry, ring);
            }
        }

        // Deactivate rings whose entry is no longer pending (committed, reassigned by pooling,
        // exhausted, or gone). Prune entries the game has since destroyed.
        s_stale.Clear();
        foreach (KeyValuePair<InitiativeTrackActorBehaviour, Image> kv in s_rings)
        {
            if (kv.Key == null || kv.Value == null || !s_active.Contains(kv.Key))
                s_stale.Add(kv.Key!); // Unity fake-null (destroyed entry) is a real ref, never null
        }
        for (int i = 0; i < s_stale.Count; i++)
        {
            InitiativeTrackActorBehaviour entry = s_stale[i];
            if (entry == null || !s_rings.TryGetValue(entry, out Image ring) || ring == null)
            {
                s_rings.Remove(entry!);
                continue;
            }
            if (ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
        }
    }

    /// <summary>Hide every ring (phase exit / feature off). Rings are kept for cheap reuse.</summary>
    public static void ClearAll()
    {
        if (s_rings.Count == 0)
            return;
        foreach (Image ring in s_rings.Values)
        {
            if (ring != null && ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
        }
    }

    /// <summary>Destroy every ring GameObject and forget them (module shutdown / hot-reload).</summary>
    public static void Reset()
    {
        foreach (Image ring in s_rings.Values)
        {
            if (ring != null)
                Object.Destroy(ring.gameObject);
        }
        s_rings.Clear();
        s_active.Clear();
        s_stale.Clear();
        s_diagLogged.Clear();
    }

    /// <summary>
    /// Resolve the actual VISIBLE portrait rect of an entry — the avatar face
    /// (<c>InitiativeTrackActorAvatar.m_AvatarImage</c>, a <c>RawImage</c>). We take the first active
    /// RawImage under the entry's <c>Avatar</c> (the character face is the avatar's only RawImage;
    /// every other graphic — initiative digit, name, selection frame — is a TMP/Image). Returns null
    /// while the avatar has not been pooled in yet.
    /// </summary>
    private static RectTransform? FindPortraitRect(InitiativeTrackActorBehaviour entry)
    {
        InitiativeTrackActorAvatar avatar = entry.Avatar;
        if (avatar == null)
            return null;
        RawImage[] raws = avatar.GetComponentsInChildren<RawImage>(includeInactive: false);
        for (int i = 0; i < raws.Length; i++)
        {
            RawImage r = raws[i];
            if (r != null && r.transform is RectTransform rt)
                return rt;
        }
        return null;
    }

    /// <summary>Lazily build (or rebuild after a scene-swap destroyed it) the entry's portrait ring.</summary>
    private static Image EnsureRing(InitiativeTrackActorBehaviour entry)
    {
        if (s_rings.TryGetValue(entry, out Image existing))
        {
            if (existing != null && existing.transform.parent != null)
                return existing;
            s_rings.Remove(entry); // destroyed / re-pooled with the old track — rebuild below
        }

        RectTransform? portrait = FindPortraitRect(entry);
        if (portrait == null)
            return null!; // avatar face not laid out yet — caller retries next tick

        var go = new GameObject("GloomhavenVR.SelectionRing", typeof(RectTransform), typeof(Image));
        var rt = (RectTransform)go.transform;
        rt.SetParent(portrait, worldPositionStays: false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-OutsetPixels, -OutsetPixels);
        rt.offsetMax = new Vector2(OutsetPixels, OutsetPixels);
        rt.localScale = Vector3.one;
        rt.localRotation = Quaternion.identity;
        rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f); // z==0 ⇒ ignored by depth passes
        rt.SetAsLastSibling(); // draw after the face — the ring lives in the margin, nothing occludes it

        var img = go.GetComponent<Image>();
        img.sprite = SoftCueArt.FrameSprite(RingCornerRadiusPx);
        img.type = Image.Type.Sliced;
        img.fillCenter = false; // hollow — a frame, never a wash over the face
        img.color = GlowColor;
        img.raycastTarget = false; // MUST NOT eat the portrait's character-switch click
        go.layer = portrait.gameObject.layer;
        go.SetActive(false); // Apply activates it this same tick

        s_rings[entry] = img;
        return img;
    }

    /// <summary>
    /// Corner rounding (px) of this ring's outline. 0 = the ORIGINAL square-cornered ring — the portrait
    /// it frames is itself a hard rect, and this cue is already approved on hardware, so the shared
    /// factory is asked for exactly the sprite it always drew. (The item-card frame, which had to stop
    /// reading as a rectangle, asks the same factory for a rounded radius instead.)
    /// </summary>
    private const int RingCornerRadiusPx = 0;

    /// <summary>
    /// One line per entry the first time its ring lights: the resolved portrait, its world-space rect
    /// size, and the layer it renders on — so a "the ring is invisible" report is diagnosable from the
    /// hardware log alone (Info level: BepInEx's default disk config drops Debug). Never throws.
    /// </summary>
    private static void LogRingDiagOnce(InitiativeTrackActorBehaviour entry, Image ring)
    {
        if (!s_diagLogged.Add(entry))
            return;
        try
        {
            if (ring.transform is not RectTransform rt)
                return;
            rt.GetWorldCorners(s_diagCorners); // 0=BL,1=TL,2=TR,3=BR
            float w = (s_diagCorners[3] - s_diagCorners[0]).magnitude;
            float h = (s_diagCorners[1] - s_diagCorners[0]).magnitude;
            string portrait = rt.parent != null ? rt.parent.name : "?";
            VRLog.Info("Board", $"[SelectionReady] ring on portrait '{portrait}' (entry '{entry.name}'): " +
                                $"world {w:F3}x{h:F3} m, layer {LayerMask.LayerToName(ring.gameObject.layer)}.");
        }
        catch (System.Exception ex)
        {
            VRLog.Warn("Board", $"[SelectionReady] ring diag skipped ({ex.Message}).");
        }
    }
}
