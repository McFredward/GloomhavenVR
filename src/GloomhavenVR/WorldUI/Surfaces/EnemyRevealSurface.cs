using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.UI;

namespace GloomhavenVR.WorldUI.Surfaces;

/// <summary>
/// Enemy round reveal — the monster ability cards the game shows after every player
/// confirmed card selection ("what will the enemies do") — as a TRANSIENT world panel
/// floating above the diorama (test #20: the reveal was lost on the control board).
///
/// Mechanism (verified in decompiled GH.Runtime): Choreographer's
/// <c>MonsterClassesToSelectAbilityCards</c> handler calls
/// <c>InitiativeTrack.ShowMonsterClassesForSelectingRoundAbilityCards</c>
/// (Choreographer.cs:3715), which sets <c>enemyCardsBlocker.raycastTarget = true</c>
/// (InitiativeTrack.cs:747) and animates one <c>MonsterBaseUI</c> card per monster
/// class — all pooled under the serialized <c>enemyCardsHolder</c> Transform
/// (InitiativeTrack.cs:47; re-parented there by
/// <c>InitiativeTrackEnemyBehaviour.SetCardHolder</c>, :188-195) — via fade/scale/move
/// (MonsterBaseUI.AnimateAppearance, :239-271). The Continue ready button ends it:
/// <c>PostEnemyCardAnimationProceed</c> clears the blocker and <c>Deselect()</c>s every
/// card (<c>SetActive(false)</c>, InitiativeTrack.cs:519-524) before
/// <c>Choreographer.Pass()</c>.
///
/// Where it rendered before: <c>enemyCardsHolder</c> is a child of the InitiativeTrack
/// root, so the reveal rode the tray-docked <see cref="InitiativeTrackSurface"/> host —
/// unfitted (the track's content fit is scoped to <c>initiativeTrackHolder</c>, test
/// #16), positioned relative to the shrunk portrait row: effectively invisible in VR.
///
/// LEVEL-TRIGGERED on the game's own visibility state, polled per frame (no edge
/// events): reveal visible ⇔ <c>enemyCardsBlocker.raycastTarget</c> (flips exactly at
/// reveal start/end, the only two writes in the decompile) AND at least one active card
/// under <c>enemyCardsHolder</c> (the blocker rises a few frames before the staggered
/// card animations start). While visible, the holder subtree is converted onto its own
/// host (<see cref="CanvasConversion"/> — the REAL widgets, native art/text) floating
/// in the player's forward VIEW FOCUS — anchored in front of the HEAD at a comfortable
/// reading distance (the settings panel's "spawn in view" / ModalFallback.PlaceAtHmd
/// pattern via <see cref="PanelPlacement.Spawn"/>), upright and facing the head, its
/// POSITION and yaw lazily easing back to re-centre only when the head has clearly turned
/// or moved, at a fixed comfortable REAL distance/size in the player's forward view —
/// COMPLETELY INDEPENDENT of the control board (user #2): its world pose is driven ONLY by
/// the head pose and the player-frame scale (<see cref="HeadFrameScale"/>), so moving,
/// repositioning or zooming the board / tray / initiative-track dock never shifts it — with ONE
/// deliberate, one-way exception since item 12 (<see cref="ApplyBoardClearance"/>): a NEW plant
/// (spawn / recentre / lazy-follow goal) is raised if the gaze would put it BEHIND the control
/// board, because the player is looking down at that board exactly when the reveal appears. That
/// clamp only ever moves the TARGET, never a standing panel, so board motion still cannot bob it.
/// At the
/// shared tray density; on hide it is released back to its exact 2D home
/// inside the track (whether
/// that home is currently the docked track host or the screen-space canvas — the
/// restore is parent-relative either way). Nested canvases inside the subtree are
/// handled entirely by CanvasConversion's neutralization + periodic sweep; this class
/// deliberately never touches Canvas components, so it is agnostic to how the
/// neutralization is implemented (disable vs override-off).
///
/// DISPLAY-ONLY: converted non-pokeable (never registered in UguiPokeSurfaces, so
/// neither the laser nor the fingertip nor the IsPointerOverUI patch ever see it), the
/// host raycaster is kept dark against the lock-mirror re-enable (StatPanelSurface
/// pattern), and no mode is asserted — the game's own reveal modality (the blocker +
/// the Continue ready button, mirrored on the button cluster) stays in charge.
/// </summary>
internal sealed class EnemyRevealSurface
{
    /// <summary>
    /// Per-panel multiplier on the shared tray density (test #17 pattern): 0.6 renders
    /// the same content pixels ~1.67x larger — the objectives' verified readability
    /// bump, sensible here too since the cards are read from board distance.
    /// </summary>
    private const float DensityScale = 0.6f;

    /// <summary>Hard width cap, real meters — many monster classes must not span the room.</summary>
    private const float MaxWidthMeters = 1.2f;

    /// <summary>The fitted host width must hold steady this long before the fit is pinned.</summary>
    private const float FitPinSettleSeconds = 1f;

    /// <summary>
    /// Item 8: only a growth larger than this FRACTION of the current max width restarts
    /// the settle dwell. The active monster card's highlight keeps pulsing a pixel or two
    /// larger, which under the old flat 0.5 px threshold reset the clock every frame so
    /// the fit never settled — hysteresis lets the small pulse-growth pass without
    /// resetting, so the reveal actually pins.
    /// </summary>
    private const float FitPinGrowthFraction = 0.03f;

    /// <summary>
    /// Item 8 hard fallback: pin the fit this long after the reveal was first measured,
    /// regardless of continued pulsing — the full layout is in by then, and a
    /// never-settling pulse must not leave the content re-fitting/twitching forever.
    /// </summary>
    private const float FitPinHardTimeoutSeconds = 2.5f;

    /// <summary>
    /// Mip-bake rescan cadence while the reveal is up (mirrors <c>CardFace.MipRescanInterval</c>,
    /// halved). MIP BAKE was added here for the 2026-08 report "Die Linien und Rahmen auf allen
    /// Karten und den Gegnerinfos haben wieder starkes Aliasing": these ARE monster ability
    /// cards — the same mipless game atlases and the same 512² mipless monster portraits the
    /// player's own cards were fixed for — shown on a world quad at board distance, and this
    /// surface had never been wired to <see cref="PanelMipBake"/> at all. The cadence (rather
    /// than a single pass at conversion) is load-bearing: the cards animate in STAGGERED
    /// (<c>MonsterBaseUI.AnimateAppearance</c>, delayAnimationDraw) and their art loads async,
    /// so a convert-time-only pass would bake the first card and leave the rest shimmering.
    /// </summary>
    private const float MipRescanInterval = 0.5f;

    /// <summary>Unscaled time of the next mip-bake rescan; 0 = due now.</summary>
    private float _nextMipRescan;

    // LAZY FOLLOW — ALL AXES (X/Z *and* Y), computed in the RIG-LOCAL frame. User #4's 11th
    // clarification ("I DO want the lazy movement so the enemy info stays in my field of view; I do
    // NOT want it to move UP/DOWN when I rotate or move the control board — but that is exactly what
    // happens") was answered for a while by a horizontal-only follow plus a world-Y LOCK at spawn.
    // That lock is GONE and must not come back: the bobbing it was aimed at was never the follow at
    // all. An attribution log proved the cards were provably under our Y-locked host yet their world
    // Y still swung ±20 — `enemyCardsHolder` is the CONTENT of a ScrollRect on the tray-docked
    // InitiativeTrack, whose LateUpdate slid the content vertically INSIDE our fixed host. Once that
    // ScrollRect was disabled while floated (c8f8da6, the real root cause) the Y-lock became dead
    // weight and was removed in e5e7027, restoring the full-axis follow.
    //   * There is NO `_worldYLocked` field, and Place() does not override worldPos.y.
    //   * Board-independence comes from RIG-LOCALITY (a world-grab moves the rig root and the
    //     head-child together, so the stored rig-local pose does not change), not from a Y lock and
    //     not from an absolute world pose — see the HeadFrameScale doc and INVARIANTS-WorldUI.md
    //     "The reveal's real height bug was a tray-coupled ScrollRect".
    // If the reveal ever appears to bob with the board again, look for a re-enabled ScrollRect (or a
    // new tray-coupled parent), NOT for a missing Y lock.
    private const float FollowDeadzoneDeg = 22f;
    private const float FollowDwellSeconds = 0.5f;
    private const float FollowSettledDeg = 5f;
    private const float FollowEaseRate = 3f;

