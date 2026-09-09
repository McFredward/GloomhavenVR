using GloomhavenVR.Core;
using UnityEngine;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// A GRAB BAR FOR A <see cref="WorldSurface"/>'S FLOATED PANEL — the same rod, the same grip, the
/// same laser-carry and the same reel a floated <see cref="ModalFallback"/> window has, attached to
/// a panel that <see cref="ModalFallback"/> can never see.
///
/// <para><b>THE REPORT (user, 2026-09-03, verbatim):</b> <i>"Ich sehe nun das Fenster in dem
/// ausgewählt wird, allerdings ohne Greifbalken. Ich will das auch das wie jedes andere Fenster auch
/// behandelt wird und das Fenster normal verschiebbar ist wie alle anderen Fenster auch."</i> The
/// window is the map-side travel-event reward popup, floated by
/// <see cref="DistributeRewardSurface"/>. It went up in ModBuild 373 and could not be moved,
/// because nothing in the <see cref="WorldSurface"/> family had ever given a panel a handle.</para>
///
/// <para><b>WHAT THIS REUSES, AND WHAT IT DELIBERATELY DOES NOT.</b> "Drag a panel" is
/// <see cref="PanelGrabHandle"/> — it owns the palm grab, the laser-carry, the reel, the two-hand
/// resize, the head guard and the far bound — and "the rod the player grips" is
/// <see cref="GrabBarVisual"/>. BOTH ARE USED HERE UNCHANGED, through the same
/// <see cref="IPanelGrabOwner"/> seam <c>GrabbableModal</c>, <c>CombatLogSurface</c> and
/// <c>PlayTray</c> already plug into. So there is no second drag implementation in this mod: there
/// is one core with a fourth owner.</para>
///
/// <para><b>WHY NOT <c>GrabbableModal</c> ITSELF, WHICH IS THE MODAL WINDOWS' OWNER.</b> It was the
/// first choice and it is structurally unavailable to a surface, for one reason that is not a
/// matter of taste: <c>GrabbableModal.EnsureFrame</c> registers every holder it builds in
/// <c>GrabbableModal.LiveHolders</c>, and <c>ModalFallback.SweepOrphanChrome</c> — which runs every
/// <c>ChromeSweepSeconds</c> = 5 s from <c>ModalFallback.Tick</c>, on the map as well as in a
/// scenario — destroys every holder in that list that is not referenced by some
/// <c>ModalFallback.Converted[j].Grab</c>. A surface's holder is by construction owned by no
/// <c>WindowPanel</c> (these panels carry no <c>UIWindow</c> at all — that IS the premise of
/// <see cref="FloatingDecisionSurface"/>, and the ModBuild 373 log states it for this very popup:
/// "the conversion target 'UI Distribute Items Rewards Popup' carries no UIWindow"), so a
/// <c>GrabbableModal</c> built from here would be destroyed within five seconds with an
/// ORPHAN CHROME DESTROYED warning, and the same teardown sweep would take it at every bulk release.
/// Making it survivable needs an opt-out either in <c>ModalFallback.9.Spawn.cs</c> or in
/// <c>GrabbableModal.cs</c>; both files are owned by another lane this round, so the route was
/// rejected rather than half-taken. That is the ONLY blocker — everything else <c>GrabbableModal</c>
/// does (its <c>Tick</c>, its <c>LateSyncHost</c>, its <c>SyncSharedState</c>) is callable from a
/// surface as it stands.</para>
///
/// <para><b>AND THIS IS THE SMALL HALF OF IT.</b> What is NOT reimplemented here is everything
/// <c>GrabbableModal</c> carries for reasons a decision panel does not have: the shared-window
/// badge and the remote pose easing (multiplayer window sharing — a decision panel's pose is local
/// presentation and nothing about it goes on the wire), the ink-union bar placement (it needs
/// <c>PanelInkBounds</c>' committed union, which is fed by ModalFallback's fit machinery and does
/// not exist for these panels — the bar is placed off the host rect instead, which is exactly what
/// <c>GrabbableModal</c> itself falls back to when the ink cannot be measured), the close X and its
/// plate (see NO X, below), the diagnostic throttle and the pose lock. What is left is ~120 lines
/// of transform arithmetic that is a straight read of <c>GrabbableModal.SyncBar</c>'s frame-based
/// branch, with the same constants, so the two handles cannot drift in look or feel.</para>
///
/// <para><b>AND SINCE ModBuild 376 THEY CANNOT DRIFT IN BEHAVIOUR EITHER.</b> The rule that decides
/// when a size change reaches the rod at all is <see cref="BarSizeSettle"/>, a shared class both
/// this type and <c>GrabbableModal</c> instantiate per bar and per size term. It answers the report
/// of 2026-09-03 — <i>"Wenn sich das Fenster nicht wirklich vergrößert, sollte der Greifbalken auch
/// nicht größer werden"</i>, asked for <i>"allgemein"</i> — and its whole derivation, its settle
/// window and its symmetric shrink ruling live over there rather than being restated here, because
/// two copies of one rule is precisely how two handles drift.</para>
///
/// <para><b>NO CLOSE X, BY CONSTRUCTION AND NOT BY OMISSION.</b> The X is a SEPARATE call in
/// <c>ModalFallback.8.Convert</c> — <c>ModalCloseButton.Attach(panel, window)</c> — taken beside
/// the grab decision, not inside it, and its second argument is a <c>UIWindow</c> these panels do
/// not have. So the bar-without-an-X shape this class produces is the same shape the map room's
/// character screen ships in ("MODAL WINDOW: 'New Party display' (ID PartyPanel) floats WITHOUT an
/// X"): a handle to move it, and the game's own buttons as the only exit. For the distribute popup
/// that is not a nicety — <c>MapChoreographer.WaitDistributionEnds</c> is
/// <c>WaitUntil(() =&gt; !UIDistributeRewardManager.Instance.IsDistributing)</c> and only the
/// popup's own confirm button resolves it, while the map is input-locked by
/// <c>AdventureMapUIManager.LockOptionsInteraction</c>, so an X on it would be a button that hands
/// the player a hard deadlock. This class contains no close affordance and no code path that could
/// grow one; if one is ever wanted for some other surface it must be decided by that surface, not
/// by the handle.</para>
///
/// <para><b>IT CANNOT LOSE THE PANEL, and each half of that is somebody's existing guarantee rather
/// than a promise made here.</b>
/// <list type="bullet">
/// <item>A PALM carry moves the panel to wherever the hand is, which is within arm's reach by
/// definition.</item>
/// <item>A LASER carry is bounded by the reel, whose far bound is
/// <c>RayGrabDriver.MaxDistanceMeters</c> minus the panel's own reach — <c>PanelGrabHandle.ArmReel</c>
/// states the intent in those words: "the drag bar can never be pushed past the distance at which
/// the ray that pushed it would still find it. That is the honest definition of 'lost'".</item>
/// <item>The NEAR side is the handle's own per-frame head guard, which measures the panel as a rect
/// against the head and refuses the step.</item>
/// <item>ORIENTATION cannot be lost either: the one-hand carry yaws the panel with the wrist
/// (<see cref="PanelCarryMode.Level"/>), so <see cref="IPanelGrabOwner.OnGrabFinished"/> re-faces it
/// through the same <c>PanelPlacement.Facing</c> the spawn placement uses — a released panel reads
/// exactly like a freshly floated one.</item>
/// <item>And the universal rescue is untouched: every <see cref="FloatingDecisionSurface"/> gates on
/// <c>!FlatScreen.ManualScreenActive</c>, so the player's A/X chord releases the float and restores
/// the popup to the 2D composite, wherever in the room he left it.</item>
/// </list></para>
///
/// <para><b>AND IT CANNOT END A FLOAT.</b> Everything written from here is either a mod-owned
/// transform (the holder, the frame, the rod, the grab zone) or the game host's
/// position/rotation/localScale — which <see cref="FloatingDecisionSurface.Place"/> was already
/// writing through <c>CanvasConversion.PlaceHost</c> before this class existed. There is no
/// <c>Release</c>, no <c>SetActive</c> on game content, no write to any term of a surface's
/// <c>WantConverted</c>, and nothing on the wire. In particular <see cref="DistributeRewardSurface"/>'s
/// distribution HOLD (<c>_holdArmed</c> / <c>HoldForDistribution</c>) reads
/// <c>UIDistributeRewardManager.IsDistributing</c> and the popup's own <c>activeSelf</c>, neither of
/// which any line below touches.</para>
///
/// <para><b>MULTIPLAYER:</b> a panel pose is local presentation. No <c>NetProtocol</c> surface, no
/// record, no game state. The peer-shared-window machinery in <c>GrabbableModal</c> (the badge, the
/// glide, <c>SharedWindows.IsShared</c>) is deliberately absent rather than stubbed, because a
/// decision panel is never a shared window.</para>
/// </summary>
internal sealed class SurfaceGrabBar : IPanelGrabOwner
{
    // ---- geometry: every constant below USED TO BE declared here, copied by value and by name from
    //      GrabbableModal so the two handles would read as one piece of furniture. Copying is what
    //      made them one, and copying is also what would have let them drift — so the whole set,
    //      and the eight lines of derivation both files spent on it, now lives in one place
    //      (WorldUI.GrabBarLayout) that this class, GrabbableModal and CombatLogSurface all call.
    //      Nothing about the shipped numbers moved.
    /// <summary>The rod's nominal radius — the design sheet's value, shared with every other bar.</summary>
    private const float BarRadius = GrabBarLayout.BarRadius;

