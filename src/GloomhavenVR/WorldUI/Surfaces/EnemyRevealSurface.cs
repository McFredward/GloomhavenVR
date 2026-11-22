using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Cards;
using GloomhavenVR.Core;
using ScenarioRuleLibrary;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

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
/// in the player's forward view at a comfortable reading distance. The initial
/// placement and the next gaze-follow target reserve the complete fitted paper
/// against the viewer's own and visible peer boards, within the headset view.
/// The pose stays in the tracking frame, so board movement cannot steer a panel
/// that is already open. At the
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
    //     not from an absolute world pose — see PanelLayout.WorldScale and INVARIANTS-WorldUI.md
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
    // PanelLayout.WorldScale) so they are a fixed comfortable distance in the player's OWN view,
    // NOT tied to the board's diorama size — user #2: the reveal must be COMPLETELY
    // INDEPENDENT of the control board, always in the forward view regardless of the board.
    // Tune here.
    private const float RevealReadingDistance = 1.3f;
    private const float RevealViewDrop = 0.15f;

    // Give the opening a footprint before the first staggered fit arrives.
    // Later fits replay against the same opening eye and boards rather than
    // following whatever the board or headset did during the card animation.
    private static readonly Vector2 NominalHalfSize = new(0.30f, 0.20f);
    private const float FollowPositionTolerance = 0.02f;

    private static readonly StringBuilder NameScratch = new(128);

    private ConvertedPanel? _panel;
    // Stored pose is RIG-LOCAL (head pose relative to RigRoot), re-projected through the LIVE
    // rig each frame in Place(). Rig-local is grab-invariant relative to the physical head, so
    // the follow below reacts only to real head movement, never to board/tray/world-grab.
    // (An ABSOLUTE-WORLD stored pose was tried and is wrong: a world-grab rotates the rig about
    // the grab pivot, which swings a world-fixed reveal out of view.)
    private Vector3 _position;                        // rig-local host position (head-relative) — follow in ALL axes
    private Vector3 _goal;
    private readonly EnemyRevealPlacement _openingLayout = new();
    private readonly List<Transform> _boardRoots = new(4);
    private Matrix4x4 _openingWorldToRig;
    private Vector3 _openingHeadLocal, _openingGazeLocal;
    private Vector2 _openingHalfSize;
    private bool _openingMeasured;
    private string _clearance = "";
    private bool _placed;
    private int _facedPoseVersion = -1;
    private float _offGazeSince = -1f;
    private bool _easing;
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

    // ---- "Hagels Info ist leer" — THE EMPTY ALLY CARD (user, hardware, 2026-08-24) -------------
    // HIS REPORT, VERBATIM: "In einem Szenario gibt es einen verbündeten NPC namens 'Hagel', ihre
    // Info was sie diese Runde macht ist leer. Siehe empty_info.jpg. Gewährleiste dass dort immer
    // das zu sehen ist was im flat Spiel auch zu sehen wäre. Auch beim Mouseover in der
    // Initiativreihenfolge wird es leer angezeigt. Oder kommt das wirklich vom Spiel? Untersuche
    // das."
    //
    // HIS LAST QUESTION IS THE WHOLE ROUND, and the mod cannot answer it from its own side — this
    // project has a standing lesson that an instrument must not assert a mechanism it cannot
    // observe. What CAN be observed is the game's own data next to the mod's own picture, in one
    // line, so the answer falls out either way. <see cref="AuditCard"/> prints, for the actor whose
    // info is shown:
    //   * the ACTOR's real type — an ALLY (CActor.EType.Ally) is NOT a monster, and the whole info
    //     card is nevertheless built by the monster path (MonsterBaseUI), which is why the ally
    //     even has a "role" caption ("VERBÜNDETER", MonsterBaseUI.SetBaseStats sets roleText to
    //     GUI_ALLY for EType.Ally);
    //   * WHAT THE GAME'S OWN DATA HOLDS: the round ability card and, decisively, how many
    //     renderable rows its CAction carries (Abilities + Augmentations + Infusions). That count
    //     IS the flat-game answer: CreateLayout's monster-card constructor (CreateLayout.cs:221)
    //     builds the card body from exactly those three lists and from nothing else, and the flat
    //     game runs the identical code — so ZERO there means the flat game draws an empty body too;
    //   * THE PICTURE, not the state: how many Graphics inside the card, and inside the ability
    //     body (MonsterBaseUI.contentHolder) specifically, are actually DRAWING — effective alpha
    //     color.a x CanvasRenderer.GetInheritedAlpha() >= 0.05, the house test, because a graphic
    //     under a CanvasGroup at alpha 0 has enabled/activeInHierarchy true, color.a 1 and cull
    //     false and paints nothing ([[inherited-alpha-is-not-the-group]]).
    //
    // THE AUDIT CAN FAIL, which is the point: "rows in the data, zero graphics drawing" convicts
    // the mod; "zero rows in the data" acquits it and ends the round with a report.
    //
    // TWO SYMPTOMS, ONE WIDGET. He reports the floating reveal AND the initiative-track mouseover.
    // Both are the SAME MonsterBaseUI instance — InitiativeTrackEnemyBehaviour.monsterBaseUI —
    // reached by two entry points: the reveal animates it in (AnimateCardHighlight ->
    // MonsterBaseUI.AnimateAppearance) and the hover toggles it on
    // (OnAvatarHighlight -> TogglePreview, InitiativeTrackEnemyBehaviour.cs:98-118). Its body is
    // the same contentHolder, filled once by MonsterBaseUI.MakeMerging. So they share a cause BY
    // CONSTRUCTION unless the audit says otherwise — which is why the hover is audited too, from
    // the same method, and the context tag names which entry point produced the reading.
    // (Not the deliberate hover block: Patches/InitiativeHoverCardBlock no-ops
    // CardsHandManager.Preview, the PLAYER-entry path only; enemies/allies never go through it.)
    //
    // TIMING IS ONE OF THE CANDIDATES, so the reveal is read TWICE: once at the flip (content may
    // still be arriving — the cards animate in staggered and MakeMerging is a coroutine) and once
    // when the layout has settled. Two identical empty readings rule the late-content theory out;
    // an empty-then-full pair proves it.
    private bool _auditedAtFlip;      // one-shot: the early (flip-time) reading for this reveal
    private bool _auditedSettled;     // one-shot: the settled reading for this reveal
    private float _revealShownAt = -1f;
    private int _hoverAuditedId;      // MonsterBaseUI instance id last audited on the hover path
    private float _nextHoverScan;     // 4 Hz throttle for the hover watch (no reveal up)

    /// <summary>Effective-alpha floor for "this graphic is actually drawing" — the house test
    /// (<see cref="PanelInkBounds"/>, CanvasConversion's fit): color.a x inherited alpha.
    /// <c>CanvasConversion.FitMinAlpha</c> itself rather than a fifth copy of 0.05 (ModBuild 439,
    /// survey row R27). The value is unchanged, and this instrument's printed floor
    /// (the audit line's <c>DrawAlphaFloor.ToString("F2")</c>) now cannot disagree with the fit that
    /// decided the same graphic.</summary>
    private const float DrawAlphaFloor = CanvasConversion.FitMinAlpha;

    /// <summary>Seconds after the show flip at which the settled reading is taken if the content
    /// fit never pinned (the pin itself hard-times-out at 2.5 s, so this only covers a reveal that
    /// was dismissed or rebuilt before that).</summary>
    private const float SettledAuditSeconds = 3f;

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
            if (visible)
            {
                // Re-arm both readings for this reveal (see the empty-card field block).
                _auditedAtFlip = false;
                _auditedSettled = false;
                _revealShownAt = Time.unscaledTime;
            }
            // Deliberately NO audit on the hide flip: the Continue button deactivates every card
            // (InitiativeTrack.cs:519-524) before this edge is seen, so such a line could only ever
            // read "nothing shown" and would be one guaranteed-empty log per reveal.
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

            // THE EMPTY-CARD AUDIT (see the field block). Read twice: at the flip, and again once
            // the layout has settled — an empty-then-full pair would prove the content merely
            // arrives late, two empty readings rule that candidate out.
            if (!_auditedAtFlip)
            {
                _auditedAtFlip = true;
                AuditRevealedCards(track, "reveal, at flip");
            }
            else if (!_auditedSettled
                     && (_fitPinned || Time.unscaledTime - _revealShownAt >= SettledAuditSeconds))
            {
                _auditedSettled = true;
                AuditRevealedCards(track, _fitPinned ? "reveal, layout settled" : "reveal, +3s");
            }
        }
        else
        {
            // NO reveal up: watch the OTHER entry point to the same widget — the initiative-track
            // MOUSEOVER / held-figure peek (OnAvatarHighlight -> MonsterBaseUI.TogglePreview), the
            // second half of his report. Audited once per popup so the two symptoms can be compared
            // line to line instead of assumed to share a cause.
            WatchHoverPreview(track);
        }
    }

    /// <summary>
    /// One-shot audit of the initiative-track HOVER popup (the second symptom). Runs only while no
    /// reveal is floated; the popup is the same <c>MonsterBaseUI</c> the reveal shows, toggled on by
    /// <c>InitiativeTrackEnemyBehaviour.OnAvatarHighlight</c> (pointer-enter on the track avatar, or
    /// the mod's own <see cref="FigureIntentPeek"/> when a mini is held). Throttled to 4 Hz and
    /// keyed on the popup's instance id, so a steady hover logs exactly once.
    /// </summary>
    private void WatchHoverPreview(InitiativeTrack track)
    {
        if (track == null || track.enemiesUI == null)
        {
            _hoverAuditedId = 0;
            return;
        }
        if (Time.unscaledTime < _nextHoverScan)
            return;
        _nextHoverScan = Time.unscaledTime + 0.25f;

        for (int i = 0; i < track.enemiesUI.Count; i++)
        {
            InitiativeTrackEnemyBehaviour enemy = track.enemiesUI[i];
            if (enemy == null || enemy.monsterBaseUI == null
                || !enemy.monsterBaseUI.gameObject.activeSelf)
                continue;
            int id = enemy.monsterBaseUI.GetInstanceID();
            if (id == _hoverAuditedId)
                return; // same popup still up — already read
            _hoverAuditedId = id;
            AuditCard(enemy, "initiative-track hover/peek");
            return;
        }
        _hoverAuditedId = 0; // nothing previewing — re-arm for the next hover
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

    /// <summary>Audit every card the reveal currently shows (see the field block for the why).</summary>
    private static void AuditRevealedCards(InitiativeTrack track, string context)
    {
        if (track == null || track.enemiesUI == null)
            return;
        int audited = 0;
        for (int i = 0; i < track.enemiesUI.Count; i++)
        {
            InitiativeTrackEnemyBehaviour enemy = track.enemiesUI[i];
            if (enemy == null || enemy.monsterBaseUI == null
                || !enemy.monsterBaseUI.gameObject.activeSelf)
                continue;
            AuditCard(enemy, context);
            audited++;
        }
        if (audited == 0)
            VRLog.Info("WorldUI", $"ENEMY INFO AUDIT [{context}]: no active MonsterBaseUI on the " +
                                  "initiative track — nothing was being shown at this instant.");
    }

    /// <summary>
    /// THE FALSIFIER for "Hagels Info ist leer" (field block above): the game's OWN data for this
    /// actor's round action next to the mod's OWN reading of what is drawn, in one line, with a
    /// verdict that can convict the mod as easily as acquit it.
    ///
    /// Reads nothing but state the game already owns (no writes, no game calls) and is fully
    /// defensive — every hop is null-checked, because it runs inside the reveal's per-frame tick.
    /// </summary>
    private static void AuditCard(InitiativeTrackEnemyBehaviour enemy, string context)
    {
        MonsterBaseUI ui = enemy.monsterBaseUI;
        CActor? actor = enemy.Actor;
        var enemyActor = actor as CEnemyActor;
        CMonsterAbilityCard? card = enemy.monsterAbilityCard;

        var sb = new StringBuilder(768);
        sb.Append("ENEMY INFO AUDIT [").Append(context).Append("] '")
          .Append(actor != null ? actor.GetPrefabName() : enemy.name).Append("': ");

        // --- WHAT THIS ACTOR IS. An ALLY is not a monster, yet the whole info card is the monster
        // widget: MonsterBaseUI.SetBaseStats switches roleText to GUI_ALLY for EType.Ally, which is
        // the "VERBÜNDETER" caption in his screenshot. Both types are printed because a mind-
        // controlled or converted actor's live Type differs from the one it was born with.
        sb.Append("ACTOR type=").Append(actor != null ? actor.Type.ToString() : "<null>")
          .Append(" (original ").Append(actor != null ? actor.OriginalType.ToString() : "<null>").Append(')');
        if (enemyActor != null && enemyActor.MonsterClass != null)
            sb.Append(", monster class '").Append(enemyActor.MonsterClass.ID)
              .Append("' locKey '").Append(enemyActor.MonsterClass.LocKey)
              .Append("', ability DECK holds ")
              .Append(enemyActor.MonsterClass.AbilityCardsPool != null
                  ? enemyActor.MonsterClass.AbilityCardsPool.Count : -1)
              .Append(" card(s)");
        else
            sb.Append(", NOT a CEnemyActor — it has no monster class and no ability deck at all");

        // --- WHAT THE GAME'S OWN DATA HOLDS. This is the answer to "kommt das wirklich vom Spiel?":
        // CreateLayout's monster-card constructor builds the card body out of CardAction.Abilities,
        // .Augmentations and .Infusions and out of NOTHING else, and the flat game runs that same
        // constructor — so this count is what the flat game would have to draw.
        int dataRows = -1;
        if (card == null)
        {
            sb.Append(" | GAME DATA: this actor has NO round ability card at all " +
                      "(MonsterClass.RoundAbilityCard is null) — the game has nothing to draw here.");
            dataRows = 0;
        }
        else
        {
            CAction? action = card.Action;
            int abilities = action != null && action.Abilities != null ? action.Abilities.Count : 0;
            int augs = action != null && action.Augmentations != null ? action.Augmentations.Count : 0;
            int infusions = action != null && action.Infusions != null ? action.Infusions.Count : 0;
            dataRows = abilities + augs + infusions;
            sb.Append(" | GAME DATA: round card '").Append(card.Name).Append("' (ID ").Append(card.ID)
              .Append(", initiative ").Append(card.Initiative)
              .Append(", shuffle ").Append(card.Shuffle)
              .Append("); its CAction holds ").Append(abilities).Append(" ability/ies, ")
              .Append(augs).Append(" augmentation(s), ").Append(infusions).Append(" infusion(s) [");
            if (action != null && action.Abilities != null)
            {
                for (int i = 0; i < action.Abilities.Count && i < 12; i++)
                {
                    CAbility ability = action.Abilities[i];
                    if (i > 0)
                        sb.Append(", ");
                    if (ability == null)
                    {
                        sb.Append("<null>");
                        continue;
                    }
                    sb.Append(ability.AbilityType.ToString());
                    if (!string.IsNullOrEmpty(ability.Name))
                        sb.Append('\'').Append(ability.Name).Append('\'');
                    if (ability.OnDeath)
                        sb.Append("(onDeath)");
                }
            }
            sb.Append(']');
        }

        // --- WHAT IS ACTUALLY DRAWN. Never the state alone: this project has burned builds on
        // defects where every state reading was clean and the image was still wrong. "Leer" becomes
        // a number here — graphics whose EFFECTIVE alpha clears the floor, whole card and ability
        // body separately, because the body is the part he says is empty.
        CountInk(ui != null ? ui.transform : null, out int cardTotal, out int cardDrawn);
        RectTransform? body = ui != null ? ui.contentHolder : null;
        CountInk(body, out int bodyTotal, out int bodyDrawn);
        sb.Append(" | PICTURE: the whole info card carries ").Append(cardTotal)
          .Append(" Graphic(s), ").Append(cardDrawn)
          .Append(" of them actually drawing (color.a x inherited alpha >= ")
          .Append(DrawAlphaFloor.ToString("F2")).Append("); the ABILITY BODY (contentHolder) ");
        if (body == null)
        {
            sb.Append("could not be resolved on the widget");
        }
        else
        {
            sb.Append("has ").Append(body.childCount).Append(" child object(s), ")
              .Append(bodyTotal).Append(" Graphic(s), ").Append(bodyDrawn).Append(" drawing")
              .Append(body.gameObject.activeInHierarchy ? "" : " [the body object is INACTIVE]")
              .Append(", rect ").Append(body.rect.width.ToString("F0")).Append('x')
              .Append(body.rect.height.ToString("F0")).Append(" px");
            AppendBodyText(body, sb);
        }

        // --- THE VERDICT, and it can go against the mod. ---
        sb.Append(" | VERDICT: ");
        if (dataRows <= 0)
            sb.Append("the GAME's own card data holds ZERO renderable rows, so there is nothing to " +
                      "show. CreateLayout (CreateLayout.cs:221) builds this body from those lists " +
                      "alone and the flat game runs the same code on the same data — the flat game " +
                      "shows the same empty area. NOT a VR defect; nothing for the mod to fix.");
        else if (bodyDrawn <= 0)
            sb.Append("THE MOD (or the generation timing) IS AT FAULT: the game holds ")
              .Append(dataRows).Append(" renderable row(s) for this card and ZERO graphics are " +
                      "drawing inside the body. Compare the flip vs settled readings — equal means " +
                      "the rows were never built or are at inherited alpha 0, different means the " +
                      "content merely arrived after the earlier reading.");
        else
            sb.Append("the body is POPULATED — ").Append(dataRows).Append(" row(s) in the data, ")
              .Append(bodyDrawn).Append(" graphic(s) drawing. If the player still reports it empty, " +
                      "the fault is legibility (scale/contrast/occlusion), not content.");

        VRLog.Info("WorldUI", sb.ToString());
    }

    /// <summary>
    /// Graphics under <paramref name="root"/> (inactive included in the total) and how many of them
    /// are really painting. The drawing test is the house one — <c>color.a x
    /// CanvasRenderer.GetInheritedAlpha()</c> against <see cref="DrawAlphaFloor"/>, never
    /// <c>color.a</c> alone: a graphic under a CanvasGroup at alpha 0 has <c>enabled</c> true,
    /// <c>activeInHierarchy</c> true, <c>color.a</c> 1 and <c>cull</c> false and draws nothing.
    /// </summary>
    private static void CountInk(Transform? root, out int total, out int drawing)
    {
        total = 0;
        drawing = 0;
        if (root == null)
            return;
        InkScratch.Clear();
        root.GetComponentsInChildren(includeInactive: true, InkScratch);
        for (int i = 0; i < InkScratch.Count; i++)
        {
            Graphic g = InkScratch[i];
            if (g == null)
                continue;
            total++;
            if (!g.isActiveAndEnabled)
                continue;
            CanvasRenderer cr = g.canvasRenderer;
            if (cr == null || cr.cull)
                continue;
            if (g.color.a * cr.GetInheritedAlpha() >= DrawAlphaFloor)
                drawing++;
        }
        InkScratch.Clear();
    }

    /// <summary>
    /// The first few non-empty ability-row strings actually in the body — the human-readable half of
    /// the picture reading ("Bewegen 3, Angriff 2" vs nothing at all). Empty strings are skipped so
    /// a row-shaped object with no text cannot masquerade as content.
    /// </summary>
    private static void AppendBodyText(RectTransform body, StringBuilder sb)
    {
        TextScratch.Clear();
        body.GetComponentsInChildren(includeInactive: true, TextScratch);
        int shown = 0;
        for (int i = 0; i < TextScratch.Count && shown < 6; i++)
        {
            TMP_Text t = TextScratch[i];
            if (t == null || string.IsNullOrEmpty(t.text) || t.text.Trim().Length == 0)
                continue;
            sb.Append(shown == 0 ? ", text: \"" : "\" | \"").Append(t.text.Replace('\n', ' '));
            shown++;
        }
        sb.Append(shown > 0 ? "\"" : ", NO non-empty text anywhere in the body");
        TextScratch.Clear();
    }

    private static readonly List<Graphic> InkScratch = new(64);
    private static readonly List<TMP_Text> TextScratch = new(32);

    /// <summary>
    /// Keep a standing pose in tracking space. Gather obstacles at opening or
    /// after a physical head movement settles beyond the follow deadzone.
    /// Content growth can correct that placement using the same opening snapshot.
    /// </summary>
    private void PlantPose(Camera head, Transform? rig, float scale, Vector2 halfSize, bool measured)
    {
        Transform eye = head.transform;
        Vector3 headLocal = rig != null ? rig.InverseTransformPoint(eye.position) : eye.position;
        Vector3 gazeLocal = rig != null ? rig.InverseTransformDirection(eye.forward) : eye.forward;
        float now = Time.unscaledTime;
        int poseVersion = Rig.VRRigDriver.RigPoseVersion;
        if (!_placed || poseVersion != _facedPoseVersion)
        {
            CaptureOpening(head, rig, scale, headLocal, gazeLocal);
            _position = LayoutGoal(halfSize, measured);
            _placed = true;
            _facedPoseVersion = poseVersion;
            _offGazeSince = -1f;
            _easing = false;
            if (!_dropLogged)
            {
                _dropLogged = true;
                Vector3 world = rig != null ? rig.TransformPoint(_position) : _position;
                VRLog.Info("WorldUI", $"ENEMY REVEAL spawned at world {world:F2} " +
                    $"({(world - eye.position).magnitude / scale:F2} m from the head, half-size {halfSize:F2} m) — {_clearance}.");
            }
            return;
        }

        if ((measured && !_openingMeasured) ||
            halfSize.x > _openingHalfSize.x * (1f + FitPinGrowthFraction) ||
            halfSize.y > _openingHalfSize.y * (1f + FitPinGrowthFraction))
        {
            _easing = !NearPosition(_position, LayoutGoal(halfSize, measured));
        }

        if (!_easing)
        {
            float off = Vector3.Angle(gazeLocal, _position - headLocal);
            bool headMoved = !NearPosition(headLocal, _openingHeadLocal)
                || Vector3.Angle(gazeLocal, _openingGazeLocal) > FollowSettledDeg;
            if (off <= FollowDeadzoneDeg || !headMoved)
                _offGazeSince = -1f;
            else if (_offGazeSince < 0f)
                _offGazeSince = now;
            else if (now - _offGazeSince >= FollowDwellSeconds)
            {
                CaptureOpening(head, rig, scale, headLocal, gazeLocal);
                _easing = !NearPosition(_position, LayoutGoal(halfSize, measured));
                _offGazeSince = -1f;
                if (_easing)
                    VRLog.Info("WorldUI", $"ENEMY REVEAL lazy follow: {off:F0}° off the gaze — gliding into view.");
            }
        }

        if (_easing)
        {
            float blend = 1f - Mathf.Exp(-FollowEaseRate * Time.unscaledDeltaTime);
            _position = Vector3.Lerp(_position, _goal, blend);
            if (NearPosition(_position, _goal))
            {
                _position = _goal;
                _easing = false;
                _offGazeSince = -1f;
            }
        }
    }

    private static bool NearPosition(Vector3 a, Vector3 b) =>
        (a - b).sqrMagnitude <= FollowPositionTolerance * FollowPositionTolerance;

    private void CaptureOpening(Camera head, Transform? rig, float scale, Vector3 headLocal, Vector3 gazeLocal)
    {
        _openingWorldToRig = rig != null ? rig.worldToLocalMatrix : Matrix4x4.identity;
        _openingHeadLocal = headLocal;
        _openingGazeLocal = gazeLocal;
        Vector2 view = EnemyRevealPlacement.DefaultViewHalfTangents;
        if (XRSettings.isDeviceActive)
        {
            try
            {
                view = EnemyRevealPlacement.StereoViewHalfTangents(
                    head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left),
                    head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right));
            }
            catch (System.Exception)
            {
                // The runtime may start or replace its eye targets during an opening.
                // The default projection bounds preserve the reveal and native Continue.
            }
        }
        _openingLayout.Begin(head.transform.position, head.transform.forward, scale, view);
        float margin = Mathf.Max(0f, WorldUIConfig.EnemyRevealBoardClearance != null
            ? WorldUIConfig.EnemyRevealBoardClearance.Value : Defaults.EnemyRevealBoardClearance);
        PlayTray? tray = PlayTray.Current;
        Transform? own = tray != null && tray.IsVisible ? tray.Root : null;
        if (own != null)
            AddBoard(own, margin, own: true);
        _boardRoots.Clear();
        Net.PeerBoardFade.CollectShownBoardRoots(_boardRoots);
        foreach (Transform board in _boardRoots)
        {
            if (board != null && board != own)
                AddBoard(board, margin, own: false);
        }
        _boardRoots.Clear();
    }

    private void AddBoard(Transform root, float margin, bool own) =>
        _openingLayout.AddBoard(PlayTray.MeasureBoardLocalBounds(root), root.localToWorldMatrix, margin, own);

    private Vector3 LayoutGoal(Vector2 halfSize, bool measured)
    {
        Vector3 target = _openingLayout.Find(RevealReadingDistance, RevealViewDrop, halfSize, out string clearance);
        _openingHalfSize = halfSize;
        _openingMeasured = measured;
        _goal = _openingWorldToRig.MultiplyPoint3x4(target);
        if (_clearance != clearance && VRLog.WantsDebug)
            VRLog.Debug("WorldUI", "MIRRORED SURFACE DEPTH: enemy reveal panel — " + clearance);
        _clearance = clearance;
        return _goal;
    }

    private void Place()
    {
        if (_panel == null)
            return;
        Camera? head = CanvasConversion.WorldCamera;
        if (head == null)
            return;

        float scale = PanelLayout.WorldScale;
        Rect rect = _panel.HostRect.rect;
        float metersPerPx = _placedMetersPerPx;
        if (metersPerPx <= 0f)
        {
            metersPerPx = 1f / (PlayTray.TrayPixelsPerMeter * DensityScale);
            if (rect.width > 1f)
                metersPerPx = Mathf.Min(metersPerPx, MaxWidthMeters / rect.width);
            if (_fitPinned)
                _placedMetersPerPx = metersPerPx;
        }
        bool measured = _panel.FitMeasuredOnce && rect.width > 1f && rect.height > 1f;
        Vector2 halfSize = measured ? rect.size * (0.5f * metersPerPx) : NominalHalfSize;
        Transform? rig = Rig.VRRigDriver.RigRoot;
        PlantPose(head, rig, scale, halfSize, measured);

        Vector3 worldPos = rig != null ? rig.TransformPoint(_position) : _position;
        Transform host = _panel.HostTransform;
        host.SetPositionAndRotation(worldPos, PanelPlacement.Facing(worldPos, head.transform.position));
        host.localScale = Vector3.one * (metersPerPx * scale);

        // Diagnostic samples the actual reparent, without another placement or board scan.
        float now = Time.unscaledTime;
        if (VRLog.WantsDebug && now - _lastDiagTime >= 1f)
        {
            _lastDiagTime = now;
            InitiativeTrack tr = InitiativeTrack.Instance;
            Transform? holder = tr != null ? tr.enemyCardsHolder : null;
            Transform? card = FirstActiveMonsterCard(tr);
            Transform? tray = PlayTray.Current != null ? PlayTray.Current.Root : null;
            VRLog.Debug("WorldUI", $"ENEMY REVEAL attribution: myHostY={worldPos.y:F2} | " +
                $"CARD widget worldY={card?.position.y:F2} underMyHost={card != null && IsDescendantOf(card, host)} | " +
                $"enemyCardsHolder.parent='{holder?.parent?.name}' onMyHost={holder != null && holder.parent == host} | " +
                $"TRAY Y={tray?.position.y:F2} yaw={tray?.eulerAngles.y:F0}° | " +
                $"headWorldY={head.transform.position.y:F2} rigScale={scale:F1} easing={_easing} | " +
                $"CLEARANCE {_clearance} " +
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
        _auditedAtFlip = false;
        _auditedSettled = false;
        _hoverAuditedId = 0;
    }
}