    // Item 3 (user #4, RECURRING) — HEAD/VIEW-ANCHORED height. The reveal must land in the
    // player's comfortable forward VIEW so it reads WITHOUT looking up. Two earlier takes
    // anchored the HEIGHT to something other than the gaze and both floated too high: the
    // horizon-flattened PanelPlacement.Spawn planted it at eye level (above the ~30-40°
    // downward table gaze), and the later BOARD-anchored height (a fixed offset above the
    // high-mounted control board) sat higher still. The reliable fix is to stop letting the
    // horizon OR the board decide the height and place the panel straight along the ACTUAL
    // gaze — pitch included, exactly like ModalFallback.PlaceAtHmd floats a window "in front
    // of the HMD" — at a comfortable reading distance, then drop it a little below the gaze
    // line for a natural reading angle. Because it rides the real gaze it is centred in the
    // forward field of view at ANY head pitch, so a player looking down at the diorama reads
    // it in place and never looks up. Both are REAL meters (× the LIVE head-frame scale,
    // HeadFrameScale) so they are a fixed comfortable distance in the player's OWN view,
    // NOT tied to the board's diorama size — user #2: the reveal must be COMPLETELY
    // INDEPENDENT of the control board, always in the forward view regardless of the board.
    // Tune here.
    private const float RevealReadingDistance = 1.3f;
    private const float RevealViewDrop = 0.15f;

    // Item 12 (user, hardware: "Die Gegnerinfo spawnt meist genau hinter dem Controllboard, da der
    // Spieler in der Regel auf das Controllboard schaut, wenn er die gelegten Karten betätigt und
    // das erscheint. Es sollte höher spawnen, damit man es direkt lesen kann.")
    // ROOT CAUSE: item 3 above made the spawn purely GAZE-anchored (position = head + gaze ×
    // RevealReadingDistance, pitch INCLUDED). That is right for "always in the forward view" but it
    // is blind to what is ALREADY in that view: the reveal fires exactly when the player has just
    // operated the played cards, i.e. while looking DOWN at the control board — so the plant lands
    // 1.3 m along a 30-45° downward gaze, which is BEHIND/BELOW the board's top edge. The board is
    // opaque depth-writing geometry, so it swallows the lower half of the panel (screenshot
    // position_gegnerinfo.png: the "Elite-Banditenwache" card cut off by the board's top edge).
    // Neither a bigger view-drop nor a fixed height fixes this: the board's own height/tilt/scale
    // and the player's gaze pitch all vary, so ANY constant is wrong for some pose.
    // FIX: keep the gaze plant (it is what puts the reveal in the forward view at any head pitch),
    // then apply a ONE-WAY, BOARD-AWARE clearance FLOOR to the TARGET: the panel is raised — never
    // lowered, never moved sideways — just far enough that the player's LINE OF SIGHT to the
    // panel's BOTTOM edge passes over the control board's REAL rendered top edge
    // (PlayTray.MeasureBoardLocalExtents, so any board scale/tilt/position and both FOLLOW and
    // PINNED tray modes are covered automatically). Applied to the TARGET pose only (the snap and
    // the lazy-follow goal), never to the applied pose per frame — so moving/rotating/zooming the
    // board still NEVER bobs a standing reveal (user #4), it only decides where the NEXT plant
    // goes. The gap itself is [WorldUI] EnemyRevealBoardClearance (debug menu, Panels->Initiative).

    /// <summary>
    /// Assumed half-HEIGHT of the reveal panel (real metres) while the content fit has not
    /// measured yet. The clearance must hold for the panel's BOTTOM edge, but at spawn the host
    /// rect is still growing with the staggered card animation — under-estimating there would let
    /// the finished panel sink back into the board, so the larger of (measured, this) is used.
    /// </summary>
    private const float NominalHalfHeight = 0.20f;

    /// <summary>
    /// Comfort cap: the clearance lift may never push the panel's centre higher than this far
    /// above EYE level (real metres). A board mounted absurdly high would otherwise trade
    /// "swallowed by the board" for "you have to look up" — the cap keeps the reveal readable
    /// without moving the head, and the log names the case when it binds.
    /// </summary>
    private const float MaxLiftAboveEye = 0.10f;

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    // Stored pose is RIG-LOCAL (head pose relative to RigRoot), re-projected through the LIVE
    // rig each frame in Place(). Rig-local is grab-invariant relative to the physical head, so
    // the follow below reacts only to real head movement, never to board/tray/world-grab.
    // (An ABSOLUTE-WORLD stored pose was tried and is wrong: a world-grab rotates the rig about
    // the grab pivot, which swings a world-fixed reveal out of view.)
    private Vector3 _position;                        // rig-local host position (head-relative) — follow in ALL axes
    private Quaternion _rotation = Quaternion.identity; // rig-local host rotation (unused for facing now; kept for snap)
    private bool _placed;                            // false until the first in-view pose is snapped
    private int _facedPoseVersion = -1;              // RigPoseVersion the pose was last snapped at (re-snap on recenter)
    private float _offGazeSince = -1f;               // unscaled time the panel first drifted past the deadzone
    private bool _easing;                            // gliding back to the in-view target (all axes)
    private bool _lastVisible;
    private bool _dropLogged;                        // one-shot per reveal: log the applied plant pose
    private float _lastDiagTime = -99f;              // throttle for the movement diagnostic (~1/s)

    // Host-rect pin (test #23): largest fitted width seen this reveal + the time it
    // last grew; the fit freezes once it has held at that max for FitPinSettleSeconds.
    private float _fitMaxWidth = -1f;
    private float _fitAtMaxSince;
    private float _fitFirstMeasured = -1f; // unscaled time the fit first measured this reveal (hard-timeout base)
    private bool _fitPinned;

    // Placement density cached once the fit pins (item 8): after the host rect is frozen
    // this holds metersPerPx so a later stray re-measure can never rescale/move the panel.
    private float _placedMetersPerPx = -1f;

    // ---- board-coupled scrollbar cleanup (item 8) -------------------------------------
    // WHAT THE USER SEES: a tall vertical scrollbar (tan track + lighter handle) on the far
    // right of the control board, appearing only while the enemy info is shown and rotating
    // WITH the board. Root cause: the game's <c>enemyCardsHolder</c> is the CONTENT of a
    // ScrollRect that lives inside the InitiativeTrack root window — the SAME window that
    // InitiativeTrackSurface (TablePanelSurfaces.cs) separately converts and DOCKS to the tray
    // (so it is board-coupled). CanvasConversion.Convert reparents ONLY the target
    // (enemyCardsHolder) onto our floating host; it never moves siblings — so the wrapping
    // ScrollRect's viewport frame and its Scrollbar(s) are LEFT BEHIND on the docked window,
    // now content-less but still drawing an orphaned scrollbar that rides the board. The
    // floating reveal itself is stable (proven by the movement diagnostic); this orphaned
    // board-anchored scrollbar is the residual "the enemy info depends on the board" the user
    // still perceives. We hide those scrollbar GameObjects while the reveal is floated and
    // restore them verbatim on release. Captured BEFORE Convert reparents the holder (after
    // that, walking the holder's parent chain would climb our host, not the original window).
    private readonly List<GameObject> _hiddenScrollbars = new(2);
    private bool _scrollbarHideLogged;
    private bool _scrollDiagLogged; // one-shot per reveal: the full scrollbar attribution audit