    /// <summary>Draw-order offset for the rod's three renderers: it must paint OVER its own panel.</summary>
    private const int BarOrderOffset = GrabBarLayout.BarOrderOffset;

    /// <summary>Below this the release re-face writes nothing (and says nothing).
    /// <see cref="WindowReFacePolicy.ReFaceEpsilonDeg"/> rather than a local 0.5f: this file and
    /// <c>GrabbableModal</c> each declared the same constant under the same name.</summary>
    private const float ReFaceEpsilonDeg = WindowReFacePolicy.ReFaceEpsilonDeg;

    /// <summary>Pose delta that counts as "this panel is moving" for
    /// <see cref="ConvertedPanel.GuardHostMoving"/> — the same 5 mm epsilon GrabbableModal uses.</summary>
    private const float MovingEpsilonMeters = 0.005f;

    private ConvertedPanel? _panel;
    private string _logName = "surface panel";

    /// <summary>The surface's own extra shrink on the host (the float factor), so the bar and the
    /// host agree about how big a host pixel is.</summary>
    private float _extraScale = 1f;

    /// <summary>The diorama scale CAPTURED AT BUILD, never the live one — the same ruling
    /// GrabbableModal follows (item 2, "no auto-scale with world zoom"): zooming the diorama after
    /// the panel is up must not grow or shrink it under the player's hands.</summary>
    private float _spawnWorldScale = 1f;

