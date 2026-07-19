using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using UnityEngine;

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
/// or moved, scaling with the diorama, at the shared tray density; on hide it is released back to its exact 2D home
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

    // Lazy follow (test #27) — FlatScreen.FollowHead's feel, extended to POSITION as
    // well as yaw: re-centre in front of the head only once the panel has drifted more
    // than FollowDeadzoneDeg off the gaze (head turned / player walked), held for the
    // dwell so quick glances and tremor are ignored, then ease in until it has re-
    // converged on the fresh in-view target within FollowSettledDeg. Between triggers the
    // panel is world-stable (its stored pose is applied verbatim, never recomputed), so
    // it sits perfectly still and only glides on a deliberate move. FollowEaseRate is the
    // Lerp/Slerp rate (matches FlatScreen's 3/s glide). The deadzone easily clears the
    // ~7° that the comfortable reading drop already puts the panel below the gaze axis
    // (see <see cref="RevealViewDrop"/>), so it never self-triggers at rest.
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
    // it in place and never looks up. Both are reference meters (× diorama scale) so they
    // zoom with the board. Tune here.
    private const float RevealReadingDistance = 1.3f;
    private const float RevealViewDrop = 0.15f;

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    private Vector3 _position;                        // current applied host position (world-stable between glides)
    private Quaternion _rotation = Quaternion.identity; // current applied host rotation (upright, facing the head)
    private bool _placed;                            // false until Place() snaps the first in-view pose
    private int _facedPoseVersion = -1;              // RigPoseVersion the pose was last snapped at
    private float _offGazeSince = -1f;
    private bool _easing;
    private bool _lastVisible;
    private bool _dropLogged;                        // one-shot per reveal: log the applied downward gaze bias (item 3)

    // Host-rect pin (test #23): largest fitted width seen this reveal + the time it
    // last grew; the fit freezes once it has held at that max for FitPinSettleSeconds.
    private float _fitMaxWidth = -1f;
    private float _fitAtMaxSince;
    private float _fitFirstMeasured = -1f; // unscaled time the fit first measured this reveal (hard-timeout base)
    private bool _fitPinned;

    // Placement density cached once the fit pins (item 8): after the host rect is frozen
    // this holds metersPerPx so a later stray re-measure can never rescale/move the panel.
    private float _placedMetersPerPx = -1f;

    public string Name => "EnemyReveal";

    public void Tick()
    {
        // Scene unload killed the target — the framework pruned the host already.
        if (_panel != null && !_panel.IsAlive)
            _panel = null;

        InitiativeTrack track = InitiativeTrack.Instance;
        bool visible = WorldUIConfig.EnemyReveal.Value && WorldUIConfig.ConversionActive
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
                // Flatten2D (test #23): the monster-card subtree carries the same baked
                // 3D tilt as the combat log (recessed z / rotated RectTransforms shown
                // through the perspective UI camera) — neutralize it so the cards lie
                // flat on the world panel instead of sticking out.
                _panel = CanvasConversion.Convert(holder, Name, pokeable: false,
                    fitContent: true, flatten2D: true);
                if (_panel != null)
                {
                    _fitMaxWidth = -1f;
                    _fitAtMaxSince = 0f;
                    _fitFirstMeasured = -1f;
                    _fitPinned = false;
                    _placedMetersPerPx = -1f;
                    _placed = false; // Place() snaps the first in-view pose on the next tick
                    _easing = false;
                    _offGazeSince = -1f;
                    _dropLogged = false; // re-log the applied downward gaze bias for this reveal (item 3)
                }
            }
        }
        else if (!visible && _panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }

        if (_panel != null)
        {
            // The lock mirror in CanvasConversion.Tick may re-enable host raycasters
            // wholesale — keep this one dark so the vanilla EventSystem never hits it.
            if (_panel.HostRaycaster != null && _panel.HostRaycaster.enabled)
                _panel.HostRaycaster.enabled = false;
            PinWhenSettled();
            Place();
        }
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
    /// Lazy follow into view (test #27), mirroring FlatScreen.FollowHead but easing
    /// POSITION as well as yaw: the panel is planted in the forward view focus and left
    /// world-stable, then glides back to re-centre in front of the head only when the
    /// player has clearly turned or moved. The comfortable in-view target rides the ACTUAL
    /// gaze — a reading distance straight ahead at the current head pitch, dropped slightly
    /// below the gaze line, upright and facing the head (facing reused from
    /// <see cref="PanelPlacement.Spawn"/>) — sized in FIXED game-world units at the diorama
    /// reference scale so it still zooms WITH the board under world-grab.
    ///
    /// SNAP (no ease) at spawn and on rig rebuild/recenter — the RigPoseVersion derive
    /// events every panel uses — so a deliberate recentre re-places at once. Otherwise
    /// re-centre only after the panel has drifted &gt; <see cref="FollowDeadzoneDeg"/> off
    /// the gaze for <see cref="FollowDwellSeconds"/> (dead-zoned against quick glances /
    /// tremor), then Lerp/Slerp toward the fresh target until it has re-converged within
    /// <see cref="FollowSettledDeg"/>. CRUCIAL for jitter: while NOT easing the stored
    /// <see cref="_position"/>/<see cref="_rotation"/> are applied verbatim — the target
    /// is never recomputed per frame, so nothing fights the ease and the panel sits still.
    /// </summary>
    private void UpdateFollow(Camera head, float scale)
    {
        // Comfortable in-view target (settings-panel / ModalFallback.PlaceAtHmd "spawn in
        // view" pattern). Reuse PanelPlacement.Spawn ONLY for the UPRIGHT facing (its
        // Facing: panel front toward the head, +Z away — the convention every surface here
        // shares); its horizon-flattened position is discarded.
        PanelPlacement.Spawn(head, scale, out _, out Quaternion desiredRot);

        // Item 3 (user #4, RECURRING) rework: the HEIGHT fix. Place the panel along the
        // ACTUAL gaze — pitch included, like ModalFallback.PlaceAtHmd floats a window in
        // front of the HMD — at a reading distance, then drop it slightly below the gaze
        // line. Riding the real gaze (not the horizon, not the high-mounted board) puts it
        // in the forward field of view at ANY head pitch, so a player looking down at the
        // diorama reads it in place and never has to look up. Sized at the reference scale
        // so the distance is world-fixed and the panel zooms with the board.
        Vector3 gaze = head.transform.forward;
        Vector3 desiredPos = head.transform.position + gaze * (RevealReadingDistance * scale)
                             - Vector3.up * (RevealViewDrop * scale);
        if (!_dropLogged)
        {
            _dropLogged = true;
            VRLog.Info("WorldUI", $"ENEMY REVEAL head/view-anchored to y={desiredPos.y:F3} m " +
                                  $"(along the gaze at {RevealReadingDistance:F2} m, dropped {RevealViewDrop:F2} m " +
                                  $"below it, ×{scale:F2} scale) — spawns in the forward view, no looking up.");
        }

        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (!_placed || poseVersion != _facedPoseVersion)
        {
            _position = desiredPos;
            _rotation = desiredRot;
            _placed = true;
            _facedPoseVersion = poseVersion;
            _offGazeSince = -1f;
            _easing = false;
            return;
        }

        // Drift = the CURRENT panel centre's angle off the gaze. The reading drop already
        // sits it ~7° below the forward axis, which FollowDeadzoneDeg clears, so an
        // at-rest panel never self-triggers; a head turn / walk that pushes it past the
        // deadzone does.
        Vector3 toPanel = _position - head.transform.position;
        float off = toPanel.sqrMagnitude > 1e-6f
            ? Vector3.Angle(head.transform.forward, toPanel)
            : 0f;
        if (off > FollowDeadzoneDeg)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_easing && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _easing = true;
                VRLog.Info("WorldUI", $"ENEMY REVEAL lazy follow: {off:F0}° off the gaze for " +
                                      $">{FollowDwellSeconds:F1}s — gliding back into the view focus.");
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
            // Settled = converged on the fresh target (its direction AND facing), not on
            // the gaze axis — the target itself sits below the axis by the reading drop.
            Vector3 toDesired = desiredPos - head.transform.position;
            bool posSettled = toDesired.sqrMagnitude < 1e-6f
                || Vector3.Angle(_position - head.transform.position, toDesired) < FollowSettledDeg;
            if (posSettled && Quaternion.Angle(_rotation, desiredRot) < FollowSettledDeg)
            {
                _easing = false;
                _offGazeSince = -1f;
            }
        }
    }

    /// <summary>
    /// Anchor the reveal in the player's forward view focus (test #27), at the SHARED
    /// tray density (test #16) with the objectives' readability multiplier, width-capped.
    /// Position/facing come from the lazy head-follow (<see cref="UpdateFollow"/>); the
    /// host is sized in FIXED game-world units at the diorama's reference scale
    /// (<see cref="ReferenceScale"/>) so the panel zooms WITH the board under world-grab
    /// (test #23 (b)) — see that helper for why the old live WorldScale multiplier held
    /// it at a constant apparent size instead.
    /// </summary>
    private void Place()
    {
        if (_panel == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return; // no head yet — leave the panel where it last sat (world-stable)

        float scale = ReferenceScale();
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

        UpdateFollow(head, scale);

        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(_position, _rotation);
        host.localScale = Vector3.one * (metersPerPx * scale);
    }

    /// <summary>
    /// Diorama reference scale (game units per real meter, fixed at rig build). The
    /// board sits at a fixed game-world size; world-grab zoom rescales the RIG, so a
    /// game-unit-fixed panel appears to scale with the board exactly as the board does.
    /// The old code multiplied position and size by the LIVE WorldScale, which cancels
    /// that viewing magnification — the reveal floated at a constant apparent size
    /// while the diorama scaled beneath it (test #23 (b)). Anchoring to the constant
    /// base scale keeps the default look (live == base at the default zoom) while
    /// letting zoom move the panel with the board. Falls back to the live scale (1
    /// outside a scenario) when no rig has resolved a base scale yet.
    /// </summary>
    private static float ReferenceScale()
    {
        float baseScale = Rig.VRRigDriver.BaseWorldScale;
        return baseScale > 1e-4f ? baseScale : PanelLayout.WorldScale;
    }

    public void Shutdown()
    {
        if (_panel != null)
        {
            CanvasConversion.Release(_panel);
            _panel = null;
        }
        _lastVisible = false;
    }
}