    // THE actual "enemy info height moves with the board" cause (proven by the attribution log:
    // enemyCardsHolder is under our Y-locked host — underMyHost=True — yet the card widget world Y
    // swings ±20 while the host stays flat). enemyCardsHolder is the CONTENT of this ScrollRect
    // ("Main Area"), which lives on the tray-docked InitiativeTrack; the live ScrollRect keeps
    // driving its content's anchoredPosition every LateUpdate (tray-coupled viewport), sliding the
    // cards vertically INSIDE our fixed host. We disable the ScrollRect component while floated
    // (re-asserted each tick in case the game re-enables it) and pin the holder's local position,
    // then restore both on release. This freezes the cards inside the locked host.
    private ScrollRect? _disabledScroll;
    private RectTransform? _pinnedHolder;
    private Vector3 _pinnedHolderLocalPos;
    private bool _holderPinned;

    public string Name => "EnemyReveal";

    public void Tick()
    {
        // Scene unload killed the target — the framework pruned the host already.
        if (_panel != null && !_panel.IsAlive)
        {
            _panel = null;
            RestoreBoardCoupledScrollbars(); // Unity-null-safe: just clears the stale list
        }

        InitiativeTrack track = InitiativeTrack.Instance;
        // [WorldUI] EnemyReveal is GONE (user ruling 2026-08-13): the monsters' ability cards for
        // the round are the information the whole round is played against, and OFF hid them on
        // the control board where VR never shows them. The reveal is unconditional now.
        bool visible = WorldUIConfig.ConversionActive
                       && Choreographer.s_Choreographer != null && RevealVisible(track);

        // Once per flip, with what is shown (the card names carry the round info).
        if (visible != _lastVisible)
        {
            _lastVisible = visible;
            VRLog.Info("WorldUI", visible
                ? $"ENEMY REVEAL shown: {DescribeCards(track)} — floating in the player's view focus."
                : "ENEMY REVEAL hidden — cards restored to their 2D home in the initiative track.");
        }

        if (visible && _panel == null)
        {
            var holder = track.enemyCardsHolder as RectTransform;
            if (holder != null)
            {
                // Kill the orphaned board-coupled scrollbar (see the _hiddenScrollbars docs):
                // capture the wrapping ScrollRect and hide its scrollbar(s) BEFORE Convert
                // reparents the content out from under it. Reversible on release.
                HideBoardCoupledScrollbars(holder, track);

                // Flatten2D (test #23): the monster-card subtree carries the same baked
                // 3D tilt as the combat log (recessed z / rotated RectTransforms shown
                // through the perspective UI camera) — neutralize it so the cards lie
                // flat on the world panel instead of sticking out.
                _panel = CanvasConversion.Convert(holder, Name, pokeable: false,
                    fitContent: true, flatten2D: true);
                if (_panel == null)
                    RestoreBoardCoupledScrollbars(); // convert failed — undo the pre-hide
                if (_panel != null)
                {
                    _fitMaxWidth = -1f;
                    _fitAtMaxSince = 0f;
                    _fitFirstMeasured = -1f;
                    _fitPinned = false;
                    _placedMetersPerPx = -1f;
                    _placed = false; // PlantPose() snaps the first in-view pose on the next tick
                    _easing = false;
                    _offGazeSince = -1f;
                    _dropLogged = false; // re-log the applied plant pose for this reveal (item 3)
                    _nextMipRescan = 0f; // bake this reveal's cards from the first tick
                }
            }
        }
        else if (!visible && _panel != null)
        {
            // Mutate-and-restore: the monster cards go HOME to the 2D initiative track, so hand
            // every graphic its original mipless sprite back BEFORE the reparent (house style —
            // the baked copies are a VR presentation detail and must never leak into the game UI).
            RestoreMips();
            CanvasConversion.Release(_panel);
            _panel = null;
            RestoreBoardCoupledScrollbars(); // hand the board-docked scrollbar back
        }

        if (_panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (_panel.HostRaycaster != null && _panel.HostRaycaster.enabled)
                _panel.HostRaycaster.enabled = false;
            ReassertScrollFreeze();
            PinWhenSettled();
            Place();
            RescanMips(); // cadence-gated inside; catches the staggered/async monster card art
        }
    }

    /// <summary>
    /// One cadence-gated mip-bake pass over the revealed monster cards. Config-gated and fully
    /// guarded inside <see cref="PanelMipBake.Rescan"/> (a bake surprise can never break the
    /// reveal), and idempotent-cheap once warm — a graphic already sampling a baked copy is a
    /// dictionary hit and is not rewritten. Scans the game's <c>enemyCardsHolder</c> subtree,
    /// which is what Convert reparented and what Release hands back.
    /// </summary>
    private void RescanMips()
    {
        if (Time.unscaledTime < _nextMipRescan)
            return;
        _nextMipRescan = Time.unscaledTime + MipRescanInterval;
        PanelMipBake.Rescan(EnemyCardsHolder(), Name);
    }

    /// <summary>Originals back on the monster cards (see <see cref="RescanMips"/>).</summary>
    private void RestoreMips()
    {
        _nextMipRescan = 0f;
        PanelMipBake.Restore(EnemyCardsHolder());
    }

    /// <summary>The game's enemy-card holder, or null when the track is gone.</summary>
    private static RectTransform? EnemyCardsHolder()
    {
        InitiativeTrack? track = InitiativeTrack.Instance;
        return track != null ? track.enemyCardsHolder as RectTransform : null;
    }

    /// <summary>
    /// Pin the host rect once the staggered reveal has settled at its FULL layout.
    /// The cards animate in one by one (MonsterBaseUI.AnimateAppearance, staggered by
    /// delayAnimationDraw), so the content fit keeps re-measuring and the host thrashes
    /// (627↔649 px as cards pulse/highlight — test #23). Track the largest fitted width
    /// and, once it has held there for <see cref="FitPinSettleSeconds"/> with no further
    /// growth, freeze the fit — like <see cref="CombatLogSurface"/>.OnConverted clears
    /// Panel.FitEnabled, except the log can disable immediately (it converts at its full
    /// window rect) whereas here the full layout only exists after the cards are in, so
    /// we settle first. Pinned strictly AT the max extent, never a mid-shrink measure.
    /// </summary>
    private void PinWhenSettled()
    {
        if (_panel == null || _fitPinned || !_panel.FitEnabled || _panel.HostRect == null)
            return;
        if (_fitFirstMeasured < 0f && _panel.FitMeasuredOnce)
            _fitFirstMeasured = Time.unscaledTime; // start the hard-timeout clock at first real measure

        float width = _panel.HostRect.rect.width;
        float growth = width - _fitMaxWidth;
        if (growth > 0f)
        {
            // Hysteresis (item 8): a highlight PULSE growing the width a pixel or two must
            // NOT restart the settle dwell (that flat 0.5 px reset is why the reveal never
            // pinned). Only a growth beyond FitPinGrowthFraction of the current max — a
            // real new card widening the layout — restarts the clock. The max is still
            // tracked either way so the pin lands strictly at the full extent.
            bool significant = _fitMaxWidth <= 1f || growth > _fitMaxWidth * FitPinGrowthFraction;
            _fitMaxWidth = width;
            if (significant)
                _fitAtMaxSince = Time.unscaledTime;
        }

        bool settled = _panel.FitMeasuredOnce && _fitMaxWidth > 1f && width >= _fitMaxWidth - 0.5f
                       && Time.unscaledTime - _fitAtMaxSince >= FitPinSettleSeconds;
        // Hard fallback (item 8): pin regardless of continued pulsing once the reveal has
        // been up long enough — the full layout is in by then, so a never-settling pulse
        // must not leave TickFit re-centring/rescaling forever (the ~2 s twitch).
        bool timedOut = _fitFirstMeasured >= 0f && _fitMaxWidth > 1f
                        && Time.unscaledTime - _fitFirstMeasured >= FitPinHardTimeoutSeconds;
        if (settled || timedOut)
        {
            _panel.FitEnabled = false;
            _fitPinned = true;
            VRLog.Info("WorldUI", $"ENEMY REVEAL host rect pinned at {width:F0} px " +
                                  (settled ? "(settled full layout)" : "(hard timeout — pulse never settled)") +
                                  " — content re-fit disabled to stop the ~2 s twitch.");
        }
    }