    private Transform? _holder;     // scene root, identity pose and identity scale
    private Transform? _frame;      // the GRAB ROOT at the panel centre; localScale = user factor
    private GrabBarVisual? _bar;    // the drawn rod: shaft + two caps, one material
    private readonly WindowReFaceTween _reFaceTween = new();
    private GrabBarTween? _barTween; // THE ONE WRITER of the rod's presented pose — see GrabBarTween
    private BoxCollider? _grabZone; // the palm zone, on the frame, offset down to the rod
    private PanelGrabHandle? _handle;

    private Vector3 _lastHostPos;
    private bool _lastHostPosValid;
    private bool _userMoved;

    /// <summary>
    /// THE SHARED SETTLE RULE, one instance per size term and per bar (see
    /// <see cref="BarSizeSettle"/> for the report it answers and for the whole derivation).
    /// <c>GrabbableModal</c> holds the same pair for the same two terms and feeds them the same
    /// quantities, so the two handles cannot drift in behaviour any more than the constants above
    /// let them drift in look.
    /// </summary>
    private readonly BarSizeSettle _widthSettle = new("width");
    private readonly BarSizeSettle _heightSettle = new("height");

    /// <summary>True while a hand grips the bar.</summary>
    internal bool IsGrabbed => _handle != null && _handle.IsGrabbed;

    /// <summary>
    /// Latched the first time the player grips this panel, and never cleared for the life of the
    /// float. Exposed for the same reason <c>GrabbableModal.UserMoved</c> is: from that moment the
    /// pose belongs to the player and no automatic re-placement may take it back. Today nothing
    /// re-places a floated decision panel (<see cref="FloatingDecisionSurface.Place"/> is a
    /// once-per-conversion write guarded by its own <c>_placed</c> latch), so this flag has no
    /// consumer yet — it is published rather than inferred because the NEXT surface that wants a
    /// re-place must be able to ask, and re-deriving "has the player touched this" from a live
    /// grab state would answer no for every panel he moved and let go of.
    /// </summary>
    internal bool UserMoved => _userMoved;

    /// <summary>
    /// THE ROD IS OFF BECAUSE THE PANEL HAS NOTHING ON IT — the ModBuild 378 rule, carried into the
    /// surface family by <see cref="SurfaceMaterialise"/>.
    ///
    /// <para><b>USER, 2026-09-03, verbatim:</b> <i>"Wenn kein Fenster inhalt hat soll neben der
    /// Animation auch kein Greifbalken erscheinen."</i> On the modal side that is one stored verdict
    /// — <c>ModalFallback.AppearStillOwed</c>, which <c>GrabbableModal.SyncBarVisibility</c> reads —
    /// so the dust and the rod cannot disagree about whether a window has content. This flag is the
    /// same seam for a surface panel, and the verdict behind it is likewise stored ONCE, by
    /// <see cref="SurfaceMaterialise"/>, and read from here: this class measures nothing.</para>
    ///
    /// <para>It also carries the CLOSE edge. The moment the game hides one of these popups its
    /// whole subtree goes inactive in the same statement (<c>UIDistributePointsPopup.Hide</c> →
    /// <c>window.SetActive(false)</c>), and the surface only notices on its next Update tick — so
    /// without this the rod would hang in the room for a frame with no window on it, which is the
    /// artefact the user photographed three of (.planning/debug/leeres_fenster2.jpg).</para>
    ///
    /// <para><b>IT CANNOT STRAND THE PANEL.</b> Nothing here touches the host, the conversion, any
    /// <c>WantConverted</c> term or any game state — it writes the mod-owned rod's own
    /// <c>MeshRenderer.enabled</c> and one bool that <see cref="IPanelGrabOwner.GrabVisible"/>
    /// consults, so the worst a wrong value can do is hide a handle. The panel itself, its buttons
    /// and the game's confirm are untouched.</para>
    /// </summary>
    private bool _withheld;

