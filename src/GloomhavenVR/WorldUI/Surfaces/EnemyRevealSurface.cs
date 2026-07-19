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
/// or moved, at a fixed comfortable REAL distance/size in the player's forward view —
/// COMPLETELY INDEPENDENT of the control board (user #2): its world pose is driven ONLY by
/// the head pose and the player-frame scale (<see cref="HeadFrameScale"/>), so moving,
/// repositioning or zooming the board / tray / initiative-track dock never shifts it. At the
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

    // NOTE (user #5): the earlier lazy head-follow (deadzone/dwell/ease constants) is
    // GONE. The reveal now plants ONCE in the forward view and holds an absolute world
    // pose — no glide, no chasing the head — so handling the control board can never make
    // it drift. See PlantPose().

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

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    // Stored pose is now ABSOLUTE WORLD space, planted ONCE from the head's world pose and
    // then held verbatim (user #5, recurring). The earlier rig-local + lazy-follow builds
    // were mathematically board-invariant for the pose itself, but the head-follow GLIDE
    // reacted to every real head movement — and the player physically leans/turns while
    // handling the control board, so the reveal drifted "depending on how I rotate the
    // board." Holding a fixed world pose removes ALL of that: the board / tray / world-grab
    // move the RIG, never this stored world pose, and there is no follow to chase the head.
    private Vector3 _position;                        // ABSOLUTE world host position (planted once, held)
    private Quaternion _rotation = Quaternion.identity; // ABSOLUTE world host rotation (upright, facing the head at plant)
    private bool _placed;                            // false until PlantPose() snaps the first in-view world pose
    private int _facedPoseVersion = -1;              // RigPoseVersion the pose was last planted at (re-plant on recenter)
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
                    _placed = false; // PlantPose() snaps the first in-view world pose on the next tick
                    _dropLogged = false; // re-log the applied plant pose for this reveal (item 3)
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
    /// Plant the reveal ONCE in the player's forward view from the head's ABSOLUTE WORLD
    /// pose, then hold that world pose verbatim (user #5, recurring "board coupling").
    ///
    /// Why the switch away from the earlier rig-local + lazy-follow build: that build was
    /// mathematically board-invariant for the pose ITSELF (the head is a child of the rig
    /// root that WorldGrab moves, so a rig-local pose re-projected through the live rig
    /// stays glued to the physical head through any grab). The residual "moves depending on
    /// how I rotate the control board" was the FOLLOW, not the frame: the lazy head-follow
    /// re-centred whenever the head drifted &gt;22° off the panel, and the player physically
    /// leans and turns while grabbing / rotating the control board — so the reveal glided
    /// around in lock-step with board handling (the logs show the follow firing 24–35° right
    /// as the tray yaw is dragged). Killing the follow and pinning an ABSOLUTE world pose
    /// removes every path: the board / tray / world-grab move the RIG, never this stored
    /// world pose, and there is no follow to chase the head. The panel spawns in view and
    /// then sits perfectly still until dismissed.
    ///
    /// Recomputed ONLY when not yet placed or when the rig was rebuilt / recentred
    /// (RigPoseVersion changed) — a deliberate recentre teleports the whole rig, so the
    /// reveal re-plants in front of the head there; a plain world-grab does NOT bump
    /// RigPoseVersion, so it can never move the reveal.
    /// </summary>
    private void PlantPose(Camera head, Transform? rig)
    {
        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (_placed && poseVersion == _facedPoseVersion)
            return; // already planted — hold the RIG-LOCAL pose, re-projected each frame

        // RIG-LOCAL plant (user #5 — the CORRECT decoupling). Read the head's pose RELATIVE to
        // the rig root, NOT world space. The HeadCamera is a CHILD of RigRoot, and WorldGrab
        // moves/rotates/scales RigRoot about the grab pivot — so a WORLD-fixed pose SWINGS out
        // of view when the player world-grabs to reposition the board (the residual "reacts to
        // board movement": world-grab rotates the view, a world-fixed reveal stays put and
        // slides off). Storing rig-LOCAL and re-projecting through the LIVE rig each frame
        // (Place) keeps the reveal glued to the PHYSICAL head through any grab — world-grab
        // moves head AND reveal together (both are rig children), so it stays in front of the
        // face and never appears to move with the board. No follow, so it also never chases a
        // head movement (user #5: "nothing to do with head movements").
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

        // Upright billboard facing the head (yaw only), +Z toward the viewer — built purely
        // from the rig-local gaze heading, never any board / tray / rig-root term.
        Vector3 awayL = gazeL;
        awayL.y = 0f;
        awayL = awayL.sqrMagnitude > 1e-4f ? awayL.normalized : Vector3.forward;
        _rotation = Quaternion.LookRotation(awayL, Vector3.up);

        // Along the ACTUAL gaze (pitch included) at a reading distance, dropped slightly below
        // the gaze line (item 3). RAW rig-local metres: Place() projects through the rig (whose
        // lossyScale is the diorama scale) so it lands at a FIXED comfortable REAL distance.
        _position = headPosL + gazeL * RevealReadingDistance - Vector3.up * RevealViewDrop;

        _placed = true;
        _facedPoseVersion = poseVersion;

        if (!_dropLogged)
        {
            _dropLogged = true;
            float worldY = rig != null ? rig.TransformPoint(_position).y : _position.y;
            VRLog.Info("WorldUI", $"ENEMY REVEAL planted (RIG-LOCAL, plant-once) world-y={worldY:F3} m " +
                                  $"(gaze reading distance {RevealReadingDistance:F2} m, dropped {RevealViewDrop:F2} m) — " +
                                  "glued to the physical head via the live rig, NO follow: world-grab keeps it in front " +
                                  "of the face (never swings with the board), and it never chases head movement.");
        }
    }

    /// <summary>
    /// Anchor the reveal in the player's forward view focus, at the SHARED tray density
    /// (test #16) with the objectives' readability multiplier, width-capped. Position/facing
    /// are PLANTED ONCE by <see cref="PlantPose"/> and then held as an absolute world pose;
    /// only the host SIZE tracks the LIVE head-frame scale (<see cref="HeadFrameScale"/>) so
    /// the content reads at a fixed apparent size. COMPLETELY INDEPENDENT of the control
    /// board (user #5): the board / tray / world-grab move the rig, never this world pose.
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

        // Plant once (rig-local) in the forward view, then re-project through the LIVE rig each
        // frame (user #5). World-grab moves the rig ROOT and the head-child together, so the
        // reveal rides the physical head and never swings with the board; no follow, so it
        // never chases a head movement either.
        Transform? rig = Rig.VRRigDriver.RigRoot;
        PlantPose(head, rig);

        Vector3 worldPos = rig != null ? rig.TransformPoint(_position) : _position;
        Quaternion worldRot = rig != null ? rig.rotation * _rotation : _rotation;

        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(worldPos, worldRot);
        host.localScale = Vector3.one * (metersPerPx * scale);

        // Decisive diagnostic (throttled ~1/s while visible): the reveal host WORLD pose vs the
        // rig's world yaw. If a future log still shows "moves with the board", this proves
        // whether the host world position is actually changing and whether it tracks the rig
        // yaw (world-grab) or something else. Cheap; remove once the coupling is confirmed gone.
        float now = Time.unscaledTime;
        if (now - _lastDiagTime >= 1f)
        {
            _lastDiagTime = now;
            float rigYaw = rig != null ? rig.eulerAngles.y : 0f;
            float rigScale = rig != null ? rig.lossyScale.x : 1f;
            VRLog.Info("WorldUI", $"ENEMY REVEAL diag: host world pos={worldPos} yaw={worldRot.eulerAngles.y:F1}° " +
                                  $"| rig yaw={rigYaw:F1}° rigScale={rigScale:F1} — rig-local plant held.");
        }
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
    /// </summary>
    private static float HeadFrameScale()
    {
        return PanelLayout.WorldScale;
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