    /// <summary>
    /// The game's own reveal state (see class doc for the decompiled evidence):
    /// blocker raised AND at least one card widget actually active under the holder
    /// (also keeps the panel down for the phase-gated avatar HOVER preview,
    /// InitiativeTrackEnemyBehaviour.OnAvatarHighlight — the blocker stays false there).
    /// </summary>
    private static bool RevealVisible(InitiativeTrack track)
    {
        if (track == null || track.enemyCardsHolder == null || track.enemyCardsBlocker == null)
            return false;
        if (!track.enemyCardsBlocker.raycastTarget)
            return false;
        Transform holder = track.enemyCardsHolder;
        for (int i = 0; i < holder.childCount; i++)
        {
            if (holder.GetChild(i).gameObject.activeSelf)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Card names for the flip log. The reveal animation is staggered
    /// (delayAnimationDraw per card), so the show-flip may list only the first
    /// card(s) already active — the count still attributes the round.
    /// </summary>
    private static string DescribeCards(InitiativeTrack track)
    {
        if (track == null)
            return "<no track>";
        NameScratch.Clear();
        int count = 0;
        for (int i = 0; i < track.enemiesUI.Count; i++)
        {
            InitiativeTrackEnemyBehaviour enemy = track.enemiesUI[i];
            if (enemy == null || enemy.monsterBaseUI == null
                || !enemy.monsterBaseUI.gameObject.activeSelf)
                continue;
            count++;
            if (NameScratch.Length > 0)
                NameScratch.Append(", ");
            NameScratch.Append(enemy.monsterAbilityCard != null
                ? enemy.monsterAbilityCard.Name
                : enemy.name);
        }
        return $"{count} monster card(s) [{NameScratch}]";
    }

    /// <summary>
    /// Spawn the reveal in the player's forward view and LAZILY follow the PHYSICAL head
    /// (user #5 final: "spawn in front with the lazy movement, completely independent of how I
    /// move other elements"). ALL of this is computed in the rig's TRACKING SPACE (head pose
    /// relative to RigRoot), so it responds ONLY to real physical head movement:
    /// - moving/rotating the control board via WORLD-GRAB rotates the rig ROOT, which carries
    ///   the head AND this rig-local pose together → no change in rig-local → no follow;
    /// - a TRAY-grab moves the tray, not the rig → no change in rig-local → no follow
    ///   (proven by the diagnostic: host world pos held fixed while the tray yaw hit 141°).
    /// Only a genuine physical head turn/walk past the deadzone glides the panel back into
    /// the forward view. SNAP (no ease) at spawn and on rig rebuild/recenter (RigPoseVersion).
    ///
    /// Item 12: the gaze target then passes through the CONTROL-BOARD CLEARANCE FLOOR
    /// (<see cref="ApplyBoardClearance"/>) so a plant can never land behind the board the player is
    /// looking at. The floor is applied to the TARGET only, so a standing reveal is still never
    /// moved by the board itself.
    /// </summary>
    /// <param name="panelHalfHeight">Half height of the reveal panel in WORLD metres (its BOTTOM
    /// edge, not its pivot, is what has to clear the board).</param>
    private void PlantPose(Camera head, Transform? rig, float panelHalfHeight)
    {
        // Head pose in the rig's tracking space (grab-invariant relative to the physical head).
        Vector3 headPosL;
        Quaternion headRotL;
        if (rig != null)
        {
            headPosL = rig.InverseTransformPoint(head.transform.position);
            headRotL = Quaternion.Inverse(rig.rotation) * head.transform.rotation;
        }
        else
        {
            // No rig (dev harness) — world frame IS the head frame; Place() applies verbatim.
            headPosL = head.transform.position;
            headRotL = head.transform.rotation;
        }
        Vector3 gazeL = headRotL * Vector3.forward;

        // Upright billboard facing the head (yaw only), +Z toward the viewer.
        Vector3 awayL = gazeL;
        awayL.y = 0f;
        awayL = awayL.sqrMagnitude > 1e-4f ? awayL.normalized : Vector3.forward;
        Quaternion desiredRot = Quaternion.LookRotation(awayL, Vector3.up);

        // TARGET along the FULL gaze (pitch INCLUDED) so the panel glides into the player's field
        // of view vertically as well (user #5, latest: "let it move on the Y axis too, lazily, to
        // come into view"). The height coupling that used to bob it was the tray ScrollRect (now
        // frozen while floated, see ReassertScrollFreeze) — NOT this pose — so following the gaze is
        // safe again. A small drop keeps it just below the gaze line for a natural reading angle.
        // All rig-local, so world-grab/tray-grab never trip the follow; only a real head move does.
        Vector3 desiredPos = headPosL + gazeL * RevealReadingDistance;
        desiredPos.y -= RevealViewDrop;

        // CONTROL-BOARD CLEARANCE FLOOR (item 12 — the "spawnt hinter dem Controllboard" fix).
        // Evaluated in WORLD space (that is where the board lives, and world-up is the axis the
        // clamp works along — a world-grab pitch/roll of the rig must not tilt the clearance), then
        // folded back into the rig-local target so everything downstream is unchanged.
        Vector3 desiredWorld = rig != null ? rig.TransformPoint(desiredPos) : desiredPos;
        _clearance = ApplyBoardClearance(head.transform.position, ref desiredWorld, panelHalfHeight);
        if (_clearance.Lifted)
            desiredPos = rig != null ? rig.InverseTransformPoint(desiredWorld) : desiredWorld;

        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (!_placed || poseVersion != _facedPoseVersion)
        {
            // SNAP: first spawn, or a deliberate recentre/rebuild teleported the whole rig.
            _position = desiredPos;
            _rotation = desiredRot;
            _placed = true;
            _facedPoseVersion = poseVersion;
            _offGazeSince = -1f;
            _easing = false;
            if (!_dropLogged)
            {
                _dropLogged = true;
                // VERIFICATION LINE (grep "ENEMY REVEAL spawned"): the chosen world spawn pose and
                // its measured clearance from the control board — the next hardware log must show a
                // POSITIVE gap over the board's top edge, i.e. the panel is no longer swallowed.
                Vector3 worldPos = rig != null ? rig.TransformPoint(_position) : _position;
                VRLog.Info("WorldUI", $"ENEMY REVEAL spawned at world {worldPos:F2} " +
                                      $"({(worldPos - head.transform.position).magnitude:F2} m from the head, " +
                                      $"panel half-height {panelHalfHeight:F2} m) — {_clearance.Describe()} " +
                                      "Lazy follow ON in ALL axes (X/Z + Y), board-decoupled " +
                                      "(tray ScrollRect frozen while floated; the board clearance only " +
                                      "moves the TARGET, never a standing panel).");
            }
            return;
        }

        // LAZY FOLLOW (all axes): glide back into the forward view when the player has physically
        // turned OR looked up/down past the deadzone — measured in rig-local as the FULL angle
        // between the gaze and the head→panel direction, so world-grab / tray-grab never trip it.
        Vector3 toPanel = _position - headPosL;
        float off = toPanel.sqrMagnitude > 1e-6f ? Vector3.Angle(gazeL, toPanel) : 0f;

        // Item 12 guard: the target is no longer guaranteed to sit ON the gaze axis — the board
        // clearance floor deliberately holds it ABOVE the gaze while the player looks down at the
        // control board. Without this check the panel would sit permanently "off gaze", re-arm the
        // follow every dwell, ease nowhere (it is already AT the target) and settle again — a
        // pointless 2 Hz log/ease cycle. Only a target the panel is genuinely NOT at may arm it.
        Vector3 toTarget = desiredPos - headPosL;
        float misfit = (toPanel.sqrMagnitude > 1e-6f && toTarget.sqrMagnitude > 1e-6f)
            ? Vector3.Angle(toPanel, toTarget)
            : 0f;
        if (off > FollowDeadzoneDeg && misfit > FollowSettledDeg)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_easing && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _easing = true;
                VRLog.Info("WorldUI", $"ENEMY REVEAL lazy follow: {off:F0}° off the gaze for " +
                                      $">{FollowDwellSeconds:F1}s (physical head moved) — gliding into view (all axes).");
            }
        }
        else
        {
            _offGazeSince = -1f;
        }

        if (_easing)
        {
            float t = Time.deltaTime * FollowEaseRate;
            _position = Vector3.Lerp(_position, desiredPos, t);
            _rotation = Quaternion.Slerp(_rotation, desiredRot, t);
            Vector3 toDesired = desiredPos - headPosL;
            Vector3 cur = _position - headPosL;
            if (toDesired.sqrMagnitude < 1e-6f || Vector3.Angle(cur, toDesired) < FollowSettledDeg)
            {
                _easing = false;
                _offGazeSince = -1f;
            }
        }
    }

