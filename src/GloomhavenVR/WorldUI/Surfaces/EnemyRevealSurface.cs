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
/// above the board center, upright, yaw lazily easing toward the head, scaling with the
/// diorama, at the shared tray density; on hide it is released back to its exact 2D home
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
    /// <summary>Panel center height above the board-center anchor, real meters.</summary>
    private const float HeightMeters = 0.55f;

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

    // Lazy follow (test #23 (c)) — FlatScreen.FollowHead's feel: re-face only past this
    // yaw error, held for the dwell (ignore quick glances / jitter), then ease in until
    // settled. FollowEaseRate is the Slerp rate (matches FlatScreen's 3/s glide).
    private const float FollowDeadzoneDeg = 45f;
    private const float FollowDwellSeconds = 1f;
    private const float FollowSettledDeg = 5f;
    private const float FollowEaseRate = 3f;

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    private Quaternion _yaw = Quaternion.identity;   // current applied yaw (eased toward the head)
    private bool _yawInitialized;
    private int _facedPoseVersion = -1;              // RigPoseVersion the yaw was last snapped at
    private float _offGazeSince = -1f;
    private bool _easing;
    private bool _lastVisible;

    // Host-rect pin (test #23): largest fitted width seen this reveal + the time it
    // last grew; the fit freezes once it has held at that max for FitPinSettleSeconds.
    private float _fitMaxWidth = -1f;
    private float _fitAtMaxSince;
    private bool _fitPinned;

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
                ? $"ENEMY REVEAL shown: {DescribeCards(track)} — floating over the board center."
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
                    _fitPinned = false;
                    _yawInitialized = false; // Place() snaps the yaw on the first tick
                    _easing = false;
                    _offGazeSince = -1f;
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
        float width = _panel.HostRect.rect.width;
        if (width > _fitMaxWidth + 0.5f)
        {
            _fitMaxWidth = width;
            _fitAtMaxSince = Time.unscaledTime; // grew — restart the settle dwell
        }
        if (_panel.FitMeasuredOnce && _fitMaxWidth > 1f && width >= _fitMaxWidth - 0.5f
            && Time.unscaledTime - _fitAtMaxSince >= FitPinSettleSeconds)
        {
            _panel.FitEnabled = false;
            _fitPinned = true;
            VRLog.Info("WorldUI", $"ENEMY REVEAL host rect pinned at {width:F0} px " +
                                  "(settled full layout) — content re-fit disabled to stop thrashing.");
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
    /// Lazy follow into view (test #23 (c)), mirroring FlatScreen.FollowHead: the panel
    /// stays over the board center, but its yaw eases to face the player instead of
    /// being stranded edge-on when the player walks around the table or snap-turns.
    /// SNAP (no ease) at spawn and on rig rebuild/recenter — the RigPoseVersion derive
    /// events the world-anchored panels use, so a deliberate recentre re-faces at once.
    /// Otherwise re-face only after the yaw has been &gt; <see cref="FollowDeadzoneDeg"/>
    /// off the head for <see cref="FollowDwellSeconds"/> (dead-zoned against quick
    /// glances / jitter), then Slerp toward it until it settles within
    /// <see cref="FollowSettledDeg"/>.
    /// </summary>
    private void UpdateFollowYaw(Vector3 center, Quaternion seatYaw)
    {
        Quaternion desired = DesiredYaw(center, seatYaw);

        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (!_yawInitialized || poseVersion != _facedPoseVersion)
        {
            _yaw = desired;
            _yawInitialized = true;
            _facedPoseVersion = poseVersion;
            _offGazeSince = -1f;
            _easing = false;
            return;
        }

        float off = Quaternion.Angle(_yaw, desired);
        if (off > FollowDeadzoneDeg)
        {
            if (_offGazeSince < 0f)
                _offGazeSince = Time.unscaledTime;
            if (!_easing && Time.unscaledTime - _offGazeSince >= FollowDwellSeconds)
            {
                _easing = true;
                VRLog.Info("WorldUI", $"ENEMY REVEAL lazy follow: {off:F0}° off the head for " +
                                      $">{FollowDwellSeconds:F0}s — easing to face the player.");
            }
        }
        else
        {
            _offGazeSince = -1f;
        }

        if (_easing)
        {
            _yaw = Quaternion.Slerp(_yaw, desired, Time.deltaTime * FollowEaseRate);
            if (Quaternion.Angle(_yaw, desired) < FollowSettledDeg)
            {
                _easing = false;
                _offGazeSince = -1f;
            }
        }
    }

    /// <summary>
    /// Upright yaw that points the panel's uGUI front at the player from
    /// <paramref name="center"/> (canvas fronts render along -forward, so +Z points
    /// AWAY from the head). Falls back to the seat yaw when the head is unavailable or
    /// the head sits directly under the panel.
    /// </summary>
    private static Quaternion DesiredYaw(Vector3 center, Quaternion seatYaw)
    {
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return seatYaw;
        Vector3 facing = center - head.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f)
            return seatYaw;
        return Quaternion.LookRotation(facing.normalized, Vector3.up);
    }

    /// <summary>
    /// Above the board center (the orbit focus the panels anchor to), at the SHARED
    /// tray density (test #16) with the objectives' readability multiplier,
    /// width-capped. Sized and offset in FIXED game-world units at the diorama's
    /// reference scale (<see cref="ReferenceScale"/>), so the panel zooms WITH the
    /// board under world-grab (test #23 (b)) — see that helper for why the old live
    /// WorldScale multiplier held it at a constant apparent size instead.
    /// </summary>
    private void Place()
    {
        if (_panel == null || !PanelLayout.TryGetAnchor(out Vector3 anchor, out Quaternion seatYaw))
            return;

        float scale = ReferenceScale();
        float metersPerPx = 1f / (PlayTray.TrayPixelsPerMeter * DensityScale);
        Rect rect = _panel.HostRect.rect; // content-fitted by CanvasConversion.TickFit
        if (rect.width > 1f)
            metersPerPx = Mathf.Min(metersPerPx, MaxWidthMeters / rect.width);

        Vector3 center = anchor + Vector3.up * (HeightMeters * scale);
        UpdateFollowYaw(center, seatYaw);

        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(center, _yaw);
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