    /// <summary>
    /// Withhold or restore the rod. Idempotent; safe before the handle is built (the value is
    /// re-asserted from <see cref="Tick"/>, so a withhold taken at the reveal edge survives a later
    /// reveal walk re-enabling the recorded renderer set).
    /// </summary>
    internal void SetWithheld(bool withheld)
    {
        if (withheld)
            _reFaceTween.Cancel();
        _withheld = withheld;
        // 2026-09-03 ("es ploppt") — the RELEASE grows the rod from zero at its place; the
        // WITHHOLD itself stays instant, and that is not an omission: both of its callers are
        // edges on which the rod must not be seen at all — SurfaceMaterialise's reveal-edge
        // verdict for a panel that has never drawn, and BeginVanish, where the window's own
        // pixels are leaving on this frame (.planning/debug/leeres_fenster2.jpg is a rod that
        // outlived its window by ONE frame; a 150 ms shrink would be thirteen of them).
        if (_barTween != null)
        {
            bool gateHidden = _panel == null || !_panel.IsAlive || _panel.RenderHidden
                              || _panel.OwnerRenderHidden;
            string? snapWhy = withheld
                ? "the rod leaves with the window's pixels (a withhold is a never-drew or a vanish verdict)"
                : gateHidden ? "the panel is behind the reveal gate"
                : (_handle != null && _handle.IsGrabbed) ? "a hand is carrying the panel"
                : null;
            _barTween.SetVisible(!withheld, snapWhy,
                withheld ? "SurfaceGrabBar.SetWithheld (withheld)"
                         : "SurfaceGrabBar.SetWithheld (released: the panel draws)");
        }
        ApplyWithheld();
    }

    private void ApplyWithheld()
    {
        if (_bar == null)
            return;
        // The tween's Shown is ANDed in for symmetry with GrabbableModal, though it can never
        // widen this: a withhold snaps the tween to hidden on the same call, so Shown is false
        // whenever _withheld is true. Kept as one expression so a later eased withhold needs no
        // second rule here.
        bool want = !_withheld || (_barTween != null && _barTween.Shown);
        System.Collections.Generic.IReadOnlyList<MeshRenderer> rs = _bar.Renderers;
        for (int i = 0; i < rs.Count; i++)
        {
            MeshRenderer r = rs[i];
            // Written only on a DIFFERENCE: this runs every tick and an unconditional write would
            // fight the panel's own reveal walk over the same renderers every frame.
            if (r != null && r.enabled != want)
                r.enabled = want;
        }
    }

    /// <summary>
    /// Build the handle for a freshly floated, freshly PLACED host: the frame is seeded at the
    /// host's current world pose, so the first follow tick keeps the panel exactly where the
    /// surface put it and nothing jumps on the frame the bar appears.
    /// </summary>
    /// <param name="extraScale">The surface's own float shrink — the same factor it passed to
    /// <c>CanvasConversion.PlaceHost</c> as part of the world scale. Kept separate here because the
    /// bar's metres-per-host-pixel must be the host's own, not the diorama's.</param>
    internal void Build(ConvertedPanel panel, float extraScale, float worldScale, string logName)
    {
        _reFaceTween.Cancel();
        _panel = panel;
        _extraScale = extraScale;
        _spawnWorldScale = Mathf.Max(worldScale, 0.01f);
        _logName = logName;
        // BEFORE EnsureFrame, which ends in the first Tick. A settler carrying a previous float's
        // value would hold the old rod size for a whole settle window on a panel it was never
        // measured against; the first sample after a Reset is adopted immediately instead.
        _widthSettle.Reset();
        _heightSettle.Reset();
        EnsureFrame();
        if (_frame != null && panel.HostGo != null)
        {
            Transform h = panel.HostGo.transform;
            _frame.SetPositionAndRotation(h.position, h.rotation);
            _frame.localScale = Vector3.one; // user factor 1x
        }
        Tick(); // size the rod and re-write the host from the frame in the same frame
    }