    /// <summary>
    /// One clearance evaluation, kept so the spawn log and the per-second diagnostic can PROVE on
    /// the hardware log that the reveal cleared the control board (and by how much).
    /// </summary>
    private readonly struct BoardClearance
    {
        public BoardClearance(bool hasBoard, bool lifted, bool capped, float topEdgeY,
            float bottomY, float lift, float gapAtBoard, float gapBefore)
        {
            HasBoard = hasBoard;
            Lifted = lifted;
            Capped = capped;
            TopEdgeY = topEdgeY;
            BottomY = bottomY;
            Lift = lift;
            GapAtBoard = gapAtBoard;
            GapBefore = gapBefore;
        }

        /// <summary>A live, visible control board was found (else there is nothing to clear).</summary>
        public readonly bool HasBoard;

        /// <summary>The target was actually raised out of the board.</summary>
        public readonly bool Lifted;

        /// <summary>The comfort cap (<see cref="MaxLiftAboveEye"/>) limited the lift.</summary>
        public readonly bool Capped;

        /// <summary>World Y of the board's REAL (rendered) top edge.</summary>
        public readonly float TopEdgeY;

        /// <summary>World Y of the reveal panel's BOTTOM edge after the clamp.</summary>
        public readonly float BottomY;

        /// <summary>Metres the target was raised (0 when the gaze plant was already clear).</summary>
        public readonly float Lift;

        /// <summary>Sight-line gap over the top edge, measured AT THE BOARD (world metres).</summary>
        public readonly float GapAtBoard;

        /// <summary>The same gap BEFORE the clamp — negative = the raw pose was inside the board.</summary>
        public readonly float GapBefore;

        /// <summary>Compact log phrase (both the vertical gap and the sight-line gap).</summary>
        public string Describe() => !HasBoard
            ? "no control board in the scene (menu / tray hidden) — pure gaze plant, nothing to clear."
            : $"board top edge world Y {TopEdgeY:F2} m, panel bottom Y {BottomY:F2} m → " +
              $"{(BottomY - TopEdgeY):F2} m above the edge in world Y, sight-line gap AT THE BOARD " +
              $"{GapAtBoard:F2} m" +
              (Lifted ? $" (raw gaze pose was {GapBefore:F2} m — INSIDE the board — raised {Lift:F2} m)"
                      : " (gaze plant was already clear)") +
              (Capped ? " [comfort cap bound — held at eye level]" : "") + ".";
    }

    /// <summary>Last clearance evaluation (spawn log + per-second diagnostic).</summary>
    private BoardClearance _clearance;

    /// <summary>
    /// CONTROL-BOARD CLEARANCE FLOOR (item 12 — see the constants block for the full root cause).
    /// Swing <paramref name="posWorld"/> UP around the head — never down, never sideways, and
    /// always at the SAME distance the plant chose — until the player's LINE OF SIGHT to the
    /// panel's BOTTOM edge passes <see cref="WorldUIConfig.EnemyRevealBoardClearance"/> real metres
    /// ABOVE the control board's REAL rendered top edge.
    ///
    /// Why a sight-line test and not "put it above the board": the reveal floats ~1.3 m out while
    /// the board sits ~0.6 m out, so the panel is already BEYOND the board in world space — it is
    /// occluded by PERSPECTIVE, not by containment. A footprint/containment test would never fire
    /// (the screenshot's panel is past the board's far edge yet still swallowed by it). Solving the
    /// occlusion in the vertical plane through head and panel also yields the SMALLEST lift that
    /// works, so the reveal stays as low — as comfortable — as it can while being fully visible.
    ///
    /// Board-aware by construction: the edge comes from <see cref="PlayTray.MeasureBoardLocalExtents"/>
    /// on the tray's LIVE root, so any board scale, tilt, position, board style, and both FOLLOW
    /// (rig-parented) and PINNED (world-parented) tray modes are handled with no special cases; if
    /// there is no board at all, nothing is clamped.
    /// </summary>
    private static BoardClearance ApplyBoardClearance(Vector3 headWorld, ref Vector3 posWorld,
        float panelHalfHeight)
    {
        PlayTray? tray = PlayTray.Current;
        Transform? root = tray != null && tray.IsVisible ? tray.Root : null;
        if (root == null)
            return default; // menu / tray hidden / Cards module off — nothing to clear

        PlayTray.MeasureBoardLocalExtents(root, out float topLocalY, out float halfLocalX);
        Vector3 topEdge = root.TransformPoint(new Vector3(0f, topLocalY, 0f));
        float halfWidthWorld = halfLocalX * Mathf.Max(Mathf.Abs(root.lossyScale.x), 1e-4f);

        // The clearance is a PLAYER-frame quantity (like the reading distance / view drop): real
        // metres × the head-frame scale, so it reads the same at any diorama zoom — NOT tied to the
        // board's own scale, which the player may have tuned to any size.
        float scale = HeadFrameScale();
        float margin = Mathf.Max(0f, WorldUIConfig.EnemyRevealBoardClearance != null
            ? WorldUIConfig.EnemyRevealBoardClearance.Value
            : 0.10f) * scale;

        Vector3 toPanel = posWorld - headWorld;
        float d = toPanel.magnitude; // the plant's reading DISTANCE — preserved by the raise below
        var toPanelH = new Vector3(toPanel.x, 0f, toPanel.z);
        float dP = toPanelH.magnitude;
        float bottomY = posWorld.y - panelHalfHeight;
        if (dP < 1e-3f || d < 1e-3f)
        {
            // Degenerate: the panel is straight above/below the head — no horizontal sight line to
            // solve, and the board cannot be "in front of" it in any meaningful sense.
            float plainGap = bottomY - topEdge.y;
            return new BoardClearance(true, false, false, topEdge.y, bottomY, 0f, plainGap, plainGap);
        }
        Vector3 bearing = toPanelH / dP;

        var toEdgeH = new Vector3(topEdge.x - headWorld.x, 0f, topEdge.z - headWorld.z);
        float dT = Vector3.Dot(toEdgeH, bearing);                 // board distance ALONG the sight bearing
        float lateral = (toEdgeH - bearing * dT).magnitude;       // how far the board sits off that bearing
        float gap = SightGapAtBoard(headWorld.y, bottomY, dP, dT, topEdge.y);

        // The board only matters when it is genuinely BETWEEN the head and the panel and the sight
        // line actually crosses it: otherwise the player is looking somewhere else entirely and any
        // lift would be an unexplained jump. (The lateral test compares the board's centre offset
        // from the sight bearing against its half WIDTH — an approximation that ignores the board's
        // yaw relative to that bearing, which is deliberately generous: it can only fire the clamp
        // slightly early, never late, and the player is square to the board whenever this matters.)
        bool inLine = dT > 1e-3f && dT < dP && lateral <= halfWidthWorld + margin;
        if (!inLine || gap >= margin)
            return new BoardClearance(true, false, false, topEdge.y, bottomY, 0f, gap, gap);

        // RAISE BY ELEVATION, NOT BY Y (this is what keeps the fix comfortable): the panel is
        // swung UP around the head along its own bearing, so its DISTANCE — hence its apparent
        // size and its distance from the diorama — is exactly the one the plant chose. Simply
        // adding height would drag the panel toward the player (1.3 m along a 60° downward gaze is
        // only 0.65 m of ground reach, so a vertical lift shortens the ray a lot) and the fixed-size
        // reveal would loom at arm's length.
        //
        // With φ = the panel's elevation angle from the head, the visibility condition
        //     headY + (d·sinφ − halfHeight − headY)·(dT / (d·cosφ)) ≥ topEdgeY + margin
        // reduces to  A·sinφ + B·cosφ ≥ halfHeight  with  A = d,  B = (headY − topEdgeY − margin)·d/dT,
        // i.e. R·sin(φ + θ) ≥ halfHeight for R = √(A²+B²), θ = atan2(B, A). The SMALLEST φ that
        // satisfies it — the minimal, least intrusive raise — is therefore asin(halfHeight/R) − θ.
        float A = d;
        float B = (headWorld.y - topEdge.y - margin) * d / dT;
        float R = Mathf.Sqrt(A * A + B * B);
        float ratio = R > 1e-4f ? panelHalfHeight / R : 2f;
        float currentPhi = Mathf.Asin(Mathf.Clamp(toPanel.y / d, -1f, 1f));
        // ratio > 1: no elevation clears this board at this distance (absurdly tall/close board) —
        // go as high as the comfort cap allows and let the log name it.
        float neededPhi = ratio <= 1f
            ? Mathf.Asin(ratio) - Mathf.Atan2(B, A)
            : Mathf.PI * 0.5f;

        // Comfort cap: never trade "swallowed by the board" for "you have to look up".
        float capPhi = Mathf.Asin(Mathf.Clamp(MaxLiftAboveEye * scale / d, -1f, 1f));
        bool capped = neededPhi > capPhi;
        float phi = Mathf.Clamp(neededPhi, currentPhi, Mathf.Max(currentPhi, capPhi));
        if (phi <= currentPhi + 1e-5f)
            return new BoardClearance(true, false, capped, topEdge.y, bottomY, 0f, gap, gap); // cap left nothing to do

        float rawY = posWorld.y;
        posWorld = headWorld + bearing * (d * Mathf.Cos(phi)) + Vector3.up * (d * Mathf.Sin(phi));
        bottomY = posWorld.y - panelHalfHeight;
        float newDp = Mathf.Max(d * Mathf.Cos(phi), 1e-3f);
        return new BoardClearance(true, true, capped, topEdge.y, bottomY, posWorld.y - rawY,
            SightGapAtBoard(headWorld.y, bottomY, newDp, dT, topEdge.y), gap);
    }

    /// <summary>
    /// Height of the sight line head→(panel bottom edge) at the board's distance, minus the board's
    /// top-edge height: positive = the player sees the whole panel above the board, negative = the
    /// board eats that much of it.
    /// </summary>
    private static float SightGapAtBoard(float headY, float bottomY, float panelDist,
        float boardDist, float topEdgeY)
        => headY + (bottomY - headY) * (boardDist / panelDist) - topEdgeY;

    /// <summary>
    /// Anchor the reveal in the player's forward view focus, at the SHARED tray density
    /// (test #16) with the objectives' readability multiplier, width-capped. The pose is stored
    /// RIG-LOCAL by <see cref="PlantPose"/> and RE-PROJECTED through the live rig every frame
    /// here (all axes, including Y), easing to a new target only after the panel has sat past
    /// the follow deadzone for the dwell. The host SIZE tracks the LIVE head-frame scale
    /// (<see cref="HeadFrameScale"/>) so the content reads at a fixed apparent size.
    /// COMPLETELY INDEPENDENT of the control board (user #5) — but that independence comes from
    /// RIG-LOCALITY, not from an absolute world pose: the board / tray / world-grab move the rig
    /// ROOT and the head-child together, so the stored rig-local pose does not change. The facing
    /// is rebuilt in WORLD space, upright and yaw-only, so rig pitch/roll never tilts the panel.
    /// </summary>
    private void Place()
    {
        if (_panel == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return; // no head yet — leave the panel where it last sat (world-stable)

        float scale = HeadFrameScale();
        // Cache metersPerPx once the fit has PINNED (item 8): the host rect is frozen at
        // its full-layout max then, so capturing it here guarantees a later stray
        // re-measure can never rescale/move the panel — the twitch is gone even if some
        // late fit slips through. Before the pin, track the live rect so the panel scales
        // naturally with the content as the cards animate in (the appear animation).
        float metersPerPx;
        if (_placedMetersPerPx >= 0f)
        {
            metersPerPx = _placedMetersPerPx;
        }
        else
        {
            metersPerPx = 1f / (PlayTray.TrayPixelsPerMeter * DensityScale);
            Rect rect = _panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
            if (rect.width > 1f)
                metersPerPx = Mathf.Min(metersPerPx, MaxWidthMeters / rect.width);
            if (_fitPinned)
                _placedMetersPerPx = metersPerPx; // freeze at the pinned (max) width
        }

        // The panel's own half HEIGHT in world metres — what has to clear the control board's top
        // edge (item 12). The host pivot is centred (CanvasConversion), so half the fitted rect is
        // the distance from the pivot to the bottom edge. While the staggered card animation is
        // still growing the rect (pre-pin) the measurement UNDER-states the finished panel, and a
        // plant that used it would sink back into the board as the panel finishes growing — so the
        // nominal height is used as a floor.
        float halfHeightWorld = Mathf.Max(
            _panel.HostRect != null ? _panel.HostRect.rect.height * 0.5f * metersPerPx * scale : 0f,
            NominalHalfHeight * scale);

        // Plant (rig-local) in the forward view, then re-project through the LIVE rig each frame
        // (user #5). World-grab moves the rig ROOT and the head-child together, so the reveal
        // rides the physical head and never swings with the board. PlantPose also runs the lazy
        // follow: a head turn/pitch past the deadzone eases the stored pose to a new target.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        PlantPose(head, rig, halfHeightWorld);

        // Full rig-local follow pose re-projected through the live rig (all axes, incl. Y).
        Vector3 worldPos = rig != null ? rig.TransformPoint(_position) : _position;

        // Upright, yaw-only billboard built in WORLD space (facing the head horizontally). Because
        // it uses WORLD up and only the horizontal head→panel direction, rig PITCH/ROLL from a
        // world-grab never tilts the panel and never introduces a vertical component.
        Vector3 awayWorld = worldPos - head.transform.position;
        awayWorld.y = 0f;
        awayWorld = awayWorld.sqrMagnitude > 1e-6f ? awayWorld.normalized : Vector3.forward;
        Quaternion worldRot = Quaternion.LookRotation(awayWorld, Vector3.up);

        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(worldPos, worldRot);
        host.localScale = Vector3.one * (metersPerPx * scale);

        // DECISIVE HEIGHT ATTRIBUTION DIAGNOSTIC (throttled ~1/s). Columns actually printed below:
        // myHostY (the pose we applied), the CARD widget's world Y + whether it is really under our
        // host, enemyCardsHolder's live parent, the TRAY Y/yaw, trackRootY, headWorldY, rigScale
        // (zoom), easing, and the re-measured board CLEARANCE. The question it answers is WHOSE
        // transform moves the pixels the player sees: if the card's world Y tracks TRAY Y rather
        // than myHostY, the visible reveal is the tray-docked track, not our float — which is
        // exactly how the tray-coupled ScrollRect was caught (c8f8da6). This is not a lock check;
        // there is no Y lock (see the LAZY FOLLOW block at the top of the file).
        float now = Time.unscaledTime;
        if (now - _lastDiagTime >= 1f)
        {
            _lastDiagTime = now;
            float rigScale = rig != null ? rig.lossyScale.x : 1f;

            // DECISIVE ATTRIBUTION (user: "seriously trace what happens, it is NOT my head").
            // My host is provably Y-locked, yet the user still sees the info bob with the board.
            // So the VISIBLE content must be positioned by another path. Log the ACTUAL monster
            // card widget's WORLD Y and whether it is really under MY host, the live parent of
            // enemyCardsHolder (did our reparent stick?), and the TRAY pose — because
            // InitiativeTrackSurface converts the whole InitiativeTrack ROOT and docks it to the
            // tray, and enemyCardsHolder is a child of that root. If cardY tracks trayY (not
            // myHostY), the reveal the user sees is the tray-docked track, not this float.
            // Re-measure the clearance of the pose ACTUALLY APPLIED this frame (the clamp mutates
            // only this local probe copy, never the host).
            Vector3 probe = worldPos;
            BoardClearance standing = ApplyBoardClearance(head.transform.position, ref probe, halfHeightWorld);

            InitiativeTrack tr = InitiativeTrack.Instance;
            Transform? holderNow = tr != null ? tr.enemyCardsHolder : null;
            string holderParent = holderNow != null && holderNow.parent != null ? holderNow.parent.name : "<null>";
            bool holderOnMyHost = holderNow != null && holderNow.parent == host;
            Transform? card = FirstActiveMonsterCard(tr);
            string cardY = card != null ? card.position.y.ToString("F2") : "n/a";
            bool cardUnderMyHost = card != null && IsDescendantOf(card, host);
            Transform? tray = PlayTray.Current != null ? PlayTray.Current.Root : null;
            float trayY = tray != null ? tray.position.y : 0f;
            float trayYaw = tray != null ? tray.eulerAngles.y : 0f;
            float trackRootY = tr != null ? tr.transform.position.y : 0f;
            VRLog.Info("WorldUI",
                $"ENEMY REVEAL attribution: myHostY={worldPos.y:F2} | " +
                $"CARD widget worldY={cardY} underMyHost={cardUnderMyHost} | " +
                $"enemyCardsHolder.parent='{holderParent}' onMyHost={holderOnMyHost} | " +
                $"TRAY Y={trayY:F2} yaw={trayYaw:F0}° | trackRootY={trackRootY:F2} | " +
                $"headWorldY={head.transform.position.y:F2} rigScale={rigScale:F1} easing={_easing} | " +
                // Item 12: the live board clearance of the STANDING panel (probe = the APPLIED pose
                // re-measured; the clamp writes only the local copy), so a reveal that ends up
                // swallowed again is attributable from the log alone — 'raw gaze pose was NEGATIVE'
                // there means the panel as drawn is inside the board.
                $"CLEARANCE {standing.Describe()} " +
                "=> if CARD worldY tracks TRAY Y (not myHostY), the visible reveal is TRAY-DOCKED, not on my float.");
        }
    }

    /// <summary>World transform of the first ACTIVE monster reveal card widget (what the player
    /// actually sees), for the attribution diagnostic. Null when none is active.</summary>
    private static Transform? FirstActiveMonsterCard(InitiativeTrack? track)
    {
        if (track == null || track.enemiesUI == null)
            return null;
        for (int i = 0; i < track.enemiesUI.Count; i++)
        {
            InitiativeTrackEnemyBehaviour enemy = track.enemiesUI[i];
            if (enemy != null && enemy.monsterBaseUI != null && enemy.monsterBaseUI.gameObject.activeSelf)
                return enemy.monsterBaseUI.transform;
        }
        return null;
    }

    /// <summary>
    /// LIVE head-frame scale (game units per real meter, <see cref="PanelLayout.WorldScale"/>
    /// = the rig's current lossy scale). This is the PLAYER's own real->world conversion, NOT
    /// a board property — so multiplying the reading distance / view-drop / host size by it
    /// keeps the reveal at a FIXED comfortable REAL distance and apparent size in the player's
    /// forward view at ANY zoom or board position.
    ///
    /// This deliberately replaces the earlier <c>BaseWorldScale</c> anchor (the diorama base
    /// scale, itself derived from the board's hex tile size). That base scale was the RESIDUAL
    /// BOARD COUPLING behind user #2: with it, the reveal's distance/size were pinned to the
    /// board's diorama frame, so world-grabbing the board (which rescales/moves the rig while
    /// BaseWorldScale stayed constant) dragged and rescaled the reveal WITH the board. It also
    /// reverses test #23 (b)'s "zoom with the board" intent per the newer user #2 requirement:
    /// the reveal must be COMPLETELY INDEPENDENT of the control board. No PlayTray / tray /
    /// board / InitiativeTrack transform feeds the reveal's world position — only the head
    /// pose and this player-frame scalar do. Falls back to 1 outside a scenario (no rig).
    ///
    /// Item 12 nuance: the board IS consulted once per PLANT, but only as a one-way clearance floor
    /// on the target (<see cref="ApplyBoardClearance"/>) — the standing panel's pose is still driven
    /// exclusively by the stored rig-local plant and this scalar, so the board can never move it.
    /// </summary>
    private static float HeadFrameScale()
    {
        return PanelLayout.WorldScale;
    }

    /// <summary>
    /// Hide the board-coupled scrollbar (see <see cref="_hiddenScrollbars"/>). The reveal
    /// content (<paramref name="holder"/>) is the CONTENT of a ScrollRect inside the
    /// InitiativeTrack window; that window is docked to the tray by
    /// <see cref="InitiativeTrackSurface"/>, so the ScrollRect — and its Scrollbar(s) —
    /// are board-anchored. Once we float the content out, the scrollbar is an orphan on the
    /// board. Find the owning ScrollRect (the holder's ancestor, or a ScrollRect in the
    /// track window whose <c>content</c> is/holds the holder) and disable its scrollbar
    /// GameObjects, recording each so <see cref="RestoreBoardCoupledScrollbars"/> can undo
    /// it exactly. MUST run before Convert reparents the holder onto our host.
    /// </summary>
    private void HideBoardCoupledScrollbars(RectTransform holder, InitiativeTrack track)
    {
        // Owning ScrollRect: the enemy-cards area is a ScrollRect whose CONTENT is (or holds)
        // enemyCardsHolder. In the game prefab the ScrollRect sits ABOVE the holder
        // (ScrollRect root → Viewport(mask) → Content=enemyCardsHolder), so it is an ANCESTOR
        // and GetComponentInParent finds it; the fallback also matches a ScrollRect that
        // merely references the holder as its content.
        ScrollRect scroll = holder.GetComponentInParent<ScrollRect>(true);
        if (scroll == null && track != null)
        {
            ScrollRect[] scrolls = track.GetComponentsInChildren<ScrollRect>(true);
            for (int i = 0; i < scrolls.Length; i++)
            {
                RectTransform content = scrolls[i].content;
                if (content == holder || (content != null && holder.IsChildOf(content)))
                {
                    scroll = scrolls[i];
                    break;
                }
            }
        }

        // ROOT FIX (issue #3): disable the owning ScrollRect while the content is floated so it
        // stops driving enemyCardsHolder's anchoredPosition each LateUpdate (the tray-coupled slide
        // that moved the cards vertically inside our locked host). Restored on release.
        if (scroll != null && scroll.enabled)
        {
            scroll.enabled = false;
            _disabledScroll = scroll;
            VRLog.Info("WorldUI", $"ENEMY REVEAL: disabled owning ScrollRect '{scroll.gameObject.name}' " +
                                  "while floated — it no longer slides the reveal cards vertically inside the fixed host.");
        }

        // DEFINITIVE ATTRIBUTION AUDIT (once per reveal): dump EVERY Scrollbar in the
        // InitiativeTrack subtree — full transform path, live active state, and which side it
        // lands on once Convert reparents enemyCardsHolder. A Scrollbar that is a DESCENDANT of
        // the holder rides the float (moves with the lazy reveal); one OUTSIDE the holder stays
        // on the tray-docked InitiativeTrackSurface host (rotates with the board). This proves
        // the scrollbar's owner on the next hardware log regardless of ScrollRect wiring — the
        // earlier 'hid N' log never fired because ScrollRect.verticalScrollbar was NULL
        // (the prefab's scrollbar GameObject is a manually-placed child, not wired to the
        // ScrollRect), so the wired-only path hid nothing while the bar stayed visible.
        if (!_scrollDiagLogged && track != null)
        {
            _scrollDiagLogged = true;
            Scrollbar[] bars = track.GetComponentsInChildren<Scrollbar>(true);
            var sb = new StringBuilder(256);
            sb.Append("ENEMY REVEAL scrollbar audit: ").Append(bars.Length)
              .Append(" Scrollbar(s) under InitiativeTrack '").Append(track.name).Append('\'');
            if (scroll != null)
                sb.Append(", owning ScrollRect='").Append(scroll.gameObject.name)
                  .Append("' vBar=").Append(scroll.verticalScrollbar != null ? "wired" : "NULL")
                  .Append(" hBar=").Append(scroll.horizontalScrollbar != null ? "wired" : "NULL");
            else
                sb.Append(", NO owning ScrollRect matched enemyCardsHolder");
            for (int i = 0; i < bars.Length; i++)
            {
                Transform bt = bars[i].transform;
                bool onFloat = IsDescendantOf(bt, holder);
                sb.Append("\n  [").Append(i).Append("] ").Append(TransformPath(bt, track.transform))
                  .Append(" active=").Append(bt.gameObject.activeInHierarchy)
                  .Append(onFloat ? " => RIDES FLOAT (under enemyCardsHolder)"
                                  : " => BOARD-COUPLED (outside enemyCardsHolder, stays on docked track)");
            }
            VRLog.Info("WorldUI", sb.ToString());
        }

        // HIDE the board-coupled scrollbar(s) ROBUSTLY — by enumerating Scrollbar GameObjects
        // directly, NOT via ScrollRect.verticalScrollbar/.horizontalScrollbar (unwired here,
        // which is exactly why the old path hid nothing). Scope to the owning ScrollRect's
        // subtree when found (tight and correct — the scrollbar chrome is a child of the
        // ScrollRect root); fall back to the whole InitiativeTrack otherwise. Skip any
        // Scrollbar that is a descendant of enemyCardsHolder: those ride the float with the
        // cards and are not board-coupled. Every hide is recorded for exact restore.
        Transform? scopeRoot = scroll != null ? scroll.transform
                            : (track != null ? track.transform : null);
        if (scopeRoot == null)
            return;
        int hidden = 0;
        Scrollbar[] scoped = scopeRoot.GetComponentsInChildren<Scrollbar>(true);
        for (int i = 0; i < scoped.Length; i++)
        {
            Transform bt = scoped[i].transform;
            if (IsDescendantOf(bt, holder))
                continue; // rides the float with the cards — not board-coupled
            hidden += HideScrollbarGo(bt.gameObject);
        }
        if (!_scrollbarHideLogged)
        {
            _scrollbarHideLogged = true;
            VRLog.Info("WorldUI", hidden > 0
                ? $"ENEMY REVEAL: hid {hidden} board-coupled scrollbar GameObject(s) " +
                  (scroll != null ? $"under ScrollRect '{scroll.gameObject.name}'" : "in the InitiativeTrack") +
                  " while the reveal floats — the board-anchored scrollbar no longer rides the control board."
                : "ENEMY REVEAL: no board-coupled scrollbar to hide (all Scrollbars ride the float, " +
                  "or none were active) — see the scrollbar audit above for attribution.");
        }
    }

    /// <summary>Disable a scrollbar GameObject (if live and active), recording it for restore. Returns 1 if hidden.</summary>
    private int HideScrollbarGo(GameObject go)
    {
        if (go == null || !go.activeSelf)
            return 0;
        go.SetActive(false);
        _hiddenScrollbars.Add(go);
        return 1;
    }

    /// <summary>True if <paramref name="t"/> is <paramref name="ancestor"/> or nested under it.</summary>
    private static bool IsDescendantOf(Transform t, Transform ancestor)
    {
        for (Transform c = t; c != null; c = c.parent)
        {
            if (c == ancestor)
                return true;
        }
        return false;
    }

    /// <summary>Slash-joined transform path from <paramref name="stop"/> down to <paramref name="t"/> (for the audit log).</summary>
    private static string TransformPath(Transform t, Transform stop)
    {
        var sb = new StringBuilder(96);
        for (Transform c = t; c != null && c != stop; c = c.parent)
        {
            if (sb.Length > 0)
                sb.Insert(0, '/');
            sb.Insert(0, c.name);
        }
        sb.Insert(0, (stop != null ? stop.name : "<root>") + "/");
        return sb.ToString();
    }

    /// <summary>Re-enable every scrollbar GameObject this surface hid (Unity-null safe), then clear.</summary>
    private void RestoreBoardCoupledScrollbars()
    {
        for (int i = 0; i < _hiddenScrollbars.Count; i++)
        {
            GameObject go = _hiddenScrollbars[i];
            if (go != null) // Unity-null: destroyed on scene unload — skip
                go.SetActive(true);
        }
        _hiddenScrollbars.Clear();
        // Re-enable the owning ScrollRect we disabled, and drop the holder pin.
        if (_disabledScroll != null)
        {
            _disabledScroll.enabled = true;
            _disabledScroll = null;
        }
        _pinnedHolder = null;
        _holderPinned = false;
        // Re-arm the one-shot logs so the NEXT reveal re-audits + re-reports its hide.
        _scrollbarHideLogged = false;
        _scrollDiagLogged = false;
    }

    /// <summary>Per-frame while floated: keep the tray-coupled ScrollRect disabled (the game may
    /// re-enable it) and pin enemyCardsHolder's local position so the cards can't slide vertically
    /// inside our Y-locked host. This is the concrete fix for "the enemy info height moves with the
    /// board" — proven by the attribution log (cards under our host but their world Y swinging).</summary>
    private void ReassertScrollFreeze()
    {
        if (_disabledScroll != null && _disabledScroll.enabled)
            _disabledScroll.enabled = false;

        InitiativeTrack? tr = InitiativeTrack.Instance;
        RectTransform? holder = tr != null ? tr.enemyCardsHolder as RectTransform : null;
        if (holder == null)
            return;
        if (!_holderPinned)
        {
            _pinnedHolder = holder;
            _pinnedHolderLocalPos = holder.localPosition;
            _holderPinned = true;
        }
        else if (_pinnedHolder == holder && holder.localPosition != _pinnedHolderLocalPos)
        {
            holder.localPosition = _pinnedHolderLocalPos;
        }
    }

    public void Shutdown()
    {
        if (_panel != null)
        {
            RestoreMips(); // originals back before the cards return to the 2D track
            CanvasConversion.Release(_panel);
            _panel = null;
        }
        RestoreBoardCoupledScrollbars();
        _lastVisible = false;
    }
}