    /// <summary>
    /// The game-owned host follows the mod-owned grab frame — position, rotation and scale — and
    /// the rod follows the host's live rect. Allocation-free; called once per surface tick.
    /// </summary>
    internal void Tick()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _panel.HostRect == null)
            return;
        EnsureFrame();
        if (_holder == null || _frame == null)
            return;

        // THE HOLDER STAYS AT IDENTITY SCALE. GrabbableModal carries the same line and the same
        // warning: scaling the holder ties the frame's WORLD position to the diorama scale, and a
        // world scale that settles at scene start then drags the panel toward the origin with it.
        _holder.localScale = Vector3.one;
        if (!_holder.gameObject.activeSelf)
            _holder.gameObject.SetActive(true);

        bool held = _handle != null && _handle.IsGrabbed;
        if (!_userMoved && held)
        {
            _userMoved = true;
            // HW-VERIFY: THE ANSWER TO THE REPORT. The user asked for these panels to be
            // "normal verschiebbar wie alle anderen Fenster auch"; this line is the only evidence
            // that a hand actually took one. Printed ONCE per conversion (the latch), so it cannot
            // become drag spam. If the SURFACE GRAB BAR line is in the log and this one is not, the
            // handle was built but could not be gripped — look at GrabVisible's terms (RenderHidden
            // / OwnerRenderHidden) and at whether the rod was on the screen at all.
            VRLog.Note("WorldUI", $"SURFACE WINDOW: '{_logName}' grabbed — its pose is now "
                                  + "PLAYER-OWNED. Nothing re-places a floated decision panel today "
                                  + "(the surface places it once per conversion), so this is the "
                                  + "claim being recorded, not a re-placement being refused.");
        }

        // PUBLISHED FOR THE SUPERSAMPLER, which defers its sharpening repair while a hand is on a
        // window (PanelSupersample.HandOn). Without this write the repair would fire mid-drag on
        // exactly the panels this class just made draggable. It can only ever DEFER work — nothing
        // downstream of it releases a conversion — so a wrong value here is a cost, never a loss.
        _panel.GuardHostHeld = held;

        SyncHost();
        SyncBar(_panel.HostRect.rect);
        // RE-ASSERTED EVERY TICK, and only where it differs. The panel's reveal walk owns the same
        // three MeshRenderers (the holder is registered with CanvasConversion.AddRenderRoot), so a
        // withhold taken at the reveal edge would be undone by the next show pass without this.
        ApplyWithheld();
    }

    /// <summary>
    /// Re-copy the frame onto the host in LateUpdate, AFTER every Update-phase writer in the
    /// process has run. Driven by <see cref="HostLateSync"/> on the holder itself rather than by a
    /// surface <c>LateTick</c>, because <c>WorldUIModule</c>'s generic late pass covers only the
    /// slot-layout surfaces and adding the decision surfaces to it would be an edit outside this
    /// lane's owned paths — and because a MonoBehaviour on the holder cannot get out of step with
    /// the holder's own lifetime.
    ///
    /// <para>WHY IT IS NEEDED AT ALL (this is GrabbableModal.LateSyncHost's root cause, unchanged):
    /// <see cref="PanelGrabHandle"/> moves the FRAME from its own MonoBehaviour <c>Update</c>,
    /// which has NO defined execution order against <c>WorldUIModule.Update</c>. On every frame the
    /// handle runs later, the host was synced from the PREVIOUS pose and the panel renders one
    /// frame behind the rod that is carrying it — visible as the content shearing off its own
    /// handle during a fast drag. Unity runs every LateUpdate after every Update, so a copy written
    /// here is ordering-proof by rule instead of by luck. For a panel nobody is holding the frame
    /// is static and this is a change-free write.</para>
    /// </summary>
    private void LateSync()
    {
        if (_panel == null || !_panel.IsAlive || _panel.HostGo == null || _frame == null)
        {
            _reFaceTween.Cancel();
            return;
        }
        if ((_handle != null && _handle.IsGrabbed) || !_panel.HostGo.activeInHierarchy
            || _panel.RenderHidden || _panel.OwnerRenderHidden || _withheld
            || !_frame.gameObject.activeInHierarchy)
            _reFaceTween.Cancel();
        _reFaceTween.Advance(_frame);
        SyncHost();
    }

    /// <summary>Copy the grab frame's pose and the user's size factor onto the game-owned host.</summary>
    private void SyncHost()
    {
        if (_panel == null || _panel.HostGo == null || _frame == null)
            return;
        // The user grab factor rides the SHARED range the two-hand pinch clamps to; a tighter floor
        // here would silently re-cap what the pinch was allowed to shrink.
        float factor = Mathf.Clamp(_frame.localScale.x, PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        Transform host = _panel.HostGo.transform;
        Vector3 pos = _frame.position;
        host.SetPositionAndRotation(pos, _frame.rotation);
        host.localScale = Vector3.one * (metersPerPixel * _spawnWorldScale * _extraScale * factor);

        _panel.GuardHostMoving = _lastHostPosValid
                                 && (pos - _lastHostPos).sqrMagnitude
                                    > MovingEpsilonMeters * MovingEpsilonMeters;
        _lastHostPos = pos;
        _lastHostPosValid = true;
    }

    /// <summary>
    /// Place and size the rod and the palm zone under the panel's live rect.
    ///
    /// <para>Measured off the HOST RECT and not off the drawn ink, deliberately. GrabbableModal
    /// prefers the ink union so a window that draws in a corner of an oversized frame still gets
    /// its handle under what is visible — but that union comes from <c>PanelInkBounds</c>' committed
    /// capture, which ModalFallback's fit machinery feeds and which does not exist for a surface
    /// panel. The frame-based branch below is byte-for-byte the one GrabbableModal itself falls
    /// back to whenever the ink cannot be measured, so this is its documented fallback rather than
    /// a second rule. It is also the right one here on the merits: a decision popup is a tight
    /// panel that fills its own frame (the ModBuild 373 log measures the reward popup's content at
    /// 416x464 px inside a 368x500 px frame — it OVERFLOWS its rect rather than rattling inside
    /// it), so an ink union would move the bar by pixels.</para>
    /// </summary>
    private void SyncBar(Rect rect)
    {
        if (_bar == null || _grabZone == null)
            return;
        float metersPerPixel = WorldUIConfig.CanvasScaleMm.Value * 0.001f;
        // Frame-local metres per host pixel. The frame's own localScale carries the user grab
        // factor, so it must NOT appear here — otherwise the bar would grow twice with a pinch.
        float unit = metersPerPixel * _extraScale * _spawnWorldScale;
        float worldScale = _spawnWorldScale;

        // THE TWO SIZE TERMS GO THROUGH THE SHARED SETTLE RULE, and only these two: a change in
        // either makes the ROD itself bigger or smaller, which is the whole of the 2026-09-03
        // report ("Wenn sich das Fenster nicht wirklich vergrößert, sollte der Greifbalken auch
        // nicht größer werden"). Everything else below is POSITION and is deliberately ungated —
        // see BarSizeSettle's class comment for why a lagging handle would be the worse artefact.
        //
        // The visibility term is the SAME predicate IPanelGrabOwner.GrabVisible answers with —
        // "grabbing something that is not there" and "gating a size the player cannot see" are the
        // same question about the same rod. A change made behind the reveal gate is adopted at once;
        // see BarSizeSettle's OFF-SCREEN CHANGES ARE FREE block for the closed bug that depends on
        // it (a decision panel is built, fitted and only then revealed).
        bool onScreen = _panel != null && _panel.IsAlive && !_panel.RenderHidden
                        && !_panel.OwnerRenderHidden && !_withheld
                        && _holder != null && _holder.gameObject.activeInHierarchy;
        float panelHeight = _heightSettle.Apply(rect.height * unit / Mathf.Max(worldScale, 1e-4f),
                                                onScreen, _logName);
        float sourceWidth = _widthSettle.Apply(rect.width * unit, onScreen, _logName);

        // ONE CALL FOR EVERY DIMENSION THE ROD HAS, shared with GrabbableModal and CombatLogSurface.
        // ONE settled width feeds BOTH fractions inside it, so the drawn rod and the palm zone can
        // never disagree about how wide the panel is, and the MinBarWidth floor is applied there
        // rather than by the settle rule for the reason GrabBarLayout.Solve states.
        GrabBarLayout.Rod rod = GrabBarLayout.Solve(sourceWidth, panelHeight, worldScale);
        float rodScale = rod.Scale;
        float zoneDepth = rod.ZoneDepth;
        float barWidth = rod.BarWidth;
        float zoneWidth = rod.ZoneWidth;

        // rect.yMin / rect.center are taken from the RectTransform rather than assuming a centred
        // pivot: the host is created by CanvasConversion and is pivot-centred today, but a bar
        // hung off an assumed pivot is a bar that silently detaches the day one is not.
        float x = rect.center.x * unit;
        float y = rect.yMin * unit - rod.Gap;

        // 2026-09-03 ("es ploppt") — TARGETS, NOT WRITES: the root position, the uniform scale, the
        // length (barWidth is in FRAME-local metres and SetLength wants the ROD's own, under a root
        // scaled by rodScale — divided back out so the drawn end-to-end length is barWidth exactly)
        // and the palm zone's box go to GrabBarTween, which eases the drawn rod toward them in
        // LateUpdate. Snapped, never eased, while a hand carries the panel or while the rod is off
        // the screen — the same two exemptions GrabbableModal.SyncBar states, for the same reasons.
        bool carried = _handle != null && _handle.IsGrabbed;
        string? snapWhy = carried
            ? "a hand is carrying the panel"
            : !onScreen ? "the rod is off the screen (behind the reveal gate or withheld)" : null;
        _barTween?.SetTarget(new Vector3(x, y, 0f), rodScale, barWidth / rodScale,
                             new Vector3(zoneWidth, zoneDepth, zoneDepth), snapWhy,
                             "SurfaceGrabBar.SyncBar (the host rect)");
    }

    // ---- IPanelGrabOwner ---------------------------------------------------------------------

    Transform? IPanelGrabOwner.GrabRoot => _frame;

    // A panel the reveal gate is still hiding, or one its own surface render-hid, must not be
    // grabbable either: "grabbing something that is not there" is the standing ruling, and
    // PanelGrabHandle.CanGrab consults this for BOTH grab paths (the palm candidate scan and the
    // laser's bar-collider ray test), so one property covers both.
    // ModBuild 378's rule joins the same predicate rather than sitting beside it: a rod that is
    // WITHHELD because the panel is drawing nothing is exactly "something that is not there".
    bool IPanelGrabOwner.GrabVisible =>
        _panel != null && _panel.IsAlive && !_panel.RenderHidden && !_panel.OwnerRenderHidden
        && !_withheld
        && _holder != null && _holder.gameObject.activeInHierarchy;

    // Carry the yaw with the hand, level in the plain WORLD frame — the same mode every floated
    // window uses. Nothing else authors this panel's rotation, so there is no two-writer jitter.
    PanelCarryMode IPanelGrabOwner.CarryMode => PanelCarryMode.Level;
    Quaternion IPanelGrabOwner.GrabLevelFrame => Quaternion.identity;
    Vector2 IPanelGrabOwner.GrabPitchLimits => new(-180f, 180f);

    /// <summary>A decision panel has no apparent-size ruling, so the handle's generic factor range
    /// IS its resize window — verbatim what GrabbableModal returns.</summary>
    Vector2 IPanelGrabOwner.GrabScaleLimits => new(PanelGrabHandle.MinScale, PanelGrabHandle.MaxScale);

    /// <summary>
    /// RE-FACE ON RELEASE. The one-hand carry yaws the panel with the WRIST, so a drag to the side
    /// leaves it turned to wherever the hand happened to point — readable only edge-on, and for a
    /// panel with no close X that is the difference between "moved" and "lost". The moment the LAST
    /// hand lets go, the target is captured through <c>PanelPlacement.Facing</c>, the same formula
    /// the spawn placement uses. A short cubic ease-out turns it toward that fixed target using
    /// the grab bar's duration, so a moved panel reads exactly like a freshly floated one.
    ///
    /// <para><b>THE PANEL DOES NOT TRAVEL.</b> The turn is about the frame origin, which for these
    /// hosts IS the drawn centre: <c>CanvasConversion</c> creates a pivot-centred host and
    /// <see cref="FloatingDecisionSurface.Place"/> seats the frame on it, so the arc-swing
    /// GrabbableModal had to solve with an ink pivot (ModBuild 240 — a window whose ink sat 859 mm
    /// off its frame origin travelled two thirds of a metre on a 44° re-face) cannot arise here.
    /// It is stated rather than assumed because it is a property of how the frame is SEATED, and
    /// the day a surface seats it somewhere else this comment is the thing that is wrong.</para>
    ///
    /// <para><b>AND IT IS THE PLAYER'S DIAL THAT DECIDES WHETHER IT HAPPENS AT ALL</b> (ModBuild
    /// 439, survey row R1). This class was written twelve days after <c>[WorldUI] WindowFacing</c>
    /// landed and re-faced UNCONDITIONALLY, so a player who had set the dial to <i>Nie</i> kept his
    /// modal windows at the angle he let go at and watched every floating decision panel snap round
    /// anyway — against a description that promises <i>"das bisherige Verhalten aller Fenster"</i>.
    /// The verdict now comes from <see cref="WindowReFacePolicy"/>, the same policy
    /// <c>GrabbableModal</c> asks, so there is one answer per release instead of one per file. A
    /// decision panel is never a shared window (see the MULTIPLAYER paragraph on the class), so the
    /// first gate is passed a constant <c>false</c> rather than a stub.</para>
    /// </summary>
    void IPanelGrabOwner.OnGrabFinished()
    {
        _reFaceTween.Cancel();
        if (_frame == null)
            return;
        if (!WindowReFacePolicy.WantsReFaceOnRelease(
                shared: false,
                laserGrab: _handle != null && _handle.LastGrabWasLaser,
                kind: "SURFACE WINDOW",
                logName: _logName))
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;
        Quaternion facing = PanelPlacement.Facing(_frame.position, head.transform.position);
        float turned = Quaternion.Angle(_frame.rotation, facing);
        if (turned < ReFaceEpsilonDeg)
            return;
        _reFaceTween.Begin(_frame, _frame.position, facing);
        VRLog.Info("WorldUI", $"SURFACE WINDOW: '{_logName}' released after a move — re-facing the "
                              + $"player by {turned:F1}° over {GrabBarTween.DurationSeconds * 1000f:F0} ms, "
                              + "about the frame origin (= the host's own centre). "
                              + "The panel does not travel: only the orientation changes.");
    }

    // ---- construction ------------------------------------------------------------------------

    /// <summary>
    /// Build the scene-root holder tree. A SCENE ROOT and not a child of the host, because the host
    /// FOLLOWS the frame rather than the other way round — which is also why
    /// <see cref="Destroy"/> has to be called explicitly: nothing about releasing the panel can
    /// reach this tree.
    /// </summary>
    private void EnsureFrame()
    {
        if (_holder != null && _frame != null)
            return;
        // NOTHING IS BUILT WITHOUT A PANEL. GrabbableModal carries the same guard for a reason this
        // project paid a build for: an un-built handle is a brass bar hanging in mid-air with no
        // window on it, and it looks exactly like a real one (ModBuild 225/226,
        // .planning/debug/leeres_fenster.jpg).
        if (_panel == null)
            return;

        var holderGo = new GameObject($"GloomhavenVR.SurfaceGrab_{_logName}");
        _holder = holderGo.transform;
        holderGo.AddComponent<HostLateSync>().Owner = this;

        var frameGo = new GameObject("Frame");
        _frame = frameGo.transform;
        _frame.SetParent(_holder, worldPositionStays: false);

        // Style Generic is the WINDOW rod (dark oiled walnut, small aged-brass knobs) — the same one
        // every floated modal wears, so a decision panel is not visibly a different kind of object.
        // overlay:true puts it on GloomhavenVR/Overlay with _ZWrite forced on, which is what makes
        // it draw solid over the panel's own content while a hand physically in front still
        // occludes it.
        GrabBarVisual bar = GrabBarVisual.Build(_frame, "Bar", GrabBarStyle.Generic,
                                                BarRadius, overlay: true);
        // ALL THREE RENDERERS ride the draw-order ladder, not just the shaft: registering one would
        // sort the shaft over the panel and leave both knobs behind it — a bar with its ends bitten
        // off.
        for (int i = 0; i < bar.Renderers.Count; i++)
            CanvasConversion.RegisterOrderFollower(_panel, bar.Renderers[i], BarOrderOffset);
        _bar = bar;

        // The LASER's target is a capsule down the rod's own axis, which SetLength keeps in step;
        // the PALM's target is the generous box on the frame below. The split is deliberate and is
        // the lost-menu fix: the far ray must land only on the visible strip.
        Collider barCollider = bar.AttachLaserTarget();

        // Collider BEFORE the handle: PanelGrabHandle.OnEnable registers whatever Collider is on its
        // own GameObject, and AddComponent runs OnEnable immediately.
        _grabZone = frameGo.AddComponent<BoxCollider>();
        _grabZone.isTrigger = true;
        _grabZone.size = new Vector3(0.25f, 0.05f, 0.05f);
        // THE TWEEN OWNS THE ROD'S POSE FROM HERE ON (2026-09-03): SyncBar and SetWithheld set
        // targets, WorldUIModule's GrabBarTween.Late step eases the drawn rod, its laser capsule
        // and this palm zone toward them. Seeded from the withhold verdict already taken, so a
        // panel withheld before its frame existed starts at zero and grows when released.
        _barTween = new GrabBarTween(bar, _grabZone, _logName, visible: !_withheld);
        _handle = frameGo.AddComponent<PanelGrabHandle>();
        // bar.Renderer is the SHAFT, and all three pieces share ONE Material — so the handle's
        // single sharedMaterial.color write in OnGrabHighlight lights the whole rod.
        _handle.Init(this, bar.Renderer, "WorldUI", $"{_logName} panel");
        _handle.SetBarCollider(barCollider);

        // Render-only mod layer; grabs and pokes route through the registries, not through layers.
        // After the rod exists, because it walks the tree it is given.
        VRLayers.Apply(holderGo);

        // The holder is a SCENE-ROOT tree, so the panel's reveal gate (which walks the host) could
        // never hide the rod on its own — the handle would pop into the room at the pre-fit size
        // before its panel drew anything, which is the "Aufploppen der Greifbar" the user reported
        // for the modal windows. Registering it as an extra render root makes the panel's own hide
        // walk this tree too.
        CanvasConversion.AddRenderRoot(_panel, _holder);

        // HW-VERIFY: the answer to "wie jedes andere Fenster auch". Its presence names the panel
        // that got a handle; its ABSENCE for a panel the player says he cannot move is the whole
        // diagnosis, because the only two ways to reach it are a null ConvertedPanel and a surface
        // that never called Build.
        VRLog.Note("WorldUI", $"SURFACE GRAB BAR: '{_logName}' is now a grabbable/scalable world "
                              + "element — one hand on the rod moves it (palm grip or laser-carry "
                              + "with the stick reel), two hands resize it "
                              + $"{PanelGrabHandle.MinScale:0.##}x-{PanelGrabHandle.MaxScale:0.##}x, "
                              + "and the last hand off re-faces it to the player. It carries NO "
                              + "close X, deliberately: these panels have no UIWindow to close and "
                              + "the game's own confirm button is the only thing that resolves the "
                              + "promise the campaign is waiting on.");
    }

    /// <summary>Destroy the mod-owned holder. The game-owned host is released by the surface,
    /// separately — this class never releases a conversion.</summary>
    internal void Destroy()
    {
        _reFaceTween.Cancel();
        if (_holder != null)
            Object.Destroy(_holder.gameObject);
        _holder = null;
        _frame = null;
        _bar = null;
        _barTween?.Release(); // leaves the LateUpdate tick list; the rod it presented is gone
        _barTween = null;
        _grabZone = null;
        _handle = null;
        _panel = null;
        _lastHostPosValid = false;
        _userMoved = false;
        // A withhold describes a panel that no longer exists. Cleared here as well, so an instance
        // that is destroyed and rebuilt does not start life with the previous float's verdict.
        _withheld = false;
        // The settled sizes describe a rect that no longer exists. Cleared here as well as in
        // Build, so an instance that is destroyed and never rebuilt leaves no state behind either.
        _widthSettle.Reset();
        _heightSettle.Reset();
    }

    /// <summary>
    /// The LateUpdate pump for <see cref="LateSync"/>, on the holder itself. An ordinary
    /// MonoBehaviour: Unity guarantees every LateUpdate runs after every Update, which is the whole
    /// point (see <see cref="LateSync"/>).
    /// </summary>
    private sealed class HostLateSync : MonoBehaviour
    {
        internal SurfaceGrabBar? Owner;

        private void LateUpdate() => Owner?.LateSync();
        private void OnDisable() => Owner?._reFaceTween.Cancel();
    }
}
